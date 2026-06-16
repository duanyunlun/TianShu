using System.Text.Json;
using System.Text.Json.Nodes;
using TianShu.Provider.Abstractions;

namespace TianShu.Provider.OpenAI;

/// <summary>
/// OpenAI Responses 工具面构建器。
/// Tool-surface builder for the OpenAI Responses API.
/// </summary>
public sealed class OpenAiResponsesToolSurfaceBuilder : IProviderResponsesToolSurfaceBuilder
{
    /// <inheritdoc />
    public string WireApi => "responses";

    /// <inheritdoc />
    public IReadOnlyList<object> Build(ProviderResponsesToolSurfaceBuilderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Definitions);

        var tools = new List<object>(context.Definitions.Count);
        foreach (var definition in context.Definitions)
        {
            ArgumentNullException.ThrowIfNull(definition);
            tools.Add(BuildTool(definition));
        }

        return tools;
    }

    private static object BuildTool(ProviderResponsesToolDefinition definition)
    {
        return definition switch
        {
            ProviderResponsesFunctionToolDefinition function => BuildFunctionTool(function),
            ProviderResponsesCustomToolDefinition custom => BuildCustomTool(custom),
            ProviderResponsesHostedToolDefinition hosted => BuildHostedTool(hosted),
            _ => throw new InvalidOperationException($"未知的 Responses 工具定义类型：{definition.GetType().FullName}"),
        };
    }

    private static object BuildFunctionTool(ProviderResponsesFunctionToolDefinition definition)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = "function",
            ["name"] = definition.Name,
            ["description"] = definition.Description,
            ["strict"] = definition.Strict,
            ["parameters"] = OpenAiResponsesJsonSchemaSanitizer.Sanitize(definition.InputSchema),
        };

        var outputSchema = BuildOutputSchema(definition.OutputSchema, definition.OutputShape);
        if (outputSchema is { } schema)
        {
            payload["output_schema"] = schema;
        }

        return payload;
    }

    private static object BuildCustomTool(ProviderResponsesCustomToolDefinition definition)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = "custom",
            ["name"] = definition.Name,
            ["description"] = definition.Description,
            ["format"] = JsonSerializer.Deserialize<object?>(definition.Format.GetRawText()),
        };

        var outputSchema = BuildOutputSchema(definition.OutputSchema, definition.OutputShape);
        if (outputSchema is { } schema)
        {
            payload["output_schema"] = schema;
        }

        return payload;
    }

    private static object BuildHostedTool(ProviderResponsesHostedToolDefinition definition)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = definition.ToolType,
        };

        if (!string.IsNullOrWhiteSpace(definition.Description))
        {
            payload["description"] = definition.Description;
        }

        if (definition.InputSchema is { } inputSchema)
        {
            payload["parameters"] = OpenAiResponsesJsonSchemaSanitizer.Sanitize(inputSchema);
        }

        var outputSchema = BuildOutputSchema(definition.OutputSchema, definition.OutputShape);
        if (outputSchema is { } schema)
        {
            payload["output_schema"] = schema;
        }

        if (definition.Strict.HasValue)
        {
            payload["strict"] = definition.Strict.Value;
        }

        if (!string.IsNullOrWhiteSpace(definition.Execution))
        {
            payload["execution"] = definition.Execution;
        }

        if (definition.ExternalWebAccess.HasValue)
        {
            payload["external_web_access"] = definition.ExternalWebAccess.Value;
        }

        if (definition.SearchContentTypes is { Count: > 0 })
        {
            payload["search_content_types"] = definition.SearchContentTypes.ToArray();
        }

        if (!string.IsNullOrWhiteSpace(definition.OutputFormat))
        {
            payload["output_format"] = definition.OutputFormat;
        }

        return payload;
    }

    private static JsonElement? BuildOutputSchema(JsonElement? schema, ProviderResponsesToolOutputShape shape)
    {
        return shape switch
        {
            ProviderResponsesToolOutputShape.DirectSchema when schema is { } directSchema
                => OpenAiResponsesJsonSchemaSanitizer.Sanitize(directSchema),
            ProviderResponsesToolOutputShape.DirectSchema => null,
            ProviderResponsesToolOutputShape.McpToolResultEnvelope
                => BuildMcpToolResultEnvelopeSchema(schema),
            _ => throw new InvalidOperationException($"未知的 Responses 输出 schema 形状：{shape}"),
        };
    }

    private static JsonElement BuildMcpToolResultEnvelopeSchema(JsonElement? structuredContentSchema)
    {
        var structuredContent = structuredContentSchema is { } schema
            ? JsonSerializer.Deserialize<object?>(schema.GetRawText())
            : new Dictionary<string, object?>(StringComparer.Ordinal);

        return JsonSerializer.SerializeToElement(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["content"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["type"] = "array",
                    ["items"] = new Dictionary<string, object?>(StringComparer.Ordinal),
                },
                ["structuredContent"] = structuredContent,
                ["isError"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["type"] = "boolean",
                },
                ["_meta"] = new Dictionary<string, object?>(StringComparer.Ordinal),
            },
            ["required"] = new[] { "content" },
            ["additionalProperties"] = false,
        });
    }
}

internal static class OpenAiResponsesJsonSchemaSanitizer
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
