using TianShu.Contracts.Memory;
using TianShu.Contracts.Primitives;

namespace TianShu.IdentityMemory;

/// <summary>
/// 默认规则抽取器，只识别明确要求记住的文本并生成策略候选。
/// </summary>
public sealed class RuleBasedMemoryExtractor : IMemoryExtractor
{
    private static readonly ExtractionRule[] Rules =
    [
        new("preference.default", "rule.explicit-default", ["以后默认", "之后默认", "默认"], 0.82m),
        new("preference.avoid", "rule.explicit-avoid", ["不要再", "以后不要", "别再"], 0.82m),
        new("preference.user", "rule.explicit-preference", ["我的偏好是", "我偏好", "我更喜欢"], 0.8m),
        new("memory.note", "rule.explicit-remember", ["请记住", "记住", "记一下"], 0.8m),
    ];

    public Task<IReadOnlyList<MemoryCandidate>> ExtractAsync(
        ExtractMemory command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var text = ResolveText(command);
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult<IReadOnlyList<MemoryCandidate>>(Array.Empty<MemoryCandidate>());
        }

        foreach (var rule in Rules)
        {
            if (TryMatchRule(text, rule, out var value))
            {
                IReadOnlyList<MemoryCandidate> candidates =
                [
                    new(
                        rule.Key,
                        StructuredValue.FromString(value),
                        command.MemorySpaceId,
                        rule.Confidence,
                        command.Source,
                        $"命中显式记忆规则：{rule.RuleId}",
                        rule.RuleId)
                ];
                return Task.FromResult(candidates);
            }
        }

        return Task.FromResult<IReadOnlyList<MemoryCandidate>>(Array.Empty<MemoryCandidate>());
    }

    private static string? ResolveText(ExtractMemory command)
        => command.Content?.GetString()
           ?? command.Source.Snippet;

    private static bool TryMatchRule(string text, ExtractionRule rule, out string value)
    {
        foreach (var trigger in rule.Triggers)
        {
            var index = text.IndexOf(trigger, StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            value = TrimCandidateValue(text[(index + trigger.Length)..]);
            return !string.IsNullOrWhiteSpace(value);
        }

        value = string.Empty;
        return false;
    }

    private static string TrimCandidateValue(string value)
        => value.Trim()
            .TrimStart('：', ':', '，', ',', '。', '.', ' ', '\t')
            .Trim()
            .TrimEnd('。', '.', '！', '!', '？', '?');

    private sealed record ExtractionRule(
        string Key,
        string RuleId,
        IReadOnlyList<string> Triggers,
        decimal Confidence);
}
