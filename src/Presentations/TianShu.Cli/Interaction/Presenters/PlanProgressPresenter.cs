using TianShu.Contracts.Primitives;

namespace TianShu.Cli.Interaction.Presenters;

internal static class PlanProgressPresenter
{
    public static PlanProgressBlock? FromPayload(string? title, StructuredValue? payload)
    {
        var steps = ReadSteps(payload);
        if (string.IsNullOrWhiteSpace(title) && steps.Count == 0)
        {
            return null;
        }

        var completed = steps.Count(step => step.Status == PlanStepPresentationStatus.Completed);
        var current = steps.FirstOrDefault(step => step.Status == PlanStepPresentationStatus.Running)
                      ?? steps.FirstOrDefault(step => step.Status != PlanStepPresentationStatus.Completed);
        return new PlanProgressBlock(
            Normalize(title),
            completed,
            steps.Count,
            current?.Text,
            steps);
    }

    public static string RenderPlain(PlanProgressBlock block)
    {
        var header = block.TotalCount > 0
            ? $"● 计划更新  {block.CompletedCount}/{block.TotalCount} 完成"
            : "● 计划更新";
        if (!string.IsNullOrWhiteSpace(block.Title))
        {
            header += $"  {block.Title}";
        }

        var lines = new List<string> { header };
        foreach (var step in block.Steps.Take(6))
        {
            lines.Add($"  {ResolveMarker(step.Status)} {step.Sequence}. {step.Text}");
        }

        if (block.Steps.Count > 6)
        {
            lines.Add($"  ... 还有 {block.Steps.Count - 6} 项");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static IReadOnlyList<PlanProgressStep> ReadSteps(StructuredValue? payload)
    {
        if (payload is null
            || payload.Kind != StructuredValueKind.Object
            || (!TryReadObjectPropertyIgnoreCase(payload, "steps", out var stepsValue)
                && !TryReadObjectPropertyIgnoreCase(payload, "plan", out stepsValue))
            || stepsValue.Kind != StructuredValueKind.Array)
        {
            return Array.Empty<PlanProgressStep>();
        }

        var steps = new List<PlanProgressStep>();
        for (var index = 0; index < stepsValue.Items.Count; index++)
        {
            var step = stepsValue.Items[index];
            if (step.Kind != StructuredValueKind.Object)
            {
                continue;
            }

            var text = ReadStructuredString(step, "step")
                       ?? ReadStructuredString(step, "text")
                       ?? ReadStructuredString(step, "title");
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var sequence = ReadStructuredString(step, "sequence") ?? (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var rawStatus = ReadStructuredString(step, "status");
            steps.Add(new PlanProgressStep(sequence, text, ResolveStatus(rawStatus), rawStatus));
        }

        return steps;
    }

    private static PlanStepPresentationStatus ResolveStatus(string? status)
        => Normalize(status) switch
        {
            "completed" or "done" => PlanStepPresentationStatus.Completed,
            "in_progress" or "running" => PlanStepPresentationStatus.Running,
            "failed" or "blocked" => PlanStepPresentationStatus.Failed,
            _ => PlanStepPresentationStatus.Pending,
        };

    private static string ResolveMarker(PlanStepPresentationStatus status)
        => status switch
        {
            PlanStepPresentationStatus.Completed => "✓",
            PlanStepPresentationStatus.Running => "▶",
            PlanStepPresentationStatus.Failed => "✗",
            _ => "□",
        };

    private static string? ReadStructuredString(StructuredValue value, string propertyName)
    {
        if (!TryReadObjectPropertyIgnoreCase(value, propertyName, out var propertyValue))
        {
            return null;
        }

        return propertyValue.Kind switch
        {
            StructuredValueKind.String => Normalize(propertyValue.StringValue),
            StructuredValueKind.Number => Normalize(propertyValue.NumberValue),
            StructuredValueKind.Boolean => propertyValue.BooleanValue?.ToString(System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant(),
            _ => null,
        };
    }

    private static bool TryReadObjectPropertyIgnoreCase(StructuredValue value, string propertyName, out StructuredValue propertyValue)
    {
        if (value.Kind == StructuredValueKind.Object)
        {
            foreach (var pair in value.Properties)
            {
                if (string.Equals(pair.Key, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    propertyValue = pair.Value;
                    return true;
                }
            }
        }

        propertyValue = StructuredValue.Null;
        return false;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
