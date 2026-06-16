using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Primitives;

namespace TianShu.ProjectionStores;

/// <summary>
/// 执行追踪存储，负责逐步聚合同一执行链路的 attempt、audit 与 checkpoint。
/// Execution trace store responsible for incrementally aggregating attempts, audits, and checkpoints for the same execution flow.
/// </summary>
public interface IExecutionTraceStore
{
    /// <summary>
    /// 追加一次执行尝试。
    /// Appends an execution attempt.
    /// </summary>
    Task<ExecutionTrace> AppendAttemptAsync(
        ExecutionTraceId traceId,
        ExecutionId executionId,
        AttemptSummary attempt,
        CancellationToken cancellationToken);

    /// <summary>
    /// 追加一条审计记录。
    /// Appends an audit record.
    /// </summary>
    Task<ExecutionTrace> AppendAuditRecordAsync(
        ExecutionTraceId traceId,
        ExecutionId executionId,
        AuditRecord record,
        CancellationToken cancellationToken);

    /// <summary>
    /// 追加一条恢复检查点。
    /// Appends a recovery checkpoint.
    /// </summary>
    Task<ExecutionTrace> AppendRecoveryCheckpointAsync(
        ExecutionTraceId traceId,
        ExecutionId executionId,
        RecoveryCheckpoint checkpoint,
        CancellationToken cancellationToken);

    /// <summary>
    /// 读取完整执行追踪。
    /// Reads the full execution trace.
    /// </summary>
    Task<ExecutionTrace?> GetAsync(ExecutionTraceId traceId, CancellationToken cancellationToken);

    /// <summary>
    /// 按执行链路读取已记录的尝试摘要。
    /// Reads attempt summaries recorded for one execution flow.
    /// </summary>
    Task<IReadOnlyList<AttemptSummary>> ListAttemptsAsync(ExecutionId executionId, CancellationToken cancellationToken);
}
