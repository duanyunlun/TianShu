using System.Text.Json;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Provider;

namespace TianShu.Provider.Abstractions;

/// <summary>
/// Provider 服务端请求载荷解释器边界。
/// Typed interpreter boundary for provider-originated server-request payloads.
/// </summary>
public interface IProviderServerRequestInterpreter
{
    /// <summary>
    /// 将 provider 原始请求参数解释为 runtime 可消费的 typed 请求模型。
    /// Interprets provider request parameters into runtime-consumable typed request models.
    /// </summary>
    ProviderServerRequestPayload? Interpret(ProviderServerRequestRoute route, JsonElement parameters);
}

/// <summary>
/// 审批元数据字段载体。
/// Approval metadata field payload.
/// </summary>
public sealed record ApprovalMetadataFieldPayload(
    string Key,
    string ValueType,
    string ValueText);

/// <summary>
/// 审批请求载体。
/// Approval request payload.
/// </summary>
public sealed record ApprovalRequestPayload(
    string? ToolName,
    string? ApprovalKind,
    IReadOnlyList<string>? AvailableDecisions,
    string? Summary,
    IReadOnlyList<ApprovalMetadataFieldPayload> MetadataFields,
    IReadOnlyList<ApprovalDecisionOptionPayload>? AvailableDecisionOptions = null,
    ExecPolicyAmendmentPayload? ProposedExecPolicyAmendment = null,
    IReadOnlyList<NetworkPolicyAmendmentPayload>? ProposedNetworkPolicyAmendments = null);

/// <summary>
/// 权限字段载体。
/// Permission field payload.
/// </summary>
public sealed record PermissionFieldPayload(
    string Key,
    string ValueType,
    string ValueText);

/// <summary>
/// 权限请求载体。
/// Permission request payload.
/// </summary>
public sealed record PermissionRequestPayload(
    string? Reason,
    IReadOnlyList<PermissionFieldPayload> Fields,
    string? PermissionsJson,
    string? Summary);

/// <summary>
/// 用户输入选项载体。
/// User-input option payload.
/// </summary>
public sealed record UserInputOptionPayload(
    string Label,
    string? Description);

/// <summary>
/// 用户输入问题载体。
/// User-input question payload.
/// </summary>
public sealed record UserInputQuestionPayload(
    string Id,
    string Header,
    string Prompt,
    bool IsSecret,
    bool IsOther,
    IReadOnlyList<UserInputOptionPayload>? Options);

/// <summary>
/// 用户输入请求载体。
/// User-input request payload.
/// </summary>
public sealed record UserInputRequestPayload(
    IReadOnlyList<UserInputQuestionPayload> Questions,
    string? Summary,
    string? Mode = null,
    StructuredValue? RequestedSchema = null,
    string? Url = null,
    string? ServerName = null,
    string? ElicitationId = null);

/// <summary>
/// Provider 服务端请求的统一 typed 基类。
/// Unified typed base model for provider server requests.
/// </summary>
public abstract record ProviderServerRequestPayload(
    string? ThreadId,
    string? TurnId,
    string? ItemId,
    string CallId,
    string ToolName,
    string? ServerName,
    string Summary);

/// <summary>
/// 命令执行审批请求。
/// Command-execution approval request.
/// </summary>
public sealed record ProviderCommandExecutionApprovalRequest(
    string? ThreadId,
    string? TurnId,
    string? ItemId,
    string CallId,
    string Summary,
    ApprovalRequestPayload ApprovalRequest)
    : ProviderServerRequestPayload(ThreadId, TurnId, ItemId, CallId, "commandExecution", null, Summary);

/// <summary>
/// 文件变更审批请求。
/// File-change approval request.
/// </summary>
public sealed record ProviderFileChangeApprovalRequest(
    string? ThreadId,
    string? TurnId,
    string? ItemId,
    string CallId,
    string Summary,
    ApprovalRequestPayload ApprovalRequest)
    : ProviderServerRequestPayload(ThreadId, TurnId, ItemId, CallId, "fileChange", null, Summary);

/// <summary>
/// 工具审批请求。
/// Tool approval request.
/// </summary>
public sealed record ProviderToolApprovalRequest(
    string? ThreadId,
    string? TurnId,
    string? ItemId,
    string CallId,
    string ToolName,
    string? ServerName,
    string Summary,
    ApprovalRequestPayload ApprovalRequest)
    : ProviderServerRequestPayload(ThreadId, TurnId, ItemId, CallId, ToolName, ServerName, Summary);

/// <summary>
/// MCP server elicitation 审批请求。
/// MCP server elicitation approval request.
/// </summary>
public sealed record ProviderMcpServerElicitationApprovalRequest(
    string? ThreadId,
    string? TurnId,
    string CallId,
    string ToolName,
    string? ServerName,
    string Summary,
    ApprovalRequestPayload ApprovalRequest)
    : ProviderServerRequestPayload(ThreadId, TurnId, null, CallId, ToolName, ServerName, Summary);

/// <summary>
/// MCP server elicitation 用户输入请求。
/// MCP server elicitation user-input request.
/// </summary>
public sealed record ProviderMcpServerElicitationUserInputRequest(
    string? ThreadId,
    string? TurnId,
    string CallId,
    string ToolName,
    string? ServerName,
    string Summary,
    UserInputRequestPayload UserInputRequest)
    : ProviderServerRequestPayload(ThreadId, TurnId, null, CallId, ToolName, ServerName, Summary);

/// <summary>
/// 权限请求。
/// Permission request.
/// </summary>
public sealed record ProviderPermissionRequestApprovalRequest(
    string? ThreadId,
    string? TurnId,
    string? ItemId,
    string CallId,
    string Summary,
    PermissionRequestPayload PermissionRequest)
    : ProviderServerRequestPayload(ThreadId, TurnId, ItemId, CallId, "request_permissions", null, Summary);

/// <summary>
/// 工具用户输入请求。
/// Tool user-input request.
/// </summary>
public sealed record ProviderToolUserInputRequest(
    string? ThreadId,
    string? TurnId,
    string? ItemId,
    string CallId,
    string Summary,
    UserInputRequestPayload UserInputRequest)
    : ProviderServerRequestPayload(ThreadId, TurnId, ItemId, CallId, "requestUserInput", null, Summary);

/// <summary>
/// 动态工具调用请求。
/// Dynamic tool-call request.
/// </summary>
public sealed record ProviderDynamicToolCallRequest(
    string? ThreadId,
    string? TurnId,
    string CallId,
    string ToolName,
    StructuredValue Arguments,
    string InputText)
    : ProviderServerRequestPayload(ThreadId, TurnId, null, CallId, ToolName, null, InputText);
