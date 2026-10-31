using DTNTB.Core.Constants;
using DTNTB.Core.DTOs;
using DTNTB.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DTNTB.API.Controllers
{
    /// <summary>
    /// Quản lý công việc TTHT (phiếu yêu cầu công việc).
    ///   CV.TAO / CV.TAO_TOAN_TINH : tạo phiếu (địa bàn mình / toàn tỉnh + khai báo KTHT)
    ///   (bảng người nhận)          : giao, chuyển tổ, trả lại - service tự kiểm tra
    ///   CV.XU_LY                   : xử lý việc được giao
    ///   CV.DUYET                   : duyệt tính lương
    ///   CV.VIEW / CV.EXPORT        : báo cáo, Excel
    ///   CV.QUAN_TRI                : danh mục đầu việc, người nhận, chuyển tổ mọi phiếu
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/cv")]
    public class CvController : ControllerBase
    {
        private readonly ICvService _service;
        private readonly ICurrentUserService _currentUser;
        private readonly ILogger<CvController> _logger;

        public CvController(ICvService service, ICurrentUserService currentUser, ILogger<CvController> logger)
        {
            _service = service;
            _currentUser = currentUser;
            _logger = logger;
        }

        // ============================================================
        // DANH MỤC
        // ============================================================
        [HttpGet("context")]
        public Task<IActionResult> GetContext() => Run(() => _service.GetContextAsync());

        [HttpGet("dau-viec")]
        public Task<IActionResult> GetDauViec([FromQuery] bool tatCa) => Run(() => _service.GetDauViecAsync(tatCa));

        [HttpPost("dau-viec")]
        [Authorize(Policy = AppPermissions.CV.QUAN_TRI)]
        public Task<IActionResult> LuuDauViec([FromBody] CvDauViecDto dto) => Run(() => _service.LuuDauViecAsync(dto ?? new CvDauViecDto()));

        [HttpGet("nguoi-nhan")]
        public Task<IActionResult> GetNguoiNhan([FromQuery] bool tatCa) => Run(() => _service.GetNguoiNhanAsync(tatCa));

        [HttpPost("nguoi-nhan")]
        [Authorize(Policy = AppPermissions.CV.QUAN_TRI)]
        public Task<IActionResult> LuuNguoiNhan([FromBody] CvNguoiNhanDto dto) => Run(() => _service.LuuNguoiNhanAsync(dto ?? new CvNguoiNhanDto()));

        [HttpGet("nguoi-nhan/tao-phieu")]
        public Task<IActionResult> GetNguoiNhanTaoPhieu([FromQuery] int dauViecId) => Run(() => _service.GetNguoiNhanTaoPhieuAsync(dauViecId));

        // ============================================================
        // PHIẾU
        // ============================================================
        [HttpGet("phieu")]
        public Task<IActionResult> GetPhieuList([FromQuery] CvPhieuQueryDto query) => Run(() => _service.GetPhieuListAsync(query));

        [HttpGet("phieu/{id:long}")]
        public async Task<IActionResult> GetPhieu(long id)
        {
            try
            {
                var ct = await _service.GetPhieuAsync(id);
                return ct == null ? NotFound(new { message = "Không tìm thấy phiếu hoặc bạn không có quyền xem." }) : Ok(ct);
            }
            catch (Exception ex)
            {
                return Fail(ex, "Chưa tải được phiếu.");
            }
        }

        [HttpPost("phieu")]
        public Task<IActionResult> TaoPhieu([FromBody] CvPhieuRequestDto request)
        {
            if (!_currentUser.HasPermission(AppPermissions.CV.TAO) && !_currentUser.HasPermission(AppPermissions.CV.TAO_TOAN_TINH))
                return Task.FromResult<IActionResult>(Forbid());
            return Run(() => _service.TaoPhieuAsync(request ?? new CvPhieuRequestDto()));
        }

        [HttpPut("phieu/{id:long}")]
        public Task<IActionResult> SuaPhieu(long id, [FromBody] CvPhieuRequestDto request) =>
            Run(() => _service.SuaPhieuAsync(id, request ?? new CvPhieuRequestDto()));

        [HttpPost("phieu/{id:long}/huy")]
        public Task<IActionResult> HuyPhieu(long id, [FromBody] CvLyDoRequestDto? request) =>
            Act(() => _service.HuyPhieuAsync(id, request ?? new CvLyDoRequestDto()));

        [HttpPost("phieu/{id:long}/tra")]
        public Task<IActionResult> TraPhieu(long id, [FromBody] CvLyDoRequestDto? request) =>
            Act(() => _service.TraPhieuAsync(id, request ?? new CvLyDoRequestDto()));

        [HttpPost("phieu/{id:long}/chuyen")]
        public Task<IActionResult> ChuyenPhieu(long id, [FromBody] CvChuyenRequestDto? request) =>
            Act(() => _service.ChuyenPhieuAsync(id, request ?? new CvChuyenRequestDto()));

        [HttpGet("phieu/{id:long}/nhan-vien")]
        public Task<IActionResult> GetNhanVienGiao(long id, [FromQuery] string? search) => Run(() => _service.GetNhanVienGiaoAsync(id, search));

        [HttpPost("phieu/{id:long}/giao")]
        public Task<IActionResult> Giao(long id, [FromBody] CvGiaoRequestDto? request) =>
            Act(() => _service.GiaoAsync(id, request ?? new CvGiaoRequestDto()));

        [HttpPost("xu-ly/{xuLyId:long}/huy-giao")]
        public Task<IActionResult> HuyGiao(long xuLyId) => Act(() => _service.HuyGiaoAsync(xuLyId));

        // ============================================================
        // FILE
        // ============================================================
        [HttpPost("phieu/{id:long}/file")]
        public async Task<IActionResult> UploadDinhKem(long id, IFormFile? file, CancellationToken ct)
        {
            try
            {
                if (file == null) return BadRequest(new { message = "Chưa chọn file." });
                return Ok(await _service.UploadDinhKemAsync(id, file, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Fail(ex, "Chưa lưu được file.");
            }
        }

        /// <summary>Ảnh minh chứng (multipart): anh (một hoặc nhiều), meta (JSON mảng {lat,lng,chupLuc,fileName} theo thứ tự ảnh).</summary>
        [HttpPost("xu-ly/{xuLyId:long}/anh")]
        public async Task<IActionResult> UploadAnh(long xuLyId, [FromForm] List<IFormFile>? anh, [FromForm] string? meta, CancellationToken ct)
        {
            try
            {
                return Ok(await _service.UploadAnhAsync(xuLyId, anh ?? new List<IFormFile>(), meta, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Fail(ex, "Chưa lưu được ảnh.");
            }
        }

        [HttpDelete("file/{fileId:long}")]
        public async Task<IActionResult> XoaFile(long fileId, CancellationToken ct)
        {
            try
            {
                await _service.XoaFileAsync(fileId, ct);
                return NoContent();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Fail(ex, "Chưa xoá được file.");
            }
        }

        [HttpGet("file/{fileId:long}")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> GetFile(long fileId, [FromQuery] bool nho, [FromQuery] bool taiVe, CancellationToken ct)
        {
            try
            {
                var f = await _service.GetFileAsync(fileId, nho, ct);
                if (f == null) return NotFound();
                Response.Headers.Append("X-Content-Type-Options", "nosniff");
                // Chỉ ảnh / PDF được mở trực tiếp; Word / Excel luôn tải về.
                var inline = !taiVe && (f.Value.ContentType.StartsWith("image/", StringComparison.Ordinal) || f.Value.ContentType == "application/pdf");
                return inline ? File(f.Value.Stream, f.Value.ContentType) : File(f.Value.Stream, f.Value.ContentType, f.Value.FileName);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Không đọc được file phiếu công việc {Id}", fileId);
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { message = "Máy chủ lưu file hiện không phản hồi. Vui lòng thử lại sau." });
            }
        }

        // ============================================================
        // XỬ LÝ / ĐÓNG
        // ============================================================
        [HttpGet("viec")]
        public Task<IActionResult> GetViec([FromQuery] string? tab) => Run(() => _service.GetViecCuaToiAsync(tab));

        [HttpPost("xu-ly/{xuLyId:long}/yeu-cau-khoa")]
        public Task<IActionResult> YeuCauKhoa(long xuLyId, [FromBody] CvYeuCauKhoaRequestDto? request) =>
            Act(() => _service.YeuCauKhoaAsync(xuLyId, request ?? new CvYeuCauKhoaRequestDto()));

        [HttpPost("xu-ly/{xuLyId:long}/dong")]
        public Task<IActionResult> Dong(long xuLyId) => Act(() => _service.DongAsync(xuLyId));

        [HttpPost("xu-ly/{xuLyId:long}/tra-lai")]
        public Task<IActionResult> TraLai(long xuLyId, [FromBody] CvLyDoRequestDto? request) =>
            Act(() => _service.TraLaiXuLyAsync(xuLyId, request ?? new CvLyDoRequestDto()));

        // ============================================================
        // DUYỆT / BÁO CÁO
        // ============================================================
        [HttpGet("duyet")]
        [Authorize(Policy = AppPermissions.CV.DUYET)]
        public Task<IActionResult> GetDuyet([FromQuery] CvDuyetQueryDto query) => Run(() => _service.GetDuyetAsync(query));

        [HttpPost("duyet")]
        [Authorize(Policy = AppPermissions.CV.DUYET)]
        public Task<IActionResult> Duyet([FromBody] CvDuyetRequestDto? request) => Run(() => _service.DuyetAsync(request ?? new CvDuyetRequestDto()));

        [HttpGet("bao-cao/to")]
        public Task<IActionResult> GetBaoCaoTo([FromQuery] string? ngay) => Run(() => _service.GetBaoCaoToAsync(ngay));

        [HttpGet("bao-cao/luong")]
        public Task<IActionResult> GetBaoCaoLuong([FromQuery] int thang, [FromQuery] string? maDvHt) => Run(() => _service.GetBaoCaoLuongAsync(thang, maDvHt));

        [HttpGet("export/phieu")]
        [Authorize(Policy = AppPermissions.CV.EXPORT)]
        public async Task<IActionResult> ExportPhieu([FromQuery] CvPhieuQueryDto query)
        {
            try
            {
                var bytes = await _service.ExportPhieuAsync(query);
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"PhieuCongViec_{DateTime.Now:ddMMyyyy_HHmm}.xlsx");
            }
            catch (Exception ex)
            {
                return Fail(ex, "Máy chủ chưa thể tạo file Excel.");
            }
        }

        [HttpGet("export/luong")]
        [Authorize(Policy = AppPermissions.CV.EXPORT)]
        public async Task<IActionResult> ExportLuong([FromQuery] int thang, [FromQuery] string? maDvHt)
        {
            try
            {
                var bytes = await _service.ExportLuongAsync(thang, maDvHt);
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"LuongCongViec_{thang}.xlsx");
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

        private async Task<IActionResult> Act(Func<Task> action)
        {
            try
            {
                await action();
                return NoContent();
            }
            catch (Exception ex)
            {
                return Fail(ex, "Máy chủ gặp lỗi khi xử lý.");
            }
        }

        private IActionResult Fail(Exception ex, string message)
        {
            if (ex is CvValidationException) return BadRequest(new { message = ex.Message });
            _logger.LogError(ex, "Lỗi API phiếu công việc: {Message}. TraceId {TraceId}", message, HttpContext.TraceIdentifier);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = $"{message} Mã tra cứu: {HttpContext.TraceIdentifier}" });
        }
    }
}
