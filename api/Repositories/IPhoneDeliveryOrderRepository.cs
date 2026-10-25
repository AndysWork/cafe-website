using Cafe.Api.Models;

namespace Cafe.Api.Repositories;

public interface IPhoneDeliveryOrderRepository
{
    Task<List<Order>> GetPhoneDeliveryOrdersAsync(string outletId, string? status);
    Task MarkPhoneEffectsQueuedAsync(string orderId);
    Task<bool> TryPickupPhoneDeliveryAsync(string orderId, DeliveryPartner partner);
    Task<bool> TryCompletePhoneDeliveryAsync(string orderId, DeliveryPartner partner);
    Task<bool> TryRecordPhoneCodAsync(string orderId, DeliveryPartner partner, string userId);
    Task ReleasePhonePartnerAsync(Order order);
}
