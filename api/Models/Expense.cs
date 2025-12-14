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

    [BsonElement("expenseSource")]
    public string ExpenseSource { get; set; } = "Offline"; // Offline or Online

    [BsonElement("amount")]
    public decimal Amount { get; set; };

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
    public DateTime Date { get; set; }
    public string ExpenseType { get; set; } = string.Empty;
    public string ExpenseSource { get; set; } = "Offline";
    public decimal Amount { get; set; };
    public string PaymentMethod { get; set; } = "Cash";
    public string? Notes { get; set; }
}

public class ExpenseResponse
{
    public string Id { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string ExpenseType { get; set; } = string.Empty;
    public string ExpenseSource { get; set; } = string.Empty;
    public decimal Amount { get; set; };
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
