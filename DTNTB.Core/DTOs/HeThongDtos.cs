using System;
using System.Collections.Generic;

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
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(100)]
        public string MaNd { get; set; } = string.Empty;
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(50)]
        public string RoleCode { get; set; } = string.Empty;
    }

    public class UpdateRolePermissionsRequestDto
    {
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(50)]
        public string RoleCode { get; set; } = string.Empty;
        public List<string> Permissions { get; set; } = new();
    }

    public class SaveRoleRequestDto
    {
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(50)]
        public string RoleCode { get; set; } = string.Empty;
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(200)]
        public string RoleName { get; set; } = string.Empty;
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(30)]
        public string DataScope { get; set; } = string.Empty;
        [System.ComponentModel.DataAnnotations.StringLength(1000)]
        public string Description { get; set; } = string.Empty;
    }
}
