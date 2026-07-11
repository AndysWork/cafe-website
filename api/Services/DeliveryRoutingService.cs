using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cafe.Api.Models;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Cafe.Api.Services;

public class DeliveryRoutingService
{
    private readonly MongoService _mongo;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DeliveryRoutingService> _logger;
    private readonly IMongoCollection<DeliveryRouteLink> _routeLinks;

    public DeliveryRoutingService(
        MongoService mongo,
        IHttpClientFactory httpClientFactory,
        ILogger<DeliveryRoutingService> logger)
    {
        _mongo = mongo;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _routeLinks = _mongo.Database.GetCollection<DeliveryRouteLink>("DeliveryRouteLinks");
    }

    public async Task<DeliveryRouteQuoteResponse?> BuildRouteQuoteAsync(string outletId, string destinationAddress)
    {
        if (string.IsNullOrWhiteSpace(outletId) || string.IsNullOrWhiteSpace(destinationAddress))
        {
            return null;
        }

        var outlet = await _mongo.GetOutletByIdAsync(outletId);
        if (outlet == null || !outlet.IsActive)
        {
            return null;
        }

        var originAddress = BuildOutletAddress(outlet);
        if (string.IsNullOrWhiteSpace(originAddress))
        {
            return null;
        }

        var destination = destinationAddress.Trim();
        var mapUrl = BuildDirectionsUrl(originAddress, destination);

        double? distanceKm = null;
        int? etaMinutes = null;

        var fromGoogle = await TryResolveDistanceFromGoogleAsync(originAddress, destination);
        if (fromGoogle.distanceKm.HasValue)
        {
            distanceKm = fromGoogle.distanceKm;
            etaMinutes = fromGoogle.etaMinutes;
        }
        else
        {
            var zones = await _mongo.GetActiveDeliveryZonesAsync(outletId);
            var closestZone = zones.OrderBy(z => z.MinDistance).FirstOrDefault();
            if (closestZone != null)
            {
                distanceKm = Math.Round((closestZone.MinDistance + closestZone.MaxDistance) / 2d, 1);
                etaMinutes = closestZone.EstimatedMinutes;
            }
        }

        return new DeliveryRouteQuoteResponse
        {
            MapUrl = mapUrl,
            OriginAddress = originAddress,
            DestinationAddress = destination,
            DistanceKm = distanceKm,
            EtaMinutes = etaMinutes,
            Provider = "google"
        };
    }

    public async Task<DeliveryRouteQuoteResponse?> BuildPointToPointRouteQuoteAsync(string originAddress, string destinationAddress)
    {
        if (string.IsNullOrWhiteSpace(originAddress) || string.IsNullOrWhiteSpace(destinationAddress))
        {
            return null;
        }

        var origin = originAddress.Trim();
        var destination = destinationAddress.Trim();
        var mapUrl = BuildDirectionsUrl(origin, destination);

        var fromGoogle = await TryResolveDistanceFromGoogleAsync(origin, destination);

        return new DeliveryRouteQuoteResponse
        {
            MapUrl = mapUrl,
            OriginAddress = origin,
            DestinationAddress = destination,
            DistanceKm = fromGoogle.distanceKm,
            EtaMinutes = fromGoogle.etaMinutes,
            Provider = "google"
        };
    }

    public async Task<DeliveryRouteLink> CreateOrReuseShortLinkAsync(
        string outletId,
        string destinationAddress,
        string fullMapUrl,
        string? orderId,
        double? distanceKm,
        int? etaMinutes)
    {
        if (!string.IsNullOrWhiteSpace(orderId))
        {
            var existing = await _routeLinks
                .Find(l => l.OrderId == orderId && l.ExpiresAt > MongoService.GetIstNow())
                .SortByDescending(l => l.CreatedAt)
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                return existing;
            }
        }

        var code = await GenerateUniqueCodeAsync();
        var shortUrl = BuildShortUrl(code);
        var now = MongoService.GetIstNow();

        var link = new DeliveryRouteLink
        {
            Code = code,
            FullMapUrl = fullMapUrl,
            ShortUrl = shortUrl,
            OrderId = string.IsNullOrWhiteSpace(orderId) ? null : orderId,
            OutletId = outletId,
            DestinationAddress = destinationAddress,
            DistanceKm = distanceKm,
            EtaMinutes = etaMinutes,
            CreatedAt = now,
            ExpiresAt = now.AddDays(14)
        };

        await _routeLinks.InsertOneAsync(link);
        return link;
    }

    public async Task<DeliveryRouteLink?> ResolveShortCodeAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        var normalized = code.Trim();
        var now = MongoService.GetIstNow();
        return await _routeLinks.Find(l => l.Code == normalized && l.ExpiresAt > now).FirstOrDefaultAsync();
    }

    public string BuildShortUrl(string code)
    {
        var baseUrl = Environment.GetEnvironmentVariable("App__BaseUrl")?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            var hostName = Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME")?.Trim();
            if (!string.IsNullOrWhiteSpace(hostName))
            {
                baseUrl = $"https://{hostName}";
            }
            else
            {
                baseUrl = "http://localhost:7071";
            }
        }

        return $"{baseUrl.TrimEnd('/')}/api/r/{code}";
    }

    private static string BuildOutletAddress(Outlet outlet)
    {
        var parts = new[] { outlet.Address, outlet.City, outlet.State }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim());
        return string.Join(", ", parts);
    }

    private static string BuildDirectionsUrl(string originAddress, string destinationAddress)
    {
        var origin = Uri.EscapeDataString(originAddress);
        var destination = Uri.EscapeDataString(destinationAddress);
        return $"https://www.google.com/maps/dir/?api=1&origin={origin}&destination={destination}&travelmode=driving";
    }

    private async Task<(double? distanceKm, int? etaMinutes)> TryResolveDistanceFromGoogleAsync(string originAddress, string destinationAddress)
    {
        var apiKey = Environment.GetEnvironmentVariable("GoogleMaps__ApiKey")?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return (null, null);
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://routes.googleapis.com/directions/v2:computeRoutes");
            request.Headers.Add("X-Goog-Api-Key", apiKey);
            request.Headers.Add("X-Goog-FieldMask", "routes.distanceMeters,routes.duration");

            var payload = new
            {
                origin = new
                {
                    address = originAddress
                },
                destination = new
                {
                    address = destinationAddress
                },
                travelMode = "DRIVE",
                routingPreference = "TRAFFIC_AWARE",
                computeAlternativeRoutes = false,
                languageCode = "en-US",
                units = "METRIC"
            };

            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return (null, null);
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);

            var root = doc.RootElement;
            if (!root.TryGetProperty("routes", out var routes) || routes.GetArrayLength() == 0)
            {
                return (null, null);
            }

            var firstRoute = routes[0];
            if (!firstRoute.TryGetProperty("distanceMeters", out var distanceMetersJson))
            {
                return (null, null);
            }

            if (!firstRoute.TryGetProperty("duration", out var durationJson))
            {
                return (null, null);
            }

            var distanceMeters = distanceMetersJson.GetDouble();
            var durationSeconds = ParseDurationSeconds(durationJson.GetString());
            if (durationSeconds <= 0)
            {
                return (null, null);
            }

            var km = Math.Round(distanceMeters / 1000d, 1, MidpointRounding.AwayFromZero);
            var eta = Math.Max(1, (int)Math.Ceiling(durationSeconds / 60d));
            return (km, eta);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve route distance via Google Routes API");
            return (null, null);
        }
    }

    private static double ParseDurationSeconds(string? durationText)
    {
        if (string.IsNullOrWhiteSpace(durationText))
        {
            return 0;
        }

        var trimmed = durationText.Trim();
        if (!trimmed.EndsWith("s", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var numeric = trimmed[..^1];
        return double.TryParse(numeric, out var seconds) ? seconds : 0;
    }

    private async Task<string> GenerateUniqueCodeAsync()
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var code = GenerateCode(8);
            var exists = await _routeLinks.Find(l => l.Code == code).AnyAsync();
            if (!exists)
            {
                return code;
            }
        }

        return GenerateCode(10);
    }

    private static string GenerateCode(int length)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);

        var sb = new StringBuilder(length);
        for (var i = 0; i < bytes.Length; i++)
        {
            sb.Append(alphabet[bytes[i] % alphabet.Length]);
        }

        return sb.ToString();
    }
}