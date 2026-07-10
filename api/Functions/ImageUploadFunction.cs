using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Helpers;
using System.Net;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;

namespace Cafe.Api.Functions;

public class ImageUploadFunction
{
    private const long MaxMultipartBodyBytes = 6 * 1024 * 1024; // 6MB hard cap at request parser level

    private readonly BlobStorageService _blobService;
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public ImageUploadFunction(BlobStorageService blobService, MongoService mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _blobService = blobService;
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<ImageUploadFunction>();
    }

    /// <summary>
    /// Uploads an image for a menu item and updates the menu item's ImageUrl.
    /// Accepts multipart/form-data with a single 'file' field.
    /// </summary>
    [Function("UploadMenuItemImage")]
    [OpenApiOperation(operationId: "UploadMenuItemImage", tags: new[] { "Images" }, Summary = "Upload menu item image")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Image uploaded and menu item updated")]
    public async Task<HttpResponseData> UploadMenuItemImage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "menu/{menuItemId}/image")] HttpRequestData req,
        string menuItemId)
    {
        try
        {
            // Admin only
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            // Validate menu item exists
            var menuItem = await _mongo.GetMenuItemAsync(menuItemId);
            if (menuItem == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Menu item not found" });
                return notFound;
            }

            // Parse multipart form data
            var contentType = req.Headers.GetValues("Content-Type").FirstOrDefault() ?? "";
            if (!contentType.Contains("multipart/form-data"))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Content-Type must be multipart/form-data" });
                return badRequest;
            }

            var boundary = GetBoundary(contentType);
            if (string.IsNullOrEmpty(boundary))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid multipart boundary" });
                return badRequest;
            }

            using var bodyStream = await ReadRequestBodyWithLimitAsync(req.Body, MaxMultipartBodyBytes);

            var (fileData, fileName, fileContentType) = ExtractFileFromMultipart(bodyStream, boundary);
            if (fileData == null || fileData.Length == 0)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "No image file provided" });
                return badRequest;
            }

            // Delete old image if exists
            if (!string.IsNullOrEmpty(menuItem.ImageUrl))
            {
                await _blobService.DeleteMenuImageAsync(menuItem.ImageUrl);
            }

            // Upload new image
            using var uploadStream = new MemoryStream(fileData);
            var (imageUrl, thumbnailUrl) = await _blobService.UploadMenuImageAsync(
                uploadStream,
                fileName,
                fileContentType,
                menuItem.OutletId
            );

            // Update menu item with new image URL
            menuItem.ImageUrl = imageUrl;
            menuItem.ImageThumbnailUrl = thumbnailUrl;
            await _mongo.UpdateMenuItemAsync(menuItemId, menuItem);

            _log.LogInformation("Menu item {MenuItemId} image updated: {ImageUrl}", menuItemId, imageUrl);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { imageUrl, thumbnailUrl, message = "Image uploaded successfully" });
            return response;
        }
        catch (ArgumentException)
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return badRequest;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error uploading image for menu item {MenuItemId}", menuItemId);
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to upload image" });
            return error;
        }
    }

    private static async Task<MemoryStream> ReadRequestBodyWithLimitAsync(Stream input, long maxBytes)
    {
        var destination = new MemoryStream();
        var buffer = new byte[81920];
        long totalRead = 0;

        while (true)
        {
            var bytesRead = await input.ReadAsync(buffer, 0, buffer.Length);
            if (bytesRead <= 0)
                break;

            totalRead += bytesRead;
            if (totalRead > maxBytes)
                throw new ArgumentException($"Request payload exceeds {maxBytes / (1024 * 1024)}MB limit");

            await destination.WriteAsync(buffer, 0, bytesRead);
        }

        destination.Position = 0;
        return destination;
    }
    /// <summary>
    /// Deletes the image for a menu item.
    /// </summary>
    [Function("DeleteMenuItemImage")]
    [OpenApiOperation(operationId: "DeleteMenuItemImage", tags: new[] { "Images" }, Summary = "Delete menu item image")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    public async Task<HttpResponseData> DeleteMenuItemImage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "menu/{menuItemId}/image")] HttpRequestData req,
        string menuItemId)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var menuItem = await _mongo.GetMenuItemAsync(menuItemId);
            if (menuItem == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Menu item not found" });
                return notFound;
            }

            if (!string.IsNullOrEmpty(menuItem.ImageUrl))
            {
                await _blobService.DeleteMenuImageAsync(menuItem.ImageUrl);
                menuItem.ImageUrl = string.Empty;
                menuItem.ImageThumbnailUrl = string.Empty;
                await _mongo.UpdateMenuItemAsync(menuItemId, menuItem);
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Image deleted successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error deleting image for menu item {MenuItemId}", menuItemId);
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to delete image" });
            return error;
        }
    }

    /// <summary>
    /// Uploads a profile picture for the authenticated user.
    /// </summary>
    [Function("UploadProfilePicture")]
    [OpenApiOperation(operationId: "UploadProfilePicture", tags: new[] { "Images" }, Summary = "Upload profile picture")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Profile picture uploaded")]
    public async Task<HttpResponseData> UploadProfilePicture(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/profile/picture")] HttpRequestData req)
    {
        try
        {
            // Any authenticated user can upload their own profile picture
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized) return errorResponse!;

            // Parse multipart form data
            var contentType = req.Headers.GetValues("Content-Type").FirstOrDefault() ?? "";
            if (!contentType.Contains("multipart/form-data"))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Content-Type must be multipart/form-data" });
                return badRequest;
            }

            var boundary = GetBoundary(contentType);
            if (string.IsNullOrEmpty(boundary))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid multipart boundary" });
                return badRequest;
            }

            using var bodyStream = await ReadRequestBodyWithLimitAsync(req.Body, MaxMultipartBodyBytes);

            var (fileData, fileName, fileContentType) = ExtractFileFromMultipart(bodyStream, boundary);
            if (fileData == null || fileData.Length == 0)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "No image file provided" });
                return badRequest;
            }

            // Delete old profile picture if exists
            var user = await _mongo.GetUserByIdAsync(userId!);
            if (user != null && !string.IsNullOrEmpty(user.ProfilePictureUrl))
            {
                await _blobService.DeleteProfilePictureAsync(user.ProfilePictureUrl);
            }

            // Upload new profile picture
            using var uploadStream = new MemoryStream(fileData);
            var imageUrl = await _blobService.UploadProfilePictureAsync(
                uploadStream,
                fileName,
                fileContentType,
                userId!
            );

            // Update user's profile picture URL
            await _mongo.UpdateProfilePictureUrlAsync(userId!, imageUrl);

            _log.LogInformation("Profile picture updated for user {UserId}: {ImageUrl}", userId, imageUrl);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { profilePictureUrl = imageUrl, message = "Profile picture uploaded successfully" });
            return response;
        }
        catch (ArgumentException)
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return badRequest;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error uploading profile picture");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to upload profile picture" });
            return error;
        }
    }

    /// <summary>
    /// Deletes the authenticated user's profile picture.
    /// </summary>
    [Function("DeleteProfilePicture")]
    [OpenApiOperation(operationId: "DeleteProfilePicture", tags: new[] { "Images" }, Summary = "Delete profile picture")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    public async Task<HttpResponseData> DeleteProfilePicture(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "auth/profile/picture")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var user = await _mongo.GetUserByIdAsync(userId!);
            if (user != null && !string.IsNullOrEmpty(user.ProfilePictureUrl))
            {
                await _blobService.DeleteProfilePictureAsync(user.ProfilePictureUrl);
                await _mongo.UpdateProfilePictureUrlAsync(userId!, null);
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Profile picture deleted successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error deleting profile picture");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to delete profile picture" });
            return error;
        }
    }

    /// <summary>
    /// Uploads a receipt/invoice image for an order. Owner or admin can upload.
    /// </summary>
    [Function("UploadOrderReceipt")]
    [OpenApiOperation(operationId: "UploadOrderReceipt", tags: new[] { "Images" }, Summary = "Upload order receipt image")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Receipt image uploaded")]
    public async Task<HttpResponseData> UploadOrderReceipt(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "orders/{orderId}/receipt")] HttpRequestData req,
        string orderId)
    {
        try
        {
            var (isAuthorized, userId, role, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized) return errorResponse!;

            // Validate order exists
            var order = await _mongo.GetOrderByIdAsync(orderId);
            if (order == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            // Only order owner or admin can upload receipt
            if (order.UserId != userId && role != "admin")
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "You can only upload receipts for your own orders" });
                return forbidden;
            }

            // Parse multipart form data
            var contentType = req.Headers.GetValues("Content-Type").FirstOrDefault() ?? "";
            if (!contentType.Contains("multipart/form-data"))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Content-Type must be multipart/form-data" });
                return badRequest;
            }

            var boundary = GetBoundary(contentType);
            if (string.IsNullOrEmpty(boundary))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid multipart boundary" });
                return badRequest;
            }

            using var bodyStream = await ReadRequestBodyWithLimitAsync(req.Body, MaxMultipartBodyBytes);

            var (fileData, fileName, fileContentType) = ExtractFileFromMultipart(bodyStream, boundary);
            if (fileData == null || fileData.Length == 0)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "No image file provided" });
                return badRequest;
            }

            // Delete old receipt image if exists
            if (!string.IsNullOrEmpty(order.ReceiptImageUrl))
            {
                await _blobService.DeleteReceiptImageAsync(order.ReceiptImageUrl);
            }

            // Upload new receipt image (with compression)
            using var uploadStream = new MemoryStream(fileData);
            var imageUrl = await _blobService.UploadReceiptImageAsync(
                uploadStream,
                fileName,
                fileContentType,
                userId!
            );

            // Update order with receipt image URL
            await _mongo.UpdateReceiptImageUrlAsync(orderId, imageUrl);

            _log.LogInformation("Receipt image uploaded for order {OrderId}: {ImageUrl}", orderId, imageUrl);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { receiptImageUrl = imageUrl, message = "Receipt uploaded successfully" });
            return response;
        }
        catch (ArgumentException)
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return badRequest;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error uploading receipt for order {OrderId}", orderId);
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to upload receipt" });
            return error;
        }
    }

    /// <summary>
    /// Submits UPI proof for a UPI QR order.
    /// Accepts optional UTR in query/body and optional screenshot via multipart/form-data.
    /// </summary>
    [Function("SubmitUpiPaymentProof")]
    [OpenApiOperation(operationId: "SubmitUpiPaymentProof", tags: new[] { "Payments" }, Summary = "Submit UPI payment proof")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    public async Task<HttpResponseData> SubmitUpiPaymentProof(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "orders/{orderId}/upi-proof")] HttpRequestData req,
        string orderId)
    {
        try
        {
            var (isAuthorized, userId, role, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var order = await _mongo.GetOrderByIdAsync(orderId);
            if (order == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            if (order.UserId != userId && role != "admin")
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "You can only submit proof for your own orders" });
                return forbidden;
            }

            var paymentMethod = (order.PaymentMethod ?? string.Empty).Trim().ToLowerInvariant();
            if (paymentMethod != "upi-qr" && paymentMethod != "upi")
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "UPI proof can only be submitted for UPI QR orders" });
                return badReq;
            }

            var requestedUpiRef = req.Query["upiReference"] ?? req.Query["utr"];
            byte[]? fileData = null;
            var fileName = string.Empty;
            var fileContentType = string.Empty;

            var contentType = req.Headers.GetValues("Content-Type").FirstOrDefault() ?? string.Empty;
            if (contentType.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            {
                var boundary = GetBoundary(contentType);
                if (string.IsNullOrWhiteSpace(boundary))
                {
                    var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badReq.WriteAsJsonAsync(new { error = "Invalid multipart boundary" });
                    return badReq;
                }

                using var bodyStream = await ReadRequestBodyWithLimitAsync(req.Body, MaxMultipartBodyBytes);

                var extracted = ExtractFileFromMultipart(bodyStream, boundary);
                fileData = extracted.Data;
                fileName = extracted.FileName;
                fileContentType = extracted.ContentType;

                var refFromBody = ExtractFieldFromMultipart(bodyStream, boundary, "upiReference")
                    ?? ExtractFieldFromMultipart(bodyStream, boundary, "utr");
                if (string.IsNullOrWhiteSpace(requestedUpiRef))
                {
                    requestedUpiRef = refFromBody;
                }
            }
            else if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                var (body, validationError) = await ValidationHelper.ValidateBody<SubmitUpiProofRequest>(req);
                if (validationError != null) return validationError;
                if (string.IsNullOrWhiteSpace(requestedUpiRef))
                {
                    requestedUpiRef = body.UpiReference;
                }
            }

            var normalizedRef = NormalizeUpiReference(requestedUpiRef);
            if (normalizedRef != null && !IsValidUpiReference(normalizedRef))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "UPI reference must be 6-100 characters and contain only letters, numbers, '-', '_' or '/'" });
                return badReq;
            }

            if ((fileData == null || fileData.Length == 0) && string.IsNullOrWhiteSpace(normalizedRef))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Provide at least one proof item: UPI reference or screenshot" });
                return badReq;
            }

            if (fileData != null && fileData.Length > 0)
            {
                if (!string.IsNullOrWhiteSpace(order.UpiProofUrl))
                {
                    await _blobService.DeleteReceiptImageAsync(order.UpiProofUrl);
                }

                using var uploadStream = new MemoryStream(fileData);
                var proofUrl = await _blobService.UploadReceiptImageAsync(
                    uploadStream,
                    string.IsNullOrWhiteSpace(fileName) ? "upi-proof.jpg" : fileName,
                    string.IsNullOrWhiteSpace(fileContentType) ? "image/jpeg" : fileContentType,
                    userId ?? order.UserId
                );

                order.UpiProofUrl = proofUrl;
            }

            if (!string.IsNullOrWhiteSpace(normalizedRef))
            {
                order.UpiReference = normalizedRef;
            }

            order.UpdatedAt = MongoService.GetIstNow();
            var saved = await _mongo.UpdateOrderAsync(order);
            if (!saved)
            {
                var error = req.CreateResponse(HttpStatusCode.InternalServerError);
                await error.WriteAsJsonAsync(new { error = "Failed to save UPI proof" });
                return error;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = "UPI proof submitted successfully",
                paymentStatus = order.PaymentStatus,
                upiReference = order.UpiReference,
                upiProofUrl = order.UpiProofUrl,
                proofValidated = !string.IsNullOrWhiteSpace(order.UpiReference) || !string.IsNullOrWhiteSpace(order.UpiProofUrl)
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error submitting UPI proof for order {OrderId}", orderId);
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to submit UPI proof" });
            return error;
        }
    }

    /// <summary>
    /// Deletes the receipt image for an order. Owner or admin can delete.
    /// </summary>
    [Function("DeleteOrderReceipt")]
    [OpenApiOperation(operationId: "DeleteOrderReceipt", tags: new[] { "Images" }, Summary = "Delete order receipt image")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    public async Task<HttpResponseData> DeleteOrderReceipt(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "orders/{orderId}/receipt")] HttpRequestData req,
        string orderId)
    {
        try
        {
            var (isAuthorized, userId, role, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var order = await _mongo.GetOrderByIdAsync(orderId);
            if (order == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            if (order.UserId != userId && role != "admin")
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "You can only delete receipts for your own orders" });
                return forbidden;
            }

            if (!string.IsNullOrEmpty(order.ReceiptImageUrl))
            {
                await _blobService.DeleteReceiptImageAsync(order.ReceiptImageUrl);
                await _mongo.UpdateReceiptImageUrlAsync(orderId, null);
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Receipt deleted successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error deleting receipt for order {OrderId}", orderId);
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to delete receipt" });
            return error;
        }
    }

    private static string? GetBoundary(string contentType)
    {
        var parts = contentType.Split("boundary=");
        if (parts.Length <= 1) return null;
        var boundary = parts[1].Trim();
        // Strip quotes if present (some clients wrap boundary in quotes)
        if (boundary.StartsWith('"') && boundary.EndsWith('"'))
            boundary = boundary[1..^1];
        // Remove any trailing parameters after semicolon
        var semiIdx = boundary.IndexOf(';');
        if (semiIdx >= 0) boundary = boundary[..semiIdx].Trim();
        return boundary;
    }

    private static (byte[]? Data, string FileName, string ContentType) ExtractFileFromMultipart(Stream stream, string boundary)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        var content = reader.ReadToEnd();
        stream.Position = 0;

        var rawBytes = new byte[stream.Length];
        stream.ReadExactly(rawBytes, 0, rawBytes.Length);

        var boundaryBytes = System.Text.Encoding.UTF8.GetBytes("--" + boundary);
        var parts = SplitByBoundary(rawBytes, boundaryBytes);

        foreach (var part in parts)
        {
            var partString = System.Text.Encoding.UTF8.GetString(part);

            // Look for Content-Disposition with filename
            if (!partString.Contains("filename=", StringComparison.OrdinalIgnoreCase))
                continue;

            // Extract filename
            var fileName = "image.jpg";
            var fnStart = partString.IndexOf("filename=\"", StringComparison.OrdinalIgnoreCase);
            if (fnStart >= 0)
            {
                fnStart += 10;
                var fnEnd = partString.IndexOf("\"", fnStart);
                if (fnEnd > fnStart)
                    fileName = partString[fnStart..fnEnd];
            }

            // Extract content type
            var fileContentType = "image/jpeg";
            var ctStart = partString.IndexOf("Content-Type:", StringComparison.OrdinalIgnoreCase);
            if (ctStart >= 0)
            {
                ctStart += 13;
                var ctEnd = partString.IndexOf("\r\n", ctStart);
                if (ctEnd > ctStart)
                    fileContentType = partString[ctStart..ctEnd].Trim();
            }

            // Find the double CRLF that separates headers from body
            var headerEnd = FindDoubleCrlf(part);
            if (headerEnd < 0) continue;

            var bodyStart = headerEnd + 4; // Skip \r\n\r\n
            var bodyLength = part.Length - bodyStart;

            // Trim trailing \r\n before next boundary
            if (bodyLength >= 2 && part[bodyStart + bodyLength - 2] == '\r' && part[bodyStart + bodyLength - 1] == '\n')
                bodyLength -= 2;

            var fileData = new byte[bodyLength];
            Array.Copy(part, bodyStart, fileData, 0, bodyLength);

            return (fileData, fileName, fileContentType);
        }

        return (null, string.Empty, string.Empty);
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

    private static string? ExtractFieldFromMultipart(Stream stream, string boundary, string fieldName)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        var content = reader.ReadToEnd();
        stream.Position = 0;

        var rawBytes = new byte[stream.Length];
        stream.ReadExactly(rawBytes, 0, rawBytes.Length);
        stream.Position = 0;

        var boundaryBytes = System.Text.Encoding.UTF8.GetBytes("--" + boundary);
        var parts = SplitByBoundary(rawBytes, boundaryBytes);

        foreach (var part in parts)
        {
            var partString = System.Text.Encoding.UTF8.GetString(part);
            if (partString.Contains("filename=", StringComparison.OrdinalIgnoreCase))
                continue;

            var nameToken = $"name=\"{fieldName}\"";
            if (!partString.Contains(nameToken, StringComparison.OrdinalIgnoreCase))
                continue;

            var headerEnd = FindDoubleCrlf(part);
            if (headerEnd < 0) continue;

            var bodyStart = headerEnd + 4;
            var bodyLength = part.Length - bodyStart;
            if (bodyLength >= 2 && part[bodyStart + bodyLength - 2] == '\r' && part[bodyStart + bodyLength - 1] == '\n')
                bodyLength -= 2;

            if (bodyLength <= 0) return null;

            var value = System.Text.Encoding.UTF8.GetString(part, bodyStart, bodyLength).Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }

    private static string? NormalizeUpiReference(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return raw.Trim();
    }

    private static bool IsValidUpiReference(string reference)
    {
        if (reference.Length < 6 || reference.Length > 100) return false;
        foreach (var c in reference)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '/')
            {
                continue;
            }

            return false;
        }

        return true;
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
            // Skip the \r\n after boundary
            if (start < data.Length - 1 && data[start] == '\r' && data[start + 1] == '\n')
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

public class SubmitUpiProofRequest
{
    public string? UpiReference { get; set; }
}
