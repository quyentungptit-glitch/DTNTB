namespace DTNTB.Core.Interfaces
{
    /// <summary>Kho ảnh 5S nhà trạm (MinIO, bucket riêng "ttht-5s").</summary>
    public interface INamSImageStore
    {
        Task SaveAsync(string objectKey, byte[] content, string contentType, CancellationToken ct = default);

        /// <summary>Trả null khi ảnh không tồn tại.</summary>
        Task<(Stream Stream, string ContentType)?> GetAsync(string objectKey, CancellationToken ct = default);

        Task DeleteAsync(string objectKey, CancellationToken ct = default);
    }
}
