namespace DTNTB.Core.Interfaces
{
    /// <summary>
    /// Đổi toạ độ GPS thành địa chỉ dễ đọc (ví dụ "Thôn 3, xã Gia Sinh, Gia Viễn, Ninh Bình").
    /// Trả null khi chưa cấu hình dịch vụ hoặc dịch vụ không phản hồi; không bao giờ làm hỏng luồng điểm danh.
    /// </summary>
    public interface IReverseGeocodingService
    {
        Task<string?> GetAddressAsync(double latitude, double longitude, CancellationToken ct = default);
    }
}
