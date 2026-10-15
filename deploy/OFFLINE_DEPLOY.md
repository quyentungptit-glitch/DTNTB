# Triển khai khi máy chủ Ubuntu không có Internet

Image Docker mặc định (`Dockerfile`) phải tải NuGet, npm và gói apt lúc build nên **không build được**
trên máy chủ bị khoá Internet. Cách dưới đây build sẵn trên máy dev rồi chỉ chép kết quả lên máy chủ.

Yêu cầu trên máy chủ: đã có sẵn image nền `mcr.microsoft.com/dotnet/aspnet:8.0` và `nginx:1.27-alpine`
(có từ những lần build trước; kiểm tra bằng `docker images`). Nếu chưa có: trên máy có Docker + Internet chạy
`docker pull` hai image đó rồi `docker save -o base.tar mcr.microsoft.com/dotnet/aspnet:8.0 nginx:1.27-alpine`,
chép lên máy chủ và `docker load -i base.tar`.

## 1. Trên máy dev (Windows, có Internet)

```powershell
cd D:\PJ1_DTNTB\DTNTB
powershell -ExecutionPolicy Bypass -File .\deploy\build-offline.ps1
```

Tạo ra `deploy\out\dtntb-api.tar.gz` (thư mục `publish`) và `deploy\out\dtntb-frontend.tar.gz` (thư mục `dist`).

## 2. Chép lên máy chủ

Đưa code mới lên như mọi khi (git pull / chép thư mục), rồi chép 2 file nén (WinSCP hoặc `scp`):

```bash
scp deploy/out/dtntb-*.tar.gz user@may-chu:/tmp/
```

## 3. Trên máy chủ

```bash
cd /duong-dan/DTNTB                     # thư mục chứa docker-compose.yml
rm -rf publish && tar -xzf /tmp/dtntb-api.tar.gz
(cd ../DTNTB-Frontend && rm -rf dist && tar -xzf /tmp/dtntb-frontend.tar.gz)

docker compose -f docker-compose.yml -f docker-compose.offline.yml up -d --build
```

## Cấu hình khi không có Internet (file `.env`)

```
Geocoding__Provider=None        # Không tra địa chỉ chữ qua OpenStreetMap; vẫn lưu toạ độ GPS
```

Nếu quên đặt, API tự tạm ngừng tra địa chỉ 5 phút sau mỗi lần không kết nối được, nên điểm danh vẫn chạy.
Khi có API bản đồ nội bộ (mapVNPT) thì chuyển `Geocoding__Provider=Custom` (xem DIEMDANH_API.md).

Những phần **không** cần Internet từ máy chủ:
- Ảnh lưu MinIO nội bộ `10.30.28.22:9000`.
- Bản đồ nền do điện thoại/máy tính người dùng tự tải từ tile.openstreetmap.org.
- Font in chữ lên ảnh đã đóng gói trong `DTNTB.API/Fonts`.

Gửi nhiều ảnh một lượt: nginx đứng trước API cần `client_max_body_size 15m;`.
