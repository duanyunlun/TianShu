using System.Collections.Concurrent;
using TianShu.Cli;

namespace TianShu.Cli.Interaction.Orchestration;

/// <summary>
/// Stores pending approval, permission, and user-input requests observed by the CLI chat session.
/// 保存 CLI chat 会话中观察到的审批、权限与用户补录待处理请求。
/// </summary>
internal sealed class PendingInteractiveRequestStore
{
    private readonly ConcurrentDictionary<string, CliPendingApprovalRequestState> approvals = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CliPendingPermissionRequestState> permissions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CliPendingUserInputRequestState> userInputs = new(StringComparer.Ordinal);

    public int ApprovalCount => approvals.Count;

    public int PermissionCount => permissions.Count;

    public int UserInputCount => userInputs.Count;

    public bool IsEmpty => approvals.IsEmpty && permissions.IsEmpty && userInputs.IsEmpty;

    public PendingInteractiveRequestSnapshot Snapshot
        => new(
            approvals.Keys.OrderBy(static item => item, StringComparer.Ordinal).ToArray(),
            permissions.Keys.OrderBy(static item => item, StringComparer.Ordinal).ToArray(),
            userInputs.Keys.OrderBy(static item => item, StringComparer.Ordinal).ToArray(),
            ApprovalCount,
            PermissionCount,
            UserInputCount);

    public void AddApproval(CliPendingApprovalRequestState request)
    {
        ArgumentNullException.ThrowIfNull(request);
        approvals[request.CallId] = request;
    }

    public void AddPermission(CliPendingPermissionRequestState request)
    {
        ArgumentNullException.ThrowIfNull(request);
        permissions[request.CallId] = request;
    }

    public void AddUserInput(CliPendingUserInputRequestState request)
    {
        ArgumentNullException.ThrowIfNull(request);
        userInputs[request.CallId] = request;
    }

    public bool TryGetApproval(string callId, out CliPendingApprovalRequestState? request)
        => approvals.TryGetValue(callId, out request);

    public void RemoveApproval(string callId)
        => approvals.TryRemove(callId, out _);

    public void RemovePermission(string callId)
        => permissions.TryRemove(callId, out _);

    public void RemoveUserInput(string callId)
        => userInputs.TryRemove(callId, out _);

    public void Remove(string callId)
    {
        RemoveApproval(callId);
        RemovePermission(callId);
        RemoveUserInput(callId);
    }

    public void Clear()
    {
        approvals.Clear();
        permissions.Clear();
        userInputs.Clear();
    }

    public void ClearForTurn(string? threadId, string? turnId)
    {
        var normalizedThreadId = Normalize(threadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            return;
        }

        var callIds = approvals.Values
            .Select(static item => (CliPendingInteractiveRequestState)item)
            .Concat(permissions.Values.Select(static item => (CliPendingInteractiveRequestState)item))
            .Concat(userInputs.Values.Select(static item => (CliPendingInteractiveRequestState)item))
            .Where(item => MatchesTurn(item, normalizedThreadId!, turnId))
            .Select(item => item.CallId)
            .Where(static callId => !string.IsNullOrWhiteSpace(callId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var callId in callIds)
        {
            Remove(callId);
        }
    }

    private static bool MatchesTurn(CliPendingInteractiveRequestState pendingRequest, string threadId, string? turnId)
    {
        var eventThreadId = Normalize(pendingRequest.ThreadId);
        if (string.IsNullOrWhiteSpace(eventThreadId)
            || !string.Equals(eventThreadId, threadId, StringComparison.Ordinal))
        {
            return false;
        }

        var normalizedTurnId = Normalize(turnId);
        var eventTurnId = Normalize(pendingRequest.TurnId);
        if (string.IsNullOrWhiteSpace(normalizedTurnId))
        {
            return string.IsNullOrWhiteSpace(eventTurnId);
        }

        return string.IsNullOrWhiteSpace(eventTurnId)
               || string.Equals(eventTurnId, normalizedTurnId, StringComparison.Ordinal);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed record PendingInteractiveRequestSnapshot(
    IReadOnlyList<string> ApprovalCallIds,
    IReadOnlyList<string> PermissionCallIds,
    IReadOnlyList<string> UserInputCallIds,
    int ApprovalCount,
    int PermissionCount,
    int UserInputCount);
