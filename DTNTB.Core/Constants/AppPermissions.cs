namespace DTNTB.Core.Constants
{
    public static class AppPermissions
    {
        // ==========================================
        // MODULE: ĐIỀU HÀNH THUÊ BAO (DTNTB)
        // ==========================================
        public static class DTNTB
        {
            public const string VIEW    = "PERMISSIONS.DTNTB.VIEW";
            public const string ADD     = "PERMISSIONS.DTNTB.ADD";
            public const string UPDATE  = "PERMISSIONS.DTNTB.UPDATE";
            public const string DELETE  = "PERMISSIONS.DTNTB.DELETE";
            public const string IMPORT  = "PERMISSIONS.DTNTB.IMPORT";
            public const string EXPORT  = "PERMISSIONS.DTNTB.EXPORT";
            public const string ACTION  = "PERMISSIONS.DTNTB.ACTION";

            public const string MANAGE_ALL  = "PERMISSIONS.DTNTB.MANAGE_ALL";
            public const string MANAGE_AREA = "PERMISSIONS.DTNTB.MANAGE_AREA";
            public const string MANAGE_UNIT = "PERMISSIONS.DTNTB.MANAGE_UNIT";
            public const string MANAGE_TEAM = "PERMISSIONS.DTNTB.MANAGE_TEAM";
        }

        // ==========================================
        // MODULE: DASHBOARD QUẢN TRỊ (DASHBOARD)
        // ==========================================
        public static class DASHBOARD
        {
            public const string VIEW   = "PERMISSIONS.DASHBOARD.VIEW";    // Xem báo cáo dashboard
            public const string ACTION = "PERMISSIONS.DASHBOARD.ACTION";  // Giao phiếu / Gửi phiếu kế hoạch
            public const string EXPORT = "PERMISSIONS.DASHBOARD.EXPORT";  // Xuất file Excel dashboard
        }

        // ==========================================
        // MODULE: QUẢN TRỊ HỆ THỐNG (HETHONG)
        // ==========================================
        public static class HETHONG
        {
            public const string VIEW        = "PERMISSIONS.HETHONG.VIEW";
            public const string ADD         = "PERMISSIONS.HETHONG.ADD";
            public const string UPDATE      = "PERMISSIONS.HETHONG.UPDATE";
            public const string DELETE      = "PERMISSIONS.HETHONG.DELETE";
            public const string PHAN_QUYEN  = "PERMISSIONS.HETHONG.PHAN_QUYEN";
        }

        // ==========================================
        // MODULE: BÁO CÁO GIAO HẠN TRẢ TRƯỚC (GHTT)
        // ==========================================
        public static class GHTT
        {
            public const string VIEW            = "PERMISSIONS.GHTT.VIEW";         // Xem dữ liệu báo cáo GHTT (btnxem)
            public const string EXPORT          = "PERMISSIONS.GHTT.EXPORT";       // Tải/Xuất file Excel báo cáo GHTT (btndowload)
            public const string TONG_HOP        = "PERMISSIONS.GHTT.TONG_HOP";     // Tổng hợp / Đồng bộ số liệu GHTT (btntonghop)
            public const string CHOT_SO_LIEU    = "PERMISSIONS.GHTT.CHOT_SO_LIEU"; // Chốt số liệu GHTT (btnchotsl - thay thế kiểm tra level_role == 1)
            public const string PHIEU_GIAO      = "PERMISSIONS.GHTT.PHIEU_GIAO";   // Giao phiếu tiếp cận TB GHTT (Biểu 8) trong phạm vi
            public const string PHIEU_XU_LY     = "PERMISSIONS.GHTT.PHIEU_XU_LY";  // Cập nhật kết quả phiếu GHTT (kể cả nhập hộ) trong phạm vi
        }

        // ==========================================
        // MODULE: QUẢN TRỊ THÔNG BÁO (THONGBAO)
        // ==========================================
        public static class THONGBAO
        {
            public const string VIEW      = "PERMISSIONS.THONGBAO.VIEW";
            public const string UPDATE    = "PERMISSIONS.THONGBAO.UPDATE";
            public const string SEND_TEST = "PERMISSIONS.THONGBAO.SEND_TEST";
        }

        // ==========================================
        // MODULE: ĐIỂM DANH HIỆN TRƯỜNG (DIEMDANH)
        // ==========================================
        public static class DIEMDANH
        {
            public const string CHECKIN = "PERMISSIONS.DIEMDANH.CHECKIN"; // Nhân viên chụp ảnh điểm danh
            public const string VIEW    = "PERMISSIONS.DIEMDANH.VIEW";    // Xem bản đồ / danh sách / lịch sử theo phạm vi
            public const string EXPORT  = "PERMISSIONS.DIEMDANH.EXPORT";  // Xuất Excel lịch sử điểm danh
            public const string DELETE  = "PERMISSIONS.DIEMDANH.DELETE";  // Xóa lượt điểm danh (kèm ảnh) trong phạm vi
            public const string APPROVE = "PERMISSIONS.DIEMDANH.APPROVE"; // Duyệt / từ chối lượt điểm danh trong phạm vi
        }

        // ==========================================
        // MODULE: 5S NHÀ TRẠM (NAMS = "năm S")
        // ==========================================
        public static class NAMS
        {
            public const string GIAO      = "PERMISSIONS.NAMS.GIAO";       // Giao trạm cho nhân viên, thu hồi phiếu
            public const string THUC_HIEN = "PERMISSIONS.NAMS.THUC_HIEN";  // Nhân viên tải ảnh theo tiêu chí và nộp
            public const string CHAM      = "PERMISSIONS.NAMS.CHAM";       // Chấm điểm phiếu đã nộp
            public const string VIEW      = "PERMISSIONS.NAMS.VIEW";       // Xem báo cáo tổng hợp / chi tiết
            public const string EXPORT    = "PERMISSIONS.NAMS.EXPORT";     // Xuất Excel báo cáo
        }

        // ==========================================
        // MODULE: QUẢN LÝ CÔNG VIỆC TTHT (PHIẾU YÊU CẦU CÔNG VIỆC)
        // Tổ trưởng / người nhận KTHT không cần quyền: có tên trong dtntb_cv_nguoi_nhan là nhận phiếu.
        // ==========================================
        public static class CV
        {
            public const string TAO            = "PERMISSIONS.CV.TAO";            // Tạo phiếu cho tổ Hạ tầng của địa bàn mình
            public const string TAO_TOAN_TINH  = "PERMISSIONS.CV.TAO_TOAN_TINH";  // Tạo phiếu toàn tỉnh + phiếu khai báo KTHT
            public const string XU_LY          = "PERMISSIONS.CV.XU_LY";          // Xử lý phiếu được giao
            public const string DUYET          = "PERMISSIONS.CV.DUYET";          // Duyệt tính lương phiếu đã đóng
            public const string VIEW           = "PERMISSIONS.CV.VIEW";           // Xem báo cáo
            public const string EXPORT         = "PERMISSIONS.CV.EXPORT";         // Xuất Excel
            public const string QUAN_TRI       = "PERMISSIONS.CV.QUAN_TRI";       // Danh mục, người nhận, chuyển tổ mọi phiếu
        }
    }

    // ==========================================
    // 5 CẤP ĐỘ PHẠM VI DỮ LIỆU (DATA SCOPE)
    // ==========================================
    public enum UserDataScopeLevel
    {
        ToanTinh = 1,  // Cấp 1: Toàn tỉnh
        DiaBan   = 2,  // Cấp 2: Địa bàn khu vực
        DonVi    = 3,  // Cấp 3: Đơn vị (7 ký tự)
        ToQuanLy = 4,  // Cấp 4: Tổ quản lý (11 ký tự)
        NhanVien = 5   // Cấp 5: Nhân viên cá nhân
    }
}
