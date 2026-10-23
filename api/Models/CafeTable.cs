using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Cafe.Api.Helpers;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Cafe.Api.Models;

/// <summary>
/// Physical table master. Occupancy (occupied / bill requested) is derived live from the
/// active <see cref="DineInSession"/>; only housekeeping state is stored here.
/// </summary>
public class CafeTable
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("outletId")]
    [BsonSerializer(typeof(StringOrObjectIdSerializer))]
    public string OutletId { get; set; } = string.Empty;

    [BsonElement("tableNumber")]
    public string TableNumber { get; set; } = string.Empty;

    /// <summary>Upper-cased table number used for uniqueness and lookups.</summary>
    [BsonElement("tableKey")]
    public string TableKey { get; set; } = string.Empty;

    [BsonElement("area")]
    public string? Area { get; set; }

    [BsonElement("capacity")]
    public int Capacity { get; set; } = 4;

    [BsonElement("sortOrder")]
    public int SortOrder { get; set; }

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    /// <summary>available | cleaning | out_of_service</summary>
    [BsonElement("housekeepingStatus")]
    public string HousekeepingStatus { get; set; } = CafeTableStatus.Available;

    [BsonElement("qrTokenHash")]
    [BsonIgnoreIfNull]
    [JsonIgnore]
    public string? QrTokenHash { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedBy")]
    public string? UpdatedBy { get; set; }

    public static string ToKey(string tableNumber) => tableNumber.Trim().ToUpperInvariant();
}

public static class CafeTableStatus
{
    // Stored housekeeping states
    public const string Available = "available";
    public const string Cleaning = "cleaning";
    public const string OutOfService = "out_of_service";

    // Derived live states
    public const string Free = "free";
    public const string Occupied = "occupied";
    public const string BillRequested = "bill_requested";

    public static readonly IReadOnlySet<string> Housekeeping = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Available, Cleaning, OutOfService
    };
}

public class CreateCafeTableRequest
{
    [Required(ErrorMessage = "Table number is required")]
    [StringLength(20, MinimumLength = 1, ErrorMessage = "Table number must be 1-20 characters")]
    public string TableNumber { get; set; } = string.Empty;

    [StringLength(50)]
    public string? Area { get; set; }

    [Range(1, 50, ErrorMessage = "Capacity must be between 1 and 50")]
    public int Capacity { get; set; } = 4;

    public int? SortOrder { get; set; }

    public string? OutletId { get; set; }
}

public class BulkCreateCafeTablesRequest
{
    [StringLength(10)]
    public string? Prefix { get; set; } = "T";

    [Range(1, 999)]
    public int From { get; set; } = 1;

    [Range(1, 999)]
    public int To { get; set; } = 10;

    [StringLength(50)]
    public string? Area { get; set; }

    [Range(1, 50)]
    public int Capacity { get; set; } = 4;

    public string? OutletId { get; set; }
}

public class UpdateCafeTableRequest
{
    [StringLength(20, MinimumLength = 1)]
    public string? TableNumber { get; set; }

    [StringLength(50)]
    public string? Area { get; set; }

    [Range(1, 50)]
    public int? Capacity { get; set; }

    public int? SortOrder { get; set; }

    public bool? IsActive { get; set; }
}

public class UpdateCafeTableStatusRequest
{
    [Required]
    public string Status { get; set; } = string.Empty;
}

/// <summary>Table with its live floor state.</summary>
public class CafeTableFloorView
{
    public string? Id { get; set; }
    public string TableNumber { get; set; } = string.Empty;
    public string? Area { get; set; }
    public int Capacity { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public string HousekeepingStatus { get; set; } = CafeTableStatus.Available;
    /// <summary>free | occupied | bill_requested | cleaning | out_of_service</summary>
    public string Status { get; set; } = CafeTableStatus.Free;
    public string? SessionId { get; set; }
    public string? CustomerName { get; set; }
    public int RoundsCount { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal AmountDue { get; set; }
    public DateTime? OccupiedSince { get; set; }
    public int? MinutesActive { get; set; }
}
