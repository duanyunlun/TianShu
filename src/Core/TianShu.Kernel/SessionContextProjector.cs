using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Primitives;

namespace TianShu.Kernel;

/// <summary>
/// 会话上下文投影器，负责按目标 Stage 的投影策略生成 StageContextPackage。
/// Session context projector that creates a StageContextPackage from the target stage projection policy.
/// </summary>
public sealed class SessionContextProjector
{
    /// <summary>
    /// 生成 Stage 上下文包。
    /// Creates a stage context package.
    /// </summary>
    public StageContextPackage Project(
        string packageId,
        StageDefinition selectedStage,
        SessionId sessionId,
        ThreadId threadId,
        IReadOnlyList<StageCheckpoint> checkpoints,
        IReadOnlyList<StageContextSegment> contextLedgerSegments,
        int? contextBudgetTokens,
        SessionObservedState? observedState = null)
    {
        ArgumentNullException.ThrowIfNull(selectedStage);

        return new StageContextPackage(
            packageId,
            selectedStage.Id,
            sessionId,
            threadId,
            segments: ProjectSegments(contextLedgerSegments, observedState, selectedStage.ContextProjectionMode, contextBudgetTokens),
            sourceCheckpointIds: (checkpoints ?? Array.Empty<StageCheckpoint>()).Select(static checkpoint => checkpoint.CheckpointId).ToArray(),
            projectionMode: selectedStage.ContextProjectionMode,
            budgetTokens: contextBudgetTokens);
    }

    private static IReadOnlyList<StageContextSegment> ProjectSegments(
        IReadOnlyList<StageContextSegment>? ledgerSegments,
        SessionObservedState? observedState,
        StageContextProjectionMode projectionMode,
        int? budgetTokens)
    {
        var sourceSegments = (ledgerSegments ?? Array.Empty<StageContextSegment>())
            .Concat((observedState ?? SessionObservedState.Empty).ToContextSegments())
            .ToArray();
        var filteredSegments = projectionMode == StageContextProjectionMode.ReferencesOnly
            ? sourceSegments.Where(static segment => segment.Source is not null || string.Equals(segment.Kind, "reference", StringComparison.OrdinalIgnoreCase))
            : sourceSegments;

        if (budgetTokens is null)
        {
            return filteredSegments.ToArray();
        }

        var remaining = budgetTokens.Value;
        var projected = new List<StageContextSegment>();
        foreach (var segment in filteredSegments.OrderByDescending(static item => item.Required))
        {
            var estimatedTokens = segment.EstimatedTokens ?? 0;
            if (!segment.Required && estimatedTokens > remaining)
            {
                continue;
            }

            projected.Add(segment);
            remaining -= estimatedTokens;
        }

        return projected.ToArray();
    }
}
