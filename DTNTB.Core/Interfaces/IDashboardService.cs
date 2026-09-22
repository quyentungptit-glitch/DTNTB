using System.Collections.Generic;
using System.Threading.Tasks;
using DTNTB.Core.Constants;
using DTNTB.Core.DTOs;

namespace DTNTB.Core.Interfaces
{
    public interface IDashboardService
    {
        Task<DashboardKpiDto> GetKpisAndChartsAsync(UserDataScopeLevel scope, string? maDv, string? diaBanId);
        Task<List<RecentActivityDto>> GetRecentActivitiesAsync(UserDataScopeLevel scope, string? maDv, string? diaBanId);
        Task<List<KeHoachItemDto>> GetHighRiskPlansTodayAsync(UserDataScopeLevel scope, string? maDv, string? diaBanId);
        Task<(bool Success, string Message)> AssignPhieuAsync(decimal phieuId, UserDataScopeLevel scope, string? maDv, string? diaBanId, string maNvGiao, string? ghiChu);
        Task<(int Count, string Message)> AssignAllPhieuAsync(UserDataScopeLevel scope, string? maDv, string? diaBanId, string maNvGiao, string? ghiChuChung);
        Task<byte[]> ExportExcelPlansTodayAsync(UserDataScopeLevel scope, string? maDv, string? diaBanId);
    }
}
