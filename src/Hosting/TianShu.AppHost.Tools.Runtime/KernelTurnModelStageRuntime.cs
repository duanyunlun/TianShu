using System.Diagnostics;
using TianShu.Execution.Runtime;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// Turn model stage 运行时，负责默认模型 Stage 的执行生命周期。
/// Runtime that owns the default model stage execution lifecycle.
/// </summary>
internal sealed class KernelTurnModelStageRuntime
{
    private readonly Func<KernelCollaborationModeState?, bool> isPlanCollaborationMode;
    private readonly Func<string?, string?> normalize;
    private readonly Func<string, string, string, CancellationToken, bool, Task> resolvePendingInteractiveRequestsForThreadLifecycleAsync;
    private readonly Func<TurnOperationState, Task> ensureAgentMessageStartedAsync;
    private readonly Func<string, string, TurnRequestContext, Activity?> startTurnActivity;
    private readonly Func<string, string, string, string, TurnRequestContext, CancellationToken, Task> maybeExtractMemoryFromCompletedTurnAsync;
    private readonly KernelTurnActiveSnapshotRuntime activeSnapshotRuntime;
    private readonly KernelTurnTerminalStateRuntime terminalStateRuntime;
    private readonly KernelTurnFinalizationRuntime finalizationRuntime;
    private readonly KernelTurnStageNotificationRuntime stageNotificationRuntime;
    private readonly KernelTurnOperationChainRuntime operationChainRuntime;

    public KernelTurnModelStageRuntime(
        Func<KernelCollaborationModeState?, bool> isPlanCollaborationMode,
        Func<string?, string?> normalize,
        Func<string, string, string, CancellationToken, bool, Task> resolvePendingInteractiveRequestsForThreadLifecycleAsync,
        Func<TurnOperationState, Task> ensureAgentMessageStartedAsync,
        Func<string, string, TurnRequestContext, Activity?> startTurnActivity,
        Func<string, string, string, string, TurnRequestContext, CancellationToken, Task> maybeExtractMemoryFromCompletedTurnAsync,
        KernelTurnActiveSnapshotRuntime activeSnapshotRuntime,
        KernelTurnTerminalStateRuntime terminalStateRuntime,
        KernelTurnFinalizationRuntime finalizationRuntime,
        KernelTurnStageNotificationRuntime stageNotificationRuntime,
        KernelTurnOperationChainRuntime operationChainRuntime)
    {
        this.isPlanCollaborationMode = isPlanCollaborationMode ?? throw new ArgumentNullException(nameof(isPlanCollaborationMode));
        this.normalize = normalize ?? throw new ArgumentNullException(nameof(normalize));
        this.resolvePendingInteractiveRequestsForThreadLifecycleAsync = resolvePendingInteractiveRequestsForThreadLifecycleAsync ?? throw new ArgumentNullException(nameof(resolvePendingInteractiveRequestsForThreadLifecycleAsync));
        this.ensureAgentMessageStartedAsync = ensureAgentMessageStartedAsync ?? throw new ArgumentNullException(nameof(ensureAgentMessageStartedAsync));
        this.startTurnActivity = startTurnActivity ?? throw new ArgumentNullException(nameof(startTurnActivity));
        this.maybeExtractMemoryFromCompletedTurnAsync = maybeExtractMemoryFromCompletedTurnAsync ?? throw new ArgumentNullException(nameof(maybeExtractMemoryFromCompletedTurnAsync));
        this.activeSnapshotRuntime = activeSnapshotRuntime ?? throw new ArgumentNullException(nameof(activeSnapshotRuntime));
        this.terminalStateRuntime = terminalStateRuntime ?? throw new ArgumentNullException(nameof(terminalStateRuntime));
        this.finalizationRuntime = finalizationRuntime ?? throw new ArgumentNullException(nameof(finalizationRuntime));
        this.stageNotificationRuntime = stageNotificationRuntime ?? throw new ArgumentNullException(nameof(stageNotificationRuntime));
        this.operationChainRuntime = operationChainRuntime ?? throw new ArgumentNullException(nameof(operationChainRuntime));
    }

    public Task ExecuteAsync(
        TurnExecutionRunRequest<TurnRequestContext> request,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            request.ThreadId,
            request.TurnId,
            request.UserText,
            request.Context,
            request.PersistExtendedHistory,
            cancellationToken);

    private async Task ExecuteAsync(
        string threadId,
        string turnId,
        string userText,
        TurnRequestContext turnContext,
        bool persistExtendedHistory,
        CancellationToken cancellationToken)
    {
        var itemId = $"msg_{turnId}";
        var reasoningItemId = $"reasoning_{turnId}";
        var effectiveUserText = userText;
        var reviewEnterItemId = turnId;
        var reviewExitItemId = turnId;
        var reviewOutputText = string.Empty;
        var reviewFailureMessage = string.Empty;
        var finalTurnStatus = "inProgress";
        string? finalAssistantText = null;
        KernelTurnErrorRecord? finalTurnError = null;
        var turnSessionPersistedBeforeTerminal = false;
        var opState = new TurnOperationState(threadId, turnId, itemId, reasoningItemId, userText)
        {
            IsPlanMode = isPlanCollaborationMode(turnContext.CollaborationMode),
        };
        using var turnActivity = startTurnActivity(threadId, turnId, turnContext);

        try
        {
            await resolvePendingInteractiveRequestsForThreadLifecycleAsync(
                    threadId,
                    turnId,
                    "turn_started",
                    CancellationToken.None,
                    false)
                .ConfigureAwait(false);

            await stageNotificationRuntime.PublishTurnStartedAsync(threadId, turnId).ConfigureAwait(false);
            await activeSnapshotRuntime.PersistAsync(threadId, turnId, CancellationToken.None).ConfigureAwait(false);

            if (turnContext.IsReview)
            {
                await stageNotificationRuntime
                    .PublishReviewEnteredAsync(
                        threadId,
                        turnId,
                        reviewEnterItemId,
                        turnContext.ReviewDisplayText)
                    .ConfigureAwait(false);
            }

            if (!opState.IsPlanMode)
            {
                await ensureAgentMessageStartedAsync(opState).ConfigureAwait(false);
            }

            opState = await operationChainRuntime.ExecuteAsync(
                opState,
                turnContext,
                cancellationToken).ConfigureAwait(false);
            effectiveUserText = opState.EffectiveUserText;
            var assistant = opState.AssistantText;
            reviewOutputText = assistant;
            finalTurnStatus = "completed";
            finalAssistantText = assistant;
            var terminalCommit = CreateTerminalCommit(
                threadId,
                turnId,
                turnContext,
                reviewExitItemId,
                reviewOutputText,
                reviewFailureMessage,
                effectiveUserText,
                finalAssistantText,
                finalTurnStatus,
                finalTurnError,
                persistExtendedHistory);

            await terminalStateRuntime.PersistAsync(terminalCommit).ConfigureAwait(false);

            await maybeExtractMemoryFromCompletedTurnAsync(
                    threadId,
                    turnId,
                    effectiveUserText,
                    assistant,
                    turnContext,
                    CancellationToken.None)
                .ConfigureAwait(false);

            await stageNotificationRuntime.PublishCompletedAgentMessageAsync(opState, assistant).ConfigureAwait(false);
            await stageNotificationRuntime
                .PublishTokenUsageUpdatedAsync(threadId, turnId, effectiveUserText, assistant)
                .ConfigureAwait(false);
            await stageNotificationRuntime.CompletePlanItemAsync(opState).ConfigureAwait(false);

            await terminalStateRuntime.PublishAsync(terminalCommit).ConfigureAwait(false);
            turnSessionPersistedBeforeTerminal = true;
        }
        catch (OperationCanceledException)
        {
            reviewFailureMessage = "review_interrupted";
            finalTurnStatus = "interrupted";
            var terminalCommit = CreateTerminalCommit(
                threadId,
                turnId,
                turnContext,
                reviewExitItemId,
                reviewOutputText,
                reviewFailureMessage,
                effectiveUserText,
                finalAssistantText,
                finalTurnStatus,
                finalTurnError,
                persistExtendedHistory);

            await terminalStateRuntime.PersistAsync(terminalCommit).ConfigureAwait(false);

            await stageNotificationRuntime.PublishInterruptedAgentMessageAsync(opState).ConfigureAwait(false);

            await terminalStateRuntime.PublishAsync(terminalCommit).ConfigureAwait(false);
            turnSessionPersistedBeforeTerminal = true;
        }
        catch (Exception ex)
        {
            reviewFailureMessage = normalize(ex.Message) ?? "review_failed";
            finalTurnStatus = "failed";
            finalAssistantText = reviewFailureMessage;
            finalTurnError = new KernelTurnErrorRecord { Message = ex.Message };
            var terminalCommit = CreateTerminalCommit(
                threadId,
                turnId,
                turnContext,
                reviewExitItemId,
                reviewOutputText,
                reviewFailureMessage,
                effectiveUserText,
                finalAssistantText,
                finalTurnStatus,
                finalTurnError,
                persistExtendedHistory);

            await terminalStateRuntime.PersistAsync(terminalCommit).ConfigureAwait(false);

            await stageNotificationRuntime.PublishFailedAgentMessageAsync(opState, reviewFailureMessage).ConfigureAwait(false);
            await stageNotificationRuntime.PublishErrorAsync(threadId, turnId, ex.Message).ConfigureAwait(false);

            await terminalStateRuntime.PublishAsync(terminalCommit).ConfigureAwait(false);
            turnSessionPersistedBeforeTerminal = true;
        }
        finally
        {
            if (!turnSessionPersistedBeforeTerminal)
            {
                await terminalStateRuntime
                    .PersistTurnSessionBeforeTerminalAsync(
                        CreateTerminalCommit(
                            threadId,
                            turnId,
                            turnContext,
                            reviewExitItemId,
                            reviewOutputText,
                            reviewFailureMessage,
                            effectiveUserText,
                            finalAssistantText,
                            finalTurnStatus,
                            finalTurnError,
                            persistExtendedHistory))
                    .ConfigureAwait(false);
            }

            await finalizationRuntime.FinalizeAsync(threadId, turnId).ConfigureAwait(false);
        }
    }

    private static KernelTurnTerminalStateCommit CreateTerminalCommit(
        string threadId,
        string turnId,
        TurnRequestContext turnContext,
        string reviewExitItemId,
        string reviewOutputText,
        string reviewFailureMessage,
        string effectiveUserText,
        string? finalAssistantText,
        string finalTurnStatus,
        KernelTurnErrorRecord? finalTurnError,
        bool persistExtendedHistory)
        => new(
            threadId,
            turnId,
            turnContext,
            reviewExitItemId,
            reviewOutputText,
            reviewFailureMessage,
            effectiveUserText,
            finalAssistantText,
            finalTurnStatus,
            finalTurnError,
            persistExtendedHistory);
}
