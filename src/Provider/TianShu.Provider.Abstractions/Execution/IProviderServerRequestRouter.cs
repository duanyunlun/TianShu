namespace TianShu.Provider.Abstractions;

/// <summary>
/// Provider 服务端请求分发边界。
/// Typed routing boundary for provider-originated server requests.
/// </summary>
public interface IProviderServerRequestRouter
{
    /// <summary>
    /// 将 provider 方法名映射为 provider-neutral 的请求语义。
    /// Maps a provider method name into provider-neutral request semantics.
    /// </summary>
    ProviderServerRequestRoute? Route(string? method);

    /// <summary>
    /// 根据 provider 请求方法名解析挂起交互请求类型。
    /// Resolves the pending interactive request kind from a provider request method.
    /// </summary>
    string? ResolvePendingRequestKind(string? requestMethod);
}

/// <summary>
/// provider 服务端请求语义枚举。
/// Semantic kinds of provider server requests.
/// </summary>
public enum ProviderServerRequestKind
{
    CommandExecutionApproval = 0,
    FileChangeApproval = 1,
    ToolApproval = 2,
    McpServerElicitation = 3,
    PermissionApproval = 4,
    ToolUserInput = 5,
    DynamicToolCall = 6,
}

/// <summary>
/// 审批响应 southbound 负载形状。
/// Southbound payload shape used for approval responses.
/// </summary>
public enum ProviderApprovalResponsePayloadKind
{
    Standard = 0,
    Legacy = 1,
    McpServerElicitation = 2,
}

/// <summary>
/// provider 服务端请求路由结果。
/// Route result for a provider server request.
/// </summary>
public sealed record ProviderServerRequestRoute(
    ProviderServerRequestKind Kind,
    string Method,
    string? PendingRequestKind,
    ProviderApprovalResponsePayloadKind ApprovalResponsePayloadKind = ProviderApprovalResponsePayloadKind.Standard);

/// <summary>
/// provider 服务端请求对应的挂起交互类型常量。
/// Pending interactive kind constants used by provider server requests.
/// </summary>
public static class ProviderServerRequestPendingKinds
{
    /// <summary>
    /// 审批请求。
    /// Approval request.
    /// </summary>
    public const string ApprovalRequested = "approval_requested";

    /// <summary>
    /// 权限请求。
    /// Permission request.
    /// </summary>
    public const string PermissionRequested = "permission_requested";

    /// <summary>
    /// 用户输入请求。
    /// User-input request.
    /// </summary>
    public const string UserInput = "request_user_input";
}
