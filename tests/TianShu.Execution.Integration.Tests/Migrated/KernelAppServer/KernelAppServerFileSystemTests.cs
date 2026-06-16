using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.Execution.Integration.Tests;

[Collection("EnvironmentVariables")]
public sealed class AppHostServerFileSystemTests
{
    [Fact]
    public async Task RunAsync_ShouldSupportFsSurface()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var sourceDir = Path.Combine(root, "source");
        var nestedDir = Path.Combine(sourceDir, "nested");
        var nestedFile = Path.Combine(nestedDir, "note.txt");
        var rootFile = Path.Combine(sourceDir, "root.txt");
        var copiedDir = Path.Combine(root, "copied");
        var copiedNestedFile = Path.Combine(copiedDir, "nested", "note.txt");
        var copiedFile = Path.Combine(root, "copy.txt");
        var nestedPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes("hello from app-server"));
        var rootPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes("hello from source root"));
        var input = string.Join(
            Environment.NewLine,
            SerializeRequest(1, "initialize", new { }),
            SerializeRequest(2, "fs/createDirectory", new { path = ToJsonPath(nestedDir) }),
            SerializeRequest(3, "fs/writeFile", new { path = ToJsonPath(nestedFile), dataBase64 = nestedPayload }),
            SerializeRequest(4, "fs/writeFile", new { path = ToJsonPath(rootFile), dataBase64 = rootPayload }),
            SerializeRequest(5, "fs/readFile", new { path = ToJsonPath(nestedFile) }),
            SerializeRequest(6, "fs/copy", new { sourcePath = ToJsonPath(nestedFile), destinationPath = ToJsonPath(copiedFile), recursive = false }),
            SerializeRequest(7, "fs/copy", new { sourcePath = ToJsonPath(sourceDir), destinationPath = ToJsonPath(copiedDir), recursive = true }),
            SerializeRequest(8, "fs/readDirectory", new { path = ToJsonPath(sourceDir) }),
            SerializeRequest(9, "fs/getMetadata", new { path = ToJsonPath(nestedFile) }),
            SerializeRequest(10, "fs/readFile", new { path = ToJsonPath(copiedNestedFile) }),
            SerializeRequest(11, "fs/remove", new { path = ToJsonPath(copiedDir) }));

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(input);
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var readResult = messages.Single(x => IsResponseId(x.RootElement, 5)).RootElement.GetProperty("result");
                Assert.Equal(nestedPayload, readResult.GetProperty("dataBase64").GetString());

                var directoryEntries = messages.Single(x => IsResponseId(x.RootElement, 8))
                    .RootElement
                    .GetProperty("result")
                    .GetProperty("entries")
                    .EnumerateArray()
                    .Select(static entry => new
                    {
                        FileName = entry.GetProperty("fileName").GetString(),
                        IsDirectory = entry.GetProperty("isDirectory").GetBoolean(),
                        IsFile = entry.GetProperty("isFile").GetBoolean(),
                    })
                    .OrderBy(static entry => entry.FileName, StringComparer.Ordinal)
                    .ToArray();
                Assert.Equal(
                    ["nested", "root.txt"],
                    directoryEntries.Select(static entry => entry.FileName).ToArray());
                Assert.Equal(
                    [true, false],
                    directoryEntries.Select(static entry => entry.IsDirectory).ToArray());
                Assert.Equal(
                    [false, true],
                    directoryEntries.Select(static entry => entry.IsFile).ToArray());

                var metadata = messages.Single(x => IsResponseId(x.RootElement, 9)).RootElement.GetProperty("result");
                Assert.False(metadata.GetProperty("isDirectory").GetBoolean());
                Assert.True(metadata.GetProperty("isFile").GetBoolean());
                Assert.True(metadata.GetProperty("modifiedAtMs").GetInt64() > 0);

                var copiedReadResult = messages.Single(x => IsResponseId(x.RootElement, 10)).RootElement.GetProperty("result");
                Assert.Equal(nestedPayload, copiedReadResult.GetProperty("dataBase64").GetString());

                Assert.Equal("hello from app-server", await File.ReadAllTextAsync(copiedFile));
                Assert.False(Directory.Exists(copiedDir));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRejectRelativeFsPaths()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var absoluteFile = Path.Combine(root, "absolute.txt");
        await File.WriteAllTextAsync(absoluteFile, "hello");
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("hello"));
        var input = string.Join(
            Environment.NewLine,
            SerializeRequest(1, "initialize", new { }),
            SerializeRequest(2, "fs/readFile", new { path = "relative.txt" }),
            SerializeRequest(3, "fs/writeFile", new { path = "relative.txt", dataBase64 = payload }),
            SerializeRequest(4, "fs/createDirectory", new { path = "relative-dir" }),
            SerializeRequest(5, "fs/getMetadata", new { path = "relative.txt" }),
            SerializeRequest(6, "fs/readDirectory", new { path = "relative-dir" }),
            SerializeRequest(7, "fs/remove", new { path = "relative.txt" }),
            SerializeRequest(8, "fs/copy", new { sourcePath = "relative.txt", destinationPath = ToJsonPath(absoluteFile) }),
            SerializeRequest(9, "fs/copy", new { sourcePath = ToJsonPath(absoluteFile), destinationPath = "relative-copy.txt" }),
            SerializeRequest(10, "fs/watch", new { path = "relative-watch" }));

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(input);
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                foreach (var responseId in Enumerable.Range(2, 9))
                {
                    var error = messages.Single(x => IsResponseId(x.RootElement, responseId)).RootElement.GetProperty("error");
                    Assert.Equal("Invalid request: AbsolutePathBuf deserialized without a base path", error.GetProperty("message").GetString());
                }
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldEmitFsChangedNotification_And_UnwatchShouldStopFutureNotifications()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var gitDir = Path.Combine(root, "repo", ".git");
        var fetchHead = Path.Combine(gitDir, "FETCH_HEAD");
        var packedRefs = Path.Combine(gitDir, "packed-refs");
        Directory.CreateDirectory(gitDir);
        await File.WriteAllTextAsync(fetchHead, "old");

        var inputChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });

        var reader = new ChannelTextReader(inputChannel.Reader);
        using var writer = new ChannelTextWriter();
        var threadStore = new KernelThreadStore(storePath);
        var server = new AppHostServer(reader, writer, threadStore);
        var runTask = server.RunAsync(CancellationToken.None);
        await KernelAppServerTestProtocol.InitializeAsync(inputChannel.Writer, writer.Lines, TimeSpan.FromSeconds(5));

        try
        {
            var watchRequest = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "fs/watch",
                @params = new
                {
                    path = ToJsonPath(gitDir),
                },
            });
            Assert.True(inputChannel.Writer.TryWrite(watchRequest));

            var watchResponseLine = await WaitForJsonRpcResponseIdAsync(writer.Lines, 1, TimeSpan.FromSeconds(5));
            using var watchResponse = JsonDocument.Parse(watchResponseLine);
            var watchResult = watchResponse.RootElement.GetProperty("result");
            var watchId = watchResult.GetProperty("watchId").GetString();
            Assert.False(string.IsNullOrWhiteSpace(watchId));
            Assert.Equal(Path.GetFullPath(gitDir), watchResult.GetProperty("path").GetString());

            await File.WriteAllTextAsync(fetchHead, "updated");

            var changedLine = await WaitForJsonRpcMethodAsync(writer.Lines, "fs/changed", TimeSpan.FromSeconds(5));
            using var changed = JsonDocument.Parse(changedLine);
            var changedParams = changed.RootElement.GetProperty("params");
            Assert.Equal(watchId, changedParams.GetProperty("watchId").GetString());
            var changedPaths = changedParams.GetProperty("changedPaths").EnumerateArray().Select(static x => x.GetString()).ToArray();
            Assert.Contains(Path.GetFullPath(fetchHead), changedPaths);

            var unwatchRequest = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "fs/unwatch",
                @params = new
                {
                    watchId,
                },
            });
            Assert.True(inputChannel.Writer.TryWrite(unwatchRequest));
            _ = await WaitForJsonRpcResponseIdAsync(writer.Lines, 2, TimeSpan.FromSeconds(5));

            await File.WriteAllTextAsync(packedRefs, "refs");
            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await WaitForJsonRpcMethodAsync(writer.Lines, "fs/changed", TimeSpan.FromMilliseconds(1200)));
        }
        finally
        {
            inputChannel.Writer.TryComplete();
            await runTask.WaitAsync(TimeSpan.FromSeconds(10));
            DeleteDirectory(root);
        }
    }

    private static string ToJsonPath(string path)
        => Path.GetFullPath(path).Replace("\\", "/", StringComparison.Ordinal);

    private static string SerializeRequest(int id, string method, object parameters)
        => JsonSerializer.Serialize(new
        {
            id,
            method,
            @params = parameters,
        });

    private static bool IsResponseId(JsonElement json, int id)
        => json.TryGetProperty("id", out var idElement)
           && idElement.ValueKind == JsonValueKind.Number
           && idElement.TryGetInt32(out var parsed)
           && parsed == id;

    private static async Task<string> WaitForJsonRpcMethodAsync(ChannelReader<string> lines, string method, TimeSpan timeout)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        try
        {
            while (await lines.WaitToReadAsync(timeoutCts.Token))
            {
                while (lines.TryRead(out var line))
                {
                    using var doc = JsonDocument.Parse(line);
                    if (doc.RootElement.TryGetProperty("method", out var methodElement)
                        && string.Equals(methodElement.GetString(), method, StringComparison.Ordinal))
                    {
                        return line;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException($"未等到 method={method} 的 JSON-RPC 消息。");
        }

        throw new TimeoutException($"未等到 method={method} 的 JSON-RPC 消息。");
    }

    private static async Task<string> WaitForJsonRpcResponseIdAsync(ChannelReader<string> lines, long id, TimeSpan timeout)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        try
        {
            while (await lines.WaitToReadAsync(timeoutCts.Token))
            {
                while (lines.TryRead(out var line))
                {
                    using var doc = JsonDocument.Parse(line);
                    if (doc.RootElement.TryGetProperty("id", out var idElement)
                        && idElement.ValueKind == JsonValueKind.Number
                        && idElement.TryGetInt64(out var parsed)
                        && parsed == id)
                    {
                        return line;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException($"未等到 id={id} 的 JSON-RPC response。");
        }

        throw new TimeoutException($"未等到 id={id} 的 JSON-RPC response。");
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "TianShuKernelFsTests", Guid.NewGuid().ToString("N"));
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

    private sealed class ChannelTextReader(ChannelReader<string> source) : TextReader
    {
        public override async Task<string?> ReadLineAsync()
        {
            while (await source.WaitToReadAsync())
            {
                if (source.TryRead(out var line))
                {
                    return line;
                }
            }

            return null;
        }
    }

    private sealed class ChannelTextWriter : TextWriter
    {
        private readonly Channel<string> lines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });
        private readonly StringBuilder capturedText = new();
        private bool disposed;

        public ChannelReader<string> Lines => lines.Reader;

        public override Encoding Encoding => Encoding.UTF8;

        public override Task WriteLineAsync(string? value)
        {
            if (disposed)
            {
                return Task.CompletedTask;
            }

            var line = value ?? string.Empty;
            capturedText.AppendLine(line);
            lines.Writer.TryWrite(line);
            return Task.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing || disposed)
            {
                base.Dispose(disposing);
                return;
            }

            disposed = true;
            lines.Writer.TryComplete();
            base.Dispose(disposing);
        }
    }
}
