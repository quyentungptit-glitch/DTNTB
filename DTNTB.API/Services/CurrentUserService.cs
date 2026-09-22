using DTNTB.Core.Constants;
using DTNTB.Core.Interfaces;
using Microsoft.AspNetCore.Http;
using System.Linq;
using System.Security.Claims;

namespace DTNTB.Infrastructure.Services
{
    public class CurrentUserService : ICurrentUserService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CurrentUserService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

        public string? Username => User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        public string? TenNv => User?.FindFirst(ClaimTypes.Name)?.Value;
        public string? MaNv => User?.FindFirst("ma_nv")?.Value;
        public string? MaDv => User?.FindFirst("ma_dv")?.Value;
        public string? DiaBanId => User?.FindFirst("diaban_id")?.Value;
        public string Role => User?.FindFirst(ClaimTypes.Role)?.Value ?? "NVKT";

        public string? MaDv7 => string.IsNullOrEmpty(MaDv)
            ? ""
            : (MaDv.Length > 7 ? MaDv.Substring(0, 7) : MaDv);

        public string? MaDv11 => string.IsNullOrEmpty(MaDv)
            ? ""
            : (MaDv.Length > 11 ? MaDv.Substring(0, 11) : MaDv);

        public bool HasPermission(string permission)
        {
            return User?.Claims.Any(c => c.Type == "permission" && c.Value == permission) ?? false;
        }

        public UserDataScopeLevel ScopeLevel
        {
            get
            {
                var scope = User?.FindFirst("data_scope")?.Value;
                return scope switch
                {
                    "TOAN_TINH" or "GLOBAL" => UserDataScopeLevel.ToanTinh,
                    "DIA_BAN" or "AREA" => UserDataScopeLevel.DiaBan,
                    "DON_VI" or "UNIT" => UserDataScopeLevel.DonVi,
                    "TO_QL" or "TEAM" => UserDataScopeLevel.ToQuanLy,
                    _ => UserDataScopeLevel.NhanVien
                };
            }
        }
    }
}
