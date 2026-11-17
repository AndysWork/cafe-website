using System.Net;
using System.Text;
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
public sealed class CustomerJourneyFunctionTests
{
    private const string UserId = "111111111111111111111111";
    private const string OutletId = "222222222222222222222222";
    private const string OrderId = "333333333333333333333333";
    private const string SessionId = "11111111-1111-4111-8111-111111111111";
    private readonly Mock<ICustomerJourneyService> _journeys = new(MockBehavior.Strict);
    private readonly Mock<IOutletRepository> _outlets = new();
    private readonly Mock<IOrderRepository> _orders = new();
    private readonly AuthService _auth;
    private readonly CustomerJourneyFunction _function;
    private static readonly JourneyContext Context = new() { SessionId = SessionId, Token = new string('a', 64) };

    public CustomerJourneyFunctionTests()
    {
        var previous = Environment.GetEnvironmentVariable("Jwt__Secret");
        try
        {
            Environment.SetEnvironmentVariable("Jwt__Secret", Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
            _auth = new AuthService();
        }
        finally { Environment.SetEnvironmentVariable("Jwt__Secret", previous); }
        _outlets.Setup(repository => repository.GetOutletByIdAsync(OutletId, false))
            .ReturnsAsync(new Outlet { Id = OutletId, IsActive = true });
        _function = new(_journeys.Object, _auth, _outlets.Object, _orders.Object,
            NullLogger<CustomerJourneyFunction>.Instance);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("user", HttpStatusCode.Forbidden)]
    [InlineData("manager", HttpStatusCode.Forbidden)]
    public async Task EveryReportRequiresAnAdministrator(string? role, HttpStatusCode status)
    {
        Func<HttpRequestData, CancellationToken, Task<HttpResponseData>>[] readers =
        [
            _function.Dashboard, _function.Sessions, _function.Live, _function.Export,
            (request, ct) => _function.Timeline(request, SessionId, ct)
        ];
        foreach (var read in readers)
        {
            var response = await read(Request(role, OutletId), default);
            Assert.Equal(status, response.StatusCode);
            Assert.Equal("no-store", Assert.Single(response.Headers.GetValues("Cache-Control")));
        }
        _journeys.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReportingRequiresOneExplicitOutlet()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _function.Dashboard(Request("admin"), default)).StatusCode);
        _journeys.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UnavailableTimelineReturnsNotFoundRatherThanEmptySuccess()
    {
        _journeys.Setup(service => service.GetTimelineAsync(OutletId, It.IsAny<JourneyReportRange>(),
            SessionId, 1, It.IsAny<CancellationToken>())).ReturnsAsync((JourneyTimelineResponse?)null);
        var response = await _function.Timeline(Request("admin", OutletId), SessionId, default);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task InvalidCapabilityIsRejectedBeforeIngestion()
    {
        _journeys.Setup(service => service.TrackAsync(It.IsAny<TrackJourneyBatchRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("The analytics session has expired or was revoked."));
        var response = await _function.Track(Request(body: new TrackJourneyBatchRequest
        {
            Context = Context, Events = [new JourneyEventRequest { Id = Guid.NewGuid().ToString(), Type = "page_view" }]
        }), default);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ConfirmationReloadsTheOwnedOrderAndNeverAcceptsClientTotals()
    {
        var order = new Order { Id = OrderId, UserId = UserId, OutletId = OutletId, Total = 123 };
        _orders.Setup(repository => repository.GetOrderByIdAsync(OrderId)).ReturnsAsync(order);
        _journeys.Setup(service => service.ConfirmOrderAsync(Context, order, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var response = await _function.ConfirmOrder(Request("user", body:
            new ConfirmJourneyOrderRequest { Context = Context, OrderId = OrderId }), default);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        _journeys.Verify(service => service.ConfirmOrderAsync(Context, order, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnotherCustomersOrderCannotBeAttributed()
    {
        _orders.Setup(repository => repository.GetOrderByIdAsync(OrderId))
            .ReturnsAsync(new Order { Id = OrderId, UserId = OutletId });
        var response = await _function.ConfirmOrder(Request("user", body:
            new ConfirmJourneyOrderRequest { Context = Context, OrderId = OrderId }), default);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        _journeys.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LegacyIngestionCannotBypassExplicitConsent()
    {
        _journeys.Setup(service => service.ValidateAsync(null, It.IsAny<CancellationToken>(), false))
            .ThrowsAsync(new UnauthorizedAccessException());
        var legacy = new UserAnalyticsFunction(NullLogger<UserAnalyticsFunction>.Instance, null!, _auth, _journeys.Object);
        Assert.Equal(HttpStatusCode.Forbidden, (await legacy.TrackEvent(Request("user",
            body: new TrackEventRequest { EventType = "PageView" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await legacy.Heartbeat(Request("user",
            body: new LegacyJourneySessionRequest()))).StatusCode);
    }

    private HttpRequestData Request(string? role = null, string? outlet = null, object? body = null)
    {
        var services = new ServiceCollection();
        services.AddOptions<WorkerOptions>().Configure(options =>
            options.Serializer = new JsonObjectSerializer(new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var context = new Mock<FunctionContext>();
        context.SetupProperty(value => value.InstanceServices, services.BuildServiceProvider());
        var request = new Mock<HttpRequestData>(context.Object);
        var headers = new HttpHeadersCollection();
        if (role != null) headers.Add("Authorization", $"Bearer {_auth.GenerateJwtToken(UserId, "Test customer", role)}");
        if (outlet != null) headers.Add("X-Outlet-Id", outlet);
        var url = new Uri("http://localhost:7071/api/analytics/journey/dashboard");
        request.SetupGet(value => value.Headers).Returns(headers);
        request.SetupGet(value => value.Url).Returns(url);
        request.SetupGet(value => value.Query).Returns(System.Web.HttpUtility.ParseQueryString(url.Query));
        request.SetupGet(value => value.Body).Returns(new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
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
}
