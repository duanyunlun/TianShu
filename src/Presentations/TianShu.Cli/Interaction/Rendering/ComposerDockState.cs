namespace TianShu.Cli.Interaction.Rendering;

internal sealed record ComposerDockState(
    string InputText,
    int Cursor,
    string Prompt,
    PlanDockSummary? Plan,
    AgentDockSummary? Agents,
    ModelDockSummary? Model,
    bool IsBusy,
    TimeSpan? WorkingElapsed = null,
    QueuedFollowUpDockState? QueuedFollowUps = null,
    string? InputNotice = null,
    CommandOverlayDockState? CommandOverlay = null)
{
    public ComposerInputState Input => new(InputText, Cursor, Prompt);

    public StatusBarState StatusBar => new(IsBusy, WorkingElapsed, Agents, Model);

    public PlanDockState? PlanPanel => Plan is null
        ? null
        : new PlanDockState(Plan.CompletedCount, Plan.TotalCount, Plan.CurrentStep, Plan.Title, Plan.Steps);
}

internal sealed record QueuedFollowUpDockState(
    int Count,
    IReadOnlyList<QueuedFollowUpDockEntryState> Entries,
    int? SelectedIndex = null);

internal sealed record QueuedFollowUpDockEntryState(
    int Index,
    string Preview,
    bool IsSelected = false);

internal sealed record CommandOverlayDockState(IReadOnlyList<string> Lines);
