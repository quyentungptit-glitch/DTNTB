using Microsoft.AspNetCore.Http;
using System.IO;
using System.Threading.Tasks;

namespace DTNTB.Core.Interfaces
{
    public interface IFileStorageService
    {
        /// <summary>
        /// Server 2 tự quyết định đường dẫn đích (targetRelativePath) và gửi sang Server 1 lưu
        /// </summary>
        Task<bool> SaveFileAsync(string targetRelativePath, IFormFile file);

        /// <summary>
        /// Server 2 lấy stream file từ Server 1 theo đường dẫn
        /// </summary>
        Task<(Stream Stream, string ContentType)?> GetFileStreamAsync(string relativePath);

        /// <summary>
        /// Server 2 bảo Server 1 xóa file theo đường dẫn
        /// </summary>
        Task<bool> DeleteFileAsync(string relativePath);
    }
}