using System.Text.RegularExpressions;
using Cafe.Api.Models;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Helpers;

/// <summary>
/// Parses text extracted from Zomato/Swiggy invoice screenshots.
/// In production, integrate Azure AI Vision OCR to extract text from images first.
/// For now, this provides a manual-entry fallback where the customer enters the total
/// and the system calculates points, with admin verification from the screenshot.
/// </summary>
public static class InvoiceParser
{
    /// <summary>
    /// Attempts to extract items and total from OCR text output.
    /// Handles common Zomato/Swiggy invoice formats.
    /// </summary>
    public static (List<ExtractedInvoiceItem> Items, decimal Total) ParseInvoiceText(string ocrText)
    {
        var items = new List<ExtractedInvoiceItem>();
        decimal total = 0;

        if (string.IsNullOrWhiteSpace(ocrText))
            return (items, total);

        var lines = ocrText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // Try to extract grand total / bill total patterns
            var totalMatch = Regex.Match(line,
                @"(?:grand\s*total|bill\s*total|total\s*amount|amount\s*paid|paid|to\s*pay|net\s*payable)[:\s]*₹?\s*([\d,]+\.?\d*)",
                RegexOptions.IgnoreCase);
            if (totalMatch.Success)
            {
                var parsed = decimal.TryParse(totalMatch.Groups[1].Value.Replace(",", ""), out var t);
                if (parsed && t > total)
                    total = t;
                continue;
            }

            // Try to extract item lines: "Item Name x2  ₹220" or "Item Name  ₹220"
            var itemMatch = Regex.Match(line,
                @"^(.+?)\s+(?:x\s*(\d+)\s+)?₹?\s*([\d,]+\.?\d*)$",
                RegexOptions.IgnoreCase);
            if (itemMatch.Success)
            {
                var name = itemMatch.Groups[1].Value.Trim();
                var qty = itemMatch.Groups[2].Success ? int.Parse(itemMatch.Groups[2].Value) : 1;
                var price = decimal.TryParse(itemMatch.Groups[3].Value.Replace(",", ""), out var p) ? p : 0;

                // Skip non-item lines (taxes, delivery, etc.)
                if (IsLikelyItem(name) && price > 0)
                {
                    items.Add(new ExtractedInvoiceItem { Name = name, Quantity = qty, Price = price });
                }
            }
        }

        // If no total found from patterns, sum items
        if (total == 0 && items.Count > 0)
            total = items.Sum(i => i.Price * i.Quantity);

        return (items, total);
    }

    /// <summary>
    /// Calculates loyalty points from the invoice total.
    /// Formula: total * 60% (subtracting 40%), rounded down to nearest integer.
    /// </summary>
    public static int CalculatePoints(decimal total)
    {
        if (total <= 0) return 0;
        return (int)Math.Floor(total * 0.60m);
    }

    private static bool IsLikelyItem(string name)
    {
        var lower = name.ToLower();
        // Exclude common non-item lines
        var excludePatterns = new[]
        {
            "tax", "gst", "sgst", "cgst", "delivery", "packaging",
            "discount", "coupon", "subtotal", "sub total", "tip",
            "convenience", "platform", "service charge", "restaurant"
        };
        return !excludePatterns.Any(p => lower.Contains(p));
    }
}
