using System.Net;
using System.Text;
using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.Execution.Integration.Tests;

[Collection("EnvironmentVariables")]
public sealed class AppHostServerArtifactsToolLoopTests
{
    [Fact]
    public async Task RunAsync_ShouldRoundTripArtifactsCustomToolOutput()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        var tianShuHome = Path.Combine(root, ".tianshu");
        Directory.CreateDirectory(repoRoot);
        Directory.CreateDirectory(tianShuHome);
        await File.WriteAllTextAsync(Path.Combine(tianShuHome, "tianshu.toml"), "[features]\nartifact = true\n");
        KernelArtifactsRuntimeTestHelper.CreateFakeArtifactRuntime(tianShuHome);

        const string threadId = "thread_artifacts_tool_loop_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var firstStream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-artifacts-1" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "custom_tool_call",
                        name = "artifacts",
                        input = "console.log(marker);",
                        call_id = "artifact-call-1",
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = "resp-artifacts-1" },
                }));

            var secondStream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-artifacts-2" },
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
                    response = new { id = "resp-artifacts-2" },
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
                    approvalPolicy = "never",
                    sandboxPolicy = new { type = "danger-full-access" },
                    input = new[]
                    {
                        new { text = "执行 artifacts" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            Assert.Equal(2, handler.RequestBodies.Count);

            using var firstRequest = JsonDocument.Parse(handler.RequestBodies[0]);
            var tools = firstRequest.RootElement.GetProperty("tools");
            Assert.Contains(tools.EnumerateArray(), static tool =>
                tool.GetProperty("type").GetString() == "custom"
                && tool.GetProperty("name").GetString() == "artifacts");

            using var secondRequest = JsonDocument.Parse(handler.RequestBodies[1]);
            var customOutput = secondRequest.RootElement
                .GetProperty("input")
                .EnumerateArray()
                .First(static item =>
                    item.GetProperty("type").GetString() == "custom_tool_call_output"
                    && item.GetProperty("call_id").GetString() == "artifact-call-1");
            Assert.Contains("artifact-runtime-ok", customOutput.GetProperty("output").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    private sealed class CapturingSequencedSseHandler : HttpMessageHandler
    {
        private readonly Queue<string> streams;

        public CapturingSequencedSseHandler(IEnumerable<string> streams)
        {
            this.streams = new Queue<string>(streams);
        }

        public List<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));

            var body = streams.Count > 0 ? streams.Dequeue() : string.Empty;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
            };
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
        var path = Path.Combine(Path.GetTempPath(), "tianshu-kernel-artifacts-tool-loop-tests", Guid.NewGuid().ToString("N"));
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

        Directory.Delete(path, recursive: true);
    }
}
