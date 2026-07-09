using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Cafe.Api.Helpers;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Cafe.Api.Services;

public class BlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<BlobStorageService> _logger;
    private readonly string _cdnBaseUrl;
    private const string MenuImagesContainer = "menu-images";
    private const string ProfilePicturesContainer = "profile-pictures";
    private const string InvoiceUploadsContainer = "invoice-uploads";
    private const string ReceiptImagesContainer = "receipt-images";
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

            var receiptContainer = _blobServiceClient.GetBlobContainerClient(ReceiptImagesContainer);
            await receiptContainer.CreateIfNotExistsAsync(PublicAccessType.Blob);
            _logger.LogInformation("Blob container '{Container}' initialized", ReceiptImagesContainer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize blob containers");
        }
    }

    /// <summary>
    /// Uploads a menu item image to blob storage.
    /// Returns the CDN/blob URLs for full and thumbnail variants.
    /// </summary>
    public async Task<(string ImageUrl, string ThumbnailUrl)> UploadMenuImageAsync(Stream fileStream, string fileName, string contentType, string outletId)
    {
        ValidateUpload(fileStream, fileName, contentType);

        // Generate web-optimized menu variants (full + thumbnail)
        var originalSize = fileStream.Length;
        var (fullStream, thumbnailStream, compressedContentType) = await ImageCompressor.CompressMenuVariantsAsync(fileStream);
        await using var _ = fullStream;
        await using var __ = thumbnailStream;
        contentType = compressedContentType;

        var containerClient = _blobServiceClient.GetBlobContainerClient(MenuImagesContainer);

        var extension = ".webp";
        var safeBaseName = SanitizeFileBaseName(Path.GetFileNameWithoutExtension(fileName));
        var keyBase = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{safeBaseName}-{Guid.NewGuid():N}";
        var fullBlobName = $"{outletId}/{keyBase}{extension}";
        var thumbnailBlobName = $"{outletId}/{keyBase}_thumb{extension}";

        var fullBlobClient = containerClient.GetBlobClient(fullBlobName);
        var thumbnailBlobClient = containerClient.GetBlobClient(thumbnailBlobName);

        var headers = new BlobHttpHeaders
        {
            ContentType = contentType,
            CacheControl = "public, max-age=31536000, immutable" // 1 year cache — images are immutable (new upload = new URL)
        };

        await fullBlobClient.UploadAsync(fullStream, new BlobUploadOptions { HttpHeaders = headers });
        await thumbnailBlobClient.UploadAsync(thumbnailStream, new BlobUploadOptions { HttpHeaders = headers });

        _logger.LogInformation("Uploaded menu image variants: {FullBlob} ({FullSize} bytes), {ThumbBlob} ({ThumbSize} bytes), source {OriginalSize} bytes",
            fullBlobName, fullStream.Length, thumbnailBlobName, thumbnailStream.Length, originalSize);

        return (
            GetPublicUrl(MenuImagesContainer, fullBlobName),
            GetPublicUrl(MenuImagesContainer, thumbnailBlobName)
        );
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

            // Also clean up sibling thumbnail variant for full-size menu images.
            if (blobName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) &&
                !blobName.EndsWith("_thumb.webp", StringComparison.OrdinalIgnoreCase))
            {
                var thumbBlobName = blobName[..^5] + "_thumb.webp";
                var thumbBlobClient = containerClient.GetBlobClient(thumbBlobName);
                await thumbBlobClient.DeleteIfExistsAsync();
            }

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
    /// Uploads an invoice screenshot for external order claims with compression.
    /// </summary>
    public async Task<string> UploadInvoiceImageAsync(Stream fileStream, string fileName, string contentType, string userId)
    {
        ValidateUpload(fileStream, fileName, contentType);

        // Compress image before upload
        var originalSize = fileStream.Length;
        var (compressedStream, compressedContentType) = await ImageCompressor.CompressAsync(fileStream, contentType, isProfilePicture: false);
        await using var _ = compressedStream;
        contentType = compressedContentType;

        var containerClient = _blobServiceClient.GetBlobContainerClient(InvoiceUploadsContainer);
        var extension = GetExtensionForContentType(contentType);
        var blobName = $"{userId}/{Guid.NewGuid()}{extension}";
        var blobClient = containerClient.GetBlobClient(blobName);

        var headers = new BlobHttpHeaders
        {
            ContentType = contentType,
            CacheControl = "public, max-age=31536000, immutable"
        };

        await blobClient.UploadAsync(compressedStream, new BlobUploadOptions { HttpHeaders = headers });

        _logger.LogInformation("Uploaded invoice image: {BlobName} ({ContentType}, {OriginalSize} -> {CompressedSize} bytes)",
            blobName, contentType, originalSize, compressedStream.Length);

        return GetPublicUrl(InvoiceUploadsContainer, blobName);
    }

    /// <summary>
    /// Uploads a receipt/invoice image for an order with compression.
    /// Returns the CDN/blob URL of the uploaded image.
    /// </summary>
    public async Task<string> UploadReceiptImageAsync(Stream fileStream, string fileName, string contentType, string userId)
    {
        ValidateUpload(fileStream, fileName, contentType);

        // Compress image before upload
        var originalSize = fileStream.Length;
        var (compressedStream, compressedContentType) = await ImageCompressor.CompressAsync(fileStream, contentType, isProfilePicture: false);
        await using var _ = compressedStream;
        contentType = compressedContentType;

        var containerClient = _blobServiceClient.GetBlobContainerClient(ReceiptImagesContainer);
        var extension = GetExtensionForContentType(contentType);
        var blobName = $"{userId}/{Guid.NewGuid()}{extension}";
        var blobClient = containerClient.GetBlobClient(blobName);

        var headers = new BlobHttpHeaders
        {
            ContentType = contentType,
            CacheControl = "public, max-age=31536000, immutable"
        };

        await blobClient.UploadAsync(compressedStream, new BlobUploadOptions { HttpHeaders = headers });

        _logger.LogInformation("Uploaded receipt image: {BlobName} ({ContentType}, {OriginalSize} -> {CompressedSize} bytes)",
            blobName, contentType, originalSize, compressedStream.Length);

        return GetPublicUrl(ReceiptImagesContainer, blobName);
    }

    /// <summary>
    /// Deletes a receipt image from blob storage by its full URL.
    /// </summary>
    public async Task<bool> DeleteReceiptImageAsync(string imageUrl)
    {
        if (string.IsNullOrEmpty(imageUrl))
            return false;

        try
        {
            var blobName = ExtractBlobName(imageUrl, ReceiptImagesContainer);
            if (string.IsNullOrEmpty(blobName))
                return false;

            var containerClient = _blobServiceClient.GetBlobContainerClient(ReceiptImagesContainer);
            var blobClient = containerClient.GetBlobClient(blobName);
            var response = await blobClient.DeleteIfExistsAsync();

            if (response.Value)
                _logger.LogInformation("Deleted receipt image: {BlobName}", blobName);

            return response.Value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete receipt image: {ImageUrl}", imageUrl);
            return false;
        }
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

    private static string SanitizeFileBaseName(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized)) return "menu-image";

        normalized = Regex.Replace(normalized, "[^a-z0-9]+", "-").Trim('-');
        if (normalized.Length == 0) return "menu-image";
        return normalized.Length <= 40 ? normalized : normalized[..40];
    }

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
