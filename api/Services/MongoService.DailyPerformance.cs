using MongoDB.Driver;
using Cafe.Api.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Cafe.Api.Services;

public partial class MongoService
{
    #region Daily Performance Entry Methods

    public async Task<List<DailyPerformanceEntry>> GetDailyPerformanceByDateAsync(string date, string outletId)
    {
        var filter = Builders<DailyPerformanceEntry>.Filter.And(
            Builders<DailyPerformanceEntry>.Filter.Eq(e => e.Date, date),
            Builders<DailyPerformanceEntry>.Filter.Eq(e => e.OutletId, outletId)
        );

        var entries = await _dailyPerformanceEntries.Find(filter).ToListAsync();

        // Batch populate staff names (fixes N+1 query)
        await PopulateStaffNamesAsync(entries);

        return entries;
    }

    public async Task<List<DailyPerformanceEntry>> GetDailyPerformanceByStaffAsync(
        string staffId,
        string? startDate = null,
        string? endDate = null)
    {
        var filterBuilder = Builders<DailyPerformanceEntry>.Filter;
        var filters = new List<FilterDefinition<DailyPerformanceEntry>>
        {
            filterBuilder.Eq(e => e.StaffId, staffId)
        };

        if (!string.IsNullOrEmpty(startDate))
        {
            filters.Add(filterBuilder.Gte(e => e.Date, startDate));
        }

        if (!string.IsNullOrEmpty(endDate))
        {
            filters.Add(filterBuilder.Lte(e => e.Date, endDate));
        }

        var filter = filterBuilder.And(filters);
        var entries = await _dailyPerformanceEntries.Find(filter)
            .SortByDescending(e => e.Date)
            .ToListAsync();

        // Populate staff name
        if (entries.Count > 0)
        {
            var staff = await GetStaffByIdAsync(staffId);
            if (staff != null)
            {
                var staffName = $"{staff.FirstName} {staff.LastName}";
                foreach (var entry in entries)
                {
                    entry.StaffName = staffName;
                }
            }
        }

        return entries;
    }

    public async Task<List<DailyPerformanceEntry>> GetDailyPerformanceByDateRangeAsync(
        string startDate,
        string endDate,
        string outletId)
    {
        var filter = Builders<DailyPerformanceEntry>.Filter.And(
            Builders<DailyPerformanceEntry>.Filter.Gte(e => e.Date, startDate),
            Builders<DailyPerformanceEntry>.Filter.Lte(e => e.Date, endDate),
            Builders<DailyPerformanceEntry>.Filter.Eq(e => e.OutletId, outletId)
        );

        var entries = await _dailyPerformanceEntries.Find(filter)
            .SortBy(e => e.Date)
            .ThenBy(e => e.StaffId)
            .ToListAsync();

        // Batch populate staff names (fixes N+1 query)
        await PopulateStaffNamesAsync(entries);

        return entries;
    }

    public async Task<DailyPerformanceEntry> UpsertDailyPerformanceAsync(
        UpsertDailyPerformanceRequest request,
        string outletId)
    {
        // Find existing entry for this staff on this date
        var filter = Builders<DailyPerformanceEntry>.Filter.And(
            Builders<DailyPerformanceEntry>.Filter.Eq(e => e.StaffId, request.StaffId),
            Builders<DailyPerformanceEntry>.Filter.Eq(e => e.Date, request.Date),
            Builders<DailyPerformanceEntry>.Filter.Eq(e => e.OutletId, outletId)
        );

        var existingEntry = await _dailyPerformanceEntries.Find(filter).FirstOrDefaultAsync();

        // Fetch staff name to save in database
        var staff = await GetStaffByIdAsync(request.StaffId);
        var staffName = staff != null ? $"{staff.FirstName} {staff.LastName}" : null;

        // Calculate individual shift working hours if shifts are provided
        if (request.Shifts != null && request.Shifts.Any())
        {
            foreach (var shift in request.Shifts)
            {
                shift.WorkingHours = CalculateWorkingHours(shift.InTime, shift.OutTime);
            }
        }

        // Calculate total working hours:
        // If shifts exist, sum their working hours; otherwise use main InTime/OutTime
        var workingHours = (request.Shifts != null && request.Shifts.Any())
            ? request.Shifts.Sum(s => s.WorkingHours)
            : CalculateWorkingHours(request.InTime, request.OutTime);

        if (existingEntry != null)
        {
            // Update existing entry
            existingEntry.StaffName = staffName;
            existingEntry.InTime = request.InTime;
            existingEntry.OutTime = request.OutTime;
            existingEntry.WorkingHours = workingHours;
            existingEntry.TotalOrdersPrepared = request.TotalOrdersPrepared;
            existingEntry.GoodOrdersCount = request.GoodOrdersCount;
            existingEntry.BadOrdersCount = request.BadOrdersCount;
            existingEntry.RefundAmountRecovery = request.RefundAmountRecovery;
            existingEntry.Notes = request.Notes;
            existingEntry.LeaveHours = request.LeaveHours;
            existingEntry.UpdatedAt = DateTime.UtcNow;
            
            // Update shifts if provided (including empty array to clear all shifts)
            if (request.Shifts != null)
            {
                existingEntry.Shifts = request.Shifts;
            }

            await _dailyPerformanceEntries.ReplaceOneAsync(
                e => e.Id == existingEntry.Id,
                existingEntry
            );

            return existingEntry;
        }
        else
        {
            // Create new entry
            var newEntry = new DailyPerformanceEntry
            {
                OutletId = outletId,
                StaffId = request.StaffId,
                StaffName = staffName,
                Date = request.Date,
                InTime = request.InTime,
                OutTime = request.OutTime,
                WorkingHours = workingHours,
                TotalOrdersPrepared = request.TotalOrdersPrepared,
                GoodOrdersCount = request.GoodOrdersCount,
                BadOrdersCount = request.BadOrdersCount,
                RefundAmountRecovery = request.RefundAmountRecovery,
                Notes = request.Notes,
                LeaveHours = request.LeaveHours,
                Shifts = request.Shifts ?? new List<PerformanceShift>(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _dailyPerformanceEntries.InsertOneAsync(newEntry);

            return newEntry;
        }
    }

    public async Task<List<DailyPerformanceEntry>> BulkUpsertDailyPerformanceAsync(
        BulkDailyPerformanceRequest request,
        string outletId)
    {
        var tasks = request.Entries.Select(entryRequest =>
        {
            var upsertRequest = new UpsertDailyPerformanceRequest
            {
                StaffId = entryRequest.StaffId,
                Date = request.Date,
                InTime = entryRequest.InTime,
                OutTime = entryRequest.OutTime,
                TotalOrdersPrepared = entryRequest.TotalOrdersPrepared,
                GoodOrdersCount = entryRequest.GoodOrdersCount,
                BadOrdersCount = entryRequest.BadOrdersCount,
                RefundAmountRecovery = entryRequest.RefundAmountRecovery,
                Notes = entryRequest.Notes,
                LeaveHours = entryRequest.LeaveHours,
                Shifts = entryRequest.Shifts
            };

            return UpsertDailyPerformanceAsync(upsertRequest, outletId);
        });

        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    public async Task<bool> DeleteDailyPerformanceAsync(string id)
    {
        var result = await _dailyPerformanceEntries.DeleteOneAsync(e => e.Id == id);
        return result.DeletedCount > 0;
    }

    /// <summary>
    /// Batch populate staff names for a list of performance entries.
    /// Uses a single DB query instead of N+1 individual queries.
    /// </summary>
    private async Task PopulateStaffNamesAsync(List<DailyPerformanceEntry> entries)
    {
        if (entries.Count == 0) return;

        var staffIds = entries.Select(e => e.StaffId).Distinct().ToList();
        var staffFilter = Builders<Staff>.Filter.In(s => s.Id, staffIds);
        var staffMembers = await _staff.Find(staffFilter)
            .Project(Builders<Staff>.Projection
                .Include(s => s.Id)
                .Include(s => s.FirstName)
                .Include(s => s.LastName))
            .As<Staff>()
            .ToListAsync();

        var staffMap = staffMembers.ToDictionary(
            s => s.Id!,
            s => $"{s.FirstName} {s.LastName}"
        );

        foreach (var entry in entries)
        {
            if (staffMap.TryGetValue(entry.StaffId, out var name))
            {
                entry.StaffName = name;
            }
        }
    }

    private double CalculateWorkingHours(string inTime, string outTime)
    {
        try
        {
            var inParts = inTime.Split(':');
            var outParts = outTime.Split(':');

            if (inParts.Length != 2 || outParts.Length != 2)
            {
                return 0;
            }

            var inHours = int.Parse(inParts[0]);
            var inMinutes = int.Parse(inParts[1]);
            var outHours = int.Parse(outParts[0]);
            var outMinutes = int.Parse(outParts[1]);

            var inTotalMinutes = inHours * 60 + inMinutes;
            var outTotalMinutes = outHours * 60 + outMinutes;

            var diffMinutes = outTotalMinutes - inTotalMinutes;

            // Handle overnight shifts
            if (diffMinutes < 0)
            {
                diffMinutes += 24 * 60;
            }

            return Math.Round(diffMinutes / 60.0, 2);
        }
        catch
        {
            return 0;
        }
    }

    #endregion

    #region Performance Shift Management

    public async Task<PerformanceShift> AddPerformanceShiftAsync(string entryId, PerformanceShift shift)
    {
        var entry = await _dailyPerformanceEntries.Find(e => e.Id == entryId).FirstOrDefaultAsync();
        if (entry == null)
        {
            throw new Exception("Daily performance entry not found");
        }

        shift.Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
        shift.WorkingHours = CalculateWorkingHours(shift.InTime, shift.OutTime);

        if (entry.Shifts == null)
        {
            entry.Shifts = new List<PerformanceShift>();
        }

        entry.Shifts.Add(shift);
        entry.UpdatedAt = DateTime.UtcNow;

        await _dailyPerformanceEntries.ReplaceOneAsync(e => e.Id == entryId, entry);

        return shift;
    }

    public async Task<PerformanceShift> UpdatePerformanceShiftAsync(string entryId, string shiftId, PerformanceShift updatedShift)
    {
        var entry = await _dailyPerformanceEntries.Find(e => e.Id == entryId).FirstOrDefaultAsync();
        if (entry == null)
        {
            throw new Exception("Daily performance entry not found");
        }

        var shift = entry.Shifts?.FirstOrDefault(s => s.Id == shiftId);
        if (shift == null)
        {
            throw new Exception("Shift not found");
        }

        shift.ShiftName = updatedShift.ShiftName;
        shift.InTime = updatedShift.InTime;
        shift.OutTime = updatedShift.OutTime;
        shift.WorkingHours = CalculateWorkingHours(updatedShift.InTime, updatedShift.OutTime);
        shift.TotalOrdersPrepared = updatedShift.TotalOrdersPrepared;
        shift.GoodOrdersCount = updatedShift.GoodOrdersCount;
        shift.BadOrdersCount = updatedShift.BadOrdersCount;
        shift.RefundAmountRecovery = updatedShift.RefundAmountRecovery;
        shift.Notes = updatedShift.Notes;

        entry.UpdatedAt = DateTime.UtcNow;

        await _dailyPerformanceEntries.ReplaceOneAsync(e => e.Id == entryId, entry);

        return shift;
    }

    public async Task<bool> DeletePerformanceShiftAsync(string entryId, string shiftId)
    {
        var entry = await _dailyPerformanceEntries.Find(e => e.Id == entryId).FirstOrDefaultAsync();
        if (entry == null)
        {
            return false;
        }

        var shift = entry.Shifts?.FirstOrDefault(s => s.Id == shiftId);
        if (shift == null)
        {
            return false;
        }

        entry.Shifts?.Remove(shift);
        entry.UpdatedAt = DateTime.UtcNow;

        await _dailyPerformanceEntries.ReplaceOneAsync(e => e.Id == entryId, entry);

        return true;
    }

    public async Task<List<PerformanceShift>> GetPerformanceShiftsAsync(string entryId)
    {
        var entry = await _dailyPerformanceEntries.Find(e => e.Id == entryId).FirstOrDefaultAsync();
        if (entry == null)
        {
            throw new Exception("Daily performance entry not found");
        }

        return entry.Shifts ?? new List<PerformanceShift>();
    }
}
    #endregion
