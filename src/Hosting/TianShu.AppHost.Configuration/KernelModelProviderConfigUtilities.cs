using System.Text.Json;

namespace TianShu.AppHost.Configuration;

/// <summary>
/// model provider 与 service tier 原始配置读取辅助件。
/// Helpers for raw model-provider and service-tier config lookups.
/// </summary>
internal static class KernelModelProviderConfigUtilities
{
    public static string? ReadConfiguredModelProviderSetting(
        IReadOnlyDictionary<string, string> config,
        string? modelProvider,
        params string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(modelProvider))
        {
            return null;
        }

        foreach (var propertyName in propertyNames)
        {
            if (TryReadFlattened(config, out var TianShuValue, $"providers.{modelProvider}.{propertyName}"))
            {
                return TianShuValue;
            }
        }

        return null;
    }

    public static string? ReadConfiguredModelProviderProtocolValue(
        IReadOnlyDictionary<string, string> config,
        string? modelProvider)
    {
        if (string.IsNullOrWhiteSpace(modelProvider))
        {
            return null;
        }

        return ReadConfiguredModelProviderSetting(config, modelProvider, "default_protocol");
    }

    public static string? ReadConfiguredServiceTierValue(
        IReadOnlyDictionary<string, string> config,
        params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (TryReadFlattened(config, out var value, propertyName))
            {
                return value;
            }
        }

        return null;
    }

    public static string? ReadConfiguredServiceTierValue(
        Dictionary<string, object?> config,
        params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (TryReadValueExact(config, propertyName, out var rawValue)
                && TryReadString(rawValue, out var text))
            {
                return text;
            }
        }

        return null;
    }

    public static string? ReadConfiguredModelProviderSetting(
        Dictionary<string, object?> config,
        string? modelProvider,
        params string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(modelProvider))
        {
            return null;
        }

        foreach (var propertyName in propertyNames)
        {
            if (TryReadNestedValueExact(config, ["providers", modelProvider!, propertyName], out var rawValue)
                && TryReadString(rawValue, out var TianShuValue))
            {
                return TianShuValue;
            }
        }

        return null;
    }

    public static string? ReadConfiguredModelProviderNestedSetting(
        Dictionary<string, object?> config,
        string? modelProvider,
        string childName,
        params string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(modelProvider) || string.IsNullOrWhiteSpace(childName))
        {
            return null;
        }

        foreach (var propertyName in propertyNames)
        {
            if (TryReadNestedValueExact(config, ["providers", modelProvider!, childName, propertyName], out var rawValue)
                && TryReadString(rawValue, out var TianShuValue))
            {
                return TianShuValue;
            }
        }

        return null;
    }

    public static int? ReadConfiguredModelProviderInt(
        Dictionary<string, object?> config,
        string? modelProvider,
        params string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(modelProvider))
        {
            return null;
        }

        foreach (var propertyName in propertyNames)
        {
            if (TryReadNestedValueExact(config, ["providers", modelProvider!, propertyName], out var TianShuRawValue))
            {
                return TryReadInt(TianShuRawValue, out var value) ? value : null;
            }
        }

        return null;
    }

    public static long? ReadConfiguredModelProviderLong(
        Dictionary<string, object?> config,
        string? modelProvider,
        params string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(modelProvider))
        {
            return null;
        }

        foreach (var propertyName in propertyNames)
        {
            if (TryReadNestedValueExact(config, ["providers", modelProvider!, propertyName], out var TianShuRawValue))
            {
                return TryReadLong(TianShuRawValue, out var value) ? value : null;
            }
        }

        return null;
    }

    public static bool? ReadConfiguredModelProviderBoolean(
        Dictionary<string, object?> config,
        string? modelProvider,
        params string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(modelProvider))
        {
            return null;
        }

        foreach (var propertyName in propertyNames)
        {
            if (TryReadNestedValueExact(config, ["providers", modelProvider!, propertyName], out var TianShuRawValue)
                && TryReadBoolean(TianShuRawValue, out var TianShuValue))
            {
                return TianShuValue;
            }
        }

        return null;
    }

    private static bool TryReadFlattened(
        IReadOnlyDictionary<string, string> config,
        out string? value,
        string key)
    {
        value = null;
        if (!config.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        value = raw.Trim();
        return true;
    }

    private static bool TryReadValueExact(Dictionary<string, object?> config, string key, out object? value)
        => config.TryGetValue(key, out value);

    private static bool TryReadNestedValueExact(
        Dictionary<string, object?> config,
        IReadOnlyList<string> path,
        out object? value)
    {
        var current = config;
        for (var index = 0; index < path.Count; index++)
        {
            if (!TryReadValueExact(current, path[index], out value))
            {
                return false;
            }

            if (index == path.Count - 1)
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

    private static bool TryReadInt(object? value, out int intValue)
    {
        switch (value)
        {
            case int native:
                intValue = native;
                return true;
            case long native when native is >= int.MinValue and <= int.MaxValue:
                intValue = (int)native;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var parsed):
                intValue = parsed;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.String
                                      && int.TryParse(element.GetString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsedFromString):
                intValue = parsedFromString;
                return true;
            case string text when int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsedFromText):
                intValue = parsedFromText;
                return true;
            default:
                intValue = default;
                return false;
        }
    }

    private static bool TryReadLong(object? value, out long longValue)
    {
        switch (value)
        {
            case long native:
                longValue = native;
                return true;
            case int native:
                longValue = native;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var parsed):
                longValue = parsed;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.String
                                      && long.TryParse(element.GetString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsedFromString):
                longValue = parsedFromString;
                return true;
            case string text when long.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsedFromText):
                longValue = parsedFromText;
                return true;
            default:
                longValue = default;
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
}
