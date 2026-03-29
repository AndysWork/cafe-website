using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;
using Cafe.Api.Services;

namespace Cafe.Api.Models;

/// <summary>
/// Represents a customer's claim for loyalty points from an external platform (Zomato/Swiggy) invoice.
/// </summary>
[BsonIgnoreExtraElements]
public class ExternalOrderClaim
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("userId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("username")]
    public string Username { get; set; } = string.Empty;

    [BsonElement("platform")]
    public string Platform { get; set; } = string.Empty; // "zomato" or "swiggy"

    [BsonElement("invoiceImageUrl")]
    public string InvoiceImageUrl { get; set; } = string.Empty;

    /// <summary>Items extracted from the invoice screenshot.</summary>
    [BsonElement("extractedItems")]
    public List<ExtractedInvoiceItem> ExtractedItems { get; set; } = new();

    /// <summary>Total amount extracted from the invoice.</summary>
    [BsonElement("extractedTotal")]
    public decimal ExtractedTotal { get; set; }

    /// <summary>Loyalty points to award (extractedTotal * 0.60, rounded down).</summary>
    [BsonElement("calculatedPoints")]
    public int CalculatedPoints { get; set; }

    /// <summary>pending, approved, rejected</summary>
    [BsonElement("status")]
    public string Status { get; set; } = "pending";

    [BsonElement("adminNotes")]
    public string? AdminNotes { get; set; }

    [BsonElement("reviewedBy")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? ReviewedBy { get; set; }

    [BsonElement("reviewedAt")]
    public DateTime? ReviewedAt { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();

    [BsonElement("updatedAt")]
    public DateTime? UpdatedAt { get; set; }
}

[BsonIgnoreExtraElements]
public class ExtractedInvoiceItem
{
    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("quantity")]
    public int Quantity { get; set; } = 1;

    [BsonElement("price")]
    public decimal Price { get; set; }
}

// ── Request/Response DTOs ──

public class SubmitExternalClaimRequest
{
    [Required]
    public string Platform { get; set; } = string.Empty; // "zomato" or "swiggy"
}

public class ReviewClaimRequest
{
    [Required]
    public string Action { get; set; } = string.Empty; // "approve" or "reject"

    public string? AdminNotes { get; set; }

    /// <summary>Admin can override the calculated points.</summary>
    public int? OverridePoints { get; set; }
}

public class ExternalOrderClaimResponse
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string InvoiceImageUrl { get; set; } = string.Empty;
    public List<ExtractedInvoiceItem> ExtractedItems { get; set; } = new();
    public decimal ExtractedTotal { get; set; }
    public int CalculatedPoints { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? AdminNotes { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
