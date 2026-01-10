using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;

namespace Cafe.Api.Functions;

public class OperationalExpenseFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public OperationalExpenseFunction(MongoService mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<OperationalExpenseFunction>();
    }

    // GET: Get all operational expenses (Admin only)
    [Function("GetAllOperationalExpenses")]
    public async Task<HttpResponseData> GetAllOperationalExpenses(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "operational-expenses")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var expenses = await _mongo.GetAllOperationalExpensesAsync(outletId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(expenses);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error getting operational expenses: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to get operational expenses" });
            return error;
        }
    }

    // GET: Get operational expenses by year (Admin only)
    [Function("GetOperationalExpensesByYear")]
    public async Task<HttpResponseData> GetOperationalExpensesByYear(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "operational-expenses/year/{year}")] HttpRequestData req, int year)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var expenses = await _mongo.GetOperationalExpensesByYearAsync(year);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(expenses);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error getting operational expenses for year {year}: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to get operational expenses" });
            return error;
        }
    }

    // GET: Get operational expense by month and year (Admin only)
    [Function("GetOperationalExpenseByMonthYear")]
    public async Task<HttpResponseData> GetOperationalExpenseByMonthYear(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "operational-expenses/{year}/{month}")] HttpRequestData req, int year, int month)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var expense = await _mongo.GetOperationalExpenseByMonthYearAsync(month, year);
            
            if (expense == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Operational expense not found for this month" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(expense);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error getting operational expense for {month}/{year}: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to get operational expense" });
            return error;
        }
    }

    // GET: Calculate rent for a month from offline expenses (Admin only)
    [Function("CalculateRentForMonth")]
    public async Task<HttpResponseData> CalculateRentForMonth(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "operational-expenses/calculate-rent/{year}/{month}")] HttpRequestData req, int year, int month)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var rentAmount = await _mongo.CalculateRentForMonthAsync(month, year);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { rent = rentAmount });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error calculating rent for {month}/{year}: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to calculate rent" });
            return error;
        }
    }

    // POST: Create new operational expense (Admin only)
    [Function("CreateOperationalExpense")]
    public async Task<HttpResponseData> CreateOperationalExpense(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "operational-expenses")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, username, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var requestBody = await req.ReadFromJsonAsync<CreateOperationalExpenseRequest>();
            if (requestBody == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid request body" });
                return badRequest;
            }

            // Validate outlet access
            var (hasAccess, outletId, accessError) = await OutletHelper.ValidateOutletAccess(req, _auth, _mongo);
            if (!hasAccess)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = accessError });
                return forbidden;
            }

            // Check if operational expense already exists for this month/year
            var existing = await _mongo.GetOperationalExpenseByMonthYearAsync(requestBody.Month, requestBody.Year);
            if (existing != null)
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = $"Operational expense already exists for {requestBody.Month}/{requestBody.Year}" });
                return conflict;
            }

            var expense = new OperationalExpense
            {
                OutletId = outletId,
                Month = requestBody.Month,
                Year = requestBody.Year,
                CookSalary = requestBody.CookSalary,
                HelperSalary = requestBody.HelperSalary,
                Electricity = requestBody.Electricity,
                MachineMaintenance = requestBody.MachineMaintenance,
                Misc = requestBody.Misc,
                Notes = requestBody.Notes,
                RecordedBy = username ?? "Unknown",
                TransactionType = "Cash" // Always cash for operational expenses
            };

            var created = await _mongo.CreateOperationalExpenseAsync(expense);
            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(created);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error creating operational expense: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to create operational expense" });
            return error;
        }
    }

    // PUT: Update operational expense (Admin only)
    [Function("UpdateOperationalExpense")]
    public async Task<HttpResponseData> UpdateOperationalExpense(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "operational-expenses/{id}")] HttpRequestData req, string id)
    {
        try
        {
            var (isAuthorized, _, username, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var requestBody = await req.ReadFromJsonAsync<UpdateOperationalExpenseRequest>();
            if (requestBody == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid request body" });
                return badRequest;
            }

            var existing = await _mongo.GetOperationalExpenseByIdAsync(id);
            if (existing == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Operational expense not found" });
                return notFound;
            }

            // Update fields
            existing.CookSalary = requestBody.CookSalary;
            existing.HelperSalary = requestBody.HelperSalary;
            existing.Electricity = requestBody.Electricity;
            existing.MachineMaintenance = requestBody.MachineMaintenance;
            existing.Misc = requestBody.Misc;
            existing.Notes = requestBody.Notes;

            var success = await _mongo.UpdateOperationalExpenseAsync(id, existing);
            
            if (!success)
            {
                var error = req.CreateResponse(HttpStatusCode.InternalServerError);
                await error.WriteAsJsonAsync(new { error = "Failed to update operational expense" });
                return error;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(existing);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error updating operational expense: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to update operational expense" });
            return error;
        }
    }

    // DELETE: Delete operational expense (Admin only)
    [Function("DeleteOperationalExpense")]
    public async Task<HttpResponseData> DeleteOperationalExpense(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "operational-expenses/{id}")] HttpRequestData req, string id)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var success = await _mongo.DeleteOperationalExpenseAsync(id);
            
            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Operational expense not found" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Operational expense deleted successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error deleting operational expense: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to delete operational expense" });
            return error;
        }
    }
}
