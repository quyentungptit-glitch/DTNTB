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
    public class HeThongController : ControllerBase
    {
        private readonly IHeThongService _heThongService;

        public HeThongController(IHeThongService heThongService)
        {
            _heThongService = heThongService;
        }

        // 1. Lấy danh mục tất cả vai trò
        [HttpGet("roles")]
        [Authorize(Policy = AppPermissions.HETHONG.VIEW)]
        public async Task<IActionResult> GetRoles()
        {
            var roles = await _heThongService.GetRolesAsync();
            return Ok(roles);
        }

        // 2. Lấy chi tiết vai trò cùng danh sách quyền của vai trò đó
        [HttpGet("roles/{roleCode}")]
        [Authorize(Policy = AppPermissions.HETHONG.VIEW)]
        public async Task<IActionResult> GetRoleDetail(string roleCode)
        {
            var role = await _heThongService.GetRoleDetailAsync(roleCode);
            if (role == null) return NotFound(new { message = "Không tìm thấy vai trò." });
            return Ok(role);
        }

        // 3. Lấy toàn bộ danh mục Permissions trong hệ thống
        [HttpGet("permissions")]
        [Authorize(Policy = AppPermissions.HETHONG.VIEW)]
        public async Task<IActionResult> GetAllPermissions()
        {
            var perms = await _heThongService.GetAllPermissionsAsync();
            return Ok(perms);
        }

        // 4. Thêm mới hoặc cập nhật vai trò
        [HttpPost("roles")]
        [Authorize(Policy = AppPermissions.HETHONG.ADD)]
        public async Task<IActionResult> SaveRole([FromBody] SaveRoleRequestDto model)
        {
            var success = await _heThongService.SaveRoleAsync(model);
            if (!success) return BadRequest(new { message = "Lưu vai trò thất bại." });
            return Ok(new { success = true, message = "Lưu vai trò thành công." });
        }

        [HttpPut("roles/{roleCode}")]
        [Authorize(Policy = AppPermissions.HETHONG.UPDATE)]
        public async Task<IActionResult> UpdateRole(string roleCode, [FromBody] SaveRoleRequestDto model)
        {
            if (!string.Equals(roleCode?.Trim(), model.RoleCode?.Trim(), StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "Mã vai trò trên URL và nội dung không khớp." });

            var success = await _heThongService.UpdateRoleAsync(model);
            if (!success) return BadRequest(new { message = "Cập nhật vai trò thất bại." });
            return Ok(new { success = true, message = "Cập nhật vai trò thành công." });
        }

        // 5. Xóa vai trò
        [HttpDelete("roles/{roleCode}")]
        [Authorize(Policy = AppPermissions.HETHONG.DELETE)]
        public async Task<IActionResult> DeleteRole(string roleCode)
        {
            var success = await _heThongService.DeleteRoleAsync(roleCode);
            if (!success) return BadRequest(new { message = "Xóa vai trò thất bại." });
            return Ok(new { success = true, message = "Xóa vai trò thành công." });
        }

        // 6. Cập nhật ma trận quyền cho vai trò (Gán permission cho Role)
        [HttpPut("roles/permissions")]
        [Authorize(Policy = AppPermissions.HETHONG.PHAN_QUYEN)]
        public async Task<IActionResult> UpdateRolePermissions([FromBody] UpdateRolePermissionsRequestDto model)
        {
            var success = await _heThongService.UpdateRolePermissionsAsync(model);
            if (!success) return BadRequest(new { message = "Cập nhật quyền cho vai trò thất bại." });
            return Ok(new { success = true, message = "Cập nhật quyền thành công." });
        }

        // 7. Lấy danh sách tài khoản đã được phân vai trò
        [HttpGet("users")]
        [Authorize(Policy = AppPermissions.HETHONG.VIEW)]
        public async Task<IActionResult> GetUserRoles(
            [FromQuery] string? search, [FromQuery] string? roleCode,
            [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var result = await _heThongService.GetUserRolesAsync(search, roleCode, page, pageSize);
            return Ok(result);
        }

        // 8. Gán vai trò cho người dùng
        [HttpPost("users/assign-role")]
        [Authorize(Policy = AppPermissions.HETHONG.PHAN_QUYEN)]
        public async Task<IActionResult> AssignUserRole([FromBody] AssignUserRoleRequestDto model)
        {
            var success = await _heThongService.AssignUserRoleAsync(model);
            if (!success) return BadRequest(new { message = "Gán vai trò cho người dùng thất bại." });
            return Ok(new { success = true, message = "Gán vai trò thành công." });
        }

        // 9. Thu hồi vai trò của người dùng (Đưa về NVKT mặc định)
        [HttpDelete("users/{maNd}/role")]
        [Authorize(Policy = AppPermissions.HETHONG.PHAN_QUYEN)]
        public async Task<IActionResult> RemoveUserRole(string maNd)
        {
            var success = await _heThongService.RemoveUserRoleAsync(maNd);
            if (!success) return BadRequest(new { message = "Thu hồi vai trò thất bại." });
            return Ok(new { success = true, message = "Thu hồi vai trò thành công." });
        }
    }
}
