using Cafe.Api.Models;

namespace Cafe.Api.Repositories;

public interface IStaffRepository
{
    // Staff CRUD
    Task<List<Staff>> GetAllStaffAsync();
    Task<List<Staff>> GetActiveStaffAsync();
    Task<Staff?> GetStaffByIdAsync(string staffId);
    Task<Staff?> GetStaffByEmployeeIdAsync(string employeeId);
    Task<Staff?> GetStaffByEmailAsync(string email);
    Task<List<Staff>> GetStaffByOutletAsync(string outletId);
    Task<List<Staff>> GetStaffByPositionAsync(string position);
    Task<List<Staff>> GetStaffByDepartmentAsync(string department);
    Task<Staff> CreateStaffAsync(Staff staff);
    Task<bool> UpdateStaffAsync(string staffId, Staff updatedStaff);
    Task<bool> UpdateStaffActiveStatusAsync(string staffId, bool isActive, string updatedBy);
    Task<bool> UpdateStaffSalaryAsync(string staffId, decimal newSalary, string updatedBy);
    Task<bool> UpdateStaffOutletAsync(string staffId, string outletId, string updatedBy);
    Task<bool> UpdateStaffPositionAsync(string staffId, string position, string updatedBy);
    Task<bool> UpdateStaffPerformanceRatingAsync(string staffId, decimal rating, string updatedBy);
    Task<bool> AddStaffDocumentAsync(string staffId, StaffDocument document);
    Task<bool> UpdateStaffLeaveBalancesAsync(string staffId, int annual, int sick, int casual, string updatedBy);
    Task<bool> DeleteStaffAsync(string staffId, string deletedBy);
    Task<bool> HardDeleteStaffAsync(string staffId);
    Task<List<Staff>> SearchStaffAsync(string searchTerm);
    Task<long> GetStaffCountByOutletAsync(string outletId);
    Task<StaffStatistics> GetStaffStatisticsAsync();

    // Bonus Configuration
    Task<List<BonusConfiguration>> GetAllBonusConfigurationsAsync(string? outletId = null);
    Task<List<BonusConfiguration>> GetActiveBonusConfigurationsAsync(string? outletId = null);
    Task<BonusConfiguration?> GetBonusConfigurationByIdAsync(string id);
    Task<List<BonusConfiguration>> GetBonusConfigurationsForStaffAsync(Staff staff);
    Task<BonusConfiguration> CreateBonusConfigurationAsync(BonusConfiguration config);
    Task<bool> UpdateBonusConfigurationAsync(string id, BonusConfiguration config);
    Task<bool> DeleteBonusConfigurationAsync(string id);
    Task<bool> ToggleBonusConfigurationStatusAsync(string id, bool isActive);
    Task<string> GetBonusDescriptionForStaffAsync(Staff staff);

    // Staff Performance
    Task<StaffPerformanceRecord?> GetStaffPerformanceRecordByIdAsync(string id);
    Task<List<StaffPerformanceRecord>> GetStaffPerformanceRecordsAsync(string staffId, string period);
    Task<List<StaffPerformanceRecord>> GetOutletPerformanceRecordsAsync(string? outletId, string period);
    Task<StaffPerformanceRecord> UpsertStaffPerformanceRecordAsync(StaffPerformanceRecord record);
    Task<StaffPerformanceRecord> CalculateStaffBonusAsync(string recordId);

    // Daily Performance
    Task<List<DailyPerformanceEntry>> GetDailyPerformanceByDateAsync(string date, string outletId);
    Task<List<DailyPerformanceEntry>> GetDailyPerformanceByStaffAsync(string staffId, string? startDate = null, string? endDate = null);
    Task<List<DailyPerformanceEntry>> GetDailyPerformanceByDateRangeAsync(string startDate, string endDate, string outletId);
    Task<DailyPerformanceEntry> UpsertDailyPerformanceAsync(UpsertDailyPerformanceRequest request, string outletId);
    Task<List<DailyPerformanceEntry>> BulkUpsertDailyPerformanceAsync(BulkDailyPerformanceRequest request, string outletId);
    Task<bool> DeleteDailyPerformanceAsync(string id);
    Task<PerformanceShift> AddPerformanceShiftAsync(string entryId, PerformanceShift shift);
    Task<PerformanceShift> UpdatePerformanceShiftAsync(string entryId, string shiftId, PerformanceShift updatedShift);
    Task<bool> DeletePerformanceShiftAsync(string entryId, string shiftId);
    Task<List<PerformanceShift>> GetPerformanceShiftsAsync(string entryId);

    // Attendance
    Task<Attendance?> GetTodayAttendanceAsync(string staffId, string outletId);
    Task<List<Attendance>> GetAllTodayAttendanceAsync(string outletId);
    Task<Attendance> ClockInAsync(string staffId, string staffName, string outletId);
    Task<Attendance?> ClockOutAsync(string staffId, string outletId);
    Task<List<Attendance>> GetAttendanceByDateRangeAsync(string outletId, DateTime start, DateTime end, string? staffId = null);
    Task<List<AttendanceSummary>> GetAttendanceSummaryAsync(string outletId, DateTime start, DateTime end);

    // Leave
    Task<LeaveRequest> CreateLeaveRequestAsync(LeaveRequest request);
    Task<List<LeaveRequest>> GetLeaveRequestsAsync(string outletId, string? status = null);
    Task<bool> UpdateLeaveRequestStatusAsync(string id, string status, string? approvedBy = null);
}
