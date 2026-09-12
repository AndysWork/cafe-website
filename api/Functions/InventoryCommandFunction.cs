using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace Cafe.Api.Functions;

public class InventoryCommandFunction
{
    private readonly MongoService _mongoService;
    private readonly AuthService _authService;
    private readonly IEmailService _emailService;
    private readonly ILogger<InventoryCommandFunction> _logger;

    public InventoryCommandFunction(
        MongoService mongoService,
        AuthService authService,
        IEmailService emailService,
        ILogger<InventoryCommandFunction> logger)
    {
        _mongoService = mongoService;
        _authService = authService;
        _emailService = emailService;
        _logger = logger;
    }

    [Function("CreateInventory")]
    public async Task<HttpResponseData> CreateInventory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "inventory")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return authError!;

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var inventory = JsonSerializer.Deserialize<Inventory>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            });

            if (inventory == null)
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Invalid inventory data");
                return badRequestResponse;
            }

            // Validate required fields
            if (string.IsNullOrEmpty(inventory.IngredientName) || string.IsNullOrEmpty(inventory.Unit))
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Ingredient name and unit are required");
                return badRequestResponse;
            }

            // Validate outlet access and assign outlet
            var (hasAccess, outletId, accessError) = await OutletHelper.ValidateOutletAccess(req, _authService, _mongoService);
            if (!hasAccess)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = accessError });
                return forbidden;
            }
            
            inventory.OutletId = outletId;
            var createdInventory = await _mongoService.CreateInventoryAsync(inventory);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(createdInventory);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating inventory");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return response;
        }
    }

    [Function("UpdateInventory")]
    public async Task<HttpResponseData> UpdateInventory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "inventory/item/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return authError!;

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var inventory = JsonSerializer.Deserialize<Inventory>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            });

            if (inventory == null)
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Invalid inventory data");
                return badRequestResponse;
            }

            var success = await _mongoService.UpdateInventoryAsync(id, inventory);
            if (!success)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync("Inventory item not found");
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Inventory updated successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating inventory: {Id}", id);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return response;
        }
    }

    [Function("DeleteInventory")]
    public async Task<HttpResponseData> DeleteInventory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "inventory/item/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return authError!;

            var success = await _mongoService.DeleteInventoryAsync(id);
            if (!success)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync("Inventory item not found");
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Inventory deleted successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting inventory: {Id}", id);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return response;
        }
    }

    [Function("StockIn")]
    public async Task<HttpResponseData> StockIn(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "inventory/item/{id}/stock-in")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return authError!;

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var data = JsonSerializer.Deserialize<StockInRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (data == null || data.Quantity <= 0)
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Invalid stock-in data. Quantity must be positive.");
                return badRequestResponse;
            }

            var success = await _mongoService.StockInAsync(
                id,
                data.Quantity,
                data.CostPerUnit,
                data.SupplierName,
                data.ReferenceNumber,
                data.PerformedBy ?? "System",
                data.ExpiryDate,
                data.PurchasePrice
            );

            if (!success)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync("Inventory item not found");
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Stock added successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding stock: {Id}", id);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return response;
        }
    }

    [Function("StockOut")]
    public async Task<HttpResponseData> StockOut(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "inventory/item/{id}/stock-out")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return authError!;

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var data = JsonSerializer.Deserialize<StockOutRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (data == null || data.Quantity <= 0)
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Invalid stock-out data. Quantity must be positive.");
                return badRequestResponse;
            }

            var success = await _mongoService.StockOutAsync(
                id,
                data.Quantity,
                data.Reason ?? "Stock usage",
                data.PerformedBy ?? "System",
                data.BatchId
            );

            if (!success)
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await errorResponse.WriteStringAsync("Inventory item not found or insufficient stock");
                return errorResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Stock removed successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing stock: {Id}", id);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return response;
        }
    }

    [Function("LogBatchWastage")]
    public async Task<HttpResponseData> LogBatchWastage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "inventory/item/{id}/wastage")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, userId, role, authError) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _authService);
            if (!isAuthorized) return authError!;

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var data = JsonSerializer.Deserialize<LogBatchWastageRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (data == null || data.Quantity <= 0)
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Invalid wastage data. Quantity must be positive.");
                return badRequestResponse;
            }

            data.PerformedBy = userId ?? data.PerformedBy ?? "admin";
            var result = await _mongoService.LogBatchWastageAsync(id, data);

            if (!result.Success)
            {
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteAsJsonAsync(result);
                return badResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(result);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging batch wastage: {Id}", id);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return response;
        }
    }

    [Function("AdjustStock")]
    public async Task<HttpResponseData> AdjustStock(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "inventory/item/{id}/adjust")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return authError!;

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var data = JsonSerializer.Deserialize<StockAdjustmentRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (data == null || data.QuantityChange == 0)
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Invalid adjustment data");
                return badRequestResponse;
            }

            var success = await _mongoService.AdjustStockAsync(
                id,
                data.QuantityChange,
                TransactionType.Adjustment,
                data.Reason ?? "Stock adjustment",
                data.PerformedBy ?? "System",
                data.ReferenceNumber
            );

            if (!success)
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await errorResponse.WriteStringAsync("Inventory item not found or invalid adjustment");
                return errorResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Stock adjusted successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adjusting stock: {Id}", id);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return response;
        }
    }

    [Function("ResolveAlert")]
    public async Task<HttpResponseData> ResolveAlert(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "inventory/alerts/{alertId}/resolve")] HttpRequestData req,
        string alertId)
    {
        try
        {
            var (isAuthorized, _, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return authError!;

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var data = JsonSerializer.Deserialize<ResolveAlertRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var success = await _mongoService.ResolveAlertAsync(alertId, data?.ResolvedBy ?? "System");
            if (!success)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync("Alert not found");
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Alert resolved successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving alert: {AlertId}", alertId);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return response;
        }
    }

    [Function("MigrateInventoryTransactionOutlets")]
    public async Task<HttpResponseData> MigrateInventoryTransactionOutlets(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "inventory/migrate-transaction-outlets")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return authError!;

            _logger.LogInformation("Starting migration of inventory transaction outlet IDs");
            
            // Parse request body to get default outlet ID
            string? defaultOutletId = null;
            try
            {
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                if (!string.IsNullOrEmpty(requestBody))
                {
                    var jsonDoc = System.Text.Json.JsonDocument.Parse(requestBody);
                    if (jsonDoc.RootElement.TryGetProperty("defaultOutletId", out var outletIdElement))
                    {
                        defaultOutletId = outletIdElement.GetString();
                    }
                }
            }
            catch
            {
                // If parsing fails, continue without default outlet ID
            }
            
            var updated = await _mongoService.MigrateInventoryTransactionOutletIdsAsync(defaultOutletId);
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { 
                success = true, 
                message = $"Successfully updated {updated} inventory transactions with outlet IDs",
                updatedCount = updated,
                defaultOutletIdUsed = defaultOutletId
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error migrating inventory transaction outlet IDs");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { 
                success = false, 
                error = "An internal error occurred" 
            });
            return response;
        }
    }

    [Function("DownloadInventoryTemplate")]
    public async Task<HttpResponseData> DownloadInventoryTemplate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "inventory/template")] HttpRequestData req)
    {
        try
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("Inventory Items");

            // Define column headers
            string[] headers = new[]
            {
                "ItemName",
                "Category",
                "Unit",
                "MinimumStock",
                "MaximumStock",
                "ReorderQuantity",
                "StorageLocation",
                "InitialStock",
                "CostPerUnit",
                "SupplierName",
                "ExpiryDate"
            };

            for (int col = 1; col <= headers.Length; col++)
            {
                var cell = worksheet.Cells[1, col];
                cell.Value = headers[col - 1];
                cell.Style.Font.Bold = true;
                cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
                cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(12, 74, 110)); // Deep theme #0C4A6E
                cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            }

            var today = MongoService.GetIstNow().Date;

            // Sample rows with diverse categories and realistic data
            var sampleRows = new object[][]
            {
                new object[] { "Amul Taaza Milk 1L", "Dairy", "L", 10, 60, 20, "Walk-in Chiller Rack 1", 24, 56.00, "Amul Dairy Distributor", today.AddDays(3).ToString("yyyy-MM-dd") },
                new object[] { "Fresh Chicken Breast (Boneless)", "Meats", "kg", 5, 30, 10, "Meat Freezer B", 12, 240.00, "Quality Poultry Farms", today.AddDays(1).ToString("yyyy-MM-dd") },
                new object[] { "English Carrots (Grade A)", "Vegetables", "kg", 4, 25, 10, "Veg Crate 3", 10, 42.50, "Fresh Mandi Wholesale", today.AddDays(2).ToString("yyyy-MM-dd") },
                new object[] { "Burger Buns (Pack of 6)", "Bakery", "packet", 10, 50, 20, "Dry Bakery Rack 2", 18, 45.00, "Golden Crust Bakery", today.AddDays(3).ToString("yyyy-MM-dd") },
                new object[] { "French Fries 9mm Frozen (2.5kg)", "frozen", "packet", 4, 20, 8, "Deep Freezer 1", 6, 310.00, "McCain Foods India", today.AddDays(30).ToString("yyyy-MM-dd") },
                new object[] { "Arabica Coffee Beans 1kg", "Beverages", "kg", 3, 15, 5, "Espresso Bar Shelf", 5, 850.00, "Blue Tokai Coffee Roasters", today.AddDays(90).ToString("yyyy-MM-dd") },
                new object[] { "Refined Sunflower Oil 5L", "Oils", "L", 5, 30, 10, "Kitchen Oil Drum Rack", 15, 120.00, "Fortune Edible Oils", today.AddDays(180).ToString("yyyy-MM-dd") }
            };

            for (int r = 0; r < sampleRows.Length; r++)
            {
                int rowNum = r + 2;
                for (int c = 0; c < headers.Length; c++)
                {
                    worksheet.Cells[rowNum, c + 1].Value = sampleRows[r][c];
                }
            }

            worksheet.Row(1).Height = 26;
            for (int r = 2; r <= sampleRows.Length + 1; r++)
            {
                worksheet.Row(r).Height = 20;
            }

            worksheet.Cells.AutoFitColumns();
            // Add slight margin to each column
            for (int col = 1; col <= headers.Length; col++)
            {
                worksheet.Column(col).Width = Math.Max(worksheet.Column(col).Width + 4, 15);
            }

            var excelBytes = package.GetAsByteArray();
            var res = req.CreateResponse(HttpStatusCode.OK);
            res.Headers.Add("Content-Type", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            res.Headers.Add("Content-Disposition", "attachment; filename=inventory_template.xlsx");
            await res.WriteBytesAsync(excelBytes);
            return res;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating inventory template");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "An error occurred while generating inventory template" });
            return errorResponse;
        }
    }

    [Function("UploadInventoryExcel")]
    public async Task<HttpResponseData> UploadInventoryExcel(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "inventory/upload")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, role, authError) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _authService);
            if (!isAuthorized) return authError!;

            var (hasAccess, outletId, accessError) = await OutletHelper.ValidateOutletAccess(req, _authService, _mongoService);
            if (!hasAccess || string.IsNullOrEmpty(outletId))
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = accessError ?? "Outlet access required" });
                return forbidden;
            }

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            byte[] fileBytes;
            using (var memoryStream = new MemoryStream())
            {
                await req.Body.CopyToAsync(memoryStream);
                fileBytes = memoryStream.ToArray();
            }

            if (fileBytes == null || fileBytes.Length == 0)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "No file data received in request" });
                return badReq;
            }

            const int MaxFileSizeBytes = 10 * 1024 * 1024; // 10MB
            if (fileBytes.Length > MaxFileSizeBytes)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "File size exceeds 10MB limit" });
                return badReq;
            }

            // Extract excel data (handles multipart boundary or raw binary)
            var excelData = ExtractExcelFromMultipart(fileBytes);
            if (excelData == null || excelData.Length == 0)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Invalid Excel file format. Please upload a valid .xlsx file" });
                return badReq;
            }

            var items = new List<InventoryItemUpload>();

            using (var stream = new MemoryStream(excelData))
            using (var package = new ExcelPackage(stream))
            {
                if (package.Workbook.Worksheets.Count == 0)
                {
                    var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badReq.WriteAsJsonAsync(new { error = "Excel workbook contains no worksheets" });
                    return badReq;
                }

                var worksheet = package.Workbook.Worksheets[0];
                int rowCount = worksheet.Dimension?.Rows ?? 0;
                int colCount = worksheet.Dimension?.Columns ?? 0;

                if (rowCount < 2)
                {
                    var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badReq.WriteAsJsonAsync(new { error = "Excel sheet must contain headers in row 1 and at least one data row" });
                    return badReq;
                }

                // Map header column indices flexibly (case-insensitive and whitespace-trimmed)
                var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int col = 1; col <= colCount; col++)
                {
                    var headerVal = worksheet.Cells[1, col].Text?.Trim();
                    if (!string.IsNullOrEmpty(headerVal))
                    {
                        var normalized = headerVal.Replace(" ", "").Replace("_", "").ToLowerInvariant();
                        colMap[normalized] = col;
                    }
                }

                int GetCol(params string[] aliases)
                {
                    foreach (var a in aliases)
                    {
                        var norm = a.Replace(" ", "").Replace("_", "").ToLowerInvariant();
                        if (colMap.TryGetValue(norm, out int colIdx)) return colIdx;
                    }
                    return -1;
                }

                int colItemName = GetCol("ItemName", "Item", "IngredientName", "Name");
                int colCategory = GetCol("Category");
                int colUnit = GetCol("Unit", "MeasurementUnit", "UOM");
                int colMinStock = GetCol("MinimumStock", "MinStock", "Min");
                int colMaxStock = GetCol("MaximumStock", "MaxStock", "Max");
                int colReorderQty = GetCol("ReorderQuantity", "ReorderQty", "Reorder");
                int colStorage = GetCol("StorageLocation", "Location", "Storage");
                int colInitialStock = GetCol("InitialStock", "CurrentStock", "Stock", "Quantity", "Qty");
                int colCost = GetCol("CostPerUnit", "Cost", "UnitCost", "Price", "BuyPrice");
                int colSupplier = GetCol("SupplierName", "Supplier", "Vendor");
                int colExpiry = GetCol("ExpiryDate", "Expiry", "ExpDate");

                // If no header mapping matched, fallback to default positions (1 to 11)
                if (colItemName == -1) colItemName = 1;
                if (colCategory == -1) colCategory = 2;
                if (colUnit == -1) colUnit = 3;
                if (colMinStock == -1) colMinStock = 4;
                if (colMaxStock == -1) colMaxStock = 5;
                if (colReorderQty == -1) colReorderQty = 6;
                if (colStorage == -1) colStorage = 7;
                if (colInitialStock == -1) colInitialStock = 8;
                if (colCost == -1) colCost = 9;
                if (colSupplier == -1) colSupplier = 10;
                if (colExpiry == -1) colExpiry = 11;

                for (int row = 2; row <= rowCount; row++)
                {
                    var itemName = colItemName > 0 ? worksheet.Cells[row, colItemName].Text?.Trim() : null;
                    if (string.IsNullOrEmpty(itemName)) continue; // skip blank rows

                    var category = colCategory > 0 ? worksheet.Cells[row, colCategory].Text?.Trim() : null;
                    var unit = colUnit > 0 ? worksheet.Cells[row, colUnit].Text?.Trim() : null;
                    var minText = colMinStock > 0 ? worksheet.Cells[row, colMinStock].Text?.Trim() : null;
                    var maxText = colMaxStock > 0 ? worksheet.Cells[row, colMaxStock].Text?.Trim() : null;
                    var reorderText = colReorderQty > 0 ? worksheet.Cells[row, colReorderQty].Text?.Trim() : null;
                    var storage = colStorage > 0 ? worksheet.Cells[row, colStorage].Text?.Trim() : null;
                    var initStockText = colInitialStock > 0 ? worksheet.Cells[row, colInitialStock].Text?.Trim() : null;
                    var costText = colCost > 0 ? worksheet.Cells[row, colCost].Text?.Trim() : null;
                    var supplier = colSupplier > 0 ? worksheet.Cells[row, colSupplier].Text?.Trim() : null;

                    DateTime? expiryDate = null;
                    if (colExpiry > 0)
                    {
                        var expCell = worksheet.Cells[row, colExpiry];
                        if (expCell.Value is DateTime dt)
                        {
                            expiryDate = dt;
                        }
                        else if (double.TryParse(expCell.Text, out var oaDate) && oaDate > 0)
                        {
                            try { expiryDate = DateTime.FromOADate(oaDate); } catch { }
                        }
                        else if (DateTime.TryParse(expCell.Text, out var parsedDate))
                        {
                            expiryDate = parsedDate;
                        }
                    }

                    var uploadItem = new InventoryItemUpload
                    {
                        ItemName = itemName,
                        Category = !string.IsNullOrEmpty(category) ? category : "Other",
                        Unit = !string.IsNullOrEmpty(unit) ? unit : "kg",
                        MinimumStock = decimal.TryParse(minText, out var minS) ? minS : 5,
                        MaximumStock = decimal.TryParse(maxText, out var maxS) ? maxS : 50,
                        ReorderQuantity = decimal.TryParse(reorderText, out var reorderQ) ? reorderQ : 10,
                        StorageLocation = storage,
                        InitialStock = decimal.TryParse(initStockText, out var initS) ? Math.Max(0, initS) : 0,
                        CostPerUnit = decimal.TryParse(costText, out var cpu) ? Math.Max(0, cpu) : 0,
                        SupplierName = supplier,
                        ExpiryDate = expiryDate
                    };

                    items.Add(uploadItem);
                }
            }

            if (items.Count == 0)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "No valid inventory item rows found in Excel sheet" });
                return badReq;
            }

            var result = await _mongoService.BulkUploadInventoryAsync(items, outletId, userId ?? "admin");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(result);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading inventory Excel");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "An internal error occurred while processing the Excel file" });
            return errorResponse;
        }
    }

    private static byte[] ExtractExcelFromMultipart(byte[] data)
    {
        try
        {
            // Look for Excel file signature (PK\x03\x04 for .xlsx files)
            var excelSignature = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
            for (int i = 0; i <= data.Length - 4; i++)
            {
                if (data[i] == excelSignature[0] &&
                    data[i + 1] == excelSignature[1] &&
                    data[i + 2] == excelSignature[2] &&
                    data[i + 3] == excelSignature[3])
                {
                    var excelData = new byte[data.Length - i];
                    Array.Copy(data, i, excelData, 0, excelData.Length);
                    return excelData;
                }
            }
            return data;
        }
        catch
        {
            return data;
        }
    }
}

// Request DTOs
public class StockInRequest
{
    public decimal Quantity { get; set; }
    public decimal? CostPerUnit { get; set; }
    public decimal? PurchasePrice { get; set; }
    public string? SupplierName { get; set; }
    public string? ReferenceNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? PerformedBy { get; set; }
}

public class StockOutRequest
{
    public decimal Quantity { get; set; }
    public string? Reason { get; set; }
    public string? BatchId { get; set; }
    public string? PerformedBy { get; set; }
}

public class StockAdjustmentRequest
{
    public decimal QuantityChange { get; set; }
    public string? Reason { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? PerformedBy { get; set; }
}

public class ResolveAlertRequest
{
    public string? ResolvedBy { get; set; }
}
