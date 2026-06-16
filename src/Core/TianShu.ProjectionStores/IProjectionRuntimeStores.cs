using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Primitives;

namespace TianShu.ProjectionStores;

/// <summary>
/// Projection / trace / replay runtime store facade.
/// 统一暴露 projection / trace / replay 运行时存储入口。
/// </summary>
public interface IProjectionRuntimeStores
{
    /// <summary>
    /// Current materialized projection snapshots.
    /// 当前物化投影视图快照。
    /// </summary>
    IProjectionSnapshotStore Snapshots { get; }

    /// <summary>
    /// Ordered execution trace history.
    /// 顺序执行追踪历史。
    /// </summary>
    IExecutionTraceStore ExecutionTraces { get; }

    /// <summary>
    /// Current replay checkpoints keyed by execution and stage.
    /// 按执行与阶段索引的当前 replay checkpoint。
    /// </summary>
    IReplayCheckpointStore ReplayCheckpoints { get; }

    /// <summary>
    /// Records a recovery checkpoint into trace history and updates the current replay state in one formal entry.
    /// 通过单一正式入口同时写入 trace history 与 replay current state。
    /// </summary>
    Task<ExecutionTrace> RecordRecoveryCheckpointAsync(
        ExecutionTraceId traceId,
        ExecutionId executionId,
        RecoveryCheckpoint checkpoint,
        CancellationToken cancellationToken);
}
