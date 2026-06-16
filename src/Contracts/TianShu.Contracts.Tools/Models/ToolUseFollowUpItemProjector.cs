using System.Text.Json;

namespace TianShu.Contracts.Tools;

/// <summary>
/// ToolUse provider item 投影器，统一维护模型工具调用与输出回填给 provider 的线形状。
/// ToolUse provider item projector that owns provider-facing tool call and output item shapes.
/// </summary>
public static class ToolUseFollowUpItemProjector
{
    /// <summary>
    /// 构造 function tool call item。
    /// Builds a function tool call item.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> BuildFunctionCallItem(
        string name,
        string arguments,
        string callId)
    {
        return new Dictionary<string, object?>
        {
            ["type"] = "function_call",
            ["name"] = name,
            ["arguments"] = arguments,
            ["call_id"] = callId,
        };
    }

    /// <summary>
    /// 构造 function/custom tool call output follow-up item。
    /// Builds a function/custom tool call output follow-up item.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> BuildFunctionCallOutputItem(
        string callId,
        bool isCustomToolCall,
        object? output)
    {
        return new Dictionary<string, object?>
        {
            ["type"] = isCustomToolCall ? "custom_tool_call_output" : "function_call_output",
            ["call_id"] = callId,
            ["output"] = output,
        };
    }

    /// <summary>
    /// 构造 tool_search output follow-up item。
    /// Builds a tool_search output follow-up item.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> BuildToolSearchOutputItem(
        string callId,
        IReadOnlyList<JsonElement> tools)
    {
        return new Dictionary<string, object?>
        {
            ["type"] = "tool_search_output",
            ["call_id"] = callId,
            ["status"] = "completed",
            ["execution"] = "client",
            ["tools"] = tools.Select(static item => item.Clone()).ToArray(),
        };
    }

    /// <summary>
    /// 构造取消后的 function/custom tool call output follow-up item。
    /// Builds a cancelled function/custom tool call output follow-up item.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> BuildCancelledFunctionCallOutputItem(
        string callId,
        string toolName,
        bool isCustomToolCall,
        double elapsedSeconds)
    {
        return BuildFunctionCallOutputItem(
            callId,
            isCustomToolCall,
            BuildToolAbortMessage(toolName, elapsedSeconds));
    }

    /// <summary>
    /// 构造 function/custom tool call output 的 output 字段。
    /// Builds the output field for a function/custom tool call output item.
    /// </summary>
    public static object BuildFunctionCallOutputPayload(
        string? outputText,
        IReadOnlyList<ToolOutputContentItem>? outputContentItems)
    {
        if (outputContentItems is null || outputContentItems.Count == 0)
        {
            return outputText ?? string.Empty;
        }

        return outputContentItems.Select(ToWireItem).ToArray();
    }

    /// <summary>
    /// 从内容项构造文本预览。
    /// Builds a text preview from output content items.
    /// </summary>
    public static string BuildTextPreview(IReadOnlyList<ToolOutputContentItem>? outputContentItems)
    {
        if (outputContentItems is null || outputContentItems.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var item in outputContentItems)
        {
            if (!string.Equals(Normalize(item.Type), "input_text", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = Normalize(item.Text);
            if (!string.IsNullOrWhiteSpace(text))
            {
                parts.Add(text!);
            }
        }

        return string.Join(Environment.NewLine, parts);
    }

    /// <summary>
    /// 构造模型工具调用在运行时使用的稳定 item id。
    /// Builds the stable runtime item id for a model tool call.
    /// </summary>
    public static string BuildModelToolCallItemId(string callId, string toolName)
    {
        var safeCallId = BuildSafeToolIdentifierSegment(callId, "call", maxLength: 48);
        var safeTool = BuildSafeToolIdentifierSegment(toolName, "tool");

        return $"tool_{safeTool}_{safeCallId}";
    }

    /// <summary>
    /// 从 tool_search 工具结果中提取 provider follow-up 所需的 tools 数组。
    /// Extracts the tools array needed by provider follow-up items from a tool_search result.
    /// </summary>
    public static IReadOnlyList<JsonElement> ExtractToolSearchOutputTools(
        bool success,
        string? outputText,
        JsonElement? structuredOutput)
    {
        if (!success)
        {
            return Array.Empty<JsonElement>();
        }

        if (structuredOutput is { ValueKind: JsonValueKind.Object } structured
            && structured.TryGetProperty("tools", out var structuredTools)
            && structuredTools.ValueKind == JsonValueKind.Array)
        {
            return structuredTools.EnumerateArray().Select(static item => item.Clone()).ToArray();
        }

        try
        {
            using var document = JsonDocument.Parse(outputText ?? string.Empty);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("tools", out var tools)
                || tools.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<JsonElement>();
            }

            return tools.EnumerateArray().Select(static item => item.Clone()).ToArray();
        }
        catch (JsonException)
        {
            return Array.Empty<JsonElement>();
        }
    }

    private static string BuildToolAbortMessage(string toolName, double elapsedSeconds)
    {
        var normalized = string.IsNullOrWhiteSpace(toolName) ? string.Empty : toolName.Trim();
        return normalized switch
        {
            "shell" or "container.exec" or "local_shell" or "shell_command" or "unified_exec"
                => $"Wall time: {Math.Max(elapsedSeconds, 0.1):0.0} seconds\naborted by user",
            _ => $"aborted by user after {Math.Max(elapsedSeconds, 0.1):0.0}s",
        };
    }

    private static Dictionary<string, object?> ToWireItem(ToolOutputContentItem item)
    {
        var type = Normalize(item.Type) ?? "input_text";
        return type switch
        {
            "input_image" => BuildInputImageItem(item),
            _ => new Dictionary<string, object?>
            {
                ["type"] = "input_text",
                ["text"] = item.Text ?? string.Empty,
            },
        };
    }

    private static Dictionary<string, object?> BuildInputImageItem(ToolOutputContentItem item)
    {
        var payload = new Dictionary<string, object?>
        {
            ["type"] = "input_image",
            ["image_url"] = item.ImageUrl ?? string.Empty,
        };

        var detail = Normalize(item.Detail);
        if (!string.IsNullOrWhiteSpace(detail))
        {
            payload["detail"] = detail;
        }

        return payload;
    }

    private static string BuildSafeToolIdentifierSegment(string value, string fallback, int? maxLength = null)
    {
        var source = string.IsNullOrWhiteSpace(value) ? fallback : value;
        var length = maxLength.HasValue ? Math.Min(source.Length, maxLength.Value) : source.Length;
        return string.Create(
            length,
            source,
            static (destination, text) =>
            {
                for (var i = 0; i < destination.Length; i++)
                {
                    var c = text[i];
                    destination[i] = char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_';
                }
            });
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
