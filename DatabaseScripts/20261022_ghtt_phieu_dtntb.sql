-- ============================================================
-- GIAO PHIẾU GHTT trên DTNTB (dùng chung bảng với BCBG).
-- Bảng GHTT_PHIEU / GHTT_PHIEU_LS / GHTT_DM_LYDO đã được tạo bên BCBG bằng
--   20261022_ghtt_phieu_giao.sql (+ 20261022_ghtt_phieu_giao_sua.sql nếu đã chạy bản đầu)
-- trên cùng schema ConnectionString_NBH. Script này CHỈ thêm quyền và menu cho DTNTB.
-- Chạy lại nhiều lần không sao.
-- ============================================================

-- Quyền: không tự gán cho role nào; gán trong form Phân quyền.
--   PHIEU_GIAO   : vào màn Giao phiếu, giao phiếu trong phạm vi dữ liệu (toàn tỉnh / địa bàn / đơn vị / tổ)
--   PHIEU_XU_LY  : vào màn Phiếu GHTT, cập nhật kết quả (kể cả nhập hộ) trong phạm vi dữ liệu
--   Xuất Excel dùng quyền có sẵn PERMISSIONS.GHTT.EXPORT
MERGE INTO dtntb_sys_permissions target
USING (
    SELECT 'PERMISSIONS.GHTT.PHIEU_GIAO' AS permission_code, 'Giao phiếu tiếp cận GHTT' AS permission_name, 'GHTT' AS module_group FROM dual
    UNION ALL SELECT 'PERMISSIONS.GHTT.PHIEU_XU_LY', 'Xử lý / cập nhật kết quả phiếu GHTT', 'GHTT' FROM dual
) source
ON (UPPER(TRIM(target.permission_code)) = source.permission_code)
WHEN MATCHED THEN UPDATE SET target.permission_name = source.permission_name, target.module_group = source.module_group
WHEN NOT MATCHED THEN INSERT (permission_code, permission_name, module_group)
VALUES (source.permission_code, source.permission_name, source.module_group);

-- Menu: đặt cùng nhóm với menu báo cáo GHTT đang có (route /ghtt); gán cho role trong form Phân Menu.
MERGE INTO dtntb_sys_menus target
USING (
    SELECT 'MENU_GHTT_GIAO_PHIEU' AS menu_code,
           'Giao phiếu GHTT' AS menu_name,
           NVL((SELECT MAX(m.menu_group) FROM dtntb_sys_menus m WHERE TRIM(m.route_url) = '/ghtt'), 'GIA HẠN TRẢ TRƯỚC') AS menu_group,
           '/ghtt/giao-phieu' AS route_url,
           'fa fa-paper-plane' AS icon,
           NVL((SELECT MAX(m.order_index) FROM dtntb_sys_menus m WHERE TRIM(m.route_url) = '/ghtt'), 20) + 1 AS order_index,
           1 AS is_active
    FROM dual
    UNION ALL
    SELECT 'MENU_GHTT_PHIEU',
           'Phiếu GHTT',
           NVL((SELECT MAX(m.menu_group) FROM dtntb_sys_menus m WHERE TRIM(m.route_url) = '/ghtt'), 'GIA HẠN TRẢ TRƯỚC'),
           '/ghtt/phieu',
           'fa fa-id-card',
           NVL((SELECT MAX(m.order_index) FROM dtntb_sys_menus m WHERE TRIM(m.route_url) = '/ghtt'), 20) + 2,
           1
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
