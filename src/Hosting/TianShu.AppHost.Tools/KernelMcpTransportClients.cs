using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TianShu.AppHost.Tools;

internal interface IKernelMcpClient : IAsyncDisposable
{
    Task<JsonElement> SendRequestAsync(string method, object? parameters, CancellationToken cancellationToken);

    Task SendSandboxStateAsync(KernelMcpSandboxState sandboxState, CancellationToken cancellationToken);
}

internal sealed class KernelMcpStreamableHttpClient : IKernelMcpClient
{
    private const string ProtocolVersion = "2025-06-18";

    private readonly Uri endpoint;
    private readonly HttpClient httpClient;
    private readonly TimeSpan startupTimeout;
    private readonly TimeSpan requestTimeout;
    private readonly IReadOnlyDictionary<string, string> headers;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SemaphoreSlim initializeGate = new(1, 1);

    private string? sessionId;
    private bool initialized;
    private bool supportsSandboxStateCapability;
    private string? lastSandboxStatePayload;
    private int nextRequestId = 1;

    public KernelMcpStreamableHttpClient(
        Uri endpoint,
        HttpClient httpClient,
        TimeSpan startupTimeout,
        TimeSpan requestTimeout,
        IReadOnlyDictionary<string, string> headers)
    {
        this.endpoint = endpoint;
        this.httpClient = httpClient;
        this.startupTimeout = startupTimeout;
        this.requestTimeout = requestTimeout;
        this.headers = headers;
    }

    public async Task<JsonElement> SendRequestAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SendRequestCoreAsync(method, parameters, requestTimeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (initialized)
        {
            return;
        }

        await initializeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (initialized)
            {
                return;
            }

            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var initializeResult = await SendRequestCoreAsync(
                    "initialize",
                    new
                    {
                        protocolVersion = ProtocolVersion,
                        capabilities = new
                        {
                            experimental = (object?)null,
                            extensions = (object?)null,
                            roots = (object?)null,
                            sampling = (object?)null,
                            elicitation = (object?)null,
                            tasks = (object?)null,
                        },
                        clientInfo = new
                        {
                            name = "tianshu-mcp-client",
                            version = "0.1.0",
                            title = "TianShu",
                        },
                    },
                    startupTimeout,
                    cancellationToken).ConfigureAwait(false);
                supportsSandboxStateCapability = SupportsSandboxStateCapability(initializeResult);

                await SendNotificationCoreAsync("notifications/initialized", null, startupTimeout, cancellationToken).ConfigureAwait(false);
                initialized = true;
            }
            finally
            {
                gate.Release();
            }
        }
        finally
        {
            initializeGate.Release();
        }
    }

    public async Task SendSandboxStateAsync(KernelMcpSandboxState sandboxState, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sandboxState);

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        if (!supportsSandboxStateCapability)
        {
            return;
        }

        var payload = sandboxState.ToJson();
        if (string.Equals(lastSandboxStatePayload, payload, StringComparison.Ordinal))
        {
            return;
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _ = await SendRequestCoreAsync(
                KernelMcpManager.McpSandboxStateMethod,
                sandboxState.ToPayload(),
                requestTimeout,
                cancellationToken).ConfigureAwait(false);
            lastSandboxStatePayload = payload;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<JsonElement> SendRequestCoreAsync(string method, object? parameters, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var requestId = Interlocked.Increment(ref nextRequestId);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = BuildRequestContent(new
            {
                jsonrpc = "2.0",
                id = requestId,
                method,
                @params = parameters,
            }),
        };

        ApplyHeaders(request);
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", ProtocolVersion);
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            request.Headers.TryAddWithoutValidation("MCP-Session-Id", sessionId);
        }

        using var response = await SendHttpAsync(request, timeout, cancellationToken).ConfigureAwait(false);
        CaptureSessionId(response);
        var payload = await ReadJsonRpcPayloadAsync(response, cancellationToken).ConfigureAwait(false);
        return ExtractJsonRpcResult(payload, requestId, method);
    }

    private async Task SendNotificationCoreAsync(string method, object? parameters, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = BuildRequestContent(new
            {
                jsonrpc = "2.0",
                method,
                @params = parameters,
            }),
        };

        ApplyHeaders(request);
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", ProtocolVersion);
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            request.Headers.TryAddWithoutValidation("MCP-Session-Id", sessionId);
        }

        using var response = await SendHttpAsync(request, timeout, cancellationToken).ConfigureAwait(false);
        CaptureSessionId(response);
        if ((int)response.StatusCode >= 400)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"MCP notification `{method}` failed with HTTP {(int)response.StatusCode}: {body}");
        }
    }

    private async Task<HttpResponseMessage> SendHttpAsync(HttpRequestMessage request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"MCP HTTP request timed out after {timeout.TotalSeconds:0.###} seconds.");
        }
    }

    private static StringContent BuildRequestContent(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private void ApplyHeaders(HttpRequestMessage request)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        foreach (var pair in headers)
        {
            request.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
        }
    }

    private void CaptureSessionId(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("MCP-Session-Id", out var values))
        {
            sessionId = values.FirstOrDefault();
        }
    }

    private static async Task<JsonElement> ReadJsonRpcPayloadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if ((int)response.StatusCode >= 400)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"MCP request failed with HTTP {(int)response.StatusCode}: {errorBody}");
        }

        var contentType = response.Content.Headers.ContentType?.MediaType;
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("MCP response body was empty.");
        }

        if (string.Equals(contentType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            var eventPayload = ExtractFirstSseDataPayload(text);
            if (string.IsNullOrWhiteSpace(eventPayload))
            {
                throw new InvalidOperationException("MCP SSE response did not contain a data payload.");
            }

            return JsonDocument.Parse(eventPayload).RootElement.Clone();
        }

        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static string? ExtractFirstSseDataPayload(string text)
    {
        var builder = new StringBuilder();
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (builder.Length > 0)
                {
                    return builder.ToString();
                }

                continue;
            }

            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(line[5..].TrimStart());
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static JsonElement ExtractJsonRpcResult(JsonElement payload, int requestId, string method)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"MCP response for `{method}` was not a JSON object.");
        }

        if (payload.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException(BuildJsonRpcErrorMessage(method, error));
        }

        if (!payload.TryGetProperty("id", out var idElement))
        {
            throw new InvalidOperationException($"MCP response for `{method}` returned an unexpected id.");
        }

        var idValue = idElement.ValueKind switch
        {
            JsonValueKind.Number when idElement.TryGetInt32(out var numericId) => numericId,
            _ => int.MinValue,
        };

        if (idValue != requestId)
        {
            throw new InvalidOperationException($"MCP response for `{method}` returned an unexpected id.");
        }

        if (!payload.TryGetProperty("result", out var result))
        {
            throw new InvalidOperationException($"MCP response for `{method}` did not include a result.");
        }

        return result.Clone();
    }

    private static bool SupportsSandboxStateCapability(JsonElement initializeResult)
    {
        return initializeResult.ValueKind == JsonValueKind.Object
               && initializeResult.TryGetProperty("capabilities", out var capabilities)
               && capabilities.ValueKind == JsonValueKind.Object
               && capabilities.TryGetProperty("experimental", out var experimental)
               && experimental.ValueKind == JsonValueKind.Object
               && experimental.TryGetProperty(KernelMcpManager.McpSandboxStateCapability, out _);
    }

    private static string BuildJsonRpcErrorMessage(string method, JsonElement error)
    {
        var code = error.TryGetProperty("code", out var codeElement) ? codeElement.GetRawText() : "unknown";
        var message = error.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString() ?? "unknown"
            : "unknown";
        return $"MCP request `{method}` failed ({code}): {message}";
    }

    public ValueTask DisposeAsync()
    {
        gate.Dispose();
        initializeGate.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed class KernelMcpStdioClient : IKernelMcpClient
{
    private const string ProtocolVersion = "2025-06-18";

    private readonly string serverName;
    private readonly string command;
    private readonly IReadOnlyList<string> args;
    private readonly string? workingDirectory;
    private readonly IReadOnlyDictionary<string, string> environment;
    private readonly IReadOnlyList<string> inheritedEnvironmentVariables;
    private readonly Func<string, string?> readEnvironmentVariable;
    private readonly TimeSpan startupTimeout;
    private readonly TimeSpan requestTimeout;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SemaphoreSlim initializeGate = new(1, 1);
    private readonly ConcurrentQueue<string> stderrLines = new();

    private Process? process;
    private StreamWriter? stdin;
    private StreamReader? stdout;
    private StreamReader? stderr;
    private Task? stderrPumpTask;
    private int nextRequestId = 1;
    private bool initialized;
    private bool supportsSandboxStateCapability;
    private string? lastSandboxStatePayload;

    public KernelMcpStdioClient(
        string serverName,
        string command,
        IReadOnlyList<string> args,
        string? workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        IReadOnlyList<string> inheritedEnvironmentVariables,
        Func<string, string?> readEnvironmentVariable,
        TimeSpan startupTimeout,
        TimeSpan requestTimeout)
    {
        this.serverName = serverName;
        this.command = command;
        this.args = args;
        this.workingDirectory = workingDirectory;
        this.environment = environment;
        this.inheritedEnvironmentVariables = inheritedEnvironmentVariables;
        this.readEnvironmentVariable = readEnvironmentVariable;
        this.startupTimeout = startupTimeout;
        this.requestTimeout = requestTimeout;
    }

    public async Task<JsonElement> SendRequestAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SendRequestCoreAsync(method, parameters, requestTimeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (initialized)
        {
            return;
        }

        await initializeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (initialized)
            {
                return;
            }

            EnsureProcessStarted();

            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var initializeResult = await SendRequestCoreAsync(
                    "initialize",
                    new
                    {
                        protocolVersion = ProtocolVersion,
                        capabilities = new
                        {
                            experimental = (object?)null,
                            extensions = (object?)null,
                            roots = (object?)null,
                            sampling = (object?)null,
                            elicitation = (object?)null,
                            tasks = (object?)null,
                        },
                        clientInfo = new
                        {
                            name = "tianshu-mcp-client",
                            version = "0.1.0",
                            title = "TianShu",
                        },
                    },
                    startupTimeout,
                    cancellationToken).ConfigureAwait(false);
                supportsSandboxStateCapability = SupportsSandboxStateCapability(initializeResult);

                await SendNotificationCoreAsync("notifications/initialized", null, startupTimeout, cancellationToken).ConfigureAwait(false);
                initialized = true;
            }
            finally
            {
                gate.Release();
            }
        }
        finally
        {
            initializeGate.Release();
        }
    }

    public async Task SendSandboxStateAsync(KernelMcpSandboxState sandboxState, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sandboxState);

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        if (!supportsSandboxStateCapability)
        {
            return;
        }

        var payload = sandboxState.ToJson();
        if (string.Equals(lastSandboxStatePayload, payload, StringComparison.Ordinal))
        {
            return;
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _ = await SendRequestCoreAsync(
                KernelMcpManager.McpSandboxStateMethod,
                sandboxState.ToPayload(),
                requestTimeout,
                cancellationToken).ConfigureAwait(false);
            lastSandboxStatePayload = payload;
        }
        finally
        {
            gate.Release();
        }
    }

    private void EnsureProcessStarted()
    {
        if (process is { HasExited: false } && stdin is not null && stdout is not null)
        {
            return;
        }

        var startInfo = new ProcessStartInfo(command)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        foreach (var pair in environment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        foreach (var variable in inheritedEnvironmentVariables)
        {
            var value = readEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                startInfo.Environment[variable] = value;
            }
        }

        process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start MCP server `{serverName}`.");
        }

        stdin = process.StandardInput;
        stdout = process.StandardOutput;
        stderr = process.StandardError;
        stderrPumpTask = Task.Run(PumpStderrAsync);
    }

    private async Task<JsonElement> SendRequestCoreAsync(string method, object? parameters, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (stdin is null || stdout is null)
        {
            throw new InvalidOperationException($"MCP server `{serverName}` is not running.");
        }

        var requestId = Interlocked.Increment(ref nextRequestId);
        var payload = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = requestId,
            method,
            @params = parameters,
        });

        await WriteLineAsync(stdin, payload, timeout, cancellationToken).ConfigureAwait(false);

        while (true)
        {
            var line = await ReadStdoutLineAsync(stdout, method, timeout, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var response = JsonDocument.Parse(line);
            var root = response.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!root.TryGetProperty("id", out var idElement))
            {
                continue;
            }

            if (idElement.ValueKind == JsonValueKind.Number && idElement.TryGetInt32(out var idValue) && idValue == requestId)
            {
                if (root.TryGetProperty("error", out var error))
                {
                    throw new InvalidOperationException(BuildJsonRpcErrorMessage(method, error));
                }

                if (!root.TryGetProperty("result", out var result))
                {
                    throw new InvalidOperationException($"MCP response for `{method}` did not include a result.");
                }

                return result.Clone();
            }
        }
    }

    private async Task SendNotificationCoreAsync(string method, object? parameters, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (stdin is null)
        {
            throw new InvalidOperationException($"MCP server `{serverName}` is not running.");
        }

        var payload = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method,
            @params = parameters,
        });

        await WriteLineAsync(stdin, payload, timeout, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> ReadStdoutLineAsync(StreamReader reader, string method, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            var line = await reader.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);
            if (line is null)
            {
                throw new InvalidOperationException(BuildProcessExitMessage(method));
            }

            return line;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"MCP server `{serverName}` timed out while waiting for `{method}`.");
        }
    }

    private async Task PumpStderrAsync()
    {
        if (stderr is null)
        {
            return;
        }

        try
        {
            while (true)
            {
                var line = await stderr.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                stderrLines.Enqueue(line);
                while (stderrLines.Count > 40 && stderrLines.TryDequeue(out _))
                {
                }
            }
        }
        catch
        {
        }
    }

    private async Task WriteLineAsync(StreamWriter writer, string line, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await writer.WriteAsync(line.AsMemory(), timeoutCts.Token).ConfigureAwait(false);
            await writer.WriteAsync(Environment.NewLine.AsMemory(), timeoutCts.Token).ConfigureAwait(false);
            await writer.FlushAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"MCP server `{serverName}` timed out while sending a request.");
        }
    }

    private string BuildProcessExitMessage(string method)
    {
        var stderrText = string.Join(Environment.NewLine, stderrLines);
        if (process is not null && process.HasExited)
        {
            return string.IsNullOrWhiteSpace(stderrText)
                ? $"MCP server `{serverName}` exited with code {process.ExitCode} during `{method}`."
                : $"MCP server `{serverName}` exited with code {process.ExitCode} during `{method}`. stderr: {stderrText}";
        }

        return string.IsNullOrWhiteSpace(stderrText)
            ? $"MCP server `{serverName}` closed stdout during `{method}`."
            : $"MCP server `{serverName}` closed stdout during `{method}`. stderr: {stderrText}";
    }

    private static string BuildJsonRpcErrorMessage(string method, JsonElement error)
    {
        var code = error.TryGetProperty("code", out var codeElement) ? codeElement.GetRawText() : "unknown";
        var message = error.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString() ?? "unknown"
            : "unknown";
        return $"MCP request `{method}` failed ({code}): {message}";
    }

    private static bool SupportsSandboxStateCapability(JsonElement initializeResult)
    {
        return initializeResult.ValueKind == JsonValueKind.Object
               && initializeResult.TryGetProperty("capabilities", out var capabilities)
               && capabilities.ValueKind == JsonValueKind.Object
               && capabilities.TryGetProperty("experimental", out var experimental)
               && experimental.ValueKind == JsonValueKind.Object
               && experimental.TryGetProperty(KernelMcpManager.McpSandboxStateCapability, out _);
    }

    public async ValueTask DisposeAsync()
    {
        gate.Dispose();
        initializeGate.Dispose();

        if (stdin is not null)
        {
            try
            {
                await stdin.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
            }
        }

        if (stdout is not null)
        {
            stdout.Dispose();
        }

        if (stderr is not null)
        {
            stderr.Dispose();
        }

        if (process is not null)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
            }
            catch
            {
            }

            process.Dispose();
        }

        if (stderrPumpTask is not null)
        {
            try
            {
                await stderrPumpTask.ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }
}
