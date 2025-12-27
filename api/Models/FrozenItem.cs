using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Cafe.Api.Models;

public class FrozenItem
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("itemName")]
    public string ItemName { get; set; } = string.Empty;

    [BsonElement("quantity")]
    public int Quantity { get; set; } // Number of packets/units

    [BsonElement("packetWeight")]
    public decimal PacketWeight { get; set; } // Weight of each packet

    [BsonElement("buyPrice")]
    public decimal BuyPrice { get; set; } // Total purchase price

    [BsonElement("perPiecePrice")]
    public decimal PerPiecePrice { get; set; } // Price per individual piece

    [BsonElement("perPieceWeight")]
    public decimal PerPieceWeight { get; set; } // Weight per individual piece

    [BsonElement("vendor")]
    public string Vendor { get; set; } = string.Empty;

    [BsonElement("category")]
    public string Category { get; set; } = "frozen";

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    [BsonElement("notes")]
    public string? Notes { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime? UpdatedAt { get; set; }
}

public class FrozenItemUpload
{
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal PacketWeight { get; set; }
    public decimal BuyPrice { get; set; }
    public decimal PerPiecePrice { get; set; }
    public decimal PerPieceWeight { get; set; }
    public string Vendor { get; set; } = string.Empty;
}
