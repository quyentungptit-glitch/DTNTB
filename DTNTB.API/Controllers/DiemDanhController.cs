using System.Globalization;
using DTNTB.Core.Constants;
using DTNTB.Core.DTOs;
using DTNTB.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DTNTB.API.Controllers
{
    /// <summary>
    /// Điểm danh hiện trường.
    ///   Nhân viên (CHECKIN): lấy danh mục việc, đổi toạ độ ra địa chỉ, gửi điểm danh, xem lượt của mình.
    ///   Quản lý (VIEW/EXPORT): bản đồ trong ngày, danh sách ai đã điểm danh mấy lần, lịch sử, xuất Excel.
    /// Phạm vi dữ liệu luôn theo data_scope trong JWT.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class DiemDanhController : ControllerBase
    {
        private readonly IDiemDanhService _service;
        private readonly ICurrentUserService _currentUser;
        private readonly ILogger<DiemDanhController> _logger;

        public DiemDanhController(IDiemDanhService service, ICurrentUserService currentUser, ILogger<DiemDanhController> logger)
        {
            _service = service;
            _currentUser = currentUser;
            _logger = logger;
        }

        // ============================================================
        // NHÂN VIÊN
        // ============================================================

        /// <summary>Danh sách việc chọn nhanh: sửa sự cố, lắp đặt, bảo dưỡng trạm...</summary>
        [HttpGet("loai-cong-viec")]
        public IActionResult GetLoaiCongViec()
        {
            if (!_currentUser.HasPermission(AppPermissions.DIEMDANH.CHECKIN)
                && !_currentUser.HasPermission(AppPermissions.DIEMDANH.VIEW))
                return Forbid();
            return Ok(_service.GetLoaiCongViec());
        }

        /// <summary>Đổi toạ độ ra địa chỉ để hiện cho nhân viên xem trước khi gửi.</summary>
        [HttpGet("dia-chi")]
        [Authorize(Policy = AppPermissions.DIEMDANH.CHECKIN)]
        public async Task<IActionResult> GetDiaChi([FromQuery] double lat, [FromQuery] double lng, CancellationToken ct)
        {
            try
            {
                return Ok(await _service.GetDiaChiAsync(lat, lng, ct));
            }
            catch (DiemDanhValidationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Gửi điểm danh (multipart/form-data): anh (một hoặc nhiều), latitude, longitude, accuracy, loaiCongViec, ghiChu, clientTime.
        /// </summary>
        [HttpPost("checkin")]
        [Authorize(Policy = AppPermissions.DIEMDANH.CHECKIN)]
        public async Task<IActionResult> CheckIn([FromForm] DiemDanhCheckInFormDto model, CancellationToken ct)
        {
            try
            {
                var result = await _service.CheckInAsync(
                    model,
                    Request.Headers.UserAgent.ToString(),
                    HttpContext.Connection.RemoteIpAddress?.ToString(),
                    ct);
                return Ok(result);
            }
            catch (DiemDanhValidationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return StatusCode(499);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Điểm danh thất bại cho {User}. TraceId {TraceId}", _currentUser.Username, HttpContext.TraceIdentifier);
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    message = $"Chưa lưu được điểm danh, vui lòng thử lại. Mã tra cứu: {HttpContext.TraceIdentifier}"
                });
            }
        }

        /// <summary>Các lượt điểm danh của chính mình trong ngày (mặc định hôm nay).</summary>
        [HttpGet("cua-toi")]
        [Authorize(Policy = AppPermissions.DIEMDANH.CHECKIN)]
        public async Task<IActionResult> GetCuaToi([FromQuery] string? ngay)
        {
            if (!TryParseNgay(ngay, out var date)) return BadRequest(new { message = "Ngày không hợp lệ (yyyy-MM-dd)." });
            return Ok(await _service.GetCuaToiAsync(date));
        }

        // ============================================================
        // QUẢN LÝ
        // ============================================================

        [HttpGet("don-vi")]
        [Authorize(Policy = AppPermissions.DIEMDANH.VIEW)]
        public async Task<IActionResult> GetDonVi() => Ok(await _service.GetDonViAsync());

        /// <summary>Danh sách tổ (toàn bộ ma_dv) của đơn vị đang chọn.</summary>
        [HttpGet("to")]
        [Authorize(Policy = AppPermissions.DIEMDANH.VIEW)]
        public async Task<IActionResult> GetTo([FromQuery] string? maDv)
            => await Run(() => _service.GetToAsync(maDv));

        /// <summary>Mọi lượt điểm danh trong ngày để chấm lên bản đồ.</summary>
        [HttpGet("ban-do")]
        [Authorize(Policy = AppPermissions.DIEMDANH.VIEW)]
        public async Task<IActionResult> GetBanDo([FromQuery] string? ngay, [FromQuery] string? maDv, [FromQuery] string? maTo)
        {
            if (!TryParseNgay(ngay, out var date)) return BadRequest(new { message = "Ngày không hợp lệ (yyyy-MM-dd)." });
            return await Run(() => _service.GetBanDoAsync(date, maDv, maTo));
        }

        /// <summary>Ai đã điểm danh, mấy lần trong ngày, nhóm theo đơn vị → tổ → nhân viên.</summary>
        [HttpGet("tong-hop")]
        [Authorize(Policy = AppPermissions.DIEMDANH.VIEW)]
        public async Task<IActionResult> GetTongHop([FromQuery] string? ngay, [FromQuery] string? maDv, [FromQuery] string? maTo)
        {
            if (!TryParseNgay(ngay, out var date)) return BadRequest(new { message = "Ngày không hợp lệ (yyyy-MM-dd)." });
            return await Run(() => _service.GetTongHopAsync(date, maDv, maTo));
        }

        /// <summary>Lịch sử theo người hoặc theo ngày, có phân trang.</summary>
        [HttpGet("lich-su")]
        [Authorize(Policy = AppPermissions.DIEMDANH.VIEW)]
        public async Task<IActionResult> GetLichSu([FromQuery] DiemDanhLichSuQueryDto query)
            => await Run(() => _service.GetLichSuAsync(query));

        [HttpGet("lich-su/export")]
        [Authorize(Policy = AppPermissions.DIEMDANH.EXPORT)]
        public async Task<IActionResult> ExportLichSu([FromQuery] DiemDanhLichSuQueryDto query)
        {
            // Xuất Excel cần cả quyền xem, để không lộ dữ liệu khi role chỉ được gán EXPORT.
            if (!_currentUser.HasPermission(AppPermissions.DIEMDANH.VIEW)) return Forbid();

            try
            {
                var bytes = await _service.ExportLichSuAsync(query);
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"DiemDanh_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
            }
            catch (DiemDanhValidationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Xuất Excel điểm danh thất bại. TraceId {TraceId}", HttpContext.TraceIdentifier);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = $"Máy chủ chưa thể tạo file Excel. Mã tra cứu: {HttpContext.TraceIdentifier}" });
            }
        }

        /// <summary>Xóa một lượt điểm danh (kèm ảnh) trong phạm vi dữ liệu; cần quyền DELETE và VIEW.</summary>
        [HttpDelete("{id:long}")]
        [Authorize(Policy = AppPermissions.DIEMDANH.DELETE)]
        public async Task<IActionResult> Delete(long id, CancellationToken ct)
        {
            if (!_currentUser.HasPermission(AppPermissions.DIEMDANH.VIEW)) return Forbid();

            try
            {
                return await _service.DeleteAsync(id, ct)
                    ? NoContent()
                    : NotFound(new { message = "Không tìm thấy lượt điểm danh hoặc lượt này ngoài phạm vi quản lý." });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Xóa lượt điểm danh {Id} thất bại. TraceId {TraceId}", id, HttpContext.TraceIdentifier);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = $"Chưa xóa được lượt điểm danh. Mã tra cứu: {HttpContext.TraceIdentifier}" });
            }
        }

        // ============================================================
        // ẢNH
        // ============================================================

        /// <summary>
        /// Ảnh điểm danh. Người điểm danh xem được ảnh của mình; người khác cần quyền VIEW
        /// và lượt điểm danh nằm trong phạm vi dữ liệu.
        /// </summary>
        [HttpGet("{id:long}/anh")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> GetAnh(long id, [FromQuery] int i, CancellationToken ct)
        {
            if (i < 0 || i > 20) return NotFound();

            try
            {
                var file = await _service.GetAnhAsync(id, i, ct);
                if (file == null) return NotFound();

                Response.Headers.Append("X-Content-Type-Options", "nosniff");
                return File(file.Value.Stream, file.Value.ContentType);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Không đọc được ảnh điểm danh {Id}", id);
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { message = "Máy chủ lưu ảnh hiện không phản hồi. Vui lòng thử lại sau." });
            }
        }

        // ============================================================
        // TIỆN ÍCH
        // ============================================================
        private static bool TryParseNgay(string? value, out DateTime date)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                date = DateTime.Today;
                return true;
            }
            return DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        private async Task<IActionResult> Run<T>(Func<Task<T>> action)
        {
            try
            {
                return Ok(await action());
            }
            catch (DiemDanhValidationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
