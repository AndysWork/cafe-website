using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Cafe.Api.Services;
using System.ComponentModel.DataAnnotations;
using Cafe.Api.Helpers;

namespace Cafe.Api.Models;

public class TableReservation
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string OutletId { get; set; } = string.Empty;

    [BsonElement("userId")]
    public string? UserId { get; set; }

    [BsonElement("customerName")]
    public string CustomerName { get; set; } = string.Empty;

    [BsonElement("customerPhone")]
    public string CustomerPhone { get; set; } = string.Empty;

    [BsonElement("customerEmail")]
    public string? CustomerEmail { get; set; }

    [BsonElement("partySize")]
    public int PartySize { get; set; }

    [BsonElement("tableNumber")]
    public string? TableNumber { get; set; }

    [BsonElement("reservationDate")]
    public DateTime ReservationDate { get; set; }

    [BsonElement("timeSlot")]
    public string TimeSlot { get; set; } = string.Empty; // "12:00-13:00"

    [BsonElement("status")]
    public string Status { get; set; } = "pending"; // pending, confirmed, seated, completed, cancelled, no-show

    [BsonElement("specialRequests")]
    public string? SpecialRequests { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = MongoService.GetIstNow();
}

public class CreateReservationRequest
{
    [Required] [StringLength(100, MinimumLength = 2)]
    public string CustomerName { get; set; } = string.Empty;

    [Required] [IndianPhoneNumber]
    public string CustomerPhone { get; set; } = string.Empty;

    public string? CustomerEmail { get; set; }

    [Range(1, 50)]
    public int PartySize { get; set; }

    public string? TableNumber { get; set; }

    [Required]
    public DateTime ReservationDate { get; set; }

    [Required]
    public string TimeSlot { get; set; } = string.Empty;

    [StringLength(500)]
    public string? SpecialRequests { get; set; }
}

public class UpdateReservationStatusRequest
{
    [Required]
    [AllowedValuesList("pending", "confirmed", "seated", "completed", "cancelled", "no-show")]
    public string Status { get; set; } = string.Empty;
}
