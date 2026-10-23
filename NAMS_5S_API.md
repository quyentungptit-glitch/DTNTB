# API 5S nhà trạm (`/api/5s`)

Luồng: **Giao trạm** (NAMS.GIAO) → nhân viên **tải ảnh, nộp** (NAMS.THUC_HIEN) → **chấm điểm** (NAMS.CHAM) → **báo cáo** (NAMS.VIEW / NAMS.EXPORT).
Phạm vi dữ liệu theo `data_scope` trong JWT: TOAN_TINH, DIA_BAN, DON_VI (7 ký tự đầu `ma_dv`), TO_QL (`ma_toql` = `ma_dv` người dùng), NHAN_VIEN.

## Cài đặt

1. Chạy `DatabaseScripts/20261023_5s_nha_tram.sql` (bảng `DTNTB_5S_TIEUCHI`, `DTNTB_5S_PHIEU`, `DTNTB_5S_ANH`, `DTNTB_5S_DIEM`, quyền `PERMISSIONS.NAMS.*`, 4 menu nhóm "5S NHÀ TRẠM").
2. Gán quyền / menu cho vai trò trong màn Quản trị hệ thống.
3. Ảnh lưu MinIO cùng bucket với điểm danh, khoá `5s/yyyy-MM/{phieu}_{anh}.jpg`.
4. Cấu hình tuỳ chọn (không có thì dùng mặc định trong code), mục `NamS` trong appsettings:

```json
"NamS": {
  "FarFromSiteMeters": 300,
  "MaxImagesPerUpload": 10,
  "MaxImagesPerPhieu": 60,
  "MaxDimension": 2048,
  "JpegQuality": 85,
  "XepLoai": { "Tot": 90, "Kha": 75, "TrungBinh": 50 }
}
```

## Trạng thái phiếu

| Mã | Ý nghĩa |
|---|---|
| DA_GIAO | Đã giao, nhân viên đang thực hiện (mỗi trạm chỉ 1 phiếu DA_GIAO) |
| DA_NOP | Nhân viên đã nộp, chờ chấm |
| DA_CHAM | Đã chấm (chấm lại được, ghi đè) |
| THU_HOI | Người giao thu hồi trước khi nộp (ảnh bị xoá) |

Xếp loại theo % tổng điểm: TOT ≥ 90, KHA ≥ 75, TB ≥ 50, KEM < 50.

Cảnh báo ảnh: `XA_TRAM` (cách trạm > 300 m), `KHONG_GPS` (không có toạ độ), `TRUNG_ANH` (trùng SHA-256 với ảnh đã tải).

## Endpoint

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/tieu-chi` | bất kỳ NAMS.* | Bộ tiêu chí đang dùng |
| GET | `/xep-loai` | bất kỳ NAMS.* | Thang xếp loại |
| GET | `/don-vi`, `/to?maDv=` | GIAO/CHAM/VIEW/EXPORT | Đơn vị / tổ trong phạm vi (từ DTNTB_CSHT) |
| GET | `/tram?maDv=&maToQl=&search=` | GIAO | Trạm trong phạm vi + phiếu đang giao + kết quả gần nhất (tối đa 5.000) |
| GET | `/nhan-vien?maDv=&maToQl=` | GIAO | Nhân viên trong phạm vi (v_nguoidung_diaban) + số phiếu đang mở |
| POST | `/giao` `{maCsht[], maNd, ghiChu}` | GIAO | Giao trạm; bỏ qua trạm đang có phiếu hoặc ngoài phạm vi |
| GET | `/dang-giao?maDv=&maToQl=` | GIAO | Phiếu DA_GIAO trong phạm vi |
| POST | `/phieu/{id}/thu-hoi` | GIAO | Thu hồi phiếu chưa nộp |
| GET | `/cua-toi` | THUC_HIEN | Phiếu DA_GIAO của tôi |
| GET | `/phieu/{id}` | NAMS.* | Chi tiết phiếu + tiêu chí + điểm + ảnh (`duocSua`, `duocCham`) |
| POST | `/phieu/{id}/anh` (multipart: `tieuChiId`, `anh`, `meta`) | THUC_HIEN | Tải ảnh; `meta` = JSON mảng `{lat,lng,nguon:'EXIF'|'THIET_BI',chupLuc,fileName}` theo thứ tự ảnh |
| DELETE | `/anh/{anhId}` | THUC_HIEN | Xoá ảnh (phiếu của mình, chưa nộp) |
| POST | `/phieu/{id}/nop` `{ghiChu}` | THUC_HIEN | Nộp; kiểm tra đủ số ảnh tối thiểu từng tiêu chí bắt buộc |
| GET | `/anh/{anhId}` | NAMS.* | Ảnh JPEG (người được giao, hoặc GIAO/CHAM/VIEW trong phạm vi) |
| GET | `/phieu?tuThang=&denThang=&maDv=&maToQl=&trangThai=&xepLoai=&search=&page=&pageSize=` | CHAM hoặc VIEW | Danh sách phiếu; `trangThai` = DA_GIAO/DA_NOP/DA_CHAM/NOP_CHAM/rỗng (trừ THU_HOI) |
| POST | `/phieu/{id}/cham` `{diem:[{tieuChiId,diem,ghiChu}], nhanXet}` | CHAM | Chấm điểm (phiếu DA_NOP/DA_CHAM trong phạm vi) |
| GET | `/tong-hop?tuThang=&denThang=&maDv=` | VIEW hoặc EXPORT | Tổng hợp theo đơn vị → tổ |
| GET | `/export?...` | EXPORT | Excel 2 sheet: TongHop, ChiTiet |

Tháng dạng `yyyyMM` (ví dụ `202610`); phiếu ghi tháng theo ngày giao.
