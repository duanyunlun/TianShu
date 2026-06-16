using System.Text.Json;
using TianShu.Contracts.Interactions;
using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Primitives;

namespace TianShu.AppHost.State;

/// <summary>
/// 表示 rollout 文件中的线程持久化模型，仅承载宿主侧可回放状态。
/// Represents the persisted thread model inside rollout files and only carries host-side replayable state.
/// </summary>
internal sealed class KernelRolloutThreadRecord
{
    public string ThreadId { get; set; } = string.Empty;

    public string? Cwd { get; set; }

    public string? ForkedFromThreadId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsArchived { get; set; }

    public JsonElement? ConfigSnapshotPayload { get; set; }

    public JsonElement? PendingInputStatePayload { get; set; }

    public KernelRolloutSessionProjectionStateRecord? SessionState { get; set; }

    public List<JsonElement> SeedHistoryPayloads { get; set; } = [];

    public List<KernelRolloutTurnRecord> Turns { get; set; } = [];
}

internal sealed class KernelRolloutSessionProjectionStateRecord
{
    public string SessionId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string CollaborationSpaceId { get; set; } = "tianshu-runtime";

    public string CollaborationSpaceKey { get; set; } = "tianshu-runtime";

    public string CollaborationSpaceDisplayName { get; set; } = "TianShu Runtime";

    public string SessionMode { get; set; } = "interactive";

    public bool IsClosed { get; set; }

    public string? ActiveThreadId { get; set; }

    public bool HasActiveTurn { get; set; }

    public KernelRolloutOrchestrationStateRecord? Orchestration { get; set; }
}

internal sealed class KernelRolloutOrchestrationStateRecord
{
    public string? CurrentStageId { get; set; }

    public KernelRolloutOrchestratorDecisionStateRecord? LastDecision { get; set; }

    public KernelRolloutStageContextPackageStateRecord? LastContextPackage { get; set; }

    public List<KernelRolloutStageContextSegmentStateRecord> ContextLedgerSegments { get; set; } = [];

    public List<KernelRolloutStageCheckpointStateRecord> Checkpoints { get; set; } = [];
}

internal sealed class KernelRolloutOrchestratorDecisionStateRecord
{
    public string DecisionId { get; set; } = string.Empty;

    public string SelectedStageId { get; set; } = string.Empty;

    public List<string> CandidateStageIds { get; set; } = [];

    public string ReasonCode { get; set; } = string.Empty;

    public string? PreviousStageId { get; set; }

    public string? ContextProjectionReason { get; set; }

    public List<string> PolicyHits { get; set; } = [];

    public DateTimeOffset DecidedAt { get; set; }
}

internal sealed class KernelRolloutStageContextPackageStateRecord
{
    public string PackageId { get; set; } = string.Empty;

    public string StageId { get; set; } = string.Empty;

    public StageContextProjectionMode ProjectionMode { get; set; } = StageContextProjectionMode.SelectedSegments;

    public int? BudgetTokens { get; set; }

    public List<string> SourceCheckpointIds { get; set; } = [];

    public int SegmentCount { get; set; }

    public int ArtifactRefCount { get; set; }

    public List<KernelRolloutStageContextSegmentStateRecord> Segments { get; set; } = [];

    public List<KernelRolloutArtifactRefStateRecord> ArtifactRefs { get; set; } = [];

    public StructuredValue? ProjectionReport { get; set; }

    public StructuredValue? Metadata { get; set; }
}

internal sealed class KernelRolloutStageContextSegmentStateRecord
{
    public string Kind { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string? Title { get; set; }

    public KernelRolloutResourceRefStateRecord? Source { get; set; }

    public bool Required { get; set; }

    public int? EstimatedTokens { get; set; }
}

internal sealed class KernelRolloutStageCheckpointStateRecord
{
    public string CheckpointId { get; set; } = string.Empty;

    public string StageId { get; set; } = string.Empty;

    public StageExecutionState State { get; set; } = StageExecutionState.Pending;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? Summary { get; set; }

    public List<KernelRolloutArtifactRefStateRecord> ArtifactRefs { get; set; } = [];

    public string? ModelRouteSetId { get; set; }

    public string? ModelRouteKind { get; set; }

    public StructuredValue? Diagnostics { get; set; }

    public List<string> NextStageSuggestions { get; set; } = [];
}

internal sealed class KernelRolloutArtifactRefStateRecord
{
    public string Id { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string? Kind { get; set; }
}

internal sealed class KernelRolloutResourceRefStateRecord
{
    public string Kind { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;
}

/// <summary>
/// 表示 rollout 中的单次 turn 留痕记录。
/// Represents a single turn entry persisted inside rollout storage.
/// </summary>
internal sealed class KernelRolloutTurnRecord
{
    public string Id { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset CompletedAt { get; set; }

    public string Status { get; set; } = "completed";

    public string? UserMessage { get; set; }

    public string? AssistantMessage { get; set; }

    public InteractionEnvelopeRef? InteractionEnvelope { get; set; }

    public List<KernelRolloutTurnItemRecord> Items { get; set; } = [];

    public KernelRolloutTurnErrorRecord? Error { get; set; }

    public bool IsContextCompaction { get; set; }
}

/// <summary>
/// 表示 rollout 中的 turn item 记录，保留 provider-neutral 的原始 payload。
/// Represents a rollout turn item and keeps the provider-neutral raw payload.
/// </summary>
internal sealed class KernelRolloutTurnItemRecord
{
    public string Id { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public JsonElement Payload { get; set; }
}

/// <summary>
/// 表示 rollout 中的 turn error 记录。
/// Represents an error entry captured for a rollout turn.
/// </summary>
internal sealed class KernelRolloutTurnErrorRecord
{
    public string Message { get; set; } = string.Empty;

    public string? AdditionalDetails { get; set; }
}
