using System.Text.Json;
using TianShu.AppHost.Tools;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using TianShu.Provider.Abstractions;

namespace TianShu.AppHost.Tools.Runtime;

internal static class KernelViewImageRuntimeSupport
{
    internal const string UnsupportedImageInputsMessage = "view_image is not allowed because you do not support image inputs";

    public static JsonElement BuildInputSchema(bool includeOriginalDetail)
    {
        var properties = new Dictionary<string, object?>
        {
            ["path"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Local filesystem path to an image file",
            },
        };
        if (includeOriginalDetail)
        {
            properties["detail"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Optional detail override. The only supported value is `original`; omit this field for default resized behavior. Use `original` to preserve the file's original resolution instead of resizing to fit.",
            };
        }

        return JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new[] { "path" },
            ["additionalProperties"] = false,
        });
    }

    public static ProviderResponsesToolDefinition BuildProviderToolDefinition(bool includeOriginalDetail)
        => new ProviderResponsesFunctionToolDefinition(
            "view_image",
            "View a local image from the filesystem (only use if given a full filepath by the user, and the image isn't already attached to the thread context within <image ...> tags).",
            BuildInputSchema(includeOriginalDetail),
            strict: false);

    public static async Task<KernelToolResult> ExecuteAsync(JsonElement arguments, KernelToolCallContext context, CancellationToken cancellationToken)
        => await ViewImageToolExecutor.ExecuteAsync(arguments, context, cancellationToken).ConfigureAwait(false);
}

internal static class ViewImageToolExecutor
{
    private const int MaxWidth = 2048;
    private const int MaxHeight = 768;
    private const string OriginalDetail = "original";

    public static async Task<KernelToolResult> ExecuteAsync(JsonElement arguments, KernelToolCallContext context, CancellationToken cancellationToken)
    {
        if (!context.SupportsImageInput)
        {
            return new KernelToolResult(false, KernelViewImageRuntimeSupport.UnsupportedImageInputsMessage);
        }

        var rawPath = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(arguments, "path"));
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return new KernelToolResult(false, "path must not be empty");
        }

        var rawDetail = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(arguments, "detail"));
        if (!string.IsNullOrWhiteSpace(rawDetail)
            && !string.Equals(rawDetail, OriginalDetail, StringComparison.Ordinal))
        {
            return new KernelToolResult(false, $"view_image.detail only supports `original`; omit `detail` for default resized behavior, got `{rawDetail}`");
        }

        var cwd = KernelToolJsonHelpers.Normalize(context.Cwd) ?? Directory.GetCurrentDirectory();
        var fullPath = Path.GetFullPath(Path.IsPathRooted(rawPath)
            ? rawPath
            : Path.Combine(cwd, rawPath));

        if (Directory.Exists(fullPath))
        {
            return new KernelToolResult(false, $"image path `{fullPath}` is not a file");
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or FileNotFoundException)
        {
            return new KernelToolResult(false, $"unable to locate image at `{fullPath}`: {ex.Message}");
        }

        var useOriginalDetail = context.CanRequestOriginalImageDetail
                                && string.Equals(rawDetail, OriginalDetail, StringComparison.Ordinal);
        if (!TryBuildDataUrl(fullPath, bytes, useOriginalDetail, out var dataUrl, out var detail, out var error))
        {
            return new KernelToolResult(false, error ?? $"TianShu cannot attach image at `{fullPath}`.");
        }

        return new KernelToolResult(
            success: true,
            outputText: string.Empty,
            outputContentItems:
            [
                new KernelToolOutputContentItem(
                    Type: "input_image",
                    ImageUrl: dataUrl,
                    Detail: detail),
            ]);
    }

    private static bool TryBuildDataUrl(
        string fullPath,
        byte[] bytes,
        bool useOriginalDetail,
        out string? dataUrl,
        out string? detail,
        out string? error)
    {
        dataUrl = null;
        detail = null;
        error = null;

        if (bytes.Length == 0)
        {
            error = $"Image located at `{fullPath}` is invalid: file is empty";
            return false;
        }

        var guessedMime = GuessMimeTypeFromPath(fullPath);

        try
        {
            var format = Image.DetectFormat(bytes) ?? throw new UnknownImageFormatException("Image format not recognized.");
            using var image = Image.Load(bytes);
            var shouldResize = !useOriginalDetail && (image.Width > MaxWidth || image.Height > MaxHeight);

            byte[] outputBytes;
            string mimeType;
            if (!shouldResize && CanPreserveSourceBytes(format))
            {
                outputBytes = bytes;
                mimeType = GetMimeType(format);
            }
            else
            {
                if (shouldResize)
                {
                    image.Mutate(static operation => operation.Resize(new ResizeOptions
                    {
                        Mode = ResizeMode.Max,
                        Size = new Size(MaxWidth, MaxHeight),
                        Sampler = KnownResamplers.Triangle,
                    }));
                }

                outputBytes = EncodePng(image);
                mimeType = "image/png";
            }

            dataUrl = $"data:{mimeType};base64,{Convert.ToBase64String(outputBytes)}";
            detail = useOriginalDetail ? OriginalDetail : null;
            return true;
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            error = BuildImageError(fullPath, guessedMime, ex.Message);
            return false;
        }
    }

    private static byte[] EncodePng(Image image)
    {
        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        return stream.ToArray();
    }

    private static string BuildImageError(string fullPath, string? guessedMime, string reason)
    {
        var normalizedMime = KernelToolJsonHelpers.Normalize(guessedMime);
        if (string.IsNullOrWhiteSpace(normalizedMime))
        {
            return $"TianShu could not read the local image at `{fullPath}`: unsupported MIME type (unknown)";
        }

        if (!normalizedMime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return $"TianShu could not read the local image at `{fullPath}`: unsupported MIME type `{normalizedMime}`";
        }

        if (!CanDecodeMimeType(normalizedMime))
        {
            return $"TianShu cannot attach image at `{fullPath}`: unsupported image format `{normalizedMime}`.";
        }

        return $"Image located at `{fullPath}` is invalid: {reason}";
    }

    private static bool CanPreserveSourceBytes(IImageFormat format)
    {
        var formatName = format.Name;
        return string.Equals(formatName, "PNG", StringComparison.OrdinalIgnoreCase)
               || string.Equals(formatName, "JPEG", StringComparison.OrdinalIgnoreCase)
               || string.Equals(formatName, "WEBP", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanDecodeMimeType(string mimeType)
    {
        return mimeType switch
        {
            "image/png" => true,
            "image/jpeg" => true,
            "image/gif" => true,
            "image/webp" => true,
            "image/bmp" => true,
            _ => false,
        };
    }

    private static string GetMimeType(IImageFormat format)
    {
        var formatName = format.Name;
        if (string.Equals(formatName, "JPEG", StringComparison.OrdinalIgnoreCase))
        {
            return "image/jpeg";
        }

        if (string.Equals(formatName, "WEBP", StringComparison.OrdinalIgnoreCase))
        {
            return "image/webp";
        }

        if (string.Equals(formatName, "GIF", StringComparison.OrdinalIgnoreCase))
        {
            return "image/gif";
        }

        if (string.Equals(formatName, "BMP", StringComparison.OrdinalIgnoreCase))
        {
            return "image/bmp";
        }

        return "image/png";
    }

    private static string? GuessMimeTypeFromPath(string fullPath)
    {
        return Path.GetExtension(fullPath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            ".avif" => "image/avif",
            ".heic" => "image/heic",
            ".heif" => "image/heif",
            ".ico" => "image/x-icon",
            ".tif" or ".tiff" => "image/tiff",
            ".json" => "application/json",
            ".txt" => "text/plain",
            _ => null,
        };
    }
}
