using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Cafe.Api.Services;
using System.ComponentModel.DataAnnotations;

namespace Cafe.Api.Models;

public class CustomerWallet
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("balance")]
    public decimal Balance { get; set; }

    [BsonElement("totalCredited")]
    public decimal TotalCredited { get; set; }

    [BsonElement("totalDebited")]
    public decimal TotalDebited { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = MongoService.GetIstNow();
}

public class WalletTransaction
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("type")]
    public string Type { get; set; } = string.Empty; // credit, debit

    [BsonElement("amount")]
    public decimal Amount { get; set; }

    [BsonElement("balanceAfter")]
    public decimal BalanceAfter { get; set; }

    [BsonElement("description")]
    public string Description { get; set; } = string.Empty;

    [BsonElement("referenceId")]
    public string? ReferenceId { get; set; } // orderId, razorpayPaymentId, etc.

    [BsonElement("source")]
    public string Source { get; set; } = string.Empty; // recharge, order_payment, refund, cashback, bonus

    [BsonElement("razorpayPaymentId")]
    public string? RazorpayPaymentId { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();
}

public class WalletRechargeRequest
{
    [Range(10, 50000, ErrorMessage = "Recharge amount must be between ₹10 and ₹50,000")]
    public decimal Amount { get; set; }

    public string? RazorpayPaymentId { get; set; }
    public string? RazorpayOrderId { get; set; }
    public string? RazorpaySignature { get; set; }
}

public class WalletResponse
{
    public string Id { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public decimal TotalCredited { get; set; }
    public decimal TotalDebited { get; set; }
    public List<WalletTransaction> RecentTransactions { get; set; } = new();
}
