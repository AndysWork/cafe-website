using System.Net;
using Cafe.Api.Helpers;
using Cafe.Api.Models;
using Cafe.Api.Repositories;
using Cafe.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace Cafe.Api.Functions;

public sealed class ManagerDashboardFunction(
    IManagerDashboardService dashboard, IUserRepository users, IStaffRepository staff,
    IOutletRepository outlets, AuthService auth, ILogger<ManagerDashboardFunction> logger)
{
    [Function("GetManagerDashboard")]
    [OpenApiOperation(operationId: "GetManagerDashboard", tags: new[] { "Manager" }, Summary = "Read an assigned outlet dashboard")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "X-Outlet-Id", In = ParameterLocation.Header, Required = true, Type = typeof(string), Description = "An active outlet assigned to the caller.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ManagerDashboardResponse), Description = "Read-only dashboard data")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Authentication required")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Manager role or outlet assignment missing")]
    public async Task<HttpResponseData> GetDashboard(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/dashboard")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var (authorized, _, _, authError) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, auth);
        if (!authorized) return authError!;
        try
        {
            var (allowed, outletId, accessError) = await OutletHelper.ValidateOutletAccess(req, auth, users, staff, outlets);
            if (!allowed || string.IsNullOrWhiteSpace(outletId))
                return await Error(req, HttpStatusCode.Forbidden, accessError ?? "An assigned outlet is required");

            var data = await dashboard.GetAsync(outletId, cancellationToken);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(data, cancellationToken);
            return response;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Unable to read Manager dashboard");
            return await Error(req, HttpStatusCode.InternalServerError, "Unable to load dashboard data");
        }
    }

    [Function("GetManagerDashboardOutlets")]
    [OpenApiOperation(operationId: "GetManagerDashboardOutlets", tags: new[] { "Manager" }, Summary = "List assigned active dashboard outlets")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(List<DashboardOutletResponse>), Description = "Authorized active outlets")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Authentication required")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Manager role required")]
    public async Task<HttpResponseData> GetOutlets(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/dashboard/outlets")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var (authorized, _, _, authError) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, auth);
        if (!authorized) return authError!;
        try
        {
            var result = new List<DashboardOutletResponse>();
            foreach (var outlet in await outlets.GetActiveOutletsAsync().WaitAsync(cancellationToken))
            {
                if (!outlet.IsActive || outlet.IsDeleted) continue;
                if (string.IsNullOrWhiteSpace(outlet.Id))
                {
                    logger.LogWarning("Skipping an active outlet without an ID in Manager dashboard choices");
                    continue;
                }
                var (allowed, _, _) = await OutletHelper.ValidateOutletAccess(req, auth, users, staff, outlets, outlet.Id);
                if (allowed)
                    result.Add(new(outlet.Id, outlet.OutletCode, outlet.OutletName,
                        outlet.Address, outlet.City, outlet.State, outlet.IsActive, outlet.Settings));
            }
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(result, cancellationToken);
            return response;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Unable to load assigned dashboard outlets");
            return await Error(req, HttpStatusCode.InternalServerError, "Unable to load assigned outlets");
        }
    }

    private static async Task<HttpResponseData> Error(HttpRequestData req, HttpStatusCode status, string message)
    {
        var response = req.CreateResponse(status);
        await response.WriteAsJsonAsync(new { error = message });
        return response;
    }
}

/// <summary>Outlet identity available to a Manager's dashboard branch selector.</summary>
public sealed record DashboardOutletResponse(
    string Id, string OutletCode, string OutletName, string Address, string City, string State,
    bool IsActive, OutletSettings Settings);
