using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Cafe.Api.Models;

// Daily Expense Record
public class Expense
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("date")]
    public DateTime Date { get; set; }

    [BsonElement("expenseType")]
    public string ExpenseType { get; set; } = string.Empty; // Inventory, Salary, Rent, Utilities, Maintenance, Marketing, Other

    [BsonElement("description")]
    public string Description { get; set; } = string.Empty;

    [BsonElement("amount")]
    public decimal Amount { get; set; }

    [BsonElement("vendor")]
    public string? Vendor { get; set; }

    [BsonElement("paymentMethod")]
    public string PaymentMethod { get; set; } = "Cash"; // Cash, Card, UPI, Bank Transfer

    [BsonElement("invoiceNumber")]
    public string? InvoiceNumber { get; set; }

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
    public DateTime Date { get; set; }
    public string ExpenseType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Vendor { get; set; }
    public string PaymentMethod { get; set; } = "Cash";
    public string? InvoiceNumber { get; set; }
    public string? Notes { get; set; }
}

public class ExpenseResponse
{
    public string Id { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string ExpenseType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Vendor { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string? InvoiceNumber { get; set; }
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
