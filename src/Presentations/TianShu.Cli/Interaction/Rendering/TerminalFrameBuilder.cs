using TianShu.Cli.Interaction.Projection;
using TianShu.Cli.Terminal;

namespace TianShu.Cli.Interaction.Rendering;

internal sealed class TerminalFrameBuilder
{
    private readonly TerminalTranscriptRenderer transcriptRenderer;

    public TerminalFrameBuilder()
        : this(new TerminalTranscriptRenderer())
    {
    }

    public TerminalFrameBuilder(TerminalTranscriptRenderer transcriptRenderer)
    {
        this.transcriptRenderer = transcriptRenderer ?? throw new ArgumentNullException(nameof(transcriptRenderer));
    }

    public TerminalChatFrame Build(ChatPresentationState state, TerminalFrameBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);

        var transcriptLines = new List<string>();
        foreach (var block in state.Blocks)
        {
            AddRenderedBlock(transcriptLines, block, context.Styled, context.Width);
        }

        if (state.HasActiveAssistantText)
        {
            AddAssistantLines(transcriptLines, state.ActiveAssistantText, context.Styled);
        }

        var dock = new ComposerDockState(
            context.InputText,
            context.Cursor,
            context.Prompt,
            state.Plan,
            context.Agents,
            context.Model,
            context.IsBusy,
            context.WorkingElapsed,
            context.QueuedFollowUps,
            context.InputNotice,
            CommandOverlay: context.CommandOverlay);
        return new TerminalChatFrame(transcriptLines, dock, context.PopupLines, context.Width);
    }

    private void AddRenderedBlock(ICollection<string> lines, ChatPresentationBlock block, bool styled, int width)
    {
        switch (block)
        {
            case UserMessageBlock user:
                AddUserMessageLines(lines, user.Text, user.Label, styled, width);
                break;
            case AssistantMessageBlock assistant:
                AddAssistantLines(lines, assistant.Text, styled);
                break;
            case SystemNoticeBlock notice:
                AddRenderedText(lines, styled ? TerminalAnsi.RedText(notice.Text) : notice.Text);
                break;
            default:
                AddRenderedText(lines, transcriptRenderer.Render(block, styled));
                break;
        }
    }

    private static void AddUserMessageLines(ICollection<string> lines, string text, string? label, bool styled, int width)
    {
        var renderedLines = TerminalMarkdownRenderer.RenderLines(text, styled: false);
        if (renderedLines.Count == 0)
        {
            return;
        }

        var topSeparator = BuildUserMessageSeparator(width, ResolveUserMessageLabel(label), styled);
        var bottomSeparator = new string('-', Math.Max(1, width));
        lines.Add(string.Empty);
        lines.Add(topSeparator);
        foreach (var line in renderedLines)
        {
            lines.Add(styled ? TerminalAnsi.SkyBlueText(line) : line);
        }

        lines.Add(styled ? TerminalAnsi.SkyBlueText(bottomSeparator) : bottomSeparator);
        lines.Add(string.Empty);
    }

    private static string ResolveUserMessageLabel(string? label)
        => string.IsNullOrWhiteSpace(label) ? "用户消息" : label.Trim();

    private static string BuildUserMessageSeparator(int width, string? label, bool styled)
    {
        var safeWidth = Math.Max(1, width);
        var normalizedLabel = string.IsNullOrWhiteSpace(label) ? "用户消息" : label.Trim();

        var marker = $" {normalizedLabel} ";
        var markerWidth = TerminalLayoutCalculator.GetDisplayWidth(marker);
        if (markerWidth >= safeWidth)
        {
            var truncated = TerminalLayoutCalculator.FitToWidth(marker, safeWidth).TrimEnd();
            return styled ? StyleUserMessageLabel(truncated) : truncated;
        }

        var left = Math.Max(1, (safeWidth - markerWidth) / 2);
        var right = safeWidth - markerWidth - left;
        if (!styled)
        {
            return new string('-', left) + marker + new string('-', right);
        }

        return TerminalAnsi.SkyBlueText(new string('-', left))
               + StyleUserMessageLabel(marker)
               + TerminalAnsi.SkyBlueText(new string('-', right));
    }

    private static string StyleUserMessageLabel(string text)
    {
        if (text.Contains("正在引导", StringComparison.Ordinal))
        {
            return TerminalAnsi.YellowText(text);
        }

        if (text.Contains("引导成功", StringComparison.Ordinal))
        {
            return TerminalAnsi.GreenText(text);
        }

        return TerminalAnsi.SkyBlueText(text);
    }

    private static void AddAssistantLines(ICollection<string> lines, string text, bool styled)
    {
        var renderedLines = TerminalMarkdownRenderer.RenderLines(text, styled);
        if (renderedLines.Count == 0)
        {
            return;
        }

        var marker = styled ? TerminalAnsi.GreenText("• ") : string.Empty;
        lines.Add(marker + renderedLines[0]);
        foreach (var line in renderedLines.Skip(1))
        {
            lines.Add(line);
        }
    }

    private static void AddRenderedText(ICollection<string> lines, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lines.Add(line);
        }
    }
}

internal sealed record TerminalFrameBuildContext(
    string InputText,
    int Cursor,
    string Prompt,
    AgentDockSummary? Agents,
    ModelDockSummary? Model,
    bool IsBusy,
    TimeSpan? WorkingElapsed,
    QueuedFollowUpDockState? QueuedFollowUps,
    string? InputNotice,
    CommandOverlayDockState? CommandOverlay,
    IReadOnlyList<string>? PopupLines,
    int Width,
    bool Styled);
