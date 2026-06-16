using System.Text;
using Tomlyn;
using Tomlyn.Model;

namespace TianShu.Configuration;

/// <summary>
/// 读取、扫描并写回 TianShu Approval / Policy Strategy 包 manifest。
/// Loads, scans, and updates TianShu Approval / Policy Strategy package manifests.
/// </summary>
public sealed class TianShuPolicyStrategyManifestConfiguration
{
    public const string PolicyStrategyDirectoryName = "modules/policies/strategies";
    public const string LegacyPolicyStrategyDirectoryName = "policy-strategies";
    public const string ManifestFileName = "policy.toml";

    public PolicyStrategyManifestProjection Load(string rootDirectory, string? selectedManifestPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        var root = Path.GetFullPath(rootDirectory);
        var files = ScanPolicyStrategyManifests(root);
        var selectedPath = ResolveSelectedManifest(root, selectedManifestPath, files);
        var issues = new List<PolicyStrategyManifestIssue>();
        PolicyStrategyPackageManifestValue? package = null;

        if (selectedPath is not null)
        {
            try
            {
                package = ReadPackage(selectedPath);
                AddCompatibilityIssue(package, selectedPath, issues);
            }
            catch (Exception ex)
            {
                issues.Add(new PolicyStrategyManifestIssue(
                    "policy_strategy_manifest.parse_failed",
                    $"无法解析审批策略包 manifest：{ex.Message}",
                    selectedPath));
            }
        }

        return new PolicyStrategyManifestProjection(
            root,
            ResolvePolicyStrategyRootDirectory(root),
            selectedPath,
            files.Select(file => file with { IsSelected = string.Equals(file.Path, selectedPath, StringComparison.OrdinalIgnoreCase) }).ToArray(),
            package,
            issues);
    }

    public IReadOnlyList<PolicyStrategyPackageManifestValue> LoadEnabledPackages(string rootDirectory)
    {
        var projection = Load(rootDirectory);
        var packages = new List<PolicyStrategyPackageManifestValue>();
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

    public IReadOnlyList<PolicyStrategyManifestValue> LoadEnabledStrategies(string rootDirectory)
        => LoadEnabledPackages(rootDirectory)
            .SelectMany(static package => package.Strategies)
            .Where(static strategy => strategy.Enabled)
            .OrderBy(static strategy => strategy.Priority)
            .ThenBy(static strategy => strategy.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public string CreatePackage(string rootDirectory, string packageId, bool overwrite = false)
    {
        var manifestPath = ResolveWritableManifestPath(rootDirectory, packageId);
        if (File.Exists(manifestPath) && !overwrite)
        {
            throw new InvalidOperationException($"审批策略包 manifest 已存在：{manifestPath}");
        }

        var package = new PolicyStrategyPackageManifestValue
        {
            Id = NormalizePackageId(packageId),
            DisplayName = NormalizePackageId(packageId),
            Enabled = true,
            Type = "package",
            Priority = 0,
            ManifestPath = manifestPath,
            PackageDirectory = Path.GetDirectoryName(manifestPath)!,
            Strategies = [],
        };

        SavePackage(manifestPath, package);
        return manifestPath;
    }

    public string CopyPackage(string rootDirectory, string sourceManifestPath, string packageId, bool overwrite = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceManifestPath);
        var sourcePath = EnsureManifestInsidePolicyStrategyRoot(rootDirectory, sourceManifestPath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("源审批策略包 manifest 不存在。", sourcePath);
        }

        var targetPath = ResolveWritableManifestPath(rootDirectory, packageId);
        if (File.Exists(targetPath) && !overwrite)
        {
            throw new InvalidOperationException($"审批策略包 manifest 已存在：{targetPath}");
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
        var fullPath = EnsureManifestInsidePolicyStrategyRoot(rootDirectory, manifestPath);
        var packageDirectory = Path.GetDirectoryName(fullPath);
        if (string.Equals(Path.GetFileName(packageDirectory), "builtin", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("官方 builtin 审批策略包不允许删除；可通过启用状态禁用或复制后替换。");
        }

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    public void SavePackage(string manifestPath, PolicyStrategyPackageManifestValue package)
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

        var strategies = new TomlTableArray();
        foreach (var strategy in package.Strategies.Where(static strategy => !string.IsNullOrWhiteSpace(strategy.Id)))
        {
            var strategyTable = new TomlTable
            {
                ["id"] = NormalizeStrategyId(strategy.Id),
                ["enabled"] = strategy.Enabled,
                ["type"] = NormalizeScalar(strategy.Type, "policy"),
                ["priority"] = strategy.Priority,
            };
            SetOptional(strategyTable, "display_name", strategy.DisplayName);
            SetOptional(strategyTable, "approval_policy", strategy.ApprovalPolicy);
            SetOptional(strategyTable, "sandbox_mode", strategy.SandboxMode);
            SetOptionalBoolean(strategyTable, "network_access", strategy.NetworkAccess);
            SetOptionalBoolean(strategyTable, "allow_login_shell", strategy.AllowLoginShell);
            SetOptional(strategyTable, "file_write_policy", strategy.FileWritePolicy);
            SetStringArray(strategyTable, "write_requires_approval_globs", strategy.WriteRequiresApprovalGlobs);
            SetStringArray(strategyTable, "dangerous_command_patterns", strategy.DangerousCommandPatterns);
            SetRuleArray(strategyTable, "command_rules", strategy.CommandRules, WriteCommandRule);
            SetRuleArray(strategyTable, "network_rules", strategy.NetworkRules, WriteNetworkRule);
            SetOptional(strategyTable, "assembly_path", strategy.AssemblyPath);
            SetOptional(strategyTable, "provider_type", strategy.ProviderType);
            strategies.Add(strategyTable);
        }

        if (strategies.Count > 0)
        {
            root["strategies"] = strategies;
        }
        else
        {
            root.Remove("strategies");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, Toml.FromModel(root).TrimEnd() + Environment.NewLine, Encoding.UTF8);
    }

    public static string ResolveRootDirectory(string tianShuConfigPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tianShuConfigPath);
        return Path.GetDirectoryName(Path.GetFullPath(tianShuConfigPath)) ?? Environment.CurrentDirectory;
    }

    public static string ResolvePolicyStrategyRootDirectory(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        return TianShuHomePathUtilities.ResolveModulePathFromHome(rootDirectory, "policies", "strategies");
    }

    public static PolicyStrategyEffectiveDefaults ResolveEffectiveDefaults(string rootDirectory)
    {
        try
        {
            PolicyStrategyEffectiveDefaults defaults = new(null, null, null, null);
            foreach (var strategy in new TianShuPolicyStrategyManifestConfiguration().LoadEnabledStrategies(rootDirectory))
            {
                defaults = new PolicyStrategyEffectiveDefaults(
                    NormalizeOptionalScalar(strategy.ApprovalPolicy, defaults.ApprovalPolicy),
                    NormalizeOptionalScalar(strategy.SandboxMode, defaults.SandboxMode),
                    strategy.NetworkAccess ?? defaults.NetworkAccess,
                    strategy.AllowLoginShell ?? defaults.AllowLoginShell);
            }

            return defaults;
        }
        catch
        {
            return new PolicyStrategyEffectiveDefaults(null, null, null, null);
        }
    }

    public static IReadOnlyList<PolicyStrategyCommandRuleValue> ResolveEffectiveCommandRules(string rootDirectory)
    {
        try
        {
            return new TianShuPolicyStrategyManifestConfiguration()
                .LoadEnabledStrategies(rootDirectory)
                .SelectMany(static strategy => strategy.CommandRules)
                .Where(static rule => rule.Prefix.Count > 0 && !string.IsNullOrWhiteSpace(rule.Decision))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    public static IReadOnlyList<PolicyStrategyNetworkRuleValue> ResolveEffectiveNetworkRules(string rootDirectory)
    {
        try
        {
            return new TianShuPolicyStrategyManifestConfiguration()
                .LoadEnabledStrategies(rootDirectory)
                .SelectMany(static strategy => strategy.NetworkRules)
                .Where(static rule => !string.IsNullOrWhiteSpace(rule.Protocol)
                                      && !string.IsNullOrWhiteSpace(rule.Host)
                                      && !string.IsNullOrWhiteSpace(rule.Decision))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    public static string ResolveStrategyAssemblyFullPath(PolicyStrategyPackageManifestValue package, PolicyStrategyManifestValue strategy)
    {
        if (string.IsNullOrWhiteSpace(strategy.AssemblyPath))
        {
            return string.Empty;
        }

        return Path.IsPathFullyQualified(strategy.AssemblyPath)
            ? Path.GetFullPath(strategy.AssemblyPath)
            : Path.GetFullPath(Path.Combine(package.PackageDirectory, strategy.AssemblyPath));
    }

    public static string ResolveAssemblyFullPath(PolicyStrategyPackageManifestValue package, PolicyStrategyManifestValue strategy)
        => ResolveStrategyAssemblyFullPath(package, strategy);

    private static IReadOnlyList<PolicyStrategyManifestFileDescriptor> ScanPolicyStrategyManifests(string rootDirectory)
    {
        return EnumeratePolicyStrategyRoots(rootDirectory)
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateDirectories(root)
                .Select(directory => Path.Combine(directory, ManifestFileName))
                .Where(File.Exists))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new PolicyStrategyManifestFileDescriptor(
                Path.GetFullPath(path),
                GetDisplayName(rootDirectory, path)))
            .OrderBy(static file => file.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ResolveSelectedManifest(
        string rootDirectory,
        string? selectedManifestPath,
        IReadOnlyList<PolicyStrategyManifestFileDescriptor> files)
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

        return files.FirstOrDefault(static file => file.DisplayName.Contains("strategies", StringComparison.OrdinalIgnoreCase)
                                                  && file.DisplayName.Contains("builtin", StringComparison.OrdinalIgnoreCase))?.Path
               ?? files.First().Path;
    }

    private static PolicyStrategyPackageManifestValue ReadPackage(string manifestPath)
    {
        var fullPath = Path.GetFullPath(manifestPath);
        var packageDirectory = Path.GetDirectoryName(fullPath)!;
        var root = ReadTomlTable(fullPath);
        var packageId = ReadString(root, "id") ?? Path.GetFileName(packageDirectory);
        var package = new PolicyStrategyPackageManifestValue
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

        if (root.TryGetValue("strategies", out var strategiesValue) && strategiesValue is TomlTableArray strategyArray)
        {
            package.Strategies = strategyArray
                .OfType<TomlTable>()
                .Select(ReadStrategy)
                .Where(static strategy => !string.IsNullOrWhiteSpace(strategy.Id))
                .ToArray();
        }

        return package;
    }

    private static void AddCompatibilityIssue(
        PolicyStrategyPackageManifestValue package,
        string manifestPath,
        ICollection<PolicyStrategyManifestIssue> issues)
    {
        if (package.Enabled && !TianShuExtensionManifestCommon.ApplyCompatibility(package))
        {
            issues.Add(new PolicyStrategyManifestIssue(
                $"policy_strategy_manifest.{TianShuExtensionManifestCommon.VersionIncompatibleIssueCode}",
                $"审批策略包不可用：{package.UnavailableReason}",
                manifestPath));
        }
    }

    private static PolicyStrategyManifestValue ReadStrategy(TomlTable table)
        => new()
        {
            Id = ReadString(table, "id") ?? string.Empty,
            DisplayName = ReadString(table, "display_name") ?? string.Empty,
            Enabled = ReadBoolean(table, "enabled") ?? true,
            Type = ReadString(table, "type") ?? "policy",
            Priority = ReadInteger(table, "priority") ?? 0,
            ApprovalPolicy = ReadString(table, "approval_policy"),
            SandboxMode = ReadString(table, "sandbox_mode"),
            NetworkAccess = ReadBoolean(table, "network_access"),
            AllowLoginShell = ReadBoolean(table, "allow_login_shell"),
            FileWritePolicy = ReadString(table, "file_write_policy"),
            WriteRequiresApprovalGlobs = ReadStringArray(table, "write_requires_approval_globs"),
            DangerousCommandPatterns = ReadStringArray(table, "dangerous_command_patterns"),
            CommandRules = ReadRuleArray(table, "command_rules", ReadCommandRule),
            NetworkRules = ReadRuleArray(table, "network_rules", ReadNetworkRule),
            AssemblyPath = ReadString(table, "assembly_path"),
            ProviderType = ReadString(table, "provider_type") ?? ReadString(table, "type_name"),
        };

    private static PolicyStrategyCommandRuleValue ReadCommandRule(TomlTable table)
        => new(
            Prefix: ReadStringArray(table, "prefix"),
            Decision: ReadString(table, "decision") ?? "ask",
            Reason: ReadString(table, "reason"));

    private static TomlTable WriteCommandRule(PolicyStrategyCommandRuleValue rule)
    {
        var table = new TomlTable();
        SetStringArray(table, "prefix", rule.Prefix);
        SetOptional(table, "decision", rule.Decision);
        SetOptional(table, "reason", rule.Reason);
        return table;
    }

    private static PolicyStrategyNetworkRuleValue ReadNetworkRule(TomlTable table)
        => new(
            Protocol: ReadString(table, "protocol") ?? "https",
            Host: ReadString(table, "host") ?? string.Empty,
            Decision: ReadString(table, "decision") ?? "ask",
            Reason: ReadString(table, "reason"));

    private static TomlTable WriteNetworkRule(PolicyStrategyNetworkRuleValue rule)
    {
        var table = new TomlTable();
        SetOptional(table, "protocol", rule.Protocol);
        SetOptional(table, "host", rule.Host);
        SetOptional(table, "decision", rule.Decision);
        SetOptional(table, "reason", rule.Reason);
        return table;
    }

    private static TomlTable ReadTomlTable(string path)
        => TomlTable.From(Toml.Parse(File.ReadAllText(path, Encoding.UTF8), path));

    private static string ResolveWritableManifestPath(string rootDirectory, string packageId)
    {
        var normalizedId = NormalizePackageId(packageId);
        var targetRoot = ResolvePolicyStrategyRootDirectory(rootDirectory);
        return Path.Combine(targetRoot, normalizedId, ManifestFileName);
    }

    private static string EnsureManifestInsidePolicyStrategyRoot(string rootDirectory, string manifestPath)
    {
        var root = Path.GetFullPath(rootDirectory);
        var fullPath = Path.GetFullPath(Path.IsPathFullyQualified(manifestPath) ? manifestPath : Path.Combine(root, manifestPath));
        var policyStrategyRoot = ResolvePolicyStrategyRootDirectory(root);
        if (IsUnderDirectory(fullPath, policyStrategyRoot))
        {
            return fullPath;
        }

        throw new InvalidOperationException("审批策略包 manifest 只能位于 TianShu modules/policies/strategies 目录内。");
    }

    private static IEnumerable<string> EnumeratePolicyStrategyRoots(string rootDirectory)
    {
        yield return ResolvePolicyStrategyRootDirectory(rootDirectory);
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
            throw new InvalidOperationException("审批策略包 id 只能包含字母、数字、下划线或短横线。");
        }

        return trimmed;
    }

    private static string NormalizeStrategyId(string id)
    {
        var trimmed = id.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)
            || trimmed.Any(static ch => !(char.IsLetterOrDigit(ch) || ch is '_' or '-')))
        {
            throw new InvalidOperationException("审批策略 id 只能包含字母、数字、下划线或短横线。");
        }

        return trimmed;
    }

    private static string? NormalizeOptionalScalar(string? value, string? fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

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

    private static void SetOptionalBoolean(TomlTable table, string key, bool? value)
    {
        if (value is null)
        {
            table.Remove(key);
            return;
        }

        table[key] = value.Value;
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

    private static void SetRuleArray<T>(TomlTable table, string key, IReadOnlyList<T> rules, Func<T, TomlTable> write)
    {
        if (rules.Count == 0)
        {
            table.Remove(key);
            return;
        }

        var array = new TomlTableArray();
        foreach (var rule in rules)
        {
            array.Add(write(rule));
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

    private static IReadOnlyList<T> ReadRuleArray<T>(TomlTable table, string key, Func<TomlTable, T> read)
    {
        if (!table.TryGetValue(key, out var value) || value is not TomlTableArray array)
        {
            return [];
        }

        return array
            .OfType<TomlTable>()
            .Select(read)
            .ToArray();
    }

    private static string GetDisplayName(string rootDirectory, string path)
    {
        var relative = Path.GetRelativePath(rootDirectory, path);
        return relative.StartsWith("..", StringComparison.Ordinal) ? path : relative;
    }
}

public sealed record PolicyStrategyManifestProjection(
    string RootDirectory,
    string PolicyStrategyRootDirectory,
    string? SelectedManifestPath,
    IReadOnlyList<PolicyStrategyManifestFileDescriptor> Files,
    PolicyStrategyPackageManifestValue? SelectedPackage,
    IReadOnlyList<PolicyStrategyManifestIssue> Issues);

public sealed record PolicyStrategyManifestFileDescriptor(
    string Path,
    string DisplayName)
{
    public bool IsSelected { get; init; }
}

public sealed record PolicyStrategyManifestIssue(string Code, string Message, string? Path);

public sealed record PolicyStrategyEffectiveDefaults(
    string? ApprovalPolicy,
    string? SandboxMode,
    bool? NetworkAccess,
    bool? AllowLoginShell);

public sealed class PolicyStrategyPackageManifestValue : ITianShuExtensionManifestMetadata
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

    public IReadOnlyList<PolicyStrategyManifestValue> Strategies { get; set; } = [];
}

public sealed class PolicyStrategyManifestValue
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string Type { get; set; } = "policy";

    public int Priority { get; set; }

    public string? ApprovalPolicy { get; set; }

    public string? SandboxMode { get; set; }

    public bool? NetworkAccess { get; set; }

    public bool? AllowLoginShell { get; set; }

    public string? FileWritePolicy { get; set; }

    public IReadOnlyList<string> WriteRequiresApprovalGlobs { get; set; } = [];

    public IReadOnlyList<string> DangerousCommandPatterns { get; set; } = [];

    public IReadOnlyList<PolicyStrategyCommandRuleValue> CommandRules { get; set; } = [];

    public IReadOnlyList<PolicyStrategyNetworkRuleValue> NetworkRules { get; set; } = [];

    public string? AssemblyPath { get; set; }

    public string? ProviderType { get; set; }
}

public sealed record PolicyStrategyCommandRuleValue(
    IReadOnlyList<string> Prefix,
    string Decision,
    string? Reason);

public sealed record PolicyStrategyNetworkRuleValue(
    string Protocol,
    string Host,
    string Decision,
    string? Reason);
