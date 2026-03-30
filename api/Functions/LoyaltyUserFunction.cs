using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;
using System.Security.Claims;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;

namespace Cafe.Api.Functions;

public class LoyaltyUserFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;
    private readonly IWhatsAppService _whatsApp;

    public LoyaltyUserFunction(MongoService mongo, AuthService auth, IWhatsAppService whatsApp, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _whatsApp = whatsApp;
        _log = loggerFactory.CreateLogger<LoyaltyUserFunction>();
    }

    // GET: Get user's loyalty account
    [Function("GetLoyaltyAccount")]
    [OpenApiOperation(operationId: "GetLoyaltyAccount", tags: new[] { "Loyalty" }, Summary = "Get loyalty account", Description = "Retrieves the authenticated user's loyalty account")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(LoyaltyAccountResponse), Description = "Successfully retrieved loyalty account")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.BadRequest, Description = "Invalid user information")]
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

            // Process any expired points
            await _mongo.ProcessExpiredPointsAsync(userId);
            // Refresh account after expiry processing
            account = await _mongo.GetOrCreateLoyaltyAccountAsync(userId, username);

            // Check birthday bonus
            var birthdayAvailable = _mongo.IsBirthdayBonusAvailable(account);
            var birthdayBonusPoints = _mongo.GetBirthdayBonusPoints(account.Tier);

            // Get expiring points info
            var (expiringPoints, expiringDate) = await _mongo.GetExpiringPointsInfoAsync(userId);

            // Calculate next tier info
            var (nextTier, pointsToNextTier) = LoyaltyHelper.CalculateNextTierInfo(account.TotalPointsEarned);

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
                ReferralCode = account.ReferralCode,
                TotalReferrals = account.TotalReferrals,
                LoyaltyCardNumber = account.LoyaltyCardNumber,
                DateOfBirth = account.DateOfBirth,
                TierMultiplier = _mongo.GetTierMultiplier(account.Tier),
                TierBenefits = _mongo.GetTierBenefits(account.Tier),
                ExpiringPoints = expiringPoints,
                ExpiringDate = expiringDate,
                BirthdayBonusAvailable = birthdayAvailable,
                BirthdayBonusPoints = birthdayBonusPoints,
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
                ExpiresAt = t.ExpiresAt,
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

            // Send WhatsApp notification about reward redemption
            try
            {
                var user = await _mongo.GetUserByIdAsync(userId);
                if (user != null && !string.IsNullOrEmpty(user.PhoneNumber))
                {
                    var notificationMessage = !string.IsNullOrWhiteSpace(redemptionResult.Message)
                        ? redemptionResult.Message
                        : "Redeemed a reward";
                    
                    await _whatsApp.SendLoyaltyNotificationAsync(
                        user.PhoneNumber,
                        redemptionResult.Account?.Username ?? "Customer",
                        0, // No new points earned
                        redemptionResult.Account?.TotalPointsEarned ?? 0,
                        notificationMessage
                    );
                }
            }
            catch (Exception whatsAppEx)
            {
                _log.LogWarning(whatsAppEx, "Failed to send WhatsApp notification for reward redemption");
                // Don't fail the redemption if WhatsApp sending fails
            }

            // Calculate next tier info
            var (nextTier, pointsToNextTier) = LoyaltyHelper.CalculateNextTierInfo(redemptionResult.Account!.TotalPointsEarned);

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
                ReferralCode = redemptionResult.Account.ReferralCode,
                TotalReferrals = redemptionResult.Account.TotalReferrals,
                LoyaltyCardNumber = redemptionResult.Account.LoyaltyCardNumber,
                DateOfBirth = redemptionResult.Account.DateOfBirth,
                TierMultiplier = _mongo.GetTierMultiplier(redemptionResult.Account.Tier),
                TierBenefits = _mongo.GetTierBenefits(redemptionResult.Account.Tier),
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

    // POST: Transfer points to another user
    [Function("TransferPoints")]
    [OpenApiOperation(operationId: "TransferPoints", tags: new[] { "Loyalty" }, Summary = "Transfer points", Description = "Transfer loyalty points to another user")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(TransferPointsRequest))]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object))]
    public async Task<HttpResponseData> TransferPoints(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "loyalty/transfer")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, role, errorResponse) =
                await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized || string.IsNullOrEmpty(userId))
                return errorResponse!;

            var (body, validationError) = await ValidationHelper.ValidateBody<TransferPointsRequest>(req);
            if (validationError != null) return validationError;

            if (body.Points <= 0)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Points amount must be greater than 0" });
                return badRequest;
            }

            var sanitizedUsername = InputSanitizer.Sanitize(body.RecipientUsername);

            var (success, message) = await _mongo.TransferPointsAsync(userId, sanitizedUsername, body.Points);

            if (!success)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = message });
                return badReq;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, message });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error transferring points: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to transfer points" });
            return error;
        }
    }

    // POST: Apply referral code
    [Function("ApplyReferralCode")]
    [OpenApiOperation(operationId: "ApplyReferralCode", tags: new[] { "Loyalty" }, Summary = "Apply referral code", Description = "Apply a referral code to earn bonus points")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(ApplyReferralRequest))]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object))]
    public async Task<HttpResponseData> ApplyReferralCode(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "loyalty/referral/apply")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, role, errorResponse) =
                await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized || string.IsNullOrEmpty(userId))
                return errorResponse!;

            var (body, validationError) = await ValidationHelper.ValidateBody<ApplyReferralRequest>(req);
            if (validationError != null) return validationError;

            var sanitizedCode = InputSanitizer.Sanitize(body.ReferralCode).Trim().ToUpper();

            var (success, message) = await _mongo.ApplyReferralCodeAsync(userId, sanitizedCode);

            if (!success)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = message });
                return badReq;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, message });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error applying referral code: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to apply referral code" });
            return error;
        }
    }

    // PUT: Set birthday
    [Function("SetBirthday")]
    [OpenApiOperation(operationId: "SetBirthday", tags: new[] { "Loyalty" }, Summary = "Set birthday", Description = "Set your date of birth for birthday rewards")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(SetBirthdayRequest))]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object))]
    public async Task<HttpResponseData> SetBirthday(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "loyalty/birthday")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, role, errorResponse) =
                await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized || string.IsNullOrEmpty(userId))
                return errorResponse!;

            var (body, validationError) = await ValidationHelper.ValidateBody<SetBirthdayRequest>(req);
            if (validationError != null) return validationError;

            var (success, message) = await _mongo.SetBirthdayAsync(userId, body.DateOfBirth);

            if (!success)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = message });
                return badReq;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, message });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error setting birthday: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to set birthday" });
            return error;
        }
    }

    // POST: Claim birthday bonus
    [Function("ClaimBirthdayBonus")]
    [OpenApiOperation(operationId: "ClaimBirthdayBonus", tags: new[] { "Loyalty" }, Summary = "Claim birthday bonus", Description = "Claim your annual birthday bonus points")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object))]
    public async Task<HttpResponseData> ClaimBirthdayBonus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "loyalty/birthday/claim")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, role, errorResponse) =
                await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized || string.IsNullOrEmpty(userId))
                return errorResponse!;

            var (awarded, points) = await _mongo.CheckAndAwardBirthdayBonusAsync(userId);

            if (!awarded)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Birthday bonus not available. Either it's not your birthday, you've already claimed it this year, or your birthday is not set." });
                return badReq;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, message = $"Happy Birthday! You earned {points} bonus points! 🎂", points });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error claiming birthday bonus: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to claim birthday bonus" });
            return error;
        }
    }
}
