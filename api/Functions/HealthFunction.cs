using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;
using MongoDB.Driver;

namespace Cafe.Api.Functions;

public class HealthFunction
{
    private readonly IMongoClient _client;
    private readonly IMongoDatabase _database;

    public HealthFunction(IMongoClient client, IMongoDatabase database)
    {
        _client = client;
        _database = database;
    }

    [Function("HealthCheck")]
    public async Task<HttpResponseData> HealthCheck(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData req)
    {
        var response = req.CreateResponse();

        try
        {
            // Ping MongoDB to verify connectivity
            await _database.RunCommandAsync<MongoDB.Bson.BsonDocument>(new MongoDB.Bson.BsonDocument("ping", 1));

            // Report connection pool stats from the cluster description
            var clusterDescription = _client.Cluster.Description;
            var serverCount = clusterDescription.Servers.Count;
            var connectedServers = clusterDescription.Servers.Count(s => s.State == MongoDB.Driver.Core.Servers.ServerState.Connected);

            response.StatusCode = HttpStatusCode.OK;
            await response.WriteAsJsonAsync(new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow,
                database = "connected",
                cluster = new
                {
                    type = clusterDescription.Type.ToString(),
                    servers = serverCount,
                    connectedServers
                }
            });
        }
        catch (Exception)
        {
            response.StatusCode = HttpStatusCode.ServiceUnavailable;
            await response.WriteAsJsonAsync(new
            {
                status = "unhealthy",
                timestamp = DateTime.UtcNow,
                database = "disconnected"
            });
        }

        return response;
    }
}
