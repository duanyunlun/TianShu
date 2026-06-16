using TianShu.Contracts.Kernel;
using TianShu.Kernel.Abstractions;

namespace TianShu.Kernel.Tracing;

/// <summary>
/// 进程内 Kernel trace 存储，供默认实现和单元测试使用。
/// In-process Kernel trace store used by the default implementation and unit tests.
/// </summary>
public sealed class InMemoryKernelTraceStore : IKernelTraceStore
{
    private readonly Dictionary<KernelRunId, List<KernelTraceEvent>> eventsByRun = new();

    public Task AppendAsync(KernelRunId runId, KernelTraceEvent traceEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(traceEvent);

        if (!eventsByRun.TryGetValue(runId, out var events))
        {
            events = new List<KernelTraceEvent>();
            eventsByRun.Add(runId, events);
        }

        events.Add(traceEvent);
        return Task.CompletedTask;
    }

    public Task<KernelTrace?> ReadTraceAsync(KernelTraceId traceId, CancellationToken cancellationToken = default)
    {
        var runIdValue = traceId.Value.StartsWith("trace-", StringComparison.Ordinal)
            ? traceId.Value["trace-".Length..]
            : traceId.Value;
        var runId = new KernelRunId(runIdValue);

        return eventsByRun.TryGetValue(runId, out var events)
            ? Task.FromResult<KernelTrace?>(new KernelTrace(traceId, runId, new KernelRunTrace(runId, events.ToArray())))
            : Task.FromResult<KernelTrace?>(null);
    }

    public Task<KernelRunTrace?> ReadRunTraceAsync(KernelRunId runId, CancellationToken cancellationToken = default)
        => eventsByRun.TryGetValue(runId, out var events)
            ? Task.FromResult<KernelRunTrace?>(new KernelRunTrace(runId, events.ToArray()))
            : Task.FromResult<KernelRunTrace?>(null);

    public Task<KernelTraceSummary> SummarizeAsync(KernelRunId runId, CancellationToken cancellationToken = default)
    {
        eventsByRun.TryGetValue(runId, out var events);
        events ??= new List<KernelTraceEvent>();

        var summary = new KernelTraceSummary(
            runId,
            events.Count,
            events.Where(static item => item.Kind == KernelTraceEventKind.Rejected).Select(static item => item.Message).ToArray(),
            events.Where(static item => item.Kind == KernelTraceEventKind.CheckpointCreated).Select(static item => item.Message).ToArray());

        return Task.FromResult(summary);
    }
}
