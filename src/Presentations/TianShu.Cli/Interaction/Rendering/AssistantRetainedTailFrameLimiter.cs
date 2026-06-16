using TianShu.Cli.Terminal;

namespace TianShu.Cli.Interaction.Rendering;

/// <summary>
/// Keeps the live assistant retained preview bounded so long streamed text does not scroll retained frames into history.
/// 将实时 assistant retained 预览限制在有限尾部窗口内，避免长流式文本把 retained frame 刷进 scrollback。
/// </summary>
internal static class AssistantRetainedTailFrameLimiter
{
    public const int DefaultMaxTranscriptPhysicalLines = 12;

    public static TerminalChatFrame Limit(
        TerminalChatFrame frame,
        TerminalChatFrameRenderer renderer,
        int maxTranscriptPhysicalLines = DefaultMaxTranscriptPhysicalLines,
        bool styled = true)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(renderer);

        if (maxTranscriptPhysicalLines <= 0)
        {
            return frame with { TranscriptLines = Array.Empty<string>() };
        }

        var transcriptLines = renderer.RenderTranscriptLines(frame);
        if (transcriptLines.Count <= maxTranscriptPhysicalLines)
        {
            return frame;
        }

        var visibleTailLines = Math.Max(1, maxTranscriptPhysicalLines - 1);
        var marker = styled
            ? TerminalAnsi.GreenText("• ") + TerminalAnsi.DimText("...")
            : "...";
        var limitedLines = new List<string>(maxTranscriptPhysicalLines) { marker };
        limitedLines.AddRange(transcriptLines.Skip(transcriptLines.Count - visibleTailLines));

        return frame with { TranscriptLines = limitedLines };
    }
}
