using static TianShu.AppHost.Configuration.KernelTomlTextParsingUtilities;

namespace TianShu.AppHost.Tools;

/// <summary>
/// MCP server OAuth URL fallback、authStatus 与配置归一化辅助件。
/// Host-side helpers for MCP server OAuth URL fallback, auth-status shaping, and config normalization.
/// </summary>
internal static class McpServerAuthUtilities
{
    public static async Task<List<string>> ListMcpServerNamesAsync(
        string tianShuHomePath,
        Func<CancellationToken, Task<Dictionary<string, string>>> loadEffectiveConfigValuesAsync,
        CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in KernelMcpServerPackageManifestLoader.ListServerNames(tianShuHomePath))
        {
            names.Add(name);
        }

        var configToml = Path.Combine(tianShuHomePath, "tianshu.toml");
        foreach (var name in ReadTomlSectionNames(configToml, "mcp_servers"))
        {
            names.Add(name);
        }

        var values = await loadEffectiveConfigValuesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var key in values.Keys)
        {
            if (TryParseScopedConfigKey(key, "mcp_servers", out var name, out _))
            {
                names.Add(name);
            }
        }

        return names
            .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static async Task<string?> ResolveMcpServerAuthorizationUrlAsync(
        string serverName,
        string tianShuHomePath,
        Func<CancellationToken, Task<Dictionary<string, string>>> loadEffectiveConfigValuesAsync,
        CancellationToken cancellationToken)
    {
        var values = await loadEffectiveConfigValuesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var key in new[]
        {
            $"mcp_servers.{serverName}.oauth_authorization_url",
            $"mcp_servers.{serverName}.url",
        })
        {
            if (!values.TryGetValue(key, out var raw))
            {
                continue;
            }

            var value = NormalizeRawConfigValue(raw);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return TryReadMcpServerUrlFromToml(Path.Combine(tianShuHomePath, "tianshu.toml"), serverName)
               ?? KernelMcpServerPackageManifestLoader.ResolveServerUrl(tianShuHomePath, serverName);
    }

    public static string? TryReadMcpServerUrlFromToml(string configTomlPath, string serverName)
    {
        if (!File.Exists(configTomlPath))
        {
            return null;
        }

        var inSection = false;
        foreach (var line in File.ReadLines(configTomlPath))
        {
            var text = line.Trim();
            if (string.IsNullOrWhiteSpace(text) || text.StartsWith('#'))
            {
                continue;
            }

            if (text.StartsWith('[') && text.EndsWith(']'))
            {
                var section = text[1..^1].Trim();
                inSection = string.Equals(section, $"mcp_servers.{serverName}", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(section, $"mcp_servers.\"{serverName}\"", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inSection || !text.Contains('='))
            {
                continue;
            }

            var idx = text.IndexOf('=', StringComparison.Ordinal);
            var key = text[..idx].Trim();
            var value = text[(idx + 1)..].Trim();
            if (string.Equals(key, "oauth_authorization_url", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "url", StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeRawConfigValue(value);
            }
        }

        return null;
    }

    public static string ResolveMcpServerAuthStatus(string serverName, IReadOnlyDictionary<string, string> values)
    {
        if (HasConfiguredValue(values, $"mcp_servers.{serverName}.oauth_access_token"))
        {
            return "oauth";
        }

        if (HasConfiguredValue(values, $"mcp_servers.{serverName}.access_token")
            || HasConfiguredValue(values, $"mcp_servers.{serverName}.api_key"))
        {
            return "bearer_token";
        }

        var hasEndpoint = HasConfiguredValue(values, $"mcp_servers.{serverName}.oauth_authorization_url")
                          || HasConfiguredValue(values, $"mcp_servers.{serverName}.url");
        if (hasEndpoint)
        {
            return "not_logged_in";
        }

        return "unsupported";
    }

    public static bool HasConfiguredValue(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var raw)
               && !string.IsNullOrWhiteSpace(NormalizeRawConfigValue(raw));
    }

    public static string? NormalizeRawConfigValue(string raw)
    {
        var value = ReadScalarConfigValue(raw);
        return KernelToolJsonHelpers.Normalize(value);
    }

    private static IEnumerable<string> ReadTomlSectionNames(string configTomlPath, string sectionPrefix)
    {
        if (!File.Exists(configTomlPath))
        {
            yield break;
        }

        foreach (var line in File.ReadLines(configTomlPath))
        {
            var text = line.Trim();
            if (!TryExtractTomlSectionName(text, sectionPrefix, out var name))
            {
                continue;
            }

            yield return name;
        }
    }

    private static bool TryExtractTomlSectionName(string text, string sectionPrefix, out string name)
    {
        name = string.Empty;
        if (!text.StartsWith('[') || !text.EndsWith(']'))
        {
            return false;
        }

        var section = text[1..^1].Trim();
        var prefix = $"{sectionPrefix}.";
        if (!section.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remainder = section[prefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(remainder))
        {
            return false;
        }

        if (remainder.StartsWith('"'))
        {
            var closing = remainder.IndexOf('"', 1);
            if (closing <= 1)
            {
                return false;
            }

            name = remainder[1..closing];
            return !string.IsNullOrWhiteSpace(name);
        }

        var dotIndex = remainder.IndexOf('.');
        var candidate = dotIndex >= 0 ? remainder[..dotIndex] : remainder;
        name = candidate.Trim().Trim('"', '\'');
        return !string.IsNullOrWhiteSpace(name);
    }

    private static bool TryParseScopedConfigKey(string key, string sectionPrefix, out string name, out string leaf)
    {
        name = string.Empty;
        leaf = string.Empty;
        var prefix = $"{sectionPrefix}.";
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remainder = key[prefix.Length..];
        if (string.IsNullOrWhiteSpace(remainder))
        {
            return false;
        }

        if (remainder.StartsWith('"'))
        {
            var closing = remainder.IndexOf('"', 1);
            if (closing <= 1 || closing + 2 >= remainder.Length || remainder[closing + 1] != '.')
            {
                return false;
            }

            name = remainder[1..closing];
            leaf = remainder[(closing + 2)..];
            return !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(leaf);
        }

        var split = remainder.IndexOf('.');
        if (split <= 0 || split >= remainder.Length - 1)
        {
            return false;
        }

        name = remainder[..split].Trim().Trim('"', '\'');
        leaf = remainder[(split + 1)..].Trim();
        return !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(leaf);
    }
}
