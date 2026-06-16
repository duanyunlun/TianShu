using TianShu.Cli.Interaction.Rendering;

namespace TianShu.Cli.Interaction.Host;

/// <summary>
/// Owns the active assistant retained tail frame and cursor restoration.
/// 持有正在流式输出的 assistant retained tail 帧与光标恢复状态。
/// </summary>
internal sealed class AssistantRetainedTailController
{
    private readonly Func<bool> isHumanOutput;
    private readonly Func<bool> hasUncommittedText;
    private readonly Func<string> readUncommittedText;
    private readonly Func<InteractionPipeline> getPipeline;
    private readonly Func<PlanDockSummary?> getPlan;
    private readonly Func<ComposerDockState> buildCurrentDockState;
    private readonly Func<ComposerDockState, bool, bool, TerminalFrameBuildContext> buildFrameContextFromDock;
    private readonly Action<ComposerDockState> updateCurrentDockState;
    private readonly Func<IDisposable> hideCursorForRefresh;
    private readonly TerminalPromptFrameController promptFrame;
    private readonly TerminalChatFrameRenderer frameRenderer = new();
    private int lineCount;
    private int cursorLineIndex;

    public AssistantRetainedTailController(
        Func<bool> isHumanOutput,
        Func<bool> hasUncommittedText,
        Func<string> readUncommittedText,
        Func<InteractionPipeline> getPipeline,
        Func<PlanDockSummary?> getPlan,
        Func<ComposerDockState> buildCurrentDockState,
        Func<ComposerDockState, bool, bool, TerminalFrameBuildContext> buildFrameContextFromDock,
        Action<ComposerDockState> updateCurrentDockState,
        Func<IDisposable> hideCursorForRefresh,
        TerminalPromptFrameController promptFrame)
    {
        this.isHumanOutput = isHumanOutput ?? throw new ArgumentNullException(nameof(isHumanOutput));
        this.hasUncommittedText = hasUncommittedText ?? throw new ArgumentNullException(nameof(hasUncommittedText));
        this.readUncommittedText = readUncommittedText ?? throw new ArgumentNullException(nameof(readUncommittedText));
        this.getPipeline = getPipeline ?? throw new ArgumentNullException(nameof(getPipeline));
        this.getPlan = getPlan ?? throw new ArgumentNullException(nameof(getPlan));
        this.buildCurrentDockState = buildCurrentDockState ?? throw new ArgumentNullException(nameof(buildCurrentDockState));
        this.buildFrameContextFromDock = buildFrameContextFromDock ?? throw new ArgumentNullException(nameof(buildFrameContextFromDock));
        this.updateCurrentDockState = updateCurrentDockState ?? throw new ArgumentNullException(nameof(updateCurrentDockState));
        this.hideCursorForRefresh = hideCursorForRefresh ?? throw new ArgumentNullException(nameof(hideCursorForRefresh));
        this.promptFrame = promptFrame ?? throw new ArgumentNullException(nameof(promptFrame));
    }

    public bool HasFrame => lineCount > 0;

    public bool HasUncommittedText()
        => hasUncommittedText();

    public void RenderUnsafe()
    {
        if (!isHumanOutput() || !hasUncommittedText())
        {
            return;
        }

        using var cursorScope = hideCursorForRefresh();
        if (lineCount > 0)
        {
            ClearUnsafe();
        }
        else if (promptFrame.IsVisible)
        {
            promptFrame.ClearVisibleFrame();
        }

        var frame = BuildPreviewFrame(includePopupLines: true);
        var dockState = frame.Dock;
        var lines = frameRenderer.RenderLines(frame, styled: true);
        foreach (var line in lines)
        {
            Console.WriteLine(line);
        }

        lineCount = lines.Count;
        cursorLineIndex = Math.Clamp(
            frameRenderer.CalculateDockInputLineIndex(frame),
            0,
            Math.Max(0, lines.Count - 1));
        MoveCursorToDockInputUnsafe(dockState, lines.Count);
        updateCurrentDockState(dockState);
        promptFrame.MarkVisible();
    }

    public void CommitUnsafe()
    {
        if (!hasUncommittedText())
        {
            ClearUnsafe();
            return;
        }

        var frame = BuildFrame(includePopupLines: false);
        ClearUnsafe();
        foreach (var line in frameRenderer.RenderTranscriptLines(frame))
        {
            Console.WriteLine(line);
        }

        promptFrame.MarkHidden();
        cursorLineIndex = 0;
    }

    public void ClearUnsafe()
    {
        if (lineCount <= 0)
        {
            return;
        }

        using var cursorScope = hideCursorForRefresh();
        if (cursorLineIndex > 0)
        {
            Console.Write($"\u001b[{cursorLineIndex}A\r");
        }
        else
        {
            Console.Write('\r');
        }

        for (var index = 0; index < lineCount; index++)
        {
            Console.Write("\u001b[2K");
            if (index < lineCount - 1)
            {
                Console.Write('\n');
            }
        }

        if (lineCount > 1)
        {
            Console.Write($"\u001b[{lineCount - 1}A\r");
        }
        else
        {
            Console.Write('\r');
        }

        lineCount = 0;
        cursorLineIndex = 0;
        promptFrame.MarkHidden();
    }

    public void Reset()
    {
        lineCount = 0;
        cursorLineIndex = 0;
    }

    private TerminalChatFrame BuildFrame(bool includePopupLines)
    {
        var text = readUncommittedText();
        return getPipeline().BuildActiveAssistantFrame(
            string.IsNullOrEmpty(text) ? " " : text,
            getPlan(),
            buildFrameContextFromDock(buildCurrentDockState(), includePopupLines, true));
    }

    private TerminalChatFrame BuildPreviewFrame(bool includePopupLines)
        => AssistantRetainedTailFrameLimiter.Limit(
            BuildFrame(includePopupLines),
            frameRenderer,
            styled: true);

    private void MoveCursorToDockInputUnsafe(ComposerDockState dockState, int renderedLineCount)
    {
        if (renderedLineCount <= 0)
        {
            return;
        }

        var linesToMoveUp = renderedLineCount - cursorLineIndex;
        if (linesToMoveUp > 0)
        {
            Console.Write($"\u001b[{linesToMoveUp}A\r");
        }
        else
        {
            Console.Write('\r');
        }

        var column = Math.Min(
            Math.Max(0, TerminalLayoutCalculator.SafeWritableWidth() - 1),
            Math.Max(0, ComposerDockRenderer.CalculateCursorColumn(dockState)));
        if (column > 0)
        {
            Console.Write($"\u001b[{column}C");
        }
    }
}
