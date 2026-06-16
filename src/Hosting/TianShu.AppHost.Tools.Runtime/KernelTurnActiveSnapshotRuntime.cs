using TianShu.AppHost.State;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// Active turn snapshot 运行时，负责把正在执行的 turn 快照持久化到线程状态与 rollout。
/// Active turn snapshot runtime that persists an in-flight turn snapshot into thread state and rollout records.
/// </summary>
internal sealed class KernelTurnActiveSnapshotRuntime
{
    private readonly KernelThreadStore threadStore;
    private readonly KernelThreadManager threadManager;
    private readonly Func<string?, string?, KernelTurnRecord?> buildTrackedActiveTurnSnapshot;
    private readonly Func<string, CancellationToken, Task<bool>> isEphemeralThreadAsync;

    public KernelTurnActiveSnapshotRuntime(
        KernelThreadStore threadStore,
        KernelThreadManager threadManager,
        Func<string?, string?, KernelTurnRecord?> buildTrackedActiveTurnSnapshot,
        Func<string, CancellationToken, Task<bool>> isEphemeralThreadAsync)
    {
        this.threadStore = threadStore ?? throw new ArgumentNullException(nameof(threadStore));
        this.threadManager = threadManager ?? throw new ArgumentNullException(nameof(threadManager));
        this.buildTrackedActiveTurnSnapshot = buildTrackedActiveTurnSnapshot ?? throw new ArgumentNullException(nameof(buildTrackedActiveTurnSnapshot));
        this.isEphemeralThreadAsync = isEphemeralThreadAsync ?? throw new ArgumentNullException(nameof(isEphemeralThreadAsync));
    }

    public async Task PersistAsync(
        string threadId,
        string turnId,
        CancellationToken cancellationToken)
    {
        var activeTurn = buildTrackedActiveTurnSnapshot(threadId, turnId);
        if (activeTurn is null)
        {
            return;
        }

        var updatedRecord = await threadStore.UpsertActiveTurnAsync(threadId, activeTurn, cancellationToken).ConfigureAwait(false);
        if (updatedRecord is null)
        {
            return;
        }

        if (threadManager.TryGetThread(threadId, out var runtimeThread) && runtimeThread is not null)
        {
            runtimeThread.Update(updatedRecord, loaded: true);
        }

        if (await isEphemeralThreadAsync(threadId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await threadStore.RolloutRecorder.AppendTurnResultAsync(
            threadId,
            turnId,
            activeTurn.Status,
            activeTurn.UserMessage,
            activeTurn.AssistantMessage,
            cancellationToken,
            items: activeTurn.Items.Select(KernelRolloutStateMapper.ToRolloutTurnItemRecord).ToArray(),
            error: KernelRolloutStateMapper.ToRolloutTurnErrorRecord(activeTurn.Error),
            startedAt: activeTurn.StartedAt,
            completedAt: activeTurn.CompletedAt == default ? activeTurn.StartedAt : activeTurn.CompletedAt,
            interactionEnvelope: activeTurn.InteractionEnvelope).ConfigureAwait(false);
    }
}
