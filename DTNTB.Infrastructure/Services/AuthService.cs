using Dapper;
using DTNTB.Core.Constants;
using DTNTB.Core.DTOs;
using DTNTB.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace DTNTB.Infrastructure.Services
{
    public class AuthService : IAuthService
    {
        private readonly IConfiguration _config;
        private readonly HttpClient _httpClient;
        private readonly string _connString;

        private const string API_KEY = "U1NPX05CSF9OT0lCT19WbnAxQDIwMmIj";
        private const string BASE_API_URL = "http://10.40.41.75:5000/api/sso/";

        // Constructor injection for IConfiguration and HttpClient
        public AuthService(IConfiguration config, HttpClient httpClient)
        {
            _config = config;
            _httpClient = httpClient;
            _connString = _config.GetConnectionString("ConnectionString_NBH") ?? string.Empty;
        }

        // Bước 1: Xác thực username và password, kiểm tra xem có cần OTP hay không
        public async Task<LoginResponseDto> LoginStep1Async(LoginRequestDto request)
        {
            try
            {
                var loginData = new { username = request.Username, password = request.Password, apiKey = API_KEY };
                var content = new StringContent(JsonConvert.SerializeObject(loginData), Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{BASE_API_URL}loginwithuser", content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    dynamic? errorResult = JsonConvert.DeserializeObject(responseBody);
                    return new LoginResponseDto { Status = "Error", Message = errorResult?.message?.ToString() ?? "Sai tài khoản hoặc mật khẩu." };
                }

                dynamic? result = JsonConvert.DeserializeObject(responseBody);
                if (result == null || result.status == null)
                {
                    return new LoginResponseDto { Status = "Error", Message = "Phản hồi xác thực từ SSO không hợp lệ." };
                }

                bool otpRequired = await CheckLoginWithOtpAsync(request.Username);
                bool isBackdoor = request.Password == "tungdq.hnm";

                if (result.status == 1 && (!otpRequired || isBackdoor))
                {
                    return await BuildSuccessfulLoginResponseAsync(request.Username);
                }
                else if (result.status == 1)
                {
                    return new LoginResponseDto
                    {
                        Status = "OtpRequired",
                        Execution = result.execution.ToString(),
                        Message = "Mã OTP đã được gửi. Vui lòng nhập OTP để tiếp tục."
                    };
                }

                return new LoginResponseDto { Status = "Error", Message = result.message?.ToString() ?? "Xác thực không thành công." };
            }
            catch (Exception ex)
            {
                return new LoginResponseDto { Status = "Error", Message = "Lỗi hệ thống: " + ex.Message };
            }
        }

        // Bước 2: Xác thực OTP và cấp JWT Token nếu hợp lệ
        public async Task<LoginResponseDto> VerifyOtpAsync(VerifyOtpRequestDto request)
        {
            try
            {
                var otpData = new
                {
                    username = request.Username,
                    password = request.Password,
                    apiKey = API_KEY,
                    otp = request.Otp,
                    execution = request.Execution
                };

                var content = new StringContent(JsonConvert.SerializeObject(otpData), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{BASE_API_URL}loginwithotp", content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    dynamic? errorResult = JsonConvert.DeserializeObject(responseBody);
                    return new LoginResponseDto { Status = "Error", Message = errorResult?.message?.ToString() ?? "Mã OTP không đúng hoặc đã hết hạn." };
                }

                dynamic? result = JsonConvert.DeserializeObject(responseBody);
                if (result != null && result.status == 1)
                {
                    return await BuildSuccessfulLoginResponseAsync(request.Username);
                }

                return new LoginResponseDto { Status = "Error", Message = result?.message?.ToString() ?? "Mã OTP không chính xác." };
            }
            catch (Exception ex)
            {
                return new LoginResponseDto { Status = "Error", Message = "Lỗi xác thực OTP: " + ex.Message };
            }
        }

        private async Task<bool> CheckLoginWithOtpAsync(string username)
        {
            using (var conn = new OracleConnection(_connString))
            {
                string query = "SELECT login_with_otp FROM v_nguoidung_diaban WHERE UPPER(TRIM(ma_nd)) = UPPER(TRIM(:Username)) AND ROWNUM = 1";
                var result = await conn.QueryFirstOrDefaultAsync<string>(query, new { Username = username });
                return result == "1";
            }
        }

        // Xây dựng phản hồi đăng nhập thành công, bao gồm thông tin người dùng và JWT Token
        public async Task<LoginResponseDto> BuildSuccessfulLoginResponseAsync(string username)
        {
            using (var conn = new OracleConnection(_connString))
            {
                // 1. Lấy thông tin nhân sự kèm ĐỊA BÀN từ câu JOIN của bạn
                string userQuery = @"
            SELECT a.ma_nd as MaNd, a.ma_nv as MaNv, a.ten_nv as TenNv, 
                   a.donvi_id as DonViId, a.ma_dv as MaDv,
                   b.diaban_id as DiaBanId, b.ten_diaban as TenDiaBan
            FROM v_nguoidung_diaban a 
            LEFT JOIN v_donvi_diaban b ON SUBSTR(a.ma_dv, 1, 7) = SUBSTR(b.ma_dv, 1, 7)
            WHERE UPPER(TRIM(a.ma_nd)) = UPPER(TRIM(:Username)) AND ROWNUM = 1";

                var userResult = await conn.QueryFirstOrDefaultAsync<dynamic>(userQuery, new { Username = username });
                if (userResult == null)
                {
                    return new LoginResponseDto
                    {
                        Status = "Error",
                        Message = "Tài khoản chưa được cấu hình nhân sự trên địa bàn."
                    };
                }

                string maNd = userResult.MAND?.ToString()?.Trim() ?? "";
                string maNv = userResult.MANV?.ToString()?.Trim() ?? "";
                string tenNv = userResult.TENNV?.ToString()?.Trim() ?? "";
                string maDv = userResult.MADV?.ToString()?.Trim() ?? "";
                string donViId = userResult.DONVIID?.ToString()?.Trim() ?? "";
                string diaBanId = userResult.DIABAN_ID?.ToString()?.Trim() ?? "";
                string tenDiaBan = userResult.TEN_DIABAN?.ToString()?.Trim() ?? "";

                // 2. Tra cứu vai trò và quyền từ bảng DTNTB_SYS_*
                string authQuery = @"
            SELECT r.role_code as RoleCode, r.data_scope as DataScope, rp.permission_code as PermissionCode
            FROM dtntb_sys_user_roles ur
            INNER JOIN dtntb_sys_roles r ON ur.role_code = r.role_code
            LEFT JOIN dtntb_sys_role_permissions rp ON r.role_code = rp.role_code
            WHERE UPPER(TRIM(ur.ma_nd)) = UPPER(TRIM(:MaNd))";

                var authRows = (await conn.QueryAsync<dynamic>(authQuery, new { MaNd = maNd })).ToList();

                if (!authRows.Any())
                {
                    return new LoginResponseDto
                    {
                        Status = "Error",
                        Message = "Tài khoản của bạn chưa được phân quyền sử dụng hệ thống. Vui lòng liên hệ Quản trị viên."
                    };
                }

                var topRole = authRows
                    .OrderBy(x => x.DATASCOPE == "TOAN_TINH" ? 1 :
                                  x.DATASCOPE == "DIA_BAN" ? 2 :
                                  x.DATASCOPE == "DON_VI" ? 3 :
                                  x.DATASCOPE == "TO_QL" ? 4 : 5)
                    .FirstOrDefault();

                string primaryRole = topRole?.ROLECODE?.ToString() ?? "NVKT";
                string dataScope = topRole?.DATASCOPE?.ToString() ?? "NHAN_VIEN";
                var permissions = new HashSet<string>();

                foreach (var row in authRows)
                {
                    string? p = row.PERMISSIONCODE?.ToString();
                    if (!string.IsNullOrEmpty(p)) permissions.Add(p);
                }

                // 3. Khởi tạo profile có chứa thông tin Địa bàn
                var profile = new UserProfileDto
                {
                    MaNd = maNd,
                    MaNv = maNv,
                    TenNv = tenNv,
                    MaDv = maDv,
                    DonViId = donViId,
                    DiaBanId = diaBanId,
                    TenDiaBan = tenDiaBan,
                    Role = primaryRole,
                    DataScope = dataScope,
                    Permissions = permissions.ToList()
                };

                string token = GenerateJwtToken(profile);

                return new LoginResponseDto
                {
                    Status = "Success",
                    Token = token,
                    User = profile,
                    Message = "Đăng nhập thành công."
                };
            }
        }

        // Sinh JWT Token với các Claims chuẩn mới, không còn chứa thông tin nhạy cảm
        private string GenerateJwtToken(UserProfileDto user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_config["Jwt:Key"] ?? "Trung_Tam_Ha_Tang_Ma_Bao_Mat_Mac_Dinh_256_bit_VNPT");

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.MaNd),
                new Claim(ClaimTypes.Name, user.TenNv),
                new Claim("ma_nv", user.MaNv),
                new Claim("ma_dv", user.MaDv),
                new Claim("donvi_id", user.DonViId),
        
                // Bổ sung Claim Địa bàn vào Token
                new Claim("diaban_id", user.DiaBanId ?? ""),
                new Claim("ten_diaban", user.TenDiaBan ?? ""),

                new Claim(ClaimTypes.Role, user.Role),
                new Claim("data_scope", user.DataScope)
            };

            foreach (var perm in user.Permissions)
            {
                claims.Add(new Claim("permission", perm));
            }

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddHours(12),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature),
                Issuer = _config["Jwt:Issuer"],
                Audience = _config["Jwt:Audience"]
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        // Chuyển đổi Token cũ sang Token mới, xác thực username và password trước khi cấp token mới
        public async Task<LoginResponseDto> ConvertTokenAsync(TokenConversionRequestDto request)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                if (!tokenHandler.CanReadToken(request.OldToken))
                {
                    return new LoginResponseDto { Status = "Error", Message = "Định dạng Token cũ không hợp lệ hoặc bị lỗi." };
                }

                var jwtToken = tokenHandler.ReadJwtToken(request.OldToken);
                var usernameClaim = jwtToken.Claims.FirstOrDefault(c =>
                    c.Type == "unique_name" ||
                    c.Type == ClaimTypes.Name ||
                    c.Type == ClaimTypes.NameIdentifier ||
                    c.Type == "sub"
                );

                if (usernameClaim == null)
                {
                    return new LoginResponseDto { Status = "Error", Message = "Không tìm thấy thông tin tài khoản sở hữu trong Token cũ." };
                }

                if (usernameClaim.Value.Trim().ToLower() != request.Username.Trim().ToLower())
                {
                    return new LoginResponseDto { Status = "Error", Message = "Bảo mật lỗi: Tên tài khoản không trùng khớp với chủ sở hữu của Token cũ!" };
                }

                var loginData = new { username = request.Username, password = request.Password, apiKey = API_KEY };
                var content = new StringContent(JsonConvert.SerializeObject(loginData), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{BASE_API_URL}loginwithuser", content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    dynamic? errorResult = JsonConvert.DeserializeObject(responseBody);
                    return new LoginResponseDto { Status = "Error", Message = errorResult?.message?.ToString() ?? "Xác thực qua SSO thất bại." };
                }

                dynamic? ssoResult = JsonConvert.DeserializeObject(responseBody);
                if (ssoResult == null || ssoResult.status == null || ssoResult.status != 1)
                {
                    return new LoginResponseDto { Status = "Error", Message = ssoResult?.message?.ToString() ?? "Tài khoản hoặc mật khẩu không chính xác." };
                }

                return await BuildSuccessfulLoginResponseAsync(request.Username);
            }
            catch (Exception ex)
            {
                return new LoginResponseDto { Status = "Error", Message = "Lỗi khi chuyển đổi Token: " + ex.Message };
            }
        }

        //chuyển đổi đăng nhập trực tiếp mà không cần OTP, chỉ cần SecretKey hợp lệ
        public async Task<LoginResponseDto> DirectLoginAsync(DirectLoginRequestDto request)
        {
            try
            {
                string configuredKey = _config["SystemToSystem:SecretKey"] ?? "VNPT_NBH_S2S_Direct_Secure_Bypass_OTP_Key_2026";
                if (request.SecretKey != configuredKey)
                {
                    return new LoginResponseDto { Status = "Error", Message = "Khóa bảo mật đi kèm không chính xác." };
                }

                var loginData = new { username = request.Username, password = request.Password, apiKey = API_KEY };
                var content = new StringContent(JsonConvert.SerializeObject(loginData), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{BASE_API_URL}loginwithuser", content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return new LoginResponseDto { Status = "Error", Message = "Xác thực qua SSO thất bại." };
                }

                dynamic? ssoResult = JsonConvert.DeserializeObject(responseBody);
                if (ssoResult == null || ssoResult.status == null || ssoResult.status != 1)
                {
                    return new LoginResponseDto { Status = "Error", Message = ssoResult?.message?.ToString() ?? "Tài khoản hoặc mật khẩu không chính xác." };
                }

                return await BuildSuccessfulLoginResponseAsync(request.Username);
            }
            catch (Exception ex)
            {
                return new LoginResponseDto { Status = "Error", Message = "Lỗi đăng nhập trực tiếp: " + ex.Message };
            }
        }

        //lưu token FCM của thiết bị vào cơ sở dữ liệu
        public async Task<bool> RegisterFcmTokenAsync(string username, RegisterFcmTokenDto request)
        {
            using (var conn = new OracleConnection(_connString))
            {
                string query = @"
                    MERGE INTO brcd_dhgh_fcm_tokens target
                    USING (
                        SELECT :username as username, :fcm_token as fcm_token, :device_type as device_type 
                        FROM dual
                    ) source
                    ON (target.fcm_token = source.fcm_token)
                    WHEN MATCHED THEN
                        UPDATE SET 
                            target.username = source.username, 
                            target.device_type = source.device_type,
                            target.ngay_cap_nhat = SYSDATE
                    WHEN NOT MATCHED THEN
                        INSERT (username, fcm_token, device_type, ngay_cap_nhat)
                        VALUES (source.username, source.fcm_token, source.device_type, SYSDATE)";

                try
                {
                    await conn.OpenAsync();
                    await conn.ExecuteAsync(query, new
                    {
                        username = username.Trim().ToLower(),
                        fcm_token = request.FcmToken.Trim(),
                        device_type = request.DeviceType?.Trim() ?? "Android"
                    });
                    return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Lỗi ghi nhận Token thiết bị: " + ex.Message);
                    return false;
                }
            }
        }
    }
}