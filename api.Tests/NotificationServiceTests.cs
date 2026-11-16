using Cafe.Api.Models;
using Cafe.Api.Repositories;
using Cafe.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Moq;
using Xunit;

namespace Cafe.Api.Tests;

public sealed class NotificationServiceTests
{
    private const string UserId = "111111111111111111111111";
    private const string OrderId = "222222222222222222222222";
    private readonly Mock<INotificationRepository> _repository = new(MockBehavior.Strict);
    private readonly NotificationService _service;

    public NotificationServiceTests()
    {
        var database = new Mock<IMongoDatabase>();
        database.Setup(db => db.GetCollection<User>("Users", It.IsAny<MongoCollectionSettings>()))
            .Returns(Mock.Of<IMongoCollection<User>>());
        var push = new WebPushService(database.Object, NullLogger<WebPushService>.Instance);
        _service = new NotificationService(_repository.Object, push, NullLogger<NotificationService>.Instance);
    }

    [Theory]
    [InlineData("guest_dinein")]
    [InlineData("guest_takeaway")]
    [InlineData("guest_phone_delivery")]
    [InlineData("not-an-account")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task NonAccountRecipientsAreSkippedBeforeLookingUpPreferences(string? recipient)
    {
        Assert.False(await _service.SendAsync(recipient!, "order_status", "Ready", "Order ready"));
        _repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GuestDineInOrderStatusDoesNotAccessNotificationStorage()
    {
        await _service.SendOrderStatusNotificationAsync(
            new Order { Id = OrderId, UserId = "guest_dinein", OrderType = "dine-in" }, "ready");
        _repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RegisteredDineInCustomerStillReceivesStatusNotification()
    {
        _repository.Setup(repo => repo.GetNotificationPreferencesAsync(UserId))
            .ReturnsAsync(new NotificationPreferences());
        _repository.Setup(repo => repo.CreateNotificationAsync(It.IsAny<AppNotification>()))
            .Returns(Task.CompletedTask);

        await _service.SendOrderStatusNotificationAsync(
            new Order { Id = OrderId, UserId = UserId, OrderType = "dine-in" }, "ready");

        _repository.Verify(repo => repo.GetNotificationPreferencesAsync(UserId), Times.Once);
        _repository.Verify(repo => repo.CreateNotificationAsync(It.Is<AppNotification>(notification =>
            notification.UserId == UserId && notification.Type == "order_status" &&
            notification.ActionUrl == "/orders" && notification.CreatedAtIsUtc == true &&
            notification.Data!["orderId"] == OrderId && notification.Data["status"] == "ready")), Times.Once);
        _repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RegisteredUserPreferencesStillSuppressDisabledOrderNotifications()
    {
        _repository.Setup(repo => repo.GetNotificationPreferencesAsync(UserId))
            .ReturnsAsync(new NotificationPreferences { OrderUpdates = false });

        Assert.False(await _service.SendAsync(UserId, "order_status", "Ready", "Order ready"));
        _repository.Verify(repo => repo.GetNotificationPreferencesAsync(UserId), Times.Once);
        _repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task BatchWithGuestRecipientStillNotifiesRegisteredRecipient()
    {
        _repository.Setup(repo => repo.GetNotificationPreferencesAsync(UserId))
            .ReturnsAsync(new NotificationPreferences());
        _repository.Setup(repo => repo.CreateNotificationAsync(It.IsAny<AppNotification>()))
            .Returns(Task.CompletedTask);

        var count = await _service.SendToManyAsync(
            ["guest_dinein", UserId, "guest_takeaway"], "system", "Update", "Cafe update");

        Assert.Equal(1, count);
        _repository.Verify(repo => repo.GetNotificationPreferencesAsync(UserId), Times.Once);
        _repository.Verify(repo => repo.CreateNotificationAsync(It.Is<AppNotification>(n => n.UserId == UserId)), Times.Once);
        _repository.VerifyNoOtherCalls();
    }
}
