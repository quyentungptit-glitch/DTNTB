-- ============================================================
-- Điểm danh: cho phép một lượt điểm danh gửi nhiều ảnh.
-- Chạy sau 20261015_create_diemdanh.sql. Chạy lại nhiều lần không sao.
--   * dtntb_diemdanh.image_path / image_sha256 vẫn là ảnh đầu tiên (giữ tương thích dữ liệu cũ).
--   * so_anh      : tổng số ảnh của lượt (dữ liệu cũ = 1).
--   * dtntb_diemdanh_anh : các ảnh thứ 2 trở đi (thu_tu = 1, 2, ...).
-- ============================================================
DECLARE
    e_column_exists EXCEPTION;
    PRAGMA EXCEPTION_INIT(e_column_exists, -1430);
BEGIN
    EXECUTE IMMEDIATE 'ALTER TABLE dtntb_diemdanh ADD (so_anh NUMBER(2) DEFAULT 1 NOT NULL)';
EXCEPTION
    WHEN e_column_exists THEN NULL;
END;
/

DECLARE
    e_table_exists EXCEPTION;
    PRAGMA EXCEPTION_INIT(e_table_exists, -955);
BEGIN
    EXECUTE IMMEDIATE '
        CREATE TABLE dtntb_diemdanh_anh
        (
            diemdanh_id   NUMBER(19)     NOT NULL,
            thu_tu        NUMBER(2)      NOT NULL,   -- 1 = ảnh thứ hai, 2 = ảnh thứ ba...
            image_path    VARCHAR2(500)  NOT NULL,   -- Khoá ảnh trên MinIO (bucket ttht-diemdanh)
            image_sha256  VARCHAR2(64)   NOT NULL,
            CONSTRAINT pk_dtntb_diemdanh_anh PRIMARY KEY (diemdanh_id, thu_tu),
            CONSTRAINT fk_dtntb_diemdanh_anh FOREIGN KEY (diemdanh_id)
                REFERENCES dtntb_diemdanh (diemdanh_id) ON DELETE CASCADE
        )';
EXCEPTION
    WHEN e_table_exists THEN NULL;
END;
/

DECLARE
    e_index_exists EXCEPTION;
    PRAGMA EXCEPTION_INIT(e_index_exists, -955);
BEGIN
    EXECUTE IMMEDIATE 'CREATE INDEX ix_dtntb_diemdanh_anh_sha ON dtntb_diemdanh_anh (image_sha256)';
EXCEPTION
    WHEN e_index_exists THEN NULL;
END;
/
