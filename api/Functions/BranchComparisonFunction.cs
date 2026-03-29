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

            // Get all outlets
            var outlets = await _mongo.GetAllOutletsAsync();
            var branchData = new List<object>();

            foreach (var outlet in outlets)
            {
                var oid = outlet.Id ?? "default";

                // Sales data
                var sales = await _mongo.GetSalesByDateRangeAsync(startDate, endDate, oid);
                var totalRevenue = sales.Sum(s => s.TotalAmount);
                var totalOrders = sales.Count;

                // Expenses
                var expenses = await _mongo.GetExpensesByDateRangeAsync(startDate, endDate, oid);
                var totalExpenses = expenses.Sum(e => e.Amount);

                // Menu items
                var menuItems = await _mongo.GetMenuAsync(oid);
                var topItems = menuItems
                    .Take(5)
                    .Select(m => new { m.Name, m.OnlinePrice })
                    .ToList();

                branchData.Add(new
                {
                    outletId = oid,
                    outletName = outlet.OutletName ?? oid,
                    metrics = new
                    {
                        totalRevenue,
                        totalOrders,
                        totalExpenses,
                        profit = totalRevenue - totalExpenses,
                        profitMargin = totalRevenue > 0
                            ? Math.Round(((totalRevenue - totalExpenses) / totalRevenue) * 100, 1)
                            : 0,
                        averageOrderValue = totalOrders > 0
                            ? Math.Round(totalRevenue / totalOrders, 2)
                            : 0,
                        menuItemCount = menuItems.Count
                    },
                    topSellingItems = topItems,
                    period = new { startDate = startDate.ToString("yyyy-MM-dd"), endDate = endDate.ToString("yyyy-MM-dd") }
                });
            }

            // If only one outlet, still return it
            if (!branchData.Any())
            {
                var sales = await _mongo.GetSalesByDateRangeAsync(startDate, endDate);
                var expenses = await _mongo.GetExpensesByDateRangeAsync(startDate, endDate);

                branchData.Add(new
                {
                    outletId = "default",
                    outletName = "Main Branch",
                    metrics = new
                    {
                        totalRevenue = sales.Sum(s => s.TotalAmount),
                        totalOrders = sales.Count,
                        totalExpenses = expenses.Sum(e => e.Amount),
                        profit = sales.Sum(s => s.TotalAmount) - expenses.Sum(e => e.Amount),
                        profitMargin = sales.Sum(s => s.TotalAmount) > 0
                            ? Math.Round(((sales.Sum(s => s.TotalAmount) - expenses.Sum(e => e.Amount)) / sales.Sum(s => s.TotalAmount)) * 100, 1)
                            : 0,
                        averageOrderValue = sales.Count > 0
                            ? Math.Round(sales.Sum(s => s.TotalAmount) / sales.Count, 2)
                            : 0,
                        menuItemCount = 0
                    },
                    topSellingItems = new List<object>(),
                    period = new { startDate = startDate.ToString("yyyy-MM-dd"), endDate = endDate.ToString("yyyy-MM-dd") }
                });
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
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
