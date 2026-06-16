namespace TianShu.Cli.Interaction.Rendering;

/// <summary>
/// Describes the rendered plan dock panel state.
/// 描述已投影到计划 Dock 面板的展示状态。
/// </summary>
internal sealed record PlanDockState(
    int CompletedCount,
    int TotalCount,
    string? CurrentStep,
    string? Title,
    IReadOnlyList<PlanDockStep>? Steps);

/// <summary>
/// Describes the latest plan progress summary consumed by the Dock.
/// 描述 Dock 消费的最新计划进度摘要。
/// </summary>
internal sealed record PlanDockSummary(
    int CompletedCount,
    int TotalCount,
    string? CurrentStep,
    string? Title = null,
    IReadOnlyList<PlanDockStep>? Steps = null);

/// <summary>
/// Describes one typed plan step in the Dock.
/// 描述 Dock 中一个类型化计划步骤。
/// </summary>
internal sealed record PlanDockStep(
    string Sequence,
    string Text,
    PlanDockStepStatus Status);

internal enum PlanDockStepStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
}
