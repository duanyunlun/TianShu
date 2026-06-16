using TianShu.Cli.Interaction.Rendering;

namespace TianShu.Cli.Interaction.Projection;

internal sealed record ChatPresentationProjection(
    IReadOnlyList<ChatPresentationBlock> CommittedBlocks,
    PlanDockSummary? Plan,
    IReadOnlyList<ChatProjectionRecord> Records);
