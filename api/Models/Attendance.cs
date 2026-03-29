using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Cafe.Api.Services;
using System.ComponentModel.DataAnnotations;
using Cafe.Api.Helpers;

namespace Cafe.Api.Models;

public class Attendance
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string OutletId { get; set; } = string.Empty;

    [BsonElement("staffId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string StaffId { get; set; } = string.Empty;

    [BsonElement("staffName")]
    public string StaffName { get; set; } = string.Empty;

    [BsonElement("date")]
    public DateTime Date { get; set; }

    [BsonElement("clockIn")]
    public DateTime? ClockIn { get; set; }

    [BsonElement("clockOut")]
    public DateTime? ClockOut { get; set; }

    [BsonElement("hoursWorked")]
    public double HoursWorked { get; set; }

    [BsonElement("status")]
    public string Status { get; set; } = "absent"; // present, absent, half-day, late, leave

    [BsonElement("leaveType")]
    public string? LeaveType { get; set; } // sick, casual, earned, unpaid

    [BsonElement("notes")]
    public string? Notes { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();
}

public class LeaveRequest
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string OutletId { get; set; } = string.Empty;

    [BsonElement("staffId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string StaffId { get; set; } = string.Empty;

    [BsonElement("staffName")]
    public string StaffName { get; set; } = string.Empty;

    [BsonElement("leaveType")]
    public string LeaveType { get; set; } = string.Empty;

    [BsonElement("startDate")]
    public DateTime StartDate { get; set; }

    [BsonElement("endDate")]
    public DateTime EndDate { get; set; }

    [BsonElement("reason")]
    public string Reason { get; set; } = string.Empty;

    [BsonElement("status")]
    public string Status { get; set; } = "pending"; // pending, approved, rejected

    [BsonElement("approvedBy")]
    public string? ApprovedBy { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();
}

public class ClockInOutRequest
{
    [Required]
    public string StaffId { get; set; } = string.Empty;

    [Required]
    [AllowedValuesList("clockIn", "clockOut")]
    public string Action { get; set; } = string.Empty;
}

public class CreateLeaveRequestDto
{
    [Required]
    public string StaffId { get; set; } = string.Empty;

    [Required]
    [AllowedValuesList("sick", "casual", "earned", "unpaid")]
    public string LeaveType { get; set; } = string.Empty;

    [Required]
    public DateTime StartDate { get; set; }

    [Required]
    public DateTime EndDate { get; set; }

    [Required] [StringLength(500)]
    public string Reason { get; set; } = string.Empty;
}

public class AttendanceSummary
{
    public string StaffId { get; set; } = string.Empty;
    public string StaffName { get; set; } = string.Empty;
    public int PresentDays { get; set; }
    public int AbsentDays { get; set; }
    public int LateDays { get; set; }
    public int LeaveDays { get; set; }
    public double TotalHoursWorked { get; set; }
}
