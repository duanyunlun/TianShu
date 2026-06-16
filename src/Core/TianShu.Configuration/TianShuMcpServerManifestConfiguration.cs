using System.Text;
using Tomlyn;
using Tomlyn.Model;

namespace TianShu.Configuration;

/// <summary>
/// 读取、扫描并写回 TianShu MCP Server 包 manifest。
/// Loads, scans, and updates TianShu MCP Server package manifests.
/// </summary>
public sealed class TianShuMcpServerManifestConfiguration
{
    public const string McpServerDirectoryName = "modules/mcp-servers";
    public const string LegacyMcpServerDirectoryName = "mcp-servers";
    public const string ManifestFileName = "server.toml";

    public McpServerManifestProjection Load(string rootDirectory, string? selectedManifestPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        var root = Path.GetFullPath(rootDirectory);
        var files = ScanMcpServerManifests(root);
        var selectedPath = ResolveSelectedManifest(root, selectedManifestPath, files);
        var issues = new List<McpServerManifestIssue>();
        McpServerPackageManifestValue? package = null;

        if (selectedPath is not null)
        {
            try
            {
                package = ReadPackage(selectedPath);
            }
            catch (Exception ex)
            {
                issues.Add(new McpServerManifestIssue(
                    "mcp_server_manifest.parse_failed",
                    $"无法解析 MCP Server 包 manifest：{ex.Message}",
                    selectedPath));
            }
        }

        return new McpServerManifestProjection(
            root,
            ResolveMcpServerRootDirectory(root),
            selectedPath,
            files.Select(file => file with { IsSelected = string.Equals(file.Path, selectedPath, StringComparison.OrdinalIgnoreCase) }).ToArray(),
            package,
            issues);
    }

    public IReadOnlyList<McpServerPackageManifestValue> LoadEnabledPackages(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        var root = Path.GetFullPath(rootDirectory);
        var packages = new List<McpServerPackageManifestValue>();
        foreach (var file in ScanMcpServerManifests(root))
        {
            try
            {
                var package = ReadPackage(file.Path);
                if (package.Enabled)
                {
                    packages.Add(package);
                }
            }
            catch
            {
                // 损坏的能力包 manifest 由调用方以保守方式跳过。
                // Broken capability manifests are conservatively skipped by runtime callers.
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
            throw new InvalidOperationException($"MCP Server 包 manifest 已存在：{manifestPath}");
        }

        var normalizedId = NormalizePackageId(packageId);
        var package = new McpServerPackageManifestValue
        {
            Id = normalizedId,
            DisplayName = normalizedId,
            Enabled = true,
            Type = "package",
            Priority = 0,
            ManifestPath = manifestPath,
            PackageDirectory = Path.GetDirectoryName(manifestPath)!,
            Servers = [],
        };

        SavePackage(manifestPath, package);
        return manifestPath;
    }

    public string CopyPackage(string rootDirectory, string sourceManifestPath, string packageId, bool overwrite = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceManifestPath);
        var sourcePath = EnsureManifestInsideMcpServerRoot(rootDirectory, sourceManifestPath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("源 MCP Server 包 manifest 不存在。", sourcePath);
        }

        var targetPath = ResolveWritableManifestPath(rootDirectory, packageId);
        if (File.Exists(targetPath) && !overwrite)
        {
            throw new InvalidOperationException($"MCP Server 包 manifest 已存在：{targetPath}");
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
        var fullPath = EnsureManifestInsideMcpServerRoot(rootDirectory, manifestPath);
        var packageDirectory = Path.GetDirectoryName(fullPath);
        if (string.Equals(Path.GetFileName(packageDirectory), "builtin", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("官方 builtin MCP Server 包不允许删除；可通过启用状态禁用或复制后替换。");
        }

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    public void SavePackage(string manifestPath, McpServerPackageManifestValue package)
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

        var servers = new TomlTableArray();
        foreach (var server in package.Servers.Where(static server => !string.IsNullOrWhiteSpace(server.Id)))
        {
            var serverTable = new TomlTable
            {
                ["id"] = NormalizeServerId(server.Id),
                ["enabled"] = server.Enabled,
                ["required"] = server.Required,
            };
            SetOptional(serverTable, "display_name", server.DisplayName);
            SetOptional(serverTable, "transport", server.Transport);
            SetOptional(serverTable, "command", server.Command);
            SetArray(serverTable, "args", server.Args);
            SetMap(serverTable, "env", server.Env);
            SetArray(serverTable, "env_vars", server.EnvVars);
            SetOptional(serverTable, "cwd", server.Cwd);
            SetOptional(serverTable, "url", server.Url);
            SetOptional(serverTable, "bearer_token_env_var", server.BearerTokenEnvVar);
            SetMap(serverTable, "http_headers", server.HttpHeaders);
            SetMap(serverTable, "env_http_headers", server.EnvHttpHeaders);
            SetOptionalNumber(serverTable, "startup_timeout_ms", server.StartupTimeoutMs);
            SetOptionalNumber(serverTable, "tool_timeout_ms", server.ToolTimeoutMs);
            SetArray(serverTable, "enabled_tools", server.EnabledTools);
            SetArray(serverTable, "disabled_tools", server.DisabledTools);
            servers.Add(serverTable);
        }

        if (servers.Count > 0)
        {
            root["servers"] = servers;
        }
        else
        {
            root.Remove("servers");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, Toml.FromModel(root).TrimEnd() + Environment.NewLine, Encoding.UTF8);
    }

    public static string ResolveRootDirectory(string TianShuConfigPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(TianShuConfigPath);
        return Path.GetDirectoryName(Path.GetFullPath(TianShuConfigPath)) ?? Environment.CurrentDirectory;
    }

    public static string ResolveMcpServerRootDirectory(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        return TianShuHomePathUtilities.ResolveModulePathFromHome(rootDirectory, "mcp-servers");
    }

    public static string ResolveServerCwdFullPath(McpServerPackageManifestValue package, McpServerManifestValue server)
    {
        if (string.IsNullOrWhiteSpace(server.Cwd))
        {
            return string.Empty;
        }

        return Path.IsPathFullyQualified(server.Cwd)
            ? Path.GetFullPath(server.Cwd)
            : Path.GetFullPath(Path.Combine(package.PackageDirectory, server.Cwd));
    }

    private static IReadOnlyList<McpServerManifestFileDescriptor> ScanMcpServerManifests(string rootDirectory)
    {
        return EnumerateMcpServerRoots(rootDirectory)
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateDirectories(root)
                .Select(directory => Path.Combine(directory, ManifestFileName))
                .Where(File.Exists))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new McpServerManifestFileDescriptor(
                Path.GetFullPath(path),
                GetDisplayName(rootDirectory, path)))
            .OrderBy(static file => file.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ResolveSelectedManifest(
        string rootDirectory,
        string? selectedManifestPath,
        IReadOnlyList<McpServerManifestFileDescriptor> files)
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

        return files.FirstOrDefault(static file => file.DisplayName.Contains("mcp-servers", StringComparison.OrdinalIgnoreCase)
                                                  && file.DisplayName.Contains("builtin", StringComparison.OrdinalIgnoreCase))?.Path
               ?? files.First().Path;
    }

    private static McpServerPackageManifestValue ReadPackage(string manifestPath)
    {
        var fullPath = Path.GetFullPath(manifestPath);
        var packageDirectory = Path.GetDirectoryName(fullPath)!;
        var root = ReadTomlTable(fullPath);
        var packageId = ReadString(root, "id") ?? Path.GetFileName(packageDirectory);
        var package = new McpServerPackageManifestValue
        {
            Id = packageId,
            DisplayName = ReadString(root, "display_name") ?? packageId,
            Enabled = ReadBoolean(root, "enabled") ?? true,
            Type = ReadString(root, "type") ?? "package",
            Priority = ReadInteger(root, "priority") ?? 0,
            ManifestPath = fullPath,
            PackageDirectory = packageDirectory,
        };

        if (root.TryGetValue("servers", out var serversValue) && serversValue is TomlTableArray serverArray)
        {
            package.Servers = serverArray
                .OfType<TomlTable>()
                .Select(ReadServer)
                .Where(static server => !string.IsNullOrWhiteSpace(server.Id))
                .ToArray();
        }

        return package;
    }

    private static McpServerManifestValue ReadServer(TomlTable table)
        => new()
        {
            Id = ReadString(table, "id") ?? string.Empty,
            DisplayName = ReadString(table, "display_name") ?? string.Empty,
            Enabled = ReadBoolean(table, "enabled") ?? true,
            Required = ReadBoolean(table, "required") ?? false,
            Transport = ReadString(table, "transport") ?? string.Empty,
            Command = ReadString(table, "command"),
            Args = ReadStringArray(table, "args"),
            Env = ReadStringMap(table, "env"),
            EnvVars = ReadStringArray(table, "env_vars"),
            Cwd = ReadString(table, "cwd"),
            Url = ReadString(table, "url"),
            BearerTokenEnvVar = ReadString(table, "bearer_token_env_var") ?? ReadString(table, "bearer_token_env"),
            HttpHeaders = ReadStringMap(table, "http_headers"),
            EnvHttpHeaders = ReadStringMap(table, "env_http_headers"),
            StartupTimeoutMs = ReadInteger(table, "startup_timeout_ms") ?? ReadSecondsAsMilliseconds(table, "startup_timeout_sec"),
            ToolTimeoutMs = ReadInteger(table, "tool_timeout_ms") ?? ReadSecondsAsMilliseconds(table, "tool_timeout_sec"),
            EnabledTools = ReadStringArray(table, "enabled_tools"),
            DisabledTools = ReadStringArray(table, "disabled_tools"),
        };

    private static TomlTable ReadTomlTable(string path)
        => TomlTable.From(Toml.Parse(File.ReadAllText(path, Encoding.UTF8), path));

    private static string ResolveWritableManifestPath(string rootDirectory, string packageId)
    {
        var normalizedId = NormalizePackageId(packageId);
        var targetRoot = ResolveMcpServerRootDirectory(rootDirectory);
        return Path.Combine(targetRoot, normalizedId, ManifestFileName);
    }

    private static string EnsureManifestInsideMcpServerRoot(string rootDirectory, string manifestPath)
    {
        var root = Path.GetFullPath(rootDirectory);
        var fullPath = Path.GetFullPath(Path.IsPathFullyQualified(manifestPath) ? manifestPath : Path.Combine(root, manifestPath));
        var manifestRoot = ResolveMcpServerRootDirectory(root);
        if (IsUnderDirectory(fullPath, manifestRoot))
        {
            return fullPath;
        }

        throw new InvalidOperationException("MCP Server 包 manifest 只能位于 TianShu modules/mcp-servers 目录内。");
    }

    private static IEnumerable<string> EnumerateMcpServerRoots(string rootDirectory)
    {
        yield return ResolveMcpServerRootDirectory(rootDirectory);
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePackageId(string id)
        => NormalizeIdentifier(id, "MCP Server 包 id");

    private static string NormalizeServerId(string id)
        => NormalizeIdentifier(id, "MCP Server id");

    private static string NormalizeIdentifier(string id, string label)
    {
        var trimmed = id.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)
            || trimmed.Any(static ch => !(char.IsLetterOrDigit(ch) || ch is '_' or '-')))
        {
            throw new InvalidOperationException($"{label} 只能包含字母、数字、下划线或短横线。");
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

    private static void SetOptionalNumber(TomlTable table, string key, int? value)
    {
        if (value is null)
        {
            table.Remove(key);
            return;
        }

        table[key] = value.Value;
    }

    private static void SetArray(TomlTable table, string key, IReadOnlyList<string> values)
    {
        var normalized = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .ToArray();
        if (normalized.Length == 0)
        {
            table.Remove(key);
            return;
        }

        table[key] = normalized;
    }

    private static void SetMap(TomlTable table, string key, IReadOnlyDictionary<string, string> values)
    {
        var map = new TomlTable();
        foreach (var (name, value) in values)
        {
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(value))
            {
                map[name.Trim()] = value.Trim();
            }
        }

        if (map.Count == 0)
        {
            table.Remove(key);
            return;
        }

        table[key] = map;
    }

    private static string? ReadString(TomlTable table, string key)
        => table.TryGetValue(key, out var value) ? value as string : null;

    private static bool? ReadBoolean(TomlTable table, string key)
        => table.TryGetValue(key, out var value) ? value as bool? : null;

    private static int? ReadInteger(TomlTable table, string key)
        => table.TryGetValue(key, out var value) && value is not null ? Convert.ToInt32(value) : null;

    private static int? ReadSecondsAsMilliseconds(TomlTable table, string key)
    {
        if (!table.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        var seconds = Convert.ToDouble(value);
        return seconds < 0 ? null : Convert.ToInt32(seconds * 1000);
    }

    private static IReadOnlyList<string> ReadStringArray(TomlTable table, string key)
        => table.TryGetValue(key, out var value) && value is IEnumerable<object> items
            ? items.Select(static item => item as string).Where(static item => !string.IsNullOrWhiteSpace(item)).Select(static item => item!).ToArray()
            : [];

    private static IReadOnlyDictionary<string, string> ReadStringMap(TomlTable table, string key)
    {
        if (!table.TryGetValue(key, out var value) || value is not TomlTable map)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return map.Where(static pair => pair.Value is string)
            .ToDictionary(static pair => pair.Key, static pair => (string)pair.Value!, StringComparer.OrdinalIgnoreCase);
    }

    private static string GetDisplayName(string rootDirectory, string path)
    {
        var relative = Path.GetRelativePath(rootDirectory, path);
        return relative.StartsWith("..", StringComparison.Ordinal) ? path : relative;
    }
}

public sealed record McpServerManifestProjection(
    string RootDirectory,
    string McpServerRootDirectory,
    string? SelectedManifestPath,
    IReadOnlyList<McpServerManifestFileDescriptor> Files,
    McpServerPackageManifestValue? SelectedPackage,
    IReadOnlyList<McpServerManifestIssue> Issues);

public sealed record McpServerManifestFileDescriptor(
    string Path,
    string DisplayName)
{
    public bool IsSelected { get; init; }
}

public sealed record McpServerManifestIssue(string Code, string Message, string? Path);

public sealed class McpServerPackageManifestValue
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string Type { get; set; } = "package";

    public int Priority { get; set; }

    public string ManifestPath { get; set; } = string.Empty;

    public string PackageDirectory { get; set; } = string.Empty;

    public IReadOnlyList<McpServerManifestValue> Servers { get; set; } = [];
}

public sealed class McpServerManifestValue
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public bool Required { get; set; }

    public string Transport { get; set; } = string.Empty;

    public string? Command { get; set; }

    public IReadOnlyList<string> Args { get; set; } = [];

    public IReadOnlyDictionary<string, string> Env { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> EnvVars { get; set; } = [];

    public string? Cwd { get; set; }

    public string? Url { get; set; }

    public string? BearerTokenEnvVar { get; set; }

    public IReadOnlyDictionary<string, string> HttpHeaders { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> EnvHttpHeaders { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public int? StartupTimeoutMs { get; set; }

    public int? ToolTimeoutMs { get; set; }

    public IReadOnlyList<string> EnabledTools { get; set; } = [];

    public IReadOnlyList<string> DisabledTools { get; set; } = [];
}
