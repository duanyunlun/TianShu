using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;

namespace TianShu.Cli.Interaction.Orchestration;

/// <summary>
/// Rebuilds stream events for pending interactive requests restored from a thread snapshot.
/// 将线程快照中恢复出的待处理交互请求重新构造为可投递的 stream event。
/// </summary>
internal sealed class PendingInteractiveReplayCoordinator
{
    public IReadOnlyList<ControlPlaneConversationStreamEvent> BuildReplayEvents(
        IReadOnlyList<ControlPlanePendingInteractiveRequest> requests)
    {
        if (requests.Count == 0)
        {
            return [];
        }

        return requests
            .OrderBy(static item => item.RequestId)
            .Select(BuildReplayEvent)
            .Where(static item => item is not null)
            .Cast<ControlPlaneConversationStreamEvent>()
            .ToArray();
    }

    internal static ControlPlaneConversationStreamEvent? BuildReplayEvent(ControlPlanePendingInteractiveRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CallId))
        {
            return null;
        }

        return request.RequestKind switch
        {
            "approval_requested" => new ControlPlaneConversationStreamEvent
            {
                Kind = ControlPlaneConversationStreamEventKind.ApprovalRequested,
                Timestamp = DateTimeOffset.Now,
                ThreadId = string.IsNullOrWhiteSpace(request.ThreadId) ? null : new ThreadId(request.ThreadId),
                TurnId = string.IsNullOrWhiteSpace(request.TurnId) ? null : new TurnId(request.TurnId),
                CallId = new CallId(request.CallId),
                ToolName = request.ToolName,
                ServerName = request.ServerName,
                Text = request.Text,
                Status = request.Status,
                Phase = request.Phase,
                RequiresApproval = request.RequiresApproval ?? true,
                ApprovalKind = request.ApprovalKind,
                AvailableDecisions = request.AvailableDecisions,
                AvailableDecisionOptions = request.AvailableDecisionOptions,
            },
            "permission_requested" => new ControlPlaneConversationStreamEvent
            {
                Kind = ControlPlaneConversationStreamEventKind.PermissionRequested,
                Timestamp = DateTimeOffset.Now,
                ThreadId = string.IsNullOrWhiteSpace(request.ThreadId) ? null : new ThreadId(request.ThreadId),
                TurnId = string.IsNullOrWhiteSpace(request.TurnId) ? null : new TurnId(request.TurnId),
                CallId = new CallId(request.CallId),
                ToolName = request.ToolName,
                Text = request.Text,
                Status = request.Status,
                Phase = request.Phase,
            },
            "request_user_input" => new ControlPlaneConversationStreamEvent
            {
                Kind = ControlPlaneConversationStreamEventKind.UserInputRequested,
                Timestamp = DateTimeOffset.Now,
                ThreadId = string.IsNullOrWhiteSpace(request.ThreadId) ? null : new ThreadId(request.ThreadId),
                TurnId = string.IsNullOrWhiteSpace(request.TurnId) ? null : new TurnId(request.TurnId),
                CallId = new CallId(request.CallId),
                ToolName = request.ToolName,
                Text = request.Text,
                Status = request.Status,
                Phase = request.Phase,
            },
            _ => null,
        };
    }
}
