using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Cafe.Api.Models;

public class KitchenVoiceStockRequest
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("outletId")]
    public string OutletId { get; set; } = string.Empty;

    [BsonElement("requestedByUserId")]
    public string RequestedByUserId { get; set; } = string.Empty;

    [BsonElement("requestedByName")]
    public string RequestedByName { get; set; } = string.Empty;

    [BsonElement("requestedByRole")]
    public string RequestedByRole { get; set; } = string.Empty;

    [BsonElement("transcriptText")]
    public string TranscriptText { get; set; } = string.Empty;

    [BsonElement("requestedItems")]
    public List<string> RequestedItems { get; set; } = new();

    [BsonElement("sttProvider")]
    public string? SttProvider { get; set; }

    [BsonElement("sttConfidence")]
    public double? SttConfidence { get; set; }

    [BsonElement("status")]
    public string Status { get; set; } = "pending";

    [BsonElement("reviewedByUserId")]
    public string? ReviewedByUserId { get; set; }

    [BsonElement("reviewedByName")]
    public string? ReviewedByName { get; set; }

    [BsonElement("reviewNote")]
    public string? ReviewNote { get; set; }

    [BsonElement("reviewedAt")]
    public DateTime? ReviewedAt { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class CreateKitchenVoiceStockRequest
{
    [StringLength(1000, ErrorMessage = "Transcript text cannot exceed 1000 characters")]
    public string? TranscriptText { get; set; }

    public List<string>? RequestedItems { get; set; }

    [StringLength(50)]
    public string? SttProvider { get; set; }

    [Range(0, 1, ErrorMessage = "Confidence must be between 0 and 1")]
    public double? SttConfidence { get; set; }
}

public class ReviewKitchenVoiceStockRequest
{
    [Required(ErrorMessage = "Decision is required")]
    [StringLength(20)]
    public string Decision { get; set; } = string.Empty;

    [StringLength(300, ErrorMessage = "Note cannot exceed 300 characters")]
    public string? Note { get; set; }
}
