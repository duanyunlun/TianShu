using TianShu.Cli.Interaction.Orchestration;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Sessions;

namespace TianShu.Cli.Tests;

public sealed class ThreadResumeCoordinatorTests
{
    [Fact]
    public async Task ResumeThreadByIdAsync_WhenRuntimeReturnsNull_WritesFailure()
    {
        var harness = new Harness
        {
            ResumeResult = null,
        };

        var result = await harness.Coordinator.ResumeThreadByIdAsync(harness.Context, "thread_missing", CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(["thread_missing"], harness.ResumeCalls);
        Assert.Contains(harness.Output, static line => line.IsError && line.Text == "恢复线程失败。");
    }

    [Fact]
    public async Task ResumeThreadByIdAsync_WhenSnapshotHasPendingState_ReplaysRequestsAndRestoresFollowUps()
    {
        var harness = new Harness
        {
            ResumeResult = Snapshot(
                "thread_1",
                pendingRequests:
                [
                    new ControlPlanePendingInteractiveRequest
                    {
                        RequestId = 1,
                        RequestKind = "approval_requested",
                        ThreadId = "thread_1",
                        TurnId = "turn_1",
                        CallId = "approval_1",
                        ToolName = "shell",
                    },
                ],
                pendingInputState: new ControlPlanePendingInputState(
                    Entries: [Entry("corr_1", "Queue", "QueuedUserMessage", "恢复草稿")],
                    InterruptRequestPending: false)),
        };

        var result = await harness.Coordinator.ResumeThreadByIdAsync(harness.Context, "thread_1", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(harness.ReplayedEvents);
        Assert.Equal(ControlPlaneConversationStreamEventKind.ApprovalRequested, harness.ReplayedEvents[0].Kind);
        Assert.Equal("恢复草稿", harness.RestoredFollowUps.ComposerDraft?.PreviewText);
        Assert.Contains(harness.PromotedDrafts, static draft => draft?.PreviewText == "恢复草稿");
        Assert.Contains(harness.Output, static line => line.Text.Contains("已恢复线程：thread_1", StringComparison.Ordinal));
        Assert.Contains(harness.Output, static line => line.Text.Contains("已回放待处理交互", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TryConsumeStartupResumedThreadStateAsync_WhenActiveThreadAndRequested_RefreshesAndConsumesState()
    {
        var harness = new Harness
        {
            SessionSnapshot = new ControlPlaneSessionSnapshot
            {
                ActiveThreadId = new ThreadId("thread_startup"),
            },
            ResumeResult = Snapshot("thread_startup"),
        };

        await harness.Coordinator.TryConsumeStartupResumedThreadStateAsync(
            harness.Context,
            shouldConsumeStartupResumedThread: true,
            CancellationToken.None);

        Assert.Equal(["thread_startup"], harness.ResumeCalls);
        Assert.Equal(1, harness.ApplySessionSnapshotCount);
        Assert.Equal(1, harness.RefreshSessionSnapshotCount);
    }

    private static ControlPlaneThreadSnapshot Snapshot(
        string threadId,
        IReadOnlyList<ControlPlanePendingInteractiveRequest>? pendingRequests = null,
        ControlPlanePendingInputState? pendingInputState = null)
        => new()
        {
            Thread = new ControlPlaneThreadSummary
            {
                ThreadId = new ThreadId(threadId),
            },
            Messages = [],
            Turns = [],
            SeedHistory = [],
            PendingInteractiveRequests = pendingRequests ?? [],
            PendingInputState = pendingInputState,
        };

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

    private sealed class Harness
    {
        public ThreadResumeCoordinator Coordinator { get; } = new();

        public PendingInteractiveRequestStore PendingInteractiveRequests { get; } = new();

        public PendingInteractiveReplayCoordinator PendingInteractiveReplay { get; } = new();

        public RestoredFollowUpCoordinator RestoredFollowUps { get; } = new();

        public List<string> ResumeCalls { get; } = [];

        public List<ControlPlaneConversationStreamEvent> ReplayedEvents { get; } = [];

        public List<RestoredPendingFollowUp?> PromotedDrafts { get; } = [];

        public List<(string Text, bool IsError)> Output { get; } = [];

        public ControlPlaneThreadSnapshot? ResumeResult { get; set; }

        public ControlPlaneSessionSnapshot SessionSnapshot { get; set; } = new();

        public int ApplySessionSnapshotCount { get; private set; }

        public int RefreshSessionSnapshotCount { get; private set; }

        public ThreadResumeCoordinatorContext Context
            => new(
                (threadId, _) =>
                {
                    ResumeCalls.Add(threadId);
                    return Task.FromResult(ResumeResult);
                },
                _ => Task.FromResult(SessionSnapshot),
                _ =>
                {
                    RefreshSessionSnapshotCount++;
                    return Task.CompletedTask;
                },
                _ => ApplySessionSnapshotCount++,
                PendingInteractiveRequests,
                PendingInteractiveReplay,
                RestoredFollowUps,
                (streamEvent, _) =>
                {
                    ReplayedEvents.Add(streamEvent);
                    if (streamEvent.Kind == ControlPlaneConversationStreamEventKind.ApprovalRequested
                        && streamEvent.CallId is not null)
                    {
                        PendingInteractiveRequests.AddApproval(new CliPendingApprovalRequestState(
                            streamEvent.CallId.Value,
                            streamEvent.ThreadId?.Value,
                            streamEvent.TurnId?.Value,
                            streamEvent.ToolName,
                            streamEvent.ApprovalKind,
                            streamEvent.AvailableDecisions,
                            AvailableDecisionOptions: null));
                    }
                },
                PromotedDrafts.Add,
                (message, isError) => Output.Add((message, isError)));
    }
}
