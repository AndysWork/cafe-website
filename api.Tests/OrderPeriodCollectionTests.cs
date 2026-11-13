using Cafe.Api.Helpers;
using Cafe.Api.Models;
using Xunit;

namespace Cafe.Api.Tests;

public class OrderPeriodCollectionTests
{
    [Fact]
    public void IncludesRecordedMethodsAcrossSourcesAndCountsSplitSessionOnce()
    {
        var orders = new[]
        {
            new Order { Id = "takeaway", OrderType = "pickup", PaymentStatus = "paid", PaymentMethod = "cash_at_counter", Total = 100 },
            new Order { Id = "phone", OrderSource = "phone", PaymentStatus = "paid", PaymentMethod = "upi-qr", Total = 80 },
            new Order { Id = "cod", PaymentStatus = "paid", PaymentMethod = "cod", Total = 25 },
            new Order { Id = "online", PaymentStatus = "paid", PaymentMethod = "razorpay", Total = 25 },
            new Order { Id = "unpaid", PaymentStatus = "pending", PaymentMethod = "upi-qr", Total = 500 },
            new Order { Id = "partial", PaymentStatus = "partially_paid", Total = 100 },
            new Order { Id = "refunded", PaymentStatus = "refunded", Total = 100 },
            new Order { Id = "round1", OutletId = "outlet", DineInSessionId = "session", PaymentStatus = "paid", Total = 100 },
            new Order { Id = "round2", OutletId = "outlet", DineInSessionId = "session", PaymentStatus = "paid", Total = 100 }
        };
        var session = new DineInSession { Id = "session", OutletId = "outlet", AmountPaid = 55,
            PaymentEntries = new() {
                new() { Id = "cash", Status = "confirmed", PaymentMethod = "cash_at_counter", Amount = 30 },
                new() { Id = "upi", Status = "confirmed", PaymentMethod = "upi-qr", Amount = 20 },
                new() { Id = "pending", Status = "pending", PaymentMethod = "upi-qr", Amount = 500 }
            } };

        var result = OrderPeriodCollection.Calculate(orders, new Dictionary<string, DineInSession> { ["session"] = session });

        Assert.Equal(100, result.Upi);
        Assert.Equal(155, result.Cash);
        Assert.Equal(25, result.Other);
        Assert.Equal(5, result.Unallocated);
        Assert.Equal(285, result.Total);
        Assert.Equal(2, result.UnknownAmountOrders);
        Assert.Equal(new[] { "cash_at_counter", "upi-qr" }, result.SessionMethods["session"]);
    }

    [Fact]
    public void MergedSessionsResolveToOneFinalSession()
    {
        var sessions = new Dictionary<string, DineInSession> {
            ["old"] = new() { Id = "old", OutletId = "outlet", MergedIntoSessionId = "current" },
            ["current"] = new() { Id = "current", OutletId = "outlet", AmountPaid = 50,
                PaymentEntries = new() { new() { Id = "payment", Status = "confirmed", PaymentMethod = "upi-qr", Amount = 50 } } }
        };
        var orders = new[] {
            new Order { OutletId = "outlet", DineInSessionId = "old" },
            new Order { OutletId = "outlet", DineInSessionId = "current" }
        };
        var result = OrderPeriodCollection.Calculate(orders, sessions);
        Assert.Equal(50, result.Total);
        Assert.Equal(new[] { "upi-qr" }, result.SessionMethods["old"]);
    }

    [Fact]
    public void MissingOrCrossOutletSessionsFailInsteadOfReturningPartialTotals()
    {
        var order = new Order { OutletId = "outlet", DineInSessionId = "session" };
        Assert.Throws<InvalidOperationException>(() => OrderPeriodCollection.Calculate(new[] { order }, new Dictionary<string, DineInSession>()));
        Assert.Throws<InvalidOperationException>(() => OrderPeriodCollection.Calculate(new[] { order },
            new Dictionary<string, DineInSession> { ["session"] = new() { OutletId = "other" } }));
    }
}
