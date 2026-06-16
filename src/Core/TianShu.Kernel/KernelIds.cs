using TianShu.Contracts.Kernel;

namespace TianShu.Kernel;

internal static class KernelIds
{
    public static KernelRunId NewRunId() => new($"kernel-run-{Guid.NewGuid():N}");

    public static KernelTraceId TraceIdFor(KernelRunId runId) => new($"trace-{runId.Value}");

    public static KernelOperationId OperationIdFor(StageId stageId) => new($"operation-{stageId.Value}");
}
