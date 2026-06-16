using TianShu.Cli.Interaction.Projection;
using TianShu.Cli.Interaction.Recording;
using TianShu.Contracts.Conversations;
using TianShu.Execution.Runtime;

namespace TianShu.Cli.Interaction.Orchestration;

/// <summary>
/// Consumes runtime stream events and applies CLI chat lifecycle side effects.
/// 消费运行时流事件，并应用 CLI chat 生命周期副作用。
/// </summary>
internal sealed class ChatStreamEventConsumer
{
    public void Handle(
        ChatStreamEventConsumerContext context,
        ControlPlaneConversationStreamEvent streamEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(streamEvent);

        context.TouchConversationActivity();
        context.SessionState.ObserveStreamEvent(streamEvent);

        if (streamEvent.Kind == ControlPlaneConversationStreamEventKind.TurnCompleted)
        {
            context.ConversationActivity.MarkTerminalTurn();
        }
        else if (streamEvent.TurnId is not null)
        {
            context.ConversationActivity.MarkTurnObserved();
        }

        context.InteractionRecorder.RecordStreamEvent(streamEvent);
        var projectionResult = context.RecordPresentationProjection(streamEvent);
        context.EventWaiter.RecordObservedEventAndNotifyWaiters(streamEvent);

        switch (streamEvent.Kind)
        {
            case ControlPlaneConversationStreamEventKind.TurnStarted:
                context.SessionState.ApplyTurnStatus(streamEvent.Status);
                break;
            case ControlPlaneConversationStreamEventKind.AssistantTextDelta:
                HandleAssistantTextDelta(context, streamEvent);
                break;
            case ControlPlaneConversationStreamEventKind.AssistantTextCompleted:
                context.WriteProjectionCommittedBlocks(projectionResult.Projection.CommittedBlocks, true);
                context.SetAssistantLineOpen(false);
                break;
            case ControlPlaneConversationStreamEventKind.ToolCallStarted:
            case ControlPlaneConversationStreamEventKind.ToolCallCompleted:
                context.WriteProjectionCommittedBlocks(projectionResult.Projection.CommittedBlocks, true);
                context.WriteVerboseToolEventIfRequested(streamEvent);
                break;
            case ControlPlaneConversationStreamEventKind.ItemStarted:
            case ControlPlaneConversationStreamEventKind.ItemCompleted:
                context.WriteProjectionCommittedBlocks(projectionResult.Projection.CommittedBlocks, true);
                context.WriteVerboseEventIfRequested(streamEvent);
                break;
            case ControlPlaneConversationStreamEventKind.PlanUpdated:
                context.WriteProjectionCommittedBlocks(projectionResult.Projection.CommittedBlocks, true);
                context.RefreshInlineTailPrompt();
                context.WriteVerboseEventIfRequested(streamEvent);
                break;
            case ControlPlaneConversationStreamEventKind.ApprovalRequested:
                context.CloseAssistantLineIfOpen();
                context.PendingInteractiveAutomation.HandleApprovalRequested(
                    context.Runtime,
                    context.PendingInteractiveRequests,
                    streamEvent,
                    context.Options.ApproveAll,
                    context.Options.ApprovalDecision,
                    context.WriteLine,
                    cancellationToken);
                break;
            case ControlPlaneConversationStreamEventKind.PermissionRequested:
                context.CloseAssistantLineIfOpen();
                context.PendingInteractiveAutomation.HandlePermissionRequested(
                    context.Runtime,
                    context.PendingInteractiveRequests,
                    streamEvent,
                    context.PermissionScript,
                    context.WriteLine,
                    cancellationToken);
                break;
            case ControlPlaneConversationStreamEventKind.UserInputRequested:
                context.CloseAssistantLineIfOpen();
                context.PendingInteractiveAutomation.HandleUserInputRequested(
                    context.Runtime,
                    context.PendingInteractiveRequests,
                    streamEvent,
                    context.UserInputScript,
                    context.WriteLine,
                    cancellationToken);
                break;
            case ControlPlaneConversationStreamEventKind.ServerRequestResolved:
                if (!string.IsNullOrWhiteSpace(streamEvent.CallId?.Value))
                {
                    context.ClearPendingInteractiveRequest(streamEvent.CallId!.Value);
                }
                break;
            case ControlPlaneConversationStreamEventKind.TurnCompleted:
                HandleTurnCompleted(context, streamEvent, projectionResult);
                break;
            case ControlPlaneConversationStreamEventKind.PendingFollowUpUpdated:
                context.TrackPendingGuidanceMessage(streamEvent);
                context.TryWriteCommittedGuidanceMessage(streamEvent);
                context.WriteVerboseEventIfRequested(streamEvent);
                break;
            case ControlPlaneConversationStreamEventKind.UserMessageCommitted:
                context.TryWriteCommittedGuidanceMessage(streamEvent);
                context.WriteVerboseEventIfRequested(streamEvent);
                break;
            case ControlPlaneConversationStreamEventKind.Error:
                context.WriteProjectionCommittedBlocks(
                    projectionResult.Projection.CommittedBlocks,
                    streamEvent.WillRetry != true);
                break;
            default:
                context.WriteVerboseEventIfRequested(streamEvent);
                break;
        }
    }

    private static void HandleAssistantTextDelta(
        ChatStreamEventConsumerContext context,
        ControlPlaneConversationStreamEvent streamEvent)
    {
        if (string.IsNullOrEmpty(streamEvent.Text))
        {
            return;
        }

        if (context.GetAssistantLeadingSpacerPending()
            && !context.HasRetainedTailFrame()
            && !context.GetAssistantLineOpen())
        {
            context.WriteTerminalVisualSpacerLine();
        }

        context.SetAssistantLeadingSpacerPending(false);
        context.Write(streamEvent.Text);
        context.SetAssistantLineOpen(true);
    }

    private static void HandleTurnCompleted(
        ChatStreamEventConsumerContext context,
        ControlPlaneConversationStreamEvent streamEvent,
        InteractionPipelineProjectionResult projectionResult)
    {
        context.WriteProjectionCommittedBlocks(projectionResult.Projection.CommittedBlocks, true);
        context.SetAssistantLineOpen(false);

        context.SessionState.ApplyTurnStatus(streamEvent.Status);
        context.ClearPendingInteractiveRequestsForTurn(
            streamEvent.ThreadId?.Value ?? context.SessionState.SessionActiveThreadId,
            streamEvent.TurnId?.Value);
        context.WriteLifecycleDebug(
            $"回合完成：thread={streamEvent.ThreadId?.Value ?? context.SessionState.SessionActiveThreadId ?? "<none>"}, turn={streamEvent.TurnId?.Value ?? "<none>"}, status={streamEvent.Status ?? "completed"}");
        if (context.IsFailedTurnStatus(streamEvent.Status))
        {
            context.MarkFailure();
        }

        if (IsInterruptedTurnStatus(streamEvent.Status))
        {
            context.ClearPlanDockState();
            context.WriteInterruptedControlOutput();
        }

        context.TryPromotePendingRestoredFollowUpAfterTurnCompleted(streamEvent);
        context.RefreshInlineTailPrompt();
    }

    private static bool IsInterruptedTurnStatus(string? status)
        => string.Equals(status?.Trim(), "interrupted", StringComparison.OrdinalIgnoreCase);
}

internal sealed class ChatStreamEventConsumerContext
{
    public required IExecutionRuntime Runtime { get; init; }

    public required ChatCommandOptions Options { get; init; }

    public ProbePermissionRequestScript? PermissionScript { get; init; }

    public ProbeUserInputScript? UserInputScript { get; init; }

    public required ChatSessionState SessionState { get; init; }

    public required ConversationActivityTracker ConversationActivity { get; init; }

    public required ChatInteractionRecorder InteractionRecorder { get; init; }

    public required ConversationEventWaiter EventWaiter { get; init; }

    public required PendingInteractiveRequestStore PendingInteractiveRequests { get; init; }

    public required PendingInteractiveAutomationHandler PendingInteractiveAutomation { get; init; }

    public required Action TouchConversationActivity { get; init; }

    public required Func<ControlPlaneConversationStreamEvent, InteractionPipelineProjectionResult> RecordPresentationProjection { get; init; }

    public required Action<IReadOnlyList<ChatPresentationBlock>, bool> WriteProjectionCommittedBlocks { get; init; }

    public required Action<ControlPlaneConversationStreamEvent> WriteVerboseToolEventIfRequested { get; init; }

    public required Action<ControlPlaneConversationStreamEvent> WriteVerboseEventIfRequested { get; init; }

    public required Action<string, bool> WriteLine { get; init; }

    public required Action<string> Write { get; init; }

    public required Action WriteTerminalVisualSpacerLine { get; init; }

    public required Action CloseAssistantLineIfOpen { get; init; }

    public required Action RefreshInlineTailPrompt { get; init; }

    public required Action<string> ClearPendingInteractiveRequest { get; init; }

    public required Action<string?, string?> ClearPendingInteractiveRequestsForTurn { get; init; }

    public required Action<string> WriteLifecycleDebug { get; init; }

    public required Func<string?, bool> IsFailedTurnStatus { get; init; }

    public required Action MarkFailure { get; init; }

    public required Action<ControlPlaneConversationStreamEvent> TryWriteCommittedGuidanceMessage { get; init; }

    public required Action<ControlPlaneConversationStreamEvent> TrackPendingGuidanceMessage { get; init; }

    public required Action<ControlPlaneConversationStreamEvent> TryPromotePendingRestoredFollowUpAfterTurnCompleted { get; init; }

    public required Action ClearPlanDockState { get; init; }

    public required Action WriteInterruptedControlOutput { get; init; }

    public required Func<bool> GetAssistantLineOpen { get; init; }

    public required Action<bool> SetAssistantLineOpen { get; init; }

    public required Func<bool> GetAssistantLeadingSpacerPending { get; init; }

    public required Action<bool> SetAssistantLeadingSpacerPending { get; init; }

    public required Func<bool> HasRetainedTailFrame { get; init; }
}
