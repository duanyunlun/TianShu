using TianShu.ControlPlane;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Governance;
using TianShu.Execution.Runtime;

namespace TianShu.Cli.Interaction.Orchestration;

/// <summary>
/// Handles CLI-side pending interactive request registration and scripted automatic responses.
/// 处理 CLI 侧待处理交互请求登记与脚本化自动响应。
/// </summary>
internal sealed class PendingInteractiveAutomationHandler
{
    public void HandleApprovalRequested(
        IExecutionRuntime runtime,
        PendingInteractiveRequestStore pendingRequests,
        ControlPlaneConversationStreamEvent streamEvent,
        bool approveAll,
        ControlPlaneApprovalDecision approvalDecision,
        Action<string, bool> writeLine,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(pendingRequests);
        ArgumentNullException.ThrowIfNull(streamEvent);
        ArgumentNullException.ThrowIfNull(writeLine);

        var pendingApproval = CliInteractiveStateConverters.ToPendingApprovalRequestState(streamEvent);
        if (pendingApproval is null)
        {
            return;
        }

        pendingRequests.AddApproval(pendingApproval);
        writeLine($"审批请求：callId={pendingApproval.CallId} tool={pendingApproval.ToolName ?? "<unknown>"}。使用 /approve、/approve-session、/approve-always、/reject 或 /cancel-approval。", false);

        if (!approveAll)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var response = CliApprovalResponseResolver.BuildResolution(
                    pendingApproval.CallId,
                    pendingApproval,
                    approvalDecision,
                    "CLI 自动审批",
                    out var resolvedDecision);
                var responded = await TianShuControlPlaneClientFactory.Create(runtime).Governance.ResolveApprovalAsync(response, cancellationToken).ConfigureAwait(false);
                if (responded)
                {
                    pendingRequests.RemoveApproval(pendingApproval.CallId);
                    writeLine($"已自动提交审批响应：{pendingApproval.CallId} ({CliApprovalResponseResolver.ToDisplayToken(resolvedDecision)})", false);
                }
                else
                {
                    writeLine($"自动提交审批响应失败：{pendingApproval.CallId}", true);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                writeLine($"自动提交审批响应失败：{pendingApproval.CallId} - {ex.Message}", true);
            }
        }, cancellationToken);
    }

    public void HandlePermissionRequested(
        IExecutionRuntime runtime,
        PendingInteractiveRequestStore pendingRequests,
        ControlPlaneConversationStreamEvent streamEvent,
        ProbePermissionRequestScript? permissionScript,
        Action<string, bool> writeLine,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(pendingRequests);
        ArgumentNullException.ThrowIfNull(streamEvent);
        ArgumentNullException.ThrowIfNull(writeLine);

        var pendingPermission = CliInteractiveStateConverters.ToPendingPermissionRequestState(streamEvent);
        if (pendingPermission is null)
        {
            return;
        }

        pendingRequests.AddPermission(pendingPermission);
        writeLine($"权限申请：callId={pendingPermission.CallId} tool={pendingPermission.ToolName ?? "<unknown>"}。使用 /permissions <callId> <json> 回复。", false);

        if (permissionScript is null)
        {
            return;
        }

        if (!permissionScript.TryResolveResponse(pendingPermission.CallId, out var response))
        {
            writeLine($"权限响应脚本未匹配到结果：{pendingPermission.CallId}", true);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var responded = await TianShuControlPlaneClientFactory.Create(runtime).Governance.ResolvePermissionRequestAsync(response, cancellationToken).ConfigureAwait(false);
                if (responded)
                {
                    pendingRequests.RemovePermission(pendingPermission.CallId);
                    writeLine($"已自动提交权限响应：{pendingPermission.CallId}", false);
                }
                else
                {
                    writeLine($"自动提交权限响应失败：{pendingPermission.CallId}", true);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                writeLine($"自动提交权限响应失败：{pendingPermission.CallId} - {ex.Message}", true);
            }
        }, cancellationToken);
    }

    public void HandleUserInputRequested(
        IExecutionRuntime runtime,
        PendingInteractiveRequestStore pendingRequests,
        ControlPlaneConversationStreamEvent streamEvent,
        ProbeUserInputScript? userInputScript,
        Action<string, bool> writeLine,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(pendingRequests);
        ArgumentNullException.ThrowIfNull(streamEvent);
        ArgumentNullException.ThrowIfNull(writeLine);

        var pendingUserInput = CliInteractiveStateConverters.ToPendingUserInputRequestState(streamEvent);
        if (pendingUserInput is null)
        {
            return;
        }

        pendingRequests.AddUserInput(pendingUserInput);
        writeLine($"用户补录请求：callId={pendingUserInput.CallId}。使用 /input <callId> <json> 回复。", false);

        if (userInputScript is null)
        {
            return;
        }

        if (!userInputScript.TryResolveAnswers(pendingUserInput.CallId, out var answers))
        {
            writeLine($"用户补录脚本未匹配到答案：{pendingUserInput.CallId}", true);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var responded = await TianShuControlPlaneClientFactory.Create(runtime).Governance.SubmitUserInputAsync(answers, cancellationToken).ConfigureAwait(false);
                if (responded)
                {
                    pendingRequests.RemoveUserInput(pendingUserInput.CallId);
                    writeLine($"已自动提交补录答案：{pendingUserInput.CallId}", false);
                }
                else
                {
                    writeLine($"自动提交补录答案失败：{pendingUserInput.CallId}", true);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                writeLine($"自动提交补录答案失败：{pendingUserInput.CallId} - {ex.Message}", true);
            }
        }, cancellationToken);
    }
}
