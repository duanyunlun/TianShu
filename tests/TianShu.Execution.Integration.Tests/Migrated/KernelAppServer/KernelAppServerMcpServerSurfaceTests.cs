using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.Execution.Integration.Tests;

public sealed class AppHostServerMcpServerSurfaceTests
{
    [Fact]
    public async Task RunAsync_ShouldListMcpServerTools()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize("""{"id":1,"method":"mcpServer/tools/list","params":{}}"""));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, new KernelThreadStore(storePath));

            await server.RunAsync(CancellationToken.None);

            var messages = ParseMessages(writer);
            try
            {
                var response = messages.Single(static x => IsResponseId(x.RootElement, 1));
                var result = response.RootElement.GetProperty("result");
                Assert.Equal(JsonValueKind.Null, result.GetProperty("nextCursor").ValueKind);

                var tools = result.GetProperty("tools").EnumerateArray().ToArray();
                Assert.Equal(2, tools.Length);

                var tianShu = tools.Single(static item => item.GetProperty("name").GetString() == "tianshu");
                var tianShuReply = tools.Single(static item => item.GetProperty("name").GetString() == "tianshu-reply");

                var tianShuOutputRequired = tianShu.GetProperty("outputSchema").GetProperty("required").EnumerateArray().Select(static item => item.GetString()).ToArray();
                var tianShuReplyOutputRequired = tianShuReply.GetProperty("outputSchema").GetProperty("required").EnumerateArray().Select(static item => item.GetString()).ToArray();

                Assert.Contains("threadId", tianShuOutputRequired);
                Assert.Contains("content", tianShuOutputRequired);
                Assert.Contains("threadId", tianShuReplyOutputRequired);
                Assert.Contains("content", tianShuReplyOutputRequired);
            }
            finally
            {
                DisposeDocuments(messages);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldHandleMcpServerTianShuToolCall()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string input = """{"id":1,"method":"mcpServer/tools/call","params":{"name":"tianshu","arguments":{"prompt":"/tool test_sync_tool {}"}}}""";

        try
        {
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, new KernelThreadStore(storePath));

            var runTask = server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));
            await runTask;

            var messages = ParseMessages(writer);
            try
            {
                var response = messages.Single(static x => IsResponseId(x.RootElement, 1));
                var result = response.RootElement.GetProperty("result");
                Assert.False(result.GetProperty("isError").GetBoolean());
                Assert.Equal("ok", result.GetProperty("content")[0].GetProperty("text").GetString());

                var structuredContent = result.GetProperty("structuredContent");
                var threadId = structuredContent.GetProperty("threadId").GetString();
                Assert.False(string.IsNullOrWhiteSpace(threadId));
                Assert.Equal("ok", structuredContent.GetProperty("content").GetString());

                var threadStore = new KernelThreadStore(storePath);
                await threadStore.InitializeAsync(CancellationToken.None);
                var persisted = await threadStore.GetThreadAsync(threadId!, CancellationToken.None);
                Assert.NotNull(persisted);
                Assert.Equal(threadId, persisted!.Id);
            }
            finally
            {
                DisposeDocuments(messages);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldHandleMcpServerTianShuReplyToolCall()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            var firstWriter = new StringWriter();
            var firstServer = new AppHostServer(
                new StringReader(KernelAppServerTestProtocol.WithInitialize("""{"id":1,"method":"mcpServer/tools/call","params":{"name":"tianshu","arguments":{"prompt":"/tool test_sync_tool {}"}}}""")),
                firstWriter,
                new KernelThreadStore(storePath));

            var firstRunTask = firstServer.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(firstWriter, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));
            await firstRunTask;

            var firstMessages = ParseMessages(firstWriter);
            string? threadId;
            try
            {
                var firstResponse = firstMessages.Single(static x => IsResponseId(x.RootElement, 1));
                threadId = firstResponse.RootElement
                    .GetProperty("result")
                    .GetProperty("structuredContent")
                    .GetProperty("threadId")
                    .GetString();
                Assert.False(string.IsNullOrWhiteSpace(threadId));
            }
            finally
            {
                DisposeDocuments(firstMessages);
            }

            var secondWriter = new StringWriter();
            var secondInput = $"{{\"id\":2,\"method\":\"mcpServer/tools/call\",\"params\":{{\"name\":\"tianshu-reply\",\"arguments\":{{\"threadId\":\"{threadId}\",\"prompt\":\"/tool test_sync_tool {{}}\"}}}}}}";
            var secondServer = new AppHostServer(
                new StringReader(KernelAppServerTestProtocol.WithInitialize(secondInput)),
                secondWriter,
                new KernelThreadStore(storePath));

            var secondRunTask = secondServer.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(secondWriter, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));
            await secondRunTask;

            var secondMessages = ParseMessages(secondWriter);
            try
            {
                var secondResponse = secondMessages.Single(static x => IsResponseId(x.RootElement, 2));
                var secondResult = secondResponse.RootElement.GetProperty("result");
                Assert.False(secondResult.GetProperty("isError").GetBoolean());
                Assert.Equal("ok", secondResult.GetProperty("content")[0].GetProperty("text").GetString());
                Assert.Equal(threadId, secondResult.GetProperty("structuredContent").GetProperty("threadId").GetString());
                Assert.Equal("ok", secondResult.GetProperty("structuredContent").GetProperty("content").GetString());
            }
            finally
            {
                DisposeDocuments(secondMessages);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldReturnErrorWhenMcpServerTianShuReplyThreadMissing()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string missingThreadId = "thread_missing_001";

        try
        {
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize($"{{\"id\":1,\"method\":\"mcpServer/tools/call\",\"params\":{{\"name\":\"tianshu-reply\",\"arguments\":{{\"threadId\":\"{missingThreadId}\",\"prompt\":\"hello\"}}}}}}"));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, new KernelThreadStore(storePath));

            await server.RunAsync(CancellationToken.None);

            var messages = ParseMessages(writer);
            try
            {
                var response = messages.Single(static x => IsResponseId(x.RootElement, 1));
                var result = response.RootElement.GetProperty("result");
                Assert.True(result.GetProperty("isError").GetBoolean());
                Assert.Equal($"Session not found for thread_id: {missingThreadId}", result.GetProperty("content")[0].GetProperty("text").GetString());
                Assert.Equal(missingThreadId, result.GetProperty("structuredContent").GetProperty("threadId").GetString());
                Assert.Equal($"Session not found for thread_id: {missingThreadId}", result.GetProperty("structuredContent").GetProperty("content").GetString());
            }
            finally
            {
                DisposeDocuments(messages);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static JsonDocument[] ParseMessages(StringWriter writer)
    {
        return writer
            .ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static line => JsonDocument.Parse(line))
            .ToArray();
    }

    private static void DisposeDocuments(IEnumerable<JsonDocument> documents)
    {
        foreach (var document in documents)
        {
            document.Dispose();
        }
    }

    private static bool IsResponseId(JsonElement json, int id)
    {
        if (!json.TryGetProperty("id", out var idElement))
        {
            return false;
        }

        return idElement.ValueKind == JsonValueKind.Number
               && idElement.TryGetInt32(out var value)
               && value == id;
    }

    private static async Task WaitForWriterContainsAsync(StringWriter writer, string expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (writer.ToString().Contains(expected, StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(100);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "TianShuAppHostMcpServerSurfaceTests", Guid.NewGuid().ToString("N"));
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
