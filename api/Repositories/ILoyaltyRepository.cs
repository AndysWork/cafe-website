using Cafe.Api.Models;

namespace Cafe.Api.Repositories;

public interface ILoyaltyRepository
{
    // Loyalty Accounts
    Task<LoyaltyAccount> GetOrCreateLoyaltyAccountAsync(string userId, string username);
    Task<LoyaltyAccount?> GetLoyaltyAccountByUserIdAsync(string userId);
    Task<List<LoyaltyAccount>> GetAllLoyaltyAccountsAsync(int? page = null, int? pageSize = null);
    Task<long> GetAllLoyaltyAccountsCountAsync();
    Task<LoyaltyAccount?> AwardPointsAsync(string userId, int points, string description, string? orderId = null);
    Task<(bool Success, string Message, LoyaltyAccount? Account)> RedeemRewardAsync(string userId, string rewardId);
    Task<List<PointsTransaction>> GetUserTransactionsAsync(string userId);
    Task<LoyaltyAccount?> GetLoyaltyAccountAsync(string userId);
    Task<bool> DeductLoyaltyPointsAsync(string userId, int points, string description);
    Task<List<PointsTransaction>> GetAllTransactionsAsync();

    // Rewards
    Task<List<Reward>> GetActiveRewardsAsync();
    Task<List<Reward>> GetAllRewardsAsync();
    Task<Reward> CreateRewardAsync(Reward reward);
    Task<bool> UpdateRewardAsync(string id, Reward reward);
    Task<bool> DeleteRewardAsync(string id);

    // Transfer & Referral
    Task<(bool Success, string Message)> TransferPointsAsync(string fromUserId, string toUsername, int points);
    Task<LoyaltyAccount?> GetLoyaltyAccountByReferralCodeAsync(string referralCode);
    Task<(bool Success, string Message)> ApplyReferralCodeAsync(string userId, string referralCode);

    // Birthday
    Task<(bool Success, string Message)> SetBirthdayAsync(string userId, DateTime dateOfBirth);
    Task<(bool Awarded, int Points)> CheckAndAwardBirthdayBonusAsync(string userId);
    bool IsBirthdayBonusAvailable(LoyaltyAccount account);
    int GetBirthdayBonusPoints(string tier);

    // Expiration
    Task<int> ProcessExpiredPointsAsync(string userId);
    Task<(int ExpiringPoints, DateTime? NextExpiryDate)> GetExpiringPointsInfoAsync(string userId);

    // External Order Claims
    Task CreateExternalClaimAsync(ExternalOrderClaim claim);
    Task<ExternalOrderClaim?> GetExternalClaimByIdAsync(string id);
    Task<List<ExternalOrderClaim>> GetUserExternalClaimsAsync(string userId);
    Task<List<ExternalOrderClaim>> GetPendingExternalClaimsAsync();
    Task<List<ExternalOrderClaim>> GetAllExternalClaimsAsync(string? status = null, int page = 1, int pageSize = 20);
    Task<long> CountExternalClaimsAsync(string? status = null);
    Task<bool> UpdateExternalClaimStatusAsync(string id, string status, string? adminNotes, string? reviewedBy, int? overridePoints = null);
}
