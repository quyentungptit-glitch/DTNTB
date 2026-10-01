using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DTNTB.Core.DTOs
{
    // DTO cho Dropdown đơn vị/nhân viên
    public class GhttDonViDto
    {
        public int DonViId { get; set; }
        public string TenDv { get; set; } = string.Empty;
    }

    // Giá trị hiển thị tại từng cell dữ liệu trên ma trận báo cáo
    public class GhttUnitValueDto
    {
        public string TlhtDisplay { get; set; } = "—"; // Hiển thị % hoặc "—"
        public string PhanSo { get; set; } = "0 / 0";   // Hiển thị "Thực hiện / Tổng"
        public string ColorClass { get; set; } = string.Empty; // text-success, text-warning, text-danger-bold
        public string BgClass { get; set; } = string.Empty;    // bg-light-green, bg-light-red
    }

    // Một hàng chỉ số (Row) sau khi Pivot
    public class GhttReportRowDto
    {
        public string MaCS { get; set; } = string.Empty;
        public string TenChiSo { get; set; } = string.Empty;
        public string MucTieu { get; set; } = string.Empty;
        public List<GhttUnitValueDto> Values { get; set; } = new();
    }

    // Toàn bộ kết quả trả về cho giao diện lưới Báo cáo
    public class GhttReportResponseDto
    {
        public string? LastSyncGhtt { get; set; }  // Thời gian tổng hợp gần nhất
        public string? LastSyncChot { get; set; }  // Thời gian chốt số liệu gần nhất
        public List<string> Headers { get; set; } = new(); // Danh sách tên cột động
        public List<GhttReportRowDto> Rows { get; set; } = new();
    }

    // Request DTO cho thao tác Tổng hợp & Chốt số liệu
    public class GhttActionRequestDto
    {
        [Required]
        [Range(200001, 209912, ErrorMessage = "Tháng phải có định dạng yyyyMM")]
        public int Thang { get; set; }
    }
}