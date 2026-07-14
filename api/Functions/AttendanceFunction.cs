using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Repositories;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using MongoDB.Bson;

namespace Cafe.Api.Functions;

public class AttendanceFunction
{
    private readonly IStaffRepository _mongo;
    private readonly IUserRepository _users;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public AttendanceFunction(IStaffRepository mongo, IUserRepository users, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _users = users;
        _auth = auth;
        _log = loggerFactory.CreateLogger<AttendanceFunction>();
    }

    [Function("StartMyShift")]
    public async Task<HttpResponseData> StartMyShift(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "attendance/my/shift-start")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized || string.IsNullOrWhiteSpace(userId)) return errorResponse!;

            var (staff, outletId, shiftInfo, resolveError) = await ResolveStaffAndShiftAsync(req, userId);
            if (staff == null || !string.IsNullOrWhiteSpace(resolveError))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = resolveError ?? "Staff profile not linked" });
                return badReq;
            }

            var clientNow = ResolveClientNow(req);
            var existingAttendance = await _mongo.GetTodayAttendanceAsync(staff.Id!, outletId, clientNow);
            var shiftState = BuildShiftActionState(shiftInfo, existingAttendance, clientNow);
            if (!shiftState.CanShiftIn)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = shiftState.Message });
                return badReq;
            }

            var attendance = await _mongo.ClockInAsync(
                staff.Id!,
                $"{staff.FirstName} {staff.LastName}".Trim(),
                outletId,
                shiftState.CurrentShiftScheduledHours,
                shiftState.CurrentShiftName,
                shiftState.CurrentShiftKey,
                shiftState.CurrentShiftStart,
                shiftState.CurrentShiftEnd,
                clientNow);

            var updatedState = BuildShiftActionState(shiftInfo, attendance, clientNow);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = "Shift started",
                attendance,
                schedule = new
                {
                    shiftInfo.shiftLabel,
                    scheduledHours = shiftInfo.scheduledHours,
                    shiftNames = shiftInfo.shiftNames,
                    shifts = shiftInfo.shifts.Select(s => new
                    {
                        name = s.name,
                        startTime = s.startTime,
                        endTime = s.endTime,
                        breakDuration = s.breakDuration,
                        scheduledHours = s.hours
                    }),
                    currentShiftName = updatedState.CurrentShiftName,
                    currentShiftKey = updatedState.CurrentShiftKey,
                    currentShiftStart = updatedState.CurrentShiftStart,
                    currentShiftEnd = updatedState.CurrentShiftEnd,
                    currentShiftScheduledHours = updatedState.CurrentShiftScheduledHours,
                    canShiftIn = updatedState.CanShiftIn,
                    canShiftOut = updatedState.CanShiftOut,
                    actionMessage = updatedState.Message
                }
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error starting self shift");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while starting shift" });
            return res;
        }
    }

    [Function("EndMyShift")]
    public async Task<HttpResponseData> EndMyShift(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "attendance/my/shift-end")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized || string.IsNullOrWhiteSpace(userId)) return errorResponse!;

            var (staff, outletId, shiftInfo, resolveError) = await ResolveStaffAndShiftAsync(req, userId);
            if (staff == null || !string.IsNullOrWhiteSpace(resolveError))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = resolveError ?? "Staff profile not linked" });
                return badReq;
            }

            var clientNow = ResolveClientNow(req);
            var existingAttendance = await _mongo.GetTodayAttendanceAsync(staff.Id!, outletId, clientNow);
            var shiftState = BuildShiftActionState(shiftInfo, existingAttendance, clientNow);

            if (existingAttendance == null || !existingAttendance.ClockIn.HasValue || existingAttendance.ClockOut.HasValue)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "No active shift found for today" });
                return notFound;
            }

            if (!shiftState.CanShiftOut)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = shiftState.Message });
                return badReq;
            }

            var attendance = await _mongo.ClockOutAsync(
                staff.Id!,
                outletId,
                shiftState.CurrentShiftScheduledHours,
                shiftState.CurrentShiftName,
                shiftState.CurrentShiftKey,
                clientNow);

            if (attendance == null || !attendance.ClockIn.HasValue)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "No active shift found for today" });
                return notFound;
            }

            var updatedState = BuildShiftActionState(shiftInfo, attendance, clientNow);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = "Shift ended",
                attendance,
                schedule = new
                {
                    shiftInfo.shiftLabel,
                    scheduledHours = shiftInfo.scheduledHours,
                    shiftNames = shiftInfo.shiftNames,
                    shifts = shiftInfo.shifts.Select(s => new
                    {
                        name = s.name,
                        startTime = s.startTime,
                        endTime = s.endTime,
                        breakDuration = s.breakDuration,
                        scheduledHours = s.hours
                    }),
                    currentShiftName = updatedState.CurrentShiftName,
                    currentShiftKey = updatedState.CurrentShiftKey,
                    currentShiftStart = updatedState.CurrentShiftStart,
                    currentShiftEnd = updatedState.CurrentShiftEnd,
                    currentShiftScheduledHours = updatedState.CurrentShiftScheduledHours,
                    canShiftIn = updatedState.CanShiftIn,
                    canShiftOut = updatedState.CanShiftOut,
                    actionMessage = updatedState.Message
                },
                summary = new
                {
                    hoursWorked = attendance.HoursWorked,
                    scheduledHours = attendance.ScheduledHours,
                    overtimeHours = attendance.OvertimeHours,
                    undertimeHours = attendance.UndertimeHours,
                    shiftLabel = attendance.ScheduledShiftLabel
                }
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error ending self shift");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while ending shift" });
            return res;
        }
    }

    [Function("GetMyTodayAttendance")]
    public async Task<HttpResponseData> GetMyTodayAttendance(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "attendance/my/today")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized || string.IsNullOrWhiteSpace(userId)) return errorResponse!;

            var (staff, outletId, shiftInfo, resolveError) = await ResolveStaffAndShiftAsync(req, userId);
            if (staff == null || !string.IsNullOrWhiteSpace(resolveError))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = resolveError ?? "Staff profile not linked" });
                return badReq;
            }

            var clientNow = ResolveClientNow(req);
            var attendance = await _mongo.GetTodayAttendanceAsync(staff.Id!, outletId, clientNow);
            var shiftState = BuildShiftActionState(shiftInfo, attendance, clientNow);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                attendance,
                schedule = new
                {
                    shiftInfo.shiftLabel,
                    scheduledHours = shiftInfo.scheduledHours,
                    shiftNames = shiftInfo.shiftNames,
                    shifts = shiftInfo.shifts.Select(s => new
                    {
                        name = s.name,
                        startTime = s.startTime,
                        endTime = s.endTime,
                        breakDuration = s.breakDuration,
                        scheduledHours = s.hours
                    }),
                    currentShiftName = shiftState.CurrentShiftName,
                    currentShiftKey = shiftState.CurrentShiftKey,
                    currentShiftStart = shiftState.CurrentShiftStart,
                    currentShiftEnd = shiftState.CurrentShiftEnd,
                    currentShiftScheduledHours = shiftState.CurrentShiftScheduledHours,
                    canShiftIn = shiftState.CanShiftIn,
                    canShiftOut = shiftState.CanShiftOut,
                    actionMessage = shiftState.Message
                }
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting self attendance");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while fetching attendance" });
            return res;
        }
    }

    [Function("ClockIn")]
    public async Task<HttpResponseData> ClockIn(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "attendance/clock-in")] HttpRequestData req)
    {
        var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
        await forbidden.WriteAsJsonAsync(new
        {
            error = "Manual attendance entry is disabled. Staff must use attendance/my/shift-start."
        });
        return forbidden;
    }

    [Function("ClockOut")]
    public async Task<HttpResponseData> ClockOut(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "attendance/clock-out")] HttpRequestData req)
    {
        var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
        await forbidden.WriteAsJsonAsync(new
        {
            error = "Manual attendance entry is disabled. Staff must use attendance/my/shift-end."
        });
        return forbidden;
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

            var (request, validationError) = await ValidationHelper.ValidateBody<CreateLeaveRequestDto>(req);
            if (validationError != null) return validationError;
            if (request == null)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Invalid request body" });
                return badReq;
            }

            if (request.EndDate.Date < request.StartDate.Date)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "End date must be same or after start date" });
                return badReq;
            }

            if (request.IsHalfDay && request.EndDate.Date != request.StartDate.Date)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Half-day leave must be for a single date" });
                return badReq;
            }

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var staff = await _mongo.GetStaffByIdAsync(request.StaffId);
            var staffName = staff == null
                ? string.Empty
                : $"{staff.FirstName} {staff.LastName}".Trim();

            var leave = new LeaveRequest
            {
                OutletId = outletId ?? "default",
                StaffId = InputSanitizer.Sanitize(request.StaffId),
                StaffName = staffName,
                LeaveType = InputSanitizer.Sanitize(request.LeaveType),
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                IsHalfDay = request.IsHalfDay,
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

    [Function("CreateMyLeaveRequest")]
    public async Task<HttpResponseData> CreateMyLeaveRequest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "attendance/my/leave")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized || string.IsNullOrWhiteSpace(userId)) return errorResponse!;

            var (staff, outletId, _, resolveError) = await ResolveStaffAndShiftAsync(req, userId);
            if (staff == null || !string.IsNullOrWhiteSpace(resolveError))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = resolveError ?? "Staff profile not linked" });
                return badReq;
            }

            var (request, validationError) = await ValidationHelper.ValidateBody<CreateMyLeaveRequestDto>(req);
            if (validationError != null) return validationError;
            if (request == null)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Invalid request body" });
                return badReq;
            }

            if (request.EndDate.Date < request.StartDate.Date)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "End date must be same or after start date" });
                return badReq;
            }

            if (request.IsHalfDay && request.EndDate.Date != request.StartDate.Date)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Half-day leave must be for a single date" });
                return badReq;
            }

            var leave = new LeaveRequest
            {
                OutletId = outletId,
                StaffId = staff.Id!,
                StaffName = $"{staff.FirstName} {staff.LastName}".Trim(),
                LeaveType = InputSanitizer.Sanitize(request.LeaveType),
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                IsHalfDay = request.IsHalfDay,
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
            _log.LogError(ex, "Error creating self leave request");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("GetMyLeaveRequests")]
    public async Task<HttpResponseData> GetMyLeaveRequests(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "attendance/my/leave")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized || string.IsNullOrWhiteSpace(userId)) return errorResponse!;

            var (staff, outletId, _, resolveError) = await ResolveStaffAndShiftAsync(req, userId);
            if (staff == null || !string.IsNullOrWhiteSpace(resolveError))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = resolveError ?? "Staff profile not linked" });
                return badReq;
            }

            var status = req.Query["status"];
            var leaves = await _mongo.GetLeaveRequestsAsync(outletId, status);
            var mine = leaves
                .Where(l => l.StaffId == staff.Id)
                .OrderByDescending(l => l.CreatedAt)
                .ToList();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(mine);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting self leave requests");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("GetMyMonthlyLeaveBalance")]
    public async Task<HttpResponseData> GetMyMonthlyLeaveBalance(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "attendance/my/leave-balance")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized || string.IsNullOrWhiteSpace(userId)) return errorResponse!;

            var (staff, outletId, _, resolveError) = await ResolveStaffAndShiftAsync(req, userId);
            if (staff == null || !string.IsNullOrWhiteSpace(resolveError) || string.IsNullOrWhiteSpace(staff.Id))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = resolveError ?? "Staff profile not linked" });
                return badReq;
            }

            var monthParam = req.Query["month"];
            var now = ResolveClientNow(req);
            var targetMonth = ParseMonthOrDefault(monthParam, now);
            var monthStart = new DateTime(targetMonth.Year, targetMonth.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);

            var leaves = await _mongo.GetLeaveRequestsAsync(outletId, null);
            var myMonthLeaves = leaves
                .Where(l => l.StaffId == staff.Id)
                .Where(l => l.EndDate.Date >= monthStart.Date && l.StartDate.Date <= monthEnd.Date)
                .ToList();

            var approved = myMonthLeaves.Where(l => string.Equals(l.Status, "approved", StringComparison.OrdinalIgnoreCase)).ToList();
            var pending = myMonthLeaves.Where(l => string.Equals(l.Status, "pending", StringComparison.OrdinalIgnoreCase)).ToList();

            var approvedSick = SumOverlapDaysByType(approved, "sick", monthStart, monthEnd);
            var approvedCasual = SumOverlapDaysByType(approved, "casual", monthStart, monthEnd);
            var approvedEarned = SumOverlapDaysByType(approved, "earned", monthStart, monthEnd);
            var approvedUnpaid = SumOverlapDaysByType(approved, "unpaid", monthStart, monthEnd);

            var pendingSick = SumOverlapDaysByType(pending, "sick", monthStart, monthEnd);
            var pendingCasual = SumOverlapDaysByType(pending, "casual", monthStart, monthEnd);
            var pendingEarned = SumOverlapDaysByType(pending, "earned", monthStart, monthEnd);
            var pendingUnpaid = SumOverlapDaysByType(pending, "unpaid", monthStart, monthEnd);

            var monthlySickQuota = GetMonthlyQuota(staff.SickLeaveBalance);
            var monthlyCasualQuota = GetMonthlyQuota(staff.CasualLeaveBalance);
            var monthlyEarnedQuota = GetMonthlyQuota(staff.AnnualLeaveBalance);

            var sickApplied = approvedSick + pendingSick;
            var casualApplied = approvedCasual + pendingCasual;
            var earnedApplied = approvedEarned + pendingEarned;

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                period = targetMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                from = monthStart,
                to = monthEnd,
                balances = new
                {
                    sick = new
                    {
                        available = monthlySickQuota,
                        annualBalance = staff.SickLeaveBalance,
                        approvedThisMonth = approvedSick,
                        pendingThisMonth = pendingSick,
                        appliedThisMonth = sickApplied,
                        remainingAfterMonthUsage = Math.Max(0, monthlySickQuota - sickApplied)
                    },
                    casual = new
                    {
                        available = monthlyCasualQuota,
                        annualBalance = staff.CasualLeaveBalance,
                        approvedThisMonth = approvedCasual,
                        pendingThisMonth = pendingCasual,
                        appliedThisMonth = casualApplied,
                        remainingAfterMonthUsage = Math.Max(0, monthlyCasualQuota - casualApplied)
                    },
                    earned = new
                    {
                        available = monthlyEarnedQuota,
                        annualBalance = staff.AnnualLeaveBalance,
                        approvedThisMonth = approvedEarned,
                        pendingThisMonth = pendingEarned,
                        appliedThisMonth = earnedApplied,
                        remainingAfterMonthUsage = Math.Max(0, monthlyEarnedQuota - earnedApplied)
                    },
                    unpaid = new
                    {
                        approvedThisMonth = approvedUnpaid,
                        pendingThisMonth = pendingUnpaid
                    }
                },
                totals = new
                {
                    approvedThisMonth = approvedSick + approvedCasual + approvedEarned + approvedUnpaid,
                    pendingThisMonth = pendingSick + pendingCasual + pendingEarned + pendingUnpaid
                }
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting monthly leave balance");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while fetching monthly leave balance" });
            return res;
        }
    }

    [Function("GetMyPayslip")]
    public async Task<HttpResponseData> GetMyPayslip(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "attendance/my/payslip")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized || string.IsNullOrWhiteSpace(userId)) return errorResponse!;

            var (staff, _, _, resolveError) = await ResolveStaffAndShiftAsync(req, userId);
            if (staff == null || !string.IsNullOrWhiteSpace(resolveError) || string.IsNullOrWhiteSpace(staff.Id))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = resolveError ?? "Staff profile not linked" });
                return badReq;
            }

            var monthParam = req.Query["month"];
            var now = ResolveClientNow(req);
            var targetMonth = ParseMonthOrDefault(monthParam, now);
            var monthStart = new DateTime(targetMonth.Year, targetMonth.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);

            var entries = await _mongo.GetDailyPerformanceByStaffAsync(
                staff.Id,
                monthStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                monthEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            var workedHours = entries.Sum(e => e.WorkingHours);
            var workedDays = entries.Count(e => e.WorkingHours > 0);

            var bonusRecords = await _mongo.GetStaffPerformanceRecordsAsync(staff.Id, targetMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture));
            var bonusAmount = bonusRecords.Where(r => r.IsCalculated).Sum(r => r.NetAmount);

            var bonusConfigs = await _mongo.GetBonusConfigurationsForStaffAsync(staff);
            var eligibleForBonus = bonusConfigs.Any();

            var isCurrentMonth = targetMonth.Year == now.Year && targetMonth.Month == now.Month;
            var effectiveDay = isCurrentMonth ? now.Day : DateTime.DaysInMonth(targetMonth.Year, targetMonth.Month);
            var daysInMonth = DateTime.DaysInMonth(targetMonth.Year, targetMonth.Month);

            var baseEarnings = CalculateBaseEarnings(staff, workedHours, workedDays, effectiveDay, daysInMonth, isCurrentMonth);
            var estimatedTotalEarnings = baseEarnings + bonusAmount;

            var history = await BuildPayslipHistoryAsync(staff, now, targetMonth);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                staff = new
                {
                    staff.Id,
                    name = $"{staff.FirstName} {staff.LastName}".Trim(),
                    staff.Position,
                    staff.Salary,
                    staff.SalaryType,
                    employeeId = staff.EmployeeId
                },
                current = new
                {
                    period = targetMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                    from = monthStart,
                    to = isCurrentMonth ? now.Date : monthEnd,
                    isEstimated = isCurrentMonth,
                    workedHours = Math.Round(workedHours, 2),
                    workedDays,
                    baseEarnings = Math.Round(baseEarnings, 2),
                    bonusAmount = Math.Round(bonusAmount, 2),
                    estimatedTotalEarnings = Math.Round(estimatedTotalEarnings, 2),
                    eligibleForBonus,
                    bonusConfigurations = bonusConfigs.Select(c => c.ConfigurationName).ToList()
                },
                history
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting self payslip");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while fetching payslip" });
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

            var (request, validationError) = await ValidationHelper.ValidateBody<UpdateLeaveStatusRequest>(req);
            if (validationError != null) return validationError;
            if (request == null)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Invalid request body" });
                return badReq;
            }

            var validStatuses = new[] { "approved", "rejected" };
            var normalizedStatus = request.Status.ToLowerInvariant();
            if (!validStatuses.Contains(normalizedStatus))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Status must be 'approved' or 'rejected'" });
                return badReq;
            }

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth) ?? "default";
            var leaves = await _mongo.GetLeaveRequestsAsync(outletId, null);
            var leave = leaves.FirstOrDefault(l => string.Equals(l.Id, id, StringComparison.Ordinal));
            if (leave == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Leave request not found" });
                return notFound;
            }

            if (leave.Status.Equals("approved", StringComparison.OrdinalIgnoreCase) && normalizedStatus == "approved")
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Leave request is already approved" });
                return badReq;
            }

            if (normalizedStatus == "approved")
            {
                var deductionResult = await TryDeductLeaveBalanceForApprovalAsync(req, leave);
                if (deductionResult != null)
                {
                    return deductionResult;
                }
            }

            var success = await _mongo.UpdateLeaveRequestStatusAsync(id, normalizedStatus);

            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Leave request not found" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = $"Leave request {request.Status}", deductedUnits = normalizedStatus == "approved" ? GetLeaveUnits(leave.StartDate, leave.EndDate, leave.IsHalfDay) : 0 });
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

    private async Task<(Staff? staff, string outletId, (double scheduledHours, string shiftLabel, List<string> shiftNames, List<(string name, string startTime, string endTime, int breakDuration, double hours, DateTime startAt, DateTime endAt)> shifts) shiftInfo, string? error)>
        ResolveStaffAndShiftAsync(HttpRequestData req, string userId)
    {
        var user = await _users.GetUserByIdAsync(userId);
        if (user == null)
            return (null, string.Empty, EmptyShiftInfo(), "User not found");

        var staff = await _mongo.GetStaffByUserIdAsync(userId);
        if (staff?.Id == null)
        {
            staff = await _mongo.GetStaffByEmailAsync(user.Email);
            if (staff?.Id != null && string.IsNullOrWhiteSpace(staff.UserId))
            {
                await _mongo.LinkStaffToUserAsync(staff.Id, userId);
                staff.UserId = userId;
            }
        }

        if (staff?.Id == null)
            return (null, string.Empty, EmptyShiftInfo(), "No staff profile mapped to this account");

        var outletId = ResolveValidOutletId(req, user, staff);
        if (string.IsNullOrWhiteSpace(outletId))
            return (null, string.Empty, EmptyShiftInfo(), "No valid outlet assigned for this staff account");

        var shiftInfo = ResolveTodayShiftInfoDetailed(staff, outletId);

        return (staff, outletId, shiftInfo, null);
    }

    private string ResolveValidOutletId(HttpRequestData req, User user, Staff staff)
    {
        var candidate = OutletHelper.GetOutletIdForAdmin(req, _auth) ?? user.DefaultOutletId;
        if (IsValidObjectId(candidate)) return candidate!;

        var fromStaff = (staff.OutletIds ?? new List<string>()).FirstOrDefault(IsValidObjectId);
        return fromStaff ?? string.Empty;
    }

    private static bool IsValidObjectId(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && ObjectId.TryParse(value, out _);
    }

    private static (double scheduledHours, string shiftLabel, List<string> shiftNames, List<(string name, string startTime, string endTime, int breakDuration, double hours, DateTime startAt, DateTime endAt)> shifts) EmptyShiftInfo()
    {
        return (0, "No shift configured", new List<string>(), new List<(string, string, string, int, double, DateTime, DateTime)>());
    }

    private static (double scheduledHours, string shiftLabel, List<string> shiftNames, List<(string name, string startTime, string endTime, int breakDuration, double hours, DateTime startAt, DateTime endAt)> shifts)
        ResolveTodayShiftInfoDetailed(Staff staff, string outletId)
    {
        var now = MongoService.GetIstNow();
        var today = now.DayOfWeek.ToString();

        var rawShifts = (staff.Shifts ?? new List<StaffShift>())
            .Where(s => s.IsActive)
            .Where(s => string.Equals(s.DayOfWeek, today, StringComparison.OrdinalIgnoreCase))
            .Where(s => string.IsNullOrWhiteSpace(s.OutletId) || s.OutletId == outletId)
            .ToList();

        if (!rawShifts.Any())
        {
            return (0, "No shift configured", new List<string>(), new List<(string, string, string, int, double, DateTime, DateTime)>());
        }

        var detailed = new List<(string name, string startTime, string endTime, int breakDuration, double hours, DateTime startAt, DateTime endAt)>();

        foreach (var shift in rawShifts)
        {
            var name = string.IsNullOrWhiteSpace(shift.ShiftName) ? shift.DayOfWeek : shift.ShiftName.Trim();
            var startTime = shift.StartTime ?? "00:00";
            var endTime = shift.EndTime ?? "00:00";
            var breakDuration = Math.Max(0, shift.BreakDuration);
            var hours = CalculateShiftHours(shift.StartTime, shift.EndTime, shift.BreakDuration);

            var startTs = TimeSpan.TryParse(startTime, out var st) ? st : TimeSpan.Zero;
            var endTs = TimeSpan.TryParse(endTime, out var et) ? et : TimeSpan.Zero;

            var startAt = now.Date.Add(startTs);
            var endAt = now.Date.Add(endTs);
            if (endAt <= startAt)
            {
                endAt = endAt.AddDays(1);
            }

            detailed.Add((name, startTime, endTime, breakDuration, Math.Round(hours, 2), startAt, endAt));
        }

        detailed = detailed.OrderBy(s => s.startAt).ToList();
        var scheduledHours = Math.Round(detailed.Sum(s => s.hours), 2);
        var shiftNames = detailed.Select(s => s.name).ToList();
        var shiftLabel = string.Join(" + ", shiftNames);

        return (scheduledHours, shiftLabel, shiftNames, detailed);
    }

    private static (bool CanShiftIn, bool CanShiftOut, string Message, string? CurrentShiftName, string? CurrentShiftKey, string? CurrentShiftStart, string? CurrentShiftEnd, double CurrentShiftScheduledHours)
        BuildShiftActionState((double scheduledHours, string shiftLabel, List<string> shiftNames, List<(string name, string startTime, string endTime, int breakDuration, double hours, DateTime startAt, DateTime endAt)> shifts) shiftInfo,
            Attendance? attendance,
            DateTime now)
    {
        if (!shiftInfo.shifts.Any())
        {
            return (false, false, "No shift configured for today", null, null, null, null, 0);
        }

        var activeAttendance = attendance?.ClockIn.HasValue == true && !attendance.ClockOut.HasValue;
        var completedAttendance = attendance?.ClockIn.HasValue == true && attendance.ClockOut.HasValue;

        var currentShift = shiftInfo.shifts.FirstOrDefault(s => now >= s.startAt && now <= s.endAt);
        var nextShift = shiftInfo.shifts.FirstOrDefault(s => now < s.startAt);
        var pastShift = shiftInfo.shifts.LastOrDefault(s => now > s.endAt);

        if (activeAttendance)
        {
            var clockInAt = attendance!.ClockIn!.Value;
            var assignedShift = shiftInfo.shifts.FirstOrDefault(s => clockInAt >= s.startAt && clockInAt <= s.endAt);
            assignedShift = assignedShift.name == null ? (currentShift.name != null ? currentShift : pastShift) : assignedShift;

            if (assignedShift.name == null)
            {
                return (false, false, "You are clocked in, but no matching shift window was found", null, null, null, null, 0);
            }

            var canOut = now >= assignedShift.endAt;
            var message = canOut
                ? "Shift end is enabled now"
                : $"Shift end will be enabled after {assignedShift.endTime}";

            var shiftKey = BuildShiftKey(assignedShift.name, assignedShift.startTime, assignedShift.endTime);

            return (false, canOut, message, assignedShift.name, shiftKey, assignedShift.startTime, assignedShift.endTime, assignedShift.hours);
        }

        if (completedAttendance)
        {
            return (false, false, "Today's shift is already completed", null, null, null, null, 0);
        }

        if (currentShift.name != null)
        {
            var shiftKey = BuildShiftKey(currentShift.name, currentShift.startTime, currentShift.endTime);
            return (true, false, "Shift start is enabled for current shift window", currentShift.name, shiftKey, currentShift.startTime, currentShift.endTime, currentShift.hours);
        }

        if (nextShift.name != null)
        {
            var shiftKey = BuildShiftKey(nextShift.name, nextShift.startTime, nextShift.endTime);
            return (false, false, $"Shift start will be enabled at {nextShift.startTime}", nextShift.name, shiftKey, nextShift.startTime, nextShift.endTime, nextShift.hours);
        }

        return (false, false, "All configured shift windows for today have ended", null, null, null, null, 0);
    }

    private static string BuildShiftKey(string shiftName, string startTime, string endTime)
    {
        return $"{shiftName.Trim().ToLowerInvariant()}|{startTime}|{endTime}";
    }

    private static double CalculateShiftHours(string? startTime, string? endTime, int breakDurationMinutes)
    {
        if (!TimeSpan.TryParse(startTime, out var start) || !TimeSpan.TryParse(endTime, out var end))
        {
            return 8;
        }

        if (end <= start)
        {
            end = end.Add(TimeSpan.FromDays(1));
        }

        var total = (end - start).TotalHours - (Math.Max(0, breakDurationMinutes) / 60.0);
        return Math.Max(0, total);
    }

    private static DateTime ParseMonthOrDefault(string? month, DateTime fallback)
    {
        if (!string.IsNullOrWhiteSpace(month)
            && DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return new DateTime(parsed.Year, parsed.Month, 1);
        }

        return new DateTime(fallback.Year, fallback.Month, 1);
    }

    private static DateTime ResolveClientNow(HttpRequestData req)
    {
        var headers = req.Headers;

        if (!TryGetSingleHeader(headers, "x-client-epoch-ms", out var epochRaw)
            || !long.TryParse(epochRaw, out var epochMs))
        {
            return MongoService.GetIstNow();
        }

        try
        {
            var utc = DateTimeOffset.FromUnixTimeMilliseconds(epochMs).UtcDateTime;

            if (TryGetSingleHeader(headers, "x-client-timezone-offset-minutes", out var offsetRaw)
                && int.TryParse(offsetRaw, out var offsetMinutes)
                && offsetMinutes >= -840
                && offsetMinutes <= 840)
            {
                var local = utc - TimeSpan.FromMinutes(offsetMinutes);
                return DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            }

            return DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);
        }
        catch
        {
            return MongoService.GetIstNow();
        }
    }

    private static bool TryGetSingleHeader(HttpHeadersCollection headers, string key, out string value)
    {
        value = string.Empty;
        if (!headers.TryGetValues(key, out var values))
        {
            return false;
        }

        value = values.FirstOrDefault() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static decimal CalculateBaseEarnings(Staff staff, double workedHours, int workedDays, int effectiveDay, int daysInMonth, bool isCurrentMonth)
    {
        var salaryType = (staff.SalaryType ?? "Monthly").Trim().ToLowerInvariant();
        return salaryType switch
        {
            "hourly" => staff.Salary * (decimal)workedHours,
            "daily" => staff.Salary * workedDays,
            _ => isCurrentMonth
                ? (staff.Salary / daysInMonth) * effectiveDay
                : staff.Salary
        };
    }

    private async Task<HttpResponseData?> TryDeductLeaveBalanceForApprovalAsync(HttpRequestData req, LeaveRequest leave)
    {
        var staff = await _mongo.GetStaffByIdAsync(leave.StaffId);
        if (staff == null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Staff linked to leave request was not found" });
            return notFound;
        }

        var unitsToDeduct = GetLeaveUnits(leave.StartDate, leave.EndDate, leave.IsHalfDay);
        var leaveType = leave.LeaveType.Trim().ToLowerInvariant();

        var annual = staff.AnnualLeaveBalance;
        var sick = staff.SickLeaveBalance;
        var casual = staff.CasualLeaveBalance;

        switch (leaveType)
        {
            case "earned":
                if (annual < unitsToDeduct)
                    return await CreateInsufficientBalanceResponse(req, "earned", annual, unitsToDeduct);
                annual = RoundLeaveBalance(annual - unitsToDeduct);
                break;
            case "sick":
                if (sick < unitsToDeduct)
                    return await CreateInsufficientBalanceResponse(req, "sick", sick, unitsToDeduct);
                sick = RoundLeaveBalance(sick - unitsToDeduct);
                break;
            case "casual":
                if (casual < unitsToDeduct)
                    return await CreateInsufficientBalanceResponse(req, "casual", casual, unitsToDeduct);
                casual = RoundLeaveBalance(casual - unitsToDeduct);
                break;
            case "unpaid":
                return null;
            default:
                return null;
        }

        var success = await _mongo.UpdateStaffLeaveBalancesAsync(
            leave.StaffId,
            annual,
            sick,
            casual,
            "leave-approval");

        if (!success)
        {
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to update staff leave balance during approval" });
            return error;
        }

        return null;
    }

    private static async Task<HttpResponseData> CreateInsufficientBalanceResponse(HttpRequestData req, string leaveType, double available, double requested)
    {
        var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
        await badReq.WriteAsJsonAsync(new
        {
            error = $"Insufficient {leaveType} leave balance",
            available,
            requested
        });
        return badReq;
    }

    private static double SumOverlapDaysByType(IEnumerable<LeaveRequest> leaves, string leaveType, DateTime monthStart, DateTime monthEnd)
    {
        return leaves
            .Where(l => string.Equals(l.LeaveType, leaveType, StringComparison.OrdinalIgnoreCase))
            .Sum(l => GetOverlappingUnits(l, monthStart, monthEnd));
    }

    private static double GetOverlappingUnits(LeaveRequest leave, DateTime monthStart, DateTime monthEnd)
    {
        var start = leave.StartDate.Date > monthStart.Date ? leave.StartDate.Date : monthStart.Date;
        var end = leave.EndDate.Date < monthEnd.Date ? leave.EndDate.Date : monthEnd.Date;
        if (end < start)
        {
            return 0;
        }

        if (leave.IsHalfDay)
        {
            return 0.5;
        }

        return (end - start).Days + 1;
    }

    private static double GetLeaveUnits(DateTime startDate, DateTime endDate, bool isHalfDay)
    {
        if (isHalfDay)
        {
            return 0.5;
        }

        var days = (endDate.Date - startDate.Date).Days + 1;
        return Math.Max(0, days);
    }

    private static double RoundLeaveBalance(double balance)
    {
        return Math.Round(Math.Max(0, balance), 2);
    }

    private static double GetMonthlyQuota(double annualBalance)
    {
        if (annualBalance <= 0)
        {
            return 0;
        }

        return Math.Round(annualBalance / 12.0, 2);
    }

    private async Task<List<PayslipHistoryEntry>> BuildPayslipHistoryAsync(Staff staff, DateTime now, DateTime currentPeriod)
    {
        var history = new List<PayslipHistoryEntry>();

        for (var i = 1; i <= 12; i++)
        {
            var month = currentPeriod.AddMonths(-i);
            if (month > now)
            {
                continue;
            }

            var monthStart = new DateTime(month.Year, month.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);

            var entries = await _mongo.GetDailyPerformanceByStaffAsync(
                staff.Id!,
                monthStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                monthEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            var workedHours = entries.Sum(e => e.WorkingHours);
            var workedDays = entries.Count(e => e.WorkingHours > 0);

            var bonusRecords = await _mongo.GetStaffPerformanceRecordsAsync(staff.Id!, month.ToString("yyyy-MM", CultureInfo.InvariantCulture));
            var bonusAmount = bonusRecords.Where(r => r.IsCalculated).Sum(r => r.NetAmount);

            if (workedHours <= 0 && bonusAmount <= 0)
            {
                continue;
            }

            var baseEarnings = CalculateBaseEarnings(staff, workedHours, workedDays, DateTime.DaysInMonth(month.Year, month.Month), DateTime.DaysInMonth(month.Year, month.Month), false);
            history.Add(new PayslipHistoryEntry
            {
                Period = month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                WorkedHours = Math.Round(workedHours, 2),
                WorkedDays = workedDays,
                BaseEarnings = Math.Round(baseEarnings, 2),
                BonusAmount = Math.Round(bonusAmount, 2),
                TotalEarnings = Math.Round(baseEarnings + bonusAmount, 2)
            });
        }

        return history.OrderByDescending(x => x.Period).ToList();
    }
}

public class PayslipHistoryEntry
{
    public string Period { get; set; } = string.Empty;
    public double WorkedHours { get; set; }
    public int WorkedDays { get; set; }
    public decimal BaseEarnings { get; set; }
    public decimal BonusAmount { get; set; }
    public decimal TotalEarnings { get; set; }
}

public class UpdateLeaveStatusRequest
{
    public string Status { get; set; } = string.Empty;
}

public class CreateMyLeaveRequestDto
{
    [Required]
    [AllowedValuesList("earned")]
    public string LeaveType { get; set; } = string.Empty;

    [Required]
    public DateTime StartDate { get; set; }

    [Required]
    public DateTime EndDate { get; set; }

    public bool IsHalfDay { get; set; }

    [Required]
    [StringLength(500)]
    public string Reason { get; set; } = string.Empty;
}
