namespace DTNTB.Core.DTOs
{
    // ============================================================
    // GIAO PHIẾU TIẾP CẬN THUÊ BAO GHTT (Biểu 8) - dùng chung bảng GHTT_PHIEU với BCBG
    // ============================================================

    /// <summary>Tình trạng của TB trong danh sách giao / của phiếu.</summary>
    public static class GhttPhieuTinhTrang
    {
        public const string CHUA_GIAO = "CHUA_GIAO";   // TB chưa có phiếu
        public const string DA_GIAO = "DA_GIAO";       // phiếu còn hạn, chưa có kết quả
        public const string QUA_HAN = "QUA_HAN";       // phiếu hết hạn 72h, chưa có kết quả
        public const string KHONG_TC = "KHONG_TC";
        public const string THANH_CONG = "THANH_CONG";
    }

    public class GhttThangDto
    {
        public int Thang { get; set; }
    }

    public class GhttPhieuDonViDto
    {
        /// <summary>7 ký tự đầu mã đơn vị (215.3xxx).</summary>
        public string MaDv { get; set; } = string.Empty;
        public string? TenDv { get; set; }
    }

    /// <summary>Một TB trong danh sách cần tiếp cận (màn Giao phiếu) kèm phiếu gần nhất.</summary>
    public class GhttThueBaoDto
    {
        /// <summary>"phanvung_id|thuebao_id" - khóa để chọn khi giao.</summary>
        public string Khoa { get; set; } = string.Empty;
        public int PhanVungId { get; set; }
        public long ThueBaoId { get; set; }
        public int SlThang { get; set; }
        public string? MaTb { get; set; }
        public string? TenTb { get; set; }
        public string? DiaChi { get; set; }
        public string? SoDt { get; set; }
        public string? TenDv { get; set; }
        public string? MaDvQl { get; set; }
        public long? DoiVtId { get; set; }
        public string? TenDoiVt { get; set; }
        public DateTime? NgayKtdc { get; set; }
        public string? TenKm { get; set; }
        public string? KqOb { get; set; }
        public DateTime? NgayCn { get; set; }
        public string? MaNvTh { get; set; }
        public string? TenNvTh { get; set; }

        public long? PhieuId { get; set; }
        public string? MaNvNhan { get; set; }
        public string? TenNvNhan { get; set; }
        public DateTime? NgayGiao { get; set; }
        public DateTime? HanXl { get; set; }
        public string? LyDo { get; set; }
        public string? GhiChuKq { get; set; }
        public DateTime? NgayKq { get; set; }
        public DateTime? NgayHen { get; set; }
        public int SoLanGiao { get; set; }
        /// <summary>CHUA_GIAO | DA_GIAO | QUA_HAN | KHONG_TC | THANH_CONG</summary>
        public string TinhTrang { get; set; } = GhttPhieuTinhTrang.CHUA_GIAO;
        /// <summary>Chọn được để giao: chưa có phiếu hoặc phiếu gần nhất đã hết hạn, và ghép được NV thực hiện.</summary>
        public bool DuocGiao => DuocGiaoSo == 1;

        /// <summary>Cột số 0/1 đọc từ CSDL (Dapper không đổi NUMBER sang bool).</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public int DuocGiaoSo { get; set; }
    }

    public class GhttThueBaoQueryDto
    {
        public int Thang { get; set; }
        /// <summary>Đơn vị (7 ký tự đầu mã); bỏ trống = toàn bộ phạm vi.</summary>
        public string? MaDv { get; set; }

        // Các bộ lọc dưới chỉ dùng khi xuất Excel (màn hình lọc tại trình duyệt).
        public long? DoiVtId { get; set; }
        public string? MaNv { get; set; }
        public string? Search { get; set; }
        /// <summary>CHUA (chưa giao) | DA (đã giao) | bỏ trống = tất cả.</summary>
        public string? DanhSach { get; set; }
        /// <summary>TAT_CA | DUOC_GIAO | CHUA_GIAO | DA_GIAO | QUA_HAN | CO_KQ</summary>
        public string? TrangThai { get; set; }
    }

    public class GhttGiaoPhieuRequestDto
    {
        public int Thang { get; set; }
        public string? MaDv { get; set; }
        /// <summary>Danh sách khóa "phanvung_id|thuebao_id".</summary>
        public List<string> Khoa { get; set; } = new();
        public string? GhiChu { get; set; }
    }

    public class GhttGiaoPhieuResultDto
    {
        public int ThanhCong { get; set; }
        public int BoQua { get; set; }
        public DateTime HanXl { get; set; }
        public List<string> GhiChu { get; set; } = new();
    }

    /// <summary>Một phiếu (màn Phiếu GHTT).</summary>
    public class GhttPhieuDto
    {
        public long Id { get; set; }
        public int SlThang { get; set; }
        public string? MaTb { get; set; }
        public string? TenTb { get; set; }
        public string? DiaChi { get; set; }
        public string? SoDt { get; set; }
        public string? TenDv { get; set; }
        public string? MaDv { get; set; }
        public long? DoiVtId { get; set; }
        public string? TenDoiVt { get; set; }
        public DateTime? NgayKtdc { get; set; }
        public string? TenKm { get; set; }
        public string? MaNvNhan { get; set; }
        public string? TenNvNhan { get; set; }
        public string? NguoiGiao { get; set; }
        public string? TenNguoiGiao { get; set; }
        public DateTime NgayGiao { get; set; }
        public DateTime? HanXl { get; set; }
        public string? GhiChuGiao { get; set; }
        public string TrangThai { get; set; } = GhttPhieuTinhTrang.DA_GIAO;
        public string? LyDo { get; set; }
        public string? GhiChuKq { get; set; }
        public DateTime? NgayHen { get; set; }
        public DateTime? NgayKq { get; set; }
        public string? NguoiKq { get; set; }
        /// <summary>DA_GIAO | QUA_HAN | KHONG_TC | THANH_CONG</summary>
        public string TinhTrang { get; set; } = GhttPhieuTinhTrang.DA_GIAO;
    }

    public class GhttPhieuQueryDto
    {
        /// <summary>CAN (đang mở, gồm quá hạn) | QUA_HAN | XONG (có kết quả trong 60 ngày)</summary>
        public string? Tab { get; set; }
        public string? MaDv { get; set; }
        public long? DoiVtId { get; set; }
        public string? MaNv { get; set; }
    }

    public class GhttPhieuDemDto
    {
        public int Can { get; set; }
        public int QuaHan { get; set; }
        public int Xong { get; set; }
    }

    public class GhttPhieuListDto
    {
        public List<GhttPhieuDto> Items { get; set; } = new();
        public GhttPhieuDemDto Dem { get; set; } = new();
    }

    /// <summary>Giá trị cho các ô lọc (đơn vị / tổ / NV nhận) từ phiếu trong phạm vi.</summary>
    public class GhttPhieuBoLocDto
    {
        public string? MaDv7 { get; set; }
        public string? TenDv { get; set; }
        public long? DoiVtId { get; set; }
        public string? TenDoiVt { get; set; }
        public string? MaNvNhan { get; set; }
        public string? TenNvNhan { get; set; }
    }

    public class GhttKetQuaRequestDto
    {
        public bool ThanhCong { get; set; }
        /// <summary>Bắt buộc khi không thành công (tối đa 300 ký tự).</summary>
        public string? LyDo { get; set; }
        public string? GhiChu { get; set; }
        /// <summary>Ngày KH hẹn lại (yyyy-MM-dd), chỉ khi không thành công.</summary>
        public string? NgayHen { get; set; }
    }
}
