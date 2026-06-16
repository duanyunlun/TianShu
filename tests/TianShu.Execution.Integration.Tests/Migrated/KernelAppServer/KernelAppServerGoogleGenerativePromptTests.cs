using System.Net;
using System.Text;
using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.Execution.Integration.Tests;

[Collection("EnvironmentVariables")]
public sealed class AppHostServerGoogleGenerativePromptTests
{
    [Fact]
    public async Task RunAsync_WhenProviderWireApiIsGoogleGenerative_ShouldUseStreamGenerateContentEndpointAndStreamText()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);

        var apiKeyEnv = $"TIANSHU_GOOGLE_TEST_KEY_{Guid.NewGuid():N}";
        var originalApiKey = Environment.GetEnvironmentVariable(apiKeyEnv);
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable(apiKeyEnv, "test-google-key");
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
                model = "gemini-2.5-pro"
                """);

            const string threadId = "thread_google_generative_001";
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var handler = new CapturingGoogleGenerativeHandler();
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
                    model = "gemini-2.5-pro",
                    providerWireApi = "google_generative",
                    providerBaseUrl = "https://generativelanguage.googleapis.test/v1beta",
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
            await WaitForWriterContainsAsync(writer, "OK from google", TimeSpan.FromSeconds(5));

            var output = writer.ToString();
            Assert.Contains("OK from google", output, StringComparison.Ordinal);
            var request = Assert.Single(handler.Requests);
            Assert.EndsWith("/v1beta/models/gemini-2.5-pro:streamGenerateContent?key=test-google-key&alt=sse", request.Endpoint, StringComparison.Ordinal);
            Assert.Equal("text/event-stream", request.Headers["Accept"]);
            Assert.DoesNotContain("Authorization", request.Headers.Keys);
            Assert.DoesNotContain("\"model\"", request.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("\"input\"", request.Body, StringComparison.Ordinal);
            Assert.Contains("\"contents\"", request.Body, StringComparison.Ordinal);
            Assert.Contains("\"parts\"", request.Body, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(apiKeyEnv, originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenGoogleFunctionCallArrives_ShouldExecuteToolAndSendFunctionResponsePart()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);
        var notePath = Path.Combine(repoRoot, "note.txt");
        await File.WriteAllTextAsync(notePath, "hello-google-tool");

        var apiKeyEnv = $"TIANSHU_GOOGLE_TEST_KEY_{Guid.NewGuid():N}";
        var originalApiKey = Environment.GetEnvironmentVariable(apiKeyEnv);
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable(apiKeyEnv, "test-google-key");
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
                model = "gemini-2.5-pro"
                """);

            const string threadId = "thread_google_generative_tool_001";
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var notePathJson = notePath.Replace('\\', '/');
            var handler = new CapturingSequencedGoogleGenerativeHandler(
                BuildGoogleSseStream(JsonSerializer.Serialize(new
                {
                    candidates = new[]
                    {
                        new
                        {
                            content = new
                            {
                                role = "model",
                                parts = new object[]
                                {
                                    new
                                    {
                                        functionCall = new
                                        {
                                            name = "read_file",
                                            args = new { file_path = notePathJson },
                                        },
                                    },
                                },
                            },
                            finishReason = "STOP",
                        },
                    },
                    responseId = "google_tool_1",
                })),
                BuildGoogleSseStream(JsonSerializer.Serialize(new
                {
                    candidates = new[]
                    {
                        new
                        {
                            content = new
                            {
                                role = "model",
                                parts = new[] { new { text = "OK after google tool" } },
                            },
                            finishReason = "STOP",
                        },
                    },
                    responseId = "google_tool_2",
                })));
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
                    model = "gemini-2.5-pro",
                    providerWireApi = "google_generative",
                    providerBaseUrl = "https://generativelanguage.googleapis.test/v1beta",
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
            await WaitForWriterContainsAsync(writer, "OK after google tool", TimeSpan.FromSeconds(5));

            Assert.Equal(2, handler.Requests.Count);
            Assert.Contains("\"toolName\":\"read_file\"", writer.ToString(), StringComparison.Ordinal);

            using var firstRequestJson = JsonDocument.Parse(handler.Requests[0].Body);
            var tools = firstRequestJson.RootElement.GetProperty("tools").EnumerateArray().ToArray();
            var declarations = Assert.Single(tools).GetProperty("functionDeclarations").EnumerateArray().ToArray();
            Assert.Contains(declarations, static item => item.TryGetProperty("name", out _)
                && item.TryGetProperty("parameters", out _)
                && !item.TryGetProperty("type", out _));

            using var secondRequestJson = JsonDocument.Parse(handler.Requests[1].Body);
            var contents = secondRequestJson.RootElement.GetProperty("contents").EnumerateArray().ToArray();
            Assert.Contains(contents, static content => content.GetProperty("role").GetString() == "model"
                && content.GetProperty("parts").EnumerateArray().Any(static part =>
                    part.TryGetProperty("functionCall", out var functionCall)
                    && functionCall.GetProperty("name").GetString() == "read_file"));
            Assert.Contains(contents, static content => content.GetProperty("role").GetString() == "user"
                && content.GetProperty("parts").EnumerateArray().Any(static part =>
                    part.TryGetProperty("functionResponse", out var functionResponse)
                    && functionResponse.GetProperty("name").GetString() == "read_file"
                    && functionResponse.GetProperty("response").GetProperty("output").GetString()!.Contains("hello-google-tool", StringComparison.Ordinal)));
        }
        finally
        {
            Environment.SetEnvironmentVariable(apiKeyEnv, originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    private sealed class CapturingGoogleGenerativeHandler : HttpMessageHandler
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

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    BuildGoogleSseStream(JsonSerializer.Serialize(new
                    {
                        candidates = new[]
                        {
                            new
                            {
                                content = new
                                {
                                    role = "model",
                                    parts = new[] { new { text = "OK from google" } },
                                },
                                finishReason = "STOP",
                            },
                        },
                        usageMetadata = new { promptTokenCount = 1, candidatesTokenCount = 2, totalTokenCount = 3 },
                        responseId = "google_text_1",
                    })),
                    Encoding.UTF8,
                    "text/event-stream"),
            };
        }
    }

    private sealed class CapturingSequencedGoogleGenerativeHandler(params string[] streams) : HttpMessageHandler
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

    private static string BuildGoogleSseStream(params string[] chunks)
    {
        var builder = new StringBuilder();
        foreach (var chunk in chunks)
        {
            builder.Append("data: ");
            builder.AppendLine(chunk);
            builder.AppendLine();
        }

        return builder.ToString();
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
        var path = Path.Combine(Path.GetTempPath(), "tianshu-kernel-google-prompt-tests", Guid.NewGuid().ToString("N"));
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
