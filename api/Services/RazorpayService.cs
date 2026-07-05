using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Services;

public class RazorpayService : IRazorpayService
{
    private readonly HttpClient _httpClient;
    private readonly string _keyId;
    private readonly string _keySecret;
    private readonly string _webhookSecret;
    private readonly ILogger<RazorpayService> _logger;
    private const string RazorpayBaseUrl = "https://api.razorpay.com/v1";

    public RazorpayService(IHttpClientFactory httpClientFactory, IConfiguration config, ILogger<RazorpayService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("Razorpay");
        _keyId = (config["Razorpay__KeyId"] ?? string.Empty).Trim();
        _keySecret = (config["Razorpay__KeySecret"] ?? string.Empty).Trim();
        _webhookSecret = config["Razorpay__WebhookSecret"] ?? string.Empty;
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(_keyId) && !string.IsNullOrWhiteSpace(_keySecret))
        {
            // Set Basic Auth header only when credentials are configured.
            var authBytes = Encoding.ASCII.GetBytes($"{_keyId}:{_keySecret}");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
        }
        else
        {
            _logger.LogWarning("Razorpay credentials are not configured. Payment order/refund endpoints will be unavailable until Razorpay__KeyId and Razorpay__KeySecret are set.");
        }
    }

    public string GetKeyId() => _keyId;

    public async Task<RazorpayOrderResponse> CreateOrderAsync(decimal amount, string receipt, string currency = "INR")
    {
        EnsureConfigured();

        // Razorpay expects amount in paise (smallest currency unit)
        var amountInPaise = (long)(amount * 100);

        var payload = new
        {
            amount = amountInPaise,
            currency,
            receipt
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"{RazorpayBaseUrl}/orders", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Razorpay order creation failed: {StatusCode} - {Body}", response.StatusCode, responseBody);
            throw new Exception($"Failed to create Razorpay order: {responseBody}");
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var razorpayOrder = JsonSerializer.Deserialize<RazorpayOrderResponse>(responseBody, options);

        if (razorpayOrder == null)
            throw new Exception("Failed to deserialize Razorpay order response");

        _logger.LogInformation("Razorpay order created: {OrderId} for amount {Amount} paise", razorpayOrder.Id, amountInPaise);
        return razorpayOrder;
    }

    public bool VerifyPaymentSignature(string orderId, string paymentId, string signature)
    {
        EnsureConfigured();

        var payload = $"{orderId}|{paymentId}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_keySecret));
        var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var computedSignature = BitConverter.ToString(computedHash).Replace("-", "").ToLowerInvariant();

        // Use constant-time comparison to prevent timing attacks
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedSignature),
            Encoding.UTF8.GetBytes(signature?.ToLowerInvariant() ?? ""));
    }

    public bool VerifyWebhookSignature(string payload, string signature)
    {
        if (string.IsNullOrWhiteSpace(_webhookSecret) || string.IsNullOrWhiteSpace(signature) || payload == null)
        {
            _logger.LogWarning("Razorpay webhook secret/signature/payload missing. Verification failed.");
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_webhookSecret));
        var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var computedSignature = BitConverter.ToString(computedHash).Replace("-", "").ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedSignature),
            Encoding.UTF8.GetBytes(signature.ToLowerInvariant()));
    }

    public async Task<RazorpayRefundResponse> RefundPaymentAsync(string paymentId, decimal amount, string? reason = null)
    {
        EnsureConfigured();

        var amountInPaise = (long)(amount * 100);

        var payload = new Dictionary<string, object>
        {
            { "amount", amountInPaise }
        };

        if (!string.IsNullOrEmpty(reason))
            payload["notes"] = new Dictionary<string, string> { { "reason", reason } };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"{RazorpayBaseUrl}/payments/{paymentId}/refund", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Razorpay refund failed: {StatusCode} - {Body}", response.StatusCode, responseBody);
            throw new Exception($"Failed to process refund: {responseBody}");
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var refundResponse = JsonSerializer.Deserialize<RazorpayRefundResponse>(responseBody, options);

        if (refundResponse == null)
            throw new Exception("Failed to deserialize Razorpay refund response");

        _logger.LogInformation("Razorpay refund processed: {RefundId} for payment {PaymentId}, amount {Amount} paise",
            refundResponse.Id, paymentId, amountInPaise);
        return refundResponse;
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_keyId) || string.IsNullOrWhiteSpace(_keySecret))
        {
            throw new InvalidOperationException("Razorpay credentials are not configured");
        }
    }
}
