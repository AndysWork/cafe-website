using System.Net;
using System.Text.Json;
using Azure.Core.Serialization;
using Cafe.Api.Functions;
using Cafe.Api.Helpers;
using Cafe.Api.Models;
using Cafe.Api.Repositories;
using Cafe.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using Moq;
using Xunit;

namespace Cafe.Api.Tests;

[Collection("Auth environment")]
public sealed class ManagerDashboardTests
{
    private const string UserId = "111111111111111111111111";
    private const string OutletId = "222222222222222222222222";
    private const string OtherId = "333333333333333333333333";
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IStaffRepository> _staff = new();
    private readonly Mock<IOutletRepository> _outlets = new();
    private readonly Mock<IManagerDashboardService> _reader = new(MockBehavior.Strict);
    private readonly AuthService _auth;
    private readonly ManagerDashboardFunction _function;
    private readonly User _user = new() { Id = UserId, Email = "manager@example.invalid", AssignedOutlets = new() { OutletId } };

    public ManagerDashboardTests()
    {
        var previous = Environment.GetEnvironmentVariable("Jwt__Secret");
        try
        {
            Environment.SetEnvironmentVariable("Jwt__Secret", Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
            _auth = new AuthService();
        }
        finally { Environment.SetEnvironmentVariable("Jwt__Secret", previous); }
        _users.Setup(repository => repository.GetUserByIdAsync(UserId)).ReturnsAsync(_user);
        _outlets.Setup(repository => repository.GetOutletByIdAsync(OutletId, false))
            .ReturnsAsync(new Outlet { Id = OutletId, IsActive = true });
        _function = new(_reader.Object, _users.Object, _staff.Object, _outlets.Object, _auth,
            NullLogger<ManagerDashboardFunction>.Instance);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("user", HttpStatusCode.Forbidden)]
    [InlineData("cashier", HttpStatusCode.Forbidden)]
    public async Task DashboardAndChoicesRequireManagerRole(string? role, HttpStatusCode status)
    {
        Assert.Equal(status, (await _function.GetDashboard(Request(role), default)).StatusCode);
        Assert.Equal(status, (await _function.GetOutlets(Request(role), default)).StatusCode);
        _reader.VerifyNoOtherCalls();
        _outlets.Verify(repository => repository.GetActiveOutletsAsync(), Times.Never);
    }

    [Theory]
    [InlineData("manager", null)]
    [InlineData("waiter", "manager")]
    public async Task AssignedManagerAndSecondaryManagerRoleCanRead(string role, string? secondary)
    {
        var snapshot = EmptySnapshot();
        _reader.Setup(reader => reader.GetAsync(OutletId, It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);
        var response = await _function.GetDashboard(Request(role, OutletId, secondary), default);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await Read(response)).TryGetProperty("onlineSales", out _));
        _reader.Verify(reader => reader.GetAsync(OutletId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(OtherId)]
    [InlineData("")]
    public async Task MissingOrUnassignedOutletNeverReadsFinancialData(string outletId)
    {
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _function.GetDashboard(Request("manager", outletId), default)).StatusCode);
        _reader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RangeReadsUseInclusiveDatesAndEnforceOutletAssignment()
    {
        var from = new DateTime(2024, 2, 25);
        var endExclusive = new DateTime(2024, 3, 2);
        _reader.Setup(reader => reader.GetRangeAsync(OutletId, from, endExclusive, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptySnapshot());
        var query = "from=2024-02-25&to=2024-03-01";
        Assert.Equal(HttpStatusCode.OK, (await _function.GetRange(Request("manager", OutletId, query: query), default)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _function.GetRange(Request("manager", OtherId, query: query), default)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _function.GetRange(
            Request("manager", OutletId, query: "from=2024-03-02&to=2024-03-01"), default)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _function.GetRange(Request("cashier", OutletId, query: query), default)).StatusCode);
        _reader.Verify(reader => reader.GetRangeAsync(OutletId, from, endExclusive, It.IsAny<CancellationToken>()), Times.Once);
        _reader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DashboardOrderPagesEnforceOutletAccessAndCarryTheSelectedRange()
    {
        var orders = new Mock<IOrderRepository>(MockBehavior.Strict);
        var from = new DateTime(2024, 2, 28);
        var endExclusive = new DateTime(2024, 3, 2);
        orders.Setup(repository => repository.GetDashboardOrdersAsync(OutletId, from, endExclusive, 2, 500))
            .ReturnsAsync(new List<Order> { new() { Id = "old-active", Status = "pending", CreatedAt = from } });
        var function = new OrderFunction(orders.Object, null!, null!, null!, _users.Object, _staff.Object,
            _outlets.Object, null!, null!, _auth, null!, null!, null!, null!, NullLoggerFactory.Instance);
        var query = "from=2024-02-28&to=2024-03-01&page=2";
        var response = await function.GetDashboardOrders(Request("manager", OutletId, query: query), default);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single((await Read(response)).EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await function.GetDashboardOrders(Request("manager", OtherId, query: query), default)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await function.GetDashboardOrders(
            Request("manager", OutletId, query: "from=2024-02-28&to=2024-03-01&page=0"), default)).StatusCode);
        orders.VerifyAll();
        orders.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ChoicesUseAccountOrActiveLinkedStaffAssignmentsAndExcludeOtherOutlets()
    {
        _user.AssignedOutlets.Clear();
        _staff.Setup(repository => repository.GetStaffByUserIdAsync(UserId))
            .ReturnsAsync(new Staff { UserId = UserId, IsActive = true, OutletIds = new() { OutletId } });
        _outlets.Setup(repository => repository.GetActiveOutletsAsync()).ReturnsAsync(new List<Outlet>
        {
            new() { Id = OtherId, OutletName = "Other", IsActive = true },
            new() { Id = OutletId, OutletName = "Assigned", IsActive = true },
            new() { Id = "inactive", IsActive = false }
        });
        var response = await _function.GetOutlets(Request("manager"), default);
        var choices = await Read(response);
        Assert.Single(choices.EnumerateArray());
        Assert.Equal(OutletId, choices[0].GetProperty("id").GetString());
        _reader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InactiveAssignedOutletIsForbidden()
    {
        _outlets.Setup(repository => repository.GetOutletByIdAsync(OutletId, false))
            .ReturnsAsync(new Outlet { Id = OutletId, IsActive = false });
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _function.GetDashboard(Request("manager", OutletId), default)).StatusCode);
        _reader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OperationalAndFloorDashboardReadsAlsoRejectUnassignedManagerOutlet()
    {
        var operations = new Mock<IOperationsRepository>(MockBehavior.Strict);
        var board = new ManagerOperationsFunction(operations.Object, Mock.Of<IOrderRepository>(),
            _users.Object, _staff.Object, _outlets.Object, _auth, null!, NullLoggerFactory.Instance);
        var floor = new CafeTableFunction(null!, Mock.Of<IOrderRepository>(), _outlets.Object,
            _users.Object, _staff.Object, _auth, null!, NullLoggerFactory.Instance);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await board.GetManagerOperationsBoard(Request("manager", OtherId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await floor.GetFloor(Request("manager", OtherId))).StatusCode);
        operations.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReaderUsesOneOutletIncludesWebPayoutsAndPreservesLegacyIstAndUtcDates()
    {
        var finance = new Mock<IFinanceRepository>(MockBehavior.Strict);
        var menu = new Mock<IMenuRepository>(MockBehavior.Strict);
        var inventory = new Mock<IInventoryRepository>(MockBehavior.Strict);
        var legacyDate = new DateTime(2026, 11, 9, 23, 45, 0, DateTimeKind.Unspecified);
        finance.Setup(repository => repository.GetOnlineSalesAsync(null, OutletId, null, null, true))
            .ReturnsAsync(new List<OnlineSale> { new() { Platform = "Web Sales", Payout = 139, OrderAt = legacyDate } });
        finance.Setup(repository => repository.GetAllSalesAsync(OutletId, null, null))
            .ReturnsAsync(new List<Sales> { new() { Date = legacyDate, TotalAmount = 100 } });
        finance.Setup(repository => repository.GetAllExpensesAsync(OutletId, null, null)).ReturnsAsync(new List<Expense>());
        finance.Setup(repository => repository.GetZomatoSourceRecordsByPeriodAsync(
                OutletId, DateTime.MinValue, DateTime.MaxValue, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ZomatoSalesPayoutInsightRecord>());
        menu.Setup(repository => repository.GetMenuAsync(OutletId, null, null))
            .ReturnsAsync(new List<CafeMenuItem> { new() { Name = "Hidden Tea", IsVisibleToCustomers = false, IsAvailable = false } });
        inventory.Setup(repository => repository.GetAllAlertsAsync(OutletId))
            .ThrowsAsync(new InvalidOperationException("Isolated unavailable source"));
        _staff.Setup(repository => repository.GetAllTodayAttendanceAsync(OutletId))
            .ReturnsAsync(new List<Attendance> { new() { ClockIn = new DateTime(2026, 11, 9, 18, 30, 0, DateTimeKind.Utc) } });
        var reader = new ManagerDashboardService(finance.Object, menu.Object, inventory.Object,
            _staff.Object, NullLogger<ManagerDashboardService>.Instance);
        var result = await reader.GetAsync(OutletId, default);
        Assert.Equal(139, Assert.Single(result.OnlineSales).Payout);
        Assert.Equal(TimeSpan.FromMinutes(330), result.OnlineSales[0].OrderAt.Offset);
        Assert.Equal(9, result.OnlineSales[0].OrderAt.Day);
        Assert.Equal(23, result.OfflineSales[0].Date.Hour);
        Assert.False(Assert.Single(result.MenuItems).IsAvailable);
        Assert.Equal(TimeSpan.Zero, Assert.Single(result.Attendance!).ClockIn!.Value.Offset);
        Assert.Null(result.StockAlerts);
        Assert.Equal(new[] { "Stock alerts" }, result.UnavailableSources);
        finance.VerifyAll();
        menu.VerifyAll();
        inventory.VerifyAll();
    }

    [Fact]
    public async Task RangeReaderUsesIstBoundariesAndDoesNotTruncateLargeFinanceResults()
    {
        var finance = new Mock<IFinanceRepository>(MockBehavior.Strict);
        var menu = new Mock<IMenuRepository>(MockBehavior.Strict);
        var inventory = new Mock<IInventoryRepository>(MockBehavior.Strict);
        var from = new DateTime(2024, 2, 25);
        var end = new DateTime(2024, 3, 2);
        var utcFrom = new DateTime(2024, 2, 24, 18, 30, 0, DateTimeKind.Utc);
        var utcEnd = new DateTime(2024, 3, 1, 18, 30, 0, DateTimeKind.Utc);
        finance.Setup(repository => repository.GetDashboardOnlineSalesAsync(OutletId, from, end))
            .ReturnsAsync(Enumerable.Range(0, 5001).Select(_ => new OnlineSale { OrderAt = from, Payout = 1 }).ToList());
        finance.Setup(repository => repository.GetDashboardSalesAsync(OutletId, utcFrom, utcEnd))
            .ReturnsAsync(new List<Sales> { new() { Date = utcFrom, TotalAmount = 20 } });
        finance.Setup(repository => repository.GetDashboardExpensesAsync(OutletId, utcFrom, utcEnd))
            .ReturnsAsync(new List<Expense> { new() { Date = utcEnd.AddTicks(-1), Amount = 10 } });
        finance.Setup(repository => repository.GetZomatoSourceRecordsByPeriodAsync(
                OutletId, from, end.AddDays(-1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ZomatoSalesPayoutInsightRecord>());
        menu.Setup(repository => repository.GetMenuAsync(OutletId, null, null)).ReturnsAsync(new List<CafeMenuItem>());
        inventory.Setup(repository => repository.GetAllAlertsAsync(OutletId)).ReturnsAsync(new List<StockAlert>());
        _staff.Setup(repository => repository.GetAllTodayAttendanceAsync(OutletId)).ReturnsAsync(new List<Attendance>());
        var reader = new ManagerDashboardService(finance.Object, menu.Object, inventory.Object,
            _staff.Object, NullLogger<ManagerDashboardService>.Instance);
        var result = await reader.GetRangeAsync(OutletId, from, end, default);
        Assert.Equal(5001, result.OnlineSales.Count);
        Assert.Equal(20, Assert.Single(result.OfflineSales).TotalAmount);
        Assert.Equal(10, Assert.Single(result.Expenses).Amount);
        finance.VerifyAll();
        finance.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RangeReaderMergesParsedZomatoOrdersOnceWithSettlementPayouts()
    {
        var finance = new Mock<IFinanceRepository>(MockBehavior.Strict);
        var menu = new Mock<IMenuRepository>(MockBehavior.Strict);
        var inventory = new Mock<IInventoryRepository>(MockBehavior.Strict);
        var from = new DateTime(2025, 10, 26);
        var end = new DateTime(2025, 10, 28);
        var utcFrom = IstDateHelper.IstWallTimeToUtc(from);
        var utcEnd = IstDateHelper.IstWallTimeToUtc(end);
        OrderHistoryRow Order(string id, DateTime at, string items = "2 x Cold Coffee, 1 x Fries") => new()
        {
            OrderId = id, PlacedAt = at, Status = "Delivered", ItemsInOrder = items,
            BillSubtotal = 300, RestaurantDiscountPromo = 30, Rating = 4, Review = "Nice"
        };
        static BsonDocument Serialize(ZomatoReportSourceData data) => BsonDocument.Parse(JsonSerializer.Serialize(data));
        var older = new ZomatoSalesPayoutInsightRecord
        {
            Id = "a", UpdatedAt = new DateTime(2025, 11, 1), PeriodStartDate = from, PeriodEndDate = from.AddDays(1),
            SourceData = Serialize(new ZomatoReportSourceData
            {
                Orders = { Order("Z1", from.AddHours(13), "9 x Stale") },
                AdditionalDeductions = { new AdditionalDeductionRow { AdjustedAmount = 500 } }
            })
        };
        var latest = new ZomatoSalesPayoutInsightRecord
        {
            Id = "b", UpdatedAt = new DateTime(2025, 11, 2), PeriodStartDate = from, PeriodEndDate = from.AddDays(1),
            SourceData = Serialize(new ZomatoReportSourceData
            {
                AdditionalTotals =
                {
                    new AdditionalDeductionTotal { Label = "Total Deductions", AdjustedAmount = 120.456m },
                    new AdditionalDeductionTotal { Label = "Total Additions", AdjustedAmount = 10 }
                },
                AdditionalDeductions = { new AdditionalDeductionRow { AdjustedAmount = 999 } },
                Orders =
                {
                    Order("Z1", from.AddHours(13)), Order("LEGACY", from.AddHours(14)),
                    Order("OUT", end.AddHours(1)), Order("Z2", from.AddDays(1).AddHours(20))
                },
                Settlements =
                {
                    new SettlementRow { OrderId = "Z1", OrderLevelPayout = 210, NetDeductions = 60 },
                    new SettlementRow { OrderId = "CANCELLED", OrderDate = from.AddHours(9), OrderLevelPayout = 50 }
                }
            })
        };
        finance.Setup(repository => repository.GetDashboardOnlineSalesAsync(OutletId, from, end))
            .ReturnsAsync(new List<OnlineSale> { new() { Platform = "zomato", OrderId = "LEGACY", OrderAt = from, Payout = 99 } });
        finance.Setup(repository => repository.GetDashboardSalesAsync(OutletId, utcFrom, utcEnd)).ReturnsAsync(new List<Sales>());
        finance.Setup(repository => repository.GetDashboardExpensesAsync(OutletId, utcFrom, utcEnd)).ReturnsAsync(new List<Expense>());
        finance.Setup(repository => repository.GetZomatoSourceRecordsByPeriodAsync(
                OutletId, from, end.AddDays(-1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ZomatoSalesPayoutInsightRecord> { older, latest });
        menu.Setup(repository => repository.GetMenuAsync(OutletId, null, null)).ReturnsAsync(new List<CafeMenuItem>());
        inventory.Setup(repository => repository.GetAllAlertsAsync(OutletId)).ReturnsAsync(new List<StockAlert>());
        _staff.Setup(repository => repository.GetAllTodayAttendanceAsync(OutletId)).ReturnsAsync(new List<Attendance>());
        var reader = new ManagerDashboardService(finance.Object, menu.Object, inventory.Object,
            _staff.Object, NullLogger<ManagerDashboardService>.Instance);

        var result = await reader.GetRangeAsync(OutletId, from, end, default);

        Assert.Equal(new[] { "LEGACY", "CANCELLED", "Z1", "Z2" }, result.OnlineSales.Select(sale => sale.OrderId));
        var z1 = result.OnlineSales.Single(sale => sale.OrderId == "Z1");
        Assert.Equal("Zomato", z1.Platform);
        Assert.Equal(210, z1.Payout);
        Assert.Equal(60, z1.PlatformDeduction);
        Assert.Equal(30, z1.DiscountAmount);
        Assert.Equal(new[] { ("Cold Coffee", 2), ("Fries", 1) }, z1.OrderedItems.Select(item => (item.ItemName, item.Quantity)));
        Assert.Equal(new DateTimeOffset(from.AddHours(13), TimeSpan.FromMinutes(330)), z1.OrderAt);
        Assert.Equal(0, result.OnlineSales.Single(sale => sale.OrderId == "Z2").Payout);
        Assert.Empty(result.OnlineSales.Single(sale => sale.OrderId == "CANCELLED").OrderedItems);
        var adjustment = Assert.Single(result.ZomatoAdjustments!);
        Assert.Equal(new DashboardZomatoAdjustmentResponse("2025-10-26", "2025-10-27", 120.46m, 10), adjustment);
        Assert.Empty(result.UnavailableSources);
    }

    private static ManagerDashboardResponse EmptySnapshot() => new(
        Array.Empty<DashboardMenuItemResponse>(), Array.Empty<DashboardOnlineSaleResponse>(),
        Array.Empty<DashboardOfflineSaleResponse>(), Array.Empty<DashboardExpenseResponse>(),
        Array.Empty<DashboardAttendanceResponse>(), Array.Empty<DashboardStockAlertResponse>(), Array.Empty<string>());

    private HttpRequestData Request(string? role, string? outletId = null, string? secondary = null, string query = "")
    {
        var services = new ServiceCollection();
        services.AddOptions<WorkerOptions>().Configure(options =>
            options.Serializer = new JsonObjectSerializer(new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var context = new Mock<FunctionContext>();
        context.SetupProperty(value => value.InstanceServices, services.BuildServiceProvider());
        var request = new Mock<HttpRequestData>(context.Object);
        var headers = new HttpHeadersCollection();
        if (role != null) headers.Add("Authorization", $"Bearer {_auth.GenerateJwtToken(UserId, "manager", role,
            additionalRoles: secondary == null ? null : new[] { secondary })}");
        if (outletId != null) headers.Add("X-Outlet-Id", outletId);
        request.SetupGet(value => value.Headers).Returns(headers);
        request.SetupGet(value => value.Query).Returns(System.Web.HttpUtility.ParseQueryString(query));
        request.Setup(value => value.CreateResponse()).Returns(() =>
        {
            var response = new Mock<HttpResponseData>(context.Object);
            response.SetupProperty(value => value.StatusCode, HttpStatusCode.OK);
            response.SetupProperty(value => value.Headers, new HttpHeadersCollection());
            response.SetupProperty(value => value.Body, new MemoryStream());
            return response.Object;
        });
        return request.Object;
    }

    private static async Task<JsonElement> Read(HttpResponseData response)
    {
        response.Body.Position = 0;
        return (await JsonDocument.ParseAsync(response.Body)).RootElement.Clone();
    }
}
