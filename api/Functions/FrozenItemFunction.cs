using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Cafe.Api.Services;
using Cafe.Api.Models;
using OfficeOpenXml;

namespace Cafe.Api.Functions;

public class FrozenItemFunction
{
    private readonly MongoService _mongoService;
    private readonly ILogger<FrozenItemFunction> _logger;

    public FrozenItemFunction(MongoService mongoService, ILogger<FrozenItemFunction> logger)
    {
        _mongoService = mongoService;
        _logger = logger;
        // Set EPPlus license context
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
    }

    [Function("GetAllFrozenItems")]
    public async Task<HttpResponseData> GetAllFrozenItems(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "frozen-items")] HttpRequestData req)
    {
        try
        {
            var items = await _mongoService.GetAllFrozenItemsAsync();
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(items);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all frozen items");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("GetActiveFrozenItems")]
    public async Task<HttpResponseData> GetActiveFrozenItems(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "frozen-items/active")] HttpRequestData req)
    {
        try
        {
            var items = await _mongoService.GetActiveFrozenItemsAsync();
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(items);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active frozen items");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("GetFrozenItemById")]
    public async Task<HttpResponseData> GetFrozenItemById(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "frozen-items/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var item = await _mongoService.GetFrozenItemByIdAsync(id);
            if (item == null)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync("Frozen item not found");
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(item);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting frozen item by ID: {Id}", id);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("CreateFrozenItem")]
    public async Task<HttpResponseData> CreateFrozenItem(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "frozen-items")] HttpRequestData req)
    {
        try
        {
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var item = JsonSerializer.Deserialize<FrozenItem>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (item == null || string.IsNullOrEmpty(item.ItemName))
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Invalid frozen item data");
                return badRequestResponse;
            }

            var createdItem = await _mongoService.CreateFrozenItemAsync(item);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(createdItem);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating frozen item");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("UpdateFrozenItem")]
    public async Task<HttpResponseData> UpdateFrozenItem(
        [HttpTrigger(AuthorizationLevel.Function, "put", Route = "frozen-items/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var item = JsonSerializer.Deserialize<FrozenItem>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (item == null)
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Invalid frozen item data");
                return badRequestResponse;
            }

            var success = await _mongoService.UpdateFrozenItemAsync(id, item);
            if (!success)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync("Frozen item not found");
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Frozen item updated successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating frozen item: {Id}", id);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("DeleteFrozenItem")]
    public async Task<HttpResponseData> DeleteFrozenItem(
        [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "frozen-items/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var success = await _mongoService.DeleteFrozenItemAsync(id);
            if (!success)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync("Frozen item not found");
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Frozen item deleted successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting frozen item: {Id}", id);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("UploadFrozenItemsExcel")]
    public async Task<HttpResponseData> UploadFrozenItemsExcel(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "frozen-items/upload")] HttpRequestData req)
    {
        try
        {
            // Read the multipart form data
            var contentType = req.Headers.GetValues("Content-Type").FirstOrDefault();
            if (contentType == null || !contentType.Contains("multipart/form-data"))
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Content-Type must be multipart/form-data");
                return badRequestResponse;
            }

            using var memoryStream = new MemoryStream();
            await req.Body.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            var items = new List<FrozenItemUpload>();

            using (var package = new ExcelPackage(memoryStream))
            {
                if (package.Workbook.Worksheets.Count == 0)
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Excel file contains no worksheets");
                    return badRequestResponse;
                }

                var worksheet = package.Workbook.Worksheets[0];
                int rowCount = worksheet.Dimension?.Rows ?? 0;

                // Validate headers (row 1)
                if (rowCount < 2)
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Excel file must contain headers and at least one data row");
                    return badRequestResponse;
                }

                // Parse data rows (starting from row 2)
                for (int row = 2; row <= rowCount; row++)
                {
                    try
                    {
                        var itemName = worksheet.Cells[row, 1].Text?.Trim();
                        if (string.IsNullOrEmpty(itemName)) continue; // Skip empty rows

                        var item = new FrozenItemUpload
                        {
                            ItemName = itemName,
                            Quantity = int.TryParse(worksheet.Cells[row, 2].Text, out int qty) ? qty : 0,
                            PacketWeight = decimal.TryParse(worksheet.Cells[row, 3].Text, out decimal pkgWt) ? pkgWt : 0,
                            BuyPrice = decimal.TryParse(worksheet.Cells[row, 4].Text, out decimal buyPr) ? buyPr : 0,
                            PerPiecePrice = decimal.TryParse(worksheet.Cells[row, 5].Text, out decimal ppPrice) ? ppPrice : 0,
                            PerPieceWeight = decimal.TryParse(worksheet.Cells[row, 6].Text, out decimal ppWeight) ? ppWeight : 0,
                            Vendor = worksheet.Cells[row, 7].Text?.Trim() ?? ""
                        };

                        items.Add(item);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Error parsing row {Row}: {Error}", row, ex.Message);
                    }
                }
            }

            if (items.Count == 0)
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("No valid items found in Excel file");
                return badRequestResponse;
            }

            // Process the items
            var (success, failed, errors) = await _mongoService.BulkUploadFrozenItemsAsync(items);

            var result = new
            {
                success = success,
                failed = failed,
                total = items.Count,
                errors = errors,
                message = $"Successfully processed {success} items, {failed} failed"
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(result);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading frozen items Excel");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("SyncFrozenItemsToInventory")]
    public async Task<HttpResponseData> SyncFrozenItemsToInventory(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "frozen-items/sync-inventory")] HttpRequestData req)
    {
        try
        {
            var syncedCount = await _mongoService.SyncAllFrozenItemsToInventoryAsync();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = $"Successfully synced {syncedCount} frozen items to inventory",
                syncedCount = syncedCount
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing frozen items to inventory");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }
}
