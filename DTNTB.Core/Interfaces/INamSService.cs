using DTNTB.Core.DTOs;
using Microsoft.AspNetCore.Http;

namespace DTNTB.Core.Interfaces
{
    /// <summary>5S nhà trạm: giao trạm, nhân viên tải ảnh và nộp, chấm điểm, báo cáo. Phạm vi theo data_scope.</summary>
    public interface INamSService
    {
        Task<List<NamSTieuChiDto>> GetTieuChiAsync();
        IReadOnlyList<NamSXepLoaiDto> GetXepLoai();
        Task<List<NamSOptionDto>> GetDonViAsync();
        Task<List<NamSOptionDto>> GetToAsync(string? maDv);

        // Giao
        Task<List<NamSTramDto>> GetTramAsync(NamSTramQueryDto query);
        Task<List<NamSNhanVienDto>> GetNhanVienAsync(string? maDv, string? maToQl);
        /// <summary>Đơn vị / tổ của nhân sự (để chọn người nhận phiếu), theo phạm vi người xem.</summary>
        Task<List<NamSOptionDto>> GetNvDonViAsync();
        Task<List<NamSOptionDto>> GetNvToAsync(string? maDv);
        Task<NamSKetQuaGiaoDto> GiaoAsync(NamSGiaoRequestDto request);
        /// <summary>File Excel mẫu giao trạm (kèm danh sách trạm và nhân viên trong phạm vi để tra mã).</summary>
        Task<byte[]> TaoFileMauGiaoAsync();
        /// <summary>Đọc file Excel giao trạm: xacNhan = false chỉ kiểm tra từng dòng; true thì giao các dòng hợp lệ.</summary>
        Task<NamSImportKetQuaDto> ImportGiaoAsync(IFormFile file, bool xacNhan, CancellationToken ct = default);
        Task<bool> ThuHoiAsync(long phieuId, CancellationToken ct = default);
        Task<List<NamSPhieuDto>> GetPhieuDangGiaoAsync(NamSTramQueryDto query);

        // Nhân viên
        Task<List<NamSPhieuDto>> GetCuaToiAsync();
        /// <summary>Trạm nhân viên tự tạo phiếu được: tìm theo từ khoá, hoặc gần toạ độ (lat/lng) nhất.</summary>
        Task<List<NamSTramTuTaoDto>> TimTramTuTaoAsync(string? search, double? lat, double? lng);
        /// <summary>Nhân viên tự tạo phiếu 5S cho mình ở trạm không được giao. Trả phiếu (hoặc phiếu đang mở của chính mình).</summary>
        Task<NamSPhieuDto> TuTaoAsync(NamSTuTaoRequestDto request);
        /// <summary>Xoá phiếu tự tạo chưa nộp của chính mình (kèm ảnh).</summary>
        Task<bool> XoaPhieuTuTaoAsync(long phieuId, CancellationToken ct = default);
        /// <summary>Người chấm xoá phiếu (mọi trạng thái) trong phạm vi, kèm ảnh và điểm.</summary>
        Task<bool> XoaPhieuAsync(long phieuId, CancellationToken ct = default);
        Task<NamSPhieuChiTietDto?> GetPhieuAsync(long phieuId);
        Task<List<NamSAnhDto>> UploadAnhAsync(long phieuId, int tieuChiId, IReadOnlyList<IFormFile> files, string? metaJson, CancellationToken ct = default);
        Task<bool> XoaAnhAsync(long anhId, CancellationToken ct = default);
        Task<bool> NopAsync(long phieuId, NamSNopRequestDto request);
        /// <summary>Ảnh gốc, hoặc ảnh thu nhỏ (~400 px) khi thuNho = true (ảnh cũ chưa có bản nhỏ thì tạo và lưu lại).</summary>
        Task<(Stream Stream, string ContentType)?> GetAnhAsync(long anhId, bool thuNho = false, CancellationToken ct = default);

        // Chấm
        Task<PaginatedResultDto<NamSPhieuDto>> GetPhieuListAsync(NamSPhieuQueryDto query);
        Task<NamSPhieuDto?> ChamAsync(long phieuId, NamSChamRequestDto request);
        /// <summary>Trả phiếu đã nộp / đã chấm về cho nhân viên chụp bổ sung (xoá điểm, giữ ảnh).</summary>
        Task<NamSPhieuDto?> TraPhieuAsync(long phieuId, NamSTraPhieuRequestDto request);
        /// <summary>Trạm trong phạm vi kèm kết quả 5S trong kỳ (bản đồ + dashboard).</summary>
        Task<List<NamSBanDoTramDto>> GetBanDoAsync(NamSPhieuQueryDto query);

        // Báo cáo
        Task<List<NamSTongHopDto>> GetTongHopAsync(NamSPhieuQueryDto query);
        Task<List<NamSTongHopNvDto>> GetTongHopNhanVienAsync(NamSPhieuQueryDto query);
        Task<byte[]> ExportAsync(NamSPhieuQueryDto query);
    }

    public class NamSValidationException : Exception
    {
        public NamSValidationException(string message) : base(message) { }
    }
}
