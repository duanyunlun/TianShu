using System.Text.Json;

namespace TianShu.AppHost.Configuration;

/// <summary>
/// 轻量 TOML 文本标量 / section 解析辅助件。
/// Lightweight helpers for scalar and section parsing over TOML text content.
/// </summary>
internal static class KernelTomlTextParsingUtilities
{
    public static bool TryParseTopLevelTomlScalar(string text, string key, out string? value)
    {
        value = null;
        var inSection = false;
        foreach (var rawLine in text.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inSection = true;
                continue;
            }

            if (inSection)
            {
                continue;
            }

            var equalIndex = line.IndexOf('=');
            if (equalIndex <= 0)
            {
                continue;
            }

            var currentKey = line[..equalIndex].Trim();
            if (!string.Equals(currentKey, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = ReadScalarConfigValue(line[(equalIndex + 1)..]);
            return true;
        }

        return false;
    }

    public static bool TryParseTomlStringArray(string text, string key, out List<string> values)
    {
        values = [];
        var marker = $"{key} = [";
        var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return false;
        }

        var start = index + marker.Length;
        var end = text.IndexOf(']', start);
        if (end < start)
        {
            return true;
        }

        values = ParseTomlInlineStringArray(text[start..end]);
        return true;
    }

    public static Dictionary<string, bool> ParseTomlBooleanSection(string text, string sectionName)
    {
        var values = ParseTomlSectionRawValues(text, sectionName);
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in values)
        {
            var scalar = ReadScalarConfigValue(entry.Value);
            if (bool.TryParse(scalar, out var boolValue))
            {
                result[entry.Key] = boolValue;
            }
        }

        return result;
    }

    public static Dictionary<string, string> ParseTomlSectionRawValues(string text, string sectionName)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
                continue;
            }

            if (!string.Equals(currentSection, sectionName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var equalIndex = line.IndexOf('=');
            if (equalIndex <= 0)
            {
                continue;
            }

            var currentKey = line[..equalIndex].Trim();
            var rawValue = line[(equalIndex + 1)..].Trim();
            result[currentKey] = rawValue;
        }

        return result;
    }

    public static bool? TryReadTomlSectionBoolean(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var raw))
        {
            return null;
        }

        var scalar = ReadScalarConfigValue(raw);
        return bool.TryParse(scalar, out var boolValue) ? boolValue : null;
    }

    public static ushort? TryReadTomlSectionUInt16(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var raw))
        {
            return null;
        }

        var scalar = ReadScalarConfigValue(raw);
        return ushort.TryParse(scalar, out var numericValue) ? numericValue : null;
    }

    public static List<string>? TryReadTomlSectionStringArray(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var raw))
        {
            return null;
        }

        return raw.TrimStart().StartsWith('[')
            ? ParseTomlInlineStringArray(raw.Trim().TrimStart('[').TrimEnd(']'))
            : null;
    }

    public static string? ReadScalarConfigValue(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(rawValue);
            var root = json.RootElement;
            return root.ValueKind switch
            {
                JsonValueKind.String => Normalize(root.GetString()),
                JsonValueKind.Number => Normalize(root.GetRawText()),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => null,
                _ => null,
            };
        }
        catch (JsonException)
        {
            return Normalize(rawValue.Trim().Trim('"', '\''));
        }
    }

    private static List<string> ParseTomlInlineStringArray(string raw)
    {
        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static value => value.Trim().Trim('"', '\''))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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
}
