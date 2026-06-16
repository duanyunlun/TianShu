using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TianShu.AppHost.Tools;

internal sealed record KernelCodeModeOptions(
    string NodePath,
    string WorkingDirectory);

internal sealed record KernelCodeModeToolCall(
    string RequestId,
    string Id,
    string ToolName,
    JsonElement? Input);

internal enum KernelCodeModeToolKind
{
    Function,
    Freeform,
}

internal sealed record KernelCodeModeEnabledTool(
    [property: JsonPropertyName("tool_name")] string ToolName,
    [property: JsonPropertyName("global_name")] string GlobalName,
    [property: JsonPropertyName("module")] string ModulePath,
    [property: JsonPropertyName("namespace")] IReadOnlyList<string> Namespace,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("kind")] string Kind);

internal sealed class KernelCodeModeManager : IAsyncDisposable
{
    private const string RunnerResourceName = "TianShu.AppHost.Tools.Resources.code-mode.runner.cjs";
    private const string BridgeResourceName = "TianShu.AppHost.Tools.Resources.code-mode.bridge.js";
    internal const int DefaultExecYieldTimeMs = 10_000;
    internal const int DefaultWaitYieldTimeMs = 10_000;
    internal const int DefaultMaxOutputTokens = 10_000;

    private static readonly Lazy<string> RunnerSource = new(LoadRunnerSource);
    private static readonly Lazy<string> BridgeSource = new(LoadBridgeSource);

    private readonly KernelCodeModeOptions options;
    private readonly SemaphoreSlim requestGate = new(1, 1);
    private readonly SemaphoreSlim processGate = new(1, 1);
    private readonly SemaphoreSlim stdinWriteGate = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> pendingResponses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonElement> storedValues = new(StringComparer.Ordinal);
    private readonly object stateGate = new();

    private Process? process;
    private StreamWriter? standardInput;
    private CancellationTokenSource? processLifetimeCts;
    private Task? stdoutLoopTask;
    private Task? stderrLoopTask;
    private string? activeTurnId;
    private Func<KernelCodeModeToolCall, CancellationToken, Task<JsonElement>>? activeToolInvoker;
    private CancellationToken activeToolInvokerCancellationToken;
    private long nextCellId;

    public KernelCodeModeManager(KernelCodeModeOptions options)
    {
        this.options = options;
    }

    public void ActivateTurn(
        string turnId,
        Func<KernelCodeModeToolCall, CancellationToken, Task<JsonElement>> toolInvoker,
        CancellationToken cancellationToken)
    {
        lock (stateGate)
        {
            activeTurnId = turnId;
            activeToolInvoker = toolInvoker;
            activeToolInvokerCancellationToken = cancellationToken;
        }
    }

    public void DeactivateTurn(string turnId)
    {
        lock (stateGate)
        {
            if (!string.Equals(activeTurnId, turnId, StringComparison.Ordinal))
            {
                return;
            }

            activeTurnId = null;
            activeToolInvokerCancellationToken = default;
        }
    }

    public async Task<KernelCodeModeOperationResult> ExecuteAsync(
        KernelCodeModeExecutionRequest request,
        IReadOnlyList<KernelCodeModeEnabledTool> enabledTools,
        CancellationToken cancellationToken)
    {
        await requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureProcessAsync(cancellationToken).ConfigureAwait(false);

            var requestId = Guid.NewGuid().ToString();
            var cellId = Interlocked.Increment(ref nextCellId).ToString();
            var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!pendingResponses.TryAdd(requestId, completion))
            {
                throw new InvalidOperationException("无法创建 exec 响应槽位。");
            }

            try
            {
                var startedAt = Stopwatch.StartNew();
                await SendMessageAsync(new Dictionary<string, object?>
                {
                    ["type"] = "start",
                    ["request_id"] = requestId,
                    ["cell_id"] = cellId,
                    ["default_yield_time_ms"] = DefaultExecYieldTimeMs,
                    ["enabled_tools"] = enabledTools,
                    ["stored_values"] = CloneStoredValuesForTransport(),
                    ["source"] = BuildSource(request.Code),
                    ["yield_time_ms"] = request.YieldTimeMs,
                    ["max_output_tokens"] = request.MaxOutputTokens,
                }, cancellationToken).ConfigureAwait(false);

                var payload = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return HandleProtocolResult(payload, cellId, request.MaxOutputTokens, startedAt.Elapsed);
            }
            finally
            {
                pendingResponses.TryRemove(requestId, out _);
            }
        }
        finally
        {
            requestGate.Release();
        }
    }

    public async Task<KernelCodeModeOperationResult> WaitAsync(
        KernelCodeModeWaitRequest request,
        CancellationToken cancellationToken)
    {
        await requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureProcessAsync(cancellationToken).ConfigureAwait(false);

            var requestId = Guid.NewGuid().ToString();
            var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!pendingResponses.TryAdd(requestId, completion))
            {
                throw new InvalidOperationException("无法创建 exec_wait 响应槽位。");
            }

            var startedAt = Stopwatch.StartNew();
            try
            {
                await SendMessageAsync(
                    request.Terminate
                        ? new Dictionary<string, object?>
                        {
                            ["type"] = "terminate",
                            ["request_id"] = requestId,
                            ["cell_id"] = request.CellId,
                        }
                        : new Dictionary<string, object?>
                        {
                            ["type"] = "poll",
                            ["request_id"] = requestId,
                            ["cell_id"] = request.CellId,
                            ["yield_time_ms"] = request.YieldTimeMs,
                        },
                    cancellationToken).ConfigureAwait(false);

                var payload = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return HandleProtocolResult(payload, request.CellId, request.MaxTokens, startedAt.Elapsed);
            }
            finally
            {
                pendingResponses.TryRemove(requestId, out _);
            }
        }
        finally
        {
            requestGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await RestartProcessAsync().ConfigureAwait(false);
        requestGate.Dispose();
        processGate.Dispose();
        stdinWriteGate.Dispose();
    }

    private static string LoadRunnerSource()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(RunnerResourceName)
            ?? throw new InvalidOperationException($"未找到嵌入资源：{RunnerResourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string LoadBridgeSource()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(BridgeResourceName)
            ?? throw new InvalidOperationException($"未找到嵌入资源：{BridgeResourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private async Task EnsureProcessAsync(CancellationToken cancellationToken)
    {
        if (IsProcessAlive())
        {
            return;
        }

        await processGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsProcessAlive())
            {
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = string.IsNullOrWhiteSpace(options.NodePath) ? "node" : options.NodePath,
                WorkingDirectory = string.IsNullOrWhiteSpace(options.WorkingDirectory)
                    ? Environment.CurrentDirectory
                    : options.WorkingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("--experimental-vm-modules");
            startInfo.ArgumentList.Add("--eval");
            startInfo.ArgumentList.Add(RunnerSource.Value);

            var startedProcess = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true,
            };
            if (!startedProcess.Start())
            {
                throw new InvalidOperationException("无法启动 exec Node 进程。");
            }

            process = startedProcess;
            standardInput = startedProcess.StandardInput;
            standardInput.NewLine = "\n";
            standardInput.AutoFlush = true;
            processLifetimeCts = new CancellationTokenSource();
            stdoutLoopTask = Task.Run(() => ReadStdoutLoopAsync(startedProcess.StandardOutput, processLifetimeCts.Token));
            stderrLoopTask = Task.Run(() => ReadStderrLoopAsync(startedProcess.StandardError, processLifetimeCts.Token));
        }
        finally
        {
            processGate.Release();
        }
    }

    private bool IsProcessAlive()
        => process is { HasExited: false };

    private async Task RestartProcessAsync()
    {
        Process? processToStop;
        CancellationTokenSource? lifetimeToStop;
        Task? stdoutToAwait;
        Task? stderrToAwait;

        await processGate.WaitAsync().ConfigureAwait(false);
        try
        {
            processToStop = process;
            lifetimeToStop = processLifetimeCts;
            stdoutToAwait = stdoutLoopTask;
            stderrToAwait = stderrLoopTask;
            process = null;
            standardInput = null;
            processLifetimeCts = null;
            stdoutLoopTask = null;
            stderrLoopTask = null;
        }
        finally
        {
            processGate.Release();
        }

        lifetimeToStop?.Cancel();

        if (processToStop is not null)
        {
            try
            {
                if (!processToStop.HasExited)
                {
                    processToStop.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            try
            {
                await processToStop.WaitForExitAsync().ConfigureAwait(false);
            }
            catch
            {
            }
            finally
            {
                processToStop.Dispose();
            }
        }

        if (stdoutToAwait is not null)
        {
            try
            {
                await stdoutToAwait.ConfigureAwait(false);
            }
            catch
            {
            }
        }

        if (stderrToAwait is not null)
        {
            try
            {
                await stderrToAwait.ConfigureAwait(false);
            }
            catch
            {
            }
        }

        var failurePayload = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["type"] = "result",
            ["content_items"] = Array.Empty<object>(),
            ["stored_values"] = new Dictionary<string, object?>(),
            ["error_text"] = "exec runner terminated unexpectedly",
            ["max_output_tokens_per_exec_call"] = DefaultMaxOutputTokens,
        });
        FailPendingResponses(failurePayload);
    }

    private async Task SendMessageAsync(object payload, CancellationToken cancellationToken)
    {
        var writer = standardInput ?? throw new InvalidOperationException("exec 标准输入尚未初始化。");
        var line = JsonSerializer.Serialize(payload);
        await stdinWriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await writer.WriteLineAsync(line).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            stdinWriteGate.Release();
        }
    }

    private async Task ReadStdoutLoopAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using var document = JsonDocument.Parse(line);
                var root = document.RootElement.Clone();
                var type = ReadString(root, "type");
                if (string.Equals(type, "tool_call", StringComparison.Ordinal))
                {
                    _ = Task.Run(() => HandleToolCallAsync(root, cancellationToken), CancellationToken.None);
                    continue;
                }

                var requestId = ReadString(root, "request_id");
                if (!string.IsNullOrWhiteSpace(requestId)
                    && pendingResponses.TryRemove(requestId!, out var completion))
                {
                    completion.TrySetResult(root);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            await RestartProcessAsync().ConfigureAwait(false);
        }
        finally
        {
            FailPendingResponses(JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["type"] = "result",
                ["content_items"] = Array.Empty<object>(),
                ["stored_values"] = new Dictionary<string, object?>(),
                ["error_text"] = "exec runner terminated unexpectedly",
                ["max_output_tokens_per_exec_call"] = DefaultMaxOutputTokens,
            }));
        }
    }

    private async Task ReadStderrLoopAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void FailPendingResponses(JsonElement payload)
    {
        foreach (var pending in pendingResponses.ToArray())
        {
            pending.Value.TrySetResult(payload);
            pendingResponses.TryRemove(pending.Key, out _);
        }
    }

    private async Task HandleToolCallAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var requestId = ReadString(payload, "request_id");
        var id = ReadString(payload, "id");
        var toolName = ReadString(payload, "name");
        JsonElement? input = null;
        if (payload.TryGetProperty("input", out var inputElement)
            && inputElement.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            input = inputElement.Clone();
        }

        if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(toolName))
        {
            return;
        }

        Func<KernelCodeModeToolCall, CancellationToken, Task<JsonElement>>? toolInvoker;
        CancellationToken invokerCancellationToken;
        lock (stateGate)
        {
            toolInvoker = activeToolInvoker;
            invokerCancellationToken = activeToolInvokerCancellationToken;
        }

        JsonElement codeModeResult;
        if (toolInvoker is null)
        {
            codeModeResult = JsonSerializer.SerializeToElement("exec tool runtime is unavailable");
        }
        else
        {
            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, invokerCancellationToken);
                codeModeResult = await toolInvoker(
                    new KernelCodeModeToolCall(requestId!, id!, toolName!, input),
                    linkedCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                codeModeResult = JsonSerializer.SerializeToElement(NormalizeMessage(ex.Message) ?? "exec nested tool failed");
            }
        }

        await SendMessageAsync(new Dictionary<string, object?>
        {
            ["type"] = "response",
            ["request_id"] = requestId,
            ["id"] = id,
            ["code_mode_result"] = JsonSerializer.Deserialize<object?>(codeModeResult.GetRawText()),
        }, cancellationToken).ConfigureAwait(false);
    }

    private KernelCodeModeOperationResult HandleProtocolResult(
        JsonElement payload,
        string cellId,
        int? maxTokensOverride,
        TimeSpan wallTime)
    {
        var type = ReadString(payload, "type");
        var contentItems = ParseContentItems(payload);
        return type switch
        {
            "yielded" => CreateRunningResult(cellId, contentItems, maxTokensOverride, wallTime),
            "terminated" => CreateFinishedResult("Script terminated", true, contentItems, maxTokensOverride, wallTime),
            "result" => CreateResultResult(payload, contentItems, maxTokensOverride, wallTime),
            _ => new KernelCodeModeOperationResult(false, "exec returned an unknown protocol payload", [])
        };
    }

    private KernelCodeModeOperationResult CreateRunningResult(
        string cellId,
        IReadOnlyList<KernelToolOutputContentItem> contentItems,
        int? maxTokensOverride,
        TimeSpan wallTime)
    {
        var items = TruncateContentItems(contentItems, maxTokensOverride);
        items = PrependHeader(items, $"Script running with cell ID {cellId}", wallTime);
        return new KernelCodeModeOperationResult(true, BuildTextPreview(items), items);
    }

    private KernelCodeModeOperationResult CreateFinishedResult(
        string statusText,
        bool success,
        IReadOnlyList<KernelToolOutputContentItem> contentItems,
        int? maxTokensOverride,
        TimeSpan wallTime)
    {
        var items = TruncateContentItems(contentItems, maxTokensOverride);
        items = PrependHeader(items, statusText, wallTime);
        return new KernelCodeModeOperationResult(success, BuildTextPreview(items), items);
    }

    private KernelCodeModeOperationResult CreateResultResult(
        JsonElement payload,
        IReadOnlyList<KernelToolOutputContentItem> contentItems,
        int? maxTokensOverride,
        TimeSpan wallTime)
    {
        ReplaceStoredValues(payload);
        var errorText = NormalizeMessage(ReadString(payload, "error_text"));
        var maxTokens = maxTokensOverride
            ?? ReadInt(payload, "max_output_tokens_per_exec_call");
        var items = contentItems.ToList();
        var success = string.IsNullOrWhiteSpace(errorText);
        if (!string.IsNullOrWhiteSpace(errorText))
        {
            items.Add(new KernelToolOutputContentItem("input_text", Text: $"Script error:\n{errorText}"));
        }

        return CreateFinishedResult(success ? "Script completed" : "Script failed", success, items, maxTokens, wallTime);
    }

    private void ReplaceStoredValues(JsonElement payload)
    {
        if (!payload.TryGetProperty("stored_values", out var storedValuesElement)
            || storedValuesElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        lock (stateGate)
        {
            storedValues.Clear();
            foreach (var property in storedValuesElement.EnumerateObject())
            {
                storedValues[property.Name] = property.Value.Clone();
            }
        }
    }

    private Dictionary<string, JsonElement> CloneStoredValuesForTransport()
    {
        lock (stateGate)
        {
            return storedValues.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.Clone(),
                StringComparer.Ordinal);
        }
    }

    private static IReadOnlyList<KernelToolOutputContentItem> ParseContentItems(JsonElement payload)
    {
        if (!payload.TryGetProperty("content_items", out var contentItems)
            || contentItems.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<KernelToolOutputContentItem>();
        }

        var items = new List<KernelToolOutputContentItem>();
        foreach (var item in contentItems.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var type = NormalizeMessage(ReadString(item, "type")) ?? "input_text";
            if (string.Equals(type, "input_image", StringComparison.OrdinalIgnoreCase))
            {
                items.Add(new KernelToolOutputContentItem(
                    "input_image",
                    ImageUrl: ReadString(item, "image_url"),
                    Detail: ReadString(item, "detail")));
                continue;
            }

            items.Add(new KernelToolOutputContentItem(
                "input_text",
                Text: ReadString(item, "text") ?? string.Empty));
        }

        return items;
    }

    private static IReadOnlyList<KernelToolOutputContentItem> PrependHeader(
        IReadOnlyList<KernelToolOutputContentItem> contentItems,
        string statusText,
        TimeSpan wallTime)
    {
        var wallTimeSeconds = Math.Round(wallTime.TotalSeconds, 1, MidpointRounding.AwayFromZero);
        var items = new List<KernelToolOutputContentItem>(contentItems.Count + 1)
        {
            new("input_text", Text: $"{statusText}\nWall time {Math.Max(wallTimeSeconds, 0):0.0} seconds\nOutput:\n")
        };
        items.AddRange(contentItems);
        return items;
    }

    private static IReadOnlyList<KernelToolOutputContentItem> TruncateContentItems(
        IReadOnlyList<KernelToolOutputContentItem> items,
        int? maxTokens)
    {
        if (items.Count == 0)
        {
            return items;
        }

        if (items.All(static item => string.Equals(item.Type, "input_text", StringComparison.OrdinalIgnoreCase)))
        {
            return KernelTextTruncator.MergeTextItemsAndTruncateByTokens(items, maxTokens ?? DefaultMaxOutputTokens, out _);
        }

        return KernelTextTruncator.TruncateFunctionOutputItemsByTokens(items, maxTokens ?? DefaultMaxOutputTokens);
    }

    private static string BuildTextPreview(IReadOnlyList<KernelToolOutputContentItem> items)
    {
        var parts = new List<string>();
        foreach (var item in items)
        {
            if (!string.Equals(item.Type, "input_text", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = NormalizeMessage(item.Text);
            if (!string.IsNullOrWhiteSpace(text))
            {
                parts.Add(text!);
            }
        }

        return string.Join("\n", parts);
    }

    private static string BuildSource(string userCode)
        => BridgeSource.Value.Replace("__CODE_MODE_USER_CODE_PLACEHOLDER__", userCode ?? string.Empty, StringComparison.Ordinal);

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.GetRawText();
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var number) => number,
            _ => null,
        };
    }

    private static string? NormalizeMessage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
