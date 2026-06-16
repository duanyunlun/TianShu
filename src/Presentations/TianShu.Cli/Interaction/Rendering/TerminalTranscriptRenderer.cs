using TianShu.Cli.Terminal;

namespace TianShu.Cli.Interaction.Rendering;

internal sealed class TerminalTranscriptRenderer
{
    public string Render(ChatPresentationBlock block, bool styled)
        => block switch
        {
            ToolInvocationBlock tool => RenderTool(tool, styled),
            PlanProgressBlock plan => RenderPlan(plan, styled),
            _ => string.Empty,
        };

    private static string RenderTool(ToolInvocationBlock block, bool styled)
    {
        var marker = block.Status switch
        {
            ToolPresentationStatus.Failed => "✗",
            ToolPresentationStatus.Running => "▶",
            ToolPresentationStatus.Canceled => "⊘",
            _ => "✓",
        };
        if (!styled)
        {
            var plainLines = new List<string> { $"● {block.TitleText}" };
            if (!string.IsNullOrWhiteSpace(block.SubjectText)
                && (ShouldAlwaysShowSubject(block) || !SummaryAlreadyContainsSubject(block.SubjectText, block.Summary)))
            {
                plainLines.Add($"  {block.SubjectText}");
            }

            if (!string.IsNullOrWhiteSpace(block.Summary))
            {
                plainLines.Add($"  {marker} {block.Summary}");
            }

            return string.Join(Environment.NewLine, plainLines);
        }

        var lines = new List<string> { StyleToolTitle($"● {block.TitleText}", block.Kind) };
        if (!string.IsNullOrWhiteSpace(block.SubjectText)
            && (ShouldAlwaysShowSubject(block) || !SummaryAlreadyContainsSubject(block.SubjectText, block.Summary)))
        {
            lines.Add(TerminalAnsi.DimText($"  {block.SubjectText}"));
        }

        if (!string.IsNullOrWhiteSpace(block.Summary))
        {
            lines.Add(StyleToolResult($"  {marker} {block.Summary}", block.Status));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string StyleToolTitle(string text, ToolPresentationKind kind)
        => kind switch
        {
            ToolPresentationKind.Command => TerminalAnsi.YellowText(text),
            ToolPresentationKind.FileChange or ToolPresentationKind.CodePatch => TerminalAnsi.BlueText(text),
            ToolPresentationKind.PlanUpdate => TerminalAnsi.MagentaText(text),
            ToolPresentationKind.WebSearch or ToolPresentationKind.Search => TerminalAnsi.BlueText(text),
            ToolPresentationKind.ImageGeneration or ToolPresentationKind.ImageView => TerminalAnsi.BlueText(text),
            _ => TerminalAnsi.MagentaText(text),
        };

    private static string StyleToolResult(string text, ToolPresentationStatus status)
        => status switch
        {
            ToolPresentationStatus.Failed => TerminalAnsi.RedText(text),
            ToolPresentationStatus.Canceled => TerminalAnsi.YellowText(text),
            _ => TerminalAnsi.GreenText(text),
        };

    private static bool SummaryAlreadyContainsSubject(string subject, string? summary)
        => !string.IsNullOrWhiteSpace(summary)
           && summary.Contains(subject, StringComparison.OrdinalIgnoreCase);

    private static bool ShouldAlwaysShowSubject(ToolInvocationBlock block)
        => block.Subject?.AlwaysShow == true || block.Kind == ToolPresentationKind.Command;

    private static string RenderPlan(PlanProgressBlock block, bool styled)
    {
        var header = block.TotalCount > 0
            ? $"● 计划更新  {block.CompletedCount}/{block.TotalCount} 完成"
            : "● 计划更新";

        var lines = new List<string>
        {
            styled && !string.IsNullOrWhiteSpace(block.Title)
                ? $"{StylePlanChrome(header)}  {block.Title}"
                : !string.IsNullOrWhiteSpace(block.Title)
                    ? $"{header}  {block.Title}"
                    : StylePlanLine(header, styled),
        };
        foreach (var step in block.Steps.Take(6))
        {
            lines.Add(StylePlanLine($"  {ResolveMarker(step.Status)} {step.Sequence}. {step.Text}", styled));
        }

        if (block.Steps.Count > 6)
        {
            lines.Add(StylePlanLine($"  ... 还有 {block.Steps.Count - 6} 项", styled));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string StylePlanLine(string text, bool styled)
        => styled ? StylePlanChrome(text) : text;

    private static string StylePlanChrome(string text)
        => TerminalAnsi.DimText(TerminalAnsi.GreenText(text));

    private static string ResolveMarker(PlanStepPresentationStatus status)
        => status switch
        {
            PlanStepPresentationStatus.Completed => "✓",
            PlanStepPresentationStatus.Running => "▶",
            PlanStepPresentationStatus.Failed => "✗",
            _ => "□",
        };
}
