using System.ComponentModel.DataAnnotations;
using Cafe.Api.Helpers;

namespace Cafe.Api.Models;

public class CreateCounterSaleRequest
{
    [Required(ErrorMessage = "Counter sale must contain at least one item")]
    [MinLength(1, ErrorMessage = "Counter sale must contain at least one item")]
    public List<OrderItemRequest> Items { get; set; } = new();

    [StringLength(120, ErrorMessage = "Customer name cannot exceed 120 characters")]
    public string? CustomerName { get; set; }

    [IndianPhoneNumber]
    public string? PhoneNumber { get; set; }

    [AllowedValuesList("cash_at_counter", "upi-qr")]
    public string PaymentMethod { get; set; } = "cash_at_counter";

    [StringLength(100, ErrorMessage = "UPI reference cannot exceed 100 characters")]
    public string? UpiReference { get; set; }

    [StringLength(500, ErrorMessage = "Notes cannot exceed 500 characters")]
    public string? Notes { get; set; }

    public bool PrintInvoice { get; set; } = true;

    public string? OutletId { get; set; }
}

public class CounterSaleInvoiceResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal Total { get; set; }
    public string ReceiptPdfPath { get; set; } = string.Empty;
    public bool PrintQueued { get; set; }
}
