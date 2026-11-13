using Cafe.Api.Models;

namespace Cafe.Api.Helpers;

public sealed record OrderPeriodCollectionResult(
    decimal Upi, decimal Cash, decimal Other, decimal Unallocated,
    decimal Total, int UnknownAmountOrders, Dictionary<string, string[]> SessionMethods,
    Dictionary<string, SessionCollection> SessionPayments);

public sealed record SessionCollection(
    string SessionId, string TableNumber, decimal GrandTotal, string PaymentStatus,
    decimal Cash, decimal Upi, decimal Other, decimal Unallocated, decimal Total);

public static class OrderPeriodCollection
{
    public static OrderPeriodCollectionResult Calculate(
        IEnumerable<Order> orders, IReadOnlyDictionary<string, DineInSession> sessions)
    {
        decimal upi = 0, cash = 0, other = 0, unallocated = 0;
        var unknownAmountOrders = 0;
        var selectedSessions = new Dictionary<string, DineInSession>();
        var sessionMethods = new Dictionary<string, string[]>();
        var resolvedSessions = new Dictionary<string, string>();
        var sessionPayments = new Dictionary<string, SessionCollection>();

        void AddPayment(string? method, decimal amount)
        {
            switch (method?.Trim().ToLowerInvariant())
            {
                case "upi":
                case "upi-qr": upi += amount; break;
                case "cash":
                case "cash_at_counter":
                case "cod": cash += amount; break;
                default: other += amount; break;
            }
        }

        foreach (var order in orders)
        {
            if (!string.IsNullOrWhiteSpace(order.DineInSessionId))
            {
                var sessionId = order.DineInSessionId;
                var visited = new HashSet<string>();
                while (true)
                {
                    if (!visited.Add(sessionId) || !sessions.TryGetValue(sessionId, out var session)
                        || !string.Equals(session.OutletId, order.OutletId, StringComparison.Ordinal))
                        throw new InvalidOperationException("Unable to resolve an order's dine-in session.");
                    if (string.IsNullOrWhiteSpace(session.MergedIntoSessionId))
                    {
                        selectedSessions[sessionId] = session;
                        var methods = (session.PaymentEntries ?? new List<DineInPaymentEntry>())
                            .Where(entry => string.Equals(entry.Status, "confirmed", StringComparison.OrdinalIgnoreCase))
                            .Select(entry => entry.PaymentMethod).Where(method => !string.IsNullOrWhiteSpace(method))
                            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                        sessionMethods[order.DineInSessionId] = methods;
                        resolvedSessions[order.DineInSessionId] = sessionId;
                        break;
                    }
                    sessionId = session.MergedIntoSessionId;
                }
            }
            else if (string.Equals(order.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(order.PaymentMethod, "dine_in_tab", StringComparison.OrdinalIgnoreCase))
            {
                AddPayment(order.PaymentMethod, order.Total);
            }
            else if (string.Equals(order.PaymentStatus, "partially_paid", StringComparison.OrdinalIgnoreCase)
                || string.Equals(order.PaymentStatus, "refunded", StringComparison.OrdinalIgnoreCase)
                || (string.Equals(order.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(order.PaymentMethod, "dine_in_tab", StringComparison.OrdinalIgnoreCase)))
            {
                unknownAmountOrders++;
            }
        }

        var seenEntries = new HashSet<string>();
        foreach (var session in selectedSessions.Values)
        {
            var previousCash = cash;
            var previousUpi = upi;
            var previousOther = other;
            decimal confirmedTotal = 0;
            foreach (var entry in session.PaymentEntries ?? new List<DineInPaymentEntry>())
            {
                if (!string.Equals(entry.Status, "confirmed", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrWhiteSpace(entry.Id) || !seenEntries.Add(entry.Id))
                    throw new InvalidOperationException("Duplicate or missing dine-in payment entry ID.");
                confirmedTotal += entry.Amount;
                AddPayment(entry.PaymentMethod, entry.Amount);
            }
            var sessionUnallocated = Math.Max(0, session.AmountPaid - confirmedTotal);
            unallocated += sessionUnallocated;
            sessionPayments[session.Id!] = new SessionCollection(
                session.Id!, session.TableNumber, session.GrandTotal, session.PaymentStatus,
                cash - previousCash, upi - previousUpi, other - previousOther, sessionUnallocated,
                confirmedTotal + sessionUnallocated);
        }
        return new OrderPeriodCollectionResult(upi, cash, other, unallocated,
            upi + cash + other + unallocated, unknownAmountOrders, sessionMethods,
            resolvedSessions.ToDictionary(pair => pair.Key, pair => sessionPayments[pair.Value]));
    }
}
