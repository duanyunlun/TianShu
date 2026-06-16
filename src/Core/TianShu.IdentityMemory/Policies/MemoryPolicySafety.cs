using TianShu.Contracts.Memory;
using TianShu.Contracts.Primitives;

namespace TianShu.IdentityMemory;

internal static class MemoryPolicySafety
{
    internal static MemoryScopeKind? ResolveScopeKind(MemorySpaceId memorySpaceId)
    {
        var value = memorySpaceId.Value;
        if (value.StartsWith("memory:user:", StringComparison.OrdinalIgnoreCase))
        {
            return MemoryScopeKind.User;
        }

        if (value.StartsWith("memory:workspace:", StringComparison.OrdinalIgnoreCase))
        {
            return MemoryScopeKind.Workspace;
        }

        if (value.StartsWith("memory:team:", StringComparison.OrdinalIgnoreCase))
        {
            return MemoryScopeKind.Team;
        }

        if (value.StartsWith("memory:session:", StringComparison.OrdinalIgnoreCase))
        {
            return MemoryScopeKind.Session;
        }

        if (value.StartsWith("memory:agent:", StringComparison.OrdinalIgnoreCase))
        {
            return MemoryScopeKind.Agent;
        }

        if (value.StartsWith("memory:collaboration:", StringComparison.OrdinalIgnoreCase))
        {
            return MemoryScopeKind.Collaboration;
        }

        return null;
    }

    internal static bool IsScopeEscalation(MemoryScopeKind? sourceScopeKind, MemoryScopeKind targetScopeKind)
    {
        if (sourceScopeKind is null)
        {
            return false;
        }

        return ScopePersistenceRank(targetScopeKind) > ScopePersistenceRank(sourceScopeKind.Value);
    }

    internal static bool IsLongTermBehaviorChange(string key, MemoryScopeKind targetScopeKind)
        => ScopePersistenceRank(targetScopeKind) >= ScopePersistenceRank(MemoryScopeKind.User)
           && (key.StartsWith("preference.", StringComparison.OrdinalIgnoreCase)
               || key.StartsWith("behavior.", StringComparison.OrdinalIgnoreCase)
               || key.Contains(".default", StringComparison.OrdinalIgnoreCase)
               || key.Contains(".avoid", StringComparison.OrdinalIgnoreCase));

    internal static bool LooksSensitive(StructuredValue value)
        => LooksSensitive(value.GetString());

    internal static bool LooksSensitive(MemorySourceRef source)
        => LooksSensitive(source.SourceId)
           || LooksSensitive(source.Snippet)
           || source.Metadata.Any(item => LooksSensitive(item.Key) || LooksSensitive(item.Value));

    internal static bool LooksSensitive(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        var lower = normalized.ToLowerInvariant();
        return lower.StartsWith("sk-", StringComparison.Ordinal)
               || lower.StartsWith("sk_", StringComparison.Ordinal)
               || lower.StartsWith("ghp_", StringComparison.Ordinal)
               || lower.StartsWith("github_pat_", StringComparison.Ordinal)
               || lower.StartsWith("xoxb-", StringComparison.Ordinal)
               || lower.StartsWith("bearer ", StringComparison.Ordinal)
               || lower.Contains("=sk-", StringComparison.Ordinal)
               || lower.Contains("=ghp_", StringComparison.Ordinal)
               || lower.Contains("password", StringComparison.Ordinal)
               || lower.Contains("secret", StringComparison.Ordinal)
               || lower.Contains("api_key", StringComparison.Ordinal)
               || lower.Contains("apikey", StringComparison.Ordinal)
               || lower.Contains("access_token", StringComparison.Ordinal)
               || lower.Contains("bearer_token", StringComparison.Ordinal)
               || lower.Contains("private key", StringComparison.Ordinal);
    }

    internal static MemorySourceStrength DeriveSourceStrength(MemorySourceRef? source, MemorySourceStrength explicitStrength)
    {
        if (explicitStrength != MemorySourceStrength.Unknown)
        {
            return explicitStrength;
        }

        if (source is null || source.SourceKind == MemorySourceKind.Unknown)
        {
            return MemorySourceStrength.Unknown;
        }

        return source.SourceKind switch
        {
            MemorySourceKind.System or MemorySourceKind.ToolResult or MemorySourceKind.Artifact => MemorySourceStrength.Strong,
            MemorySourceKind.ExternalProvider or MemorySourceKind.Url => MemorySourceStrength.Weak,
            _ => MemorySourceStrength.Normal,
        };
    }

    private static int ScopePersistenceRank(MemoryScopeKind scopeKind)
        => scopeKind switch
        {
            MemoryScopeKind.Session => 0,
            MemoryScopeKind.Workspace => 1,
            MemoryScopeKind.Team => 2,
            MemoryScopeKind.Agent => 2,
            MemoryScopeKind.Collaboration => 2,
            MemoryScopeKind.User => 3,
            _ => 1,
        };
}
