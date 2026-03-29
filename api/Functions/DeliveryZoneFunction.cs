using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;

namespace Cafe.Api.Functions;

public class DeliveryZoneFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public DeliveryZoneFunction(MongoService mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<DeliveryZoneFunction>();
    }

    [Function("GetDeliveryZones")]
    public async Task<HttpResponseData> GetDeliveryZones(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "delivery-zones")] HttpRequestData req)
    {
        try
        {
            var (hasAccess, outletId, accessError) = await OutletHelper.ValidateOutletAccess(req, _auth, _mongo);
            if (!hasAccess)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = accessError });
                return forbidden;
            }

            var zones = await _mongo.GetDeliveryZonesAsync(outletId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(zones);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting delivery zones");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while retrieving delivery zones" });
            return res;
        }
    }

    [Function("CreateDeliveryZone")]
    public async Task<HttpResponseData> CreateDeliveryZone(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "delivery-zones")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);

            var request = await req.ReadFromJsonAsync<CreateDeliveryZoneRequest>();
            if (request == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid request" });
                return badRequest;
            }

            if (!ValidationHelper.TryValidate(request, out var validationError))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(validationError!.Value);
                return badRequest;
            }

            var zone = new DeliveryZone
            {
                OutletId = outletId ?? "",
                ZoneName = InputSanitizer.Sanitize(request.ZoneName),
                MinDistance = request.MinDistance,
                MaxDistance = request.MaxDistance,
                DeliveryFee = request.DeliveryFee,
                FreeDeliveryAbove = request.FreeDeliveryAbove,
                EstimatedMinutes = request.EstimatedMinutes
            };

            var created = await _mongo.CreateDeliveryZoneAsync(zone);
            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(created);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating delivery zone");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while creating the delivery zone" });
            return res;
        }
    }

    [Function("UpdateDeliveryZone")]
    public async Task<HttpResponseData> UpdateDeliveryZone(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "delivery-zones/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var request = await req.ReadFromJsonAsync<CreateDeliveryZoneRequest>();
            if (request == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid request" });
                return badRequest;
            }

            var existing = await _mongo.GetDeliveryZoneByIdAsync(id);
            if (existing == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery zone not found" });
                return notFound;
            }

            existing.ZoneName = InputSanitizer.Sanitize(request.ZoneName);
            existing.MinDistance = request.MinDistance;
            existing.MaxDistance = request.MaxDistance;
            existing.DeliveryFee = request.DeliveryFee;
            existing.FreeDeliveryAbove = request.FreeDeliveryAbove;
            existing.EstimatedMinutes = request.EstimatedMinutes;

            await _mongo.UpdateDeliveryZoneAsync(id, existing);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Delivery zone updated successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating delivery zone");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while updating the delivery zone" });
            return res;
        }
    }

    [Function("DeleteDeliveryZone")]
    public async Task<HttpResponseData> DeleteDeliveryZone(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "delivery-zones/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            await _mongo.DeleteDeliveryZoneAsync(id);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Delivery zone deleted successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error deleting delivery zone");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while deleting the delivery zone" });
            return res;
        }
    }

    [Function("CalculateDeliveryFee")]
    public async Task<HttpResponseData> CalculateDeliveryFee(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "delivery-zones/calculate-fee")] HttpRequestData req)
    {
        try
        {
            var (hasAccess, outletId, accessError) = await OutletHelper.ValidateOutletAccess(req, _auth, _mongo);
            if (!hasAccess)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = accessError });
                return forbidden;
            }

            var subtotalStr = req.Query["subtotal"];
            if (!decimal.TryParse(subtotalStr, out var subtotal))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid subtotal" });
                return badRequest;
            }

            var fee = await _mongo.CalculateDeliveryFeeAsync(outletId, subtotal);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { deliveryFee = fee });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error calculating delivery fee");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while calculating the delivery fee" });
            return res;
        }
    }
}
