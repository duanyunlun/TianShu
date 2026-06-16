using System.Text.Json;

namespace TianShu.Execution.Runtime;

/// <summary>
/// 线程投影 payload 工厂，负责把线程 session/orchestration 状态映射为 runtime protocol payload。
/// Thread projection payload factory that maps thread session/orchestration state to runtime protocol payloads.
/// </summary>
internal static class KernelThreadProjectionPayloadFactory
{
    public static KernelThreadSessionProjectionPayload? ToSessionProjectionPayload(
        KernelThreadSessionProjectionStateRecord? sessionState)
    {
        if (sessionState is null)
        {
            return null;
        }

        return new KernelThreadSessionProjectionPayload(
            SessionId: sessionState.SessionId,
            Title: sessionState.Title,
            CollaborationSpaceId: sessionState.CollaborationSpaceId,
            CollaborationSpaceKey: sessionState.CollaborationSpaceKey,
            CollaborationSpaceDisplayName: sessionState.CollaborationSpaceDisplayName,
            SessionMode: sessionState.SessionMode,
            IsClosed: sessionState.IsClosed,
            ActiveThreadId: sessionState.ActiveThreadId,
            HasActiveTurn: sessionState.HasActiveTurn,
            Orchestration: ToOrchestrationProjectionPayload(sessionState.Orchestration));
    }

    public static KernelThreadOrchestrationProjectionPayload? ToOrchestrationProjectionPayload(
        KernelThreadOrchestrationStateRecord? orchestration)
    {
        var normalized = KernelThreadOrchestrationStateNormalizer.Clone(orchestration);
        if (normalized is null)
        {
            return null;
        }

        return new KernelThreadOrchestrationProjectionPayload(
            CurrentStageId: normalized.CurrentStageId,
            LastDecision: normalized.LastDecision is null
                ? null
                : new KernelThreadOrchestratorDecisionProjectionPayload(
                    DecisionId: normalized.LastDecision.DecisionId,
                    SelectedStageId: normalized.LastDecision.SelectedStageId,
                    CandidateStageIds: normalized.LastDecision.CandidateStageIds,
                    ReasonCode: normalized.LastDecision.ReasonCode,
                    PreviousStageId: normalized.LastDecision.PreviousStageId,
                    ContextProjectionReason: normalized.LastDecision.ContextProjectionReason,
                    PolicyHits: normalized.LastDecision.PolicyHits,
                    DecidedAt: normalized.LastDecision.DecidedAt),
            LastContextPackage: normalized.LastContextPackage is null
                ? null
                : new KernelThreadStageContextPackageProjectionPayload(
                    PackageId: normalized.LastContextPackage.PackageId,
                    StageId: normalized.LastContextPackage.StageId,
                    ProjectionMode: normalized.LastContextPackage.ProjectionMode.ToString(),
                    BudgetTokens: normalized.LastContextPackage.BudgetTokens,
                    SourceCheckpointIds: normalized.LastContextPackage.SourceCheckpointIds,
                    SegmentCount: normalized.LastContextPackage.SegmentCount,
                    ArtifactRefCount: normalized.LastContextPackage.ArtifactRefCount),
            ContextLedgerSegments: normalized.ContextLedgerSegments
                .Select(static segment => new KernelThreadStageContextSegmentProjectionPayload(
                    Kind: segment.Kind,
                    Content: segment.Content,
                    Title: segment.Title,
                    Source: segment.Source is null
                        ? null
                        : new KernelThreadResourceRefProjectionPayload(segment.Source.Kind, segment.Source.Key),
                    Required: segment.Required,
                    EstimatedTokens: segment.EstimatedTokens))
                .ToArray(),
            Checkpoints: normalized.Checkpoints
                .Select(static checkpoint => new KernelThreadStageCheckpointProjectionPayload(
                    CheckpointId: checkpoint.CheckpointId,
                    StageId: checkpoint.StageId,
                    State: checkpoint.State.ToString(),
                    StartedAt: checkpoint.StartedAt,
                    CompletedAt: checkpoint.CompletedAt,
                    Summary: checkpoint.Summary,
                    ArtifactRefs: checkpoint.ArtifactRefs
                        .Select(static artifact => new KernelThreadArtifactRefProjectionPayload(
                            artifact.Id,
                            artifact.Name,
                            artifact.Kind))
                        .ToArray(),
                    ModelRouteSetId: checkpoint.ModelRouteSetId,
                    ModelRouteKind: checkpoint.ModelRouteKind,
                    Diagnostics: checkpoint.Diagnostics is null
                        ? null
                        : JsonSerializer.SerializeToElement(checkpoint.Diagnostics),
                    NextStageSuggestions: checkpoint.NextStageSuggestions))
                .ToArray());
    }
}
