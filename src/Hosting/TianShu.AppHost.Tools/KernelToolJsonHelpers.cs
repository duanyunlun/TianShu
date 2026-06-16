using System.Text.Json;
using System.Text.Json.Nodes;

namespace TianShu.AppHost.Tools;

/// <summary>
/// 工具参数与结构化 JSON 读取辅助方法。
/// Helper methods for reading tool arguments and structured JSON payloads.
/// </summary>
internal static class KernelToolJsonHelpers
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

    public static bool? ReadBool(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var b) => b,
            _ => null,
        };
    }

    public static List<string> ReadStringArray(JsonElement json, string propertyName)
    {
        var result = new List<string>();
        if (json.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var value in property.EnumerateArray())
        {
            var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
            var normalized = Normalize(text);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                result.Add(normalized!);
            }
        }

        return result;
    }

    public static bool TryReadInputArray(JsonElement json, out List<JsonElement> items)
    {
        items = [];
        if (json.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty("input", out var input)
            || input.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        items = input.EnumerateArray().Select(static x => x.Clone()).ToList();
        return true;
    }

    public static Dictionary<string, string[]> TryReadExtraSkillRoots(
        JsonElement @params,
        IReadOnlyList<string> cwds,
        out string? error)
    {
        error = null;
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (@params.ValueKind != JsonValueKind.Object
            || !@params.TryGetProperty("perCwdExtraUserRoots", out var extra)
            || extra.ValueKind != JsonValueKind.Array)
        {
            return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        }

        var cwdSet = cwds.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in extra.EnumerateArray())
        {
            var entryCwd = ReadString(entry, "cwd");
            if (string.IsNullOrWhiteSpace(entryCwd))
            {
                continue;
            }

            var normalizedCwd = Path.GetFullPath(entryCwd);
            if (!cwdSet.Contains(normalizedCwd))
            {
                continue;
            }

            var roots = ReadStringArray(entry, "extraUserRoots");
            var invalidRoot = roots.FirstOrDefault(static x => !Path.IsPathRooted(x));
            if (!string.IsNullOrWhiteSpace(invalidRoot))
            {
                error = $"skills/list perCwdExtraUserRoots extraUserRoots paths must be absolute: {invalidRoot}";
                return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            }

            if (!map.TryGetValue(normalizedCwd, out var collected))
            {
                collected = new List<string>();
                map[normalizedCwd] = collected;
            }

            collected.AddRange(roots.Select(Path.GetFullPath));
        }

        return map.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
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

/// <summary>
/// 对外暴露工具 schema 前执行最小清洗，避免 provider 侧收到不兼容 JSON Schema。
/// Performs minimal schema sanitization before exposing tool schemas to providers.
/// </summary>
internal static class KernelJsonSchemaSanitizer
{
    private static readonly HashSet<string> SupportedTypes = new(StringComparer.Ordinal)
    {
        "object",
        "array",
        "string",
        "number",
        "integer",
        "boolean",
    };

    public static JsonElement Sanitize(JsonElement schema)
    {
        JsonNode? node = JsonNode.Parse(schema.GetRawText());
        if (node is null)
        {
            return JsonSerializer.SerializeToElement(new { type = "string" });
        }

        node = SanitizeNode(node);
        return JsonSerializer.SerializeToElement(node);
    }

    private static JsonNode SanitizeNode(JsonNode node)
    {
        if (node is JsonValue valueNode)
        {
            return valueNode.TryGetValue<bool>(out _)
                ? new JsonObject { ["type"] = "string" }
                : node;
        }

        if (node is JsonArray arrayNode)
        {
            for (var i = 0; i < arrayNode.Count; i++)
            {
                if (arrayNode[i] is not null)
                {
                    var originalChild = arrayNode[i]!;
                    var sanitizedChild = SanitizeNode(originalChild);
                    if (!ReferenceEquals(sanitizedChild, originalChild))
                    {
                        arrayNode[i] = sanitizedChild;
                    }
                }
            }

            return arrayNode;
        }

        if (node is not JsonObject objectNode)
        {
            return node;
        }

        if (objectNode["properties"] is JsonObject properties)
        {
            foreach (var property in properties.ToList())
            {
                if (property.Value is not null)
                {
                    var originalPropertyValue = property.Value;
                    var sanitizedPropertyValue = SanitizeNode(originalPropertyValue);
                    if (!ReferenceEquals(sanitizedPropertyValue, originalPropertyValue))
                    {
                        properties[property.Key] = sanitizedPropertyValue;
                    }
                }
            }
        }

        if (objectNode["items"] is JsonNode itemsNode)
        {
            var sanitizedItemsNode = SanitizeNode(itemsNode);
            if (!ReferenceEquals(sanitizedItemsNode, itemsNode))
            {
                objectNode["items"] = sanitizedItemsNode;
            }
        }

        foreach (var combiner in new[] { "oneOf", "anyOf", "allOf", "prefixItems" })
        {
            if (objectNode[combiner] is JsonNode combinerNode)
            {
                var sanitizedCombinerNode = SanitizeNode(combinerNode);
                if (!ReferenceEquals(sanitizedCombinerNode, combinerNode))
                {
                    objectNode[combiner] = sanitizedCombinerNode;
                }
            }
        }

        var type = TryGetString(objectNode["type"]);
        if (type is null && objectNode["type"] is JsonArray typeArray)
        {
            foreach (var candidate in typeArray)
            {
                var typeName = TryGetString(candidate);
                if (typeName is not null && SupportedTypes.Contains(typeName))
                {
                    type = typeName;
                    break;
                }
            }
        }

        if (type is null)
        {
            if (objectNode.ContainsKey("properties")
                || objectNode.ContainsKey("required")
                || objectNode.ContainsKey("additionalProperties"))
            {
                type = "object";
            }
            else if (objectNode.ContainsKey("items") || objectNode.ContainsKey("prefixItems"))
            {
                type = "array";
            }
            else if (objectNode.ContainsKey("enum")
                || objectNode.ContainsKey("const")
                || objectNode.ContainsKey("format"))
            {
                type = "string";
            }
            else if (objectNode.ContainsKey("minimum")
                || objectNode.ContainsKey("maximum")
                || objectNode.ContainsKey("exclusiveMinimum")
                || objectNode.ContainsKey("exclusiveMaximum")
                || objectNode.ContainsKey("multipleOf"))
            {
                type = "number";
            }
        }

        type ??= "string";
        objectNode["type"] = type;

        if (string.Equals(type, "object", StringComparison.Ordinal))
        {
            objectNode["properties"] ??= new JsonObject();

            if (objectNode["additionalProperties"] is JsonNode additionalPropertiesNode)
            {
                if (additionalPropertiesNode is not JsonValue additionalPropertiesValue
                    || !additionalPropertiesValue.TryGetValue<bool>(out _))
                {
                    var sanitizedAdditionalPropertiesNode = SanitizeNode(additionalPropertiesNode);
                    if (!ReferenceEquals(sanitizedAdditionalPropertiesNode, additionalPropertiesNode))
                    {
                        objectNode["additionalProperties"] = sanitizedAdditionalPropertiesNode;
                    }
                }
            }
        }

        if (string.Equals(type, "array", StringComparison.Ordinal) && objectNode["items"] is null)
        {
            objectNode["items"] = new JsonObject
            {
                ["type"] = "string",
            };
        }

        return objectNode;
    }

    private static string? TryGetString(JsonNode? node)
    {
        return node is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : null;
    }
}
