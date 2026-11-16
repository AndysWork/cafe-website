using CsvHelper;
using CsvHelper.Configuration;
using OfficeOpenXml;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Cafe.Api.Services;

public partial class ZomatoSalesPayoutInsightService
{
    public async Task<ZomatoSalesPayoutInsightResult> GenerateInsightsAsync(Stream orderHistoryCsvStream, Stream settlementWorkbookStream)
    {
        return (await ParseReportAsync(orderHistoryCsvStream, settlementWorkbookStream)).Insights;
    }

    public async Task<ParsedZomatoReport> ParseReportAsync(Stream orderHistoryCsvStream, Stream settlementWorkbookStream)
    {
        var orderRows = await ParseOrderHistoryAsync(orderHistoryCsvStream);
        var settlementData = ParseSettlementWorkbook(settlementWorkbookStream);
        var source = new ZomatoReportSourceData
        {
            Orders = orderRows,
            Settlements = settlementData.SettlementRows,
            AdditionalDeductions = settlementData.AdditionalDeductionRows,
            AdditionalTotals = settlementData.AdditionalDeductionTotals
        };
        return new ParsedZomatoReport(
            BuildInsights(source.Orders, source.Settlements, source.AdditionalDeductions, source.AdditionalTotals),
            source);
    }

    private async Task<List<OrderHistoryRow>> ParseOrderHistoryAsync(Stream csvStream)
    {
        csvStream.Position = 0;
        using var reader = new StreamReader(csvStream);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HeaderValidated = null,
            MissingFieldFound = null
        });

        var rows = new List<OrderHistoryRow>();
        await csv.ReadAsync();
        csv.ReadHeader();

        while (await csv.ReadAsync())
        {
            var orderId = GetCsvField(csv, "Order ID");
            if (string.IsNullOrWhiteSpace(orderId))
            {
                continue;
            }

            var kpt = ParseDuration(GetCsvField(csv, "KPT duration (minutes)"));
            var riderWait = ParseDuration(GetCsvField(csv, "Rider wait time (minutes)"));
            rows.Add(new OrderHistoryRow
            {
                OrderId = orderId.Trim(),
                PlacedAt = ParseDateTime(GetCsvField(csv, "Order Placed At")),
                Status = (GetCsvField(csv, "Order Status") ?? string.Empty).Trim(),
                DeliveryType = (GetCsvField(csv, "Delivery") ?? string.Empty).Trim(),
                DistanceKm = ParseDistance(GetCsvField(csv, "Distance")),
                ItemsInOrder = (GetCsvField(csv, "Items in order") ?? string.Empty).Trim(),
                DiscountConstruct = (GetCsvField(csv, "Discount construct") ?? string.Empty).Trim(),
                BillSubtotal = ParseDecimal(GetCsvField(csv, "Bill subtotal")),
                PackagingCharges = ParseDecimal(GetCsvField(csv, "Packaging charges")),
                RestaurantDiscountPromo = ParseDecimal(GetCsvField(csv, "Restaurant discount (Promo)")),
                RestaurantDiscountOthers = ParseDecimal(GetCsvField(csv, "Restaurant discount (Flat offs, Freebies & others)")),
                GoldDiscount = ParseDecimal(GetCsvField(csv, "Gold discount")),
                BrandPackDiscount = ParseDecimal(GetCsvField(csv, "Brand pack discount")),
                Total = ParseDecimal(GetCsvField(csv, "Total")),
                Rating = ParseNullableDecimal(GetCsvField(csv, "Rating")),
                Review = (GetCsvField(csv, "Review") ?? string.Empty).Trim(),
                KptMinutes = kpt.Minutes,
                KptDataStatus = kpt.Status,
                RiderWaitMinutes = riderWait.Minutes,
                RiderWaitDataStatus = riderWait.Status,
                OrderReadyMarked = (GetCsvField(csv, "Order Ready Marked") ?? string.Empty).Trim(),
                CustomerComplaintTag = (GetCsvField(csv, "Customer complaint tag") ?? string.Empty).Trim()
            });
        }

        return rows;
    }

    private static string? GetCsvField(CsvReader csv, string name)
    {
        return csv.TryGetField<string>(name, out var value) ? value : null;
    }

    private SettlementWorkbookData ParseSettlementWorkbook(Stream workbookStream)
    {
        workbookStream.Position = 0;
        using var package = new ExcelPackage(workbookStream);
        var worksheet = package.Workbook.Worksheets.FirstOrDefault(w =>
            string.Equals(w.Name, "Order Level", StringComparison.OrdinalIgnoreCase));

        if (worksheet == null || worksheet.Dimension == null)
        {
            return new SettlementWorkbookData();
        }

        var headerRow = FindHeaderRow(worksheet);
        if (headerRow == -1)
        {
            return new SettlementWorkbookData();
        }

        var headerMap = BuildHeaderMap(worksheet, headerRow);
        var rows = new List<SettlementRow>();

        for (var row = headerRow + 1; row <= worksheet.Dimension.Rows; row++)
        {
            var orderId = GetSettlementText(worksheet, row, headerMap, "orderid");
            if (string.IsNullOrWhiteSpace(orderId) || orderId.Contains("#REF", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            rows.Add(new SettlementRow
            {
                OrderId = orderId.Trim(),
                OrderDate = ParseDateTime(GetSettlementText(worksheet, row, headerMap, "orderdate")),
                DiscountConstruct = GetSettlementText(worksheet, row, headerMap, "discountconstruct") ?? string.Empty,
                OrderStatus = GetSettlementText(worksheet, row, headerMap, "orderstatus") ?? string.Empty,
                Subtotal = GetSettlementDecimal(worksheet, row, headerMap, "subtotal"),
                PackagingCharge = GetSettlementDecimal(worksheet, row, headerMap, "packagingcharge"),
                PromoDiscount = GetSettlementDecimal(worksheet, row, headerMap, "restaurantdiscountpromo"),
                OtherRestaurantDiscount = GetSettlementDecimal(worksheet, row, headerMap, "restaurantdiscountbogo"),
                NetOrderValue = GetSettlementDecimal(worksheet, row, headerMap, "netordervalue"),
                TotalCommissionableValue = GetSettlementDecimal(worksheet, row, headerMap, "totalcommissionablevalue"),
                ServiceFeeAndPmFee = GetSettlementDecimal(worksheet, row, headerMap, "servicefeepaymentmechanismfee"),
                TaxesOnServiceFee = GetSettlementDecimal(worksheet, row, headerMap, "taxesonservicefee"),
                GovernmentCharges = GetSettlementDecimal(worksheet, row, headerMap, "governmentcharges"),
                OtherOrderLevelDeductions = GetSettlementDecimal(worksheet, row, headerMap, "otherorderleveldeductions"),
                NetDeductions = GetSettlementDecimal(worksheet, row, headerMap, "netdeductions"),
                NetAdditions = GetSettlementDecimal(worksheet, row, headerMap, "netadditions"),
                OrderLevelPayout = GetSettlementDecimal(worksheet, row, headerMap, "orderlevelpayout"),
                SettlementStatus = GetSettlementText(worksheet, row, headerMap, "settlementstatus") ?? string.Empty,
                SettlementDate = ParseDateTime(GetSettlementText(worksheet, row, headerMap, "settlementdate")),
                BankUtr = GetSettlementText(worksheet, row, headerMap, "bankutr") ?? string.Empty,
                UnsettledAmount = GetSettlementDecimal(worksheet, row, headerMap, "unsettledamount"),
                ActualOrderDistanceKm = GetSettlementDecimal(worksheet, row, headerMap, "actualorderdistancekm"),
                Tds194OAmount = GetSettlementDecimal(worksheet, row, headerMap, "tds194oamount")
            });
        }

        if (rows.Count == 0)
        {
            rows = ParseSettlementRowsByFixedColumns(worksheet, Math.Max(headerRow + 1, 8));
        }

        var additionalData = ParseAdditionalDeductionsSheet(package);
        return new SettlementWorkbookData
        {
            SettlementRows = rows,
            AdditionalDeductionRows = additionalData.Rows,
            AdditionalDeductionTotals = additionalData.Totals
        };
    }

    private static List<SettlementRow> ParseSettlementRowsByFixedColumns(ExcelWorksheet worksheet, int startRow)
    {
        var rows = new List<SettlementRow>();
        for (var row = startRow; row <= worksheet.Dimension!.Rows; row++)
        {
            var orderId = worksheet.Cells[row, 2].Text?.Trim();
            if (string.IsNullOrWhiteSpace(orderId) || orderId.Contains("#REF", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            rows.Add(new SettlementRow
            {
                OrderId = orderId,
                OrderDate = ParseDateTime(worksheet.Cells[row, 3].Text),
                DiscountConstruct = worksheet.Cells[row, 7].Text?.Trim() ?? string.Empty,
                OrderStatus = worksheet.Cells[row, 9].Text?.Trim() ?? string.Empty,
                NetOrderValue = ParseDecimal(worksheet.Cells[row, 23].Value),
                ServiceFeeAndPmFee = ParseDecimal(worksheet.Cells[row, 35].Value),
                TaxesOnServiceFee = ParseDecimal(worksheet.Cells[row, 36].Value),
                GovernmentCharges = ParseDecimal(worksheet.Cells[row, 44].Value),
                OtherOrderLevelDeductions = ParseDecimal(worksheet.Cells[row, 53].Value),
                NetDeductions = ParseDecimal(worksheet.Cells[row, 54].Value),
                NetAdditions = ParseDecimal(worksheet.Cells[row, 55].Value),
                OrderLevelPayout = ParseDecimal(worksheet.Cells[row, 56].Value),
                SettlementStatus = worksheet.Cells[row, 57].Text?.Trim() ?? string.Empty,
                SettlementDate = ParseDateTime(worksheet.Cells[row, 58].Text),
                BankUtr = worksheet.Cells[row, 59].Text?.Trim() ?? string.Empty,
                UnsettledAmount = ParseDecimal(worksheet.Cells[row, 60].Value)
            });
        }

        return rows;
    }

    private static int FindHeaderRow(ExcelWorksheet worksheet)
    {
        var maxProbeRow = Math.Min(20, worksheet.Dimension!.Rows);
        for (var row = 1; row <= maxProbeRow; row++)
        {
            var first = NormalizeHeader(worksheet.Cells[row, 1].Text);
            var second = NormalizeHeader(worksheet.Cells[row, 2].Text);
            if (first == "sno" && second == "orderid")
            {
                return row;
            }
        }

        return -1;
    }

    private static Dictionary<string, int> BuildHeaderMap(ExcelWorksheet worksheet, int headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var col = 1; col <= worksheet.Dimension!.Columns; col++)
        {
            var normalized = NormalizeHeader(worksheet.Cells[headerRow, col].Text);
            if (string.IsNullOrEmpty(normalized))
            {
                continue;
            }

            map[normalized] = col;
        }

        return map;
    }

    private static string? GetSettlementText(ExcelWorksheet ws, int row, Dictionary<string, int> map, string field)
    {
        var col = ResolveSettlementColumn(map, field);
        return col == null ? null : ws.Cells[row, col.Value].Text?.Trim();
    }

    private static decimal GetSettlementDecimal(ExcelWorksheet ws, int row, Dictionary<string, int> map, string field)
    {
        var col = ResolveSettlementColumn(map, field);
        if (col == null)
        {
            return 0m;
        }

        var value = ws.Cells[row, col.Value].Value;
        return ParseDecimal(value);
    }

    private static int? ResolveSettlementColumn(Dictionary<string, int> map, string field)
    {
        foreach (var entry in map)
        {
            var key = entry.Key;
            if (field switch
            {
                "orderid" => key == "orderid",
                "orderdate" => key == "orderdate",
                "discountconstruct" => key == "discountconstruct",
                "orderstatus" => key.StartsWith("orderstatus"),
                "subtotal" => key.StartsWith("subtotalitemstotal"),
                "packagingcharge" => key == "packagingcharge",
                "restaurantdiscountpromo" => key == "restaurantdiscountpromo",
                "restaurantdiscountbogo" => key.StartsWith("restaurantdiscountbogofreebiesgoldbrandpackothers"),
                "netordervalue" => key.StartsWith("netordervalue"),
                "totalcommissionablevalue" => key.StartsWith("totalcommissionablevalue"),
                "servicefeepaymentmechanismfee" => key.StartsWith("servicefeepaymentmechanismfee"),
                "taxesonservicefee" => key.StartsWith("taxesonservicefeepaymentmechanismfee"),
                "governmentcharges" => key.StartsWith("governmentcharges"),
                "otherorderleveldeductions" => key.StartsWith("otherorderleveldeductions"),
                "netdeductions" => key.StartsWith("netdeductions"),
                "netadditions" => key.StartsWith("netadditions"),
                "orderlevelpayout" => key.StartsWith("orderlevelpayout"),
                "settlementstatus" => key == "settlementstatus",
                "settlementdate" => key == "settlementdate",
                "bankutr" => key == "bankutr",
                "unsettledamount" => key == "unsettledamount",
                "actualorderdistancekm" => key.StartsWith("actualorderdistancekm"),
                "tds194oamount" => key.StartsWith("tds194oamount"),
                _ => false
            })
            {
                return entry.Value;
            }
        }

        return null;
    }

    private ZomatoSalesPayoutInsightResult BuildInsights(
        List<OrderHistoryRow> orderRows,
        List<SettlementRow> settlementRows,
        List<AdditionalDeductionRow> additionalDeductionRows,
        List<AdditionalDeductionTotal> additionalDeductionTotals)
    {
        var deliveredOrders = orderRows.Where(r => string.Equals(r.Status, "Delivered", StringComparison.OrdinalIgnoreCase)).ToList();
        var csvOrderIds = orderRows.Select(r => r.OrderId).Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var deliveredCsvOrderIds = deliveredOrders.Select(r => r.OrderId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var settlementOrderIds = settlementRows.Select(r => r.OrderId).Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var matchedOrderIds = deliveredCsvOrderIds.Intersect(settlementOrderIds, StringComparer.OrdinalIgnoreCase).ToList();
        var missingInSettlement = deliveredCsvOrderIds.Except(settlementOrderIds, StringComparer.OrdinalIgnoreCase).ToList();
        var missingInOrderHistory = settlementOrderIds.Except(csvOrderIds, StringComparer.OrdinalIgnoreCase).ToList();

        var csvGrossSales = deliveredOrders.Sum(r => r.Total);
        var csvTotalDiscount = deliveredOrders.Sum(r => r.RestaurantDiscountPromo + r.RestaurantDiscountOthers + r.GoldDiscount + r.BrandPackDiscount);
        var settlementPayout = settlementRows.Sum(r => r.OrderLevelPayout);
        var settledPayout = settlementRows
            .Where(r => string.Equals(r.SettlementStatus, "settled", StringComparison.OrdinalIgnoreCase))
            .Sum(r => r.OrderLevelPayout);
        // Use only grand totals; section subtotals (e.g. "Total Ads & ...") repeat the same amounts.
        var grandDeductionTotal = additionalDeductionTotals.FirstOrDefault(t =>
            t.Label.StartsWith("Total Deductions", StringComparison.OrdinalIgnoreCase));
        var grandAdditionTotal = additionalDeductionTotals.FirstOrDefault(t =>
            t.Label.StartsWith("Total Additions", StringComparison.OrdinalIgnoreCase));
        var additionalDeductionsAdjusted = grandDeductionTotal?.AdjustedAmount
            ?? additionalDeductionRows.Where(r => !r.IsAddition).Sum(r => r.AdjustedAmount);
        var additionalAdditionsAdjusted = grandAdditionTotal?.AdjustedAmount
            ?? additionalDeductionRows.Where(r => r.IsAddition).Sum(r => r.AdjustedAmount);
        var actualPayoutCredited = settledPayout + additionalAdditionsAdjusted - additionalDeductionsAdjusted;
        var peakHour = deliveredOrders
            .Where(o => o.PlacedAt.HasValue)
            .GroupBy(o => o.PlacedAt!.Value.Hour)
            .Select(g => new { Hour = g.Key, Orders = g.Count() })
            .OrderByDescending(g => g.Orders)
            .ThenBy(g => g.Hour)
            .FirstOrDefault();

        var dailySales = deliveredOrders
            .Where(r => r.PlacedAt.HasValue)
            .GroupBy(r => DateOnly.FromDateTime(r.PlacedAt!.Value))
            .Select(g => new DailyInsight
            {
                Date = g.Key.ToString("yyyy-MM-dd"),
                Orders = g.Count(),
                GrossSales = Round2(g.Sum(x => x.Total)),
                Discounts = Round2(g.Sum(x => x.RestaurantDiscountPromo + x.RestaurantDiscountOthers + x.GoldDiscount + x.BrandPackDiscount))
            })
            .OrderBy(x => x.Date)
            .ToList();
        var topSalesDay = dailySales
            .OrderByDescending(x => x.GrossSales)
            .ThenByDescending(x => x.Orders)
            .FirstOrDefault();

        var dailySettlement = settlementRows
            .Where(r => r.OrderDate.HasValue)
            .GroupBy(r => DateOnly.FromDateTime(r.OrderDate!.Value))
            .Select(g => new DailySettlementInsight
            {
                Date = g.Key.ToString("yyyy-MM-dd"),
                Orders = g.Count(),
                NetOrderValue = Round2(g.Sum(x => x.NetOrderValue)),
                Payout = Round2(g.Sum(x => x.OrderLevelPayout)),
                NetDeductions = Round2(g.Sum(x => x.NetDeductions))
            })
            .OrderBy(x => x.Date)
            .ToList();
        var topPayoutDay = dailySettlement
            .OrderByDescending(x => x.Payout)
            .ThenByDescending(x => x.Orders)
            .FirstOrDefault();

        var topDiscountConstructs = deliveredOrders
            .GroupBy(r => string.IsNullOrWhiteSpace(r.DiscountConstruct) ? "NA" : r.DiscountConstruct.Trim())
            .Select(g => new DiscountConstructInsight
            {
                DiscountConstruct = g.Key,
                Orders = g.Count(),
                GrossSales = Round2(g.Sum(x => x.Total)),
                DiscountGiven = Round2(g.Sum(x => x.RestaurantDiscountPromo + x.RestaurantDiscountOthers + x.GoldDiscount + x.BrandPackDiscount))
            })
            .OrderByDescending(x => x.Orders)
            .ThenByDescending(x => x.DiscountGiven)
            .Take(10)
            .ToList();
        var detailedDiscountInsights = deliveredOrders
            .GroupBy(r => string.IsNullOrWhiteSpace(r.DiscountConstruct) ? "NA" : r.DiscountConstruct.Trim())
            .Select(g =>
            {
                var gross = g.Sum(x => x.Total);
                var discount = g.Sum(x => x.RestaurantDiscountPromo + x.RestaurantDiscountOthers + x.GoldDiscount + x.BrandPackDiscount);
                return new DetailedDiscountInsight
                {
                    DiscountConstruct = g.Key,
                    Orders = g.Count(),
                    GrossSales = Round2(gross),
                    DiscountGiven = Round2(discount),
                    AverageDiscountPerOrder = g.Count() > 0 ? Round2(discount / g.Count()) : 0m,
                    EffectiveDiscountPercent = gross > 0 ? Round2((discount / gross) * 100m) : 0m
                };
            })
            .OrderByDescending(x => x.Orders)
            .ThenByDescending(x => x.DiscountGiven)
            .ToList();

        var topLossOrders = settlementRows
            .OrderByDescending(r => r.NetDeductions)
            .Take(10)
            .Select(r => new LossOrderInsight
            {
                OrderId = r.OrderId,
                OrderDate = r.OrderDate?.ToString("yyyy-MM-dd HH:mm:ss"),
                NetOrderValue = Round2(r.NetOrderValue),
                NetDeductions = Round2(r.NetDeductions),
                Payout = Round2(r.OrderLevelPayout),
                DeductionRatePercent = r.NetOrderValue > 0 ? Round2((r.NetDeductions / r.NetOrderValue) * 100m) : 0m
            })
            .ToList();
        var itemSalesInsights = BuildItemSalesInsights(deliveredOrders);
        var hourlyOrderInsights = deliveredOrders
            .Where(o => o.PlacedAt.HasValue)
            .GroupBy(o => o.PlacedAt!.Value.Hour)
            .Select(g => new HourlyOrderInsight
            {
                HourLabel = $"{g.Key:00}:00-{((g.Key + 1) % 24):00}:00",
                Orders = g.Count(),
                GrossSales = Round2(g.Sum(x => x.Total)),
                AverageOrderValue = g.Count() > 0 ? Round2(g.Sum(x => x.Total) / g.Count()) : 0m
            })
            .OrderByDescending(h => h.Orders)
            .ThenByDescending(h => h.GrossSales)
            .ToList();

        var kptValues = deliveredOrders.Where(o => o.KptMinutes >= 0).Select(o => o.KptMinutes!.Value).ToList();
        var rwtValues = deliveredOrders.Where(o => o.RiderWaitMinutes >= 0).Select(o => o.RiderWaitMinutes!.Value).ToList();
        var ratings = deliveredOrders.Where(o => o.Rating.HasValue).Select(o => o.Rating!.Value).ToList();

        return new ZomatoSalesPayoutInsightResult
        {
            Period = new InsightPeriod
            {
                StartDate = GetStartDate(orderRows, settlementRows),
                EndDate = GetEndDate(orderRows, settlementRows)
            },
            OrderHistorySummary = new OrderHistorySummary
            {
                TotalOrders = orderRows.Count,
                DeliveredOrders = deliveredOrders.Count,
                CancelledOrRejectedOrders = orderRows.Count - deliveredOrders.Count,
                GrossSales = Round2(csvGrossSales),
                AverageOrderValue = deliveredOrders.Count > 0 ? Round2(csvGrossSales / deliveredOrders.Count) : 0m,
                TotalDiscount = Round2(csvTotalDiscount),
                PromoDiscount = Round2(deliveredOrders.Sum(r => r.RestaurantDiscountPromo)),
                OtherRestaurantDiscount = Round2(deliveredOrders.Sum(r => r.RestaurantDiscountOthers)),
                GoldDiscount = Round2(deliveredOrders.Sum(r => r.GoldDiscount)),
                BrandPackDiscount = Round2(deliveredOrders.Sum(r => r.BrandPackDiscount)),
                PackagingCharges = Round2(deliveredOrders.Sum(r => r.PackagingCharges)),
                AverageDistanceKm = deliveredOrders.Count > 0 ? Round2(deliveredOrders.Average(r => r.DistanceKm)) : 0m,
                AverageKptMinutes = kptValues.Count > 0 ? Round2(kptValues.Average()) : 0m,
                AverageRiderWaitMinutes = rwtValues.Count > 0 ? Round2(rwtValues.Average()) : 0m,
                RatedOrders = ratings.Count,
                AverageRating = ratings.Count > 0 ? Round2(ratings.Average()) : 0m,
                PeakOrderHour = peakHour == null ? null : $"{peakHour.Hour:00}:00-{((peakHour.Hour + 1) % 24):00}:00",
                PeakOrderHourOrders = peakHour?.Orders ?? 0,
                TopSalesDay = topSalesDay?.Date,
                TopSalesDayGrossSales = topSalesDay?.GrossSales ?? 0m,
                OrderMarkingIssues = deliveredOrders.Count(r =>
                    string.Equals(r.OrderReadyMarked, "Missed", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(r.OrderReadyMarked, "Incorrectly", StringComparison.OrdinalIgnoreCase)),
                CustomerComplaintCount = deliveredOrders.Count(r => !string.IsNullOrWhiteSpace(r.CustomerComplaintTag))
            },
            SettlementSummary = new SettlementSummary
            {
                TotalOrders = settlementRows.Count,
                SettledOrders = settlementRows.Count(r => string.Equals(r.SettlementStatus, "settled", StringComparison.OrdinalIgnoreCase)),
                UnsettledOrders = settlementRows.Count(r => !string.Equals(r.SettlementStatus, "settled", StringComparison.OrdinalIgnoreCase)),
                NetOrderValue = Round2(settlementRows.Sum(r => r.NetOrderValue)),
                TotalPayout = Round2(settlementPayout),
                SettledPayout = Round2(settledPayout),
                NetDeductions = Round2(settlementRows.Sum(r => r.NetDeductions)),
                NetAdditions = Round2(settlementRows.Sum(r => r.NetAdditions)),
                ServiceFeeAndPmFee = Round2(settlementRows.Sum(r => r.ServiceFeeAndPmFee)),
                TaxesOnServiceFee = Round2(settlementRows.Sum(r => r.TaxesOnServiceFee)),
                GovernmentCharges = Round2(settlementRows.Sum(r => r.GovernmentCharges)),
                OtherOrderLevelDeductions = Round2(settlementRows.Sum(r => r.OtherOrderLevelDeductions)),
                TotalUnsettledAmount = Round2(settlementRows.Sum(r => r.UnsettledAmount)),
                Tds194OTotal = Round2(settlementRows.Sum(r => r.Tds194OAmount)),
                AdditionalDeductionsAdjusted = Round2(additionalDeductionsAdjusted),
                AdditionalAdditionsAdjusted = Round2(additionalAdditionsAdjusted),
                ActualPayoutCredited = Round2(actualPayoutCredited),
                AveragePayoutPerOrder = settlementRows.Count > 0 ? Round2(settlementPayout / settlementRows.Count) : 0m,
                TopPayoutDay = topPayoutDay?.Date,
                TopPayoutDayAmount = topPayoutDay?.Payout ?? 0m
            },
            Reconciliation = new ReconciliationSummary
            {
                MatchedDeliveredOrders = matchedOrderIds.Count,
                MissingInSettlementCount = missingInSettlement.Count,
                MissingInOrderHistoryCount = missingInOrderHistory.Count,
                MissingInSettlementOrderIds = missingInSettlement.Take(20).ToList(),
                MissingInOrderHistoryOrderIds = missingInOrderHistory.Take(20).ToList(),
                CsvGrossSales = Round2(csvGrossSales),
                SettlementPayout = Round2(settlementPayout),
                GrossToPayoutDifference = Round2(csvGrossSales - settlementPayout),
                PayoutToGrossPercent = csvGrossSales > 0 ? Round2((settlementPayout / csvGrossSales) * 100m) : 0m,
                DeliveredOrderCoveragePercent = deliveredCsvOrderIds.Count > 0 ? Round2((decimal)matchedOrderIds.Count / deliveredCsvOrderIds.Count * 100m) : 0m
            },
            DailySales = dailySales,
            DailySettlement = dailySettlement,
            HourlyOrderInsights = hourlyOrderInsights,
            ItemSalesInsights = itemSalesInsights,
            TopDiscountConstructs = topDiscountConstructs,
            DetailedDiscountInsights = detailedDiscountInsights,
            AdditionalDeductionDetails = additionalDeductionRows,
            AdditionalDeductionTotals = additionalDeductionTotals,
            TopLossOrders = topLossOrders,
            ReviewRatingInsights = BuildReviewRatingInsights(orderRows),
            KitchenOrders = BuildKitchenOrders(deliveredOrders)
        };
    }

    private static ReviewRatingInsights BuildReviewRatingInsights(List<OrderHistoryRow> orderRows)
    {
        var rated = orderRows.Where(o => o.Rating.HasValue).ToList();
        var ratingValues = rated.Select(o => o.Rating!.Value).ToList();
        var deliveredCount = orderRows.Count(o => string.Equals(o.Status, "Delivered", StringComparison.OrdinalIgnoreCase));

        var distribution = Enumerable.Range(1, 5)
            .Select(star =>
            {
                var count = ratingValues.Count(v => (int)Math.Round(v, MidpointRounding.AwayFromZero) == star);
                return new RatingDistributionInsight
                {
                    Stars = star,
                    Count = count,
                    Percent = ratingValues.Count > 0 ? Round2((decimal)count / ratingValues.Count * 100m) : 0m
                };
            })
            .OrderByDescending(d => d.Stars)
            .ToList();

        var feedbackOrders = orderRows
            .Where(o => o.Rating.HasValue || !string.IsNullOrWhiteSpace(o.Review) || !string.IsNullOrWhiteSpace(o.CustomerComplaintTag))
            .OrderBy(o => o.Rating ?? 6m)
            .ThenByDescending(o => o.PlacedAt)
            .Select(o => new OrderFeedbackInsight
            {
                OrderId = o.OrderId,
                OrderDate = o.PlacedAt?.ToString("yyyy-MM-dd HH:mm"),
                Rating = o.Rating,
                Review = o.Review,
                ComplaintTag = o.CustomerComplaintTag,
                Items = o.ItemsInOrder,
                OrderValue = Round2(o.Total),
                KptMinutes = o.KptMinutes,
                OrderReadyMarked = o.OrderReadyMarked
            })
            .ToList();

        var complaintTags = orderRows
            .Where(o => !string.IsNullOrWhiteSpace(o.CustomerComplaintTag))
            .GroupBy(o => o.CustomerComplaintTag, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ComplaintTagInsight { Tag = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToList();

        var itemRatings = rated
            .SelectMany(o => ParseItems(o.ItemsInOrder).Select(i => new { i.Name, Rating = o.Rating!.Value }))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ItemRatingInsight
            {
                ItemName = g.Key,
                RatedOrders = g.Count(),
                AverageRating = Round2(g.Average(x => x.Rating)),
                LowRatings = g.Count(x => x.Rating <= 2m)
            })
            .OrderBy(i => i.AverageRating)
            .ThenByDescending(i => i.RatedOrders)
            .ToList();

        var dailyRatings = rated
            .Where(o => o.PlacedAt.HasValue)
            .GroupBy(o => o.PlacedAt!.Value.Date)
            .OrderBy(g => g.Key)
            .Select(g => new DailyRatingInsight
            {
                Date = g.Key.ToString("yyyy-MM-dd"),
                RatedOrders = g.Count(),
                AverageRating = Round2(g.Average(o => o.Rating!.Value)),
                LowRatings = g.Count(o => o.Rating!.Value <= 2m)
            })
            .ToList();

        var ratedKpt = rated.Where(o => o.KptMinutes.HasValue).ToList();
        var lowKpt = ratedKpt.Where(o => o.Rating!.Value <= 2m).Select(o => o.KptMinutes!.Value).ToList();
        var highKpt = ratedKpt.Where(o => o.Rating!.Value >= 4m).Select(o => o.KptMinutes!.Value).ToList();

        return new ReviewRatingInsights
        {
            RatedOrders = ratingValues.Count,
            RatingCoveragePercent = deliveredCount > 0 ? Round2((decimal)ratingValues.Count / deliveredCount * 100m) : 0m,
            AverageRating = ratingValues.Count > 0 ? Round2(ratingValues.Average()) : 0m,
            PositiveRatings = ratingValues.Count(v => v >= 4m),
            NeutralRatings = ratingValues.Count(v => v >= 3m && v < 4m),
            NegativeRatings = ratingValues.Count(v => v < 3m),
            PositivePercent = ratingValues.Count > 0 ? Round2((decimal)ratingValues.Count(v => v >= 4m) / ratingValues.Count * 100m) : 0m,
            NegativePercent = ratingValues.Count > 0 ? Round2((decimal)ratingValues.Count(v => v < 3m) / ratingValues.Count * 100m) : 0m,
            WrittenReviews = orderRows.Count(o => !string.IsNullOrWhiteSpace(o.Review)),
            ComplaintOrders = orderRows.Count(o => !string.IsNullOrWhiteSpace(o.CustomerComplaintTag)),
            AverageKptLowRated = lowKpt.Count > 0 ? Round2(lowKpt.Average()) : null,
            AverageKptHighRated = highKpt.Count > 0 ? Round2(highKpt.Average()) : null,
            RatingDistribution = distribution,
            ComplaintTags = complaintTags,
            ItemRatings = itemRatings,
            DailyRatings = dailyRatings,
            OrderFeedback = feedbackOrders
        };
    }

    private static List<ItemSalesInsight> BuildItemSalesInsights(List<OrderHistoryRow> deliveredOrders)
    {
        var itemMap = new Dictionary<string, ItemSalesAccumulator>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in deliveredOrders)
        {
            var parsedItems = ParseItems(order.ItemsInOrder);
            if (parsedItems.Count == 0)
            {
                continue;
            }

            var quantityTotal = parsedItems.Sum(i => i.Quantity);
            foreach (var item in parsedItems)
            {
                if (!itemMap.TryGetValue(item.Name, out var acc))
                {
                    acc = new ItemSalesAccumulator { Name = item.Name };
                    itemMap[item.Name] = acc;
                }

                acc.Quantity += item.Quantity;
                acc.OrderIds.Add(order.OrderId);
                if (quantityTotal > 0)
                {
                    var estimatedRevenue = order.Total * ((decimal)item.Quantity / quantityTotal);
                    acc.EstimatedRevenue += estimatedRevenue;
                }
            }
        }

        return itemMap.Values
            .Select(i => new ItemSalesInsight
            {
                ItemName = i.Name,
                QuantitySold = i.Quantity,
                OrdersContainingItem = i.OrderIds.Count,
                EstimatedGrossSales = Round2(i.EstimatedRevenue)
            })
            .OrderByDescending(i => i.QuantitySold)
            .ThenBy(i => i.ItemName)
            .ToList();
    }

    private static List<ParsedOrderItem> ParseItems(string? itemsInOrder)
    {
        if (string.IsNullOrWhiteSpace(itemsInOrder))
        {
            return new List<ParsedOrderItem>();
        }

        var parts = itemsInOrder.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var parsed = new List<ParsedOrderItem>();
        foreach (var part in parts)
        {
            var match = Regex.Match(part, @"^\s*(\d+)\s*x\s*(.+?)\s*$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                parsed.Add(new ParsedOrderItem
                {
                    Quantity = int.TryParse(match.Groups[1].Value, out var qty) ? Math.Max(1, qty) : 1,
                    Name = match.Groups[2].Value.Trim()
                });
                continue;
            }

            parsed.Add(new ParsedOrderItem
            {
                Quantity = 1,
                Name = part.Trim()
            });
        }

        return parsed.Where(i => !string.IsNullOrWhiteSpace(i.Name)).ToList();
    }

    private static AdditionalDeductionsSheetData ParseAdditionalDeductionsSheet(ExcelPackage package)
    {
        var sheet = package.Workbook.Worksheets.FirstOrDefault(w =>
            string.Equals(w.Name, "Addition Deductions Details", StringComparison.OrdinalIgnoreCase));
        if (sheet == null || sheet.Dimension == null)
        {
            return new AdditionalDeductionsSheetData();
        }

        var rows = new List<AdditionalDeductionRow>();
        var totals = new List<AdditionalDeductionTotal>();
        var currentSection = string.Empty;
        var inAdditions = false;

        for (var r = 1; r <= sheet.Dimension.Rows; r++)
        {
            var col2 = (sheet.Cells[r, 2].Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(col2))
            {
                continue;
            }

            if (string.Equals(col2, "Addition Type", StringComparison.OrdinalIgnoreCase))
            {
                inAdditions = true;
                currentSection = "Additions";
                continue;
            }

            if (string.Equals(col2, "Deduction Type", StringComparison.OrdinalIgnoreCase))
            {
                inAdditions = false;
                currentSection = string.Empty;
                continue;
            }

            if (Regex.IsMatch(col2, @"^[A-Z]\)"))
            {
                currentSection = col2;
                continue;
            }

            if (string.Equals(col2, "Type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var totalAmount = ParseDecimal(sheet.Cells[r, 7].Value);
            var adjustedAmount = ParseDecimal(sheet.Cells[r, 8].Value);
            var outstandingAmount = ParseDecimal(sheet.Cells[r, 9].Value);

            if (col2.StartsWith("Total", StringComparison.OrdinalIgnoreCase))
            {
                totals.Add(new AdditionalDeductionTotal
                {
                    Label = col2,
                    TotalAmount = Round2(totalAmount),
                    AdjustedAmount = Round2(adjustedAmount),
                    OutstandingAmount = Round2(outstandingAmount)
                });
                continue;
            }

            var hasAmounts = totalAmount != 0m || adjustedAmount != 0m || outstandingAmount != 0m;
            if (!hasAmounts)
            {
                continue;
            }

            rows.Add(new AdditionalDeductionRow
            {
                Section = currentSection,
                IsAddition = inAdditions,
                Type = col2,
                Reference = (sheet.Cells[r, 4].Text ?? string.Empty).Trim(),
                Period = (sheet.Cells[r, 5].Text ?? string.Empty).Trim(),
                SettlementStatus = (sheet.Cells[r, 6].Text ?? string.Empty).Trim(),
                TotalAmount = Round2(totalAmount),
                AdjustedAmount = Round2(adjustedAmount),
                OutstandingAmount = Round2(outstandingAmount)
            });
        }

        return new AdditionalDeductionsSheetData
        {
            Rows = rows,
            Totals = totals
        };
    }

    private static string? GetStartDate(IEnumerable<OrderHistoryRow> orderRows, IEnumerable<SettlementRow> settlementRows)
    {
        var min = orderRows.Where(r => r.PlacedAt.HasValue).Select(r => r.PlacedAt!.Value)
            .Concat(settlementRows.Where(r => r.OrderDate.HasValue).Select(r => r.OrderDate!.Value))
            .DefaultIfEmpty()
            .Min();
        return min == default ? null : min.ToString("yyyy-MM-dd");
    }

    private static string? GetEndDate(IEnumerable<OrderHistoryRow> orderRows, IEnumerable<SettlementRow> settlementRows)
    {
        var max = orderRows.Where(r => r.PlacedAt.HasValue).Select(r => r.PlacedAt!.Value)
            .Concat(settlementRows.Where(r => r.OrderDate.HasValue).Select(r => r.OrderDate!.Value))
            .DefaultIfEmpty()
            .Max();
        return max == default ? null : max.ToString("yyyy-MM-dd");
    }

    private static string NormalizeHeader(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return string.Empty;
        }

        return Regex.Replace(header.ToLowerInvariant(), "[^a-z0-9]+", string.Empty);
    }

    private static decimal ParseDecimal(object? raw)
    {
        if (raw == null)
        {
            return 0m;
        }

        if (raw is decimal dec)
        {
            return dec;
        }

        if (raw is double dbl)
        {
            return Convert.ToDecimal(dbl);
        }

        if (raw is int i)
        {
            return i;
        }

        var text = raw.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0m;
        }

        text = text.Replace(",", string.Empty).Replace("₹", string.Empty).Trim();
        return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;
    }

    private static decimal ParseDistance(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        var normalized = value.ToLowerInvariant().Replace("km", string.Empty).Trim();
        return ParseDecimal(normalized);
    }

    private static decimal? ParseNullableDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parsed = ParseDecimal(value);
        return parsed == 0m && !value.Contains('0') ? null : parsed;
    }

    private static DateTime? ParseDateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = Regex.Replace(value.Trim(), @"\s+", " ");
        if (DateTime.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            return parsed;
        }

        var formats = new[]
        {
            "hh:mm tt, MMMM dd yyyy",
            "h:mm tt, MMMM dd yyyy",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd H:mm:ss",
            "yyyy-MM-dd HH:mm",
            "yyyy-MM-dd H:mm"
        };

        return DateTime.TryParseExact(normalized, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)
            ? parsed
            : null;
    }

    private static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

public class ZomatoSalesPayoutInsightResult
{
    public List<KitchenOrderTiming>? KitchenOrders { get; set; }
    public List<string> Notices { get; set; } = new();
    public InsightPeriod Period { get; set; } = new();
    public OrderHistorySummary OrderHistorySummary { get; set; } = new();
    public SettlementSummary SettlementSummary { get; set; } = new();
    public ReconciliationSummary Reconciliation { get; set; } = new();
    public List<DailyInsight> DailySales { get; set; } = new();
    public List<DailySettlementInsight> DailySettlement { get; set; } = new();
    public List<HourlyOrderInsight> HourlyOrderInsights { get; set; } = new();
    public List<ItemSalesInsight> ItemSalesInsights { get; set; } = new();
    public List<DiscountConstructInsight> TopDiscountConstructs { get; set; } = new();
    public List<DetailedDiscountInsight> DetailedDiscountInsights { get; set; } = new();
    public List<AdditionalDeductionRow> AdditionalDeductionDetails { get; set; } = new();
    public List<AdditionalDeductionTotal> AdditionalDeductionTotals { get; set; } = new();
    public List<LossOrderInsight> TopLossOrders { get; set; } = new();
    public ReviewRatingInsights ReviewRatingInsights { get; set; } = new();
}

public class ReviewRatingInsights
{
    public int RatedOrders { get; set; }
    public decimal RatingCoveragePercent { get; set; }
    public decimal AverageRating { get; set; }
    public int PositiveRatings { get; set; }
    public int NeutralRatings { get; set; }
    public int NegativeRatings { get; set; }
    public decimal PositivePercent { get; set; }
    public decimal NegativePercent { get; set; }
    public int WrittenReviews { get; set; }
    public int ComplaintOrders { get; set; }
    public decimal? AverageKptLowRated { get; set; }
    public decimal? AverageKptHighRated { get; set; }
    public List<RatingDistributionInsight> RatingDistribution { get; set; } = new();
    public List<ComplaintTagInsight> ComplaintTags { get; set; } = new();
    public List<ItemRatingInsight> ItemRatings { get; set; } = new();
    public List<DailyRatingInsight> DailyRatings { get; set; } = new();
    public List<OrderFeedbackInsight> OrderFeedback { get; set; } = new();
}

public class RatingDistributionInsight
{
    public int Stars { get; set; }
    public int Count { get; set; }
    public decimal Percent { get; set; }
}

public class ComplaintTagInsight
{
    public string Tag { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class ItemRatingInsight
{
    public string ItemName { get; set; } = string.Empty;
    public int RatedOrders { get; set; }
    public decimal AverageRating { get; set; }
    public int LowRatings { get; set; }
}

public class DailyRatingInsight
{
    public string Date { get; set; } = string.Empty;
    public int RatedOrders { get; set; }
    public decimal AverageRating { get; set; }
    public int LowRatings { get; set; }
}

public class OrderFeedbackInsight
{
    public string OrderId { get; set; } = string.Empty;
    public string? OrderDate { get; set; }
    public decimal? Rating { get; set; }
    public string Review { get; set; } = string.Empty;
    public string ComplaintTag { get; set; } = string.Empty;
    public string Items { get; set; } = string.Empty;
    public decimal OrderValue { get; set; }
    public decimal? KptMinutes { get; set; }
    public string OrderReadyMarked { get; set; } = string.Empty;
}

public class InsightPeriod
{
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
}

public class OrderHistorySummary
{
    public int TotalOrders { get; set; }
    public int DeliveredOrders { get; set; }
    public int CancelledOrRejectedOrders { get; set; }
    public decimal GrossSales { get; set; }
    public decimal AverageOrderValue { get; set; }
    public decimal TotalDiscount { get; set; }
    public decimal PromoDiscount { get; set; }
    public decimal OtherRestaurantDiscount { get; set; }
    public decimal GoldDiscount { get; set; }
    public decimal BrandPackDiscount { get; set; }
    public decimal PackagingCharges { get; set; }
    public decimal AverageDistanceKm { get; set; }
    public decimal AverageKptMinutes { get; set; }
    public decimal AverageRiderWaitMinutes { get; set; }
    public int RatedOrders { get; set; }
    public decimal AverageRating { get; set; }
    public string? PeakOrderHour { get; set; }
    public int PeakOrderHourOrders { get; set; }
    public string? TopSalesDay { get; set; }
    public decimal TopSalesDayGrossSales { get; set; }
    public int OrderMarkingIssues { get; set; }
    public int CustomerComplaintCount { get; set; }
}

public class SettlementSummary
{
    public int TotalOrders { get; set; }
    public int SettledOrders { get; set; }
    public int UnsettledOrders { get; set; }
    public decimal NetOrderValue { get; set; }
    public decimal TotalPayout { get; set; }
    public decimal SettledPayout { get; set; }
    public decimal NetDeductions { get; set; }
    public decimal NetAdditions { get; set; }
    public decimal ServiceFeeAndPmFee { get; set; }
    public decimal TaxesOnServiceFee { get; set; }
    public decimal GovernmentCharges { get; set; }
    public decimal OtherOrderLevelDeductions { get; set; }
    public decimal TotalUnsettledAmount { get; set; }
    public decimal Tds194OTotal { get; set; }
    public decimal? AdditionalDeductionsAdjusted { get; set; }
    public decimal? AdditionalAdditionsAdjusted { get; set; }
    public decimal? ActualPayoutCredited { get; set; }
    public decimal AveragePayoutPerOrder { get; set; }
    public string? TopPayoutDay { get; set; }
    public decimal TopPayoutDayAmount { get; set; }
}

public class ReconciliationSummary
{
    public int MatchedDeliveredOrders { get; set; }
    public int MissingInSettlementCount { get; set; }
    public int MissingInOrderHistoryCount { get; set; }
    public List<string> MissingInSettlementOrderIds { get; set; } = new();
    public List<string> MissingInOrderHistoryOrderIds { get; set; } = new();
    public decimal CsvGrossSales { get; set; }
    public decimal SettlementPayout { get; set; }
    public decimal GrossToPayoutDifference { get; set; }
    public decimal PayoutToGrossPercent { get; set; }
    public decimal DeliveredOrderCoveragePercent { get; set; }
}

public class DailyInsight
{
    public string Date { get; set; } = string.Empty;
    public int Orders { get; set; }
    public decimal GrossSales { get; set; }
    public decimal Discounts { get; set; }
}

public class DailySettlementInsight
{
    public string Date { get; set; } = string.Empty;
    public int Orders { get; set; }
    public decimal NetOrderValue { get; set; }
    public decimal Payout { get; set; }
    public decimal NetDeductions { get; set; }
}

public class DiscountConstructInsight
{
    public string DiscountConstruct { get; set; } = string.Empty;
    public int Orders { get; set; }
    public decimal GrossSales { get; set; }
    public decimal DiscountGiven { get; set; }
}

public class DetailedDiscountInsight
{
    public string DiscountConstruct { get; set; } = string.Empty;
    public int Orders { get; set; }
    public decimal GrossSales { get; set; }
    public decimal DiscountGiven { get; set; }
    public decimal AverageDiscountPerOrder { get; set; }
    public decimal EffectiveDiscountPercent { get; set; }
}

public class HourlyOrderInsight
{
    public string HourLabel { get; set; } = string.Empty;
    public int Orders { get; set; }
    public decimal GrossSales { get; set; }
    public decimal AverageOrderValue { get; set; }
}

public class ItemSalesInsight
{
    public string ItemName { get; set; } = string.Empty;
    public int QuantitySold { get; set; }
    public int OrdersContainingItem { get; set; }
    public decimal EstimatedGrossSales { get; set; }
}

public class AdditionalDeductionRow
{
    public string Section { get; set; } = string.Empty;
    public bool IsAddition { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public string SettlementStatus { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal AdjustedAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
}

public class AdditionalDeductionTotal
{
    public string Label { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal AdjustedAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
}

public class LossOrderInsight
{
    public string OrderId { get; set; } = string.Empty;
    public string? OrderDate { get; set; }
    public decimal NetOrderValue { get; set; }
    public decimal NetDeductions { get; set; }
    public decimal Payout { get; set; }
    public decimal DeductionRatePercent { get; set; }
}

public class OrderHistoryRow
{
    public string OrderId { get; set; } = string.Empty;
    public DateTime? PlacedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string DeliveryType { get; set; } = string.Empty;
    public decimal DistanceKm { get; set; }
    public string ItemsInOrder { get; set; } = string.Empty;
    public string DiscountConstruct { get; set; } = string.Empty;
    public decimal BillSubtotal { get; set; }
    public decimal PackagingCharges { get; set; }
    public decimal RestaurantDiscountPromo { get; set; }
    public decimal RestaurantDiscountOthers { get; set; }
    public decimal GoldDiscount { get; set; }
    public decimal BrandPackDiscount { get; set; }
    public decimal Total { get; set; }
    public decimal? Rating { get; set; }
    public string Review { get; set; } = string.Empty;
    public decimal? KptMinutes { get; set; }
    public string? KptDataStatus { get; set; }
    public decimal? RiderWaitMinutes { get; set; }
    public string? RiderWaitDataStatus { get; set; }
    public string OrderReadyMarked { get; set; } = string.Empty;
    public string CustomerComplaintTag { get; set; } = string.Empty;
}

public class SettlementRow
{
    public string OrderId { get; set; } = string.Empty;
    public DateTime? OrderDate { get; set; }
    public string DiscountConstruct { get; set; } = string.Empty;
    public string OrderStatus { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal PackagingCharge { get; set; }
    public decimal PromoDiscount { get; set; }
    public decimal OtherRestaurantDiscount { get; set; }
    public decimal NetOrderValue { get; set; }
    public decimal TotalCommissionableValue { get; set; }
    public decimal ServiceFeeAndPmFee { get; set; }
    public decimal TaxesOnServiceFee { get; set; }
    public decimal GovernmentCharges { get; set; }
    public decimal OtherOrderLevelDeductions { get; set; }
    public decimal NetDeductions { get; set; }
    public decimal NetAdditions { get; set; }
    public decimal OrderLevelPayout { get; set; }
    public string SettlementStatus { get; set; } = string.Empty;
    public DateTime? SettlementDate { get; set; }
    public string BankUtr { get; set; } = string.Empty;
    public decimal UnsettledAmount { get; set; }
    public decimal ActualOrderDistanceKm { get; set; }
    public decimal Tds194OAmount { get; set; }
}

internal class ParsedOrderItem
{
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

internal class ItemSalesAccumulator
{
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal EstimatedRevenue { get; set; }
    public HashSet<string> OrderIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal class SettlementWorkbookData
{
    public List<SettlementRow> SettlementRows { get; set; } = new();
    public List<AdditionalDeductionRow> AdditionalDeductionRows { get; set; } = new();
    public List<AdditionalDeductionTotal> AdditionalDeductionTotals { get; set; } = new();
}

internal class AdditionalDeductionsSheetData
{
    public List<AdditionalDeductionRow> Rows { get; set; } = new();
    public List<AdditionalDeductionTotal> Totals { get; set; } = new();
}
