using DTNTB.Core.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DTNTB.Core.Interfaces
{
    public interface IGhttService
    {
        Task<List<GhttDonViDto>> GetDanhSachDonViAsync(string loaiDv, int thang, string dataScope = "TOAN_TINH", string username = "");
        Task<GhttReportResponseDto> GetBaoCaoGhttAsync(int thang, int donvi, string loaiDv, string dataScope = "TOAN_TINH", string username = "");
        Task<byte[]> ExportExcelGhttAsync(int thang, int donvi, string loaiDv, string dataScope = "TOAN_TINH", string username = "");
        Task<bool> TongHopSoLieuAsync(int thang, string nguoiCn);
        Task<string> ChotSoLieuAsync(int thang, string nguoiCn);
        Task<PaginatedResultDto<GhttChuaGiaHanDto>> GetDanhSachChuaGiaHanAsync(GhttChuaGiaHanFilterDto filter, string dataScope, string username);
    }
}
