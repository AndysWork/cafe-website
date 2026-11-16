using System.Globalization;

namespace Cafe.Api.Services;

public partial class ZomatoSalesPayoutInsightService
{
    private static (decimal? Minutes, string Status) ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (null, "missing");
        return decimal.TryParse(value.Trim(), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var minutes) && minutes >= 0
            ? (minutes, "valid")
            : (null, "invalid");
    }

    private static List<KitchenOrderTiming> BuildKitchenOrders(List<OrderHistoryRow> orders) =>
        orders.Select(order =>
        {
            var items = ParseItems(order.ItemsInOrder)
                .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => new KitchenOrderItem { Name = group.First().Name, Quantity = group.Sum(item => item.Quantity) })
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new KitchenOrderTiming
            {
                OrderId = order.OrderId,
                PlacedAt = order.PlacedAt?.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                KptMinutes = order.KptMinutes >= 0 ? order.KptMinutes : null,
                RiderWaitMinutes = order.RiderWaitMinutes >= 0 ? order.RiderWaitMinutes : null,
                KptStatus = order.KptDataStatus ?? DurationStatus(order.KptMinutes),
                RiderWaitStatus = order.RiderWaitDataStatus ?? DurationStatus(order.RiderWaitMinutes),
                TimingsValidated = order.KptDataStatus != null && order.RiderWaitDataStatus != null,
                Items = items,
                ItemsText = order.ItemsInOrder,
                Quantity = items.Sum(item => item.Quantity),
                DistinctItems = items.Count,
                DeliveryType = order.DeliveryType,
                Rating = order.Rating is >= 1 and <= 5 ? order.Rating : null,
                Review = order.Review,
                Complaint = order.CustomerComplaintTag,
                ReadyMarked = order.OrderReadyMarked,
                OrderValue = order.Total
            };
        }).ToList();

    private static string DurationStatus(decimal? value) =>
        value == null ? "missing" : value < 0 ? "invalid" : "valid";
}

public class KitchenOrderTiming
{
    public string OrderId { get; set; } = string.Empty;
    public string? PlacedAt { get; set; }
    public decimal? KptMinutes { get; set; }
    public decimal? RiderWaitMinutes { get; set; }
    public string KptStatus { get; set; } = "missing";
    public string RiderWaitStatus { get; set; } = "missing";
    public bool TimingsValidated { get; set; }
    public List<KitchenOrderItem> Items { get; set; } = new();
    public string ItemsText { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int DistinctItems { get; set; }
    public string DeliveryType { get; set; } = string.Empty;
    public decimal? Rating { get; set; }
    public string Review { get; set; } = string.Empty;
    public string Complaint { get; set; } = string.Empty;
    public string ReadyMarked { get; set; } = string.Empty;
    public decimal OrderValue { get; set; }
}

public class KitchenOrderItem
{
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
}
