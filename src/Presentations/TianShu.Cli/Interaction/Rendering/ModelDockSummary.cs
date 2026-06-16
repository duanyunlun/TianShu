namespace TianShu.Cli.Interaction.Rendering;

/// <summary>
/// Describes the active model summary shown in the status bar.
/// 描述状态栏中展示的当前模型摘要。
/// </summary>
internal sealed record ModelDockSummary(
    string? Model,
    string? Provider = null,
    string? Route = null,
    string? Protocol = null);
