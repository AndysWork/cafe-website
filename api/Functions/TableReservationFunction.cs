using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;
using System.Security.Claims;

namespace Cafe.Api.Functions;

public class TableReservationFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly NotificationService _notificationService;
    private readonly ILogger _log;

    public TableReservationFunction(MongoService mongo, AuthService auth, NotificationService notificationService, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _notificationService = notificationService;
        _log = loggerFactory.CreateLogger<TableReservationFunction>();
    }

    [Function("CreateReservation")]
    public async Task<HttpResponseData> CreateReservation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "reservations")] HttpRequestData req)
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

            // Try to get userId if logged in (optional for reservations)
            string? userId = null;
            var authHeader = req.Headers.TryGetValues("Authorization", out var headerValues) ? headerValues.FirstOrDefault() : null;
            if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer "))
            {
                var token = authHeader.Substring("Bearer ".Length).Trim();
                var principal = _auth.ValidateToken(token);
                userId = principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            }

            var (request, validationError) = await ValidationHelper.ValidateBody<CreateReservationRequest>(req);
            if (validationError != null) return validationError;

            var reservation = new TableReservation
            {
                OutletId = outletId,
                UserId = userId,
                CustomerName = InputSanitizer.Sanitize(request.CustomerName),
                CustomerPhone = request.CustomerPhone,
                CustomerEmail = request.CustomerEmail,
                PartySize = request.PartySize,
                TableNumber = request.TableNumber,
                ReservationDate = request.ReservationDate,
                TimeSlot = request.TimeSlot,
                SpecialRequests = request.SpecialRequests != null ? InputSanitizer.Sanitize(request.SpecialRequests) : null
            };

            var created = await _mongo.CreateReservationAsync(reservation);

            if (!string.IsNullOrEmpty(userId))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _notificationService.SendAsync(userId, "reservation", "Table Reserved! 🍽️",
                            $"Your table for {request.PartySize} is reserved on {request.ReservationDate:dd MMM yyyy} at {request.TimeSlot}",
                            actionUrl: "/orders");
                    }
                    catch { }
                });
            }

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(created);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating reservation");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while creating the reservation" });
            return res;
        }
    }

    [Function("GetReservations")]
    public async Task<HttpResponseData> GetReservations(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reservations")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            DateTime? date = DateTime.TryParse(req.Query["date"], out var d) ? d : null;

            var reservations = await _mongo.GetReservationsAsync(outletId ?? "", date);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(reservations);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting reservations");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while retrieving reservations" });
            return res;
        }
    }

    [Function("GetMyReservations")]
    public async Task<HttpResponseData> GetMyReservations(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reservations/my")] HttpRequestData req)
    {
        try
        {
            var (isAuthenticated, userId, _, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthenticated) return errorResponse!;

            var reservations = await _mongo.GetUserReservationsAsync(userId!);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(reservations);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting user reservations");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while retrieving your reservations" });
            return res;
        }
    }

    [Function("UpdateReservationStatus")]
    public async Task<HttpResponseData> UpdateReservationStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "reservations/{id}/status")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<UpdateReservationStatusRequest>(req);
            if (validationError != null) return validationError;

            var success = await _mongo.UpdateReservationStatusAsync(id, request.Status);
            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Reservation not found" });
                return notFound;
            }

            var reservation = await _mongo.GetReservationByIdAsync(id);
            if (reservation != null && !string.IsNullOrEmpty(reservation.UserId))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _notificationService.SendAsync(reservation.UserId, "reservation",
                            $"Reservation {request.Status.ToUpper()}",
                            $"Your reservation for {reservation.ReservationDate:dd MMM yyyy} at {reservation.TimeSlot} has been {request.Status}.",
                            actionUrl: "/orders");
                    }
                    catch { }
                });
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Reservation status updated successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating reservation status");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while updating the reservation" });
            return res;
        }
    }
}
