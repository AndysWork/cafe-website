using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using Cafe.Api.Models;
using Cafe.Api.Services;
using Cafe.Api.Helpers;
using System.Text.Json;

namespace Cafe.Api.Functions;

public class OnlineSaleFunction
{
    private readonly ILogger<OnlineSaleFunction> _log;
    private readonly MongoService _mongo;
    private readonly AuthService _auth;

    public OnlineSaleFunction(ILogger<OnlineSaleFunction> log, MongoService mongo, AuthService auth)
    {
        _log = log;
        _mongo = mongo;
        _auth = auth;
    }

    // GET /api/online-sales - Get all online sales with optional platform filter
    [Function("GetOnlineSales")]
    public async Task<HttpResponseData> GetOnlineSales(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "online-sales")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var platform = query["platform"]; // Optional: "Zomato" or "Swiggy"

            var sales = await _mongo.GetOnlineSalesAsync(platform);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = sales });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting online sales");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { success = false, message = ex.Message });
            return errorResponse;
        }
    }

    // GET /api/online-sales/date-range - Get sales in date range
    [Function("GetOnlineSalesByDateRange")]
    public async Task<HttpResponseData> GetOnlineSalesByDateRange(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "online-sales/date-range")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var platform = query["platform"];
            var startDateStr = query["startDate"];
            var endDateStr = query["endDate"];

            if (string.IsNullOrEmpty(startDateStr) || string.IsNullOrEmpty(endDateStr))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, message = "startDate and endDate are required" });
                return badRequest;
            }

            if (!DateTime.TryParse(startDateStr, out var startDate))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, message = $"Invalid startDate format: {startDateStr}" });
                return badRequest;
            }

            if (!DateTime.TryParse(endDateStr, out var endDate))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, message = $"Invalid endDate format: {endDateStr}" });
                return badRequest;
            }

            var sales = await _mongo.GetOnlineSalesByDateRangeAsync(platform, startDate, endDate);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = sales });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting online sales by date range");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { success = false, message = ex.Message });
            return errorResponse;
        }
    }

    // GET /api/online-sales/daily-income - Get daily income grouped by date and platform
    [Function("GetDailyOnlineIncome")]
    public async Task<HttpResponseData> GetDailyOnlineIncome(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "online-sales/daily-income")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var startDateStr = query["startDate"];
            var endDateStr = query["endDate"];

            if (string.IsNullOrEmpty(startDateStr) || string.IsNullOrEmpty(endDateStr))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, message = "startDate and endDate are required" });
                return badRequest;
            }

            if (!DateTime.TryParse(startDateStr, out var startDate))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, message = $"Invalid startDate format: {startDateStr}" });
                return badRequest;
            }

            if (!DateTime.TryParse(endDateStr, out var endDate))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, message = $"Invalid endDate format: {endDateStr}" });
                return badRequest;
            }

            var dailyIncome = await _mongo.GetDailyOnlineIncomeAsync(startDate, endDate);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = dailyIncome });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting daily online income");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { success = false, message = ex.Message });
            return errorResponse;
        }
    }

    // GET /api/online-sales/id/{id} - Get single sale
    [Function("GetOnlineSaleById")]
    public async Task<HttpResponseData> GetOnlineSaleById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "online-sales/id/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            // Validate that id is a valid MongoDB ObjectId (24-character hex string)
            if (string.IsNullOrEmpty(id) || id.Length != 24 || !System.Text.RegularExpressions.Regex.IsMatch(id, "^[0-9a-fA-F]{24}$"))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, message = "Invalid ID format. Expected a 24-character hexadecimal string." });
                return badRequest;
            }

            var sale = await _mongo.GetOnlineSaleByIdAsync(id);

            if (sale == null)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteAsJsonAsync(new { success = false, message = "Online sale not found" });
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = sale });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting online sale by ID");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { success = false, message = ex.Message });
            return errorResponse;
        }
    }

    // POST /api/online-sales - Create new online sale
    [Function("CreateOnlineSale")]
    public async Task<HttpResponseData> CreateOnlineSale(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "online-sales")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var request = JsonSerializer.Deserialize<CreateOnlineSaleRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, message = "Invalid request body" });
                return badRequest;
            }

            var validationErrors = ValidationHelper.ValidateModel(request);
            if (validationErrors.Any())
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, message = "Validation failed", errors = validationErrors });
                return badRequest;
            }

            var sale = new OnlineSale
            {
                Platform = request.Platform,
                OrderId = request.OrderId,
                CustomerName = request.CustomerName,
                OrderAt = request.OrderAt,
                Distance = request.Distance,
                OrderedItems = request.OrderedItems.Select(i => new OrderedItem
                {
                    Quantity = i.Quantity,
                    ItemName = i.ItemName,
                    MenuItemId = i.MenuItemId
                }).ToList(),
                Instructions = request.Instructions,
                DiscountCoupon = request.DiscountCoupon,
                BillSubTotal = request.BillSubTotal,
                PackagingCharges = request.PackagingCharges,
                DiscountAmount = request.DiscountAmount,
                TotalCommissionable = request.TotalCommissionable,
                Payout = request.Payout,
                PlatformDeduction = request.PlatformDeduction,
                Investment = request.Investment,
                MiscCharges = request.MiscCharges,
                Rating = request.Rating,
                Review = request.Review,
                KPT = request.KPT,
                RWT = request.RWT,
                OrderMarking = request.OrderMarking,
                Complain = request.Complain
            };

            var created = await _mongo.CreateOnlineSaleAsync(sale, userId!);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new { success = true, data = created });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating online sale");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { success = false, message = ex.Message });
            return errorResponse;
        }
    }

    // PUT /api/online-sales/id/{id} - Update online sale
    [Function("UpdateOnlineSale")]
    public async Task<HttpResponseData> UpdateOnlineSale(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "online-sales/id/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var request = JsonSerializer.Deserialize<UpdateOnlineSaleRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, message = "Invalid request body" });
                return badRequest;
            }

            var updated = await _mongo.UpdateOnlineSaleAsync(id, request);

            if (updated == null)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteAsJsonAsync(new { success = false, message = "Online sale not found" });
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = updated });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating online sale");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { success = false, message = ex.Message });
            return errorResponse;
        }
    }

    // DELETE /api/online-sales/id/{id} - Delete online sale
    [Function("DeleteOnlineSale")]
    public async Task<HttpResponseData> DeleteOnlineSale(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "online-sales/id/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var deleted = await _mongo.DeleteOnlineSaleAsync(id);

            if (!deleted)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteAsJsonAsync(new { success = false, message = "Online sale not found" });
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, message = "Online sale deleted successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error deleting online sale");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { success = false, message = ex.Message });
            return errorResponse;
        }
    }

    // DELETE /api/online-sales/bulk - Bulk delete online sales by criteria
    [Function("BulkDeleteOnlineSales")]
    public async Task<HttpResponseData> BulkDeleteOnlineSales(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "online-sales/bulk")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var platform = query["platform"];
            var startDateStr = query["startDate"];
            var endDateStr = query["endDate"];

            DateTime? startDate = null;
            DateTime? endDate = null;

            if (!string.IsNullOrEmpty(startDateStr) && DateTime.TryParse(startDateStr, out var parsedStart))
            {
                startDate = parsedStart;
            }

            if (!string.IsNullOrEmpty(endDateStr) && DateTime.TryParse(endDateStr, out var parsedEnd))
            {
                endDate = parsedEnd;
            }

            var deletedCount = await _mongo.BulkDeleteOnlineSalesAsync(platform, startDate, endDate);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new 
            { 
                success = true, 
                message = $"Successfully deleted {deletedCount} online sale(s)",
                deletedCount = deletedCount
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error bulk deleting online sales");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { success = false, message = ex.Message });
            return errorResponse;
        }
    }

    // GET /api/online-sales/reviews/five-star - Get 5-star reviews for landing page
    [Function("GetFiveStarReviews")]
    public async Task<HttpResponseData> GetFiveStarReviews(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "online-sales/reviews/five-star")] HttpRequestData req)
    {
        try
        {
            // No authorization required - public endpoint for landing page
            
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var limitStr = query["limit"] ?? "10";
            var limit = int.TryParse(limitStr, out var l) ? l : 10;

            var reviews = await _mongo.GetFiveStarReviewsAsync(limit);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = reviews });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting five star reviews");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { success = false, message = ex.Message });
            return errorResponse;
        }
    }
}




