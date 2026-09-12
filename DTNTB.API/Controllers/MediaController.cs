using System.Threading.Tasks;
using DTNTB.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DTNTB.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MediaController : ControllerBase
    {
        private readonly IFileStorageService _fileStorageService;

        public MediaController(IFileStorageService fileStorageService)
        {
            _fileStorageService = fileStorageService;
        }

        // Angular xem ảnh qua: GET https://server2/api/media/nguycotb/2026/phieu_10/anh1.jpg
        [HttpGet("{**path}")]
        [ResponseCache(Duration = 86400)]
        public async Task<IActionResult> GetMedia(string path)
        {
            // Chuẩn hóa: Nếu chuỗi có chữ "uploads/" hoặc "/uploads/" ở đầu thì cắt bỏ đi
            // để biến thành "MatLuoi/11572_....jpg" gửi sang Server 1
            var cleanPath = path.TrimStart('/');
            if (cleanPath.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
            {
                cleanPath = cleanPath.Substring("uploads/".Length);
            }

            var fileData = await _fileStorageService.GetFileStreamAsync(cleanPath);
            if (fileData == null) return NotFound();

            return File(fileData.Value.Stream, fileData.Value.ContentType);
        }

        // Angular xóa ảnh qua: DELETE https://server2/api/media?path=nguycotb/2026/phieu_10/anh1.jpg
        [HttpDelete]
        public async Task<IActionResult> DeleteMedia([FromQuery] string path)
        {
            var success = await _fileStorageService.DeleteFileAsync(path);
            if (!success) return BadRequest(new { message = "Không thể xóa file từ storage." });

            return Ok(new { success = true, message = "Đã xóa ảnh thành công." });
        }
    }
}