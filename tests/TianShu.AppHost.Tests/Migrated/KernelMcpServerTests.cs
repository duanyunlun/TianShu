using System.Text.Json;
using TianShu.AppHost;

namespace TianShu.AppHost.Tests;

public sealed class AppHostMcpServerTests
{
    [Fact]
    public async Task RunAsync_ShouldRejectRequestsBeforeInitialize_WithoutCrashing()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            var reader = new StringReader("""{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""");
            var writer = new StringWriter();
            var server = new AppHostMcpServer(reader, writer, new KernelThreadStore(storePath));

            await server.RunAsync(CancellationToken.None);

            var message = ParseMessages(writer).Single();
            try
            {
                Assert.Equal("2.0", message.RootElement.GetProperty("jsonrpc").GetString());
                Assert.Equal(1, message.RootElement.GetProperty("id").GetInt32());
                var error = message.RootElement.GetProperty("error");
                Assert.Equal(-32600, error.GetProperty("code").GetInt32());
                Assert.Equal("Not initialized", error.GetProperty("message").GetString());
            }
            finally
            {
                message.Dispose();
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldListToolsThroughOuterMcpServer()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var input = string.Join(
            Environment.NewLine,
            KernelAppServerTestProtocol.CreateInitializeRequest(experimentalApi: true),
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
            """{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""");

        try
        {
            var reader = new StringReader(input);
            var writer = new StringWriter();
            var server = new AppHostMcpServer(reader, writer, new KernelThreadStore(storePath));

            await server.RunAsync(CancellationToken.None);

            var messages = ParseMessages(writer);
            try
            {
                Assert.All(messages, static message => Assert.Equal("2.0", message.RootElement.GetProperty("jsonrpc").GetString()));

                var initializeResponse = messages.Single(static x => IsResponseId(x.RootElement, 0));
                Assert.Equal("2025-06-18", initializeResponse.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());

                var toolListResponse = messages.Single(static x => IsResponseId(x.RootElement, 1));
                var tools = toolListResponse.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().ToArray();
                Assert.Equal(2, tools.Length);
                Assert.Contains(tools, static item => item.GetProperty("name").GetString() == "tianshu");
                Assert.Contains(tools, static item => item.GetProperty("name").GetString() == "tianshu-reply");
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
    public async Task RunAsync_ShouldHandleTianShuToolCallThroughOuterMcpServer()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var input = string.Join(
            Environment.NewLine,
            KernelAppServerTestProtocol.CreateInitializeRequest(experimentalApi: true),
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"tianshu","arguments":{"prompt":"/tool test_sync_tool {}"}}}""");

        try
        {
            var reader = new StringReader(input);
            var writer = new StringWriter();
            var server = new AppHostMcpServer(reader, writer, new KernelThreadStore(storePath));

            await server.RunAsync(CancellationToken.None);

            var messages = ParseMessages(writer);
            try
            {
                Assert.All(messages, static message => Assert.Equal("2.0", message.RootElement.GetProperty("jsonrpc").GetString()));

                var response = messages.Single(static x => IsResponseId(x.RootElement, 1));
                var result = response.RootElement.GetProperty("result");
                Assert.False(result.GetProperty("isError").GetBoolean());
                Assert.Equal("ok", result.GetProperty("content")[0].GetProperty("text").GetString());
                Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("structuredContent").GetProperty("threadId").GetString()));
                Assert.Contains(messages, static x => IsNotificationMethod(x.RootElement, "turn/completed"));
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

    private static bool IsNotificationMethod(JsonElement json, string method)
    {
        return json.TryGetProperty("method", out var methodElement)
               && string.Equals(methodElement.GetString(), method, StringComparison.Ordinal);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "TianShuAppHostMcpServerTests", Guid.NewGuid().ToString("N"));
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
