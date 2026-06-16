using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed class KernelCommandExecAppHostRuntime
{
    private readonly ConcurrentDictionary<string, KernelTrackedCommandExecSession> trackedCommandExecSessions = new(StringComparer.Ordinal);
    private readonly Func<JsonElement?, int, string, CancellationToken, Task> writeErrorAsync;
    private readonly Func<JsonElement, object, CancellationToken, Task> writeResultAsync;
    private readonly Func<string, object, CancellationToken, Task> writeNotificationAsync;
    private readonly Func<string, string, string, string, string, string?, CancellationToken, Task> emitCommandExecutionStartedNotificationAsync;
    private readonly Func<string, string, string, string, string, string?, string, string?, int?, long?, CancellationToken, Task> emitCommandExecutionCompletedNotificationAsync;

    public KernelCommandExecAppHostRuntime(
        Func<JsonElement?, int, string, CancellationToken, Task> writeErrorAsync,
        Func<JsonElement, object, CancellationToken, Task> writeResultAsync,
        Func<string, object, CancellationToken, Task> writeNotificationAsync,
        Func<string, string, string, string, string, string?, CancellationToken, Task> emitCommandExecutionStartedNotificationAsync,
        Func<string, string, string, string, string, string?, string, string?, int?, long?, CancellationToken, Task> emitCommandExecutionCompletedNotificationAsync)
    {
        this.writeErrorAsync = writeErrorAsync;
        this.writeResultAsync = writeResultAsync;
        this.writeNotificationAsync = writeNotificationAsync;
        this.emitCommandExecutionStartedNotificationAsync = emitCommandExecutionStartedNotificationAsync;
        this.emitCommandExecutionCompletedNotificationAsync = emitCommandExecutionCompletedNotificationAsync;
    }

    public async Task StartTrackedCommandExecAsync(
        JsonElement id,
        string processId,
        IReadOnlyList<string> command,
        string cwd,
        IReadOnlyDictionary<string, string> environment,
        bool tty,
        KernelCommandExecTerminalSize? terminalSize,
        bool streamStdin,
        bool streamStdoutStderr,
        int? timeoutMs,
        bool disableTimeout,
        int? outputBytesCap,
        KernelManagedNetworkExecutionLease managedNetworkLease,
        string threadId,
        string turnId,
        string itemId,
        string commandText,
        CancellationToken cancellationToken)
    {
        if (trackedCommandExecSessions.ContainsKey(processId))
        {
            await managedNetworkLease.DisposeAsync().ConfigureAwait(false);
            await writeErrorAsync(id, -32600, $"duplicate active command/exec process id: {processId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        KernelTrackedCommandExecSession session;
        try
        {
            session = KernelTrackedCommandExecSession.Start(
                processId,
                command,
                cwd,
                environment,
                tty,
                terminalSize,
                streamStdin,
                streamStdoutStderr,
                outputBytesCap,
                managedNetworkLease,
                (pid, stream, deltaBase64, capReached) => writeNotificationAsync(
                    "command/exec/outputDelta",
                    new
                    {
                        processId = pid,
                        stream,
                        deltaBase64,
                        capReached,
                    },
                    CancellationToken.None));
        }
        catch (Exception ex)
        {
            await managedNetworkLease.DisposeAsync().ConfigureAwait(false);
            await writeErrorAsync(id, -32603, $"exec failed: {Normalize(ex.Message) ?? "unknown"}", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!trackedCommandExecSessions.TryAdd(processId, session))
        {
            session.Dispose();
            await writeErrorAsync(id, -32600, $"duplicate active command/exec process id: {processId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            await emitCommandExecutionStartedNotificationAsync(
                threadId,
                turnId,
                itemId,
                commandText,
                cwd,
                processId,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            RemoveTrackedCommandExecSession(processId);
            throw;
        }

        var commandExecutionStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var responseId = id.Clone();
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await session.WaitForCompletionAsync(timeoutMs, disableTimeout, CancellationToken.None).ConfigureAwait(false);
                await emitCommandExecutionCompletedNotificationAsync(
                    threadId,
                    turnId,
                    itemId,
                    commandText,
                    cwd,
                    processId,
                    KernelToolItemLifecycleHelpers.TryGetCommandExecutionStatusFromExitCode(result.ExitCode),
                    KernelToolItemLifecycleHelpers.BuildCommandExecutionAggregatedOutput(result.StdOut, result.StdErr),
                    result.ExitCode,
                    (long)Math.Max(0, commandExecutionStopwatch.Elapsed.TotalMilliseconds),
                    CancellationToken.None).ConfigureAwait(false);
                await writeResultAsync(responseId, new
                {
                    exitCode = result.ExitCode,
                    stdout = result.StdOut,
                    stderr = result.StdErr,
                }, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await emitCommandExecutionCompletedNotificationAsync(
                    threadId,
                    turnId,
                    itemId,
                    commandText,
                    cwd,
                    processId,
                    "failed",
                    null,
                    null,
                    (long)Math.Max(0, commandExecutionStopwatch.Elapsed.TotalMilliseconds),
                    CancellationToken.None).ConfigureAwait(false);
                await writeErrorAsync(responseId, -32603, $"exec failed: {Normalize(ex.Message) ?? "unknown"}", CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                RemoveTrackedCommandExecSession(processId);
            }
        }, CancellationToken.None);
    }

    public async Task HandleCommandExecWriteAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var processId = Normalize(ReadString(@params, "processId"));
        if (string.IsNullOrWhiteSpace(processId))
        {
            await writeErrorAsync(id, -32600, "command/exec/write requires processId", cancellationToken).ConfigureAwait(false);
            return;
        }

        var closeStdin = ReadBool(@params, "closeStdin") ?? false;
        var deltaBase64 = Normalize(ReadString(@params, "deltaBase64"));
        if (deltaBase64 is null && !closeStdin)
        {
            await writeErrorAsync(id, -32602, "command/exec/write requires deltaBase64 or closeStdin", cancellationToken).ConfigureAwait(false);
            return;
        }

        byte[] delta;
        if (deltaBase64 is null)
        {
            delta = Array.Empty<byte>();
        }
        else
        {
            try
            {
                delta = Convert.FromBase64String(deltaBase64);
            }
            catch (FormatException ex)
            {
                await writeErrorAsync(id, -32602, $"invalid deltaBase64: {Normalize(ex.Message) ?? "unknown"}", cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        if (!trackedCommandExecSessions.TryGetValue(processId!, out var session))
        {
            await writeErrorAsync(id, -32600, $"no active command/exec for process id {JsonSerializer.Serialize(processId)}", cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            await session.WriteStdinAsync(delta, closeStdin, cancellationToken).ConfigureAwait(false);
            await writeResultAsync(id, new { }, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            await writeErrorAsync(id, -32600, Normalize(ex.Message) ?? "command/exec/write failed", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await writeErrorAsync(id, -32603, Normalize(ex.Message) ?? "command/exec/write failed", cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task HandleCommandExecTerminateAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var processId = Normalize(ReadString(@params, "processId"));
        if (string.IsNullOrWhiteSpace(processId))
        {
            await writeErrorAsync(id, -32600, "command/exec/terminate requires processId", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!trackedCommandExecSessions.TryGetValue(processId!, out var session))
        {
            await writeErrorAsync(id, -32600, $"no active command/exec for process id {JsonSerializer.Serialize(processId)}", cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            session.RequestTerminate();
            await writeResultAsync(id, new { }, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            await writeErrorAsync(id, -32600, Normalize(ex.Message) ?? "command/exec/terminate failed", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await writeErrorAsync(id, -32603, Normalize(ex.Message) ?? "command/exec/terminate failed", cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task HandleCommandExecResizeAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var processId = Normalize(ReadString(@params, "processId"));
        if (string.IsNullOrWhiteSpace(processId))
        {
            await writeErrorAsync(id, -32600, "command/exec/resize requires processId", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!trackedCommandExecSessions.TryGetValue(processId!, out var session))
        {
            await writeErrorAsync(id, -32600, $"no active command/exec for process id {JsonSerializer.Serialize(processId)}", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!@params.TryGetProperty("size", out var sizeElement)
            || sizeElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            await writeErrorAsync(id, -32602, "command/exec/resize requires size", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!KernelCommandExecRequestHelpers.TryReadCommandExecTerminalSize(sizeElement, out var size, out var sizeError))
        {
            await writeErrorAsync(id, -32602, sizeError ?? "command/exec size is invalid", cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            session.Resize(size!.Value.Rows, size.Value.Cols);
            await writeResultAsync(id, new { }, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            await writeErrorAsync(id, -32600, Normalize(ex.Message) ?? "command/exec/resize failed", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await writeErrorAsync(id, -32603, Normalize(ex.Message) ?? "command/exec/resize failed", cancellationToken).ConfigureAwait(false);
        }
    }

    public void DisposeTrackedCommandExecSessions()
    {
        foreach (var processId in trackedCommandExecSessions.Keys.ToArray())
        {
            RemoveTrackedCommandExecSession(processId);
        }
    }

    public static KernelCommandRunResult ApplyCommandExecOutputCap(KernelCommandRunResult result, int? outputBytesCap, bool disableOutputCap)
    {
        if (disableOutputCap || outputBytesCap is null)
        {
            return result;
        }

        return result with
        {
            StdOut = TruncateCommandExecOutput(result.StdOut, outputBytesCap.Value),
            StdErr = TruncateCommandExecOutput(result.StdErr, outputBytesCap.Value),
        };
    }

    private void RemoveTrackedCommandExecSession(string processId)
    {
        if (trackedCommandExecSessions.TryRemove(processId, out var session))
        {
            session.Dispose();
        }
    }

    private static string TruncateCommandExecOutput(string value, int outputBytesCap)
    {
        if (string.IsNullOrEmpty(value) || outputBytesCap < 0)
        {
            return value;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length <= outputBytesCap)
        {
            return value;
        }

        return Encoding.UTF8.GetString(bytes.AsSpan(0, outputBytesCap));
    }

    private static string? ReadString(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool? ReadBool(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
