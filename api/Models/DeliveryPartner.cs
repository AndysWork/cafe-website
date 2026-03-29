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

    [BsonElement("status")]
    public string Status { get; set; } = "available"; // available, on-delivery, offline

    [BsonElement("currentOrderId")]
    public string? CurrentOrderId { get; set; }

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
}

public class AssignDeliveryRequest
{
    [Required]
    public string OrderId { get; set; } = string.Empty;

    [Required]
    public string DeliveryPartnerId { get; set; } = string.Empty;
}
