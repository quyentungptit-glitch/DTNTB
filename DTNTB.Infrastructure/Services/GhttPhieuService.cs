using System.Data;
using System.Globalization;
using ClosedXML.Excel;
using Dapper;
using DTNTB.Core.Constants;
using DTNTB.Core.DTOs;
using DTNTB.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;

namespace DTNTB.Infrastructure.Services
{
    /// <summary>
    /// Giao phiếu tiếp cận thuê bao GHTT (Biểu 8: TB đã qua outbound nhưng chưa gia hạn online) và cập nhật kết quả.
    /// Chuyển từ BCBG (DAO_ghtt_phieu, GhttGiaoPhieu.aspx, GhttPhieuCuaToi.aspx), DÙNG CHUNG bảng GHTT_PHIEU,
    /// GHTT_PHIEU_LS, GHTT_DM_LYDO trên schema ConnectionString_NBH nên phiếu giao bên nào bên kia cũng thấy.
    ///
    /// Quy tắc giữ nguyên như BCBG:
    ///  - Nguồn = tháng được chọn, lần import mới nhất (ngày) của từng phân vùng; LOAITB_ID = 58, NHANVIEN_TH_ID > 0,
    ///    NGAYHEN_BH_TU IS NULL; đơn vị ghép (DONVI_ID, PHANVUNG_ID) sang V_DONVI với MA_DV LIKE '215.3%'.
    ///  - Phiếu giao cho đúng NV thực hiện, hạn 72 giờ; TB còn phiếu trong hạn không giao thêm; hết hạn chưa xử lý thì
    ///    phiếu cũ treo, được giao phiếu mới. Khóa bảng khi giao để 2 người bấm cùng lúc không giao trùng.
    ///
    /// Phân quyền theo cấu trúc DTNTB (thay level_role_dtntb):
    ///  - PHIEU_GIAO: giao phiếu; PHIEU_XU_LY: cập nhật kết quả (kể cả nhập hộ); EXPORT: xuất Excel.
    ///  - Phạm vi theo data_scope: TOAN_TINH tất cả; DIA_BAN các đơn vị thuộc địa bàn; DON_VI cùng 7 ký tự đầu mã đơn vị;
    ///    TO_QL NV thuộc tổ (v_nguoidung_diaban cùng toàn bộ ma_dv); NHAN_VIEN chỉ của mình (ma_nv).
    ///  - Mã đơn vị 215.7xxx được quy về 215.3xxx khi so sánh (như BCBG).
    /// </summary>
    public class GhttPhieuService : IGhttPhieuService
    {
        public const int HanGio = 72;
        private const int MaxChonMotLan = 3000;
        private const int MaxExportRows = 50000;

        private readonly string _connString;
        private readonly ICurrentUserService _currentUser;
        private readonly ILogger<GhttPhieuService> _logger;

        public GhttPhieuService(IConfiguration config, ICurrentUserService currentUser, ILogger<GhttPhieuService> logger)
        {
            _connString = config.GetConnectionString("ConnectionString_NBH")
                       ?? config.GetConnectionString("ConnectionString_NBH_NEW")
                       ?? string.Empty;
            _currentUser = currentUser;
            _logger = logger;
        }

        // ============================================================
        // DANH MỤC
        // ============================================================
        public async Task<List<int>> GetThangAsync()
        {
            await using var conn = new OracleConnection(_connString);
            var rows = await conn.QueryAsync<int>("SELECT DISTINCT sl_thang FROM ghtt_bieu_8 ORDER BY sl_thang DESC");
            return rows.ToList();
        }

        public async Task<List<GhttPhieuDonViDto>> GetDonViAsync()
        {
            var p = new DynamicParameters();
            var scope = DonViScopeClause(p, "d.ma_dv");
            await using var conn = new OracleConnection(_connString);
            var rows = await conn.QueryAsync<GhttPhieuDonViDto>($@"
                SELECT SUBSTR({Norm("d.ma_dv")}, 1, 7) AS MaDv, MAX(b.ten_dv) AS TenDv
                FROM (SELECT DISTINCT donvi_id, phanvung_id, ten_dv FROM ghtt_bieu_8) b
                JOIN v_donvi d ON d.donvi_id = b.donvi_id AND d.phanvung_id = b.phanvung_id
                WHERE d.ma_dv LIKE '215.3%' AND {scope}
                GROUP BY SUBSTR({Norm("d.ma_dv")}, 1, 7)
                ORDER BY 2", p);
            return rows.ToList();
        }

        public async Task<List<string>> GetGoiYLyDoAsync()
        {
            await using var conn = new OracleConnection(_connString);
            var rows = await conn.QueryAsync<string>("SELECT ten FROM ghtt_dm_lydo WHERE trang_thai = 1 ORDER BY thu_tu, id");
            return rows.ToList();
        }

        // ============================================================
        // DANH SÁCH TB ĐỂ GIAO
        // ============================================================

        /// <summary>CTE nguồn Biểu 8 (giống BCBG, bỏ lọc đơn vị ra ngoài để áp phạm vi DTNTB). Tham số :thang.</summary>
        private const string CteNguon = @"
            b2 AS (
                SELECT b.*, MAX(TRUNC(b.ngay_cn)) OVER (PARTITION BY b.phanvung_id) AS ngay_moi
                FROM ghtt_bieu_8 b WHERE b.sl_thang = :thang
            ),
            dv AS (
                SELECT donvi_id, phanvung_id, MAX(ma_dv) AS ma_dv
                FROM v_donvi WHERE ma_dv LIKE '215.3%'
                GROUP BY donvi_id, phanvung_id
            ),
            nguon AS (
                SELECT b2.*, dv.ma_dv AS ma_dv_ql,
                       ROW_NUMBER() OVER (PARTITION BY b2.phanvung_id, b2.thuebao_id ORDER BY b2.ngay_cn DESC NULLS LAST) AS rn
                FROM b2
                JOIN dv ON dv.donvi_id = b2.donvi_id AND dv.phanvung_id = b2.phanvung_id
                WHERE (b2.ngay_moi IS NULL OR TRUNC(b2.ngay_cn) = b2.ngay_moi)
                  AND b2.loaitb_id = 58
                  AND b2.nhanvien_th_id > 0
                  AND b2.ngayhen_bh_tu IS NULL
            ),
            nv AS (
                SELECT phanvung_id, nhanvien_id, MAX(ma_nv) AS ma_nv, MAX(ten_nv) AS ten_nv
                FROM v_nv_tungqd GROUP BY phanvung_id, nhanvien_id
            ),
            ph AS (
                SELECT p.*,
                       ROW_NUMBER() OVER (PARTITION BY p.phanvung_id, p.thuebao_id ORDER BY p.ngay_giao DESC, p.id DESC) AS rn,
                       COUNT(*) OVER (PARTITION BY p.phanvung_id, p.thuebao_id) AS so_lan_giao
                FROM ghtt_phieu p
                WHERE p.trang_thai <> 'THU_HOI'
            )";

        public async Task<List<GhttThueBaoDto>> GetThueBaoAsync(int thang, string? maDv)
        {
            if (thang <= 0) throw new GhttPhieuValidationException("Chưa chọn tháng dữ liệu.");

            var p = new DynamicParameters();
            p.Add("thang", thang);
            var where = new List<string>
            {
                "n.rn = 1",
                ScopeClause(p, "n.ma_dv_ql", "nv.ma_nv")
            };
            AddDonViFilter(where, p, "n.ma_dv_ql", maDv);

            await using var conn = new OracleConnection(_connString);
            var rows = await conn.QueryAsync<GhttThueBaoDto>($@"
                WITH {CteNguon}
                SELECT n.phanvung_id || '|' || n.thuebao_id AS Khoa,
                       n.phanvung_id AS PhanVungId, n.thuebao_id AS ThueBaoId, n.sl_thang AS SlThang,
                       n.ma_tb AS MaTb, n.ten_tb AS TenTb, n.diachi_ld AS DiaChi, NVL(n.so_dt, n.sdt_lh) AS SoDt,
                       n.ten_dv AS TenDv, n.ma_dv_ql AS MaDvQl, n.doivt_id AS DoiVtId, n.ten_doivt AS TenDoiVt,
                       n.ngay_ktdc AS NgayKtdc, n.ten_km AS TenKm, n.ketqua_ob_saucung_ipcc AS KqOb, n.ngay_cn AS NgayCn,
                       nv.ma_nv AS MaNvTh, nv.ten_nv AS TenNvTh,
                       p.id AS PhieuId, p.ma_nv_nhan AS MaNvNhan, p.ten_nv_nhan AS TenNvNhan, p.ngay_giao AS NgayGiao,
                       p.han_xl AS HanXl, p.ly_do AS LyDo, p.ghichu_kq AS GhiChuKq, p.ngay_kq AS NgayKq, p.ngay_hen AS NgayHen,
                       NVL(p.so_lan_giao, 0) AS SoLanGiao,
                       CASE
                           WHEN p.id IS NULL THEN 'CHUA_GIAO'
                           WHEN p.trang_thai = 'THANH_CONG' THEN 'THANH_CONG'
                           WHEN p.trang_thai = 'KHONG_TC' THEN 'KHONG_TC'
                           WHEN p.han_xl > SYSDATE THEN 'DA_GIAO'
                           ELSE 'QUA_HAN'
                       END AS TinhTrang,
                       CASE WHEN (p.id IS NULL OR p.han_xl <= SYSDATE) AND nv.ma_nv IS NOT NULL THEN 1 ELSE 0 END AS DuocGiaoSo
                FROM nguon n
                LEFT JOIN nv ON nv.phanvung_id = n.phanvung_id AND nv.nhanvien_id = n.nhanvien_th_id
                LEFT JOIN ph p ON p.phanvung_id = n.phanvung_id AND p.thuebao_id = n.thuebao_id AND p.rn = 1
                WHERE {string.Join(" AND ", where)}
                ORDER BY n.ten_doivt, nv.ten_nv, n.ma_tb", p, commandTimeout: 120);
            return rows.ToList();
        }

        public async Task<GhttGiaoPhieuResultDto> GiaoPhieuAsync(GhttGiaoPhieuRequestDto request)
        {
            var chon = (request.Khoa ?? new List<string>())
                .Select(k => (k ?? string.Empty).Trim())
                .Where(k => k.Length > 0)
                .Distinct()
                .ToList();
            if (chon.Count == 0) throw new GhttPhieuValidationException("Chưa chọn thuê bao nào.");
            if (chon.Count > MaxChonMotLan) throw new GhttPhieuValidationException($"Mỗi lần giao tối đa {MaxChonMotLan} thuê bao.");

            var nguoiGiao = NguoiThaoTac();
            var tenNguoiGiao = Truncate(_currentUser.TenNv?.Trim(), 200);
            var ghiChu = CleanText(request.GhiChu, 1000);

            // Nguồn hợp lệ trong phạm vi tại thời điểm giao (không tin dữ liệu gửi lên).
            var nguon = (await GetThueBaoAsync(request.Thang, request.MaDv)).ToDictionary(x => x.Khoa);

            var kq = new GhttGiaoPhieuResultDto();
            int khongCoNv = 0, conHan = 0, ngoaiDs = 0;

            await using var conn = new OracleConnection(_connString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                // Khóa bảng trong giao dịch ngắn (giống BCBG) để 2 người bấm cùng lúc không giao trùng 1 TB.
                await conn.ExecuteAsync("LOCK TABLE ghtt_phieu IN EXCLUSIVE MODE", transaction: tx);

                foreach (var khoa in chon)
                {
                    if (!nguon.TryGetValue(khoa, out var r)) { ngoaiDs++; continue; }
                    if (string.IsNullOrWhiteSpace(r.MaNvTh)) { khongCoNv++; continue; }

                    var id = await conn.ExecuteScalarAsync<long>("SELECT ghtt_phieu_seq.NEXTVAL FROM dual", transaction: tx);
                    var p = new DynamicParameters();
                    p.Add("id", id);
                    p.Add("sl_thang", r.SlThang);
                    p.Add("pv", r.PhanVungId);
                    p.Add("tb", r.ThueBaoId);
                    p.Add("ma_tb", r.MaTb);
                    p.Add("ten_tb", r.TenTb);
                    p.Add("diachi", r.DiaChi);
                    p.Add("so_dt", r.SoDt);
                    p.Add("ten_dv", r.TenDv);
                    p.Add("ma_dv", r.MaDvQl);
                    p.Add("doivt_id", r.DoiVtId);
                    p.Add("ten_doivt", r.TenDoiVt);
                    p.Add("ngay_ktdc", r.NgayKtdc, DbType.DateTime);
                    p.Add("ten_km", r.TenKm);
                    p.Add("ma_nv", r.MaNvTh);
                    p.Add("ten_nv", r.TenNvTh);
                    p.Add("nguoi", nguoiGiao);
                    p.Add("ten_nguoi", tenNguoiGiao);
                    p.Add("gio", HanGio);
                    p.Add("ghichu", ghiChu);

                    // donvi_id lấy lại từ Biểu 8 trong câu INSERT (DTO không mang donvi_id).
                    var n = await conn.ExecuteAsync(@"
                        INSERT INTO ghtt_phieu (id, sl_thang, phanvung_id, thuebao_id, ma_tb, ten_tb, diachi, so_dt,
                            donvi_id, ten_dv, ma_dv, doivt_id, ten_doivt, ngay_ktdc, ten_km,
                            ma_nv_nhan, ten_nv_nhan, nguoi_giao, ten_nguoi_giao, ngay_giao, han_xl, ghichu_giao, trang_thai)
                        SELECT :id, :sl_thang, :pv, :tb, :ma_tb, :ten_tb, :diachi, :so_dt,
                               (SELECT MAX(b.donvi_id) FROM ghtt_bieu_8 b
                                 WHERE b.sl_thang = :sl_thang AND b.phanvung_id = :pv AND b.thuebao_id = :tb),
                               :ten_dv, :ma_dv, :doivt_id, :ten_doivt, :ngay_ktdc, :ten_km,
                               :ma_nv, :ten_nv, :nguoi, :ten_nguoi, SYSDATE, SYSDATE + :gio / 24, :ghichu, 'DA_GIAO'
                        FROM dual
                        WHERE NOT EXISTS (SELECT 1 FROM ghtt_phieu x
                                          WHERE x.phanvung_id = :pv AND x.thuebao_id = :tb
                                            AND x.trang_thai <> 'THU_HOI' AND x.han_xl > SYSDATE)", p, tx);
                    if (n == 1)
                    {
                        await GhiLichSuAsync(conn, tx, id, "GIAO",
                            $"Giao cho NV thực hiện {r.MaNvTh}, hạn {HanGio}h" + (ghiChu == null ? "" : ". " + ghiChu) + " [DTNTB]",
                            nguoiGiao);
                        kq.ThanhCong++;
                    }
                    else conHan++;
                }

                kq.HanXl = await conn.ExecuteScalarAsync<DateTime>($"SELECT SYSDATE + {HanGio} / 24 FROM dual", transaction: tx);
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }

            kq.BoQua = conHan + khongCoNv + ngoaiDs;
            if (conHan > 0) kq.GhiChu.Add($"{conHan} TB còn phiếu trong hạn.");
            if (khongCoNv > 0) kq.GhiChu.Add($"{khongCoNv} TB chưa ghép được NV thực hiện sang tài khoản.");
            if (ngoaiDs > 0) kq.GhiChu.Add($"{ngoaiDs} TB không còn trong danh sách (ngoài phạm vi hoặc dữ liệu vừa import lại).");

            _logger.LogInformation("{User} giao {N} phiếu GHTT tháng {Thang} (bỏ qua {BoQua}).", nguoiGiao, kq.ThanhCong, request.Thang, kq.BoQua);
            return kq;
        }

        public async Task<byte[]> ExportThueBaoAsync(GhttThueBaoQueryDto query)
        {
            var all = await GetThueBaoAsync(query.Thang, query.MaDv);
            var rows = LocThueBao(all, query).Take(MaxExportRows).ToList();

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("GHTT");
            var headers = new[]
            {
                "STT", "Mã TB", "Tên TB", "Địa chỉ", "SĐT", "Đơn vị", "Tổ/Đội", "Ngày hết hạn", "Gói/KM", "KQ outbound",
                "Mã NV thực hiện", "NV thực hiện", "Ngày giao", "Hạn XL", "Trạng thái",
                "Lý do", "Ghi chú KQ", "Ngày hẹn", "Ngày cập nhật KQ", "Số lần giao"
            };
            for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
            var head = ws.Range(1, 1, 1, headers.Length);
            head.Style.Font.Bold = true;
            head.Style.Font.FontColor = XLColor.White;
            head.Style.Fill.BackgroundColor = XLColor.FromHtml("#0b6bcb");

            var row = 2;
            foreach (var r in rows)
            {
                ws.Cell(row, 1).Value = (double)(row - 1);
                ws.Cell(row, 2).Value = Text(r.MaTb);
                ws.Cell(row, 3).Value = Text(r.TenTb);
                ws.Cell(row, 4).Value = Text(r.DiaChi);
                ws.Cell(row, 5).Value = Text(r.SoDt);
                ws.Cell(row, 6).Value = Text(r.TenDv);
                ws.Cell(row, 7).Value = Text(r.TenDoiVt);
                SetDate(ws.Cell(row, 8), r.NgayKtdc, "dd/MM/yyyy");
                ws.Cell(row, 9).Value = Text(r.TenKm);
                ws.Cell(row, 10).Value = Text(r.KqOb);
                ws.Cell(row, 11).Value = Text(r.MaNvTh);
                ws.Cell(row, 12).Value = Text(r.TenNvTh);
                SetDate(ws.Cell(row, 13), r.NgayGiao, "HH:mm dd/MM/yyyy");
                SetDate(ws.Cell(row, 14), r.HanXl, "HH:mm dd/MM/yyyy");
                ws.Cell(row, 15).Value = TenTinhTrang(r.TinhTrang);
                ws.Cell(row, 16).Value = Text(r.LyDo);
                ws.Cell(row, 17).Value = Text(r.GhiChuKq);
                SetDate(ws.Cell(row, 18), r.NgayHen, "dd/MM/yyyy");
                SetDate(ws.Cell(row, 19), r.NgayKq, "HH:mm dd/MM/yyyy");
                ws.Cell(row, 20).Value = (double)r.SoLanGiao;
                row++;
            }
            ws.Columns().AdjustToContents(1, Math.Min(row, 300), 8, 60);
            ws.SheetView.FreezeRows(1);

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        /// <summary>Lọc giống màn hình: danh sách (chưa giao / đã giao), thẻ trạng thái, tổ, NV thực hiện, từ khóa.</summary>
        private static IEnumerable<GhttThueBaoDto> LocThueBao(IEnumerable<GhttThueBaoDto> q, GhttThueBaoQueryDto f)
        {
            if (f.DoiVtId.HasValue) q = q.Where(r => r.DoiVtId == f.DoiVtId);
            if (!string.IsNullOrWhiteSpace(f.MaNv)) q = q.Where(r => string.Equals(r.MaNvTh, f.MaNv.Trim(), StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(f.Search))
            {
                var s = f.Search.Trim().ToLowerInvariant();
                q = q.Where(r => $"{r.MaTb}|{r.TenTb}|{r.SoDt}|{r.DiaChi}".ToLowerInvariant().Contains(s));
            }
            if (string.Equals(f.DanhSach, "DA", StringComparison.OrdinalIgnoreCase)) q = q.Where(r => r.TinhTrang != GhttPhieuTinhTrang.CHUA_GIAO);
            else if (string.Equals(f.DanhSach, "CHUA", StringComparison.OrdinalIgnoreCase)) q = q.Where(r => r.TinhTrang == GhttPhieuTinhTrang.CHUA_GIAO);

            switch ((f.TrangThai ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "DUOC_GIAO": q = q.Where(r => r.DuocGiao); break;
                case "CHUA_GIAO":
                case "DA_GIAO":
                case "QUA_HAN":
                    var tt = f.TrangThai!.Trim().ToUpperInvariant();
                    q = q.Where(r => r.TinhTrang == tt);
                    break;
                case "CO_KQ": q = q.Where(r => r.TinhTrang is GhttPhieuTinhTrang.KHONG_TC or GhttPhieuTinhTrang.THANH_CONG); break;
            }
            return q;
        }

        // ============================================================
        // PHIẾU (NHÂN VIÊN / NHẬP HỘ)
        // ============================================================

        /// <summary>Phiếu đang mở và chưa bị phiếu mới hơn của cùng TB thay thế (giống BCBG).</summary>
        private const string DkMo = @"p.trang_thai = 'DA_GIAO'
                AND NOT EXISTS (SELECT 1 FROM ghtt_phieu m
                                WHERE m.phanvung_id = p.phanvung_id AND m.thuebao_id = p.thuebao_id
                                  AND m.trang_thai <> 'THU_HOI' AND m.ngay_giao > p.ngay_giao)";

        private const string DkXong = "p.trang_thai IN ('THANH_CONG', 'KHONG_TC') AND p.ngay_kq >= TRUNC(SYSDATE) - 60";

        public async Task<GhttPhieuListDto> GetPhieuAsync(GhttPhieuQueryDto query)
        {
            var p = new DynamicParameters();
            var where = new List<string> { ScopeClause(p, "p.ma_dv", "p.ma_nv_nhan") };
            AddDonViFilter(where, p, "p.ma_dv", query.MaDv);
            if (query.DoiVtId.HasValue)
            {
                where.Add("p.doivt_id = :f_to");
                p.Add("f_to", query.DoiVtId.Value);
            }
            if (!string.IsNullOrWhiteSpace(query.MaNv))
            {
                where.Add("UPPER(TRIM(p.ma_nv_nhan)) = UPPER(TRIM(:f_nv))");
                p.Add("f_nv", query.MaNv.Trim());
            }
            var dkPhamVi = string.Join(" AND ", where);

            var tab = (query.Tab ?? "CAN").Trim().ToUpperInvariant();
            var dkTab = tab switch
            {
                "QUA_HAN" => DkMo + " AND p.han_xl <= SYSDATE",
                "XONG" => DkXong,
                _ => DkMo
            };

            await using var conn = new OracleConnection(_connString);
            var items = (await conn.QueryAsync<GhttPhieuDto>($@"
                SELECT p.id AS Id, p.sl_thang AS SlThang, p.ma_tb AS MaTb, p.ten_tb AS TenTb, p.diachi AS DiaChi, p.so_dt AS SoDt,
                       p.ten_dv AS TenDv, p.ma_dv AS MaDv, p.doivt_id AS DoiVtId, p.ten_doivt AS TenDoiVt,
                       p.ngay_ktdc AS NgayKtdc, p.ten_km AS TenKm, p.ma_nv_nhan AS MaNvNhan, p.ten_nv_nhan AS TenNvNhan,
                       p.nguoi_giao AS NguoiGiao, p.ten_nguoi_giao AS TenNguoiGiao, p.ngay_giao AS NgayGiao, p.han_xl AS HanXl,
                       p.ghichu_giao AS GhiChuGiao, p.trang_thai AS TrangThai, p.ly_do AS LyDo, p.ghichu_kq AS GhiChuKq,
                       p.ngay_hen AS NgayHen, p.ngay_kq AS NgayKq, p.nguoi_kq AS NguoiKq,
                       CASE WHEN p.trang_thai = 'DA_GIAO' AND p.han_xl <= SYSDATE THEN 'QUA_HAN' ELSE p.trang_thai END AS TinhTrang
                FROM ghtt_phieu p
                WHERE {dkPhamVi} AND {dkTab}
                ORDER BY {(tab == "XONG" ? "p.ngay_kq DESC" : "p.ten_doivt, p.ten_nv_nhan, p.han_xl")}", p)).ToList();

            var dem = await conn.QueryFirstOrDefaultAsync<GhttPhieuDemDto>($@"
                SELECT NVL(SUM(CASE WHEN {DkMo} THEN 1 ELSE 0 END), 0) AS Can,
                       NVL(SUM(CASE WHEN {DkMo} AND p.han_xl <= SYSDATE THEN 1 ELSE 0 END), 0) AS QuaHan,
                       NVL(SUM(CASE WHEN {DkXong} THEN 1 ELSE 0 END), 0) AS Xong
                FROM ghtt_phieu p WHERE {dkPhamVi}", p);

            return new GhttPhieuListDto { Items = items, Dem = dem ?? new GhttPhieuDemDto() };
        }

        public async Task<List<GhttPhieuBoLocDto>> GetBoLocPhieuAsync()
        {
            var p = new DynamicParameters();
            var scope = ScopeClause(p, "p.ma_dv", "p.ma_nv_nhan");
            await using var conn = new OracleConnection(_connString);
            var rows = await conn.QueryAsync<GhttPhieuBoLocDto>($@"
                SELECT DISTINCT SUBSTR({Norm("p.ma_dv")}, 1, 7) AS MaDv7, p.ten_dv AS TenDv, p.doivt_id AS DoiVtId,
                       p.ten_doivt AS TenDoiVt, p.ma_nv_nhan AS MaNvNhan, p.ten_nv_nhan AS TenNvNhan
                FROM ghtt_phieu p
                WHERE p.trang_thai <> 'THU_HOI' AND {scope}", p);
            return rows.ToList();
        }

        public async Task<bool> CapNhatKetQuaAsync(long phieuId, GhttKetQuaRequestDto request)
        {
            if (phieuId <= 0) return false;
            var lyDo = CleanText(request.LyDo, 300);
            if (!request.ThanhCong && lyDo == null)
                throw new GhttPhieuValidationException("Chưa nhập lý do không thành công.");
            var ghiChu = CleanText(request.GhiChu, 1000);

            DateTime? ngayHen = null;
            if (!request.ThanhCong && !string.IsNullOrWhiteSpace(request.NgayHen))
            {
                if (!DateTime.TryParseExact(request.NgayHen.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var hen))
                    throw new GhttPhieuValidationException("Ngày hẹn không hợp lệ.");
                ngayHen = hen.Date;
            }

            var nguoiNhap = NguoiThaoTac();
            var trangThai = request.ThanhCong ? GhttPhieuTinhTrang.THANH_CONG : GhttPhieuTinhTrang.KHONG_TC;

            await using var conn = new OracleConnection(_connString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                var p = new DynamicParameters();
                p.Add("id", phieuId);
                var scope = ScopeClause(p, "p.ma_dv", "p.ma_nv_nhan");
                var maNvNhan = await conn.ExecuteScalarAsync<string?>(
                    $"SELECT p.ma_nv_nhan FROM ghtt_phieu p WHERE p.id = :id AND {scope} FOR UPDATE", p, tx);
                if (maNvNhan == null)
                {
                    await tx.RollbackAsync();
                    return false;
                }

                var u = new DynamicParameters();
                u.Add("tt", trangThai);
                u.Add("ly_do", request.ThanhCong ? null : lyDo);
                u.Add("ghichu", ghiChu);
                u.Add("ngay_hen", ngayHen, DbType.Date);
                u.Add("nguoi", nguoiNhap);
                u.Add("id", phieuId);
                // Phiếu đang mở (kể cả quá hạn) -> TC / KTC; phiếu KTC -> được đổi sang TC (giống BCBG).
                var n = await conn.ExecuteAsync(@"
                    UPDATE ghtt_phieu
                    SET trang_thai = :tt, ly_do = :ly_do, ghichu_kq = :ghichu, ngay_hen = :ngay_hen,
                        ngay_kq = SYSDATE, nguoi_kq = :nguoi
                    WHERE id = :id
                      AND (trang_thai = 'DA_GIAO' OR (trang_thai = 'KHONG_TC' AND :tt = 'THANH_CONG'))", u, tx);

                if (n == 1)
                {
                    var nhapHo = !string.Equals(maNvNhan.Trim(), nguoiNhap.Trim(), StringComparison.OrdinalIgnoreCase);
                    var nd = request.ThanhCong
                        ? (ghiChu ?? string.Empty)
                        : "Lý do: " + lyDo + (ghiChu == null ? "" : ". " + ghiChu);
                    await GhiLichSuAsync(conn, tx, phieuId, trangThai,
                        (nhapHo ? $"[Nhập hộ NV {maNvNhan.Trim()}] " : "") + nd + " [DTNTB]", nguoiNhap);
                }
                await tx.CommitAsync();
                return n == 1;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // ============================================================
        // PHẠM VI DỮ LIỆU
        // ============================================================

        /// <summary>Quy mã 215.7xxx về 215.3xxx (như BCBG) để so khớp mã đơn vị.</summary>
        private static string Norm(string column) => $"REGEXP_REPLACE(TRIM({column}), '^215\\.7', '215.3')";

        private static string? NormValue(string? maDv)
        {
            if (string.IsNullOrWhiteSpace(maDv)) return null;
            var v = maDv.Trim();
            return v.StartsWith("215.7", StringComparison.Ordinal) ? "215.3" + v[5..] : v;
        }

        /// <summary>
        /// Điều kiện phạm vi theo data_scope.
        /// maDvColumn: cột mã đơn vị (215.3...), maNvColumn: cột mã NV (NV thực hiện / NV nhận phiếu).
        /// </summary>
        private string ScopeClause(DynamicParameters p, string maDvColumn, string maNvColumn)
        {
            switch (_currentUser.ScopeLevel)
            {
                case UserDataScopeLevel.ToanTinh:
                    return "1 = 1";
                case UserDataScopeLevel.DiaBan:
                    if (string.IsNullOrWhiteSpace(_currentUser.DiaBanId)) return "1 = 0";
                    p.Add("sc_diaban", _currentUser.DiaBanId.Trim());
                    return $@"SUBSTR({Norm(maDvColumn)}, 1, 7) IN (
                                SELECT SUBSTR({Norm("db.ma_dv")}, 1, 7) FROM v_donvi_diaban db
                                WHERE TRIM(TO_CHAR(db.diaban_id)) = :sc_diaban)";
                case UserDataScopeLevel.DonVi:
                {
                    var dv = NormValue(_currentUser.MaDv);
                    if (dv == null) return "1 = 0";
                    p.Add("sc_dv", dv.Length > 7 ? dv[..7] : dv);
                    return $"SUBSTR({Norm(maDvColumn)}, 1, 7) = :sc_dv";
                }
                case UserDataScopeLevel.ToQuanLy:
                {
                    // Tổ = toàn bộ ma_dv của người dùng; NV thuộc tổ lấy từ v_nguoidung_diaban.
                    var to = NormValue(_currentUser.MaDv);
                    if (to == null) return "1 = 0";
                    p.Add("sc_to", to);
                    return $@"UPPER(TRIM({maNvColumn})) IN (
                                SELECT UPPER(TRIM(u.ma_nv)) FROM v_nguoidung_diaban u
                                WHERE u.ma_nv IS NOT NULL AND {Norm("u.ma_dv")} = :sc_to)";
                }
                default:
                {
                    var maNv = _currentUser.MaNv?.Trim();
                    if (string.IsNullOrEmpty(maNv)) return "1 = 0";
                    p.Add("sc_nv", maNv);
                    return $"UPPER(TRIM({maNvColumn})) = UPPER(:sc_nv)";
                }
            }
        }

        /// <summary>Danh sách đơn vị chọn được: toàn tỉnh / địa bàn theo phạm vi, các cấp dưới chỉ đơn vị của mình.</summary>
        private string DonViScopeClause(DynamicParameters p, string maDvColumn)
        {
            switch (_currentUser.ScopeLevel)
            {
                case UserDataScopeLevel.ToanTinh:
                case UserDataScopeLevel.DiaBan:
                    return ScopeClause(p, maDvColumn, "NULL");
                default:
                    var dv = NormValue(_currentUser.MaDv);
                    if (dv == null) return "1 = 0";
                    p.Add("sc_dv7", dv.Length > 7 ? dv[..7] : dv);
                    return $"SUBSTR({Norm(maDvColumn)}, 1, 7) = :sc_dv7";
            }
        }

        private static void AddDonViFilter(List<string> where, DynamicParameters p, string maDvColumn, string? maDv)
        {
            var dv = NormValue(maDv);
            if (dv == null || dv.Equals("ALL", StringComparison.OrdinalIgnoreCase)) return;
            if (dv.Length > 30 || !dv.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-'))
                throw new GhttPhieuValidationException("Mã đơn vị không hợp lệ.");
            where.Add($"SUBSTR({Norm(maDvColumn)}, 1, 7) = :f_dv");
            p.Add("f_dv", dv.Length > 7 ? dv[..7] : dv);
        }

        // ============================================================
        // TIỆN ÍCH
        // ============================================================

        /// <summary>Người thao tác ghi vào phiếu: mã NV (như BCBG), thiếu thì dùng tài khoản đăng nhập.</summary>
        private string NguoiThaoTac()
        {
            var v = _currentUser.MaNv?.Trim();
            if (string.IsNullOrEmpty(v)) v = _currentUser.Username?.Trim();
            if (string.IsNullOrEmpty(v)) throw new GhttPhieuValidationException("Không xác định được người dùng đăng nhập.");
            return v.Length > 50 ? v[..50] : v;
        }

        private static async Task GhiLichSuAsync(OracleConnection conn, System.Data.Common.DbTransaction tx, long phieuId, string thaoTac, string noiDung, string nguoi)
        {
            await conn.ExecuteAsync(@"
                INSERT INTO ghtt_phieu_ls (id, phieu_id, thao_tac, noi_dung, nguoi, ngay)
                VALUES (ghtt_phieu_ls_seq.NEXTVAL, :pid, :tt, :nd, :nguoi, SYSDATE)",
                new { pid = phieuId, tt = thaoTac, nd = Truncate(noiDung, 1000), nguoi }, tx);
        }

        private static string? CleanText(string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var clean = new string(value.Where(c => !char.IsControl(c) || c == '\n').ToArray()).Trim();
            return clean.Length == 0 ? null : (clean.Length > max ? clean[..max] : clean);
        }

        private static string? Truncate(string? value, int max) =>
            value == null ? null : (value.Length > max ? value[..max] : value);

        private static string Text(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            // Chặn công thức Excel (CSV/Formula injection).
            return value[0] is '=' or '+' or '-' or '@' ? "'" + value : value;
        }

        private static void SetDate(IXLCell cell, DateTime? value, string format)
        {
            if (!value.HasValue) return;
            cell.Value = value.Value;
            cell.Style.DateFormat.Format = format;
        }

        private static string TenTinhTrang(string tt) => tt switch
        {
            GhttPhieuTinhTrang.CHUA_GIAO => "Chưa giao",
            GhttPhieuTinhTrang.DA_GIAO => "Đang xử lý",
            GhttPhieuTinhTrang.QUA_HAN => "Quá hạn",
            GhttPhieuTinhTrang.KHONG_TC => "Không thành công",
            GhttPhieuTinhTrang.THANH_CONG => "NV báo thành công",
            _ => tt
        };
    }
}
