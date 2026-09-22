using DTNTB.Core.Constants;
using DTNTB.Core.DTOs;
using DTNTB.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace DTNTB.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class HeThongController : ControllerBase
    {
        private readonly IHeThongService _heThongService;
        private readonly ICurrentUserService _currentUser;

        public HeThongController(IHeThongService heThongService, ICurrentUserService currentUser)
        {
            _heThongService = heThongService;
            _currentUser = currentUser;
        }

        // Quản trị role, permission và menu tác động đến toàn bộ hệ thống, do đó ngoài
        // permission của từng thao tác còn bắt buộc phạm vi dữ liệu toàn tỉnh.
        // Không suy luận quyền từ mã role (ADMIN/SUPER_ADMIN).
        private bool HasProvinceSystemScope() =>
            _currentUser.ScopeLevel == UserDataScopeLevel.ToanTinh;

        private IActionResult? RequireProvinceSystemScope() =>
            HasProvinceSystemScope()
                ? null
                : Forbid();

        // ==========================================
        // QUẢN TRỊ VAI TRÒ & PERMISSIONS
        // ==========================================

        // 1. Lấy danh mục tất cả vai trò
        [HttpGet("roles")]
        [Authorize(Policy = AppPermissions.HETHONG.VIEW)]
        public async Task<IActionResult> GetRoles()
        {
            if (RequireProvinceSystemScope() is IActionResult denied) return denied;
            var roles = await _heThongService.GetRolesAsync();
            return Ok(roles);
        }

        // 2. Lấy chi tiết vai trò cùng danh sách quyền của vai trò đó
        [HttpGet("roles/{roleCode}")]
        [Authorize(Policy = AppPermissions.HETHONG.VIEW)]
        public async Task<IActionResult> GetRoleDetail(string roleCode)
        {
            if (RequireProvinceSystemScope() is IActionResult denied) return denied;
            var role = await _heThongService.GetRoleDetailAsync(roleCode);
            if (role == null) return NotFound(new { message = "Không tìm thấy vai trò." });
            return Ok(role);
        }

        // 3. Lấy toàn bộ danh mục Permissions trong hệ thống
        [HttpGet("permissions")]
        [Authorize(Policy = AppPermissions.HETHONG.VIEW)]
        public async Task<IActionResult> GetAllPermissions()
        {
            if (RequireProvinceSystemScope() is IActionResult denied) return denied;
            var perms = await _heThongService.GetAllPermissionsAsync();
            return Ok(perms);
        }

        // 4. Thêm mới vai trò
        [HttpPost("roles")]
        [Authorize(Policy = AppPermissions.HETHONG.ADD)]
        public async Task<IActionResult> SaveRole([FromBody] SaveRoleRequestDto model)
        {
            if (RequireProvinceSystemScope() is IActionResult denied) return denied;
            var success = await _heThongService.SaveRoleAsync(model);
            if (!success) return BadRequest(new { message = "Lưu vai trò thất bại." });
            return Ok(new { success = true, message = "Lưu vai trò thành công." });
        }

        // 4.1. Cập nhật thông tin vai trò
        [HttpPut("roles/{roleCode}")]
        [Authorize(Policy = AppPermissions.HETHONG.UPDATE)]
        public async Task<IActionResult> UpdateRole(string roleCode, [FromBody] SaveRoleRequestDto model)
        {
            if (RequireProvinceSystemScope() is IActionResult denied) return denied;
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
            if (RequireProvinceSystemScope() is IActionResult denied) return denied;
            var success = await _heThongService.DeleteRoleAsync(roleCode);
            if (!success) return BadRequest(new { message = "Xóa vai trò thất bại." });
            return Ok(new { success = true, message = "Xóa vai trò thành công." });
        }

        // 6. Cập nhật ma trận quyền cho vai trò (Gán permission cho Role)
        [HttpPut("roles/permissions")]
        [Authorize(Policy = AppPermissions.HETHONG.PHAN_QUYEN)]
        public async Task<IActionResult> UpdateRolePermissions([FromBody] UpdateRolePermissionsRequestDto model)
        {
            if (RequireProvinceSystemScope() is IActionResult denied) return denied;
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
            if (RequireProvinceSystemScope() is IActionResult denied) return denied;
            var result = await _heThongService.GetUserRolesAsync(search, roleCode, page, pageSize);
            return Ok(result);
        }

        // 8. Gán vai trò cho người dùng
        [HttpPost("users/assign-role")]
        [Authorize(Policy = AppPermissions.HETHONG.PHAN_QUYEN)]
        public async Task<IActionResult> AssignUserRole([FromBody] AssignUserRoleRequestDto model)
        {
            if (RequireProvinceSystemScope() is IActionResult denied) return denied;
            var success = await _heThongService.AssignUserRoleAsync(model);
            if (!success) return BadRequest(new { message = "Gán vai trò cho người dùng thất bại." });
            return Ok(new { success = true, message = "Gán vai trò thành công." });
        }

        // 9. Thu hồi vai trò của người dùng (Đưa về NVKT mặc định)
        [HttpDelete("users/{maNd}/role")]
        [Authorize(Policy = AppPermissions.HETHONG.PHAN_QUYEN)]
        public async Task<IActionResult> RemoveUserRole(string maNd)
        {
            if (RequireProvinceSystemScope() is IActionResult denied) return denied;
            var success = await _heThongService.RemoveUserRoleAsync(maNd);
            if (!success) return BadRequest(new { message = "Thu hồi vai trò thất bại." });
            return Ok(new { success = true, message = "Thu hồi vai trò thành công." });
        }

        // ==========================================
        // QUẢN TRỊ MENU HỆ THỐNG & PHÂN MENU THEO VAI TRÒ
        // ==========================================

        // 10. Lấy toàn bộ danh mục Menu hệ thống (dành cho hiển thị Tab 3 Phân Menu)
        [HttpGet("menus")]
        [Authorize(Policy = AppPermissions.HETHONG.VIEW)]
        public async Task<IActionResult> GetSystemMenus()
        {
            if (RequireProvinceSystemScope() is IActionResult denied) return denied;
            var menus = await _heThongService.GetSystemMenusAsync();
            return Ok(menus);
        }

        // 11. Lấy danh sách mã Menu đã gán cho 1 vai trò cụ thể
        [HttpGet("roles/{roleCode}/menus")]
        [Authorize(Policy = AppPermissions.HETHONG.VIEW)]
        public async Task<IActionResult> GetRoleMenus(string roleCode)
        {
            if (RequireProvinceSystemScope() is IActionResult denied) return denied;
            var menus = await _heThongService.GetRoleMenusAsync(roleCode);
            return Ok(menus);
        }

        // 12. Cập nhật phân quyền Menu cho vai trò (Được gọi khi bấm nút "Lưu Cấu Hình Menu" ở Tab 3)
        [HttpPut("roles/menus")]
        [Authorize(Policy = AppPermissions.HETHONG.PHAN_QUYEN)]
        public async Task<IActionResult> UpdateRoleMenus([FromBody] UpdateRoleMenusRequestDto model)
        {
            if (RequireProvinceSystemScope() is IActionResult denied) return denied;
            if (string.IsNullOrWhiteSpace(model.RoleCode))
            {
                return BadRequest(new { message = "Mã vai trò không được để trống." });
            }

            var success = await _heThongService.UpdateRoleMenusAsync(model);
            if (!success)
            {
                return BadRequest(new { message = "Cập nhật phân quyền menu cho vai trò thất bại." });
            }

            return Ok(new { success = true, message = $"Cập nhật cấu hình menu cho vai trò [{model.RoleCode}] thành công." });
        }

        // 13. Lấy danh sách menu động cho người dùng hiện tại (Hiển thị lên Sidebar MainLayout)
        [HttpGet("user-menus")]
        public async Task<IActionResult> GetUserMenus()
        {
            // Trích xuất Role của tài khoản đang đăng nhập từ JWT Claims
            string role = User.FindFirst("role")?.Value
                       ?? User.FindFirst(ClaimTypes.Role)?.Value
                       ?? User.FindFirst("level_role_dtntb")?.Value
                       ?? "NVKT";

            var menus = await _heThongService.GetUserMenusAsync(role);
            return Ok(menus);
        }
    }
}
