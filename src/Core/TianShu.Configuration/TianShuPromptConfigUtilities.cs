namespace TianShu.Configuration;

/// <summary>
/// Prompt 配置段合并模式。
/// Merge mode used by configurable prompt sections.
/// </summary>
public enum TianShuPromptMergeMode
{
    Replace = 0,
    Append = 1,
    Prepend = 2,
}

/// <summary>
/// 单个 prompt 段的解析结果。
/// Resolved prompt section content.
/// </summary>
public sealed record TianShuPromptSection(
    bool Enabled,
    TianShuPromptMergeMode Mode,
    string? Text);

/// <summary>
/// 已解析的 TianShu prompt 配置。
/// Resolved TianShu prompt configuration.
/// </summary>
public sealed record TianShuPromptConfiguration(
    TianShuPromptSection? Base,
    TianShuPromptSection? Developer,
    TianShuPromptSection? LanguagePolicy,
    TianShuPromptSection? ApplyPatch,
    TianShuPromptSection? CollaborationDefault,
    TianShuPromptSection? CollaborationPlan,
    string? ModelStatusReasoningProbePrompt,
    string? ReviewUncommittedChangesPrompt,
    string? ReviewBaseBranchPrompt,
    string? ReviewCommitPrompt,
    string? ReviewContextIntro,
    string? RealtimeStartupContextHeader,
    string? RealtimeStartInstructions,
    string? RealtimeEndInstructions,
    string? PluginExplicitCapabilityTemplate)
{
    public static TianShuPromptConfiguration Empty { get; } = new(
        Base: null,
        Developer: null,
        LanguagePolicy: null,
        ApplyPatch: null,
        CollaborationDefault: null,
        CollaborationPlan: null,
        ModelStatusReasoningProbePrompt: null,
        ReviewUncommittedChangesPrompt: null,
        ReviewBaseBranchPrompt: null,
        ReviewCommitPrompt: null,
        ReviewContextIntro: null,
        RealtimeStartupContextHeader: null,
        RealtimeStartInstructions: null,
        RealtimeEndInstructions: null,
        PluginExplicitCapabilityTemplate: null);
}

/// <summary>
/// 可参与 prompt 文件定位的配置层。
/// Config layer metadata used for prompt-file discovery.
/// </summary>
public sealed record TianShuPromptConfigLayer(
    string? Path,
    string? DirectoryPath,
    Dictionary<string, object?> Config,
    bool IsDisabled,
    bool FileExists);

/// <summary>
/// TianShu prompt TOML 读取、归一化与合并辅助件。
/// Helpers for loading and normalizing TianShu prompt TOML files.
/// </summary>
public static class TianShuPromptConfigUtilities
{
    public const string PromptConfigKey = "prompt_config";
    private const string PromptPackDirectoryName = "prompts";
    private const string PromptPackManifestFileName = "prompt.toml";

    public static TianShuPromptConfiguration FromConfig(IReadOnlyDictionary<string, object?>? config)
    {
        if (config is null)
        {
            return TianShuPromptConfiguration.Empty;
        }

        var root = TryReadDictionary(config, PromptConfigKey);
        if (root is null)
        {
            return TianShuPromptConfiguration.Empty;
        }

        return new TianShuPromptConfiguration(
            Base: ReadSection(root, "base"),
            Developer: ReadSection(root, "developer"),
            LanguagePolicy: ReadSection(root, "language_policy"),
            ApplyPatch: ReadSection(root, "tools", "apply_patch"),
            CollaborationDefault: ReadSection(root, "collaboration", "default"),
            CollaborationPlan: ReadSection(root, "collaboration", "plan"),
            ModelStatusReasoningProbePrompt: ReadNestedString(root, ["model_status"], "reasoning_probe_prompt"),
            ReviewUncommittedChangesPrompt: ReadNestedString(root, ["review", "uncommitted_changes"], "prompt"),
            ReviewBaseBranchPrompt: ReadNestedString(root, ["review", "base_branch"], "prompt"),
            ReviewCommitPrompt: ReadNestedString(root, ["review", "commit"], "prompt"),
            ReviewContextIntro: ReadNestedString(root, ["review", "context"], "intro"),
            RealtimeStartupContextHeader: ReadNestedString(root, ["realtime"], "startup_context_header"),
            RealtimeStartInstructions: ReadNestedString(root, ["realtime"], "start_instructions"),
            RealtimeEndInstructions: ReadNestedString(root, ["realtime"], "end_instructions"),
            PluginExplicitCapabilityTemplate: ReadNestedString(root, ["plugins", "explicit_capability"], "template"));
    }

    public static string? ApplySection(TianShuPromptSection? section, string? builtInDefault)
    {
        if (section is null)
        {
            return Normalize(builtInDefault);
        }

        if (!section.Enabled)
        {
            return null;
        }

        var sectionText = Normalize(section.Text);
        var fallback = Normalize(builtInDefault);
        if (sectionText is null)
        {
            return fallback;
        }

        if (fallback is null || section.Mode == TianShuPromptMergeMode.Replace)
        {
            return sectionText;
        }

        return section.Mode == TianShuPromptMergeMode.Prepend
            ? JoinSections(sectionText, fallback)
            : JoinSections(fallback, sectionText);
    }

    public static void ApplyPromptConfigLayer(
        Dictionary<string, object?> effectiveConfig,
        IReadOnlyList<TianShuPromptConfigLayer> layers,
        string? cwd)
    {
        ArgumentNullException.ThrowIfNull(effectiveConfig);
        ArgumentNullException.ThrowIfNull(layers);

        var mergedPromptConfig = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var packageConfig in LoadPromptPackageConfigObjects(layers, cwd))
        {
            MergeDictionaries(mergedPromptConfig, packageConfig);
        }

        if (mergedPromptConfig.Count > 0)
        {
            effectiveConfig[PromptConfigKey] = mergedPromptConfig;
        }
    }

    private static Dictionary<string, object?> BuildPromptConfigObject(
        Dictionary<string, object?> promptRoot,
        string promptDirectory)
    {
        var merged = CloneWithoutMetaSections(promptRoot);
        var profileName = Normalize(ReadString(promptRoot, "profile"))
                          ?? "default";
        if (TryReadDictionary(promptRoot, "profiles") is { } profiles
            && TryReadDictionary(profiles, profileName) is { } profile)
        {
            MergeDictionaries(merged, profile);
        }

        ResolveSectionFile(merged, promptDirectory, ["base"]);
        ResolveSectionFile(merged, promptDirectory, ["developer"]);
        ResolveSectionFile(merged, promptDirectory, ["language_policy"]);
        ResolveSectionFile(merged, promptDirectory, ["tools", "apply_patch"]);
        ResolveSectionFile(merged, promptDirectory, ["collaboration", "default"]);
        ResolveSectionFile(merged, promptDirectory, ["collaboration", "plan"]);
        ResolveInlineFileKey(merged, promptDirectory, ["realtime"], "start_instructions_file", "start_instructions");
        ResolveInlineFileKey(merged, promptDirectory, ["realtime"], "end_instructions_file", "end_instructions");
        return merged;
    }

    private static IReadOnlyList<Dictionary<string, object?>> LoadPromptPackageConfigObjects(
        IReadOnlyList<TianShuPromptConfigLayer> layers,
        string? cwd)
    {
        var packages = new List<PromptPackageConfigObject>();
        for (var layerIndex = 0; layerIndex < layers.Count; layerIndex++)
        {
            var layer = layers[layerIndex];
            if (layer.IsDisabled)
            {
                continue;
            }

            var baseDirectory = ResolveLayerBaseDirectory(layer, cwd);
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                continue;
            }

            var packageRoot = Path.Combine(baseDirectory!, "modules", PromptPackDirectoryName);
            if (!Directory.Exists(packageRoot))
            {
                continue;
            }

            foreach (var packageDirectory in Directory.EnumerateDirectories(packageRoot))
            {
                var manifestPath = Path.Combine(packageDirectory, PromptPackManifestFileName);
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                var root = TianShuConfigObjectUtilities.ReadTomlConfigObject(manifestPath, suppressErrors: false);
                if (ReadBoolean(root, "enabled") == false)
                {
                    continue;
                }

                packages.Add(new PromptPackageConfigObject(
                    layerIndex,
                    ReadInteger(root, "priority") ?? 0,
                    manifestPath,
                    root));
            }
        }

        return packages
            .OrderBy(static package => package.LayerIndex)
            .ThenBy(static package => package.Priority)
            .ThenBy(static package => package.Path, StringComparer.OrdinalIgnoreCase)
            .Select(package => BuildPromptConfigObject(
                package.Root,
                Path.GetDirectoryName(package.Path) ?? Environment.CurrentDirectory))
            .Where(static promptConfig => promptConfig.Count > 0)
            .ToArray();
    }

    private static string? ResolveLayerBaseDirectory(TianShuPromptConfigLayer layer, string? cwd)
    {
        if (!string.IsNullOrWhiteSpace(layer.Path))
        {
            return Path.GetDirectoryName(layer.Path!);
        }

        if (!string.IsNullOrWhiteSpace(layer.DirectoryPath))
        {
            return layer.DirectoryPath;
        }

        return Normalize(cwd);
    }

    private static void ResolveSectionFile(Dictionary<string, object?> root, string promptDirectory, IReadOnlyList<string> path)
    {
        if (GetNestedDictionary(root, path) is not { } section)
        {
            return;
        }

        var file = Normalize(ReadString(section, "file"));
        if (file is null)
        {
            return;
        }

        section["text"] = ReadPromptTextFile(promptDirectory, file);
    }

    private static void ResolveInlineFileKey(
        Dictionary<string, object?> root,
        string promptDirectory,
        IReadOnlyList<string> path,
        string sourceKey,
        string targetKey)
    {
        if (GetNestedDictionary(root, path) is not { } section)
        {
            return;
        }

        var file = Normalize(ReadString(section, sourceKey));
        if (file is null)
        {
            return;
        }

        section[targetKey] = ReadPromptTextFile(promptDirectory, file);
    }

    private static string ReadPromptTextFile(string baseDirectory, string file)
    {
        var resolvedPath = Path.IsPathRooted(file)
            ? Path.GetFullPath(file)
            : Path.GetFullPath(Path.Combine(baseDirectory, file));
        if (!File.Exists(resolvedPath))
        {
            throw new InvalidOperationException($"prompt 引用的文件不存在：{resolvedPath}");
        }

        var text = File.ReadAllText(resolvedPath);
        var normalized = Normalize(text);
        if (normalized is null)
        {
            throw new InvalidOperationException($"prompt 引用的文件为空：{resolvedPath}");
        }

        return normalized;
    }

    private static TianShuPromptSection? ReadSection(IReadOnlyDictionary<string, object?> root, params string[] path)
    {
        if (GetNestedDictionary(root, path) is not { } section)
        {
            return null;
        }

        var enabled = ReadBoolean(section, "enabled") ?? true;
        var mode = Normalize(ReadString(section, "mode"))?.ToLowerInvariant() switch
        {
            "append" => TianShuPromptMergeMode.Append,
            "prepend" => TianShuPromptMergeMode.Prepend,
            _ => TianShuPromptMergeMode.Replace,
        };
        return new TianShuPromptSection(enabled, mode, Normalize(ReadString(section, "text")));
    }

    private static string? ReadNestedString(
        IReadOnlyDictionary<string, object?> root,
        IReadOnlyList<string> path,
        string key)
        => GetNestedDictionary(root, path) is { } section
            ? Normalize(ReadString(section, key))
            : null;

    private static Dictionary<string, object?> CloneWithoutMetaSections(Dictionary<string, object?> source)
    {
        var clone = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in source)
        {
            if (string.Equals(key, "version", StringComparison.Ordinal)
                || string.Equals(key, "id", StringComparison.Ordinal)
                || string.Equals(key, "display_name", StringComparison.Ordinal)
                || string.Equals(key, "enabled", StringComparison.Ordinal)
                || string.Equals(key, "type", StringComparison.Ordinal)
                || string.Equals(key, "priority", StringComparison.Ordinal)
                || string.Equals(key, "profile", StringComparison.Ordinal)
                || string.Equals(key, "profiles", StringComparison.Ordinal))
            {
                continue;
            }

            clone[key] = TianShuConfigObjectUtilities.CloneConfigValue(value);
        }

        return clone;
    }

    private static void MergeDictionaries(Dictionary<string, object?> target, Dictionary<string, object?> source)
    {
        foreach (var (key, value) in source)
        {
            if (value is Dictionary<string, object?> sourceChild
                && target.TryGetValue(key, out var existing)
                && existing is Dictionary<string, object?> targetChild)
            {
                MergeDictionaries(targetChild, sourceChild);
                continue;
            }

            target[key] = TianShuConfigObjectUtilities.CloneConfigValue(value);
        }
    }

    private static Dictionary<string, object?>? GetNestedDictionary(
        IReadOnlyDictionary<string, object?> root,
        IReadOnlyList<string> path)
    {
        Dictionary<string, object?>? current = null;
        IReadOnlyDictionary<string, object?> cursor = root;
        foreach (var segment in path)
        {
            if (!cursor.TryGetValue(segment, out var value) || !TryAsDictionary(value, out current))
            {
                return null;
            }

            cursor = current;
        }

        return current;
    }

    private static Dictionary<string, object?>? TryReadDictionary(IReadOnlyDictionary<string, object?> root, string key)
        => root.TryGetValue(key, out var value) && TryAsDictionary(value, out var dictionary)
            ? dictionary
            : null;

    private static bool TryAsDictionary(object? value, out Dictionary<string, object?> dictionary)
    {
        dictionary = value as Dictionary<string, object?> ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        return value is Dictionary<string, object?>;
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> root, string key)
    {
        if (!root.TryGetValue(key, out var value))
        {
            return null;
        }

        return value switch
        {
            string text => text,
            char character => character.ToString(),
            bool boolean => boolean.ToString(),
            int or long or double or float or decimal => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
            _ => null,
        };
    }

    private static bool? ReadBoolean(IReadOnlyDictionary<string, object?> root, string key)
    {
        if (!root.TryGetValue(key, out var value))
        {
            return null;
        }

        return value switch
        {
            bool boolean => boolean,
            string text when bool.TryParse(text, out var parsed) => parsed,
            _ => null,
        };
    }

    private static int? ReadInteger(IReadOnlyDictionary<string, object?> root, string key)
    {
        if (!root.TryGetValue(key, out var value))
        {
            return null;
        }

        return value switch
        {
            int integer => integer,
            long number when number >= int.MinValue && number <= int.MaxValue => (int)number,
            string text when int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
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

    private static string JoinSections(string first, string second)
        => $"{first}{Environment.NewLine}{Environment.NewLine}{second}";

    private sealed record PromptPackageConfigObject(
        int LayerIndex,
        int Priority,
        string Path,
        Dictionary<string, object?> Root);
}
