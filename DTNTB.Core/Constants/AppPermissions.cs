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