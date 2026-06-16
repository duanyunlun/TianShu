namespace TianShu.Provider.Abstractions;

/// <summary>
/// Resolves provider HTTP endpoints from a configured base URL.
/// 根据配置的 base URL 解析 provider HTTP endpoint。
/// </summary>
public static class ProviderEndpointPathUtilities
{
    /// <summary>
    /// Resolves an endpoint whose official path has a version segment and a terminal resource path.
    /// 解析带版本段与终止资源路径的官方 endpoint。
    /// </summary>
    public static string ResolveVersionedEndpoint(
        string baseUrl,
        string versionSegment,
        string terminalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionSegment);
        ArgumentException.ThrowIfNullOrWhiteSpace(terminalPath);

        var trimmed = baseUrl.TrimEnd('/');
        var version = NormalizeSegment(versionSegment);
        var terminal = NormalizePath(terminalPath);
        if (trimmed.EndsWith(terminal, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return $"{trimmed}/{version.TrimStart('/')}{terminal}";
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith(version, StringComparison.OrdinalIgnoreCase))
        {
            return $"{trimmed}{terminal}";
        }

        return $"{trimmed}{version}{terminal}";
    }

    private static string NormalizeSegment(string value)
    {
        var normalized = value.Trim().Trim('/');
        return "/" + normalized;
    }

    private static string NormalizePath(string value)
    {
        var normalized = value.Trim();
        return normalized.StartsWith("/", StringComparison.Ordinal)
            ? normalized
            : "/" + normalized;
    }
}
