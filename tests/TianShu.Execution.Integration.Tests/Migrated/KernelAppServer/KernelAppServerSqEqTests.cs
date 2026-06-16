using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.Execution.Integration.Tests;

[Collection("EnvironmentVariables")]
public sealed class AppHostServerSqEqTests
{
    [Fact]
    public async Task RunAsync_ShouldInvokeDynamicToolFromThreadSession()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        var inputChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });

        var reader = new ChannelTextReader(inputChannel.Reader);
        var writer = new ChannelTextWriter();
        var threadStore = new KernelThreadStore(storePath);
        var server = new AppHostServer(reader, writer, threadStore);
        var runTask = server.RunAsync(CancellationToken.None);
        await KernelAppServerTestProtocol.InitializeAsync(inputChannel.Writer, writer.Lines, TimeSpan.FromSeconds(5));

        try
        {
            var startRequest = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "thread/start",
                @params = new
                {
                    cwd = root.Replace("\\", "/"),
                    dynamicTools = new object[]
                    {
                        new
                        {
                            name = "toolA",
                            description = "demo",
                            inputSchema = new
                            {
                                type = "object",
                                properties = new
                                {
                                    value = new { type = "string" },
                                },
                            },
                        },
                    },
                },
            });
            Assert.True(inputChannel.Writer.TryWrite(startRequest));

            var startResponseLine = await WaitForJsonRpcResponseIdAsync(writer.Lines, 1, TimeSpan.FromSeconds(5));
            using var startResponse = JsonDocument.Parse(startResponseLine);
            var threadId = startResponse.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(threadId));

            var turnRequest = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "/tool toolA {\"value\":\"hello\"}" },
                    },
                },
            });
            Assert.True(inputChannel.Writer.TryWrite(turnRequest));

            var dynamicToolRequestLine = await WaitForJsonRpcRequestMethodAsync(writer.Lines, "item/tool/call", TimeSpan.FromSeconds(5));
            using var dynamicToolRequest = JsonDocument.Parse(dynamicToolRequestLine);
            Assert.False(dynamicToolRequest.RootElement.TryGetProperty("jsonrpc", out _));
            var dynamicToolRequestId = dynamicToolRequest.RootElement.GetProperty("id").GetInt64();
            var dynamicToolParams = dynamicToolRequest.RootElement.GetProperty("params");
            Assert.Equal(threadId, dynamicToolParams.GetProperty("threadId").GetString());
            Assert.False(string.IsNullOrWhiteSpace(dynamicToolParams.GetProperty("turnId").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(dynamicToolParams.GetProperty("callId").GetString()));
            Assert.Equal("toolA", dynamicToolParams.GetProperty("tool").GetString());
            Assert.Equal("hello", dynamicToolParams.GetProperty("arguments").GetProperty("value").GetString());
            Assert.False(dynamicToolParams.TryGetProperty("itemId", out _));
            Assert.False(dynamicToolParams.TryGetProperty("toolName", out _));

            var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");
            Assert.True(pending.TryGetValue(dynamicToolRequestId, out var pendingRequest));
            Assert.NotNull(pendingRequest);
            pendingRequest!.TrySetResult(JsonSerializer.SerializeToElement(new
            {
                success = true,
                contentItems = new object[]
                {
                    new { type = "inputText", text = "dynamic-ok" },
                },
            }));

            inputChannel.Writer.TryComplete();
            await runTask.WaitAsync(TimeSpan.FromSeconds(10));
            var output = writer.CapturedText.ToString();
            var turnResponseLine = FindJsonRpcMessageById(output, 2);
            using var turnResponse = JsonDocument.Parse(turnResponseLine);
            Assert.True(turnResponse.RootElement.TryGetProperty("result", out _));
            Assert.Contains("dynamic-ok", output, StringComparison.Ordinal);
            Assert.Contains("\"method\":\"item/tool/call\"", output, StringComparison.Ordinal);
            Assert.DoesNotContain("item/tool/invokeDynamic", output, StringComparison.Ordinal);
            var docs = output
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var started = Assert.Single(docs.Where(static doc =>
                    doc.RootElement.TryGetProperty("method", out var method)
                    && method.GetString() == "item/started"
                    && doc.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "dynamicToolCall"));
                var startedItem = started.RootElement.GetProperty("params").GetProperty("item");
                Assert.Equal("toolA", startedItem.GetProperty("tool").GetString());
                Assert.Equal("inProgress", startedItem.GetProperty("status").GetString());
                Assert.Equal("hello", startedItem.GetProperty("arguments").GetProperty("value").GetString());

                var completed = Assert.Single(docs.Where(static doc =>
                    doc.RootElement.TryGetProperty("method", out var method)
                    && method.GetString() == "item/completed"
                    && doc.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "dynamicToolCall"));
                var completedItem = completed.RootElement.GetProperty("params").GetProperty("item");
                Assert.Equal("toolA", completedItem.GetProperty("tool").GetString());
                Assert.Equal("completed", completedItem.GetProperty("status").GetString());
                Assert.True(completedItem.GetProperty("success").GetBoolean());
                Assert.Equal("inputText", completedItem.GetProperty("contentItems")[0].GetProperty("type").GetString());
                Assert.Equal("dynamic-ok", completedItem.GetProperty("contentItems")[0].GetProperty("text").GetString());
            }
            finally
            {
                foreach (var doc in docs)
                {
                    doc.Dispose();
                }
            }


        }
        finally
        {
            inputChannel.Writer.TryComplete();
            DeleteDirectory(root);
            writer.Dispose();
        }
    }
    [Fact]
    public async Task RunAsync_ShouldNotAbortTurnWhenDynamicToolReturnsFailure()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        var inputChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });

        var reader = new ChannelTextReader(inputChannel.Reader);
        var writer = new ChannelTextWriter();
        var threadStore = new KernelThreadStore(storePath);
        var server = new AppHostServer(reader, writer, threadStore);
        var runTask = server.RunAsync(CancellationToken.None);
        await KernelAppServerTestProtocol.InitializeAsync(inputChannel.Writer, writer.Lines, TimeSpan.FromSeconds(5));

        try
        {
            var startRequest = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "thread/start",
                @params = new
                {
                    cwd = root.Replace("\\", "/"),
                    dynamicTools = new object[]
                    {
                        new
                        {
                            name = "toolFail",
                            description = "demo",
                            inputSchema = new { type = "object" },
                        },
                    },
                },
            });
            Assert.True(inputChannel.Writer.TryWrite(startRequest));

            var startResponseLine = await WaitForJsonRpcResponseIdAsync(writer.Lines, 1, TimeSpan.FromSeconds(5));
            using var startResponse = JsonDocument.Parse(startResponseLine);
            var threadId = startResponse.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(threadId));

            var turnRequest = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[] { new { text = "/tool toolFail {}" } },
                },
            });
            Assert.True(inputChannel.Writer.TryWrite(turnRequest));

            var dynamicToolRequestLine = await WaitForJsonRpcRequestMethodAsync(writer.Lines, "item/tool/call", TimeSpan.FromSeconds(5));
            using var dynamicToolRequest = JsonDocument.Parse(dynamicToolRequestLine);
            Assert.False(dynamicToolRequest.RootElement.TryGetProperty("jsonrpc", out _));
            var dynamicToolRequestId = dynamicToolRequest.RootElement.GetProperty("id").GetInt64();
            var dynamicToolParams = dynamicToolRequest.RootElement.GetProperty("params");
            Assert.Equal(threadId, dynamicToolParams.GetProperty("threadId").GetString());
            Assert.False(string.IsNullOrWhiteSpace(dynamicToolParams.GetProperty("turnId").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(dynamicToolParams.GetProperty("callId").GetString()));
            Assert.Equal("toolFail", dynamicToolParams.GetProperty("tool").GetString());
            Assert.False(dynamicToolParams.TryGetProperty("itemId", out _));
            Assert.False(dynamicToolParams.TryGetProperty("toolName", out _));

            var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");
            Assert.True(pending.TryGetValue(dynamicToolRequestId, out var pendingRequest));
            Assert.NotNull(pendingRequest);
            pendingRequest!.TrySetResult(JsonSerializer.SerializeToElement(new
            {
                success = false,
                contentItems = new object[]
                {
                    new { type = "inputText", text = "dynamic-failed" },
                },
            }));

            inputChannel.Writer.TryComplete();
            await runTask.WaitAsync(TimeSpan.FromSeconds(10));

            var output = writer.CapturedText.ToString();
            _ = FindJsonRpcMessageById(output, 2);
            Assert.Contains("dynamic-failed", output, StringComparison.Ordinal);
            var docs = output
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var completed = Assert.Single(docs.Where(static doc =>
                    doc.RootElement.TryGetProperty("method", out var method)
                    && method.GetString() == "item/completed"
                    && doc.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "dynamicToolCall"));
                var completedItem = completed.RootElement.GetProperty("params").GetProperty("item");
                Assert.Equal("toolFail", completedItem.GetProperty("tool").GetString());
                Assert.Equal("failed", completedItem.GetProperty("status").GetString());
                Assert.False(completedItem.GetProperty("success").GetBoolean());
                Assert.Equal("dynamic-failed", completedItem.GetProperty("contentItems")[0].GetProperty("text").GetString());
            }
            finally
            {
                foreach (var doc in docs)
                {
                    doc.Dispose();
                }
            }
            Assert.Contains("\"method\":\"turn/completed\"", output, StringComparison.Ordinal);
        }
        finally
        {
            inputChannel.Writer.TryComplete();
            DeleteDirectory(root);
            writer.Dispose();
        }
    }
    [Fact]
    public async Task RunAsync_ShouldResolveServerRequestFromInputStream()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var otherRoot = Path.Combine(root, "other");
        Directory.CreateDirectory(otherRoot);

        var inputChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });

        var reader = new ChannelTextReader(inputChannel.Reader);
        var writer = new ChannelTextWriter();
        var threadStore = new KernelThreadStore(storePath);
        var server = new AppHostServer(reader, writer, threadStore);

        var runTask = server.RunAsync(CancellationToken.None);
        await KernelAppServerTestProtocol.InitializeAsync(inputChannel.Writer, writer.Lines, TimeSpan.FromSeconds(5));

        try
        {
            var commandRequestId = 100;
            var commandRequest = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = commandRequestId,
                method = "command/exec",
                @params = new
                {
                    threadId = "thread_command_sqeq_001",
                    itemId = "cmd_sqeq_001",
                    command = new[] { "powershell.exe", "-Command", "Write-Output sqeq-ok" },
                    cwd = root.Replace("\\", "/"),
                    approvalPolicy = "on-request",
                    sandboxPolicy = new
                    {
                        type = "workspaceWrite",
                        writableRoots = new[] { otherRoot.Replace("\\", "/") },
                        networkAccess = false,
                    },
                },
            });
            Assert.True(inputChannel.Writer.TryWrite(commandRequest));

            var approvalRequestLine = await WaitForJsonRpcMethodAsync(writer.Lines, "item/commandExecution/requestApproval", TimeSpan.FromSeconds(5));
            using var approvalRequest = JsonDocument.Parse(approvalRequestLine);
            var approvalRequestId = approvalRequest.RootElement.GetProperty("id").GetInt64();

            var approvalResponse = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = approvalRequestId,
                result = new
                {
                    decision = "accept",
                },
            });
            Assert.True(inputChannel.Writer.TryWrite(approvalResponse));

            var commandResponseLine = await WaitForJsonRpcResponseIdAsync(writer.Lines, commandRequestId, TimeSpan.FromSeconds(10));
            using var commandResponse = JsonDocument.Parse(commandResponseLine);
            var result = commandResponse.RootElement.GetProperty("result");
            Assert.Equal(0, result.GetProperty("exitCode").GetInt32());
            Assert.Contains("sqeq-ok", result.GetProperty("stdout").GetString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            inputChannel.Writer.TryComplete();
            await runTask.WaitAsync(TimeSpan.FromSeconds(10));
            DeleteDirectory(root);
            writer.Dispose();
        }
    }

    [Fact]
    public async Task RunAsync_ShouldEmitSkillsChangedWhenWatchedSkillFilesChange()
    {
        var root = CreateTempDirectory();
        var workspaceRoot = Path.Combine(root, "workspace");
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var skillDir = Path.Combine(workspaceRoot, ".tianshu", "skills", "demo-skill");
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(skillDir);
        Directory.CreateDirectory(Path.Combine(tianShuHome, "modules", "skills"));
        await File.WriteAllTextAsync(Path.Combine(skillDir, "SKILL.md"), "initial");

        var storePath = Path.Combine(root, "threads.json");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var originalThrottle = Environment.GetEnvironmentVariable("TIANSHU_FILE_WATCHER_THROTTLE_MS");

        var inputChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });

        var reader = new ChannelTextReader(inputChannel.Reader);
        var writer = new ChannelTextWriter();
        var threadStore = new KernelThreadStore(storePath);

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Environment.SetEnvironmentVariable("TIANSHU_FILE_WATCHER_THROTTLE_MS", "100");

            var server = new AppHostServer(reader, writer, threadStore);
            var runTask = server.RunAsync(CancellationToken.None);
            await KernelAppServerTestProtocol.InitializeAsync(inputChannel.Writer, writer.Lines, TimeSpan.FromSeconds(5));

            var threadStartRequest = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "thread/start",
                @params = new
                {
                    cwd = workspaceRoot.Replace("\\", "/"),
                },
            });
            Assert.True(inputChannel.Writer.TryWrite(threadStartRequest));

            _ = await WaitForJsonRpcResponseIdAsync(writer.Lines, 1, TimeSpan.FromSeconds(5));

            await File.WriteAllTextAsync(Path.Combine(skillDir, "SKILL.md"), "updated");

            var changedLine = await WaitForJsonRpcMethodAsync(writer.Lines, "skills/changed", TimeSpan.FromSeconds(10));
            using var changed = JsonDocument.Parse(changedLine);
            Assert.Equal("skills/changed", changed.RootElement.GetProperty("method").GetString());
            var changedParams = changed.RootElement.GetProperty("params");
            Assert.Equal(JsonValueKind.Object, changedParams.ValueKind);
            Assert.False(changedParams.TryGetProperty("paths", out _));
            Assert.Empty(changedParams.EnumerateObject());

            inputChannel.Writer.TryComplete();
            await runTask.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            Environment.SetEnvironmentVariable("TIANSHU_FILE_WATCHER_THROTTLE_MS", originalThrottle);
            DeleteDirectory(root);
            writer.Dispose();
        }
    }


    [Fact]
    public async Task RunAsync_ShouldEmitMcpToolCallLifecycleForDynamicMcpTool()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        var inputChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });

        var reader = new ChannelTextReader(inputChannel.Reader);
        var writer = new ChannelTextWriter();
        var threadStore = new KernelThreadStore(storePath);
        var server = new AppHostServer(reader, writer, threadStore);
        var runTask = server.RunAsync(CancellationToken.None);
        await KernelAppServerTestProtocol.InitializeAsync(inputChannel.Writer, writer.Lines, TimeSpan.FromSeconds(5));

        try
        {
            var startRequest = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "thread/start",
                @params = new
                {
                    cwd = root.Replace("\\", "/"),
                    dynamicTools = new object[]
                    {
                        new
                        {
                            name = "docs_search",
                            server = "docs",
                            description = "Search docs.",
                            inputSchema = new
                            {
                                type = "object",
                                properties = new
                                {
                                    query = new { type = "string" },
                                },
                            },
                        },
                    },
                },
            });
            Assert.True(inputChannel.Writer.TryWrite(startRequest));

            var startResponseLine = await WaitForJsonRpcResponseIdAsync(writer.Lines, 1, TimeSpan.FromSeconds(5));
            using var startResponse = JsonDocument.Parse(startResponseLine);
            var threadId = startResponse.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(threadId));

            var turnRequest = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[] { new { text = "/tool docs_search {\"query\":\"hello\"}" } },
                },
            });
            Assert.True(inputChannel.Writer.TryWrite(turnRequest));

            var dynamicToolRequestLine = await WaitForJsonRpcRequestMethodAsync(writer.Lines, "item/tool/call", TimeSpan.FromSeconds(5));
            using var dynamicToolRequest = JsonDocument.Parse(dynamicToolRequestLine);
            var dynamicToolRequestId = dynamicToolRequest.RootElement.GetProperty("id").GetInt64();

            var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");
            Assert.True(pending.TryGetValue(dynamicToolRequestId, out var pendingRequest));
            Assert.NotNull(pendingRequest);
            pendingRequest!.TrySetResult(JsonSerializer.SerializeToElement(new
            {
                success = true,
                contentItems = new object[]
                {
                    new { type = "text", text = "hit-1" },
                },
                structuredContent = new
                {
                    total = 1,
                },
            }));

            inputChannel.Writer.TryComplete();
            await runTask.WaitAsync(TimeSpan.FromSeconds(10));

            var docs = writer.CapturedText
                .ToString()
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToList();
            try
            {
                var started = Assert.Single(docs.Where(static doc =>
                    doc.RootElement.TryGetProperty("method", out var method)
                    && method.GetString() == "item/started"
                    && doc.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "mcpToolCall"));
                var startedItem = started.RootElement.GetProperty("params").GetProperty("item");
                Assert.Equal("docs", startedItem.GetProperty("server").GetString());
                Assert.Equal("docs_search", startedItem.GetProperty("tool").GetString());
                Assert.Equal("inProgress", startedItem.GetProperty("status").GetString());
                Assert.Equal("hello", startedItem.GetProperty("arguments").GetProperty("query").GetString());

                var completed = Assert.Single(docs.Where(static doc =>
                    doc.RootElement.TryGetProperty("method", out var method)
                    && method.GetString() == "item/completed"
                    && doc.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "mcpToolCall"));
                var completedItem = completed.RootElement.GetProperty("params").GetProperty("item");
                Assert.Equal("completed", completedItem.GetProperty("status").GetString());
                Assert.Equal("docs", completedItem.GetProperty("server").GetString());
                Assert.Equal("docs_search", completedItem.GetProperty("tool").GetString());
                Assert.Equal("text", completedItem.GetProperty("result").GetProperty("content")[0].GetProperty("type").GetString());
                Assert.Equal("hit-1", completedItem.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString());
                Assert.Equal(1, completedItem.GetProperty("result").GetProperty("structuredContent").GetProperty("total").GetInt32());
                Assert.True(completedItem.GetProperty("durationMs").ValueKind is JsonValueKind.Number or JsonValueKind.Null);
            }
            finally
            {
                foreach (var doc in docs)
                {
                    doc.Dispose();
                }
            }
        }
        finally
        {
            inputChannel.Writer.TryComplete();
            DeleteDirectory(root);
            writer.Dispose();
        }
    }

    [Fact]
    public async Task RunAsync_ShouldEmitFailedMcpToolCallLifecycleForDynamicMcpTool()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        var inputChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });

        var reader = new ChannelTextReader(inputChannel.Reader);
        var writer = new ChannelTextWriter();
        var threadStore = new KernelThreadStore(storePath);
        var server = new AppHostServer(reader, writer, threadStore);
        var runTask = server.RunAsync(CancellationToken.None);
        await KernelAppServerTestProtocol.InitializeAsync(inputChannel.Writer, writer.Lines, TimeSpan.FromSeconds(5));

        try
        {
            var startRequest = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "thread/start",
                @params = new
                {
                    cwd = root.Replace("\\", "/"),
                    dynamicTools = new object[]
                    {
                        new
                        {
                            name = "docs_search_fail",
                            server = "docs",
                            description = "Search docs.",
                            inputSchema = new { type = "object" },
                        },
                    },
                },
            });
            Assert.True(inputChannel.Writer.TryWrite(startRequest));

            var startResponseLine = await WaitForJsonRpcResponseIdAsync(writer.Lines, 1, TimeSpan.FromSeconds(5));
            using var startResponse = JsonDocument.Parse(startResponseLine);
            var threadId = startResponse.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(threadId));

            var turnRequest = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[] { new { text = "/tool docs_search_fail {}" } },
                },
            });
            Assert.True(inputChannel.Writer.TryWrite(turnRequest));

            var dynamicToolRequestLine = await WaitForJsonRpcRequestMethodAsync(writer.Lines, "item/tool/call", TimeSpan.FromSeconds(5));
            using var dynamicToolRequest = JsonDocument.Parse(dynamicToolRequestLine);
            var dynamicToolRequestId = dynamicToolRequest.RootElement.GetProperty("id").GetInt64();

            var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");
            Assert.True(pending.TryGetValue(dynamicToolRequestId, out var pendingRequest));
            Assert.NotNull(pendingRequest);
            pendingRequest!.TrySetResult(JsonSerializer.SerializeToElement(new
            {
                success = false,
                contentItems = new object[]
                {
                    new { type = "text", text = "permission denied" },
                },
            }));

            inputChannel.Writer.TryComplete();
            await runTask.WaitAsync(TimeSpan.FromSeconds(10));

            var docs = writer.CapturedText
                .ToString()
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToList();
            try
            {
                var completed = Assert.Single(docs.Where(static doc =>
                    doc.RootElement.TryGetProperty("method", out var method)
                    && method.GetString() == "item/completed"
                    && doc.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "mcpToolCall"));
                var completedItem = completed.RootElement.GetProperty("params").GetProperty("item");
                Assert.Equal("failed", completedItem.GetProperty("status").GetString());
                Assert.Equal("docs", completedItem.GetProperty("server").GetString());
                Assert.Equal("docs_search_fail", completedItem.GetProperty("tool").GetString());
                Assert.Equal("permission denied", completedItem.GetProperty("error").GetProperty("message").GetString());
            }
            finally
            {
                foreach (var doc in docs)
                {
                    doc.Dispose();
                }
            }
        }
        finally
        {
            inputChannel.Writer.TryComplete();
            DeleteDirectory(root);
            writer.Dispose();
        }
    }
    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        var value = field!.GetValue(instance);
        Assert.IsType<T>(value);
        return (T)value!;
    }
    private static string FindJsonRpcMessageById(string capturedText, long id)
    {
        foreach (var line in capturedText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
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

        throw new Xunit.Sdk.XunitException($"未在 capturedText 中找到 id={id} 的 JSON-RPC 消息。");
    }
    private static async Task<string> WaitForJsonRpcMethodAsync(
        ChannelReader<string> lines,
        string method,
        TimeSpan timeout)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
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

        throw new TimeoutException($"未等到 method={method} 的 JSON-RPC 消息。");
    }

    private static async Task<string> WaitForJsonRpcRequestMethodAsync(
        ChannelReader<string> lines,
        string method,
        TimeSpan timeout)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        while (await lines.WaitToReadAsync(timeoutCts.Token))
        {
            while (lines.TryRead(out var line))
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("method", out var methodElement)
                    && string.Equals(methodElement.GetString(), method, StringComparison.Ordinal)
                    && doc.RootElement.TryGetProperty("id", out _))
                {
                    return line;
                }
            }
        }

        throw new TimeoutException($"未等到 method={method} 的 JSON-RPC request。");
    }

    private static async Task<string> WaitForJsonRpcResponseIdAsync(
        ChannelReader<string> lines,
        long id,
        TimeSpan timeout)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
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

        throw new TimeoutException($"未等到 id={id} 的 JSON-RPC response。");
    }

    private sealed class ChannelTextReader(ChannelReader<string> source) : TextReader
    {
        public override async Task<string?> ReadLineAsync()
        {
            while (await source.WaitToReadAsync().ConfigureAwait(false))
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

        private bool disposed;
        private readonly StringBuilder capturedText = new();

        public ChannelReader<string> Lines => lines.Reader;

        public StringBuilder CapturedText => capturedText;

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

        public override Task FlushAsync() => Task.CompletedTask;

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

    private static string CreateTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "tianshu-kernel", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // ignore cleanup failures
        }
    }
}
