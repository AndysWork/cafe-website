using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Configuration;
using System.Net;

namespace Cafe.Api.Functions;

public class PaymentFunction
{
    private readonly IConfiguration _config;

    public PaymentFunction(IConfiguration config)
    {
        _config = config;
    }

    /// <summary>
    /// Returns runtime UPI payment configuration for QR rendering.
    /// </summary>
    [Function("GetUpiConfig")]
    [OpenApiOperation(operationId: "GetUpiConfig", tags: new[] { "Payments" }, Summary = "Get runtime UPI config")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(UpiConfigResponse), Description = "UPI config response")]
    public async Task<HttpResponseData> GetUpiConfig(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "payments/upi-config")] HttpRequestData req)
    {
        var upiQrEnabled = GetFeatureFlag("Payment__EnableUpiQr", true);
        var upiId = (_config["Upi:Id"] ?? _config["Upi__Id"] ?? Environment.GetEnvironmentVariable("Upi__Id") ?? string.Empty).Trim();
        var payeeName = (_config["Upi:PayeeName"] ?? _config["Upi__PayeeName"] ?? Environment.GetEnvironmentVariable("Upi__PayeeName") ?? "Cafe").Trim();
        var configured = upiQrEnabled && !string.IsNullOrWhiteSpace(upiId) && upiId.Contains('@');

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new UpiConfigResponse
        {
            Configured = configured,
            UpiId = upiQrEnabled ? upiId : string.Empty,
            PayeeName = payeeName,
            UpiQrEnabled = upiQrEnabled
        });
        return response;
    }

    private static bool GetFeatureFlag(string key, bool defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        return bool.TryParse(raw.Trim(), out var parsed) ? parsed : defaultValue;
    }
}

public class UpiConfigResponse
{
    public bool Configured { get; set; }
    public string UpiId { get; set; } = string.Empty;
    public string PayeeName { get; set; } = "Cafe";
    public bool UpiQrEnabled { get; set; }
}
