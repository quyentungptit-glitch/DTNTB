using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ClosedXML.Excel;
using Dapper;
using DTNTB.Core.Constants;
using DTNTB.Core.DTOs;
using DTNTB.Core.Interfaces;
using ImageMagick;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;

namespace DTNTB.Infrastructure.Services
{
    /// <summary>
    /// Quản lý công việc TTHT (phiếu yêu cầu công việc). Bảng dtntb_cv_* (DatabaseScripts/20261031_cv_phieu_cong_viec.sql).
    ///
    ///  - Người tạo (CV.TAO): chỉ gửi tổ Hạ tầng của địa bàn mình (TTVT = 7 ký tự đầu mã đơn vị, 215.7xx quy về 215.3xx).
    ///    CV.TAO_TOAN_TINH: gửi tổ bất kỳ và tạo phiếu khai báo KTHT (đầu việc KTHT_) cho người nhận KTHT.
    ///  - Người nhận = dòng trong dtntb_cv_nguoi_nhan (không cần quyền riêng): giao 1 / nhiều người, chuyển tổ, trả lại.
    ///  - Người xử lý (CV.XU_LY): chụp ảnh minh chứng, nhập số lượng, yêu cầu khoá. Quá hạn chốt tại lúc yêu cầu khoá.
    ///  - Người tạo đóng lẻ từng người (hoặc trả lại người đó). Mọi người đã đóng -> phiếu HOAN_THANH.
    ///  - CV.DUYET: duyệt tính lương các dòng đã đóng (được sửa số lượng). Tháng lương = tháng duyệt.
    ///  - Hạn xử lý 24/7 = ngày tạo + SLA (phút); đầu việc không SLA thì không có hạn; không đơn giá thì thành tiền 0.
    /// Người dùng được nhận diện theo mã NV (claim ma_nv) hoặc tài khoản đăng nhập (ma_nd), không phân biệt hoa thường.
    /// </summary>
    public class CvService : ICvService
    {
        private const int MaxRows = 3000;
        private const int MaxDinhKem = 5;
        private const int MaxAnhMoiLan = 10;
        private const int MaxAnhMoiNguoi = 30;
        private const int ThuNhoPx = 400;

        private static readonly Dictionary<string, string> LoaiFileChoPhep = new(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".doc"] = "application/msword",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".xls"] = "application/vnd.ms-excel",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png"
        };

        private readonly string _connString;
        private readonly ICurrentUserService _currentUser;
        private readonly ICvFileStore _store;
        private readonly ILogger<CvService> _logger;
        private readonly long _maxFileBytes;
        private readonly int _maxDimension;
        private readonly int _jpegQuality;

        public CvService(IConfiguration config, ICurrentUserService currentUser, ICvFileStore store, ILogger<CvService> logger)
        {
            _connString = config.GetConnectionString("ConnectionString_NBH") ?? string.Empty;
            _currentUser = currentUser;
            _store = store;
            _logger = logger;
            _maxFileBytes = Int(config, "Upload:MaxFileSizeInMB", 10, 1, 20) * 1024L * 1024L;
            _maxDimension = Int(config, "NamS:MaxDimension", 2048, 800, 4096);
            _jpegQuality = Int(config, "NamS:JpegQuality", 85, 60, 95);
        }

        private static int Int(IConfiguration config, string key, int fallback, int min, int max)
            => int.TryParse(config[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, min, max) : fallback;

        // ============================================================
        // NGƯỜI DÙNG
        // ============================================================
        private string? MaNvMe => string.IsNullOrWhiteSpace(_currentUser.MaNv) ? null : _currentUser.MaNv.Trim();
        private string? MaNdMe => string.IsNullOrWhiteSpace(_currentUser.Username) ? null : _currentUser.Username.Trim();

        /// <summary>Mã ghi vào phiếu cho người thao tác: mã NV, thiếu thì tài khoản đăng nhập.</summary>
        private string Me()
        {
            var v = MaNvMe ?? MaNdMe;
            if (string.IsNullOrEmpty(v)) throw new CvValidationException("Không xác định được người dùng đăng nhập.");
            return v.Length > 50 ? v[..50] : v;
        }

        private string? TenMe => Truncate(_currentUser.TenNv?.Trim(), 200);

        private bool LaToi(string? ma)
        {
            if (string.IsNullOrWhiteSpace(ma)) return false;
            var m = ma.Trim();
            return (MaNvMe != null && string.Equals(m, MaNvMe, StringComparison.OrdinalIgnoreCase))
                || (MaNdMe != null && string.Equals(m, MaNdMe, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Điều kiện "cột mã người = người đang đăng nhập" (so cả mã NV và tài khoản).</summary>
        private string MeSql(DynamicParameters p, string column)
        {
            var nv = MaNvMe ?? MaNdMe ?? "#";
            var nd = MaNdMe ?? MaNvMe ?? "#";
            p.Add("me_nv", nv.ToUpperInvariant());
            p.Add("me_nd", nd.ToUpperInvariant());
            return $"UPPER(TRIM({column})) IN (:me_nv, :me_nd)";
        }

        private bool Co(string perm) => _currentUser.HasPermission(perm);
        private bool CoQuanTri => Co(AppPermissions.CV.QUAN_TRI);
        private bool CoXemBaoCao => Co(AppPermissions.CV.VIEW) || Co(AppPermissions.CV.DUYET) || CoQuanTri;

        /// <summary>TTVT của người đăng nhập: 7 ký tự đầu mã đơn vị, 215.7xx quy về 215.3xx.</summary>
        private string? TtvtCuaToi()
        {
            var dv = _currentUser.MaDv?.Trim();
            if (string.IsNullOrEmpty(dv)) return null;
            if (dv.StartsWith("215.7", StringComparison.Ordinal)) dv = "215.3" + dv[5..];
            return dv.Length > 7 ? dv[..7] : dv;
        }

        public async Task<CvContextDto> GetContextAsync()
        {
            var p = new DynamicParameters();
            var me = MeSql(p, "n.ma_nv");
            await using var conn = new OracleConnection(_connString);
            var laNguoiNhan = await conn.ExecuteScalarAsync<int>(
                $"SELECT COUNT(1) FROM dtntb_cv_nguoi_nhan n WHERE n.trang_thai = 1 AND {me} AND ROWNUM = 1", p);
            return new CvContextDto
            {
                MaNv = MaNvMe ?? MaNdMe,
                TenNv = TenMe,
                MaDvTtvt = TtvtCuaToi(),
                CoTao = Co(AppPermissions.CV.TAO) || Co(AppPermissions.CV.TAO_TOAN_TINH),
                CoTaoToanTinh = Co(AppPermissions.CV.TAO_TOAN_TINH),
                CoXuLy = Co(AppPermissions.CV.XU_LY),
                CoDuyet = Co(AppPermissions.CV.DUYET),
                CoXem = CoXemBaoCao,
                CoXuat = Co(AppPermissions.CV.EXPORT),
                CoQuanTri = CoQuanTri,
                LaNguoiNhan = laNguoiNhan > 0
            };
        }

        // ============================================================
        // DANH MỤC
        // ============================================================
        private const string DauViecColumns = @"d.dau_viec_id AS DauViecId, d.ma AS Ma, d.ten AS Ten, d.nhom AS Nhom, d.loai_phieu AS LoaiPhieu,
            d.sla_phut AS SlaPhut, d.don_gia AS DonGia, d.dvt AS Dvt, d.nhom_dinh_muc AS NhomDinhMuc, d.tinh_tg_chay AS TinhTgChay,
            d.thu_tu AS ThuTu, d.is_active AS IsActive";

        public async Task<List<CvDauViecDto>> GetDauViecAsync(bool tatCa)
        {
            var dk = tatCa && CoQuanTri ? "1 = 1" : "d.is_active = 1";
            await using var conn = new OracleConnection(_connString);
            var rows = await conn.QueryAsync<CvDauViecDto>($"SELECT {DauViecColumns} FROM dtntb_cv_dau_viec d WHERE {dk} ORDER BY d.thu_tu, d.ma");
            return rows.ToList();
        }

        public async Task<CvDauViecDto> LuuDauViecAsync(CvDauViecDto dto)
        {
            if (!CoQuanTri) throw new CvValidationException("Bạn không có quyền quản trị danh mục.");
            var ma = CleanText(dto.Ma, 30)?.ToUpperInvariant();
            var ten = CleanText(dto.Ten, 300);
            if (ma == null || !ma.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.')) throw new CvValidationException("Mã đầu việc không hợp lệ.");
            if (ten == null) throw new CvValidationException("Chưa nhập tên đầu việc.");
            var loai = (dto.LoaiPhieu ?? "").Trim().ToUpperInvariant();
            if (loai is not (CvConst.LOAI_HT or CvConst.LOAI_KTHT)) throw new CvValidationException("Loại phiếu phải là HT hoặc KTHT.");
            var nhom = CleanText(dto.Nhom, 10)?.ToUpperInvariant() ?? loai;
            if (dto.SlaPhut is < 0 or > 525600) throw new CvValidationException("SLA (phút) không hợp lệ.");
            if (dto.DonGia is < 0 or > 1_000_000_000) throw new CvValidationException("Đơn giá không hợp lệ.");

            var p = new DynamicParameters();
            p.Add("ma", ma);
            p.Add("ten", ten);
            p.Add("nhom", nhom);
            p.Add("loai", loai);
            p.Add("sla", dto.SlaPhut is > 0 ? dto.SlaPhut : null);
            p.Add("gia", dto.DonGia is > 0 ? dto.DonGia : null);
            p.Add("dvt", CleanText(dto.Dvt, 50));
            p.Add("map", CleanText(dto.NhomDinhMuc, 300));
            p.Add("chay", dto.TinhTgChay == 1 ? 1 : 0);
            p.Add("thu_tu", dto.ThuTu);
            p.Add("act", dto.IsActive == 0 ? 0 : 1);
            p.Add("nguoi", Me());

            await using var conn = new OracleConnection(_connString);
            var trungMa = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM dtntb_cv_dau_viec WHERE ma = :ma AND dau_viec_id <> :id", new { ma, id = dto.DauViecId });
            if (trungMa > 0) throw new CvValidationException($"Mã đầu việc {ma} đã tồn tại.");

            long id = dto.DauViecId;
            if (id > 0)
            {
                p.Add("id", id);
                var n = await conn.ExecuteAsync(@"
                    UPDATE dtntb_cv_dau_viec SET ma = :ma, ten = :ten, nhom = :nhom, loai_phieu = :loai, sla_phut = :sla, don_gia = :gia,
                        dvt = :dvt, nhom_dinh_muc = :map, tinh_tg_chay = :chay, thu_tu = :thu_tu, is_active = :act,
                        nguoi_cn = :nguoi, ngay_cn = SYSDATE
                    WHERE dau_viec_id = :id", p);
                if (n == 0) throw new CvValidationException("Không tìm thấy đầu việc.");
            }
            else
            {
                id = await conn.ExecuteScalarAsync<long>("SELECT seq_dtntb_cv_dau_viec.NEXTVAL FROM dual");
                p.Add("id", id);
                await conn.ExecuteAsync(@"
                    INSERT INTO dtntb_cv_dau_viec (dau_viec_id, ma, ten, nhom, loai_phieu, sla_phut, don_gia, dvt, nhom_dinh_muc,
                        tinh_tg_chay, thu_tu, is_active, nguoi_cn, ngay_cn)
                    VALUES (:id, :ma, :ten, :nhom, :loai, :sla, :gia, :dvt, :map, :chay, :thu_tu, :act, :nguoi, SYSDATE)", p);
            }
            return (await conn.QueryFirstAsync<CvDauViecDto>($"SELECT {DauViecColumns} FROM dtntb_cv_dau_viec d WHERE d.dau_viec_id = :id", new { id }));
        }

        private const string NguoiNhanColumns = @"n.id AS Id, n.loai AS Loai, n.ma_dv_ttvt AS MaDvTtvt, n.ten_dv_ttvt AS TenDvTtvt,
            n.ma_nv AS MaNv, n.ten_nv AS TenNv, n.ma_dv_ht AS MaDvHt, n.ten_dv_ht AS TenDvHt, n.thu_tu AS ThuTu,
            n.trang_thai AS TrangThai, n.ghi_chu AS GhiChu";

        public async Task<List<CvNguoiNhanDto>> GetNguoiNhanAsync(bool tatCa)
        {
            var dk = tatCa && CoQuanTri ? "1 = 1" : "n.trang_thai = 1";
            await using var conn = new OracleConnection(_connString);
            var rows = await conn.QueryAsync<CvNguoiNhanDto>(
                $"SELECT {NguoiNhanColumns} FROM dtntb_cv_nguoi_nhan n WHERE {dk} ORDER BY n.loai DESC, n.ten_dv_ttvt, n.thu_tu, n.ten_nv");
            return rows.ToList();
        }

        public async Task<CvNguoiNhanDto> LuuNguoiNhanAsync(CvNguoiNhanDto dto)
        {
            if (!CoQuanTri) throw new CvValidationException("Bạn không có quyền quản trị người nhận phiếu.");
            var loai = (dto.Loai ?? "").Trim().ToUpperInvariant();
            if (loai is not (CvConst.LOAI_HT or CvConst.LOAI_KTHT)) throw new CvValidationException("Loại phải là HT hoặc KTHT.");
            var ttvt = loai == CvConst.LOAI_KTHT ? null : CleanText(dto.MaDvTtvt, 30);
            if (loai == CvConst.LOAI_HT && ttvt == null) throw new CvValidationException("Người nhận Hạ tầng phải có mã TTVT.");
            var maNv = CleanText(dto.MaNv, 50);
            var maHt = CleanText(dto.MaDvHt, 30);
            if (maNv == null) throw new CvValidationException("Chưa nhập mã nhân viên.");
            if (maHt == null) throw new CvValidationException("Chưa nhập mã tổ (ma_dv_ht).");
            foreach (var code in new[] { ttvt, maNv, maHt })
                if (code != null && !code.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-'))
                    throw new CvValidationException($"Mã \"{code}\" không hợp lệ.");

            var p = new DynamicParameters();
            p.Add("loai", loai);
            p.Add("ttvt", ttvt);
            p.Add("ten_ttvt", loai == CvConst.LOAI_KTHT ? null : CleanText(dto.TenDvTtvt, 300));
            p.Add("ma_nv", maNv);
            p.Add("ten_nv", CleanText(dto.TenNv, 200));
            p.Add("ma_ht", maHt);
            p.Add("ten_ht", CleanText(dto.TenDvHt, 300));
            p.Add("thu_tu", dto.ThuTu);
            p.Add("tt", dto.TrangThai == 0 ? 0 : 1);
            p.Add("ghi_chu", CleanText(dto.GhiChu, 500));
            p.Add("nguoi", Me());

            await using var conn = new OracleConnection(_connString);
            var trung = await conn.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1) FROM dtntb_cv_nguoi_nhan
                WHERE loai = :loai AND NVL(ma_dv_ttvt, '#') = NVL(:ttvt, '#') AND UPPER(ma_nv) = UPPER(:ma_nv) AND id <> :id",
                new { loai, ttvt, ma_nv = maNv, id = dto.Id });
            if (trung > 0) throw new CvValidationException("Người nhận này đã có trong danh sách (cùng loại, cùng TTVT).");

            long id = dto.Id;
            if (id > 0)
            {
                p.Add("id", id);
                var n = await conn.ExecuteAsync(@"
                    UPDATE dtntb_cv_nguoi_nhan SET loai = :loai, ma_dv_ttvt = :ttvt, ten_dv_ttvt = :ten_ttvt, ma_nv = :ma_nv, ten_nv = :ten_nv,
                        ma_dv_ht = :ma_ht, ten_dv_ht = :ten_ht, thu_tu = :thu_tu, trang_thai = :tt, ghi_chu = :ghi_chu,
                        nguoi_cn = :nguoi, ngay_cn = SYSDATE
                    WHERE id = :id", p);
                if (n == 0) throw new CvValidationException("Không tìm thấy dòng người nhận.");
            }
            else
            {
                id = await conn.ExecuteScalarAsync<long>("SELECT seq_dtntb_cv_nguoi_nhan.NEXTVAL FROM dual");
                p.Add("id", id);
                await conn.ExecuteAsync(@"
                    INSERT INTO dtntb_cv_nguoi_nhan (id, loai, ma_dv_ttvt, ten_dv_ttvt, ma_nv, ten_nv, ma_dv_ht, ten_dv_ht, thu_tu,
                        trang_thai, ghi_chu, nguoi_cn, ngay_cn)
                    VALUES (:id, :loai, :ttvt, :ten_ttvt, :ma_nv, :ten_nv, :ma_ht, :ten_ht, :thu_tu, :tt, :ghi_chu, :nguoi, SYSDATE)", p);
            }
            return await conn.QueryFirstAsync<CvNguoiNhanDto>($"SELECT {NguoiNhanColumns} FROM dtntb_cv_nguoi_nhan n WHERE n.id = :id", new { id });
        }

        public async Task<List<CvNguoiNhanDto>> GetNguoiNhanTaoPhieuAsync(int dauViecId)
        {
            await using var conn = new OracleConnection(_connString);
            var dv = await GetDauViecActiveAsync(conn, dauViecId);
            return await NguoiNhanChoPhepAsync(conn, dv);
        }

        private async Task<CvDauViecDto> GetDauViecActiveAsync(OracleConnection conn, int dauViecId, IDbTransaction? tx = null)
        {
            var dv = await conn.QueryFirstOrDefaultAsync<CvDauViecDto>(
                $"SELECT {DauViecColumns} FROM dtntb_cv_dau_viec d WHERE d.dau_viec_id = :id AND d.is_active = 1", new { id = dauViecId }, tx);
            return dv ?? throw new CvValidationException("Chưa chọn đầu việc hoặc đầu việc đã ngừng sử dụng.");
        }

        /// <summary>Người nhận được phép chọn khi tạo phiếu với đầu việc này.</summary>
        private async Task<List<CvNguoiNhanDto>> NguoiNhanChoPhepAsync(OracleConnection conn, CvDauViecDto dv, IDbTransaction? tx = null)
        {
            var toanTinh = Co(AppPermissions.CV.TAO_TOAN_TINH);
            if (dv.LoaiPhieu == CvConst.LOAI_KTHT)
            {
                if (!toanTinh) throw new CvValidationException("Đầu việc KTHT chỉ dành cho thành viên tổ KTHT (quyền tạo phiếu toàn tỉnh).");
                return (await conn.QueryAsync<CvNguoiNhanDto>(
                    $"SELECT {NguoiNhanColumns} FROM dtntb_cv_nguoi_nhan n WHERE n.trang_thai = 1 AND n.loai = 'KTHT' ORDER BY n.thu_tu, n.ten_nv",
                    transaction: tx)).ToList();
            }

            if (toanTinh)
                return (await conn.QueryAsync<CvNguoiNhanDto>(
                    $"SELECT {NguoiNhanColumns} FROM dtntb_cv_nguoi_nhan n WHERE n.trang_thai = 1 AND n.loai = 'HT' ORDER BY n.ten_dv_ttvt, n.thu_tu, n.ten_nv",
                    transaction: tx)).ToList();

            if (!Co(AppPermissions.CV.TAO)) throw new CvValidationException("Bạn không có quyền tạo phiếu công việc.");
            var ttvt = TtvtCuaToi();
            if (ttvt == null) throw new CvValidationException("Tài khoản chưa có mã đơn vị nên không xác định được địa bàn.");
            var rows = (await conn.QueryAsync<CvNguoiNhanDto>(
                $"SELECT {NguoiNhanColumns} FROM dtntb_cv_nguoi_nhan n WHERE n.trang_thai = 1 AND n.loai = 'HT' AND TRIM(n.ma_dv_ttvt) = :ttvt ORDER BY n.thu_tu, n.ten_nv",
                new { ttvt }, tx)).ToList();
            if (rows.Count == 0) throw new CvValidationException($"Địa bàn {ttvt} chưa được gán tổ trưởng Hạ tầng nhận phiếu. Liên hệ quản trị.");
            return rows;
        }

        // ============================================================
        // PHIẾU: DANH SÁCH / CHI TIẾT
        // ============================================================
        /// <summary>Tổng hợp dòng xử lý theo phiếu (bỏ dòng đã huỷ).</summary>
        private const string CteXuLy = @"xl AS (
                SELECT x.phieu_id, COUNT(*) AS so_nguoi,
                       SUM(CASE WHEN x.trang_thai = 'CHO_DONG' THEN 1 ELSE 0 END) AS so_cho_dong,
                       SUM(CASE WHEN x.trang_thai = 'DA_DONG' THEN 1 ELSE 0 END) AS so_da_dong,
                       SUM(CASE WHEN x.ngay_yc_khoa IS NULL THEN 1 ELSE 0 END) AS so_chua_yc,
                       MAX(x.ngay_yc_khoa) AS yc_cuoi,
                       LISTAGG(SUBSTR(NVL(x.ten_nv, x.ma_nv), 1, 60), ', ') WITHIN GROUP (ORDER BY x.xu_ly_id) AS ds_nguoi
                FROM dtntb_cv_xu_ly x WHERE x.trang_thai <> 'DA_HUY'
                GROUP BY x.phieu_id
            ),
            fl AS (SELECT f.phieu_id, COUNT(*) AS so_file FROM dtntb_cv_file f WHERE f.loai = 'DINH_KEM' GROUP BY f.phieu_id)";

        /// <summary>Quá hạn: có hạn và (thời điểm mọi người đã yêu cầu khoá, chưa xong thì hiện tại) vượt hạn.</summary>
        private const string QuaHanExpr = @"CASE WHEN p.trang_thai <> 'DA_HUY' AND p.han_xl IS NOT NULL
                AND p.han_xl < NVL(CASE WHEN xl.so_nguoi > 0 AND xl.so_chua_yc = 0 THEN xl.yc_cuoi END, SYSDATE) THEN 1 ELSE 0 END";

        private const string PhieuColumns = @"p.phieu_id AS PhieuId, p.loai_phieu AS LoaiPhieu, p.tieu_de AS TieuDe, p.mo_ta AS MoTa,
            p.do_uu_tien AS DoUuTien, p.dau_viec_id AS DauViecId, p.ma_dau_viec AS MaDauViec, p.ten_dau_viec AS TenDauViec,
            p.sla_phut AS SlaPhut, p.don_gia AS DonGia, p.dvt AS Dvt, p.tinh_tg_chay AS TinhTgChay,
            p.nguoi_tao AS NguoiTao, p.ten_nguoi_tao AS TenNguoiTao, p.ma_dv_tao AS MaDvTao, p.ten_dv_tao AS TenDvTao,
            p.ngay_tao AS NgayTao, p.han_xl AS HanXl, p.ma_dv_ttvt AS MaDvTtvt, p.ten_dv_ttvt AS TenDvTtvt,
            p.ma_nv_nhan AS MaNvNhan, p.ten_nv_nhan AS TenNvNhan, p.ma_dv_ht AS MaDvHt, p.ten_dv_ht AS TenDvHt,
            p.trang_thai AS TrangThai, p.ngay_giao AS NgayGiao, p.ngay_hoan_thanh AS NgayHoanThanh, p.ly_do_tra AS LyDoTra,
            p.so_lan_tra AS SoLanTra, p.so_lan_chuyen AS SoLanChuyen,
            NVL(xl.so_nguoi, 0) AS SoNguoi, NVL(xl.so_cho_dong, 0) AS SoChoDong, NVL(xl.so_da_dong, 0) AS SoDaDong,
            xl.ds_nguoi AS DsNguoiXl,
            CASE WHEN xl.so_nguoi > 0 AND xl.so_chua_yc = 0 THEN xl.yc_cuoi END AS XongLuc,
            NVL(fl.so_file, 0) AS SoFile";

        /// <summary>Phạm vi xem báo cáo theo data_scope (tạo / nhận / xử lý thuộc đơn vị).</summary>
        private string PhamViSql(DynamicParameters p, string a)
        {
            switch (_currentUser.ScopeLevel)
            {
                case UserDataScopeLevel.ToanTinh:
                    return "1 = 1";
                case UserDataScopeLevel.DiaBan:
                    if (string.IsNullOrWhiteSpace(_currentUser.DiaBanId)) return "1 = 0";
                    p.Add("pv_diaban", _currentUser.DiaBanId.Trim());
                    return $@"(SUBSTR(TRIM({a}.ma_dv_tao), 1, 7) IN (SELECT SUBSTR(TRIM(db.ma_dv), 1, 7) FROM v_donvi_diaban db WHERE TRIM(TO_CHAR(db.diaban_id)) = :pv_diaban)
                              OR SUBSTR(TRIM({a}.ma_dv_ttvt), 1, 7) IN (SELECT SUBSTR(TRIM(db.ma_dv), 1, 7) FROM v_donvi_diaban db WHERE TRIM(TO_CHAR(db.diaban_id)) = :pv_diaban))";
                case UserDataScopeLevel.DonVi:
                {
                    var dv = _currentUser.MaDv7?.Trim();
                    if (string.IsNullOrEmpty(dv)) return "1 = 0";
                    p.Add("pv_dv", dv);
                    p.Add("pv_ttvt", TtvtCuaToi() ?? dv);
                    return $@"(SUBSTR(TRIM({a}.ma_dv_tao), 1, 7) = :pv_dv OR SUBSTR(TRIM({a}.ma_dv_ht), 1, 7) = :pv_dv
                              OR TRIM({a}.ma_dv_ttvt) = :pv_ttvt)";
                }
                case UserDataScopeLevel.ToQuanLy:
                {
                    var to = _currentUser.MaDv?.Trim();
                    if (string.IsNullOrEmpty(to)) return "1 = 0";
                    p.Add("pv_to", to);
                    return $"(TRIM({a}.ma_dv_ht) = :pv_to OR TRIM({a}.ma_dv_tao) = :pv_to)";
                }
                default:
                {
                    var me = MeSql(p, $"{a}.nguoi_tao");
                    var me2 = MeSql(p, $"{a}.ma_nv_nhan");
                    var me3 = MeSql(p, "z.ma_nv");
                    return $"({me} OR {me2} OR EXISTS (SELECT 1 FROM dtntb_cv_xu_ly z WHERE z.phieu_id = {a}.phieu_id AND {me3}))";
                }
            }
        }

        private (string Where, DynamicParameters P) BuildPhieuFilter(CvPhieuQueryDto q)
        {
            var p = new DynamicParameters();
            var where = new List<string>();
            var view = (q.View ?? "TAO").Trim().ToUpperInvariant();
            switch (view)
            {
                case "TAO": where.Add(MeSql(p, "p.nguoi_tao")); break;
                case "NHAN": where.Add(MeSql(p, "p.ma_nv_nhan")); break;
                case "TAT_CA":
                    if (!CoXemBaoCao) throw new CvValidationException("Bạn không có quyền xem báo cáo phiếu công việc.");
                    where.Add(PhamViSql(p, "p"));
                    break;
                default: throw new CvValidationException("Kiểu danh sách không hợp lệ.");
            }

            var tt = (q.TrangThai ?? "").Trim().ToUpperInvariant();
            switch (tt)
            {
                case "": case "ALL": break;
                case "MO": where.Add("p.trang_thai IN ('CHO_GIAO', 'BI_TRA', 'DANG_XL')"); break;
                case "CHO_DONG": where.Add("NVL(xl.so_cho_dong, 0) > 0"); break;
                case CvConst.CHO_GIAO: case CvConst.BI_TRA: case CvConst.DANG_XL: case CvConst.HOAN_THANH: case CvConst.DA_HUY:
                    where.Add("p.trang_thai = :f_tt"); p.Add("f_tt", tt); break;
                default: throw new CvValidationException("Trạng thái lọc không hợp lệ.");
            }

            var loai = (q.LoaiPhieu ?? "").Trim().ToUpperInvariant();
            if (loai is CvConst.LOAI_HT or CvConst.LOAI_KTHT) { where.Add("p.loai_phieu = :f_loai"); p.Add("f_loai", loai); }
            if (!string.IsNullOrWhiteSpace(q.MaDvHt)) { where.Add("TRIM(p.ma_dv_ht) = :f_ht"); p.Add("f_ht", q.MaDvHt.Trim()); }
            var tu = ParseNgay(q.TuNgay, "Từ ngày");
            var den = ParseNgay(q.DenNgay, "Đến ngày");
            if (tu.HasValue) { where.Add("p.ngay_tao >= :f_tu"); p.Add("f_tu", tu.Value, DbType.DateTime); }
            if (den.HasValue) { where.Add("p.ngay_tao < :f_den"); p.Add("f_den", den.Value.AddDays(1), DbType.DateTime); }
            var s = CleanText(q.Search, 100);
            if (s != null)
            {
                where.Add(@"(UPPER(p.tieu_de) LIKE :f_s OR UPPER(p.ten_dau_viec) LIKE :f_s OR UPPER(p.ten_nguoi_tao) LIKE :f_s
                            OR UPPER(p.ten_nv_nhan) LIKE :f_s OR UPPER(xl.ds_nguoi) LIKE :f_s OR TO_CHAR(p.phieu_id) = :f_id)");
                p.Add("f_s", "%" + s.ToUpperInvariant() + "%");
                p.Add("f_id", s.TrimStart('#'));
            }
            if (q.ChiQuaHan) where.Add(QuaHanExpr + " = 1");
            return (string.Join(" AND ", where), p);
        }

        public async Task<List<CvPhieuDto>> GetPhieuListAsync(CvPhieuQueryDto query)
        {
            var (where, p) = BuildPhieuFilter(query);
            await using var conn = new OracleConnection(_connString);
            var rows = await conn.QueryAsync<CvPhieuDto>($@"
                WITH {CteXuLy}
                SELECT * FROM (
                    SELECT {PhieuColumns}, {QuaHanExpr} AS QuaHanSo
                    FROM dtntb_cv_phieu p
                    LEFT JOIN xl ON xl.phieu_id = p.phieu_id
                    LEFT JOIN fl ON fl.phieu_id = p.phieu_id
                    WHERE {where}
                    ORDER BY p.ngay_tao DESC
                ) WHERE ROWNUM <= {MaxRows}", p, commandTimeout: 120);
            return rows.ToList();
        }

        private async Task<CvPhieuDto?> LoadPhieuAsync(OracleConnection conn, long phieuId, IDbTransaction? tx = null)
        {
            return await conn.QueryFirstOrDefaultAsync<CvPhieuDto>($@"
                WITH {CteXuLy}
                SELECT {PhieuColumns}, {QuaHanExpr} AS QuaHanSo
                FROM dtntb_cv_phieu p
                LEFT JOIN xl ON xl.phieu_id = p.phieu_id
                LEFT JOIN fl ON fl.phieu_id = p.phieu_id
                WHERE p.phieu_id = :id", new { id = phieuId }, tx);
        }

        private const string XuLyColumns = @"x.xu_ly_id AS XuLyId, x.phieu_id AS PhieuId, x.ma_nv AS MaNv, x.ten_nv AS TenNv, x.ma_dv AS MaDv,
            x.nguoi_giao AS NguoiGiao, x.ngay_giao AS NgayGiao, x.trang_thai AS TrangThai, x.so_luong AS SoLuong, x.ghi_chu_xl AS GhiChuXl,
            x.ngay_yc_khoa AS NgayYcKhoa, x.qua_han AS QuaHan, x.tg_chay_phut AS TgChayPhut, x.nguoi_dong AS NguoiDong, x.ngay_dong AS NgayDong,
            x.ly_do_tra AS LyDoTra, x.so_lan_tra AS SoLanTra, x.duyet_tt AS DuyetTt, x.so_luong_duyet AS SoLuongDuyet, x.don_gia AS DonGia,
            x.thanh_tien AS ThanhTien, x.thang_luong AS ThangLuong, x.nguoi_duyet AS NguoiDuyet, x.ten_nguoi_duyet AS TenNguoiDuyet,
            x.ngay_duyet AS NgayDuyet, x.ghi_chu_duyet AS GhiChuDuyet";

        private const string FileColumns = @"f.file_id AS FileId, f.phieu_id AS PhieuId, f.xu_ly_id AS XuLyId, f.loai AS Loai, f.ten_file AS TenFile,
            f.content_type AS ContentType, f.kich_thuoc AS KichThuoc, CASE WHEN f.thumb_key IS NULL THEN 0 ELSE 1 END AS CoThuNho,
            f.lat AS Lat, f.lng AS Lng, f.chup_luc AS ChupLuc, f.nguoi_tai AS NguoiTai, f.ngay_tai AS NgayTai";

        private async Task<bool> DuocXemAsync(OracleConnection conn, CvPhieuDto ph)
        {
            if (LaToi(ph.NguoiTao) || LaToi(ph.MaNvNhan) || CoXemBaoCao) return true;
            var p = new DynamicParameters();
            p.Add("id", ph.PhieuId);
            var me = MeSql(p, "x.ma_nv");
            return await conn.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM dtntb_cv_xu_ly x WHERE x.phieu_id = :id AND {me}", p) > 0;
        }

        public async Task<CvPhieuChiTietDto?> GetPhieuAsync(long phieuId)
        {
            await using var conn = new OracleConnection(_connString);
            var ph = await LoadPhieuAsync(conn, phieuId);
            if (ph == null || !await DuocXemAsync(conn, ph)) return null;

            var files = (await conn.QueryAsync<CvFileDto>(
                $"SELECT {FileColumns} FROM dtntb_cv_file f WHERE f.phieu_id = :id ORDER BY f.file_id", new { id = phieuId })).ToList();
            var xuLy = (await conn.QueryAsync<CvXuLyDto>(
                $"SELECT {XuLyColumns} FROM dtntb_cv_xu_ly x WHERE x.phieu_id = :id ORDER BY x.xu_ly_id", new { id = phieuId })).ToList();
            foreach (var x in xuLy) x.Anh = files.Where(f => f.XuLyId == x.XuLyId).ToList();
            var ls = (await conn.QueryAsync<CvLichSuDto>(@"
                SELECT l.ls_id AS LsId, l.xu_ly_id AS XuLyId, l.thao_tac AS ThaoTac, l.noi_dung AS NoiDung, l.nguoi AS Nguoi,
                       l.ten_nguoi AS TenNguoi, l.ngay AS Ngay
                FROM dtntb_cv_lich_su l WHERE l.phieu_id = :id ORDER BY l.ngay DESC, l.ls_id DESC", new { id = phieuId })).ToList();

            var laTao = LaToi(ph.NguoiTao);
            var laNhan = LaToi(ph.MaNvNhan);
            var mo = ph.TrangThai is CvConst.CHO_GIAO or CvConst.BI_TRA;
            var dieuPhoi = ph.TrangThai is CvConst.CHO_GIAO or CvConst.DANG_XL;
            var cuaToi = xuLy.FirstOrDefault(x => x.TrangThai != CvConst.XL_DA_HUY && LaToi(x.MaNv));
            return new CvPhieuChiTietDto
            {
                Phieu = ph,
                DinhKem = files.Where(f => f.Loai == CvConst.FILE_DINH_KEM).ToList(),
                XuLy = xuLy,
                LichSu = ls,
                Quyen = new CvQuyenPhieuDto
                {
                    LaNguoiTao = laTao,
                    LaNguoiNhan = laNhan,
                    SuaGuiLai = laTao && mo,
                    Huy = laTao && mo,
                    ThemDinhKem = laTao && mo,
                    Giao = (laNhan || CoQuanTri) && dieuPhoi,
                    Tra = laNhan && ph.TrangThai == CvConst.CHO_GIAO,
                    Chuyen = (laNhan || CoQuanTri) && dieuPhoi,
                    XuLyCuaToi = cuaToi?.XuLyId
                }
            };
        }

        // ============================================================
        // PHIẾU: TẠO / SỬA / HUỶ / TRẢ / CHUYỂN
        // ============================================================
        private (string TieuDe, string MoTa, string UuTien) KiemTraNoiDung(CvPhieuRequestDto r)
        {
            var tieuDe = CleanText(r.TieuDe, 300) ?? throw new CvValidationException("Chưa nhập tiêu đề phiếu.");
            var moTa = CleanText(r.MoTa, 4000) ?? throw new CvValidationException("Chưa nhập mô tả.");
            var uuTien = (r.DoUuTien ?? "BINH_THUONG").Trim().ToUpperInvariant();
            if (!CvConst.UU_TIEN.Contains(uuTien)) throw new CvValidationException("Độ ưu tiên không hợp lệ.");
            return (tieuDe, moTa, uuTien);
        }

        public async Task<CvPhieuDto> TaoPhieuAsync(CvPhieuRequestDto request)
        {
            var (tieuDe, moTa, uuTien) = KiemTraNoiDung(request);
            var me = Me();
            await using var conn = new OracleConnection(_connString);
            await conn.OpenAsync();
            var dv = await GetDauViecActiveAsync(conn, request.DauViecId);
            var nn = (await NguoiNhanChoPhepAsync(conn, dv)).FirstOrDefault(x => x.Id == request.NguoiNhanId)
                     ?? throw new CvValidationException("Người nhận không hợp lệ với đầu việc / địa bàn của bạn.");

            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                var id = await conn.ExecuteScalarAsync<long>("SELECT seq_dtntb_cv_phieu.NEXTVAL FROM dual", transaction: tx);
                var p = new DynamicParameters();
                p.Add("id", id);
                p.Add("loai", dv.LoaiPhieu);
                p.Add("tieu_de", tieuDe);
                p.Add("mo_ta", moTa);
                p.Add("uu_tien", uuTien);
                p.Add("dv_id", dv.DauViecId);
                p.Add("ma_dv_viec", dv.Ma);
                p.Add("ten_dv_viec", dv.Ten);
                p.Add("sla", dv.SlaPhut);
                p.Add("gia", dv.DonGia);
                p.Add("dvt", dv.Dvt);
                p.Add("chay", dv.TinhTgChay);
                p.Add("nguoi", me);
                p.Add("ten_nguoi", TenMe);
                p.Add("ma_dv_tao", Truncate(_currentUser.MaDv?.Trim(), 30));
                p.Add("ttvt", nn.MaDvTtvt);
                p.Add("ten_ttvt", nn.TenDvTtvt);
                p.Add("ma_nhan", nn.MaNv);
                p.Add("ten_nhan", nn.TenNv);
                p.Add("ma_ht", nn.MaDvHt);
                p.Add("ten_ht", nn.TenDvHt);
                await conn.ExecuteAsync(@"
                    INSERT INTO dtntb_cv_phieu (phieu_id, loai_phieu, tieu_de, mo_ta, do_uu_tien, dau_viec_id, ma_dau_viec, ten_dau_viec,
                        sla_phut, don_gia, dvt, tinh_tg_chay, nguoi_tao, ten_nguoi_tao, ma_dv_tao, ten_dv_tao, ngay_tao, han_xl,
                        ma_dv_ttvt, ten_dv_ttvt, ma_nv_nhan, ten_nv_nhan, ma_dv_ht, ten_dv_ht, trang_thai, ngay_cn)
                    VALUES (:id, :loai, :tieu_de, :mo_ta, :uu_tien, :dv_id, :ma_dv_viec, :ten_dv_viec,
                        :sla, :gia, :dvt, :chay, :nguoi, :ten_nguoi, :ma_dv_tao,
                        (SELECT MAX(t.ten_dv) FROM v_donvi t WHERE TRIM(t.ma_dv) = :ma_dv_tao), SYSDATE,
                        CASE WHEN :sla IS NULL THEN NULL ELSE SYSDATE + :sla / 1440 END,
                        :ttvt, :ten_ttvt, :ma_nhan, :ten_nhan, :ma_ht, :ten_ht, 'CHO_GIAO', SYSDATE)", p, tx);
                await GhiLichSuAsync(conn, tx, id, null, "TAO",
                    $"Tạo phiếu [{dv.Ma}] {dv.Ten}, gửi {nn.TenNv ?? nn.MaNv} ({nn.TenDvHt ?? nn.MaDvHt})");
                await tx.CommitAsync();
                return (await LoadPhieuAsync(conn, id))!;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        /// <summary>Khoá dòng phiếu trong giao dịch; null nếu không có.</summary>
        private static async Task<CvPhieuDto?> KhoaPhieuAsync(OracleConnection conn, IDbTransaction tx, long phieuId)
        {
            var id = await conn.ExecuteScalarAsync<long?>("SELECT phieu_id FROM dtntb_cv_phieu WHERE phieu_id = :id FOR UPDATE", new { id = phieuId }, tx);
            if (id == null) return null;
            return await conn.QueryFirstOrDefaultAsync<CvPhieuDto>($@"
                SELECT p.phieu_id AS PhieuId, p.loai_phieu AS LoaiPhieu, p.trang_thai AS TrangThai, p.nguoi_tao AS NguoiTao,
                       p.ma_nv_nhan AS MaNvNhan, p.ten_nv_nhan AS TenNvNhan, p.ma_dv_ht AS MaDvHt, p.ten_dv_ht AS TenDvHt,
                       p.ma_dv_ttvt AS MaDvTtvt, p.han_xl AS HanXl, p.ngay_tao AS NgayTao, p.don_gia AS DonGia, p.tinh_tg_chay AS TinhTgChay,
                       p.tieu_de AS TieuDe, p.ma_dau_viec AS MaDauViec, p.ten_dau_viec AS TenDauViec, p.dvt AS Dvt
                FROM dtntb_cv_phieu p WHERE p.phieu_id = :id", new { id = phieuId }, tx);
        }

        private async Task<T> TrongGiaoDichAsync<T>(Func<OracleConnection, IDbTransaction, Task<T>> work)
        {
            await using var conn = new OracleConnection(_connString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                var kq = await work(conn, tx);
                await tx.CommitAsync();
                return kq;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task<CvPhieuDto> SuaPhieuAsync(long phieuId, CvPhieuRequestDto request)
        {
            var (tieuDe, moTa, uuTien) = KiemTraNoiDung(request);
            await TrongGiaoDichAsync(async (conn, tx) =>
            {
                var ph = await KhoaPhieuAsync(conn, tx, phieuId) ?? throw new CvValidationException("Không tìm thấy phiếu.");
                if (!LaToi(ph.NguoiTao)) throw new CvValidationException("Chỉ người tạo được sửa phiếu.");
                if (ph.TrangThai is not (CvConst.CHO_GIAO or CvConst.BI_TRA))
                    throw new CvValidationException("Phiếu đã được giao, không sửa được nữa.");
                var dv = await GetDauViecActiveAsync(conn, request.DauViecId, tx);
                var nn = (await NguoiNhanChoPhepAsync(conn, dv, tx)).FirstOrDefault(x => x.Id == request.NguoiNhanId)
                         ?? throw new CvValidationException("Người nhận không hợp lệ với đầu việc / địa bàn của bạn.");
                var p = new DynamicParameters();
                p.Add("id", phieuId);
                p.Add("loai", dv.LoaiPhieu);
                p.Add("tieu_de", tieuDe);
                p.Add("mo_ta", moTa);
                p.Add("uu_tien", uuTien);
                p.Add("dv_id", dv.DauViecId);
                p.Add("ma_dv_viec", dv.Ma);
                p.Add("ten_dv_viec", dv.Ten);
                p.Add("sla", dv.SlaPhut);
                p.Add("gia", dv.DonGia);
                p.Add("dvt", dv.Dvt);
                p.Add("chay", dv.TinhTgChay);
                p.Add("ttvt", nn.MaDvTtvt);
                p.Add("ten_ttvt", nn.TenDvTtvt);
                p.Add("ma_nhan", nn.MaNv);
                p.Add("ten_nhan", nn.TenNv);
                p.Add("ma_ht", nn.MaDvHt);
                p.Add("ten_ht", nn.TenDvHt);
                // Hạn tính lại từ thời điểm tạo theo đầu việc mới.
                await conn.ExecuteAsync(@"
                    UPDATE dtntb_cv_phieu SET loai_phieu = :loai, tieu_de = :tieu_de, mo_ta = :mo_ta, do_uu_tien = :uu_tien,
                        dau_viec_id = :dv_id, ma_dau_viec = :ma_dv_viec, ten_dau_viec = :ten_dv_viec, sla_phut = :sla, don_gia = :gia,
                        dvt = :dvt, tinh_tg_chay = :chay,
                        han_xl = CASE WHEN :sla IS NULL THEN NULL ELSE ngay_tao + :sla / 1440 END,
                        ma_dv_ttvt = :ttvt, ten_dv_ttvt = :ten_ttvt, ma_nv_nhan = :ma_nhan, ten_nv_nhan = :ten_nhan,
                        ma_dv_ht = :ma_ht, ten_dv_ht = :ten_ht, trang_thai = 'CHO_GIAO', ngay_cn = SYSDATE
                    WHERE phieu_id = :id", p, tx);
                await GhiLichSuAsync(conn, tx, phieuId, null, ph.TrangThai == CvConst.BI_TRA ? "GUI_LAI" : "SUA",
                    (ph.TrangThai == CvConst.BI_TRA ? "Sửa và gửi lại" : "Sửa phiếu") + $": [{dv.Ma}] {dv.Ten}, gửi {nn.TenNv ?? nn.MaNv}");
                return true;
            });
            await using var c = new OracleConnection(_connString);
            return (await LoadPhieuAsync(c, phieuId))!;
        }

        public async Task HuyPhieuAsync(long phieuId, CvLyDoRequestDto request)
        {
            var lyDo = CleanText(request.LyDo, 1000);
            await TrongGiaoDichAsync(async (conn, tx) =>
            {
                var ph = await KhoaPhieuAsync(conn, tx, phieuId) ?? throw new CvValidationException("Không tìm thấy phiếu.");
                if (!LaToi(ph.NguoiTao)) throw new CvValidationException("Chỉ người tạo được huỷ phiếu.");
                if (ph.TrangThai is not (CvConst.CHO_GIAO or CvConst.BI_TRA))
                    throw new CvValidationException("Phiếu đã được giao cho người xử lý, không huỷ được.");
                await conn.ExecuteAsync("UPDATE dtntb_cv_phieu SET trang_thai = 'DA_HUY', ngay_cn = SYSDATE WHERE phieu_id = :id", new { id = phieuId }, tx);
                await GhiLichSuAsync(conn, tx, phieuId, null, "HUY", lyDo == null ? "Huỷ phiếu" : "Huỷ phiếu: " + lyDo);
                return true;
            });
        }

        public async Task TraPhieuAsync(long phieuId, CvLyDoRequestDto request)
        {
            var lyDo = CleanText(request.LyDo, 1000) ?? throw new CvValidationException("Chưa nhập lý do trả lại.");
            await TrongGiaoDichAsync(async (conn, tx) =>
            {
                var ph = await KhoaPhieuAsync(conn, tx, phieuId) ?? throw new CvValidationException("Không tìm thấy phiếu.");
                if (!LaToi(ph.MaNvNhan)) throw new CvValidationException("Chỉ người nhận phiếu được trả lại.");
                if (ph.TrangThai != CvConst.CHO_GIAO) throw new CvValidationException("Chỉ trả lại được phiếu chưa giao cho người xử lý.");
                await conn.ExecuteAsync(@"UPDATE dtntb_cv_phieu SET trang_thai = 'BI_TRA', ly_do_tra = :ly_do, so_lan_tra = so_lan_tra + 1,
                        ngay_cn = SYSDATE WHERE phieu_id = :id", new { id = phieuId, ly_do = lyDo }, tx);
                await GhiLichSuAsync(conn, tx, phieuId, null, "TRA", "Trả lại người tạo: " + lyDo);
                return true;
            });
        }

        public async Task ChuyenPhieuAsync(long phieuId, CvChuyenRequestDto request)
        {
            var ghiChu = CleanText(request.GhiChu, 500);
            await TrongGiaoDichAsync(async (conn, tx) =>
            {
                var ph = await KhoaPhieuAsync(conn, tx, phieuId) ?? throw new CvValidationException("Không tìm thấy phiếu.");
                if (!LaToi(ph.MaNvNhan) && !CoQuanTri) throw new CvValidationException("Chỉ người nhận phiếu (hoặc quản trị) được chuyển tổ.");
                if (ph.TrangThai is not (CvConst.CHO_GIAO or CvConst.DANG_XL))
                    throw new CvValidationException("Phiếu đã hoàn thành / huỷ / bị trả, không chuyển được.");
                var nn = await conn.QueryFirstOrDefaultAsync<CvNguoiNhanDto>(
                    $"SELECT {NguoiNhanColumns} FROM dtntb_cv_nguoi_nhan n WHERE n.id = :id AND n.trang_thai = 1", new { id = request.NguoiNhanId }, tx)
                    ?? throw new CvValidationException("Người nhận mới không hợp lệ.");
                if (string.Equals(nn.MaNv.Trim(), ph.MaNvNhan.Trim(), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(nn.MaDvHt.Trim(), ph.MaDvHt.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new CvValidationException("Phiếu đang ở đúng người nhận này.");
                await conn.ExecuteAsync(@"
                    UPDATE dtntb_cv_phieu SET ma_nv_nhan = :ma, ten_nv_nhan = :ten, ma_dv_ht = :ht, ten_dv_ht = :ten_ht,
                        ma_dv_ttvt = NVL(:ttvt, ma_dv_ttvt), ten_dv_ttvt = NVL(:ten_ttvt, ten_dv_ttvt),
                        so_lan_chuyen = so_lan_chuyen + 1, ngay_cn = SYSDATE
                    WHERE phieu_id = :id",
                    new { id = phieuId, ma = nn.MaNv, ten = nn.TenNv, ht = nn.MaDvHt, ten_ht = nn.TenDvHt, ttvt = nn.MaDvTtvt, ten_ttvt = nn.TenDvTtvt }, tx);
                await GhiLichSuAsync(conn, tx, phieuId, null, "CHUYEN_TO",
                    $"Chuyển từ {ph.TenNvNhan ?? ph.MaNvNhan} ({ph.TenDvHt ?? ph.MaDvHt}) sang {nn.TenNv ?? nn.MaNv} ({nn.TenDvHt ?? nn.MaDvHt})"
                    + (ghiChu == null ? "" : ". " + ghiChu));
                return true;
            });
        }

        // ============================================================
        // GIAO NGƯỜI XỬ LÝ
        // ============================================================
        /// <summary>Người nhận phiếu đang là người nhận KTHT (được giao cho bất kỳ ai).</summary>
        private static async Task<bool> NhanLaKthtAsync(OracleConnection conn, CvPhieuDto ph, IDbTransaction? tx = null)
        {
            return await conn.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1) FROM dtntb_cv_nguoi_nhan n
                WHERE n.loai = 'KTHT' AND n.trang_thai = 1 AND UPPER(TRIM(n.ma_nv)) = UPPER(TRIM(:ma)) AND ROWNUM = 1",
                new { ma = ph.MaNvNhan }, tx) > 0;
        }

        private const string NhanVienSql = @"
            WITH u0 AS (
                SELECT NVL(TRIM(u.ma_nv), TRIM(u.ma_nd)) AS ma, TRIM(u.ma_nd) AS ma_nd, u.ten_nv, TRIM(u.ma_dv) AS ma_dv,
                       ROW_NUMBER() OVER (PARTITION BY UPPER(NVL(TRIM(u.ma_nv), TRIM(u.ma_nd))) ORDER BY u.ma_nd) AS rn
                FROM v_nguoidung_diaban u
                WHERE u.trangthai_id = 1 AND NVL(TRIM(u.ma_nv), TRIM(u.ma_nd)) IS NOT NULL AND {0}
            )
            SELECT * FROM (
                SELECT u0.ma AS MaNv, u0.ten_nv AS TenNv, u0.ma_dv AS MaDv,
                       (SELECT MAX(t.ten_dv) FROM v_donvi t WHERE TRIM(t.ma_dv) = u0.ma_dv) AS TenDv
                FROM u0 WHERE u0.rn = 1
                ORDER BY u0.ten_nv
            ) WHERE ROWNUM <= 300";

        public async Task<List<CvNhanVienDto>> GetNhanVienGiaoAsync(long phieuId, string? search)
        {
            await using var conn = new OracleConnection(_connString);
            var ph = await LoadPhieuAsync(conn, phieuId) ?? throw new CvValidationException("Không tìm thấy phiếu.");
            if (!LaToi(ph.MaNvNhan) && !CoQuanTri) throw new CvValidationException("Chỉ người nhận phiếu được giao việc.");
            var s = CleanText(search, 60);
            var p = new DynamicParameters();
            string dk;
            if (await NhanLaKthtAsync(conn, ph) || CoQuanTri)
            {
                // Người nhận KTHT giao được cho bất kỳ ai: chưa gõ tìm kiếm thì gợi ý người trong tổ.
                if (s == null) { dk = "TRIM(u.ma_dv) LIKE :ht || '%'"; p.Add("ht", ph.MaDvHt.Trim()); }
                else
                {
                    dk = "(UPPER(u.ten_nv) LIKE :s OR UPPER(u.ma_nv) LIKE :s OR UPPER(u.ma_nd) LIKE :s)";
                    p.Add("s", "%" + s.ToUpperInvariant() + "%");
                }
            }
            else
            {
                dk = "TRIM(u.ma_dv) LIKE :ht || '%'";
                p.Add("ht", ph.MaDvHt.Trim());
                if (s != null)
                {
                    dk += " AND (UPPER(u.ten_nv) LIKE :s OR UPPER(u.ma_nv) LIKE :s OR UPPER(u.ma_nd) LIKE :s)";
                    p.Add("s", "%" + s.ToUpperInvariant() + "%");
                }
            }
            var rows = (await conn.QueryAsync<CvNhanVienDto>(string.Format(NhanVienSql, dk), p)).ToList();

            // Người nhận phiếu luôn tự giao cho mình được.
            if (!rows.Any(r => string.Equals(r.MaNv, ph.MaNvNhan.Trim(), StringComparison.OrdinalIgnoreCase)) && s == null)
                rows.Insert(0, new CvNhanVienDto { MaNv = ph.MaNvNhan.Trim(), TenNv = ph.TenNvNhan, MaDv = ph.MaDvHt, TenDv = ph.TenDvHt });
            return rows;
        }

        public async Task GiaoAsync(long phieuId, CvGiaoRequestDto request)
        {
            var ds = (request.MaNv ?? new List<string>()).Select(x => (x ?? "").Trim()).Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (ds.Count == 0) throw new CvValidationException("Chưa chọn người xử lý.");
            if (ds.Count > 30) throw new CvValidationException("Mỗi lần giao tối đa 30 người.");
            var ghiChu = CleanText(request.GhiChu, 500);
            var me = Me();

            await TrongGiaoDichAsync(async (conn, tx) =>
            {
                var ph = await KhoaPhieuAsync(conn, tx, phieuId) ?? throw new CvValidationException("Không tìm thấy phiếu.");
                if (!LaToi(ph.MaNvNhan) && !CoQuanTri) throw new CvValidationException("Chỉ người nhận phiếu được giao việc.");
                if (ph.TrangThai is not (CvConst.CHO_GIAO or CvConst.DANG_XL))
                    throw new CvValidationException("Phiếu không ở trạng thái giao được.");
                var tuDo = CoQuanTri || await NhanLaKthtAsync(conn, ph, tx);

                var daGiao = new List<string>();
                foreach (var ma in ds)
                {
                    var trung = await conn.ExecuteScalarAsync<int>(@"
                        SELECT COUNT(1) FROM dtntb_cv_xu_ly WHERE phieu_id = :id AND trang_thai <> 'DA_HUY' AND UPPER(TRIM(ma_nv)) = UPPER(:ma)",
                        new { id = phieuId, ma }, tx);
                    if (trung > 0) continue;

                    CvNhanVienDto? nv;
                    if (string.Equals(ma, ph.MaNvNhan.Trim(), StringComparison.OrdinalIgnoreCase))
                        nv = new CvNhanVienDto { MaNv = ph.MaNvNhan.Trim(), TenNv = ph.TenNvNhan, MaDv = ph.MaDvHt };
                    else
                    {
                        var p = new DynamicParameters();
                        p.Add("ma", ma.ToUpperInvariant());
                        var dk = "(UPPER(TRIM(u.ma_nv)) = :ma OR UPPER(TRIM(u.ma_nd)) = :ma)";
                        if (!tuDo) { dk += " AND TRIM(u.ma_dv) LIKE :ht || '%'"; p.Add("ht", ph.MaDvHt.Trim()); }
                        nv = (await conn.QueryAsync<CvNhanVienDto>(string.Format(NhanVienSql, dk), p, tx)).FirstOrDefault();
                    }
                    if (nv == null) throw new CvValidationException($"Nhân viên {ma} không thuộc tổ {ph.TenDvHt ?? ph.MaDvHt} hoặc đã nghỉ.");

                    var xlId = await conn.ExecuteScalarAsync<long>("SELECT seq_dtntb_cv_xu_ly.NEXTVAL FROM dual", transaction: tx);
                    await conn.ExecuteAsync(@"
                        INSERT INTO dtntb_cv_xu_ly (xu_ly_id, phieu_id, ma_nv, ten_nv, ma_dv, nguoi_giao, ngay_giao, trang_thai, duyet_tt, don_gia)
                        VALUES (:xl, :id, :ma, :ten, :dv, :nguoi, SYSDATE, 'DANG_LAM', 'CHUA', :gia)",
                        new { xl = xlId, id = phieuId, ma = nv.MaNv, ten = Truncate(nv.TenNv, 200), dv = Truncate(nv.MaDv, 30), nguoi = me, gia = ph.DonGia }, tx);
                    daGiao.Add(nv.TenNv ?? nv.MaNv);
                }
                if (daGiao.Count == 0) throw new CvValidationException("Những người đã chọn đều đang xử lý phiếu này.");

                await conn.ExecuteAsync(@"UPDATE dtntb_cv_phieu SET trang_thai = 'DANG_XL', ngay_giao = NVL(ngay_giao, SYSDATE),
                        ngay_hoan_thanh = NULL, ngay_cn = SYSDATE WHERE phieu_id = :id", new { id = phieuId }, tx);
                await GhiLichSuAsync(conn, tx, phieuId, null, "GIAO", "Giao cho " + string.Join(", ", daGiao) + (ghiChu == null ? "" : ". " + ghiChu));
                return true;
            });
        }

        public async Task HuyGiaoAsync(long xuLyId)
        {
            await TrongGiaoDichAsync(async (conn, tx) =>
            {
                var x = await LoadXuLyAsync(conn, xuLyId, tx) ?? throw new CvValidationException("Không tìm thấy người xử lý.");
                var ph = await KhoaPhieuAsync(conn, tx, x.PhieuId) ?? throw new CvValidationException("Không tìm thấy phiếu.");
                if (!LaToi(ph.MaNvNhan) && !CoQuanTri) throw new CvValidationException("Chỉ người nhận phiếu được bỏ giao.");
                var n = await conn.ExecuteAsync(
                    "UPDATE dtntb_cv_xu_ly SET trang_thai = 'DA_HUY' WHERE xu_ly_id = :id AND trang_thai = 'DANG_LAM'", new { id = xuLyId }, tx);
                if (n == 0) throw new CvValidationException("Chỉ bỏ giao được người chưa yêu cầu khoá.");
                await GhiLichSuAsync(conn, tx, x.PhieuId, xuLyId, "HUY_GIAO", "Bỏ giao " + (x.TenNv ?? x.MaNv));
                await CapNhatTrangThaiPhieuAsync(conn, tx, x.PhieuId);
                return true;
            });
        }

        /// <summary>Tính lại trạng thái phiếu theo các dòng xử lý còn hiệu lực.</summary>
        private static async Task CapNhatTrangThaiPhieuAsync(OracleConnection conn, IDbTransaction tx, long phieuId)
        {
            await conn.ExecuteAsync(@"
                UPDATE dtntb_cv_phieu p SET
                    trang_thai = CASE
                        WHEN (SELECT COUNT(*) FROM dtntb_cv_xu_ly x WHERE x.phieu_id = p.phieu_id AND x.trang_thai <> 'DA_HUY') = 0 THEN 'CHO_GIAO'
                        WHEN (SELECT COUNT(*) FROM dtntb_cv_xu_ly x WHERE x.phieu_id = p.phieu_id AND x.trang_thai NOT IN ('DA_HUY', 'DA_DONG')) = 0 THEN 'HOAN_THANH'
                        ELSE 'DANG_XL' END,
                    ngay_hoan_thanh = CASE
                        WHEN (SELECT COUNT(*) FROM dtntb_cv_xu_ly x WHERE x.phieu_id = p.phieu_id AND x.trang_thai <> 'DA_HUY') > 0
                         AND (SELECT COUNT(*) FROM dtntb_cv_xu_ly x WHERE x.phieu_id = p.phieu_id AND x.trang_thai NOT IN ('DA_HUY', 'DA_DONG')) = 0
                        THEN NVL(p.ngay_hoan_thanh, SYSDATE) END,
                    ngay_cn = SYSDATE
                WHERE p.phieu_id = :id AND p.trang_thai IN ('DANG_XL', 'HOAN_THANH', 'CHO_GIAO')", new { id = phieuId }, tx);
        }

        private static Task<CvXuLyDto?> LoadXuLyAsync(OracleConnection conn, long xuLyId, IDbTransaction? tx = null) =>
            conn.QueryFirstOrDefaultAsync<CvXuLyDto?>($"SELECT {XuLyColumns} FROM dtntb_cv_xu_ly x WHERE x.xu_ly_id = :id", new { id = xuLyId }, tx);

        // ============================================================
        // XỬ LÝ / ĐÓNG
        // ============================================================
        private const string ViecColumns = XuLyColumns + @", p.loai_phieu AS LoaiPhieu, p.tieu_de AS TieuDe, p.ma_dau_viec AS MaDauViec,
            p.ten_dau_viec AS TenDauViec, p.dvt AS Dvt, p.don_gia AS DonGiaPhieu, p.han_xl AS HanXl, p.ngay_tao AS NgayTao,
            p.ten_nguoi_tao AS TenNguoiTao, p.ten_dv_tao AS TenDvTao, p.ma_dv_ht AS MaDvHt, p.ten_dv_ht AS TenDvHt,
            p.ten_nv_nhan AS TenNvNhan, p.trang_thai AS TrangThaiPhieu, p.do_uu_tien AS DoUuTien,
            (SELECT COUNT(*) FROM dtntb_cv_file f WHERE f.xu_ly_id = x.xu_ly_id) AS SoAnh";

        public async Task<List<CvViecDto>> GetViecCuaToiAsync(string? tab)
        {
            var p = new DynamicParameters();
            var me = MeSql(p, "x.ma_nv");
            var dk = (tab ?? "DANG").Trim().ToUpperInvariant() switch
            {
                "CHO" => "x.trang_thai = 'CHO_DONG'",
                "XONG" => "x.trang_thai = 'DA_DONG' AND x.ngay_dong >= TRUNC(SYSDATE) - 90",
                _ => "x.trang_thai = 'DANG_LAM'"
            };
            await using var conn = new OracleConnection(_connString);
            var rows = await conn.QueryAsync<CvViecDto>($@"
                SELECT * FROM (
                    SELECT {ViecColumns}
                    FROM dtntb_cv_xu_ly x JOIN dtntb_cv_phieu p ON p.phieu_id = x.phieu_id
                    WHERE {me} AND {dk} AND p.trang_thai <> 'DA_HUY'
                    ORDER BY NVL(p.han_xl, DATE '2999-12-31'), x.ngay_giao DESC
                ) WHERE ROWNUM <= {MaxRows}", p);
            return rows.ToList();
        }

        public async Task YeuCauKhoaAsync(long xuLyId, CvYeuCauKhoaRequestDto request)
        {
            var ghiChu = CleanText(request.GhiChu, 2000);
            await TrongGiaoDichAsync(async (conn, tx) =>
            {
                var x = await LoadXuLyAsync(conn, xuLyId, tx) ?? throw new CvValidationException("Không tìm thấy việc được giao.");
                if (!LaToi(x.MaNv)) throw new CvValidationException("Việc này không giao cho bạn.");
                var ph = await KhoaPhieuAsync(conn, tx, x.PhieuId) ?? throw new CvValidationException("Không tìm thấy phiếu.");
                if (ph.TrangThai != CvConst.DANG_XL) throw new CvValidationException("Phiếu không còn ở trạng thái xử lý.");
                if (x.TrangThai != CvConst.XL_DANG_LAM) throw new CvValidationException("Bạn đã yêu cầu khoá việc này rồi.");
                var soAnh = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM dtntb_cv_file WHERE xu_ly_id = :id", new { id = xuLyId }, tx);
                if (soAnh == 0) throw new CvValidationException("Cần chụp ít nhất 1 ảnh minh chứng trước khi yêu cầu khoá.");
                if (request.SoLuong is < 0 or > 100000) throw new CvValidationException("Số lượng không hợp lệ.");
                if (ph.DonGia is > 0 && request.SoLuong is not > 0)
                    throw new CvValidationException($"Đầu việc có đơn giá, cần nhập số lượng ({ph.Dvt ?? "đơn vị"}) đã xử lý.");

                var n = await conn.ExecuteAsync(@"
                    UPDATE dtntb_cv_xu_ly SET trang_thai = 'CHO_DONG', so_luong = :sl, ghi_chu_xl = :gc, ngay_yc_khoa = SYSDATE,
                        qua_han = (SELECT CASE WHEN p.han_xl IS NULL THEN NULL WHEN SYSDATE > p.han_xl THEN 1 ELSE 0 END
                                   FROM dtntb_cv_phieu p WHERE p.phieu_id = dtntb_cv_xu_ly.phieu_id),
                        tg_chay_phut = (SELECT CASE WHEN p.tinh_tg_chay = 1 THEN ROUND((SYSDATE - p.ngay_tao) * 1440) END
                                        FROM dtntb_cv_phieu p WHERE p.phieu_id = dtntb_cv_xu_ly.phieu_id),
                        ly_do_tra = NULL
                    WHERE xu_ly_id = :id AND trang_thai = 'DANG_LAM'",
                    new { id = xuLyId, sl = request.SoLuong, gc = ghiChu }, tx);
                if (n == 0) throw new CvValidationException("Bạn đã yêu cầu khoá việc này rồi.");
                await GhiLichSuAsync(conn, tx, x.PhieuId, xuLyId, "YC_KHOA",
                    "Yêu cầu khoá" + (request.SoLuong.HasValue ? $", số lượng {request.SoLuong:0.###} {ph.Dvt}" : "")
                    + (ph.HanXl.HasValue && DateTime.Now > ph.HanXl.Value ? " (quá hạn)" : "") + (ghiChu == null ? "" : ". " + ghiChu));
                return true;
            });
        }

        public async Task DongAsync(long xuLyId)
        {
            await TrongGiaoDichAsync(async (conn, tx) =>
            {
                var x = await LoadXuLyAsync(conn, xuLyId, tx) ?? throw new CvValidationException("Không tìm thấy người xử lý.");
                var ph = await KhoaPhieuAsync(conn, tx, x.PhieuId) ?? throw new CvValidationException("Không tìm thấy phiếu.");
                if (!LaToi(ph.NguoiTao)) throw new CvValidationException("Chỉ người tạo phiếu được đóng.");
                var n = await conn.ExecuteAsync(@"UPDATE dtntb_cv_xu_ly SET trang_thai = 'DA_DONG', nguoi_dong = :nguoi, ngay_dong = SYSDATE
                        WHERE xu_ly_id = :id AND trang_thai = 'CHO_DONG'", new { id = xuLyId, nguoi = Me() }, tx);
                if (n == 0) throw new CvValidationException("Người này chưa yêu cầu khoá hoặc đã được đóng.");
                await GhiLichSuAsync(conn, tx, x.PhieuId, xuLyId, "DONG", "Đóng việc của " + (x.TenNv ?? x.MaNv));
                await CapNhatTrangThaiPhieuAsync(conn, tx, x.PhieuId);
                return true;
            });
        }

        public async Task TraLaiXuLyAsync(long xuLyId, CvLyDoRequestDto request)
        {
            var lyDo = CleanText(request.LyDo, 1000) ?? throw new CvValidationException("Chưa nhập lý do trả lại.");
            await TrongGiaoDichAsync(async (conn, tx) =>
            {
                var x = await LoadXuLyAsync(conn, xuLyId, tx) ?? throw new CvValidationException("Không tìm thấy người xử lý.");
                var ph = await KhoaPhieuAsync(conn, tx, x.PhieuId) ?? throw new CvValidationException("Không tìm thấy phiếu.");
                if (!LaToi(ph.NguoiTao)) throw new CvValidationException("Chỉ người tạo phiếu được trả lại.");
                var n = await conn.ExecuteAsync(@"UPDATE dtntb_cv_xu_ly SET trang_thai = 'DANG_LAM', ly_do_tra = :ly_do, so_lan_tra = so_lan_tra + 1,
                        ngay_yc_khoa = NULL, qua_han = NULL, tg_chay_phut = NULL
                        WHERE xu_ly_id = :id AND trang_thai = 'CHO_DONG'", new { id = xuLyId, ly_do = lyDo }, tx);
                if (n == 0) throw new CvValidationException("Chỉ trả lại được người đang chờ đóng.");
                await GhiLichSuAsync(conn, tx, x.PhieuId, xuLyId, "TRA_LAI_XL", $"Trả lại {x.TenNv ?? x.MaNv} làm tiếp: {lyDo}");
                return true;
            });
        }

        // ============================================================
        // FILE
        // ============================================================
        public async Task<CvFileDto> UploadDinhKemAsync(long phieuId, IFormFile file, CancellationToken ct = default)
        {
            if (file == null || file.Length == 0) throw new CvValidationException("Chưa chọn file.");
            if (file.Length > _maxFileBytes) throw new CvValidationException($"File \"{file.FileName}\" quá lớn (tối đa {_maxFileBytes / 1024 / 1024} MB).");
            var ext = Path.GetExtension(file.FileName ?? "").ToLowerInvariant();
            if (!LoaiFileChoPhep.TryGetValue(ext, out var contentType))
                throw new CvValidationException("Chỉ nhận file PDF, Word, Excel hoặc ảnh JPG/PNG.");

            await using var conn = new OracleConnection(_connString);
            await conn.OpenAsync(ct);
            var ph = await LoadPhieuAsync(conn, phieuId) ?? throw new CvValidationException("Không tìm thấy phiếu.");
            if (!LaToi(ph.NguoiTao)) throw new CvValidationException("Chỉ người tạo được đính kèm file.");
            if (ph.TrangThai is not (CvConst.CHO_GIAO or CvConst.BI_TRA)) throw new CvValidationException("Phiếu đã giao, không thêm file được.");
            if (ph.SoFile >= MaxDinhKem) throw new CvValidationException($"Mỗi phiếu tối đa {MaxDinhKem} file đính kèm.");

            var bytes = await DocFileAsync(file, ct);
            if (!DungDinhDang(bytes, ext)) throw new CvValidationException($"Nội dung file \"{file.FileName}\" không đúng định dạng {ext}.");
            byte[]? thumb = null;
            if (contentType.StartsWith("image/", StringComparison.Ordinal))
            {
                KiemTraAnh(bytes, file.FileName);
                bytes = Reencode(bytes);
                contentType = "image/jpeg";
                ext = ".jpg";
                thumb = TaoThuNho(bytes);
            }

            var fileId = await conn.ExecuteScalarAsync<long>("SELECT seq_dtntb_cv_file.NEXTVAL FROM dual");
            var key = $"{DateTime.Now:yyyy-MM}/{phieuId}/dk_{fileId}{ext}";
            var thumbKey = thumb == null ? null : $"{DateTime.Now:yyyy-MM}/{phieuId}/dk_{fileId}_t.jpg";
            await _store.SaveAsync(key, bytes, contentType, ct);
            if (thumb != null) await LuuNhoAsync(thumbKey!, thumb, ct);

            await conn.ExecuteAsync(@"
                INSERT INTO dtntb_cv_file (file_id, phieu_id, xu_ly_id, loai, ten_file, object_key, thumb_key, content_type, kich_thuoc, sha256, nguoi_tai, ngay_tai)
                VALUES (:id, :pid, NULL, 'DINH_KEM', :ten, :k, :tk, :ct, :kt, :sha, :nguoi, SYSDATE)",
                new
                {
                    id = fileId, pid = phieuId, ten = TenFileAnToan(file.FileName, ext), k = key, tk = thumbKey, ct = contentType,
                    kt = (long)bytes.Length, sha = Sha(bytes), nguoi = Me()
                });
            return (await conn.QueryFirstAsync<CvFileDto>($"SELECT {FileColumns} FROM dtntb_cv_file f WHERE f.file_id = :id", new { id = fileId }));
        }

        public async Task<List<CvFileDto>> UploadAnhAsync(long xuLyId, IReadOnlyList<IFormFile> files, string? metaJson, CancellationToken ct = default)
        {
            var list = (files ?? Array.Empty<IFormFile>()).Where(f => f is { Length: > 0 }).ToList();
            if (list.Count == 0) throw new CvValidationException("Chưa chọn ảnh.");
            if (list.Count > MaxAnhMoiLan) throw new CvValidationException($"Mỗi lần tải tối đa {MaxAnhMoiLan} ảnh.");
            List<CvAnhMetaDto> metas;
            try
            {
                metas = string.IsNullOrWhiteSpace(metaJson)
                    ? new List<CvAnhMetaDto>()
                    : JsonSerializer.Deserialize<List<CvAnhMetaDto>>(metaJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            }
            catch (JsonException)
            {
                metas = new List<CvAnhMetaDto>();
            }

            await using var conn = new OracleConnection(_connString);
            await conn.OpenAsync(ct);
            var x = await LoadXuLyAsync(conn, xuLyId) ?? throw new CvValidationException("Không tìm thấy việc được giao.");
            if (!LaToi(x.MaNv)) throw new CvValidationException("Việc này không giao cho bạn.");
            if (x.TrangThai != CvConst.XL_DANG_LAM) throw new CvValidationException("Đã yêu cầu khoá, không tải thêm ảnh được.");
            var daCo = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM dtntb_cv_file WHERE xu_ly_id = :id", new { id = xuLyId });
            if (daCo + list.Count > MaxAnhMoiNguoi) throw new CvValidationException($"Mỗi người tối đa {MaxAnhMoiNguoi} ảnh minh chứng.");

            var result = new List<CvFileDto>();
            for (var i = 0; i < list.Count; i++)
            {
                var meta = i < metas.Count ? metas[i] : new CvAnhMetaDto();
                var raw = await DocFileAsync(list[i], ct);
                if (!LaJpeg(raw) && !LaPng(raw)) throw new CvValidationException($"\"{list[i].FileName}\" không phải ảnh JPEG/PNG.");
                KiemTraAnh(raw, list[i].FileName);
                var jpeg = Reencode(raw);
                var thumb = TaoThuNho(jpeg);

                double? lat = meta.Lat is >= -90 and <= 90 && meta.Lng is >= -180 and <= 180 && !(meta.Lat == 0 && meta.Lng == 0)
                    ? Math.Round(meta.Lat!.Value, 7) : null;
                double? lng = lat.HasValue ? Math.Round(meta.Lng!.Value, 7) : null;
                DateTime? chupLuc = DateTime.TryParse(meta.ChupLuc, CultureInfo.InvariantCulture, DateTimeStyles.None, out var cl) ? cl : null;

                var fileId = await conn.ExecuteScalarAsync<long>("SELECT seq_dtntb_cv_file.NEXTVAL FROM dual");
                var key = $"{DateTime.Now:yyyy-MM}/{x.PhieuId}/mc_{xuLyId}_{fileId}.jpg";
                var thumbKey = thumb == null ? null : key[..^4] + "_t.jpg";
                await _store.SaveAsync(key, jpeg, "image/jpeg", ct);
                if (thumb != null) await LuuNhoAsync(thumbKey!, thumb, ct);
                try
                {
                    var ins = new DynamicParameters();
                    ins.Add("id", fileId);
                    ins.Add("pid", x.PhieuId);
                    ins.Add("xl", xuLyId);
                    ins.Add("ten", TenFileAnToan(meta.FileName ?? list[i].FileName, ".jpg"));
                    ins.Add("k", key);
                    ins.Add("tk", thumbKey);
                    ins.Add("kt", (long)jpeg.Length);
                    ins.Add("sha", Sha(raw));
                    ins.Add("lat", lat);
                    ins.Add("lng", lng);
                    ins.Add("chup", chupLuc, DbType.DateTime);
                    ins.Add("nguoi", Me());
                    await conn.ExecuteAsync(@"
                        INSERT INTO dtntb_cv_file (file_id, phieu_id, xu_ly_id, loai, ten_file, object_key, thumb_key, content_type, kich_thuoc,
                            sha256, lat, lng, chup_luc, nguoi_tai, ngay_tai)
                        VALUES (:id, :pid, :xl, 'MINH_CHUNG', :ten, :k, :tk, 'image/jpeg', :kt, :sha, :lat, :lng, :chup, :nguoi, SYSDATE)", ins);
                }
                catch
                {
                    await XoaObjectAsync(key, thumbKey);
                    throw;
                }
                result.Add(await conn.QueryFirstAsync<CvFileDto>($"SELECT {FileColumns} FROM dtntb_cv_file f WHERE f.file_id = :id", new { id = fileId }));
            }
            return result;
        }

        private sealed class FileRow
        {
            public long FileId { get; set; }
            public long PhieuId { get; set; }
            public long? XuLyId { get; set; }
            public string Loai { get; set; } = string.Empty;
            public string TenFile { get; set; } = string.Empty;
            public string ObjectKey { get; set; } = string.Empty;
            public string? ThumbKey { get; set; }
            public string? ContentType { get; set; }
        }

        private static Task<FileRow?> LoadFileAsync(OracleConnection conn, long fileId) =>
            conn.QueryFirstOrDefaultAsync<FileRow?>(@"
                SELECT file_id AS FileId, phieu_id AS PhieuId, xu_ly_id AS XuLyId, loai AS Loai, ten_file AS TenFile,
                       object_key AS ObjectKey, thumb_key AS ThumbKey, content_type AS ContentType
                FROM dtntb_cv_file WHERE file_id = :id", new { id = fileId });

        public async Task XoaFileAsync(long fileId, CancellationToken ct = default)
        {
            await using var conn = new OracleConnection(_connString);
            await conn.OpenAsync(ct);
            var f = await LoadFileAsync(conn, fileId) ?? throw new CvValidationException("Không tìm thấy file.");
            if (f.Loai == CvConst.FILE_DINH_KEM)
            {
                var ph = await LoadPhieuAsync(conn, f.PhieuId) ?? throw new CvValidationException("Không tìm thấy phiếu.");
                if (!LaToi(ph.NguoiTao)) throw new CvValidationException("Chỉ người tạo được xoá file đính kèm.");
                if (ph.TrangThai is not (CvConst.CHO_GIAO or CvConst.BI_TRA)) throw new CvValidationException("Phiếu đã giao, không xoá file được.");
            }
            else
            {
                var x = await LoadXuLyAsync(conn, f.XuLyId ?? 0) ?? throw new CvValidationException("Không tìm thấy việc được giao.");
                if (!LaToi(x.MaNv)) throw new CvValidationException("Chỉ người xử lý được xoá ảnh của mình.");
                if (x.TrangThai != CvConst.XL_DANG_LAM) throw new CvValidationException("Đã yêu cầu khoá, không xoá ảnh được.");
            }
            await conn.ExecuteAsync("DELETE FROM dtntb_cv_file WHERE file_id = :id", new { id = fileId });
            await XoaObjectAsync(f.ObjectKey, f.ThumbKey);
        }

        public async Task<(Stream Stream, string ContentType, string FileName)?> GetFileAsync(long fileId, bool thuNho, CancellationToken ct = default)
        {
            await using var conn = new OracleConnection(_connString);
            await conn.OpenAsync(ct);
            var f = await LoadFileAsync(conn, fileId);
            if (f == null) return null;
            var ph = await LoadPhieuAsync(conn, f.PhieuId);
            if (ph == null || !await DuocXemAsync(conn, ph)) return null;

            if (thuNho && !string.IsNullOrEmpty(f.ThumbKey))
            {
                var nho = await _store.GetAsync(f.ThumbKey, ct);
                if (nho != null) return (nho, "image/jpeg", f.TenFile);
            }
            var s = await _store.GetAsync(f.ObjectKey, ct);
            if (s == null) return null;
            return (s, f.ContentType ?? "application/octet-stream", f.TenFile);
        }

        private async Task XoaObjectAsync(string? key, string? thumbKey)
        {
            foreach (var k in new[] { key, thumbKey })
            {
                if (string.IsNullOrWhiteSpace(k)) continue;
                try { await _store.DeleteAsync(k); }
                catch (Exception ex) { _logger.LogWarning(ex, "Không xoá được file phiếu công việc {Key}", k); }
            }
        }

        private async Task LuuNhoAsync(string key, byte[] thumb, CancellationToken ct)
        {
            try { await _store.SaveAsync(key, thumb, "image/jpeg", ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Không lưu được ảnh thu nhỏ {Key}", key);
            }
        }

        private static async Task<byte[]> DocFileAsync(IFormFile file, CancellationToken ct)
        {
            await using var input = file.OpenReadStream();
            using var ms = new MemoryStream((int)Math.Min(file.Length, int.MaxValue));
            await input.CopyToAsync(ms, ct);
            return ms.ToArray();
        }

        private static bool LaJpeg(byte[] b) => b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;
        private static bool LaPng(byte[] b) => b.Length >= 8 && b.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        /// <summary>Kiểm tra chữ ký file theo phần mở rộng (chặn đổi đuôi file thực thi).</summary>
        private static bool DungDinhDang(byte[] b, string ext) => ext switch
        {
            ".pdf" => b.Length >= 5 && b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46,                 // %PDF
            ".docx" or ".xlsx" => b.Length >= 4 && b[0] == 0x50 && b[1] == 0x4B && b[2] == 0x03 && b[3] == 0x04,      // ZIP
            ".doc" or ".xls" => b.Length >= 8 && b.AsSpan(0, 8).SequenceEqual(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }),
            ".jpg" or ".jpeg" => LaJpeg(b),
            ".png" => LaPng(b),
            _ => false
        };

        private static void KiemTraAnh(byte[] bytes, string? name)
        {
            try
            {
                var info = new MagickImageInfo(bytes);
                if (info.Width == 0 || info.Height == 0 || (ulong)info.Width * info.Height > 60_000_000UL)
                    throw new CvValidationException($"Kích thước ảnh \"{name}\" không hợp lệ.");
            }
            catch (MagickException)
            {
                throw new CvValidationException($"Không đọc được ảnh \"{name}\".");
            }
        }

        private byte[] Reencode(byte[] source)
        {
            using var image = new MagickImage(source);
            image.AutoOrient();
            image.Resize(new MagickGeometry((uint)_maxDimension, (uint)_maxDimension) { Greater = true });
            image.Strip();
            image.Quality = (uint)_jpegQuality;
            image.Format = MagickFormat.Jpeg;
            return image.ToByteArray();
        }

        private static byte[]? TaoThuNho(byte[] jpeg)
        {
            try
            {
                using var image = new MagickImage(jpeg);
                image.AutoOrient();
                image.Resize(new MagickGeometry((uint)ThuNhoPx, (uint)ThuNhoPx) { Greater = true });
                image.Strip();
                image.Quality = 70;
                image.Format = MagickFormat.Jpeg;
                return image.ToByteArray();
            }
            catch (MagickException)
            {
                return null;
            }
        }

        private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

        private static string TenFileAnToan(string? name, string ext)
        {
            var n = Path.GetFileName(name ?? "").Trim();
            n = new string(n.Where(c => !char.IsControl(c) && c is not ('/' or '\\' or '"' or '<' or '>' or '|' or ':' or '*' or '?')).ToArray());
            if (string.IsNullOrWhiteSpace(n)) n = "file" + ext;
            return n.Length > 200 ? n[..200] : n;
        }

        // ============================================================
        // DUYỆT TÍNH LƯƠNG
        // ============================================================
        private static int ThangNay() => DateTime.Now.Year * 100 + DateTime.Now.Month;

        public async Task<List<CvViecDto>> GetDuyetAsync(CvDuyetQueryDto query)
        {
            if (!Co(AppPermissions.CV.DUYET)) throw new CvValidationException("Bạn không có quyền duyệt tính lương.");
            var p = new DynamicParameters();
            var where = new List<string> { "x.trang_thai = 'DA_DONG'", PhamViSql(p, "p") };
            var tt = (query.DuyetTt ?? CvConst.DUYET_CHUA).Trim().ToUpperInvariant();
            if (tt is CvConst.DUYET_CHUA or CvConst.DUYET_DA or CvConst.DUYET_KHONG) { where.Add("x.duyet_tt = :tt"); p.Add("tt", tt); }
            if (query.ThangLuong is > 200000 and < 300000 && tt != CvConst.DUYET_CHUA) { where.Add("x.thang_luong = :thang"); p.Add("thang", query.ThangLuong); }
            if (!string.IsNullOrWhiteSpace(query.MaDvHt)) { where.Add("TRIM(p.ma_dv_ht) = :ht"); p.Add("ht", query.MaDvHt.Trim()); }
            var loai = (query.LoaiPhieu ?? "").Trim().ToUpperInvariant();
            if (loai is CvConst.LOAI_HT or CvConst.LOAI_KTHT) { where.Add("p.loai_phieu = :loai"); p.Add("loai", loai); }
            var s = CleanText(query.Search, 100);
            if (s != null)
            {
                where.Add("(UPPER(x.ten_nv) LIKE :s OR UPPER(x.ma_nv) LIKE :s OR UPPER(p.tieu_de) LIKE :s OR UPPER(p.ten_dau_viec) LIKE :s OR TO_CHAR(p.phieu_id) = :sid)");
                p.Add("s", "%" + s.ToUpperInvariant() + "%");
                p.Add("sid", s.TrimStart('#'));
            }
            await using var conn = new OracleConnection(_connString);
            var rows = await conn.QueryAsync<CvViecDto>($@"
                SELECT * FROM (
                    SELECT {ViecColumns}
                    FROM dtntb_cv_xu_ly x JOIN dtntb_cv_phieu p ON p.phieu_id = x.phieu_id
                    WHERE {string.Join(" AND ", where)}
                    ORDER BY p.ten_dv_ht, x.ten_nv, x.ngay_dong
                ) WHERE ROWNUM <= 5000", p, commandTimeout: 120);
            return rows.ToList();
        }

        public async Task<CvDuyetKetQuaDto> DuyetAsync(CvDuyetRequestDto request)
        {
            if (!Co(AppPermissions.CV.DUYET)) throw new CvValidationException("Bạn không có quyền duyệt tính lương.");
            var ds = (request.Dong ?? new List<CvDuyetDongDto>()).Where(d => d.XuLyId > 0).GroupBy(d => d.XuLyId).Select(g => g.Last()).ToList();
            if (ds.Count == 0) throw new CvValidationException("Chưa chọn dòng nào để duyệt.");
            if (ds.Count > 2000) throw new CvValidationException("Mỗi lần duyệt tối đa 2000 dòng.");
            var me = Me();
            var ten = TenMe;
            var thang = ThangNay();
            var kq = new CvDuyetKetQuaDto();

            await TrongGiaoDichAsync(async (conn, tx) =>
            {
                foreach (var d in ds)
                {
                    if (d.SoLuong is < 0 or > 100000) { kq.BoQua++; kq.GhiChu.Add($"Dòng {d.XuLyId}: số lượng không hợp lệ."); continue; }
                    var gc = CleanText(d.GhiChu, 1000);
                    // Đơn giá lấy từ phiếu (đã chụp lúc tạo); chỉ duyệt dòng đã đóng, chưa duyệt hoặc duyệt trong tháng này.
                    var n = await conn.ExecuteAsync(@"
                        UPDATE dtntb_cv_xu_ly x SET
                            duyet_tt = :tt,
                            so_luong_duyet = NVL(:sl, x.so_luong),
                            don_gia = (SELECT p.don_gia FROM dtntb_cv_phieu p WHERE p.phieu_id = x.phieu_id),
                            thanh_tien = CASE WHEN :tt = 'DA_DUYET'
                                THEN ROUND(NVL(NVL(:sl, x.so_luong), 0) * NVL((SELECT p.don_gia FROM dtntb_cv_phieu p WHERE p.phieu_id = x.phieu_id), 0))
                                ELSE 0 END,
                            thang_luong = :thang, nguoi_duyet = :nguoi, ten_nguoi_duyet = :ten, ngay_duyet = SYSDATE, ghi_chu_duyet = :gc
                        WHERE x.xu_ly_id = :id AND x.trang_thai = 'DA_DONG'
                          AND (x.duyet_tt = 'CHUA' OR x.thang_luong = :thang)",
                        new
                        {
                            id = d.XuLyId, tt = d.TinhLuong ? CvConst.DUYET_DA : CvConst.DUYET_KHONG, sl = d.SoLuong,
                            thang, nguoi = me, ten, gc
                        }, tx);
                    if (n == 0)
                    {
                        kq.BoQua++;
                        kq.GhiChu.Add($"Dòng {d.XuLyId}: chưa đóng hoặc đã duyệt ở tháng trước.");
                        continue;
                    }
                    var phieuId = await conn.ExecuteScalarAsync<long>("SELECT phieu_id FROM dtntb_cv_xu_ly WHERE xu_ly_id = :id", new { id = d.XuLyId }, tx);
                    await GhiLichSuAsync(conn, tx, phieuId, d.XuLyId, "DUYET",
                        (d.TinhLuong ? "Duyệt tính lương" : "Không tính lương") + (d.SoLuong.HasValue ? $", số lượng {d.SoLuong:0.###}" : "")
                        + $" (tháng {thang % 100:00}/{thang / 100})" + (gc == null ? "" : ". " + gc));
                    kq.ThanhCong++;
                }
                return true;
            });
            if (kq.GhiChu.Count > 20) kq.GhiChu = kq.GhiChu.Take(20).Append($"... và {kq.GhiChu.Count - 20} dòng khác.").ToList();
            return kq;
        }

        // ============================================================
        // BÁO CÁO
        // ============================================================
        public async Task<List<CvBaoCaoToDto>> GetBaoCaoToAsync(string? ngay)
        {
            if (!CoXemBaoCao) throw new CvValidationException("Bạn không có quyền xem báo cáo.");
            var d = ParseNgay(ngay, "Ngày") ?? DateTime.Today;
            var p = new DynamicParameters();
            p.Add("d0", d.Date, DbType.DateTime);
            p.Add("d1", d.Date.AddDays(1), DbType.DateTime);
            p.Add("m0", new DateTime(d.Year, d.Month, 1), DbType.DateTime);
            var pv = PhamViSql(p, "p");
            await using var conn = new OracleConnection(_connString);
            var rows = await conn.QueryAsync<CvBaoCaoToDto>($@"
                WITH {CteXuLy},
                b AS (
                    SELECT p.ma_dv_ht, p.ten_dv_ht, p.ngay_tao, p.trang_thai, {QuaHanExpr} AS qh
                    FROM dtntb_cv_phieu p
                    LEFT JOIN xl ON xl.phieu_id = p.phieu_id
                    WHERE p.trang_thai <> 'DA_HUY' AND {pv}
                      AND (p.ngay_tao >= :m0 OR p.trang_thai IN ('CHO_GIAO', 'BI_TRA', 'DANG_XL'))
                )
                SELECT TRIM(b.ma_dv_ht) AS MaDvHt, MAX(b.ten_dv_ht) AS TenDvHt,
                       SUM(CASE WHEN b.ngay_tao >= :d0 AND b.ngay_tao < :d1 THEN 1 ELSE 0 END) AS SoPhieuNgay,
                       SUM(CASE WHEN b.ngay_tao >= :d0 AND b.ngay_tao < :d1 AND b.qh = 1 THEN 1 ELSE 0 END) AS QuaHanNgay,
                       SUM(CASE WHEN b.ngay_tao >= :m0 AND b.ngay_tao < :d1 THEN 1 ELSE 0 END) AS SoPhieuThang,
                       SUM(CASE WHEN b.ngay_tao >= :m0 AND b.ngay_tao < :d1 AND b.qh = 1 THEN 1 ELSE 0 END) AS QuaHanThang,
                       SUM(CASE WHEN b.trang_thai IN ('CHO_GIAO', 'BI_TRA', 'DANG_XL') THEN 1 ELSE 0 END) AS DangXuLy,
                       SUM(CASE WHEN b.ngay_tao >= :m0 AND b.ngay_tao < :d1 AND b.trang_thai = 'HOAN_THANH' THEN 1 ELSE 0 END) AS HoanThanhThang
                FROM b
                GROUP BY TRIM(b.ma_dv_ht)
                ORDER BY MAX(b.ten_dv_ht)", p, commandTimeout: 120);
            return rows.ToList();
        }

        public async Task<List<CvBaoCaoLuongDto>> GetBaoCaoLuongAsync(int thang, string? maDvHt)
        {
            if (!CoXemBaoCao) throw new CvValidationException("Bạn không có quyền xem báo cáo.");
            if (thang is < 200001 or > 299912) throw new CvValidationException("Tháng không hợp lệ.");
            var p = new DynamicParameters();
            p.Add("thang", thang);
            var where = new List<string> { "x.duyet_tt = 'DA_DUYET'", "x.thang_luong = :thang", PhamViSql(p, "p") };
            if (!string.IsNullOrWhiteSpace(maDvHt)) { where.Add("TRIM(p.ma_dv_ht) = :ht"); p.Add("ht", maDvHt.Trim()); }
            await using var conn = new OracleConnection(_connString);
            var rows = await conn.QueryAsync<CvBaoCaoLuongDto>($@"
                SELECT TRIM(x.ma_nv) AS MaNv, MAX(x.ten_nv) AS TenNv, MAX(TRIM(p.ma_dv_ht)) AS MaDvHt, MAX(p.ten_dv_ht) AS TenDvHt,
                       COUNT(*) AS SoViec,
                       SUM(CASE WHEN p.loai_phieu = 'KTHT' THEN 1 ELSE 0 END) AS SoViecKtht,
                       NVL(SUM(x.thanh_tien), 0) AS TongTien,
                       NVL(SUM(CASE WHEN p.loai_phieu = 'KTHT' THEN x.thanh_tien END), 0) AS TienKtht
                FROM dtntb_cv_xu_ly x JOIN dtntb_cv_phieu p ON p.phieu_id = x.phieu_id
                WHERE {string.Join(" AND ", where)}
                GROUP BY TRIM(x.ma_nv)
                ORDER BY MAX(p.ten_dv_ht), MAX(x.ten_nv)", p, commandTimeout: 120);
            return rows.ToList();
        }

        public async Task<byte[]> ExportPhieuAsync(CvPhieuQueryDto query)
        {
            var rows = await GetPhieuListAsync(query);
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Phieu");
            var head = new[]
            {
                "STT", "Mã phiếu", "Loại", "Tiêu đề", "Đầu việc", "Ưu tiên", "Người tạo", "Đơn vị tạo", "Ngày tạo", "Hạn XL",
                "Người nhận", "Tổ xử lý", "Người xử lý", "Trạng thái", "Xong lúc", "Quá hạn", "Đơn giá", "ĐVT", "Mô tả"
            };
            Header(ws, head);
            var r = 2;
            foreach (var x in rows)
            {
                ws.Cell(r, 1).Value = r - 1;
                ws.Cell(r, 2).Value = x.PhieuId;
                ws.Cell(r, 3).Value = x.LoaiPhieu == CvConst.LOAI_KTHT ? "Khai báo KTHT" : "Hạ tầng";
                ws.Cell(r, 4).Value = Text(x.TieuDe);
                ws.Cell(r, 5).Value = Text(x.TenDauViec);
                ws.Cell(r, 6).Value = TenUuTien(x.DoUuTien);
                ws.Cell(r, 7).Value = Text(x.TenNguoiTao ?? x.NguoiTao);
                ws.Cell(r, 8).Value = Text(x.TenDvTao ?? x.MaDvTao);
                SetDate(ws.Cell(r, 9), x.NgayTao);
                SetDate(ws.Cell(r, 10), x.HanXl);
                ws.Cell(r, 11).Value = Text(x.TenNvNhan ?? x.MaNvNhan);
                ws.Cell(r, 12).Value = Text(x.TenDvHt ?? x.MaDvHt);
                ws.Cell(r, 13).Value = Text(x.DsNguoiXl);
                ws.Cell(r, 14).Value = TenTrangThai(x.TrangThai);
                SetDate(ws.Cell(r, 15), x.XongLuc);
                ws.Cell(r, 16).Value = x.QuaHan ? "Quá hạn" : "";
                if (x.DonGia.HasValue) ws.Cell(r, 17).Value = x.DonGia.Value;
                ws.Cell(r, 18).Value = Text(x.Dvt);
                ws.Cell(r, 19).Value = Text(x.MoTa);
                r++;
            }
            ws.Column(17).Style.NumberFormat.Format = "#,##0";
            Finish(ws, r, head.Length);
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public async Task<byte[]> ExportLuongAsync(int thang, string? maDvHt)
        {
            var tong = await GetBaoCaoLuongAsync(thang, maDvHt);
            var ct = await GetDuyetChiTietLuongAsync(thang, maDvHt);
            using var wb = new XLWorkbook();

            var ws = wb.Worksheets.Add("TongHop");
            var h1 = new[] { "STT", "Mã NV", "Họ tên", "Tổ", "Số việc", "Thành tiền", "Trong đó KTHT (số việc)", "Trong đó KTHT (tiền)" };
            Header(ws, h1);
            var r = 2;
            foreach (var x in tong)
            {
                ws.Cell(r, 1).Value = r - 1;
                ws.Cell(r, 2).Value = Text(x.MaNv);
                ws.Cell(r, 3).Value = Text(x.TenNv);
                ws.Cell(r, 4).Value = Text(x.TenDvHt ?? x.MaDvHt);
                ws.Cell(r, 5).Value = x.SoViec;
                ws.Cell(r, 6).Value = x.TongTien;
                ws.Cell(r, 7).Value = x.SoViecKtht;
                ws.Cell(r, 8).Value = x.TienKtht;
                r++;
            }
            ws.Cell(r, 3).Value = "Tổng cộng";
            ws.Cell(r, 5).Value = tong.Sum(x => x.SoViec);
            ws.Cell(r, 6).Value = tong.Sum(x => x.TongTien);
            ws.Cell(r, 7).Value = tong.Sum(x => x.SoViecKtht);
            ws.Cell(r, 8).Value = tong.Sum(x => x.TienKtht);
            ws.Row(r).Style.Font.Bold = true;
            ws.Columns(6, 8).Style.NumberFormat.Format = "#,##0";
            Finish(ws, r + 1, h1.Length);

            var ws2 = wb.Worksheets.Add("ChiTiet");
            var h2 = new[]
            {
                "STT", "Mã phiếu", "Loại", "Đầu việc", "Tiêu đề", "Mã NV", "Họ tên", "Tổ", "Ngày tạo", "Yêu cầu khoá", "Quá hạn",
                "SL báo", "SL duyệt", "ĐVT", "Đơn giá", "Thành tiền", "Người duyệt", "Ngày duyệt", "Ghi chú duyệt"
            };
            Header(ws2, h2);
            r = 2;
            foreach (var x in ct)
            {
                ws2.Cell(r, 1).Value = r - 1;
                ws2.Cell(r, 2).Value = x.PhieuId;
                ws2.Cell(r, 3).Value = x.LoaiPhieu == CvConst.LOAI_KTHT ? "Khai báo KTHT" : "Hạ tầng";
                ws2.Cell(r, 4).Value = Text(x.TenDauViec);
                ws2.Cell(r, 5).Value = Text(x.TieuDe);
                ws2.Cell(r, 6).Value = Text(x.MaNv);
                ws2.Cell(r, 7).Value = Text(x.TenNv);
                ws2.Cell(r, 8).Value = Text(x.TenDvHt ?? x.MaDvHt);
                SetDate(ws2.Cell(r, 9), x.NgayTao);
                SetDate(ws2.Cell(r, 10), x.NgayYcKhoa);
                ws2.Cell(r, 11).Value = x.QuaHan == 1 ? "Quá hạn" : "";
                if (x.SoLuong.HasValue) ws2.Cell(r, 12).Value = x.SoLuong.Value;
                if (x.SoLuongDuyet.HasValue) ws2.Cell(r, 13).Value = x.SoLuongDuyet.Value;
                ws2.Cell(r, 14).Value = Text(x.Dvt);
                if (x.DonGia.HasValue) ws2.Cell(r, 15).Value = x.DonGia.Value;
                if (x.ThanhTien.HasValue) ws2.Cell(r, 16).Value = x.ThanhTien.Value;
                ws2.Cell(r, 17).Value = Text(x.TenNguoiDuyet ?? x.NguoiDuyet);
                SetDate(ws2.Cell(r, 18), x.NgayDuyet);
                ws2.Cell(r, 19).Value = Text(x.GhiChuDuyet);
                r++;
            }
            ws2.Columns(15, 16).Style.NumberFormat.Format = "#,##0";
            Finish(ws2, r, h2.Length);

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        private async Task<List<CvViecDto>> GetDuyetChiTietLuongAsync(int thang, string? maDvHt)
        {
            var p = new DynamicParameters();
            p.Add("thang", thang);
            var where = new List<string> { "x.duyet_tt = 'DA_DUYET'", "x.thang_luong = :thang", PhamViSql(p, "p") };
            if (!string.IsNullOrWhiteSpace(maDvHt)) { where.Add("TRIM(p.ma_dv_ht) = :ht"); p.Add("ht", maDvHt.Trim()); }
            await using var conn = new OracleConnection(_connString);
            var rows = await conn.QueryAsync<CvViecDto>($@"
                SELECT {ViecColumns}
                FROM dtntb_cv_xu_ly x JOIN dtntb_cv_phieu p ON p.phieu_id = x.phieu_id
                WHERE {string.Join(" AND ", where)}
                ORDER BY p.ten_dv_ht, x.ten_nv, p.phieu_id", p, commandTimeout: 120);
            return rows.ToList();
        }

        // ============================================================
        // TIỆN ÍCH
        // ============================================================
        private async Task GhiLichSuAsync(OracleConnection conn, IDbTransaction tx, long phieuId, long? xuLyId, string thaoTac, string noiDung)
        {
            await conn.ExecuteAsync(@"
                INSERT INTO dtntb_cv_lich_su (ls_id, phieu_id, xu_ly_id, thao_tac, noi_dung, nguoi, ten_nguoi, ngay)
                VALUES (seq_dtntb_cv_lich_su.NEXTVAL, :pid, :xl, :tt, :nd, :nguoi, :ten, SYSDATE)",
                new { pid = phieuId, xl = xuLyId, tt = thaoTac, nd = Truncate(noiDung, 2000), nguoi = Me(), ten = TenMe }, tx);
        }

        private static DateTime? ParseNgay(string? s, string label)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (DateTime.TryParseExact(s.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d.Date;
            throw new CvValidationException($"{label} không hợp lệ.");
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
            return value[0] is '=' or '+' or '-' or '@' ? "'" + value : value;
        }

        private static void SetDate(IXLCell cell, DateTime? value)
        {
            if (!value.HasValue) return;
            cell.Value = value.Value;
            cell.Style.DateFormat.Format = "HH:mm dd/MM/yyyy";
        }

        private static void Header(IXLWorksheet ws, string[] head)
        {
            for (var i = 0; i < head.Length; i++) ws.Cell(1, i + 1).Value = head[i];
            var range = ws.Range(1, 1, 1, head.Length);
            range.Style.Font.Bold = true;
            range.Style.Font.FontColor = XLColor.White;
            range.Style.Fill.BackgroundColor = XLColor.FromHtml("#0b6bcb");
        }

        private static void Finish(IXLWorksheet ws, int lastRow, int cols)
        {
            ws.Columns(1, cols).AdjustToContents(1, Math.Min(lastRow, 300), 6, 60);
            ws.SheetView.FreezeRows(1);
        }

        private static string TenTrangThai(string tt) => tt switch
        {
            CvConst.CHO_GIAO => "Chờ giao",
            CvConst.BI_TRA => "Bị trả lại",
            CvConst.DANG_XL => "Đang xử lý",
            CvConst.HOAN_THANH => "Hoàn thành",
            CvConst.DA_HUY => "Đã huỷ",
            _ => tt
        };

        private static string TenUuTien(string u) => u switch
        {
            "THAP" => "Thấp",
            "CAO" => "Cao",
            "KHAN_CAP" => "Khẩn cấp",
            _ => "Bình thường"
        };
    }
}
