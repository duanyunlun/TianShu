using System.Text.Json.Serialization;
using TianShu.Contracts.Conversations;

namespace TianShu.VSSDK.Sidecar;

/// <summary>
/// Sidecar 本地执行策略补丁载体。
/// Sidecar-local execution-policy amendment carrier.
/// </summary>
internal sealed class SidecarExecPolicyAmendmentPayload
{
    [JsonPropertyName("commandPrefix")]
    public string[] CommandPrefix { get; init; } = [];
}

/// <summary>
/// Sidecar 本地网络策略补丁载体。
/// Sidecar-local network-policy amendment carrier.
/// </summary>
internal sealed class SidecarNetworkPolicyAmendmentPayload
{
    [JsonPropertyName("host")]
    public string? Host { get; init; }

    [JsonPropertyName("action")]
    public string? Action { get; init; }
}

/// <summary>
/// Sidecar 本地审批选项载体。
/// Sidecar-local approval-decision option carrier.
/// </summary>
internal sealed class SidecarApprovalDecisionOptionPayload
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("execPolicyAmendment")]
    public SidecarExecPolicyAmendmentPayload? ExecPolicyAmendment { get; init; }

    [JsonPropertyName("networkPolicyAmendment")]
    public SidecarNetworkPolicyAmendmentPayload? NetworkPolicyAmendment { get; init; }

    public static SidecarApprovalDecisionOptionPayload FromControlPlane(ControlPlaneApprovalDecisionOption payload)
        => new SidecarApprovalDecisionOptionPayload
        {
            Type = payload.Type,
            ExecPolicyAmendment = payload.ExecPolicyAmendment is not { CommandPrefix.Count: > 0 }
                ? null
                : new SidecarExecPolicyAmendmentPayload
                {
                    CommandPrefix = payload.ExecPolicyAmendment.CommandPrefix.ToArray(),
                },
            NetworkPolicyAmendment = payload.NetworkPolicyAmendment is null
                ? null
                : new SidecarNetworkPolicyAmendmentPayload
                {
                    Host = payload.NetworkPolicyAmendment.Host,
                    Action = payload.NetworkPolicyAmendment.Action,
                },
        };
}

/// <summary>
/// Sidecar 本地审批元数据字段载体。
/// Sidecar-local approval metadata-field carrier.
/// </summary>
internal sealed class SidecarApprovalMetadataFieldPayload
{
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    [JsonPropertyName("valueType")]
    public string ValueType { get; init; } = string.Empty;

    [JsonPropertyName("valueText")]
    public string ValueText { get; init; } = string.Empty;
}

/// <summary>
/// Sidecar 本地审批请求载体。
/// Sidecar-local approval request carrier.
/// </summary>
internal sealed class SidecarApprovalRequestPayload
{
    [JsonPropertyName("toolName")]
    public string? ToolName { get; init; }

    [JsonPropertyName("approvalKind")]
    public string? ApprovalKind { get; init; }

    [JsonPropertyName("availableDecisions")]
    public string[]? AvailableDecisions { get; init; }

    [JsonPropertyName("availableDecisionOptions")]
    public SidecarApprovalDecisionOptionPayload[]? AvailableDecisionOptions { get; init; }

    [JsonPropertyName("summary")]
    public string? Summary { get; init; }

    [JsonPropertyName("metadataFields")]
    public SidecarApprovalMetadataFieldPayload[] MetadataFields { get; init; } = [];

    [JsonPropertyName("proposedExecPolicyAmendment")]
    public SidecarExecPolicyAmendmentPayload? ProposedExecPolicyAmendment { get; init; }

    [JsonPropertyName("proposedNetworkPolicyAmendments")]
    public SidecarNetworkPolicyAmendmentPayload[]? ProposedNetworkPolicyAmendments { get; init; }

    public static SidecarApprovalDecisionOptionPayload[]? MapDecisionOptions(
        IReadOnlyList<ControlPlaneApprovalDecisionOption>? payloads)
        => payloads is not { Count: > 0 }
            ? null
            : payloads.Select(SidecarApprovalDecisionOptionPayload.FromControlPlane).ToArray();
}
