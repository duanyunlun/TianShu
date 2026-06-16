using System.Text.Json.Serialization;
using TianShu.Contracts.Primitives;

namespace TianShu.VSSDK.Sidecar;

/// <summary>
/// Sidecar 本地权限字段载体。
/// Sidecar-local permission field carrier.
/// </summary>
internal sealed class SidecarPermissionFieldPayload
{
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    [JsonPropertyName("valueType")]
    public string ValueType { get; init; } = string.Empty;

    [JsonPropertyName("valueText")]
    public string ValueText { get; init; } = string.Empty;
}

/// <summary>
/// Sidecar 本地权限请求载体。
/// Sidecar-local permission request carrier.
/// </summary>
internal sealed class SidecarPermissionRequestPayload
{
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("fields")]
    public SidecarPermissionFieldPayload[] Fields { get; init; } = [];

    [JsonPropertyName("permissionsJson")]
    public string? PermissionsJson { get; init; }

    [JsonPropertyName("summary")]
    public string? Summary { get; init; }
}

/// <summary>
/// Sidecar 本地补录选项载体。
/// Sidecar-local user-input option carrier.
/// </summary>
internal sealed class SidecarUserInputOptionPayload
{
    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

/// <summary>
/// Sidecar 本地补录问题载体。
/// Sidecar-local user-input question carrier.
/// </summary>
internal sealed class SidecarUserInputQuestionPayload
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("header")]
    public string Header { get; init; } = string.Empty;

    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = string.Empty;

    [JsonPropertyName("isSecret")]
    public bool IsSecret { get; init; }

    [JsonPropertyName("isOther")]
    public bool IsOther { get; init; }

    [JsonPropertyName("options")]
    public SidecarUserInputOptionPayload[]? Options { get; init; }
}

/// <summary>
/// Sidecar 本地补录请求载体。
/// Sidecar-local user-input request carrier.
/// </summary>
internal sealed class SidecarUserInputRequestPayload
{
    [JsonPropertyName("questions")]
    public SidecarUserInputQuestionPayload[] Questions { get; init; } = [];

    [JsonPropertyName("summary")]
    public string? Summary { get; init; }

    [JsonPropertyName("mode")]
    public string? Mode { get; init; }

    [JsonPropertyName("requestedSchema")]
    public StructuredValue? RequestedSchema { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("serverName")]
    public string? ServerName { get; init; }

    [JsonPropertyName("elicitationId")]
    public string? ElicitationId { get; init; }
}

/// <summary>
/// Sidecar 本地服务端请求已解决载体。
/// Sidecar-local server-request-resolved carrier.
/// </summary>
internal sealed class SidecarServerRequestResolvedPayload
{
    [JsonPropertyName("requestId")]
    public long RequestId { get; init; }

    [JsonPropertyName("requestKind")]
    public string? RequestKind { get; init; }

    [JsonPropertyName("callId")]
    public string? CallId { get; init; }

    [JsonPropertyName("requestIdRaw")]
    public string? RequestIdRaw { get; init; }
}

/// <summary>
/// Sidecar 本地待处理跟进对比键载体。
/// Sidecar-local pending-follow-up compare-key carrier.
/// </summary>
internal sealed class SidecarPendingFollowUpCompareKeyPayload
{
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("imageCount")]
    public int ImageCount { get; init; }
}

/// <summary>
/// Sidecar 本地待处理跟进载体。
/// Sidecar-local pending-follow-up carrier.
/// </summary>
internal sealed class SidecarPendingFollowUpPayload
{
    [JsonPropertyName("correlationId")]
    public string CorrelationId { get; init; } = string.Empty;

    [JsonPropertyName("requestedMode")]
    public string RequestedMode { get; init; } = string.Empty;

    [JsonPropertyName("effectiveMode")]
    public string EffectiveMode { get; init; } = string.Empty;

    [JsonPropertyName("lifecycleState")]
    public string LifecycleState { get; init; } = string.Empty;

    [JsonPropertyName("expectedTurnId")]
    public string? ExpectedTurnId { get; init; }

    [JsonPropertyName("turnId")]
    public string? TurnId { get; init; }

    [JsonPropertyName("compareKey")]
    public SidecarPendingFollowUpCompareKeyPayload? CompareKey { get; init; }
}

/// <summary>
/// Sidecar 本地待处理输入条目载体。
/// Sidecar-local pending-input entry carrier.
/// </summary>
internal sealed class SidecarPendingInputStateEntryPayload
{
    [JsonPropertyName("correlationId")]
    public string CorrelationId { get; init; } = string.Empty;

    [JsonPropertyName("requestedMode")]
    public string RequestedMode { get; init; } = string.Empty;

    [JsonPropertyName("effectiveMode")]
    public string EffectiveMode { get; init; } = string.Empty;

    [JsonPropertyName("lifecycleState")]
    public string LifecycleState { get; init; } = string.Empty;

    [JsonPropertyName("expectedTurnId")]
    public string? ExpectedTurnId { get; init; }

    [JsonPropertyName("turnId")]
    public string? TurnId { get; init; }

    [JsonPropertyName("pendingBucket")]
    public string PendingBucket { get; init; } = "QueuedUserMessage";

    [JsonPropertyName("compareKey")]
    public SidecarPendingFollowUpCompareKeyPayload? CompareKey { get; init; }

    [JsonPropertyName("inputs")]
    public SidecarUserInputPayload[] Inputs { get; init; } = [];
}

/// <summary>
/// Sidecar 本地待处理输入状态载体。
/// Sidecar-local pending-input state carrier.
/// </summary>
internal sealed class SidecarPendingInputStatePayload
{
    [JsonPropertyName("entries")]
    public SidecarPendingInputStateEntryPayload[] Entries { get; init; } = [];

    [JsonPropertyName("queuedUserMessages")]
    public SidecarPendingInputStateEntryPayload[] QueuedUserMessages { get; init; } = [];

    [JsonPropertyName("pendingSteers")]
    public SidecarPendingInputStateEntryPayload[] PendingSteers { get; init; } = [];

    [JsonPropertyName("interruptRequestPending")]
    public bool InterruptRequestPending { get; init; }

    [JsonPropertyName("submitPendingSteersAfterInterrupt")]
    public bool SubmitPendingSteersAfterInterrupt { get; init; }
}
