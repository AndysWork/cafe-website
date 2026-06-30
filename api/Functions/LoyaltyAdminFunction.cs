using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;

namespace Cafe.Api.Functions;

public class LoyaltyAdminFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public LoyaltyAdminFunction(MongoService mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<LoyaltyAdminFunction>();
    }

    // GET: Get all loyalty accounts (Admin only)
    [Function("GetAllLoyaltyAccounts")]
    public async Task<HttpResponseData> GetAllLoyaltyAccounts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/loyalty/accounts")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var (page, pageSize) = Helpers.PaginationHelper.ParsePagination(req);
            var accounts = await _mongo.GetAllLoyaltyAccountsAsync(page, pageSize);

            var accountResponses = accounts.Select(a =>
            {
                var (nextTier, pointsToNextTier) = _mongo.GetNextTierInfo(a.TotalPointsEarned);
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
                    ReferralCode = a.ReferralCode,
                    TotalReferrals = a.TotalReferrals,
                    LoyaltyCardNumber = a.LoyaltyCardNumber,
                    DateOfBirth = a.DateOfBirth,
                    TierMultiplier = _mongo.GetTierMultiplier(a.Tier),
                    TierBenefits = _mongo.GetTierBenefits(a.Tier),
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
            _log.LogError(ex, "Error getting all loyalty accounts");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to get loyalty accounts" });
            return error;
        }
    }

    [Function("GetLoyaltyTierConfig")]
    public async Task<HttpResponseData> GetLoyaltyTierConfig(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/loyalty/tier-config")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) =
                await AuthorizationHelper.ValidateAdminRole(req, _auth);

            if (!isAuthorized)
                return errorResponse!;

            var rules = await _mongo.GetLoyaltyTierRulesAsync();
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(rules.OrderBy(x => x.MinPoints).ThenBy(x => x.DisplayOrder));
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error fetching loyalty tier config");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to get loyalty tier config" });
            return error;
        }
    }

    // Backward-compatible alias for environments still pointing to tier-rules.
    [Function("GetLoyaltyTierRules")]
    public async Task<HttpResponseData> GetLoyaltyTierRules(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/loyalty/tier-rules")] HttpRequestData req)
    {
        return await GetLoyaltyTierConfig(req);
    }

    [Function("UpdateLoyaltyTierConfig")]
    public async Task<HttpResponseData> UpdateLoyaltyTierConfig(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "manage/loyalty/tier-config")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) =
                await AuthorizationHelper.ValidateAdminRole(req, _auth);

            if (!isAuthorized)
                return errorResponse!;

            var (rules, validationError) = await ValidationHelper.ValidateBody<List<UpdateLoyaltyTierRuleRequest>>(req);
            if (validationError != null) return validationError;

            if (rules == null || rules.Count == 0)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "At least one tier configuration is required" });
                return badRequest;
            }

            var duplicatedTier = rules
                .GroupBy(r => (r.Tier ?? string.Empty).Trim().ToLowerInvariant())
                .FirstOrDefault(g => g.Key != string.Empty && g.Count() > 1);

            if (duplicatedTier != null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = $"Duplicate tier found: {duplicatedTier.Key}" });
                return badRequest;
            }

            var models = rules.Select(r => new LoyaltyTierRule
            {
                Tier = r.Tier,
                MinPoints = r.MinPoints,
                Multiplier = r.Multiplier,
                BirthdayBonusPoints = r.BirthdayBonusPoints,
                Benefits = r.Benefits ?? new List<string>(),
                Color = r.Color,
                DisplayOrder = r.DisplayOrder,
                IsActive = r.IsActive
            }).ToList();

            var updated = await _mongo.UpdateLoyaltyTierRulesAsync(models);

            if (!updated)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Failed to update loyalty tier config" });
                return badRequest;
            }

            var refreshed = await _mongo.GetLoyaltyTierRulesAsync();
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, message = "Tier configuration updated", rules = refreshed });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating loyalty tier config");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to update loyalty tier config" });
            return error;
        }
    }

    // Backward-compatible alias for environments still pointing to tier-rules.
    [Function("UpdateLoyaltyTierRules")]
    public async Task<HttpResponseData> UpdateLoyaltyTierRules(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "manage/loyalty/tier-rules")] HttpRequestData req)
    {
        return await UpdateLoyaltyTierConfig(req);
    }

    // POST: Create reward (Admin only)
    [Function("CreateReward")]
    public async Task<HttpResponseData> CreateReward(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/loyalty/rewards")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var (reward, validationError) = await ValidationHelper.ValidateBody<Reward>(req);
            if (validationError != null) return validationError;

            if (reward.PointsCost <= 0)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Points cost must be greater than 0" });
                return badRequest;
            }

            var createdReward = await _mongo.CreateRewardAsync(reward);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(createdReward);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating reward");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to create reward" });
            return error;
        }
    }

    // GET: Get all rewards including inactive (Admin only)
    [Function("GetAllRewards")]
    public async Task<HttpResponseData> GetAllRewards(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/loyalty/rewards")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var rewards = await _mongo.GetAllRewardsAsync();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(rewards);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting all rewards");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to get rewards" });
            return error;
        }
    }

    // PUT: Update reward (Admin only)
    [Function("UpdateReward")]
    public async Task<HttpResponseData> UpdateReward(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "manage/loyalty/rewards/{id}")] HttpRequestData req,
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
            _log.LogError(ex, "Error updating reward");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to update reward" });
            return error;
        }
    }

    // DELETE: Delete reward (Admin only)
    [Function("DeleteReward")]
    public async Task<HttpResponseData> DeleteReward(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "manage/loyalty/rewards/{id}")] HttpRequestData req,
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
            _log.LogError(ex, "Error deleting reward");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to delete reward" });
            return error;
        }
    }

    // GET: Get all redemption history (Admin only)
    [Function("GetAllRedemptions")]
    public async Task<HttpResponseData> GetAllRedemptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/loyalty/redemptions")] HttpRequestData req)
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
            _log.LogError(ex, "Error getting all redemptions");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to get redemptions" });
            return error;
        }
    }
}
