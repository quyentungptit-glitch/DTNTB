using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
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
        }

        public async Task<bool> SaveFileAsync(string targetRelativePath, IFormFile file)
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

            var response = await _httpClient.SendAsync(request);

            // 👉 NẾU SERVER 1 TỪ CHỐI, IN/NÉM LỖI RÕ RÀNG ĐỂ BIẾT:
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"Lỗi từ Server 1 ({_server1Url}): Mã {response.StatusCode} - Chi tiết: {errorBody}");
            }

            return true;
        }

        public async Task<(Stream Stream, string ContentType)?> GetFileStreamAsync(string relativePath)
        {
            var url = $"{_server1Url}/api/storage/read?filePath={Uri.EscapeDataString(relativePath)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Internal-Key", _apiKey);

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return null;

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            var stream = await response.Content.ReadAsStreamAsync();
            return (stream, contentType);
        }

        public async Task<bool> DeleteFileAsync(string relativePath)
        {
            var url = $"{_server1Url}/api/storage/delete?filePath={Uri.EscapeDataString(relativePath)}";
            using var request = new HttpRequestMessage(HttpMethod.Delete, url);
            request.Headers.Add("X-Internal-Key", _apiKey);

            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
    }
}