using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Helpers;
using System.Net;

namespace Cafe.Api.Functions;

public class BranchComparisonFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public BranchComparisonFunction(MongoService mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<BranchComparisonFunction>();
    }

    [Function("CompareBranches")]
    public async Task<HttpResponseData> CompareBranches(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/branch-comparison")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var startDateStr = req.Query["startDate"];
            var endDateStr = req.Query["endDate"];

            DateTime startDate = DateTime.TryParse(startDateStr, out var sd) ? sd : MongoService.GetIstNow().AddDays(-30);
            DateTime endDate = DateTime.TryParse(endDateStr, out var ed) ? ed : MongoService.GetIstNow();

            // Parse optional outletIds filter
            var outletIdsParam = req.Query["outletIds"];
            var filterOutletIds = !string.IsNullOrWhiteSpace(outletIdsParam)
                ? outletIdsParam.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(id => id.Trim()).ToHashSet()
                : null;

            // Get all outlets, then filter if needed
            var outlets = await _mongo.GetAllOutletsAsync();
            if (filterOutletIds != null && filterOutletIds.Count > 0)
            {
                outlets = outlets.Where(o => filterOutletIds.Contains(o.Id ?? "default")).ToList();
            }

            var branchData = new List<object>();

            foreach (var outlet in outlets)
            {
                var oid = outlet.Id ?? "default";

                // Sales data
                var sales = await _mongo.GetSalesByDateRangeAsync(startDate, endDate, oid);
                var totalSales = sales.Sum(s => s.TotalAmount);
                var totalOrders = sales.Count;

                // Expenses
                var expenses = await _mongo.GetExpensesByDateRangeAsync(startDate, endDate, oid);
                var totalExpenses = expenses.Sum(e => e.Amount);

                var netProfit = totalSales - totalExpenses;
                var averageOrderValue = totalOrders > 0 ? Math.Round(totalSales / totalOrders, 2) : 0;

                branchData.Add(new
                {
                    outletId = oid,
                    outletName = outlet.OutletName ?? oid,
                    totalSales,
                    totalOrders,
                    totalExpenses,
                    netProfit,
                    averageOrderValue,
                    profitMargin = totalSales > 0
                        ? Math.Round((netProfit / totalSales) * 100, 1)
                        : 0
                });
            }

            // Fallback if no outlets found
            if (!branchData.Any())
            {
                var sales = await _mongo.GetSalesByDateRangeAsync(startDate, endDate);
                var expenses = await _mongo.GetExpensesByDateRangeAsync(startDate, endDate);
                var totalSales = sales.Sum(s => s.TotalAmount);
                var totalExpenses = expenses.Sum(e => e.Amount);

                branchData.Add(new
                {
                    outletId = "default",
                    outletName = "Main Branch",
                    totalSales,
                    totalOrders = sales.Count,
                    totalExpenses,
                    netProfit = totalSales - totalExpenses,
                    averageOrderValue = sales.Count > 0 ? Math.Round(totalSales / sales.Count, 2) : 0,
                    profitMargin = totalSales > 0
                        ? Math.Round(((totalSales - totalExpenses) / totalSales) * 100, 1)
                        : 0
                });
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                period = $"{startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}",
                branches = branchData,
                totalBranches = branchData.Count
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error comparing branches");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }
}
