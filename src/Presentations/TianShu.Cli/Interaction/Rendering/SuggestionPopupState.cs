namespace TianShu.Cli.Interaction.Rendering;

/// <summary>
/// Describes the slash-command suggestion popup view model.
/// 描述 slash-command 建议弹窗的展示模型。
/// </summary>
internal sealed record SuggestionPopupState(
    IReadOnlyList<string> Items,
    int SelectedIndex,
    int ViewportStart,
    int ViewportSize,
    bool CanSubmit);
