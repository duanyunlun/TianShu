using System.Text;
using Tomlyn;
using Tomlyn.Model;

namespace TianShu.Configuration;

/// <summary>
/// 读取、扫描并写回 TianShu Workspace / Project Resolver 包 manifest。
/// Loads, scans, and updates TianShu Workspace / Project Resolver package manifests.
/// </summary>
public sealed class TianShuWorkspaceResolverManifestConfiguration
{
    public const string WorkspaceResolverDirectoryName = "modules/workspace/resolvers";
    public const string LegacyWorkspaceResolverDirectoryName = "workspace-resolvers";
    public const string ManifestFileName = "resolver.toml";

    public WorkspaceResolverManifestProjection Load(string rootDirectory, string? selectedManifestPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        var root = Path.GetFullPath(rootDirectory);
        var files = ScanWorkspaceResolverManifests(root);
        var selectedPath = ResolveSelectedManifest(root, selectedManifestPath, files);
        var issues = new List<WorkspaceResolverManifestIssue>();
        WorkspaceResolverPackageManifestValue? package = null;

        if (selectedPath is not null)
        {
            try
            {
                package = ReadPackage(selectedPath);
                AddCompatibilityIssue(package, selectedPath, issues);
            }
            catch (Exception ex)
            {
                issues.Add(new WorkspaceResolverManifestIssue(
                    "workspace_resolver_manifest.parse_failed",
                    $"无法解析 Workspace Resolver 包 manifest：{ex.Message}",
                    selectedPath));
            }
        }

        return new WorkspaceResolverManifestProjection(
            root,
            ResolveWorkspaceResolverRootDirectory(root),
            selectedPath,
            files.Select(file => file with { IsSelected = string.Equals(file.Path, selectedPath, StringComparison.OrdinalIgnoreCase) }).ToArray(),
            package,
            issues);
    }

    public IReadOnlyList<WorkspaceResolverPackageManifestValue> LoadEnabledPackages(string rootDirectory)
    {
        var projection = Load(rootDirectory);
        var packages = new List<WorkspaceResolverPackageManifestValue>();
        foreach (var file in projection.Files)
        {
            try
            {
                var package = ReadPackage(file.Path);
                if (package.Enabled && TianShuExtensionManifestCommon.ApplyCompatibility(package))
                {
                    packages.Add(package);
                }
            }
            catch
            {
                // 投影层会暴露解析问题；运行时加载保守跳过损坏包。
                // Projection exposes parse issues; runtime loading conservatively skips broken packages.
            }
        }

        return packages
            .OrderBy(static package => package.Priority)
            .ThenBy(static package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string CreatePackage(string rootDirectory, string packageId, bool overwrite = false)
    {
        var manifestPath = ResolveWritableManifestPath(rootDirectory, packageId);
        if (File.Exists(manifestPath) && !overwrite)
        {
            throw new InvalidOperationException($"Workspace Resolver 包 manifest 已存在：{manifestPath}");
        }

        var package = new WorkspaceResolverPackageManifestValue
        {
            Id = NormalizePackageId(packageId),
            DisplayName = NormalizePackageId(packageId),
            Enabled = true,
            Type = "package",
            Priority = 0,
            ManifestPath = manifestPath,
            PackageDirectory = Path.GetDirectoryName(manifestPath)!,
            Resolvers = [],
        };

        SavePackage(manifestPath, package);
        return manifestPath;
    }

    public string CopyPackage(string rootDirectory, string sourceManifestPath, string packageId, bool overwrite = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceManifestPath);
        var sourcePath = EnsureManifestInsideWorkspaceResolverRoot(rootDirectory, sourceManifestPath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("源 Workspace Resolver 包 manifest 不存在。", sourcePath);
        }

        var targetPath = ResolveWritableManifestPath(rootDirectory, packageId);
        if (File.Exists(targetPath) && !overwrite)
        {
            throw new InvalidOperationException($"Workspace Resolver 包 manifest 已存在：{targetPath}");
        }

        var package = ReadPackage(sourcePath);
        package.Id = NormalizePackageId(packageId);
        package.DisplayName = string.IsNullOrWhiteSpace(package.DisplayName) ? package.Id : package.DisplayName;
        package.ManifestPath = targetPath;
        package.PackageDirectory = Path.GetDirectoryName(targetPath)!;
        SavePackage(targetPath, package);
        return targetPath;
    }

    public void DeletePackage(string rootDirectory, string manifestPath)
    {
        var fullPath = EnsureManifestInsideWorkspaceResolverRoot(rootDirectory, manifestPath);
        var packageDirectory = Path.GetDirectoryName(fullPath);
        if (string.Equals(Path.GetFileName(packageDirectory), "builtin", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("官方 builtin Workspace Resolver 包不允许删除；可通过启用状态禁用或复制后替换。");
        }

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    public void SavePackage(string manifestPath, WorkspaceResolverPackageManifestValue package)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ArgumentNullException.ThrowIfNull(package);

        var fullPath = Path.GetFullPath(manifestPath);
        var root = File.Exists(fullPath) ? ReadTomlTable(fullPath) : new TomlTable();

        root["id"] = NormalizePackageId(package.Id);
        SetOptional(root, "display_name", package.DisplayName);
        root["enabled"] = package.Enabled;
        root["type"] = NormalizeScalar(package.Type, "package");
        root["priority"] = package.Priority;
        TianShuExtensionManifestCommon.WriteMetadata(root, package);

        var resolvers = new TomlTableArray();
        foreach (var resolver in package.Resolvers.Where(static resolver => !string.IsNullOrWhiteSpace(resolver.Id)))
        {
            var resolverTable = new TomlTable
            {
                ["id"] = NormalizeResolverId(resolver.Id),
                ["enabled"] = resolver.Enabled,
                ["type"] = NormalizeScalar(resolver.Type, "marker"),
                ["priority"] = resolver.Priority,
            };
            SetOptional(resolverTable, "display_name", resolver.DisplayName);
            SetOptional(resolverTable, "profile", resolver.Profile);
            SetOptional(resolverTable, "trust_policy", resolver.TrustPolicy);
            SetOptional(resolverTable, "artifact_root", resolver.ArtifactRoot);
            SetOptional(resolverTable, "state_root", resolver.StateRoot);
            SetOptional(resolverTable, "assembly_path", resolver.AssemblyPath);
            SetOptional(resolverTable, "provider_type", resolver.ProviderType);
            SetStringArray(resolverTable, "root_markers", resolver.RootMarkers);
            SetStringArray(resolverTable, "ignore_globs", resolver.IgnoreGlobs);
            SetStringArray(resolverTable, "language_markers", resolver.LanguageMarkers);
            SetStringArray(resolverTable, "framework_markers", resolver.FrameworkMarkers);
            resolvers.Add(resolverTable);
        }

        if (resolvers.Count > 0)
        {
            root["resolvers"] = resolvers;
        }
        else
        {
            root.Remove("resolvers");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, Toml.FromModel(root).TrimEnd() + Environment.NewLine, Encoding.UTF8);
    }

    public static string ResolveRootDirectory(string TianShuConfigPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(TianShuConfigPath);
        return Path.GetDirectoryName(Path.GetFullPath(TianShuConfigPath)) ?? Environment.CurrentDirectory;
    }

    public static string ResolveWorkspaceResolverRootDirectory(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        return TianShuHomePathUtilities.ResolveModulePathFromHome(rootDirectory, "workspace", "resolvers");
    }

    public static string ResolveArtifactRootFullPath(WorkspaceResolverPackageManifestValue package, WorkspaceResolverManifestValue resolver)
    {
        var path = string.IsNullOrWhiteSpace(resolver.ArtifactRoot)
            ? ".tianshu/artifacts"
            : resolver.ArtifactRoot;
        return ResolvePackageRelativePath(package, path!);
    }

    public static string ResolveStateRootFullPath(WorkspaceResolverPackageManifestValue package, WorkspaceResolverManifestValue resolver)
    {
        var path = string.IsNullOrWhiteSpace(resolver.StateRoot)
            ? ".tianshu/state"
            : resolver.StateRoot;
        return ResolvePackageRelativePath(package, path!);
    }

    public static string ResolveAssemblyFullPath(WorkspaceResolverPackageManifestValue package, WorkspaceResolverManifestValue resolver)
    {
        if (string.IsNullOrWhiteSpace(resolver.AssemblyPath))
        {
            return string.Empty;
        }

        return ResolvePackageRelativePath(package, resolver.AssemblyPath!);
    }

    public static IReadOnlyList<string> ResolveEffectiveRootMarkers(
        string rootDirectory,
        IReadOnlyList<string>? configuredMarkers)
        => ResolveEffectivePolicy(rootDirectory, configuredMarkers).RootMarkers;

    /// <summary>
    /// 合并启用的 Workspace Resolver 包为运行时可消费的策略快照。
    /// Merges enabled workspace resolver packages into a runtime-consumable policy snapshot.
    /// </summary>
    public static WorkspaceResolverEffectivePolicy ResolveEffectivePolicy(
        string rootDirectory,
        IReadOnlyList<string>? configuredMarkers)
    {
        var markers = new List<string>();
        if (configuredMarkers is not null)
        {
            markers.AddRange(configuredMarkers.Where(static marker => !string.IsNullOrWhiteSpace(marker)));
        }

        var ignoreGlobs = new List<string>();
        var languageMarkers = new List<string>();
        var frameworkMarkers = new List<string>();
        var matchedResolvers = new List<WorkspaceResolverEffectiveResolver>();
        string? defaultProfile = null;
        string? trustPolicy = null;
        string? artifactRoot = null;
        string? stateRoot = null;

        try
        {
            var configuration = new TianShuWorkspaceResolverManifestConfiguration();
            foreach (var package in configuration.LoadEnabledPackages(rootDirectory))
            {
                foreach (var resolver in package.Resolvers
                             .Where(static resolver => resolver.Enabled)
                             .OrderBy(static resolver => resolver.Priority)
                             .ThenBy(static resolver => resolver.Id, StringComparer.OrdinalIgnoreCase))
                {
                    markers.AddRange(resolver.RootMarkers.Where(static marker => !string.IsNullOrWhiteSpace(marker)));
                    ignoreGlobs.AddRange(resolver.IgnoreGlobs.Where(static value => !string.IsNullOrWhiteSpace(value)));
                    languageMarkers.AddRange(resolver.LanguageMarkers.Where(static value => !string.IsNullOrWhiteSpace(value)));
                    frameworkMarkers.AddRange(resolver.FrameworkMarkers.Where(static value => !string.IsNullOrWhiteSpace(value)));

                    defaultProfile ??= NormalizeOptional(resolver.Profile);
                    trustPolicy ??= NormalizeOptional(resolver.TrustPolicy);
                    artifactRoot ??= ResolveOptionalPackagePath(package, resolver.ArtifactRoot);
                    stateRoot ??= ResolveOptionalPackagePath(package, resolver.StateRoot);
                    matchedResolvers.Add(new WorkspaceResolverEffectiveResolver(
                        package.Id,
                        resolver.Id,
                        resolver.Type,
                        package.Priority,
                        resolver.Priority));
                }
            }
        }
        catch
        {
            // Resolver manifest 是可替换策略输入，损坏时不应阻断基础配置读取。
            // Resolver manifests are replaceable policy inputs; broken files must not block baseline config loading.
        }

        return new WorkspaceResolverEffectivePolicy(
            NormalizeDistinct(markers),
            defaultProfile,
            trustPolicy,
            artifactRoot,
            stateRoot,
            NormalizeDistinct(ignoreGlobs),
            NormalizeDistinct(languageMarkers),
            NormalizeDistinct(frameworkMarkers),
            matchedResolvers);
    }

    private static string ResolvePackageRelativePath(WorkspaceResolverPackageManifestValue package, string path)
        => Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(package.PackageDirectory, path));

    private static string? ResolveOptionalPackagePath(WorkspaceResolverPackageManifestValue package, string? path)
        => string.IsNullOrWhiteSpace(path) ? null : ResolvePackageRelativePath(package, path.Trim());

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> NormalizeDistinct(IEnumerable<string> values)
        => values
            .Select(static value => value.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<WorkspaceResolverManifestFileDescriptor> ScanWorkspaceResolverManifests(string rootDirectory)
    {
        return EnumerateWorkspaceResolverRoots(rootDirectory)
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateDirectories(root)
                .Select(directory => Path.Combine(directory, ManifestFileName))
                .Where(File.Exists))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new WorkspaceResolverManifestFileDescriptor(
                Path.GetFullPath(path),
                GetDisplayName(rootDirectory, path)))
            .OrderBy(static file => file.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ResolveSelectedManifest(
        string rootDirectory,
        string? selectedManifestPath,
        IReadOnlyList<WorkspaceResolverManifestFileDescriptor> files)
    {
        if (files.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(selectedManifestPath))
        {
            var normalized = Path.GetFullPath(Path.IsPathFullyQualified(selectedManifestPath)
                ? selectedManifestPath
                : Path.Combine(rootDirectory, selectedManifestPath));
            var selected = files.FirstOrDefault(file => string.Equals(file.Path, normalized, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                return selected.Path;
            }
        }

        return files.FirstOrDefault(static file => file.DisplayName.Contains("resolvers", StringComparison.OrdinalIgnoreCase)
                                                  && file.DisplayName.Contains("builtin", StringComparison.OrdinalIgnoreCase))?.Path
               ?? files.First().Path;
    }

    private static WorkspaceResolverPackageManifestValue ReadPackage(string manifestPath)
    {
        var fullPath = Path.GetFullPath(manifestPath);
        var packageDirectory = Path.GetDirectoryName(fullPath)!;
        var root = ReadTomlTable(fullPath);
        var packageId = ReadString(root, "id") ?? Path.GetFileName(packageDirectory);
        var package = new WorkspaceResolverPackageManifestValue
        {
            Id = packageId,
            DisplayName = ReadString(root, "display_name") ?? packageId,
            Enabled = ReadBoolean(root, "enabled") ?? true,
            Type = ReadString(root, "type") ?? "package",
            Priority = ReadInteger(root, "priority") ?? 0,
            ManifestPath = fullPath,
            PackageDirectory = packageDirectory,
        };
        TianShuExtensionManifestCommon.ReadMetadata(root, package);

        if (root.TryGetValue("resolvers", out var resolversValue) && resolversValue is TomlTableArray resolverArray)
        {
            package.Resolvers = resolverArray
                .OfType<TomlTable>()
                .Select(ReadResolver)
                .Where(static resolver => !string.IsNullOrWhiteSpace(resolver.Id))
                .ToArray();
        }

        return package;
    }

    private static void AddCompatibilityIssue(
        WorkspaceResolverPackageManifestValue package,
        string manifestPath,
        ICollection<WorkspaceResolverManifestIssue> issues)
    {
        if (package.Enabled && !TianShuExtensionManifestCommon.ApplyCompatibility(package))
        {
            issues.Add(new WorkspaceResolverManifestIssue(
                $"workspace_resolver_manifest.{TianShuExtensionManifestCommon.VersionIncompatibleIssueCode}",
                $"Workspace Resolver 包不可用：{package.UnavailableReason}",
                manifestPath));
        }
    }

    private static WorkspaceResolverManifestValue ReadResolver(TomlTable table)
        => new()
        {
            Id = ReadString(table, "id") ?? string.Empty,
            DisplayName = ReadString(table, "display_name") ?? string.Empty,
            Enabled = ReadBoolean(table, "enabled") ?? true,
            Type = ReadString(table, "type") ?? "marker",
            Priority = ReadInteger(table, "priority") ?? 0,
            RootMarkers = ReadStringArray(table, "root_markers"),
            Profile = ReadString(table, "profile"),
            TrustPolicy = ReadString(table, "trust_policy"),
            ArtifactRoot = ReadString(table, "artifact_root"),
            StateRoot = ReadString(table, "state_root"),
            IgnoreGlobs = ReadStringArray(table, "ignore_globs"),
            LanguageMarkers = ReadStringArray(table, "language_markers"),
            FrameworkMarkers = ReadStringArray(table, "framework_markers"),
            AssemblyPath = ReadString(table, "assembly_path"),
            ProviderType = ReadString(table, "provider_type") ?? ReadString(table, "type_name"),
        };

    private static TomlTable ReadTomlTable(string path)
        => TomlTable.From(Toml.Parse(File.ReadAllText(path, Encoding.UTF8), path));

    private static string ResolveWritableManifestPath(string rootDirectory, string packageId)
    {
        var normalizedId = NormalizePackageId(packageId);
        var targetRoot = ResolveWorkspaceResolverRootDirectory(rootDirectory);
        return Path.Combine(targetRoot, normalizedId, ManifestFileName);
    }

    private static string EnsureManifestInsideWorkspaceResolverRoot(string rootDirectory, string manifestPath)
    {
        var root = Path.GetFullPath(rootDirectory);
        var fullPath = Path.GetFullPath(Path.IsPathFullyQualified(manifestPath) ? manifestPath : Path.Combine(root, manifestPath));
        var workspaceResolverRoot = ResolveWorkspaceResolverRootDirectory(root);
        if (IsUnderDirectory(fullPath, workspaceResolverRoot))
        {
            return fullPath;
        }

        throw new InvalidOperationException("Workspace Resolver 包 manifest 只能位于 TianShu modules/workspace/resolvers 目录内。");
    }

    private static IEnumerable<string> EnumerateWorkspaceResolverRoots(string rootDirectory)
    {
        yield return ResolveWorkspaceResolverRootDirectory(rootDirectory);
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePackageId(string id)
    {
        var trimmed = id.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)
            || trimmed.Any(static ch => !(char.IsLetterOrDigit(ch) || ch is '_' or '-')))
        {
            throw new InvalidOperationException("Workspace Resolver 包 id 只能包含字母、数字、下划线或短横线。");
        }

        return trimmed;
    }

    private static string NormalizeResolverId(string id)
    {
        var trimmed = id.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)
            || trimmed.Any(static ch => !(char.IsLetterOrDigit(ch) || ch is '_' or '-')))
        {
            throw new InvalidOperationException("Workspace Resolver id 只能包含字母、数字、下划线或短横线。");
        }

        return trimmed;
    }

    private static string NormalizeScalar(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static void SetOptional(TomlTable table, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            table.Remove(key);
            return;
        }

        table[key] = value.Trim();
    }

    private static void SetStringArray(TomlTable table, string key, IReadOnlyList<string> values)
    {
        var normalized = values
            .Select(static value => value.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalized.Length == 0)
        {
            table.Remove(key);
            return;
        }

        var array = new TomlArray();
        foreach (var value in normalized)
        {
            array.Add(value);
        }

        table[key] = array;
    }

    private static string? ReadString(TomlTable table, string key)
        => table.TryGetValue(key, out var value) ? value as string : null;

    private static bool? ReadBoolean(TomlTable table, string key)
        => table.TryGetValue(key, out var value) ? value as bool? : null;

    private static int? ReadInteger(TomlTable table, string key)
        => table.TryGetValue(key, out var value) ? Convert.ToInt32(value) : null;

    private static IReadOnlyList<string> ReadStringArray(TomlTable table, string key)
    {
        if (!table.TryGetValue(key, out var value) || value is not TomlArray array)
        {
            return [];
        }

        return array
            .OfType<string>()
            .Select(static item => item.Trim())
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
    }

    private static string GetDisplayName(string rootDirectory, string path)
    {
        var relative = Path.GetRelativePath(rootDirectory, path);
        return relative.StartsWith("..", StringComparison.Ordinal) ? path : relative;
    }
}

public sealed record WorkspaceResolverManifestProjection(
    string RootDirectory,
    string WorkspaceResolverRootDirectory,
    string? SelectedManifestPath,
    IReadOnlyList<WorkspaceResolverManifestFileDescriptor> Files,
    WorkspaceResolverPackageManifestValue? SelectedPackage,
    IReadOnlyList<WorkspaceResolverManifestIssue> Issues);

public sealed record WorkspaceResolverManifestFileDescriptor(
    string Path,
    string DisplayName)
{
    public bool IsSelected { get; init; }
}

public sealed record WorkspaceResolverManifestIssue(string Code, string Message, string? Path);

/// <summary>
/// 已合并的 Workspace Resolver 运行时策略快照。
/// Merged runtime policy snapshot for workspace resolvers.
/// </summary>
public sealed record WorkspaceResolverEffectivePolicy(
    IReadOnlyList<string> RootMarkers,
    string? DefaultProfile,
    string? TrustPolicy,
    string? ArtifactRoot,
    string? StateRoot,
    IReadOnlyList<string> IgnoreGlobs,
    IReadOnlyList<string> LanguageMarkers,
    IReadOnlyList<string> FrameworkMarkers,
    IReadOnlyList<WorkspaceResolverEffectiveResolver> Resolvers)
{
    public static WorkspaceResolverEffectivePolicy Empty { get; } = new(
        [],
        DefaultProfile: null,
        TrustPolicy: null,
        ArtifactRoot: null,
        StateRoot: null,
        [],
        [],
        [],
        []);
}

/// <summary>
/// 参与策略合并的 Workspace Resolver 来源摘要。
/// Source summary for a workspace resolver that participated in policy merging.
/// </summary>
public sealed record WorkspaceResolverEffectiveResolver(
    string PackageId,
    string ResolverId,
    string Type,
    int PackagePriority,
    int ResolverPriority);

public sealed class WorkspaceResolverPackageManifestValue : ITianShuExtensionManifestMetadata
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string Type { get; set; } = "package";

    public int Priority { get; set; }

    public string ManifestPath { get; set; } = string.Empty;

    public string PackageDirectory { get; set; } = string.Empty;

    public string? Version { get; set; }

    public string? MinTianShuVersion { get; set; }

    public IReadOnlyList<string> Capabilities { get; set; } = [];

    public IReadOnlyList<string> Diagnostics { get; set; } = [];

    public string LoadStatus { get; set; } = TianShuExtensionManifestCommon.LoadStatusAvailable;

    public string? UnavailableReason { get; set; }

    public IReadOnlyList<WorkspaceResolverManifestValue> Resolvers { get; set; } = [];
}

public sealed class WorkspaceResolverManifestValue
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string Type { get; set; } = "marker";

    public int Priority { get; set; }

    public IReadOnlyList<string> RootMarkers { get; set; } = [];

    public string? Profile { get; set; }

    public string? TrustPolicy { get; set; }

    public string? ArtifactRoot { get; set; }

    public string? StateRoot { get; set; }

    public IReadOnlyList<string> IgnoreGlobs { get; set; } = [];

    public IReadOnlyList<string> LanguageMarkers { get; set; } = [];

    public IReadOnlyList<string> FrameworkMarkers { get; set; } = [];

    public string? AssemblyPath { get; set; }

    public string? ProviderType { get; set; }
}
