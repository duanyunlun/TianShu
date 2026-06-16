using TianShu.Cli.Interaction.Rendering;

namespace TianShu.Cli.Interaction.Host;

/// <summary>
/// Provides terminal-specific operations used by chat output writing.
/// 提供 chat 输出写入所需的终端专用操作，隔离 retained frame 与 composer dock 渲染细节。
/// </summary>
internal interface IChatOutputTerminalHost
{
    TerminalFrameBuildContext BuildTerminalFrameContextFromDock(
        ComposerDockState dockState,
        bool includePopupLines,
        bool styled);

    ComposerDockState BuildCurrentComposerDockState();

    bool PrepareInlineTailPromptWrite(bool assistantLineOpen);

    void RestoreInlineTailPrompt();

    void RenderAssistantRetainedTailFrameUnsafe();

    void RefreshAndRestoreInlineTailPrompt();

    IDisposable BeginCommandOverlay(Action? onEscape = null);

    void SetCommandOverlayLines(IReadOnlyList<string> lines);

    void WriteHumanTerminalRetainedText(string text, bool isError);

    void WriteHumanTerminalPresentationBlock(ChatPresentationBlock block, bool isError);
}
