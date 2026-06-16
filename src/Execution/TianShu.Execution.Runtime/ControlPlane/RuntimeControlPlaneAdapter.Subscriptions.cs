using System.Runtime.CompilerServices;
using System.Threading.Channels;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Projections;
using TianShu.ControlPlane.Abstractions;
using TianShu.ControlPlane.Abstractions.Subscriptions;

namespace TianShu.Execution.Runtime.ControlPlane;

public sealed partial class RuntimeControlPlaneAdapter
{
    public IAsyncEnumerable<ControlPlaneConversationStreamEvent> SubscribeStreamAsync(
        ThreadId? threadId,
        CancellationToken cancellationToken)
        => SubscribeCoreAsync(
            static (streamEvent, state) => MatchesThread(streamEvent, state),
            static streamEvent => streamEvent,
            threadId,
            cancellationToken);

    public IAsyncEnumerable<ControlPlaneProjectionEvent> SubscribeThreadAsync(
        ControlPlaneThreadSubscription request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SubscribeCoreAsync(
            static (streamEvent, state) => IsThreadProjectionEvent(streamEvent) && MatchesThread(streamEvent, state),
            static streamEvent => ToControlPlaneProjectionEvent(streamEvent),
            request.ThreadId,
            cancellationToken);
    }

    public IAsyncEnumerable<ControlPlaneProjectionEvent> SubscribeWorkflowAsync(
        ControlPlaneWorkflowSubscription request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SubscribeCoreAsync(
            static (streamEvent, state) => IsWorkflowProjectionEvent(streamEvent) && MatchesThread(streamEvent, state),
            static streamEvent => ToControlPlaneProjectionEvent(streamEvent),
            request.ThreadId,
            cancellationToken);
    }

    public IAsyncEnumerable<ControlPlaneProjectionEvent> SubscribeAgentAsync(
        ControlPlaneAgentSubscription request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SubscribeCoreAsync(
            static (streamEvent, state) => IsAgentProjectionEvent(streamEvent) && MatchesThread(streamEvent, state),
            static streamEvent => ToControlPlaneProjectionEvent(streamEvent),
            request.ThreadId,
            cancellationToken);
    }

    public IAsyncEnumerable<ControlPlaneProjectionEvent> SubscribeGovernanceAsync(
        ControlPlaneGovernanceSubscription request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SubscribeCoreAsync(
            static (streamEvent, state) => IsGovernanceProjectionEvent(streamEvent) && MatchesThread(streamEvent, state),
            static streamEvent => ToControlPlaneProjectionEvent(streamEvent),
            request.ThreadId,
            cancellationToken);
    }

    private async IAsyncEnumerable<TItem> SubscribeCoreAsync<TItem>(
        Func<ControlPlaneConversationStreamEvent, ThreadId?, bool> filter,
        Func<ControlPlaneConversationStreamEvent, TItem> projector,
        ThreadId? threadId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<TItem>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
            });

        EventHandler<ControlPlaneConversationStreamEventArgs>? handler = null;
        handler = (_, args) =>
        {
            var streamEvent = args.StreamEvent;
            if (!filter(streamEvent, threadId))
            {
                return;
            }

            channel.Writer.TryWrite(projector(streamEvent));
        };

        runtime.StreamEventReceived += handler;
        using var registration = cancellationToken.Register(static state => ((ChannelWriter<TItem>)state!).TryComplete(), channel.Writer);

        try
        {
            while (await channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out var item))
                {
                    yield return item;
                }
            }
        }
        finally
        {
            runtime.StreamEventReceived -= handler;
            channel.Writer.TryComplete();
        }
    }

    private static bool MatchesThread(ControlPlaneConversationStreamEvent streamEvent, ThreadId? threadId)
        => threadId is null
           || (streamEvent.ThreadId is not null
               && string.Equals(streamEvent.ThreadId.Value, threadId.Value, StringComparison.Ordinal));

    private static bool IsThreadProjectionEvent(ControlPlaneConversationStreamEvent streamEvent)
        => streamEvent.Kind
            is ControlPlaneConversationStreamEventKind.Info
            or ControlPlaneConversationStreamEventKind.TurnStarted
            or ControlPlaneConversationStreamEventKind.AssistantTextDelta
            or ControlPlaneConversationStreamEventKind.AssistantTextCompleted
            or ControlPlaneConversationStreamEventKind.ToolCallStarted
            or ControlPlaneConversationStreamEventKind.ToolCallOutputDelta
            or ControlPlaneConversationStreamEventKind.ToolCallCompleted
            or ControlPlaneConversationStreamEventKind.TurnCompleted
            or ControlPlaneConversationStreamEventKind.Error
            or ControlPlaneConversationStreamEventKind.ReasoningDelta
            or ControlPlaneConversationStreamEventKind.PlanUpdated
            or ControlPlaneConversationStreamEventKind.ThreadStatusChanged
            or ControlPlaneConversationStreamEventKind.ThreadNameUpdated
            or ControlPlaneConversationStreamEventKind.ThreadTokenUsageUpdated
            or ControlPlaneConversationStreamEventKind.ThreadCompacted
            or ControlPlaneConversationStreamEventKind.CommandExecOutputDelta
            or ControlPlaneConversationStreamEventKind.ThreadRealtimeItemAdded
            or ControlPlaneConversationStreamEventKind.ThreadRealtimeOutputAudioDelta
            or ControlPlaneConversationStreamEventKind.ThreadRealtimeError
            or ControlPlaneConversationStreamEventKind.ThreadRealtimeClosed
            or ControlPlaneConversationStreamEventKind.UserMessageCommitted
            or ControlPlaneConversationStreamEventKind.PendingFollowUpUpdated;

    private static bool IsWorkflowProjectionEvent(ControlPlaneConversationStreamEvent streamEvent)
        => streamEvent.Kind
            is ControlPlaneConversationStreamEventKind.PlanUpdated
            or ControlPlaneConversationStreamEventKind.TaskStarted
            or ControlPlaneConversationStreamEventKind.TaskCompleted
            or ControlPlaneConversationStreamEventKind.OperationReported
            or ControlPlaneConversationStreamEventKind.AgentJobProgress;

    private static bool IsAgentProjectionEvent(ControlPlaneConversationStreamEvent streamEvent)
        => streamEvent.Kind
            is ControlPlaneConversationStreamEventKind.ItemStarted
            or ControlPlaneConversationStreamEventKind.ItemCompleted
            or ControlPlaneConversationStreamEventKind.ModelRerouted
            or ControlPlaneConversationStreamEventKind.HookStarted
            or ControlPlaneConversationStreamEventKind.HookCompleted
            or ControlPlaneConversationStreamEventKind.ThreadStatusChanged
            or ControlPlaneConversationStreamEventKind.ThreadNameUpdated;

    private static bool IsGovernanceProjectionEvent(ControlPlaneConversationStreamEvent streamEvent)
        => streamEvent.Kind
            is ControlPlaneConversationStreamEventKind.ApprovalRequested
            or ControlPlaneConversationStreamEventKind.PermissionRequested
            or ControlPlaneConversationStreamEventKind.UserInputRequested
            or ControlPlaneConversationStreamEventKind.ServerRequestResolved;

    private static ControlPlaneProjectionEvent ToControlPlaneProjectionEvent(ControlPlaneConversationStreamEvent streamEvent)
        => new()
        {
            Kind = streamEvent.Kind.ToString(),
            Timestamp = streamEvent.Timestamp,
            ThreadId = streamEvent.ThreadId,
            TurnId = streamEvent.TurnId,
            ItemId = streamEvent.ItemId,
            CallId = streamEvent.CallId,
            ToolName = streamEvent.ToolName,
            ServerName = streamEvent.ServerName,
            Text = streamEvent.Text,
            Status = streamEvent.Status,
            Phase = streamEvent.Phase,
            Message = streamEvent.Message,
            WillRetry = streamEvent.WillRetry,
            RequiresApproval = streamEvent.RequiresApproval,
            ApprovalKind = streamEvent.ApprovalKind,
            SourceMethod = streamEvent.SourceMethod,
            OperationName = streamEvent.OperationName,
            Payload = streamEvent.Payload,
        };
}
