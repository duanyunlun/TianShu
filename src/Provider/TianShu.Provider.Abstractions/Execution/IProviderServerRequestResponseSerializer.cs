using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;

namespace TianShu.Provider.Abstractions;

/// <summary>
/// Provider 服务端请求响应序列化器边界。
/// Serializer boundary for provider-originated server-request responses.
/// </summary>
public interface IProviderServerRequestResponseSerializer
{
    /// <summary>
    /// 序列化审批响应，返回 provider 所需的多种 southbound 负载形状。
    /// Serializes an approval response into provider-specific southbound payload shapes.
    /// </summary>
    ProviderApprovalResponseFormats SerializeApprovalResponse(ProviderApprovalOutcome response);

    /// <summary>
    /// 序列化权限授予响应。
    /// Serializes a permission-grant response.
    /// </summary>
    ProviderPermissionResponseFormats SerializePermissionResponse(ProviderPermissionGrantOutcome response);

    /// <summary>
    /// 序列化用户输入响应。
    /// Serializes a user-input response.
    /// </summary>
    ProviderUserInputResponseFormats SerializeUserInputResponse(ProviderUserInputOutcome response);

    /// <summary>
    /// 序列化动态工具调用响应。
    /// Serializes a dynamic tool-call response.
    /// </summary>
    ProviderDynamicToolCallResponseFormats SerializeDynamicToolCallResponse(ToolInvocationResult result);
}

/// <summary>
/// Provider 审批决策。
/// Provider-neutral approval decision.
/// </summary>
public enum ProviderApprovalDecision
{
    Accept = 0,
    AcceptForSession = 1,
    AcceptAndRemember = 2,
    AcceptWithExecPolicyAmendment = 3,
    ApplyNetworkPolicyAmendment = 4,
    Decline = 5,
    Cancel = 6,
}

/// <summary>
/// Provider-neutral 审批响应输入模型。
/// Provider-neutral input model for approval responses.
/// </summary>
public sealed record ProviderApprovalOutcome(
    ProviderApprovalDecision Decision,
    string? Note = null,
    IReadOnlyList<string>? ExecPolicyCommandPrefix = null,
    string? NetworkPolicyHost = null,
    string? NetworkPolicyAction = null);

/// <summary>
/// 审批响应的 provider-specific 负载集合。
/// Provider-specific payload set for approval responses.
/// </summary>
public sealed record ProviderApprovalResponseFormats(
    StructuredValue DecisionPayload,
    StructuredValue StandardServerRequestPayload,
    StructuredValue LegacyServerRequestPayload,
    StructuredValue McpServerElicitationPayload);

/// <summary>
/// Provider-neutral 权限授权范围。
/// Provider-neutral permission grant scope.
/// </summary>
public enum ProviderPermissionScope
{
    Turn = 0,
    Session = 1,
}

/// <summary>
/// Provider-neutral 权限授予输入模型。
/// Provider-neutral input model for permission-grant responses.
/// </summary>
public sealed record ProviderPermissionGrantOutcome(
    IReadOnlyDictionary<string, StructuredValue> Permissions,
    ProviderPermissionScope Scope = ProviderPermissionScope.Turn);

/// <summary>
/// 权限响应序列化结果。
/// Serialized permission-response payload and projection summary.
/// </summary>
public sealed record ProviderPermissionResponseFormats(
    StructuredValue Payload,
    string Summary);

/// <summary>
/// Provider-neutral 用户输入提交模型。
/// Provider-neutral input model for user-input submissions.
/// </summary>
public sealed record ProviderUserInputOutcome(
    IReadOnlyDictionary<string, StructuredValue> Answers);

/// <summary>
/// 用户输入响应序列化结果。
/// Serialized user-input payloads and provider-specific summary metadata.
/// </summary>
public sealed record ProviderUserInputResponseFormats(
    StructuredValue ToolRequestPayload,
    string ToolRequestSummary,
    StructuredValue McpServerElicitationPayload,
    string McpServerElicitationAction,
    string McpServerElicitationSummary,
    string McpServerElicitationStatus);

/// <summary>
/// 动态工具调用响应序列化结果。
/// Serialized dynamic tool-call payload and projection summary.
/// </summary>
public sealed record ProviderDynamicToolCallResponseFormats(
    StructuredValue Payload,
    string OutputText,
    bool Success);
