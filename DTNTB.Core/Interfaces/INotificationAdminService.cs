using System.Collections.Generic;
using System.Threading.Tasks;
using DTNTB.Core.DTOs;

namespace DTNTB.Core.Interfaces
{
    public interface INotificationAdminService
    {
        Task<NotificationSettingsDto> GetSettingsAsync();
        Task<bool> SaveSettingsAsync(NotificationSettingsDto settings, string updatedBy);
        Task<NotificationHistoryPageDto> GetHistoryAsync(int page, int pageSize);
        Task<NotificationSendResultDto> SendTestAsync(SendTestNotificationRequestDto request, string sentBy);
        Task<NotificationSendResultDto> SendToUsernameAsync(
            string username,
            string notificationType,
            string title,
            string body,
            IReadOnlyDictionary<string, string>? data,
            string? createdBy);
    }
}
