using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;
using System.Security.Claims;
using OfficeOpenXml;

namespace Cafe.Api.Functions;

public class SalesFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public SalesFunction(MongoService mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<SalesFunction>();
    }

    // GET: Get all sales records (Admin only)
    [Function("GetAllSales")]
    public async Task<HttpResponseData> GetAllSales(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "sales")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var sales = await _mongo.GetAllSalesAsync();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(sales);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error getting sales records: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to get sales records" });
            return error;
        }
    }

    // GET: Get sales by date range (Admin only)
    [Function("GetSalesByDateRange")]
    public async Task<HttpResponseData> GetSalesByDateRange(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "sales/range")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var startDateStr = query["startDate"];
            var endDateStr = query["endDate"];

            if (!DateTime.TryParse(startDateStr, out var startDate) || 
                !DateTime.TryParse(endDateStr, out var endDate))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid date format" });
                return badRequest;
            }

            var sales = await _mongo.GetSalesByDateRangeAsync(startDate, endDate);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(sales);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error getting sales by date range: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to get sales records" });
            return error;
        }
    }

    // GET: Get sales summary by date (Admin only)
    [Function("GetSalesSummary")]
    public async Task<HttpResponseData> GetSalesSummary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "sales/summary")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var dateStr = query["date"];

            if (!DateTime.TryParse(dateStr, out var date))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid date format" });
                return badRequest;
            }

            var summary = await _mongo.GetSalesSummaryByDateAsync(date);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(summary);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error getting sales summary: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to get sales summary" });
            return error;
        }
    }

    // POST: Create new sales record (Admin only)
    [Function("CreateSales")]
    public async Task<HttpResponseData> CreateSales(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sales")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var salesRequest = await req.ReadFromJsonAsync<CreateSalesRequest>();
            if (salesRequest == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, error = "Invalid sales data" });
                return badRequest;
            }

            // Validate request
            if (!ValidationHelper.TryValidate(salesRequest, out var validationError))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(validationError!.Value);
                return badRequest;
            }

            // Get username for recordedBy
            var user = await _mongo.GetUserByIdAsync(userId!);
            var username = user?.Username ?? "Admin";

            // Calculate total and create sales items
            decimal totalAmount = 0;
            var salesItems = new List<SalesItem>();

            foreach (var item in salesRequest.Items)
            {
                var totalPrice = item.UnitPrice * item.Quantity;
                totalAmount += totalPrice;

                salesItems.Add(new SalesItem
                {
                    MenuItemId = item.MenuItemId,
                    ItemName = item.ItemName,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    TotalPrice = totalPrice
                });
            }

            var sales = new Sales
            {
                Date = salesRequest.Date,
                Items = salesItems,
                TotalAmount = totalAmount,
                PaymentMethod = salesRequest.PaymentMethod,
                Notes = salesRequest.Notes,
                RecordedBy = username,
                CreatedAt = MongoService.GetIstNow(),
                UpdatedAt = MongoService.GetIstNow()
            };

            var createdSales = await _mongo.CreateSalesAsync(sales);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new { success = true, data = createdSales });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error creating sales record: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to create sales record" });
            return error;
        }
    }

    // PUT: Update sales record (Admin only)
    [Function("UpdateSales")]
    public async Task<HttpResponseData> UpdateSales(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "sales/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var salesRequest = await req.ReadFromJsonAsync<CreateSalesRequest>();
            if (salesRequest == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid sales data" });
                return badRequest;
            }

            var existingSales = await _mongo.GetSalesByIdAsync(id);
            if (existingSales == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Sales record not found" });
                return notFound;
            }

            // Calculate total and create sales items
            decimal totalAmount = 0;
            var salesItems = new List<SalesItem>();

            foreach (var item in salesRequest.Items)
            {
                var totalPrice = item.UnitPrice * item.Quantity;
                totalAmount += totalPrice;

                salesItems.Add(new SalesItem
                {
                    MenuItemId = item.MenuItemId,
                    ItemName = item.ItemName,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    TotalPrice = totalPrice
                });
            }

            existingSales.Date = salesRequest.Date;
            existingSales.Items = salesItems;
            existingSales.TotalAmount = totalAmount;
            existingSales.PaymentMethod = salesRequest.PaymentMethod;
            existingSales.Notes = salesRequest.Notes;

            var success = await _mongo.UpdateSalesAsync(id, existingSales);

            if (!success)
            {
                var error = req.CreateResponse(HttpStatusCode.InternalServerError);
                await error.WriteAsJsonAsync(new { error = "Failed to update sales record" });
                return error;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(existingSales);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error updating sales record: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to update sales record" });
            return error;
        }
    }

    // DELETE: Delete sales record (Admin only)
    [Function("DeleteSales")]
    public async Task<HttpResponseData> DeleteSales(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "sales/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var success = await _mongo.DeleteSalesAsync(id);

            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Sales record not found" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Sales record deleted successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error deleting sales record: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to delete sales record" });
            return error;
        }
    }

    // POST: Upload sales from Excel (Admin only)
    [Function("UploadSalesExcel")]
    public async Task<HttpResponseData> UploadSalesExcel(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sales/upload")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            // Get username for recordedBy
            var user = await _mongo.GetUserByIdAsync(userId!);
            var username = user?.Username ?? "Admin";

            // Set EPPlus license context
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            // Read the file
            byte[] fileBytes;
            using (var memoryStream = new MemoryStream())
            {
                await req.Body.CopyToAsync(memoryStream);
                fileBytes = memoryStream.ToArray();
            }

            if (fileBytes == null || fileBytes.Length == 0)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "No file data received" });
                return badRequest;
            }

            var result = await ProcessSalesExcel(fileBytes, username);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(result);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error uploading sales Excel file");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
            return errorResponse;
        }
    }

    private async Task<object> ProcessSalesExcel(byte[] fileBytes, string recordedBy)
    {
        using var package = new ExcelPackage(new MemoryStream(fileBytes));
        var worksheet = package.Workbook.Worksheets.FirstOrDefault();

        if (worksheet == null)
            throw new Exception("No worksheet found in Excel file");

        var rowCount = worksheet.Dimension?.Rows ?? 0;
        if (rowCount < 2)
            throw new Exception("Excel file is empty or has no data rows");

        var salesRecords = new List<Sales>();
        var currentDate = DateTime.MinValue;
        var currentItems = new List<SalesItem>();
        var currentPaymentMethod = "Cash";

        for (int row = 2; row <= rowCount; row++)
        {
            var dateValue = worksheet.Cells[row, 1].Value?.ToString();
            var itemName = worksheet.Cells[row, 2].Value?.ToString();
            var quantityStr = worksheet.Cells[row, 3].Value?.ToString();
            var priceStr = worksheet.Cells[row, 4].Value?.ToString();
            var totalSaleStr = worksheet.Cells[row, 5].Value?.ToString();
            var paymentMethod = worksheet.Cells[row, 6].Value?.ToString() ?? "Cash";

            // Check if this is a new date (new sales record)
            if (!string.IsNullOrEmpty(dateValue) && DateTime.TryParse(dateValue, out var parsedDate))
            {
                // Save previous record if exists
                if (currentDate != DateTime.MinValue && currentItems.Any())
                {
                    salesRecords.Add(new Sales
                    {
                        Date = currentDate,
                        Items = new List<SalesItem>(currentItems),
                        TotalAmount = currentItems.Sum(i => i.TotalPrice),
                        PaymentMethod = currentPaymentMethod,
                        RecordedBy = recordedBy,
                        CreatedAt = MongoService.GetIstNow(),
                        UpdatedAt = MongoService.GetIstNow()
                    });
                }

                // Start new record
                currentDate = parsedDate;
                currentItems.Clear();
                currentPaymentMethod = paymentMethod;
            }

            // Add item to current record
            if (!string.IsNullOrEmpty(itemName) && 
                int.TryParse(quantityStr, out var quantity) && 
                decimal.TryParse(priceStr, out var price))
            {
                // Use provided TotalSale if available, otherwise calculate
                decimal totalPrice = quantity * price;
                if (!string.IsNullOrEmpty(totalSaleStr) && decimal.TryParse(totalSaleStr, out var parsedTotal))
                {
                    totalPrice = parsedTotal;
                }

                currentItems.Add(new SalesItem
                {
                    ItemName = itemName,
                    Quantity = quantity,
                    UnitPrice = price,
                    TotalPrice = totalPrice
                });
            }
        }

        // Add last record
        if (currentDate != DateTime.MinValue && currentItems.Any())
        {
            salesRecords.Add(new Sales
            {
                Date = currentDate,
                Items = new List<SalesItem>(currentItems),
                TotalAmount = currentItems.Sum(i => i.TotalPrice),
                PaymentMethod = currentPaymentMethod,
                RecordedBy = recordedBy,
                CreatedAt = MongoService.GetIstNow(),
                UpdatedAt = MongoService.GetIstNow()
            });
        }

        // Insert all records
        foreach (var sales in salesRecords)
        {
            await _mongo.CreateSalesAsync(sales);
        }

        return new
        {
            message = $"Successfully uploaded {salesRecords.Count} sales records",
            processedRecords = salesRecords.Count,
            totalItems = salesRecords.Sum(s => s.Items.Count),
            totalAmount = salesRecords.Sum(s => s.TotalAmount)
        };
    }
}
