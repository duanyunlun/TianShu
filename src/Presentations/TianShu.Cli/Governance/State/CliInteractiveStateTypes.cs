using System.Globalization;
using System.Text.Json;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;

namespace TianShu.Cli;

/// <summary>
/// CLI 本地执行策略修订载体。
/// CLI-local execution-policy amendment carrier.
/// </summary>
internal sealed record CliExecPolicyAmendmentPayload(
    IReadOnlyList<string> CommandPrefix)
{
    public static CliExecPolicyAmendmentPayload? FromControlPlane(ControlPlaneExecPolicyAmendment? payload)
        => payload is null ? null : new CliExecPolicyAmendmentPayload(payload.CommandPrefix.ToArray());
}

/// <summary>
/// CLI 本地网络策略修订载体。
/// CLI-local network-policy amendment carrier.
/// </summary>
internal sealed record CliNetworkPolicyAmendmentPayload(
    string Host,
    string Action)
{
    public static CliNetworkPolicyAmendmentPayload? FromControlPlane(ControlPlaneNetworkPolicyAmendment? payload)
        => payload is null ? null : new CliNetworkPolicyAmendmentPayload(payload.Host, payload.Action);
}

/// <summary>
/// CLI 本地审批选项载体。
/// CLI-local approval decision option carrier.
/// </summary>
internal sealed record CliApprovalDecisionOptionPayload(
    string Type,
    CliExecPolicyAmendmentPayload? ExecPolicyAmendment = null,
    CliNetworkPolicyAmendmentPayload? NetworkPolicyAmendment = null)
{
    public static CliApprovalDecisionOptionPayload? FromControlPlane(ControlPlaneApprovalDecisionOption? payload)
        => payload is null
            ? null
            : new CliApprovalDecisionOptionPayload(
                payload.Type,
                CliExecPolicyAmendmentPayload.FromControlPlane(payload.ExecPolicyAmendment),
                CliNetworkPolicyAmendmentPayload.FromControlPlane(payload.NetworkPolicyAmendment));
}

/// <summary>
/// CLI 本地审批元数据字段载体。
/// CLI-local approval metadata field carrier.
/// </summary>
internal sealed record CliApprovalMetadataFieldPayload(
    string Key,
    string ValueType,
    string ValueText);

/// <summary>
/// CLI 本地审批请求载体。
/// CLI-local approval request carrier.
/// </summary>
internal sealed record CliApprovalRequestPayload(
    string? ToolName,
    string? ApprovalKind,
    IReadOnlyList<string>? AvailableDecisions,
    string? Summary,
    IReadOnlyList<CliApprovalMetadataFieldPayload> MetadataFields,
    IReadOnlyList<CliApprovalDecisionOptionPayload>? AvailableDecisionOptions = null,
    CliExecPolicyAmendmentPayload? ProposedExecPolicyAmendment = null,
    IReadOnlyList<CliNetworkPolicyAmendmentPayload>? ProposedNetworkPolicyAmendments = null);

/// <summary>
/// CLI 本地权限字段载体。
/// CLI-local permission field carrier.
/// </summary>
internal sealed record CliPermissionFieldPayload(
    string Key,
    string ValueType,
    string ValueText);

/// <summary>
/// CLI 本地权限请求载体。
/// CLI-local permission request carrier.
/// </summary>
internal sealed record CliPermissionRequestPayload(
    string? Reason,
    IReadOnlyList<CliPermissionFieldPayload> Fields,
    string? PermissionsJson,
    string? Summary);

/// <summary>
/// CLI 本地补录选项载体。
/// CLI-local user-input option carrier.
/// </summary>
internal sealed record CliUserInputOptionPayload(
    string Label,
    string? Description);

/// <summary>
/// CLI 本地补录问题载体。
/// CLI-local user-input question carrier.
/// </summary>
internal sealed record CliUserInputQuestionPayload(
    string Id,
    string Header,
    string Prompt,
    bool IsSecret,
    bool IsOther,
    IReadOnlyList<CliUserInputOptionPayload>? Options);

/// <summary>
/// CLI 本地补录请求载体。
/// CLI-local user-input request carrier.
/// </summary>
internal sealed record CliUserInputRequestPayload(
    IReadOnlyList<CliUserInputQuestionPayload> Questions,
    string? Summary,
    string? Mode = null,
    StructuredValue? RequestedSchema = null,
    string? Url = null,
    string? ServerName = null,
    string? ElicitationId = null);

/// <summary>
/// CLI 本地待处理跟进对比键载体。
/// CLI-local pending-follow-up compare-key carrier.
/// </summary>
internal sealed record CliPendingFollowUpCompareKeyPayload(
    string? Message,
    int ImageCount);

/// <summary>
/// CLI 本地待处理跟进载体。
/// CLI-local pending-follow-up carrier.
/// </summary>
internal sealed record CliPendingFollowUpPayload(
    string CorrelationId,
    string RequestedMode,
    string EffectiveMode,
    string LifecycleState,
    string? ExpectedTurnId,
    string? TurnId,
    CliPendingFollowUpCompareKeyPayload? CompareKey);

/// <summary>
/// CLI 本地待处理输入条目载体。
/// CLI-local pending-input entry carrier.
/// </summary>
internal sealed record CliPendingInputStateEntryPayload(
    string CorrelationId,
    string RequestedMode,
    string EffectiveMode,
    string LifecycleState,
    string? ExpectedTurnId,
    string? TurnId,
    CliPendingFollowUpCompareKeyPayload? CompareKey,
    string PendingBucket = "QueuedUserMessage",
    IReadOnlyList<ControlPlaneInputItem>? Inputs = null)
{
    public static CliPendingInputStateEntryPayload FromControlPlane(ControlPlanePendingInputStateEntry payload)
        => new(
            payload.CorrelationId,
            payload.RequestedMode,
            payload.EffectiveMode,
            payload.LifecycleState,
            payload.ExpectedTurnId,
            payload.TurnId,
            CliInteractiveStateConverters.ToCliPendingFollowUpCompareKeyPayload(payload.CompareKey),
            payload.PendingBucket,
            payload.Inputs?.ToArray());
}

/// <summary>
/// CLI 本地待处理输入状态载体。
/// CLI-local pending-input state carrier.
/// </summary>
internal sealed record CliPendingInputStatePayload(
    IReadOnlyList<CliPendingInputStateEntryPayload> Entries,
    bool InterruptRequestPending,
    bool SubmitPendingSteersAfterInterrupt = false,
    IReadOnlyList<CliPendingInputStateEntryPayload>? QueuedUserMessages = null,
    IReadOnlyList<CliPendingInputStateEntryPayload>? PendingSteers = null)
{
    public static CliPendingInputStatePayload? FromControlPlane(ControlPlanePendingInputState? payload)
        => payload is null
            ? null
            : new CliPendingInputStatePayload(
                payload.Entries.Select(CliPendingInputStateEntryPayload.FromControlPlane).ToArray(),
                payload.InterruptRequestPending,
                payload.SubmitPendingSteersAfterInterrupt,
                payload.QueuedUserMessages?.Select(CliPendingInputStateEntryPayload.FromControlPlane).ToArray(),
                payload.PendingSteers?.Select(CliPendingInputStateEntryPayload.FromControlPlane).ToArray());
}

/// <summary>
/// CLI 本地待处理交互请求基础载体。
/// CLI-local pending interactive request base carrier.
/// </summary>
internal abstract record CliPendingInteractiveRequestState(
    string CallId,
    string? ThreadId,
    string? TurnId);

/// <summary>
/// CLI 本地待处理审批请求载体。
/// CLI-local pending approval-request carrier.
/// </summary>
internal sealed record CliPendingApprovalRequestState(
    string CallId,
    string? ThreadId,
    string? TurnId,
    string? ToolName,
    string? ApprovalKind,
    IReadOnlyList<string>? AvailableDecisions,
    IReadOnlyList<CliApprovalDecisionOptionPayload>? AvailableDecisionOptions)
    : CliPendingInteractiveRequestState(CallId, ThreadId, TurnId);

/// <summary>
/// CLI 本地待处理权限请求载体。
/// CLI-local pending permission-request carrier.
/// </summary>
internal sealed record CliPendingPermissionRequestState(
    string CallId,
    string? ThreadId,
    string? TurnId,
    string? ToolName)
    : CliPendingInteractiveRequestState(CallId, ThreadId, TurnId);

/// <summary>
/// CLI 本地待处理补录请求载体。
/// CLI-local pending user-input-request carrier.
/// </summary>
internal sealed record CliPendingUserInputRequestState(
    string CallId,
    string? ThreadId,
    string? TurnId,
    string? ToolName)
    : CliPendingInteractiveRequestState(CallId, ThreadId, TurnId);

/// <summary>
/// CLI 本地交互态转换工具。
/// CLI-local interactive-state conversion helpers.
/// </summary>
internal static class CliInteractiveStateConverters
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

    public static CliPendingApprovalRequestState? ToPendingApprovalRequestState(ControlPlaneConversationStreamEvent? streamEvent)
    {
        if (streamEvent is null || streamEvent.CallId is null)
        {
            return null;
        }

        return new CliPendingApprovalRequestState(
            streamEvent.CallId.Value.Value,
            streamEvent.ThreadId?.Value,
            streamEvent.TurnId?.Value,
            streamEvent.ToolName,
            streamEvent.ApprovalKind,
            streamEvent.AvailableDecisions?.ToArray(),
            streamEvent.AvailableDecisionOptions?.Select(CliApprovalDecisionOptionPayload.FromControlPlane).OfType<CliApprovalDecisionOptionPayload>().ToArray());
    }

    public static CliPendingPermissionRequestState? ToPendingPermissionRequestState(ControlPlaneConversationStreamEvent? streamEvent)
    {
        if (streamEvent is null || streamEvent.CallId is null)
        {
            return null;
        }

        return new CliPendingPermissionRequestState(
            streamEvent.CallId.Value.Value,
            streamEvent.ThreadId?.Value,
            streamEvent.TurnId?.Value,
            streamEvent.ToolName);
    }

    public static CliPendingUserInputRequestState? ToPendingUserInputRequestState(ControlPlaneConversationStreamEvent? streamEvent)
    {
        if (streamEvent is null || streamEvent.CallId is null)
        {
            return null;
        }

        return new CliPendingUserInputRequestState(
            streamEvent.CallId.Value.Value,
            streamEvent.ThreadId?.Value,
            streamEvent.TurnId?.Value,
            streamEvent.ToolName);
    }

    public static CliPendingInputStatePayload? ToCliPendingInputStatePayload(ControlPlanePendingInputState? payload)
        => CliPendingInputStatePayload.FromControlPlane(payload);

    public static CliPendingFollowUpCompareKeyPayload? ToCliPendingFollowUpCompareKeyPayload(StructuredValue? payload)
    {
        if (payload is null || payload.Kind != StructuredValueKind.Object)
        {
            return null;
        }

        static string? ReadStringProperty(StructuredValue source, string name)
            => source.Properties.TryGetValue(name, out var entry)
                ? Convert.ToString(entry.ToPlainObject(), CultureInfo.InvariantCulture)
                : null;

        var imageCount = 0;
        var imageCountText = ReadStringProperty(payload, "imageCount");
        if (!string.IsNullOrWhiteSpace(imageCountText))
        {
            _ = int.TryParse(imageCountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out imageCount);
        }

        return new CliPendingFollowUpCompareKeyPayload(
            ReadStringProperty(payload, "message"),
            imageCount);
    }

    public static CliPendingFollowUpPayload? ReadPendingFollowUpPayload(ControlPlaneConversationStreamEvent streamEvent)
        => ReadPayload<CliPendingFollowUpPayload>(streamEvent, ControlPlaneConversationStreamPayloadKind.PendingFollowUp);

    public static CliPendingInputStatePayload? ReadPendingInputStatePayload(ControlPlaneConversationStreamEvent streamEvent)
    {
        if (streamEvent.PayloadKind != ControlPlaneConversationStreamPayloadKind.PendingInputState
            || streamEvent.Payload is null)
        {
            return null;
        }

        var payloadElement = JsonSerializer.SerializeToElement(streamEvent.Payload.ToPlainObject(), PayloadJsonOptions);
        return new CliPendingInputStatePayload(
            ReadPendingInputStateEntries(payloadElement, "entries"),
            ReadBoolean(payloadElement, "interruptRequestPending"),
            ReadBoolean(payloadElement, "submitPendingSteersAfterInterrupt"),
            ReadPendingInputStateEntries(payloadElement, "queuedUserMessages"),
            ReadPendingInputStateEntries(payloadElement, "pendingSteers"));
    }

    private static TPayload? ReadPayload<TPayload>(
        ControlPlaneConversationStreamEvent streamEvent,
        ControlPlaneConversationStreamPayloadKind payloadKind)
        where TPayload : class
    {
        if (streamEvent.PayloadKind != payloadKind || streamEvent.Payload is null)
        {
            return null;
        }

        var payloadElement = JsonSerializer.SerializeToElement(streamEvent.Payload.ToPlainObject(), PayloadJsonOptions);
        return JsonSerializer.Deserialize<TPayload>(payloadElement, PayloadJsonOptions);
    }

    private static CliPendingInputStateEntryPayload[] ReadPendingInputStateEntries(JsonElement payloadElement, string propertyName)
    {
        if (!payloadElement.TryGetProperty(propertyName, out var entriesElement)
            || entriesElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<CliPendingInputStateEntryPayload>();
        }

        return entriesElement
            .EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.Object)
            .Select(ReadPendingInputStateEntryPayload)
            .ToArray();
    }

    private static CliPendingInputStateEntryPayload ReadPendingInputStateEntryPayload(JsonElement entryElement)
        => new(
            Normalize(ReadString(entryElement, "correlationId")) ?? string.Empty,
            Normalize(ReadString(entryElement, "requestedMode")) ?? string.Empty,
            Normalize(ReadString(entryElement, "effectiveMode")) ?? string.Empty,
            Normalize(ReadString(entryElement, "lifecycleState")) ?? string.Empty,
            Normalize(ReadString(entryElement, "expectedTurnId")),
            Normalize(ReadString(entryElement, "turnId")),
            ReadPendingFollowUpCompareKeyPayload(entryElement, "compareKey"),
            Normalize(ReadString(entryElement, "pendingBucket")) ?? "QueuedUserMessage",
            ReadControlPlaneInputs(entryElement, "inputs"));

    private static CliPendingFollowUpCompareKeyPayload? ReadPendingFollowUpCompareKeyPayload(
        JsonElement payloadElement,
        string propertyName)
    {
        if (!payloadElement.TryGetProperty(propertyName, out var compareKeyElement)
            || compareKeyElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new CliPendingFollowUpCompareKeyPayload(
            Normalize(ReadString(compareKeyElement, "message")),
            ReadInt32(compareKeyElement, "imageCount") ?? 0);
    }

    private static IReadOnlyList<ControlPlaneInputItem>? ReadControlPlaneInputs(JsonElement payloadElement, string propertyName)
    {
        if (!payloadElement.TryGetProperty(propertyName, out var inputsElement)
            || inputsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var inputs = inputsElement
            .EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.Object)
            .Select(ReadControlPlaneInput)
            .OfType<ControlPlaneInputItem>()
            .ToArray();
        return inputs.Length == 0 ? null : inputs;
    }

    private static ControlPlaneInputItem? ReadControlPlaneInput(JsonElement inputElement)
        => Normalize(ReadString(inputElement, "type")) switch
        {
            "text" => new ControlPlaneTextInput(
                ReadString(inputElement, "text") ?? string.Empty,
                ReadTextElements(inputElement, "textElements")),
            "image" when Normalize(ReadString(inputElement, "url")) is { } url => new ControlPlaneImageInput(url),
            "local_image" when Normalize(ReadString(inputElement, "path")) is { } path => new ControlPlaneLocalImageInput(path),
            "skill" when Normalize(ReadString(inputElement, "name")) is { } name
                           && Normalize(ReadString(inputElement, "path")) is { } skillPath => new ControlPlaneSkillInput(name, skillPath),
            "mention" when Normalize(ReadString(inputElement, "name")) is { } mentionName
                             && Normalize(ReadString(inputElement, "path")) is { } mentionPath => new ControlPlaneMentionInput(mentionName, mentionPath),
            _ => null,
        };

    private static IReadOnlyList<ControlPlaneTextElement>? ReadTextElements(JsonElement payloadElement, string propertyName)
    {
        if (!payloadElement.TryGetProperty(propertyName, out var textElementsElement)
            || textElementsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var textElements = textElementsElement
            .EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.Object)
            .Select(ReadTextElement)
            .OfType<ControlPlaneTextElement>()
            .ToArray();
        return textElements.Length == 0 ? null : textElements;
    }

    private static ControlPlaneTextElement? ReadTextElement(JsonElement textElement)
    {
        var byteRange = ReadByteRange(textElement, "byteRange");
        return byteRange is null
            ? null
            : new ControlPlaneTextElement(byteRange, Normalize(ReadString(textElement, "placeholder")));
    }

    private static ControlPlaneByteRange? ReadByteRange(JsonElement payloadElement, string propertyName)
    {
        if (!payloadElement.TryGetProperty(propertyName, out var byteRangeElement)
            || byteRangeElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new ControlPlaneByteRange(
            ReadInt32(byteRangeElement, "start") ?? 0,
            ReadInt32(byteRangeElement, "end") ?? 0);
    }

    private static int? ReadInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
            _ => null,
        };
    }

    private static bool ReadBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var value) => value,
            _ => false,
        };
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => property.ToString(),
        };
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
