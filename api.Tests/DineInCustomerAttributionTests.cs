using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Azure.Core.Serialization;
using Cafe.Api.Functions;
using Cafe.Api.Helpers;
using Cafe.Api.Models;
using Cafe.Api.Repositories;
using Cafe.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using Xunit;

namespace Cafe.Api.Tests;

[Collection("Auth environment")]
public sealed class DineInCustomerAttributionTests
{
    private const string WaiterId = "111111111111111111111111";
    private const string CustomerId = "222222222222222222222222";
    private const string OutletId = "333333333333333333333333";
    private const string SessionId = "444444444444444444444444";
    private const string MenuItemId = "555555555555555555555555";
    private readonly Mock<IOrderRepository> _orders = new();
    private readonly Mock<IMenuRepository> _menu = new();
    private readonly Mock<IOutletRepository> _outlets = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly List<Order> _savedOrders = new();
    private readonly List<OutboxMessage> _messages = new();
    private readonly AuthService _auth;
    private readonly DineInFunction _function;
    private DineInSession? _session;

    public DineInCustomerAttributionTests()
    {
        var secret = Environment.GetEnvironmentVariable("Jwt__Secret");
        try
        {
            Environment.SetEnvironmentVariable("Jwt__Secret", new string('a', 64));
            _auth = new AuthService();
        }
        finally
        {
            Environment.SetEnvironmentVariable("Jwt__Secret", secret);
        }

        var database = new Mock<IMongoDatabase>();
        var tables = new Mock<IMongoCollection<CafeTable>> { DefaultValue = DefaultValue.Mock };
        tables.Setup(collection => collection.FindAsync(
                It.IsAny<FilterDefinition<CafeTable>>(),
                It.IsAny<FindOptions<CafeTable, CafeTable>>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                var cursor = new Mock<IAsyncCursor<CafeTable>>();
                cursor.SetupGet(value => value.Current).Returns(new[] {
                    new CafeTable { OutletId = OutletId, TableNumber = "T1" }
                });
                cursor.SetupSequence(value => value.MoveNextAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(true).ReturnsAsync(false);
                return Task.FromResult(cursor.Object);
            });
        database.Setup(value => value.GetCollection<CafeTable>("CafeTables", null)).Returns(tables.Object);
        var outbox = new Mock<IMongoCollection<OutboxMessage>> { DefaultValue = DefaultValue.Mock };
        outbox.Setup(collection => collection.InsertOneAsync(
                It.IsAny<OutboxMessage>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
            .Callback<OutboxMessage, InsertOneOptions, CancellationToken>((message, _, _) => _messages.Add(message))
            .Returns(Task.CompletedTask);
        database.Setup(value => value.GetCollection<OutboxMessage>("OutboxMessages", null)).Returns(outbox.Object);

        _outlets.Setup(value => value.GetOutletByIdAsync(OutletId, It.IsAny<bool>()))
            .ReturnsAsync(new Outlet { Id = OutletId, OutletName = "Cafe", IsActive = true });
        _users.Setup(value => value.GetUserByIdAsync(WaiterId)).ReturnsAsync(new User
        {
            Id = WaiterId, Username = "waiter-login", FirstName = "Gourab", LastName = "Ghosh"
        });
        _menu.Setup(value => value.GetMenuItemsByIdsAsync(It.IsAny<List<string>>(), OutletId))
            .ReturnsAsync(new List<CafeMenuItem> {
                new() { Id = MenuItemId, Name = "Tea", IsAvailable = true, DineInPrice = 100 }
            });
        _orders.Setup(value => value.GetActiveDineInSessionByTableAsync(OutletId, "T1"))
            .ReturnsAsync(() => _session);
        _orders.Setup(value => value.CreateDineInSessionAsync(It.IsAny<DineInSession>()))
            .ReturnsAsync((DineInSession session) => { session.Id = SessionId; return _session = session; });
        _orders.Setup(value => value.CreateOrderAsync(It.IsAny<Order>()))
            .ReturnsAsync((Order order) => { _savedOrders.Add(order); return order; });
        _orders.Setup(value => value.GetOrderByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((string id) => _savedOrders.SingleOrDefault(order => order.Id == id));
        _orders.Setup(value => value.UpdateDineInSessionAsync(It.IsAny<DineInSession>())).ReturnsAsync(true);
        _orders.Setup(value => value.GetNextDineInKotNumberAsync(OutletId)).ReturnsAsync("KOT-001");
        _function = new DineInFunction(
            _orders.Object, _menu.Object, _outlets.Object,
            new CafeTableRepository(database.Object, NullLogger<CafeTableRepository>.Instance),
            _users.Object, Mock.Of<IOfferRepository>(), Mock.Of<ILoyaltyRepository>(),
            Mock.Of<IOperationsRepository>(), null!, _auth,
            new OutboxService(database.Object, NullLogger<OutboxService>.Instance),
            null!, new ConfigurationBuilder().Build(), NullLoggerFactory.Instance);
    }

    [Theory]
    [InlineData("Asha")]
    [InlineData(null)]
    public async Task MultiRoleWaiterOrderStoresSeparateIdentitiesAndEarnsNoPoints(string? customerName)
    {
        var response = await PlaceOrder(customerName);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(_session);
        Assert.Null(_session.UserId);
        Assert.Equal(WaiterId, _session.WaiterUserId);
        Assert.Equal("Gourab Ghosh", _session.WaiterName);
        Assert.Equal(customerName, _session.CustomerName);
        Assert.Null(_session.CustomerPhone);
        var order = Assert.Single(_savedOrders);
        Assert.Equal("guest_dinein", order.UserId);
        Assert.Equal(customerName, order.CustomerName);
        Assert.Equal(WaiterId, order.WaiterUserId);
        Assert.Equal("Gourab Ghosh", order.WaiterName);
        Assert.Equal(customerName ?? "Walk-in customer", order.Username);
        Assert.Contains(_messages, message => message.EventType == "ThermalKotPrint");
        Assert.Equal(0, ReadBody(response).GetProperty("session").GetProperty("estimatedPointsToEarn").GetInt32());
        await Settle();
        Assert.DoesNotContain(_messages, message => message.EventType.StartsWith("Loyalty"));
    }

    [Fact]
    public async Task WaiterAddingRoundPreservesQrCustomerAndAwardsOnlyThatCustomer()
    {
        _session = new DineInSession {
            Id = SessionId, OutletId = OutletId, TableNumber = "T1",
            UserId = CustomerId, CustomerName = "QR customer", CustomerPhone = "9999999999"
        };
        var response = await PlaceOrder("Do not overwrite QR customer");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(CustomerId, _session.UserId);
        Assert.Equal("QR customer", _session.CustomerName);
        Assert.Equal("9999999999", _session.CustomerPhone);
        Assert.Equal(WaiterId, _session.WaiterUserId);
        Assert.Equal("Gourab Ghosh", _session.WaiterName);
        var order = Assert.Single(_savedOrders);
        Assert.Equal(CustomerId, order.UserId);
        Assert.Equal("QR customer", order.CustomerName);
        Assert.Equal(WaiterId, order.WaiterUserId);
        Assert.Equal(10, ReadBody(response).GetProperty("session").GetProperty("estimatedPointsToEarn").GetInt32());
        await Settle();
        var award = Assert.Single(_messages, message => message.EventType == "LoyaltyPointsAwardExact");
        using var payload = JsonDocument.Parse(award.Payload);
        Assert.Equal(CustomerId, payload.RootElement.GetProperty("UserId").GetString());
        Assert.Equal(10, payload.RootElement.GetProperty("Points").GetInt32());
    }

    [Fact]
    public async Task QrCustomerOrderStillOwnsItsSessionAndAwaitsWaiterApproval()
    {
        var request = Request(new CreateOrderRequest {
            OutletId = OutletId, TableNumber = "T1",
            Items = new() { new() { MenuItemId = MenuItemId, Quantity = 1 } }
        }, isWaiter: false);
        var response = await _function.PlaceDineInOrder(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(CustomerId, _session!.UserId);
        Assert.Equal("customer-login", _session.CustomerName);
        var order = Assert.Single(_savedOrders);
        Assert.Null(order.WaiterUserId);
        Assert.Null(order.WaiterName);
        Assert.Equal("awaiting_waiter_approval", order.Status);
        Assert.DoesNotContain(_messages, message => message.EventType == "ThermalKotPrint");
        Assert.Equal(10, LoyaltyHelper.GetDineInPointsToEarn(_session, _savedOrders));
    }

    [Fact]
    public async Task ExplicitStaffSessionStartDoesNotRegisterTheWaiterAsCustomer()
    {
        var response = await _function.StartSession(Request(new StartDineInSessionRequest {
            OutletId = OutletId, TableNumber = "T1", CustomerName = "Asha"
        }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(_session!.UserId);
        Assert.Equal("Asha", _session.CustomerName);
        Assert.Equal(WaiterId, _session.WaiterUserId);
        Assert.Equal("Gourab Ghosh", _session.WaiterName);
    }

    [Fact]
    public async Task EachRoundKeepsItsOwnWaiterWithoutChangingTheSessionOpeningWaiter()
    {
        await PlaceOrder("Asha");
        _users.Setup(value => value.GetUserByIdAsync(CustomerId)).ReturnsAsync(new User {
            Id = CustomerId, FirstName = "Second", LastName = "Waiter"
        });
        var response = await _function.PlaceDineInOrder(Request(new CreateOrderRequest {
            OutletId = OutletId, TableNumber = "T1",
            Items = new() { new() { MenuItemId = MenuItemId, Quantity = 1 } }
        }, waiterId: CustomerId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(WaiterId, _session!.WaiterUserId);
        Assert.Equal("Gourab Ghosh", _session.WaiterName);
        Assert.Null(_session.UserId);
        Assert.Equal("Asha", _savedOrders[1].CustomerName);
        Assert.Equal(CustomerId, _savedOrders[1].WaiterUserId);
        Assert.Equal("Second Waiter", _savedOrders[1].WaiterName);
        Assert.Equal(200, _session.GrandTotal);
        await Settle();
        Assert.DoesNotContain(_messages, message => message.EventType.StartsWith("Loyalty"));
    }

    [Fact]
    public async Task ClientCannotSpoofTheRecordedWaiter()
    {
        var response = await _function.PlaceDineInOrder(Request(new {
            outletId = OutletId, tableNumber = "T1", customerName = "Asha",
            waiterUserId = CustomerId, waiterName = "Forged",
            items = new[] { new { menuItemId = MenuItemId, quantity = 1 } }
        }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = Assert.Single(_savedOrders);
        Assert.Equal(WaiterId, order.WaiterUserId);
        Assert.Equal("Gourab Ghosh", order.WaiterName);
    }

    [CollectionDefinition("Auth environment", DisableParallelization = true)]
    public sealed class AuthEnvironmentCollection;

    [Fact]
    public async Task LegacyWaiterOwnedSessionDoesNotAwardSettlementPoints()
    {
        _session = new DineInSession {
            Id = SessionId, OutletId = OutletId, TableNumber = "T1", UserId = WaiterId,
            CustomerName = "waiter-login", GrandTotal = 100
        };
        var order = new Order {
            Id = MenuItemId, OrderType = "dine-in", DineInSessionId = SessionId,
            DineInOrderSource = "waiter", UserId = WaiterId, ApprovedByUserId = WaiterId,
            Subtotal = 100, Total = 100
        };
        _session.OrderIds.Add(order.Id);
        _savedOrders.Add(order);
        await Settle();
        Assert.DoesNotContain(_messages, message => message.EventType.StartsWith("Loyalty"));
    }

    [Fact]
    public async Task CustomerNameValidationRejectsOverlongNamesBeforePersisting()
    {
        var response = await PlaceOrder(new string('x', 101));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_savedOrders);
        Assert.Null(_session);
    }

    [Fact]
    public void IdentityFieldsSurviveMongoSerialization()
    {
        var order = new Order {
            CustomerName = "Asha", WaiterUserId = WaiterId, WaiterName = "Gourab Ghosh"
        };
        var savedOrder = BsonSerializer.Deserialize<Order>(order.ToBson());
        Assert.Equal("Asha", savedOrder.CustomerName);
        Assert.Equal(WaiterId, savedOrder.WaiterUserId);
        Assert.Equal("Gourab Ghosh", savedOrder.WaiterName);
        var session = new DineInSession {
            CustomerName = "Asha", WaiterUserId = WaiterId, WaiterName = "Gourab Ghosh"
        };
        var savedSession = BsonSerializer.Deserialize<DineInSession>(session.ToBson());
        Assert.Null(savedSession.UserId);
        Assert.Equal("Asha", savedSession.CustomerName);
        Assert.Equal(WaiterId, savedSession.WaiterUserId);
        Assert.Equal("Gourab Ghosh", savedSession.WaiterName);
    }

    private Task<HttpResponseData> PlaceOrder(string? customerName) =>
        _function.PlaceDineInOrder(Request(new CreateOrderRequest {
            OutletId = OutletId, TableNumber = "T1", CustomerName = customerName,
            Items = new() { new() { MenuItemId = MenuItemId, Quantity = 1 } }
        }));

    private async Task Settle()
    {
        Assert.NotNull(_session);
        _session.AmountPaid = _session.GrandTotal;
        // Exercise the shared finalizer used by both cashier payment entry and confirmation.
        var finalize = typeof(DineInFunction).GetMethod("FinalizeSessionPaymentAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.True(await (Task<bool>)finalize.Invoke(_function, new object[] { _session })!);
        Assert.Equal("paid", _session.Status);
    }

    private HttpRequestData Request(object body, bool isWaiter = true, string? waiterId = null)
    {
        var services = new ServiceCollection().AddSingleton<IOptions<WorkerOptions>>(
            Options.Create(new WorkerOptions {
                Serializer = new JsonObjectSerializer(new JsonSerializerOptions(JsonSerializerDefaults.Web))
            })).BuildServiceProvider();
        var context = new Mock<FunctionContext>();
        context.SetupProperty(value => value.InstanceServices, services);
        var request = new Mock<HttpRequestData>(context.Object);
        var token = _auth.GenerateJwtToken(isWaiter ? waiterId ?? WaiterId : CustomerId,
            isWaiter ? "waiter-login" : "customer-login", isWaiter ? "cashier" : "user",
            additionalRoles: isWaiter ? new[] { "waiter", "delivery-partner" } : null);
        request.SetupGet(value => value.Headers).Returns(new HttpHeadersCollection {
            { "Authorization", $"Bearer {token}" }
        });
        request.SetupGet(value => value.Body)
            .Returns(new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body))));
        request.Setup(value => value.CreateResponse()).Returns(() =>
        {
            var response = new Mock<HttpResponseData>(context.Object);
            response.SetupProperty(value => value.StatusCode);
            response.SetupProperty(value => value.Headers, new HttpHeadersCollection());
            response.SetupProperty(value => value.Body, new MemoryStream());
            return response.Object;
        });
        return request.Object;
    }

    private static JsonElement ReadBody(HttpResponseData response)
    {
        response.Body.Position = 0;
        using var document = JsonDocument.Parse(response.Body);
        return document.RootElement.Clone();
    }
}
