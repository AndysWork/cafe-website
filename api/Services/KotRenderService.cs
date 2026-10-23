using System.Text;
using Cafe.Api.Models;

namespace Cafe.Api.Services;

public class KotRenderService
{
    public string Render(Order order, int width = 48, bool isDuplicate = false)
    {
        var sb = new StringBuilder();
        var line = new string('-', width);

        sb.AppendLine(CenterText("MAA TARA CAFE", width));
        sb.AppendLine(CenterText(isDuplicate ? "DUPLICATE KITCHEN TICKET" : "KITCHEN ORDER TICKET", width));
        sb.AppendLine(line);
        if (!string.IsNullOrWhiteSpace(order.KotNumber))
        {
            sb.AppendLine(CenterText(order.KotNumber, width));
            sb.AppendLine(line);
        }
        sb.AppendLine($"Order #: {order.Id}");
        sb.AppendLine($"Date: {order.CreatedAt:dd/MM/yyyy HH:mm}");
        sb.AppendLine($"Type: {order.OrderType?.ToUpper() ?? "DELIVERY"}");

        if (string.Equals(order.OrderType, "pickup", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine(line);
            sb.AppendLine(CenterText("*** TAKEAWAY - PACK AS PARCEL ***", width));
            if (!string.IsNullOrWhiteSpace(order.KotNumber))
                sb.AppendLine(CenterText($"TOKEN: {order.KotNumber}", width));
            if (string.Equals(order.TakeawaySource, "waiter", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(order.WaiterName))
                sb.AppendLine(CenterText($"Hand over to: {order.WaiterName}", width));
            sb.AppendLine(line);
        }

        if (!string.IsNullOrWhiteSpace(order.TableNumber))
        {
            var roundText = order.RoundNumber > 0 ? $" (ROUND #{order.RoundNumber})" : "";
            sb.AppendLine($"Table: {order.TableNumber}{roundText}");
            if (order.PaymentMethod == "dine_in_tab")
            {
                sb.AppendLine("Billing: DINE-IN RUNNING TAB");
            }
        }

        AppendPeople(sb, order);

        sb.AppendLine(line);
        sb.AppendLine(FormatKotRow("ITEM", "QTY", width));
        sb.AppendLine(line);

        foreach (var item in order.Items)
        {
            sb.AppendLine(FormatKotRow(item.Name, item.Quantity.ToString(), width));
            if (!string.IsNullOrWhiteSpace(item.SelectedVariantName))
                sb.AppendLine($"  Variant: {item.SelectedVariantName}");
            if (item.SelectedAddOns?.Count > 0)
                sb.AppendLine($"  Add-ons: {string.Join(", ", item.SelectedAddOns.Select(addOn => addOn.Name))}");
        }

        sb.AppendLine(line);

        var specialInstructions = string.IsNullOrWhiteSpace(order.PreparationNotes)
            ? order.Notes
            : order.PreparationNotes;

        if (!string.IsNullOrWhiteSpace(specialInstructions))
        {
            sb.AppendLine("SPECIAL INSTRUCTIONS:");
            sb.AppendLine(specialInstructions);
            sb.AppendLine(line);
        }

        if (order.IsScheduled && order.ScheduledFor.HasValue)
        {
            sb.AppendLine($"*** SCHEDULED: {order.ScheduledFor.Value:dd/MM HH:mm} ***");
            sb.AppendLine(line);
        }

        sb.AppendLine(CenterText($"Total Items: {order.Items.Sum(i => i.Quantity)}", width));
        sb.AppendLine("");

        return sb.ToString();
    }

    public string RenderCancellation(Order order, string reason, int width = 48)
    {
        var sb = new StringBuilder();
        var line = new string('-', width);
        sb.AppendLine(CenterText("MAA TARA CAFE", width));
        sb.AppendLine(CenterText("KITCHEN TICKET CANCELLED", width));
        sb.AppendLine(line);
        if (!string.IsNullOrWhiteSpace(order.KotNumber)) sb.AppendLine($"KOT #: {order.KotNumber}");
        if (!string.IsNullOrWhiteSpace(order.TableNumber))
            sb.AppendLine($"Table: {order.TableNumber} (ROUND #{order.RoundNumber})");
        sb.AppendLine($"Order #: {order.Id}");
        AppendPeople(sb, order);
        sb.AppendLine(line);
        foreach (var item in order.Items)
            sb.AppendLine(FormatKotRow(item.Name, item.Quantity.ToString(), width));
        sb.AppendLine(line);
        sb.AppendLine("REASON:");
        sb.AppendLine(reason);
        sb.AppendLine(line);
        sb.AppendLine($"Cancelled: {order.KotCancelledAt:dd/MM/yyyy HH:mm}");
        return sb.ToString();
    }

    private static void AppendPeople(StringBuilder sb, Order order)
    {
        var customerName = !string.IsNullOrWhiteSpace(order.CustomerName)
            ? order.CustomerName
            : string.Equals(order.DineInOrderSource, "waiter", StringComparison.OrdinalIgnoreCase)
                ? "Walk-in customer"
                : order.Username;
        if (!string.IsNullOrWhiteSpace(customerName))
            sb.AppendLine($"Customer: {customerName}");
        var waiterName = order.WaiterName
            ?? (string.Equals(order.DineInOrderSource, "waiter", StringComparison.OrdinalIgnoreCase)
                && order.WaiterUserId == null ? order.Username : null);
        if (!string.IsNullOrWhiteSpace(waiterName))
            sb.AppendLine($"Waiter: {waiterName}");
    }

    private static string CenterText(string text, int width)
    {
        if (text.Length >= width)
        {
            return text;
        }

        var padding = (width - text.Length) / 2;
        return text.PadLeft(padding + text.Length).PadRight(width);
    }

    private static string FormatKotRow(string left, string right, int width)
    {
        var available = width - right.Length - 2;
        if (left.Length > available)
        {
            left = left[..available];
        }

        return left.PadRight(available) + "  " + right;
    }
}