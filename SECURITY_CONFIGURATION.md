# Cấu hình bí mật khi chạy DTNTB

Không lưu mật khẩu, private key hoặc token trong Git. Bản triển khai Docker hiện dùng `.env` trên Ubuntu (đã được `.gitignore`); đặt quyền `600` và không sao chép file này ra ngoài máy chủ.

## DTNTB API

Các khóa cấu hình bắt buộc:

- `ConnectionStrings__ConnectionString_NBH`
- `Jwt__Key` (ít nhất 32 byte ngẫu nhiên)
- `Jwt__Issuer`
- `Jwt__Audience`
- `Sso__BaseUrl` (HTTPS ở production)
- `Sso__ApiKey`
- `SystemToSystem__SecretKey`
- `RemoteStorage__BaseUrl` (HTTPS, hoặc HTTP tới IP private/loopback khi bật tùy chọn bên dưới)
- `RemoteStorage__AllowInsecureHttpForPrivateNetwork=true` (chỉ dùng cho VLAN nội bộ đã giới hạn firewall)
- `RemoteStorage__InternalApiKey`
- `Firebase__ServiceAccountPath` (đường dẫn ngoài source đến service account)

## Storage Server

Khóa cấu hình bắt buộc:

- `StorageSettings__InternalApiKey`
- `StorageSettings__InternalApiKey_FILE` (nên dùng trên IIS, trỏ tới tệp chỉ tài khoản App Pool được đọc)
- `AllowedHosts` (hostname/IP thực tế dùng để gọi Storage)
- `StorageSettings__ForceHttps=false` khi Storage chỉ phục vụ HTTP trong VLAN nội bộ

`StorageSettings__InternalApiKey` phải là khóa mới, đủ dài, ngẫu nhiên và khác với các khóa đã từng có trong source.

## Ví dụ local (không commit)

Tạo `DTNTB.API/appsettings.Local.json` và `DTNTB.StorageServer/appsettings.Local.json` chỉ trên máy được cấp quyền. Chỉ dùng placeholder hoặc secret manager trong tài liệu/chia sẻ, không gửi khóa thật qua chat hoặc commit.

Sau khi triển khai cấu hình mới, kiểm tra API và Storage Server không thể khởi động nếu thiếu khóa bắt buộc; đây là hành vi an toàn mong muốn.

Trên Ubuntu, sao chép `.env.example` thành `.env` và điền các giá trị thực tế. Firebase service account vẫn nằm tại `/opt/dtntb/secrets/firebase-service-account.json`, được mount chỉ đọc vào container. Không đưa `.env` hoặc file Firebase vào Git.

Khi dùng Storage qua HTTP nội bộ, `X-Internal-Key` và nội dung ảnh truyền dưới dạng rõ. Chỉ mở cổng Storage cho đúng IP Ubuntu/API, không NAT/public cổng này ra Internet và cần coi VLAN là vùng mạng tin cậy. API chỉ chấp nhận HTTP khi host là loopback hoặc địa chỉ IPv4 private và tùy chọn cho phép đã được bật rõ ràng.
