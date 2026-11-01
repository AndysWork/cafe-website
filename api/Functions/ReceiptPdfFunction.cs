using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Helpers;
using System.Net;
using System.Security.Claims;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Cafe.Api.Functions;

public class ReceiptPdfFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _log;

    public ReceiptPdfFunction(MongoService mongo, AuthService auth, IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _httpClientFactory = httpClientFactory;
        _log = loggerFactory.CreateLogger<ReceiptPdfFunction>();
    }

    [Function("GetReceiptPdf")]
    public async Task<HttpResponseData> GetReceiptPdf(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "orders/{id}/receipt-pdf")] HttpRequestData req,
        string id)
    {
        try
        {
            var authHeader = req.Headers.GetValues("Authorization").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Authentication required" });
                return unauthorized;
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();
            var principal = _auth.ValidateToken(token);

            if (principal == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Invalid or expired token" });
                return unauthorized;
            }

            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var role = principal.FindFirst(ClaimTypes.Role)?.Value;

            var order = await _mongo.GetOrderByIdAsync(id);

            if (order == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            var normalizedRole = (role ?? string.Empty).Trim().ToLowerInvariant();
            if (normalizedRole != "admin" && normalizedRole != "manager" && order.UserId != userId)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
                return forbidden;
            }

            // Get outlet info for branding
            Models.Outlet? outlet = null;
            if (!string.IsNullOrEmpty(order.OutletId))
            {
                outlet = await _mongo.GetOutletByIdAsync(order.OutletId);
            }

            var branding = ResolveBranding(outlet);
            var logoBytes = await TryLoadLogoAsync(branding.LogoUrl);
            var pdfBytes = GenerateReceiptPdf(order, outlet, branding, logoBytes);

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/pdf");
            response.Headers.Add("Content-Disposition", $"attachment; filename=\"receipt-{order.Id?[^6..]}.pdf\"");
            await response.Body.WriteAsync(pdfBytes);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error generating receipt PDF for order {OrderId}", id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while generating the receipt" });
            return res;
        }
    }

    private static byte[] GenerateReceiptPdf(Models.Order order, Models.Outlet? outlet, ReceiptBranding branding, byte[]? logoBytes)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var cafeName = branding.CafeName;
        var cafeAddress = outlet != null
            ? $"{outlet.Address}, {outlet.City}, {outlet.State}"
            : "Kalyani, West Bengal";
        var cafePhone = branding.Phone ?? string.Empty;
        var cafeEmail = branding.Email ?? string.Empty;
        var cafeTagline = branding.Tagline ?? string.Empty;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A5);
                page.MarginVertical(20);
                page.MarginHorizontal(25);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Segoe UI"));

                page.Header().Column(col =>
                {
                    if (logoBytes != null && logoBytes.Length > 0)
                    {
                        col.Item().PaddingBottom(6).AlignCenter().Width(90).Image(logoBytes);
                    }

                    col.Item().AlignCenter().Text(cafeName)
                        .FontSize(18).Bold().FontColor("#0EA5E9");

                    col.Item().AlignCenter().Text(cafeAddress)
                        .FontSize(8).FontColor("#6b7280");

                    if (!string.IsNullOrEmpty(cafePhone) || !string.IsNullOrEmpty(cafeEmail))
                    {
                        var contactLine = string.IsNullOrEmpty(cafePhone)
                            ? $"Email: {cafeEmail}"
                            : string.IsNullOrEmpty(cafeEmail)
                                ? $"Phone: {cafePhone}"
                                : $"Phone: {cafePhone}   |   Email: {cafeEmail}";

                        col.Item().AlignCenter().Text(contactLine)
                            .FontSize(8).FontColor("#6b7280");
                    }

                    if (!string.IsNullOrWhiteSpace(cafeTagline))
                    {
                        col.Item().AlignCenter().Text(cafeTagline)
                            .FontSize(8).Italic().FontColor("#4b5563");
                    }

                    col.Item().PaddingVertical(6).LineHorizontal(1).LineColor("#e2e8f0");

                    col.Item().AlignCenter().Text("ORDER RECEIPT")
                        .FontSize(12).Bold().FontColor("#1a1a2e");

                    col.Item().PaddingTop(4).Row(row =>
                    {
                        row.RelativeItem().Text($"Order #{order.Id?[^6..]}")
                            .FontSize(9).SemiBold().FontColor("#374151");
                        row.RelativeItem().AlignRight().Text(order.CreatedAt.ToString("dd MMM yyyy, hh:mm tt"))
                            .FontSize(8).FontColor("#6b7280");
                    });

                    col.Item().PaddingVertical(4).LineHorizontal(0.5f).LineColor("#e2e8f0");
                });

                page.Content().Column(col =>
                {
                    // Items table
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(4);
                            columns.ConstantColumn(30);
                            columns.ConstantColumn(55);
                            columns.ConstantColumn(60);
                        });

                        // Header
                        table.Header(header =>
                        {
                            header.Cell().PaddingBottom(4).Text("Item").Bold().FontSize(8).FontColor("#374151");
                            header.Cell().PaddingBottom(4).AlignCenter().Text("Qty").Bold().FontSize(8).FontColor("#374151");
                            header.Cell().PaddingBottom(4).AlignRight().Text("Price").Bold().FontSize(8).FontColor("#374151");
                            header.Cell().PaddingBottom(4).AlignRight().Text("Total").Bold().FontSize(8).FontColor("#374151");
                        });

                        foreach (var item in order.Items)
                        {
                            table.Cell().PaddingVertical(2).Text(item.Name).FontSize(8.5f);
                            table.Cell().PaddingVertical(2).AlignCenter().Text(item.Quantity.ToString()).FontSize(8.5f);
                            table.Cell().PaddingVertical(2).AlignRight().Text($"\u20b9{item.Price:N2}").FontSize(8.5f);
                            table.Cell().PaddingVertical(2).AlignRight().Text($"\u20b9{item.Total:N2}").FontSize(8.5f);
                        }
                    });

                    // Dashed separator
                    col.Item().PaddingVertical(6).LineHorizontal(0.5f).LineColor("#d1d5db");

                    // Summary
                    col.Item().Column(summary =>
                    {
                        AddSummaryRow(summary, "Subtotal", $"\u20b9{order.Subtotal:N2}");
                        AddSummaryRow(summary, "GST (2.5%)", $"\u20b9{order.Tax:N2}");
                        AddSummaryRow(summary, "Platform Charge (2.5%)", $"\u20b9{order.PlatformCharge:N2}");

                        if (order.DiscountAmount > 0)
                        {
                            AddSummaryRow(summary, $"Coupon ({order.CouponCode})", $"-\u20b9{order.DiscountAmount:N2}", "#16a34a");
                        }

                        if (order.LoyaltyDiscountAmount > 0)
                        {
                            AddSummaryRow(summary, $"Loyalty ({order.LoyaltyPointsUsed} pts)", $"-\u20b9{order.LoyaltyDiscountAmount:N2}", "#16a34a");
                        }

                        // Total line
                        summary.Item().PaddingTop(4).LineHorizontal(1).LineColor("#1a1a2e");
                        summary.Item().PaddingTop(4).Row(row =>
                        {
                            row.RelativeItem().Text("Total").Bold().FontSize(11).FontColor("#1a1a2e");
                            row.RelativeItem().AlignRight().Text($"\u20b9{order.Total:N2}").Bold().FontSize(11).FontColor("#1a1a2e");
                        });
                    });

                    // Payment & Delivery info
                    col.Item().PaddingTop(10).LineHorizontal(0.5f).LineColor("#e2e8f0");

                    col.Item().PaddingTop(6).Column(info =>
                    {
                        var paymentDisplay = order.PaymentMethod switch
                        {
                            "upi-qr" => "UPI QR",
                            "cash_at_counter" => "Cash at Counter",
                            "dine_in_tab" => "Dine-In Tab",
                            _ => "Cash on Delivery"
                        };
                        AddInfoRow(info, "Payment", $"{paymentDisplay} — {order.PaymentStatus}");
                        AddInfoRow(info, "Customer", order.Username);

                        if (!string.IsNullOrEmpty(order.DeliveryAddress))
                            AddInfoRow(info, "Address", order.DeliveryAddress);

                        if (!string.IsNullOrEmpty(order.PhoneNumber))
                            AddInfoRow(info, "Phone", order.PhoneNumber);

                        if (!string.IsNullOrEmpty(order.Notes))
                            AddInfoRow(info, "Notes", order.Notes);
                    });
                });

                page.Footer().Column(col =>
                {
                    col.Item().PaddingTop(10).LineHorizontal(0.5f).LineColor("#e2e8f0");
                    col.Item().PaddingTop(6).AlignCenter().Text("Thank you for your order!")
                        .FontSize(10).SemiBold().FontColor("#0EA5E9");
                    var footerLine = !string.IsNullOrWhiteSpace(cafeTagline)
                        ? cafeTagline
                        : $"Visit us again at {cafeName}";
                    col.Item().AlignCenter().Text(footerLine)
                        .FontSize(7).FontColor("#9ca3af");
                });
            });
        });

        return document.GeneratePdf();
    }

    private static void AddSummaryRow(ColumnDescriptor col, string label, string value, string? color = null)
    {
        col.Item().PaddingVertical(1).Row(row =>
        {
            row.RelativeItem().Text(label).FontSize(8.5f).FontColor("#6b7280");
            var valueText = row.RelativeItem().AlignRight().Text(value).FontSize(8.5f);
            if (color != null)
                valueText.FontColor(color);
            else
                valueText.FontColor("#374151");
        });
    }

    private static void AddInfoRow(ColumnDescriptor col, string label, string value)
    {
        col.Item().PaddingVertical(1).Row(row =>
        {
            row.ConstantItem(65).Text($"{label}:").FontSize(8).SemiBold().FontColor("#6b7280");
            row.RelativeItem().Text(value).FontSize(8).FontColor("#374151");
        });
    }

    private static ReceiptBranding ResolveBranding(Models.Outlet? outlet)
    {
        var cafeName = !string.IsNullOrWhiteSpace(outlet?.OutletName)
            ? outlet.OutletName.Trim()
            : (Environment.GetEnvironmentVariable("CafeBranding__Name")?.Trim() ?? "Maa Tara Cafe");

        var phone = !string.IsNullOrWhiteSpace(outlet?.PhoneNumber)
            ? outlet.PhoneNumber.Trim()
            : Environment.GetEnvironmentVariable("CafeBranding__Phone")?.Trim();

        var email = !string.IsNullOrWhiteSpace(outlet?.Email)
            ? outlet.Email.Trim()
            : Environment.GetEnvironmentVariable("CafeBranding__Email")?.Trim();

        var tagline = !string.IsNullOrWhiteSpace(outlet?.Settings?.InvoiceTagline)
            ? outlet.Settings.InvoiceTagline.Trim()
            : Environment.GetEnvironmentVariable("CafeBranding__Tagline")?.Trim();

        var logoUrl = !string.IsNullOrWhiteSpace(outlet?.Settings?.InvoiceLogoUrl)
            ? outlet.Settings.InvoiceLogoUrl.Trim()
            : Environment.GetEnvironmentVariable("CafeBranding__LogoUrl")?.Trim();

        return new ReceiptBranding(cafeName, phone, email, tagline, logoUrl);
    }

    private async Task<byte[]?> TryLoadLogoAsync(string? logoUrl)
    {
        if (string.IsNullOrWhiteSpace(logoUrl) || !Uri.TryCreate(logoUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            _log.LogWarning("Skipping invoice logo with unsupported URI scheme: {Scheme}", uri.Scheme);
            return null;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(6);
            using var response = await client.GetAsync(uri);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning("Unable to load invoice logo from {LogoUrl}. Status: {StatusCode}", logoUrl, response.StatusCode);
                return null;
            }

            return await response.Content.ReadAsByteArrayAsync();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Unable to load invoice logo from {LogoUrl}", logoUrl);
            return null;
        }
    }

    private sealed record ReceiptBranding(string CafeName, string? Phone, string? Email, string? Tagline, string? LogoUrl);
}
