# API Giao phiếu GHTT (dùng chung với BCBG)

Chuyển 2 form của BCBG (`DiemNguyCoTB/GhttGiaoPhieu.aspx`, `DiemNguyCoTB/GhttPhieuCuaToi.aspx`) sang DTNTB.
**Dùng chung bảng** `GHTT_PHIEU`, `GHTT_PHIEU_LS`, `GHTT_DM_LYDO` (schema `ConnectionString_NBH`):
phiếu giao bên nào thì bên kia cũng thấy; Biểu 8 (`GHTT_BIEU_8`) vẫn import bên BCBG.

## Triển khai

1. Bảng phải có sẵn từ BCBG: `20261022_ghtt_phieu_giao.sql` (+ `20261022_ghtt_phieu_giao_sua.sql` nếu đã chạy bản đầu).
2. Chạy `DatabaseScripts/20261022_ghtt_phieu_dtntb.sql` (2 quyền + 2 menu, cùng nhóm với menu `/ghtt`).
3. Gán quyền trong form Phân quyền và menu trong form Phân Menu, đăng xuất / đăng nhập lại.

| Quyền | Dùng cho |
|---|---|
| `PERMISSIONS.GHTT.PHIEU_GIAO` | Màn Giao phiếu (`/ghtt/giao-phieu`): xem TB cần tiếp cận, giao phiếu |
| `PERMISSIONS.GHTT.PHIEU_XU_LY` | Màn Phiếu GHTT (`/ghtt/phieu`): xem phiếu, cập nhật Thành công / Không thành công (kể cả nhập hộ) |
| `PERMISSIONS.GHTT.EXPORT` (có sẵn) | Xuất Excel danh sách giao (cần kèm PHIEU_GIAO) |

## Phạm vi dữ liệu (data_scope của role)

| Scope | Giao phiếu (TB theo đơn vị quản lý / NV thực hiện) | Phiếu GHTT |
|---|---|---|
| TOAN_TINH | Tất cả, chọn đơn vị | Tất cả phiếu |
| DIA_BAN | Đơn vị thuộc địa bàn (`v_donvi_diaban`) | Phiếu của các đơn vị đó |
| DON_VI | Cùng 7 ký tự đầu mã đơn vị | Phiếu của đơn vị |
| TO_QL | TB có NV thực hiện thuộc tổ (NV trong `v_nguoidung_diaban` cùng toàn bộ `ma_dv`) | Phiếu giao cho NV thuộc tổ |
| NHAN_VIEN | TB mình là NV thực hiện | Chỉ phiếu giao cho mình (`ma_nv` trong JWT) |

Mã đơn vị `215.7xxx` được quy về `215.3xxx` khi so sánh (như BCBG). Người thao tác ghi vào phiếu bằng `ma_nv`
(thiếu thì dùng tài khoản đăng nhập); lịch sử ghi `[Nhập hộ NV ...]` khi người cập nhật khác NV nhận phiếu và có hậu tố `[DTNTB]`.

## Quy tắc (giữ nguyên BCBG)

- Nguồn: tháng chọn, lần import mới nhất của từng phân vùng; `LOAITB_ID = 58`, `NHANVIEN_TH_ID > 0`, `NGAYHEN_BH_TU IS NULL`.
- Phiếu giao cho đúng NV thực hiện, hạn 72 giờ. TB còn phiếu trong hạn không giao thêm; hết hạn chưa xử lý thì phiếu cũ
  treo và được giao phiếu mới. Khi giao khóa bảng `GHTT_PHIEU` trong giao dịch ngắn để không giao trùng.
- Phiếu đang mở (kể cả quá hạn) → Thành công / Không thành công (bắt buộc lý do); phiếu Không thành công được đổi sang Thành công.

## Endpoints (`/api/ghtt-phieu`)

| Method | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/thang` | GIAO hoặc XU_LY | Các tháng có Biểu 8 |
| GET | `/don-vi` | PHIEU_GIAO | Đơn vị chọn được `[{ maDv, tenDv }]` |
| GET | `/thue-bao?thang=&maDv=` | PHIEU_GIAO | TB cần tiếp cận + phiếu gần nhất (`tinhTrang`, `duocGiao`) |
| POST | `/giao` | PHIEU_GIAO | `{ thang, maDv, khoa: ["pv\|tb"], ghiChu }` → `{ thanhCong, boQua, hanXl, ghiChu[] }` |
| GET | `/thue-bao/export?thang=&maDv=&doiVtId=&maNv=&search=&danhSach=&trangThai=` | EXPORT + PHIEU_GIAO | Excel theo bộ lọc |
| GET | `/phieu?tab=CAN\|QUA_HAN\|XONG&maDv=&doiVtId=&maNv=` | PHIEU_XU_LY | `{ items, dem: { can, quaHan, xong } }` |
| GET | `/phieu/bo-loc` | PHIEU_XU_LY | Giá trị lọc đơn vị / tổ / NV nhận trong phạm vi |
| GET | `/ly-do` | PHIEU_XU_LY | Gợi ý lý do không thành công |
| POST | `/phieu/{id}/ket-qua` | PHIEU_XU_LY | `{ thanhCong, lyDo, ghiChu, ngayHen: "yyyy-MM-dd" }` |
