using TianShu.AppHost.State;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// Turn terminal state 运行时，负责提交 turn 终态状态、日志、rollout、线程状态与终态通知。
/// Turn terminal state runtime that commits terminal turn state, logs, rollout records, thread status, and terminal notifications.
/// </summary>
internal sealed class KernelTurnTerminalStateRuntime
{
    private readonly KernelThreadStore threadStore;
    private readonly Func<string?, string?> normalize;
    private readonly Func<string, string, string, string, string?, object, CancellationToken, Task> persistTurnLogAsync;
    private readonly Func<string, string, string, string, string?, object, CancellationToken, Task> persistRolloutAsync;
    private readonly Func<KernelThreadRecord, CancellationToken, Task> writeThreadStatusChangedAsync;
    private readonly Func<string, string, TurnRequestContext, string, string?, string?, string?, string?, string, KernelTurnErrorRecord?, bool, Task> persistTurnSessionBeforeTerminalAsync;
    private readonly Func<string, string, string, CancellationToken, bool, Task> resolvePendingInteractiveRequestsForThreadLifecycleAsync;
    private readonly Func<string?, string?, CancellationToken, Task> flushPendingTurnInterruptResponsesAsync;
    private readonly Func<string, object, CancellationToken, Task> writeNotificationAsync;

    public KernelTurnTerminalStateRuntime(
        KernelThreadStore threadStore,
        Func<string?, string?> normalize,
        Func<string, string, string, string, string?, object, CancellationToken, Task> persistTurnLogAsync,
        Func<string, string, string, string, string?, object, CancellationToken, Task> persistRolloutAsync,
        Func<KernelThreadRecord, CancellationToken, Task> writeThreadStatusChangedAsync,
        Func<string, string, TurnRequestContext, string, string?, string?, string?, string?, string, KernelTurnErrorRecord?, bool, Task> persistTurnSessionBeforeTerminalAsync,
        Func<string, string, string, CancellationToken, bool, Task> resolvePendingInteractiveRequestsForThreadLifecycleAsync,
        Func<string?, string?, CancellationToken, Task> flushPendingTurnInterruptResponsesAsync,
        Func<string, object, CancellationToken, Task> writeNotificationAsync)
    {
        this.threadStore = threadStore ?? throw new ArgumentNullException(nameof(threadStore));
        this.normalize = normalize ?? throw new ArgumentNullException(nameof(normalize));
        this.persistTurnLogAsync = persistTurnLogAsync ?? throw new ArgumentNullException(nameof(persistTurnLogAsync));
        this.persistRolloutAsync = persistRolloutAsync ?? throw new ArgumentNullException(nameof(persistRolloutAsync));
        this.writeThreadStatusChangedAsync = writeThreadStatusChangedAsync ?? throw new ArgumentNullException(nameof(writeThreadStatusChangedAsync));
        this.persistTurnSessionBeforeTerminalAsync = persistTurnSessionBeforeTerminalAsync ?? throw new ArgumentNullException(nameof(persistTurnSessionBeforeTerminalAsync));
        this.resolvePendingInteractiveRequestsForThreadLifecycleAsync = resolvePendingInteractiveRequestsForThreadLifecycleAsync ?? throw new ArgumentNullException(nameof(resolvePendingInteractiveRequestsForThreadLifecycleAsync));
        this.flushPendingTurnInterruptResponsesAsync = flushPendingTurnInterruptResponsesAsync ?? throw new ArgumentNullException(nameof(flushPendingTurnInterruptResponsesAsync));
        this.writeNotificationAsync = writeNotificationAsync ?? throw new ArgumentNullException(nameof(writeNotificationAsync));
    }

    public async Task PersistAsync(KernelTurnTerminalStateCommit commit)
    {
        var completedTurnAssistantText = ResolveCompletedTurnAssistantText(commit);
        await threadStore
            .AppendCompletedTurnAsync(
                commit.ThreadId,
                commit.TurnId,
                commit.EffectiveUserText,
                completedTurnAssistantText,
                commit.FinalTurnStatus,
                CancellationToken.None)
            .ConfigureAwait(false);

        await persistTurnLogAsync(
            commit.ThreadId,
            commit.TurnId,
            "turn.completed",
            commit.FinalTurnStatus,
            ResolveTurnLogSummary(commit),
            CreateTurnLogPayload(commit),
            CancellationToken.None).ConfigureAwait(false);

        await persistRolloutAsync(
            commit.ThreadId,
            commit.TurnId,
            "turn",
            threadStore.RolloutRecorder.GetRolloutPath(commit.ThreadId),
            ResolveRolloutSummary(commit),
            CreateRolloutPayload(commit),
            CancellationToken.None).ConfigureAwait(false);
    }

    public async Task PublishAsync(KernelTurnTerminalStateCommit commit)
    {
        var threadStatus = commit.FinalTurnStatus == "failed" ? "systemError" : "idle";
        var updatedThread = await threadStore
            .SetThreadStatusAsync(commit.ThreadId, threadStatus, Array.Empty<string>(), CancellationToken.None)
            .ConfigureAwait(false);
        if (updatedThread is not null)
        {
            await writeThreadStatusChangedAsync(updatedThread, CancellationToken.None).ConfigureAwait(false);
        }

        await PersistTurnSessionBeforeTerminalAsync(commit).ConfigureAwait(false);
        await resolvePendingInteractiveRequestsForThreadLifecycleAsync(
                commit.ThreadId,
                commit.TurnId,
                ResolveThreadLifecycleReason(commit.FinalTurnStatus),
                CancellationToken.None,
                true)
            .ConfigureAwait(false);
        await flushPendingTurnInterruptResponsesAsync(commit.ThreadId, commit.TurnId, CancellationToken.None).ConfigureAwait(false);

        await writeNotificationAsync("turn/completed", new
        {
            threadId = commit.ThreadId,
            turn = new
            {
                id = commit.TurnId,
                status = commit.FinalTurnStatus,
                items = Array.Empty<object>(),
                error = CreateTerminalErrorPayload(commit.FinalTurnError),
            },
        }, CancellationToken.None).ConfigureAwait(false);
    }

    public Task PersistTurnSessionBeforeTerminalAsync(KernelTurnTerminalStateCommit commit)
        => persistTurnSessionBeforeTerminalAsync(
            commit.ThreadId,
            commit.TurnId,
            commit.TurnContext,
            commit.ReviewExitItemId,
            commit.ReviewOutputText,
            commit.ReviewFailureMessage,
            commit.EffectiveUserText,
            commit.FinalAssistantText,
            commit.FinalTurnStatus,
            commit.FinalTurnError,
            commit.PersistExtendedHistory);

    private static string? ResolveCompletedTurnAssistantText(KernelTurnTerminalStateCommit commit)
        => commit.FinalTurnStatus switch
        {
            "completed" => commit.FinalAssistantText,
            "failed" => commit.FinalAssistantText,
            _ => null,
        };

    private string ResolveTurnLogSummary(KernelTurnTerminalStateCommit commit)
        => commit.FinalTurnStatus switch
        {
            "completed" => normalize(commit.FinalAssistantText) ?? string.Empty,
            "interrupted" => normalize(commit.ReviewFailureMessage) ?? "interrupted",
            "failed" => normalize(commit.FinalTurnError?.Message) ?? "failed",
            _ => normalize(commit.FinalAssistantText) ?? commit.FinalTurnStatus,
        };

    private string ResolveRolloutSummary(KernelTurnTerminalStateCommit commit)
        => commit.FinalTurnStatus switch
        {
            "completed" => commit.FinalAssistantText ?? string.Empty,
            "failed" => normalize(commit.FinalTurnError?.Message) ?? commit.EffectiveUserText,
            _ => commit.EffectiveUserText,
        };

    private static object CreateTurnLogPayload(KernelTurnTerminalStateCommit commit)
        => commit.FinalTurnStatus switch
        {
            "completed" => new
            {
                threadId = commit.ThreadId,
                turnId = commit.TurnId,
                userText = commit.EffectiveUserText,
                assistantText = commit.FinalAssistantText,
            },
            "interrupted" => new
            {
                threadId = commit.ThreadId,
                turnId = commit.TurnId,
                userText = commit.EffectiveUserText,
                reason = "interrupted",
            },
            "failed" => new
            {
                threadId = commit.ThreadId,
                turnId = commit.TurnId,
                userText = commit.EffectiveUserText,
                error = commit.FinalTurnError?.Message,
            },
            _ => new
            {
                threadId = commit.ThreadId,
                turnId = commit.TurnId,
                userText = commit.EffectiveUserText,
                status = commit.FinalTurnStatus,
            },
        };

    private static object CreateRolloutPayload(KernelTurnTerminalStateCommit commit)
        => commit.FinalTurnStatus switch
        {
            "completed" => new
            {
                threadId = commit.ThreadId,
                turnId = commit.TurnId,
                status = "completed",
                userText = commit.EffectiveUserText,
                assistantText = commit.FinalAssistantText,
            },
            "failed" => new
            {
                threadId = commit.ThreadId,
                turnId = commit.TurnId,
                status = "failed",
                userText = commit.EffectiveUserText,
                error = commit.FinalTurnError?.Message,
            },
            _ => new
            {
                threadId = commit.ThreadId,
                turnId = commit.TurnId,
                status = commit.FinalTurnStatus,
                userText = commit.EffectiveUserText,
            },
        };

    private static object? CreateTerminalErrorPayload(KernelTurnErrorRecord? error)
        => error is null
            ? null
            : new
            {
                message = error.Message,
                providerErrorInfo = (object?)null,
                additionalDetails = error.AdditionalDetails,
            };

    private static string ResolveThreadLifecycleReason(string finalTurnStatus)
        => finalTurnStatus switch
        {
            "completed" => "turn_completed",
            "interrupted" => "turn_interrupted",
            "failed" => "turn_failed",
            _ => $"turn_{finalTurnStatus}",
        };
}
