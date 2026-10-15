namespace DTNTB.Core.Interfaces
{
    /// <summary>
    /// Kho ảnh điểm danh (MinIO, bucket riêng). Khoá ảnh có dạng "yyyy-MM/ten_file.jpg"
    /// để ảnh được chia thư mục con theo tháng.
    /// </summary>
    public interface IDiemDanhImageStore
    {
        Task SaveAsync(string objectKey, byte[] content, string contentType, CancellationToken ct = default);

        /// <summary>Trả null khi ảnh không tồn tại.</summary>
        Task<(Stream Stream, string ContentType)?> GetAsync(string objectKey, CancellationToken ct = default);

        Task DeleteAsync(string objectKey, CancellationToken ct = default);
    }
}
