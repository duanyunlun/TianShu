using System.Text.Json;
using TianShu.AppHost.Configuration;
using TianShu.Contracts.Tools;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed record KernelToolImplementationSelection(
    string ToolKey,
    string? ProviderId,
    string? ImplementationId,
    ToolImplementationKind? ImplementationKind,
    int Priority,
    string? FallbackStrategy);

internal sealed class KernelToolProfileOptions
{
    private static readonly KernelToolProfileOptions EmptyInstance = new(
        toolsEnabled: true,
        activeProfileId: null,
        toolProfileId: null,
        enabledToolKeys: [],
        disabledToolKeys: [],
        implementationSelections: new Dictionary<string, KernelToolImplementationSelection>(StringComparer.Ordinal));

    private readonly HashSet<string> enabledToolKeys;
    private readonly HashSet<string> disabledToolKeys;
    private readonly Dictionary<string, KernelToolImplementationSelection> implementationSelections;

    private KernelToolProfileOptions(
        bool toolsEnabled,
        string? activeProfileId,
        string? toolProfileId,
        IEnumerable<string> enabledToolKeys,
        IEnumerable<string> disabledToolKeys,
        Dictionary<string, KernelToolImplementationSelection> implementationSelections)
    {
        ToolsEnabled = toolsEnabled;
        ActiveProfileId = Normalize(activeProfileId);
        ToolProfileId = Normalize(toolProfileId);
        this.enabledToolKeys = NormalizeKeys(enabledToolKeys);
        this.disabledToolKeys = NormalizeKeys(disabledToolKeys);
        this.implementationSelections = implementationSelections;
    }

    public bool ToolsEnabled { get; }

    public string? ActiveProfileId { get; }

    public string? ToolProfileId { get; }

    public IReadOnlySet<string> EnabledToolKeys => enabledToolKeys;

    public IReadOnlySet<string> DisabledToolKeys => disabledToolKeys;

    public IReadOnlyDictionary<string, KernelToolImplementationSelection> ImplementationSelections => implementationSelections;

    public static KernelToolProfileOptions Empty => EmptyInstance;

    public static KernelToolProfileOptions FromConfigValues(IReadOnlyDictionary<string, object?>? values)
        => FromConfigValues(NormalizeConfigValues(values));

    public static KernelToolProfileOptions FromConfigValues(IReadOnlyDictionary<string, string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return Empty;
        }

        var activeProfile = ReadConfigString(values, "profile") ?? "default";
        var toolProfile = ReadConfigString(values, $"profiles.{activeProfile}.tools")
                          ?? ReadConfigString(values, "tool_profile", "tools.profile");
        var enabledTools = new HashSet<string>(StringComparer.Ordinal);
        var disabledTools = new HashSet<string>(StringComparer.Ordinal);
        var toolsEnabled = ReadConfigBoolean(values, "tools.enabled") ?? true;
        AddRange(enabledTools, ReadConfigStringArray(values, $"tool_profiles.{toolProfile}.enabled", $"tool_profiles.{toolProfile}.enabled_tools"));
        AddRange(disabledTools, ReadConfigStringArray(values, $"tool_profiles.{toolProfile}.disabled", $"tool_profiles.{toolProfile}.disabled_tools"));

        var selections = new Dictionary<string, KernelToolImplementationSelection>(StringComparer.Ordinal);
        foreach (var key in values.Keys)
        {
            if (!TryReadToolKeyFromConfigPath(key, out var toolKey))
            {
                continue;
            }

            if (ReadConfigBoolean(values, $"tools.{toolKey}.enabled") is { } enabled)
            {
                if (enabled)
                {
                    enabledTools.Add(toolKey);
                    disabledTools.Remove(toolKey);
                }
                else
                {
                    disabledTools.Add(toolKey);
                    enabledTools.Remove(toolKey);
                }
            }

            if (!selections.ContainsKey(toolKey))
            {
                var selection = ReadImplementationSelection(values, toolKey);
                if (selection is not null)
                {
                    selections[toolKey] = selection;
                }
            }
        }

        return new KernelToolProfileOptions(toolsEnabled, activeProfile, toolProfile, enabledTools, disabledTools, selections);
    }

    internal static IReadOnlyDictionary<string, string>? NormalizeConfigValues(IReadOnlyDictionary<string, object?>? values)
    {
        if (values is null || values.Count == 0)
        {
            return null;
        }

        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            if (string.IsNullOrWhiteSpace(key) || value is null)
            {
                continue;
            }

            var text = NormalizeConfigValue(value);
            if (!string.IsNullOrWhiteSpace(text))
            {
                normalized[key] = text;
            }
        }

        return normalized;
    }

    public static KernelToolProfileOptions FromTomlText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Empty;
        }

        var sections = ParseTomlSections(text);
        sections.TryGetValue(string.Empty, out var root);
        var activeProfile = ReadTomlScalar(root, "profile") ?? "default";
        var toolProfile = ReadTomlScalar(GetSection(sections, $"profiles.{activeProfile}"), "tools")
                          ?? ReadTomlScalar(root, "tool_profile")
                          ?? ReadTomlScalar(GetSection(sections, "tools"), "profile");
        var toolsSection = GetSection(sections, "tools");
        var toolsEnabled = ReadTomlBoolean(toolsSection, "enabled") ?? true;
        var enabledTools = new HashSet<string>(StringComparer.Ordinal);
        var disabledTools = new HashSet<string>(StringComparer.Ordinal);
        var toolProfileSection = GetSection(sections, $"tool_profiles.{toolProfile}");
        AddRange(enabledTools, ReadTomlStringArray(toolProfileSection, "enabled") ?? ReadTomlStringArray(toolProfileSection, "enabled_tools"));
        AddRange(disabledTools, ReadTomlStringArray(toolProfileSection, "disabled") ?? ReadTomlStringArray(toolProfileSection, "disabled_tools"));

        var selections = new Dictionary<string, KernelToolImplementationSelection>(StringComparer.Ordinal);
        foreach (var section in sections)
        {
            if (!section.Key.StartsWith("tools.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var toolKey = Normalize(section.Key["tools.".Length..]);
            if (string.IsNullOrWhiteSpace(toolKey))
            {
                continue;
            }

            if (ReadTomlBoolean(section.Value, "enabled") is { } enabled)
            {
                if (enabled)
                {
                    enabledTools.Add(toolKey);
                    disabledTools.Remove(toolKey);
                }
                else
                {
                    disabledTools.Add(toolKey);
                    enabledTools.Remove(toolKey);
                }
            }

            var selection = ReadImplementationSelection(toolKey, section.Value);
            if (selection is not null)
            {
                selections[toolKey] = selection;
            }
        }

        return new KernelToolProfileOptions(toolsEnabled, activeProfile, toolProfile, enabledTools, disabledTools, selections);
    }

    public bool TryGetDisabledReason(string toolKey, string? handlerName, out string? reason)
    {
        var normalizedToolKey = Normalize(toolKey);
        var normalizedHandlerName = Normalize(handlerName);
        if (!ToolsEnabled)
        {
            reason = "disabled by tools.enabled";
            return true;
        }

        if (Contains(disabledToolKeys, normalizedToolKey) || Contains(disabledToolKeys, normalizedHandlerName))
        {
            reason = "disabled by tool profile";
            return true;
        }

        if (enabledToolKeys.Count > 0
            && !Contains(enabledToolKeys, normalizedToolKey)
            && !Contains(enabledToolKeys, normalizedHandlerName))
        {
            reason = "not enabled by tool profile";
            return true;
        }

        reason = null;
        return false;
    }

    public bool TryGetImplementationSelection(string toolKey, out KernelToolImplementationSelection? selection)
    {
        var normalized = Normalize(toolKey);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            selection = null;
            return false;
        }

        return implementationSelections.TryGetValue(normalized, out selection);
    }

    public bool IsSelectedImplementation(string toolKey, ToolImplementationBinding binding)
    {
        if (!TryGetImplementationSelection(toolKey, out var selection) || selection is null)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(selection.ImplementationId)
            && string.Equals(selection.ImplementationId, binding.ImplementationId, StringComparison.Ordinal))
        {
            return true;
        }

        if (selection.ImplementationKind.HasValue && selection.ImplementationKind.Value == binding.ImplementationKind)
        {
            return true;
        }

        return string.IsNullOrWhiteSpace(selection.ImplementationId) && !selection.ImplementationKind.HasValue;
    }

    private static Dictionary<string, string> GetSection(
        IReadOnlyDictionary<string, Dictionary<string, string>> sections,
        string? sectionName)
    {
        if (string.IsNullOrWhiteSpace(sectionName))
        {
            return [];
        }

        return sections.TryGetValue(sectionName, out var section) ? section : [];
    }

    private static Dictionary<string, Dictionary<string, string>> ParseTomlSections(string text)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            [string.Empty] = new(StringComparer.OrdinalIgnoreCase),
        };
        var currentSection = string.Empty;
        foreach (var rawLine in text.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim();
                if (!sections.ContainsKey(currentSection))
                {
                    sections[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                continue;
            }

            var equalIndex = line.IndexOf('=');
            if (equalIndex <= 0)
            {
                continue;
            }

            sections[currentSection][line[..equalIndex].Trim()] = line[(equalIndex + 1)..].Trim();
        }

        return sections;
    }

    private static KernelToolImplementationSelection? ReadImplementationSelection(
        IReadOnlyDictionary<string, string> values,
        string toolKey)
    {
        var providerId = ReadConfigString(values, $"tools.{toolKey}.provider", $"tools.{toolKey}.provider_id");
        var implementationId = ReadConfigString(values, $"tools.{toolKey}.implementation_id");
        var implementationKind = ParseImplementationKind(ReadConfigString(values, $"tools.{toolKey}.implementation_kind"));
        var fallback = ReadConfigString(values, $"tools.{toolKey}.fallback", $"tools.{toolKey}.fallback_strategy");
        var priority = ReadConfigInt(values, $"tools.{toolKey}.priority") ?? 0;
        return string.IsNullOrWhiteSpace(providerId)
               && string.IsNullOrWhiteSpace(implementationId)
               && implementationKind is null
               && string.IsNullOrWhiteSpace(fallback)
               && priority == 0
            ? null
            : new KernelToolImplementationSelection(toolKey, providerId, implementationId, implementationKind, priority, fallback);
    }

    private static KernelToolImplementationSelection? ReadImplementationSelection(
        string toolKey,
        IReadOnlyDictionary<string, string> section)
    {
        var providerId = ReadTomlScalar(section, "provider") ?? ReadTomlScalar(section, "provider_id");
        var implementationId = ReadTomlScalar(section, "implementation_id");
        var implementationKind = ParseImplementationKind(ReadTomlScalar(section, "implementation_kind"));
        var fallback = ReadTomlScalar(section, "fallback") ?? ReadTomlScalar(section, "fallback_strategy");
        var priority = ReadTomlInt(section, "priority") ?? 0;
        return string.IsNullOrWhiteSpace(providerId)
               && string.IsNullOrWhiteSpace(implementationId)
               && implementationKind is null
               && string.IsNullOrWhiteSpace(fallback)
               && priority == 0
            ? null
            : new KernelToolImplementationSelection(toolKey, providerId, implementationId, implementationKind, priority, fallback);
    }

    private static bool TryReadToolKeyFromConfigPath(string path, out string toolKey)
    {
        toolKey = string.Empty;
        if (!path.StartsWith("tools.", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "tools.enabled", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = path["tools.".Length..];
        var dotIndex = rest.IndexOf('.');
        if (dotIndex <= 0)
        {
            return false;
        }

        toolKey = rest[..dotIndex];
        return !string.IsNullOrWhiteSpace(toolKey);
    }

    private static string? ReadConfigString(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!values.TryGetValue(key, out var raw))
            {
                continue;
            }

            var value = ReadJsonScalar(raw);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool? ReadConfigBoolean(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var raw) && bool.TryParse(ReadJsonScalar(raw), out var value) ? value : null;

    private static int? ReadConfigInt(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var raw) && int.TryParse(ReadJsonScalar(raw), out var value) ? value : null;

    private static IReadOnlyList<string>? ReadConfigStringArray(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!values.TryGetValue(key, out var raw))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(raw);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    return document.RootElement.EnumerateArray()
                        .Select(static item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText())
                        .Where(static item => !string.IsNullOrWhiteSpace(item))
                        .Select(static item => item!)
                        .ToArray();
                }
            }
            catch (JsonException)
            {
                return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
        }

        return null;
    }

    private static string? ReadJsonScalar(string raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.String => Normalize(document.RootElement.GetString()),
                JsonValueKind.Number => document.RootElement.GetRawText(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                _ => null,
            };
        }
        catch (JsonException)
        {
            return Normalize(raw.Trim().Trim('"', '\''));
        }
    }

    private static string? NormalizeConfigValue(object value)
    {
        if (value is string text)
        {
            return text;
        }

        if (value is JsonElement element)
        {
            return element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : element.GetRawText();
        }

        try
        {
            return JsonSerializer.Serialize(value);
        }
        catch (NotSupportedException)
        {
            return value.ToString();
        }
    }

    private static string? ReadTomlScalar(IReadOnlyDictionary<string, string>? section, string key)
        => section is not null && section.TryGetValue(key, out var raw)
            ? KernelTomlTextParsingUtilities.ReadScalarConfigValue(raw)
            : null;

    private static bool? ReadTomlBoolean(IReadOnlyDictionary<string, string>? section, string key)
        => bool.TryParse(ReadTomlScalar(section, key), out var value) ? value : null;

    private static int? ReadTomlInt(IReadOnlyDictionary<string, string>? section, string key)
        => int.TryParse(ReadTomlScalar(section, key), out var value) ? value : null;

    private static IReadOnlyList<string>? ReadTomlStringArray(IReadOnlyDictionary<string, string>? section, string key)
        => section is not null && section.TryGetValue(key, out var raw)
            ? KernelTomlTextParsingUtilities.TryReadTomlSectionStringArray(section, key)
            : null;

    private static ToolImplementationKind? ParseImplementationKind(string? value)
        => Enum.TryParse<ToolImplementationKind>(Normalize(value), ignoreCase: true, out var kind) ? kind : null;

    private static void AddRange(HashSet<string> target, IEnumerable<string>? values)
    {
        if (values is null)
        {
            return;
        }

        foreach (var value in values)
        {
            var normalized = Normalize(value);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                target.Add(normalized);
            }
        }
    }

    private static bool Contains(IReadOnlySet<string> values, string? value)
        => !string.IsNullOrWhiteSpace(value) && values.Contains(value);

    private static HashSet<string> NormalizeKeys(IEnumerable<string> values)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        AddRange(result, values);
        return result;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
