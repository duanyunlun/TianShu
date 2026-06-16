using TianShu.AppHost.State;
using TianShu.Contracts.Orchestration;

namespace TianShu.RuntimeComposition;

/// <summary>
/// AppHost Core Loop 编排状态存储边界，负责读取与提交线程级 orchestration state。
/// AppHost core-loop orchestration state store boundary that reads and commits thread orchestration state.
/// </summary>
internal sealed class AppHostCoreLoopOrchestrationStateStore
{
    private readonly KernelThreadStore threadStore;

    public AppHostCoreLoopOrchestrationStateStore(KernelThreadStore threadStore)
        => this.threadStore = threadStore ?? throw new ArgumentNullException(nameof(threadStore));

    public async Task<AppHostCoreLoopStoredOrchestrationState> ReadAsync(
        string threadId,
        CancellationToken cancellationToken)
    {
        var record = await threadStore.GetThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        return new AppHostCoreLoopStoredOrchestrationState(
            record is not null,
            record?.SessionState?.Orchestration,
            record?.Cwd,
            record?.MemoryMode);
    }

    public async Task CommitStepAsync(
        string threadId,
        AppHostCoreLoopStoredOrchestrationState state,
        OrchestratorDecision decision,
        StageContextPackage contextPackage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(contextPackage);

        if (!state.ThreadExists)
        {
            return;
        }

        await threadStore
            .ApplySessionOrchestrationStepAsync(
                threadId,
                decision,
                contextPackage,
                cancellationToken)
            .ConfigureAwait(false);
    }
}

internal sealed record AppHostCoreLoopStoredOrchestrationState(
    bool ThreadExists,
    KernelThreadOrchestrationStateRecord? Orchestration,
    string? Cwd,
    string? MemoryMode);
