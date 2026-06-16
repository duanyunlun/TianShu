using TianShu.AppHost.State;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// Turn start state 运行时，负责持久化 turn 启动状态、日志、状态通知与 rollout session meta。
/// Turn start state runtime that persists turn-start state, logs, status notifications, and rollout session metadata.
/// </summary>
internal sealed class KernelTurnStartStateRuntime
{
    private readonly KernelThreadStore threadStore;
    private readonly Func<string?, string?> normalize;
    private readonly Func<string, string, string, string, string?, object, CancellationToken, Task> persistTurnLogAsync;
    private readonly Func<KernelThreadRecord, CancellationToken, Task> writeThreadStatusChangedAsync;

    public KernelTurnStartStateRuntime(
        KernelThreadStore threadStore,
        Func<string?, string?> normalize,
        Func<string, string, string, string, string?, object, CancellationToken, Task> persistTurnLogAsync,
        Func<KernelThreadRecord, CancellationToken, Task> writeThreadStatusChangedAsync)
    {
        this.threadStore = threadStore ?? throw new ArgumentNullException(nameof(threadStore));
        this.normalize = normalize ?? throw new ArgumentNullException(nameof(normalize));
        this.persistTurnLogAsync = persistTurnLogAsync ?? throw new ArgumentNullException(nameof(persistTurnLogAsync));
        this.writeThreadStatusChangedAsync = writeThreadStatusChangedAsync ?? throw new ArgumentNullException(nameof(writeThreadStatusChangedAsync));
    }

    public async Task<KernelThreadRecord?> PersistAsync(
        string threadId,
        string turnId,
        string userText,
        TurnRequestContext turnContext,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var activeRecord = await threadStore
            .UpsertActiveTurnAsync(
                threadId,
                new KernelTurnRecord
                {
                    Id = turnId,
                    StartedAt = startedAt,
                    CompletedAt = startedAt,
                    Status = "inProgress",
                    UserMessage = normalize(userText),
                    InteractionEnvelope = turnContext.InteractionEnvelope,
                },
                cancellationToken)
            .ConfigureAwait(false);

        await persistTurnLogAsync(
            threadId,
            turnId,
            "turn.started",
            "inProgress",
            normalize(userText),
            new
            {
                threadId,
                turnId,
                userText,
                context = turnContext,
            },
            cancellationToken).ConfigureAwait(false);

        if (activeRecord is null)
        {
            return null;
        }

        return activeRecord;
    }

    public async Task PublishStartedAsync(
        string threadId,
        KernelThreadRecord activeRecord,
        KernelRuntimeThread runtimeThread,
        bool refreshRuntimeThread,
        CancellationToken cancellationToken)
    {
        if (refreshRuntimeThread)
        {
            runtimeThread.Update(activeRecord, runtimeThread.Session, loaded: true);
        }

        await writeThreadStatusChangedAsync(activeRecord, cancellationToken).ConfigureAwait(false);
        if (runtimeThread.Session.Ephemeral)
        {
            return;
        }

        await threadStore.RolloutRecorder
            .EnsureSessionMetaAsync(
                threadId,
                KernelRolloutStateMapper.ToRolloutThreadRecord(activeRecord, runtimeThread.ConfigSnapshot),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
