using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DTNTB.Core.DTOs
{
    public class NotificationSettingsDto
    {
        public bool DailyEnabled { get; set; } = true;

        [Required, RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$")]
        public string DailyTime { get; set; } = "08:30";

        [Required, StringLength(200)]
        public string DailyTitle { get; set; } = "🔔 CẢNH BÁO PHIẾU TỒN ĐỌNG";

        [Required, StringLength(1000)]
        public string DailyBody { get; set; } = "Chào bạn, hiện tại bạn đang có {count} phiếu đo kiểm chưa thực hiện thực địa. Vui lòng xử lý để tránh quá hạn SLA!";

        public string UpdatedBy { get; set; } = string.Empty;
        public DateTime? UpdatedAt { get; set; }
    }

    public class SendTestNotificationRequestDto
    {
        [Required, StringLength(100)]
        public string Username { get; set; } = string.Empty;

        [Required, StringLength(200)]
        public string Title { get; set; } = "🔔 THÔNG BÁO KIỂM TRA";

        [Required, StringLength(1000)]
        public string Body { get; set; } = "Đây là thông báo kiểm tra từ hệ thống.";
    }

    public class NotificationHistoryItemDto
    {
        public long NotificationId { get; set; }
        public string NotificationType { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Topic { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string FirebaseMessageId { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? SentAt { get; set; }
    }

    public class NotificationHistoryPageDto
    {
        public List<NotificationHistoryItemDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
    }

    public class NotificationSendResultDto
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string FirebaseMessageId { get; set; } = string.Empty;
    }
}
