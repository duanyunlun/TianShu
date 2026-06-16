using System.Text.Json;
using System.Text.RegularExpressions;

namespace TianShu.AppHost.Tools.Runtime;

internal static class KernelToolSchemaValidator
{
    public static bool TryValidate(JsonElement schema, JsonElement arguments, out string error)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            error = "arguments 必须是 JSON object。";
            return false;
        }

        return TryValidateAgainstSchema(schema, arguments, "arguments", out error);
    }

    private static bool TryValidateAgainstSchema(
        JsonElement schema,
        JsonElement value,
        string path,
        out string error)
    {
        error = string.Empty;
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return true;
        }

        if (!ValidateAllOf(schema, value, path, out error))
        {
            return false;
        }

        if (!ValidateAnyOf(schema, value, path, out error))
        {
            return false;
        }

        if (!ValidateOneOf(schema, value, path, out error))
        {
            return false;
        }

        if (!ValidateTypeConstraint(schema, value, path, out error))
        {
            return false;
        }

        if (!ValidateEnumConstraint(schema, value, path, out error))
        {
            return false;
        }

        if (!ValidateStringConstraints(schema, value, path, out error))
        {
            return false;
        }

        if (!ValidateNumberConstraints(schema, value, path, out error))
        {
            return false;
        }

        if (!ValidateArrayConstraints(schema, value, path, out error))
        {
            return false;
        }

        if (!ValidateObjectConstraints(schema, value, path, out error))
        {
            return false;
        }

        return true;
    }

    private static bool ValidateAllOf(JsonElement schema, JsonElement value, string path, out string error)
    {
        error = string.Empty;
        if (!schema.TryGetProperty("allOf", out var allOf) || allOf.ValueKind != JsonValueKind.Array)
        {
            return true;
        }

        foreach (var child in allOf.EnumerateArray())
        {
            if (!TryValidateAgainstSchema(child, value, path, out error))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValidateAnyOf(JsonElement schema, JsonElement value, string path, out string error)
    {
        error = string.Empty;
        if (!schema.TryGetProperty("anyOf", out var anyOf) || anyOf.ValueKind != JsonValueKind.Array)
        {
            return true;
        }

        foreach (var child in anyOf.EnumerateArray())
        {
            if (TryValidateAgainstSchema(child, value, path, out _))
            {
                return true;
            }
        }

        error = $"{path} 不满足 anyOf 约束。";
        return false;
    }

    private static bool ValidateOneOf(JsonElement schema, JsonElement value, string path, out string error)
    {
        error = string.Empty;
        if (!schema.TryGetProperty("oneOf", out var oneOf) || oneOf.ValueKind != JsonValueKind.Array)
        {
            return true;
        }

        var matchCount = 0;
        foreach (var child in oneOf.EnumerateArray())
        {
            if (TryValidateAgainstSchema(child, value, path, out _))
            {
                matchCount++;
            }
        }

        if (matchCount == 1)
        {
            return true;
        }

        error = $"{path} 不满足 oneOf 约束。";
        return false;
    }

    private static bool ValidateTypeConstraint(JsonElement schema, JsonElement value, string path, out string error)
    {
        error = string.Empty;
        if (!schema.TryGetProperty("type", out var typeElement))
        {
            return true;
        }

        var expectedTypes = ParseTypeNames(typeElement);
        if (expectedTypes.Count == 0)
        {
            return true;
        }

        if (expectedTypes.Any(typeName => MatchesType(value, typeName)))
        {
            return true;
        }

        error = $"{path} 类型错误，预期 {string.Join(" | ", expectedTypes)}。";
        return false;
    }

    private static bool ValidateEnumConstraint(JsonElement schema, JsonElement value, string path, out string error)
    {
        error = string.Empty;
        if (!schema.TryGetProperty("enum", out var enumElement) || enumElement.ValueKind != JsonValueKind.Array)
        {
            return true;
        }

        foreach (var candidate in enumElement.EnumerateArray())
        {
            if (JsonValueEquals(candidate, value))
            {
                return true;
            }
        }

        error = $"{path} 不在 enum 允许值范围内。";
        return false;
    }

    private static bool ValidateStringConstraints(JsonElement schema, JsonElement value, string path, out string error)
    {
        error = string.Empty;
        if (value.ValueKind != JsonValueKind.String)
        {
            return true;
        }

        var text = value.GetString() ?? string.Empty;
        if (schema.TryGetProperty("minLength", out var minLengthElement)
            && minLengthElement.ValueKind == JsonValueKind.Number
            && minLengthElement.TryGetInt32(out var minLength)
            && text.Length < Math.Max(0, minLength))
        {
            error = $"{path} 长度不能小于 {minLength}。";
            return false;
        }

        if (schema.TryGetProperty("maxLength", out var maxLengthElement)
            && maxLengthElement.ValueKind == JsonValueKind.Number
            && maxLengthElement.TryGetInt32(out var maxLength)
            && text.Length > Math.Max(0, maxLength))
        {
            error = $"{path} 长度不能大于 {maxLength}。";
            return false;
        }

        if (schema.TryGetProperty("pattern", out var patternElement)
            && patternElement.ValueKind == JsonValueKind.String)
        {
            var pattern = patternElement.GetString() ?? string.Empty;
            try
            {
                if (!Regex.IsMatch(text, pattern))
                {
                    error = $"{path} 不匹配 pattern 约束。";
                    return false;
                }
            }
            catch (ArgumentException)
            {
                error = $"{path} 的 pattern 定义无效。";
                return false;
            }
        }

        return true;
    }

    private static bool ValidateNumberConstraints(JsonElement schema, JsonElement value, string path, out string error)
    {
        error = string.Empty;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var numberValue))
        {
            return true;
        }

        if (schema.TryGetProperty("minimum", out var minimumElement)
            && minimumElement.ValueKind == JsonValueKind.Number
            && minimumElement.TryGetDouble(out var minimum)
            && numberValue < minimum)
        {
            error = $"{path} 不能小于 {minimum}。";
            return false;
        }

        if (schema.TryGetProperty("maximum", out var maximumElement)
            && maximumElement.ValueKind == JsonValueKind.Number
            && maximumElement.TryGetDouble(out var maximum)
            && numberValue > maximum)
        {
            error = $"{path} 不能大于 {maximum}。";
            return false;
        }

        if (schema.TryGetProperty("exclusiveMinimum", out var exclusiveMinimumElement)
            && exclusiveMinimumElement.ValueKind == JsonValueKind.Number
            && exclusiveMinimumElement.TryGetDouble(out var exclusiveMinimum)
            && numberValue <= exclusiveMinimum)
        {
            error = $"{path} 必须大于 {exclusiveMinimum}。";
            return false;
        }

        if (schema.TryGetProperty("exclusiveMaximum", out var exclusiveMaximumElement)
            && exclusiveMaximumElement.ValueKind == JsonValueKind.Number
            && exclusiveMaximumElement.TryGetDouble(out var exclusiveMaximum)
            && numberValue >= exclusiveMaximum)
        {
            error = $"{path} 必须小于 {exclusiveMaximum}。";
            return false;
        }

        return true;
    }

    private static bool ValidateArrayConstraints(JsonElement schema, JsonElement value, string path, out string error)
    {
        error = string.Empty;
        if (value.ValueKind != JsonValueKind.Array)
        {
            return true;
        }

        var count = value.GetArrayLength();
        if (schema.TryGetProperty("minItems", out var minItemsElement)
            && minItemsElement.ValueKind == JsonValueKind.Number
            && minItemsElement.TryGetInt32(out var minItems)
            && count < Math.Max(0, minItems))
        {
            error = $"{path} 数组长度不能小于 {minItems}。";
            return false;
        }

        if (schema.TryGetProperty("maxItems", out var maxItemsElement)
            && maxItemsElement.ValueKind == JsonValueKind.Number
            && maxItemsElement.TryGetInt32(out var maxItems)
            && count > Math.Max(0, maxItems))
        {
            error = $"{path} 数组长度不能大于 {maxItems}。";
            return false;
        }

        if (!schema.TryGetProperty("items", out var itemsSchema))
        {
            return true;
        }

        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            if (itemsSchema.ValueKind == JsonValueKind.Object)
            {
                if (!TryValidateAgainstSchema(itemsSchema, item, $"{path}[{index}]", out error))
                {
                    return false;
                }
            }
            else if (itemsSchema.ValueKind == JsonValueKind.Array)
            {
                if (index >= itemsSchema.GetArrayLength())
                {
                    break;
                }

                var tupleSchema = itemsSchema[index];
                if (!TryValidateAgainstSchema(tupleSchema, item, $"{path}[{index}]", out error))
                {
                    return false;
                }
            }

            index++;
        }

        return true;
    }

    private static bool ValidateObjectConstraints(JsonElement schema, JsonElement value, string path, out string error)
    {
        error = string.Empty;
        if (value.ValueKind != JsonValueKind.Object)
        {
            return true;
        }

        if (schema.TryGetProperty("required", out var required)
            && required.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in required.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var key = item.GetString();
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (!value.TryGetProperty(key, out _))
                {
                    error = $"缺少必填字段：{path}.{key}";
                    return false;
                }
            }
        }

        var hasProperties = schema.TryGetProperty("properties", out var properties)
                            && properties.ValueKind == JsonValueKind.Object;

        var allowAdditionalProperties = true;
        if (schema.TryGetProperty("additionalProperties", out var additionalPropertiesElement)
            && additionalPropertiesElement.ValueKind == JsonValueKind.False)
        {
            allowAdditionalProperties = false;
        }

        if (!allowAdditionalProperties && hasProperties)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (!properties.TryGetProperty(property.Name, out _))
                {
                    error = $"{path} 存在未声明字段：{property.Name}";
                    return false;
                }
            }
        }

        if (!hasProperties)
        {
            return true;
        }

        foreach (var property in properties.EnumerateObject())
        {
            if (!value.TryGetProperty(property.Name, out var propertyValue))
            {
                continue;
            }

            if (!TryValidateAgainstSchema(property.Value, propertyValue, $"{path}.{property.Name}", out error))
            {
                return false;
            }
        }

        return true;
    }

    private static List<string> ParseTypeNames(JsonElement typeElement)
    {
        if (typeElement.ValueKind == JsonValueKind.String)
        {
            var typeName = KernelToolJsonHelpers.Normalize(typeElement.GetString());
            return string.IsNullOrWhiteSpace(typeName)
                ? []
                : [typeName!];
        }

        if (typeElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var types = new List<string>();
        foreach (var item in typeElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var typeName = KernelToolJsonHelpers.Normalize(item.GetString());
            if (!string.IsNullOrWhiteSpace(typeName))
            {
                types.Add(typeName!);
            }
        }

        return types;
    }

    private static bool MatchesType(JsonElement value, string expectedType)
    {
        return expectedType switch
        {
            "string" => value.ValueKind == JsonValueKind.String,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "number" => value.ValueKind == JsonValueKind.Number,
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "null" => value.ValueKind == JsonValueKind.Null,
            _ => true,
        };
    }

    private static bool JsonValueEquals(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
        {
            return false;
        }

        return left.ValueKind switch
        {
            JsonValueKind.Null => true,
            JsonValueKind.String => string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal),
            JsonValueKind.True or JsonValueKind.False => left.GetBoolean() == right.GetBoolean(),
            JsonValueKind.Number => left.GetRawText() == right.GetRawText(),
            JsonValueKind.Array => JsonArrayEquals(left, right),
            JsonValueKind.Object => JsonObjectEquals(left, right),
            _ => left.GetRawText() == right.GetRawText(),
        };
    }

    private static bool JsonArrayEquals(JsonElement left, JsonElement right)
    {
        if (left.GetArrayLength() != right.GetArrayLength())
        {
            return false;
        }

        var index = 0;
        foreach (var leftItem in left.EnumerateArray())
        {
            if (!JsonValueEquals(leftItem, right[index]))
            {
                return false;
            }

            index++;
        }

        return true;
    }

    private static bool JsonObjectEquals(JsonElement left, JsonElement right)
    {
        var leftProperties = left.EnumerateObject().ToArray();
        var rightProperties = right.EnumerateObject().ToArray();
        if (leftProperties.Length != rightProperties.Length)
        {
            return false;
        }

        foreach (var leftProperty in leftProperties)
        {
            if (!right.TryGetProperty(leftProperty.Name, out var rightValue))
            {
                return false;
            }

            if (!JsonValueEquals(leftProperty.Value, rightValue))
            {
                return false;
            }
        }

        return true;
    }
}
