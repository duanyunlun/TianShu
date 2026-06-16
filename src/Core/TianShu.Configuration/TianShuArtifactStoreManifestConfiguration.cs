using System.Text;
using Tomlyn;
using Tomlyn.Model;

namespace TianShu.Configuration;

/// <summary>
/// 读取、扫描并写回 TianShu Artifact / Runtime Store 包 manifest。
/// Loads, scans, and updates TianShu Artifact / Runtime Store package manifests.
/// </summary>
public sealed class TianShuArtifactStoreManifestConfiguration
{
    public const string ArtifactStoreDirectoryName = "modules/artifacts/stores";
    public const string LegacyArtifactStoreDirectoryName = "artifact-stores";
    public const string ManifestFileName = "store.toml";

    public ArtifactStoreManifestProjection Load(string rootDirectory, string? selectedManifestPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        var root = Path.GetFullPath(rootDirectory);
        var files = ScanArtifactStoreManifests(root);
        var selectedPath = ResolveSelectedManifest(root, selectedManifestPath, files);
        var issues = new List<ArtifactStoreManifestIssue>();
        ArtifactStorePackageManifestValue? package = null;

        if (selectedPath is not null)
        {
            try
            {
                package = ReadPackage(selectedPath);
                AddCompatibilityIssue(package, selectedPath, issues);
            }
            catch (Exception ex)
            {
                issues.Add(new ArtifactStoreManifestIssue(
                    "artifact_store_manifest.parse_failed",
                    $"无法解析工件存储包 manifest：{ex.Message}",
                    selectedPath));
            }
        }

        return new ArtifactStoreManifestProjection(
            root,
            ResolveArtifactStoreRootDirectory(root),
            selectedPath,
            files.Select(file => file with { IsSelected = string.Equals(file.Path, selectedPath, StringComparison.OrdinalIgnoreCase) }).ToArray(),
            package,
            issues);
    }

    public string CreatePackage(string rootDirectory, string packageId, bool overwrite = false)
    {
        var manifestPath = ResolveWritableManifestPath(rootDirectory, packageId);
        if (File.Exists(manifestPath) && !overwrite)
        {
            throw new InvalidOperationException($"工件存储包 manifest 已存在：{manifestPath}");
        }

        var package = new ArtifactStorePackageManifestValue
        {
            Id = NormalizePackageId(packageId),
            DisplayName = NormalizePackageId(packageId),
            Enabled = true,
            Type = "package",
            Priority = 0,
            ManifestPath = manifestPath,
            PackageDirectory = Path.GetDirectoryName(manifestPath)!,
            Stores = [],
        };

        SavePackage(manifestPath, package);
        return manifestPath;
    }

    public string CopyPackage(string rootDirectory, string sourceManifestPath, string packageId, bool overwrite = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceManifestPath);
        var sourcePath = EnsureManifestInsideArtifactStoreRoot(rootDirectory, sourceManifestPath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("源工件存储包 manifest 不存在。", sourcePath);
        }

        var targetPath = ResolveWritableManifestPath(rootDirectory, packageId);
        if (File.Exists(targetPath) && !overwrite)
        {
            throw new InvalidOperationException($"工件存储包 manifest 已存在：{targetPath}");
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
        var fullPath = EnsureManifestInsideArtifactStoreRoot(rootDirectory, manifestPath);
        var packageDirectory = Path.GetDirectoryName(fullPath);
        if (string.Equals(Path.GetFileName(packageDirectory), "builtin", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("官方 builtin 工件存储包不允许删除；可通过启用状态禁用或复制后替换。");
        }

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    public void SavePackage(string manifestPath, ArtifactStorePackageManifestValue package)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ArgumentNullException.ThrowIfNull(package);

        var fullPath = Path.GetFullPath(manifestPath);
        var root = File.Exists(fullPath) ? ReadTomlTable(fullPath) : new TomlTable();

        root["id"] = NormalizePackageId(package.Id);
        if (string.IsNullOrWhiteSpace(package.DisplayName))
        {
            root.Remove("display_name");
        }
        else
        {
            root["display_name"] = package.DisplayName.Trim();
        }

        root["enabled"] = package.Enabled;
        root["type"] = NormalizeScalar(package.Type, "package");
        root["priority"] = package.Priority;
        TianShuExtensionManifestCommon.WriteMetadata(root, package);

        var stores = new TomlTableArray();
        foreach (var store in package.Stores.Where(static store => !string.IsNullOrWhiteSpace(store.Id)))
        {
            var storeTable = new TomlTable
            {
                ["id"] = NormalizeStoreId(store.Id),
                ["enabled"] = store.Enabled,
                ["type"] = NormalizeScalar(store.Type, "filesystem"),
                ["priority"] = store.Priority,
            };
            SetOptional(storeTable, "display_name", store.DisplayName);
            SetOptional(storeTable, "root", store.Root);
            SetOptional(storeTable, "assembly_path", store.AssemblyPath);
            SetOptional(storeTable, "provider_type", store.ProviderType);
            if (store.MaxHistoryVersions is not null)
            {
                storeTable["max_history_versions"] = store.MaxHistoryVersions.Value;
            }
            else
            {
                storeTable.Remove("max_history_versions");
            }

            storeTable["enable_cross_process_sync"] = store.EnableCrossProcessSync;
            stores.Add(storeTable);
        }

        if (stores.Count > 0)
        {
            root["stores"] = stores;
        }
        else
        {
            root.Remove("stores");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, Toml.FromModel(root).TrimEnd() + Environment.NewLine, Encoding.UTF8);
    }

    public static string ResolveRootDirectory(string TianShuConfigPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(TianShuConfigPath);
        return Path.GetDirectoryName(Path.GetFullPath(TianShuConfigPath)) ?? Environment.CurrentDirectory;
    }

    public static string ResolveArtifactStoreRootDirectory(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        return TianShuHomePathUtilities.ResolveModulePathFromHome(rootDirectory, "artifacts", "stores");
    }

    public static string ResolveStoreRootFullPath(ArtifactStorePackageManifestValue package, ArtifactStoreManifestValue store)
    {
        var path = store.Root;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = "./data";
        }

        return Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(package.PackageDirectory, path));
    }

    public static string ResolveStoreAssemblyFullPath(ArtifactStorePackageManifestValue package, ArtifactStoreManifestValue store)
    {
        var path = store.AssemblyPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(package.PackageDirectory, path));
    }

    private static IReadOnlyList<ArtifactStoreManifestFileDescriptor> ScanArtifactStoreManifests(string rootDirectory)
    {
        return EnumerateArtifactStoreRoots(rootDirectory)
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateDirectories(root)
                .Select(directory => Path.Combine(directory, ManifestFileName))
                .Where(File.Exists))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new ArtifactStoreManifestFileDescriptor(
                Path.GetFullPath(path),
                GetDisplayName(rootDirectory, path)))
            .OrderBy(static file => file.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ResolveSelectedManifest(
        string rootDirectory,
        string? selectedManifestPath,
        IReadOnlyList<ArtifactStoreManifestFileDescriptor> files)
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

        return files.FirstOrDefault(static file => file.DisplayName.Contains("stores", StringComparison.OrdinalIgnoreCase)
                                                  && file.DisplayName.Contains("builtin", StringComparison.OrdinalIgnoreCase))?.Path
               ?? files.First().Path;
    }

    private static ArtifactStorePackageManifestValue ReadPackage(string manifestPath)
    {
        var fullPath = Path.GetFullPath(manifestPath);
        var packageDirectory = Path.GetDirectoryName(fullPath)!;
        var root = ReadTomlTable(fullPath);
        var packageId = ReadString(root, "id") ?? Path.GetFileName(packageDirectory);
        var package = new ArtifactStorePackageManifestValue
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

        if (root.TryGetValue("stores", out var storesValue) && storesValue is TomlTableArray storeArray)
        {
            package.Stores = storeArray
                .OfType<TomlTable>()
                .Select(ReadStore)
                .Where(static store => !string.IsNullOrWhiteSpace(store.Id))
                .ToArray();
        }

        return package;
    }

    private static void AddCompatibilityIssue(
        ArtifactStorePackageManifestValue package,
        string manifestPath,
        ICollection<ArtifactStoreManifestIssue> issues)
    {
        if (package.Enabled && !TianShuExtensionManifestCommon.ApplyCompatibility(package))
        {
            issues.Add(new ArtifactStoreManifestIssue(
                $"artifact_store_manifest.{TianShuExtensionManifestCommon.VersionIncompatibleIssueCode}",
                $"工件存储包不可用：{package.UnavailableReason}",
                manifestPath));
        }
    }

    private static ArtifactStoreManifestValue ReadStore(TomlTable table)
        => new()
        {
            Id = ReadString(table, "id") ?? string.Empty,
            DisplayName = ReadString(table, "display_name") ?? string.Empty,
            Enabled = ReadBoolean(table, "enabled") ?? true,
            Type = ReadString(table, "type") ?? "filesystem",
            Root = ReadString(table, "root") ?? ReadString(table, "path"),
            AssemblyPath = ReadString(table, "assembly_path"),
            ProviderType = ReadString(table, "provider_type") ?? ReadString(table, "type_name"),
            MaxHistoryVersions = ReadInteger(table, "max_history_versions"),
            EnableCrossProcessSync = ReadBoolean(table, "enable_cross_process_sync") ?? false,
            Priority = ReadInteger(table, "priority") ?? 0,
        };

    private static TomlTable ReadTomlTable(string path)
        => TomlTable.From(Toml.Parse(File.ReadAllText(path, Encoding.UTF8), path));

    private static string ResolveWritableManifestPath(string rootDirectory, string packageId)
    {
        var normalizedId = NormalizePackageId(packageId);
        var targetRoot = ResolveArtifactStoreRootDirectory(rootDirectory);
        return Path.Combine(targetRoot, normalizedId, ManifestFileName);
    }

    private static string EnsureManifestInsideArtifactStoreRoot(string rootDirectory, string manifestPath)
    {
        var root = Path.GetFullPath(rootDirectory);
        var fullPath = Path.GetFullPath(Path.IsPathFullyQualified(manifestPath) ? manifestPath : Path.Combine(root, manifestPath));
        var artifactStoreRoot = ResolveArtifactStoreRootDirectory(root);
        if (IsUnderDirectory(fullPath, artifactStoreRoot))
        {
            return fullPath;
        }

        throw new InvalidOperationException("工件存储包 manifest 只能位于 TianShu modules/artifacts/stores 目录内。");
    }

    private static IEnumerable<string> EnumerateArtifactStoreRoots(string rootDirectory)
    {
        yield return ResolveArtifactStoreRootDirectory(rootDirectory);
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
            throw new InvalidOperationException("工件存储包 id 只能包含字母、数字、下划线或短横线。");
        }

        return trimmed;
    }

    private static string NormalizeStoreId(string id)
    {
        var trimmed = id.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)
            || trimmed.Any(static ch => !(char.IsLetterOrDigit(ch) || ch is '_' or '-')))
        {
            throw new InvalidOperationException("工件存储 id 只能包含字母、数字、下划线或短横线。");
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

    private static string? ReadString(TomlTable table, string key)
        => table.TryGetValue(key, out var value) ? value as string : null;

    private static bool? ReadBoolean(TomlTable table, string key)
        => table.TryGetValue(key, out var value) ? value as bool? : null;

    private static int? ReadInteger(TomlTable table, string key)
        => table.TryGetValue(key, out var value) ? Convert.ToInt32(value) : null;

    private static string GetDisplayName(string rootDirectory, string path)
    {
        var relative = Path.GetRelativePath(rootDirectory, path);
        return relative.StartsWith("..", StringComparison.Ordinal) ? path : relative;
    }
}

public sealed record ArtifactStoreManifestProjection(
    string RootDirectory,
    string ArtifactStoreRootDirectory,
    string? SelectedManifestPath,
    IReadOnlyList<ArtifactStoreManifestFileDescriptor> Files,
    ArtifactStorePackageManifestValue? SelectedPackage,
    IReadOnlyList<ArtifactStoreManifestIssue> Issues);

public sealed record ArtifactStoreManifestFileDescriptor(
    string Path,
    string DisplayName)
{
    public bool IsSelected { get; init; }
}

public sealed record ArtifactStoreManifestIssue(string Code, string Message, string? Path);

public sealed class ArtifactStorePackageManifestValue : ITianShuExtensionManifestMetadata
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

    public IReadOnlyList<ArtifactStoreManifestValue> Stores { get; set; } = [];
}

public sealed class ArtifactStoreManifestValue
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string Type { get; set; } = "filesystem";

    public string? Root { get; set; }

    public string? AssemblyPath { get; set; }

    public string? ProviderType { get; set; }

    public int? MaxHistoryVersions { get; set; }

    public bool EnableCrossProcessSync { get; set; }

    public int Priority { get; set; }
}
