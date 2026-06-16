using System.Net;
using System.Text;
using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.Execution.Integration.Tests;

[Collection("EnvironmentVariables")]
public sealed class AppHostServerCustomToolLoopTests
{
    [Fact]
    public async Task RunAsync_ShouldRoundTripCustomToolCallOutput()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);

        const string threadId = "thread_custom_tool_loop_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

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
                        type = "custom_tool_call",
                        name = "js_repl",
                        input = "console.log(41 + 1);",
                        call_id = "custom-call-1",
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
                        new { text = "执行 js_repl" },
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
                && tool.GetProperty("name").GetString() == "js_repl");

            using var secondRequest = JsonDocument.Parse(handler.RequestBodies[1]);
            var customOutput = secondRequest.RootElement
                .GetProperty("input")
                .EnumerateArray()
                .First(static item =>
                    item.GetProperty("type").GetString() == "custom_tool_call_output"
                    && item.GetProperty("call_id").GetString() == "custom-call-1");
            var output = customOutput.GetProperty("output").GetString();
            Assert.Contains("artifactRef: custom-call-1", output, StringComparison.Ordinal);
            Assert.Contains("stdout:", output, StringComparison.Ordinal);
            Assert.Contains("42", output, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRoundTripApplyPatchCustomToolCallOutput()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);

        const string threadId = "thread_custom_apply_patch_loop_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var patch =
                """
                *** Begin Patch
                *** Add File: hello.txt
                +Hello from freeform
                *** End Patch
                """;

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
                        type = "custom_tool_call",
                        name = "apply_patch",
                        input = patch,
                        call_id = "apply-patch-call-1",
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
                    model = "gpt-5.4",
                    input = new[]
                    {
                        new { text = "执行 apply_patch" },
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
                && tool.GetProperty("name").GetString() == "apply_patch");

            using var secondRequest = JsonDocument.Parse(handler.RequestBodies[1]);
            var customOutput = secondRequest.RootElement
                .GetProperty("input")
                .EnumerateArray()
                .First(static item =>
                    item.GetProperty("type").GetString() == "custom_tool_call_output"
                    && item.GetProperty("call_id").GetString() == "apply-patch-call-1");
            Assert.Contains("A hello.txt", customOutput.GetProperty("output").GetString(), StringComparison.Ordinal);
            Assert.Equal("Hello from freeform\n", await File.ReadAllTextAsync(Path.Combine(repoRoot, "hello.txt")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
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
        var path = Path.Combine(Path.GetTempPath(), "tianshu-kernel-custom-tool-tests", Guid.NewGuid().ToString("N"));
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
