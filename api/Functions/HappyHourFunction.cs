using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;

namespace Cafe.Api.Functions;

public class HappyHourFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public HappyHourFunction(MongoService mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<HappyHourFunction>();
    }

    [Function("GetActiveHappyHours")]
    public async Task<HttpResponseData> GetActiveHappyHours(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "happy-hours/active")] HttpRequestData req)
    {
        try
        {
            var outletId = req.Query["outletId"] ?? "default";
            var active = await _mongo.GetActiveHappyHoursAsync(outletId);

            var responses = active.Select(h => new ActiveHappyHourResponse
            {
                Id = h.Id!,
                Name = h.Name,
                DiscountType = h.DiscountType,
                DiscountValue = h.DiscountValue,
                MaxDiscount = h.MaxDiscount,
                ApplicableCategories = h.ApplicableCategories,
                ApplicableItems = h.ApplicableItems,
                EndTime = h.EndTime
            }).ToList();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(responses);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting active happy hours");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("GetAllHappyHours")]
    public async Task<HttpResponseData> GetAllHappyHours(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/happy-hours")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var rules = await _mongo.GetHappyHourRulesAsync(outletId ?? "default");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(rules);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting happy hour rules");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("CreateHappyHour")]
    public async Task<HttpResponseData> CreateHappyHour(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/happy-hours")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var request = await req.ReadFromJsonAsync<CreateHappyHourRequest>();
            if (request == null)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Invalid request body" });
                return badReq;
            }

            if (!ValidationHelper.TryValidate(request, out var validationError))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(validationError!.Value);
                return badReq;
            }

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);

            var rule = new HappyHourRule
            {
                OutletId = outletId ?? "default",
                Name = InputSanitizer.Sanitize(request.Name),
                StartTime = request.StartTime,
                EndTime = request.EndTime,
                DaysOfWeek = request.DaysOfWeek,
                DiscountType = InputSanitizer.Sanitize(request.DiscountType),
                DiscountValue = request.DiscountValue,
                MaxDiscount = request.MaxDiscount,
                ApplicableCategories = request.ApplicableCategories?.Select(InputSanitizer.Sanitize).ToList(),
                ApplicableItems = request.ApplicableItems?.Select(InputSanitizer.Sanitize).ToList(),
                IsActive = true,
                CreatedAt = MongoService.GetIstNow()
            };

            await _mongo.CreateHappyHourRuleAsync(rule);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(rule);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating happy hour rule");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("UpdateHappyHour")]
    public async Task<HttpResponseData> UpdateHappyHour(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "manage/happy-hours/{id}")] HttpRequestData req, string id)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var request = await req.ReadFromJsonAsync<CreateHappyHourRequest>();
            if (request == null)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Invalid request body" });
                return badReq;
            }

            var existing = await _mongo.GetHappyHourRuleByIdAsync(id);
            if (existing == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Happy hour rule not found" });
                return notFound;
            }

            existing.Name = InputSanitizer.Sanitize(request.Name);
            existing.StartTime = request.StartTime;
            existing.EndTime = request.EndTime;
            existing.DaysOfWeek = request.DaysOfWeek;
            existing.DiscountType = InputSanitizer.Sanitize(request.DiscountType);
            existing.DiscountValue = request.DiscountValue;
            existing.MaxDiscount = request.MaxDiscount;
            existing.ApplicableCategories = request.ApplicableCategories?.Select(InputSanitizer.Sanitize).ToList();
            existing.ApplicableItems = request.ApplicableItems?.Select(InputSanitizer.Sanitize).ToList();

            await _mongo.UpdateHappyHourRuleAsync(id, existing);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(existing);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating happy hour rule");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("DeleteHappyHour")]
    public async Task<HttpResponseData> DeleteHappyHour(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "manage/happy-hours/{id}")] HttpRequestData req, string id)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            await _mongo.DeleteHappyHourRuleAsync(id);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Happy hour rule deleted" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error deleting happy hour rule");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }
}
