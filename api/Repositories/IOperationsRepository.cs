using Cafe.Api.Models;

namespace Cafe.Api.Repositories;

public interface IOperationsRepository
{
    // Delivery Zones
    Task<List<DeliveryZone>> GetDeliveryZonesAsync(string outletId);
    Task<DeliveryZone?> GetDeliveryZoneByIdAsync(string id);
    Task<DeliveryZone> CreateDeliveryZoneAsync(DeliveryZone zone);
    Task<bool> UpdateDeliveryZoneAsync(string id, DeliveryZone zone);
    Task<bool> DeleteDeliveryZoneAsync(string id);
    Task<decimal> CalculateDeliveryFeeAsync(string outletId, decimal orderSubtotal);

    // Table Reservations
    Task<TableReservation> CreateReservationAsync(TableReservation reservation);
    Task<List<TableReservation>> GetReservationsAsync(string outletId, DateTime? date = null, int page = 1, int pageSize = 50);
    Task<TableReservation?> GetReservationByIdAsync(string id);
    Task<bool> UpdateReservationStatusAsync(string id, string status);
    Task<List<TableReservation>> GetUserReservationsAsync(string userId);

    // Delivery Partners
    Task<DeliveryPartner> CreateDeliveryPartnerAsync(DeliveryPartner partner);
    Task<List<DeliveryPartner>> GetDeliveryPartnersAsync(string outletId);
    Task<DeliveryPartner?> GetAvailableDeliveryPartnerAsync(string outletId);
    Task<bool> AssignDeliveryPartnerAsync(string partnerId, string orderId);
    Task<bool> CompleteDeliveryAsync(string partnerId);
    Task<bool> UpdateDeliveryPartnerAsync(string id, DeliveryPartner partner);
    Task<bool> DeleteDeliveryPartnerAsync(string id);

    // Subscriptions
    Task<SubscriptionPlan> CreateSubscriptionPlanAsync(SubscriptionPlan plan);
    Task<List<SubscriptionPlan>> GetSubscriptionPlansAsync(string outletId, bool activeOnly = false);
    Task<SubscriptionPlan?> GetSubscriptionPlanByIdAsync(string id);
    Task<bool> DeleteSubscriptionPlanAsync(string id);
    Task<CustomerSubscription> CreateCustomerSubscriptionAsync(CustomerSubscription sub);
    Task<CustomerSubscription?> GetActiveSubscriptionAsync(string userId);
    Task<List<CustomerSubscription>> GetUserSubscriptionsAsync(string userId);

    // Customer Segments
    Task<List<CustomerSegment>> GetCustomerSegmentsAsync(string? segment = null, int page = 1, int pageSize = 50);
    Task<List<SegmentSummary>> GetSegmentSummaryAsync();
    Task<int> RefreshCustomerSegmentsAsync();
}
