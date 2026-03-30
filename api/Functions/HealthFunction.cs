using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;
using Azure.Storage.Blobs;
using MongoDB.Driver;

namespace Cafe.Api.Functions;

public class HealthFunction
{
    private readonly IMongoClient _client;
    private readonly IMongoDatabase _database;
    private readonly BlobServiceClient _blobService;

    public HealthFunction(IMongoClient client, IMongoDatabase database, BlobServiceClient blobService)
    {
        _client = client;
        _database = database;
        _blobService = blobService;
    }

    [Function("HealthCheck")]
    public async Task<HttpResponseData> HealthCheck(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData req)
    {
        var response = req.CreateResponse();
        var checks = new Dictionary<string, object>();
        var criticalHealthy = true;
        var degraded = false;

        // 1. MongoDB — critical dependency
        try
        {
            await _database.RunCommandAsync<MongoDB.Bson.BsonDocument>(new MongoDB.Bson.BsonDocument("ping", 1));
            var clusterDescription = _client.Cluster.Description;
            var serverCount = clusterDescription.Servers.Count;
            var connectedServers = clusterDescription.Servers.Count(s => s.State == MongoDB.Driver.Core.Servers.ServerState.Connected);

            checks["mongodb"] = new
            {
                status = "healthy",
                cluster = new
                {
                    type = clusterDescription.Type.ToString(),
                    servers = serverCount,
                    connectedServers
                }
            };
        }
        catch (Exception ex)
        {
            criticalHealthy = false;
            checks["mongodb"] = new { status = "unhealthy", error = ex.Message };
        }

        // 2. Azure Blob Storage — critical for images/backups
        try
        {
            await _blobService.GetPropertiesAsync();
            checks["blobStorage"] = new { status = "healthy" };
        }
        catch (Exception ex)
        {
            degraded = true;
            checks["blobStorage"] = new { status = "unhealthy", error = ex.Message };
        }

        // 3. Email service — non-critical (config check)
        checks["email"] = CheckServiceConfig("Email__SmtpHost", "Email service");

        // 4. Razorpay — non-critical (config check)
        checks["razorpay"] = CheckServiceConfig("Razorpay__KeyId", "Razorpay payment gateway");

        // 5. WhatsApp / Twilio — non-critical (config check)
        checks["whatsapp"] = CheckServiceConfig("Twilio__AccountSid", "Twilio WhatsApp service");

        // Determine overall status
        var overallStatus = !criticalHealthy ? "unhealthy" : degraded ? "degraded" : "healthy";
        response.StatusCode = criticalHealthy ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable;

        await response.WriteAsJsonAsync(new
        {
            status = overallStatus,
            timestamp = DateTime.UtcNow,
            checks
        });

        return response;
    }

    private static object CheckServiceConfig(string envVar, string serviceName)
    {
        var configured = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(envVar));
        return new
        {
            status = configured ? "configured" : "not_configured",
            detail = configured ? $"{serviceName} configuration present" : $"{serviceName} configuration missing"
        };
    }
}
