using TianShu.Contracts.Memory;
using TianShu.Contracts.Primitives;

namespace TianShu.IdentityMemory;

/// <summary>
/// 自适应语义抽取器，用本地可解释规则识别无固定关键词的偏好、项目规则、禁止项与纠错。
/// </summary>
public sealed class AdaptiveMemoryExtractor : IMemoryExtractor
{
    private static readonly string[] ProjectScopeMarkers =
    [
        "这个仓库", "当前仓库", "本仓库", "这个项目", "当前项目", "本项目", "TianShu", "天枢", "这里"
    ];

    private static readonly string[] ProjectDirectiveMarkers =
    [
        "不要", "禁止", "必须", "应该", "一律", "固定", "默认", "尽量", "优先"
    ];

    private static readonly string[] PreferenceMarkers =
    [
        "我一般喜欢", "我通常喜欢", "我更喜欢", "我喜欢", "我更希望", "我希望", "我倾向于", "我习惯", "一般我希望", "通常我希望"
    ];

    private static readonly string[] AvoidMarkers =
    [
        "我不喜欢", "我不希望", "我不想", "尽量不要", "少用", "避免", "别老是", "不需要"
    ];

    private static readonly string[] CorrectionMarkers =
    [
        "不是", "不对", "改成", "应该是", "准确地说", "更正一下"
    ];

    public Task<IReadOnlyList<MemoryCandidate>> ExtractAsync(
        ExtractMemory command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (command.Source.SourceKind != MemorySourceKind.Conversation
            || !string.Equals(command.Source.Role, "user", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<IReadOnlyList<MemoryCandidate>>(Array.Empty<MemoryCandidate>());
        }

        var text = ResolveText(command);
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult<IReadOnlyList<MemoryCandidate>>(Array.Empty<MemoryCandidate>());
        }

        var candidates = new List<MemoryCandidate>();
        if (TryExtractCorrection(text, command, out var correction))
        {
            candidates.Add(correction);
        }

        if (TryExtractProjectRule(text, command, out var projectRule))
        {
            candidates.Add(projectRule);
        }

        if (TryExtractAvoidance(text, command, out var avoid))
        {
            candidates.Add(avoid);
        }

        if (TryExtractPreference(text, command, out var preference))
        {
            candidates.Add(preference);
        }

        return Task.FromResult<IReadOnlyList<MemoryCandidate>>(Deduplicate(candidates));
    }

    private static string? ResolveText(ExtractMemory command)
        => command.Content?.GetString()
           ?? command.Source.Snippet;

    private static bool TryExtractCorrection(
        string text,
        ExtractMemory command,
        out MemoryCandidate candidate)
    {
        candidate = null!;
        if (!ContainsAny(text, CorrectionMarkers))
        {
            return false;
        }

        var value = ExtractAfterAny(text, ["改成", "应该是", "而是", "更正一下", "准确地说"])
                    ?? ExtractAfterAny(text, ["以后", "之后", "默认"])
                    ?? text;
        value = TrimCandidateValue(value);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var key = ContainsAny(text, ["默认", "以后", "之后"])
            ? "preference.default"
            : ContainsAny(text, ["不要", "别", "避免", "禁止"])
                ? "preference.avoid"
                : "preference.user";
        candidate = CreateCandidate(command, key, value, 0.84m, "rule.semantic-correction");
        return true;
    }

    private static bool TryExtractProjectRule(
        string text,
        ExtractMemory command,
        out MemoryCandidate candidate)
    {
        candidate = null!;
        if (!ContainsAny(text, ProjectScopeMarkers) || !ContainsAny(text, ProjectDirectiveMarkers))
        {
            return false;
        }

        var value = TrimCandidateValue(RemoveLeadingScope(text));
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        candidate = CreateCandidate(command, ResolveProjectRuleKey(text), value, 0.84m, "rule.semantic-project-rule");
        return true;
    }

    private static bool TryExtractAvoidance(
        string text,
        ExtractMemory command,
        out MemoryCandidate candidate)
    {
        candidate = null!;
        var value = ExtractAfterAny(text, AvoidMarkers);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        candidate = CreateCandidate(command, "preference.avoid", TrimCandidateValue(value), 0.82m, "rule.semantic-avoid");
        return true;
    }

    private static bool TryExtractPreference(
        string text,
        ExtractMemory command,
        out MemoryCandidate candidate)
    {
        candidate = null!;
        var value = ExtractAfterAny(text, PreferenceMarkers);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        candidate = CreateCandidate(command, "preference.user", TrimCandidateValue(value), 0.82m, "rule.semantic-preference");
        return true;
    }

    private static MemoryCandidate CreateCandidate(
        ExtractMemory command,
        string key,
        string value,
        decimal confidence,
        string ruleId)
        => new(
            key,
            StructuredValue.FromString(value),
            command.MemorySpaceId,
            confidence,
            command.Source,
            $"语义抽取候选：{ruleId}",
            ruleId);

    private static IReadOnlyList<MemoryCandidate> Deduplicate(IReadOnlyList<MemoryCandidate> candidates)
    {
        var results = new Dictionary<string, MemoryCandidate>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            var key = $"{candidate.Key}\n{candidate.Value.GetString()}";
            if (!results.ContainsKey(key))
            {
                results[key] = candidate;
            }
        }

        return results.Values.ToArray();
    }

    private static bool ContainsAny(string text, IReadOnlyList<string> markers)
        => markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static string? ExtractAfterAny(string text, IReadOnlyList<string> markers)
    {
        foreach (var marker in markers)
        {
            var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                return text[(index + marker.Length)..];
            }
        }

        return null;
    }

    private static string RemoveLeadingScope(string text)
    {
        var value = text.Trim();
        foreach (var marker in ProjectScopeMarkers)
        {
            if (value.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
            {
                return value[marker.Length..];
            }
        }

        return value;
    }

    private static string ResolveProjectRuleKey(string text)
    {
        if (text.Contains("VSIX", StringComparison.OrdinalIgnoreCase))
        {
            return "workspace.rule.vsix";
        }

        if (text.Contains("文档", StringComparison.OrdinalIgnoreCase))
        {
            return "workspace.rule.docs";
        }

        if (text.Contains("构建", StringComparison.OrdinalIgnoreCase)
            || text.Contains("build", StringComparison.OrdinalIgnoreCase))
        {
            return "workspace.rule.build";
        }

        return "workspace.rule";
    }

    private static string TrimCandidateValue(string value)
        => value.Trim()
            .TrimStart('：', ':', '，', ',', '。', '.', ' ', '\t')
            .Trim()
            .TrimEnd('。', '.', '！', '!', '？', '?');
}
