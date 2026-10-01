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
    private readonly IUserRepository _userRepo;
    private readonly IOfferRepository _offerRepo;
    private readonly ILoyaltyRepository _loyaltyRepo;
    private readonly IOperationsRepository _operationsRepo;
    private readonly AuthService _auth;
    private readonly OutboxService _outbox;
    private readonly IConfiguration _config;
    private readonly ILogger _log;

    public DineInFunction(
        IOrderRepository orderRepo,
        IMenuRepository menuRepo,
        IOutletRepository outletRepo,
        IUserRepository userRepo,
        IOfferRepository offerRepo,
        ILoyaltyRepository loyaltyRepo,
        IOperationsRepository operationsRepo,
        AuthService auth,
        OutboxService outbox,
        IConfiguration config,
        ILoggerFactory loggerFactory)
    {
        _orderRepo = orderRepo;
        _menuRepo = menuRepo;
        _outletRepo = outletRepo;
        _userRepo = userRepo;
        _offerRepo = offerRepo;
        _loyaltyRepo = loyaltyRepo;
        _operationsRepo = operationsRepo;
        _auth = auth;
        _outbox = outbox;
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
                Status = "active",
                PaymentStatus = "unpaid",
                CreatedAt = MongoService.GetIstNow(),
                UpdatedAt = MongoService.GetIstNow()
            };

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
                    Status = "active",
                    PaymentStatus = "unpaid",
                    CreatedAt = MongoService.GetIstNow(),
                    UpdatedAt = MongoService.GetIstNow()
                };
                session = await _orderRepo.CreateDineInSessionAsync(session);
            }
            else
            {
                if (!HasSessionAccess(req, session))
                {
                    var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                    await forbidden.WriteAsJsonAsync(new { error = "A valid dine-in session token is required" });
                    return forbidden;
                }

                if (session.Status == "bill_requested")
                {
                    session.Status = "active";
                    session.BillRequestedAt = null;
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
                // Start at "pending" so kitchen flow shows: Order Received -> Confirmed -> Preparing -> Prepared -> Served.
                Status = "pending",
                PaymentStatus = "unpaid",
                PaymentMethod = "dine_in_tab",
                OrderType = "dine-in",
                Channel = "shop",
                TableNumber = tableNumber,
                DineInSessionId = session.Id,
                RoundNumber = nextRoundNum,
                PhoneNumber = customerPhone,
                PreparationNotes = orderRequest.PreparationNotes?.Trim(),
                Notes = orderRequest.Notes?.Trim(),
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
            await _orderRepo.UpdateDineInSessionAsync(session);

            // Notify kitchen of new round
            await _outbox.EnqueueAsync("OrderNotificationKitchen", "Order", order.Id!,
                new { OrderId = order.Id!, Total = order.Total, TableNumber = tableNumber, RoundNumber = nextRoundNum });

            _log.LogInformation("Dine-In Round #{Round} for Table {Table} placed. Order {OrderId}", nextRoundNum, tableNumber, order.Id);

            var updatedBill = await BuildBillResponseAsync(session);
            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new
            {
                message = $"Round #{nextRoundNum} sent to Kitchen!",
                orderId = order.Id,
                roundNumber = nextRoundNum,
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

            if (session.PaymentStatus == "pending_verification" || session.PaymentStatus == "pending_cash")
            {
                var pending = req.CreateResponse(HttpStatusCode.OK);
                await pending.WriteAsJsonAsync(new
                {
                    message = "Payment is already awaiting staff confirmation.",
                    isSettled = false,
                    session = await BuildBillResponseAsync(session)
                });
                return pending;
            }

            if (session.PaymentStatus == "processing")
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = "Payment confirmation is currently in progress" });
                return conflict;
            }

            if (session.Status != "bill_requested" || session.PaymentStatus != "unpaid")
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Request the final bill before submitting payment" });
                return badReq;
            }

            var (request, valError) = await ValidationHelper.ValidateBody<SettleDineInBillRequest>(req);
            if (valError != null) return valError;

            var paymentMethod = request.PaymentMethod.ToLowerInvariant();

            if (paymentMethod == "upi-qr")
            {
                session.UpiReference = request.UpiReference?.Trim();
            }

            session.PaymentMethod = paymentMethod;
            session.PaymentStatus = paymentMethod == "cash_at_counter" ? "pending_cash" : "pending_verification";
            session.Status = "bill_requested";
            session.SettledAt = null;
            session.Notes = request.Notes;

            await _orderRepo.UpdateDineInSessionAsync(session);

            var bill = await BuildBillResponseAsync(session);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = paymentMethod == "cash_at_counter"
                    ? "Cash settlement requested. Please pay at counter."
                    : "UPI payment submitted for staff verification.",
                isSettled = false,
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

    [Function("ConfirmDineInPayment")]
    public async Task<HttpResponseData> ConfirmDineInPayment(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "dine-in/session/{sessionId}/payment/confirm")] HttpRequestData req,
        string sessionId)
    {
        string? claimedPaymentStatus = null;
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
                var ok = req.CreateResponse(HttpStatusCode.OK);
                await ok.WriteAsJsonAsync(new { message = "Payment already confirmed", session = await BuildBillResponseAsync(session) });
                return ok;
            }

            if (session.PaymentStatus != "pending_verification" && session.PaymentStatus != "pending_cash")
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "This session has no payment awaiting confirmation" });
                return badRequest;
            }

            claimedPaymentStatus = session.PaymentStatus;
            if (!await _orderRepo.TransitionDineInPaymentStatusAsync(
                    sessionId,
                    new[] { "pending_verification", "pending_cash" },
                    "processing"))
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = "This payment is already being confirmed by another staff member" });
                return conflict;
            }
            session.PaymentStatus = "processing";

            session.Notes = string.IsNullOrWhiteSpace(session.Notes)
                ? $"Payment confirmed by {userId}"
                : $"{session.Notes} | Payment confirmed by {userId}";
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

    #region Helpers

    private async Task<bool> FinalizeSessionPaymentAsync(DineInSession session)
    {
        Offer? appliedOffer = null;
        if (!string.IsNullOrWhiteSpace(session.CouponCode))
        {
            appliedOffer = await _offerRepo.GetOfferByCodeAsync(session.CouponCode);
            if (appliedOffer?.Id == null || !await _offerRepo.IncrementOfferUsageAsync(appliedOffer.Id))
                return false;
        }

        try
        {
        session.PaymentStatus = "paid";
        session.Status = "paid";
        session.SettledAt = MongoService.GetIstNow();
        session.UpdatedAt = MongoService.GetIstNow();
        await _orderRepo.UpdateDineInSessionAsync(session);

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
                var role = principal.FindFirst(ClaimTypes.Role)?.Value?.Trim().ToLowerInvariant();
                if (role is "admin" or "manager" or "assistant-manager" or "cashier" or "staff" or "cook" or "chef" or "sous-chef" or "kitchen" or "kitchen-staff")
                    return true;
                if (!string.IsNullOrWhiteSpace(userId) && string.Equals(session.UserId, userId, StringComparison.Ordinal))
                    return true;
            }
        }

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

    private static (string token, string hash) CreateSessionAccessToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        return (token, hash);
    }

    #endregion
}
