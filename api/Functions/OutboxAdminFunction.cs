using System.Net;
using Cafe.Api.Helpers;
using Cafe.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Cafe.Api.Functions;

public class OutboxAdminFunction
{
    private readonly OutboxService _outbox;
    private readonly AuthService _auth;

    public OutboxAdminFunction(OutboxService outbox, AuthService auth)
    {
        _outbox = outbox;
        _auth = auth;
    }

    [Function("GetOutboxHealthAdmin")]
    public async Task<HttpResponseData> GetOutboxHealth(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/ops/outbox-admin/health")] HttpRequestData req)
    {
        var (isAuth, _, _, authError) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
        if (!isAuth)
        {
            return authError!;
        }

        var summary = await _outbox.GetHealthSummaryAsync();
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(summary);
        return response;
    }

    [Function("GetOutboxDeadLettersAdmin")]
    public async Task<HttpResponseData> GetOutboxDeadLetters(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/ops/outbox-admin/dead-letters")] HttpRequestData req)
    {
        var (isAuth, _, _, authError) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
        if (!isAuth)
        {
            return authError!;
        }

        var limitRaw = req.Query["limit"];
        var limit = int.TryParse(limitRaw, out var parsed) ? parsed : 50;
        var deadLetters = await _outbox.GetDeadLetterMessagesAsync(limit);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(deadLetters.Select(d => new
        {
            d.Id,
            d.EventType,
            d.AggregateType,
            d.AggregateId,
            d.RetryCount,
            d.MaxRetries,
            d.Error,
            d.CreatedAt,
            d.LastAttemptAt,
            d.DeadLetteredAt,
            isPushFailure = !string.IsNullOrWhiteSpace(d.Error)
                            && d.Error.Contains("push", StringComparison.OrdinalIgnoreCase)
        }));
        return response;
    }

    [Function("GetOutboxRetryVisibilityAdmin")]
    public async Task<HttpResponseData> GetOutboxRetryVisibility(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/ops/outbox-admin/retries")] HttpRequestData req)
    {
        var (isAuth, _, _, authError) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
        if (!isAuth)
        {
            return authError!;
        }

        var limitRaw = req.Query["limit"];
        var limit = int.TryParse(limitRaw, out var parsed) ? parsed : 20;
        var retries = await _outbox.GetRetryVisibilityMessagesAsync(limit);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(retries.Select(d => new
        {
            d.Id,
            d.EventType,
            d.AggregateType,
            d.AggregateId,
            d.Status,
            d.RetryCount,
            d.MaxRetries,
            d.Error,
            d.CreatedAt,
            d.LastAttemptAt,
            d.NextRetryAt,
            d.DeadLetteredAt,
            isPushFailure = !string.IsNullOrWhiteSpace(d.Error)
                            && d.Error.Contains("push", StringComparison.OrdinalIgnoreCase)
        }));
        return response;
    }
}
