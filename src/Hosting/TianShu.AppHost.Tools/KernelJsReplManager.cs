using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TianShu.AppHost.Tools;

internal sealed record KernelJsReplOptions(
    string NodePath,
    string WorkingDirectory,
    IReadOnlyList<string>? NodeModuleDirectories = null);

internal sealed record KernelJsReplToolCall(string RequestId, string ToolName, JsonElement Arguments);

internal sealed record KernelJsReplHostToolResponse(
    bool Success,
    JsonElement? Response = null,
    string? Error = null);

internal sealed class KernelJsReplManager : IAsyncDisposable
{
    private static readonly TimeSpan DefaultExecutionTimeout = TimeSpan.FromSeconds(30);
    private static readonly JsonElement EmptyObject = JsonSerializer.SerializeToElement(new { });
    private static readonly Regex StaticImportRegex = new(
        "^\\s*import\\s+(?!\\()(?:(?:.+?\\s+from\\s+)?[\"\'](?<module>[^\"\']+)[\"\'])",
        RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex BlockedImportRegex = new(
        "\\bimport\\s*(?:\\(\\s*[\"\'](?<dynamic>node:process)[\"\']\\s*\\)|[^;\\r\\n]*?\\bfrom\\s*[\"\'](?<static>node:process)[\"\'])",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private readonly KernelJsReplOptions options;
    private readonly SemaphoreSlim executionGate = new(1, 1);
    private readonly SemaphoreSlim processGate = new(1, 1);
    private readonly SemaphoreSlim stdinWriteGate = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<KernelJsReplWireExecutionResult>> pendingExecutions = new(StringComparer.Ordinal);
    private readonly StringBuilder stderrBuffer = new();
    private readonly string tempDirectory;

    private Process? process;
    private StreamWriter? standardInput;
    private CancellationTokenSource? processLifetimeCts;
    private Task? stdoutLoopTask;
    private Task? stderrLoopTask;
    private Func<KernelJsReplToolCall, CancellationToken, Task<KernelJsReplHostToolResponse>>? currentToolInvoker;
    private CancellationToken currentToolInvokerCancellationToken;

    public KernelJsReplManager(KernelJsReplOptions options)
    {
        this.options = options;
        tempDirectory = Path.Combine(Path.GetTempPath(), "tianshu-js-repl", Guid.NewGuid().ToString("N"));
    }

    public async Task<KernelJsReplExecutionResult> ExecuteAsync(
        KernelJsReplExecutionRequest request,
        Func<KernelJsReplToolCall, CancellationToken, Task<KernelJsReplHostToolResponse>> toolInvoker,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateSource(request.Code);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            return new KernelJsReplExecutionResult(false, validationError!, Array.Empty<KernelToolOutputContentItem>());
        }

        await executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureProcessAsync(cancellationToken).ConfigureAwait(false);

            var executionId = Guid.NewGuid().ToString("N");
            var completion = new TaskCompletionSource<KernelJsReplWireExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!pendingExecutions.TryAdd(executionId, completion))
            {
                throw new InvalidOperationException("无法创建 js_repl 执行槽位。");
            }

            currentToolInvoker = toolInvoker;
            currentToolInvokerCancellationToken = cancellationToken;

            try
            {
                await SendMessageAsync(new Dictionary<string, object?>
                {
                    ["type"] = "execute",
                    ["id"] = executionId,
                    ["code"] = request.Code,
                }, cancellationToken).ConfigureAwait(false);

                var timeout = request.TimeoutMs is > 0
                    ? TimeSpan.FromMilliseconds(request.TimeoutMs.Value)
                    : DefaultExecutionTimeout;
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(timeout);

                KernelJsReplWireExecutionResult wireResult;
                try
                {
                    wireResult = await completion.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    await RestartProcessAsync().ConfigureAwait(false);
                    return new KernelJsReplExecutionResult(
                        false,
                        $"js_repl timed out after {(long)Math.Max(1, timeout.TotalMilliseconds)} ms",
                        Array.Empty<KernelToolOutputContentItem>());
                }

                var contentItems = (wireResult.ContentItems ?? Array.Empty<KernelJsReplWireContentItem>())
                    .Select(MapContentItem)
                    .Where(static item => item is not null)
                    .Cast<KernelToolOutputContentItem>()
                    .ToArray();
                var output = NormalizeOutput(wireResult.Output, wireResult.Error);

                return new KernelJsReplExecutionResult(wireResult.Success, output, contentItems);
            }
            finally
            {
                pendingExecutions.TryRemove(executionId, out _);
                currentToolInvoker = null;
                currentToolInvokerCancellationToken = default;
            }
        }
        finally
        {
            executionGate.Release();
        }
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RestartProcessAsync().ConfigureAwait(false);
        }
        finally
        {
            executionGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await RestartProcessAsync().ConfigureAwait(false);
        executionGate.Dispose();
        processGate.Dispose();
        stdinWriteGate.Dispose();
        try
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
        catch
        {
            // js_repl 缓存目录清理失败不应影响宿主退出。
        }
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

            Directory.CreateDirectory(tempDirectory);
            stderrBuffer.Clear();

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
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            startInfo.ArgumentList.Add("-e");
            startInfo.ArgumentList.Add(NodeKernelScript);
            startInfo.Environment["TIANSHU_JS_REPL_TMPDIR"] = tempDirectory;

            var moduleDirs = ResolveNodeModuleDirectories();
            if (moduleDirs.Count > 0)
            {
                var existingNodePath = Environment.GetEnvironmentVariable("NODE_PATH");
                var merged = string.Join(Path.PathSeparator, moduleDirs.Concat(
                    string.IsNullOrWhiteSpace(existingNodePath)
                        ? Array.Empty<string>()
                        : existingNodePath!.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
                startInfo.Environment["NODE_PATH"] = merged;
            }

            var startedProcess = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true,
            };
            if (!startedProcess.Start())
            {
                throw new InvalidOperationException("无法启动 js_repl Node 进程。");
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

    private IReadOnlyList<string> ResolveNodeModuleDirectories()
    {
        var fromOptions = options.NodeModuleDirectories ?? Array.Empty<string>();
        var fromEnvironment = (Environment.GetEnvironmentVariable("TIANSHU_JS_REPL_NODE_MODULE_DIRS") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return fromOptions
            .Concat(fromEnvironment)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
                // 忽略终止失败，后续仍尝试等待退出。
            }

            try
            {
                await processToStop.WaitForExitAsync().ConfigureAwait(false);
            }
            catch
            {
                // 进程结束等待失败时，不再阻塞重启链路。
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
                // 读取循环异常已通过 pending execution 反馈给上层。
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
                // 同上。
            }
        }

        FailPendingExecutions(BuildKernelTerminationMessage());
    }

    private async Task SendMessageAsync(object payload, CancellationToken cancellationToken)
    {
        var writer = standardInput ?? throw new InvalidOperationException("js_repl 标准输入尚未初始化。");
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
                if (string.Equals(type, "exec_result", StringComparison.Ordinal))
                {
                    CompleteExecution(root);
                    continue;
                }

                if (string.Equals(type, "tool_call", StringComparison.Ordinal))
                {
                    _ = Task.Run(() => HandleToolCallAsync(root, cancellationToken), CancellationToken.None);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            FailPendingExecutions(NormalizeOutput(null, ex.Message));
        }
        finally
        {
            FailPendingExecutions(BuildKernelTerminationMessage());
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

                lock (stderrBuffer)
                {
                    if (stderrBuffer.Length > 0)
                    {
                        stderrBuffer.AppendLine();
                    }

                    stderrBuffer.Append(line);
                    if (stderrBuffer.Length > 4096)
                    {
                        stderrBuffer.Remove(0, stderrBuffer.Length - 4096);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void CompleteExecution(JsonElement payload)
    {
        var executionId = ReadString(payload, "id");
        if (string.IsNullOrWhiteSpace(executionId)
            || !pendingExecutions.TryGetValue(executionId, out var completion))
        {
            return;
        }

        var success = ReadBoolean(payload, "success") ?? false;
        var output = ReadString(payload, "output");
        var error = ReadString(payload, "error");
        var contentItems = TryReadContentItems(payload, "content_items");

        completion.TrySetResult(new KernelJsReplWireExecutionResult(success, output, error, contentItems));
    }

    private async Task HandleToolCallAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var requestId = ReadString(payload, "request_id");
        var toolName = ReadString(payload, "tool_name");
        var arguments = TryReadJsonProperty(payload, "arguments", out var rawArguments)
            ? rawArguments.Clone()
            : EmptyObject;

        if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(toolName))
        {
            return;
        }

        var toolInvoker = currentToolInvoker;
        if (toolInvoker is null)
        {
            await SendToolResultAsync(requestId!, false, null, "js_repl tool runtime is unavailable", cancellationToken).ConfigureAwait(false);
            return;
        }

        KernelJsReplHostToolResponse response;
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, currentToolInvokerCancellationToken);
            response = await toolInvoker(new KernelJsReplToolCall(requestId!, toolName!, arguments), linkedCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            response = new KernelJsReplHostToolResponse(false, null, NormalizeOutput(null, ex.Message));
        }

        await SendToolResultAsync(requestId!, response.Success, response.Response, response.Error, cancellationToken).ConfigureAwait(false);
    }

    private Task SendToolResultAsync(
        string requestId,
        bool success,
        JsonElement? response,
        string? error,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["type"] = "tool_result",
            ["request_id"] = requestId,
            ["success"] = success,
        };
        if (response is JsonElement responseElement)
        {
            payload["response"] = JsonSerializer.Deserialize<object>(responseElement.GetRawText());
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            payload["error"] = error;
        }

        return SendMessageAsync(payload, cancellationToken);
    }

    private void FailPendingExecutions(string message)
    {
        foreach (var pending in pendingExecutions.ToArray())
        {
            pending.Value.TrySetResult(new KernelJsReplWireExecutionResult(false, message, message, Array.Empty<KernelJsReplWireContentItem>()));
            pendingExecutions.TryRemove(pending.Key, out _);
        }
    }

    private string BuildKernelTerminationMessage()
    {
        lock (stderrBuffer)
        {
            var stderr = stderrBuffer.ToString();
            return NormalizeOutput(null, stderr) switch
            {
                { Length: > 0 } text => $"js_repl kernel terminated unexpectedly: {text}",
                _ => "js_repl kernel terminated unexpectedly",
            };
        }
    }

    private static KernelToolOutputContentItem? MapContentItem(KernelJsReplWireContentItem item)
    {
        var type = KernelToolJsonHelpers.Normalize(item.Type) ?? "input_text";
        return type switch
        {
            "input_text" => new KernelToolOutputContentItem("input_text", Text: item.Text ?? string.Empty),
            "input_image" => new KernelToolOutputContentItem("input_image", ImageUrl: item.ImageUrl ?? string.Empty, Detail: item.Detail),
            _ => null,
        };
    }

    internal static string? ValidateSource(string code)
    {
        var trimmed = code.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return "js_repl expects raw JavaScript source, not markdown code fences. Resend plain JS only (optional first line `// tianshu-js-repl: ...`).";
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (document.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.String)
            {
                return "js_repl is a freeform tool and expects raw JavaScript source. Resend plain JS only (optional first line `// tianshu-js-repl: ...`); do not send JSON (`{\"code\":...}`), quoted code, or markdown fences.";
            }
        }
        catch (JsonException)
        {
            // 非 JSON 输入，继续走 JS 校验。
        }

        var staticImport = StaticImportRegex.Match(code);
        if (staticImport.Success)
        {
            var moduleName = staticImport.Groups["module"].Value;
            if (string.IsNullOrWhiteSpace(moduleName))
            {
                moduleName = "<unknown>";
            }

            return $"Top-level static import \"{moduleName}\" is not supported in js_repl";
        }

        var blockedImport = BlockedImportRegex.Match(code);
        if (blockedImport.Success)
        {
            var moduleName = blockedImport.Groups["dynamic"].Value;
            if (string.IsNullOrWhiteSpace(moduleName))
            {
                moduleName = blockedImport.Groups["static"].Value;
            }

            if (!string.IsNullOrWhiteSpace(moduleName))
            {
                return $"Importing module \"{moduleName}\" is not allowed in js_repl";
            }
        }

        return null;
    }

    private static string NormalizeOutput(string? output, string? error)
    {
        var normalizedOutput = KernelToolJsonHelpers.Normalize(output);
        var normalizedError = KernelToolJsonHelpers.Normalize(error);
        if (string.IsNullOrWhiteSpace(normalizedOutput))
        {
            return normalizedError ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(normalizedError)
            || string.Equals(normalizedOutput, normalizedError, StringComparison.Ordinal))
        {
            return normalizedOutput!;
        }

        return normalizedOutput!.Contains(normalizedError, StringComparison.Ordinal)
            ? normalizedOutput
            : $"{normalizedOutput}{Environment.NewLine}{normalizedError}";
    }

    private static bool TryReadJsonProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static KernelJsReplWireContentItem[]? TryReadContentItems(JsonElement element, string propertyName)
    {
        if (!TryReadJsonProperty(element, propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var items = new List<KernelJsReplWireContentItem>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            items.Add(new KernelJsReplWireContentItem(
                Type: ReadString(item, "type"),
                Text: ReadString(item, "text"),
                ImageUrl: ReadString(item, "image_url"),
                Detail: ReadString(item, "detail")));
        }

        return items.ToArray();
    }

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

    private static bool? ReadBoolean(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private sealed record KernelJsReplWireExecutionResult(
        bool Success,
        string? Output,
        string? Error,
        IReadOnlyList<KernelJsReplWireContentItem>? ContentItems);

    private sealed record KernelJsReplWireContentItem(
        string? Type,
        string? Text,
        string? ImageUrl,
        string? Detail);

    private const string NodeKernelScript = """
const repl = require("node:repl");
const readline = require("node:readline");
const { PassThrough, Writable } = require("node:stream");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const util = require("node:util");
const hostProcess = globalThis.process;

const input = new PassThrough();
let replBuffer = "";
const output = new Writable({
  write(chunk, encoding, callback) {
    replBuffer += chunk.toString();
    callback();
  },
});

let currentExec = null;
let toolSequence = 0;
const pendingToolCalls = new Map();
const tempDirFromEnv = hostProcess.env.TIANSHU_JS_REPL_TMPDIR;
const rl = readline.createInterface({ input: hostProcess.stdin, crlfDelay: Infinity });
let server = createServer();

function createServer() {
  const replServer = repl.start({
    prompt: "",
    terminal: false,
    useGlobal: false,
    ignoreUndefined: false,
    input,
    output,
  });

  const tmpDir = tempDirFromEnv && tempDirFromEnv.length > 0
    ? tempDirFromEnv
    : fs.mkdtempSync(path.join(os.tmpdir(), "tianshu-js-repl-"));
  fs.mkdirSync(tmpDir, { recursive: true });

  replServer.context.process = undefined;
  replServer.context.require = undefined;
  replServer.context.module = undefined;
  replServer.context.exports = undefined;
  replServer.context.__dirname = undefined;
  replServer.context.__filename = undefined;
  replServer.context.console = createConsole();
  replServer.context.tianshu = {
    tmpDir,
    tool: async (name, args) => {
      if (!currentExec) {
        throw new Error("tianshu.tool is only available during js_repl execution");
      }

      const requestId = `${currentExec.id}:tool:${++toolSequence}`;
      return await requestTool(requestId, name, args ?? {});
    },
    emitImage: async (value) => {
      if (!currentExec) {
        throw new Error("tianshu.emitImage is only available during js_repl execution");
      }

      const items = normalizeImageItems(value);
      currentExec.contentItems.push(...items);
      return items.length;
    },
  };

  return replServer;
}

function createConsole() {
  const append = (...args) => {
    if (!currentExec) {
      return;
    }

    currentExec.logs.push(util.formatWithOptions({ colors: false, depth: 6 }, ...args));
  };

  return {
    log: append,
    info: append,
    debug: append,
    warn: append,
    error: append,
  };
}

function writeMessage(message) {
  hostProcess.stdout.write(JSON.stringify(message) + "\n");
}

function requestTool(requestId, toolName, argumentsValue) {
  writeMessage({
    type: "tool_call",
    request_id: requestId,
    tool_name: toolName,
    arguments: argumentsValue,
  });

  return new Promise((resolve, reject) => {
    pendingToolCalls.set(requestId, { resolve, reject });
  });
}

function normalizeImageItems(value) {
  const source = value && typeof value === "object" && !Array.isArray(value) && Object.prototype.hasOwnProperty.call(value, "output")
    ? value.output
    : value;
  const candidates = Array.isArray(source) ? source : [source];
  const items = [];

  for (const candidate of candidates) {
    if (candidate == null) {
      continue;
    }

    if (typeof candidate === "string") {
      if (!candidate.startsWith("data:image/")) {
        throw new Error("tianshu.emitImage only accepts data URLs");
      }

      items.push({ type: "input_image", image_url: candidate });
      continue;
    }

    if (typeof candidate === "object" && candidate.type === "input_image") {
      if (typeof candidate.image_url !== "string" || !candidate.image_url.startsWith("data:image/")) {
        throw new Error("tianshu.emitImage only accepts data URLs");
      }

      items.push({
        type: "input_image",
        image_url: candidate.image_url,
        detail: candidate.detail ?? null,
      });
      continue;
    }
  }

  if (items.length === 0) {
    throw new Error("tianshu.emitImage expected input_image content items or data URLs");
  }

  return items;
}

function finalizeCurrentExec(success, output, error) {
  const contentItems = currentExec ? currentExec.contentItems : [];
  const payload = {
    type: "exec_result",
    id: currentExec ? currentExec.id : "unknown",
    success,
    output,
    content_items: contentItems,
  };

  if (error) {
    payload.error = error;
  }

  writeMessage(payload);
  currentExec = null;
  replBuffer = "";
}

function waitForEval(code) {
  return new Promise((resolve) => {
    let settled = false;
    let poll = null;

    const finish = (result) => {
      if (settled) {
        return;
      }

      settled = true;
      if (poll) {
        clearInterval(poll);
      }
      resolve(result);
    };

    replBuffer = "";
    server.eval(code, server.context, "tianshu-js-repl", (error, value) => {
      if (error) {
        finish({ success: false, error: String(error && error.message ? error.message : error) });
        return;
      }

      finish({ success: true, value });
    });

    poll = setInterval(() => {
      const text = replBuffer.trim();
      if (!text) {
        return;
      }

      if (/^(Uncaught|SyntaxError|ReferenceError|TypeError|RangeError|Error:)/m.test(text)) {
        finish({ success: false, error: text });
      }
    }, 15);
  });
}

async function execute(message) {
  currentExec = {
    id: message.id,
    logs: [],
    contentItems: [],
  };

  try {
    const result = await waitForEval(String(message.code ?? ""));
    let outputText = currentExec.logs.join("\n");

    if (result.success) {
      if (result.value !== undefined) {
        const inspected = typeof result.value === "string"
          ? result.value
          : util.inspect(result.value, { colors: false, depth: 6, maxArrayLength: 50, breakLength: Infinity });
        if (inspected) {
          outputText = outputText ? `${outputText}\n${inspected}` : inspected;
        }
      }

      finalizeCurrentExec(true, outputText, null);
      return;
    }

    const errorText = String(result.error ?? "js_repl execution failed");
    outputText = outputText ? `${outputText}\n${errorText}` : errorText;
    finalizeCurrentExec(false, outputText, errorText);
  } catch (error) {
    const errorText = String(error && error.message ? error.message : error);
    const outputText = currentExec && currentExec.logs.length > 0
      ? `${currentExec.logs.join("\n")}\n${errorText}`
      : errorText;
    finalizeCurrentExec(false, outputText, errorText);
  }
}

rl.on("line", async (line) => {
  if (!line) {
    return;
  }

  let message;
  try {
    message = JSON.parse(line);
  } catch {
    return;
  }

  switch (message.type) {
    case "execute": {
      await execute(message);
      break;
    }
    case "tool_result": {
      const pending = pendingToolCalls.get(message.request_id);
      if (!pending) {
        break;
      }

      pendingToolCalls.delete(message.request_id);
      if (message.success) {
        pending.resolve(message.response);
      } else {
        pending.reject(new Error(String(message.error ?? "tool call failed")));
      }
      break;
    }
    default:
      break;
  }
});
""";
}

