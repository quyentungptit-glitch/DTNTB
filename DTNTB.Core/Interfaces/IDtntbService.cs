using DTNTB.Core.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DTNTB.Core.Interfaces
{
    public interface IDtntbService
    {
        Task<List<DropdownItemDto>?> GetDonViAsync();
        Task<List<DropdownItemDto>?> GetNvktAsync(string maDv);
        Task<PaginatedResultDto<DtntbDetailDto>> GetListAsync(string? maDv, string? maNvkt, string nguyCo, string? search, int page, int pageSize);
        Task<RiskCountDto> GetRiskCountAsync(string? maDv, string? maNvkt, string? search);
        Task<DtntbDetailDto?> GetDetailAsync(long phieuId);
        Task<bool> SaveTacNghiepUpgradeAsync(SaveTacNghiepFormDto model);
        Task<bool> DeleteImageUpgradeAsync(DeleteImageRequestDto model);
        Task<byte[]?> ExportExcelAsync(string? maDv, string? maNvkt, string nguyCo, string? search);
        Task<(Stream Stream, string ContentType)?> GetImageAsync(long phieuId, string fileNameOrRelativePath);
    }
}
