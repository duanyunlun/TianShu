using TianShu.Cli.Interaction.Commands.Governance;
using TianShu.Cli.Interaction.Orchestration;
using TianShu.Contracts.Governance;

namespace TianShu.Cli.Tests;

public sealed class InteractiveGovernanceCommandHandlerTests
{
    [Fact]
    public async Task HandleApprovalAsync_SubmitsResolutionAndClearsPendingApproval()
    {
        var runtime = new CliConsumerFakeRuntime();
        var store = new PendingInteractiveRequestStore();
        var handler = new InteractiveGovernanceCommandHandler();
        var output = new List<(string Text, bool IsError)>();
        store.AddApproval(new CliPendingApprovalRequestState("approval_1", "thread_1", "turn_1", "shell", "exec", ["accept", "acceptForSession"], null));

        await handler.HandleApprovalAsync(
            runtime,
            store,
            "approval_1 looks good",
            ControlPlaneApprovalDecision.ApproveForSession,
            (text, isError) => output.Add((text, isError)),
            CancellationToken.None);

        Assert.Empty(store.Snapshot.ApprovalCallIds);
        var response = Assert.Single(runtime.ApprovalResponses);
        Assert.Equal("approval_1", response.CallId.Value);
        Assert.Equal(ControlPlaneApprovalDecision.ApproveForSession, response.Decision);
        Assert.Equal("looks good", response.Note);
        Assert.Contains(output, static line => !line.IsError && line.Text.Contains("已提交审批响应：approval_1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleApprovalAsync_WhenCallIdMissing_WritesUsageAndDoesNotCallRuntime()
    {
        var runtime = new CliConsumerFakeRuntime();
        var handler = new InteractiveGovernanceCommandHandler();
        var output = new List<(string Text, bool IsError)>();

        await handler.HandleApprovalAsync(
            runtime,
            new PendingInteractiveRequestStore(),
            string.Empty,
            ControlPlaneApprovalDecision.Approve,
            (text, isError) => output.Add((text, isError)),
            CancellationToken.None);

        Assert.Empty(runtime.ApprovalResponses);
        Assert.Contains(output, static line => line.IsError && line.Text.StartsWith("用法：/approve", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleApprovalAsync_WhenRuntimeRejectsResponse_KeepsPendingApproval()
    {
        var runtime = new CliConsumerFakeRuntime
        {
            RespondToApprovalAsyncHandler = static (_, _) => Task.FromResult(false),
        };
        var store = new PendingInteractiveRequestStore();
        var handler = new InteractiveGovernanceCommandHandler();
        var output = new List<(string Text, bool IsError)>();
        store.AddApproval(new CliPendingApprovalRequestState("approval_1", "thread_1", "turn_1", "shell", null, null, null));

        await handler.HandleApprovalAsync(
            runtime,
            store,
            "approval_1",
            ControlPlaneApprovalDecision.Approve,
            (text, isError) => output.Add((text, isError)),
            CancellationToken.None);

        Assert.Equal(["approval_1"], store.Snapshot.ApprovalCallIds);
        Assert.Single(runtime.ApprovalResponses);
        Assert.Contains(output, static line => line.IsError && line.Text == "审批响应失败：approval_1");
    }

    [Fact]
    public async Task HandlePermissionRequestAsync_SubmitsGrantAndClearsPendingPermission()
    {
        var runtime = new CliConsumerFakeRuntime();
        var store = new PendingInteractiveRequestStore();
        var handler = new InteractiveGovernanceCommandHandler();
        var output = new List<(string Text, bool IsError)>();
        store.AddPermission(new CliPendingPermissionRequestState("permission_1", "thread_1", "turn_1", "request_permissions"));

        await handler.HandlePermissionRequestAsync(
            runtime,
            store,
            "permission_1 {\"scope\":\"session\",\"permissions\":{\"network\":\"on\"}}",
            (text, isError) => output.Add((text, isError)),
            CancellationToken.None);

        Assert.Empty(store.Snapshot.PermissionCallIds);
        var response = Assert.Single(runtime.PermissionResponses);
        Assert.Equal("permission_1", response.CallId.Value);
        Assert.Equal(ControlPlanePermissionScope.Session, response.Scope);
        Assert.True(response.Permissions.ContainsKey("network"));
        Assert.Contains(output, static line => !line.IsError && line.Text == "已提交权限响应：permission_1");
    }

    [Fact]
    public async Task HandlePermissionRequestAsync_WhenJsonInvalid_WritesErrorAndDoesNotCallRuntime()
    {
        var runtime = new CliConsumerFakeRuntime();
        var handler = new InteractiveGovernanceCommandHandler();
        var output = new List<(string Text, bool IsError)>();

        await handler.HandlePermissionRequestAsync(
            runtime,
            new PendingInteractiveRequestStore(),
            "permission_1 {",
            (text, isError) => output.Add((text, isError)),
            CancellationToken.None);

        Assert.Empty(runtime.PermissionResponses);
        Assert.Contains(output, static line => line.IsError && line.Text.StartsWith("解析权限响应 JSON 失败：", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleUserInputAsync_SubmitsAnswersAndClearsPendingUserInput()
    {
        var runtime = new CliConsumerFakeRuntime();
        var store = new PendingInteractiveRequestStore();
        var handler = new InteractiveGovernanceCommandHandler();
        var output = new List<(string Text, bool IsError)>();
        store.AddUserInput(new CliPendingUserInputRequestState("input_1", "thread_1", "turn_1", null));

        await handler.HandleUserInputAsync(
            runtime,
            store,
            "input_1 {\"choice\":\"A\"}",
            (text, isError) => output.Add((text, isError)),
            CancellationToken.None);

        Assert.Empty(store.Snapshot.UserInputCallIds);
        var response = Assert.Single(runtime.UserInputResponses);
        Assert.Equal("input_1", response.CallId.Value);
        Assert.True(response.Answers.ContainsKey("choice"));
        Assert.Contains(output, static line => !line.IsError && line.Text == "已提交补录答案：input_1");
    }

    [Fact]
    public async Task HandleUserInputAsync_WhenJsonInvalid_WritesErrorAndDoesNotCallRuntime()
    {
        var runtime = new CliConsumerFakeRuntime();
        var handler = new InteractiveGovernanceCommandHandler();
        var output = new List<(string Text, bool IsError)>();

        await handler.HandleUserInputAsync(
            runtime,
            new PendingInteractiveRequestStore(),
            "input_1 [",
            (text, isError) => output.Add((text, isError)),
            CancellationToken.None);

        Assert.Empty(runtime.UserInputResponses);
        Assert.Contains(output, static line => line.IsError && line.Text.StartsWith("解析补录 JSON 失败：", StringComparison.Ordinal));
    }
}
