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