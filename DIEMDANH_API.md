# API Điểm danh hiện trường

Nhân viên hạ tầng chụp ảnh tại chỗ để điểm danh: hệ thống lưu ảnh, toạ độ GPS, địa chỉ,
loại việc/ghi chú và **giờ máy chủ**, đồng thời in các thông tin này lên góc ảnh.
Tổ trưởng/lãnh đạo xem bản đồ trong ngày, danh sách ai đã điểm danh mấy lần và lịch sử.

## Triển khai

1. Chạy `DatabaseScripts/20261015_create_diemdanh.sql` trên Oracle (bảng `dtntb_diemdanh`,
   sequence, index, 3 permission, 2 menu). Nếu đã chạy bản đầu (chưa có cột `ten_to`) thì chạy thêm
   `20261015b_diemdanh_add_ten_to.sql`. Sau đó chạy `20261015d_diemdanh_nhieu_anh.sql` (cột `so_anh`,
   bảng `dtntb_diemdanh_anh` cho phép một lượt gửi nhiều ảnh).
2. Gán permission cho role trong form Phân quyền:
   | Permission | Dùng cho |
   |---|---|
   | `PERMISSIONS.DIEMDANH.CHECKIN` | Nhân viên được điểm danh |
   | `PERMISSIONS.DIEMDANH.VIEW` | Xem bản đồ / danh sách / lịch sử (theo phạm vi dữ liệu của role) |
   | `PERMISSIONS.DIEMDANH.EXPORT` | Xuất Excel (cần kèm VIEW) |
   | `PERMISSIONS.DIEMDANH.APPROVE` | Duyệt / từ chối lượt điểm danh trong phạm vi dữ liệu (cần kèm VIEW). Tạo bằng `20261016_diemdanh_duyet.sql` |
   | `PERMISSIONS.DIEMDANH.DELETE` | Xóa lượt điểm danh kèm ảnh, trong phạm vi dữ liệu (cần kèm VIEW). Tạo bằng `20261015c_diemdanh_permission_delete.sql` |
3. Gán menu `MENU_DIEMDANH` (/diemdanh) và `MENU_DIEMDANH_THEODOI` (/diemdanh/theo-doi) trong form Phân Menu.
4. Ảnh điểm danh lưu ở **MinIO**, bucket `ttht-diemdanh` (không qua Storage server), chia thư mục con theo
   tháng: `2026-10/20261015_081203_123.jpg`. Cấu hình mục `Minio` (Endpoint = host:cổng **S3 API**, mặc định 9000,
   không phải cổng console 9001). AccessKey/SecretKey không để trong appsettings.json: dùng appsettings.Local.json
   khi chạy máy dev, còn trên máy chủ dùng biến môi trường `Minio__AccessKey`, `Minio__SecretKey`
   (hoặc `Minio__SecretKey_FILE` trỏ tới file secret). Nên tạo access key riêng chỉ có quyền trên bucket này.
5. Cấu hình dịch vụ đổi toạ độ ra địa chỉ (mục `Geocoding` trong appsettings) — xem bên dưới.
   Hiện dùng tạm máy chủ OpenStreetMap Nominatim công cộng (máy chủ API cần ra được Internet tới
   nominatim.openstreetmap.org; giới hạn 1 request/giây, đã có hàng đợi + cache). Khi có API mapVNPT thì
   chuyển sang `Provider = "Custom"`. Để `Provider = "None"` thì vẫn điểm danh bình thường, chỉ không có địa chỉ chữ.

## Phạm vi dữ liệu (theo `data_scope` của role)

| Scope | Thấy được |
|---|---|
| TOAN_TINH | Toàn bộ |
| DIA_BAN | Các đơn vị thuộc địa bàn (`v_donvi_diaban`) |
| DON_VI | Cùng 7 ký tự đầu mã đơn vị |
| TO_QL | Cùng toàn bộ ma_dv (cùng tổ) |
| NHAN_VIEN | Chỉ của mình |

Mã đơn vị được chụp lại tại thời điểm điểm danh (từ `v_nguoidung_diaban`), nên lịch sử
không đổi khi nhân sự chuyển tổ về sau.

## Endpoints (`/api/diemdanh`)

### Nhân viên (CHECKIN)

| Method | Đường dẫn | Mô tả |
|---|---|---|
| GET | `/loai-cong-viec` | Danh mục việc chọn nhanh: `[{ ma, ten }]` |
| GET | `/dia-chi?lat=..&lng=..` | Đổi toạ độ ra địa chỉ để hiện trước khi gửi: `{ latitude, longitude, diaChi }` |
| POST | `/checkin` | Gửi điểm danh (multipart/form-data, xem dưới) |
| GET | `/cua-toi?ngay=yyyy-MM-dd` | Các lượt của chính mình trong ngày (mặc định hôm nay) |

`POST /checkin` — form-data:

| Trường | Bắt buộc | Ghi chú |
|---|---|---|
| `anh` | ✔ | Ảnh JPEG/PNG, mỗi ảnh tối đa `Upload:MaxFileSizeInMB`. Gửi nhiều ảnh bằng cách lặp lại trường `anh` (tối đa `DiemDanh:MaxImagesPerCheckIn`, mặc định 5; tổng request ≤ `Upload:MaxRequestSizeInMB`) |
| `latitude`, `longitude` | ✔ | Dấu chấm thập phân, ví dụ `20.2506149` |
| `accuracy` | | `coords.accuracy` (mét) |
| `loaiCongViec` | * | Một mã trong `/loai-cong-viec` |
| `ghiChu` | * | ≤ 500 ký tự. (*) Cần ít nhất một trong `loaiCongViec` / `ghiChu` |
| `clientTime` | | Giờ điện thoại ISO 8601, chỉ để đối chiếu |

Trả về `{ diemDanhId, thoiGian, diaChi, canhBao: [], anhUrl, soAnh }`.
Lỗi nghiệp vụ trả **400** `{ message }` (thiếu GPS, ảnh không hợp lệ, bấm lại trong vòng
`DiemDanh:MinSecondsBetweenCheckIns` giây...). Lỗi lưu ảnh/CSDL trả **502** kèm mã tra cứu.

### Quản lý (VIEW / EXPORT)

| Method | Đường dẫn | Mô tả |
|---|---|---|
| GET | `/don-vi` | Đơn vị được phép lọc: `[{ maDv, tenDv }]` (maDv = 7 ký tự đầu) |
| GET | `/to?maDv=` | Tổ của đơn vị: `[{ maTo, tenTo }]` (maTo = toàn bộ ma_dv) |
| GET | `/ban-do?ngay=&maDv=&maTo=` | Mọi lượt điểm danh trong ngày để chấm bản đồ (tối đa 5000) |
| GET | `/tong-hop?ngay=&maDv=&maTo=` | Ai đã điểm danh, mấy lần: đơn vị → tổ → nhân viên |
| GET | `/lich-su?tuNgay=&denNgay=&maDv=&maTo=&maNd=&search=&chiCanhBao=&trangThai=&page=&pageSize=` | Lịch sử phân trang `{ items, totalCount }`, tối đa 92 ngày |
| GET | `/lich-su/export?...` | Excel cùng bộ lọc (cần EXPORT + VIEW), tối đa 50.000 dòng |
| POST | `/{id}/duyet` | Duyệt/từ chối: `{ trangThai: "DA_DUYET" \| "TU_CHOI" \| "CHO_DUYET", lyDo }` (lý do bắt buộc khi từ chối). Cần APPROVE + VIEW, trong phạm vi (duyệt được cả lượt của mình). Trả lượt đã cập nhật |
| DELETE | `/{id}` | Xóa lượt điểm danh và ảnh trên MinIO (cần DELETE + VIEW, trong phạm vi). 204 / 404 |

Quy ước mã: **mã đơn vị = 7 ký tự đầu của `ma_dv`**, **mã tổ = toàn bộ `ma_dv`** (tên tổ lấy từ `v_donvi`).

### Ảnh

`GET /api/diemdanh/{id}/anh?i=0` — ảnh thứ `i` (0 = ảnh đầu, tới `soAnh - 1`; mỗi lượt trong danh sách có `soAnh`). Người điểm danh luôn xem được ảnh của mình; người khác cần
VIEW và lượt điểm danh nằm trong phạm vi dữ liệu.

## Cờ bất thường (`canhBao`)

Không chặn điểm danh, chỉ đánh dấu để quản lý xem lại:

| Mã | Ý nghĩa |
|---|---|
| `TRUNG_ANH` | Ảnh trùng mã băm với một ảnh đã gửi trước đó |
| `GPS_KEM` | Sai số GPS lớn hơn `DiemDanh:LowAccuracyMeters` |
| `LECH_GIO` | Giờ điện thoại lệch quá `DiemDanh:MaxClockSkewMinutes` so với giờ máy chủ |

## Cấu hình

```json
"Geocoding": {
  "Provider": "Nominatim",     // None | Nominatim | Custom
  "BaseUrl": "https://nominatim.openstreetmap.org", // Nominatim
  "UrlTemplate": "",           // Custom: ví dụ https://<mapvnpt>/reverse?lat={lat}&lng={lng}&key={key}
  "AddressJsonPath": "",       // Custom: trường địa chỉ trong JSON trả về, ví dụ results[0].formatted_address
  "ApiKey": "",
  "Language": "vi",
  "UserAgent": "VNPT-NBH-DTNTB/1.0 (+https://clm.vnptninhbinh.com.vn)",
  "TimeoutSeconds": 5
},
"DiemDanh": {
  "MaxDimension": 1600,              // Cạnh dài tối đa của ảnh sau khi xử lý
  "JpegQuality": 82,
  "MinSecondsBetweenCheckIns": 60,   // Chống bấm liên tục
  "LowAccuracyMeters": 200,
  "MaxClockSkewMinutes": 15,
  "MaxImagesPerCheckIn": 5,          // Số ảnh tối đa cho một lượt điểm danh
  "ServerUtcOffsetHours": 7,
  "WatermarkFont": ""                // Để trống: tự dò Arial (Windows) / DejaVu Sans (Linux)
}
```

Font in chữ lên ảnh phải có dấu tiếng Việt. Dockerfile đã cài `fonts-dejavu-core`;
trên Windows/IIS dùng sẵn Arial.

## Duyệt điểm danh

Mỗi lượt có `trangThai`: `CHO_DUYET` (mặc định khi gửi) → `DA_DUYET` hoặc `TU_CHOI` (kèm `lyDoTuChoi`).
Duyệt tay từng lượt, không tự động, không hạn chót; người duyệt có thể đổi kết quả hoặc đưa về chờ duyệt.
Mỗi lượt lưu người duyệt (`nguoiDuyet`, `tenNguoiDuyet`) và `thoiGianDuyet` (giờ máy chủ).
Tổng hợp theo ngày có `soDaDuyet`, `soChoDuyet`, `soTuChoi` cho từng người, `soChoDuyet` cho tổ/đơn vị.
Excel lịch sử thêm 4 cột: Trạng thái duyệt, Người duyệt, Thời gian duyệt, Lý do từ chối.
Chạy `DatabaseScripts/20261016_diemdanh_duyet.sql` trước khi cập nhật API.
