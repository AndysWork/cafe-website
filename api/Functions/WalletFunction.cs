using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;

namespace Cafe.Api.Functions;

public class WalletFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public WalletFunction(MongoService mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<WalletFunction>();
    }

    [Function("GetMyWallet")]
    public async Task<HttpResponseData> GetMyWallet(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "wallet")] HttpRequestData req)
    {
        try
        {
            var (isAuthenticated, userId, _, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthenticated) return errorResponse!;

            var wallet = await _mongo.GetOrCreateWalletAsync(userId!);
            var transactions = await _mongo.GetWalletTransactionsAsync(userId!, 1, 10);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new WalletResponse
            {
                Id = wallet.Id!,
                Balance = wallet.Balance,
                TotalCredited = wallet.TotalCredited,
                TotalDebited = wallet.TotalDebited,
                RecentTransactions = transactions
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting wallet");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while retrieving your wallet" });
            return res;
        }
    }

    [Function("GetWalletTransactions")]
    public async Task<HttpResponseData> GetWalletTransactions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "wallet/transactions")] HttpRequestData req)
    {
        try
        {
            var (isAuthenticated, userId, _, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthenticated) return errorResponse!;

            var (page, pageSize) = PaginationHelper.ParsePagination(req);
            var transactions = await _mongo.GetWalletTransactionsAsync(userId!, page ?? 1, pageSize ?? 20);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(transactions);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting wallet transactions");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while retrieving transactions" });
            return res;
        }
    }

    [Function("RechargeWallet")]
    public async Task<HttpResponseData> RechargeWallet(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "wallet/recharge")] HttpRequestData req)
    {
        try
        {
            var (isAuthenticated, userId, _, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthenticated) return errorResponse!;

            var request = await req.ReadFromJsonAsync<WalletRechargeRequest>();
            if (request == null || request.Amount <= 0)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid recharge amount" });
                return badRequest;
            }

            if (!ValidationHelper.TryValidate(request, out var validationError))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(validationError!.Value);
                return badRequest;
            }

            // Calculate cashback bonus (5% on recharges >= 500)
            decimal bonusAmount = 0;
            if (request.Amount >= 500)
            {
                bonusAmount = Math.Round(request.Amount * 0.05m, 2);
            }

            var txn = await _mongo.CreditWalletAsync(userId!, request.Amount,
                $"Wallet recharge of ₹{request.Amount:F2}", "recharge",
                razorpayPaymentId: request.RazorpayPaymentId);

            if (bonusAmount > 0)
            {
                await _mongo.CreditWalletAsync(userId!, bonusAmount,
                    $"5% cashback on ₹{request.Amount:F2} recharge", "cashback");
            }

            var wallet = await _mongo.GetWalletAsync(userId!);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = bonusAmount > 0
                    ? $"Wallet recharged with ₹{request.Amount:F2} + ₹{bonusAmount:F2} cashback!"
                    : $"Wallet recharged with ₹{request.Amount:F2}",
                balance = wallet?.Balance ?? 0,
                bonusAmount
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error recharging wallet");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while recharging your wallet" });
            return res;
        }
    }
}
