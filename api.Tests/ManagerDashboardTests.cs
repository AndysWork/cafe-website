using System.Net;
using System.Text.Json;
using Azure.Core.Serialization;
using Cafe.Api.Functions;
using Cafe.Api.Models;
using Cafe.Api.Repositories;
using Cafe.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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

    private static ManagerDashboardResponse EmptySnapshot() => new(
        Array.Empty<DashboardMenuItemResponse>(), Array.Empty<DashboardOnlineSaleResponse>(),
        Array.Empty<DashboardOfflineSaleResponse>(), Array.Empty<DashboardExpenseResponse>(),
        Array.Empty<DashboardAttendanceResponse>(), Array.Empty<DashboardStockAlertResponse>(), Array.Empty<string>());

    private HttpRequestData Request(string? role, string? outletId = null, string? secondary = null)
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
        request.SetupGet(value => value.Query).Returns(System.Web.HttpUtility.ParseQueryString(""));
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
