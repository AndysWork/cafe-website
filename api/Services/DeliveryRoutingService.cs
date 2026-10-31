using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
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

    public async Task<(double? distanceKm, int? etaMinutes)> ResolveDistanceForDeliveryFeeAsync(
        string outletId,
        string deliveryAddress,
        string? deliveryLocationUrl)
    {
        if (string.IsNullOrWhiteSpace(outletId) || string.IsNullOrWhiteSpace(deliveryAddress))
        {
            return (null, null);
        }

        var outlet = await _mongo.GetOutletByIdAsync(outletId);
        if (outlet == null || !outlet.IsActive)
        {
            return (null, null);
        }

        var originAddress = BuildOutletAddress(outlet);
        if (string.IsNullOrWhiteSpace(originAddress))
        {
            return (null, null);
        }

        if (TryExtractLatLngFromMapsUrl(deliveryLocationUrl, out var latitude, out var longitude))
        {
            return await TryResolveDistanceFromGoogleAsync(originAddress, null, latitude, longitude);
        }

        if (!string.IsNullOrWhiteSpace(deliveryLocationUrl))
        {
            var expandedUrl = await TryExpandMapsShortLinkAsync(deliveryLocationUrl);
            if (!string.IsNullOrWhiteSpace(expandedUrl)
                && TryExtractLatLngFromMapsUrl(expandedUrl, out latitude, out longitude))
            {
                return await TryResolveDistanceFromGoogleAsync(originAddress, null, latitude, longitude);
            }
        }

        return await TryResolveDistanceFromGoogleAsync(originAddress, deliveryAddress.Trim(), null, null);
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
        var parts = new[] { outlet.OutletName, outlet.Address, outlet.City, outlet.State }
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

    private async Task<(double? distanceKm, int? etaMinutes)> TryResolveDistanceFromGoogleAsync(
        string originAddress,
        string? destinationAddress,
        double? destinationLatitude,
        double? destinationLongitude)
    {
        var apiKey = Environment.GetEnvironmentVariable("GoogleMaps__ApiKey")?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return (null, null);
        }

        var destinationParam = BuildDestinationParameter(destinationAddress, destinationLatitude, destinationLongitude);
        if (string.IsNullOrWhiteSpace(destinationParam))
        {
            return (null, null);
        }

        var routesError = string.Empty;
        try
        {
            var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://routes.googleapis.com/directions/v2:computeRoutes");
            request.Headers.Add("X-Goog-Api-Key", apiKey);
            request.Headers.Add("X-Goog-FieldMask", "routes.distanceMeters,routes.duration");

            object destinationPayload;
            if (destinationLatitude.HasValue && destinationLongitude.HasValue)
            {
                destinationPayload = new
                {
                    location = new
                    {
                        latLng = new
                        {
                            latitude = destinationLatitude.Value,
                            longitude = destinationLongitude.Value
                        }
                    }
                };
            }
            else if (!string.IsNullOrWhiteSpace(destinationAddress))
            {
                destinationPayload = new
                {
                    address = destinationAddress
                };
            }
            else
            {
                destinationPayload = new
                {
                    address = destinationParam
                };
            }

            var payload = new
            {
                origin = new
                {
                    address = originAddress
                },
                destination = destinationPayload,
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
                routesError = "Routes API response has no routes";
                return await TryResolveDistanceFromLegacyDirectionsAsync(client, apiKey, originAddress, destinationParam, routesError);
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);

            var root = doc.RootElement;
            if (!root.TryGetProperty("routes", out var routes) || routes.GetArrayLength() == 0)
            {
                routesError = "Routes API response has no distanceMeters";
                return await TryResolveDistanceFromLegacyDirectionsAsync(client, apiKey, originAddress, destinationParam, routesError);
            }

            var firstRoute = routes[0];
            if (!firstRoute.TryGetProperty("distanceMeters", out var distanceMetersJson))
            {
                routesError = "Routes API response has no duration";
                return await TryResolveDistanceFromLegacyDirectionsAsync(client, apiKey, originAddress, destinationParam, routesError);
            }

            if (!firstRoute.TryGetProperty("duration", out var durationJson))
            {
                return (null, null);
            }

            var distanceMeters = distanceMetersJson.GetDouble();
            var durationSeconds = ParseDurationSeconds(durationJson.GetString());
            if (durationSeconds <= 0)
            {
                routesError = "Routes API duration parsed as zero";
                return await TryResolveDistanceFromLegacyDirectionsAsync(client, apiKey, originAddress, destinationParam, routesError);
            }

            var km = Math.Round(distanceMeters / 1000d, 1, MidpointRounding.AwayFromZero);
            var eta = Math.Max(1, (int)Math.Ceiling(durationSeconds / 60d));
            return (km, eta);
        }
        catch (Exception ex)
        {
            var client = _httpClientFactory.CreateClient();
            return await TryResolveDistanceFromLegacyDirectionsAsync(client, apiKey, originAddress, destinationParam, ex.Message);
        }
    }

    private async Task<(double? distanceKm, int? etaMinutes)> TryResolveDistanceFromGoogleAsync(string originAddress, string destinationAddress)
    {
        return await TryResolveDistanceFromGoogleAsync(originAddress, destinationAddress, null, null);
    }

    private static bool TryExtractLatLngFromMapsUrl(string? url, out double latitude, out double longitude)
    {
        latitude = 0;
        longitude = 0;

        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        var candidates = new List<string>();
        var queryValues = ParseQueryValues(uri.Query);
        queryValues.TryGetValue("q", out var qValue);
        queryValues.TryGetValue("query", out var queryValue);
        queryValues.TryGetValue("ll", out var llValue);
        candidates.Add(qValue ?? string.Empty);
        candidates.Add(queryValue ?? string.Empty);
        candidates.Add(llValue ?? string.Empty);
        candidates.Add(uri.AbsolutePath);
        candidates.Add(uri.Fragment);

        var coordinatePattern = new Regex(@"(-?\d{1,2}(?:\.\d+)?),\s*(-?\d{1,3}(?:\.\d+)?)", RegexOptions.Compiled);
        var bangPattern = new Regex(@"!3d(-?\d{1,2}(?:\.\d+)?)!4d(-?\d{1,3}(?:\.\d+)?)", RegexOptions.Compiled);
        var atPattern = new Regex(@"@(-?\d{1,2}(?:\.\d+)?),(-?\d{1,3}(?:\.\d+)?)", RegexOptions.Compiled);
        foreach (var candidate in candidates.Where(c => !string.IsNullOrWhiteSpace(c)))
        {
            var match = coordinatePattern.Match(candidate);
            if (!match.Success) match = bangPattern.Match(candidate);
            if (!match.Success) match = atPattern.Match(candidate);
            if (!match.Success) continue;
            if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)) continue;
            if (!double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var lng)) continue;
            if (lat < -90 || lat > 90 || lng < -180 || lng > 180) continue;
            latitude = lat;
            longitude = lng;
            return true;
        }

        return false;
    }

    private static Dictionary<string, string> ParseQueryValues(string rawQuery)
    {
        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var query = rawQuery.StartsWith("?") ? rawQuery[1..] : rawQuery;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var segment in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var index = segment.IndexOf('=');
            var key = index >= 0 ? segment[..index] : segment;
            var value = index >= 0 ? segment[(index + 1)..] : string.Empty;
            if (string.IsNullOrWhiteSpace(key)) continue;
            values[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value);
        }

        return values;
    }

    private async Task<string?> TryExpandMapsShortLinkAsync(string originalUrl)
    {
        if (string.IsNullOrWhiteSpace(originalUrl) || !Uri.TryCreate(originalUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        var host = uri.Host.ToLowerInvariant();
        if (host != "maps.app.goo.gl" && host != "goo.gl")
        {
            return originalUrl;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            return response.RequestMessage?.RequestUri?.ToString() ?? originalUrl;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not expand Google Maps short link for distance resolution");
            return originalUrl;
        }
    }

    private static string? BuildDestinationParameter(string? destinationAddress, double? destinationLatitude, double? destinationLongitude)
    {
        if (destinationLatitude.HasValue && destinationLongitude.HasValue)
        {
            return FormattableString.Invariant($"{destinationLatitude.Value},{destinationLongitude.Value}");
        }

        if (!string.IsNullOrWhiteSpace(destinationAddress))
        {
            return destinationAddress.Trim();
        }

        return null;
    }

    private async Task<(double? distanceKm, int? etaMinutes)> TryResolveDistanceFromLegacyDirectionsAsync(
        HttpClient client,
        string apiKey,
        string originAddress,
        string destinationParam,
        string reason)
    {
        try
        {
            var url = "https://maps.googleapis.com/maps/api/directions/json"
                + $"?origin={Uri.EscapeDataString(originAddress)}"
                + $"&destination={Uri.EscapeDataString(destinationParam)}"
                + "&mode=driving"
                + "&units=metric"
                + $"&key={Uri.EscapeDataString(apiKey)}";

            using var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Google Routes fallback failed: {Reason}. Directions API returned {StatusCode}", reason, (int)response.StatusCode);
                return (null, null);
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            var root = doc.RootElement;
            var status = root.TryGetProperty("status", out var statusProp) ? statusProp.GetString() : null;
            if (!string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Google Routes fallback failed: {Reason}. Directions status: {Status}", reason, status ?? "unknown");
                return (null, null);
            }

            if (!root.TryGetProperty("routes", out var routes) || routes.GetArrayLength() == 0)
            {
                return (null, null);
            }

            var legs = routes[0].TryGetProperty("legs", out var legArray) ? legArray : default;
            if (legs.ValueKind != JsonValueKind.Array || legs.GetArrayLength() == 0)
            {
                return (null, null);
            }

            var leg = legs[0];
            if (!leg.TryGetProperty("distance", out var distanceObj)
                || !distanceObj.TryGetProperty("value", out var distanceValue)
                || !leg.TryGetProperty("duration", out var durationObj)
                || !durationObj.TryGetProperty("value", out var durationValue))
            {
                return (null, null);
            }

            var meters = distanceValue.GetDouble();
            var seconds = durationValue.GetDouble();
            if (meters <= 0 || seconds <= 0)
            {
                return (null, null);
            }

            var km = Math.Round(meters / 1000d, 1, MidpointRounding.AwayFromZero);
            var eta = Math.Max(1, (int)Math.Ceiling(seconds / 60d));
            return (km, eta);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed both Google Routes and Directions API for route distance resolution. Initial reason: {Reason}", reason);
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
        return double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ? seconds : 0;
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