using DTNTB.Core.Constants;
using DTNTB.Core.DTOs;
using DTNTB.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace DTNTB.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class DtntbController : ControllerBase
    {
        private static readonly HashSet<string> AllowedRiskFilters = new(StringComparer.OrdinalIgnoreCase)
        {
            "ALL", "BINH_THUONG", "THEO_DOI", "NGUY_CO", "CAO", "RAT_CAO"
        };
        private readonly IDtntbService _dtntbService;
        private readonly ILogger<DtntbController> _logger;

        public DtntbController(IDtntbService dtntbService, ILogger<DtntbController> logger)
        {
            _dtntbService = dtntbService;
            _logger = logger;
        }

        [HttpGet("donvi")]
        public async Task<IActionResult> GetDonVi()
        {
            var list = await _dtntbService.GetDonViAsync();
            if (list == null) return Forbid();
            return Ok(list);
        }

        [HttpGet("nvkt/{maDv}")]
        public async Task<IActionResult> GetNvkt(string maDv)
        {
            if (string.IsNullOrWhiteSpace(maDv) || maDv.Length > 30)
                return BadRequest(new { message = "Mã đơn vị không hợp lệ." });

            var list = await _dtntbService.GetNvktAsync(maDv);
            if (list == null) return Forbid();
            return Ok(list);
        }

        [HttpGet("list")]
        [Authorize(Policy = AppPermissions.DTNTB.VIEW)]
        public async Task<IActionResult> GetList(
            [FromQuery] string? maDv, [FromQuery] string? maNvkt,
            [FromQuery] string nguyCo = "ALL", [FromQuery] string? search = null,
            [FromQuery] int page = 1, [FromQuery] int pageSize = 12)
        {
            if (IsInvalidFilterInput(maDv, maNvkt, nguyCo, search))
                return BadRequest(new { message = "Tham số lọc không hợp lệ." });

            var result = await _dtntbService.GetListAsync(maDv, maNvkt, nguyCo, search, page, pageSize);
            return Ok(result);
        }

        [HttpGet("riskCount")]
        [Authorize(Policy = AppPermissions.DTNTB.VIEW)]
        public async Task<IActionResult> GetRiskCount(
            [FromQuery] string? maDv, [FromQuery] string? maNvkt, [FromQuery] string? search = null)
        {
            if (IsInvalidFilterInput(maDv, maNvkt, "ALL", search))
                return BadRequest(new { message = "Tham số lọc không hợp lệ." });

            var result = await _dtntbService.GetRiskCountAsync(maDv, maNvkt, search);
            return Ok(result);
        }

        [HttpGet("{phieuId}")]
        [Authorize(Policy = AppPermissions.DTNTB.VIEW)]
        public async Task<IActionResult> GetDetail(long phieuId)
        {
            if (phieuId <= 0) return BadRequest(new { message = "Mã phiếu không hợp lệ." });

            var detail = await _dtntbService.GetDetailAsync(phieuId);
            if (detail == null) return NotFound(new { message = "Không tìm thấy dữ liệu phiếu kế hoạch hoặc không có quyền." });
            return Ok(detail);
        }

        [HttpPost("tac-nghiep-nang-cap")]
        [Authorize(Policy = AppPermissions.DTNTB.ACTION)]
        public async Task<IActionResult> SaveTacNghiepUpgrade([FromForm] SaveTacNghiepFormDto model)
        {
            try
            {
                var success = await _dtntbService.SaveTacNghiepUpgradeAsync(model);
                if (!success) return BadRequest(new { message = "Ghi nhận tác nghiệp thất bại, ảnh không hợp lệ hoặc phiếu đã quá SLA 72h." });
                return Ok(new { success = true, message = "Lưu kết quả tác nghiệp thành công." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Không thể lưu tác nghiệp/ảnh của phiếu {PhieuId}", model.PhieuId);
                return StatusCode(StatusCodes.Status502BadGateway,
                    new { message = "Không thể kết nối máy chủ lưu ảnh. Vui lòng thử lại sau." });
            }
        }

        [HttpPost("delete-image-upgrade")]
        [Authorize(Policy = AppPermissions.DTNTB.ACTION)]
        public async Task<IActionResult> DeleteImageUpgrade([FromBody] DeleteImageRequestDto model)
        {
            try
            {
                var success = await _dtntbService.DeleteImageUpgradeAsync(model);
                if (!success) return BadRequest(new { message = "Xóa ảnh thất bại, không có quyền hoặc phiếu đã quá SLA 72h." });
                return Ok(new { success = true, message = "Xóa ảnh hiện trường thành công." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Không thể xóa ảnh của phiếu {PhieuId}", model.PhieuId);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "Máy chủ chưa thể xóa ảnh. Vui lòng thử lại sau." });
            }
        }

        [HttpGet("export-excel")]
        [Authorize(Policy = AppPermissions.DTNTB.EXPORT)]
        public async Task<IActionResult> ExportExcel(
            [FromQuery] string? maDv, [FromQuery] string? maNvkt,
            [FromQuery] string nguyCo = "ALL", [FromQuery] string? search = null)
        {
            if (IsInvalidFilterInput(maDv, maNvkt, nguyCo, search))
                return BadRequest(new { message = "Tham số lọc không hợp lệ." });

            var excelBytes = await _dtntbService.ExportExcelAsync(maDv, maNvkt, nguyCo, search);
            if (excelBytes == null) return BadRequest(new { message = "Lỗi xuất file Excel hoặc không có quyền." });

            return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"DanhSach_TinNhiem_ThueBao_{System.DateTime.Now:yyyyMMdd}.xlsx");
        }

        private static bool IsInvalidFilterInput(string? maDv, string? maNvkt, string nguyCo, string? search)
        {
            return (maDv?.Length ?? 0) > 30
                || (maNvkt?.Length ?? 0) > 50
                || (search?.Length ?? 0) > 100
                || !AllowedRiskFilters.Contains(nguyCo);
        }
    }
}
