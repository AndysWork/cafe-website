using System.Net;
using System.Security.Cryptography;
using System.Text;
using Cafe.Api.Helpers;
using Cafe.Api.Models;
using Cafe.Api.Repositories;
using Cafe.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Functions;

/// <summary>Table master (setup) and live floor view for dine-in.</summary>
public class CafeTableFunction
{
    private const string AuditEntity = "CafeTable";

    private readonly CafeTableRepository _tables;
    private readonly IOrderRepository _orderRepo;
    private readonly IOutletRepository _outletRepo;
    private readonly AuthService _auth;
    private readonly EventLogService _events;
    private readonly ILogger _log;

    public CafeTableFunction(
        CafeTableRepository tables,
        IOrderRepository orderRepo,
        IOutletRepository outletRepo,
        AuthService auth,
        EventLogService events,
        ILoggerFactory loggerFactory)
    {
        _tables = tables;
        _orderRepo = orderRepo;
        _outletRepo = outletRepo;
        _auth = auth;
        _events = events;
        _log = loggerFactory.CreateLogger<CafeTableFunction>();
    }

    [Function("GetCafeTables")]
    public async Task<HttpResponseData> GetTables(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "tables")] HttpRequestData req)
    {
        var (ok, _, _, error) = await AuthorizationHelper.ValidateAnyRole(req, _auth, RoleHelper.StaffRoles, "Staff access required");
        if (!ok) return error!;

        try
        {
            var outletId = await ResolveOutletIdAsync(req, req.Query["outletId"]);
            if (outletId == null) return await Error(req, HttpStatusCode.BadRequest, "Outlet ID required");

            var includeInactive = string.Equals(req.Query["includeInactive"], "true", StringComparison.OrdinalIgnoreCase);
            var tables = await _tables.GetByOutletAsync(outletId, includeInactive);
            return await Ok(req, tables);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error listing tables");
            return await Error(req, HttpStatusCode.InternalServerError, "Failed to load tables");
        }
    }

    /// <summary>
    /// Live floor: every active table with its derived status. Sessions on table numbers that are
    /// not in the master (e.g. legacy free-text QR tables) are appended so nothing is hidden.
    /// </summary>
    [Function("GetCafeTableFloor")]
    public async Task<HttpResponseData> GetFloor(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "tables/floor")] HttpRequestData req)
    {
        var (ok, _, _, error) = await AuthorizationHelper.ValidateAnyRole(req, _auth, RoleHelper.StaffRoles, "Staff access required");
        if (!ok) return error!;

        try
        {
            var outletId = await ResolveOutletIdAsync(req, req.Query["outletId"]);
            if (outletId == null) return await Error(req, HttpStatusCode.BadRequest, "Outlet ID required");

            var tables = await _tables.GetByOutletAsync(outletId, includeInactive: false);
            var sessions = await _orderRepo.GetActiveDineInSessionsAsync(outletId);
            var floor = BuildFloor(tables, sessions, MongoService.GetIstNow());

            var summary = new
            {
                total = floor.Count,
                free = floor.Count(f => f.Status == CafeTableStatus.Free),
                occupied = floor.Count(f => f.Status == CafeTableStatus.Occupied),
                billRequested = floor.Count(f => f.Status == CafeTableStatus.BillRequested),
                cleaning = floor.Count(f => f.Status == CafeTableStatus.Cleaning),
                outOfService = floor.Count(f => f.Status == CafeTableStatus.OutOfService)
            };

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { success = true, data = floor, summary });
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error loading table floor");
            return await Error(req, HttpStatusCode.InternalServerError, "Failed to load floor");
        }
    }

    [Function("CleanupCancelledDineInSessions")]
    public async Task<HttpResponseData> CleanupCancelledSessions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "tables/cleanup-cancelled-sessions")] HttpRequestData req)
    {
        var (authorized, userId, _, error) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!authorized) return error!;
        if (string.IsNullOrWhiteSpace(userId))
            return await Error(req, HttpStatusCode.Unauthorized, "Authenticated admin identity is required");

        var dryRun = !string.Equals(req.Query["dryRun"], "false", StringComparison.OrdinalIgnoreCase);
        string? outletId = null;
        try
        {
            outletId = await ResolveOutletIdAsync(req, req.Query["outletId"]);
            if (string.IsNullOrWhiteSpace(outletId))
                return await Error(req, HttpStatusCode.BadRequest, "Outlet ID required");

            var candidates = new List<(DineInSession Session, int OrderCount)>();
            var sessions = await _orderRepo.GetActiveDineInSessionsAsync(outletId);
            foreach (var session in sessions)
            {
                if (!CanCleanupCancelledSession(session)) continue;

                var allOrdersCancelled = true;
                foreach (var orderId in session.OrderIds)
                {
                    var order = await _orderRepo.GetOrderByIdAsync(orderId);
                    if (order == null
                        || order.OutletId != outletId
                        || order.OrderType != "dine-in"
                        || order.DineInSessionId != session.Id
                        || order.Status != "cancelled")
                    {
                        allOrdersCancelled = false;
                        break;
                    }
                }

                if (allOrdersCancelled) candidates.Add((session, session.OrderIds.Count));
            }

            var closed = new List<object>();
            if (!dryRun)
            {
                foreach (var candidate in candidates)
                {
                    if (await _orderRepo.TryCloseCancelledDineInSessionAsync(
                            candidate.Session.Id!, candidate.Session.UpdatedAt, userId))
                    {
                        closed.Add(new
                        {
                            sessionId = candidate.Session.Id,
                            tableNumber = candidate.Session.TableNumber,
                            orderCount = candidate.OrderCount
                        });
                    }
                }
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                dryRun,
                candidateCount = candidates.Count,
                closedCount = closed.Count,
                skippedCount = dryRun ? 0 : candidates.Count - closed.Count,
                candidates = candidates.Select(candidate => new
                {
                    sessionId = candidate.Session.Id,
                    tableNumber = candidate.Session.TableNumber,
                    orderCount = candidate.OrderCount
                }),
                closed
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to clean up cancelled dine-in sessions for outlet {OutletId}", outletId);
            return await Error(req, HttpStatusCode.InternalServerError, "Failed to clean up cancelled table sessions");
        }
    }

    [Function("CreateCafeTable")]
    public async Task<HttpResponseData> CreateTable(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "tables")] HttpRequestData req)
    {
        var (ok, userId, role, error) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
        if (!ok) return error!;

        try
        {
            var (request, validationError) = await ValidationHelper.ValidateBody<CreateCafeTableRequest>(req);
            if (validationError != null) return validationError;

            var outletId = await ResolveOutletIdAsync(req, request.OutletId);
            if (outletId == null) return await Error(req, HttpStatusCode.BadRequest, "Outlet ID required");

            var table = new CafeTable
            {
                OutletId = outletId,
                TableNumber = request.TableNumber.Trim(),
                Area = string.IsNullOrWhiteSpace(request.Area) ? null : request.Area.Trim(),
                Capacity = request.Capacity,
                SortOrder = request.SortOrder ?? await _tables.GetMaxSortOrderAsync(outletId) + 1,
                UpdatedBy = userId
            };

            if (!await _tables.TryCreateAsync(table))
                return await Error(req, HttpStatusCode.Conflict, $"Table '{table.TableNumber}' already exists");

            await _events.LogEventAsync(AuditEntity, table.Id!, "Created", userId, role, newState: table, outletId: outletId);
            return await Ok(req, table, HttpStatusCode.Created);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating table");
            return await Error(req, HttpStatusCode.InternalServerError, "Failed to create table");
        }
    }

    /// <summary>Creates a numbered range of tables, e.g. T1..T10. Existing numbers are skipped.</summary>
    [Function("BulkCreateCafeTables")]
    public async Task<HttpResponseData> BulkCreate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "tables/bulk")] HttpRequestData req)
    {
        var (ok, userId, role, error) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
        if (!ok) return error!;

        try
        {
            var (request, validationError) = await ValidationHelper.ValidateBody<BulkCreateCafeTablesRequest>(req);
            if (validationError != null) return validationError;

            if (request.To < request.From || request.To - request.From >= 200)
                return await Error(req, HttpStatusCode.BadRequest, "Invalid range (max 200 tables at once)");

            var outletId = await ResolveOutletIdAsync(req, request.OutletId);
            if (outletId == null) return await Error(req, HttpStatusCode.BadRequest, "Outlet ID required");

            var prefix = (request.Prefix ?? string.Empty).Trim();
            var sortOrder = await _tables.GetMaxSortOrderAsync(outletId);
            var created = new List<string>();
            var skipped = new List<string>();

            for (var n = request.From; n <= request.To; n++)
            {
                var table = new CafeTable
                {
                    OutletId = outletId,
                    TableNumber = $"{prefix}{n}",
                    Area = string.IsNullOrWhiteSpace(request.Area) ? null : request.Area.Trim(),
                    Capacity = request.Capacity,
                    SortOrder = ++sortOrder,
                    UpdatedBy = userId
                };

                if (await _tables.TryCreateAsync(table)) created.Add(table.TableNumber);
                else { skipped.Add(table.TableNumber); sortOrder--; }
            }

            if (created.Count > 0)
            {
                await _events.LogEventAsync(AuditEntity, outletId, "BulkCreated", userId, role,
                    newState: new { created }, outletId: outletId);
            }

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { success = true, data = new { created, skipped } });
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error bulk creating tables");
            return await Error(req, HttpStatusCode.InternalServerError, "Failed to create tables");
        }
    }

    [Function("GenerateCafeTableQrToken")]
    public async Task<HttpResponseData> GenerateQrToken(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "tables/{id}/qr-token")] HttpRequestData req,
        string id)
    {
        var (ok, userId, role, authError) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
        if (!ok) return authError!;

        try
        {
            var table = await _tables.GetByIdAsync(id);
            if (table == null) return await Error(req, HttpStatusCode.NotFound, "Table not found");

            var outletId = await ResolveOutletIdAsync(req, req.Query["outletId"]);
            if (string.IsNullOrWhiteSpace(outletId) || table.OutletId != outletId)
                return await Error(req, HttpStatusCode.NotFound, "Table not found for this outlet");

            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

            if (!await _tables.SetQrTokenHashAsync(id, tokenHash, userId))
                return await Error(req, HttpStatusCode.Conflict, "Table QR credential could not be saved");

            var activeSessions = await _orderRepo.GetActiveDineInSessionsAsync(outletId);
            foreach (var session in activeSessions.Where(s =>
                         CafeTable.ToKey(s.TableNumber) == CafeTable.ToKey(table.TableNumber)))
            {
                session.QrAccessTokenHash = tokenHash;
                session.UpdatedAt = MongoService.GetIstNow();
                if (!await _orderRepo.UpdateDineInSessionAsync(session))
                    throw new InvalidOperationException($"Could not update QR access for active session {session.Id}");
            }

            await _events.LogEventAsync(
                AuditEntity, id, "QrTokenRotated", userId, role,
                outletId: outletId);

            return await Ok(req, new
            {
                tableNumber = table.TableNumber,
                outletId,
                qrToken = token
            }, HttpStatusCode.Created);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error generating QR token for table {TableId}", id);
            return await Error(req, HttpStatusCode.InternalServerError, "Failed to generate table QR");
        }
    }

    [Function("UpdateCafeTable")]
    public async Task<HttpResponseData> UpdateTable(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "tables/{id}")] HttpRequestData req,
        string id)
    {
        var (ok, userId, role, error) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
        if (!ok) return error!;

        try
        {
            var (request, validationError) = await ValidationHelper.ValidateBody<UpdateCafeTableRequest>(req);
            if (validationError != null) return validationError;

            var table = await _tables.GetByIdAsync(id);
            if (table == null) return await Error(req, HttpStatusCode.NotFound, "Table not found");

            var before = Snapshot(table);
            var renaming = !string.IsNullOrWhiteSpace(request.TableNumber)
                && CafeTable.ToKey(request.TableNumber) != table.TableKey;
            var deactivating = request.IsActive == false && table.IsActive;

            if ((renaming || deactivating) && await HasActiveSessionAsync(table))
                return await Error(req, HttpStatusCode.Conflict, "Table has an active session; settle it before renaming or deactivating");

            if (!string.IsNullOrWhiteSpace(request.TableNumber)) table.TableNumber = request.TableNumber.Trim();
            if (request.Area != null) table.Area = string.IsNullOrWhiteSpace(request.Area) ? null : request.Area.Trim();
            if (request.Capacity.HasValue) table.Capacity = request.Capacity.Value;
            if (request.SortOrder.HasValue) table.SortOrder = request.SortOrder.Value;
            if (request.IsActive.HasValue) table.IsActive = request.IsActive.Value;
            table.UpdatedBy = userId;

            if (!await _tables.TryReplaceAsync(table))
                return await Error(req, HttpStatusCode.Conflict, $"Table '{table.TableNumber}' already exists");

            await _events.LogEventAsync(AuditEntity, table.Id!, "Updated", userId, role,
                oldState: before, newState: Snapshot(table), outletId: table.OutletId);
            return await Ok(req, table);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating table {TableId}", id);
            return await Error(req, HttpStatusCode.InternalServerError, "Failed to update table");
        }
    }

    /// <summary>Sets housekeeping status (available / cleaning / out_of_service).</summary>
    [Function("UpdateCafeTableStatus")]
    public async Task<HttpResponseData> UpdateStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "tables/{id}/status")] HttpRequestData req,
        string id)
    {
        var allowed = RoleHelper.WaiterRoles.Concat(RoleHelper.CashierRoles);
        var (ok, userId, role, error) = await AuthorizationHelper.ValidateAnyRole(req, _auth, allowed, "Waiter, cashier or manager access required");
        if (!ok) return error!;

        try
        {
            var (request, validationError) = await ValidationHelper.ValidateBody<UpdateCafeTableStatusRequest>(req);
            if (validationError != null) return validationError;

            var status = request.Status.Trim().ToLowerInvariant();
            if (!CafeTableStatus.Housekeeping.Contains(status))
                return await Error(req, HttpStatusCode.BadRequest, "Status must be available, cleaning or out_of_service");

            var table = await _tables.GetByIdAsync(id);
            if (table == null) return await Error(req, HttpStatusCode.NotFound, "Table not found");

            if (status == CafeTableStatus.OutOfService && await HasActiveSessionAsync(table))
                return await Error(req, HttpStatusCode.Conflict, "Table has an active session");

            var previous = table.HousekeepingStatus;
            await _tables.SetHousekeepingStatusAsync(table.OutletId, table.TableNumber, status, userId);
            table.HousekeepingStatus = status;

            await _events.LogEventAsync(AuditEntity, table.Id!, "StatusChanged", userId, role,
                oldState: new { status = previous }, newState: new { status }, outletId: table.OutletId);
            return await Ok(req, table);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating table status {TableId}", id);
            return await Error(req, HttpStatusCode.InternalServerError, "Failed to update table status");
        }
    }

    [Function("DeleteCafeTable")]
    public async Task<HttpResponseData> DeleteTable(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "tables/{id}")] HttpRequestData req,
        string id)
    {
        var (ok, userId, role, error) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
        if (!ok) return error!;

        try
        {
            var table = await _tables.GetByIdAsync(id);
            if (table == null) return await Error(req, HttpStatusCode.NotFound, "Table not found");

            if (await HasActiveSessionAsync(table))
                return await Error(req, HttpStatusCode.Conflict, "Table has an active session; settle it before deleting");

            await _tables.DeleteAsync(id);
            await _events.LogEventAsync(AuditEntity, id, "Deleted", userId, role, oldState: Snapshot(table), outletId: table.OutletId);

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { success = true, message = $"Table '{table.TableNumber}' deleted" });
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error deleting table {TableId}", id);
            return await Error(req, HttpStatusCode.InternalServerError, "Failed to delete table");
        }
    }

    internal static List<CafeTableFloorView> BuildFloor(List<CafeTable> tables, List<DineInSession> sessions, DateTime now)
    {
        var sessionByKey = sessions
            .Where(s => !string.IsNullOrWhiteSpace(s.TableNumber))
            .GroupBy(s => CafeTable.ToKey(s.TableNumber))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.CreatedAt).First());

        var floor = new List<CafeTableFloorView>();

        foreach (var t in tables)
        {
            var view = new CafeTableFloorView
            {
                Id = t.Id,
                TableNumber = t.TableNumber,
                Area = t.Area,
                Capacity = t.Capacity,
                SortOrder = t.SortOrder,
                IsActive = t.IsActive,
                HousekeepingStatus = t.HousekeepingStatus
            };

            if (sessionByKey.Remove(t.TableKey, out var session))
                ApplySession(view, session, now);
            else
                view.Status = t.HousekeepingStatus switch
                {
                    CafeTableStatus.Cleaning => CafeTableStatus.Cleaning,
                    CafeTableStatus.OutOfService => CafeTableStatus.OutOfService,
                    _ => CafeTableStatus.Free
                };

            floor.Add(view);
        }

        // Sessions whose table number isn't configured in the master.
        foreach (var session in sessionByKey.Values.OrderBy(s => s.TableNumber))
        {
            var view = new CafeTableFloorView
            {
                TableNumber = session.TableNumber,
                Area = "Unmapped",
                SortOrder = int.MaxValue,
                IsActive = true
            };
            ApplySession(view, session, now);
            floor.Add(view);
        }

        return floor;
    }

    private static void ApplySession(CafeTableFloorView view, DineInSession session, DateTime now)
    {
        view.Status = session.Status == "bill_requested" ? CafeTableStatus.BillRequested : CafeTableStatus.Occupied;
        view.SessionId = session.Id;
        view.CustomerName = session.CustomerName;
        view.RoundsCount = session.OrderIds.Count;
        view.GrandTotal = session.GrandTotal;
        view.AmountDue = Math.Max(0, Math.Round(session.GrandTotal - session.AmountPaid, 2));
        view.OccupiedSince = session.CreatedAt;
        view.MinutesActive = Math.Max(0, (int)(now - session.CreatedAt).TotalMinutes);
    }

    private static bool CanCleanupCancelledSession(DineInSession session) =>
        !string.IsNullOrWhiteSpace(session.Id)
        && (session.Status is "active" or "bill_requested")
        && session.OrderIds is { Count: > 0 }
        && session.PaymentStatus == "unpaid"
        && session.AmountPaid == 0
        && string.IsNullOrWhiteSpace(session.RazorpayOrderId)
        && !(session.PaymentEntries ?? new()).Any(entry => entry.Status != "rejected");

    private async Task<bool> HasActiveSessionAsync(CafeTable table)
    {
        var sessions = await _orderRepo.GetActiveDineInSessionsAsync(table.OutletId);
        return sessions.Any(s => CafeTable.ToKey(s.TableNumber) == table.TableKey);
    }

    private static object Snapshot(CafeTable t) => new
    {
        t.TableNumber,
        t.Area,
        t.Capacity,
        t.SortOrder,
        t.IsActive,
        t.HousekeepingStatus
    };

    private async Task<string?> ResolveOutletIdAsync(HttpRequestData req, string? requestedId)
    {
        if (!string.IsNullOrWhiteSpace(requestedId)) return requestedId.Trim();
        var headerId = req.Headers.TryGetValues("X-Outlet-Id", out var vals) ? vals.FirstOrDefault() : null;
        if (!string.IsNullOrWhiteSpace(headerId)) return headerId.Trim();

        var outlets = await _outletRepo.GetAllOutletsAsync();
        return outlets.FirstOrDefault()?.Id;
    }

    private static async Task<HttpResponseData> Ok(HttpRequestData req, object data, HttpStatusCode status = HttpStatusCode.OK)
    {
        var res = req.CreateResponse(status);
        await res.WriteAsJsonAsync(new { success = true, data });
        return res;
    }

    private static async Task<HttpResponseData> Error(HttpRequestData req, HttpStatusCode status, string message)
    {
        var res = req.CreateResponse(status);
        await res.WriteAsJsonAsync(new { error = message });
        return res;
    }
}
