using TianShu.Contracts.Catalog;
using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Primitives;
using TianShu.Kernel;

namespace TianShu.ControlPlane;

/// <summary>
/// 会话检查点工厂，负责将 Stage 执行结果固化为可持久化 StageCheckpoint。
/// Session checkpoint factory that pins a stage execution result into a persistable StageCheckpoint.
/// </summary>
public sealed class SessionCheckpointFactory
{
    /// <summary>
    /// 创建 Stage checkpoint。
    /// Creates a stage checkpoint.
    /// </summary>
    public StageCheckpoint Create(
        SessionOrchestrationStep step,
        StageExecutionState state,
        DateTimeOffset startedAt,
        DateTimeOffset? completedAt = null,
        string? summary = null,
        StructuredValue? output = null,
        IReadOnlyList<ArtifactRef>? artifactRefs = null,
        StructuredValue? diagnostics = null,
        IReadOnlyList<string>? nextStageSuggestions = null)
    {
        ArgumentNullException.ThrowIfNull(step);

        return new StageCheckpoint(
            BuildStableId("checkpoint", step.Decision.DecisionId, step.Stage.Id),
            step.Stage.Id,
            state,
            startedAt,
            completedAt,
            summary: summary,
            output: output,
            artifactRefs: artifactRefs,
            modelRouteKind: step.Stage.ModelRouteKind,
            diagnostics: diagnostics,
            nextStageSuggestions: nextStageSuggestions);
    }

    private static string BuildStableId(string prefix, string correlationId, string stageId)
        => $"{prefix}-{Normalize(correlationId) ?? "session"}-{stageId}";

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
