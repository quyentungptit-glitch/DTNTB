-- Chỉ chạy nếu đã chạy 20261015_create_diemdanh.sql bản đầu (chưa có cột ten_to).
-- Bổ sung tên tổ để màn Theo dõi điểm danh nhóm được đến cấp tổ.
DECLARE
    e_column_exists EXCEPTION;
    PRAGMA EXCEPTION_INIT(e_column_exists, -1430);
BEGIN
    EXECUTE IMMEDIATE 'ALTER TABLE dtntb_diemdanh ADD (ten_to VARCHAR2(300))';
EXCEPTION
    WHEN e_column_exists THEN NULL;
END;
/

-- Điền tên tổ cho các lượt đã có.
UPDATE dtntb_diemdanh d
SET d.ten_to = (SELECT MAX(t.ten_dv) FROM v_donvi t WHERE TRIM(t.ma_dv) = TRIM(d.ma_dv))
WHERE d.ten_to IS NULL;

COMMIT;
