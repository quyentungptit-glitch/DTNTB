using System.Collections.Generic;

namespace DTNTB.Core.DTOs
{
    public class LoginRequestDto
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class VerifyOtpRequestDto
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Otp { get; set; } = string.Empty;
        public string Execution { get; set; } = string.Empty;
    }

    public class LoginResponseDto
    {
        public string Status { get; set; } = string.Empty; // "Success", "OtpRequired", "Error"
        public string Message { get; set; } = string.Empty;
        public string? Token { get; set; }
        public string? Execution { get; set; }
        public UserProfileDto? User { get; set; }
    }

    public class UserProfileDto
    {
        public string MaNd { get; set; } = string.Empty;
        public string MaNv { get; set; } = string.Empty;
        public string TenNv { get; set; } = string.Empty;
        public string MaDv { get; set; } = string.Empty;
        public string DonViId { get; set; } = string.Empty;

        // Bổ sung thông tin Địa bàn
        public string DiaBanId { get; set; } = string.Empty;
        public string TenDiaBan { get; set; } = string.Empty;

        public string Role { get; set; } = "NVKT";
        public string DataScope { get; set; } = "NHAN_VIEN";
        public List<string> Permissions { get; set; } = new();
    }

    public class TokenConversionRequestDto
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string OldToken { get; set; } = string.Empty;
    }

    public class DirectLoginRequestDto
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;
    }

    public class RegisterFcmTokenDto
    {
        public string FcmToken { get; set; } = string.Empty;
        public string DeviceType { get; set; } = string.Empty;
    }
}