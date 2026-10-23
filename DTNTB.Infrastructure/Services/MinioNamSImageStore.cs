using DTNTB.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace DTNTB.Infrastructure.Services
{
    /// <summary>
    /// Lưu ảnh 5S nhà trạm vào MinIO, bucket riêng (mặc định "ttht-5s", đổi bằng Minio:Bucket5S).
    /// Dùng chung Endpoint / AccessKey / SecretKey / UseSSL với ảnh điểm danh (mục "Minio").
    /// Ảnh 5S cũ (khoá bắt đầu "5s/") từng lưu trong bucket điểm danh vẫn đọc / xoá được ở bucket đó.
    /// </summary>
    public sealed class MinioNamSImageStore : INamSImageStore
    {
        private const string LegacyPrefix = "5s/";
        private readonly IMinioClient _client;
        private readonly string _bucket;
        private readonly string _legacyBucket;
        private readonly ILogger<MinioNamSImageStore> _logger;

        public MinioNamSImageStore(IConfiguration config, ILogger<MinioNamSImageStore> logger)
        {
            _logger = logger;
            var endpoint = config["Minio:Endpoint"];
            var accessKey = config["Minio:AccessKey"];
            var secretKey = config["Minio:SecretKey"];
            _bucket = string.IsNullOrWhiteSpace(config["Minio:Bucket5S"]) ? "ttht-5s" : config["Minio:Bucket5S"]!.Trim();
            _legacyBucket = string.IsNullOrWhiteSpace(config["Minio:Bucket"]) ? "ttht-diemdanh" : config["Minio:Bucket"]!.Trim();

            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey))
                throw new InvalidOperationException("Minio:Endpoint, Minio:AccessKey và Minio:SecretKey chưa được cấu hình.");

            _client = new MinioClient()
                .WithEndpoint(endpoint.Trim())
                .WithCredentials(accessKey.Trim(), secretKey)
                .WithSSL(config.GetValue<bool>("Minio:UseSSL"))
                .Build();
        }

        private string BucketOf(string objectKey) =>
            objectKey.StartsWith(LegacyPrefix, StringComparison.Ordinal) ? _legacyBucket : _bucket;

        public async Task SaveAsync(string objectKey, byte[] content, string contentType, CancellationToken ct = default)
        {
            using var stream = new MemoryStream(content, writable: false);
            await _client.PutObjectAsync(new PutObjectArgs()
                .WithBucket(BucketOf(objectKey))
                .WithObject(objectKey)
                .WithStreamData(stream)
                .WithObjectSize(content.Length)
                .WithContentType(contentType), ct);
        }

        public async Task<(Stream Stream, string ContentType)?> GetAsync(string objectKey, CancellationToken ct = default)
        {
            var buffer = new MemoryStream();
            try
            {
                await _client.GetObjectAsync(new GetObjectArgs()
                    .WithBucket(BucketOf(objectKey))
                    .WithObject(objectKey)
                    .WithCallbackStream(async (stream, token) => await stream.CopyToAsync(buffer, token)), ct);
            }
            catch (ObjectNotFoundException)
            {
                buffer.Dispose();
                return null;
            }

            buffer.Position = 0;
            return (buffer, "image/jpeg");
        }

        public async Task DeleteAsync(string objectKey, CancellationToken ct = default)
        {
            try
            {
                await _client.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(BucketOf(objectKey)).WithObject(objectKey), ct);
            }
            catch (ObjectNotFoundException)
            {
                // Đã không còn thì coi như xoá xong.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không xoá được ảnh 5S {Key} trên MinIO", objectKey);
                throw;
            }
        }
    }
}
