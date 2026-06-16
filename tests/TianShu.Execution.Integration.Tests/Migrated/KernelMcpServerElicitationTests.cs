using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.Execution.Integration.Tests;

public sealed class AppHostMcpServerElicitationTests
{
    [Fact]
    public async Task RequestMcpServerElicitationAsync_ShouldWriteFormRequestAndParseAcceptedResponse()
    {
        var root = CreateTempDirectory();
        try
        {
            var server = CreateServer(root, out var writer);
            var requestedSchema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    confirmed = new { type = "boolean" },
                },
                required = new[] { "confirmed" },
            });

            var task = server.RequestMcpServerElicitationAsync(
                threadId: "thread_mcp_elicitation_001",
                turnId: "turn_mcp_elicitation_001",
                new McpServerElicitationRequest(
                    ServerName: "calendar",
                    Mode: "form",
                    Message: "Allow this request?",
                    RequestedSchema: requestedSchema),
                CancellationToken.None);

            var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");
            var pendingRequest = await WaitForSinglePendingServerRequestAsync(pending, TimeSpan.FromSeconds(5));
            pendingRequest.Value.TrySetResult(JsonSerializer.SerializeToElement(new
            {
                action = "accept",
                content = new
                {
                    confirmed = true,
                },
            }));

            var result = await task;
            Assert.Equal("accept", result.Action);
            Assert.True(result.Content.HasValue);
            Assert.True(result.Content.Value.TryGetProperty("confirmed", out var confirmed));
            Assert.True(confirmed.GetBoolean());

            var messages = ParseMessages(writer);
            try
            {
                var request = Assert.Single(messages.Where(static x => IsRequestMethod(x.RootElement, "mcpServer/elicitation/request")));
                var parameters = request.RootElement.GetProperty("params");
                Assert.Equal("thread_mcp_elicitation_001", parameters.GetProperty("threadId").GetString());
                Assert.Equal("turn_mcp_elicitation_001", parameters.GetProperty("turnId").GetString());
                Assert.Equal("calendar", parameters.GetProperty("serverName").GetString());
                Assert.Equal("form", parameters.GetProperty("mode").GetString());
                Assert.Equal("Allow this request?", parameters.GetProperty("message").GetString());
                Assert.True(parameters.TryGetProperty("requestedSchema", out var schema));
                Assert.Equal(JsonValueKind.Object, schema.ValueKind);
            }
            finally
            {
                DisposeAll(messages);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RequestMcpServerElicitationAsync_ShouldFallbackToDeclineOnMalformedResponse()
    {
        var root = CreateTempDirectory();
        try
        {
            var server = CreateServer(root, out _);
            var task = server.RequestMcpServerElicitationAsync(
                threadId: "thread_mcp_elicitation_002",
                turnId: "turn_mcp_elicitation_002",
                new McpServerElicitationRequest(
                    ServerName: "calendar",
                    Mode: "url",
                    Message: "Open the confirmation page.",
                    Url: "https://example.test/confirm",
                    ElicitationId: "elicitation-001"),
                CancellationToken.None);

            var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");
            var pendingRequest = await WaitForSinglePendingServerRequestAsync(pending, TimeSpan.FromSeconds(5));
            pendingRequest.Value.TrySetResult(JsonSerializer.SerializeToElement(new
            {
                unexpected = true,
            }));

            var result = await task;
            Assert.Equal("decline", result.Action);
            Assert.False(result.Content.HasValue);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteToolCallAsync_ShouldBridgeMcpServerElicitationThroughTestSyncTool()
    {
        var root = CreateTempDirectory();
        try
        {
            var server = CreateServer(root, out var writer);
            var execTask = server.ExecuteToolCallAsync(
                threadId: "thread_mcp_elicitation_tool_001",
                turnId: "turn_mcp_elicitation_tool_001",
                itemId: "tool_mcp_elicitation_001",
                toolName: "test_sync_tool",
                arguments: JsonSerializer.SerializeToElement(new
                {
                    elicitation = new
                    {
                        server_name = "calendar",
                        mode = "form",
                        message = "Allow this request?",
                        requested_schema = new
                        {
                            type = "object",
                            properties = new
                            {
                                confirmed = new { type = "boolean" },
                            },
                            required = new[] { "confirmed" },
                        },
                    },
                }),
                context: new TurnRequestContext(
                    Model: null,
                    ModelProvider: null,
                    ServiceTier: null,
                    ApprovalPolicy: "on-request",
                    SandboxPolicy: null,
                    SandboxMode: null,
                    Cwd: root,
                    ProviderBaseUrl: null,
                    ProviderApiKeyEnvironmentVariable: null,
                    ProviderWireApi: null,
                    IsReview: false,
                    ReviewDisplayText: null),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");
            var pendingRequest = await WaitForSinglePendingServerRequestAsync(pending, TimeSpan.FromSeconds(5));
            pendingRequest.Value.TrySetResult(JsonSerializer.SerializeToElement(new
            {
                action = "accept",
                content = new
                {
                    confirmed = true,
                },
            }));

            var result = await execTask;
            Assert.True(result.Success);
            Assert.Contains("\"action\":\"accept\"", result.OutputText, StringComparison.Ordinal);
            Assert.Contains("\"confirmed\":true", result.OutputText, StringComparison.Ordinal);

            var messages = ParseMessages(writer);
            try
            {
                Assert.Contains(messages, static x => IsRequestMethod(x.RootElement, "mcpServer/elicitation/request"));
                Assert.Contains(messages, static x => IsNotificationMethod(x.RootElement, "item/tool/call"));
            }
            finally
            {
                DisposeAll(messages);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RequestMcpServerElicitationAsync_ShouldCanonicalizeMultiSelectSchemaAliases()
    {
        var root = CreateTempDirectory();
        try
        {
            var server = CreateServer(root, out var writer);
            using var schemaDocument = JsonDocument.Parse(
                """
                {
                  "type": "object",
                  "properties": {
                    "scopes": {
                      "type": "array",
                      "items": {
                        "oneOf": [
                          { "const": "read", "title": "Read" },
                          { "const": "write", "title": "Write" }
                        ]
                      }
                    }
                  }
                }
                """);

            var task = server.RequestMcpServerElicitationAsync(
                threadId: "thread_mcp_elicitation_003",
                turnId: "turn_mcp_elicitation_003",
                new McpServerElicitationRequest(
                    ServerName: "calendar",
                    Mode: "form",
                    Message: "Choose scopes.",
                    RequestedSchema: schemaDocument.RootElement.Clone()),
                CancellationToken.None);

            var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");
            var pendingRequest = await WaitForSinglePendingServerRequestAsync(pending, TimeSpan.FromSeconds(5));

            var messages = ParseMessages(writer);
            try
            {
                var request = Assert.Single(messages.Where(static x => IsRequestMethod(x.RootElement, "mcpServer/elicitation/request")));
                var requestedSchema = request.RootElement
                    .GetProperty("params")
                    .GetProperty("requestedSchema")
                    .GetProperty("properties")
                    .GetProperty("scopes")
                    .GetProperty("items");

                Assert.True(requestedSchema.TryGetProperty("anyOf", out var anyOf));
                Assert.False(requestedSchema.TryGetProperty("oneOf", out _));
                Assert.Equal("read", anyOf[0].GetProperty("const").GetString());
            }
            finally
            {
                DisposeAll(messages);
            }

            pendingRequest.Value.TrySetResult(JsonSerializer.SerializeToElement(new { action = "decline" }));
            var result = await task;
            Assert.Equal("decline", result.Action);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RequestMcpServerElicitationAsync_ShouldRejectInvalidRequestedSchemaBeforeSendingRequest()
    {
        var root = CreateTempDirectory();
        try
        {
            var server = CreateServer(root, out _);
            using var schemaDocument = JsonDocument.Parse(
                """
                {
                  "type": "object",
                  "properties": {
                    "confirmed": {
                      "type": "object"
                    }
                  }
                }
                """);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                server.RequestMcpServerElicitationAsync(
                    threadId: "thread_mcp_elicitation_004",
                    turnId: "turn_mcp_elicitation_004",
                    new McpServerElicitationRequest(
                        ServerName: "calendar",
                        Mode: "form",
                        Message: "Allow this request?",
                        RequestedSchema: schemaDocument.RootElement.Clone()),
                    CancellationToken.None));

            Assert.Contains("requested_schema.properties.confirmed.type", ex.Message, StringComparison.Ordinal);

            var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");
            Assert.Empty(pending);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }
    private static AppHostServer CreateServer(string root, out StringWriter writer)
    {
        var storePath = Path.Combine(root, "threads.json");
        var threadStore = new KernelThreadStore(storePath);
        writer = new StringWriter();
        return new AppHostServer(new StringReader(string.Empty), writer, threadStore);
    }

    private static JsonDocument[] ParseMessages(StringWriter writer)
    {
        return writer
            .ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static line => JsonDocument.Parse(line))
            .ToArray();
    }

    private static void DisposeAll(IEnumerable<JsonDocument> documents)
    {
        foreach (var document in documents)
        {
            document.Dispose();
        }
    }

    private static bool IsNotificationMethod(JsonElement json, string method)
    {
        return json.TryGetProperty("method", out var methodElement)
               && methodElement.ValueKind == JsonValueKind.String
               && string.Equals(methodElement.GetString(), method, StringComparison.Ordinal)
               && !json.TryGetProperty("id", out _);
    }

    private static bool IsRequestMethod(JsonElement json, string method)
    {
        return json.TryGetProperty("method", out var methodElement)
               && methodElement.ValueKind == JsonValueKind.String
               && string.Equals(methodElement.GetString(), method, StringComparison.Ordinal)
               && json.TryGetProperty("id", out _);
    }

    private static async Task<KeyValuePair<long, TaskCompletionSource<JsonElement>>> WaitForSinglePendingServerRequestAsync(
        ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pending,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow <= deadline)
        {
            if (pending.Count == 1)
            {
                return pending.Single();
            }

            await Task.Delay(20).ConfigureAwait(false);
        }

        throw new TimeoutException("Timed out waiting for a pending server request.");
    }

    private static T GetPrivateField<T>(object instance, string name)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (T)field!.GetValue(instance)!;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "TianShuKernelTests", Guid.NewGuid().ToString("N"));
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



