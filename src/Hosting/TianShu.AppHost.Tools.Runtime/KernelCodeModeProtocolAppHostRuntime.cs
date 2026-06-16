using System.Text.Json;
using TianShu.AppHost.State;
using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed class KernelCodeModeProtocolAppHostRuntime
{
    private readonly KernelThreadStore threadStore;
    private readonly KernelThreadManager threadManager;
    private readonly Func<KernelThreadRecord, KernelThreadSessionState> buildDefaultThreadSession;
    private readonly Func<string> nextTurnId;
    private readonly Func<JsonElement?, int, string, CancellationToken, Task> writeErrorAsync;
    private readonly Func<JsonElement, object, CancellationToken, Task> writeResultAsync;
    private readonly Func<string, KernelThreadSessionState, TurnRequestContext> buildTurnRequestContext;
    private readonly Func<string, string, TurnRequestContext, KernelCodeModeExecutionRequest, CancellationToken, Task<KernelCodeModeOperationResult>> executeCodeModeFromToolAsync;
    private readonly Func<string, string, TurnRequestContext, KernelCodeModeWaitRequest, CancellationToken, Task<KernelCodeModeOperationResult>> waitOnCodeModeFromToolAsync;
    private readonly Action<string> deactivateCodeModeTurn;

    public KernelCodeModeProtocolAppHostRuntime(
        KernelThreadStore threadStore,
        KernelThreadManager threadManager,
        Func<KernelThreadRecord, KernelThreadSessionState> buildDefaultThreadSession,
        Func<string> nextTurnId,
        Func<JsonElement?, int, string, CancellationToken, Task> writeErrorAsync,
        Func<JsonElement, object, CancellationToken, Task> writeResultAsync,
        Func<string, KernelThreadSessionState, TurnRequestContext> buildTurnRequestContext,
        Func<string, string, TurnRequestContext, KernelCodeModeExecutionRequest, CancellationToken, Task<KernelCodeModeOperationResult>> executeCodeModeFromToolAsync,
        Func<string, string, TurnRequestContext, KernelCodeModeWaitRequest, CancellationToken, Task<KernelCodeModeOperationResult>> waitOnCodeModeFromToolAsync,
        Action<string> deactivateCodeModeTurn)
    {
        this.threadStore = threadStore;
        this.threadManager = threadManager;
        this.buildDefaultThreadSession = buildDefaultThreadSession;
        this.nextTurnId = nextTurnId;
        this.writeErrorAsync = writeErrorAsync;
        this.writeResultAsync = writeResultAsync;
        this.buildTurnRequestContext = buildTurnRequestContext;
        this.executeCodeModeFromToolAsync = executeCodeModeFromToolAsync;
        this.waitOnCodeModeFromToolAsync = waitOnCodeModeFromToolAsync;
        this.deactivateCodeModeTurn = deactivateCodeModeTurn;
    }

    public async Task HandleCodeModeExecAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var threadId = Normalize(ReadString(@params, "threadId") ?? ReadString(@params, "thread_id"));
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await writeErrorAsync(id, -32602, "threadId 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var input = ReadString(@params, "input") ?? ReadString(@params, "source") ?? ReadString(@params, "code");
        var parseResult = KernelCodeModeRuntimeSupport.ParseExecFreeformInput(input ?? string.Empty);
        if (!parseResult.Success || parseResult.Request is null)
        {
            await writeErrorAsync(id, -32602, parseResult.Error ?? "exec 输入无效。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var thread = await threadStore.GetThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        if (thread is null)
        {
            await writeErrorAsync(id, -32004, $"线程不存在：{threadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        var runtimeThread = threadManager.GetOrAttachThread(thread, buildDefaultThreadSession, loaded: true);
        var turnId = Normalize(ReadString(@params, "turnId") ?? ReadString(@params, "turn_id")) ?? nextTurnId();
        runtimeThread.SetActiveTurn(turnId);
        try
        {
            var result = await executeCodeModeFromToolAsync(
                    threadId!,
                    turnId,
                    buildTurnRequestContext(threadId!, runtimeThread.Session),
                    parseResult.Request,
                    cancellationToken)
                .ConfigureAwait(false);

            await writeResultAsync(
                    id,
                    KernelCodeModeProtocolHelpers.BuildCodeModeProtocolPayload(threadId!, turnId, result, fallbackCellId: null),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            runtimeThread.ClearActiveTurn(turnId);
            deactivateCodeModeTurn(turnId);
        }
    }

    public async Task HandleCodeModeWaitAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var threadId = Normalize(ReadString(@params, "threadId") ?? ReadString(@params, "thread_id"));
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await writeErrorAsync(id, -32602, "threadId 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var cellId = Normalize(ReadString(@params, "cellId") ?? ReadString(@params, "cell_id"));
        if (string.IsNullOrWhiteSpace(cellId))
        {
            await writeErrorAsync(id, -32602, "cellId 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var thread = await threadStore.GetThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        if (thread is null)
        {
            await writeErrorAsync(id, -32004, $"线程不存在：{threadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        var runtimeThread = threadManager.GetOrAttachThread(thread, buildDefaultThreadSession, loaded: true);
        var turnId = Normalize(ReadString(@params, "turnId") ?? ReadString(@params, "turn_id")) ?? nextTurnId();
        var request = new KernelCodeModeWaitRequest(
            cellId!,
            ReadInt(@params, "yieldTimeMs") ?? ReadInt(@params, "yield_time_ms") ?? KernelCodeModeManager.DefaultWaitYieldTimeMs,
            ReadInt(@params, "maxTokens") ?? ReadInt(@params, "max_tokens"),
            ReadBool(@params, "terminate") ?? false);

        runtimeThread.SetActiveTurn(turnId);
        try
        {
            var result = await waitOnCodeModeFromToolAsync(
                    threadId!,
                    turnId,
                    buildTurnRequestContext(threadId!, runtimeThread.Session),
                    request,
                    cancellationToken)
                .ConfigureAwait(false);

            await writeResultAsync(
                    id,
                    KernelCodeModeProtocolHelpers.BuildCodeModeProtocolPayload(threadId!, turnId, result, cellId),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            runtimeThread.ClearActiveTurn(turnId);
            deactivateCodeModeTurn(turnId);
        }
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result))
        {
            return result;
        }

        return null;
    }

    private static bool? ReadBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }
}
