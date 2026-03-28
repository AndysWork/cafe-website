using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Processing;

namespace Cafe.Api.Helpers;

/// <summary>
/// Compresses images before blob storage upload.
/// Resizes oversized images and applies format-specific quality optimization.
/// </summary>
public static class ImageCompressor
{
    private const int MaxDimension = 2048; // Max width or height in pixels
    private const int ProfilePicMaxDimension = 512;
    private const int JpegQuality = 82;
    private const int WebpQuality = 80;

    /// <summary>
    /// Compresses an image stream. Returns a new MemoryStream with compressed data
    /// and the (possibly unchanged) content type.
    /// </summary>
    public static async Task<(MemoryStream CompressedStream, string ContentType)> CompressAsync(
        Stream inputStream, string contentType, bool isProfilePicture = false)
    {
        inputStream.Position = 0;

        // GIFs may be animated — skip processing to preserve animation
        if (contentType.Equals("image/gif", StringComparison.OrdinalIgnoreCase))
        {
            var passthrough = new MemoryStream();
            await inputStream.CopyToAsync(passthrough);
            passthrough.Position = 0;
            return (passthrough, contentType);
        }

        using var image = await Image.LoadAsync(inputStream);

        var maxDim = isProfilePicture ? ProfilePicMaxDimension : MaxDimension;

        // Resize if either dimension exceeds the limit (maintain aspect ratio)
        if (image.Width > maxDim || image.Height > maxDim)
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(maxDim, maxDim)
            }));
        }

        var output = new MemoryStream();

        if (contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase))
        {
            await image.SaveAsync(output, new PngEncoder
            {
                CompressionLevel = PngCompressionLevel.BestCompression,
                ColorType = PngColorType.RgbWithAlpha
            });
        }
        else if (contentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase))
        {
            await image.SaveAsync(output, new WebpEncoder
            {
                Quality = WebpQuality,
                FileFormat = WebpFileFormatType.Lossy
            });
        }
        else
        {
            // Default to JPEG for image/jpeg and any other type
            await image.SaveAsync(output, new JpegEncoder
            {
                Quality = JpegQuality
            });
            contentType = "image/jpeg";
        }

        output.Position = 0;
        return (output, contentType);
    }
}
