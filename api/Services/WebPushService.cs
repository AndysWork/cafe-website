using System.Text.Json;
using Cafe.Api.Models;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using WebPush;

namespace Cafe.Api.Services;

public class WebPushService
{
    private readonly IMongoCollection<User> _users;
    private readonly ILogger<WebPushService> _logger;
    private readonly string? _publicKey;
    private readonly string? _privateKey;
    private readonly string _subject;

    public WebPushService(IMongoDatabase db, ILogger<WebPushService> logger)
    {
        _users = db.GetCollection<User>("Users");
        _logger = logger;

        _publicKey = Environment.GetEnvironmentVariable("WebPush__PublicKey")
            ?? Environment.GetEnvironmentVariable("VAPID_PUBLIC_KEY");
        _privateKey = Environment.GetEnvironmentVariable("WebPush__PrivateKey")
            ?? Environment.GetEnvironmentVariable("VAPID_PRIVATE_KEY");
        _subject = Environment.GetEnvironmentVariable("WebPush__Subject")
            ?? Environment.GetEnvironmentVariable("VAPID_SUBJECT")
            ?? "mailto:ops@maataracafe.local";
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_publicKey) && !string.IsNullOrWhiteSpace(_privateKey);

    public string? PublicKey => _publicKey;

    public async Task UpsertSubscriptionAsync(string userId, RegisterWebPushSubscriptionRequest request)
    {
        var endpoint = request.Endpoint?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException("Endpoint is required", nameof(request.Endpoint));
        }

        var now = MongoService.GetIstNow();
        var existing = await _users.Find(u => u.Id == userId).Project(u => u.WebPushSubscriptions).FirstOrDefaultAsync() ?? new();
        var current = existing.FirstOrDefault(x => x.Endpoint == endpoint);

        if (current == null)
        {
            existing.Add(new WebPushSubscriptionDevice
            {
                Endpoint = endpoint,
                P256Dh = request.P256Dh,
                Auth = request.Auth,
                UserAgent = request.UserAgent,
                DeviceLabel = request.DeviceLabel,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            current.P256Dh = request.P256Dh;
            current.Auth = request.Auth;
            current.UserAgent = request.UserAgent;
            current.DeviceLabel = request.DeviceLabel;
            current.UpdatedAt = now;
        }

        await _users.UpdateOneAsync(
            u => u.Id == userId,
            Builders<User>.Update.Set(u => u.WebPushSubscriptions, existing));
    }

    public async Task RemoveSubscriptionAsync(string userId, string endpoint)
    {
        var cleaned = endpoint?.Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return;
        }

        await _users.UpdateOneAsync(
            u => u.Id == userId,
            Builders<User>.Update.PullFilter(u => u.WebPushSubscriptions, s => s.Endpoint == cleaned));
    }

    public async Task<int> SendToUsersAsync(IEnumerable<string> userIds, WebPushPayload payload)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("Web Push is not configured. Skipping push delivery.");
            return 0;
        }

        var ids = userIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        if (ids.Count == 0)
        {
            return 0;
        }

        var users = await _users.Find(u => ids.Contains(u.Id!) && u.IsActive)
            .Project(u => new { u.Id, u.WebPushSubscriptions, u.NotificationPreferences })
            .ToListAsync();

        var vapid = new VapidDetails(_subject, _publicKey!, _privateKey!);
        var client = new WebPushClient();
        var sent = 0;

        foreach (var user in users)
        {
            if (user.NotificationPreferences != null && !user.NotificationPreferences.PushNotifications)
            {
                continue;
            }

            foreach (var sub in user.WebPushSubscriptions ?? new List<WebPushSubscriptionDevice>())
            {
                try
                {
                    var subscription = new PushSubscription(sub.Endpoint, sub.P256Dh, sub.Auth);
                    var body = JsonSerializer.Serialize(new
                    {
                        title = payload.Title,
                        body = payload.Body,
                        data = payload.Data,
                        actions = payload.Actions
                    });

                    await client.SendNotificationAsync(subscription, body, vapid);
                    sent++;
                }
                catch (WebPushException ex)
                {
                    var status = (int?)ex.StatusCode;
                    if (status == 404 || status == 410)
                    {
                        await RemoveSubscriptionAsync(user.Id!, sub.Endpoint);
                    }

                    _logger.LogWarning(ex, "Web push send failed for user {UserId}, endpoint {Endpoint}", user.Id, sub.Endpoint);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Web push send error for user {UserId}", user.Id);
                }
            }
        }

        return sent;
    }
}
