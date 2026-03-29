using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;

namespace Cafe.Api.Functions;

public class AttendanceFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public AttendanceFunction(MongoService mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<AttendanceFunction>();
    }

    [Function("ClockIn")]
    public async Task<HttpResponseData> ClockIn(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "attendance/clock-in")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var request = await req.ReadFromJsonAsync<ClockInOutRequest>();
            if (request == null || string.IsNullOrWhiteSpace(request.StaffId))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "StaffId is required" });
                return badReq;
            }

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var attendance = await _mongo.ClockInAsync(request.StaffId, "", outletId ?? "default");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Clocked in successfully", attendance });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error clocking in");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while clocking in" });
            return res;
        }
    }

    [Function("ClockOut")]
    public async Task<HttpResponseData> ClockOut(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "attendance/clock-out")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var request = await req.ReadFromJsonAsync<ClockInOutRequest>();
            if (request == null || string.IsNullOrWhiteSpace(request.StaffId))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "StaffId is required" });
                return badReq;
            }

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var attendance = await _mongo.ClockOutAsync(request.StaffId, outletId ?? "default");

            if (attendance == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "No active clock-in found for this staff member today" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = $"Clocked out. Hours worked: {attendance.HoursWorked:F1}h", attendance });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error clocking out");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while clocking out" });
            return res;
        }
    }

    [Function("GetTodayAttendance")]
    public async Task<HttpResponseData> GetTodayAttendance(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "attendance/today")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var records = await _mongo.GetAllTodayAttendanceAsync(outletId ?? "default");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(records);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting today attendance");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("GetAttendanceReport")]
    public async Task<HttpResponseData> GetAttendanceReport(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "attendance/report")] HttpRequestData req)
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

            var records = await _mongo.GetAttendanceByDateRangeAsync(outletId ?? "default", startDate, endDate);
            var summary = await _mongo.GetAttendanceSummaryAsync(outletId ?? "default", startDate, endDate);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { records, summary });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting attendance report");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("CreateLeaveRequest")]
    public async Task<HttpResponseData> CreateLeaveRequest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "attendance/leave")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var request = await req.ReadFromJsonAsync<CreateLeaveRequestDto>();
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

            var leave = new LeaveRequest
            {
                OutletId = outletId ?? "default",
                StaffId = InputSanitizer.Sanitize(request.StaffId),
                StaffName = "",
                LeaveType = InputSanitizer.Sanitize(request.LeaveType),
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                Reason = InputSanitizer.Sanitize(request.Reason),
                Status = "pending",
                CreatedAt = MongoService.GetIstNow()
            };

            await _mongo.CreateLeaveRequestAsync(leave);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(leave);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating leave request");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("GetLeaveRequests")]
    public async Task<HttpResponseData> GetLeaveRequests(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "attendance/leave")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var status = req.Query["status"];

            var leaves = await _mongo.GetLeaveRequestsAsync(outletId ?? "default", status);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(leaves);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting leave requests");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("UpdateLeaveRequestStatus")]
    public async Task<HttpResponseData> UpdateLeaveRequestStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "attendance/leave/{id}/status")] HttpRequestData req, string id)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var request = await req.ReadFromJsonAsync<UpdateLeaveStatusRequest>();
            if (request == null || string.IsNullOrWhiteSpace(request.Status))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Status is required" });
                return badReq;
            }

            var validStatuses = new[] { "approved", "rejected" };
            if (!validStatuses.Contains(request.Status.ToLower()))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Status must be 'approved' or 'rejected'" });
                return badReq;
            }

            var success = await _mongo.UpdateLeaveRequestStatusAsync(id, request.Status.ToLower());

            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Leave request not found" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = $"Leave request {request.Status}" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating leave request");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }
}

public class UpdateLeaveStatusRequest
{
    public string Status { get; set; } = string.Empty;
}
