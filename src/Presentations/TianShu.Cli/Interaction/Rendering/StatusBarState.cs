namespace TianShu.Cli.Interaction.Rendering;

/// <summary>
/// Describes the bottom status bar view model.
/// 描述底部状态栏的展示模型。
/// </summary>
internal sealed record StatusBarState(
    bool IsBusy,
    TimeSpan? WorkingElapsed,
    AgentDockSummary? Agents,
    ModelDockSummary? Model);
