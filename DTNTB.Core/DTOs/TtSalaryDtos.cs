using System.ComponentModel.DataAnnotations;

namespace DTNTB.Core.DTOs;

public sealed class TtSalaryLoginRequestDto
{
    // API lương nội bộ hiện cho phép chuỗi rỗng; gateway không lưu lại giá trị này.
    [StringLength(2048)]
    public string ApiKey { get; init; } = string.Empty;

    [Required, StringLength(100)]
    public string Username { get; init; } = string.Empty;

    [Required, StringLength(256)]
    public string Password { get; init; } = string.Empty;
}

public sealed class TtSalaryMarkSalarySlipViewedRequestDto
{
    [Required, StringLength(100)]
    public string MaHrm { get; init; } = string.Empty;
}

/// <summary>
/// Phản hồi nguyên trạng từ hệ thống lương nội bộ để controller relay về client.
/// </summary>
public sealed class TtSalaryRelayResponseDto
{
    public int StatusCode { get; init; }
    public string ContentType { get; init; } = "application/json; charset=utf-8";
    public string Content { get; init; } = string.Empty;
}

