using System.Text.Json;
using Tomlyn;
using Tomlyn.Model;

namespace TianShu.AppHost.Configuration;

/// <summary>
/// TianShu 项目根与信任决策解析器。
/// Resolves TianShu project roots and project trust decisions.
/// </summary>
public static class TianShuProjectRootResolver
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
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

            var markers = markersArray
                .OfType<string>()
                .Select(Normalize)
                .Where(static marker => !string.IsNullOrWhiteSpace(marker))
                .Select(static marker => marker!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return markers;
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

                var markers = document.RootElement
                    .EnumerateArray()
                    .Where(static item => item.ValueKind == JsonValueKind.String)
                    .Select(static item => Normalize(item.GetString()))
                    .Where(static marker => !string.IsNullOrWhiteSpace(marker))
                    .Select(static marker => marker!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return markers;
            }
            catch
            {
                return DefaultProjectRootMarkers;
            }
        }

        return DefaultProjectRootMarkers;
    }

    public static IReadOnlyList<string> ResolveProjectRootMarkers(IReadOnlyDictionary<string, object?>? config)
    {
        if (config is null || config.Count == 0)
        {
            return DefaultProjectRootMarkers;
        }

        foreach (var key in new[] { "project_root_markers", "projectRootMarkers" })
        {
            if (!TryGetValue(config, key, out var rawValue) || rawValue is null)
            {
                continue;
            }

            var markers = ParseProjectRootMarkers(rawValue);
            if (markers is null)
            {
                continue;
            }

            return markers;
        }

        return DefaultProjectRootMarkers;
    }

    public static IReadOnlyList<string> ResolveProjectRootMarkersStrict(IReadOnlyDictionary<string, object?>? config)
    {
        if (config is null || config.Count == 0)
        {
            return DefaultProjectRootMarkers;
        }

        foreach (var key in new[] { "project_root_markers", "projectRootMarkers" })
        {
            if (!TryGetValue(config, key, out var rawValue))
            {
                continue;
            }

            if (rawValue is null)
            {
                throw CreateInvalidProjectRootMarkersException();
            }

            return ParseProjectRootMarkersStrict(rawValue);
        }

        return DefaultProjectRootMarkers;
    }

    public static string? ResolveProjectConfigDisabledReason(
        string? layerDirectory,
        IReadOnlyDictionary<string, object?>? config,
        IReadOnlyList<string>? projectRootMarkers,
        string userConfigPath)
    {
        var normalizedLayerDirectory = NormalizeDirectory(layerDirectory);
        if (string.IsNullOrWhiteSpace(normalizedLayerDirectory))
        {
            return null;
        }

        var trustLevels = ResolveProjectTrustLevels(config);
        var decision = ResolveTrustDecision(normalizedLayerDirectory, trustLevels, projectRootMarkers);
        if (decision.TrustLevel != KernelProjectTrustLevel.Untrusted)
        {
            return null;
        }

        return $"{decision.TrustKey} is marked as untrusted in {userConfigPath}. To load tianshu.toml, mark it trusted.";
    }

    public static bool IsProjectExplicitlyUntrusted(
        string? directory,
        IReadOnlyDictionary<string, object?>? config,
        IReadOnlyList<string>? projectRootMarkers)
    {
        var normalizedDirectory = NormalizeDirectory(directory);
        if (string.IsNullOrWhiteSpace(normalizedDirectory))
        {
            return false;
        }

        var trustLevels = ResolveProjectTrustLevels(config);
        var decision = ResolveTrustDecision(normalizedDirectory, trustLevels, projectRootMarkers);
        return decision.TrustLevel == KernelProjectTrustLevel.Untrusted;
    }

    public static string? FindGitRoot(string? cwd)
    {
        var normalizedCwd = NormalizeDirectory(cwd);
        if (string.IsNullOrWhiteSpace(normalizedCwd))
        {
            return null;
        }

        var current = normalizedCwd;
        while (!string.IsNullOrWhiteSpace(current))
        {
            var markerPath = Path.Combine(current!, ".git");
            if (File.Exists(markerPath) || Directory.Exists(markerPath))
            {
                return current;
            }

            var parent = Directory.GetParent(current!)?.FullName;
            if (string.IsNullOrWhiteSpace(parent)
                || string.Equals(parent, current, PathComparison))
            {
                break;
            }

            current = parent;
        }

        return null;
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

    private static bool TryGetValue(IReadOnlyDictionary<string, object?> values, string key, out object? value)
    {
        foreach (var pair in values)
        {
            if (KeyComparer.Equals(pair.Key, key))
            {
                value = pair.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static string? NormalizeConfiguredPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return NormalizeDirectory(path) ?? path.Trim();
        }
        catch
        {
            return path.Trim();
        }
    }

    private static IReadOnlyList<string>? ParseProjectRootMarkers(object rawValue)
    {
        if (rawValue is List<object?> list)
        {
            return list
                .OfType<string>()
                .Select(Normalize)
                .Where(static marker => !string.IsNullOrWhiteSpace(marker))
                .Select(static marker => marker!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (rawValue is string rawString)
        {
            try
            {
                using var document = JsonDocument.Parse(rawString);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return null;
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
                return null;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ParseProjectRootMarkersStrict(object rawValue)
    {
        if (rawValue is List<object?> list)
        {
            if (list.Count == 0)
            {
                return Array.Empty<string>();
            }

            var markers = new List<string>(list.Count);
            foreach (var entry in list)
            {
                if (entry is not string text)
                {
                    throw CreateInvalidProjectRootMarkersException();
                }

                var normalized = Normalize(text);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    markers.Add(normalized!);
                }
            }

            return markers
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (rawValue is string rawString)
        {
            try
            {
                using var document = JsonDocument.Parse(rawString);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    throw CreateInvalidProjectRootMarkersException();
                }

                var markers = new List<string>();
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        throw CreateInvalidProjectRootMarkersException();
                    }

                    var normalized = Normalize(item.GetString());
                    if (!string.IsNullOrWhiteSpace(normalized))
                    {
                        markers.Add(normalized!);
                    }
                }

                return markers
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (JsonException ex)
            {
                throw new FormatException(CreateInvalidProjectRootMarkersException().Message, ex);
            }
        }

        throw CreateInvalidProjectRootMarkersException();
    }

    private static FormatException CreateInvalidProjectRootMarkersException()
        => new("project_root_markers must be an array of strings");

    private static Dictionary<string, KernelProjectTrustLevel> ResolveProjectTrustLevels(IReadOnlyDictionary<string, object?>? config)
    {
        var levels = new Dictionary<string, KernelProjectTrustLevel>(PathComparer);
        if (config is null
            || !TryGetValue(config, "projects", out var projectsValue)
            || projectsValue is not Dictionary<string, object?> projectsTable)
        {
            return levels;
        }

        foreach (var project in projectsTable)
        {
            if (project.Value is not Dictionary<string, object?> projectConfig)
            {
                continue;
            }

            if (!TryGetValue(projectConfig, "trust_level", out var trustLevelValue)
                && !TryGetValue(projectConfig, "trustLevel", out trustLevelValue))
            {
                continue;
            }

            var trustLevel = ParseTrustLevel(trustLevelValue as string);
            if (trustLevel == KernelProjectTrustLevel.Unknown)
            {
                continue;
            }

            var normalizedPath = NormalizeConfiguredPath(project.Key);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                continue;
            }

            levels[normalizedPath] = trustLevel;
        }

        return levels;
    }

    private static KernelProjectTrustDecision ResolveTrustDecision(
        string directory,
        IReadOnlyDictionary<string, KernelProjectTrustLevel> trustLevels,
        IReadOnlyList<string>? projectRootMarkers)
    {
        if (trustLevels.TryGetValue(directory, out var directTrustLevel))
        {
            return new KernelProjectTrustDecision(directTrustLevel, directory);
        }

        var projectRoot = FindProjectRoot(directory, projectRootMarkers) ?? directory;
        if (trustLevels.TryGetValue(projectRoot, out var projectRootTrustLevel))
        {
            return new KernelProjectTrustDecision(projectRootTrustLevel, projectRoot);
        }

        var gitRoot = FindGitRoot(directory);
        if (!string.IsNullOrWhiteSpace(gitRoot)
            && trustLevels.TryGetValue(gitRoot, out var gitRootTrustLevel))
        {
            return new KernelProjectTrustDecision(gitRootTrustLevel, gitRoot);
        }

        return new KernelProjectTrustDecision(
            KernelProjectTrustLevel.Unknown,
            gitRoot ?? projectRoot);
    }

    private static KernelProjectTrustLevel ParseTrustLevel(string? value)
    {
        var normalized = Normalize(value);
        return normalized?.ToLowerInvariant() switch
        {
            "trusted" => KernelProjectTrustLevel.Trusted,
            "untrusted" => KernelProjectTrustLevel.Untrusted,
            _ => KernelProjectTrustLevel.Unknown,
        };
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
        if (File.Exists(fullPath))
        {
            return Path.GetDirectoryName(fullPath);
        }

        return fullPath;
    }

    private enum KernelProjectTrustLevel
    {
        Unknown = 0,
        Trusted = 1,
        Untrusted = 2,
    }

    private readonly record struct KernelProjectTrustDecision(KernelProjectTrustLevel TrustLevel, string TrustKey);
}
