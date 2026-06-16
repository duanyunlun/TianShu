using TianShu.Cli.Interaction.Commands.Threads;
using TianShu.ControlPlane.Abstractions.Conversations;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;
using ThreadViewProjection = TianShu.Contracts.Projections.ThreadProjection;

namespace TianShu.Cli.Tests;

public sealed class InteractiveThreadCommandHandlerTests
{
    [Fact]
    public async Task HandleDeleteThreadAsync_WhenDeletingCurrentThread_ClearsBeforeAndAfterDelete()
    {
        var conversations = new FakeConversationControlPlane { DeleteResult = true };
        var clearCount = 0;
        var clearedHistory = new List<string>();
        var messages = new List<(string Message, bool IsError)>();
        var context = CreateContext(
            isCurrentThread: static threadId => threadId == "thread-delete-001",
            clearCurrentThreadState: () => clearCount++,
            clearInputHistoryForThread: clearedHistory.Add,
            messages: messages);

        await new InteractiveThreadCommandHandler().HandleThreadLifecycleCommandAsync(
            conversations,
            "delete --thread-id thread-delete-001 --confirm thread-delete-001",
            context,
            CancellationToken.None);

        Assert.Equal(["thread-delete-001"], conversations.DeleteThreadIds);
        Assert.Equal(2, clearCount);
        Assert.Equal(["thread-delete-001"], clearedHistory);
        Assert.Contains(messages, static item => item.Message == "已删除线程：thread-delete-001" && !item.IsError);
    }

    [Fact]
    public async Task HandleDeleteThreadAsync_WhenConfirmationMismatches_DoesNotCallRuntime()
    {
        var conversations = new FakeConversationControlPlane { DeleteResult = true };
        var messages = new List<(string Message, bool IsError)>();

        await new InteractiveThreadCommandHandler().HandleThreadLifecycleCommandAsync(
            conversations,
            "delete --thread-id thread-delete-001 --confirm wrong",
            CreateContext(messages: messages),
            CancellationToken.None);

        Assert.Empty(conversations.DeleteThreadIds);
        Assert.Contains(messages, static item => item.Message == "已取消删除线程。" && !item.IsError);
    }

    [Fact]
    public async Task HandleClearThreadsAsync_WhenConfirmationMismatches_DoesNotCallRuntime()
    {
        var conversations = new FakeConversationControlPlane();
        var messages = new List<(string Message, bool IsError)>();

        await new InteractiveThreadCommandHandler().HandleThreadLifecycleCommandAsync(
            conversations,
            "clear --confirm delete-all",
            CreateContext(messages: messages),
            CancellationToken.None);

        Assert.Equal(0, conversations.ClearCallCount);
        Assert.Contains(messages, static item => item.Message == "已取消清空线程。" && !item.IsError);
    }

    [Fact]
    public async Task HandleClearThreadsAsync_WhenConfirmed_ClearsCurrentStateAndRuntime()
    {
        var conversations = new FakeConversationControlPlane { ClearDeletedCount = 3 };
        var clearCount = 0;
        var clearAllHistoryCount = 0;
        var messages = new List<(string Message, bool IsError)>();

        await new InteractiveThreadCommandHandler().HandleThreadLifecycleCommandAsync(
            conversations,
            "clear --confirm DELETE ALL THREADS",
            CreateContext(
                clearCurrentThreadState: () => clearCount++,
                clearAllInputHistory: () => clearAllHistoryCount++,
                messages: messages),
            CancellationToken.None);

        Assert.Equal(1, conversations.ClearCallCount);
        Assert.Equal(2, clearCount);
        Assert.Equal(1, clearAllHistoryCount);
        Assert.Contains(messages, static item => item.Message == "已清空线程：3 个。" && !item.IsError);
    }

    [Fact]
    public async Task HandleRenameThreadAsync_WhenMissingName_DoesNotCallRuntime()
    {
        var conversations = new FakeConversationControlPlane();
        var messages = new List<(string Message, bool IsError)>();

        await new InteractiveThreadCommandHandler().HandleRenameThreadAsync(
            conversations,
            "thread-only",
            CreateContext(messages: messages),
            CancellationToken.None);

        Assert.Empty(conversations.RenameCalls);
        Assert.Contains(messages, static item => item.Message == "用法：/rename <threadId> <name>" && item.IsError);
    }

    [Fact]
    public async Task HandleNewThreadAsync_WhenCreated_ActivatesThread()
    {
        var conversations = new FakeConversationControlPlane { StartThreadId = "thread-new-001" };
        ThreadId? activatedThreadId = null;
        var messages = new List<(string Message, bool IsError)>();

        await new InteractiveThreadCommandHandler().HandleNewThreadAsync(
            conversations,
            new ChatCommandOptions
            {
                RuntimeModel = "gpt-5.1",
                RuntimeModelProvider = "openai",
                WorkingDirectory = "D:/repo",
            },
            CreateContext(activateThread: threadId => activatedThreadId = threadId, messages: messages),
            CancellationToken.None);

        var command = Assert.Single(conversations.StartCommands);
        Assert.Equal("gpt-5.1", command.Model);
        Assert.Equal("openai", command.ModelProvider);
        Assert.Equal("D:/repo", command.WorkingDirectory);
        Assert.Equal("thread-new-001", activatedThreadId?.Value);
        Assert.Contains(messages, static item => item.Message == "已创建线程：thread-new-001" && !item.IsError);
    }

    [Fact]
    public void TryReadPlainThreadLifecycleCommand_OnlyAcceptsDeleteAndClear()
    {
        Assert.True(InteractiveThreadCommandHandler.TryReadPlainThreadLifecycleCommand("thread delete --thread-id t1", out var deleteRest));
        Assert.Equal("delete --thread-id t1", deleteRest);

        Assert.True(InteractiveThreadCommandHandler.TryReadPlainThreadLifecycleCommand("thread clear --confirm DELETE ALL THREADS", out var clearRest));
        Assert.Equal("clear --confirm DELETE ALL THREADS", clearRest);

        Assert.False(InteractiveThreadCommandHandler.TryReadPlainThreadLifecycleCommand("thread rename t1 name", out _));
        Assert.False(InteractiveThreadCommandHandler.TryReadPlainThreadLifecycleCommand("hello", out _));
    }

    private static InteractiveThreadCommandContext CreateContext(
        Func<bool>? hasRunningConversation = null,
        Func<string, bool>? isCurrentThread = null,
        Action? clearCurrentThreadState = null,
        Action<ThreadId>? activateThread = null,
        Action<string>? clearInputHistoryForThread = null,
        Action? clearAllInputHistory = null,
        List<(string Message, bool IsError)>? messages = null)
        => new(
            hasRunningConversation ?? (() => false),
            isCurrentThread ?? (_ => false),
            clearCurrentThreadState ?? (() => { }),
            activateThread ?? (_ => { }),
            static (expected, configured, _, _) => Task.FromResult(string.Equals(expected, configured, StringComparison.Ordinal)),
            static (_, _) => Task.FromResult<ControlPlaneThreadSnapshot?>(null),
            clearInputHistoryForThread ?? (_ => { }),
            clearAllInputHistory ?? (() => { }),
            (message, isError) => messages?.Add((message, isError)));

    private sealed class FakeConversationControlPlane : IConversationControlPlane
    {
        public List<ControlPlaneStartThreadCommand> StartCommands { get; } = [];

        public List<string> DeleteThreadIds { get; } = [];

        public List<(string ThreadId, string Name)> RenameCalls { get; } = [];

        public int ClearCallCount { get; private set; }

        public string? StartThreadId { get; init; }

        public bool DeleteResult { get; init; }

        public int ClearDeletedCount { get; init; }

        public Task<ControlPlaneThreadSummary?> StartThreadAsync(ControlPlaneStartThreadCommand command, CancellationToken cancellationToken)
        {
            StartCommands.Add(command);
            return Task.FromResult<ControlPlaneThreadSummary?>(StartThreadId is null
                ? null
                : new ControlPlaneThreadSummary
                {
                    ThreadId = new ThreadId(StartThreadId),
                });
        }

        public Task<bool> DeleteThreadAsync(ControlPlaneDeleteThreadCommand command, CancellationToken cancellationToken)
        {
            DeleteThreadIds.Add(command.ThreadId.Value);
            return Task.FromResult(DeleteResult);
        }

        public Task<ControlPlaneClearThreadsResult> ClearThreadsAsync(ControlPlaneClearThreadsCommand command, CancellationToken cancellationToken)
        {
            ClearCallCount++;
            return Task.FromResult(new ControlPlaneClearThreadsResult { DeletedCount = ClearDeletedCount });
        }

        public Task<bool> RenameThreadAsync(ControlPlaneRenameThreadCommand command, CancellationToken cancellationToken)
        {
            RenameCalls.Add((command.ThreadId.Value, command.Name));
            return Task.FromResult(true);
        }

        public Task<ControlPlaneThreadSummary?> ForkThreadAsync(ControlPlaneForkThreadCommand command, CancellationToken cancellationToken)
            => Task.FromResult<ControlPlaneThreadSummary?>(new ControlPlaneThreadSummary { ThreadId = new ThreadId(command.ThreadId.Value + "-fork") });

        public Task<bool> ArchiveThreadAsync(ControlPlaneArchiveThreadCommand command, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<ControlPlaneThreadSnapshot?> ResumeThreadAsync(ControlPlaneResumeThreadCommand command, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ControlPlaneTurnSubmissionResult> SubmitTurnAsync(ControlPlaneSubmitTurnCommand command, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ControlPlaneTurnSubmissionResult> SubmitFollowUpAsync(ControlPlaneSubmitFollowUpCommand command, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ControlPlanePendingFollowUpMutationResult> MutatePendingFollowUpAsync(ControlPlaneMutatePendingFollowUpCommand command, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task InterruptTurnAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ControlPlaneThreadListResult> ListThreadsAsync(ControlPlaneThreadListQuery query, CancellationToken cancellationToken)
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
