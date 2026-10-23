using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Cafe.Api.Models;
using Cafe.Api.Repositories;
using Cafe.Api.Services;
using Cafe.Api.Helpers;
using System.Net;
using System.Security.Claims;
using System.Globalization;
using MongoDB.Bson;
using System.Security.Cryptography;
using System.Text;

namespace Cafe.Api.Functions;

public class DineInFunction
{
    private readonly IOrderRepository _orderRepo;
    private readonly IMenuRepository _menuRepo;
    private readonly IOutletRepository _outletRepo;
    private readonly CafeTableRepository _tableRepo;
    private readonly IUserRepository _userRepo;
    private readonly IOfferRepository _offerRepo;
    private readonly ILoyaltyRepository _loyaltyRepo;
    private readonly IOperationsRepository _operationsRepo;
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly OutboxService _outbox;
    private readonly EventLogService _eventLog;
    private readonly IConfiguration _config;
    private readonly ILogger _log;

    public DineInFunction(
        IOrderRepository orderRepo,
        IMenuRepository menuRepo,
        IOutletRepository outletRepo,
        CafeTableRepository tableRepo,
        IUserRepository userRepo,
        IOfferRepository offerRepo,
        ILoyaltyRepository loyaltyRepo,
        IOperationsRepository operationsRepo,
        MongoService mongo,
        AuthService auth,
        OutboxService outbox,
        EventLogService eventLog,
        IConfiguration config,
        ILoggerFactory loggerFactory)
    {
        _orderRepo = orderRepo;
        _menuRepo = menuRepo;
        _outletRepo = outletRepo;
        _tableRepo = tableRepo;
        _userRepo = userRepo;
        _offerRepo = offerRepo;
        _loyaltyRepo = loyaltyRepo;
        _operationsRepo = operationsRepo;
        _mongo = mongo;
        _auth = auth;
        _outbox = outbox;
        _eventLog = eventLog;
        _config = config;
        _log = loggerFactory.CreateLogger<DineInFunction>();
    }

    /// <summary>
    /// Gets the active running dine-in session and bill for a table.
    /// </summary>
    [Function("GetActiveDineInSession")]
    public async Task<HttpResponseData> GetActiveSession(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dine-in/session/active")] HttpRequestData req)
    {
        try
        {
            var tableNumber = req.Query["tableNumber"]?.Trim();
            var outletId = req.Query["outletId"]?.Trim();

            if (string.IsNullOrWhiteSpace(tableNumber))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "tableNumber query parameter is required" });
                return badReq;
            }

            outletId = await ResolveOutletIdAsync(req, outletId);
            if (string.IsNullOrWhiteSpace(outletId))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Unable to determine outlet" });
                return badReq;
            }

            var session = await _orderRepo.GetActiveDineInSessionByTableAsync(outletId, tableNumber);
            if (session == null)
            {
                var latest = await _orderRepo.GetLatestDineInSessionByTableAsync(outletId, tableNumber);
                var isSettled = latest != null && latest.Status == "paid";
                var latestBill = isSettled && HasSessionAccess(req, latest!)
                    ? await BuildBillResponseAsync(latest!)
                    : null;

                var okEmpty = req.CreateResponse(HttpStatusCode.OK);
                await okEmpty.WriteAsJsonAsync(new
                {
                    session = (object?)null,
                    isTableSettled = isSettled,
                    latestPaidSession = latestBill
                });
                return okEmpty;
            }

            if (!HasSessionAccess(req, session))
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "A valid dine-in session token is required" });
                return forbidden;
            }

            var bill = await BuildBillResponseAsync(session);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { session = bill });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting active dine-in session");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "Failed to load dine-in session" });
            return res;
        }
    }

    /// <summary>
    /// Gets the bill for a specific table reservation (whether active, seated, or completed).
    /// </summary>
    [Function("GetReservationBill")]
    public async Task<HttpResponseData> GetReservationBill(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dine-in/reservations/{reservationId}/bill")] HttpRequestData req,
        string reservationId)
    {
        try
        {
            var reservation = await _operationsRepo.GetReservationByIdAsync(reservationId);
            if (reservation == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Reservation not found" });
                return notFound;
            }

            DineInSession? session = null;
            if (!string.IsNullOrWhiteSpace(reservation.DineInSessionId))
            {
                session = await _orderRepo.GetDineInSessionByIdAsync(reservation.DineInSessionId);
            }

            // If reservation is completed, ensure we don't return an empty/unpaid session
            if (reservation.Status == "completed" && (session == null || session.Status != "paid" || session.OrderIds.Count == 0))
            {
                var userSessions = await _orderRepo.GetUserDineInSessionsAsync(reservation.UserId ?? string.Empty);
                var cleanResTable = System.Text.RegularExpressions.Regex.Replace(reservation.TableNumber ?? string.Empty, @"(?i)^table\s*", string.Empty).Trim();

                var paidSession = userSessions.FirstOrDefault(s =>
                    s.Status == "paid" &&
                    s.OrderIds.Count > 0 &&
                    (!string.IsNullOrWhiteSpace(cleanResTable) &&
                     System.Text.RegularExpressions.Regex.Replace(s.TableNumber ?? string.Empty, @"(?i)^table\s*", string.Empty).Trim().Equals(cleanResTable, StringComparison.OrdinalIgnoreCase)));

                if (paidSession != null)
                {
                    session = paidSession;
                }
            }

            // Fallback 1: Search by customer userId if reservation was created by user
            if (session == null && !string.IsNullOrWhiteSpace(reservation.UserId) && reservation.UserId != "guest_dinein")
            {
                var userSessions = await _orderRepo.GetUserDineInSessionsAsync(reservation.UserId);
                var cleanResTable = System.Text.RegularExpressions.Regex.Replace(reservation.TableNumber ?? string.Empty, @"(?i)^table\s*", string.Empty).Trim();

                session = userSessions.FirstOrDefault(s =>
                {
                    if (reservation.Status == "completed" && s.Status != "paid") return false;
                    if (!string.IsNullOrWhiteSpace(cleanResTable))
                    {
                        var cleanSessionTable = System.Text.RegularExpressions.Regex.Replace(s.TableNumber ?? string.Empty, @"(?i)^table\s*", string.Empty).Trim();
                        return cleanSessionTable.Equals(cleanResTable, StringComparison.OrdinalIgnoreCase);
                    }
                    return true;
                });

                if (session != null)
                {
                }
            }

            // Fallback 2: Search by table number
            if (session == null && !string.IsNullOrWhiteSpace(reservation.TableNumber))
            {
                session = await _orderRepo.GetLatestDineInSessionByTableAsync(reservation.OutletId, reservation.TableNumber);
                if (session == null)
                {
                    session = await _orderRepo.GetLatestDineInSessionByTableAsync(string.Empty, reservation.TableNumber);
                }

                if (session != null)
                {
                }
            }

            if (session == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "No dining bill recorded for this reservation yet." });
                return notFound;
            }

            if (!HasSessionAccess(req, session))
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Access denied for this reservation bill" });
                return forbidden;
            }

            var bill = await BuildBillResponseAsync(session);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(bill);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting bill for reservation {ReservationId}", reservationId);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "Failed to load reservation bill" });
            return res;
        }
    }

    /// <summary>
    /// Starts or joins an active dine-in session for a table.
    /// </summary>
    [Function("StartDineInSession")]
    public async Task<HttpResponseData> StartSession(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "dine-in/session/start")] HttpRequestData req)
    {
        try
        {
            var (request, valError) = await ValidationHelper.ValidateBody<StartDineInSessionRequest>(req);
            if (valError != null) return valError;

            var tableNumber = request.TableNumber.Trim();
            var outletId = await ResolveOutletIdAsync(req, request.OutletId);

            if (string.IsNullOrWhiteSpace(outletId))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Valid outlet ID is required" });
                return badReq;
            }

            var table = await _tableRepo.GetByNumberAsync(outletId, tableNumber);
            if (table?.QrTokenHash is not null && !HasTableQrAccess(req, table.QrTokenHash))
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Scan this table's QR code to start a dine-in session" });
                return forbidden;
            }
            if (table != null && (!table.IsActive || table.HousekeepingStatus != CafeTableStatus.Available))
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = "This table is not available for dine-in ordering" });
                return conflict;
            }

            // Check if active session already exists for this table
            var existingSession = await _orderRepo.GetActiveDineInSessionByTableAsync(outletId, tableNumber);
            if (existingSession != null)
            {
                if (!HasSessionAccess(req, existingSession))
                {
                    var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                    await conflict.WriteAsJsonAsync(new { error = "This table already has an active session. Ask staff to help you join it." });
                    return conflict;
                }
                var bill = await BuildBillResponseAsync(existingSession);
                var resExisting = req.CreateResponse(HttpStatusCode.OK);
                await resExisting.WriteAsJsonAsync(new { message = "Joined active table session", session = bill });
                return resExisting;
            }

            // Resolve customer info if authenticated
            var (userId, username, userPhone) = GetOptionalUserInfo(req);
            var customerName = !string.IsNullOrWhiteSpace(request.CustomerName) ? request.CustomerName.Trim() : username;
            var customerPhone = !string.IsNullOrWhiteSpace(request.CustomerPhone) ? request.CustomerPhone.Trim() : userPhone;

            var outlet = await _outletRepo.GetOutletByIdAsync(outletId);
            var (sessionAccessToken, sessionAccessTokenHash) = CreateSessionAccessToken();

            var session = new DineInSession
            {
                OutletId = outletId,
                OutletName = outlet?.OutletName ?? "Main Cafe",
                TableNumber = tableNumber,
                UserId = userId,
                CustomerName = customerName,
                CustomerPhone = customerPhone,
                AccessTokenHash = sessionAccessTokenHash,
                QrAccessTokenHash = table?.QrTokenHash,
                Status = "active",
                PaymentStatus = "unpaid",
                AmountPaid = 0,
                CreatedAt = MongoService.GetIstNow(),
                UpdatedAt = MongoService.GetIstNow()
            };

            AddSessionEvent(
                session,
                "session_started",
                $"Dine-in session started at Table {tableNumber}",
                userId ?? customerName ?? "guest");

            var created = await _orderRepo.CreateDineInSessionAsync(session);
            var billCreated = await BuildBillResponseAsync(created);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new
            {
                message = "Table session started",
                session = billCreated,
                sessionAccessToken
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error starting dine-in session");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "Failed to start table session" });
            return res;
        }
    }

    /// <summary>
    /// Places an incremental Dine-In order round directly to the running tab.
    /// Items are sent directly to the kitchen without requiring upfront payment.
    /// </summary>
    [Function("PlaceDineInOrder")]
    public async Task<HttpResponseData> PlaceDineInOrder(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "dine-in/order")] HttpRequestData req)
    {
        try
        {
            var (orderRequest, valError) = await ValidationHelper.ValidateBody<CreateOrderRequest>(req);
            if (valError != null) return valError;

            if (orderRequest.Items == null || orderRequest.Items.Count == 0)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Order must contain at least one item" });
                return badReq;
            }

            var tableNumber = (orderRequest.TableNumber ?? "").Trim();
            if (string.IsNullOrWhiteSpace(tableNumber))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Table number is required for dine-in ordering" });
                return badReq;
            }

            var outletId = await ResolveOutletIdAsync(req, orderRequest.OutletId);
            if (string.IsNullOrWhiteSpace(outletId))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Unable to resolve outlet" });
                return badReq;
            }

            var (userId, username, userPhone) = GetOptionalUserInfo(req);
            var isWaiterOrder = RoleHelper.HasAnyRole(
                AuthorizationHelper.GetRolesFromAuthorizationHeader(req, _auth),
                RoleHelper.WaiterRoles);
            var table = await _tableRepo.GetByNumberAsync(outletId, tableNumber);
            if (isWaiterOrder)
            {
                if (table == null)
                {
                    var activeLegacySession = await _orderRepo.GetActiveDineInSessionByTableAsync(outletId, tableNumber);
                    if (activeLegacySession == null)
                    {
                        var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                        await notFound.WriteAsJsonAsync(new { error = "Select a table configured for this outlet" });
                        return notFound;
                    }
                }
                else if (!table.IsActive || table.HousekeepingStatus != CafeTableStatus.Available)
                {
                    var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                    await conflict.WriteAsJsonAsync(new { error = "This table is not available for ordering" });
                    return conflict;
                }

                if (table != null) tableNumber = table.TableNumber;
            }
            else if (table?.QrTokenHash is not null && !HasTableQrAccess(req, table.QrTokenHash))
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Scan this table's QR code to place an order" });
                return forbidden;
            }
            else if (table != null && (!table.IsActive || table.HousekeepingStatus != CafeTableStatus.Available))
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = "This table is not available for ordering" });
                return conflict;
            }
            var customerPhone = string.IsNullOrWhiteSpace(orderRequest.PhoneNumber) ? userPhone : orderRequest.PhoneNumber.Trim();
            string? sessionAccessToken = null;

            // Find or automatically start active session for this table
            var session = await _orderRepo.GetActiveDineInSessionByTableAsync(outletId, tableNumber);
            if (session == null)
            {
                var outlet = await _outletRepo.GetOutletByIdAsync(outletId);
                var generatedAccess = CreateSessionAccessToken();
                sessionAccessToken = generatedAccess.token;
                session = new DineInSession
                {
                    OutletId = outletId,
                    OutletName = outlet?.OutletName ?? "Main Cafe",
                    TableNumber = tableNumber,
                    UserId = userId,
                    CustomerName = username,
                    CustomerPhone = customerPhone,
                    AccessTokenHash = generatedAccess.hash,
                    QrAccessTokenHash = table?.QrTokenHash,
                    Status = "active",
                    PaymentStatus = "unpaid",
                    AmountPaid = 0,
                    CreatedAt = MongoService.GetIstNow(),
                    UpdatedAt = MongoService.GetIstNow()
                };

                AddSessionEvent(
                    session,
                    "session_started",
                    $"Dine-in session auto-started at Table {tableNumber}",
                    userId ?? username ?? "guest");
                session = await _orderRepo.CreateDineInSessionAsync(session);
            }
            else
            {
                if (!isWaiterOrder && !HasSessionAccess(req, session))
                {
                    var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                    await forbidden.WriteAsJsonAsync(new { error = "A valid dine-in session token is required" });
                    return forbidden;
                }

                if (session.Status == "bill_requested")
                {
                    var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                    await conflict.WriteAsJsonAsync(new
                    {
                        error = "Final bill already requested. Ask manager to reopen ordering before adding new items."
                    });
                    return conflict;
                }
            }

            // Batch validate items from menu
            var menuItemIds = orderRequest.Items.Select(i => i.MenuItemId).Distinct().ToList();
            var allMenuItems = await _menuRepo.GetMenuItemsByIdsAsync(menuItemIds, outletId);
            var menuItemMap = allMenuItems.Where(m => m.Id != null).ToDictionary(m => m.Id!, m => m);

            var categoryIds = allMenuItems.Where(m => !string.IsNullOrEmpty(m.CategoryId)).Select(m => m.CategoryId!).Distinct().ToList();
            var categories = categoryIds.Count > 0 ? await _menuRepo.GetCategoriesByIdsAsync(categoryIds) : new Dictionary<string, string>();

            var orderItems = new List<OrderItem>();
            decimal roundSubtotal = 0;

            foreach (var item in orderRequest.Items)
            {
                if (!menuItemMap.TryGetValue(item.MenuItemId, out var menuItem))
                {
                    var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badReq.WriteAsJsonAsync(new { error = $"Menu item {item.MenuItemId} not found" });
                    return badReq;
                }

                if (!menuItem.IsAvailable)
                {
                    var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badReq.WriteAsJsonAsync(new { error = $"{menuItem.Name} is currently unavailable" });
                    return badReq;
                }

                categories.TryGetValue(menuItem.CategoryId ?? "", out var categoryName);

                // Use dine-in price if configured, otherwise fallback to web/shop price
                var baseUnitPrice = menuItem.DineInPrice > 0
                    ? menuItem.DineInPrice
                    : (menuItem.WebPrice > 0 ? menuItem.WebPrice : menuItem.ShopSellingPrice);

                string? variantName = null;
                decimal? variantPrice = null;
                if (!string.IsNullOrWhiteSpace(item.SelectedVariantName) && menuItem.Variants != null)
                {
                    var v = menuItem.Variants.FirstOrDefault(x => x.VariantName.Equals(item.SelectedVariantName, StringComparison.OrdinalIgnoreCase));
                    if (v != null)
                    {
                        variantName = v.VariantName;
                        variantPrice = v.Price;
                        baseUnitPrice = v.Price;
                    }
                }

                var addOns = new List<OrderItemAddOn>();
                decimal addOnTotal = 0;
                if (item.SelectedAddOnNames != null && menuItem.AddOns != null)
                {
                    foreach (var addOnName in item.SelectedAddOnNames)
                    {
                        var match = menuItem.AddOns.FirstOrDefault(a => a.Name.Equals(addOnName, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                        {
                            addOns.Add(new OrderItemAddOn { Name = match.Name, Price = match.Price });
                            addOnTotal += match.Price;
                        }
                    }
                }

                var unitPrice = baseUnitPrice + addOnTotal;
                var itemTotal = unitPrice * item.Quantity;
                roundSubtotal += itemTotal;

                orderItems.Add(new OrderItem
                {
                    MenuItemId = menuItem.Id!,
                    Name = menuItem.Name,
                    Description = menuItem.Description,
                    CategoryId = menuItem.CategoryId,
                    CategoryName = categoryName,
                    Quantity = item.Quantity,
                    Price = unitPrice,
                    BaseUnitPrice = baseUnitPrice,
                    SelectedVariantName = variantName,
                    SelectedVariantPrice = variantPrice,
                    SelectedAddOns = addOns,
                    AddOnTotal = addOnTotal,
                    Total = itemTotal
                });
            }

            var nextRoundNum = (session.OrderIds?.Count ?? 0) + 1;
            // GST & Taxes temporarily disabled (0%) per requirement
            var roundTax = 0m;
            var roundTotal = roundSubtotal + roundTax;

            var newOrderId = ObjectId.GenerateNewId().ToString();
            var kotNumber = isWaiterOrder
                ? await _orderRepo.GetNextDineInKotNumberAsync(outletId)
                : null;
            var order = new Order
            {
                Id = newOrderId,
                OutletId = outletId,
                UserId = userId ?? session.UserId ?? "guest_dinein",
                Username = username ?? session.CustomerName ?? $"Table {tableNumber}",
                Items = orderItems,
                Subtotal = roundSubtotal,
                Tax = roundTax,
                PlatformCharge = 0,
                DeliveryFee = 0,
                Total = roundTotal,
                PaymentStatus = "unpaid",
                PaymentMethod = "dine_in_tab",
                OrderType = "dine-in",
                Channel = "shop",
                TableNumber = tableNumber,
                DineInSessionId = session.Id,
                RoundNumber = nextRoundNum,
                DineInOrderSource = isWaiterOrder ? "waiter" : "qr",
                KotNumber = kotNumber,
                ApprovalStatus = isWaiterOrder ? "approved" : "pending",
                ApprovedByUserId = isWaiterOrder ? userId : null,
                ApprovedAt = isWaiterOrder ? MongoService.GetIstNow() : null,
                PhoneNumber = customerPhone,
                PreparationNotes = orderRequest.PreparationNotes?.Trim(),
                Notes = orderRequest.Notes?.Trim(),
                Status = isWaiterOrder ? "pending" : "awaiting_waiter_approval",
                CreatedAt = MongoService.GetIstNow(),
                UpdatedAt = MongoService.GetIstNow()
            };

            await _orderRepo.CreateOrderAsync(order);

            // Append order to session and update running totals
            session.OrderIds ??= new List<string>();
            session.OrderIds.Add(newOrderId);
            if (string.IsNullOrWhiteSpace(session.CustomerName) && !string.IsNullOrWhiteSpace(username))
                session.CustomerName = username;
            if (string.IsNullOrWhiteSpace(session.CustomerPhone) && !string.IsNullOrWhiteSpace(customerPhone))
                session.CustomerPhone = customerPhone;

            await RecalculateSessionTotalsAsync(session);
            AddSessionEvent(
                session,
                "round_added",
                isWaiterOrder
                    ? $"Round #{nextRoundNum} sent to kitchen by waiter"
                    : $"QR order for round #{nextRoundNum} is awaiting waiter approval",
                userId ?? username ?? session.CustomerName ?? "guest",
                new Dictionary<string, string>
                {
                    ["orderId"] = newOrderId,
                    ["roundNumber"] = nextRoundNum.ToString(CultureInfo.InvariantCulture),
                    ["roundTotal"] = order.Total.ToString("0.00", CultureInfo.InvariantCulture),
                    ["source"] = order.DineInOrderSource
                });
            await _orderRepo.UpdateDineInSessionAsync(session);

            if (isWaiterOrder)
            {
                await _outbox.EnqueueAsync("OrderNotificationKitchen", "Order", order.Id!,
                    new { OrderId = order.Id!, Total = order.Total, TableNumber = tableNumber, RoundNumber = nextRoundNum });

                // Keep printer retries independent from kitchen notifications.
                await _outbox.EnqueueAsync("ThermalKotPrint", "Order", order.Id!,
                    new { OrderId = order.Id!, Trigger = "WaiterSendToKitchen" });
            }

            _log.LogInformation(
                "Dine-In Round #{Round} for Table {Table} placed via {Source}. Order {OrderId}",
                nextRoundNum, tableNumber, order.DineInOrderSource, order.Id);

            var updatedBill = await BuildBillResponseAsync(session);
            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new
            {
                message = isWaiterOrder
                    ? $"Round #{nextRoundNum} sent to Kitchen!"
                    : $"Round #{nextRoundNum} submitted and is awaiting waiter approval.",
                orderId = order.Id,
                roundNumber = nextRoundNum,
                approvalStatus = order.ApprovalStatus,
                session = updatedBill,
                sessionAccessToken
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error placing dine-in order round");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "Failed to place dine-in order" });
            return res;
        }
    }

    /// <summary>
    /// Gets the running bill of an ongoing table session.
    /// Can be called anytime by the customer or staff.
    /// </summary>
    [Function("GetDineInSessionBill")]
    public async Task<HttpResponseData> GetSessionBill(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dine-in/session/{sessionId}/bill")] HttpRequestData req,
        string sessionId)
    {
        try
        {
            var session = await _orderRepo.GetDineInSessionByIdAsync(sessionId);
            if (session == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Dine-in session not found" });
                return notFound;
            }

            if (!HasSessionAccess(req, session))
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "A valid dine-in session token is required" });
                return forbidden;
            }

            var bill = await BuildBillResponseAsync(session);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(bill);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting dine-in bill for session {SessionId}", sessionId);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "Failed to load bill" });
            return res;
        }
    }

    /// <summary>
    /// Customer requests the final bill. Locks tab for additional items and unlocks payment gateway / UPI QR.
    /// </summary>
    [Function("RequestDineInFinalBill")]
    public async Task<HttpResponseData> RequestFinalBill(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "dine-in/session/{sessionId}/request-bill")] HttpRequestData req,
        string sessionId)
    {
        try
        {
            var session = await _orderRepo.GetDineInSessionByIdAsync(sessionId);
            if (session == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Dine-in session not found" });
                return notFound;
            }

            if (!HasSessionAccess(req, session))
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "A valid dine-in session token is required" });
                return forbidden;
            }

            if (session.Status == "paid")
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Bill for this table is already paid and closed" });
                return badReq;
            }

            await RecalculateSessionTotalsAsync(session);
            session.Status = "bill_requested";
            session.BillRequestedAt = MongoService.GetIstNow();
            session.LastPaymentRequestedAt = MongoService.GetIstNow();
            AddSessionEvent(
                session,
                "bill_requested",
                $"Final bill requested for Table {session.TableNumber}",
                session.UserId ?? session.CustomerName ?? "guest");
            await _orderRepo.UpdateDineInSessionAsync(session);

            // Notify cashier & manager
            await _outbox.EnqueueAsync("OrderNotificationAdmin", "DineInSession", session.Id!,
                new { Message = $"Table {session.TableNumber} requested final bill: ₹{session.GrandTotal:N2}", SessionId = session.Id });

            _log.LogInformation("Final bill requested for Table {Table}, Session {SessionId}, Total: ₹{Total}", session.TableNumber, session.Id, session.GrandTotal);

            var bill = await BuildBillResponseAsync(session);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Final bill generated", session = bill });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error requesting final bill for session {SessionId}", sessionId);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "Failed to request final bill" });
            return res;
        }
    }

    /// <summary>
    /// Applies a coupon code to the consolidated dine-in bill.
    /// Allowed ONLY before requesting the final bill.
    /// </summary>
    [Function("ApplyDineInCoupon")]
    public async Task<HttpResponseData> ApplyCoupon(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "dine-in/session/{sessionId}/apply-coupon")] HttpRequestData req,
        string sessionId)
    {
        try
        {
            var session = await _orderRepo.GetDineInSessionByIdAsync(sessionId);
            if (session == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Dine-in session not found" });
                return notFound;
            }

            if (!HasSessionAccess(req, session))
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "A valid dine-in session token is required" });
                return forbidden;
            }

            if (session.Status != "active")
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Coupons can only be applied before requesting the final bill." });
                return badReq;
            }

            var (request, valError) = await ValidationHelper.ValidateBody<ApplyDineInCouponRequest>(req);
            if (valError != null) return valError;

            var couponCode = request.CouponCode.Trim().ToUpperInvariant();
            var offer = await _offerRepo.GetOfferByCodeAsync(couponCode);

            var now = MongoService.GetIstNow();
            if (offer == null || !offer.IsActive || offer.ValidFrom > now || offer.ValidTill < now
                || (offer.UsageLimit.HasValue && offer.UsageCount >= offer.UsageLimit.Value))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Invalid or expired coupon code" });
                return badReq;
            }

            if (session.Subtotal < offer.MinOrderAmount)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = $"Minimum order amount for this coupon is ₹{offer.MinOrderAmount}" });
                return badReq;
            }

            decimal discount = 0;
            if (offer.DiscountType == "percentage")
            {
                discount = (session.Subtotal * offer.DiscountValue) / 100m;
                if (offer.MaxDiscount.HasValue && discount > offer.MaxDiscount.Value)
                    discount = offer.MaxDiscount.Value;
            }
            else
            {
                discount = offer.DiscountValue;
            }

            discount = Math.Min(discount, session.Subtotal);
            session.CouponCode = couponCode;
            session.DiscountAmount = Math.Round(discount, 2);

            await RecalculateSessionTotalsAsync(session);
            await _orderRepo.UpdateDineInSessionAsync(session);

            var bill = await BuildBillResponseAsync(session);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = $"Coupon {couponCode} applied! Saved ₹{session.DiscountAmount}", session = bill });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error applying coupon to session {SessionId}", sessionId);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "Failed to apply coupon" });
            return res;
        }
    }

    /// <summary>
    /// Removes the applied coupon code from the running dine-in tab.
    /// Allowed ONLY before requesting the final bill.
    /// </summary>
    [Function("RemoveDineInCoupon")]
    public async Task<HttpResponseData> RemoveCoupon(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "dine-in/session/{sessionId}/remove-coupon")] HttpRequestData req,
        string sessionId)
    {
        try
        {
            var session = await _orderRepo.GetDineInSessionByIdAsync(sessionId);
            if (session == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Dine-in session not found" });
                return notFound;
            }

            if (!HasSessionAccess(req, session))
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "A valid dine-in session token is required" });
                return forbidden;
            }

            if (session.Status != "active")
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Coupons can only be modified before requesting the final bill." });
                return badReq;
            }

            session.CouponCode = null;
            session.DiscountAmount = 0;

            await RecalculateSessionTotalsAsync(session);
            await _orderRepo.UpdateDineInSessionAsync(session);

            var bill = await BuildBillResponseAsync(session);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Coupon removed", session = bill });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error removing coupon from session {SessionId}", sessionId);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "Failed to remove coupon" });
            return res;
        }
    }

    /// <summary>
    /// Settles and closes the dine-in table session upon payment.
    /// </summary>
    [Function("SettleDineInBill")]
    public async Task<HttpResponseData> SettleBill(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "dine-in/session/{sessionId}/settle")] HttpRequestData req,
        string sessionId)
    {
        try
        {
            var session = await _orderRepo.GetDineInSessionByIdAsync(sessionId);
            if (session == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Dine-in session not found" });
                return notFound;
            }

            if (!HasSessionAccess(req, session))
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "A valid dine-in session token is required" });
                return forbidden;
            }

            if (session.Status == "paid")
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "This bill has already been settled" });
                return badReq;
            }

            if (session.PaymentStatus == "processing")
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = "Payment confirmation is currently in progress" });
                return conflict;
            }

            var remainingBeforeSubmit = GetSessionAmountDue(session);
            if (remainingBeforeSubmit <= 0)
            {
                var alreadyPaid = req.CreateResponse(HttpStatusCode.OK);
                await alreadyPaid.WriteAsJsonAsync(new
                {
                    message = "Bill is already fully paid.",
                    isSettled = true,
                    session = await BuildBillResponseAsync(session)
                });
                return alreadyPaid;
            }

            if (session.Status != "bill_requested"
                || !IsPaymentSubmissionAllowed(session.PaymentStatus))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Request the final bill before submitting payment" });
                return badReq;
            }

            var (request, valError) = await ValidationHelper.ValidateBody<SettleDineInBillRequest>(req);
            if (valError != null) return valError;

            var paymentMethod = request.PaymentMethod.ToLowerInvariant();
            var submittedAmount = request.Amount ?? remainingBeforeSubmit;
            submittedAmount = Math.Round(submittedAmount, 2);

            if (submittedAmount <= 0)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Payment amount must be greater than zero" });
                return badReq;
            }

            if (submittedAmount > remainingBeforeSubmit)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new
                {
                    error = $"Submitted amount exceeds pending dues. Pending due is ₹{remainingBeforeSubmit:0.00}"
                });
                return badReq;
            }

            if (paymentMethod == "upi-qr")
            {
                session.UpiReference = request.UpiReference?.Trim();
            }

            session.PaymentEntries ??= new List<DineInPaymentEntry>();
            var paymentEntry = new DineInPaymentEntry
            {
                Amount = submittedAmount,
                PaymentMethod = paymentMethod,
                UpiReference = request.UpiReference?.Trim(),
                Status = "pending",
                Notes = request.Notes,
                RequestedAt = MongoService.GetIstNow()
            };
            session.PaymentEntries.Add(paymentEntry);
            session.PaymentMethod = paymentMethod;
            session.PaymentStatus = GetPaymentStatusForPendingEntries(session);
            session.Status = "bill_requested";
            session.SettledAt = null;
            session.LastPaymentRequestedAt = MongoService.GetIstNow();
            session.Notes = string.IsNullOrWhiteSpace(session.Notes)
                ? request.Notes
                : $"{session.Notes} | {request.Notes}";

            AddSessionEvent(
                session,
                "payment_submitted",
                $"Payment submitted: ₹{submittedAmount:0.00} via {paymentMethod}",
                session.UserId ?? session.CustomerName ?? "guest",
                new Dictionary<string, string>
                {
                    ["paymentEntryId"] = paymentEntry.Id,
                    ["amount"] = submittedAmount.ToString("0.00", CultureInfo.InvariantCulture),
                    ["paymentMethod"] = paymentMethod
                });

            await _orderRepo.UpdateDineInSessionAsync(session);

            var bill = await BuildBillResponseAsync(session);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = paymentMethod == "cash_at_counter"
                    ? "Cash settlement requested. Please pay at counter."
                    : "UPI payment submitted for staff verification.",
                isSettled = false,
                amountDue = GetSessionAmountDue(session),
                session = bill
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error settling bill for session {SessionId}", sessionId);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "Failed to settle bill" });
            return res;
        }
    }

    [Function("GetDineInSalesReport")]
    public async Task<HttpResponseData> GetDineInSalesReport(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dine-in/reports")] HttpRequestData req)
    {
        var (authorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
        if (!authorized) return errorResponse!;

        try
        {
            var (hasOutletAccess, outletId, outletError) = await OutletHelper.ValidateOutletAccess(
                req, _auth, _mongo, req.Query["outletId"]);
            if (!hasOutletAccess)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = outletError ?? "Outlet access denied" });
                return forbidden;
            }
            if (string.IsNullOrWhiteSpace(outletId))
                return await WriteErrorAsync(req, HttpStatusCode.BadRequest, "Select an outlet to view the dine-in report");

            var startValue = req.Query["startDate"];
            var endValue = req.Query["endDate"];
            if (!DateTime.TryParseExact(startValue, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var startDate)
                || !DateTime.TryParseExact(endValue, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var endDate))
                return await WriteErrorAsync(req, HttpStatusCode.BadRequest, "startDate and endDate must use yyyy-MM-dd");

            if (endDate < startDate || (endDate - startDate).TotalDays > 30)
                return await WriteErrorAsync(req, HttpStatusCode.BadRequest, "Choose a valid date range of no more than 31 days");

            var start = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Unspecified);
            var endExclusive = DateTime.SpecifyKind(endDate.Date.AddDays(1), DateTimeKind.Unspecified);
            var sessions = await _orderRepo.GetDineInSessionsByDateRangeAsync(outletId, start, endExclusive);
            var confirmedPayments = sessions
                .SelectMany(session => session.PaymentEntries ?? new List<DineInPaymentEntry>())
                .Where(entry => string.Equals(entry.Status, "confirmed", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var paymentTotals = confirmedPayments
                .GroupBy(entry => string.IsNullOrWhiteSpace(entry.PaymentMethod) ? "unspecified" : entry.PaymentMethod)
                .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Amount), StringComparer.OrdinalIgnoreCase);

            var istZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
            var auditEvents = await _eventLog.GetOutletEventsAsync(
                outletId,
                TimeZoneInfo.ConvertTimeToUtc(start, istZone),
                TimeZoneInfo.ConvertTimeToUtc(endExclusive, istZone),
                500);
            var sessionRows = sessions.Select(session => new
            {
                sessionId = session.Id,
                tableNumber = session.TableNumber,
                customerName = session.CustomerName,
                status = session.Status,
                paymentStatus = session.PaymentStatus,
                createdAt = session.CreatedAt,
                settledAt = session.SettledAt,
                grandTotal = session.GrandTotal,
                amountPaid = session.AmountPaid,
                amountDue = GetSessionAmountDue(session),
                timeline = (session.Timeline ?? new List<DineInSessionEvent>())
                    .OrderByDescending(entry => entry.CreatedAt)
                    .Take(50)
            });

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                startDate = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                endDate = endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                summary = new
                {
                    sessionCount = sessions.Count,
                    settledSessions = sessions.Count(session => session.Status == "paid"),
                    totalBilled = sessions.Sum(session => session.GrandTotal),
                    salesCollected = sessions.Sum(session => session.AmountPaid),
                    outstandingBalance = sessions.Sum(GetSessionAmountDue),
                    paymentTotals
                },
                sessions = sessionRows,
                auditEvents
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to generate dine-in sales report");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "Failed to generate dine-in report" });
            return response;
        }
    }

    [Function("GetCashierPendingDineInPayments")]
    public async Task<HttpResponseData> GetCashierPendingDineInPayments(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dine-in/cashier/pending-payments")] HttpRequestData req)
    {
        var (authorized, _, _, errorResponse) = await AuthorizationHelper.ValidateCashierRole(req, _auth);
        if (!authorized) return errorResponse!;

        try
        {
            var (hasOutletAccess, outletId, accessError) = await OutletHelper.ValidateOutletAccess(
                req, _auth, _mongo, req.Query["outletId"]);
            if (!hasOutletAccess)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = accessError ?? "Outlet access denied" });
                return forbidden;
            }
            if (string.IsNullOrWhiteSpace(outletId))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Select an outlet to view pending table payments" });
                return badRequest;
            }

            var sessions = await _orderRepo.GetActiveDineInSessionsAsync(outletId);
            var activeSessions = sessions
                .Select(session => new
                {
                    sessionId = session.Id,
                    tableNumber = session.TableNumber,
                    customerName = session.CustomerName,
                    sessionStatus = session.Status,
                    paymentStatus = session.PaymentStatus,
                    grandTotal = session.GrandTotal,
                    amountPaid = session.AmountPaid,
                    amountDue = GetSessionAmountDue(session),
                    hasPendingPayment = (session.PaymentEntries ?? new List<DineInPaymentEntry>())
                        .Any(entry => string.Equals(entry.Status, "pending", StringComparison.OrdinalIgnoreCase))
                })
                .OrderBy(session => session.tableNumber)
                .ToList();
            var pendingPayments = sessions
                .Select(session => new
                {
                    sessionId = session.Id,
                    tableNumber = session.TableNumber,
                    customerName = session.CustomerName,
                    customerPhone = session.CustomerPhone,
                    sessionStatus = session.Status,
                    paymentStatus = session.PaymentStatus,
                    grandTotal = session.GrandTotal,
                    amountPaid = session.AmountPaid,
                    amountDue = GetSessionAmountDue(session),
                    pendingPayment = (session.PaymentEntries ?? new List<DineInPaymentEntry>())
                        .Where(entry => string.Equals(entry.Status, "pending", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(entry => entry.RequestedAt)
                        .Select(entry => new
                        {
                            entry.Id,
                            entry.Amount,
                            entry.PaymentMethod,
                            entry.UpiReference,
                            entry.Notes,
                            entry.RequestedAt
                        })
                        .FirstOrDefault()
                })
                .Where(session => session.pendingPayment != null)
                .OrderBy(session => session.pendingPayment!.RequestedAt)
                .ToList();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { payments = pendingPayments, sessions = activeSessions });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load pending dine-in payments for cashier");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "Failed to load pending dine-in payments" });
            return response;
        }
    }

    [Function("RecordCashierDineInPayment")]
    public async Task<HttpResponseData> RecordCashierDineInPayment(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "dine-in/session/{sessionId}/cashier/payment")] HttpRequestData req,
        string sessionId)
    {
        string? claimedPaymentStatus = null;
        try
        {
            var (authorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateCashierRole(req, _auth);
            if (!authorized) return errorResponse!;

            var session = await _orderRepo.GetDineInSessionByIdAsync(sessionId);
            if (session == null)
                return await WriteErrorAsync(req, HttpStatusCode.NotFound, "Dine-in session not found");

            var (hasOutletAccess, accessibleOutletId, outletError) = await OutletHelper.ValidateOutletAccess(
                req, _auth, _mongo, req.Query["outletId"] ?? session.OutletId);
            if (!hasOutletAccess || !string.Equals(accessibleOutletId, session.OutletId, StringComparison.Ordinal))
                return await WriteErrorAsync(req, HttpStatusCode.Forbidden, outletError ?? "You cannot record payments for this outlet");

            if (string.Equals(session.Status, "paid", StringComparison.OrdinalIgnoreCase)
                || string.Equals(session.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
                return await WriteErrorAsync(req, HttpStatusCode.Conflict, "This table session is already closed");

            var (request, validationError) = await ValidationHelper.ValidateBody<SettleDineInBillRequest>(req);
            if (validationError != null) return validationError;
            if (request == null || request.Amount == null)
                return await WriteErrorAsync(req, HttpStatusCode.BadRequest, "Payment amount is required");

            session.PaymentEntries ??= new List<DineInPaymentEntry>();
            if (session.PaymentEntries.Any(entry =>
                    string.Equals(entry.Status, "pending", StringComparison.OrdinalIgnoreCase)))
                return await WriteErrorAsync(req, HttpStatusCode.Conflict,
                    "Confirm or resolve the customer's pending payment request before recording another payment");

            if (session.PaymentStatus is not ("unpaid" or "partially_paid"))
                return await WriteErrorAsync(req, HttpStatusCode.Conflict,
                    "This session is not available for direct cashier payment");

            await RecalculateSessionTotalsAsync(session);
            var amountDue = GetSessionAmountDue(session);
            var amount = Math.Round(request.Amount.Value, 2);
            if (amount <= 0 || amount > amountDue)
                return await WriteErrorAsync(req, HttpStatusCode.BadRequest,
                    $"Payment amount must be greater than zero and no more than the balance due of ₹{amountDue:0.00}");

            claimedPaymentStatus = session.PaymentStatus;
            if (!await _orderRepo.TransitionDineInPaymentStatusAsync(
                    sessionId, new[] { claimedPaymentStatus }, "processing"))
                return await WriteErrorAsync(req, HttpStatusCode.Conflict,
                    "Another payment is being recorded for this table; refresh and try again");

            var paymentMethod = request.PaymentMethod.Trim().ToLowerInvariant();
            var paymentTime = MongoService.GetIstNow();
            var paymentEntry = new DineInPaymentEntry
            {
                Amount = amount,
                PaymentMethod = paymentMethod,
                UpiReference = request.UpiReference?.Trim(),
                Status = "confirmed",
                Notes = request.Notes?.Trim(),
                RequestedAt = paymentTime,
                ConfirmedAt = paymentTime,
                ConfirmedBy = userId
            };
            session.PaymentEntries.Add(paymentEntry);
            session.PaymentMethod = paymentMethod;
            session.AmountPaid = Math.Round(session.AmountPaid + amount, 2);
            session.Notes = string.IsNullOrWhiteSpace(session.Notes)
                ? $"Payment recorded by {userId}"
                : $"{session.Notes} | Payment recorded by {userId}";
            AddSessionEvent(
                session,
                "cashier_payment_recorded",
                $"Cashier recorded payment: ₹{amount:0.00} via {paymentMethod}",
                userId,
                new Dictionary<string, string>
                {
                    ["paymentEntryId"] = paymentEntry.Id,
                    ["amount"] = amount.ToString("0.00", CultureInfo.InvariantCulture),
                    ["paymentMethod"] = paymentMethod,
                    ["remainingDue"] = Math.Max(0, amountDue - amount).ToString("0.00", CultureInfo.InvariantCulture)
                });

            if (amountDue - amount > 0)
            {
                session.PaymentStatus = "partially_paid";
                session.Status = "bill_requested";
                session.SettledAt = null;
                if (!await _orderRepo.UpdateDineInSessionAsync(session))
                {
                    await _orderRepo.TransitionDineInPaymentStatusAsync(
                        sessionId, new[] { "processing" }, claimedPaymentStatus);
                    claimedPaymentStatus = null;
                    return await WriteErrorAsync(req, HttpStatusCode.InternalServerError,
                        "Payment could not be saved. Refresh the table balance before retrying.");
                }
                claimedPaymentStatus = null;
                var partialResponse = req.CreateResponse(HttpStatusCode.OK);
                await partialResponse.WriteAsJsonAsync(new
                {
                    message = $"Payment recorded. ₹{Math.Max(0, amountDue - amount):0.00} still due.",
                    session = await BuildBillResponseAsync(session)
                });
                return partialResponse;
            }

            if (!await FinalizeSessionPaymentAsync(session))
            {
                await _orderRepo.TransitionDineInPaymentStatusAsync(
                    sessionId, new[] { "processing" }, claimedPaymentStatus);
                claimedPaymentStatus = null;
                return await WriteErrorAsync(req, HttpStatusCode.Conflict,
                    "Unable to finalize settlement. Check the bill and try again.");
            }

            claimedPaymentStatus = null;
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = "Payment recorded and table closed",
                session = await BuildBillResponseAsync(session)
            });
            return response;
        }
        catch (Exception ex)
        {
            if (claimedPaymentStatus != null)
                await _orderRepo.TransitionDineInPaymentStatusAsync(
                    sessionId, new[] { "processing" }, claimedPaymentStatus);
            _log.LogError(ex, "Error recording cashier payment for dine-in session {SessionId}", sessionId);
            return await WriteErrorAsync(req, HttpStatusCode.InternalServerError, "Failed to record cashier payment");
        }
    }

    [Function("ConfirmDineInPayment")]
    public async Task<HttpResponseData> ConfirmDineInPayment(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "dine-in/session/{sessionId}/payment/confirm")] HttpRequestData req,
        string sessionId)
    {
        string? claimedPaymentStatus = null;
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateCashierRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var session = await _orderRepo.GetDineInSessionByIdAsync(sessionId);
            if (session == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Dine-in session not found" });
                return notFound;
            }

            var (hasOutletAccess, accessibleOutletId, outletError) = await OutletHelper.ValidateOutletAccess(
                req, _auth, _mongo, req.Query["outletId"] ?? session.OutletId);
            if (!hasOutletAccess || !string.Equals(accessibleOutletId, session.OutletId, StringComparison.Ordinal))
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = outletError ?? "You cannot confirm payments for this outlet" });
                return forbidden;
            }

            if (session.Status == "paid")
            {
                var ok = req.CreateResponse(HttpStatusCode.OK);
                await ok.WriteAsJsonAsync(new { message = "Payment already confirmed", session = await BuildBillResponseAsync(session) });
                return ok;
            }

            session.PaymentEntries ??= new List<DineInPaymentEntry>();
            var hasPendingPayments = session.PaymentEntries.Any(p => string.Equals(p.Status, "pending", StringComparison.OrdinalIgnoreCase));

            if (!hasPendingPayments)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "This session has no payment awaiting confirmation" });
                return badRequest;
            }

            var (confirmReq, valErr) = await ValidationHelper.ValidateBody<ConfirmDineInPaymentRequest>(req);
            if (valErr != null) return valErr;
            confirmReq ??= new ConfirmDineInPaymentRequest();

            var pendingPayment = session.PaymentEntries
                .Where(p => string.Equals(p.Status, "pending", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p.RequestedAt)
                .FirstOrDefault();

            if (pendingPayment == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "No pending payment entry found for confirmation" });
                return badRequest;
            }

            var remainingBeforeConfirm = GetSessionAmountDue(session);
            if (remainingBeforeConfirm <= 0)
            {
                if (!await FinalizeSessionPaymentAsync(session))
                {
                    var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                    await conflict.WriteAsJsonAsync(new { error = "Unable to finalize session settlement" });
                    return conflict;
                }

                var ok = req.CreateResponse(HttpStatusCode.OK);
                await ok.WriteAsJsonAsync(new { message = "Payment already confirmed", session = await BuildBillResponseAsync(session) });
                return ok;
            }

            var confirmedAmount = Math.Round(confirmReq.ConfirmedAmount ?? pendingPayment.Amount, 2);
            if (confirmedAmount <= 0)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Confirmed amount must be greater than zero" });
                return badRequest;
            }

            if (confirmedAmount > remainingBeforeConfirm)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new
                {
                    error = $"Confirmed amount exceeds pending dues. Pending due is ₹{remainingBeforeConfirm:0.00}"
                });
                return badRequest;
            }

            claimedPaymentStatus = session.PaymentStatus;
            if (!await _orderRepo.TransitionDineInPaymentStatusAsync(
                    sessionId,
                    new[] { "pending_verification", "pending_cash", "partially_paid" },
                    "processing"))
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = "This payment is already being confirmed by another staff member" });
                return conflict;
            }
            session.PaymentStatus = "processing";

            pendingPayment.Amount = confirmedAmount;
            pendingPayment.Status = "confirmed";
            pendingPayment.ConfirmedAt = MongoService.GetIstNow();
            pendingPayment.ConfirmedBy = userId;
            if (!string.IsNullOrWhiteSpace(confirmReq.Notes))
            {
                pendingPayment.Notes = string.IsNullOrWhiteSpace(pendingPayment.Notes)
                    ? confirmReq.Notes
                    : $"{pendingPayment.Notes} | {confirmReq.Notes}";
            }

            session.AmountPaid = Math.Round(session.AmountPaid + confirmedAmount, 2);
            session.Notes = string.IsNullOrWhiteSpace(session.Notes)
                ? $"Payment confirmed by {userId}"
                : $"{session.Notes} | Payment confirmed by {userId}";

            AddSessionEvent(
                session,
                "payment_confirmed",
                $"Payment confirmed: ₹{confirmedAmount:0.00}",
                userId,
                new Dictionary<string, string>
                {
                    ["paymentEntryId"] = pendingPayment.Id,
                    ["amount"] = confirmedAmount.ToString("0.00", CultureInfo.InvariantCulture),
                    ["remainingDue"] = Math.Max(0, GetSessionAmountDue(session)).ToString("0.00", CultureInfo.InvariantCulture)
                });

            var remainingAfterConfirm = GetSessionAmountDue(session);
            if (remainingAfterConfirm > 0)
            {
                session.PaymentStatus = GetPaymentStatusForPendingEntries(session);
                session.Status = "bill_requested";
                session.SettledAt = null;
                await _orderRepo.UpdateDineInSessionAsync(session);

                var partialResp = req.CreateResponse(HttpStatusCode.OK);
                await partialResp.WriteAsJsonAsync(new
                {
                    message = $"Partial payment confirmed. ₹{remainingAfterConfirm:0.00} still due.",
                    session = await BuildBillResponseAsync(session)
                });
                return partialResp;
            }

            if (!await FinalizeSessionPaymentAsync(session))
            {
                await _orderRepo.TransitionDineInPaymentStatusAsync(sessionId, new[] { "processing" }, claimedPaymentStatus);
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = "The applied coupon is no longer available. Remove it and recalculate the bill." });
                return conflict;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = "Dine-in payment confirmed and table closed",
                session = await BuildBillResponseAsync(session)
            });
            return response;
        }
        catch (Exception ex)
        {
            if (claimedPaymentStatus != null)
            {
                await _orderRepo.TransitionDineInPaymentStatusAsync(sessionId, new[] { "processing" }, claimedPaymentStatus);
            }
            _log.LogError(ex, "Error confirming payment for dine-in session {SessionId}", sessionId);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "Failed to confirm dine-in payment" });
            return response;
        }
    }

    [Function("GetDineInPaymentReconciliation")]
    public async Task<HttpResponseData> GetPaymentReconciliation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dine-in/session/{sessionId}/payment/reconcile")] HttpRequestData req,
        string sessionId)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var session = await _orderRepo.GetDineInSessionByIdAsync(sessionId);
            if (session == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Dine-in session not found" });
                return notFound;
            }

            await RecalculateSessionTotalsAsync(session);
            var due = GetSessionAmountDue(session);
            var payload = new DineInPaymentReconciliationResponse
            {
                SessionId = session.Id ?? sessionId,
                GrandTotal = Math.Round(session.GrandTotal, 2),
                AmountPaid = Math.Round(session.AmountPaid, 2),
                AmountDue = due,
                IsFullySettled = due <= 0,
                PaymentEntries = session.PaymentEntries
                    .OrderByDescending(p => p.RequestedAt)
                    .ToList()
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(payload);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting payment reconciliation for session {SessionId}", sessionId);
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to load payment reconciliation" });
            return error;
        }
    }

    /// <summary>
    /// Lists all active table sessions for admin and manager roles.
    /// </summary>
    [Function("GetActiveDineInTables")]
    public async Task<HttpResponseData> GetActiveTables(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dine-in/tables/active")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = req.Query["outletId"]?.Trim();
            outletId = await ResolveOutletIdAsync(req, outletId);

            if (string.IsNullOrWhiteSpace(outletId))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Outlet ID required" });
                return badReq;
            }

            var sessions = await _orderRepo.GetActiveDineInSessionsAsync(outletId);
            var summaries = new List<object>();

            foreach (var s in sessions)
            {
                var amountDue = Math.Max(0, Math.Round(s.GrandTotal - s.AmountPaid, 2));
                summaries.Add(new
                {
                    s.Id,
                    s.TableNumber,
                    s.CustomerName,
                    s.Status,
                    s.PaymentStatus,
                    RoundsCount = s.OrderIds.Count,
                    s.Subtotal,
                    s.GrandTotal,
                    s.AmountPaid,
                    AmountDue = amountDue,
                    s.CreatedAt,
                    s.BillRequestedAt,
                    MinutesActive = (int)(MongoService.GetIstNow() - s.CreatedAt).TotalMinutes
                });
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(summaries);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting active dine-in tables");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "Failed to load active tables" });
            return res;
        }
    }

    [Function("ReopenDineInOrdering")]
    public async Task<HttpResponseData> ReopenOrdering(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "dine-in/session/{sessionId}/reopen-ordering")] HttpRequestData req,
        string sessionId)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var session = await _orderRepo.GetDineInSessionByIdAsync(sessionId);
            if (session == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Dine-in session not found" });
                return notFound;
            }

            if (session.Status == "paid")
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Cannot reopen ordering for a paid/closed session" });
                return badReq;
            }

            var (request, valError) = await ValidationHelper.ValidateBody<DineInReopenOrderingRequest>(req);
            if (valError != null) return valError;

            session.Status = "active";
            session.BillRequestedAt = null;
            session.ReopenedByUserId = userId;
            session.ReopenedReason = request.Reason.Trim();
            session.PaymentStatus = session.AmountPaid > 0 ? "partially_paid" : "unpaid";

            AddSessionEvent(
                session,
                "ordering_reopened",
                $"Ordering reopened by manager: {request.Reason.Trim()}",
                userId);

            await _orderRepo.UpdateDineInSessionAsync(session);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = "Ordering reopened for this table session",
                session = await BuildBillResponseAsync(session)
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error reopening ordering for session {SessionId}", sessionId);
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to reopen ordering" });
            return error;
        }
    }

    [Function("RequestDineInItemVoid")]
    public async Task<HttpResponseData> RequestItemVoid(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "dine-in/session/{sessionId}/void/request")] HttpRequestData req,
        string sessionId)
    {
        try
        {
            var session = await _orderRepo.GetDineInSessionByIdAsync(sessionId);
            if (session == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Dine-in session not found" });
                return notFound;
            }

            if (!HasSessionAccess(req, session))
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "A valid dine-in session token is required" });
                return forbidden;
            }

            if (session.Status == "paid")
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Cannot request item void after bill is closed" });
                return badReq;
            }

            var (request, valError) = await ValidationHelper.ValidateBody<DineInVoidItemRequest>(req);
            if (valError != null) return valError;

            var order = await _orderRepo.GetOrderByIdAsync(request.OrderId.Trim());
            if (order == null || order.DineInSessionId != session.Id)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Round order not found in this session" });
                return badReq;
            }

            var item = order.Items.FirstOrDefault(i => i.MenuItemId == request.MenuItemId.Trim());
            if (item == null)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Requested item not found in order" });
                return badReq;
            }

            if (request.Quantity > item.Quantity)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = $"Cannot void more than ordered quantity ({item.Quantity})" });
                return badReq;
            }

            var actor = ResolveActorLabel(req, session);
            var voidRequest = new DineInVoidRequest
            {
                OrderId = order.Id ?? string.Empty,
                MenuItemId = item.MenuItemId,
                ItemName = item.Name,
                Quantity = request.Quantity,
                Reason = request.Reason.Trim(),
                Status = "pending",
                RequestedBy = actor,
                RequestedAt = MongoService.GetIstNow()
            };

            session.VoidRequests ??= new List<DineInVoidRequest>();
            session.VoidRequests.Add(voidRequest);
            AddSessionEvent(
                session,
                "void_requested",
                $"Void requested: {voidRequest.ItemName} x{voidRequest.Quantity}",
                actor,
                new Dictionary<string, string>
                {
                    ["voidRequestId"] = voidRequest.Id,
                    ["orderId"] = voidRequest.OrderId,
                    ["reason"] = voidRequest.Reason
                });

            await _orderRepo.UpdateDineInSessionAsync(session);

            var response = req.CreateResponse(HttpStatusCode.Accepted);
            await response.WriteAsJsonAsync(new
            {
                message = "Void request submitted for manager approval",
                voidRequestId = voidRequest.Id,
                session = await BuildBillResponseAsync(session)
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error requesting dine-in item void for session {SessionId}", sessionId);
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to submit void request" });
            return error;
        }
    }

    [Function("ReviewDineInVoidRequest")]
    public async Task<HttpResponseData> ReviewVoidRequest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "dine-in/session/{sessionId}/void/{voidRequestId}/review")] HttpRequestData req,
        string sessionId,
        string voidRequestId)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var session = await _orderRepo.GetDineInSessionByIdAsync(sessionId);
            if (session == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Dine-in session not found" });
                return notFound;
            }

            var (request, valError) = await ValidationHelper.ValidateBody<DineInReviewVoidRequest>(req);
            if (valError != null) return valError;

            var voidRequest = (session.VoidRequests ?? new List<DineInVoidRequest>())
                .FirstOrDefault(v => string.Equals(v.Id, voidRequestId, StringComparison.Ordinal));
            if (voidRequest == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Void request not found" });
                return notFound;
            }

            if (!string.Equals(voidRequest.Status, "pending", StringComparison.OrdinalIgnoreCase))
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = "Void request already reviewed" });
                return conflict;
            }

            var decision = request.Decision.ToLowerInvariant();
            voidRequest.ReviewedBy = userId;
            voidRequest.ReviewedAt = MongoService.GetIstNow();
            voidRequest.ReviewNotes = request.Notes?.Trim();

            if (decision == "rejected")
            {
                voidRequest.Status = "rejected";
                AddSessionEvent(session, "void_rejected", $"Void request rejected for {voidRequest.ItemName}", userId);
                await _orderRepo.UpdateDineInSessionAsync(session);

                var rejected = req.CreateResponse(HttpStatusCode.OK);
                await rejected.WriteAsJsonAsync(new { message = "Void request rejected", session = await BuildBillResponseAsync(session) });
                return rejected;
            }

            var order = await _orderRepo.GetOrderByIdAsync(voidRequest.OrderId);
            if (order == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Associated order no longer exists" });
                return notFound;
            }

            var item = order.Items.FirstOrDefault(i => i.MenuItemId == voidRequest.MenuItemId);
            if (item == null)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Associated item no longer exists in the order" });
                return badReq;
            }

            if (voidRequest.Quantity > item.Quantity)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Void quantity exceeds current item quantity" });
                return badReq;
            }

            item.Quantity -= voidRequest.Quantity;
            item.Total = Math.Round(item.Price * item.Quantity, 2);
            if (item.Quantity == 0)
            {
                order.Items.Remove(item);
            }

            order.Subtotal = Math.Round(order.Items.Sum(i => i.Total), 2);
            order.Total = Math.Round(order.Subtotal + order.Tax + order.PlatformCharge + order.DeliveryFee, 2);
            order.UpdatedAt = MongoService.GetIstNow();
            if (!order.Items.Any())
            {
                order.Status = "cancelled";
            }

            await _orderRepo.UpdateOrderAsync(order);

            voidRequest.Status = "approved";
            await RecalculateSessionTotalsAsync(session);
            AddSessionEvent(
                session,
                "void_approved",
                $"Void approved: {voidRequest.ItemName} x{voidRequest.Quantity}",
                userId,
                new Dictionary<string, string>
                {
                    ["voidRequestId"] = voidRequest.Id,
                    ["orderId"] = order.Id ?? string.Empty
                });
            await _orderRepo.UpdateDineInSessionAsync(session);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = "Void approved and bill updated",
                session = await BuildBillResponseAsync(session)
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error reviewing void request {VoidRequestId} for session {SessionId}", voidRequestId, sessionId);
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to review void request" });
            return error;
        }
    }

    [Function("ReseatDineInSession")]
    public async Task<HttpResponseData> ReseatSession(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "dine-in/session/{sessionId}/reseat")] HttpRequestData req,
        string sessionId)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, valError) = await ValidationHelper.ValidateBody<DineInReseatRequest>(req);
            if (valError != null) return valError;

            var session = await _orderRepo.GetDineInSessionByIdAsync(sessionId);
            if (session == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Dine-in session not found" });
                return notFound;
            }

            var targetTable = request.NewTableNumber.Trim();
            if (string.Equals(targetTable, session.TableNumber, StringComparison.OrdinalIgnoreCase))
            {
                var ok = req.CreateResponse(HttpStatusCode.OK);
                await ok.WriteAsJsonAsync(new { message = "Session already assigned to this table", session = await BuildBillResponseAsync(session) });
                return ok;
            }

            var occupied = await _orderRepo.GetActiveDineInSessionByTableAsync(session.OutletId, targetTable);
            if (occupied != null && occupied.Id != session.Id)
            {
                if (!request.MergeIfOccupied)
                {
                    var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                    await conflict.WriteAsJsonAsync(new { error = "Target table has an active session. Enable merge to combine sessions." });
                    return conflict;
                }

                foreach (var orderId in session.OrderIds)
                {
                    if (!occupied.OrderIds.Contains(orderId))
                    {
                        occupied.OrderIds.Add(orderId);
                    }

                    var order = await _orderRepo.GetOrderByIdAsync(orderId);
                    if (order != null)
                    {
                        order.DineInSessionId = occupied.Id;
                        order.TableNumber = targetTable;
                        order.UpdatedAt = MongoService.GetIstNow();
                        await _orderRepo.UpdateOrderAsync(order);
                    }
                }

                occupied.AmountPaid = Math.Round(occupied.AmountPaid + session.AmountPaid, 2);
                occupied.PaymentEntries ??= new List<DineInPaymentEntry>();
                occupied.PaymentEntries.AddRange(session.PaymentEntries ?? new List<DineInPaymentEntry>());
                occupied.VoidRequests ??= new List<DineInVoidRequest>();
                occupied.VoidRequests.AddRange(session.VoidRequests ?? new List<DineInVoidRequest>());
                occupied.Timeline ??= new List<DineInSessionEvent>();
                occupied.Timeline.AddRange(session.Timeline ?? new List<DineInSessionEvent>());
                occupied.TableNumber = targetTable;

                await RecalculateSessionTotalsAsync(occupied);
                AddSessionEvent(occupied, "session_merged_in", $"Merged session {session.Id} into this table", userId);
                await _orderRepo.UpdateDineInSessionAsync(occupied);

                session.Status = "cancelled";
                session.MergedIntoSessionId = occupied.Id;
                session.MergeNote = request.Reason?.Trim();
                AddSessionEvent(session, "session_merged_out", $"Merged into session {occupied.Id}", userId);
                await _orderRepo.UpdateDineInSessionAsync(session);

                var merged = req.CreateResponse(HttpStatusCode.OK);
                await merged.WriteAsJsonAsync(new
                {
                    message = $"Sessions merged at Table {targetTable}",
                    session = await BuildBillResponseAsync(occupied)
                });
                return merged;
            }

            var previousTable = session.TableNumber;
            session.TableNumber = targetTable;
            AddSessionEvent(
                session,
                "reseated",
                $"Reseated from Table {previousTable} to Table {targetTable}",
                userId,
                string.IsNullOrWhiteSpace(request.Reason)
                    ? null
                    : new Dictionary<string, string> { ["reason"] = request.Reason.Trim() });

            await _orderRepo.UpdateDineInSessionAsync(session);

            foreach (var orderId in session.OrderIds)
            {
                var order = await _orderRepo.GetOrderByIdAsync(orderId);
                if (order == null) continue;
                order.TableNumber = targetTable;
                order.UpdatedAt = MongoService.GetIstNow();
                await _orderRepo.UpdateOrderAsync(order);
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = $"Table reassigned from {previousTable} to {targetTable}",
                session = await BuildBillResponseAsync(session)
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error reseating session {SessionId}", sessionId);
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to reseat table session" });
            return error;
        }
    }

    [Function("GetDineInSlaAlerts")]
    public async Task<HttpResponseData> GetSlaAlerts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dine-in/alerts/sla")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = req.Query["outletId"]?.Trim();
            outletId = await ResolveOutletIdAsync(req, outletId);
            if (string.IsNullOrWhiteSpace(outletId))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Outlet ID required" });
                return badReq;
            }

            var sessions = await _orderRepo.GetActiveDineInSessionsAsync(outletId);
            var now = MongoService.GetIstNow();
            var alerts = new List<object>();

            foreach (var session in sessions)
            {
                foreach (var orderId in session.OrderIds)
                {
                    var order = await _orderRepo.GetOrderByIdAsync(orderId);
                    if (order == null) continue;
                    if (string.Equals(order.Status, "delivered", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(order.Status, "cancelled", StringComparison.OrdinalIgnoreCase)) continue;

                    var waitedMinutes = (int)Math.Max(0, (now - order.CreatedAt).TotalMinutes);
                    var threshold = order.Status switch
                    {
                        "pending" => 6,
                        "confirmed" => 8,
                        "preparing" => 20,
                        "ready" => 12,
                        "served" => 20,
                        _ => 25
                    };

                    if (waitedMinutes < threshold) continue;

                    alerts.Add(new
                    {
                        sessionId = session.Id,
                        orderId = order.Id,
                        tableNumber = session.TableNumber,
                        status = order.Status,
                        waitedMinutes,
                        thresholdMinutes = threshold,
                        severity = waitedMinutes >= threshold * 2 ? "critical" : "warning",
                        createdAt = order.CreatedAt
                    });
                }
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                outletId,
                generatedAt = now,
                totalAlerts = alerts.Count,
                alerts
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting dine-in SLA alerts");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to load dine-in SLA alerts" });
            return error;
        }
    }

    [Function("GetDineInDayEndOpenTables")]
    public async Task<HttpResponseData> GetDayEndOpenTables(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dine-in/day-end/open-tables")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = req.Query["outletId"]?.Trim();
            outletId = await ResolveOutletIdAsync(req, outletId);
            if (string.IsNullOrWhiteSpace(outletId))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Outlet ID required" });
                return badReq;
            }

            var sessions = await _orderRepo.GetActiveDineInSessionsAsync(outletId);
            var now = MongoService.GetIstNow();
            var openSessions = sessions
                .Where(s => s.Status != "paid")
                .Select(s => new
                {
                    s.Id,
                    s.TableNumber,
                    s.CustomerName,
                    s.Status,
                    s.PaymentStatus,
                    s.Subtotal,
                    s.GrandTotal,
                    s.AmountPaid,
                    AmountDue = Math.Max(0, Math.Round(s.GrandTotal - s.AmountPaid, 2)),
                    s.CreatedAt,
                    s.BillRequestedAt,
                    MinutesOpen = (int)Math.Max(0, (now - s.CreatedAt).TotalMinutes)
                })
                .OrderByDescending(s => s.AmountDue)
                .ThenByDescending(s => s.MinutesOpen)
                .ToList();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                outletId,
                generatedAt = now,
                openTablesCount = openSessions.Count,
                totalOutstandingAmount = Math.Round(openSessions.Sum(s => s.AmountDue), 2),
                requiresAttention = openSessions.Any(s => s.AmountDue > 0),
                sessions = openSessions
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error running day-end open table safety check");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to run day-end safety check" });
            return error;
        }
    }

    #region Helpers

    private static bool IsPaymentSubmissionAllowed(string? paymentStatus)
    {
        return paymentStatus == "unpaid"
            || paymentStatus == "partially_paid"
            || paymentStatus == "pending_verification"
            || paymentStatus == "pending_cash";
    }

    private static string GetPaymentStatusForPendingEntries(DineInSession session)
    {
        session.PaymentEntries ??= new List<DineInPaymentEntry>();

        var latestPending = session.PaymentEntries
            .Where(p => string.Equals(p.Status, "pending", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.RequestedAt)
            .FirstOrDefault();

        if (latestPending == null)
        {
            return "partially_paid";
        }

        return string.Equals(latestPending.PaymentMethod, "cash_at_counter", StringComparison.OrdinalIgnoreCase)
            ? "pending_cash"
            : "pending_verification";
    }

    private async Task<bool> FinalizeSessionPaymentAsync(DineInSession session)
    {
        await RecalculateSessionTotalsAsync(session);
        if (GetSessionAmountDue(session) > 0)
        {
            return false;
        }

        Offer? appliedOffer = null;
        if (!string.IsNullOrWhiteSpace(session.CouponCode))
        {
            appliedOffer = await _offerRepo.GetOfferByCodeAsync(session.CouponCode);
            if (appliedOffer?.Id == null || !await _offerRepo.IncrementOfferUsageAsync(appliedOffer.Id))
                return false;
        }

        try
        {
        session.AmountPaid = Math.Round(session.GrandTotal, 2);
        session.PaymentStatus = "paid";
        session.Status = "paid";
        session.SettledAt = MongoService.GetIstNow();
        session.UpdatedAt = MongoService.GetIstNow();
        session.PaymentEntries ??= new List<DineInPaymentEntry>();
        foreach (var entry in session.PaymentEntries.Where(e => string.Equals(e.Status, "pending", StringComparison.OrdinalIgnoreCase)))
        {
            entry.Status = "confirmed";
            entry.ConfirmedAt ??= session.SettledAt;
        }
        AddSessionEvent(
            session,
            "session_paid",
            $"Session closed as paid. Total settled: ₹{session.GrandTotal:0.00}",
            "system");
        if (!await _orderRepo.UpdateDineInSessionAsync(session))
            throw new InvalidOperationException($"Failed to persist settled dine-in session {session.Id}");

        foreach (var orderId in session.OrderIds)
        {
            await _orderRepo.UpdatePaymentStatusAsync(orderId, "paid");

            var order = await _orderRepo.GetOrderByIdAsync(orderId);
            if (order == null) continue;

            // Closing a paid dine-in bill should clear KDS by moving all non-cancelled rounds to delivered.
            if (!string.Equals(order.Status, "cancelled", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(order.Status, "delivered", StringComparison.OrdinalIgnoreCase))
            {
                await _orderRepo.UpdateOrderStatusAsync(orderId, "delivered");
            }
        }

        try
        {
            await _operationsRepo.CompleteReservationForDineInSessionAsync(session);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not complete reservation for dine-in session {SessionId}", session.Id);
        }

        if (!string.IsNullOrWhiteSpace(session.UserId) && session.UserId != "guest_dinein")
        {
            var pointsToAward = (int)Math.Floor(session.GrandTotal * 0.10m);
            if (pointsToAward > 0)
            {
                await _outbox.EnqueueAsync("LoyaltyPointsAwardExact", "DineInSession", session.Id!,
                    new { UserId = session.UserId, Points = pointsToAward, Reason = $"Dine-In Table {session.TableNumber} bill settlement", OrderId = session.Id });
                await _outbox.EnqueueAsync("LoyaltyNotification", "DineInSession", session.Id!,
                    new { UserId = session.UserId, PointsEarned = pointsToAward, TotalPoints = pointsToAward, Reason = $"Dine-In Table {session.TableNumber}" });
            }
        }
        return true;
        }
        catch
        {
            if (appliedOffer?.Id != null)
                await _offerRepo.DecrementOfferUsageAsync(appliedOffer.Id);
            throw;
        }
    }

    private async Task RecalculateSessionTotalsAsync(DineInSession session)
    {
        decimal subtotal = 0;
        foreach (var orderId in session.OrderIds)
        {
            var order = await _orderRepo.GetOrderByIdAsync(orderId);
            if (order != null && order.Status != "cancelled")
            {
                subtotal += order.Subtotal;
            }
        }

        session.Subtotal = subtotal;
        var discount = session.DiscountAmount + session.LoyaltyDiscountAmount;
        var taxable = Math.Max(0, subtotal - discount);
        // GST & Taxes temporarily disabled (0%) per requirement
        session.TaxAmount = 0m;
        session.GrandTotal = taxable;
    }

    private async Task<DineInBillResponse> BuildBillResponseAsync(DineInSession session)
    {
        var rounds = new List<DineInRoundDto>();
        int totalItems = 0;

        for (int i = 0; i < session.OrderIds.Count; i++)
        {
            var orderId = session.OrderIds[i];
            var order = await _orderRepo.GetOrderByIdAsync(orderId);
            if (order == null) continue;

            var roundItems = order.Items.Select(item => new DineInRoundItemDto
            {
                MenuItemId = item.MenuItemId,
                Name = item.Name,
                Quantity = item.Quantity,
                UnitPrice = item.Price,
                TotalPrice = item.Total,
                SelectedVariantName = item.SelectedVariantName,
                SelectedAddOnNames = item.SelectedAddOns?.Select(a => a.Name).ToList(),
                PreparationNotes = order.PreparationNotes
            }).ToList();

            totalItems += order.Items.Sum(x => x.Quantity);

            rounds.Add(new DineInRoundDto
            {
                RoundNumber = order.RoundNumber > 0 ? order.RoundNumber : (i + 1),
                OrderId = order.Id!,
                Status = order.Status,
                CreatedAt = order.CreatedAt,
                RoundSubtotal = order.Subtotal,
                Items = roundItems
            });
        }

        var outlet = await _outletRepo.GetOutletByIdAsync(session.OutletId);
        var outletName = outlet?.OutletName ?? session.OutletName ?? "Maa Tara Cafe";

        var upiId = (_config["Upi:Id"] ?? _config["Upi__Id"] ?? Environment.GetEnvironmentVariable("Upi__Id") ?? string.Empty).Trim();
        var payeeName = (_config["Upi:PayeeName"] ?? _config["Upi__PayeeName"] ?? Environment.GetEnvironmentVariable("Upi__PayeeName") ?? "Maa Tara Cafe").Trim();

        string? upiQrString = null;
        if (!string.IsNullOrWhiteSpace(upiId) && session.GrandTotal > 0)
        {
            var note = $"Table {session.TableNumber} Bill";
            upiQrString = $"upi://pay?pa={upiId}&pn={Uri.EscapeDataString(payeeName)}&am={session.GrandTotal:F2}&cu=INR&tn={Uri.EscapeDataString(note)}";
        }

        var invoiceNum = session.Status == "paid"
            ? $"INV-T{session.TableNumber}-{session.CreatedAt:yyyyMMdd}-{session.Id?[^4..].ToUpperInvariant()}"
            : null;

        var pointsToEarn = (int)Math.Floor(session.GrandTotal * 0.10m);
        var amountDue = GetSessionAmountDue(session);

        return new DineInBillResponse
        {
            SessionId = session.Id!,
            OutletId = session.OutletId,
            OutletName = outletName,
            TableNumber = session.TableNumber,
            CustomerName = session.CustomerName,
            CustomerPhone = session.CustomerPhone,
            Status = session.Status,
            PaymentStatus = session.PaymentStatus,
            PaymentMethod = session.PaymentMethod,
            AmountPaid = Math.Round(session.AmountPaid, 2),
            AmountDue = amountDue,
            PaymentEntries = (session.PaymentEntries ?? new List<DineInPaymentEntry>())
                .OrderByDescending(p => p.RequestedAt)
                .ToList(),
            VoidRequests = (session.VoidRequests ?? new List<DineInVoidRequest>())
                .OrderByDescending(v => v.RequestedAt)
                .ToList(),
            Timeline = (session.Timeline ?? new List<DineInSessionEvent>())
                .OrderByDescending(t => t.CreatedAt)
                .ToList(),
            Rounds = rounds,
            TotalItemsCount = totalItems,
            Subtotal = session.Subtotal,
            DiscountAmount = session.DiscountAmount,
            CouponCode = session.CouponCode,
            LoyaltyPointsUsed = session.LoyaltyPointsUsed,
            LoyaltyDiscountAmount = session.LoyaltyDiscountAmount,
            TaxAmount = session.TaxAmount,
            GrandTotal = session.GrandTotal,
            CreatedAt = session.CreatedAt,
            BillRequestedAt = session.BillRequestedAt,
            SettledAt = session.SettledAt,
            CanAddItems = session.Status == "active",
            CanRequestBill = session.Status == "active" && totalItems > 0,
            UpiQrString = upiQrString,
            UpiId = upiId,
            PayeeName = payeeName,
            InvoiceNumber = invoiceNum,
            EstimatedPointsToEarn = pointsToEarn,
            CanApplyCoupon = session.Status == "active"
        };
    }

    private decimal GetSessionAmountDue(DineInSession session)
    {
        var due = Math.Round(session.GrandTotal - session.AmountPaid, 2);
        return due < 0 ? 0 : due;
    }

    private static void AddSessionEvent(
        DineInSession session,
        string eventType,
        string message,
        string? actor,
        Dictionary<string, string>? metadata = null)
    {
        session.Timeline ??= new List<DineInSessionEvent>();
        session.Timeline.Add(new DineInSessionEvent
        {
            EventType = eventType,
            Message = message,
            Actor = actor,
            CreatedAt = MongoService.GetIstNow(),
            Metadata = metadata
        });
    }

    private string ResolveActorLabel(HttpRequestData req, DineInSession session)
    {
        var authHeader = req.Headers.TryGetValues("Authorization", out var authValues)
            ? authValues.FirstOrDefault()
            : null;
        if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var principal = _auth.ValidateToken(authHeader["Bearer ".Length..].Trim());
            if (principal != null)
            {
                var role = principal.FindFirst(ClaimTypes.Role)?.Value;
                var user = principal.FindFirst(ClaimTypes.Name)?.Value ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!string.IsNullOrWhiteSpace(user))
                {
                    return string.IsNullOrWhiteSpace(role) ? user : $"{user} ({role})";
                }
            }
        }

        return session.CustomerName ?? "guest";
    }

    private async Task<string?> ResolveOutletIdAsync(HttpRequestData req, string? requestedId)
    {
        if (!string.IsNullOrWhiteSpace(requestedId)) return requestedId.Trim();
        var headerId = req.Headers.TryGetValues("X-Outlet-Id", out var vals) ? vals.FirstOrDefault() : null;
        if (!string.IsNullOrWhiteSpace(headerId)) return headerId.Trim();

        var outlets = await _outletRepo.GetAllOutletsAsync();
        return outlets.FirstOrDefault()?.Id;
    }

    private (string? userId, string? username, string? phone) GetOptionalUserInfo(HttpRequestData req)
    {
        var authHeader = req.Headers.TryGetValues("Authorization", out var vals) ? vals.FirstOrDefault() : null;
        if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer "))
        {
            var token = authHeader.Substring("Bearer ".Length).Trim();
            var principal = _auth.ValidateToken(token);
            if (principal != null)
            {
                var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var username = principal.FindFirst(ClaimTypes.Name)?.Value;
                var phone = principal.FindFirst(ClaimTypes.MobilePhone)?.Value;
                return (userId, username, phone);
            }
        }
        return (null, null, null);
    }

    private bool HasSessionAccess(HttpRequestData req, DineInSession session)
    {
        var authHeader = req.Headers.TryGetValues("Authorization", out var authValues)
            ? authValues.FirstOrDefault()
            : null;
        if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var principal = _auth.ValidateToken(authHeader["Bearer ".Length..].Trim());
            if (principal != null)
            {
                var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (RoleHelper.HasAnyRole(principal, RoleHelper.StaffRoles))
                    return true;
                if (!string.IsNullOrWhiteSpace(userId) && string.Equals(session.UserId, userId, StringComparison.Ordinal))
                    return true;
            }
        }

        if (HasTableQrAccess(req, session.QrAccessTokenHash)) return true;

        if (string.IsNullOrWhiteSpace(session.AccessTokenHash)) return false;
        var suppliedToken = req.Headers.TryGetValues("X-Dine-In-Session-Token", out var tokenValues)
            ? tokenValues.FirstOrDefault()?.Trim()
            : null;
        if (string.IsNullOrWhiteSpace(suppliedToken)) return false;

        try
        {
            var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedToken));
            var expectedHash = Convert.FromHexString(session.AccessTokenHash);
            return suppliedHash.Length == expectedHash.Length
                && CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private bool HasTableQrAccess(HttpRequestData req, string? expectedHash)
    {
        if (string.IsNullOrWhiteSpace(expectedHash)) return false;
        var suppliedToken = req.Headers.TryGetValues("X-Dine-In-Table-Token", out var tokenValues)
            ? tokenValues.FirstOrDefault()?.Trim()
            : null;
        if (string.IsNullOrWhiteSpace(suppliedToken)) return false;

        try
        {
            var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedToken));
            var expected = Convert.FromHexString(expectedHash);
            return suppliedHash.Length == expected.Length
                && CryptographicOperations.FixedTimeEquals(suppliedHash, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static async Task<HttpResponseData> WriteErrorAsync(
        HttpRequestData req, HttpStatusCode statusCode, string message)
    {
        var response = req.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(new { error = message });
        return response;
    }

    private static (string token, string hash) CreateSessionAccessToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        return (token, hash);
    }

    #endregion
}
