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

        // 1. LẤY BÁO CÁO (Lấy số liệu)
        [HttpGet("report")]
        [Authorize(Policy = AppPermissions.GHTT.VIEW)]
        public async Task<IActionResult> GetReport(
            [FromQuery] int thang,
            [FromQuery] int donvi = 0,
            [FromQuery] string loaiDv = "ALL")
        {
            string dataScope = User.FindFirst("data_scope")?.Value?.ToUpper().Trim() ?? "NHAN_VIEN";
            string username = GetCurrentUserId();

            // Nếu cấp 4 (TO_QL) hoặc cấp 5 (NHAN_VIEN): Bắt buộc phạm vi xem là NVDB
            if (dataScope == "TO_QL" || dataScope == "NHAN_VIEN")
            {
                loaiDv = "NVDB";
            }

            var result = await _ghttService.GetBaoCaoGhttAsync(thang, donvi, loaiDv, dataScope, username);
            return Ok(result);
        }

        // 2. LẤY DANH SÁCH ĐƠN VỊ DROPDOWN
        [HttpGet("don-vi")]
        [Authorize(Policy = AppPermissions.GHTT.VIEW)]
        public async Task<IActionResult> GetDonVi([FromQuery] string loaiDv, [FromQuery] int thang)
        {
            string dataScope = User.FindFirst("data_scope")?.Value?.ToUpper().Trim() ?? "NHAN_VIEN";
            string username = GetCurrentUserId();

            if (dataScope == "TO_QL" || dataScope == "NHAN_VIEN")
            {
                loaiDv = "NVDB";
            }

            var data = await _ghttService.GetDanhSachDonViAsync(loaiDv, thang, dataScope, username);
            return Ok(data);
        }

        // 3. XUẤT FILE EXCEL BÁO CÁO
        [HttpGet("export")]
        [Authorize(Policy = AppPermissions.GHTT.EXPORT)]
        public async Task<IActionResult> ExportExcel(
            [FromQuery] int thang,
            [FromQuery] int donvi = 0,
            [FromQuery] string loaiDv = "ALL")
        {
            string dataScope = User.FindFirst("data_scope")?.Value?.ToUpper().Trim() ?? "NHAN_VIEN";
            string username = GetCurrentUserId();

            if (dataScope == "TO_QL" || dataScope == "NHAN_VIEN")
            {
                loaiDv = "NVDB";
            }

            var fileBytes = await _ghttService.ExportExcelGhttAsync(thang, donvi, loaiDv, dataScope, username);
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

        // 5. CHỐT SỐ LIỆU
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

        // 6. CHI TIẾT THUÊ BAO CHƯA GIA HẠN TỪ Ô BÁO CÁO
        [HttpGet("chua-gia-han")]
        [Authorize(Policy = AppPermissions.GHTT.VIEW)]
        public async Task<IActionResult> GetDanhSachChuaGiaHan([FromQuery] GhttChuaGiaHanFilterDto filter)
        {
            string dataScope = User.FindFirst("data_scope")?.Value?.ToUpper().Trim() ?? "NHAN_VIEN";
            string username = GetCurrentUserId();
            // Chi tiết được khóa tại service bằng scope và tài khoản đăng nhập;
            // không có trường hợp ALL mở danh sách tổng hợp.
            try
            {
                var result = await _ghttService.GetDanhSachChuaGiaHanAsync(filter, dataScope, username);
                return Ok(result);
            }
            catch (System.UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
            }
        }

        [HttpGet("chua-gia-han/export")]
        [Authorize(Policy = AppPermissions.GHTT.EXPORT)]
        public async Task<IActionResult> ExportDanhSachChuaGiaHan([FromQuery] GhttChuaGiaHanFilterDto filter)
        {
            string dataScope = User.FindFirst("data_scope")?.Value?.ToUpper().Trim() ?? "NHAN_VIEN";
            string username = GetCurrentUserId();
            try
            {
                var fileBytes = await _ghttService.ExportDanhSachChuaGiaHanAsync(filter, dataScope, username);
                var fileName = $"DanhSachChuaGiaHan_{filter.MaCs?.Trim().ToUpperInvariant()}_{filter.Thang}.xlsx";
                return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (System.UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
            }
        }
    }
}
