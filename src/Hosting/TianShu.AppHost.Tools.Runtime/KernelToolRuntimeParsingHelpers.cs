using System.Text.Json;
using System.Text.RegularExpressions;

namespace TianShu.AppHost.Tools.Runtime;

internal static class KernelToolRuntimeParsingHelpers
{
    private static readonly Regex InlineToolRegex = new(
        @"^\s*/tool\s+(?<name>[a-zA-Z0-9_\-]+)\s*(?<args>\{.*\})?\s*$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    public static bool TryResolveDynamicToolSchema(
        IReadOnlyList<KernelDynamicToolDescriptor>? dynamicTools,
        string toolName,
        out JsonElement? schema)
    {
        schema = null;
        if (!KernelDynamicToolResolver.TryResolveDescriptor(dynamicTools, toolName, out var descriptor))
        {
            return false;
        }

        if (descriptor.InputSchema is { } inputSchema)
        {
            schema = inputSchema.Clone();
            return true;
        }

        schema = JsonSerializer.SerializeToElement(new { type = "object" });
        return true;
    }

    public static bool TryResolveDynamicToolDescriptor(
        IReadOnlyList<KernelDynamicToolDescriptor>? dynamicTools,
        string toolName,
        out KernelDynamicToolDescriptor descriptor)
        => KernelDynamicToolResolver.TryResolveDescriptor(dynamicTools, toolName, out descriptor);

    public static IReadOnlyList<KernelToolOutputContentItem>? ReadDynamicToolOutputContentItems(JsonElement response)
    {
        if (!TryReadJsonProperty(response, "contentItems", out var contentItems)
            || contentItems.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var items = new List<KernelToolOutputContentItem>();
        foreach (var item in contentItems.EnumerateArray())
        {
            var parsed = TryConvertDynamicToolContentItem(item);
            if (parsed is not null)
            {
                items.Add(parsed);
            }
        }

        return items.Count == 0 ? null : items;
    }

    public static IReadOnlyList<JsonElement>? ReadDynamicToolRawContentItems(JsonElement response)
    {
        if (!TryReadJsonProperty(response, "contentItems", out var contentItems)
            || contentItems.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return contentItems.EnumerateArray().Select(static item => item.Clone()).ToArray();
    }

    public static JsonElement? ReadDynamicToolStructuredOutput(JsonElement response)
    {
        var structuredOutput = TryReadJsonProperty(response, "structuredContent")
            ?? TryReadJsonProperty(response, "structured_content");
        return structuredOutput?.Clone();
    }

    public static JsonElement? ReadDynamicToolMetadata(JsonElement response)
    {
        var metadata = TryReadJsonProperty(response, "_meta")
            ?? TryReadJsonProperty(response, "meta");
        return metadata?.Clone();
    }

    public static KernelToolOutputContentItem? TryConvertDynamicToolContentItem(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var normalizedType = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(item, "type"));
        if (string.Equals(normalizedType, "input_image", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedType, "image", StringComparison.OrdinalIgnoreCase))
        {
            return new KernelToolOutputContentItem(
                Type: "input_image",
                ImageUrl: KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(item, "image_url"))
                          ?? KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(item, "imageUrl")),
                Detail: KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(item, "detail")));
        }

        var text = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(item, "text"))
            ?? KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(item, "content"));
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return new KernelToolOutputContentItem("input_text", Text: text);
    }

    public static string ExtractDynamicToolOutput(JsonElement response)
    {
        if (TryReadJsonProperty(response, "contentItems", out var contentItems)
            && contentItems.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var item in contentItems.EnumerateArray())
            {
                var text = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(item, "text"))
                    ?? KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(item, "content"));
                if (!string.IsNullOrWhiteSpace(text))
                {
                    parts.Add(text!);
                }
            }

            if (parts.Count > 0)
            {
                return string.Join(Environment.NewLine, parts);
            }
        }

        var output = TryReadJsonProperty(response, "output") ?? response;
        return output.ValueKind == JsonValueKind.String
            ? (KernelToolJsonHelpers.Normalize(output.GetString()) ?? string.Empty)
            : output.GetRawText();
    }

    public static bool TryParseInlineToolCall(string text, out string toolName, out JsonElement arguments)
    {
        toolName = string.Empty;
        arguments = JsonSerializer.SerializeToElement(new { });
        var normalized = KernelToolJsonHelpers.Normalize(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var match = InlineToolRegex.Match(normalized);
        if (!match.Success)
        {
            return false;
        }

        toolName = match.Groups["name"].Value;
        var rawArgs = KernelToolJsonHelpers.Normalize(match.Groups["args"].Value) ?? "{}";
        try
        {
            arguments = JsonDocument.Parse(rawArgs).RootElement.Clone();
            return arguments.ValueKind == JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadJsonProperty(JsonElement json, string propertyName, out JsonElement value)
    {
        value = default;
        return json.ValueKind == JsonValueKind.Object && json.TryGetProperty(propertyName, out value);
    }

    private static JsonElement? TryReadJsonProperty(JsonElement json, string propertyName)
        => TryReadJsonProperty(json, propertyName, out var value) ? value : null;
}
