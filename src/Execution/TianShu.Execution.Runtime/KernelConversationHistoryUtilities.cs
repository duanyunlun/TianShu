using System.Text.Json;

namespace TianShu.Execution.Runtime;

internal enum KernelPromptContentFormat
{
    PlainText = 0,
    Responses = 1,
    ChatCompletions = 2,
}

internal static class KernelConversationHistoryUtilities
{
    private static readonly HashSet<string> SupportedResponseItemTypes = new(StringComparer.Ordinal)
    {
        "message",
        "reasoning",
        "local_shell_call",
        "function_call",
        "tool_search_call",
        "function_call_output",
        "custom_tool_call",
        "custom_tool_call_output",
        "tool_search_output",
        "web_search_call",
        "image_generation_call",
        "ghost_snapshot",
        "compaction",
        "other",
    };

    public static bool HasMeaningfulContent(KernelConversationHistoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return !string.IsNullOrWhiteSpace(BuildDisplayText(item)) || item.Inputs.Count > 0;
    }

    public static bool HasReplayablePayload(KernelConversationHistoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.RawResponseItem is { ValueKind: JsonValueKind.Object } rawResponseItem
            ? TryNormalizeRawResponseItemForReplay(rawResponseItem, out _) || HasMeaningfulContent(item)
            : HasMeaningfulContent(item);
    }

    public static bool IsSupportedResponseItemPayload(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var rawType = KernelRuntimeJsonHelpers.Normalize(KernelRuntimeJsonHelpers.ReadString(item, "type"));
        return !string.IsNullOrWhiteSpace(rawType) && SupportedResponseItemTypes.Contains(rawType!);
    }

    public static bool IsReplayableRawResponseItem(JsonElement item)
        => TryNormalizeRawResponseItemForReplay(item, out _);

    public static bool TryNormalizeRawResponseItemForReplay(JsonElement item, out JsonElement normalizedItem)
    {
        normalizedItem = default;
        if (!IsSupportedResponseItemPayload(item))
        {
            return false;
        }

        var rawType = KernelRuntimeJsonHelpers.Normalize(KernelRuntimeJsonHelpers.ReadString(item, "type"));
        if (string.Equals(rawType, "reasoning", StringComparison.Ordinal))
        {
            // 本地线程历史会合成 `reasoning_<turnId>` 项用于 UI 跟踪，不能当作 provider 原生 response item 回放；
            // Responses API 只接受 provider 下发的 reasoning id（当前格式以 `rs` 开头）。
            var id = KernelRuntimeJsonHelpers.Normalize(KernelRuntimeJsonHelpers.ReadString(item, "id"));
            if (string.IsNullOrWhiteSpace(id) || !id.StartsWith("rs", StringComparison.Ordinal))
            {
                return false;
            }

            normalizedItem = item.Clone();
            return true;
        }

        if (string.Equals(rawType, "function_call_output", StringComparison.Ordinal)
            || string.Equals(rawType, "custom_tool_call_output", StringComparison.Ordinal))
        {
            return TryNormalizeToolOutputItem(item, out normalizedItem);
        }

        normalizedItem = item.Clone();
        return true;
    }

    public static KernelConversationHistoryItem Clone(KernelConversationHistoryItem source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new KernelConversationHistoryItem
        {
            Role = NormalizeConversationRole(source.Role),
            Content = KernelRuntimeJsonHelpers.Normalize(source.Content) ?? string.Empty,
            Inputs = (source.Inputs ?? [])
                .Select(CloneInput)
                .ToList(),
            RawResponseItem = source.RawResponseItem?.Clone(),
        };
    }

    public static KernelConversationHistoryItem? ParseHistoryItem(JsonElement item, bool strictResponseItem = false)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            if (strictResponseItem)
            {
                throw new JsonException("history item 必须是对象。");
            }

            return null;
        }

        var rawType = KernelRuntimeJsonHelpers.Normalize(KernelRuntimeJsonHelpers.ReadString(item, "type"));
        var hasLegacyFields =
            item.TryGetProperty("role", out _)
            || item.TryGetProperty("content", out _)
            || item.TryGetProperty("inputs", out _)
            || item.TryGetProperty("text", out _);
        if (strictResponseItem)
        {
            if (string.IsNullOrWhiteSpace(rawType))
            {
                throw new JsonException("history item 必须包含非空 type。");
            }

            if (!SupportedResponseItemTypes.Contains(rawType!))
            {
                throw new JsonException($"history item type 不受支持：{rawType}");
            }

            ValidateStrictResponseItemSchema(item, rawType!);
        }

        if (string.IsNullOrWhiteSpace(rawType) && !hasLegacyFields)
        {
            return null;
        }

        var inputs = ParseHistoryInputs(item);
        var content = ReadPreferredContent(item, inputs);
        if (string.IsNullOrWhiteSpace(content) && inputs.Count == 0 && string.IsNullOrWhiteSpace(rawType))
        {
            return null;
        }

        var rawResponseItem = TryNormalizeRawResponseItemForReplay(item, out var normalizedRawResponseItem)
            ? normalizedRawResponseItem
            : item.Clone();

        return new KernelConversationHistoryItem
        {
            Role = NormalizeConversationRole(KernelRuntimeJsonHelpers.ReadString(item, "role")),
            Content = content ?? string.Empty,
            Inputs = inputs.ToList(),
            RawResponseItem = rawResponseItem,
        };
    }

    public static object SerializeHistoryItem(KernelConversationHistoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.RawResponseItem is { ValueKind: JsonValueKind.Object } rawResponseItem
            && TryNormalizeRawResponseItemForReplay(rawResponseItem, out var normalizedRawResponseItem))
        {
            return normalizedRawResponseItem.Clone();
        }

        var payload = new Dictionary<string, object?>
        {
            ["role"] = NormalizeConversationRole(item.Role),
        };

        var content = KernelRuntimeJsonHelpers.Normalize(item.Content);
        if (!string.IsNullOrWhiteSpace(content))
        {
            payload["content"] = content;
        }

        if (item.Inputs.Count > 0)
        {
            payload["inputs"] = item.Inputs.Select(SerializeInput).ToArray();
        }

        return payload;
    }

    public static string? BuildDisplayText(KernelConversationHistoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.Inputs.Count > 0)
        {
            return BuildInputPreview(item.Inputs) ?? KernelRuntimeJsonHelpers.Normalize(item.Content);
        }

        return KernelRuntimeJsonHelpers.Normalize(item.Content);
    }

    public static string? BuildInputPreview(IReadOnlyList<KernelConversationInputRecord> inputs)
    {
        if (inputs.Count == 0)
        {
            return null;
        }

        var parts = new List<string>(inputs.Count);
        foreach (var input in inputs)
        {
            var preview = BuildInputPreview(input);
            if (!string.IsNullOrWhiteSpace(preview))
            {
                parts.Add(preview);
            }
        }

        return parts.Count == 0 ? null : string.Join(Environment.NewLine, parts);
    }

    public static IReadOnlyList<KernelConversationInputRecord> ParseHistoryInputs(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<KernelConversationInputRecord>();
        }

        if (TryGetArray(item, "inputs", out var inputs))
        {
            return ParseInputArray(inputs);
        }

        if (TryGetArray(item, "content", out var content))
        {
            return ParseInputArray(content);
        }

        return Array.Empty<KernelConversationInputRecord>();
    }

    public static IReadOnlyList<KernelConversationInputRecord> ParseInputItems(IEnumerable<JsonElement>? items)
    {
        if (items is null)
        {
            return Array.Empty<KernelConversationInputRecord>();
        }

        var parsed = new List<KernelConversationInputRecord>();
        foreach (var item in items)
        {
            var input = ParseInput(item);
            if (input is not null)
            {
                parsed.Add(input);
            }
        }

        return parsed;
    }

    public static IReadOnlyList<KernelConversationInputRecord> ParseInputItems(IEnumerable<KernelTurnInputItem>? items)
    {
        if (items is null)
        {
            return Array.Empty<KernelConversationInputRecord>();
        }

        var parsed = new List<KernelConversationInputRecord>();
        foreach (var item in items)
        {
            var input = ParseInput(item);
            if (input is not null)
            {
                parsed.Add(input);
            }
        }

        return parsed;
    }

    public static object[] BuildProviderContentItems(
        IReadOnlyList<KernelConversationInputRecord> inputs,
        KernelPromptContentFormat format)
    {
        if (format == KernelPromptContentFormat.PlainText || inputs.Count == 0)
        {
            return Array.Empty<object>();
        }

        var content = new List<object>(inputs.Count);
        foreach (var input in inputs)
        {
            foreach (var item in BuildProviderContentItems(input, format))
            {
                content.Add(item);
            }
        }

        return content.ToArray();
    }

    private static KernelConversationInputRecord CloneInput(KernelConversationInputRecord source)
        => new()
        {
            Type = KernelRuntimeJsonHelpers.Normalize(source.Type) ?? string.Empty,
            Text = KernelRuntimeJsonHelpers.Normalize(source.Text),
            Url = KernelRuntimeJsonHelpers.Normalize(source.Url),
            Path = KernelRuntimeJsonHelpers.Normalize(source.Path),
            Name = KernelRuntimeJsonHelpers.Normalize(source.Name),
            TextElements = (source.TextElements ?? [])
                .Select(CloneTextElement)
                .ToArray(),
        };

    private static KernelConversationTextElementRecord CloneTextElement(KernelConversationTextElementRecord source)
        => new(
            source.ByteRange is null
                ? null
                : new KernelConversationByteRangeRecord(
                    Math.Max(0, source.ByteRange.Start),
                    Math.Max(0, source.ByteRange.End)),
            KernelRuntimeJsonHelpers.Normalize(source.Placeholder));

    private static string NormalizeConversationRole(string? role)
    {
        var normalized = KernelRuntimeJsonHelpers.Normalize(role);
        return normalized?.ToLowerInvariant() switch
        {
            "system" => "system",
            "assistant" => "assistant",
            "developer" => "developer",
            _ => "user",
        };
    }

    private static void ValidateStrictResponseItemSchema(JsonElement item, string rawType)
    {
        switch (rawType)
        {
            case "message":
                RequireStringProperty(item, "role", rawType);
                RequireArrayProperty(item, "content", rawType);
                break;
            case "reasoning":
                RequireStringProperty(item, "id", rawType);
                RequireArrayProperty(item, "summary", rawType);
                break;
            case "local_shell_call":
                RequireObjectProperty(item, "action", rawType);
                RequireStringProperty(item, "status", rawType);
                break;
            case "function_call":
                RequireStringProperty(item, "arguments", rawType);
                RequireStringProperty(item, "call_id", rawType);
                RequireStringProperty(item, "name", rawType);
                break;
            case "tool_search_call":
                RequireProperty(item, "arguments", rawType);
                RequireStringProperty(item, "execution", rawType);
                break;
            case "function_call_output":
                RequireStringProperty(item, "call_id", rawType);
                RequireReplayableToolOutputProperty(item, rawType);
                break;
            case "custom_tool_call":
                RequireStringProperty(item, "call_id", rawType);
                RequireStringProperty(item, "input", rawType);
                RequireStringProperty(item, "name", rawType);
                break;
            case "custom_tool_call_output":
                RequireStringProperty(item, "call_id", rawType);
                RequireReplayableToolOutputProperty(item, rawType);
                break;
            case "tool_search_output":
                RequireStringProperty(item, "execution", rawType);
                RequireStringProperty(item, "status", rawType);
                RequireArrayProperty(item, "tools", rawType);
                break;
            case "image_generation_call":
                RequireStringProperty(item, "id", rawType);
                RequireStringProperty(item, "result", rawType);
                RequireStringProperty(item, "status", rawType);
                break;
            case "ghost_snapshot":
                RequireObjectProperty(item, "ghost_commit", rawType);
                break;
            case "compaction":
                RequireStringProperty(item, "encrypted_content", rawType);
                break;
        }
    }

    private static JsonElement RequireProperty(JsonElement item, string propertyName, string rawType)
    {
        if (!item.TryGetProperty(propertyName, out var value))
        {
            throw new JsonException($"history item type={rawType} 缺少必填字段 {propertyName}。");
        }

        return value;
    }

    private static void RequireReplayableToolOutputProperty(JsonElement item, string rawType)
    {
        var output = RequireProperty(item, "output", rawType);
        if (output.ValueKind is JsonValueKind.String or JsonValueKind.Array)
        {
            return;
        }

        if (output.ValueKind == JsonValueKind.Object
            && (TryReadLegacyToolOutputBody(output, "body", out _)
                || TryReadLegacyToolOutputBody(output, "content", out _)))
        {
            return;
        }

        throw new JsonException($"history item type={rawType} 的 output 必须是 string、array，或带 body/content 的 legacy wrapper。");
    }

    private static bool TryNormalizeToolOutputItem(JsonElement item, out JsonElement normalizedItem)
    {
        normalizedItem = default;
        if (!item.TryGetProperty("output", out var output))
        {
            return false;
        }

        if (output.ValueKind is JsonValueKind.String or JsonValueKind.Array)
        {
            normalizedItem = item.Clone();
            return true;
        }

        if (output.ValueKind != JsonValueKind.Object
            || (!TryReadLegacyToolOutputBody(output, "body", out var legacyBody)
                && !TryReadLegacyToolOutputBody(output, "content", out legacyBody)))
        {
            return false;
        }

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in item.EnumerateObject())
        {
            payload[property.Name] = string.Equals(property.Name, "output", StringComparison.Ordinal)
                ? legacyBody
                : JsonSerializer.Deserialize<object?>(property.Value.GetRawText());
        }

        normalizedItem = JsonSerializer.SerializeToElement(payload);
        return true;
    }

    private static bool TryReadLegacyToolOutputBody(JsonElement output, string propertyName, out object? body)
    {
        body = null;
        if (!output.TryGetProperty(propertyName, out var bodyElement)
            || bodyElement.ValueKind is not (JsonValueKind.String or JsonValueKind.Array))
        {
            return false;
        }

        body = JsonSerializer.Deserialize<object?>(bodyElement.GetRawText());
        return true;
    }

    private static void RequireStringProperty(JsonElement item, string propertyName, string rawType)
    {
        var value = RequireProperty(item, propertyName, rawType);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"history item type={rawType} 字段 {propertyName} 必须是字符串。");
        }
    }

    private static void RequireArrayProperty(JsonElement item, string propertyName, string rawType)
    {
        var value = RequireProperty(item, propertyName, rawType);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException($"history item type={rawType} 字段 {propertyName} 必须是数组。");
        }
    }

    private static void RequireObjectProperty(JsonElement item, string propertyName, string rawType)
    {
        var value = RequireProperty(item, propertyName, rawType);
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"history item type={rawType} 字段 {propertyName} 必须是对象。");
        }
    }

    private static void RequirePropertyKinds(
        JsonElement item,
        string propertyName,
        string rawType,
        params JsonValueKind[] allowedKinds)
    {
        var value = RequireProperty(item, propertyName, rawType);
        if (allowedKinds.Contains(value.ValueKind))
        {
            return;
        }

        var expectedKinds = string.Join("、", allowedKinds.Select(static kind => kind.ToString()));
        throw new JsonException($"history item type={rawType} 字段 {propertyName} 必须是以下类型之一：{expectedKinds}。");
    }

    private static string? ReadPreferredContent(JsonElement item, IReadOnlyList<KernelConversationInputRecord> inputs)
    {
        var directText = KernelRuntimeJsonHelpers.Normalize(KernelRuntimeJsonHelpers.ReadString(item, "text"));
        if (!string.IsNullOrWhiteSpace(directText))
        {
            return directText;
        }

        if (item.TryGetProperty("content", out var contentValue) && contentValue.ValueKind == JsonValueKind.String)
        {
            var directContent = KernelRuntimeJsonHelpers.Normalize(contentValue.GetString());
            if (!string.IsNullOrWhiteSpace(directContent))
            {
                return directContent;
            }
        }

        var preview = BuildInputPreview(inputs);
        if (!string.IsNullOrWhiteSpace(preview))
        {
            return preview;
        }

        if (TryGetArray(item, "content", out var contentItems))
        {
            return ExtractTextSegments(contentItems);
        }

        return null;
    }

    private static IReadOnlyList<KernelConversationInputRecord> ParseInputArray(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<KernelConversationInputRecord>();
        }

        var inputs = new List<KernelConversationInputRecord>();
        foreach (var item in array.EnumerateArray())
        {
            var parsed = ParseInput(item);
            if (parsed is not null)
            {
                inputs.Add(parsed);
            }
        }

        return inputs;
    }

    private static KernelConversationInputRecord? ParseInput(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.String)
        {
            var directText = KernelRuntimeJsonHelpers.Normalize(item.GetString());
            return string.IsNullOrWhiteSpace(directText)
                ? null
                : new KernelConversationInputRecord
                {
                    Type = "text",
                    Text = directText,
                };
        }

        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var normalizedType = KernelRuntimeJsonHelpers.Normalize(KernelRuntimeJsonHelpers.ReadString(item, "type"));
        if (string.IsNullOrWhiteSpace(normalizedType))
        {
            normalizedType = "text";
        }

        normalizedType = normalizedType!.ToLowerInvariant() switch
        {
            "input_text" or "output_text" => "text",
            "input_image" => "image",
            _ => normalizedType,
        };

        var text = KernelRuntimeJsonHelpers.Normalize(KernelRuntimeJsonHelpers.ReadString(item, "text"))
                   ?? KernelRuntimeJsonHelpers.Normalize(KernelRuntimeJsonHelpers.ReadString(item, "content"))
                   ?? KernelRuntimeJsonHelpers.Normalize(KernelRuntimeJsonHelpers.ReadString(item, "value"));
        var url = KernelRuntimeJsonHelpers.Normalize(KernelRuntimeJsonHelpers.ReadString(item, "url"))
                  ?? KernelRuntimeJsonHelpers.Normalize(KernelRuntimeJsonHelpers.ReadString(item, "image_url"))
                  ?? KernelRuntimeJsonHelpers.Normalize(KernelRuntimeJsonHelpers.ReadString(item, "imageUrl"));
        var path = KernelRuntimeJsonHelpers.Normalize(KernelRuntimeJsonHelpers.ReadString(item, "path"));
        var name = KernelRuntimeJsonHelpers.Normalize(KernelRuntimeJsonHelpers.ReadString(item, "name"));
        var textElements = ParseTextElements(item);

        if (string.IsNullOrWhiteSpace(text)
            && string.IsNullOrWhiteSpace(url)
            && string.IsNullOrWhiteSpace(path)
            && string.IsNullOrWhiteSpace(name)
            && textElements.Count == 0)
        {
            return null;
        }

        return new KernelConversationInputRecord
        {
            Type = normalizedType,
            Text = text,
            Url = url,
            Path = path,
            Name = name,
            TextElements = textElements,
        };
    }

    private static KernelConversationInputRecord? ParseInput(KernelTurnInputItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var normalizedType = KernelRuntimeJsonHelpers.Normalize(item.Type);
        if (string.IsNullOrWhiteSpace(normalizedType))
        {
            normalizedType = "text";
        }

        normalizedType = normalizedType!.ToLowerInvariant() switch
        {
            "input_text" or "output_text" => "text",
            "input_image" => "image",
            _ => normalizedType,
        };

        if (string.IsNullOrWhiteSpace(item.Text)
            && string.IsNullOrWhiteSpace(item.Url)
            && string.IsNullOrWhiteSpace(item.Path)
            && string.IsNullOrWhiteSpace(item.Name)
            && item.TextElements.Count == 0)
        {
            return null;
        }

        return new KernelConversationInputRecord
        {
            Type = normalizedType,
            Text = KernelRuntimeJsonHelpers.Normalize(item.Text),
            Url = KernelRuntimeJsonHelpers.Normalize(item.Url),
            Path = KernelRuntimeJsonHelpers.Normalize(item.Path),
            Name = KernelRuntimeJsonHelpers.Normalize(item.Name),
            TextElements = item.TextElements.ToArray(),
        };
    }

    private static IReadOnlyList<KernelConversationTextElementRecord> ParseTextElements(JsonElement item)
    {
        if (!TryGetArray(item, "text_elements", out var textElements)
            && !TryGetArray(item, "textElements", out textElements))
        {
            return Array.Empty<KernelConversationTextElementRecord>();
        }

        var parsed = new List<KernelConversationTextElementRecord>();
        foreach (var entry in textElements.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            JsonElement byteRange;
            var hasByteRange = entry.TryGetProperty("byte_range", out byteRange)
                               || entry.TryGetProperty("byteRange", out byteRange);
            KernelConversationByteRangeRecord? range = null;
            if (hasByteRange && byteRange.ValueKind == JsonValueKind.Object)
            {
                var start = KernelRuntimeJsonHelpers.ReadInt(byteRange, "start");
                var end = KernelRuntimeJsonHelpers.ReadInt(byteRange, "end");
                if (start.HasValue && end.HasValue)
                {
                    range = new KernelConversationByteRangeRecord(
                        Math.Max(0, start.Value),
                        Math.Max(0, end.Value));
                }
            }

            parsed.Add(new KernelConversationTextElementRecord(
                range,
                KernelRuntimeJsonHelpers.Normalize(KernelRuntimeJsonHelpers.ReadString(entry, "placeholder"))));
        }

        return parsed;
    }

    private static bool TryGetArray(JsonElement json, string propertyName, out JsonElement array)
    {
        array = default;
        return json.ValueKind == JsonValueKind.Object
               && json.TryGetProperty(propertyName, out array)
               && array.ValueKind == JsonValueKind.Array;
    }

    private static string? ExtractTextSegments(JsonElement contentItems)
    {
        if (contentItems.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var segments = new List<string>();
        foreach (var segment in contentItems.EnumerateArray())
        {
            if (segment.ValueKind == JsonValueKind.String)
            {
                var direct = KernelRuntimeJsonHelpers.Normalize(segment.GetString());
                if (!string.IsNullOrWhiteSpace(direct))
                {
                    segments.Add(direct!);
                }

                continue;
            }

            if (segment.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var text = KernelRuntimeJsonHelpers.Normalize(KernelRuntimeJsonHelpers.ReadString(segment, "text"))
                       ?? KernelRuntimeJsonHelpers.Normalize(KernelRuntimeJsonHelpers.ReadString(segment, "content"))
                       ?? KernelRuntimeJsonHelpers.Normalize(KernelRuntimeJsonHelpers.ReadString(segment, "value"));
            if (!string.IsNullOrWhiteSpace(text))
            {
                segments.Add(text!);
            }
        }

        return segments.Count == 0 ? null : string.Join(Environment.NewLine, segments);
    }

    private static object SerializeInput(KernelConversationInputRecord input)
    {
        var normalizedType = KernelRuntimeJsonHelpers.Normalize(input.Type) ?? "text";
        return normalizedType.ToLowerInvariant() switch
        {
            "text" => SerializeTextInput(input),
            "image" => new Dictionary<string, object?>
            {
                ["type"] = "image",
                ["url"] = input.Url ?? string.Empty,
            },
            "local_image" => new Dictionary<string, object?>
            {
                ["type"] = "local_image",
                ["path"] = input.Path ?? string.Empty,
            },
            "skill" => new Dictionary<string, object?>
            {
                ["type"] = "skill",
                ["name"] = input.Name ?? string.Empty,
                ["path"] = input.Path ?? string.Empty,
            },
            "mention" => new Dictionary<string, object?>
            {
                ["type"] = "mention",
                ["name"] = input.Name ?? string.Empty,
                ["path"] = input.Path ?? string.Empty,
            },
            _ => new Dictionary<string, object?>
            {
                ["type"] = normalizedType,
                ["text"] = input.Text,
                ["url"] = input.Url,
                ["path"] = input.Path,
                ["name"] = input.Name,
            },
        };
    }

    private static object SerializeTextInput(KernelConversationInputRecord input)
    {
        var payload = new Dictionary<string, object?>
        {
            ["type"] = "text",
            ["text"] = input.Text ?? string.Empty,
        };

        if (input.TextElements.Count > 0)
        {
            payload["text_elements"] = input.TextElements.Select(static element => new Dictionary<string, object?>
            {
                ["byte_range"] = element.ByteRange is null
                    ? null
                    : new Dictionary<string, object?>
                    {
                        ["start"] = element.ByteRange.Start,
                        ["end"] = element.ByteRange.End,
                    },
                ["placeholder"] = element.Placeholder,
            }).ToArray();
        }

        return payload;
    }

    private static IEnumerable<object> BuildProviderContentItems(
        KernelConversationInputRecord input,
        KernelPromptContentFormat format)
    {
        var normalizedType = KernelRuntimeJsonHelpers.Normalize(input.Type) ?? "text";
        switch (normalizedType.ToLowerInvariant())
        {
            case "text":
                {
                    var text = KernelRuntimeJsonHelpers.Normalize(input.Text);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        yield break;
                    }

                    if (format == KernelPromptContentFormat.Responses)
                    {
                        var payload = new Dictionary<string, object?>
                        {
                            ["type"] = "input_text",
                            ["text"] = text!,
                        };

                        if (input.TextElements.Count > 0)
                        {
                            payload["text_elements"] = input.TextElements.Select(static element => new Dictionary<string, object?>
                            {
                                ["byte_range"] = element.ByteRange is null
                                    ? null
                                    : new Dictionary<string, object?>
                                    {
                                        ["start"] = element.ByteRange.Start,
                                        ["end"] = element.ByteRange.End,
                                    },
                                ["placeholder"] = element.Placeholder,
                            }).ToArray();
                        }

                        yield return payload;
                    }
                    else
                    {
                        yield return new Dictionary<string, object?>
                        {
                            ["type"] = "text",
                            ["text"] = text!,
                        };
                    }

                    yield break;
                }

            case "image":
                {
                    var url = KernelRuntimeJsonHelpers.Normalize(input.Url);
                    if (string.IsNullOrWhiteSpace(url))
                    {
                        yield break;
                    }

                    if (format == KernelPromptContentFormat.Responses)
                    {
                        yield return new Dictionary<string, object?>
                        {
                            ["type"] = "input_image",
                            ["image_url"] = url!,
                        };
                    }
                    else
                    {
                        yield return new Dictionary<string, object?>
                        {
                            ["type"] = "image_url",
                            ["image_url"] = new Dictionary<string, object?>
                            {
                                ["url"] = url!,
                            },
                        };
                    }

                    yield break;
                }

            default:
                {
                    var preview = BuildInputPreview(input);
                    if (string.IsNullOrWhiteSpace(preview))
                    {
                        yield break;
                    }

                    yield return new Dictionary<string, object?>
                    {
                        ["type"] = format == KernelPromptContentFormat.Responses ? "input_text" : "text",
                        ["text"] = preview!,
                    };
                    yield break;
                }
        }
    }

    private static string? BuildInputPreview(KernelConversationInputRecord input)
    {
        var kind = KernelRuntimeJsonHelpers.Normalize(input.Type) ?? "text";
        return kind.ToLowerInvariant() switch
        {
            "text" => KernelRuntimeJsonHelpers.Normalize(input.Text),
            "image" => "[image]",
            "local_image" => $"[local_image:{KernelRuntimeJsonHelpers.Normalize(input.Path) ?? string.Empty}]",
            "skill" => BuildNamedInputPreview("skill", input.Name, input.Path),
            "mention" => BuildNamedInputPreview("mention", input.Name, input.Path),
            _ => KernelRuntimeJsonHelpers.Normalize(input.Text) ?? $"[{kind}]",
        };
    }

    private static string BuildNamedInputPreview(string kind, string? name, string? path)
    {
        var normalizedName = KernelRuntimeJsonHelpers.Normalize(name) ?? string.Empty;
        var normalizedPath = KernelRuntimeJsonHelpers.Normalize(path);
        return string.IsNullOrWhiteSpace(normalizedPath)
            ? $"[{kind}:${normalizedName}]"
            : $"[{kind}:${normalizedName}]({normalizedPath})";
    }
}
