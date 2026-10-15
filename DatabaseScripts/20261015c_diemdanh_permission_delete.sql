-- ============================================================
-- Điểm danh: thêm quyền XÓA lượt điểm danh (kèm ảnh trên MinIO).
-- Chạy sau 20261015_create_diemdanh.sql. Chạy lại nhiều lần không sao.
-- Không tự gán cho role nào; gán trong form Phân quyền (cần kèm VIEW).
-- ============================================================
MERGE INTO dtntb_sys_permissions target
USING (
    SELECT 'PERMISSIONS.DIEMDANH.DELETE' AS permission_code, 'Xóa lượt điểm danh' AS permission_name, 'DIEMDANH' AS module_group FROM dual
) source
ON (UPPER(TRIM(target.permission_code)) = source.permission_code)
WHEN MATCHED THEN UPDATE SET target.permission_name = source.permission_name, target.module_group = source.module_group
WHEN NOT MATCHED THEN INSERT (permission_code, permission_name, module_group)
VALUES (source.permission_code, source.permission_name, source.module_group);

COMMIT;
