using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace DTNTB.Core.DTOs
{
    public class DtntbDetailDto
    {
        public long ThueBaoId { get; set; }
        public int PhanVungId { get; set; }
        public long PhieuId { get; set; }
        public string MaTB { get; set; } = string.Empty;
        public string TenTB { get; set; } = string.Empty;
        public string SoDT { get; set; } = string.Empty;
        public string DiaChiTB { get; set; } = string.Empty;
        public int DiemTinNhiem { get; set; }
        public string MaDv { get; set; } = string.Empty;
        public string TenNvkt { get; set; } = string.Empty;

        // Điểm chi tiết các mắt lưới
        public double Tgsd { get; set; }
        public int DiemTgsd { get; set; }
        public int DiemDadv { get; set; }
        public string DichVuDetail { get; set; } = "Internet đơn lẻ";
        public int SoThangTt { get; set; }
        public int DiemTttt { get; set; }
        public double TuoiOnt { get; set; }
        public int DiemTbi { get; set; }
        public int SlSuyHao { get; set; }
        public int DiemSuyHao { get; set; }
        public int SlOffLos { get; set; }
        public int DiemOffLos { get; set; }
        public int SolanBh1t { get; set; }
        public int DiemBhll { get; set; }
        public int DiemKohl { get; set; }
        public double TgscTb { get; set; }
        public double DiemTgsc { get; set; }

        // ─── 4 THUỘC TÍNH LỊCH SỬ DÙNG CHO DANH SÁCH BẢNG NGOÀI (BỊ THIẾU) ───
        public int DiemGocLs { get; set; }
        public int DiemSauLs { get; set; }
        public string NgayTaoLs { get; set; } = "-";
        public bool DaTacNghiep { get; set; }

        // Các trường can thiệp thực địa mới
        public bool DaThayThietBi { get; set; }
        public bool DaThietBiTot { get; set; }
        public bool DaSuaSuyHao { get; set; }
        public bool DaTuVanCuoc6T { get; set; }
        public bool DaTuVanCuoc12T { get; set; }
        public bool DaTrichNoTuDong { get; set; }
        public bool DaTuVanMyTV { get; set; }
        public bool DaTuVanMesh { get; set; }
        public bool DaTuVanCam { get; set; }
        public string GhiChu { get; set; } = string.Empty;

        // Dữ liệu gốc phục vụ tính điểm giả lập
        public bool CoMyTV { get; set; }
        public bool CoMeshCam { get; set; }
        public double SoThangConLai { get; set; }

        public string NgayGiao { get; set; } = string.Empty;
        public int TrangThaiPhieu { get; set; }
        public string NgaySd { get; set; } = string.Empty;
        public string NgayKtdc { get; set; } = string.Empty;
        public string LoaiOnt { get; set; } = string.Empty;
        public bool IsSlaExpired { get; set; }
        public List<string> AnhCskhUrls { get; set; } = new();
        public List<LichSuTacNghiepDto> LichSu { get; set; } = new();

        // Thiết bị phụ trợ
        public bool DaDungCamVnpt { get; set; }
        public int SlCamVnpt { get; set; }
        public bool DaDungCamOther { get; set; }
        public int SlCamOther { get; set; }
        public bool DaDungTotoVnpt { get; set; }
        public int SlTotoVnpt { get; set; }
        public bool DaDungTotoOther { get; set; }
        public int SlTotoOther { get; set; }
    }

    public class LichSuTacNghiepDto
    {
        public long? PhieuId { get; set; }
        public string NgayGiao { get; set; } = "-";
        public string NgayTao { get; set; } = string.Empty;
        public string NguoiXuly { get; set; } = string.Empty;
        public string TenNv { get; set; } = string.Empty;
        public short DaThayThietBi { get; set; }
        public short DaThietBiTot { get; set; }
        public short DaSuaSuyHao { get; set; }
        public short DaTuVanCuoc6T { get; set; }
        public short DaTuVanCuoc12T { get; set; }
        public short DaTrichNoTuDong { get; set; }
        public short DaTuVanMyTV { get; set; }
        public short DaTuVanMesh { get; set; }
        public short DaTuVanCam { get; set; }
        public short DaTuVanCuoc { get; set; }
        public short DaTuVanCombo { get; set; }
        public string GhiChu { get; set; } = string.Empty;
        public int DiemGoc { get; set; }
        public int DiemSauXl { get; set; }
    }

    public class SaveTacNghiepFormDto
    {
        [System.ComponentModel.DataAnnotations.Range(1, long.MaxValue)]
        public long PhieuId { get; set; }
        public bool DaThayThietBi { get; set; }
        public bool DaThietBiTot { get; set; }
        public bool DaSuaSuyHao { get; set; }
        public bool DaTuVanCuoc6T { get; set; }
        public bool DaTuVanCuoc12T { get; set; }
        public bool DaTrichNoTuDong { get; set; }
        public bool DaTuVanMyTV { get; set; }
        public bool DaTuVanMesh { get; set; }
        public bool DaTuVanCam { get; set; }
        [System.ComponentModel.DataAnnotations.StringLength(2000)]
        public string? GhiChu { get; set; }
        public bool IsUnresolved { get; set; }
        public List<IFormFile>? fuAnhCSKH { get; set; }

        public bool DaDungCamVnpt { get; set; }
        [System.ComponentModel.DataAnnotations.Range(0, 100)]
        public int SlCamVnpt { get; set; }
        public bool DaDungCamOther { get; set; }
        [System.ComponentModel.DataAnnotations.Range(0, 100)]
        public int SlCamOther { get; set; }
        public bool DaDungTotoVnpt { get; set; }
        [System.ComponentModel.DataAnnotations.Range(0, 100)]
        public int SlTotoVnpt { get; set; }
        public bool DaDungTotoOther { get; set; }
        [System.ComponentModel.DataAnnotations.Range(0, 100)]
        public int SlTotoOther { get; set; }
    }

    public class DeleteImageRequestDto
    {
        [System.ComponentModel.DataAnnotations.Range(1, long.MaxValue)]
        public long PhieuId { get; set; }
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(1000)]
        public string ImageUrl { get; set; } = string.Empty;
    }

    public class RiskCountDto
    {
        public int Total { get; set; }
        public int BinhThuong { get; set; }
        public int TheoDoi { get; set; }
        public int NguyCo { get; set; }
        public int Cao { get; set; }
        public int RatCao { get; set; }
    }

    public class DropdownItemDto
    {
        public string Value { get; set; } = string.Empty;
        public string DisplayText { get; set; } = string.Empty;
    }

    public class PaginatedResultDto<T>
    {
        public List<T> Items { get; set; } = new();
        public int TotalCount { get; set; }
    }
}
