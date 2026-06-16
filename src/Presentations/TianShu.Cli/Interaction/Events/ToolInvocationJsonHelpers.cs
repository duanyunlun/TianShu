using System.Text.Json;

namespace TianShu.Cli.Interaction.Events;

internal static class ToolInvocationJsonHelpers
{
    public static bool TryParseJsonObject(string value, out JsonDocument? document)
    {
        try
        {
            document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return true;
            }

            document.Dispose();
            document = null;
            return false;
        }
        catch (JsonException)
        {
            document = null;
            return false;
        }
    }

    public static string? ReadJsonString(JsonElement root, string propertyName)
        => TryGetJsonPropertyIgnoreCase(root, propertyName, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => NormalizeDisplayText(value.GetString()),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
                _ => NormalizeDisplayText(value.GetRawText()),
            }
            : null;

    public static string? BuildCompactJsonSummary(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var parts = root.EnumerateObject()
            .Where(static property => property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            .Select(static property => $"{property.Name}={JsonElementToDisplayText(property.Value)}")
            .Where(static text => !string.IsNullOrWhiteSpace(text))
            .Take(4)
            .ToArray();
        return parts.Length == 0 ? null : string.Join(", ", parts);
    }

    public static bool TryGetJsonPropertyIgnoreCase(JsonElement root, string propertyName, out JsonElement value)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    public static string? NormalizeDisplayText(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is null
            ? null
            : string.Join(" ", normalized.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
    }

    private static string JsonElementToDisplayText(JsonElement value)
        => value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.GetRawText();
}
