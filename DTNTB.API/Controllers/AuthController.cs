using DTNTB.Core.DTOs;
using DTNTB.Core.Interfaces;
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

        public AuthController(IAuthService authService)
        {
            _authService = authService;
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

        //[HttpPost("convert-token")]
        //[EnableRateLimiting("auth")]
        //public async Task<IActionResult> ConvertToken([FromBody] TokenConversionRequestDto model)
        //{
        //    if (model == null || string.IsNullOrEmpty(model.Username) ||
        //        string.IsNullOrEmpty(model.Password) || string.IsNullOrEmpty(model.OldToken))
        //    {
        //        return BadRequest(new { message = "Vui lòng truyền đầy đủ tham số yêu cầu." });
        //    }

        //    // Gọi chuyển tiếp nghiệp vụ sang lớp dịch vụ theo đúng chuẩn kiến trúc sạch
        //    var response = await _authService.ConvertTokenAsync(model);
        //    if (response.Status == "Error")
        //    {
        //        return BadRequest(new { message = response.Message });
        //    }

        //    return Ok(response);
        //}

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
        [HttpPost("logout")]
        public IActionResult Logout()
        {
            return Ok(new { success = true });
        }


        // API 5: TIẾP NHẬN ĐĂNG KÝ TOKEN THIẾT BỊ TỪ ĐIỆN THOẠI DI ĐỘNG (YÊU CẦU ĐÃ ĐĂNG NHẬP) [INDEX]
        //[Authorize]
        //[HttpPost("register-fcm-token")]
        //public async Task<IActionResult> RegisterFcmToken([FromBody] RegisterFcmTokenDto model)
        //{
        //    // Trích xuất username an toàn từ JWT Token của người gửi
        //    string username = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";

        //    if (string.IsNullOrEmpty(username) || model == null || string.IsNullOrEmpty(model.FcmToken))
        //    {
        //        return BadRequest(new { message = "Dữ liệu đăng ký Token không hợp lệ." });
        //    }

        //    var success = await _authService.RegisterFcmTokenAsync(username, model);
        //    if (!success)
        //    {
        //        return BadRequest(new { message = "Không thể ghi nhận thiết bị này trên cơ sở dữ liệu." });
        //    }

        //    return Ok(new { success = true, message = "Đăng ký Token thiết bị nhận thông báo thành công." });
        //}


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
