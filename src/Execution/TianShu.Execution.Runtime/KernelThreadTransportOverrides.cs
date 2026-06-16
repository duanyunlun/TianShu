using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TianShu.Execution.Runtime;

[JsonConverter(typeof(KernelSandboxPolicyOverrideJsonConverter))]
internal sealed class KernelSandboxPolicyOverride
{
    private readonly JsonElement policy;

    private KernelSandboxPolicyOverride(JsonElement policy)
    {
        this.policy = policy.Clone();
        Type = KernelTransportJsonHelpers.ReadLooseString(policy, "type");
    }

    public string? Type { get; }

    public JsonElement ToJsonElement()
        => policy.Clone();

    public static KernelSandboxPolicyOverride FromElement(JsonElement policy)
        => new(policy);

    public static KernelSandboxPolicyOverride FromMode(string mode)
        => new(JsonSerializer.SerializeToElement(new
        {
            type = mode,
        }));
}

[JsonConverter(typeof(KernelConversationHistoryOverrideJsonConverter))]
internal sealed class KernelConversationHistoryOverride
{
    public KernelConversationHistoryOverride(bool shouldOverride, IReadOnlyList<KernelConversationHistoryItem> items)
    {
        ShouldOverride = shouldOverride;
        Items = items;
    }

    public bool ShouldOverride { get; }

    public IReadOnlyList<KernelConversationHistoryItem> Items { get; }
}

[JsonConverter(typeof(KernelCollaborationModeOverrideJsonConverter))]
internal sealed class KernelCollaborationModeOverride
{
    public string? Mode { get; init; }

    public string? Model { get; init; }

    public string? ReasoningEffort { get; init; }

    public KernelOptional<string?> DeveloperInstructions { get; init; }
}

[JsonConverter(typeof(KernelPersonalityJsonConverter))]
internal sealed class KernelPersonality : IEquatable<KernelPersonality>
{
    private KernelPersonality(string value)
    {
        Value = value;
    }

    public static KernelPersonality None { get; } = new("none");

    public static KernelPersonality Friendly { get; } = new("friendly");

    public static KernelPersonality Pragmatic { get; } = new("pragmatic");

    public string Value { get; }

    public static KernelPersonality Parse(string value)
        => TryParse(value, out var personality)
            ? personality
            : throw new FormatException($"不支持的 personality：{value}");

    public static bool TryParse(string? value, [NotNullWhen(true)] out KernelPersonality? personality)
    {
        switch (KernelRuntimeJsonHelpers.Normalize(value)?.ToLowerInvariant())
        {
            case "none":
                personality = None;
                return true;
            case "friendly":
                personality = Friendly;
                return true;
            case "pragmatic":
                personality = Pragmatic;
                return true;
            default:
                personality = null;
                return false;
        }
    }

    public bool Equals(KernelPersonality? other)
        => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj)
        => obj is KernelPersonality other && Equals(other);

    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString()
        => Value;

    public static implicit operator string?(KernelPersonality? value)
        => value?.Value;
}

[JsonConverter(typeof(KernelJsonSchemaPayloadJsonConverter))]
internal sealed class KernelJsonSchemaPayload
{
    private readonly JsonElement schema;

    private KernelJsonSchemaPayload(JsonElement schema)
    {
        this.schema = schema.Clone();
    }

    public JsonElement ToJsonElement()
        => schema.Clone();

    public static KernelJsonSchemaPayload FromElement(JsonElement schema)
        => new(schema);
}

[JsonConverter(typeof(KernelConfigOverridePayloadJsonConverter))]
internal sealed class KernelConfigOverridePayload
{
    private readonly JsonElement config;

    private KernelConfigOverridePayload(JsonElement config)
    {
        this.config = config.Clone();
    }

    public JsonElement ToJsonElement()
        => config.Clone();

    public static KernelConfigOverridePayload FromElement(JsonElement config)
        => new(config);
}

internal sealed class KernelTurnInputItem
{
    public string Type { get; init; } = "text";

    public string? Text { get; init; }

    public string? Url { get; init; }

    public string? Path { get; init; }

    public string? CanonicalPath { get; init; }

    public string? Name { get; init; }

    public IReadOnlyList<KernelConversationTextElementRecord> TextElements { get; init; }
        = Array.Empty<KernelConversationTextElementRecord>();

    public IReadOnlyList<KernelTurnInputItem> ContentItems { get; init; }
        = Array.Empty<KernelTurnInputItem>();
}

internal static class KernelThreadTransportParsers
{
    public static IReadOnlyList<KernelTurnInputItem>? ParseTurnInputItems(JsonElement? input)
    {
        if (input is not { ValueKind: JsonValueKind.Array } array)
        {
            return null;
        }

        var items = new List<KernelTurnInputItem>();
        foreach (var item in array.EnumerateArray())
        {
            var parsed = ParseTurnInputItem(item);
            if (parsed is not null)
            {
                items.Add(parsed);
            }
        }

        return items;
    }

    public static KernelTurnInputItem? ParseTurnInputItem(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.String)
        {
            var directText = KernelRuntimeJsonHelpers.Normalize(item.GetString());
            return string.IsNullOrWhiteSpace(directText)
                ? null
                : new KernelTurnInputItem
                {
                    Type = "text",
                    Text = directText,
                };
        }

        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var contentItems = ParseNestedContentItems(item);
        var normalizedType = KernelTransportJsonHelpers.ReadLooseString(item, "type");
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

        var text = ReadTurnInputText(item);
        var url = KernelTransportJsonHelpers.ReadLooseString(item, "url", "image_url", "imageUrl");
        var path = KernelTransportJsonHelpers.ReadLooseString(item, "path");
        var canonicalPath = KernelTransportJsonHelpers.ReadLooseString(item, "pathToSkillsMd", "targetPath");
        var name = KernelTransportJsonHelpers.ReadLooseString(item, "name");
        var textElements = ParseTextElements(item);

        if (string.IsNullOrWhiteSpace(text)
            && string.IsNullOrWhiteSpace(url)
            && string.IsNullOrWhiteSpace(path)
            && string.IsNullOrWhiteSpace(canonicalPath)
            && string.IsNullOrWhiteSpace(name)
            && textElements.Count == 0
            && contentItems.Count == 0)
        {
            return null;
        }

        return new KernelTurnInputItem
        {
            Type = normalizedType,
            Text = text,
            Url = url,
            Path = path,
            CanonicalPath = canonicalPath,
            Name = name,
            TextElements = textElements,
            ContentItems = contentItems,
        };
    }

    public static IReadOnlyList<KernelConversationHistoryItem> ParseHistoryItems(JsonElement? history)
    {
        if (history is not { ValueKind: JsonValueKind.Array } array)
        {
            return Array.Empty<KernelConversationHistoryItem>();
        }

        var messages = new List<KernelConversationHistoryItem>();
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            KernelConversationHistoryItem? parsed;
            try
            {
                parsed = KernelConversationHistoryUtilities.ParseHistoryItem(item, strictResponseItem: true);
            }
            catch (JsonException ex)
            {
                throw new JsonException($"history[{index}] {ex.Message}", ex);
            }

            if (parsed is null)
            {
                throw new JsonException($"history[{index}] 无法解析为 ResponseItem。");
            }

            messages.Add(parsed);
            index++;
        }

        return messages;
    }

    public static object SerializeTurnInputItem(KernelTurnInputItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var payload = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(item.Type))
        {
            payload["type"] = item.Type;
        }

        if (!string.IsNullOrWhiteSpace(item.Text))
        {
            payload["text"] = item.Text;
        }

        if (!string.IsNullOrWhiteSpace(item.Url))
        {
            payload["url"] = item.Url;
        }

        if (!string.IsNullOrWhiteSpace(item.Path))
        {
            payload["path"] = item.Path;
        }

        if (!string.IsNullOrWhiteSpace(item.CanonicalPath))
        {
            payload["targetPath"] = item.CanonicalPath;
        }

        if (!string.IsNullOrWhiteSpace(item.Name))
        {
            payload["name"] = item.Name;
        }

        if (item.TextElements.Count > 0)
        {
            payload["textElements"] = item.TextElements.Select(static element => new Dictionary<string, object?>
            {
                ["byteRange"] = element.ByteRange is null
                    ? null
                    : new Dictionary<string, object?>
                    {
                        ["start"] = element.ByteRange.Start,
                        ["end"] = element.ByteRange.End,
                    },
                ["placeholder"] = element.Placeholder,
            }).ToArray();
        }

        if (item.ContentItems.Count > 0)
        {
            payload["content"] = item.ContentItems.Select(SerializeTurnInputItem).ToArray();
        }

        return payload;
    }

    private static IReadOnlyList<KernelTurnInputItem> ParseNestedContentItems(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<KernelTurnInputItem>();
        }

        var items = new List<KernelTurnInputItem>();
        foreach (var entry in content.EnumerateArray())
        {
            var parsed = ParseTurnInputItem(entry);
            if (parsed is not null)
            {
                items.Add(parsed);
            }
        }

        return items;
    }

    private static string? ReadTurnInputText(JsonElement item)
    {
        var directText = KernelTransportJsonHelpers.ReadLooseString(item, "text");
        if (!string.IsNullOrWhiteSpace(directText))
        {
            return directText;
        }

        if (item.ValueKind == JsonValueKind.Object
            && item.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.String)
        {
            return KernelRuntimeJsonHelpers.Normalize(content.GetString());
        }

        return KernelTransportJsonHelpers.ReadLooseString(item, "value");
    }

    private static IReadOnlyList<KernelConversationTextElementRecord> ParseTextElements(JsonElement item)
    {
        JsonElement textElements;
        if (!KernelTransportJsonHelpers.TryReadProperty(item, out textElements, "text_elements", "textElements")
            || textElements.ValueKind != JsonValueKind.Array)
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
            var hasByteRange = KernelTransportJsonHelpers.TryReadProperty(entry, out byteRange, "byte_range", "byteRange")
                               && byteRange.ValueKind == JsonValueKind.Object;
            KernelConversationByteRangeRecord? range = null;
            if (hasByteRange)
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
                KernelTransportJsonHelpers.ReadLooseString(entry, "placeholder")));
        }

        return parsed;
    }
}

internal static class KernelTransportJsonHelpers
{
    public static string? ReadLooseString(JsonElement json, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryReadProperty(json, out var property, propertyName))
            {
                continue;
            }

            switch (property.ValueKind)
            {
                case JsonValueKind.String:
                    return KernelRuntimeJsonHelpers.Normalize(property.GetString());
                case JsonValueKind.Number:
                    return KernelRuntimeJsonHelpers.Normalize(property.GetRawText());
                case JsonValueKind.True:
                    return bool.TrueString;
                case JsonValueKind.False:
                    return bool.FalseString;
                case JsonValueKind.Null:
                    return null;
            }
        }

        return null;
    }

    public static bool HasProperty(JsonElement json, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty(propertyName, out _))
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryReadProperty(JsonElement json, out JsonElement property, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty(propertyName, out property))
            {
                property = property.Clone();
                return true;
            }
        }

        property = default;
        return false;
    }
}

internal sealed class KernelDynamicToolListJsonConverter : JsonConverter<IReadOnlyList<KernelDynamicToolDescriptor>?>
{
    public override IReadOnlyList<KernelDynamicToolDescriptor>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return KernelDynamicToolResolver.Parse(document.RootElement.Clone());
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<KernelDynamicToolDescriptor>? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (var descriptor in value)
        {
            writer.WriteStartObject();
            writer.WriteString("name", descriptor.FullName);
            writer.WriteString("tool_name", descriptor.ShortName);
            if (!string.IsNullOrWhiteSpace(descriptor.Namespace))
            {
                writer.WriteString("namespace", descriptor.Namespace);
            }

            if (!string.IsNullOrWhiteSpace(descriptor.Description))
            {
                writer.WriteString("description", descriptor.Description);
            }

            if (!string.IsNullOrWhiteSpace(descriptor.Title))
            {
                writer.WriteString("title", descriptor.Title);
            }

            if (!string.IsNullOrWhiteSpace(descriptor.Server))
            {
                writer.WriteString("server", descriptor.Server);
            }

            if (!string.IsNullOrWhiteSpace(descriptor.ConnectorName))
            {
                writer.WriteString("connectorName", descriptor.ConnectorName);
            }

            if (!string.IsNullOrWhiteSpace(descriptor.ConnectorDescription))
            {
                writer.WriteString("connectorDescription", descriptor.ConnectorDescription);
            }

            if (descriptor.InputSchema is { } inputSchema)
            {
                writer.WritePropertyName("inputSchema");
                inputSchema.WriteTo(writer);
            }

            if (descriptor.OutputSchema is { } outputSchema)
            {
                writer.WritePropertyName("outputSchema");
                outputSchema.WriteTo(writer);
            }

            if (descriptor.Meta is { } meta)
            {
                writer.WritePropertyName("_meta");
                meta.WriteTo(writer);
            }

            if (descriptor.Annotations is { } annotations)
            {
                writer.WritePropertyName("annotations");
                annotations.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}

internal sealed class KernelTurnInputListJsonConverter : JsonConverter<IReadOnlyList<KernelTurnInputItem>?>
{
    public override IReadOnlyList<KernelTurnInputItem>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return KernelThreadTransportParsers.ParseTurnInputItems(document.RootElement.Clone());
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<KernelTurnInputItem>? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        JsonSerializer.Serialize(
            writer,
            value.Select(KernelThreadTransportParsers.SerializeTurnInputItem).ToArray(),
            options);
    }
}

internal sealed class KernelConversationHistoryOverrideJsonConverter : JsonConverter<KernelConversationHistoryOverride?>
{
    public override bool HandleNull => true;

    public override KernelConversationHistoryOverride? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement.Clone();
        if (root.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("history 必须是 ResponseItem 数组。");
        }

        return new KernelConversationHistoryOverride(
            shouldOverride: true,
            items: KernelThreadTransportParsers.ParseHistoryItems(root));
    }

    public override void Write(Utf8JsonWriter writer, KernelConversationHistoryOverride? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        JsonSerializer.Serialize(
            writer,
            value.Items.Select(KernelConversationHistoryUtilities.SerializeHistoryItem).ToArray(),
            options);
    }
}

internal sealed class KernelPersonalityJsonConverter : JsonConverter<KernelPersonality?>
{
    public override KernelPersonality? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("personality 必须是字符串。");
        }

        try
        {
            return KernelPersonality.Parse(reader.GetString() ?? string.Empty);
        }
        catch (FormatException ex)
        {
            throw new JsonException(ex.Message, ex);
        }
    }

    public override void Write(Utf8JsonWriter writer, KernelPersonality? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Value);
    }
}

internal sealed class KernelSandboxPolicyOverrideJsonConverter : JsonConverter<KernelSandboxPolicyOverride?>
{
    public override KernelSandboxPolicyOverride? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement.Clone();
        return root.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => KernelSandboxPolicyOverride.FromMode(
                KernelRuntimeJsonHelpers.Normalize(root.GetString()) ?? "workspaceWrite"),
            JsonValueKind.Object => KernelSandboxPolicyOverride.FromElement(root),
            _ => null,
        };
    }

    public override void Write(Utf8JsonWriter writer, KernelSandboxPolicyOverride? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        value.ToJsonElement().WriteTo(writer);
    }
}

internal sealed class KernelCollaborationModeOverrideJsonConverter : JsonConverter<KernelCollaborationModeOverride?>
{
    public override KernelCollaborationModeOverride? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement.Clone();
        if (root.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (root.ValueKind == JsonValueKind.String)
        {
            return new KernelCollaborationModeOverride
            {
                Mode = KernelRuntimeJsonHelpers.Normalize(root.GetString()),
            };
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        JsonElement settings;
        var hasSettings = KernelTransportJsonHelpers.TryReadProperty(root, out settings, "settings")
                          && settings.ValueKind == JsonValueKind.Object;

        var developerInstructions = hasSettings
            ? KernelTransportJsonHelpers.ReadLooseString(settings, "developer_instructions", "developerInstructions")
            : null;

        return new KernelCollaborationModeOverride
        {
            Mode = KernelTransportJsonHelpers.ReadLooseString(root, "mode"),
            Model = hasSettings
                ? KernelTransportJsonHelpers.ReadLooseString(settings, "model")
                : null,
            ReasoningEffort = hasSettings
                ? KernelTransportJsonHelpers.ReadLooseString(settings, "reasoning_effort", "reasoningEffort")
                : null,
            DeveloperInstructions = new KernelOptional<string?>(
                hasSettings && KernelTransportJsonHelpers.HasProperty(settings, "developer_instructions", "developerInstructions"),
                developerInstructions),
        };
    }

    public override void Write(Utf8JsonWriter writer, KernelCollaborationModeOverride? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        if (!value.DeveloperInstructions.IsSpecified
            && string.IsNullOrWhiteSpace(value.Model)
            && string.IsNullOrWhiteSpace(value.ReasoningEffort))
        {
            writer.WriteStringValue(value.Mode);
            return;
        }

        writer.WriteStartObject();
        if (!string.IsNullOrWhiteSpace(value.Mode))
        {
            writer.WriteString("mode", value.Mode);
        }

        writer.WritePropertyName("settings");
        writer.WriteStartObject();
        if (!string.IsNullOrWhiteSpace(value.Model))
        {
            writer.WriteString("model", value.Model);
        }

        if (!string.IsNullOrWhiteSpace(value.ReasoningEffort))
        {
            writer.WriteString("reasoning_effort", value.ReasoningEffort);
        }

        if (value.DeveloperInstructions.IsSpecified)
        {
            if (value.DeveloperInstructions.Value is null)
            {
                writer.WriteNull("developer_instructions");
            }
            else
            {
                writer.WriteString("developer_instructions", value.DeveloperInstructions.Value);
            }
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }
}

internal sealed class KernelJsonSchemaPayloadJsonConverter : JsonConverter<KernelJsonSchemaPayload?>
{
    public override KernelJsonSchemaPayload? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement.Clone();
        return root.ValueKind == JsonValueKind.Null
            ? null
            : KernelJsonSchemaPayload.FromElement(root);
    }

    public override void Write(Utf8JsonWriter writer, KernelJsonSchemaPayload? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        value.ToJsonElement().WriteTo(writer);
    }
}

internal sealed class KernelConfigOverridePayloadJsonConverter : JsonConverter<KernelConfigOverridePayload?>
{
    public override KernelConfigOverridePayload? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement.Clone();
        return root.ValueKind == JsonValueKind.Null
            ? null
            : KernelConfigOverridePayload.FromElement(root);
    }

    public override void Write(Utf8JsonWriter writer, KernelConfigOverridePayload? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        value.ToJsonElement().WriteTo(writer);
    }
}
