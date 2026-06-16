using TianShu.Provider.Abstractions;

namespace TianShu.Provider.OpenAI;

/// <summary>
/// OpenAI provider 服务端请求路由器。
/// Provider server-request router for the OpenAI provider.
/// </summary>
public sealed class OpenAiProviderServerRequestRouter : IProviderServerRequestRouter
{
    /// <inheritdoc />
    public ProviderServerRequestRoute? Route(string? method)
    {
        var normalized = Normalize(method);
        if (normalized is null)
        {
            return null;
        }

        return normalized switch
        {
            "item/commandexecution/requestapproval" => new ProviderServerRequestRoute(
                ProviderServerRequestKind.CommandExecutionApproval,
                "item/commandExecution/requestApproval",
                ProviderServerRequestPendingKinds.ApprovalRequested),
            "execcommandapproval" => new ProviderServerRequestRoute(
                ProviderServerRequestKind.CommandExecutionApproval,
                "execCommandApproval",
                ProviderServerRequestPendingKinds.ApprovalRequested,
                ProviderApprovalResponsePayloadKind.Legacy),
            "item/filechange/requestapproval" => new ProviderServerRequestRoute(
                ProviderServerRequestKind.FileChangeApproval,
                "item/fileChange/requestApproval",
                ProviderServerRequestPendingKinds.ApprovalRequested),
            "applypatchapproval" => new ProviderServerRequestRoute(
                ProviderServerRequestKind.FileChangeApproval,
                "applyPatchApproval",
                ProviderServerRequestPendingKinds.ApprovalRequested,
                ProviderApprovalResponsePayloadKind.Legacy),
            "item/tool/requestapproval" => new ProviderServerRequestRoute(
                ProviderServerRequestKind.ToolApproval,
                "item/tool/requestApproval",
                ProviderServerRequestPendingKinds.ApprovalRequested),
            "mcpserver/elicitation/request" => new ProviderServerRequestRoute(
                ProviderServerRequestKind.McpServerElicitation,
                "mcpServer/elicitation/request",
                ProviderServerRequestPendingKinds.ApprovalRequested,
                ProviderApprovalResponsePayloadKind.McpServerElicitation),
            "item/permissions/requestapproval" => new ProviderServerRequestRoute(
                ProviderServerRequestKind.PermissionApproval,
                "item/permissions/requestApproval",
                ProviderServerRequestPendingKinds.PermissionRequested),
            "item/tool/requestuserinput" => new ProviderServerRequestRoute(
                ProviderServerRequestKind.ToolUserInput,
                "item/tool/requestUserInput",
                ProviderServerRequestPendingKinds.UserInput),
            "item/tool/call" => new ProviderServerRequestRoute(
                ProviderServerRequestKind.DynamicToolCall,
                "item/tool/call",
                null),
            _ => null,
        };
    }

    /// <inheritdoc />
    public string? ResolvePendingRequestKind(string? requestMethod)
        => Route(requestMethod)?.PendingRequestKind;

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized.ToLowerInvariant();
    }
}
