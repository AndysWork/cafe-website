using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Cafe.Api.Models
{
    public class PerformanceShift
    {
        // Note: Shifts are embedded documents, not root documents, so no [BsonId] needed
        // MongoDB will store them as part of the parent DailyPerformanceEntry
        public string? Id { get; set; }

        [Required]
        public string ShiftName { get; set; } = string.Empty;

        [Required]
        public string InTime { get; set; } = string.Empty; // Format: HH:mm

        [Required]
        public string OutTime { get; set; } = string.Empty; // Format: HH:mm

        public double WorkingHours { get; set; }

        public int TotalOrdersPrepared { get; set; }

        public int GoodOrdersCount { get; set; }

        public int BadOrdersCount { get; set; }

        public decimal RefundAmountRecovery { get; set; }

        public string? Notes { get; set; }
    }

    public class DailyPerformanceEntry
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [Required]
        public string OutletId { get; set; } = string.Empty;

        [Required]
        public string StaffId { get; set; } = string.Empty;

        public string? StaffName { get; set; }

        [Required]
        public string Date { get; set; } = string.Empty; // Format: YYYY-MM-DD

        // Legacy fields - kept for backward compatibility
        public string InTime { get; set; } = string.Empty; // Format: HH:mm

        public string OutTime { get; set; } = string.Empty; // Format: HH:mm

        public double WorkingHours { get; set; }

        public int TotalOrdersPrepared { get; set; }

        public int GoodOrdersCount { get; set; }

        public int BadOrdersCount { get; set; }

        public decimal RefundAmountRecovery { get; set; }

        public string? Notes { get; set; }

        // New multi-shift support
        public List<PerformanceShift> Shifts { get; set; } = new List<PerformanceShift>();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class UpsertDailyPerformanceRequest
    {
        [Required]
        public string StaffId { get; set; } = string.Empty;

        [Required]
        public string Date { get; set; } = string.Empty;

        [Required]
        public string InTime { get; set; } = string.Empty;

        [Required]
        public string OutTime { get; set; } = string.Empty;

        public int TotalOrdersPrepared { get; set; }

        public int GoodOrdersCount { get; set; }

        public int BadOrdersCount { get; set; }

        public decimal RefundAmountRecovery { get; set; }

        public string? Notes { get; set; }

        public List<PerformanceShift>? Shifts { get; set; }
    }

    public class BulkDailyPerformanceRequest
    {
        [Required]
        public string Date { get; set; } = string.Empty;

        [Required]
        public List<DailyPerformanceEntryRequest> Entries { get; set; } = new List<DailyPerformanceEntryRequest>();
    }

    public class DailyPerformanceEntryRequest
    {
        [Required]
        public string StaffId { get; set; } = string.Empty;

        [Required]
        public string InTime { get; set; } = string.Empty;

        [Required]
        public string OutTime { get; set; } = string.Empty;

        public int TotalOrdersPrepared { get; set; }

        public int GoodOrdersCount { get; set; }

        public int BadOrdersCount { get; set; }

        public decimal RefundAmountRecovery { get; set; }

        public string? Notes { get; set; }

        public List<PerformanceShift>? Shifts { get; set; }
    }
}
