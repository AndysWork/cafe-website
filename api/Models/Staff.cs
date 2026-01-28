using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Cafe.Api.Services;
using System.ComponentModel.DataAnnotations;

namespace Cafe.Api.Models;

public class Staff
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("employeeId")]
    [Required]
    public string EmployeeId { get; set; } = string.Empty; // Unique employee identifier

    [BsonElement("firstName")]
    [Required]
    public string FirstName { get; set; } = string.Empty;

    [BsonElement("lastName")]
    [Required]
    public string LastName { get; set; } = string.Empty;

    [BsonElement("email")]
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [BsonElement("phoneNumber")]
    [Required]
    [Phone]
    public string PhoneNumber { get; set; } = string.Empty;

    [BsonElement("alternatePhoneNumber")]
    public string? AlternatePhoneNumber { get; set; }

    [BsonElement("dateOfBirth")]
    public DateTime? DateOfBirth { get; set; }

    [BsonElement("gender")]
    public string? Gender { get; set; } // Male, Female, Other

    [BsonElement("address")]
    public StaffAddress? Address { get; set; }

    [BsonElement("emergencyContact")]
    public EmergencyContact? EmergencyContact { get; set; }

    // Employment Details
    [BsonElement("position")]
    [Required]
    public string Position { get; set; } = string.Empty; // Manager, Cashier, Barista, Chef, Waiter, etc.

    [BsonElement("department")]
    public string? Department { get; set; } // Kitchen, Service, Management, etc.

    [BsonElement("employmentType")]
    public string EmploymentType { get; set; } = "Full-Time"; // Full-Time, Part-Time, Contract

    [BsonElement("hireDate")]
    [Required]
    public DateTime HireDate { get; set; } = MongoService.GetIstNow();

    [BsonElement("probationEndDate")]
    public DateTime? ProbationEndDate { get; set; }

    [BsonElement("terminationDate")]
    public DateTime? TerminationDate { get; set; }

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    // Compensation
    [BsonElement("salary")]
    public decimal Salary { get; set; }

    [BsonElement("salaryType")]
    public string SalaryType { get; set; } = "Monthly"; // Monthly, Daily, Hourly

    [BsonElement("bankDetails")]
    public BankDetails? BankDetails { get; set; }

    // Outlet Assignment - Staff can work at multiple outlets
    [BsonElement("outletIds")]
    [BsonRepresentation(BsonType.ObjectId)]
    public List<string> OutletIds { get; set; } = new();

    // Work Schedule
    [BsonElement("workingDays")]
    public List<string> WorkingDays { get; set; } = new(); // Monday, Tuesday, etc.

    [BsonElement("shiftStartTime")]
    public string? ShiftStartTime { get; set; } // e.g., "09:00"

    [BsonElement("shiftEndTime")]
    public string? ShiftEndTime { get; set; } // e.g., "18:00"

    // Documents
    [BsonElement("documents")]
    public List<StaffDocument> Documents { get; set; } = new();

    // Performance & Notes
    [BsonElement("performanceRating")]
    public decimal? PerformanceRating { get; set; } // 0-5 scale

    [BsonElement("notes")]
    public string? Notes { get; set; }

    [BsonElement("skills")]
    public List<string> Skills { get; set; } = new();

    // Audit Fields
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();

    [BsonElement("createdBy")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? CreatedBy { get; set; }

    [BsonElement("updatedAt")]
    public DateTime? UpdatedAt { get; set; }

    [BsonElement("updatedBy")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? UpdatedBy { get; set; }

    // Leave Balance
    [BsonElement("annualLeaveBalance")]
    public int AnnualLeaveBalance { get; set; } = 0;

    [BsonElement("sickLeaveBalance")]
    public int SickLeaveBalance { get; set; } = 0;

    [BsonElement("casualLeaveBalance")]
    public int CasualLeaveBalance { get; set; } = 0;
}

public class StaffAddress
{
    [BsonElement("street")]
    public string? Street { get; set; }

    [BsonElement("city")]
    public string? City { get; set; }

    [BsonElement("state")]
    public string? State { get; set; }

    [BsonElement("postalCode")]
    public string? PostalCode { get; set; }

    [BsonElement("country")]
    public string? Country { get; set; }
}

public class EmergencyContact
{
    [BsonElement("name")]
    public string? Name { get; set; }

    [BsonElement("relationship")]
    public string? Relationship { get; set; }

    [BsonElement("phoneNumber")]
    public string? PhoneNumber { get; set; }

    [BsonElement("alternatePhoneNumber")]
    public string? AlternatePhoneNumber { get; set; }
}

public class BankDetails
{
    [BsonElement("accountHolderName")]
    public string? AccountHolderName { get; set; }

    [BsonElement("accountNumber")]
    public string? AccountNumber { get; set; }

    [BsonElement("bankName")]
    public string? BankName { get; set; }

    [BsonElement("ifscCode")]
    public string? IfscCode { get; set; }

    [BsonElement("branchName")]
    public string? BranchName { get; set; }
}

public class StaffDocument
{
    [BsonElement("documentType")]
    public string DocumentType { get; set; } = string.Empty; // Aadhar, PAN, Resume, etc.

    [BsonElement("documentNumber")]
    public string? DocumentNumber { get; set; }

    [BsonElement("documentUrl")]
    public string? DocumentUrl { get; set; }

    [BsonElement("uploadedAt")]
    public DateTime UploadedAt { get; set; } = MongoService.GetIstNow();

    [BsonElement("expiryDate")]
    public DateTime? ExpiryDate { get; set; }

    [BsonElement("isVerified")]
    public bool IsVerified { get; set; } = false;
}

// Staff statistics model for reporting
public class StaffStatistics
{
    public int TotalStaff { get; set; }
    public int ActiveStaff { get; set; }
    public int InactiveStaff { get; set; }
    public int FullTimeStaff { get; set; }
    public int PartTimeStaff { get; set; }
    public int ContractStaff { get; set; }
    public Dictionary<string, int> StaffByPosition { get; set; } = new();
    public Dictionary<string, int> StaffByDepartment { get; set; } = new();
}
