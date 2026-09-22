using DTNTB.Core.Constants;

namespace DTNTB.Core.Interfaces
{
    public interface ICurrentUserService
    {
        string? Username { get; }
        string? MaNv { get; }
        string? TenNv { get; }
        string? MaDv { get; }
        string? MaDv7 { get; }
        string? MaDv11 { get; }
        string? DiaBanId { get; }   // <-- THÊM MỚI
        string Role { get; }
        UserDataScopeLevel ScopeLevel { get; }
        bool HasPermission(string permission);
    }
}
