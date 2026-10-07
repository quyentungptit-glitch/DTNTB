using DTNTB.Core.DTOs;
using DTNTB.Core.Interfaces;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.Tasks;

namespace DTNTB.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IAuthService authService, ILogger<AuthController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        [HttpPost("login")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto model)
        {
            if (model == null || string.IsNullOrEmpty(model.Username) || string.IsNullOrEmpty(model.Password))
            {
                return BadRequest(new { message = "Vui lòng cung cấp đầy đủ thông tin đăng nhập." });
            }

            var response = await _authService.LoginStep1Async(model);
            if (response.Status == "Error")
            {
                return BadRequest(new { message = response.Message });
            }

            return Ok(response);
        }

        [HttpPost("verify-otp")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequestDto model)
        {
            if (model == null || string.IsNullOrEmpty(model.Otp) || string.IsNullOrEmpty(model.Execution))
            {
                return BadRequest(new { message = "Mã OTP hoặc mã phiên xác thực không hợp lệ." });
            }

            var response = await _authService.VerifyOtpAsync(model);
            if (response.Status == "Error")
            {
                return BadRequest(new { message = response.Message });
            }

            return Ok(response);
        }

        [HttpPost("convert-token")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> ConvertToken([FromBody] TokenConversionRequestDto model)
        {
            if (model == null || string.IsNullOrEmpty(model.Username) ||
                string.IsNullOrEmpty(model.OldToken) || string.IsNullOrEmpty(model.SecretKey))
            {
                return BadRequest(new { message = "Vui lòng truyền đầy đủ tham số yêu cầu." });
            }

            // Gọi chuyển tiếp nghiệp vụ sang lớp dịch vụ theo đúng chuẩn kiến trúc sạch
            var response = await _authService.ConvertTokenAsync(model);
            if (response.Status == "Error")
            {
                return BadRequest(new { message = response.Message });
            }

            return Ok(response);
        }

        [HttpPost("direct-login")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> DirectLogin([FromBody] DirectLoginRequestDto model)
        {
            if (model == null || string.IsNullOrEmpty(model.Username) ||
                string.IsNullOrEmpty(model.Password) || string.IsNullOrEmpty(model.SecretKey))
            {
                return BadRequest(new { message = "Vui lòng truyền đầy đủ tham số yêu cầu." });
            }

            // Gọi chuyển tiếp nghiệp vụ trực tiếp xuống tầng dịch vụ xử lý [1.1.1]
            var response = await _authService.DirectLoginAsync(model);
            if (response.Status == "Error")
            {
                return BadRequest(new { message = response.Message });
            }

            return Ok(response);
        }

        [Authorize]
        [HttpGet("session")]
        public IActionResult ValidateSession()
        {
            return Ok(new { authenticated = true });
        }

        [Authorize]
        [HttpGet("profile")]
        public async Task<IActionResult> GetCurrentProfile()
        {
            var username = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var profile = await _authService.GetCurrentUserProfileAsync(username ?? string.Empty);
            if (profile == null) return NotFound(new { message = "Không tìm thấy hồ sơ người dùng." });
            return Ok(profile);
        }

        [Authorize]
        [HttpPost("logout")]
        public IActionResult Logout()
        {
            Response.Cookies.Delete("dtntb_access_token", new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/"
            });
            return Ok(new { success = true });
        }

        [Authorize]
        [HttpGet("fcm-notification-preference")]
        public async Task<IActionResult> GetFcmNotificationPreference()
        {
            var username = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            return Ok(await _authService.GetFcmNotificationPreferenceAsync(username ?? string.Empty));
        }


        // Token thiết bị chỉ được dùng tức thời để Firebase subscribe vào topic.
        // Không lưu token tại CSDL: worker gửi hoàn toàn theo Firebase Topic.
        [Authorize]
        [HttpPost("register-fcm-token")]
        public async Task<IActionResult> RegisterFcmToken([FromBody] RegisterFcmTokenDto model)
        {
            string username = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(username) || model == null || string.IsNullOrWhiteSpace(model.FcmToken))
            {
                return BadRequest(new { message = "Dữ liệu đăng ký token không hợp lệ." });
            }

            string topic = BuildPendingTicketTopic(username);
            try
            {
                await FirebaseMessaging.DefaultInstance.SubscribeToTopicAsync(new[] { model.FcmToken.Trim() }, topic);
                if (!await _authService.SetFcmNotificationPreferenceAsync(username, true))
                {
                    return NotFound(new { message = "Không tìm thấy phân quyền người dùng để lưu lựa chọn thông báo." });
                }
                return Ok(new { success = true, topic, message = "Đã bật thông báo trên thiết bị này." });
            }
            catch (TokenResponseException ex)
            {
                // Lỗi này xảy ra trước khi FCM xử lý token thiết bị: service account
                // không đổi được JWT sang OAuth access token tại Google.
                _logger.LogError(ex,
                    "Firebase service account không lấy được OAuth token khi subscribe topic {Topic} cho {Username}.",
                    topic, username);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    message = "Máy chủ Firebase chưa xác thực được service account. " +
                              "Hãy kiểm tra khóa service account và thời gian máy chủ."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Không subscribe được FCM token vào topic {Topic} cho {Username}.", topic, username);
                if (FindException<TokenResponseException>(ex) is not null)
                {
                    return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                    {
                        message = "Firebase chưa xác thực được service account với Google. " +
                                  "Kiểm tra lại JSON key và kết nối OAuth của máy chủ."
                    });
                }

                var errorCode = ex is FirebaseMessagingException firebaseException
                    && !string.IsNullOrWhiteSpace(firebaseException.MessagingErrorCode.ToString())
                    ? firebaseException.MessagingErrorCode.ToString()
                    : ex.GetType().Name;
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { message = $"Firebase chưa subscribe được topic (mã: {errorCode})." });
            }
        }

        [Authorize]
        [HttpPost("unregister-fcm-token")]
        public async Task<IActionResult> UnregisterFcmToken([FromBody] RegisterFcmTokenDto model)
        {
            string username = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(username) || model == null || string.IsNullOrWhiteSpace(model.FcmToken))
            {
                return BadRequest(new { message = "Dữ liệu hủy token không hợp lệ." });
            }

            string topic = BuildPendingTicketTopic(username);
            try
            {
                await FirebaseMessaging.DefaultInstance.UnsubscribeFromTopicAsync(new[] { model.FcmToken.Trim() }, topic);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không unsubscribe được FCM token khỏi topic {Topic} cho {Username}.", topic, username);
                // Không có dữ liệu token cục bộ cần xử lý; việc hủy topic được Firebase quản lý.
            }

            return Ok(new { success = true });
        }

        [Authorize]
        [HttpPost("disable-fcm-notifications")]
        public async Task<IActionResult> DisableFcmNotifications([FromBody] RegisterFcmTokenDto model)
        {
            string username = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(username) || model == null)
            {
                return BadRequest(new { message = "Dữ liệu tắt thông báo không hợp lệ." });
            }

            string topic = BuildPendingTicketTopic(username);
            try
            {
                if (!string.IsNullOrWhiteSpace(model.FcmToken))
                {
                    await FirebaseMessaging.DefaultInstance.UnsubscribeFromTopicAsync(new[] { model.FcmToken.Trim() }, topic);
                }
                await _authService.SetFcmNotificationPreferenceAsync(username, false);
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không tắt được FCM topic {Topic} cho {Username}.", topic, username);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Không thể tắt thông báo lúc này." });
            }
        }

        private static string BuildPendingTicketTopic(string username)
        {
            return "phieu_nguy_co_ton_" + username.Trim().ToLowerInvariant().Replace('.', '_');
        }

        private static TException? FindException<TException>(Exception exception)
            where TException : Exception
        {
            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                if (current is TException matched) return matched;
            }

            return null;
        }


        // API TEST: BẮN THỬ THÔNG BÁO PUSH LÊN TOPIC NGAY LẬP TỨC ĐỂ KIỂM TRA ĐƯỜNG TRUYỀN GOOGLE [INDEX]
        //[AllowAnonymous] // Cho phép gọi không cần đăng nhập để dễ test
        //[HttpPost("test-push-topic")]
        //public async Task<IActionResult> TestPushTopic([FromQuery] string topic, [FromQuery] int count)
        //{
        //    if (string.IsNullOrEmpty(topic))
        //    {
        //        return BadRequest(new { message = "Vui lòng nhập tên Topic cần test." });
        //    }

        //    try
        //    {
        //        var message = new FirebaseAdmin.Messaging.Message()
        //        {
        //            Topic = topic,
        //            Notification = new FirebaseAdmin.Messaging.Notification()
        //            {
        //                Title = "🔔 TEST PUSH NOTIFICATION",
        //                Body = $"Đây là thông báo test kết nối hệ thống. Bạn đang tồn {count} phiếu!"
        //            },
        //            Data = new Dictionary<string, string>()
        //            {
        //                { "click_action", "open_default_list" }
        //            }
        //        };

        //        // Thực thi bắn thử ngay lập tức lên Google Firebase [INDEX]
        //        string response = await FirebaseAdmin.Messaging.FirebaseMessaging.DefaultInstance.SendAsync(message);
        //        return Ok(new { success = true, response = response, message = "Đường truyền Google thông suốt. Bắn test thành công!" });
        //    }
        //    catch (Exception ex)
        //    {
        //        return BadRequest(new { message = "Lỗi kết nối Firebase: " + ex.Message });
        //    }
        //}
    }
}
