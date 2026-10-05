using System.Collections.Generic;

namespace DTNTB.Core.DTOs
{
    public class LoginRequestDto
    {
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(100)]
        public string Username { get; set; } = string.Empty;
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(256)]
        public string Password { get; set; } = string.Empty;
    }

    public class VerifyOtpRequestDto
    {
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(100)]
        public string Username { get; set; } = string.Empty;
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(256)]
        public string Password { get; set; } = string.Empty;
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.RegularExpression("^[0-9]{6}$")]
        public string Otp { get; set; } = string.Empty;
        // Execution là mã phiên opaque do SSO phát hành; có thể dài hơn 2 KB.
        // Giới hạn 8 KB đủ cho token phiên hợp lệ nhưng vẫn chặn payload bất thường.
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(8192)]
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

    // Dữ liệu hồ sơ phục vụ hiển thị trên từng form. Không dùng DTO này để
    // quyết định quyền hay phạm vi dữ liệu; các quyết định đó luôn nằm ở JWT/API.
    public class CurrentUserProfileDto
    {
        public string MaNd { get; set; } = string.Empty;
        public string MaNv { get; set; } = string.Empty;
        public string TenNv { get; set; } = string.Empty;
        public string MaDv { get; set; } = string.Empty;
        public string TenDv { get; set; } = string.Empty;
        public string DonViId { get; set; } = string.Empty;
        public string DiaBanId { get; set; } = string.Empty;
        public string TenDiaBan { get; set; } = string.Empty;
    }

    public class TokenConversionRequestDto
    {
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(100)]
        public string Username { get; set; } = string.Empty;
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(256)]
        public string Password { get; set; } = string.Empty;
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(8192)]
        public string OldToken { get; set; } = string.Empty;
    }

    public class DirectLoginRequestDto
    {
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(100)]
        public string Username { get; set; } = string.Empty;
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(256)]
        public string Password { get; set; } = string.Empty;
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(512)]
        public string SecretKey { get; set; } = string.Empty;
    }

    public class RegisterFcmTokenDto
    {
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(4096)]
        public string FcmToken { get; set; } = string.Empty;
    }
}
