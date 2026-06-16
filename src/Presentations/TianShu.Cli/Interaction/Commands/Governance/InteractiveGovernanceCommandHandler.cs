using System.Text.Json;
using TianShu.Cli.Interaction.Orchestration;
using TianShu.ControlPlane;
using TianShu.Contracts.Governance;
using TianShu.Contracts.Primitives;
using TianShu.Execution.Runtime;

namespace TianShu.Cli.Interaction.Commands.Governance;

/// <summary>
/// Handles interactive governance response commands emitted from the chat slash command surface.
/// 处理 chat slash command 面中的审批、权限与用户补录响应命令。
/// </summary>
internal sealed class InteractiveGovernanceCommandHandler
{
    public async Task HandleApprovalAsync(
        IExecutionRuntime runtime,
        PendingInteractiveRequestStore pendingRequests,
        string rest,
        ControlPlaneApprovalDecision preferredDecision,
        Action<string, bool> writeLine,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(pendingRequests);
        ArgumentNullException.ThrowIfNull(writeLine);

        var callId = ReadFirstToken(rest, out var note);
        if (string.IsNullOrWhiteSpace(callId))
        {
            writeLine("用法：/approve|/approve-session|/approve-always|/reject|/cancel-approval <callId> [note]", true);
            return;
        }

        pendingRequests.TryGetApproval(callId, out var pendingApproval);
        var response = CliApprovalResponseResolver.BuildResolution(
            callId,
            pendingApproval,
            preferredDecision,
            Normalize(note),
            out var resolvedDecision);
        var responded = await TianShuControlPlaneClientFactory.Create(runtime).Governance.ResolveApprovalAsync(response, cancellationToken).ConfigureAwait(false);
        if (!responded)
        {
            writeLine($"审批响应失败：{callId}", true);
            return;
        }

        pendingRequests.RemoveApproval(callId);
        writeLine($"已提交审批响应：{callId} ({CliApprovalResponseResolver.ToDisplayToken(resolvedDecision)})", false);
    }

    public async Task HandlePermissionRequestAsync(
        IExecutionRuntime runtime,
        PendingInteractiveRequestStore pendingRequests,
        string rest,
        Action<string, bool> writeLine,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(pendingRequests);
        ArgumentNullException.ThrowIfNull(writeLine);

        var callId = ReadFirstToken(rest, out var json);
        if (string.IsNullOrWhiteSpace(callId) || string.IsNullOrWhiteSpace(json))
        {
            writeLine("用法：/permissions <callId> <json-object>", true);
            return;
        }

        ControlPlanePermissionGrant response;
        try
        {
            response = CliGovernanceEnvelopeFactory.Normalize(
                ProbePermissionRequestScript.ParseJson(json) with { CallId = new CallId(callId) });
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            writeLine($"解析权限响应 JSON 失败：{ex.Message}", true);
            return;
        }

        var responded = await TianShuControlPlaneClientFactory.Create(runtime).Governance.ResolvePermissionRequestAsync(response, cancellationToken).ConfigureAwait(false);
        if (!responded)
        {
            writeLine($"权限响应失败：{callId}", true);
            return;
        }

        pendingRequests.RemovePermission(callId);
        writeLine($"已提交权限响应：{callId}", false);
    }

    public async Task HandleUserInputAsync(
        IExecutionRuntime runtime,
        PendingInteractiveRequestStore pendingRequests,
        string rest,
        Action<string, bool> writeLine,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(pendingRequests);
        ArgumentNullException.ThrowIfNull(writeLine);

        var callId = ReadFirstToken(rest, out var json);
        if (string.IsNullOrWhiteSpace(callId) || string.IsNullOrWhiteSpace(json))
        {
            writeLine("用法：/input <callId> <json-object>", true);
            return;
        }

        ControlPlaneUserInputSubmission answers;
        try
        {
            answers = CliGovernanceEnvelopeFactory.Normalize(
                ProbeUserInputScript.ParseJson(json) with { CallId = new CallId(callId) });
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            writeLine($"解析补录 JSON 失败：{ex.Message}", true);
            return;
        }

        var responded = await TianShuControlPlaneClientFactory.Create(runtime).Governance.SubmitUserInputAsync(answers, cancellationToken).ConfigureAwait(false);
        if (!responded)
        {
            writeLine($"补录响应失败：{callId}", true);
            return;
        }

        pendingRequests.RemoveUserInput(callId);
        writeLine($"已提交补录答案：{callId}", false);
    }

    private static string ReadFirstToken(string text, out string remainder)
    {
        text = text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            remainder = string.Empty;
            return string.Empty;
        }

        var index = text.IndexOf(' ', StringComparison.Ordinal);
        if (index < 0)
        {
            remainder = string.Empty;
            return text;
        }

        remainder = text[(index + 1)..].Trim();
        return text[..index];
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
