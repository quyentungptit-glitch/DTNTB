-- Lựa chọn nhận push là thuộc tính của người dùng hiện có phân quyền.
-- Không lưu Firebase registration token; token chỉ dùng tạm thời để subscribe topic.
DECLARE
    e_column_exists EXCEPTION;
    PRAGMA EXCEPTION_INIT(e_column_exists, -1430);
BEGIN
    EXECUTE IMMEDIATE 'ALTER TABLE dtntb_sys_user_roles ADD (fcm_enabled NUMBER(1) DEFAULT 0 NOT NULL)';
EXCEPTION
    WHEN e_column_exists THEN NULL;
END;
/

DECLARE
    e_column_exists EXCEPTION;
    PRAGMA EXCEPTION_INIT(e_column_exists, -1430);
BEGIN
    EXECUTE IMMEDIATE 'ALTER TABLE dtntb_sys_user_roles ADD (fcm_enabled_at DATE NULL)';
EXCEPTION
    WHEN e_column_exists THEN NULL;
END;
/

UPDATE dtntb_sys_user_roles
SET fcm_enabled = NVL(fcm_enabled, 0)
WHERE fcm_enabled IS NULL;

DECLARE
    v_count NUMBER;
BEGIN
    SELECT COUNT(1) INTO v_count
    FROM user_constraints
    WHERE table_name = 'DTNTB_SYS_USER_ROLES'
      AND constraint_name = 'CHK_DTN_UR_FCM_ENABLED';

    IF v_count = 0 THEN
        EXECUTE IMMEDIATE '
            ALTER TABLE dtntb_sys_user_roles
            ADD CONSTRAINT chk_dtn_ur_fcm_enabled
            CHECK (fcm_enabled IN (0, 1))';
    END IF;
END;
/

COMMIT;
