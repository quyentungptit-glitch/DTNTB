using System.Threading.Tasks;
using DTNTB.Core.DTOs;
using System.Security.Claims;

namespace DTNTB.Core.Interfaces
{
    public interface IAuthService
    {
        Task<CurrentUserProfileDto?> GetCurrentUserProfileAsync(string username);
        Task<LoginResponseDto> LoginStep1Async(LoginRequestDto request);
        Task<LoginResponseDto> VerifyOtpAsync(VerifyOtpRequestDto request);
        Task<LoginResponseDto> ConvertTokenAsync(TokenConversionRequestDto request);
        Task<LoginResponseDto> DirectLoginAsync(DirectLoginRequestDto request);
        Task<bool> IsAuthorizationStateCurrentAsync(ClaimsPrincipal principal);

    }
}
