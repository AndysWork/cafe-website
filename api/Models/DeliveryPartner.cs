using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Cafe.Api.Services;
using System.ComponentModel.DataAnnotations;
using Cafe.Api.Helpers;

namespace Cafe.Api.Models;

public class DeliveryPartner
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string OutletId { get; set; } = string.Empty;

    [BsonElement("userId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? UserId { get; set; }

    [BsonElement("name")]
    [Required] [StringLength(100, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [BsonElement("phone")]
    [Required]
    public string Phone { get; set; } = string.Empty;

    [BsonElement("vehicleType")]
    public string VehicleType { get; set; } = "bike"; // bike, scooter, cycle, car

    [BsonElement("vehicleNumber")]
    public string? VehicleNumber { get; set; }

    [BsonElement("licenseNumber")]
    public string? LicenseNumber { get; set; }

    [BsonElement("emergencyContactName")]
    public string? EmergencyContactName { get; set; }

    [BsonElement("emergencyContactPhone")]
    public string? EmergencyContactPhone { get; set; }

    [BsonElement("bankOrUpi")]
    public string? BankOrUpi { get; set; }

    [BsonElement("enrollmentStatus")]
    public string EnrollmentStatus { get; set; } = "verified"; // draft, submitted, verified, rejected, inactive

    [BsonElement("mileageKmpl")]
    public decimal MileageKmpl { get; set; } = 40;

    [BsonElement("codAllowed")]
    public bool CodAllowed { get; set; } = true;

    [BsonElement("payoutEnabled")]
    public bool PayoutEnabled { get; set; } = true;

    [BsonElement("status")]
    public string Status { get; set; } = "available"; // available, on-delivery, offline

    [BsonElement("currentOrderId")]
    public string? CurrentOrderId { get; set; }

    [BsonElement("currentLatitude")]
    public double? CurrentLatitude { get; set; }

    [BsonElement("currentLongitude")]
    public double? CurrentLongitude { get; set; }

    [BsonElement("lastLocationUpdatedAt")]
    public DateTime? LastLocationUpdatedAt { get; set; }

    [BsonElement("totalDeliveries")]
    public int TotalDeliveries { get; set; }

    [BsonElement("rating")]
    public double Rating { get; set; } = 5.0;

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();
}

public class CreateDeliveryPartnerRequest
{
    [Required] [StringLength(100, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Required] [IndianPhoneNumber]
    public string Phone { get; set; } = string.Empty;

    public string VehicleType { get; set; } = "bike";
    public string? VehicleNumber { get; set; }
    public string? UserId { get; set; }
    public decimal? MileageKmpl { get; set; }
    public bool? CodAllowed { get; set; }
    public bool? PayoutEnabled { get; set; }
    public string? LicenseNumber { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? BankOrUpi { get; set; }
}

public class AssignDeliveryRequest
{
    [Required]
    public string OrderId { get; set; } = string.Empty;

    public string? DeliveryPartnerId { get; set; }
}

public class UpdateDeliveryPartnerLocationRequest
{
    [Range(-90, 90)]
    public double Latitude { get; set; }

    [Range(-180, 180)]
    public double Longitude { get; set; }
}
