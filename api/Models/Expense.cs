using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;
using Cafe.Api.Helpers;

namespace Cafe.Api.Models;

// Daily Expense Record
[BsonIgnoreExtraElements]
public class Expense
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? OutletId { get; set; }

    [BsonElement("date")]
    public DateTime Date { get; set; }

    [BsonElement("expenseType")]
    public string ExpenseType { get; set; } = string.Empty; // Inventory, Salary, Rent, Utilities, Maintenance, Marketing, Other

    [BsonElement("expenseSource")]
    public string ExpenseSource { get; set; } = "Offline"; // Offline or Online

    [BsonElement("amount")]
    public decimal Amount { get; set; }

    [BsonElement("paymentMethod")]
    public string PaymentMethod { get; set; } = "Cash"; // Cash, Card, UPI, Bank Transfer

    [BsonElement("notes")]
    public string? Notes { get; set; }

    [BsonElement("recordedBy")]
    public string RecordedBy { get; set; } = string.Empty;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

// Request/Response DTOs
public class CreateExpenseRequest
{
    [Required(ErrorMessage = "Date is required")]
    public string Date { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Expense type is required")]
    [AllowedValuesList("Inventory", "Salary", "Rent", "Utilities", "Maintenance", "Marketing", "Other")]
    public string ExpenseType { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Expense source is required")]
    [AllowedValuesList("Offline", "Online")]
    public string ExpenseSource { get; set; } = "Offline";
    
    [Range(0.01, 10000000, ErrorMessage = "Amount must be between 0.01 and 10,000,000")]
    public decimal Amount { get; set; }
    
    [Required(ErrorMessage = "Payment method is required")]
    [AllowedValuesList("Cash", "Card", "UPI", "Bank Transfer")]
    public string PaymentMethod { get; set; } = "Cash";
    
    [StringLength(500, ErrorMessage = "Notes cannot exceed 500 characters")]
    public string? Notes { get; set; }
}

public class ExpenseResponse
{
    public string Id { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string ExpenseType { get; set; } = string.Empty;
    public string ExpenseSource { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ExpenseSummary
{
    public DateTime Date { get; set; }
    public decimal TotalExpenses { get; set; }
    public Dictionary<string, decimal> ExpenseTypeBreakdown { get; set; } = new();
}
