using DTNTB.Core.DTOs;

namespace DTNTB.Core.Interfaces
{
    public interface IDiemDanhService
    {
        IReadOnlyList<LoaiCongViecDto> GetLoaiCongViec();

        Task<DiaChiDto> GetDiaChiAsync(double latitude, double longitude, CancellationToken ct = default);

        /// <summary>
        /// Lưu một lượt điểm danh. Ném <see cref="DiemDanhValidationException"/> khi dữ liệu không hợp lệ
        /// (thông điệp hiển thị được cho người dùng).
        /// </summary>
        Task<DiemDanhCheckInResultDto> CheckInAsync(DiemDanhCheckInFormDto model, string? userAgent, string? clientIp, CancellationToken ct = default);

        /// <summary>Các lượt điểm danh của chính người đăng nhập trong một ngày.</summary>
        Task<List<DiemDanhItemDto>> GetCuaToiAsync(DateTime ngay);

        /// <summary>Danh sách đơn vị/tổ được phép lọc theo phạm vi dữ liệu của người xem.</summary>
        Task<List<DonViOptionDto>> GetDonViAsync();

        /// <summary>Danh sách tổ (toàn bộ ma_dv) của một đơn vị, trong phạm vi dữ liệu của người xem.</summary>
        Task<List<ToOptionDto>> GetToAsync(string? maDv);

        /// <summary>Tất cả lượt điểm danh trong ngày (trong phạm vi), để chấm lên bản đồ.</summary>
        Task<List<DiemDanhItemDto>> GetBanDoAsync(DateTime ngay, string? maDv, string? maTo);

        /// <summary>Ai đã điểm danh, mấy lần trong ngày, nhóm theo đơn vị → tổ → nhân viên.</summary>
        Task<List<DiemDanhTongHopDonViDto>> GetTongHopAsync(DateTime ngay, string? maDv, string? maTo);

        Task<PaginatedResultDto<DiemDanhItemDto>> GetLichSuAsync(DiemDanhLichSuQueryDto query);

        Task<byte[]> ExportLichSuAsync(DiemDanhLichSuQueryDto query);

        /// <summary>
        /// Ảnh thứ <paramref name="thuTu"/> (0 = ảnh đầu) của một lượt điểm danh nếu người xem có quyền;
        /// null nếu không thấy/không có quyền.
        /// </summary>
        Task<(Stream Stream, string ContentType)?> GetAnhAsync(long diemDanhId, int thuTu = 0, CancellationToken ct = default);

        /// <summary>
        /// Xóa một lượt điểm danh trong phạm vi dữ liệu của người xóa, kèm ảnh trên MinIO.
        /// Trả false nếu không thấy hoặc ngoài phạm vi.
        /// </summary>
        Task<bool> DeleteAsync(long diemDanhId, CancellationToken ct = default);
    }

    /// <summary>Lỗi nghiệp vụ khi điểm danh; Message hiển thị thẳng cho người dùng.</summary>
    public class DiemDanhValidationException : Exception
    {
        public DiemDanhValidationException(string message) : base(message) { }
    }
}
