-- ============================================================
-- Điểm danh: DUYỆT từng lượt điểm danh (duyệt tay, không tự động, không hạn chót).
-- Chạy sau 20261015d_diemdanh_nhieu_anh.sql. Chạy lại nhiều lần không sao.
--   trang_thai      : CHO_DUYET (mặc định) | DA_DUYET | TU_CHOI
--   nguoi_duyet     : ma_nd người duyệt/từ chối gần nhất; ten_nguoi_duyet: tên lúc duyệt
--   thoi_gian_duyet : giờ máy chủ lúc duyệt
--   ly_do_tu_choi   : bắt buộc khi từ chối
-- Các lượt đã có trước khi chạy script sẽ ở trạng thái CHO_DUYET.
-- ============================================================
DECLARE
    e_column_exists EXCEPTION;
    PRAGMA EXCEPTION_INIT(e_column_exists, -1430);
    PROCEDURE add_col(p_sql VARCHAR2) IS
    BEGIN
        EXECUTE IMMEDIATE p_sql;
    EXCEPTION
        WHEN e_column_exists THEN NULL;
    END;
BEGIN
    add_col('ALTER TABLE dtntb_diemdanh ADD (trang_thai VARCHAR2(20) DEFAULT ''CHO_DUYET'' NOT NULL)');
    add_col('ALTER TABLE dtntb_diemdanh ADD (nguoi_duyet VARCHAR2(100))');
    add_col('ALTER TABLE dtntb_diemdanh ADD (ten_nguoi_duyet VARCHAR2(200))');
    add_col('ALTER TABLE dtntb_diemdanh ADD (thoi_gian_duyet DATE)');
    add_col('ALTER TABLE dtntb_diemdanh ADD (ly_do_tu_choi VARCHAR2(500))');
END;
/

DECLARE
    e_exists EXCEPTION;
    PRAGMA EXCEPTION_INIT(e_exists, -2264); -- tên ràng buộc đã tồn tại
BEGIN
    EXECUTE IMMEDIATE 'ALTER TABLE dtntb_diemdanh ADD CONSTRAINT ck_dtntb_diemdanh_tt
        CHECK (trang_thai IN (''CHO_DUYET'', ''DA_DUYET'', ''TU_CHOI''))';
EXCEPTION
    WHEN e_exists THEN NULL;
END;
/

DECLARE
    e_index_exists EXCEPTION;
    PRAGMA EXCEPTION_INIT(e_index_exists, -955);
BEGIN
    EXECUTE IMMEDIATE 'CREATE INDEX ix_dtntb_diemdanh_tt_ngay ON dtntb_diemdanh (trang_thai, ngay)';
EXCEPTION
    WHEN e_index_exists THEN NULL;
END;
/

-- Quyền duyệt: gán trong form Phân quyền (cần kèm VIEW). Phạm vi duyệt theo data_scope của role
-- (toàn tỉnh / địa bàn / đơn vị / tổ), duyệt được cả lượt của chính mình.
MERGE INTO dtntb_sys_permissions target
USING (
    SELECT 'PERMISSIONS.DIEMDANH.APPROVE' AS permission_code, 'Duyệt điểm danh' AS permission_name, 'DIEMDANH' AS module_group FROM dual
) source
ON (UPPER(TRIM(target.permission_code)) = source.permission_code)
WHEN MATCHED THEN UPDATE SET target.permission_name = source.permission_name, target.module_group = source.module_group
WHEN NOT MATCHED THEN INSERT (permission_code, permission_name, module_group)
VALUES (source.permission_code, source.permission_name, source.module_group);

COMMIT;
