namespace Cafe.Api.Services;

public interface IRazorpayService
{
    Task<RazorpayOrderResponse> CreateOrderAsync(decimal amount, string receipt, string currency = "INR");
    bool VerifyPaymentSignature(string orderId, string paymentId, string signature);
    Task<RazorpayRefundResponse> RefundPaymentAsync(string paymentId, decimal amount, string? reason = null);
    string GetKeyId();
}

public class RazorpayOrderResponse
{
    public string Id { get; set; } = string.Empty;
    public long Amount { get; set; }
    public string Currency { get; set; } = "INR";
    public string Receipt { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class RazorpayRefundResponse
{
    public string Id { get; set; } = string.Empty;
    public string Entity { get; set; } = string.Empty;
    public long Amount { get; set; }
    public string Currency { get; set; } = "INR";
    public string Status { get; set; } = string.Empty;
}
