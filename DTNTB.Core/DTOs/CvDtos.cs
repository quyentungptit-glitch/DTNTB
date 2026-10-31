namespace DTNTB.Core.DTOs
{
    /// <summary>Hằng số Quản lý công việc TTHT (phiếu yêu cầu công việc).</summary>
    public static class CvConst
    {
        public const string LOAI_HT = "HT";
        public const string LOAI_KTHT = "KTHT";

        // Trạng thái phiếu
        public const string CHO_GIAO = "CHO_GIAO";
        public const string BI_TRA = "BI_TRA";
        public const string DANG_XL = "DANG_XL";
        public const string HOAN_THANH = "HOAN_THANH";
        public const string DA_HUY = "DA_HUY";

        // Trạng thái từng người xử lý
        public const string XL_DANG_LAM = "DANG_LAM";
        public const string XL_CHO_DONG = "CHO_DONG";
        public const string XL_DA_DONG = "DA_DONG";
        public const string XL_DA_HUY = "DA_HUY";

        // Duyệt tính lương
        public const string DUYET_CHUA = "CHUA";
        public const string DUYET_DA = "DA_DUYET";
        public const string DUYET_KHONG = "KHONG_TINH";

        public const string FILE_DINH_KEM = "DINH_KEM";
        public const string FILE_MINH_CHUNG = "MINH_CHUNG";

        public static readonly string[] UU_TIEN = { "THAP", "BINH_THUONG", "CAO", "KHAN_CAP" };
    }

    // ============================================================
    // DANH MỤC
    // ============================================================
    public class CvDauViecDto
    {
        public int DauViecId { get; set; }
        public string Ma { get; set; } = string.Empty;
        public string Ten { get; set; } = string.Empty;
        public string Nhom { get; set; } = string.Empty;
        public string LoaiPhieu { get; set; } = string.Empty;
        public int? SlaPhut { get; set; }
        public decimal? DonGia { get; set; }
        public string? Dvt { get; set; }
        public string? NhomDinhMuc { get; set; }
        public int TinhTgChay { get; set; }
        public int ThuTu { get; set; }
        public int IsActive { get; set; } = 1;
    }

    public class CvNguoiNhanDto
    {
        public long Id { get; set; }
        public string Loai { get; set; } = string.Empty;
        public string? MaDvTtvt { get; set; }
        public string? TenDvTtvt { get; set; }
        public string MaNv { get; set; } = string.Empty;
        public string? TenNv { get; set; }
        public string MaDvHt { get; set; } = string.Empty;
        public string? TenDvHt { get; set; }
        public int ThuTu { get; set; }
        public int TrangThai { get; set; } = 1;
        public string? GhiChu { get; set; }
    }

    public class CvNhanVienDto
    {
        public string MaNv { get; set; } = string.Empty;
        public string? TenNv { get; set; }
        public string? MaDv { get; set; }
        public string? TenDv { get; set; }
    }

    /// <summary>Thông tin người đang đăng nhập dùng cho màn hình.</summary>
    public class CvContextDto
    {
        public string? MaNv { get; set; }
        public string? TenNv { get; set; }
        public string? MaDvTtvt { get; set; }
        public bool CoTao { get; set; }
        public bool CoTaoToanTinh { get; set; }
        public bool CoXuLy { get; set; }
        public bool CoDuyet { get; set; }
        public bool CoXem { get; set; }
        public bool CoXuat { get; set; }
        public bool CoQuanTri { get; set; }
        /// <summary>Có tên trong bảng người nhận (tổ trưởng / người nhận KTHT).</summary>
        public bool LaNguoiNhan { get; set; }
    }

    // ============================================================
    // PHIẾU
    // ============================================================
    public class CvPhieuDto
    {
        public long PhieuId { get; set; }
        public string LoaiPhieu { get; set; } = string.Empty;
        public string TieuDe { get; set; } = string.Empty;
        public string MoTa { get; set; } = string.Empty;
        public string DoUuTien { get; set; } = "BINH_THUONG";
        public int DauViecId { get; set; }
        public string MaDauViec { get; set; } = string.Empty;
        public string TenDauViec { get; set; } = string.Empty;
        public int? SlaPhut { get; set; }
        public decimal? DonGia { get; set; }
        public string? Dvt { get; set; }
        public int TinhTgChay { get; set; }
        public string NguoiTao { get; set; } = string.Empty;
        public string? TenNguoiTao { get; set; }
        public string? MaDvTao { get; set; }
        public string? TenDvTao { get; set; }
        public DateTime NgayTao { get; set; }
        public DateTime? HanXl { get; set; }
        public string? MaDvTtvt { get; set; }
        public string? TenDvTtvt { get; set; }
        public string MaNvNhan { get; set; } = string.Empty;
        public string? TenNvNhan { get; set; }
        public string MaDvHt { get; set; } = string.Empty;
        public string? TenDvHt { get; set; }
        public string TrangThai { get; set; } = CvConst.CHO_GIAO;
        public DateTime? NgayGiao { get; set; }
        public DateTime? NgayHoanThanh { get; set; }
        public string? LyDoTra { get; set; }
        public int SoLanTra { get; set; }
        public int SoLanChuyen { get; set; }

        // Tổng hợp từ các dòng xử lý
        public int SoNguoi { get; set; }
        public int SoChoDong { get; set; }
        public int SoDaDong { get; set; }
        public string? DsNguoiXl { get; set; }
        /// <summary>Thời điểm yêu cầu khoá muộn nhất khi mọi người đã yêu cầu khoá (null nếu chưa xong).</summary>
        public DateTime? XongLuc { get; set; }
        public int QuaHanSo { get; set; }
        public bool QuaHan => QuaHanSo == 1;
        public int SoFile { get; set; }
    }

    public class CvXuLyDto
    {
        public long XuLyId { get; set; }
        public long PhieuId { get; set; }
        public string MaNv { get; set; } = string.Empty;
        public string? TenNv { get; set; }
        public string? MaDv { get; set; }
        public string NguoiGiao { get; set; } = string.Empty;
        public DateTime NgayGiao { get; set; }
        public string TrangThai { get; set; } = CvConst.XL_DANG_LAM;
        public decimal? SoLuong { get; set; }
        public string? GhiChuXl { get; set; }
        public DateTime? NgayYcKhoa { get; set; }
        public int? QuaHan { get; set; }
        public int? TgChayPhut { get; set; }
        public string? NguoiDong { get; set; }
        public DateTime? NgayDong { get; set; }
        public string? LyDoTra { get; set; }
        public int SoLanTra { get; set; }
        public string DuyetTt { get; set; } = CvConst.DUYET_CHUA;
        public decimal? SoLuongDuyet { get; set; }
        public decimal? DonGia { get; set; }
        public decimal? ThanhTien { get; set; }
        public int? ThangLuong { get; set; }
        public string? NguoiDuyet { get; set; }
        public string? TenNguoiDuyet { get; set; }
        public DateTime? NgayDuyet { get; set; }
        public string? GhiChuDuyet { get; set; }
        public List<CvFileDto> Anh { get; set; } = new();
    }

    public class CvFileDto
    {
        public long FileId { get; set; }
        public long PhieuId { get; set; }
        public long? XuLyId { get; set; }
        public string Loai { get; set; } = string.Empty;
        public string TenFile { get; set; } = string.Empty;
        public string? ContentType { get; set; }
        public long? KichThuoc { get; set; }
        public int CoThuNho { get; set; }
        public double? Lat { get; set; }
        public double? Lng { get; set; }
        public DateTime? ChupLuc { get; set; }
        public string NguoiTai { get; set; } = string.Empty;
        public DateTime NgayTai { get; set; }
    }

    public class CvLichSuDto
    {
        public long LsId { get; set; }
        public long? XuLyId { get; set; }
        public string ThaoTac { get; set; } = string.Empty;
        public string? NoiDung { get; set; }
        public string Nguoi { get; set; } = string.Empty;
        public string? TenNguoi { get; set; }
        public DateTime Ngay { get; set; }
    }

    /// <summary>Các thao tác người đang xem được làm với phiếu (máy chủ vẫn kiểm tra lại khi thực hiện).</summary>
    public class CvQuyenPhieuDto
    {
        public bool LaNguoiTao { get; set; }
        public bool LaNguoiNhan { get; set; }
        public bool SuaGuiLai { get; set; }
        public bool Huy { get; set; }
        public bool Giao { get; set; }
        public bool Tra { get; set; }
        public bool Chuyen { get; set; }
        public bool ThemDinhKem { get; set; }
        /// <summary>Dòng xử lý của chính người xem (nếu được giao).</summary>
        public long? XuLyCuaToi { get; set; }
    }

    public class CvPhieuChiTietDto
    {
        public CvPhieuDto Phieu { get; set; } = new();
        public List<CvFileDto> DinhKem { get; set; } = new();
        public List<CvXuLyDto> XuLy { get; set; } = new();
        public List<CvLichSuDto> LichSu { get; set; } = new();
        public CvQuyenPhieuDto Quyen { get; set; } = new();
    }

    public class CvPhieuQueryDto
    {
        /// <summary>TAO (tôi tạo) | NHAN (gửi cho tôi điều phối) | TAT_CA (báo cáo, theo phạm vi)</summary>
        public string? View { get; set; }
        public string? TrangThai { get; set; }
        public string? LoaiPhieu { get; set; }
        public string? MaDvHt { get; set; }
        public string? TuNgay { get; set; }
        public string? DenNgay { get; set; }
        public string? Search { get; set; }
        public bool ChiQuaHan { get; set; }
    }

    public class CvPhieuRequestDto
    {
        public string? TieuDe { get; set; }
        public string? MoTa { get; set; }
        public int DauViecId { get; set; }
        public string? DoUuTien { get; set; }
        /// <summary>Dòng trong dtntb_cv_nguoi_nhan được chọn làm người nhận.</summary>
        public long NguoiNhanId { get; set; }
    }

    public class CvLyDoRequestDto
    {
        public string? LyDo { get; set; }
    }

    public class CvChuyenRequestDto
    {
        public long NguoiNhanId { get; set; }
        public string? GhiChu { get; set; }
    }

    public class CvGiaoRequestDto
    {
        public List<string> MaNv { get; set; } = new();
        public string? GhiChu { get; set; }
    }

    public class CvYeuCauKhoaRequestDto
    {
        public decimal? SoLuong { get; set; }
        public string? GhiChu { get; set; }
    }

    public class CvAnhMetaDto
    {
        public double? Lat { get; set; }
        public double? Lng { get; set; }
        public string? ChupLuc { get; set; }
        public string? FileName { get; set; }
    }

    // ============================================================
    // VIỆC CỦA TÔI / DUYỆT
    // ============================================================
    /// <summary>Dòng xử lý kèm thông tin phiếu (Việc của tôi, Duyệt tính lương).</summary>
    public class CvViecDto : CvXuLyDto
    {
        public string LoaiPhieu { get; set; } = string.Empty;
        public string TieuDe { get; set; } = string.Empty;
        public string MaDauViec { get; set; } = string.Empty;
        public string TenDauViec { get; set; } = string.Empty;
        public string? Dvt { get; set; }
        public decimal? DonGiaPhieu { get; set; }
        public DateTime? HanXl { get; set; }
        public DateTime NgayTao { get; set; }
        public string? TenNguoiTao { get; set; }
        public string? TenDvTao { get; set; }
        public string MaDvHt { get; set; } = string.Empty;
        public string? TenDvHt { get; set; }
        public string? TenNvNhan { get; set; }
        public string TrangThaiPhieu { get; set; } = string.Empty;
        public string DoUuTien { get; set; } = string.Empty;
        public int SoAnh { get; set; }
    }

    public class CvDuyetQueryDto
    {
        /// <summary>CHUA | DA_DUYET | KHONG_TINH | (trống: tất cả)</summary>
        public string? DuyetTt { get; set; }
        public int? ThangLuong { get; set; }
        public string? MaDvHt { get; set; }
        public string? LoaiPhieu { get; set; }
        public string? Search { get; set; }
    }

    public class CvDuyetDongDto
    {
        public long XuLyId { get; set; }
        public bool TinhLuong { get; set; } = true;
        public decimal? SoLuong { get; set; }
        public string? GhiChu { get; set; }
    }

    public class CvDuyetRequestDto
    {
        public List<CvDuyetDongDto> Dong { get; set; } = new();
    }

    public class CvDuyetKetQuaDto
    {
        public int ThanhCong { get; set; }
        public int BoQua { get; set; }
        public List<string> GhiChu { get; set; } = new();
    }

    // ============================================================
    // BÁO CÁO
    // ============================================================
    public class CvBaoCaoToDto
    {
        public string MaDvHt { get; set; } = string.Empty;
        public string? TenDvHt { get; set; }
        public int SoPhieuNgay { get; set; }
        public int QuaHanNgay { get; set; }
        public int SoPhieuThang { get; set; }
        public int QuaHanThang { get; set; }
        public int DangXuLy { get; set; }
        public int HoanThanhThang { get; set; }
    }

    public class CvBaoCaoLuongDto
    {
        public string MaNv { get; set; } = string.Empty;
        public string? TenNv { get; set; }
        public string? MaDvHt { get; set; }
        public string? TenDvHt { get; set; }
        public int SoViec { get; set; }
        public int SoViecKtht { get; set; }
        public decimal TongTien { get; set; }
        public decimal TienKtht { get; set; }
    }
}
