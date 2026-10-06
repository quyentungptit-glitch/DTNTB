CREATE TABLE dtntb_user_notifications (
    notification_id NUMBER(19) PRIMARY KEY,
    username VARCHAR2(100) NOT NULL,
    notification_type VARCHAR2(50) NOT NULL,
    title VARCHAR2(200) NOT NULL,
    message_body VARCHAR2(1000) NOT NULL,
    route VARCHAR2(500),
    is_read NUMBER(1) DEFAULT 0 NOT NULL,
    created_at DATE DEFAULT SYSDATE NOT NULL,
    read_at DATE NULL,
    CONSTRAINT chk_dtn_un_read CHECK (is_read IN (0, 1))
);

CREATE SEQUENCE seq_dtntb_user_notifications START WITH 1 INCREMENT BY 1 NOCACHE;
CREATE INDEX ix_dtn_un_username_read ON dtntb_user_notifications (username, is_read, created_at DESC);

COMMIT;
