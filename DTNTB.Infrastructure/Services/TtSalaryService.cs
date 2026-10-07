using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DTNTB.Core.DTOs;
using DTNTB.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DTNTB.Infrastructure.Services;

/// <summary>
/// Gateway kết nối hệ thống lương nội bộ. Toàn bộ URL/token upstream được giữ
/// trong cấu hình máy chủ; caller chỉ nhận response đã relay.
/// </summary>
public sealed class TtSalaryService : ITtSalaryService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TtSalaryService> _logger;

    public TtSalaryService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<TtSalaryService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<TtSalaryRelayResponseDto> LoginAsync(
        TtSalaryLoginRequestDto request,
        CancellationToken cancellationToken = default)
    {
        using var upstreamRequest = CreateUpstreamRequest(HttpMethod.Post, GetConfiguredPath("LoginPath", "auth/loginapp"));
        if (upstreamRequest is null) return UpstreamConfigurationError();

        upstreamRequest.Content = JsonContent.Create(new
        {
            apiKey = request.ApiKey ?? string.Empty,
            username = request.Username.Trim(),
            password = request.Password
        });

        return await SendAndRelayAsync(upstreamRequest, cancellationToken);
    }

    public async Task<TtSalaryRelayResponseDto> GetEmployeeSalaryAsync(
        int month,
        string? userToken,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidMonth(month))
        {
            return Error((int)HttpStatusCode.BadRequest, "month phải có dạng yyyyMM hợp lệ.");
        }

        if (string.IsNullOrWhiteSpace(userToken))
        {
            return Error((int)HttpStatusCode.Unauthorized, "Thiếu Authorization: Bearer <token> của hệ thống lương.");
        }

        var salaryPath = GetConfiguredPath("SalaryPath", "app/luong/nv");
        using var upstreamRequest = CreateUpstreamRequest(HttpMethod.Get, $"{salaryPath}?month={month}");
        if (upstreamRequest is null) return UpstreamConfigurationError();

        upstreamRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
        return await SendAndRelayAsync(upstreamRequest, cancellationToken);
    }

    private HttpRequestMessage? CreateUpstreamRequest(HttpMethod method, string relativePath)
    {
        var baseUrl = _configuration["TtSalary:BaseUrl"]?.Trim();
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(relativePath)
            || Uri.TryCreate(relativePath, UriKind.Absolute, out _))
        {
            _logger.LogError("TtSalary có BaseUrl hoặc endpoint path không hợp lệ.");
            return null;
        }

        var normalizedBaseUri = new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/");
        return new HttpRequestMessage(method, new Uri(normalizedBaseUri, relativePath.TrimStart('/')));
    }

    private async Task<TtSalaryRelayResponseDto> SendAndRelayAsync(
        HttpRequestMessage upstreamRequest,
        CancellationToken cancellationToken)
    {
        try
        {
            using var upstreamResponse = await _httpClient.SendAsync(
                upstreamRequest,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken);

            return new TtSalaryRelayResponseDto
            {
                StatusCode = (int)upstreamResponse.StatusCode,
                ContentType = upstreamResponse.Content.Headers.ContentType?.ToString() ?? "application/json; charset=utf-8",
                Content = await upstreamResponse.Content.ReadAsStringAsync(cancellationToken)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Error(499, "Yêu cầu đã bị hủy.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Không kết nối được API lương nội bộ.");
            return Error((int)HttpStatusCode.ServiceUnavailable, "Chưa kết nối được dịch vụ lương nội bộ.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi gateway TtSalary.");
            return Error((int)HttpStatusCode.BadGateway, "Không thể xử lý phản hồi từ dịch vụ lương.");
        }
    }

    private TtSalaryRelayResponseDto UpstreamConfigurationError() =>
        Error((int)HttpStatusCode.ServiceUnavailable,
            "Dịch vụ lương chưa được cấu hình địa chỉ hợp lệ trên máy chủ.");

    private string GetConfiguredPath(string key, string fallback) =>
        _configuration[$"TtSalary:{key}"]?.Trim() ?? fallback;

    private static TtSalaryRelayResponseDto Error(int statusCode, string message) =>
        new()
        {
            StatusCode = statusCode,
            Content = JsonSerializer.Serialize(new { message })
        };

    private static bool IsValidMonth(int month)
    {
        var year = month / 100;
        var calendarMonth = month % 100;
        return year is >= 2000 and <= 2100 && calendarMonth is >= 1 and <= 12;
    }

}
