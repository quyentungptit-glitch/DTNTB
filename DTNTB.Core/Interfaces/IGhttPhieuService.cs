using DTNTB.Core.DTOs;

namespace DTNTB.Core.Interfaces
{
    /// <summary>
    /// Giao phiếu tiếp cận TB GHTT (Biểu 8) và cập nhật kết quả. Dùng chung bảng GHTT_PHIEU với BCBG.
    /// Phạm vi dữ liệu theo data_scope trong JWT (toàn tỉnh / địa bàn / đơn vị / tổ / nhân viên).
    /// </summary>
    public interface IGhttPhieuService
    {
        Task<List<int>> GetThangAsync();
        Task<List<GhttPhieuDonViDto>> GetDonViAsync();
        Task<List<string>> GetGoiYLyDoAsync();

        /// <summary>TB cần tiếp cận trong phạm vi kèm phiếu gần nhất.</summary>
        Task<List<GhttThueBaoDto>> GetThueBaoAsync(int thang, string? maDv);
        Task<GhttGiaoPhieuResultDto> GiaoPhieuAsync(GhttGiaoPhieuRequestDto request);
        Task<byte[]> ExportThueBaoAsync(GhttThueBaoQueryDto query);

        Task<GhttPhieuListDto> GetPhieuAsync(GhttPhieuQueryDto query);
        Task<List<GhttPhieuBoLocDto>> GetBoLocPhieuAsync();
        /// <summary>Cập nhật kết quả; false nếu phiếu không còn hiệu lực hoặc ngoài phạm vi.</summary>
        Task<bool> CapNhatKetQuaAsync(long phieuId, GhttKetQuaRequestDto request);
    }

    public class GhttPhieuValidationException : Exception
    {
        public GhttPhieuValidationException(string message) : base(message) { }
    }
}
