using TianShu.Cli.Interaction.Orchestration;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;

namespace TianShu.Cli.Tests;

public sealed class RestoredFollowUpCoordinatorTests
{
    [Fact]
    public void Restore_DeduplicatesCorrelationIdsAndPromotesFirstDraft()
    {
        var coordinator = new RestoredFollowUpCoordinator();
        var state = new ControlPlanePendingInputState(
            Entries:
            [
                Entry("corr-1", "Queue", "QueuedUserMessage", "第一条"),
                Entry("corr-1", "Queue", "QueuedUserMessage", "重复条"),
            ],
            InterruptRequestPending: false,
            SubmitPendingSteersAfterInterrupt: false,
            PendingSteers:
            [
                Entry("corr-2", "Steer", "PendingSteer", "第二条"),
            ]);

        var result = coordinator.Restore(state);
        var snapshot = coordinator.CaptureSnapshot();

        Assert.Equal(2, result.RestoredCount);
        Assert.NotNull(result.PromotedDraft);
        Assert.Equal("第一条", result.PromotedDraft.PreviewText);
        Assert.Equal(ControlPlaneFollowUpMode.Queue, result.PromotedDraft.Mode);
        Assert.Equal(1, snapshot.QueuedCount);
        Assert.Collection(
            snapshot.Correlations,
            item => Assert.Equal("corr-1", item),
            item => Assert.Equal("corr-2", item));
    }

    [Fact]
    public void Restore_WhenSubmitPendingSteersAfterInterrupt_ConvertsPendingSteerToQueue()
    {
        var coordinator = new RestoredFollowUpCoordinator();
        var state = new ControlPlanePendingInputState(
            Entries:
            [
                Entry("corr-steer", "Steer", "PendingSteer", "恢复后的引导消息"),
            ],
            InterruptRequestPending: false,
            SubmitPendingSteersAfterInterrupt: true);

        var result = coordinator.Restore(state);

        Assert.Equal(1, result.RestoredCount);
        Assert.NotNull(result.PromotedDraft);
        Assert.Equal(ControlPlaneFollowUpMode.Queue, result.PromotedDraft.Mode);
    }

    [Fact]
    public void Restore_IgnoresCompareKeyOnlyEntries()
    {
        var coordinator = new RestoredFollowUpCoordinator();
        var state = new ControlPlanePendingInputState(
            Entries:
            [
                new ControlPlanePendingInputStateEntry(
                    "corr-empty",
                    "Queue",
                    "Queue",
                    "awaiting_commit",
                    ExpectedTurnId: null,
                    TurnId: null,
                    Inputs: Array.Empty<ControlPlaneInputItem>()),
            ],
            InterruptRequestPending: false);

        var result = coordinator.Restore(state);

        Assert.Equal(0, result.RestoredCount);
        Assert.Null(result.PromotedDraft);
        Assert.False(coordinator.HasComposerDraft);
    }

    [Fact]
    public void TakeEditedComposerDraft_ReplacesTextAndClearsComposerDraft()
    {
        var coordinator = new RestoredFollowUpCoordinator();
        coordinator.Restore(new ControlPlanePendingInputState(
            Entries:
            [
                Entry("corr-edit", "Queue", "QueuedUserMessage", "旧文本"),
            ],
            InterruptRequestPending: false));

        var draft = coordinator.TakeEditedComposerDraft("新文本");

        Assert.NotNull(draft);
        Assert.Equal("新文本", draft.PreviewText);
        Assert.False(coordinator.HasComposerDraft);
    }

    [Fact]
    public void DispatchResult_WaitsForMatchingTurnCompletionBeforePromotingNextDraft()
    {
        var coordinator = new RestoredFollowUpCoordinator();
        coordinator.Restore(new ControlPlanePendingInputState(
            Entries:
            [
                Entry("corr-first", "Queue", "QueuedUserMessage", "第一条"),
                Entry("corr-second", "Steer", "PendingSteer", "第二条"),
            ],
            InterruptRequestPending: false));
        var first = coordinator.TakeComposerDraftForSend().Draft!;

        var transition = coordinator.ApplyDispatchResult(
            new ControlPlaneTurnSubmissionResult
            {
                Accepted = true,
                TurnId = new TurnId("turn-first"),
                TurnStatus = "submitted",
            },
            first,
            static status => string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(RestoredFollowUpDispatchTransitionKind.Dispatching, transition.Kind);
        Assert.Null(coordinator.TryPromoteAfterTurnCompleted("turn-other"));
        var promoted = coordinator.TryPromoteAfterTurnCompleted("turn-first");

        Assert.NotNull(promoted);
        Assert.Equal("第二条", promoted.PreviewText);
        Assert.Equal(ControlPlaneFollowUpMode.Steer, promoted.Mode);
    }

    [Fact]
    public void DispatchResult_WhenFailed_RestoresDraftAndDoesNotPromoteNext()
    {
        var coordinator = new RestoredFollowUpCoordinator();
        coordinator.Restore(new ControlPlanePendingInputState(
            Entries:
            [
                Entry("corr-first", "Queue", "QueuedUserMessage", "第一条"),
                Entry("corr-second", "Queue", "QueuedUserMessage", "第二条"),
            ],
            InterruptRequestPending: false));
        var first = coordinator.TakeComposerDraftForSend().Draft!;

        var transition = coordinator.ApplyDispatchResult(
            new ControlPlaneTurnSubmissionResult { Accepted = false },
            first,
            static _ => false);

        Assert.Equal(RestoredFollowUpDispatchTransitionKind.FailedAndRestored, transition.Kind);
        Assert.Equal("第一条", coordinator.ComposerDraft?.PreviewText);
        Assert.Equal(1, coordinator.QueuedCount);
    }

    private static ControlPlanePendingInputStateEntry Entry(
        string correlationId,
        string mode,
        string pendingBucket,
        string text)
        => new(
            correlationId,
            mode,
            mode,
            "awaiting_commit",
            ExpectedTurnId: null,
            TurnId: null,
            PendingBucket: pendingBucket,
            Inputs: [new ControlPlaneTextInput(text)]);
}
