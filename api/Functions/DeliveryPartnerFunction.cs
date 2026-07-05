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
    private readonly IOrderRepository _orderRepo;
    private readonly NotificationService _notificationService;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public DeliveryPartnerFunction(IOperationsRepository mongo, IOrderRepository orderRepo, NotificationService notificationService, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _orderRepo = orderRepo;
        _notificationService = notificationService;
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
                UserId = string.IsNullOrWhiteSpace(request.UserId) ? null : InputSanitizer.Sanitize(request.UserId),
                MileageKmpl = request.MileageKmpl is > 0 ? request.MileageKmpl.Value : 40,
                CodAllowed = request.CodAllowed ?? true,
                PayoutEnabled = request.PayoutEnabled ?? true,
                LicenseNumber = string.IsNullOrWhiteSpace(request.LicenseNumber) ? null : InputSanitizer.Sanitize(request.LicenseNumber),
                EmergencyContactName = string.IsNullOrWhiteSpace(request.EmergencyContactName) ? null : InputSanitizer.Sanitize(request.EmergencyContactName),
                EmergencyContactPhone = string.IsNullOrWhiteSpace(request.EmergencyContactPhone) ? null : InputSanitizer.Sanitize(request.EmergencyContactPhone),
                BankOrUpi = string.IsNullOrWhiteSpace(request.BankOrUpi) ? null : InputSanitizer.Sanitize(request.BankOrUpi),
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

            var order = await _orderRepo.GetOrderByIdAsync(request.OrderId);
            if (order == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var requiredChannel = query["channel"]?.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(requiredChannel) && (requiredChannel == "web" || requiredChannel == "shop" || requiredChannel == "partner"))
            {
                var orderChannel = string.IsNullOrWhiteSpace(order.Channel)
                    ? (order.OrderType == "dine-in" ? "shop" : "web")
                    : order.Channel.Trim().ToLowerInvariant();

                if (!string.Equals(orderChannel, requiredChannel, StringComparison.OrdinalIgnoreCase))
                {
                    var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                    await forbidden.WriteAsJsonAsync(new { error = $"Order channel does not match required '{requiredChannel}'" });
                    return forbidden;
                }
            }

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

            var partner = await _mongo.GetDeliveryPartnerByIdAsync(partnerId);
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

            var partner = await _mongo.GetDeliveryPartnerByIdAsync(partnerId);
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
            partner.UserId = string.IsNullOrWhiteSpace(request.UserId) ? partner.UserId : InputSanitizer.Sanitize(request.UserId);
            partner.MileageKmpl = request.MileageKmpl is > 0 ? request.MileageKmpl.Value : partner.MileageKmpl;
            partner.CodAllowed = request.CodAllowed ?? partner.CodAllowed;
            partner.PayoutEnabled = request.PayoutEnabled ?? partner.PayoutEnabled;
            partner.LicenseNumber = string.IsNullOrWhiteSpace(request.LicenseNumber) ? partner.LicenseNumber : InputSanitizer.Sanitize(request.LicenseNumber);
            partner.EmergencyContactName = string.IsNullOrWhiteSpace(request.EmergencyContactName) ? partner.EmergencyContactName : InputSanitizer.Sanitize(request.EmergencyContactName);
            partner.EmergencyContactPhone = string.IsNullOrWhiteSpace(request.EmergencyContactPhone) ? partner.EmergencyContactPhone : InputSanitizer.Sanitize(request.EmergencyContactPhone);
            partner.BankOrUpi = string.IsNullOrWhiteSpace(request.BankOrUpi) ? partner.BankOrUpi : InputSanitizer.Sanitize(request.BankOrUpi);

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

    [Function("UpdateDeliveryPartnerLocation")]
    public async Task<HttpResponseData> UpdateDeliveryPartnerLocation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "manage/delivery-partners/{partnerId}/location")] HttpRequestData req, string partnerId)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<UpdateDeliveryPartnerLocationRequest>(req);
            if (validationError != null) return validationError;

            var success = await _mongo.UpdateDeliveryPartnerLocationAsync(partnerId, request.Latitude, request.Longitude);
            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner not found" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Location updated" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating delivery partner location");
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

    [Function("GetDeliveryPartnerDashboard")]
    public async Task<HttpResponseData> GetDeliveryPartnerDashboard(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "partner/delivery/dashboard")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, role, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized) return errorResponse!;

            DeliveryPartner? partner = null;
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var requestedPartnerId = query["partnerId"];

            if ((role == "admin" || role == "manager") && !string.IsNullOrWhiteSpace(requestedPartnerId))
            {
                partner = await _mongo.GetDeliveryPartnerByIdAsync(requestedPartnerId);
            }
            else if (!string.IsNullOrWhiteSpace(userId))
            {
                partner = await _mongo.GetDeliveryPartnerByUserIdAsync(userId);
            }

            if (partner == null || string.IsNullOrWhiteSpace(partner.Id))
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner profile not found" });
                return notFound;
            }

            var now = MongoService.GetIstNow();
            var dayStart = now.Date;
            var dayEnd = dayStart.AddDays(1);

            var activeShift = await _mongo.GetActiveShiftForPartnerAsync(partner.Id);
            var activeOrders = await _mongo.GetActiveOrdersForPartnerAsync(partner.Id, partner.OutletId);
            var todayDistance = await _mongo.GetPartnerDistanceAsync(partner.Id, dayStart, dayEnd);
            var fuelPrice = await _mongo.GetFuelPriceAsync(partner.OutletId, dayStart);
            var codOutstanding = await _mongo.GetOutstandingCodAmountAsync(partner.Id);
            var (avgRating, reviewsCount) = await _mongo.GetDeliveryPartnerRatingSummaryAsync(partner.Id);

            var mileage = partner.MileageKmpl <= 0 ? 40 : partner.MileageKmpl;
            var litres = mileage > 0 ? todayDistance / mileage : 0;
            var todayPayout = Math.Round(litres * (fuelPrice?.PetrolPricePerLitre ?? 0), 2, MidpointRounding.AwayFromZero);

            var responseModel = new PartnerDashboardResponse
            {
                Profile = partner,
                ActiveShift = activeShift,
                ActiveOrders = activeOrders,
                TodayDistanceKm = Math.Round(todayDistance, 2, MidpointRounding.AwayFromZero),
                TodayPayout = todayPayout,
                CodOutstanding = Math.Round(codOutstanding, 2, MidpointRounding.AwayFromZero),
                AverageRating = avgRating,
                ReviewsCount = reviewsCount
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(responseModel);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting delivery partner dashboard");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("StartDeliveryPartnerShift")]
    public async Task<HttpResponseData> StartDeliveryPartnerShift(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/delivery-partners/{partnerId}/shift/start")] HttpRequestData req,
        string partnerId)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<StartPartnerShiftRequest>(req);
            if (validationError != null) return validationError;

            var partner = await _mongo.GetDeliveryPartnerByIdAsync(partnerId);
            if (partner == null || string.IsNullOrWhiteSpace(partner.Id))
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner not found" });
                return notFound;
            }

            var existingShift = await _mongo.GetActiveShiftForPartnerAsync(partner.Id);
            if (existingShift != null)
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = "Partner already has an active shift" });
                return conflict;
            }

            var now = MongoService.GetIstNow();
            var shift = new DeliveryShift
            {
                PartnerId = partner.Id,
                OutletId = partner.OutletId,
                ShiftDate = now.Date,
                StartedAt = now,
                StartOdometerKm = request.StartOdometerKm,
                StartLatitude = request.StartLatitude,
                StartLongitude = request.StartLongitude,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : InputSanitizer.Sanitize(request.Notes),
                Status = "active",
                CreatedAt = now,
                UpdatedAt = now
            };

            await _mongo.StartPartnerShiftAsync(shift);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(shift);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error starting partner shift");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("EndDeliveryPartnerShift")]
    public async Task<HttpResponseData> EndDeliveryPartnerShift(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/delivery-partners/{partnerId}/shift/{shiftId}/end")] HttpRequestData req,
        string partnerId,
        string shiftId)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<EndPartnerShiftRequest>(req);
            if (validationError != null) return validationError;

            var partner = await _mongo.GetDeliveryPartnerByIdAsync(partnerId);
            if (partner == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner not found" });
                return notFound;
            }

            var activeShift = await _mongo.GetActiveShiftForPartnerAsync(partnerId);
            if (activeShift == null || activeShift.Id != shiftId)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "No matching active shift found" });
                return badReq;
            }

            if (request.EndOdometerKm < activeShift.StartOdometerKm)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "End odometer cannot be less than start odometer" });
                return badReq;
            }

            var success = await _mongo.EndPartnerShiftAsync(
                shiftId,
                request.EndOdometerKm,
                request.EndLatitude,
                request.EndLongitude,
                string.IsNullOrWhiteSpace(request.Notes) ? null : InputSanitizer.Sanitize(request.Notes));

            if (!success)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Could not end shift" });
                return badReq;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Shift ended successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error ending partner shift");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("CreateDeliveryPartnerTrip")]
    public async Task<HttpResponseData> CreateDeliveryPartnerTrip(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/delivery-partners/{partnerId}/trips")] HttpRequestData req,
        string partnerId)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<CreatePartnerTripRequest>(req);
            if (validationError != null) return validationError;

            var partner = await _mongo.GetDeliveryPartnerByIdAsync(partnerId);
            if (partner == null || string.IsNullOrWhiteSpace(partner.Id))
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner not found" });
                return notFound;
            }

            var activeShift = await _mongo.GetActiveShiftForPartnerAsync(partner.Id);
            if (activeShift == null || activeShift.Id != request.ShiftId)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Trip can be created only for active shift" });
                return badReq;
            }

            if (request.EndOdometerKm < request.StartOdometerKm)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "End odometer cannot be less than start odometer" });
                return badReq;
            }

            var now = MongoService.GetIstNow();
            var trip = new PartnerTripLog
            {
                ShiftId = request.ShiftId,
                PartnerId = partner.Id,
                OutletId = partner.OutletId,
                TripType = InputSanitizer.Sanitize(request.TripType).ToLowerInvariant(),
                OrderId = string.IsNullOrWhiteSpace(request.OrderId) ? null : request.OrderId,
                StartPointLabel = string.IsNullOrWhiteSpace(request.StartPointLabel) ? null : InputSanitizer.Sanitize(request.StartPointLabel),
                EndPointLabel = string.IsNullOrWhiteSpace(request.EndPointLabel) ? null : InputSanitizer.Sanitize(request.EndPointLabel),
                StartLatitude = request.StartLatitude,
                StartLongitude = request.StartLongitude,
                EndLatitude = request.EndLatitude,
                EndLongitude = request.EndLongitude,
                StartOdometerKm = request.StartOdometerKm,
                EndOdometerKm = request.EndOdometerKm,
                DistanceKm = Math.Max(0, request.EndOdometerKm - request.StartOdometerKm),
                StartedAt = now,
                EndedAt = now,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : InputSanitizer.Sanitize(request.Notes),
                CreatedAt = now
            };

            await _mongo.CreatePartnerTripAsync(trip);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(trip);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating delivery partner trip");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("UpsertDeliveryFuelPrice")]
    public async Task<HttpResponseData> UpsertDeliveryFuelPrice(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "manage/delivery-partners/fuel-price")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<UpsertFuelPriceRequest>(req);
            if (validationError != null) return validationError;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth) ?? "default";
            var price = await _mongo.UpsertFuelPriceAsync(outletId, request.Date.Date, request.PetrolPricePerLitre);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(price);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error upserting delivery fuel price");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("ConfirmCodCollection")]
    public async Task<HttpResponseData> ConfirmCodCollection(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/delivery-partners/{partnerId}/cod/confirm")] HttpRequestData req,
        string partnerId)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<ConfirmCodCollectionRequest>(req);
            if (validationError != null) return validationError;

            var partner = await _mongo.GetDeliveryPartnerByIdAsync(partnerId);
            if (partner == null || string.IsNullOrWhiteSpace(partner.Id))
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner not found" });
                return notFound;
            }

            var order = await _orderRepo.GetOrderByIdAsync(request.OrderId);
            if (order == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            if (!string.Equals(order.PaymentMethod, "cod", StringComparison.OrdinalIgnoreCase))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Order is not COD" });
                return badReq;
            }

            if (!string.Equals(order.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "COD must be confirmed by assigned delivery partner in partner dashboard" });
                return badReq;
            }

            if (!string.Equals(order.DeliveryPartnerId, partnerId, StringComparison.OrdinalIgnoreCase))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Order is not assigned to this delivery partner" });
                return badReq;
            }

            var now = MongoService.GetIstNow();
            var codLog = new CODCollectionLog
            {
                OrderId = order.Id!,
                PartnerId = partnerId,
                Amount = request.Amount,
                Collected = true,
                CollectionReference = string.IsNullOrWhiteSpace(request.CollectionReference) ? null : InputSanitizer.Sanitize(request.CollectionReference),
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : InputSanitizer.Sanitize(request.Notes),
                CollectedAt = now,
                ConfirmedByAdmin = true,
                CreatedAt = now
            };

            await _mongo.UpsertCodCollectionAsync(codLog);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "COD collection verified" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error confirming COD collection");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("ConfirmMyCodCollection")]
    public async Task<HttpResponseData> ConfirmMyCodCollection(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "partner/delivery/orders/{orderId}/cod/confirm")] HttpRequestData req,
        string orderId)
    {
        try
        {
            var (isAuthorized, userId, role, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized) return errorResponse!;

            if (role != "partner" && role != "delivery-partner" && role != "admin" && role != "manager")
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Partner access required" });
                return forbidden;
            }

            if (string.IsNullOrWhiteSpace(userId))
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Invalid user context" });
                return unauthorized;
            }

            var partner = await _mongo.GetDeliveryPartnerByUserIdAsync(userId);
            if (partner == null || string.IsNullOrWhiteSpace(partner.Id))
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner profile not found" });
                return notFound;
            }

            var (request, validationError) = await ValidationHelper.ValidateBody<ConfirmCodCollectionRequest>(req);
            if (validationError != null) return validationError;

            var order = await _orderRepo.GetOrderByIdAsync(orderId);
            if (order == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            if (!string.Equals(order.PaymentMethod, "cod", StringComparison.OrdinalIgnoreCase))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Order is not COD" });
                return badReq;
            }

            if (!string.Equals(order.DeliveryPartnerId, partner.Id, StringComparison.OrdinalIgnoreCase))
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "This COD order is not assigned to you" });
                return forbidden;
            }

            if (string.Equals(order.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
            {
                var ok = req.CreateResponse(HttpStatusCode.OK);
                await ok.WriteAsJsonAsync(new { message = "COD already confirmed", paymentStatus = "paid" });
                return ok;
            }

            var now = MongoService.GetIstNow();
            var codLog = new CODCollectionLog
            {
                OrderId = order.Id!,
                PartnerId = partner.Id,
                Amount = request.Amount,
                Collected = true,
                CollectionReference = string.IsNullOrWhiteSpace(request.CollectionReference) ? null : InputSanitizer.Sanitize(request.CollectionReference),
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : InputSanitizer.Sanitize(request.Notes),
                CollectedAt = now,
                ConfirmedByAdmin = false,
                CreatedAt = now
            };

            await _mongo.UpsertCodCollectionAsync(codLog);

            order.PaymentStatus = "paid";
            order.UpdatedAt = now;
            await _orderRepo.UpdateOrderAsync(order);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "COD payment confirmed", paymentStatus = "paid" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error confirming COD collection from partner dashboard");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("AddDeliveryPartnerReview")]
    public async Task<HttpResponseData> AddDeliveryPartnerReview(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "delivery-partners/reviews")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<AddDeliveryPartnerReviewRequest>(req);
            if (validationError != null) return validationError;

            var order = await _orderRepo.GetOrderByIdAsync(request.OrderId);
            if (order == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            if (order.UserId != userId)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "You can only review your own orders" });
                return forbidden;
            }

            if (!string.Equals(order.Status, "delivered", StringComparison.OrdinalIgnoreCase))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Only delivered orders can be reviewed" });
                return badReq;
            }

            if (!string.Equals(order.DeliveryPartnerId, request.PartnerId, StringComparison.OrdinalIgnoreCase))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Partner does not match order assignment" });
                return badReq;
            }

            var review = new DeliveryPartnerReview
            {
                OrderId = request.OrderId,
                PartnerId = request.PartnerId,
                UserId = userId!,
                Rating = request.Rating,
                Review = string.IsNullOrWhiteSpace(request.Review) ? null : InputSanitizer.Sanitize(request.Review),
                CreatedAt = MongoService.GetIstNow()
            };

            await _mongo.AddDeliveryPartnerReviewAsync(review);

            var partner = await _mongo.GetDeliveryPartnerByIdAsync(request.PartnerId);
            if (!string.IsNullOrWhiteSpace(partner?.UserId))
            {
                await _notificationService.SendSystemNotificationAsync(
                    partner.UserId,
                    "New Delivery Rating",
                    $"You received a new rating of {request.Rating}/5 for order #{request.OrderId}");
            }

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(review);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error adding delivery partner review");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("GetDeliveryPartnerPayoutSummary")]
    public async Task<HttpResponseData> GetDeliveryPartnerPayoutSummary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/delivery-partners/{partnerId}/payout")] HttpRequestData req,
        string partnerId)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var partner = await _mongo.GetDeliveryPartnerByIdAsync(partnerId);
            if (partner == null || string.IsNullOrWhiteSpace(partner.Id))
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner not found" });
                return notFound;
            }

            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var periodType = string.IsNullOrWhiteSpace(query["periodType"]) ? "day" : query["periodType"]!.Trim().ToLowerInvariant();
            var referenceDate = DateTime.TryParse(query["date"], out var parsedDate)
                ? parsedDate
                : MongoService.GetIstNow();

            var (periodStart, periodEnd) = GetPeriodRange(periodType, referenceDate);

            var totalDistance = await _mongo.GetPartnerDistanceAsync(partner.Id, periodStart, periodEnd);
            var trips = await _mongo.GetPartnerTripsAsync(partner.Id, periodStart, periodEnd);
            var totalDeliveries = trips.Count(t => t.TripType == "delivery");

            var fuel = await _mongo.GetFuelPriceAsync(partner.OutletId, periodStart.Date)
                ?? await _mongo.UpsertFuelPriceAsync(partner.OutletId, periodStart.Date, 105);

            var mileage = partner.MileageKmpl <= 0 ? 40 : partner.MileageKmpl;
            var litresConsumed = mileage > 0 ? totalDistance / mileage : 0;
            var payoutAmount = Math.Round(litresConsumed * fuel.PetrolPricePerLitre, 2, MidpointRounding.AwayFromZero);

            var responseModel = new PartnerPayoutSummaryResponse
            {
                PeriodType = periodType,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                TotalDistanceKm = Math.Round(totalDistance, 2, MidpointRounding.AwayFromZero),
                TotalDeliveries = totalDeliveries,
                MileageKmpl = mileage,
                FuelPricePerLitre = fuel.PetrolPricePerLitre,
                LitresConsumed = Math.Round(litresConsumed, 3, MidpointRounding.AwayFromZero),
                PayoutAmount = payoutAmount
            };

            var ledger = new PartnerPayoutLedger
            {
                PartnerId = partner.Id,
                OutletId = partner.OutletId,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                PeriodType = periodType,
                TotalDistanceKm = responseModel.TotalDistanceKm,
                MileageKmpl = responseModel.MileageKmpl,
                FuelPricePerLitre = responseModel.FuelPricePerLitre,
                LitresConsumed = responseModel.LitresConsumed,
                PayoutAmount = responseModel.PayoutAmount,
                TotalDeliveries = responseModel.TotalDeliveries,
                IsFinalized = false
            };

            await _mongo.CreatePartnerPayoutLedgerAsync(ledger);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(responseModel);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting delivery partner payout summary");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("StartMyDeliveryShift")]
    public async Task<HttpResponseData> StartMyDeliveryShift(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "partner/delivery/shift/start")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, role, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized) return errorResponse!;

            if (role != "partner" && role != "delivery-partner" && role != "admin" && role != "manager")
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Partner access required" });
                return forbidden;
            }

            if (string.IsNullOrWhiteSpace(userId))
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Invalid user context" });
                return unauthorized;
            }

            var partner = await _mongo.GetDeliveryPartnerByUserIdAsync(userId);
            if (partner == null || string.IsNullOrWhiteSpace(partner.Id))
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner profile not found" });
                return notFound;
            }

            var (request, validationError) = await ValidationHelper.ValidateBody<StartPartnerShiftRequest>(req);
            if (validationError != null) return validationError;

            var existingShift = await _mongo.GetActiveShiftForPartnerAsync(partner.Id);
            if (existingShift != null)
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = "You already have an active shift" });
                return conflict;
            }

            var now = MongoService.GetIstNow();
            var shift = new DeliveryShift
            {
                PartnerId = partner.Id,
                OutletId = partner.OutletId,
                ShiftDate = now.Date,
                StartedAt = now,
                StartOdometerKm = request.StartOdometerKm,
                StartLatitude = request.StartLatitude,
                StartLongitude = request.StartLongitude,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : InputSanitizer.Sanitize(request.Notes),
                Status = "active",
                CreatedAt = now,
                UpdatedAt = now
            };

            await _mongo.StartPartnerShiftAsync(shift);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(shift);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error starting self-service partner shift");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("EndMyDeliveryShift")]
    public async Task<HttpResponseData> EndMyDeliveryShift(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "partner/delivery/shift/{shiftId}/end")] HttpRequestData req,
        string shiftId)
    {
        try
        {
            var (isAuthorized, userId, role, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized) return errorResponse!;

            if (role != "partner" && role != "delivery-partner" && role != "admin" && role != "manager")
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Partner access required" });
                return forbidden;
            }

            if (string.IsNullOrWhiteSpace(userId))
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Invalid user context" });
                return unauthorized;
            }

            var partner = await _mongo.GetDeliveryPartnerByUserIdAsync(userId);
            if (partner == null || string.IsNullOrWhiteSpace(partner.Id))
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner profile not found" });
                return notFound;
            }

            var (request, validationError) = await ValidationHelper.ValidateBody<EndPartnerShiftRequest>(req);
            if (validationError != null) return validationError;

            var activeShift = await _mongo.GetActiveShiftForPartnerAsync(partner.Id);
            if (activeShift == null || activeShift.Id != shiftId)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "No matching active shift found" });
                return badReq;
            }

            if (request.EndOdometerKm < activeShift.StartOdometerKm)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "End odometer cannot be less than start odometer" });
                return badReq;
            }

            var success = await _mongo.EndPartnerShiftAsync(
                shiftId,
                request.EndOdometerKm,
                request.EndLatitude,
                request.EndLongitude,
                string.IsNullOrWhiteSpace(request.Notes) ? null : InputSanitizer.Sanitize(request.Notes));

            if (!success)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Could not end shift" });
                return badReq;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Shift ended successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error ending self-service partner shift");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("CreateMyDeliveryTrip")]
    public async Task<HttpResponseData> CreateMyDeliveryTrip(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "partner/delivery/trips")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, role, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized) return errorResponse!;

            if (role != "partner" && role != "delivery-partner" && role != "admin" && role != "manager")
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Partner access required" });
                return forbidden;
            }

            if (string.IsNullOrWhiteSpace(userId))
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Invalid user context" });
                return unauthorized;
            }

            var partner = await _mongo.GetDeliveryPartnerByUserIdAsync(userId);
            if (partner == null || string.IsNullOrWhiteSpace(partner.Id))
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner profile not found" });
                return notFound;
            }

            var (request, validationError) = await ValidationHelper.ValidateBody<CreatePartnerTripRequest>(req);
            if (validationError != null) return validationError;

            var activeShift = await _mongo.GetActiveShiftForPartnerAsync(partner.Id);
            if (activeShift == null || activeShift.Id != request.ShiftId)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Trip can be created only for active shift" });
                return badReq;
            }

            if (request.EndOdometerKm < request.StartOdometerKm)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "End odometer cannot be less than start odometer" });
                return badReq;
            }

            var now = MongoService.GetIstNow();
            var trip = new PartnerTripLog
            {
                ShiftId = request.ShiftId,
                PartnerId = partner.Id,
                OutletId = partner.OutletId,
                TripType = InputSanitizer.Sanitize(request.TripType).ToLowerInvariant(),
                OrderId = string.IsNullOrWhiteSpace(request.OrderId) ? null : request.OrderId,
                StartPointLabel = string.IsNullOrWhiteSpace(request.StartPointLabel) ? null : InputSanitizer.Sanitize(request.StartPointLabel),
                EndPointLabel = string.IsNullOrWhiteSpace(request.EndPointLabel) ? null : InputSanitizer.Sanitize(request.EndPointLabel),
                StartLatitude = request.StartLatitude,
                StartLongitude = request.StartLongitude,
                EndLatitude = request.EndLatitude,
                EndLongitude = request.EndLongitude,
                StartOdometerKm = request.StartOdometerKm,
                EndOdometerKm = request.EndOdometerKm,
                DistanceKm = Math.Max(0, request.EndOdometerKm - request.StartOdometerKm),
                StartedAt = now,
                EndedAt = now,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : InputSanitizer.Sanitize(request.Notes),
                CreatedAt = now
            };

            await _mongo.CreatePartnerTripAsync(trip);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(trip);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating self-service partner trip");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("GetMyDeliveryPayoutSummary")]
    public async Task<HttpResponseData> GetMyDeliveryPayoutSummary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "partner/delivery/payout")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, role, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized) return errorResponse!;

            if (role != "partner" && role != "delivery-partner" && role != "admin" && role != "manager")
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Partner access required" });
                return forbidden;
            }

            if (string.IsNullOrWhiteSpace(userId))
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Invalid user context" });
                return unauthorized;
            }

            var partner = await _mongo.GetDeliveryPartnerByUserIdAsync(userId);
            if (partner == null || string.IsNullOrWhiteSpace(partner.Id))
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner profile not found" });
                return notFound;
            }

            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var periodType = string.IsNullOrWhiteSpace(query["periodType"]) ? "day" : query["periodType"]!.Trim().ToLowerInvariant();
            var referenceDate = DateTime.TryParse(query["date"], out var parsedDate)
                ? parsedDate
                : MongoService.GetIstNow();

            var (periodStart, periodEnd) = GetPeriodRange(periodType, referenceDate);

            var totalDistance = await _mongo.GetPartnerDistanceAsync(partner.Id, periodStart, periodEnd);
            var trips = await _mongo.GetPartnerTripsAsync(partner.Id, periodStart, periodEnd);
            var totalDeliveries = trips.Count(t => t.TripType == "delivery");

            var fuel = await _mongo.GetFuelPriceAsync(partner.OutletId, periodStart.Date)
                ?? await _mongo.UpsertFuelPriceAsync(partner.OutletId, periodStart.Date, 105);

            var mileage = partner.MileageKmpl <= 0 ? 40 : partner.MileageKmpl;
            var litresConsumed = mileage > 0 ? totalDistance / mileage : 0;
            var payoutAmount = Math.Round(litresConsumed * fuel.PetrolPricePerLitre, 2, MidpointRounding.AwayFromZero);

            var responseModel = new PartnerPayoutSummaryResponse
            {
                PeriodType = periodType,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                TotalDistanceKm = Math.Round(totalDistance, 2, MidpointRounding.AwayFromZero),
                TotalDeliveries = totalDeliveries,
                MileageKmpl = mileage,
                FuelPricePerLitre = fuel.PetrolPricePerLitre,
                LitresConsumed = Math.Round(litresConsumed, 3, MidpointRounding.AwayFromZero),
                PayoutAmount = payoutAmount
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(responseModel);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting self-service partner payout summary");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    private static (DateTime periodStart, DateTime periodEnd) GetPeriodRange(string periodType, DateTime referenceDate)
    {
        var d = referenceDate.Date;
        return periodType switch
        {
            "week" => (d.AddDays(-(int)d.DayOfWeek), d.AddDays(-(int)d.DayOfWeek).AddDays(7)),
            "month" => (new DateTime(d.Year, d.Month, 1), new DateTime(d.Year, d.Month, 1).AddMonths(1)),
            "year" => (new DateTime(d.Year, 1, 1), new DateTime(d.Year + 1, 1, 1)),
            _ => (d, d.AddDays(1))
        };
    }
}

public class UpdateDeliveryPartnerStatusRequest
{
    public string Status { get; set; } = string.Empty;
}
