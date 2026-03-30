using Cafe.Api.Models;

namespace Cafe.Api.Repositories;

public interface IUserRepository
{
    Task<User?> GetUserByUsernameAsync(string username);
    Task<User?> GetUserByEmailAsync(string email);
    Task<User?> GetUserByIdAsync(string id);
    Task<User> CreateUserAsync(User user);
    Task UpdateUserLastLoginAsync(string userId);
    Task<bool> UpdateUserRoleAsync(string userId, string role);
    Task<List<User>> GetAllUsersAsync();
    Task<bool> UpdateUserActiveStatusAsync(string userId, bool isActive);
    Task<bool> UpdateUserProfileAsync(string userId, UpdateProfileRequest profile);
    Task<bool> UpdateProfilePictureUrlAsync(string userId, string? profilePictureUrl);
    Task<bool> UpdateUserPasswordAsync(string userId, string newPasswordHash);

    // Addresses
    Task<List<DeliveryAddress>> GetUserAddressesAsync(string userId);
    Task<DeliveryAddress> AddUserAddressAsync(string userId, DeliveryAddress address);
    Task<bool> UpdateUserAddressAsync(string userId, string addressId, UpdateDeliveryAddressRequest req);
    Task<bool> DeleteUserAddressAsync(string userId, string addressId);

    // Favorites
    Task<List<string>> GetUserFavoritesAsync(string userId);
    Task<bool> ToggleFavoriteAsync(string userId, string menuItemId);

    // Password Reset
    Task<PasswordResetToken> CreatePasswordResetTokenAsync(string userId);
    Task<PasswordResetToken?> GetPasswordResetTokenAsync(string token);
    Task<bool> MarkPasswordResetTokenAsUsedAsync(string tokenId);
    Task DeleteExpiredPasswordResetTokensAsync();
    Task<bool> ResetAdminPasswordAsync(string newPassword);

    // Admin helpers
    Task<List<string>> GetAdminUserIdsAsync();
}
