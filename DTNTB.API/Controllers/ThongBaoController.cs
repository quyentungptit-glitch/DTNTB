using System;
using System.Security.Claims;
using System.Threading.Tasks;
using DTNTB.Core.Constants;
using DTNTB.Core.DTOs;
using DTNTB.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DTNTB.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class ThongBaoController : ControllerBase
    {
        private readonly INotificationAdminService _notificationService;

        public ThongBaoController(INotificationAdminService notificationService)
        {
            _notificationService = notificationService;
        }

        [HttpGet("settings")]
        [Authorize(Policy = AppPermissions.THONGBAO.VIEW)]
        public async Task<IActionResult> GetSettings()
        {
            return Ok(await _notificationService.GetSettingsAsync());
        }

        [HttpPut("settings")]
        [Authorize(Policy = AppPermissions.THONGBAO.UPDATE)]
        public async Task<IActionResult> SaveSettings([FromBody] NotificationSettingsDto settings)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var username = User.FindFirst(ClaimTypes.NameIdentifier)?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(username)) return Unauthorized();

            var saved = await _notificationService.SaveSettingsAsync(settings, username);
            if (!saved) return BadRequest(new { message = "Không lưu được cấu hình thông báo." });
            return Ok(new { success = true, message = "Đã lưu cấu hình thông báo." });
        }

        [HttpGet("history")]
        [Authorize(Policy = AppPermissions.THONGBAO.VIEW)]
        public async Task<IActionResult> GetHistory([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            return Ok(await _notificationService.GetHistoryAsync(page, pageSize));
        }

        [HttpPost("send-test")]
        [Authorize(Policy = AppPermissions.THONGBAO.SEND_TEST)]
        public async Task<IActionResult> SendTest([FromBody] SendTestNotificationRequestDto request)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var username = User.FindFirst(ClaimTypes.NameIdentifier)?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(username)) return Unauthorized();

            var result = await _notificationService.SendTestAsync(request, username);
            return result.Success ? Ok(result) : StatusCode(502, result);
        }
    }
}
