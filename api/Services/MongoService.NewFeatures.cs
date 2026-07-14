using MongoDB.Driver;
using MongoDB.Bson;
using Cafe.Api.Models;
using Cafe.Api.Repositories;

namespace Cafe.Api.Services;

public partial class MongoService : IOperationsRepository
{
    #region Delivery Zones

    public async Task<List<DeliveryZone>> GetDeliveryZonesAsync(string outletId)
    {
        return await _deliveryZones.Find(z => z.OutletId == outletId && z.IsDeleted != true)
            .SortBy(z => z.MinDistance)
            .ToListAsync();
    }

    public async Task<List<DeliveryZone>> GetActiveDeliveryZonesAsync(string outletId)
    {
        return await _deliveryZones.Find(z => z.OutletId == outletId && z.IsActive && z.IsDeleted != true)
            .SortBy(z => z.MinDistance)
            .ToListAsync();
    }

    public async Task<DeliveryZone?> GetDeliveryZoneByIdAsync(string id)
    {
        return await _deliveryZones.Find(z => z.Id == id && z.IsDeleted != true).FirstOrDefaultAsync();
    }

    public async Task<DeliveryZone> CreateDeliveryZoneAsync(DeliveryZone zone)
    {
        await _deliveryZones.InsertOneAsync(zone);
        return zone;
    }

    public async Task<bool> UpdateDeliveryZoneAsync(string id, DeliveryZone zone)
    {
        var update = Builders<DeliveryZone>.Update
            .Set(z => z.ZoneName, zone.ZoneName)
            .Set(z => z.MinDistance, zone.MinDistance)
            .Set(z => z.MaxDistance, zone.MaxDistance)
            .Set(z => z.DeliveryFee, zone.DeliveryFee)
            .Set(z => z.FreeDeliveryAbove, zone.FreeDeliveryAbove)
            .Set(z => z.EstimatedMinutes, zone.EstimatedMinutes)
            .Set(z => z.IsActive, zone.IsActive);
        var result = await _deliveryZones.UpdateOneAsync(z => z.Id == id, update);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> DeleteDeliveryZoneAsync(string id)
    {
        var update = Builders<DeliveryZone>.Update
            .Set(z => z.IsDeleted, true)
            .Set(z => z.DeletedAt, DateTime.UtcNow);
        var result = await _deliveryZones.UpdateOneAsync(z => z.Id == id && z.IsDeleted != true, update);
        return result.ModifiedCount > 0;
    }

    public async Task<decimal> CalculateDeliveryFeeAsync(string outletId, decimal orderSubtotal)
    {
        var zones = await GetActiveDeliveryZonesAsync(outletId);
        if (zones.Count == 0) return 0;

        // Use the first (closest) zone as default
        var zone = zones.First();
        if (zone.FreeDeliveryAbove.HasValue && orderSubtotal >= zone.FreeDeliveryAbove.Value)
            return 0;

        return zone.DeliveryFee;
    }

    #endregion

    #region Table Reservations

    public async Task<TableReservation> CreateReservationAsync(TableReservation reservation)
    {
        await _tableReservations.InsertOneAsync(reservation);
        return reservation;
    }

    public async Task<List<TableReservation>> GetReservationsAsync(string outletId, DateTime? date = null, int page = 1, int pageSize = 50)
    {
        var filterBuilder = Builders<TableReservation>.Filter;
        var filter = filterBuilder.Eq(r => r.OutletId, outletId);

        if (date.HasValue)
        {
            var startOfDay = date.Value.Date;
            var endOfDay = startOfDay.AddDays(1);
            filter &= filterBuilder.Gte(r => r.ReservationDate, startOfDay) &
                      filterBuilder.Lt(r => r.ReservationDate, endOfDay);
        }

        return await _tableReservations.Find(filter)
            .SortByDescending(r => r.ReservationDate)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();
    }

    public async Task<TableReservation?> GetReservationByIdAsync(string id)
    {
        return await _tableReservations.Find(r => r.Id == id).FirstOrDefaultAsync();
    }

    public async Task<bool> UpdateReservationStatusAsync(string id, string status)
    {
        var update = Builders<TableReservation>.Update
            .Set(r => r.Status, status)
            .Set(r => r.UpdatedAt, GetIstNow());
        var result = await _tableReservations.UpdateOneAsync(r => r.Id == id, update);
        return result.ModifiedCount > 0;
    }

    public async Task<List<TableReservation>> GetUserReservationsAsync(string userId)
    {
        return await _tableReservations.Find(r => r.UserId == userId)
            .SortByDescending(r => r.ReservationDate)
            .ToListAsync();
    }

    #endregion

    #region Wastage Records

    public async Task<WastageRecord> CreateWastageRecordAsync(WastageRecord record)
    {
        await _wastageRecords.InsertOneAsync(record);
        return record;
    }

    public async Task<List<WastageRecord>> GetWastageRecordsAsync(string outletId, DateTime? startDate = null, DateTime? endDate = null, int page = 1, int pageSize = 50)
    {
        var filterBuilder = Builders<WastageRecord>.Filter;
        var filter = filterBuilder.Eq(r => r.OutletId, outletId);

        if (startDate.HasValue)
            filter &= filterBuilder.Gte(r => r.Date, startDate.Value);
        if (endDate.HasValue)
            filter &= filterBuilder.Lte(r => r.Date, endDate.Value);

        return await _wastageRecords.Find(filter)
            .SortByDescending(r => r.Date)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();
    }

    public async Task<WastageSummary> GetWastageSummaryAsync(string outletId, DateTime startDate, DateTime endDate)
    {
        var records = await _wastageRecords.Find(r =>
            r.OutletId == outletId && r.Date >= startDate && r.Date <= endDate)
            .ToListAsync();

        var byReason = records.GroupBy(r => r.Reason)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.TotalValue));

        return new WastageSummary
        {
            TotalWastageValue = records.Sum(r => r.TotalValue),
            TotalRecords = records.Count,
            ByReason = byReason,
            Records = records
        };
    }

    #endregion

    #region Attendance & Leave

    public async Task<Attendance?> GetTodayAttendanceAsync(string staffId, string outletId, DateTime? referenceNow = null)
    {
        var today = (referenceNow ?? GetIstNow()).Date;
        var filterBuilder = Builders<Attendance>.Filter;
        var filter = filterBuilder.Eq(a => a.StaffId, staffId) & filterBuilder.Eq(a => a.Date, today);

        // OutletId is represented as ObjectId in Attendance. Avoid passing placeholders like "default".
        if (!string.IsNullOrWhiteSpace(outletId) && ObjectId.TryParse(outletId, out _))
        {
            filter &= filterBuilder.Eq(a => a.OutletId, outletId);
        }

        return await _attendance.Find(filter).FirstOrDefaultAsync();
    }

    public async Task<List<Attendance>> GetAllTodayAttendanceAsync(string outletId)
    {
        var today = GetIstNow().Date;
        var hasValidOutletId = !string.IsNullOrWhiteSpace(outletId)
            && !string.Equals(outletId, "default", StringComparison.OrdinalIgnoreCase)
            && MongoDB.Bson.ObjectId.TryParse(outletId, out _);

        var filterBuilder = Builders<Attendance>.Filter;
        var filter = filterBuilder.Eq(a => a.Date, today);

        if (hasValidOutletId)
        {
            filter &= filterBuilder.Or(
                filterBuilder.Eq(a => a.OutletId, outletId),
                filterBuilder.Exists(a => a.OutletId, false),
                filterBuilder.Eq(a => a.OutletId, null));
        }

        return await _attendance.Find(filter)
            .ToListAsync();
    }

    public async Task<Attendance> ClockInAsync(
        string staffId,
        string staffName,
        string outletId,
        double? scheduledHours = null,
        string? scheduledShiftLabel = null,
        string? shiftKey = null,
        string? shiftStartTime = null,
        string? shiftEndTime = null,
        DateTime? clockInAt = null)
    {
        var now = clockInAt ?? GetIstNow();
        var existing = await GetTodayAttendanceAsync(staffId, outletId, now);

        var attendance = existing ?? new Attendance
        {
            StaffId = staffId,
            StaffName = staffName,
            OutletId = outletId,
            Date = now.Date
        };

        attendance.StaffName = string.IsNullOrWhiteSpace(attendance.StaffName) ? staffName : attendance.StaffName;
        attendance.Sessions ??= new List<AttendanceSession>();

        var normalizedShiftKey = string.IsNullOrWhiteSpace(shiftKey)
            ? $"{now:yyyy-MM-dd}|ad-hoc|{attendance.Sessions.Count + 1}"
            : shiftKey.Trim();

        var openSession = attendance.Sessions
            .Where(s => s.ClockIn.HasValue && !s.ClockOut.HasValue)
            .OrderByDescending(s => s.ClockIn)
            .FirstOrDefault();

        // Allow only one active session at a time for a staff member.
        if (openSession != null)
        {
            return attendance;
        }

        var existingSession = attendance.Sessions
            .FirstOrDefault(s => string.Equals(s.ShiftKey, normalizedShiftKey, StringComparison.OrdinalIgnoreCase));

        if (existingSession != null)
        {
            // Shift already recorded for today (in-progress or completed); do not duplicate.
            return attendance;
        }

        var sessionScheduledHours = Math.Round(Math.Max(0, scheduledHours ?? 0), 2);
        var sessionName = string.IsNullOrWhiteSpace(scheduledShiftLabel) ? "Shift" : scheduledShiftLabel.Trim();

        attendance.Sessions.Add(new AttendanceSession
        {
            ShiftKey = normalizedShiftKey,
            ShiftName = sessionName,
            ShiftStartTime = shiftStartTime,
            ShiftEndTime = shiftEndTime,
            ClockIn = now,
            ScheduledHours = sessionScheduledHours,
            Status = "in-progress"
        });

        RecalculateAttendanceAggregate(attendance);

        if (existing != null)
        {
            await _attendance.ReplaceOneAsync(a => a.Id == existing.Id, attendance);
        }
        else
        {
            await _attendance.InsertOneAsync(attendance);
        }
        return attendance;
    }

    public async Task<Attendance?> ClockOutAsync(
        string staffId,
        string outletId,
        double? scheduledHours = null,
        string? scheduledShiftLabel = null,
        string? shiftKey = null,
        DateTime? clockOutAt = null)
    {
        var now = NormalizeToIstWallClock(clockOutAt ?? GetIstNow());
        var existing = await GetTodayAttendanceAsync(staffId, outletId, now);
        if (existing == null)
            return existing;

        existing.Sessions ??= new List<AttendanceSession>();

        var targetSession = !string.IsNullOrWhiteSpace(shiftKey)
            ? existing.Sessions.FirstOrDefault(s =>
                string.Equals(s.ShiftKey, shiftKey.Trim(), StringComparison.OrdinalIgnoreCase)
                && s.ClockIn.HasValue
                && !s.ClockOut.HasValue)
            : existing.Sessions
                .Where(s => s.ClockIn.HasValue && !s.ClockOut.HasValue)
                .OrderByDescending(s => s.ClockIn)
                .FirstOrDefault();

        if (targetSession == null)
            return existing;

        var clockInAt = NormalizeToIstWallClock(targetSession.ClockIn!.Value);
        if (now < clockInAt)
        {
            now = clockInAt;
        }

        targetSession.ClockOut = now;
        targetSession.HoursWorked = Math.Round(Math.Max(0, (now - clockInAt).TotalHours), 2);

        if (scheduledHours.HasValue)
        {
            targetSession.ScheduledHours = Math.Round(Math.Max(0, scheduledHours.Value), 2);
        }
        if (!string.IsNullOrWhiteSpace(scheduledShiftLabel))
        {
            targetSession.ShiftName = scheduledShiftLabel.Trim();
        }

        var baselineHours = targetSession.ScheduledHours;
        targetSession.OvertimeHours = Math.Round(Math.Max(0, targetSession.HoursWorked - baselineHours), 2);
        targetSession.UndertimeHours = Math.Round(Math.Max(0, baselineHours - targetSession.HoursWorked), 2);
        targetSession.Status = "completed";

        RecalculateAttendanceAggregate(existing);

        await _attendance.ReplaceOneAsync(a => a.Id == existing.Id, existing);
        return existing;
    }

    private static DateTime NormalizeToIstWallClock(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc)
        {
            return DateTime.SpecifyKind(ConvertToIst(value), DateTimeKind.Unspecified);
        }

        if (value.Kind == DateTimeKind.Local)
        {
            var ist = TimeZoneInfo.ConvertTime(value, IstTimeZone);
            return DateTime.SpecifyKind(ist, DateTimeKind.Unspecified);
        }

        return DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
    }

    private static void RecalculateAttendanceAggregate(Attendance attendance)
    {
        attendance.Sessions ??= new List<AttendanceSession>();

        var completed = attendance.Sessions
            .Where(s => s.ClockIn.HasValue && s.ClockOut.HasValue)
            .ToList();

        var inProgress = attendance.Sessions
            .Where(s => s.ClockIn.HasValue && !s.ClockOut.HasValue)
            .ToList();

        attendance.ClockIn = attendance.Sessions
            .Where(s => s.ClockIn.HasValue)
            .OrderBy(s => s.ClockIn)
            .Select(s => s.ClockIn)
            .FirstOrDefault();

        attendance.ClockOut = inProgress.Any()
            ? null
            : completed
                .OrderByDescending(s => s.ClockOut)
                .Select(s => s.ClockOut)
                .FirstOrDefault();

        attendance.HoursWorked = Math.Round(completed.Sum(s => s.HoursWorked), 2);
        attendance.ScheduledHours = Math.Round(attendance.Sessions.Sum(s => s.ScheduledHours), 2);
        attendance.OvertimeHours = Math.Round(completed.Sum(s => s.OvertimeHours), 2);
        attendance.UndertimeHours = Math.Round(completed.Sum(s => s.UndertimeHours), 2);

        var labels = attendance.Sessions
            .Where(s => !string.IsNullOrWhiteSpace(s.ShiftName))
            .Select(s => s.ShiftName.Trim())
            .Distinct()
            .ToList();
        attendance.ScheduledShiftLabel = labels.Any() ? string.Join(" + ", labels) : null;

        attendance.Status = attendance.Sessions.Any(s => s.ClockIn.HasValue) ? "present" : "absent";
    }

    public async Task<List<Attendance>> GetAttendanceByDateRangeAsync(string outletId, DateTime start, DateTime end, string? staffId = null)
    {
        var filterBuilder = Builders<Attendance>.Filter;
        var hasValidOutletId = !string.IsNullOrWhiteSpace(outletId)
            && !string.Equals(outletId, "default", StringComparison.OrdinalIgnoreCase)
            && MongoDB.Bson.ObjectId.TryParse(outletId, out _);

        var filter = filterBuilder.Gte(a => a.Date, start.Date)
            & filterBuilder.Lte(a => a.Date, end.Date);

        if (hasValidOutletId)
        {
            filter &= filterBuilder.Or(
                filterBuilder.Eq(a => a.OutletId, outletId),
                filterBuilder.Exists(a => a.OutletId, false),
                filterBuilder.Eq(a => a.OutletId, null));
        }

        if (!string.IsNullOrEmpty(staffId))
            filter &= filterBuilder.Eq(a => a.StaffId, staffId);

        return await _attendance.Find(filter).SortByDescending(a => a.Date).ToListAsync();
    }

    public async Task<List<AttendanceSummary>> GetAttendanceSummaryAsync(string outletId, DateTime start, DateTime end)
    {
        var records = await GetAttendanceByDateRangeAsync(outletId, start, end);
        return records.GroupBy(a => a.StaffId)
            .Select(g => new AttendanceSummary
            {
                StaffId = g.Key,
                StaffName = g.First().StaffName,
                PresentDays = g.Count(a => a.Status == "present"),
                AbsentDays = g.Count(a => a.Status == "absent"),
                LateDays = g.Count(a => a.Status == "late"),
                LeaveDays = g.Count(a => a.Status == "leave"),
                TotalHoursWorked = g.Sum(a => a.HoursWorked)
            })
            .ToList();
    }

    public async Task<LeaveRequest> CreateLeaveRequestAsync(LeaveRequest request)
    {
        await _leaveRequests.InsertOneAsync(request);
        return request;
    }

    public async Task<List<LeaveRequest>> GetLeaveRequestsAsync(string outletId, string? status = null)
    {
        var filterBuilder = Builders<LeaveRequest>.Filter;
        var hasValidOutletId = !string.IsNullOrWhiteSpace(outletId)
            && !string.Equals(outletId, "default", StringComparison.OrdinalIgnoreCase)
            && MongoDB.Bson.ObjectId.TryParse(outletId, out _);

        FilterDefinition<LeaveRequest> filter;
        if (hasValidOutletId)
        {
            // OutletId is stored as ObjectId-represented string, so only valid ObjectId values can be used here.
            // Also include legacy records where outletId might be missing/null.
            filter = filterBuilder.Or(
                filterBuilder.Eq(r => r.OutletId, outletId),
                filterBuilder.Exists(r => r.OutletId, false),
                filterBuilder.Eq(r => r.OutletId, null));
        }
        else
        {
            filter = filterBuilder.Empty;
        }

        if (!string.IsNullOrEmpty(status))
            filter &= filterBuilder.Eq(r => r.Status, status);

        return await _leaveRequests.Find(filter)
            .SortByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<LeaveRequest?> GetLeaveRequestByIdAsync(string id)
    {
        return await _leaveRequests.Find(r => r.Id == id).FirstOrDefaultAsync();
    }

    public async Task<bool> UpdateLeaveRequestStatusAsync(string id, string status, string? approvedBy = null)
    {
        var update = Builders<LeaveRequest>.Update
            .Set(r => r.Status, status);
        if (approvedBy != null)
            update = update.Set(r => r.ApprovedBy, approvedBy);

        var result = await _leaveRequests.UpdateOneAsync(r => r.Id == id, update);
        return result.MatchedCount > 0;
    }

    public async Task<bool> DeleteLeaveRequestAsync(string id)
    {
        var result = await _leaveRequests.DeleteOneAsync(r => r.Id == id);
        return result.DeletedCount > 0;
    }

    #endregion

    #region Combo Meals

    public async Task<ComboMeal> CreateComboMealAsync(ComboMeal combo)
    {
        await _comboMeals.InsertOneAsync(combo);
        return combo;
    }

    public async Task<List<ComboMeal>> GetComboMealsAsync(string outletId, bool activeOnly = false)
    {
        var filter = Builders<ComboMeal>.Filter.Eq(c => c.OutletId, outletId) & Builders<ComboMeal>.Filter.Ne(c => c.IsDeleted, true);
        if (activeOnly)
            filter &= Builders<ComboMeal>.Filter.Eq(c => c.IsActive, true);

        return await _comboMeals.Find(filter)
            .SortByDescending(c => c.CreatedAt)
            .ToListAsync();
    }

    public async Task<ComboMeal?> GetComboMealByIdAsync(string id)
    {
        return await _comboMeals.Find(c => c.Id == id && c.IsDeleted != true).FirstOrDefaultAsync();
    }

    public async Task<bool> UpdateComboMealAsync(string id, ComboMeal combo)
    {
        combo.UpdatedAt = GetIstNow();
        var result = await _comboMeals.ReplaceOneAsync(c => c.Id == id, combo);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> DeleteComboMealAsync(string id)
    {
        var update = Builders<ComboMeal>.Update
            .Set(c => c.IsDeleted, true)
            .Set(c => c.DeletedAt, DateTime.UtcNow);
        var result = await _comboMeals.UpdateOneAsync(c => c.Id == id && c.IsDeleted != true, update);
        return result.ModifiedCount > 0;
    }

    #endregion

    #region Happy Hour Rules

    public async Task<HappyHourRule> CreateHappyHourRuleAsync(HappyHourRule rule)
    {
        await _happyHourRules.InsertOneAsync(rule);
        return rule;
    }

    public async Task<List<HappyHourRule>> GetHappyHourRulesAsync(string outletId)
    {
        return await _happyHourRules.Find(r => r.OutletId == outletId)
            .SortByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<HappyHourRule?> GetHappyHourRuleByIdAsync(string id)
    {
        return await _happyHourRules.Find(r => r.Id == id).FirstOrDefaultAsync();
    }

    public async Task<List<HappyHourRule>> GetActiveHappyHoursAsync(string outletId)
    {
        var now = GetIstNow();
        var currentTime = now.ToString("HH:mm");
        var dayOfWeek = (int)now.DayOfWeek;

        var rules = await _happyHourRules.Find(r =>
            r.OutletId == outletId && r.IsActive).ToListAsync();

        return rules.Where(r =>
            r.DaysOfWeek.Contains(dayOfWeek) &&
            string.Compare(currentTime, r.StartTime, StringComparison.Ordinal) >= 0 &&
            string.Compare(currentTime, r.EndTime, StringComparison.Ordinal) <= 0)
            .ToList();
    }

    public async Task<bool> UpdateHappyHourRuleAsync(string id, HappyHourRule rule)
    {
        rule.UpdatedAt = GetIstNow();
        var result = await _happyHourRules.ReplaceOneAsync(r => r.Id == id, rule);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> DeleteHappyHourRuleAsync(string id)
    {
        var result = await _happyHourRules.DeleteOneAsync(r => r.Id == id);
        return result.DeletedCount > 0;
    }

    #endregion

    #region Purchase Orders

    public async Task<PurchaseOrder> CreatePurchaseOrderAsync(PurchaseOrder po)
    {
        po.PoNumber = $"PO-{GetIstNow():yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpper()}";
        await _purchaseOrders.InsertOneAsync(po);
        return po;
    }

    public async Task<List<PurchaseOrder>> GetPurchaseOrdersAsync(string outletId, string? status = null, int page = 1, int pageSize = 50)
    {
        var filterBuilder = Builders<PurchaseOrder>.Filter;
        var filter = filterBuilder.Eq(p => p.OutletId, outletId);
        if (!string.IsNullOrEmpty(status))
            filter &= filterBuilder.Eq(p => p.Status, status);

        return await _purchaseOrders.Find(filter)
            .SortByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();
    }

    public async Task<bool> UpdatePurchaseOrderStatusAsync(string id, string status)
    {
        var update = Builders<PurchaseOrder>.Update
            .Set(p => p.Status, status)
            .Set(p => p.UpdatedAt, GetIstNow());
        var result = await _purchaseOrders.UpdateOneAsync(p => p.Id == id, update);
        return result.ModifiedCount > 0;
    }

    public async Task<List<PurchaseOrder>> GenerateAutoReorderPurchaseOrdersAsync(string outletId)
    {
        var lowStockItems = await _inventory.Find(i =>
            i.OutletId == outletId && i.CurrentStock <= i.MinimumStock && i.CurrentStock > 0)
            .ToListAsync();

        var orders = new List<PurchaseOrder>();
        foreach (var item in lowStockItems)
        {
            var existingPo = await _purchaseOrders.Find(p =>
                p.IngredientId == item.IngredientId && p.OutletId == outletId &&
                (p.Status == "pending" || p.Status == "approved" || p.Status == "ordered"))
                .FirstOrDefaultAsync();

            if (existingPo != null) continue;

            var ingredient = await _ingredients.Find(i => i.Id == item.IngredientId && i.IsDeleted != true).FirstOrDefaultAsync();
            if (ingredient == null) continue;

            var po = new PurchaseOrder
            {
                OutletId = outletId,
                IngredientId = item.IngredientId!,
                IngredientName = ingredient.Name,
                SupplierName = item.SupplierName ?? "Unknown",
                SupplierContact = item.SupplierContact,
                Quantity = item.ReorderQuantity > 0 ? item.ReorderQuantity : item.MaximumStock - item.CurrentStock,
                Unit = ingredient.Unit,
                EstimatedCost = (item.ReorderQuantity > 0 ? item.ReorderQuantity : item.MaximumStock - item.CurrentStock) * (item.LastPurchasePrice ?? 0),
                IsAutoGenerated = true
            };
            orders.Add(await CreatePurchaseOrderAsync(po));
        }
        return orders;
    }

    #endregion

    #region Subscription Plans

    public async Task<SubscriptionPlan> CreateSubscriptionPlanAsync(SubscriptionPlan plan)
    {
        await _subscriptionPlans.InsertOneAsync(plan);
        return plan;
    }

    public async Task<List<SubscriptionPlan>> GetSubscriptionPlansAsync(string outletId, bool activeOnly = false)
    {
        var filter = Builders<SubscriptionPlan>.Filter.Eq(p => p.OutletId, outletId) & Builders<SubscriptionPlan>.Filter.Ne(p => p.IsDeleted, true);
        if (activeOnly)
            filter &= Builders<SubscriptionPlan>.Filter.Eq(p => p.IsActive, true);

        return await _subscriptionPlans.Find(filter).SortBy(p => p.Price).ToListAsync();
    }

    public async Task<SubscriptionPlan?> GetSubscriptionPlanByIdAsync(string id)
    {
        return await _subscriptionPlans.Find(p => p.Id == id && p.IsDeleted != true).FirstOrDefaultAsync();
    }

    public async Task<bool> UpdateSubscriptionPlanAsync(string id, SubscriptionPlan plan)
    {
        var result = await _subscriptionPlans.ReplaceOneAsync(
            p => p.Id == id && p.IsDeleted != true, plan);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> DeleteSubscriptionPlanAsync(string id)
    {
        // Check for active customer subscriptions on this plan
        var hasSubscribers = await _customerSubscriptions.Find(s => s.PlanId == id && s.Status == "active").AnyAsync();
        if (hasSubscribers)
            throw new InvalidOperationException("Cannot delete subscription plan: it has active subscribers. Deactivate it instead.");

        var update = Builders<SubscriptionPlan>.Update
            .Set(p => p.IsDeleted, true)
            .Set(p => p.DeletedAt, DateTime.UtcNow)
            .Set(p => p.IsActive, false);
        var result = await _subscriptionPlans.UpdateOneAsync(p => p.Id == id && p.IsDeleted != true, update);
        return result.ModifiedCount > 0;
    }

    public async Task<CustomerSubscription> CreateCustomerSubscriptionAsync(CustomerSubscription sub)
    {
        await _customerSubscriptions.InsertOneAsync(sub);
        return sub;
    }

    public async Task<CustomerSubscription?> GetActiveSubscriptionAsync(string userId)
    {
        return await _customerSubscriptions.Find(s =>
            s.UserId == userId && s.Status == "active" && s.EndDate >= GetIstNow())
            .FirstOrDefaultAsync();
    }

    public async Task<List<CustomerSubscription>> GetUserSubscriptionsAsync(string userId)
    {
        return await _customerSubscriptions.Find(s => s.UserId == userId)
            .SortByDescending(s => s.CreatedAt)
            .ToListAsync();
    }

    #endregion

    #region Delivery Partners

    public async Task<DeliveryPartner> CreateDeliveryPartnerAsync(DeliveryPartner partner)
    {
        await _deliveryPartners.InsertOneAsync(partner);
        return partner;
    }

    public async Task<List<DeliveryPartner>> GetDeliveryPartnersAsync(string outletId)
    {
        var hasValidOutletId = !string.IsNullOrWhiteSpace(outletId)
            && !string.Equals(outletId, "default", StringComparison.OrdinalIgnoreCase)
            && ObjectId.TryParse(outletId, out _);

        var filterBuilder = Builders<DeliveryPartner>.Filter;
        var filter = filterBuilder.Eq(p => p.IsActive, true);
        if (hasValidOutletId)
        {
            filter &= filterBuilder.Eq(p => p.OutletId, outletId);
        }

        return await _deliveryPartners.Find(filter)
            .SortBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<DeliveryPartner?> GetDeliveryPartnerByUserIdAsync(string userId)
    {
        return await _deliveryPartners.Find(p => p.UserId == userId && p.IsActive).FirstOrDefaultAsync();
    }

    public async Task<DeliveryPartner?> GetAvailableDeliveryPartnerAsync(string outletId)
    {
        var hasValidOutletId = !string.IsNullOrWhiteSpace(outletId)
            && !string.Equals(outletId, "default", StringComparison.OrdinalIgnoreCase)
            && ObjectId.TryParse(outletId, out _);

        var filterBuilder = Builders<DeliveryPartner>.Filter;
        var filter = filterBuilder.Eq(p => p.IsActive, true)
            & filterBuilder.Eq(p => p.Status, "available");
        if (hasValidOutletId)
        {
            filter &= filterBuilder.Eq(p => p.OutletId, outletId);
        }

        return await _deliveryPartners.Find(filter)
            .SortByDescending(p => p.Rating)
            .FirstOrDefaultAsync();
    }

    public async Task<DeliveryPartner?> GetDeliveryPartnerByIdAsync(string partnerId)
    {
        return await _deliveryPartners.Find(p => p.Id == partnerId && p.IsActive).FirstOrDefaultAsync();
    }

    public async Task<bool> AssignDeliveryPartnerAsync(string partnerId, string orderId)
    {
        var partner = await _deliveryPartners.Find(p => p.Id == partnerId && p.IsActive).FirstOrDefaultAsync();
        if (partner == null)
        {
            return false;
        }

        var update = Builders<DeliveryPartner>.Update
            .Set(p => p.Status, "on-delivery")
            .Set(p => p.CurrentOrderId, orderId);
        var result = await _deliveryPartners.UpdateOneAsync(p => p.Id == partnerId, update);

        if (result.ModifiedCount > 0)
        {
            await _orders.UpdateOneAsync(
                o => o.Id == orderId,
                Builders<Order>.Update
                    .Set(o => o.DeliveryPartnerId, partnerId)
                    .Set(o => o.DeliveryPartnerName, partner.Name)
                    .Set(o => o.UpdatedAt, GetIstNow()));
        }
        return result.ModifiedCount > 0;
    }

    public async Task<bool> TryAssignUnassignedDeliveryPartnerAsync(string partnerId, string orderId)
    {
        var partner = await _deliveryPartners.Find(p => p.Id == partnerId && p.IsActive).FirstOrDefaultAsync();
        if (partner == null)
        {
            return false;
        }

        var assignableStatuses = new[] { "pending", "confirmed", "preparing", "ready" };
        var orderFilter = Builders<Order>.Filter.Eq(o => o.Id, orderId)
            & Builders<Order>.Filter.Ne(o => o.IsDeleted, true)
            & Builders<Order>.Filter.Eq(o => o.OrderType, "delivery")
            & Builders<Order>.Filter.In(o => o.Status, assignableStatuses)
            & Builders<Order>.Filter.Or(
                Builders<Order>.Filter.Eq(o => o.DeliveryPartnerId, null),
                Builders<Order>.Filter.Eq(o => o.DeliveryPartnerId, string.Empty));

        var orderUpdate = Builders<Order>.Update
            .Set(o => o.DeliveryPartnerId, partnerId)
            .Set(o => o.DeliveryPartnerName, partner.Name)
            .Set(o => o.UpdatedAt, GetIstNow());

        var orderResult = await _orders.UpdateOneAsync(orderFilter, orderUpdate);
        if (orderResult.ModifiedCount == 0)
        {
            return false;
        }

        var partnerUpdate = Builders<DeliveryPartner>.Update
            .Set(p => p.Status, "on-delivery")
            .Set(p => p.CurrentOrderId, orderId);
        await _deliveryPartners.UpdateOneAsync(p => p.Id == partnerId && p.IsActive, partnerUpdate);

        return true;
    }

    public async Task<List<Order>> GetActiveOrdersForPartnerAsync(string partnerId, string? outletId = null)
    {
        var statuses = new[] { "confirmed", "preparing", "ready", "out-for-delivery" };
        var filterBuilder = Builders<Order>.Filter;
        var filter = filterBuilder.Eq(o => o.DeliveryPartnerId, partnerId) &
                     filterBuilder.In(o => o.Status, statuses) &
                     filterBuilder.Ne(o => o.IsDeleted, true);

        if (!string.IsNullOrWhiteSpace(outletId))
        {
            filter &= filterBuilder.Eq(o => o.OutletId, outletId);
        }

        return await _orders.Find(filter)
            .SortByDescending(o => o.CreatedAt)
            .ToListAsync();
    }

    public async Task<bool> UpdateDeliveryPartnerLocationAsync(string partnerId, double latitude, double longitude)
    {
        var update = Builders<DeliveryPartner>.Update
            .Set(p => p.CurrentLatitude, latitude)
            .Set(p => p.CurrentLongitude, longitude)
            .Set(p => p.LastLocationUpdatedAt, GetIstNow());

        var result = await _deliveryPartners.UpdateOneAsync(p => p.Id == partnerId && p.IsActive, update);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> CompleteDeliveryAsync(string partnerId)
    {
        var update = Builders<DeliveryPartner>.Update
            .Set(p => p.Status, "available")
            .Set(p => p.CurrentOrderId, (string?)null)
            .Inc(p => p.TotalDeliveries, 1);
        var result = await _deliveryPartners.UpdateOneAsync(p => p.Id == partnerId, update);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> UpdateDeliveryPartnerAsync(string id, DeliveryPartner partner)
    {
        var result = await _deliveryPartners.ReplaceOneAsync(p => p.Id == id, partner);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> DeleteDeliveryPartnerAsync(string id)
    {
        var update = Builders<DeliveryPartner>.Update.Set(p => p.IsActive, false);
        var result = await _deliveryPartners.UpdateOneAsync(p => p.Id == id, update);
        return result.ModifiedCount > 0;
    }

    public async Task<DeliveryShift?> GetActiveShiftForPartnerAsync(string partnerId)
    {
        return await _deliveryShifts.Find(s => s.PartnerId == partnerId && s.Status == "active")
            .SortByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<DeliveryShift> StartPartnerShiftAsync(DeliveryShift shift)
    {
        await _deliveryShifts.InsertOneAsync(shift);
        await _deliveryPartners.UpdateOneAsync(
            p => p.Id == shift.PartnerId,
            Builders<DeliveryPartner>.Update
                .Set(p => p.Status, "available")
                .Set(p => p.LastLocationUpdatedAt, shift.StartedAt));
        return shift;
    }

    public async Task<bool> EndPartnerShiftAsync(string shiftId, decimal endOdometerKm, double? endLatitude, double? endLongitude, string? notes)
    {
        var shift = await _deliveryShifts.Find(s => s.Id == shiftId && s.Status == "active").FirstOrDefaultAsync();
        if (shift == null)
        {
            return false;
        }

        var totalDistance = Math.Max(0, endOdometerKm - shift.StartOdometerKm);
        var now = GetIstNow();

        var update = Builders<DeliveryShift>.Update
            .Set(s => s.EndedAt, now)
            .Set(s => s.EndOdometerKm, endOdometerKm)
            .Set(s => s.EndLatitude, endLatitude)
            .Set(s => s.EndLongitude, endLongitude)
            .Set(s => s.TotalDistanceKm, totalDistance)
            .Set(s => s.Status, "completed")
            .Set(s => s.Notes, notes)
            .Set(s => s.UpdatedAt, now);

        var result = await _deliveryShifts.UpdateOneAsync(s => s.Id == shiftId && s.Status == "active", update);

        if (result.ModifiedCount > 0)
        {
            await _deliveryPartners.UpdateOneAsync(
                p => p.Id == shift.PartnerId,
                Builders<DeliveryPartner>.Update
                    .Set(p => p.Status, "offline")
                    .Set(p => p.CurrentOrderId, (string?)null)
                    .Set(p => p.LastLocationUpdatedAt, now));
        }

        return result.ModifiedCount > 0;
    }

    public async Task<List<DeliveryShift>> GetPartnerShiftsAsync(string partnerId, DateTime? fromDate = null, DateTime? toDate = null, int page = 1, int pageSize = 30)
    {
        var filterBuilder = Builders<DeliveryShift>.Filter;
        var filter = filterBuilder.Eq(s => s.PartnerId, partnerId);

        if (fromDate.HasValue)
        {
            filter &= filterBuilder.Gte(s => s.StartedAt, fromDate.Value);
        }
        if (toDate.HasValue)
        {
            filter &= filterBuilder.Lte(s => s.StartedAt, toDate.Value);
        }

        return await _deliveryShifts.Find(filter)
            .SortByDescending(s => s.StartedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();
    }

    public async Task<PartnerTripLog> CreatePartnerTripAsync(PartnerTripLog trip)
    {
        await _partnerTripLogs.InsertOneAsync(trip);

        await _deliveryShifts.UpdateOneAsync(
            s => s.Id == trip.ShiftId,
            Builders<DeliveryShift>.Update
                .Inc(s => s.TotalDistanceKm, trip.DistanceKm)
                .Set(s => s.UpdatedAt, GetIstNow()));

        return trip;
    }

    public async Task<List<PartnerTripLog>> GetPartnerTripsAsync(string partnerId, DateTime? fromDate = null, DateTime? toDate = null, int page = 1, int pageSize = 100)
    {
        var filterBuilder = Builders<PartnerTripLog>.Filter;
        var filter = filterBuilder.Eq(t => t.PartnerId, partnerId);

        if (fromDate.HasValue)
        {
            filter &= filterBuilder.Gte(t => t.StartedAt, fromDate.Value);
        }
        if (toDate.HasValue)
        {
            filter &= filterBuilder.Lte(t => t.StartedAt, toDate.Value);
        }

        return await _partnerTripLogs.Find(filter)
            .SortByDescending(t => t.StartedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();
    }

    public async Task<decimal> GetPartnerDistanceAsync(string partnerId, DateTime fromDate, DateTime toDate)
    {
        var tripFilterBuilder = Builders<PartnerTripLog>.Filter;
        var tripFilter = tripFilterBuilder.Eq(t => t.PartnerId, partnerId) &
                         tripFilterBuilder.Gte(t => t.StartedAt, fromDate) &
                         tripFilterBuilder.Lte(t => t.StartedAt, toDate);

        var shiftFilterBuilder = Builders<DeliveryShift>.Filter;
        var shiftFilter = shiftFilterBuilder.Eq(s => s.PartnerId, partnerId) &
                          shiftFilterBuilder.Gte(s => s.StartedAt, fromDate) &
                          shiftFilterBuilder.Lte(s => s.StartedAt, toDate);

        var trips = await _partnerTripLogs.Find(tripFilter).ToListAsync();
        var shifts = await _deliveryShifts.Find(shiftFilter).ToListAsync();

        var tripDistance = trips.Sum(t => t.DistanceKm);
        var shiftDistance = shifts.Sum(s => s.TotalDistanceKm);

        // Use the larger source to avoid undercounting when only one source is maintained.
        return Math.Max(tripDistance, shiftDistance);
    }

    public async Task<FuelPriceDaily> UpsertFuelPriceAsync(string outletId, DateTime date, decimal petrolPricePerLitre)
    {
        var normalizedDate = date.Date;
        var now = GetIstNow();
        var filter = Builders<FuelPriceDaily>.Filter.Eq(f => f.OutletId, outletId) &
                     Builders<FuelPriceDaily>.Filter.Eq(f => f.Date, normalizedDate);

        var update = Builders<FuelPriceDaily>.Update
            .Set(f => f.PetrolPricePerLitre, petrolPricePerLitre)
            .Set(f => f.UpdatedAt, now)
            .SetOnInsert(f => f.OutletId, outletId)
            .SetOnInsert(f => f.Date, normalizedDate)
            .SetOnInsert(f => f.CreatedAt, now);

        await _fuelPriceDaily.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true });
        return await _fuelPriceDaily.Find(filter).FirstAsync();
    }

    public async Task<FuelPriceDaily?> GetFuelPriceAsync(string outletId, DateTime date)
    {
        var normalizedDate = date.Date;
        return await _fuelPriceDaily.Find(f => f.OutletId == outletId && f.Date == normalizedDate).FirstOrDefaultAsync();
    }

    public async Task<CODCollectionLog> UpsertCodCollectionAsync(CODCollectionLog codLog)
    {
        var existing = await _codCollectionLogs.Find(c => c.OrderId == codLog.OrderId).FirstOrDefaultAsync();
        if (existing == null)
        {
            await _codCollectionLogs.InsertOneAsync(codLog);
            return codLog;
        }

        var update = Builders<CODCollectionLog>.Update
            .Set(c => c.PartnerId, codLog.PartnerId)
            .Set(c => c.Amount, codLog.Amount)
            .Set(c => c.Collected, codLog.Collected)
            .Set(c => c.CollectionReference, codLog.CollectionReference)
            .Set(c => c.Notes, codLog.Notes)
            .Set(c => c.CollectedAt, codLog.CollectedAt)
            .Set(c => c.ConfirmedByAdmin, codLog.ConfirmedByAdmin);
        await _codCollectionLogs.UpdateOneAsync(c => c.Id == existing.Id, update);

        return await _codCollectionLogs.Find(c => c.Id == existing.Id).FirstAsync();
    }

    public async Task<List<CODCollectionLog>> GetCodCollectionsAsync(string partnerId, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var filterBuilder = Builders<CODCollectionLog>.Filter;
        var filter = filterBuilder.Eq(c => c.PartnerId, partnerId);

        if (fromDate.HasValue)
        {
            filter &= filterBuilder.Gte(c => c.CreatedAt, fromDate.Value);
        }
        if (toDate.HasValue)
        {
            filter &= filterBuilder.Lte(c => c.CreatedAt, toDate.Value);
        }

        return await _codCollectionLogs.Find(filter)
            .SortByDescending(c => c.CreatedAt)
            .ToListAsync();
    }

    public async Task<decimal> GetOutstandingCodAmountAsync(string partnerId)
    {
        var filter = Builders<Order>.Filter.Eq(o => o.DeliveryPartnerId, partnerId)
            & Builders<Order>.Filter.Eq(o => o.OrderType, "delivery")
            & Builders<Order>.Filter.Eq(o => o.PaymentMethod, "cod")
            & Builders<Order>.Filter.Ne(o => o.IsDeleted, true)
            & Builders<Order>.Filter.In(o => o.Status, new[] { "confirmed", "preparing", "ready", "out-for-delivery" });

        var pendingCodOrders = await _orders.Find(filter).ToListAsync();
        if (pendingCodOrders.Count == 0)
        {
            return 0;
        }

        var orderIds = pendingCodOrders
            .Select(o => o.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToList();

        var collectedOrderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (orderIds.Count > 0)
        {
            var collectedLogs = await _codCollectionLogs
                .Find(Builders<CODCollectionLog>.Filter.In(c => c.OrderId, orderIds)
                    & Builders<CODCollectionLog>.Filter.Eq(c => c.Collected, true))
                .Project(c => c.OrderId)
                .ToListAsync();

            foreach (var orderId in collectedLogs)
            {
                if (!string.IsNullOrWhiteSpace(orderId))
                {
                    collectedOrderIds.Add(orderId);
                }
            }
        }

        return pendingCodOrders
            .Where(o => !string.Equals(o.PaymentStatus?.Trim(), "paid", StringComparison.OrdinalIgnoreCase))
            .Where(o => string.IsNullOrWhiteSpace(o.Id) || !collectedOrderIds.Contains(o.Id))
            .Sum(o => o.Total);
    }

    public async Task<DeliveryPartnerReview> AddDeliveryPartnerReviewAsync(DeliveryPartnerReview review)
    {
        await _deliveryPartnerReviews.InsertOneAsync(review);

        var (averageRating, _) = await GetDeliveryPartnerRatingSummaryAsync(review.PartnerId);
        await _deliveryPartners.UpdateOneAsync(
            p => p.Id == review.PartnerId,
            Builders<DeliveryPartner>.Update.Set(p => p.Rating, averageRating));

        return review;
    }

    public async Task<List<DeliveryPartnerReview>> GetDeliveryPartnerReviewsAsync(string partnerId, int limit = 10)
    {
        var safeLimit = Math.Clamp(limit, 1, 50);
        return await _deliveryPartnerReviews.Find(r => r.PartnerId == partnerId)
            .SortByDescending(r => r.CreatedAt)
            .Limit(safeLimit)
            .ToListAsync();
    }

    public async Task<(double averageRating, int totalReviews)> GetDeliveryPartnerRatingSummaryAsync(string partnerId)
    {
        var reviews = await _deliveryPartnerReviews.Find(r => r.PartnerId == partnerId).ToListAsync();
        if (reviews.Count == 0)
        {
            return (0, 0);
        }

        var average = reviews.Average(r => r.Rating);
        return (Math.Round(average, 2), reviews.Count);
    }

    public async Task<ParcelDeliveryTask> CreateParcelTaskAsync(ParcelDeliveryTask task)
    {
        await _parcelDeliveryTasks.InsertOneAsync(task);
        return task;
    }

    public async Task<List<ParcelDeliveryTask>> GetParcelTasksForPartnerAsync(string partnerId, string? status = null, int limit = 100)
    {
        var filter = Builders<ParcelDeliveryTask>.Filter.Eq(t => t.PartnerId, partnerId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            filter &= Builders<ParcelDeliveryTask>.Filter.Eq(t => t.Status, status);
        }

        return await _parcelDeliveryTasks.Find(filter)
            .SortByDescending(t => t.CreatedAt)
            .Limit(Math.Max(1, limit))
            .ToListAsync();
    }

    public async Task<ParcelDeliveryTask?> GetParcelTaskByIdAsync(string taskId)
    {
        return await _parcelDeliveryTasks.Find(t => t.Id == taskId).FirstOrDefaultAsync();
    }

    public async Task<bool> AcceptParcelTaskAsync(string taskId, string partnerId)
    {
        var now = GetIstNow();
        var filter = Builders<ParcelDeliveryTask>.Filter.Eq(t => t.Id, taskId)
            & Builders<ParcelDeliveryTask>.Filter.Eq(t => t.PartnerId, partnerId)
            & Builders<ParcelDeliveryTask>.Filter.Eq(t => t.Status, "assigned");

        var update = Builders<ParcelDeliveryTask>.Update
            .Set(t => t.Status, "accepted")
            .Set(t => t.AcceptedAt, now)
            .Set(t => t.UpdatedAt, now);

        var result = await _parcelDeliveryTasks.UpdateOneAsync(filter, update);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> CompleteParcelTaskAsync(string taskId, string partnerId)
    {
        var now = GetIstNow();
        var filter = Builders<ParcelDeliveryTask>.Filter.Eq(t => t.Id, taskId)
            & Builders<ParcelDeliveryTask>.Filter.Eq(t => t.PartnerId, partnerId)
            & Builders<ParcelDeliveryTask>.Filter.In(t => t.Status, new[] { "assigned", "accepted" });

        var update = Builders<ParcelDeliveryTask>.Update
            .Set(t => t.Status, "completed")
            .Set(t => t.CompletedAt, now)
            .Set(t => t.UpdatedAt, now);

        var result = await _parcelDeliveryTasks.UpdateOneAsync(filter, update);
        return result.ModifiedCount > 0;
    }

    public async Task<PartnerPayoutLedger> CreatePartnerPayoutLedgerAsync(PartnerPayoutLedger ledger)
    {
        var now = GetIstNow();
        ledger.CreatedAt = now;
        ledger.UpdatedAt = now;

        var existing = await GetPartnerPayoutLedgerByPeriodAsync(ledger.PartnerId, ledger.PeriodStart, ledger.PeriodEnd, ledger.PeriodType);
        if (existing == null)
        {
            await _partnerPayoutLedgers.InsertOneAsync(ledger);
            return ledger;
        }

        var update = Builders<PartnerPayoutLedger>.Update
            .Set(p => p.TotalDistanceKm, ledger.TotalDistanceKm)
            .Set(p => p.TotalDeliveries, ledger.TotalDeliveries)
            .Set(p => p.MileageKmpl, ledger.MileageKmpl)
            .Set(p => p.FuelPricePerLitre, ledger.FuelPricePerLitre)
            .Set(p => p.LitresConsumed, ledger.LitresConsumed)
            .Set(p => p.PayoutAmount, ledger.PayoutAmount)
            .Set(p => p.IsFinalized, ledger.IsFinalized)
            .Set(p => p.UpdatedAt, now);

        await _partnerPayoutLedgers.UpdateOneAsync(p => p.Id == existing.Id, update);
        return await _partnerPayoutLedgers.Find(p => p.Id == existing.Id).FirstAsync();
    }

    public async Task<List<PartnerPayoutLedger>> GetPartnerPayoutLedgersAsync(string partnerId, int page = 1, int pageSize = 30)
    {
        return await _partnerPayoutLedgers.Find(p => p.PartnerId == partnerId)
            .SortByDescending(p => p.PeriodStart)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();
    }

    public async Task<PartnerPayoutLedger?> GetPartnerPayoutLedgerByPeriodAsync(string partnerId, DateTime periodStart, DateTime periodEnd, string periodType)
    {
        return await _partnerPayoutLedgers.Find(p =>
                p.PartnerId == partnerId &&
                p.PeriodStart == periodStart &&
                p.PeriodEnd == periodEnd &&
                p.PeriodType == periodType)
            .FirstOrDefaultAsync();
    }

    public async Task<List<Order>> GetKitchenQueueOrdersAsync(string outletId, int limit = 100)
    {
        var statuses = new[] { "pending", "confirmed", "preparing", "ready" };
        return await _orders.Find(o =>
                o.OutletId == outletId
                && o.OrderType != "dine-in"
                && statuses.Contains(o.Status)
                && o.IsDeleted != true)
            .SortBy(o => o.CreatedAt)
            .Limit(Math.Max(1, limit))
            .ToListAsync();
    }

    public async Task<List<Order>> GetDeliveryQueueOrdersAsync(string outletId, int limit = 100)
    {
        var statuses = new[] { "ready", "out-for-delivery" };
        return await _orders.Find(o =>
                o.OutletId == outletId
                && o.OrderType == "delivery"
                && statuses.Contains(o.Status)
                && o.IsDeleted != true)
            .SortBy(o => o.CreatedAt)
            .Limit(Math.Max(1, limit))
            .ToListAsync();
    }

    public async Task<List<ParcelDeliveryTask>> GetParcelTasksByOutletAsync(string outletId, string? status = null, int limit = 200)
    {
        var filter = Builders<ParcelDeliveryTask>.Filter.Eq(t => t.OutletId, outletId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            filter &= Builders<ParcelDeliveryTask>.Filter.Eq(t => t.Status, status);
        }

        return await _parcelDeliveryTasks.Find(filter)
            .SortByDescending(t => t.CreatedAt)
            .Limit(Math.Max(1, limit))
            .ToListAsync();
    }

    public async Task<bool> ReassignOrderPartnerAsync(string orderId, string partnerId)
    {
        var partner = await _deliveryPartners.Find(p => p.Id == partnerId && p.IsActive).FirstOrDefaultAsync();
        if (partner == null)
        {
            return false;
        }

        var now = GetIstNow();
        var assignableStatuses = new[] { "pending", "confirmed", "preparing", "ready", "out-for-delivery" };
        var orderUpdate = Builders<Order>.Update
            .Set(o => o.DeliveryPartnerId, partner.Id)
            .Set(o => o.DeliveryPartnerName, partner.Name)
            .Set(o => o.UpdatedAt, now);

        var result = await _orders.UpdateOneAsync(
            o => o.Id == orderId && assignableStatuses.Contains(o.Status) && o.IsDeleted != true,
            orderUpdate);

        if (result.ModifiedCount == 0)
        {
            return false;
        }

        await _deliveryPartners.UpdateOneAsync(
            p => p.Id == partner.Id,
            Builders<DeliveryPartner>.Update
                .Set(p => p.Status, "on-delivery")
                .Set(p => p.CurrentOrderId, orderId));

        return true;
    }

    public async Task<bool> MarkOrderUrgentAsync(string orderId, bool urgent, string? reason = null)
    {
        var now = GetIstNow();
        var update = Builders<Order>.Update
            .Set(o => o.IsUrgent, urgent)
            .Set(o => o.UrgentReason, string.IsNullOrWhiteSpace(reason) ? null : reason.Trim())
            .Set(o => o.UrgentMarkedAt, urgent ? now : null)
            .Set(o => o.UpdatedAt, now);

        var result = await _orders.UpdateOneAsync(o => o.Id == orderId && o.IsDeleted != true, update);
        return result.ModifiedCount > 0;
    }

    public async Task CreateManagerOpsAuditEntryAsync(ManagerOpsAuditEntry entry)
    {
        entry.CreatedAt = GetIstNow();
        await _managerOpsAuditEntries.InsertOneAsync(entry);
    }

    public async Task<List<ManagerOpsAuditEntry>> GetManagerOpsAuditEntriesAsync(string outletId, DateTime from, DateTime to, int limit = 200)
    {
        return await _managerOpsAuditEntries.Find(a =>
                a.OutletId == outletId &&
                a.CreatedAt >= from &&
                a.CreatedAt < to)
            .SortByDescending(a => a.CreatedAt)
            .Limit(Math.Max(1, limit))
            .ToListAsync();
    }

    public async Task<decimal> GetPayoutLedgerTotalAsync(string outletId, DateTime from, DateTime to)
    {
        var partnerIds = await _deliveryPartners.Find(p => p.OutletId == outletId && p.IsActive)
            .Project(p => p.Id)
            .ToListAsync();

        if (partnerIds.Count == 0)
        {
            return 0;
        }

        var total = await _partnerPayoutLedgers.Find(p =>
                partnerIds.Contains(p.PartnerId) &&
                p.PeriodStart >= from &&
                p.PeriodStart < to)
            .Project(p => (decimal?)p.PayoutAmount)
            .ToListAsync();

        return total.Sum() ?? 0;
    }

    #endregion

    #region Kitchen Orders

    public async Task<List<Order>> GetOrdersByStatusAsync(string[] statuses, string? outletId = null)
    {
        var filterBuilder = Builders<Order>.Filter;
        var filter = filterBuilder.In(o => o.Status, statuses) & filterBuilder.Ne(o => o.IsDeleted, true);
        if (!string.IsNullOrEmpty(outletId))
            filter &= filterBuilder.Eq(o => o.OutletId, outletId);

        return await _orders.Find(filter)
            .SortBy(o => o.CreatedAt)
            .ToListAsync();
    }

    #endregion

    #region Customer Segments

    public async Task<List<CustomerSegment>> GetCustomerSegmentsAsync(string? segment = null, int page = 1, int pageSize = 50)
    {
        var filter = string.IsNullOrEmpty(segment)
            ? Builders<CustomerSegment>.Filter.Empty
            : Builders<CustomerSegment>.Filter.Eq(c => c.Segment, segment);

        return await _customerSegments.Find(filter)
            .SortByDescending(c => c.TotalSpent)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();
    }

    public async Task<List<SegmentSummary>> GetSegmentSummaryAsync()
    {
        var segments = await _customerSegments.Find(_ => true).ToListAsync();
        return segments.GroupBy(s => s.Segment)
            .Select(g => new SegmentSummary
            {
                Segment = g.Key,
                Count = g.Count(),
                TotalRevenue = g.Sum(c => c.TotalSpent),
                AverageOrderValue = g.Average(c => c.AverageOrderValue)
            })
            .OrderByDescending(s => s.TotalRevenue)
            .ToList();
    }

    public async Task<int> RefreshCustomerSegmentsAsync()
    {
        var users = await _users.Find(_ => true).ToListAsync();
        int count = 0;
        foreach (var user in users)
        {
            var orders = await _orders.Find(o => o.UserId == user.Id && o.IsDeleted != true).ToListAsync();
            if (orders.Count == 0) continue;

            var totalSpent = orders.Sum(o => o.Total);
            var avgOrder = totalSpent / orders.Count;
            var lastOrder = orders.Max(o => o.CreatedAt);
            var daysSinceLastOrder = (GetIstNow() - lastOrder).TotalDays;

            string segment;
            if (daysSinceLastOrder > 90) segment = "dormant";
            else if (daysSinceLastOrder > 45) segment = "at-risk";
            else if (orders.Count >= 20 || totalSpent >= 10000) segment = "vip";
            else if (orders.Count >= 5) segment = "regular";
            else segment = "new";

            var favoriteItems = orders
                .SelectMany(o => o.Items)
                .GroupBy(i => i.Name)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => g.Key)
                .ToList();

            var preferredPayment = orders
                .GroupBy(o => o.PaymentMethod)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key;

            var filter = Builders<CustomerSegment>.Filter.Eq(c => c.UserId, user.Id!);
            var update = Builders<CustomerSegment>.Update
                .Set(c => c.UserId, user.Id!)
                .Set(c => c.Username, user.Username ?? "")
                .Set(c => c.Email, user.Email)
                .Set(c => c.Phone, user.PhoneNumber)
                .Set(c => c.Segment, segment)
                .Set(c => c.TotalOrders, orders.Count)
                .Set(c => c.TotalSpent, totalSpent)
                .Set(c => c.AverageOrderValue, avgOrder)
                .Set(c => c.LastOrderDate, lastOrder)
                .Set(c => c.FirstOrderDate, orders.Min(o => o.CreatedAt))
                .Set(c => c.FavoriteItems, favoriteItems)
                .Set(c => c.PreferredPaymentMethod, preferredPayment)
                .Set(c => c.UpdatedAt, GetIstNow());

            await _customerSegments.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true });
            count++;
        }
        return count;
    }

    #endregion
}
