-- Chạy một lần trước khi deploy form Quản trị thông báo.
-- Không lưu FCM registration token; mọi thông báo được gửi theo Firebase Topic.

CREATE TABLE dtntb_fcm_settings
(
    setting_id    NUMBER(1)      NOT NULL,
    daily_enabled NUMBER(1)      DEFAULT 1 NOT NULL,
    daily_time    VARCHAR2(5)    DEFAULT '08:30' NOT NULL,
    daily_title   VARCHAR2(200)  NOT NULL,
    daily_body    VARCHAR2(1000) NOT NULL,
    updated_by    VARCHAR2(100),
    updated_at    DATE           NOT NULL,
    CONSTRAINT pk_dtntb_fcm_settings PRIMARY KEY (setting_id),
    CONSTRAINT ck_dtntb_fcm_settings_id CHECK (setting_id = 1),
    CONSTRAINT ck_dtntb_fcm_settings_enabled CHECK (daily_enabled IN (0, 1))
);

INSERT INTO dtntb_fcm_settings
    (setting_id, daily_enabled, daily_time, daily_title, daily_body, updated_by, updated_at)
VALUES
    (1, 1, '08:30', '🔔 CẢNH BÁO PHIẾU TỒN ĐỌNG',
     'Chào bạn, hiện tại bạn đang có {count} phiếu đo kiểm chưa thực hiện thực địa. Vui lòng xử lý để tránh quá hạn SLA!',
     'SYSTEM', SYSDATE);

CREATE TABLE dtntb_fcm_notification_history
(
    notification_id      NUMBER(19)     NOT NULL,
    notification_type    VARCHAR2(50)   NOT NULL,
    username             VARCHAR2(100)  NOT NULL,
    topic                VARCHAR2(200)  NOT NULL,
    title                VARCHAR2(200)  NOT NULL,
    message_body         VARCHAR2(1000) NOT NULL,
    status               VARCHAR2(20)   NOT NULL,
    firebase_message_id  VARCHAR2(255),
    error_message        VARCHAR2(1000),
    created_by           VARCHAR2(100),
    created_at           DATE           NOT NULL,
    sent_at              DATE,
    CONSTRAINT pk_dtntb_fcm_notification_history PRIMARY KEY (notification_id),
    CONSTRAINT ck_dtntb_fcm_notification_history_status CHECK (status IN ('SENT', 'FAILED'))
);

CREATE SEQUENCE seq_dtntb_fcm_notification_history START WITH 1 INCREMENT BY 1 NOCACHE;
CREATE INDEX ix_dtntb_fcm_notification_history_created ON dtntb_fcm_notification_history (created_at DESC);

-- Đăng ký permission. Không gán tự động cho bất kỳ role nào.
MERGE INTO dtntb_sys_permissions target
USING (
    SELECT 'PERMISSIONS.THONGBAO.VIEW' AS permission_code, 'Xem quản trị thông báo' AS permission_name, 'THONGBAO' AS module_group FROM dual
    UNION ALL SELECT 'PERMISSIONS.THONGBAO.UPDATE', 'Cập nhật cấu hình thông báo', 'THONGBAO' FROM dual
    UNION ALL SELECT 'PERMISSIONS.THONGBAO.SEND_TEST', 'Gửi thông báo kiểm tra', 'THONGBAO' FROM dual
) source
ON (UPPER(TRIM(target.permission_code)) = source.permission_code)
WHEN MATCHED THEN UPDATE SET target.permission_name = source.permission_name, target.module_group = source.module_group
WHEN NOT MATCHED THEN INSERT (permission_code, permission_name, module_group)
VALUES (source.permission_code, source.permission_name, source.module_group);

-- Đăng ký menu. Menu chỉ hiện sau khi được gán cho role trong form Phân Menu.
MERGE INTO dtntb_sys_menus target
USING (
    SELECT 'MENU_THONGBAO' AS menu_code,
           'Quản trị thông báo' AS menu_name,
           'QUẢN TRỊ HỆ THỐNG' AS menu_group,
           '/thongbao' AS route_url,
           'fa fa-bell' AS icon,
           4 AS order_index,
           1 AS is_active
    FROM dual
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
