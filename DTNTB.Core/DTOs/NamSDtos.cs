namespace DTNTB.Core.DTOs
{
    // ============================================================
    // 5S NHÀ TRẠM
    // ============================================================

    public static class NamSTrangThai
    {
        public const string DA_GIAO = "DA_GIAO";   // đã giao, nhân viên đang thực hiện
        public const string DA_NOP = "DA_NOP";     // nhân viên đã nộp, chờ chấm
        public const string DA_CHAM = "DA_CHAM";
        public const string THU_HOI = "THU_HOI";
    }

    public class NamSTieuChiDto
    {
        public int TieuChiId { get; set; }
        public string Ma { get; set; } = string.Empty;
        public string NoiDung { get; set; } = string.Empty;
        public string? YeuCauAnh { get; set; }
        public int SlAnhToiThieu { get; set; }
        public decimal DiemToiDa { get; set; }
        public bool BatBuoc => BatBuocSo == 1;
        [System.Text.Json.Serialization.JsonIgnore] public int BatBuocSo { get; set; }
        public int ThuTu { get; set; }
    }

    public class NamSOptionDto
    {
        public string Ma { get; set; } = string.Empty;
        public string? Ten { get; set; }
    }

    /// <summary>Một trạm trong màn Giao trạm (kèm phiếu đang mở và lần chấm gần nhất).</summary>
    public class NamSTramDto
    {
        public string MaCsht { get; set; } = string.Empty;
        public string? TenCsht { get; set; }
        public string? MaToQl { get; set; }
        public string? TenToQl { get; set; }
        public string? MaDv { get; set; }
        public string? TenDv { get; set; }
        public string? PhuongXa { get; set; }
        public string? DiaChi { get; set; }

        /// <summary>Phiếu đang giao (nhân viên chưa nộp); có thì không giao thêm.</summary>
        public long? PhieuMoId { get; set; }
        public string? NvMo { get; set; }
        public string? MaNdMo { get; set; }
        public DateTime? NgayGiaoMo { get; set; }

        /// <summary>Số lần đã giao trong tháng hiện tại (không tính thu hồi).</summary>
        public int SoLanThang { get; set; }

        /// <summary>Lần đã nộp gần nhất: DA_NOP (chờ chấm) hoặc DA_CHAM.</summary>
        public long? PhieuGanNhatId { get; set; }
        public string? TrangThaiGanNhat { get; set; }
        public decimal? DiemGanNhat { get; set; }
        public string? XepLoaiGanNhat { get; set; }
        public DateTime? NgayNopGanNhat { get; set; }
    }

    public class NamSTramQueryDto
    {
        public string? MaDv { get; set; }
        public string? MaToQl { get; set; }
        public string? Search { get; set; }
    }

    public class NamSNhanVienDto
    {
        public string MaNd { get; set; } = string.Empty;
        public string? MaNv { get; set; }
        public string? TenNv { get; set; }
        public string? MaDv { get; set; }
        /// <summary>Số phiếu đang giữ (chưa nộp) để người giao cân đối.</summary>
        public int SoPhieuMo { get; set; }
    }

    /// <summary>Trạm để nhân viên tự tạo phiếu 5S (tìm theo từ khoá hoặc gần vị trí hiện tại).</summary>
    public class NamSTramTuTaoDto
    {
        public string MaCsht { get; set; } = string.Empty;
        public string? TenCsht { get; set; }
        public string? DiaChi { get; set; }
        public string? TenToQl { get; set; }
        public string? TenDv { get; set; }
        public double? Lat { get; set; }
        public double? Lng { get; set; }
        public int? KhoangCachM { get; set; }
        /// <summary>Phiếu đang giao (chưa nộp) của trạm, nếu có.</summary>
        public long? PhieuMoId { get; set; }
        public string? NvMo { get; set; }
        public bool PhieuCuaToi { get; set; }
    }

    public class NamSTuTaoRequestDto
    {
        public string? MaCsht { get; set; }
        public string? GhiChu { get; set; }
    }

    public class NamSGiaoRequestDto
    {
        public List<string> MaCsht { get; set; } = new();
        public string? MaNd { get; set; }
        public string? GhiChu { get; set; }
    }

    /// <summary>Một dòng trong file import giao trạm.</summary>
    public class NamSImportDongDto
    {
        public int Dong { get; set; }
        public string? MaCsht { get; set; }
        public string? TenCsht { get; set; }
        public string? TenToQl { get; set; }
        public string? MaNv { get; set; }
        public string? MaNd { get; set; }
        public string? TenNv { get; set; }
        public string? GhiChu { get; set; }
        public bool HopLe { get; set; }
        public string? LyDo { get; set; }
        /// <summary>Đã tạo phiếu (chỉ khi xác nhận giao).</summary>
        public bool DaGiao { get; set; }
    }

    public class NamSImportKetQuaDto
    {
        public bool XacNhan { get; set; }
        public int TongDong { get; set; }
        public int SoHopLe { get; set; }
        public int SoLoi { get; set; }
        public int SoDaGiao { get; set; }
        public List<NamSImportDongDto> Dong { get; set; } = new();
    }

    public class NamSKetQuaGiaoDto
    {
        public int ThanhCong { get; set; }
        public int BoQua { get; set; }
        public List<string> GhiChu { get; set; } = new();
    }

    /// <summary>Phiếu dạng dòng (danh sách của tôi / chấm / báo cáo chi tiết).</summary>
    public class NamSPhieuDto
    {
        public long PhieuId { get; set; }
        public int Thang { get; set; }
        public int Lan { get; set; }
        public string MaCsht { get; set; } = string.Empty;
        public string? TenCsht { get; set; }
        public string? MaDv { get; set; }
        public string? TenDv { get; set; }
        public string? MaToQl { get; set; }
        public string? TenToQl { get; set; }
        public string? DiaChi { get; set; }
        public double? ToaDoLat { get; set; }
        public double? ToaDoLong { get; set; }
        public string? MaNdNhan { get; set; }
        public string? MaNvNhan { get; set; }
        public string? TenNvNhan { get; set; }
        public string? TenNguoiGiao { get; set; }
        public DateTime NgayGiao { get; set; }
        public string? GhiChuGiao { get; set; }
        public string TrangThai { get; set; } = NamSTrangThai.DA_GIAO;
        public DateTime? NgayNop { get; set; }
        public string? GhiChuNop { get; set; }
        public decimal? TongDiem { get; set; }
        public decimal? DiemToiDa { get; set; }
        public string? XepLoai { get; set; }
        public string? NhanXet { get; set; }
        public string? TenNguoiCham { get; set; }
        public DateTime? NgayCham { get; set; }
        public int SoAnh { get; set; }
        /// <summary>Số ảnh có cảnh báo (xa trạm, không GPS, trùng ảnh).</summary>
        public int SoAnhCanhBao { get; set; }
        /// <summary>Nhân viên tự tạo (người giao = người nhận).</summary>
        public bool TuTao => TuTaoSo == 1;
        [System.Text.Json.Serialization.JsonIgnore] public int TuTaoSo { get; set; }
        /// <summary>Số lần người chấm trả phiếu về cho nhân viên chụp bổ sung.</summary>
        public int SoLanTra { get; set; }
        public string? LyDoTra { get; set; }
        public DateTime? NgayTra { get; set; }
        public string? TenNguoiTra { get; set; }
    }

    public class NamSTraPhieuRequestDto
    {
        public string? LyDo { get; set; }
    }

    /// <summary>Trạm trên bản đồ 5S với kết quả trong kỳ.</summary>
    public class NamSBanDoTramDto
    {
        public string MaCsht { get; set; } = string.Empty;
        public string? TenCsht { get; set; }
        public string? DiaChi { get; set; }
        public string? MaDv { get; set; }
        public string? TenDv { get; set; }
        public string? MaToQl { get; set; }
        public string? TenToQl { get; set; }
        public double? Lat { get; set; }
        public double? Lng { get; set; }
        /// <summary>TOT | KHA | TB | KEM (đã chấm, lấy phiếu chấm gần nhất) | CHO_CHAM | DANG_LAM | CHUA.</summary>
        public string KetQua { get; set; } = "CHUA";
        /// <summary>Điểm % của phiếu chấm gần nhất trong kỳ.</summary>
        public decimal? Diem { get; set; }
        public long? PhieuId { get; set; }
        public DateTime? NgayCham { get; set; }
        public string? TenNv { get; set; }
        public int SoPhieu { get; set; }
    }

    public class NamSAnhDto
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
        public List<string> CanhBao { get; set; } = new();
        public string AnhUrl { get; set; } = string.Empty;
    }

    public class NamSTieuChiPhieuDto : NamSTieuChiDto
    {
        public decimal? Diem { get; set; }
        public string? GhiChuDiem { get; set; }
        public List<NamSAnhDto> Anh { get; set; } = new();
    }

    public class NamSPhieuChiTietDto
    {
        public NamSPhieuDto Phieu { get; set; } = new();
        public List<NamSTieuChiPhieuDto> TieuChi { get; set; } = new();
        /// <summary>Người xem là nhân viên được giao và phiếu còn mở: được tải/xóa ảnh, nộp.</summary>
        public bool DuocSua { get; set; }
        public bool DuocCham { get; set; }
    }

    /// <summary>Thông tin kèm từng ảnh tải lên (đọc ở trình duyệt từ EXIF hoặc GPS điện thoại).</summary>
    public class NamSAnhMetaDto
    {
        public double? Lat { get; set; }
        public double? Lng { get; set; }
        /// <summary>EXIF | THIET_BI</summary>
        public string? Nguon { get; set; }
        /// <summary>Giờ chụp ISO 8601 (EXIF DateTimeOriginal), giờ địa phương.</summary>
        public string? ChupLuc { get; set; }
        public string? FileName { get; set; }
    }

    public class NamSNopRequestDto
    {
        public string? GhiChu { get; set; }
    }

    public class NamSDiemItemDto
    {
        public int TieuChiId { get; set; }
        public decimal Diem { get; set; }
        public string? GhiChu { get; set; }
    }

    public class NamSChamRequestDto
    {
        public List<NamSDiemItemDto> Diem { get; set; } = new();
        public string? NhanXet { get; set; }
    }

    public class NamSPhieuQueryDto
    {
        /// <summary>yyyyMM; bỏ trống = mọi tháng (chấm) / tháng hiện tại (báo cáo).</summary>
        public int? TuThang { get; set; }
        public int? DenThang { get; set; }
        /// <summary>Lọc theo ngày giao (ưu tiên hơn TuThang/DenThang khi có).</summary>
        public DateTime? TuNgay { get; set; }
        public DateTime? DenNgay { get; set; }
        /// <summary>Báo cáo theo nhân viên: lọc theo đơn vị (7 ký tự) / tổ (ma_dv) của nhân viên.</summary>
        public string? NvMaDv { get; set; }
        public string? NvMaTo { get; set; }
        public string? MaDv { get; set; }
        public string? MaToQl { get; set; }
        /// <summary>DA_GIAO | DA_NOP | DA_CHAM | NOP_CHAM (đã nộp hoặc đã chấm) | bỏ trống = tất cả (trừ thu hồi)</summary>
        public string? TrangThai { get; set; }
        public string? XepLoai { get; set; }
        public string? Search { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }

    /// <summary>Một dòng tổng hợp theo tổ (màn hình nhóm đơn vị -> tổ).</summary>
    public class NamSTongHopDto
    {
        public string? MaDv { get; set; }
        public string? TenDv { get; set; }
        public string? MaToQl { get; set; }
        public string? TenToQl { get; set; }
        public int SoTram { get; set; }
        public int SoPhieuGiao { get; set; }
        public int SoDaNop { get; set; }
        public int SoDaCham { get; set; }
        public int SoTramDaCham { get; set; }
        public decimal? DiemTb { get; set; }
        public int SoTot { get; set; }
        public int SoKha { get; set; }
        public int SoTb { get; set; }
        public int SoKem { get; set; }
    }

    /// <summary>Tổng hợp theo nhân viên thực hiện.</summary>
    public class NamSTongHopNvDto
    {
        public string? MaNv { get; set; }
        public string? MaNd { get; set; }
        public string? TenNv { get; set; }
        public string? TenDv { get; set; }
        public string? TenToQl { get; set; }
        public int SoPhieuGiao { get; set; }
        public int SoTuTao { get; set; }
        public int SoDangLam { get; set; }
        public int SoDaNop { get; set; }
        public int SoDaCham { get; set; }
        public decimal? DiemTb { get; set; }
        public int SoTot { get; set; }
        public int SoKha { get; set; }
        public int SoTb { get; set; }
        public int SoKem { get; set; }
        public int SoAnh { get; set; }
        public int SoAnhCanhBao { get; set; }
    }

    public class NamSXepLoaiDto
    {
        public string Ma { get; set; } = string.Empty;
        public string Ten { get; set; } = string.Empty;
        /// <summary>Phần trăm điểm tối thiểu để đạt loại này.</summary>
        public decimal TuPhanTram { get; set; }
    }
}
