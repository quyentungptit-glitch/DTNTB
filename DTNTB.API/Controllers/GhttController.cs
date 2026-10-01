using DTNTB.Core.Constants;
using DTNTB.Core.DTOs;
using DTNTB.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Threading.Tasks;

namespace DTNTB.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class GhttController : ControllerBase
    {
        private readonly IGhttService _ghttService;

        public GhttController(IGhttService ghttService)
        {
            _ghttService = ghttService;
        }

        private string GetCurrentUserId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value?.Trim()
                ?? User.FindFirst("nameid")?.Value?.Trim()
                ?? User.FindFirst("sub")?.Value?.Trim()
                ?? "SYSTEM";
        }

        // 1. LẤY DANH SÁCH ĐƠN VỊ DROPDOWN
        [HttpGet("don-vi")]
        [Authorize(Policy = AppPermissions.GHTT.VIEW)]
        public async Task<IActionResult> GetDonVi([FromQuery] string loaiDv, [FromQuery] int thang)
        {
            var data = await _ghttService.GetDanhSachDonViAsync(loaiDv, thang);
            return Ok(data);
        }

        // 2. LẤY DỮ LIỆU BÁO CÁO (Lấy số liệu)
        [HttpGet("report")]
        [Authorize(Policy = AppPermissions.GHTT.VIEW)]
        public async Task<IActionResult> GetReport(
            [FromQuery] int thang,
            [FromQuery] int donvi = 0,
            [FromQuery] string loaiDv = "ALL")
        {
            var result = await _ghttService.GetBaoCaoGhttAsync(thang, donvi, loaiDv);
            return Ok(result);
        }

        // 3. XUẤT FILE EXCEL BÁO CÁO (Tải số liệu)
        [HttpGet("export")]
        [Authorize(Policy = AppPermissions.GHTT.EXPORT)]
        public async Task<IActionResult> ExportExcel(
            [FromQuery] int thang,
            [FromQuery] int donvi = 0,
            [FromQuery] string loaiDv = "ALL")
        {
            var fileBytes = await _ghttService.ExportExcelGhttAsync(thang, donvi, loaiDv);
            string fileName = $"BaoCao_GHTT_{thang}.xlsx";
            return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

        // 4. TỔNG HỢP SỐ LIỆU
        [HttpPost("tong-hop")]
        [Authorize(Policy = AppPermissions.GHTT.TONG_HOP)]
        public async Task<IActionResult> TongHopSoLieu([FromBody] GhttActionRequestDto request)
        {
            string nguoiCn = GetCurrentUserId();
            bool success = await _ghttService.TongHopSoLieuAsync(request.Thang, nguoiCn);

            if (!success)
            {
                return BadRequest(new { success = false, message = "Tổng hợp số liệu thất bại." });
            }

            return Ok(new { success = true, message = "Đồng bộ và tổng hợp số liệu thành công." });
        }

        // 5. CHỐT SỐ LIỆU (Yêu cầu quyền AppPermissions.GHTT.CHOT_SO_LIEU)
        [HttpPost("chot-so-lieu")]
        [Authorize(Policy = AppPermissions.GHTT.CHOT_SO_LIEU)]
        public async Task<IActionResult> ChotSoLieu([FromBody] GhttActionRequestDto request)
        {
            string nguoiCn = GetCurrentUserId();
            string message = await _ghttService.ChotSoLieuAsync(request.Thang, nguoiCn);

            bool isSuccess = message.Contains("thành công", System.StringComparison.OrdinalIgnoreCase)
                          || message.Contains("ok", System.StringComparison.OrdinalIgnoreCase);

            if (!isSuccess)
            {
                return BadRequest(new { success = false, message });
            }

            return Ok(new { success = true, message });
        }
    }
}