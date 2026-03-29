using System.Net;
using System.Text.Json;
using Cafe.Api.Helpers;
using Cafe.Api.Models;
using Cafe.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace Cafe.Api.Functions;

public class ExternalOrderClaimFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly BlobStorageService _blobService;
    private readonly NotificationService _notificationService;
    private readonly IEmailService _emailService;
    private readonly ILogger<ExternalOrderClaimFunction> _log;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ExternalOrderClaimFunction(
        MongoService mongo, AuthService auth, BlobStorageService blobService,
        NotificationService notificationService, IEmailService emailService,
        ILogger<ExternalOrderClaimFunction> log)
    {
        _mongo = mongo;
        _auth = auth;
        _blobService = blobService;
        _notificationService = notificationService;
        _emailService = emailService;
        _log = log;
    }

    // ─────────────────────────── CUSTOMER ENDPOINTS ───────────────────────────

    /// <summary>
    /// Customer uploads a Zomato/Swiggy invoice screenshot to claim loyalty points.
    /// Multipart form: 'file' (image) + 'platform' (zomato/swiggy) + 'totalAmount' (decimal).
    /// </summary>
    [Function("SubmitExternalClaim")]
    [OpenApiOperation(operationId: "SubmitExternalClaim", tags: new[] { "ExternalClaims" },
        Summary = "Submit external order invoice for loyalty points")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    public async Task<HttpResponseData> SubmitExternalClaim(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "loyalty/external-claim")] HttpRequestData req)
    {
        try
        {
            var (isAuth, userId, _, authError) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuth || string.IsNullOrEmpty(userId)) return authError!;

            // Parse multipart form data
            var contentType = req.Headers.GetValues("Content-Type").FirstOrDefault() ?? "";
            if (!contentType.Contains("multipart/form-data"))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "Content-Type must be multipart/form-data" });
                return bad;
            }

            var boundary = GetBoundary(contentType);
            if (string.IsNullOrEmpty(boundary))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "Invalid multipart boundary" });
                return bad;
            }

            using var bodyStream = new MemoryStream();
            await req.Body.CopyToAsync(bodyStream);
            bodyStream.Position = 0;

            var (fileData, fileName, fileContentType, formFields) = ExtractFileAndFields(bodyStream, boundary);

            if (fileData == null || fileData.Length == 0)
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "No invoice image provided" });
                return bad;
            }

            // Extract form fields
            formFields.TryGetValue("platform", out var platform);
            formFields.TryGetValue("totalAmount", out var totalAmountStr);
            formFields.TryGetValue("items", out var itemsJson);

            platform = platform?.Trim().ToLower() ?? "";
            if (platform != "zomato" && platform != "swiggy")
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "Platform must be 'zomato' or 'swiggy'" });
                return bad;
            }

            // Parse total amount (customer-entered, admin will verify against screenshot)
            if (!decimal.TryParse(totalAmountStr, out var totalAmount) || totalAmount <= 0)
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "Please provide a valid total amount from the invoice" });
                return bad;
            }

            if (totalAmount > 50000)
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "Total amount seems too high. Please verify." });
                return bad;
            }

            // Parse items if provided
            var extractedItems = new List<ExtractedInvoiceItem>();
            if (!string.IsNullOrWhiteSpace(itemsJson))
            {
                try
                {
                    extractedItems = JsonSerializer.Deserialize<List<ExtractedInvoiceItem>>(itemsJson, _jsonOptions) ?? new();
                }
                catch { /* Items are optional — admin verifies from screenshot */ }
            }

            // Upload invoice image to blob storage
            using var uploadStream = new MemoryStream(fileData);
            var invoiceImageUrl = await _blobService.UploadInvoiceImageAsync(
                uploadStream, fileName, fileContentType, userId);

            // Calculate loyalty points: 60% of total (subtracting 40%)
            var calculatedPoints = InvoiceParser.CalculatePoints(totalAmount);

            // Get user info
            var user = await _mongo.GetUserByIdAsync(userId);

            var claim = new ExternalOrderClaim
            {
                UserId = userId,
                Username = user?.Username ?? "Unknown",
                Platform = platform,
                InvoiceImageUrl = invoiceImageUrl,
                ExtractedItems = extractedItems,
                ExtractedTotal = totalAmount,
                CalculatedPoints = calculatedPoints,
                Status = "pending"
            };

            await _mongo.CreateExternalClaimAsync(claim);

            _log.LogInformation("External claim submitted by user {UserId}: {Platform}, ₹{Total}, {Points} points",
                userId, platform, totalAmount, calculatedPoints);

            // Notify admins
            _ = Task.Run(async () =>
            {
                try
                {
                    var adminIds = await _mongo.GetAdminUserIdsAsync();
                    await _notificationService.SendToManyAsync(
                        adminIds,
                        "loyalty_points",
                        "New Invoice Claim Pending 📋",
                        $"{user?.Username ?? "A customer"} submitted a {platform} invoice for ₹{totalAmount:N2} ({calculatedPoints} pts)",
                        new Dictionary<string, string>
                        {
                            { "claimId", claim.Id ?? "" },
                            { "platform", platform }
                        },
                        actionUrl: "/admin/loyalty"
                    );
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to notify admins about external claim");
                }
            });

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new
            {
                message = "Invoice submitted successfully! Your claim is pending admin review.",
                claimId = claim.Id,
                calculatedPoints,
                extractedTotal = totalAmount
            });
            return response;
        }
        catch (ArgumentException ex)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = ex.Message });
            return bad;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error submitting external claim");
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteAsJsonAsync(new { error = "Failed to submit claim" });
            return err;
        }
    }

    /// <summary>
    /// Get the authenticated user's external order claims history.
    /// </summary>
    [Function("GetMyExternalClaims")]
    [OpenApiOperation(operationId: "GetMyExternalClaims", tags: new[] { "ExternalClaims" },
        Summary = "Get my external order claims")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    public async Task<HttpResponseData> GetMyExternalClaims(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "loyalty/external-claims")] HttpRequestData req)
    {
        try
        {
            var (isAuth, userId, _, authError) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuth || string.IsNullOrEmpty(userId)) return authError!;

            var claims = await _mongo.GetUserExternalClaimsAsync(userId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(claims.Select(MapToResponse).ToList());
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting user external claims");
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteAsJsonAsync(new { error = "Failed to get claims" });
            return err;
        }
    }

    // ─────────────────────────── ADMIN ENDPOINTS ───────────────────────────

    /// <summary>
    /// Get all external order claims (admin). Supports filtering by status.
    /// </summary>
    [Function("AdminGetExternalClaims")]
    [OpenApiOperation(operationId: "AdminGetExternalClaims", tags: new[] { "ExternalClaims" },
        Summary = "Admin: Get all external claims")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    public async Task<HttpResponseData> AdminGetExternalClaims(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/loyalty/external-claims")] HttpRequestData req)
    {
        try
        {
            var (isAuth, _, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuth) return authError!;

            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var status = query["status"]; // null = all, "pending", "approved", "rejected"
            var page = int.TryParse(query["page"], out var p) && p > 0 ? p : 1;
            var pageSize = int.TryParse(query["pageSize"], out var ps) && ps > 0 && ps <= 50 ? ps : 20;

            var claims = await _mongo.GetAllExternalClaimsAsync(status, page, pageSize);
            var totalCount = await _mongo.CountExternalClaimsAsync(status);
            var pendingCount = await _mongo.CountExternalClaimsAsync("pending");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                claims = claims.Select(MapToResponse).ToList(),
                totalCount,
                pendingCount,
                page,
                pageSize
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting admin external claims");
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteAsJsonAsync(new { error = "Failed to get claims" });
            return err;
        }
    }

    /// <summary>
    /// Admin approves or rejects an external order claim.
    /// On approval, loyalty points are credited to the customer's account.
    /// </summary>
    [Function("ReviewExternalClaim")]
    [OpenApiOperation(operationId: "ReviewExternalClaim", tags: new[] { "ExternalClaims" },
        Summary = "Admin: Approve or reject external claim")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    public async Task<HttpResponseData> ReviewExternalClaim(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "manage/loyalty/external-claims/{claimId}/review")] HttpRequestData req,
        string claimId)
    {
        try
        {
            var (isAuth, adminUserId, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuth) return authError!;

            var body = await req.ReadAsStringAsync();
            var reviewReq = JsonSerializer.Deserialize<ReviewClaimRequest>(body ?? "", _jsonOptions);

            if (reviewReq == null || string.IsNullOrWhiteSpace(reviewReq.Action))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "Action is required (approve or reject)" });
                return bad;
            }

            var action = reviewReq.Action.ToLower();
            if (action != "approve" && action != "reject")
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "Action must be 'approve' or 'reject'" });
                return bad;
            }

            var claim = await _mongo.GetExternalClaimByIdAsync(claimId);
            if (claim == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Claim not found" });
                return notFound;
            }

            if (claim.Status != "pending")
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = $"Claim has already been {claim.Status}" });
                return conflict;
            }

            var newStatus = action == "approve" ? "approved" : "rejected";

            // Validate override points if provided
            if (action == "approve" && reviewReq.OverridePoints.HasValue)
            {
                if (reviewReq.OverridePoints.Value < 0 || reviewReq.OverridePoints.Value > 50000)
                {
                    var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                    await bad.WriteAsJsonAsync(new { error = "Override points must be between 0 and 50000" });
                    return bad;
                }
            }

            await _mongo.UpdateExternalClaimStatusAsync(
                claimId, newStatus, reviewReq.AdminNotes, adminUserId, reviewReq.OverridePoints);

            var pointsToAward = reviewReq.OverridePoints ?? claim.CalculatedPoints;

            // If approved, credit loyalty points
            if (action == "approve")
            {
                var description = $"External {claim.Platform} order claim (₹{claim.ExtractedTotal:N2})";
                var account = await _mongo.AwardPointsAsync(claim.UserId, pointsToAward, description);

                if (account == null)
                {
                    // Create loyalty account if it doesn't exist, then retry
                    await _mongo.GetOrCreateLoyaltyAccountAsync(claim.UserId, claim.Username);
                    account = await _mongo.AwardPointsAsync(claim.UserId, pointsToAward, description);
                }

                _log.LogInformation("Awarded {Points} loyalty points to user {UserId} for external claim {ClaimId}",
                    pointsToAward, claim.UserId, claimId);
            }

            // Notify customer (fire and forget)
            _ = Task.Run(async () =>
            {
                try
                {
                    if (action == "approve")
                    {
                        await _notificationService.SendLoyaltyPointsNotificationAsync(
                            claim.UserId, pointsToAward, 0,
                            $"{claim.Platform} invoice claim approved");

                        // Send email
                        var user = await _mongo.GetUserByIdAsync(claim.UserId);
                        if (user?.Email != null)
                        {
                            await _emailService.SendPromotionalEmailAsync(
                                user.Email,
                                user.FirstName ?? user.Username ?? "Customer",
                                "Your Invoice Claim Has Been Approved! ⭐",
                                $"Great news! Your {claim.Platform} invoice claim for ₹{claim.ExtractedTotal:N2} has been approved.<br><br>" +
                                $"<strong>{pointsToAward} loyalty points</strong> have been credited to your account.<br><br>" +
                                $"Keep ordering and earning rewards! 🎉"
                            );
                        }
                    }
                    else
                    {
                        await _notificationService.SendAsync(
                            claim.UserId, "loyalty_points",
                            "Invoice Claim Rejected ❌",
                            $"Your {claim.Platform} invoice claim for ₹{claim.ExtractedTotal:N2} was not approved." +
                            (string.IsNullOrEmpty(reviewReq.AdminNotes) ? "" : $" Reason: {reviewReq.AdminNotes}"),
                            actionUrl: "/loyalty"
                        );

                        // Send email
                        var user = await _mongo.GetUserByIdAsync(claim.UserId);
                        if (user?.Email != null)
                        {
                            await _emailService.SendPromotionalEmailAsync(
                                user.Email,
                                user.FirstName ?? user.Username ?? "Customer",
                                "Invoice Claim Update",
                                $"Your {claim.Platform} invoice claim for ₹{claim.ExtractedTotal:N2} was not approved.<br><br>" +
                                (string.IsNullOrEmpty(reviewReq.AdminNotes) ? "" : $"Reason: {reviewReq.AdminNotes}<br><br>") +
                                "If you believe this is an error, please submit a new claim with a clearer screenshot."
                            );
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to send notification/email for claim {ClaimId}", claimId);
                }
            });

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = action == "approve"
                    ? $"Claim approved! {pointsToAward} points credited to {claim.Username}."
                    : "Claim rejected.",
                claimId,
                status = newStatus,
                pointsAwarded = action == "approve" ? pointsToAward : 0
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error reviewing external claim {ClaimId}", claimId);
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteAsJsonAsync(new { error = "Failed to review claim" });
            return err;
        }
    }

    // ─────────────────────────── HELPERS ───────────────────────────

    private static ExternalOrderClaimResponse MapToResponse(ExternalOrderClaim c) => new()
    {
        Id = c.Id ?? "",
        UserId = c.UserId,
        Username = c.Username,
        Platform = c.Platform,
        InvoiceImageUrl = c.InvoiceImageUrl,
        ExtractedItems = c.ExtractedItems,
        ExtractedTotal = c.ExtractedTotal,
        CalculatedPoints = c.CalculatedPoints,
        Status = c.Status,
        AdminNotes = c.AdminNotes,
        ReviewedBy = c.ReviewedBy,
        ReviewedAt = c.ReviewedAt,
        CreatedAt = c.CreatedAt
    };

    private static string? GetBoundary(string contentType)
    {
        var parts = contentType.Split("boundary=");
        if (parts.Length <= 1) return null;
        var boundary = parts[1].Trim();
        if (boundary.StartsWith('"') && boundary.EndsWith('"'))
            boundary = boundary[1..^1];
        var semiIdx = boundary.IndexOf(';');
        if (semiIdx >= 0) boundary = boundary[..semiIdx].Trim();
        return boundary;
    }

    private static (byte[]? Data, string FileName, string ContentType, Dictionary<string, string> Fields) ExtractFileAndFields(Stream stream, string boundary)
    {
        byte[]? fileData = null;
        var fileName = "invoice.jpg";
        var fileContentType = "image/jpeg";
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var rawBytes = new byte[stream.Length];
        stream.ReadExactly(rawBytes, 0, rawBytes.Length);

        var boundaryBytes = System.Text.Encoding.UTF8.GetBytes("--" + boundary);
        var parts = SplitByBoundary(rawBytes, boundaryBytes);

        foreach (var part in parts)
        {
            var partString = System.Text.Encoding.UTF8.GetString(part);

            // Extract field name
            var nameMatch = System.Text.RegularExpressions.Regex.Match(partString,
                @"name=""([^""]+)""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!nameMatch.Success) continue;
            var fieldName = nameMatch.Groups[1].Value;

            if (partString.Contains("filename=", StringComparison.OrdinalIgnoreCase))
            {
                // File field
                var fnStart = partString.IndexOf("filename=\"", StringComparison.OrdinalIgnoreCase);
                if (fnStart >= 0)
                {
                    fnStart += 10;
                    var fnEnd = partString.IndexOf("\"", fnStart);
                    if (fnEnd > fnStart) fileName = partString[fnStart..fnEnd];
                }

                var ctStart = partString.IndexOf("Content-Type:", StringComparison.OrdinalIgnoreCase);
                if (ctStart >= 0)
                {
                    ctStart += 13;
                    var ctEnd = partString.IndexOf("\r\n", ctStart);
                    if (ctEnd > ctStart) fileContentType = partString[ctStart..ctEnd].Trim();
                }

                var headerEnd = FindDoubleCrlf(part);
                if (headerEnd < 0) continue;

                var bodyStart = headerEnd + 4;
                var bodyLength = part.Length - bodyStart;
                if (bodyLength >= 2 && part[bodyStart + bodyLength - 2] == '\r' && part[bodyStart + bodyLength - 1] == '\n')
                    bodyLength -= 2;

                fileData = new byte[bodyLength];
                Array.Copy(part, bodyStart, fileData, 0, bodyLength);
            }
            else
            {
                // Text field
                var headerEnd = FindDoubleCrlf(part);
                if (headerEnd < 0) continue;

                var bodyStart = headerEnd + 4;
                var bodyLength = part.Length - bodyStart;
                if (bodyLength >= 2 && part[bodyStart + bodyLength - 2] == '\r' && part[bodyStart + bodyLength - 1] == '\n')
                    bodyLength -= 2;

                var value = System.Text.Encoding.UTF8.GetString(part, bodyStart, bodyLength).Trim();
                fields[fieldName] = value;
            }
        }

        return (fileData, fileName, fileContentType, fields);
    }

    private static int FindDoubleCrlf(byte[] data)
    {
        for (int i = 0; i < data.Length - 3; i++)
        {
            if (data[i] == '\r' && data[i + 1] == '\n' && data[i + 2] == '\r' && data[i + 3] == '\n')
                return i;
        }
        return -1;
    }

    private static List<byte[]> SplitByBoundary(byte[] data, byte[] boundary)
    {
        var parts = new List<byte[]>();
        var positions = new List<int>();

        for (int i = 0; i <= data.Length - boundary.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < boundary.Length; j++)
            {
                if (data[i + j] != boundary[j]) { match = false; break; }
            }
            if (match) positions.Add(i);
        }

        for (int i = 0; i < positions.Count - 1; i++)
        {
            var start = positions[i] + boundary.Length;
            // Skip CRLF after boundary
            if (start + 1 < data.Length && data[start] == '\r' && data[start + 1] == '\n')
                start += 2;

            var end = positions[i + 1];
            var length = end - start;
            if (length > 0)
            {
                var part = new byte[length];
                Array.Copy(data, start, part, 0, length);
                parts.Add(part);
            }
        }

        return parts;
    }
}
