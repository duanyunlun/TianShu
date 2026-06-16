using System.Text.Json;
using TianShu.Cli.Interaction.Commands.FollowUp;
using TianShu.Cli.Interaction.Host;
using TianShu.Cli.Interaction.Orchestration;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;

namespace TianShu.Cli.Tests;

public sealed class InteractiveFollowUpCommandHandlerTests
{
    [Fact]
    public async Task HandleFollowUpAsync_WhenUsageInvalid_WritesUsageAndDoesNotStartOperation()
    {
        var harness = new Harness();

        await harness.Handler.HandleFollowUpAsync(harness.Context, "queue", CancellationToken.None);

        Assert.Contains(harness.Output, static line => line.IsError && line.Text == "用法：/follow-up <queue|steer|interrupt> <text>，或 /follow-up <promote|drop> <编号>");
        Assert.Empty(harness.StartedOperations);
    }

    [Fact]
    public async Task HandleFollowUpAsync_WhenPromoteQueuedItem_MutatesRuntimeAndTracksPendingGuidance()
    {
        var harness = new Harness();
        harness.QueuedFollowUps.Add(("corr1", "排队内容"));

        await harness.Handler.HandleFollowUpAsync(harness.Context, "promote 1", CancellationToken.None);

        Assert.Equal([("corr1", ControlPlanePendingFollowUpMutationKind.PromoteToSteer)], harness.PendingFollowUpMutations);
        Assert.Equal(["corr1"], harness.RemovedQueuedFollowUps);
        Assert.Empty(harness.UserMessages);
        Assert.Equal([("corr1", "排队内容")], harness.PendingGuidance);
        Assert.Contains(harness.Output, static line => line.Text == "已将待发送 #1 转为引导，等待进入当前回合上下文。");
    }

    [Fact]
    public async Task HandleFollowUpAsync_WhenDropQueuedItem_MutatesRuntimeWithoutUserMessage()
    {
        var harness = new Harness();
        harness.QueuedFollowUps.Add(("corr1", "排队内容"));

        await harness.Handler.HandleFollowUpAsync(harness.Context, "drop 1", CancellationToken.None);

        Assert.Equal([("corr1", ControlPlanePendingFollowUpMutationKind.Drop)], harness.PendingFollowUpMutations);
        Assert.Equal(["corr1"], harness.RemovedQueuedFollowUps);
        Assert.Empty(harness.UserMessages);
        Assert.Contains(harness.Output, static line => line.Text == "已删除待发送 #1。");
    }

    [Fact]
    public async Task HandleFollowUpAsync_WhenInterruptAndRunning_RemembersTurnAndStartsOperation()
    {
        var harness = new Harness
        {
            HasRunningConversation = true,
            LastObservedTurnId = "turn_active",
        };

        await harness.Handler.HandleFollowUpAsync(harness.Context, "interrupt 请停一下", CancellationToken.None);

        var operation = Assert.Single(harness.StartedOperations);
        Assert.Equal("follow-up", operation.Label);
        Assert.Equal(["turn_active"], harness.RememberedInterruptTurns);
        Assert.Contains(harness.Output, static line => line.Text == "已请求中断当前回合，等待确认。");
        await operation.Operation(CancellationToken.None);
        operation.OnResult?.Invoke(new ControlPlaneTurnSubmissionResult
        {
            Accepted = true,
            RequestedMode = ControlPlaneFollowUpMode.Interrupt,
            EffectiveMode = ControlPlaneFollowUpMode.Interrupt,
        });

        var submit = Assert.Single(harness.SubmittedFollowUps);
        Assert.Equal(ControlPlaneFollowUpMode.Interrupt, submit.Mode);
        Assert.NotNull(submit.CorrelationId);
        Assert.Contains(submit.Inputs, static input => input is ControlPlaneTextInput text && text.Text == "请停一下");
        Assert.DoesNotContain(harness.Output, static line => line.Text == "运行时已接收 中断 follow-up。");
    }

    [Fact]
    public async Task TryExecuteRunningFollowUpInputAsync_WhenQueueMode_RecordsSyntheticEventAndKeepsMainOutputClean()
    {
        var harness = new Harness
        {
            HasSteerableConversation = true,
            CurrentSessionThreadId = "thread_1",
            LastObservedTurnId = "turn_1",
        };

        var handled = await harness.Handler.TryExecuteRunningFollowUpInputAsync(
            harness.Context,
            "继续补充",
            ControlPlaneFollowUpMode.Queue,
            CancellationToken.None);

        Assert.True(handled);
        var operation = Assert.Single(harness.StartedOperations);
        Assert.Empty(harness.Output);
        Assert.Empty(harness.UserMessages);
        Assert.Contains(harness.QueuedFollowUps, static item => item.Text == "继续补充");
        operation.OnResult?.Invoke(new ControlPlaneTurnSubmissionResult
        {
            Accepted = true,
            RequestedMode = ControlPlaneFollowUpMode.Queue,
            EffectiveMode = ControlPlaneFollowUpMode.Queue,
        });
        Assert.Empty(harness.Output);
        Assert.Contains(harness.SyntheticEvents, static item =>
            item.Kind == "CliPlainTextFollowUpSubmitted"
            && item.ThreadId == "thread_1"
            && item.TurnId == "turn_1"
            && item.Text == "继续补充");
    }

    [Fact]
    public async Task TryExecuteRunningFollowUpInputAsync_WhenQueuedItemWasDeleted_DoesNotWriteFailure()
    {
        var harness = new Harness
        {
            HasSteerableConversation = true,
        };

        var handled = await harness.Handler.TryExecuteRunningFollowUpInputAsync(
            harness.Context,
            "继续补充",
            ControlPlaneFollowUpMode.Queue,
            CancellationToken.None);

        Assert.True(handled);
        var operation = Assert.Single(harness.StartedOperations);
        var correlationId = Assert.Single(harness.QueuedFollowUps).CorrelationId;

        operation.OnResult?.Invoke(new ControlPlaneTurnSubmissionResult
        {
            Accepted = false,
            Message = "跟进发送已删除。",
        });

        Assert.Equal([correlationId], harness.RemovedQueuedFollowUps);
        Assert.Empty(harness.Output);
    }

    [Fact]
    public async Task TryExecuteRunningFollowUpInputAsync_WhenQueuedItemWasPromoted_DoesNotWriteFailure()
    {
        var harness = new Harness
        {
            HasSteerableConversation = true,
        };

        var handled = await harness.Handler.TryExecuteRunningFollowUpInputAsync(
            harness.Context,
            "继续补充",
            ControlPlaneFollowUpMode.Queue,
            CancellationToken.None);

        Assert.True(handled);
        var operation = Assert.Single(harness.StartedOperations);
        var correlationId = Assert.Single(harness.QueuedFollowUps).CorrelationId;

        operation.OnResult?.Invoke(new ControlPlaneTurnSubmissionResult
        {
            Accepted = false,
            Message = "跟进发送已转换为引导。",
        });

        Assert.Equal([correlationId], harness.RemovedQueuedFollowUps);
        Assert.Empty(harness.Output);
    }

    [Fact]
    public async Task TryExecuteRunningFollowUpInputAsync_WhenSteerMode_TracksPendingGuidance()
    {
        var harness = new Harness
        {
            HasSteerableConversation = true,
        };

        var handled = await harness.Handler.TryExecuteRunningFollowUpInputAsync(
            harness.Context,
            "中途引导",
            ControlPlaneFollowUpMode.Steer,
            CancellationToken.None);

        Assert.True(handled);
        Assert.Empty(harness.UserMessages);
        Assert.Single(harness.StartedOperations);
        var pendingGuidance = Assert.Single(harness.PendingGuidance);
        Assert.Equal("中途引导", pendingGuidance.Text);
        Assert.False(string.IsNullOrWhiteSpace(pendingGuidance.CorrelationId));
        Assert.Empty(harness.QueuedFollowUps);
    }

    [Fact]
    public void HandleRestoredDraftState_WhenDraftExists_WritesJsonSnapshot()
    {
        var harness = new Harness();
        harness.RestoredFollowUps.Restore(new ControlPlanePendingInputState(
            Entries: [Entry("corr_1", "Queue", "QueuedUserMessage", "恢复草稿")],
            InterruptRequestPending: false));

        harness.Handler.HandleRestoredDraftState(harness.Context);

        var line = Assert.Single(harness.Output);
        using var document = JsonDocument.Parse(line.Text);
        Assert.Equal("Queue", document.RootElement.GetProperty("mode").GetString());
        Assert.Equal("恢复草稿", document.RootElement.GetProperty("preview").GetString());
        Assert.Equal("corr_1", document.RootElement.GetProperty("correlationId").GetString());
    }

    [Fact]
    public void HandleSendRestoredFollowUp_WhenDispatchRejected_RestoresDraftAndWritesError()
    {
        var harness = new Harness();
        harness.RestoredFollowUps.Restore(new ControlPlanePendingInputState(
            Entries: [Entry("corr_1", "Queue", "QueuedUserMessage", "恢复草稿")],
            InterruptRequestPending: false));

        harness.Handler.HandleSendRestoredFollowUp(harness.Context, CancellationToken.None);
        var operation = Assert.Single(harness.StartedOperations);
        operation.OnResult?.Invoke(new ControlPlaneTurnSubmissionResult { Accepted = false });

        Assert.Equal("恢复草稿", harness.RestoredFollowUps.ComposerDraft?.PreviewText);
        Assert.Contains(harness.Output, static line =>
            line.IsError
            && !line.CountAsFailure
            && line.Text == "恢复草稿发送失败，已保留当前草稿。");
    }

    [Fact]
    public void HandleDropRestoredFollowUp_WhenMoreDraftsExist_PromotesNextDraft()
    {
        var harness = new Harness();
        harness.RestoredFollowUps.Restore(new ControlPlanePendingInputState(
            Entries:
            [
                Entry("corr_1", "Queue", "QueuedUserMessage", "第一条"),
                Entry("corr_2", "Steer", "PendingSteer", "第二条"),
            ],
            InterruptRequestPending: false));

        harness.Handler.HandleDropRestoredFollowUp(harness.Context);

        Assert.Equal("第二条", harness.RestoredFollowUps.ComposerDraft?.PreviewText);
        Assert.Contains(harness.Output, static line => line.Text == "已丢弃恢复草稿：第一条");
        Assert.Contains(harness.Output, static line => line.Text == "已恢复到待编辑 Steer follow-up：第二条");
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

    private sealed class Harness
    {
        public InteractiveFollowUpCommandHandler Handler { get; } = new();

        public RestoredFollowUpCoordinator RestoredFollowUps { get; } = new();

        public List<(string Text, bool IsError, bool CountAsFailure)> Output { get; } = [];

        public List<string?> RememberedInterruptTurns { get; } = [];

        public List<(string Kind, string? ThreadId, string? TurnId, string? Message, string? Text)> SyntheticEvents { get; } = [];

        public List<(IReadOnlyList<ControlPlaneInputItem> Inputs, ControlPlaneFollowUpMode Mode, string? CorrelationId)> SubmittedFollowUps { get; } = [];

        public List<(string CorrelationId, ControlPlanePendingFollowUpMutationKind Kind)> PendingFollowUpMutations { get; } = [];

        public List<StartedOperation> StartedOperations { get; } = [];

        public List<(string Text, string? Label)> UserMessages { get; } = [];

        public List<(string CorrelationId, string Text)> PendingGuidance { get; } = [];

        public List<(string CorrelationId, string Text)> QueuedFollowUps { get; } = [];

        public List<string> RemovedQueuedFollowUps { get; } = [];

        public bool HasSteerableConversation { get; set; }

        public bool HasRunningConversation { get; set; }

        public string? CurrentSessionThreadId { get; set; }

        public string? LastObservedTurnId { get; set; }

        public InteractiveFollowUpCommandContext Context
            => new(
                RestoredFollowUps,
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                () => HasSteerableConversation,
                () => HasRunningConversation,
                CurrentSessionThreadId,
                LastObservedTurnId,
                RememberedInterruptTurns.Add,
                (kind, threadId, turnId, message, text) => SyntheticEvents.Add((kind, threadId, turnId, message, text)),
                (inputs, mode, _, correlationId) =>
                {
                    SubmittedFollowUps.Add((inputs, mode, correlationId));
                    return Task.FromResult(new ControlPlaneTurnSubmissionResult { Accepted = true });
                },
                (correlationId, kind, _) =>
                {
                    PendingFollowUpMutations.Add((correlationId, kind));
                    return Task.FromResult(new ControlPlanePendingFollowUpMutationResult
                    {
                        Accepted = true,
                        CorrelationId = correlationId,
                        Kind = kind,
                    });
                },
                (correlationId, text) => QueuedFollowUps.Add((correlationId, text)),
                correlationId => RemovedQueuedFollowUps.Add(correlationId),
                index => index > 0 && index <= QueuedFollowUps.Count
                    ? new QueuedFollowUpDockEntrySnapshot(index, QueuedFollowUps[index - 1].CorrelationId, QueuedFollowUps[index - 1].Text)
                    : null,
                (correlationId, text) => PendingGuidance.Add((correlationId, text)),
                (label, operation, cancellationToken, onResult, onCancelled, onException) =>
                    StartedOperations.Add(new StartedOperation(label, operation, cancellationToken, onResult, onCancelled, onException)),
                static status => string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase),
                (text, label) => UserMessages.Add((text, label)),
                (message, isError, countAsFailure) => Output.Add((message, isError, countAsFailure)));
    }

    private sealed record StartedOperation(
        string Label,
        Func<CancellationToken, Task<ControlPlaneTurnSubmissionResult>> Operation,
        CancellationToken CancellationToken,
        Action<ControlPlaneTurnSubmissionResult>? OnResult,
        Action? OnCancelled,
        Action<Exception>? OnException);
}
