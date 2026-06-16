using System.Globalization;
using System.Text;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;

namespace TianShu.Cli.Interaction.Presenters;

/// <summary>
/// Builds compact text summaries for plan update events.
/// 为计划更新事件构造紧凑文本摘要。
/// </summary>
internal static class PlanUpdateSummaryBuilder
{
    public static string? BuildDisplayText(ControlPlaneConversationStreamEvent streamEvent)
    {
        ArgumentNullException.ThrowIfNull(streamEvent);

        var explanation = Normalize(ReadPlanPayloadString(streamEvent, "explanation"))
                          ?? Normalize(streamEvent.Text);
        var allSteps = ReadPlanPayloadSteps(streamEvent);
        var steps = allSteps
            .Take(6)
            .ToArray();
        if (string.IsNullOrWhiteSpace(explanation) && allSteps.Count == 0)
        {
            return null;
        }

        var completed = allSteps.Count(step => IsPlanStatus(step.Status, "completed") || IsPlanStatus(step.Status, "done"));
        var total = allSteps.Count;
        var current = allSteps.FirstOrDefault(step => IsPlanStatus(step.Status, "in_progress") || IsPlanStatus(step.Status, "running"))
                      ?? allSteps.FirstOrDefault(step => !IsPlanStatus(step.Status, "completed") && !IsPlanStatus(step.Status, "done"));

        var builder = new StringBuilder("- 更新计划");
        if (!string.IsNullOrWhiteSpace(explanation))
        {
            builder.Append('：').Append(Truncate(explanation!, 80));
        }

        if (total > 0)
        {
            builder.Append(string.IsNullOrWhiteSpace(explanation) ? "：" : " ");
            builder.Append('(').Append(completed).Append('/').Append(total).Append(" 完成");
            if (current is not null)
            {
                builder.Append("，当前：").Append(Truncate(current.Text, 60));
            }

            builder.Append(')');
        }

        foreach (var step in steps)
        {
            builder.AppendLine();
            builder.Append("  ")
                   .Append(ResolveStepMarker(step.Status))
                   .Append(' ')
                   .Append(step.Sequence)
                   .Append(". ")
                   .Append(Truncate(step.Text, 88));
            if (!string.IsNullOrWhiteSpace(step.Status))
            {
                builder.Append(" [").Append(NormalizeStatus(step.Status)).Append(']');
            }
        }

        if (allSteps.Count > steps.Length)
        {
            builder.AppendLine();
            builder.Append("  ... 还有 ")
                   .Append(allSteps.Count - steps.Length)
                   .Append(" 项");
        }

        return builder.ToString();
    }

    private static IReadOnlyList<PlanDisplayStep> ReadPlanPayloadSteps(ControlPlaneConversationStreamEvent streamEvent)
    {
        if (streamEvent.PayloadKind != ControlPlaneConversationStreamPayloadKind.Plan
            || streamEvent.Payload is null
            || streamEvent.Payload.Kind != StructuredValueKind.Object)
        {
            return Array.Empty<PlanDisplayStep>();
        }

        if (!TryReadObjectPropertyIgnoreCase(streamEvent.Payload, "steps", out var stepsValue)
            && !TryReadObjectPropertyIgnoreCase(streamEvent.Payload, "plan", out stepsValue))
        {
            return Array.Empty<PlanDisplayStep>();
        }

        if (stepsValue.Kind != StructuredValueKind.Array)
        {
            return Array.Empty<PlanDisplayStep>();
        }

        var steps = new List<PlanDisplayStep>();
        for (var index = 0; index < stepsValue.Items.Count; index++)
        {
            var step = stepsValue.Items[index];
            if (step.Kind != StructuredValueKind.Object)
            {
                continue;
            }

            var stepText = ReadStructuredString(step, "step")
                           ?? ReadStructuredString(step, "text")
                           ?? ReadStructuredString(step, "title");
            if (string.IsNullOrWhiteSpace(stepText))
            {
                continue;
            }

            var sequence = ReadStructuredString(step, "sequence") ?? (index + 1).ToString(CultureInfo.InvariantCulture);
            var status = ReadStructuredString(step, "status");
            steps.Add(new PlanDisplayStep(sequence, stepText!, status));
        }

        return steps;
    }

    private static string? ReadPlanPayloadString(ControlPlaneConversationStreamEvent streamEvent, string propertyName)
    {
        if (streamEvent.PayloadKind != ControlPlaneConversationStreamPayloadKind.Plan
            || streamEvent.Payload is null
            || streamEvent.Payload.Kind != StructuredValueKind.Object
            || !TryReadObjectPropertyIgnoreCase(streamEvent.Payload, propertyName, out var propertyValue))
        {
            return null;
        }

        return ReadStructuredValueAsString(propertyValue);
    }

    private static string? ReadStructuredString(StructuredValue value, string propertyName)
    {
        if (!TryReadObjectPropertyIgnoreCase(value, propertyName, out var propertyValue))
        {
            return null;
        }

        return ReadStructuredValueAsString(propertyValue);
    }

    private static string? ReadStructuredValueAsString(StructuredValue value)
        => value.Kind switch
        {
            StructuredValueKind.String => Normalize(value.StringValue),
            StructuredValueKind.Number => Normalize(value.NumberValue),
            StructuredValueKind.Boolean => value.BooleanValue?.ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
            StructuredValueKind.Null => null,
            _ => Normalize(Convert.ToString(value.ToPlainObject(), CultureInfo.InvariantCulture)),
        };

    private static bool TryReadObjectPropertyIgnoreCase(StructuredValue value, string propertyName, out StructuredValue propertyValue)
    {
        if (value.Kind != StructuredValueKind.Object)
        {
            propertyValue = StructuredValue.Null;
            return false;
        }

        if (value.Properties.TryGetValue(propertyName, out var directMatch)
            && directMatch is not null)
        {
            propertyValue = directMatch;
            return true;
        }

        foreach (var (candidateName, candidateValue) in value.Properties)
        {
            if (string.Equals(candidateName, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                propertyValue = candidateValue;
                return true;
            }
        }

        propertyValue = StructuredValue.Null;
        return false;
    }

    private static string ResolveStepMarker(string? status)
    {
        if (IsPlanStatus(status, "completed") || IsPlanStatus(status, "done"))
        {
            return "[x]";
        }

        if (IsPlanStatus(status, "in_progress") || IsPlanStatus(status, "running"))
        {
            return "[>]";
        }

        if (IsPlanStatus(status, "failed") || IsPlanStatus(status, "blocked"))
        {
            return "[!]";
        }

        return "[ ]";
    }

    private static bool IsPlanStatus(string? status, string expected)
        => string.Equals(status, expected, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeStatus(string? status)
        => Normalize(status) switch
        {
            "completed" => "完成",
            "done" => "完成",
            "in_progress" => "进行中",
            "running" => "进行中",
            "pending" => "待处理",
            "todo" => "待处理",
            "failed" => "失败",
            "blocked" => "阻塞",
            { Length: > 0 } value => value,
            _ => string.Empty,
        };

    private static string? Normalize(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is null
            ? null
            : string.Join(" ", normalized.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";

    private sealed record PlanDisplayStep(string Sequence, string Text, string? Status);
}
