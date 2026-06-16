using System.Text.Json;
using TianShu.Cli.Interaction;
using TianShu.Cli.Interaction.Orchestration;
using TianShu.Cli.Interaction.Projection;
using TianShu.Cli.Interaction.Recording;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;

namespace TianShu.Cli.Tests;

public sealed class ChatStreamEventConsumerTests
{
    [Fact]
    public void Handle_AssistantTextDelta_WritesTextAndOpensAssistantLine()
    {
        var harness = new Harness
        {
            AssistantLeadingSpacerPending = true,
        };

        harness.Handle(Event(ControlPlaneConversationStreamEventKind.AssistantTextDelta, text: "hello"));

        Assert.Equal(["hello"], harness.Writes);
        Assert.Equal(1, harness.SpacerWriteCount);
        Assert.True(harness.AssistantLineOpen);
        Assert.False(harness.AssistantLeadingSpacerPending);
        Assert.Equal("thread_1", harness.SessionState.LastObservedThreadId);
        Assert.Equal(1, harness.TouchCount);
        Assert.Equal(1, harness.EventWaiter.ObservedEventCount);
    }

    [Fact]
    public void Handle_AssistantTextCompleted_WritesCommittedBlocksAndClosesAssistantLine()
    {
        var harness = new Harness
        {
            AssistantLineOpen = true,
            ProjectionBlocks = [new AssistantMessageBlock("done", IsComplete: true)],
        };

        harness.Handle(Event(ControlPlaneConversationStreamEventKind.AssistantTextCompleted));

        var write = Assert.Single(harness.CommittedBlockWrites);
        Assert.Single(write.Blocks);
        Assert.True(write.CountErrorsAsFailure);
        Assert.False(harness.AssistantLineOpen);
    }

    [Theory]
    [InlineData(ControlPlaneConversationStreamEventKind.ToolCallStarted, true, false)]
    [InlineData(ControlPlaneConversationStreamEventKind.ToolCallCompleted, true, false)]
    [InlineData(ControlPlaneConversationStreamEventKind.ItemStarted, false, true)]
    [InlineData(ControlPlaneConversationStreamEventKind.ItemCompleted, false, true)]
    [InlineData(ControlPlaneConversationStreamEventKind.PlanUpdated, false, true)]
    public void Handle_ToolItemAndPlanEvents_WritesCommittedBlocksAndVerboseEvent(
        ControlPlaneConversationStreamEventKind kind,
        bool expectsToolVerbose,
        bool expectsGeneralVerbose)
    {
        var harness = new Harness
        {
            ProjectionBlocks = [new SystemNoticeBlock("notice")],
        };

        harness.Handle(Event(kind));

        Assert.Single(harness.CommittedBlockWrites);
        Assert.Equal(expectsToolVerbose ? 1 : 0, harness.ToolVerboseCount);
        Assert.Equal(expectsGeneralVerbose ? 1 : 0, harness.GeneralVerboseCount);
        Assert.Equal(kind == ControlPlaneConversationStreamEventKind.PlanUpdated ? 1 : 0, harness.RefreshPromptCount);
    }

    [Fact]
    public void Handle_ServerRequestResolved_ClearsPendingInteractiveRequest()
    {
        var harness = new Harness();
        harness.PendingInteractiveRequests.AddApproval(new CliPendingApprovalRequestState(
            "call_1",
            "thread_1",
            "turn_1",
            "shell",
            ApprovalKind: null,
            AvailableDecisions: null,
            AvailableDecisionOptions: null));

        harness.Handle(Event(ControlPlaneConversationStreamEventKind.ServerRequestResolved, callId: "call_1"));

        Assert.Empty(harness.PendingInteractiveRequests.Snapshot.ApprovalCallIds);
        Assert.Equal(["call_1"], harness.ClearedCallIds);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Handle_Error_UsesWillRetryToDecideFailureCounting(bool willRetry, bool expectedCountErrorsAsFailure)
    {
        var harness = new Harness
        {
            ProjectionBlocks = [new SystemNoticeBlock("error")],
        };

        harness.Handle(Event(ControlPlaneConversationStreamEventKind.Error, willRetry: willRetry));

        var write = Assert.Single(harness.CommittedBlockWrites);
        Assert.Equal(expectedCountErrorsAsFailure, write.CountErrorsAsFailure);
    }

    [Fact]
    public void Handle_TurnCompleted_ClearsPendingTurnStateAndPromotesFollowUp()
    {
        var harness = new Harness
        {
            AssistantLineOpen = true,
            ProjectionBlocks = [new AssistantMessageBlock("final", IsComplete: true)],
        };
        harness.PendingInteractiveRequests.AddApproval(new CliPendingApprovalRequestState(
            "call_1",
            "thread_1",
            "turn_1",
            "shell",
            ApprovalKind: null,
            AvailableDecisions: null,
            AvailableDecisionOptions: null));

        harness.Handle(Event(ControlPlaneConversationStreamEventKind.TurnCompleted, status: "failed"));

        Assert.Empty(harness.PendingInteractiveRequests.Snapshot.ApprovalCallIds);
        Assert.False(harness.AssistantLineOpen);
        Assert.Equal("failed", harness.SessionState.LastObservedTurnStatus);
        Assert.Equal(1, harness.MarkFailureCount);
        Assert.Equal(1, harness.PromoteRestoredFollowUpCount);
        Assert.Equal(1, harness.RefreshPromptCount);
        Assert.Contains(harness.LifecycleDebugMessages, static message => message.Contains("status=failed", StringComparison.Ordinal));
    }

    [Fact]
    public void Handle_TurnCompleted_WhenInterrupted_ClearsPlanDockAndWritesInterruptedControlOutput()
    {
        var harness = new Harness();

        harness.Handle(Event(ControlPlaneConversationStreamEventKind.TurnCompleted, status: "interrupted"));

        Assert.Equal(1, harness.ClearPlanDockCount);
        Assert.Equal(["已中断当前回合。"], harness.InterruptedControlOutputLines);
        Assert.Equal(1, harness.RefreshPromptCount);
        Assert.Equal(0, harness.MarkFailureCount);
    }

    [Fact]
    public void Handle_UserMessageCommitted_UpdatesPendingGuidanceAndWritesVerboseEvent()
    {
        var harness = new Harness();

        harness.Handle(Event(ControlPlaneConversationStreamEventKind.UserMessageCommitted));

        Assert.Equal(1, harness.GuidanceCommittedCount);
        Assert.Equal(1, harness.GeneralVerboseCount);
    }

    [Fact]
    public void Handle_PendingFollowUpUpdated_TracksGuidanceAndConsumesCommittedLifecycle()
    {
        var harness = new Harness();

        harness.Handle(Event(ControlPlaneConversationStreamEventKind.PendingFollowUpUpdated, status: "awaiting_commit"));
        harness.Handle(Event(ControlPlaneConversationStreamEventKind.PendingFollowUpUpdated, status: "committed"));

        Assert.Equal(2, harness.PendingGuidanceTrackCount);
        Assert.Equal(2, harness.GuidanceCommittedCount);
        Assert.Equal(2, harness.GeneralVerboseCount);
    }

    private static ControlPlaneConversationStreamEvent Event(
        ControlPlaneConversationStreamEventKind kind,
        string? text = null,
        string? status = null,
        string? callId = null,
        bool? willRetry = null)
        => CliConsumerFakeRuntime.CreateStreamEvent(
            kind,
            threadId: "thread_1",
            turnId: "turn_1",
            text: text,
            status: status,
            callId: callId,
            willRetry: willRetry);

    private sealed class Harness
    {
        private readonly ChatStreamEventConsumer consumer = new();
        private readonly CliConsumerFakeRuntime runtime = new();
        private readonly ChatInteractionRecorder recorder = new(new JsonSerializerOptions(JsonSerializerDefaults.Web));

        public ChatSessionState SessionState { get; } = new();

        public ConversationActivityTracker ConversationActivity { get; } = new();

        public ConversationEventWaiter EventWaiter { get; } = new();

        public PendingInteractiveRequestStore PendingInteractiveRequests { get; } = new();

        public IReadOnlyList<ChatPresentationBlock> ProjectionBlocks { get; set; } = [];

        public List<string> Writes { get; } = [];

        public List<(IReadOnlyList<ChatPresentationBlock> Blocks, bool CountErrorsAsFailure)> CommittedBlockWrites { get; } = [];

        public List<string> ClearedCallIds { get; } = [];

        public List<string> LifecycleDebugMessages { get; } = [];

        public bool AssistantLineOpen { get; set; }

        public bool AssistantLeadingSpacerPending { get; set; }

        public int SpacerWriteCount { get; private set; }

        public int TouchCount { get; private set; }

        public int ToolVerboseCount { get; private set; }

        public int GeneralVerboseCount { get; private set; }

        public int RefreshPromptCount { get; private set; }

        public int MarkFailureCount { get; private set; }

        public int PromoteRestoredFollowUpCount { get; private set; }

        public int GuidanceCommittedCount { get; private set; }

        public int PendingGuidanceTrackCount { get; private set; }

        public int ClearPlanDockCount { get; private set; }

        public List<string> InterruptedControlOutputLines { get; } = [];

        public void Handle(ControlPlaneConversationStreamEvent streamEvent)
            => consumer.Handle(CreateContext(), streamEvent, CancellationToken.None);

        private ChatStreamEventConsumerContext CreateContext()
            => new()
            {
                Runtime = runtime,
                Options = new ChatCommandOptions(),
                SessionState = SessionState,
                ConversationActivity = ConversationActivity,
                InteractionRecorder = recorder,
                EventWaiter = EventWaiter,
                PendingInteractiveRequests = PendingInteractiveRequests,
                PendingInteractiveAutomation = new PendingInteractiveAutomationHandler(),
                TouchConversationActivity = () => TouchCount++,
                RecordPresentationProjection = _ => new InteractionPipelineProjectionResult(
                    new ChatPresentationProjection(ProjectionBlocks, Plan: null, Records: []),
                    new ChatPresentationState(
                        ProjectionBlocks,
                        ActiveAssistantText: string.Empty,
                        Plan: null,
                        ConversationOutputModel.FromBlocks(ProjectionBlocks))),
                WriteProjectionCommittedBlocks = (blocks, countErrorsAsFailure) => CommittedBlockWrites.Add((blocks, countErrorsAsFailure)),
                WriteVerboseToolEventIfRequested = _ => ToolVerboseCount++,
                WriteVerboseEventIfRequested = _ => GeneralVerboseCount++,
                WriteLine = static (_, _) => { },
                Write = Writes.Add,
                WriteTerminalVisualSpacerLine = () => SpacerWriteCount++,
                CloseAssistantLineIfOpen = () => AssistantLineOpen = false,
                RefreshInlineTailPrompt = () => RefreshPromptCount++,
                ClearPendingInteractiveRequest = callId =>
                {
                    ClearedCallIds.Add(callId);
                    PendingInteractiveRequests.Remove(callId);
                },
                ClearPendingInteractiveRequestsForTurn = PendingInteractiveRequests.ClearForTurn,
                WriteLifecycleDebug = LifecycleDebugMessages.Add,
                IsFailedTurnStatus = ChatSessionState.IsFailedTurnStatus,
                MarkFailure = () => MarkFailureCount++,
                TryWriteCommittedGuidanceMessage = _ => GuidanceCommittedCount++,
                TrackPendingGuidanceMessage = _ => PendingGuidanceTrackCount++,
                TryPromotePendingRestoredFollowUpAfterTurnCompleted = _ => PromoteRestoredFollowUpCount++,
                ClearPlanDockState = () => ClearPlanDockCount++,
                WriteInterruptedControlOutput = () => InterruptedControlOutputLines.Add("已中断当前回合。"),
                GetAssistantLineOpen = () => AssistantLineOpen,
                SetAssistantLineOpen = value => AssistantLineOpen = value,
                GetAssistantLeadingSpacerPending = () => AssistantLeadingSpacerPending,
                SetAssistantLeadingSpacerPending = value => AssistantLeadingSpacerPending = value,
                HasRetainedTailFrame = static () => false,
            };
    }
}
