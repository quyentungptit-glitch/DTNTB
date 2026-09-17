using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ClosedXML.Excel;
using DTNTB.Core.DTOs;
using DTNTB.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;

namespace DTNTB.Infrastructure.Services
{
    public class DashboardService : IDashboardService
    {
        private readonly string _connectionString;
        private readonly ILogger<DashboardService> _logger;

        public DashboardService(IConfiguration configuration, ILogger<DashboardService> logger)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("ConnectionString_NBH")
                             ?? configuration.GetConnectionString("DefaultConnection")
                             ?? throw new InvalidOperationException("Chưa cấu hình ConnectionString_NBH trong file cấu hình.");
        }

        private OracleConnection CreateConnection() => new OracleConnection(_connectionString);

        public async Task<DashboardKpiDto> GetKpisAndChartsAsync(string? maDv, bool isSuperAdmin)
        {
            var result = new DashboardKpiDto();
            string userMaDv = (maDv?.Length > 7 ? maDv.Substring(0, 7) : maDv)?.Trim() ?? string.Empty;

            // CHỈ áp dụng bộ lọc đơn vị khi KHÔNG phải Quản trị viên toàn tỉnh VÀ có mã đơn vị cụ thể
            string unitFilter = (!isSuperAdmin && !string.IsNullOrWhiteSpace(userMaDv))
                ? " AND TRIM(t.ma_dv) = TRIM(:ma_dv) "
                : "";

            using var conn = CreateConnection();
            await conn.OpenAsync();

            // 1. LẤY SỐ LIỆU TỔNG HỢP KPI VÀ BIỂU ĐỒ TRÒN DOUGHNUT
            string sqlSummary = string.Format(@"
                WITH data_goc AS (
                    SELECT t.ma_dv, t.ten_dv, t.diem_tin_nhiem,
                           (t.diem_tin_nhiem
                            - (CASE WHEN NVL(xl.da_thay_thietbi, 0) = 1 THEN t.diem_tbi ELSE 0 END)
                            - (CASE WHEN NVL(xl.da_sua_suyhao, 0) = 1 THEN t.diem_suyhao ELSE 0 END)
                            - (CASE WHEN NVL(xl.da_tuvan_cuoc, 0) = 1 THEN t.diem_tttt ELSE 0 END)
                            - (CASE WHEN NVL(xl.da_tuvan_combo, 0) = 1 THEN t.diem_dadv ELSE 0 END)
                           ) AS score,
                           xl.thuebao_id as xuly_id
                    FROM brcd_dhgh t
                    LEFT JOIN brcd_dhgh_xuly xl ON t.thuebao_id = xl.thuebao_id AND t.phanvung_id = xl.phanvung_id
                    WHERE t.trangthaitb_id = 1 {0}
                )
                SELECT
                    COUNT(CASE WHEN diem_tin_nhiem >= 29 THEN 1 END) as tong_nguy_co,
                    COUNT(xuly_id) as da_xu_ly, 
                    NVL(ROUND(AVG(score), 1), 0) as avg_score,
                    COUNT(CASE WHEN diem_tin_nhiem <= 25 THEN 1 END) as binh_thuong,
                    COUNT(CASE WHEN diem_tin_nhiem > 25 AND diem_tin_nhiem <= 28 THEN 1 END) as theo_doi,
                    COUNT(CASE WHEN diem_tin_nhiem > 28 AND diem_tin_nhiem <= 31 THEN 1 END) as nguy_co,
                    COUNT(CASE WHEN diem_tin_nhiem > 31 AND diem_tin_nhiem <= 40 THEN 1 END) as nguy_co_cao,
                    COUNT(CASE WHEN diem_tin_nhiem > 40 THEN 1 END) as rat_cao
                FROM data_goc", unitFilter);

            using (var cmd = new OracleCommand(sqlSummary, conn))
            {
                cmd.BindByName = true;
                if (!isSuperAdmin && !string.IsNullOrWhiteSpace(userMaDv))
                {
                    cmd.Parameters.Add("ma_dv", OracleDbType.Varchar2).Value = userMaDv;
                }

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    result.TongNguyCo = reader["tong_nguy_co"] != DBNull.Value ? Convert.ToInt32(reader["tong_nguy_co"]) : 0;
                    result.DaXuLy = reader["da_xu_ly"] != DBNull.Value ? Convert.ToInt32(reader["da_xu_ly"]) : 0;
                    result.AvgScore = reader["avg_score"] != DBNull.Value ? Convert.ToDouble(reader["avg_score"]) : 0.0;
                    result.BinhThuong = reader["binh_thuong"] != DBNull.Value ? Convert.ToInt32(reader["binh_thuong"]) : 0;
                    result.TheoDoi = reader["theo_doi"] != DBNull.Value ? Convert.ToInt32(reader["theo_doi"]) : 0;
                    result.NguyCo = reader["nguy_co"] != DBNull.Value ? Convert.ToInt32(reader["nguy_co"]) : 0;
                    result.NguyCoCao = reader["nguy_co_cao"] != DBNull.Value ? Convert.ToInt32(reader["nguy_co_cao"]) : 0;
                    result.RatCao = reader["rat_cao"] != DBNull.Value ? Convert.ToInt32(reader["rat_cao"]) : 0;
                }
            }

            // 2. LẤY BIỂU ĐỒ CỘT BAR THEO ĐƠN VỊ (>= 29 ĐIỂM)
            string sqlUnits = string.Format(@"
                WITH data_goc AS (
                    SELECT t.ma_dv, t.ten_dv, t.diem_tin_nhiem
                    FROM brcd_dhgh t
                    WHERE t.trangthaitb_id = 1 {0}
                )
                SELECT NVL(TRIM(ten_dv), 'Không tên') AS ten_dv, COUNT(1) AS total_risk
                FROM data_goc
                WHERE diem_tin_nhiem >= 29 AND ma_dv IS NOT NULL
                GROUP BY ten_dv
                ORDER BY total_risk DESC", unitFilter);

            using (var cmdBar = new OracleCommand(sqlUnits, conn))
            {
                cmdBar.BindByName = true;
                if (!isSuperAdmin && !string.IsNullOrWhiteSpace(userMaDv))
                {
                    cmdBar.Parameters.Add("ma_dv", OracleDbType.Varchar2).Value = userMaDv;
                }

                using var reader = await cmdBar.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    result.UnitRisks.Add(new UnitRiskDto
                    {
                        TenDv = reader["ten_dv"]?.ToString() ?? "",
                        TotalRisk = reader["total_risk"] != DBNull.Value ? Convert.ToInt32(reader["total_risk"]) : 0
                    });
                }
            }

            // 3. ĐẾM SỐ PHIẾU KẾ HOẠCH HÔM NAY (THẺ 2)
            string sqlPhieu = "SELECT COUNT(1) FROM brcd_dhgh_kehoach WHERE TRUNC(ngay_lap_kh) = TRUNC(SYSDATE)"
                            + (!isSuperAdmin && !string.IsNullOrWhiteSpace(userMaDv) ? " AND TRIM(ma_dv) = TRIM(:ma_dv)" : "");

            using (var cmdPhieu = new OracleCommand(sqlPhieu, conn))
            {
                cmdPhieu.BindByName = true;
                if (!isSuperAdmin && !string.IsNullOrWhiteSpace(userMaDv))
                {
                    cmdPhieu.Parameters.Add("ma_dv", OracleDbType.Varchar2).Value = userMaDv;
                }

                var count = await cmdPhieu.ExecuteScalarAsync();
                result.TongPhieuHomNay = count != null && count != DBNull.Value ? Convert.ToInt32(count) : 0;
            }

            return result;
        }

        public async Task<List<RecentActivityDto>> GetRecentActivitiesAsync(string? maDv, bool isSuperAdmin)
        {
            var list = new List<RecentActivityDto>();
            string userMaDv = (maDv?.Length > 7 ? maDv.Substring(0, 7) : maDv)?.Trim() ?? string.Empty;
            string unitFilter = (!isSuperAdmin && !string.IsNullOrWhiteSpace(userMaDv))
                ? " AND TRIM(xl.ma_dv) = TRIM(:ma_dv) "
                : "";

            using var conn = CreateConnection();
            await conn.OpenAsync();

            string sqlRecent = string.Format(@"
                SELECT * FROM (
                    SELECT xl.phieu_id, t.ngay_giao, xl.ngay_tao, t.ma_tb, t.ten_tb, xl.nguoi_xuly, xl.ten_nv, xl.ma_dv, xl.ten_dv, xl.ghi_chu,
                           NVL(xl.da_thay_thietbi, 0) AS da_thay_thietbi, 
                           NVL(xl.da_thietbi_tot, 0) AS da_thietbi_tot, 
                           NVL(xl.da_sua_suyhao, 0) AS da_sua_suyhao, 
                           NVL(xl.da_tuvan_cuoc, 0) AS da_tuvan_cuoc, 
                           NVL(xl.da_trichno_tudong, 0) AS da_trichno_tudong, 
                           NVL(xl.da_tuvan_combo, 0) AS da_tuvan_combo
                    FROM brcd_dhgh_xuly xl
                    LEFT JOIN brcd_dhgh_kehoach t ON xl.phieu_id = t.phieu_id
                    WHERE 1=1 {0}
                    ORDER BY xl.ngay_tao DESC
                ) WHERE ROWNUM <= 5", unitFilter);

            using var cmd = new OracleCommand(sqlRecent, conn);
            cmd.BindByName = true;
            if (!isSuperAdmin && !string.IsNullOrWhiteSpace(userMaDv))
            {
                cmd.Parameters.Add("ma_dv", OracleDbType.Varchar2).Value = userMaDv;
            }

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new RecentActivityDto
                {
                    PhieuId = reader["phieu_id"] != DBNull.Value ? Convert.ToDecimal(reader["phieu_id"]) : null,
                    NgayGiao = reader["ngay_giao"] != DBNull.Value ? Convert.ToDateTime(reader["ngay_giao"]) : null,
                    NgayTao = reader["ngay_tao"] != DBNull.Value ? Convert.ToDateTime(reader["ngay_tao"]) : null,
                    MaTb = reader["ma_tb"]?.ToString() ?? "",
                    TenTb = reader["ten_tb"]?.ToString() ?? "",
                    NguoiXuLy = reader["nguoi_xuly"]?.ToString() ?? "",
                    TenNv = reader["ten_nv"]?.ToString() ?? "",
                    MaDv = reader["ma_dv"]?.ToString() ?? "",
                    TenDv = reader["ten_dv"]?.ToString() ?? "",
                    GhiChu = reader["ghi_chu"]?.ToString() ?? "",
                    DaThayThietbi = Convert.ToInt32(reader["da_thay_thietbi"]),
                    DaThietbiTot = Convert.ToInt32(reader["da_thietbi_tot"]),
                    DaSuaSuyhao = Convert.ToInt32(reader["da_sua_suyhao"]),
                    DaTuvanCuoc = Convert.ToInt32(reader["da_tuvan_cuoc"]),
                    DaTrichnoTudong = Convert.ToInt32(reader["da_trichno_tudong"]),
                    DaTuvanCombo = Convert.ToInt32(reader["da_tuvan_combo"])
                });
            }
            return list;
        }

        public async Task<List<KeHoachItemDto>> GetHighRiskPlansTodayAsync(string? maDv, bool isSuperAdmin)
        {
            var list = new List<KeHoachItemDto>();
            string userMaDv = (maDv?.Length > 7 ? maDv.Substring(0, 7) : maDv)?.Trim() ?? string.Empty;
            string filter = (!isSuperAdmin && !string.IsNullOrWhiteSpace(userMaDv))
                ? " AND TRIM(ma_dv) = TRIM(:ma_dv) "
                : "";

            using var conn = CreateConnection();
            await conn.OpenAsync();

            string sql = $@"
                SELECT phieu_id, ma_tb, ten_tb, so_dt, ten_nvkt, ma_nvkt, ma_dv, ten_dv,
                       NVL(trangthai_phieu, 0) AS trangthai_phieu, ten_nv_nhan, ngay_giao,
                       NVL(diem_tbi, 0) AS diem_tbi, NVL(diem_suyhao, 0) AS diem_suyhao, NVL(diem_offlos, 0) AS diem_offlos,
                       NVL(diem_tttt, 0) AS diem_tttt, NVL(diem_dadv, 0) AS diem_dadv, NVL(diem_bhll, 0) AS diem_bhll,
                       NVL(diem_kohl, 0) AS diem_kohl, NVL(diem_tgsc, 0) AS diem_tgsc, NVL(diem_tgsd, 0) AS diem_tgsd,
                       NVL(diem_tin_nhiem, 0) AS diem_tin_nhiem, ghi_chu_giao
                FROM brcd_dhgh_kehoach
                WHERE TRUNC(ngay_lap_kh) = TRUNC(SYSDATE) {filter}
                ORDER BY diem_tin_nhiem DESC";

            using var cmd = new OracleCommand(sql, conn);
            cmd.BindByName = true;
            if (!isSuperAdmin && !string.IsNullOrWhiteSpace(userMaDv))
            {
                cmd.Parameters.Add("ma_dv", OracleDbType.Varchar2).Value = userMaDv;
            }

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new KeHoachItemDto
                {
                    PhieuId = Convert.ToDecimal(reader["phieu_id"]),
                    MaTb = reader["ma_tb"]?.ToString() ?? "",
                    TenTb = reader["ten_tb"]?.ToString() ?? "",
                    SoDt = reader["so_dt"]?.ToString() ?? "",
                    TenNvkt = reader["ten_nvkt"]?.ToString() ?? "",
                    MaNvkt = reader["ma_nvkt"]?.ToString() ?? "",
                    MaDv = reader["ma_dv"]?.ToString() ?? "",
                    TenDv = reader["ten_dv"]?.ToString() ?? "",
                    TrangThaiPhieu = Convert.ToInt32(reader["trangthai_phieu"]),
                    TenNvNhan = reader["ten_nv_nhan"] != DBNull.Value ? reader["ten_nv_nhan"]?.ToString() : null,
                    NgayGiao = reader["ngay_giao"] != DBNull.Value ? Convert.ToDateTime(reader["ngay_giao"]) : null,
                    DiemTbi = Convert.ToInt32(reader["diem_tbi"]),
                    DiemSuyhao = Convert.ToInt32(reader["diem_suyhao"]),
                    DiemOfflos = Convert.ToInt32(reader["diem_offlos"]),
                    DiemTttt = Convert.ToInt32(reader["diem_tttt"]),
                    DiemDadv = Convert.ToInt32(reader["diem_dadv"]),
                    DiemBhll = Convert.ToInt32(reader["diem_bhll"]),
                    DiemKohl = Convert.ToInt32(reader["diem_kohl"]),
                    DiemTgsc = Convert.ToDouble(reader["diem_tgsc"]),
                    DiemTgsd = Convert.ToInt32(reader["diem_tgsd"]),
                    DiemTinNhiem = Convert.ToInt32(reader["diem_tin_nhiem"]),
                    GhiChuGiao = reader["ghi_chu_giao"] != DBNull.Value ? reader["ghi_chu_giao"]?.ToString() : null
                });
            }
            return list;
        }

        public async Task<(bool Success, string Message)> AssignPhieuAsync(decimal phieuId, string maNvGiao, string? ghiChu)
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();

            string sqlSelect = @"
                SELECT ma_nvkt, ten_nvkt, ma_tb, ten_tb, diem_tin_nhiem, ten_dv, trangthai_phieu,
                       diem_tgsd, diem_dadv, diem_tttt, diem_tbi, diem_suyhao, diem_offlos, diem_bhll, diem_kohl, diem_tgsc
                FROM brcd_dhgh_kehoach
                WHERE phieu_id = :phieu_id";

            string maNvKt = "", tenNvKt = "", maTb = "", tenTb = "", tenDv = "";
            int diemTinNhiem = 0, dTgsd = 0, dDadv = 0, dTttt = 0, dTbi = 0, dSuyhao = 0, dLos = 0, dBhll = 0, dKohl = 0;
            double dTgsc = 0.0;

            using (var cmdSel = new OracleCommand(sqlSelect, conn))
            {
                cmdSel.BindByName = true;
                cmdSel.Parameters.Add("phieu_id", OracleDbType.Decimal).Value = phieuId;
                using var reader = await cmdSel.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    int tt = reader["trangthai_phieu"] != DBNull.Value ? Convert.ToInt32(reader["trangthai_phieu"]) : 0;
                    if (tt != 0) return (false, "Phiếu này đã được giao hoặc đã xử lý trước đó!");

                    maNvKt = reader["ma_nvkt"]?.ToString() ?? "";
                    tenNvKt = reader["ten_nvkt"]?.ToString() ?? "";
                    maTb = reader["ma_tb"]?.ToString() ?? "";
                    tenTb = reader["ten_tb"]?.ToString() ?? "";
                    tenDv = reader["ten_dv"]?.ToString() ?? "";
                    diemTinNhiem = reader["diem_tin_nhiem"] != DBNull.Value ? Convert.ToInt32(reader["diem_tin_nhiem"]) : 0;
                    dTgsd = reader["diem_tgsd"] != DBNull.Value ? Convert.ToInt32(reader["diem_tgsd"]) : 0;
                    dDadv = reader["diem_dadv"] != DBNull.Value ? Convert.ToInt32(reader["diem_dadv"]) : 0;
                    dTttt = reader["diem_tttt"] != DBNull.Value ? Convert.ToInt32(reader["diem_tttt"]) : 0;
                    dTbi = reader["diem_tbi"] != DBNull.Value ? Convert.ToInt32(reader["diem_tbi"]) : 0;
                    dSuyhao = reader["diem_suyhao"] != DBNull.Value ? Convert.ToInt32(reader["diem_suyhao"]) : 0;
                    dLos = reader["diem_offlos"] != DBNull.Value ? Convert.ToInt32(reader["diem_offlos"]) : 0;
                    dBhll = reader["diem_bhll"] != DBNull.Value ? Convert.ToInt32(reader["diem_bhll"]) : 0;
                    dKohl = reader["diem_kohl"] != DBNull.Value ? Convert.ToInt32(reader["diem_kohl"]) : 0;
                    dTgsc = reader["diem_tgsc"] != DBNull.Value ? Convert.ToDouble(reader["diem_tgsc"]) : 0.0;
                }
                else
                {
                    return (false, "Không tìm thấy thông tin phiếu kế hoạch trên hệ thống.");
                }
            }

            string sqlUpdate = @"
                UPDATE brcd_dhgh_kehoach
                SET trangthai_phieu = 1,
                    ma_nv_giao = :ma_nv_giao,
                    ngay_giao = SYSDATE,
                    ma_nv_nhan = ma_nvkt,
                    ten_nv_nhan = ten_nvkt,
                    ghi_chu_giao = :ghi_chu_giao
                WHERE phieu_id = :phieu_id AND trangthai_phieu = 0";

            using (var cmdUpd = new OracleCommand(sqlUpdate, conn))
            {
                cmdUpd.BindByName = true;
                cmdUpd.Parameters.Add("ma_nv_giao", OracleDbType.Varchar2).Value = maNvGiao;
                cmdUpd.Parameters.Add("ghi_chu_giao", OracleDbType.NVarchar2).Value = (object?)ghiChu ?? DBNull.Value;
                cmdUpd.Parameters.Add("phieu_id", OracleDbType.Decimal).Value = phieuId;

                int rows = await cmdUpd.ExecuteNonQueryAsync();
                if (rows > 0)
                {
                    await SendTelegramGiaoPhieuAsync(conn, maNvKt, tenNvKt, maTb, tenTb, diemTinNhiem, tenDv,
                        dTgsd, dDadv, dTttt, dTbi, dSuyhao, dLos, dBhll, dKohl, dTgsc, ghiChu);
                    return (true, "Đã giao phiếu và gửi thông báo Telegram thành công.");
                }
            }

            return (false, "Không thể cập nhật trạng thái phiếu.");
        }

        public async Task<(int Count, string Message)> AssignAllPhieuAsync(string? maDv, bool isSuperAdmin, string maNvGiao, string? ghiChuChung)
        {
            string userMaDv = (maDv?.Length > 7 ? maDv.Substring(0, 7) : maDv)?.Trim() ?? string.Empty;
            string filter = (!isSuperAdmin && !string.IsNullOrWhiteSpace(userMaDv))
                ? " AND TRIM(ma_dv) = TRIM(:ma_dv)"
                : "";

            using var conn = CreateConnection();
            await conn.OpenAsync();

            string sqlGet = $@"
                SELECT phieu_id, ma_nvkt, ten_nvkt, ma_tb, ten_tb, diem_tin_nhiem, ten_dv,
                       diem_tgsd, diem_dadv, diem_tttt, diem_tbi, diem_suyhao, diem_offlos, diem_bhll, diem_kohl, diem_tgsc
                FROM brcd_dhgh_kehoach
                WHERE TRUNC(ngay_lap_kh) = TRUNC(SYSDATE) AND NVL(trangthai_phieu, 0) = 0 {filter}";

            var list = new List<KeHoachItemDto>();
            using (var cmdGet = new OracleCommand(sqlGet, conn))
            {
                cmdGet.BindByName = true;
                if (!isSuperAdmin && !string.IsNullOrWhiteSpace(userMaDv))
                {
                    cmdGet.Parameters.Add("ma_dv", OracleDbType.Varchar2).Value = userMaDv;
                }

                using var reader = await cmdGet.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new KeHoachItemDto
                    {
                        PhieuId = Convert.ToDecimal(reader["phieu_id"]),
                        MaNvkt = reader["ma_nvkt"]?.ToString() ?? "",
                        TenNvkt = reader["ten_nvkt"]?.ToString() ?? "",
                        MaTb = reader["ma_tb"]?.ToString() ?? "",
                        TenTb = reader["ten_tb"]?.ToString() ?? "",
                        TenDv = reader["ten_dv"]?.ToString() ?? "",
                        DiemTinNhiem = reader["diem_tin_nhiem"] != DBNull.Value ? Convert.ToInt32(reader["diem_tin_nhiem"]) : 0,
                        DiemTgsd = reader["diem_tgsd"] != DBNull.Value ? Convert.ToInt32(reader["diem_tgsd"]) : 0,
                        DiemDadv = reader["diem_dadv"] != DBNull.Value ? Convert.ToInt32(reader["diem_dadv"]) : 0,
                        DiemTttt = reader["diem_tttt"] != DBNull.Value ? Convert.ToInt32(reader["diem_tttt"]) : 0,
                        DiemTbi = reader["diem_tbi"] != DBNull.Value ? Convert.ToInt32(reader["diem_tbi"]) : 0,
                        DiemSuyhao = reader["diem_suyhao"] != DBNull.Value ? Convert.ToInt32(reader["diem_suyhao"]) : 0,
                        DiemOfflos = reader["diem_offlos"] != DBNull.Value ? Convert.ToInt32(reader["diem_offlos"]) : 0,
                        DiemBhll = reader["diem_bhll"] != DBNull.Value ? Convert.ToInt32(reader["diem_bhll"]) : 0,
                        DiemKohl = reader["diem_kohl"] != DBNull.Value ? Convert.ToInt32(reader["diem_kohl"]) : 0,
                        DiemTgsc = reader["diem_tgsc"] != DBNull.Value ? Convert.ToDouble(reader["diem_tgsc"]) : 0.0
                    });
                }
            }

            if (list.Count == 0) return (0, "Không còn phiếu nào ở trạng thái mới lập để giao!");

            string sqlUpdate = @"
                UPDATE brcd_dhgh_kehoach
                SET trangthai_phieu = 1,
                    ma_nv_giao = :ma_nv_giao,
                    ngay_giao = SYSDATE,
                    ma_nv_nhan = ma_nvkt,
                    ten_nv_nhan = ten_nvkt,
                    ghi_chu_giao = :ghi_chu_giao
                WHERE phieu_id = :phieu_id AND trangthai_phieu = 0";

            int success = 0;
            using var cmdUpd = new OracleCommand(sqlUpdate, conn);
            cmdUpd.BindByName = true;
            cmdUpd.Parameters.Add("ma_nv_giao", OracleDbType.Varchar2).Value = maNvGiao;
            cmdUpd.Parameters.Add("ghi_chu_giao", OracleDbType.NVarchar2).Value = (object?)ghiChuChung ?? DBNull.Value;
            var pPhieuId = cmdUpd.Parameters.Add("phieu_id", OracleDbType.Decimal);

            foreach (var p in list)
            {
                pPhieuId.Value = p.PhieuId;
                int rows = await cmdUpd.ExecuteNonQueryAsync();
                if (rows > 0)
                {
                    success++;
                    await SendTelegramGiaoPhieuAsync(conn, p.MaNvkt, p.TenNvkt, p.MaTb, p.TenTb, p.DiemTinNhiem, p.TenDv,
                        p.DiemTgsd, p.DiemDadv, p.DiemTttt, p.DiemTbi, p.DiemSuyhao, p.DiemOfflos, p.DiemBhll, p.DiemKohl, p.DiemTgsc, ghiChuChung);
                }
            }

            return (success, $"Đã giao thành công {success} phiếu và gửi thông báo Telegram đến nhân viên kỹ thuật phụ trách.");
        }

        private async Task SendTelegramGiaoPhieuAsync(
            OracleConnection conn, string maNvKt, string tenNvKt, string maTb, string tenTb, int diemTinNhiem, string tenDv,
            int dTgsd, int dDadv, int dTttt, int dTbi, int dSuyhao, int dLos, int dBhll, int dKohl, double dTgsc,
            string? ghiChuGiao)
        {
            if (string.IsNullOrEmpty(maNvKt)) return;

            var sb = new StringBuilder();
            sb.AppendLine("🔔 THÔNG BÁO GIAO PHIẾU XỬ LÝ MẮT LƯỚI");
            sb.AppendLine($"Thuê bao: {maTb} - {tenTb}");
            sb.AppendLine($"Đơn vị: {tenDv}");
            sb.AppendLine($"Tổng điểm rủi ro: {diemTinNhiem}đ");
            sb.AppendLine($"Nhân viên phụ trách: {tenNvKt}");

            if (!string.IsNullOrWhiteSpace(ghiChuGiao))
            {
                sb.AppendLine();
                sb.AppendLine("📝 GHI CHÚ GIAO VIỆC:");
                sb.AppendLine(ghiChuGiao.Trim());
            }

            sb.AppendLine();
            sb.AppendLine("📊 CHI TIẾT ĐIỂM CHỈ TIÊU PHẠT:");
            sb.AppendLine($"• ONT & Wi-Fi: {dTbi}/15đ {(dTbi >= 10 ? " ⚠️ (CẦN XỬ LÝ NGAY)" : "")}");
            sb.AppendLine($"• Suy hao quang: {dSuyhao}/15đ {(dSuyhao >= 8 ? " ⚠️ (CẦN XỬ LÝ NGAY)" : "")}");
            sb.AppendLine($"• Mất tín hiệu LOS: {dLos}/14đ {(dLos >= 8 ? " ⚠️ (CẦN XỬ LÝ NGAY)" : "")}");
            sb.AppendLine($"• Trả trước cước: {dTttt}/10đ {(dTttt >= 10 ? " ⚠️ (CẦN XỬ LÝ NGAY)" : "")}");
            sb.AppendLine($"• Combo/Đa dịch vụ: {dDadv}/5đ {(dDadv >= 3 ? " ⚠️ (CẦN XỬ LÝ NGAY)" : "")}");
            sb.AppendLine($"• Báo hỏng lặp lại: {dBhll}/20đ {(dBhll >= 12 ? " ⚠️ (CẦN XỬ LÝ NGAY)" : "")}");
            sb.AppendLine($"• Đánh giá không hài lòng: {dKohl}/10đ {(dKohl >= 5 ? " ⚠️ (CẦN XỬ LÝ NGAY)" : "")}");
            sb.AppendLine($"• Thời gian sửa chữa: {dTgsc:N1}/6đ {(dTgsc >= 4 ? " ⚠️ (CẦN XỬ LÝ NGAY)" : "")}");
            sb.AppendLine($"• Thời gian sử dụng: {dTgsd}/5đ {(dTgsd >= 3 ? " ⚠️ (CẦN XỬ LÝ NGAY)" : "")}");
            sb.AppendLine();
            sb.AppendLine("Đề nghị anh/chị khẩn trương kiểm tra nhà khách hàng, củng cố mạng lưới thực địa và cập nhật kết quả trong ngày.");

            string sqlNotify = @"
                SELECT PKG_NOTIFICATION.SEND_TELEGRAM_POST@ttkdhnm(chat_id, :noi_dung)
                FROM telegram_user
                WHERE chat_id IS NOT NULL AND code = :ma_nv";

            try
            {
                using var cmdNotify = new OracleCommand(sqlNotify, conn);
                cmdNotify.BindByName = true;
                cmdNotify.Parameters.Add("noi_dung", OracleDbType.Varchar2).Value = sb.ToString();
                cmdNotify.Parameters.Add("ma_nv", OracleDbType.Varchar2).Value = maNvKt;
                using var reader = await cmdNotify.ExecuteReaderAsync();
                while (await reader.ReadAsync()) { }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Gửi Telegram cho NV {MaNv} thất bại: {Message}", maNvKt, ex.Message);
            }
        }

        public async Task<byte[]> ExportExcelPlansTodayAsync(string? maDv, bool isSuperAdmin)
        {
            var plans = await GetHighRiskPlansTodayAsync(maDv, isSuperAdmin);
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("DanhSachPhieuKeHoach");

            string[] headers = {
                "Mã Thuê Bao", "Tên Khách Hàng", "Số Điện Thoại", "Nhân Viên Kỹ Thuật Phụ Trách",
                "Mã ĐV", "Tên Đơn Vị", "Trạng Thái Phiếu", "Nhân Viên Nhận Việc", "Ngày Giao",
                "Điểm TBI ONT", "Điểm Suy Hao", "Điểm OFF LOS", "Điểm Trả Trước", "Điểm Đa Dịch Vụ",
                "Điểm Báo Hỏng Lặp", "Điểm CSAT (KHL)", "Điểm TG Sửa Chữa", "Điểm TG Sử Dụng",
                "Tổng Điểm Tín Nhiệm", "Ghi Chú Giao Việc"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0f3358");
                cell.Style.Font.FontColor = XLColor.White;
            }

            int row = 2;
            foreach (var r in plans)
            {
                string tt = r.TrangThaiPhieu switch
                {
                    1 => "Đã giao",
                    2 => "Đã xử lý thành công",
                    3 => "Chưa xử lý được",
                    _ => "Mới lập"
                };

                ws.Cell(row, 1).Value = r.MaTb;
                ws.Cell(row, 2).Value = r.TenTb;
                ws.Cell(row, 3).Value = r.SoDt;
                ws.Cell(row, 4).Value = r.TenNvkt;
                ws.Cell(row, 5).Value = r.MaDv;
                ws.Cell(row, 6).Value = r.TenDv;
                ws.Cell(row, 7).Value = tt;
                ws.Cell(row, 8).Value = r.TenNvNhan ?? "-";
                ws.Cell(row, 9).Value = r.NgayGiao?.ToString("dd/MM/yyyy HH:mm") ?? "-";
                ws.Cell(row, 10).Value = r.DiemTbi;
                ws.Cell(row, 11).Value = r.DiemSuyhao;
                ws.Cell(row, 12).Value = r.DiemOfflos;
                ws.Cell(row, 13).Value = r.DiemTttt;
                ws.Cell(row, 14).Value = r.DiemDadv;
                ws.Cell(row, 15).Value = r.DiemBhll;
                ws.Cell(row, 16).Value = r.DiemKohl;
                ws.Cell(row, 17).Value = r.DiemTgsc.ToString("N1");
                ws.Cell(row, 18).Value = r.DiemTgsd;
                ws.Cell(row, 19).Value = r.DiemTinNhiem;
                ws.Cell(row, 20).Value = r.GhiChuGiao ?? "";
                row++;
            }

            ws.Columns().AdjustToContents();
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }
    }
}