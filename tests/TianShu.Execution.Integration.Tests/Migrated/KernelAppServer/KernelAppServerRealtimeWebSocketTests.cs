using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.Execution.Integration.Tests;

[Collection("EnvironmentVariables")]
public sealed class AppHostServerRealtimeWebSocketTests
{
    [Fact]
    public async Task RunAsync_ShouldUseConfiguredRealtimeStartupContextOverride()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var cwd = Path.Combine(root, "workspace");
        const string threadId = "thread_realtime_ws_override_001";
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        await using var realtimeServer = new RealtimeWebSocketTestServer(new
        {
            type = "session.updated",
            session = new
            {
                id = "sess_custom_context",
                instructions = "prompt from config\n\ncustom startup context",
            },
        });

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(cwd);
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $"""
                experimental_realtime_ws_base_url = "{realtimeServer.Uri}"
                experimental_realtime_ws_model = "realtime-test-model"
                experimental_realtime_ws_backend_prompt = "prompt from config"
                experimental_realtime_ws_startup_context = "custom startup context"
                """);

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, cwd, CancellationToken.None);
            _ = await setupStore.AppendCompletedTurnAsync(
                threadId,
                "turn_custom_context",
                "Investigate realtime startup context",
                "Reviewed websocket routing",
                "completed",
                CancellationToken.None);

            var input = """{"jsonrpc":"2.0","id":1,"method":"thread/realtime/start","params":{"threadId":"thread_realtime_ws_override_001","prompt":"prompt from op"}}""";
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var threadStore = new KernelThreadStore(storePath);
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            using var request = JsonDocument.Parse(await realtimeServer.WaitForFirstRequestAsync());
            var instructions = request.RootElement.GetProperty("session").GetProperty("instructions").GetString();
            Assert.Equal("session.update", request.RootElement.GetProperty("type").GetString());
            Assert.Equal("quicksilver", request.RootElement.GetProperty("session").GetProperty("type").GetString());
            Assert.Equal("prompt from config\n\ncustom startup context", instructions);
            Assert.Equal("audio/pcm", request.RootElement.GetProperty("session").GetProperty("audio").GetProperty("input").GetProperty("format").GetProperty("type").GetString());
            Assert.Equal(24000, request.RootElement.GetProperty("session").GetProperty("audio").GetProperty("input").GetProperty("format").GetProperty("rate").GetInt32());
            Assert.Equal("fathom", request.RootElement.GetProperty("session").GetProperty("audio").GetProperty("output").GetProperty("voice").GetString());
            Assert.Contains("intent=quicksilver", realtimeServer.FirstRequestPathAndQuery, StringComparison.Ordinal);
            Assert.Contains("model=realtime-test-model", realtimeServer.FirstRequestPathAndQuery, StringComparison.Ordinal);

            var messages = ParseMessages(writer.ToString());
            try
            {
                var response = messages.Single(static x => IsResponseId(x.RootElement, 1)).RootElement;
                Assert.True(response.TryGetProperty("result", out _));

                var started = messages.Single(static x => IsNotificationMethod(x.RootElement, "thread/realtime/started")).RootElement;
                Assert.Equal(threadId, started.GetProperty("params").GetProperty("threadId").GetString());
                Assert.Equal("sess_custom_context", started.GetProperty("params").GetProperty("sessionId").GetString());
            }
            finally
            {
                DisposeMessages(messages);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldDisableRealtimeStartupContextWhenOverrideIsEmpty()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var cwd = Path.Combine(root, "workspace");
        const string threadId = "thread_realtime_ws_no_context_001";
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        await using var realtimeServer = new RealtimeWebSocketTestServer(new
        {
            type = "session.updated",
            session = new
            {
                id = "sess_no_context",
                instructions = "prompt from config",
            },
        });

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(cwd);
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $"""
                experimental_realtime_ws_base_url = "{realtimeServer.Uri}"
                experimental_realtime_ws_backend_prompt = "prompt from config"
                experimental_realtime_ws_startup_context = ""
                """);

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, cwd, CancellationToken.None);
            _ = await setupStore.AppendCompletedTurnAsync(
                threadId,
                "turn_no_context",
                "Investigate realtime startup context",
                "Reviewed websocket routing",
                "completed",
                CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(cwd, "README.md"), "workspace marker");

            var input = """{"jsonrpc":"2.0","id":1,"method":"thread/realtime/start","params":{"threadId":"thread_realtime_ws_no_context_001","prompt":"prompt from op"}}""";
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var threadStore = new KernelThreadStore(storePath);
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            using var request = JsonDocument.Parse(await realtimeServer.WaitForFirstRequestAsync());
            var instructions = request.RootElement.GetProperty("session").GetProperty("instructions").GetString();
            Assert.Equal("prompt from config", instructions);
            Assert.DoesNotContain("来自 TianShu 的启动上下文。", instructions ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("README.md", instructions ?? string.Empty, StringComparison.Ordinal);
            Assert.Equal("quicksilver", request.RootElement.GetProperty("session").GetProperty("type").GetString());
            Assert.Contains("intent=quicksilver", realtimeServer.FirstRequestPathAndQuery, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldBuildRealtimeStartupContextFromThreadHistoryAndWorkspace()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var cwd = Path.Combine(root, "workspace");
        const string threadId = "thread_realtime_ws_generated_context_001";
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        await using var realtimeServer = new RealtimeWebSocketTestServer(new
        {
            type = "session.updated",
            session = new
            {
                id = "sess_generated_context",
                instructions = "prompt from config",
            },
        });

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(cwd);
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $"""
                experimental_realtime_ws_base_url = "{realtimeServer.Uri}"
                experimental_realtime_ws_backend_prompt = "prompt from config"
                """);

            await File.WriteAllTextAsync(Path.Combine(cwd, "README.md"), "workspace marker");
            Directory.CreateDirectory(Path.Combine(cwd, "docs"));
            await File.WriteAllTextAsync(Path.Combine(cwd, "docs", "notes.txt"), "notes");

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            var thread = await setupStore.CreateThreadAsync(threadId, cwd, CancellationToken.None);
            thread!.GitInfo = new KernelGitInfoRecord
            {
                Branch = "branch-latest",
            };
            _ = await setupStore.UpdateThreadGitInfoAsync(threadId, hasSha: false, sha: null, hasBranch: true, branch: "branch-latest", hasOriginUrl: false, originUrl: null, CancellationToken.None);
            _ = await setupStore.AppendCompletedTurnAsync(
                threadId,
                "turn_generated_context",
                "Investigate realtime startup context",
                "Reviewed websocket routing",
                "completed",
                CancellationToken.None);

            var input = """{"jsonrpc":"2.0","id":1,"method":"thread/realtime/start","params":{"threadId":"thread_realtime_ws_generated_context_001","prompt":"prompt from op"}}""";
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var threadStore = new KernelThreadStore(storePath);
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            using var request = JsonDocument.Parse(await realtimeServer.WaitForFirstRequestAsync());
            var instructions = request.RootElement.GetProperty("session").GetProperty("instructions").GetString();
            Assert.NotNull(instructions);
            Assert.StartsWith("prompt from config\n\n来自 TianShu 的启动上下文。", instructions, StringComparison.Ordinal);
            Assert.Contains("## 近期工作", instructions, StringComparison.Ordinal);
            Assert.Contains("最新分支：branch-latest", instructions, StringComparison.Ordinal);
            Assert.Contains("Investigate realtime startup context", instructions, StringComparison.Ordinal);
            Assert.Contains("## 机器 / 工作区地图", instructions, StringComparison.Ordinal);
            Assert.Contains("README.md", instructions, StringComparison.Ordinal);
            Assert.Contains("notes.txt", instructions, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    private static JsonDocument[] ParseMessages(string output)
    {
        return output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static line => JsonDocument.Parse(line))
            .ToArray();
    }

    private static async Task WaitForWriterContainsAsync(StringWriter writer, string expected, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (writer.ToString().Contains(expected, StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        throw new TimeoutException($"Timed out waiting for writer output to contain: {expected}");
    }

    private static void DisposeMessages(IEnumerable<JsonDocument> messages)
    {
        foreach (var message in messages)
        {
            message.Dispose();
        }
    }

    private static bool IsResponseId(JsonElement json, int id)
    {
        return json.TryGetProperty("id", out var idElement)
               && idElement.ValueKind == JsonValueKind.Number
               && idElement.GetInt32() == id;
    }

    private static bool IsNotificationMethod(JsonElement json, string method)
    {
        return json.TryGetProperty("method", out var methodElement)
               && string.Equals(methodElement.GetString(), method, StringComparison.Ordinal);
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
    }

    private sealed class RealtimeWebSocketTestServer : IAsyncDisposable
    {
        private readonly HttpListener listener;
        private readonly CancellationTokenSource shutdown = new();
        private readonly Task acceptLoopTask;
        private readonly TaskCompletionSource<string> firstRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Channel<string> receivedRequests = Channel.CreateUnbounded<string>();
        private readonly string[] responsePayloads;

        public RealtimeWebSocketTestServer(params object[] responses)
        {
            var port = GetFreeTcpPort();
            listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            responsePayloads = responses.Select(static response => JsonSerializer.Serialize(response)).ToArray();
            Uri = $"ws://127.0.0.1:{port}/";
            acceptLoopTask = Task.Run(RunAsync);
        }

        public string Uri { get; }

        public string FirstRequestPathAndQuery { get; private set; } = string.Empty;

        public async Task<string> WaitForFirstRequestAsync()
        {
            var completed = await Task.WhenAny(firstRequest.Task, Task.Delay(TimeSpan.FromSeconds(5), shutdown.Token)).ConfigureAwait(false);
            if (!ReferenceEquals(completed, firstRequest.Task))
            {
                throw new TimeoutException("Timed out waiting for the realtime websocket request.");
            }

            return await firstRequest.Task.ConfigureAwait(false);
        }

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
                var context = await listener.GetContextAsync().ConfigureAwait(false);
                if (!context.Request.IsWebSocketRequest)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    context.Response.Close();
                    return;
                }

                var webSocketContext = await context.AcceptWebSocketAsync(subProtocol: null).ConfigureAwait(false);
                using var webSocket = webSocketContext.WebSocket;
                FirstRequestPathAndQuery = context.Request.RawUrl ?? string.Empty;
                var request = await ReceiveTextAsync(webSocket, shutdown.Token).ConfigureAwait(false);
                firstRequest.TrySetResult(request ?? string.Empty);
                if (request is not null)
                {
                    receivedRequests.Writer.TryWrite(request);
                }

                foreach (var responsePayload in responsePayloads)
                {
                    var bytes = Encoding.UTF8.GetBytes(responsePayload);
                    await webSocket.SendAsync(
                            new ArraySegment<byte>(bytes),
                            WebSocketMessageType.Text,
                            endOfMessage: true,
                            shutdown.Token)
                        .ConfigureAwait(false);
                }

                while (!shutdown.IsCancellationRequested)
                {
                    var message = await ReceiveTextAsync(webSocket, shutdown.Token).ConfigureAwait(false);
                    if (message is null)
                    {
                        break;
                    }

                    receivedRequests.Writer.TryWrite(message);
                }
            }
            catch (Exception ex)
            {
                firstRequest.TrySetException(ex);
            }
            finally
            {
                receivedRequests.Writer.TryComplete();
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

        private static int GetFreeTcpPort()
        {
            var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }
    }
    [Fact]
    public async Task RunAsync_ShouldSendRealtimeAppendTextThroughWebSocketTransport()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var cwd = Path.Combine(root, "workspace");
        const string threadId = "thread_realtime_ws_append_text_001";
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        await using var realtimeServer = new RealtimeWebSocketTestServer(new
        {
            type = "session.updated",
            session = new
            {
                id = "sess_append_text",
            },
        });

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(cwd);
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $"""
                experimental_realtime_ws_base_url = "{realtimeServer.Uri}"
                """);

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, cwd, CancellationToken.None);

            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"thread/realtime/start","params":{"threadId":"thread_realtime_ws_append_text_001"}}""",
                """{"jsonrpc":"2.0","id":2,"method":"thread/realtime/appendText","params":{"threadId":"thread_realtime_ws_append_text_001","sessionId":"sess_append_text","text":"hello realtime"}}""");
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var threadStore = new KernelThreadStore(storePath);
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var requests = await realtimeServer.WaitForRequestCountAsync(2);
            using var appendRequest = JsonDocument.Parse(requests[1]);
            Assert.Equal("conversation.item.create", appendRequest.RootElement.GetProperty("type").GetString());
            Assert.Equal("message", appendRequest.RootElement.GetProperty("item").GetProperty("type").GetString());
            Assert.Equal("user", appendRequest.RootElement.GetProperty("item").GetProperty("role").GetString());
            Assert.Equal("text", appendRequest.RootElement.GetProperty("item").GetProperty("content")[0].GetProperty("type").GetString());
            Assert.Equal("hello realtime", appendRequest.RootElement.GetProperty("item").GetProperty("content")[0].GetProperty("text").GetString());

            var messages = ParseMessages(writer.ToString());
            try
            {
                var appendResponse = messages.Single(static x => IsResponseId(x.RootElement, 2)).RootElement;
                Assert.True(appendResponse.TryGetProperty("result", out _));
                Assert.DoesNotContain(messages, static x => IsNotificationMethod(x.RootElement, "thread/realtime/itemAdded"));
            }
            finally
            {
                DisposeMessages(messages);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldUseRealtimeV2PayloadAndEmitHandoffRequestItem()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var cwd = Path.Combine(root, "workspace");
        const string threadId = "thread_realtime_ws_v2_handoff_001";
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        await using var realtimeServer = new RealtimeWebSocketTestServer(
            new
            {
                type = "session.updated",
                session = new
                {
                    id = "sess_v2",
                },
            },
            new
            {
                type = "conversation.item.input_audio_transcription.delta",
                delta = "need ",
            },
            new
            {
                type = "response.output_text.delta",
                delta = "working ",
            },
            new
            {
                type = "conversation.item.done",
                item = new
                {
                    id = "item_v2_1",
                    type = "function_call",
                    name = "tianshu",
                    call_id = "handoff_v2_1",
                    arguments = JsonSerializer.Serialize(new
                    {
                        prompt = "finish the task",
                    }),
                },
            });

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(cwd);
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $"""
                experimental_realtime_ws_base_url = "{realtimeServer.Uri}"
                experimental_realtime_ws_startup_context = ""

                [features]
                realtime_conversation_v2 = true
                """);

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, cwd, CancellationToken.None);

            var input = """{"jsonrpc":"2.0","id":1,"method":"thread/realtime/start","params":{"threadId":"thread_realtime_ws_v2_handoff_001","prompt":"realtime prompt"}}""";
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var threadStore = new KernelThreadStore(storePath);
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"thread/realtime/itemAdded\"", TimeSpan.FromSeconds(5));

            using var request = JsonDocument.Parse(await realtimeServer.WaitForFirstRequestAsync());
            var session = request.RootElement.GetProperty("session");
            Assert.Equal("realtime", session.GetProperty("type").GetString());
            Assert.Equal("realtime prompt", session.GetProperty("instructions").GetString());
            Assert.Equal("tianshu", session.GetProperty("tools")[0].GetProperty("name").GetString());
            Assert.DoesNotContain("intent=quicksilver", realtimeServer.FirstRequestPathAndQuery, StringComparison.Ordinal);

            var messages = ParseMessages(writer.ToString());
            try
            {
                var itemAdded = messages.Last(static x => IsNotificationMethod(x.RootElement, "thread/realtime/itemAdded")).RootElement;
                var item = itemAdded.GetProperty("params").GetProperty("item");
                Assert.Equal("handoff_request", item.GetProperty("type").GetString());
                Assert.Equal("handoff_v2_1", item.GetProperty("handoff_id").GetString());
                Assert.Equal("item_v2_1", item.GetProperty("item_id").GetString());
                Assert.Equal("finish the task", item.GetProperty("input_transcript").GetString());
                var transcript = item.GetProperty("active_transcript");
                Assert.Equal(2, transcript.GetArrayLength());
                Assert.Equal("user", transcript[0].GetProperty("role").GetString());
                Assert.Equal("need ", transcript[0].GetProperty("text").GetString());
                Assert.Equal("assistant", transcript[1].GetProperty("role").GetString());
                Assert.Equal("working ", transcript[1].GetProperty("text").GetString());
            }
            finally
            {
                DisposeMessages(messages);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldUseRealtimeV2InputTextPayloadShape()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var cwd = Path.Combine(root, "workspace");
        const string threadId = "thread_realtime_ws_v2_append_text_001";
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        await using var realtimeServer = new RealtimeWebSocketTestServer(new
        {
            type = "session.updated",
            session = new
            {
                id = "sess_v2_append_text",
            },
        });

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(cwd);
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $"""
                experimental_realtime_ws_base_url = "{realtimeServer.Uri}"
                experimental_realtime_ws_startup_context = ""

                [features]
                realtime_conversation_v2 = true
                """);

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, cwd, CancellationToken.None);

            var input =
                """
                {"jsonrpc":"2.0","id":1,"method":"thread/realtime/start","params":{"threadId":"thread_realtime_ws_v2_append_text_001","prompt":"realtime prompt"}}
                {"jsonrpc":"2.0","id":2,"method":"thread/realtime/appendText","params":{"threadId":"thread_realtime_ws_v2_append_text_001","sessionId":"sess_v2_append_text","text":"hello realtime"}}
                """;
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var threadStore = new KernelThreadStore(storePath);
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var requests = await realtimeServer.WaitForRequestCountAsync(2);
            using var appendRequest = JsonDocument.Parse(requests[1]);
            Assert.Equal("conversation.item.create", appendRequest.RootElement.GetProperty("type").GetString());
            Assert.Equal("input_text", appendRequest.RootElement.GetProperty("item").GetProperty("content")[0].GetProperty("type").GetString());
            Assert.Equal("hello realtime", appendRequest.RootElement.GetProperty("item").GetProperty("content")[0].GetProperty("text").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldWriteRealtimeV1HandoffOutputToWebSocket()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var cwd = Path.Combine(root, "workspace");
        const string threadId = "thread_realtime_ws_v1_handoff_output_001";
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        await using var realtimeServer = new RealtimeWebSocketTestServer(new
        {
            type = "session.updated",
            session = new
            {
                id = "sess_v1_handoff_output",
                instructions = "realtime prompt",
            },
        });

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(cwd);
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $"""
                experimental_realtime_ws_base_url = "{realtimeServer.Uri}"
                experimental_realtime_ws_startup_context = ""
                """);

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, cwd, CancellationToken.None);

            var input =
                """
                {"jsonrpc":"2.0","id":1,"method":"thread/realtime/start","params":{"threadId":"thread_realtime_ws_v1_handoff_output_001","prompt":"realtime prompt"}}
                {"jsonrpc":"2.0","id":2,"method":"thread/realtime/handoffOutput","params":{"threadId":"thread_realtime_ws_v1_handoff_output_001","sessionId":"sess_v1_handoff_output","handoffId":"handoff_v1_1","output":"delegated result"}}
                """;
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var threadStore = new KernelThreadStore(storePath);
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var requests = await realtimeServer.WaitForRequestCountAsync(2);
            using var handoffRequest = JsonDocument.Parse(requests[1]);
            Assert.Equal("conversation.handoff.append", handoffRequest.RootElement.GetProperty("type").GetString());
            Assert.Equal("handoff_v1_1", handoffRequest.RootElement.GetProperty("handoff_id").GetString());
            Assert.Equal("delegated result", handoffRequest.RootElement.GetProperty("output_text").GetString());

            var messages = ParseMessages(writer.ToString());
            try
            {
                var response = messages.Single(static x => IsResponseId(x.RootElement, 2)).RootElement;
                Assert.True(response.TryGetProperty("result", out _));
            }
            finally
            {
                DisposeMessages(messages);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldWriteRealtimeV2HandoffOutputToWebSocket()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var cwd = Path.Combine(root, "workspace");
        const string threadId = "thread_realtime_ws_v2_handoff_output_001";
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        await using var realtimeServer = new RealtimeWebSocketTestServer(new
        {
            type = "session.updated",
            session = new
            {
                id = "sess_v2_handoff_output",
            },
        });

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(cwd);
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $"""
                experimental_realtime_ws_base_url = "{realtimeServer.Uri}"
                experimental_realtime_ws_startup_context = ""

                [features]
                realtime_conversation_v2 = true
                """);

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, cwd, CancellationToken.None);

            var input =
                """
                {"jsonrpc":"2.0","id":1,"method":"thread/realtime/start","params":{"threadId":"thread_realtime_ws_v2_handoff_output_001","prompt":"realtime prompt"}}
                {"jsonrpc":"2.0","id":2,"method":"thread/realtime/handoffOutput","params":{"threadId":"thread_realtime_ws_v2_handoff_output_001","sessionId":"sess_v2_handoff_output","handoffId":"handoff_v2_2","output":"delegated result"}}
                """;
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var threadStore = new KernelThreadStore(storePath);
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var requests = await realtimeServer.WaitForRequestCountAsync(2);
            using var handoffRequest = JsonDocument.Parse(requests[1]);
            Assert.Equal("conversation.item.create", handoffRequest.RootElement.GetProperty("type").GetString());
            var item = handoffRequest.RootElement.GetProperty("item");
            Assert.Equal("function_call_output", item.GetProperty("type").GetString());
            Assert.Equal("handoff_v2_2", item.GetProperty("call_id").GetString());
            Assert.Equal("delegated result", item.GetProperty("output").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldUseTranscriptionSessionModeWhenConfigured()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var cwd = Path.Combine(root, "workspace");
        const string threadId = "thread_realtime_ws_transcription_001";
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        await using var realtimeServer = new RealtimeWebSocketTestServer(new
        {
            type = "session.updated",
            session = new
            {
                id = "sess_transcription",
            },
        });

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(cwd);
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $"""
                experimental_realtime_ws_base_url = "{realtimeServer.Uri}"
                experimental_realtime_ws_mode = "transcription"

                [features]
                realtime_conversation_v2 = true
                """);

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, cwd, CancellationToken.None);

            var input = """{"jsonrpc":"2.0","id":1,"method":"thread/realtime/start","params":{"threadId":"thread_realtime_ws_transcription_001","prompt":"ignored prompt"}}""";
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var threadStore = new KernelThreadStore(storePath);
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            using var request = JsonDocument.Parse(await realtimeServer.WaitForFirstRequestAsync());
            var session = request.RootElement.GetProperty("session");
            Assert.Equal("transcription", session.GetProperty("type").GetString());
            Assert.False(session.TryGetProperty("instructions", out _));
            Assert.False(session.GetProperty("audio").TryGetProperty("output", out _));
            Assert.Equal("tianshu", session.GetProperty("tools")[0].GetProperty("name").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldUseConversationalSessionModeWhenConfigured()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var cwd = Path.Combine(root, "workspace");
        const string threadId = "thread_realtime_ws_conversational_001";
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        await using var realtimeServer = new RealtimeWebSocketTestServer(new
        {
            type = "session.updated",
            session = new
            {
                id = "sess_conversational",
                instructions = "prompt from config",
            },
        });

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(cwd);
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $"""
                experimental_realtime_ws_base_url = "{realtimeServer.Uri}"
                experimental_realtime_ws_backend_prompt = "prompt from config"
                experimental_realtime_ws_mode = "conversational"
                experimental_realtime_ws_startup_context = ""

                [features]
                realtime_conversation_v2 = true
                """);

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, cwd, CancellationToken.None);

            var input = """{"jsonrpc":"2.0","id":1,"method":"thread/realtime/start","params":{"threadId":"thread_realtime_ws_conversational_001","prompt":"ignored prompt"}}""";
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var threadStore = new KernelThreadStore(storePath);
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            using var request = JsonDocument.Parse(await realtimeServer.WaitForFirstRequestAsync());
            var session = request.RootElement.GetProperty("session");
            Assert.Equal("realtime", session.GetProperty("type").GetString());
            Assert.Equal("prompt from config", session.GetProperty("instructions").GetString());
            Assert.Equal("fathom", session.GetProperty("audio").GetProperty("output").GetProperty("voice").GetString());
            Assert.Equal("tianshu", session.GetProperty("tools")[0].GetProperty("name").GetString());
            Assert.DoesNotContain("intent=quicksilver", realtimeServer.FirstRequestPathAndQuery, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldFallbackToConversationalModeWhenTranscriptionConfiguredWithoutV2()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var cwd = Path.Combine(root, "workspace");
        const string threadId = "thread_realtime_ws_transcription_v1_fallback_001";
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        await using var realtimeServer = new RealtimeWebSocketTestServer(new
        {
            type = "session.updated",
            session = new
            {
                id = "sess_transcription_v1_fallback",
                instructions = "fallback prompt",
            },
        });

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(cwd);
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $"""
                experimental_realtime_ws_base_url = "{realtimeServer.Uri}"
                experimental_realtime_ws_mode = "transcription"
                experimental_realtime_ws_startup_context = ""
                """);

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, cwd, CancellationToken.None);

            var input = """{"jsonrpc":"2.0","id":1,"method":"thread/realtime/start","params":{"threadId":"thread_realtime_ws_transcription_v1_fallback_001","prompt":"fallback prompt"}}""";
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var threadStore = new KernelThreadStore(storePath);
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            using var request = JsonDocument.Parse(await realtimeServer.WaitForFirstRequestAsync());
            var session = request.RootElement.GetProperty("session");
            Assert.Equal("quicksilver", session.GetProperty("type").GetString());
            Assert.Equal("fallback prompt", session.GetProperty("instructions").GetString());
            Assert.Equal("fathom", session.GetProperty("audio").GetProperty("output").GetProperty("voice").GetString());
            Assert.Contains("intent=quicksilver", realtimeServer.FirstRequestPathAndQuery, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldSurfaceNestedRealtimeErrorMessageOnStartup()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var cwd = Path.Combine(root, "workspace");
        const string threadId = "thread_realtime_ws_nested_error_001";
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        await using var realtimeServer = new RealtimeWebSocketTestServer(new
        {
            type = "error",
            error = new
            {
                message = "nested boom",
            },
        });

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(cwd);
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $"""
                experimental_realtime_ws_base_url = "{realtimeServer.Uri}"
                experimental_realtime_ws_startup_context = ""
                """);

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, cwd, CancellationToken.None);

            var input = """{"jsonrpc":"2.0","id":1,"method":"thread/realtime/start","params":{"threadId":"thread_realtime_ws_nested_error_001","prompt":"realtime prompt"}}""";
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var threadStore = new KernelThreadStore(storePath);
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = ParseMessages(writer.ToString());
            try
            {
                var errorNotification = messages.Single(static x => IsNotificationMethod(x.RootElement, "thread/realtime/error")).RootElement;
                Assert.Equal("启动实时会话失败：nested boom", errorNotification.GetProperty("params").GetProperty("message").GetString());

                var errorResponse = messages.Single(static x => IsResponseId(x.RootElement, 1)).RootElement;
                Assert.Equal(-32603, errorResponse.GetProperty("error").GetProperty("code").GetInt32());
                Assert.Equal("启动实时会话失败：nested boom", errorResponse.GetProperty("error").GetProperty("message").GetString());
            }
            finally
            {
                DisposeMessages(messages);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }
}
