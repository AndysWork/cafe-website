using System.ComponentModel.DataAnnotations;
using Cafe.Api.Helpers;

namespace Cafe.Api.Models;

/// <summary>A staff-recorded guest phone delivery, also used for a non-mutating price quote.</summary>
public sealed record CreatePhoneDeliveryOrderRequest
{
    public string? OutletId { get; init; }
    [Required, StringLength(100, MinimumLength = 1)]
    public string CustomerName { get; init; } = string.Empty;
    [Required, IndianPhoneNumber]
    public string PhoneNumber { get; init; } = string.Empty;
    [Required, StringLength(500)]
    public string DeliveryAddress { get; init; } = string.Empty;
    [Required, StringLength(2048)]
    public string DeliveryLocationUrl { get; init; } = string.Empty;
    [Required, MinLength(1), MaxLength(100)]
    public List<OrderItemRequest> Items { get; init; } = new();
    [Required, AllowedValuesList("cod", "cash_at_counter", "upi-qr")]
    public string PaymentMethod { get; init; } = "cod";
    [StringLength(100)]
    public string? UpiReference { get; init; }
    [StringLength(500)]
    public string? PreparationNotes { get; init; }
    [StringLength(500)]
    public string? Notes { get; init; }
    public decimal? ExpectedTotal { get; init; }
    [Range(typeof(decimal), "0", "10000")]
    public decimal? ManualDeliveryFee { get; init; }
    [Range(typeof(double), "0", "200")]
    public double? ManualApproximateDistanceKm { get; init; }
}
