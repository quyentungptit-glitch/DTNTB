-- ============================================================
-- ĐIỂM DANH HIỆN TRƯỜNG
-- Chạy một lần trên Oracle trước khi deploy API có DiemDanhController.
-- Mỗi lượt điểm danh = 1 ảnh chụp tại chỗ + toạ độ GPS + địa chỉ + ghi chú.
-- Giờ ghi nhận luôn là giờ máy chủ (SYSDATE), không tin giờ điện thoại.
-- ============================================================

CREATE TABLE dtntb_diemdanh
(
    diemdanh_id     NUMBER(19)      NOT NULL,
    ngay            DATE            NOT NULL,   -- TRUNC(created_at), dùng để lọc theo ngày
    created_at      DATE            NOT NULL,   -- Giờ máy chủ lúc ghi nhận
    client_time     DATE,                       -- Giờ điện thoại gửi lên (chỉ để đối chiếu)

    -- Thông tin người điểm danh, chụp lại tại thời điểm điểm danh để lịch sử
    -- không bị đổi khi nhân sự chuyển tổ/đơn vị về sau.
    ma_nd           VARCHAR2(100)   NOT NULL,
    ma_nv           VARCHAR2(50),
    ten_nv          VARCHAR2(200),
    ma_dv           VARCHAR2(30),               -- Mã tổ = toàn bộ ma_dv của nhân viên; 7 ký tự đầu = mã đơn vị
    ten_to          VARCHAR2(300),              -- Tên tổ (v_donvi theo toàn bộ ma_dv)
    ten_dv          VARCHAR2(300),              -- Tên đơn vị (v_donvi_diaban theo 7 ký tự đầu)

    latitude        NUMBER(10, 7)   NOT NULL,
    longitude       NUMBER(10, 7)   NOT NULL,
    accuracy_m      NUMBER(10, 2),              -- Sai số GPS (mét) do trình duyệt báo
    dia_chi         VARCHAR2(500),              -- Địa chỉ đổi từ toạ độ (có thể rỗng nếu dịch vụ bản đồ lỗi)

    loai_cong_viec  VARCHAR2(50),               -- Mã việc chọn nhanh: SUA_SU_CO, LAP_DAT...
    ghi_chu         VARCHAR2(500),

    image_path      VARCHAR2(500)   NOT NULL,   -- Đường dẫn ảnh trên Storage server
    image_sha256    VARCHAR2(64)    NOT NULL,   -- Mã băm ảnh gốc, phát hiện gửi trùng ảnh
    canh_bao        VARCHAR2(200),              -- Danh sách cờ bất thường, cách nhau dấu phẩy

    user_agent      VARCHAR2(300),
    client_ip       VARCHAR2(64),

    CONSTRAINT pk_dtntb_diemdanh PRIMARY KEY (diemdanh_id),
    CONSTRAINT ck_dtntb_diemdanh_lat CHECK (latitude BETWEEN -90 AND 90),
    CONSTRAINT ck_dtntb_diemdanh_lng CHECK (longitude BETWEEN -180 AND 180)
);

CREATE SEQUENCE seq_dtntb_diemdanh START WITH 1 INCREMENT BY 1 NOCACHE;

-- Màn hình quản lý lọc theo ngày + đơn vị; nhân viên xem theo chính mình.
CREATE INDEX ix_dtntb_diemdanh_ngay_dv  ON dtntb_diemdanh (ngay, ma_dv);
CREATE INDEX ix_dtntb_diemdanh_nd_time  ON dtntb_diemdanh (ma_nd, created_at);
CREATE INDEX ix_dtntb_diemdanh_sha256   ON dtntb_diemdanh (image_sha256);

-- ============================================================
-- PERMISSION: không tự gán cho role nào; gán trong form Phân quyền.
--   CHECKIN : nhân viên được điểm danh
--   VIEW    : xem bản đồ / danh sách / lịch sử trong phạm vi dữ liệu của role
--   EXPORT  : xuất Excel lịch sử
-- ============================================================
MERGE INTO dtntb_sys_permissions target
USING (
    SELECT 'PERMISSIONS.DIEMDANH.CHECKIN' AS permission_code, 'Điểm danh hiện trường' AS permission_name, 'DIEMDANH' AS module_group FROM dual
    UNION ALL SELECT 'PERMISSIONS.DIEMDANH.VIEW', 'Xem theo dõi điểm danh', 'DIEMDANH' FROM dual
    UNION ALL SELECT 'PERMISSIONS.DIEMDANH.EXPORT', 'Xuất Excel lịch sử điểm danh', 'DIEMDANH' FROM dual
) source
ON (UPPER(TRIM(target.permission_code)) = source.permission_code)
WHEN MATCHED THEN UPDATE SET target.permission_name = source.permission_name, target.module_group = source.module_group
WHEN NOT MATCHED THEN INSERT (permission_code, permission_name, module_group)
VALUES (source.permission_code, source.permission_name, source.module_group);

-- ============================================================
-- MENU: chỉ hiện sau khi gán cho role trong form Phân Menu.
-- ============================================================
MERGE INTO dtntb_sys_menus target
USING (
    SELECT 'MENU_DIEMDANH' AS menu_code,
           'Điểm danh hiện trường' AS menu_name,
           'ĐIỂM DANH' AS menu_group,
           '/diemdanh' AS route_url,
           'fa fa-camera' AS icon,
           5 AS order_index,
           1 AS is_active
    FROM dual
    UNION ALL
    SELECT 'MENU_DIEMDANH_THEODOI', 'Theo dõi điểm danh', 'ĐIỂM DANH', '/diemdanh/theo-doi', 'fa fa-map-marker', 6, 1 FROM dual
) source
ON (UPPER(TRIM(target.menu_code)) = source.menu_code)
WHEN MATCHED THEN UPDATE SET
    target.menu_name = source.menu_name,
    target.menu_group = source.menu_group,
    target.route_url = source.route_url,
    target.icon = source.icon,
    target.order_index = source.order_index,
    target.is_active = source.is_active
WHEN NOT MATCHED THEN INSERT
    (menu_code, menu_name, menu_group, route_url, icon, order_index, is_active)
VALUES
    (source.menu_code, source.menu_name, source.menu_group, source.route_url, source.icon, source.order_index, source.is_active);

COMMIT;
