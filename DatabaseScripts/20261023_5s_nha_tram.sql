-- ============================================================
-- 5S NHÀ TRẠM: giao trạm cho nhân viên, nhân viên tải ảnh theo tiêu chí, bộ phận chấm điểm, báo cáo.
-- Danh mục trạm dùng bảng có sẵn DTNTB_CSHT (ma_csht, ten_csht, ma_toql, ten_toql, ma_dv, ten_dv,
-- phuongxa, diachi, toado_long, toado_lat).
-- Mỗi phiếu = 1 lần đi 5S (1 lần chấm). Trạng thái: DA_GIAO -> DA_NOP -> DA_CHAM (THU_HOI nếu giao nhầm).
-- Ảnh lưu MinIO (bucket điểm danh), thư mục 5s/yyyy-MM/.
-- ============================================================

-- 1. Bộ tiêu chí (tạm 1 tiêu chí chung; thêm dòng là có thêm tiêu chí, không cần sửa code)
CREATE TABLE dtntb_5s_tieuchi
(
    tieuchi_id          NUMBER(10)          NOT NULL,
    ma                  VARCHAR2(30)        NOT NULL,          -- ND_I.1 ...
    noi_dung            VARCHAR2(300 CHAR)  NOT NULL,
    yeu_cau_anh         VARCHAR2(1000 CHAR),
    sl_anh_toi_thieu    NUMBER(3)           DEFAULT 1 NOT NULL,
    diem_toi_da         NUMBER(6, 2)        NOT NULL,
    bat_buoc            NUMBER(1)           DEFAULT 1 NOT NULL, -- 1: phải đủ ảnh mới nộp được
    thu_tu              NUMBER(5)           DEFAULT 0 NOT NULL,
    is_active           NUMBER(1)           DEFAULT 1 NOT NULL,
    CONSTRAINT pk_dtntb_5s_tieuchi PRIMARY KEY (tieuchi_id),
    CONSTRAINT uq_dtntb_5s_tieuchi_ma UNIQUE (ma)
);

INSERT INTO dtntb_5s_tieuchi (tieuchi_id, ma, noi_dung, yeu_cau_anh, sl_anh_toi_thieu, diem_toi_da, bat_buoc, thu_tu)
VALUES (1, '5S', 'Đánh giá 5S chung nhà trạm',
        'Chụp toàn cảnh nhà trạm, khuôn viên, cột anten, hệ thống thiết bị, cáp/máng cáp, ODF, điều hòa, máy phát điện, sổ nhật ký trạm.',
        1, 100, 1, 1);

-- 2. Phiếu: thông tin trạm được chụp lại lúc giao để báo cáo không đổi khi danh mục thay đổi
CREATE TABLE dtntb_5s_phieu
(
    phieu_id        NUMBER(19)          NOT NULL,
    thang           NUMBER(6)           NOT NULL,          -- yyyyMM theo ngày giao
    lan             NUMBER(4)           DEFAULT 1 NOT NULL, -- lần thứ mấy của trạm trong tháng
    ma_csht         VARCHAR2(50)        NOT NULL,
    ten_csht        VARCHAR2(300 CHAR),
    ma_dv           VARCHAR2(30),
    ten_dv          VARCHAR2(300 CHAR),
    ma_toql         VARCHAR2(30),
    ten_toql        VARCHAR2(300 CHAR),
    diachi          VARCHAR2(500 CHAR),
    toado_lat       NUMBER(10, 7),
    toado_long      NUMBER(10, 7),

    ma_nd_nhan      VARCHAR2(100)       NOT NULL,          -- tài khoản nhân viên được giao
    ma_nv_nhan      VARCHAR2(50),
    ten_nv_nhan     VARCHAR2(200 CHAR),
    nguoi_giao      VARCHAR2(100)       NOT NULL,
    ten_nguoi_giao  VARCHAR2(200 CHAR),
    ngay_giao       DATE                DEFAULT SYSDATE NOT NULL,
    ghichu_giao     VARCHAR2(500 CHAR),

    trang_thai      VARCHAR2(20)        DEFAULT 'DA_GIAO' NOT NULL,
    ngay_nop        DATE,
    ghichu_nop      VARCHAR2(500 CHAR),

    tong_diem       NUMBER(7, 2),
    diem_toi_da     NUMBER(7, 2),
    xep_loai        VARCHAR2(20),
    nhan_xet        VARCHAR2(1000 CHAR),
    nguoi_cham      VARCHAR2(100),
    ten_nguoi_cham  VARCHAR2(200 CHAR),
    ngay_cham       DATE,

    ngay_thu_hoi    DATE,
    nguoi_thu_hoi   VARCHAR2(100),

    CONSTRAINT pk_dtntb_5s_phieu PRIMARY KEY (phieu_id),
    CONSTRAINT ck_dtntb_5s_phieu_tt CHECK (trang_thai IN ('DA_GIAO', 'DA_NOP', 'DA_CHAM', 'THU_HOI'))
);
CREATE SEQUENCE seq_dtntb_5s_phieu START WITH 1 INCREMENT BY 1 NOCACHE;
CREATE INDEX ix_dtntb_5s_phieu_csht ON dtntb_5s_phieu (ma_csht, trang_thai);
CREATE INDEX ix_dtntb_5s_phieu_nd ON dtntb_5s_phieu (ma_nd_nhan, trang_thai);
CREATE INDEX ix_dtntb_5s_phieu_thang ON dtntb_5s_phieu (thang, ma_dv);

-- 3. Ảnh theo tiêu chí
CREATE TABLE dtntb_5s_anh
(
    anh_id          NUMBER(19)          NOT NULL,
    phieu_id        NUMBER(19)          NOT NULL,
    tieuchi_id      NUMBER(10)          NOT NULL,
    image_path      VARCHAR2(500)       NOT NULL,          -- khoá MinIO: 5s/yyyy-MM/<phieu>_<anh>.jpg
    image_sha256    VARCHAR2(64)        NOT NULL,
    file_name       VARCHAR2(200 CHAR),
    ngay_tai        DATE                DEFAULT SYSDATE NOT NULL,
    chup_luc        DATE,                                  -- giờ chụp đọc từ EXIF (nếu có)
    latitude        NUMBER(10, 7),                         -- toạ độ EXIF, không có thì toạ độ máy lúc tải
    longitude       NUMBER(10, 7),
    nguon_toado     VARCHAR2(10),                          -- EXIF | THIET_BI
    khoang_cach_m   NUMBER(10),                            -- khoảng cách tới toạ độ trạm
    canh_bao        VARCHAR2(200),                         -- XA_TRAM, KHONG_GPS, TRUNG_ANH (cách nhau dấu phẩy)
    CONSTRAINT pk_dtntb_5s_anh PRIMARY KEY (anh_id),
    CONSTRAINT fk_dtntb_5s_anh_phieu FOREIGN KEY (phieu_id) REFERENCES dtntb_5s_phieu (phieu_id) ON DELETE CASCADE
);
CREATE SEQUENCE seq_dtntb_5s_anh START WITH 1 INCREMENT BY 1 NOCACHE;
CREATE INDEX ix_dtntb_5s_anh_phieu ON dtntb_5s_anh (phieu_id, tieuchi_id);
CREATE INDEX ix_dtntb_5s_anh_sha ON dtntb_5s_anh (image_sha256);

-- 4. Điểm theo tiêu chí
CREATE TABLE dtntb_5s_diem
(
    phieu_id        NUMBER(19)          NOT NULL,
    tieuchi_id      NUMBER(10)          NOT NULL,
    diem            NUMBER(6, 2)        NOT NULL,
    ghi_chu         VARCHAR2(500 CHAR),
    CONSTRAINT pk_dtntb_5s_diem PRIMARY KEY (phieu_id, tieuchi_id),
    CONSTRAINT fk_dtntb_5s_diem_phieu FOREIGN KEY (phieu_id) REFERENCES dtntb_5s_phieu (phieu_id) ON DELETE CASCADE
);

-- ============================================================
-- QUYỀN: gán trong form Phân quyền. Phạm vi dữ liệu theo data_scope của role.
-- ============================================================
MERGE INTO dtntb_sys_permissions target
USING (
    SELECT 'PERMISSIONS.NAMS.GIAO' AS permission_code, 'Giao trạm 5S cho nhân viên' AS permission_name, 'NAMS' AS module_group FROM dual
    UNION ALL SELECT 'PERMISSIONS.NAMS.THUC_HIEN', 'Thực hiện 5S (tải ảnh, nộp)', 'NAMS' FROM dual
    UNION ALL SELECT 'PERMISSIONS.NAMS.CHAM', 'Chấm điểm 5S nhà trạm', 'NAMS' FROM dual
    UNION ALL SELECT 'PERMISSIONS.NAMS.VIEW', 'Xem báo cáo 5S nhà trạm', 'NAMS' FROM dual
    UNION ALL SELECT 'PERMISSIONS.NAMS.EXPORT', 'Xuất Excel báo cáo 5S', 'NAMS' FROM dual
) source
ON (UPPER(TRIM(target.permission_code)) = source.permission_code)
WHEN MATCHED THEN UPDATE SET target.permission_name = source.permission_name, target.module_group = source.module_group
WHEN NOT MATCHED THEN INSERT (permission_code, permission_name, module_group)
VALUES (source.permission_code, source.permission_name, source.module_group);

-- ============================================================
-- MENU: gán cho role trong form Phân Menu.
-- ============================================================
MERGE INTO dtntb_sys_menus target
USING (
    SELECT 'MENU_5S_CUA_TOI' AS menu_code, '5S của tôi' AS menu_name, '5S NHÀ TRẠM' AS menu_group,
           '/5s' AS route_url, 'fa fa-camera-retro' AS icon, 30 AS order_index, 1 AS is_active FROM dual
    UNION ALL SELECT 'MENU_5S_GIAO', 'Giao trạm 5S', '5S NHÀ TRẠM', '/5s/giao', 'fa fa-tower-broadcast', 31, 1 FROM dual
    UNION ALL SELECT 'MENU_5S_CHAM', 'Chấm điểm 5S', '5S NHÀ TRẠM', '/5s/cham', 'fa fa-star-half-stroke', 32, 1 FROM dual
    UNION ALL SELECT 'MENU_5S_BAO_CAO', 'Báo cáo 5S', '5S NHÀ TRẠM', '/5s/bao-cao', 'fa fa-chart-column', 33, 1 FROM dual
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
