using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Cafe.Api.Helpers;
using Cafe.Api.Models;
using Cafe.Api.Repositories;
using Cafe.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Functions;

public sealed class CustomerJourneyFunction(
    ICustomerJourneyService journeys, AuthService auth, IOutletRepository outlets,
    IOrderRepository orders, ILogger<CustomerJourneyFunction> log)
{
    [Function("CreateJourneySession")]
    [OpenApiOperation("CreateJourneySession", new[] { "CustomerJourney" }, Summary = "Opt into retained journey analytics")]
    public Task<HttpResponseData> CreateSession(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "analytics/journey/sessions")] HttpRequestData req,
        CancellationToken ct) => Execute(req, async () =>
    {
        var (body, error) = await ValidationHelper.ValidateBody<CreateJourneySessionRequest>(req);
        if (error != null) return error;
        ClaimsPrincipal? principal = null;
        if (req.Headers.TryGetValues("Authorization", out var headers))
        {
            var header = headers.FirstOrDefault();
            if (header?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) != true ||
                (principal = auth.ValidateToken(header[7..].Trim())) == null)
                return await Json(req, HttpStatusCode.Unauthorized,
                    new { error = "Sign in again before starting an authenticated journey." }, ct);
        }
        var session = await journeys.CreateSessionAsync(body, principal, ct);
        var response = await Json(req, HttpStatusCode.Created, session, ct);
        response.Headers.Add("Location", $"/api/analytics/journey/sessions/{session.Context.SessionId}");
        return response;
    }, ct);

    [Function("TrackJourneyEvents")]
    [OpenApiOperation("TrackJourneyEvents", new[] { "CustomerJourney" }, Summary = "Ingest consented, idempotent browser events")]
    public Task<HttpResponseData> Track(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "analytics/journey/events")] HttpRequestData req,
        CancellationToken ct) => Execute(req, async () =>
    {
        var (body, error) = await ValidationHelper.ValidateBody<TrackJourneyBatchRequest>(req);
        if (error != null) return error;
        await journeys.TrackAsync(body, ct);
        return req.CreateResponse(HttpStatusCode.NoContent);
    }, ct);

    [Function("RevokeJourneyConsent")]
    [OpenApiOperation("RevokeJourneyConsent", new[] { "CustomerJourney" }, Summary = "Stop and erase this session's analytics")]
    public Task<HttpResponseData> Revoke(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "analytics/journey/revoke")] HttpRequestData req,
        CancellationToken ct) => Execute(req, async () =>
    {
        var (body, error) = await ValidationHelper.ValidateBody<RevokeJourneyRequest>(req);
        if (error != null) return error;
        await journeys.RevokeAsync(body.Context, ct);
        return req.CreateResponse(HttpStatusCode.NoContent);
    }, ct);

    [Function("ConfirmJourneyOrder")]
    [OpenApiOperation("ConfirmJourneyOrder", new[] { "CustomerJourney" }, Summary = "Confirm a real owned order after a telemetry delivery failure")]
    public Task<HttpResponseData> ConfirmOrder(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "analytics/journey/orders")] HttpRequestData req,
        CancellationToken ct) => Execute(req, async () =>
    {
        var (authorized, userId, _, authError) = await AuthorizationHelper.ValidateAuthenticatedUser(req, auth);
        if (!authorized) return authError!;
        var (body, error) = await ValidationHelper.ValidateBody<ConfirmJourneyOrderRequest>(req);
        if (error != null) return error;
        if (!MongoDB.Bson.ObjectId.TryParse(body.OrderId, out _)) throw new ArgumentException("Invalid order ID.");
        var order = await orders.GetOrderByIdAsync(body.OrderId);
        if (order == null || order.UserId != userId)
            return await Json(req, HttpStatusCode.NotFound, new { error = "Order not found." }, ct);
        await journeys.ConfirmOrderAsync(body.Context, order, ct);
        return req.CreateResponse(HttpStatusCode.NoContent);
    }, ct);

    [Function("GetJourneyDashboard")]
    [OpenApiOperation("GetJourneyDashboard", new[] { "CustomerJourney" }, Summary = "Read consented outlet traffic and conversions")]
    public Task<HttpResponseData> Dashboard(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "analytics/journey/dashboard")] HttpRequestData req,
        CancellationToken ct) => Read(req, ct, async outlet =>
        await Json(req, HttpStatusCode.OK, await journeys.GetDashboardAsync(outlet, Range(req), ct), ct));

    [Function("GetJourneySessions")]
    [OpenApiOperation("GetJourneySessions", new[] { "CustomerJourney" }, Summary = "Search retained visitor sessions and permitted account journeys")]
    public Task<HttpResponseData> Sessions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "analytics/journey/sessions")] HttpRequestData req,
        CancellationToken ct) => Read(req, ct, async outlet =>
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        return await Json(req, HttpStatusCode.OK, await journeys.GetSessionsAsync(
            outlet, Range(req), query["segment"] ?? "customers", query["q"], Page(query["page"]), ct), ct);
    });

    [Function("GetJourneyTimeline")]
    [OpenApiOperation("GetJourneyTimeline", new[] { "CustomerJourney" }, Summary = "Read a chronological session timeline")]
    public Task<HttpResponseData> Timeline(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "analytics/journey/sessions/{sessionId}")] HttpRequestData req,
        string sessionId, CancellationToken ct) => Read(req, ct, async outlet =>
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var timeline = await journeys.GetTimelineAsync(outlet, Range(req), sessionId, Page(query["page"]), ct);
        return timeline == null
            ? await Json(req, HttpStatusCode.NotFound, new { error = "This journey is unavailable, expired, or outside this outlet/date range." }, ct)
            : await Json(req, HttpStatusCode.OK, timeline, ct);
    });

    [Function("GetLiveJourneys")]
    [OpenApiOperation("GetLiveJourneys", new[] { "CustomerJourney" }, Summary = "Read active customer and separate staff sessions")]
    public Task<HttpResponseData> Live(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "analytics/journey/live")] HttpRequestData req,
        CancellationToken ct) => Read(req, ct, async outlet =>
        await Json(req, HttpStatusCode.OK, await journeys.GetLiveAsync(outlet, ct), ct));

    [Function("ExportJourneyReport")]
    [OpenApiOperation("ExportJourneyReport", new[] { "CustomerJourney" }, Summary = "Export complete filtered retained aggregates or sessions as CSV")]
    public Task<HttpResponseData> Export(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "analytics/journey/export")] HttpRequestData req,
        CancellationToken ct) => Read(req, ct, async outlet =>
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var report = query["report"] ?? "sessions";
        var csv = await journeys.ExportAsync(outlet, Range(req), report, query["segment"] ?? "customers", query["q"], ct);
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/csv; charset=utf-8");
        response.Headers.Add("Content-Disposition", $"attachment; filename=\"journey-{report}.csv\"");
        response.Headers.Add("Cache-Control", "no-store");
        await response.Body.WriteAsync(Encoding.UTF8.GetPreamble(), ct);
        await response.WriteStringAsync(csv, ct);
        return response;
    });

    private Task<HttpResponseData> Read(HttpRequestData req, CancellationToken ct, Func<string, Task<HttpResponseData>> read) =>
        Execute(req, async () =>
        {
            var (authorized, _, _, error) = await AuthorizationHelper.ValidateAdminRole(req, auth);
            if (!authorized) return error!;
            var outletId = OutletHelper.GetOutletIdFromRequest(req, auth);
            if (!MongoDB.Bson.ObjectId.TryParse(outletId, out _))
                throw new ArgumentException("Select one outlet to view customer journeys.");
            var outlet = await outlets.GetOutletByIdAsync(outletId!);
            if (outlet == null) return await Json(req, HttpStatusCode.NotFound, new { error = "Outlet not found." }, ct);
            return await read(outletId!);
        }, ct);

    public static JourneyReportRange ParseRange(string? from, string? to, DateTime nowUtc)
    {
        var today = nowUtc.AddHours(5.5).Date;
        var start = today.AddDays(-6);
        var end = today;
        if (from != null && !IstDateHelper.TryParseIsoDate(from, out start) ||
            to != null && !IstDateHelper.TryParseIsoDate(to, out end))
            throw new ArgumentException("Dates must use yyyy-MM-dd.");
        if (start > end || start < today.AddDays(-29) || end > today)
            throw new ArgumentException("Choose an inclusive date range within the last 30 calendar days.");
        return new(IstDateHelper.IstWallTimeToUtc(start), IstDateHelper.IstWallTimeToUtc(end.AddDays(1)));
    }

    private static JourneyReportRange Range(HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        return ParseRange(query["from"], query["to"], DateTime.UtcNow);
    }

    private static int Page(string? value) => value == null ? 1 :
        int.TryParse(value, out var page) && page is >= 1 and <= 20000 ? page :
        throw new ArgumentException("Invalid page.");

    private async Task<HttpResponseData> Execute(HttpRequestData req, Func<Task<HttpResponseData>> action, CancellationToken ct)
    {
        try
        {
            var response = await action();
            if (!response.Headers.Contains("Cache-Control")) response.Headers.Add("Cache-Control", "no-store");
            return response;
        }
        catch (ArgumentException ex)
        {
            log.LogWarning("Customer journey request rejected: {Reason}", ex.Message);
            return await Json(req, HttpStatusCode.BadRequest, new { error = ex.Message }, ct);
        }
        catch (UnauthorizedAccessException ex)
        {
            log.LogWarning("Customer journey capability rejected: {Reason}", ex.Message);
            return await Json(req, HttpStatusCode.Forbidden, new { error = ex.Message }, ct);
        }
        catch (JsonException)
        {
            log.LogWarning("Customer journey request contained malformed JSON");
            return await Json(req, HttpStatusCode.BadRequest, new { error = "Invalid JSON request." }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Customer journey request failed");
            return await Json(req, HttpStatusCode.InternalServerError, new { error = "Journey analytics are unavailable. Please retry." }, ct);
        }
    }

    private static async Task<HttpResponseData> Json(HttpRequestData req, HttpStatusCode status, object body, CancellationToken ct)
    {
        var response = req.CreateResponse(status);
        response.Headers.Add("Cache-Control", "no-store");
        await response.WriteAsJsonAsync(body, ct);
        return response;
    }
}
