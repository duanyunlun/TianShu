using System.Text.Json;
using System.Text.Json.Serialization;
using TianShu.Provider.Abstractions;

namespace TianShu.Provider.OpenAI;

internal static class OpenAiJsonHelpers
{
    internal static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public static T? Deserialize<T>(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(element.GetRawText(), SerializerOptions);
    }
}

internal sealed class OpenAiAppServerResponseItemDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("phase")]
    public string? Phase { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("toolName")]
    public string? ToolName { get; init; }

    [JsonPropertyName("callId")]
    public string? CallId { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("outputText")]
    public string? OutputText { get; init; }

    [JsonPropertyName("delta")]
    public string? Delta { get; init; }

    [JsonPropertyName("output")]
    public string? Output { get; init; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; init; }

    [JsonPropertyName("input")]
    public string? Input { get; init; }
}

internal sealed class OpenAiCommandExecutionRequestApprovalParamsDto
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public string? TurnId { get; init; }

    [JsonPropertyName("approvalId")]
    public string? ApprovalId { get; init; }

    [JsonPropertyName("itemId")]
    public string? ItemId { get; init; }

    [JsonPropertyName("command")]
    public string? Command { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("skillMetadata")]
    public JsonElement SkillMetadata { get; init; }

    [JsonPropertyName("availableDecisions")]
    public JsonElement AvailableDecisions { get; init; }

    [JsonPropertyName("available_decisions")]
    public JsonElement LegacyAvailableDecisions { get; init; }

    [JsonPropertyName("proposedExecpolicyAmendment")]
    public JsonElement ProposedExecpolicyAmendment { get; init; }

    [JsonPropertyName("proposedNetworkPolicyAmendments")]
    public IReadOnlyList<OpenAiAppServerNetworkPolicyAmendmentDto>? ProposedNetworkPolicyAmendments { get; init; }

    [JsonIgnore]
    public JsonElement? SkillMetadataObject => OpenAiServerRequestDtoHelpers.CloneObjectOrNull(SkillMetadata);

    [JsonIgnore]
    public IReadOnlyList<ApprovalDecisionOptionPayload>? ResolvedAvailableDecisionOptions
        => OpenAiServerRequestDtoHelpers.ResolveAvailableDecisionOptions(AvailableDecisions, LegacyAvailableDecisions);

    [JsonIgnore]
    public IReadOnlyList<string>? ResolvedAvailableDecisions
        => OpenAiServerRequestDtoHelpers.ResolveAvailableDecisions(ResolvedAvailableDecisionOptions);

    [JsonIgnore]
    public ExecPolicyAmendmentPayload? ResolvedProposedExecPolicyAmendment
        => OpenAiServerRequestDtoHelpers.ResolveExecPolicyAmendment(ProposedExecpolicyAmendment);

    [JsonIgnore]
    public IReadOnlyList<NetworkPolicyAmendmentPayload>? ResolvedProposedNetworkPolicyAmendments
        => ProposedNetworkPolicyAmendments?
            .Select(static amendment => amendment.ToPayload())
            .Where(static amendment => amendment is not null)
            .Cast<NetworkPolicyAmendmentPayload>()
            .ToArray();
}

internal sealed class OpenAiLegacyExecCommandApprovalParamsDto
{
    [JsonPropertyName("conversationId")]
    public string? ConversationId { get; init; }

    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public string? TurnId { get; init; }

    [JsonPropertyName("callId")]
    public string? CallId { get; init; }

    [JsonPropertyName("approvalId")]
    public string? ApprovalId { get; init; }

    [JsonPropertyName("command")]
    public JsonElement Command { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonIgnore]
    public string? ResolvedThreadId => ThreadId ?? ConversationId;

    [JsonIgnore]
    public string? ResolvedCommandText
        => Command.ValueKind switch
        {
            JsonValueKind.String => OpenAiServerRequestDtoHelpers.Normalize(Command.GetString()),
            JsonValueKind.Array => string.Join(
                " ",
                Command.EnumerateArray()
                    .Select(static item => OpenAiServerRequestDtoHelpers.Normalize(item.GetString()))
                    .Where(static item => !string.IsNullOrWhiteSpace(item))),
            _ => null,
        };
}

internal sealed class OpenAiFileChangeRequestApprovalParamsDto
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public string? TurnId { get; init; }

    [JsonPropertyName("itemId")]
    public string? ItemId { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("availableDecisions")]
    public IReadOnlyList<string>? AvailableDecisions { get; init; }

    [JsonPropertyName("available_decisions")]
    public IReadOnlyList<string>? LegacyAvailableDecisions { get; init; }

    [JsonIgnore]
    public IReadOnlyList<string>? ResolvedAvailableDecisions => OpenAiServerRequestDtoHelpers.ResolveAvailableDecisions(AvailableDecisions, LegacyAvailableDecisions);
}

internal sealed class OpenAiLegacyApplyPatchApprovalParamsDto
{
    [JsonPropertyName("conversationId")]
    public string? ConversationId { get; init; }

    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public string? TurnId { get; init; }

    [JsonPropertyName("callId")]
    public string? CallId { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("grantRoot")]
    public string? GrantRoot { get; init; }

    [JsonIgnore]
    public string? ResolvedThreadId => ThreadId ?? ConversationId;
}

internal sealed class OpenAiToolRequestApprovalParamsDto
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public string? TurnId { get; init; }

    [JsonPropertyName("itemId")]
    public string? ItemId { get; init; }

    [JsonPropertyName("callId")]
    public string? CallId { get; init; }

    [JsonPropertyName("toolName")]
    public string? ToolName { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("serverName")]
    public string? ServerName { get; init; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; init; }

    [JsonPropertyName("input")]
    public string? Input { get; init; }

    [JsonPropertyName("approvalKind")]
    public string? ApprovalKind { get; init; }

    [JsonPropertyName("approval_kind")]
    public string? LegacyApprovalKind { get; init; }

    [JsonPropertyName("availableDecisions")]
    public IReadOnlyList<string>? AvailableDecisions { get; init; }

    [JsonPropertyName("available_decisions")]
    public IReadOnlyList<string>? LegacyAvailableDecisions { get; init; }

    [JsonPropertyName("_meta")]
    public JsonElement Meta { get; init; }

    [JsonPropertyName("meta")]
    public JsonElement LegacyMeta { get; init; }

    [JsonPropertyName("item")]
    public OpenAiAppServerResponseItemDto? Item { get; init; }

    [JsonIgnore]
    public string? ResolvedApprovalKind => ApprovalKind ?? LegacyApprovalKind;

    [JsonIgnore]
    public IReadOnlyList<string>? ResolvedAvailableDecisions => OpenAiServerRequestDtoHelpers.ResolveAvailableDecisions(AvailableDecisions, LegacyAvailableDecisions);

    [JsonIgnore]
    public JsonElement? ResolvedMeta => OpenAiServerRequestDtoHelpers.CloneObjectOrNull(Meta) ?? OpenAiServerRequestDtoHelpers.CloneObjectOrNull(LegacyMeta);
}

internal sealed class OpenAiMcpServerElicitationRequestParamsDto
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public string? TurnId { get; init; }

    [JsonPropertyName("serverName")]
    public string? ServerName { get; init; }

    [JsonPropertyName("elicitationId")]
    public string? ElicitationId { get; init; }

    [JsonPropertyName("mode")]
    public string? Mode { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("requestedSchema")]
    public JsonElement RequestedSchema { get; init; }

    [JsonPropertyName("requested_schema")]
    public JsonElement LegacyRequestedSchema { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("_meta")]
    public JsonElement Meta { get; init; }

    [JsonPropertyName("meta")]
    public JsonElement LegacyMeta { get; init; }

    [JsonIgnore]
    public JsonElement? ResolvedMeta => OpenAiServerRequestDtoHelpers.CloneObjectOrNull(Meta) ?? OpenAiServerRequestDtoHelpers.CloneObjectOrNull(LegacyMeta);

    [JsonIgnore]
    public JsonElement? ResolvedRequestedSchema
        => OpenAiServerRequestDtoHelpers.CloneValueOrNull(RequestedSchema) ?? OpenAiServerRequestDtoHelpers.CloneValueOrNull(LegacyRequestedSchema);

    [JsonIgnore]
    public string? ResolvedApprovalKind => ResolvedMeta is { } meta ? OpenAiServerRequestDtoHelpers.ReadString(meta, "codex_approval_kind") : null;

    [JsonIgnore]
    public IReadOnlyList<string>? ResolvedAvailableDecisions => ResolvedMeta is { } meta
        ? OpenAiServerRequestDtoHelpers.ReadStringArray(meta, "available_decisions", "availableDecisions")
        : null;

    [JsonIgnore]
    public string? ResolvedMetaToolName => ResolvedMeta is { } meta ? OpenAiServerRequestDtoHelpers.ReadString(meta, "tool_name") : null;

    [JsonIgnore]
    public string? ResolvedInstallUrl => ResolvedMeta is { } meta ? OpenAiServerRequestDtoHelpers.ReadString(meta, "install_url") : null;

    [JsonIgnore]
    public string? ResolvedUrl => OpenAiServerRequestDtoHelpers.Normalize(Url);
}

internal sealed class OpenAiPermissionRequestParamsDto
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public string? TurnId { get; init; }

    [JsonPropertyName("itemId")]
    public string? ItemId { get; init; }

    [JsonPropertyName("callId")]
    public string? CallId { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("permissions")]
    public JsonElement Permissions { get; init; }
}

internal sealed class OpenAiAppServerUserInputOptionDto
{
    [JsonPropertyName("label")]
    public string? Label { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

internal sealed class OpenAiAppServerNetworkPolicyAmendmentDto
{
    [JsonPropertyName("host")]
    public string? Host { get; init; }

    [JsonPropertyName("action")]
    public string? Action { get; init; }

    public NetworkPolicyAmendmentPayload? ToPayload()
    {
        var host = OpenAiServerRequestDtoHelpers.Normalize(Host);
        var action = OpenAiServerRequestDtoHelpers.Normalize(Action);
        return string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(action)
            ? null
            : new NetworkPolicyAmendmentPayload(host!, action!);
    }
}

internal sealed class OpenAiAppServerUserInputQuestionDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("header")]
    public string? Header { get; init; }

    [JsonPropertyName("question")]
    public string? Question { get; init; }

    [JsonPropertyName("prompt")]
    public string? Prompt { get; init; }

    [JsonPropertyName("isOther")]
    public bool? IsOther { get; init; }

    [JsonPropertyName("isSecret")]
    public bool? IsSecret { get; init; }

    [JsonPropertyName("options")]
    public IReadOnlyList<OpenAiAppServerUserInputOptionDto>? Options { get; init; }
}

internal sealed class OpenAiToolRequestUserInputParamsDto
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public string? TurnId { get; init; }

    [JsonPropertyName("itemId")]
    public string? ItemId { get; init; }

    [JsonPropertyName("questions")]
    public IReadOnlyList<OpenAiAppServerUserInputQuestionDto>? Questions { get; init; }
}

internal sealed class OpenAiDynamicToolCallRequestParamsDto
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public string? TurnId { get; init; }

    [JsonPropertyName("callId")]
    public string? CallId { get; init; }

    [JsonPropertyName("tool")]
    public string? Tool { get; init; }

    [JsonPropertyName("arguments")]
    public JsonElement Arguments { get; init; }
}

internal static class OpenAiServerRequestDtoHelpers
{
    public static IReadOnlyList<string>? ResolveAvailableDecisions(IReadOnlyList<string>? availableDecisions, IReadOnlyList<string>? legacyAvailableDecisions)
        => availableDecisions is { Count: > 0 }
            ? availableDecisions
            : legacyAvailableDecisions is { Count: > 0 }
                ? legacyAvailableDecisions
                : null;

    public static IReadOnlyList<ApprovalDecisionOptionPayload>? ResolveAvailableDecisionOptions(
        JsonElement availableDecisions,
        JsonElement legacyAvailableDecisions)
    {
        var source = availableDecisions.ValueKind == JsonValueKind.Array
            ? availableDecisions
            : legacyAvailableDecisions.ValueKind == JsonValueKind.Array
                ? legacyAvailableDecisions
                : default;
        if (source.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var options = new List<ApprovalDecisionOptionPayload>();
        foreach (var item in source.EnumerateArray())
        {
            if (TryParseApprovalDecisionOption(item, out var option))
            {
                options.Add(option);
            }
        }

        return options.Count > 0 ? options : null;
    }

    public static IReadOnlyList<string>? ResolveAvailableDecisions(IReadOnlyList<ApprovalDecisionOptionPayload>? options)
        => options is { Count: > 0 }
            ? options.Select(static option => option.Type).ToArray()
            : null;

    public static ExecPolicyAmendmentPayload? ResolveExecPolicyAmendment(JsonElement amendment)
        => TryReadExecPolicyAmendment(amendment, out var parsed) ? parsed : null;

    public static JsonElement? CloneObjectOrNull(JsonElement element)
        => element.ValueKind == JsonValueKind.Object ? element.Clone() : null;

    public static JsonElement? CloneValueOrNull(JsonElement element)
        => element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : element.Clone();

    internal static string? ReadString(JsonElement json, params string[] path)
    {
        var current = json;
        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        if (current.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : current.ToString();
    }

    internal static IReadOnlyList<string>? ReadStringArray(JsonElement json, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!json.TryGetProperty(propertyName, out var property)
                || property.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var values = property
                .EnumerateArray()
                .Where(static item => item.ValueKind == JsonValueKind.String)
                .Select(static item => item.GetString())
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToArray();
            if (values.Length > 0)
            {
                return values;
            }
        }

        return null;
    }

    internal static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static bool TryParseApprovalDecisionOption(JsonElement json, out ApprovalDecisionOptionPayload option)
    {
        option = default!;
        switch (json.ValueKind)
        {
            case JsonValueKind.String:
                var stringType = NormalizeDecisionType(json.GetString());
                if (string.IsNullOrWhiteSpace(stringType))
                {
                    return false;
                }

                option = new ApprovalDecisionOptionPayload(stringType!);
                return true;

            case JsonValueKind.Object:
                if (TryReadObjectDecisionOption(json, out option))
                {
                    return true;
                }

                return TryReadUnionDecisionOption(json, out option);

            default:
                return false;
        }
    }

    private static bool TryReadObjectDecisionOption(JsonElement json, out ApprovalDecisionOptionPayload option)
    {
        option = default!;
        var type = NormalizeDecisionType(ReadString(json, "type"));
        if (string.IsNullOrWhiteSpace(type))
        {
            return false;
        }

        var execPolicyAmendment = type == "acceptWithExecpolicyAmendment"
            ? ReadExecPolicyAmendmentFromObject(json)
            : null;
        var networkPolicyAmendment = type == "applyNetworkPolicyAmendment"
            ? ReadNetworkPolicyAmendmentFromObject(json)
            : null;
        option = new ApprovalDecisionOptionPayload(type!, execPolicyAmendment, networkPolicyAmendment);
        return true;
    }

    private static bool TryReadUnionDecisionOption(JsonElement json, out ApprovalDecisionOptionPayload option)
    {
        option = default!;
        if (!json.EnumerateObject().MoveNext())
        {
            return false;
        }

        foreach (var property in json.EnumerateObject())
        {
            var type = NormalizeDecisionType(property.Name);
            if (string.IsNullOrWhiteSpace(type))
            {
                continue;
            }

            var execPolicyAmendment = type == "acceptWithExecpolicyAmendment"
                ? ReadExecPolicyAmendmentFromUnion(property.Value)
                : null;
            var networkPolicyAmendment = type == "applyNetworkPolicyAmendment"
                ? ReadNetworkPolicyAmendmentFromUnion(property.Value)
                : null;
            option = new ApprovalDecisionOptionPayload(type!, execPolicyAmendment, networkPolicyAmendment);
            return true;
        }

        return false;
    }

    private static string? NormalizeDecisionType(string? value)
    {
        var normalized = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant() switch
        {
            "accept" => "accept",
            "acceptforsession" => "acceptForSession",
            "acceptandremember" => "acceptAndRemember",
            "acceptwithexecpolicyamendment" => "acceptWithExecpolicyAmendment",
            "applynetworkpolicyamendment" => "applyNetworkPolicyAmendment",
            "decline" => "decline",
            "cancel" => "cancel",
            _ => normalized,
        };
    }

    private static bool TryReadExecPolicyAmendment(JsonElement json, out ExecPolicyAmendmentPayload? amendment)
    {
        amendment = null;
        if (json.ValueKind == JsonValueKind.Undefined || json.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        amendment = json.ValueKind switch
        {
            JsonValueKind.Array => BuildExecPolicyAmendment(json),
            JsonValueKind.Object => ReadExecPolicyAmendmentFromObject(json) ?? ReadExecPolicyAmendmentFromUnion(json),
            _ => null,
        };
        return amendment is not null;
    }

    private static ExecPolicyAmendmentPayload? ReadExecPolicyAmendmentFromObject(JsonElement json)
    {
        if (json.TryGetProperty("execPolicyAmendment", out var camel))
        {
            return camel.ValueKind == JsonValueKind.Array ? BuildExecPolicyAmendment(camel) : ReadExecPolicyAmendmentFromUnion(camel);
        }

        if (json.TryGetProperty("execpolicy_amendment", out var snake))
        {
            return snake.ValueKind == JsonValueKind.Array ? BuildExecPolicyAmendment(snake) : ReadExecPolicyAmendmentFromUnion(snake);
        }

        return null;
    }

    private static ExecPolicyAmendmentPayload? ReadExecPolicyAmendmentFromUnion(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Array)
        {
            return BuildExecPolicyAmendment(json);
        }

        if (json.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (json.TryGetProperty("execpolicy_amendment", out var snake))
        {
            return snake.ValueKind == JsonValueKind.Array ? BuildExecPolicyAmendment(snake) : null;
        }

        if (json.TryGetProperty("execPolicyAmendment", out var camel))
        {
            return camel.ValueKind == JsonValueKind.Array ? BuildExecPolicyAmendment(camel) : null;
        }

        return null;
    }

    private static ExecPolicyAmendmentPayload? BuildExecPolicyAmendment(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var commandPrefix = json
            .EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => Normalize(item.GetString()))
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item!)
            .ToArray();
        return commandPrefix.Length == 0 ? null : new ExecPolicyAmendmentPayload(commandPrefix);
    }

    private static NetworkPolicyAmendmentPayload? ReadNetworkPolicyAmendmentFromObject(JsonElement json)
    {
        if (json.TryGetProperty("networkPolicyAmendment", out var camel))
        {
            return BuildNetworkPolicyAmendment(camel);
        }

        if (json.TryGetProperty("network_policy_amendment", out var snake))
        {
            return BuildNetworkPolicyAmendment(snake);
        }

        return null;
    }

    private static NetworkPolicyAmendmentPayload? ReadNetworkPolicyAmendmentFromUnion(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (json.TryGetProperty("network_policy_amendment", out var snake))
        {
            return BuildNetworkPolicyAmendment(snake);
        }

        if (json.TryGetProperty("networkPolicyAmendment", out var camel))
        {
            return BuildNetworkPolicyAmendment(camel);
        }

        return null;
    }

    private static NetworkPolicyAmendmentPayload? BuildNetworkPolicyAmendment(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var host = Normalize(ReadString(json, "host"));
        var action = Normalize(ReadString(json, "action"));
        return string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(action)
            ? null
            : new NetworkPolicyAmendmentPayload(host!, action!);
    }
}
