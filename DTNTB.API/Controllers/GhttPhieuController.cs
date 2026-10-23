using DTNTB.Core.Constants;
using DTNTB.Core.DTOs;
using DTNTB.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DTNTB.API.Controllers
{
    /// <summary>
    /// Giao phiếu tiếp cận thuê bao GHTT (Biểu 8) và cập nhật kết quả. Dùng chung bảng GHTT_PHIEU với BCBG.
    ///   PHIEU_GIAO : danh sách TB + giao phiếu (EXPORT để xuất Excel)
    ///   PHIEU_XU_LY: danh sách phiếu + cập nhật kết quả (kể cả nhập hộ)
    /// Phạm vi dữ liệu theo data_scope trong JWT.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/ghtt-phieu")]
    public class GhttPhieuController : ControllerBase
    {
        private readonly IGhttPhieuService _service;
        private readonly ICurrentUserService _currentUser;
        private readonly ILogger<GhttPhieuController> _logger;

        public GhttPhieuController(IGhttPhieuService service, ICurrentUserService currentUser, ILogger<GhttPhieuController> logger)
        {
            _service = service;
            _currentUser = currentUser;
            _logger = logger;
        }

        private bool CoQuanPhieu =>
            _currentUser.HasPermission(AppPermissions.GHTT.PHIEU_GIAO) || _currentUser.HasPermission(AppPermissions.GHTT.PHIEU_XU_LY);

        // ============================================================
        // DANH MỤC
        // ============================================================

        [HttpGet("thang")]
        public async Task<IActionResult> GetThang()
        {
            if (!CoQuanPhieu) return Forbid();
            return await Run(() => _service.GetThangAsync());
        }

        [HttpGet("don-vi")]
        [Authorize(Policy = AppPermissions.GHTT.PHIEU_GIAO)]
        public async Task<IActionResult> GetDonVi() => await Run(() => _service.GetDonViAsync());

        [HttpGet("ly-do")]
        [Authorize(Policy = AppPermissions.GHTT.PHIEU_XU_LY)]
        public async Task<IActionResult> GetLyDo() => await Run(() => _service.GetGoiYLyDoAsync());

        // ============================================================
        // GIAO PHIẾU
        // ============================================================

        /// <summary>TB cần tiếp cận trong phạm vi kèm phiếu gần nhất (màn hình tự lọc tổ / NV / trạng thái).</summary>
        [HttpGet("thue-bao")]
        [Authorize(Policy = AppPermissions.GHTT.PHIEU_GIAO)]
        public async Task<IActionResult> GetThueBao([FromQuery] int thang, [FromQuery] string? maDv)
            => await Run(() => _service.GetThueBaoAsync(thang, maDv));

        [HttpPost("giao")]
        [Authorize(Policy = AppPermissions.GHTT.PHIEU_GIAO)]
        public async Task<IActionResult> Giao([FromBody] GhttGiaoPhieuRequestDto request)
            => await Run(() => _service.GiaoPhieuAsync(request ?? new GhttGiaoPhieuRequestDto()));

        [HttpGet("thue-bao/export")]
        [Authorize(Policy = AppPermissions.GHTT.EXPORT)]
        public async Task<IActionResult> ExportThueBao([FromQuery] GhttThueBaoQueryDto query)
        {
            if (!_currentUser.HasPermission(AppPermissions.GHTT.PHIEU_GIAO)) return Forbid();
            try
            {
                var bytes = await _service.ExportThueBaoAsync(query);
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"GHTT_GiaoPhieu_{DateTime.Now:ddMMyyyy_HHmm}.xlsx");
            }
            catch (GhttPhieuValidationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Xuất Excel giao phiếu GHTT thất bại. TraceId {TraceId}", HttpContext.TraceIdentifier);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = $"Máy chủ chưa thể tạo file Excel. Mã tra cứu: {HttpContext.TraceIdentifier}" });
            }
        }

        // ============================================================
        // PHIẾU
        // ============================================================

        [HttpGet("phieu")]
        [Authorize(Policy = AppPermissions.GHTT.PHIEU_XU_LY)]
        public async Task<IActionResult> GetPhieu([FromQuery] GhttPhieuQueryDto query)
            => await Run(() => _service.GetPhieuAsync(query));

        [HttpGet("phieu/bo-loc")]
        [Authorize(Policy = AppPermissions.GHTT.PHIEU_XU_LY)]
        public async Task<IActionResult> GetBoLoc() => await Run(() => _service.GetBoLocPhieuAsync());

        [HttpPost("phieu/{id:long}/ket-qua")]
        [Authorize(Policy = AppPermissions.GHTT.PHIEU_XU_LY)]
        public async Task<IActionResult> CapNhatKetQua(long id, [FromBody] GhttKetQuaRequestDto request)
        {
            try
            {
                var ok = await _service.CapNhatKetQuaAsync(id, request ?? new GhttKetQuaRequestDto());
                return ok
                    ? Ok(new { message = "Đã cập nhật kết quả." })
                    : NotFound(new { message = "Phiếu không còn hiệu lực hoặc ngoài phạm vi của bạn." });
            }
            catch (GhttPhieuValidationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cập nhật kết quả phiếu GHTT {Id} thất bại. TraceId {TraceId}", id, HttpContext.TraceIdentifier);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = $"Chưa lưu được kết quả. Mã tra cứu: {HttpContext.TraceIdentifier}" });
            }
        }

        // ============================================================
        // TIỆN ÍCH
        // ============================================================
        private async Task<IActionResult> Run<T>(Func<Task<T>> action)
        {
            try
            {
                return Ok(await action());
            }
            catch (GhttPhieuValidationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi API giao phiếu GHTT. TraceId {TraceId}", HttpContext.TraceIdentifier);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = $"Máy chủ gặp lỗi khi xử lý. Mã tra cứu: {HttpContext.TraceIdentifier}" });
            }
        }
    }
}
