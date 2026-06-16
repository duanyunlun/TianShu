using TianShu.Cli.Interaction.Events;
using TianShu.Cli.Interaction.Presenters;
using TianShu.Cli.Interaction.Rendering;

namespace TianShu.Cli.Interaction.Projection;

internal sealed class PlanDockStateProjector
{
    public PlanDockSummary? Current { get; private set; }

    public void Clear()
        => Current = null;

    public PlanProgressBlock? Project(PlanUpdatedInteractionEvent planUpdated, out PlanDockSummary? summary)
    {
        var block = PlanProgressPresenter.FromPayload(planUpdated.Explanation, planUpdated.Payload);
        summary = Project(block);
        return block;
    }

    public PlanDockSummary? Project(PlanProgressBlock? block)
    {
        Current = BuildSummary(block);
        return Current;
    }

    public static PlanDockSummary? BuildSummary(PlanProgressBlock? block)
        => block is null || block.TotalCount == 0
            ? null
            : new PlanDockSummary(
                block.CompletedCount,
                block.TotalCount,
                block.CurrentStep,
                block.Title,
                block.Steps.Select(static step => new PlanDockStep(
                    step.Sequence,
                    step.Text,
                    step.Status switch
                    {
                        PlanStepPresentationStatus.Completed => PlanDockStepStatus.Completed,
                        PlanStepPresentationStatus.Running => PlanDockStepStatus.Running,
                        PlanStepPresentationStatus.Failed => PlanDockStepStatus.Failed,
                        _ => PlanDockStepStatus.Pending,
                    })).ToArray());
}
