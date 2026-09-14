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
            var (request, validationError) = await ValidationHelper.ValidateBody<CreateReservationRequest>(req);
            if (validationError != null) return validationError;

            // Resolve target outlet
            string? outletId = request.OutletId?.Trim();
            if (string.IsNullOrWhiteSpace(outletId))
            {
                outletId = OutletHelper.GetOutletIdFromRequest(req, _auth);
            }
            if (string.IsNullOrWhiteSpace(outletId))
            {
                var outlets = await _mongo.GetActiveOutletsAsync();
                outletId = outlets.FirstOrDefault()?.Id;
            }
            if (string.IsNullOrWhiteSpace(outletId))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Valid outlet ID is required" });
                return badRequest;
            }

            // Try to get userId if logged in (optional for reservations)
            string? userId = null;
            var authHeader = req.Headers.TryGetValues("Authorization", out var headerValues) ? headerValues.FirstOrDefault() : null;
            if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authHeader.Substring("Bearer ".Length).Trim();
                var principal = _auth.ValidateToken(token);
                userId = principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            }

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
                            actionUrl: "/reservations");
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

            var reservations = await _mongo.GetReservationsAsync(outletId, date);
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
            var userSessions = await _mongo.GetUserDineInSessionsAsync(userId!);

            // Auto-link missing DineInSessionId for seated or completed reservations if not linked yet
            foreach (var r in reservations)
            {
                try
                {
                    var cleanResTable = System.Text.RegularExpressions.Regex.Replace(r.TableNumber ?? string.Empty, @"(?i)^table\s*", string.Empty).Trim();
                    DineInSession? matchedSession = null;

                    // If reservation is completed, strictly prioritize the paid session with items
                    if (r.Status == "completed")
                    {
                        if (!string.IsNullOrWhiteSpace(r.DineInSessionId))
                        {
                            matchedSession = userSessions.FirstOrDefault(s => s.Id == r.DineInSessionId && s.Status == "paid");
                        }

                        if (matchedSession == null && !string.IsNullOrWhiteSpace(cleanResTable))
                        {
                            matchedSession = userSessions.FirstOrDefault(s =>
                                s.Status == "paid" &&
                                System.Text.RegularExpressions.Regex.Replace(s.TableNumber ?? string.Empty, @"(?i)^table\s*", string.Empty).Trim().Equals(cleanResTable, StringComparison.OrdinalIgnoreCase));
                        }
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(r.DineInSessionId))
                        {
                            matchedSession = userSessions.FirstOrDefault(s => s.Id == r.DineInSessionId);
                        }

                        if (matchedSession == null && !string.IsNullOrWhiteSpace(cleanResTable))
                        {
                            matchedSession = userSessions.FirstOrDefault(s =>
                                System.Text.RegularExpressions.Regex.Replace(s.TableNumber ?? string.Empty, @"(?i)^table\s*", string.Empty).Trim().Equals(cleanResTable, StringComparison.OrdinalIgnoreCase));
                        }
                    }

                    if (matchedSession != null)
                    {
                        bool shouldUpdate = false;
                        if (r.DineInSessionId != matchedSession.Id)
                        {
                            r.DineInSessionId = matchedSession.Id;
                            shouldUpdate = true;
                        }
                        if (matchedSession.Status == "paid" && r.Status != "completed" && r.Status != "cancelled")
                        {
                            r.Status = "completed";
                            shouldUpdate = true;
                        }
                        if (shouldUpdate)
                        {
                            _ = _mongo.UpdateReservationStatusAsync(r.Id!, r.Status, r.TableNumber, matchedSession.Id);
                        }
                    }
                }
                catch { }
            }

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
            var (isAuthenticated, currentUserId, role, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthenticated) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<UpdateReservationStatusRequest>(req);
            if (validationError != null) return validationError;

            var existing = await _mongo.GetReservationByIdAsync(id);
            if (existing == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Reservation not found" });
                return notFound;
            }

            var isAdminOrStaff = role == "admin" || role == "manager" || role == "assistant-manager";

            // If user is a customer, they are permitted to cancel their own reservation
            if (!isAdminOrStaff)
            {
                if (existing.UserId != currentUserId)
                {
                    var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                    await forbidden.WriteAsJsonAsync(new { error = "You are not authorized to modify this reservation." });
                    return forbidden;
                }

                if (request.Status != "cancelled")
                {
                    var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                    await forbidden.WriteAsJsonAsync(new { error = "Customers can only cancel their reservations." });
                    return forbidden;
                }

                if (existing.Status == "completed" || existing.Status == "cancelled")
                {
                    var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badReq.WriteAsJsonAsync(new { error = $"Cannot cancel a reservation that is already {existing.Status}." });
                    return badReq;
                }
            }

            var tableNumber = !string.IsNullOrWhiteSpace(request.TableNumber) ? request.TableNumber.Trim() : existing.TableNumber;
            string? dineInSessionId = existing.DineInSessionId;

            // If status is transitioning to 'seated', automatically provision/link DineInSession for the assigned table
            if (request.Status == "seated" && !string.IsNullOrWhiteSpace(tableNumber))
            {
                var session = await _mongo.GetActiveDineInSessionByTableAsync(existing.OutletId, tableNumber);
                if (session == null)
                {
                    var outlet = await _mongo.GetOutletByIdAsync(existing.OutletId);
                    session = new DineInSession
                    {
                        OutletId = existing.OutletId,
                        OutletName = outlet?.OutletName ?? "Maa Tara Cafe",
                        TableNumber = tableNumber,
                        UserId = existing.UserId,
                        CustomerName = existing.CustomerName,
                        CustomerPhone = existing.CustomerPhone,
                        Status = "active",
                        PaymentStatus = "unpaid",
                        CreatedAt = MongoService.GetIstNow(),
                        UpdatedAt = MongoService.GetIstNow()
                    };
                    session = await _mongo.CreateDineInSessionAsync(session);
                }
                dineInSessionId = session.Id;
            }

            var success = await _mongo.UpdateReservationStatusAsync(id, request.Status, tableNumber, dineInSessionId);
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
                        var msg = request.Status == "seated"
                            ? $"You are checked in at Table {tableNumber}! You can start ordering food right away."
                            : $"Your reservation for {reservation.ReservationDate:dd MMM yyyy} at {reservation.TimeSlot} has been {request.Status}.";

                        await _notificationService.SendAsync(reservation.UserId, "reservation",
                            $"Reservation {request.Status.ToUpper()}",
                            msg,
                            actionUrl: request.Status == "seated" ? $"/menu?table={tableNumber}" : "/reservations");
                    }
                    catch { }
                });
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = "Reservation status updated successfully",
                status = request.Status,
                tableNumber,
                dineInSessionId
            });
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

    /// <summary>
    /// Customer checks in on the reservation day.
    /// If table is already assigned or provided, starts the dine-in session immediately and returns table details.
    /// </summary>
    [Function("CheckInReservation")]
    public async Task<HttpResponseData> CheckInReservation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "reservations/{id}/check-in")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthenticated, currentUserId, role, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthenticated) return errorResponse!;

            var reservation = await _mongo.GetReservationByIdAsync(id);
            if (reservation == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Reservation not found" });
                return notFound;
            }

            // Verify user owns reservation or is staff/admin
            if (role != "admin" && role != "manager" && reservation.UserId != currentUserId)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "You are not authorized to check in for this reservation." });
                return forbidden;
            }

            if (reservation.Status == "cancelled" || reservation.Status == "no-show" || reservation.Status == "completed")
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = $"Cannot check in a {reservation.Status} reservation." });
                return badReq;
            }

            var (request, validationError) = await ValidationHelper.ValidateBody<CheckInReservationRequest>(req);
            if (validationError != null) return validationError;

            var tableNumber = !string.IsNullOrWhiteSpace(request?.TableNumber)
                ? request.TableNumber.Trim()
                : reservation.TableNumber?.Trim();

            if (string.IsNullOrWhiteSpace(tableNumber))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Please enter or select a table number from 1 to 10." });
                return badReq;
            }

            if (!int.TryParse(tableNumber, out int tableNum) || tableNum < 1 || tableNum > 10)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Invalid table number. Only table numbers from 1 to 10 are valid." });
                return badReq;
            }

            tableNumber = tableNum.ToString();

            // Create or join active DineInSession for this table
            var session = await _mongo.GetActiveDineInSessionByTableAsync(reservation.OutletId, tableNumber);
            if (session == null)
            {
                var outlet = await _mongo.GetOutletByIdAsync(reservation.OutletId);
                session = new DineInSession
                {
                    OutletId = reservation.OutletId,
                    OutletName = outlet?.OutletName ?? "Maa Tara Cafe",
                    TableNumber = tableNumber,
                    UserId = reservation.UserId ?? currentUserId,
                    CustomerName = reservation.CustomerName,
                    CustomerPhone = reservation.CustomerPhone,
                    Status = "active",
                    PaymentStatus = "unpaid",
                    CreatedAt = MongoService.GetIstNow(),
                    UpdatedAt = MongoService.GetIstNow()
                };
                session = await _mongo.CreateDineInSessionAsync(session);
            }

            // Mark reservation as seated and record table + session
            await _mongo.UpdateReservationStatusAsync(id, "seated", tableNumber, session.Id);

            var checkInResponse = new CheckInReservationResponse
            {
                ReservationId = id,
                TableNumber = tableNumber,
                SessionId = session.Id!,
                OutletId = reservation.OutletId,
                Status = "seated",
                Message = $"Welcome to Maa Tara Cafe! You are seated at Table {tableNumber}. Your dining tab is open."
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(checkInResponse);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error checking in reservation");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred during check-in" });
            return res;
        }
    }
}
