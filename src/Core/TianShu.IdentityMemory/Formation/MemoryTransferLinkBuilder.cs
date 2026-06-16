using TianShu.Contracts.Memory;

namespace TianShu.IdentityMemory;

/// <summary>
/// 基于结构化上下文构建类比迁移链路摘要，不依赖 embedding 或外部检索。
/// Builds analogical-transfer summaries from structured context without embeddings or external retrieval.
/// </summary>
public sealed class MemoryTransferLinkBuilder
{
    public MemoryTransferLink? Build(
        MemoryCandidate candidate,
        IEnumerable<FactMemoryRecord> existingFacts,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(existingFacts);

        var matches = existingFacts
            .Where(fact => fact.LifecycleStatus == MemoryLifecycleStatus.Active)
            .Select(fact => new
            {
                Fact = fact,
                Basis = SimilarityBasis(candidate, fact),
            })
            .Where(match => match.Basis.Count > 0)
            .OrderByDescending(static match => match.Basis.Count)
            .ThenBy(static match => match.Fact.Key, StringComparer.Ordinal)
            .Take(3)
            .ToArray();

        if (matches.Length == 0)
        {
            return null;
        }

        var basis = string.Join(", ", matches.SelectMany(static match => match.Basis).Distinct(StringComparer.Ordinal));
        return new MemoryTransferLink(
            matches.Select(static match => match.Fact.Id).ToArray(),
            basis,
            $"Candidate `{candidate.Key}` may transfer from related structured memories.",
            candidate.ValidationEvidence.Select(static evidence => evidence.EvidenceId).ToArray(),
            applicability: candidate.ContextSignature is null ? null : "context-signature",
            createdAt: timestamp);
    }

    private static IReadOnlyList<string> SimilarityBasis(MemoryCandidate candidate, FactMemoryRecord fact)
    {
        var basis = new List<string>();
        if (string.Equals(candidate.Key, fact.Key, StringComparison.Ordinal))
        {
            basis.Add("same-key");
        }

        if (candidate.ContextSignature?.Tags.Count > 0
            && candidate.ContextSignature.Tags.Intersect(fact.Tags, StringComparer.Ordinal).Any())
        {
            basis.Add("shared-tags");
        }

        if (candidate.ContextSignature?.MemorySpaceIds.Any(space =>
                string.Equals(space.Value, fact.MemorySpaceId.Value, StringComparison.Ordinal)) == true)
        {
            basis.Add("same-space");
        }

        if (candidate.ContextSignature?.Sources.Any(source =>
                fact.Sources.Any(factSource => string.Equals(factSource.SourceId, source.SourceId, StringComparison.Ordinal))) == true)
        {
            basis.Add("same-source");
        }

        return basis;
    }
}
