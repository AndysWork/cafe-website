using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Cafe.Api.Helpers;

/// <summary>
/// Helper class for validating models and returning structured validation errors
/// </summary>
public static class ValidationHelper
{
    /// <summary>
    /// Validates a model and returns validation errors if any
    /// </summary>
    /// <param name="model">The model to validate</param>
    /// <returns>Validation results</returns>
    public static List<ValidationResult> ValidateModel(object model)
    {
        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(model);
        Validator.TryValidateObject(model, validationContext, validationResults, validateAllProperties: true);
        return validationResults;
    }

    /// <summary>
    /// Checks if a model is valid
    /// </summary>
    /// <param name="model">The model to validate</param>
    /// <returns>True if valid, false otherwise</returns>
    public static bool IsValid(object model)
    {
        var validationContext = new ValidationContext(model);
        return Validator.TryValidateObject(model, validationContext, null, validateAllProperties: true);
    }

    /// <summary>
    /// Creates a BadRequest response with validation errors
    /// </summary>
    /// <param name="validationResults">List of validation results</param>
    /// <returns>BadRequestObjectResult with structured error response</returns>
    public static BadRequestObjectResult CreateValidationErrorResponse(List<ValidationResult> validationResults)
    {
        var errors = validationResults
            .GroupBy(vr => vr.MemberNames.FirstOrDefault() ?? "General")
            .ToDictionary(
                g => g.Key,
                g => g.Select(vr => vr.ErrorMessage ?? "Validation error").ToArray()
            );

        return new BadRequestObjectResult(new
        {
            success = false,
            message = "Validation failed",
            errors = errors
        });
    }

    /// <summary>
    /// Validates a model and returns a BadRequest response if validation fails
    /// </summary>
    /// <param name="model">The model to validate</param>
    /// <param name="errorResponse">The error response if validation fails</param>
    /// <returns>True if valid, false if validation failed</returns>
    public static bool TryValidate(object model, out BadRequestObjectResult? errorResponse)
    {
        var validationResults = ValidateModel(model);
        
        if (validationResults.Any())
        {
            errorResponse = CreateValidationErrorResponse(validationResults);
            return false;
        }

        errorResponse = null;
        return true;
    }

    /// <summary>
    /// Validates multiple models and returns a combined error response if any validation fails
    /// </summary>
    /// <param name="models">Dictionary of model names and models to validate</param>
    /// <param name="errorResponse">The combined error response if validation fails</param>
    /// <returns>True if all valid, false if any validation failed</returns>
    public static bool TryValidateMultiple(Dictionary<string, object> models, out BadRequestObjectResult? errorResponse)
    {
        var allErrors = new Dictionary<string, List<string>>();

        foreach (var (modelName, model) in models)
        {
            var validationResults = ValidateModel(model);
            
            foreach (var result in validationResults)
            {
                var key = result.MemberNames.Any()
                    ? $"{modelName}.{result.MemberNames.First()}"
                    : modelName;

                if (!allErrors.ContainsKey(key))
                    allErrors[key] = new List<string>();

                allErrors[key].Add(result.ErrorMessage ?? "Validation error");
            }
        }

        if (allErrors.Any())
        {
            errorResponse = new BadRequestObjectResult(new
            {
                success = false,
                message = "Validation failed",
                errors = allErrors.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray())
            });
            return false;
        }

        errorResponse = null;
        return true;
    }
}
