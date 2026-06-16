using System.Text;
using System.Text.Json;
using TianShu.Provider.Abstractions;

namespace TianShu.AppHost.Tools.Runtime;

internal static class KernelCodeModeRuntimeSupport
{
    private const long MaxJsSafeInteger = (1L << 53) - 1;
    private const string CodeModeDescriptionTemplate = """
## exec
- Runs raw JavaScript in an isolated context (no Node, no file system, or network access, no console).
- Send raw JavaScript source text, not JSON, quoted strings, or markdown code fences.
- You may optionally start the tool input with a first-line pragma like `// @exec: {"yield_time_ms": 10000, "max_output_tokens": 1000}`.
- `yield_time_ms` asks `exec` to yield early after that many milliseconds if the script is still running.
- `max_output_tokens` sets the token budget for direct `exec` results. By default the result is truncated to 10000 tokens.
- All nested tools are available on the global `tools` object, for example `await tools.some_tool(...)`. Tool names are exposed as normalized JavaScript identifiers, for example `await tools.mcp__ologs__get_profile(...)`.
- Tool methods take either string or object as parameter.
- They return either a structured value or a string based on the description above.

- Global helpers:
- `text(value: string | number | boolean | undefined | null)`: Appends a text item and returns it. Non-string values are stringified with `JSON.stringify(...)` when possible.
- `image(imageUrl: string)`: Appends an image item and returns it. `image_url` can be an HTTPS URL or a base64-encoded `data:` URL.
- `store(key: string, value: any)`: stores a serializable value under a string key for later `exec` calls in the same session.
- `load(key: string)`: returns the stored value for a string key, or `undefined` if it is missing.
- `ALL_TOOLS`: metadata for the enabled nested tools as `{ name, description }` entries.
- `yield_control()`: yields the accumulated output to the model immediately while the script keeps running.
""";
    private const string FreeformGrammar = """
start: pragma_source | plain_source
pragma_source: PRAGMA_LINE NEWLINE SOURCE
plain_source: SOURCE

PRAGMA_LINE: /[ \t]*\/\/ @exec:[^\r\n]*/
NEWLINE: /\r?\n/
SOURCE: /[\s\S]+/
""";

    private static readonly JsonElement InputFormat = JsonSerializer.SerializeToElement(new
    {
        type = "grammar",
        syntax = "lark",
        definition = FreeformGrammar,
    });

    public static ProviderResponsesToolDefinition BuildExecProviderToolDefinition(IReadOnlyList<string>? enabledToolNames)
        => new ProviderResponsesCustomToolDefinition(
            "exec",
            BuildDescription(enabledToolNames),
            InputFormat);

    internal static KernelCodeModeParseResult ParseExecFreeformInput(string input)
    {
        var normalized = input ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return KernelCodeModeParseResult.FromError(
                "exec expects raw JavaScript source text (non-empty). Provide JS only, optionally with first-line `// @exec: {\"yield_time_ms\": 10000, \"max_output_tokens\": 1000}`.");
        }

        var trimmed = normalized.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return KernelCodeModeParseResult.FromError(
                "exec expects raw JavaScript source text. Do not send markdown code fences.");
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (document.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.String)
            {
                return KernelCodeModeParseResult.FromError(
                    "exec expects raw JavaScript source text. Do not send JSON or quoted code.");
            }
        }
        catch (JsonException)
        {
        }

        var firstNewline = normalized.IndexOf('\n');
        if (firstNewline < 0)
        {
            return KernelCodeModeParseResult.FromRequest(new KernelCodeModeExecutionRequest(normalized, null, null));
        }

        var firstLine = normalized[..firstNewline];
        var rest = normalized[(firstNewline + 1)..];
        var trimmedFirstLine = firstLine.TrimStart();
        if (!trimmedFirstLine.StartsWith("// @exec:", StringComparison.Ordinal))
        {
            return KernelCodeModeParseResult.FromRequest(new KernelCodeModeExecutionRequest(normalized, null, null));
        }

        if (string.IsNullOrWhiteSpace(rest))
        {
            return KernelCodeModeParseResult.FromError(
                "exec pragma must be followed by JavaScript source on subsequent lines");
        }

        var pragmaText = trimmedFirstLine["// @exec:".Length..].Trim();
        if (string.IsNullOrWhiteSpace(pragmaText))
        {
            return KernelCodeModeParseResult.FromError(
                "exec pragma must be a JSON object with supported fields `yield_time_ms` and `max_output_tokens`");
        }

        JsonDocument pragmaDocument;
        try
        {
            pragmaDocument = JsonDocument.Parse(pragmaText);
        }
        catch (JsonException ex)
        {
            return KernelCodeModeParseResult.FromError(
                $"exec pragma must be valid JSON with supported fields `yield_time_ms` and `max_output_tokens`: {ex.Message}");
        }

        using (pragmaDocument)
        {
            if (pragmaDocument.RootElement.ValueKind != JsonValueKind.Object)
            {
                return KernelCodeModeParseResult.FromError(
                    "exec pragma must be a JSON object with supported fields `yield_time_ms` and `max_output_tokens`");
            }

            int? yieldTimeMs = null;
            int? maxOutputTokens = null;
            foreach (var property in pragmaDocument.RootElement.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "yield_time_ms":
                    {
                        if (!TryReadSafeInteger(property.Value, out var parsedYieldTime))
                        {
                            return KernelCodeModeParseResult.FromError(
                                "exec pragma field `yield_time_ms` must be a non-negative safe integer");
                        }

                        yieldTimeMs = parsedYieldTime;
                        break;
                    }
                    case "max_output_tokens":
                    {
                        if (!TryReadSafeInteger(property.Value, out var parsedMaxOutputTokens))
                        {
                            return KernelCodeModeParseResult.FromError(
                                "exec pragma field `max_output_tokens` must be a non-negative safe integer");
                        }

                        maxOutputTokens = parsedMaxOutputTokens;
                        break;
                    }
                    default:
                        return KernelCodeModeParseResult.FromError(
                            $"exec pragma only supports `yield_time_ms` and `max_output_tokens`; got `{property.Name}`");
                }
            }

            return KernelCodeModeParseResult.FromRequest(new KernelCodeModeExecutionRequest(rest, yieldTimeMs, maxOutputTokens));
        }
    }

    private static bool TryReadSafeInteger(JsonElement value, out int? parsed)
    {
        parsed = null;
        long? candidate = value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var integerValue) => integerValue,
            JsonValueKind.String when long.TryParse(value.GetString(), out var integerValue) => integerValue,
            _ => null,
        };
        if (candidate is null || candidate < 0 || candidate > MaxJsSafeInteger || candidate > int.MaxValue)
        {
            return false;
        }

        parsed = (int)candidate.Value;
        return true;
    }

    private static string BuildDescription(IReadOnlyList<string>? enabledToolNames)
    {
        var enabledList = enabledToolNames is { Count: > 0 }
            ? string.Join(", ", enabledToolNames)
            : "none";
        return $"{CodeModeDescriptionTemplate.TrimEnd()}{Environment.NewLine}- Enabled nested tools: {enabledList}.";
    }

    private const string WaitDescriptionTemplate = """
- Use `exec_wait` only after `exec` returns `Script running with cell ID ...`.
- `cell_id` identifies the running `exec` cell to resume.
- `yield_time_ms` controls how long to wait for more output before yielding again. If omitted, `exec_wait` uses its default wait timeout.
- `max_tokens` limits how much new output this wait call returns.
- `terminate: true` stops the running cell instead of waiting for more output.
- `exec_wait` returns only the new output since the last yield, or the final completion or termination result for that cell.
- If the cell is still running, `exec_wait` may yield again with the same `cell_id`.
- If the cell has already finished, `exec_wait` returns the completed result and closes the cell.
""";

    private static readonly JsonElement ExecWaitInputSchemaElement = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            cell_id = new { type = "string" },
            yield_time_ms = new { type = "number" },
            max_tokens = new { type = "number" },
            terminate = new { type = "boolean" },
        },
        required = new[] { "cell_id" },
        additionalProperties = false,
    });

    internal static string ExecWaitDescription =>
        $"Waits on a yielded `exec` cell and returns new output or completion.{Environment.NewLine}{WaitDescriptionTemplate.Trim()}";

    internal static JsonElement ExecWaitInputSchema => ExecWaitInputSchemaElement.Clone();
}

internal sealed record KernelCodeModeParseResult(KernelCodeModeExecutionRequest? Request, string? Error)
{
    public bool Success => Request is not null && string.IsNullOrWhiteSpace(Error);

    public static KernelCodeModeParseResult FromRequest(KernelCodeModeExecutionRequest request) => new(request, null);

    public static KernelCodeModeParseResult FromError(string error) => new(null, error);
}

internal static class KernelCodeModeDescriptionBuilder
{
    public static string BuildNestedToolDescription(
        string description,
        string toolName,
        bool isFreeform,
        JsonElement inputSchema,
        JsonElement? outputSchema)
    {
        if (string.Equals(toolName, "exec", StringComparison.Ordinal))
        {
            return description;
        }

        var inputType = isFreeform
            ? "string"
            : RenderJsonSchemaToTypeScript(inputSchema);
        var outputType = !isFreeform && outputSchema is { } schema
            ? RenderJsonSchemaToTypeScript(schema)
            : "unknown";
        var declaration = RenderToolDeclaration(toolName, isFreeform ? "input" : "args", inputType, outputType);
        return $"{description}{Environment.NewLine}{Environment.NewLine}Code mode declaration:{Environment.NewLine}```ts{Environment.NewLine}{declaration}{Environment.NewLine}```";
    }

    public static string NormalizeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "_";
        }

        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            var isValid = index == 0
                ? ch is '_' or '$' || char.IsAsciiLetter(ch)
                : ch is '_' or '$' || char.IsAsciiLetterOrDigit(ch);
            builder.Append(isValid ? ch : '_');
        }

        return builder.Length == 0 ? "_" : builder.ToString();
    }

    private static string RenderToolDeclaration(string toolName, string inputName, string inputType, string outputType)
    {
        var normalizedName = NormalizeIdentifier(toolName);
        return $"declare const tools: {{{Environment.NewLine}  {normalizedName}({inputName}: {IndentMultilineType(inputType, 2)}): Promise<{IndentMultilineType(outputType, 2)}>;{Environment.NewLine}}};";
    }

    private static string IndentMultilineType(string value, int spaces)
    {
        var indent = new string(' ', spaces);
        return string.Join(
            Environment.NewLine,
            value.Split(["\r\n", "\n"], StringSplitOptions.None)
                .Select((line, index) => index == 0 ? line : indent + line));
    }

    private static string RenderJsonSchemaToTypeScript(JsonElement schema)
    {
        return RenderJsonSchemaToTypeScriptInner(schema, 0);
    }

    private static string RenderJsonSchemaToTypeScriptInner(JsonElement schema, int indent)
    {
        return schema.ValueKind switch
        {
            JsonValueKind.True => "unknown",
            JsonValueKind.False => "never",
            JsonValueKind.Object => RenderObjectSchema(schema, indent),
            _ => "unknown",
        };
    }

    private static string RenderObjectSchema(JsonElement schema, int indent)
    {
        if (TryGetProperty(schema, "const", out var constValue))
        {
            return JsonSerializer.Serialize(constValue);
        }

        if (TryGetProperty(schema, "enum", out var enumValue) && enumValue.ValueKind == JsonValueKind.Array)
        {
            var variants = enumValue.EnumerateArray().Select(static item => JsonSerializer.Serialize(item)).ToArray();
            if (variants.Length > 0)
            {
                return string.Join(" | ", variants);
            }
        }

        foreach (var unionKey in new[] { "anyOf", "oneOf" })
        {
            if (TryGetProperty(schema, unionKey, out var unionValue) && unionValue.ValueKind == JsonValueKind.Array)
            {
                var variants = unionValue.EnumerateArray().Select(item => RenderJsonSchemaToTypeScriptInner(item, indent)).ToArray();
                if (variants.Length > 0)
                {
                    return string.Join(" | ", variants);
                }
            }
        }

        if (TryGetProperty(schema, "allOf", out var allOfValue) && allOfValue.ValueKind == JsonValueKind.Array)
        {
            var parts = allOfValue.EnumerateArray().Select(item => RenderJsonSchemaToTypeScriptInner(item, indent)).ToArray();
            if (parts.Length > 0)
            {
                return string.Join(" & ", parts);
            }
        }

        if (TryGetProperty(schema, "type", out var typeValue))
        {
            if (typeValue.ValueKind == JsonValueKind.Array)
            {
                var types = typeValue.EnumerateArray()
                    .Where(static item => item.ValueKind == JsonValueKind.String)
                    .Select(item => RenderSchemaTypeKeyword(schema, item.GetString()!, indent))
                    .ToArray();
                if (types.Length > 0)
                {
                    return string.Join(" | ", types);
                }
            }

            if (typeValue.ValueKind == JsonValueKind.String)
            {
                return RenderSchemaTypeKeyword(schema, typeValue.GetString()!, indent);
            }
        }

        if (schema.TryGetProperty("properties", out _)
            || schema.TryGetProperty("additionalProperties", out _)
            || schema.TryGetProperty("required", out _))
        {
            return RenderSchemaObjectType(schema, indent);
        }

        if (schema.TryGetProperty("items", out _)
            || schema.TryGetProperty("prefixItems", out _))
        {
            return RenderSchemaArrayType(schema, indent);
        }

        return "unknown";
    }

    private static string RenderSchemaTypeKeyword(JsonElement schema, string schemaType, int indent)
    {
        return schemaType switch
        {
            "string" => "string",
            "number" or "integer" => "number",
            "boolean" => "boolean",
            "null" => "null",
            "array" => RenderSchemaArrayType(schema, indent),
            "object" => RenderSchemaObjectType(schema, indent),
            _ => "unknown",
        };
    }

    private static string RenderSchemaArrayType(JsonElement schema, int indent)
    {
        if (TryGetProperty(schema, "items", out var itemsValue))
        {
            return $"Array<{RenderJsonSchemaToTypeScriptInner(itemsValue, indent + 2)}>";
        }

        if (TryGetProperty(schema, "prefixItems", out var prefixItems) && prefixItems.ValueKind == JsonValueKind.Array)
        {
            var itemTypes = prefixItems.EnumerateArray().Select(item => RenderJsonSchemaToTypeScriptInner(item, indent + 2)).ToArray();
            if (itemTypes.Length > 0)
            {
                return $"[{string.Join(", ", itemTypes)}]";
            }
        }

        return "unknown[]";
    }

    private static string RenderSchemaObjectType(JsonElement schema, int indent)
    {
        var required = TryGetProperty(schema, "required", out var requiredValue) && requiredValue.ValueKind == JsonValueKind.Array
            ? requiredValue.EnumerateArray()
                .Where(static item => item.ValueKind == JsonValueKind.String)
                .Select(static item => item.GetString()!)
                .ToHashSet(StringComparer.Ordinal)
            : [];

        var lines = new List<string>();
        if (TryGetProperty(schema, "properties", out var propertiesValue) && propertiesValue.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in propertiesValue.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal))
            {
                var optionalMarker = required.Contains(property.Name) ? string.Empty : "?";
                var propertyType = RenderJsonSchemaToTypeScriptInner(property.Value, indent + 2);
                lines.Add($"{new string(' ', indent + 2)}{RenderPropertyName(property.Name)}{optionalMarker}: {propertyType};");
            }
        }

        if (TryGetProperty(schema, "additionalProperties", out var additionalProperties))
        {
            var additionalType = additionalProperties.ValueKind switch
            {
                JsonValueKind.True => "unknown",
                JsonValueKind.False => null,
                _ => RenderJsonSchemaToTypeScriptInner(additionalProperties, indent + 2),
            };
            if (!string.IsNullOrWhiteSpace(additionalType))
            {
                lines.Add($"{new string(' ', indent + 2)}[key: string]: {additionalType};");
            }
        }
        else if (lines.Count == 0)
        {
            lines.Add($"{new string(' ', indent + 2)}[key: string]: unknown;");
        }

        return lines.Count == 0
            ? "{}"
            : $"{{{Environment.NewLine}{string.Join(Environment.NewLine, lines)}{Environment.NewLine}{new string(' ', indent)}}}";
    }

    private static string RenderPropertyName(string name)
    {
        return NormalizeIdentifier(name) == name
            ? name
            : JsonSerializer.Serialize(name);
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement property)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out property))
        {
            return true;
        }

        property = default;
        return false;
    }
}
