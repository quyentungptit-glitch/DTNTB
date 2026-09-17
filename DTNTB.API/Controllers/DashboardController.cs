using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using DTNTB.Core.Constants;
using DTNTB.Core.DTOs;
using DTNTB.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DTNTB.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class DashboardController : ControllerBase
    {
        private readonly IDashboardService _dashboardService;
        private readonly ICurrentUserService _currentUserService;

        public DashboardController(IDashboardService dashboardService, ICurrentUserService currentUserService)
        {
            _dashboardService = dashboardService;
            _currentUserService = currentUserService;
        }

        private (string MaNv, string MaDv, bool IsSuperAdmin, bool CanAccessDashboard, bool CanActionDashboard) GetContext()
        {
            // 1. Lấy mã NV và mã ĐV từ CurrentUserService hoặc Fallback qua Claims
            string maNv = _currentUserService.MaNv
                       ?? User.FindFirst("ma_nv")?.Value
                       ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? "";

            string maDv = _currentUserService.MaDv
                       ?? User.FindFirst("ma_dv")?.Value
                       ?? "";

            // 2. Lấy toàn bộ claims để kiểm tra linh hoạt không phân biệt hoa thường
            var userClaims = User.Claims.ToList();

            string scope = userClaims.FirstOrDefault(c =>
                c.Type.Equals("dataScope", StringComparison.OrdinalIgnoreCase) ||
                c.Type.Equals("datascope", StringComparison.OrdinalIgnoreCase) ||
                c.Type.EndsWith("scope", StringComparison.OrdinalIgnoreCase))?.Value?.ToUpper() ?? "";

            string role = userClaims.FirstOrDefault(c =>
                c.Type.Equals("level_role_dtntb", StringComparison.OrdinalIgnoreCase) ||
                c.Type.Equals("role", StringComparison.OrdinalIgnoreCase) ||
                c.Type == ClaimTypes.Role ||
                c.Type.EndsWith("role", StringComparison.OrdinalIgnoreCase))?.Value?.ToUpper() ?? "";

            var permissions = userClaims
                .Where(c => c.Type.Equals("permission", StringComparison.OrdinalIgnoreCase) ||
                            c.Type.Equals("permissions", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Value.ToUpper())
                .ToHashSet();

            // 3. Phân định quyền Admin Toàn tỉnh
            bool isSuperAdmin = scope.Contains("TOAN_TINH")
                             || scope.Contains("GLOBAL")
                             || role == "1"
                             || role.Contains("ADMIN")
                             || permissions.Contains(AppPermissions.DTNTB.MANAGE_ALL)
                             || userClaims.Any(c => c.Value.ToUpper().Contains("TOAN_TINH"));

            // 4. Quyền truy cập Dashboard: Chỉ mở cho MANAGE_ALL, MANAGE_AREA, MANAGE_UNIT hoặc quyền DASHBOARD.VIEW
            bool canAccessDashboard = isSuperAdmin
                || permissions.Contains(AppPermissions.DASHBOARD.VIEW)
                || permissions.Contains(AppPermissions.DTNTB.MANAGE_ALL)
                || permissions.Contains(AppPermissions.DTNTB.MANAGE_AREA)
                || permissions.Contains(AppPermissions.DTNTB.MANAGE_UNIT)
                || scope.Contains("DIA_BAN")
                || scope.Contains("AREA")
                || scope.Contains("DON_VI")
                || scope.Contains("UNIT")
                || role == "2";

            // 5. Quyền giao phiếu: Yêu cầu quyền DASHBOARD.ACTION và phải thuộc nhóm có quyền quản lý
            bool canActionDashboard = canAccessDashboard && (
                permissions.Contains(AppPermissions.DASHBOARD.ACTION) ||
                permissions.Contains(AppPermissions.DTNTB.MANAGE_ALL) ||
                permissions.Contains(AppPermissions.DTNTB.MANAGE_UNIT) ||
                isSuperAdmin
            );

            return (maNv, maDv, isSuperAdmin, canAccessDashboard, canActionDashboard);
        }

        // ==============================================================================
        // CÁC ENDPOINT VIEW: YÊU CẦU QUYỀN TRUY CẬP DASHBOARD
        // ==============================================================================

        [HttpGet("kpis")]
        public async Task<IActionResult> GetKpis()
        {
            var (_, maDv, isSuperAdmin, canAccess, _) = GetContext();
            if (!canAccess)
            {
                return StatusCode(403, new { message = "Bạn không có quyền truy cập Dashboard Quản trị mắt lưới." });
            }

            var res = await _dashboardService.GetKpisAndChartsAsync(maDv, isSuperAdmin);
            return Ok(res);
        }

        [HttpGet("recent-activities")]
        public async Task<IActionResult> GetRecentActivities()
        {
            var (_, maDv, isSuperAdmin, canAccess, _) = GetContext();
            if (!canAccess)
            {
                return StatusCode(403, new { message = "Bạn không có quyền truy cập Dashboard Quản trị mắt lưới." });
            }

            var res = await _dashboardService.GetRecentActivitiesAsync(maDv, isSuperAdmin);
            return Ok(res);
        }

        [HttpGet("high-risk-plans")]
        public async Task<IActionResult> GetHighRiskPlans()
        {
            var (_, maDv, isSuperAdmin, canAccess, _) = GetContext();
            if (!canAccess)
            {
                return StatusCode(403, new { message = "Bạn không có quyền xem danh sách phiếu kế hoạch hôm nay." });
            }

            var res = await _dashboardService.GetHighRiskPlansTodayAsync(maDv, isSuperAdmin);
            return Ok(res);
        }

        [HttpGet("export-excel")]
        public async Task<IActionResult> ExportExcel()
        {
            var (_, maDv, isSuperAdmin, canAccess, _) = GetContext();
            if (!canAccess)
            {
                return StatusCode(403, new { message = "Bạn không có quyền xuất danh sách phiếu kế hoạch." });
            }

            var bytes = await _dashboardService.ExportExcelPlansTodayAsync(maDv, isSuperAdmin);
            string filename = $"DS_PhieuKeHoach_{DateTime.Now:ddMMyyyy}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", filename);
        }

        // ==============================================================================
        // CÁC ENDPOINT ACTION: BẮT BUỘC QUYỀN DASHBOARD.ACTION
        // ==============================================================================

        [HttpPost("assign-plan")]
        public async Task<IActionResult> AssignPlan([FromBody] AssignPlanRequest req)
        {
            var (maNv, _, _, canAccess, canAction) = GetContext();
            if (!canAccess || !canAction)
            {
                return StatusCode(403, new { message = "Bạn không có quyền thực hiện giao phiếu kế hoạch (Thiếu quyền DASHBOARD.ACTION)." });
            }

            var (success, msg) = await _dashboardService.AssignPhieuAsync(req.PhieuId, maNv, req.GhiChuGiao);
            if (!success) return BadRequest(new { message = msg });
            return Ok(new { message = msg });
        }

        [HttpPost("assign-all-plans")]
        public async Task<IActionResult> AssignAllPlans([FromBody] AssignAllPlansRequest req)
        {
            var (maNv, maDv, isSuperAdmin, canAccess, canAction) = GetContext();
            if (!canAccess || !canAction)
            {
                return StatusCode(403, new { message = "Bạn không có quyền giao hàng loạt phiếu kế hoạch (Thiếu quyền DASHBOARD.ACTION)." });
            }

            var (count, msg) = await _dashboardService.AssignAllPhieuAsync(maDv, isSuperAdmin, maNv, req.GhiChuChung);
            return Ok(new { count, message = msg });
        }
    }
}