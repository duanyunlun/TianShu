using TianShu.AppHost.State;
using TianShu.Execution.Runtime;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// Terminal turn projection commit runtime，负责把已完成 turn 固化为终态投影并写回线程状态。
/// Terminal turn projection commit runtime that pins a completed turn to terminal projection state.
/// </summary>
internal sealed class KernelTerminalTurnProjectionCommitRuntime
{
    private readonly KernelThreadStore threadStore;
    private readonly Func<string?, string?> normalize;
    private readonly Func<string, string, string, string, string?, object, CancellationToken, Task> persistTurnLogAsync;

    public KernelTerminalTurnProjectionCommitRuntime(
        KernelThreadStore threadStore,
        Func<string?, string?> normalize,
        Func<string, string, string, string, string?, object, CancellationToken, Task> persistTurnLogAsync)
    {
        this.threadStore = threadStore ?? throw new ArgumentNullException(nameof(threadStore));
        this.normalize = normalize ?? throw new ArgumentNullException(nameof(normalize));
        this.persistTurnLogAsync = persistTurnLogAsync ?? throw new ArgumentNullException(nameof(persistTurnLogAsync));
    }

    public async Task TryCommitTerminalTurnProjectionAsync(
        string threadId,
        string turnId,
        TurnRequestContext turnContext,
        string? reviewOutputText,
        string? reviewFailureMessage,
        string? effectiveUserText,
        string? finalAssistantText,
        string finalTurnStatus,
        KernelTurnErrorRecord? finalTurnError,
        DateTimeOffset? turnCompletedAt)
    {
        var stageId = normalize(turnContext.StageId);
        if (stageId is null)
        {
            return;
        }

        var dispatchContext = ResolveExecutionDispatchContext(turnContext);
        var checkpoint = TurnTerminalProjectionCheckpointBuilder.Instance.CompleteTerminalTurn(
            dispatchContext,
            turnId,
            finalTurnStatus,
            turnCompletedAt,
            effectiveUserText,
            finalAssistantText,
            reviewOutputText,
            reviewFailureMessage,
            finalTurnError?.Message,
            finalTurnError?.AdditionalDetails);

        try
        {
            await threadStore.AppendTerminalTurnProjectionAsync(threadId, checkpoint, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await persistTurnLogAsync(
                threadId,
                turnId,
                "stage.checkpoint.persist",
                "failed",
                normalize(ex.Message) ?? "append_stage_checkpoint_failed",
                new
                {
                    threadId,
                    turnId,
                    stageId,
                    checkpointId = checkpoint.CheckpointId,
                    exceptionType = ex.GetType().FullName,
                    error = ex.Message,
                },
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static TurnExecutionDispatchContext ResolveExecutionDispatchContext(TurnRequestContext turnContext)
        => turnContext.ExecutionDispatchContext
           ?? throw new ArgumentException("Execution dispatch context 不能为空。", "turnContext.ExecutionDispatchContext");
}
