using Cafe.Api.Helpers;
using Cafe.Api.Models;
using Cafe.Api.Services;
using Xunit;

namespace Cafe.Api.Tests;

public sealed class KotRenderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NormalAndDuplicateTicketsShowCustomerAndWaiterSeparately(bool duplicate)
    {
        var order = new Order {
            OrderType = "dine-in", DineInOrderSource = "waiter", TableNumber = "T1",
            Username = "Asha", CustomerName = "Asha", WaiterName = "Gourab Ghosh"
        };
        var renderer = new KotRenderService();
        var text = renderer.Render(order, isDuplicate: duplicate);
        Assert.Contains("Customer: Asha", text);
        Assert.Contains("Waiter: Gourab Ghosh", text);
        var cancellation = renderer.RenderCancellation(order, "Wrong item");
        Assert.Contains("Customer: Asha", cancellation);
        Assert.Contains("Waiter: Gourab Ghosh", cancellation);
    }

    [Fact]
    public void LegacyWaiterTicketDoesNotLabelWaiterAsCustomer()
    {
        var text = new KotRenderService().Render(new Order {
            DineInOrderSource = "waiter", Username = "waiter-login"
        });
        Assert.Contains("Customer: Walk-in customer", text);
        Assert.Contains("Waiter: waiter-login", text);
        Assert.DoesNotContain("Customer: waiter-login", text);
    }

    [Fact]
    public void NonDineInTicketsKeepExistingCustomerLabel()
    {
        var text = new KotRenderService().Render(new Order { Username = "Customer" });
        Assert.Contains("Customer: Customer", text);
        Assert.DoesNotContain("Waiter:", text);
    }

    [Theory]
    [InlineData("dine-in", "guest_dinein", "waiter", "staff", true)]
    [InlineData("dine-in", "staff", "waiter", "staff", true)]
    [InlineData("dine-in", "staff", "waiter", null, true)]
    [InlineData("dine-in", "customer", "waiter", "staff", false)]
    [InlineData("dine-in", "customer", "qr", "staff", false)]
    [InlineData("delivery", "customer", null, null, false)]
    [InlineData("pickup", "customer", null, null, false)]
    public void OrderRewardGuardsExcludeWaiterAccountsButPreserveCustomerRewards(
        string type, string userId, string? source, string? waiterId, bool expected)
    {
        Assert.Equal(expected, LoyaltyHelper.IsWaiterOrGuestDineInOrder(new Order {
            OrderType = type, UserId = userId, DineInOrderSource = source, WaiterUserId = waiterId
        }));
    }
}
