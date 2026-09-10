using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Oracle.ManagedDataAccess.Client;
using Dapper;
using FirebaseAdmin.Messaging;

namespace DTNTB.API.BackgroundWorkers
{
    public class FcmNotificationWorker : BackgroundService
    {
        private readonly string _connString;

        public FcmNotificationWorker(IConfiguration config)
        {
            _connString = config.GetConnectionString("ConnectionString_NBH") ?? string.Empty;
        }


        //bắn thông báo theo giờ
        //protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        //{
        //    while (!stoppingToken.IsCancellationRequested)
        //    {
        //        var now = DateTime.Now;
        //        DateTime nextRun;

        //        // ─── THUẬT TOÁN HẸN GIỜ DÀNH RIÊNG CHO KHUNG 6h00 SÁNG ĐẾN 17h00 CHIỀU ───
        //        if (now.Hour < 6)
        //        {
        //            // Hôm nay chưa đến 6h00 sáng -> Hẹn chạy vào đúng 6h00 sáng hôm nay [INDEX]
        //            nextRun = new DateTime(now.Year, now.Month, now.Day, 6, 0, 0);
        //        }
        //        else if (now.Hour >= 17 && (now.Hour > 17 || now.Minute > 0 || now.Second > 0))
        //        {
        //            // Đã qua mốc 17h00 hôm nay -> Hẹn chạy vào đúng 6h00 sáng ngày mai [INDEX]
        //            var tomorrow = now.AddDays(1);
        //            nextRun = new DateTime(tomorrow.Year, tomorrow.Month, tomorrow.Day, 6, 0, 0);
        //        }
        //        else
        //        {
        //            // Đang ở trong khoảng 6h00 đến 17h00 -> Hẹn chạy ở đầu giờ (00 phút) của tiếng tiếp theo [INDEX]
        //            nextRun = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).AddHours(1);
        //        }

        //        // Tính toán số mili-giây cần ngủ
        //        double delayMilliseconds = (nextRun - now).TotalMilliseconds;
        //        if (delayMilliseconds <= 0)
        //        {
        //            delayMilliseconds = 1000; // Phòng hờ sai lệch tích tắc của CPU
        //        }

        //        // Luồng chạy ngầm đi ngủ
        //        await Task.Delay((int)delayMilliseconds, stoppingToken);

        //        if (stoppingToken.IsCancellationRequested) break;

        //        // Thức dậy thực thi quét phiếu tồn đọng và bắn Push
        //        try
        //        {
        //            await SendDailyPendingTicketsNotificationsAsync();
        //        }
        //        catch (Exception ex)
        //        {
        //            System.Diagnostics.Debug.WriteLine("Lỗi tiến trình Worker bắn Push: " + ex.Message);
        //        }
        //    }
        //}



        ////Cách 1 sử dụng token của thiết bị, lưu token thiết bị vào database và bắn push trực tiếp đến thiết bị đó
        //private async Task SendDailyPendingTicketsNotificationsAsync()
        //{
        //    using (var conn = new OracleConnection(_connString))
        //    {
        //        // 1. Quét tìm các phiếu có trangthai_phieu = 1 (Chưa thực hiện), gom nhóm theo nhân viên [INDEX]
        //        string scanQuery = @"
        //            SELECT UPPER(TRIM(t.ma_nvkt)) as MaNvkt, COUNT(1) as PendingCount 
        //            FROM brcd_dhgh_kehoach t 
        //            WHERE t.trangthai_phieu = 1 AND t.ma_nvkt IS NOT NULL
        //            GROUP BY UPPER(TRIM(t.ma_nvkt))";

        //        var pendingList = (await conn.QueryAsync<dynamic>(scanQuery)).ToList();

        //        if (!pendingList.Any()) return; // Không có phiếu tồn đọng, dừng tiến trình

        //        // 2. Vòng lặp bắn Push cho từng nhân viên tồn phiếu
        //        foreach (var item in pendingList)
        //        {
        //            string maNvkt = item.MANVKT.ToString();
        //            int count = Convert.ToInt32(item.PENDINGCOUNT);

        //            // Truy vấn lấy danh sách Token thiết bị từ bảng mới: brcd_dhgh_fcm_tokens [INDEX]
        //            string tokenQuery = "SELECT fcm_token FROM brcd_dhgh_fcm_tokens WHERE UPPER(TRIM(username)) = :username";
        //            var fcmTokens = (await conn.QueryAsync<string>(tokenQuery, new { username = maNvkt })).ToList();

        //            if (fcmTokens.Any())
        //            {
        //                await PushToFirebaseDevicesAsync(fcmTokens, count);
        //            }
        //        }
        //    }
        //}

        //private async Task PushToFirebaseDevicesAsync(List<string> tokens, int pendingCount)
        //{
        //    try
        //    {
        //        var message = new MulticastMessage()
        //        {
        //            Tokens = tokens,
        //            Notification = new Notification()
        //            {
        //                Title = "🔔 CẢNH BÁO PHIẾU TỒN ĐỌNG",
        //                Body = $"Chào bạn, hiện tại bạn đang có {pendingCount} phiếu đo kiểm chưa thực hiện thực địa. Vui lòng xử lý để tránh quá hạn SLA!"
        //            },
        //            Data = new Dictionary<string, string>()
        //            {
        //                { "click_action", "open_default_list" }
        //            }
        //        };

        //        var response = await FirebaseMessaging.DefaultInstance.SendMulticastAsync(message);
        //        System.Diagnostics.Debug.WriteLine($"Đã bắn Push thành công cho {response.SuccessCount} thiết bị.");
        //    }
        //    catch (Exception ex)
        //    {
        //        System.Diagnostics.Debug.WriteLine("Lỗi gọi Firebase Messaging: " + ex.Message);
        //    }
        //}


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // ─── MẸO THỬ NGHIỆM: TỰ ĐỘNG BẮN LUÔN 1 LƯỢT NGAY KHI VỪA BẬT SERVER ───
            try
            {
                await SendDailyPendingTicketsNotificationsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Lỗi bắn thử lúc khởi động: " + ex.Message);
            }


            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.Now;
                DateTime nextRun;

                // Thiết lập 2 mốc giờ chạy cố định trong ngày: 7h30 và 13h30 [INDEX]
                var run1 = new DateTime(now.Year, now.Month, now.Day, 7, 30, 0);
                var run2 = new DateTime(now.Year, now.Month, now.Day, 13, 30, 0);

                if (now < run1)
                {
                    nextRun = run1; // Chưa đến 7h30 -> Hẹn chạy lúc 7h30 sáng nay [INDEX]
                }
                else if (now < run2)
                {
                    nextRun = run2; // Đã qua 7h30 nhưng chưa đến 13h30 -> Hẹn chạy lúc 13h30 chiều nay [INDEX]
                }
                else
                {
                    nextRun = run1.AddDays(1); // Đã qua mốc chiều -> Hẹn chạy lúc 7h30 sáng ngày mai [INDEX]
                }

                // Tính toán số mili-giây cần ngủ
                double delayMilliseconds = (nextRun - now).TotalMilliseconds;
                if (delayMilliseconds <= 0)
                {
                    delayMilliseconds = 1000;
                }

                // Luồng chạy ngầm đi ngủ
                await Task.Delay((int)delayMilliseconds, stoppingToken);

                if (stoppingToken.IsCancellationRequested) break;

                // Thức dậy thực thi quét phiếu tồn đọng và bắn Push
                try
                {
                    await SendDailyPendingTicketsNotificationsAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Lỗi tiến trình Worker bắn Push: " + ex.Message);
                }
            }
        }


        //cách 2 sử dụng Topic cá nhân của từng nhân viên, chuẩn hóa tên Topic theo username và bắn Push lên Topic đó
        private async Task SendDailyPendingTicketsNotificationsAsync()
        {
            using (var conn = new OracleConnection(_connString))
            {
                string scanQuery = @"
                    SELECT UPPER(TRIM(nd.ma_nd)) as MaNvkt, COUNT(1) as PendingCount 
                    FROM brcd_dhgh_kehoach t
                    inner join v_nhanvien nv on t.ma_nv_nhan=nv.ma_nv and t.phanvung_id=nv.phanvung_id
                    inner join v_nguoidung nd on nv.nhanvien_id=nd.nhanvien_id and nv.phanvung_id=nd.phanvung_id
                    WHERE t.trangthai_phieu = 1 AND t.ma_nvkt IS NOT NULL
                      AND nd.trangthai=1
                      AND sysdate - t.ngay_giao < 1
                    GROUP BY UPPER(TRIM(nd.ma_nd))";

                var pendingList = (await conn.QueryAsync<dynamic>(scanQuery)).ToList();

                if (!pendingList.Any()) return;

                foreach (var item in pendingList)
                {
                    string maNvkt = item.MANVKT.ToString().ToLower();
                    int count = Convert.ToInt32(item.PENDINGCOUNT);

                    // Sửa đổi: Chuẩn hóa tên Topic cá nhân (Ví dụ: "user_khanhlx_nbh" - thay thế dấu "." bằng "_") [INDEX]
                    string topicName = "phieu_nguy_co_ton_" + maNvkt.Replace(".", "_");

                    // Bắn trực tiếp lên Topic cá nhân của nhân viên đó [INDEX]
                    await PushToFirebaseTopicAsync(topicName, count);
                }
            }
        }

        private async Task PushToFirebaseTopicAsync(string topicName, int pendingCount)
        {
            try
            {
                var message = new Message() // Đổi thành Message đơn lẻ gửi lên Topic [INDEX]
                {
                    Topic = topicName, // Chỉ định gửi lên Topic cá nhân [INDEX]
                    Notification = new Notification()
                    {
                        Title = "🔔 CẢNH BÁO PHIẾU TỒN ĐỌNG",
                        Body = $"Chào bạn, hiện tại bạn đang có {pendingCount} phiếu đo kiểm chưa thực hiện thực địa. Vui lòng xử lý để tránh quá hạn SLA!"
                    },
                    Data = new Dictionary<string, string>()
                    {
                        { "click_action", "open_default_list" }
                    }
                };

                // Gọi lệnh SendAsync gửi lên máy chủ Firebase [INDEX]
                string response = await FirebaseMessaging.DefaultInstance.SendAsync(message);
                System.Diagnostics.Debug.WriteLine($"Đã bắn Push lên Topic {topicName} thành công: {response}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Lỗi bắn Push lên Topic {topicName}: " + ex.Message);
            }
        }
    }
}