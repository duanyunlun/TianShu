using System.Text;
using TianShu.Cli.Terminal;

namespace TianShu.Cli.Interaction.Rendering;

internal sealed class ComposerDockRenderer
{
    public IReadOnlyList<string> BuildDockLines(
        ComposerDockState state,
        IReadOnlyList<string>? popupLines,
        bool styled,
        int? width = null)
    {
        var actualWidth = width ?? TerminalLayoutCalculator.SafeWindowWidth();
        if (state.CommandOverlay is { Lines.Count: > 0 } overlay)
        {
            return BuildCommandOverlayLines(overlay, styled, actualWidth);
        }

        var lines = new List<string>();
        lines.AddRange(BuildAboveInputLines(state, styled, actualWidth));
        lines.Add(TerminalLayoutCalculator.FitToWidth(BuildInputLine(state, styled), actualWidth));
        var separator = new string('─', Math.Max(1, actualWidth));
        lines.Add(styled ? TerminalAnsi.DimText(separator) : separator);
        if (popupLines is { Count: > 0 })
        {
            lines.AddRange(popupLines.Select(line => TerminalLayoutCalculator.FitToWidth(line, actualWidth)));
        }

        lines.AddRange(BuildFooterLines(state, styled, actualWidth));
        return lines;
    }

    public static IReadOnlyList<string> BuildPromptAboveInputLines(ComposerDockState state, bool styled, int width)
        => state.CommandOverlay is { Lines.Count: > 0 }
            ? Array.Empty<string>()
            : BuildAboveInputLines(state, styled, Math.Max(1, width));

    public static IReadOnlyList<string> BuildPromptFooterLines(ComposerDockState state, bool styled, int width)
    {
        if (state.CommandOverlay is { Lines.Count: > 0 })
        {
            return Array.Empty<string>();
        }

        var actualWidth = Math.Max(1, width);
        var separator = new string('─', actualWidth);
        var lines = new List<string>
        {
            styled ? TerminalAnsi.DimText(separator) : separator,
        };
        lines.AddRange(BuildFooterLines(state, styled, actualWidth));
        return lines;
    }

    public static int CalculateInputLineIndex(ComposerDockState state, IReadOnlyList<string>? popupLines, int width)
        => state.CommandOverlay is { Lines.Count: > 0 }
            ? 0
            : BuildAboveInputLines(state, styled: false, Math.Max(1, width)).Count;

    private static IReadOnlyList<string> BuildAboveInputLines(ComposerDockState state, bool styled, int width)
    {
        var notice = NormalizeInput(state.InputNotice ?? string.Empty);
        if (string.IsNullOrWhiteSpace(notice))
        {
            return Array.Empty<string>();
        }

        var lines = new List<string>();
        var confirmation = ExtractConfirmationPhrase(notice);
        var displayNotice = confirmation is null
            ? notice
            : notice.Replace($"请输入 {confirmation} 确认", "请按下方确认文本输入后确认", StringComparison.Ordinal);
        lines.AddRange(TerminalLayoutCalculator.WrapToWidth($"确认提示：{displayNotice}", Math.Max(1, width)));
        if (confirmation is not null)
        {
            lines.AddRange(TerminalLayoutCalculator.WrapToWidth("确认文本：", Math.Max(1, width)));
            lines.AddRange(TerminalLayoutCalculator.WrapToWidth(confirmation, Math.Max(1, width)));
        }

        return lines.Select(line => StyleNoticeLine(line, styled)).ToArray();
    }

    private static string? ExtractConfirmationPhrase(string notice)
    {
        const string prefix = "请输入 ";
        const string suffix = " 确认";
        var start = notice.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += prefix.Length;
        var end = notice.IndexOf(suffix, start, StringComparison.Ordinal);
        if (end <= start)
        {
            return null;
        }

        return notice[start..end].Trim();
    }

    private static string NormalizePreview(string value)
    {
        var preview = NormalizeInput(value);
        return preview.Length <= 72 ? preview : preview[..72] + "...";
    }

    private static string StylePendingLine(string text, bool styled)
        => styled ? TerminalAnsi.YellowText(text) : text;

    private static string StyleNoticeLine(string text, bool styled)
        => styled ? TerminalAnsi.MagentaText(text) : text;

    private static IReadOnlyList<string> BuildCommandOverlayLines(CommandOverlayDockState overlay, bool styled, int width)
    {
        var actualWidth = Math.Max(1, width);
        var lines = new List<string>
        {
            styled
                ? TerminalAnsi.DimText(new string('─', actualWidth))
                : new string('─', actualWidth),
        };

        foreach (var line in overlay.Lines)
        {
            lines.Add(TerminalLayoutCalculator.FitToWidth(line, actualWidth));
        }

        return lines;
    }

    public static IReadOnlyList<string> BuildFooterLines(ComposerDockState state, bool styled, int width)
    {
        var lines = new List<string>();
        if (state.QueuedFollowUps is { Count: > 0 } queued)
        {
            lines.AddRange(BuildQueuedFollowUpLines(queued, styled, width));
            lines.Add(styled
                ? TerminalAnsi.DimText(new string('─', Math.Max(1, width)))
                : new string('─', Math.Max(1, width)));
        }

        lines.Add(TerminalLayoutCalculator.FitToWidth(BuildStatusLine(state, styled), width));

        if (state.Plan is { Steps.Count: > 0 } plan)
        {
            foreach (var step in plan.Steps)
            {
                lines.Add(TerminalLayoutCalculator.FitToWidth(BuildPlanStepLine(step, styled), width));
            }
        }

        return lines;
    }

    private static IReadOnlyList<string> BuildQueuedFollowUpLines(QueuedFollowUpDockState queued, bool styled, int width)
    {
        var lines = new List<string>
        {
            TerminalLayoutCalculator.FitToWidth(
                StylePendingLine($"待发送队列：{queued.Count} 条，当前回合结束后自动发送；/follow-up promote <编号> 可转为引导，/follow-up drop <编号> 可删除", styled),
                width),
        };
        var previewLines = queued.Entries.Take(3).ToArray();
        foreach (var entry in previewLines)
        {
            var marker = entry.IsSelected ? ">" : " ";
            lines.Add(TerminalLayoutCalculator.FitToWidth(
                StylePendingLine($"  {marker} [{entry.Index}] {NormalizePreview(entry.Preview)}", styled),
                width));
        }

        if (queued.Count > previewLines.Length)
        {
            lines.Add(TerminalLayoutCalculator.FitToWidth(
                StylePendingLine($"  • 另有 {queued.Count - previewLines.Length} 条待发送", styled),
                width));
        }

        return lines;
    }

    public static string BuildStatusLine(ComposerDockState state, bool styled)
    {
        var parts = new List<string>();
        if (state.IsBusy)
        {
            var working = "• Working...";
            if (state.WorkingElapsed is { } elapsed)
            {
                working += $" {FormatWorkingElapsed(elapsed)}";
            }

            parts.Add(styled ? TerminalAnsi.YellowText(working) : working);
        }

        parts.Add(StyleStatusPart(state.IsBusy ? string.Empty : "Idle", styled));

        if (state.Plan is { } plan
            && plan.TotalCount > 0
            && plan.Steps is not { Count: > 0 })
        {
            parts.Add(StyleStatusPart($"Plan {plan.CompletedCount}/{plan.TotalCount}", styled));
            if (!string.IsNullOrWhiteSpace(plan.CurrentStep))
            {
                parts.Add(StyleStatusPart($"▶ {plan.CurrentStep}", styled));
            }
        }

        if (state.Agents is { } agents)
        {
            parts.Add(StyleStatusPart($"Agents: {agents.RunningCount} running", styled));
        }
        else
        {
            parts.Add(StyleStatusPart("Agents: 0 running", styled));
        }

        var modelSummary = FormatModelSummary(state.Model);
        if (!string.IsNullOrWhiteSpace(modelSummary))
        {
            parts.Add(StyleStatusPart($"Model: {modelSummary}", styled));
        }

        return string.Join("       ", parts.Where(static part => !string.IsNullOrWhiteSpace(RemoveAnsi(part))));
    }

    private static string? FormatModelSummary(ModelDockSummary? model)
    {
        if (model is null || string.IsNullOrWhiteSpace(model.Model))
        {
            return null;
        }

        var parts = new List<string>();
        var provider = NormalizeStatusValue(model.Provider);
        var modelName = NormalizeStatusValue(model.Model)!;
        parts.Add(provider is null ? modelName : $"{provider} / {modelName}");
        AddKeyValuePart(parts, "route", model.Route);
        AddKeyValuePart(parts, "protocol", model.Protocol);
        return string.Join(" · ", parts);
    }

    private static void AddKeyValuePart(List<string> parts, string key, string? value)
    {
        var normalized = NormalizeStatusValue(value);
        if (normalized is not null)
        {
            parts.Add($"{key}: {normalized}");
        }
    }

    private static string? NormalizeStatusValue(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string StyleStatusPart(string text, bool styled)
        => styled ? TerminalAnsi.DimText(text) : text;

    private static string FormatWorkingElapsed(TimeSpan elapsed)
    {
        var normalized = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        var days = (int)normalized.TotalDays;
        var hours = normalized.Hours;
        var minutes = normalized.Minutes;
        var seconds = normalized.Seconds;
        var parts = new List<string>(4);
        if (days > 0)
        {
            parts.Add($"{days}d");
        }

        if (hours > 0)
        {
            parts.Add($"{hours}h");
        }

        if (minutes > 0)
        {
            parts.Add($"{minutes}m");
        }

        if (seconds > 0 || parts.Count == 0)
        {
            parts.Add($"{seconds}s");
        }

        return string.Join(" ", parts);
    }

    private static string RemoveAnsi(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\u001b')
            {
                while (index < text.Length && text[index] != 'm')
                {
                    index++;
                }

                continue;
            }

            builder.Append(text[index]);
        }

        return builder.ToString();
    }

    private static string BuildPlanStepLine(PlanDockStep step, bool styled)
    {
        var marker = step.Status switch
        {
            PlanDockStepStatus.Completed => "✓",
            PlanDockStepStatus.Running => "▶",
            PlanDockStepStatus.Failed => "✗",
            _ => "□",
        };
        var text = $"  {marker} {step.Sequence}. {step.Text}";
        return step.Status switch
        {
            PlanDockStepStatus.Completed => styled ? TerminalAnsi.GreenText(text) : text,
            PlanDockStepStatus.Running => styled ? TerminalAnsi.YellowText(text) : text,
            PlanDockStepStatus.Failed => styled ? TerminalAnsi.RedText(text) : text,
            _ => styled ? TerminalAnsi.DimText(text) : text,
        };
    }

    public static string BuildInputLine(ComposerDockState state, bool styled)
    {
        var text = NormalizeInput(state.InputText);
        var line = string.IsNullOrWhiteSpace(text)
            ? state.Prompt
            : $"{state.Prompt}{text}";
        return styled ? line : line;
    }

    public static int CalculateCursorColumn(ComposerDockState state)
    {
        var text = NormalizeInput(state.InputText);
        var cursor = Math.Clamp(state.Cursor, 0, text.Length);
        return TerminalLayoutCalculator.GetDisplayWidth(state.Prompt)
               + TerminalLayoutCalculator.GetDisplayWidth(text[..cursor]);
    }

    private static string NormalizeInput(string value)
        => value.Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ');
}
