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
    private readonly ILogger _log;

    public ReceiptPdfFunction(MongoService mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
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

            if (role != "admin" && order.UserId != userId)
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

            var pdfBytes = GenerateReceiptPdf(order, outlet);

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

    private static byte[] GenerateReceiptPdf(Models.Order order, Models.Outlet? outlet)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var cafeName = outlet?.OutletName ?? "Maa Tara Cafe";
        var cafeAddress = outlet != null
            ? $"{outlet.Address}, {outlet.City}, {outlet.State}"
            : "Kalyani, West Bengal";
        var cafePhone = outlet?.PhoneNumber ?? "";

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
                    col.Item().AlignCenter().Text(cafeName)
                        .FontSize(18).Bold().FontColor("#0EA5E9");

                    col.Item().AlignCenter().Text(cafeAddress)
                        .FontSize(8).FontColor("#6b7280");

                    if (!string.IsNullOrEmpty(cafePhone))
                    {
                        col.Item().AlignCenter().Text($"Phone: {cafePhone}")
                            .FontSize(8).FontColor("#6b7280");
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
                        var paymentDisplay = order.PaymentMethod == "razorpay" ? "Online (Razorpay)" : "Cash on Delivery";
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
                    col.Item().AlignCenter().Text("Visit us again at Maa Tara Cafe")
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
}
