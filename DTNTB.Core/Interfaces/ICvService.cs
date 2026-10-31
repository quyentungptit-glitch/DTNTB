using DTNTB.Core.DTOs;
using Microsoft.AspNetCore.Http;

namespace DTNTB.Core.Interfaces
{
    /// <summary>
    /// Quản lý công việc TTHT (phiếu yêu cầu công việc): tạo phiếu -> người nhận giao / chuyển tổ / trả lại
    /// -> người xử lý chụp ảnh, nhập số lượng, yêu cầu khoá -> người tạo đóng -> duyệt tính lương.
    /// </summary>
    public interface ICvService
    {
        Task<CvContextDto> GetContextAsync();

        // Danh mục
        Task<List<CvDauViecDto>> GetDauViecAsync(bool tatCa);
        Task<CvDauViecDto> LuuDauViecAsync(CvDauViecDto dto);
        Task<List<CvNguoiNhanDto>> GetNguoiNhanAsync(bool tatCa);
        Task<CvNguoiNhanDto> LuuNguoiNhanAsync(CvNguoiNhanDto dto);
        /// <summary>Người nhận chọn được khi tạo phiếu theo đầu việc (đã áp quyền tạo theo địa bàn / toàn tỉnh).</summary>
        Task<List<CvNguoiNhanDto>> GetNguoiNhanTaoPhieuAsync(int dauViecId);

        // Phiếu
        Task<List<CvPhieuDto>> GetPhieuListAsync(CvPhieuQueryDto query);
        Task<CvPhieuChiTietDto?> GetPhieuAsync(long phieuId);
        Task<CvPhieuDto> TaoPhieuAsync(CvPhieuRequestDto request);
        Task<CvPhieuDto> SuaPhieuAsync(long phieuId, CvPhieuRequestDto request);
        Task HuyPhieuAsync(long phieuId, CvLyDoRequestDto request);
        Task TraPhieuAsync(long phieuId, CvLyDoRequestDto request);
        Task ChuyenPhieuAsync(long phieuId, CvChuyenRequestDto request);
        Task<List<CvNhanVienDto>> GetNhanVienGiaoAsync(long phieuId, string? search);
        Task GiaoAsync(long phieuId, CvGiaoRequestDto request);
        Task HuyGiaoAsync(long xuLyId);

        // File
        Task<CvFileDto> UploadDinhKemAsync(long phieuId, IFormFile file, CancellationToken ct = default);
        Task<List<CvFileDto>> UploadAnhAsync(long xuLyId, IReadOnlyList<IFormFile> files, string? metaJson, CancellationToken ct = default);
        Task XoaFileAsync(long fileId, CancellationToken ct = default);
        Task<(Stream Stream, string ContentType, string FileName)?> GetFileAsync(long fileId, bool thuNho, CancellationToken ct = default);

        // Xử lý / đóng
        Task<List<CvViecDto>> GetViecCuaToiAsync(string? tab);
        Task YeuCauKhoaAsync(long xuLyId, CvYeuCauKhoaRequestDto request);
        Task DongAsync(long xuLyId);
        Task TraLaiXuLyAsync(long xuLyId, CvLyDoRequestDto request);

        // Duyệt tính lương
        Task<List<CvViecDto>> GetDuyetAsync(CvDuyetQueryDto query);
        Task<CvDuyetKetQuaDto> DuyetAsync(CvDuyetRequestDto request);

        // Báo cáo
        Task<List<CvBaoCaoToDto>> GetBaoCaoToAsync(string? ngay);
        Task<List<CvBaoCaoLuongDto>> GetBaoCaoLuongAsync(int thang, string? maDvHt);
        Task<byte[]> ExportPhieuAsync(CvPhieuQueryDto query);
        Task<byte[]> ExportLuongAsync(int thang, string? maDvHt);
    }

    /// <summary>Kho file phiếu công việc (MinIO, bucket riêng "ttht-congviec").</summary>
    public interface ICvFileStore
    {
        Task SaveAsync(string objectKey, byte[] content, string contentType, CancellationToken ct = default);
        Task<Stream?> GetAsync(string objectKey, CancellationToken ct = default);
        Task DeleteAsync(string objectKey, CancellationToken ct = default);
    }

    public class CvValidationException : Exception
    {
        public CvValidationException(string message) : base(message) { }
    }
}
