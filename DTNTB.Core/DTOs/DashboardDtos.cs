using System;
using System.Collections.Generic;

namespace DTNTB.Core.DTOs
{
    public class DashboardKpiDto
    {
        public int TongNguyCo { get; set; }        // Thẻ 1: Thuê bao nguy cơ (>= 29đ)
        public int TongPhieuHomNay { get; set; }    // Thẻ 2: Phiếu kế hoạch hôm nay (>= 32đ)
        public int DaXuLy { get; set; }             // Thẻ 3: Đã xử lý thực địa
        public double AvgScore { get; set; }        // Thẻ 4: Điểm rủi ro trung bình

        // Dữ liệu Doughnut Chart
        public int BinhThuong { get; set; }
        public int TheoDoi { get; set; }
        public int NguyCo { get; set; }
        public int NguyCoCao { get; set; }
        public int RatCao { get; set; }

        // Dữ liệu Bar Chart theo đơn vị
        public List<UnitRiskDto> UnitRisks { get; set; } = new();
    }

    public class UnitRiskDto
    {
        public string TenDv { get; set; } = string.Empty;
        public int TotalRisk { get; set; }
    }

    public class RecentActivityDto
    {
        public decimal? PhieuId { get; set; }
        public DateTime? NgayGiao { get; set; }
        public DateTime? NgayTao { get; set; }
        public string MaTb { get; set; } = string.Empty;
        public string TenTb { get; set; } = string.Empty;
        public string NguoiXuLy { get; set; } = string.Empty;
        public string TenNv { get; set; } = string.Empty;
        public string MaDv { get; set; } = string.Empty;
        public string TenDv { get; set; } = string.Empty;
        public string GhiChu { get; set; } = string.Empty;

        public int DaThayThietbi { get; set; }
        public int DaThietbiTot { get; set; }
        public int DaSuaSuyhao { get; set; }
        public int DaTuvanCuoc { get; set; }
        public int DaTrichnoTudong { get; set; }
        public int DaTuvanCombo { get; set; }
    }

    public class KeHoachItemDto
    {
        public decimal PhieuId { get; set; }
        public string MaTb { get; set; } = string.Empty;
        public string TenTb { get; set; } = string.Empty;
        public string SoDt { get; set; } = string.Empty;
        public string TenNvkt { get; set; } = string.Empty;
        public string MaNvkt { get; set; } = string.Empty;
        public string MaDv { get; set; } = string.Empty;
        public string TenDv { get; set; } = string.Empty;
        public int TrangThaiPhieu { get; set; }
        public string? TenNvNhan { get; set; }
        public DateTime? NgayGiao { get; set; }

        // 9 chỉ tiêu điểm số chi tiết
        public int DiemTbi { get; set; }
        public int DiemSuyhao { get; set; }
        public int DiemOfflos { get; set; }
        public int DiemTttt { get; set; }
        public int DiemDadv { get; set; }
        public int DiemBhll { get; set; }
        public int DiemKohl { get; set; }
        public double DiemTgsc { get; set; }
        public int DiemTgsd { get; set; }
        public int DiemTinNhiem { get; set; }

        public string? GhiChuGiao { get; set; }
    }

    public class AssignPlanRequest
    {
        public decimal PhieuId { get; set; }
        public string? GhiChuGiao { get; set; }
    }

    public class AssignAllPlansRequest
    {
        public string? GhiChuChung { get; set; }
    }
}