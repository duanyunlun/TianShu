using TianShu.Contracts.Primitives;
using Tomlyn;
using Tomlyn.Model;

namespace TianShu.Configuration;

/// <summary>
/// TianShu 已知模块配置对象的正式 TOML 路径映射。
/// Maps known TianShu module configuration objects to their canonical TOML paths.
/// </summary>
public static class TianShuKnownModuleConfigurationPaths
{
    private static readonly ModuleConfigurationPathDefinition[] DefaultLayerDefinitions =
    [
        Definition("model_route_sets.", ["model", "route-sets"], "模型路由方案"),
        Definition("model_protocol_rule_sets.", ["model", "protocol-rules"], "默认模型协议规则"),
        Definition("agents.", ["agent", "agents"], "Agent 配置"),
        Definition("execution_profiles.", ["agent", "execution-profiles"], "执行配置文件"),
        Definition("session_profiles.", ["agent", "session-profiles"], "会话配置文件"),
        Definition("conversation_profiles.", ["agent", "conversation-profiles"], "对话配置文件"),
        Definition("memory_profiles.", ["memory", "profiles"], "记忆配置文件"),
        Definition("memory.spaces.", ["memory", "spaces"], "记忆空间"),
        Definition("memory.providers.", ["memory", "providers"], "记忆提供方"),
        Definition("memory.bindings.", ["memory", "bindings"], "记忆绑定"),
        Definition("tool_profiles.", ["tools", "profiles"], "工具配置文件"),
        Definition("workspace_profiles.", ["workspace", "profiles"], "工作空间配置文件"),
        Definition("permission_profiles.", ["policies", "permission-profiles"], "审批配置文件"),
        Definition("governance_profiles.", ["policies", "governance-profiles"], "治理配置文件"),
        Definition("sandboxes.", ["policies", "sandboxes"], "沙箱配置"),
        Definition("tui_profiles.", ["experience", "tui-profiles"], "TUI 配置文件"),
        Definition("feature_profiles.", ["experience", "feature-profiles"], "功能开关配置文件"),
        Definition("realtime_profiles.", ["experience", "realtime-profiles"], "实时交互配置文件"),
        Definition("identity_profiles.", ["identity", "profiles"], "身份配置文件"),
        Definition("accounts.", ["identity", "accounts"], "账号配置"),
        Definition("devices.", ["identity", "devices"], "设备配置"),
        Definition("collaboration_profiles.", ["collaboration", "profiles"], "协作配置文件"),
        Definition("workflow_profiles.", ["collaboration", "workflow-profiles"], "工作流配置文件"),
    ];

    public static IReadOnlyList<ModuleConfigurationPathDefinition> DefaultLayerModuleDefinitions => DefaultLayerDefinitions;

    public static IReadOnlyList<string> EnumerateDefaultLayerModuleFiles(string configPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);

        var paths = new List<string>();
        foreach (var definition in DefaultLayerDefinitions)
        {
            var directory = ResolveDirectory(configPath, definition);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            paths.AddRange(Directory
                .EnumerateFiles(directory, "*.toml")
                .Order(StringComparer.OrdinalIgnoreCase));
        }

        return paths
            .Distinct(GetPathComparer())
            .ToArray();
    }

    public static bool TryResolveWriteTargetPath(string configPath, string key, out string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);

        targetPath = string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (key.StartsWith("providers.", StringComparison.OrdinalIgnoreCase))
        {
            targetPath = ResolveProviderInstancePath(configPath);
            return true;
        }

        if (string.Equals(key, "memory.enabled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "memory.default_profile", StringComparison.OrdinalIgnoreCase))
        {
            targetPath = Path.Combine(
                TianShuHomePathUtilities.ResolveModulePathFromConfig(configPath, "memory", "profiles"),
                $"{ResolveMemoryProfileId(configPath)}.toml");
            return true;
        }

        foreach (var definition in DefaultLayerDefinitions)
        {
            if (!TryExtractObjectId(key, definition.KeyPrefix, out var id))
            {
                continue;
            }

            targetPath = Path.Combine(ResolveDirectory(configPath, definition), $"{id}.toml");
            return true;
        }

        return false;
    }

    public static IReadOnlyList<string> ResolveProviderInstanceModulePaths(
        string configPath,
        IReadOnlyDictionary<string, StructuredValue>? rootValues = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);

        var directory = TianShuHomePathUtilities.ResolveModulePathFromConfig(configPath, "model", "provider-instances");
        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        var configured = ReadConfiguredString(rootValues, "provider_instances") ?? ReadConfiguredString(configPath, "provider_instances");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var selectedPath = Path.Combine(directory, $"{configured!.Trim()}.toml");
            return File.Exists(selectedPath) ? [selectedPath] : Array.Empty<string>();
        }

        return Directory
            .EnumerateFiles(directory, "*.toml")
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string ResolveProviderInstancePath(string configPath)
    {
        var directory = TianShuHomePathUtilities.ResolveModulePathFromConfig(configPath, "model", "provider-instances");
        var configured = ReadConfiguredString(configPath, "provider_instances");
        return Path.Combine(directory, $"{(string.IsNullOrWhiteSpace(configured) ? "default" : configured!.Trim())}.toml");
    }

    private static string ResolveMemoryProfileId(string configPath)
    {
        var configured = ReadConfiguredString(configPath, "memory.default_profile");
        return string.IsNullOrWhiteSpace(configured) ? "default" : configured!.Trim();
    }

    private static string ResolveDirectory(string configPath, ModuleConfigurationPathDefinition definition)
        => TianShuHomePathUtilities.ResolveModulePathFromConfig(configPath, definition.ModuleSegments.ToArray());

    private static bool TryExtractObjectId(string key, string prefix, out string id)
    {
        id = string.Empty;
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = key[prefix.Length..];
        var dotIndex = rest.IndexOf('.', StringComparison.Ordinal);
        if (dotIndex == 0)
        {
            return false;
        }

        id = dotIndex < 0 ? rest : rest[..dotIndex];
        return !string.IsNullOrWhiteSpace(id);
    }

    private static string? ReadConfiguredString(IReadOnlyDictionary<string, StructuredValue>? values, string key)
    {
        if (values is null || !values.TryGetValue(key, out var value))
        {
            return null;
        }

        return value.Kind == StructuredValueKind.String ? Normalize(value.StringValue) : Normalize(value.ToPlainObject()?.ToString());
    }

    private static string? ReadConfiguredString(string path, string key)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var root = TomlTable.From(Toml.Parse(File.ReadAllText(path), path));
            if (!TryReadTomlPath(root, key.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), out var value))
            {
                return null;
            }

            return Normalize(value?.ToString());
        }
        catch
        {
            return null;
        }
    }

    private static bool TryReadTomlPath(TomlTable root, IReadOnlyList<string> segments, out object? value)
    {
        value = null;
        TomlTable current = root;
        for (var index = 0; index < segments.Count; index++)
        {
            if (!current.TryGetValue(segments[index], out var currentValue))
            {
                return false;
            }

            if (index == segments.Count - 1)
            {
                value = currentValue;
                return true;
            }

            if (currentValue is not TomlTable child)
            {
                return false;
            }

            current = child;
        }

        return false;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static StringComparer GetPathComparer()
        => OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static ModuleConfigurationPathDefinition Definition(string keyPrefix, string[] moduleSegments, string displayName)
        => new(keyPrefix, moduleSegments, displayName);
}

public sealed record ModuleConfigurationPathDefinition(
    string KeyPrefix,
    IReadOnlyList<string> ModuleSegments,
    string DisplayName);
