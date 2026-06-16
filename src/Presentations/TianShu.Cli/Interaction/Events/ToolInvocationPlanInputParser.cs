using System.Globalization;
using System.Text.Json;

namespace TianShu.Cli.Interaction.Events;

internal static class ToolInvocationPlanInputParser
{
    public static ToolInvocationInput Build(string raw, JsonElement root)
        => new(raw, BuildPlanInputSummary(root) ?? ToolInvocationJsonHelpers.BuildCompactJsonSummary(root), null, null);

    private static string? BuildPlanInputSummary(JsonElement root)
    {
        var explanation = ToolInvocationJsonHelpers.ReadJsonString(root, "explanation");
        if ((!ToolInvocationJsonHelpers.TryGetJsonPropertyIgnoreCase(root, "plan", out var planElement)
                && !ToolInvocationJsonHelpers.TryGetJsonPropertyIgnoreCase(root, "steps", out planElement))
            || planElement.ValueKind != JsonValueKind.Array)
        {
            return explanation;
        }

        var steps = planElement.EnumerateArray()
            .Select(static (step, index) => BuildPlanStepSummary(step, index + 1))
            .Where(static text => !string.IsNullOrWhiteSpace(text))
            .Take(4)
            .ToArray();
        if (steps.Length == 0)
        {
            return explanation;
        }

        return string.IsNullOrWhiteSpace(explanation)
            ? string.Join(" | ", steps)
            : $"{explanation} | {string.Join(" | ", steps)}";
    }

    private static string? BuildPlanStepSummary(JsonElement step, int fallbackSequence)
    {
        if (step.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var text = ToolInvocationJsonHelpers.ReadJsonString(step, "step")
                   ?? ToolInvocationJsonHelpers.ReadJsonString(step, "text")
                   ?? ToolInvocationJsonHelpers.ReadJsonString(step, "title");
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var sequence = ToolInvocationJsonHelpers.ReadJsonString(step, "sequence") ?? fallbackSequence.ToString(CultureInfo.InvariantCulture);
        var status = ToolInvocationJsonHelpers.ReadJsonString(step, "status");
        var suffix = string.IsNullOrWhiteSpace(status) ? string.Empty : $" [{status}]";
        return $"{sequence}. {Truncate(text, 72)}{suffix}";
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
}
