using Cafe.Api.Models;

namespace Cafe.Api.Repositories;

public interface INotificationRepository
{
    Task CreateNotificationAsync(AppNotification notification);
    Task<List<AppNotification>> GetUserNotificationsAsync(string userId, int page = 1, int pageSize = 20);
    Task<long> GetUnreadNotificationCountAsync(string userId);
    Task<long> GetTotalNotificationCountAsync(string userId);
    Task<bool> MarkNotificationAsReadAsync(string notificationId, string userId);
    Task<long> MarkAllNotificationsAsReadAsync(string userId);
    Task<bool> DeleteNotificationAsync(string notificationId, string userId);
    Task<long> DeleteAllNotificationsAsync(string userId);
    Task<NotificationPreferences> GetNotificationPreferencesAsync(string userId);
    Task<bool> UpdateNotificationPreferencesAsync(string userId, NotificationPreferences preferences);
    Task<List<string>> GetAdminUserIdsAsync();
}
