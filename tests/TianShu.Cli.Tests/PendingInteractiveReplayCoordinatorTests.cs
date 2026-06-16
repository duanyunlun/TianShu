using TianShu.Cli.Interaction.Orchestration;
using TianShu.Contracts.Conversations;

namespace TianShu.Cli.Tests;

public sealed class PendingInteractiveReplayCoordinatorTests
{
    [Fact]
    public void BuildReplayEvents_MapsApprovalPermissionAndUserInputRequests()
    {
        var events = new PendingInteractiveReplayCoordinator().BuildReplayEvents(
        [
            new ControlPlanePendingInteractiveRequest
            {
                RequestId = 3,
                RequestKind = "request_user_input",
                ThreadId = "thread-replay-001",
                TurnId = "turn-replay-001",
                CallId = "input-replay-001",
                ToolName = "request_user_input",
                Text = "请选择",
                Status = "pending",
                Phase = "request_user_input",
            },
            new ControlPlanePendingInteractiveRequest
            {
                RequestId = 1,
                RequestKind = "approval_requested",
                ThreadId = "thread-replay-001",
                TurnId = "turn-replay-001",
                CallId = "approval-replay-001",
                ToolName = "shell_command",
                ServerName = "local",
                Text = "需要审批",
                Status = "pending",
                Phase = "request_approval",
                ApprovalKind = "shell_command",
            },
            new ControlPlanePendingInteractiveRequest
            {
                RequestId = 2,
                RequestKind = "permission_requested",
                ThreadId = "thread-replay-001",
                TurnId = "turn-replay-001",
                CallId = "permission-replay-001",
                ToolName = "request_permissions",
                Text = "需要权限",
                Status = "pending",
                Phase = "request_permission",
            },
        ]);

        Assert.Collection(
            events,
            approval =>
            {
                Assert.Equal(ControlPlaneConversationStreamEventKind.ApprovalRequested, approval.Kind);
                Assert.Equal("approval-replay-001", approval.CallId?.Value);
                Assert.Equal("thread-replay-001", approval.ThreadId?.Value);
                Assert.Equal("turn-replay-001", approval.TurnId?.Value);
                Assert.Equal("shell_command", approval.ToolName);
                Assert.Equal("local", approval.ServerName);
                Assert.True(approval.RequiresApproval);
                Assert.Equal("shell_command", approval.ApprovalKind);
            },
            permission =>
            {
                Assert.Equal(ControlPlaneConversationStreamEventKind.PermissionRequested, permission.Kind);
                Assert.Equal("permission-replay-001", permission.CallId?.Value);
                Assert.Equal("request_permissions", permission.ToolName);
                Assert.Equal("request_permission", permission.Phase);
            },
            userInput =>
            {
                Assert.Equal(ControlPlaneConversationStreamEventKind.UserInputRequested, userInput.Kind);
                Assert.Equal("input-replay-001", userInput.CallId?.Value);
                Assert.Equal("request_user_input", userInput.ToolName);
                Assert.Equal("请选择", userInput.Text);
            });
    }

    [Fact]
    public void BuildReplayEvents_SkipsMissingCallIdAndUnknownKind()
    {
        var events = new PendingInteractiveReplayCoordinator().BuildReplayEvents(
        [
            new ControlPlanePendingInteractiveRequest
            {
                RequestId = 1,
                RequestKind = "approval_requested",
                CallId = "",
            },
            new ControlPlanePendingInteractiveRequest
            {
                RequestId = 2,
                RequestKind = "unknown",
                CallId = "unknown-replay-001",
            },
            new ControlPlanePendingInteractiveRequest
            {
                RequestId = 3,
                RequestKind = "permission_requested",
                CallId = "permission-replay-002",
            },
        ]);

        var replayEvent = Assert.Single(events);
        Assert.Equal(ControlPlaneConversationStreamEventKind.PermissionRequested, replayEvent.Kind);
        Assert.Equal("permission-replay-002", replayEvent.CallId?.Value);
    }
}
