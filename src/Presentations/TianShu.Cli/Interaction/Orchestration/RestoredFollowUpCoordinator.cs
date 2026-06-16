using TianShu.Contracts.Conversations;

namespace TianShu.Cli.Interaction.Orchestration;

/// <summary>
/// Coordinates restored pending follow-up drafts for the interactive CLI.
/// 协调交互式 CLI 中从会话恢复得到的待处理 follow-up 草稿状态机。
/// </summary>
internal sealed class RestoredFollowUpCoordinator
{
    private readonly Queue<RestoredPendingFollowUp> pending = new();

    public RestoredPendingFollowUp? ComposerDraft { get; private set; }

    public PendingRestoredFollowUpDispatch? PendingDispatch { get; private set; }

    public int QueuedCount => pending.Count;

    public bool HasComposerDraft => ComposerDraft is not null;

    public string BuildPrompt(string defaultPrompt)
        => ComposerDraft is null
            ? defaultPrompt
            : $"{ComposerDraft.Mode.ToString().ToLowerInvariant()} draft> ";

    public RestoredFollowUpSnapshot CaptureSnapshot()
        => new(
            ComposerDraft,
            PendingDispatch,
            QueuedCount,
            GetCorrelations());

    public string[] GetCorrelations()
    {
        var correlations = new List<string>();
        if (!string.IsNullOrWhiteSpace(PendingDispatch?.Draft.CorrelationId))
        {
            correlations.Add(PendingDispatch.Draft.CorrelationId);
        }

        if (!string.IsNullOrWhiteSpace(ComposerDraft?.CorrelationId))
        {
            correlations.Add(ComposerDraft.CorrelationId);
        }

        correlations.AddRange(
            pending
                .Select(static item => item.CorrelationId)
                .Where(static item => !string.IsNullOrWhiteSpace(item)));
        return correlations.ToArray();
    }

    public RestoredFollowUpRestoreResult Restore(ControlPlanePendingInputState? pendingInputState)
    {
        Clear();
        if (pendingInputState is null)
        {
            return new RestoredFollowUpRestoreResult(0, null);
        }

        var restored = ResolveRestorablePendingFollowUps(pendingInputState)
            .Select(entry => BuildRestoredPendingFollowUp(entry, pendingInputState.SubmitPendingSteersAfterInterrupt))
            .Where(static item => item is not null)
            .Cast<RestoredPendingFollowUp>()
            .ToArray();
        foreach (var followUp in restored)
        {
            pending.Enqueue(followUp);
        }

        var promoted = restored.Length == 0 ? null : PromoteNextToComposer();
        return new RestoredFollowUpRestoreResult(restored.Length, promoted);
    }

    public RestoredPendingFollowUp? TakeEditedComposerDraft(string text)
    {
        if (ComposerDraft is null)
        {
            return null;
        }

        var editedDraft = BuildEditedRestoredPendingFollowUp(ComposerDraft, text);
        ComposerDraft = null;
        return editedDraft;
    }

    public RestoredFollowUpTakeResult TakeComposerDraftForSend()
    {
        if (ComposerDraft is null)
        {
            return PendingDispatch is null
                ? new RestoredFollowUpTakeResult(RestoredFollowUpTakeStatus.Empty, null)
                : new RestoredFollowUpTakeResult(RestoredFollowUpTakeStatus.Dispatching, null);
        }

        var draft = ComposerDraft;
        ComposerDraft = null;
        return new RestoredFollowUpTakeResult(RestoredFollowUpTakeStatus.Ready, draft);
    }

    public RestoredFollowUpDropResult DropComposerDraft()
    {
        if (ComposerDraft is null)
        {
            return PendingDispatch is null
                ? new RestoredFollowUpDropResult(RestoredFollowUpDropStatus.Empty, null, null)
                : new RestoredFollowUpDropResult(RestoredFollowUpDropStatus.Dispatching, null, null);
        }

        var dropped = ComposerDraft;
        ComposerDraft = null;
        var promoted = PromoteNextToComposer();
        return new RestoredFollowUpDropResult(RestoredFollowUpDropStatus.Dropped, dropped, promoted);
    }

    public RestoredFollowUpDispatchTransition ApplyDispatchResult(
        ControlPlaneTurnSubmissionResult result,
        RestoredPendingFollowUp draft,
        Func<string?, bool> isTerminalTurnStatus)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(isTerminalTurnStatus);

        if (!result.Accepted)
        {
            RestoreDraftAfterDispatchFailure(draft);
            return new RestoredFollowUpDispatchTransition(RestoredFollowUpDispatchTransitionKind.FailedAndRestored, null);
        }

        if (isTerminalTurnStatus(result.TurnStatus))
        {
            PendingDispatch = null;
            var promoted = PromoteNextToComposer();
            return new RestoredFollowUpDispatchTransition(RestoredFollowUpDispatchTransitionKind.CompletedAndPromoted, promoted);
        }

        PendingDispatch = new PendingRestoredFollowUpDispatch(draft, result.TurnId?.Value);
        return new RestoredFollowUpDispatchTransition(RestoredFollowUpDispatchTransitionKind.Dispatching, null);
    }

    public void RestoreDraftAfterDispatchFailure(RestoredPendingFollowUp draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        PendingDispatch = null;
        ComposerDraft ??= draft;
    }

    public RestoredPendingFollowUp? TryPromoteAfterTurnCompleted(string? turnId)
    {
        if (PendingDispatch is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(PendingDispatch.TurnId)
            && !string.Equals(PendingDispatch.TurnId, turnId, StringComparison.Ordinal))
        {
            return null;
        }

        PendingDispatch = null;
        return PromoteNextToComposer();
    }

    public void Clear()
    {
        pending.Clear();
        ComposerDraft = null;
        PendingDispatch = null;
    }

    private RestoredPendingFollowUp? PromoteNextToComposer()
    {
        if (ComposerDraft is not null)
        {
            return null;
        }

        if (pending.Count == 0)
        {
            return null;
        }

        ComposerDraft = pending.Dequeue();
        return ComposerDraft;
    }

    private static IReadOnlyList<ControlPlanePendingInputStateEntry> ResolveRestorablePendingFollowUps(ControlPlanePendingInputState pendingInputState)
    {
        var merged = new List<ControlPlanePendingInputStateEntry>();
        if (pendingInputState.Entries is { Count: > 0 } entries)
        {
            merged.AddRange(entries);
        }

        if (pendingInputState.QueuedUserMessages is { Count: > 0 } queuedUserMessages)
        {
            merged.AddRange(queuedUserMessages);
        }

        if (pendingInputState.PendingSteers is { Count: > 0 } pendingSteers)
        {
            merged.AddRange(pendingSteers);
        }

        if (merged.Count == 0)
        {
            return Array.Empty<ControlPlanePendingInputStateEntry>();
        }

        var deduped = new List<ControlPlanePendingInputStateEntry>(merged.Count);
        var seenCorrelationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in merged)
        {
            var correlationId = Normalize(entry.CorrelationId);
            if (!string.IsNullOrWhiteSpace(correlationId) && !seenCorrelationIds.Add(correlationId!))
            {
                continue;
            }

            deduped.Add(entry);
        }

        return deduped;
    }

    private static RestoredPendingFollowUp? BuildRestoredPendingFollowUp(
        ControlPlanePendingInputStateEntry entry,
        bool submitPendingSteersAfterInterrupt)
    {
        IReadOnlyList<ControlPlaneInputItem> inputs = entry.Inputs is { Count: > 0 }
            ? entry.Inputs.ToArray()
            : Array.Empty<ControlPlaneInputItem>();
        if (inputs.Count == 0)
        {
            return null;
        }

        var requestedMode = Normalize(entry.EffectiveMode) ?? Normalize(entry.RequestedMode);
        var mode = requestedMode?.ToLowerInvariant() switch
        {
            "steer" => ControlPlaneFollowUpMode.Steer,
            "interrupt" => ControlPlaneFollowUpMode.Interrupt,
            "queue" => ControlPlaneFollowUpMode.Queue,
            _ => string.Equals(entry.PendingBucket, "PendingSteer", StringComparison.OrdinalIgnoreCase)
                ? ControlPlaneFollowUpMode.Steer
                : ControlPlaneFollowUpMode.Queue,
        };
        if (submitPendingSteersAfterInterrupt
            && mode == ControlPlaneFollowUpMode.Steer
            && string.Equals(entry.PendingBucket, "PendingSteer", StringComparison.OrdinalIgnoreCase))
        {
            mode = ControlPlaneFollowUpMode.Queue;
        }

        var previewText = CliConversationInputUtilities.BuildPreview(inputs)
            ?? "<empty>";
        var correlationId = Normalize(entry.CorrelationId) ?? Guid.NewGuid().ToString("N");
        return new RestoredPendingFollowUp(
            inputs,
            mode,
            correlationId,
            previewText,
            Normalize(entry.PendingBucket) ?? "QueuedUserMessage");
    }

    private static RestoredPendingFollowUp BuildEditedRestoredPendingFollowUp(RestoredPendingFollowUp draft, string text)
    {
        var inputs = CliConversationInputUtilities.ReplaceStructuredInputs(draft.Inputs, text);
        return draft with
        {
            Inputs = inputs,
            PreviewText = CliConversationInputUtilities.BuildPreview(inputs) ?? text,
        };
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed record RestoredPendingFollowUp(
    IReadOnlyList<ControlPlaneInputItem> Inputs,
    ControlPlaneFollowUpMode Mode,
    string CorrelationId,
    string PreviewText,
    string PendingBucket);

internal sealed record PendingRestoredFollowUpDispatch(
    RestoredPendingFollowUp Draft,
    string? TurnId);

internal sealed record RestoredFollowUpSnapshot(
    RestoredPendingFollowUp? ComposerDraft,
    PendingRestoredFollowUpDispatch? PendingDispatch,
    int QueuedCount,
    IReadOnlyList<string> Correlations);

internal sealed record RestoredFollowUpRestoreResult(
    int RestoredCount,
    RestoredPendingFollowUp? PromotedDraft);

internal enum RestoredFollowUpTakeStatus
{
    Ready = 0,
    Empty = 1,
    Dispatching = 2,
}

internal sealed record RestoredFollowUpTakeResult(
    RestoredFollowUpTakeStatus Status,
    RestoredPendingFollowUp? Draft);

internal enum RestoredFollowUpDropStatus
{
    Dropped = 0,
    Empty = 1,
    Dispatching = 2,
}

internal sealed record RestoredFollowUpDropResult(
    RestoredFollowUpDropStatus Status,
    RestoredPendingFollowUp? DroppedDraft,
    RestoredPendingFollowUp? PromotedDraft);

internal enum RestoredFollowUpDispatchTransitionKind
{
    Dispatching = 0,
    CompletedAndPromoted = 1,
    FailedAndRestored = 2,
}

internal sealed record RestoredFollowUpDispatchTransition(
    RestoredFollowUpDispatchTransitionKind Kind,
    RestoredPendingFollowUp? PromotedDraft);
