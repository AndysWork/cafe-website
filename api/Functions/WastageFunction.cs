using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Repositories;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;

namespace Cafe.Api.Functions;

public class WastageFunction
{
    private readonly IInventoryRepository _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public WastageFunction(IInventoryRepository mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<WastageFunction>();
    }

    [Function("CreateWastageRecord")]
    public async Task<HttpResponseData> CreateWastageRecord(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "wastage")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, role, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var request = await req.ReadFromJsonAsync<CreateWastageRequest>();
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

            var record = new WastageRecord
            {
                OutletId = outletId ?? "default",
                Date = MongoService.GetIstNow(),
                Reason = InputSanitizer.Sanitize(request.Reason),
                Notes = request.Notes != null ? InputSanitizer.Sanitize(request.Notes) : null,
                RecordedBy = userId!,
                Items = request.Items.Select(i => new WastageItem
                {
                    ItemName = InputSanitizer.Sanitize(i.ItemName),
                    Quantity = i.Quantity,
                    Unit = InputSanitizer.Sanitize(i.Unit),
                    CostPerUnit = i.CostPerUnit,
                    TotalCost = i.Quantity * i.CostPerUnit
                }).ToList()
            };
            record.TotalValue = record.Items.Sum(i => i.TotalCost);

            await _mongo.CreateWastageRecordAsync(record);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(record);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating wastage record");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while creating wastage record" });
            return res;
        }
    }

    [Function("GetWastageRecords")]
    public async Task<HttpResponseData> GetWastageRecords(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "wastage")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var startDateStr = req.Query["startDate"];
            var endDateStr = req.Query["endDate"];

            DateTime startDate = DateTime.TryParse(startDateStr, out var sd) ? sd : MongoService.GetIstNow().AddDays(-30);
            DateTime endDate = DateTime.TryParse(endDateStr, out var ed) ? ed : MongoService.GetIstNow();

            var records = await _mongo.GetWastageRecordsAsync(outletId ?? "default", startDate, endDate);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(records);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting wastage records");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while retrieving wastage records" });
            return res;
        }
    }

    [Function("GetWastageSummary")]
    public async Task<HttpResponseData> GetWastageSummary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "wastage/summary")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var startDateStr = req.Query["startDate"];
            var endDateStr = req.Query["endDate"];

            DateTime startDate = DateTime.TryParse(startDateStr, out var sd) ? sd : MongoService.GetIstNow().AddDays(-30);
            DateTime endDate = DateTime.TryParse(endDateStr, out var ed) ? ed : MongoService.GetIstNow();

            var summary = await _mongo.GetWastageSummaryAsync(outletId ?? "default", startDate, endDate);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(summary);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting wastage summary");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while retrieving wastage summary" });
            return res;
        }
    }
}
