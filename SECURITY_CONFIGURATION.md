# Cấu hình bí mật khi chạy DTNTB

Không lưu mật khẩu, private key hoặc token trong Git. Sau khi xoay vòng các bí mật cũ, cấu hình chúng bằng biến môi trường của tiến trình chạy ứng dụng, secret vault, hoặc tệp `appsettings.Local.json` (đã được `.gitignore`).

## DTNTB API

Các khóa cấu hình bắt buộc:

- `ConnectionStrings__ConnectionString_NBH`
- `Jwt__Key` (ít nhất 32 byte ngẫu nhiên)
- `Jwt__Issuer`
- `Jwt__Audience`
- `Sso__BaseUrl` (HTTPS ở production)
- `Sso__ApiKey`
- `SystemToSystem__SecretKey`
- `RemoteStorage__BaseUrl` (HTTPS nếu không phải `localhost`)
- `RemoteStorage__InternalApiKey`
- `Firebase__ServiceAccountPath` (đường dẫn ngoài source đến service account)

## Storage Server

Khóa cấu hình bắt buộc:

- `StorageSettings__InternalApiKey`

`StorageSettings__InternalApiKey` phải là khóa mới, đủ dài, ngẫu nhiên và khác với các khóa đã từng có trong source.

## Ví dụ local (không commit)

Tạo `DTNTB.API/appsettings.Local.json` và `DTNTB.StorageServer/appsettings.Local.json` chỉ trên máy được cấp quyền. Chỉ dùng placeholder hoặc secret manager trong tài liệu/chia sẻ, không gửi khóa thật qua chat hoặc commit.

Sau khi triển khai cấu hình mới, kiểm tra API và Storage Server không thể khởi động nếu thiếu khóa bắt buộc; đây là hành vi an toàn mong muốn.
