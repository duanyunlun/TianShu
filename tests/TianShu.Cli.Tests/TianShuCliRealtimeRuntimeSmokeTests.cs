using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace TianShu.Cli.Tests;

[Collection("EnvironmentVariables")]
public sealed class TianShuCliRealtimeRuntimeSmokeTests : IDisposable
{
    private static readonly Assembly CliAssembly = ReflectionTestHelper.LoadRequiredAssembly("TianShu.Cli");
    private readonly string? originalOpenAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

    [Fact]
    public async Task RunRealtimeAsync_Start_WithActualRuntime_UsesConfigDrivenTranscriptionMode()
    {
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var repoRoot = FindRepoRoot();
        var appHostProjectPath = Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost", "TianShu.AppHost.csproj");

        using var workspace = new TestWorkspace();
        await using var realtimeServer = new RealtimeWebSocketTestServer(new
        {
            type = "session.updated",
            session = new
            {
                id = "sess_cli_transcription_001",
            },
        });

        var configPath = workspace.WriteConfig(
            $"""
            model = "gpt-5-codex"
            provider = "openai"
            approval_policy = "never"
            experimental_realtime_ws_base_url = "{realtimeServer.Uri}"
            experimental_realtime_ws_mode = "transcription"

            [features]
            realtime_conversation_v2 = true

            [providers.openai]
            base_url = "https://api.openai.com/v1"
            api_key_env = "OPENAI_API_KEY"
            default_protocol = "responses"
            """);

        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");

        var runner = Activator.CreateInstance(runnerType, nonPublic: true);
        Assert.NotNull(runner);

        var threadCommand = ParseCommand(
            parserType,
            [
                "thread",
                "start",
                "--cwd",
                workspace.RootPath,
                "--apphost-project",
                appHostProjectPath,
                "--config",
                configPath,
                "--json",
            ]);

        var (threadExitCode, threadOutput) = await InvokeRunnerAndCaptureOutputAsync(runner!, "RunThreadAsync", threadCommand);
        Assert.Equal(0, threadExitCode);

        using var threadJson = JsonDocument.Parse(threadOutput);
        var threadId = threadJson.RootElement.GetProperty("threadId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(threadId));

        var realtimeCommand = ParseCommand(
            parserType,
            [
                "realtime",
                "start",
                "--cwd",
                workspace.RootPath,
                "--apphost-project",
                appHostProjectPath,
                "--config",
                configPath,
                "--thread-id",
                threadId!,
            ]);

        var (realtimeExitCode, realtimeOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRealtimeAsync", realtimeCommand);
        Assert.Equal(0, realtimeExitCode);

        using var request = JsonDocument.Parse(await realtimeServer.WaitForFirstRequestAsync());
        var session = request.RootElement.GetProperty("session");
        Assert.Equal("transcription", session.GetProperty("type").GetString());
        Assert.False(session.TryGetProperty("instructions", out _));
        Assert.False(session.GetProperty("audio").TryGetProperty("output", out _));
        Assert.Contains("Started realtime session.", realtimeOutput, StringComparison.Ordinal);
        Assert.Contains("threadId=", realtimeOutput, StringComparison.Ordinal);
        Assert.Contains("sessionId=sess_cli_transcription_001", realtimeOutput, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalOpenAiApiKey);
    }

    private static object ParseCommand(Type parserType, string[] args)
    {
        var parseResult = ReflectionTestHelper.InvokeStaticMethod(parserType, "Parse", (object)args);
        Assert.NotNull(parseResult);

        var errorMessage = ReflectionTestHelper.GetProperty(parseResult!, "ErrorMessage") as string;
        Assert.True(string.IsNullOrWhiteSpace(errorMessage), errorMessage ?? "CLI parse failed.");

        var command = ReflectionTestHelper.GetProperty(parseResult!, "Command");
        Assert.NotNull(command);
        return command!;
    }

    private static async Task<(int ExitCode, string Output)> InvokeRunnerAndCaptureOutputAsync(object runner, string methodName, object command)
    {
        using var writer = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;

        try
        {
            Console.SetOut(writer);
            Console.SetError(writer);
            var task = ReflectionTestHelper.InvokeMethod(runner, methodName, command, CancellationToken.None);
            var exitCode = await ReflectionTestHelper.AwaitTaskResultAsync(task);
            return (Assert.IsType<int>(exitCode), writer.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TianShu.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("未找到 TianShu.sln。");
    }

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            RootPath = Path.Combine(AppContext.BaseDirectory, "cli-realtime-runtime-smoke", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
            Directory.CreateDirectory(Path.Combine(RootPath, ".tianshu"));
        }

        public string RootPath { get; }

        public string WriteConfig(string content)
        {
            var path = Path.Combine(RootPath, ".tianshu", "tianshu.toml");
            File.WriteAllText(path, content, new UTF8Encoding(false));
            return path;
        }

        public void Dispose()
        {
            if (!Directory.Exists(RootPath))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(RootPath, "*", SearchOption.AllDirectories))
            {
                var attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                }
            }

            Directory.Delete(RootPath, recursive: true);
        }
    }

    private sealed class RealtimeWebSocketTestServer : IAsyncDisposable
    {
        private readonly HttpListener listener;
        private readonly CancellationTokenSource shutdown = new();
        private readonly Task acceptLoopTask;
        private readonly TaskCompletionSource<string> firstRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);
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

        public async Task<string> WaitForFirstRequestAsync()
        {
            var completed = await Task.WhenAny(firstRequest.Task, Task.Delay(TimeSpan.FromSeconds(10), shutdown.Token)).ConfigureAwait(false);
            if (!ReferenceEquals(completed, firstRequest.Task))
            {
                throw new TimeoutException("Timed out waiting for realtime websocket request.");
            }

            return await firstRequest.Task.ConfigureAwait(false);
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
                // ignore shutdown errors in tests
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
                var request = await ReceiveTextAsync(webSocket, shutdown.Token).ConfigureAwait(false);
                firstRequest.TrySetResult(request ?? string.Empty);

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

                while (!shutdown.IsCancellationRequested && webSocket.State == WebSocketState.Open)
                {
                    var next = await ReceiveTextAsync(webSocket, shutdown.Token).ConfigureAwait(false);
                    if (next is null)
                    {
                        break;
                    }
                }
            }
            catch when (shutdown.IsCancellationRequested)
            {
            }
            catch (HttpListenerException) when (shutdown.IsCancellationRequested)
            {
            }
        }

        private static async Task<string?> ReceiveTextAsync(WebSocket webSocket, CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            using var stream = new MemoryStream();
            while (true)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                stream.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    return Encoding.UTF8.GetString(stream.ToArray());
                }
            }
        }

        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
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
}
