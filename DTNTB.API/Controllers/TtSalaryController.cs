using System.Net.Http.Headers;
using DTNTB.Core.DTOs;
using DTNTB.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DTNTB.API.Controllers;

/// <summary>
/// API gateway công khai, có kiểm soát, cho hệ thống lương nội bộ.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class TtSalaryController : ControllerBase
{
    private readonly ITtSalaryService _ttSalaryService;

    public TtSalaryController(ITtSalaryService ttSalaryService)
    {
        _ttSalaryService = ttSalaryService;
    }

    /// <summary>
    /// Đăng nhập vào hệ thống lương bằng apiKey, username và password.
    /// Endpoint này tự cấp token trong entity, không yêu cầu Authorization header.
    /// </summary>
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(
        [FromBody] TtSalaryLoginRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        return Relay(await _ttSalaryService.LoginAsync(request, cancellationToken));
    }

    /// <summary>
    /// Lấy lương cá nhân theo tháng. Dùng Bearer token nhận từ endpoint login.
    /// </summary>
    [HttpGet("luong/nv")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> GetEmployeeSalary(
        [FromQuery] int month,
        CancellationToken cancellationToken)
    {
        var userToken = ExtractBearerToken();
        return Relay(await _ttSalaryService.GetEmployeeSalaryAsync(month, userToken, cancellationToken));
    }

    private string? ExtractBearerToken()
    {
        return AuthenticationHeaderValue.TryParse(Request.Headers.Authorization.ToString(), out var authorization)
               && authorization is not null
               && string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
               && !string.IsNullOrWhiteSpace(authorization.Parameter)
            ? authorization.Parameter
            : null;
    }

    private static ContentResult Relay(TtSalaryRelayResponseDto response) => new()
    {
        StatusCode = response.StatusCode,
        ContentType = response.ContentType,
        Content = response.Content
    };
}
