using System.Collections.Generic;
using System.Threading.Tasks;
using DTNTB.Core.DTOs;

namespace DTNTB.Core.Interfaces
{
    public interface IDashboardService
    {
        Task<DashboardKpiDto> GetKpisAndChartsAsync(string? maDv, bool isSuperAdmin);
        Task<List<RecentActivityDto>> GetRecentActivitiesAsync(string? maDv, bool isSuperAdmin);
        Task<List<KeHoachItemDto>> GetHighRiskPlansTodayAsync(string? maDv, bool isSuperAdmin);
        Task<(bool Success, string Message)> AssignPhieuAsync(decimal phieuId, string maNvGiao, string? ghiChu);
        Task<(int Count, string Message)> AssignAllPhieuAsync(string? maDv, bool isSuperAdmin, string maNvGiao, string? ghiChuChung);
        Task<byte[]> ExportExcelPlansTodayAsync(string? maDv, bool isSuperAdmin);
    }
}