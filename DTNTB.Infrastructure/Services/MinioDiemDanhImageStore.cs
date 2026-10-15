using DTNTB.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace DTNTB.Infrastructure.Services
{
    /// <summary>
    /// Lưu ảnh điểm danh vào MinIO. Cấu hình mục "Minio" trong appsettings:
    ///   Endpoint  : host:port của MinIO S3 API (cổng API, mặc định 9000 — không phải cổng console 9001)
    ///   AccessKey / SecretKey : khoá truy cập (nên tạo access key riêng, chỉ có quyền trên bucket này)
    ///   UseSSL    : true nếu MinIO chạy HTTPS
    ///   Bucket    : tên bucket, mặc định "ttht-diemdanh"
    /// </summary>
    public sealed class MinioDiemDanhImageStore : IDiemDanhImageStore
    {
        private readonly IMinioClient _client;
        private readonly string _bucket;
        private readonly ILogger<MinioDiemDanhImageStore> _logger;

        public MinioDiemDanhImageStore(IConfiguration config, ILogger<MinioDiemDanhImageStore> logger)
        {
            _logger = logger;
            var endpoint = config["Minio:Endpoint"];
            var accessKey = config["Minio:AccessKey"];
            var secretKey = config["Minio:SecretKey"];
            _bucket = string.IsNullOrWhiteSpace(config["Minio:Bucket"]) ? "ttht-diemdanh" : config["Minio:Bucket"]!.Trim();

            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey))
                throw new InvalidOperationException("Minio:Endpoint, Minio:AccessKey và Minio:SecretKey chưa được cấu hình.");

            _client = new MinioClient()
                .WithEndpoint(endpoint.Trim())
                .WithCredentials(accessKey.Trim(), secretKey)
                .WithSSL(config.GetValue<bool>("Minio:UseSSL"))
                .Build();
        }

        public async Task SaveAsync(string objectKey, byte[] content, string contentType, CancellationToken ct = default)
        {
            using var stream = new MemoryStream(content, writable: false);
            await _client.PutObjectAsync(new PutObjectArgs()
                .WithBucket(_bucket)
                .WithObject(objectKey)
                .WithStreamData(stream)
                .WithObjectSize(content.Length)
                .WithContentType(contentType), ct);
        }

        public async Task<(Stream Stream, string ContentType)?> GetAsync(string objectKey, CancellationToken ct = default)
        {
            // Ảnh điểm danh nhỏ (vài trăm KB) nên đọc hết vào bộ nhớ rồi trả về.
            var buffer = new MemoryStream();
            try
            {
                await _client.GetObjectAsync(new GetObjectArgs()
                    .WithBucket(_bucket)
                    .WithObject(objectKey)
                    .WithCallbackStream(async (stream, token) => await stream.CopyToAsync(buffer, token)), ct);
            }
            catch (ObjectNotFoundException)
            {
                buffer.Dispose();
                return null;
            }

            buffer.Position = 0;
            var contentType = objectKey.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
            return (buffer, contentType);
        }

        public async Task DeleteAsync(string objectKey, CancellationToken ct = default)
        {
            try
            {
                await _client.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(_bucket).WithObject(objectKey), ct);
            }
            catch (ObjectNotFoundException)
            {
                // Đã không còn thì coi như xoá xong.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không xoá được ảnh điểm danh {Key} trên MinIO", objectKey);
                throw;
            }
        }
    }
}
