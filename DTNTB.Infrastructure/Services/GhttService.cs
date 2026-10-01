using ClosedXML.Excel;
using Dapper;
using DTNTB.Core.DTOs;
using DTNTB.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DTNTB.Infrastructure.Services
{
    public class GhttService : IGhttService
    {
        private readonly string _connString;

        public GhttService(IConfiguration config)
        {
            _connString = config.GetConnectionString("ConnectionString_NBH")
                       ?? config.GetConnectionString("ConnectionString_NBH_NEW")
                       ?? string.Empty;
        }

        // 1. LẤY DANH MỤC ĐƠN VỊ CHO DROPDOWN
        // 1. LẤY DANH MỤC ĐƠN VỊ CHO DROPDOWN
        public async Task<List<GhttDonViDto>> GetDanhSachDonViAsync(string loaiDv, int thang, string dataScope = "TOAN_TINH", string username = "")
        {
            var list = new List<GhttDonViDto>();

            if (!string.Equals(loaiDv, "NVDB", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(new GhttDonViDto { DonViId = 0, TenDv = "ALL" });
                return list;
            }

            try
            {
                using var conn = new OracleConnection(_connString);
                await conn.OpenAsync();

                // NẾU LÀ CẤP 4 (TO_QL) HOẶC CẤP 5 (NHAN_VIEN):
                // Khớp theo 7 ký tự đầu của ma_dv để lấy đúng Đơn vị cha trong ghtt_tonghop
                string userDonViFilter = "";
                if (dataScope == "TO_QL" || dataScope == "NHAN_VIEN")
                {
                    userDonViFilter = @" AND SUBSTR(TRIM(ma_dv), 1, 7) IN (
                SELECT SUBSTR(TRIM(ma_dv), 1, 7) 
                FROM v_nguoidung_diaban 
                WHERE UPPER(TRIM(ma_nd)) = UPPER(TRIM(:username))
                  AND ma_dv IS NOT NULL
            )";
                }

                string qry = $@"SELECT DISTINCT donvi_id AS DonViId, ten_dv AS TenDv 
                        FROM ghtt_tonghop 
                        WHERE sl_thang = :thang 
                          AND (ma_dv LIKE '215.3__' OR ma_dv LIKE '215.7__')
                          {userDonViFilter}
                        ORDER BY ten_dv";

                using var cmd = new OracleCommand(qry, conn);
                cmd.Parameters.Add("thang", OracleDbType.Varchar2).Value = thang.ToString();
                if (!string.IsNullOrEmpty(userDonViFilter))
                {
                    cmd.Parameters.Add("username", OracleDbType.Varchar2).Value = username;
                }

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new GhttDonViDto
                    {
                        DonViId = reader["DonViId"] == DBNull.Value ? 0 : Convert.ToInt32(reader["DonViId"]),
                        TenDv = reader["TenDv"]?.ToString() ?? ""
                    });
                }
            }
            catch (Exception)
            {
                list.Add(new GhttDonViDto { DonViId = 0, TenDv = "ALL" });
            }

            return list;
        }

        // 2. HÀM HỖ TRỢ: Lấy donvi_id đơn vị cha của user trong bảng ghtt_tonghop theo 7 ký tự đầu ma_dv
        private async Task<int> GetUserDonViIdAsync(string username)
        {
            try
            {
                using var conn = new OracleConnection(_connString);
                // Tìm donvi_id cấp đơn vị trong ghtt_tonghop khớp với 7 ký tự đầu ma_dv của người dùng
                string qry = @"
            SELECT g.donvi_id 
            FROM ghtt_tonghop g
            INNER JOIN v_nguoidung_diaban u 
                ON SUBSTR(TRIM(g.ma_dv), 1, 7) = SUBSTR(TRIM(u.ma_dv), 1, 7)
            WHERE UPPER(TRIM(u.ma_nd)) = UPPER(TRIM(:username))
              AND ROWNUM = 1";

                await conn.OpenAsync();
                using var cmd = new OracleCommand(qry, conn);
                cmd.Parameters.Add("username", OracleDbType.Varchar2).Value = username;
                var val = await cmd.ExecuteScalarAsync();
                return (val != null && val != DBNull.Value) ? Convert.ToInt32(val) : 0;
            }
            catch
            {
                return 0;
            }
        }

        // 2. LẤY DỮ LIỆU BÁO CÁO VÀ PIVOT MA TRẬN ĐỘNG (HOÀN TOÀN TỪ DATABASE)
        public async Task<GhttReportResponseDto> GetBaoCaoGhttAsync(int thang, int donvi, string loaiDv, string dataScope = "TOAN_TINH", string username = "")
        {
            var response = new GhttReportResponseDto();
            try
            {
                response.LastSyncGhtt = await GetLastSyncAsync(thang, "GHTT");
                response.LastSyncChot = await GetLastSyncAsync(thang, "CHOT_GHTT");

                // Nếu là cấp 4 hoặc 5 mà client gửi lên donvi = 0, tự động lấy đơn vị của user
                if ((dataScope == "TO_QL" || dataScope == "NHAN_VIEN") && donvi == 0)
                {
                    donvi = await GetUserDonViIdAsync(username);
                }

                DataTable dt = await LoadGhttTongHopDataTableAsync(thang, donvi, loaiDv);

                // Dù không có dòng số liệu nào, vẫn giữ headers
                List<string> listColumns = DetermineHeaderColumns(dt, loaiDv);
                response.Headers = listColumns;

                if (dt == null || dt.Rows.Count == 0)
                {
                    return response;
                }

                CleanTenDvColumn(dt);

                if (!string.Equals(loaiDv, "ALL", StringComparison.OrdinalIgnoreCase))
                {
                    listColumns = DetermineHeaderColumns(dt, loaiDv);
                    response.Headers = listColumns;
                }

                response.Rows = PivotReportData(dt, listColumns, loaiDv);
            }
            catch (Exception)
            {
                if (string.Equals(loaiDv, "ALL", StringComparison.OrdinalIgnoreCase))
                {
                    response.Headers = new List<string> { "Cá nhân", "Doanh nghiệp" };
                }
                else
                {
                    response.Headers = new List<string>();
                }
                response.Rows = new List<GhttReportRowDto>();
            }
            return response;
        }

        // Pivot dữ liệu an toàn dùng Convert.ToDecimal
        private static List<GhttReportRowDto> PivotReportData(DataTable dt, List<string> listColumns, string loaiDv)
        {
            return dt.AsEnumerable()
                .GroupBy(row => new
                {
                    MaCS = row["MA_CS"]?.ToString() ?? "",
                    Name = row["NAME"]?.ToString() ?? "",
                    MucTieu = row["MUCTIEU"]?.ToString() ?? ""
                })
                .Select(g =>
                {
                    var minValues = new List<decimal>();
                    if ((g.Key.MaCS == "CS1" || g.Key.MaCS == "CS2") && !string.Equals(loaiDv, "ALL", StringComparison.OrdinalIgnoreCase))
                    {
                        minValues = g.Where(x => Convert.ToDecimal(x["TONG"] == DBNull.Value ? 0 : x["TONG"]) > 0)
                                     .Select(x => Convert.ToDecimal(x["TLHT"] == DBNull.Value ? 0 : x["TLHT"]))
                                     .OrderBy(v => v)
                                     .Take(3)
                                     .Distinct()
                                     .ToList();
                    }

                    return new GhttReportRowDto
                    {
                        MaCS = g.Key.MaCS,
                        TenChiSo = g.Key.Name,
                        MucTieu = g.Key.MucTieu,
                        Values = listColumns.Select(col =>
                        {
                            DataRow? item = FindMatchedRow(g, col, loaiDv);
                            if (item != null)
                            {
                                decimal tlht = item["TLHT"] == DBNull.Value ? 0 : Convert.ToDecimal(item["TLHT"]);
                                decimal nguong = item["NGUONG"] == DBNull.Value ? 0 : Convert.ToDecimal(item["NGUONG"]);
                                decimal tong = item["TONG"] == DBNull.Value ? 0 : Convert.ToDecimal(item["TONG"]);
                                int loaiNguong = item["LOAI_NGUONG"] == DBNull.Value ? 1 : Convert.ToInt32(item["LOAI_NGUONG"]);
                                decimal thuchien = item["THUCHIEN"] == DBNull.Value ? 0 : Convert.ToDecimal(item["THUCHIEN"]);

                                bool datMucTieu = (loaiNguong == 1) ? (tlht >= nguong) : (tlht <= nguong);
                                bool isBottomThree = minValues.Count > 0 && tong > 0 && minValues.Contains(tlht);

                                string colorClass = (tong > 0) ? (datMucTieu ? "text-success" : "text-warning") : "";
                                string bgClass = (datMucTieu && tong > 0) ? "bg-light-green" : "";

                                if (isBottomThree)
                                {
                                    colorClass = "text-danger-bold";
                                    bgClass = "bg-light-red";
                                }

                                return new GhttUnitValueDto
                                {
                                    TlhtDisplay = (tong > 0) ? string.Format("{0:N2}%", tlht) : "—",
                                    PhanSo = string.Format("{0:N0} / {1:N0}", thuchien, tong),
                                    ColorClass = colorClass,
                                    BgClass = bgClass
                                };
                            }

                            return new GhttUnitValueDto { TlhtDisplay = "—", PhanSo = "0 / 0" };
                        }).ToList()
                    };
                }).ToList();
        }

        // 3. XUẤT BÁO CÁO EXCEL VỚI CLOSEDXML
        public async Task<byte[]> ExportExcelGhttAsync(int thang, int donvi, string loaiDv, string dataScope = "TOAN_TINH", string username = "")
        {
            if ((dataScope == "TO_QL" || dataScope == "NHAN_VIEN") && donvi == 0)
            {
                donvi = await GetUserDonViIdAsync(username);
            }

            DataTable dt = await LoadGhttTongHopDataTableAsync(thang, donvi, loaiDv);
            CleanTenDvColumn(dt);
            List<string> listColumns = DetermineHeaderColumns(dt, loaiDv);

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("BaoCaoGHTT");

            ws.Cell(1, 1).Value = "Mã chỉ số";
            ws.Cell(1, 2).Value = "Tên chỉ số";
            ws.Cell(1, 3).Value = "Mục tiêu";
            for (int i = 0; i < listColumns.Count; i++)
            {
                ws.Cell(1, i + 4).Value = listColumns[i];
            }

            var headerRange = ws.Range(1, 1, 1, listColumns.Count + 3);
            headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#007bff");
            headerRange.Style.Font.FontColor = XLColor.White;
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var groupedData = dt.AsEnumerable().GroupBy(row => new
            {
                MaCS = row["MA_CS"]?.ToString() ?? "",
                Name = row["NAME"]?.ToString() ?? "",
                MucTieu = row["MUCTIEU"]?.ToString() ?? ""
            }).ToList();

            int currentRow = 2;
            foreach (var g in groupedData)
            {
                ws.Cell(currentRow, 1).Value = g.Key.MaCS;
                ws.Cell(currentRow, 2).Value = g.Key.Name;
                ws.Cell(currentRow, 3).Value = g.Key.MucTieu;

                for (int i = 0; i < listColumns.Count; i++)
                {
                    string colName = listColumns[i];
                    DataRow? item = FindMatchedRow(g, colName, loaiDv);
                    var cell = ws.Cell(currentRow, i + 4);

                    if (item != null)
                    {
                        decimal tlht = item["TLHT"] == DBNull.Value ? 0 : Convert.ToDecimal(item["TLHT"]);
                        decimal nguong = item["NGUONG"] == DBNull.Value ? 0 : Convert.ToDecimal(item["NGUONG"]);
                        decimal tong = item["TONG"] == DBNull.Value ? 0 : Convert.ToDecimal(item["TONG"]);
                        decimal thuchien = item["THUCHIEN"] == DBNull.Value ? 0 : Convert.ToDecimal(item["THUCHIEN"]);
                        int loaiNguong = item["LOAI_NGUONG"] == DBNull.Value ? 1 : Convert.ToInt32(item["LOAI_NGUONG"]);

                        if (tong > 0)
                        {
                            cell.Value = string.Format("{0:N2}%\n{1:N0} / {2:N0}", tlht, thuchien, tong);
                            cell.Style.Alignment.WrapText = true;

                            bool dat = (loaiNguong == 1) ? (tlht >= nguong) : (tlht <= nguong);
                            cell.Style.Font.FontColor = dat ? XLColor.FromHtml("#28a745") : XLColor.FromHtml("#f39c12");
                            cell.Style.Font.Bold = true;

                            if (dat) cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#f0fff4");
                        }
                        else
                        {
                            cell.Value = "—\n0 / 0";
                            cell.Style.Alignment.WrapText = true;
                            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        }
                    }
                    else
                    {
                        cell.Value = "—";
                    }
                }
                currentRow++;
            }

            var fullRange = ws.Range(1, 1, currentRow - 1, listColumns.Count + 3);
            fullRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            fullRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            fullRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            ws.Columns().AdjustToContents();
            ws.Column(2).Width = 40;

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        // 4. TỔNG HỢP SỐ LIỆU
        public async Task<bool> TongHopSoLieuAsync(int thang, string nguoiCn)
        {
            using var conn = new OracleConnection(_connString);
            using var cmd = new OracleCommand("ghtt.P_DONGBO_ALL_NEW", conn)
            {
                CommandType = CommandType.StoredProcedure,
                BindByName = true
            };

            cmd.Parameters.Add("P_MONTH", OracleDbType.Int32, ParameterDirection.Input).Value = thang;
            cmd.Parameters.Add("P_NGUOICN", OracleDbType.Varchar2, ParameterDirection.Input).Value = nguoiCn;

            try
            {
                await conn.OpenAsync();
                await cmd.ExecuteNonQueryAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        // 5. CHỐT SỐ LIỆU
        public async Task<string> ChotSoLieuAsync(int thang, string nguoiCn)
        {
            using var conn = new OracleConnection(_connString);
            using var cmd = new OracleCommand("ghtt.P_CHOT_SOLIEU_NEW", conn)
            {
                CommandType = CommandType.StoredProcedure,
                BindByName = true
            };

            cmd.Parameters.Add("P_THANG", OracleDbType.Int32, ParameterDirection.Input).Value = thang;
            cmd.Parameters.Add("P_MESSAGE", OracleDbType.Varchar2, 4000).Direction = ParameterDirection.Output;
            cmd.Parameters.Add("P_NGUOICN", OracleDbType.Varchar2, ParameterDirection.Input).Value = nguoiCn;

            try
            {
                await conn.OpenAsync();
                await cmd.ExecuteNonQueryAsync();

                if (cmd.Parameters["P_MESSAGE"].Value != DBNull.Value)
                {
                    return cmd.Parameters["P_MESSAGE"].Value.ToString() ?? string.Empty;
                }
                return "Chốt số liệu thành công.";
            }
            catch (Exception ex)
            {
                return "Lỗi hệ thống: " + ex.Message;
            }
        }

        // ==========================================
        // PRIVATE HELPER METHODS
        // ==========================================
        private async Task<DataTable> LoadGhttTongHopDataTableAsync(int thang, int donvi, string loaidv)
        {
            var dt = new DataTable();
            using var conn = new OracleConnection(_connString);
            using var cmd = new OracleCommand("ghtt.SP_GET_GHTT", conn)
            {
                CommandType = CommandType.StoredProcedure,
                BindByName = true
            };

            cmd.Parameters.Add("P_MONTH", OracleDbType.Int32, ParameterDirection.Input).Value = thang;
            cmd.Parameters.Add("P_DEPARTMENT", OracleDbType.Int32, ParameterDirection.Input).Value = donvi;
            cmd.Parameters.Add("P_LOAI", OracleDbType.Varchar2, ParameterDirection.Input).Value = loaidv;
            cmd.Parameters.Add("RSOUT", OracleDbType.RefCursor, ParameterDirection.Output);

            await conn.OpenAsync();
            using var reader = await cmd.ExecuteReaderAsync();
            dt.Load(reader);
            return dt;
        }

        private async Task<string?> GetLastSyncAsync(int thang, string maLog)
        {
            using var conn = new OracleConnection(_connString);
            using var cmd = new OracleCommand("ghtt.SP_GET_LAST_SYNC_NEW", conn)
            {
                CommandType = CommandType.StoredProcedure,
                BindByName = true
            };

            cmd.Parameters.Add("P_MONTH", OracleDbType.Int32, ParameterDirection.Input).Value = thang;
            cmd.Parameters.Add("P_MALOG", OracleDbType.Varchar2, ParameterDirection.Input).Value = maLog;
            cmd.Parameters.Add("RSOUT", OracleDbType.RefCursor, ParameterDirection.Output);

            try
            {
                await conn.OpenAsync();
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return reader["noidung"]?.ToString();
                }
            }
            catch { }
            return null;
        }

        private static void CleanTenDvColumn(DataTable dt)
        {
            foreach (DataRow row in dt.Rows)
            {
                if (row["TEN_DV"] != DBNull.Value)
                {
                    string tenDV = row["TEN_DV"].ToString() ?? "";
                    tenDV = tenDV.Replace("Trung tâm Viễn thông", "")
                                 .Replace("VNPT", "")
                                 .Replace("Phòng Bán hàng", "")
                                 .Trim();
                    row["TEN_DV"] = tenDV;
                }
            }
        }

        private static List<string> DetermineHeaderColumns(DataTable dt, string loaiDv)
        {
            if (string.Equals(loaiDv, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                return new List<string> { "Cá nhân", "Doanh nghiệp" };
            }

            if (string.Equals(loaiDv, "NVDB", StringComparison.OrdinalIgnoreCase))
            {
                return dt.AsEnumerable()
                         .Where(r => r["TEN_NV"] != DBNull.Value)
                         .Select(r => r["TEN_NV"]?.ToString() ?? "")
                         .Distinct()
                         .OrderBy(x => x)
                         .ToList();
            }

            return dt.AsEnumerable()
                     .Where(r => r["TEN_DV"] != DBNull.Value)
                     .Select(r => r["TEN_DV"]?.ToString() ?? "")
                     .Distinct()
                     .OrderBy(x => x)
                     .ToList();
        }

        private static DataRow? FindMatchedRow(IGrouping<dynamic, DataRow> g, string col, string loaiDv)
        {
            if (string.Equals(loaiDv, "ALL", StringComparison.OrdinalIgnoreCase))
                return g.FirstOrDefault(x => (x["MA_DV"]?.ToString() ?? "") == (col == "Cá nhân" ? "KHCN" : "KHDN"));
            if (string.Equals(loaiDv, "NVDB", StringComparison.OrdinalIgnoreCase))
                return g.FirstOrDefault(x => (x["TEN_NV"]?.ToString() ?? "") == col);
            return g.FirstOrDefault(x => (x["TEN_DV"]?.ToString() ?? "") == col);
        }
    }
}