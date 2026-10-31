-- ============================================================
-- QUẢN LÝ CÔNG VIỆC TTHT (PHIẾU YÊU CẦU CÔNG VIỆC)
--
-- Hai loại phiếu (theo đầu việc):
--   HT   : đầu việc HT_ / MNV_ -> gửi tổ trưởng tổ Hạ tầng của địa bàn; có SLA (nếu đầu việc có), có đơn giá (nếu có).
--   KTHT : đầu việc KTHT_     -> khai báo công việc, gửi 1 trong các người nhận KTHT (bảng dtntb_cv_nguoi_nhan, loai = 'KTHT').
--
-- Luồng phiếu   : CHO_GIAO -> DANG_XL -> HOAN_THANH;  CHO_GIAO -> BI_TRA (người nhận trả lại) -> CHO_GIAO (người tạo sửa, gửi lại)
--                 CHO_GIAO / BI_TRA -> DA_HUY (người tạo huỷ khi chưa giao).
--                 Người nhận được "chuyển tổ" sang người nhận khác (sai địa bàn / đổi người / nghỉ phép).
-- Luồng mỗi người xử lý (dtntb_cv_xu_ly): DANG_LAM -> CHO_DONG (ảnh + số lượng + yêu cầu khoá)
--                 -> DA_DONG (người tạo đóng; hoặc trả lại -> DANG_LAM). Mọi người DA_DONG -> phiếu HOAN_THANH.
-- Duyệt tính lương (bước cuối, sau khi đã đóng; không làm đổi trạng thái phiếu):
--                 duyet_tt CHUA -> DA_DUYET (tính lương, được sửa số lượng) / KHONG_TINH. Tháng lương = tháng duyệt.
-- Hạn xử lý: 24/7, = ngày tạo + sla_phut. Quá hạn tính tại thời điểm yêu cầu khoá (không chờ người tạo đóng).
-- Đầu việc không có SLA: không có hạn, không tính quá hạn. Không có đơn giá: thành tiền 0.
-- Đơn giá / SLA / tên đầu việc được CHỤP vào phiếu lúc tạo, sửa danh mục sau không ảnh hưởng phiếu cũ.
-- ============================================================

-- 1. Danh mục đầu việc
CREATE TABLE dtntb_cv_dau_viec
(
    dau_viec_id     NUMBER(10)          NOT NULL,
    ma              VARCHAR2(30)        NOT NULL,
    ten             VARCHAR2(300 CHAR)  NOT NULL,
    nhom            VARCHAR2(10)        NOT NULL,          -- HT | MNV | KTHT (hiển thị, lọc)
    loai_phieu      VARCHAR2(10)        NOT NULL,          -- HT | KTHT
    sla_phut        NUMBER(10),                            -- NULL: không áp hạn xử lý
    don_gia         NUMBER(14, 2),                         -- NULL: không có đơn giá
    dvt             VARCHAR2(50 CHAR),                     -- đơn vị tính: thiết bị, tủ, hộp, km...
    nhom_dinh_muc   VARCHAR2(300 CHAR),                    -- cột "Map" trong file đơn giá
    tinh_tg_chay    NUMBER(1)           DEFAULT 0 NOT NULL, -- 1: ghi thời gian chạy = yêu cầu khoá - tạo phiếu (chạy máy phát)
    thu_tu          NUMBER(5)           DEFAULT 0 NOT NULL,
    is_active       NUMBER(1)           DEFAULT 1 NOT NULL,
    nguoi_cn        VARCHAR2(50),
    ngay_cn         DATE                DEFAULT SYSDATE,
    CONSTRAINT pk_dtntb_cv_dau_viec PRIMARY KEY (dau_viec_id),
    CONSTRAINT uq_dtntb_cv_dau_viec_ma UNIQUE (ma),
    CONSTRAINT ck_dtntb_cv_dau_viec_loai CHECK (loai_phieu IN ('HT', 'KTHT'))
);
CREATE SEQUENCE seq_dtntb_cv_dau_viec START WITH 100 INCREMENT BY 1 NOCACHE;

-- 2. Người nhận phiếu (thay cho vai trò TO_TRUONG)
--    loai = 'HT'  : TTVT (ma_dv_ttvt, 7 ký tự đầu mã đơn vị người tạo) -> tổ trưởng tổ Hạ tầng phụ trách.
--                   1 tổ phụ trách 2 TTVT = 2 dòng; 1 TTVT có 2 người nhận (tổ trưởng + tổ phó) = 2 dòng.
--    loai = 'KTHT': người nhận phiếu khai báo (để trống ma_dv_ttvt).
--    ma_dv_ht     : mã tổ của người nhận, dùng để liệt kê nhân viên trong tổ khi giao việc và báo cáo theo tổ.
--    Không xoá dòng khi đổi người: đặt trang_thai = 0 và thêm dòng mới.
CREATE TABLE dtntb_cv_nguoi_nhan
(
    id              NUMBER(10)          NOT NULL,
    loai            VARCHAR2(10)        NOT NULL,
    ma_dv_ttvt      VARCHAR2(30),
    ten_dv_ttvt     VARCHAR2(300 CHAR),
    ma_nv           VARCHAR2(50)        NOT NULL,
    ten_nv          VARCHAR2(200 CHAR),
    ma_dv_ht        VARCHAR2(30)        NOT NULL,
    ten_dv_ht       VARCHAR2(300 CHAR),
    thu_tu          NUMBER(5)           DEFAULT 0 NOT NULL,
    trang_thai      NUMBER(1)           DEFAULT 1 NOT NULL,
    ghi_chu         VARCHAR2(500 CHAR),
    nguoi_cn        VARCHAR2(50),
    ngay_cn         DATE                DEFAULT SYSDATE,
    CONSTRAINT pk_dtntb_cv_nguoi_nhan PRIMARY KEY (id),
    CONSTRAINT ck_dtntb_cv_nguoi_nhan_loai CHECK (loai IN ('HT', 'KTHT')),
    CONSTRAINT ck_dtntb_cv_nguoi_nhan_ttvt CHECK (loai = 'KTHT' OR ma_dv_ttvt IS NOT NULL),
    CONSTRAINT uq_dtntb_cv_nguoi_nhan UNIQUE (loai, ma_dv_ttvt, ma_nv)
);
CREATE SEQUENCE seq_dtntb_cv_nguoi_nhan START WITH 1 INCREMENT BY 1 NOCACHE;
CREATE INDEX ix_dtntb_cv_nn_ttvt ON dtntb_cv_nguoi_nhan (loai, ma_dv_ttvt, trang_thai);
CREATE INDEX ix_dtntb_cv_nn_nv ON dtntb_cv_nguoi_nhan (ma_nv);

-- 3. Phiếu
CREATE TABLE dtntb_cv_phieu
(
    phieu_id        NUMBER(12)          NOT NULL,
    loai_phieu      VARCHAR2(10)        NOT NULL,          -- HT | KTHT
    tieu_de         VARCHAR2(300 CHAR)  NOT NULL,
    mo_ta           VARCHAR2(4000 CHAR) NOT NULL,
    do_uu_tien      VARCHAR2(20)        DEFAULT 'BINH_THUONG' NOT NULL, -- THAP | BINH_THUONG | CAO | KHAN_CAP
    -- đầu việc (chụp lúc tạo)
    dau_viec_id     NUMBER(10)          NOT NULL,
    ma_dau_viec     VARCHAR2(30)        NOT NULL,
    ten_dau_viec    VARCHAR2(300 CHAR)  NOT NULL,
    sla_phut        NUMBER(10),
    don_gia         NUMBER(14, 2),
    dvt             VARCHAR2(50 CHAR),
    tinh_tg_chay    NUMBER(1)           DEFAULT 0 NOT NULL,
    -- người tạo
    nguoi_tao       VARCHAR2(50)        NOT NULL,          -- mã NV
    ten_nguoi_tao   VARCHAR2(200 CHAR),
    ma_dv_tao       VARCHAR2(30),
    ten_dv_tao      VARCHAR2(300 CHAR),
    ngay_tao        DATE                DEFAULT SYSDATE NOT NULL,
    han_xl          DATE,                                  -- NULL khi đầu việc không có SLA
    -- người nhận hiện tại (đổi khi chuyển tổ)
    ma_dv_ttvt      VARCHAR2(30),                          -- địa bàn của phiếu HT
    ten_dv_ttvt     VARCHAR2(300 CHAR),
    ma_nv_nhan      VARCHAR2(50)        NOT NULL,
    ten_nv_nhan     VARCHAR2(200 CHAR),
    ma_dv_ht        VARCHAR2(30)        NOT NULL,          -- tổ xử lý
    ten_dv_ht       VARCHAR2(300 CHAR),
    -- trạng thái
    trang_thai      VARCHAR2(20)        DEFAULT 'CHO_GIAO' NOT NULL, -- CHO_GIAO | BI_TRA | DANG_XL | HOAN_THANH | DA_HUY
    ngay_giao       DATE,
    ngay_hoan_thanh DATE,
    ly_do_tra       VARCHAR2(1000 CHAR),
    so_lan_tra      NUMBER(5)           DEFAULT 0 NOT NULL,
    so_lan_chuyen   NUMBER(5)           DEFAULT 0 NOT NULL,
    ngay_cn         DATE                DEFAULT SYSDATE,
    CONSTRAINT pk_dtntb_cv_phieu PRIMARY KEY (phieu_id),
    CONSTRAINT ck_dtntb_cv_phieu_tt CHECK (trang_thai IN ('CHO_GIAO', 'BI_TRA', 'DANG_XL', 'HOAN_THANH', 'DA_HUY'))
);
CREATE SEQUENCE seq_dtntb_cv_phieu START WITH 1 INCREMENT BY 1 NOCACHE;
CREATE INDEX ix_dtntb_cv_phieu_tao ON dtntb_cv_phieu (nguoi_tao, trang_thai);
CREATE INDEX ix_dtntb_cv_phieu_nhan ON dtntb_cv_phieu (ma_nv_nhan, trang_thai);
CREATE INDEX ix_dtntb_cv_phieu_to ON dtntb_cv_phieu (ma_dv_ht, ngay_tao);
CREATE INDEX ix_dtntb_cv_phieu_ngay ON dtntb_cv_phieu (ngay_tao);

-- 4. Người xử lý của phiếu (giao nhiều người: mỗi người 1 dòng, đóng / duyệt riêng)
CREATE TABLE dtntb_cv_xu_ly
(
    xu_ly_id        NUMBER(12)          NOT NULL,
    phieu_id        NUMBER(12)          NOT NULL,
    ma_nv           VARCHAR2(50)        NOT NULL,
    ten_nv          VARCHAR2(200 CHAR),
    ma_dv           VARCHAR2(30),                          -- tổ của người xử lý lúc giao
    nguoi_giao      VARCHAR2(50)        NOT NULL,
    ngay_giao       DATE                DEFAULT SYSDATE NOT NULL,
    trang_thai      VARCHAR2(20)        DEFAULT 'DANG_LAM' NOT NULL, -- DANG_LAM | CHO_DONG | DA_DONG | DA_HUY
    -- người xử lý báo xong
    so_luong        NUMBER(12, 3),
    ghi_chu_xl      VARCHAR2(2000 CHAR),
    ngay_yc_khoa    DATE,
    qua_han         NUMBER(1),                             -- 1: yêu cầu khoá sau han_xl (chốt lúc yêu cầu khoá)
    tg_chay_phut    NUMBER(10),                            -- đầu việc tinh_tg_chay = 1
    -- người tạo đóng / trả lại
    nguoi_dong      VARCHAR2(50),
    ngay_dong       DATE,
    ly_do_tra       VARCHAR2(1000 CHAR),
    so_lan_tra      NUMBER(5)           DEFAULT 0 NOT NULL,
    -- duyệt tính lương
    duyet_tt        VARCHAR2(20)        DEFAULT 'CHUA' NOT NULL,  -- CHUA | DA_DUYET | KHONG_TINH
    so_luong_duyet  NUMBER(12, 3),
    don_gia         NUMBER(14, 2),
    thanh_tien      NUMBER(16, 2),
    thang_luong     NUMBER(6),                             -- yyyymm = tháng duyệt
    nguoi_duyet     VARCHAR2(50),
    ten_nguoi_duyet VARCHAR2(200 CHAR),
    ngay_duyet      DATE,
    ghi_chu_duyet   VARCHAR2(1000 CHAR),
    CONSTRAINT pk_dtntb_cv_xu_ly PRIMARY KEY (xu_ly_id),
    CONSTRAINT fk_dtntb_cv_xu_ly_phieu FOREIGN KEY (phieu_id) REFERENCES dtntb_cv_phieu (phieu_id),
    CONSTRAINT ck_dtntb_cv_xu_ly_tt CHECK (trang_thai IN ('DANG_LAM', 'CHO_DONG', 'DA_DONG', 'DA_HUY')),
    CONSTRAINT ck_dtntb_cv_xu_ly_duyet CHECK (duyet_tt IN ('CHUA', 'DA_DUYET', 'KHONG_TINH'))
);
CREATE SEQUENCE seq_dtntb_cv_xu_ly START WITH 1 INCREMENT BY 1 NOCACHE;
CREATE INDEX ix_dtntb_cv_xl_phieu ON dtntb_cv_xu_ly (phieu_id);
CREATE INDEX ix_dtntb_cv_xl_nv ON dtntb_cv_xu_ly (ma_nv, trang_thai);
CREATE INDEX ix_dtntb_cv_xl_duyet ON dtntb_cv_xu_ly (duyet_tt, trang_thai);
CREATE INDEX ix_dtntb_cv_xl_luong ON dtntb_cv_xu_ly (thang_luong, ma_nv);

-- 5. File đính kèm (khi tạo phiếu) và ảnh minh chứng (khi yêu cầu khoá). Lưu MinIO.
CREATE TABLE dtntb_cv_file
(
    file_id         NUMBER(12)          NOT NULL,
    phieu_id        NUMBER(12)          NOT NULL,
    xu_ly_id        NUMBER(12),                            -- NULL: đính kèm của phiếu
    loai            VARCHAR2(20)        NOT NULL,          -- DINH_KEM | MINH_CHUNG
    ten_file        VARCHAR2(300 CHAR)  NOT NULL,
    object_key      VARCHAR2(300)       NOT NULL,
    thumb_key       VARCHAR2(300),
    content_type    VARCHAR2(100),
    kich_thuoc      NUMBER(12),
    sha256          VARCHAR2(64),
    lat             NUMBER(10, 7),
    lng             NUMBER(10, 7),
    chup_luc        DATE,
    nguoi_tai       VARCHAR2(50)        NOT NULL,
    ngay_tai        DATE                DEFAULT SYSDATE NOT NULL,
    CONSTRAINT pk_dtntb_cv_file PRIMARY KEY (file_id),
    CONSTRAINT fk_dtntb_cv_file_phieu FOREIGN KEY (phieu_id) REFERENCES dtntb_cv_phieu (phieu_id),
    CONSTRAINT ck_dtntb_cv_file_loai CHECK (loai IN ('DINH_KEM', 'MINH_CHUNG'))
);
CREATE SEQUENCE seq_dtntb_cv_file START WITH 1 INCREMENT BY 1 NOCACHE;
CREATE INDEX ix_dtntb_cv_file_phieu ON dtntb_cv_file (phieu_id, xu_ly_id);

-- 6. Lịch sử thao tác
CREATE TABLE dtntb_cv_lich_su
(
    ls_id           NUMBER(14)          NOT NULL,
    phieu_id        NUMBER(12)          NOT NULL,
    xu_ly_id        NUMBER(12),
    thao_tac        VARCHAR2(30)        NOT NULL,          -- TAO | SUA | HUY | TRA | GIAO | CHUYEN_TO | YC_KHOA | DONG | TRA_LAI_XL | DUYET ...
    noi_dung        VARCHAR2(2000 CHAR),
    nguoi           VARCHAR2(50)        NOT NULL,
    ten_nguoi       VARCHAR2(200 CHAR),
    ngay            DATE                DEFAULT SYSDATE NOT NULL,
    CONSTRAINT pk_dtntb_cv_lich_su PRIMARY KEY (ls_id)
);
CREATE SEQUENCE seq_dtntb_cv_lich_su START WITH 1 INCREMENT BY 1 NOCACHE;
CREATE INDEX ix_dtntb_cv_ls_phieu ON dtntb_cv_lich_su (phieu_id, ngay);

-- ============================================================
-- DỮ LIỆU DANH MỤC ĐẦU VIỆC (49 đầu việc theo file Qlcv.xlsx)
-- Đơn giá trống = không có đơn giá; SLA trống = không áp hạn xử lý.
-- CV47 (chạy máy phát): không SLA, ghi thời gian chạy = yêu cầu khoá - tạo phiếu.
-- ============================================================
MERGE INTO dtntb_cv_dau_viec t
USING (
    SELECT 'CV01' AS ma, 'KTHT_Khai báo công việc' AS ten, 'KTHT' AS nhom, 'KTHT' AS loai_phieu, CAST(NULL AS NUMBER) AS sla_phut, CAST(NULL AS NUMBER) AS don_gia, CAST(NULL AS VARCHAR2(50 CHAR)) AS dvt, CAST(NULL AS VARCHAR2(300 CHAR)) AS nhom_dinh_muc, 0 AS tinh_tg_chay, 1 AS thu_tu FROM dual
    UNION ALL SELECT 'CV02', 'KTHT_Cấp vật tư (module, card…)', 'KTHT', 'KTHT', NULL, NULL, NULL, NULL, 0, 2 FROM dual
    UNION ALL SELECT 'CV03', 'KTHT_Xóa dịch vụ ONU', 'KTHT', 'KTHT', NULL, NULL, NULL, NULL, 0, 3 FROM dual
    UNION ALL SELECT 'CV04', 'KTHT_Xử lý CLDV khách hàng VIP (bao gồm cả BRCD và di động)', 'KTHT', 'KTHT', 720, 36000, 'phiếu', NULL, 0, 4 FROM dual
    UNION ALL SELECT 'CV05', 'HT_Phản ánh CSHT xuống cấp', 'HT', 'HT', 1440, NULL, NULL, NULL, 0, 5 FROM dual
    UNION ALL SELECT 'CV06', 'HT_Chuyển port OLT', 'HT', 'HT', 120, 51000, 'thiết bị', 'Lắp đặt, thay thế OLT, SiteRouter, Switch', 0, 6 FROM dual
    UNION ALL SELECT 'CV07', 'HT_Swap Card', 'HT', 'HT', 180, 51000, 'thiết bị', 'Lắp đặt, thay thế OLT, SiteRouter, Switch', 0, 7 FROM dual
    UNION ALL SELECT 'CV08', 'HT_Swap OLT, L2SW', 'HT', 'HT', 240, 51000, 'thiết bị', 'Lắp đặt, thay thế OLT, SiteRouter, Switch', 0, 8 FROM dual
    UNION ALL SELECT 'CV09', 'KTHT_Swap ManE', 'KTHT', 'KTHT', 240, 51000, 'thiết bị', 'Lắp đặt, thay thế OLT, SiteRouter, Switch', 0, 9 FROM dual
    UNION ALL SELECT 'CV10', 'HT_Lắp đặt ManE', 'HT', 'HT', 480, 51000, 'thiết bị', 'Lắp đặt, thay thế OLT, SiteRouter, Switch', 0, 10 FROM dual
    UNION ALL SELECT 'CV11', 'HT_lắp đặt Tủ nguồn', 'HT', 'HT', 480, 111000, 'tủ', 'Lắp đặt, thay thế tủ nguồn', 0, 11 FROM dual
    UNION ALL SELECT 'CV12', 'HT_lắp đặt Accu', 'HT', 'HT', 480, 21000, 'accu', 'Lắp đặt, thay thế Accu', 0, 12 FROM dual
    UNION ALL SELECT 'CV13', 'HT_lắp đặt OLT, L2SW', 'HT', 'HT', 480, 51000, 'thiết bị', 'Lắp đặt, thay thế OLT, SiteRouter, Switch', 0, 13 FROM dual
    UNION ALL SELECT 'CV14', 'HT_Bảo dưỡng CSHT (Nhà trạm, máy nổ, điều hòa, tủ nguồn…)', 'HT', 'HT', 10080, 299000, 'trạm', 'Bảo dưỡng CSHT', 0, 14 FROM dual
    UNION ALL SELECT 'CV15', 'HT_Xử lý sự cố MFĐ', 'HT', 'HT', 360, 75000, 'lần', 'Xử lý sự cố Máy phát điện, ứng cứu PIN', 0, 15 FROM dual
    UNION ALL SELECT 'CV16', 'HT_Xử lý sự cố Tủ nguồn', 'HT', 'HT', 120, 111000, 'tủ', 'Lắp đặt thay thế tủ nguồn', 0, 16 FROM dual
    UNION ALL SELECT 'CV17', 'HT_Xử lý sự cố Điều hoà', 'HT', 'HT', 240, 111000, 'tủ', 'Lắp đặt thay thế tủ nguồn', 0, 17 FROM dual
    UNION ALL SELECT 'CV18', 'HT_Dịch chuyển CSHT', 'HT', 'HT', 1440, 111000, 'trạm', 'Lắp đặt/di chuyển trạm Marco', 0, 18 FROM dual
    UNION ALL SELECT 'CV19', 'HT_Tối ưu CSHT', 'HT', 'HT', 1440, 69000, 'trạm', 'Lắp đặt/di chuyển trạm Remote, SMC', 0, 19 FROM dual
    UNION ALL SELECT 'CV20', 'MNV_Xử lý suy hao truyền dẫn Uplink ManE', 'MNV', 'HT', 480, 51000, 'hộp', 'Hàn nối, lắp đặt tủ hộp spliter, odf 24 sợi', 0, 20 FROM dual
    UNION ALL SELECT 'CV21', 'MNV_Xử lý suy hao truyền dẫn Uplink OLT, L2SW', 'MNV', 'HT', 360, 39000, 'hộp', 'Hàn nối, lắp đặt tủ hộp Spliter, ODF loại 1:8,1:16, ODF quang 12 sợi', 0, 21 FROM dual
    UNION ALL SELECT 'CV22', 'MNV_Xử lý suy hao truyền dẫn 2G, 3G,4G,5G', 'MNV', 'HT', 360, 39000, 'hộp', 'Hàn nối, lắp đặt tủ hộp Spliter, ODF loại 1:8,1:16, ODF quang 12 sợi', 0, 22 FROM dual
    UNION ALL SELECT 'CV23', 'MNV_Xử lý truyền dẫn SP cấp 1', 'MNV', 'HT', 240, 39000, 'hộp', 'Hàn nối, lắp đặt tủ hộp Spliter, ODF loại 1:8,1:16, ODF quang 12 sợi', 0, 23 FROM dual
    UNION ALL SELECT 'CV24', 'MNV_Xử lý truyền dẫn SP cấp 2', 'MNV', 'HT', 240, 39000, 'hộp', 'Hàn nối, lắp đặt tủ hộp Spliter, ODF loại 1:8,1:16, ODF quang 12 sợi', 0, 24 FROM dual
    UNION ALL SELECT 'CV25', 'MNV_Thay thế SP cấp 1', 'MNV', 'HT', 480, 51000, 'hộp', 'Hàn nối, lắp đặt tủ hộp spliter, odf 24 sợi', 0, 25 FROM dual
    UNION ALL SELECT 'CV26', 'MNV_Thay thế SP cấp 2', 'MNV', 'HT', 480, 51000, 'hộp', 'Hàn nối, lắp đặt tủ hộp spliter, odf 24 sợi', 0, 26 FROM dual
    UNION ALL SELECT 'CV27', 'MNV_Chỉnh trang SP cấp 1(5S)', 'MNV', 'HT', 240, 51000, 'hộp', 'Hàn nối, lắp đặt tủ hộp spliter, odf 24 sợi', 0, 27 FROM dual
    UNION ALL SELECT 'CV28', 'MNV_Chỉnh trang SP cấp 2(5S)', 'MNV', 'HT', 240, 51000, 'hộp', 'Hàn nối, lắp đặt tủ hộp spliter, odf 24 sợi', 0, 28 FROM dual
    UNION ALL SELECT 'CV29', 'MNV_Kéo cáp, Mở mới SP cấp 1', 'MNV', 'HT', 720, 51000, 'hộp', 'Hàn nối, lắp đặt tủ hộp spliter, odf 24 sợi', 0, 29 FROM dual
    UNION ALL SELECT 'CV30', 'MNV_Kéo cáp, Mở mới SP cấp 2', 'MNV', 'HT', 720, 51000, 'hộp', 'Hàn nối, lắp đặt tủ hộp spliter, odf 24 sợi', 0, 30 FROM dual
    UNION ALL SELECT 'CV31', 'MNV_XLSC, Kéo, Hàn cáp 12 Treo', 'MNV', 'HT', 240, 39000, '1 đầu cáp', 'Hàn cáp quang <= 12 Fo', 0, 31 FROM dual
    UNION ALL SELECT 'CV32', 'MNV_XLSC, Kéo, Hàn cáp 24 Treo', 'MNV', 'HT', 270, 51000, '1 đầu cáp', 'Hàn cáp quang 24 Fo', 0, 32 FROM dual
    UNION ALL SELECT 'CV33', 'MNV_XLSC, Kéo, Hàn cáp 48 Treo', 'MNV', 'HT', 300, 65000, '1 đầu cáp', 'Hàn cáp quang 48 Fo', 0, 33 FROM dual
    UNION ALL SELECT 'CV34', 'MNV_XLSC, Kéo, Hàn cáp 96 Treo', 'MNV', 'HT', 360, 99000, '1 đầu cáp', 'Hàn cáp quang 96 Fo', 0, 34 FROM dual
    UNION ALL SELECT 'CV35', 'MNV_XLSC, Kéo, Hàn cáp 12 Chôn', 'MNV', 'HT', 360, 39000, '1 đầu cáp', 'Hàn cáp quang <= 12 Fo', 0, 35 FROM dual
    UNION ALL SELECT 'CV36', 'MNV_XLSC, Kéo, Hàn cáp 24 Chôn', 'MNV', 'HT', 390, 51000, '1 đầu cáp', 'Hàn cáp quang 24 Fo', 0, 36 FROM dual
    UNION ALL SELECT 'CV37', 'MNV_XLSC, Kéo, Hàn cáp 48 Chôn', 'MNV', 'HT', 420, 65000, '1 đầu cáp', 'Hàn cáp quang 48 Fo', 0, 37 FROM dual
    UNION ALL SELECT 'CV38', 'MNV_XLSC, Kéo, Hàn cáp 96 Chôn', 'MNV', 'HT', 480, 99000, '1 đầu cáp', 'Hàn cáp quang 96 Fo', 0, 38 FROM dual
    UNION ALL SELECT 'CV39', 'MNV_Dịch chuyển tuyến cáp', 'MNV', 'HT', 1440, 82000, 'lần', 'Thay thế, dịch chuyển cột bê tông, thay thế nắp đan cống bể', 0, 39 FROM dual
    UNION ALL SELECT 'CV40', 'MNV_Dịch chuyển tuyến Cột', 'MNV', 'HT', 1440, 82000, 'lần', 'Thay thế, dịch chuyển cột bê tông, thay thế nắp đan cống bể', 0, 40 FROM dual
    UNION ALL SELECT 'CV41', 'MNV_Dịch chuyển tuyến cống bể', 'MNV', 'HT', 1440, 82000, 'lần', 'Thay thế, dịch chuyển cột bê tông, thay thế nắp đan cống bể', 0, 41 FROM dual
    UNION ALL SELECT 'CV42', 'MNV_Căng chỉnh cột nghiêng, đổ, tuyến cáp trùng võng', 'MNV', 'HT', 1440, 210000, 'km', 'Căn chỉnh tuyến cáp trùng, võng', 0, 42 FROM dual
    UNION ALL SELECT 'CV43', 'MNV_Mở mới uplink OLT, L2SW,3G,4G,5G', 'MNV', 'HT', 720, 39000, 'hộp', 'Hàn nối, lắp đặt tủ hộp Spliter, ODF loại 1:8,1:16, ODF quang 12 sợi', 0, 43 FROM dual
    UNION ALL SELECT 'CV44', 'MNV_Mở mới uplink ManE', 'MNV', 'HT', 720, 51000, 'hộp', 'Hàn nối, lắp đặt tủ hộp spliter, odf 24 sợi', 0, 44 FROM dual
    UNION ALL SELECT 'CV45', 'MNV_Khảo sát CSHT cho các khu mới', 'MNV', 'HT', 480, NULL, NULL, NULL, 0, 45 FROM dual
    UNION ALL SELECT 'CV46', 'MNV_Số hóa mạng ngoại vi', 'MNV', 'HT', 480, NULL, NULL, NULL, 0, 46 FROM dual
    UNION ALL SELECT 'CV47', 'HT_Chạy máy phát điện', 'HT', 'HT', NULL, 65000, 'lần', NULL, 1, 47 FROM dual
    UNION ALL SELECT 'CV48', 'HT_Xử lý phiếu vô tuyến chủ động', 'HT', 'HT', NULL, 36000, 'phiếu', NULL, 0, 48 FROM dual
    UNION ALL SELECT 'CV49', 'Kéo mới cáp quang treo', 'MNV', 'HT', NULL, 690000, 'km', NULL, 0, 49 FROM dual
) s
ON (t.ma = s.ma)
WHEN MATCHED THEN UPDATE SET
    t.ten = s.ten, t.nhom = s.nhom, t.loai_phieu = s.loai_phieu, t.sla_phut = s.sla_phut, t.don_gia = s.don_gia,
    t.dvt = s.dvt, t.nhom_dinh_muc = s.nhom_dinh_muc, t.tinh_tg_chay = s.tinh_tg_chay, t.thu_tu = s.thu_tu, t.ngay_cn = SYSDATE
WHEN NOT MATCHED THEN INSERT
    (dau_viec_id, ma, ten, nhom, loai_phieu, sla_phut, don_gia, dvt, nhom_dinh_muc, tinh_tg_chay, thu_tu)
VALUES
    (s.thu_tu, s.ma, s.ten, s.nhom, s.loai_phieu, s.sla_phut, s.don_gia, s.dvt, s.nhom_dinh_muc, s.tinh_tg_chay, s.thu_tu);

-- ============================================================
-- QUYỀN: gán trong form Phân quyền.
--   Tổ trưởng / người nhận KTHT KHÔNG cần quyền riêng: ai có tên trong dtntb_cv_nguoi_nhan thì thấy phiếu gửi cho mình.
-- ============================================================
MERGE INTO dtntb_sys_permissions target
USING (
    SELECT 'PERMISSIONS.CV.TAO' AS permission_code, 'Tạo phiếu công việc cho tổ Hạ tầng của địa bàn mình' AS permission_name, 'CV' AS module_group FROM dual
    UNION ALL SELECT 'PERMISSIONS.CV.TAO_TOAN_TINH', 'Tạo phiếu toàn tỉnh và phiếu khai báo KTHT (thành viên tổ KTHT)', 'CV' FROM dual
    UNION ALL SELECT 'PERMISSIONS.CV.XU_LY', 'Xử lý phiếu được giao (chụp ảnh, nhập số lượng, yêu cầu khoá)', 'CV' FROM dual
    UNION ALL SELECT 'PERMISSIONS.CV.DUYET', 'Duyệt tính lương phiếu đã đóng (được sửa số lượng)', 'CV' FROM dual
    UNION ALL SELECT 'PERMISSIONS.CV.VIEW', 'Xem báo cáo phiếu công việc', 'CV' FROM dual
    UNION ALL SELECT 'PERMISSIONS.CV.EXPORT', 'Xuất Excel báo cáo phiếu công việc', 'CV' FROM dual
    UNION ALL SELECT 'PERMISSIONS.CV.QUAN_TRI', 'Quản trị danh mục đầu việc, người nhận; chuyển tổ mọi phiếu', 'CV' FROM dual
) source
ON (UPPER(TRIM(target.permission_code)) = source.permission_code)
WHEN MATCHED THEN UPDATE SET target.permission_name = source.permission_name, target.module_group = source.module_group
WHEN NOT MATCHED THEN INSERT (permission_code, permission_name, module_group)
VALUES (source.permission_code, source.permission_name, source.module_group);

-- ============================================================
-- MENU: gán cho role trong form Phân Menu.
-- ============================================================
MERGE INTO dtntb_sys_menus target
USING (
    SELECT 'MENU_CV_TAO' AS menu_code, 'Phiếu tôi tạo' AS menu_name, 'PHIẾU CÔNG VIỆC' AS menu_group,
           '/cv/tao' AS route_url, 'fa fa-file-circle-plus' AS icon, 40 AS order_index, 1 AS is_active FROM dual
    UNION ALL SELECT 'MENU_CV_DIEU_PHOI', 'Điều phối phiếu (tổ trưởng)', 'PHIẾU CÔNG VIỆC', '/cv/dieu-phoi', 'fa fa-people-arrows', 41, 1 FROM dual
    UNION ALL SELECT 'MENU_CV_VIEC', 'Việc của tôi', 'PHIẾU CÔNG VIỆC', '/cv/viec', 'fa fa-screwdriver-wrench', 42, 1 FROM dual
    UNION ALL SELECT 'MENU_CV_DUYET', 'Duyệt tính lương', 'PHIẾU CÔNG VIỆC', '/cv/duyet', 'fa fa-file-invoice-dollar', 43, 1 FROM dual
    UNION ALL SELECT 'MENU_CV_BAO_CAO', 'Báo cáo công việc', 'PHIẾU CÔNG VIỆC', '/cv/bao-cao', 'fa fa-chart-column', 44, 1 FROM dual
    UNION ALL SELECT 'MENU_CV_DANH_MUC', 'Danh mục & người nhận', 'PHIẾU CÔNG VIỆC', '/cv/danh-muc', 'fa fa-list-check', 45, 1 FROM dual
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

-- ============================================================
-- MẪU NHẬP NGƯỜI NHẬN (sửa mã / tên cho đúng rồi chạy; không xoá dòng cũ, đặt trang_thai = 0)
-- ============================================================
-- INSERT INTO dtntb_cv_nguoi_nhan (id, loai, ma_dv_ttvt, ten_dv_ttvt, ma_nv, ten_nv, ma_dv_ht, ten_dv_ht, thu_tu)
-- VALUES (seq_dtntb_cv_nguoi_nhan.NEXTVAL, 'HT', '215.3xx', 'Trung tâm Viễn thông ...', 'MA_NV', 'Tên tổ trưởng', 'MA_TO_HT', 'Tổ Hạ tầng ...', 1);
-- INSERT INTO dtntb_cv_nguoi_nhan (id, loai, ma_dv_ttvt, ten_dv_ttvt, ma_nv, ten_nv, ma_dv_ht, ten_dv_ht, thu_tu)
-- VALUES (seq_dtntb_cv_nguoi_nhan.NEXTVAL, 'KTHT', NULL, NULL, 'MA_NV', 'a Quỳnh', 'MA_TO_KTHT', 'Tổ Khai thác hệ thống', 1);
-- COMMIT;
