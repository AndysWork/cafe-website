using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Repositories;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;
using OfficeOpenXml;

namespace Cafe.Api.Functions;

public class GstReportFunction
{
    private readonly IOrderRepository _mongo;
    private readonly IFinanceRepository _finance;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public GstReportFunction(IOrderRepository mongo, IFinanceRepository finance, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _finance = finance;
        _auth = auth;
        _log = loggerFactory.CreateLogger<GstReportFunction>();
    }

    [Function("GetGstSummary")]
    public async Task<HttpResponseData> GetGstSummary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reports/gst/summary")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var (startDate, endDate, month, year) = ResolveDateRange(req);

            var orders = await _mongo.GetAllOrdersAsync(outletId);
            var monthOrders = orders.Where(o => o.CreatedAt >= startDate && o.CreatedAt <= endDate && o.Status != "cancelled").ToList();

            var marketplaceSales = await GetMarketplaceSalesAsync(startDate, endDate, outletId);

            var offlineTaxableValue = monthOrders.Sum(o => o.Subtotal);
            var offlineTax = monthOrders.Sum(o => o.Tax);
            var offlineInvoiceValue = monthOrders.Sum(o => o.Total);

            var onlineTaxableValue = marketplaceSales.Sum(s => s.BillSubTotal);
            var onlineTax = marketplaceSales.Sum(s => s.GST);
            var onlineInvoiceValue = marketplaceSales.Sum(s => s.BillSubTotal + s.GST);

            var totalTaxableValue = offlineTaxableValue + onlineTaxableValue;
            var totalTax = offlineTax + onlineTax;
            var totalCgst = totalTax / 2;
            var totalSgst = totalTax / 2;
            var totalInvoiceValue = offlineInvoiceValue + onlineInvoiceValue;

            var gstSummary = new
            {
                period = $"{startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}",
                month,
                year,
                totalOrders = monthOrders.Count + marketplaceSales.Count,
                offlineOrders = monthOrders.Count,
                onlineOrders = marketplaceSales.Count,
                totalSales = Math.Round(totalTaxableValue, 2),
                totalTaxableAmount = Math.Round(totalTaxableValue, 2),
                totalTaxableValue = Math.Round(totalTaxableValue, 2),
                cgst = Math.Round(totalCgst, 2),
                sgst = Math.Round(totalSgst, 2),
                igst = 0m,
                totalGst = Math.Round(totalTax, 2),
                totalWithGst = Math.Round(totalInvoiceValue, 2),
                totalInvoiceValue = Math.Round(totalInvoiceValue, 2),
                hsnSummary = new[]
                {
                    new { hsnCode = "9963", description = "Food & Beverage Services", taxableValue = Math.Round(totalTaxableValue, 2), cgstRate = 2.5m, sgstRate = 2.5m, cgstAmount = Math.Round(totalCgst, 2), sgstAmount = Math.Round(totalSgst, 2) }
                },
                breakdown = new
                {
                    offline = new { taxableValue = Math.Round(offlineTaxableValue, 2), gst = Math.Round(offlineTax, 2), invoiceValue = Math.Round(offlineInvoiceValue, 2) },
                    online = new { taxableValue = Math.Round(onlineTaxableValue, 2), gst = Math.Round(onlineTax, 2), invoiceValue = Math.Round(onlineInvoiceValue, 2) }
                }
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(gstSummary);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error generating GST summary");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while generating the GST summary" });
            return res;
        }
    }

    [Function("ExportGstr1")]
    public async Task<HttpResponseData> ExportGstr1(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reports/gst/gstr1/export")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var (startDate, endDate, month, year) = ResolveDateRange(req);

            var orders = await _mongo.GetAllOrdersAsync(outletId);
            var monthOrders = orders.Where(o => o.CreatedAt >= startDate && o.CreatedAt <= endDate && o.Status != "cancelled").ToList();
            var marketplaceSales = await GetMarketplaceSalesAsync(startDate, endDate, outletId);

            using var package = new ExcelPackage();

            // B2B Sheet (not applicable for most small cafes, but included for completeness)
            var b2bWs = package.Workbook.Worksheets.Add("B2B Invoices");
            b2bWs.Cells[1, 1].Value = "No B2B invoices — all sales are B2C (consumer)";

            // B2C Large Sheet
            var b2cLargeWs = package.Workbook.Worksheets.Add("B2C Large");
            b2cLargeWs.Cells[1, 1].Value = "Place of Supply";
            b2cLargeWs.Cells[1, 2].Value = "Rate";
            b2cLargeWs.Cells[1, 3].Value = "Applicable % of Tax Rate";
            b2cLargeWs.Cells[1, 4].Value = "Taxable Value";
            b2cLargeWs.Cells[1, 5].Value = "CGST Amount";
            b2cLargeWs.Cells[1, 6].Value = "SGST Amount";
            b2cLargeWs.Cells[1, 7].Value = "Cess Amount";
            StyleHeader(b2cLargeWs, 1, 7);

            var largeOrders = monthOrders.Where(o => o.Total >= 250000).ToList();
            var largeOnlineSales = marketplaceSales.Where(s => (s.BillSubTotal + s.GST) >= 250000).ToList();
            int row = 2;
            if (largeOrders.Count > 0 || largeOnlineSales.Count > 0)
            {
                var taxableValue = largeOrders.Sum(o => o.Subtotal) + largeOnlineSales.Sum(s => s.BillSubTotal);
                var largeTax = largeOrders.Sum(o => o.Tax) + largeOnlineSales.Sum(s => s.GST);
                b2cLargeWs.Cells[row, 1].Value = "West Bengal";
                b2cLargeWs.Cells[row, 2].Value = 5;
                b2cLargeWs.Cells[row, 3].Value = 100;
                b2cLargeWs.Cells[row, 4].Value = (double)taxableValue;
                b2cLargeWs.Cells[row, 5].Value = (double)(largeTax / 2);
                b2cLargeWs.Cells[row, 6].Value = (double)(largeTax / 2);
                b2cLargeWs.Cells[row, 7].Value = 0;
            }

            // B2C Small Sheet (most sales for cafes)
            var b2cSmallWs = package.Workbook.Worksheets.Add("B2C Small");
            b2cSmallWs.Cells[1, 1].Value = "Type";
            b2cSmallWs.Cells[1, 2].Value = "Place of Supply";
            b2cSmallWs.Cells[1, 3].Value = "Rate";
            b2cSmallWs.Cells[1, 4].Value = "Applicable % of Tax Rate";
            b2cSmallWs.Cells[1, 5].Value = "Taxable Value";
            b2cSmallWs.Cells[1, 6].Value = "CGST Amount";
            b2cSmallWs.Cells[1, 7].Value = "SGST Amount";
            b2cSmallWs.Cells[1, 8].Value = "Cess Amount";
            StyleHeader(b2cSmallWs, 1, 8);

            var smallOrders = monthOrders.Where(o => o.Total < 250000).ToList();
            var smallOnlineSales = marketplaceSales.Where(s => (s.BillSubTotal + s.GST) < 250000).ToList();
            var smallTaxable = smallOrders.Sum(o => o.Subtotal) + smallOnlineSales.Sum(s => s.BillSubTotal);
            var smallTax = smallOrders.Sum(o => o.Tax) + smallOnlineSales.Sum(s => s.GST);
            b2cSmallWs.Cells[2, 1].Value = "OE";
            b2cSmallWs.Cells[2, 2].Value = "West Bengal";
            b2cSmallWs.Cells[2, 3].Value = 5;
            b2cSmallWs.Cells[2, 4].Value = 100;
            b2cSmallWs.Cells[2, 5].Value = (double)smallTaxable;
            b2cSmallWs.Cells[2, 6].Value = (double)(smallTax / 2);
            b2cSmallWs.Cells[2, 7].Value = (double)(smallTax / 2);
            b2cSmallWs.Cells[2, 8].Value = 0;

            // HSN Summary
            var hsnWs = package.Workbook.Worksheets.Add("HSN Summary");
            hsnWs.Cells[1, 1].Value = "HSN Code";
            hsnWs.Cells[1, 2].Value = "Description";
            hsnWs.Cells[1, 3].Value = "UQC";
            hsnWs.Cells[1, 4].Value = "Total Quantity";
            hsnWs.Cells[1, 5].Value = "Taxable Value";
            hsnWs.Cells[1, 6].Value = "CGST Amount";
            hsnWs.Cells[1, 7].Value = "SGST Amount";
            hsnWs.Cells[1, 8].Value = "Total Tax";
            StyleHeader(hsnWs, 1, 8);

            var totalTaxable = monthOrders.Sum(o => o.Subtotal) + marketplaceSales.Sum(s => s.BillSubTotal);
            var totalTax = monthOrders.Sum(o => o.Tax) + marketplaceSales.Sum(s => s.GST);
            var totalItems = monthOrders.Sum(o => o.Items.Sum(i => i.Quantity)) + marketplaceSales.Sum(s => s.OrderedItems.Sum(i => i.Quantity));
            hsnWs.Cells[2, 1].Value = "9963";
            hsnWs.Cells[2, 2].Value = "Food & Beverage Services";
            hsnWs.Cells[2, 3].Value = "NOS";
            hsnWs.Cells[2, 4].Value = totalItems;
            hsnWs.Cells[2, 5].Value = (double)totalTaxable;
            hsnWs.Cells[2, 6].Value = (double)(totalTax / 2);
            hsnWs.Cells[2, 7].Value = (double)(totalTax / 2);
            hsnWs.Cells[2, 8].Value = (double)totalTax;

            foreach (var ws in package.Workbook.Worksheets)
                ws.Cells.AutoFitColumns();

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            response.Headers.Add("Content-Disposition", $"attachment; filename=GSTR1-{year}-{month:D2}.xlsx");
            await response.Body.WriteAsync(package.GetAsByteArray());
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error exporting GSTR-1");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while exporting the GSTR-1 report" });
            return res;
        }
    }

    private static void StyleHeader(ExcelWorksheet ws, int row, int colCount)
    {
        using var range = ws.Cells[row, 1, row, colCount];
        range.Style.Font.Bold = true;
        range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
        range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(14, 165, 233));
        range.Style.Font.Color.SetColor(System.Drawing.Color.White);
    }

    private async Task<List<OnlineSale>> GetMarketplaceSalesAsync(DateTime startDate, DateTime endDate, string? outletId)
    {
        var sales = await _finance.GetOnlineSalesByDateRangeAsync(null, startDate, endDate, outletId, includeWebSales: false);
        return sales
            .Where(s => string.Equals(s.Platform, "Swiggy", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(s.Platform, "Zomato", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static (DateTime startDate, DateTime endDate, int month, int year) ResolveDateRange(HttpRequestData req)
    {
        var startDateStr = req.Query["startDate"];
        var endDateStr = req.Query["endDate"];

        if (DateTime.TryParse(startDateStr, out var parsedStart) && DateTime.TryParse(endDateStr, out var parsedEnd))
        {
            var startDate = parsedStart.Date;
            var endDate = parsedEnd.Date.AddDays(1).AddTicks(-1);
            return (startDate, endDate, startDate.Month, startDate.Year);
        }

        var monthStr = req.Query["month"];
        var yearStr = req.Query["year"];

        int month = int.TryParse(monthStr, out var m) ? m : MongoService.GetIstNow().Month;
        int year = int.TryParse(yearStr, out var y) ? y : MongoService.GetIstNow().Year;

        var monthStartDate = new DateTime(year, month, 1);
        var monthEndDate = monthStartDate.AddMonths(1).AddSeconds(-1);
        return (monthStartDate, monthEndDate, month, year);
    }
}
