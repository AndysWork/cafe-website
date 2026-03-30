using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker.Http;

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

    /// <summary>
    /// Reads, sanitizes, and validates a JSON request body in a single call.
    /// Replaces the per-function pattern of ReadFromJsonAsync + null check + sanitize + validate.
    /// Returns either the validated model or an HttpResponseData error.
    /// </summary>
    public static async Task<(T? Model, HttpResponseData? ErrorResponse)> ValidateBody<T>(HttpRequestData req) where T : class
    {
        T? model;
        try
        {
            model = await req.ReadFromJsonAsync<T>();
        }
        catch (Exception)
        {
            var error = req.CreateResponse(HttpStatusCode.BadRequest);
            await error.WriteAsJsonAsync(new { success = false, error = "Invalid JSON format" });
            return (null, error);
        }

        if (model == null)
        {
            var error = req.CreateResponse(HttpStatusCode.BadRequest);
            await error.WriteAsJsonAsync(new { success = false, error = "Request body is required" });
            return (null, error);
        }

        // Sanitize all string properties recursively
        SanitizeObjectStrings(model);

        // Validate using DataAnnotations
        var validationResults = ValidateModel(model);
        if (validationResults.Any())
        {
            var errors = validationResults
                .GroupBy(vr => vr.MemberNames.FirstOrDefault() ?? "General")
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(vr => vr.ErrorMessage ?? "Validation error").ToArray()
                );

            var error = req.CreateResponse(HttpStatusCode.BadRequest);
            await error.WriteAsJsonAsync(new { success = false, message = "Validation failed", errors });
            return (null, error);
        }

        return (model, null);
    }

    /// <summary>
    /// Recursively sanitizes all string properties on an object using InputSanitizer.Sanitize().
    /// Skips properties whose names contain "password", "secret", or "token".
    /// </summary>
    public static void SanitizeObjectStrings(object obj, int depth = 0)
    {
        if (obj == null || depth > 3) return;

        var type = obj.GetType();
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || !prop.CanWrite) continue;

            var nameLower = prop.Name.ToLowerInvariant();
            if (nameLower.Contains("password") || nameLower.Contains("secret") || nameLower.Contains("token"))
                continue;

            if (prop.PropertyType == typeof(string))
            {
                var value = (string?)prop.GetValue(obj);
                if (!string.IsNullOrEmpty(value))
                {
                    prop.SetValue(obj, InputSanitizer.Sanitize(value));
                }
            }
            else if (prop.PropertyType.IsClass && prop.PropertyType != typeof(string))
            {
                if (typeof(IEnumerable).IsAssignableFrom(prop.PropertyType))
                {
                    var enumerable = prop.GetValue(obj) as IEnumerable;
                    if (enumerable != null)
                    {
                        foreach (var item in enumerable)
                        {
                            if (item != null && item is not string)
                                SanitizeObjectStrings(item, depth + 1);
                        }
                    }
                }
                else
                {
                    var nested = prop.GetValue(obj);
                    if (nested != null)
                        SanitizeObjectStrings(nested, depth + 1);
                }
            }
        }
    }
}
