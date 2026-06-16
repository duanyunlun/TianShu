using System.Text.RegularExpressions;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Primitives;

namespace TianShu.IdentityMemory;

/// <summary>
/// Phase A 记忆 overlay 解析器。
/// Phase A memory overlay resolver.
/// </summary>
public sealed class MemoryOverlayResolver
{
    private const int DefaultMaxFacts = 24;
    private static readonly Regex TokenRegex = new(@"[\p{L}\p{N}_\-.#/\\]+", RegexOptions.Compiled);
    private readonly MemoryPolicyEngine policy;

    public MemoryOverlayResolver(MemoryPolicyEngine? policy = null)
    {
        this.policy = policy ?? new MemoryPolicyEngine();
    }

    public MemoryOverlay Resolve(
        IEnumerable<FactMemoryRecord> defaultFacts,
        IEnumerable<FactMemoryRecord> providerFacts,
        HabitProfile? habitProfile = null,
        MemoryOverlayResolutionProfile? profile = null)
    {
        ArgumentNullException.ThrowIfNull(defaultFacts);
        ArgumentNullException.ThrowIfNull(providerFacts);

        profile ??= MemoryOverlayResolutionProfile.Default;
        var merged = new Dictionary<string, FactMemoryRecord>(StringComparer.Ordinal);
        foreach (var fact in defaultFacts.Where(fact => policy.CanRead(fact)))
        {
            MergeFact(merged, fact, profile);
        }

        foreach (var fact in providerFacts.Where(fact => policy.CanRead(fact)))
        {
            if (!ShouldInclude(fact, profile))
            {
                continue;
            }

            MergeFact(merged, fact, profile);
        }

        var rankedFacts = merged.Values
            .Select(fact => new RankedMemoryFact(fact, OverlayScore(fact, profile), Explain(fact, profile)))
            .OrderBy(item => ApplicabilityPriority(item.Fact, profile))
            .ThenByDescending(static item => item.Score)
            .ThenBy(static item => item.Fact.Key, StringComparer.Ordinal)
            .Take(profile.MaxFacts ?? int.MaxValue)
            .ToArray();
        var facts = rankedFacts.Select(static item => item.Fact).ToArray();
        var citation = new MemoryCitation(facts
            .Select(static fact => new MemoryCitationEntry(
                fact.Id,
                fact.MemorySpaceId,
                fact.Key,
                fact.Sources.FirstOrDefault(),
                "selected-by-overlay"))
            .ToArray());
        var explanations = rankedFacts
            .Select((item, index) => new MemoryOverlayExplanation(
                item.Fact.Id,
                item.Fact.MemorySpaceId,
                item.Fact.Key,
                index + 1,
                item.Score,
                item.Factors,
                profile.QueryTerms.Count == 0 ? "structured" : "keyword"))
            .ToArray();
        return new MemoryOverlay(
            facts,
            habitProfile,
            facts.Length == 0 ? MemoryMergeDecision.Ignored : MemoryMergeDecision.Applied,
            citation,
            explanations);
    }

    private static void MergeFact(
        Dictionary<string, FactMemoryRecord> merged,
        FactMemoryRecord candidate,
        MemoryOverlayResolutionProfile profile)
    {
        if (!merged.TryGetValue(candidate.Key, out var existing)
            || ApplicabilityPriority(candidate, profile) < ApplicabilityPriority(existing, profile)
            || (ApplicabilityPriority(candidate, profile) == ApplicabilityPriority(existing, profile)
                && OverlayScore(candidate, profile) >= OverlayScore(existing, profile)))
        {
            merged[candidate.Key] = candidate;
        }
    }

    private static bool ShouldInclude(FactMemoryRecord fact, MemoryOverlayResolutionProfile profile)
    {
        if (profile.AnchorMemorySpaceIds.Count == 0 && profile.QueryTerms.Count == 0)
        {
            return true;
        }

        if (IsAnchorSpace(fact.MemorySpaceId, profile))
        {
            return true;
        }

        if (!IsWorkspaceSpace(fact.MemorySpaceId))
        {
            return true;
        }

        return KeywordRelevanceScore(fact, profile) > 0;
    }

    private static int ApplicabilityPriority(FactMemoryRecord fact, MemoryOverlayResolutionProfile profile)
    {
        if (IsSessionSpace(fact.MemorySpaceId))
        {
            return 0;
        }

        if (IsCurrentWorkspaceSpace(fact.MemorySpaceId, profile))
        {
            return 1;
        }

        if (IsCollaborationSpace(fact.MemorySpaceId))
        {
            return 2;
        }

        if (IsTeamSpace(fact.MemorySpaceId))
        {
            return 3;
        }

        if (IsUserSpace(fact.MemorySpaceId))
        {
            return 4;
        }

        if (IsAgentSpace(fact.MemorySpaceId))
        {
            return 5;
        }

        if (IsWorkspaceSpace(fact.MemorySpaceId))
        {
            return 6;
        }

        return 7;
    }

    private static decimal OverlayScore(FactMemoryRecord fact, MemoryOverlayResolutionProfile profile)
    {
        var score = 0m;
        score += Math.Max(0, 12 - ApplicabilityPriority(fact, profile) * 2);
        score += KeywordRelevanceScore(fact, profile);
        score += fact.Confidence * 4m;
        score += Math.Min(fact.UsageCount, 8) * 0.25m;
        score += EvidenceStrength(fact) * 0.5m;
        score += RecencyScore(fact.RecordedAt);
        if (fact.IsCounterexample)
        {
            score += 1.5m;
        }

        return Math.Round(score, 4);
    }

    private static IReadOnlyList<string> Explain(FactMemoryRecord fact, MemoryOverlayResolutionProfile profile)
    {
        var factors = new List<string>
        {
            $"scope:{MemoryPolicySafety.ResolveScopeKind(fact.MemorySpaceId)?.ToString() ?? "unknown"}",
            $"confidence:{fact.Confidence:0.##}",
        };
        if (IsAnchorSpace(fact.MemorySpaceId, profile))
        {
            factors.Add("scope-match");
        }

        if (KeywordRelevanceScore(fact, profile) > 0)
        {
            factors.Add("keyword-match");
        }

        factors.Add("semantic:0");

        if (fact.ValidationEvidence.Count > 0 || fact.Sources.Count > 0)
        {
            factors.Add($"evidence:{EvidenceStrength(fact)}");
        }

        if (fact.UsageCount > 0)
        {
            factors.Add($"usage:{fact.UsageCount}");
        }

        if (fact.IsCounterexample)
        {
            factors.Add("negative-example");
        }

        factors.Add($"recency:{RecencyScore(fact.RecordedAt):0.##}");
        return factors;
    }

    private static int KeywordRelevanceScore(FactMemoryRecord fact, MemoryOverlayResolutionProfile profile)
    {
        var queryTerms = profile.QueryTerms;
        if (queryTerms.Count == 0)
        {
            return fact.IsCounterexample ? 2 : 0;
        }

        var score = 0;
        var searchable = MemorySearchText.Build(fact);
        foreach (var term in queryTerms)
        {
            if (searchable.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += 4;
            }
        }

        if (fact.IsCounterexample)
        {
            score += 2;
        }

        if (fact.ContextSignature?.Tags.Count > 0)
        {
            score += fact.ContextSignature.Tags.Count(tag =>
                queryTerms.Any(term => tag.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        return score;
    }

    private static int EvidenceStrength(FactMemoryRecord fact)
        => fact.ValidationEvidence.Count * 2 + fact.Sources.Count;

    private static decimal RecencyScore(DateTimeOffset recordedAt)
    {
        var age = DateTimeOffset.UtcNow - recordedAt;
        if (age.TotalDays <= 7)
        {
            return 2m;
        }

        if (age.TotalDays <= 30)
        {
            return 1m;
        }

        return 0m;
    }

    private static bool IsAnchorSpace(MemorySpaceId memorySpaceId, MemoryOverlayResolutionProfile profile)
        => profile.AnchorMemorySpaceIds.Contains(memorySpaceId.Value);

    private static bool IsCurrentWorkspaceSpace(MemorySpaceId memorySpaceId, MemoryOverlayResolutionProfile profile)
        => IsWorkspaceSpace(memorySpaceId) && IsAnchorSpace(memorySpaceId, profile);

    private static bool IsSessionSpace(MemorySpaceId memorySpaceId)
        => memorySpaceId.Value.StartsWith("memory:session:", StringComparison.OrdinalIgnoreCase);

    private static bool IsWorkspaceSpace(MemorySpaceId memorySpaceId)
        => memorySpaceId.Value.StartsWith("memory:workspace:", StringComparison.OrdinalIgnoreCase);

    private static bool IsCollaborationSpace(MemorySpaceId memorySpaceId)
        => memorySpaceId.Value.StartsWith("memory:collaboration:", StringComparison.OrdinalIgnoreCase);

    private static bool IsTeamSpace(MemorySpaceId memorySpaceId)
        => memorySpaceId.Value.StartsWith("memory:team:", StringComparison.OrdinalIgnoreCase);

    private static bool IsUserSpace(MemorySpaceId memorySpaceId)
        => memorySpaceId.Value.StartsWith("memory:user:", StringComparison.OrdinalIgnoreCase);

    private static bool IsAgentSpace(MemorySpaceId memorySpaceId)
        => memorySpaceId.Value.StartsWith("memory:agent:", StringComparison.OrdinalIgnoreCase);

    internal static IReadOnlyList<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        return TokenRegex.Matches(text)
            .Select(static match => match.Value.Trim().ToLowerInvariant())
            .Where(static token => token.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private sealed record RankedMemoryFact(
        FactMemoryRecord Fact,
        decimal Score,
        IReadOnlyList<string> Factors);
}

/// <summary>
/// 记忆 overlay 解析画像，用于表达当前上下文锚点、查询词和输出上限。
/// Memory overlay resolution profile that carries context anchors, query terms, and output limits.
/// </summary>
public sealed record MemoryOverlayResolutionProfile(
    IReadOnlySet<string> AnchorMemorySpaceIds,
    IReadOnlyList<string> QueryTerms,
    int? MaxFacts)
{
    public static MemoryOverlayResolutionProfile Default { get; } = new(
        new HashSet<string>(StringComparer.Ordinal),
        Array.Empty<string>(),
        MaxFacts: null);

    public static MemoryOverlayResolutionProfile Create(
        IEnumerable<MemorySpaceId> anchorMemorySpaceIds,
        string? queryText,
        int? maxFacts = DefaultMaxFacts)
        => new(
            new HashSet<string>(
                anchorMemorySpaceIds.Select(static id => id.Value),
                StringComparer.Ordinal),
            MemoryOverlayResolver.Tokenize(queryText),
            maxFacts);

    private const int DefaultMaxFacts = 24;
}
