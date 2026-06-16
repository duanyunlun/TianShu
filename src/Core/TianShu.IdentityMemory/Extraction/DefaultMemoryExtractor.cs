using TianShu.Contracts.Memory;

namespace TianShu.IdentityMemory;

/// <summary>
/// 默认组合抽取器：确定性规则兜底，自适应语义抽取补足无固定关键词的表达。
/// </summary>
public sealed class DefaultMemoryExtractor : IMemoryExtractor
{
    private readonly IReadOnlyList<IMemoryExtractor> extractors;

    public DefaultMemoryExtractor()
        : this([new RuleBasedMemoryExtractor(), new AdaptiveMemoryExtractor()])
    {
    }

    public DefaultMemoryExtractor(IReadOnlyList<IMemoryExtractor> extractors)
    {
        ArgumentNullException.ThrowIfNull(extractors);
        if (extractors.Count == 0)
        {
            throw new ArgumentException("默认记忆抽取器至少需要一个子抽取器。", nameof(extractors));
        }

        this.extractors = extractors;
    }

    public async Task<IReadOnlyList<MemoryCandidate>> ExtractAsync(
        ExtractMemory command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var results = new Dictionary<string, MemoryCandidate>(StringComparer.Ordinal);
        foreach (var extractor in extractors)
        {
            var candidates = await extractor.ExtractAsync(command, context, cancellationToken).ConfigureAwait(false);
            foreach (var candidate in candidates)
            {
                var key = $"{candidate.Key}\n{candidate.Value.GetString()}";
                if (!results.TryGetValue(key, out var existing) || candidate.Confidence > existing.Confidence)
                {
                    results[key] = candidate;
                }
            }
        }

        return results.Values.ToArray();
    }
}
