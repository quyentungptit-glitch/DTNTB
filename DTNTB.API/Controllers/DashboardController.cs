using System;
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

        private (string MaNv, string MaDv, string DiaBanId, UserDataScopeLevel Scope, bool HasManagementScope) GetContext()
        {
            // Permission được ASP.NET Core kiểm tra bằng [Authorize(Policy = ...)].
            // data_scope từ JWT chỉ quyết định phạm vi dữ liệu, không suy luận từ role.
            var scope = _currentUserService.ScopeLevel;
            bool isManagementScope = scope is UserDataScopeLevel.ToanTinh
                or UserDataScopeLevel.DiaBan
                or UserDataScopeLevel.DonVi
                or UserDataScopeLevel.ToQuanLy;

            return (
                _currentUserService.MaNv ?? string.Empty,
                _currentUserService.MaDv ?? string.Empty,
                _currentUserService.DiaBanId ?? string.Empty,
                scope,
                isManagementScope);
        }

        // ==============================================================================
        // CÁC ENDPOINT VIEW: YÊU CẦU QUYỀN TRUY CẬP DASHBOARD
        // ==============================================================================

        [HttpGet("kpis")]
        [Authorize(Policy = AppPermissions.DASHBOARD.VIEW)]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> GetKpis()
        {
            var (_, maDv, diaBanId, scope, hasManagementScope) = GetContext();
            if (!hasManagementScope)
            {
                return Forbid();
            }

            var res = await _dashboardService.GetKpisAndChartsAsync(scope, maDv, diaBanId);
            return Ok(res);
        }

        [HttpGet("recent-activities")]
        [Authorize(Policy = AppPermissions.DASHBOARD.VIEW)]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> GetRecentActivities()
        {
            var (_, maDv, diaBanId, scope, hasManagementScope) = GetContext();
            if (!hasManagementScope)
            {
                return Forbid();
            }

            var res = await _dashboardService.GetRecentActivitiesAsync(scope, maDv, diaBanId);
            return Ok(res);
        }

        [HttpGet("high-risk-plans")]
        [Authorize(Policy = AppPermissions.DASHBOARD.VIEW)]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> GetHighRiskPlans()
        {
            var (_, maDv, diaBanId, scope, hasManagementScope) = GetContext();
            if (!hasManagementScope)
            {
                return Forbid();
            }

            var res = await _dashboardService.GetHighRiskPlansTodayAsync(scope, maDv, diaBanId);
            return Ok(res);
        }

        [HttpGet("export-excel")]
        [Authorize(Policy = AppPermissions.DASHBOARD.EXPORT)]
        public async Task<IActionResult> ExportExcel()
        {
            var (_, maDv, diaBanId, scope, hasManagementScope) = GetContext();
            if (!hasManagementScope)
            {
                return Forbid();
            }

            var bytes = await _dashboardService.ExportExcelPlansTodayAsync(scope, maDv, diaBanId);
            string filename = $"DS_PhieuKeHoach_{DateTime.Now:ddMMyyyy}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", filename);
        }

        // ==============================================================================
        // CÁC ENDPOINT ACTION: BẮT BUỘC QUYỀN DASHBOARD.ACTION
        // ==============================================================================

        [HttpPost("assign-plan")]
        [Authorize(Policy = AppPermissions.DASHBOARD.ACTION)]
        public async Task<IActionResult> AssignPlan([FromBody] AssignPlanRequest req)
        {
            var (maNv, maDv, diaBanId, scope, hasManagementScope) = GetContext();
            if (!hasManagementScope)
            {
                return Forbid();
            }

            var (success, msg) = await _dashboardService.AssignPhieuAsync(req.PhieuId, scope, maDv, diaBanId, maNv, req.GhiChuGiao);
            if (!success) return BadRequest(new { message = msg });
            return Ok(new { message = msg });
        }

        [HttpPost("assign-all-plans")]
        [Authorize(Policy = AppPermissions.DASHBOARD.ACTION)]
        public async Task<IActionResult> AssignAllPlans([FromBody] AssignAllPlansRequest req)
        {
            var (maNv, maDv, diaBanId, scope, hasManagementScope) = GetContext();
            if (!hasManagementScope)
            {
                return Forbid();
            }

            var (count, msg) = await _dashboardService.AssignAllPhieuAsync(scope, maDv, diaBanId, maNv, req.GhiChuChung);
            return Ok(new { count, message = msg });
        }
    }
}
