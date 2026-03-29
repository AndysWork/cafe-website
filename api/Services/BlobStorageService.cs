using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Cafe.Api.Helpers;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Services;

public class BlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<BlobStorageService> _logger;
    private readonly string _cdnBaseUrl;
    private const string MenuImagesContainer = "menu-images";
    private const string ProfilePicturesContainer = "profile-pictures";
    private const string InvoiceUploadsContainer = "invoice-uploads";
    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5MB for images

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif"
    };

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif"
    };

    public BlobStorageService(BlobServiceClient blobServiceClient, ILogger<BlobStorageService> logger)
    {
        _blobServiceClient = blobServiceClient;
        _logger = logger;

        // CDN base URL — falls back to blob storage direct URL if CDN not configured
        _cdnBaseUrl = Environment.GetEnvironmentVariable("Blob__CdnBaseUrl") ?? "";
    }

    /// <summary>
    /// Ensures the required blob containers exist with public read access for images
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            var menuContainer = _blobServiceClient.GetBlobContainerClient(MenuImagesContainer);
            await menuContainer.CreateIfNotExistsAsync(PublicAccessType.Blob);
            _logger.LogInformation("Blob container '{Container}' initialized", MenuImagesContainer);

            var profileContainer = _blobServiceClient.GetBlobContainerClient(ProfilePicturesContainer);
            await profileContainer.CreateIfNotExistsAsync(PublicAccessType.Blob);
            _logger.LogInformation("Blob container '{Container}' initialized", ProfilePicturesContainer);

            var invoiceContainer = _blobServiceClient.GetBlobContainerClient(InvoiceUploadsContainer);
            await invoiceContainer.CreateIfNotExistsAsync(PublicAccessType.Blob);
            _logger.LogInformation("Blob container '{Container}' initialized", InvoiceUploadsContainer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize blob containers");
        }
    }

    /// <summary>
    /// Uploads a menu item image to blob storage.
    /// Returns the CDN/blob URL of the uploaded image.
    /// </summary>
    public async Task<string> UploadMenuImageAsync(Stream fileStream, string fileName, string contentType, string outletId)
    {
        ValidateUpload(fileStream, fileName, contentType);

        // Compress image before upload
        var originalSize = fileStream.Length;
        var (compressedStream, compressedContentType) = await ImageCompressor.CompressAsync(fileStream, contentType, isProfilePicture: false);
        await using var _ = compressedStream;
        contentType = compressedContentType;

        var containerClient = _blobServiceClient.GetBlobContainerClient(MenuImagesContainer);

        // Use extension matching the (possibly changed) content type
        var extension = GetExtensionForContentType(contentType);
        var blobName = $"{outletId}/{Guid.NewGuid()}{extension}";

        var blobClient = containerClient.GetBlobClient(blobName);

        var headers = new BlobHttpHeaders
        {
            ContentType = contentType,
            CacheControl = "public, max-age=31536000, immutable" // 1 year cache — images are immutable (new upload = new URL)
        };

        await blobClient.UploadAsync(compressedStream, new BlobUploadOptions { HttpHeaders = headers });

        _logger.LogInformation("Uploaded menu image: {BlobName} ({ContentType}, {OriginalSize} -> {CompressedSize} bytes)",
            blobName, contentType, originalSize, compressedStream.Length);

        return GetPublicUrl(MenuImagesContainer, blobName);
    }

    /// <summary>
    /// Deletes a menu item image from blob storage by its full URL.
    /// </summary>
    public async Task<bool> DeleteMenuImageAsync(string imageUrl)
    {
        if (string.IsNullOrEmpty(imageUrl))
            return false;

        try
        {
            var blobName = ExtractBlobName(imageUrl, MenuImagesContainer);
            if (string.IsNullOrEmpty(blobName))
                return false;

            var containerClient = _blobServiceClient.GetBlobContainerClient(MenuImagesContainer);
            var blobClient = containerClient.GetBlobClient(blobName);
            var response = await blobClient.DeleteIfExistsAsync();

            if (response.Value)
                _logger.LogInformation("Deleted menu image: {BlobName}", blobName);

            return response.Value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete menu image: {ImageUrl}", imageUrl);
            return false;
        }
    }

    /// <summary>
    /// Uploads a profile picture to blob storage.
    /// Returns the CDN/blob URL of the uploaded image.
    /// </summary>
    public async Task<string> UploadProfilePictureAsync(Stream fileStream, string fileName, string contentType, string userId)
    {
        ValidateUpload(fileStream, fileName, contentType);

        // Compress image before upload (profile pictures get smaller max dimension)
        var originalSize = fileStream.Length;
        var (compressedStream, compressedContentType) = await ImageCompressor.CompressAsync(fileStream, contentType, isProfilePicture: true);
        await using var _ = compressedStream;
        contentType = compressedContentType;

        var containerClient = _blobServiceClient.GetBlobContainerClient(ProfilePicturesContainer);

        // Use extension matching the (possibly changed) content type
        var extension = GetExtensionForContentType(contentType);
        var blobName = $"{userId}/{Guid.NewGuid()}{extension}";

        var blobClient = containerClient.GetBlobClient(blobName);

        var headers = new BlobHttpHeaders
        {
            ContentType = contentType,
            CacheControl = "public, max-age=31536000, immutable"
        };

        await blobClient.UploadAsync(compressedStream, new BlobUploadOptions { HttpHeaders = headers });

        _logger.LogInformation("Uploaded profile picture: {BlobName} ({ContentType}, {OriginalSize} -> {CompressedSize} bytes)",
            blobName, contentType, originalSize, compressedStream.Length);

        return GetPublicUrl(ProfilePicturesContainer, blobName);
    }

    /// <summary>
    /// Deletes a profile picture from blob storage by its full URL.
    /// </summary>
    public async Task<bool> DeleteProfilePictureAsync(string imageUrl)
    {
        if (string.IsNullOrEmpty(imageUrl))
            return false;

        try
        {
            var blobName = ExtractBlobName(imageUrl, ProfilePicturesContainer);
            if (string.IsNullOrEmpty(blobName))
                return false;

            var containerClient = _blobServiceClient.GetBlobContainerClient(ProfilePicturesContainer);
            var blobClient = containerClient.GetBlobClient(blobName);
            var response = await blobClient.DeleteIfExistsAsync();

            if (response.Value)
                _logger.LogInformation("Deleted profile picture: {BlobName}", blobName);

            return response.Value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete profile picture: {ImageUrl}", imageUrl);
            return false;
        }
    }

    /// <summary>
    /// Uploads an invoice screenshot for external order claims (no compression — preserve text quality for OCR).
    /// </summary>
    public async Task<string> UploadInvoiceImageAsync(Stream fileStream, string fileName, string contentType, string userId)
    {
        ValidateUpload(fileStream, fileName, contentType);

        var containerClient = _blobServiceClient.GetBlobContainerClient(InvoiceUploadsContainer);
        var extension = GetExtensionForContentType(contentType);
        var blobName = $"{userId}/{Guid.NewGuid()}{extension}";
        var blobClient = containerClient.GetBlobClient(blobName);

        var headers = new BlobHttpHeaders
        {
            ContentType = contentType,
            CacheControl = "public, max-age=31536000, immutable"
        };

        await blobClient.UploadAsync(fileStream, new BlobUploadOptions { HttpHeaders = headers });

        _logger.LogInformation("Uploaded invoice image: {BlobName} ({ContentType}, {Size} bytes)",
            blobName, contentType, fileStream.Length);

        return GetPublicUrl(InvoiceUploadsContainer, blobName);
    }

    private void ValidateUpload(Stream fileStream, string fileName, string contentType)
    {
        if (fileStream.Length > MaxFileSizeBytes)
            throw new ArgumentException($"File size exceeds maximum of {MaxFileSizeBytes / (1024 * 1024)}MB");

        if (fileStream.Length == 0)
            throw new ArgumentException("File is empty");

        if (!AllowedContentTypes.Contains(contentType))
            throw new ArgumentException($"Content type '{contentType}' is not allowed. Allowed: {string.Join(", ", AllowedContentTypes)}");

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
            throw new ArgumentException($"File extension '{extension}' is not allowed. Allowed: {string.Join(", ", AllowedExtensions)}");
    }

    private static string GetExtensionForContentType(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        _ => ".jpg"
    };

    /// <summary>
    /// Returns full public URL — CDN if configured, otherwise direct blob URL.
    /// </summary>
    private string GetPublicUrl(string containerName, string blobName)
    {
        if (!string.IsNullOrEmpty(_cdnBaseUrl))
            return $"{_cdnBaseUrl.TrimEnd('/')}/{containerName}/{blobName}";

        // Direct blob storage URL (still works with public container access)
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        return $"{containerClient.Uri}/{blobName}";
    }

    /// <summary>
    /// Extracts the blob name from a full CDN or blob URL.
    /// </summary>
    private string? ExtractBlobName(string url, string containerName)
    {
        var containerPath = $"/{containerName}/";
        var idx = url.IndexOf(containerPath, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
            return url[(idx + containerPath.Length)..];

        return null;
    }
}
