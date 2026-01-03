using System.ComponentModel.DataAnnotations;

namespace Cafe.Api.Helpers;

/// <summary>
/// Validates that a decimal value is a whole number (integer)
/// </summary>
public class IntegerValueAttribute : ValidationAttribute
{
    public IntegerValueAttribute()
    {
        ErrorMessage = "Value must be a whole number (no decimal places)";
    }
    
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null)
            return ValidationResult.Success;
        
        if (value is decimal decimalValue)
        {
            if (decimalValue % 1 != 0)
                return new ValidationResult(ErrorMessage);
        }
        
        return ValidationResult.Success;
    }
}

/// <summary>
/// Validates that a file size does not exceed the maximum allowed size
/// </summary>
public class MaxFileSizeAttribute : ValidationAttribute
{
    private readonly int _maxFileSize;
    
    public MaxFileSizeAttribute(int maxFileSize)
    {
        _maxFileSize = maxFileSize;
        ErrorMessage = $"File size cannot exceed {maxFileSize / (1024 * 1024)}MB";
    }
    
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null)
            return ValidationResult.Success;
        
        if (value is Stream stream)
        {
            if (stream.Length > _maxFileSize)
                return new ValidationResult(ErrorMessage);
        }
        
        return ValidationResult.Success;
    }
}

/// <summary>
/// Validates Indian phone number format (10 digits starting with 6-9)
/// </summary>
public class IndianPhoneNumberAttribute : ValidationAttribute
{
    public IndianPhoneNumberAttribute()
    {
        ErrorMessage = "Phone number must be a valid 10-digit Indian number starting with 6-9";
    }
    
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null or "")
            return ValidationResult.Success;
        
        var phoneNumber = value.ToString()!.Trim();
        
        // Remove country code if present
        if (phoneNumber.StartsWith("+91"))
            phoneNumber = phoneNumber.Substring(3).Trim();
        
        if (phoneNumber.StartsWith("91") && phoneNumber.Length == 12)
            phoneNumber = phoneNumber.Substring(2);
        
        // Check format: 10 digits starting with 6-9
        if (phoneNumber.Length != 10)
            return new ValidationResult(ErrorMessage);
        
        if (!char.IsDigit(phoneNumber[0]) || phoneNumber[0] < '6' || phoneNumber[0] > '9')
            return new ValidationResult(ErrorMessage);
        
        if (!phoneNumber.All(char.IsDigit))
            return new ValidationResult(ErrorMessage);
        
        return ValidationResult.Success;
    }
}

/// <summary>
/// Validates that a string contains only alphanumeric characters and underscores
/// </summary>
public class AlphanumericAttribute : ValidationAttribute
{
    public AlphanumericAttribute()
    {
        ErrorMessage = "Field can only contain letters, numbers, and underscores";
    }
    
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null or "")
            return ValidationResult.Success;
        
        var input = value.ToString()!;
        
        if (!input.All(c => char.IsLetterOrDigit(c) || c == '_'))
            return new ValidationResult(ErrorMessage);
        
        return ValidationResult.Success;
    }
}

/// <summary>
/// Validates that a value is one of the allowed values
/// </summary>
public class AllowedValuesListAttribute : ValidationAttribute
{
    private readonly string[] _allowedValues;
    
    public AllowedValuesListAttribute(params string[] allowedValues)
    {
        _allowedValues = allowedValues;
        ErrorMessage = $"Value must be one of: {string.Join(", ", allowedValues)}";
    }
    
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null or "")
            return ValidationResult.Success;
        
        var input = value.ToString()!;
        
        if (!_allowedValues.Contains(input, StringComparer.OrdinalIgnoreCase))
            return new ValidationResult(ErrorMessage);
        
        return ValidationResult.Success;
    }
}
