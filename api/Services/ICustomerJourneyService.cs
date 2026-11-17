using System.Security.Claims;
using Cafe.Api.Models;

namespace Cafe.Api.Services;

public interface ICustomerJourneyService
{
    Task<JourneySessionResponse> CreateSessionAsync(CreateJourneySessionRequest request, ClaimsPrincipal? principal, CancellationToken ct);
    Task<JourneySession> ValidateAsync(JourneyContext? context, CancellationToken ct, bool allowInactive = false);
    Task TrackAsync(TrackJourneyBatchRequest request, CancellationToken ct);
    Task RevokeAsync(JourneyContext context, CancellationToken ct);
    Task EraseIfRevokedAsync(string sessionId, CancellationToken ct);
    Task ConfirmOrderAsync(JourneyContext context, Order order, CancellationToken ct);
    Task<JourneyDashboardResponse> GetDashboardAsync(string outletId, JourneyReportRange range, CancellationToken ct);
    Task<JourneySessionListResponse> GetSessionsAsync(string outletId, JourneyReportRange range, string segment, string? search, int page, CancellationToken ct);
    Task<JourneyTimelineResponse?> GetTimelineAsync(string outletId, JourneyReportRange range, string sessionId, int page, CancellationToken ct);
    Task<JourneyLiveResponse> GetLiveAsync(string outletId, CancellationToken ct);
    Task<string> ExportAsync(string outletId, JourneyReportRange range, string report, string segment, string? search, CancellationToken ct);
}
