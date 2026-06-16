using System.Net;
using System.Text;
using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.Execution.Integration.Tests;

[Collection("EnvironmentVariables")]
public sealed class KernelW3cTraceContextTests
{
    [Fact]
    public async Task RunAsync_ShouldSendTraceParentHeader()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);

        const string threadId = "thread_trace_context_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var stream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-1" },
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
                    response = new { id = "resp-1" },
                }));

            var handler = new TraceCapturingSseHandler(stream);
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
                        new { text = "请输出OK" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            Assert.NotNull(handler.TraceParent);
            Assert.Matches("^00-[0-9a-f]{32}-[0-9a-f]{16}-[0-9a-f]{2}$", handler.TraceParent!);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    private sealed class TraceCapturingSseHandler : HttpMessageHandler
    {
        private readonly string stream;

        public TraceCapturingSseHandler(string stream)
        {
            this.stream = stream;
        }

        public string? TraceParent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.TryGetValues("traceparent", out var values))
            {
                TraceParent = values.FirstOrDefault();
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(stream, Encoding.UTF8, "text/event-stream"),
            };

            return Task.FromResult(response);
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
        var path = Path.Combine(Path.GetTempPath(), "tianshu-kernel-trace-tests", Guid.NewGuid().ToString("N"));
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
}
