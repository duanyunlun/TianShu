namespace TianShu.Cli.Interaction.Rendering;

/// <summary>
/// Captures one retained terminal chat frame before it is rendered.
/// 捕获一次终端聊天 retained frame 渲染前的完整状态。
/// </summary>
internal sealed record TerminalChatFrame(
    IReadOnlyList<string> TranscriptLines,
    ComposerDockState Dock,
    IReadOnlyList<string>? PopupLines,
    int Width);
