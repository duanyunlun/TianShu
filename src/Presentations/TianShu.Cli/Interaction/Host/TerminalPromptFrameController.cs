using TianShu.Cli.Interaction.Rendering;
using TianShu.Cli.Terminal;

namespace TianShu.Cli.Interaction.Host;

/// <summary>
/// Owns the current prompt frame and its visible/hidden lifecycle.
/// 持有当前输入提示帧及其显示、隐藏、恢复生命周期。
/// </summary>
internal sealed class TerminalPromptFrameController
{
    private readonly Func<IDisposable> hideCursorForRefresh;
    private TerminalPromptRenderer? renderer;
    private TerminalRenderFrame? frame;
    private bool visible;

    public TerminalPromptFrameController(Func<IDisposable> hideCursorForRefresh)
        => this.hideCursorForRefresh = hideCursorForRefresh ?? throw new ArgumentNullException(nameof(hideCursorForRefresh));

    public TerminalRenderFrame? CurrentFrame => frame;

    public bool IsVisible => visible;

    public bool HasRenderableFrame => renderer is not null && frame is not null;

    public void SetFrame(TerminalPromptRenderer renderer, TerminalRenderFrame frame)
    {
        this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        this.frame = frame;
    }

    public void RenderCurrent()
    {
        if (renderer is null || frame is null)
        {
            return;
        }

        visible = true;
        Console.Write(renderer.Render(frame.Value));
    }

    public void CompleteInputLine(TerminalPromptRenderer? fallbackRenderer, bool addVisualSpacerAfter, bool clearSubmittedInput)
    {
        if (clearSubmittedInput && renderer is not null)
        {
            Console.Write(renderer.ClearFrame());
            visible = false;
            return;
        }

        Console.Write(fallbackRenderer?.CompleteLine() ?? Environment.NewLine);
        visible = false;
        if (addVisualSpacerAfter)
        {
            Console.WriteLine();
        }
    }

    public bool ClearForInlineTailWrite()
    {
        if (!visible || renderer is null || frame is null)
        {
            return false;
        }

        Console.Write(renderer.ClearFrame());
        frame = frame.Value with
        {
            LeadingBlankLineCount = 0,
        };
        visible = false;

        return true;
    }

    public bool ClearVisibleFrame()
    {
        if (!visible || renderer is null)
        {
            return false;
        }

        Console.Write(renderer.ClearFrame());
        visible = false;
        return true;
    }

    public void RestoreInlineTailPrompt()
    {
        if (renderer is null || frame is null)
        {
            return;
        }

        using var cursorScope = hideCursorForRefresh();
        var nextFrame = visible
            ? frame.Value
            : frame.Value with
            {
                LeadingBlankLineCount = TerminalChatFrameRenderer.DockLeadingBlankLineCount,
            };
        frame = nextFrame;
        Console.Write(renderer.Render(nextFrame));
        visible = true;
    }

    public bool UpdateFooterLines(ComposerDockState state)
    {
        if (renderer is null || frame is null)
        {
            return false;
        }

        frame = frame.Value with
        {
            PopupLines = state.CommandOverlay is { Lines.Count: > 0 }
                ? null
                : frame.Value.PopupLines,
            AboveInputLines = ComposerDockRenderer.BuildPromptAboveInputLines(
                state,
                styled: true,
                TerminalLayoutCalculator.SafeWritableWidth()),
            FooterLine = null,
            FooterLines = ComposerDockRenderer.BuildPromptFooterLines(
                state,
                styled: true,
                TerminalLayoutCalculator.SafeWritableWidth()),
            LeadingBlankLineCount = Math.Max(
                frame.Value.LeadingBlankLineCount,
                TerminalChatFrameRenderer.DockLeadingBlankLineCount),
            OverrideLines = state.CommandOverlay is { Lines.Count: > 0 }
                ? new ComposerDockRenderer().BuildDockLines(
                    state,
                    popupLines: null,
                    styled: true,
                    TerminalLayoutCalculator.SafeWritableWidth())
                : null,
        };
        return true;
    }

    public void MarkHidden()
        => visible = false;

    public void MarkVisible()
        => visible = true;

    public void Reset()
    {
        renderer = null;
        frame = null;
        visible = false;
    }
}
