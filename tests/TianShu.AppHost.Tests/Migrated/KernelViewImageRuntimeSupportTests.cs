using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace TianShu.AppHost.Tests;

public sealed class KernelViewImageRuntimeSupportTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldReturnInputImageContentItem()
    {
        var root = CreateTempDirectory();
        try
        {
            var imagePath = Path.Combine(root, "pixel.png");
            WritePng(imagePath, 1, 1);

            using var json = JsonDocument.Parse($"{{\"path\":\"{imagePath.Replace("\\", "\\\\")}\"}}");
            var result = await KernelViewImageRuntimeSupport.ExecuteAsync(json.RootElement.Clone(), new KernelToolCallContext("thread", "turn", root), CancellationToken.None);

            Assert.True(result.Success);
            Assert.NotNull(result.OutputContentItems);
            var item = Assert.Single(result.OutputContentItems!);
            Assert.Equal("input_image", item.Type);
            Assert.StartsWith("data:image/png;base64,", item.ImageUrl, StringComparison.Ordinal);
            Assert.Null(item.Detail);
            Assert.Equal(string.Empty, result.OutputText);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectTextOnlyModel()
    {
        var root = CreateTempDirectory();
        try
        {
            var imagePath = Path.Combine(root, "pixel.png");
            WritePng(imagePath, 1, 1);

            using var json = JsonDocument.Parse($"{{\"path\":\"{imagePath.Replace("\\", "\\\\")}\"}}");
            var result = await KernelViewImageRuntimeSupport.ExecuteAsync(
                json.RootElement.Clone(),
                new KernelToolCallContext("thread", "turn", root, SupportsImageInput: false),
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal(KernelViewImageRuntimeSupport.UnsupportedImageInputsMessage, result.OutputText);
            Assert.Null(result.OutputContentItems);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectMissingFile()
    {
        var root = CreateTempDirectory();
        try
        {
            var imagePath = Path.Combine(root, "missing.png");
            using var json = JsonDocument.Parse($"{{\"path\":\"{imagePath.Replace("\\", "\\\\")}\"}}");
            var result = await KernelViewImageRuntimeSupport.ExecuteAsync(json.RootElement.Clone(), new KernelToolCallContext("thread", "turn", root), CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("unable to locate image", result.OutputText, StringComparison.Ordinal);
            Assert.Null(result.OutputContentItems);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnPlaceholderForNonImageFile()
    {
        var root = CreateTempDirectory();
        try
        {
            var filePath = Path.Combine(root, "note.json");
            await File.WriteAllTextAsync(filePath, "{ \"message\": \"hello\" }");

            using var json = JsonDocument.Parse($"{{\"path\":\"{filePath.Replace("\\", "\\\\")}\"}}");
            var result = await KernelViewImageRuntimeSupport.ExecuteAsync(json.RootElement.Clone(), new KernelToolCallContext("thread", "turn", root), CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("TianShu could not read the local image", result.OutputText, StringComparison.Ordinal);
            Assert.DoesNotContain("Codex", result.OutputText, StringComparison.Ordinal);
            Assert.Contains("unsupported MIME type `application/json`", result.OutputText, StringComparison.Ordinal);
            Assert.Null(result.OutputContentItems);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectUnsupportedDetailValue()
    {
        var root = CreateTempDirectory();
        try
        {
            var imagePath = Path.Combine(root, "pixel.png");
            WritePng(imagePath, 1, 1);

            using var json = JsonDocument.Parse($"{{\"path\":\"{imagePath.Replace("\\", "\\\\")}\",\"detail\":\"low\"}}");
            var result = await KernelViewImageRuntimeSupport.ExecuteAsync(json.RootElement.Clone(), new KernelToolCallContext("thread", "turn", root), CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("view_image.detail only supports `original`; omit `detail` for default resized behavior, got `low`", result.OutputText);
            Assert.Null(result.OutputContentItems);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldIncludeOriginalDetailWhenCapabilityIsEnabled()
    {
        var root = CreateTempDirectory();
        try
        {
            var imagePath = Path.Combine(root, "large.png");
            WriteLargePng(imagePath, 4096, 2048);

            using var json = JsonDocument.Parse($"{{\"path\":\"{imagePath.Replace("\\", "\\\\")}\",\"detail\":\"original\"}}");
            var result = await KernelViewImageRuntimeSupport.ExecuteAsync(
                json.RootElement.Clone(),
                new KernelToolCallContext("thread", "turn", root, CanRequestOriginalImageDetail: true),
                CancellationToken.None);

            Assert.True(result.Success);
            var item = Assert.Single(result.OutputContentItems!);
            Assert.Equal("original", item.Detail);
            using var image = Image.Load(DecodeImageDataUrl(item.ImageUrl!));
            Assert.Equal(4096, image.Width);
            Assert.Equal(2048, image.Height);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldResizeLargeImageByDefault()
    {
        var root = CreateTempDirectory();
        try
        {
            var imagePath = Path.Combine(root, "large.png");
            WriteLargePng(imagePath, 4096, 2048);

            using var json = JsonDocument.Parse($"{{\"path\":\"{imagePath.Replace("\\", "\\\\")}\"}}");
            var result = await KernelViewImageRuntimeSupport.ExecuteAsync(json.RootElement.Clone(), new KernelToolCallContext("thread", "turn", root), CancellationToken.None);

            Assert.True(result.Success);
            var item = Assert.Single(result.OutputContentItems!);
            Assert.Null(item.Detail);
            using var image = Image.Load(DecodeImageDataUrl(item.ImageUrl!));
            Assert.True(image.Width <= 2048);
            Assert.True(image.Height <= 768);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldIgnoreOriginalDetailWhenCapabilityIsDisabled()
    {
        var root = CreateTempDirectory();
        try
        {
            var imagePath = Path.Combine(root, "large.png");
            WriteLargePng(imagePath, 4096, 2048);

            using var json = JsonDocument.Parse($"{{\"path\":\"{imagePath.Replace("\\", "\\\\")}\",\"detail\":\"original\"}}");
            var result = await KernelViewImageRuntimeSupport.ExecuteAsync(json.RootElement.Clone(), new KernelToolCallContext("thread", "turn", root), CancellationToken.None);

            Assert.True(result.Success);
            var item = Assert.Single(result.OutputContentItems!);
            Assert.Null(item.Detail);
            using var image = Image.Load(DecodeImageDataUrl(item.ImageUrl!));
            Assert.True(image.Width <= 2048);
            Assert.True(image.Height <= 768);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void WritePng(string imagePath, int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        image.SaveAsPng(imagePath);
    }

    private static void WriteLargePng(string imagePath, int width, int height)
        => WritePng(imagePath, width, height);

    private static byte[] DecodeImageDataUrl(string imageUrl)
    {
        var encoded = imageUrl.Split(',', 2)[1];
        return Convert.FromBase64String(encoded);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "TianShuTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
