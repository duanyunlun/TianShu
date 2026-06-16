using System.Text.Json;
using TianShu.Execution.Runtime;

namespace TianShu.AppHost.State;

/// <summary>
/// 负责 Kernel 运行时模型与 AppHost.State rollout 持久化模型之间的双向映射。
/// Handles the bidirectional mapping between kernel runtime models and AppHost.State rollout persistence models.
/// </summary>
internal static class KernelRolloutStateMapper
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static KernelRolloutThreadRecord ToRolloutThreadRecord(
        KernelThreadRecord record,
        KernelThreadConfigSnapshot? configSnapshotOverride = null)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new KernelRolloutThreadRecord
        {
            ThreadId = record.Id,
            Cwd = record.Cwd,
            ForkedFromThreadId = record.ForkedFromThreadId,
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt,
            IsArchived = record.IsArchived,
            ConfigSnapshotPayload = SerializeConfigSnapshot(configSnapshotOverride ?? record.ConfigSnapshot),
            PendingInputStatePayload = SerializePendingInputState(record.PendingInputState),
            SessionState = ToRolloutSessionState(record.SessionState),
            SeedHistoryPayloads = SerializeSeedHistory(record.SeedHistory).ToList(),
            Turns = record.Turns.Select(ToRolloutTurnRecord).ToList(),
        };
    }

    public static KernelRolloutTurnRecord ToRolloutTurnRecord(KernelTurnRecord turn)
    {
        ArgumentNullException.ThrowIfNull(turn);

        return new KernelRolloutTurnRecord
        {
            Id = turn.Id,
            StartedAt = turn.StartedAt,
            CompletedAt = turn.CompletedAt,
            Status = turn.Status,
            UserMessage = turn.UserMessage,
            AssistantMessage = turn.AssistantMessage,
            InteractionEnvelope = turn.InteractionEnvelope,
            Items = turn.Items.Select(ToRolloutTurnItemRecord).ToList(),
            Error = ToRolloutTurnErrorRecord(turn.Error),
            IsContextCompaction = turn.IsContextCompaction,
        };
    }

    public static KernelRolloutTurnItemRecord ToRolloutTurnItemRecord(KernelTurnItemRecord item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new KernelRolloutTurnItemRecord
        {
            Id = item.Id,
            Type = item.Type,
            Payload = item.Payload.Clone(),
        };
    }

    public static KernelRolloutTurnErrorRecord? ToRolloutTurnErrorRecord(KernelTurnErrorRecord? error)
        => error is null
            ? null
            : new KernelRolloutTurnErrorRecord
            {
                Message = error.Message,
                AdditionalDetails = error.AdditionalDetails,
            };

    public static IReadOnlyList<JsonElement> SerializeSeedHistory(IReadOnlyList<KernelConversationHistoryItem> history)
    {
        ArgumentNullException.ThrowIfNull(history);

        if (history.Count == 0)
        {
            return Array.Empty<JsonElement>();
        }

        var payloads = new List<JsonElement>(history.Count);
        foreach (var item in history)
        {
            var payload = KernelConversationHistoryUtilities.SerializeHistoryItem(item);
            payloads.Add(payload is JsonElement json
                ? json.Clone()
                : JsonSerializer.SerializeToElement(payload, SerializerOptions));
        }

        return payloads;
    }

    public static JsonElement? SerializePendingInputState(KernelPendingInputStateRecord? pendingInputState)
    {
        var normalized = KernelPendingInputStateFactory.ToPersistedState(pendingInputState);
        return JsonSerializer.SerializeToElement(normalized, SerializerOptions);
    }

    public static KernelThreadRecord FromRolloutThreadRecord(KernelRolloutThreadRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var mapped = new KernelThreadRecord
        {
            Id = record.ThreadId,
            Cwd = record.Cwd,
            ForkedFromThreadId = record.ForkedFromThreadId,
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt,
            MemoryMode = "enabled",
            StatusType = "idle",
            ActiveFlags = [],
            Turns = record.Turns.Select(FromRolloutTurnRecord).ToList(),
            SeedHistory = DeserializeSeedHistory(record.SeedHistoryPayloads),
            ConfigSnapshot = DeserializeConfigSnapshot(record.ConfigSnapshotPayload),
            PendingInputState = DeserializePendingInputState(record.PendingInputStatePayload),
            SessionState = FromRolloutSessionState(record.SessionState),
            IsArchived = record.IsArchived,
        };

        RefreshDerivedState(mapped);
        return mapped;
    }

    private static KernelThreadConfigSnapshot? DeserializeConfigSnapshot(JsonElement? payload)
    {
        if (payload is null)
        {
            return null;
        }

        return KernelThreadConfigSnapshotFactory.TryRead(payload.Value, out var snapshot)
            ? snapshot
            : null;
    }

    private static KernelPendingInputStateRecord? DeserializePendingInputState(JsonElement? payload)
    {
        if (payload is null)
        {
            return null;
        }

        return KernelPendingInputStateFactory.TryRead(payload.Value, out var state)
            ? state
            : null;
    }

    private static List<KernelConversationHistoryItem> DeserializeSeedHistory(IReadOnlyList<JsonElement> payloads)
    {
        if (payloads.Count == 0)
        {
            return [];
        }

        var items = new List<KernelConversationHistoryItem>(payloads.Count);
        foreach (var payload in payloads)
        {
            var parsed = KernelConversationHistoryUtilities.ParseHistoryItem(payload);
            if (parsed is not null)
            {
                items.Add(parsed);
            }
        }

        return items;
    }

    private static KernelTurnRecord FromRolloutTurnRecord(KernelRolloutTurnRecord turn)
    {
        ArgumentNullException.ThrowIfNull(turn);

        return new KernelTurnRecord
        {
            Id = turn.Id,
            StartedAt = turn.StartedAt,
            CompletedAt = turn.CompletedAt,
            Status = turn.Status,
            UserMessage = turn.UserMessage,
            AssistantMessage = turn.AssistantMessage,
            InteractionEnvelope = turn.InteractionEnvelope,
            Items = turn.Items.Select(FromRolloutTurnItemRecord).ToList(),
            Error = FromRolloutTurnErrorRecord(turn.Error),
            IsContextCompaction = turn.IsContextCompaction,
        };
    }

    private static KernelTurnItemRecord FromRolloutTurnItemRecord(KernelRolloutTurnItemRecord item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new KernelTurnItemRecord
        {
            Id = item.Id,
            Type = item.Type,
            Payload = item.Payload.Clone(),
        };
    }

    private static KernelTurnErrorRecord? FromRolloutTurnErrorRecord(KernelRolloutTurnErrorRecord? error)
        => error is null
            ? null
            : new KernelTurnErrorRecord
            {
                Message = error.Message,
                AdditionalDetails = error.AdditionalDetails,
            };

    private static JsonElement? SerializeConfigSnapshot(KernelThreadConfigSnapshot? snapshot)
        => snapshot is null
            ? null
            : JsonSerializer.SerializeToElement(snapshot, SerializerOptions);

    private static KernelRolloutSessionProjectionStateRecord? ToRolloutSessionState(KernelThreadSessionProjectionStateRecord? state)
        => state is null
            ? null
            : new KernelRolloutSessionProjectionStateRecord
            {
                SessionId = state.SessionId,
                Title = state.Title,
                CollaborationSpaceId = state.CollaborationSpaceId,
                CollaborationSpaceKey = state.CollaborationSpaceKey,
                CollaborationSpaceDisplayName = state.CollaborationSpaceDisplayName,
                SessionMode = state.SessionMode,
                IsClosed = state.IsClosed,
                ActiveThreadId = state.ActiveThreadId,
                HasActiveTurn = state.HasActiveTurn,
                Orchestration = ToRolloutOrchestrationState(state.Orchestration),
            };

    private static KernelThreadSessionProjectionStateRecord? FromRolloutSessionState(KernelRolloutSessionProjectionStateRecord? state)
        => state is null
            ? null
            : new KernelThreadSessionProjectionStateRecord
            {
                SessionId = state.SessionId,
                Title = state.Title,
                CollaborationSpaceId = state.CollaborationSpaceId,
                CollaborationSpaceKey = state.CollaborationSpaceKey,
                CollaborationSpaceDisplayName = state.CollaborationSpaceDisplayName,
                SessionMode = state.SessionMode,
                IsClosed = state.IsClosed,
                ActiveThreadId = state.ActiveThreadId,
                HasActiveTurn = state.HasActiveTurn,
                Orchestration = FromRolloutOrchestrationState(state.Orchestration),
            };

    private static KernelRolloutOrchestrationStateRecord? ToRolloutOrchestrationState(KernelThreadOrchestrationStateRecord? source)
    {
        var state = KernelThreadOrchestrationStateNormalizer.Clone(source);
        if (state is null)
        {
            return null;
        }

        return new KernelRolloutOrchestrationStateRecord
        {
            CurrentStageId = state.CurrentStageId,
            LastDecision = state.LastDecision is null
                ? null
                : new KernelRolloutOrchestratorDecisionStateRecord
                {
                    DecisionId = state.LastDecision.DecisionId,
                    SelectedStageId = state.LastDecision.SelectedStageId,
                    CandidateStageIds = state.LastDecision.CandidateStageIds.ToList(),
                    ReasonCode = state.LastDecision.ReasonCode,
                    PreviousStageId = state.LastDecision.PreviousStageId,
                    ContextProjectionReason = state.LastDecision.ContextProjectionReason,
                    PolicyHits = state.LastDecision.PolicyHits.ToList(),
                    DecidedAt = state.LastDecision.DecidedAt,
                },
            LastContextPackage = state.LastContextPackage is null
                ? null
                : new KernelRolloutStageContextPackageStateRecord
                {
                    PackageId = state.LastContextPackage.PackageId,
                    StageId = state.LastContextPackage.StageId,
                    ProjectionMode = state.LastContextPackage.ProjectionMode,
                    BudgetTokens = state.LastContextPackage.BudgetTokens,
                    SourceCheckpointIds = state.LastContextPackage.SourceCheckpointIds.ToList(),
                    SegmentCount = state.LastContextPackage.SegmentCount,
                    ArtifactRefCount = state.LastContextPackage.ArtifactRefCount,
                    Segments = state.LastContextPackage.Segments
                        .Select(static segment => new KernelRolloutStageContextSegmentStateRecord
                        {
                            Kind = segment.Kind,
                            Content = segment.Content,
                            Title = segment.Title,
                            Source = segment.Source is null
                                ? null
                                : new KernelRolloutResourceRefStateRecord
                                {
                                    Kind = segment.Source.Kind,
                                    Key = segment.Source.Key,
                                },
                            Required = segment.Required,
                            EstimatedTokens = segment.EstimatedTokens,
                        })
                        .ToList(),
                    ArtifactRefs = state.LastContextPackage.ArtifactRefs
                        .Select(static artifact => new KernelRolloutArtifactRefStateRecord
                        {
                            Id = artifact.Id,
                            Name = artifact.Name,
                            Kind = artifact.Kind,
                        })
                        .ToList(),
                    ProjectionReport = state.LastContextPackage.ProjectionReport,
                    Metadata = state.LastContextPackage.Metadata,
                },
            ContextLedgerSegments = state.ContextLedgerSegments
                .Select(static segment => new KernelRolloutStageContextSegmentStateRecord
                {
                    Kind = segment.Kind,
                    Content = segment.Content,
                    Title = segment.Title,
                    Source = segment.Source is null
                        ? null
                        : new KernelRolloutResourceRefStateRecord
                        {
                            Kind = segment.Source.Kind,
                            Key = segment.Source.Key,
                        },
                    Required = segment.Required,
                    EstimatedTokens = segment.EstimatedTokens,
                })
                .ToList(),
            Checkpoints = state.Checkpoints
                .Select(static checkpoint => new KernelRolloutStageCheckpointStateRecord
                {
                    CheckpointId = checkpoint.CheckpointId,
                    StageId = checkpoint.StageId,
                    State = checkpoint.State,
                    StartedAt = checkpoint.StartedAt,
                    CompletedAt = checkpoint.CompletedAt,
                    Summary = checkpoint.Summary,
                    ArtifactRefs = checkpoint.ArtifactRefs
                        .Select(static artifact => new KernelRolloutArtifactRefStateRecord
                        {
                            Id = artifact.Id,
                            Name = artifact.Name,
                            Kind = artifact.Kind,
                        })
                        .ToList(),
                    ModelRouteSetId = checkpoint.ModelRouteSetId,
                    ModelRouteKind = checkpoint.ModelRouteKind,
                    Diagnostics = checkpoint.Diagnostics,
                    NextStageSuggestions = checkpoint.NextStageSuggestions.ToList(),
                })
                .ToList(),
        };
    }

    private static KernelThreadOrchestrationStateRecord? FromRolloutOrchestrationState(KernelRolloutOrchestrationStateRecord? state)
    {
        if (state is null)
        {
            return null;
        }

        return KernelThreadOrchestrationStateNormalizer.Clone(new KernelThreadOrchestrationStateRecord
        {
            CurrentStageId = state.CurrentStageId,
            LastDecision = state.LastDecision is null
                ? null
                : new KernelThreadOrchestratorDecisionStateRecord
                {
                    DecisionId = state.LastDecision.DecisionId,
                    SelectedStageId = state.LastDecision.SelectedStageId,
                    CandidateStageIds = state.LastDecision.CandidateStageIds.ToList(),
                    ReasonCode = state.LastDecision.ReasonCode,
                    PreviousStageId = state.LastDecision.PreviousStageId,
                    ContextProjectionReason = state.LastDecision.ContextProjectionReason,
                    PolicyHits = state.LastDecision.PolicyHits.ToList(),
                    DecidedAt = state.LastDecision.DecidedAt,
                },
            LastContextPackage = state.LastContextPackage is null
                ? null
                : new KernelThreadStageContextPackageStateRecord
                {
                    PackageId = state.LastContextPackage.PackageId,
                    StageId = state.LastContextPackage.StageId,
                    ProjectionMode = state.LastContextPackage.ProjectionMode,
                    BudgetTokens = state.LastContextPackage.BudgetTokens,
                    SourceCheckpointIds = state.LastContextPackage.SourceCheckpointIds.ToList(),
                    SegmentCount = state.LastContextPackage.SegmentCount,
                    ArtifactRefCount = state.LastContextPackage.ArtifactRefCount,
                    Segments = state.LastContextPackage.Segments
                        .Select(static segment => new KernelThreadStageContextSegmentStateRecord
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
                        })
                        .ToList(),
                    ArtifactRefs = state.LastContextPackage.ArtifactRefs
                        .Select(static artifact => new KernelThreadArtifactRefStateRecord
                        {
                            Id = artifact.Id,
                            Name = artifact.Name,
                            Kind = artifact.Kind,
                        })
                        .ToList(),
                    ProjectionReport = state.LastContextPackage.ProjectionReport,
                    Metadata = state.LastContextPackage.Metadata,
                },
            ContextLedgerSegments = state.ContextLedgerSegments
                .Select(static segment => new KernelThreadStageContextSegmentStateRecord
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
                })
                .ToList(),
            Checkpoints = state.Checkpoints
                .Select(static checkpoint => new KernelThreadStageCheckpointStateRecord
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
                            Id = artifact.Id,
                            Name = artifact.Name,
                            Kind = artifact.Kind,
                        })
                        .ToList(),
                    ModelRouteSetId = checkpoint.ModelRouteSetId,
                    ModelRouteKind = checkpoint.ModelRouteKind,
                    Diagnostics = checkpoint.Diagnostics,
                    NextStageSuggestions = checkpoint.NextStageSuggestions.ToList(),
                })
                .ToList(),
        });
    }

    private static void RefreshDerivedState(KernelThreadRecord record)
    {
        var lastTurn = record.Turns.LastOrDefault();
        record.LastUserMessage = lastTurn?.UserMessage;
        record.LastAssistantMessage = lastTurn?.AssistantMessage;
        record.StatusType = lastTurn is not null && !IsTerminalTurnStatus(lastTurn.Status)
            ? "active"
            : "idle";
        record.ActiveFlags = [];
        if (record.CreatedAt == default)
        {
            record.CreatedAt = record.UpdatedAt == default ? DateTimeOffset.UtcNow : record.UpdatedAt;
        }

        if (record.UpdatedAt == default)
        {
            record.UpdatedAt = lastTurn is null
                ? record.CreatedAt
                : IsTerminalTurnStatus(lastTurn.Status)
                    ? lastTurn.CompletedAt
                    : lastTurn.StartedAt;
        }
    }

    private static bool IsTerminalTurnStatus(string? status)
        => string.Equals(ReadNormalizedStatus(status), "completed", StringComparison.Ordinal)
           || string.Equals(ReadNormalizedStatus(status), "failed", StringComparison.Ordinal)
           || string.Equals(ReadNormalizedStatus(status), "interrupted", StringComparison.Ordinal);

    private static string? ReadNormalizedStatus(string? status)
        => string.IsNullOrWhiteSpace(status) ? null : status.Trim();
}
