using System.Collections.Concurrent;
using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Primitives;

namespace TianShu.ProjectionStores;

/// <summary>
/// 基于进程内内存的执行追踪存储。
/// In-process memory-backed execution trace store.
/// </summary>
public sealed class InMemoryExecutionTraceStore : IExecutionTraceStore
{
    private readonly ConcurrentDictionary<ExecutionTraceId, ExecutionTrace> traces = new();

    /// <inheritdoc />
    public Task<ExecutionTrace> AppendAttemptAsync(
        ExecutionTraceId traceId,
        ExecutionId executionId,
        AttemptSummary attempt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(attempt);

        if (attempt.ExecutionId != executionId)
        {
            throw new ArgumentException("Attempt summary execution id does not match the requested trace execution id.", nameof(attempt));
        }

        var trace = UpsertTrace(
            traceId,
            executionId,
            existing => CreateTrace(
                existing,
                attempts: existing.Attempts.Concat([attempt]).ToArray()));

        return Task.FromResult(trace);
    }

    /// <inheritdoc />
    public Task<ExecutionTrace> AppendAuditRecordAsync(
        ExecutionTraceId traceId,
        ExecutionId executionId,
        AuditRecord record,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(record);

        var trace = UpsertTrace(
            traceId,
            executionId,
            existing => CreateTrace(
                existing,
                auditTrail: existing.AuditTrail.Concat([record]).ToArray()));

        return Task.FromResult(trace);
    }

    /// <inheritdoc />
    public Task<ExecutionTrace> AppendRecoveryCheckpointAsync(
        ExecutionTraceId traceId,
        ExecutionId executionId,
        RecoveryCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(checkpoint);

        if (checkpoint.ExecutionId != executionId)
        {
            throw new ArgumentException("Recovery checkpoint execution id does not match the requested trace execution id.", nameof(checkpoint));
        }

        var trace = UpsertTrace(
            traceId,
            executionId,
            existing => CreateTrace(
                existing,
                recoveryCheckpoints: existing.RecoveryCheckpoints.Concat([checkpoint]).ToArray()));

        return Task.FromResult(trace);
    }

    /// <inheritdoc />
    public Task<ExecutionTrace?> GetAsync(ExecutionTraceId traceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        traces.TryGetValue(traceId, out var trace);
        return Task.FromResult(trace);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AttemptSummary>> ListAttemptsAsync(ExecutionId executionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var attempts = traces.Values
            .Where(trace => trace.ExecutionId == executionId)
            .SelectMany(static trace => trace.Attempts)
            .OrderBy(static attempt => attempt.AttemptNumber)
            .ThenBy(static attempt => attempt.StartedAt)
            .ToArray();
        return Task.FromResult<IReadOnlyList<AttemptSummary>>(attempts);
    }

    private ExecutionTrace UpsertTrace(
        ExecutionTraceId traceId,
        ExecutionId executionId,
        Func<ExecutionTrace, ExecutionTrace> update)
    {
        return traces.AddOrUpdate(
            traceId,
            static (currentTraceId, state) => state.Update(new ExecutionTrace(currentTraceId, state.ExecutionId)),
            static (_, existing, state) =>
            {
                if (existing.ExecutionId != state.ExecutionId)
                {
                    throw new InvalidOperationException("Execution trace store detected mismatched execution ids for the same trace.");
                }

                return state.Update(existing);
            },
            (ExecutionId: executionId, Update: update));
    }

    private static ExecutionTrace CreateTrace(
        ExecutionTrace source,
        IReadOnlyList<AttemptSummary>? attempts = null,
        IReadOnlyList<AuditRecord>? auditTrail = null,
        IReadOnlyList<RecoveryCheckpoint>? recoveryCheckpoints = null)
        => new(
            source.Id,
            source.ExecutionId,
            attempts ?? source.Attempts,
            auditTrail ?? source.AuditTrail,
            recoveryCheckpoints ?? source.RecoveryCheckpoints);
}
