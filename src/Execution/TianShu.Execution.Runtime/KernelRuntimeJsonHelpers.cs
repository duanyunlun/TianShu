using System.Text.Json;

namespace TianShu.Execution.Runtime;

/// <summary>
/// Runtime 内部读取 provider / transport JSON payload 的轻量辅助方法。
/// </summary>
internal static class KernelRuntimeJsonHelpers
{
    public static string? ReadString(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null,
        };
    }

    public static int? ReadInt(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var i))
        {
            return i;
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
