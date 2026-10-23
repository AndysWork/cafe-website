using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Models;
using Cafe.Api.Services;
using Cafe.Api.Repositories;
using Cafe.Api.Helpers;
using System.Net;

namespace Cafe.Api.Functions;

public class KotFunction
{
    private readonly IOrderRepository _mongo;
    private readonly IInventoryRepository _inventoryRepo;
    private readonly AuthService _auth;
    private readonly KotRenderService _kotRender;
    private readonly ThermalPrinterService _thermalPrinter;
    private readonly ILogger _log;

    public KotFunction(
        IOrderRepository mongo,
        IInventoryRepository inventoryRepo,
        AuthService auth,
        KotRenderService kotRender,
        ThermalPrinterService thermalPrinter,
        ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _inventoryRepo = inventoryRepo;
        _auth = auth;
        _kotRender = kotRender;
        _thermalPrinter = thermalPrinter;
        _log = loggerFactory.CreateLogger<KotFunction>();
    }

    [Function("PrintSampleKot")]
    public async Task<HttpResponseData> PrintSampleKot(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "kitchen/kot/test-print")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            if (!_thermalPrinter.IsConfigured)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new
                {
                    error = "Thermal printer is not configured",
                    required = "Set ThermalPrinter__KotEndpoint (and optional ThermalPrinter__ApiKey, ThermalPrinter__PrinterName, ThermalPrinter__KotCopies)"
                });
                return badReq;
            }

            var sampleOrder = new Order
            {
                Id = $"TEST-{DateTime.UtcNow:yyyyMMddHHmmss}",
                OutletId = "test-outlet",
                Username = "Printer Setup",
                OrderType = "dine-in",
                TableNumber = "TEST-01",
                PaymentMethod = "dine_in_tab",
                RoundNumber = 1,
                CreatedAt = MongoService.GetIstNow(),
                Items = new List<OrderItem>
                {
                    new() { Name = "Masala Chai", Quantity = 2 },
                    new() { Name = "Veg Sandwich", Quantity = 1 },
                    new() { Name = "French Fries", Quantity = 1 }
                },
                PreparationNotes = "Sample KOT print verification",
                Notes = $"Triggered by {userId ?? "admin"}"
            };

            var kotText = _kotRender.Render(sampleOrder, 48);
            var printed = await _thermalPrinter.TryPrintKotAsync(sampleOrder, kotText, "AdminTestEndpoint");

            if (!printed)
            {
                var badGateway = req.CreateResponse(HttpStatusCode.BadGateway);
                await badGateway.WriteAsJsonAsync(new
                {
                    error = "Thermal print dispatch failed",
                    message = "Printer bridge is reachable but rejected or failed to process the request"
                });
                return badGateway;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = "Sample KOT print dispatched",
                orderId = sampleOrder.Id,
                triggeredBy = userId,
                generatedAt = MongoService.GetIstNow()
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error dispatching sample KOT print");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while dispatching sample print" });
            return res;
        }
    }

    [Function("GenerateKot")]
    public async Task<HttpResponseData> GenerateKot(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "kitchen/orders/{orderId}/kot")] HttpRequestData req, string orderId)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateKitchenAccessRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var order = await _mongo.GetOrderByIdAsync(orderId);
            if (order == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            var kotText = _kotRender.Render(order, 48);

            // Recipe-Inventory Sync: When KOT is generated/dispatched, auto-deduct recipe inventory if not yet deducted
            if (!order.InventoryDeducted)
            {
                try
                {
                    var count = await _inventoryRepo.DeductInventoryForOrderRecipesAsync(order);
                    if (count > 0)
                    {
                        _log.LogInformation("KOT dispatch auto-deducted {Count} recipe ingredients for Order {OrderId}", count, order.Id);
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to auto-deduct recipe inventory during KOT generation for Order {OrderId}", order.Id);
                }
            }

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
}
