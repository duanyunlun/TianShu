using System.Globalization;
using System.Text.Json;
using TianShu.Contracts.Configuration;
using Tomlyn;
using Tomlyn.Model;

namespace TianShu.Configuration;

/// <summary>
/// TianShu home / state / session 根目录解析工具。
/// </summary>
public static class TianShuHomePathUtilities
{
    public static string ResolveTianShuHomePath()
        => TianShuRuntimeLayoutPaths.ResolveTianShuHomePath();

    public static string ResolveTianShuStateRootPath()
        => TianShuRuntimeLayoutPaths.ResolveTianShuStateRootPath();

    public static string ResolveTianShuSessionsRootPath()
        => TianShuRuntimeLayoutPaths.ResolveTianShuSessionsRootPath();

    public static string ResolveTianShuRuntimeRootPath()
        => TianShuRuntimeLayoutPaths.ResolveTianShuRuntimeRootPath();

    public static string ResolveTianShuDataRootPath()
        => TianShuRuntimeLayoutPaths.ResolveTianShuDataRootPath();

    public static string ResolveTianShuModulesRootPath()
        => TianShuRuntimeLayoutPaths.ResolveTianShuModulesRootPath();

    public static string ResolveTianShuModulePath(params string[] segments)
        => TianShuRuntimeLayoutPaths.ResolveTianShuModulePath(segments);

    public static string ResolveRuntimePathFromHome(string tianShuHomePath, params string[] segments)
        => TianShuRuntimeLayoutPaths.ResolveRuntimePathFromHome(tianShuHomePath, segments);

    public static string ResolveDataPathFromHome(string tianShuHomePath, params string[] segments)
        => TianShuRuntimeLayoutPaths.ResolveDataPathFromHome(tianShuHomePath, segments);

    public static string ResolveModulePathFromHome(string tianShuHomePath, params string[] segments)
        => TianShuRuntimeLayoutPaths.ResolveModulePathFromHome(tianShuHomePath, segments);

    public static string ResolveModulePathFromConfig(string TianShuConfigPath, params string[] segments)
        => TianShuRuntimeLayoutPaths.ResolveModulePathFromConfig(TianShuConfigPath, segments);

    public static string ResolveDataPathFromConfig(string TianShuConfigPath, params string[] segments)
        => TianShuRuntimeLayoutPaths.ResolveDataPathFromConfig(TianShuConfigPath, segments);
}

/// <summary>
/// TianShu 技能根目录路径工具。
/// </summary>
public static class TianShuSkillRootPaths
{
    private const string DefaultProgramDataDirectory = @"C:\ProgramData";

    public static string ResolveSystemSkillsCacheRoot(string homePath)
        => TianShuRuntimeLayoutPaths.ResolveSystemSkillsCacheRoot(homePath);

    public static string ResolveAdminSkillsRoot(string? systemConfigRoot = null)
        => Path.Combine(
            NormalizePath(systemConfigRoot) ?? ResolveDefaultSystemConfigRoot(),
            "skills");

    public static string ResolveDefaultSystemConfigRoot()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "/etc/tianshu";
        }

        var programData = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData))
            ?? DefaultProgramDataDirectory;
        return Path.Combine(programData, "TianShu");
    }

    private static string? NormalizePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Path.GetFullPath(value.Trim());
    }
}

/// <summary>
/// TianShu 配置文件路径解析器。
/// </summary>
public static class TianShuConfigTomlPathResolver
{
    private const string SystemRootOverrideEnvironmentVariable = "TIANSHU_SYSTEM_CONFIG_ROOT";
    private const string UnixSystemConfigTomlPath = "/etc/tianshu/tianshu.toml";
    private const string WindowsDefaultProgramDataPath = @"C:\ProgramData";

    public static string ResolveSystemConfigTomlPath()
    {
        var systemRootOverride = ResolveSystemRootOverride();
        if (!string.IsNullOrWhiteSpace(systemRootOverride))
        {
            return Path.Combine(systemRootOverride!, "tianshu.toml");
        }

        if (!OperatingSystem.IsWindows())
        {
            return UnixSystemConfigTomlPath;
        }

        var programData = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData))
            ?? NormalizePath(Environment.GetEnvironmentVariable("ProgramData"))
            ?? WindowsDefaultProgramDataPath;
        return Path.Combine(programData, "TianShu", "tianshu.toml");
    }

    public static string ResolveUserConfigTomlPath()
        => Path.Combine(TianShuHomePathUtilities.ResolveTianShuHomePath(), "tianshu.toml");

    public static string ResolveCwdConfigTomlPath(string? cwd)
    {
        var directory = NormalizeDirectory(cwd) ?? NormalizeDirectory(Environment.CurrentDirectory);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("无法确定工作区目录，无法读取 cwd tianshu.toml。");
        }

        return Path.Combine(directory, ".tianshu", "tianshu.toml");
    }

    public static IReadOnlyList<string> EnumerateProjectConfigPaths(
        string? cwd,
        IReadOnlyList<string>? projectRootMarkers = null)
    {
        var directory = NormalizeDirectory(cwd);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return Array.Empty<string>();
        }

        var paths = new List<string>();
        foreach (var current in TianShuProjectRootResolver.EnumerateDirectoriesBetweenProjectRootAndCwd(directory, projectRootMarkers))
        {
            var path = Path.Combine(current, ".tianshu", "tianshu.toml");
            if (File.Exists(path))
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    public static string? NormalizeDirectory(string? cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd))
        {
            return null;
        }

        var fullPath = Path.IsPathRooted(cwd)
            ? Path.GetFullPath(cwd)
            : Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, cwd));

        return File.Exists(fullPath) ? Path.GetDirectoryName(fullPath) : fullPath;
    }

    private static string? ResolveSystemRootOverride()
        => NormalizePath(Environment.GetEnvironmentVariable(SystemRootOverrideEnvironmentVariable));

    private static string? NormalizePath(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// TianShu 项目根解析器。
/// </summary>
public static class TianShuProjectRootResolver
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private static readonly StringComparer KeyComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly string[] DefaultProjectRootMarkers = [".git"];

    public static IReadOnlyList<string> ResolveProjectRootMarkers()
    {
        var userConfigPath = TianShuConfigTomlPathResolver.ResolveUserConfigTomlPath();
        if (!File.Exists(userConfigPath))
        {
            return DefaultProjectRootMarkers;
        }

        try
        {
            var syntax = Toml.Parse(File.ReadAllText(userConfigPath));
            if (syntax.HasErrors || syntax.ToModel() is not TomlTable root)
            {
                return DefaultProjectRootMarkers;
            }

            if (!root.TryGetValue("project_root_markers", out var markersValue)
                || markersValue is not TomlArray markersArray)
            {
                return DefaultProjectRootMarkers;
            }

            return markersArray
                .OfType<string>()
                .Select(Normalize)
                .Where(static marker => !string.IsNullOrWhiteSpace(marker))
                .Select(static marker => marker!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return DefaultProjectRootMarkers;
        }
    }

    public static IReadOnlyList<string> ResolveProjectRootMarkers(IReadOnlyDictionary<string, string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return DefaultProjectRootMarkers;
        }

        foreach (var key in new[] { "project_root_markers", "projectRootMarkers" })
        {
            if (!TryGetValue(values, key, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(rawValue);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                return document.RootElement
                    .EnumerateArray()
                    .Where(static item => item.ValueKind == JsonValueKind.String)
                    .Select(static item => Normalize(item.GetString()))
                    .Where(static marker => !string.IsNullOrWhiteSpace(marker))
                    .Select(static marker => marker!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch
            {
                return DefaultProjectRootMarkers;
            }
        }

        return DefaultProjectRootMarkers;
    }

    public static string? FindProjectRoot(string? cwd, IReadOnlyList<string>? projectRootMarkers = null)
    {
        var normalizedCwd = NormalizeDirectory(cwd);
        if (string.IsNullOrWhiteSpace(normalizedCwd))
        {
            return null;
        }

        var markers = projectRootMarkers ?? ResolveProjectRootMarkers();
        if (markers.Count == 0)
        {
            return normalizedCwd;
        }

        var current = normalizedCwd;
        while (!string.IsNullOrWhiteSpace(current))
        {
            foreach (var marker in markers)
            {
                if (string.IsNullOrWhiteSpace(marker))
                {
                    continue;
                }

                var markerPath = Path.Combine(current!, marker);
                if (File.Exists(markerPath) || Directory.Exists(markerPath))
                {
                    return current;
                }
            }

            var parent = Directory.GetParent(current!)?.FullName;
            if (string.IsNullOrWhiteSpace(parent)
                || string.Equals(parent, current, PathComparison))
            {
                break;
            }

            current = parent;
        }

        return normalizedCwd;
    }

    public static IReadOnlyList<string> EnumerateDirectoriesBetweenProjectRootAndCwd(
        string? cwd,
        IReadOnlyList<string>? projectRootMarkers = null)
    {
        var normalizedCwd = NormalizeDirectory(cwd);
        if (string.IsNullOrWhiteSpace(normalizedCwd))
        {
            return Array.Empty<string>();
        }

        var projectRoot = FindProjectRoot(normalizedCwd, projectRootMarkers);
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return [normalizedCwd!];
        }

        var stack = new Stack<string>();
        var current = normalizedCwd;
        while (!string.IsNullOrWhiteSpace(current))
        {
            stack.Push(current!);
            if (string.Equals(current, projectRoot, PathComparison))
            {
                break;
            }

            var parent = Directory.GetParent(current!)?.FullName;
            if (string.IsNullOrWhiteSpace(parent)
                || string.Equals(parent, current, PathComparison))
            {
                break;
            }

            current = parent;
        }

        var directories = new List<string>(stack.Count);
        while (stack.TryPop(out var directory))
        {
            directories.Add(directory);
        }

        return directories;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool TryGetValue(IReadOnlyDictionary<string, string> values, string key, out string value)
    {
        foreach (var pair in values)
        {
            if (KeyComparer.Equals(pair.Key, key))
            {
                value = pair.Value;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static string? NormalizeDirectory(string? value)
    {
        var normalized = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var fullPath = Path.IsPathRooted(normalized)
            ? Path.GetFullPath(normalized)
            : Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, normalized));
        return File.Exists(fullPath) ? Path.GetDirectoryName(fullPath) : fullPath;
    }
}

/// <summary>
/// 读取 TianShu 原生 TOML 中的 memory provider 配置。
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
        paths.AddRange(EnumerateMemoryProviderModuleFilesFromConfig(TianShuConfigTomlPathResolver.ResolveUserConfigTomlPath()));
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

    private static IReadOnlyList<string> EnumerateMemoryProviderModuleFilesFromConfig(string configPath)
    {
        var directory = TianShuHomePathUtilities.ResolveModulePathFromConfig(configPath, "memory", "providers");
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.toml").Order(StringComparer.OrdinalIgnoreCase).ToArray()
            : Array.Empty<string>();
    }
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
