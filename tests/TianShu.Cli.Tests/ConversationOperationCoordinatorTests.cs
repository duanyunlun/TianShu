using TianShu.Cli.Interaction.Orchestration;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;

namespace TianShu.Cli.Tests;

public sealed class ConversationOperationCoordinatorTests
{
    [Fact]
    public async Task ExecuteAsync_WhenRejectedWithoutInterrupt_WritesErrorAndClosesAssistantLine()
    {
        var harness = new Harness
        {
            AssistantLineOpen = true,
        };

        await harness.Coordinator.ExecuteAsync(
            harness.Context,
            "send",
            static () => Task.FromResult(new ControlPlaneTurnSubmissionResult
            {
                Accepted = false,
                Message = "bad request",
                TurnId = new TurnId("turn_1"),
                TurnStatus = "failed",
            }),
            CancellationToken.None);

        Assert.False(harness.AssistantLineOpen);
        Assert.Contains(harness.ErrorLines, static line => line == "send 执行失败：bad request");
        Assert.Contains(harness.SyntheticEvents, static item => item.Kind == "CliConversationFailed" && item.Status == "failed");
    }

    [Fact]
    public async Task ExecuteAsync_WhenRejectedInterruptWasRequested_WritesVerboseWithoutError()
    {
        var harness = new Harness
        {
            ConsumeInterrupt = true,
        };

        await harness.Coordinator.ExecuteAsync(
            harness.Context,
            "follow-up",
            static () => Task.FromResult(new ControlPlaneTurnSubmissionResult
            {
                Accepted = false,
                Message = "interrupted",
                TurnId = new TurnId("turn_1"),
                TurnStatus = "interrupted",
                CorrelationId = "corr_1",
            }),
            CancellationToken.None);

        Assert.Empty(harness.ErrorLines);
        Assert.Contains(harness.VerboseLines, static line => line.Contains("follow-up 已按请求中断", StringComparison.Ordinal));
        Assert.Contains(harness.SyntheticEvents, static item => item.Kind == "CliConversationInterrupted");
    }

    [Fact]
    public async Task ExecuteAsync_WhenQueuedFollowUpWasDeleted_DoesNotWriteError()
    {
        var harness = new Harness();

        await harness.Coordinator.ExecuteAsync(
            harness.Context,
            "follow-up",
            static () => Task.FromResult(new ControlPlaneTurnSubmissionResult
            {
                Accepted = false,
                Message = "跟进发送已删除。",
                CorrelationId = "corr_1",
            }),
            CancellationToken.None);

        Assert.Empty(harness.ErrorLines);
        Assert.Empty(harness.Lines);
        Assert.Contains(harness.SyntheticEvents, static item => item.Kind == "CliPendingFollowUpDropped");
    }

    [Fact]
    public async Task ExecuteAsync_WhenQueuedFollowUpWasPromoted_DoesNotWriteError()
    {
        var harness = new Harness();

        await harness.Coordinator.ExecuteAsync(
            harness.Context,
            "follow-up",
            static () => Task.FromResult(new ControlPlaneTurnSubmissionResult
            {
                Accepted = false,
                Message = "跟进发送已转换为引导。",
                CorrelationId = "corr_1",
            }),
            CancellationToken.None);

        Assert.Empty(harness.ErrorLines);
        Assert.Empty(harness.Lines);
        Assert.Contains(harness.VerboseLines, static line => line.Contains("原排队调度已结束", StringComparison.Ordinal));
        Assert.Contains(harness.SyntheticEvents, static item => item.Kind == "CliPendingFollowUpPromoted");
    }

    [Fact]
    public async Task ExecuteAsync_WhenAcceptedFailedTerminalStatus_MarksFailureAndTerminalTurn()
    {
        var harness = new Harness();
        harness.ConversationActivity.MarkTurnObserved();

        await harness.Coordinator.ExecuteAsync(
            harness.Context,
            "send",
            static () => Task.FromResult(new ControlPlaneTurnSubmissionResult
            {
                Accepted = true,
                TurnId = new TurnId("turn_1"),
                TurnStatus = "failed",
            }),
            CancellationToken.None);

        Assert.Equal(1, harness.MarkFailureCount);
        Assert.False(harness.ConversationActivity.IsBusy);
        Assert.Contains(harness.SyntheticEvents, static item => item.Kind == "CliConversationCompleted" && item.Status == "failed");
    }

    [Fact]
    public async Task ExecuteAsync_WhenActionThrows_WritesErrorAndInvokesExceptionCallback()
    {
        var harness = new Harness
        {
            AssistantLineOpen = true,
        };
        Exception? observed = null;

        await harness.Coordinator.ExecuteAsync(
            harness.Context,
            "send",
            static () => throw new InvalidOperationException("boom"),
            CancellationToken.None,
            onException: ex => observed = ex);

        Assert.False(harness.AssistantLineOpen);
        Assert.IsType<InvalidOperationException>(observed);
        Assert.Contains("send 执行失败：boom", harness.ErrorLines);
        Assert.Contains(harness.SyntheticEvents, static item => item.Kind == "CliConversationException");
    }

    [Fact]
    public async Task ExecuteAsync_ControlOutputScope_DoesNotWrapLongRunningAction()
    {
        var harness = new Harness();
        var actionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAction = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var running = harness.Coordinator.ExecuteAsync(
            harness.Context,
            "follow-up",
            async () =>
            {
                harness.Events.Add("action-start");
                actionStarted.SetResult();
                await releaseAction.Task;
                harness.Events.Add("action-end");
                return new ControlPlaneTurnSubmissionResult
                {
                    Accepted = true,
                    TurnStatus = "running",
                };
            },
            CancellationToken.None);

        await actionStarted.Task;

        Assert.Equal(["action-start"], harness.Events);

        releaseAction.SetResult();
        await running;

        Assert.Equal(
            [
                "action-start",
                "action-end",
                "scope-begin",
                "scope-end",
            ],
            harness.Events);
    }

    private sealed class Harness
    {
        public ConversationOperationCoordinator Coordinator { get; } = new();

        public ConversationActivityTracker ConversationActivity { get; } = new();

        public List<(string Kind, string? ThreadId, string? TurnId, string? Message, string? Text, string? Status)> SyntheticEvents { get; } = [];

        public List<string> VerboseLines { get; } = [];

        public List<string> Lines { get; } = [];

        public List<string> ErrorLines { get; } = [];

        public List<string> Events { get; } = [];

        public bool AssistantLineOpen { get; set; }

        public bool ConsumeInterrupt { get; set; }

        public int MarkFailureCount { get; private set; }

        public ConversationOperationCoordinatorContext Context
            => new()
            {
                ConversationActivity = ConversationActivity,
                CurrentSessionThreadId = "thread_1",
                LastObservedTurnId = "turn_0",
                TouchConversationActivity = static () => { },
                RecordSyntheticEvent = (kind, threadId, turnId, message, text, status) =>
                    SyntheticEvents.Add((kind, threadId, turnId, message, text, status)),
                StartWorkingDockTimer = static () => { },
                StopWorkingDockTimer = static () => { },
                RefreshSessionSnapshotAsync = static _ => Task.CompletedTask,
                ApplyTurnResult = static (_, _) => { },
                TryConsumeUserRequestedInterrupt = (_, _) => ConsumeInterrupt,
                WriteVerboseOrImportant = (message, _) => VerboseLines.Add(message),
                IsTerminalTurnStatus = ChatSessionState.IsTerminalTurnStatus,
                IsFailedTurnStatus = ChatSessionState.IsFailedTurnStatus,
                MarkFailure = () => MarkFailureCount++,
                WriteLine = Lines.Add,
                WriteErrorLineOnce = ErrorLines.Add,
                BeginControlOutputScope = () =>
                {
                    Events.Add("scope-begin");
                    return new DisposeAction(() => Events.Add("scope-end"));
                },
                RefreshInlineTailPrompt = static () => { },
                GetAssistantLineOpen = () => AssistantLineOpen,
                SetAssistantLineOpen = value => AssistantLineOpen = value,
            };
    }

    private sealed class DisposeAction(Action dispose) : IDisposable
    {
        public void Dispose()
            => dispose();
    }
}
