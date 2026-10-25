using System.Text;
using Cafe.Api.Models;

namespace Cafe.Api.Services;

public class InvoiceRenderService
{
    public string Render(Order order, int width = 48)
    {
        var sb = new StringBuilder();
        var invoiceNumber = BuildInvoiceNumber(order);
        var printedAt = MongoService.GetIstNow().ToString("dd MMM yyyy hh:mm tt");

        sb.AppendLine(Center("MAA TARA CAFE", width));
        sb.AppendLine(Center("COUNTER SALE INVOICE", width));
        sb.AppendLine(new string('-', width));
        sb.AppendLine($"Invoice : {invoiceNumber}");
        sb.AppendLine($"Order   : {(order.Id?.Length >= 6 ? order.Id[^6..].ToUpperInvariant() : order.Id)}");
        sb.AppendLine($"Date    : {printedAt}");
        sb.AppendLine($"Customer: {Safe(order.Username, "Walk-in Customer")}");
        if (!string.IsNullOrWhiteSpace(order.PhoneNumber))
        {
            sb.AppendLine($"Phone   : {order.PhoneNumber}");
        }
        if (Cafe.Api.Helpers.PhoneDeliveryOrderRules.IsPhoneOrder(order) && !string.IsNullOrWhiteSpace(order.DeliveryAddress))
            sb.AppendLine($"Address : {order.DeliveryAddress}");

        sb.AppendLine(new string('-', width));
        sb.AppendLine($"{"Item",-26}{"Qty",4}{"Rate",8}{"Total",10}");
        sb.AppendLine(new string('-', width));

        foreach (var item in order.Items)
        {
            var name = Truncate(item.Name, 26);
            sb.AppendLine($"{name,-26}{item.Quantity,4}{item.Price,8:0.##}{item.Total,10:0.##}");
        }

        sb.AppendLine(new string('-', width));
        sb.AppendLine($"{"Subtotal",-36}{order.Subtotal,12:0.##}");
        sb.AppendLine($"{"Tax",-36}{order.Tax,12:0.##}");
        sb.AppendLine($"{"Platform",-36}{order.PlatformCharge,12:0.##}");
        if (order.DeliveryFee > 0)
            sb.AppendLine($"{"Delivery Fee",-36}{order.DeliveryFee,12:0.##}");
        if (order.PackagingCharge > 0)
        {
            sb.AppendLine($"{"Packaging",-36}{order.PackagingCharge,12:0.##}");
        }
        if (order.DiscountAmount > 0)
        {
            sb.AppendLine($"{"Discount",-36}{-order.DiscountAmount,12:0.##}");
        }

        sb.AppendLine(new string('-', width));
        sb.AppendLine($"{"TOTAL",-36}{order.Total,12:0.##}");
        sb.AppendLine($"Payment: {MapPaymentMethod(order.PaymentMethod)} ({order.PaymentStatus})");
        if (!string.IsNullOrWhiteSpace(order.UpiReference))
        {
            sb.AppendLine($"UPI Ref: {order.UpiReference}");
        }

        sb.AppendLine(new string('-', width));
        sb.AppendLine(Center("Thank you. Visit again!", width));
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine();

        return sb.ToString();
    }

    private static string Center(string text, int width)
    {
        if (string.IsNullOrEmpty(text) || text.Length >= width) return text;
        var pad = (width - text.Length) / 2;
        return new string(' ', Math.Max(0, pad)) + text;
    }

    private static string Truncate(string text, int max)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= max ? text : text[..(max - 1)] + "~";
    }

    private static string Safe(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string MapPaymentMethod(string? method)
    {
        return (method ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "cash_at_counter" => "Cash",
            "upi-qr" => "UPI QR",
            "cod" => "Cash on Delivery",
            _ => "Cash"
        };
    }

    private static string BuildInvoiceNumber(Order order)
    {
        var suffix = order.Id?.Length >= 6 ? order.Id[^6..].ToUpperInvariant() : (order.Id ?? "NA").ToUpperInvariant();
        return $"CS-{order.CreatedAt:yyyyMMdd}-{suffix}";
    }
}
