using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;
using Cafe.Api.Helpers;

namespace Cafe.Api.Models;

// Daily Cash Reconciliation Record
public class DailyCashReconciliation
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("date")]
    public DateTime Date { get; set; }

    // Expected Collections (from sales)
    [BsonElement("expectedCash")]
    public decimal ExpectedCash { get; set; }

    [BsonElement("expectedCoins")]
    public decimal ExpectedCoins { get; set; }

    [BsonElement("expectedOnline")]
    public decimal ExpectedOnline { get; set; }

    [BsonElement("expectedTotal")]
    public decimal ExpectedTotal { get; set; }

    // Actual Counted Values
    [BsonElement("countedCash")]
    public decimal CountedCash { get; set; }

    [BsonElement("countedCoins")]
    public decimal CountedCoins { get; set; }

    [BsonElement("actualOnline")]
    public decimal ActualOnline { get; set; }

    [BsonElement("countedTotal")]
    public decimal CountedTotal { get; set; }

    // Deficits/Surplus
    [BsonElement("cashDeficit")]
    public decimal CashDeficit { get; set; } // Negative means surplus

    [BsonElement("coinDeficit")]
    public decimal CoinDeficit { get; set; }

    [BsonElement("onlineDeficit")]
    public decimal OnlineDeficit { get; set; }

    [BsonElement("totalDeficit")]
    public decimal TotalDeficit { get; set; }

    // Running Balances - Track actual shop balances
    [BsonElement("openingCashBalance")]
    public decimal OpeningCashBalance { get; set; }

    [BsonElement("openingCoinBalance")]
    public decimal OpeningCoinBalance { get; set; }

    [BsonElement("openingOnlineBalance")]
    public decimal OpeningOnlineBalance { get; set; }

    [BsonElement("closingCashBalance")]
    public decimal ClosingCashBalance { get; set; }

    [BsonElement("closingCoinBalance")]
    public decimal ClosingCoinBalance { get; set; }

    [BsonElement("closingOnlineBalance")]
    public decimal ClosingOnlineBalance { get; set; }

    // Additional Information
    [BsonElement("notes")]
    public string? Notes { get; set; }

    [BsonElement("reconciledBy")]
    public string ReconciledBy { get; set; } = string.Empty;

    [BsonElement("isReconciled")]
    public bool IsReconciled { get; set; } = false;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

// Request DTOs
public class CreateDailyCashReconciliationRequest
{
    [Required(ErrorMessage = "Date is required")]
    public DateTime Date { get; set; }

    [Range(0, 10000000, ErrorMessage = "Expected cash must be between 0 and 10,000,000")]
    public decimal ExpectedCash { get; set; }

    [Range(0, 100000, ErrorMessage = "Expected coins must be between 0 and 100,000")]
    public decimal ExpectedCoins { get; set; }

    // ExpectedOnline removed - user will manually track actual online instead
    // [Range(0, 10000000, ErrorMessage = "Expected online must be between 0 and 10,000,000")]
    // public decimal ExpectedOnline { get; set; }

    [Range(0, 10000000, ErrorMessage = "Counted cash must be between 0 and 10,000,000")]
    public decimal CountedCash { get; set; }

    [Range(0, 100000, ErrorMessage = "Counted coins must be between 0 and 100,000")]
    public decimal CountedCoins { get; set; }

    [Range(0, 10000000, ErrorMessage = "Actual online must be between 0 and 10,000,000")]
    public decimal ActualOnline { get; set; }

    [StringLength(1000, ErrorMessage = "Notes cannot exceed 1000 characters")]
    public string? Notes { get; set; }
}

public class UpdateDailyCashReconciliationRequest
{
    [Range(0, 10000000, ErrorMessage = "Counted cash must be between 0 and 10,000,000")]
    public decimal CountedCash { get; set; }

    [Range(0, 100000, ErrorMessage = "Counted coins must be between 0 and 100,000")]
    public decimal CountedCoins { get; set; }

    [Range(0, 10000000, ErrorMessage = "Actual online must be between 0 and 10,000,000")]
    public decimal ActualOnline { get; set; }

    [StringLength(1000, ErrorMessage = "Notes cannot exceed 1000 characters")]
    public string? Notes { get; set; }

    public bool IsReconciled { get; set; }
}

public class BulkReconciliationRequest
{
    [Required(ErrorMessage = "Reconciliation records are required")]
    [MinLength(1, ErrorMessage = "At least one reconciliation record is required")]
    public List<CreateDailyCashReconciliationRequest> Records { get; set; } = new();
}

public class DailyCashReconciliationResponse
{
    public string Id { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public decimal ExpectedCash { get; set; }
    public decimal ExpectedCoins { get; set; }
    public decimal ExpectedOnline { get; set; }
    public decimal ExpectedTotal { get; set; }
    public decimal CountedCash { get; set; }
    public decimal CountedCoins { get; set; }
    public decimal ActualOnline { get; set; }
    public decimal CountedTotal { get; set; }
    public decimal CashDeficit { get; set; }
    public decimal CoinDeficit { get; set; }
    public decimal OnlineDeficit { get; set; }
    public decimal TotalDeficit { get; set; }
    public string? Notes { get; set; }
    public string ReconciledBy { get; set; } = string.Empty;
    public bool IsReconciled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
