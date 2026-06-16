using System.Text;
using TianShu.Contracts.Configuration;
using Tomlyn;
using Tomlyn.Model;

namespace TianShu.Configuration;

/// <summary>
/// 读取、扫描并写回 TianShu prompt TOML 配置。
/// Loads, scans, and updates TianShu prompt TOML configuration files.
/// </summary>
public sealed class TianShuPromptTomlConfiguration
{
    public const string PromptPackModuleDirectoryName = "modules/prompts";
    private const string PromptPackDirectoryName = "prompts";
    private const string PromptPackManifestFileName = "prompt.toml";

    private static readonly PromptSectionDefinition[] SectionDefinitions =
    [
        new("base", "基础指令", "TianShu 的基础行为指令。", ["base"], "text", true, true),
        new("developer", "开发者指令", "开发者级行为边界与工作方式。", ["developer"], "text", true, true),
        new("language_policy", "语言策略", "默认回复语言与语言切换规则。", ["language_policy"], "text", true, true),
        new("tools.apply_patch", "apply_patch 工具指令", "文件补丁工具的专用开发者指令。", ["tools", "apply_patch"], "text", true, true),
        new("collaboration.default", "默认协作模式", "默认协作模式下的附加行为指令。", ["collaboration", "default"], "text", true, true),
        new("collaboration.plan", "Plan 协作模式", "Plan 模式下的附加行为指令。", ["collaboration", "plan"], "text", true, true),
        new("model_status.reasoning_probe_prompt", "模型状态探针", "/model-route status 使用的 reasoning/connectivity 探针。", ["model_status"], "reasoning_probe_prompt", false, false),
        new("review.uncommitted_changes.prompt", "未提交变更 Review Prompt", "审查工作区未提交变更时使用的 prompt 模板。", ["review", "uncommitted_changes"], "prompt", false, false),
        new("review.base_branch.prompt", "基线分支 Review Prompt", "审查相对基线分支变更时使用的 prompt 模板。", ["review", "base_branch"], "prompt", false, false),
        new("review.commit.prompt", "提交 Review Prompt", "审查指定提交时使用的 prompt 模板。", ["review", "commit"], "prompt", false, false),
        new("review.context.intro", "Review 上下文引导", "自动采集 diff/context 时插入的说明。", ["review", "context"], "intro", false, false),
        new("realtime.startup_context_header", "Realtime 上下文标题", "Realtime 启动上下文标题。", ["realtime"], "startup_context_header", false, false),
        new("realtime.start_instructions", "Realtime 开始指令", "Realtime 会话开始时注入的指令。", ["realtime"], "start_instructions", false, false),
        new("realtime.end_instructions", "Realtime 结束指令", "Realtime 会话结束时注入的指令。", ["realtime"], "end_instructions", false, false),
        new("plugins.explicit_capability.template", "插件显式能力模板", "用户显式提及插件时注入的能力说明模板。", ["plugins", "explicit_capability"], "template", false, false),
    ];

    public IReadOnlyList<PromptConfigurationSectionDescriptor> Sections { get; } = SectionDefinitions
        .Select(static section => new PromptConfigurationSectionDescriptor
        {
            Key = section.Key,
            DisplayName = section.DisplayName,
            Description = section.Description,
            SupportsEnabled = section.SupportsEnabled,
            SupportsMode = section.SupportsMode,
        })
        .ToArray();

    public PromptConfigurationProjection Load(string rootDirectory, string? selectedFilePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        var root = Path.GetFullPath(rootDirectory);
        var files = ScanPromptTomlFiles(root);
        var selectedPath = ResolveSelectedPromptFile(root, selectedFilePath, files);
        var issues = new List<ConfigurationIssue>();
        var values = SectionDefinitions
            .Select(static section => new PromptConfigurationSectionValue { Key = section.Key, IsConfigured = false })
            .ToArray();

        if (selectedPath is not null)
        {
            var selectedFile = files.FirstOrDefault(file => string.Equals(file.Path, selectedPath, StringComparison.OrdinalIgnoreCase));
            if (selectedFile is not null
                && string.Equals(selectedFile.LoadStatus, TianShuExtensionManifestCommon.LoadStatusUnavailable, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ConfigurationIssue
                {
                    Severity = ConfigurationIssueSeverity.Error,
                    Code = $"prompt.toml.{TianShuExtensionManifestCommon.VersionIncompatibleIssueCode}",
                    Message = $"Prompt Pack 不可用：{selectedFile.UnavailableReason}",
                    FieldKey = selectedPath,
                });
            }

            try
            {
                var table = ReadTomlTable(selectedPath);
                values = SectionDefinitions.Select(section => ReadSectionValue(table, section)).ToArray();
            }
            catch (Exception ex)
            {
                issues.Add(new ConfigurationIssue
                {
                    Severity = ConfigurationIssueSeverity.Error,
                    Code = "prompt.toml.parse_failed",
                    Message = $"无法解析 Prompt 配置文件：{ex.Message}",
                    FieldKey = selectedPath,
                });
            }
        }

        return new PromptConfigurationProjection
        {
            RootDirectory = root,
            SelectedFilePath = selectedPath,
            Files = files
                .Select(file => file with { IsSelected = string.Equals(file.Path, selectedPath, StringComparison.OrdinalIgnoreCase) })
                .ToArray(),
            Sections = Sections,
            Values = values,
            Issues = issues,
        };
    }

    public void SaveSection(string promptFilePath, PromptConfigurationSectionChange change)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(promptFilePath);
        ArgumentNullException.ThrowIfNull(change);

        var definition = SectionDefinitions.FirstOrDefault(section => string.Equals(section.Key, change.SectionKey, StringComparison.OrdinalIgnoreCase))
                         ?? throw new InvalidOperationException($"未知 Prompt 配置段：{change.SectionKey}");
        var fullPath = Path.GetFullPath(promptFilePath);
        if (!IsPromptPackManifestPath(fullPath))
        {
            throw new InvalidOperationException("Prompt 文件必须位于 modules/prompts/<package>/prompt.toml，且扩展名必须为 .toml。");
        }

        var root = File.Exists(fullPath) ? ReadTomlTable(fullPath) : new TomlTable();
        EnsurePromptPackMetadata(root, fullPath);
        var section = GetOrCreateSection(root, definition.Path);

        if (definition.SupportsEnabled)
        {
            section["enabled"] = change.Enabled;
        }

        if (definition.SupportsMode)
        {
            section["mode"] = ToTomlMode(change.Mode);
        }

        section[definition.TextKey] = change.Text ?? string.Empty;
        section.Remove("file");

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, TomlWriter.Write(root).TrimEnd() + Environment.NewLine, Encoding.UTF8);
    }

    public string CreatePromptFile(string rootDirectory, string fileName, bool overwrite = false)
    {
        var targetPath = ResolvePromptFilePath(rootDirectory, fileName);
        if (File.Exists(targetPath) && !overwrite)
        {
            throw new InvalidOperationException($"Prompt Pack 已存在：{targetPath}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var root = new TomlTable();
        EnsurePromptPackMetadata(root, targetPath);
        File.WriteAllText(targetPath, TomlWriter.Write(root).TrimEnd() + Environment.NewLine, Encoding.UTF8);
        return targetPath;
    }

    public string CopyPromptFile(string rootDirectory, string sourceFilePath, string targetFileName, bool overwrite = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        var sourcePath = EnsurePromptFileWithinRoot(rootDirectory, sourceFilePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("源 Prompt 文件不存在。", sourcePath);
        }

        var targetPath = ResolvePromptFilePath(rootDirectory, targetFileName);
        if (File.Exists(targetPath) && !overwrite)
        {
            throw new InvalidOperationException($"Prompt Pack 已存在：{targetPath}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Copy(sourcePath, targetPath, overwrite);
        return targetPath;
    }

    public void DeletePromptFile(string rootDirectory, string promptFilePath)
    {
        var fullPath = EnsurePromptFileWithinRoot(rootDirectory, promptFilePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    public static string ResolveRootDirectory(string TianShuConfigPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(TianShuConfigPath);
        return Path.GetDirectoryName(Path.GetFullPath(TianShuConfigPath)) ?? Environment.CurrentDirectory;
    }

    private static string ResolvePromptFilePath(string rootDirectory, string packageIdOrManifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageIdOrManifestPath);

        var root = Path.GetFullPath(rootDirectory);
        var normalized = packageIdOrManifestPath.Trim().Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized)
            || normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Any(static segment => segment == ".."))
        {
            throw new InvalidOperationException("Prompt Pack ID 或 manifest 路径必须是 TianShu 根目录下的相对路径。");
        }

        string combined;
        var segments = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (IsRelativePromptManifestSegments(segments))
        {
            combined = segments.Aggregate(root, static (path, segment) => Path.Combine(path, segment));
        }
        else
        {
            var packageId = NormalizePromptPackageId(normalized);
            combined = Path.Combine(root, "modules", PromptPackDirectoryName, packageId, PromptPackManifestFileName);
        }

        return EnsurePromptFileWithinRoot(root, combined);
    }

    private static string EnsurePromptFileWithinRoot(string rootDirectory, string promptFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(promptFilePath);

        var root = Path.GetFullPath(rootDirectory);
        var fullPath = Path.GetFullPath(Path.IsPathRooted(promptFilePath)
            ? promptFilePath
            : Path.Combine(root, promptFilePath));
        var rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var isPromptPackManifest = IsPromptPackManifestUnder(root, fullPath, Path.Combine("modules", PromptPackDirectoryName));

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            || !isPromptPackManifest
            || !string.Equals(Path.GetExtension(fullPath), ".toml", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Prompt 文件必须位于 modules/prompts/<package>/prompt.toml，且扩展名必须为 .toml。");
        }

        return fullPath;
    }

    private static IReadOnlyList<PromptConfigurationFileDescriptor> ScanPromptTomlFiles(string rootDirectory)
    {
        var candidates = new List<string>();
        var promptPackDirectory = ResolvePromptModuleRootDirectory(rootDirectory);
        if (Directory.Exists(promptPackDirectory))
        {
            candidates.AddRange(Directory
                .EnumerateDirectories(promptPackDirectory)
                .Select(directory => Path.Combine(directory, PromptPackManifestFileName))
                .Where(File.Exists));
        }

        return candidates
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(IsValidTomlFile)
            .OrderBy(path => GetPromptDisplayName(rootDirectory, path), StringComparer.OrdinalIgnoreCase)
            .Select(path => BuildPromptFileDescriptor(rootDirectory, path))
            .ToArray();
    }

    private static PromptConfigurationFileDescriptor BuildPromptFileDescriptor(string rootDirectory, string path)
    {
        var table = ReadTomlTable(path);
        var metadata = new PromptPackManifestMetadata
        {
            MinTianShuVersion = ReadString(table, "min_tianshu_version"),
        };
        TianShuExtensionManifestCommon.ApplyCompatibility(metadata);
        return new PromptConfigurationFileDescriptor
        {
            Path = path,
            DisplayName = GetPromptDisplayName(rootDirectory, path),
            Version = ReadString(table, "version"),
            MinTianShuVersion = metadata.MinTianShuVersion,
            Capabilities = TianShuExtensionManifestCommon.ReadStringArray(table, "capabilities"),
            Diagnostics = TianShuExtensionManifestCommon.ReadStringArray(table, "diagnostics"),
            LoadStatus = metadata.LoadStatus,
            UnavailableReason = metadata.UnavailableReason,
        };
    }

    private static string? ResolveSelectedPromptFile(
        string rootDirectory,
        string? selectedFilePath,
        IReadOnlyList<PromptConfigurationFileDescriptor> files)
    {
        if (!string.IsNullOrWhiteSpace(selectedFilePath))
        {
            var fullSelectedPath = Path.GetFullPath(Path.IsPathRooted(selectedFilePath)
                ? selectedFilePath
                : Path.Combine(rootDirectory, selectedFilePath));
            if (files.Any(file => string.Equals(file.Path, fullSelectedPath, StringComparison.OrdinalIgnoreCase)))
            {
                return fullSelectedPath;
            }
        }

        var defaultPromptPath = Path.Combine(rootDirectory, "modules", PromptPackDirectoryName, "default", PromptPackManifestFileName);
        var defaultPrompt = files.FirstOrDefault(file => string.Equals(file.Path, defaultPromptPath, StringComparison.OrdinalIgnoreCase));
        if (defaultPrompt is not null)
        {
            return defaultPrompt.Path;
        }

        return files.FirstOrDefault()?.Path;
    }

    private static bool IsValidTomlFile(string path)
    {
        try
        {
            _ = ReadTomlTable(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static TomlTable ReadTomlTable(string path)
        => TomlTable.From(Toml.Parse(File.ReadAllText(path, Encoding.UTF8), path));

    private static string NormalizePromptPackageId(string packageId)
    {
        if (packageId.Contains(Path.DirectorySeparatorChar) || packageId.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidOperationException("Prompt Pack ID 只能是单层目录名，例如 default 或 team-prompts。");
        }

        var normalized = Path.GetFileNameWithoutExtension(packageId.Trim());
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || string.Equals(normalized, PromptPackDirectoryName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, PromptPackManifestFileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Prompt Pack ID 只能是单层目录名，例如 default 或 team-prompts。");
        }

        return normalized;
    }

    private static bool IsPromptPackManifestPath(string fullPath)
    {
        return string.Equals(Path.GetFileName(fullPath), PromptPackManifestFileName, StringComparison.OrdinalIgnoreCase)
            && HasPromptRootAncestor(fullPath, Path.Combine("modules", PromptPackDirectoryName));
    }

    public static string ResolveDefaultPromptFilePath(string rootDirectory)
        => Path.Combine(Path.GetFullPath(rootDirectory), "modules", PromptPackDirectoryName, "default", PromptPackManifestFileName);

    public static string ResolvePromptModuleRootDirectory(string rootDirectory)
        => Path.Combine(Path.GetFullPath(rootDirectory), "modules", PromptPackDirectoryName);

    private static bool IsRelativePromptManifestSegments(IReadOnlyList<string> segments)
        => segments.Count == 4
                && string.Equals(segments[0], "modules", StringComparison.OrdinalIgnoreCase)
                && string.Equals(segments[1], PromptPackDirectoryName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(segments[3], PromptPackManifestFileName, StringComparison.OrdinalIgnoreCase);

    private static bool IsPromptPackManifestUnder(string root, string fullPath, string relativeRoot)
    {
        var promptPackDirectory = Path.Combine(root, relativeRoot);
        var promptPackWithSeparator = promptPackDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(promptPackWithSeparator, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(fullPath), PromptPackManifestFileName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetDirectoryName(Path.GetDirectoryName(fullPath) ?? string.Empty), promptPackDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasPromptRootAncestor(string fullPath, string relativeRoot)
    {
        var expected = relativeRoot
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        var parent = Path.GetDirectoryName(Path.GetDirectoryName(fullPath) ?? string.Empty);
        if (string.IsNullOrWhiteSpace(parent))
        {
            return false;
        }

        return parent.EndsWith(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsurePromptPackMetadata(TomlTable root, string manifestPath)
    {
        var packageId = Path.GetFileName(Path.GetDirectoryName(manifestPath)) ?? "default";
        if (!root.ContainsKey("id"))
        {
            root["id"] = packageId;
        }

        if (!root.ContainsKey("display_name"))
        {
            root["display_name"] = packageId;
        }

        if (!root.ContainsKey("enabled"))
        {
            root["enabled"] = true;
        }

        if (!root.ContainsKey("type"))
        {
            root["type"] = "package";
        }

        if (!root.ContainsKey("priority"))
        {
            root["priority"] = 0;
        }
    }

    private static PromptConfigurationSectionValue ReadSectionValue(TomlTable root, PromptSectionDefinition definition)
    {
        var section = GetSection(root, definition.Path);
        if (section is null)
        {
            return new PromptConfigurationSectionValue { Key = definition.Key, IsConfigured = false };
        }

        return new PromptConfigurationSectionValue
        {
            Key = definition.Key,
            Enabled = ReadBoolean(section, "enabled") ?? true,
            Mode = ReadMode(section, "mode") ?? PromptConfigurationSectionMergeMode.Append,
            Text = ReadString(section, definition.TextKey),
            File = ReadString(section, "file"),
            IsConfigured = section.ContainsKey(definition.TextKey)
                || section.ContainsKey("file")
                || section.ContainsKey("enabled")
                || section.ContainsKey("mode"),
        };
    }

    private static TomlTable? GetSection(TomlTable root, IReadOnlyList<string> path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (!current.TryGetValue(segment, out var value) || value is not TomlTable table)
            {
                return null;
            }

            current = table;
        }

        return current;
    }

    private static TomlTable GetOrCreateSection(TomlTable root, IReadOnlyList<string> path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (current.TryGetValue(segment, out var value) && value is TomlTable table)
            {
                current = table;
                continue;
            }

            var created = new TomlTable();
            current[segment] = created;
            current = created;
        }

        return current;
    }

    private static string? ReadString(TomlTable table, string key)
        => table.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static bool? ReadBoolean(TomlTable table, string key)
        => table.TryGetValue(key, out var value) && value is bool boolean ? boolean : null;

    private static PromptConfigurationSectionMergeMode? ReadMode(TomlTable table, string key)
        => ReadString(table, key)?.Trim().ToLowerInvariant() switch
        {
            "replace" => PromptConfigurationSectionMergeMode.Replace,
            "append" => PromptConfigurationSectionMergeMode.Append,
            "prepend" => PromptConfigurationSectionMergeMode.Prepend,
            _ => null,
        };

    private static string ToTomlMode(PromptConfigurationSectionMergeMode mode)
        => mode switch
        {
            PromptConfigurationSectionMergeMode.Replace => "replace",
            PromptConfigurationSectionMergeMode.Prepend => "prepend",
            _ => "append",
        };

    private static string GetPromptDisplayName(string rootDirectory, string path)
    {
        var relative = Path.GetRelativePath(rootDirectory, path);
        return relative.StartsWith("..", StringComparison.Ordinal)
            ? path
            : relative;
    }

    private sealed record PromptSectionDefinition(
        string Key,
        string DisplayName,
        string Description,
        IReadOnlyList<string> Path,
        string TextKey,
        bool SupportsEnabled,
        bool SupportsMode);

    private sealed class PromptPackManifestMetadata : ITianShuExtensionManifestMetadata
    {
        public string? Version { get; set; }

        public string? MinTianShuVersion { get; set; }

        public IReadOnlyList<string> Capabilities { get; set; } = [];

        public IReadOnlyList<string> Diagnostics { get; set; } = [];

        public string LoadStatus { get; set; } = TianShuExtensionManifestCommon.LoadStatusAvailable;

        public string? UnavailableReason { get; set; }
    }

    private static class TomlWriter
    {
        public static string Write(TomlTable root)
        {
            var builder = new StringBuilder();
            WriteTable(builder, root, prefix: null);
            return builder.ToString();
        }

        private static void WriteTable(StringBuilder builder, TomlTable table, string? prefix)
        {
            foreach (var pair in table.Where(static pair => pair.Value is not TomlTable and not TomlTableArray))
            {
                builder.Append(pair.Key);
                builder.Append(" = ");
                builder.AppendLine(FormatValue(pair.Value));
            }

            foreach (var pair in table.Where(static pair => pair.Value is TomlTable))
            {
                if (builder.Length > 0 && builder[^1] != '\n')
                {
                    builder.AppendLine();
                }

                var tablePrefix = string.IsNullOrWhiteSpace(prefix) ? pair.Key : $"{prefix}.{pair.Key}";
                builder.AppendLine();
                builder.Append('[');
                builder.Append(tablePrefix);
                builder.AppendLine("]");
                WriteTable(builder, (TomlTable)pair.Value!, tablePrefix);
            }
        }

        private static string FormatValue(object? value)
            => value switch
            {
                bool boolean => boolean ? "true" : "false",
                string text when text.Contains('\n', StringComparison.Ordinal) => "\"\"\"" + text.Replace("\"\"\"", "\\\"\\\"\\\"", StringComparison.Ordinal) + "\"\"\"",
                string text => $"\"{EscapeString(text)}\"",
                null => "\"\"",
                _ => value.ToString() ?? "\"\"",
            };

        private static string EscapeString(string text)
            => text
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
