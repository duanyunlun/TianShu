using System.Text;
using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

public sealed class KernelApplyPatchRuntimeSupportTests
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    [Fact]
    public async Task ExecuteAsync_AddFile_WritesFileAndReturnsSummary()
    {
        var root = CreateTempDirectory();
        try
        {
            var patch =
                """
                *** Begin Patch
                *** Add File: hello.txt
                +Hello world
                *** End Patch
                """;
            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new { input = patch }));
            var result = await KernelApplyPatchRuntimeSupport.ExecuteAsync(
                args.RootElement,
                new KernelToolCallContext("thread", "turn", root),
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal("Success. Updated the following files:\nA hello.txt\n", result.OutputText);
            Assert.Equal("Hello world\n", await File.ReadAllTextAsync(Path.Combine(root, "hello.txt"), Utf8NoBom));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteCustomAsync_AddFile_WritesFileAndReturnsSummary()
    {
        var root = CreateTempDirectory();
        try
        {
            var patch =
                """
                *** Begin Patch
                *** Add File: hello.txt
                +Hello world
                *** End Patch
                """;
            var result = await KernelApplyPatchRuntimeSupport.ExecuteCustomAsync(
                patch,
                new KernelToolCallContext("thread", "turn", root),
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal("Success. Updated the following files:\nA hello.txt\n", result.OutputText);
            Assert.Equal("Hello world\n", await File.ReadAllTextAsync(Path.Combine(root, "hello.txt"), Utf8NoBom));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteAsync_UpdateFile_ModifiesFileAndAppendsTrailingNewline()
    {
        var root = CreateTempDirectory();
        try
        {
            var filePath = Path.Combine(root, "source.txt");
            await File.WriteAllTextAsync(filePath, "original content", Utf8NoBom);

            var patch =
                """
                *** Begin Patch
                *** Update File: source.txt
                @@
                -original content
                +modified by apply_patch
                *** End Patch
                """;
            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new { input = patch }));
            var result = await KernelApplyPatchRuntimeSupport.ExecuteAsync(
                args.RootElement,
                new KernelToolCallContext("thread", "turn", root),
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal("Success. Updated the following files:\nM source.txt\n", result.OutputText);
            Assert.Equal("modified by apply_patch\n", await File.ReadAllTextAsync(filePath, Utf8NoBom));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteAsync_UpdateFileWithMove_RenamesAndWritesDestination()
    {
        var root = CreateTempDirectory();
        try
        {
            var sourcePath = Path.Combine(root, "old.txt");
            await File.WriteAllTextAsync(sourcePath, "old\n", Utf8NoBom);

            var patch =
                """
                *** Begin Patch
                *** Update File: old.txt
                *** Move to: nested/new.txt
                @@
                -old
                +new
                *** End Patch
                """;
            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new { input = patch }));
            var result = await KernelApplyPatchRuntimeSupport.ExecuteAsync(
                args.RootElement,
                new KernelToolCallContext("thread", "turn", root),
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal("Success. Updated the following files:\nM nested/new.txt\n", result.OutputText);
            Assert.False(File.Exists(sourcePath));
            Assert.Equal("new\n", await File.ReadAllTextAsync(Path.Combine(root, "nested", "new.txt"), Utf8NoBom));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteAsync_DeleteFile_RemovesFile()
    {
        var root = CreateTempDirectory();
        try
        {
            var filePath = Path.Combine(root, "obsolete.txt");
            await File.WriteAllTextAsync(filePath, "gone\n", Utf8NoBom);

            var patch =
                """
                *** Begin Patch
                *** Delete File: obsolete.txt
                *** End Patch
                """;
            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new { input = patch }));
            var result = await KernelApplyPatchRuntimeSupport.ExecuteAsync(
                args.RootElement,
                new KernelToolCallContext("thread", "turn", root),
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal("Success. Updated the following files:\nD obsolete.txt\n", result.OutputText);
            Assert.False(File.Exists(filePath));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteAsync_DeleteThenAddSamePath_PreservesPatchOrder()
    {
        var root = CreateTempDirectory();
        try
        {
            var filePath = Path.Combine(root, "MainWindow.xaml");
            await File.WriteAllTextAsync(filePath, "<old />\n", Utf8NoBom);

            var patch =
                """
                *** Begin Patch
                *** Delete File: MainWindow.xaml
                *** Add File: MainWindow.xaml
                +<new />
                *** End Patch
                """;
            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new { input = patch }));
            var result = await KernelApplyPatchRuntimeSupport.ExecuteAsync(
                args.RootElement,
                new KernelToolCallContext("thread", "turn", root),
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal("Success. Updated the following files:\nD MainWindow.xaml\nA MainWindow.xaml\n", result.OutputText);
            Assert.True(File.Exists(filePath));
            Assert.Equal("<new />\n", await File.ReadAllTextAsync(filePath, Utf8NoBom));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteAsync_EmptyPatch_Fails()
    {
        var root = CreateTempDirectory();
        try
        {
            var patch =
                """
                *** Begin Patch
                *** End Patch
                """;
            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new { input = patch }));
            var result = await KernelApplyPatchRuntimeSupport.ExecuteAsync(
                args.RootElement,
                new KernelToolCallContext("thread", "turn", root),
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("apply_patch verification failed: No files were modified.", result.OutputText);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteAsync_VerificationFailure_DoesNotPartiallyApplyChanges()
    {
        var root = CreateTempDirectory();
        try
        {
            var patch =
                """
                *** Begin Patch
                *** Add File: created.txt
                +hello
                *** Update File: missing.txt
                @@
                -old
                +new
                *** End Patch
                """;
            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new { input = patch }));
            var result = await KernelApplyPatchRuntimeSupport.ExecuteAsync(
                args.RootElement,
                new KernelToolCallContext("thread", "turn", root),
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("apply_patch verification failed:", result.OutputText, StringComparison.Ordinal);
            Assert.Contains("Failed to read file to update", result.OutputText, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(root, "created.txt")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ExpectedLinesNotFound_FailsWithoutModifyingFile()
    {
        var root = CreateTempDirectory();
        var filePath = Path.Combine(root, "source.txt");
        try
        {
            await File.WriteAllTextAsync(filePath, "alpha\n", Utf8NoBom);

            var patch =
                """
                *** Begin Patch
                *** Update File: source.txt
                @@
                -beta
                +gamma
                *** End Patch
                """;
            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new { input = patch }));
            var result = await KernelApplyPatchRuntimeSupport.ExecuteAsync(
                args.RootElement,
                new KernelToolCallContext("thread", "turn", root),
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("Failed to find expected lines", result.OutputText, StringComparison.Ordinal);
            Assert.Equal("alpha\n", await File.ReadAllTextAsync(filePath, Utf8NoBom));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteAsync_InvalidHunkHeader_Fails()
    {
        var root = CreateTempDirectory();
        try
        {
            var patch =
                """
                *** Begin Patch
                *** Not A Hunk
                *** End Patch
                """;
            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new { input = patch }));
            var result = await KernelApplyPatchRuntimeSupport.ExecuteAsync(
                args.RootElement,
                new KernelToolCallContext("thread", "turn", root),
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("invalid hunk at line 2", result.OutputText, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteAsync_AddFile_ShouldHonorSandboxWriteRestriction()
    {
        var root = CreateTempDirectory();
        try
        {
            var patch =
                """
                *** Begin Patch
                *** Add File: blocked.txt
                +blocked
                *** End Patch
                """;
            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new { input = patch }));
            var result = await KernelApplyPatchRuntimeSupport.ExecuteAsync(
                args.RootElement,
                new KernelToolCallContext(
                    "thread",
                    "turn",
                    root,
                    SandboxPolicy: JsonSerializer.SerializeToElement(new { type = "readOnly", networkAccess = false }),
                    SandboxMode: "readOnly"),
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("Patch target is outside sandbox writable roots", result.OutputText, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(root, "blocked.txt")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteAsync_DeleteFile_ShouldHonorSandboxWriteRestriction()
    {
        var root = CreateTempDirectory();
        try
        {
            var filePath = Path.Combine(root, "blocked.txt");
            await File.WriteAllTextAsync(filePath, "blocked\n", Utf8NoBom);

            var patch =
                """
                *** Begin Patch
                *** Delete File: blocked.txt
                *** End Patch
                """;
            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new { input = patch }));
            var result = await KernelApplyPatchRuntimeSupport.ExecuteAsync(
                args.RootElement,
                new KernelToolCallContext(
                    "thread",
                    "turn",
                    root,
                    SandboxPolicy: JsonSerializer.SerializeToElement(new { type = "readOnly", networkAccess = false }),
                    SandboxMode: "readOnly"),
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("Patch target is outside sandbox writable roots", result.OutputText, StringComparison.Ordinal);
            Assert.True(File.Exists(filePath));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteAsync_UpdateFile_ShouldHonorSandboxWriteRestriction()
    {
        var root = CreateTempDirectory();
        var filePath = Path.Combine(root, "source.txt");
        try
        {
            await File.WriteAllTextAsync(filePath, "original\n", Utf8NoBom);

            var patch =
                """
                *** Begin Patch
                *** Update File: source.txt
                @@
                -original
                +changed
                *** End Patch
                """;
            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new { input = patch }));
            var result = await KernelApplyPatchRuntimeSupport.ExecuteAsync(
                args.RootElement,
                new KernelToolCallContext(
                    "thread",
                    "turn",
                    root,
                    SandboxPolicy: JsonSerializer.SerializeToElement(new { type = "readOnly", networkAccess = false }),
                    SandboxMode: "readOnly"),
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("Patch target is outside sandbox writable roots", result.OutputText, StringComparison.Ordinal);
            Assert.Equal("original\n", await File.ReadAllTextAsync(filePath, Utf8NoBom));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteAsync_UpdateFileWithMove_ShouldValidateDestinationWriteRestriction()
    {
        var root = CreateTempDirectory();
        try
        {
            var allowedRoot = Path.Combine(root, "allowed");
            Directory.CreateDirectory(allowedRoot);
            var sourcePath = Path.Combine(allowedRoot, "old.txt");
            await File.WriteAllTextAsync(sourcePath, "old\n", Utf8NoBom);

            var patch =
                """
                *** Begin Patch
                *** Update File: allowed/old.txt
                *** Move to: blocked/new.txt
                @@
                -old
                +new
                *** End Patch
                """;
            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new { input = patch }));
            var result = await KernelApplyPatchRuntimeSupport.ExecuteAsync(
                args.RootElement,
                new KernelToolCallContext(
                    "thread",
                    "turn",
                    root,
                    SandboxPolicy: JsonSerializer.SerializeToElement(new
                    {
                        type = "workspaceWrite",
                        writableRoots = new[] { allowedRoot.Replace("\\", "/") },
                        networkAccess = false,
                    }),
                    SandboxMode: "workspaceWrite"),
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("Patch target is outside sandbox writable roots", result.OutputText, StringComparison.Ordinal);
            Assert.True(File.Exists(sourcePath));
            Assert.False(File.Exists(Path.Combine(root, "blocked", "new.txt")));
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
