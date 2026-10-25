using Cafe.Api.Models;
using MongoDB.Driver;

namespace Cafe.Api.Services;

public partial class MongoService : Cafe.Api.Repositories.IPhoneDeliveryOrderRepository
{
    public async Task<List<Order>> GetPhoneDeliveryOrdersAsync(string outletId, string? status)
    {
        var filter = Builders<Order>.Filter.Eq(o => o.OutletId, outletId)
            & Builders<Order>.Filter.Eq(o => o.OrderSource, "phone")
            & Builders<Order>.Filter.Ne(o => o.IsDeleted, true);
        if (!string.IsNullOrWhiteSpace(status))
            filter &= Builders<Order>.Filter.Eq(o => o.Status, status);
        var active = filter & Builders<Order>.Filter.Nin(o => o.Status, new[] { "delivered", "cancelled" });
        var recent = filter & Builders<Order>.Filter.In(o => o.Status, new[] { "delivered", "cancelled" })
            & Builders<Order>.Filter.Gte(o => o.UpdatedAt, GetIstNow().AddDays(-7));
        var orders = await _orders.Find(active).SortByDescending(o => o.CreatedAt).Limit(300).ToListAsync();
        orders.AddRange(await _orders.Find(recent).SortByDescending(o => o.CreatedAt).Limit(100).ToListAsync());
        return orders;
    }

    public async Task MarkPhoneEffectsQueuedAsync(string orderId) =>
        await _orders.UpdateOneAsync(o => o.Id == orderId && o.OrderSource == "phone",
            Builders<Order>.Update.Set(o => o.PhoneEffectsQueued, true));

    public async Task<bool> TryUpdatePhoneKitchenAsync(Order order, string expectedStatus, DateTime expectedUpdatedAt)
    {
        var result = await _orders.ReplaceOneAsync(o => o.Id == order.Id && o.OrderSource == "phone"
            && o.IsDeleted != true && o.Status == expectedStatus && o.UpdatedAt == expectedUpdatedAt, order);
        return result.ModifiedCount == 1;
    }

    public async Task<bool> TryPickupPhoneDeliveryAsync(string orderId, DeliveryPartner partner)
    {
        var result = await _orders.UpdateOneAsync(o => o.Id == orderId && o.OrderSource == "phone"
            && o.IsDeleted != true && o.OutletId == partner.OutletId && o.DeliveryPartnerId == partner.Id
            && o.Status == "ready" && (o.PaymentMethod == "cod" || o.PaymentStatus == "paid"),
            Builders<Order>.Update.Set(o => o.Status, "out-for-delivery").Set(o => o.UpdatedAt, GetIstNow()));
        return result.ModifiedCount == 1;
    }

    public async Task<bool> TryCompletePhoneDeliveryAsync(string orderId, DeliveryPartner partner)
    {
        var now = GetIstNow();
        var result = await _orders.UpdateOneAsync(o => o.Id == orderId && o.OrderSource == "phone"
            && o.IsDeleted != true && o.OutletId == partner.OutletId && o.DeliveryPartnerId == partner.Id
            && o.Status == "out-for-delivery" && o.PaymentStatus == "paid",
            Builders<Order>.Update.Set(o => o.Status, "delivered").Set(o => o.CompletedAt, now).Set(o => o.UpdatedAt, now));
        return result.ModifiedCount == 1;
    }

    // Frees a partner assigned to a phone order that was cancelled before pickup.
    public async Task ReleasePhonePartnerAsync(Order order)
    {
        if (order.OrderSource != "phone" || string.IsNullOrWhiteSpace(order.Id)
            || string.IsNullOrWhiteSpace(order.DeliveryPartnerId))
            return;
        await _deliveryPartners.UpdateOneAsync(
            p => p.Id == order.DeliveryPartnerId && p.CurrentOrderId == order.Id,
            Builders<DeliveryPartner>.Update.Set(p => p.Status, "available").Set(p => p.CurrentOrderId, (string?)null));
    }

    public async Task<bool> TryRecordPhoneCodAsync(string orderId, DeliveryPartner partner, string userId)
    {
        var now = GetIstNow();
        var result = await _orders.UpdateOneAsync(o => o.Id == orderId && o.OrderSource == "phone"
            && o.IsDeleted != true && o.OutletId == partner.OutletId && o.DeliveryPartnerId == partner.Id
            && o.Status == "out-for-delivery" && o.PaymentMethod == "cod" && o.PaymentStatus != "paid",
            Builders<Order>.Update.Set(o => o.PaymentStatus, "paid").Set(o => o.PaymentRecordedByUserId, userId)
                .Set(o => o.PaymentRecordedAt, now).Set(o => o.UpdatedAt, now));
        return result.ModifiedCount == 1;
    }
}
