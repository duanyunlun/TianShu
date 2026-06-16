using System.Text.Json;

namespace TianShu.AppHost.Configuration;

/// <summary>
/// 配置读取层合并、origin 分配与 request override 载荷转换辅助件。
/// Helpers for config-read layer merging, origin assignment, and request-override payload conversion.
/// </summary>
internal static class KernelConfigReadLayerUtilities
{
    public static Dictionary<string, string> MergeConfigValueLayers(
        Dictionary<string, string> lowerPrecedence,
        Dictionary<string, string> higherPrecedence)
    {
        var merged = new Dictionary<string, string>(lowerPrecedence, StringComparer.Ordinal);
        foreach (var pair in higherPrecedence)
        {
            merged[pair.Key] = pair.Value;
        }

        return merged;
    }

    public static void MergeConfigObjects(Dictionary<string, object?> target, Dictionary<string, object?> source)
    {
        foreach (var pair in source)
        {
            if (pair.Value is Dictionary<string, object?> sourceDictionary
                && target.TryGetValue(pair.Key, out var existing)
                && existing is Dictionary<string, object?> targetDictionary)
            {
                MergeConfigObjects(targetDictionary, sourceDictionary);
                continue;
            }

            target[pair.Key] = KernelConfigObjectUtilities.CloneConfigValue(pair.Value);
        }
    }

    public static Dictionary<string, object?> BuildProjectDocScopedConfig(KernelConfigReadSnapshot snapshot)
    {
        var scopedConfig = KernelConfigObjectUtilities.CloneConfigDictionary(snapshot.Config);
        var projectRootMarkerConfig = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var layer in snapshot.OrderedLayers)
        {
            if (IsProjectConfigReadLayer(layer))
            {
                continue;
            }

            MergeConfigObjects(projectRootMarkerConfig, layer.Config);
        }

        ReplaceProjectRootMarkers(scopedConfig, projectRootMarkerConfig);
        return scopedConfig;
    }

    public static bool IsProjectConfigReadLayer(KernelConfigReadLayer layer)
    {
        var element = JsonSerializer.SerializeToElement(layer.Name);
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var value = type.GetString();
        return string.Equals(value, "project", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "cwd", StringComparison.OrdinalIgnoreCase);
    }

    public static void AssignConfigOrigins(
        Dictionary<string, object?> source,
        string? prefix,
        object metadata,
        Dictionary<string, object?> origins)
    {
        foreach (var pair in source)
        {
            var keyPath = string.IsNullOrWhiteSpace(prefix)
                ? pair.Key
                : $"{prefix}.{pair.Key}";
            origins[keyPath] = metadata;

            if (pair.Value is Dictionary<string, object?> nested)
            {
                AssignConfigOrigins(nested, keyPath, metadata, origins);
            }
        }
    }

    public static Dictionary<string, object?> BuildConfigObjectFromOverrideElement(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        return ConvertJsonObjectToConfigDictionary(element);
    }

    public static Dictionary<string, object?> ConvertJsonObjectToConfigDictionary(JsonElement element)
    {
        var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            dictionary[property.Name] = ConvertJsonElementToConfigValue(property.Value);
        }

        return dictionary;
    }

    public static object? ConvertJsonElementToConfigValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => ConvertJsonObjectToConfigDictionary(element),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElementToConfigValue).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var int64Value) => int64Value,
            JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.GetRawText(),
        };
    }

    private static void ReplaceProjectRootMarkers(
        Dictionary<string, object?> target,
        Dictionary<string, object?> source)
    {
        var hasSnake = source.TryGetValue("project_root_markers", out var snakeValue);
        var hasCamel = source.TryGetValue("projectRootMarkers", out var camelValue);

        target.Remove("project_root_markers");
        target.Remove("projectRootMarkers");

        if (hasSnake)
        {
            target["project_root_markers"] = KernelConfigObjectUtilities.CloneConfigValue(snakeValue);
        }

        if (hasCamel)
        {
            target["projectRootMarkers"] = KernelConfigObjectUtilities.CloneConfigValue(camelValue);
        }
    }
}
