namespace TianShu.Cli.Terminal;

/// <summary>
/// Captures the current prompt frame rendered by the TianShu terminal shell.
/// 表示 TianShu 终端交互壳当前输入提示帧。
/// </summary>
internal readonly record struct TerminalRenderFrame(
    string Prompt,
    string Text,
    int Cursor,
    IReadOnlyList<string>? PopupLines = null,
    string? PlaceholderText = null,
    IReadOnlyList<string>? AboveInputLines = null,
    string? FooterLine = null,
    IReadOnlyList<string>? FooterLines = null,
    int LeadingBlankLineCount = 0,
    IReadOnlyList<string>? OverrideLines = null);
