using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Helpers;
using System.Net;
using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;

namespace Cafe.Api.Functions;

public class PaymentFunction
{
    private readonly IRazorpayService _razorpay;
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public PaymentFunction(IRazorpayService razorpay, MongoService mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _razorpay = razorpay;
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<PaymentFunction>();
    }

    /// <summary>
    /// Creates a Razorpay order for payment processing
    /// </summary>
    [Function("CreatePaymentOrder")]
    [OpenApiOperation(operationId: "CreatePaymentOrder", tags: new[] { "Payments" }, Summary = "Create Razorpay payment order")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(CreatePaymentOrderRequest), Required = true)]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(CreatePaymentOrderResponse), Description = "Payment order created")]
    public async Task<HttpResponseData> CreatePaymentOrder(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "payments/create-order")] HttpRequestData req)
    {
        try
        {
            // Validate authentication
            var authHeader = req.Headers.GetValues("Authorization").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Authentication required" });
                return unauthorized;
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();
            var principal = _auth.ValidateToken(token);

            if (principal == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Invalid or expired token" });
                return unauthorized;
            }

            var request = await req.ReadFromJsonAsync<CreatePaymentOrderRequest>();
            if (request == null || request.Amount <= 0)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Valid amount is required" });
                return badRequest;
            }

            var razorpayOrder = await _razorpay.CreateOrderAsync(request.Amount, request.Receipt ?? $"order_{DateTime.UtcNow.Ticks}");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new CreatePaymentOrderResponse
            {
                OrderId = razorpayOrder.Id,
                Amount = razorpayOrder.Amount,
                Currency = razorpayOrder.Currency,
                KeyId = _razorpay.GetKeyId()
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating payment order");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "Failed to create payment order" });
            return res;
        }
    }

    /// <summary>
    /// Verifies a Razorpay payment signature and updates order payment status
    /// </summary>
    [Function("VerifyPayment")]
    [OpenApiOperation(operationId: "VerifyPayment", tags: new[] { "Payments" }, Summary = "Verify Razorpay payment")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(VerifyPaymentRequest), Required = true)]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Payment verified")]
    public async Task<HttpResponseData> VerifyPayment(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "payments/verify")] HttpRequestData req)
    {
        try
        {
            // Validate authentication
            var authHeader = req.Headers.GetValues("Authorization").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Authentication required" });
                return unauthorized;
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();
            var principal = _auth.ValidateToken(token);

            if (principal == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Invalid or expired token" });
                return unauthorized;
            }

            var request = await req.ReadFromJsonAsync<VerifyPaymentRequest>();
            if (request == null || string.IsNullOrEmpty(request.RazorpayOrderId) ||
                string.IsNullOrEmpty(request.RazorpayPaymentId) || string.IsNullOrEmpty(request.RazorpaySignature))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "All payment verification fields are required" });
                return badRequest;
            }

            // Verify the signature
            var isValid = _razorpay.VerifyPaymentSignature(
                request.RazorpayOrderId,
                request.RazorpayPaymentId,
                request.RazorpaySignature
            );

            if (!isValid)
            {
                _log.LogWarning("Payment verification failed for Razorpay order {OrderId}", request.RazorpayOrderId);
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Payment verification failed. Invalid signature." });
                return badRequest;
            }

            // Update order payment status if orderId is provided
            if (!string.IsNullOrEmpty(request.OrderId))
            {
                await _mongo.UpdatePaymentStatusAsync(
                    request.OrderId,
                    "paid",
                    request.RazorpayPaymentId,
                    request.RazorpaySignature
                );
                _log.LogInformation("Payment verified for order {OrderId}, Razorpay payment {PaymentId}", request.OrderId, request.RazorpayPaymentId);
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, message = "Payment verified successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error verifying payment");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "Failed to verify payment" });
            return res;
        }
    }

    /// <summary>
    /// Processes a refund for a Razorpay payment (admin only)
    /// </summary>
    [Function("RefundPayment")]
    [OpenApiOperation(operationId: "RefundPayment", tags: new[] { "Payments" }, Summary = "Refund a Razorpay payment")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(RefundPaymentRequest), Required = true)]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Refund processed")]
    public async Task<HttpResponseData> RefundPayment(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "payments/refund")] HttpRequestData req)
    {
        try
        {
            // Validate admin authentication
            var authHeader = req.Headers.GetValues("Authorization").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Authentication required" });
                return unauthorized;
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();
            var principal = _auth.ValidateToken(token);

            if (principal == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Invalid or expired token" });
                return unauthorized;
            }

            // Admin check
            var role = principal.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            if (role != "admin")
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Admin access required" });
                return forbidden;
            }

            var request = await req.ReadFromJsonAsync<RefundPaymentRequest>();
            if (request == null || string.IsNullOrEmpty(request.OrderId))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Order ID is required" });
                return badRequest;
            }

            // Get the order
            var order = await _mongo.GetOrderByIdAsync(request.OrderId);
            if (order == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            if (order.PaymentMethod != "razorpay" || string.IsNullOrEmpty(order.RazorpayPaymentId))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "This order was not paid via Razorpay" });
                return badRequest;
            }

            if (order.PaymentStatus == "refunded")
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "This order has already been refunded" });
                return badRequest;
            }

            // Process refund via Razorpay
            var refundAmount = request.Amount > 0 ? request.Amount : order.Total;
            var refundResult = await _razorpay.RefundPaymentAsync(
                order.RazorpayPaymentId,
                refundAmount,
                request.Reason
            );

            // Update order payment status to refunded
            await _mongo.UpdatePaymentStatusAsync(request.OrderId, "refunded");

            // Store refund ID
            await _mongo.UpdateRefundIdAsync(request.OrderId, refundResult.Id);

            _log.LogInformation("Refund {RefundId} processed for order {OrderId}, amount ₹{Amount}",
                refundResult.Id, request.OrderId, refundAmount);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                refundId = refundResult.Id,
                amount = refundAmount,
                status = refundResult.Status,
                message = "Refund processed successfully"
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error processing refund");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "Failed to process refund" });
            return res;
        }
    }
}

// DTOs for Payment endpoints
public class CreatePaymentOrderRequest
{
    [Required]
    [Range(1, 10000000)]
    public decimal Amount { get; set; }
    public string? Receipt { get; set; }
}

public class CreatePaymentOrderResponse
{
    public string OrderId { get; set; } = string.Empty;
    public long Amount { get; set; }
    public string Currency { get; set; } = "INR";
    public string KeyId { get; set; } = string.Empty;
}

public class VerifyPaymentRequest
{
    [Required]
    public string RazorpayOrderId { get; set; } = string.Empty;
    [Required]
    public string RazorpayPaymentId { get; set; } = string.Empty;
    [Required]
    public string RazorpaySignature { get; set; } = string.Empty;
    public string? OrderId { get; set; }
}

public class RefundPaymentRequest
{
    [Required]
    public string OrderId { get; set; } = string.Empty;
    public decimal Amount { get; set; } // 0 = full refund
    public string? Reason { get; set; }
}
