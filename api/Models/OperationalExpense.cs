using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Cafe.Api.Models;

// Monthly Operational Expense Record
[BsonIgnoreExtraElements]
public class OperationalExpense
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("month")]
    [Required(ErrorMessage = "Month is required")]
    public int Month { get; set; } // 1-12

    [BsonElement("year")]
    [Required(ErrorMessage = "Year is required")]
    public int Year { get; set; }

    [BsonElement("rent")]
    public decimal Rent { get; set; } // Calculated from offline expenses

    [BsonElement("cookSalary")]
    public decimal CookSalary { get; set; }

    [BsonElement("helperSalary")]
    public decimal HelperSalary { get; set; }

    [BsonElement("electricity")]
    public decimal Electricity { get; set; }

    [BsonElement("machineMaintenance")]
    public decimal MachineMaintenance { get; set; }

    [BsonElement("misc")]
    public decimal Misc { get; set; }

    [BsonElement("totalOperationalCost")]
    public decimal TotalOperationalCost { get; set; }

    [BsonElement("notes")]
    public string? Notes { get; set; }

    [BsonElement("recordedBy")]
    public string RecordedBy { get; set; } = string.Empty;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    // Transaction type is always Cash for operational expenses
    [BsonElement("transactionType")]
    public string TransactionType { get; set; } = "Cash";
}

// Request/Response DTOs
public class CreateOperationalExpenseRequest
{
    [Required(ErrorMessage = "Month is required")]
    [Range(1, 12, ErrorMessage = "Month must be between 1 and 12")]
    public int Month { get; set; }
    
    [Required(ErrorMessage = "Year is required")]
    [Range(2020, 2100, ErrorMessage = "Year must be between 2020 and 2100")]
    public int Year { get; set; }
    
    public decimal CookSalary { get; set; }
    public decimal HelperSalary { get; set; }
    public decimal Electricity { get; set; }
    public decimal MachineMaintenance { get; set; }
    public decimal Misc { get; set; }
    public string? Notes { get; set; }
}

public class UpdateOperationalExpenseRequest
{
    public decimal CookSalary { get; set; }
    public decimal HelperSalary { get; set; }
    public decimal Electricity { get; set; }
    public decimal MachineMaintenance { get; set; }
    public decimal Misc { get; set; }
    public string? Notes { get; set; }
}

public class OperationalExpenseSummary
{
    public int Month { get; set; }
    public int Year { get; set; }
    public decimal TotalRent { get; set; }
    public decimal TotalCookSalary { get; set; }
    public decimal TotalHelperSalary { get; set; }
    public decimal TotalElectricity { get; set; }
    public decimal TotalMachineMaintenance { get; set; }
    public decimal TotalMisc { get; set; }
    public decimal GrandTotal { get; set; }
}
