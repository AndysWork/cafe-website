using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Repositories;
using Cafe.Api.Helpers;
using System.Net;
using System.Text;

namespace Cafe.Api.Functions;

public class KotFunction
{
    private readonly IOrderRepository _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public KotFunction(IOrderRepository mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<KotFunction>();
    }

    [Function("GenerateKot")]
    public async Task<HttpResponseData> GenerateKot(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "kitchen/orders/{orderId}/kot")] HttpRequestData req, string orderId)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var order = await _mongo.GetOrderByIdAsync(orderId);
            if (order == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            // Generate 80mm thermal printer format (48 chars per line)
            var sb = new StringBuilder();
            var line = new string('-', 48);

            sb.AppendLine(CenterText("MAA TARA CAFE", 48));
            sb.AppendLine(CenterText("KITCHEN ORDER TICKET", 48));
            sb.AppendLine(line);
            sb.AppendLine($"Order #: {order.Id}");
            sb.AppendLine($"Date: {order.CreatedAt:dd/MM/yyyy HH:mm}");
            sb.AppendLine($"Type: {order.OrderType?.ToUpper() ?? "DELIVERY"}");

            if (!string.IsNullOrWhiteSpace(order.TableNumber))
                sb.AppendLine($"Table: {order.TableNumber}");

            if (!string.IsNullOrWhiteSpace(order.Username))
                sb.AppendLine($"Customer: {order.Username}");

            sb.AppendLine(line);
            sb.AppendLine(FormatKotRow("ITEM", "QTY", 48));
            sb.AppendLine(line);

            foreach (var item in order.Items)
            {
                sb.AppendLine(FormatKotRow(item.Name, item.Quantity.ToString(), 48));
            }

            sb.AppendLine(line);

            if (!string.IsNullOrWhiteSpace(order.Notes))
            {
                sb.AppendLine("SPECIAL INSTRUCTIONS:");
                sb.AppendLine(order.Notes);
                sb.AppendLine(line);
            }

            if (order.IsScheduled && order.ScheduledFor.HasValue)
            {
                sb.AppendLine($"*** SCHEDULED: {order.ScheduledFor.Value:dd/MM HH:mm} ***");
                sb.AppendLine(line);
            }

            sb.AppendLine(CenterText($"Total Items: {order.Items.Sum(i => i.Quantity)}", 48));
            sb.AppendLine("");

            var kotText = sb.ToString();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                orderId = order.Id,
                kotText,
                printWidth = 48,
                generatedAt = MongoService.GetIstNow()
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error generating KOT");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    private static string CenterText(string text, int width)
    {
        if (text.Length >= width) return text;
        int padding = (width - text.Length) / 2;
        return text.PadLeft(padding + text.Length).PadRight(width);
    }

    private static string FormatKotRow(string left, string right, int width)
    {
        int available = width - right.Length - 2;
        if (left.Length > available) left = left[..available];
        return left.PadRight(available) + "  " + right;
    }
}
