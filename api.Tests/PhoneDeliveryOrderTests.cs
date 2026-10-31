using System.Net;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using Xunit;

namespace Cafe.Api.Tests;

[Collection("Auth environment")]
public sealed class PhoneDeliveryOrderTests
{
    private const string OutletId = "222222222222222222222222";
    private const string UserId = "111111111111111111111111";
    private const string MenuId = "333333333333333333333333";
    private readonly Mock<IOrderRepository> _orders = new();
    private readonly Mock<IMenuRepository> _menu = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IStaffRepository> _staff = new();
    private readonly Mock<IOutletRepository> _outlets = new();
    private readonly Mock<IOperationsRepository> _operations = new();
    private readonly Mock<IPhoneDeliveryOrderRepository> _phone = new();
    private readonly Mock<OutboxService> _outbox;
    private readonly AuthService _auth;
    private readonly PhoneDeliveryOrderFunction _function;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public PhoneDeliveryOrderTests()
    {
        var previous = Environment.GetEnvironmentVariable("Jwt__Secret");
        try
        {
            Environment.SetEnvironmentVariable("Jwt__Secret", Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
            _auth = new AuthService();
        }
        finally { Environment.SetEnvironmentVariable("Jwt__Secret", previous); }
        _outbox = new Mock<OutboxService>(Mock.Of<IMongoDatabase>(), NullLogger<OutboxService>.Instance);
        _outbox.Setup(o => o.EnqueueOnceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>())).Returns(Task.CompletedTask);
        _function = new PhoneDeliveryOrderFunction(_orders.Object, _menu.Object, _users.Object, _staff.Object,
            _outlets.Object, _operations.Object, _phone.Object, _auth, _outbox.Object, NullLoggerFactory.Instance);
        _users.Setup(u => u.GetUserByIdAsync(UserId)).ReturnsAsync(new User
        {
            Id = UserId, FirstName = "Staff", LastName = "Recorder", AssignedOutlets = new() { OutletId }
        });
        _outlets.Setup(o => o.GetOutletByIdAsync(OutletId, It.IsAny<bool>()))
            .ReturnsAsync(new Outlet { Id = OutletId, IsActive = true });
        _menu.Setup(m => m.GetMenuItemsByIdsAsync(It.IsAny<List<string>>(), OutletId)).ReturnsAsync(new List<CafeMenuItem>
        {
            new()
            {
                Id = MenuId, Name = "Roll", IsAvailable = true, ShopSellingPrice = 80, WebPrice = 90,
                Variants = new() { new() { VariantName = "Large", Price = 120 } },
                AddOns = new() { new() { Name = "Cheese", Price = 15, IsActive = true } }
            }
        });
        _operations.Setup(o => o.GetActiveDeliveryZonesAsync(OutletId)).ReturnsAsync(new List<DeliveryZone>
        {
            new()
            {
                OutletId = OutletId,
                ZoneName = "Core",
                MinDistance = 0,
                MaxDistance = 5,
                DeliveryFee = 25,
                FreeDeliveryAbove = 500,
                EstimatedMinutes = 30,
                IsActive = true
            }
        });
        _operations.Setup(o => o.CalculateDeliveryFeeAsync(OutletId, It.IsAny<decimal>())).ReturnsAsync(25m);
        _orders.Setup(o => o.GetNextDineInKotNumberAsync(OutletId)).ReturnsAsync("KOT-123");
        _orders.Setup(o => o.CreateOrderAsync(It.IsAny<Order>())).ReturnsAsync((Order o) => o);
        _phone.Setup(o => o.MarkPhoneEffectsQueuedAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
    }

    private static CreatePhoneDeliveryOrderRequest Payload(string paymentMethod = "cod") => new()
    {
        OutletId = OutletId, CustomerName = "Guest Person", PhoneNumber = "9876543210",
        DeliveryAddress = "10 Main Road", DeliveryLocationUrl = "https://www.google.com/maps?q=22.5,88.3&z=18",
        Items = new() { new() { MenuItemId = MenuId, Quantity = 2 } }, PaymentMethod = paymentMethod
    };

    [Fact]
    public async Task QuoteUsesShopPricingAndServerFeeWithoutMutatingOrRequiringIdempotency()
    {
        var response = await _function.Quote(Request(Payload(), key: null));
        var body = await Read(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(160, body.GetProperty("subtotal").GetDecimal());
        Assert.Equal(25, body.GetProperty("deliveryFee").GetDecimal());
        Assert.Equal(185, body.GetProperty("total").GetDecimal());
        Assert.Equal(0, body.GetProperty("tax").GetDecimal());
        _orders.Verify(o => o.CreateOrderAsync(It.IsAny<Order>()), Times.Never);
        _orders.Verify(o => o.GetNextDineInKotNumberAsync(It.IsAny<string>()), Times.Never);
        _outbox.Verify(o => o.EnqueueOnceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public async Task ManualDistanceAndFeeOverrideAutoValuesAndPersistOnCreate()
    {
        var payload = Payload("cash_at_counter") with
        {
            ManualApproximateDistanceKm = 3.1,
            ManualDeliveryFee = 55,
            ExpectedTotal = 215
        };

        var quote = await Read(await _function.Quote(Request(payload)));
        Assert.True(quote.GetProperty("manualOverrideApplied").GetBoolean());
        Assert.Equal(55, quote.GetProperty("deliveryFee").GetDecimal());
        Assert.Equal(3.1, quote.GetProperty("approximateDistanceKm").GetDouble(), 1);

        var created = await Read(await _function.Create(Request(payload)));
        var order = created.GetProperty("order");
        Assert.Equal(55, order.GetProperty("deliveryFee").GetDecimal());
        Assert.Equal(3.1, order.GetProperty("deliveryDistanceKm").GetDouble(), 1);
        Assert.Equal(55, order.GetProperty("manualDeliveryFeeOverride").GetDecimal());
        Assert.Equal(3.1, order.GetProperty("manualDeliveryDistanceKmOverride").GetDouble(), 1);
    }

    [Fact]
    public async Task RejectsPartialManualOverrideValues()
    {
        var feeOnly = Payload() with { ManualDeliveryFee = 40 };
        var distanceOnly = Payload() with { ManualApproximateDistanceKm = 2.8 };
        Assert.Equal(HttpStatusCode.BadRequest, (await _function.Quote(Request(feeOnly, key: null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _function.Quote(Request(distanceOnly, key: null))).StatusCode);
    }

    [Fact]
    public async Task QuoteAndCreateUseExactlyTheSameVariantAddonAndFeePrices()
    {
        var payload = Payload("cash_at_counter") with
        {
            Items = new() { new() { MenuItemId = MenuId, Quantity = 2, SelectedVariantName = "large", SelectedAddOnNames = new() { "Cheese" } } },
            ExpectedTotal = 295
        };
        var quote = await Read(await _function.Quote(Request(payload)));
        var created = await _function.Create(Request(payload));
        var order = (await Read(created)).GetProperty("order");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(quote.GetProperty("total").GetDecimal(), order.GetProperty("total").GetDecimal());
        Assert.Equal(270, order.GetProperty("subtotal").GetDecimal());
        Assert.Equal("paid", order.GetProperty("paymentStatus").GetString());
        Assert.Equal("phone", order.GetProperty("orderSource").GetString());
        Assert.Equal("shop", order.GetProperty("channel").GetString());
        Assert.StartsWith("/track-order/", order.GetProperty("trackingPath").GetString());
        Assert.Equal(payload.DeliveryLocationUrl, order.GetProperty("deliveryLocationUrl").GetString());
        _orders.Verify(o => o.CreateOrderAsync(It.Is<Order>(o => o.UserId == PhoneDeliveryOrderRules.GuestUserId
            && o.RecordedByUserId == UserId && o.PaymentRecordedByUserId == UserId && o.PaymentRecordedAt != null
            && o.WaiterUserId == null && o.PhoneTrackingToken!.Length == 43)), Times.Once);
    }

    [Fact]
    public async Task ChangedExpectedTotalRejectsWithoutRecordingPaymentOrOrder()
    {
        var response = await _function.Create(Request(Payload("cash_at_counter") with { ExpectedTotal = 184 }));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        _orders.Verify(o => o.CreateOrderAsync(It.IsAny<Order>()), Times.Never);
    }

    [Fact]
    public async Task PrepaidUpiRequiresReferenceButQuoteDoesNot()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _function.Create(Request(Payload("upi-qr")))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _function.Quote(Request(Payload("upi-qr")))).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await _function.Create(Request(Payload("upi-qr") with { UpiReference = "BANK-123" }))).StatusCode);
        _orders.Verify(o => o.CreateOrderAsync(It.Is<Order>(o => o.UpiConfirmedBy == UserId
            && o.UpiReference == "BANK-123" && o.PaymentStatus == "paid")), Times.Once);
    }

    [Fact]
    public async Task CodStartsUnpaidAndCreationRequiresIdempotencyKey()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _function.Create(Request(Payload(), key: null))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _function.Create(Request(Payload()))).StatusCode);
        _orders.Verify(o => o.CreateOrderAsync(It.Is<Order>(o => o.PaymentStatus == "pending"
            && o.PaymentRecordedAt == null && o.PaymentRecordedByUserId == null)), Times.Once);
    }

    [Fact]
    public async Task CaseInsensitivePaymentValidationCannotTreatCodAsPrepaidOrSkipUpiReference()
    {
        Assert.Equal(HttpStatusCode.Created, (await _function.Create(Request(Payload("COD")))).StatusCode);
        _orders.Verify(o => o.CreateOrderAsync(It.Is<Order>(o => o.PaymentMethod == "cod" && o.PaymentStatus == "pending")), Times.Once);
        Assert.Equal(HttpStatusCode.BadRequest, (await _function.Create(Request(Payload("UPI-QR")))).StatusCode);
    }

    [Fact]
    public async Task StaffEndpointsRequireLoginAndInvalidDestinationNeverCreates()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _function.Create(Request(Payload(), role: null))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _function.Quote(Request(Payload(), role: null))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _function.List(Request(null, role: null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _function.Create(Request(Payload() with
        {
            DeliveryLocationUrl = "https://example.com/private",
        }))).StatusCode);
        _orders.Verify(o => o.CreateOrderAsync(It.IsAny<Order>()), Times.Never);
    }

    [Fact]
    public async Task PublicTrackingHandlesDatabaseFailureWithoutCachingOrLeakingExceptionDetails()
    {
        var id = "555555555555555555555555";
        _orders.Setup(o => o.GetOrderByIdAsync(id)).ThrowsAsync(new InvalidOperationException("PRIVATE DATABASE DETAILS"));
        var response = await _function.Track(Request(null, role: null,
            url: $"https://localhost/api/public/phone-orders/{id}/tracking?token={PhoneDeliveryOrderRules.NewTrackingToken()}"), id);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("no-store", response.Headers.GetValues("Cache-Control").Single());
        Assert.DoesNotContain("PRIVATE", (await Read(response)).GetRawText());
    }

    [Fact]
    public void IdempotencyIdentityIsStableAndScopedToActorOutletAndKey()
    {
        var id = PhoneDeliveryOrderRules.IdempotentOrderId(UserId, OutletId, "key");
        Assert.True(ObjectId.TryParse(id, out _));
        Assert.Equal(id, PhoneDeliveryOrderRules.IdempotentOrderId(UserId, OutletId, "key"));
        Assert.NotEqual(id, PhoneDeliveryOrderRules.IdempotentOrderId("other-user", OutletId, "key"));
        Assert.NotEqual(id, PhoneDeliveryOrderRules.IdempotentOrderId(UserId, "other-outlet", "key"));
        Assert.NotEqual(id, PhoneDeliveryOrderRules.IdempotentOrderId(UserId, OutletId, "other-key"));
    }

    [Theory]
    [InlineData("pending", "pending", "cod", false)]
    [InlineData("preparing", "pending", "cod", false)]
    [InlineData("ready", "pending", "cod", false)]
    [InlineData("out-for-delivery", "paid", "cod", true)]
    [InlineData("delivered", "paid", "upi-qr", true)]
    public void AssignmentDoesNotRequireReadyButCannotReassignAfterPickup(string status, string payment, string method, bool blocked)
    {
        var order = new Order { OrderSource = "phone", Status = status, PaymentStatus = payment, PaymentMethod = method, OutletId = OutletId };
        var partner = new DeliveryPartner { Id = "p1", OutletId = OutletId, IsActive = true, Status = "available", CodAllowed = true };
        Assert.Equal(blocked, PhoneDeliveryOrderRules.AssignmentBlockReason(order, partner) != null);
        partner.OutletId = "other";
        Assert.NotNull(PhoneDeliveryOrderRules.AssignmentBlockReason(order, partner));
    }

    [Theory]
    [InlineData("preparing", "cod", "pending", true)]
    [InlineData("ready", "cod", "pending", false)]
    [InlineData("ready", "cash_at_counter", "paid", false)]
    [InlineData("ready", "upi-qr", "paid", false)]
    [InlineData("ready", "upi-qr", "pending", true)]
    [InlineData("ready", "cash_at_counter", "refunded", true)]
    public void PickupRequiresReadyAssignedSameOutletAndValidPrepayment(string status, string method, string payment, bool blocked)
    {
        var order = new Order { OrderSource = "phone", Status = status, PaymentMethod = method, PaymentStatus = payment,
            OutletId = OutletId, DeliveryPartnerId = "p1" };
        var partner = new DeliveryPartner { Id = "p1", OutletId = OutletId };
        Assert.Equal(blocked, PhoneDeliveryOrderRules.PickupBlockReason(order, partner) != null);
        partner.Id = "different-partner";
        Assert.NotNull(PhoneDeliveryOrderRules.PickupBlockReason(order, partner));
        partner.Id = "p1";
        partner.OutletId = "different-outlet";
        Assert.NotNull(PhoneDeliveryOrderRules.PickupBlockReason(order, partner));
    }

    [Fact]
    public void CodAssignmentsHonorPartnerCollectionPermission()
    {
        var order = new Order { Status = "ready", OutletId = OutletId, PaymentMethod = "cod" };
        var partner = new DeliveryPartner { IsActive = true, Status = "available", OutletId = OutletId, CodAllowed = false };
        Assert.NotNull(PhoneDeliveryOrderRules.AssignmentBlockReason(order, partner));
        order.PaymentMethod = "cash_at_counter";
        Assert.Null(PhoneDeliveryOrderRules.AssignmentBlockReason(order, partner));
    }

    private DeliveryPartnerFunction PartnerFunctions() => new(_operations.Object, _orders.Object, _phone.Object,
        _users.Object, _staff.Object, _outlets.Object, null!, null!, null!, _auth, _outbox.Object, NullLoggerFactory.Instance);

    [Theory]
    [InlineData("pending")]
    [InlineData("preparing")]
    [InlineData("ready")]
    public async Task AssignmentEndpointAllowsEarlyAssignmentWithoutDispatchingOrChangingKitchenStatus(string status)
    {
        var order = new Order { Id = "555555555555555555555555", OrderSource = "phone", OrderType = "delivery",
            Channel = "shop", OutletId = OutletId, Status = status, DeliveryLocationUrl = "https://maps.app.goo.gl/Abc" };
        _orders.Setup(o => o.GetOrderByIdAsync(order.Id)).ReturnsAsync(order);
        _operations.Setup(o => o.AssignDeliveryPartnerAsync("p1", order.Id)).ReturnsAsync(true);
        _operations.Setup(o => o.GetDeliveryPartnerByIdAsync("p1")).ReturnsAsync(new DeliveryPartner { Id = "p1", OutletId = OutletId });
        var response = await PartnerFunctions().AssignDeliveryPartner(Request(
            new { orderId = order.Id, deliveryPartnerId = "p1" }, url: "https://localhost/api/manage/delivery-partners/assign?channel=shop"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(status, order.Status);
        _orders.Verify(o => o.UpdateOrderAsync(It.IsAny<Order>()), Times.Never);
        _orders.Verify(o => o.UpdateOrderStatusAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _operations.Verify(o => o.AssignDeliveryPartnerAsync("p1", order.Id), Times.Once);
    }

    [Fact]
    public async Task AssignmentEndpointRejectsManagerOutsideOrderOutletBeforeMutation()
    {
        var order = new Order { Id = "555555555555555555555555", OrderSource = "phone",
            OrderType = "delivery", Channel = "shop", OutletId = "444444444444444444444444", Status = "pending" };
        _orders.Setup(o => o.GetOrderByIdAsync(order.Id)).ReturnsAsync(order);
        var response = await PartnerFunctions().AssignDeliveryPartner(Request(
            new { orderId = order.Id, deliveryPartnerId = "p1" }));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _operations.Verify(o => o.AssignDeliveryPartnerAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ManagerCannotReadAnotherOutletPartnerDashboard()
    {
        _operations.Setup(o => o.GetDeliveryPartnerByIdAsync("p1")).ReturnsAsync(new DeliveryPartner
        {
            Id = "p1", OutletId = "444444444444444444444444"
        });
        var response = await PartnerFunctions().GetDeliveryPartnerDashboard(
            Request(null, url: "https://localhost/api/partner/delivery/dashboard?partnerId=p1"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _operations.Verify(o => o.GetActiveOrdersForPartnerAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("preparing")]
    public async Task PartnerPickupEndpointRefusesPhoneOrderBeforeReady(string status)
    {
        var order = new Order { Id = "555555555555555555555555", OrderSource = "phone", OrderType = "delivery",
            Channel = "shop", OutletId = OutletId, Status = status, DeliveryPartnerId = "p1", PaymentMethod = "cod" };
        _orders.Setup(o => o.GetOrderByIdAsync(order.Id)).ReturnsAsync(order);
        _operations.Setup(o => o.GetDeliveryPartnerByUserIdAsync(UserId)).ReturnsAsync(new DeliveryPartner { Id = "p1", OutletId = OutletId });
        var response = await PartnerFunctions().PickupAssignedOrder(Request(null, "delivery-partner", key: null), order.Id);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        _phone.Verify(p => p.TryPickupPhoneDeliveryAsync(It.IsAny<string>(), It.IsAny<DeliveryPartner>()), Times.Never);
        Assert.Equal(status, order.Status);
    }

    [Fact]
    public async Task PhoneSelfClaimEndpointIsForbiddenEvenForSecondaryPartnerRole()
    {
        var order = new Order { Id = "555555555555555555555555", OrderSource = "phone", OrderType = "delivery",
            Channel = "shop", OutletId = OutletId, Status = "ready" };
        _orders.Setup(o => o.GetOrderByIdAsync(order.Id)).ReturnsAsync(order);
        _operations.Setup(o => o.GetDeliveryPartnerByUserIdAsync(UserId)).ReturnsAsync(new DeliveryPartner { Id = "p1", OutletId = OutletId });
        var response = await PartnerFunctions().AcceptDeliveryOrder(Request(null, "waiter", key: null,
            extraRoles: new[] { "delivery-partner" }), order.Id);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _operations.Verify(o => o.TryAssignUnassignedDeliveryPartnerAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ListReturnsAuthorizedOrderWithTrackingPathButNoRawSecretProperties()
    {
        var token = PhoneDeliveryOrderRules.NewTrackingToken();
        _phone.Setup(p => p.GetPhoneDeliveryOrdersAsync(OutletId, null)).ReturnsAsync(new List<Order>
        {
            new() { Id = "555555555555555555555555", OutletId = OutletId, OrderSource = "phone",
                OrderType = "delivery", Channel = "shop", PhoneTrackingToken = token,
                PhoneRequestHash = "PRIVATE HASH", DeliveryLocationUrl = "https://maps.app.goo.gl/Abc" }
        });
        var response = await _function.List(Request(null, url: $"https://localhost/api/phone-orders?outletId={OutletId}"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var order = (await Read(response)).GetProperty("orders")[0];
        Assert.Equal("delivery", order.GetProperty("orderType").GetString());
        Assert.Equal("shop", order.GetProperty("channel").GetString());
        Assert.EndsWith($"?token={token}", order.GetProperty("trackingPath").GetString());
        Assert.False(order.TryGetProperty("phoneTrackingToken", out _));
        Assert.False(order.TryGetProperty("phoneRequestHash", out _));
        var other = await _function.List(Request(null,
            url: "https://localhost/api/phone-orders?outletId=444444444444444444444444"));
        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
    }

    [Fact]
    public async Task DurableReplayReturnsSameTrackingLinkWithoutRepricingOrCreating()
    {
        var payload = Payload();
        var id = PhoneDeliveryOrderRules.IdempotentOrderId(UserId, OutletId, "request-key");
        var existing = new Order
        {
            Id = id, OrderSource = "phone", OutletId = OutletId, PhoneEffectsQueued = true,
            PhoneTrackingToken = PhoneDeliveryOrderRules.NewTrackingToken(), Total = 185,
            PhoneRequestHash = IdempotencyService.ComputeRequestHash(JsonSerializer.Serialize(payload))
        };
        _orders.Setup(o => o.GetOrderByIdAsync(id)).ReturnsAsync(existing);
        var response = await _function.Create(Request(payload));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = (await Read(response)).GetProperty("order");
        Assert.Equal($"/track-order/{id}?token={existing.PhoneTrackingToken}", order.GetProperty("trackingPath").GetString());
        _menu.Verify(m => m.GetMenuItemsByIdsAsync(It.IsAny<List<string>>(), It.IsAny<string>()), Times.Never);
        _orders.Verify(o => o.CreateOrderAsync(It.IsAny<Order>()), Times.Never);
        Assert.Equal(HttpStatusCode.Conflict,
            (await _function.Create(Request(payload with { CustomerName = "Different guest" }))).StatusCode);
    }

    [Fact]
    public async Task ReplayRepairsOutboxAfterInterruptedCreationWithoutCreatingAgain()
    {
        var payload = Payload();
        var id = PhoneDeliveryOrderRules.IdempotentOrderId(UserId, OutletId, "request-key");
        _orders.Setup(o => o.GetOrderByIdAsync(id)).ReturnsAsync(new Order
        {
            Id = id, OrderSource = "phone", OutletId = OutletId,
            PhoneTrackingToken = PhoneDeliveryOrderRules.NewTrackingToken(),
            PhoneRequestHash = IdempotencyService.ComputeRequestHash(JsonSerializer.Serialize(payload))
        });
        Assert.Equal(HttpStatusCode.Created, (await _function.Create(Request(payload))).StatusCode);
        _outbox.Verify(o => o.EnqueueOnceAsync("ThermalKotPrint", id, It.IsAny<object>()), Times.Once);
        _outbox.Verify(o => o.EnqueueOnceAsync("OrderNotificationKitchen", id, It.IsAny<object>()), Times.Once);
        _phone.Verify(p => p.MarkPhoneEffectsQueuedAsync(id), Times.Once);
        _orders.Verify(o => o.CreateOrderAsync(It.IsAny<Order>()), Times.Never);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("waiter")]
    [InlineData("cashier")]
    [InlineData("delivery-partner")]
    public async Task NonManagersCannotCreateQuoteOrList(string role)
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await _function.Create(Request(Payload(), role))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _function.Quote(Request(Payload(), role))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _function.List(Request(null, role))).StatusCode);
    }

    [Fact]
    public async Task SecondaryManagerRoleWorksAndOutletScopeRemainsMandatory()
    {
        Assert.Equal(HttpStatusCode.OK, (await _function.Quote(Request(Payload(), "waiter", extraRoles: new[] { "manager" }))).StatusCode);
        var other = Payload() with { OutletId = "444444444444444444444444" };
        Assert.Equal(HttpStatusCode.Forbidden, (await _function.Quote(Request(other))).StatusCode);
        _phone.Setup(p => p.GetPhoneDeliveryOrdersAsync(OutletId, null)).ReturnsAsync(new List<Order>());
        Assert.Equal(HttpStatusCode.OK, (await _function.List(Request(null, url: $"https://localhost/api/phone-orders?outletId={OutletId}"))).StatusCode);
        _phone.Verify(p => p.GetPhoneDeliveryOrdersAsync(OutletId, null), Times.Once);
    }

    [Fact]
    public async Task PublicTrackingIsTokenOnlyMinimalAndNeverCacheable()
    {
        var id = "555555555555555555555555";
        var token = PhoneDeliveryOrderRules.NewTrackingToken();
        _orders.Setup(o => o.GetOrderByIdAsync(id)).ReturnsAsync(new Order
        {
            Id = id, OrderSource = "phone", PhoneTrackingToken = token, Total = 185,
            DeliveryAddress = "PRIVATE ADDRESS", PhoneNumber = "PRIVATE PHONE", CustomerName = "PRIVATE CUSTOMER",
            RecordedByName = "PRIVATE STAFF", PhoneRequestHash = "PRIVATE HASH", DeliveryLocationUrl = "PRIVATE MAPS"
        });
        var response = await _function.Track(Request(null, role: null, url: $"https://localhost/api/public/phone-orders/{id}/tracking?token={token}"), id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.GetValues("Cache-Control").Single());
        var body = await Read(response);
        Assert.Equal(id, body.GetProperty("orderId").GetString());
        Assert.DoesNotContain("PRIVATE", body.GetRawText());
        Assert.DoesNotContain(token, body.GetRawText());
        var forbiddenFields = new[] { "customerName", "phoneNumber", "deliveryAddress", "deliveryLocationUrl", "recordedByName", "phoneTrackingToken", "trackingPath" };
        foreach (var field in forbiddenFields) Assert.False(body.TryGetProperty(field, out _));
        var invalid = await _function.Track(Request(null, role: null,
            url: $"https://localhost/api/public/phone-orders/{id}/tracking?token={PhoneDeliveryOrderRules.NewTrackingToken()}"), id);
        Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);
        Assert.Contains("no-store", invalid.Headers.GetValues("Cache-Control").Single());
    }

    [Theory]
    [InlineData("https://maps.app.goo.gl/Abc123", true)]
    [InlineData("https://goo.gl/maps/Abc123", true)]
    [InlineData("https://www.google.com/maps?q=22,88", true)]
    [InlineData("https://maps.google.com/?q=22,88", true)]
    [InlineData("https://www.google.co.in/maps/place/Cafe", true)]
    [InlineData("http://maps.app.goo.gl/Abc123", false)]
    [InlineData("https://maps.app.goo.gl.evil.example/Abc123", false)]
    [InlineData("https://www.google.com.evil.example/maps", false)]
    [InlineData("https://www.google.com/url?q=https://evil.example", false)]
    [InlineData("https://user@maps.app.goo.gl/Abc123", false)]
    [InlineData("https://127.0.0.1/maps", false)]
    [InlineData("https://www.google.com:8443/maps", false)]
    [InlineData("javascript:alert(1)", false)]
    public void MapsValidationUsesExplicitHttpsHostAndPathAllowlist(string url, bool expected) =>
        Assert.Equal(expected, PhoneDeliveryOrderRules.IsValidMapsUrl(url));

    [Fact]
    public void TrackingSecretsPersistButNeverSerializeOnStandardOrders()
    {
        var token = PhoneDeliveryOrderRules.NewTrackingToken();
        var order = new Order { PhoneTrackingToken = token, PhoneRequestHash = "hidden" };
        Assert.DoesNotContain(token, JsonSerializer.Serialize(order));
        Assert.DoesNotContain("hidden", JsonSerializer.Serialize(order));
        var restored = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<Order>(order.ToBson());
        Assert.Equal(token, restored.PhoneTrackingToken);
        Assert.True(PhoneDeliveryOrderRules.IsValidTrackingToken(token, restored.PhoneTrackingToken));
        Assert.False(PhoneDeliveryOrderRules.IsValidTrackingToken(token, new string('A', 43)));
        Assert.False(PhoneDeliveryOrderRules.IsValidTrackingToken(token, null));
        Assert.NotEqual(token, PhoneDeliveryOrderRules.NewTrackingToken());
    }

    [Theory]
    [InlineData("pending", "ready", false)]
    [InlineData("ready", "preparing", true)]
    [InlineData("ready", "delivered", true)]
    [InlineData("ready", "out-for-delivery", true)]
    [InlineData("ready", "served", true)]
    [InlineData("out-for-delivery", "ready", true)]
    [InlineData("delivered", "pending", true)]
    public void KitchenAndGeneralStatusCannotBypassPartnerLifecycle(string current, string next, bool blocked) =>
        Assert.Equal(blocked, PhoneDeliveryOrderRules.WorkflowBlockReason(new Order { OrderSource = "phone", Status = current }, next) != null);

    [Fact]
    public void PhoneKotInvoiceAndLoyaltyUseGuestDeliverySemantics()
    {
        var order = new Order
        {
            OrderSource = "phone", OrderType = "delivery", CustomerName = "Guest Person", Username = "Guest Person",
            PhoneNumber = "9876543210", DeliveryAddress = "10 Main Road", DeliveryFee = 25,
            UserId = PhoneDeliveryOrderRules.GuestUserId, PaymentMethod = "cod", KotNumber = "KOT-123"
        };
        var kot = new KotRenderService().Render(order);
        Assert.Contains("PHONE DELIVERY - PACK FOR DELIVERY", kot);
        Assert.Contains("Customer: Guest Person", kot);
        Assert.DoesNotContain("Hand over to:", kot);
        Assert.True(LoyaltyHelper.IsWaiterOrGuestDineInOrder(order));
        var invoice = new InvoiceRenderService().Render(order);
        Assert.Contains("Delivery Fee", invoice);
        Assert.Contains("Cash on Delivery", invoice);
        Assert.Contains(order.DeliveryAddress, invoice);
    }

    [Fact]
    public async Task SharedBuilderRejectsInvalidQuantitiesAndUnknownVariants()
    {
        Assert.NotNull((await ShopOrderItemBuilder.BuildAsync(_menu.Object,
            new() { new() { MenuItemId = MenuId, Quantity = -1 } }, OutletId)).Error);
        Assert.NotNull((await ShopOrderItemBuilder.BuildAsync(_menu.Object,
            new() { new() { MenuItemId = MenuId, Quantity = 1, SelectedVariantName = "Missing" } }, OutletId)).Error);
        Assert.NotNull((await ShopOrderItemBuilder.BuildAsync(_menu.Object,
            new() { new() { MenuItemId = MenuId, Quantity = 1, SelectedAddOnNames = new() { "Missing" } } }, OutletId)).Error);
    }

    private HttpRequestData Request(object? payload, string? role = "manager", string? key = "request-key",
        string url = "https://localhost/api/phone-orders", IEnumerable<string>? extraRoles = null)
    {
        var services = new ServiceCollection();
        services.AddOptions<WorkerOptions>().Configure(o => o.Serializer = new JsonObjectSerializer(JsonOptions));
        var context = new Mock<FunctionContext>();
        context.SetupProperty(c => c.InstanceServices, services.BuildServiceProvider());
        var request = new Mock<HttpRequestData>(context.Object);
        var headers = new HttpHeadersCollection();
        if (role != null) headers.Add("Authorization", $"Bearer {_auth.GenerateJwtToken(UserId, "staff", role, additionalRoles: extraRoles)}");
        if (key != null) headers.Add("Idempotency-Key", key);
        request.SetupGet(r => r.Headers).Returns(headers);
        request.SetupGet(r => r.Url).Returns(new Uri(url));
        request.SetupGet(r => r.Query).Returns(System.Web.HttpUtility.ParseQueryString(new Uri(url).Query));
        request.SetupGet(r => r.Body).Returns(new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions))));
        request.Setup(r => r.CreateResponse()).Returns(() =>
        {
            var response = new Mock<HttpResponseData>(context.Object);
            response.SetupProperty(r => r.StatusCode, HttpStatusCode.OK);
            response.SetupProperty(r => r.Headers, new HttpHeadersCollection());
            response.SetupProperty(r => r.Body, new MemoryStream());
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
