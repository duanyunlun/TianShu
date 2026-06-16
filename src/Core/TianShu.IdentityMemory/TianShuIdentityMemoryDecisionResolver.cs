using TianShu.Contracts.Memory;
using TianShu.Contracts.Primitives;

namespace TianShu.IdentityMemory;

/// <summary>
/// Identity / Memory 默认决策解析器。
/// Default decision resolver for the identity and memory plane.
/// </summary>
public static class TianShuIdentityMemoryDecisionResolver
{
    public static HabitProfile BuildHabitProfile(TianShuIdentityMemoryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new HabitProfile(
            context.AccountId,
            context.PreferredTools,
            context.PreferredVerbosity,
            BuildHabitLabels(context));
    }

    public static string? ResolveExecutionVerbosity(TianShuIdentityMemoryContext context)
        => BuildHabitProfile(context).PreferredVerbosity;

    private static LabelSet BuildHabitLabels(TianShuIdentityMemoryContext context)
    {
        var labels = new List<string>
        {
            "local-default",
            context.Platform,
            $"team:{context.TeamKey}",
        };

        if (!string.IsNullOrWhiteSpace(context.WorkingDirectory))
        {
            labels.Add($"workspace:{NormalizeSegment(context.WorkingDirectory!)}");
        }

        if (!string.IsNullOrWhiteSpace(context.CollaborationSpaceId))
        {
            labels.Add($"collaboration:{NormalizeSegment(context.CollaborationSpaceId!)}");
        }

        return LabelSet.Create(labels);
    }

    private static string NormalizeSegment(string value)
    {
        var normalized = value
            .Trim()
            .Replace('\\', '/')
            .Replace(' ', '-')
            .ToLowerInvariant();

        if (normalized.Length >= 3
            && char.IsLetter(normalized[0])
            && normalized[1] == ':'
            && normalized[2] == '/')
        {
            normalized = normalized[0] + normalized[2..];
        }

        return normalized.Replace(':', '_');
    }
}
