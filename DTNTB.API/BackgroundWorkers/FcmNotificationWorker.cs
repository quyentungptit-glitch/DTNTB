using Dapper;
using DTNTB.Core.Interfaces;
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
    private readonly INotificationAdminService _notificationService;
    private readonly ILogger<FcmNotificationWorker> _logger;

    public FcmNotificationWorker(
        IConfiguration configuration,
        INotificationAdminService notificationService,
        ILogger<FcmNotificationWorker> logger)
    {
        _connString = configuration.GetConnectionString("ConnectionString_NBH")
            ?? throw new InvalidOperationException("ConnectionStrings:ConnectionString_NBH chưa được cấu hình.");
        _logger = logger;
        _notificationService = notificationService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FcmNotificationWorker đã khởi động; lịch gửi được đọc từ cấu hình Quản trị thông báo.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var settings = await _notificationService.GetSettingsAsync();
                var now = DateTime.Now;
                if (settings.DailyEnabled
                    && TimeOnly.TryParse(settings.DailyTime, out var scheduledTime)
                    && now.Hour == scheduledTime.Hour
                    && now.Minute == scheduledTime.Minute)
                {
                    await SendDailyPendingTicketsNotificationsAsync(
                        settings.DailyTitle,
                        settings.DailyBody,
                        stoppingToken);
                }

                var nextMinute = now.AddMinutes(1);
                await Task.Delay(nextMinute.AddSeconds(-nextMinute.Second).AddMilliseconds(-nextMinute.Millisecond) - DateTime.Now, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi tiến trình gửi thông báo phiếu tồn.");
                // Khi bảng cấu hình/chứng chỉ Firebase chưa sẵn sàng, tránh vòng lặp
                // ghi log liên tục và tự thử lại sau một phút.
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    private async Task SendDailyPendingTicketsNotificationsAsync(
        string titleTemplate,
        string bodyTemplate,
        CancellationToken stoppingToken)
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

            // Khóa ngày + người dùng + loại thông báo giúp tránh gửi lặp khi ứng dụng chạy lại.
            if (!await TryStartNotificationAuditAsync(connection, username, item.PendingCount))
            {
                _logger.LogInformation("Bỏ qua thông báo trùng cho {Username} vào ngày hôm nay.", username);
                continue;
            }

            try
            {
                var title = titleTemplate.Replace("{count}", item.PendingCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var body = bodyTemplate.Replace("{count}", item.PendingCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var result = await _notificationService.SendToUsernameAsync(
                    username,
                    NotificationType,
                    title,
                    body,
                    new Dictionary<string, string>
                    {
                        ["route"] = "/nguycotb/view",
                        ["click_action"] = "open_default_list",
                        ["badge_count"] = item.PendingCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["notification_type"] = NotificationType
                    },
                    "SYSTEM");

                if (!result.Success)
                {
                    throw new InvalidOperationException(result.Message);
                }

                if (result.Skipped)
                {
                    await CompleteNotificationAuditAsync(connection, username, "SKIPPED", null, result.Message);
                    _logger.LogInformation("Bỏ qua thông báo phiếu tồn cho {Username}: {Message}", username, result.Message);
                    continue;
                }

                await CompleteNotificationAuditAsync(connection, username, "SENT", result.FirebaseMessageId, null);
                _logger.LogInformation(
                    "Đã gửi nhắc phiếu tồn cho {Username}; số phiếu: {PendingCount}; Firebase message ID: {MessageId}.",
                    username,
                    item.PendingCount,
                    result.FirebaseMessageId);
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

    private sealed class PendingTicketCount
    {
        public string MaNvkt { get; init; } = string.Empty;
        public int PendingCount { get; init; }
    }
}
