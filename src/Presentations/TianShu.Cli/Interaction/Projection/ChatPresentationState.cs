using TianShu.Cli.Interaction.Rendering;

namespace TianShu.Cli.Interaction.Projection;

internal sealed record ChatPresentationState(
    IReadOnlyList<ChatPresentationBlock> Blocks,
    string ActiveAssistantText,
    PlanDockSummary? Plan,
    ConversationOutputModel Output)
{
    public bool HasActiveAssistantText => !string.IsNullOrEmpty(ActiveAssistantText);

    public ConversationOutputModel OutputModel => Output;
}
