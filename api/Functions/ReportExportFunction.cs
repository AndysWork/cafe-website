using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;
using OfficeOpenXml;

namespace Cafe.Api.Functions;

public class ReportExportFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public ReportExportFunction(MongoService mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<ReportExportFunction>();
    }

    [Function("ExportSalesReport")]
    public async Task<HttpResponseData> ExportSalesReport(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reports/sales/export")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var format = req.Query["format"] ?? "excel";
            var startDateStr = req.Query["startDate"];
            var endDateStr = req.Query["endDate"];

            DateTime startDate = DateTime.TryParse(startDateStr, out var sd) ? sd : MongoService.GetIstNow().AddDays(-30);
            DateTime endDate = DateTime.TryParse(endDateStr, out var ed) ? ed : MongoService.GetIstNow();

            var sales = await _mongo.GetSalesByDateRangeAsync(startDate, endDate, outletId);

            if (format == "csv")
            {
                var csv = "Date,Item Name,Payment Method,Amount,Notes\n";
                foreach (var s in sales)
                {
                    foreach (var item in s.Items)
                    {
                        csv += $"{s.Date:yyyy-MM-dd},{EscapeCsv(item.ItemName)},{s.PaymentMethod},{item.TotalPrice:F2},{EscapeCsv(s.Notes ?? "")}\n";
                    }
                }

                var csvResponse = req.CreateResponse(HttpStatusCode.OK);
                csvResponse.Headers.Add("Content-Type", "text/csv");
                csvResponse.Headers.Add("Content-Disposition", $"attachment; filename=sales-report-{startDate:yyyyMMdd}-{endDate:yyyyMMdd}.csv");
                await csvResponse.WriteStringAsync(csv);
                return csvResponse;
            }

            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Sales Report");
            ws.Cells[1, 1].Value = "Date";
            ws.Cells[1, 2].Value = "Payment Method";
            ws.Cells[1, 3].Value = "Item Name";
            ws.Cells[1, 4].Value = "Quantity";
            ws.Cells[1, 5].Value = "Amount (₹)";
            ws.Cells[1, 6].Value = "Notes";

            using (var range = ws.Cells[1, 1, 1, 6])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(14, 165, 233));
                range.Style.Font.Color.SetColor(System.Drawing.Color.White);
            }

            int row = 2;
            foreach (var s in sales)
            {
                foreach (var item in s.Items)
                {
                    ws.Cells[row, 1].Value = s.Date.ToString("yyyy-MM-dd");
                    ws.Cells[row, 2].Value = s.PaymentMethod;
                    ws.Cells[row, 3].Value = item.ItemName;
                    ws.Cells[row, 4].Value = item.Quantity;
                    ws.Cells[row, 5].Value = (double)item.TotalPrice;
                    ws.Cells[row, 6].Value = s.Notes ?? "";
                    row++;
                }
            }

            ws.Cells[row + 1, 4].Value = "Total:";
            ws.Cells[row + 1, 4].Style.Font.Bold = true;
            ws.Cells[row + 1, 5].Value = (double)sales.Sum(s => s.TotalAmount);
            ws.Cells[row + 1, 5].Style.Font.Bold = true;
            ws.Cells[row + 1, 5].Style.Numberformat.Format = "#,##0.00";

            ws.Cells.AutoFitColumns();

            var excelResponse = req.CreateResponse(HttpStatusCode.OK);
            excelResponse.Headers.Add("Content-Type", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            excelResponse.Headers.Add("Content-Disposition", $"attachment; filename=sales-report-{startDate:yyyyMMdd}-{endDate:yyyyMMdd}.xlsx");
            await excelResponse.Body.WriteAsync(package.GetAsByteArray());
            return excelResponse;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error exporting sales report");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while exporting the report" });
            return res;
        }
    }

    [Function("ExportExpenseReport")]
    public async Task<HttpResponseData> ExportExpenseReport(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reports/expenses/export")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var startDateStr = req.Query["startDate"];
            var endDateStr = req.Query["endDate"];

            DateTime startDate = DateTime.TryParse(startDateStr, out var sd) ? sd : MongoService.GetIstNow().AddDays(-30);
            DateTime endDate = DateTime.TryParse(endDateStr, out var ed) ? ed : MongoService.GetIstNow();

            var expenses = await _mongo.GetExpensesByDateRangeAsync(startDate, endDate, outletId);

            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Expense Report");
            ws.Cells[1, 1].Value = "Date";
            ws.Cells[1, 2].Value = "Type";
            ws.Cells[1, 3].Value = "Source";
            ws.Cells[1, 4].Value = "Payment Method";
            ws.Cells[1, 5].Value = "Amount (₹)";
            ws.Cells[1, 6].Value = "Notes";

            using (var range = ws.Cells[1, 1, 1, 6])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(14, 165, 233));
                range.Style.Font.Color.SetColor(System.Drawing.Color.White);
            }

            int row = 2;
            foreach (var e in expenses)
            {
                ws.Cells[row, 1].Value = e.Date.ToString("yyyy-MM-dd");
                ws.Cells[row, 2].Value = e.ExpenseType;
                ws.Cells[row, 3].Value = e.ExpenseSource;
                ws.Cells[row, 4].Value = e.PaymentMethod;
                ws.Cells[row, 5].Value = (double)e.Amount;
                ws.Cells[row, 6].Value = e.Notes ?? "";
                row++;
            }

            ws.Cells[row + 1, 4].Value = "Total:";
            ws.Cells[row + 1, 4].Style.Font.Bold = true;
            ws.Cells[row + 1, 5].Value = (double)expenses.Sum(e => e.Amount);
            ws.Cells[row + 1, 5].Style.Font.Bold = true;
            ws.Cells.AutoFitColumns();

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            response.Headers.Add("Content-Disposition", $"attachment; filename=expense-report-{startDate:yyyyMMdd}-{endDate:yyyyMMdd}.xlsx");
            await response.Body.WriteAsync(package.GetAsByteArray());
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error exporting expense report");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while exporting the report" });
            return res;
        }
    }

    [Function("ExportOrdersReport")]
    public async Task<HttpResponseData> ExportOrdersReport(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reports/orders/export")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var orders = await _mongo.GetAllOrdersAsync(outletId);

            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Orders Report");
            ws.Cells[1, 1].Value = "Order ID";
            ws.Cells[1, 2].Value = "Customer";
            ws.Cells[1, 3].Value = "Date";
            ws.Cells[1, 4].Value = "Status";
            ws.Cells[1, 5].Value = "Payment Method";
            ws.Cells[1, 6].Value = "Payment Status";
            ws.Cells[1, 7].Value = "Subtotal (₹)";
            ws.Cells[1, 8].Value = "Tax (₹)";
            ws.Cells[1, 9].Value = "Delivery Fee (₹)";
            ws.Cells[1, 10].Value = "Discount (₹)";
            ws.Cells[1, 11].Value = "Total (₹)";
            ws.Cells[1, 12].Value = "Items";

            using (var range = ws.Cells[1, 1, 1, 12])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(14, 165, 233));
                range.Style.Font.Color.SetColor(System.Drawing.Color.White);
            }

            int row = 2;
            foreach (var o in orders)
            {
                ws.Cells[row, 1].Value = o.Id?[^6..];
                ws.Cells[row, 2].Value = o.Username;
                ws.Cells[row, 3].Value = o.CreatedAt.ToString("yyyy-MM-dd HH:mm");
                ws.Cells[row, 4].Value = o.Status;
                ws.Cells[row, 5].Value = o.PaymentMethod;
                ws.Cells[row, 6].Value = o.PaymentStatus;
                ws.Cells[row, 7].Value = (double)o.Subtotal;
                ws.Cells[row, 8].Value = (double)o.Tax;
                ws.Cells[row, 9].Value = (double)o.DeliveryFee;
                ws.Cells[row, 10].Value = (double)(o.DiscountAmount + o.LoyaltyDiscountAmount);
                ws.Cells[row, 11].Value = (double)o.Total;
                ws.Cells[row, 12].Value = string.Join(", ", o.Items.Select(i => $"{i.Name} x{i.Quantity}"));
                row++;
            }

            ws.Cells.AutoFitColumns();

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            response.Headers.Add("Content-Disposition", $"attachment; filename=orders-report-{MongoService.GetIstNow():yyyyMMdd}.xlsx");
            await response.Body.WriteAsync(package.GetAsByteArray());
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error exporting orders report");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while exporting the report" });
            return res;
        }
    }

    [Function("ExportProfitLossReport")]
    public async Task<HttpResponseData> ExportProfitLossReport(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reports/profit-loss/export")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var startDateStr = req.Query["startDate"];
            var endDateStr = req.Query["endDate"];

            DateTime startDate = DateTime.TryParse(startDateStr, out var sd) ? sd : MongoService.GetIstNow().AddDays(-30);
            DateTime endDate = DateTime.TryParse(endDateStr, out var ed) ? ed : MongoService.GetIstNow();

            var sales = await _mongo.GetSalesByDateRangeAsync(startDate, endDate, outletId);
            var expenses = await _mongo.GetExpensesByDateRangeAsync(startDate, endDate, outletId);

            var totalRevenue = sales.Sum(s => s.TotalAmount);
            var totalExpenses = expenses.Sum(e => e.Amount);
            var profit = totalRevenue - totalExpenses;

            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("P&L Summary");

            ws.Cells[1, 1].Value = $"Profit & Loss Report ({startDate:dd-MMM-yyyy} to {endDate:dd-MMM-yyyy})";
            ws.Cells[1, 1, 1, 3].Merge = true;
            ws.Cells[1, 1].Style.Font.Bold = true;
            ws.Cells[1, 1].Style.Font.Size = 14;

            ws.Cells[3, 1].Value = "REVENUE";
            ws.Cells[3, 1].Style.Font.Bold = true;

            var revByType = sales.SelectMany(s => s.Items)
                .GroupBy(i => i.ItemName)
                .Select(g => new { Type = g.Key, Amount = g.Sum(i => i.TotalPrice) })
                .OrderByDescending(x => x.Amount);

            int row = 4;
            foreach (var r in revByType)
            {
                ws.Cells[row, 1].Value = r.Type;
                ws.Cells[row, 2].Value = (double)r.Amount;
                ws.Cells[row, 2].Style.Numberformat.Format = "#,##0.00";
                row++;
            }
            ws.Cells[row, 1].Value = "Total Revenue";
            ws.Cells[row, 1].Style.Font.Bold = true;
            ws.Cells[row, 2].Value = (double)totalRevenue;
            ws.Cells[row, 2].Style.Font.Bold = true;
            ws.Cells[row, 2].Style.Numberformat.Format = "#,##0.00";

            row += 2;
            ws.Cells[row, 1].Value = "EXPENSES";
            ws.Cells[row, 1].Style.Font.Bold = true;
            row++;

            var expByType = expenses.GroupBy(e => e.ExpenseType)
                .Select(g => new { Type = g.Key, Amount = g.Sum(e => e.Amount) })
                .OrderByDescending(x => x.Amount);

            foreach (var e in expByType)
            {
                ws.Cells[row, 1].Value = e.Type;
                ws.Cells[row, 2].Value = (double)e.Amount;
                ws.Cells[row, 2].Style.Numberformat.Format = "#,##0.00";
                row++;
            }
            ws.Cells[row, 1].Value = "Total Expenses";
            ws.Cells[row, 1].Style.Font.Bold = true;
            ws.Cells[row, 2].Value = (double)totalExpenses;
            ws.Cells[row, 2].Style.Font.Bold = true;

            row += 2;
            ws.Cells[row, 1].Value = "NET PROFIT / (LOSS)";
            ws.Cells[row, 1].Style.Font.Bold = true;
            ws.Cells[row, 1].Style.Font.Size = 12;
            ws.Cells[row, 2].Value = (double)profit;
            ws.Cells[row, 2].Style.Font.Bold = true;
            ws.Cells[row, 2].Style.Font.Size = 12;
            ws.Cells[row, 2].Style.Numberformat.Format = "#,##0.00";

            ws.Cells.AutoFitColumns();

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            response.Headers.Add("Content-Disposition", $"attachment; filename=profit-loss-{startDate:yyyyMMdd}-{endDate:yyyyMMdd}.xlsx");
            await response.Body.WriteAsync(package.GetAsByteArray());
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error exporting P&L report");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while exporting the report" });
            return res;
        }
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
