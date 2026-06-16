using System.Text.Json;
using System.Text.Json.Nodes;

namespace TianShu.AppHost.Tools;

/// <summary>
/// MCP server elicitation 在宿主工具层与 Kernel 间传递的轻量请求/响应载荷。
/// Lightweight MCP server elicitation request/response payloads shared by the host tool layer and Kernel.
/// </summary>
internal sealed record McpServerElicitationRequest(
    string ServerName,
    string Mode,
    string Message,
    JsonElement? RequestedSchema = null,
    string? Url = null,
    string? ElicitationId = null,
    JsonElement? Meta = null);

internal sealed record McpServerElicitationResponse(
    string Action,
    JsonElement? Content,
    JsonElement? Meta = null);

/// <summary>
/// 规范化 MCP elicitation form schema，确保只暴露当前宿主协议支持的字段。
/// Normalizes MCP elicitation form schemas so only host-supported fields are emitted.
/// </summary>
internal static class McpElicitationSchemaCodec
{
    private static readonly HashSet<string> RootFields = new(StringComparer.Ordinal)
    {
        "$schema",
        "type",
        "properties",
        "required",
    };

    private static readonly HashSet<string> StringFields = new(StringComparer.Ordinal)
    {
        "type",
        "title",
        "description",
        "minLength",
        "maxLength",
        "format",
        "default",
    };

    private static readonly HashSet<string> NumberFields = new(StringComparer.Ordinal)
    {
        "type",
        "title",
        "description",
        "minimum",
        "maximum",
        "default",
    };

    private static readonly HashSet<string> BooleanFields = new(StringComparer.Ordinal)
    {
        "type",
        "title",
        "description",
        "default",
    };

    private static readonly HashSet<string> LegacyEnumFields = new(StringComparer.Ordinal)
    {
        "type",
        "title",
        "description",
        "enum",
        "enumNames",
        "default",
    };

    private static readonly HashSet<string> SingleSelectFields = new(StringComparer.Ordinal)
    {
        "type",
        "title",
        "description",
        "oneOf",
        "default",
    };

    private static readonly HashSet<string> MultiSelectFields = new(StringComparer.Ordinal)
    {
        "type",
        "title",
        "description",
        "minItems",
        "maxItems",
        "items",
        "default",
    };

    private static readonly HashSet<string> UntitledEnumItemsFields = new(StringComparer.Ordinal)
    {
        "type",
        "enum",
    };

    private static readonly HashSet<string> TitledEnumItemsFields = new(StringComparer.Ordinal)
    {
        "anyOf",
        "oneOf",
    };

    private static readonly HashSet<string> ConstOptionFields = new(StringComparer.Ordinal)
    {
        "const",
        "title",
    };

    private static readonly HashSet<string> AllowedStringFormats = new(StringComparer.Ordinal)
    {
        "email",
        "uri",
        "date",
        "date-time",
    };

    public static JsonElement NormalizeFormRequestedSchema(JsonElement? schema)
    {
        if (schema is null)
        {
            throw new InvalidOperationException("requested_schema is required when mode=form.");
        }

        if (schema.Value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("requested_schema must be an object.");
        }

        return JsonSerializer.SerializeToElement(NormalizeRoot(schema.Value));
    }

    private static JsonObject NormalizeRoot(JsonElement schema)
    {
        const string path = "requested_schema";
        ValidateAllowedProperties(schema, RootFields, path);

        var type = RequireString(schema, "type", path);
        if (!string.Equals(type, "object", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{path}.type must be \"object\".");
        }

        var properties = RequireObject(schema, "properties", path);
        var normalizedProperties = new JsonObject();
        foreach (var property in properties.EnumerateObject())
        {
            normalizedProperties[property.Name] = NormalizePrimitiveSchema(property.Value, $"{path}.properties.{property.Name}");
        }

        var normalized = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = normalizedProperties,
        };

        AddOptionalString(normalized, "$schema", schema, "$schema");
        AddOptionalStringArray(normalized, "required", schema, path, allowEmpty: true);
        return normalized;
    }

    private static JsonNode NormalizePrimitiveSchema(JsonElement schema, string path)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"{path} must be an object.");
        }

        var type = RequireString(schema, "type", path);
        return type switch
        {
            "string" when schema.TryGetProperty("enum", out _) => NormalizeLegacyEnumSchema(schema, path),
            "string" when schema.TryGetProperty("oneOf", out _) => NormalizeSingleSelectSchema(schema, path),
            "string" => NormalizeStringSchema(schema, path),
            "number" or "integer" => NormalizeNumberSchema(schema, path),
            "boolean" => NormalizeBooleanSchema(schema, path),
            "array" => NormalizeMultiSelectSchema(schema, path),
            _ => throw new InvalidOperationException(
                $"{path}.type must be one of \"string\", \"number\", \"integer\", \"boolean\", or \"array\"."),
        };
    }

    private static JsonObject NormalizeStringSchema(JsonElement schema, string path)
    {
        ValidateAllowedProperties(schema, StringFields, path);
        var normalized = new JsonObject
        {
            ["type"] = "string",
        };

        AddOptionalString(normalized, "title", schema, path);
        AddOptionalString(normalized, "description", schema, path);
        AddOptionalUInt32(normalized, "minLength", schema, path);
        AddOptionalUInt32(normalized, "maxLength", schema, path);
        AddOptionalFormat(normalized, schema, path);
        AddOptionalString(normalized, "default", schema, path);
        return normalized;
    }

    private static JsonObject NormalizeNumberSchema(JsonElement schema, string path)
    {
        ValidateAllowedProperties(schema, NumberFields, path);
        var type = RequireString(schema, "type", path);
        if (!string.Equals(type, "number", StringComparison.Ordinal)
            && !string.Equals(type, "integer", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{path}.type must be \"number\" or \"integer\".");
        }

        var normalized = new JsonObject
        {
            ["type"] = type,
        };

        AddOptionalString(normalized, "title", schema, path);
        AddOptionalString(normalized, "description", schema, path);
        AddOptionalDouble(normalized, "minimum", schema, path);
        AddOptionalDouble(normalized, "maximum", schema, path);
        AddOptionalDouble(normalized, "default", schema, path);
        return normalized;
    }

    private static JsonObject NormalizeBooleanSchema(JsonElement schema, string path)
    {
        ValidateAllowedProperties(schema, BooleanFields, path);
        var type = RequireString(schema, "type", path);
        if (!string.Equals(type, "boolean", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{path}.type must be \"boolean\".");
        }

        var normalized = new JsonObject
        {
            ["type"] = "boolean",
        };

        AddOptionalString(normalized, "title", schema, path);
        AddOptionalString(normalized, "description", schema, path);
        AddOptionalBoolean(normalized, "default", schema, path);
        return normalized;
    }

    private static JsonObject NormalizeLegacyEnumSchema(JsonElement schema, string path)
    {
        ValidateAllowedProperties(schema, LegacyEnumFields, path);
        RequireString(schema, "type", path, "string");

        var normalized = new JsonObject
        {
            ["type"] = "string",
            ["enum"] = RequireStringArray(schema, "enum", path),
        };

        AddOptionalString(normalized, "title", schema, path);
        AddOptionalString(normalized, "description", schema, path);
        AddOptionalStringArray(normalized, "enumNames", schema, path, allowEmpty: true);
        AddOptionalString(normalized, "default", schema, path);
        return normalized;
    }

    private static JsonObject NormalizeSingleSelectSchema(JsonElement schema, string path)
    {
        ValidateAllowedProperties(schema, SingleSelectFields, path);
        RequireString(schema, "type", path, "string");

        var normalized = new JsonObject
        {
            ["type"] = "string",
            ["oneOf"] = RequireConstOptions(schema, "oneOf", path),
        };

        AddOptionalString(normalized, "title", schema, path);
        AddOptionalString(normalized, "description", schema, path);
        AddOptionalString(normalized, "default", schema, path);
        return normalized;
    }

    private static JsonObject NormalizeMultiSelectSchema(JsonElement schema, string path)
    {
        ValidateAllowedProperties(schema, MultiSelectFields, path);
        RequireString(schema, "type", path, "array");

        var normalized = new JsonObject
        {
            ["type"] = "array",
        };

        AddOptionalString(normalized, "title", schema, path);
        AddOptionalString(normalized, "description", schema, path);
        AddOptionalUInt64(normalized, "minItems", schema, path);
        AddOptionalUInt64(normalized, "maxItems", schema, path);
        normalized["items"] = NormalizeMultiSelectItems(RequireObject(schema, "items", path), $"{path}.items");
        AddOptionalStringArray(normalized, "default", schema, path, allowEmpty: true);
        return normalized;
    }

    private static JsonObject NormalizeMultiSelectItems(JsonElement items, string path)
    {
        if (items.TryGetProperty("enum", out _))
        {
            ValidateAllowedProperties(items, UntitledEnumItemsFields, path);
            RequireString(items, "type", path, "string");
            return new JsonObject
            {
                ["type"] = "string",
                ["enum"] = RequireStringArray(items, "enum", path),
            };
        }

        ValidateAllowedProperties(items, TitledEnumItemsFields, path);
        if (items.TryGetProperty("anyOf", out _) && items.TryGetProperty("oneOf", out _))
        {
            throw new InvalidOperationException($"{path} cannot contain both anyOf and oneOf.");
        }

        if (items.TryGetProperty("anyOf", out _))
        {
            return new JsonObject
            {
                ["anyOf"] = RequireConstOptions(items, "anyOf", path),
            };
        }

        if (items.TryGetProperty("oneOf", out _))
        {
            return new JsonObject
            {
                ["anyOf"] = RequireConstOptions(items, "oneOf", path),
            };
        }

        throw new InvalidOperationException($"{path} must contain enum, anyOf, or oneOf.");
    }

    private static JsonArray RequireConstOptions(JsonElement json, string propertyName, string path)
    {
        var array = RequireArray(json, propertyName, path);
        var normalized = new JsonArray();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"{path}.{propertyName} entries must be objects.");
            }

            ValidateAllowedProperties(item, ConstOptionFields, $"{path}.{propertyName}");
            normalized.Add(new JsonObject
            {
                ["const"] = RequireString(item, "const", $"{path}.{propertyName}"),
                ["title"] = RequireString(item, "title", $"{path}.{propertyName}"),
            });
        }

        return normalized;
    }

    private static JsonArray RequireStringArray(JsonElement json, string propertyName, string path)
    {
        var array = RequireArray(json, propertyName, path);
        var normalized = new JsonArray();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException($"{path}.{propertyName} entries must be strings.");
            }

            normalized.Add(item.GetString());
        }

        return normalized;
    }

    private static void AddOptionalString(JsonObject target, string propertyName, JsonElement source, string path)
    {
        if (!source.TryGetProperty(propertyName, out var property))
        {
            return;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"{path}.{propertyName} must be a string.");
        }

        target[propertyName] = property.GetString();
    }

    private static void AddOptionalStringArray(JsonObject target, string propertyName, JsonElement source, string path, bool allowEmpty)
    {
        if (!source.TryGetProperty(propertyName, out _))
        {
            return;
        }

        var normalized = RequireStringArray(source, propertyName, path);
        if (!allowEmpty && normalized.Count == 0)
        {
            throw new InvalidOperationException($"{path}.{propertyName} must not be empty.");
        }

        target[propertyName] = normalized;
    }

    private static void AddOptionalUInt32(JsonObject target, string propertyName, JsonElement source, string path)
    {
        if (!source.TryGetProperty(propertyName, out var property))
        {
            return;
        }

        if (!property.TryGetUInt32(out var value))
        {
            throw new InvalidOperationException($"{path}.{propertyName} must be an unsigned integer.");
        }

        target[propertyName] = value;
    }

    private static void AddOptionalUInt64(JsonObject target, string propertyName, JsonElement source, string path)
    {
        if (!source.TryGetProperty(propertyName, out var property))
        {
            return;
        }

        if (!property.TryGetUInt64(out var value))
        {
            throw new InvalidOperationException($"{path}.{propertyName} must be an unsigned integer.");
        }

        target[propertyName] = value;
    }

    private static void AddOptionalDouble(JsonObject target, string propertyName, JsonElement source, string path)
    {
        if (!source.TryGetProperty(propertyName, out var property))
        {
            return;
        }

        if (!property.TryGetDouble(out var value))
        {
            throw new InvalidOperationException($"{path}.{propertyName} must be a number.");
        }

        target[propertyName] = value;
    }

    private static void AddOptionalBoolean(JsonObject target, string propertyName, JsonElement source, string path)
    {
        if (!source.TryGetProperty(propertyName, out var property))
        {
            return;
        }

        if (property.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw new InvalidOperationException($"{path}.{propertyName} must be a boolean.");
        }

        target[propertyName] = property.GetBoolean();
    }

    private static void AddOptionalFormat(JsonObject target, JsonElement source, string path)
    {
        if (!source.TryGetProperty("format", out var property))
        {
            return;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"{path}.format must be a string.");
        }

        var format = property.GetString() ?? string.Empty;
        if (!AllowedStringFormats.Contains(format))
        {
            throw new InvalidOperationException($"{path}.format must be one of email, uri, date, or date-time.");
        }

        target["format"] = format;
    }

    private static string RequireString(JsonElement json, string propertyName, string path, string? expectedValue = null)
    {
        if (!json.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"{path}.{propertyName} must be a string.");
        }

        var value = property.GetString() ?? string.Empty;
        if (expectedValue is not null && !string.Equals(value, expectedValue, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{path}.{propertyName} must be \"{expectedValue}\".");
        }

        return value;
    }

    private static JsonElement RequireObject(JsonElement json, string propertyName, string path)
    {
        if (!json.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"{path}.{propertyName} must be an object.");
        }

        return property;
    }

    private static JsonElement RequireArray(JsonElement json, string propertyName, string path)
    {
        if (!json.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"{path}.{propertyName} must be an array.");
        }

        return property;
    }

    private static void ValidateAllowedProperties(JsonElement json, HashSet<string> allowedProperties, string path)
    {
        foreach (var property in json.EnumerateObject())
        {
            if (!allowedProperties.Contains(property.Name))
            {
                throw new InvalidOperationException($"{path} contains unsupported field '{property.Name}'.");
            }
        }
    }
}
