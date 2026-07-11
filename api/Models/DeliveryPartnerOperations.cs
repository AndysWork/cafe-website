using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;
using Cafe.Api.Services;

namespace Cafe.Api.Models;

public class DeliveryShift
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("partnerId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string PartnerId { get; set; } = string.Empty;

    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string OutletId { get; set; } = string.Empty;

    [BsonElement("shiftDate")]
    public DateTime ShiftDate { get; set; } = MongoService.GetIstNow().Date;

    [BsonElement("startedAt")]
    public DateTime StartedAt { get; set; } = MongoService.GetIstNow();

    [BsonElement("endedAt")]
    public DateTime? EndedAt { get; set; }

    [BsonElement("startOdometerKm")]
    public decimal StartOdometerKm { get; set; }

    [BsonElement("endOdometerKm")]
    public decimal? EndOdometerKm { get; set; }

    [BsonElement("startLatitude")]
    public double? StartLatitude { get; set; }

    [BsonElement("startLongitude")]
    public double? StartLongitude { get; set; }

    [BsonElement("endLatitude")]
    public double? EndLatitude { get; set; }

    [BsonElement("endLongitude")]
    public double? EndLongitude { get; set; }

    [BsonElement("totalDistanceKm")]
    public decimal TotalDistanceKm { get; set; }

    [BsonElement("status")]
    public string Status { get; set; } = "active"; // active, completed, cancelled

    [BsonElement("notes")]
    public string? Notes { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = MongoService.GetIstNow();
}

public class PartnerTripLog
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("shiftId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string ShiftId { get; set; } = string.Empty;

    [BsonElement("partnerId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string PartnerId { get; set; } = string.Empty;

    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string OutletId { get; set; } = string.Empty;

    [BsonElement("tripType")]
    public string TripType { get; set; } = "delivery"; // delivery, outlet-transfer, market-stop, misc

    [BsonElement("orderId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? OrderId { get; set; }

    [BsonElement("startPointLabel")]
    public string? StartPointLabel { get; set; }

    [BsonElement("endPointLabel")]
    public string? EndPointLabel { get; set; }

    [BsonElement("startLatitude")]
    public double? StartLatitude { get; set; }

    [BsonElement("startLongitude")]
    public double? StartLongitude { get; set; }

    [BsonElement("endLatitude")]
    public double? EndLatitude { get; set; }

    [BsonElement("endLongitude")]
    public double? EndLongitude { get; set; }

    [BsonElement("startOdometerKm")]
    public decimal StartOdometerKm { get; set; }

    [BsonElement("endOdometerKm")]
    public decimal EndOdometerKm { get; set; }

    [BsonElement("distanceKm")]
    public decimal DistanceKm { get; set; }

    [BsonElement("startedAt")]
    public DateTime StartedAt { get; set; } = MongoService.GetIstNow();

    [BsonElement("endedAt")]
    public DateTime EndedAt { get; set; } = MongoService.GetIstNow();

    [BsonElement("notes")]
    public string? Notes { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();
}

public class FuelPriceDaily
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string OutletId { get; set; } = string.Empty;

    [BsonElement("date")]
    public DateTime Date { get; set; } = MongoService.GetIstNow().Date;

    [BsonElement("petrolPricePerLitre")]
    public decimal PetrolPricePerLitre { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = MongoService.GetIstNow();
}

public class CODCollectionLog
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("orderId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string OrderId { get; set; } = string.Empty;

    [BsonElement("partnerId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string PartnerId { get; set; } = string.Empty;

    [BsonElement("amount")]
    public decimal Amount { get; set; }

    [BsonElement("collected")]
    public bool Collected { get; set; }

    [BsonElement("collectionReference")]
    public string? CollectionReference { get; set; }

    [BsonElement("notes")]
    public string? Notes { get; set; }

    [BsonElement("collectedAt")]
    public DateTime? CollectedAt { get; set; }

    [BsonElement("confirmedByAdmin")]
    public bool ConfirmedByAdmin { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();
}

public class DeliveryPartnerReview
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("orderId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string OrderId { get; set; } = string.Empty;

    [BsonElement("partnerId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string PartnerId { get; set; } = string.Empty;

    [BsonElement("userId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("rating")]
    public int Rating { get; set; }

    [BsonElement("review")]
    public string? Review { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();
}

public class PartnerPayoutLedger
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("partnerId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string PartnerId { get; set; } = string.Empty;

    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string OutletId { get; set; } = string.Empty;

    [BsonElement("periodStart")]
    public DateTime PeriodStart { get; set; }

    [BsonElement("periodEnd")]
    public DateTime PeriodEnd { get; set; }

    [BsonElement("periodType")]
    public string PeriodType { get; set; } = "day"; // day, week, month, year

    [BsonElement("totalDistanceKm")]
    public decimal TotalDistanceKm { get; set; }

    [BsonElement("mileageKmpl")]
    public decimal MileageKmpl { get; set; } = 40;

    [BsonElement("fuelPricePerLitre")]
    public decimal FuelPricePerLitre { get; set; }

    [BsonElement("litresConsumed")]
    public decimal LitresConsumed { get; set; }

    [BsonElement("payoutAmount")]
    public decimal PayoutAmount { get; set; }

    [BsonElement("totalDeliveries")]
    public int TotalDeliveries { get; set; }

    [BsonElement("isFinalized")]
    public bool IsFinalized { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = MongoService.GetIstNow();
}

public class ParcelDeliveryTask
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string OutletId { get; set; } = string.Empty;

    [BsonElement("partnerId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string PartnerId { get; set; } = string.Empty;

    [BsonElement("partnerName")]
    public string? PartnerName { get; set; }

    [BsonElement("assignedByUserId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? AssignedByUserId { get; set; }

    [BsonElement("startPoint")]
    public string StartPoint { get; set; } = string.Empty;

    [BsonElement("endPoint")]
    public string EndPoint { get; set; } = string.Empty;

    [BsonElement("distanceKm")]
    public decimal DistanceKm { get; set; }

    [BsonElement("isRoundTrip")]
    public bool IsRoundTrip { get; set; }

    [BsonElement("billableDistanceKm")]
    public decimal BillableDistanceKm { get; set; }

    [BsonElement("etaMinutes")]
    public int? EtaMinutes { get; set; }

    [BsonElement("routeMapUrl")]
    public string? RouteMapUrl { get; set; }

    [BsonElement("routeShortCode")]
    public string? RouteShortCode { get; set; }

    [BsonElement("routeShortUrl")]
    public string? RouteShortUrl { get; set; }

    [BsonElement("notes")]
    public string? Notes { get; set; }

    [BsonElement("status")]
    public string Status { get; set; } = "assigned"; // assigned, accepted, completed, cancelled

    [BsonElement("acceptedAt")]
    public DateTime? AcceptedAt { get; set; }

    [BsonElement("completedAt")]
    public DateTime? CompletedAt { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = MongoService.GetIstNow();
}

public class StartPartnerShiftRequest
{
    [Range(0, 1000000)]
    public decimal StartOdometerKm { get; set; }

    [Range(-90, 90)]
    public double? StartLatitude { get; set; }

    [Range(-180, 180)]
    public double? StartLongitude { get; set; }

    [StringLength(300)]
    public string? Notes { get; set; }
}

public class EndPartnerShiftRequest
{
    [Range(0, 1000000)]
    public decimal EndOdometerKm { get; set; }

    [Range(-90, 90)]
    public double? EndLatitude { get; set; }

    [Range(-180, 180)]
    public double? EndLongitude { get; set; }

    [StringLength(300)]
    public string? Notes { get; set; }
}

public class CreatePartnerTripRequest
{
    [Required]
    public string ShiftId { get; set; } = string.Empty;

    [Required]
    public string TripType { get; set; } = "delivery";

    public string? OrderId { get; set; }

    [StringLength(120)]
    public string? StartPointLabel { get; set; }

    [StringLength(120)]
    public string? EndPointLabel { get; set; }

    [Range(-90, 90)]
    public double? StartLatitude { get; set; }

    [Range(-180, 180)]
    public double? StartLongitude { get; set; }

    [Range(-90, 90)]
    public double? EndLatitude { get; set; }

    [Range(-180, 180)]
    public double? EndLongitude { get; set; }

    [Range(0, 1000000)]
    public decimal StartOdometerKm { get; set; }

    [Range(0, 1000000)]
    public decimal EndOdometerKm { get; set; }

    [StringLength(300)]
    public string? Notes { get; set; }
}

public class UpsertFuelPriceRequest
{
    [Required]
    public DateTime Date { get; set; }

    [Range(1, 1000)]
    public decimal PetrolPricePerLitre { get; set; }
}

public class ConfirmCodCollectionRequest
{
    [Required]
    public string OrderId { get; set; } = string.Empty;

    [Range(0, 1000000)]
    public decimal Amount { get; set; }

    [StringLength(120)]
    public string? CollectionReference { get; set; }

    [StringLength(300)]
    public string? Notes { get; set; }
}

public class CreateParcelTaskRequest
{
    [Required]
    public string PartnerId { get; set; } = string.Empty;

    [Required]
    [StringLength(220)]
    public string StartPoint { get; set; } = string.Empty;

    [Required]
    [StringLength(220)]
    public string EndPoint { get; set; } = string.Empty;

    public bool IsRoundTrip { get; set; }

    [StringLength(300)]
    public string? Notes { get; set; }
}

public class ParcelRouteQuoteRequest
{
    [Required]
    [StringLength(220)]
    public string StartPoint { get; set; } = string.Empty;

    [Required]
    [StringLength(220)]
    public string EndPoint { get; set; } = string.Empty;

    public bool IsRoundTrip { get; set; }
}

public class ParcelTaskRouteQuoteResponse
{
    public decimal DistanceKm { get; set; }
    public decimal BillableDistanceKm { get; set; }
    public bool IsRoundTrip { get; set; }
    public int? EtaMinutes { get; set; }
    public string? MapUrl { get; set; }
}

public class AddDeliveryPartnerReviewRequest
{
    [Required]
    public string OrderId { get; set; } = string.Empty;

    [Required]
    public string PartnerId { get; set; } = string.Empty;

    [Range(1, 5)]
    public int Rating { get; set; }

    [StringLength(600)]
    public string? Review { get; set; }
}

public class PartnerDashboardResponse
{
    public DeliveryPartner? Profile { get; set; }
    public DeliveryShift? ActiveShift { get; set; }
    public List<Order> ActiveOrders { get; set; } = new();
    public List<Order> PendingRequests { get; set; } = new();
    public List<ParcelDeliveryTask> ActiveParcelTasks { get; set; } = new();
    public List<ParcelDeliveryTask> PendingParcelTasks { get; set; } = new();
    public decimal TodayDistanceKm { get; set; }
    public decimal TodayPayout { get; set; }
    public decimal CodOutstanding { get; set; }
    public double AverageRating { get; set; }
    public int ReviewsCount { get; set; }
    public List<PartnerReviewSummary> RecentReviews { get; set; } = new();
}

public class PartnerReviewSummary
{
    public string? OrderId { get; set; }
    public int Rating { get; set; }
    public string? Review { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class PartnerPayoutSummaryResponse
{
    public string PeriodType { get; set; } = "day";
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public decimal TotalDistanceKm { get; set; }
    public int TotalDeliveries { get; set; }
    public decimal MileageKmpl { get; set; }
    public decimal FuelPricePerLitre { get; set; }
    public decimal LitresConsumed { get; set; }
    public decimal PayoutAmount { get; set; }
}

public class ManagerOpsAuditEntry
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string OutletId { get; set; } = string.Empty;

    [BsonElement("eventType")]
    public string EventType { get; set; } = string.Empty;

    [BsonElement("entityType")]
    public string EntityType { get; set; } = string.Empty;

    [BsonElement("entityId")]
    public string EntityId { get; set; } = string.Empty;

    [BsonElement("summary")]
    public string Summary { get; set; } = string.Empty;

    [BsonElement("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();

    [BsonElement("createdByUserId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? CreatedByUserId { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();
}
