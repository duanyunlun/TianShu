using TianShu.Contracts.Conversations;
using TianShu.Cli.Interaction.Events;
using TianShu.Cli.Interaction.Presenters;
using TianShu.Cli.Interaction.Rendering;

namespace TianShu.Cli.Interaction.Projection;

internal sealed class ChatPresentationProjector
{
    private readonly List<ChatPresentationBlock> blocks = [];
    private readonly AssistantTextAccumulator assistant = new();
    private readonly ToolInvocationAccumulator tools = new();
    private readonly PlanDockStateProjector planDock = new();
    private readonly ConversationOutputBuilder output = new();
    private readonly HashSet<string> completedToolKeys = new(StringComparer.Ordinal);
    private PlanDockSummary? plan;

    public ChatPresentationState State => new(blocks.ToArray(), assistant.Text, plan, output.ToModel());

    public void ClearPlanDockState()
    {
        planDock.Clear();
        plan = null;
    }

    public ChatPresentationProjection Project(ControlPlaneConversationStreamEvent streamEvent)
        => Project(CliEventNormalizer.Normalize(streamEvent));

    public ChatPresentationProjection Project(CliInteractionEvent interactionEvent)
    {
        var committedBlocks = new List<ChatPresentationBlock>();
        var records = new List<ChatProjectionRecord>();

        switch (interactionEvent)
        {
            case AssistantTextDeltaEvent assistantDelta:
                assistant.Append(assistantDelta.Text);
                records.Add(new ChatProjectionRecord(
                    assistantDelta.Timestamp,
                    "assistant_delta_received",
                    assistantDelta.Text,
                    "assistant"));
                break;

            case AssistantTextCompletedEvent assistantCompleted:
                CommitAssistantBlock(committedBlocks, records, assistantCompleted.Timestamp, "assistant_completed", isComplete: true);
                break;

            case ToolInvocationEvent toolEvent:
                if (ToolInvocationProjectionPolicy.IsInternalToolEvent(toolEvent))
                {
                    break;
                }

                CommitAssistantBlock(committedBlocks, records, toolEvent.Timestamp, "before_tool", isComplete: false);
                var snapshot = tools.Merge(toolEvent);
                records.Add(new ChatProjectionRecord(
                    toolEvent.Timestamp,
                    toolEvent.InvocationPhase == ToolInvocationPhase.Started ? "tool_input_cached" : "tool_event_merged",
                    snapshot.InputText,
                    "tool",
                    snapshot.ToolName));
                if (toolEvent.InvocationPhase == ToolInvocationPhase.Completed)
                {
                    var completionKey = ToolInvocationProjectionPolicy.ResolveCompletionKey(toolEvent, snapshot);
                    if (!completedToolKeys.Add(completionKey)
                        || ToolInvocationProjectionPolicy.ShouldSuppressCompletedDisplay(snapshot))
                    {
                        break;
                    }

                    var toolBlock = ToolInvocationPresenter.BuildCompleted(
                        snapshot.ToolName,
                        snapshot.InputText,
                        snapshot.OutputText,
                        snapshot.Status,
                        snapshot.Payload);
                    if (toolBlock is not null)
                    {
                        blocks.Add(toolBlock);
                        output.AddToolBlock(toolBlock);
                        committedBlocks.Add(toolBlock);
                        records.Add(new ChatProjectionRecord(
                            toolEvent.Timestamp,
                            "tool_block_committed",
                            toolBlock.Summary,
                            "tool",
                            toolBlock.TitleText));
                    }
                }
                break;

            case PlanUpdatedInteractionEvent planUpdated:
                CommitAssistantBlock(committedBlocks, records, planUpdated.Timestamp, "before_plan", isComplete: false);
                planDock.Project(planUpdated, out plan);
                records.Add(new ChatProjectionRecord(
                    planUpdated.Timestamp,
                    "dock_state_updated",
                    plan?.CurrentStep,
                    "plan"));
                break;

            case ErrorInteractionEvent error:
                CommitAssistantBlock(committedBlocks, records, error.Timestamp, "before_error", isComplete: false);
                var errorBlock = ErrorNoticePresenter.BuildBlock(error.Message);
                blocks.Add(errorBlock);
                output.AddNoticeBlock(errorBlock);
                committedBlocks.Add(errorBlock);
                records.Add(new ChatProjectionRecord(error.Timestamp, "error_block_committed", error.Message, "error"));
                break;

            case TurnCompletedInteractionEvent turnCompleted:
                CommitAssistantBlock(committedBlocks, records, turnCompleted.Timestamp, "turn_completed", isComplete: true);
                records.Add(new ChatProjectionRecord(turnCompleted.Timestamp, "turn_completed", turnCompleted.Status, "turn"));
                break;
        }

        return new ChatPresentationProjection(committedBlocks, plan, records);
    }

    private void CommitAssistantBlock(
        ICollection<ChatPresentationBlock> committedBlocks,
        ICollection<ChatProjectionRecord> records,
        DateTimeOffset timestamp,
        string reason,
        bool isComplete)
    {
        var block = assistant.Commit(isComplete);
        if (block is null)
        {
            return;
        }

        blocks.Add(block);
        output.AddAssistantBlock(block);
        committedBlocks.Add(block);
        records.Add(new ChatProjectionRecord(
            timestamp,
            "assistant_block_committed",
            block.Text,
            "assistant",
            reason));
    }
}
