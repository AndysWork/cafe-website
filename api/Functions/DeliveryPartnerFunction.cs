using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Repositories;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;

namespace Cafe.Api.Functions;

public class DeliveryPartnerFunction
{
    private readonly IOperationsRepository _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public DeliveryPartnerFunction(IOperationsRepository mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<DeliveryPartnerFunction>();
    }

    [Function("GetDeliveryPartners")]
    public async Task<HttpResponseData> GetDeliveryPartners(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/delivery-partners")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var partners = await _mongo.GetDeliveryPartnersAsync(outletId ?? "default");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(partners);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting delivery partners");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("CreateDeliveryPartner")]
    public async Task<HttpResponseData> CreateDeliveryPartner(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/delivery-partners")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<CreateDeliveryPartnerRequest>(req);
            if (validationError != null) return validationError;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);

            var partner = new DeliveryPartner
            {
                OutletId = outletId ?? "default",
                Name = InputSanitizer.Sanitize(request.Name),
                Phone = InputSanitizer.Sanitize(request.Phone),
                VehicleType = InputSanitizer.Sanitize(request.VehicleType),
                VehicleNumber = request.VehicleNumber != null ? InputSanitizer.Sanitize(request.VehicleNumber) : null,
                Status = "available",
                TotalDeliveries = 0,
                Rating = 5.0,
                CreatedAt = MongoService.GetIstNow()
            };

            await _mongo.CreateDeliveryPartnerAsync(partner);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(partner);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating delivery partner");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("AssignDeliveryPartner")]
    public async Task<HttpResponseData> AssignDeliveryPartner(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/delivery-partners/assign")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<AssignDeliveryRequest>(req);
            if (validationError != null) return validationError;

            string? partnerId = request.DeliveryPartnerId;
            string? partnerName = null;

            if (string.IsNullOrWhiteSpace(partnerId))
            {
                // Auto-assign available partner
                var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
                var available = await _mongo.GetAvailableDeliveryPartnerAsync(outletId ?? "default");
                if (available == null)
                {
                    var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                    await conflict.WriteAsJsonAsync(new { error = "No delivery partners available" });
                    return conflict;
                }
                partnerId = available.Id!;
                partnerName = available.Name;
            }

            var success = await _mongo.AssignDeliveryPartnerAsync(partnerId!, request.OrderId);
            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order or delivery partner not found" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = $"Delivery partner {partnerName ?? partnerId} assigned to order",
                partnerId,
                orderId = request.OrderId
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error assigning delivery partner");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("CompleteDelivery")]
    public async Task<HttpResponseData> CompleteDelivery(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/delivery-partners/{partnerId}/complete")] HttpRequestData req, string partnerId)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var success = await _mongo.CompleteDeliveryAsync(partnerId);
            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner not found" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Delivery completed" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error completing delivery");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("UpdateDeliveryPartnerStatus")]
    public async Task<HttpResponseData> UpdateDeliveryPartnerStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "manage/delivery-partners/{partnerId}/status")] HttpRequestData req, string partnerId)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<UpdateDeliveryPartnerStatusRequest>(req);
            if (validationError != null) return validationError;

            var validStatuses = new[] { "available", "offline" };
            if (!validStatuses.Contains(request.Status.ToLower()))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Status must be 'available' or 'offline'" });
                return badReq;
            }

            var partners = await _mongo.GetDeliveryPartnersAsync("default");
            var partner = partners.FirstOrDefault(p => p.Id == partnerId);
            if (partner == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner not found" });
                return notFound;
            }

            partner.Status = request.Status.ToLower();
            await _mongo.UpdateDeliveryPartnerAsync(partnerId, partner);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = $"Status updated to {request.Status}" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating status");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("UpdateDeliveryPartner")]
    public async Task<HttpResponseData> UpdateDeliveryPartner(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "manage/delivery-partners/{partnerId}")] HttpRequestData req, string partnerId)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<CreateDeliveryPartnerRequest>(req);
            if (validationError != null) return validationError;

            var partners = await _mongo.GetDeliveryPartnersAsync("default");
            var partner = partners.FirstOrDefault(p => p.Id == partnerId);
            if (partner == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner not found" });
                return notFound;
            }

            partner.Name = InputSanitizer.Sanitize(request.Name);
            partner.Phone = InputSanitizer.Sanitize(request.Phone);
            partner.VehicleType = InputSanitizer.Sanitize(request.VehicleType);
            partner.VehicleNumber = request.VehicleNumber != null ? InputSanitizer.Sanitize(request.VehicleNumber) : null;

            await _mongo.UpdateDeliveryPartnerAsync(partnerId, partner);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Delivery partner updated successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating delivery partner");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("DeleteDeliveryPartner")]
    public async Task<HttpResponseData> DeleteDeliveryPartner(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "manage/delivery-partners/{partnerId}")] HttpRequestData req, string partnerId)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var success = await _mongo.DeleteDeliveryPartnerAsync(partnerId);
            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner not found" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Delivery partner deleted" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error deleting delivery partner");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }
}

public class UpdateDeliveryPartnerStatusRequest
{
    public string Status { get; set; } = string.Empty;
}
