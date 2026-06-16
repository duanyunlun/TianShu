using System.Text;
using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;
using TianShu.Contracts.Tools;

namespace TianShu.AppHost.Tests;

public sealed class GrepFilesToolHandlerTests
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    [Fact]
    public void ImplementationBinding_UsesFilesystemProviderManagedImplementation()
    {
        var handler = ToolProviderTestAdapters.CreateFileSystemRuntimeHandler("grep_files");

        Assert.Equal("grep_files", handler.ImplementationBinding.ToolKey);
        Assert.Equal(ToolImplementationKind.Managed, handler.ImplementationBinding.ImplementationKind);
        Assert.Equal("tianshu.tools.filesystem", handler.ImplementationBinding.ImplementationId);
        Assert.Contains(handler.ImplementationBinding.Requirements, static requirement =>
            string.Equals(requirement.Key, "file_system", StringComparison.Ordinal));
        Assert.Equal("managed_default", handler.ImplementationBinding.FallbackPolicy?.Strategy);
    }

    [Fact]
    public async Task ExecuteAsync_FailsWhenPatternMissing()
    {
        var handler = ToolProviderTestAdapters.CreateFileSystemRuntimeHandler("grep_files");
        using var args = JsonDocument.Parse("{}");

        var result = await handler.ExecuteAsync(
            args.RootElement,
            new KernelToolCallContext("thread", "turn", Environment.CurrentDirectory),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("pattern must not be empty", result.OutputText);
    }

    [Fact]
    public async Task ExecuteAsync_FailsWhenLimitIsZero()
    {
        var handler = ToolProviderTestAdapters.CreateFileSystemRuntimeHandler("grep_files");
        using var args = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            pattern = "alpha",
            limit = 0,
        }));

        var result = await handler.ExecuteAsync(
            args.RootElement,
            new KernelToolCallContext("thread", "turn", Environment.CurrentDirectory),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("limit must be greater than zero", result.OutputText);
    }

    [Fact]
    public async Task ExecuteAsync_FailsWhenPathInaccessible()
    {
        var handler = ToolProviderTestAdapters.CreateFileSystemRuntimeHandler("grep_files");
        var missing = Path.Combine(Path.GetTempPath(), "tianshu-missing-" + Guid.NewGuid().ToString("N"));
        using var args = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            pattern = "alpha",
            path = missing.Replace('\\', '/'),
        }));

        var result = await handler.ExecuteAsync(
            args.RootElement,
            new KernelToolCallContext("thread", "turn", Environment.CurrentDirectory),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("unable to access", result.OutputText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNoMatchesAsFailure()
    {
        var root = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "one.txt"), "omega", Utf8NoBom);

            var handler = ToolProviderTestAdapters.CreateFileSystemRuntimeHandler("grep_files");
            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                pattern = "alpha",
                path = root.Replace('\\', '/'),
                limit = 10,
            }));

            var result = await handler.ExecuteAsync(
                args.RootElement,
                new KernelToolCallContext("thread", "turn", root),
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("No matches found.", result.OutputText);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsMatchingFiles()
    {
        var root = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "match_one.txt"), "alpha beta gamma", Utf8NoBom);
            await File.WriteAllTextAsync(Path.Combine(root, "match_two.txt"), "alpha delta", Utf8NoBom);
            await File.WriteAllTextAsync(Path.Combine(root, "other.txt"), "omega", Utf8NoBom);

            var handler = ToolProviderTestAdapters.CreateFileSystemRuntimeHandler("grep_files");
            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                pattern = "alpha",
                path = root.Replace('\\', '/'),
                limit = 10,
            }));

            var result = await handler.ExecuteAsync(
                args.RootElement,
                new KernelToolCallContext("thread", "turn", root),
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("match_one.txt", result.OutputText, StringComparison.Ordinal);
            Assert.Contains("match_two.txt", result.OutputText, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteAsync_RespectsIncludeGlob()
    {
        var root = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "match_one.rs"), "alpha beta gamma", Utf8NoBom);
            await File.WriteAllTextAsync(Path.Combine(root, "match_two.txt"), "alpha delta", Utf8NoBom);

            var handler = ToolProviderTestAdapters.CreateFileSystemRuntimeHandler("grep_files");
            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                pattern = "alpha",
                include = "*.rs",
                path = root.Replace('\\', '/'),
                limit = 10,
            }));

            var result = await handler.ExecuteAsync(
                args.RootElement,
                new KernelToolCallContext("thread", "turn", root),
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("match_one.rs", result.OutputText, StringComparison.Ordinal);
            Assert.DoesNotContain("match_two.txt", result.OutputText, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteAsync_RespectsLimit()
    {
        var root = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "one.txt"), "alpha one", Utf8NoBom);
            await File.WriteAllTextAsync(Path.Combine(root, "two.txt"), "alpha two", Utf8NoBom);
            await File.WriteAllTextAsync(Path.Combine(root, "three.txt"), "alpha three", Utf8NoBom);

            var handler = ToolProviderTestAdapters.CreateFileSystemRuntimeHandler("grep_files");
            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                pattern = "alpha",
                path = root.Replace('\\', '/'),
                limit = 2,
            }));

            var result = await handler.ExecuteAsync(
                args.RootElement,
                new KernelToolCallContext("thread", "turn", root),
                CancellationToken.None);

            Assert.True(result.Success);
            var lines = result.OutputText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.Equal(2, lines.Length);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteAsync_SkipsBinaryFiles()
    {
        var root = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "text.txt"), "alpha text", Utf8NoBom);
            await File.WriteAllBytesAsync(Path.Combine(root, "binary.dat"), [0, 1, 2, 3, 4, 5]);

            var handler = ToolProviderTestAdapters.CreateFileSystemRuntimeHandler("grep_files");
            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                pattern = "alpha|\\u0001",
                path = root.Replace('\\', '/'),
                limit = 10,
            }));

            var result = await handler.ExecuteAsync(
                args.RootElement,
                new KernelToolCallContext("thread", "turn", root),
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("text.txt", result.OutputText, StringComparison.Ordinal);
            Assert.DoesNotContain("binary.dat", result.OutputText, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "tianshu-kernel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(120);
            }
            catch (UnauthorizedAccessException) when (attempt < 5)
            {
                Thread.Sleep(120);
            }
        }

        Directory.Delete(path, recursive: true);
    }
}

