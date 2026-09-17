using DTNTB.Core.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DTNTB.Core.Interfaces
{
    public interface IHeThongService
    {
        Task<List<RoleDto>> GetRolesAsync();
        Task<RoleDto?> GetRoleDetailAsync(string roleCode);
        Task<List<PermissionDto>> GetAllPermissionsAsync();
        Task<bool> SaveRoleAsync(SaveRoleRequestDto request);
        Task<bool> UpdateRoleAsync(SaveRoleRequestDto request);
        Task<bool> DeleteRoleAsync(string roleCode);
        Task<bool> UpdateRolePermissionsAsync(UpdateRolePermissionsRequestDto request);
        Task<PaginatedResultDto<UserRoleDto>> GetUserRolesAsync(string? search, string? roleCode, int page, int pageSize);
        Task<bool> AssignUserRoleAsync(AssignUserRoleRequestDto request);
        Task<bool> RemoveUserRoleAsync(string maNd);

        // ==========================================
        // QUẢN TRỊ MENU HỆ THỐNG & PHÂN MENU THEO VAI TRÒ
        // ==========================================
        Task<List<MenuItemDto>> GetSystemMenusAsync();
        Task<List<string>> GetRoleMenusAsync(string roleCode);
        Task<bool> UpdateRoleMenusAsync(UpdateRoleMenusRequestDto request);
        Task<List<MenuItemDto>> GetUserMenusAsync(string roleCode);
    }
}