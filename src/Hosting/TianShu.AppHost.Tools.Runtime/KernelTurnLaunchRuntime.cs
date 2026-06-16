using System.Text.Json;
using TianShu.AppHost.State;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// Turn launch 运行时，负责 turn 启动校验、上下文构造、started state 和后台调度。
/// Runtime that owns turn launch validation, context creation, started state, and background scheduling.
/// </summary>
internal sealed class KernelTurnLaunchRuntime
{
    private readonly KernelThreadStore threadStore;
    private readonly KernelThreadManager threadManager;
    private readonly Func<JsonElement, int, string, object?, CancellationToken, Task> writeErrorAsync;
    private readonly Func<JsonElement, object, CancellationToken, Task> writeResultAsync;
    private readonly Func<string> nextTurnId;
    private readonly Func<string?, int> countTextChars;
    private readonly Func<KernelTurnStartRequest, int> countTurnInputTextChars;
    private readonly int maxUserInputTextChars;
    private readonly Func<KernelThreadRecord, KernelThreadSessionState> buildDefaultThreadSession;
    private readonly Func<KernelThreadSessionState, KernelTurnStartRequest, KernelThreadSessionState> applyTurnOverrides;
    private readonly Func<KernelRuntimeThread, KernelThreadSessionState, KernelTurnStartRequest, CancellationToken, Task<TurnRequestContext>> buildTurnRequestContext;
    private readonly Func<KernelThreadSessionState, CancellationToken, Task> updateMcpSandboxStateAsync;
    private readonly Func<KernelTurnStartRequest, string> extractUserText;
    private readonly Action<string, string, string?, IReadOnlyList<KernelTurnInputItem>?> seedTrackedTurnUserMessage;
    private readonly KernelTurnBackgroundSchedulerRuntime backgroundTurnSchedulerRuntime;
    private readonly KernelTurnStartStateRuntime turnStartStateRuntime;
    private readonly Func<string, string, string, TurnRequestContext, bool, CancellationTokenSource, Task> runTurnAsync;

    public KernelTurnLaunchRuntime(
        KernelThreadStore threadStore,
        KernelThreadManager threadManager,
        Func<JsonElement, int, string, object?, CancellationToken, Task> writeErrorAsync,
        Func<JsonElement, object, CancellationToken, Task> writeResultAsync,
        Func<string> nextTurnId,
        Func<string?, int> countTextChars,
        Func<KernelTurnStartRequest, int> countTurnInputTextChars,
        int maxUserInputTextChars,
        Func<KernelThreadRecord, KernelThreadSessionState> buildDefaultThreadSession,
        Func<KernelThreadSessionState, KernelTurnStartRequest, KernelThreadSessionState> applyTurnOverrides,
        Func<KernelRuntimeThread, KernelThreadSessionState, KernelTurnStartRequest, CancellationToken, Task<TurnRequestContext>> buildTurnRequestContext,
        Func<KernelThreadSessionState, CancellationToken, Task> updateMcpSandboxStateAsync,
        Func<KernelTurnStartRequest, string> extractUserText,
        Action<string, string, string?, IReadOnlyList<KernelTurnInputItem>?> seedTrackedTurnUserMessage,
        KernelTurnBackgroundSchedulerRuntime backgroundTurnSchedulerRuntime,
        KernelTurnStartStateRuntime turnStartStateRuntime,
        Func<string, string, string, TurnRequestContext, bool, CancellationTokenSource, Task> runTurnAsync)
    {
        this.threadStore = threadStore ?? throw new ArgumentNullException(nameof(threadStore));
        this.threadManager = threadManager ?? throw new ArgumentNullException(nameof(threadManager));
        this.writeErrorAsync = writeErrorAsync ?? throw new ArgumentNullException(nameof(writeErrorAsync));
        this.writeResultAsync = writeResultAsync ?? throw new ArgumentNullException(nameof(writeResultAsync));
        this.nextTurnId = nextTurnId ?? throw new ArgumentNullException(nameof(nextTurnId));
        this.countTextChars = countTextChars ?? throw new ArgumentNullException(nameof(countTextChars));
        this.countTurnInputTextChars = countTurnInputTextChars ?? throw new ArgumentNullException(nameof(countTurnInputTextChars));
        this.maxUserInputTextChars = maxUserInputTextChars;
        this.buildDefaultThreadSession = buildDefaultThreadSession ?? throw new ArgumentNullException(nameof(buildDefaultThreadSession));
        this.applyTurnOverrides = applyTurnOverrides ?? throw new ArgumentNullException(nameof(applyTurnOverrides));
        this.buildTurnRequestContext = buildTurnRequestContext ?? throw new ArgumentNullException(nameof(buildTurnRequestContext));
        this.updateMcpSandboxStateAsync = updateMcpSandboxStateAsync ?? throw new ArgumentNullException(nameof(updateMcpSandboxStateAsync));
        this.extractUserText = extractUserText ?? throw new ArgumentNullException(nameof(extractUserText));
        this.seedTrackedTurnUserMessage = seedTrackedTurnUserMessage ?? throw new ArgumentNullException(nameof(seedTrackedTurnUserMessage));
        this.backgroundTurnSchedulerRuntime = backgroundTurnSchedulerRuntime ?? throw new ArgumentNullException(nameof(backgroundTurnSchedulerRuntime));
        this.turnStartStateRuntime = turnStartStateRuntime ?? throw new ArgumentNullException(nameof(turnStartStateRuntime));
        this.runTurnAsync = runTurnAsync ?? throw new ArgumentNullException(nameof(runTurnAsync));
    }

    public async Task HandleTurnStartAsync(
        JsonElement id,
        KernelTurnStartRequest request,
        CancellationToken cancellationToken)
    {
        var threadId = request.ThreadId;
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await writeErrorAsync(id, -32602, "threadId 不能为空。", null, cancellationToken).ConfigureAwait(false);
            return;
        }

        var record = await threadStore.GetThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            await writeErrorAsync(id, -32004, $"线程不存在：{threadId}", null, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (record.IsArchived)
        {
            await writeErrorAsync(id, -32006, $"线程已归档，无法开始 turn：{threadId}", null, cancellationToken).ConfigureAwait(false);
            return;
        }

        var inputChars = countTurnInputTextChars(request);
        if (inputChars > maxUserInputTextChars)
        {
            await writeErrorAsync(
                id,
                -32602,
                $"Input exceeds the maximum length of {maxUserInputTextChars} characters.",
                new
                {
                    input_error_code = "input_too_large",
                    max_chars = maxUserInputTextChars,
                    actual_chars = inputChars,
                },
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var runtimeThread = threadManager.GetOrAttachThread(record, buildDefaultThreadSession, loaded: true);
        var updatedSession = applyTurnOverrides(runtimeThread.Session, request);
        runtimeThread.UpdateSession(updatedSession);
        await updateMcpSandboxStateAsync(updatedSession, cancellationToken).ConfigureAwait(false);

        var userText = extractUserText(request);
        var turnContext = await buildTurnRequestContext(runtimeThread, updatedSession, request, cancellationToken).ConfigureAwait(false);
        var launch = await PrepareLaunchAsync(
                threadId,
                runtimeThread,
                userText,
                turnContext,
                updatedSession.PersistExtendedHistory,
                refreshRuntimeThread: false,
                cancellationToken)
            .ConfigureAwait(false);

        await writeResultAsync(id, new
        {
            turn = new
            {
                id = launch.TurnId,
                status = "inProgress",
                items = Array.Empty<object>(),
            },
        }, cancellationToken).ConfigureAwait(false);

        await PublishStartedAndScheduleAsync(launch, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> StartBackgroundTurnAsync(
        KernelThreadRecord record,
        KernelRuntimeThread runtimeThread,
        string userText,
        TurnRequestContext turnContext,
        bool persistExtendedHistory,
        CancellationToken cancellationToken)
    {
        if (countTextChars(userText) > maxUserInputTextChars)
        {
            throw new InvalidOperationException($"Input exceeds the maximum length of {maxUserInputTextChars} characters.");
        }

        var launch = await PrepareLaunchAsync(
                record.Id,
                runtimeThread,
                userText,
                turnContext,
                persistExtendedHistory,
                refreshRuntimeThread: true,
                cancellationToken)
            .ConfigureAwait(false);
        await PublishStartedAndScheduleAsync(launch, cancellationToken).ConfigureAwait(false);
        return launch.TurnId;
    }

    private async Task<TurnLaunchState> PrepareLaunchAsync(
        string threadId,
        KernelRuntimeThread runtimeThread,
        string userText,
        TurnRequestContext turnContext,
        bool persistExtendedHistory,
        bool refreshRuntimeThread,
        CancellationToken cancellationToken)
    {
        var turnId = nextTurnId();
        _ = backgroundTurnSchedulerRuntime.Register(turnId, cancellationToken);
        runtimeThread.SetActiveTurn(turnId);
        seedTrackedTurnUserMessage(threadId, turnId, userText, turnContext.InputItems);
        var activeRecord = await turnStartStateRuntime
            .PersistAsync(
                threadId,
                turnId,
                userText,
                turnContext,
                cancellationToken)
            .ConfigureAwait(false);

        return new TurnLaunchState(
            threadId,
            turnId,
            userText,
            turnContext,
            persistExtendedHistory,
            runtimeThread,
            activeRecord,
            refreshRuntimeThread);
    }

    private async Task PublishStartedAndScheduleAsync(
        TurnLaunchState launch,
        CancellationToken cancellationToken)
    {
        if (launch.ActiveRecord is not null)
        {
            await turnStartStateRuntime
                .PublishStartedAsync(
                    launch.ThreadId,
                    launch.ActiveRecord,
                    launch.RuntimeThread,
                    launch.RefreshRuntimeThread,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        backgroundTurnSchedulerRuntime.Schedule(
            launch.TurnId,
            runningCts => runTurnAsync(
                launch.ThreadId,
                launch.TurnId,
                launch.UserText,
                launch.TurnContext,
                launch.PersistExtendedHistory,
                runningCts));
    }

    private sealed record TurnLaunchState(
        string ThreadId,
        string TurnId,
        string UserText,
        TurnRequestContext TurnContext,
        bool PersistExtendedHistory,
        KernelRuntimeThread RuntimeThread,
        KernelThreadRecord? ActiveRecord,
        bool RefreshRuntimeThread);
}
