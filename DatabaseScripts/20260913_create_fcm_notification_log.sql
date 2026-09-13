-- Chạy một lần trên Oracle trước khi deploy API có FcmNotificationWorker.
-- Khóa chính giúp mỗi nhân viên chỉ nhận một thông báo nhắc cùng loại trong một ngày.
CREATE TABLE brcd_dhgh_fcm_notification_log
(
    notification_date   DATE           NOT NULL,
    username            VARCHAR2(100)  NOT NULL,
    notification_type   VARCHAR2(50)   NOT NULL,
    pending_count       NUMBER(10)     NOT NULL,
    status              VARCHAR2(20)   NOT NULL,
    firebase_message_id VARCHAR2(255),
    error_message       VARCHAR2(1000),
    created_at          DATE           NOT NULL,
    sent_at             DATE,
    CONSTRAINT pk_dhgh_fcm_notification_log
        PRIMARY KEY (notification_date, username, notification_type),
    CONSTRAINT ck_dhgh_fcm_notification_log_status
        CHECK (status IN ('SENDING', 'SENT', 'FAILED'))
);
