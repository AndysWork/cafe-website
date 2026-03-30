using Cafe.Api.Models;

namespace Cafe.Api.Repositories;

public interface IAnalyticsRepository
{
    Task TrackEventAsync(UserActivityEvent evt);
    Task TrackEventsBatchAsync(List<UserActivityEvent> events);
    Task<UserSession> CreateSessionAsync(string userId, string username, string role, string sessionId);
    Task EndSessionAsync(string sessionId);
    Task UpdateSessionActivityAsync(string sessionId);
    Task<UserMetrics> GetUserMetricsAsync();
    Task<List<FeatureUsageStat>> GetTopFeaturesAsync(int limit = 15, DateTime? periodStart = null);
    Task<List<ApiPerformanceStat>> GetApiPerformanceAsync(int limit = 20, DateTime? periodStart = null);
    Task<CartAnalytics> GetCartAnalyticsAsync(DateTime? periodStart = null);
    Task<List<DailyActiveUserStat>> GetDailyActiveUsersAsync(int days = 30);
    Task<List<HourlyActivityStat>> GetHourlyActivityAsync();
    Task<List<RecentSessionInfo>> GetRecentSessionsAsync(int limit = 50);
    Task EnsureAnalyticsIndexesAsync();
}
