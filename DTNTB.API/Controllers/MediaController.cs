using System.Threading.Tasks;
using DTNTB.Core.Constants;
using DTNTB.Core.DTOs;
using DTNTB.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace DTNTB.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    [EnableCors("AllowAngular")]
    public class MediaController : ControllerBase
    {
        private readonly IDtntbService _dtntbService;

        public MediaController(IDtntbService dtntbService)
        {
            _dtntbService = dtntbService;
        }

        // Angular xem ảnh qua: GET https://server2/api/media/nguycotb/2026/phieu_10/anh1.jpg
        [HttpGet("{**path}")]
        [Authorize(Policy = AppPermissions.DTNTB.VIEW)]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> GetMedia(string path, [FromQuery] long phieuId)
        {
            if (phieuId <= 0)
            {
                return BadRequest(new { message = "phieuId là bắt buộc để xem ảnh." });
            }

            var fileData = await _dtntbService.GetImageAsync(phieuId, path);
            if (fileData == null) return NotFound();

            Response.Headers.Append("X-Content-Type-Options", "nosniff");
            return File(fileData.Value.Stream, fileData.Value.ContentType);
        }

        [HttpDelete]
        [Authorize(Policy = AppPermissions.DTNTB.ACTION)]
        public async Task<IActionResult> DeleteMedia([FromQuery] long phieuId, [FromQuery] string path)
        {
            if (phieuId <= 0 || string.IsNullOrWhiteSpace(path))
            {
                return BadRequest(new { message = "phieuId và path là bắt buộc." });
            }

            var success = await _dtntbService.DeleteImageUpgradeAsync(new DeleteImageRequestDto
            {
                PhieuId = phieuId,
                ImageUrl = path
            });

            if (!success)
            {
                return BadRequest(new { message = "Không thể xóa ảnh hoặc bạn không có quyền tác nghiệp phiếu này." });
            }

            return Ok(new { success = true, message = "Đã xóa ảnh thành công." });
        }
    }
}
