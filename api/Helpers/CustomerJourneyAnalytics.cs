using Cafe.Api.Models;

namespace Cafe.Api.Helpers;

public static class CustomerJourneyAnalytics
{
    public static double Rate(int numerator, int denominator) =>
        denominator == 0 ? 0 : Math.Round(100d * numerator / denominator, 2);

    public static IReadOnlyList<JourneySessionSummary> Summarize(
        IReadOnlyList<JourneySession> sessions, IReadOnlyList<JourneyEvent> events, DateTime now, DateTime? rangeEnd = null)
    {
        var bySession = events.GroupBy(e => e.SessionId).ToDictionary(g => g.Key, g => g.OrderBy(e => e.OccurredAt).ToList());
        return sessions.Where(s => bySession.ContainsKey(s.Id)).Select(s =>
        {
            var activity = bySession[s.Id];
            var cart = activity.LastOrDefault(IsCartSnapshot);
            var lastOrder = activity.LastOrDefault(e => e.Type == "order_placed");
            var valueSnapshot = cart?.CartCount > 0 ? cart : lastOrder ?? cart;
            var orders = activity.Count(e => e.Type == "order_placed");
            var continued = activity.Any(e => e.Type == "session_continued") ||
                rangeEnd.HasValue && s.LastSeenAt >= rangeEnd.Value;
            var mature = !continued && s.LastSeenAt <= now.AddMinutes(-30);
            var hasCart = cart?.CartCount > 0 && cart.Type != "order_placed" &&
                (lastOrder == null || cart.OccurredAt > lastOrder.OccurredAt);
            var checkout = activity.Any(e => e.Type == "checkout_started" &&
                (lastOrder == null || e.OccurredAt > lastOrder.OccurredAt));
            var outcome = hasCart && mature && checkout ? "Checkout abandoned" :
                hasCart && mature ? "Cart abandoned" :
                orders > 0 ? "Converted" :
                continued ? "Continued in another session / range" :
                hasCart && checkout ? "Checkout in progress" :
                hasCart ? "Active cart" : "Browsing";
            return new JourneySessionSummary(s.Id, $"Visitor {s.VisitorId[..8]}", s.AccountId, s.AccountLabel,
                s.IsLoggedIn, s.IsStaff, s.IsReturning, s.Device, s.StartedAt, s.LastSeenAt,
                activity.Last().Page, ActiveSeconds(activity), valueSnapshot?.CartValue ?? 0, valueSnapshot?.DeliveryFee,
                outcome, orders, activity.Count(e => e.Type == "page_view"));
        }).OrderByDescending(s => s.LastSeenAt).ToList();
    }

    public static bool IsCartSnapshot(JourneyEvent e) =>
        e.Type is "cart_add" or "cart_remove" or "cart_update" or "cart_view" or
            "checkout_started" or "checkout_updated" or "order_placed";

    public static bool IsLive(JourneySessionSummary s, IReadOnlyList<JourneyEvent> events, DateTime now) =>
        IsLive(s, events.Where(e => e.SessionId == s.Id).MaxBy(e => e.OccurredAt), now);

    public static bool IsLive(JourneySessionSummary s, JourneyEvent? lastEvent, DateTime now) =>
        s.LastSeenAt.UtcDateTime >= now.AddSeconds(-90) &&
        lastEvent?.Type is not ("session_end" or "session_continued");

    // Merge overlapping intervals so retries or multiple engagement events cannot inflate measured time.
    public static int ActiveSeconds(IEnumerable<JourneyEvent> events)
    {
        var seconds = 0d;
        foreach (var session in events.Where(e => e.Type == "page_engagement" && e.ActiveSeconds > 0).GroupBy(e => e.SessionId))
        {
            var intervals = session.Select(e => (Start: e.OccurredAt.AddSeconds(-e.ActiveSeconds), End: e.OccurredAt))
                .OrderBy(i => i.Start);
            var end = DateTime.MinValue;
            foreach (var interval in intervals)
            {
                var start = interval.Start > end ? interval.Start : end;
                if (interval.End > start) seconds += (interval.End - start).TotalSeconds;
                if (interval.End > end) end = interval.End;
            }
        }
        return (int)Math.Floor(seconds);
    }

    public static IReadOnlyList<JourneySessionSummary> FilterSessions(
        IReadOnlyList<JourneySessionSummary> rows, string segment, string? search)
    {
        IEnumerable<JourneySessionSummary> selected = segment switch
        {
            "all" => rows,
            "customers" => rows.Where(s => !s.IsStaff),
            "staff" => rows.Where(s => s.IsStaff),
            "anonymous" => rows.Where(s => !s.IsStaff && !s.IsLoggedIn),
            "logged-in" => rows.Where(s => !s.IsStaff && s.IsLoggedIn),
            "returning" => rows.Where(s => !s.IsStaff && s.IsReturning),
            "first-time" => rows.Where(s => !s.IsStaff && !s.IsReturning),
            "no-order" => rows.Where(s => !s.IsStaff && s.Orders == 0),
            "cart-abandoned" => rows.Where(s => !s.IsStaff && s.Outcome == "Cart abandoned"),
            "checkout-abandoned" => rows.Where(s => !s.IsStaff && s.Outcome == "Checkout abandoned"),
            _ => throw new ArgumentException("Unknown visitor segment.")
        };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var query = search.Trim();
            if (query.Length > 120) throw new ArgumentException("Search must be 120 characters or fewer.");
            selected = selected.Where(s => s.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.VisitorLabel.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (s.AccountLabel?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (s.AccountId?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        return selected.ToList();
    }

    public static JourneyDashboardResponse BuildDashboard(
        IReadOnlyList<JourneySession> sessions, IReadOnlyList<JourneyEvent> events,
        JourneyReportRange range, DateTime now, int registeredAccounts, JourneyOverview? previous = null)
    {
        var rows = Summarize(sessions, events, now, range.ToExclusive);
        var customers = rows.Where(s => !s.IsStaff).ToList();
        var customerById = customers.ToDictionary(s => s.Id);
        var customerIds = customers.Select(s => s.Id).ToHashSet();
        var activity = events.Where(e => customerIds.Contains(e.SessionId)).ToList();
        var visitorBySession = sessions.ToDictionary(s => s.Id, s => s.VisitorId);
        var placed = activity.Where(e => e.Type == "order_placed").ToList();
        var netOrders = placed.Where(e => !e.Cancelled).ToList();
        var cartSessions = activity.Where(e => IsCartSnapshot(e) && e.CartCount > 0).Select(e => e.SessionId).ToHashSet();
        var viewingSessions = activity.Where(e => e.Type == "menu_item_view").Select(e => e.SessionId).ToHashSet();
        var checkoutSessions = activity.Where(e => e.Type == "checkout_started").Select(e => e.SessionId).ToHashSet();
        var continuedSessions = activity.Where(e => e.Type == "session_continued").Select(e => e.SessionId).ToHashSet();
        var latestEvents = activity.GroupBy(e => e.SessionId).ToDictionary(g => g.Key, g => g.MaxBy(e => e.OccurredAt));
        var matureCarts = customers.Where(s => s.LastSeenAt.UtcDateTime <= now.AddMinutes(-30) &&
            s.LastSeenAt.UtcDateTime < range.ToExclusive && !continuedSessions.Contains(s.Id) && cartSessions.Contains(s.Id)).ToList();
        var overview = new JourneyOverview(
            customers.Select(s => visitorBySession[s.Id]).Distinct().Count(), customers.Count,
            customers.Where(s => s.IsLoggedIn).Select(s => visitorBySession[s.Id]).Distinct().Count(),
            customers.Where(s => s.IsReturning).Select(s => visitorBySession[s.Id]).Distinct().Count(),
            activity.Count(e => e.Type == "page_view"), customers.Sum(s => s.ActiveSeconds),
            customers.Count == 0 ? 0 : Math.Round(customers.Average(s => s.ActiveSeconds), 1),
            placed.Count, placed.Count(e => e.Cancelled), netOrders.Sum(e => e.CartValue),
            Rate(customers.Count(s => s.Orders > 0), customers.Count),
            Rate(matureCarts.Count(IsAbandoned), matureCarts.Count),
            customers.Count(s => IsLive(s, latestEvents.GetValueOrDefault(s.Id), now)), rows.Count(s => s.IsStaff));

        JourneyBreakdown Breakdown(string name, IEnumerable<JourneySessionSummary> group,
            IEnumerable<JourneyEvent>? selectedActivity = null)
        {
            var list = group.ToList();
            var ids = list.Select(s => s.Id).ToHashSet();
            var selected = (selectedActivity ?? activity.Where(e => ids.Contains(e.SessionId))).ToList();
            return new(name, list.Count, selected.Count(e => e.Type == "page_view"),
                selected.Count(e => e.Type == "order_placed"), list.Count(IsAbandoned),
                ActiveSeconds(selected), selected.Where(e => e.Type == "order_placed" && !e.Cancelled).Sum(e => e.CartValue),
                Rate(list.Count(s => s.Orders > 0), list.Count));
        }

        var pageStats = activity.GroupBy(e => e.Page).Select(g =>
            Breakdown(g.Key, g.Select(e => e.SessionId).Distinct().Select(id => customerById[id]), g))
            .OrderByDescending(p => p.Views).ToList();
        var categoryStats = activity.Where(e => e.Category != null).GroupBy(e => e.Category!)
            .Select(g => Breakdown(g.Key, g.Select(e => e.SessionId).Distinct().Select(id => customerById[id]), g) with { Views = g.Count() })
            .OrderByDescending(c => c.Sessions).ToList();
        var pathStats = activity.Where(e => e.Type == "page_view").GroupBy(e => e.SessionId)
            .Select(g => (Session: g.Key, Path: string.Join(" > ", g.OrderBy(e => e.OccurredAt).Select(e => e.Page).Take(6))))
            .GroupBy(p => p.Path).Select(g => Breakdown(g.Key, g.Select(p => customerById[p.Session])))
            .OrderByDescending(p => p.Sessions).Take(20).ToList();

        var daily = new List<JourneyDailyStat>();
        for (var date = range.From.AddHours(5.5).Date;
             date < range.ToExclusive.AddHours(5.5); date = date.AddDays(1))
        {
            var ids = activity.Where(e => e.OccurredAt.AddHours(5.5).Date == date)
                .Select(e => e.SessionId).Distinct().ToList();
            daily.Add(new(date.ToString("yyyy-MM-dd"), ids.Select(id => visitorBySession[id]).Distinct().Count(),
                ids.Count, placed.Count(e => e.OccurredAt.AddHours(5.5).Date == date)));
        }

        var attentionByItem = activity.Where(e => e.MenuItemId != null).ToLookup(e => e.MenuItemId!);
        var ordersByItem = netOrders.SelectMany(o => o.Items.Select(i => (Item: i, Order: o))).ToLookup(p => p.Item.MenuItemId);
        var menuIds = activity.Where(e => e.MenuItemId != null).Select(e => e.MenuItemId!)
            .Concat(netOrders.SelectMany(o => o.Items.Select(i => i.MenuItemId))).Distinct();
        var menu = menuIds.Select(id =>
        {
            var attention = attentionByItem[id].ToList();
            var firstViews = attention.Where(e => e.Type == "menu_item_view").GroupBy(e => e.SessionId)
                .ToDictionary(g => g.Key, g => g.Min(e => e.OccurredAt));
            var viewers = firstViews.Keys.ToHashSet();
            var added = attention.Where(e => e.Type == "cart_add").ToList();
            var itemOrders = ordersByItem[id].ToList();
            var ordered = itemOrders.Select(p => p.Order).DistinctBy(o => o.Id).ToList();
            var items = itemOrders.Select(p => p.Item).ToList();
            var convertingViewers = ordered.Where(o => firstViews.TryGetValue(o.SessionId, out var firstView) && o.OccurredAt >= firstView)
                .Select(o => o.SessionId).Distinct().Count();
            var name = attention.FirstOrDefault(e => e.ItemName != null)?.ItemName ?? items.FirstOrDefault()?.Name ?? id;
            var category = attention.FirstOrDefault(e => e.Category != null)?.Category ?? "Uncategorized";
            var itemCartSessions = added.Select(e => e.SessionId).Distinct().Count();
            return new JourneyMenuStat(id, name, category,
                attention.Count(e => e.Type == "menu_item_view"), attention.Count(e => e.Type == "menu_item_click"),
                viewers.Count, added.Sum(e => e.Quantity), itemCartSessions, items.Sum(i => i.Quantity),
                ordered.Count, items.Sum(i => i.Revenue),
                Rate(added.Where(e => firstViews.TryGetValue(e.SessionId, out var firstView) && e.OccurredAt >= firstView)
                    .Select(e => e.SessionId).Distinct().Count(), viewers.Count),
                Rate(convertingViewers, viewers.Count), viewers.Count >= 5 && Rate(convertingViewers, viewers.Count) < 10);
        }).OrderByDescending(m => m.Clicks).ThenByDescending(m => m.QuantityOrdered).ToList();

        var funnelCounts = new int[5];
        foreach (var sessionEvents in activity.GroupBy(e => e.SessionId))
        {
            funnelCounts[0]++;
            var stage = 1;
            foreach (var e in sessionEvents.OrderBy(e => e.OccurredAt))
            {
                if (stage == 1 && (e.Page is "/menu" or "/dine-in" || e.Type is "menu_item_view" or "category_view")) stage++;
                else if (stage == 2 && IsCartSnapshot(e) && e.CartCount > 0 && e.Type != "order_placed") stage++;
                else if (stage == 3 && e.Type == "checkout_started") stage++;
                else if (stage == 4 && e.Type == "order_placed") stage++;
                else continue;
                funnelCounts[stage - 1]++;
            }
        }
        var stageNames = new[] { "Website visit", "Browse menu", "Cart with items", "Checkout started", "Order placed" };
        var funnel = stageNames.Select((name, i) => new JourneyFunnelStep(name, funnelCounts[i],
            i == 0 ? 0 : Rate(funnelCounts[i - 1] - funnelCounts[i], funnelCounts[i - 1]))).ToList();

        return new(range.From, range.ToExclusive, now.AddDays(-30), now, overview, previous, daily, funnel,
            pageStats, categoryStats,
            new[]
            {
                Breakdown("Anonymous", customers.Where(s => !s.IsLoggedIn)),
                Breakdown("Signed in, unlinked", customers.Where(s => s.IsLoggedIn && s.AccountId == null)),
                Breakdown("Account linked", customers.Where(s => s.AccountId != null)),
                Breakdown("Staff / admin", rows.Where(s => s.IsStaff), events.Where(e => rows.Any(s => s.IsStaff && s.Id == e.SessionId)))
            },
            customers.GroupBy(s => s.Device).Select(g => Breakdown(g.Key, g)).ToList(),
            customers.Where(s => s.CartValue > 0).GroupBy(s =>
                s.CartValue < 200 ? "Under INR 200" : s.CartValue < 500 ? "INR 200-499" : "INR 500+")
                .Select(g => Breakdown(g.Key, g)).ToList(),
            customers.Where(s => s.CartValue > 0).GroupBy(s =>
                !s.DeliveryFee.HasValue ? "Not quoted" : s.DeliveryFee == 0 ? "Free / pickup" :
                s.DeliveryFee <= 40 ? "INR 1-40" : "Above INR 40").Select(g => Breakdown(g.Key, g)).ToList(),
            pathStats,
            new[]
            {
                Breakdown("First-time visitor", customers.Where(s => !s.IsReturning)),
                Breakdown("Returning visitor", customers.Where(s => s.IsReturning)),
                Breakdown("Anonymous, no order", customers.Where(s => !s.IsLoggedIn && s.Orders == 0)),
                Breakdown("Signed in, no order", customers.Where(s => s.IsLoggedIn && s.Orders == 0)),
                Breakdown("Viewed items, no order", customers.Where(s => s.Orders == 0 && viewingSessions.Contains(s.Id))),
                Breakdown("Cart, no order", customers.Where(s => s.Orders == 0 && s.CartValue > 0)),
                Breakdown("Checkout, no order", customers.Where(s => s.Orders == 0 && checkoutSessions.Contains(s.Id)))
            },
            menu, rows.Take(12).ToList(), registeredAccounts);
    }

    private static bool IsAbandoned(JourneySessionSummary s) =>
        s.Outcome is "Cart abandoned" or "Checkout abandoned";

    public static JourneyTimelineEvent TimelineEvent(JourneyEvent e) => new(
        e.Id, e.Type, e.OccurredAt, e.Page, e.ItemName, e.Category, e.Quantity, e.CartCount,
        e.CartValue, e.DeliveryFee, e.ActiveSeconds, e.OrderId, e.Cancelled);
}
