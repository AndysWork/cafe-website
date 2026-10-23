using System.Net.Http.Json;
using Cafe.Api.Models;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Services;

public class ThermalPrinterService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ThermalPrinterService> _logger;

    public ThermalPrinterService(IHttpClientFactory httpClientFactory, ILogger<ThermalPrinterService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ThermalPrinter__KotEndpoint"));

    public bool IsInvoiceConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ThermalPrinter__InvoiceEndpoint"))
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ThermalPrinter__KotEndpoint"));

    public async Task<bool> TryPrintKotAsync(Order order, string kotText, string trigger)
    {
        var endpoint = Environment.GetEnvironmentVariable("ThermalPrinter__KotEndpoint")?.Trim();
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return false;
        }

        var enabledRaw = Environment.GetEnvironmentVariable("ThermalPrinter__Enabled");
        if (!string.IsNullOrWhiteSpace(enabledRaw)
            && bool.TryParse(enabledRaw, out var enabled)
            && !enabled)
        {
            return false;
        }

        var printerName = Environment.GetEnvironmentVariable("ThermalPrinter__PrinterName")?.Trim();
        var apiKey = Environment.GetEnvironmentVariable("ThermalPrinter__ApiKey")?.Trim();
        var copiesRaw = Environment.GetEnvironmentVariable("ThermalPrinter__KotCopies");
        var copies = 1;
        if (!string.IsNullOrWhiteSpace(copiesRaw) && int.TryParse(copiesRaw, out var parsedCopies) && parsedCopies > 0)
        {
            copies = parsedCopies;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(8);

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(new
                {
                    orderId = order.Id,
                    outletId = order.OutletId,
                    tableNumber = order.TableNumber,
                    roundNumber = order.RoundNumber,
                    printerName,
                    copies,
                    kotText,
                    source = "cafe-api",
                    trigger,
                    generatedAt = MongoService.GetIstNow()
                })
            };

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Add("X-Api-Key", apiKey);
            }

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Thermal KOT print failed for Order {OrderId}. Status: {StatusCode}",
                    order.Id,
                    response.StatusCode);
                return false;
            }

            _logger.LogInformation(
                "Thermal KOT print dispatched for Order {OrderId} (Outlet: {OutletId}, Table: {Table})",
                order.Id,
                order.OutletId,
                order.TableNumber);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Thermal KOT print dispatch error for Order {OrderId}", order.Id);
            return false;
        }
    }

    public async Task<bool> TryPrintInvoiceAsync(Order order, string invoiceText, string trigger)
    {
        var endpoint = Environment.GetEnvironmentVariable("ThermalPrinter__InvoiceEndpoint")?.Trim();
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            endpoint = Environment.GetEnvironmentVariable("ThermalPrinter__KotEndpoint")?.Trim();
        }

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return false;
        }

        var enabledRaw = Environment.GetEnvironmentVariable("ThermalPrinter__Enabled");
        if (!string.IsNullOrWhiteSpace(enabledRaw)
            && bool.TryParse(enabledRaw, out var enabled)
            && !enabled)
        {
            return false;
        }

        var printerName = Environment.GetEnvironmentVariable("ThermalPrinter__PrinterName")?.Trim();
        var apiKey = Environment.GetEnvironmentVariable("ThermalPrinter__ApiKey")?.Trim();
        var copiesRaw = Environment.GetEnvironmentVariable("ThermalPrinter__InvoiceCopies");
        if (string.IsNullOrWhiteSpace(copiesRaw))
        {
            copiesRaw = Environment.GetEnvironmentVariable("ThermalPrinter__KotCopies");
        }

        var copies = 1;
        if (!string.IsNullOrWhiteSpace(copiesRaw) && int.TryParse(copiesRaw, out var parsedCopies) && parsedCopies > 0)
        {
            copies = parsedCopies;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(8);

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(new
                {
                    orderId = order.Id,
                    outletId = order.OutletId,
                    printerName,
                    copies,
                    invoiceText,
                    source = "cafe-api",
                    trigger,
                    documentType = "invoice",
                    generatedAt = MongoService.GetIstNow()
                })
            };

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Add("X-Api-Key", apiKey);
            }

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Thermal invoice print failed for Order {OrderId}. Status: {StatusCode}",
                    order.Id,
                    response.StatusCode);
                return false;
            }

            _logger.LogInformation(
                "Thermal invoice print dispatched for Order {OrderId} (Outlet: {OutletId})",
                order.Id,
                order.OutletId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Thermal invoice print dispatch error for Order {OrderId}", order.Id);
            return false;
        }
    }
}