using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Helpers;

/// <summary>
/// Comprehensive audit logging for security and compliance
/// </summary>
public class AuditLogger
{
    private readonly ILogger _logger;
    private static readonly ConcurrentQueue<AuditLog> _auditLogs = new();
    private const int MaxLogsInMemory = 10000;

    public AuditLogger(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Logs authentication events
    /// </summary>
    public void LogAuthentication(string userId, string action, bool success, string? ipAddress = null, string? userAgent = null, string? reason = null)
    {
        var log = new AuditLog
        {
            Category = AuditCategory.Authentication,
            Action = action,
            UserId = userId,
            Success = success,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Reason = reason,
            Timestamp = DateTime.UtcNow
        };

        AddLog(log);

        var logLevel = success ? LogLevel.Information : LogLevel.Warning;
        _logger.Log(logLevel, $"[AUTH] {action} - User: {userId}, Success: {success}, IP: {ipAddress}, Reason: {reason}");
    }

    /// <summary>
    /// Logs data access events
    /// </summary>
    public void LogDataAccess(string userId, string resourceType, string resourceId, string action, bool success, string? reason = null)
    {
        var log = new AuditLog
        {
            Category = AuditCategory.DataAccess,
            Action = action,
            UserId = userId,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Success = success,
            Reason = reason,
            Timestamp = DateTime.UtcNow
        };

        AddLog(log);

        _logger.LogInformation($"[DATA] {action} - User: {userId}, Resource: {resourceType}/{resourceId}, Success: {success}");
    }

    /// <summary>
    /// Logs data modification events
    /// </summary>
    public void LogDataModification(string userId, string resourceType, string resourceId, string action, object? oldValue = null, object? newValue = null)
    {
        var log = new AuditLog
        {
            Category = AuditCategory.DataModification,
            Action = action,
            UserId = userId,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Success = true,
            OldValue = oldValue != null ? JsonSerializer.Serialize(oldValue) : null,
            NewValue = newValue != null ? JsonSerializer.Serialize(newValue) : null,
            Timestamp = DateTime.UtcNow
        };

        AddLog(log);

        _logger.LogInformation($"[MODIFY] {action} - User: {userId}, Resource: {resourceType}/{resourceId}");
    }

    /// <summary>
    /// Logs security events
    /// </summary>
    public void LogSecurityEvent(string eventType, string? userId = null, string? ipAddress = null, string? details = null, SecuritySeverity severity = SecuritySeverity.Medium)
    {
        var log = new AuditLog
        {
            Category = AuditCategory.Security,
            Action = eventType,
            UserId = userId,
            IpAddress = ipAddress,
            Details = details,
            Severity = severity,
            Success = false,
            Timestamp = DateTime.UtcNow
        };

        AddLog(log);

        var logLevel = severity switch
        {
            SecuritySeverity.Critical => LogLevel.Critical,
            SecuritySeverity.High => LogLevel.Error,
            SecuritySeverity.Medium => LogLevel.Warning,
            _ => LogLevel.Information
        };

        _logger.Log(logLevel, $"[SECURITY] {eventType} - User: {userId}, IP: {ipAddress}, Severity: {severity}, Details: {details}");
    }

    /// <summary>
    /// Logs administrative actions
    /// </summary>
    public void LogAdminAction(string adminUserId, string action, string? targetUserId = null, string? details = null)
    {
        var log = new AuditLog
        {
            Category = AuditCategory.Administration,
            Action = action,
            UserId = adminUserId,
            TargetUserId = targetUserId,
            Details = details,
            Success = true,
            Timestamp = DateTime.UtcNow
        };

        AddLog(log);

        _logger.LogWarning($"[ADMIN] {action} - Admin: {adminUserId}, Target: {targetUserId}, Details: {details}");
    }

    /// <summary>
    /// Logs API usage
    /// </summary>
    public void LogApiCall(string endpoint, string method, string? userId = null, string? ipAddress = null, int statusCode = 200, long responseTimeMs = 0)
    {
        var log = new AuditLog
        {
            Category = AuditCategory.ApiUsage,
            Action = $"{method} {endpoint}",
            UserId = userId,
            IpAddress = ipAddress,
            StatusCode = statusCode,
            ResponseTimeMs = responseTimeMs,
            Success = statusCode >= 200 && statusCode < 400,
            Timestamp = DateTime.UtcNow
        };

        AddLog(log);
    }

    /// <summary>
    /// Logs file operations
    /// </summary>
    public void LogFileOperation(string userId, string operation, string fileName, long fileSize, bool success, string? reason = null)
    {
        var log = new AuditLog
        {
            Category = AuditCategory.FileOperation,
            Action = operation,
            UserId = userId,
            FileName = fileName,
            FileSize = fileSize,
            Success = success,
            Reason = reason,
            Timestamp = DateTime.UtcNow
        };

        AddLog(log);

        _logger.LogInformation($"[FILE] {operation} - User: {userId}, File: {fileName}, Size: {fileSize}, Success: {success}");
    }

    /// <summary>
    /// Gets audit logs by criteria
    /// </summary>
    public List<AuditLog> GetLogs(
        AuditCategory? category = null,
        string? userId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        int maxResults = 100)
    {
        var query = _auditLogs.AsEnumerable();

        if (category.HasValue)
            query = query.Where(l => l.Category == category.Value);

        if (!string.IsNullOrEmpty(userId))
            query = query.Where(l => l.UserId == userId);

        if (startDate.HasValue)
            query = query.Where(l => l.Timestamp >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(l => l.Timestamp <= endDate.Value);

        return query.OrderByDescending(l => l.Timestamp).Take(maxResults).ToList();
    }

    /// <summary>
    /// Gets security alerts (failed auth attempts, suspicious activity)
    /// </summary>
    public List<AuditLog> GetSecurityAlerts(int hours = 24)
    {
        var cutoff = DateTime.UtcNow.AddHours(-hours);
        return _auditLogs
            .Where(l =>
                l.Timestamp >= cutoff &&
                (l.Category == AuditCategory.Security ||
                 (l.Category == AuditCategory.Authentication && !l.Success)))
            .OrderByDescending(l => l.Timestamp)
            .ToList();
    }

    /// <summary>
    /// Gets failed login attempts for a user
    /// </summary>
    public int GetFailedLoginAttempts(string userId, int hours = 1)
    {
        var cutoff = DateTime.UtcNow.AddHours(-hours);
        return _auditLogs
            .Count(l =>
                l.Timestamp >= cutoff &&
                l.Category == AuditCategory.Authentication &&
                l.UserId == userId &&
                !l.Success &&
                (l.Action == "Login" || l.Action == "Login Failed"));
    }

    /// <summary>
    /// Adds log to queue and manages memory
    /// </summary>
    private void AddLog(AuditLog log)
    {
        _auditLogs.Enqueue(log);

        // Keep only recent logs in memory
        while (_auditLogs.Count > MaxLogsInMemory)
        {
            _auditLogs.TryDequeue(out _);
        }
    }

    /// <summary>
    /// Exports audit logs to file or external system
    /// </summary>
    public Task<string> ExportLogs(DateTime startDate, DateTime endDate, string format = "json")
    {
        var logs = GetLogs(startDate: startDate, endDate: endDate, maxResults: int.MaxValue);

        if (format.ToLower() == "json")
        {
            return Task.FromResult(JsonSerializer.Serialize(logs, new JsonSerializerOptions { WriteIndented = true }));
        }
        else if (format.ToLower() == "csv")
        {
            var csv = new System.Text.StringBuilder();
            csv.AppendLine("Timestamp,Category,Action,UserId,ResourceType,ResourceId,Success,IpAddress,Details");

            foreach (var log in logs)
            {
                csv.AppendLine($"{log.Timestamp:O},{log.Category},{log.Action},{log.UserId},{log.ResourceType},{log.ResourceId},{log.Success},{log.IpAddress},{log.Details}");
            }

            return Task.FromResult(csv.ToString());
        }

        throw new ArgumentException("Unsupported format. Use 'json' or 'csv'.");
    }
}

public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Timestamp { get; set; }
    public AuditCategory Category { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? TargetUserId { get; set; }
    public string? ResourceType { get; set; }
    public string? ResourceId { get; set; }
    public bool Success { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Reason { get; set; }
    public string? Details { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? FileName { get; set; }
    public long? FileSize { get; set; }
    public int? StatusCode { get; set; }
    public long? ResponseTimeMs { get; set; }
    public SecuritySeverity Severity { get; set; } = SecuritySeverity.Low;
}

public enum AuditCategory
{
    Authentication,
    Authorization,
    DataAccess,
    DataModification,
    Security,
    Administration,
    ApiUsage,
    FileOperation,
    Configuration
}

public enum SecuritySeverity
{
    Low,
    Medium,
    High,
    Critical
}
