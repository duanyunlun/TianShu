using System.Text;
using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

public sealed class ReadFileToolHandlerTests
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    [Fact]
    public async Task SliceMode_ReadsRequestedRange()
    {
        var root = CreateTempDirectory();
        var filePath = Path.Combine(root, "sample.txt");

        try
        {
            await File.WriteAllTextAsync(
                filePath,
                """
alpha
beta
gamma
""",
                Utf8NoBom);

            var handler = ToolProviderTestAdapters.CreateFileSystemRuntimeHandler("read_file");
            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                file_path = filePath.Replace('\\', '/'),
                offset = 2,
                limit = 2,
            }));

            var result = await handler.ExecuteAsync(
                args.RootElement,
                new KernelToolCallContext("thread", "turn", root),
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal("L2: beta\nL3: gamma", result.OutputText);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SliceMode_ErrorsWhenOffsetExceedsLength()
    {
        var root = CreateTempDirectory();
        var filePath = Path.Combine(root, "sample.txt");

        try
        {
            await File.WriteAllTextAsync(filePath, "only\n", Utf8NoBom);

            var handler = ToolProviderTestAdapters.CreateFileSystemRuntimeHandler("read_file");
            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                file_path = filePath.Replace('\\', '/'),
                offset = 3,
                limit = 1,
            }));

            var result = await handler.ExecuteAsync(
                args.RootElement,
                new KernelToolCallContext("thread", "turn", root),
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("offset exceeds file length", result.OutputText);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SliceMode_ReadsNonUtf8Lines()
    {
        var root = CreateTempDirectory();
        var filePath = Path.Combine(root, "sample.bin");

        try
        {
            var bytes = new List<byte>
            {
                0xFF,
                0xFE,
                (byte)'\n',
            };
            bytes.AddRange(Encoding.ASCII.GetBytes("plain\n"));
            await File.WriteAllBytesAsync(filePath, bytes.ToArray());

            var handler = ToolProviderTestAdapters.CreateFileSystemRuntimeHandler("read_file");
            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                file_path = filePath.Replace('\\', '/'),
                offset = 1,
                limit = 2,
            }));

            var result = await handler.ExecuteAsync(
                args.RootElement,
                new KernelToolCallContext("thread", "turn", root),
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal($"L1: \uFFFD\uFFFD\nL2: plain", result.OutputText);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SliceMode_TrimsCrlfEndings()
    {
        var root = CreateTempDirectory();
        var filePath = Path.Combine(root, "sample.txt");

        try
        {
            await File.WriteAllTextAsync(filePath, "one\r\ntwo\r\n", Utf8NoBom);

            var handler = ToolProviderTestAdapters.CreateFileSystemRuntimeHandler("read_file");
            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                file_path = filePath.Replace('\\', '/'),
                offset = 1,
                limit = 2,
            }));

            var result = await handler.ExecuteAsync(
                args.RootElement,
                new KernelToolCallContext("thread", "turn", root),
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal("L1: one\nL2: two", result.OutputText);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SliceMode_TruncatesLongLinesAtUtf8ByteLimit()
    {
        var root = CreateTempDirectory();
        var filePath = Path.Combine(root, "sample.txt");

        try
        {
            var longLine = new string('x', 500 + 50);
            await File.WriteAllTextAsync(filePath, $"{longLine}\n", Utf8NoBom);

            var handler = ToolProviderTestAdapters.CreateFileSystemRuntimeHandler("read_file");
            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                file_path = filePath.Replace('\\', '/'),
                offset = 1,
                limit = 1,
            }));

            var result = await handler.ExecuteAsync(
                args.RootElement,
                new KernelToolCallContext("thread", "turn", root),
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal($"L1: {new string('x', 500)}", result.OutputText);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SliceMode_TruncatesMultibyteCharactersAtUtf8Boundary()
    {
        var root = CreateTempDirectory();
        var filePath = Path.Combine(root, "sample.txt");

        try
        {
            var multi = new string('你', 200);
            await File.WriteAllTextAsync(filePath, $"{multi}\n", Utf8NoBom);

            var handler = ToolProviderTestAdapters.CreateFileSystemRuntimeHandler("read_file");
            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                file_path = filePath.Replace('\\', '/'),
                offset = 1,
                limit = 1,
            }));

            var result = await handler.ExecuteAsync(
                args.RootElement,
                new KernelToolCallContext("thread", "turn", root),
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal($"L1: {new string('你', 166)}", result.OutputText);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task IndentationMode_CapturesBlock()
    {
        var root = CreateTempDirectory();
        var filePath = Path.Combine(root, "sample.rs");

        try
        {
            await File.WriteAllTextAsync(
                filePath,
                """
fn outer() {
    if cond {
        inner();
    }
    tail();
}
""",
                Utf8NoBom);

            var handler = ToolProviderTestAdapters.CreateFileSystemRuntimeHandler("read_file");
            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                file_path = filePath.Replace('\\', '/'),
                offset = 3,
                limit = 10,
                mode = "indentation",
                indentation = new
                {
                    anchor_line = 3,
                    include_siblings = false,
                    max_levels = 1,
                },
            }));

            var result = await handler.ExecuteAsync(
                args.RootElement,
                new KernelToolCallContext("thread", "turn", root),
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal("L2:     if cond {\nL3:         inner();\nL4:     }", result.OutputText);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task IndentationMode_RespectsSiblingFlag()
    {
        var root = CreateTempDirectory();
        var filePath = Path.Combine(root, "sample.rs");

        try
        {
            await File.WriteAllTextAsync(
                filePath,
                """
fn wrapper() {
    if first {
        do_first();
    }
    if second {
        do_second();
    }
}
""",
                Utf8NoBom);

            var handler = ToolProviderTestAdapters.CreateFileSystemRuntimeHandler("read_file");
            using var argsWithout = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                file_path = filePath.Replace('\\', '/'),
                offset = 3,
                limit = 50,
                mode = "indentation",
                indentation = new
                {
                    anchor_line = 3,
                    include_siblings = false,
                    max_levels = 1,
                },
            }));

            var withoutSiblings = await handler.ExecuteAsync(
                argsWithout.RootElement,
                new KernelToolCallContext("thread", "turn", root),
                CancellationToken.None);
            Assert.True(withoutSiblings.Success);
            Assert.Equal("L2:     if first {\nL3:         do_first();\nL4:     }", withoutSiblings.OutputText);

            using var argsWith = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                file_path = filePath.Replace('\\', '/'),
                offset = 3,
                limit = 50,
                mode = "indentation",
                indentation = new
                {
                    anchor_line = 3,
                    include_siblings = true,
                    max_levels = 1,
                },
            }));

            var withSiblings = await handler.ExecuteAsync(
                argsWith.RootElement,
                new KernelToolCallContext("thread", "turn", root),
                CancellationToken.None);
            Assert.True(withSiblings.Success);
            Assert.Equal(
                "L2:     if first {\nL3:         do_first();\nL4:     }\nL5:     if second {\nL6:         do_second();\nL7:     }",
                withSiblings.OutputText);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task IndentationMode_IncludeHeaderControlsCommentInclusion()
    {
        var root = CreateTempDirectory();
        var filePath = Path.Combine(root, "sample.cpp");

        try
        {
            await File.WriteAllTextAsync(
                filePath,
                """
class Runner {
    // Run the code
    int run() const {
        switch (mode_) {
            case 1:
                return 1;
        }
    }
}
""",
                Utf8NoBom);

            var handler = ToolProviderTestAdapters.CreateFileSystemRuntimeHandler("read_file");
            using var argsWithHeaders = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                file_path = filePath.Replace('\\', '/'),
                offset = 6,
                limit = 200,
                mode = "indentation",
                indentation = new
                {
                    anchor_line = 6,
                    max_levels = 3,
                    include_siblings = false,
                    include_header = true,
                },
            }));

            var withHeaders = await handler.ExecuteAsync(
                argsWithHeaders.RootElement,
                new KernelToolCallContext("thread", "turn", root),
                CancellationToken.None);
            Assert.True(withHeaders.Success);
            Assert.Equal(
                "L2:     // Run the code\nL3:     int run() const {\nL4:         switch (mode_) {\nL5:             case 1:\nL6:                 return 1;\nL7:         }\nL8:     }",
                withHeaders.OutputText);

            using var argsWithoutHeaders = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                file_path = filePath.Replace('\\', '/'),
                offset = 6,
                limit = 200,
                mode = "indentation",
                indentation = new
                {
                    anchor_line = 6,
                    max_levels = 3,
                    include_siblings = false,
                    include_header = false,
                },
            }));

            var withoutHeaders = await handler.ExecuteAsync(
                argsWithoutHeaders.RootElement,
                new KernelToolCallContext("thread", "turn", root),
                CancellationToken.None);
            Assert.True(withoutHeaders.Success);
            Assert.Equal(
                "L3:     int run() const {\nL4:         switch (mode_) {\nL5:             case 1:\nL6:                 return 1;\nL7:         }\nL8:     }",
                withoutHeaders.OutputText);
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
