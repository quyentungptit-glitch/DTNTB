using DTNTB.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace DTNTB.Infrastructure.Services
{
    /// <summary>
    /// Lưu file phiếu công việc (đính kèm, ảnh minh chứng) vào MinIO, bucket riêng (mặc định "ttht-congviec",
    /// đổi bằng Minio:BucketCv). Dùng chung Endpoint / AccessKey / SecretKey / UseSSL với ảnh điểm danh.
    /// Bucket chưa có thì tự tạo ở lần ghi đầu (tài khoản MinIO không có quyền tạo thì tạo tay một lần).
    /// </summary>
    public sealed class MinioCvFileStore : ICvFileStore
    {
        private readonly IMinioClient _client;
        private readonly string _bucket;
        private readonly ILogger<MinioCvFileStore> _logger;
        private readonly SemaphoreSlim _bucketLock = new(1, 1);
        private volatile bool _bucketOk;

        public MinioCvFileStore(IConfiguration config, ILogger<MinioCvFileStore> logger)
        {
            _logger = logger;
            var endpoint = config["Minio:Endpoint"];
            var accessKey = config["Minio:AccessKey"];
            var secretKey = config["Minio:SecretKey"];
            _bucket = string.IsNullOrWhiteSpace(config["Minio:BucketCv"]) ? "ttht-congviec" : config["Minio:BucketCv"]!.Trim();

            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey))
                throw new InvalidOperationException("Minio:Endpoint, Minio:AccessKey và Minio:SecretKey chưa được cấu hình.");

            _client = new MinioClient()
                .WithEndpoint(endpoint.Trim())
                .WithCredentials(accessKey.Trim(), secretKey)
                .WithSSL(config.GetValue<bool>("Minio:UseSSL"))
                .Build();
        }

        private async Task DamBaoBucketAsync(CancellationToken ct)
        {
            if (_bucketOk) return;
            await _bucketLock.WaitAsync(ct);
            try
            {
                if (_bucketOk) return;
                var coSan = await _client.BucketExistsAsync(new BucketExistsArgs().WithBucket(_bucket), ct);
                if (!coSan)
                {
                    await _client.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucket), ct);
                    _logger.LogInformation("Đã tạo bucket MinIO {Bucket} cho phiếu công việc", _bucket);
                }
                _bucketOk = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Không kiểm tra / tạo được (thiếu quyền) thì vẫn thử ghi: bucket có thể đã được tạo tay.
                _logger.LogWarning(ex, "Không kiểm tra / tạo được bucket MinIO {Bucket}", _bucket);
                _bucketOk = true;
            }
            finally
            {
                _bucketLock.Release();
            }
        }

        public async Task SaveAsync(string objectKey, byte[] content, string contentType, CancellationToken ct = default)
        {
            await DamBaoBucketAsync(ct);
            using var stream = new MemoryStream(content, writable: false);
            await _client.PutObjectAsync(new PutObjectArgs()
                .WithBucket(_bucket)
                .WithObject(objectKey)
                .WithStreamData(stream)
                .WithObjectSize(content.Length)
                .WithContentType(contentType), ct);
        }

        public async Task<Stream?> GetAsync(string objectKey, CancellationToken ct = default)
        {
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
            catch (BucketNotFoundException)
            {
                buffer.Dispose();
                return null;
            }
            buffer.Position = 0;
            return buffer;
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
        }
    }
}
