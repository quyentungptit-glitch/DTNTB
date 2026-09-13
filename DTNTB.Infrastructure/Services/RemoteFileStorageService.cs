using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using DTNTB.Core.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace DTNTB.Infrastructure.Services
{
    public class RemoteFileStorageService : IFileStorageService
    {
        private readonly HttpClient _httpClient;
        private readonly string _server1Url;
        private readonly string _apiKey;

        public RemoteFileStorageService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;

            _server1Url = (configuration["RemoteStorage:BaseUrl"] ?? "").TrimEnd('/');
            _apiKey = configuration["RemoteStorage:InternalApiKey"] ?? "";
            var allowInsecureHttpForPrivateNetwork = configuration.GetValue<bool>("RemoteStorage:AllowInsecureHttpForPrivateNetwork");

            // FIX #3: fail-fast nếu thiếu cấu hình bắt buộc, thay vì âm thầm gọi API với giá trị rỗng
            if (string.IsNullOrWhiteSpace(_server1Url))
                throw new InvalidOperationException("RemoteStorage:BaseUrl chưa được cấu hình.");

            if (!Uri.TryCreate(_server1Url, UriKind.Absolute, out var storageUri)
                || !IsAllowedStorageUri(storageUri, allowInsecureHttpForPrivateNetwork))
            {
                throw new InvalidOperationException(
                    "RemoteStorage:BaseUrl phải dùng HTTPS. HTTP chỉ được phép cho localhost hoặc IP private " +
                    "khi RemoteStorage:AllowInsecureHttpForPrivateNetwork=true.");
            }

            if (string.IsNullOrWhiteSpace(_apiKey))
                throw new InvalidOperationException("RemoteStorage:InternalApiKey chưa được cấu hình.");
        }

        private static bool IsAllowedStorageUri(Uri storageUri, bool allowInsecureHttpForPrivateNetwork)
        {
            if (storageUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!storageUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
                return false;

            if (storageUri.IsLoopback)
                return true;

            return allowInsecureHttpForPrivateNetwork
                && IPAddress.TryParse(storageUri.Host, out var address)
                && IsPrivateIpv4Address(address);
        }

        private static bool IsPrivateIpv4Address(IPAddress address)
        {
            if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                return false;

            var octets = address.GetAddressBytes();
            return octets[0] == 10
                || (octets[0] == 172 && octets[1] is >= 16 and <= 31)
                || (octets[0] == 192 && octets[1] == 168);
        }

        public async Task<bool> SaveFileAsync(string targetRelativePath, IFormFile file, CancellationToken ct = default)
        {
            if (file == null || file.Length == 0) return false;

            using var content = new MultipartFormDataContent();
            await using var stream = file.OpenReadStream();
            using var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType ?? "application/octet-stream");

            content.Add(fileContent, "file", file.FileName);
            content.Add(new StringContent(targetRelativePath), "targetPath");

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_server1Url}/api/storage/save");
            request.Headers.Add("X-Internal-Key", _apiKey);
            request.Content = content;

            var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                throw new Exception($"Lỗi từ Server 1 ({_server1Url}): Mã {response.StatusCode} - Chi tiết: {errorBody}");
            }

            return true;
        }

        // FIX #1: gói stream + response vào 1 object dùng chung, dispose response
        // khi và chỉ khi stream được dispose, tránh leak connection.
        private sealed class ResponseBackedStream : Stream
        {
            private readonly HttpResponseMessage _response;
            private readonly Stream _inner;

            public ResponseBackedStream(HttpResponseMessage response, Stream inner)
            {
                _response = response;
                _inner = inner;
            }

            public override bool CanRead => _inner.CanRead;
            public override bool CanSeek => _inner.CanSeek;
            public override bool CanWrite => _inner.CanWrite;
            public override long Length => _inner.Length;
            public override long Position { get => _inner.Position; set => _inner.Position = value; }
            public override void Flush() => _inner.Flush();
            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
                => _inner.ReadAsync(buffer, offset, count, cancellationToken);
            public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
            public override void SetLength(long value) => _inner.SetLength(value);
            public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _inner.Dispose();
                    _response.Dispose(); // giải phóng connection khi stream đóng
                }
                base.Dispose(disposing);
            }
        }

        public async Task<(Stream Stream, string ContentType)?> GetFileStreamAsync(string relativePath, CancellationToken ct = default)
        {
            var url = $"{_server1Url}/api/storage/read?filePath={Uri.EscapeDataString(relativePath)}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Internal-Key", _apiKey);

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                response.Dispose();
                return null; // File không tồn tại - trường hợp hợp lệ, không phải lỗi hệ thống
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                var statusCode = response.StatusCode;
                response.Dispose();
                throw new Exception($"Lỗi khi đọc file từ Server 1 ({_server1Url}): Mã {statusCode} - Chi tiết: {errorBody}");
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            var innerStream = await response.Content.ReadAsStreamAsync(ct);

            return (new ResponseBackedStream(response, innerStream), contentType);
        }

        public async Task<bool> DeleteFileAsync(string relativePath, CancellationToken ct = default)
        {
            var url = $"{_server1Url}/api/storage/delete?filePath={Uri.EscapeDataString(relativePath)}";
            using var request = new HttpRequestMessage(HttpMethod.Delete, url);
            request.Headers.Add("X-Internal-Key", _apiKey);

            var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                // FIX #2: log/ném lỗi thay vì chỉ trả false, để biết vì sao xóa thất bại
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                throw new Exception($"Lỗi khi xóa file trên Server 1 ({_server1Url}): Mã {response.StatusCode} - Chi tiết: {errorBody}");
            }

            return true;
        }
    }
}
