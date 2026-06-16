using System.Net;
using System.Text;
using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.Execution.Integration.Tests;

[Collection("EnvironmentVariables")]
public sealed class AppHostServerAnthropicMessagesPromptTests
{
    [Fact]
    public async Task RunAsync_WhenProviderWireApiIsAnthropicMessages_ShouldUseMessagesEndpointAndStreamText()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);

        var apiKeyEnv = $"TIANSHU_ANTHROPIC_TEST_KEY_{Guid.NewGuid():N}";
        var originalApiKey = Environment.GetEnvironmentVariable(apiKeyEnv);
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable(apiKeyEnv, "test-anthropic-key");
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
                model = "openai-compatible-default"
                """);

            const string threadId = "thread_anthropic_messages_001";
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var handler = new CapturingAnthropicMessagesHandler();
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
                    model = "claude-sonnet-4-5",
                    providerWireApi = "anthropic_messages",
                    providerBaseUrl = "https://api.anthropic.test/v1",
                    providerApiKeyEnvironmentVariable = apiKeyEnv,
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
            await WaitForWriterContainsAsync(writer, "OK from anthropic", TimeSpan.FromSeconds(5));

            var output = writer.ToString();
            Assert.Contains("OK from anthropic", output, StringComparison.Ordinal);
            var request = Assert.Single(handler.Requests);
            Assert.EndsWith("/messages", request.Endpoint, StringComparison.Ordinal);
            Assert.Equal("test-anthropic-key", request.Headers["x-api-key"]);
            Assert.Equal("2023-06-01", request.Headers["anthropic-version"]);
            Assert.Contains("\"model\":\"claude-sonnet-4-5\"", request.Body, StringComparison.Ordinal);
            Assert.Contains("\"messages\"", request.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("\"input\"", request.Body, StringComparison.Ordinal);

            using var requestJson = JsonDocument.Parse(request.Body);
            Assert.True(requestJson.RootElement.GetProperty("max_tokens").GetInt32() >= 4096);
        }
        finally
        {
            Environment.SetEnvironmentVariable(apiKeyEnv, originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenAnthropicMessagesReturnsReasoningOnly_ShouldRepairOnceAndContinue()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);

        var apiKeyEnv = $"TIANSHU_ANTHROPIC_TEST_KEY_{Guid.NewGuid():N}";
        var originalApiKey = Environment.GetEnvironmentVariable(apiKeyEnv);
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable(apiKeyEnv, "test-anthropic-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", Path.Combine(root, "tianshu-home"));

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "tianshu-home"));
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            await File.WriteAllTextAsync(Path.Combine(root, "tianshu-home", "AGENTS.md"), "home agents");
            await File.WriteAllTextAsync(Path.Combine(repoRoot, "AGENTS.md"), "workspace agents");

            const string threadId = "thread_anthropic_messages_empty_repair_001";
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var handler = new CapturingSequencedAnthropicMessagesHandler(
                BuildAnthropicSseStream(
                    JsonSerializer.Serialize(new
                    {
                        type = "message_start",
                        message = new { id = "msg_reasoning_only_1", type = "message", role = "assistant", content = Array.Empty<object>(), model = "openai-compatible-default" },
                    }),
                    JsonSerializer.Serialize(new { type = "content_block_start", index = 0, content_block = new { type = "thinking", thinking = string.Empty } }),
                    JsonSerializer.Serialize(new { type = "content_block_delta", index = 0, delta = new { type = "thinking_delta", thinking = "I should continue but forgot visible text." } }),
                    JsonSerializer.Serialize(new { type = "content_block_stop", index = 0 }),
                    JsonSerializer.Serialize(new { type = "message_delta", delta = new { stop_reason = "end_turn", stop_sequence = (string?)null }, usage = new { output_tokens = 8 } }),
                    JsonSerializer.Serialize(new { type = "message_stop" })),
                BuildAnthropicSseStream(
                    JsonSerializer.Serialize(new
                    {
                        type = "message_start",
                        message = new { id = "msg_repaired_2", type = "message", role = "assistant", content = Array.Empty<object>(), model = "openai-compatible-default" },
                    }),
                    JsonSerializer.Serialize(new { type = "content_block_start", index = 0, content_block = new { type = "text", text = string.Empty } }),
                    JsonSerializer.Serialize(new { type = "content_block_delta", index = 0, delta = new { type = "text_delta", text = "已继续输出可见结果" } }),
                    JsonSerializer.Serialize(new { type = "content_block_stop", index = 0 }),
                    JsonSerializer.Serialize(new { type = "message_stop" })));
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
                    model = "openai-compatible-default",
                    providerWireApi = "anthropic_messages",
                    providerBaseUrl = "https://api.anthropic.test/v1",
                    providerApiKeyEnvironmentVariable = apiKeyEnv,
                    input = new[]
                    {
                        new { text = "执行一个需要多步工具循环的任务" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            var encodedAssistantText = JsonSerializer.Serialize("已继续输出可见结果").Trim('"');
            await WaitForWriterContainsAsync(writer, encodedAssistantText, TimeSpan.FromSeconds(5));

            Assert.Equal(2, handler.Requests.Count);
            Assert.Contains(
                ReadAnthropicMessageTexts(handler.Requests[1].Body),
                static text => text.Contains("没有提供可展示的 assistant 文本", StringComparison.Ordinal));
            Assert.Contains(encodedAssistantText, writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(apiKeyEnv, originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenNonClaudeAnthropicMessagesToolUseHasThinking_ShouldFlattenToolResultFollowUp()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);
        var notePath = Path.Combine(repoRoot, "note.txt");
        await File.WriteAllTextAsync(notePath, "hello-anthropic-tool");

        var apiKeyEnv = $"TIANSHU_ANTHROPIC_TEST_KEY_{Guid.NewGuid():N}";
        var originalApiKey = Environment.GetEnvironmentVariable(apiKeyEnv);
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable(apiKeyEnv, "test-anthropic-key");
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
                model = "claude-sonnet-4-5"
                """);

            const string threadId = "thread_anthropic_messages_tool_001";
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var notePathJson = notePath.Replace('\\', '/');
            var functionCallArgs = JsonSerializer.Serialize(new
            {
                file_path = notePathJson,
            });
            var handler = new CapturingSequencedAnthropicMessagesHandler(
                BuildAnthropicSseStream(
                    JsonSerializer.Serialize(new
                    {
                        type = "message_start",
                        message = new { id = "msg_tool_1", type = "message", role = "assistant", content = Array.Empty<object>(), model = "openai-compatible-default" },
                    }),
                    JsonSerializer.Serialize(new
                    {
                        type = "content_block_start",
                        index = 0,
                        content_block = new { type = "thinking", thinking = string.Empty },
                    }),
                    JsonSerializer.Serialize(new
                    {
                        type = "content_block_delta",
                        index = 0,
                        delta = new { type = "thinking_delta", thinking = "I need to inspect the file before answering." },
                    }),
                    JsonSerializer.Serialize(new
                    {
                        type = "content_block_delta",
                        index = 0,
                        delta = new { type = "signature_delta", signature = "thinking-signature" },
                    }),
                    JsonSerializer.Serialize(new { type = "content_block_stop", index = 0 }),
                    JsonSerializer.Serialize(new
                    {
                        type = "content_block_start",
                        index = 1,
                        content_block = new { type = "tool_use", id = "toolu_01", name = "read_file", input = new { } },
                    }),
                    JsonSerializer.Serialize(new
                    {
                        type = "content_block_delta",
                        index = 1,
                        delta = new { type = "input_json_delta", partial_json = functionCallArgs },
                    }),
                    JsonSerializer.Serialize(new { type = "content_block_stop", index = 1 }),
                    JsonSerializer.Serialize(new { type = "message_stop" })),
                BuildAnthropicSseStream(
                    JsonSerializer.Serialize(new
                    {
                        type = "message_start",
                        message = new { id = "msg_tool_2", type = "message", role = "assistant", content = Array.Empty<object>(), model = "openai-compatible-default" },
                    }),
                    JsonSerializer.Serialize(new { type = "content_block_start", index = 0, content_block = new { type = "text", text = string.Empty } }),
                    JsonSerializer.Serialize(new { type = "content_block_delta", index = 0, delta = new { type = "text_delta", text = "OK after tool" } }),
                    JsonSerializer.Serialize(new { type = "content_block_stop", index = 0 }),
                    JsonSerializer.Serialize(new { type = "message_stop" })));
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
                    model = "openai-compatible-default",
                    providerWireApi = "anthropic_messages",
                    providerBaseUrl = "https://api.anthropic.test/v1",
                    providerApiKeyEnvironmentVariable = apiKeyEnv,
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
            await WaitForWriterContainsAsync(writer, "OK after tool", TimeSpan.FromSeconds(5));

            Assert.Equal(2, handler.Requests.Count);
            Assert.Contains("\"toolName\":\"read_file\"", writer.ToString(), StringComparison.Ordinal);

            using var firstRequestJson = JsonDocument.Parse(handler.Requests[0].Body);
            var tools = firstRequestJson.RootElement.GetProperty("tools").EnumerateArray().ToArray();
            Assert.Contains(tools, static tool => tool.TryGetProperty("name", out _)
                && tool.TryGetProperty("input_schema", out _)
                && !tool.TryGetProperty("type", out _));

            using var secondRequestJson = JsonDocument.Parse(handler.Requests[1].Body);
            var messages = secondRequestJson.RootElement.GetProperty("messages").EnumerateArray().ToArray();
            Assert.DoesNotContain(messages, static message => message.GetProperty("role").GetString() == "assistant");
            Assert.DoesNotContain(handler.Requests[1].Body, "\"tool_result\"", StringComparison.Ordinal);
            Assert.DoesNotContain(handler.Requests[1].Body, "\"tool_use\"", StringComparison.Ordinal);
            Assert.Contains(messages, static message => message.GetProperty("role").GetString() == "user"
                && message.GetProperty("content").EnumerateArray().Any(static block =>
                    block.GetProperty("type").GetString() == "text"
                    && block.GetProperty("text").GetString()!.Contains("工具执行结果如下", StringComparison.Ordinal)
                    && block.GetProperty("text").GetString()!.Contains("read_file", StringComparison.Ordinal)
                    && block.GetProperty("text").GetString()!.Contains("hello-anthropic-tool", StringComparison.Ordinal)));
        }
        finally
        {
            Environment.SetEnvironmentVariable(apiKeyEnv, originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenAnthropicMessagesGatewayReturnsChatStyleReasoningToolCall_ShouldNormalizeToolArgsAndReplayReasoning()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);

        var apiKeyEnv = $"TIANSHU_ANTHROPIC_COMPAT_TEST_KEY_{Guid.NewGuid():N}";
        var originalApiKey = Environment.GetEnvironmentVariable(apiKeyEnv);
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable(apiKeyEnv, "test-anthropic-key");
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
                model = "openai-compatible-default"
                """);

            const string threadId = "thread_anthropic_messages_chat_style_tool_001";
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var functionCallArgs = Enumerable.Range(0, 3)
                .Select(static index => JsonSerializer.Serialize(new
                {
                    command = JsonSerializer.Serialize(new[] { "powershell.exe", "-NoProfile", "-Command", $"Write-Output tianshu-anthropic-compat-{index}" }),
                }))
                .ToArray();
            var handler = new CapturingSequencedAnthropicMessagesHandler(
                BuildAnthropicSseStream(
                    JsonSerializer.Serialize(new
                    {
                        id = "chatcmpl-test",
                        choices = new[]
                        {
                            new
                            {
                                delta = new { reasoning_content = "需要先确认当前目录。" },
                                finish_reason = (string?)null,
                            },
                        },
                    }),
                    JsonSerializer.Serialize(new
                    {
                        id = "chatcmpl-test",
                        choices = new[]
                        {
                            new
                            {
                                delta = new
                                {
                                    tool_calls = new[]
                                    {
                                        new
                                        {
                                            index = 0,
                                            id = "call_001",
                                            type = "function",
                                            function = new
                                            {
                                                name = "shell",
                                                arguments = functionCallArgs[0],
                                            },
                                        },
                                        new
                                        {
                                            index = 1,
                                            id = "call_002",
                                            type = "function",
                                            function = new
                                            {
                                                name = "shell",
                                                arguments = functionCallArgs[1],
                                            },
                                        },
                                        new
                                        {
                                            index = 2,
                                            id = "call_003",
                                            type = "function",
                                            function = new
                                            {
                                                name = "shell",
                                                arguments = functionCallArgs[2],
                                            },
                                        },
                                    },
                                },
                                finish_reason = (string?)null,
                            },
                        },
                    }),
                    JsonSerializer.Serialize(new
                    {
                        id = "chatcmpl-test",
                        choices = new[]
                        {
                            new
                            {
                                delta = new { },
                                finish_reason = "tool_calls",
                            },
                        },
                    }),
                    "[DONE]"),
                BuildAnthropicSseStream(
                    JsonSerializer.Serialize(new
                    {
                        type = "message_start",
                        message = new { id = "msg_tool_2", type = "message", role = "assistant", content = Array.Empty<object>(), model = "openai-compatible-default" },
                    }),
                    JsonSerializer.Serialize(new { type = "content_block_start", index = 0, content_block = new { type = "text", text = string.Empty } }),
                    JsonSerializer.Serialize(new { type = "content_block_delta", index = 0, delta = new { type = "text_delta", text = "OK after compat tool" } }),
                    JsonSerializer.Serialize(new { type = "content_block_stop", index = 0 }),
                    JsonSerializer.Serialize(new { type = "message_stop" })));
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
                    model = "openai-compatible-default",
                    providerWireApi = "anthropic_messages",
                    providerBaseUrl = "https://api.anthropic.test/v1",
                    providerApiKeyEnvironmentVariable = apiKeyEnv,
                    input = new[]
                    {
                        new { text = "请确认当前目录" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "OK after compat tool", TimeSpan.FromSeconds(5));

            var output = writer.ToString();
            Assert.DoesNotContain("工具参数无效", output, StringComparison.Ordinal);
            Assert.Equal(2, handler.Requests.Count);

            using var secondRequestJson = JsonDocument.Parse(handler.Requests[1].Body);
            var messages = secondRequestJson.RootElement.GetProperty("messages").EnumerateArray().ToArray();
            Assert.DoesNotContain(messages, static message => message.GetProperty("role").GetString() == "assistant");
            Assert.DoesNotContain(handler.Requests[1].Body, "\"tool_result\"", StringComparison.Ordinal);
            Assert.DoesNotContain(handler.Requests[1].Body, "\"tool_use\"", StringComparison.Ordinal);
            var flattenedToolResultMessages = messages.Where(static message => message.GetProperty("role").GetString() == "user"
                && message.GetProperty("content").EnumerateArray().Any(static block =>
                    block.GetProperty("type").GetString() == "text"
                    && block.GetProperty("text").GetString()!.Contains("工具执行结果如下", StringComparison.Ordinal))).ToArray();
            var user = Assert.Single(flattenedToolResultMessages);
            var flattenedText = Assert.Single(user.GetProperty("content").EnumerateArray()).GetProperty("text").GetString();
            for (var index = 0; index < 3; index++)
            {
                var expectedOutput = $"tianshu-anthropic-compat-{index}";
                Assert.Contains("shell", flattenedText, StringComparison.Ordinal);
                Assert.Contains(expectedOutput, flattenedText, StringComparison.Ordinal);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(apiKeyEnv, originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    private sealed class CapturingAnthropicMessagesHandler : HttpMessageHandler
    {
        public List<(string Endpoint, Dictionary<string, string> Headers, string Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((
                request.RequestUri?.ToString() ?? string.Empty,
                request.Headers.ToDictionary(static header => header.Key, static header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase),
                body));

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    data: {"type":"message_start","message":{"id":"msg_01","type":"message","role":"assistant","content":[],"model":"claude-sonnet-4-5","stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":1,"output_tokens":1}}}

                    data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

                    data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"OK from anthropic"}}

                    data: {"type":"content_block_stop","index":0}

                    data: {"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":5}}

                    data: {"type":"message_stop"}

                    """,
                    Encoding.UTF8,
                    "text/event-stream"),
            };

            return response;
        }
    }

    private sealed class CapturingSequencedAnthropicMessagesHandler(params string[] streams) : HttpMessageHandler
    {
        private int index;

        public List<(string Endpoint, string Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri?.ToString() ?? string.Empty, body));

            var responseIndex = Math.Min(index, streams.Length - 1);
            index++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    streams[responseIndex],
                    Encoding.UTF8,
                    "text/event-stream"),
            };
        }
    }

    private static string BuildAnthropicSseStream(params string[] events)
    {
        var builder = new StringBuilder();
        foreach (var @event in events)
        {
            builder.Append("data: ");
            builder.AppendLine(@event);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static IReadOnlyList<string> ReadAnthropicMessageTexts(string requestBody)
    {
        using var document = JsonDocument.Parse(requestBody);
        var texts = new List<string>();
        foreach (var message in document.RootElement.GetProperty("messages").EnumerateArray())
        {
            if (!message.TryGetProperty("content", out var content))
            {
                continue;
            }

            if (content.ValueKind == JsonValueKind.String)
            {
                texts.Add(content.GetString() ?? string.Empty);
                continue;
            }

            if (content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                if (contentItem.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    texts.Add(text.GetString() ?? string.Empty);
                }
            }
        }

        return texts;
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
        var path = Path.Combine(Path.GetTempPath(), "tianshu-kernel-anthropic-prompt-tests", Guid.NewGuid().ToString("N"));
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
