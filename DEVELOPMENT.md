# Chạy và debug DTNTB trên Windows

Các địa chỉ cố định của môi trường Development:

- Angular: `http://localhost:4200`
- DTNTB API: `http://localhost:5001`
- Storage API local: `http://localhost:5181`
- Swagger API: `http://localhost:5001/swagger`
- Swagger Storage: `http://localhost:5181/swagger`

## 1. Cấu hình bí mật local

File bắt buộc khi chạy Angular/API local và không được commit lên Git:

- `DTNTB/DTNTB.API/appsettings.Local.json`

Chỉ cần file sau khi muốn debug riêng Storage project trên máy:

- `DTNTB.StorageServer/appsettings.Local.json`

API cần có chuỗi kết nối Oracle, JWT issuer/audience/key, SSO URL/key,
Storage internal key và danh sách mật khẩu bypass nếu cần. Storage API phải dùng
cùng internal key với API.

Không dùng `.env` Docker khi chạy bằng Visual Studio hoặc `dotnet run`.
`appsettings.Local.json` chỉ được API nạp khi `ASPNETCORE_ENVIRONMENT=Development`.

## 2. Chạy Storage API local

Mở terminal thứ nhất:

```powershell
cd D:\PJ1_DTNTB\DTNTB.StorageServer
dotnet run --launch-profile http
```

`RemoteStorage:InternalApiKey` trong `DTNTB.API/appsettings.Local.json` phải
trùng `StorageSettings:InternalApiKey` trong `DTNTB.StorageServer/appsettings.Local.json`.

## 3. Chạy DTNTB API

Mở terminal thứ nhất:

```powershell
cd D:\PJ1_DTNTB\DTNTB\DTNTB.API
dotnet run --launch-profile http
```

Khi debug bằng Visual Studio, chọn project `DTNTB.API`, profile `http`, rồi nhấn
F5. Đặt breakpoint trong Controller hoặc Service như bình thường.

## 4. Chạy Angular

Mở terminal thứ hai:

```powershell
cd D:\PJ1_DTNTB\DTNTB-Frontend
npm install
npm start
```

Angular gọi `/api`; dev proxy tự chuyển request tới `http://localhost:5001`.
Nhờ vậy không cần sửa URL production, không gặp lỗi chứng chỉ HTTPS local và
không cần tắt bảo mật trình duyệt.

## 5. Kiểm tra trước khi production

```powershell
cd D:\PJ1_DTNTB\DTNTB
dotnet build DTNTB.slnx --no-restore

cd D:\PJ1_DTNTB\DTNTB-Frontend
npm test -- --watch=false
npm run build
```

FCM worker mặc định tắt ở Development để không gửi thông báo thật khi debug.
Production vẫn bật theo `appsettings.json`.
