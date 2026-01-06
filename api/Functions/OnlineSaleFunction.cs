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

    // GET /api/online-sales/discount-coupons - Get unique discount coupons by platform
    [Function("GetDiscountCoupons")]
    public async Task<HttpResponseData> GetDiscountCoupons(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "online-sales/discount-coupons")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var coupons = await _mongo.GetUniqueDiscountCouponsAsync();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = coupons });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting discount coupons");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { success = false, message = ex.Message });
            return errorResponse;
        }
    }

    // PUT /api/online-sales/discount-coupons/{couponCode}/{platform}/status - Toggle coupon active status
    [Function("UpdateDiscountCouponStatus")]
    public async Task<HttpResponseData> UpdateDiscountCouponStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "online-sales/discount-coupons/{couponCode}/{platform}/status")] HttpRequestData req,
        string couponCode,
        string platform)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var requestBody = await req.ReadFromJsonAsync<UpdateCouponStatusRequest>();
            if (requestBody == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, error = "Invalid request body" });
                return badRequest;
            }

            // Create or update the coupon status
            var updatedCoupon = await _mongo.CreateOrUpdateDiscountCouponAsync(
                couponCode, 
                platform, 
                requestBody.IsActive, 
                userId!
            );

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { 
                success = true, 
                message = $"Coupon '{couponCode}' for {platform} is now {(requestBody.IsActive ? "active" : "inactive")}",
                data = updatedCoupon
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating discount coupon status");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { success = false, message = ex.Message });
            return errorResponse;
        }
    }

    // GET /api/online-sales/discount-coupons/active - Get only active coupons
    [Function("GetActiveDiscountCoupons")]
    public async Task<HttpResponseData> GetActiveDiscountCoupons(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "online-sales/discount-coupons/active")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var coupons = await _mongo.GetActiveDiscountCouponsAsync();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = coupons });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting active discount coupons");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { success = false, message = ex.Message });
            return errorResponse;
        }
    }

    // PUT /api/online-sales/discount-coupons/{id}/max-value - Update coupon max value
    [Function("UpdateDiscountCouponMaxValue")]
    public async Task<HttpResponseData> UpdateDiscountCouponMaxValue(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "online-sales/discount-coupons/{id}/max-value")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var requestBody = await req.ReadFromJsonAsync<UpdateCouponMaxValueRequest>();
            if (requestBody == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, error = "Invalid request body" });
                return badRequest;
            }

            var updated = await _mongo.UpdateDiscountCouponMaxValueAsync(id, requestBody.MaxValue);
            
            if (!updated)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { success = false, error = "Coupon not found" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { 
                success = true, 
                message = $"Coupon max value updated to {requestBody.MaxValue?.ToString() ?? "unlimited"}"
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating discount coupon max value");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { success = false, message = ex.Message });
            return errorResponse;
        }
    }

    // PUT /api/online-sales/discount-coupons/{id}/discount-percentage - Update coupon discount percentage
    [Function("UpdateDiscountCouponPercentage")]
    public async Task<HttpResponseData> UpdateDiscountCouponPercentage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "online-sales/discount-coupons/{id}/discount-percentage")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var requestBody = await req.ReadFromJsonAsync<UpdateCouponDiscountPercentageRequest>();
            if (requestBody == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, error = "Invalid request body" });
                return badRequest;
            }

            var updated = await _mongo.UpdateDiscountCouponPercentageAsync(id, requestBody.DiscountPercentage);
            
            if (!updated)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { success = false, error = "Coupon not found" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { 
                success = true, 
                message = $"Coupon discount percentage updated to {requestBody.DiscountPercentage?.ToString() ?? "unset"}%"
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating discount coupon percentage");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { success = false, message = ex.Message });
            return errorResponse;
        }
    }

    // GET /api/online-sales/kpt-analysis - Get KPT analysis by menu items
    [Function("GetKptAnalysis")]
    public async Task<HttpResponseData> GetKptAnalysis(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "online-sales/kpt-analysis")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var platform = query["platform"]; // Optional: "Zomato" or "Swiggy"
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

            // Get sales with KPT data
            var sales = await _mongo.GetOnlineSalesAsync(platform);
            
            // Filter by date range if provided
            if (startDate.HasValue)
            {
                sales = sales.Where(s => s.OrderAt >= startDate.Value).ToList();
            }
            if (endDate.HasValue)
            {
                // Add one day to include the end date
                sales = sales.Where(s => s.OrderAt < endDate.Value.AddDays(1)).ToList();
            }

            // Filter only sales with KPT data
            var salesWithKpt = sales.Where(s => s.KPT.HasValue && s.KPT.Value > 0).ToList();

            // Group by menu items and calculate statistics
            var menuItemStats = new Dictionary<string, MenuItemKptStats>();

            foreach (var sale in salesWithKpt)
            {
                // Calculate total items in this order (sum of all quantities)
                var totalItemsInOrder = sale.OrderedItems.Sum(i => i.Quantity);
                
                // Calculate per-item KPT (divide total KPT by number of items)
                var perItemKpt = totalItemsInOrder > 0 
                    ? sale.KPT.Value / totalItemsInOrder 
                    : sale.KPT.Value;

                foreach (var item in sale.OrderedItems)
                {
                    var itemName = item.ItemName?.Trim() ?? "Unknown Item";

                    if (!menuItemStats.ContainsKey(itemName))
                    {
                        menuItemStats[itemName] = new MenuItemKptStats
                        {
                            ItemName = itemName,
                            PreparationTimes = new List<decimal>(),
                            OrderCount = 0,
                            TotalQuantity = 0,
                            MenuItemId = item.MenuItemId
                        };
                    }

                    // Add the proportional KPT for this item (per-item KPT × quantity)
                    // This represents this item's contribution to the total preparation time
                    var itemKpt = perItemKpt * item.Quantity;
                    menuItemStats[itemName].PreparationTimes.Add(itemKpt);
                    menuItemStats[itemName].OrderCount++;
                    menuItemStats[itemName].TotalQuantity += item.Quantity;
                }
            }

            // Calculate statistics for each menu item
            var results = menuItemStats.Values.Select(stats =>
            {
                var times = stats.PreparationTimes.OrderBy(x => x).ToList();
                var count = times.Count;

                return new
                {
                    itemName = stats.ItemName,
                    menuItemId = stats.MenuItemId,
                    orderCount = stats.OrderCount,
                    totalQuantity = stats.TotalQuantity,
                    avgPreparationTime = times.Average(),
                    minPreparationTime = times.Min(),
                    maxPreparationTime = times.Max(),
                    medianPreparationTime = count % 2 == 0 
                        ? (times[count / 2 - 1] + times[count / 2]) / 2 
                        : times[count / 2],
                    // Standard deviation calculation
                    stdDeviation = Math.Sqrt(times.Average(t => Math.Pow((double)(t - times.Average()), 2))),
                    preparationTimeRange = $"{times.Min():F1} - {times.Max():F1} min"
                };
            })
            .OrderByDescending(x => x.orderCount)
            .ToList();

            var summary = new
            {
                totalOrdersAnalyzed = salesWithKpt.Count,
                totalMenuItems = results.Count,
                dateRange = new
                {
                    start = startDate?.ToString("yyyy-MM-dd") ?? salesWithKpt.MinBy(s => s.OrderAt)?.OrderAt.ToString("yyyy-MM-dd"),
                    end = endDate?.ToString("yyyy-MM-dd") ?? salesWithKpt.MaxBy(s => s.OrderAt)?.OrderAt.ToString("yyyy-MM-dd")
                },
                platform = string.IsNullOrEmpty(platform) ? "All Platforms" : platform,
                averageKptAllOrders = salesWithKpt.Average(s => s.KPT!.Value),
                minKptAllOrders = salesWithKpt.Min(s => s.KPT!.Value),
                maxKptAllOrders = salesWithKpt.Max(s => s.KPT!.Value)
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new 
            { 
                success = true, 
                summary,
                menuItems = results 
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error analyzing KPT data");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { success = false, message = ex.Message });
            return errorResponse;
        }
    }
}

// Helper class for KPT statistics
internal class MenuItemKptStats
{
    public string ItemName { get; set; } = string.Empty;
    public string? MenuItemId { get; set; }
    public List<decimal> PreparationTimes { get; set; } = new();
    public int OrderCount { get; set; }
    public int TotalQuantity { get; set; }
}