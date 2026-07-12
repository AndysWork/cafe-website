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

    [BsonElement("scheduledHours")]
    public double ScheduledHours { get; set; }

    [BsonElement("overtimeHours")]
    public double OvertimeHours { get; set; }

    [BsonElement("undertimeHours")]
    public double UndertimeHours { get; set; }

    [BsonElement("scheduledShiftLabel")]
    public string? ScheduledShiftLabel { get; set; }

    [BsonElement("sessions")]
    public List<AttendanceSession> Sessions { get; set; } = new();

    [BsonElement("status")]
    public string Status { get; set; } = "absent"; // present, absent, half-day, late, leave

    [BsonElement("leaveType")]
    public string? LeaveType { get; set; } // sick, casual, earned, unpaid

    [BsonElement("notes")]
    public string? Notes { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();
}

public class AttendanceSession
{
    [BsonElement("sessionId")]
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");

    [BsonElement("shiftKey")]
    public string ShiftKey { get; set; } = string.Empty;

    [BsonElement("shiftName")]
    public string ShiftName { get; set; } = string.Empty;

    [BsonElement("shiftStartTime")]
    public string? ShiftStartTime { get; set; }

    [BsonElement("shiftEndTime")]
    public string? ShiftEndTime { get; set; }

    [BsonElement("clockIn")]
    public DateTime? ClockIn { get; set; }

    [BsonElement("clockOut")]
    public DateTime? ClockOut { get; set; }

    [BsonElement("scheduledHours")]
    public double ScheduledHours { get; set; }

    [BsonElement("hoursWorked")]
    public double HoursWorked { get; set; }

    [BsonElement("overtimeHours")]
    public double OvertimeHours { get; set; }

    [BsonElement("undertimeHours")]
    public double UndertimeHours { get; set; }

    [BsonElement("status")]
    public string Status { get; set; } = "pending"; // pending, in-progress, completed
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

    [BsonElement("isHalfDay")]
    public bool IsHalfDay { get; set; }

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
    [AllowedValuesList("earned")]
    public string LeaveType { get; set; } = string.Empty;

    [Required]
    public DateTime StartDate { get; set; }

    [Required]
    public DateTime EndDate { get; set; }

    public bool IsHalfDay { get; set; }

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
