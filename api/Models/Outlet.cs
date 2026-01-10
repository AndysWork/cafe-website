using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Cafe.Api.Services;
using System.ComponentModel.DataAnnotations;

namespace Cafe.Api.Models;

/// <summary>
/// Represents a physical outlet/branch of Maa Tara Cafe
/// </summary>
public class Outlet
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [Required(ErrorMessage = "Outlet name is required")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Outlet name must be between 2 and 100 characters")]
    [BsonElement("outletName")]
    public string OutletName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Outlet code is required")]
    [StringLength(10, MinimumLength = 2, ErrorMessage = "Outlet code must be between 2 and 10 characters")]
    [BsonElement("outletCode")]
    public string OutletCode { get; set; } = string.Empty; // e.g., "MT001", "MT002"

    [StringLength(200, ErrorMessage = "Address cannot exceed 200 characters")]
    [BsonElement("address")]
    public string Address { get; set; } = string.Empty;

    [StringLength(100, ErrorMessage = "City cannot exceed 100 characters")]
    [BsonElement("city")]
    public string City { get; set; } = string.Empty;

    [StringLength(100, ErrorMessage = "State cannot exceed 100 characters")]
    [BsonElement("state")]
    public string State { get; set; } = string.Empty;

    [StringLength(20, ErrorMessage = "Phone number cannot exceed 20 characters")]
    [BsonElement("phoneNumber")]
    public string? PhoneNumber { get; set; }

    [EmailAddress(ErrorMessage = "Invalid email format")]
    [StringLength(100, ErrorMessage = "Email cannot exceed 100 characters")]
    [BsonElement("email")]
    public string? Email { get; set; }

    [StringLength(100, ErrorMessage = "Manager name cannot exceed 100 characters")]
    [BsonElement("managerName")]
    public string? ManagerName { get; set; }

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    [BsonElement("settings")]
    public OutletSettings Settings { get; set; } = new();

    [BsonElement("createdBy")]
    public string CreatedBy { get; set; } = "System";

    [BsonElement("createdDate")]
    public DateTime CreatedDate { get; set; } = MongoService.GetIstNow();

    [BsonElement("lastUpdatedBy")]
    public string LastUpdatedBy { get; set; } = "System";

    [BsonElement("lastUpdated")]
    public DateTime LastUpdated { get; set; } = MongoService.GetIstNow();
}

/// <summary>
/// Outlet-specific settings and configuration
/// </summary>
public class OutletSettings
{
    [BsonElement("openingTime")]
    public string OpeningTime { get; set; } = "08:00";

    [BsonElement("closingTime")]
    public string ClosingTime { get; set; } = "22:00";

    [BsonElement("acceptsOnlineOrders")]
    public bool AcceptsOnlineOrders { get; set; } = true;

    [BsonElement("acceptsDineIn")]
    public bool AcceptsDineIn { get; set; } = true;

    [BsonElement("acceptsTakeaway")]
    public bool AcceptsTakeaway { get; set; } = true;

    [BsonElement("taxPercentage")]
    [Range(0, 100, ErrorMessage = "Tax percentage must be between 0 and 100")]
    public decimal TaxPercentage { get; set; } = 5;

    [BsonElement("deliveryRadius")]
    public decimal? DeliveryRadius { get; set; } // in kilometers

    [BsonElement("minimumOrderAmount")]
    public decimal? MinimumOrderAmount { get; set; }
}

// Request/Response DTOs
public class CreateOutletRequest
{
    [Required(ErrorMessage = "Outlet name is required")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Outlet name must be between 2 and 100 characters")]
    public string OutletName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Outlet code is required")]
    [StringLength(10, MinimumLength = 2, ErrorMessage = "Outlet code must be between 2 and 10 characters")]
    public string OutletCode { get; set; } = string.Empty;

    [StringLength(200, ErrorMessage = "Address cannot exceed 200 characters")]
    public string? Address { get; set; }

    [StringLength(100, ErrorMessage = "City cannot exceed 100 characters")]
    public string? City { get; set; }

    [StringLength(100, ErrorMessage = "State cannot exceed 100 characters")]
    public string? State { get; set; }

    [StringLength(20, ErrorMessage = "Phone number cannot exceed 20 characters")]
    public string? PhoneNumber { get; set; }

    [EmailAddress(ErrorMessage = "Invalid email format")]
    [StringLength(100, ErrorMessage = "Email cannot exceed 100 characters")]
    public string? Email { get; set; }

    [StringLength(100, ErrorMessage = "Manager name cannot exceed 100 characters")]
    public string? ManagerName { get; set; }

    public OutletSettings? Settings { get; set; }
}

public class UpdateOutletRequest
{
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Outlet name must be between 2 and 100 characters")]
    public string? OutletName { get; set; }

    [StringLength(200, ErrorMessage = "Address cannot exceed 200 characters")]
    public string? Address { get; set; }

    [StringLength(100, ErrorMessage = "City cannot exceed 100 characters")]
    public string? City { get; set; }

    [StringLength(100, ErrorMessage = "State cannot exceed 100 characters")]
    public string? State { get; set; }

    [StringLength(20, ErrorMessage = "Phone number cannot exceed 20 characters")]
    public string? PhoneNumber { get; set; }

    [EmailAddress(ErrorMessage = "Invalid email format")]
    [StringLength(100, ErrorMessage = "Email cannot exceed 100 characters")]
    public string? Email { get; set; }

    [StringLength(100, ErrorMessage = "Manager name cannot exceed 100 characters")]
    public string? ManagerName { get; set; }

    public bool? IsActive { get; set; }

    public OutletSettings? Settings { get; set; }
}
