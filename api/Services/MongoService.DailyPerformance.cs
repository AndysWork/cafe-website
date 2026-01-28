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

        // Populate staff names
        foreach (var entry in entries)
        {
            var staff = await GetStaffByIdAsync(entry.StaffId);
            if (staff != null)
            {
                entry.StaffName = $"{staff.FirstName} {staff.LastName}";
            }
        }

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

        // Populate staff names
        var staffIds = entries.Select(e => e.StaffId).Distinct().ToList();
        var staffMap = new Dictionary<string, string>();

        foreach (var staffId in staffIds)
        {
            var staff = await GetStaffByIdAsync(staffId);
            if (staff != null)
            {
                staffMap[staffId] = $"{staff.FirstName} {staff.LastName}";
            }
        }

        foreach (var entry in entries)
        {
            if (staffMap.ContainsKey(entry.StaffId))
            {
                entry.StaffName = staffMap[entry.StaffId];
            }
        }

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

        // Calculate working hours
        var workingHours = CalculateWorkingHours(request.InTime, request.OutTime);

        if (existingEntry != null)
        {
            // Update existing entry
            existingEntry.InTime = request.InTime;
            existingEntry.OutTime = request.OutTime;
            existingEntry.WorkingHours = workingHours;
            existingEntry.TotalOrdersPrepared = request.TotalOrdersPrepared;
            existingEntry.GoodOrdersCount = request.GoodOrdersCount;
            existingEntry.BadOrdersCount = request.BadOrdersCount;
            existingEntry.RefundAmountRecovery = request.RefundAmountRecovery;
            existingEntry.Notes = request.Notes;
            existingEntry.UpdatedAt = DateTime.UtcNow;

            await _dailyPerformanceEntries.ReplaceOneAsync(
                e => e.Id == existingEntry.Id,
                existingEntry
            );

            // Populate staff name
            var staff = await GetStaffByIdAsync(request.StaffId);
            if (staff != null)
            {
                existingEntry.StaffName = $"{staff.FirstName} {staff.LastName}";
            }

            return existingEntry;
        }
        else
        {
            // Create new entry
            var newEntry = new DailyPerformanceEntry
            {
                OutletId = outletId,
                StaffId = request.StaffId,
                Date = request.Date,
                InTime = request.InTime,
                OutTime = request.OutTime,
                WorkingHours = workingHours,
                TotalOrdersPrepared = request.TotalOrdersPrepared,
                GoodOrdersCount = request.GoodOrdersCount,
                BadOrdersCount = request.BadOrdersCount,
                RefundAmountRecovery = request.RefundAmountRecovery,
                Notes = request.Notes,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _dailyPerformanceEntries.InsertOneAsync(newEntry);

            // Populate staff name
            var staff = await GetStaffByIdAsync(request.StaffId);
            if (staff != null)
            {
                newEntry.StaffName = $"{staff.FirstName} {staff.LastName}";
            }

            return newEntry;
        }
    }

    public async Task<List<DailyPerformanceEntry>> BulkUpsertDailyPerformanceAsync(
        BulkDailyPerformanceRequest request, 
        string outletId)
    {
        var results = new List<DailyPerformanceEntry>();

        foreach (var entryRequest in request.Entries)
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
                Notes = entryRequest.Notes
            };

            var result = await UpsertDailyPerformanceAsync(upsertRequest, outletId);
            results.Add(result);
        }

        return results;
    }

    public async Task<bool> DeleteDailyPerformanceAsync(string id)
    {
        var result = await _dailyPerformanceEntries.DeleteOneAsync(e => e.Id == id);
        return result.DeletedCount > 0;
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
}
