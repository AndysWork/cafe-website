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

            using var bodyStream = new MemoryStream();
            await req.Body.CopyToAsync(bodyStream);
            bodyStream.Position = 0;

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
            var imageUrl = await _blobService.UploadMenuImageAsync(
                uploadStream,
                fileName,
                fileContentType,
                menuItem.OutletId
            );

            // Update menu item with new image URL
            menuItem.ImageUrl = imageUrl;
            await _mongo.UpdateMenuItemAsync(menuItemId, menuItem);

            _log.LogInformation("Menu item {MenuItemId} image updated: {ImageUrl}", menuItemId, imageUrl);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { imageUrl, message = "Image uploaded successfully" });
            return response;
        }
        catch (ArgumentException ex)
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new { error = ex.Message });
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

    private static string? GetBoundary(string contentType)
    {
        var parts = contentType.Split("boundary=");
        return parts.Length > 1 ? parts[1].Trim() : null;
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
