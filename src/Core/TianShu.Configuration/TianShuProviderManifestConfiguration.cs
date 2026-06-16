using System.Text;
using Tomlyn;
using Tomlyn.Model;

namespace TianShu.Configuration;

/// <summary>
/// 读取、扫描并写回 TianShu 模型 Provider 包 manifest。
/// Loads, scans, and updates TianShu model provider package manifests.
/// </summary>
public sealed class TianShuProviderManifestConfiguration
{
    public const string ProviderDirectoryName = "modules/model/provider-adapters";
    public const string LegacyProviderDirectoryName = "providers";
    public const string ManifestFileName = "provider.toml";

    public ProviderManifestProjection Load(string rootDirectory, string? selectedManifestPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        var root = Path.GetFullPath(rootDirectory);
        var files = ScanProviderManifests(root);
        var selectedPath = ResolveSelectedManifest(root, selectedManifestPath, files);
        var issues = new List<ProviderManifestIssue>();
        ProviderPackageManifestValue? package = null;

        if (selectedPath is not null)
        {
            try
            {
                package = ReadPackage(selectedPath);
                AddCompatibilityIssue(package, selectedPath, issues);
            }
            catch (Exception ex)
            {
                issues.Add(new ProviderManifestIssue(
                    "provider_manifest.parse_failed",
                    $"无法解析模型 Provider 包 manifest：{ex.Message}",
                    selectedPath));
            }
        }

        return new ProviderManifestProjection(
            root,
            ResolveProviderRootDirectory(root),
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
            throw new InvalidOperationException($"模型 Provider 包 manifest 已存在：{manifestPath}");
        }

        var package = new ProviderPackageManifestValue
        {
            Id = NormalizePackageId(packageId),
            DisplayName = NormalizePackageId(packageId),
            Enabled = true,
            Type = "assembly",
            Priority = 0,
            ManifestPath = manifestPath,
            PackageDirectory = Path.GetDirectoryName(manifestPath)!,
            Adapters = [],
        };

        SavePackage(manifestPath, package);
        return manifestPath;
    }

    public string CopyPackage(string rootDirectory, string sourceManifestPath, string packageId, bool overwrite = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceManifestPath);
        var sourcePath = EnsureManifestInsideProviderRoot(rootDirectory, sourceManifestPath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("源模型 Provider 包 manifest 不存在。", sourcePath);
        }

        var targetPath = ResolveWritableManifestPath(rootDirectory, packageId);
        if (File.Exists(targetPath) && !overwrite)
        {
            throw new InvalidOperationException($"模型 Provider 包 manifest 已存在：{targetPath}");
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
        var fullPath = EnsureManifestInsideProviderRoot(rootDirectory, manifestPath);
        var packageDirectory = Path.GetDirectoryName(fullPath);
        if (string.Equals(Path.GetFileName(packageDirectory), "builtin", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("官方 builtin 模型 Provider 包不允许删除；可通过启用状态禁用或复制后替换。");
        }

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    public void SavePackage(string manifestPath, ProviderPackageManifestValue package)
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
        root["type"] = NormalizeScalar(package.Type, "assembly");
        root["priority"] = package.Priority;
        TianShuExtensionManifestCommon.WriteMetadata(root, package);

        var adapters = new TomlTableArray();
        foreach (var adapter in package.Adapters.Where(static adapter => !string.IsNullOrWhiteSpace(adapter.Id)))
        {
            var adapterTable = new TomlTable
            {
                ["id"] = NormalizeAdapterId(adapter.Id),
                ["enabled"] = adapter.Enabled,
                ["type"] = NormalizeScalar(adapter.Type, "assembly"),
                ["priority"] = adapter.Priority,
            };
            SetOptional(adapterTable, "display_name", adapter.DisplayName);
            SetOptional(adapterTable, "assembly_path", adapter.AssemblyPath);
            adapters.Add(adapterTable);
        }

        if (adapters.Count > 0)
        {
            root["adapters"] = adapters;
        }
        else
        {
            root.Remove("adapters");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, Toml.FromModel(root).TrimEnd() + Environment.NewLine, Encoding.UTF8);
    }

    public static string ResolveRootDirectory(string TianShuConfigPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(TianShuConfigPath);
        return Path.GetDirectoryName(Path.GetFullPath(TianShuConfigPath)) ?? Environment.CurrentDirectory;
    }

    public static string ResolveProviderRootDirectory(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        return TianShuHomePathUtilities.ResolveModulePathFromHome(rootDirectory, "model", "provider-adapters");
    }

    public static string ResolveAdapterAssemblyFullPath(ProviderPackageManifestValue package, ProviderAdapterManifestValue adapter)
    {
        var path = adapter.AssemblyPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(package.PackageDirectory, path));
    }

    private static IReadOnlyList<ProviderManifestFileDescriptor> ScanProviderManifests(string rootDirectory)
    {
        return EnumerateProviderRoots(rootDirectory)
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateDirectories(root)
                .Select(directory => Path.Combine(directory, ManifestFileName))
                .Where(File.Exists))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new ProviderManifestFileDescriptor(
                Path.GetFullPath(path),
                GetDisplayName(rootDirectory, path)))
            .OrderBy(static file => file.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ResolveSelectedManifest(
        string rootDirectory,
        string? selectedManifestPath,
        IReadOnlyList<ProviderManifestFileDescriptor> files)
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

        return files.FirstOrDefault(static file => file.DisplayName.Contains("provider-adapters", StringComparison.OrdinalIgnoreCase)
                                                  && file.DisplayName.Contains("builtin", StringComparison.OrdinalIgnoreCase))?.Path
               ?? files.First().Path;
    }

    private static ProviderPackageManifestValue ReadPackage(string manifestPath)
    {
        var fullPath = Path.GetFullPath(manifestPath);
        var packageDirectory = Path.GetDirectoryName(fullPath)!;
        var root = ReadTomlTable(fullPath);
        var packageId = ReadString(root, "id") ?? Path.GetFileName(packageDirectory);
        var package = new ProviderPackageManifestValue
        {
            Id = packageId,
            DisplayName = ReadString(root, "display_name") ?? packageId,
            Enabled = ReadBoolean(root, "enabled") ?? true,
            Type = ReadString(root, "type") ?? "assembly",
            Priority = ReadInteger(root, "priority") ?? 0,
            ManifestPath = fullPath,
            PackageDirectory = packageDirectory,
        };
        TianShuExtensionManifestCommon.ReadMetadata(root, package);

        if (root.TryGetValue("adapters", out var adaptersValue) && adaptersValue is TomlTableArray adapterArray)
        {
            package.Adapters = adapterArray
                .OfType<TomlTable>()
                .Select(ReadAdapter)
                .Where(static adapter => !string.IsNullOrWhiteSpace(adapter.Id))
                .ToArray();
        }

        return package;
    }

    private static void AddCompatibilityIssue(
        ProviderPackageManifestValue package,
        string manifestPath,
        ICollection<ProviderManifestIssue> issues)
    {
        if (package.Enabled && !TianShuExtensionManifestCommon.ApplyCompatibility(package))
        {
            issues.Add(new ProviderManifestIssue(
                $"provider_manifest.{TianShuExtensionManifestCommon.VersionIncompatibleIssueCode}",
                $"模型 Provider 包不可用：{package.UnavailableReason}",
                manifestPath));
        }
    }

    private static ProviderAdapterManifestValue ReadAdapter(TomlTable table)
        => new()
        {
            Id = ReadString(table, "id") ?? string.Empty,
            DisplayName = ReadString(table, "display_name") ?? string.Empty,
            Enabled = ReadBoolean(table, "enabled") ?? true,
            Type = ReadString(table, "type") ?? "assembly",
            AssemblyPath = ReadString(table, "assembly_path") ?? ReadString(table, "path"),
            Priority = ReadInteger(table, "priority") ?? 0,
        };

    private static TomlTable ReadTomlTable(string path)
        => TomlTable.From(Toml.Parse(File.ReadAllText(path, Encoding.UTF8), path));

    private static string ResolveWritableManifestPath(string rootDirectory, string packageId)
    {
        var normalizedId = NormalizePackageId(packageId);
        var targetRoot = ResolveProviderRootDirectory(rootDirectory);
        return Path.Combine(targetRoot, normalizedId, ManifestFileName);
    }

    private static string EnsureManifestInsideProviderRoot(string rootDirectory, string manifestPath)
    {
        var root = Path.GetFullPath(rootDirectory);
        var fullPath = Path.GetFullPath(Path.IsPathFullyQualified(manifestPath) ? manifestPath : Path.Combine(root, manifestPath));
        var providerRoot = ResolveProviderRootDirectory(root);
        if (IsUnderDirectory(fullPath, providerRoot))
        {
            return fullPath;
        }

        throw new InvalidOperationException("模型 Provider 包 manifest 只能位于 TianShu modules/model/provider-adapters 目录内。");
    }

    private static IEnumerable<string> EnumerateProviderRoots(string rootDirectory)
    {
        yield return ResolveProviderRootDirectory(rootDirectory);
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
            throw new InvalidOperationException("模型 Provider 包 id 只能包含字母、数字、下划线或短横线。");
        }

        return trimmed;
    }

    private static string NormalizeAdapterId(string id)
    {
        var trimmed = id.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)
            || trimmed.Any(static ch => !(char.IsLetterOrDigit(ch) || ch is '_' or '-')))
        {
            throw new InvalidOperationException("模型 Provider adapter id 只能包含字母、数字、下划线或短横线。");
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

public sealed record ProviderManifestProjection(
    string RootDirectory,
    string ProviderRootDirectory,
    string? SelectedManifestPath,
    IReadOnlyList<ProviderManifestFileDescriptor> Files,
    ProviderPackageManifestValue? SelectedPackage,
    IReadOnlyList<ProviderManifestIssue> Issues);

public sealed record ProviderManifestFileDescriptor(
    string Path,
    string DisplayName)
{
    public bool IsSelected { get; init; }
}

public sealed record ProviderManifestIssue(string Code, string Message, string? Path);

public sealed class ProviderPackageManifestValue : ITianShuExtensionManifestMetadata
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string Type { get; set; } = "assembly";

    public int Priority { get; set; }

    public string ManifestPath { get; set; } = string.Empty;

    public string PackageDirectory { get; set; } = string.Empty;

    public string? Version { get; set; }

    public string? MinTianShuVersion { get; set; }

    public IReadOnlyList<string> Capabilities { get; set; } = [];

    public IReadOnlyList<string> Diagnostics { get; set; } = [];

    public string LoadStatus { get; set; } = TianShuExtensionManifestCommon.LoadStatusAvailable;

    public string? UnavailableReason { get; set; }

    public IReadOnlyList<ProviderAdapterManifestValue> Adapters { get; set; } = [];
}

public sealed class ProviderAdapterManifestValue
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string Type { get; set; } = "assembly";

    public string? AssemblyPath { get; set; }

    public int Priority { get; set; }
}
