using Dapper;
using DTNTB.Core.DTOs;
using DTNTB.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace DTNTB.Infrastructure.Services
{
    public class HeThongService : IHeThongService
    {
        private readonly string _connString;
        private readonly ICurrentUserService _currentUser;

        public HeThongService(IConfiguration config, ICurrentUserService currentUser)
        {
            _connString = config.GetConnectionString("ConnectionString_NBH") ?? string.Empty;
            _currentUser = currentUser;
        }

        public async Task<List<RoleDto>> GetRolesAsync()
        {
            using (var conn = new OracleConnection(_connString))
            {
                string query = @"
            SELECT r.role_code as RoleCode, r.role_name as RoleName, 
                   r.data_scope as DataScope, r.description as Description
            FROM dtntb_sys_roles r
            ORDER BY 
                CASE r.data_scope
                    WHEN 'TOAN_TINH' THEN 1
                    WHEN 'DIA_BAN'   THEN 2
                    WHEN 'DON_VI'    THEN 3
                    WHEN 'TO_QL'     THEN 4
                    WHEN 'NHAN_VIEN' THEN 5
                    ELSE 99
                END,
                CASE r.role_code
                    WHEN 'SUPER_ADMIN' THEN 1
                    WHEN 'AREA_ADMIN'  THEN 2
                    WHEN 'UNIT_ADMIN'  THEN 3
                    WHEN 'TO_TRUONG'   THEN 4
                    WHEN 'NVKT'        THEN 5
                    WHEN 'NVKD'        THEN 6
                    WHEN 'NVAM'        THEN 7
                    ELSE 99
                END";

                var roles = (await conn.QueryAsync<RoleDto>(query)).ToList();
                return roles;
            }
        }

        public async Task<RoleDto?> GetRoleDetailAsync(string roleCode)
        {
            using (var conn = new OracleConnection(_connString))
            {
                string queryRole = "SELECT role_code as RoleCode, role_name as RoleName, data_scope as DataScope, description as Description FROM dtntb_sys_roles WHERE UPPER(TRIM(role_code)) = UPPER(TRIM(:role_code))";
                var role = await conn.QueryFirstOrDefaultAsync<RoleDto>(queryRole, new { role_code = roleCode });
                if (role == null) return null;

                string queryPerms = "SELECT permission_code FROM dtntb_sys_role_permissions WHERE UPPER(TRIM(role_code)) = UPPER(TRIM(:role_code))";
                var perms = (await conn.QueryAsync<string>(queryPerms, new { role_code = roleCode })).ToList();
                role.Permissions = perms;

                return role;
            }
        }

        public async Task<List<PermissionDto>> GetAllPermissionsAsync()
        {
            using (var conn = new OracleConnection(_connString))
            {
                string query = @"
            SELECT permission_code as PermissionCode, permission_name as PermissionName, module_group as ModuleGroup 
            FROM dtntb_sys_permissions 
            ORDER BY 
                CASE module_group
                    WHEN 'DTNTB' THEN 1
                    WHEN 'HETHONG' THEN 2
                    ELSE 99
                END,
                CASE 
                    WHEN permission_code LIKE '%VIEW' THEN 1
                    WHEN permission_code LIKE '%ADD' THEN 2
                    WHEN permission_code LIKE '%UPDATE' THEN 3
                    WHEN permission_code LIKE '%DELETE' THEN 4
                    WHEN permission_code LIKE '%ACTION' THEN 5
                    WHEN permission_code LIKE '%IMPORT' THEN 6
                    WHEN permission_code LIKE '%EXPORT' THEN 7
                    WHEN permission_code LIKE '%PHAN_QUYEN' THEN 8
                    WHEN permission_code LIKE '%MANAGE_ALL' THEN 9
                    WHEN permission_code LIKE '%MANAGE_AREA' THEN 10
                    WHEN permission_code LIKE '%MANAGE_UNIT' THEN 11
                    WHEN permission_code LIKE '%MANAGE_TEAM' THEN 12
                    ELSE 99
                END";

                var list = (await conn.QueryAsync<PermissionDto>(query)).ToList();
                return list;
            }
        }

        public async Task<bool> SaveRoleAsync(SaveRoleRequestDto request)
        {
            var allowedScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "TOAN_TINH", "DIA_BAN", "DON_VI", "TO_QL", "NHAN_VIEN"
            };
            var normalizedScope = request.DataScope.Trim().ToUpperInvariant();
            if (!allowedScopes.Contains(normalizedScope)) return false;

            using (var conn = new OracleConnection(_connString))
            {
                const string insertQuery = @"
                    INSERT INTO dtntb_sys_roles (role_code, role_name, data_scope, description)
                    SELECT :role_code, :role_name, :data_scope, :description
                    FROM dual
                    WHERE NOT EXISTS (
                        SELECT 1 FROM dtntb_sys_roles
                        WHERE UPPER(TRIM(role_code)) = UPPER(TRIM(:role_code))
                    )";

                var rows = await conn.ExecuteAsync(insertQuery, new
                {
                    role_code = request.RoleCode.Trim().ToUpper(),
                    role_name = request.RoleName.Trim(),
                    data_scope = normalizedScope,
                    description = request.Description?.Trim() ?? ""
                });

                return rows > 0;
            }
        }

        public async Task<bool> UpdateRoleAsync(SaveRoleRequestDto request)
        {
            var allowedScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "TOAN_TINH", "DIA_BAN", "DON_VI", "TO_QL", "NHAN_VIEN"
            };
            var normalizedScope = request.DataScope.Trim().ToUpperInvariant();
            if (!allowedScopes.Contains(normalizedScope)) return false;

            using var conn = new OracleConnection(_connString);
            const string currentScopeQuery = @"
                SELECT data_scope FROM dtntb_sys_roles
                WHERE UPPER(TRIM(role_code)) = UPPER(TRIM(:role_code))";
            var currentScope = await conn.QueryFirstOrDefaultAsync<string>(currentScopeQuery, new
            {
                role_code = request.RoleCode
            });
            if (currentScope == null) return false;
            if (!string.Equals(currentScope.Trim(), normalizedScope, StringComparison.OrdinalIgnoreCase)
                && !_currentUser.HasPermission("PERMISSIONS.HETHONG.PHAN_QUYEN"))
            {
                return false;
            }

            const string updateQuery = @"
                UPDATE dtntb_sys_roles
                SET role_name = :role_name,
                    data_scope = :data_scope,
                    description = :description
                WHERE UPPER(TRIM(role_code)) = UPPER(TRIM(:role_code))";
            var rows = await conn.ExecuteAsync(updateQuery, new
            {
                role_code = request.RoleCode.Trim().ToUpperInvariant(),
                role_name = request.RoleName.Trim(),
                data_scope = normalizedScope,
                description = request.Description?.Trim() ?? ""
            });
            return rows > 0;
        }

        public async Task<bool> DeleteRoleAsync(string roleCode)
        {
            if (string.Equals(roleCode?.Trim(), "SUPER_ADMIN", StringComparison.OrdinalIgnoreCase))
                return false;

            using (var conn = new OracleConnection(_connString))
            {
                string query = "DELETE FROM dtntb_sys_roles WHERE UPPER(TRIM(role_code)) = UPPER(TRIM(:role_code))";
                var rows = await conn.ExecuteAsync(query, new { role_code = roleCode });
                return rows > 0;
            }
        }

        public async Task<bool> UpdateRolePermissionsAsync(UpdateRolePermissionsRequestDto request)
        {
            if (request.Permissions.Count > 100
                || request.Permissions.Any(permission => string.IsNullOrWhiteSpace(permission) || permission.Length > 100))
            {
                return false;
            }

            using (var conn = new OracleConnection(_connString))
            {
                await conn.OpenAsync();
                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        // 1. Xóa các quyền cũ của role
                        string deleteQuery = "DELETE FROM dtntb_sys_role_permissions WHERE UPPER(TRIM(role_code)) = UPPER(TRIM(:role_code))";
                        await conn.ExecuteAsync(deleteQuery, new { role_code = request.RoleCode }, trans);

                        // 2. Chèn lại danh sách quyền mới
                        if (request.Permissions != null && request.Permissions.Any())
                        {
                            string insertQuery = "INSERT INTO dtntb_sys_role_permissions (role_code, permission_code) VALUES (:role_code, :permission_code)";
                            var insertParams = request.Permissions.Select(p => new
                            {
                                role_code = request.RoleCode.Trim().ToUpper(),
                                permission_code = p.Trim().ToUpper()
                            });
                            await conn.ExecuteAsync(insertQuery, insertParams, trans);
                        }

                        trans.Commit();
                        return true;
                    }
                    catch
                    {
                        trans.Rollback();
                        return false;
                    }
                }
            }
        }

        public async Task<PaginatedResultDto<UserRoleDto>> GetUserRolesAsync(string? search, string? roleCode, int page, int pageSize)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            using (var conn = new OracleConnection(_connString))
            {
                string baseSql = @"
                    FROM dtntb_sys_user_roles ur
                    INNER JOIN dtntb_sys_roles r ON ur.role_code = r.role_code
                    LEFT JOIN v_nguoidung_diaban nv ON UPPER(TRIM(ur.ma_nd)) = UPPER(TRIM(nv.ma_nd))
                    WHERE 1 = 1";

                var parameters = new DynamicParameters();

                if (!string.IsNullOrEmpty(roleCode) && roleCode != "ALL")
                {
                    baseSql += " AND UPPER(TRIM(ur.role_code)) = UPPER(TRIM(:role_code))";
                    parameters.Add("role_code", roleCode, DbType.String);
                }

                if (!string.IsNullOrEmpty(search))
                {
                    baseSql += " AND (UPPER(ur.ma_nd) LIKE UPPER(:search) OR UPPER(nv.ten_nv) LIKE UPPER(:search) OR UPPER(nv.ma_nv) LIKE UPPER(:search))";
                    parameters.Add("search", "%" + search.Trim() + "%", DbType.String);
                }

                string countSql = "SELECT COUNT(1) " + baseSql;
                int totalCount = await conn.ExecuteScalarAsync<int>(countSql, parameters);

                int startRow = (page - 1) * pageSize + 1;
                int endRow = page * pageSize;
                parameters.Add("start_row", startRow, DbType.Int32);
                parameters.Add("end_row", endRow, DbType.Int32);

                string paginatedSql = $@"
                                        SELECT * FROM (
                                            SELECT a.*, ROWNUM rnum FROM (
                                                SELECT ur.ma_nd as MaNd, nv.ten_nv as TenNv, nv.ma_nv as MaNv, nv.ma_dv as MaDv,
                                                       r.role_code as RoleCode, r.role_name as RoleName, r.data_scope as DataScope,
                                                       ur.assigned_date as AssignedDate
                                                {baseSql}
                                                ORDER BY 
                                                    CASE r.data_scope
                                                        WHEN 'TOAN_TINH' THEN 1
                                                        WHEN 'DIA_BAN'   THEN 2
                                                        WHEN 'DON_VI'    THEN 3
                                                        WHEN 'TO_QL'     THEN 4
                                                        WHEN 'NHAN_VIEN' THEN 5
                                                        ELSE 99
                                                    END,
                                                    ur.assigned_date DESC
                                            ) a WHERE ROWNUM <= :end_row
                                        ) WHERE rnum >= :start_row";

                var list = (await conn.QueryAsync<dynamic>(paginatedSql, parameters)).Select(x => new UserRoleDto
                {
                    MaNd = x.MAND?.ToString() ?? "",
                    TenNv = x.TENNV?.ToString() ?? "",
                    MaNv = x.MANV?.ToString() ?? "",
                    MaDv = x.MADV?.ToString() ?? "",
                    RoleCode = x.ROLECODE?.ToString() ?? "",
                    RoleName = x.ROLENAME?.ToString() ?? "",
                    DataScope = x.DATASCOPE?.ToString() ?? "",
                    AssignedDate = x.ASSIGNEDDATE != null ? Convert.ToDateTime(x.ASSIGNEDDATE).ToString("dd/MM/yyyy HH:mm") : "-"
                }).ToList();

                return new PaginatedResultDto<UserRoleDto>
                {
                    Items = list,
                    TotalCount = totalCount
                };
            }
        }

        public async Task<bool> AssignUserRoleAsync(AssignUserRoleRequestDto request)
        {
            using (var conn = new OracleConnection(_connString))
            {
                string mergeQuery = @"
                    MERGE INTO dtntb_sys_user_roles target
                    USING (
                        SELECT :ma_nd as ma_nd, :role_code as role_code FROM dual
                    ) src
                    ON (UPPER(TRIM(target.ma_nd)) = UPPER(TRIM(src.ma_nd)))
                    WHEN MATCHED THEN
                        UPDATE SET target.role_code = src.role_code, target.assigned_date = SYSDATE
                    WHEN NOT MATCHED THEN
                        INSERT (ma_nd, role_code, assigned_date) 
                        VALUES (src.ma_nd, src.role_code, SYSDATE)";

                var rows = await conn.ExecuteAsync(mergeQuery, new
                {
                    ma_nd = request.MaNd.Trim().ToLower(),
                    role_code = request.RoleCode.Trim().ToUpper()
                });

                return rows > 0;
            }
        }

        public async Task<bool> RemoveUserRoleAsync(string maNd)
        {
            using (var conn = new OracleConnection(_connString))
            {
                string query = "DELETE FROM dtntb_sys_user_roles WHERE UPPER(TRIM(ma_nd)) = UPPER(TRIM(:ma_nd))";
                var rows = await conn.ExecuteAsync(query, new { ma_nd = maNd });
                return rows > 0;
            }
        }
    }
}
