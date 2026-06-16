using System.Text;
using System.Text.Json;
using TianShu.AppHost.Configuration;

namespace TianShu.AppHost.Tools;

internal sealed record KernelSkillDescriptor(
    string Name,
    string? Description,
    string? ShortDescription,
    KernelSkillInterfaceInfo? Interface,
    KernelSkillDependencies? Dependencies,
    KernelSkillPermissionProfile? PermissionProfile,
    KernelSkillManagedNetworkOverride? ManagedNetworkOverride,
    string PathToSkillsMd,
    string Path,
    string Scope,
    bool Enabled);

internal sealed record KernelSkillScanError(string Path, string Message);

internal sealed record KernelSkillScanResult(
    IReadOnlyList<KernelSkillDescriptor> Skills,
    IReadOnlyList<KernelSkillScanError> Errors);

internal sealed class KernelSkillsManager
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private readonly string homePath;
    private readonly Func<string?, CancellationToken, Task<Dictionary<string, string>>> loadEffectiveConfigOverridesAsync;
    private readonly Func<string?, CancellationToken, Task<Dictionary<string, string>>> loadProjectRootConfigOverridesAsync;
    private readonly Func<string?, CancellationToken, Task<Dictionary<string, string>>> loadWritableConfigOverridesAsync;
    private readonly Func<Dictionary<string, string>, string?, CancellationToken, Task<string>> saveConfigOverridesAsync;
    private readonly object cacheGate = new();
    private readonly Dictionary<string, SkillScanCacheEntry> cache = new(StringComparer.Ordinal);
    private readonly KernelPluginsManager? pluginsManager;
    private readonly string systemConfigRoot;
    private readonly string userHome;

    private sealed record SkillRootContext(string Path, string? Namespace, string Scope);
    private sealed record SkillScanCacheEntry(string Fingerprint, KernelSkillScanResult Result);

    public KernelSkillsManager(
        Func<string?, CancellationToken, Task<Dictionary<string, string>>> loadEffectiveConfigOverridesAsync,
        Func<Dictionary<string, string>, string?, CancellationToken, Task<string>> saveConfigOverridesAsync,
        string? tianShuHome = null,
        KernelPluginsManager? pluginsManager = null,
        Func<string?, CancellationToken, Task<Dictionary<string, string>>>? loadProjectRootConfigOverridesAsync = null,
        Func<string?, CancellationToken, Task<Dictionary<string, string>>>? loadWritableConfigOverridesAsync = null,
        string? userHome = null,
        string? systemConfigRoot = null)
    {
        this.loadEffectiveConfigOverridesAsync = loadEffectiveConfigOverridesAsync;
        this.loadProjectRootConfigOverridesAsync = loadProjectRootConfigOverridesAsync
            ?? ((cwd, cancellationToken) => loadEffectiveConfigOverridesAsync(cwd, cancellationToken));
        this.loadWritableConfigOverridesAsync = loadWritableConfigOverridesAsync
            ?? ((cwd, cancellationToken) => loadEffectiveConfigOverridesAsync(cwd, cancellationToken));
        this.saveConfigOverridesAsync = saveConfigOverridesAsync;
        homePath = NormalizePath(tianShuHome) ?? TianShuHomePathUtilities.ResolveTianShuHomePath();
        this.pluginsManager = pluginsManager;
        this.systemConfigRoot = NormalizePath(systemConfigRoot) ?? TianShuSkillRootPaths.ResolveDefaultSystemConfigRoot();
        this.userHome = NormalizePath(userHome)
                        ?? NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
                        ?? string.Empty;
    }

    public async Task<KernelSkillScanResult> ScanAsync(
        string cwd,
        IReadOnlyList<string> extraRoots,
        bool forceReload,
        CancellationToken cancellationToken)
    {
        var normalizedCwd = NormalizePath(cwd) ?? Environment.CurrentDirectory;
        var normalizedExtraRoots = NormalizeExtraRoots(extraRoots);
        var configValues = await loadEffectiveConfigOverridesAsync(normalizedCwd, cancellationToken).ConfigureAwait(false);
        var cacheFingerprint = BuildCacheFingerprint(normalizedExtraRoots, configValues);

        lock (cacheGate)
        {
            if (!forceReload
                && cache.TryGetValue(normalizedCwd, out var cached)
                && string.Equals(cached.Fingerprint, cacheFingerprint, StringComparison.Ordinal))
            {
                return cached.Result;
            }
        }

        var bundledSkillsEnabled = ResolveBundledSkillsEnabled(configValues);

        var effectiveRoots = await ResolveSkillRootContextsAsync(
                normalizedCwd,
                normalizedExtraRoots,
                bundledSkillsEnabled,
                cancellationToken)
            .ConfigureAwait(false);

        var skills = new List<KernelSkillDescriptor>();
        var errors = new List<KernelSkillScanError>();
        foreach (var root in effectiveRoots)
        {
            if (!Directory.Exists(root.Path))
            {
                continue;
            }

            try
            {
                foreach (var skillFile in Directory.EnumerateFiles(root.Path, "SKILL.md", SearchOption.AllDirectories))
                {
                    if (ContainsHiddenRelativeDirectory(root.Path, skillFile))
                    {
                        continue;
                    }

                    var skillDir = Path.GetDirectoryName(skillFile);
                    if (string.IsNullOrWhiteSpace(skillDir))
                    {
                        continue;
                    }

                    skillDir = Path.GetFullPath(skillDir);
                    var localName = Path.GetFileName(skillDir);
                    var resolvedMetadata = KernelSkillMetadataResolver.LoadForSkillDirectory(skillDir);
                    var baseName = Normalize(resolvedMetadata.Name) ?? localName;
                    var name = string.IsNullOrWhiteSpace(root.Namespace)
                        ? baseName
                        : $"{root.Namespace}:{baseName}";

                    skills.Add(new KernelSkillDescriptor(
                        Name: name,
                        Description: resolvedMetadata.Description ?? string.Empty,
                        ShortDescription: resolvedMetadata.ShortDescription,
                        Interface: resolvedMetadata.Interface,
                        Dependencies: resolvedMetadata.Dependencies,
                        PermissionProfile: resolvedMetadata.PermissionProfile,
                        ManagedNetworkOverride: resolvedMetadata.ManagedNetworkOverride,
                        PathToSkillsMd: resolvedMetadata.PathToSkillsMd,
                        Path: resolvedMetadata.PathToSkillsMd,
                        Scope: root.Scope,
                        Enabled: ResolveSkillEnabled(configValues, skillDir, resolvedMetadata.PathToSkillsMd)));
                }
            }
            catch (Exception ex)
            {
                errors.Add(new KernelSkillScanError(root.Path, ex.Message));
            }
        }

        var result = new KernelSkillScanResult(
            DeduplicateSkillsByPath(skills)
                .OrderBy(static skill => GetScopeSortRank(skill.Scope))
                .ThenBy(static skill => skill.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static skill => skill.Path, PathComparer)
                .ToArray(),
            errors
                .OrderBy(static error => error.Path, PathComparer)
                .ToArray());

        lock (cacheGate)
        {
            cache[normalizedCwd] = new SkillScanCacheEntry(cacheFingerprint, result);
        }

        return result;
    }

    public async Task<IReadOnlyList<string>> ResolveWatchRootsAsync(string cwd, CancellationToken cancellationToken)
    {
        var normalizedCwd = NormalizePath(cwd) ?? Environment.CurrentDirectory;
        var configValues = await loadEffectiveConfigOverridesAsync(normalizedCwd, cancellationToken).ConfigureAwait(false);
        var bundledSkillsEnabled = ResolveBundledSkillsEnabled(configValues);
        var roots = await ResolveSkillRootContextsAsync(
                normalizedCwd,
                Array.Empty<string>(),
                bundledSkillsEnabled,
                cancellationToken)
            .ConfigureAwait(false);
        return roots
            .Select(static root => root.Path)
            .Distinct(PathComparer)
            .ToArray();
    }

    public async Task<bool> WriteEnabledAsync(string skillPath, bool enabled, string? cwd, CancellationToken cancellationToken)
    {
        var normalizedPath = KernelPathUtilities.NormalizeSkillDocumentPath(skillPath, cwd);
        var values = await loadWritableConfigOverridesAsync(cwd, cancellationToken).ConfigureAwait(false);
        values[KernelPersistedSkillConfigUtilities.ToSkillEnabledConfigKey(normalizedPath)] = enabled ? "true" : "false";
        await saveConfigOverridesAsync(values, cwd, cancellationToken).ConfigureAwait(false);
        ClearCache();
        return enabled;
    }

    public void ClearCache()
    {
        lock (cacheGate)
        {
            cache.Clear();
        }
    }

    private static bool ResolveSkillEnabled(IReadOnlyDictionary<string, string> values, string skillDir, string skillDocumentPath)
    {
        foreach (var key in GetSkillEnabledConfigKeys(skillDir, skillDocumentPath))
        {
            if (!values.TryGetValue(key, out var rawValue))
            {
                continue;
            }

            var normalized = NormalizeRawConfigValue(rawValue);
            return !bool.TryParse(normalized, out var enabled) || enabled;
        }

        return true;
    }

    private static string[] NormalizeExtraRoots(IReadOnlyList<string> extraRoots)
        => extraRoots
            .Select(static path => NormalizePath(path))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(PathComparer)
            .OrderBy(static path => path, PathComparer)
            .ToArray()!;

    private async Task<SkillRootContext[]> ResolveSkillRootContextsAsync(
        string normalizedCwd,
        IReadOnlyList<string> normalizedExtraRoots,
        bool bundledSkillsEnabled,
        CancellationToken cancellationToken)
    {
        var projectRootConfigValues = await loadProjectRootConfigOverridesAsync(normalizedCwd, cancellationToken).ConfigureAwait(false);
        var projectRootMarkers = TianShuProjectRootResolver.ResolveProjectRootMarkers(projectRootConfigValues);
        var projectDirectories = TianShuProjectRootResolver
            .EnumerateDirectoriesBetweenProjectRootAndCwd(normalizedCwd, projectRootMarkers);

        var roots = new List<SkillRootContext>();

        foreach (var directory in projectDirectories)
        {
            roots.Add(new(Path.Combine(directory, ".tianshu", "skills"), null, "repo"));
        }

        roots.Add(new(TianShuHomePathUtilities.ResolveModulePathFromHome(homePath, "skills"), null, "user"));

        var homeAgentsSkills = ResolveHomeAgentsSkillsRoot();
        if (!string.IsNullOrWhiteSpace(homeAgentsSkills))
        {
            roots.Add(new(homeAgentsSkills!, null, "user"));
        }

        if (bundledSkillsEnabled)
        {
            roots.Add(new(TianShuSkillRootPaths.ResolveSystemSkillsCacheRoot(homePath), null, "system"));
        }

        roots.Add(new(TianShuSkillRootPaths.ResolveAdminSkillsRoot(systemConfigRoot), null, "admin"));

        roots.AddRange(normalizedExtraRoots.Select(static path => new SkillRootContext(path, null, "user")));
        if (pluginsManager is not null)
        {
            var pluginRoots = await pluginsManager.GetEffectiveSkillRootsAsync(cancellationToken).ConfigureAwait(false);
            roots.AddRange(pluginRoots.Select(static root => new SkillRootContext(root.RootPath, root.Namespace, "user")));
        }

        foreach (var directory in projectDirectories)
        {
            roots.Add(new(Path.Combine(directory, ".agents", "skills"), null, "repo"));
        }

        return DeduplicateSkillRootContexts(
            roots
                .Select(static context => new SkillRootContext(NormalizePath(context.Path)!, context.Namespace, context.Scope))
                .Where(static context => !string.IsNullOrWhiteSpace(context.Path)));
    }

    private static bool ResolveBundledSkillsEnabled(IReadOnlyDictionary<string, string> values)
    {
        foreach (var pair in values)
        {
            if (!string.Equals(pair.Key, "skills.bundled.enabled", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var normalized = NormalizeRawConfigValue(pair.Value);
            return !bool.TryParse(normalized, out var enabled) || enabled;
        }

        return true;
    }

    private static string BuildCacheFingerprint(
        IReadOnlyList<string> normalizedExtraRoots,
        IReadOnlyDictionary<string, string> configValues)
    {
        var builder = new StringBuilder();

        foreach (var root in normalizedExtraRoots)
        {
            builder.Append("root=").Append(root).AppendLine();
        }

        foreach (var pair in configValues.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            builder.Append(pair.Key).Append('=').Append(pair.Value).AppendLine();
        }

        return builder.ToString();
    }

    private static bool ContainsHiddenRelativeDirectory(string rootPath, string skillFilePath)
    {
        var relativePath = Path.GetRelativePath(rootPath, skillFilePath);
        var segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length <= 1)
        {
            return false;
        }

        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (segments[index].StartsWith(".", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> GetSkillEnabledConfigKeys(string skillDir, string skillDocumentPath)
    {
        var keys = new[]
        {
            TryNormalizeSkillConfigPath(skillDocumentPath),
            TryNormalizeSkillConfigPath(skillDir),
        };
        return keys
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Select(static key => KernelPersistedSkillConfigUtilities.ToSkillEnabledConfigKey(key!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeSkillConfigPath(string skillPath)
        => KernelPathUtilities.NormalizeSkillDocumentPath(skillPath);

    private static string? TryNormalizeSkillConfigPath(string? skillPath)
    {
        var normalized = Normalize(skillPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        try
        {
            return NormalizeSkillConfigPath(normalized!);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private string? ResolveHomeAgentsSkillsRoot()
        => string.IsNullOrWhiteSpace(userHome)
            ? null
            : Path.Combine(userHome, ".agents", "skills");

    private static SkillRootContext[] DeduplicateSkillRootContexts(IEnumerable<SkillRootContext> contexts)
    {
        var seen = new HashSet<string>(PathComparer);
        var deduplicated = new List<SkillRootContext>();
        foreach (var context in contexts)
        {
            if (!seen.Add(context.Path))
            {
                continue;
            }

            deduplicated.Add(context);
        }

        return deduplicated.ToArray();
    }

    private static IReadOnlyList<KernelSkillDescriptor> DeduplicateSkillsByPath(IEnumerable<KernelSkillDescriptor> skills)
    {
        var seen = new HashSet<string>(PathComparer);
        var deduplicated = new List<KernelSkillDescriptor>();
        foreach (var skill in skills)
        {
            if (!seen.Add(skill.PathToSkillsMd))
            {
                continue;
            }

            deduplicated.Add(skill);
        }

        return deduplicated;
    }

    private static int GetScopeSortRank(string scope)
        => scope switch
        {
            "repo" => 0,
            "user" => 1,
            "system" => 2,
            "admin" => 3,
            _ => 4,
        };

    private static string? NormalizeRawConfigValue(string rawValue)
    {
        try
        {
            using var json = JsonDocument.Parse(rawValue);
            var root = json.RootElement;
            return root.ValueKind switch
            {
                JsonValueKind.String => Normalize(root.GetString()),
                JsonValueKind.Number => Normalize(root.GetRawText()),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => null,
                _ => null,
            };
        }
        catch
        {
            return Normalize(rawValue.Trim().Trim('"', '\''));
        }
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string? NormalizePath(string? value, string? baseDirectory = null)
    {
        var normalized = Normalize(value);
        if (normalized is null)
        {
            return null;
        }

        if (Path.IsPathRooted(normalized))
        {
            return Path.GetFullPath(normalized);
        }

        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            return Path.GetFullPath(Path.Combine(baseDirectory!, normalized));
        }

        return Path.GetFullPath(normalized);
    }
}
