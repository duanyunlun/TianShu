using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using TianShu.AppHost.State;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost;

internal sealed class AppHostMcpServer : IAsyncDisposable
{
    private const string DefaultProtocolVersion = "2025-06-18";

    private readonly TextReader input;
    private readonly TextWriter output;
    private readonly ProcessAppHostServerBridge bridge;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);
    private bool initializeReceived;

    public AppHostMcpServer(
        TextReader input,
        TextWriter output,
        KernelThreadStore threadStore,
        IReadOnlyDictionary<string, string>? cliConfigOverrides = null,
        string? configFilePath = null)
    {
        this.input = input ?? throw new ArgumentNullException(nameof(input));
        this.output = output ?? throw new ArgumentNullException(nameof(output));
        bridge = new ProcessAppHostServerBridge(
            threadStore ?? throw new ArgumentNullException(nameof(threadStore)),
            cliConfigOverrides,
            configFilePath,
            ForwardInnerMessageAsync);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await input.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                JsonDocument? document = null;
                try
                {
                    document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    var id = root.TryGetProperty("id", out var idElement) ? idElement.Clone() : (JsonElement?)null;
                    if (root.TryGetProperty("method", out var methodElement)
                        && methodElement.ValueKind == JsonValueKind.String)
                    {
                        var method = methodElement.GetString()!;
                        var @params = root.TryGetProperty("params", out var paramsElement)
                            ? paramsElement.Clone()
                            : default;

                        if (id.HasValue)
                        {
                            await HandleRequestAsync(id.Value, method, @params, cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            await HandleNotificationAsync(method, @params, cancellationToken).ConfigureAwait(false);
                        }

                        continue;
                    }

                    if (id.HasValue)
                    {
                        var result = root.TryGetProperty("result", out var resultElement)
                            ? resultElement.Clone()
                            : root.TryGetProperty("error", out var errorElement)
                                ? errorElement.Clone()
                                : JsonSerializer.SerializeToElement<object?>(null, jsonOptions);
                        await bridge.RespondToForwardedRequestAsync(ReadRequestId(id.Value), result, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (JsonException ex)
                {
                    await WriteErrorAsync(id: null, -32700, $"invalid JSON: {ex.Message}", cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    document?.Dispose();
                }
            }
        }
        finally
        {
            await bridge.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await bridge.DisposeAsync().ConfigureAwait(false);
    }

    private async Task HandleRequestAsync(JsonElement id, string method, JsonElement @params, CancellationToken cancellationToken)
    {
        switch (method)
        {
            case "initialize":
                if (initializeReceived)
                {
                    await WriteErrorAsync(id, -32600, "initialize called more than once", cancellationToken).ConfigureAwait(false);
                    return;
                }

                initializeReceived = true;
                await WriteResultAsync(
                        id,
                        BuildInitializeResult(@params),
                        cancellationToken)
                    .ConfigureAwait(false);
                return;

            case "ping":
                await WriteResultAsync(id, new { }, cancellationToken).ConfigureAwait(false);
                return;

            case "tools/list":
                if (!await EnsureInitializedAsync(id, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                await EnsureBridgeStartedAsync(cancellationToken).ConfigureAwait(false);
                var listResult = await bridge.SendRequestAsync(
                        "mcpServer/tools/list",
                        null,
                        cancellationToken)
                    .ConfigureAwait(false);
                await WriteResultAsync(id, listResult, cancellationToken).ConfigureAwait(false);
                return;

            case "tools/call":
                if (!await EnsureInitializedAsync(id, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                await EnsureBridgeStartedAsync(cancellationToken).ConfigureAwait(false);
                var callPayload = @params.ValueKind == JsonValueKind.Object ? @params : JsonSerializer.SerializeToElement(new { });
                var callResult = await bridge.SendRequestAsync(
                        "mcpServer/tools/call",
                        callPayload,
                        cancellationToken)
                    .ConfigureAwait(false);
                await WriteResultAsync(id, callResult, cancellationToken).ConfigureAwait(false);
                return;

            default:
                await WriteErrorAsync(id, -32601, $"method not found: {method}", cancellationToken).ConfigureAwait(false);
                return;
        }
    }

    private async Task HandleNotificationAsync(string method, JsonElement @params, CancellationToken cancellationToken)
    {
        _ = @params;
        _ = cancellationToken;

        if (string.Equals(method, "notifications/initialized", StringComparison.Ordinal))
        {
            return;
        }
    }

    private async Task<bool> EnsureInitializedAsync(JsonElement id, CancellationToken cancellationToken)
    {
        if (initializeReceived)
        {
            return true;
        }

        await WriteErrorAsync(id, -32600, "Not initialized", cancellationToken).ConfigureAwait(false);
        return false;
    }

    private Task EnsureBridgeStartedAsync(CancellationToken cancellationToken)
    {
        return bridge.StartAsync(cancellationToken);
    }

    private object BuildInitializeResult(JsonElement @params)
    {
        var protocolVersion = @params.ValueKind == JsonValueKind.Object
                              && @params.TryGetProperty("protocolVersion", out var protocolVersionElement)
                              && protocolVersionElement.ValueKind == JsonValueKind.String
            ? protocolVersionElement.GetString()
            : null;

        return new Dictionary<string, object?>
        {
            ["protocolVersion"] = string.IsNullOrWhiteSpace(protocolVersion) ? DefaultProtocolVersion : protocolVersion,
            ["capabilities"] = new Dictionary<string, object?>
            {
                ["tools"] = new Dictionary<string, object?>
                {
                    ["listChanged"] = true,
                },
            },
            ["serverInfo"] = new Dictionary<string, object?>
            {
                ["name"] = "tianshu-mcp-server",
                ["title"] = "TianShu",
                ["version"] = "0.1.0",
                ["user_agent"] = "tianshu-mcp-server",
            },
        };
    }

    private async Task ForwardInnerMessageAsync(JsonElement message, CancellationToken cancellationToken)
    {
        await WriteMessageAsync(NormalizeJsonRpcMessagePayload(message), cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteResultAsync(JsonElement id, object? result, CancellationToken cancellationToken)
    {
        await WriteMessageAsync(
                new Dictionary<string, object?>
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id.Clone(),
                    ["result"] = result,
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task WriteErrorAsync(JsonElement? id, int code, string message, CancellationToken cancellationToken)
    {
        await WriteMessageAsync(
                new Dictionary<string, object?>
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id.HasValue ? id.Value.Clone() : null,
                    ["error"] = new Dictionary<string, object?>
                    {
                        ["code"] = code,
                        ["message"] = message,
                    },
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task WriteMessageAsync(object payload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await output.WriteLineAsync(JsonSerializer.Serialize(payload, jsonOptions)).ConfigureAwait(false);
        await output.FlushAsync().ConfigureAwait(false);
    }

    private static Dictionary<string, object?> NormalizeJsonRpcMessagePayload(JsonElement payload)
    {
        var normalized = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (payload.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in payload.EnumerateObject())
            {
                normalized[property.Name] = property.Value.Clone();
            }
        }

        if (!normalized.ContainsKey("jsonrpc"))
        {
            normalized["jsonrpc"] = "2.0";
        }

        return normalized;
    }

    private static long ReadRequestId(JsonElement id)
    {
        return id.ValueKind switch
        {
            JsonValueKind.Number when id.TryGetInt64(out var number) => number,
            _ => throw new InvalidOperationException("MCP request id 必须是整数。"),
        };
    }

    private sealed class ProcessAppHostServerBridge : IAsyncDisposable
    {
        private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pendingResponses = new();
        private readonly KernelThreadStore threadStore;
        private readonly IReadOnlyDictionary<string, string>? cliConfigOverrides;
        private readonly string? configFilePath;
        private readonly Func<JsonElement, CancellationToken, Task> outgoingHandler;
        private Process? process;
        private StreamWriter? stdin;
        private Task? stdoutPumpTask;
        private Task? stderrPumpTask;
        private long nextRequestId = -1;
        private bool disposed;

        public ProcessAppHostServerBridge(
            KernelThreadStore threadStore,
            IReadOnlyDictionary<string, string>? cliConfigOverrides,
            string? configFilePath,
            Func<JsonElement, CancellationToken, Task> outgoingHandler)
        {
            this.threadStore = threadStore;
            this.cliConfigOverrides = cliConfigOverrides;
            this.configFilePath = configFilePath;
            this.outgoingHandler = outgoingHandler;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (process is not null)
            {
                return;
            }

            process = new Process
            {
                StartInfo = BuildStartInfo(),
                EnableRaisingEvents = true,
            };
            if (!process.Start())
            {
                throw new InvalidOperationException("启动 TianShu app-server 子进程失败。");
            }

            stdin = process.StandardInput;
            stdoutPumpTask = PumpOutputAsync(process.StandardOutput, cancellationToken);
            stderrPumpTask = PumpStderrAsync(process.StandardError, cancellationToken);

            _ = await SendRequestAsync(
                    "initialize",
                    JsonSerializer.SerializeToElement(new
                    {
                        clientInfo = new
                        {
                            name = "tianshu_mcp_server_bridge",
                            title = "TianShu MCP Server Bridge",
                            version = "0.1.0",
                        },
                    }),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<JsonElement> SendRequestAsync(string method, JsonElement? @params, CancellationToken cancellationToken)
        {
            var writer = stdin ?? throw new InvalidOperationException("TianShu app-server 子进程尚未启动。");
            var requestId = Interlocked.Decrement(ref nextRequestId);
            var completionSource = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingResponses[requestId] = completionSource;

            await writer.WriteLineAsync(
                    JsonSerializer.Serialize(new Dictionary<string, object?>
                    {
                        ["id"] = requestId,
                        ["method"] = method,
                        ["params"] = @params.HasValue ? @params.Value.Clone() : null,
                    }))
                .ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);

            return await completionSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public Task RespondToForwardedRequestAsync(long requestId, JsonElement result, CancellationToken cancellationToken)
        {
            var payload = JsonSerializer.SerializeToElement(new
            {
                requestId,
                result = result.Clone(),
            });
            return SendRequestAsync("serverRequest/respond", payload, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            try
            {
                stdin?.Close();
            }
            catch (IOException)
            {
            }

            if (process is not null)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (InvalidOperationException)
                {
                }
                catch (NotSupportedException)
                {
                }
            }

            if (stdoutPumpTask is not null)
            {
                await IgnoreCancellationAsync(stdoutPumpTask).ConfigureAwait(false);
            }

            if (stderrPumpTask is not null)
            {
                await IgnoreCancellationAsync(stderrPumpTask).ConfigureAwait(false);
            }
        }

        private ProcessStartInfo BuildStartInfo()
        {
            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Environment.CurrentDirectory,
            };
            ApplyExecutable(startInfo);
            startInfo.ArgumentList.Add("app-server");
            startInfo.ArgumentList.Add("--listen");
            startInfo.ArgumentList.Add("stdio://");

            if (!string.IsNullOrWhiteSpace(configFilePath))
            {
                startInfo.ArgumentList.Add("--config-file");
                startInfo.ArgumentList.Add(configFilePath);
            }

            if (cliConfigOverrides is not null)
            {
                foreach (var pair in cliConfigOverrides)
                {
                    startInfo.ArgumentList.Add("-c");
                    startInfo.ArgumentList.Add($"{pair.Key}={pair.Value}");
                }
            }

            var stateDirectory = Path.GetDirectoryName(threadStore.FilePath);
            if (!string.IsNullOrWhiteSpace(stateDirectory))
            {
                startInfo.Environment["TIANSHU_STATE_HOME"] = stateDirectory;
            }

            startInfo.Environment["TIANSHU_SESSIONS_HOME"] = threadStore.RolloutRecorder.SessionsDirectoryPath;
            return startInfo;
        }

        private static void ApplyExecutable(ProcessStartInfo startInfo)
        {
            var executablePath = Environment.ProcessPath;
            var executableName = string.IsNullOrWhiteSpace(executablePath)
                ? null
                : Path.GetFileNameWithoutExtension(executablePath);
            var assemblyPath = Path.Combine(
                AppContext.BaseDirectory,
                $"{typeof(AppHostServer).Assembly.GetName().Name}.dll");
            if (!string.IsNullOrWhiteSpace(executablePath)
                && string.Equals(executableName, "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(assemblyPath))
                {
                    startInfo.FileName = executablePath;
                    startInfo.ArgumentList.Add(assemblyPath);
                    return;
                }
            }

            if (File.Exists(assemblyPath)
                && (string.IsNullOrWhiteSpace(executablePath)
                    || string.Equals(executableName, "testhost", StringComparison.OrdinalIgnoreCase)))
            {
                startInfo.FileName = "dotnet";
                startInfo.ArgumentList.Add(assemblyPath);
                return;
            }

            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new InvalidOperationException("无法定位 TianShu AppHost 可执行入口。");
            }

            startInfo.FileName = executablePath;
        }

        private async Task PumpOutputAsync(TextReader reader, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (root.TryGetProperty("id", out var idElement)
                        && idElement.ValueKind == JsonValueKind.Number
                        && idElement.TryGetInt64(out var responseId)
                        && pendingResponses.TryRemove(responseId, out var pendingResponse))
                    {
                        if (root.TryGetProperty("error", out var errorElement))
                        {
                            pendingResponse.TrySetException(new InvalidOperationException(
                                errorElement.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
                                    ? messageElement.GetString()
                                    : "app-server request failed"));
                        }
                        else
                        {
                            var result = root.TryGetProperty("result", out var resultElement)
                                ? resultElement.Clone()
                                : JsonSerializer.SerializeToElement<object?>(null);
                            pendingResponse.TrySetResult(result);
                        }

                        continue;
                    }

                    await outgoingHandler(root.Clone(), cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                CompletePendingResponses(ex);
                throw;
            }
            finally
            {
                if (!disposed && !cancellationToken.IsCancellationRequested)
                {
                    CompletePendingResponses(new IOException("TianShu app-server output closed."));
                }
            }
        }

        private void CompletePendingResponses(Exception exception)
        {
            foreach (var pair in pendingResponses.ToArray())
            {
                if (pendingResponses.TryRemove(pair.Key, out var pendingResponse))
                {
                    pendingResponse.TrySetException(exception);
                }
            }
        }

        private static async Task PumpStderrAsync(TextReader reader, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (await reader.ReadLineAsync().ConfigureAwait(false) is null)
                {
                    break;
                }
            }
        }

        private static async Task IgnoreCancellationAsync(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
