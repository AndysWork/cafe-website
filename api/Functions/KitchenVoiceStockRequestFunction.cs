using System.Net;
using System.Web;
using Cafe.Api.Helpers;
using Cafe.Api.Models;
using Cafe.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Functions;

public class KitchenVoiceStockRequestFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly OutboxService _outbox;
    private readonly ILogger _logger;

    public KitchenVoiceStockRequestFunction(MongoService mongo, AuthService auth, OutboxService outbox, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _outbox = outbox;
        _logger = loggerFactory.CreateLogger<KitchenVoiceStockRequestFunction>();
    }

    [Function("CreateKitchenVoiceStockRequest")]
    public async Task<HttpResponseData> CreateKitchenVoiceStockRequest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "kitchen/stock-requests/voice")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, role, errorResponse) = await AuthorizationHelper.ValidateKitchenAccessRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (body, validationError) = await ValidationHelper.ValidateBody<CreateKitchenVoiceStockRequest>(req);
            if (validationError != null) return validationError;

            if (body == null)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Invalid request body" });
                return badReq;
            }

            var transcript = (body.TranscriptText ?? string.Empty).Trim();
            var maxTranscriptChars = int.TryParse(Environment.GetEnvironmentVariable("Stt__MaxTranscriptChars"), out var configuredMax)
                ? Math.Clamp(configuredMax, 100, 5000)
                : 800;

            if (transcript.Length > maxTranscriptChars)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = $"Transcript exceeds {maxTranscriptChars} characters" });
                return badReq;
            }

            var requestedItems = NormalizeRequestedItems(body.RequestedItems, transcript);
            if (!requestedItems.Any() && string.IsNullOrWhiteSpace(transcript))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Provide at least one requested item or transcript text" });
                return badReq;
            }

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            if (string.IsNullOrWhiteSpace(outletId))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Outlet context is required" });
                return badReq;
            }

            var user = !string.IsNullOrWhiteSpace(userId) ? await _mongo.GetUserByIdAsync(userId) : null;
            var requestedByName = user != null
                ? $"{user.FirstName} {user.LastName}".Trim()
                : string.Empty;

            if (string.IsNullOrWhiteSpace(requestedByName))
            {
                requestedByName = user?.Username ?? "Kitchen Staff";
            }

            var created = await _mongo.CreateKitchenVoiceStockRequestAsync(new KitchenVoiceStockRequest
            {
                OutletId = outletId,
                RequestedByUserId = userId ?? string.Empty,
                RequestedByName = requestedByName,
                RequestedByRole = role ?? "kitchen",
                TranscriptText = transcript,
                RequestedItems = requestedItems,
                SttProvider = string.IsNullOrWhiteSpace(body.SttProvider) ? Environment.GetEnvironmentVariable("Stt__Provider") ?? "web-speech" : body.SttProvider,
                SttConfidence = body.SttConfidence,
                Status = "pending"
            });

            if (!string.IsNullOrWhiteSpace(created.Id))
            {
                await _outbox.EnqueueAsync(
                    "KitchenVoiceStockRequestAdminNotification",
                    "KitchenVoiceStockRequest",
                    created.Id,
                    new KitchenVoiceStockRequestAdminNotificationPayload(
                        created.Id,
                        created.OutletId,
                        created.RequestedByName,
                        created.RequestedItems,
                        created.TranscriptText));
            }

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(created);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating kitchen voice stock request");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("GetAdminKitchenVoiceStockRequests")]
    public async Task<HttpResponseData> GetAdminKitchenVoiceStockRequests(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/kitchen/stock-requests/voice")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var query = HttpUtility.ParseQueryString(req.Url.Query);
            var status = query["status"];
            var outletId = query["outletId"];
            var limit = int.TryParse(query["limit"], out var parsedLimit) ? parsedLimit : 100;

            var items = await _mongo.GetKitchenVoiceStockRequestsAsync(outletId, status, limit);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                items,
                count = items.Count,
                pendingCount = items.Count(i => i.Status == "pending")
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching kitchen voice stock requests");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("ReviewKitchenVoiceStockRequest")]
    public async Task<HttpResponseData> ReviewKitchenVoiceStockRequest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "manage/kitchen/stock-requests/voice/{id}/decision")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, adminUserId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (body, validationError) = await ValidationHelper.ValidateBody<ReviewKitchenVoiceStockRequest>(req);
            if (validationError != null) return validationError;

            if (body == null)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Invalid request body" });
                return badReq;
            }

            var decision = body.Decision.Trim().ToLowerInvariant();
            if (decision != "approved" && decision != "rejected")
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Decision must be approved or rejected" });
                return badReq;
            }

            var existing = await _mongo.GetKitchenVoiceStockRequestByIdAsync(id);
            if (existing == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Request not found" });
                return notFound;
            }

            if (!string.Equals(existing.Status, "pending", StringComparison.OrdinalIgnoreCase))
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = "Only pending requests can be reviewed" });
                return conflict;
            }

            var adminUser = !string.IsNullOrWhiteSpace(adminUserId) ? await _mongo.GetUserByIdAsync(adminUserId) : null;
            var reviewedByName = adminUser != null
                ? $"{adminUser.FirstName} {adminUser.LastName}".Trim()
                : string.Empty;

            if (string.IsNullOrWhiteSpace(reviewedByName))
            {
                reviewedByName = adminUser?.Username ?? "Admin";
            }

            var reviewed = await _mongo.ReviewKitchenVoiceStockRequestAsync(
                id,
                decision,
                adminUserId ?? string.Empty,
                reviewedByName,
                body.Note);

            if (!reviewed)
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = "Request could not be updated" });
                return conflict;
            }

            var updated = await _mongo.GetKitchenVoiceStockRequestByIdAsync(id);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = $"Request {decision}",
                item = updated
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reviewing kitchen voice stock request {RequestId}", id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    private static List<string> NormalizeRequestedItems(List<string>? requestedItems, string transcript)
    {
        var items = new List<string>();

        if (requestedItems != null)
        {
            items.AddRange(requestedItems);
        }

        if (!string.IsNullOrWhiteSpace(transcript))
        {
            var split = transcript
                .Replace(" and ", ",", StringComparison.OrdinalIgnoreCase)
                .Replace("\n", ",")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            items.AddRange(split);
        }

        return items
            .Select(i => i.Trim())
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .ToList();
    }

    private record KitchenVoiceStockRequestAdminNotificationPayload(
        string RequestId,
        string OutletId,
        string RequestedByName,
        List<string> RequestedItems,
        string TranscriptText);
}
