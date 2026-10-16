using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
    /// Điểm danh hiện trường: nhân viên chụp ảnh tại chỗ kèm toạ độ; quản lý xem bản đồ,
    /// danh sách ai đã điểm danh và lịch sử. Phạm vi dữ liệu luôn lấy từ JWT (data_scope),
    /// không tin tham số lọc từ client.
    /// </summary>
    public class DiemDanhService : IDiemDanhService
    {
        // Mã cờ bất thường lưu trong cột canh_bao.
        public const string FLAG_TRUNG_ANH = "TRUNG_ANH";   // Ảnh đã từng được gửi (trùng mã băm)
        public const string FLAG_GPS_KEM = "GPS_KEM";       // Sai số GPS lớn hơn ngưỡng cấu hình
        public const string FLAG_LECH_GIO = "LECH_GIO";     // Giờ điện thoại lệch nhiều so với giờ máy chủ

        private static readonly IReadOnlyList<LoaiCongViecDto> LoaiCongViecList = new List<LoaiCongViecDto>
        {
            new() { Ma = "SUA_SU_CO", Ten = "Sửa sự cố" },
            new() { Ma = "LAP_DAT", Ten = "Lắp đặt mới" },
            new() { Ma = "BAO_DUONG_TRAM", Ten = "Bảo dưỡng trạm" },
            new() { Ma = "KEO_CAP", Ten = "Kéo / thu hồi cáp" },
            new() { Ma = "CSKH", Ten = "Chăm sóc khách hàng" },
            new() { Ma = "KHAO_SAT", Ten = "Khảo sát" },
            new() { Ma = "KHAC", Ten = "Việc khác" }
        };

        private static readonly Regex MaDvPattern = new("^[A-Za-z0-9_.\\-]{1,30}$", RegexOptions.Compiled);
        // Khoá ảnh trong bucket MinIO: "yyyy-MM/yyyyMMdd_HHmmss_{id}.jpg" (thư mục con theo tháng).
        // Ảnh đầu: 2026-10/20261015_081203_123.jpg; ảnh thứ 2 trở đi: 2026-10/20261015_081203_123_2.jpg
        private static readonly Regex ImageKeyPattern = new(@"^\d{4}-\d{2}/\d{8}_\d{6}_\d+(_\d{1,2})?\.jpg$", RegexOptions.Compiled);
        private static readonly StringComparer VietnameseComparer = CreateVietnameseComparer();
        private const int MaxGhiChuLength = 500;
        private const int MaxRangeDays = 92;
        private const int MaxMapPoints = 5000;
        private const int MaxExportRows = 50000;

        private readonly string _connString;
        private readonly ICurrentUserService _currentUser;
        private readonly IDiemDanhImageStore _imageStore;
        private readonly IReverseGeocodingService _geocoding;
        private readonly ILogger<DiemDanhService> _logger;

        private readonly long _maxImageBytes;
        private readonly ulong _maxDecodedPixels;
        private readonly int _maxDimension;
        private readonly int _jpegQuality;
        private readonly int _minSecondsBetweenCheckIns;
        private readonly double _lowAccuracyMeters;
        private readonly int _maxClockSkewMinutes;
        private readonly int _maxImages;
        private readonly TimeSpan _serverUtcOffset;
        private readonly string? _watermarkFont;

        public DiemDanhService(
            IConfiguration config,
            ICurrentUserService currentUser,
            IDiemDanhImageStore imageStore,
            IReverseGeocodingService geocoding,
            ILogger<DiemDanhService> logger)
        {
            _connString = config.GetConnectionString("ConnectionString_NBH") ?? string.Empty;
            _currentUser = currentUser;
            _imageStore = imageStore;
            _geocoding = geocoding;
            _logger = logger;

            int Int(string key, int fallback, int min, int max)
                => int.TryParse(config[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, min, max) : fallback;

            _maxImageBytes = Int("Upload:MaxFileSizeInMB", 10, 1, 20) * 1024L * 1024L;
            _maxDecodedPixels = (ulong)Int("ImageOptimization:MaxDecodedMegapixels", 40, 5, 80) * 1_000_000UL;
            _maxDimension = Int("DiemDanh:MaxDimension", 1600, 800, 3840);
            _jpegQuality = Int("DiemDanh:JpegQuality", 82, 60, 95);
            _minSecondsBetweenCheckIns = Int("DiemDanh:MinSecondsBetweenCheckIns", 60, 0, 3600);
            _lowAccuracyMeters = Int("DiemDanh:LowAccuracyMeters", 200, 10, 10000);
            _maxClockSkewMinutes = Int("DiemDanh:MaxClockSkewMinutes", 15, 1, 1440);
            _maxImages = Int("DiemDanh:MaxImagesPerCheckIn", 5, 1, 10);
            _serverUtcOffset = TimeSpan.FromHours(Int("DiemDanh:ServerUtcOffsetHours", 7, -12, 14));
            _watermarkFont = ResolveWatermarkFont(config["DiemDanh:WatermarkFont"]);
        }

        // ============================================================
        // DANH MỤC
        // ============================================================
        public IReadOnlyList<LoaiCongViecDto> GetLoaiCongViec() => LoaiCongViecList;

        private static string? TenLoaiCongViec(string? ma)
            => ma == null ? null : LoaiCongViecList.FirstOrDefault(x => x.Ma == ma)?.Ten ?? ma;

        public async Task<DiaChiDto> GetDiaChiAsync(double latitude, double longitude, CancellationToken ct = default)
        {
            if (!IsValidCoordinate(latitude, longitude))
                throw new DiemDanhValidationException("Toạ độ không hợp lệ.");

            return new DiaChiDto
            {
                Latitude = latitude,
                Longitude = longitude,
                DiaChi = await _geocoding.GetAddressAsync(latitude, longitude, ct)
            };
        }

        // ============================================================
        // NHÂN VIÊN ĐIỂM DANH
        // ============================================================
        public async Task<DiemDanhCheckInResultDto> CheckInAsync(
            DiemDanhCheckInFormDto model, string? userAgent, string? clientIp, CancellationToken ct = default)
        {
            var username = _currentUser.Username?.Trim();
            if (string.IsNullOrEmpty(username))
                throw new DiemDanhValidationException("Không xác định được tài khoản đăng nhập.");

            // ---- 1. Kiểm tra dữ liệu đầu vào ----
            if (!TryParseDouble(model.Latitude, out var lat) || !TryParseDouble(model.Longitude, out var lng) || !IsValidCoordinate(lat, lng))
                throw new DiemDanhValidationException("Chưa lấy được vị trí GPS. Vui lòng bật định vị và thử lại.");

            double? accuracy = TryParseDouble(model.Accuracy, out var acc) && acc >= 0 && acc < 1_000_000 ? Math.Round(acc, 2) : null;

            var loaiCongViec = string.IsNullOrWhiteSpace(model.LoaiCongViec) ? null : model.LoaiCongViec.Trim().ToUpperInvariant();
            if (loaiCongViec != null && LoaiCongViecList.All(x => x.Ma != loaiCongViec))
                throw new DiemDanhValidationException("Loại công việc không hợp lệ.");

            var ghiChu = CleanText(model.GhiChu, MaxGhiChuLength);
            if (loaiCongViec == null && ghiChu == null)
                throw new DiemDanhValidationException("Vui lòng chọn loại công việc hoặc ghi chú việc đang làm.");

            var files = (model.Anh ?? new List<IFormFile>()).Where(f => f is { Length: > 0 }).ToList();
            if (files.Count == 0)
                throw new DiemDanhValidationException("Vui lòng chụp hoặc chọn ít nhất một ảnh tại hiện trường.");
            if (files.Count > _maxImages)
                throw new DiemDanhValidationException($"Mỗi lượt điểm danh gửi tối đa {_maxImages} ảnh.");

            // Đọc + kiểm tra từng ảnh; bỏ ảnh trùng nhau trong cùng một lần gửi (chọn nhầm hai lần).
            var images = new List<(byte[] Bytes, string Sha)>();
            foreach (var file in files)
            {
                var bytes = await ReadAndValidateImageAsync(file, ct);
                var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                if (images.All(x => x.Sha != sha)) images.Add((bytes, sha));
            }

            await using var conn = new OracleConnection(_connString);
            await conn.OpenAsync(ct);

            // ---- 2. Giờ máy chủ + chống bấm liên tục ----
            var serverTime = await conn.ExecuteScalarAsync<DateTime>("SELECT SYSDATE FROM dual");

            if (_minSecondsBetweenCheckIns > 0)
            {
                var lastTime = await conn.ExecuteScalarAsync<DateTime?>(
                    "SELECT MAX(created_at) FROM dtntb_diemdanh WHERE ma_nd = :ma_nd AND created_at >= :since",
                    new { ma_nd = username, since = serverTime.AddSeconds(-_minSecondsBetweenCheckIns) });
                if (lastTime.HasValue)
                {
                    var waitSeconds = _minSecondsBetweenCheckIns - (int)(serverTime - lastTime.Value).TotalSeconds;
                    throw new DiemDanhValidationException(
                        $"Bạn vừa điểm danh lúc {lastTime.Value:HH:mm:ss}. Vui lòng đợi {Math.Max(waitSeconds, 1)} giây nữa.");
                }
            }

            // ---- 3. Thông tin nhân sự tại thời điểm điểm danh ----
            var profile = await conn.QueryFirstOrDefaultAsync<UserSnapshot>(@"
                SELECT a.ma_nv AS MaNv, a.ten_nv AS TenNv, a.ma_dv AS MaDv, t.ten_dv AS TenTo, b.ten_dv AS TenDv
                FROM v_nguoidung_diaban a
                LEFT JOIN v_donvi t ON TRIM(a.ma_dv) = TRIM(t.ma_dv)
                LEFT JOIN v_donvi_diaban b ON SUBSTR(TRIM(a.ma_dv), 1, 7) = SUBSTR(TRIM(b.ma_dv), 1, 7)
                WHERE UPPER(TRIM(a.ma_nd)) = UPPER(TRIM(:username)) AND ROWNUM = 1",
                new { username });

            var maNv = Trim(profile?.MaNv) ?? Trim(_currentUser.MaNv);
            var tenNv = Trim(profile?.TenNv) ?? Trim(_currentUser.TenNv) ?? username;
            var maDv = Trim(profile?.MaDv) ?? Trim(_currentUser.MaDv);
            var tenTo = Trim(profile?.TenTo);
            var tenDv = Trim(profile?.TenDv);

            // ---- 4. Cờ bất thường (không chặn, chỉ đánh dấu để quản lý xem lại) ----
            var flags = new List<string>();
            var shas = images.Select(x => x.Sha).ToList();
            var duplicated = await conn.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1) FROM (
                    SELECT 1 FROM dtntb_diemdanh WHERE image_sha256 IN :shas
                    UNION ALL
                    SELECT 1 FROM dtntb_diemdanh_anh WHERE image_sha256 IN :shas
                ) WHERE ROWNUM = 1", new { shas });
            if (duplicated > 0) flags.Add(FLAG_TRUNG_ANH);
            if (accuracy.HasValue && accuracy.Value > _lowAccuracyMeters) flags.Add(FLAG_GPS_KEM);

            DateTime? clientTime = DateTimeOffset.TryParse(model.ClientTime, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var clientOffset)
                ? clientOffset.ToOffset(_serverUtcOffset).DateTime
                : null;
            if (clientTime.HasValue && Math.Abs((clientTime.Value - serverTime).TotalMinutes) > _maxClockSkewMinutes)
                flags.Add(FLAG_LECH_GIO);

            // ---- 5. Địa chỉ (lỗi dịch vụ bản đồ không chặn điểm danh) ----
            string? diaChi;
            using (var geoCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                geoCts.CancelAfter(TimeSpan.FromSeconds(6));
                diaChi = await _geocoding.GetAddressAsync(lat, lng, geoCts.Token);
            }

            // ---- 6. In thông tin lên từng ảnh và lưu vào MinIO ----
            var id = await conn.ExecuteScalarAsync<long>("SELECT seq_dtntb_diemdanh.NEXTVAL FROM dual");
            var timeText = serverTime.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
            var savedKeys = new List<string>();

            try
            {
                for (var i = 0; i < images.Count; i++)
                {
                    var watermarked = BuildWatermarkedImage(images[i].Bytes, new[]
                    {
                        string.IsNullOrEmpty(maNv) ? tenNv : $"{tenNv} ({maNv})",
                        images.Count > 1 ? $"{timeText}  ·  Ảnh {i + 1}/{images.Count}" : timeText,
                        FormatCoordinate(lat, lng, accuracy),
                        diaChi ?? string.Empty
                    });

                    // Chia thư mục con theo tháng: 2026-10/20261015_081203_123.jpg, ảnh sau thêm _2, _3...
                    var key = $"{serverTime:yyyy-MM}/{serverTime:yyyyMMdd_HHmmss}_{id}" + (i == 0 ? "" : $"_{i + 1}") + ".jpg";
                    await _imageStore.SaveAsync(key, watermarked, "image/jpeg", ct);
                    savedKeys.Add(key);
                }
            }
            catch
            {
                await DeleteImagesQuietlyAsync(savedKeys);
                throw;
            }

            // ---- 7. Ghi CSDL trong một giao dịch; nếu lỗi thì dọn các ảnh vừa lưu ----
            try
            {
                await using var tx = await conn.BeginTransactionAsync(ct);
                var p = new DynamicParameters();
                p.Add("id", id);
                p.Add("created_at", serverTime, DbType.DateTime);
                p.Add("client_time", clientTime, DbType.DateTime);
                p.Add("ma_nd", username);
                p.Add("ma_nv", maNv);
                p.Add("ten_nv", Truncate(tenNv, 200));
                p.Add("ma_dv", Truncate(maDv, 30));
                p.Add("ten_to", Truncate(tenTo, 300));
                p.Add("ten_dv", Truncate(tenDv, 300));
                p.Add("lat", Math.Round(lat, 7));
                p.Add("lng", Math.Round(lng, 7));
                p.Add("acc", accuracy);
                p.Add("dia_chi", diaChi);
                p.Add("loai", loaiCongViec);
                p.Add("ghi_chu", ghiChu);
                p.Add("image_path", savedKeys[0]);
                p.Add("sha", images[0].Sha);
                p.Add("so_anh", images.Count);
                p.Add("canh_bao", flags.Count == 0 ? null : string.Join(",", flags));
                p.Add("ua", Truncate(CleanText(userAgent, 300), 300));
                p.Add("ip", Truncate(clientIp, 64));

                await conn.ExecuteAsync(@"
                    INSERT INTO dtntb_diemdanh
                        (diemdanh_id, ngay, created_at, client_time, ma_nd, ma_nv, ten_nv, ma_dv, ten_to, ten_dv,
                         latitude, longitude, accuracy_m, dia_chi, loai_cong_viec, ghi_chu,
                         image_path, image_sha256, so_anh, canh_bao, user_agent, client_ip)
                    VALUES
                        (:id, TRUNC(:created_at), :created_at, :client_time, :ma_nd, :ma_nv, :ten_nv, :ma_dv, :ten_to, :ten_dv,
                         :lat, :lng, :acc, :dia_chi, :loai, :ghi_chu,
                         :image_path, :sha, :so_anh, :canh_bao, :ua, :ip)", p, tx);

                for (var i = 1; i < savedKeys.Count; i++)
                {
                    await conn.ExecuteAsync(@"
                        INSERT INTO dtntb_diemdanh_anh (diemdanh_id, thu_tu, image_path, image_sha256)
                        VALUES (:id, :thu_tu, :image_path, :sha)",
                        new { id, thu_tu = i, image_path = savedKeys[i], sha = images[i].Sha }, tx);
                }

                await tx.CommitAsync(CancellationToken.None);
            }
            catch
            {
                await DeleteImagesQuietlyAsync(savedKeys);
                throw;
            }

            return new DiemDanhCheckInResultDto
            {
                DiemDanhId = id,
                ThoiGian = serverTime,
                DiaChi = diaChi,
                CanhBao = flags,
                AnhUrl = AnhUrl(id),
                SoAnh = images.Count
            };
        }

        public async Task<List<DiemDanhItemDto>> GetCuaToiAsync(DateTime ngay)
        {
            var username = _currentUser.Username?.Trim();
            if (string.IsNullOrEmpty(username)) return new List<DiemDanhItemDto>();

            var p = new DynamicParameters();
            p.Add("ma_nd", username);
            p.Add("ngay", ngay.Date, DbType.Date);

            await using var conn = new OracleConnection(_connString);
            var rows = await conn.QueryAsync<DiemDanhRow>(
                $"SELECT {ItemColumns} FROM dtntb_diemdanh d WHERE d.ma_nd = :ma_nd AND d.ngay = :ngay ORDER BY d.created_at DESC", p);
            return rows.Select(ToItem).ToList();
        }

        // ============================================================
        // QUẢN LÝ THEO DÕI
        // ============================================================
        public async Task<List<DonViOptionDto>> GetDonViAsync()
        {
            const string select = @"SELECT DISTINCT SUBSTR(TRIM(db.ma_dv), 1, 7) AS MaDv, db.ten_dv AS TenDv FROM v_donvi_diaban db";
            string sql;
            object? param = null;

            switch (_currentUser.ScopeLevel)
            {
                case UserDataScopeLevel.ToanTinh:
                    sql = select;
                    break;
                case UserDataScopeLevel.DiaBan:
                    if (string.IsNullOrWhiteSpace(_currentUser.DiaBanId)) return new List<DonViOptionDto>();
                    sql = $"{select} WHERE TRIM(TO_CHAR(db.diaban_id)) = :diaban_id";
                    param = new { diaban_id = _currentUser.DiaBanId };
                    break;
                case UserDataScopeLevel.DonVi:
                case UserDataScopeLevel.ToQuanLy:
                    // Đơn vị/tổ chỉ có một lựa chọn là chính đơn vị của mình.
                    sql = $"{select} WHERE SUBSTR(TRIM(db.ma_dv), 1, 7) = :ma_dv";
                    param = new { ma_dv = _currentUser.MaDv7 };
                    break;
                default:
                    return new List<DonViOptionDto>();
            }

            await using var conn = new OracleConnection(_connString);
            var list = (await conn.QueryAsync<DonViOptionDto>($"{sql} ORDER BY 2", param))
                .Where(x => !string.IsNullOrWhiteSpace(x.MaDv))
                .GroupBy(x => x.MaDv)
                .Select(g => g.First())
                .ToList();
            return list;
        }

        public async Task<List<ToOptionDto>> GetToAsync(string? maDv)
        {
            // Tổ = toàn bộ ma_dv của nhân viên; chỉ lấy các tổ có nhân sự, trong phạm vi người xem.
            var p = new DynamicParameters();
            var where = new List<string> { "a.ma_dv IS NOT NULL" };

            switch (_currentUser.ScopeLevel)
            {
                case UserDataScopeLevel.ToanTinh:
                    break;
                case UserDataScopeLevel.DiaBan:
                    if (string.IsNullOrWhiteSpace(_currentUser.DiaBanId)) return new List<ToOptionDto>();
                    where.Add(@"SUBSTR(TRIM(a.ma_dv), 1, 7) IN (
                                  SELECT SUBSTR(TRIM(db.ma_dv), 1, 7) FROM v_donvi_diaban db
                                  WHERE TRIM(TO_CHAR(db.diaban_id)) = :scope_diaban)");
                    p.Add("scope_diaban", _currentUser.DiaBanId);
                    break;
                case UserDataScopeLevel.DonVi:
                    if (string.IsNullOrWhiteSpace(_currentUser.MaDv7)) return new List<ToOptionDto>();
                    where.Add("SUBSTR(TRIM(a.ma_dv), 1, 7) = :scope_dv");
                    p.Add("scope_dv", _currentUser.MaDv7);
                    break;
                case UserDataScopeLevel.ToQuanLy:
                    if (string.IsNullOrWhiteSpace(_currentUser.MaDv)) return new List<ToOptionDto>();
                    where.Add("TRIM(a.ma_dv) = TRIM(:scope_to)");
                    p.Add("scope_to", _currentUser.MaDv);
                    break;
                default:
                    return new List<ToOptionDto>();
            }

            if (!string.IsNullOrWhiteSpace(maDv) && !maDv.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                if (!MaDvPattern.IsMatch(maDv.Trim())) throw new DiemDanhValidationException("Mã đơn vị không hợp lệ.");
                where.Add("SUBSTR(TRIM(a.ma_dv), 1, 7) = :f_dv");
                p.Add("f_dv", ToMaDonVi(maDv));
            }

            await using var conn = new OracleConnection(_connString);
            var rows = await conn.QueryAsync<ToOptionDto>($@"
                SELECT TRIM(a.ma_dv) AS MaTo, MAX(t.ten_dv) AS TenTo
                FROM v_nguoidung_diaban a
                LEFT JOIN v_donvi t ON TRIM(a.ma_dv) = TRIM(t.ma_dv)
                WHERE {string.Join(" AND ", where)}
                GROUP BY TRIM(a.ma_dv)", p);

            return rows
                .Where(x => !string.IsNullOrWhiteSpace(x.MaTo))
                .Select(x => new ToOptionDto { MaTo = x.MaTo, TenTo = string.IsNullOrWhiteSpace(x.TenTo) ? x.MaTo : x.TenTo.Trim() })
                .OrderBy(x => x.TenTo, VietnameseComparer)
                .ToList();
        }

        public async Task<List<DiemDanhItemDto>> GetBanDoAsync(DateTime ngay, string? maDv, string? maTo)
        {
            var (where, p) = BuildFilter(ngay.Date, ngay.Date, maDv, maTo, null, null, false);
            await using var conn = new OracleConnection(_connString);
            var rows = await conn.QueryAsync<DiemDanhRow>($@"
                SELECT * FROM (
                    SELECT {ItemColumns} FROM dtntb_diemdanh d WHERE {where} ORDER BY d.created_at
                ) WHERE ROWNUM <= {MaxMapPoints}", p);
            return rows.Select(ToItem).ToList();
        }

        public async Task<List<DiemDanhTongHopDonViDto>> GetTongHopAsync(DateTime ngay, string? maDv, string? maTo)
        {
            var (where, p) = BuildFilter(ngay.Date, ngay.Date, maDv, maTo, null, null, false);
            await using var conn = new OracleConnection(_connString);

            // Mỗi người 1 dòng: số lần, lần đầu, lần cuối và địa chỉ của lần cuối.
            var rows = (await conn.QueryAsync<DiemDanhTongHopDto>($@"
                SELECT ma_nd AS MaNd, ma_nv AS MaNv, ten_nv AS TenNv, ma_dv AS MaDv, ten_to AS TenTo, ten_dv AS TenDv,
                       so_lan AS SoLan, lan_dau AS LanDau, created_at AS LanCuoi,
                       diemdanh_id AS DiemDanhIdCuoi, dia_chi AS DiaChiCuoi, so_cb AS SoCanhBao,
                       so_duyet AS SoDaDuyet, so_cho AS SoChoDuyet, so_tu_choi AS SoTuChoi
                FROM (
                    SELECT d.ma_nd, d.ma_nv, d.ten_nv, d.ma_dv, d.ten_to, d.ten_dv, d.created_at, d.diemdanh_id, d.dia_chi,
                           ROW_NUMBER() OVER (PARTITION BY d.ma_nd ORDER BY d.created_at DESC, d.diemdanh_id DESC) AS rn,
                           COUNT(1) OVER (PARTITION BY d.ma_nd) AS so_lan,
                           MIN(d.created_at) OVER (PARTITION BY d.ma_nd) AS lan_dau,
                           SUM(CASE WHEN d.canh_bao IS NULL THEN 0 ELSE 1 END) OVER (PARTITION BY d.ma_nd) AS so_cb,
                           SUM(CASE WHEN d.trang_thai = 'DA_DUYET' THEN 1 ELSE 0 END) OVER (PARTITION BY d.ma_nd) AS so_duyet,
                           SUM(CASE WHEN d.trang_thai = 'CHO_DUYET' THEN 1 ELSE 0 END) OVER (PARTITION BY d.ma_nd) AS so_cho,
                           SUM(CASE WHEN d.trang_thai = 'TU_CHOI' THEN 1 ELSE 0 END) OVER (PARTITION BY d.ma_nd) AS so_tu_choi
                    FROM dtntb_diemdanh d
                    WHERE {where}
                )
                WHERE rn = 1", p)).ToList();

            // Đơn vị = 7 ký tự đầu ma_dv; tổ = toàn bộ ma_dv.
            return rows
                .GroupBy(r => ToMaDonVi(r.MaDv))
                .Select(dv =>
                {
                    var toList = dv
                        .GroupBy(r => (r.MaDv ?? string.Empty).Trim())
                        .Select(to => new DiemDanhTongHopNhomDto
                        {
                            MaDv = to.Key,
                            TenTo = to.Select(x => x.TenTo).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? to.Key,
                            SoNguoi = to.Count(),
                            SoLuot = to.Sum(x => x.SoLan),
                            SoChoDuyet = to.Sum(x => x.SoChoDuyet),
                            NhanVien = to.OrderBy(x => x.TenNv, VietnameseComparer).ToList()
                        })
                        .OrderBy(to => to.TenTo, VietnameseComparer)
                        .ToList();

                    return new DiemDanhTongHopDonViDto
                    {
                        MaDonVi = dv.Key,
                        TenDv = dv.Select(x => x.TenDv).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim(),
                        SoTo = toList.Count,
                        SoNguoi = toList.Sum(t => t.SoNguoi),
                        SoLuot = toList.Sum(t => t.SoLuot),
                        SoChoDuyet = toList.Sum(t => t.SoChoDuyet),
                        To = toList
                    };
                })
                .OrderBy(dv => dv.TenDv, VietnameseComparer)
                .ThenBy(dv => dv.MaDonVi)
                .ToList();
        }

        public async Task<PaginatedResultDto<DiemDanhItemDto>> GetLichSuAsync(DiemDanhLichSuQueryDto query)
        {
            var (tuNgay, denNgay) = NormalizeRange(query.TuNgay, query.DenNgay);
            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 1, 100);
            var (where, p) = BuildFilter(tuNgay, denNgay, query.MaDv, query.MaTo, query.MaNd, query.Search, query.ChiCanhBao, query.TrangThai);

            await using var conn = new OracleConnection(_connString);
            var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM dtntb_diemdanh d WHERE {where}", p);

            p.Add("start_row", (page - 1) * pageSize + 1);
            p.Add("end_row", page * pageSize);
            var rows = await conn.QueryAsync<DiemDanhRow>($@"
                SELECT * FROM (
                    SELECT a.*, ROWNUM rnum FROM (
                        SELECT {ItemColumns} FROM dtntb_diemdanh d
                        WHERE {where}
                        ORDER BY d.created_at DESC, d.diemdanh_id DESC
                    ) a WHERE ROWNUM <= :end_row
                ) WHERE rnum >= :start_row", p);

            return new PaginatedResultDto<DiemDanhItemDto>
            {
                Items = rows.Select(ToItem).ToList(),
                TotalCount = total
            };
        }

        public async Task<byte[]> ExportLichSuAsync(DiemDanhLichSuQueryDto query)
        {
            var (tuNgay, denNgay) = NormalizeRange(query.TuNgay, query.DenNgay);
            var (where, p) = BuildFilter(tuNgay, denNgay, query.MaDv, query.MaTo, query.MaNd, query.Search, query.ChiCanhBao, query.TrangThai);

            await using var conn = new OracleConnection(_connString);
            var rows = (await conn.QueryAsync<DiemDanhRow>($@"
                SELECT * FROM (
                    SELECT {ItemColumns} FROM dtntb_diemdanh d
                    WHERE {where}
                    ORDER BY d.created_at DESC, d.diemdanh_id DESC
                ) WHERE ROWNUM <= {MaxExportRows}", p)).Select(ToItem).ToList();

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("DiemDanh");

            ws.Cell(1, 1).Value = $"LỊCH SỬ ĐIỂM DANH HIỆN TRƯỜNG TỪ {tuNgay:dd/MM/yyyy} ĐẾN {denNgay:dd/MM/yyyy}";
            ws.Range(1, 1, 1, 20).Merge().Style.Font.SetBold().Font.SetFontSize(13);

            var headers = new[]
            {
                "STT", "Thời gian", "Mã NV", "Tên nhân viên", "Mã đơn vị", "Tên đơn vị", "Mã tổ", "Tên tổ", "Loại công việc",
                "Ghi chú", "Địa chỉ", "Vĩ độ", "Kinh độ", "Sai số GPS (m)", "Cảnh báo", "Xem bản đồ",
                "Trạng thái duyệt", "Người duyệt", "Thời gian duyệt", "Lý do từ chối"
            };
            for (var i = 0; i < headers.Length; i++) ws.Cell(3, i + 1).Value = headers[i];
            var headerRange = ws.Range(3, 1, 3, headers.Length);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Font.FontColor = XLColor.White;
            headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a73e8");

            var row = 4;
            foreach (var item in rows)
            {
                ws.Cell(row, 1).Value = (double)(row - 3);
                ws.Cell(row, 2).Value = item.ThoiGian;
                ws.Cell(row, 2).Style.DateFormat.Format = "dd/MM/yyyy HH:mm:ss";
                ws.Cell(row, 3).Value = ExcelText(item.MaNv);
                ws.Cell(row, 4).Value = ExcelText(item.TenNv);
                ws.Cell(row, 5).Value = ExcelText(item.MaDonVi);
                ws.Cell(row, 6).Value = ExcelText(item.TenDv);
                ws.Cell(row, 7).Value = ExcelText(item.MaDv);
                ws.Cell(row, 8).Value = ExcelText(item.TenTo);
                ws.Cell(row, 9).Value = ExcelText(item.TenLoaiCongViec);
                ws.Cell(row, 10).Value = ExcelText(item.GhiChu);
                ws.Cell(row, 11).Value = ExcelText(item.DiaChi);
                ws.Cell(row, 12).Value = item.Latitude;
                ws.Cell(row, 13).Value = item.Longitude;
                if (item.AccuracyM.HasValue) ws.Cell(row, 14).Value = item.AccuracyM.Value;
                ws.Cell(row, 15).Value = string.Join(", ", item.CanhBao.Select(TenCanhBao));

                var mapUrl = string.Create(CultureInfo.InvariantCulture, $"https://www.google.com/maps?q={item.Latitude},{item.Longitude}");
                ws.Cell(row, 16).Value = "Mở bản đồ";
                ws.Cell(row, 16).SetHyperlink(new XLHyperlink(mapUrl));
                ws.Cell(row, 17).Value = DiemDanhTrangThai.Ten(item.TrangThai);
                if (item.TrangThai == DiemDanhTrangThai.TU_CHOI) ws.Cell(row, 17).Style.Font.FontColor = XLColor.Red;
                else if (item.TrangThai == DiemDanhTrangThai.DA_DUYET) ws.Cell(row, 17).Style.Font.FontColor = XLColor.FromHtml("#16a34a");
                ws.Cell(row, 18).Value = ExcelText(item.TenNguoiDuyet ?? item.NguoiDuyet);
                if (item.ThoiGianDuyet.HasValue)
                {
                    ws.Cell(row, 19).Value = item.ThoiGianDuyet.Value;
                    ws.Cell(row, 19).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
                }
                ws.Cell(row, 20).Value = ExcelText(item.LyDoTuChoi);
                row++;
            }

            var widths = new[] { 6d, 20d, 14d, 24d, 10d, 26d, 14d, 30d, 20d, 30d, 44d, 12d, 12d, 12d, 22d, 14d, 14d, 24d, 18d, 34d };
            for (var c = 1; c <= widths.Length; c++) ws.Column(c).Width = widths[c - 1];
            ws.Columns(10, 11).Style.Alignment.WrapText = true;
            ws.Column(20).Style.Alignment.WrapText = true;
            ws.SheetView.FreezeRows(3);

            using var output = new MemoryStream();
            workbook.SaveAs(output);
            return output.ToArray();
        }

        public async Task<(Stream Stream, string ContentType)?> GetAnhAsync(long diemDanhId, int thuTu = 0, CancellationToken ct = default)
        {
            var username = _currentUser.Username?.Trim();
            if (diemDanhId <= 0 || thuTu < 0 || string.IsNullOrEmpty(username)) return null;

            // Người điểm danh luôn xem được ảnh của mình; người khác cần quyền VIEW và nằm trong phạm vi.
            var p = new DynamicParameters();
            p.Add("id", diemDanhId);
            p.Add("me", username);
            string accessClause = "UPPER(TRIM(d.ma_nd)) = UPPER(:me)";
            if (_currentUser.HasPermission(AppPermissions.DIEMDANH.VIEW))
            {
                var scope = BuildScopeClause(p);
                accessClause = $"({accessClause} OR ({scope}))";
            }

            await using var conn = new OracleConnection(_connString);
            string? path;
            if (thuTu == 0)
            {
                path = await conn.ExecuteScalarAsync<string?>(
                    $"SELECT d.image_path FROM dtntb_diemdanh d WHERE d.diemdanh_id = :id AND {accessClause}", p);
            }
            else
            {
                p.Add("thu_tu", thuTu);
                path = await conn.ExecuteScalarAsync<string?>($@"
                    SELECT a.image_path FROM dtntb_diemdanh_anh a
                    JOIN dtntb_diemdanh d ON d.diemdanh_id = a.diemdanh_id
                    WHERE a.diemdanh_id = :id AND a.thu_tu = :thu_tu AND {accessClause}", p);
            }

            if (string.IsNullOrWhiteSpace(path)) return null;
            var key = path.Trim().TrimStart('/');
            if (!ImageKeyPattern.IsMatch(key)) return null;

            return await _imageStore.GetAsync(key, ct);
        }

        public async Task<bool> DeleteAsync(long diemDanhId, CancellationToken ct = default)
        {
            var username = _currentUser.Username?.Trim();
            if (diemDanhId <= 0 || string.IsNullOrEmpty(username)) return false;

            var p = new DynamicParameters();
            p.Add("id", diemDanhId);
            var scope = BuildScopeClause(p);

            await using var conn = new OracleConnection(_connString);
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            // Khóa dòng trước khi xóa để lấy đúng đường dẫn ảnh và thông tin ghi log.
            var row = await conn.QueryFirstOrDefaultAsync<(string? ImagePath, string? MaNd, DateTime Ngay)>(
                $@"SELECT d.image_path, d.ma_nd, d.ngay FROM dtntb_diemdanh d
                   WHERE d.diemdanh_id = :id AND {scope} FOR UPDATE", p, tx);
            if (row == default)
            {
                await tx.RollbackAsync(ct);
                return false;
            }

            var extraPaths = (await conn.QueryAsync<string?>(
                "SELECT image_path FROM dtntb_diemdanh_anh WHERE diemdanh_id = :id", new { id = diemDanhId }, tx)).ToList();

            await conn.ExecuteAsync("DELETE FROM dtntb_diemdanh_anh WHERE diemdanh_id = :id",
                new { id = diemDanhId }, tx);
            await conn.ExecuteAsync("DELETE FROM dtntb_diemdanh WHERE diemdanh_id = :id",
                new { id = diemDanhId }, tx);
            await tx.CommitAsync(ct);

            _logger.LogWarning("Người dùng {User} đã xóa lượt điểm danh {Id} của {MaNd} ngày {Ngay:yyyy-MM-dd}.",
                username, diemDanhId, row.MaNd?.Trim(), row.Ngay);

            // Bản ghi đã xóa; ảnh xóa sau, lỗi MinIO chỉ ghi log (ảnh mồ côi không ảnh hưởng nghiệp vụ).
            var keys = new[] { row.ImagePath }.Concat(extraPaths)
                .Select(k => k?.Trim().TrimStart('/'))
                .Where(k => !string.IsNullOrEmpty(k) && ImageKeyPattern.IsMatch(k))
                .Select(k => k!)
                .ToList();
            await DeleteImagesQuietlyAsync(keys);
            return true;
        }

        // ============================================================
        // DUYỆT
        // ============================================================
        public async Task<DiemDanhItemDto?> DuyetAsync(long diemDanhId, DiemDanhDuyetDto model)
        {
            var username = _currentUser.Username?.Trim();
            if (diemDanhId <= 0 || string.IsNullOrEmpty(username)) return null;

            var trangThai = (model.TrangThai ?? string.Empty).Trim().ToUpperInvariant();
            if (!DiemDanhTrangThai.All.Contains(trangThai))
                throw new DiemDanhValidationException("Kết quả duyệt không hợp lệ.");

            var lyDo = CleanText(model.LyDo, MaxGhiChuLength);
            if (trangThai == DiemDanhTrangThai.TU_CHOI && lyDo == null)
                throw new DiemDanhValidationException("Vui lòng nhập lý do từ chối.");
            if (trangThai != DiemDanhTrangThai.TU_CHOI) lyDo = null;

            var p = new DynamicParameters();
            p.Add("id", diemDanhId);
            var scope = BuildScopeClause(p);

            await using var conn = new OracleConnection(_connString);
            await conn.OpenAsync();

            var owner = await conn.ExecuteScalarAsync<string?>(
                $"SELECT d.ma_nd FROM dtntb_diemdanh d WHERE d.diemdanh_id = :id AND {scope}", p);
            if (owner == null) return null;

            var u = new DynamicParameters();
            u.Add("id", diemDanhId);
            u.Add("tt", trangThai);
            var hoanTac = trangThai == DiemDanhTrangThai.CHO_DUYET;
            u.Add("nguoi", hoanTac ? null : username);
            u.Add("ten", hoanTac ? null : Truncate(Trim(_currentUser.TenNv) ?? username, 200));
            u.Add("ly_do", lyDo);
            await conn.ExecuteAsync($@"
                UPDATE dtntb_diemdanh
                SET trang_thai = :tt,
                    nguoi_duyet = :nguoi,
                    ten_nguoi_duyet = :ten,
                    thoi_gian_duyet = {(hoanTac ? "NULL" : "SYSDATE")},
                    ly_do_tu_choi = :ly_do
                WHERE diemdanh_id = :id", u);

            _logger.LogInformation("Người dùng {User} đặt lượt điểm danh {Id} của {Owner} thành {TrangThai}.",
                username, diemDanhId, owner.Trim(), trangThai);

            var row = await conn.QueryFirstOrDefaultAsync<DiemDanhRow>(
                $"SELECT {ItemColumns} FROM dtntb_diemdanh d WHERE d.diemdanh_id = :id", new { id = diemDanhId });
            return row == null ? null : ToItem(row);
        }

        /// <summary>Xóa ảnh trên MinIO, lỗi chỉ ghi log (dùng khi dọn ảnh mồ côi hoặc sau khi đã xóa bản ghi).</summary>
        private async Task DeleteImagesQuietlyAsync(IEnumerable<string> keys)
        {
            foreach (var key in keys)
            {
                try { await _imageStore.DeleteAsync(key, CancellationToken.None); }
                catch (Exception ex) { _logger.LogWarning(ex, "Không xóa được ảnh điểm danh {Key} trên MinIO.", key); }
            }
        }

        // ============================================================
        // LỌC THEO PHẠM VI DỮ LIỆU
        // ============================================================

        /// <summary>Điều kiện phạm vi theo data_scope trong JWT, áp lên bảng alias d.</summary>
        private string BuildScopeClause(DynamicParameters p)
        {
            switch (_currentUser.ScopeLevel)
            {
                case UserDataScopeLevel.ToanTinh:
                    return "1 = 1";
                case UserDataScopeLevel.DiaBan:
                    if (string.IsNullOrWhiteSpace(_currentUser.DiaBanId)) return "1 = 0";
                    p.Add("scope_diaban", _currentUser.DiaBanId);
                    return @"SUBSTR(TRIM(d.ma_dv), 1, 7) IN (
                                SELECT SUBSTR(TRIM(db.ma_dv), 1, 7) FROM v_donvi_diaban db
                                WHERE TRIM(TO_CHAR(db.diaban_id)) = :scope_diaban)";
                case UserDataScopeLevel.DonVi:
                    if (string.IsNullOrWhiteSpace(_currentUser.MaDv7)) return "1 = 0";
                    p.Add("scope_dv", _currentUser.MaDv7);
                    return "SUBSTR(TRIM(d.ma_dv), 1, 7) = :scope_dv";
                case UserDataScopeLevel.ToQuanLy:
                    // Tổ trưởng xem các thành viên cùng tổ (toàn bộ ma_dv).
                    if (string.IsNullOrWhiteSpace(_currentUser.MaDv)) return "1 = 0";
                    p.Add("scope_to", _currentUser.MaDv.Trim());
                    return "TRIM(d.ma_dv) = :scope_to";
                default:
                    p.Add("scope_nd", _currentUser.Username?.Trim() ?? string.Empty);
                    return "UPPER(TRIM(d.ma_nd)) = UPPER(:scope_nd)";
            }
        }

        private (string Where, DynamicParameters Params) BuildFilter(
            DateTime tuNgay, DateTime denNgay, string? maDv, string? maTo, string? maNd, string? search, bool chiCanhBao,
            string? trangThai = null)
        {
            var p = new DynamicParameters();
            var where = new List<string> { BuildScopeClause(p), "d.ngay BETWEEN :tu_ngay AND :den_ngay" };
            p.Add("tu_ngay", tuNgay.Date, DbType.Date);
            p.Add("den_ngay", denNgay.Date, DbType.Date);

            if (!string.IsNullOrWhiteSpace(maDv) && !maDv.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                if (!MaDvPattern.IsMatch(maDv.Trim()))
                    throw new DiemDanhValidationException("Mã đơn vị không hợp lệ.");
                where.Add("SUBSTR(TRIM(d.ma_dv), 1, 7) = :f_dv");
                p.Add("f_dv", ToMaDonVi(maDv));
            }

            if (!string.IsNullOrWhiteSpace(maTo) && !maTo.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                if (!MaDvPattern.IsMatch(maTo.Trim()))
                    throw new DiemDanhValidationException("Mã tổ không hợp lệ.");
                where.Add("TRIM(d.ma_dv) = :f_to");
                p.Add("f_to", maTo.Trim());
            }

            if (!string.IsNullOrWhiteSpace(maNd))
            {
                if (maNd.Length > 100) throw new DiemDanhValidationException("Tài khoản không hợp lệ.");
                where.Add("UPPER(TRIM(d.ma_nd)) = UPPER(TRIM(:f_nd))");
                p.Add("f_nd", maNd.Trim());
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                if (search.Length > 100) throw new DiemDanhValidationException("Từ khoá tìm kiếm quá dài.");
                where.Add(@"(UPPER(d.ten_nv) LIKE :f_search OR UPPER(d.ma_nv) LIKE :f_search OR UPPER(d.ma_nd) LIKE :f_search
                             OR UPPER(d.dia_chi) LIKE :f_search OR UPPER(d.ghi_chu) LIKE :f_search)");
                p.Add("f_search", "%" + search.Trim().ToUpperInvariant() + "%");
            }

            if (chiCanhBao) where.Add("d.canh_bao IS NOT NULL");

            if (!string.IsNullOrWhiteSpace(trangThai) && !trangThai.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                var tt = trangThai.Trim().ToUpperInvariant();
                if (!DiemDanhTrangThai.All.Contains(tt))
                    throw new DiemDanhValidationException("Trạng thái duyệt không hợp lệ.");
                where.Add("d.trang_thai = :f_tt");
                p.Add("f_tt", tt);
            }

            return (string.Join(" AND ", where), p);
        }

        private static (DateTime TuNgay, DateTime DenNgay) NormalizeRange(DateTime? tuNgay, DateTime? denNgay)
        {
            var den = (denNgay ?? DateTime.Today).Date;
            var tu = (tuNgay ?? den).Date;
            if (tu > den) (tu, den) = (den, tu);
            if ((den - tu).TotalDays > MaxRangeDays)
                throw new DiemDanhValidationException($"Chỉ xem được tối đa {MaxRangeDays} ngày mỗi lần.");
            return (tu, den);
        }

        // ============================================================
        // ẢNH
        // ============================================================
        private async Task<byte[]> ReadAndValidateImageAsync(IFormFile? file, CancellationToken ct)
        {
            if (file == null || file.Length == 0)
                throw new DiemDanhValidationException("Vui lòng chụp ảnh tại hiện trường.");
            if (file.Length > _maxImageBytes)
                throw new DiemDanhValidationException($"Ảnh quá lớn (tối đa {_maxImageBytes / 1024 / 1024} MB).");

            byte[] bytes;
            await using (var input = file.OpenReadStream())
            using (var ms = new MemoryStream((int)file.Length))
            {
                await input.CopyToAsync(ms, ct);
                bytes = ms.ToArray();
            }

            // Nhận diện theo nội dung, không theo tên file (camera điện thoại đặt tên tuỳ ý).
            var isJpeg = bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
            var isPng = bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            if (!isJpeg && !isPng)
                throw new DiemDanhValidationException("Ảnh phải là JPEG hoặc PNG.");

            try
            {
                var info = new MagickImageInfo(bytes);
                if (info.Width == 0 || info.Height == 0 || (ulong)info.Width * info.Height > _maxDecodedPixels)
                    throw new DiemDanhValidationException("Kích thước ảnh không hợp lệ.");
            }
            catch (MagickException)
            {
                throw new DiemDanhValidationException("Không đọc được ảnh, vui lòng chụp lại.");
            }

            return bytes;
        }

        /// <summary>
        /// Xoay đúng chiều, thu nhỏ, xoá metadata rồi in dải thông tin (tên, giờ máy chủ, toạ độ, địa chỉ)
        /// ở cuối ảnh. Ảnh lưu luôn là JPEG.
        /// </summary>
        private byte[] BuildWatermarkedImage(byte[] source, IReadOnlyList<string> rawLines)
        {
            using var image = new MagickImage(source);
            image.AutoOrient();
            image.Resize(new MagickGeometry((uint)_maxDimension, (uint)_maxDimension) { Greater = true });
            image.Strip();
            image.ColorSpace = ColorSpace.sRGB;

            var width = (double)image.Width;
            var height = (double)image.Height;
            var fontSize = Math.Clamp(Math.Min(width, height) / 40d, 14d, 36d);
            var padding = fontSize * 0.6;
            var lineHeight = fontSize * 1.32;

            // Ngắt dòng địa chỉ dài theo bề ngang ảnh (ước lượng ~0.55 em mỗi ký tự).
            var maxChars = Math.Max(20, (int)((width - padding * 2) / (fontSize * 0.55)));
            var lines = rawLines
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .SelectMany(l => WrapText(l, maxChars))
                .Take(6)
                .ToList();

            var bandHeight = (int)Math.Ceiling(padding * 2 + lineHeight * lines.Count);
            bandHeight = Math.Min(bandHeight, (int)height);

            // Dải nền đen trong suốt ~60% ở cuối ảnh để chữ trắng luôn đọc được.
            using (var band = new MagickImage(new MagickColor(0, 0, 0, 150), (uint)width, (uint)bandHeight))
            {
                image.Composite(band, 0, (int)height - bandHeight, CompositeOperator.Over);
            }

            image.Settings.FontPointsize = fontSize;
            image.Settings.FillColor = MagickColors.White;
            image.Settings.StrokeColor = MagickColors.Transparent;
            image.Settings.TextAntiAlias = true;
            if (!string.IsNullOrEmpty(_watermarkFont)) image.Settings.Font = _watermarkFont;

            var y = height - bandHeight + padding;
            foreach (var line in lines)
            {
                image.Annotate(line, new MagickGeometry((int)padding, (int)y, (uint)(width - padding * 2), (uint)lineHeight), Gravity.Northwest);
                y += lineHeight;
            }

            image.Format = MagickFormat.Jpeg;
            image.Quality = (uint)_jpegQuality;
            return image.ToByteArray();
        }

        private static IEnumerable<string> WrapText(string text, int maxChars)
        {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var current = new StringBuilder();
            foreach (var word in words)
            {
                if (current.Length > 0 && current.Length + 1 + word.Length > maxChars)
                {
                    yield return current.ToString();
                    current.Clear();
                }
                if (current.Length > 0) current.Append(' ');
                current.Append(word.Length > maxChars ? word[..maxChars] : word);
            }
            if (current.Length > 0) yield return current.ToString();
        }

        /// <summary>
        /// Font in chữ lên ảnh phải có đủ dấu tiếng Việt. Ưu tiên cấu hình, sau đó dò font phổ biến
        /// trên Windows/Linux; không thấy thì để ImageMagick dùng font mặc định.
        /// </summary>
        private static string? ResolveWatermarkFont(string? configured)
        {
            if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();
            var candidates = new[]
            {
                // Font đóng gói sẵn trong DTNTB.API/Fonts: máy chủ không cần cài thêm gói font (không cần internet).
                Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf"),
                @"C:\Windows\Fonts\arial.ttf",
                @"C:\Windows\Fonts\segoeui.ttf",
                "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
                "/usr/share/fonts/dejavu/DejaVuSans.ttf",
                "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf"
            };
            return candidates.FirstOrDefault(File.Exists);
        }

        // ============================================================
        // TIỆN ÍCH
        // ============================================================
        private const string ItemColumns = @"
            d.diemdanh_id AS DiemDanhId, d.created_at AS ThoiGian, d.ma_nd AS MaNd, d.ma_nv AS MaNv, d.ten_nv AS TenNv,
            d.ma_dv AS MaDv, d.ten_to AS TenTo, d.ten_dv AS TenDv, d.latitude AS Latitude, d.longitude AS Longitude, d.accuracy_m AS AccuracyM,
            d.dia_chi AS DiaChi, d.loai_cong_viec AS LoaiCongViec, d.ghi_chu AS GhiChu, d.canh_bao AS CanhBao, d.so_anh AS SoAnh,
            d.trang_thai AS TrangThai, d.nguoi_duyet AS NguoiDuyet, d.ten_nguoi_duyet AS TenNguoiDuyet,
            d.thoi_gian_duyet AS ThoiGianDuyet, d.ly_do_tu_choi AS LyDoTuChoi";

        private sealed class DiemDanhRow
        {
            public long DiemDanhId { get; set; }
            public DateTime ThoiGian { get; set; }
            public string? MaNd { get; set; }
            public string? MaNv { get; set; }
            public string? TenNv { get; set; }
            public string? MaDv { get; set; }
            public string? TenTo { get; set; }
            public string? TenDv { get; set; }
            public double Latitude { get; set; }
            public double Longitude { get; set; }
            public double? AccuracyM { get; set; }
            public string? DiaChi { get; set; }
            public string? LoaiCongViec { get; set; }
            public string? GhiChu { get; set; }
            public string? CanhBao { get; set; }
            public int SoAnh { get; set; }
            public string? TrangThai { get; set; }
            public string? NguoiDuyet { get; set; }
            public string? TenNguoiDuyet { get; set; }
            public DateTime? ThoiGianDuyet { get; set; }
            public string? LyDoTuChoi { get; set; }
        }

        private sealed class UserSnapshot
        {
            public string? MaNv { get; set; }
            public string? TenNv { get; set; }
            public string? MaDv { get; set; }
            public string? TenTo { get; set; }
            public string? TenDv { get; set; }
        }

        private static DiemDanhItemDto ToItem(DiemDanhRow r) => new()
        {
            DiemDanhId = r.DiemDanhId,
            ThoiGian = r.ThoiGian,
            MaNd = r.MaNd?.Trim() ?? string.Empty,
            MaNv = r.MaNv?.Trim(),
            TenNv = r.TenNv?.Trim(),
            MaDv = r.MaDv?.Trim(),
            TenTo = r.TenTo?.Trim(),
            MaDonVi = ToMaDonVi(r.MaDv),
            TenDv = r.TenDv?.Trim(),
            Latitude = r.Latitude,
            Longitude = r.Longitude,
            AccuracyM = r.AccuracyM,
            DiaChi = r.DiaChi,
            LoaiCongViec = r.LoaiCongViec,
            TenLoaiCongViec = TenLoaiCongViec(r.LoaiCongViec),
            GhiChu = r.GhiChu,
            CanhBao = string.IsNullOrWhiteSpace(r.CanhBao)
                ? new List<string>()
                : r.CanhBao.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            AnhUrl = AnhUrl(r.DiemDanhId),
            SoAnh = Math.Max(1, r.SoAnh),
            TrangThai = string.IsNullOrWhiteSpace(r.TrangThai) ? DiemDanhTrangThai.CHO_DUYET : r.TrangThai.Trim(),
            NguoiDuyet = r.NguoiDuyet?.Trim(),
            TenNguoiDuyet = r.TenNguoiDuyet?.Trim(),
            ThoiGianDuyet = r.ThoiGianDuyet,
            LyDoTuChoi = r.LyDoTuChoi
        };

        private static StringComparer CreateVietnameseComparer()
        {
            try { return StringComparer.Create(CultureInfo.GetCultureInfo("vi-VN"), ignoreCase: true); }
            catch (CultureNotFoundException) { return StringComparer.OrdinalIgnoreCase; }
        }

        /// <summary>Mã đơn vị = 7 ký tự đầu của ma_dv.</summary>
        private static string ToMaDonVi(string? maDv)
        {
            var v = (maDv ?? string.Empty).Trim();
            return v.Length > 7 ? v[..7] : v;
        }

        private static string AnhUrl(long id) => $"/api/diemdanh/{id}/anh";

        private static string TenCanhBao(string code) => code switch
        {
            FLAG_TRUNG_ANH => "Ảnh trùng ảnh đã gửi",
            FLAG_GPS_KEM => "GPS kém chính xác",
            FLAG_LECH_GIO => "Giờ điện thoại lệch",
            _ => code
        };

        private static bool IsValidCoordinate(double lat, double lng)
            => !double.IsNaN(lat) && !double.IsNaN(lng) && lat is >= -90 and <= 90 && lng is >= -180 and <= 180
               && !(lat == 0 && lng == 0);

        private static string FormatCoordinate(double lat, double lng, double? accuracy)
        {
            var text = string.Create(CultureInfo.InvariantCulture, $"{lat:F6}, {lng:F6}");
            return accuracy.HasValue ? text + string.Create(CultureInfo.InvariantCulture, $" (±{Math.Round(accuracy.Value):0} m)") : text;
        }

        private static string? CleanText(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var clean = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (clean.Length == 0) return null;
            return clean.Length > maxLength ? clean[..maxLength] : clean;
        }

        private static bool TryParseDouble(string? value, out double result)
            => double.TryParse(value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result)
               && !double.IsNaN(result) && !double.IsInfinity(result);

        private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string? Truncate(string? value, int max)
            => value == null ? null : value.Length > max ? value[..max] : value;

        private static string ExcelText(string? value)
            => value == null ? string.Empty : new string(value.Where(c => !char.IsControl(c) || c == '\n').ToArray());
    }
}
