using System.Net;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;
using TianShu.Contracts.Diagnostics;
using TianShu.Provider.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace TianShu.Execution.Integration.Tests;

[Collection("EnvironmentVariables")]
public sealed class AppHostServerResponsesToolLoopTests
{
    [Fact]
    public async Task RunAsync_ShouldExecuteResponsesToolLoopWithFunctionCall()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        Directory.CreateDirectory(repoRoot);

        var notePath = Path.Combine(repoRoot, "note.txt");
        await File.WriteAllTextAsync(notePath, "hello-tool-loop");

        const string threadId = "thread_responses_tool_loop_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);


            var notePathJson = notePath.Replace('\\', '/');
            var functionCallArgs = JsonSerializer.Serialize(new
            {
                file_path = notePathJson,
            });

            var firstStream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-1" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "message",
                        role = "assistant",
                        content = new object[]
                        {
                            new { type = "output_text", text = "我先看看文件。" },
                        },
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "function_call",
                        name = "read_file",
                        arguments = functionCallArgs,
                        call_id = "call-1",
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = "resp-1" },
                }));

            var secondStream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-2" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_text.delta",
                    delta = "OK",
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "message",
                        role = "assistant",
                        content = new object[]
                        {
                            new { type = "output_text", text = "OK" },
                        },
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = "resp-2" },
                }));

            var handler = new CapturingSequencedSseHandler([firstStream, secondStream]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请读取文件内容" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            Assert.Equal(2, handler.RequestCount);
            Assert.Contains("\"toolName\":\"read_file\"", writer.ToString(), StringComparison.Ordinal);

            using (var firstRequestJson = JsonDocument.Parse(handler.RequestBodies[0]))
            {
                var rootJson = firstRequestJson.RootElement;
                Assert.True(rootJson.TryGetProperty("parallel_tool_calls", out var ptc));
                Assert.Equal(ProviderModelCatalogs.SupportsParallelToolCalls("gpt-5"), ptc.GetBoolean());
                Assert.False(rootJson.TryGetProperty("previous_response_id", out _));
                var toolNames = rootJson.GetProperty("tools")
                    .EnumerateArray()
                    .Where(static tool => tool.TryGetProperty("name", out _))
                    .Select(static tool => tool.GetProperty("name").GetString())
                    .ToArray();
                Assert.Contains("shell", toolNames);
                Assert.DoesNotContain("shell_command", toolNames);
                Assert.DoesNotContain("exec_command", toolNames);
                Assert.DoesNotContain("write_stdin", toolNames);
            }

            using (var secondRequestJson = JsonDocument.Parse(handler.RequestBodies[1]))
            {
                var rootJson = secondRequestJson.RootElement;
                Assert.True(rootJson.TryGetProperty("parallel_tool_calls", out var ptc));
                Assert.Equal(ProviderModelCatalogs.SupportsParallelToolCalls("gpt-5"), ptc.GetBoolean());
                Assert.False(rootJson.TryGetProperty("previous_response_id", out _));

                var input = rootJson.GetProperty("input");
                Assert.Equal(JsonValueKind.Array, input.ValueKind);
                var items = input.EnumerateArray().ToArray();
                Assert.Contains(items, static item => item.GetProperty("type").GetString() == "message"
                    && item.GetProperty("role").GetString() == "user"
                    && item.GetProperty("content")[0].GetProperty("text").GetString() == "请读取文件内容");
                Assert.Contains(items, static item => item.GetProperty("type").GetString() == "message"
                    && item.GetProperty("role").GetString() == "assistant"
                    && item.GetProperty("content")[0].GetProperty("text").GetString() == "我先看看文件。");
                Assert.Contains(items, static item => item.GetProperty("type").GetString() == "function_call"
                    && item.GetProperty("call_id").GetString() == "call-1");
                Assert.Contains(items, static item => item.GetProperty("type").GetString() == "function_call_output"
                    && item.GetProperty("call_id").GetString() == "call-1");
            }

            var verifyStore = new KernelThreadStore(storePath);
            await verifyStore.InitializeAsync(CancellationToken.None);
            var thread = await verifyStore.GetThreadAsync(threadId, CancellationToken.None);
            Assert.NotNull(thread);
            Assert.NotEmpty(thread!.Turns);
            Assert.Contains("OK", thread.Turns[^1].AssistantMessage ?? string.Empty, StringComparison.Ordinal);

            var sqliteThread = await verifyStore.StateStore.GetThreadAsync(threadId, CancellationToken.None);
            Assert.NotNull(sqliteThread);
            var sqliteThreadPayload = KernelStoredThreadStateTestHelper.DeserializePayload(sqliteThread!);
            Assert.NotEmpty(sqliteThreadPayload.Turns);

            var turnLogs = await verifyStore.StateStore.ListTurnLogsAsync(threadId, CancellationToken.None);
            Assert.Contains(turnLogs, static log => log.Phase == "turn.started" && log.Status == "inProgress");
            Assert.Contains(turnLogs, static log => log.Phase == "turn.completed" && log.Status == "completed");
            Assert.Contains(turnLogs, static log => log.Phase == "turn.responses.request" && log.Status == "inProgress");
            Assert.Contains(turnLogs, static log => log.Phase == "turn.function_call.received" && log.Status == "inProgress" && string.Equals(log.Summary, "read_file -> read_file", StringComparison.Ordinal));
            Assert.Contains(turnLogs, static log => log.Phase == "turn.function_call.parsed" && log.Status == "completed" && string.Equals(log.Summary, "read_file -> read_file", StringComparison.Ordinal));

            var rollouts = await verifyStore.StateStore.ListRolloutsAsync(threadId, CancellationToken.None);
            var rollout = Assert.Single(rollouts);
            Assert.Equal("turn", rollout.Source);
            Assert.Equal(threadId, rollout.ThreadId);
            Assert.True(File.Exists(rollout.RolloutPath));

            var sessionPath = verifyStore.RolloutRecorder.GetRolloutPath(threadId);
            Assert.True(File.Exists(sessionPath));
            var sessionText = await File.ReadAllTextAsync(sessionPath, CancellationToken.None);
            Assert.Contains("\"type\":\"session_meta\"", sessionText, StringComparison.Ordinal);
            Assert.Contains("\"type\":\"turn\"", sessionText, StringComparison.Ordinal);
            Assert.Contains("\"status\":\"completed\"", sessionText, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenResponsesFileChangeOutputItemCompletes_EmitsRealtimeItemCompletedNotification()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        Directory.CreateDirectory(repoRoot);

        var changedPath = Path.Combine(repoRoot, "MainWindow.xaml");
        const string threadId = "thread_responses_file_change_lifecycle_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var stream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-file-change-1" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_text.delta",
                    delta = "项目骨架已创建。现在编写 XAML 界面。",
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        id = "tool_write_call_00_test",
                        type = "fileChange",
                        status = "completed",
                        changes = new[]
                        {
                            new
                            {
                                path = changedPath,
                                kind = "update",
                                diff = "<Window />",
                            },
                        },
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = "resp-file-change-1" },
                }));

            var handler = new CapturingSequencedSseHandler([stream]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请修改 XAML" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            var output = writer.ToString();
            Assert.Contains("\"method\":\"item/completed\"", output, StringComparison.Ordinal);
            Assert.Contains("\"type\":\"fileChange\"", output, StringComparison.Ordinal);
            Assert.Contains("tool_write_call_00_test", output, StringComparison.Ordinal);
            Assert.Contains(changedPath.Replace("\\", "\\\\"), output, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRenderStructuredReviewOutputAndSendReviewSchemaFormat()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        Directory.CreateDirectory(repoRoot);

        const string threadId = "thread_review_structured_output_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var reviewPayload = JsonSerializer.Serialize(new
            {
                findings = new object[]
                {
                    new
                    {
                        title = "Prefer Stylize helpers",
                        body = "Use .dim()/.bold() chaining instead of manual Style.",
                        confidence_score = 0.9,
                        priority = 1,
                        code_location = new
                        {
                            absolute_file_path = "/tmp/file.rs",
                            line_range = new
                            {
                                start = 10,
                                end = 20,
                            },
                        },
                    },
                },
                overall_correctness = "good",
                overall_explanation = "Looks solid overall with minor polish suggested.",
                overall_confidence_score = 0.75,
            });

            var stream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-review-1" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "message",
                        role = "assistant",
                        content = new object[]
                        {
                            new { type = "output_text", text = reviewPayload },
                        },
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = "resp-review-1" },
                }));

            var handler = new CapturingSequencedSseHandler([stream]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "review/start",
                @params = new
                {
                    threadId,
                    target = new
                    {
                        type = "custom",
                        instructions = "请审查当前改动",
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"type\":\"exitedReviewMode\"", TimeSpan.FromSeconds(5));

            Assert.Equal(1, handler.RequestCount);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var completedReview = Assert.Single(messages.Where(static message =>
                    IsNotificationMethod(message.RootElement, "item/completed")
                    && message.RootElement.TryGetProperty("params", out var @params)
                    && @params.TryGetProperty("item", out var item)
                    && string.Equals(item.GetProperty("type").GetString(), "exitedReviewMode", StringComparison.Ordinal)));

                var review = completedReview.RootElement
                    .GetProperty("params")
                    .GetProperty("item")
                    .GetProperty("review")
                    .GetString();
                Assert.NotNull(review);
                Assert.Contains("Looks solid overall with minor polish suggested.", review, StringComparison.Ordinal);
                Assert.Contains("Review comment:", review, StringComparison.Ordinal);
                Assert.Contains("Prefer Stylize helpers", review, StringComparison.Ordinal);
                Assert.Contains("/tmp/file.rs:10-20", review, StringComparison.Ordinal);

                using var requestJson = JsonDocument.Parse(handler.RequestBodies[0]);
                var format = requestJson.RootElement
                    .GetProperty("text")
                    .GetProperty("format");
                Assert.Equal("codex_output_schema", format.GetProperty("name").GetString());
                Assert.Equal("json_schema", format.GetProperty("type").GetString());
                Assert.True(format.GetProperty("strict").GetBoolean());

                var schemaProperties = format
                    .GetProperty("schema")
                    .GetProperty("properties");
                Assert.True(schemaProperties.TryGetProperty("findings", out _));
                Assert.True(schemaProperties.TryGetProperty("overall_correctness", out _));
                Assert.True(schemaProperties.TryGetProperty("overall_explanation", out _));
                Assert.True(schemaProperties.TryGetProperty("overall_confidence_score", out _));
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
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldReplayStickyTurnStateHeaderAcrossResponsesRequests()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);

        var notePath = Path.Combine(repoRoot, "sticky.txt");
        await File.WriteAllTextAsync(notePath, "sticky-state");

        const string threadId = "thread_responses_turn_state_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", Path.Combine(root, "tianshu-home"));

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var functionCallArgs = JsonSerializer.Serialize(new
            {
                file_path = notePath.Replace('\\', '/'),
            });

            var firstStream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "function_call",
                        name = "read_file",
                        arguments = functionCallArgs,
                        call_id = "call-sticky-1",
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = "resp-sticky-1" },
                }));

            var secondStream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "message",
                        role = "assistant",
                        content = new object[]
                        {
                            new { type = "output_text", text = "TURN STATE OK" },
                        },
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = "resp-sticky-2" },
                }));

            var handler = new CapturingSequencedSseHandler(
                [firstStream, secondStream],
                [
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["x-codex-turn-state"] = "sticky-turn-token-1",
                    },
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                ]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请继续执行并验证 sticky turn state" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            Assert.Equal(2, handler.RequestCount);
            Assert.False(handler.RequestHeaders[0].ContainsKey("x-codex-turn-state"));
            Assert.True(handler.RequestHeaders[1].TryGetValue("x-codex-turn-state", out var stickyValues));
            Assert.Equal("sticky-turn-token-1", Assert.Single(stickyValues));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldUseResponsesWebSocketTransportAndSendDeltaFollowUpWithPreviousResponseId()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);

        var notePath = Path.Combine(repoRoot, "note-ws.txt");
        await File.WriteAllTextAsync(notePath, "hello-websocket-tool-loop");

        const string threadId = "thread_responses_websocket_delta_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
        await WriteTianShuHomeConfigAsync(tianShuHome, """
            [features]
            responses_websockets = true
            """);

        await using var websocketServer = new ResponsesWebSocketTestServer(
            new ResponsesWebSocketConnectionPlan(
                ResponseBatches:
                [
                    [
                        JsonSerializer.Serialize(new
                        {
                            type = "response.completed",
                            response = new { id = "resp-ws-0" },
                        }),
                    ],
                    [
                        JsonSerializer.Serialize(new
                        {
                            type = "response.output_item.done",
                            item = new
                            {
                                type = "message",
                                role = "assistant",
                                content = new object[]
                                {
                                    new { type = "output_text", text = "我先看看文件。" },
                                },
                            },
                        }),
                        JsonSerializer.Serialize(new
                        {
                            type = "response.output_item.done",
                            item = new
                            {
                                type = "function_call",
                                name = "read_file",
                                arguments = JsonSerializer.Serialize(new
                                {
                                    file_path = notePath.Replace('\\', '/'),
                                }),
                                call_id = "call-ws-1",
                            },
                        }),
                        JsonSerializer.Serialize(new
                        {
                            type = "response.completed",
                            response = new { id = "resp-ws-1" },
                        }),
                    ],
                    [
                        JsonSerializer.Serialize(new
                        {
                            type = "response.output_text.delta",
                            delta = "WEBSOCKET OK",
                        }),
                        JsonSerializer.Serialize(new
                        {
                            type = "response.output_item.done",
                            item = new
                            {
                                type = "message",
                                role = "assistant",
                                content = new object[]
                                {
                                    new { type = "output_text", text = "WEBSOCKET OK" },
                                },
                            },
                        }),
                        JsonSerializer.Serialize(new
                        {
                            type = "response.completed",
                            response = new { id = "resp-ws-2" },
                        }),
                    ],
                ],
                CloseAfterRequestCount: 3));

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var httpHandler = new CapturingSequencedSseHandler(Array.Empty<string>());
            var httpClient = new HttpClient(httpHandler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请读取文件内容" },
                    },
                    providerBaseUrl = websocketServer.BaseUrl,
                    providerWireApi = "responses",
                    providerSupportsWebsockets = true,
                    providerStreamMaxRetries = 0,
                    providerRequestMaxRetries = 0,
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            Assert.Equal(0, httpHandler.RequestCount);
            Assert.Equal(1, websocketServer.ConnectionCount);

            var requests = await websocketServer.WaitForRequestCountAsync(3);
            using var warmupRequestJson = JsonDocument.Parse(requests[0]);
            using var firstRequestJson = JsonDocument.Parse(requests[1]);
            using var secondRequestJson = JsonDocument.Parse(requests[2]);

            Assert.Equal("response.create", warmupRequestJson.RootElement.GetProperty("type").GetString());
            Assert.False(warmupRequestJson.RootElement.GetProperty("generate").GetBoolean());
            Assert.False(warmupRequestJson.RootElement.TryGetProperty("previous_response_id", out _));
            Assert.Equal("response.create", firstRequestJson.RootElement.GetProperty("type").GetString());
            Assert.Equal("resp-ws-0", firstRequestJson.RootElement.GetProperty("previous_response_id").GetString());
            Assert.Empty(firstRequestJson.RootElement.GetProperty("input").EnumerateArray().ToArray());

            Assert.Equal("response.create", secondRequestJson.RootElement.GetProperty("type").GetString());
            Assert.Equal("resp-ws-1", secondRequestJson.RootElement.GetProperty("previous_response_id").GetString());

            var secondInput = secondRequestJson.RootElement.GetProperty("input").EnumerateArray().ToArray();
            Assert.Single(secondInput);
            Assert.Equal("function_call_output", secondInput[0].GetProperty("type").GetString());
            Assert.Equal("call-ws-1", secondInput[0].GetProperty("call_id").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldPrewarmResponsesWebSocketAndReuseWarmupResponseIdForFirstTurnRequest()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);

        var notePath = Path.Combine(repoRoot, "note-ws-prewarm.txt");
        await File.WriteAllTextAsync(notePath, "hello-websocket-prewarm");

        const string threadId = "thread_responses_websocket_prewarm_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
        await WriteTianShuHomeConfigAsync(tianShuHome, """
            [features]
            responses_websockets = true

            [diagnostics]
            default_level = "artifact"

            [diagnostics.artifacts]
            enabled = true
            """);

        await using var websocketServer = new ResponsesWebSocketTestServer(
            new ResponsesWebSocketConnectionPlan(
                ResponseBatches:
                [
                    [
                        JsonSerializer.Serialize(new
                        {
                            type = "response.completed",
                            response = new { id = "resp-ws-prewarm-0" },
                        }),
                    ],
                    [
                        JsonSerializer.Serialize(new
                        {
                            type = "response.output_item.done",
                            item = new
                            {
                                type = "message",
                                role = "assistant",
                                content = new object[]
                                {
                                    new { type = "output_text", text = "我先预热后读取文件。" },
                                },
                            },
                        }),
                        JsonSerializer.Serialize(new
                        {
                            type = "response.output_item.done",
                            item = new
                            {
                                type = "function_call",
                                name = "read_file",
                                arguments = JsonSerializer.Serialize(new
                                {
                                    file_path = notePath.Replace('\\', '/'),
                                }),
                                call_id = "call-ws-prewarm-1",
                            },
                        }),
                        JsonSerializer.Serialize(new
                        {
                            type = "response.completed",
                            response = new { id = "resp-ws-prewarm-1" },
                        }),
                    ],
                    [
                        JsonSerializer.Serialize(new
                        {
                            type = "response.output_text.delta",
                            delta = "PREWARM OK",
                        }),
                        JsonSerializer.Serialize(new
                        {
                            type = "response.output_item.done",
                            item = new
                            {
                                type = "message",
                                role = "assistant",
                                content = new object[]
                                {
                                    new { type = "output_text", text = "PREWARM OK" },
                                },
                            },
                        }),
                        JsonSerializer.Serialize(new
                        {
                            type = "response.completed",
                            response = new { id = "resp-ws-prewarm-2" },
                        }),
                    ],
                ],
                CloseAfterRequestCount: 3));

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var httpHandler = new CapturingSequencedSseHandler(Array.Empty<string>());
            var httpClient = new HttpClient(httpHandler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请读取文件内容" },
                    },
                    providerBaseUrl = websocketServer.BaseUrl,
                    providerWireApi = "responses",
                    providerSupportsWebsockets = true,
                    providerStreamMaxRetries = 0,
                    providerRequestMaxRetries = 0,
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            Assert.Equal(0, httpHandler.RequestCount);
            Assert.Equal(1, websocketServer.ConnectionCount);
            var handshakes = await websocketServer.WaitForHandshakeCountAsync(1);
            Assert.True(handshakes[0].TryGetValue("OpenAI-Beta", out var betaValues));
            Assert.Equal("responses_websockets=2026-02-06", Assert.Single(betaValues));
            Assert.True(handshakes[0].TryGetValue("x-client-request-id", out var requestIdValues));
            Assert.Equal(threadId, Assert.Single(requestIdValues));
            Assert.True(handshakes[0].TryGetValue("x-codex-turn-metadata", out var metadataValues));
            using (var metadataJson = JsonDocument.Parse(Assert.Single(metadataValues)))
            {
                Assert.True(metadataJson.RootElement.TryGetProperty("turn_id", out var turnIdValue));
                Assert.False(string.IsNullOrWhiteSpace(turnIdValue.GetString()));
            }

            var requests = await websocketServer.WaitForRequestCountAsync(3);
            using var warmupRequestJson = JsonDocument.Parse(requests[0]);
            using var firstTurnRequestJson = JsonDocument.Parse(requests[1]);
            using var followUpRequestJson = JsonDocument.Parse(requests[2]);

            Assert.Equal("response.create", warmupRequestJson.RootElement.GetProperty("type").GetString());
            Assert.False(warmupRequestJson.RootElement.GetProperty("generate").GetBoolean());
            Assert.False(warmupRequestJson.RootElement.TryGetProperty("previous_response_id", out _));

            var warmupInput = warmupRequestJson.RootElement.GetProperty("input").EnumerateArray().ToArray();
            Assert.NotEmpty(warmupInput);
            Assert.Contains(warmupInput, static item => item.GetProperty("type").GetString() == "message"
                && item.GetProperty("role").GetString() == "user");
            Assert.True(warmupRequestJson.RootElement.TryGetProperty("client_metadata", out var warmupClientMetadata));
            Assert.Equal("x-codex-turn-metadata", Assert.Single(warmupClientMetadata.EnumerateObject()).Name);

            Assert.Equal("response.create", firstTurnRequestJson.RootElement.GetProperty("type").GetString());
            Assert.Equal("resp-ws-prewarm-0", firstTurnRequestJson.RootElement.GetProperty("previous_response_id").GetString());
            Assert.Empty(firstTurnRequestJson.RootElement.GetProperty("input").EnumerateArray().ToArray());
            Assert.True(firstTurnRequestJson.RootElement.TryGetProperty("client_metadata", out _));

            Assert.Equal("response.create", followUpRequestJson.RootElement.GetProperty("type").GetString());
            Assert.Equal("resp-ws-prewarm-1", followUpRequestJson.RootElement.GetProperty("previous_response_id").GetString());
            var followUpInput = followUpRequestJson.RootElement.GetProperty("input").EnumerateArray().ToArray();
            Assert.Single(followUpInput);
            Assert.Equal("function_call_output", followUpInput[0].GetProperty("type").GetString());
            Assert.Equal("call-ws-prewarm-1", followUpInput[0].GetProperty("call_id").GetString());

            var messages = ParseOutputDocuments(writer.ToString());
            try
            {
                var providerStats = messages
                    .Where(static document => IsNotificationMethod(document.RootElement, DiagnosticStatisticsEventNames.ProviderRequestContextStats))
                    .Select(static document => document.RootElement.GetProperty("params"))
                    .ToArray();
                Assert.Equal(3, providerStats.Length);
                Assert.Equal([1, 1, 2], providerStats.Select(static stats => stats.GetProperty("requestSequence").GetInt32()).ToArray());
                Assert.All(providerStats, stats =>
                {
                    Assert.Equal(threadId, stats.GetProperty("threadId").GetString());
                    Assert.Equal("websocket", stats.GetProperty("transport").GetString());
                    Assert.Equal("input", stats.GetProperty("inputPropertyName").GetString());
                    var artifact = stats.GetProperty("payloadArtifact");
                    Assert.Equal("provider_request_payload", artifact.GetProperty("artifactKind").GetString());
                    Assert.Equal("sanitized", artifact.GetProperty("redactionStatus").GetString());
                    var artifactPath = Path.Combine(
                        tianShuHome,
                        "artifacts",
                        "diagnostics",
                        "provider-requests",
                        artifact.GetProperty("relativePath").GetString()!);
                    Assert.True(File.Exists(artifactPath), artifactPath);
                });
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
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldUseResponsesWebSocketTransportWithoutFeatureFlagWhenProviderSupportsWebsockets()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);

        const string threadId = "thread_responses_websocket_feature_disabled_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        await using var websocketServer = new ResponsesWebSocketTestServer(
            new ResponsesWebSocketConnectionPlan(
                ResponseBatches:
                [
                    [
                        JsonSerializer.Serialize(new
                        {
                            type = "response.completed",
                            response = new { id = "resp-ws-no-flag-0" },
                        }),
                    ],
                    [
                        JsonSerializer.Serialize(new
                        {
                            type = "response.output_text.delta",
                            delta = "NO FLAG WS OK",
                        }),
                        JsonSerializer.Serialize(new
                        {
                            type = "response.output_item.done",
                            item = new
                            {
                                type = "message",
                                role = "assistant",
                                content = new object[]
                                {
                                    new { type = "output_text", text = "NO FLAG WS OK" },
                                },
                            },
                        }),
                        JsonSerializer.Serialize(new
                        {
                            type = "response.completed",
                            response = new { id = "resp-ws-no-flag-1" },
                        }),
                    ],
                ],
                CloseAfterRequestCount: 2));

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var httpHandler = new CapturingSequencedSseHandler(Array.Empty<string>());
            var httpClient = new HttpClient(httpHandler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "feature-off" },
                    },
                    providerBaseUrl = websocketServer.BaseUrl,
                    providerWireApi = "responses",
                    providerSupportsWebsockets = true,
                    providerStreamMaxRetries = 0,
                    providerRequestMaxRetries = 0,
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            Assert.Equal(0, httpHandler.RequestCount);
            Assert.Equal(1, websocketServer.HandshakeCount);
            Assert.Equal(1, websocketServer.ConnectionCount);
            Assert.Contains("NO FLAG WS OK", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldReplayTurnStateAcrossResponsesWebSocketReconnectAndResetDeltaBaseline()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);

        var notePath = Path.Combine(repoRoot, "note-ws-reset.txt");
        await File.WriteAllTextAsync(notePath, "hello-websocket-reconnect");

        const string threadId = "thread_responses_websocket_reset_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
        await WriteTianShuHomeConfigAsync(tianShuHome, """
            [features]
            responses_websockets = true
            """);

        await using var websocketServer = new ResponsesWebSocketTestServer(
            new ResponsesWebSocketConnectionPlan(
                ResponseBatches:
                [
                    [
                        JsonSerializer.Serialize(new
                        {
                            type = "response.completed",
                            response = new { id = "resp-ws-reset-0" },
                        }),
                    ],
                    [
                        JsonSerializer.Serialize(new
                        {
                            type = "response.output_item.done",
                            item = new
                            {
                                type = "message",
                                role = "assistant",
                                content = new object[]
                                {
                                    new { type = "output_text", text = "我先看看文件。" },
                                },
                            },
                        }),
                        JsonSerializer.Serialize(new
                        {
                            type = "response.output_item.done",
                            item = new
                            {
                                type = "function_call",
                                name = "read_file",
                                arguments = JsonSerializer.Serialize(new
                                {
                                    file_path = notePath.Replace('\\', '/'),
                                }),
                                call_id = "call-ws-reset-1",
                            },
                        }),
                        JsonSerializer.Serialize(new
                        {
                            type = "response.completed",
                            response = new { id = "resp-ws-reset-1" },
                        }),
                    ],
                    Array.Empty<string>(),
                ],
                CloseAfterRequestCount: 3,
                ResponseHeaders: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["x-codex-turn-state"] = "sticky-ws-reset-1",
                }),
            new ResponsesWebSocketConnectionPlan(
                ResponseBatches:
                [
                    [
                        JsonSerializer.Serialize(new
                        {
                            type = "response.output_text.delta",
                            delta = "RECONNECT RESET OK",
                        }),
                        JsonSerializer.Serialize(new
                        {
                            type = "response.output_item.done",
                            item = new
                            {
                                type = "message",
                                role = "assistant",
                                content = new object[]
                                {
                                    new { type = "output_text", text = "RECONNECT RESET OK" },
                                },
                            },
                        }),
                        JsonSerializer.Serialize(new
                        {
                            type = "response.completed",
                            response = new { id = "resp-ws-reset-2" },
                        }),
                    ],
                ],
                CloseAfterRequestCount: 1));

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var httpHandler = new CapturingSequencedSseHandler(Array.Empty<string>());
            var httpClient = new HttpClient(httpHandler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请读取文件内容" },
                    },
                    providerBaseUrl = websocketServer.BaseUrl,
                    providerWireApi = "responses",
                    providerSupportsWebsockets = true,
                    providerStreamMaxRetries = 1,
                    providerRequestMaxRetries = 0,
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            Assert.Equal(0, httpHandler.RequestCount);
            Assert.Equal(2, websocketServer.ConnectionCount);

            var handshakes = await websocketServer.WaitForHandshakeCountAsync(2);
            var requests = await websocketServer.WaitForRequestCountAsync(4);
            using var warmupRequestJson = JsonDocument.Parse(requests[0]);
            using var firstRequestJson = JsonDocument.Parse(requests[1]);
            using var secondRequestJson = JsonDocument.Parse(requests[2]);
            using var thirdRequestJson = JsonDocument.Parse(requests[3]);

            Assert.False(handshakes[0].ContainsKey("x-codex-turn-state"));
            Assert.True(handshakes[1].TryGetValue("x-codex-turn-state", out var stickyValues));
            Assert.Equal("sticky-ws-reset-1", Assert.Single(stickyValues));

            Assert.False(warmupRequestJson.RootElement.TryGetProperty("previous_response_id", out _));
            Assert.Equal("resp-ws-reset-0", firstRequestJson.RootElement.GetProperty("previous_response_id").GetString());
            Assert.Empty(firstRequestJson.RootElement.GetProperty("input").EnumerateArray().ToArray());

            Assert.Equal("resp-ws-reset-1", secondRequestJson.RootElement.GetProperty("previous_response_id").GetString());
            var secondInput = secondRequestJson.RootElement.GetProperty("input").EnumerateArray().ToArray();
            Assert.Single(secondInput);
            Assert.Equal("function_call_output", secondInput[0].GetProperty("type").GetString());
            Assert.Equal("call-ws-reset-1", secondInput[0].GetProperty("call_id").GetString());

            Assert.False(thirdRequestJson.RootElement.TryGetProperty("previous_response_id", out _));
            var thirdInput = thirdRequestJson.RootElement.GetProperty("input").EnumerateArray().ToArray();
            Assert.True(thirdInput.Length >= 4);
            Assert.Contains(thirdInput, static item => item.GetProperty("type").GetString() == "message"
                && item.GetProperty("role").GetString() == "user");
            Assert.Contains(thirdInput, static item => item.GetProperty("type").GetString() == "function_call_output"
                && item.GetProperty("call_id").GetString() == "call-ws-reset-1");
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldFallbackToHttpImmediatelyWhenResponsesWebSocketUpgradeRequiresHttp()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);

        const string threadId = "thread_responses_websocket_upgrade_required_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
        await WriteTianShuHomeConfigAsync(tianShuHome, """
            [features]
            responses_websockets = true
            """);

        await using var websocketServer = new ResponsesWebSocketTestServer(
            new ResponsesWebSocketConnectionPlan(
                Array.Empty<IReadOnlyList<string>>(),
                RejectUpgradeStatusCode: HttpStatusCode.UpgradeRequired));

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var httpHandler = new CapturingSequencedSseHandler(
                [BuildAssistantMessageStream("resp-http-upgrade-required", "UPGRADE REQUIRED HTTP OK")]);
            var httpClient = new HttpClient(httpHandler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "upgrade-required" },
                    },
                    providerBaseUrl = websocketServer.BaseUrl,
                    providerWireApi = "responses",
                    providerSupportsWebsockets = true,
                    providerStreamMaxRetries = 2,
                    providerRequestMaxRetries = 0,
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            Assert.Equal(1, websocketServer.HandshakeCount);
            Assert.Equal(0, websocketServer.ConnectionCount);
            Assert.Equal(1, httpHandler.RequestCount);
            Assert.Contains("UPGRADE REQUIRED HTTP OK", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldLatchProviderHttpFallbackAndSkipResponsesWebSocketOnSubsequentTurns()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);

        const string threadId = "thread_responses_websocket_fallback_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
        await WriteTianShuHomeConfigAsync(tianShuHome, """
            [features]
            responses_websockets = true
            """);

        await using var websocketServer = new ResponsesWebSocketTestServer(
            new ResponsesWebSocketConnectionPlan(Array.Empty<IReadOnlyList<string>>(), CloseAfterRequestCount: 1),
            new ResponsesWebSocketConnectionPlan(Array.Empty<IReadOnlyList<string>>(), CloseAfterRequestCount: 1));

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var httpHandler = new CapturingSequencedSseHandler(
                [
                    BuildAssistantMessageStream("resp-http-fallback-1", "FIRST HTTP OK"),
                    BuildAssistantMessageStream("resp-http-fallback-2", "SECOND HTTP OK"),
                ]);
            var httpClient = new HttpClient(httpHandler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var firstTurnJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "first" },
                    },
                    providerBaseUrl = websocketServer.BaseUrl,
                    providerWireApi = "responses",
                    providerSupportsWebsockets = true,
                    providerStreamMaxRetries = 1,
                    providerRequestMaxRetries = 0,
                },
            });

            var secondTurnJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "second" },
                    },
                    providerBaseUrl = websocketServer.BaseUrl,
                    providerWireApi = "responses",
                    providerSupportsWebsockets = true,
                    providerStreamMaxRetries = 1,
                    providerRequestMaxRetries = 0,
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new QueuedTextReader();
            reader.Enqueue(KernelAppServerTestProtocol.CreateInitializeRequest(experimentalApi: true));
            reader.Enqueue(firstTurnJson);
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            var runTask = server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));
            Assert.Equal(2, websocketServer.ConnectionCount);
            reader.Enqueue(secondTurnJson);
            reader.Complete();

            await runTask;

            Assert.Equal(2, httpHandler.RequestCount);
            Assert.Equal(2, websocketServer.ConnectionCount);

            var output = writer.ToString();
            using (var outputLines = JsonDocument.Parse($"[{string.Join(",", output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))}]"))
            {
                Assert.Equal(2, outputLines.RootElement.EnumerateArray().Count(static message => IsNotificationMethod(message, "turn/completed")));
            }
            Assert.Contains("FIRST HTTP OK", output, StringComparison.Ordinal);
            Assert.Contains("SECOND HTTP OK", output, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldResetProviderHttpFallbackAcrossServerRestart()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);

        const string threadId = "thread_responses_websocket_fallback_restart_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
        await WriteTianShuHomeConfigAsync(tianShuHome, """
            [features]
            responses_websockets = true
            """);

        await using var websocketServer = new ResponsesWebSocketTestServer(
            new ResponsesWebSocketConnectionPlan(Array.Empty<IReadOnlyList<string>>(), CloseAfterRequestCount: 1),
            new ResponsesWebSocketConnectionPlan(Array.Empty<IReadOnlyList<string>>(), CloseAfterRequestCount: 1));

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var httpHandler = new CapturingSequencedSseHandler(
                [
                    BuildAssistantMessageStream("resp-http-fallback-restart-1", "FIRST HTTP OK"),
                    BuildAssistantMessageStream("resp-http-fallback-restart-2", "SECOND HTTP OK"),
                ]);
            var httpClient = new HttpClient(httpHandler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var firstTurnJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "first" },
                    },
                    providerBaseUrl = websocketServer.BaseUrl,
                    providerWireApi = "responses",
                    providerSupportsWebsockets = true,
                    providerStreamMaxRetries = 0,
                    providerRequestMaxRetries = 0,
                },
            });

            var secondTurnJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "second" },
                    },
                    providerBaseUrl = websocketServer.BaseUrl,
                    providerWireApi = "responses",
                    providerSupportsWebsockets = true,
                    providerStreamMaxRetries = 0,
                    providerRequestMaxRetries = 0,
                },
            });

            var firstServer = new AppHostServer(
                new StringReader(KernelAppServerTestProtocol.WithInitialize(firstTurnJson)),
                new StringWriter(),
                new KernelThreadStore(storePath),
                httpClient: httpClient);
            await firstServer.RunAsync(CancellationToken.None);

            var persistedStore = new KernelThreadStore(storePath);
            await persistedStore.InitializeAsync(CancellationToken.None);
            var persistedThread = await persistedStore.GetThreadAsync(threadId, CancellationToken.None);
            Assert.NotNull(persistedThread);
            Assert.True(persistedThread!.ConfigSnapshot is null || !persistedThread.ConfigSnapshot.ProviderHttpFallbackEnabled);
            var sessionPath = persistedStore.RolloutRecorder.GetRolloutPath(threadId);
            if (File.Exists(sessionPath))
            {
                var sessionText = await File.ReadAllTextAsync(sessionPath, CancellationToken.None);
                Assert.DoesNotContain("\"providerHttpFallbackEnabled\":true", sessionText, StringComparison.Ordinal);
            }

            var secondWriter = new StringWriter();
            var secondServer = new AppHostServer(
                new StringReader(KernelAppServerTestProtocol.WithInitialize(secondTurnJson)),
                secondWriter,
                new KernelThreadStore(storePath),
                httpClient: httpClient);
            await secondServer.RunAsync(CancellationToken.None);

            Assert.Equal(2, httpHandler.RequestCount);
            Assert.Equal(2, websocketServer.HandshakeCount);
            Assert.Equal(2, websocketServer.ConnectionCount);
            Assert.Contains("SECOND HTTP OK", secondWriter.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldPersistSessionTurnBeforeEmittingTerminalTurnCompleted()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);

        const string threadId = "thread_responses_session_persist_before_terminal_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", Path.Combine(root, "tianshu-home"));

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var handler = new CapturingSequencedSseHandler([BuildAssistantMessageStream("resp-session-order-1", "SESSION ORDER OK")]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请直接完成并返回确认" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var sessionPath = threadStore.RolloutRecorder.GetRolloutPath(threadId);
            string? sessionSnapshotAtTerminal = null;
            var terminalObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new InterceptingStringWriter(async line =>
            {
                if (!line.Contains("\"method\":\"turn/completed\"", StringComparison.Ordinal))
                {
                    return;
                }

                sessionSnapshotAtTerminal = File.Exists(sessionPath)
                    ? await File.ReadAllTextAsync(sessionPath, CancellationToken.None)
                    : null;
                terminalObserved.TrySetResult(true);
            });
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await terminalObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.NotNull(sessionSnapshotAtTerminal);
            Assert.Contains("\"type\":\"session_meta\"", sessionSnapshotAtTerminal, StringComparison.Ordinal);
            Assert.Contains("\"type\":\"turn\"", sessionSnapshotAtTerminal, StringComparison.Ordinal);
            Assert.Contains("\"assistantMessage\":\"SESSION ORDER OK\"", sessionSnapshotAtTerminal, StringComparison.Ordinal);

            var verifyStore = new KernelThreadStore(storePath);
            await verifyStore.InitializeAsync(CancellationToken.None);
            var turnLogs = await verifyStore.StateStore.ListTurnLogsAsync(threadId, CancellationToken.None);
            Assert.Contains(turnLogs, static log => log.Phase == "turn.rollout.persist" && log.Status == "completed");
            Assert.Contains(turnLogs, static log => log.Phase == "turn.rollout.close" && log.Status == "completed");
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldAlignResponsesRequestWithTianShuSettings()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        Directory.CreateDirectory(repoRoot);

        const string threadId = "thread_responses_prompt_injection_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            await File.WriteAllTextAsync(Path.Combine(tianShuHome, "AGENTS.md"), "home agents");
            await File.WriteAllTextAsync(Path.Combine(repoRoot, "AGENTS.md"), "workspace agents");
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
                model = "gpt-5.4"
                default_protocol = "responses"
                model_reasoning_effort = "xhigh"
                model_reasoning_summary = "auto"
                model_verbosity = "high"
                """);

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var handler = new CapturingSequencedSseHandler([BuildAssistantMessageStream("resp-prompt-1", "OK")]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    providerWireApi = ProviderWireApi.Responses,
                    input = new[]
                    {
                        new { text = "现在几点？" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            var requestBody = Assert.Single(handler.RequestBodies);
            using var requestJson = JsonDocument.Parse(requestBody);
            var instructions = requestJson.RootElement.GetProperty("instructions").GetString();
            Assert.False(string.IsNullOrWhiteSpace(instructions));
            Assert.StartsWith(
                ProviderModelCatalogs.GetBaseInstructions("gpt-5.4").TrimEnd(),
                instructions!.TrimEnd(),
                StringComparison.Ordinal);
            Assert.Contains("你是天枢（TianShu）", instructions, StringComparison.Ordinal);
            Assert.DoesNotContain("Codex CLI", instructions, StringComparison.Ordinal);
            Assert.DoesNotContain("You are Codex", instructions, StringComparison.Ordinal);

            var developerMessage = ExtractResponsesDeveloperMessageText(requestJson.RootElement.GetProperty("input"));
            Assert.NotNull(developerMessage);
            Assert.DoesNotContain(
                ProviderModelCatalogs.GetBaseInstructions("gpt-5.4").TrimEnd(),
                developerMessage,
                StringComparison.Ordinal);
            Assert.Contains("你现在处于 Default 模式。", developerMessage, StringComparison.Ordinal);
            Assert.Contains("Default 模式下不能使用 `request_user_input` 工具。", developerMessage, StringComparison.Ordinal);
            Assert.DoesNotContain("home agents", developerMessage, StringComparison.Ordinal);
            Assert.DoesNotContain("workspace agents", developerMessage, StringComparison.Ordinal);

            var userMessages = ExtractResponsesUserMessageTexts(requestJson.RootElement.GetProperty("input"));
            Assert.Contains(
                userMessages,
                static text => text.StartsWith("# AGENTS.md instructions for", StringComparison.Ordinal)
                    && text.Contains("workspace agents", StringComparison.Ordinal));
            Assert.DoesNotContain(
                userMessages,
                static text => text.Contains(ProviderModelCatalogs.GetBaseInstructions("gpt-5.4").TrimEnd(), StringComparison.Ordinal));

            var reasoning = requestJson.RootElement.GetProperty("reasoning");
            Assert.Equal("xhigh", reasoning.GetProperty("effort").GetString());
            Assert.Equal("auto", reasoning.GetProperty("summary").GetString());

            var include = requestJson.RootElement.GetProperty("include").EnumerateArray().Select(static item => item.GetString()).ToArray();
            Assert.Contains("reasoning.encrypted_content", include);

            var text = requestJson.RootElement.GetProperty("text");
            Assert.Equal("high", text.GetProperty("verbosity").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldUsePromptBaseReplaceForResponsesInstructions()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        Directory.CreateDirectory(repoRoot);

        const string threadId = "thread_responses_prompt_replace_001";
        const string customBaseInstructions = "CUSTOM TIANSHU BASE";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
                model = "gpt-5.4"
                default_protocol = "responses"
                """);
            var promptPackDirectory = Path.Combine(tianShuHome, "modules", "prompts", "default");
            Directory.CreateDirectory(promptPackDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(promptPackDirectory, "prompt.toml"),
                $$"""
                id = "default"
                enabled = true
                priority = 0

                [base]
                mode = "replace"
                text = "{{customBaseInstructions}}"
                """);

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var handler = new CapturingSequencedSseHandler([BuildAssistantMessageStream("resp-prompt-replace-1", "OK")]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    providerWireApi = ProviderWireApi.Responses,
                    input = new[]
                    {
                        new { text = "测试 prompt replace" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            var requestBody = Assert.Single(handler.RequestBodies);
            using var requestJson = JsonDocument.Parse(requestBody);
            var instructions = requestJson.RootElement.GetProperty("instructions").GetString();
            Assert.Equal(customBaseInstructions, instructions);
            Assert.DoesNotContain(
                ProviderModelCatalogs.GetBaseInstructions("gpt-5.4").TrimEnd(),
                instructions,
                StringComparison.Ordinal);
            Assert.DoesNotContain("Codex CLI", instructions, StringComparison.Ordinal);
            Assert.DoesNotContain("You are Codex", instructions, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldAdvertiseRequestUserInputInDefaultModeWhenFeatureEnabled()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, ".tianshu");
        Directory.CreateDirectory(tianShuHome);
        await WriteTianShuHomeConfigAsync(tianShuHome, """
            [features]
            default_mode_request_user_input = true
            """);

        const string threadId = "thread_request_user_input_default_mode_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var stream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-request-user-input-default-mode-1" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_text.delta",
                    delta = "OK",
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "message",
                        role = "assistant",
                        content = new object[]
                        {
                            new { type = "output_text", text = "OK" },
                        },
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = "resp-request-user-input-default-mode-1" },
                }));

            var handler = new CapturingSequencedSseHandler([stream]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "需要时可以询问我" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            var requestBody = Assert.Single(handler.RequestBodies);
            using var requestJson = JsonDocument.Parse(requestBody);
            var developerMessage = ExtractResponsesDeveloperMessageText(requestJson.RootElement.GetProperty("input"));
            Assert.NotNull(developerMessage);
            Assert.Contains("Default 模式下可以使用 `request_user_input` 工具。", developerMessage, StringComparison.Ordinal);
            Assert.Contains("优先使用 `request_user_input` 工具", developerMessage, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldPreferCompletedResponsesItemsForFollowUpInput()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);

        var notePath = Path.Combine(repoRoot, "note.txt");
        await File.WriteAllTextAsync(notePath, "hello-follow-up");

        const string threadId = "thread_responses_follow_up_items_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", Path.Combine(root, "tianshu-home"));

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var notePathJson = notePath.Replace('\\', '/');
            var functionCallArgs = JsonSerializer.Serialize(new
            {
                file_path = notePathJson,
            });

            var firstStream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-follow-1" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.added",
                    item = new
                    {
                        type = "message",
                        role = "assistant",
                        content = new object[]
                        {
                            new { type = "output_text", text = "部分消息" },
                        },
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "message",
                        role = "assistant",
                        content = new object[]
                        {
                            new { type = "output_text", text = "完整助手消息" },
                        },
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "function_call",
                        name = "read_file",
                        arguments = functionCallArgs,
                        call_id = "call-follow-1",
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = "resp-follow-1" },
                }));

            var secondStream = BuildAssistantMessageStream("resp-follow-2", "DONE");

            var handler = new CapturingSequencedSseHandler([firstStream, secondStream]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请继续处理" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            Assert.Equal(2, handler.RequestCount);

            using var secondRequestJson = JsonDocument.Parse(handler.RequestBodies[1]);
            var input = secondRequestJson.RootElement.GetProperty("input");
            Assert.Equal(JsonValueKind.Array, input.ValueKind);
            var items = input.EnumerateArray().ToArray();
            Assert.Contains(items, static item => item.GetProperty("type").GetString() == "message"
                && item.GetProperty("role").GetString() == "assistant"
                && item.GetProperty("content")[0].GetProperty("text").GetString() == "完整助手消息");
            Assert.DoesNotContain(items, static item => item.GetProperty("type").GetString() == "message"
                && item.GetProperty("role").GetString() == "assistant"
                && item.GetProperty("content")[0].GetProperty("text").GetString() == "部分消息");
            Assert.Contains(items, static item => item.GetProperty("type").GetString() == "function_call"
                && item.GetProperty("call_id").GetString() == "call-follow-1");
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldSurfaceNestedResponsesFailureDetails()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);

        const string threadId = "thread_responses_failure_details_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", Path.Combine(root, "tianshu-home"));

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var failedStream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-failure-1" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.failed",
                    response = new
                    {
                        id = "resp-failure-1",
                        error = new
                        {
                            message = "backend exploded",
                            code = "server_error",
                            type = "server_error",
                        },
                    },
                }));

            var handler = new CapturingSequencedSseHandler([failedStream]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请分析当前目录" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(
                reader,
                writer,
                threadStore,
                httpClient: httpClient,
                responsesStreamMaxRetries: 0,
                responsesStreamRetryBaseDelay: TimeSpan.Zero);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            Assert.Contains(
                "response.failed: backend exploded, code=server_error, type=server_error",
                writer.ToString(),
                StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRetryRetryableResponsesStreamFailureAndCompleteTurn()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);

        const string threadId = "thread_responses_retry_success_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", Path.Combine(root, "tianshu-home"));

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var failedStream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-retry-1" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.failed",
                    response = new
                    {
                        id = "resp-retry-1",
                        error = new
                        {
                            message = "backend exploded",
                            code = "server_error",
                            type = "server_error",
                        },
                    },
                }));

            var recoveredStream = BuildAssistantMessageStream("resp-retry-2", "RETRY OK");
            var handler = new CapturingSequencedSseHandler([failedStream, recoveredStream]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请继续处理" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(
                reader,
                writer,
                threadStore,
                httpClient: httpClient,
                responsesStreamMaxRetries: 1,
                responsesStreamRetryBaseDelay: TimeSpan.Zero);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            var output = writer.ToString();
            Assert.Equal(2, handler.RequestCount);
            Assert.Contains("\"willRetry\":true", output, StringComparison.Ordinal);
            Assert.DoesNotContain("\"willRetry\":false", output, StringComparison.Ordinal);
            Assert.Contains("Reconnecting... 1/1", output, StringComparison.Ordinal);

            var verifyStore = new KernelThreadStore(storePath);
            await verifyStore.InitializeAsync(CancellationToken.None);
            var thread = await verifyStore.GetThreadAsync(threadId, CancellationToken.None);
            Assert.NotNull(thread);
            Assert.NotEmpty(thread!.Turns);
            Assert.Contains("RETRY OK", thread.Turns[^1].AssistantMessage ?? string.Empty, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRetryMalformedResponsesStreamEventAndCompleteTurn()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);

        const string threadId = "thread_responses_malformed_retry_success_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", Path.Combine(root, "tianshu-home"));

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var malformedStream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-malformed-1" },
                }),
                """{"type" "response.completed"}""");
            var recoveredStream = BuildAssistantMessageStream("resp-malformed-2", "MALFORMED RETRY OK");
            var handler = new CapturingSequencedSseHandler([malformedStream, recoveredStream]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请继续处理" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(
                reader,
                writer,
                threadStore,
                httpClient: httpClient,
                responsesStreamMaxRetries: 1,
                responsesStreamRetryBaseDelay: TimeSpan.Zero);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            var output = writer.ToString();
            Assert.Equal(2, handler.RequestCount);
            Assert.Contains("\"willRetry\":true", output, StringComparison.Ordinal);
            Assert.Contains("responses stream emitted invalid JSON event", output, StringComparison.Ordinal);
            Assert.Contains("MALFORMED RETRY OK", output, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldFailAfterExhaustingRetryableResponsesStreamRetries()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);

        const string threadId = "thread_responses_retry_exhausted_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", Path.Combine(root, "tianshu-home"));

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var firstFailedStream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-retry-fail-1" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.failed",
                    response = new
                    {
                        id = "resp-retry-fail-1",
                        error = new
                        {
                            message = "backend exploded once",
                            code = "server_error",
                            type = "server_error",
                        },
                    },
                }));

            var secondFailedStream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-retry-fail-2" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.failed",
                    response = new
                    {
                        id = "resp-retry-fail-2",
                        error = new
                        {
                            message = "backend exploded twice",
                            code = "server_error",
                            type = "server_error",
                        },
                    },
                }));

            var handler = new CapturingSequencedSseHandler([firstFailedStream, secondFailedStream]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请继续处理" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(
                reader,
                writer,
                threadStore,
                httpClient: httpClient,
                responsesStreamMaxRetries: 1,
                responsesStreamRetryBaseDelay: TimeSpan.Zero);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            var output = writer.ToString();
            Assert.Equal(2, handler.RequestCount);
            Assert.Contains("\"willRetry\":true", output, StringComparison.Ordinal);
            Assert.Contains("\"willRetry\":false", output, StringComparison.Ordinal);
            Assert.Contains("Reconnecting... 1/1", output, StringComparison.Ordinal);
            Assert.Contains(
                "response.failed: backend exploded twice, code=server_error, type=server_error",
                output,
                StringComparison.Ordinal);
            Assert.Contains("\"status\":\"failed\"", output, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRetryWhenResponsesStreamGoesIdleBeforeCompletedAndThenCompleteTurn()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);

        const string threadId = "thread_responses_idle_retry_success_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", Path.Combine(root, "tianshu-home"));

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var idleContent = CreateBlockingSseContent(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-idle-1" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_text.delta",
                    delta = "卡住前的增量",
                }));

            var recoveredStream = BuildAssistantMessageStream("resp-idle-2", "IDLE RETRY OK");
            var handler = new SequencedSseContentHandler(
            [
                idleContent,
                CreateSseContent(recoveredStream),
            ]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请继续处理" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(
                reader,
                writer,
                threadStore,
                httpClient: httpClient,
                responsesStreamMaxRetries: 1,
                responsesStreamRetryBaseDelay: TimeSpan.Zero,
                responsesStreamIdleTimeout: TimeSpan.FromMilliseconds(50));

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            var output = writer.ToString();
            Assert.Equal(2, handler.RequestCount);
            Assert.Contains("\"willRetry\":true", output, StringComparison.Ordinal);
            Assert.DoesNotContain("\"willRetry\":false", output, StringComparison.Ordinal);
            Assert.Contains("responses stream idle timeout before response.completed", output, StringComparison.Ordinal);
            Assert.Contains("\"status\":\"completed\"", output, StringComparison.Ordinal);

            var verifyStore = new KernelThreadStore(storePath);
            await verifyStore.InitializeAsync(CancellationToken.None);
            var thread = await verifyStore.GetThreadAsync(threadId, CancellationToken.None);
            Assert.NotNull(thread);
            Assert.NotEmpty(thread!.Turns);
            Assert.Contains("IDLE RETRY OK", thread.Turns[^1].AssistantMessage ?? string.Empty, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldFailAfterResponsesStreamIdleTimeoutRetriesExhausted()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);

        const string threadId = "thread_responses_idle_retry_exhausted_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", Path.Combine(root, "tianshu-home"));

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var firstIdleContent = CreateBlockingSseContent(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-idle-fail-1" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_text.delta",
                    delta = "第一次卡住",
                }));

            var secondIdleContent = CreateBlockingSseContent(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-idle-fail-2" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_text.delta",
                    delta = "第二次卡住",
                }));

            var handler = new SequencedSseContentHandler(
            [
                firstIdleContent,
                secondIdleContent,
            ]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请继续处理" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(
                reader,
                writer,
                threadStore,
                httpClient: httpClient,
                responsesStreamMaxRetries: 1,
                responsesStreamRetryBaseDelay: TimeSpan.Zero,
                responsesStreamIdleTimeout: TimeSpan.FromMilliseconds(50));

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            var output = writer.ToString();
            Assert.Equal(2, handler.RequestCount);
            Assert.Contains("\"willRetry\":true", output, StringComparison.Ordinal);
            Assert.Contains("\"willRetry\":false", output, StringComparison.Ordinal);
            Assert.Contains("responses stream idle timeout before response.completed", output, StringComparison.Ordinal);
            Assert.Contains("\"status\":\"failed\"", output, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldAllowResponsesToolLoopBeyondTwelveIterations()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);

        var notePath = Path.Combine(repoRoot, "note.txt");
        await File.WriteAllTextAsync(notePath, "hello-tool-loop");

        const string threadId = "thread_responses_tool_loop_long_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", Path.Combine(root, "tianshu-home"));

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);


            var notePathJson = notePath.Replace('\\', '/');
            var functionCallArgs = JsonSerializer.Serialize(new
            {
                file_path = notePathJson,
            });

            List<string> streams = [];
            for (var index = 1; index <= 13; index++)
            {
                streams.Add(BuildSseStream(
                    JsonSerializer.Serialize(new
                    {
                        type = "response.created",
                        response = new { id = $"resp-{index}" },
                    }),
                    JsonSerializer.Serialize(new
                    {
                        type = "response.output_item.done",
                        item = new
                        {
                            type = "function_call",
                            name = "read_file",
                            arguments = functionCallArgs,
                            call_id = $"call-{index}",
                        },
                    }),
                    JsonSerializer.Serialize(new
                    {
                        type = "response.completed",
                        response = new { id = $"resp-{index}" },
                    })));
            }

            streams.Add(BuildAssistantMessageStream("resp-final", "DONE"));

            var handler = new SequencedSseHandler(streams);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请连续执行工具直到完成" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            Assert.Equal(14, handler.RequestCount);

            var verifyStore = new KernelThreadStore(storePath);
            await verifyStore.InitializeAsync(CancellationToken.None);
            var thread = await verifyStore.GetThreadAsync(threadId, CancellationToken.None);
            Assert.NotNull(thread);
            Assert.NotEmpty(thread!.Turns);
            Assert.Contains("DONE", thread.Turns[^1].AssistantMessage ?? string.Empty, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldStopResponsesToolLoopAtCurrentSafetyCap()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);

        var notePath = Path.Combine(repoRoot, "note.txt");
        await File.WriteAllTextAsync(notePath, "hello-tool-loop");

        const string threadId = "thread_responses_tool_loop_beyond_kernel_cap_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", Path.Combine(root, "tianshu-home"));

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);


            var notePathJson = notePath.Replace('\\', '/');
            var functionCallArgs = JsonSerializer.Serialize(new
            {
                file_path = notePathJson,
            });

            List<string> streams = [];
            for (var index = 1; index <= 96; index++)
            {
                streams.Add(BuildSseStream(
                    JsonSerializer.Serialize(new
                    {
                        type = "response.created",
                        response = new { id = $"resp-cap-{index}" },
                    }),
                    JsonSerializer.Serialize(new
                    {
                        type = "response.output_item.done",
                        item = new
                        {
                            type = "function_call",
                            name = "read_file",
                            arguments = functionCallArgs,
                            call_id = $"call-cap-{index}",
                        },
                    }),
                    JsonSerializer.Serialize(new
                    {
                        type = "response.completed",
                        response = new { id = $"resp-cap-{index}" },
                    })));
            }

            streams.Add(BuildAssistantMessageStream("resp-cap-final", "DONE AFTER 96"));

            var handler = new SequencedSseHandler(streams);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请持续执行工具直到最终完成" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            Assert.Equal(14, handler.RequestCount);

            var verifyStore = new KernelThreadStore(storePath);
            await verifyStore.InitializeAsync(CancellationToken.None);
            var thread = await verifyStore.GetThreadAsync(threadId, CancellationToken.None);
            Assert.NotNull(thread);
            Assert.NotEmpty(thread!.Turns);
            Assert.DoesNotContain("DONE AFTER 96", thread.Turns[^1].AssistantMessage ?? string.Empty, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldExecuteResponsesToolLoopWithParallelToolCalls()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);

        const string threadId = "thread_responses_tool_loop_parallel_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);


            var barrierId = $"barrier_{Guid.NewGuid():N}";
            var functionCallArgs = JsonSerializer.Serialize(new
            {
                barrier = new
                {
                    id = barrierId,
                    participants = 2,
                    timeout_ms = 1500,
                },
            });

            var firstStream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-1" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "function_call",
                        name = "test_sync_tool",
                        arguments = functionCallArgs,
                        call_id = "call-1",
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "function_call",
                        name = "test_sync_tool",
                        arguments = functionCallArgs,
                        call_id = "call-2",
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = "resp-1" },
                }));

            var secondStream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-2" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_text.delta",
                    delta = "OK",
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "message",
                        role = "assistant",
                        content = new object[]
                        {
                            new { type = "output_text", text = "OK" },
                        },
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = "resp-2" },
                }));

            var handler = new CapturingSequencedSseHandler([firstStream, secondStream]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请并行执行两个同步工具调用" },
                    },
                    model = "gpt-5.4",
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            Assert.Equal(2, handler.RequestCount);
            Assert.Equal(2, handler.RequestBodies.Count);

            using (var firstRequestJson = JsonDocument.Parse(handler.RequestBodies[0]))
            {
                var rootJson = firstRequestJson.RootElement;
                Assert.True(rootJson.TryGetProperty("parallel_tool_calls", out var ptc));
                Assert.True(ptc.GetBoolean());
                Assert.False(rootJson.TryGetProperty("previous_response_id", out _));
            }

            using (var secondRequestJson = JsonDocument.Parse(handler.RequestBodies[1]))
            {
                var rootJson = secondRequestJson.RootElement;
                Assert.True(rootJson.TryGetProperty("parallel_tool_calls", out var ptc));
                Assert.True(ptc.GetBoolean());
                Assert.False(rootJson.TryGetProperty("previous_response_id", out _));

                var input = rootJson.GetProperty("input");
                Assert.Equal(JsonValueKind.Array, input.ValueKind);

                var functionCalls = new List<JsonElement>();
                var outputs = new List<JsonElement>();
                foreach (var item in input.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (item.TryGetProperty("type", out var type)
                        && type.ValueKind == JsonValueKind.String)
                    {
                        if (string.Equals(type.GetString(), "function_call", StringComparison.Ordinal))
                        {
                            functionCalls.Add(item);
                        }
                        else if (string.Equals(type.GetString(), "function_call_output", StringComparison.Ordinal))
                        {
                            outputs.Add(item);
                        }
                    }
                }

                Assert.Equal(2, functionCalls.Count);
                Assert.Contains(functionCalls, static x => x.GetProperty("call_id").GetString() == "call-1");
                Assert.Contains(functionCalls, static x => x.GetProperty("call_id").GetString() == "call-2");
                Assert.Equal(2, outputs.Count);
                Assert.Contains(outputs, static x => x.GetProperty("call_id").GetString() == "call-1"
                                                    && x.GetProperty("output").GetString()!.Contains("stdout:", StringComparison.Ordinal)
                                                    && x.GetProperty("output").GetString()!.Contains("ok", StringComparison.Ordinal));
                Assert.Contains(outputs, static x => x.GetProperty("call_id").GetString() == "call-2"
                                                    && x.GetProperty("output").GetString()!.Contains("stdout:", StringComparison.Ordinal)
                                                    && x.GetProperty("output").GetString()!.Contains("ok", StringComparison.Ordinal));
            }

            var verifyStore = new KernelThreadStore(storePath);
            await verifyStore.InitializeAsync(CancellationToken.None);
            var thread = await verifyStore.GetThreadAsync(threadId, CancellationToken.None);
            Assert.NotNull(thread);
            Assert.NotEmpty(thread!.Turns);
            Assert.Contains("OK", thread.Turns[^1].AssistantMessage ?? string.Empty, StringComparison.Ordinal);

            var sqliteThread = await verifyStore.StateStore.GetThreadAsync(threadId, CancellationToken.None);
            Assert.NotNull(sqliteThread);
            var sqliteThreadPayload = KernelStoredThreadStateTestHelper.DeserializePayload(sqliteThread!);
            Assert.NotEmpty(sqliteThreadPayload.Turns);

            var turnLogs = await verifyStore.StateStore.ListTurnLogsAsync(threadId, CancellationToken.None);
            Assert.Contains(turnLogs, static log => log.Phase == "turn.started" && log.Status == "inProgress");
            Assert.Contains(turnLogs, static log => log.Phase == "turn.completed" && log.Status == "completed");
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }
    [Fact]
    public async Task RunAsync_ShouldSendStructuredFunctionCallOutputForViewImage()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        Directory.CreateDirectory(repoRoot);
        Directory.CreateDirectory(tianShuHome);

        var imagePath = Path.Combine(repoRoot, "pixel.png");
        WritePng(imagePath, 1, 1);

        const string threadId = "thread_responses_tool_loop_view_image_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);


            var imagePathJson = imagePath.Replace('\\', '/');
            var functionCallArgs = JsonSerializer.Serialize(new
            {
                path = imagePathJson,
            });

            var firstStream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-view-1" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "function_call",
                        name = "view_image",
                        arguments = functionCallArgs,
                        call_id = "call-view-1",
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = "resp-view-1" },
                }));

            var secondStream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-view-2" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_text.delta",
                    delta = "图像已读取",
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "message",
                        role = "assistant",
                        content = new object[]
                        {
                            new { type = "output_text", text = "图像已读取" },
                        },
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = "resp-view-2" },
                }));

            var handler = new CapturingSequencedSseHandler([firstStream, secondStream]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    model = "gpt-5.3-codex",
                    input = new[]
                    {
                        new { text = "请查看这张图片" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            Assert.Equal(2, handler.RequestCount);

            using var secondRequestJson = JsonDocument.Parse(handler.RequestBodies[1]);
            var input = secondRequestJson.RootElement.GetProperty("input");
            var output = input.EnumerateArray()
                .First(static item => item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("type", out var type)
                    && type.ValueKind == JsonValueKind.String
                    && type.GetString() == "function_call_output"
                    && item.GetProperty("call_id").GetString() == "call-view-1")
                .GetProperty("output");

            Assert.Equal(JsonValueKind.Array, output.ValueKind);
            var contentItem = Assert.Single(output.EnumerateArray());
            Assert.Equal("input_image", contentItem.GetProperty("type").GetString());
            Assert.StartsWith("data:image/png;base64,", contentItem.GetProperty("image_url").GetString(), StringComparison.Ordinal);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var imageViewStarted = Assert.Single(messages.Where(static x =>
                    IsNotificationMethod(x.RootElement, "item/started")
                    && x.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "imageView"));
                var imageViewStartedItem = imageViewStarted.RootElement.GetProperty("params").GetProperty("item");
                Assert.Equal(imagePath, imageViewStartedItem.GetProperty("path").GetString());

                var imageViewCompleted = Assert.Single(messages.Where(static x =>
                    IsNotificationMethod(x.RootElement, "item/completed")
                    && x.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "imageView"));
                var imageViewCompletedItem = imageViewCompleted.RootElement.GetProperty("params").GetProperty("item");
                Assert.Equal(imagePath, imageViewCompletedItem.GetProperty("path").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }

            var verifyStore = new KernelThreadStore(storePath);
            await verifyStore.InitializeAsync(CancellationToken.None);
            var thread = await verifyStore.GetThreadAsync(threadId, CancellationToken.None);
            Assert.NotNull(thread);
            Assert.Contains("图像已读取", thread!.Turns[^1].AssistantMessage ?? string.Empty, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldReturnUnsupportedMessageForViewImageOnTextOnlyModel_WithoutImageViewLifecycle()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        Directory.CreateDirectory(repoRoot);
        Directory.CreateDirectory(tianShuHome);

        var imagePath = Path.Combine(repoRoot, "pixel.png");
        WritePng(imagePath, 1, 1);
        await WriteModelsCacheAsync(
            Path.Combine(tianShuHome, "models_cache.json"),
            new
            {
                slug = "text-only-view-image-test-model",
                display_name = "Text-only view_image test model",
                description = "cached text-only model",
                default_reasoning_level = "medium",
                supported_reasoning_levels = new[]
                {
                    new
                    {
                        effort = "medium",
                        description = "Balances speed and reasoning depth for everyday tasks",
                    },
                },
                shell_type = "shell_command",
                visibility = "list",
                supported_in_api = true,
                priority = 0,
                base_instructions = "base instructions",
                supports_reasoning_summaries = false,
                default_reasoning_summary = "auto",
                support_verbosity = false,
                supports_parallel_tool_calls = false,
                supports_image_detail_original = false,
                input_modalities = new[] { "text" },
                prefer_websockets = false,
                supports_search_tool = false,
            });

        const string threadId = "thread_responses_tool_loop_view_image_text_only_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var imagePathJson = imagePath.Replace('\\', '/');
            var functionCallArgs = JsonSerializer.Serialize(new
            {
                path = imagePathJson,
            });

            var firstStream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-view-text-only-1" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "function_call",
                        name = "view_image",
                        arguments = functionCallArgs,
                        call_id = "call-view-text-only-1",
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = "resp-view-text-only-1" },
                }));

            var secondStream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-view-text-only-2" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_text.delta",
                    delta = "done",
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "message",
                        role = "assistant",
                        content = new object[]
                        {
                            new { type = "output_text", text = "done" },
                        },
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = "resp-view-text-only-2" },
                }));

            var handler = new CapturingSequencedSseHandler([firstStream, secondStream]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    model = "gpt-oss-120b",
                    input = new[]
                    {
                        new { text = "请查看这张图片" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            Assert.Equal(2, handler.RequestCount);

            using (var firstRequestJson = JsonDocument.Parse(handler.RequestBodies[0]))
            {
                Assert.Contains(
                    firstRequestJson.RootElement.GetProperty("tools").EnumerateArray(),
                    static tool => tool.TryGetProperty("name", out var name)
                                   && string.Equals(name.GetString(), "view_image", StringComparison.Ordinal));
            }

            using (var secondRequestJson = JsonDocument.Parse(handler.RequestBodies[1]))
            {
                var input = secondRequestJson.RootElement.GetProperty("input");
                var functionCallOutput = input.EnumerateArray()
                    .First(static item => item.ValueKind == JsonValueKind.Object
                        && item.TryGetProperty("type", out var type)
                        && type.ValueKind == JsonValueKind.String
                        && type.GetString() == "function_call_output"
                        && item.GetProperty("call_id").GetString() == "call-view-text-only-1");
                var output = ExtractFunctionCallOutputText(functionCallOutput.GetProperty("output"));
                Assert.Contains(KernelViewImageRuntimeSupport.UnsupportedImageInputsMessage, output, StringComparison.Ordinal);
            }

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                Assert.DoesNotContain(messages, static message =>
                    message.RootElement.TryGetProperty("method", out var method)
                    && (string.Equals(method.GetString(), "item/started", StringComparison.Ordinal)
                        || string.Equals(method.GetString(), "item/completed", StringComparison.Ordinal))
                    && message.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "imageView");
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
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }
    [Fact]
    public async Task RunAsync_ShouldEmitWebSearchLifecycleNotificationsForResponsesOutputItems()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        const string threadId = "thread_responses_web_search_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var stream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-web-1" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.added",
                    item = new
                    {
                        type = "web_search_call",
                        id = "ws_123",
                        status = "in_progress",
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "web_search_call",
                        id = "ws_123",
                        status = "completed",
                        action = new
                        {
                            type = "search",
                            query = "weather seattle",
                            queries = new[] { "weather seattle", "seattle weather now" },
                        },
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_text.delta",
                    delta = "查到了。",
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "message",
                        role = "assistant",
                        content = new object[]
                        {
                            new { type = "output_text", text = "查到了。" },
                        },
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = "resp-web-1" },
                }));

            var handler = new SequencedSseHandler([stream]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请搜索天气" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            var output = writer.ToString();
            Assert.DoesNotContain("\"method\":\"error\"", output, StringComparison.Ordinal);

            var messages = ParseOutputMessages(writer);
            try
            {
                var started = messages.Single(static x =>
                    IsNotificationMethod(x.RootElement, "item/started")
                    && x.RootElement.TryGetProperty("params", out var parameters)
                    && parameters.TryGetProperty("item", out var item)
                    && string.Equals(item.GetProperty("type").GetString(), "webSearch", StringComparison.Ordinal)
                    && string.Equals(item.GetProperty("id").GetString(), "ws_123", StringComparison.Ordinal));
                var startedItem = started.RootElement.GetProperty("params").GetProperty("item");
                Assert.Equal(string.Empty, startedItem.GetProperty("query").GetString());
                Assert.True(startedItem.TryGetProperty("action", out var startedAction));
                Assert.Equal(JsonValueKind.Null, startedAction.ValueKind);

                var completed = messages.Single(static x =>
                    IsNotificationMethod(x.RootElement, "item/completed")
                    && x.RootElement.TryGetProperty("params", out var parameters)
                    && parameters.TryGetProperty("item", out var item)
                    && string.Equals(item.GetProperty("type").GetString(), "webSearch", StringComparison.Ordinal)
                    && string.Equals(item.GetProperty("id").GetString(), "ws_123", StringComparison.Ordinal));
                var completedItem = completed.RootElement.GetProperty("params").GetProperty("item");
                Assert.Equal("weather seattle", completedItem.GetProperty("query").GetString());
                var action = completedItem.GetProperty("action");
                Assert.Equal("search", action.GetProperty("type").GetString());
                Assert.Equal("weather seattle", action.GetProperty("query").GetString());
                Assert.Equal(2, action.GetProperty("queries").GetArrayLength());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }

            var verifyStore = new KernelThreadStore(storePath);
            await verifyStore.InitializeAsync(CancellationToken.None);
            var thread = await verifyStore.GetThreadAsync(threadId, CancellationToken.None);
            Assert.NotNull(thread);
            Assert.Contains("查到了。", thread!.Turns[^1].AssistantMessage ?? string.Empty, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldEmitImageGenerationLifecycleNotificationsWhenProviderReturnsImageOnly()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        const string threadId = "thread_responses_image_generation_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var stream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-image-1" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "image_generation_call",
                        id = "ig_123",
                        status = "completed",
                        revised_prompt = "A tiny blue square",
                        result = "Zm9v",
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = "resp-image-1" },
                }));

            var handler = new SequencedSseHandler([stream]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请生成一张图片" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            var output = writer.ToString();
            Assert.DoesNotContain("\"method\":\"error\"", output, StringComparison.Ordinal);

            var savedPath = Path.Combine(root, "ig_123.png");
            Assert.True(File.Exists(savedPath));
            Assert.Equal(new byte[] { 102, 111, 111 }, await File.ReadAllBytesAsync(savedPath));

            var messages = ParseOutputMessages(writer);
            try
            {
                var started = messages.Single(static x =>
                    IsNotificationMethod(x.RootElement, "item/started")
                    && x.RootElement.TryGetProperty("params", out var parameters)
                    && parameters.TryGetProperty("item", out var item)
                    && string.Equals(item.GetProperty("type").GetString(), "imageGeneration", StringComparison.Ordinal)
                    && string.Equals(item.GetProperty("id").GetString(), "ig_123", StringComparison.Ordinal));
                var startedItem = started.RootElement.GetProperty("params").GetProperty("item");
                Assert.Equal("in_progress", startedItem.GetProperty("status").GetString());
                Assert.Equal(string.Empty, startedItem.GetProperty("result").GetString());
                Assert.True(startedItem.TryGetProperty("revisedPrompt", out var startedPrompt));
                Assert.Equal(JsonValueKind.Null, startedPrompt.ValueKind);

                var completed = messages.Single(static x =>
                    IsNotificationMethod(x.RootElement, "item/completed")
                    && x.RootElement.TryGetProperty("params", out var parameters)
                    && parameters.TryGetProperty("item", out var item)
                    && string.Equals(item.GetProperty("type").GetString(), "imageGeneration", StringComparison.Ordinal)
                    && string.Equals(item.GetProperty("id").GetString(), "ig_123", StringComparison.Ordinal));
                var completedItem = completed.RootElement.GetProperty("params").GetProperty("item");
                Assert.Equal("completed", completedItem.GetProperty("status").GetString());
                Assert.Equal("A tiny blue square", completedItem.GetProperty("revisedPrompt").GetString());
                Assert.Equal("Zm9v", completedItem.GetProperty("result").GetString());

                var rawResponse = messages.Single(static x =>
                    IsNotificationMethod(x.RootElement, "rawResponseItem/completed")
                    && x.RootElement.TryGetProperty("params", out var parameters)
                    && parameters.TryGetProperty("item", out var item)
                    && string.Equals(item.GetProperty("type").GetString(), "image_generation_call", StringComparison.Ordinal)
                    && string.Equals(item.GetProperty("id").GetString(), "ig_123", StringComparison.Ordinal));
                Assert.Equal("Zm9v", rawResponse.RootElement.GetProperty("params").GetProperty("item").GetProperty("result").GetString());
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
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldKeepImageGenerationLifecycleVisibleWhenImageSaveFails()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        const string threadId = "thread_responses_image_generation_save_fail_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var stream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-image-fail-1" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "image_generation_call",
                        id = "ig_invalid",
                        status = "completed",
                        revised_prompt = "broken payload",
                        result = "_-8",
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = "resp-image-fail-1" },
                }));

            var handler = new SequencedSseHandler([stream]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请生成一张图片" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            var output = writer.ToString();
            Assert.DoesNotContain("\"method\":\"error\"", output, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(root, "ig_invalid.png")));

            var messages = ParseOutputMessages(writer);
            try
            {
                Assert.Contains(messages, static x =>
                    IsNotificationMethod(x.RootElement, "item/started")
                    && x.RootElement.TryGetProperty("params", out var parameters)
                    && parameters.TryGetProperty("item", out var item)
                    && string.Equals(item.GetProperty("type").GetString(), "imageGeneration", StringComparison.Ordinal)
                    && string.Equals(item.GetProperty("id").GetString(), "ig_invalid", StringComparison.Ordinal));

                var completed = messages.Single(static x =>
                    IsNotificationMethod(x.RootElement, "item/completed")
                    && x.RootElement.TryGetProperty("params", out var parameters)
                    && parameters.TryGetProperty("item", out var item)
                    && string.Equals(item.GetProperty("type").GetString(), "imageGeneration", StringComparison.Ordinal)
                    && string.Equals(item.GetProperty("id").GetString(), "ig_invalid", StringComparison.Ordinal));
                var completedItem = completed.RootElement.GetProperty("params").GetProperty("item");
                Assert.Equal("completed", completedItem.GetProperty("status").GetString());
                Assert.Equal("broken payload", completedItem.GetProperty("revisedPrompt").GetString());
                Assert.Equal("_-8", completedItem.GetProperty("result").GetString());

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
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldAppendStructuredPluginMentionInstructionsToResponsesInstructions()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var pluginRoot = Path.Combine(tianShuHome, "plugins", "cache", "debug", "sample", "local");

        const string threadId = "thread_responses_plugin_mention_structured_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            Directory.CreateDirectory(repoRoot);
            Directory.CreateDirectory(tianShuHome);
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
                [plugins]
                enabled = true
                [plugins.installed."sample@debug"]
                enabled = true
                """);
            WriteInstalledPlugin(pluginRoot, "sample");

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);


            var handler = new CapturingSequencedSseHandler([BuildAssistantMessageStream("resp-plugin-1", "OK")]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new object[]
                    {
                        new { type = "mention", name = "sample", path = "plugin://sample@debug" },
                        new { text = "use the structured plugin mention" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            Assert.Equal(1, handler.RequestCount);
            var requestBody = Assert.Single(handler.RequestBodies);
            using var requestJson = JsonDocument.Parse(requestBody);
            var developerMessage = ExtractResponsesDeveloperMessageText(requestJson.RootElement.GetProperty("input"));
            Assert.NotNull(developerMessage);
            Assert.Contains("`sample` 插件提供的能力：", developerMessage, StringComparison.Ordinal);
            Assert.Contains("此插件的技能以 `sample:` 为前缀。", developerMessage, StringComparison.Ordinal);
            Assert.Contains("本会话可用的此插件 MCP 服务器：`sample`。", developerMessage, StringComparison.Ordinal);
            Assert.Contains("本会话可用的此插件应用：`connector_example`。", developerMessage, StringComparison.Ordinal);
            Assert.Contains("请结合这些插件关联能力完成当前任务。", developerMessage, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldAppendLinkedPluginMentionInstructionsToResponsesInstructions()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var pluginRoot = Path.Combine(tianShuHome, "plugins", "cache", "debug", "sample", "local");

        const string threadId = "thread_responses_plugin_mention_linked_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            Directory.CreateDirectory(repoRoot);
            Directory.CreateDirectory(tianShuHome);
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
                [plugins]
                enabled = true
                [plugins.installed."sample@debug"]
                enabled = true
                """);
            WriteInstalledPlugin(pluginRoot, "sample");

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);


            var handler = new CapturingSequencedSseHandler([BuildAssistantMessageStream("resp-plugin-2", "OK")]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "use [$sample](plugin://sample@debug) for this task" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            Assert.Equal(1, handler.RequestCount);
            var requestBody = Assert.Single(handler.RequestBodies);
            using var requestJson = JsonDocument.Parse(requestBody);
            var developerMessage = ExtractResponsesDeveloperMessageText(requestJson.RootElement.GetProperty("input"));
            Assert.NotNull(developerMessage);
            Assert.Contains("`sample` 插件提供的能力：", developerMessage, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldIgnorePlainPluginUriTextWhenBuildingResponsesInstructions()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var pluginRoot = Path.Combine(tianShuHome, "plugins", "cache", "debug", "sample", "local");

        const string threadId = "thread_responses_plugin_mention_plain_uri_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            Directory.CreateDirectory(repoRoot);
            Directory.CreateDirectory(tianShuHome);
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
                [plugins]
                enabled = true
                [plugins.installed."sample@debug"]
                enabled = true
                """);
            WriteInstalledPlugin(pluginRoot, "sample");

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);


            var handler = new CapturingSequencedSseHandler([BuildAssistantMessageStream("resp-plugin-3", "OK")]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "use plugin://sample@debug for this task" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            Assert.Equal(1, handler.RequestCount);
            var requestBody = Assert.Single(handler.RequestBodies);
            using var requestJson = JsonDocument.Parse(requestBody);
            var developerMessage = ExtractResponsesDeveloperMessageText(requestJson.RootElement.GetProperty("input"));
            Assert.DoesNotContain("`sample` 插件提供的能力：", developerMessage ?? string.Empty, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldAppendStructuredSkillMentionAsContextualUserMessage()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var skillRoot = Path.Combine(repoRoot, ".tianshu", "skills", "demo-skill");
        const string threadId = "thread_responses_skill_structured_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            Directory.CreateDirectory(repoRoot);
            Directory.CreateDirectory(tianShuHome);
            WriteSkill(
                skillRoot,
                """
                ---
                description: demo skill
                ---
                ## Demo
                这是一个技能正文。
                """);

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var handler = new CapturingSequencedSseHandler([BuildAssistantMessageStream("resp-skill-structured", "OK")]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new object[]
                    {
                        new { type = "skill", name = "demo-skill", path = Path.Combine(skillRoot, "SKILL.md").Replace("\\", "/") },
                        new { text = "请使用这个技能" },
                    },
                },
            });

            var server = new AppHostServer(new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson)), new StringWriter(), new KernelThreadStore(storePath), httpClient: httpClient);
            await server.RunAsync(CancellationToken.None);

            Assert.Equal(1, handler.RequestCount);
            using var requestJson = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
            var skillMessage = Assert.Single(
                ExtractResponsesUserMessageTexts(requestJson.RootElement.GetProperty("input"))
                    .Where(static text => text.StartsWith("<skill>", StringComparison.Ordinal)));
            Assert.Contains("<name>demo-skill</name>", skillMessage, StringComparison.Ordinal);
            Assert.Contains("<path>", skillMessage, StringComparison.Ordinal);
            Assert.Contains("这是一个技能正文。", skillMessage, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldAppendLinkedSkillMentionAsContextualUserMessage()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var skillRoot = Path.Combine(repoRoot, ".tianshu", "skills", "linked-skill");
        const string threadId = "thread_responses_skill_linked_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            Directory.CreateDirectory(repoRoot);
            Directory.CreateDirectory(tianShuHome);
            WriteSkill(skillRoot, "linked skill body");

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var handler = new CapturingSequencedSseHandler([BuildAssistantMessageStream("resp-skill-linked", "OK")]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请使用 [$linked-skill](skill://linked-skill) 继续。" },
                    },
                },
            });

            var server = new AppHostServer(new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson)), new StringWriter(), new KernelThreadStore(storePath), httpClient: httpClient);
            await server.RunAsync(CancellationToken.None);

            using var requestJson = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
            var skillMessage = Assert.Single(
                ExtractResponsesUserMessageTexts(requestJson.RootElement.GetProperty("input"))
                    .Where(static text => text.StartsWith("<skill>", StringComparison.Ordinal)));
            Assert.Contains("<name>linked-skill</name>", skillMessage, StringComparison.Ordinal);
            Assert.Contains("linked skill body", skillMessage, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldAppendLinkedSkillMentionFromWhitespaceSeparatedLocalSkillPath()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo with spaces");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var skillRoot = Path.Combine(repoRoot, ".tianshu", "skills", "linked-skill");
        const string threadId = "thread_responses_skill_linked_local_path_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            Directory.CreateDirectory(repoRoot);
            Directory.CreateDirectory(tianShuHome);
            WriteSkill(skillRoot, "linked skill body from local path");

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var handler = new CapturingSequencedSseHandler([BuildAssistantMessageStream("resp-skill-linked-local-path", "OK")]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var skillDocumentPath = Path.Combine(skillRoot, "SKILL.md").Replace("\\", "/");
            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = $"请使用 [$linked-skill]   ( {skillDocumentPath} ) 继续。" },
                    },
                },
            });

            var server = new AppHostServer(new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson)), new StringWriter(), new KernelThreadStore(storePath), httpClient: httpClient);
            await server.RunAsync(CancellationToken.None);

            using var requestJson = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
            var skillMessage = Assert.Single(
                ExtractResponsesUserMessageTexts(requestJson.RootElement.GetProperty("input"))
                    .Where(static text => text.StartsWith("<skill>", StringComparison.Ordinal)));
            Assert.Contains("linked skill body from local path", skillMessage, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldAppendLinkedSkillMentionFromCanonicalSkillUriPath_WhenDuplicateNamesExist()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var repoSkillRoot = Path.Combine(repoRoot, ".tianshu", "skills", "linear");
        var userSkillRoot = Path.Combine(tianShuHome, "modules", "skills", "linear");
        const string threadId = "thread_responses_skill_linked_canonical_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            Directory.CreateDirectory(repoRoot);
            Directory.CreateDirectory(tianShuHome);
            WriteSkill(repoSkillRoot, "project linear skill body");
            WriteSkill(userSkillRoot, "user linear skill body");

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var handler = new CapturingSequencedSseHandler([BuildAssistantMessageStream("resp-skill-linked-canonical", "OK")]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var skillDocumentPath = Path.Combine(repoSkillRoot, "SKILL.md").Replace("\\", "/");
            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = $"请使用 [$linear](skill://{skillDocumentPath}) 继续。" },
                    },
                },
            });

            var server = new AppHostServer(new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson)), new StringWriter(), new KernelThreadStore(storePath), httpClient: httpClient);
            await server.RunAsync(CancellationToken.None);

            using var requestJson = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
            var skillMessage = Assert.Single(
                ExtractResponsesUserMessageTexts(requestJson.RootElement.GetProperty("input"))
                    .Where(static text => text.StartsWith("<skill>", StringComparison.Ordinal)));
            Assert.Contains("<name>linear</name>", skillMessage, StringComparison.Ordinal);
            Assert.Contains("project linear skill body", skillMessage, StringComparison.Ordinal);
            Assert.DoesNotContain("user linear skill body", skillMessage, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldResolveStructuredSkillMentionByPathToSkillsMd_WhenDuplicateNamesExist()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var repoSkillRoot = Path.Combine(repoRoot, ".tianshu", "skills", "linear");
        var userSkillRoot = Path.Combine(tianShuHome, "modules", "skills", "linear");
        const string threadId = "thread_responses_skill_structured_canonical_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            Directory.CreateDirectory(repoRoot);
            Directory.CreateDirectory(tianShuHome);
            WriteSkill(repoSkillRoot, "project structured linear skill body");
            WriteSkill(userSkillRoot, "user structured linear skill body");

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var handler = new CapturingSequencedSseHandler([BuildAssistantMessageStream("resp-skill-structured-canonical", "OK")]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var skillDocumentPath = Path.Combine(repoSkillRoot, "SKILL.md").Replace("\\", "/");
            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new object[]
                    {
                        new
                        {
                            type = "skill",
                            name = "linear",
                            path = "skill://linear",
                            pathToSkillsMd = skillDocumentPath,
                        },
                        new { text = "请继续使用 $linear 完成任务。" },
                    },
                },
            });

            var server = new AppHostServer(new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson)), new StringWriter(), new KernelThreadStore(storePath), httpClient: httpClient);
            await server.RunAsync(CancellationToken.None);

            using var requestJson = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
            var skillMessages = ExtractResponsesUserMessageTexts(requestJson.RootElement.GetProperty("input"))
                .Where(static text => text.StartsWith("<skill>", StringComparison.Ordinal))
                .ToArray();
            var skillMessage = Assert.Single(skillMessages);
            Assert.Contains("<name>linear</name>", skillMessage, StringComparison.Ordinal);
            Assert.Contains("project structured linear skill body", skillMessage, StringComparison.Ordinal);
            Assert.DoesNotContain("user structured linear skill body", skillMessage, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldNotTreatLinkedAppMentionAsSkillMention()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var skillRoot = Path.Combine(repoRoot, ".agents", "skills", "linear");
        const string threadId = "thread_responses_skill_linked_app_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            Directory.CreateDirectory(tianShuHome);
            WriteSkill(skillRoot, "linked app should not resolve as skill");

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var handler = new CapturingSequencedSseHandler([BuildAssistantMessageStream("resp-skill-linked-app", "OK")]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请查看 [$linear](app://linear) 的能力。" },
                    },
                },
            });

            var writer = new StringWriter();
            var server = new AppHostServer(new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson)), writer, new KernelThreadStore(storePath), httpClient: httpClient);
            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            Assert.True(handler.RequestBodies.Count == 1, writer.ToString());
            using var requestJson = JsonDocument.Parse(handler.RequestBodies[0]);
            Assert.DoesNotContain(
                ExtractResponsesUserMessageTexts(requestJson.RootElement.GetProperty("input")),
                static text => text.StartsWith("<skill>", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldNotTreatAppSlugPlainMentionAsSkillMention()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var skillRoot = Path.Combine(repoRoot, ".agents", "skills", "linear");
        var projectConfigPath = Path.Combine(repoRoot, ".tianshu", "tianshu.toml");
        const string threadId = "thread_responses_skill_plain_app_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.GetDirectoryName(projectConfigPath)!);
            await File.WriteAllTextAsync(
                projectConfigPath,
                """
                [apps.linear]
                enabled = true
                isAccessible = true
                """);
            WriteSkill(skillRoot, "plain app slug should not resolve as skill");

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var handler = new CapturingSequencedSseHandler([BuildAssistantMessageStream("resp-skill-plain-app", "OK")]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请优先使用 $linear 完成这个任务。" },
                    },
                },
            });

            var writer = new StringWriter();
            var server = new AppHostServer(new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson)), writer, new KernelThreadStore(storePath), httpClient: httpClient);
            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            Assert.True(handler.RequestBodies.Count == 1, writer.ToString());
            using var requestJson = JsonDocument.Parse(handler.RequestBodies[0]);
            Assert.DoesNotContain(
                ExtractResponsesUserMessageTexts(requestJson.RootElement.GetProperty("input")),
                static text => text.StartsWith("<skill>", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldNotTreatCommonEnvironmentVariableAsSkillMention()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var skillRoot = Path.Combine(repoRoot, ".agents", "skills", "PATH");
        const string threadId = "thread_responses_skill_env_var_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            Directory.CreateDirectory(tianShuHome);
            WriteSkill(skillRoot, "common env var should not resolve as skill");

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var handler = new CapturingSequencedSseHandler([BuildAssistantMessageStream("resp-skill-env-var", "OK")]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请检查当前 $PATH 是否已经正确设置。" },
                    },
                },
            });

            var writer = new StringWriter();
            var server = new AppHostServer(new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson)), writer, new KernelThreadStore(storePath), httpClient: httpClient);
            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            Assert.True(handler.RequestBodies.Count == 1, writer.ToString());
            using var requestJson = JsonDocument.Parse(handler.RequestBodies[0]);
            Assert.DoesNotContain(
                ExtractResponsesUserMessageTexts(requestJson.RootElement.GetProperty("input")),
                static text => text.StartsWith("<skill>", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldNotFallbackToPlainSkillMention_WhenStructuredSkillReferenceIsExplicitButUnresolved()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var skillRoot = Path.Combine(repoRoot, ".agents", "skills", "linear");
        const string threadId = "thread_responses_skill_structured_block_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            Directory.CreateDirectory(tianShuHome);
            WriteSkill(skillRoot, "structured unresolved mention should block plain fallback");

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var handler = new CapturingSequencedSseHandler([BuildAssistantMessageStream("resp-skill-structured-block", "OK")]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new object[]
                    {
                        new { type = "skill", name = "linear", path = "skill://missing-linear" },
                        new { text = "请继续使用 $linear 完成任务。" },
                    },
                },
            });

            var writer = new StringWriter();
            var server = new AppHostServer(new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson)), writer, new KernelThreadStore(storePath), httpClient: httpClient);
            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            Assert.True(handler.RequestBodies.Count == 1, writer.ToString());
            using var requestJson = JsonDocument.Parse(handler.RequestBodies[0]);
            Assert.DoesNotContain(
                ExtractResponsesUserMessageTexts(requestJson.RootElement.GetProperty("input")),
                static text => text.StartsWith("<skill>", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldAppendAtLinkedPluginMentionInstructionsToResponsesInstructions()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var pluginRoot = Path.Combine(tianShuHome, "plugins", "cache", "debug", "sample", "local");
        const string threadId = "thread_responses_plugin_mention_at_linked_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            Directory.CreateDirectory(repoRoot);
            Directory.CreateDirectory(tianShuHome);
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
                [plugins]
                enabled = true
                [plugins.installed."sample@debug"]
                enabled = true
                """);
            WriteInstalledPlugin(pluginRoot, "sample");

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var handler = new CapturingSequencedSseHandler([BuildAssistantMessageStream("resp-plugin-at", "OK")]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请使用 [@sample](plugin://sample@debug) 处理。" },
                    },
                },
            });

            var server = new AppHostServer(new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson)), new StringWriter(), new KernelThreadStore(storePath), httpClient: httpClient);
            await server.RunAsync(CancellationToken.None);

            using var requestJson = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
            var developerMessage = ExtractResponsesDeveloperMessageText(requestJson.RootElement.GetProperty("input"));
            Assert.NotNull(developerMessage);
            Assert.Contains("`sample` 插件提供的能力：", developerMessage, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRequestEnvDependencyBeforeSendingProviderRequest()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var skillRoot = Path.Combine(repoRoot, ".tianshu", "skills", "env-skill");
        const string threadId = "thread_responses_skill_env_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            Directory.CreateDirectory(repoRoot);
            Directory.CreateDirectory(tianShuHome);
            WriteSkill(
                skillRoot,
                "env skill body",
                """
                name: env-skill
                dependencies:
                  tools:
                    - type: env_var
                      value: GITHUB_TOKEN
                      description: GitHub token
                """);

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var handler = new CapturingSequencedSseHandler([BuildAssistantMessageStream("resp-skill-env", "OK")]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new object[]
                    {
                        new { type = "skill", name = "env-skill", path = Path.Combine(skillRoot, "SKILL.md").Replace("\\", "/") },
                        new { text = "请按技能执行" },
                    },
                },
            });

            var writer = new StringWriter();
            var server = new AppHostServer(new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson)), writer, new KernelThreadStore(storePath), httpClient: httpClient);
            var runTask = server.RunAsync(CancellationToken.None);

            await WaitForWriterContainsAsync(writer, "\"method\":\"item/tool/requestUserInput\"", TimeSpan.FromSeconds(5));
            Assert.Equal(0, handler.RequestCount);

            var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");
            var pendingRequest = Assert.Single(pending);
            pendingRequest.Value.TrySetResult(JsonSerializer.SerializeToElement(new
            {
                answers = new Dictionary<string, object?>
                {
                    ["GITHUB_TOKEN"] = new
                    {
                        answers = new[] { "github-token-placeholder" },
                    },
                },
            }));

            await runTask;
            Assert.True(handler.RequestCount >= 1);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldInstallMissingMcpDependencyIntoWorkspaceConfigBeforeProviderRequest()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var skillRoot = Path.Combine(repoRoot, ".tianshu", "skills", "mcp-skill");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        const string threadId = "thread_responses_skill_mcp_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            Directory.CreateDirectory(repoRoot);
            Directory.CreateDirectory(tianShuHome);
            WriteSkill(
                skillRoot,
                "mcp skill body",
                """
                name: mcp-skill
                dependencies:
                  tools:
                    - type: mcp
                      value: github
                      transport: streamable_http
                      url: https://example.com/mcp
                """);

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var handler = new CapturingSequencedSseHandler([BuildAssistantMessageStream("resp-skill-mcp", "OK")]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new object[]
                    {
                        new { type = "skill", name = "mcp-skill", path = Path.Combine(skillRoot, "SKILL.md").Replace("\\", "/") },
                        new { text = "请按技能执行" },
                    },
                },
            });

            var writer = new StringWriter();
            var server = new AppHostServer(new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson)), writer, new KernelThreadStore(storePath), httpClient: httpClient);
            var runTask = server.RunAsync(CancellationToken.None);

            await WaitForWriterContainsAsync(writer, "\"method\":\"item/tool/requestUserInput\"", TimeSpan.FromSeconds(5));
            Assert.Equal(0, handler.RequestCount);

            var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");
            var pendingRequest = Assert.Single(pending);
            pendingRequest.Value.TrySetResult(JsonSerializer.SerializeToElement(new
            {
                answers = new Dictionary<string, object?>
                {
                    ["github"] = new
                    {
                        answers = new[] { "立即安装 (Recommended)" },
                    },
                },
            }));

            await runTask;
            Assert.True(handler.RequestCount >= 1);
            Assert.True(File.Exists(userConfigPath));
            var userConfig = await File.ReadAllTextAsync(userConfigPath);
            Assert.Contains("[mcp_servers.github]", userConfig, StringComparison.Ordinal);
            Assert.Contains("enabled = true", userConfig, StringComparison.Ordinal);
            Assert.Contains("url = \"https://example.com/mcp\"", userConfig, StringComparison.Ordinal);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                Assert.Contains(messages, static message => IsNotificationMethod(message.RootElement, "mcpServerStatus/list/updated"));
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
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldAppendRealtimeDeveloperInstructionsToResponsesInstructions()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        const string threadId = "thread_responses_realtime_active_001";
        const string sessionId = "rt_session_active_001";
        const string customRealtimeStartInstructions = "Custom realtime bridge instructions.";

        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            Directory.CreateDirectory(repoRoot);
            Directory.CreateDirectory(tianShuHome);
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $$"""
                experimental_realtime_start_instructions = "{{customRealtimeStartInstructions}}"
                """);

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var handler = new CapturingSequencedSseHandler([BuildAssistantMessageStream("resp-realtime-active", "ACTIVE OK")]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = string.Join(
                Environment.NewLine,
                JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "thread/realtime/start",
                    @params = new
                    {
                        threadId,
                        sessionId,
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id = 2,
                    method = "turn/start",
                    @params = new
                    {
                        threadId,
                        input = new[]
                        {
                            new { text = "continue after realtime" },
                        },
                    },
                }));

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            var requestBody = Assert.Single(handler.RequestBodies);
            using var requestJson = JsonDocument.Parse(requestBody);
            var developerMessage = ExtractResponsesDeveloperMessageText(requestJson.RootElement.GetProperty("input"));
            Assert.NotNull(developerMessage);
            Assert.Contains("<realtime_conversation>", developerMessage, StringComparison.Ordinal);
            Assert.Contains(customRealtimeStartInstructions, developerMessage, StringComparison.Ordinal);
            Assert.Contains("</realtime_conversation>", developerMessage, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldAppendRealtimeEndInstructionsAfterRealtimeStops()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        const string threadId = "thread_responses_realtime_inactive_001";
        const string sessionId = "rt_session_inactive_001";

        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", Path.Combine(root, "tianshu-home"));

        try
        {
            Directory.CreateDirectory(repoRoot);

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var handler = new CapturingSequencedSseHandler([BuildAssistantMessageStream("resp-realtime-inactive", "INACTIVE OK")]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = string.Join(
                Environment.NewLine,
                JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "thread/realtime/start",
                    @params = new
                    {
                        threadId,
                        sessionId,
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id = 2,
                    method = "thread/realtime/stop",
                    @params = new
                    {
                        threadId,
                        sessionId,
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id = 3,
                    method = "turn/start",
                    @params = new
                    {
                        threadId,
                        input = new[]
                        {
                            new { text = "continue after realtime stop" },
                        },
                    },
                }));

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            var requestBody = Assert.Single(handler.RequestBodies);
            using var requestJson = JsonDocument.Parse(requestBody);
            var developerMessage = ExtractResponsesDeveloperMessageText(requestJson.RootElement.GetProperty("input"));
            Assert.NotNull(developerMessage);
            Assert.Contains("<realtime_conversation>", developerMessage, StringComparison.Ordinal);
            Assert.Contains("实时会话已结束。", developerMessage, StringComparison.Ordinal);
            Assert.Contains("原因：inactive", developerMessage, StringComparison.Ordinal);
            Assert.Contains("</realtime_conversation>", developerMessage, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldPreCompactThreadWhenAutoCompactLimitIsReached()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        Directory.CreateDirectory(repoRoot);
        Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
        Directory.CreateDirectory(Path.Combine(repoRoot, ".tianshu"));
        await File.WriteAllTextAsync(Path.Combine(repoRoot, ".tianshu", "tianshu.toml"), "model_auto_compact_token_limit = 1" + Environment.NewLine);

        const string threadId = "thread_responses_auto_compact_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            var repoRootTomlPath = repoRoot.Replace("\\", "/", StringComparison.Ordinal);
            await WriteTianShuHomeConfigAsync(tianShuHome, $$"""
[projects."{{repoRootTomlPath}}"]
trust_level = "trusted"
""");

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            for (var index = 0; index < 14; index++)
            {
                await setupStore.AppendCompletedTurnAsync(
                    threadId,
                    $"turn_seed_{index:000}",
                    $"用户消息 {new string('u', 40)} {index}",
                    $"助手回复 {new string('a', 40)} {index}",
                    "completed",
                    CancellationToken.None);
            }

            var handler = new CapturingSequencedSseHandler([BuildAssistantMessageStream("resp-auto-1", "AUTO OK")]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请继续总结" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            Assert.Equal(1, handler.RequestCount);

            var messages = ParseOutputMessages(writer);
            try
            {
                Assert.Contains(messages, static x => IsNotificationMethod(x.RootElement, "thread/compacted"));
                Assert.Contains(messages, static x => IsNotificationMethod(x.RootElement, "item/started")
                    && x.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "contextCompaction");
                Assert.Contains(messages, static x => IsNotificationMethod(x.RootElement, "item/completed")
                    && x.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "contextCompaction");
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }

            var verifyStore = new KernelThreadStore(storePath);
            await verifyStore.InitializeAsync(CancellationToken.None);
            var thread = await verifyStore.GetThreadAsync(threadId, CancellationToken.None);
            Assert.NotNull(thread);
            Assert.Contains(thread!.Turns, static turn => turn.Id.StartsWith("compact_", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldMidTurnCompactResponsesFollowUpWhenToolOutputPushesPastAutoCompactLimit()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        Directory.CreateDirectory(repoRoot);
        Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
        Directory.CreateDirectory(Path.Combine(repoRoot, ".tianshu"));
        await File.WriteAllTextAsync(Path.Combine(repoRoot, ".tianshu", "tianshu.toml"), "model_auto_compact_token_limit = 1000" + Environment.NewLine);

        var notePath = Path.Combine(repoRoot, "large-note.txt");
        await File.WriteAllTextAsync(notePath, new string('x', 6000));

        const string threadId = "thread_responses_mid_turn_auto_compact_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            var repoRootTomlPath = repoRoot.Replace("\\", "/", StringComparison.Ordinal);
            await WriteTianShuHomeConfigAsync(tianShuHome, $$"""
[projects."{{repoRootTomlPath}}"]
trust_level = "trusted"
""");

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);
            for (var index = 1; index <= 13; index++)
            {
                await setupStore.AppendCompletedTurnAsync(
                    threadId,
                    $"seed_turn_{index}",
                    $"旧问题{index}",
                    $"旧回答{index}",
                    "completed",
                    CancellationToken.None);
            }

            var notePathJson = notePath.Replace('\\', '/');
            var functionCallArgs = JsonSerializer.Serialize(new
            {
                file_path = notePathJson,
            });

            var firstStream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-mid-1" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "function_call",
                        name = "read_file",
                        arguments = functionCallArgs,
                        call_id = "call-mid-1",
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = "resp-mid-1" },
                }));

            var secondStream = BuildAssistantMessageStream("resp-mid-2", "MID TURN OK");

            var handler = new CapturingSequencedSseHandler([firstStream, secondStream]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请读取大文件并继续" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            Assert.Equal(2, handler.RequestCount);
            Assert.Equal(2, handler.RequestBodies.Count);

            using (var firstRequestJson = JsonDocument.Parse(handler.RequestBodies[0]))
            {
                var rootJson = firstRequestJson.RootElement;
                Assert.False(rootJson.TryGetProperty("previous_response_id", out _));
            }

            using (var secondRequestJson = JsonDocument.Parse(handler.RequestBodies[1]))
            {
                var rootJson = secondRequestJson.RootElement;
                Assert.False(rootJson.TryGetProperty("previous_response_id", out _));

                var input = rootJson.GetProperty("input");
                Assert.Equal(JsonValueKind.Array, input.ValueKind);
                var items = input.EnumerateArray().ToArray();
                Assert.Contains(
                    items,
                    static item => item.ValueKind == JsonValueKind.Object
                        && item.TryGetProperty("type", out var type)
                        && string.Equals(type.GetString(), "function_call", StringComparison.Ordinal)
                        && item.TryGetProperty("call_id", out var callId)
                        && string.Equals(callId.GetString(), "call-mid-1", StringComparison.Ordinal));
                Assert.Contains(
                    items,
                    static item => item.ValueKind == JsonValueKind.Object
                        && item.TryGetProperty("type", out var type)
                        && string.Equals(type.GetString(), "function_call_output", StringComparison.Ordinal)
                        && item.TryGetProperty("call_id", out var callId)
                        && string.Equals(callId.GetString(), "call-mid-1", StringComparison.Ordinal));
                Assert.Contains(
                    items,
                    static item => item.ValueKind == JsonValueKind.Object
                        && item.TryGetProperty("type", out var type)
                        && string.Equals(type.GetString(), "message", StringComparison.Ordinal)
                        && item.TryGetProperty("role", out var role)
                        && string.Equals(role.GetString(), "user", StringComparison.Ordinal)
                        && item.GetProperty("content")[0].GetProperty("text").GetString() == "上下文压缩摘要");
                Assert.Contains(
                    items,
                    static item => item.ValueKind == JsonValueKind.Object
                        && item.TryGetProperty("type", out var type)
                        && string.Equals(type.GetString(), "message", StringComparison.Ordinal)
                        && item.TryGetProperty("role", out var role)
                        && string.Equals(role.GetString(), "assistant", StringComparison.Ordinal)
                        && item.GetProperty("content")[0].GetProperty("text").GetString()!.Contains("Q: 旧问题1", StringComparison.Ordinal));
            }

            var messages = ParseOutputMessages(writer);
            try
            {
                Assert.Contains(messages, static x => IsNotificationMethod(x.RootElement, "thread/compacted"));
                Assert.Contains(messages, static x => IsNotificationMethod(x.RootElement, "item/started")
                    && x.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "contextCompaction");
                Assert.Contains(messages, static x => IsNotificationMethod(x.RootElement, "item/completed")
                    && x.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "contextCompaction");
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }

            var verifyStore = new KernelThreadStore(storePath);
            await verifyStore.InitializeAsync(CancellationToken.None);
            var thread = await verifyStore.GetThreadAsync(threadId, CancellationToken.None);
            Assert.NotNull(thread);
            Assert.Contains(thread!.Turns, static turn => turn.Id.StartsWith("compact_", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(2, thread.SeedHistory.Count);
            Assert.Equal("上下文压缩摘要", thread.SeedHistory[0].Content);
            Assert.Contains("Q: 旧问题1", thread.SeedHistory[1].Content, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldCommitMidTurnSteerAfterNextToolCall()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);

        var notePath = Path.Combine(repoRoot, "note.txt");
        await File.WriteAllTextAsync(notePath, "hello-mid-turn-steer");

        const string threadId = "thread_responses_mid_turn_steer_001";
        const string steerText = "请改为仅输出最终配置字段。";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", Path.Combine(root, "tianshu-home"));

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var notePathJson = notePath.Replace('\\', '/');
            var functionCallArgs = JsonSerializer.Serialize(new
            {
                file_path = notePathJson,
            });

            var firstStream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-steer-1" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "message",
                        role = "assistant",
                        content = new object[]
                        {
                            new { type = "output_text", text = "我先读取文件。" },
                        },
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "function_call",
                        name = "read_file",
                        arguments = functionCallArgs,
                        call_id = "call-steer-1",
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = "resp-steer-1" },
                }));

            var secondStream = BuildAssistantMessageStream("resp-steer-2", "STEERED OK");

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请先读取文件再继续" },
                    },
                },
            });

            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            AppHostServer? server = null;
            var enqueueMethod = typeof(AppHostServer).GetMethod("EnqueueSteerInput", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(enqueueMethod);

            var handler = new BlockingCallbackCapturingSseHandler(
                [firstStream, secondStream],
                async cancellationToken =>
                {
                    var turnId = await WaitForTurnStartedIdAsync(writer, TimeSpan.FromSeconds(5), cancellationToken);
                    enqueueMethod!.Invoke(server, [turnId, steerText]);
                });
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var threadStore = new KernelThreadStore(storePath);
            server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            Assert.Equal(2, handler.RequestCount);
            Assert.Equal(2, handler.RequestBodies.Count);

            using (var secondRequestJson = JsonDocument.Parse(handler.RequestBodies[1]))
            {
                var input = secondRequestJson.RootElement.GetProperty("input");
                var items = input.EnumerateArray().ToArray();
                Assert.Contains(items, static item => item.GetProperty("type").GetString() == "function_call"
                    && item.GetProperty("call_id").GetString() == "call-steer-1");
                Assert.Contains(items, static item => item.GetProperty("type").GetString() == "function_call_output"
                    && item.GetProperty("call_id").GetString() == "call-steer-1");
                Assert.Contains(items, item => item.GetProperty("type").GetString() == "message"
                    && item.GetProperty("role").GetString() == "user"
                    && item.GetProperty("content")[0].GetProperty("text").GetString() == steerText);
            }

            var messages = ParseOutputMessages(writer);
            try
            {
                Assert.Contains(messages, static x => IsNotificationMethod(x.RootElement, "turn/steered")
                    && x.RootElement.GetProperty("params").GetProperty("source").GetString() == "after_next_tool_call");
                Assert.Contains(messages, x => IsNotificationMethod(x.RootElement, "item/completed")
                    && x.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "userMessage"
                    && x.RootElement.GetProperty("params").GetProperty("item").GetProperty("content")[0].GetProperty("text").GetString() == steerText);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }

            var verifyStore = new KernelThreadStore(storePath);
            await verifyStore.InitializeAsync(CancellationToken.None);
            var thread = await verifyStore.GetThreadAsync(threadId, CancellationToken.None);
            Assert.NotNull(thread);
            var turn = Assert.Single(thread!.Turns);
            Assert.Contains(turn.Items, item => string.Equals(item.Type, "userMessage", StringComparison.Ordinal)
                && item.Payload.GetProperty("content")[0].GetProperty("text").GetString() == steerText);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldNotEmitPlaceholderPlanItemForDefaultTurn()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_plan_default_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var stream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-plan-default-1" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_text.delta",
                    delta = "Default OK",
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "message",
                        role = "assistant",
                        content = new object[]
                        {
                            new { type = "output_text", text = "Default OK" },
                        },
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = "resp-plan-default-1" },
                }));

            var handler = new CapturingSequencedSseHandler([stream]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "hello" },
                    },
                },
            });

            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var threadStore = new KernelThreadStore(storePath);
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            var messages = ParseOutputMessages(writer);
            try
            {
                Assert.DoesNotContain(messages, static x => IsNotificationMethod(x.RootElement, "turn/plan/updated"));
                Assert.DoesNotContain(messages, static x => IsNotificationMethod(x.RootElement, "item/plan/delta"));
                Assert.DoesNotContain(messages, static x => IsNotificationMethod(x.RootElement, "item/started")
                    && x.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "plan");
                Assert.DoesNotContain(messages, static x => IsNotificationMethod(x.RootElement, "item/completed")
                    && x.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "plan");
            }
            finally
            {
                DisposeOutputMessages(messages);
            }

            var verifyStore = new KernelThreadStore(storePath);
            await verifyStore.InitializeAsync(CancellationToken.None);
            var thread = await verifyStore.GetThreadAsync(threadId, CancellationToken.None);
            Assert.NotNull(thread);
            var turn = Assert.Single(thread!.Turns);
            Assert.DoesNotContain(turn.Items, static item => string.Equals(item.Type, "plan", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldEmitPlanItemFromProposedPlanBlockInPlanMode()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_plan_mode_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");

        const string planText = "\n# Final plan\n- first\n- second\n";
        var fullMessage = $"Preface\n<proposed_plan>{planText}</proposed_plan>\nPostscript";

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var stream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-plan-mode-1" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_text.delta",
                    delta = "Preface\n<pro",
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_text.delta",
                    delta = "posed_plan>\n# Final plan\n- first\n",
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_text.delta",
                    delta = "- second\n</proposed_plan>\nPostscript",
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "message",
                        role = "assistant",
                        content = new object[]
                        {
                            new { type = "output_text", text = fullMessage },
                        },
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = "resp-plan-mode-1" },
                }));

            var handler = new CapturingSequencedSseHandler([stream]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    collaborationMode = new
                    {
                        mode = "plan",
                    },
                    input = new[]
                    {
                        new { text = "Plan this" },
                    },
                },
            });

            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var threadStore = new KernelThreadStore(storePath);
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            var messages = ParseOutputMessages(writer);
            try
            {
                var turnResponse = messages.Single(static x =>
                    x.RootElement.TryGetProperty("id", out var idProperty)
                    && idProperty.ValueKind == JsonValueKind.Number
                    && idProperty.GetInt32() == 1);
                var turnId = turnResponse.RootElement.GetProperty("result").GetProperty("turn").GetProperty("id").GetString();
                Assert.False(string.IsNullOrWhiteSpace(turnId));

                var expectedPlanItemId = $"{turnId}-plan";
                Assert.DoesNotContain(messages, static x => IsNotificationMethod(x.RootElement, "turn/plan/updated"));

                var planDeltas = messages
                    .Where(static x => IsNotificationMethod(x.RootElement, "item/plan/delta"))
                    .Select(static x => x.RootElement.GetProperty("params"))
                    .ToArray();
                Assert.NotEmpty(planDeltas);
                Assert.All(planDeltas, payload => Assert.Equal(expectedPlanItemId, payload.GetProperty("itemId").GetString()));
                Assert.Equal(planText, string.Concat(planDeltas.Select(static payload => payload.GetProperty("delta").GetString())));

                var startedPlan = Assert.Single(messages.Where(static x => IsNotificationMethod(x.RootElement, "item/started")
                    && x.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "plan"));
                Assert.Equal(expectedPlanItemId, startedPlan.RootElement.GetProperty("params").GetProperty("item").GetProperty("id").GetString());

                var completedPlan = Assert.Single(messages.Where(static x => IsNotificationMethod(x.RootElement, "item/completed")
                    && x.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "plan"));
                Assert.Equal(expectedPlanItemId, completedPlan.RootElement.GetProperty("params").GetProperty("item").GetProperty("id").GetString());
                Assert.Equal(planText, completedPlan.RootElement.GetProperty("params").GetProperty("item").GetProperty("text").GetString());

                var completedAgentMessage = Assert.Single(messages.Where(static x => IsNotificationMethod(x.RootElement, "item/completed")
                    && x.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "agentMessage"));
                var assistantText = completedAgentMessage.RootElement.GetProperty("params").GetProperty("item").GetProperty("text").GetString();
                Assert.DoesNotContain("<proposed_plan>", assistantText, StringComparison.Ordinal);
                Assert.DoesNotContain("</proposed_plan>", assistantText, StringComparison.Ordinal);
                Assert.Contains("Preface", assistantText, StringComparison.Ordinal);
                Assert.Contains("Postscript", assistantText, StringComparison.Ordinal);
            }
            finally
            {
                DisposeOutputMessages(messages);
            }

            var verifyStore = new KernelThreadStore(storePath);
            await verifyStore.InitializeAsync(CancellationToken.None);
            var thread = await verifyStore.GetThreadAsync(threadId, CancellationToken.None);
            Assert.NotNull(thread);
            var turn = Assert.Single(thread!.Turns);
            Assert.DoesNotContain("<proposed_plan>", turn.AssistantMessage ?? string.Empty, StringComparison.Ordinal);
            Assert.Contains(turn.Items, item => string.Equals(item.Type, "plan", StringComparison.Ordinal)
                && item.Payload.GetProperty("text").GetString() == planText);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldNotEmitPlanItemWithoutProposedPlanBlockInPlanMode()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_plan_mode_002";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var stream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-plan-mode-2" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_text.delta",
                    delta = "Done",
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "message",
                        role = "assistant",
                        content = new object[]
                        {
                            new { type = "output_text", text = "Done" },
                        },
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = "resp-plan-mode-2" },
                }));

            var handler = new CapturingSequencedSseHandler([stream]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    collaborationMode = new
                    {
                        mode = "plan",
                    },
                    input = new[]
                    {
                        new { text = "Plan this" },
                    },
                },
            });

            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var threadStore = new KernelThreadStore(storePath);
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            var messages = ParseOutputMessages(writer);
            try
            {
                Assert.DoesNotContain(messages, static x => IsNotificationMethod(x.RootElement, "turn/plan/updated"));
                Assert.DoesNotContain(messages, static x => IsNotificationMethod(x.RootElement, "item/plan/delta"));
                Assert.DoesNotContain(messages, static x => IsNotificationMethod(x.RootElement, "item/started")
                    && x.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "plan");
                Assert.DoesNotContain(messages, static x => IsNotificationMethod(x.RootElement, "item/completed")
                    && x.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "plan");
            }
            finally
            {
                DisposeOutputMessages(messages);
            }

            var verifyStore = new KernelThreadStore(storePath);
            await verifyStore.InitializeAsync(CancellationToken.None);
            var thread = await verifyStore.GetThreadAsync(threadId, CancellationToken.None);
            Assert.NotNull(thread);
            var turn = Assert.Single(thread!.Turns);
            Assert.DoesNotContain(turn.Items, static item => string.Equals(item.Type, "plan", StringComparison.Ordinal));
            Assert.Equal("Done", turn.AssistantMessage);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    private static List<JsonDocument> ParseOutputMessages(StringWriter writer)
    {
        return writer
            .ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static line => JsonDocument.Parse(line))
            .ToList();
    }

    private static void DisposeOutputMessages(IEnumerable<JsonDocument> messages)
    {
        foreach (var message in messages)
        {
            message.Dispose();
        }
    }

    private static string BuildAssistantMessageStream(string responseId, string text)
    {
        return BuildSseStream(
            JsonSerializer.Serialize(new
            {
                type = "response.created",
                response = new { id = responseId },
            }),
            JsonSerializer.Serialize(new
            {
                type = "response.output_item.done",
                item = new
                {
                    type = "message",
                    role = "assistant",
                    content = new object[]
                    {
                        new { type = "output_text", text = text },
                    },
                },
            }),
            JsonSerializer.Serialize(new
            {
                type = "response.completed",
                response = new { id = responseId },
            }));
    }

    private static void WriteInstalledPlugin(string pluginRoot, string pluginName)
    {
        WriteFile(
            Path.Combine(pluginRoot, ".tianshu-plugin", "plugin.json"),
            $$"""{"name":"{{pluginName}}"}""");
        WriteFile(
            Path.Combine(pluginRoot, "skills", "search", "SKILL.md"),
            """
            ---
            description: sample search
            ---
            """);
        WriteFile(
            Path.Combine(pluginRoot, ".mcp.json"),
            """
            {
              "mcpServers": {
                "sample": {
                  "command": "rg",
                  "args": ["--version"]
                }
              }
            }
            """);
        WriteFile(
            Path.Combine(pluginRoot, ".app.json"),
            """
            {
              "apps": {
                "example": {
                  "id": "connector_example"
                }
              }
            }
            """);
    }

    private static void WriteSkill(string skillRoot, string skillMarkdown, string? metadataYaml = null)
    {
        WriteFile(Path.Combine(skillRoot, "SKILL.md"), skillMarkdown);
        if (!string.IsNullOrWhiteSpace(metadataYaml))
        {
            WriteFile(Path.Combine(skillRoot, "agents", "tianshu.yaml"), metadataYaml);
        }
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var normalizedContent = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", Environment.NewLine, StringComparison.Ordinal);
        File.WriteAllText(path, normalizedContent);
    }

    private static bool IsNotificationMethod(JsonElement json, string method)
    {
        return json.ValueKind == JsonValueKind.Object
            && json.TryGetProperty("method", out var methodProperty)
            && string.Equals(methodProperty.GetString(), method, StringComparison.Ordinal);
    }

    private static JsonDocument[] ParseOutputDocuments(string output)
        => output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static line => JsonDocument.Parse(line))
            .ToArray();

    private static string ExtractFunctionCallOutputText(JsonElement output)
    {
        if (output.ValueKind == JsonValueKind.String)
        {
            return output.GetString() ?? string.Empty;
        }

        if (output.ValueKind != JsonValueKind.Array)
        {
            return output.ToString();
        }

        return string.Join(
            Environment.NewLine,
            output.EnumerateArray()
                .Where(static item => item.ValueKind == JsonValueKind.Object)
                .Where(static item => item.TryGetProperty("type", out var type)
                                      && string.Equals(type.GetString(), "input_text", StringComparison.Ordinal))
                .Select(static item => item.TryGetProperty("text", out var text) ? text.GetString() : null)
                .Where(static text => !string.IsNullOrWhiteSpace(text)));
    }

    private static string? ExtractResponsesDeveloperMessageText(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in input.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var typeProperty)
                || !string.Equals(typeProperty.GetString(), "message", StringComparison.Ordinal)
                || !item.TryGetProperty("role", out var roleProperty)
                || !string.Equals(roleProperty.GetString(), "developer", StringComparison.Ordinal)
                || !item.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var segments = new List<string>();
            foreach (var part in content.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.Object
                    && part.TryGetProperty("text", out var textProperty)
                    && textProperty.ValueKind == JsonValueKind.String)
                {
                    segments.Add(textProperty.GetString()!);
                }
            }

            return segments.Count == 0 ? null : string.Join(Environment.NewLine, segments);
        }

        return null;
    }

    private static IReadOnlyList<string> ExtractResponsesUserMessageTexts(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var messages = new List<string>();
        foreach (var item in input.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var typeProperty)
                || !string.Equals(typeProperty.GetString(), "message", StringComparison.Ordinal)
                || !item.TryGetProperty("role", out var roleProperty)
                || !string.Equals(roleProperty.GetString(), "user", StringComparison.Ordinal)
                || !item.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var segments = new List<string>();
            foreach (var part in content.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.Object
                    && part.TryGetProperty("text", out var textProperty)
                    && textProperty.ValueKind == JsonValueKind.String)
                {
                    segments.Add(textProperty.GetString()!);
                }
            }

            if (segments.Count > 0)
            {
                messages.Add(string.Join(Environment.NewLine, segments));
            }
        }

        return messages;
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var value = field!.GetValue(instance);
        Assert.NotNull(value);
        return Assert.IsType<T>(value);
    }

    private sealed class SequencedSseHandler : HttpMessageHandler
    {
        private readonly Queue<string> streams;

        public SequencedSseHandler(IEnumerable<string> streams)
        {
            this.streams = new Queue<string>(streams);
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var body = streams.Count > 0 ? streams.Dequeue() : string.Empty;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
            };

            return Task.FromResult(response);
        }
    }

    private sealed class CapturingSequencedSseHandler : HttpMessageHandler
    {
        private readonly Queue<string> streams;
        private readonly Queue<IReadOnlyDictionary<string, string>> responseHeaders;

        public CapturingSequencedSseHandler(
            IEnumerable<string> streams,
            IEnumerable<IReadOnlyDictionary<string, string>>? responseHeaders = null)
        {
            this.streams = new Queue<string>(streams);
            this.responseHeaders = new Queue<IReadOnlyDictionary<string, string>>(
                responseHeaders ?? Array.Empty<IReadOnlyDictionary<string, string>>());
        }

        public int RequestCount { get; private set; }

        public List<string> RequestBodies { get; } = new();

        public List<Dictionary<string, string[]>> RequestHeaders { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (request.Content is not null)
            {
                RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }
            else
            {
                RequestBodies.Add(string.Empty);
            }

            RequestHeaders.Add(
                request.Headers.ToDictionary(
                    static header => header.Key,
                    static header => header.Value.ToArray(),
                    StringComparer.OrdinalIgnoreCase));

            var body = streams.Count > 0 ? streams.Dequeue() : string.Empty;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
            };
            if (responseHeaders.Count > 0)
            {
                foreach (var header in responseHeaders.Dequeue())
                {
                    response.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return response;
        }
    }

    private sealed class SequencedSseContentHandler : HttpMessageHandler
    {
        private readonly Queue<HttpContent> contents;

        public SequencedSseContentHandler(IEnumerable<HttpContent> contents)
        {
            this.contents = new Queue<HttpContent>(contents);
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var content = contents.Count > 0
                ? contents.Dequeue()
                : CreateSseContent(string.Empty);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content,
            };

            return Task.FromResult(response);
        }
    }

    private sealed class BlockingCallbackCapturingSseHandler : HttpMessageHandler
    {
        private readonly Queue<string> streams;
        private readonly Func<CancellationToken, Task> beforeFirstResponseAsync;

        public BlockingCallbackCapturingSseHandler(IEnumerable<string> streams, Func<CancellationToken, Task> beforeFirstResponseAsync)
        {
            this.streams = new Queue<string>(streams);
            this.beforeFirstResponseAsync = beforeFirstResponseAsync;
        }

        public int RequestCount { get; private set; }

        public List<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (request.Content is not null)
            {
                RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }
            else
            {
                RequestBodies.Add(string.Empty);
            }

            if (RequestCount == 1)
            {
                await beforeFirstResponseAsync(cancellationToken);
            }

            var body = streams.Count > 0 ? streams.Dequeue() : string.Empty;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
            };

            return response;
        }
    }

    private sealed record ResponsesWebSocketConnectionPlan(
        IReadOnlyList<IReadOnlyList<string>> ResponseBatches,
        int? CloseAfterRequestCount = null,
        IReadOnlyDictionary<string, string>? ResponseHeaders = null,
        HttpStatusCode? RejectUpgradeStatusCode = null);

    private sealed class ResponsesWebSocketTestServer : IAsyncDisposable
    {
        private readonly HttpListener listener;
        private readonly CancellationTokenSource shutdown = new();
        private readonly Task acceptLoopTask;
        private readonly Channel<string> receivedRequests = Channel.CreateUnbounded<string>();
        private readonly Channel<Dictionary<string, string[]>> receivedHandshakes = Channel.CreateUnbounded<Dictionary<string, string[]>>();
        private readonly Queue<ResponsesWebSocketConnectionPlan> connectionPlans;
        private int connectionCount;
        private int handshakeCount;

        public ResponsesWebSocketTestServer(params ResponsesWebSocketConnectionPlan[] connectionPlans)
        {
            var port = GetFreeTcpPort();
            listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            this.connectionPlans = new Queue<ResponsesWebSocketConnectionPlan>(connectionPlans);
            BaseUrl = $"http://127.0.0.1:{port}/v1";
            acceptLoopTask = Task.Run(RunAsync);
        }

        public string BaseUrl { get; }

        public int ConnectionCount => Volatile.Read(ref connectionCount);

        public int HandshakeCount => Volatile.Read(ref handshakeCount);

        public async Task<string[]> WaitForRequestCountAsync(int count)
        {
            var requests = new List<string>(count);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, shutdown.Token);
            while (requests.Count < count)
            {
                requests.Add(await receivedRequests.Reader.ReadAsync(linked.Token).ConfigureAwait(false));
            }

            return requests.ToArray();
        }

        public async Task<Dictionary<string, string[]>[]> WaitForHandshakeCountAsync(int count)
        {
            var handshakes = new List<Dictionary<string, string[]>>(count);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, shutdown.Token);
            while (handshakes.Count < count)
            {
                handshakes.Add(await receivedHandshakes.Reader.ReadAsync(linked.Token).ConfigureAwait(false));
            }

            return handshakes.ToArray();
        }

        public async ValueTask DisposeAsync()
        {
            shutdown.Cancel();
            listener.Close();
            try
            {
                await acceptLoopTask.ConfigureAwait(false);
            }
            catch
            {
                // ignore shutdown failures in test server
            }

            shutdown.Dispose();
        }

        private async Task RunAsync()
        {
            try
            {
                while (!shutdown.IsCancellationRequested)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await listener.GetContextAsync().ConfigureAwait(false);
                    }
                    catch (HttpListenerException) when (shutdown.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (ObjectDisposedException) when (shutdown.IsCancellationRequested)
                    {
                        break;
                    }

                    if (!context.Request.IsWebSocketRequest)
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                        context.Response.Close();
                        continue;
                    }

                    var plan = connectionPlans.Count > 0
                        ? connectionPlans.Dequeue()
                        : new ResponsesWebSocketConnectionPlan(Array.Empty<IReadOnlyList<string>>(), CloseAfterRequestCount: 1);
                    Interlocked.Increment(ref handshakeCount);
                    receivedHandshakes.Writer.TryWrite(CaptureHeaders(context.Request.Headers));
                    if (plan.RejectUpgradeStatusCode is { } rejectStatusCode)
                    {
                        context.Response.StatusCode = (int)rejectStatusCode;
                        context.Response.Close();
                        continue;
                    }

                    if (plan.ResponseHeaders is not null)
                    {
                        foreach (var header in plan.ResponseHeaders)
                        {
                            context.Response.Headers[header.Key] = header.Value;
                        }
                    }

                    var webSocketContext = await context.AcceptWebSocketAsync(subProtocol: null).ConfigureAwait(false);
                    Interlocked.Increment(ref connectionCount);
                    await HandleConnectionAsync(webSocketContext.WebSocket, plan).ConfigureAwait(false);
                }
            }
            finally
            {
                receivedRequests.Writer.TryComplete();
                receivedHandshakes.Writer.TryComplete();
            }
        }

        private async Task HandleConnectionAsync(WebSocket webSocket, ResponsesWebSocketConnectionPlan plan)
        {
            using (webSocket)
            {
                var requestIndex = 0;
                while (!shutdown.IsCancellationRequested)
                {
                    var request = await ReceiveTextAsync(webSocket, shutdown.Token).ConfigureAwait(false);
                    if (request is null)
                    {
                        break;
                    }

                    receivedRequests.Writer.TryWrite(request);
                    if (requestIndex < plan.ResponseBatches.Count)
                    {
                        foreach (var payload in plan.ResponseBatches[requestIndex])
                        {
                            var bytes = Encoding.UTF8.GetBytes(payload);
                            await webSocket.SendAsync(
                                    new ArraySegment<byte>(bytes),
                                    WebSocketMessageType.Text,
                                    endOfMessage: true,
                                    shutdown.Token)
                                .ConfigureAwait(false);
                        }
                    }

                    requestIndex++;
                    if (plan.CloseAfterRequestCount is int closeAfter && requestIndex >= closeAfter)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None)
                            .ConfigureAwait(false);
                        break;
                    }
                }
            }
        }

        private static async Task<string?> ReceiveTextAsync(WebSocket socket, CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            using var stream = new MemoryStream();
            while (true)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                if (result.Count > 0)
                {
                    await stream.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken).ConfigureAwait(false);
                }

                if (result.EndOfMessage)
                {
                    break;
                }
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static Dictionary<string, string[]> CaptureHeaders(System.Collections.Specialized.NameValueCollection headers)
        {
            return headers.AllKeys
                .Where(static key => !string.IsNullOrWhiteSpace(key))
                .ToDictionary(
                    static key => key!,
                    key => headers.GetValues(key!) ?? Array.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase);
        }

        private static int GetFreeTcpPort()
        {
            var tcpListener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            tcpListener.Start();
            try
            {
                return ((IPEndPoint)tcpListener.LocalEndpoint).Port;
            }
            finally
            {
                tcpListener.Stop();
            }
        }
    }

    private static string BuildSseStream(params string[] jsonEvents)
    {
        var builder = new StringBuilder();
        foreach (var ev in jsonEvents)
        {
            builder.Append("data: ");
            builder.AppendLine(ev);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static HttpContent CreateSseContent(string body)
    {
        return new StringContent(body, Encoding.UTF8, "text/event-stream");
    }

    private static HttpContent CreateBlockingSseContent(params string[] jsonEvents)
    {
        var stream = new BlockingTailStream(Encoding.UTF8.GetBytes(BuildSseStream(jsonEvents)));
        var content = new StreamContent(stream);
        content.Headers.ContentType = new("text/event-stream");
        return content;
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

    private static async Task<string> WaitForTurnStartedIdAsync(StringWriter writer, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var line in lines)
            {
                using var document = JsonDocument.Parse(line);
                if (!IsNotificationMethod(document.RootElement, "turn/started"))
                {
                    continue;
                }

                var turnId = document.RootElement.GetProperty("params").GetProperty("turn").GetProperty("id").GetString();
                if (!string.IsNullOrWhiteSpace(turnId))
                {
                    return turnId!;
                }
            }

            await Task.Delay(25, cancellationToken);
        }

        throw new TimeoutException("未在限定时间内观察到 turn/started。");
    }

    private static int CountSubstring(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private sealed class QueuedTextReader : TextReader
    {
        private readonly Channel<string> lines = Channel.CreateUnbounded<string>();

        public void Enqueue(string line)
        {
            if (!lines.Writer.TryWrite(line))
            {
                throw new InvalidOperationException("无法写入测试输入行。");
            }
        }

        public void Complete()
        {
            lines.Writer.TryComplete();
        }

        public override async Task<string?> ReadLineAsync()
        {
            try
            {
                return await lines.Reader.ReadAsync().ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                return null;
            }
        }
    }

    private sealed class InterceptingStringWriter : TextWriter
    {
        private readonly StringWriter inner = new();
        private readonly Func<string, Task> onLineWrittenAsync;

        public InterceptingStringWriter(Func<string, Task> onLineWrittenAsync)
        {
            this.onLineWrittenAsync = onLineWrittenAsync;
        }

        public override Encoding Encoding => inner.Encoding;

        public override async Task WriteLineAsync(string? value)
        {
            await inner.WriteLineAsync(value);
            if (!string.IsNullOrEmpty(value))
            {
                await onLineWrittenAsync(value);
            }
        }

        public override Task FlushAsync()
            => inner.FlushAsync();

        public override string ToString()
            => inner.ToString();
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "tianshu-kernel-tool-loop-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task WriteTianShuHomeConfigAsync(string tianShuHome, string content)
    {
        Directory.CreateDirectory(tianShuHome);
        await File.WriteAllTextAsync(Path.Combine(tianShuHome, "tianshu.toml"), content);
    }

    private static Task WriteModelsCacheAsync(string cachePath, params object[] models)
    {
        var payload = JsonSerializer.Serialize(new
        {
            fetched_at = DateTimeOffset.UtcNow.ToString("O"),
            etag = (string?)null,
            client_version = 1,
            models,
        });
        return File.WriteAllTextAsync(cachePath, payload);
    }

    private static void WritePng(string imagePath, int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        image.SaveAsPng(imagePath);
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
                ResetReadOnlyAttributes(path);
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

        ResetReadOnlyAttributes(path);
        Directory.Delete(path, recursive: true);
    }

    private static void ResetReadOnlyAttributes(string path)
    {
        foreach (var directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
        {
            var attrs = File.GetAttributes(directory);
            if ((attrs & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(directory, attrs & ~FileAttributes.ReadOnly);
            }
        }

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            var attrs = File.GetAttributes(file);
            if ((attrs & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
            }
        }
    }

    private sealed class BlockingTailStream : Stream
    {
        private readonly byte[] prefixBytes;
        private readonly ManualResetEventSlim releaseEvent = new(false);
        private readonly TaskCompletionSource releaseTaskSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int position;
        private bool disposed;

        public BlockingTailStream(byte[] prefixBytes)
        {
            this.prefixBytes = prefixBytes;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => prefixBytes.Length;

        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (position < prefixBytes.Length)
            {
                var bytesToCopy = Math.Min(count, prefixBytes.Length - position);
                Buffer.BlockCopy(prefixBytes, position, buffer, offset, bytesToCopy);
                position += bytesToCopy;
                return bytesToCopy;
            }

            releaseEvent.Wait();
            return 0;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (position < prefixBytes.Length)
            {
                var bytesToCopy = Math.Min(buffer.Length, prefixBytes.Length - position);
                prefixBytes.AsMemory(position, bytesToCopy).CopyTo(buffer);
                position += bytesToCopy;
                return bytesToCopy;
            }

            await releaseTaskSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (!disposed)
            {
                disposed = true;
                releaseEvent.Set();
                releaseTaskSource.TrySetResult();
            }

            releaseEvent.Dispose();
            base.Dispose(disposing);
        }
    }
}
