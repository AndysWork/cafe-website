using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;
using System.Security.Claims;

namespace Cafe.Api.Functions;

public class LoyaltyFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public LoyaltyFunction(MongoService mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<LoyaltyFunction>();
    }

    // GET: Get user's loyalty account
    [Function("GetLoyaltyAccount")]
    public async Task<HttpResponseData> GetLoyaltyAccount(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "loyalty")] HttpRequestData req)
    {
        try
        {
            // Validate authentication
            var authHeader = req.Headers.GetValues("Authorization").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Authentication required" });
                return unauthorized;
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();
            var principal = _auth.ValidateToken(token);

            if (principal == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Invalid or expired token" });
                return unauthorized;
            }

            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var username = principal.FindFirst(ClaimTypes.Name)?.Value;

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(username))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid user information" });
                return badRequest;
            }

            // Get or create loyalty account
            var account = await _mongo.GetOrCreateLoyaltyAccountAsync(userId, username);

            // Calculate next tier info
            var (nextTier, pointsToNextTier) = CalculateNextTierInfo(account.TotalPointsEarned);

            var response = new LoyaltyAccountResponse
            {
                Id = account.Id!,
                UserId = account.UserId,
                Username = account.Username,
                CurrentPoints = account.CurrentPoints,
                TotalPointsEarned = account.TotalPointsEarned,
                TotalPointsRedeemed = account.TotalPointsRedeemed,
                Tier = account.Tier,
                NextTier = nextTier,
                PointsToNextTier = pointsToNextTier,
                CreatedAt = account.CreatedAt,
                UpdatedAt = account.UpdatedAt
            };

            var result = req.CreateResponse(HttpStatusCode.OK);
            await result.WriteAsJsonAsync(response);
            return result;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error getting loyalty account: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to get loyalty account" });
            return error;
        }
    }

    // GET: Get user's transaction history
    [Function("GetLoyaltyTransactions")]
    public async Task<HttpResponseData> GetLoyaltyTransactions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "loyalty/transactions")] HttpRequestData req)
    {
        try
        {
            // Validate authentication
            var authHeader = req.Headers.GetValues("Authorization").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Authentication required" });
                return unauthorized;
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();
            var principal = _auth.ValidateToken(token);

            if (principal == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Invalid or expired token" });
                return unauthorized;
            }

            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid user information" });
                return badRequest;
            }

            // Get transactions
            var transactions = await _mongo.GetUserTransactionsAsync(userId);

            var transactionResponses = transactions.Select(t => new PointsTransactionResponse
            {
                Id = t.Id!,
                Points = t.Points,
                Type = t.Type,
                Description = t.Description,
                OrderId = t.OrderId,
                RewardId = t.RewardId,
                CreatedAt = t.CreatedAt
            }).ToList();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(transactionResponses);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error getting transactions: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to get transactions" });
            return error;
        }
    }

    // GET: Get available rewards
    [Function("GetActiveRewards")]
    public async Task<HttpResponseData> GetActiveRewards(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "loyalty/rewards")] HttpRequestData req)
    {
        try
        {
            // Get current user's points (if authenticated)
            int userPoints = 0;
            var authHeader = req.Headers.GetValues("Authorization").FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer "))
            {
                var token = authHeader.Substring("Bearer ".Length).Trim();
                var principal = _auth.ValidateToken(token);
                var userId = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (!string.IsNullOrEmpty(userId))
                {
                    var account = await _mongo.GetLoyaltyAccountByUserIdAsync(userId);
                    userPoints = account?.CurrentPoints ?? 0;
                }
            }

            // Get active rewards
            var rewards = await _mongo.GetActiveRewardsAsync();

            var rewardResponses = rewards.Select(r => new RewardResponse
            {
                Id = r.Id!,
                Name = r.Name,
                Description = r.Description,
                PointsCost = r.PointsCost,
                Icon = r.Icon,
                IsActive = r.IsActive,
                ExpiresAt = r.ExpiresAt,
                CanRedeem = userPoints >= r.PointsCost
            }).ToList();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(rewardResponses);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error getting rewards: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to get rewards" });
            return error;
        }
    }

    // POST: Redeem a reward
    [Function("RedeemReward")]
    public async Task<HttpResponseData> RedeemReward(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "loyalty/redeem/{rewardId}")] HttpRequestData req,
        string rewardId)
    {
        try
        {
            // Validate authentication
            var (isAuthorized, userId, role, errorResponse) = 
                await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            
            if (!isAuthorized || string.IsNullOrEmpty(userId))
                return errorResponse!;

            // Redeem reward
            var redemptionResult = await _mongo.RedeemRewardAsync(userId, rewardId);

            if (!redemptionResult.Success)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = redemptionResult.Message });
                return badRequest;
            }

            // Calculate next tier info
            var (nextTier, pointsToNextTier) = CalculateNextTierInfo(redemptionResult.Account!.TotalPointsEarned);

            var accountResponse = new LoyaltyAccountResponse
            {
                Id = redemptionResult.Account.Id!,
                UserId = redemptionResult.Account.UserId,
                Username = redemptionResult.Account.Username,
                CurrentPoints = redemptionResult.Account.CurrentPoints,
                TotalPointsEarned = redemptionResult.Account.TotalPointsEarned,
                TotalPointsRedeemed = redemptionResult.Account.TotalPointsRedeemed,
                Tier = redemptionResult.Account.Tier,
                NextTier = nextTier,
                PointsToNextTier = pointsToNextTier,
                CreatedAt = redemptionResult.Account.CreatedAt,
                UpdatedAt = redemptionResult.Account.UpdatedAt
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                message = redemptionResult.Message,
                account = accountResponse
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error redeeming reward: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to redeem reward" });
            return error;
        }
    }

    // GET: Get all loyalty accounts (Admin only)
    [Function("GetAllLoyaltyAccounts")]
    public async Task<HttpResponseData> GetAllLoyaltyAccounts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/admin/loyalty/accounts")] HttpRequestData req)
    {
        try
        {
            // Validate admin authorization
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            // Get all accounts
            var accounts = await _mongo.GetAllLoyaltyAccountsAsync();

            var accountResponses = accounts.Select(a =>
            {
                var (nextTier, pointsToNextTier) = CalculateNextTierInfo(a.TotalPointsEarned);
                return new LoyaltyAccountResponse
                {
                    Id = a.Id!,
                    UserId = a.UserId,
                    Username = a.Username,
                    CurrentPoints = a.CurrentPoints,
                    TotalPointsEarned = a.TotalPointsEarned,
                    TotalPointsRedeemed = a.TotalPointsRedeemed,
                    Tier = a.Tier,
                    NextTier = nextTier,
                    PointsToNextTier = pointsToNextTier,
                    CreatedAt = a.CreatedAt,
                    UpdatedAt = a.UpdatedAt
                };
            }).ToList();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(accountResponses);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error getting all loyalty accounts: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to get loyalty accounts" });
            return error;
        }
    }

    // POST: Create reward (Admin only)
    [Function("CreateReward")]
    public async Task<HttpResponseData> CreateReward(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/admin/loyalty/rewards")] HttpRequestData req)
    {
        try
        {
            // Validate admin authorization
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            // Parse request
            var reward = await req.ReadFromJsonAsync<Reward>();
            if (reward == null || string.IsNullOrWhiteSpace(reward.Name))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid reward data" });
                return badRequest;
            }

            if (reward.PointsCost <= 0)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Points cost must be greater than 0" });
                return badRequest;
            }

            // Create reward
            var createdReward = await _mongo.CreateRewardAsync(reward);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(createdReward);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error creating reward: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to create reward" });
            return error;
        }
    }

    // GET: Get all rewards including inactive (Admin only)
    [Function("GetAllRewards")]
    public async Task<HttpResponseData> GetAllRewards(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/admin/loyalty/rewards")] HttpRequestData req)
    {
        try
        {
            // Validate admin authorization
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            // Get all rewards
            var rewards = await _mongo.GetAllRewardsAsync();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(rewards);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error getting all rewards: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to get rewards" });
            return error;
        }
    }

    // Helper method to calculate next tier
    private (string? NextTier, int? PointsToNextTier) CalculateNextTierInfo(int totalPoints)
    {
        if (totalPoints < 500)
            return ("Silver", 500 - totalPoints);
        if (totalPoints < 1500)
            return ("Gold", 1500 - totalPoints);
        if (totalPoints < 3000)
            return ("Platinum", 3000 - totalPoints);
        return (null, null); // Already at max tier
    }

    // GET: Get all redemption history (Admin only)
    [Function("GetAllRedemptions")]
    public async Task<HttpResponseData> GetAllRedemptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/admin/loyalty/redemptions")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var transactions = await _mongo.GetAllTransactionsAsync();
            var redemptions = transactions.Where(t => t.Type == "redeemed").ToList();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(redemptions);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error getting all redemptions: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to get redemptions" });
            return error;
        }
    }

    // PUT: Update reward (Admin only)
    [Function("UpdateReward")]
    public async Task<HttpResponseData> UpdateReward(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "api/admin/loyalty/rewards/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var reward = await System.Text.Json.JsonSerializer.DeserializeAsync<Reward>(req.Body);
            if (reward == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid reward data" });
                return badRequest;
            }

            reward.Id = id;
            var updated = await _mongo.UpdateRewardAsync(id, reward);

            if (!updated)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Reward not found" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(reward);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error updating reward: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to update reward" });
            return error;
        }
    }

    // DELETE: Delete reward (Admin only)
    [Function("DeleteReward")]
    public async Task<HttpResponseData> DeleteReward(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "api/admin/loyalty/rewards/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var deleted = await _mongo.DeleteRewardAsync(id);

            if (!deleted)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Reward not found" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Reward deleted successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error deleting reward: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to delete reward" });
            return error;
        }
    }
}
