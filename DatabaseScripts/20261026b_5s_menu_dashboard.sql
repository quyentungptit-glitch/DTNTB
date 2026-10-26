-- Đổi menu "Bản đồ 5S" thành "Dashboard 5S" và đưa lên đầu nhóm 5S NHÀ TRẠM
-- (các menu khác trong nhóm có order_index 30..33).
UPDATE dtntb_sys_menus
SET menu_name = 'Dashboard 5S', icon = 'fa fa-gauge-high', order_index = 29
WHERE UPPER(TRIM(menu_code)) = 'MENU_5S_BAN_DO';

COMMIT;
