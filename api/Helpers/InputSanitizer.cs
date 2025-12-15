using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace Cafe.Api.Helpers;

/// <summary>
/// Input sanitization to prevent XSS, script injection, and other attacks
/// </summary>
public static class InputSanitizer
{
    // Patterns to detect potentially malicious content
    private static readonly Regex ScriptPattern = new(@"<script[^>]*>.*?</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex HtmlPattern = new(@"<[^>]+>", RegexOptions.IgnoreCase);
    private static readonly Regex SqlPattern = new(@"(\b(SELECT|INSERT|UPDATE|DELETE|DROP|CREATE|ALTER|EXEC|EXECUTE)\b)", RegexOptions.IgnoreCase);
    private static readonly Regex JavaScriptEventPattern = new(@"on\w+\s*=", RegexOptions.IgnoreCase);
    private static readonly Regex DataUriPattern = new(@"data:text/html", RegexOptions.IgnoreCase);
    private static readonly Regex IframePattern = new(@"<iframe[^>]*>.*?</iframe>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

    /// <summary>
    /// Sanitizes input string by removing HTML tags and dangerous characters
    /// </summary>
    public static string Sanitize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var sanitized = input;

        // Remove script tags
        sanitized = ScriptPattern.Replace(sanitized, "");

        // Remove iframe tags
        sanitized = IframePattern.Replace(sanitized, "");

        // Remove HTML tags (except allowed ones)
        sanitized = StripHtmlTags(sanitized);

        // Remove JavaScript event handlers
        sanitized = JavaScriptEventPattern.Replace(sanitized, "");

        // Remove data URIs
        sanitized = DataUriPattern.Replace(sanitized, "");

        // HTML encode to prevent XSS
        sanitized = HttpUtility.HtmlEncode(sanitized);

        // Trim whitespace
        sanitized = sanitized.Trim();

        return sanitized;
    }

    /// <summary>
    /// Sanitizes input but allows safe HTML tags (for descriptions, etc.)
    /// </summary>
    public static string SanitizeAllowSafeHtml(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var sanitized = input;

        // Remove dangerous tags
        sanitized = ScriptPattern.Replace(sanitized, "");
        sanitized = IframePattern.Replace(sanitized, "");
        sanitized = JavaScriptEventPattern.Replace(sanitized, "");
        sanitized = DataUriPattern.Replace(sanitized, "");

        // Allow only safe tags: p, br, b, i, u, strong, em, ul, ol, li
        sanitized = StripHtmlTagsExceptSafe(sanitized);

        return sanitized.Trim();
    }

    /// <summary>
    /// Validates that input doesn't contain SQL injection patterns
    /// Note: This is for defense-in-depth; MongoDB is not vulnerable to SQL injection
    /// </summary>
    public static bool ContainsSqlInjection(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        return SqlPattern.IsMatch(input);
    }

    /// <summary>
    /// Sanitizes email address
    /// </summary>
    public static string SanitizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return string.Empty;

        // Remove all characters except valid email characters
        var sanitized = Regex.Replace(email, @"[^a-zA-Z0-9@._\-+]", "");
        return sanitized.ToLowerInvariant().Trim();
    }

    /// <summary>
    /// Sanitizes username (alphanumeric and underscore only)
    /// </summary>
    public static string SanitizeUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return string.Empty;

        // Remove all characters except alphanumeric and underscore
        var sanitized = Regex.Replace(username, @"[^a-zA-Z0-9_]", "");
        return sanitized.Trim();
    }

    /// <summary>
    /// Sanitizes phone number
    /// </summary>
    public static string SanitizePhoneNumber(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return string.Empty;

        // Remove all characters except digits, +, and -
        var sanitized = Regex.Replace(phone, @"[^0-9+\-]", "");
        return sanitized.Trim();
    }

    /// <summary>
    /// Strips all HTML tags
    /// </summary>
    private static string StripHtmlTags(string input)
    {
        return HtmlPattern.Replace(input, "");
    }

    /// <summary>
    /// Strips HTML tags except safe ones
    /// </summary>
    private static string StripHtmlTagsExceptSafe(string input)
    {
        var allowedTags = new[] { "p", "br", "b", "i", "u", "strong", "em", "ul", "ol", "li" };
        var pattern = new Regex(@"</?(\w+)[^>]*>");

        return pattern.Replace(input, match =>
        {
            var tag = match.Groups[1].Value.ToLowerInvariant();
            return allowedTags.Contains(tag) ? match.Value : "";
        });
    }

    /// <summary>
    /// Validates and sanitizes file name
    /// </summary>
    public static string SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;

        // Remove path characters
        var sanitized = fileName.Replace("..", "").Replace("/", "").Replace("\\", "");

        // Remove special characters except dot, dash, underscore
        sanitized = Regex.Replace(sanitized, @"[^a-zA-Z0-9._\-]", "");

        // Limit length
        if (sanitized.Length > 255)
            sanitized = sanitized.Substring(0, 255);

        return sanitized.Trim();
    }

    /// <summary>
    /// Checks if input contains potentially dangerous content
    /// </summary>
    public static bool IsPotentiallyDangerous(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        return ScriptPattern.IsMatch(input) ||
               IframePattern.IsMatch(input) ||
               JavaScriptEventPattern.IsMatch(input) ||
               DataUriPattern.IsMatch(input);
    }

    /// <summary>
    /// Sanitizes object ID (MongoDB ObjectId)
    /// </summary>
    public static string SanitizeObjectId(string? objectId)
    {
        if (string.IsNullOrWhiteSpace(objectId))
            return string.Empty;

        // MongoDB ObjectId is 24 hex characters
        var sanitized = Regex.Replace(objectId, @"[^a-fA-F0-9]", "");

        if (sanitized.Length != 24)
            return string.Empty;

        return sanitized.ToLowerInvariant();
    }

    /// <summary>
    /// Sanitizes numeric input
    /// </summary>
    public static string SanitizeNumeric(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        // Remove all characters except digits, decimal point, and minus sign
        var sanitized = Regex.Replace(input, @"[^0-9.\-]", "");
        return sanitized.Trim();
    }
}
