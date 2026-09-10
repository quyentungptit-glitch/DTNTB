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
        private readonly IDtntbService _dtntbService;

        public DtntbController(IDtntbService dtntbService)
        {
            _dtntbService = dtntbService;
        }

        [HttpGet("donvi")]
        [Authorize(Policy = AppPermissions.DTNTB.MANAGE_ALL)]
        public async Task<IActionResult> GetDonVi()
        {
            var list = await _dtntbService.GetDonViAsync();
            if (list == null) return Forbid();
            return Ok(list);
        }

        [HttpGet("nvkt/{maDv}")]
        public async Task<IActionResult> GetNvkt(string maDv)
        {
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
            var result = await _dtntbService.GetListAsync(maDv, maNvkt, nguyCo, search, page, pageSize);
            return Ok(result);
        }

        [HttpGet("{phieuId}")]
        [Authorize(Policy = AppPermissions.DTNTB.VIEW)]
        public async Task<IActionResult> GetDetail(long phieuId)
        {
            var detail = await _dtntbService.GetDetailAsync(phieuId);
            if (detail == null) return NotFound(new { message = "Không tìm thấy dữ liệu phiếu kế hoạch hoặc không có quyền." });
            return Ok(detail);
        }

        [HttpPost("tac-nghiep-nang-cap")]
        [Authorize(Policy = AppPermissions.DTNTB.ACTION)]
        public async Task<IActionResult> SaveTacNghiepUpgrade([FromForm] SaveTacNghiepFormDto model)
        {
            var success = await _dtntbService.SaveTacNghiepUpgradeAsync(model);
            if (!success) return BadRequest(new { message = "Ghi nhận tác nghiệp thất bại hoặc phiếu quá hạn 24h." });
            return Ok(new { success = true, message = "Lưu kết quả tác nghiệp thành công." });
        }

        [HttpPost("delete-image-upgrade")]
        [Authorize(Policy = AppPermissions.DTNTB.ACTION)]
        public async Task<IActionResult> DeleteImageUpgrade([FromBody] DeleteImageRequestDto model)
        {
            var success = await _dtntbService.DeleteImageUpgradeAsync(model);
            if (!success) return BadRequest(new { message = "Xóa ảnh thất bại hoặc phiếu quá hạn SLA 24h." });
            return Ok(new { success = true, message = "Xóa ảnh hiện trường thành công." });
        }

        [HttpGet("export-excel")]
        [Authorize(Policy = AppPermissions.DTNTB.EXPORT)]
        public async Task<IActionResult> ExportExcel(
            [FromQuery] string? maDv, [FromQuery] string? maNvkt,
            [FromQuery] string nguyCo = "ALL", [FromQuery] string? search = null)
        {
            var excelBytes = await _dtntbService.ExportExcelAsync(maDv, maNvkt, nguyCo, search);
            if (excelBytes == null) return BadRequest(new { message = "Lỗi xuất file Excel hoặc không có quyền." });

            return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"DanhSach_TinNhiem_ThueBao_{System.DateTime.Now:yyyyMMdd}.xlsx");
        }
    }
}