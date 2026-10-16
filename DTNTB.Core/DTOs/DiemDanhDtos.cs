using Microsoft.AspNetCore.Http;

namespace DTNTB.Core.DTOs
{
    // ============================================================
    // ĐIỂM DANH HIỆN TRƯỜNG
    // ============================================================

    /// <summary>Dữ liệu nhân viên gửi lên khi bấm "Điểm danh" (multipart/form-data).</summary>
    public class DiemDanhCheckInFormDto
    {
        /// <summary>Ảnh chụp tại chỗ (JPEG/PNG). Gửi nhiều ảnh bằng cách lặp lại trường "anh".</summary>
        public List<IFormFile>? Anh { get; set; }

        // Toạ độ nhận dạng chuỗi rồi parse theo InvariantCulture ở service, tránh lỗi dấu thập phân
        // khi máy chủ chạy locale vi-VN (form-data được ASP.NET bind theo CurrentCulture).
        public string? Latitude { get; set; }
        public string? Longitude { get; set; }

        /// <summary>Sai số GPS (mét) do trình duyệt trả về trong coords.accuracy.</summary>
        public string? Accuracy { get; set; }

        /// <summary>Mã việc chọn nhanh (xem GET /api/diemdanh/loai-cong-viec).</summary>
        public string? LoaiCongViec { get; set; }

        public string? GhiChu { get; set; }

        /// <summary>Giờ trên điện thoại (ISO 8601). Chỉ lưu để đối chiếu, không dùng làm giờ điểm danh.</summary>
        public string? ClientTime { get; set; }
    }

    public class DiemDanhCheckInResultDto
    {
        public long DiemDanhId { get; set; }
        public DateTime ThoiGian { get; set; }
        public string? DiaChi { get; set; }
        public List<string> CanhBao { get; set; } = new();
        public string AnhUrl { get; set; } = string.Empty;
        public int SoAnh { get; set; } = 1;
    }

    public class LoaiCongViecDto
    {
        public string Ma { get; set; } = string.Empty;
        public string Ten { get; set; } = string.Empty;
    }

    public class DiaChiDto
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        /// <summary>Null khi dịch vụ bản đồ chưa cấu hình hoặc không phản hồi.</summary>
        public string? DiaChi { get; set; }
    }

    /// <summary>Một lượt điểm danh (dùng cho bản đồ, lịch sử, điểm danh của tôi).</summary>
    public class DiemDanhItemDto
    {
        public long DiemDanhId { get; set; }
        public DateTime ThoiGian { get; set; }
        public string MaNd { get; set; } = string.Empty;
        public string? MaNv { get; set; }
        public string? TenNv { get; set; }
        /// <summary>Mã tổ = toàn bộ ma_dv của nhân viên.</summary>
        public string? MaDv { get; set; }
        public string? TenTo { get; set; }
        /// <summary>Mã đơn vị = 7 ký tự đầu của ma_dv.</summary>
        public string? MaDonVi { get; set; }
        public string? TenDv { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double? AccuracyM { get; set; }
        public string? DiaChi { get; set; }
        public string? LoaiCongViec { get; set; }
        public string? TenLoaiCongViec { get; set; }
        public string? GhiChu { get; set; }
        public List<string> CanhBao { get; set; } = new();
        /// <summary>Đường dẫn API để xem ảnh đầu tiên: /api/diemdanh/{id}/anh (ảnh thứ i: ?i=0..SoAnh-1)</summary>
        public string AnhUrl { get; set; } = string.Empty;
        /// <summary>Số ảnh của lượt điểm danh.</summary>
        public int SoAnh { get; set; } = 1;

        /// <summary>CHO_DUYET | DA_DUYET | TU_CHOI</summary>
        public string TrangThai { get; set; } = DiemDanhTrangThai.CHO_DUYET;
        /// <summary>Tài khoản (ma_nd) người duyệt/từ chối gần nhất.</summary>
        public string? NguoiDuyet { get; set; }
        public string? TenNguoiDuyet { get; set; }
        public DateTime? ThoiGianDuyet { get; set; }
        public string? LyDoTuChoi { get; set; }
    }

    /// <summary>Trạng thái duyệt của một lượt điểm danh.</summary>
    public static class DiemDanhTrangThai
    {
        public const string CHO_DUYET = "CHO_DUYET";
        public const string DA_DUYET = "DA_DUYET";
        public const string TU_CHOI = "TU_CHOI";

        public static readonly IReadOnlyList<string> All = new[] { CHO_DUYET, DA_DUYET, TU_CHOI };

        public static string Ten(string? code) => code switch
        {
            DA_DUYET => "Đã duyệt",
            TU_CHOI => "Từ chối",
            _ => "Chờ duyệt"
        };
    }

    /// <summary>Duyệt / từ chối / đưa về chờ duyệt một lượt điểm danh.</summary>
    public class DiemDanhDuyetDto
    {
        /// <summary>DA_DUYET | TU_CHOI | CHO_DUYET (hoàn tác về chờ duyệt).</summary>
        public string? TrangThai { get; set; }
        /// <summary>Bắt buộc khi TU_CHOI, tối đa 500 ký tự.</summary>
        public string? LyDo { get; set; }
    }

    /// <summary>Một dòng trong danh sách "ai đã điểm danh, mấy lần" theo ngày.</summary>
    public class DiemDanhTongHopDto
    {
        public string MaNd { get; set; } = string.Empty;
        public string? MaNv { get; set; }
        public string? TenNv { get; set; }
        public string? MaDv { get; set; }
        public string? TenTo { get; set; }
        public string? TenDv { get; set; }
        public int SoLan { get; set; }
        public DateTime LanDau { get; set; }
        public DateTime LanCuoi { get; set; }
        /// <summary>Lượt điểm danh gần nhất, để màn hình bấm vào xem ngay vị trí/ảnh.</summary>
        public long DiemDanhIdCuoi { get; set; }
        public string? DiaChiCuoi { get; set; }
        public int SoCanhBao { get; set; }
        public int SoDaDuyet { get; set; }
        public int SoChoDuyet { get; set; }
        public int SoTuChoi { get; set; }
    }

    /// <summary>Một tổ trong danh sách tổng hợp (mã tổ = toàn bộ ma_dv).</summary>
    public class DiemDanhTongHopNhomDto
    {
        public string? MaDv { get; set; }
        public string? TenTo { get; set; }
        public int SoNguoi { get; set; }
        public int SoLuot { get; set; }
        public int SoChoDuyet { get; set; }
        public List<DiemDanhTongHopDto> NhanVien { get; set; } = new();
    }

    /// <summary>Một đơn vị (7 ký tự đầu ma_dv) gồm các tổ bên dưới.</summary>
    public class DiemDanhTongHopDonViDto
    {
        public string MaDonVi { get; set; } = string.Empty;
        public string? TenDv { get; set; }
        public int SoTo { get; set; }
        public int SoNguoi { get; set; }
        public int SoLuot { get; set; }
        public int SoChoDuyet { get; set; }
        public List<DiemDanhTongHopNhomDto> To { get; set; } = new();
    }

    public class DiemDanhLichSuQueryDto
    {
        public DateTime? TuNgay { get; set; }
        public DateTime? DenNgay { get; set; }
        /// <summary>Lọc theo đơn vị: 7 ký tự đầu của ma_dv.</summary>
        public string? MaDv { get; set; }
        /// <summary>Lọc theo tổ: toàn bộ ma_dv.</summary>
        public string? MaTo { get; set; }
        /// <summary>Lọc theo một người (ma_nd).</summary>
        public string? MaNd { get; set; }
        /// <summary>Tìm theo tên, mã nhân viên, địa chỉ hoặc ghi chú.</summary>
        public string? Search { get; set; }
        public bool ChiCanhBao { get; set; }
        /// <summary>Lọc theo trạng thái duyệt: CHO_DUYET | DA_DUYET | TU_CHOI (bỏ trống = tất cả).</summary>
        public string? TrangThai { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public class DonViOptionDto
    {
        public string MaDv { get; set; } = string.Empty;
        public string TenDv { get; set; } = string.Empty;
    }

    public class ToOptionDto
    {
        public string MaTo { get; set; } = string.Empty;
        public string TenTo { get; set; } = string.Empty;
    }
}
