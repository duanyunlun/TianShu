using System.Text;
using Tomlyn;
using Tomlyn.Model;

namespace TianShu.Configuration;

/// <summary>
/// 读取、扫描并写回 TianShu Diagnostics / Telemetry Sink 包 manifest。
/// Loads, scans, and updates TianShu Diagnostics / Telemetry Sink package manifests.
/// </summary>
public sealed class TianShuDiagnosticSinkManifestConfiguration
{
    public const string DiagnosticSinkDirectoryName = "modules/diagnostics/sinks";
    public const string LegacyDiagnosticSinkDirectoryName = "diagnostic-sinks";
    public const string ManifestFileName = "sink.toml";

    public DiagnosticSinkManifestProjection Load(string rootDirectory, string? selectedManifestPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        var root = Path.GetFullPath(rootDirectory);
        var files = ScanDiagnosticSinkManifests(root);
        var selectedPath = ResolveSelectedManifest(root, selectedManifestPath, files);
        var issues = new List<DiagnosticSinkManifestIssue>();
        DiagnosticSinkPackageManifestValue? package = null;

        if (selectedPath is not null)
        {
            try
            {
                package = ReadPackage(selectedPath);
                AddCompatibilityIssue(package, selectedPath, issues);
            }
            catch (Exception ex)
            {
                issues.Add(new DiagnosticSinkManifestIssue(
                    "diagnostic_sink_manifest.parse_failed",
                    $"无法解析诊断 Sink 包 manifest：{ex.Message}",
                    selectedPath));
            }
        }

        return new DiagnosticSinkManifestProjection(
            root,
            ResolveDiagnosticSinkRootDirectory(root),
            selectedPath,
            files.Select(file => file with { IsSelected = string.Equals(file.Path, selectedPath, StringComparison.OrdinalIgnoreCase) }).ToArray(),
            package,
            issues);
    }

    public IReadOnlyList<DiagnosticSinkPackageManifestValue> LoadEnabledPackages(string rootDirectory)
    {
        var projection = Load(rootDirectory);
        var packages = new List<DiagnosticSinkPackageManifestValue>();
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
            throw new InvalidOperationException($"诊断 Sink 包 manifest 已存在：{manifestPath}");
        }

        var package = new DiagnosticSinkPackageManifestValue
        {
            Id = NormalizePackageId(packageId),
            DisplayName = NormalizePackageId(packageId),
            Enabled = true,
            Type = "package",
            Priority = 0,
            ManifestPath = manifestPath,
            PackageDirectory = Path.GetDirectoryName(manifestPath)!,
            Sinks = [],
        };

        SavePackage(manifestPath, package);
        return manifestPath;
    }

    public string CopyPackage(string rootDirectory, string sourceManifestPath, string packageId, bool overwrite = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceManifestPath);
        var sourcePath = EnsureManifestInsideDiagnosticSinkRoot(rootDirectory, sourceManifestPath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("源诊断 Sink 包 manifest 不存在。", sourcePath);
        }

        var targetPath = ResolveWritableManifestPath(rootDirectory, packageId);
        if (File.Exists(targetPath) && !overwrite)
        {
            throw new InvalidOperationException($"诊断 Sink 包 manifest 已存在：{targetPath}");
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
        var fullPath = EnsureManifestInsideDiagnosticSinkRoot(rootDirectory, manifestPath);
        var packageDirectory = Path.GetDirectoryName(fullPath);
        if (string.Equals(Path.GetFileName(packageDirectory), "builtin", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("官方 builtin 诊断 Sink 包不允许删除；可通过启用状态禁用或复制后替换。");
        }

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    public void SavePackage(string manifestPath, DiagnosticSinkPackageManifestValue package)
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

        var sinks = new TomlTableArray();
        foreach (var sink in package.Sinks.Where(static sink => !string.IsNullOrWhiteSpace(sink.Id)))
        {
            var sinkTable = new TomlTable
            {
                ["id"] = NormalizeSinkId(sink.Id),
                ["enabled"] = sink.Enabled,
                ["type"] = NormalizeScalar(sink.Type, "turn-log"),
                ["priority"] = sink.Priority,
            };
            SetOptional(sinkTable, "display_name", sink.DisplayName);
            SetOptional(sinkTable, "level", sink.Level);
            SetOptional(sinkTable, "target", sink.Target);
            SetOptional(sinkTable, "assembly_path", sink.AssemblyPath);
            SetOptional(sinkTable, "provider_type", sink.ProviderType);
            SetOptional(sinkTable, "endpoint", sink.Endpoint);
            if (sink.Modules.Count > 0)
            {
                var modules = new TomlArray();
                foreach (var module in sink.Modules.Select(static module => module.Trim()).Where(static module => !string.IsNullOrWhiteSpace(module)))
                {
                    modules.Add(module);
                }

                sinkTable["modules"] = modules;
            }
            else
            {
                sinkTable.Remove("modules");
            }

            if (sink.MaxBytes is not null)
            {
                sinkTable["max_bytes"] = sink.MaxBytes.Value;
            }
            else
            {
                sinkTable.Remove("max_bytes");
            }

            sinks.Add(sinkTable);
        }

        if (sinks.Count > 0)
        {
            root["sinks"] = sinks;
        }
        else
        {
            root.Remove("sinks");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, Toml.FromModel(root).TrimEnd() + Environment.NewLine, Encoding.UTF8);
    }

    public static string ResolveRootDirectory(string TianShuConfigPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(TianShuConfigPath);
        return Path.GetDirectoryName(Path.GetFullPath(TianShuConfigPath)) ?? Environment.CurrentDirectory;
    }

    public static string ResolveDiagnosticSinkRootDirectory(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        return TianShuHomePathUtilities.ResolveModulePathFromHome(rootDirectory, "diagnostics", "sinks");
    }

    public static string ResolveSinkTargetFullPath(DiagnosticSinkPackageManifestValue package, DiagnosticSinkManifestValue sink)
    {
        var path = sink.Target;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = sink.Type.Equals("artifact-file", StringComparison.OrdinalIgnoreCase)
                ? "./artifacts/provider-requests"
                : ".";
        }

        return Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(package.PackageDirectory, path));
    }

    public static string ResolveSinkAssemblyFullPath(DiagnosticSinkPackageManifestValue package, DiagnosticSinkManifestValue sink)
    {
        var path = sink.AssemblyPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(package.PackageDirectory, path));
    }

    private static IReadOnlyList<DiagnosticSinkManifestFileDescriptor> ScanDiagnosticSinkManifests(string rootDirectory)
    {
        return EnumerateDiagnosticSinkRoots(rootDirectory)
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateDirectories(root)
                .Select(directory => Path.Combine(directory, ManifestFileName))
                .Where(File.Exists))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new DiagnosticSinkManifestFileDescriptor(
                Path.GetFullPath(path),
                GetDisplayName(rootDirectory, path)))
            .OrderBy(static file => file.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ResolveSelectedManifest(
        string rootDirectory,
        string? selectedManifestPath,
        IReadOnlyList<DiagnosticSinkManifestFileDescriptor> files)
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

        return files.FirstOrDefault(static file => file.DisplayName.Contains("sinks", StringComparison.OrdinalIgnoreCase)
                                                  && file.DisplayName.Contains("builtin", StringComparison.OrdinalIgnoreCase))?.Path
               ?? files.First().Path;
    }

    private static DiagnosticSinkPackageManifestValue ReadPackage(string manifestPath)
    {
        var fullPath = Path.GetFullPath(manifestPath);
        var packageDirectory = Path.GetDirectoryName(fullPath)!;
        var root = ReadTomlTable(fullPath);
        var packageId = ReadString(root, "id") ?? Path.GetFileName(packageDirectory);
        var package = new DiagnosticSinkPackageManifestValue
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

        if (root.TryGetValue("sinks", out var sinksValue) && sinksValue is TomlTableArray sinkArray)
        {
            package.Sinks = sinkArray
                .OfType<TomlTable>()
                .Select(ReadSink)
                .Where(static sink => !string.IsNullOrWhiteSpace(sink.Id))
                .ToArray();
        }

        return package;
    }

    private static void AddCompatibilityIssue(
        DiagnosticSinkPackageManifestValue package,
        string manifestPath,
        ICollection<DiagnosticSinkManifestIssue> issues)
    {
        if (package.Enabled && !TianShuExtensionManifestCommon.ApplyCompatibility(package))
        {
            issues.Add(new DiagnosticSinkManifestIssue(
                $"diagnostic_sink_manifest.{TianShuExtensionManifestCommon.VersionIncompatibleIssueCode}",
                $"诊断 Sink 包不可用：{package.UnavailableReason}",
                manifestPath));
        }
    }

    private static DiagnosticSinkManifestValue ReadSink(TomlTable table)
        => new()
        {
            Id = ReadString(table, "id") ?? string.Empty,
            DisplayName = ReadString(table, "display_name") ?? string.Empty,
            Enabled = ReadBoolean(table, "enabled") ?? true,
            Type = ReadString(table, "type") ?? "turn-log",
            Level = ReadString(table, "level") ?? "stats",
            Target = ReadString(table, "target") ?? ReadString(table, "path"),
            AssemblyPath = ReadString(table, "assembly_path"),
            ProviderType = ReadString(table, "provider_type") ?? ReadString(table, "type_name"),
            Endpoint = ReadString(table, "endpoint"),
            Modules = ReadStringArray(table, "modules"),
            MaxBytes = ReadLong(table, "max_bytes"),
            Priority = ReadInteger(table, "priority") ?? 0,
        };

    private static TomlTable ReadTomlTable(string path)
        => TomlTable.From(Toml.Parse(File.ReadAllText(path, Encoding.UTF8), path));

    private static string ResolveWritableManifestPath(string rootDirectory, string packageId)
    {
        var normalizedId = NormalizePackageId(packageId);
        var targetRoot = ResolveDiagnosticSinkRootDirectory(rootDirectory);
        return Path.Combine(targetRoot, normalizedId, ManifestFileName);
    }

    private static string EnsureManifestInsideDiagnosticSinkRoot(string rootDirectory, string manifestPath)
    {
        var root = Path.GetFullPath(rootDirectory);
        var fullPath = Path.GetFullPath(Path.IsPathFullyQualified(manifestPath) ? manifestPath : Path.Combine(root, manifestPath));
        var diagnosticSinkRoot = ResolveDiagnosticSinkRootDirectory(root);
        if (IsUnderDirectory(fullPath, diagnosticSinkRoot))
        {
            return fullPath;
        }

        throw new InvalidOperationException("诊断 Sink 包 manifest 只能位于 TianShu modules/diagnostics/sinks 目录内。");
    }

    private static IEnumerable<string> EnumerateDiagnosticSinkRoots(string rootDirectory)
    {
        yield return ResolveDiagnosticSinkRootDirectory(rootDirectory);
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
            throw new InvalidOperationException("诊断 Sink 包 id 只能包含字母、数字、下划线或短横线。");
        }

        return trimmed;
    }

    private static string NormalizeSinkId(string id)
    {
        var trimmed = id.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)
            || trimmed.Any(static ch => !(char.IsLetterOrDigit(ch) || ch is '_' or '-')))
        {
            throw new InvalidOperationException("诊断 Sink id 只能包含字母、数字、下划线或短横线。");
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

    private static long? ReadLong(TomlTable table, string key)
        => table.TryGetValue(key, out var value) ? Convert.ToInt64(value) : null;

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

public sealed record DiagnosticSinkManifestProjection(
    string RootDirectory,
    string DiagnosticSinkRootDirectory,
    string? SelectedManifestPath,
    IReadOnlyList<DiagnosticSinkManifestFileDescriptor> Files,
    DiagnosticSinkPackageManifestValue? SelectedPackage,
    IReadOnlyList<DiagnosticSinkManifestIssue> Issues);

public sealed record DiagnosticSinkManifestFileDescriptor(
    string Path,
    string DisplayName)
{
    public bool IsSelected { get; init; }
}

public sealed record DiagnosticSinkManifestIssue(string Code, string Message, string? Path);

public sealed class DiagnosticSinkPackageManifestValue : ITianShuExtensionManifestMetadata
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

    public IReadOnlyList<DiagnosticSinkManifestValue> Sinks { get; set; } = [];
}

public sealed class DiagnosticSinkManifestValue
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string Type { get; set; } = "turn-log";

    public string Level { get; set; } = "stats";

    public string? Target { get; set; }

    public string? AssemblyPath { get; set; }

    public string? ProviderType { get; set; }

    public string? Endpoint { get; set; }

    public IReadOnlyList<string> Modules { get; set; } = [];

    public long? MaxBytes { get; set; }

    public int Priority { get; set; }
}
