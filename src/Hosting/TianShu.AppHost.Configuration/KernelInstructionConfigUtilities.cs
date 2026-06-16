using System.Text.Json;

namespace TianShu.AppHost.Configuration;

/// <summary>
/// instruction file 与 active-profile 宿主配置辅助件。
/// Host-side helpers for instruction files and active-profile config lookups.
/// </summary>
internal static class KernelInstructionConfigUtilities
{
    public static bool HasExperimentalInstructionsFile(KernelConfigReadSnapshot snapshot)
        => snapshot.OrderedLayers.Any(static layer => ContainsExperimentalInstructionsFile(layer.Config));

    public static string? LoadInstructionFileIfConfigured(
        KernelConfigReadSnapshot snapshot,
        string? keyPath,
        string? configuredPath,
        string cwd,
        string settingName)
    {
        var normalizedPath = NormalizeConfigText(configuredPath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return null;
        }

        var resolvedPath = ResolveConfiguredInstructionFilePath(snapshot, keyPath, normalizedPath!, cwd);
        if (!File.Exists(resolvedPath))
        {
            throw new InvalidOperationException($"{settingName} 指向的文件不存在：{resolvedPath}");
        }

        string content;
        try
        {
            content = File.ReadAllText(resolvedPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new InvalidOperationException($"无法读取 {settingName}：{resolvedPath}", ex);
        }

        var normalizedContent = NormalizeConfigText(content);
        if (string.IsNullOrWhiteSpace(normalizedContent))
        {
            throw new InvalidOperationException($"{settingName} 指向的文件为空：{resolvedPath}");
        }

        return normalizedContent;
    }

    public static string ResolveConfiguredInstructionFilePath(
        KernelConfigReadSnapshot snapshot,
        string? keyPath,
        string configuredPath,
        string cwd)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        var baseDirectory = ResolveConfiguredInstructionBaseDirectory(snapshot, keyPath, cwd);
        return Path.GetFullPath(Path.Combine(baseDirectory, configuredPath));
    }

    public static string? ReadConfiguredStringWithActiveProfile(
        Dictionary<string, object?> config,
        out string? keyPath,
        params string[] propertyNames)
    {
        var activeProfile = NormalizeConfigText(ReadStringExact(config, "profile"));
        if (!string.IsNullOrWhiteSpace(activeProfile)
            && TryReadObjectExact(config, "profiles", out var profiles)
            && TryReadObjectExact(profiles, activeProfile!, out var profileConfig))
        {
            if (TryReadStringExact(profileConfig, out var profileText, out var matchedPropertyName, propertyNames))
            {
                keyPath = $"profiles.{activeProfile}.{matchedPropertyName}";
                return profileText;
            }
        }

        if (TryReadStringExact(config, out var text, out var rootMatchedPropertyName, propertyNames))
        {
            keyPath = rootMatchedPropertyName;
            return text;
        }

        keyPath = null;
        return null;
    }

    public static string? ReadConfiguredNestedStringWithActiveProfile(
        Dictionary<string, object?> config,
        params string[][] propertyPaths)
    {
        var activeProfile = NormalizeConfigText(ReadStringExact(config, "profile"));
        if (!string.IsNullOrWhiteSpace(activeProfile)
            && TryReadObjectExact(config, "profiles", out var profiles)
            && TryReadObjectExact(profiles, activeProfile!, out var profileConfig))
        {
            foreach (var propertyPath in propertyPaths)
            {
                if (TryReadNestedValueExact(profileConfig, propertyPath, out var profileValue)
                    && TryReadString(profileValue, out var profileText))
                {
                    return profileText;
                }
            }
        }

        foreach (var propertyPath in propertyPaths)
        {
            if (TryReadNestedValueExact(config, propertyPath, out var rawValue)
                && TryReadString(rawValue, out var text))
            {
                return text;
            }
        }

        return null;
    }

    public static bool? ReadConfiguredNestedBooleanWithActiveProfile(
        Dictionary<string, object?> config,
        params string[][] propertyPaths)
    {
        var activeProfile = NormalizeConfigText(ReadStringExact(config, "profile"));
        if (!string.IsNullOrWhiteSpace(activeProfile)
            && TryReadObjectExact(config, "profiles", out var profiles)
            && TryReadObjectExact(profiles, activeProfile!, out var profileConfig))
        {
            foreach (var propertyPath in propertyPaths)
            {
                if (TryReadNestedValueExact(profileConfig, propertyPath, out var profileValue)
                    && TryReadBoolean(profileValue, out var profileBoolean))
                {
                    return profileBoolean;
                }
            }
        }

        return ReadBooleanExact(config, propertyPaths);
    }

    private static bool ContainsExperimentalInstructionsFile(Dictionary<string, object?> config)
    {
        if (config.ContainsKey("experimental_instructions_file"))
        {
            return true;
        }

        if (!TryReadObjectExact(config, "profiles", out var profiles))
        {
            return false;
        }

        foreach (var profile in profiles.Values)
        {
            if (!TryAsDictionary(profile, out var profileConfig))
            {
                continue;
            }

            if (profileConfig.ContainsKey("experimental_instructions_file"))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ReadStringExact(Dictionary<string, object?> config, string propertyName)
        => TryReadValueExact(config, propertyName, out var rawValue)
           && TryReadString(rawValue, out var value)
            ? value
            : null;

    private static bool TryReadStringExact(
        Dictionary<string, object?> config,
        out string value,
        out string? matchedPropertyName,
        params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (TryReadValueExact(config, propertyName, out var rawValue)
                && TryReadString(rawValue, out value))
            {
                matchedPropertyName = propertyName;
                return true;
            }
        }

        value = string.Empty;
        matchedPropertyName = null;
        return false;
    }

    private static bool? ReadBooleanExact(Dictionary<string, object?> config, params string[][] propertyPaths)
    {
        foreach (var propertyPath in propertyPaths)
        {
            if (TryReadNestedValueExact(config, propertyPath, out var rawValue)
                && TryReadBoolean(rawValue, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool TryReadObjectExact(
        Dictionary<string, object?> config,
        string propertyName,
        out Dictionary<string, object?> value)
    {
        if (TryReadValueExact(config, propertyName, out var rawValue)
            && TryAsDictionary(rawValue, out value))
        {
            return true;
        }

        value = null!;
        return false;
    }

    private static bool TryReadValueExact(Dictionary<string, object?> config, string propertyName, out object? value)
        => config.TryGetValue(propertyName, out value);

    private static bool TryReadNestedValueExact(
        Dictionary<string, object?> config,
        IReadOnlyList<string> propertyPath,
        out object? value)
    {
        var current = config;
        for (var index = 0; index < propertyPath.Count; index++)
        {
            if (!TryReadValueExact(current, propertyPath[index], out value))
            {
                return false;
            }

            if (index == propertyPath.Count - 1)
            {
                return true;
            }

            if (!TryAsDictionary(value, out current))
            {
                value = null;
                return false;
            }
        }

        value = null;
        return false;
    }

    private static bool TryAsDictionary(object? value, out Dictionary<string, object?> dictionary)
    {
        switch (value)
        {
            case Dictionary<string, object?> concrete:
                dictionary = concrete;
                return true;
            case IReadOnlyDictionary<string, object?> readOnly:
                dictionary = readOnly.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
                return true;
            case IDictionary<string, object?> mutable:
                dictionary = mutable.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
                return true;
            case IEnumerable<KeyValuePair<string, object?>> pairs:
                dictionary = pairs.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.Object:
                dictionary = ConvertJsonObject(element);
                return true;
            default:
                dictionary = null!;
                return false;
        }
    }

    private static bool TryReadString(object? value, out string text)
    {
        switch (value)
        {
            case string stringValue:
                text = stringValue;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.String:
                text = element.GetString() ?? string.Empty;
                return true;
            default:
                text = string.Empty;
                return false;
        }
    }

    private static bool TryReadBoolean(object? value, out bool booleanValue)
    {
        switch (value)
        {
            case bool native:
                booleanValue = native;
                return true;
            case JsonElement element when element.ValueKind is JsonValueKind.True or JsonValueKind.False:
                booleanValue = element.GetBoolean();
                return true;
            case string text when bool.TryParse(text, out var parsed):
                booleanValue = parsed;
                return true;
            default:
                booleanValue = default;
                return false;
        }
    }

    private static Dictionary<string, object?> ConvertJsonObject(JsonElement element)
    {
        var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            dictionary[property.Name] = ConvertJsonValue(property.Value);
        }

        return dictionary;
    }

    private static object? ConvertJsonValue(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => ConvertJsonObject(element),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonValue).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.TryGetInt64(out var intValue)
                ? intValue
                : element.TryGetDouble(out var doubleValue)
                    ? doubleValue
                    : element.GetRawText(),
            JsonValueKind.Null => null,
            _ => element.GetRawText(),
        };

    private static string ResolveConfiguredInstructionBaseDirectory(
        KernelConfigReadSnapshot snapshot,
        string? keyPath,
        string cwd)
    {
        if (!string.IsNullOrWhiteSpace(keyPath)
            && snapshot.Origins.TryGetValue(keyPath!, out var metadata))
        {
            var element = JsonSerializer.SerializeToElement(metadata);
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty("name", out var name)
                && name.ValueKind == JsonValueKind.Object)
            {
                var layerType = ReadMetadataString(name, "type");
                if (string.Equals(layerType, "user", StringComparison.OrdinalIgnoreCase))
                {
                    var file = NormalizeConfigText(ReadMetadataString(name, "file"));
                    var directory = file is null ? null : Path.GetDirectoryName(file);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        return directory!;
                    }
                }

                if (string.Equals(layerType, "project", StringComparison.OrdinalIgnoreCase))
                {
                    var dotTianShuFolder = NormalizeConfigText(ReadMetadataString(name, "dotTianShuFolder"));
                    if (!string.IsNullOrWhiteSpace(dotTianShuFolder))
                    {
                        return dotTianShuFolder!;
                    }
                }
            }
        }

        var fallbackCwd = NormalizeConfigText(cwd);
        if (!string.IsNullOrWhiteSpace(fallbackCwd))
        {
            return fallbackCwd!;
        }

        return Path.GetDirectoryName(TianShuConfigTomlPathResolver.ResolveUserConfigTomlPath())
               ?? Environment.CurrentDirectory;
    }

    private static string? ReadMetadataString(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            JsonValueKind.Null => null,
            _ => null,
        };
    }

    private static string? NormalizeConfigText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
