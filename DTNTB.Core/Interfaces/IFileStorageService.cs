using Microsoft.AspNetCore.Http;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DTNTB.Core.Interfaces
{
    public interface IFileStorageService
    {
        /// <summary>
        /// Server 2 tự quyết định đường dẫn đích (targetRelativePath) và gửi sang Server 1 lưu
        /// </summary>
        Task<bool> SaveFileAsync(string targetRelativePath, IFormFile file, CancellationToken ct = default);

        /// <summary>
        /// Server 2 lấy stream file từ Server 1 theo đường dẫn.
        /// Ném exception nếu Server 1 trả lỗi (401, 404, 500...) thay vì trả null,
        /// trừ trường hợp bạn muốn giữ null cho 404 — xem lưu ý bên dưới.
        /// </summary>
        Task<(Stream Stream, string ContentType)?> GetFileStreamAsync(string relativePath, CancellationToken ct = default);

        /// <summary>
        /// Server 2 bảo Server 1 xóa file theo đường dẫn.
        /// Ném exception nếu Server 1 từ chối hoặc file không tồn tại.
        /// </summary>
        Task<bool> DeleteFileAsync(string relativePath, CancellationToken ct = default);
    }
}