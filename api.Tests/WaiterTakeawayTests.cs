using Cafe.Api.Functions;
using Cafe.Api.Helpers;
using Cafe.Api.Models;
using Cafe.Api.Services;
using Xunit;

namespace Cafe.Api.Tests;

public sealed class WaiterTakeawayTests
{
    private static Order Takeaway(string status = "pending", string paymentStatus = "unpaid") => new()
    {
        Id = "o1",
        OrderType = "pickup",
        Channel = "shop",
        TakeawaySource = "waiter",
        UserId = TakeawayOrderRules.GuestUserId,
        Username = "Ravi",
        CustomerName = "Ravi",
        WaiterUserId = "w1",
        WaiterName = "Gourab Ghosh",
        KotNumber = "KOT-007",
        Status = status,
        PaymentStatus = paymentStatus,
        Items = new List<OrderItem> { new() { Name = "Veg Roll", Quantity = 2 } }
    };

    [Fact]
    public void KotShowsParcelBannerTokenAndHandoverWaiter()
    {
        var text = new KotRenderService().Render(Takeaway());
        Assert.Contains("*** TAKEAWAY - PACK AS PARCEL ***", text);
        Assert.Contains("TOKEN: KOT-007", text);
        Assert.Contains("Hand over to: Gourab Ghosh", text);
        Assert.Contains("Customer: Ravi", text);
    }

    [Fact]
    public void DineInKotHasNoParcelBanner()
    {
        var text = new KotRenderService().Render(new Order { OrderType = "dine-in", TableNumber = "T1" });
        Assert.DoesNotContain("TAKEAWAY", text);
    }

    [Fact]
    public void InvoiceShowsPackagingChargeOnlyWhenApplied()
    {
        var order = Takeaway(paymentStatus: "paid");
        order.Subtotal = 100;
        order.PackagingCharge = 10;
        order.Total = 110;
        Assert.Contains("Packaging", new InvoiceRenderService().Render(order));
        order.PackagingCharge = 0;
        Assert.DoesNotContain("Packaging", new InvoiceRenderService().Render(order));
    }

    [Fact]
    public void WaiterTakeawayNeverEarnsLoyalty()
    {
        Assert.True(LoyaltyHelper.IsWaiterOrGuestDineInOrder(Takeaway()));
        Assert.False(LoyaltyHelper.IsWaiterOrGuestDineInOrder(new Order { OrderType = "pickup", UserId = "customer" }));
    }

    [Theory]
    [InlineData("cash", null, "cash_at_counter", null)]
    [InlineData("UPI", "UTR123", "upi-qr", null)]
    [InlineData("upi-qr", "", null, "UPI reference is required for UPI payments")]
    [InlineData("card", null, null, "Payment method must be cash or UPI")]
    public void PaymentValidation(string method, string? reference, string? expectedMethod, string? expectedError)
    {
        var (normalized, error) = TakeawayOrderRules.ValidatePayment(method, reference);
        Assert.Equal(expectedMethod, normalized);
        Assert.Equal(expectedError, error);
    }

    [Theory]
    [InlineData(null, "handover")]
    [InlineData("UPFRONT", "upfront")]
    [InlineData("later", null)]
    public void PaymentTimingNormalisation(string? timing, string? expected) =>
        Assert.Equal(expected, TakeawayOrderRules.NormalizePaymentTiming(timing));

    [Theory]
    [InlineData("preparing", "paid", "The kitchen has not packed this parcel yet")]
    [InlineData("ready", "unpaid", "Collect payment before handing over the parcel")]
    [InlineData("ready", "paid", null)]
    public void HandoverRequiresPackedAndPaid(string status, string payment, string? expected) =>
        Assert.Equal(expected, TakeawayOrderRules.GetHandoverBlockReason(Takeaway(status, payment)));

    [Fact]
    public void HandoverRejectsNonWaiterPickup()
    {
        var online = Takeaway("ready", "paid");
        online.TakeawaySource = null;
        Assert.Equal("Waiter takeaway order not found", TakeawayOrderRules.GetHandoverBlockReason(online));
    }

    [Theory]
    [InlineData("pending", "unpaid", null)]
    [InlineData("preparing", "unpaid", "Only takeaway orders the kitchen has not started can be cancelled")]
    [InlineData("pending", "paid", "Paid takeaway orders cannot be cancelled here; ask a manager to refund")]
    public void CancelRules(string status, string payment, string? expected) =>
        Assert.Equal(expected, TakeawayOrderRules.GetCancelBlockReason(Takeaway(status, payment)));

    [Theory]
    [InlineData("ready", "unpaid", null)]
    [InlineData("ready", "paid", "Payment is already recorded for this takeaway order")]
    [InlineData("delivered", "unpaid", "Payment can only be recorded for an active takeaway order")]
    public void PaymentRules(string status, string payment, string? expected) =>
        Assert.Equal(expected, TakeawayOrderRules.GetPaymentBlockReason(Takeaway(status, payment)));

    [Theory]
    [InlineData(15.555, 15.56)]
    [InlineData(-5, 0)]
    [InlineData(5000, 1000)]
    public void PackagingChargeIsClamped(decimal configured, decimal expected) =>
        Assert.Equal(expected, TakeawayOrderRules.GetPackagingCharge(
            new Outlet { Settings = new OutletSettings { PackagingCharge = configured } }));

    [Fact]
    public void PackingChargeDefaultsToZeroAndOrderingRespectsTakeawayToggle()
    {
        var outlet = new Outlet { IsActive = true, Settings = new OutletSettings { AcceptsTakeaway = false } };
        Assert.Equal(0, TakeawayOrderRules.GetPackagingCharge(new Outlet()));
        Assert.NotNull(OutletHelper.GetOrderingUnavailableReason(outlet, "pickup"));
    }

    [Fact]
    public void TakeawayUsesCounterPricing()
    {
        Assert.Equal(80, WaiterTakeawayFunction.TakeawayUnitPrice(
            new CafeMenuItem { ShopSellingPrice = 80, WebPrice = 90, DineInPrice = 100 }));
        Assert.Equal(90, WaiterTakeawayFunction.TakeawayUnitPrice(new CafeMenuItem { WebPrice = 90 }));
    }
}
