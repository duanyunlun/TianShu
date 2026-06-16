using System.Text.Json;
using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Interactions;
using TianShu.Contracts.Primitives;

namespace TianShu.Execution.Runtime;

/// <summary>
/// 线程持久化记录模型。
/// Persisted thread record model shared between runtime state and thread store.
/// </summary>
internal sealed class KernelThreadRecord
{
    public string Id { get; set; } = string.Empty;

    public string? Cwd { get; set; }

    public string MemoryMode { get; set; } = "enabled";

    public string? ForkedFromThreadId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string? LastUserMessage { get; set; }

    public string? LastAssistantMessage { get; set; }

    public string? Name { get; set; }

    public string? AgentNickname { get; set; }

    public string? AgentRole { get; set; }

    public bool IsArchived { get; set; }

    public string StatusType { get; set; } = "idle";

    public List<string> ActiveFlags { get; set; } = [];

    public KernelGitInfoRecord? GitInfo { get; set; }

    public List<KernelTurnRecord> Turns { get; set; } = [];

    public List<KernelConversationHistoryItem> SeedHistory { get; set; } = [];

    public KernelThreadConfigSnapshot? ConfigSnapshot { get; set; }

    public KernelPendingInputStateRecord? PendingInputState { get; set; }

    public KernelThreadSessionProjectionStateRecord? SessionState { get; set; }
}

internal sealed class KernelThreadSessionProjectionStateRecord
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

    public KernelThreadOrchestrationStateRecord? Orchestration { get; set; }
}

/// <summary>
/// 线程会话编排状态，保存核心循环可恢复的账本、最近投影和检查点摘要。
/// Thread session orchestration state that persists the recoverable ledger, latest projection, and checkpoint summaries for the core loop.
/// </summary>
internal sealed class KernelThreadOrchestrationStateRecord
{
    public string? CurrentStageId { get; set; }

    public KernelThreadOrchestratorDecisionStateRecord? LastDecision { get; set; }

    public KernelThreadStageContextPackageStateRecord? LastContextPackage { get; set; }

    public List<KernelThreadStageContextSegmentStateRecord> ContextLedgerSegments { get; set; } = [];

    public List<KernelThreadStageCheckpointStateRecord> Checkpoints { get; set; } = [];
}

internal sealed class KernelThreadOrchestratorDecisionStateRecord
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

internal sealed class KernelThreadStageContextPackageStateRecord
{
    public string PackageId { get; set; } = string.Empty;

    public string StageId { get; set; } = string.Empty;

    public StageContextProjectionMode ProjectionMode { get; set; } = StageContextProjectionMode.SelectedSegments;

    public int? BudgetTokens { get; set; }

    public List<string> SourceCheckpointIds { get; set; } = [];

    public int SegmentCount { get; set; }

    public int ArtifactRefCount { get; set; }

    public List<KernelThreadStageContextSegmentStateRecord> Segments { get; set; } = [];

    public List<KernelThreadArtifactRefStateRecord> ArtifactRefs { get; set; } = [];

    public StructuredValue? ProjectionReport { get; set; }

    public StructuredValue? Metadata { get; set; }
}

internal sealed class KernelThreadStageContextSegmentStateRecord
{
    public string Kind { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string? Title { get; set; }

    public KernelThreadResourceRefStateRecord? Source { get; set; }

    public bool Required { get; set; }

    public int? EstimatedTokens { get; set; }
}

internal sealed class KernelThreadStageCheckpointStateRecord
{
    public string CheckpointId { get; set; } = string.Empty;

    public string StageId { get; set; } = string.Empty;

    public StageExecutionState State { get; set; } = StageExecutionState.Pending;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? Summary { get; set; }

    public List<KernelThreadArtifactRefStateRecord> ArtifactRefs { get; set; } = [];

    public string? ModelRouteSetId { get; set; }

    public string? ModelRouteKind { get; set; }

    public StructuredValue? Diagnostics { get; set; }

    public List<string> NextStageSuggestions { get; set; } = [];
}

internal sealed class KernelThreadArtifactRefStateRecord
{
    public string Id { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string? Kind { get; set; }
}

internal sealed class KernelThreadResourceRefStateRecord
{
    public string Kind { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;
}

internal static class KernelThreadOrchestrationStateNormalizer
{
    public static KernelThreadOrchestrationStateRecord? Clone(KernelThreadOrchestrationStateRecord? source)
    {
        if (source is null)
        {
            return null;
        }

        var cloned = new KernelThreadOrchestrationStateRecord
        {
            CurrentStageId = Normalize(source.CurrentStageId),
            LastDecision = CloneDecision(source.LastDecision),
            LastContextPackage = CloneContextPackage(source.LastContextPackage),
            ContextLedgerSegments = source.ContextLedgerSegments
                .Select(CloneSegment)
                .Where(static item => item is not null)
                .Cast<KernelThreadStageContextSegmentStateRecord>()
                .ToList(),
            Checkpoints = source.Checkpoints
                .Select(CloneCheckpoint)
                .Where(static item => item is not null)
                .Cast<KernelThreadStageCheckpointStateRecord>()
                .ToList(),
        };

        return IsEmpty(cloned) ? null : cloned;
    }

    public static KernelThreadOrchestrationStateRecord ApplyOrchestrationStep(
        KernelThreadOrchestrationStateRecord? source,
        OrchestratorDecision decision,
        StageContextPackage contextPackage)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(contextPackage);

        var next = Clone(source) ?? new KernelThreadOrchestrationStateRecord();
        next.CurrentStageId = decision.SelectedStageId;
        next.LastDecision = FromDecision(decision);
        next.LastContextPackage = FromContextPackage(contextPackage);
        next.ContextLedgerSegments = MergeContextLedgerSegments(next.ContextLedgerSegments, contextPackage.Segments);
        return next;
    }

    public static KernelThreadOrchestrationStateRecord ApplyCheckpoint(
        KernelThreadOrchestrationStateRecord? source,
        StageCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        var next = Clone(source) ?? new KernelThreadOrchestrationStateRecord();
        next.CurrentStageId = checkpoint.StageId;
        var checkpointState = FromCheckpoint(checkpoint);
        var existingIndex = next.Checkpoints.FindIndex(item => string.Equals(item.CheckpointId, checkpoint.CheckpointId, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            next.Checkpoints[existingIndex] = checkpointState;
        }
        else
        {
            next.Checkpoints.Add(checkpointState);
        }

        UpsertCheckpointLedgerSegment(next.ContextLedgerSegments, checkpointState);
        return next;
    }

    private static KernelThreadOrchestratorDecisionStateRecord? CloneDecision(KernelThreadOrchestratorDecisionStateRecord? source)
    {
        if (source is null || string.IsNullOrWhiteSpace(source.DecisionId) || string.IsNullOrWhiteSpace(source.SelectedStageId))
        {
            return null;
        }

        return new KernelThreadOrchestratorDecisionStateRecord
        {
            DecisionId = source.DecisionId.Trim(),
            SelectedStageId = source.SelectedStageId.Trim(),
            CandidateStageIds = NormalizeIdentifierList(source.CandidateStageIds),
            ReasonCode = Normalize(source.ReasonCode) ?? "unspecified",
            PreviousStageId = Normalize(source.PreviousStageId),
            ContextProjectionReason = Normalize(source.ContextProjectionReason),
            PolicyHits = NormalizeIdentifierList(source.PolicyHits),
            DecidedAt = source.DecidedAt == default ? DateTimeOffset.UtcNow : source.DecidedAt,
        };
    }

    private static KernelThreadStageContextPackageStateRecord? CloneContextPackage(KernelThreadStageContextPackageStateRecord? source)
    {
        if (source is null || string.IsNullOrWhiteSpace(source.PackageId) || string.IsNullOrWhiteSpace(source.StageId))
        {
            return null;
        }

        return new KernelThreadStageContextPackageStateRecord
        {
            PackageId = source.PackageId.Trim(),
            StageId = source.StageId.Trim(),
            ProjectionMode = source.ProjectionMode,
            BudgetTokens = source.BudgetTokens < 0 ? null : source.BudgetTokens,
            SourceCheckpointIds = NormalizeIdentifierList(source.SourceCheckpointIds),
            SegmentCount = Math.Max(0, source.SegmentCount),
            ArtifactRefCount = Math.Max(0, source.ArtifactRefCount),
            Segments = source.Segments
                .Select(CloneSegment)
                .Where(static item => item is not null)
                .Cast<KernelThreadStageContextSegmentStateRecord>()
                .ToList(),
            ArtifactRefs = source.ArtifactRefs
                .Select(CloneArtifactRef)
                .Where(static item => item is not null)
                .Cast<KernelThreadArtifactRefStateRecord>()
                .ToList(),
            ProjectionReport = source.ProjectionReport,
            Metadata = source.Metadata,
        };
    }

    private static KernelThreadStageContextSegmentStateRecord? CloneSegment(KernelThreadStageContextSegmentStateRecord? source)
    {
        if (source is null || string.IsNullOrWhiteSpace(source.Kind) || string.IsNullOrWhiteSpace(source.Content))
        {
            return null;
        }

        return new KernelThreadStageContextSegmentStateRecord
        {
            Kind = source.Kind.Trim(),
            Content = source.Content.Trim(),
            Title = Normalize(source.Title),
            Source = CloneResourceRef(source.Source),
            Required = source.Required,
            EstimatedTokens = source.EstimatedTokens < 0 ? null : source.EstimatedTokens,
        };
    }

    private static KernelThreadStageCheckpointStateRecord? CloneCheckpoint(KernelThreadStageCheckpointStateRecord? source)
    {
        if (source is null || string.IsNullOrWhiteSpace(source.CheckpointId) || string.IsNullOrWhiteSpace(source.StageId))
        {
            return null;
        }

        return new KernelThreadStageCheckpointStateRecord
        {
            CheckpointId = source.CheckpointId.Trim(),
            StageId = source.StageId.Trim(),
            State = source.State,
            StartedAt = source.StartedAt == default ? DateTimeOffset.UtcNow : source.StartedAt,
            CompletedAt = source.CompletedAt,
            Summary = Normalize(source.Summary),
            ArtifactRefs = source.ArtifactRefs
                .Select(CloneArtifactRef)
                .Where(static item => item is not null)
                .Cast<KernelThreadArtifactRefStateRecord>()
                .ToList(),
            ModelRouteSetId = Normalize(source.ModelRouteSetId),
            ModelRouteKind = Normalize(source.ModelRouteKind),
            Diagnostics = source.Diagnostics,
            NextStageSuggestions = NormalizeIdentifierList(source.NextStageSuggestions),
        };
    }

    private static KernelThreadOrchestratorDecisionStateRecord FromDecision(OrchestratorDecision decision)
        => new()
        {
            DecisionId = decision.DecisionId,
            SelectedStageId = decision.SelectedStageId,
            CandidateStageIds = decision.CandidateStageIds.ToList(),
            ReasonCode = decision.ReasonCode,
            PreviousStageId = decision.PreviousStageId,
            ContextProjectionReason = decision.ContextProjectionReason,
            PolicyHits = decision.PolicyHits.ToList(),
            DecidedAt = decision.DecidedAt,
        };

    private static KernelThreadStageContextPackageStateRecord FromContextPackage(StageContextPackage package)
        => new()
        {
            PackageId = package.PackageId,
            StageId = package.StageId,
            ProjectionMode = package.ProjectionMode,
            BudgetTokens = package.BudgetTokens,
            SourceCheckpointIds = package.SourceCheckpointIds.ToList(),
            SegmentCount = package.Segments.Count,
            ArtifactRefCount = package.ArtifactRefs.Count,
            Segments = package.Segments
                .Select(FromSegment)
                .ToList(),
            ArtifactRefs = package.ArtifactRefs
                .Select(static artifact => new KernelThreadArtifactRefStateRecord
                {
                    Id = artifact.Id.Value,
                    Name = artifact.Name,
                    Kind = artifact.Kind,
                })
                .ToList(),
            ProjectionReport = package.ProjectionReport,
            Metadata = package.Metadata.Count == 0
                ? null
                : StructuredValue.FromPlainObject(package.Metadata.Entries),
        };

    private static KernelThreadStageContextSegmentStateRecord FromSegment(StageContextSegment segment)
        => new()
        {
            Kind = segment.Kind,
            Content = segment.Content,
            Title = segment.Title,
            Source = segment.Source is null
                ? null
                : new KernelThreadResourceRefStateRecord
                {
                    Kind = segment.Source.Kind,
                    Key = segment.Source.Key,
                },
            Required = segment.Required,
            EstimatedTokens = segment.EstimatedTokens,
        };

    private static List<KernelThreadStageContextSegmentStateRecord> MergeContextLedgerSegments(
        List<KernelThreadStageContextSegmentStateRecord> existingSegments,
        IReadOnlyList<StageContextSegment> projectedSegments)
    {
        var merged = existingSegments
            .Select(CloneSegment)
            .Where(static item => item is not null)
            .Cast<KernelThreadStageContextSegmentStateRecord>()
            .ToList();

        foreach (var segment in projectedSegments.Select(FromSegment))
        {
            UpsertContextLedgerSegment(merged, segment);
        }

        return merged;
    }

    private static void UpsertContextLedgerSegment(
        List<KernelThreadStageContextSegmentStateRecord> segments,
        KernelThreadStageContextSegmentStateRecord segment)
    {
        var existingIndex = segments.FindIndex(item => IsSameContextSegment(item, segment));
        if (existingIndex >= 0)
        {
            segments[existingIndex] = segment;
            return;
        }

        segments.Add(segment);
    }

    private static bool IsSameContextSegment(
        KernelThreadStageContextSegmentStateRecord left,
        KernelThreadStageContextSegmentStateRecord right)
    {
        if (left.Source is not null && right.Source is not null)
        {
            return string.Equals(left.Source.Kind, right.Source.Kind, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(left.Source.Key, right.Source.Key, StringComparison.Ordinal);
        }

        return string.Equals(left.Kind, right.Kind, StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.Title, right.Title, StringComparison.Ordinal)
               && string.Equals(left.Content, right.Content, StringComparison.Ordinal);
    }

    private static KernelThreadStageCheckpointStateRecord FromCheckpoint(StageCheckpoint checkpoint)
        => new()
        {
            CheckpointId = checkpoint.CheckpointId,
            StageId = checkpoint.StageId,
            State = checkpoint.State,
            StartedAt = checkpoint.StartedAt,
            CompletedAt = checkpoint.CompletedAt,
            Summary = checkpoint.Summary,
            ArtifactRefs = checkpoint.ArtifactRefs
                .Select(static artifact => new KernelThreadArtifactRefStateRecord
                {
                    Id = artifact.Id.Value,
                    Name = artifact.Name,
                    Kind = artifact.Kind,
                })
                .ToList(),
            ModelRouteSetId = checkpoint.ModelRouteSetId,
            ModelRouteKind = checkpoint.ModelRouteKind?.Value,
            Diagnostics = checkpoint.Diagnostics,
            NextStageSuggestions = checkpoint.NextStageSuggestions.ToList(),
        };

    private static void UpsertCheckpointLedgerSegment(
        List<KernelThreadStageContextSegmentStateRecord> segments,
        KernelThreadStageCheckpointStateRecord checkpoint)
    {
        var segment = FromCheckpointLedgerSegment(checkpoint);
        var existingIndex = segments.FindIndex(item =>
            string.Equals(item.Source?.Kind, "stage_checkpoint", StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.Source?.Key, checkpoint.CheckpointId, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            segments[existingIndex] = segment;
            return;
        }

        segments.Add(segment);
    }

    private static KernelThreadStageContextSegmentStateRecord FromCheckpointLedgerSegment(
        KernelThreadStageCheckpointStateRecord checkpoint)
    {
        var content = BuildCheckpointLedgerContent(checkpoint);
        return new KernelThreadStageContextSegmentStateRecord
        {
            Kind = "stage_checkpoint",
            Title = $"Stage {checkpoint.StageId} checkpoint",
            Content = content,
            Source = new KernelThreadResourceRefStateRecord
            {
                Kind = "stage_checkpoint",
                Key = checkpoint.CheckpointId,
            },
            Required = true,
            EstimatedTokens = EstimateTokens(content),
        };
    }

    private static string BuildCheckpointLedgerContent(KernelThreadStageCheckpointStateRecord checkpoint)
    {
        var parts = new List<string>
        {
            $"stage={checkpoint.StageId}",
            $"state={checkpoint.State}",
        };
        if (!string.IsNullOrWhiteSpace(checkpoint.Summary))
        {
            parts.Add($"summary={checkpoint.Summary!.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(checkpoint.ModelRouteKind))
        {
            parts.Add($"model_route_kind={checkpoint.ModelRouteKind!.Trim()}");
        }

        if (checkpoint.ArtifactRefs.Count > 0)
        {
            parts.Add("artifacts=" + string.Join(
                ",",
                checkpoint.ArtifactRefs
                    .Select(static artifact => Normalize(artifact.Name) ?? Normalize(artifact.Id))
                    .Where(static value => value is not null)
                    .Select(static value => value!)));
        }

        if (checkpoint.NextStageSuggestions.Count > 0)
        {
            parts.Add("next=" + string.Join(",", checkpoint.NextStageSuggestions));
        }

        return string.Join("; ", parts);
    }

    private static int EstimateTokens(string content)
        => Math.Max(16, (content.Length + 3) / 4);

    private static KernelThreadArtifactRefStateRecord? CloneArtifactRef(KernelThreadArtifactRefStateRecord? source)
    {
        var id = Normalize(source?.Id);
        if (id is null)
        {
            return null;
        }

        return new KernelThreadArtifactRefStateRecord
        {
            Id = id,
            Name = Normalize(source?.Name),
            Kind = Normalize(source?.Kind),
        };
    }

    private static KernelThreadResourceRefStateRecord? CloneResourceRef(KernelThreadResourceRefStateRecord? source)
    {
        var kind = Normalize(source?.Kind);
        var key = Normalize(source?.Key);
        if (kind is null || key is null)
        {
            return null;
        }

        return new KernelThreadResourceRefStateRecord
        {
            Kind = kind,
            Key = key,
        };
    }

    private static List<string> NormalizeIdentifierList(IReadOnlyList<string>? values)
        => values is null
            ? []
            : values
                .Select(Normalize)
                .Where(static item => item is not null)
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToList();

    private static bool IsEmpty(KernelThreadOrchestrationStateRecord source)
        => string.IsNullOrWhiteSpace(source.CurrentStageId)
           && source.LastDecision is null
           && source.LastContextPackage is null
           && source.ContextLedgerSegments.Count == 0
           && source.Checkpoints.Count == 0;

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed class KernelConversationHistoryItem
{
    public string Role { get; set; } = "user";

    public string Content { get; set; } = string.Empty;

    public List<KernelConversationInputRecord> Inputs { get; set; } = [];

    public JsonElement? RawResponseItem { get; set; }
}

internal sealed class KernelConversationInputRecord
{
    public string Type { get; init; } = string.Empty;

    public string? Text { get; init; }

    public string? Url { get; init; }

    public string? Path { get; init; }

    public string? Name { get; init; }

    public IReadOnlyList<KernelConversationTextElementRecord> TextElements { get; init; }
        = Array.Empty<KernelConversationTextElementRecord>();
}

internal sealed record KernelConversationTextElementRecord(
    KernelConversationByteRangeRecord? ByteRange,
    string? Placeholder);

internal sealed record KernelConversationByteRangeRecord(
    int Start,
    int End);

internal sealed class KernelGitInfoRecord
{
    public string? Sha { get; set; }

    public string? Branch { get; set; }

    public string? OriginUrl { get; set; }
}

internal sealed class KernelTurnRecord
{
    public string Id { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset CompletedAt { get; set; }

    public string Status { get; set; } = "completed";

    public string? UserMessage { get; set; }

    public string? AssistantMessage { get; set; }

    public InteractionEnvelopeRef? InteractionEnvelope { get; set; }

    public List<KernelTurnItemRecord> Items { get; set; } = [];

    public KernelTurnErrorRecord? Error { get; set; }

    public bool IsContextCompaction { get; set; }
}

internal sealed class KernelTurnItemRecord
{
    public string Id { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public JsonElement Payload { get; set; }
}

internal sealed class KernelTurnErrorRecord
{
    public string Message { get; set; } = string.Empty;

    public string? AdditionalDetails { get; set; }
}
