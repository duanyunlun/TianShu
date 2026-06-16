using TianShu.Cli.Terminal;

namespace TianShu.Cli.Interaction.Rendering;

/// <summary>
/// Renders transcript lines and the inline composer dock as one retained frame.
/// 将 transcript 行与内容尾部 composer dock 作为一个 retained frame 统一渲染。
/// </summary>
internal sealed class TerminalChatFrameRenderer
{
    public const int DockLeadingBlankLineCount = 2;

    private readonly ComposerDockRenderer dockRenderer;

    public TerminalChatFrameRenderer()
        : this(new ComposerDockRenderer())
    {
    }

    public TerminalChatFrameRenderer(ComposerDockRenderer dockRenderer)
    {
        this.dockRenderer = dockRenderer ?? throw new ArgumentNullException(nameof(dockRenderer));
    }

    public IReadOnlyList<string> RenderLines(TerminalChatFrame frame, bool styled)
    {
        var width = Math.Max(1, frame.Width);
        var lines = RenderTranscriptLines(frame).ToList();
        if (lines.Count > 0)
        {
            for (var index = 0; index < DockLeadingBlankLineCount; index++)
            {
                lines.Add(string.Empty);
            }
        }

        lines.AddRange(dockRenderer.BuildDockLines(frame.Dock, frame.PopupLines, styled, width));
        return styled
            ? lines.Select(ProtectAnsiBoundary).ToList()
            : lines;
    }

    public int CalculateDockInputLineIndex(TerminalChatFrame frame)
    {
        var transcriptLineCount = RenderTranscriptLines(frame).Count;
        var spacerLineCount = transcriptLineCount > 0 ? DockLeadingBlankLineCount : 0;
        return transcriptLineCount
            + spacerLineCount
            + ComposerDockRenderer.CalculateInputLineIndex(frame.Dock, frame.PopupLines, Math.Max(1, frame.Width));
    }

    public IReadOnlyList<string> RenderTranscriptLines(TerminalChatFrame frame)
    {
        var width = Math.Max(1, frame.Width);
        var lines = new List<string>();
        foreach (var transcriptLine in frame.TranscriptLines)
        {
            foreach (var physicalLine in SplitPhysicalLines(transcriptLine))
            {
                lines.AddRange(TerminalLayoutCalculator.WrapToWidth(physicalLine, width));
            }
        }

        return lines;
    }

    public string RenderText(TerminalChatFrame frame, bool styled)
        => string.Join(Environment.NewLine, RenderLines(frame, styled));

    private static string ProtectAnsiBoundary(string value)
        => TerminalAnsi.Reset + value + TerminalAnsi.Reset;

    private static IEnumerable<string> SplitPhysicalLines(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            yield return string.Empty;
            yield break;
        }

        using var reader = new StringReader(value);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            yield return line;
        }
    }
}
