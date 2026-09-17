using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DTNTB.Core.DTOs
{
    public class RoleDto
    {
        public string RoleCode { get; set; } = string.Empty;
        public string RoleName { get; set; } = string.Empty;
        public string DataScope { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> Permissions { get; set; } = new();
    }

    public class PermissionDto
    {
        public string PermissionCode { get; set; } = string.Empty;
        public string PermissionName { get; set; } = string.Empty;
        public string ModuleGroup { get; set; } = string.Empty;
    }

    public class UserRoleDto
    {
        public string MaNd { get; set; } = string.Empty;
        public string TenNv { get; set; } = string.Empty;
        public string MaNv { get; set; } = string.Empty;
        public string MaDv { get; set; } = string.Empty;
        public string RoleCode { get; set; } = string.Empty;
        public string RoleName { get; set; } = string.Empty;
        public string DataScope { get; set; } = string.Empty;
        public string AssignedDate { get; set; } = string.Empty;
    }

    public class AssignUserRoleRequestDto
    {
        [Required, StringLength(100)]
        public string MaNd { get; set; } = string.Empty;
        [Required, StringLength(50)]
        public string RoleCode { get; set; } = string.Empty;
    }

    public class UpdateRolePermissionsRequestDto
    {
        [Required, StringLength(50)]
        public string RoleCode { get; set; } = string.Empty;
        public List<string> Permissions { get; set; } = new();
    }

    public class SaveRoleRequestDto
    {
        [Required, StringLength(50)]
        public string RoleCode { get; set; } = string.Empty;
        [Required, StringLength(200)]
        public string RoleName { get; set; } = string.Empty;
        [Required, StringLength(30)]
        public string DataScope { get; set; } = string.Empty;
        [StringLength(1000)]
        public string Description { get; set; } = string.Empty;
    }

    // ==========================================
    // DTOs QUẢN LÝ MENU HỆ THỐNG & VAI TRÒ
    // ==========================================
    public class MenuItemDto
    {
        public string MenuCode { get; set; } = string.Empty;
        public string MenuName { get; set; } = string.Empty;
        public string MenuGroup { get; set; } = string.Empty;
        public string RouteUrl { get; set; } = string.Empty;
        public string Icon { get; set; } = "fa fa-circle-o";
        public int OrderIndex { get; set; }
        public int IsActive { get; set; } = 1;
    }

    public class UpdateRoleMenusRequestDto
    {
        [Required, StringLength(50)]
        public string RoleCode { get; set; } = string.Empty;
        public List<string> MenuCodes { get; set; } = new();
    }
}