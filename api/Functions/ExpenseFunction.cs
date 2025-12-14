using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;
using OfficeOpenXml;

namespace Cafe.Api.Functions;

public class ExpenseFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public ExpenseFunction(MongoService mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<ExpenseFunction>();
    }

    // GET: Get all expenses (Admin only)
    [Function("GetAllExpenses")]
    public async Task<HttpResponseData> GetAllExpenses(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "expenses")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var expenses = await _mongo.GetAllExpensesAsync();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(expenses);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error getting expenses: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to get expenses" });
            return error;
        }
    }

    // GET: Get expenses by date range (Admin only)
    [Function("GetExpensesByDateRange")]
    public async Task<HttpResponseData> GetExpensesByDateRange(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "expenses/range")] HttpRequestData req)
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

            var expenses = await _mongo.GetExpensesByDateRangeAsync(startDate, endDate);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(expenses);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error getting expenses by date range: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to get expenses" });
            return error;
        }
    }

    // GET: Get expense summary by date (Admin only)
    [Function("GetExpenseSummary")]
    public async Task<HttpResponseData> GetExpenseSummary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "expenses/summary")] HttpRequestData req)
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

            var summary = await _mongo.GetExpenseSummaryByDateAsync(date);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(summary);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error getting expense summary: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to get expense summary" });
            return error;
        }
    }

    // POST: Create new expense (Admin only)
    [Function("CreateExpense")]
    public async Task<HttpResponseData> CreateExpense(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "expenses")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var expenseRequest = await req.ReadFromJsonAsync<CreateExpenseRequest>();
            if (expenseRequest == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid expense data" });
                return badRequest;
            }

            // Get username for recordedBy
            var user = await _mongo.GetUserByIdAsync(userId!);
            var username = user?.Username ?? "Admin";

            var expense = new Expense
            {
                Date = expenseRequest.Date,
                ExpenseType = expenseRequest.ExpenseType,
                Description = expenseRequest.Description,
                Amount = expenseRequest.Amount,
                Vendor = expenseRequest.Vendor,
                PaymentMethod = expenseRequest.PaymentMethod,
                InvoiceNumber = expenseRequest.InvoiceNumber,
                Notes = expenseRequest.Notes,
                RecordedBy = username,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var createdExpense = await _mongo.CreateExpenseAsync(expense);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(createdExpense);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error creating expense: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to create expense" });
            return error;
        }
    }

    // PUT: Update expense (Admin only)
    [Function("UpdateExpense")]
    public async Task<HttpResponseData> UpdateExpense(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "expenses/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var expenseRequest = await req.ReadFromJsonAsync<CreateExpenseRequest>();
            if (expenseRequest == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid expense data" });
                return badRequest;
            }

            var existingExpense = await _mongo.GetExpenseByIdAsync(id);
            if (existingExpense == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Expense not found" });
                return notFound;
            }

            existingExpense.Date = expenseRequest.Date;
            existingExpense.ExpenseType = expenseRequest.ExpenseType;
            existingExpense.Description = expenseRequest.Description;
            existingExpense.Amount = expenseRequest.Amount;
            existingExpense.Vendor = expenseRequest.Vendor;
            existingExpense.PaymentMethod = expenseRequest.PaymentMethod;
            existingExpense.InvoiceNumber = expenseRequest.InvoiceNumber;
            existingExpense.Notes = expenseRequest.Notes;

            var success = await _mongo.UpdateExpenseAsync(id, existingExpense);

            if (!success)
            {
                var error = req.CreateResponse(HttpStatusCode.InternalServerError);
                await error.WriteAsJsonAsync(new { error = "Failed to update expense" });
                return error;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(existingExpense);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error updating expense: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to update expense" });
            return error;
        }
    }

    // DELETE: Delete expense (Admin only)
    [Function("DeleteExpense")]
    public async Task<HttpResponseData> DeleteExpense(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "expenses/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var success = await _mongo.DeleteExpenseAsync(id);

            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Expense not found" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Expense deleted successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error deleting expense: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to delete expense" });
            return error;
        }
    }

    // POST: Upload expenses from Excel (Admin only)
    [Function("UploadExpensesExcel")]
    public async Task<HttpResponseData> UploadExpensesExcel(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "expenses/upload")] HttpRequestData req)
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

            var result = await ProcessExpensesExcel(fileBytes, username);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(result);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error uploading expenses Excel file");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
            return errorResponse;
        }
    }

    private async Task<object> ProcessExpensesExcel(byte[] fileBytes, string recordedBy)
    {
        using var package = new ExcelPackage(new MemoryStream(fileBytes));
        var worksheet = package.Workbook.Worksheets.FirstOrDefault();

        if (worksheet == null)
            throw new Exception("No worksheet found in Excel file");

        var rowCount = worksheet.Dimension?.Rows ?? 0;
        if (rowCount < 2)
            throw new Exception("Excel file is empty or has no data rows");

        var expenses = new List<Expense>();

        // Expected columns: Date, ExpenseType, Description, Amount, Vendor, PaymentMethod, InvoiceNumber, Notes
        for (int row = 2; row <= rowCount; row++)
        {
            var dateValue = worksheet.Cells[row, 1].Value?.ToString();
            var expenseType = worksheet.Cells[row, 2].Value?.ToString();
            var description = worksheet.Cells[row, 3].Value?.ToString();
            var amountStr = worksheet.Cells[row, 4].Value?.ToString();
            var vendor = worksheet.Cells[row, 5].Value?.ToString();
            var paymentMethod = worksheet.Cells[row, 6].Value?.ToString() ?? "Cash";
            var invoiceNumber = worksheet.Cells[row, 7].Value?.ToString();
            var notes = worksheet.Cells[row, 8].Value?.ToString();

            if (string.IsNullOrEmpty(dateValue) || string.IsNullOrEmpty(expenseType) || 
                string.IsNullOrEmpty(description) || string.IsNullOrEmpty(amountStr))
                continue;

            if (!DateTime.TryParse(dateValue, out var date))
                continue;

            if (!decimal.TryParse(amountStr, out var amount))
                continue;

            expenses.Add(new Expense
            {
                Date = date,
                ExpenseType = expenseType,
                Description = description,
                Amount = amount,
                Vendor = vendor,
                PaymentMethod = paymentMethod,
                InvoiceNumber = invoiceNumber,
                Notes = notes,
                RecordedBy = recordedBy,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        // Insert all expenses
        foreach (var expense in expenses)
        {
            await _mongo.CreateExpenseAsync(expense);
        }

        return new
        {
            message = $"Successfully uploaded {expenses.Count} expense records",
            processedRecords = expenses.Count,
            totalAmount = expenses.Sum(e => e.Amount)
        };
    }
}
