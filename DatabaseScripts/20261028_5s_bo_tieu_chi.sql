-- ============================================================
-- 5S NHÀ TRẠM: BỘ TIÊU CHÍ CHẤM ĐIỂM (theo mẫu chấm điểm 5S nhà trạm)
--  - 15 tiêu chí, tổng điểm tối đa 100 (quy đổi tỷ lệ từ thang 55 của mẫu, ×100/55); tiêu chí có dấu (*) trên mẫu = bắt buộc đủ ảnh mới nộp được.
--  - Tắt tiêu chí chung "5S" (tieuchi_id = 1): phiếu cũ đã có ảnh theo tiêu chí này vẫn hiển thị và chấm được.
--  - Chạy lại nhiều lần được (MERGE theo mã tiêu chí).
--  - YEU_CAU_ANH: bản gợi ý dựa trên phần chữ hiện trên mẫu, sửa lại cho đúng nguyên văn quy định nếu cần.
-- ============================================================

UPDATE dtntb_5s_tieuchi SET is_active = 0 WHERE ma = '5S';

MERGE INTO dtntb_5s_tieuchi t
USING (SELECT 'ND_I.1' AS ma FROM dual) s ON (t.ma = s.ma)
WHEN MATCHED THEN UPDATE SET
    t.noi_dung = 'Tường, trần phòng thiết bị', t.yeu_cau_anh = '- Ảnh 1: Chụp tường + trần phòng thiết bị (toàn cảnh)' || CHR(10) || '- Ảnh 2, 3: Chụp các vị trí tường/trần còn lại, vết ẩm mốc, bong tróc (nếu có)',
    t.sl_anh_toi_thieu = 3, t.diem_toi_da = 4, t.bat_buoc = 0, t.thu_tu = 1, t.is_active = 1
WHEN NOT MATCHED THEN INSERT (tieuchi_id, ma, noi_dung, yeu_cau_anh, sl_anh_toi_thieu, diem_toi_da, bat_buoc, thu_tu, is_active)
VALUES (2, 'ND_I.1', 'Tường, trần phòng thiết bị', '- Ảnh 1: Chụp tường + trần phòng thiết bị (toàn cảnh)' || CHR(10) || '- Ảnh 2, 3: Chụp các vị trí tường/trần còn lại, vết ẩm mốc, bong tróc (nếu có)', 3, 4, 0, 1, 1);

MERGE INTO dtntb_5s_tieuchi t
USING (SELECT 'ND_I.7' AS ma FROM dual) s ON (t.ma = s.ma)
WHEN MATCHED THEN UPDATE SET
    t.noi_dung = 'Khuôn viên nhà trạm', t.yeu_cau_anh = '- Ảnh 1: Chọn vị trí mặt trước nhà trạm chụp toàn cảnh khuôn viên' || CHR(10) || '- Ảnh 2-5: Chụp các góc khuôn viên, cỏ, rác, hàng rào, cổng',
    t.sl_anh_toi_thieu = 5, t.diem_toi_da = 7, t.bat_buoc = 1, t.thu_tu = 2, t.is_active = 1
WHEN NOT MATCHED THEN INSERT (tieuchi_id, ma, noi_dung, yeu_cau_anh, sl_anh_toi_thieu, diem_toi_da, bat_buoc, thu_tu, is_active)
VALUES (3, 'ND_I.7', 'Khuôn viên nhà trạm', '- Ảnh 1: Chọn vị trí mặt trước nhà trạm chụp toàn cảnh khuôn viên' || CHR(10) || '- Ảnh 2-5: Chụp các góc khuôn viên, cỏ, rác, hàng rào, cổng', 5, 7, 1, 2, 1);

MERGE INTO dtntb_5s_tieuchi t
USING (SELECT 'ND_I.8' AS ma FROM dual) s ON (t.ma = s.ma)
WHEN MATCHED THEN UPDATE SET
    t.noi_dung = 'Hệ thống thoát nước', t.yeu_cau_anh = '- Ảnh 1: Chụp nóc nhà trạm / mái' || CHR(10) || '- Ảnh 2: Chụp rãnh, ống thoát nước',
    t.sl_anh_toi_thieu = 2, t.diem_toi_da = 4, t.bat_buoc = 0, t.thu_tu = 3, t.is_active = 1
WHEN NOT MATCHED THEN INSERT (tieuchi_id, ma, noi_dung, yeu_cau_anh, sl_anh_toi_thieu, diem_toi_da, bat_buoc, thu_tu, is_active)
VALUES (4, 'ND_I.8', 'Hệ thống thoát nước', '- Ảnh 1: Chụp nóc nhà trạm / mái' || CHR(10) || '- Ảnh 2: Chụp rãnh, ống thoát nước', 2, 4, 0, 3, 1);

MERGE INTO dtntb_5s_tieuchi t
USING (SELECT 'ND_I.9' AS ma FROM dual) s ON (t.ma = s.ma)
WHEN MATCHED THEN UPDATE SET
    t.noi_dung = 'Cột Anten', t.yeu_cau_anh = '- Ảnh 1: Ảnh chụp thể hiện toàn bộ cột anten' || CHR(10) || '- Ảnh 2-6: Chụp chân cột, móng co, dây co, tăng đơ, tiếp đất, thiết bị trên cột',
    t.sl_anh_toi_thieu = 6, t.diem_toi_da = 7, t.bat_buoc = 0, t.thu_tu = 4, t.is_active = 1
WHEN NOT MATCHED THEN INSERT (tieuchi_id, ma, noi_dung, yeu_cau_anh, sl_anh_toi_thieu, diem_toi_da, bat_buoc, thu_tu, is_active)
VALUES (5, 'ND_I.9', 'Cột Anten', '- Ảnh 1: Ảnh chụp thể hiện toàn bộ cột anten' || CHR(10) || '- Ảnh 2-6: Chụp chân cột, móng co, dây co, tăng đơ, tiếp đất, thiết bị trên cột', 6, 7, 0, 4, 1);

MERGE INTO dtntb_5s_tieuchi t
USING (SELECT 'ND_II.1' AS ma FROM dual) s ON (t.ma = s.ma)
WHEN MATCHED THEN UPDATE SET
    t.noi_dung = 'Nhiệt độ', t.yeu_cau_anh = '01 ảnh nhiệt kế thể hiện nhiệt độ phòng thiết bị',
    t.sl_anh_toi_thieu = 1, t.diem_toi_da = 7, t.bat_buoc = 1, t.thu_tu = 5, t.is_active = 1
WHEN NOT MATCHED THEN INSERT (tieuchi_id, ma, noi_dung, yeu_cau_anh, sl_anh_toi_thieu, diem_toi_da, bat_buoc, thu_tu, is_active)
VALUES (6, 'ND_II.1', 'Nhiệt độ', '01 ảnh nhiệt kế thể hiện nhiệt độ phòng thiết bị', 1, 7, 1, 5, 1);

MERGE INTO dtntb_5s_tieuchi t
USING (SELECT 'ND_III.1' AS ma FROM dual) s ON (t.ma = s.ma)
WHEN MATCHED THEN UPDATE SET
    t.noi_dung = 'Hệ thống thiết bị', t.yeu_cau_anh = 'Ảnh chụp thể hiện được vị trí, tình trạng các tủ/giá thiết bị (mặt trước, bên trong, nhãn)',
    t.sl_anh_toi_thieu = 3, t.diem_toi_da = 11, t.bat_buoc = 1, t.thu_tu = 6, t.is_active = 1
WHEN NOT MATCHED THEN INSERT (tieuchi_id, ma, noi_dung, yeu_cau_anh, sl_anh_toi_thieu, diem_toi_da, bat_buoc, thu_tu, is_active)
VALUES (7, 'ND_III.1', 'Hệ thống thiết bị', 'Ảnh chụp thể hiện được vị trí, tình trạng các tủ/giá thiết bị (mặt trước, bên trong, nhãn)', 3, 11, 1, 6, 1);

MERGE INTO dtntb_5s_tieuchi t
USING (SELECT 'ND_III.5' AS ma FROM dual) s ON (t.ma = s.ma)
WHEN MATCHED THEN UPDATE SET
    t.noi_dung = 'Bảo dưỡng Điều hòa', t.yeu_cau_anh = '- Ảnh 1: Chụp lưới lọc của dàn lạnh sau vệ sinh' || CHR(10) || '- Ảnh 2: Chụp dàn nóng điều hòa',
    t.sl_anh_toi_thieu = 2, t.diem_toi_da = 4, t.bat_buoc = 1, t.thu_tu = 7, t.is_active = 1
WHEN NOT MATCHED THEN INSERT (tieuchi_id, ma, noi_dung, yeu_cau_anh, sl_anh_toi_thieu, diem_toi_da, bat_buoc, thu_tu, is_active)
VALUES (8, 'ND_III.5', 'Bảo dưỡng Điều hòa', '- Ảnh 1: Chụp lưới lọc của dàn lạnh sau vệ sinh' || CHR(10) || '- Ảnh 2: Chụp dàn nóng điều hòa', 2, 4, 1, 7, 1);

MERGE INTO dtntb_5s_tieuchi t
USING (SELECT 'ND_III.6' AS ma FROM dual) s ON (t.ma = s.ma)
WHEN MATCHED THEN UPDATE SET
    t.noi_dung = 'Cầu cáp máng cáp 1', t.yeu_cau_anh = '- 01 ảnh thể hiện được hệ thống cầu cáp, máng cáp trong phòng thiết bị',
    t.sl_anh_toi_thieu = 1, t.diem_toi_da = 1.5, t.bat_buoc = 0, t.thu_tu = 8, t.is_active = 1
WHEN NOT MATCHED THEN INSERT (tieuchi_id, ma, noi_dung, yeu_cau_anh, sl_anh_toi_thieu, diem_toi_da, bat_buoc, thu_tu, is_active)
VALUES (9, 'ND_III.6', 'Cầu cáp máng cáp 1', '- 01 ảnh thể hiện được hệ thống cầu cáp, máng cáp trong phòng thiết bị', 1, 1.5, 0, 8, 1);

MERGE INTO dtntb_5s_tieuchi t
USING (SELECT 'ND_III.6.2' AS ma FROM dual) s ON (t.ma = s.ma)
WHEN MATCHED THEN UPDATE SET
    t.noi_dung = 'Cầu cáp máng cáp 2', t.yeu_cau_anh = '- 01 ảnh thể hiện toàn bộ cầu cáp, máng cáp ngoài trời / lên cột',
    t.sl_anh_toi_thieu = 1, t.diem_toi_da = 1.5, t.bat_buoc = 0, t.thu_tu = 9, t.is_active = 1
WHEN NOT MATCHED THEN INSERT (tieuchi_id, ma, noi_dung, yeu_cau_anh, sl_anh_toi_thieu, diem_toi_da, bat_buoc, thu_tu, is_active)
VALUES (10, 'ND_III.6.2', 'Cầu cáp máng cáp 2', '- 01 ảnh thể hiện toàn bộ cầu cáp, máng cáp ngoài trời / lên cột', 1, 1.5, 0, 9, 1);

MERGE INTO dtntb_5s_tieuchi t
USING (SELECT 'ND_III.8' AS ma FROM dual) s ON (t.ma = s.ma)
WHEN MATCHED THEN UPDATE SET
    t.noi_dung = 'ODF', t.yeu_cau_anh = '- Ảnh chụp ODF tập trung, thể hiện nhãn, dây nhảy gọn gàng',
    t.sl_anh_toi_thieu = 1, t.diem_toi_da = 11, t.bat_buoc = 1, t.thu_tu = 10, t.is_active = 1
WHEN NOT MATCHED THEN INSERT (tieuchi_id, ma, noi_dung, yeu_cau_anh, sl_anh_toi_thieu, diem_toi_da, bat_buoc, thu_tu, is_active)
VALUES (11, 'ND_III.8', 'ODF', '- Ảnh chụp ODF tập trung, thể hiện nhãn, dây nhảy gọn gàng', 1, 11, 1, 10, 1);

MERGE INTO dtntb_5s_tieuchi t
USING (SELECT 'ND_III.9' AS ma FROM dual) s ON (t.ma = s.ma)
WHEN MATCHED THEN UPDATE SET
    t.noi_dung = 'Dây nhảy quang', t.yeu_cau_anh = 'Ảnh 1: Chụp thể hiện dây nhảy quang được bó, đi gọn, có nhãn' || CHR(10) || 'Ảnh 2: Chụp đầu dây nhảy tại thiết bị',
    t.sl_anh_toi_thieu = 2, t.diem_toi_da = 11, t.bat_buoc = 1, t.thu_tu = 11, t.is_active = 1
WHEN NOT MATCHED THEN INSERT (tieuchi_id, ma, noi_dung, yeu_cau_anh, sl_anh_toi_thieu, diem_toi_da, bat_buoc, thu_tu, is_active)
VALUES (12, 'ND_III.9', 'Dây nhảy quang', 'Ảnh 1: Chụp thể hiện dây nhảy quang được bó, đi gọn, có nhãn' || CHR(10) || 'Ảnh 2: Chụp đầu dây nhảy tại thiết bị', 2, 11, 1, 11, 1);

MERGE INTO dtntb_5s_tieuchi t
USING (SELECT 'ND_III.10' AS ma FROM dual) s ON (t.ma = s.ma)
WHEN MATCHED THEN UPDATE SET
    t.noi_dung = 'Cáp quang nhập trạm', t.yeu_cau_anh = 'Ảnh thể hiện cáp quang đi vào trạm (lỗ nhập cáp, cuộn dự phòng)',
    t.sl_anh_toi_thieu = 1, t.diem_toi_da = 4, t.bat_buoc = 0, t.thu_tu = 12, t.is_active = 1
WHEN NOT MATCHED THEN INSERT (tieuchi_id, ma, noi_dung, yeu_cau_anh, sl_anh_toi_thieu, diem_toi_da, bat_buoc, thu_tu, is_active)
VALUES (13, 'ND_III.10', 'Cáp quang nhập trạm', 'Ảnh thể hiện cáp quang đi vào trạm (lỗ nhập cáp, cuộn dự phòng)', 1, 4, 0, 12, 1);

MERGE INTO dtntb_5s_tieuchi t
USING (SELECT 'ND_III.11' AS ma FROM dual) s ON (t.ma = s.ma)
WHEN MATCHED THEN UPDATE SET
    t.noi_dung = 'Sổ theo dõi nhật ký trạm', t.yeu_cau_anh = '- Ảnh thể hiện được lịch sử ghi chép sổ nhật ký trạm (bìa sổ và trang ghi gần nhất)',
    t.sl_anh_toi_thieu = 3, t.diem_toi_da = 9, t.bat_buoc = 1, t.thu_tu = 13, t.is_active = 1
WHEN NOT MATCHED THEN INSERT (tieuchi_id, ma, noi_dung, yeu_cau_anh, sl_anh_toi_thieu, diem_toi_da, bat_buoc, thu_tu, is_active)
VALUES (14, 'ND_III.11', 'Sổ theo dõi nhật ký trạm', '- Ảnh thể hiện được lịch sử ghi chép sổ nhật ký trạm (bìa sổ và trang ghi gần nhất)', 3, 9, 1, 13, 1);

MERGE INTO dtntb_5s_tieuchi t
USING (SELECT 'ND_III.14' AS ma FROM dual) s ON (t.ma = s.ma)
WHEN MATCHED THEN UPDATE SET
    t.noi_dung = 'Máy phát điện, phòng máy nổ', t.yeu_cau_anh = '- 01 ảnh chụp tổng quan máy phát điện / phòng máy nổ' || CHR(10) || '- Các ảnh còn lại: bảng điều khiển, nhiên liệu, ắc quy đề, sổ vận hành',
    t.sl_anh_toi_thieu = 5, t.diem_toi_da = 9, t.bat_buoc = 1, t.thu_tu = 14, t.is_active = 1
WHEN NOT MATCHED THEN INSERT (tieuchi_id, ma, noi_dung, yeu_cau_anh, sl_anh_toi_thieu, diem_toi_da, bat_buoc, thu_tu, is_active)
VALUES (15, 'ND_III.14', 'Máy phát điện, phòng máy nổ', '- 01 ảnh chụp tổng quan máy phát điện / phòng máy nổ' || CHR(10) || '- Các ảnh còn lại: bảng điều khiển, nhiên liệu, ắc quy đề, sổ vận hành', 5, 9, 1, 14, 1);

MERGE INTO dtntb_5s_tieuchi t
USING (SELECT 'ND_III.15' AS ma FROM dual) s ON (t.ma = s.ma)
WHEN MATCHED THEN UPDATE SET
    t.noi_dung = 'Hệ thống máy nắn điện', t.yeu_cau_anh = '- Ảnh chụp thể hiện mặt trước tủ nguồn / máy nắn điện, màn hình hiển thị thông số',
    t.sl_anh_toi_thieu = 1, t.diem_toi_da = 9, t.bat_buoc = 1, t.thu_tu = 15, t.is_active = 1
WHEN NOT MATCHED THEN INSERT (tieuchi_id, ma, noi_dung, yeu_cau_anh, sl_anh_toi_thieu, diem_toi_da, bat_buoc, thu_tu, is_active)
VALUES (16, 'ND_III.15', 'Hệ thống máy nắn điện', '- Ảnh chụp thể hiện mặt trước tủ nguồn / máy nắn điện, màn hình hiển thị thông số', 1, 9, 1, 15, 1);

COMMIT;

-- Kiểm tra: 15 dòng đang dùng, tổng điểm 100
SELECT ma, noi_dung, sl_anh_toi_thieu, diem_toi_da, bat_buoc, thu_tu
FROM dtntb_5s_tieuchi WHERE is_active = 1 ORDER BY thu_tu;
SELECT COUNT(*) so_tieu_chi, SUM(diem_toi_da) tong_diem FROM dtntb_5s_tieuchi WHERE is_active = 1;
