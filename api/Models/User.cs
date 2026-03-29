using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Cafe.Api.Services;
using System.ComponentModel.DataAnnotations;
using Cafe.Api.Helpers;

namespace Cafe.Api.Models;

public class User
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("username")]
    public string Username { get; set; } = string.Empty;

    [BsonElement("email")]
    public string Email { get; set; } = string.Empty;

    [BsonElement("passwordHash")]
    public string PasswordHash { get; set; } = string.Empty;

    [BsonElement("role")]
    public string Role { get; set; } = "user"; // "admin" or "user"

    [BsonElement("assignedOutlets")]
    [BsonRepresentation(BsonType.ObjectId)]
    public List<string> AssignedOutlets { get; set; } = new(); // ObjectIds of outlets user can access

    [BsonElement("defaultOutletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? DefaultOutletId { get; set; } // User's primary outlet

    [BsonElement("firstName")]
    public string? FirstName { get; set; }

    [BsonElement("lastName")]
    public string? LastName { get; set; }

    [BsonElement("phoneNumber")]
    public string? PhoneNumber { get; set; }

    [BsonElement("profilePictureUrl")]
    public string? ProfilePictureUrl { get; set; }

    [BsonElement("notificationPreferences")]
    public NotificationPreferences NotificationPreferences { get; set; } = new();

    [BsonElement("addresses")]
    public List<DeliveryAddress> Addresses { get; set; } = new();

    [BsonElement("favoriteMenuItemIds")]
    public List<string> FavoriteMenuItemIds { get; set; } = new();

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();

    [BsonElement("lastLoginAt")]
    public DateTime? LastLoginAt { get; set; }
}

public class DeliveryAddress
{
    [BsonElement("id")]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonElement("label")]
    public string Label { get; set; } = string.Empty; // e.g., "Home", "Office"

    [BsonElement("fullAddress")]
    public string FullAddress { get; set; } = string.Empty;

    [BsonElement("city")]
    public string? City { get; set; }

    [BsonElement("pinCode")]
    public string? PinCode { get; set; }

    [BsonElement("collectorName")]
    public string CollectorName { get; set; } = string.Empty;

    [BsonElement("collectorPhone")]
    public string CollectorPhone { get; set; } = string.Empty;

    [BsonElement("isDefault")]
    public bool IsDefault { get; set; } = false;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();
}

public class LoginRequest
{
    [Required(ErrorMessage = "Username or email is required")]
    [StringLength(100, MinimumLength = 3, ErrorMessage = "Username or email must be between 3 and 100 characters")]
    public string Username { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Password is required")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters")]
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public string? DefaultOutletId { get; set; }
    public List<string>? AssignedOutlets { get; set; }
}

public class RegisterRequest
{
    [Required(ErrorMessage = "Username is required")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Username must be between 3 and 50 characters")]
    [Alphanumeric]
    public string Username { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    [StringLength(100, ErrorMessage = "Email cannot exceed 100 characters")]
    public string Email { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Password is required")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be between 8 and 100 characters")]
    public string Password { get; set; } = string.Empty;
    
    [StringLength(50, ErrorMessage = "First name cannot exceed 50 characters")]
    public string? FirstName { get; set; }
    
    [StringLength(50, ErrorMessage = "Last name cannot exceed 50 characters")]
    public string? LastName { get; set; }
    
    [IndianPhoneNumber]
    public string? PhoneNumber { get; set; }
}

public class UpdateProfileRequest
{
    [StringLength(50, ErrorMessage = "First name cannot exceed 50 characters")]
    public string? FirstName { get; set; }
    
    [StringLength(50, ErrorMessage = "Last name cannot exceed 50 characters")]
    public string? LastName { get; set; }
    
    [EmailAddress(ErrorMessage = "Invalid email format")]
    [StringLength(100, ErrorMessage = "Email cannot exceed 100 characters")]
    public string? Email { get; set; }
    
    [IndianPhoneNumber]
    public string? PhoneNumber { get; set; }
}

public class ChangePasswordRequest
{
    [Required(ErrorMessage = "Current password is required")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Current password must be at least 6 characters")]
    public string CurrentPassword { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "New password is required")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "New password must be between 8 and 100 characters")]
    public string NewPassword { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Confirm password is required")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class ForgotPasswordRequest
{
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordRequest
{
    [Required(ErrorMessage = "Reset token is required")]
    public string ResetToken { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "New password is required")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "New password must be between 8 and 100 characters")]
    public string NewPassword { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Confirm password is required")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class PasswordResetToken
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("userId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("token")]
    public string Token { get; set; } = string.Empty;

    [BsonElement("expiresAt")]
    public DateTime ExpiresAt { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();

    [BsonElement("isUsed")]
    public bool IsUsed { get; set; } = false;
}

public class AddDeliveryAddressRequest
{
    [Required(ErrorMessage = "Label is required")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "Label must be between 1 and 50 characters")]
    public string Label { get; set; } = string.Empty;

    [Required(ErrorMessage = "Full address is required")]
    [StringLength(500, MinimumLength = 5, ErrorMessage = "Address must be between 5 and 500 characters")]
    public string FullAddress { get; set; } = string.Empty;

    [StringLength(100)]
    public string? City { get; set; }

    [StringLength(10)]
    public string? PinCode { get; set; }

    [Required(ErrorMessage = "Collector name is required")]
    [StringLength(100, MinimumLength = 1)]
    public string CollectorName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Collector phone is required")]
    [IndianPhoneNumber]
    public string CollectorPhone { get; set; } = string.Empty;

    public bool IsDefault { get; set; } = false;
}

public class UpdateDeliveryAddressRequest
{
    [StringLength(50, MinimumLength = 1)]
    public string? Label { get; set; }

    [StringLength(500, MinimumLength = 5)]
    public string? FullAddress { get; set; }

    [StringLength(100)]
    public string? City { get; set; }

    [StringLength(10)]
    public string? PinCode { get; set; }

    [StringLength(100, MinimumLength = 1)]
    public string? CollectorName { get; set; }

    [IndianPhoneNumber]
    public string? CollectorPhone { get; set; }

    public bool? IsDefault { get; set; }
}
