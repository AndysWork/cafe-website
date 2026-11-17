using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Cafe.Api.Helpers;
using Cafe.Api.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Cafe.Api.Services;

public sealed class CustomerJourneyService : ICustomerJourneyService
{
    private const int MaxReportEvents = 200000;
    private const int MaxReportSessions = 20000;
    private readonly IMongoCollection<JourneySession> _sessions;
    private readonly IMongoCollection<JourneyEvent> _events;
    private readonly IMongoCollection<CafeMenuItem> _menu;
    private readonly IMongoCollection<MenuCategory> _categories;
    private readonly IMongoCollection<Order> _orders;
    private readonly IMongoCollection<User> _users;
    private readonly IMongoCollection<Outlet> _outlets;
    private readonly IMongoCollection<UserActivityEvent> _legacyEvents;
    private readonly IMongoCollection<UserSession> _legacySessions;
    private readonly SemaphoreSlim _indexLock = new(1, 1);
    private bool _indexed;

    private static readonly HashSet<string> BrowserEvents = new(StringComparer.Ordinal)
    {
        "page_view", "page_engagement", "menu_item_view", "menu_item_click", "category_view",
        "cart_add", "cart_remove", "cart_update", "cart_view", "checkout_started",
        "checkout_updated", "session_end", "session_continued", "heartbeat"
    };
    private static readonly HashSet<string> Pages = new(StringComparer.Ordinal)
    {
        "/", "/home", "/menu", "/cart", "/checkout", "/orders", "/my-orders", "/profile",
        "/login", "/register", "/forgot-password", "/reset-password", "/subscriptions",
        "/my-subscriptions", "/offers", "/loyalty", "/contact", "/about", "/admin",
        "/manager", "/kitchen", "/staff", "/partner", "/phone-orders", "/privacy", "/terms", "/other",
        "/dashboard", "/dine-in", "/track-order", "/reviews", "/reservations", "/finance"
    };

    public CustomerJourneyService(IMongoDatabase database)
    {
        _sessions = database.GetCollection<JourneySession>("JourneySessions");
        _events = database.GetCollection<JourneyEvent>("JourneyEvents");
        _menu = database.GetCollection<CafeMenuItem>("CafeMenu");
        _categories = database.GetCollection<MenuCategory>("MenuCategory");
        _orders = database.GetCollection<Order>("Orders");
        _users = database.GetCollection<User>("Users");
        _outlets = database.GetCollection<Outlet>("Outlets");
        _legacyEvents = database.GetCollection<UserActivityEvent>("UserActivityEvents");
        _legacySessions = database.GetCollection<UserSession>("UserSessions");
    }

    private async Task EnsureIndexesAsync(CancellationToken ct)
    {
        if (_indexed) return;
        await _indexLock.WaitAsync(ct);
        try
        {
            if (_indexed) return;
            await _sessions.Indexes.CreateManyAsync(new[]
            {
                new CreateIndexModel<JourneySession>(Builders<JourneySession>.IndexKeys.Ascending(s => s.ExpiresAt),
                    new CreateIndexOptions { Name = "journey_session_retention", ExpireAfter = TimeSpan.Zero }),
                new CreateIndexModel<JourneySession>(Builders<JourneySession>.IndexKeys.Ascending(s => s.OutletId).Descending(s => s.LastSeenAt)),
                new CreateIndexModel<JourneySession>(Builders<JourneySession>.IndexKeys.Ascending(s => s.VisitorId).Descending(s => s.LastSeenAt))
            }, cancellationToken: ct);
            await _events.Indexes.CreateManyAsync(new[]
            {
                new CreateIndexModel<JourneyEvent>(Builders<JourneyEvent>.IndexKeys.Ascending(e => e.ExpiresAt),
                    new CreateIndexOptions { Name = "journey_event_retention", ExpireAfter = TimeSpan.Zero }),
                new CreateIndexModel<JourneyEvent>(Builders<JourneyEvent>.IndexKeys.Ascending(e => e.OutletId).Descending(e => e.OccurredAt)),
                new CreateIndexModel<JourneyEvent>(Builders<JourneyEvent>.IndexKeys.Ascending(e => e.SessionId).Ascending(e => e.OccurredAt))
            }, cancellationToken: ct);
            await _legacyEvents.Indexes.CreateOneAsync(new CreateIndexModel<UserActivityEvent>(
                Builders<UserActivityEvent>.IndexKeys.Ascending(e => e.ExpiresAt),
                new CreateIndexOptions { Name = "consented_activity_retention", ExpireAfter = TimeSpan.Zero }), cancellationToken: ct);
            await _legacySessions.Indexes.CreateOneAsync(new CreateIndexModel<UserSession>(
                Builders<UserSession>.IndexKeys.Ascending(s => s.ExpiresAt),
                new CreateIndexOptions { Name = "consented_session_retention", ExpireAfter = TimeSpan.Zero }), cancellationToken: ct);
            _indexed = true;
        }
        finally { _indexLock.Release(); }
    }

    public async Task<JourneySessionResponse> CreateSessionAsync(
        CreateJourneySessionRequest request, ClaimsPrincipal? principal, CancellationToken ct)
    {
        if (!request.AnalyticsConsent || request.ConsentVersion != "journey-v1")
            throw new ArgumentException("Explicit analytics consent for journey-v1 is required.");
        if (!Guid.TryParseExact(request.VisitorId, "D", out _) || !ObjectId.TryParse(request.OutletId, out _) ||
            request.Device is not ("desktop" or "tablet" or "mobile"))
            throw new ArgumentException("Invalid visitor, outlet, or device.");
        var now = DateTime.UtcNow;
        if (request.ConsentExpiresAt.UtcDateTime <= now || request.ConsentExpiresAt.UtcDateTime > now.AddDays(30).AddMinutes(2))
            throw new ArgumentException("Consent must expire within 30 days.");
        await EnsureIndexesAsync(ct);
        if (!await _outlets.Find(o => o.Id == request.OutletId && !o.IsDeleted).AnyAsync(ct))
            throw new ArgumentException("Select an available outlet before enabling analytics.");
        var userId = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        // Linked accounts use a separate namespace, never the anonymous browser identifier.
        var visitorId = request.LinkAccount && userId != null
            ? Hash($"journey-v1:account:{userId}").ToLowerInvariant() : request.VisitorId;
        var previous = await _sessions.Find(s => s.VisitorId == visitorId && !s.Revoked && s.ExpiresAt > now)
            .SortByDescending(s => s.LastSeenAt).FirstOrDefaultAsync(ct);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var session = new JourneySession
        {
            Id = Guid.NewGuid().ToString("D"), TokenHash = Hash(token), VisitorId = visitorId,
            OutletId = request.OutletId, Device = request.Device, IsLoggedIn = userId != null,
            IsStaff = RoleHelper.GetRoles(principal).Any(r => r != "user"),
            UserRole = principal?.FindFirst(ClaimTypes.Role)?.Value ?? "anonymous",
            AccountId = request.LinkAccount ? userId : null,
            AccountLabel = request.LinkAccount && userId != null ? principal?.Identity?.Name : null,
            IsReturning = previous != null && (previous.IsReturning || previous.LastSeenAt < now.AddMinutes(-30)),
            StartedAt = now, LastSeenAt = now,
            ExpiresAt = request.ConsentExpiresAt.UtcDateTime < now.AddDays(30) ? request.ConsentExpiresAt.UtcDateTime : now.AddDays(30)
        };
        await _sessions.InsertOneAsync(session, cancellationToken: ct);
        return new(new JourneyContext { SessionId = session.Id, Token = token }, session.ExpiresAt);
    }

    public async Task<JourneySession> ValidateAsync(JourneyContext? context, CancellationToken ct, bool allowInactive = false)
    {
        if (context == null || !Guid.TryParseExact(context.SessionId, "D", out _) ||
            context.Token?.Length != 64 || !context.Token.All(Uri.IsHexDigit))
            throw new UnauthorizedAccessException("A valid consented session is required.");
        var now = DateTime.UtcNow;
        var session = await _sessions.Find(s => s.Id == context.SessionId && !s.Revoked && s.ExpiresAt > now &&
            s.StartedAt > now.AddDays(-30)).FirstOrDefaultAsync(ct);
        if (session == null || (!allowInactive && session.LastSeenAt < now.AddMinutes(-30)) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(session.TokenHash), Encoding.ASCII.GetBytes(Hash(context.Token))))
            throw new UnauthorizedAccessException("The analytics session has expired or was revoked.");
        return session;
    }

    public static void ValidateEvent(JourneyEventRequest e, DateTime now, DateTime sessionStarted)
    {
        if (!Guid.TryParseExact(e.Id, "D", out _) || !BrowserEvents.Contains(e.Type))
            throw new ArgumentException("Invalid event ID or event type; browser conversions are not accepted.");
        if (e.Page == null || !Pages.Contains(e.Page))
            throw new ArgumentException("Only normalized page names are accepted; URLs and query strings are prohibited.");
        if (e.OccurredAt.UtcDateTime < now.AddMinutes(-30) || e.OccurredAt.UtcDateTime < sessionStarted.AddMinutes(-2) ||
            e.OccurredAt.UtcDateTime > now.AddMinutes(1))
            throw new ArgumentException("Event timestamp is outside the active session window.");
        if (e.ActiveSeconds is < 0 or > 30 || (e.Type != "page_engagement" && e.ActiveSeconds != 0) ||
            e.Quantity is < 0 or > 1000 || e.CartCount is < 0 or > 10000 ||
            e.CartValue is < 0 or > 1000000 || e.DeliveryFee is < 0 or > 10000)
            throw new ArgumentException("Event measurements exceed allowed limits.");
        if (e.MenuItemId != null && !ObjectId.TryParse(e.MenuItemId, out _) ||
            e.CategoryId != null && !ObjectId.TryParse(e.CategoryId, out _))
            throw new ArgumentException("Invalid menu item or category ID.");
        if (e.Type is "menu_item_view" or "menu_item_click" or "cart_add" or "cart_remove" && e.MenuItemId == null)
            throw new ArgumentException("This event requires a menu item ID.");
        if (e.Type == "category_view" && e.CategoryId == null)
            throw new ArgumentException("A category ID is required.");
    }

    public async Task TrackAsync(TrackJourneyBatchRequest request, CancellationToken ct)
    {
        var session = await ValidateAsync(request.Context, ct);
        if (request.Events is not { Count: > 0 and <= 50 })
            throw new ArgumentException("Send between 1 and 50 events.");
        var now = DateTime.UtcNow;
        foreach (var e in request.Events)
        {
            if (e == null) throw new ArgumentException("Events cannot contain null entries.");
            ValidateEvent(e, now, session.StartedAt);
        }
        var itemIds = request.Events.Where(e => e.MenuItemId != null).Select(e => e.MenuItemId!).Distinct().ToList();
        var categoryIds = request.Events.Where(e => e.CategoryId != null).Select(e => e.CategoryId!).Distinct().ToList();
        var items = itemIds.Count == 0 ? new List<CafeMenuItem>() :
            await _menu.Find(m => itemIds.Contains(m.Id) && m.OutletId == session.OutletId).ToListAsync(ct);
        var categories = categoryIds.Count == 0 ? new List<MenuCategory>() :
            await _categories.Find(c => categoryIds.Contains(c.Id) && c.OutletId == session.OutletId).ToListAsync(ct);
        if (items.Count != itemIds.Count || categories.Count != categoryIds.Count)
            throw new ArgumentException("An item or category does not belong to this session's outlet.");
        var operations = request.Events.Select(e =>
        {
            var item = items.FirstOrDefault(m => m.Id == e.MenuItemId);
            var activity = new JourneyEvent
            {
                Id = $"{session.Id}:{e.Id}", SessionId = session.Id, OutletId = session.OutletId,
                Type = e.Type, Page = e.Page, OccurredAt = e.OccurredAt.UtcDateTime > now ? now : e.OccurredAt.UtcDateTime,
                MenuItemId = e.MenuItemId, ItemName = item?.Name,
                Category = item?.Category ?? categories.FirstOrDefault(c => c.Id == e.CategoryId)?.Name,
                Quantity = e.Quantity, CartCount = e.CartCount, CartValue = e.CartValue,
                DeliveryFee = e.DeliveryFee, ActiveSeconds = e.ActiveSeconds, ExpiresAt = session.ExpiresAt
            };
            return InsertOnce(activity);
        }).ToList();
        await _events.BulkWriteAsync(operations, new BulkWriteOptions { IsOrdered = false }, ct);
        var lastEvent = request.Events.Max(e => e.OccurredAt.UtcDateTime);
        await _sessions.UpdateOneAsync(s => s.Id == session.Id && !s.Revoked,
            Builders<JourneySession>.Update.Max(s => s.LastSeenAt, lastEvent > now ? now : lastEvent), cancellationToken: ct);
        await EraseIfRevokedAsync(session.Id, ct);
    }

    private static UpdateOneModel<JourneyEvent> InsertOnce(JourneyEvent e)
    {
        var document = e.ToBsonDocument();
        document.Remove("_id");
        return new(Builders<JourneyEvent>.Filter.Eq(x => x.Id, e.Id),
            new BsonDocument("$setOnInsert", document)) { IsUpsert = true };
    }

    public async Task RevokeAsync(JourneyContext context, CancellationToken ct)
    {
        var session = await ValidateAsync(context, ct, allowInactive: true);
        await _sessions.DeleteOneAsync(s => s.Id == session.Id, ct);
        await _events.DeleteManyAsync(e => e.SessionId == session.Id, ct);
        await EraseLegacyAsync(session.Id, ct);
    }

    public async Task EraseIfRevokedAsync(string sessionId, CancellationToken ct)
    {
        if (!await _sessions.Find(s => s.Id == sessionId && !s.Revoked).AnyAsync(ct))
        {
            await _events.DeleteManyAsync(e => e.SessionId == sessionId, ct);
            await EraseLegacyAsync(sessionId, ct);
        }
    }

    private async Task EraseLegacyAsync(string sessionId, CancellationToken ct)
    {
        await _legacyEvents.DeleteManyAsync(e => e.SessionId == sessionId, ct);
        await _legacySessions.DeleteManyAsync(s => s.SessionId == sessionId, ct);
    }

    public async Task ConfirmOrderAsync(JourneyContext context, Order order, CancellationToken ct)
    {
        var session = await ValidateAsync(context, ct);
        if (order.Id == null || order.OutletId != session.OutletId ||
            (session.AccountId != null && session.AccountId != order.UserId))
            throw new UnauthorizedAccessException("The order does not belong to this analytics context.");
        if (order.Channel != "web" || order.IsStaffOrder) return;
        var occurredAt = IstDateHelper.IstWallTimeToUtc(OrderDateHelper.ToIstCreatedAt(order));
        if (occurredAt < session.StartedAt.AddMinutes(-2) || occurredAt >= session.ExpiresAt)
            throw new ArgumentException("The order is outside this analytics session.");
        var activity = new JourneyEvent
        {
            Id = $"order:{order.Id}", SessionId = session.Id, OutletId = session.OutletId,
            Type = "order_placed", Page = "/checkout", OrderId = order.Id,
            CartCount = order.Items.Sum(i => i.Quantity), CartValue = order.Total, DeliveryFee = order.DeliveryFee,
            Items = order.Items.Select(i => new JourneyOrderedItem(i.MenuItemId, i.Name, i.Quantity, i.Total)).ToList(),
            OccurredAt = occurredAt, ExpiresAt = session.ExpiresAt
        };
        var operation = InsertOnce(activity);
        await _events.UpdateOneAsync(operation.Filter, operation.Update, new UpdateOptions { IsUpsert = true }, ct);
        await _sessions.UpdateOneAsync(s => s.Id == session.Id && !s.Revoked,
            Builders<JourneySession>.Update.Max(s => s.LastSeenAt, occurredAt), cancellationToken: ct);
        await EraseIfRevokedAsync(session.Id, ct);
    }

    private async Task<(List<JourneySession> Sessions, List<JourneyEvent> Events)> LoadAsync(
        string outletId, JourneyReportRange range, CancellationToken ct, string? sessionId = null)
    {
        await EnsureIndexesAsync(ct);
        var now = DateTime.UtcNow;
        var sessions = await _sessions.Find(s => s.OutletId == outletId && !s.Revoked &&
            s.ExpiresAt > now && s.StartedAt > now.AddDays(-30) && s.LastSeenAt >= range.From && s.StartedAt < range.ToExclusive &&
            (sessionId == null || s.Id == sessionId))
            .Limit(MaxReportSessions + 1).ToListAsync(ct);
        if (sessions.Count > MaxReportSessions)
            throw new ArgumentException("This report exceeds 20,000 retained sessions. Select a shorter date range.");
        var events = await _events.Find(e => e.OutletId == outletId && e.ExpiresAt > now &&
            e.OccurredAt >= range.From && e.OccurredAt < range.ToExclusive && e.OccurredAt > now.AddDays(-30) &&
            (sessionId == null || e.SessionId == sessionId))
            .Limit(MaxReportEvents + 1).ToListAsync(ct);
        if (events.Count > MaxReportEvents)
            throw new ArgumentException("This report exceeds 200,000 retained events. Select a shorter date range.");
        var sessionIds = sessions.Select(s => s.Id).ToHashSet();
        events = events.Where(e => sessionIds.Contains(e.SessionId)).ToList();
        foreach (var e in events.Where(e => e.Type == "page_engagement"))
            e.ActiveSeconds = Math.Min(e.ActiveSeconds, Math.Max(0, (int)(e.OccurredAt - range.From).TotalSeconds));
        await RefreshOrdersAsync(events, ct);
        return (sessions, events);
    }

    private async Task RefreshOrdersAsync(List<JourneyEvent> events, CancellationToken ct)
    {
        var ids = events.Where(e => e.OrderId != null).Select(e => e.OrderId!).Distinct().ToList();
        if (ids.Count == 0) return;
        var orders = await _orders.Find(o => ids.Contains(o.Id!)).Project(o => new
        {
            o.Id, o.Status, o.IsDeleted, o.Total, o.Items, o.Channel, o.IsStaffOrder
        }).ToListAsync(ct);
        var byId = orders.ToDictionary(o => o.Id!);
        foreach (var e in events.Where(e => e.OrderId != null))
        {
            if (!byId.TryGetValue(e.OrderId!, out var order))
            {
                e.Cancelled = true;
                e.CartValue = 0;
                e.Items.Clear();
                continue;
            }
            e.Cancelled = order.IsDeleted || order.Status == "cancelled" || order.Channel != "web" || order.IsStaffOrder;
            e.CartValue = order.Total;
            e.Items = order.Items.Select(i => new JourneyOrderedItem(i.MenuItemId, i.Name, i.Quantity, i.Total)).ToList();
        }
    }

    public async Task<JourneyDashboardResponse> GetDashboardAsync(string outletId, JourneyReportRange range, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var data = await LoadAsync(outletId, range, ct);
        JourneyOverview? previous = null;
        var previousRange = new JourneyReportRange(range.From - (range.ToExclusive - range.From), range.From);
        if (previousRange.From >= now.AddDays(-30))
        {
            var prior = await LoadAsync(outletId, previousRange, ct);
            previous = CustomerJourneyAnalytics.BuildDashboard(prior.Sessions, prior.Events, previousRange, now, 0).Overview;
        }
        var registered = (int)await _users.CountDocumentsAsync(u => u.IsActive, cancellationToken: ct);
        return CustomerJourneyAnalytics.BuildDashboard(data.Sessions, data.Events, range, now, registered, previous);
    }

    public async Task<JourneySessionListResponse> GetSessionsAsync(string outletId, JourneyReportRange range,
        string segment, string? search, int page, CancellationToken ct)
    {
        if (page is < 1 or > 20000) throw new ArgumentException("Invalid page.");
        var data = await LoadAsync(outletId, range, ct);
        var rows = CustomerJourneyAnalytics.FilterSessions(
            CustomerJourneyAnalytics.Summarize(data.Sessions, data.Events, DateTime.UtcNow, range.ToExclusive), segment, search);
        return new(rows.Skip((page - 1) * 25).Take(25).ToList(), rows.Count, page, 25);
    }

    public async Task<JourneyTimelineResponse?> GetTimelineAsync(string outletId, JourneyReportRange range,
        string sessionId, int page, CancellationToken ct)
    {
        if (!Guid.TryParseExact(sessionId, "D", out _) || page is < 1 or > 20000)
            throw new ArgumentException("Invalid session or page.");
        var data = await LoadAsync(outletId, range, ct, sessionId);
        var session = CustomerJourneyAnalytics.Summarize(data.Sessions, data.Events, DateTime.UtcNow, range.ToExclusive).FirstOrDefault();
        if (session == null) return null;
        var events = data.Events.Where(e => e.SessionId == sessionId).OrderBy(e => e.OccurredAt).ThenBy(e => e.Id).ToList();
        var pages = events.GroupBy(e => e.Page).Select(group => new JourneyPageEngagement(
            group.Key, group.Count(e => e.Type == "page_view"), CustomerJourneyAnalytics.ActiveSeconds(group)))
            .OrderByDescending(p => p.ActiveSeconds).ToList();
        return new(session, events.Skip((page - 1) * 100).Take(100).Select(CustomerJourneyAnalytics.TimelineEvent).ToList(),
            events.Count, page, 100, pages);
    }

    public async Task<JourneyLiveResponse> GetLiveAsync(string outletId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var data = await LoadAsync(outletId, new(now.AddMinutes(-30), now.AddMinutes(1)), ct);
        var latest = data.Events.GroupBy(e => e.SessionId).ToDictionary(g => g.Key, g => g.MaxBy(e => e.OccurredAt));
        var sessions = CustomerJourneyAnalytics.Summarize(data.Sessions, data.Events, now)
            .Where(s => CustomerJourneyAnalytics.IsLive(s, latest.GetValueOrDefault(s.Id), now)).ToList();
        return new(now, sessions, data.Events.Where(e => e.Type != "heartbeat" && e.Type != "page_engagement")
            .OrderByDescending(e => e.OccurredAt).Take(30).Select(CustomerJourneyAnalytics.TimelineEvent).ToList());
    }

    public async Task<string> ExportAsync(string outletId, JourneyReportRange range, string report,
        string segment, string? search, CancellationToken ct)
    {
        if (report is not ("sessions" or "menu" or "pages" or "daily"))
            throw new ArgumentException("Choose sessions, menu, pages, or daily for export.");
        var data = await LoadAsync(outletId, range, ct);
        if (report == "sessions")
        {
            var rows = CustomerJourneyAnalytics.FilterSessions(
                CustomerJourneyAnalytics.Summarize(data.Sessions, data.Events, DateTime.UtcNow, range.ToExclusive), segment, search);
            return Csv(new[] { "Session", "Visitor", "Permitted account", "Traffic", "Device", "Returning", "Started UTC", "Last seen UTC", "Active seconds", "Cart value", "Delivery fee", "Outcome", "Orders" },
                rows.Select(s => new object?[] { s.Id, s.VisitorLabel, s.AccountLabel, s.IsStaff ? "Staff" : "Customer", s.Device, s.IsReturning,
                    s.StartedAt.ToString("O"), s.LastSeenAt.ToString("O"), s.ActiveSeconds, s.CartValue, s.DeliveryFee, s.Outcome, s.Orders }));
        }
        var snapshot = CustomerJourneyAnalytics.BuildDashboard(data.Sessions, data.Events, range, DateTime.UtcNow, 0);
        return report switch
        {
            "menu" => Csv(new[] { "Item ID", "Item", "Category", "Detail views", "Clicks", "Viewing sessions", "Added quantity", "Cart sessions", "Net ordered quantity", "Net orders", "Item revenue before order adjustments", "Viewer add-to-cart %", "Viewer order %" },
                snapshot.Menu.Select(m => new object?[] { m.ItemId, m.Name, m.Category, m.Views, m.Clicks, m.Viewers, m.CartAdds, m.CartSessions, m.QuantityOrdered, m.Orders, m.Revenue, m.AddToCartRate, m.OrderRate })),
            "pages" => Csv(new[] { "Page", "Sessions", "Page views", "Active seconds", "Orders" },
                snapshot.Pages.Select(p => new object?[] { p.Name, p.Sessions, p.Views, p.ActiveSeconds, p.Orders })),
            _ => Csv(new[] { "Date IST", "Visitors", "Sessions", "Orders" },
                snapshot.Daily.Select(d => new object?[] { d.Date, d.Visitors, d.Sessions, d.Orders }))
        };
    }

    public static string Csv(IEnumerable<string> headers, IEnumerable<IEnumerable<object?>> rows)
    {
        static string Cell(object? value)
        {
            var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            if (text.TrimStart().StartsWith('=') || text.TrimStart().StartsWith('+') ||
                text.TrimStart().StartsWith('-') || text.TrimStart().StartsWith('@') ||
                text.StartsWith('\t') || text.StartsWith('\r'))
                text = "'" + text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
        var result = new StringBuilder();
        result.AppendLine(string.Join(",", headers.Select(Cell)));
        foreach (var row in rows) result.AppendLine(string.Join(",", row.Select(Cell)));
        return result.ToString();
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
