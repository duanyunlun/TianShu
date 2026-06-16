using TianShu.Cli.Interaction.Orchestration;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Governance;
using TianShu.Contracts.Primitives;

namespace TianShu.Cli.Tests;

public sealed class PendingInteractiveAutomationHandlerTests
{
    [Fact]
    public void HandleApprovalRequested_WhenApproveAllDisabled_RegistersPendingApproval()
    {
        var handler = new PendingInteractiveAutomationHandler();
        var runtime = new CliConsumerFakeRuntime();
        var store = new PendingInteractiveRequestStore();
        var output = new List<(string Text, bool IsError)>();

        handler.HandleApprovalRequested(
            runtime,
            store,
            ApprovalEvent("approval_1"),
            approveAll: false,
            ControlPlaneApprovalDecision.Approve,
            (text, isError) => output.Add((text, isError)),
            CancellationToken.None);

        Assert.Equal(["approval_1"], store.Snapshot.ApprovalCallIds);
        Assert.Empty(runtime.ApprovalResponses);
        Assert.Contains(output, static line => !line.IsError && line.Text.Contains("审批请求：callId=approval_1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleApprovalRequested_WhenApproveAllEnabledAndRuntimeAccepts_ClearsPendingApproval()
    {
        var handler = new PendingInteractiveAutomationHandler();
        var runtime = new CliConsumerFakeRuntime();
        var store = new PendingInteractiveRequestStore();
        var output = new List<(string Text, bool IsError)>();

        handler.HandleApprovalRequested(
            runtime,
            store,
            ApprovalEvent("approval_1"),
            approveAll: true,
            ControlPlaneApprovalDecision.ApproveForSession,
            (text, isError) => output.Add((text, isError)),
            CancellationToken.None);

        await WaitUntilAsync(() => runtime.ApprovalResponses.Count == 1);

        Assert.Empty(store.Snapshot.ApprovalCallIds);
        Assert.Equal(ControlPlaneApprovalDecision.ApproveForSession, runtime.ApprovalResponses[0].Decision);
        Assert.Contains(output, static line => !line.IsError && line.Text.Contains("已自动提交审批响应：approval_1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleApprovalRequested_WhenRuntimeRejectsAutoApproval_KeepsPendingApproval()
    {
        var handler = new PendingInteractiveAutomationHandler();
        var runtime = new CliConsumerFakeRuntime
        {
            RespondToApprovalAsyncHandler = static (_, _) => Task.FromResult(false),
        };
        var store = new PendingInteractiveRequestStore();
        var output = new List<(string Text, bool IsError)>();

        handler.HandleApprovalRequested(
            runtime,
            store,
            ApprovalEvent("approval_1"),
            approveAll: true,
            ControlPlaneApprovalDecision.Approve,
            (text, isError) => output.Add((text, isError)),
            CancellationToken.None);

        await WaitUntilAsync(() =>
            runtime.ApprovalResponses.Count == 1 &&
            output.Any(static line => line.IsError && line.Text == "自动提交审批响应失败：approval_1"));

        Assert.Equal(["approval_1"], store.Snapshot.ApprovalCallIds);
        Assert.Contains(output, static line => line.IsError && line.Text == "自动提交审批响应失败：approval_1");
    }

    [Fact]
    public async Task HandlePermissionRequested_WhenScriptMatches_SubmitsAndClearsPendingPermission()
    {
        using var script = TempJsonFile("""{"requests":{"permission_1":{"scope":"session","permissions":{"network":"on"}}}}""");
        var handler = new PendingInteractiveAutomationHandler();
        var runtime = new CliConsumerFakeRuntime();
        var store = new PendingInteractiveRequestStore();
        var output = new List<(string Text, bool IsError)>();

        handler.HandlePermissionRequested(
            runtime,
            store,
            PermissionEvent("permission_1"),
            ProbePermissionRequestScript.Load(script.Path),
            (text, isError) => output.Add((text, isError)),
            CancellationToken.None);

        await WaitUntilAsync(() => runtime.PermissionResponses.Count == 1);

        Assert.Empty(store.Snapshot.PermissionCallIds);
        Assert.Equal(ControlPlanePermissionScope.Session, runtime.PermissionResponses[0].Scope);
        Assert.Contains(output, static line => !line.IsError && line.Text == "已自动提交权限响应：permission_1");
    }

    [Fact]
    public void HandlePermissionRequested_WhenScriptDoesNotMatch_KeepsPendingPermissionAndWritesError()
    {
        using var script = TempJsonFile("""{"requests":{"other":{"permissions":{"network":"on"}}}}""");
        var handler = new PendingInteractiveAutomationHandler();
        var runtime = new CliConsumerFakeRuntime();
        var store = new PendingInteractiveRequestStore();
        var output = new List<(string Text, bool IsError)>();

        handler.HandlePermissionRequested(
            runtime,
            store,
            PermissionEvent("permission_1"),
            ProbePermissionRequestScript.Load(script.Path),
            (text, isError) => output.Add((text, isError)),
            CancellationToken.None);

        Assert.Equal(["permission_1"], store.Snapshot.PermissionCallIds);
        Assert.Empty(runtime.PermissionResponses);
        Assert.Contains(output, static line => line.IsError && line.Text == "权限响应脚本未匹配到结果：permission_1");
    }

    [Fact]
    public async Task HandleUserInputRequested_WhenScriptMatches_SubmitsAndClearsPendingUserInput()
    {
        using var script = TempJsonFile("""{"requests":{"input_1":{"choice":"A"}}}""");
        var handler = new PendingInteractiveAutomationHandler();
        var runtime = new CliConsumerFakeRuntime();
        var store = new PendingInteractiveRequestStore();
        var output = new List<(string Text, bool IsError)>();

        handler.HandleUserInputRequested(
            runtime,
            store,
            UserInputEvent("input_1"),
            ProbeUserInputScript.Load(script.Path),
            (text, isError) => output.Add((text, isError)),
            CancellationToken.None);

        await WaitUntilAsync(() => runtime.UserInputResponses.Count == 1);

        Assert.Empty(store.Snapshot.UserInputCallIds);
        Assert.True(runtime.UserInputResponses[0].Answers.ContainsKey("choice"));
        Assert.Contains(output, static line => !line.IsError && line.Text == "已自动提交补录答案：input_1");
    }

    [Fact]
    public void HandleUserInputRequested_WhenScriptDoesNotMatch_KeepsPendingUserInputAndWritesError()
    {
        using var script = TempJsonFile("""{"requests":{"other":{"choice":"A"}}}""");
        var handler = new PendingInteractiveAutomationHandler();
        var runtime = new CliConsumerFakeRuntime();
        var store = new PendingInteractiveRequestStore();
        var output = new List<(string Text, bool IsError)>();

        handler.HandleUserInputRequested(
            runtime,
            store,
            UserInputEvent("input_1"),
            ProbeUserInputScript.Load(script.Path),
            (text, isError) => output.Add((text, isError)),
            CancellationToken.None);

        Assert.Equal(["input_1"], store.Snapshot.UserInputCallIds);
        Assert.Empty(runtime.UserInputResponses);
        Assert.Contains(output, static line => line.IsError && line.Text == "用户补录脚本未匹配到答案：input_1");
    }

    private static ControlPlaneConversationStreamEvent ApprovalEvent(string callId)
        => Event(ControlPlaneConversationStreamEventKind.ApprovalRequested, callId, "shell");

    private static ControlPlaneConversationStreamEvent PermissionEvent(string callId)
        => Event(ControlPlaneConversationStreamEventKind.PermissionRequested, callId, "request_permissions");

    private static ControlPlaneConversationStreamEvent UserInputEvent(string callId)
        => Event(ControlPlaneConversationStreamEventKind.UserInputRequested, callId, null);

    private static ControlPlaneConversationStreamEvent Event(ControlPlaneConversationStreamEventKind kind, string callId, string? toolName)
        => new()
        {
            Kind = kind,
            ThreadId = new ThreadId("thread_1"),
            TurnId = new TurnId("turn_1"),
            CallId = new CallId(callId),
            ToolName = toolName,
        };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.True(condition(), "Expected asynchronous automation to complete.");
    }

    private static TempJsonFileHandle TempJsonFile(string content)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tianshu-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return new TempJsonFileHandle(path);
    }

    private sealed class TempJsonFileHandle(string path) : IDisposable
    {
        public string Path { get; } = path;

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }
}
