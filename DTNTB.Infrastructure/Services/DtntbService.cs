using ClosedXML.Excel;
using Dapper;
using DTNTB.Core.Constants;
using DTNTB.Core.DTOs;
using DTNTB.Core.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using ImageMagick;
using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DTNTB.Infrastructure.Services
{
    public class DtntbService : IDtntbService
    {
        private sealed class RecordAccessRow
        {
            public string? MaDv { get; set; }
            public string? MaNvkt { get; set; }
            public object? HoursElapsed { get; set; }
            public object? AnhCskh { get; set; }
        }

        private readonly string _connString;
        private readonly ICurrentUserService _currentUser;
        private readonly IFileStorageService _fileStorageService; // <-- 1. DỊCH VỤ STORAGE NỘI BỘ
        private readonly long _maxImageSizeBytes;
        private readonly int _maxImagesPerRequest;
        private readonly long _maxRequestImageBytes;
        private readonly int _maxImageDimension;
        private readonly int _jpegQuality;
        private readonly ulong _maxDecodedPixels;

        public DtntbService(
            IConfiguration config,
            ICurrentUserService currentUser,
            IFileStorageService fileStorageService) // <-- 2. INJECT STORAGE SERVICE
        {
            _connString = config.GetConnectionString("ConnectionString_NBH") ?? string.Empty;
            _currentUser = currentUser;
            _fileStorageService = fileStorageService;
            var maxImageSizeMb = int.TryParse(config["Upload:MaxFileSizeInMB"], out var configuredMaxImageSizeMb)
                ? Math.Clamp(configuredMaxImageSizeMb, 1, 20)
                : 10;
            _maxImageSizeBytes = maxImageSizeMb * 1024L * 1024L;
            _maxImagesPerRequest = int.TryParse(config["Upload:MaxFilesPerRequest"], out var configuredMaxFiles)
                ? Math.Clamp(configuredMaxFiles, 1, 10)
                : 5;
            var maxRequestImageMb = int.TryParse(config["Upload:MaxRequestSizeInMB"], out var configuredRequestSizeMb)
                ? Math.Clamp(configuredRequestSizeMb, 1, 25)
                : 15;
            _maxRequestImageBytes = maxRequestImageMb * 1024L * 1024L;
            _maxImageDimension = int.TryParse(config["ImageOptimization:MaxDimension"], out var configuredMaxDimension)
                ? Math.Clamp(configuredMaxDimension, 1280, 3840)
                : 1920;
            _jpegQuality = int.TryParse(config["ImageOptimization:JpegQuality"], out var configuredJpegQuality)
                ? Math.Clamp(configuredJpegQuality, 75, 95)
                : 85;
            var maxDecodedMegapixels = int.TryParse(config["ImageOptimization:MaxDecodedMegapixels"], out var configuredMegapixels)
                ? Math.Clamp(configuredMegapixels, 5, 80)
                : 40;
            _maxDecodedPixels = (ulong)maxDecodedMegapixels * 1_000_000UL;

            // Giới hạn tài nguyên toàn tiến trình để ảnh nén độc hại không chiếm hết RAM/CPU.
            ResourceLimits.Width = 16_384;
            ResourceLimits.Height = 16_384;
            ResourceLimits.ListLength = 100;
            ResourceLimits.Memory = 256UL * 1024UL * 1024UL;
            ResourceLimits.Disk = 512UL * 1024UL * 1024UL;
            ResourceLimits.Thread = 2;
            ResourceLimits.Time = 30;
        }

        private async Task<bool> HasRecordAccessAsync(OracleConnection conn, string? recordMaDv, string? recordMaNv)
        {
            var normalizedMaDv = recordMaDv?.Trim() ?? "";
            switch (_currentUser.ScopeLevel)
            {
                case UserDataScopeLevel.ToanTinh:
                    return true;
                case UserDataScopeLevel.DiaBan:
                    if (string.IsNullOrWhiteSpace(_currentUser.DiaBanId)) return false;
                    const string diaBanQuery = @"SELECT COUNT(1) FROM v_donvi_diaban db
                        WHERE SUBSTR(TRIM(db.ma_dv), 1, 7) = SUBSTR(TRIM(:ma_dv), 1, 7)
                          AND TRIM(db.diaban_id) = TRIM(:diaban_id)";
                    return await conn.ExecuteScalarAsync<int>(diaBanQuery, new
                    {
                        ma_dv = normalizedMaDv,
                        diaban_id = _currentUser.DiaBanId
                    }) > 0;
                case UserDataScopeLevel.DonVi:
                    return string.Equals(
                        normalizedMaDv.Length > 7 ? normalizedMaDv.Substring(0, 7) : normalizedMaDv,
                        _currentUser.MaDv7,
                        StringComparison.OrdinalIgnoreCase);
                case UserDataScopeLevel.ToQuanLy:
                    return string.Equals(
                        normalizedMaDv.Length > 11 ? normalizedMaDv.Substring(0, 11) : normalizedMaDv,
                        _currentUser.MaDv11,
                        StringComparison.OrdinalIgnoreCase);
                case UserDataScopeLevel.NhanVien:
                    return string.Equals(recordMaNv?.Trim(), _currentUser.MaNv, StringComparison.OrdinalIgnoreCase);
                default:
                    return false;
            }
        }

        private static async Task<bool> IsAllowedImageAsync(IFormFile file, string extension)
        {
            await using var stream = file.OpenReadStream();
            var header = new byte[12];
            var bytesRead = await stream.ReadAsync(header, 0, header.Length);

            var isJpeg = bytesRead >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
            var isPng = bytesRead >= 8 && header.Take(8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            return (isJpeg && (extension == ".jpg" || extension == ".jpeg"))
                || (isPng && extension == ".png");
        }

        private async Task<(byte[] Content, string ContentType)?> OptimizeImageAsync(IFormFile file, string extension)
        {
            await using var input = file.OpenReadStream();
            using var image = new MagickImage(input);
            image.AutoOrient();
            image.Resize(new MagickGeometry((uint)_maxImageDimension, (uint)_maxImageDimension) { Greater = true });
            image.Strip(); // Bỏ metadata/thumbnail không cần thiết sau khi đã xoay đúng chiều.

            if (extension is ".jpg" or ".jpeg")
            {
                image.Format = MagickFormat.Jpeg;
                image.Quality = (uint)_jpegQuality;
            }
            else
            {
                // PNG giữ nguyên chất lượng; chỉ nén lossless và hạ kích thước khi quá lớn.
                image.Format = MagickFormat.Png;
            }

            var optimizedContent = image.ToByteArray();

            // Luôn dùng ảnh đã decode/re-encode để loại metadata và dữ liệu nối thêm
            // có thể biến file chỉ kiểm tra header thành một polyglot độc hại.
            return (optimizedContent, extension is ".jpg" or ".jpeg" ? "image/jpeg" : "image/png");
        }

        private async Task<bool> IsWithinDecodedImageLimitAsync(IFormFile file)
        {
            try
            {
                await using var stream = file.OpenReadStream();
                var info = new MagickImageInfo(stream);
                if (info.Width == 0 || info.Height == 0) return false;
                return (ulong)info.Width * info.Height <= _maxDecodedPixels;
            }
            catch (MagickException)
            {
                return false;
            }
        }

        public async Task<List<DropdownItemDto>?> GetDonViAsync()
        {
            if (_currentUser.ScopeLevel != UserDataScopeLevel.ToanTinh) return null;

            using (var conn = new OracleConnection(_connString))
            {
                string query = "SELECT DISTINCT ma_dv as Value, ten_dv || ' (' || ma_dv || ')' as DisplayText FROM brcd_dhgh_kehoach WHERE ma_dv IS NOT NULL AND trangthai_phieu > 0 ORDER BY ma_dv";
                var list = (await conn.QueryAsync<DropdownItemDto>(query)).ToList();
                list.Insert(0, new DropdownItemDto { Value = "ALL", DisplayText = "-- Tất cả đơn vị --" });
                return list;
            }
        }

        public async Task<List<DropdownItemDto>?> GetNvktAsync(string maDv)
        {
            string filterMaDv7 = maDv.Length > 7 ? maDv.Substring(0, 7) : maDv;
            string filterMaDv11 = maDv.Length > 11 ? maDv.Substring(0, 11) : maDv;

            if (_currentUser.ScopeLevel == UserDataScopeLevel.DonVi && _currentUser.MaDv7 != filterMaDv7) return null;
            if (_currentUser.ScopeLevel == UserDataScopeLevel.ToQuanLy && _currentUser.MaDv11 != filterMaDv11) return null;
            if (_currentUser.ScopeLevel == UserDataScopeLevel.NhanVien && !string.Equals(_currentUser.MaDv, maDv, StringComparison.OrdinalIgnoreCase)) return null;

            if (_currentUser.ScopeLevel == UserDataScopeLevel.DiaBan)
            {
                if (maDv == "ALL" || string.IsNullOrWhiteSpace(_currentUser.DiaBanId)) return null;
                using var scopeConn = new OracleConnection(_connString);
                const string scopeQuery = @"SELECT COUNT(1) FROM v_donvi_diaban db
                    WHERE SUBSTR(TRIM(db.ma_dv), 1, 7) = SUBSTR(TRIM(:ma_dv), 1, 7)
                      AND TRIM(db.diaban_id) = TRIM(:diaban_id)";
                var hasAccess = await scopeConn.ExecuteScalarAsync<int>(scopeQuery, new
                {
                    ma_dv = maDv,
                    diaban_id = _currentUser.DiaBanId
                }) > 0;
                if (!hasAccess) return null;
            }

            var list = new List<DropdownItemDto>
            {
                new DropdownItemDto { Value = "ALL", DisplayText = "-- Tất cả nhân viên --" }
            };

            if (maDv == "ALL") return list;

            using (var conn = new OracleConnection(_connString))
            {
                string query = @"
                    SELECT DISTINCT ma_nvkt as Value, ten_nvkt || ' (' || ma_nvkt || ')' as DisplayText 
                    FROM brcd_dhgh_kehoach 
                    WHERE TRIM(ma_dv) = TRIM(:ma_dv) AND ma_nvkt IS NOT NULL AND ten_nvkt IS NOT NULL AND trangthai_phieu > 0
                    ORDER BY DisplayText";
                var dbList = await conn.QueryAsync<DropdownItemDto>(query, new { ma_dv = maDv });
                list.AddRange(dbList);
                return list;
            }
        }

        private (string sqlFilter, DynamicParameters parameters) BuildDataScopeFilter(string? maDv, string? maNvkt, string nguyCo, string? search)
        {
            var parameters = new DynamicParameters();
            string sql = " WHERE t.trangthai_phieu > 0";

            string filterMaDV = maDv ?? "";
            string filterMaNV = maNvkt ?? "";

            switch (_currentUser.ScopeLevel)
            {
                case UserDataScopeLevel.DiaBan:
                    // Fail closed: tài khoản được gán phạm vi địa bàn nhưng thiếu
                    // mã địa bàn tuyệt đối không được rơi xuống truy vấn toàn bộ.
                    if (string.IsNullOrWhiteSpace(_currentUser.DiaBanId))
                    {
                        sql += " AND 1 = 0";
                    }
                    else
                    {
                        sql += @" AND EXISTS (
                            SELECT 1 FROM v_donvi_diaban db 
                            WHERE SUBSTR(TRIM(t.ma_dv), 1, 7) = SUBSTR(TRIM(db.ma_dv), 1, 7) 
                              AND TRIM(db.diaban_id) = TRIM(:diaban_id)
                        )";
                        parameters.Add("diaban_id", _currentUser.DiaBanId, DbType.String);
                    }
                    break;

                case UserDataScopeLevel.DonVi:
                    filterMaDV = _currentUser.MaDv7 ?? "";
                    sql += " AND SUBSTR(TRIM(t.ma_dv), 1, 7) = :ma_dv";
                    parameters.Add("ma_dv", filterMaDV, DbType.String);
                    break;

                case UserDataScopeLevel.ToQuanLy:
                    filterMaDV = _currentUser.MaDv11 ?? "";
                    sql += " AND SUBSTR(TRIM(t.ma_dv), 1, 11) = :ma_dv";
                    parameters.Add("ma_dv", filterMaDV, DbType.String);
                    break;

                case UserDataScopeLevel.NhanVien:
                    filterMaDV = _currentUser.MaDv ?? "";
                    filterMaNV = _currentUser.MaNv ?? "";
                    sql += " AND TRIM(t.ma_dv) = TRIM(:ma_dv)";
                    parameters.Add("ma_dv", filterMaDV, DbType.String);
                    sql += " AND UPPER(TRIM(t.ma_nvkt)) = UPPER(TRIM(:ma_nvkt))";
                    parameters.Add("ma_nvkt", filterMaNV, DbType.String);
                    break;

                default:
                    if (!string.IsNullOrEmpty(filterMaDV) && filterMaDV != "ALL")
                    {
                        sql += " AND TRIM(t.ma_dv) LIKE :ma_dv || '%'";
                        parameters.Add("ma_dv", filterMaDV, DbType.String);
                    }
                    if (!string.IsNullOrEmpty(filterMaNV) && filterMaNV != "ALL")
                    {
                        sql += " AND UPPER(TRIM(t.ma_nvkt)) = UPPER(TRIM(:ma_nvkt))";
                        parameters.Add("ma_nvkt", filterMaNV, DbType.String);
                    }
                    break;
            }

            if (nguyCo == "BINH_THUONG") sql += " AND t.diem_tin_nhiem <= 25";
            else if (nguyCo == "THEO_DOI") sql += " AND t.diem_tin_nhiem > 25 AND t.diem_tin_nhiem <= 28";
            else if (nguyCo == "NGUY_CO") sql += " AND t.diem_tin_nhiem > 28 AND t.diem_tin_nhiem <= 31";
            else if (nguyCo == "CAO") sql += " AND t.diem_tin_nhiem > 31 AND t.diem_tin_nhiem <= 40";
            else if (nguyCo == "RAT_CAO") sql += " AND t.diem_tin_nhiem > 40";

            if (!string.IsNullOrEmpty(search))
            {
                sql += " AND (UPPER(t.ma_tb) LIKE UPPER(:search) OR t.so_dt LIKE :search)";
                parameters.Add("search", "%" + search.Trim() + "%", DbType.String);
            }

            return (sql, parameters);
        }

        public async Task<PaginatedResultDto<DtntbDetailDto>> GetListAsync(
            string? maDv, string? maNvkt, string nguyCo, string? search, int page, int pageSize)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            var (filterClause, parameters) = BuildDataScopeFilter(maDv, maNvkt, nguyCo, search);

            using (var conn = new OracleConnection(_connString))
            {
                string baseSql = $@"
                    FROM brcd_dhgh_kehoach t
                    LEFT JOIN brcd_dhgh_xuly xl ON t.phieu_id = xl.phieu_id
                    {filterClause}";

                string countSql = "SELECT COUNT(1) " + baseSql;
                int totalCount = await conn.ExecuteScalarAsync<int>(countSql, parameters);

                int startRow = (page - 1) * pageSize + 1;
                int endRow = page * pageSize;

                parameters.Add("start_row", startRow, DbType.Int32);
                parameters.Add("end_row", endRow, DbType.Int32);

                string paginatedSql = $@"
                    SELECT * FROM (
                        SELECT a.*, ROWNUM rnum FROM (
                            SELECT t.thuebao_id as ThueBaoId, t.phanvung_id as PhanVungId, t.ma_tb as MaTB, t.ten_tb as TenTB, 
                                   t.diachi_tb as DiaChiTB, t.so_dt as SoDT, t.ma_dv as MaDv, t.ten_nvkt as TenNvkt, 
                                   t.diem_tin_nhiem as DiemTinNhiem, t.phieu_id as PhieuId, t.ngay_giao as NgayGiao, t.trangthai_phieu as TrangThaiPhieu,
                                   xl.diem_goc as DiemGocLs, xl.diem_sau_xl as DiemSauLs, xl.ngay_tao as NgayTaoLs,
                                   CASE WHEN xl.phieu_id IS NOT NULL THEN 1 ELSE 0 END as DaTacNghiep,
                                   CASE 
                                       WHEN t.diem_tin_nhiem <= 25 THEN 'Bình thường'
                                       WHEN t.diem_tin_nhiem <= 28 THEN 'Theo dõi'
                                       WHEN t.diem_tin_nhiem <= 31 THEN 'Nguy cơ'
                                       WHEN t.diem_tin_nhiem <= 40 THEN 'Nguy cơ cao'
                                       ELSE 'Rất cao'
                                   END AS MucNguyCo
                            {baseSql}
                            ORDER BY t.ngay_giao DESC
                        ) a WHERE ROWNUM <= :end_row
                    ) WHERE rnum >= :start_row";

                var items = (await conn.QueryAsync<dynamic>(paginatedSql, parameters)).Select(result => new DtntbDetailDto
                {
                    ThueBaoId = Convert.ToInt64(result.THUEBAOID),
                    PhanVungId = Convert.ToInt32(result.PHANVUNGID),
                    PhieuId = Convert.ToInt64(result.PHIEUID),
                    MaTB = result.MATB?.ToString() ?? "",
                    TenTB = result.TENTB?.ToString() ?? "",
                    DiaChiTB = result.DIACHITB?.ToString() ?? "",
                    SoDT = result.SODT?.ToString() ?? "",
                    MaDv = result.MADV?.ToString() ?? "",
                    TenNvkt = result.TENNVKT?.ToString() ?? "",
                    DiemTinNhiem = Convert.ToInt32(result.DIEMTINNHIEM),
                    DiemGocLs = result.DIEMGOCLS != null ? Convert.ToInt32(result.DIEMGOCLS) : 0,
                    DiemSauLs = result.DIEMSAULS != null ? Convert.ToInt32(result.DIEMSAULS) : 0,
                    NgayTaoLs = result.NGAYTAOLS != null ? Convert.ToDateTime(result.NGAYTAOLS).ToString("dd/MM/yyyy HH:mm") : "-",
                    DaTacNghiep = Convert.ToInt32(result.DATACNGHIEP) == 1,
                    NgayGiao = result.NGAYGIAO != null ? Convert.ToDateTime(result.NGAYGIAO).ToString("dd/MM/yyyy HH:mm") : "-",
                    TrangThaiPhieu = Convert.ToInt32(result.TRANGTHAIPHIEU)
                }).ToList();

                return new PaginatedResultDto<DtntbDetailDto>
                {
                    Items = items,
                    TotalCount = totalCount
                };
            }
        }

        public async Task<RiskCountDto> GetRiskCountAsync(string? maDv, string? maNvkt, string? search)
        {
            // Không áp dụng lọc nguy cơ tại đây để app luôn nhận được toàn bộ các số đếm.
            var (filterClause, parameters) = BuildDataScopeFilter(maDv, maNvkt, "ALL", search);
            const string countSelect = @"
                SELECT
                    COUNT(1) AS Total,
                    NVL(SUM(CASE WHEN t.diem_tin_nhiem <= 25 THEN 1 ELSE 0 END), 0) AS BinhThuong,
                    NVL(SUM(CASE WHEN t.diem_tin_nhiem > 25 AND t.diem_tin_nhiem <= 28 THEN 1 ELSE 0 END), 0) AS TheoDoi,
                    NVL(SUM(CASE WHEN t.diem_tin_nhiem > 28 AND t.diem_tin_nhiem <= 31 THEN 1 ELSE 0 END), 0) AS NguyCo,
                    NVL(SUM(CASE WHEN t.diem_tin_nhiem > 31 AND t.diem_tin_nhiem <= 40 THEN 1 ELSE 0 END), 0) AS Cao,
                    NVL(SUM(CASE WHEN t.diem_tin_nhiem > 40 THEN 1 ELSE 0 END), 0) AS RatCao
                FROM brcd_dhgh_kehoach t";

            using var conn = new OracleConnection(_connString);
            var result = await conn.QuerySingleAsync<dynamic>(countSelect + filterClause, parameters);
            return new RiskCountDto
            {
                Total = Convert.ToInt32(result.TOTAL),
                BinhThuong = Convert.ToInt32(result.BINHTHUONG),
                TheoDoi = Convert.ToInt32(result.THEODOI),
                NguyCo = Convert.ToInt32(result.NGUYCO),
                Cao = Convert.ToInt32(result.CAO),
                RatCao = Convert.ToInt32(result.RATCAO)
            };
        }

        public static int TinhDiemCuoc(double soThang, bool isTrichNo)
        {
            if (soThang >= 10.0) return 0;
            if (soThang >= 9.0) return 1;
            if (soThang >= 8.0) return 2;
            if (soThang >= 7.0) return 3;
            if (soThang >= 6.0) return 4;
            if (soThang >= 5.0) return 5;
            if (soThang >= 4.0) return 6;
            if (soThang >= 3.0) return 7;
            if (soThang >= 2.0) return 8;
            if (soThang >= 1.0) return 9;
            return isTrichNo ? 5 : 10;
        }

        public async Task<DtntbDetailDto?> GetDetailAsync(long phieuId)
        {
            using (var conn = new OracleConnection(_connString))
            {
                string query = @"
            SELECT t.ma_tb, t.ten_tb, t.so_dt, t.diachi_tb, t.diem_tin_nhiem, t.ma_dv, t.ma_nvkt,
                   t.tgsd, t.diem_tgsd, t.ngay_sd,
                   t.co_mytv, t.co_mesh, t.co_cam, t.diem_dadv,
                   t.so_thang_tt, t.diem_tttt, t.ngay_ktdc,
                   t.tuoi_ont, t.diem_tbi, t.loai_ont, t.ten_vt,
                   t.sl_suyhao, t.diem_suyhao,
                   t.sl_offlos, t.diem_offlos,
                   t.solan_bh_1t, t.diem_bhll,
                   t.diem_kohl,
                   t.tgsc_tb, t.diem_tgsc,
                   t.thuebao_id as ThueBaoId, t.phanvung_id as PhanVungId, t.phieu_id, t.ngay_giao, t.trangthai_phieu,
                   ROUND((SYSDATE - t.ngay_giao) * 24, 2) as hours_elapsed,
                   xl.da_thay_thietbi, xl.da_thietbi_tot, xl.da_sua_suyhao, 
                   xl.da_tuvan_cuoc_6t, xl.da_tuvan_cuoc_12t, xl.da_trichno_tudong, 
                   xl.da_tuvan_mytv, xl.da_tuvan_mesh, xl.da_tuvan_cam,
                   xl.da_tuvan_cuoc, xl.da_tuvan_combo,
                   xl.ghi_chu, xl.anh_cskh,
                   xl.da_dung_cam_vnpt, xl.sl_cam_vnpt, xl.da_dung_cam_other, xl.sl_cam_other,
                   xl.da_dung_toto_vnpt, xl.sl_toto_vnpt, xl.da_dung_toto_other, xl.sl_toto_other
            FROM brcd_dhgh_kehoach t
            LEFT JOIN brcd_dhgh_xuly xl ON t.phieu_id = xl.phieu_id
            WHERE t.phieu_id = :phieu_id";

                var result = await conn.QueryFirstOrDefaultAsync<dynamic>(query, new { phieu_id = phieuId });
                if (result == null) return null;

                if (!await HasRecordAccessAsync(conn, result.MA_DV?.ToString(), result.MA_NVKT?.ToString())) return null;

                // SLA 72 GIỜ
                double hoursElapsed = result.HOURS_ELAPSED != null ? Convert.ToDouble(result.HOURS_ELAPSED) : 0;
                bool isSlaExpired = hoursElapsed > 72;

                double soThangConLai = 0;
                if (result.NGAY_KTDC != null)
                {
                    DateTime ngayKtdc = Convert.ToDateTime(result.NGAY_KTDC);
                    if (ngayKtdc > DateTime.Today)
                    {
                        double diffDays = (ngayKtdc - DateTime.Today).TotalDays;
                        soThangConLai = Math.Round(diffDays / 30.0, 1);
                    }
                }

                List<string> listImages = new List<string>();
                string rawImgString = result.ANH_CSKH?.ToString() ?? "";
                if (!string.IsNullOrEmpty(rawImgString))
                {
                    listImages = rawImgString.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                             .Select(s => ToServer2MediaUrl(s.Trim().Replace('\\', '/')))
                                             .ToList();
                }

                bool hasOrigMyTV = result.CO_MYTV?.ToString() == "1";
                bool hasOrigMeshCam = (result.CO_MESH?.ToString() == "1") || (result.CO_CAM?.ToString() == "1");

                string dichVu = "Internet đơn lẻ";
                if (hasOrigMyTV && hasOrigMeshCam) dichVu = "Internet + MyTV + Mesh/Cam";
                else if (hasOrigMeshCam) dichVu = "Internet + Mesh/Cam";
                else if (hasOrigMyTV) dichVu = "Internet + MyTV";

                bool c6t = result.DA_TUVAN_CUOC_6T?.ToString() == "1";
                bool c12t = result.DA_TUVAN_CUOC_12T?.ToString() == "1";
                if (!c6t && !c12t && result.DA_TUVAN_CUOC?.ToString() == "1") c6t = true;

                bool pMyTv = result.DA_TUVAN_MYTV?.ToString() == "1";
                bool pMesh = result.DA_TUVAN_MESH?.ToString() == "1";
                bool pCam = result.DA_TUVAN_CAM?.ToString() == "1";
                if (!pMyTv && !pMesh && !pCam && result.DA_TUVAN_COMBO?.ToString() == "1") pMyTv = true;

                var detail = new DtntbDetailDto
                {
                    ThueBaoId = Convert.ToInt64(result.THUEBAOID),
                    PhanVungId = Convert.ToInt32(result.PHANVUNGID),
                    PhieuId = Convert.ToInt64(result.PHIEU_ID),
                    MaTB = result.MA_TB?.ToString() ?? "",
                    TenTB = result.TEN_TB?.ToString() ?? "",
                    SoDT = result.SO_DT?.ToString() ?? "",
                    DiaChiTB = result.DIACHI_TB?.ToString() ?? "",
                    DiemTinNhiem = Convert.ToInt32(result.DIEM_TIN_NHIEM),
                    MaDv = result.MA_DV?.ToString() ?? "",
                    TenNvkt = result.MA_NVKT?.ToString() ?? "",
                    Tgsd = result.TGSD != null ? Convert.ToDouble(result.TGSD) : 0,
                    DiemTgsd = result.DIEM_TGSD != null ? Convert.ToInt32(result.DIEM_TGSD) : 0,
                    DiemDadv = result.DIEM_DADV != null ? Convert.ToInt32(result.DIEM_DADV) : 0,
                    DichVuDetail = dichVu,
                    SoThangTt = result.SO_THANG_TT != null ? Convert.ToInt32(result.SO_THANG_TT) : 0,
                    DiemTttt = result.DIEM_TTTT != null ? Convert.ToInt32(result.DIEM_TTTT) : 0,
                    TuoiOnt = result.TUOI_ONT != null ? Convert.ToDouble(result.TUOI_ONT) : 0,
                    DiemTbi = result.DIEM_TBI != null ? Convert.ToInt32(result.DIEM_TBI) : 0,
                    SlSuyHao = result.SL_SUYHAO != null ? Convert.ToInt32(result.SL_SUYHAO) : 0,
                    DiemSuyHao = result.DIEM_SUYHAO != null ? Convert.ToInt32(result.DIEM_SUYHAO) : 0,
                    SlOffLos = result.SL_OFFLOS != null ? Convert.ToInt32(result.SL_OFFLOS) : 0,
                    DiemOffLos = result.DIEM_OFFLOS != null ? Convert.ToInt32(result.DIEM_OFFLOS) : 0,
                    SolanBh1t = result.SOLAN_BH_1T != null ? Convert.ToInt32(result.SOLAN_BH_1T) : 0,
                    DiemBhll = result.DIEM_BHLL != null ? Convert.ToInt32(result.DIEM_BHLL) : 0,
                    DiemKohl = result.DIEM_KOHL != null ? Convert.ToInt32(result.DIEM_KOHL) : 0,
                    TgscTb = result.TGSC_TB != null ? Convert.ToDouble(result.TGSC_TB) : 0,
                    DiemTgsc = result.DIEM_TGSC != null ? Convert.ToDouble(result.DIEM_TGSC) : 0,

                    DaThayThietBi = result.DA_THAY_THIETBI?.ToString() == "1",
                    DaThietBiTot = result.DA_THIETBI_TOT?.ToString() == "1",
                    DaSuaSuyHao = result.DA_SUA_SUYHAO?.ToString() == "1",
                    DaTuVanCuoc6T = c6t,
                    DaTuVanCuoc12T = c12t,
                    DaTrichNoTuDong = result.DA_TRICHNO_TUDONG?.ToString() == "1",
                    DaTuVanMyTV = pMyTv,
                    DaTuVanMesh = pMesh,
                    DaTuVanCam = pCam,
                    GhiChu = result.GHI_CHU?.ToString() ?? "",

                    CoMyTV = hasOrigMyTV,
                    CoMeshCam = hasOrigMeshCam,
                    SoThangConLai = soThangConLai,

                    NgayGiao = result.NGAY_GIAO != null ? Convert.ToDateTime(result.NGAY_GIAO).ToString("dd/MM/yyyy HH:mm") : "",
                    TrangThaiPhieu = Convert.ToInt32(result.TRANGTHAI_PHIEU),
                    NgaySd = result.NGAY_SD != null ? Convert.ToDateTime(result.NGAY_SD).ToString("dd/MM/yyyy") : "Chưa có",
                    NgayKtdc = result.NGAY_KTDC != null ? Convert.ToDateTime(result.NGAY_KTDC).ToString("dd/MM/yyyy") : "Chưa có",
                    LoaiOnt = result.TEN_VT?.ToString() ?? "-",
                    IsSlaExpired = isSlaExpired,
                    AnhCskhUrls = listImages,

                    DaDungCamVnpt = result.DA_DUNG_CAM_VNPT?.ToString() == "1",
                    SlCamVnpt = result.SL_CAM_VNPT != null ? Convert.ToInt32(result.SL_CAM_VNPT) : 0,
                    DaDungCamOther = result.DA_DUNG_CAM_OTHER?.ToString() == "1",
                    SlCamOther = result.SL_CAM_OTHER != null ? Convert.ToInt32(result.SL_CAM_OTHER) : 0,
                    DaDungTotoVnpt = result.DA_DUNG_TOTO_VNPT?.ToString() == "1",
                    SlTotoVnpt = result.SL_TOTO_VNPT != null ? Convert.ToInt32(result.SL_TOTO_VNPT) : 0,
                    DaDungTotoOther = result.DA_DUNG_TOTO_OTHER?.ToString() == "1",
                    SlTotoOther = result.SL_TOTO_OTHER != null ? Convert.ToInt32(result.SL_TOTO_OTHER) : 0,
                };

                string thuebaoId = result.THUEBAOID.ToString();
                string phanvungId = result.PHANVUNGID.ToString();
                detail.LichSu = await GetLichSuTacNghiepAsync(thuebaoId, phanvungId);

                return detail;
            }
        }

        /// <summary>
        /// Chuyển đường dẫn ảnh lưu trong DB (dạng cũ "/uploads/MatLuoi/xxx.jpg" 
        /// hoặc dạng mới "MatLuoi/xxx.jpg") thành URL Angular gọi qua Server 2,
        /// không bao giờ để lộ path/domain của Server 1.
        /// </summary>
        private static string ToServer2MediaUrl(string rawDbPath)
        {
            if (string.IsNullOrWhiteSpace(rawDbPath)) return "";

            var clean = rawDbPath.TrimStart('/');

            if (clean.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
            {
                clean = clean.Substring("uploads/".Length); // "/uploads/MatLuoi/x.jpg" -> "MatLuoi/x.jpg"
            }

            return $"/api/media/{clean}"; // Angular luôn gọi endpoint này trên Server 2
        }

        private async Task<List<LichSuTacNghiepDto>> GetLichSuTacNghiepAsync(string thuebaoId, string phanvungId)
        {
            using (var conn = new OracleConnection(_connString))
            {
                string query = @"
            SELECT xl.phieu_id, t.ngay_giao, xl.ngay_tao, xl.nguoi_xuly, xl.ten_nv, xl.ghi_chu, xl.diem_goc, xl.diem_sau_xl,
                   xl.da_thay_thietbi, xl.da_thietbi_tot, xl.da_sua_suyhao, 
                   xl.da_tuvan_cuoc_6t, xl.da_tuvan_cuoc_12t, xl.da_trichno_tudong, 
                   xl.da_tuvan_mytv, xl.da_tuvan_mesh, xl.da_tuvan_cam,
                   xl.da_tuvan_cuoc, xl.da_tuvan_combo
            FROM brcd_dhgh_xuly xl
            LEFT JOIN brcd_dhgh_kehoach t ON xl.phieu_id = t.phieu_id
            WHERE xl.thuebao_id = :thuebao_id AND xl.phanvung_id = :phanvung_id
            ORDER BY xl.ngay_tao DESC, xl.xuly_id DESC";

                var list = await conn.QueryAsync<dynamic>(query, new { thuebao_id = Convert.ToInt64(thuebaoId), phanvung_id = Convert.ToInt32(phanvungId) });
                return list.Select(r => new LichSuTacNghiepDto
                {
                    PhieuId = r.PHIEU_ID != null ? Convert.ToInt64(r.PHIEU_ID) : null,
                    NgayGiao = r.NGAY_GIAO != null ? Convert.ToDateTime(r.NGAY_GIAO).ToString("dd/MM/yyyy HH:mm") : "-",
                    NgayTao = Convert.ToDateTime(r.NGAY_TAO).ToString("dd/MM/yyyy HH:mm"),
                    NguoiXuly = r.NGUOI_XULY?.ToString() ?? "",
                    TenNv = r.TEN_NV?.ToString() ?? "",
                    DaThayThietBi = Convert.ToInt16(r.DA_THAY_THIETBI),
                    DaThietBiTot = Convert.ToInt16(r.DA_THIETBI_TOT),
                    DaSuaSuyHao = Convert.ToInt16(r.DA_SUA_SUYHAO),
                    DaTuVanCuoc6T = Convert.ToInt16(r.DA_TUVAN_CUOC_6T),
                    DaTuVanCuoc12T = Convert.ToInt16(r.DA_TUVAN_CUOC_12T),
                    DaTrichNoTuDong = Convert.ToInt16(r.DA_TRICHNO_TUDONG),
                    DaTuVanMyTV = Convert.ToInt16(r.DA_TUVAN_MYTV),
                    DaTuVanMesh = Convert.ToInt16(r.DA_TUVAN_MESH),
                    DaTuVanCam = Convert.ToInt16(r.DA_TUVAN_CAM),
                    DaTuVanCuoc = Convert.ToInt16(r.DA_TUVAN_CUOC),
                    DaTuVanCombo = Convert.ToInt16(r.DA_TUVAN_COMBO),
                    GhiChu = r.GHI_CHU?.ToString() ?? "",
                    DiemGoc = Convert.ToInt32(r.DIEM_GOC),
                    DiemSauXl = Convert.ToInt32(r.DIEM_SAU_XL)
                }).ToList();
            }
        }

        public async Task<bool> SaveTacNghiepUpgradeAsync(SaveTacNghiepFormDto model)
        {
            string maNv = _currentUser.MaNv ?? "";
            string tenNv = _currentUser.TenNv ?? "";

            // 1. KIỂM TRA SLA 72 GIỜ VÀ QUYỀN TRÊN BẢN GHI
            using (var connSla = new OracleConnection(_connString))
            {
                string slaQuery = "SELECT ROUND((SYSDATE - ngay_giao) * 24, 2) as hours_elapsed, ma_dv, ma_nvkt FROM brcd_dhgh_kehoach WHERE phieu_id = :phieu_id";
                var target = await connSla.QueryFirstOrDefaultAsync<dynamic>(slaQuery, new { phieu_id = model.PhieuId });
                if (target == null) return false;

                double hoursElapsed = Convert.ToDouble(target.HOURS_ELAPSED);
                if (hoursElapsed > 72) return false;

                if (!await HasRecordAccessAsync(connSla, target.MA_DV?.ToString(), target.MA_NVKT?.ToString())) return false;
            }

            // 2. LỚP KIỂM TRA (VALIDATION)
            if (!model.IsUnresolved && !model.DaThayThietBi && !model.DaThietBiTot && !model.DaSuaSuyHao &&
                !model.DaTuVanCuoc6T && !model.DaTuVanCuoc12T && !model.DaTrichNoTuDong &&
                !model.DaTuVanMyTV && !model.DaTuVanMesh && !model.DaTuVanCam)
            {
                return false;
            }

            if (model.IsUnresolved && string.IsNullOrEmpty(model.GhiChu?.Trim()))
            {
                return false;
            }


            // 3. XỬ LÝ LƯU ẢNH QUA STORAGE SERVER (SERVER 1)
            string relativeFilePaths = "";
            if (model.fuAnhCSKH != null && model.fuAnhCSKH.Count > 0)
            {
                if (model.fuAnhCSKH.Count > _maxImagesPerRequest) return false;
                if (model.fuAnhCSKH.Sum(file => file.Length) > _maxRequestImageBytes) return false;

                List<string> listSavedUrls = new List<string>();
                int fileIndex = 1;

                foreach (var file in model.fuAnhCSKH)
                {
                    if (file.Length > 0)
                    {
                        string ext = Path.GetExtension(file.FileName).ToLower();
                        if (file.Length > _maxImageSizeBytes
                            || !await IsAllowedImageAsync(file, ext)
                            || !await IsWithinDecodedImageLimitAsync(file))
                        {
                            return false;
                        }

                        if (ext == ".jpg" || ext == ".jpeg" || ext == ".png")
                        {
                            string newFileName = $"{model.PhieuId}_{DateTime.Now:yyyyMMddHHmmss}_{fileIndex}{ext}";

                            // 1. Đường dẫn vật lý gửi sang Server 1 (Lưu vào thư mục con MatLuoi):
                            // Server 1 sẽ lưu tại: D:\DataUpload\MatLuoi\{newFileName}
                            string targetPathOnServer1 = $"MatLuoi/{newFileName}";

                            // 2. Tối ưu ảnh trước khi gửi: JPEG chất lượng cao, PNG lossless.
                            // Nếu ảnh gốc đã nhỏ hơn sau tối ưu thì giữ nguyên ảnh gốc.
                            var optimizedImage = await OptimizeImageAsync(file, ext);
                            bool isSaved;
                            if (optimizedImage is null)
                            {
                                isSaved = await _fileStorageService.SaveFileAsync(targetPathOnServer1, file);
                            }
                            else
                            {
                                await using var optimizedStream = new MemoryStream(optimizedImage.Value.Content, writable: false);
                                var optimizedFile = new FormFile(
                                    optimizedStream,
                                    0,
                                    optimizedImage.Value.Content.Length,
                                    "file",
                                    newFileName)
                                {
                                    Headers = new HeaderDictionary(),
                                    ContentType = optimizedImage.Value.ContentType
                                };
                                isSaved = await _fileStorageService.SaveFileAsync(targetPathOnServer1, optimizedFile);
                            }

                            if (!isSaved)
                            {
                                throw new Exception($"Không thể lưu file {newFileName} sang Server 1 (StorageServer)!");
                            }

                            // 3. Chuỗi lưu vào Oracle Database (Đúng chuẩn /uploads/MatLuoi/... cũ của bạn):
                            listSavedUrls.Add($"/uploads/MatLuoi/{newFileName}");
                            fileIndex++;
                        }
                        else
                        {
                            return false;
                        }
                    }
                }

                if (listSavedUrls.Count > 0)
                {
                    relativeFilePaths = string.Join(";", listSavedUrls);
                }
            }

            // Nếu lần này không chọn ảnh mới, giữ lại ảnh cũ trong DB
            if (string.IsNullOrEmpty(relativeFilePaths))
            {
                string rawDbPath = await GetExistingImagePathAsync(model.PhieuId);
                if (!string.IsNullOrEmpty(rawDbPath))
                {
                    relativeFilePaths = rawDbPath;
                }
            }



            // BẮT BUỘC CÓ ẢNH NẾU CHỌN "THIẾT BỊ TỐT"
            if (model.DaThietBiTot && string.IsNullOrEmpty(relativeFilePaths))
            {
                return false;
            }

            short dCamVnpt = (short)(model.DaDungCamVnpt ? 1 : 0);
            int qCamVnpt = dCamVnpt == 1 ? model.SlCamVnpt : 0;
            short dCamOther = (short)(model.DaDungCamOther ? 1 : 0);
            int qCamOther = dCamOther == 1 ? model.SlCamOther : 0;

            short dTotoVnpt = (short)(model.DaDungTotoVnpt ? 1 : 0);
            int qTotoVnpt = dTotoVnpt == 1 ? model.SlTotoVnpt : 0;
            short dTotoOther = (short)(model.DaDungTotoOther ? 1 : 0);
            int qTotoOther = dTotoOther == 1 ? model.SlTotoOther : 0;

            using (var conn = new OracleConnection(_connString))
            {
                await conn.OpenAsync();
                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        string selectSql = @"
                    SELECT thuebao_id, phanvung_id, diem_tin_nhiem, ma_dv, ten_dv, 
                           diem_tbi, diem_suyhao, diem_tttt, diem_dadv,
                           ngay_ktdc, co_mytv, co_mesh, co_cam
                    FROM brcd_dhgh_kehoach 
                    WHERE phieu_id = :phieu_id";

                        var kh = await conn.QueryFirstOrDefaultAsync<dynamic>(selectSql, new { phieu_id = model.PhieuId }, trans);
                        if (kh == null) return false;

                        long thuebaoId = Convert.ToInt64(kh.THUEBAO_ID);
                        int phanvungId = Convert.ToInt32(kh.PHANVUNG_ID);
                        int diemGoc = Convert.ToInt32(kh.DIEM_TIN_NHIEM);
                        string maDv = kh.MA_DV?.ToString() ?? "";
                        string tenDv = kh.TEN_DV?.ToString() ?? "";

                        int pTbi = kh.DIEM_TBI != null ? Convert.ToInt32(kh.DIEM_TBI) : 0;
                        int pSuyhao = kh.DIEM_SUYHAO != null ? Convert.ToInt32(kh.DIEM_SUYHAO) : 0;
                        int pTttt = kh.DIEM_TTTT != null ? Convert.ToInt32(kh.DIEM_TTTT) : 0;
                        int pDadv = kh.DIEM_DADV != null ? Convert.ToInt32(kh.DIEM_DADV) : 0;

                        double soThangConLai = 0;
                        if (kh.NGAY_KTDC != null)
                        {
                            DateTime ngayKtdc = Convert.ToDateTime(kh.NGAY_KTDC);
                            if (ngayKtdc > DateTime.Today)
                            {
                                soThangConLai = Math.Round((ngayKtdc - DateTime.Today).TotalDays / 30.0, 1);
                            }
                        }

                        bool hasOrigMyTV = kh.CO_MYTV?.ToString() == "1";
                        bool hasOrigMeshCam = (kh.CO_MESH?.ToString() == "1") || (kh.CO_CAM?.ToString() == "1");

                        short thayTbi = (short)(model.IsUnresolved ? 0 : (model.DaThayThietBi ? 1 : 0));
                        short tbiTot = (short)(model.IsUnresolved ? 0 : (model.DaThietBiTot ? 1 : 0));
                        short suaSuyHao = (short)(model.IsUnresolved ? 0 : (model.DaSuaSuyHao ? 1 : 0));

                        short cuoc6T = (short)(model.IsUnresolved ? 0 : (model.DaTuVanCuoc6T ? 1 : 0));
                        short cuoc12T = (short)(model.IsUnresolved ? 0 : (model.DaTuVanCuoc12T ? 1 : 0));
                        short trichNo = (short)(model.IsUnresolved ? 0 : (model.DaTrichNoTuDong ? 1 : 0));
                        short tuVanCuoc = (short)(cuoc6T == 1 || cuoc12T == 1 ? 1 : 0);

                        short pMyTv = (short)(model.IsUnresolved ? 0 : (model.DaTuVanMyTV ? 1 : 0));
                        short pMesh = (short)(model.IsUnresolved ? 0 : (model.DaTuVanMesh ? 1 : 0));
                        short pCam = (short)(model.IsUnresolved ? 0 : (model.DaTuVanCam ? 1 : 0));
                        short tuVanCombo = (short)(pMyTv == 1 || pMesh == 1 || pCam == 1 ? 1 : 0);

                        int computedDiemSauXl = diemGoc;
                        if (!model.IsUnresolved)
                        {
                            // 1. Giảm điểm Thiết bị
                            int minusTbi = 0;
                            if (thayTbi == 1) minusTbi = pTbi;
                            else if (tbiTot == 1) minusTbi = (pTbi > 5) ? (pTbi - 5) : 0;

                            // 2. Giảm điểm Suy hao
                            int minusSuyhao = (suaSuyHao == 1) ? pSuyhao : 0;

                            // 3. Giảm điểm Cước
                            double soThangMoi = soThangConLai;
                            if (cuoc6T == 1) soThangMoi += 6.0;
                            else if (cuoc12T == 1) soThangMoi += 12.0;

                            int newTtttScore;
                            if (cuoc6T == 1 || cuoc12T == 1) newTtttScore = TinhDiemCuoc(soThangMoi, false);
                            else if (trichNo == 1) newTtttScore = Math.Min(pTttt, 5);
                            else newTtttScore = pTttt;

                            int minusTttt = pTttt - newTtttScore;
                            if (minusTttt < 0) minusTttt = 0;

                            // 4. Giảm điểm Combo
                            bool hasNewMyTV = hasOrigMyTV || (pMyTv == 1);
                            bool hasNewMeshCam = hasOrigMeshCam || (pMesh == 1 || pCam == 1);

                            int newDadvScore;
                            if (hasNewMyTV && hasNewMeshCam) newDadvScore = 0;
                            else if (hasNewMeshCam) newDadvScore = 2;
                            else if (hasNewMyTV) newDadvScore = 3;
                            else newDadvScore = 5;

                            if (newDadvScore > pDadv) newDadvScore = pDadv;
                            int minusDadv = pDadv - newDadvScore;
                            if (minusDadv < 0) minusDadv = 0;

                            computedDiemSauXl = diemGoc - minusTbi - minusSuyhao - minusTttt - minusDadv;
                            if (computedDiemSauXl < 0) computedDiemSauXl = 0;
                        }

                        string ghiChu = (model.GhiChu ?? "").Trim();
                        if (model.IsUnresolved && !ghiChu.StartsWith("[Chưa xử lý được]"))
                        {
                            ghiChu = "[Chưa xử lý được] " + ghiChu;
                        }

                        string mergeQuery = @"
                    MERGE INTO brcd_dhgh_xuly target
                    USING (
                        SELECT :phieu_id as phieu_id, :thuebao_id as thuebao_id, :phanvung_id as phanvung_id,
                               :da_thay_thietbi as da_thay_thietbi, :da_thietbi_tot as da_thietbi_tot,
                               :da_sua_suyhao as da_sua_suyhao, 
                               :da_tuvan_cuoc as da_tuvan_cuoc,
                               :da_tuvan_cuoc_6t as da_tuvan_cuoc_6t, :da_tuvan_cuoc_12t as da_tuvan_cuoc_12t,
                               :da_trichno_tudong as da_trichno_tudong,
                               :da_tuvan_combo as da_tuvan_combo,
                               :da_tuvan_mytv as da_tuvan_mytv, :da_tuvan_mesh as da_tuvan_mesh, :da_tuvan_cam as da_tuvan_cam,
                               :diem_goc as diem_goc, :diem_sau_xl as diem_sau_xl, :ghi_chu as ghi_chu,
                               :nguoi_xuly as nguoi_xuly, :ten_nv as ten_nv, :ma_dv as ma_dv, :ten_dv as ten_dv, :anh_cskh as anh_cskh,
                               :da_dung_cam_vnpt as da_dung_cam_vnpt, :sl_cam_vnpt as sl_cam_vnpt,
                               :da_dung_cam_other as da_dung_cam_other, :sl_cam_other as sl_cam_other,
                               :da_dung_toto_vnpt as da_dung_toto_vnpt, :sl_toto_vnpt as sl_toto_vnpt,
                               :da_dung_toto_other as da_dung_toto_other, :sl_toto_other as sl_toto_other
                        FROM dual
                    ) source
                    ON (target.phieu_id = source.phieu_id)
                    WHEN MATCHED THEN
                        UPDATE SET 
                            target.da_thay_thietbi = source.da_thay_thietbi,
                            target.da_thietbi_tot = source.da_thietbi_tot,
                            target.da_sua_suyhao = source.da_sua_suyhao,
                            target.da_tuvan_cuoc = source.da_tuvan_cuoc,
                            target.da_tuvan_cuoc_6t = source.da_tuvan_cuoc_6t,
                            target.da_tuvan_cuoc_12t = source.da_tuvan_cuoc_12t,
                            target.da_trichno_tudong = source.da_trichno_tudong,
                            target.da_tuvan_combo = source.da_tuvan_combo,
                            target.da_tuvan_mytv = source.da_tuvan_mytv,
                            target.da_tuvan_mesh = source.da_tuvan_mesh,
                            target.da_tuvan_cam = source.da_tuvan_cam,
                            target.diem_goc = source.diem_goc,
                            target.diem_sau_xl = source.diem_sau_xl,
                            target.ghi_chu = source.ghi_chu,
                            target.nguoi_xuly = source.nguoi_xuly,
                            target.ten_nv = source.ten_nv,
                            target.anh_cskh = source.anh_cskh,
                            target.da_dung_cam_vnpt = source.da_dung_cam_vnpt,
                            target.sl_cam_vnpt = source.sl_cam_vnpt,
                            target.da_dung_cam_other = source.da_dung_cam_other,
                            target.sl_cam_other = source.sl_cam_other,
                            target.da_dung_toto_vnpt = source.da_dung_toto_vnpt,
                            target.sl_toto_vnpt = source.sl_toto_vnpt,
                            target.da_dung_toto_other = source.da_dung_toto_other,
                            target.sl_toto_other = source.sl_toto_other,
                            target.ngay_tao = SYSDATE
                    WHEN NOT MATCHED THEN
                        INSERT (
                            phieu_id, thuebao_id, phanvung_id, da_thay_thietbi, da_thietbi_tot, da_sua_suyhao, 
                            da_tuvan_cuoc, da_tuvan_cuoc_6t, da_tuvan_cuoc_12t, da_trichno_tudong, 
                            da_tuvan_combo, da_tuvan_mytv, da_tuvan_mesh, da_tuvan_cam,
                            diem_goc, diem_sau_xl, ghi_chu, nguoi_xuly, ten_nv, ma_dv, ten_dv, anh_cskh,
                            da_dung_cam_vnpt, sl_cam_vnpt,
                            da_dung_cam_other, sl_cam_other,
                            da_dung_toto_vnpt, sl_toto_vnpt,
                            da_dung_toto_other, sl_toto_other, ngay_tao
                        ) VALUES (
                            source.phieu_id, source.thuebao_id, source.phanvung_id, source.da_thay_thietbi, source.da_thietbi_tot, source.da_sua_suyhao, 
                            source.da_tuvan_cuoc, source.da_tuvan_cuoc_6t, source.da_tuvan_cuoc_12t, source.da_trichno_tudong, 
                            source.da_tuvan_combo, source.da_tuvan_mytv, source.da_tuvan_mesh, source.da_tuvan_cam,
                            source.diem_goc, source.diem_sau_xl, source.ghi_chu, source.nguoi_xuly, source.ten_nv, source.ma_dv, source.ten_dv, source.anh_cskh,
                            source.da_dung_cam_vnpt, source.sl_cam_vnpt,
                            source.da_dung_cam_other, source.sl_cam_other,
                            source.da_dung_toto_vnpt, source.sl_toto_vnpt,
                            source.da_dung_toto_other, source.sl_toto_other, SYSDATE
                        )";

                        var mergeParams = new DynamicParameters();
                        mergeParams.Add("phieu_id", model.PhieuId, DbType.Int64);
                        mergeParams.Add("thuebao_id", thuebaoId, DbType.Int64);
                        mergeParams.Add("phanvung_id", phanvungId, DbType.Int32);
                        mergeParams.Add("da_thay_thietbi", thayTbi, DbType.Int16);
                        mergeParams.Add("da_thietbi_tot", tbiTot, DbType.Int16);
                        mergeParams.Add("da_sua_suyhao", suaSuyHao, DbType.Int16);
                        mergeParams.Add("da_tuvan_cuoc", tuVanCuoc, DbType.Int16);
                        mergeParams.Add("da_tuvan_cuoc_6t", cuoc6T, DbType.Int16);
                        mergeParams.Add("da_tuvan_cuoc_12t", cuoc12T, DbType.Int16);
                        mergeParams.Add("da_trichno_tudong", trichNo, DbType.Int16);
                        mergeParams.Add("da_tuvan_combo", tuVanCombo, DbType.Int16);
                        mergeParams.Add("da_tuvan_mytv", pMyTv, DbType.Int16);
                        mergeParams.Add("da_tuvan_mesh", pMesh, DbType.Int16);
                        mergeParams.Add("da_tuvan_cam", pCam, DbType.Int16);
                        mergeParams.Add("diem_goc", diemGoc, DbType.Int32);
                        mergeParams.Add("diem_sau_xl", computedDiemSauXl, DbType.Int32);
                        mergeParams.Add("ghi_chu", ghiChu, DbType.String);
                        mergeParams.Add("nguoi_xuly", maNv, DbType.String);
                        mergeParams.Add("ten_nv", tenNv, DbType.String);
                        mergeParams.Add("ma_dv", maDv, DbType.String);
                        mergeParams.Add("ten_dv", tenDv, DbType.String);
                        mergeParams.Add("anh_cskh", !string.IsNullOrEmpty(relativeFilePaths) ? (object)relativeFilePaths : DBNull.Value, DbType.String);

                        mergeParams.Add("da_dung_cam_vnpt", dCamVnpt, DbType.Int16);
                        mergeParams.Add("sl_cam_vnpt", qCamVnpt, DbType.Int32);
                        mergeParams.Add("da_dung_cam_other", dCamOther, DbType.Int16);
                        mergeParams.Add("sl_cam_other", qCamOther, DbType.Int32);
                        mergeParams.Add("da_dung_toto_vnpt", dTotoVnpt, DbType.Int16);
                        mergeParams.Add("sl_toto_vnpt", qTotoVnpt, DbType.Int32);
                        mergeParams.Add("da_dung_toto_other", dTotoOther, DbType.Int16);
                        mergeParams.Add("sl_toto_other", qTotoOther, DbType.Int32);

                        await conn.ExecuteAsync(mergeQuery, mergeParams, trans);

                        int statusPhieu = model.IsUnresolved ? 3 : 2;
                        string updateKhSql = "UPDATE brcd_dhgh_kehoach SET trangthai_phieu = :status WHERE phieu_id = :phieu_id";
                        await conn.ExecuteAsync(updateKhSql, new { status = statusPhieu, phieu_id = model.PhieuId }, trans);

                        trans.Commit();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();
                        System.Diagnostics.Debug.WriteLine(ex.Message);
                        return false;
                    }
                }
            }
        }

        public async Task<bool> DeleteImageUpgradeAsync(DeleteImageRequestDto model)
        {
            // 1. KIỂM TRA SLA ĐỒNG BỘ 72 GIỜ (GIỐNG FRONTEND VÀ HÀM LƯU)
            using (var connSla = new OracleConnection(_connString))
            {
                string slaQuery = "SELECT ROUND((SYSDATE - ngay_giao) * 24, 2) AS HoursElapsed, ma_dv AS MaDv, ma_nvkt AS MaNvkt FROM brcd_dhgh_kehoach WHERE phieu_id = :phieu_id";
                var target = await connSla.QueryFirstOrDefaultAsync<RecordAccessRow>(slaQuery, new { phieu_id = model.PhieuId });
                if (target == null || !await HasRecordAccessAsync(connSla, target.MaDv, target.MaNvkt))
                {
                    return false;
                }

                object? hoursElapsedRaw = target.HoursElapsed;
                if (hoursElapsedRaw is not null && hoursElapsedRaw is not DBNull)
                {
                    double hoursElapsed = Convert.ToDouble(hoursElapsedRaw);
                    // Đồng bộ SLA thành 72h (hoặc tạm comment dòng if này nếu đang trong giai đoạn test)
                    if (hoursElapsed > 72)
                    {
                        return false;
                    }
                }
            }

            // 2. LẤY DANH SÁCH ẢNH HIỆN TẠI TRONG DATABASE
            string currentImagesRaw = await GetExistingImagePathAsync(model.PhieuId);
            if (string.IsNullOrEmpty(currentImagesRaw))
            {
                return false;
            }

            string[] imgUrls = currentImagesRaw.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> remainingUrls = new List<string>();
            bool foundTarget = false;
            bool deletionFailed = false;

            // Lấy tên file client muốn xóa (bỏ qua query param hoặc đường dẫn dài nếu có)
            string clientFileName = Path.GetFileName(model.ImageUrl.Trim().Split('?')[0]);

            foreach (string rawUrl in imgUrls)
            {
                string itemUrl = rawUrl.Trim();
                string dbFileName = Path.GetFileName(itemUrl.Split('?')[0]);

                // Nếu KHÔNG TRÙNG tên file thì giữ lại trong Database
                if (!string.Equals(dbFileName, clientFileName, StringComparison.OrdinalIgnoreCase))
                {
                    remainingUrls.Add(itemUrl);
                }
                else
                {
                    foundTarget = true;
                    // NẾU TRÙNG TÊN: TIẾN HÀNH XÓA FILE THẬT TRÊN SERVER 1
                    try
                    {
                        // Chuẩn hóa đường dẫn tương đối để gửi sang Server 1 (StorageServer)
                        // Ví dụ: "/uploads/MatLuoi/anh1.jpg" hoặc "matluoi/anh1.jpg" -> "matluoi/anh1.jpg"
                        string targetPath = itemUrl.TrimStart('/');
                        if (targetPath.StartsWith("uploads/MatLuoi/", StringComparison.OrdinalIgnoreCase))
                        {
                            targetPath = targetPath.Substring("uploads/".Length); // Cắt bỏ chữ "uploads/"
                        }
                        else if (!targetPath.Contains("/"))
                        {
                            targetPath = "matluoi/" + targetPath;
                        }

                        // GỌI SANG SERVER 1 (STORAGE) ĐỂ XÓA TỆP VẬT LÝ
                        if (!await _fileStorageService.DeleteFileAsync(targetPath))
                        {
                            remainingUrls.Add(itemUrl);
                            deletionFailed = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("Lỗi xóa file trên StorageServer: " + ex.Message);
                        remainingUrls.Add(itemUrl);
                        deletionFailed = true;
                    }
                }
            }

            // Không xóa tham chiếu trong DB nếu file vật lý không xóa được hoặc tên ảnh không thuộc phiếu.
            if (!foundTarget || deletionFailed)
            {
                return false;
            }

            // 3. CẬP NHẬT LẠI DANH SÁCH ẢNH CÒN LẠI VÀO ORACLE DATABASE
            string updatedImagesRaw = string.Join(";", remainingUrls);
            using (var conn = new OracleConnection(_connString))
            {
                string updateQuery = "UPDATE brcd_dhgh_xuly SET anh_cskh = :anh_cskh WHERE phieu_id = :phieu_id";
                var affectedRows = await conn.ExecuteAsync(updateQuery, new
                {
                    anh_cskh = !string.IsNullOrEmpty(updatedImagesRaw) ? (object)updatedImagesRaw : DBNull.Value,
                    phieu_id = model.PhieuId
                });
                return affectedRows > 0;
            }
        }

        public async Task<byte[]?> ExportExcelAsync(string? maDv, string? maNvkt, string nguyCo, string? search)
        {
            var (filterClause, parameters) = BuildDataScopeFilter(maDv, maNvkt, nguyCo, search);

            using (var conn = new OracleConnection(_connString))
            {
                string query = $@"
                    SELECT t.ma_tb as MaTb, t.ten_tb as TenTb, t.so_dt as SoDt, t.diachi_tb as DiaChiTb, 
                           t.ma_dv as MaDv, t.ten_nvkt as TenNvkt, t.ngay_giao as NgayGiao, t.trangthai_phieu as TrangThaiPhieu,
                           xl.diem_goc as DiemGoc, xl.diem_sau_xl as DiemSauXl, xl.ngay_tao as NgayTaoLs,
                           CASE WHEN xl.phieu_id IS NOT NULL THEN 1 ELSE 0 END as DaTacNghiep,
                           t.diem_tin_nhiem as DiemTinNhiem
                    FROM brcd_dhgh_kehoach t
                    LEFT JOIN brcd_dhgh_xuly xl ON t.phieu_id = xl.phieu_id
                    {filterClause}
                    ORDER BY t.ngay_giao DESC";

                try
                {
                    var list = (await conn.QueryAsync<dynamic>(query, parameters)).ToList();

                    using (var workbook = new XLWorkbook())
                    {
                        var worksheet = workbook.Worksheets.Add("DanhSachTB");

                        worksheet.Cell(1, 1).Value = "Mã Thuê Bao";
                        worksheet.Cell(1, 2).Value = "Tên Khách Hàng";
                        worksheet.Cell(1, 3).Value = "Số Điện Thoại";
                        worksheet.Cell(1, 4).Value = "Địa Chỉ Lắp Đặt";
                        worksheet.Cell(1, 5).Value = "Mã ĐV";
                        worksheet.Cell(1, 6).Value = "Nhân Viên Kỹ Thuật";
                        worksheet.Cell(1, 7).Value = "Ngày Giao Việc";
                        worksheet.Cell(1, 8).Value = "Trạng Thái Phiếu";
                        worksheet.Cell(1, 9).Value = "Kết Quả Thực Địa";
                        worksheet.Cell(1, 10).Value = "Ngày xử lý thực tế";
                        worksheet.Cell(1, 11).Value = "Điểm trước XL";
                        worksheet.Cell(1, 12).Value = "Điểm dự kiến sau XL";
                        worksheet.Cell(1, 13).Value = "Điểm Hiện Tại";

                        var headerRange = worksheet.Range("A1:M1");
                        headerRange.Style.Font.Bold = true;
                        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#0f3358");
                        headerRange.Style.Font.FontColor = XLColor.White;

                        int row = 2;
                        foreach (var item in list)
                        {
                            int daTacNghiep = item.DATACNGHIEP != null ? Convert.ToInt32(item.DATACNGHIEP) : 0;
                            int goc = item.DIEMGOC != null ? Convert.ToInt32(item.DIEMGOC) : 0;
                            int sau = item.DIEMSAUXL != null ? Convert.ToInt32(item.DIEMSAUXL) : 0;

                            string trangThaiText = "Chưa tác nghiệp";
                            if (daTacNghiep == 1)
                            {
                                trangThaiText = (goc == sau) ? "Chưa xử lý được" : "Đã tác nghiệp";
                            }

                            int trangThaiPhieuVal = item.TRANGTHAIPHIEU != null ? Convert.ToInt32(item.TRANGTHAIPHIEU) : 0;
                            string trangThaiPhieuText = "Mới lập";
                            if (trangThaiPhieuVal == 1) trangThaiPhieuText = "Đã giao việc";
                            else if (trangThaiPhieuVal == 2) trangThaiPhieuText = "Đã xử lý TC";
                            else if (trangThaiPhieuVal == 3) trangThaiPhieuText = "Chưa xử lý được";

                            worksheet.Cell(row, 1).Value = item.MATB?.ToString();
                            worksheet.Cell(row, 2).Value = item.TENTB?.ToString();
                            worksheet.Cell(row, 3).Value = item.SODT?.ToString();
                            worksheet.Cell(row, 4).Value = item.DIACHITB?.ToString();
                            worksheet.Cell(row, 5).Value = item.MADV?.ToString();
                            worksheet.Cell(row, 6).Value = item.TENNVKT?.ToString();
                            worksheet.Cell(row, 7).Value = item.NGAYGIAO != null ? Convert.ToDateTime(item.NGAYGIAO).ToString("dd/MM/yyyy HH:mm") : "-";
                            worksheet.Cell(row, 8).Value = trangThaiPhieuText;
                            worksheet.Cell(row, 9).Value = trangThaiText;
                            worksheet.Cell(row, 10).Value = item.NGAYTAOLS != null ? Convert.ToDateTime(item.NGAYTAOLS).ToString("dd/MM/yyyy HH:mm") : "-";
                            worksheet.Cell(row, 11).Value = daTacNghiep == 1 ? goc.ToString() : "-";
                            worksheet.Cell(row, 12).Value = daTacNghiep == 1 ? sau.ToString() : "-";
                            worksheet.Cell(row, 13).Value = item.DIEMTINNHIEM?.ToString();

                            row++;
                        }

                        worksheet.Columns().AdjustToContents();

                        using (var stream = new MemoryStream())
                        {
                            workbook.SaveAs(stream);
                            return stream.ToArray();
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Lỗi xuất Excel: " + ex.Message);
                    return null;
                }
            }
        }

        private async Task<string> GetExistingImagePathAsync(long phieuId)
        {
            using var conn = new OracleConnection(_connString);
            // Oracle có thể trả CLOB cho cột ANH_CSKH. Dùng dynamic rồi ToString()
            // tránh InvalidCastException từ ExecuteScalarAsync<string> khi xóa ảnh.
            const string query = "SELECT anh_cskh FROM brcd_dhgh_xuly WHERE phieu_id = :phieu_id";
            var result = await conn.QueryFirstOrDefaultAsync<dynamic>(query, new { phieu_id = phieuId });
            return result?.ANH_CSKH?.ToString()?.Trim() ?? "";
        }

        public async Task<(Stream Stream, string ContentType)?> GetImageAsync(long phieuId, string fileNameOrRelativePath)
        {
            if (phieuId <= 0 || string.IsNullOrWhiteSpace(fileNameOrRelativePath)) return null;

            // Chuẩn hóa input về đúng path trên Server 1: "MatLuoi/ten_file.jpg"
            // Chấp nhận các dạng: "/uploads/MatLuoi/x.jpg", "uploads/MatLuoi/x.jpg", "MatLuoi/x.jpg", hoặc chỉ "x.jpg"
            string targetPath = fileNameOrRelativePath.Trim().TrimStart('/');

            if (targetPath.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
            {
                targetPath = targetPath.Substring("uploads/".Length); // -> "MatLuoi/x.jpg"
            }
            else if (!targetPath.Contains("/"))
            {
                targetPath = "MatLuoi/" + targetPath; // chỉ có tên file -> thêm thư mục con
            }

            var fileName = Path.GetFileName(targetPath);
            if (!targetPath.StartsWith("MatLuoi/", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(fileName)
                || !string.Equals(targetPath, $"MatLuoi/{fileName}", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            using var conn = new OracleConnection(_connString);
            const string accessQuery = @"
                SELECT t.ma_dv AS MaDv, t.ma_nvkt AS MaNvkt, xl.anh_cskh AS AnhCskh
                FROM brcd_dhgh_xuly xl
                INNER JOIN brcd_dhgh_kehoach t ON t.phieu_id = xl.phieu_id
                WHERE xl.phieu_id = :phieu_id";
            var record = await conn.QueryFirstOrDefaultAsync<RecordAccessRow>(accessQuery, new
            {
                phieu_id = phieuId
            });
            var storedImagePaths = record?.AnhCskh?.ToString();
            var exactImageBelongsToRecord = storedImagePaths
                ?.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(imagePath => Path.GetFileName(imagePath.Trim().Split('?')[0]))
                .Any(dbFileName => string.Equals(dbFileName, fileName, StringComparison.OrdinalIgnoreCase)) == true;

            if (record == null
                || !exactImageBelongsToRecord
                || !await HasRecordAccessAsync(conn, record.MaDv, record.MaNvkt))
            {
                return null;
            }

            return await _fileStorageService.GetFileStreamAsync(targetPath);
        }
    }
}
