using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using TianShu.AppHost.State;
using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed class KernelUserShellAppHostRuntime
{
    private const int UserShellCommandTimeoutMs = 60 * 60 * 1000;
    private const string UserShellRunMethodName = "tianshu/userShell/run";

    private readonly JsonSerializerOptions strictInputJsonOptions;
    private readonly KernelThreadStore threadStore;
    private readonly KernelThreadManager threadManager;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> runningTurns;
    private readonly Func<string> nextTurnId;
    private readonly Func<JsonElement?, int, string, CancellationToken, Task> writeErrorAsync;
    private readonly Func<JsonElement, object, CancellationToken, Task> writeResultAsync;
    private readonly Func<KernelThreadRecord, KernelThreadSessionState> buildDefaultThreadSession;
    private readonly Func<KernelThreadRecord, CancellationToken, Task> writeThreadStatusChangedAsync;
    private readonly Func<string, object, CancellationToken, Task> writeNotificationAsync;
    private readonly Func<string, string, CancellationToken, Task> flushPendingTurnInterruptResponsesAsync;
    private readonly Func<IReadOnlyList<string>, string?, int?, IReadOnlyDictionary<string, string>?, CancellationToken, Task<KernelCommandRunResult>> executeCommandAsync;
    private readonly Func<string, string, string, string, string, string?, CancellationToken, Task> emitCommandExecutionStartedNotificationAsync;
    private readonly Func<string, string, string, string, string, string?, string, string?, int?, long?, CancellationToken, Task> emitCommandExecutionCompletedNotificationAsync;
    private readonly Action<string, string, string, string> overrideTrackedTurnItemRecordType;
    private readonly Func<string, string, KernelTrackedTurnHistory?> finalizeTrackedTurnHistory;
    private readonly Func<string, CancellationToken, Task<bool>> isEphemeralThreadAsync;

    public KernelUserShellAppHostRuntime(
        JsonSerializerOptions strictInputJsonOptions,
        KernelThreadStore threadStore,
        KernelThreadManager threadManager,
        ConcurrentDictionary<string, CancellationTokenSource> runningTurns,
        Func<string> nextTurnId,
        Func<JsonElement?, int, string, CancellationToken, Task> writeErrorAsync,
        Func<JsonElement, object, CancellationToken, Task> writeResultAsync,
        Func<KernelThreadRecord, KernelThreadSessionState> buildDefaultThreadSession,
        Func<KernelThreadRecord, CancellationToken, Task> writeThreadStatusChangedAsync,
        Func<string, object, CancellationToken, Task> writeNotificationAsync,
        Func<string, string, CancellationToken, Task> flushPendingTurnInterruptResponsesAsync,
        Func<IReadOnlyList<string>, string?, int?, IReadOnlyDictionary<string, string>?, CancellationToken, Task<KernelCommandRunResult>> executeCommandAsync,
        Func<string, string, string, string, string, string?, CancellationToken, Task> emitCommandExecutionStartedNotificationAsync,
        Func<string, string, string, string, string, string?, string, string?, int?, long?, CancellationToken, Task> emitCommandExecutionCompletedNotificationAsync,
        Action<string, string, string, string> overrideTrackedTurnItemRecordType,
        Func<string, string, KernelTrackedTurnHistory?> finalizeTrackedTurnHistory,
        Func<string, CancellationToken, Task<bool>> isEphemeralThreadAsync)
    {
        this.strictInputJsonOptions = strictInputJsonOptions;
        this.threadStore = threadStore;
        this.threadManager = threadManager;
        this.runningTurns = runningTurns;
        this.nextTurnId = nextTurnId;
        this.writeErrorAsync = writeErrorAsync;
        this.writeResultAsync = writeResultAsync;
        this.buildDefaultThreadSession = buildDefaultThreadSession;
        this.writeThreadStatusChangedAsync = writeThreadStatusChangedAsync;
        this.writeNotificationAsync = writeNotificationAsync;
        this.flushPendingTurnInterruptResponsesAsync = flushPendingTurnInterruptResponsesAsync;
        this.executeCommandAsync = executeCommandAsync;
        this.emitCommandExecutionStartedNotificationAsync = emitCommandExecutionStartedNotificationAsync;
        this.emitCommandExecutionCompletedNotificationAsync = emitCommandExecutionCompletedNotificationAsync;
        this.overrideTrackedTurnItemRecordType = overrideTrackedTurnItemRecordType;
        this.finalizeTrackedTurnHistory = finalizeTrackedTurnHistory;
        this.isEphemeralThreadAsync = isEphemeralThreadAsync;
    }

    public async Task HandleUserShellRunAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var request = await TryDeserializeStrictParamsAsync<KernelUserShellRunRequest>(
            id,
            @params,
            UserShellRunMethodName,
            cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return;
        }

        var threadId = Normalize(request.ThreadId);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await writeErrorAsync(id, -32602, "threadId 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var commandText = Normalize(request.Command);
        if (string.IsNullOrWhiteSpace(commandText))
        {
            await writeErrorAsync(id, -32602, "command 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var record = await threadStore.GetThreadAsync(threadId!, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            await writeErrorAsync(id, -32004, $"线程不存在：{threadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (record.IsArchived)
        {
            await writeErrorAsync(id, -32006, $"线程已归档，无法执行 user shell：{threadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        var runtimeThread = threadManager.GetOrAttachThread(record, buildDefaultThreadSession, loaded: true);
        var result = await ExecuteAsync(
            record,
            runtimeThread,
            commandText!,
            cancellationToken).ConfigureAwait(false);

        await writeResultAsync(id, result, cancellationToken).ConfigureAwait(false);
    }

    public async Task<KernelUserShellRunResultPayload> ExecuteAsync(
        KernelThreadRecord record,
        KernelRuntimeThread runtimeThread,
        string commandText,
        CancellationToken cancellationToken)
    {
        var activeTurnId = Normalize(runtimeThread.ActiveTurnId);
        if (!string.IsNullOrWhiteSpace(activeTurnId) && runningTurns.TryGetValue(activeTurnId!, out var activeTurnCancellation))
        {
            var itemId = KernelUserShellRuntimeHelpers.NextItemId();
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                activeTurnCancellation.Token,
                cancellationToken);
            var executionResult = await ExecuteUserShellCommandCoreAsync(
                record.Id,
                activeTurnId!,
                itemId,
                commandText,
                runtimeThread.Session.Cwd,
                runtimeThread.Session.ShellEnvironmentPolicy,
                linkedCancellation.Token).ConfigureAwait(false);
            return KernelUserShellToolHelpers.BuildRunResult(
                activeTurnId!,
                itemId,
                executionResult.TurnStatus,
                executionResult.ItemStatus,
                executionResult.RunResult.ExitCode,
                executionResult.RunResult.StdOut,
                executionResult.RunResult.StdErr,
                reusedActiveTurn: true);
        }

        return await RunStandaloneUserShellTurnAsync(
            record,
            runtimeThread,
            commandText,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<KernelUserShellRunResultPayload> RunStandaloneUserShellTurnAsync(
        KernelThreadRecord record,
        KernelRuntimeThread runtimeThread,
        string commandText,
        CancellationToken cancellationToken)
    {
        var turnId = nextTurnId();
        var itemId = KernelUserShellRuntimeHelpers.NextItemId();
        using var turnCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        runningTurns[turnId] = turnCancellation;
        runtimeThread.SetActiveTurn(turnId);

        try
        {
            var activeRecord = await threadStore
                .SetThreadStatusAsync(record.Id, "active", Array.Empty<string>(), cancellationToken)
                .ConfigureAwait(false);
            if (activeRecord is not null)
            {
                runtimeThread.Update(activeRecord, runtimeThread.Session, loaded: true);
                await writeThreadStatusChangedAsync(activeRecord, cancellationToken).ConfigureAwait(false);
                if (!runtimeThread.Session.Ephemeral)
                {
                    await threadStore.RolloutRecorder
                        .EnsureSessionMetaAsync(
                            record.Id,
                            KernelRolloutStateMapper.ToRolloutThreadRecord(activeRecord, runtimeThread.ConfigSnapshot),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            await writeNotificationAsync("turn/started", new
            {
                threadId = record.Id,
                turn = new
                {
                    id = turnId,
                    status = "inProgress",
                    items = Array.Empty<object>(),
                },
            }, cancellationToken).ConfigureAwait(false);

            var executionResult = await ExecuteUserShellCommandCoreAsync(
                record.Id,
                turnId,
                itemId,
                commandText,
                runtimeThread.Session.Cwd,
                runtimeThread.Session.ShellEnvironmentPolicy,
                turnCancellation.Token).ConfigureAwait(false);

            await PersistStandaloneUserShellTurnAsync(
                record.Id,
                turnId,
                executionResult.TurnStatus,
                executionResult.TurnError).ConfigureAwait(false);

            var idleRecord = await threadStore
                .SetThreadStatusAsync(record.Id, "idle", Array.Empty<string>(), CancellationToken.None)
                .ConfigureAwait(false);
            if (idleRecord is not null)
            {
                runtimeThread.Update(idleRecord, runtimeThread.Session, loaded: true);
                await writeThreadStatusChangedAsync(idleRecord, CancellationToken.None).ConfigureAwait(false);
            }

            await flushPendingTurnInterruptResponsesAsync(record.Id, turnId, CancellationToken.None).ConfigureAwait(false);

            await writeNotificationAsync("turn/completed", new
            {
                threadId = record.Id,
                turn = new
                {
                    id = turnId,
                    status = executionResult.TurnStatus,
                    items = Array.Empty<object>(),
                    error = executionResult.TurnError is null
                        ? null
                        : new
                        {
                            message = executionResult.TurnError.Message,
                            additionalDetails = executionResult.TurnError.AdditionalDetails,
                        },
                },
            }, CancellationToken.None).ConfigureAwait(false);

            return KernelUserShellToolHelpers.BuildRunResult(
                turnId,
                itemId,
                executionResult.TurnStatus,
                executionResult.ItemStatus,
                executionResult.RunResult.ExitCode,
                executionResult.RunResult.StdOut,
                executionResult.RunResult.StdErr,
                reusedActiveTurn: false);
        }
        finally
        {
            if (runningTurns.TryRemove(turnId, out var runningTurn))
            {
                runningTurn.Dispose();
            }

            _ = runtimeThread.ClearActiveTurn(turnId);
        }
    }

    private async Task<KernelUserShellExecutionResult> ExecuteUserShellCommandCoreAsync(
        string threadId,
        string turnId,
        string itemId,
        string commandText,
        string cwd,
        KernelShellEnvironmentPolicy? shellEnvironmentPolicy,
        CancellationToken cancellationToken)
    {
        var commandArgs = KernelShellCommandBuilder.BuildDefaultCommand(commandText, useLoginShell: true);
        var environment = KernelShellEnvironmentBuilder.CreateEnvironment(shellEnvironmentPolicy, threadId);
        var stopwatch = Stopwatch.StartNew();

        await emitCommandExecutionStartedNotificationAsync(
            threadId,
            turnId,
            itemId,
            commandText,
            cwd,
            null,
            CancellationToken.None).ConfigureAwait(false);
        overrideTrackedTurnItemRecordType(threadId, turnId, itemId, KernelUserShellToolHelpers.UserShellCommandRecordType);

        try
        {
            var runResult = await executeCommandAsync(
                commandArgs,
                cwd,
                UserShellCommandTimeoutMs,
                environment,
                cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(runResult.StdOut))
            {
                await writeNotificationAsync("item/commandExecution/outputDelta", new
                {
                    threadId,
                    turnId,
                    itemId,
                    stream = "stdout",
                    delta = runResult.StdOut,
                }, CancellationToken.None).ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(runResult.StdErr))
            {
                await writeNotificationAsync("item/commandExecution/outputDelta", new
                {
                    threadId,
                    turnId,
                    itemId,
                    stream = "stderr",
                    delta = runResult.StdErr,
                }, CancellationToken.None).ConfigureAwait(false);
            }

            var itemStatus = runResult.ExitCode == 0 && !runResult.TimedOut
                ? "completed"
                : "failed";
            var turnStatus = cancellationToken.IsCancellationRequested
                ? "interrupted"
                : itemStatus == "completed"
                    ? "completed"
                    : "failed";

            await emitCommandExecutionCompletedNotificationAsync(
                threadId,
                turnId,
                itemId,
                commandText,
                cwd,
                null,
                itemStatus,
                KernelToolItemLifecycleHelpers.BuildCommandExecutionAggregatedOutput(runResult.StdOut, runResult.StdErr),
                runResult.ExitCode,
                (long)Math.Max(0, stopwatch.Elapsed.TotalMilliseconds),
                CancellationToken.None).ConfigureAwait(false);
            overrideTrackedTurnItemRecordType(threadId, turnId, itemId, KernelUserShellToolHelpers.UserShellCommandRecordType);

            return new KernelUserShellExecutionResult(
                runResult,
                turnStatus,
                itemStatus,
                TurnError: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            const string abortedMessage = "command aborted by user";
            var interruptedResult = new KernelCommandRunResult(
                ExitCode: -1,
                StdOut: string.Empty,
                StdErr: abortedMessage,
                TimedOut: false);
            await emitCommandExecutionCompletedNotificationAsync(
                threadId,
                turnId,
                itemId,
                commandText,
                cwd,
                null,
                "failed",
                abortedMessage,
                -1,
                (long)Math.Max(0, stopwatch.Elapsed.TotalMilliseconds),
                CancellationToken.None).ConfigureAwait(false);
            overrideTrackedTurnItemRecordType(threadId, turnId, itemId, KernelUserShellToolHelpers.UserShellCommandRecordType);

            return new KernelUserShellExecutionResult(
                interruptedResult,
                TurnStatus: "interrupted",
                ItemStatus: "failed",
                new KernelTurnErrorRecord
                {
                    Message = abortedMessage,
                });
        }
        catch (Exception ex)
        {
            var turnError = new KernelTurnErrorRecord
            {
                Message = ex.Message,
            };
            var failedResult = new KernelCommandRunResult(
                ExitCode: -1,
                StdOut: string.Empty,
                StdErr: ex.Message,
                TimedOut: false);
            await emitCommandExecutionCompletedNotificationAsync(
                threadId,
                turnId,
                itemId,
                commandText,
                cwd,
                null,
                "failed",
                ex.Message,
                -1,
                (long)Math.Max(0, stopwatch.Elapsed.TotalMilliseconds),
                CancellationToken.None).ConfigureAwait(false);
            overrideTrackedTurnItemRecordType(threadId, turnId, itemId, KernelUserShellToolHelpers.UserShellCommandRecordType);

            return new KernelUserShellExecutionResult(
                failedResult,
                TurnStatus: "failed",
                ItemStatus: "failed",
                turnError);
        }
    }

    private async Task PersistStandaloneUserShellTurnAsync(
        string threadId,
        string turnId,
        string turnStatus,
        KernelTurnErrorRecord? turnError)
    {
        var trackedTurnHistory = finalizeTrackedTurnHistory(threadId, turnId);
        if (trackedTurnHistory is null)
        {
            return;
        }

        await threadStore.AppendCompletedTurnAsync(
            threadId,
            turnId,
            userMessage: null,
            assistantMessage: null,
            turnStatus,
            CancellationToken.None,
            items: trackedTurnHistory.Items,
            error: turnError,
            startedAt: trackedTurnHistory.StartedAt,
            completedAt: trackedTurnHistory.CompletedAt).ConfigureAwait(false);

        if (await isEphemeralThreadAsync(threadId, CancellationToken.None).ConfigureAwait(false))
        {
            return;
        }

        await threadStore.RolloutRecorder.AppendTurnResultAsync(
            threadId,
            turnId,
            turnStatus,
            userMessage: null,
            assistantMessage: null,
            CancellationToken.None,
            items: trackedTurnHistory.Items.Select(KernelRolloutStateMapper.ToRolloutTurnItemRecord).ToArray(),
            error: KernelRolloutStateMapper.ToRolloutTurnErrorRecord(turnError),
            startedAt: trackedTurnHistory.StartedAt,
            completedAt: trackedTurnHistory.CompletedAt).ConfigureAwait(false);
        await threadStore.RolloutRecorder.CloseThreadWriterAsync(threadId).ConfigureAwait(false);
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private async Task<T?> TryDeserializeStrictParamsAsync<T>(
        JsonElement id,
        JsonElement @params,
        string methodName,
        CancellationToken cancellationToken)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(@params.GetRawText(), strictInputJsonOptions);
        }
        catch (JsonException ex)
        {
            await writeErrorAsync(id, -32602, $"{methodName} 参数无效：{ex.Message}", cancellationToken).ConfigureAwait(false);
            return default;
        }
        catch (NotSupportedException ex)
        {
            await writeErrorAsync(id, -32602, $"{methodName} 参数无效：{ex.Message}", cancellationToken).ConfigureAwait(false);
            return default;
        }
    }

    private sealed class KernelUserShellRunRequest
    {
        [JsonPropertyName("threadId")]
        public string? ThreadId { get; init; }

        [JsonPropertyName("command")]
        public string? Command { get; init; }
    }

    private sealed record KernelUserShellExecutionResult(
        KernelCommandRunResult RunResult,
        string TurnStatus,
        string ItemStatus,
        KernelTurnErrorRecord? TurnError);
}
