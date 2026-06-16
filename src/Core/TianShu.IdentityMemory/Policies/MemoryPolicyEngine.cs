using TianShu.Contracts.Memory;
using TianShu.Contracts.Primitives;

namespace TianShu.IdentityMemory;

/// <summary>
/// Phase A 记忆策略引擎 façade，向旧调用点暴露兼容入口，实际职责委托给 typed policy。
/// Phase A memory policy facade that keeps legacy call sites stable while delegating to typed policies.
/// </summary>
public sealed class MemoryPolicyEngine
{
    public MemoryPolicyEngine(
        MemoryReadPolicy? readPolicy = null,
        MemoryIngestionPolicy? ingestionPolicy = null,
        MemoryPromotionPolicy? promotionPolicy = null)
    {
        Read = readPolicy ?? new MemoryReadPolicy();
        Ingestion = ingestionPolicy ?? new MemoryIngestionPolicy();
        Promotion = promotionPolicy ?? new MemoryPromotionPolicy();
    }

    public MemoryReadPolicy Read { get; }

    public MemoryIngestionPolicy Ingestion { get; }

    public MemoryPromotionPolicy Promotion { get; }

    public bool Supports(MemoryProviderDescriptor descriptor, MemoryProviderCapability capability)
        => Read.Supports(descriptor, capability);

    public bool CanRead(FactMemoryRecord fact, FilterMemory? query = null)
        => Read.CanRead(fact, query);
}

/// <summary>
/// 读取与 provider capability 策略。
/// Read and provider-capability policy.
/// </summary>
public sealed class MemoryReadPolicy
{
    public bool Supports(MemoryProviderDescriptor descriptor, MemoryProviderCapability capability)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return (descriptor.Capabilities & capability) == capability;
    }

    public bool CanRead(FactMemoryRecord fact, FilterMemory? query = null)
    {
        ArgumentNullException.ThrowIfNull(fact);

        if (query?.LifecycleStatus is { } lifecycleStatus)
        {
            if (fact.LifecycleStatus != lifecycleStatus)
            {
                return false;
            }
        }
        else if (fact.LifecycleStatus is MemoryLifecycleStatus.PendingReview
                 or MemoryLifecycleStatus.Archived
                 or MemoryLifecycleStatus.Forgotten
                 or MemoryLifecycleStatus.Deleted)
        {
            return false;
        }

        if (query?.MemorySpaceId is { } memorySpaceId
            && !string.Equals(fact.MemorySpaceId.Value, memorySpaceId.Value, StringComparison.Ordinal))
        {
            return false;
        }

        if (query?.ContextSignature is { } contextSignature
            && !MatchesContextSignature(fact, contextSignature))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query?.Key)
            && !string.Equals(fact.Key, query.Key, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query?.QueryText)
            && !MemorySearchText.Matches(fact, query.QueryText))
        {
            return false;
        }

        if (query?.MinimumConfidence is { } minimumConfidence
            && fact.Confidence < minimumConfidence)
        {
            return false;
        }

        if (query?.SourceKind is { } sourceKind
            && !fact.Sources.Any(source => source.SourceKind == sourceKind))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query?.SourceId)
            && !fact.Sources.Any(source => string.Equals(source.SourceId, query.SourceId, StringComparison.Ordinal)))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query?.Tag)
            && !fact.Tags.Any(tag => string.Equals(tag, query.Tag, StringComparison.Ordinal)))
        {
            return false;
        }

        if (query?.MinimumUsageCount is { } minimumUsageCount
            && fact.UsageCount < minimumUsageCount)
        {
            return false;
        }

        if (query?.RecordedAfter is { } recordedAfter && fact.RecordedAt < recordedAfter)
        {
            return false;
        }

        if (query?.RecordedBefore is { } recordedBefore && fact.RecordedAt > recordedBefore)
        {
            return false;
        }

        if (query?.UpdatedAfter is { } updatedAfter && fact.UpdatedAt < updatedAfter)
        {
            return false;
        }

        if (query?.UpdatedBefore is { } updatedBefore && fact.UpdatedAt > updatedBefore)
        {
            return false;
        }

        if (query?.UsedAfter is { } usedAfter
            && (fact.LastUsedAt is null || fact.LastUsedAt < usedAfter))
        {
            return false;
        }

        if (query?.UsedBefore is { } usedBefore
            && (fact.LastUsedAt is null || fact.LastUsedAt > usedBefore))
        {
            return false;
        }

        return true;
    }

    private static bool MatchesContextSignature(FactMemoryRecord fact, MemoryContextSignature signature)
    {
        if (signature.MemorySpaceIds.Count > 0
            && !signature.MemorySpaceIds.Any(space => string.Equals(space.Value, fact.MemorySpaceId.Value, StringComparison.Ordinal)))
        {
            return false;
        }

        if (signature.ScopeKinds.Count > 0
            && MemoryPolicySafety.ResolveScopeKind(fact.MemorySpaceId) is { } scopeKind
            && !signature.ScopeKinds.Contains(scopeKind))
        {
            return false;
        }

        if (signature.ScopeKinds.Count > 0 && MemoryPolicySafety.ResolveScopeKind(fact.MemorySpaceId) is null)
        {
            return false;
        }

        if (signature.Tags.Count > 0
            && !signature.Tags.Any(tag => fact.Tags.Any(factTag => string.Equals(factTag, tag, StringComparison.Ordinal))))
        {
            return false;
        }

        if (signature.Sources.Count > 0
            && !signature.Sources.Any(source => fact.Sources.Any(factSource =>
                factSource.SourceKind == source.SourceKind
                && string.Equals(factSource.SourceId, source.SourceId, StringComparison.Ordinal))))
        {
            return false;
        }

        if (signature.LifecycleStatuses.Count > 0
            && !signature.LifecycleStatuses.Contains(fact.LifecycleStatus))
        {
            return false;
        }

        if (signature.ExcludeRecordIds.Any(id => string.Equals(id.Value, fact.Id.Value, StringComparison.Ordinal)))
        {
            return false;
        }

        if (signature.MinimumConfidence is { } minimumConfidence && fact.Confidence < minimumConfidence)
        {
            return false;
        }

        if (signature.RecordedAfter is { } recordedAfter && fact.RecordedAt < recordedAfter)
        {
            return false;
        }

        if (signature.RecordedBefore is { } recordedBefore && fact.RecordedAt > recordedBefore)
        {
            return false;
        }

        if (signature.UpdatedAfter is { } updatedAfter && fact.UpdatedAt < updatedAfter)
        {
            return false;
        }

        if (signature.UpdatedBefore is { } updatedBefore && fact.UpdatedAt > updatedBefore)
        {
            return false;
        }

        if (signature.UsedAfter is { } usedAfter
            && (fact.LastUsedAt is null || fact.LastUsedAt < usedAfter))
        {
            return false;
        }

        if (signature.UsedBefore is { } usedBefore
            && (fact.LastUsedAt is null || fact.LastUsedAt > usedBefore))
        {
            return false;
        }

        return true;
    }
}

/// <summary>
/// 本地关键词检索文本构造器；语义检索不可用时作为明确降级路径。
/// Local keyword-search text builder used as an explicit fallback when semantic search is unavailable.
/// </summary>
internal static class MemorySearchText
{
    public static bool Matches(FactMemoryRecord fact, string queryText)
    {
        ArgumentNullException.ThrowIfNull(fact);
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return true;
        }

        var searchable = Build(fact);
        return MemoryOverlayResolver.Tokenize(queryText).Any(term =>
            searchable.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    public static string Build(FactMemoryRecord fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        var parts = new List<string> { fact.Key };
        AppendStructuredValue(parts, fact.Value);
        parts.AddRange(fact.Tags);
        if (fact.ContextSignature is { } signature)
        {
            parts.AddRange(signature.Tags);
            parts.AddRange(signature.ScopeKinds.Select(static kind => kind.ToString()));
        }

        foreach (var source in fact.Sources)
        {
            parts.Add(source.SourceId);
            if (!string.IsNullOrWhiteSpace(source.Snippet))
            {
                parts.Add(source.Snippet!);
            }

            if (!string.IsNullOrWhiteSpace(source.Path))
            {
                parts.Add(source.Path!);
            }
        }

        return string.Join(' ', parts);
    }

    private static void AppendStructuredValue(List<string> parts, StructuredValue value)
    {
        if (!string.IsNullOrWhiteSpace(value.StringValue))
        {
            parts.Add(value.StringValue!);
        }

        if (!string.IsNullOrWhiteSpace(value.NumberValue))
        {
            parts.Add(value.NumberValue!);
        }

        if (value.BooleanValue is { } booleanValue)
        {
            parts.Add(booleanValue.ToString());
        }

        foreach (var pair in value.Properties)
        {
            parts.Add(pair.Key);
            AppendStructuredValue(parts, pair.Value);
        }

        foreach (var item in value.Items)
        {
            AppendStructuredValue(parts, item);
        }
    }
}
