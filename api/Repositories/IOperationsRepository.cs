using Cafe.Api.Models;

namespace Cafe.Api.Repositories;

public interface IOperationsRepository
{
    // Delivery Zones
    Task<List<DeliveryZone>> GetDeliveryZonesAsync(string outletId);
    Task<List<DeliveryZone>> GetActiveDeliveryZonesAsync(string outletId);
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
    Task<DeliveryPartner?> GetDeliveryPartnerByIdAsync(string partnerId);
    Task<bool> AssignDeliveryPartnerAsync(string partnerId, string orderId);
    Task<bool> TryAssignUnassignedDeliveryPartnerAsync(string partnerId, string orderId);
    Task<bool> UpdateDeliveryPartnerLocationAsync(string partnerId, double latitude, double longitude);
    Task<bool> CompleteDeliveryAsync(string partnerId);
    Task<bool> UpdateDeliveryPartnerAsync(string id, DeliveryPartner partner);
    Task<bool> DeleteDeliveryPartnerAsync(string id);
    Task<DeliveryPartner?> GetDeliveryPartnerByUserIdAsync(string userId);
    Task<List<Order>> GetActiveOrdersForPartnerAsync(string partnerId, string? outletId = null);

    // Delivery Partner Operations
    Task<DeliveryShift?> GetActiveShiftForPartnerAsync(string partnerId);
    Task<DeliveryShift> StartPartnerShiftAsync(DeliveryShift shift);
    Task<bool> EndPartnerShiftAsync(string shiftId, decimal endOdometerKm, double? endLatitude, double? endLongitude, string? notes);
    Task<List<DeliveryShift>> GetPartnerShiftsAsync(string partnerId, DateTime? fromDate = null, DateTime? toDate = null, int page = 1, int pageSize = 30);

    Task<PartnerTripLog> CreatePartnerTripAsync(PartnerTripLog trip);
    Task<List<PartnerTripLog>> GetPartnerTripsAsync(string partnerId, DateTime? fromDate = null, DateTime? toDate = null, int page = 1, int pageSize = 100);
    Task<decimal> GetPartnerDistanceAsync(string partnerId, DateTime fromDate, DateTime toDate);

    Task<FuelPriceDaily> UpsertFuelPriceAsync(string outletId, DateTime date, decimal petrolPricePerLitre);
    Task<FuelPriceDaily?> GetFuelPriceAsync(string outletId, DateTime date);

    Task<CODCollectionLog> UpsertCodCollectionAsync(CODCollectionLog codLog);
    Task<List<CODCollectionLog>> GetCodCollectionsAsync(string partnerId, DateTime? fromDate = null, DateTime? toDate = null);
    Task<decimal> GetOutstandingCodAmountAsync(string partnerId);

    Task<DeliveryPartnerReview> AddDeliveryPartnerReviewAsync(DeliveryPartnerReview review);
    Task<List<DeliveryPartnerReview>> GetDeliveryPartnerReviewsAsync(string partnerId, int limit = 10);
    Task<(double averageRating, int totalReviews)> GetDeliveryPartnerRatingSummaryAsync(string partnerId);

    Task<PartnerPayoutLedger> CreatePartnerPayoutLedgerAsync(PartnerPayoutLedger ledger);
    Task<List<PartnerPayoutLedger>> GetPartnerPayoutLedgersAsync(string partnerId, int page = 1, int pageSize = 30);
    Task<PartnerPayoutLedger?> GetPartnerPayoutLedgerByPeriodAsync(string partnerId, DateTime periodStart, DateTime periodEnd, string periodType);

    // Subscriptions
    Task<SubscriptionPlan> CreateSubscriptionPlanAsync(SubscriptionPlan plan);
    Task<List<SubscriptionPlan>> GetSubscriptionPlansAsync(string outletId, bool activeOnly = false);
    Task<SubscriptionPlan?> GetSubscriptionPlanByIdAsync(string id);
    Task<bool> UpdateSubscriptionPlanAsync(string id, SubscriptionPlan plan);
    Task<bool> DeleteSubscriptionPlanAsync(string id);
    Task<CustomerSubscription> CreateCustomerSubscriptionAsync(CustomerSubscription sub);
    Task<CustomerSubscription?> GetActiveSubscriptionAsync(string userId);
    Task<List<CustomerSubscription>> GetUserSubscriptionsAsync(string userId);

    // Customer Segments
    Task<List<CustomerSegment>> GetCustomerSegmentsAsync(string? segment = null, int page = 1, int pageSize = 50);
    Task<List<SegmentSummary>> GetSegmentSummaryAsync();
    Task<int> RefreshCustomerSegmentsAsync();
}
