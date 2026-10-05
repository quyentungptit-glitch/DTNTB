using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using DTNTB.Core.DTOs;
using DTNTB.Core.Interfaces;
using FirebaseAdmin.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;

namespace DTNTB.Infrastructure.Services
{
    /// <summary>
    /// Quản lý cấu hình và lịch sử thông báo FCM. Không lưu registration token:
    /// mọi thông báo được gửi qua topic phieu_nguy_co_ton_{username}.
    /// </summary>
    public sealed class NotificationAdminService : INotificationAdminService
    {
        private readonly string _connectionString;
        private readonly ILogger<NotificationAdminService> _logger;

        public NotificationAdminService(IConfiguration configuration, ILogger<NotificationAdminService> logger)
        {
            _connectionString = configuration.GetConnectionString("ConnectionString_NBH")
                ?? throw new InvalidOperationException("ConnectionStrings:ConnectionString_NBH chưa được cấu hình.");
            _logger = logger;
        }

        public async Task<NotificationSettingsDto> GetSettingsAsync()
        {
            const string sql = @"
                SELECT daily_enabled AS DailyEnabled,
                       daily_time AS DailyTime,
                       daily_title AS DailyTitle,
                       daily_body AS DailyBody,
                       updated_by AS UpdatedBy,
                       updated_at AS UpdatedAt
                FROM dtntb_fcm_settings
                WHERE setting_id = 1";

            await using var conn = new OracleConnection(_connectionString);
            var settings = await conn.QuerySingleOrDefaultAsync<NotificationSettingsDto>(sql);
            return settings ?? new NotificationSettingsDto();
        }

        public async Task<bool> SaveSettingsAsync(NotificationSettingsDto settings, string updatedBy)
        {
            const string sql = @"
                MERGE INTO dtntb_fcm_settings target
                USING (SELECT 1 AS setting_id FROM dual) source
                ON (target.setting_id = source.setting_id)
                WHEN MATCHED THEN UPDATE SET
                    daily_enabled = :dailyEnabled,
                    daily_time = :dailyTime,
                    daily_title = :dailyTitle,
                    daily_body = :dailyBody,
                    updated_by = :updatedBy,
                    updated_at = SYSDATE
                WHEN NOT MATCHED THEN INSERT
                    (setting_id, daily_enabled, daily_time, daily_title, daily_body, updated_by, updated_at)
                VALUES
                    (1, :dailyEnabled, :dailyTime, :dailyTitle, :dailyBody, :updatedBy, SYSDATE)";

            await using var conn = new OracleConnection(_connectionString);
            var affected = await conn.ExecuteAsync(sql, new
            {
                dailyEnabled = settings.DailyEnabled ? 1 : 0,
                dailyTime = settings.DailyTime.Trim(),
                dailyTitle = settings.DailyTitle.Trim(),
                dailyBody = settings.DailyBody.Trim(),
                updatedBy = updatedBy.Trim().ToLowerInvariant()
            });
            return affected > 0;
        }

        public async Task<NotificationHistoryPageDto> GetHistoryAsync(int page, int pageSize)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            int offset = (page - 1) * pageSize;
            int upperBound = offset + pageSize;

            const string countSql = "SELECT COUNT(1) FROM dtntb_fcm_notification_history";
            const string pageSql = @"
                SELECT notification_id AS NotificationId,
                       notification_type AS NotificationType,
                       username AS Username,
                       topic AS Topic,
                       title AS Title,
                       message_body AS Body,
                       status AS Status,
                       firebase_message_id AS FirebaseMessageId,
                       error_message AS ErrorMessage,
                       created_by AS CreatedBy,
                       created_at AS CreatedAt,
                       sent_at AS SentAt
                FROM (
                    SELECT h.*, ROW_NUMBER() OVER (ORDER BY h.created_at DESC, h.notification_id DESC) AS row_num
                    FROM dtntb_fcm_notification_history h
                )
                WHERE row_num > :offset AND row_num <= :upperBound
                ORDER BY row_num";

            await using var conn = new OracleConnection(_connectionString);
            var total = await conn.ExecuteScalarAsync<int>(countSql);
            var items = (await conn.QueryAsync<NotificationHistoryItemDto>(pageSql, new { offset, upperBound })).ToList();
            return new NotificationHistoryPageDto { Items = items, TotalCount = total };
        }

        public Task<NotificationSendResultDto> SendTestAsync(SendTestNotificationRequestDto request, string sentBy) =>
            SendToUsernameAsync(
                request.Username,
                "MANUAL_TEST",
                request.Title,
                request.Body,
                new Dictionary<string, string>
                {
                    ["route"] = "/nguycotb/view",
                    ["notification_type"] = "MANUAL_TEST"
                },
                sentBy);

        public async Task<NotificationSendResultDto> SendToUsernameAsync(
            string username,
            string notificationType,
            string title,
            string body,
            IReadOnlyDictionary<string, string>? data,
            string? createdBy)
        {
            var normalizedUsername = username?.Trim().ToLowerInvariant() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedUsername))
            {
                return new NotificationSendResultDto { Success = false, Message = "Username nhận thông báo không hợp lệ." };
            }

            var topic = BuildPendingTicketTopic(normalizedUsername);
            string? firebaseMessageId = null;
            string? error = null;
            try
            {
                var message = new Message
                {
                    Topic = topic,
                    Notification = new Notification { Title = title.Trim(), Body = body.Trim() },
                    Data = data?.ToDictionary(x => x.Key, x => x.Value ?? string.Empty)
                        ?? new Dictionary<string, string>()
                };
                firebaseMessageId = await FirebaseMessaging.DefaultInstance.SendAsync(message);
                await AddHistoryAsync(notificationType, normalizedUsername, topic, title, body, "SENT", firebaseMessageId, null, createdBy);
                return new NotificationSendResultDto
                {
                    Success = true,
                    Message = "Đã gửi thông báo vào topic của người dùng.",
                    FirebaseMessageId = firebaseMessageId
                };
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _logger.LogWarning(ex, "Không gửi được thông báo {NotificationType} tới topic {Topic}.", notificationType, topic);
                await AddHistoryAsync(notificationType, normalizedUsername, topic, title, body, "FAILED", null, error, createdBy);
                return new NotificationSendResultDto
                {
                    Success = false,
                    Message = "Firebase không gửi được thông báo. Xem lịch sử lỗi để biết chi tiết."
                };
            }
        }

        private async Task AddHistoryAsync(
            string notificationType,
            string username,
            string topic,
            string title,
            string body,
            string status,
            string? firebaseMessageId,
            string? errorMessage,
            string? createdBy)
        {
            const string sql = @"
                INSERT INTO dtntb_fcm_notification_history
                    (notification_id, notification_type, username, topic, title, message_body, status,
                     firebase_message_id, error_message, created_by, created_at, sent_at)
                VALUES
                    (seq_dtntb_fcm_notification_history.NEXTVAL, :notificationType, :username, :topic,
                     :title, :body, :status, :firebaseMessageId, :errorMessage, :createdBy,
                     SYSDATE, CASE WHEN :status = 'SENT' THEN SYSDATE ELSE NULL END)";

            try
            {
                await using var conn = new OracleConnection(_connectionString);
                await conn.ExecuteAsync(sql, new
                {
                    notificationType = notificationType.Trim().ToUpperInvariant(),
                    username,
                    topic,
                    title = title.Trim(),
                    body = body.Trim(),
                    status,
                    firebaseMessageId = firebaseMessageId?.Length > 255 ? firebaseMessageId[..255] : firebaseMessageId,
                    errorMessage = errorMessage?.Length > 1000 ? errorMessage[..1000] : errorMessage,
                    createdBy = string.IsNullOrWhiteSpace(createdBy) ? null : createdBy.Trim().ToLowerInvariant()
                });
            }
            catch (Exception ex)
            {
                // Không để lỗi ghi lịch sử làm hỏng nghiệp vụ giao phiếu/nhắc phiếu.
                _logger.LogWarning(ex, "Không ghi được lịch sử thông báo {NotificationType} cho {Username}.", notificationType, username);
            }
        }

        private static string BuildPendingTicketTopic(string username) =>
            "phieu_nguy_co_ton_" + username.Trim().ToLowerInvariant().Replace('.', '_');
    }
}
