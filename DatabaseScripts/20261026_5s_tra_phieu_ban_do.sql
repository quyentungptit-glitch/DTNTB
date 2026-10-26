-- ============================================================
-- 5S NHÀ TRẠM - BỔ SUNG 26/10/2026
--  1. Trả phiếu về nhân viên chụp bổ sung (thay vì xoá phiếu)
--  2. Menu Dashboard 5S (bản đồ + số liệu), đứng đầu nhóm 5S NHÀ TRẠM
-- Chạy SAU script 20261023_5s_nha_tram.sql, TRƯỚC khi chạy bản API mới.
-- ============================================================

ALTER TABLE dtntb_5s_phieu ADD (
    so_lan_tra      NUMBER(3)           DEFAULT 0 NOT NULL,   -- số lần bị trả về
    ly_do_tra       VARCHAR2(500 CHAR),                       -- lý do lần trả gần nhất
    ngay_tra        DATE,
    nguoi_tra       VARCHAR2(100),
    ten_nguoi_tra   VARCHAR2(200 CHAR)
);

MERGE INTO dtntb_sys_menus target
USING (
    SELECT 'MENU_5S_BAN_DO' AS menu_code, 'Dashboard 5S' AS menu_name, '5S NHÀ TRẠM' AS menu_group,
           '/5s/ban-do' AS route_url, 'fa fa-gauge-high' AS icon, 29 AS order_index, 1 AS is_active FROM dual
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
