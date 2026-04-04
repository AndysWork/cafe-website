using MongoDB.Driver;
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

    #region Customer Wallet

    public async Task<CustomerWallet?> GetWalletAsync(string userId)
    {
        return await _customerWallets.Find(w => w.UserId == userId).FirstOrDefaultAsync();
    }

    public async Task<CustomerWallet> GetOrCreateWalletAsync(string userId)
    {
        var wallet = await GetWalletAsync(userId);
        if (wallet == null)
        {
            wallet = new CustomerWallet { UserId = userId };
            await _customerWallets.InsertOneAsync(wallet);
        }
        return wallet;
    }

    public async Task<WalletTransaction> CreditWalletAsync(string userId, decimal amount, string description, string source, string? referenceId = null, string? razorpayPaymentId = null)
    {
        var wallet = await GetOrCreateWalletAsync(userId);
        var newBalance = wallet.Balance + amount;

        await _customerWallets.UpdateOneAsync(
            w => w.UserId == userId,
            Builders<CustomerWallet>.Update
                .Set(w => w.Balance, newBalance)
                .Inc(w => w.TotalCredited, amount)
                .Set(w => w.UpdatedAt, GetIstNow()));

        var txn = new WalletTransaction
        {
            UserId = userId,
            Type = "credit",
            Amount = amount,
            BalanceAfter = newBalance,
            Description = description,
            Source = source,
            ReferenceId = referenceId,
            RazorpayPaymentId = razorpayPaymentId
        };
        await _walletTransactions.InsertOneAsync(txn);
        return txn;
    }

    public async Task<WalletTransaction?> DebitWalletAsync(string userId, decimal amount, string description, string source, string? referenceId = null)
    {
        var wallet = await GetOrCreateWalletAsync(userId);
        if (wallet.Balance < amount) return null;

        var newBalance = wallet.Balance - amount;
        await _customerWallets.UpdateOneAsync(
            w => w.UserId == userId,
            Builders<CustomerWallet>.Update
                .Set(w => w.Balance, newBalance)
                .Inc(w => w.TotalDebited, amount)
                .Set(w => w.UpdatedAt, GetIstNow()));

        var txn = new WalletTransaction
        {
            UserId = userId,
            Type = "debit",
            Amount = amount,
            BalanceAfter = newBalance,
            Description = description,
            Source = source,
            ReferenceId = referenceId
        };
        await _walletTransactions.InsertOneAsync(txn);
        return txn;
    }

    public async Task<List<WalletTransaction>> GetWalletTransactionsAsync(string userId, int page = 1, int pageSize = 20)
    {
        return await _walletTransactions.Find(t => t.UserId == userId)
            .SortByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
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

    public async Task<Attendance?> GetTodayAttendanceAsync(string staffId, string outletId)
    {
        var today = GetIstNow().Date;
        return await _attendance.Find(a =>
            a.StaffId == staffId && a.OutletId == outletId && a.Date == today)
            .FirstOrDefaultAsync();
    }

    public async Task<List<Attendance>> GetAllTodayAttendanceAsync(string outletId)
    {
        var today = GetIstNow().Date;
        return await _attendance.Find(a => a.OutletId == outletId && a.Date == today)
            .ToListAsync();
    }

    public async Task<Attendance> ClockInAsync(string staffId, string staffName, string outletId)
    {
        var now = GetIstNow();
        var existing = await GetTodayAttendanceAsync(staffId, outletId);
        if (existing != null && existing.ClockIn.HasValue)
            return existing;

        var attendance = existing ?? new Attendance
        {
            StaffId = staffId,
            StaffName = staffName,
            OutletId = outletId,
            Date = now.Date
        };

        attendance.ClockIn = now;
        attendance.Status = "present";

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

    public async Task<Attendance?> ClockOutAsync(string staffId, string outletId)
    {
        var existing = await GetTodayAttendanceAsync(staffId, outletId);
        if (existing == null || !existing.ClockIn.HasValue || existing.ClockOut.HasValue)
            return existing;

        var now = GetIstNow();
        existing.ClockOut = now;
        existing.HoursWorked = (now - existing.ClockIn.Value).TotalHours;

        await _attendance.ReplaceOneAsync(a => a.Id == existing.Id, existing);
        return existing;
    }

    public async Task<List<Attendance>> GetAttendanceByDateRangeAsync(string outletId, DateTime start, DateTime end, string? staffId = null)
    {
        var filterBuilder = Builders<Attendance>.Filter;
        var filter = filterBuilder.Eq(a => a.OutletId, outletId)
            & filterBuilder.Gte(a => a.Date, start.Date)
            & filterBuilder.Lte(a => a.Date, end.Date);

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
        var filter = filterBuilder.Eq(r => r.OutletId, outletId);
        if (!string.IsNullOrEmpty(status))
            filter &= filterBuilder.Eq(r => r.Status, status);

        return await _leaveRequests.Find(filter)
            .SortByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<bool> UpdateLeaveRequestStatusAsync(string id, string status, string? approvedBy = null)
    {
        var update = Builders<LeaveRequest>.Update
            .Set(r => r.Status, status);
        if (approvedBy != null)
            update = update.Set(r => r.ApprovedBy, approvedBy);

        var result = await _leaveRequests.UpdateOneAsync(r => r.Id == id, update);
        return result.ModifiedCount > 0;
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
        return await _deliveryPartners.Find(p => p.OutletId == outletId && p.IsActive)
            .SortBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<DeliveryPartner?> GetAvailableDeliveryPartnerAsync(string outletId)
    {
        return await _deliveryPartners.Find(p =>
            p.OutletId == outletId && p.IsActive && p.Status == "available")
            .SortByDescending(p => p.Rating)
            .FirstOrDefaultAsync();
    }

    public async Task<bool> AssignDeliveryPartnerAsync(string partnerId, string orderId)
    {
        var update = Builders<DeliveryPartner>.Update
            .Set(p => p.Status, "on-delivery")
            .Set(p => p.CurrentOrderId, orderId);
        var result = await _deliveryPartners.UpdateOneAsync(p => p.Id == partnerId, update);

        if (result.ModifiedCount > 0)
        {
            await _orders.UpdateOneAsync(
                o => o.Id == orderId,
                Builders<Order>.Update
                    .Set(o => o.DeliveryPartnerId, partnerId));
        }
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
