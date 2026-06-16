using TianShu.Contracts.Catalog;
using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Primitives;

namespace TianShu.Execution.Runtime;

/// <summary>
/// 线程编排输入投影工厂，负责把持久化线程编排状态转换为 core-loop 可消费的契约值。
/// Thread orchestration input projection factory that converts persisted thread orchestration state into core-loop contract values.
/// </summary>
internal static class KernelThreadOrchestrationInputProjectionFactory
{
    public static KernelThreadOrchestrationInputProjection Project(KernelThreadOrchestrationStateRecord? orchestrationState)
    {
        var checkpoints = ToStageCheckpoints(orchestrationState?.Checkpoints);
        return new KernelThreadOrchestrationInputProjection(
            CurrentStageId: Normalize(orchestrationState?.CurrentStageId),
            LatestCheckpointStageId: checkpoints.LastOrDefault()?.StageId,
            Checkpoints: checkpoints,
            ContextLedgerSegments: ToStageContextSegments(orchestrationState?.ContextLedgerSegments));
    }

    private static IReadOnlyList<StageContextSegment> ToStageContextSegments(
        IReadOnlyList<KernelThreadStageContextSegmentStateRecord>? records)
        => records is null
            ? Array.Empty<StageContextSegment>()
            : records
                .Select(ToStageContextSegment)
                .Where(static item => item is not null)
                .Cast<StageContextSegment>()
                .ToArray();

    private static StageContextSegment? ToStageContextSegment(KernelThreadStageContextSegmentStateRecord record)
    {
        var kind = Normalize(record.Kind);
        var content = Normalize(record.Content);
        if (kind is null || content is null)
        {
            return null;
        }

        return new StageContextSegment(
            kind,
            content,
            title: Normalize(record.Title),
            source: ToResourceRef(record.Source),
            required: record.Required,
            estimatedTokens: record.EstimatedTokens);
    }

    private static IReadOnlyList<StageCheckpoint> ToStageCheckpoints(
        IReadOnlyList<KernelThreadStageCheckpointStateRecord>? records)
        => records is null
            ? Array.Empty<StageCheckpoint>()
            : records
                .Select(ToStageCheckpoint)
                .Where(static item => item is not null)
                .Cast<StageCheckpoint>()
                .ToArray();

    private static StageCheckpoint? ToStageCheckpoint(KernelThreadStageCheckpointStateRecord record)
    {
        var checkpointId = Normalize(record.CheckpointId);
        var stageId = Normalize(record.StageId);
        if (checkpointId is null || stageId is null)
        {
            return null;
        }

        return new StageCheckpoint(
            checkpointId,
            stageId,
            record.State,
            record.StartedAt == default ? DateTimeOffset.UtcNow : record.StartedAt,
            record.CompletedAt,
            summary: Normalize(record.Summary),
            artifactRefs: ToArtifactRefs(record.ArtifactRefs),
            modelRouteSetId: Normalize(record.ModelRouteSetId),
            modelRouteKind: ToModelRouteKind(record.ModelRouteKind),
            diagnostics: record.Diagnostics,
            nextStageSuggestions: record.NextStageSuggestions);
    }

    private static IReadOnlyList<ArtifactRef> ToArtifactRefs(IReadOnlyList<KernelThreadArtifactRefStateRecord>? records)
        => records is null
            ? Array.Empty<ArtifactRef>()
            : records
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

    private static ResourceRef? ToResourceRef(KernelThreadResourceRefStateRecord? record)
    {
        var kind = Normalize(record?.Kind);
        var key = Normalize(record?.Key);
        return kind is null || key is null
            ? null
            : new ResourceRef(kind, key);
    }

    private static ModelRouteKind? ToModelRouteKind(string? value)
        => Normalize(value) is { } normalized ? new ModelRouteKind(normalized) : null;

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed record KernelThreadOrchestrationInputProjection(
    string? CurrentStageId,
    string? LatestCheckpointStageId,
    IReadOnlyList<StageCheckpoint> Checkpoints,
    IReadOnlyList<StageContextSegment> ContextLedgerSegments);
