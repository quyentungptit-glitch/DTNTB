using DTNTB.Core.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DTNTB.Core.Interfaces
{
    public interface IGhttService
    {
        Task<List<GhttDonViDto>> GetDanhSachDonViAsync(string loaiDv, int thang);
        Task<GhttReportResponseDto> GetBaoCaoGhttAsync(int thang, int donvi, string loaiDv);
        Task<byte[]> ExportExcelGhttAsync(int thang, int donvi, string loaiDv);
        Task<bool> TongHopSoLieuAsync(int thang, string nguoiCn);
        Task<string> ChotSoLieuAsync(int thang, string nguoiCn);
    }
}