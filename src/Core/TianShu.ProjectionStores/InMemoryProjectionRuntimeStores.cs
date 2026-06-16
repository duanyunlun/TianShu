using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Primitives;

namespace TianShu.ProjectionStores;

/// <summary>
/// In-memory composition facade for projection / trace / replay runtime stores.
/// 基于内存实现的 projection / trace / replay 运行时存储组合 facade。
/// </summary>
public sealed class InMemoryProjectionRuntimeStores : IProjectionRuntimeStores
{
    /// <summary>
    /// Initializes a new facade with fresh in-memory stores.
    /// 使用新的进程内 store 初始化 facade。
    /// </summary>
    public InMemoryProjectionRuntimeStores()
        : this(
            new InMemoryProjectionSnapshotStore(),
            new InMemoryExecutionTraceStore(),
            new InMemoryReplayCheckpointStore())
    {
    }

    /// <summary>
    /// Initializes a new facade over the provided stores.
    /// 使用给定的 store 组合出 facade。
    /// </summary>
    public InMemoryProjectionRuntimeStores(
        IProjectionSnapshotStore snapshots,
        IExecutionTraceStore executionTraces,
        IReplayCheckpointStore replayCheckpoints)
    {
        Snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
        ExecutionTraces = executionTraces ?? throw new ArgumentNullException(nameof(executionTraces));
        ReplayCheckpoints = replayCheckpoints ?? throw new ArgumentNullException(nameof(replayCheckpoints));
    }

    /// <inheritdoc />
    public IProjectionSnapshotStore Snapshots { get; }

    /// <inheritdoc />
    public IExecutionTraceStore ExecutionTraces { get; }

    /// <inheritdoc />
    public IReplayCheckpointStore ReplayCheckpoints { get; }

    /// <inheritdoc />
    public async Task<ExecutionTrace> RecordRecoveryCheckpointAsync(
        ExecutionTraceId traceId,
        ExecutionId executionId,
        RecoveryCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(checkpoint);

        var trace = await ExecutionTraces.AppendRecoveryCheckpointAsync(
            traceId,
            executionId,
            checkpoint,
            cancellationToken).ConfigureAwait(false);

        await ReplayCheckpoints.SaveAsync(checkpoint, cancellationToken).ConfigureAwait(false);
        return trace;
    }
}
