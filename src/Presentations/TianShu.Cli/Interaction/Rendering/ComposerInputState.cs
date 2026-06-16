namespace TianShu.Cli.Interaction.Rendering;

/// <summary>
/// Describes the composer input line view model.
/// 描述输入区文本、光标与提示符的展示模型。
/// </summary>
internal sealed record ComposerInputState(
    string Text,
    int Cursor,
    string Prompt,
    string? Placeholder = null);
