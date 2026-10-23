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
    /// 5S nhà trạm.
    ///  - Giao (NAMS.GIAO): chọn trạm trong DTNTB_CSHT (phạm vi) giao cho một nhân viên; mỗi trạm chỉ 1 phiếu đang giao.
    ///  - Thực hiện (NAMS.THUC_HIEN): nhân viên được giao tải ảnh theo tiêu chí (chọn ảnh có sẵn), nộp khi đủ ảnh bắt buộc.
    ///    Nộp xong phiếu rời khỏi danh sách của nhân viên.
    ///  - Chấm (NAMS.CHAM): chấm điểm từng tiêu chí, tổng điểm + xếp loại; chấm xong là xong (được sửa lại điểm).
    ///  - Báo cáo (NAMS.VIEW / EXPORT): tổng hợp đơn vị -> tổ, chi tiết phiếu, Excel.
    /// Phạm vi theo data_scope: TOAN_TINH; DIA_BAN (v_donvi_diaban); DON_VI (7 ký tự đầu ma_dv); TO_QL (ma_toql = toàn bộ
    /// ma_dv của người dùng); NHAN_VIEN (phiếu của mình; trạm thuộc tổ của mình).
    /// Ảnh lưu MinIO bucket riêng ttht-5s, khoá "yyyy-MM/{phieu}_{anh}.jpg" (ảnh cũ "5s/..." nằm ở bucket điểm danh).
    /// </summary>
    public class NamSService : INamSService
    {
        private const int MaxTramRows = 5000;
        private const int MaxGiaoMotLan = 500;
        private const int MaxExportRows = 50000;
        private static readonly System.Text.RegularExpressions.Regex ImageKeyPattern =
            new(@"^(5s/)?\d{4}-\d{2}/\d+_\d+\.jpg$", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly StringComparer Vi = StringComparer.Create(new CultureInfo("vi-VN"), true);

        private readonly string _connString;
        private readonly ICurrentUserService _currentUser;
        private readonly INamSImageStore _imageStore;
        private readonly ILogger<NamSService> _logger;
        private readonly long _maxImageBytes;
        private readonly int _maxDimension;
        private readonly int _jpegQuality;
        private readonly int _maxAnhMoiLan;
        private readonly int _maxAnhMoiPhieu;
        private readonly int _xaTramMet;
        private readonly List<NamSXepLoaiDto> _xepLoai;

        public NamSService(IConfiguration config, ICurrentUserService currentUser, INamSImageStore imageStore, ILogger<NamSService> logger)
        {
            _connString = config.GetConnectionString("ConnectionString_NBH") ?? string.Empty;
            _currentUser = currentUser;
            _imageStore = imageStore;
            _logger = logger;

            int Int(string key, int fallback, int min, int max)
                => int.TryParse(config[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, min, max) : fallback;
            decimal Dec(string key, decimal fallback)
                => decimal.TryParse(config[key], NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, 0, 100) : fallback;

            _maxImageBytes = Int("Upload:MaxFileSizeInMB", 10, 1, 20) * 1024L * 1024L;
            _maxDimension = Int("NamS:MaxDimension", 2048, 800, 4096);
            _jpegQuality = Int("NamS:JpegQuality", 85, 60, 95);
            _maxAnhMoiLan = Int("NamS:MaxImagesPerUpload", 10, 1, 30);
            _maxAnhMoiPhieu = Int("NamS:MaxImagesPerPhieu", 60, 5, 300);
            _xaTramMet = Int("NamS:FarFromSiteMeters", 300, 50, 20000);
            // Thang xếp loại tạm (theo % điểm); sửa trong appsettings mục NamS:XepLoai mà không cần build lại.
            _xepLoai = new List<NamSXepLoaiDto>
            {
                new() { Ma = "TOT", Ten = "Tốt", TuPhanTram = Dec("NamS:XepLoai:Tot", 90) },
                new() { Ma = "KHA", Ten = "Khá", TuPhanTram = Dec("NamS:XepLoai:Kha", 75) },
                new() { Ma = "TB", Ten = "Trung bình", TuPhanTram = Dec("NamS:XepLoai:TrungBinh", 50) },
                new() { Ma = "KEM", Ten = "Kém", TuPhanTram = 0 }
            };
        }

        // ============================================================
        // DANH MỤC
        // ============================================================
        public async Task<List<NamSTieuChiDto>> GetTieuChiAsync()
        {
            await using var conn = new OracleConnection(_connString);
            return (await conn.QueryAsync<NamSTieuChiDto>($"SELECT {TieuChiColumns} FROM dtntb_5s_tieuchi t WHERE t.is_active = 1 ORDER BY t.thu_tu, t.tieuchi_id")).ToList();
        }

        public IReadOnlyList<NamSXepLoaiDto> GetXepLoai() => _xepLoai;

        public async Task<List<NamSOptionDto>> GetDonViAsync()
        {
            var p = new DynamicParameters();
            var scope = TramScope(p, "c");
            await using var conn = new OracleConnection(_connString);
            var rows = await conn.QueryAsync<NamSOptionDto>($@"
                SELECT SUBSTR(TRIM(c.ma_dv), 1, 7) AS Ma, MAX(c.ten_dv) AS Ten
                FROM dtntb_csht c
                WHERE c.ma_dv IS NOT NULL AND {scope}
                GROUP BY SUBSTR(TRIM(c.ma_dv), 1, 7)", p);
            return rows.OrderBy(x => x.Ten ?? x.Ma, Vi).ToList();
        }

        public async Task<List<NamSOptionDto>> GetToAsync(string? maDv)
        {
            var p = new DynamicParameters();
            var where = new List<string> { TramScope(p, "c"), "c.ma_toql IS NOT NULL" };
            AddMaDvFilter(where, p, "c.ma_dv", maDv);
            await using var conn = new OracleConnection(_connString);
            var rows = await conn.QueryAsync<NamSOptionDto>($@"
                SELECT TRIM(c.ma_toql) AS Ma, MAX(c.ten_toql) AS Ten
                FROM dtntb_csht c WHERE {string.Join(" AND ", where)}
                GROUP BY TRIM(c.ma_toql)", p);
            return rows.OrderBy(x => x.Ten ?? x.Ma, Vi).ToList();
        }

        // ============================================================
        // GIAO TRẠM
        // ============================================================
        public async Task<List<NamSTramDto>> GetTramAsync(NamSTramQueryDto query)
        {
            var p = new DynamicParameters();
            var where = new List<string> { TramScope(p, "c") };
            AddTramFilters(where, p, query);
            p.Add("thang", ThangHienTai());

            await using var conn = new OracleConnection(_connString);
            var rows = await conn.QueryAsync<NamSTramDto>($@"
                SELECT * FROM (
                    SELECT TRIM(c.ma_csht) AS MaCsht, c.ten_csht AS TenCsht, TRIM(c.ma_toql) AS MaToQl, c.ten_toql AS TenToQl,
                           TRIM(c.ma_dv) AS MaDv, c.ten_dv AS TenDv, c.phuongxa AS PhuongXa, c.diachi AS DiaChi,
                           o.phieu_id AS PhieuMoId, o.ten_nv_nhan AS NvMo, o.ma_nd_nhan AS MaNdMo, o.ngay_giao AS NgayGiaoMo,
                           (SELECT COUNT(1) FROM dtntb_5s_phieu x
                             WHERE x.ma_csht = TRIM(c.ma_csht) AND x.thang = :thang AND x.trang_thai <> 'THU_HOI') AS SoLanThang,
                           l.phieu_id AS PhieuGanNhatId, l.trang_thai AS TrangThaiGanNhat, l.tong_diem AS DiemGanNhat,
                           l.xep_loai AS XepLoaiGanNhat, l.ngay_nop AS NgayNopGanNhat
                    FROM dtntb_csht c
                    LEFT JOIN (SELECT p.*, ROW_NUMBER() OVER (PARTITION BY p.ma_csht ORDER BY p.ngay_giao DESC, p.phieu_id DESC) AS rn
                               FROM dtntb_5s_phieu p WHERE p.trang_thai = 'DA_GIAO') o
                           ON o.ma_csht = TRIM(c.ma_csht) AND o.rn = 1
                    LEFT JOIN (SELECT p.*, ROW_NUMBER() OVER (PARTITION BY p.ma_csht ORDER BY p.ngay_nop DESC, p.phieu_id DESC) AS rn
                               FROM dtntb_5s_phieu p WHERE p.trang_thai IN ('DA_NOP', 'DA_CHAM')) l
                           ON l.ma_csht = TRIM(c.ma_csht) AND l.rn = 1
                    WHERE {string.Join(" AND ", where)}
                    ORDER BY c.ten_dv, c.ten_toql, c.ten_csht
                ) WHERE ROWNUM <= {MaxTramRows}", p, commandTimeout: 120);
            return rows.ToList();
        }

        public async Task<List<NamSNhanVienDto>> GetNhanVienAsync(string? maDv, string? maToQl)
        {
            var p = new DynamicParameters();
            var where = new List<string> { UserScope(p, "u"), "u.ma_nd IS NOT NULL", "u.trangthai_id = 1" };
            AddMaDvFilter(where, p, "u.ma_dv", maDv);
            if (!string.IsNullOrWhiteSpace(maToQl))
            {
                ValidateCode(maToQl, "Mã tổ");
                where.Add("TRIM(u.ma_dv) = :f_to");
                p.Add("f_to", maToQl.Trim());
            }

            // Một nhân viên (ma_nv) có thể có nhiều tài khoản trong v_nguoidung_diaban: gộp còn 1 dòng, ưu tiên tài khoản
            // đã được phân quyền 5S trên DTNTB, rồi tài khoản có vai trò DTNTB bất kỳ.
            p.Add("perm_th", AppPermissions.NAMS.THUC_HIEN);
            await using var conn = new OracleConnection(_connString);
            var rows = await conn.QueryAsync<NamSNhanVienDto>($@"
                WITH u0 AS (
                    SELECT TRIM(u.ma_nd) AS ma_nd, TRIM(u.ma_nv) AS ma_nv, u.ten_nv, TRIM(u.ma_dv) AS ma_dv,
                           CASE WHEN EXISTS (SELECT 1 FROM dtntb_sys_user_roles ur
                                             JOIN dtntb_sys_role_permissions rp ON rp.role_code = ur.role_code
                                             WHERE UPPER(TRIM(ur.ma_nd)) = UPPER(TRIM(u.ma_nd)) AND rp.permission_code = :perm_th) THEN 2
                                WHEN EXISTS (SELECT 1 FROM dtntb_sys_user_roles ur WHERE UPPER(TRIM(ur.ma_nd)) = UPPER(TRIM(u.ma_nd))) THEN 1
                                ELSE 0 END AS uu_tien
                    FROM v_nguoidung_diaban u
                    WHERE {string.Join(" AND ", where)}
                ),
                u1 AS (
                    SELECT u0.*, ROW_NUMBER() OVER (PARTITION BY NVL(UPPER(u0.ma_nv), UPPER(u0.ma_nd))
                                                    ORDER BY u0.uu_tien DESC, u0.ma_nd) AS rn
                    FROM u0
                )
                SELECT u1.ma_nd AS MaNd, u1.ma_nv AS MaNv, u1.ten_nv AS TenNv, u1.ma_dv AS MaDv,
                       (SELECT COUNT(1) FROM dtntb_5s_phieu x
                         WHERE x.trang_thai = 'DA_GIAO'
                           AND (UPPER(x.ma_nd_nhan) = UPPER(u1.ma_nd) OR (u1.ma_nv IS NOT NULL AND TRIM(x.ma_nv_nhan) = u1.ma_nv))) AS SoPhieuMo
                FROM u1 WHERE u1.rn = 1", p);
            return rows.OrderBy(x => x.TenNv ?? x.MaNd, Vi).ToList();
        }

        public async Task<List<NamSOptionDto>> GetNvDonViAsync()
        {
            // Giống màn theo dõi điểm danh: đơn vị từ v_donvi_diaban theo phạm vi người xem.
            var p = new DynamicParameters();
            string where;
            switch (_currentUser.ScopeLevel)
            {
                case UserDataScopeLevel.ToanTinh:
                    where = "1 = 1";
                    break;
                case UserDataScopeLevel.DiaBan:
                    if (string.IsNullOrWhiteSpace(_currentUser.DiaBanId)) return new List<NamSOptionDto>();
                    where = "TRIM(TO_CHAR(db.diaban_id)) = :sc_diaban";
                    p.Add("sc_diaban", _currentUser.DiaBanId.Trim());
                    break;
                default:
                    if (string.IsNullOrWhiteSpace(_currentUser.MaDv7)) return new List<NamSOptionDto>();
                    where = "SUBSTR(TRIM(db.ma_dv), 1, 7) = :sc_dv";
                    p.Add("sc_dv", _currentUser.MaDv7.Trim());
                    break;
            }
            await using var conn = new OracleConnection(_connString);
            var rows = await conn.QueryAsync<NamSOptionDto>($@"
                SELECT SUBSTR(TRIM(db.ma_dv), 1, 7) AS Ma, MAX(db.ten_dv) AS Ten
                FROM v_donvi_diaban db WHERE db.ma_dv IS NOT NULL AND {where}
                GROUP BY SUBSTR(TRIM(db.ma_dv), 1, 7)", p);
            return rows.OrderBy(x => x.Ten ?? x.Ma, Vi).ToList();
        }

        public async Task<List<NamSOptionDto>> GetNvToAsync(string? maDv)
        {
            // Tổ = toàn bộ ma_dv của nhân viên đang hoạt động, tên lấy từ v_donvi.
            var p = new DynamicParameters();
            var where = new List<string> { UserScope(p, "a"), "a.ma_dv IS NOT NULL", "a.trangthai_id = 1" };
            AddMaDvFilter(where, p, "a.ma_dv", maDv);
            await using var conn = new OracleConnection(_connString);
            var rows = await conn.QueryAsync<NamSOptionDto>($@"
                SELECT TRIM(a.ma_dv) AS Ma, MAX(t.ten_dv) AS Ten
                FROM v_nguoidung_diaban a
                LEFT JOIN v_donvi t ON TRIM(a.ma_dv) = TRIM(t.ma_dv)
                WHERE {string.Join(" AND ", where)}
                GROUP BY TRIM(a.ma_dv)", p);
            return rows.Select(x => new NamSOptionDto { Ma = x.Ma, Ten = string.IsNullOrWhiteSpace(x.Ten) ? x.Ma : x.Ten.Trim() })
                       .OrderBy(x => x.Ten, Vi).ToList();
        }

        public async Task<NamSKetQuaGiaoDto> GiaoAsync(NamSGiaoRequestDto request)
        {
            var dsTram = (request.MaCsht ?? new List<string>()).Select(x => (x ?? string.Empty).Trim()).Where(x => x.Length > 0).Distinct().ToList();
            if (dsTram.Count == 0) throw new NamSValidationException("Chưa chọn trạm nào.");
            if (dsTram.Count > MaxGiaoMotLan) throw new NamSValidationException($"Mỗi lần giao tối đa {MaxGiaoMotLan} trạm.");
            if (string.IsNullOrWhiteSpace(request.MaNd)) throw new NamSValidationException("Chưa chọn nhân viên nhận.");
            var maNd = request.MaNd.Trim();
            var ghiChu = CleanText(request.GhiChu, 500);
            var nguoiGiao = Username();

            // Nhân viên phải nằm trong phạm vi của người giao
            var nv = (await GetNhanVienAsync(null, null)).FirstOrDefault(x => x.MaNd.Equals(maNd, StringComparison.OrdinalIgnoreCase))
                     ?? throw new NamSValidationException("Nhân viên không thuộc phạm vi quản lý của bạn.");

            var kq = new NamSKetQuaGiaoDto();
            int ngoaiPhamVi = 0, dangGiao = 0;
            var thang = ThangHienTai();

            await using var conn = new OracleConnection(_connString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                await conn.ExecuteAsync("LOCK TABLE dtntb_5s_phieu IN EXCLUSIVE MODE", transaction: tx);

                foreach (var maCsht in dsTram)
                {
                    var p = new DynamicParameters();
                    p.Add("ma", maCsht);
                    var scope = TramScope(p, "c");
                    var tram = await conn.QueryFirstOrDefaultAsync<ToaDoRow>($@"
                        SELECT TRIM(TO_CHAR(c.toado_lat)) AS Lat, TRIM(TO_CHAR(c.toado_long)) AS Lng
                        FROM dtntb_csht c WHERE TRIM(c.ma_csht) = :ma AND {scope} AND ROWNUM = 1", p, tx);
                    if (tram == null) { ngoaiPhamVi++; continue; }

                    var mo = await conn.ExecuteScalarAsync<int>(
                        "SELECT COUNT(1) FROM dtntb_5s_phieu WHERE ma_csht = :ma AND trang_thai = 'DA_GIAO'", new { ma = maCsht }, tx);
                    if (mo > 0) { dangGiao++; continue; }

                    await InsertPhieuAsync(conn, tx, maCsht, thang, tram, nv.MaNd, nv.MaNv, nv.TenNv, nguoiGiao, ghiChu);
                    kq.ThanhCong++;
                }
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }

            kq.BoQua = ngoaiPhamVi + dangGiao;
            if (dangGiao > 0) kq.GhiChu.Add($"{dangGiao} trạm đang có phiếu chưa nộp.");
            if (ngoaiPhamVi > 0) kq.GhiChu.Add($"{ngoaiPhamVi} trạm không thuộc phạm vi của bạn.");
            _logger.LogInformation("{User} giao {N} trạm 5S cho {MaNd}.", nguoiGiao, kq.ThanhCong, nv.MaNd);
            return kq;
        }

        private async Task<long> InsertPhieuAsync(OracleConnection conn, System.Data.Common.DbTransaction tx, string maCsht, int thang, ToaDoRow tram,
                                                  string maNd, string? maNv, string? tenNv, string nguoiGiao, string? ghiChu)
        {
            var id = await conn.ExecuteScalarAsync<long>("SELECT seq_dtntb_5s_phieu.NEXTVAL FROM dual", transaction: tx);
            var ins = new DynamicParameters();
            ins.Add("id", id);
            ins.Add("thang", thang);
            ins.Add("ma", maCsht);
            ins.Add("lat", ParseCoord(tram.Lat, 90));
            ins.Add("lng", ParseCoord(tram.Lng, 180));
            ins.Add("ma_nd", maNd);
            ins.Add("ma_nv", maNv);
            ins.Add("ten_nv", Truncate(tenNv, 200));
            ins.Add("nguoi", nguoiGiao);
            ins.Add("ten_nguoi", Truncate(_currentUser.TenNv?.Trim(), 200));
            ins.Add("ghichu", ghiChu);
            await conn.ExecuteAsync(@"
                INSERT INTO dtntb_5s_phieu (phieu_id, thang, lan, ma_csht, ten_csht, ma_dv, ten_dv, ma_toql, ten_toql, diachi,
                    toado_lat, toado_long, ma_nd_nhan, ma_nv_nhan, ten_nv_nhan, nguoi_giao, ten_nguoi_giao, ngay_giao, ghichu_giao, trang_thai)
                SELECT :id, :thang,
                       (SELECT COUNT(1) + 1 FROM dtntb_5s_phieu x WHERE x.ma_csht = :ma AND x.thang = :thang AND x.trang_thai <> 'THU_HOI'),
                       TRIM(c.ma_csht), SUBSTR(c.ten_csht, 1, 300), TRIM(c.ma_dv), SUBSTR(c.ten_dv, 1, 300), TRIM(c.ma_toql), SUBSTR(c.ten_toql, 1, 300),
                       SUBSTR(LTRIM(TRIM(c.diachi) || CASE WHEN c.phuongxa IS NOT NULL THEN ', ' || TRIM(c.phuongxa) END, ', '), 1, 500),
                       :lat, :lng, :ma_nd, :ma_nv, :ten_nv, :nguoi, :ten_nguoi, SYSDATE, :ghichu, 'DA_GIAO'
                FROM dtntb_csht c WHERE TRIM(c.ma_csht) = :ma AND ROWNUM = 1", ins, tx);
            return id;
        }

        // ============================================================
        // NHÂN VIÊN TỰ TẠO PHIẾU (đi qua trạm không có trong danh sách được giao)
        // ============================================================

        /// <summary>Tự tạo phiếu được ở mọi trạm trong DTNTB_CSHT (đi qua trạm đơn vị khác vẫn làm được).</summary>
        private static string TuTaoScope(DynamicParameters p, string a) => "1 = 1";

        public async Task<List<NamSTramTuTaoDto>> TimTramTuTaoAsync(string? search, double? lat, double? lng)
        {
            var coViTri = IsCoord(lat, lng);
            var tim = search?.Trim();
            if (string.IsNullOrEmpty(tim) && !coViTri) return new List<NamSTramTuTaoDto>();
            if (tim is { Length: > 100 }) throw new NamSValidationException("Từ khoá quá dài.");

            var p = new DynamicParameters();
            var where = new List<string> { TuTaoScope(p, "c") };
            if (!string.IsNullOrEmpty(tim))
            {
                where.Add("(UPPER(c.ma_csht) LIKE :s OR UPPER(c.ten_csht) LIKE :s OR UPPER(c.diachi) LIKE :s OR UPPER(c.phuongxa) LIKE :s)");
                p.Add("s", "%" + tim.ToUpperInvariant() + "%");
            }
            else where.Add("c.toado_lat IS NOT NULL AND c.toado_long IS NOT NULL");

            await using var conn = new OracleConnection(_connString);
            var rows = await conn.QueryAsync<TramTuTaoRow>($@"
                SELECT * FROM (
                    SELECT TRIM(c.ma_csht) AS MaCsht, c.ten_csht AS TenCsht,
                           LTRIM(TRIM(c.diachi) || CASE WHEN c.phuongxa IS NOT NULL THEN ', ' || TRIM(c.phuongxa) END, ', ') AS DiaChi,
                           c.ten_toql AS TenToQl, c.ten_dv AS TenDv,
                           TRIM(TO_CHAR(c.toado_lat)) AS Lat, TRIM(TO_CHAR(c.toado_long)) AS Lng
                    FROM dtntb_csht c
                    WHERE {string.Join(" AND ", where)}
                ) WHERE ROWNUM <= 20000", p, commandTimeout: 60);

            var list = rows.Select(r =>
            {
                var la = ParseCoord(r.Lat, 90);
                var lo = ParseCoord(r.Lng, 180);
                return new NamSTramTuTaoDto
                {
                    MaCsht = r.MaCsht, TenCsht = r.TenCsht, DiaChi = r.DiaChi, TenToQl = r.TenToQl, TenDv = r.TenDv, Lat = la, Lng = lo,
                    KhoangCachM = coViTri && la.HasValue && lo.HasValue ? (int)Math.Round(DistanceMeters(lat!.Value, lng!.Value, la.Value, lo.Value)) : null
                };
            });
            var ketQua = (coViTri
                    ? list.OrderBy(x => x.KhoangCachM ?? int.MaxValue).ThenBy(x => x.TenCsht, Vi)
                    : list.OrderBy(x => x.TenCsht ?? x.MaCsht, Vi))
                .GroupBy(x => x.MaCsht).Select(g => g.First())
                .Take(30).ToList();
            if (ketQua.Count == 0) return ketQua;

            // Phiếu đang mở của các trạm tìm được
            var mo = (await conn.QueryAsync<PhieuMoRow>(@"
                SELECT p.ma_csht AS MaCsht, p.phieu_id AS PhieuId, p.ten_nv_nhan AS Ten, p.ma_nd_nhan AS MaNd, p.ma_nv_nhan AS MaNv FROM dtntb_5s_phieu p
                WHERE p.trang_thai = 'DA_GIAO' AND p.ma_csht IN :ds", new { ds = ketQua.Select(x => x.MaCsht).ToList() }))
                .GroupBy(x => x.MaCsht).ToDictionary(g => g.Key, g => g.First());
            foreach (var x in ketQua)
            {
                if (!mo.TryGetValue(x.MaCsht, out var m)) continue;
                x.PhieuMoId = m.PhieuId;
                x.NvMo = m.Ten ?? m.MaNd;
                x.PhieuCuaToi = LaCuaToi(m.MaNd, m.MaNv);
            }
            return ketQua;
        }

        public async Task<NamSPhieuDto> TuTaoAsync(NamSTuTaoRequestDto request)
        {
            var maCsht = request?.MaCsht?.Trim();
            if (string.IsNullOrEmpty(maCsht) || maCsht.Length > 50) throw new NamSValidationException("Chưa chọn trạm.");
            var me = Username();
            var ghiChu = CleanText(request?.GhiChu, 450);
            long phieuId;

            await using var conn = new OracleConnection(_connString);
            await conn.OpenAsync();
            await using (var tx = await conn.BeginTransactionAsync())
            {
                try
                {
                    await conn.ExecuteAsync("LOCK TABLE dtntb_5s_phieu IN EXCLUSIVE MODE", transaction: tx);
                    var p = new DynamicParameters();
                    p.Add("ma", maCsht);
                    var scope = TuTaoScope(p, "c");
                    var tram = await conn.QueryFirstOrDefaultAsync<ToaDoRow>($@"
                        SELECT TRIM(TO_CHAR(c.toado_lat)) AS Lat, TRIM(TO_CHAR(c.toado_long)) AS Lng
                        FROM dtntb_csht c WHERE TRIM(c.ma_csht) = :ma AND {scope} AND ROWNUM = 1", p, tx)
                        ?? throw new NamSValidationException("Trạm không thuộc đơn vị của bạn.");

                    var mo = await conn.QueryFirstOrDefaultAsync<PhieuMoRow>(@"
                        SELECT ma_csht AS MaCsht, phieu_id AS PhieuId, ma_nd_nhan AS MaNd, ma_nv_nhan AS MaNv, ten_nv_nhan AS Ten FROM dtntb_5s_phieu
                        WHERE ma_csht = :ma AND trang_thai = 'DA_GIAO' AND ROWNUM = 1", new { ma = maCsht }, tx);
                    if (mo != null)
                    {
                        if (!LaCuaToi(mo.MaNd, mo.MaNv))
                            throw new NamSValidationException($"Trạm đang được giao cho {mo.Ten ?? mo.MaNd}, chưa tạo thêm phiếu được.");
                        phieuId = mo.PhieuId;   // đã có phiếu của chính mình: mở lại phiếu đó
                    }
                    else
                    {
                        phieuId = await InsertPhieuAsync(conn, tx, maCsht, ThangHienTai(), tram, me, _currentUser.MaNv?.Trim(), _currentUser.TenNv?.Trim(),
                            me, "Nhân viên tự tạo" + (ghiChu == null ? "" : ": " + ghiChu));
                        _logger.LogInformation("{User} tự tạo phiếu 5S {Id} trạm {Ma}.", me, phieuId, maCsht);
                    }
                    await tx.CommitAsync();
                }
                catch
                {
                    await tx.RollbackAsync();
                    throw;
                }
            }
            return await conn.QueryFirstAsync<NamSPhieuDto>($"SELECT {PhieuColumns} FROM dtntb_5s_phieu p WHERE p.phieu_id = :id", new { id = phieuId });
        }

        private sealed class TongHopNvRow
        {
            public string Khoa { get; set; } = string.Empty;
            public string? MaNv { get; set; }
            public string? MaNd { get; set; }
            public string? TenNv { get; set; }
            public string? TenDv { get; set; }
            public string? TenToQl { get; set; }
            public int SoPhieuGiao { get; set; }
            public int SoTuTao { get; set; }
            public int SoDangLam { get; set; }
            public int SoDaNop { get; set; }
            public int SoCoDiem { get; set; }
            public decimal TongPhanTram { get; set; }
            public int SoDaCham { get; set; }
            public int SoTot { get; set; }
            public int SoKha { get; set; }
            public int SoTb { get; set; }
            public int SoKem { get; set; }
            public int SoAnh { get; set; }
            public int SoAnhCanhBao { get; set; }
        }

        public async Task<bool> XoaPhieuTuTaoAsync(long phieuId, CancellationToken ct = default)
        {
            var p = OwnParams(new { id = phieuId });
            return await XoaPhieuCoreAsync(phieuId, $@"
                SELECT p.phieu_id FROM dtntb_5s_phieu p
                WHERE p.phieu_id = :id AND {OwnSql("p")} AND p.trang_thai = 'DA_GIAO'
                  AND UPPER(TRIM(p.nguoi_giao)) = UPPER(TRIM(p.ma_nd_nhan))
                FOR UPDATE", p, "tự tạo", ct);
        }

        public async Task<bool> XoaPhieuAsync(long phieuId, CancellationToken ct = default)
        {
            var p = new DynamicParameters();
            p.Add("id", phieuId);
            var scope = PhieuScope(p, "p");
            return await XoaPhieuCoreAsync(phieuId, $"SELECT p.phieu_id FROM dtntb_5s_phieu p WHERE p.phieu_id = :id AND {scope} FOR UPDATE", p, "", ct);
        }

        private async Task<bool> XoaPhieuCoreAsync(long phieuId, string sqlKhoa, DynamicParameters p, string loai, CancellationToken ct)
        {
            List<string> keys;
            await using (var conn = new OracleConnection(_connString))
            {
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);
                var ok = await conn.ExecuteScalarAsync<long?>(sqlKhoa, p, tx);
                if (ok == null) { await tx.RollbackAsync(ct); return false; }
                keys = (await conn.QueryAsync<string>("SELECT image_path FROM dtntb_5s_anh WHERE phieu_id = :id", new { id = phieuId }, tx)).ToList();
                await conn.ExecuteAsync("DELETE FROM dtntb_5s_diem WHERE phieu_id = :id", new { id = phieuId }, tx);
                await conn.ExecuteAsync("DELETE FROM dtntb_5s_anh WHERE phieu_id = :id", new { id = phieuId }, tx);
                await conn.ExecuteAsync("DELETE FROM dtntb_5s_phieu WHERE phieu_id = :id", new { id = phieuId }, tx);
                await tx.CommitAsync(CancellationToken.None);
            }
            await DeleteImagesQuietlyAsync(keys);
            _logger.LogInformation("{User} xoá phiếu 5S {Loai} {Id}.", Username(), loai, phieuId);
            return true;
        }

        private sealed class PhieuMoRow
        {
            public string MaCsht { get; set; } = string.Empty;
            public long PhieuId { get; set; }
            public string? Ten { get; set; }
            public string? MaNd { get; set; }
            public string? MaNv { get; set; }
        }

        private sealed class TramTuTaoRow
        {
            public string MaCsht { get; set; } = string.Empty;
            public string? TenCsht { get; set; }
            public string? DiaChi { get; set; }
            public string? TenToQl { get; set; }
            public string? TenDv { get; set; }
            public string? Lat { get; set; }
            public string? Lng { get; set; }
        }

        public async Task<bool> ThuHoiAsync(long phieuId, CancellationToken ct = default)
        {
            var p = new DynamicParameters();
            p.Add("id", phieuId);
            var scope = PhieuScope(p, "p");
            List<string> keys;
            await using (var conn = new OracleConnection(_connString))
            {
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);
                var ok = await conn.ExecuteScalarAsync<int>(
                    $"SELECT COUNT(1) FROM dtntb_5s_phieu p WHERE p.phieu_id = :id AND p.trang_thai = 'DA_GIAO' AND {scope}", p, tx);
                if (ok == 0) { await tx.RollbackAsync(ct); return false; }

                keys = (await conn.QueryAsync<string>("SELECT image_path FROM dtntb_5s_anh WHERE phieu_id = :id", new { id = phieuId }, tx)).ToList();
                await conn.ExecuteAsync("DELETE FROM dtntb_5s_anh WHERE phieu_id = :id", new { id = phieuId }, tx);
                await conn.ExecuteAsync(@"UPDATE dtntb_5s_phieu SET trang_thai = 'THU_HOI', ngay_thu_hoi = SYSDATE, nguoi_thu_hoi = :nguoi
                                          WHERE phieu_id = :id", new { id = phieuId, nguoi = Username() }, tx);
                await tx.CommitAsync(CancellationToken.None);
            }
            await DeleteImagesQuietlyAsync(keys);
            return true;
        }

        public async Task<List<NamSPhieuDto>> GetPhieuDangGiaoAsync(NamSTramQueryDto query)
        {
            var p = new DynamicParameters();
            var where = new List<string> { PhieuScope(p, "p"), "p.trang_thai = 'DA_GIAO'" };
            AddMaDvFilter(where, p, "p.ma_dv", query.MaDv);
            if (!string.IsNullOrWhiteSpace(query.MaToQl))
            {
                ValidateCode(query.MaToQl, "Mã tổ");
                where.Add("p.ma_toql = :f_to");
                p.Add("f_to", query.MaToQl.Trim());
            }
            await using var conn = new OracleConnection(_connString);
            return (await conn.QueryAsync<NamSPhieuDto>($@"
                SELECT {PhieuColumns} FROM dtntb_5s_phieu p
                WHERE {string.Join(" AND ", where)}
                ORDER BY p.ngay_giao DESC", p)).ToList();
        }

        // ============================================================
        // NHÂN VIÊN THỰC HIỆN
        // ============================================================
        public async Task<List<NamSPhieuDto>> GetCuaToiAsync()
        {
            await using var conn = new OracleConnection(_connString);
            return (await conn.QueryAsync<NamSPhieuDto>($@"
                SELECT {PhieuColumns} FROM dtntb_5s_phieu p
                WHERE {OwnSql("p")} AND p.trang_thai = 'DA_GIAO'
                ORDER BY p.ngay_giao", OwnParams())).ToList();
        }

        public async Task<NamSPhieuChiTietDto?> GetPhieuAsync(long phieuId)
        {
            var p = new DynamicParameters();
            p.Add("id", phieuId);
            var access = AccessClause(p, "p");
            await using var conn = new OracleConnection(_connString);
            var phieu = await conn.QueryFirstOrDefaultAsync<NamSPhieuDto>(
                $"SELECT {PhieuColumns} FROM dtntb_5s_phieu p WHERE p.phieu_id = :id AND {access}", p);
            if (phieu == null) return null;

            // Tiêu chí đang dùng + tiêu chí đã có ảnh/điểm trong phiếu (bộ tiêu chí đổi về sau vẫn hiện đúng phiếu cũ).
            var tieuChi = (await conn.QueryAsync<NamSTieuChiPhieuDto>($@"
                SELECT {TieuChiColumns}, d.diem AS Diem, d.ghi_chu AS GhiChuDiem
                FROM dtntb_5s_tieuchi t
                LEFT JOIN dtntb_5s_diem d ON d.tieuchi_id = t.tieuchi_id AND d.phieu_id = :id
                WHERE t.is_active = 1
                   OR EXISTS (SELECT 1 FROM dtntb_5s_anh a WHERE a.phieu_id = :id AND a.tieuchi_id = t.tieuchi_id)
                   OR d.phieu_id IS NOT NULL
                ORDER BY t.thu_tu, t.tieuchi_id", new { id = phieuId })).ToList();

            var anh = await conn.QueryAsync<AnhRow>($"SELECT {AnhColumns} FROM dtntb_5s_anh a WHERE a.phieu_id = :id ORDER BY a.anh_id", new { id = phieuId });
            var byTc = anh.GroupBy(a => a.TieuChiId).ToDictionary(g => g.Key, g => g.Select(ToAnh).ToList());
            foreach (var tc in tieuChi) tc.Anh = byTc.TryGetValue(tc.TieuChiId, out var list) ? list : new List<NamSAnhDto>();

            var laNguoiNhan = LaCuaToi(phieu.MaNdNhan, phieu.MaNvNhan);
            return new NamSPhieuChiTietDto
            {
                Phieu = phieu,
                TieuChi = tieuChi,
                DuocSua = laNguoiNhan && phieu.TrangThai == NamSTrangThai.DA_GIAO && _currentUser.HasPermission(AppPermissions.NAMS.THUC_HIEN),
                DuocCham = _currentUser.HasPermission(AppPermissions.NAMS.CHAM)
                           && phieu.TrangThai is NamSTrangThai.DA_NOP or NamSTrangThai.DA_CHAM
                           && await TrongPhamViAsync(conn, phieuId)
            };
        }

        public async Task<List<NamSAnhDto>> UploadAnhAsync(long phieuId, int tieuChiId, IReadOnlyList<IFormFile> files, string? metaJson, CancellationToken ct = default)
        {
            var list = (files ?? Array.Empty<IFormFile>()).Where(f => f is { Length: > 0 }).ToList();
            if (list.Count == 0) throw new NamSValidationException("Chưa chọn ảnh.");
            if (list.Count > _maxAnhMoiLan) throw new NamSValidationException($"Mỗi lần tải tối đa {_maxAnhMoiLan} ảnh.");

            List<NamSAnhMetaDto> metas;
            try
            {
                metas = string.IsNullOrWhiteSpace(metaJson)
                    ? new List<NamSAnhMetaDto>()
                    : JsonSerializer.Deserialize<List<NamSAnhMetaDto>>(metaJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            }
            catch (JsonException)
            {
                metas = new List<NamSAnhMetaDto>();
            }

            await using var conn = new OracleConnection(_connString);
            await conn.OpenAsync(ct);
            var phieu = await conn.QueryFirstOrDefaultAsync<NamSPhieuDto>(
                $"SELECT {PhieuColumns} FROM dtntb_5s_phieu p WHERE p.phieu_id = :id AND {OwnSql("p")}",
                OwnParams(new { id = phieuId }));
            if (phieu == null) throw new NamSValidationException("Không tìm thấy phiếu hoặc phiếu không giao cho bạn.");
            if (phieu.TrangThai != NamSTrangThai.DA_GIAO) throw new NamSValidationException("Phiếu đã nộp hoặc đã thu hồi, không tải thêm ảnh được.");

            var tcOk = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM dtntb_5s_tieuchi WHERE tieuchi_id = :tc AND is_active = 1", new { tc = tieuChiId });
            if (tcOk == 0) throw new NamSValidationException("Tiêu chí không hợp lệ.");

            var daCo = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM dtntb_5s_anh WHERE phieu_id = :id", new { id = phieuId });
            if (daCo + list.Count > _maxAnhMoiPhieu) throw new NamSValidationException($"Mỗi phiếu tối đa {_maxAnhMoiPhieu} ảnh.");

            var thang = DateTime.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            var result = new List<NamSAnhDto>();
            for (var i = 0; i < list.Count; i++)
            {
                var meta = i < metas.Count ? metas[i] : new NamSAnhMetaDto();
                var raw = await ReadAndValidateImageAsync(list[i], ct);
                var sha = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
                var jpeg = Reencode(raw);

                var flags = new List<string>();
                var trung = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM dtntb_5s_anh WHERE image_sha256 = :sha AND ROWNUM = 1", new { sha });
                if (trung > 0) flags.Add("TRUNG_ANH");

                double? lat = IsCoord(meta.Lat, meta.Lng) ? Math.Round(meta.Lat!.Value, 7) : null;
                double? lng = lat.HasValue ? Math.Round(meta.Lng!.Value, 7) : null;
                int? khoangCach = null;
                if (lat == null) flags.Add("KHONG_GPS");
                else if (phieu.ToaDoLat.HasValue && phieu.ToaDoLong.HasValue)
                {
                    khoangCach = (int)Math.Round(DistanceMeters(lat.Value, lng!.Value, phieu.ToaDoLat.Value, phieu.ToaDoLong.Value));
                    if (khoangCach > _xaTramMet) flags.Add("XA_TRAM");
                }
                DateTime? chupLuc = DateTime.TryParse(meta.ChupLuc, CultureInfo.InvariantCulture, DateTimeStyles.None, out var cl) ? cl : null;
                var nguon = lat == null ? null : (string.Equals(meta.Nguon, "EXIF", StringComparison.OrdinalIgnoreCase) ? "EXIF" : "THIET_BI");

                var anhId = await conn.ExecuteScalarAsync<long>("SELECT seq_dtntb_5s_anh.NEXTVAL FROM dual");
                var key = $"{thang}/{phieuId}_{anhId}.jpg";   // bucket riêng ttht-5s
                await _imageStore.SaveAsync(key, jpeg, "image/jpeg", ct);
                try
                {
                    var ins = new DynamicParameters();
                    ins.Add("id", anhId);
                    ins.Add("pid", phieuId);
                    ins.Add("tc", tieuChiId);
                    ins.Add("path", key);
                    ins.Add("sha", sha);
                    ins.Add("fname", Truncate(CleanText(meta.FileName ?? list[i].FileName, 200), 200));
                    ins.Add("chup", chupLuc, DbType.DateTime);
                    ins.Add("lat", lat);
                    ins.Add("lng", lng);
                    ins.Add("nguon", nguon);
                    ins.Add("kc", khoangCach);
                    ins.Add("cb", flags.Count == 0 ? null : string.Join(",", flags));
                    await conn.ExecuteAsync(@"
                        INSERT INTO dtntb_5s_anh (anh_id, phieu_id, tieuchi_id, image_path, image_sha256, file_name, ngay_tai,
                                                  chup_luc, latitude, longitude, nguon_toado, khoang_cach_m, canh_bao)
                        VALUES (:id, :pid, :tc, :path, :sha, :fname, SYSDATE, :chup, :lat, :lng, :nguon, :kc, :cb)", ins);
                }
                catch
                {
                    await DeleteImagesQuietlyAsync(new[] { key });
                    throw;
                }

                var row = await conn.QueryFirstAsync<AnhRow>($"SELECT {AnhColumns} FROM dtntb_5s_anh a WHERE a.anh_id = :id", new { id = anhId });
                result.Add(ToAnh(row));
            }
            return result;
        }

        public async Task<bool> XoaAnhAsync(long anhId, CancellationToken ct = default)
        {
            string? key;
            await using (var conn = new OracleConnection(_connString))
            {
                key = await conn.ExecuteScalarAsync<string?>($@"
                    SELECT a.image_path FROM dtntb_5s_anh a JOIN dtntb_5s_phieu p ON p.phieu_id = a.phieu_id
                    WHERE a.anh_id = :id AND {OwnSql("p")} AND p.trang_thai = 'DA_GIAO'",
                    OwnParams(new { id = anhId }));
                if (key == null) return false;
                await conn.ExecuteAsync("DELETE FROM dtntb_5s_anh WHERE anh_id = :id", new { id = anhId });
            }
            await DeleteImagesQuietlyAsync(new[] { key });
            return true;
        }

        public async Task<bool> NopAsync(long phieuId, NamSNopRequestDto request)
        {
            await using var conn = new OracleConnection(_connString);
            var trangThai = await conn.ExecuteScalarAsync<string?>(
                $"SELECT p.trang_thai FROM dtntb_5s_phieu p WHERE p.phieu_id = :id AND {OwnSql("p")}",
                OwnParams(new { id = phieuId }));
            if (trangThai == null) return false;
            if (trangThai != NamSTrangThai.DA_GIAO) throw new NamSValidationException("Phiếu đã nộp hoặc đã thu hồi.");

            var thieu = (await conn.QueryAsync<string>(@"
                SELECT t.noi_dung || ' (cần ' || t.sl_anh_toi_thieu || ' ảnh)'
                FROM dtntb_5s_tieuchi t
                WHERE t.is_active = 1 AND t.bat_buoc = 1
                  AND (SELECT COUNT(1) FROM dtntb_5s_anh a WHERE a.phieu_id = :id AND a.tieuchi_id = t.tieuchi_id) < t.sl_anh_toi_thieu
                ORDER BY t.thu_tu", new { id = phieuId })).ToList();
            if (thieu.Count > 0) throw new NamSValidationException("Chưa đủ ảnh: " + string.Join("; ", thieu));

            var n = await conn.ExecuteAsync(@"
                UPDATE dtntb_5s_phieu SET trang_thai = 'DA_NOP', ngay_nop = SYSDATE, ghichu_nop = :gc
                WHERE phieu_id = :id AND trang_thai = 'DA_GIAO'", new { id = phieuId, gc = CleanText(request?.GhiChu, 500) });
            return n == 1;
        }

        public async Task<(Stream Stream, string ContentType)?> GetAnhAsync(long anhId, CancellationToken ct = default)
        {
            var p = new DynamicParameters();
            p.Add("aid", anhId);
            var access = AccessClause(p, "p");
            await using var conn = new OracleConnection(_connString);
            var key = await conn.ExecuteScalarAsync<string?>($@"
                SELECT a.image_path FROM dtntb_5s_anh a JOIN dtntb_5s_phieu p ON p.phieu_id = a.phieu_id
                WHERE a.anh_id = :aid AND {access}", p);
            if (string.IsNullOrWhiteSpace(key) || !ImageKeyPattern.IsMatch(key.Trim())) return null;
            return await _imageStore.GetAsync(key.Trim(), ct);
        }

        // ============================================================
        // CHẤM ĐIỂM / DANH SÁCH PHIẾU
        // ============================================================
        public async Task<PaginatedResultDto<NamSPhieuDto>> GetPhieuListAsync(NamSPhieuQueryDto query)
        {
            var (where, p) = BuildPhieuFilter(query, defaultThangHienTai: false);
            var page = Math.Max(1, query.Page);
            var size = Math.Clamp(query.PageSize, 1, 200);
            var order = string.Equals(query.TrangThai, NamSTrangThai.DA_NOP, StringComparison.OrdinalIgnoreCase)
                ? "p.ngay_nop, p.phieu_id"                       // chờ chấm: nộp trước chấm trước
                : "NVL(p.ngay_cham, NVL(p.ngay_nop, p.ngay_giao)) DESC, p.phieu_id DESC";

            await using var conn = new OracleConnection(_connString);
            var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM dtntb_5s_phieu p WHERE {where}", p);
            p.Add("r1", (page - 1) * size + 1);
            p.Add("r2", page * size);
            var items = (await conn.QueryAsync<NamSPhieuDto>($@"
                SELECT * FROM (
                    SELECT x.*, ROWNUM rnum FROM (
                        SELECT {PhieuColumns} FROM dtntb_5s_phieu p WHERE {where} ORDER BY {order}
                    ) x WHERE ROWNUM <= :r2
                ) WHERE rnum >= :r1", p)).ToList();
            return new PaginatedResultDto<NamSPhieuDto> { Items = items, TotalCount = total };
        }

        public async Task<NamSPhieuDto?> ChamAsync(long phieuId, NamSChamRequestDto request)
        {
            var diem = request?.Diem ?? new List<NamSDiemItemDto>();
            await using var conn = new OracleConnection(_connString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                var p = new DynamicParameters();
                p.Add("id", phieuId);
                var scope = PhieuScope(p, "p");
                var tt = await conn.ExecuteScalarAsync<string?>(
                    $"SELECT p.trang_thai FROM dtntb_5s_phieu p WHERE p.phieu_id = :id AND {scope} FOR UPDATE", p, tx);
                if (tt == null) { await tx.RollbackAsync(); return null; }
                if (tt is not (NamSTrangThai.DA_NOP or NamSTrangThai.DA_CHAM))
                    throw new NamSValidationException("Phiếu chưa nộp, chưa chấm được.");

                // Tiêu chí cần chấm = tiêu chí đang dùng + tiêu chí có ảnh trong phiếu
                var canCham = (await conn.QueryAsync<NamSTieuChiDto>($@"
                    SELECT {TieuChiColumns} FROM dtntb_5s_tieuchi t
                    WHERE t.is_active = 1 OR EXISTS (SELECT 1 FROM dtntb_5s_anh a WHERE a.phieu_id = :id AND a.tieuchi_id = t.tieuchi_id)",
                    new { id = phieuId }, tx)).ToDictionary(x => x.TieuChiId);

                var nhap = diem.GroupBy(d => d.TieuChiId).ToDictionary(g => g.Key, g => g.Last());
                var thieu = canCham.Values.Where(t => !nhap.ContainsKey(t.TieuChiId)).Select(t => t.NoiDung).ToList();
                if (thieu.Count > 0) throw new NamSValidationException("Chưa chấm: " + string.Join("; ", thieu));

                decimal tong = 0, toiDa = 0;
                foreach (var t in canCham.Values)
                {
                    var d = nhap[t.TieuChiId];
                    if (d.Diem < 0 || d.Diem > t.DiemToiDa)
                        throw new NamSValidationException($"Điểm \"{t.NoiDung}\" phải từ 0 đến {t.DiemToiDa:0.##}.");
                    var v = Math.Round(d.Diem, 2);
                    tong += v;
                    toiDa += t.DiemToiDa;
                    await conn.ExecuteAsync(@"
                        MERGE INTO dtntb_5s_diem d
                        USING (SELECT :pid AS phieu_id, :tc AS tieuchi_id FROM dual) s
                        ON (d.phieu_id = s.phieu_id AND d.tieuchi_id = s.tieuchi_id)
                        WHEN MATCHED THEN UPDATE SET d.diem = :diem, d.ghi_chu = :gc
                        WHEN NOT MATCHED THEN INSERT (phieu_id, tieuchi_id, diem, ghi_chu) VALUES (:pid, :tc, :diem, :gc)",
                        new { pid = phieuId, tc = t.TieuChiId, diem = v, gc = CleanText(d.GhiChu, 500) }, tx);
                }

                var xepLoai = XepLoai(tong, toiDa);
                await conn.ExecuteAsync(@"
                    UPDATE dtntb_5s_phieu
                    SET trang_thai = 'DA_CHAM', tong_diem = :tong, diem_toi_da = :toida, xep_loai = :xl, nhan_xet = :nx,
                        nguoi_cham = :nguoi, ten_nguoi_cham = :ten, ngay_cham = SYSDATE
                    WHERE phieu_id = :id",
                    new
                    {
                        id = phieuId, tong, toida = toiDa, xl = xepLoai, nx = CleanText(request?.NhanXet, 1000),
                        nguoi = Username(), ten = Truncate(_currentUser.TenNv?.Trim(), 200)
                    }, tx);
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }

            return await conn.QueryFirstOrDefaultAsync<NamSPhieuDto>($"SELECT {PhieuColumns} FROM dtntb_5s_phieu p WHERE p.phieu_id = :id", new { id = phieuId });
        }

        // ============================================================
        // BÁO CÁO
        // ============================================================
        public async Task<List<NamSTongHopDto>> GetTongHopAsync(NamSPhieuQueryDto query)
        {
            var p = new DynamicParameters();
            var tramWhere = new List<string> { TramScope(p, "c") };
            AddMaDvFilter(tramWhere, p, "c.ma_dv", query.MaDv);
            var phieuWhere = new List<string> { PhieuScope(p, "p"), "p.trang_thai <> 'THU_HOI'", ThoiGianClause(p, query) };
            AddMaDvFilter(phieuWhere, p, "p.ma_dv", query.MaDv, "f_dv2");

            await using var conn = new OracleConnection(_connString);
            var rows = await conn.QueryAsync<NamSTongHopDto>($@"
                WITH tram AS (
                    SELECT SUBSTR(TRIM(c.ma_dv), 1, 7) AS ma_dv, MAX(c.ten_dv) AS ten_dv, TRIM(c.ma_toql) AS ma_toql,
                           MAX(c.ten_toql) AS ten_toql, COUNT(DISTINCT TRIM(c.ma_csht)) AS so_tram
                    FROM dtntb_csht c WHERE {string.Join(" AND ", tramWhere)}
                    GROUP BY SUBSTR(TRIM(c.ma_dv), 1, 7), TRIM(c.ma_toql)
                ),
                ph AS (
                    SELECT SUBSTR(p.ma_dv, 1, 7) AS ma_dv, MAX(p.ten_dv) AS ten_dv, p.ma_toql, MAX(p.ten_toql) AS ten_toql,
                           COUNT(1) AS so_giao,
                           SUM(CASE WHEN p.trang_thai IN ('DA_NOP', 'DA_CHAM') THEN 1 ELSE 0 END) AS so_nop,
                           SUM(CASE WHEN p.trang_thai = 'DA_CHAM' THEN 1 ELSE 0 END) AS so_cham,
                           COUNT(DISTINCT CASE WHEN p.trang_thai = 'DA_CHAM' THEN p.ma_csht END) AS so_tram_cham,
                           ROUND(AVG(CASE WHEN p.trang_thai = 'DA_CHAM' AND p.diem_toi_da > 0 THEN p.tong_diem * 100 / p.diem_toi_da END), 1) AS diem_tb,
                           SUM(CASE WHEN p.xep_loai = 'TOT' AND p.trang_thai = 'DA_CHAM' THEN 1 ELSE 0 END) AS so_tot,
                           SUM(CASE WHEN p.xep_loai = 'KHA' AND p.trang_thai = 'DA_CHAM' THEN 1 ELSE 0 END) AS so_kha,
                           SUM(CASE WHEN p.xep_loai = 'TB' AND p.trang_thai = 'DA_CHAM' THEN 1 ELSE 0 END) AS so_tb,
                           SUM(CASE WHEN p.xep_loai = 'KEM' AND p.trang_thai = 'DA_CHAM' THEN 1 ELSE 0 END) AS so_kem
                    FROM dtntb_5s_phieu p WHERE {string.Join(" AND ", phieuWhere)}
                    GROUP BY SUBSTR(p.ma_dv, 1, 7), p.ma_toql
                )
                -- Chỉ các tổ có trạm được giao trong kỳ
                SELECT h.ma_dv AS MaDv, NVL(t.ten_dv, h.ten_dv) AS TenDv,
                       h.ma_toql AS MaToQl, NVL(t.ten_toql, h.ten_toql) AS TenToQl,
                       NVL(t.so_tram, 0) AS SoTram, NVL(h.so_giao, 0) AS SoPhieuGiao, NVL(h.so_nop, 0) AS SoDaNop,
                       NVL(h.so_cham, 0) AS SoDaCham, NVL(h.so_tram_cham, 0) AS SoTramDaCham, h.diem_tb AS DiemTb,
                       NVL(h.so_tot, 0) AS SoTot, NVL(h.so_kha, 0) AS SoKha, NVL(h.so_tb, 0) AS SoTb, NVL(h.so_kem, 0) AS SoKem
                FROM ph h
                LEFT JOIN tram t ON t.ma_dv = h.ma_dv AND NVL(t.ma_toql, '#') = NVL(h.ma_toql, '#')", p, commandTimeout: 120);
            return rows.OrderBy(r => r.TenDv ?? r.MaDv, Vi).ThenBy(r => r.TenToQl ?? r.MaToQl, Vi).ToList();
        }

        public async Task<List<NamSTongHopNvDto>> GetTongHopNhanVienAsync(NamSPhieuQueryDto query)
        {
            // Đơn vị / tổ theo NHÂN VIÊN (ma_dv trong v_nguoidung_diaban, tài khoản đang hoạt động), không theo trạm.
            // Thấy được phiếu trong phạm vi trạm, hoặc phiếu của nhân viên thuộc phạm vi mình quản lý.
            var p = new DynamicParameters();
            var scopePhieu = PhieuScope(p, "p");
            var scopeNv = ScopeClause(p, "p.nv_ma_dv", "p.nv_ma_dv", null);
            var where = new List<string> { $"(({scopePhieu}) OR (p.nv_ma_dv IS NOT NULL AND {scopeNv}))", "p.trang_thai <> 'THU_HOI'", ThoiGianClause(p, query) };
            AddMaDvFilter(where, p, "p.nv_ma_dv", string.IsNullOrWhiteSpace(query.NvMaDv) ? query.MaDv : query.NvMaDv);
            if (!string.IsNullOrWhiteSpace(query.NvMaTo))
            {
                ValidateCode(query.NvMaTo, "Mã tổ");
                where.Add("p.nv_ma_dv = :f_nvto");
                p.Add("f_nvto", query.NvMaTo.Trim());
            }
            await using var conn = new OracleConnection(_connString);
            var rows = await conn.QueryAsync<TongHopNvRow>($@"
                WITH n1 AS (
                    SELECT UPPER(TRIM(ma_nv)) AS k, MAX(TRIM(ma_dv)) AS ma_dv FROM v_nguoidung_diaban
                    WHERE trangthai_id = 1 AND ma_nv IS NOT NULL GROUP BY UPPER(TRIM(ma_nv))
                ),
                n2 AS (
                    SELECT UPPER(TRIM(ma_nd)) AS k, MAX(TRIM(ma_dv)) AS ma_dv FROM v_nguoidung_diaban
                    WHERE ma_nd IS NOT NULL GROUP BY UPPER(TRIM(ma_nd))
                ),
                agg AS (
                    SELECT NVL(UPPER(TRIM(p.ma_nv_nhan)), UPPER(TRIM(p.ma_nd_nhan))) AS khoa, p.nv_ma_dv,
                           MAX(TRIM(p.ma_nv_nhan)) AS ma_nv, MAX(TRIM(p.ma_nd_nhan)) AS ma_nd, MAX(p.ten_nv_nhan) AS ten_nv,
                           COUNT(1) AS so_phieu,
                           SUM(CASE WHEN UPPER(TRIM(p.nguoi_giao)) = UPPER(TRIM(p.ma_nd_nhan)) THEN 1 ELSE 0 END) AS so_tu_tao,
                           SUM(CASE WHEN p.trang_thai = 'DA_GIAO' THEN 1 ELSE 0 END) AS so_dang_lam,
                           SUM(CASE WHEN p.trang_thai IN ('DA_NOP', 'DA_CHAM') THEN 1 ELSE 0 END) AS so_nop,
                           SUM(CASE WHEN p.trang_thai = 'DA_CHAM' AND p.diem_toi_da > 0 THEN 1 ELSE 0 END) AS so_co_diem,
                           ROUND(SUM(CASE WHEN p.trang_thai = 'DA_CHAM' AND p.diem_toi_da > 0 THEN p.tong_diem * 100 / p.diem_toi_da ELSE 0 END), 4) AS tong_pct,
                           SUM(CASE WHEN p.trang_thai = 'DA_CHAM' THEN 1 ELSE 0 END) AS so_cham,
                           SUM(CASE WHEN p.trang_thai = 'DA_CHAM' AND p.xep_loai = 'TOT' THEN 1 ELSE 0 END) AS so_tot,
                           SUM(CASE WHEN p.trang_thai = 'DA_CHAM' AND p.xep_loai = 'KHA' THEN 1 ELSE 0 END) AS so_kha,
                           SUM(CASE WHEN p.trang_thai = 'DA_CHAM' AND p.xep_loai = 'TB' THEN 1 ELSE 0 END) AS so_tb,
                           SUM(CASE WHEN p.trang_thai = 'DA_CHAM' AND p.xep_loai = 'KEM' THEN 1 ELSE 0 END) AS so_kem,
                           SUM((SELECT COUNT(1) FROM dtntb_5s_anh a WHERE a.phieu_id = p.phieu_id)) AS so_anh,
                           SUM((SELECT COUNT(1) FROM dtntb_5s_anh a WHERE a.phieu_id = p.phieu_id AND a.canh_bao IS NOT NULL)) AS so_anh_cb
                    FROM (
                        SELECT x.*, NVL(n1.ma_dv, n2.ma_dv) AS nv_ma_dv
                        FROM dtntb_5s_phieu x
                        LEFT JOIN n1 ON n1.k = UPPER(TRIM(x.ma_nv_nhan))
                        LEFT JOIN n2 ON n2.k = UPPER(TRIM(x.ma_nd_nhan))
                    ) p
                    WHERE {string.Join(" AND ", where)}
                    GROUP BY NVL(UPPER(TRIM(p.ma_nv_nhan)), UPPER(TRIM(p.ma_nd_nhan))), p.nv_ma_dv
                )
                SELECT g.khoa AS Khoa, g.ma_nv AS MaNv, g.ma_nd AS MaNd, g.ten_nv AS TenNv,
                       (SELECT MAX(db.ten_dv) FROM v_donvi_diaban db WHERE SUBSTR(TRIM(db.ma_dv), 1, 7) = SUBSTR(g.nv_ma_dv, 1, 7)) AS TenDv,
                       NVL((SELECT MAX(t.ten_dv) FROM v_donvi t WHERE TRIM(t.ma_dv) = g.nv_ma_dv), g.nv_ma_dv) AS TenToQl,
                       g.so_phieu AS SoPhieuGiao, g.so_tu_tao AS SoTuTao, g.so_dang_lam AS SoDangLam, g.so_nop AS SoDaNop,
                       g.so_co_diem AS SoCoDiem, g.tong_pct AS TongPhanTram, g.so_cham AS SoDaCham, g.so_tot AS SoTot, g.so_kha AS SoKha,
                       g.so_tb AS SoTb, g.so_kem AS SoKem, g.so_anh AS SoAnh, g.so_anh_cb AS SoAnhCanhBao
                FROM agg g", p, commandTimeout: 120);

            return rows.GroupBy(r => r.Khoa).Select(g =>
            {
                var soCoDiem = g.Sum(x => x.SoCoDiem);
                return new NamSTongHopNvDto
                {
                    MaNv = g.Select(x => x.MaNv).FirstOrDefault(x => !string.IsNullOrEmpty(x)),
                    MaNd = g.Select(x => x.MaNd).FirstOrDefault(x => !string.IsNullOrEmpty(x)),
                    TenNv = g.Select(x => x.TenNv).FirstOrDefault(x => !string.IsNullOrEmpty(x)),
                    TenDv = string.Join(", ", g.Select(x => x.TenDv).Where(x => !string.IsNullOrEmpty(x)).Distinct()),
                    TenToQl = string.Join(", ", g.OrderByDescending(x => x.SoPhieuGiao).Select(x => x.TenToQl).Where(x => !string.IsNullOrEmpty(x)).Distinct()),
                    SoPhieuGiao = g.Sum(x => x.SoPhieuGiao),
                    SoTuTao = g.Sum(x => x.SoTuTao),
                    SoDangLam = g.Sum(x => x.SoDangLam),
                    SoDaNop = g.Sum(x => x.SoDaNop),
                    SoDaCham = g.Sum(x => x.SoDaCham),
                    DiemTb = soCoDiem > 0 ? Math.Round(g.Sum(x => x.TongPhanTram) / soCoDiem, 1) : null,
                    SoTot = g.Sum(x => x.SoTot),
                    SoKha = g.Sum(x => x.SoKha),
                    SoTb = g.Sum(x => x.SoTb),
                    SoKem = g.Sum(x => x.SoKem),
                    SoAnh = g.Sum(x => x.SoAnh),
                    SoAnhCanhBao = g.Sum(x => x.SoAnhCanhBao)
                };
            }).OrderBy(r => r.TenDv ?? "", Vi).ThenBy(r => r.TenNv ?? r.MaNd, Vi).ToList();
        }

        public async Task<byte[]> ExportAsync(NamSPhieuQueryDto query)
        {
            var tongHop = await GetTongHopAsync(query);
            var (where, p) = BuildPhieuFilter(query, defaultThangHienTai: true);
            await using var conn = new OracleConnection(_connString);
            var chiTiet = (await conn.QueryAsync<NamSPhieuDto>($@"
                SELECT * FROM (
                    SELECT {PhieuColumns} FROM dtntb_5s_phieu p WHERE {where}
                    ORDER BY p.ten_dv, p.ten_toql, p.ten_csht, p.thang, p.lan
                ) WHERE ROWNUM <= {MaxExportRows}", p)).ToList();
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("TongHop");
            ws.Cell(1, 1).Value = "BÁO CÁO 5S NHÀ TRẠM " + TenKy(query);
            ws.Range(1, 1, 1, 14).Merge().Style.Font.SetBold().Font.SetFontSize(13);
            Header(ws, 3, "STT", "Đơn vị", "Tổ quản lý", "Số trạm", "Phiếu giao", "Đã nộp", "Đã chấm", "Trạm đã chấm", "% trạm đã chấm",
                   "Điểm TB (%)", "Tốt", "Khá", "Trung bình", "Kém");
            var r = 4;
            foreach (var x in tongHop)
            {
                ws.Cell(r, 1).Value = (double)(r - 3);
                ws.Cell(r, 2).Value = Text(x.TenDv ?? x.MaDv);
                ws.Cell(r, 3).Value = Text(x.TenToQl ?? x.MaToQl);
                ws.Cell(r, 4).Value = (double)x.SoTram;
                ws.Cell(r, 5).Value = (double)x.SoPhieuGiao;
                ws.Cell(r, 6).Value = (double)x.SoDaNop;
                ws.Cell(r, 7).Value = (double)x.SoDaCham;
                ws.Cell(r, 8).Value = (double)x.SoTramDaCham;
                if (x.SoTram > 0) { ws.Cell(r, 9).Value = (double)x.SoTramDaCham / x.SoTram; ws.Cell(r, 9).Style.NumberFormat.Format = "0.0%"; }
                if (x.DiemTb.HasValue) ws.Cell(r, 10).Value = (double)x.DiemTb.Value;
                ws.Cell(r, 11).Value = (double)x.SoTot;
                ws.Cell(r, 12).Value = (double)x.SoKha;
                ws.Cell(r, 13).Value = (double)x.SoTb;
                ws.Cell(r, 14).Value = (double)x.SoKem;
                r++;
            }
            ws.Columns().AdjustToContents(3, Math.Max(r, 4), 6, 50);
            ws.SheetView.FreezeRows(3);

            var theoNv = await GetTongHopNhanVienAsync(query);
            var wn = wb.Worksheets.Add("TheoNhanVien");
            Header(wn, 1, "STT", "Nhân viên", "Mã NV", "Đơn vị", "Tổ", "Phiếu", "Tự tạo", "Đang làm", "Đã nộp", "Đã chấm",
                   "Điểm TB (%)", "Tốt", "Khá", "Trung bình", "Kém", "Số ảnh", "Ảnh cảnh báo");
            r = 2;
            foreach (var x in theoNv)
            {
                wn.Cell(r, 1).Value = (double)(r - 1);
                wn.Cell(r, 2).Value = Text(x.TenNv ?? x.MaNd);
                wn.Cell(r, 3).Value = Text(x.MaNv);
                wn.Cell(r, 4).Value = Text(x.TenDv);
                wn.Cell(r, 5).Value = Text(x.TenToQl);
                wn.Cell(r, 6).Value = (double)x.SoPhieuGiao;
                wn.Cell(r, 7).Value = (double)x.SoTuTao;
                wn.Cell(r, 8).Value = (double)x.SoDangLam;
                wn.Cell(r, 9).Value = (double)x.SoDaNop;
                wn.Cell(r, 10).Value = (double)x.SoDaCham;
                if (x.DiemTb.HasValue) wn.Cell(r, 11).Value = (double)x.DiemTb.Value;
                wn.Cell(r, 12).Value = (double)x.SoTot;
                wn.Cell(r, 13).Value = (double)x.SoKha;
                wn.Cell(r, 14).Value = (double)x.SoTb;
                wn.Cell(r, 15).Value = (double)x.SoKem;
                wn.Cell(r, 16).Value = (double)x.SoAnh;
                wn.Cell(r, 17).Value = (double)x.SoAnhCanhBao;
                r++;
            }
            wn.Columns().AdjustToContents(1, Math.Max(r, 2), 6, 50);
            wn.SheetView.FreezeRows(1);

            var ct = wb.Worksheets.Add("ChiTiet");
            Header(ct, 1, "STT", "Tháng", "Lần", "Mã CSHT", "Tên trạm", "Đơn vị", "Tổ quản lý", "Địa chỉ", "NV thực hiện", "Ngày giao",
                   "Ngày nộp", "Trạng thái", "Điểm", "Điểm tối đa", "Xếp loại", "Người chấm", "Ngày chấm", "Nhận xét", "Số ảnh", "Ảnh cảnh báo");
            r = 2;
            foreach (var x in chiTiet)
            {
                ct.Cell(r, 1).Value = (double)(r - 1);
                ct.Cell(r, 2).Value = TenThang(x.Thang);
                ct.Cell(r, 3).Value = (double)x.Lan;
                ct.Cell(r, 4).Value = Text(x.MaCsht);
                ct.Cell(r, 5).Value = Text(x.TenCsht);
                ct.Cell(r, 6).Value = Text(x.TenDv);
                ct.Cell(r, 7).Value = Text(x.TenToQl);
                ct.Cell(r, 8).Value = Text(x.DiaChi);
                ct.Cell(r, 9).Value = Text(x.TenNvNhan ?? x.MaNdNhan);
                SetDate(ct.Cell(r, 10), x.NgayGiao);
                SetDate(ct.Cell(r, 11), x.NgayNop);
                ct.Cell(r, 12).Value = TenTrangThai(x.TrangThai);
                if (x.TongDiem.HasValue) ct.Cell(r, 13).Value = (double)x.TongDiem.Value;
                if (x.DiemToiDa.HasValue) ct.Cell(r, 14).Value = (double)x.DiemToiDa.Value;
                ct.Cell(r, 15).Value = TenXepLoai(x.XepLoai);
                ct.Cell(r, 16).Value = Text(x.TenNguoiCham);
                SetDate(ct.Cell(r, 17), x.NgayCham);
                ct.Cell(r, 18).Value = Text(x.NhanXet);
                ct.Cell(r, 19).Value = (double)x.SoAnh;
                ct.Cell(r, 20).Value = (double)x.SoAnhCanhBao;
                r++;
            }
            ct.Columns().AdjustToContents(1, Math.Min(Math.Max(r, 2), 300), 6, 50);
            ct.SheetView.FreezeRows(1);

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        // ============================================================
        // PHẠM VI DỮ LIỆU
        // ============================================================

        /// <summary>Phạm vi trên bảng trạm DTNTB_CSHT.</summary>
        private string TramScope(DynamicParameters p, string a) => ScopeClause(p, $"{a}.ma_dv", $"{a}.ma_toql", null);

        /// <summary>Phạm vi trên phiếu (nhân viên: phiếu giao cho mình).</summary>
        private string PhieuScope(DynamicParameters p, string a) => ScopeClause(p, $"{a}.ma_dv", $"{a}.ma_toql", $"{a}.ma_nd_nhan");

        /// <summary>Phạm vi trên danh sách người dùng v_nguoidung_diaban (tổ = toàn bộ ma_dv).</summary>
        private string UserScope(DynamicParameters p, string a) => ScopeClause(p, $"{a}.ma_dv", $"{a}.ma_dv", $"{a}.ma_nd");

        private string ScopeClause(DynamicParameters p, string maDvCol, string maToCol, string? maNdCol)
        {
            switch (_currentUser.ScopeLevel)
            {
                case UserDataScopeLevel.ToanTinh:
                    return "1 = 1";
                case UserDataScopeLevel.DiaBan:
                    if (string.IsNullOrWhiteSpace(_currentUser.DiaBanId)) return "1 = 0";
                    p.Add("sc_diaban", _currentUser.DiaBanId.Trim());
                    return $@"SUBSTR(TRIM({maDvCol}), 1, 7) IN (
                                SELECT SUBSTR(TRIM(db.ma_dv), 1, 7) FROM v_donvi_diaban db
                                WHERE TRIM(TO_CHAR(db.diaban_id)) = :sc_diaban)";
                case UserDataScopeLevel.DonVi:
                    if (string.IsNullOrWhiteSpace(_currentUser.MaDv7)) return "1 = 0";
                    p.Add("sc_dv", _currentUser.MaDv7.Trim());
                    return $"SUBSTR(TRIM({maDvCol}), 1, 7) = :sc_dv";
                case UserDataScopeLevel.ToQuanLy:
                    if (string.IsNullOrWhiteSpace(_currentUser.MaDv)) return "1 = 0";
                    p.Add("sc_to", _currentUser.MaDv.Trim());
                    return $"TRIM({maToCol}) = :sc_to";
                default:
                    if (maNdCol == null)
                    {
                        // Nhân viên: trạm thuộc tổ của mình
                        if (string.IsNullOrWhiteSpace(_currentUser.MaDv)) return "1 = 0";
                        p.Add("sc_to", _currentUser.MaDv.Trim());
                        return $"TRIM({maToCol}) = :sc_to";
                    }
                    p.Add("sc_nd", Username());
                    if (maNdCol.EndsWith(".ma_nd_nhan", StringComparison.Ordinal) && MaNvCuaToi() is { } nvToi)
                    {
                        p.Add("sc_nv", nvToi);
                        return $"(UPPER(TRIM({maNdCol})) = UPPER(:sc_nd) OR TRIM({maNdCol[..^".ma_nd_nhan".Length]}.ma_nv_nhan) = :sc_nv)";
                    }
                    return $"UPPER(TRIM({maNdCol})) = UPPER(:sc_nd)";
            }
        }

        /// <summary>Người được giao luôn xem được phiếu của mình; người khác cần quyền giao/chấm/báo cáo và trong phạm vi.</summary>
        private string AccessClause(DynamicParameters p, string a)
        {
            p.Add("me", Username());
            p.Add("me_nv", MaNvCuaToi(), DbType.String);
            var own = OwnSql(a);
            var quanLy = _currentUser.HasPermission(AppPermissions.NAMS.GIAO)
                         || _currentUser.HasPermission(AppPermissions.NAMS.CHAM)
                         || _currentUser.HasPermission(AppPermissions.NAMS.VIEW);
            return quanLy ? $"({own} OR ({PhieuScope(p, a)}))" : own;
        }

        private async Task<bool> TrongPhamViAsync(OracleConnection conn, long phieuId)
        {
            var p = new DynamicParameters();
            p.Add("id", phieuId);
            var scope = PhieuScope(p, "p");
            return await conn.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM dtntb_5s_phieu p WHERE p.phieu_id = :id AND {scope}", p) > 0;
        }

        private (string Where, DynamicParameters Params) BuildPhieuFilter(NamSPhieuQueryDto q, bool defaultThangHienTai)
        {
            var p = new DynamicParameters();
            var where = new List<string> { PhieuScope(p, "p") };

            var tt = (q.TrangThai ?? string.Empty).Trim().ToUpperInvariant();
            switch (tt)
            {
                case NamSTrangThai.DA_GIAO:
                case NamSTrangThai.DA_NOP:
                case NamSTrangThai.DA_CHAM:
                case NamSTrangThai.THU_HOI:
                    where.Add("p.trang_thai = :f_tt");
                    p.Add("f_tt", tt);
                    break;
                case "NOP_CHAM":
                    where.Add("p.trang_thai IN ('DA_NOP', 'DA_CHAM')");
                    break;
                case "":
                case "ALL":
                    where.Add("p.trang_thai <> 'THU_HOI'");
                    break;
                default:
                    throw new NamSValidationException("Trạng thái không hợp lệ.");
            }

            if (q.TuNgay.HasValue || q.DenNgay.HasValue || q.TuThang.HasValue || q.DenThang.HasValue || defaultThangHienTai)
                where.Add(ThoiGianClause(p, q));

            AddMaDvFilter(where, p, "p.ma_dv", q.MaDv);
            if (!string.IsNullOrWhiteSpace(q.MaToQl))
            {
                ValidateCode(q.MaToQl, "Mã tổ");
                where.Add("p.ma_toql = :f_to");
                p.Add("f_to", q.MaToQl.Trim());
            }
            if (!string.IsNullOrWhiteSpace(q.XepLoai))
            {
                var xl = q.XepLoai.Trim().ToUpperInvariant();
                if (_xepLoai.All(x => x.Ma != xl)) throw new NamSValidationException("Xếp loại không hợp lệ.");
                where.Add("p.xep_loai = :f_xl");
                p.Add("f_xl", xl);
            }
            if (!string.IsNullOrWhiteSpace(q.Search))
            {
                if (q.Search.Length > 100) throw new NamSValidationException("Từ khoá quá dài.");
                where.Add(@"(UPPER(p.ma_csht) LIKE :f_s OR UPPER(p.ten_csht) LIKE :f_s OR UPPER(p.ten_nv_nhan) LIKE :f_s
                             OR UPPER(p.ma_nd_nhan) LIKE :f_s OR UPPER(p.diachi) LIKE :f_s)");
                p.Add("f_s", "%" + q.Search.Trim().ToUpperInvariant() + "%");
            }
            return (string.Join(" AND ", where), p);
        }

        private static void AddTramFilters(List<string> where, DynamicParameters p, NamSTramQueryDto q)
        {
            AddMaDvFilter(where, p, "c.ma_dv", q.MaDv);
            if (!string.IsNullOrWhiteSpace(q.MaToQl))
            {
                ValidateCode(q.MaToQl, "Mã tổ");
                where.Add("TRIM(c.ma_toql) = :f_to");
                p.Add("f_to", q.MaToQl.Trim());
            }
            if (!string.IsNullOrWhiteSpace(q.Search))
            {
                if (q.Search.Length > 100) throw new NamSValidationException("Từ khoá quá dài.");
                where.Add("(UPPER(c.ma_csht) LIKE :f_s OR UPPER(c.ten_csht) LIKE :f_s OR UPPER(c.diachi) LIKE :f_s OR UPPER(c.phuongxa) LIKE :f_s)");
                p.Add("f_s", "%" + q.Search.Trim().ToUpperInvariant() + "%");
            }
        }

        private static void AddMaDvFilter(List<string> where, DynamicParameters p, string col, string? maDv, string name = "f_dv")
        {
            if (string.IsNullOrWhiteSpace(maDv) || maDv.Equals("ALL", StringComparison.OrdinalIgnoreCase)) return;
            ValidateCode(maDv, "Mã đơn vị");
            var v = maDv.Trim();
            where.Add($"SUBSTR(TRIM({col}), 1, 7) = :{name}");
            p.Add(name, v.Length > 7 ? v[..7] : v);
        }

        private static void ValidateCode(string value, string label)
        {
            var v = value.Trim();
            if (v.Length > 30 || !v.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-'))
                throw new NamSValidationException($"{label} không hợp lệ.");
        }

        // ============================================================
        // ẢNH
        // ============================================================
        private async Task<byte[]> ReadAndValidateImageAsync(IFormFile file, CancellationToken ct)
        {
            if (file.Length > _maxImageBytes)
                throw new NamSValidationException($"Ảnh \"{file.FileName}\" quá lớn (tối đa {_maxImageBytes / 1024 / 1024} MB).");
            byte[] bytes;
            await using (var input = file.OpenReadStream())
            using (var ms = new MemoryStream((int)file.Length))
            {
                await input.CopyToAsync(ms, ct);
                bytes = ms.ToArray();
            }
            var isJpeg = bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
            var isPng = bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            if (!isJpeg && !isPng) throw new NamSValidationException($"\"{file.FileName}\" không phải ảnh JPEG/PNG.");
            try
            {
                var info = new MagickImageInfo(bytes);
                if (info.Width == 0 || info.Height == 0 || (ulong)info.Width * info.Height > 60_000_000UL)
                    throw new NamSValidationException($"Kích thước ảnh \"{file.FileName}\" không hợp lệ.");
            }
            catch (MagickException)
            {
                throw new NamSValidationException($"Không đọc được ảnh \"{file.FileName}\".");
            }
            return bytes;
        }

        /// <summary>Xoay đúng chiều, thu nhỏ, xoá metadata (toạ độ đã lưu riêng trong CSDL), lưu JPEG.</summary>
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

        private async Task DeleteImagesQuietlyAsync(IEnumerable<string> keys)
        {
            foreach (var key in keys)
            {
                var k = key?.Trim();
                if (string.IsNullOrEmpty(k) || !ImageKeyPattern.IsMatch(k)) continue;
                try { await _imageStore.DeleteAsync(k, CancellationToken.None); }
                catch (Exception ex) { _logger.LogWarning(ex, "Không xóa được ảnh 5S {Key} trên MinIO.", k); }
            }
        }

        // ============================================================
        // TIỆN ÍCH
        // ============================================================
        private const string TieuChiColumns = @"t.tieuchi_id AS TieuChiId, t.ma AS Ma, t.noi_dung AS NoiDung, t.yeu_cau_anh AS YeuCauAnh,
            t.sl_anh_toi_thieu AS SlAnhToiThieu, t.diem_toi_da AS DiemToiDa, t.bat_buoc AS BatBuocSo, t.thu_tu AS ThuTu";

        private const string PhieuColumns = @"p.phieu_id AS PhieuId, p.thang AS Thang, p.lan AS Lan, p.ma_csht AS MaCsht, p.ten_csht AS TenCsht,
            p.ma_dv AS MaDv, p.ten_dv AS TenDv, p.ma_toql AS MaToQl, p.ten_toql AS TenToQl, p.diachi AS DiaChi,
            p.toado_lat AS ToaDoLat, p.toado_long AS ToaDoLong, p.ma_nd_nhan AS MaNdNhan, p.ma_nv_nhan AS MaNvNhan,
            p.ten_nv_nhan AS TenNvNhan, p.ten_nguoi_giao AS TenNguoiGiao, p.ngay_giao AS NgayGiao, p.ghichu_giao AS GhiChuGiao,
            p.trang_thai AS TrangThai, p.ngay_nop AS NgayNop, p.ghichu_nop AS GhiChuNop, p.tong_diem AS TongDiem,
            p.diem_toi_da AS DiemToiDa, p.xep_loai AS XepLoai, p.nhan_xet AS NhanXet, p.ten_nguoi_cham AS TenNguoiCham,
            p.ngay_cham AS NgayCham,
            (SELECT COUNT(1) FROM dtntb_5s_anh a WHERE a.phieu_id = p.phieu_id) AS SoAnh,
            (SELECT COUNT(1) FROM dtntb_5s_anh a WHERE a.phieu_id = p.phieu_id AND a.canh_bao IS NOT NULL) AS SoAnhCanhBao,
            CASE WHEN UPPER(TRIM(p.nguoi_giao)) = UPPER(TRIM(p.ma_nd_nhan)) THEN 1 ELSE 0 END AS TuTaoSo";

        private const string AnhColumns = @"a.anh_id AS AnhId, a.tieuchi_id AS TieuChiId, a.file_name AS FileName, a.ngay_tai AS NgayTai,
            a.chup_luc AS ChupLuc, a.latitude AS Latitude, a.longitude AS Longitude, a.nguon_toado AS NguonToaDo,
            a.khoang_cach_m AS KhoangCachM, a.canh_bao AS CanhBao";

        private sealed class ToaDoRow
        {
            public string? Lat { get; set; }
            public string? Lng { get; set; }
        }

        private sealed class AnhRow
        {
            public long AnhId { get; set; }
            public int TieuChiId { get; set; }
            public string? FileName { get; set; }
            public DateTime NgayTai { get; set; }
            public DateTime? ChupLuc { get; set; }
            public double? Latitude { get; set; }
            public double? Longitude { get; set; }
            public string? NguonToaDo { get; set; }
            public int? KhoangCachM { get; set; }
            public string? CanhBao { get; set; }
        }

        private static NamSAnhDto ToAnh(AnhRow r) => new()
        {
            AnhId = r.AnhId,
            TieuChiId = r.TieuChiId,
            FileName = r.FileName,
            NgayTai = r.NgayTai,
            ChupLuc = r.ChupLuc,
            Latitude = r.Latitude,
            Longitude = r.Longitude,
            NguonToaDo = r.NguonToaDo,
            KhoangCachM = r.KhoangCachM,
            CanhBao = string.IsNullOrWhiteSpace(r.CanhBao)
                ? new List<string>()
                : r.CanhBao.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            AnhUrl = $"/api/5s/anh/{r.AnhId}"
        };

        private string XepLoai(decimal tong, decimal toiDa)
        {
            var pct = toiDa > 0 ? tong * 100 / toiDa : 0;
            return _xepLoai.OrderByDescending(x => x.TuPhanTram).First(x => pct >= x.TuPhanTram).Ma;
        }

        private string TenXepLoai(string? ma) => _xepLoai.FirstOrDefault(x => x.Ma == ma)?.Ten ?? string.Empty;

        private static string TenTrangThai(string tt) => tt switch
        {
            NamSTrangThai.DA_GIAO => "Đang thực hiện",
            NamSTrangThai.DA_NOP => "Chờ chấm",
            NamSTrangThai.DA_CHAM => "Đã chấm",
            NamSTrangThai.THU_HOI => "Thu hồi",
            _ => tt
        };

        private static int ThangHienTai() => int.Parse(DateTime.Now.ToString("yyyyMM", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        private static (int Tu, int Den) KhoangThang(NamSPhieuQueryDto q)
        {
            static bool Ok(int? t) => t is >= 200001 and <= 299912 && t % 100 is >= 1 and <= 12;
            var den = Ok(q.DenThang) ? q.DenThang!.Value : (Ok(q.TuThang) ? q.TuThang!.Value : ThangHienTai());
            var tu = Ok(q.TuThang) ? q.TuThang!.Value : den;
            if (tu > den) (tu, den) = (den, tu);
            return (tu, den);
        }

        private static string TenThang(int t) => $"{t % 100:00}/{t / 100}";

        /// <summary>Khoảng ngày giao (TuNgay/DenNgay); không có thì theo tháng (TuThang/DenThang, mặc định tháng hiện tại).</summary>
        private static (DateTime Tu, DateTime Den)? KhoangNgay(NamSPhieuQueryDto q)
        {
            if (!q.TuNgay.HasValue && !q.DenNgay.HasValue) return null;
            var tu = (q.TuNgay ?? q.DenNgay)!.Value.Date;
            var den = (q.DenNgay ?? q.TuNgay)!.Value.Date;
            if (tu > den) (tu, den) = (den, tu);
            if ((den - tu).TotalDays > 3660) throw new NamSValidationException("Khoảng ngày tối đa 10 năm.");
            return (tu, den);
        }

        private static string ThoiGianClause(DynamicParameters p, NamSPhieuQueryDto q)
        {
            var ngay = KhoangNgay(q);
            if (ngay.HasValue)
            {
                p.Add("f_tn", ngay.Value.Tu, DbType.Date);
                p.Add("f_dn", ngay.Value.Den.AddDays(1), DbType.Date);
                return "p.ngay_giao >= :f_tn AND p.ngay_giao < :f_dn";
            }
            var (tu, den) = KhoangThang(q);
            p.Add("f_tu", tu);
            p.Add("f_den", den);
            return "p.thang BETWEEN :f_tu AND :f_den";
        }

        private static string TenKy(NamSPhieuQueryDto q)
        {
            var ngay = KhoangNgay(q);
            if (ngay.HasValue)
                return ngay.Value.Tu == ngay.Value.Den
                    ? $"NGÀY {ngay.Value.Tu:dd/MM/yyyy}"
                    : $"TỪ NGÀY {ngay.Value.Tu:dd/MM/yyyy} ĐẾN NGÀY {ngay.Value.Den:dd/MM/yyyy}";
            var (tu, den) = KhoangThang(q);
            return $"THÁNG {TenThang(tu)}" + (tu == den ? "" : $" - {TenThang(den)}");
        }

        private string? MaNvCuaToi()
        {
            var nv = _currentUser.MaNv?.Trim();
            return string.IsNullOrEmpty(nv) ? null : nv;
        }

        /// <summary>Phiếu là của tôi: giao đúng tài khoản, hoặc giao cho tài khoản khác cùng mã nhân viên.</summary>
        private bool LaCuaToi(string? maNd, string? maNv)
        {
            if (string.Equals(maNd?.Trim(), Username(), StringComparison.OrdinalIgnoreCase)) return true;
            var nv = MaNvCuaToi();
            return nv != null && string.Equals(maNv?.Trim(), nv, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Điều kiện "phiếu của tôi" (tham số :me, :me_nv lấy từ OwnParams).</summary>
        private static string OwnSql(string a) =>
            $"(UPPER(TRIM({a}.ma_nd_nhan)) = UPPER(:me) OR (:me_nv IS NOT NULL AND TRIM({a}.ma_nv_nhan) = :me_nv))";

        private DynamicParameters OwnParams(object? extra = null)
        {
            var p = extra == null ? new DynamicParameters() : new DynamicParameters(extra);
            p.Add("me", Username());
            p.Add("me_nv", MaNvCuaToi(), DbType.String);
            return p;
        }

        private string Username()
        {
            var u = _currentUser.Username?.Trim();
            if (string.IsNullOrEmpty(u)) throw new NamSValidationException("Không xác định được tài khoản đăng nhập.");
            return u.Length > 100 ? u[..100] : u;
        }

        private static double? ParseCoord(string? value, double limit)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var v = value.Trim().Replace(',', '.');
            return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && Math.Abs(d) <= limit && d != 0
                ? Math.Round(d, 7) : null;
        }

        private static bool IsCoord(double? lat, double? lng) =>
            lat.HasValue && lng.HasValue && lat is >= -90 and <= 90 && lng is >= -180 and <= 180 && !(lat == 0 && lng == 0);

        private static double DistanceMeters(double lat1, double lng1, double lat2, double lng2)
        {
            const double R = 6371000;
            double Rad(double d) => d * Math.PI / 180;
            var dLat = Rad(lat2 - lat1);
            var dLng = Rad(lng2 - lng1);
            var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
            return 2 * R * Math.Asin(Math.Min(1, Math.Sqrt(h)));
        }

        private static string? CleanText(string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var clean = new string(value.Where(c => !char.IsControl(c) || c == '\n').ToArray()).Trim();
            return clean.Length == 0 ? null : (clean.Length > max ? clean[..max] : clean);
        }

        private static string? Truncate(string? value, int max) => value == null ? null : (value.Length > max ? value[..max] : value);

        private static string Text(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value[0] is '=' or '+' or '-' or '@' ? "'" + value : value;
        }

        private static void Header(IXLWorksheet ws, int row, params string[] names)
        {
            for (var i = 0; i < names.Length; i++) ws.Cell(row, i + 1).Value = names[i];
            var range = ws.Range(row, 1, row, names.Length);
            range.Style.Font.Bold = true;
            range.Style.Font.FontColor = XLColor.White;
            range.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a73e8");
            range.Style.Alignment.WrapText = true;
        }

        private static void SetDate(IXLCell cell, DateTime? value)
        {
            if (!value.HasValue) return;
            cell.Value = value.Value;
            cell.Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
        }
    }
}
