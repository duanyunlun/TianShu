using TianShu.Contracts.Primitives;

namespace TianShu.Execution.Runtime;

/// <summary>
/// 线程观察状态投影工厂，负责把线程编排状态转换为 Kernel observed state 可消费的契约值。
/// Thread observed-state projection factory that converts thread orchestration state into Kernel observed-state contract values.
/// </summary>
internal static class KernelThreadObservedStateProjectionFactory
{
    public static IReadOnlyList<ArtifactRef> ProjectArtifactRefs(KernelThreadOrchestrationStateRecord? orchestrationState)
        => (orchestrationState?.LastContextPackage?.ArtifactRefs ?? [])
            .Concat(orchestrationState?.Checkpoints.SelectMany(static checkpoint => checkpoint.ArtifactRefs) ?? [])
            .Select(ToArtifactRef)
            .Where(static item => item is not null)
            .Cast<ArtifactRef>()
            .ToArray();

    private static ArtifactRef? ToArtifactRef(KernelThreadArtifactRefStateRecord record)
    {
        var id = Normalize(record.Id);
        return id is null
            ? null
            : new ArtifactRef(new ArtifactId(id), Normalize(record.Name), Normalize(record.Kind));
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
