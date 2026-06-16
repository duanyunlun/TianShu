using System.Globalization;
using Tomlyn;
using Tomlyn.Model;

namespace TianShu.AppHost.Configuration;

/// <summary>
/// 读取 TianShu 原生 TOML 中的 memory provider 配置。
/// Loads memory provider configuration from TianShu native TOML files.
/// </summary>
public static class TianShuMemoryProviderConfigurationLoader
{
    public static IReadOnlyList<TianShuMemoryProviderConfiguration> LoadDefault(string? cwd = null)
    {
        var paths = new List<string>
        {
            TianShuConfigTomlPathResolver.ResolveSystemConfigTomlPath(),
            TianShuConfigTomlPathResolver.ResolveUserConfigTomlPath(),
        };
        paths.AddRange(TianShuConfigTomlPathResolver.EnumerateProjectConfigPaths(cwd));
        paths.Add(TianShuConfigTomlPathResolver.ResolveCwdConfigTomlPath(cwd));
        return LoadFiles(paths);
    }

    public static IReadOnlyList<TianShuMemoryProviderConfiguration> LoadFiles(IEnumerable<string> paths)
    {
        var providers = new Dictionary<string, TianShuMemoryProviderConfiguration>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths.Where(File.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var provider in LoadFile(path))
            {
                providers[provider.ProviderId] = provider;
            }
        }

        return providers.Values.OrderBy(static provider => provider.ProviderId, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyList<TianShuMemoryProviderConfiguration> LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var parsed = Toml.Parse(File.ReadAllText(path), path);
        if (parsed.HasErrors)
        {
            return Array.Empty<TianShuMemoryProviderConfiguration>();
        }

        var root = TomlTable.From(parsed);
        if (root.TryGetValue("memory", out var memoryValue) is false || memoryValue is not TomlTable memory)
        {
            return Array.Empty<TianShuMemoryProviderConfiguration>();
        }

        if (memory.TryGetValue("providers", out var providersValue) is false || providersValue is not TomlTable providers)
        {
            return Array.Empty<TianShuMemoryProviderConfiguration>();
        }

        var results = new List<TianShuMemoryProviderConfiguration>();
        foreach (var pair in providers)
        {
            if (pair.Value is not TomlTable provider)
            {
                continue;
            }

            results.Add(new TianShuMemoryProviderConfiguration(
                pair.Key,
                GetString(provider, "kind") ?? string.Empty,
                GetBoolean(provider, "enabled") ?? true,
                GetString(provider, "display_name"),
                GetString(provider, "host"),
                GetInteger(provider, "port"),
                GetInteger(provider, "grpc_port"),
                GetString(provider, "api_key_env"),
                GetString(provider, "authorization_env"),
                NormalizeMode(GetString(provider, "mode")),
                GetStringArray(provider, "capabilities"),
                GetInteger(provider, "connect_timeout_ms")));
        }

        return results;
    }

    private static string? GetString(TomlTable table, string key)
        => table.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static bool? GetBoolean(TomlTable table, string key)
        => table.TryGetValue(key, out var value) && value is bool boolean ? boolean : null;

    private static int? GetInteger(TomlTable table, string key)
    {
        if (table.TryGetValue(key, out var value) is false)
        {
            return null;
        }

        try
        {
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> GetStringArray(TomlTable table, string key)
    {
        if (table.TryGetValue(key, out var value) is false || value is not TomlArray array)
        {
            return Array.Empty<string>();
        }

        return array.Select(static item => item?.ToString()).Where(static item => !string.IsNullOrWhiteSpace(item)).Cast<string>().ToArray();
    }

    private static string NormalizeMode(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "read-write" or "readwrite" => "read-write",
            "mirror" => "mirror",
            "import-export" or "importexport" => "import-export",
            _ => "read-only",
        };
}

public sealed record TianShuMemoryProviderConfiguration(
    string ProviderId,
    string Kind,
    bool Enabled,
    string? DisplayName,
    string? Host,
    int? Port,
    int? GrpcPort,
    string? ApiKeyEnvironmentVariable,
    string? AuthorizationEnvironmentVariable,
    string Mode,
    IReadOnlyList<string> Capabilities,
    int? ConnectTimeoutMs);
