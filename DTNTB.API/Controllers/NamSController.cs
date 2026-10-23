using DTNTB.Core.Constants;
using DTNTB.Core.DTOs;
using DTNTB.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DTNTB.API.Controllers
{
    /// <summary>
    /// 5S nhà trạm.
    ///   NAMS.GIAO      : giao trạm cho nhân viên, thu hồi phiếu chưa nộp
    ///   NAMS.THUC_HIEN : nhân viên tải ảnh, nộp phiếu
    ///   NAMS.CHAM      : chấm điểm phiếu đã nộp
    ///   NAMS.VIEW      : danh sách phiếu, tổng hợp; NAMS.EXPORT: Excel
    /// Phạm vi dữ liệu theo data_scope trong JWT.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/5s")]
    public class NamSController : ControllerBase
    {
        private readonly INamSService _service;
        private readonly ICurrentUserService _currentUser;
        private readonly ILogger<NamSController> _logger;

        public NamSController(INamSService service, ICurrentUserService currentUser, ILogger<NamSController> logger)
        {
            _service = service;
            _currentUser = currentUser;
            _logger = logger;
        }

        private bool Co(params string[] perms) => perms.Any(_currentUser.HasPermission);

        private bool CoQuyen5S => Co(AppPermissions.NAMS.GIAO, AppPermissions.NAMS.THUC_HIEN, AppPermissions.NAMS.CHAM,
                                     AppPermissions.NAMS.VIEW, AppPermissions.NAMS.EXPORT);

        private bool CoQuanLy => Co(AppPermissions.NAMS.GIAO, AppPermissions.NAMS.CHAM, AppPermissions.NAMS.VIEW, AppPermissions.NAMS.EXPORT);

        // ============================================================
        // DANH MỤC
        // ============================================================

        [HttpGet("tieu-chi")]
        public async Task<IActionResult> GetTieuChi()
        {
            if (!CoQuyen5S) return Forbid();
            return await Run(() => _service.GetTieuChiAsync());
        }

        [HttpGet("xep-loai")]
        public IActionResult GetXepLoai() => CoQuyen5S ? Ok(_service.GetXepLoai()) : Forbid();

        [HttpGet("don-vi")]
        public async Task<IActionResult> GetDonVi()
        {
            if (!CoQuanLy) return Forbid();
            return await Run(() => _service.GetDonViAsync());
        }

        [HttpGet("to")]
        public async Task<IActionResult> GetTo([FromQuery] string? maDv)
        {
            if (!CoQuanLy) return Forbid();
            return await Run(() => _service.GetToAsync(maDv));
        }

        // ============================================================
        // GIAO TRẠM
        // ============================================================

        [HttpGet("tram")]
        [Authorize(Policy = AppPermissions.NAMS.GIAO)]
        public async Task<IActionResult> GetTram([FromQuery] NamSTramQueryDto query) => await Run(() => _service.GetTramAsync(query));

        [HttpGet("nhan-vien")]
        [Authorize(Policy = AppPermissions.NAMS.GIAO)]
        public async Task<IActionResult> GetNhanVien([FromQuery] string? maDv, [FromQuery] string? maToQl)
            => await Run(() => _service.GetNhanVienAsync(maDv, maToQl));

        [HttpGet("nv/don-vi")]
        public async Task<IActionResult> GetNvDonVi()
        {
            if (!CoQuanLy) return Forbid();
            return await Run(() => _service.GetNvDonViAsync());
        }

        [HttpGet("nv/to")]
        public async Task<IActionResult> GetNvTo([FromQuery] string? maDv)
        {
            if (!CoQuanLy) return Forbid();
            return await Run(() => _service.GetNvToAsync(maDv));
        }

        [HttpPost("giao")]
        [Authorize(Policy = AppPermissions.NAMS.GIAO)]
        public async Task<IActionResult> Giao([FromBody] NamSGiaoRequestDto request)
            => await Run(() => _service.GiaoAsync(request ?? new NamSGiaoRequestDto()));

        [HttpGet("dang-giao")]
        [Authorize(Policy = AppPermissions.NAMS.GIAO)]
        public async Task<IActionResult> GetDangGiao([FromQuery] NamSTramQueryDto query) => await Run(() => _service.GetPhieuDangGiaoAsync(query));

        [HttpPost("phieu/{id:long}/thu-hoi")]
        [Authorize(Policy = AppPermissions.NAMS.GIAO)]
        public async Task<IActionResult> ThuHoi(long id, CancellationToken ct)
        {
            try
            {
                return await _service.ThuHoiAsync(id, ct)
                    ? NoContent()
                    : NotFound(new { message = "Không tìm thấy phiếu đang giao trong phạm vi của bạn (có thể nhân viên đã nộp)." });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Fail(ex, "Chưa thu hồi được phiếu.");
            }
        }

        // ============================================================
        // NHÂN VIÊN THỰC HIỆN
        // ============================================================

        [HttpGet("cua-toi")]
        [Authorize(Policy = AppPermissions.NAMS.THUC_HIEN)]
        public async Task<IActionResult> GetCuaToi() => await Run(() => _service.GetCuaToiAsync());

        /// <summary>Trạm tự tạo phiếu được: theo từ khoá hoặc gần vị trí (lat/lng) nhất, tối đa 30 trạm.</summary>
        [HttpGet("tu-tao/tram")]
        [Authorize(Policy = AppPermissions.NAMS.THUC_HIEN)]
        public async Task<IActionResult> TimTramTuTao([FromQuery] string? search, [FromQuery] double? lat, [FromQuery] double? lng)
            => await Run(() => _service.TimTramTuTaoAsync(search, lat, lng));

        /// <summary>Nhân viên tự tạo phiếu 5S cho mình ở trạm không được giao.</summary>
        [HttpPost("tu-tao")]
        [Authorize(Policy = AppPermissions.NAMS.THUC_HIEN)]
        public async Task<IActionResult> TuTao([FromBody] NamSTuTaoRequestDto request)
            => await Run(() => _service.TuTaoAsync(request ?? new NamSTuTaoRequestDto()));

        /// <summary>Xoá phiếu tự tạo chưa nộp của chính mình.</summary>
        [HttpDelete("phieu/{id:long}")]
        [Authorize(Policy = AppPermissions.NAMS.THUC_HIEN)]
        public async Task<IActionResult> XoaPhieuTuTao(long id, CancellationToken ct)
        {
            try
            {
                return await _service.XoaPhieuTuTaoAsync(id, ct)
                    ? NoContent()
                    : NotFound(new { message = "Chỉ xoá được phiếu tự tạo của bạn và chưa nộp." });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Fail(ex, "Chưa xoá được phiếu.");
            }
        }

        [HttpGet("phieu/{id:long}")]
        public async Task<IActionResult> GetPhieu(long id)
        {
            if (!CoQuyen5S) return Forbid();
            try
            {
                var item = await _service.GetPhieuAsync(id);
                return item != null ? Ok(item) : NotFound(new { message = "Không tìm thấy phiếu hoặc phiếu ngoài phạm vi của bạn." });
            }
            catch (Exception ex)
            {
                return Fail(ex, "Chưa tải được phiếu.");
            }
        }

        /// <summary>Tải ảnh (multipart/form-data): tieuChiId, anh (một hoặc nhiều), meta (JSON mảng {lat,lng,nguon,chupLuc,fileName} theo thứ tự ảnh).</summary>
        [HttpPost("phieu/{id:long}/anh")]
        [Authorize(Policy = AppPermissions.NAMS.THUC_HIEN)]
        public async Task<IActionResult> UploadAnh(long id, [FromForm] int tieuChiId, [FromForm] List<IFormFile>? anh, [FromForm] string? meta, CancellationToken ct)
        {
            try
            {
                return Ok(await _service.UploadAnhAsync(id, tieuChiId, anh ?? new List<IFormFile>(), meta, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Fail(ex, "Chưa lưu được ảnh.");
            }
        }

        [HttpDelete("anh/{anhId:long}")]
        [Authorize(Policy = AppPermissions.NAMS.THUC_HIEN)]
        public async Task<IActionResult> XoaAnh(long anhId, CancellationToken ct)
        {
            try
            {
                return await _service.XoaAnhAsync(anhId, ct)
                    ? NoContent()
                    : NotFound(new { message = "Không tìm thấy ảnh hoặc phiếu đã nộp." });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Fail(ex, "Chưa xóa được ảnh.");
            }
        }

        [HttpPost("phieu/{id:long}/nop")]
        [Authorize(Policy = AppPermissions.NAMS.THUC_HIEN)]
        public async Task<IActionResult> Nop(long id, [FromBody] NamSNopRequestDto? request)
        {
            try
            {
                return await _service.NopAsync(id, request ?? new NamSNopRequestDto())
                    ? NoContent()
                    : NotFound(new { message = "Không tìm thấy phiếu giao cho bạn." });
            }
            catch (Exception ex)
            {
                return Fail(ex, "Chưa nộp được phiếu.");
            }
        }

        /// <summary>Ảnh 5S: người được giao xem ảnh của mình; người khác cần quyền GIAO/CHAM/VIEW và phiếu trong phạm vi.</summary>
        [HttpGet("anh/{anhId:long}")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> GetAnh(long anhId, CancellationToken ct)
        {
            if (!CoQuyen5S) return Forbid();
            try
            {
                var file = await _service.GetAnhAsync(anhId, ct);
                if (file == null) return NotFound();
                Response.Headers.Append("X-Content-Type-Options", "nosniff");
                return File(file.Value.Stream, file.Value.ContentType);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Không đọc được ảnh 5S {Id}", anhId);
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { message = "Máy chủ lưu ảnh hiện không phản hồi. Vui lòng thử lại sau." });
            }
        }

        // ============================================================
        // CHẤM ĐIỂM / BÁO CÁO
        // ============================================================

        [HttpGet("phieu")]
        public async Task<IActionResult> GetPhieuList([FromQuery] NamSPhieuQueryDto query)
        {
            if (!Co(AppPermissions.NAMS.CHAM, AppPermissions.NAMS.VIEW)) return Forbid();
            return await Run(() => _service.GetPhieuListAsync(query));
        }

        [HttpPost("phieu/{id:long}/cham")]
        [Authorize(Policy = AppPermissions.NAMS.CHAM)]
        public async Task<IActionResult> Cham(long id, [FromBody] NamSChamRequestDto request)
        {
            try
            {
                var item = await _service.ChamAsync(id, request ?? new NamSChamRequestDto());
                return item != null ? Ok(item) : NotFound(new { message = "Không tìm thấy phiếu trong phạm vi của bạn." });
            }
            catch (Exception ex)
            {
                return Fail(ex, "Chưa lưu được điểm.");
            }
        }

        [HttpGet("tong-hop")]
        public async Task<IActionResult> GetTongHop([FromQuery] NamSPhieuQueryDto query)
        {
            if (!Co(AppPermissions.NAMS.VIEW, AppPermissions.NAMS.EXPORT)) return Forbid();
            return await Run(() => _service.GetTongHopAsync(query));
        }

        [HttpGet("tong-hop-nv")]
        public async Task<IActionResult> GetTongHopNhanVien([FromQuery] NamSPhieuQueryDto query)
        {
            if (!Co(AppPermissions.NAMS.VIEW, AppPermissions.NAMS.EXPORT)) return Forbid();
            return await Run(() => _service.GetTongHopNhanVienAsync(query));
        }

        [HttpGet("export")]
        [Authorize(Policy = AppPermissions.NAMS.EXPORT)]
        public async Task<IActionResult> Export([FromQuery] NamSPhieuQueryDto query)
        {
            try
            {
                var bytes = await _service.ExportAsync(query);
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"BaoCao5S_{DateTime.Now:ddMMyyyy_HHmm}.xlsx");
            }
            catch (Exception ex)
            {
                return Fail(ex, "Máy chủ chưa thể tạo file Excel.");
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
            catch (Exception ex)
            {
                return Fail(ex, "Máy chủ gặp lỗi khi xử lý.");
            }
        }

        private IActionResult Fail(Exception ex, string message)
        {
            if (ex is NamSValidationException) return BadRequest(new { message = ex.Message });
            _logger.LogError(ex, "Lỗi API 5S: {Message}. TraceId {TraceId}", message, HttpContext.TraceIdentifier);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = $"{message} Mã tra cứu: {HttpContext.TraceIdentifier}" });
        }
    }
}
