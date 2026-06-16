using System.Text;
using Tomlyn;
using Tomlyn.Model;

namespace TianShu.Configuration;

/// <summary>
/// 读取、扫描并写回 TianShu 工具包 manifest。
/// Loads, scans, and updates TianShu tool package manifests.
/// </summary>
public sealed class TianShuToolManifestConfiguration
{
    public const string ToolDirectoryName = "modules/tools/packages";
    public const string LegacyToolDirectoryName = "tools";
    public const string LegacyCasedToolDirectoryName = "Tools";
    public const string ManifestFileName = "tool.toml";

    public ToolManifestProjection Load(string rootDirectory, string? selectedManifestPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        var root = Path.GetFullPath(rootDirectory);
        var files = ScanToolManifests(root);
        var selectedPath = ResolveSelectedManifest(root, selectedManifestPath, files);
        var issues = new List<ToolManifestIssue>();
        ToolPackageManifestValue? package = null;

        if (selectedPath is not null)
        {
            try
            {
                package = ReadPackage(selectedPath);
                AddCompatibilityIssue(package, selectedPath, issues);
            }
            catch (Exception ex)
            {
                issues.Add(new ToolManifestIssue(
                    "tool_manifest.parse_failed",
                    $"无法解析工具包 manifest：{ex.Message}",
                    selectedPath));
            }
        }

        return new ToolManifestProjection(
            root,
            ResolveToolRootDirectory(root),
            string.Empty,
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
            throw new InvalidOperationException($"工具包 manifest 已存在：{manifestPath}");
        }

        var package = new ToolPackageManifestValue
        {
            Id = NormalizePackageId(packageId),
            DisplayName = NormalizePackageId(packageId),
            Enabled = true,
            Type = "assembly",
            Priority = 0,
            ManifestPath = manifestPath,
            PackageDirectory = Path.GetDirectoryName(manifestPath)!,
            IsLegacy = false,
            Providers = [],
        };

        SavePackage(manifestPath, package);
        return manifestPath;
    }

    public string CopyPackage(string rootDirectory, string sourceManifestPath, string packageId, bool overwrite = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceManifestPath);
        var sourcePath = EnsureManifestInsideKnownRoots(rootDirectory, sourceManifestPath, allowLegacy: false);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("源工具包 manifest 不存在。", sourcePath);
        }

        var targetPath = ResolveWritableManifestPath(rootDirectory, packageId);
        if (File.Exists(targetPath) && !overwrite)
        {
            throw new InvalidOperationException($"工具包 manifest 已存在：{targetPath}");
        }

        var package = ReadPackage(sourcePath);
        package.Id = NormalizePackageId(packageId);
        package.DisplayName = string.IsNullOrWhiteSpace(package.DisplayName) ? package.Id : package.DisplayName;
        package.ManifestPath = targetPath;
        package.PackageDirectory = Path.GetDirectoryName(targetPath)!;
        package.IsLegacy = false;
        SavePackage(targetPath, package);
        return targetPath;
    }

    public void DeletePackage(string rootDirectory, string manifestPath)
    {
        var fullPath = EnsureManifestInsideKnownRoots(rootDirectory, manifestPath, allowLegacy: false);
        var packageDirectory = Path.GetDirectoryName(fullPath);
        if (string.Equals(Path.GetFileName(packageDirectory), "builtin", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("官方 builtin 工具包不允许删除；可通过启用状态禁用或复制后替换。");
        }

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    public void SavePackage(string manifestPath, ToolPackageManifestValue package)
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
        SetOptional(root, "assembly_path", package.AssemblyPath);
        SetOptional(root, "provider_type", package.ProviderType);
        if (package.ReplaceExisting)
        {
            root["replace_existing"] = true;
        }
        else
        {
            root.Remove("replace_existing");
        }

        var providers = new TomlTableArray();
        foreach (var provider in package.Providers.Where(static provider => !string.IsNullOrWhiteSpace(provider.Id)))
        {
            var providerTable = new TomlTable
            {
                ["id"] = NormalizeProviderId(provider.Id),
                ["enabled"] = provider.Enabled,
                ["type"] = NormalizeScalar(provider.Type, "assembly"),
                ["priority"] = provider.Priority,
            };
            SetOptional(providerTable, "assembly_path", provider.AssemblyPath);
            SetOptional(providerTable, "provider_type", provider.ProviderType);
            if (provider.ReplaceExisting)
            {
                providerTable["replace_existing"] = true;
            }

            providers.Add(providerTable);
        }

        if (providers.Count > 0)
        {
            root["providers"] = providers;
        }
        else
        {
            root.Remove("providers");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, Toml.FromModel(root).TrimEnd() + Environment.NewLine, Encoding.UTF8);
    }

    public static string ResolveRootDirectory(string TianShuConfigPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(TianShuConfigPath);
        return Path.GetDirectoryName(Path.GetFullPath(TianShuConfigPath)) ?? Environment.CurrentDirectory;
    }

    public static string ResolveToolRootDirectory(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        return TianShuHomePathUtilities.ResolveModulePathFromHome(rootDirectory, "tools", "packages");
    }

    public static string ResolveProviderAssemblyFullPath(ToolPackageManifestValue package, ToolProviderManifestValue provider)
    {
        var path = provider.AssemblyPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(package.PackageDirectory, path));
    }

    private static IReadOnlyList<ToolManifestFileDescriptor> ScanToolManifests(string rootDirectory)
    {
        var toolRoot = ResolveToolRootDirectory(rootDirectory);
        if (!Directory.Exists(toolRoot))
        {
            return [];
        }

        return Directory.EnumerateDirectories(toolRoot)
                .Select(directory => Path.Combine(directory, ManifestFileName))
                .Where(File.Exists)
                .Select(path => new ToolManifestFileDescriptor(
                    Path.GetFullPath(path),
                    GetDisplayName(rootDirectory, path),
                    false))
            .OrderBy(static file => file.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ResolveSelectedManifest(
        string rootDirectory,
        string? selectedManifestPath,
        IReadOnlyList<ToolManifestFileDescriptor> files)
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

        return files.FirstOrDefault(static file => file.DisplayName.Contains("packages", StringComparison.OrdinalIgnoreCase)
                                                  && file.DisplayName.Contains("builtin", StringComparison.OrdinalIgnoreCase))?.Path
               ?? files.First().Path;
    }

    private static ToolPackageManifestValue ReadPackage(string manifestPath)
    {
        var fullPath = Path.GetFullPath(manifestPath);
        var packageDirectory = Path.GetDirectoryName(fullPath)!;
        var root = ReadTomlTable(fullPath);
        var packageId = ReadString(root, "id") ?? Path.GetFileName(packageDirectory);
        var package = new ToolPackageManifestValue
        {
            Id = packageId,
            DisplayName = ReadString(root, "display_name") ?? packageId,
            Enabled = ReadBoolean(root, "enabled") ?? true,
            Type = ReadString(root, "type") ?? "assembly",
            Priority = ReadInteger(root, "priority") ?? 0,
            AssemblyPath = ReadString(root, "assembly_path") ?? ReadString(root, "path"),
            ProviderType = ReadString(root, "provider_type") ?? ReadString(root, "type_name"),
            ReplaceExisting = ReadBoolean(root, "replace_existing") ?? false,
            ManifestPath = fullPath,
            PackageDirectory = packageDirectory,
            IsLegacy = false,
        };
        TianShuExtensionManifestCommon.ReadMetadata(root, package);

        if (root.TryGetValue("providers", out var providersValue) && providersValue is TomlTableArray providerArray)
        {
            package.Providers = providerArray
                .OfType<TomlTable>()
                .Select(ReadProvider)
                .Where(static provider => !string.IsNullOrWhiteSpace(provider.Id))
                .ToArray();
        }

        return package;
    }

    private static void AddCompatibilityIssue(
        ToolPackageManifestValue package,
        string manifestPath,
        ICollection<ToolManifestIssue> issues)
    {
        if (package.Enabled && !TianShuExtensionManifestCommon.ApplyCompatibility(package))
        {
            issues.Add(new ToolManifestIssue(
                $"tool_manifest.{TianShuExtensionManifestCommon.VersionIncompatibleIssueCode}",
                $"工具包不可用：{package.UnavailableReason}",
                manifestPath));
        }
    }

    private static ToolProviderManifestValue ReadProvider(TomlTable table)
        => new()
        {
            Id = ReadString(table, "id") ?? string.Empty,
            Enabled = ReadBoolean(table, "enabled") ?? true,
            Type = ReadString(table, "type") ?? "assembly",
            AssemblyPath = ReadString(table, "assembly_path") ?? ReadString(table, "path"),
            ProviderType = ReadString(table, "provider_type") ?? ReadString(table, "type_name"),
            Priority = ReadInteger(table, "priority") ?? 0,
            ReplaceExisting = ReadBoolean(table, "replace_existing") ?? false,
        };

    private static TomlTable ReadTomlTable(string path)
        => TomlTable.From(Toml.Parse(File.ReadAllText(path, Encoding.UTF8), path));

    private static string ResolveWritableManifestPath(string rootDirectory, string packageId)
    {
        var normalizedId = NormalizePackageId(packageId);
        var targetRoot = ResolveToolRootDirectory(rootDirectory);
        return Path.Combine(targetRoot, normalizedId, ManifestFileName);
    }

    private static string EnsureManifestInsideKnownRoots(string rootDirectory, string manifestPath, bool allowLegacy)
    {
        var root = Path.GetFullPath(rootDirectory);
        var fullPath = Path.GetFullPath(Path.IsPathFullyQualified(manifestPath) ? manifestPath : Path.Combine(root, manifestPath));
        var writableRoot = ResolveToolRootDirectory(root);
        if (IsUnderDirectory(fullPath, writableRoot))
        {
            return fullPath;
        }

        throw new InvalidOperationException("工具包 manifest 只能写入 TianShu modules/tools/packages 目录内。");
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
            throw new InvalidOperationException("工具包 id 只能包含字母、数字、下划线或短横线。");
        }

        return trimmed;
    }

    private static string NormalizeProviderId(string id)
    {
        var trimmed = id.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)
            || trimmed.Any(static ch => !(char.IsLetterOrDigit(ch) || ch is '_' or '-')))
        {
            throw new InvalidOperationException("工具 Provider id 只能包含字母、数字、下划线或短横线。");
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

public sealed record ToolManifestProjection(
    string RootDirectory,
    string ToolRootDirectory,
    string LegacyToolRootDirectory,
    string? SelectedManifestPath,
    IReadOnlyList<ToolManifestFileDescriptor> Files,
    ToolPackageManifestValue? SelectedPackage,
    IReadOnlyList<ToolManifestIssue> Issues);

public sealed record ToolManifestFileDescriptor(
    string Path,
    string DisplayName,
    bool IsLegacy)
{
    public bool IsSelected { get; init; }
}

public sealed record ToolManifestIssue(string Code, string Message, string? Path);

public sealed class ToolPackageManifestValue : ITianShuExtensionManifestMetadata
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string Type { get; set; } = "assembly";

    public int Priority { get; set; }

    public string? AssemblyPath { get; set; }

    public string? ProviderType { get; set; }

    public bool ReplaceExisting { get; set; }

    public string ManifestPath { get; set; } = string.Empty;

    public string PackageDirectory { get; set; } = string.Empty;

    public bool IsLegacy { get; set; }

    public string? Version { get; set; }

    public string? MinTianShuVersion { get; set; }

    public IReadOnlyList<string> Capabilities { get; set; } = [];

    public IReadOnlyList<string> Diagnostics { get; set; } = [];

    public string LoadStatus { get; set; } = TianShuExtensionManifestCommon.LoadStatusAvailable;

    public string? UnavailableReason { get; set; }

    public IReadOnlyList<ToolProviderManifestValue> Providers { get; set; } = [];
}

public sealed class ToolProviderManifestValue
{
    public string Id { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string Type { get; set; } = "assembly";

    public string? AssemblyPath { get; set; }

    public string? ProviderType { get; set; }

    public int Priority { get; set; }

    public bool ReplaceExisting { get; set; }
}
