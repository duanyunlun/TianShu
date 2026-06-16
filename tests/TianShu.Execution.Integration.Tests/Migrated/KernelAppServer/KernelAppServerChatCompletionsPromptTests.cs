using System.Net;
using System.Text;
using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.Execution.Integration.Tests;

[Collection("EnvironmentVariables")]
public sealed class AppHostServerChatCompletionsPromptTests
{
    [Theory]
    [InlineData("openai-compatible-default")]
    [InlineData("gpt-5.4")]
    [InlineData("claude-sonnet-4-5")]
    [InlineData("gemini-2.5-pro")]
    [InlineData("qwen3-coder-plus")]
    [InlineData("kimi-k2-0905")]
    [InlineData("glm-4.6")]
    public async Task RunAsync_WhenProviderWireApiIsChatCompletions_ShouldUseChatCompletionsEndpoint(string model)
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);

        var threadId = $"thread_chat_completions_{SanitizeModelForThreadId(model)}";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", Path.Combine(root, "tianshu-home"));

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "tianshu-home"));
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            await File.WriteAllTextAsync(Path.Combine(root, "tianshu-home", "AGENTS.md"), "home agents");
            await File.WriteAllTextAsync(Path.Combine(repoRoot, "AGENTS.md"), "workspace agents");
            await File.WriteAllTextAsync(
                Path.Combine(root, "tianshu-home", "tianshu.toml"),
                """
                model = "gpt-5.4"
                """);

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var handler = new CapturingChatCompletionsHandler();
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
                    model,
                    providerWireApi = "chat_completions",
                    input = new[]
                    {
                        new { text = "简单说一下这个配置是什么作用" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/failed\"", TimeSpan.FromSeconds(5));

            var output = writer.ToString();
            Assert.Contains("OK from chat", output, StringComparison.Ordinal);
            var request = Assert.Single(handler.Requests);
            Assert.EndsWith("/chat/completions", request.Endpoint, StringComparison.Ordinal);
            Assert.Contains($"\"model\":\"{model}\"", request.Body, StringComparison.Ordinal);
            Assert.Contains("\"messages\"", request.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("\"input\"", request.Body, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenChatCompletionsReturnsReasoningContent_ShouldReplayItOnNextTurn()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);

        const string threadId = "thread_chat_reasoning_replay";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", Path.Combine(root, "tianshu-home"));

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "tianshu-home"));
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            await File.WriteAllTextAsync(Path.Combine(root, "tianshu-home", "tianshu.toml"), "model = \"openai-compatible-default\"");

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var handler = new CapturingChatCompletionsHandler(returnReasoningOnFirstTurn: true);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            await RunTurnAsync(storePath, httpClient, threadId, "第一轮问题", requestId: 1);
            await RunTurnAsync(storePath, httpClient, threadId, "第二轮问题", requestId: 2);

            Assert.Equal(2, handler.Requests.Count);
            Assert.DoesNotContain("\"reasoning_content\"", handler.Requests[0].Body, StringComparison.Ordinal);
            Assert.Contains("\"reasoning_content\":\"deepseek thinking artifact\"", handler.Requests[1].Body, StringComparison.Ordinal);
            Assert.Contains("\"content\":\"first answer\"", handler.Requests[1].Body, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenChatCompletionsReturnsReasoningToolCall_ShouldReplayReasoningToolCallAndToolResult()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);

        const string threadId = "thread_chat_tool_reasoning_replay";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", Path.Combine(root, "tianshu-home"));

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "tianshu-home"));
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            await File.WriteAllTextAsync(Path.Combine(root, "tianshu-home", "tianshu.toml"), "model = \"openai-compatible-default\"");

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var handler = new CapturingChatCompletionsHandler(returnToolCallOnFirstRequest: true);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            await RunTurnAsync(storePath, httpClient, threadId, "当前目录是？", requestId: 1);

            Assert.Equal(2, handler.Requests.Count);
            Assert.Contains("\"tools\"", handler.Requests[0].Body, StringComparison.Ordinal);
            using var followUpJson = JsonDocument.Parse(handler.Requests[1].Body);
            var messages = followUpJson.RootElement.GetProperty("messages").EnumerateArray().ToArray();
            var assistant = Assert.Single(messages, static message => message.GetProperty("role").GetString() == "assistant");
            var tool = Assert.Single(messages, static message => message.GetProperty("role").GetString() == "tool");
            Assert.Equal("需要先调用工具确认目录。", assistant.GetProperty("reasoning_content").GetString());
            Assert.Equal("call_001", assistant.GetProperty("tool_calls")[0].GetProperty("id").GetString());
            Assert.Equal("call_001", tool.GetProperty("tool_call_id").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    private sealed class CapturingChatCompletionsHandler : HttpMessageHandler
    {
        private readonly bool returnReasoningOnFirstTurn;
        private readonly bool returnToolCallOnFirstRequest;

        public CapturingChatCompletionsHandler(
            bool returnReasoningOnFirstTurn = false,
            bool returnToolCallOnFirstRequest = false)
        {
            this.returnReasoningOnFirstTurn = returnReasoningOnFirstTurn;
            this.returnToolCallOnFirstRequest = returnToolCallOnFirstRequest;
        }

        public List<(string Endpoint, string Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri?.ToString() ?? string.Empty, body));

            var responseBody = returnToolCallOnFirstRequest
                ? BuildReasoningToolCallResponseBody(Requests.Count)
                : returnReasoningOnFirstTurn
                ? BuildReasoningReplayResponseBody(Requests.Count)
                : """
                  data: {"id":"chatcmpl-test","choices":[{"delta":{"content":"OK from chat"},"finish_reason":null}]}

                  data: {"id":"chatcmpl-test","choices":[{"delta":{},"finish_reason":"stop"}]}

                  data: [DONE]

                  """;

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "text/event-stream"),
            };

            return response;
        }

        private static string BuildReasoningReplayResponseBody(int requestCount)
            => requestCount == 1
                ? """
                  data: {"id":"chatcmpl-test","choices":[{"delta":{"reasoning_content":"deepseek thinking artifact"},"finish_reason":null}]}

                  data: {"id":"chatcmpl-test","choices":[{"delta":{"content":"first answer"},"finish_reason":null}]}

                  data: {"id":"chatcmpl-test","choices":[{"delta":{},"finish_reason":"stop"}]}

                  data: [DONE]

                  """
                : """
                  data: {"id":"chatcmpl-test","choices":[{"delta":{"content":"second answer"},"finish_reason":null}]}

                  data: {"id":"chatcmpl-test","choices":[{"delta":{},"finish_reason":"stop"}]}

                  data: [DONE]

                  """;

        private static string BuildReasoningToolCallResponseBody(int requestCount)
            => requestCount == 1
                ? """
                  data: {"id":"chatcmpl-test","choices":[{"delta":{"reasoning_content":"需要先调用工具确认目录。"},"finish_reason":null}]}

                  data: {"id":"chatcmpl-test","choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_001","type":"function","function":{"name":"shell_command","arguments":"{\"command\":\"pwd\"}"}}]},"finish_reason":null}]}

                  data: {"id":"chatcmpl-test","choices":[{"delta":{},"finish_reason":"tool_calls"}]}

                  data: [DONE]

                  """
                : """
                  data: {"id":"chatcmpl-test","choices":[{"delta":{"content":"目录已确认"},"finish_reason":null}]}

                  data: {"id":"chatcmpl-test","choices":[{"delta":{},"finish_reason":"stop"}]}

                  data: [DONE]

                  """;
    }

    private static async Task RunTurnAsync(
        string storePath,
        HttpClient httpClient,
        string threadId,
        string text,
        int requestId)
    {
        var inputJson = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = requestId,
            method = "turn/start",
            @params = new
            {
                threadId,
                model = "openai-compatible-default",
                providerWireApi = "chat_completions",
                input = new[]
                {
                    new { text },
                },
            },
        });

        var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
        var writer = new StringWriter();
        var server = new AppHostServer(reader, writer, new KernelThreadStore(storePath), httpClient: httpClient);

        await server.RunAsync(CancellationToken.None);
        await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));
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
        var path = Path.Combine(Path.GetTempPath(), "tianshu-kernel-chat-prompt-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string SanitizeModelForThreadId(string model)
        => new(model.Select(static ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
