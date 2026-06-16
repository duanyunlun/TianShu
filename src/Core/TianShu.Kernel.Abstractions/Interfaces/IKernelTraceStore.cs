using TianShu.Contracts.Kernel;

namespace TianShu.Kernel.Abstractions;

/// <summary>
/// Kernel trace 存储抽象，只保存可审计、可回放的 Kernel 记录。
/// Kernel trace store abstraction for auditable and replayable Kernel records.
/// </summary>
public interface IKernelTraceStore
{
    Task AppendAsync(KernelRunId runId, KernelTraceEvent traceEvent, CancellationToken cancellationToken = default);

    Task<KernelTrace?> ReadTraceAsync(KernelTraceId traceId, CancellationToken cancellationToken = default);

    Task<KernelRunTrace?> ReadRunTraceAsync(KernelRunId runId, CancellationToken cancellationToken = default);

    Task<KernelTraceSummary> SummarizeAsync(KernelRunId runId, CancellationToken cancellationToken = default);
}
