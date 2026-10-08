using DTNTB.Core.DTOs;

namespace DTNTB.Core.Interfaces;

public interface ITtSalaryService
{
    Task<TtSalaryRelayResponseDto> LoginAsync(
        TtSalaryLoginRequestDto request,
        CancellationToken cancellationToken = default);
    Task<TtSalaryRelayResponseDto> GetEmployeeSalaryAsync(int month, string? userToken, CancellationToken cancellationToken = default);
    Task<TtSalaryRelayResponseDto> GetNewSalarySlipStatusAsync(string maHrm, CancellationToken cancellationToken = default);
}
