using System.Threading.Tasks;
using DTNTB.Core.DTOs;

namespace DTNTB.Core.Interfaces
{
    public interface IAuthService
    {
        Task<LoginResponseDto> LoginStep1Async(LoginRequestDto request);
        Task<LoginResponseDto> VerifyOtpAsync(VerifyOtpRequestDto request);
        Task<LoginResponseDto> ConvertTokenAsync(TokenConversionRequestDto request);
        Task<LoginResponseDto> DirectLoginAsync(DirectLoginRequestDto request);

        // Đăng ký Token thiết bị FCM
        Task<bool> RegisterFcmTokenAsync(string username, RegisterFcmTokenDto request);
    }
}