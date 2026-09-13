using System.Globalization;
using Dapper;
using FirebaseAdmin.Messaging;
using Microsoft.Extensions.Hosting;
using Oracle.ManagedDataAccess.Client;

namespace DTNTB.API.BackgroundWorkers;

/// <summary>
/// Gửi thông báo nhắc phiếu tồn một lần mỗi ngày lúc 08:30 (giờ máy chủ).
/// Luồng gửi ngay khi giao phiếu được xử lý ở nơi khác.
/// </summary>
public sealed class FcmNotificationWorker : BackgroundService
{
    private const string NotificationType = "DAILY_PENDING_TICKETS";
    private readonly string _connString;
    private readonly ILogger<FcmNotificationWorker> _logger;

    public FcmNotificationWorker(IConfiguration configuration, ILogger<FcmNotificationWorker> logger)
    {
        _connString = configuration.GetConnectionString("ConnectionString_NBH")
            ?? throw new InvalidOperationException("ConnectionStrings:ConnectionString_NBH chưa được cấu hình.");
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FcmNotificationWorker đã khởi động; lịch gửi nhắc phiếu tồn là 08:30 mỗi ngày.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var scheduledTime = new DateTime(now.Year, now.Month, now.Day, 8, 30, 0);
            var nextRun = now < scheduledTime ? scheduledTime : scheduledTime.AddDays(1);

            try
            {
                await Task.Delay(nextRun - now, stoppingToken);
                await SendDailyPendingTicketsNotificationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi tiến trình gửi thông báo phiếu tồn lúc 08:30.");
            }
        }
    }

    private async Task SendDailyPendingTicketsNotificationsAsync(CancellationToken stoppingToken)
    {
        const string scanQuery = @"
            SELECT UPPER(TRIM(nd.ma_nd)) AS MaNvkt, COUNT(1) AS PendingCount
            FROM brcd_dhgh_kehoach t
            INNER JOIN v_nhanvien nv ON t.ma_nv_nhan = nv.ma_nv AND t.phanvung_id = nv.phanvung_id
            INNER JOIN v_nguoidung nd ON nv.nhanvien_id = nd.nhanvien_id AND nv.phanvung_id = nd.phanvung_id
            WHERE t.trangthai_phieu = 1 AND t.ma_nvkt IS NOT NULL
              AND nd.trangthai = 1
              AND SYSDATE - t.ngay_giao < 3
            GROUP BY UPPER(TRIM(nd.ma_nd))";

        await using var connection = new OracleConnection(_connString);
        var pendingList = (await connection.QueryAsync<PendingTicketCount>(scanQuery)).ToList();

        if (pendingList.Count == 0)
        {
            _logger.LogInformation("Không có phiếu tồn cần gửi nhắc lúc 08:30.");
            return;
        }

        foreach (var item in pendingList)
        {
            stoppingToken.ThrowIfCancellationRequested();

            var username = item.MaNvkt.Trim().ToLowerInvariant();
            var topicName = "phieu_nguy_co_ton_" + username.Replace('.', '_');

            // Khóa ngày + người dùng + loại thông báo giúp tránh gửi lặp khi ứng dụng chạy lại.
            if (!await TryStartNotificationAuditAsync(connection, username, item.PendingCount))
            {
                _logger.LogInformation("Bỏ qua thông báo trùng cho {Username} vào ngày hôm nay.", username);
                continue;
            }

            try
            {
                var messageId = await PushToFirebaseTopicAsync(topicName, item.PendingCount);
                await CompleteNotificationAuditAsync(connection, username, "SENT", messageId, null);
                _logger.LogInformation(
                    "Đã gửi nhắc phiếu tồn cho {Username}; số phiếu: {PendingCount}; Firebase message ID: {MessageId}.",
                    username,
                    item.PendingCount,
                    messageId);
            }
            catch (Exception ex)
            {
                await CompleteNotificationAuditAsync(connection, username, "FAILED", null, ex.Message);
                _logger.LogError(ex, "Không gửi được thông báo phiếu tồn cho {Username}.", username);
            }
        }
    }

    private static async Task<bool> TryStartNotificationAuditAsync(
        OracleConnection connection,
        string username,
        int pendingCount)
    {
        const string insertAudit = @"
            INSERT INTO brcd_dhgh_fcm_notification_log
                (notification_date, username, notification_type, pending_count, status, created_at)
            VALUES
                (TRUNC(SYSDATE), :username, :notificationType, :pendingCount, 'SENDING', SYSDATE)";

        try
        {
            await connection.ExecuteAsync(insertAudit, new
            {
                username,
                notificationType = NotificationType,
                pendingCount
            });
            return true;
        }
        catch (OracleException ex) when (ex.Number == 1)
        {
            // ORA-00001: đã có bản ghi cho cùng ngày/người dùng/loại thông báo.
            return false;
        }
    }

    private static Task CompleteNotificationAuditAsync(
        OracleConnection connection,
        string username,
        string status,
        string? firebaseMessageId,
        string? errorMessage)
    {
        const string updateAudit = @"
            UPDATE brcd_dhgh_fcm_notification_log
            SET status = :status,
                firebase_message_id = :firebaseMessageId,
                error_message = :errorMessage,
                sent_at = CASE WHEN :status = 'SENT' THEN SYSDATE ELSE NULL END
            WHERE notification_date = TRUNC(SYSDATE)
              AND username = :username
              AND notification_type = :notificationType";

        return connection.ExecuteAsync(updateAudit, new
        {
            username,
            status,
            firebaseMessageId,
            errorMessage = errorMessage?.Length > 1000 ? errorMessage[..1000] : errorMessage,
            notificationType = NotificationType
        });
    }

    private static async Task<string> PushToFirebaseTopicAsync(string topicName, int pendingCount)
    {
        var message = new Message
        {
            Topic = topicName,
            Notification = new Notification
            {
                Title = "🔔 CẢNH BÁO PHIẾU TỒN ĐỌNG",
                Body = $"Chào bạn, hiện tại bạn đang có {pendingCount} phiếu đo kiểm chưa thực hiện thực địa. Vui lòng xử lý để tránh quá hạn SLA!"
            },
            Data = new Dictionary<string, string>
            {
                ["click_action"] = "open_default_list",
                ["badge_count"] = pendingCount.ToString(CultureInfo.InvariantCulture)
            }
        };

        return await FirebaseMessaging.DefaultInstance.SendAsync(message);
    }

    private sealed class PendingTicketCount
    {
        public string MaNvkt { get; init; } = string.Empty;
        public int PendingCount { get; init; }
    }
}
