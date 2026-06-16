using TianShu.Cli.Interaction.Commands;
using TianShu.Cli.Interaction.Projection;
using TianShu.Cli.Interaction.Rendering;
using TianShu.Contracts.Conversations;

namespace TianShu.Cli.Interaction;

internal sealed class InteractionPipeline
{
    private readonly ChatCommandLoop? commandLoop;
    private readonly ChatPresentationProjector presentationProjector = new();
    private readonly TerminalFrameBuilder terminalFrameBuilder = new();

    public InteractionPipeline()
    {
    }

    public InteractionPipeline(ChatCommandExecutionContext commandContext)
    {
        commandLoop = new ChatCommandLoop(commandContext);
    }

    public ChatPresentationState PresentationState => presentationProjector.State;

    public void ClearPlanDockState()
        => presentationProjector.ClearPlanDockState();

    public Task<bool> ExecuteInputLineAsync(
        string line,
        CancellationToken cancellationToken,
        ControlPlaneFollowUpMode? runningPlainTextMode = null)
    {
        if (commandLoop is null)
        {
            throw new InvalidOperationException("当前 InteractionPipeline 未配置命令执行上下文。");
        }

        return commandLoop.ExecuteInputLineAsync(line, cancellationToken, runningPlainTextMode);
    }

    public InteractionPipelineProjectionResult ProjectStreamEvent(ControlPlaneConversationStreamEvent streamEvent)
    {
        var projection = presentationProjector.Project(streamEvent);
        return new InteractionPipelineProjectionResult(projection, presentationProjector.State);
    }

    public ChatProjectionRecord? CaptureIncompleteAssistantSnapshot(DateTimeOffset timestamp, string reason)
    {
        var activeAssistantText = presentationProjector.State.ActiveAssistantText;
        if (string.IsNullOrEmpty(activeAssistantText))
        {
            return null;
        }

        return new ChatProjectionRecord(
            timestamp,
            "assistant_block_incomplete",
            activeAssistantText,
            "assistant",
            reason);
    }

    public TerminalChatFrame BuildPresentationBlockFrame(
        ChatPresentationBlock block,
        PlanDockSummary? plan,
        TerminalFrameBuildContext context)
        => terminalFrameBuilder.Build(
            new ChatPresentationState(
                [block],
                ActiveAssistantText: string.Empty,
                Plan: plan,
                ConversationOutputModel.FromBlocks([block])),
            context);

    public TerminalChatFrame BuildActiveAssistantFrame(
        string text,
        PlanDockSummary? plan,
        TerminalFrameBuildContext context)
        => terminalFrameBuilder.Build(
            new ChatPresentationState(
                Blocks: Array.Empty<ChatPresentationBlock>(),
                ActiveAssistantText: text,
                Plan: plan,
                Output: ConversationOutputModel.Empty),
            context);
}

internal sealed record InteractionPipelineProjectionResult(
    ChatPresentationProjection Projection,
    ChatPresentationState State);
