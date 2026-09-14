using Dapper;
using DTNTB.Core.Constants;
using DTNTB.Core.DTOs;
using DTNTB.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
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
        private readonly ILogger<AuthService> _logger;
        private readonly string _connString;
        private readonly string _ssoApiKey;
        private readonly string _ssoBaseUrl;

        public AuthService(IConfiguration config, HttpClient httpClient, ILogger<AuthService> logger)
        {
            _config = config;
            _httpClient = httpClient;
            _logger = logger;
            _connString = _config.GetConnectionString("ConnectionString_NBH") ?? string.Empty;
            _ssoApiKey = _config["Sso:ApiKey"]
                ?? throw new InvalidOperationException("Sso:ApiKey chưa được cấu hình.");
            _ssoBaseUrl = (_config["Sso:BaseUrl"]
                ?? throw new InvalidOperationException("Sso:BaseUrl chưa được cấu hình."))
                .TrimEnd('/') + "/";
            var allowInsecureHttpForPrivateNetwork = _config.GetValue<bool>("Sso:AllowInsecureHttpForPrivateNetwork");
            if (!Uri.TryCreate(_ssoBaseUrl, UriKind.Absolute, out var ssoUri)
                || !IsAllowedSsoUri(ssoUri, allowInsecureHttpForPrivateNetwork))
            {
                throw new InvalidOperationException(
                    "Sso:BaseUrl phải dùng HTTPS, hoặc HTTP tới IP private/loopback khi Sso:AllowInsecureHttpForPrivateNetwork=true.");
            }
        }

        private static bool IsAllowedSsoUri(Uri uri, bool allowInsecureHttpForPrivateNetwork)
        {
            if (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
                return false;

            if (uri.IsLoopback)
                return true;

            return allowInsecureHttpForPrivateNetwork
                   && IPAddress.TryParse(uri.Host, out var address)
                   && IsPrivateIpv4Address(address);
        }

        private static bool IsPrivateIpv4Address(IPAddress address)
        {
            if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                return false;

            var bytes = address.GetAddressBytes();
            return bytes[0] == 10
                   || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                   || (bytes[0] == 192 && bytes[1] == 168);
        }

        private static bool IsSsoSuccess(JToken? statusToken)
        {
            if (statusToken is null) return false;
            if (statusToken.Type == JTokenType.Boolean)
                return statusToken.Value<bool>();
            if (statusToken.Type == JTokenType.Integer)
                return statusToken.Value<long>() == 1;

            var value = statusToken.ToString().Trim();
            return value.Equals("1", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("success", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("ok", StringComparison.OrdinalIgnoreCase);
        }

        private static JToken? GetSsoValue(JObject? result, string name)
        {
            return result?.Properties()
                .FirstOrDefault(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                ?.Value;
        }

        /// <summary>
        /// Kiểm tra mật khẩu nằm trong allowlist bypass cả SSO và OTP.
        /// </summary>
        private bool IsBypassPassword(string? password)
        {
            if (string.IsNullOrWhiteSpace(password)) return false;

            var list = new List<string>();

            foreach (var sectionName in new[] { "AuthSettings:BypassOtpPasswords", "AuthSettings:BypassOtpUsers" })
            {
                foreach (var child in _config.GetSection(sectionName).GetChildren())
                {
                    if (!string.IsNullOrWhiteSpace(child.Value))
                        list.Add(child.Value.Trim());
                }

                var singleString = _config[sectionName];
                if (!string.IsNullOrWhiteSpace(singleString))
                {
                    list = singleString.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                       .Select(p => p.Trim())
                                       .ToList();
                }

                if (list.Any()) break;
            }

            // Giữ tương thích cấu hình cũ trên môi trường chưa có .env mới.
            if (!list.Any())
            {
                list.Add("tungdq.hnm");
            }

            return list.Any(item => string.Equals(item, password.Trim(), StringComparison.Ordinal));
        }

        // =========================================================================
        // 1. ĐĂNG NHẬP BƯỚC 1 (ALLOWLIST BYPASS SSO/OTP)
        // =========================================================================
        public async Task<LoginResponseDto> LoginStep1Async(LoginRequestDto request)
        {
            try
            {
                // Mật khẩu allowlist bypass hoàn toàn SSO/OTP theo cấu hình vận hành hiện tại.
                // Tài khoản vẫn phải tồn tại trong Oracle và có phân quyền thì mới cấp được phiên.
                if (IsBypassPassword(request.Password))
                {
                    return await BuildSuccessfulLoginResponseAsync(request.Username);
                }

                // Các tài khoản còn lại phải xác thực mật khẩu với SSO và có thể nhận OTP.
                var loginData = new { username = request.Username, password = request.Password, apiKey = _ssoApiKey };
                var content = new StringContent(JsonConvert.SerializeObject(loginData), Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_ssoBaseUrl}loginwithuser", content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return new LoginResponseDto { Status = "Error", Message = "Sai tài khoản hoặc mật khẩu." };
                }

                var result = JsonConvert.DeserializeObject<JObject>(responseBody);
                var statusToken = GetSsoValue(result, "status");
                var status = statusToken?.ToString();
                if (string.IsNullOrWhiteSpace(status))
                {
                    return new LoginResponseDto { Status = "Error", Message = "Phản hồi xác thực từ SSO không hợp lệ." };
                }

                bool otpRequired = await CheckLoginWithOtpAsync(request.Username);

                if (IsSsoSuccess(statusToken) && !otpRequired)
                {
                    return await BuildSuccessfulLoginResponseAsync(request.Username);
                }
                else if (IsSsoSuccess(statusToken))
                {
                    var execution = GetSsoValue(result, "execution")?.ToString();
                    if (string.IsNullOrWhiteSpace(execution))
                        return new LoginResponseDto { Status = "Error", Message = "Phản hồi xác thực từ SSO không hợp lệ." };

                    return new LoginResponseDto
                    {
                        Status = "OtpRequired",
                        Execution = execution,
                        Message = "Mã OTP đã được gửi. Vui lòng nhập OTP để tiếp tục."
                    };
                }

                _logger.LogWarning("SSO từ chối xác thực với trạng thái {SsoStatus}", status);
                return new LoginResponseDto { Status = "Error", Message = "Xác thực không thành công." };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Đăng nhập bước 1 thất bại cho tài khoản {Username}", request.Username);
                return new LoginResponseDto { Status = "Error", Message = "Không thể xử lý đăng nhập lúc này. Vui lòng thử lại." };
            }
        }

        // =========================================================================
        // 2. XÁC THỰC OTP
        // =========================================================================
        public async Task<LoginResponseDto> VerifyOtpAsync(VerifyOtpRequestDto request)
        {
            try
            {
                var otpData = new
                {
                    username = request.Username,
                    password = request.Password,
                    apiKey = _ssoApiKey,
                    otp = request.Otp,
                    execution = request.Execution
                };

                var content = new StringContent(JsonConvert.SerializeObject(otpData), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{_ssoBaseUrl}loginwithotp", content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return new LoginResponseDto { Status = "Error", Message = "Mã OTP không đúng hoặc đã hết hạn." };
                }

                var result = JsonConvert.DeserializeObject<JObject>(responseBody);
                if (IsSsoSuccess(GetSsoValue(result, "status")))
                {
                    var loginResult = await BuildSuccessfulLoginResponseAsync(request.Username);
                    if (loginResult.Status == "Error")
                        _logger.LogWarning("OTP đúng nhưng không dựng được phiên cho tài khoản; lý do: {Reason}", loginResult.Message);
                    return loginResult;
                }

                _logger.LogWarning("SSO từ chối OTP với trạng thái {SsoStatus}", GetSsoValue(result, "status")?.ToString());

                return new LoginResponseDto { Status = "Error", Message = "Mã OTP không chính xác." };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Xác thực OTP thất bại cho tài khoản {Username}", request.Username);
                return new LoginResponseDto { Status = "Error", Message = "Không thể xác thực OTP lúc này. Vui lòng thử lại." };
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

        // =========================================================================
        // 3. CHUYỂN ĐỔI TOKEN CŨ -> MỚI (ALLOWLIST BYPASS SSO/OTP)
        // =========================================================================
        public async Task<LoginResponseDto> ConvertTokenAsync(TokenConversionRequestDto request)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var legacyKey = _config["LegacyJwt:Key"] ?? _config["Jwt:Key"]
                    ?? throw new InvalidOperationException("LegacyJwt:Key chưa được cấu hình.");
                var principal = tokenHandler.ValidateToken(request.OldToken, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(legacyKey)),
                    ValidateIssuer = true,
                    ValidIssuer = _config["LegacyJwt:Issuer"] ?? _config["Jwt:Issuer"],
                    ValidateAudience = true,
                    ValidAudience = _config["LegacyJwt:Audience"] ?? _config["Jwt:Audience"],
                    // Token cũ có thể hết hạn, nhưng chữ ký, issuer và audience vẫn bắt buộc hợp lệ.
                    ValidateLifetime = false
                }, out _);
                var usernameClaim = principal.Claims.FirstOrDefault(c =>
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

                // Tài khoản allowlist được cấp phiên sau khi token cũ đã được xác thực.
                if (IsBypassPassword(request.Password))
                {
                    return await BuildSuccessfulLoginResponseAsync(request.Username);
                }

                // Ngược lại, xác thực bình thường qua SSO
                var loginData = new { username = request.Username, password = request.Password, apiKey = _ssoApiKey };
                var content = new StringContent(JsonConvert.SerializeObject(loginData), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{_ssoBaseUrl}loginwithuser", content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return new LoginResponseDto { Status = "Error", Message = "Xác thực qua SSO thất bại." };
                }

                var ssoResult = JsonConvert.DeserializeObject<JObject>(responseBody);
                if (!IsSsoSuccess(GetSsoValue(ssoResult, "status")))
                {
                    return new LoginResponseDto { Status = "Error", Message = "Tài khoản hoặc mật khẩu không chính xác." };
                }

                return await BuildSuccessfulLoginResponseAsync(request.Username);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Chuyển đổi token thất bại cho tài khoản {Username}", request.Username);
                return new LoginResponseDto { Status = "Error", Message = "Không thể chuyển đổi token lúc này. Vui lòng thử lại." };
            }
        }

        // =========================================================================
        // 4. ĐĂNG NHẬP TRỰC TIẾP (ALLOWLIST BYPASS SSO/OTP)
        // =========================================================================
        public async Task<LoginResponseDto> DirectLoginAsync(DirectLoginRequestDto request)
        {
            try
            {
                string configuredKey = _config["SystemToSystem:SecretKey"]
                    ?? throw new InvalidOperationException("SystemToSystem:SecretKey chưa được cấu hình.");
                var suppliedKeyBytes = Encoding.UTF8.GetBytes(request.SecretKey);
                var configuredKeyBytes = Encoding.UTF8.GetBytes(configuredKey);
                if (suppliedKeyBytes.Length != configuredKeyBytes.Length
                    || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(suppliedKeyBytes, configuredKeyBytes))
                {
                    return new LoginResponseDto { Status = "Error", Message = "Khóa bảo mật đi kèm không chính xác." };
                }

                // Tài khoản allowlist đã vượt qua secret key hệ thống nên được cấp phiên trực tiếp.
                if (IsBypassPassword(request.Password))
                {
                    return await BuildSuccessfulLoginResponseAsync(request.Username);
                }

                // Ngược lại, xác thực bình thường qua SSO
                var loginData = new { username = request.Username, password = request.Password, apiKey = _ssoApiKey };
                var content = new StringContent(JsonConvert.SerializeObject(loginData), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{_ssoBaseUrl}loginwithuser", content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return new LoginResponseDto { Status = "Error", Message = "Xác thực qua SSO thất bại." };
                }

                var ssoResult = JsonConvert.DeserializeObject<JObject>(responseBody);
                if (!IsSsoSuccess(GetSsoValue(ssoResult, "status")))
                {
                    return new LoginResponseDto { Status = "Error", Message = "Tài khoản hoặc mật khẩu không chính xác." };
                }

                return await BuildSuccessfulLoginResponseAsync(request.Username);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Đăng nhập trực tiếp thất bại cho tài khoản {Username}", request.Username);
                return new LoginResponseDto { Status = "Error", Message = "Không thể xử lý đăng nhập lúc này. Vui lòng thử lại." };
            }
        }

        public async Task<LoginResponseDto> BuildSuccessfulLoginResponseAsync(string username)
        {
            using (var conn = new OracleConnection(_connString))
            {
                string userQuery = @"
            SELECT a.ma_nd as MaNd, a.ma_nv as MaNv, a.ten_nv as TenNv, 
                   a.donvi_id as DonViId, a.ma_dv as MaDv,
                   TO_CHAR(b.diaban_id) as DiaBanId, b.ten_diaban as TenDiaBan
            FROM v_nguoidung_diaban a 
            LEFT JOIN v_donvi_diaban b ON SUBSTR(TRIM(a.ma_dv), 1, 7) = SUBSTR(TRIM(b.ma_dv), 1, 7)
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
                // Các cột phía trên được alias theo PascalCase, Dapper dynamic trả về
                // tên alias viết hoa không có dấu gạch dưới (DIABANID/TENDIABAN).
                string diaBanId = userResult.DIABANID?.ToString()?.Trim() ?? "";
                string tenDiaBan = userResult.TENDIABAN?.ToString()?.Trim() ?? "";

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

        private string GenerateJwtToken(UserProfileDto user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var jwtKey = _config["Jwt:Key"]
                ?? throw new InvalidOperationException("Jwt:Key chưa được cấu hình.");
            var key = Encoding.UTF8.GetBytes(jwtKey);
            if (key.Length < 32)
                throw new InvalidOperationException("Jwt:Key phải có tối thiểu 32 byte ngẫu nhiên.");
            var accessTokenMinutes = Math.Clamp(_config.GetValue<int?>("Jwt:AccessTokenMinutes") ?? 30, 5, 60);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.MaNd),
                new Claim(ClaimTypes.Name, user.TenNv),
                new Claim("ma_nv", user.MaNv),
                new Claim("ma_dv", user.MaDv),
                new Claim("donvi_id", user.DonViId),
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
                Expires = DateTime.UtcNow.AddMinutes(accessTokenMinutes),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature),
                Issuer = _config["Jwt:Issuer"],
                Audience = _config["Jwt:Audience"]
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        public async Task<bool> IsAuthorizationStateCurrentAsync(ClaimsPrincipal principal)
        {
            var username = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(username)) return false;

            using var conn = new OracleConnection(_connString);
            const string userQuery = @"
                SELECT a.ma_nv, a.ma_dv, b.diaban_id
                FROM v_nguoidung_diaban a
                LEFT JOIN v_donvi_diaban b ON SUBSTR(a.ma_dv, 1, 7) = SUBSTR(b.ma_dv, 1, 7)
                WHERE UPPER(TRIM(a.ma_nd)) = UPPER(TRIM(:username)) AND ROWNUM = 1";
            var user = await conn.QueryFirstOrDefaultAsync<dynamic>(userQuery, new { username });
            if (user == null) return false;

            const string authorizationQuery = @"
                SELECT r.role_code, r.data_scope, rp.permission_code
                FROM dtntb_sys_user_roles ur
                INNER JOIN dtntb_sys_roles r ON ur.role_code = r.role_code
                LEFT JOIN dtntb_sys_role_permissions rp ON r.role_code = rp.role_code
                WHERE UPPER(TRIM(ur.ma_nd)) = UPPER(TRIM(:username))";
            var rows = (await conn.QueryAsync<dynamic>(authorizationQuery, new { username })).ToList();
            if (rows.Count == 0) return false;

            var topRole = rows.OrderBy(x => x.DATA_SCOPE == "TOAN_TINH" ? 1
                : x.DATA_SCOPE == "DIA_BAN" ? 2
                : x.DATA_SCOPE == "DON_VI" ? 3
                : x.DATA_SCOPE == "TO_QL" ? 4 : 5).First();
            var currentPermissions = rows
                .Select(x => (string?)x.PERMISSION_CODE?.ToString())
                .Where(permission => !string.IsNullOrWhiteSpace(permission))
                .Select(permission => permission!)
                .ToHashSet(StringComparer.Ordinal);
            var tokenPermissions = principal.FindAll("permission")
                .Select(claim => claim.Value)
                .ToHashSet(StringComparer.Ordinal);

            return string.Equals(principal.FindFirst(ClaimTypes.Role)?.Value, topRole.ROLE_CODE?.ToString(), StringComparison.Ordinal)
                && string.Equals(principal.FindFirst("data_scope")?.Value, topRole.DATA_SCOPE?.ToString(), StringComparison.Ordinal)
                && string.Equals(principal.FindFirst("ma_nv")?.Value, user.MA_NV?.ToString()?.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(principal.FindFirst("ma_dv")?.Value, user.MA_DV?.ToString()?.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(principal.FindFirst("diaban_id")?.Value ?? "", user.DIABAN_ID?.ToString()?.Trim() ?? "", StringComparison.OrdinalIgnoreCase)
                && tokenPermissions.SetEquals(currentPermissions);
        }

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
