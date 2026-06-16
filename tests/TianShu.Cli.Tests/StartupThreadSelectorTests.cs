using TianShu.Cli.Interaction.Commands.Threads;
using TianShu.ControlPlane.Abstractions.Conversations;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;
using ThreadViewProjection = TianShu.Contracts.Projections.ThreadProjection;

namespace TianShu.Cli.Tests;

public sealed class StartupThreadSelectorTests
{
    [Fact]
    public async Task ExecuteAsync_WhenResumeLast_UsesCurrentCwdLimitOneAndConsumesSnapshot()
    {
        var conversations = new FakeConversationControlPlane();
        conversations.Threads =
        [
            CreateThread("thread-last-001", "最近线程", "D:/repo"),
        ];
        conversations.ResumeResult = CreateSnapshot("thread-last-001");
        ControlPlaneThreadSnapshot? consumed = null;
        var messages = new List<(string Message, bool IsError)>();

        var result = await new StartupThreadSelector().ExecuteAsync(
            conversations,
            new ChatCommandOptions
            {
                StartupThreadAction = ChatStartupThreadActionKind.Resume,
                StartupThreadUseLast = true,
            },
            CreateContext(consumedSnapshot: snapshot => consumed = snapshot, messages: messages),
            CancellationToken.None);

        Assert.Equal(StartupThreadActionOutcome.Succeeded, result);
        var query = Assert.Single(conversations.ListQueries);
        Assert.Equal(1, query.Limit);
        Assert.Equal("D:/repo", query.WorkingDirectory);
        Assert.Equal("thread-last-001", Assert.Single(conversations.ResumeThreadIds));
        Assert.Equal("thread-last-001", consumed?.Thread.ThreadId.Value);
        Assert.Contains(messages, static item => item.Message.Contains("已恢复线程：thread-last-001", StringComparison.Ordinal) && !item.IsError);
    }

    [Fact]
    public async Task ExecuteAsync_WhenForkTargetNameAcrossAllCwds_ResolvesSingleNameMatch()
    {
        var conversations = new FakeConversationControlPlane();
        conversations.Threads =
        [
            CreateThread("thread-source-001", "其他会话", "D:/elsewhere"),
            CreateThread("thread-source-002", "目标会话", "D:/elsewhere"),
        ];
        conversations.ForkResult = CreateThread("thread-source-002-fork", "目标会话 fork", "D:/repo");
        var messages = new List<(string Message, bool IsError)>();

        var result = await new StartupThreadSelector().ExecuteAsync(
            conversations,
            new ChatCommandOptions
            {
                StartupThreadAction = ChatStartupThreadActionKind.Fork,
                StartupThreadTarget = "目标会话",
                StartupThreadShowAll = true,
            },
            CreateContext(messages: messages),
            CancellationToken.None);

        Assert.Equal(StartupThreadActionOutcome.Succeeded, result);
        Assert.Null(Assert.Single(conversations.ListQueries).WorkingDirectory);
        Assert.Equal("thread-source-002", Assert.Single(conversations.ForkThreadIds));
        Assert.Contains(messages, static item => item.Message == "已分叉线程：thread-source-002-fork" && !item.IsError);
    }

    [Fact]
    public async Task ResolveSelectionAsync_WhenTargetNameDuplicates_FailsWithoutRuntimeCommand()
    {
        var conversations = new FakeConversationControlPlane();
        conversations.Threads =
        [
            CreateThread("thread-001", "重复会话", "D:/repo"),
            CreateThread("thread-002", "重复会话", "D:/repo"),
        ];
        var messages = new List<(string Message, bool IsError)>();

        var result = await new StartupThreadSelector().ResolveSelectionAsync(
            conversations,
            new ChatCommandOptions
            {
                StartupThreadAction = ChatStartupThreadActionKind.Resume,
                StartupThreadTarget = "重复会话",
            },
            CreateContext(messages: messages),
            CancellationToken.None);

        Assert.Equal(StartupThreadActionOutcome.Failed, result.Status);
        Assert.Null(result.ThreadId);
        Assert.Contains(messages, static item => item.Message.Contains("找到多个同名线程", StringComparison.Ordinal) && item.IsError);
        Assert.Empty(conversations.ResumeThreadIds);
        Assert.Empty(conversations.ForkThreadIds);
    }

    [Fact]
    public async Task ResolveSelectionAsync_WhenPickerInputIsEmpty_Cancels()
    {
        var conversations = new FakeConversationControlPlane();
        conversations.Threads =
        [
            CreateThread("thread-picker-001", "第一个线程", "D:/repo"),
        ];
        var messages = new List<(string Message, bool IsError)>();

        var result = await new StartupThreadSelector().ResolveSelectionAsync(
            conversations,
            new ChatCommandOptions
            {
                StartupThreadAction = ChatStartupThreadActionKind.Resume,
            },
            CreateContext(readLineAsync: _ => Task.FromResult<string?>(string.Empty), messages: messages),
            CancellationToken.None);

        Assert.Equal(StartupThreadActionOutcome.Cancelled, result.Status);
        Assert.Null(result.ThreadId);
        Assert.Contains(messages, static item => item.Message == "已取消线程选择。" && !item.IsError);
    }

    [Fact]
    public async Task ResolveSelectionAsync_WhenPickerInputNumberMatches_ReturnsSelectedThread()
    {
        var conversations = new FakeConversationControlPlane();
        conversations.Threads =
        [
            CreateThread("thread-picker-001", "第一个线程", "D:/repo"),
            CreateThread("thread-picker-002", "第二个线程", "D:/repo"),
        ];

        var result = await new StartupThreadSelector().ResolveSelectionAsync(
            conversations,
            new ChatCommandOptions
            {
                StartupThreadAction = ChatStartupThreadActionKind.Resume,
            },
            CreateContext(readLineAsync: _ => Task.FromResult<string?>("2")),
            CancellationToken.None);

        Assert.Equal(StartupThreadActionOutcome.Succeeded, result.Status);
        Assert.Equal("thread-picker-002", result.ThreadId);
    }

    private static StartupThreadSelectorContext CreateContext(
        Func<CancellationToken, Task<string?>>? readLineAsync = null,
        Action<ControlPlaneThreadSnapshot>? consumedSnapshot = null,
        List<(string Message, bool IsError)>? messages = null)
        => new(
            () => "D:/repo",
            () => false,
            static (_, _, _, _) => Task.FromResult<ControlPlaneThreadSummary?>(null),
            readLineAsync ?? (_ => Task.FromResult<string?>(null)),
            (snapshot, _) => consumedSnapshot?.Invoke(snapshot),
            static () => (0, 0, 0),
            (message, isError) => messages?.Add((message, isError)));

    private static ControlPlaneThreadSummary CreateThread(string id, string name, string cwd)
        => new()
        {
            ThreadId = new ThreadId(id),
            Name = name,
            WorkingDirectory = cwd,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    private static ControlPlaneThreadSnapshot CreateSnapshot(string id)
        => new()
        {
            Thread = CreateThread(id, "恢复线程", "D:/repo"),
            SeedHistory = [],
            Turns = [],
            PendingInteractiveRequests = [],
        };

    private sealed class FakeConversationControlPlane : IConversationControlPlane
    {
        public List<ControlPlaneThreadListQuery> ListQueries { get; } = [];

        public List<string> ResumeThreadIds { get; } = [];

        public List<string> ForkThreadIds { get; } = [];

        public IReadOnlyList<ControlPlaneThreadSummary> Threads { get; set; } = [];

        public ControlPlaneThreadSnapshot? ResumeResult { get; set; }

        public ControlPlaneThreadSummary? ForkResult { get; set; }

        public Task<ControlPlaneThreadListResult> ListThreadsAsync(ControlPlaneThreadListQuery query, CancellationToken cancellationToken)
        {
            ListQueries.Add(query);
            return Task.FromResult(new ControlPlaneThreadListResult { Threads = Threads });
        }

        public Task<ControlPlaneThreadSnapshot?> ResumeThreadAsync(ControlPlaneResumeThreadCommand command, CancellationToken cancellationToken)
        {
            ResumeThreadIds.Add(command.ThreadId.Value);
            return Task.FromResult(ResumeResult);
        }

        public Task<ControlPlaneThreadSummary?> ForkThreadAsync(ControlPlaneForkThreadCommand command, CancellationToken cancellationToken)
        {
            ForkThreadIds.Add(command.ThreadId.Value);
            return Task.FromResult(ForkResult);
        }

        public Task<ControlPlaneThreadSummary?> StartThreadAsync(ControlPlaneStartThreadCommand command, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<bool> DeleteThreadAsync(ControlPlaneDeleteThreadCommand command, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ControlPlaneClearThreadsResult> ClearThreadsAsync(ControlPlaneClearThreadsCommand command, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<bool> RenameThreadAsync(ControlPlaneRenameThreadCommand command, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<bool> ArchiveThreadAsync(ControlPlaneArchiveThreadCommand command, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ControlPlaneTurnSubmissionResult> SubmitTurnAsync(ControlPlaneSubmitTurnCommand command, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ControlPlaneTurnSubmissionResult> SubmitFollowUpAsync(ControlPlaneSubmitFollowUpCommand command, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ControlPlanePendingFollowUpMutationResult> MutatePendingFollowUpAsync(ControlPlaneMutatePendingFollowUpCommand command, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task InterruptTurnAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ThreadViewProjection?> GetThreadProjectionAsync(GetThreadProjection query, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ControlPlaneLoadedThreadListResult> ListLoadedThreadsAsync(ControlPlaneLoadedThreadListQuery query, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ControlPlaneThreadOperationResult> ReadThreadAsync(ControlPlaneReadThreadQuery query, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ControlPlaneThreadCommandAcceptedResult> CompactThreadAsync(ControlPlaneCompactThreadCommand command, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ControlPlaneThreadCommandAcceptedResult> CleanBackgroundTerminalsAsync(ControlPlaneCleanBackgroundTerminalsCommand command, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ControlPlaneThreadUnsubscribeResult> UnsubscribeThreadAsync(ControlPlaneUnsubscribeThreadCommand command, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ControlPlaneThreadElicitationResult> IncrementThreadElicitationAsync(ControlPlaneIncrementThreadElicitationCommand command, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ControlPlaneThreadElicitationResult> DecrementThreadElicitationAsync(ControlPlaneDecrementThreadElicitationCommand command, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ControlPlaneThreadOperationResult> UnarchiveThreadAsync(ControlPlaneUnarchiveThreadCommand command, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ControlPlaneThreadOperationResult> UpdateThreadMetadataAsync(ControlPlaneUpdateThreadMetadataCommand command, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ControlPlaneThreadOperationResult> RollbackThreadAsync(ControlPlaneRollbackThreadCommand command, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public IAsyncEnumerable<ControlPlaneConversationStreamEvent> SubscribeStreamAsync(ThreadId? threadId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ControlPlaneFuzzyFileSearchResult> SearchFuzzyFilesAsync(ControlPlaneFuzzyFileSearchQuery query, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ControlPlaneFuzzyFileSearchCommandAcceptedResult> StartFuzzyFileSearchSessionAsync(ControlPlaneStartFuzzyFileSearchSessionCommand command, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ControlPlaneFuzzyFileSearchCommandAcceptedResult> UpdateFuzzyFileSearchSessionAsync(ControlPlaneUpdateFuzzyFileSearchSessionCommand command, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ControlPlaneFuzzyFileSearchCommandAcceptedResult> StopFuzzyFileSearchSessionAsync(ControlPlaneStopFuzzyFileSearchSessionCommand command, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ControlPlaneRealtimeCommandAcceptedResult> StartRealtimeAsync(ControlPlaneRealtimeStartCommand command, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ControlPlaneRealtimeCommandAcceptedResult> AppendRealtimeTextAsync(ControlPlaneRealtimeAppendTextCommand command, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ControlPlaneRealtimeCommandAcceptedResult> AppendRealtimeAudioAsync(ControlPlaneRealtimeAppendAudioCommand command, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ControlPlaneRealtimeCommandAcceptedResult> HandoffRealtimeOutputAsync(ControlPlaneRealtimeHandoffOutputCommand command, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ControlPlaneRealtimeCommandAcceptedResult> StopRealtimeAsync(ControlPlaneRealtimeStopCommand command, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
