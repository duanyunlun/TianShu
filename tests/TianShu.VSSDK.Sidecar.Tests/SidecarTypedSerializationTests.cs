using System.Reflection;
using System.Text.Json;
using TianShu.Contracts.Agents;
using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Catalog;
using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Environment;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Governance;
using TianShu.Contracts.Identity;
using TianShu.Contracts.Interactions;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Projections;
using TianShu.Contracts.Sessions;
using TianShu.Contracts.Workflows;
using ApprovalDecisionOptionPayload = TianShu.VSSDK.Sidecar.SidecarApprovalDecisionOptionPayload;
using ApprovalMetadataFieldPayload = TianShu.VSSDK.Sidecar.SidecarApprovalMetadataFieldPayload;
using ApprovalRequestPayload = TianShu.VSSDK.Sidecar.SidecarApprovalRequestPayload;
using ControlPlaneJobItemDetails = TianShu.Contracts.Workflows.ControlPlaneJobItemDetails;
using ControlPlaneJobOperationResult = TianShu.Contracts.Workflows.ControlPlaneJobOperationResult;
using ExecPolicyAmendmentPayload = TianShu.VSSDK.Sidecar.SidecarExecPolicyAmendmentPayload;
using NetworkPolicyAmendmentPayload = TianShu.VSSDK.Sidecar.SidecarNetworkPolicyAmendmentPayload;
using TianShu.Execution.Runtime;
using TianShu.Execution.Runtime.Events;
using PermissionFieldPayload = TianShu.VSSDK.Sidecar.SidecarPermissionFieldPayload;
using PermissionRequestPayload = TianShu.VSSDK.Sidecar.SidecarPermissionRequestPayload;
using Task = System.Threading.Tasks.Task;
using UserInputOptionPayload = TianShu.VSSDK.Sidecar.SidecarUserInputOptionPayload;
using UserInputQuestionPayload = TianShu.VSSDK.Sidecar.SidecarUserInputQuestionPayload;
using UserInputRequestPayload = TianShu.VSSDK.Sidecar.SidecarUserInputRequestPayload;

namespace TianShu.VSSDK.Sidecar.Tests;

public sealed class SidecarTypedSerializationTests
{
    private static readonly Assembly SidecarAssembly = ReflectionTestHelper.LoadRequiredAssembly("TianShu.VSSDK.Sidecar");
    private static readonly JsonSerializerOptions StructuredJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task RelayRuntimeEventAsync_WhenReasoningEventArrives_WritesTypedPayloadAndDiagnostics()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var streamEvent = CreateStreamEvent(
            ControlPlaneConversationStreamEventKind.ReasoningDelta,
            threadId: "thread-1",
            turnId: "turn-1",
            text: "先检查日志，再决定是否修改。",
            status: "agentMessage",
            message: null,
            payloadKind: ControlPlaneConversationStreamPayloadKind.Reasoning,
            payload: new ReasoningEventPayload(
                "msg-1",
                "agentMessage",
                "commentary",
                "先检查日志，再决定是否修改。",
                "item/agentMessage/delta",
                SummaryIndex: 0),
            dataJson: """{"delta":"先检查日志，再决定是否修改。"}""",
            rawJson: """{"method":"item/agentMessage/delta"}""");
        streamEvent = streamEvent with
        {
            ItemId = "msg-1",
            Phase = "commentary",
            SourceMethod = "item/agentMessage/delta",
        };

        var task = ReflectionTestHelper.InvokeMethod(host!, "RelayRuntimeEventAsync", streamEvent);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        var lines = output
            .ToString()
            .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        var line = Assert.Single(lines);

        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        Assert.Equal("event", root.GetProperty("messageType").GetString());
        Assert.Equal("reasoning_delta", root.GetProperty("eventType").GetString());
        Assert.Equal("thread-1", root.GetProperty("threadId").GetString());
        Assert.Equal("turn-1", root.GetProperty("turnId").GetString());

        var data = root.GetProperty("data");
        Assert.Equal("ReasoningDelta", data.GetProperty("kind").GetString());
        Assert.Equal("msg-1", data.GetProperty("itemId").GetString());

        var reasoning = data.GetProperty("reasoning");
        Assert.Equal("commentary", reasoning.GetProperty("phase").GetString());
        Assert.Equal("先检查日志，再决定是否修改。", reasoning.GetProperty("text").GetString());
        Assert.Equal("item/agentMessage/delta", reasoning.GetProperty("sourceMethod").GetString());
        Assert.Equal(0, reasoning.GetProperty("summaryIndex").GetInt64());

        var diagnostics = data.GetProperty("diagnostics");
        Assert.Equal(streamEvent.Diagnostics?.DataJson, diagnostics.GetProperty("dataJson").GetString());
        Assert.Equal(streamEvent.Diagnostics?.RawJson, diagnostics.GetProperty("rawJson").GetString());
    }

    [Fact]
    public async Task RelayRuntimeEventAsync_WhenHookStartedEventArrives_WritesTypedHookPayload()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var streamEvent = CreateStreamEvent(
            ControlPlaneConversationStreamEventKind.HookStarted,
            threadId: "thread-hook-1",
            turnId: "turn-hook-1",
            text: "D:/Work/TianShu/.tianshu/hooks/session-start.ps1",
            status: "running",
            payloadKind: ControlPlaneConversationStreamPayloadKind.HookRun,
            payload: new HookRunPayload(
                "hook-run-1",
                "sessionStart",
                "command",
                "sync",
                "thread",
                "D:/Work/TianShu/.tianshu/hooks/session-start.ps1",
                0,
                "running",
                "执行中",
                1743300000,
                null,
                null,
                [new HookOutputEntryPayload("context", "准备环境")]));
        streamEvent = streamEvent with { SourceMethod = "hook/started" };

        var task = ReflectionTestHelper.InvokeMethod(host!, "RelayRuntimeEventAsync", streamEvent);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        var lines = output
            .ToString()
            .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        var line = Assert.Single(lines);

        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        Assert.Equal("hook_started", root.GetProperty("eventType").GetString());

        var data = root.GetProperty("data");
        Assert.Equal("HookStarted", data.GetProperty("kind").GetString());
        var hookRun = data.GetProperty("hookRun");
        Assert.Equal("hook-run-1", hookRun.GetProperty("id").GetString());
        Assert.Equal("sessionStart", hookRun.GetProperty("eventName").GetString());
        Assert.Equal("command", hookRun.GetProperty("handlerType").GetString());
        Assert.Equal("context", hookRun.GetProperty("entries")[0].GetProperty("kind").GetString());
    }

    [Fact]
    public async Task RelayRuntimeEventAsync_WhenModelReroutedEventArrives_WritesTypedReroutePayload()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var streamEvent = CreateStreamEvent(
            ControlPlaneConversationStreamEventKind.ModelRerouted,
            threadId: "thread-model-1",
            turnId: "turn-model-1",
            text: "gpt-5.3-codex -> gpt-5.2",
            status: "highRiskCyberActivity",
            payloadKind: ControlPlaneConversationStreamPayloadKind.ModelRerouted,
            payload: new ModelReroutedPayload("gpt-5.3-codex", "gpt-5.2", "highRiskCyberActivity"));
        streamEvent = streamEvent with { SourceMethod = "model/rerouted" };

        var task = ReflectionTestHelper.InvokeMethod(host!, "RelayRuntimeEventAsync", streamEvent);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        var lines = output
            .ToString()
            .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        var line = Assert.Single(lines);

        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        Assert.Equal("model_rerouted", root.GetProperty("eventType").GetString());

        var data = root.GetProperty("data");
        Assert.Equal("ModelRerouted", data.GetProperty("kind").GetString());
        var rerouted = data.GetProperty("modelRerouted");
        Assert.Equal("gpt-5.3-codex", rerouted.GetProperty("fromModel").GetString());
        Assert.Equal("gpt-5.2", rerouted.GetProperty("toModel").GetString());
        Assert.Equal("highRiskCyberActivity", rerouted.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task RelayRuntimeEventAsync_WhenUserMessageCommittedArrives_WritesTypedCommittedPayload()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var streamEvent = CreateStreamEvent(
            ControlPlaneConversationStreamEventKind.UserMessageCommitted,
            threadId: "thread-2",
            turnId: "turn-2",
            text: "请在下一个工具调用后插入这条引导。",
            status: "user_message",
            payloadKind: ControlPlaneConversationStreamPayloadKind.CommittedUserMessage,
            payload: new CommittedUserMessagePayload(
                "msg-user-1",
                "请在下一个工具调用后插入这条引导。",
                0,
                "corr-sidecar-001"));
        streamEvent = streamEvent with
        {
            ItemId = "msg-user-1",
            Phase = "completed",
            SourceMethod = "item/completed",
        };

        var task = ReflectionTestHelper.InvokeMethod(host!, "RelayRuntimeEventAsync", streamEvent);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        var lines = output
            .ToString()
            .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        var line = Assert.Single(lines);

        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        Assert.Equal("user_message_committed", root.GetProperty("eventType").GetString());

        var data = root.GetProperty("data");
        Assert.Equal("UserMessageCommitted", data.GetProperty("kind").GetString());

        var item = data.GetProperty("item");
        Assert.Equal("user_message", item.GetProperty("itemType").GetString());
        Assert.Equal("请在下一个工具调用后插入这条引导。", item.GetProperty("text").GetString());

        var committed = data.GetProperty("committedUserMessage");
        Assert.Equal("msg-user-1", committed.GetProperty("itemId").GetString());
        Assert.Equal("请在下一个工具调用后插入这条引导。", committed.GetProperty("text").GetString());
        Assert.Equal(0, committed.GetProperty("imageCount").GetInt32());
        Assert.Equal("corr-sidecar-001", committed.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task RelayRuntimeEventAsync_WhenTurnCompletedCarriesTurnError_WritesTypedTurnErrorPayload()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var streamEvent = CreateStreamEvent(
            ControlPlaneConversationStreamEventKind.TurnCompleted,
            threadId: "thread-sidecar-turn-error-1",
            turnId: "turn-sidecar-turn-error-1",
            status: "failed",
            message: "turn/completed",
            turnError: new ControlPlaneThreadTurnError
            {
                Message = "工具执行失败：git apply 退出码 1",
                AdditionalDetails = "stderr: patch does not apply",
            });

        var task = ReflectionTestHelper.InvokeMethod(host!, "RelayRuntimeEventAsync", streamEvent);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        var lines = output
            .ToString()
            .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);

        using var eventDocument = JsonDocument.Parse(lines[0]);
        var eventData = eventDocument.RootElement.GetProperty("data");
        Assert.Equal("TurnCompleted", eventData.GetProperty("kind").GetString());
        var turnError = eventData.GetProperty("turnError");
        Assert.Equal("工具执行失败：git apply 退出码 1", turnError.GetProperty("message").GetString());
        Assert.Equal("stderr: patch does not apply", turnError.GetProperty("additionalDetails").GetString());

        using var stateDocument = JsonDocument.Parse(lines[1]);
        Assert.Equal("runtime_state", stateDocument.RootElement.GetProperty("eventType").GetString());
        Assert.Equal("工具执行失败：git apply 退出码 1", stateDocument.RootElement.GetProperty("message").GetString());
        var runtimeStateData = stateDocument.RootElement.GetProperty("data");
        var runtimeStateError = runtimeStateData.GetProperty("turnError");
        Assert.Equal("工具执行失败：git apply 退出码 1", runtimeStateError.GetProperty("message").GetString());
        Assert.Equal("stderr: patch does not apply", runtimeStateError.GetProperty("additionalDetails").GetString());
    }

    [Fact]
    public async Task RelayRuntimeEventAsync_WhenPendingFollowUpUpdatedArrives_WritesTypedPendingFollowUpPayload()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var streamEvent = CreateStreamEvent(
            ControlPlaneConversationStreamEventKind.PendingFollowUpUpdated,
            threadId: "thread-followup-1",
            turnId: "turn-followup-1",
            status: "awaiting_commit",
            message: "pending follow-up awaiting_commit",
            payloadKind: ControlPlaneConversationStreamPayloadKind.PendingFollowUp,
            payload: new PendingFollowUpLifecyclePayload(
                "corr-followup-001",
                "Steer",
                "Steer",
                "awaiting_commit",
                "turn-expected-1",
                "turn-followup-1",
                new PendingFollowUpCompareKeyPayload("请在工具调用后插入这条引导。", 0)));

        var task = ReflectionTestHelper.InvokeMethod(host!, "RelayRuntimeEventAsync", streamEvent);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        var lines = output
            .ToString()
            .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        var line = Assert.Single(lines);

        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        Assert.Equal("pending_followup_updated", root.GetProperty("eventType").GetString());

        var data = root.GetProperty("data");
        Assert.Equal("PendingFollowUpUpdated", data.GetProperty("kind").GetString());

        var pendingFollowUp = data.GetProperty("pendingFollowUp");
        Assert.Equal("corr-followup-001", pendingFollowUp.GetProperty("correlationId").GetString());
        Assert.Equal("Steer", pendingFollowUp.GetProperty("requestedMode").GetString());
        Assert.Equal("Steer", pendingFollowUp.GetProperty("effectiveMode").GetString());
        Assert.Equal("awaiting_commit", pendingFollowUp.GetProperty("lifecycleState").GetString());
        Assert.Equal("turn-expected-1", pendingFollowUp.GetProperty("expectedTurnId").GetString());
        Assert.Equal("turn-followup-1", pendingFollowUp.GetProperty("turnId").GetString());
        var compareKey = pendingFollowUp.GetProperty("compareKey");
        Assert.Equal("请在工具调用后插入这条引导。", compareKey.GetProperty("message").GetString());
        Assert.Equal(0, compareKey.GetProperty("imageCount").GetInt32());

        Assert.False(data.TryGetProperty("pendingInputState", out _));
    }

    [Fact]
    public async Task RelayRuntimeEventAsync_WhenPendingFollowUpUpdatedCarriesPendingInputState_WritesTypedPendingInputStatePayload()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var streamEvent = CreateStreamEvent(
            ControlPlaneConversationStreamEventKind.PendingFollowUpUpdated,
            threadId: "thread-followup-input-1",
            turnId: "turn-followup-input-1",
            status: "awaiting_commit",
            message: "pending input state awaiting_commit",
            payloadKind: ControlPlaneConversationStreamPayloadKind.PendingInputState,
            payload: StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["entries"] = Array.Empty<object>(),
                ["queuedUserMessages"] = Array.Empty<object>(),
                ["pendingSteers"] = new object?[]
                {
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["correlationId"] = "corr-followup-001",
                        ["requestedMode"] = "Steer",
                        ["effectiveMode"] = "Steer",
                        ["lifecycleState"] = "awaiting_commit",
                        ["expectedTurnId"] = "turn-expected-1",
                        ["turnId"] = "turn-followup-input-1",
                        ["pendingBucket"] = "PendingSteer",
                        ["compareKey"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["message"] = "请在工具调用后插入这条引导。",
                            ["imageCount"] = 0,
                        },
                        ["inputs"] = new object?[]
                        {
                            new Dictionary<string, object?>(StringComparer.Ordinal)
                            {
                                ["type"] = "text",
                                ["text"] = "请在工具调用后插入这条引导。",
                            },
                            new Dictionary<string, object?>(StringComparer.Ordinal)
                            {
                                ["type"] = "mention",
                                ["name"] = "workspace",
                                ["path"] = "app://workspace",
                            },
                        },
                    },
                },
                ["interruptRequestPending"] = false,
                ["submitPendingSteersAfterInterrupt"] = true,
            }));

        var task = ReflectionTestHelper.InvokeMethod(host!, "RelayRuntimeEventAsync", streamEvent);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        var line = Assert.Single(
            output.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));

        using var document = JsonDocument.Parse(line);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("PendingFollowUpUpdated", data.GetProperty("kind").GetString());
        Assert.False(data.TryGetProperty("pendingFollowUp", out _));

        var pendingInputState = data.GetProperty("pendingInputState");
        Assert.False(pendingInputState.GetProperty("interruptRequestPending").GetBoolean());
        Assert.True(pendingInputState.GetProperty("submitPendingSteersAfterInterrupt").GetBoolean());
        Assert.Equal("corr-followup-001", pendingInputState.GetProperty("pendingSteers")[0].GetProperty("correlationId").GetString());
        var inputs = pendingInputState.GetProperty("pendingSteers")[0].GetProperty("inputs");
        Assert.Equal(2, inputs.GetArrayLength());
        Assert.Equal("text", inputs[0].GetProperty("type").GetString());
        Assert.Equal("请在工具调用后插入这条引导。", inputs[0].GetProperty("text").GetString());
        Assert.Equal("mention", inputs[1].GetProperty("type").GetString());
        Assert.Equal("workspace", inputs[1].GetProperty("name").GetString());
        Assert.Equal("app://workspace", inputs[1].GetProperty("path").GetString());
        Assert.Empty(pendingInputState.GetProperty("entries").EnumerateArray());
    }

    [Fact]
    public async Task RelayRuntimeEventAsync_WhenAgentJobProgressArrives_WritesTypedPayload()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var streamEvent = CreateStreamEvent(
            ControlPlaneConversationStreamEventKind.AgentJobProgress,
            threadId: "thread-agent-job-sidecar-1",
            turnId: "turn-agent-job-sidecar-1",
            text: "agent_job_progress:{\"job_id\":\"job-sidecar-1\"}",
            payloadKind: ControlPlaneConversationStreamPayloadKind.AgentJobProgress,
            payload: new AgentJobProgressPayload(
                "job-sidecar-1",
                6,
                2,
                1,
                3,
                0,
                15));

        var task = ReflectionTestHelper.InvokeMethod(host!, "RelayRuntimeEventAsync", streamEvent);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        var lines = output
            .ToString()
            .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        var line = Assert.Single(lines);

        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        Assert.Equal("agent_job_progress", root.GetProperty("eventType").GetString());

        var data = root.GetProperty("data");
        Assert.Equal("AgentJobProgress", data.GetProperty("kind").GetString());
        var progress = data.GetProperty("agentJobProgress");
        Assert.Equal("job-sidecar-1", progress.GetProperty("jobId").GetString());
        Assert.Equal(6, progress.GetProperty("totalItems").GetInt32());
        Assert.Equal(3, progress.GetProperty("completedItems").GetInt32());
        Assert.Equal(15, progress.GetProperty("etaSeconds").GetInt32());
    }

    [Fact]
    public async Task RelayRuntimeEventAsync_WhenWindowsSandboxSetupArrives_UsesDedicatedEventType()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var streamEvent = CreateStreamEvent(
            ControlPlaneConversationStreamEventKind.Info,
            text: "Windows Sandbox 初始化完成。",
            payloadKind: ControlPlaneConversationStreamPayloadKind.WindowsSandboxSetup,
            payload: new WindowsSandboxSetupPayload("elevated", true, null));

        var task = ReflectionTestHelper.InvokeMethod(host!, "RelayRuntimeEventAsync", streamEvent);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        var line = Assert.Single(output
            .ToString()
            .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));

        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        Assert.Equal("windows_sandbox_setup", root.GetProperty("eventType").GetString());
        var payload = root.GetProperty("data").GetProperty("windowsSandboxSetup");
        Assert.Equal("elevated", payload.GetProperty("mode").GetString());
        Assert.True(payload.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task RelayRuntimeEventAsync_WhenThreadRealtimeOutputAudioDeltaArrives_WritesTypedPayload()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var streamEvent = CreateStreamEvent(
            ControlPlaneConversationStreamEventKind.ThreadRealtimeOutputAudioDelta,
            threadId: "thread-realtime-sidecar-1",
            payloadKind: ControlPlaneConversationStreamPayloadKind.ThreadRealtimeOutputAudioDelta,
            payload: new ThreadRealtimeOutputAudioDeltaPayload("UklGRg==", 24000, 1, 480));

        var task = ReflectionTestHelper.InvokeMethod(host!, "RelayRuntimeEventAsync", streamEvent);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        var line = Assert.Single(output
            .ToString()
            .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));

        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        Assert.Equal("thread_realtime_output_audio_delta", root.GetProperty("eventType").GetString());
        var payload = root.GetProperty("data").GetProperty("threadRealtimeOutputAudioDelta");
        Assert.Equal("UklGRg==", payload.GetProperty("data").GetString());
        Assert.Equal(24000, payload.GetProperty("sampleRate").GetInt32());
        Assert.Equal(1, payload.GetProperty("numChannels").GetInt32());
        Assert.Equal(480, payload.GetProperty("samplesPerChannel").GetInt32());
    }

    [Fact]
    public async Task RelayRuntimeEventAsync_WhenTurnStartedArrives_WritesDedicatedTurnStartedEvent()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var streamEvent = CreateStreamEvent(
            ControlPlaneConversationStreamEventKind.TurnStarted,
            threadId: "thread-turn-1",
            turnId: "turn-started-1",
            status: "inProgress",
            message: "turn/started",
            rawJson: """{"method":"turn/started"}""");

        var task = ReflectionTestHelper.InvokeMethod(host!, "RelayRuntimeEventAsync", streamEvent);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        var lines = output
            .ToString()
            .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        var line = Assert.Single(lines);

        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        Assert.Equal("event", root.GetProperty("messageType").GetString());
        Assert.Equal("turn_started", root.GetProperty("eventType").GetString());
        Assert.Equal("thread-turn-1", root.GetProperty("threadId").GetString());
        Assert.Equal("turn-started-1", root.GetProperty("turnId").GetString());

        var data = root.GetProperty("data");
        Assert.Equal("TurnStarted", data.GetProperty("kind").GetString());
        Assert.Equal("inProgress", data.GetProperty("status").GetString());
    }

    [Fact]
    public void ResolveConversationInputs_WhenTypedInputsMissing_FallsBackToLegacyMessage()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");

        var result = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "ResolveConversationInputs",
            null,
            "legacy queued text");

        var inputs = Assert.IsAssignableFrom<IReadOnlyList<ControlPlaneInputItem>>(result);
        var textInput = Assert.IsType<ControlPlaneTextInput>(Assert.Single(inputs));
        Assert.Equal("legacy queued text", textInput.Text);
    }

    [Fact]
    public void BuildConversationHistory_WhenLegacyContentUsed_SynthesizesTypedTextInput()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var historyPayloadType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.ConversationHistoryMessagePayload");
        var historyListType = typeof(List<>).MakeGenericType(historyPayloadType);
        var payload = JsonSerializer.Deserialize(
            """[{"role":"user","content":"legacy history text"}]""",
            historyListType,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var result = ReflectionTestHelper.InvokeStaticMethod(hostType, "BuildConversationHistory", payload);

        var history = Assert.IsAssignableFrom<IReadOnlyList<ControlPlaneConversationMessage>>(result);
        var message = Assert.Single(history);
        Assert.Equal(ControlPlaneConversationRole.User, message.Role);
        Assert.Equal("legacy history text", message.Content);

        var textInput = Assert.IsType<ControlPlaneTextInput>(Assert.Single(message.ContentItems));
        Assert.Equal("legacy history text", textInput.Text);
    }

    [Fact]
    public void BuildConversationHistory_WhenTypedInputsProvided_PreservesStructuredInputs()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var historyPayloadType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.ConversationHistoryMessagePayload");
        var historyListType = typeof(List<>).MakeGenericType(historyPayloadType);
        var payload = JsonSerializer.Deserialize(
            """
            [
              {
                "role": "user",
                "content": "typed history text",
                "inputs": [
                  {
                    "type": "text",
                    "text": "typed history text",
                    "textElements": [
                      {
                        "placeholder": "workspace",
                        "byteRange": {
                          "start": 0,
                          "end": 5
                        }
                      }
                    ]
                  },
                  {
                    "type": "mention",
                    "name": "workspace",
                    "path": "app://workspace"
                  }
                ]
              }
            ]
            """,
            historyListType,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var result = ReflectionTestHelper.InvokeStaticMethod(hostType, "BuildConversationHistory", payload);

        var history = Assert.IsAssignableFrom<IReadOnlyList<ControlPlaneConversationMessage>>(result);
        var message = Assert.Single(history);
        Assert.Equal(2, message.ContentItems.Count);

        var textInput = Assert.IsType<ControlPlaneTextInput>(message.ContentItems[0]);
        Assert.Equal("typed history text", textInput.Text);
        Assert.Equal("workspace", Assert.Single(textInput.TextElements).Placeholder);

        var mentionInput = Assert.IsType<ControlPlaneMentionInput>(message.ContentItems[1]);
        Assert.Equal("workspace", mentionInput.Name);
        Assert.Equal("app://workspace", mentionInput.Path);
    }

    [Fact]
    public async Task InvokeThreadOperationAsync_WhenLoadedListRequested_UsesTypedRuntimeRequest()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);
        var runtime = new CliConsumerFakeRuntime
        {
            ListLoadedThreadsAsyncHandler = (request, _) => Task.FromResult(new ControlPlaneLoadedThreadListResult
            {
                ThreadIds = [new ThreadId("thread-a"), new ThreadId("thread-b")],
                NextCursor = "cursor-next",
            }),
        };

        var parameters = ReflectionTestHelper.ParseJsonElement("""{"limit":2,"cursor":"cursor-1"}""");
        var task = ReflectionTestHelper.InvokeMethod(
            host!,
            "InvokeThreadOperationAsync",
            runtime,
            "thread/loaded/list",
            parameters,
            CancellationToken.None);

        var result = await ReflectionTestHelper.AwaitTaskResultAsync(task);
        var payload = JsonSerializer.SerializeToElement(result);

        var request = Assert.Single(runtime.ThreadLoadedListCalls);
        Assert.Equal(2, request.Limit);
        Assert.Equal("cursor-1", request.Cursor);
        Assert.Equal(
            ["thread-a", "thread-b"],
            payload.GetProperty("data").EnumerateArray().Select(static item => item.GetString()).ToArray());
        Assert.Equal("cursor-next", payload.GetProperty("nextCursor").GetString());
    }

    [Fact]
    public async Task InvokeThreadOperationAsync_WhenMetadataUpdateRequested_UsesTypedRuntimeRequest()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);
        var runtime = new CliConsumerFakeRuntime
        {
            UpdateThreadMetadataAsyncHandler = (request, _) => Task.FromResult(new ControlPlaneThreadOperationResult
            {
                Thread = new ControlPlaneThreadDetail
                {
                    ThreadId = request.ThreadId,
                    Preview = "metadata updated",
                },
            }),
        };

        var parameters = ReflectionTestHelper.ParseJsonElement(
            """{"threadId":"thread-meta-1","gitInfo":{"sha":"abc123","branch":null,"originUrl":"https://example.test/repo.git"}}""");
        var task = ReflectionTestHelper.InvokeMethod(
            host!,
            "InvokeThreadOperationAsync",
            runtime,
            "thread/metadata/update",
            parameters,
            CancellationToken.None);

        var result = await ReflectionTestHelper.AwaitTaskResultAsync(task);
        var payload = JsonSerializer.SerializeToElement(result);

        var request = Assert.Single(runtime.ThreadMetadataUpdateCalls);
        Assert.Equal("thread-meta-1", request.ThreadId);
        Assert.True(request.HasGitSha);
        Assert.Equal("abc123", request.GitSha);
        Assert.True(request.HasGitBranch);
        Assert.Null(request.GitBranch);
        Assert.True(request.HasGitOriginUrl);
        Assert.Equal("https://example.test/repo.git", request.GitOriginUrl);
        Assert.Equal("thread-meta-1", payload.GetProperty("thread").GetProperty("id").GetString());
    }

    [Fact]
    public async Task InvokeThreadOperationAsync_WhenDeleteRequested_UsesTypedDeleteThreadAsync()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);
        var runtime = new CliConsumerFakeRuntime
        {
            DeleteThreadAsyncHandler = (_, _) => Task.FromResult(true),
        };

        var parameters = ReflectionTestHelper.ParseJsonElement("""{"threadId":"thread-delete-1"}""");
        var task = ReflectionTestHelper.InvokeMethod(
            host!,
            "InvokeThreadOperationAsync",
            runtime,
            "thread/delete",
            parameters,
            CancellationToken.None);

        var result = await ReflectionTestHelper.AwaitTaskResultAsync(task);
        Assert.NotNull(result);
        Assert.Equal("thread-delete-1", ReflectionTestHelper.GetProperty(result!, "threadId"));
        Assert.Single(runtime.DeleteThreadCalls);
        Assert.Equal("thread-delete-1", runtime.DeleteThreadCalls[0]);
    }

    [Fact]
    public async Task InvokeRuntimeSurfaceAsync_WhenSkillsListRequested_UsesTypedRuntimeRequest_AndPreservesDataShape()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var runtime = new CliConsumerFakeRuntime
        {
            ListSkillsAsyncHandler = (request, _) => Task.FromResult(new ControlPlaneSkillCatalogResult
            {
                Entries =
                [
                    new ControlPlaneSkillCatalogEntry
                    {
                        WorkingDirectory = "D:/Work/TianShu",
                        Skills =
                        [
                            new ControlPlaneSkillDescriptor
                            {
                                Name = "demo-skill",
                                Description = "typed skill",
                                Path = "D:/skills/demo-skill",
                                PathToSkillsMd = "D:/skills/demo-skill/SKILL.md",
                                Scope = "workspace",
                                Enabled = true,
                                Interface = StructuredValue.FromPlainObject(new Dictionary<string, object?>
                                {
                                    ["displayName"] = "Demo Skill",
                                }),
                            },
                        ],
                    },
                ],
            }),
        };

        var parameters = ReflectionTestHelper.ParseJsonElement(
            """{"cwds":["D:/Work/TianShu"],"forceReload":true,"perCwdExtraUserRoots":[{"cwd":"D:/Work/TianShu","extraUserRoots":["D:/skills"]}]}""");
        var task = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "skills/list",
            parameters,
            CancellationToken.None);

        var result = await ReflectionTestHelper.AwaitTaskResultAsync(task);
        var request = Assert.Single(runtime.SkillsListCalls);
        Assert.Equal("D:/Work/TianShu", Assert.Single(request.WorkingDirectories));
        Assert.True(request.ForceReload);
        Assert.Equal("D:/skills", Assert.Single(Assert.Single(request.ExtraRootsByWorkingDirectory).ExtraUserRoots));

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var item = Assert.Single(document.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal("D:/Work/TianShu", item.GetProperty("cwd").GetString());
        Assert.Equal("demo-skill", Assert.Single(item.GetProperty("skills").EnumerateArray()).GetProperty("name").GetString());
        Assert.Empty(runtime.RpcCalls);
    }

    [Fact]
    public async Task InvokeRuntimeSurfaceAsync_WhenCollaborationModeListRequested_PreservesLegacySnakeCase()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var runtime = new CliConsumerFakeRuntime
        {
            ListCollaborationModesAsyncHandler = _ => Task.FromResult(new ControlPlaneCollaborationModeCatalogResult
            {
                Items =
                [
                    new ControlPlaneCollaborationModeDescriptor
                    {
                        Name = "default",
                        Mode = "chat",
                        Model = "gpt-5",
                        ReasoningEffort = "high",
                    },
                ],
            }),
        };

        var parameters = ReflectionTestHelper.ParseJsonElement("""{}""");
        var task = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "collaborationMode/list",
            parameters,
            CancellationToken.None);

        var result = await ReflectionTestHelper.AwaitTaskResultAsync(task);
        Assert.Equal(1, runtime.CollaborationModeListCallCount);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var item = Assert.Single(document.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal("high", item.GetProperty("reasoning_effort").GetString());
        Assert.False(item.TryGetProperty("reasoningEffort", out _));
        Assert.Empty(runtime.RpcCalls);
    }

    [Fact]
    public async Task InvokeRuntimeSurfaceAsync_WhenMcpServerOauthLoginRequested_UsesTypedRuntimeRequest()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var runtime = new CliConsumerFakeRuntime
        {
            StartMcpServerOauthLoginAsyncHandler = (request, _) => Task.FromResult(new ControlPlaneMcpServerOauthLoginStartResult
            {
                AuthorizationUrl = $"https://example.com/oauth/{request.Name}",
            }),
        };

        var parameters = ReflectionTestHelper.ParseJsonElement("""{"name":"demo","timeoutSecs":30}""");
        var task = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "mcpServer/oauth/login",
            parameters,
            CancellationToken.None);

        var result = await ReflectionTestHelper.AwaitTaskResultAsync(task);
        var typedResult = Assert.IsType<ControlPlaneMcpServerOauthLoginStartResult>(result);
        Assert.Equal("https://example.com/oauth/demo", typedResult.AuthorizationUrl);

        var request = Assert.Single(runtime.McpServerOauthLoginCalls);
        Assert.Equal("demo", request.Name);
        Assert.Equal(30, request.TimeoutSecs);
        Assert.Empty(runtime.RpcCalls);
    }

    [Fact]
    public async Task InvokeRuntimeSurfaceAsync_WhenConfigBatchWriteRequested_UsesTypedRuntimeRequest()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var runtime = new CliConsumerFakeRuntime
        {
            WriteConfigBatchAsyncHandler = (_, _) => Task.FromResult(new ControlPlaneConfigWriteResult
            {
                Status = "ok",
                Version = "v3",
                FilePath = "D:/workspace/config.override.toml",
            }),
        };

        var parameters = ReflectionTestHelper.ParseJsonElement(
            """
            {
              "items": [
                {
                  "keyPath": "profiles.default.model",
                  "value": "gpt-5"
                }
              ],
              "cwd": "D:/workspace",
              "filePath": "D:/workspace/config.override.toml",
              "expectedVersion": "v2",
              "reload_user_config": true
            }
            """);
        var task = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "config/batchWrite",
            parameters,
            CancellationToken.None);

        var result = await ReflectionTestHelper.AwaitTaskResultAsync(task);
        var typedResult = Assert.IsType<Dictionary<string, object?>>(result);
        Assert.Equal("ok", typedResult["status"]);
        Assert.Equal("v3", typedResult["version"]);
        Assert.Equal("D:/workspace/config.override.toml", typedResult["filePath"]);

        var request = Assert.Single(runtime.ConfigBatchWriteCalls);
        Assert.Equal("D:/workspace", request.WorkingDirectory);
        Assert.Equal("D:/workspace/config.override.toml", request.FilePath);
        Assert.Equal("v2", request.ExpectedVersion);
        Assert.True(request.ReloadUserConfig);
        var item = Assert.Single(request.Items);
        Assert.Equal("profiles.default.model", item.KeyPath);
        Assert.NotNull(item.Value);
        Assert.Equal(StructuredValueKind.String, item.Value!.Kind);
        Assert.Equal("gpt-5", item.Value.StringValue);
        Assert.Empty(runtime.RpcCalls);
    }

    [Fact]
    public async Task InvokeRuntimeSurfaceAsync_WhenSessionQueriesRequested_UsesTypedControlPlaneRequests()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var space = new CollaborationSpaceRef(new CollaborationSpaceId("space-sidecar"), "space-sidecar", "Sidecar Space");
        var runtime = new CliConsumerFakeRuntime
        {
            ActiveThreadId = "thread-session-snapshot-1",
            HasActiveTurn = true,
            GetSessionOverviewAsyncHandler = (query, _) => Task.FromResult<SessionOverviewProjection?>(
                new SessionOverviewProjection(
                    query.SessionId,
                    "Session Read",
                    space,
                    SessionMode.Review,
                    new ThreadId("thread-session-1"),
                    HasActiveTurn: true)),
            ListSessionsAsyncHandler = (query, _) => Task.FromResult<IReadOnlyList<SessionOverviewProjection>>(
            [
                new SessionOverviewProjection(
                    new SessionId("session-list-1"),
                    "Session List Item",
                    space,
                    SessionMode.Interactive,
                    IsClosed: query.IncludeClosed),
                ]),
        };

        var snapshotTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "session/snapshot/read",
            ReflectionTestHelper.ParseJsonElement("""{}"""),
            CancellationToken.None);
        var snapshotResult = await ReflectionTestHelper.AwaitTaskResultAsync(snapshotTask);
        var snapshotJson = JsonSerializer.Serialize(snapshotResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("thread-session-snapshot-1", snapshotJson);

        var overviewTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "session/overview/read",
            ReflectionTestHelper.ParseJsonElement("""{"sessionId":"session-read-1"}"""),
            CancellationToken.None);
        var overviewResult = await ReflectionTestHelper.AwaitTaskResultAsync(overviewTask);
        var overviewJson = JsonSerializer.Serialize(overviewResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("session-read-1", overviewJson);
        Assert.Contains("Session Read", overviewJson);

        var listTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "session/list",
            ReflectionTestHelper.ParseJsonElement("""{"collaborationSpaceId":"space-sidecar","includeClosed":true}"""),
            CancellationToken.None);
        var listResult = await ReflectionTestHelper.AwaitTaskResultAsync(listTask);
        var listJson = JsonSerializer.Serialize(listResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("session-list-1", listJson);

        var overviewRequest = Assert.Single(runtime.SessionOverviewCalls);
        Assert.Equal("session-read-1", overviewRequest.SessionId.Value);
        var listRequest = Assert.Single(runtime.SessionListCalls);
        Assert.Equal("space-sidecar", listRequest.CollaborationSpaceId?.Value);
        Assert.True(listRequest.IncludeClosed);
    }

    [Fact]
    public async Task InvokeRuntimeSurfaceAsync_WhenConversationGovernanceAndArtifactQueriesRequested_UsesTypedControlPlaneRequests()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var space = new CollaborationSpaceRef(new CollaborationSpaceId("space-sidecar-formal"), "space-sidecar-formal", "Sidecar Formal Space");
        var participant = new ParticipantRef(new ParticipantId("participant-sidecar-formal"), ParticipantKind.Agent, "Hermes");
        var runtime = new CliConsumerFakeRuntime
        {
            GetThreadProjectionAsyncHandler = (query, _) => Task.FromResult<TianShu.Contracts.Projections.ThreadProjection?>(
                new TianShu.Contracts.Projections.ThreadProjection(
                    query.ThreadId,
                    "Sidecar Thread",
                    space,
                    participant,
                    new TurnId("turn-sidecar-formal"),
                    HasActiveTurn: true)),
            GetApprovalQueueProjectionAsyncHandler = (_, _) => Task.FromResult<ApprovalQueueProjection?>(
                new ApprovalQueueProjection(
                [
                    new ApprovalQueueItem(
                        new ApprovalId("approval-sidecar-formal"),
                        "Need approval",
                        "command execution",
                        participant,
                        DateTimeOffset.UtcNow),
                ])),
            ListUserInputRequestsAsyncHandler = (_, _) => Task.FromResult<IReadOnlyList<UserInputRequest>>(
            [
                new UserInputRequest(
                    new UserInputRequestId("user-input-sidecar-formal"),
                    "Please confirm",
                    participant,
                    requestedAt: DateTimeOffset.UtcNow),
            ]),
            GetArtifactProjectionAsyncHandler = (query, _) => Task.FromResult<ArtifactProjection?>(
                new ArtifactProjection(
                    query.ArtifactId,
                    "Artifact Detail",
                    ArtifactKind.Document,
                    ArtifactLifecycleState.Published,
                    space,
                    participant)),
            GetArtifactCollectionProjectionAsyncHandler = (query, _) => Task.FromResult<ArtifactCollectionProjection?>(
                new ArtifactCollectionProjection(
                    new CollaborationSpaceRef(query.CollaborationSpaceId ?? space.Id, "space-sidecar-formal", "Sidecar Formal Space"),
                    [
                        new ArtifactCollectionItem(
                            new ArtifactId("artifact-sidecar-list-1"),
                            "Artifact List Item",
                            ArtifactKind.Document,
                            ArtifactLifecycleState.Published,
                            participant,
                            UpdatedAt: DateTimeOffset.UtcNow),
                    ])),
        };

        var threadTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "conversation/thread/read",
            ReflectionTestHelper.ParseJsonElement("""{"threadId":"thread-sidecar-formal"}"""),
            CancellationToken.None);
        var threadResult = await ReflectionTestHelper.AwaitTaskResultAsync(threadTask);
        var threadJson = JsonSerializer.Serialize(threadResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("thread-sidecar-formal", threadJson);
        Assert.Contains("Sidecar Thread", threadJson);

        var approvalsTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "governance/approvalQueue/read",
            ReflectionTestHelper.ParseJsonElement("""{"requestedFromParticipantId":"participant-sidecar-formal"}"""),
            CancellationToken.None);
        var approvalsResult = await ReflectionTestHelper.AwaitTaskResultAsync(approvalsTask);
        var approvalsJson = JsonSerializer.Serialize(approvalsResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("approval-sidecar-formal", approvalsJson);

        var userInputsTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "governance/userInputs/list",
            ReflectionTestHelper.ParseJsonElement("""{"participantId":"participant-sidecar-formal"}"""),
            CancellationToken.None);
        var userInputsResult = await ReflectionTestHelper.AwaitTaskResultAsync(userInputsTask);
        var userInputsJson = JsonSerializer.Serialize(userInputsResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("user-input-sidecar-formal", userInputsJson);

        var artifactDetailTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "artifact/detail/read",
            ReflectionTestHelper.ParseJsonElement("""{"artifactId":"artifact-sidecar-formal"}"""),
            CancellationToken.None);
        var artifactDetailResult = await ReflectionTestHelper.AwaitTaskResultAsync(artifactDetailTask);
        var artifactDetailJson = JsonSerializer.Serialize(artifactDetailResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("artifact-sidecar-formal", artifactDetailJson);
        Assert.Contains("Artifact Detail", artifactDetailJson);

        var artifactListTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "artifact/collection/read",
            ReflectionTestHelper.ParseJsonElement("""{"spaceId":"space-sidecar-formal","producedByParticipantId":"participant-sidecar-formal"}"""),
            CancellationToken.None);
        var artifactListResult = await ReflectionTestHelper.AwaitTaskResultAsync(artifactListTask);
        var artifactListJson = JsonSerializer.Serialize(artifactListResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("artifact-sidecar-list-1", artifactListJson);

        Assert.Equal("thread-sidecar-formal", Assert.Single(runtime.ThreadProjectionCalls).ThreadId.Value);
        Assert.Equal("participant-sidecar-formal", Assert.Single(runtime.ApprovalQueueProjectionCalls).RequestedFromParticipantId?.Value);
        Assert.Equal("participant-sidecar-formal", Assert.Single(runtime.UserInputListCalls).RequestedFromParticipantId?.Value);
        Assert.Equal("artifact-sidecar-formal", Assert.Single(runtime.ArtifactProjectionCalls).ArtifactId.Value);
        var artifactCollectionRequest = Assert.Single(runtime.ArtifactCollectionProjectionCalls);
        Assert.Equal("space-sidecar-formal", artifactCollectionRequest.CollaborationSpaceId?.Value);
        Assert.Equal("participant-sidecar-formal", artifactCollectionRequest.ProducedByParticipantId?.Value);
    }

    [Fact]
    public async Task InvokeRuntimeSurfaceAsync_WhenDiagnosticsQueriesRequested_UsesTypedControlPlaneRequests()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var runtime = new CliConsumerFakeRuntime
        {
            GetExecutionTraceAsyncHandler = (query, _) => Task.FromResult<ExecutionTrace?>(
                new ExecutionTrace(
                    query.TraceId,
                    new ExecutionId("execution-sidecar-formal"),
                    [
                        new AttemptSummary(
                            new ExecutionId("execution-sidecar-formal"),
                            AttemptNumber: 1,
                            Succeeded: true,
                            StartedAt: DateTimeOffset.UtcNow,
                            CompletedAt: DateTimeOffset.UtcNow),
                    ])),
            ListAttemptSummariesAsyncHandler = (query, _) => Task.FromResult<IReadOnlyList<AttemptSummary>>(
            [
                new AttemptSummary(
                    query.ExecutionId,
                    AttemptNumber: 2,
                    Succeeded: false,
                    StartedAt: DateTimeOffset.UtcNow,
                    CompletedAt: null),
            ]),
        };

        var traceTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "diagnostics/trace/read",
            ReflectionTestHelper.ParseJsonElement("""{"traceId":"trace-sidecar-formal"}"""),
            CancellationToken.None);
        var traceResult = await ReflectionTestHelper.AwaitTaskResultAsync(traceTask);
        var traceJson = JsonSerializer.Serialize(traceResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("trace-sidecar-formal", traceJson);
        Assert.Contains("execution-sidecar-formal", traceJson);

        var attemptsTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "diagnostics/attempts/list",
            ReflectionTestHelper.ParseJsonElement("""{"executionId":"execution-sidecar-formal"}"""),
            CancellationToken.None);
        var attemptsResult = await ReflectionTestHelper.AwaitTaskResultAsync(attemptsTask);
        var attemptsJson = JsonSerializer.Serialize(attemptsResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"attemptNumber\":2", attemptsJson, StringComparison.Ordinal);

        Assert.Equal("trace-sidecar-formal", Assert.Single(runtime.ExecutionTraceCalls).TraceId.Value);
        Assert.Equal("execution-sidecar-formal", Assert.Single(runtime.AttemptSummaryListCalls).ExecutionId.Value);
    }

    [Fact]
    public async Task InvokeRuntimeSurfaceAsync_WhenIdentityAndMemoryQueriesRequested_UsesTypedControlPlaneRequests()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var accountId = new AccountId("account-sidecar-formal");
        var memorySpaceId = new MemorySpaceId("memory-space-sidecar-formal");
        var runtime = new CliConsumerFakeRuntime
        {
            GetAccountProfileAsyncHandler = (query, _) => Task.FromResult<Account?>(
                new Account(query.AccountId, "Sidecar Identity", "sidecar.identity@example.com")),
            ListBoundDevicesAsyncHandler = (query, _) => Task.FromResult<IReadOnlyList<DeviceBinding>>(
            [
                new DeviceBinding(
                    new DeviceId("device-sidecar-formal"),
                    query.AccountId,
                    "Sidecar Laptop",
                    "windows"),
            ]),
            ListMemorySpacesAsyncHandler = (query, _) => Task.FromResult<IReadOnlyList<MemorySpace>>(
            [
                new MemorySpace(
                    memorySpaceId,
                    query.ScopeKind ?? MemoryScopeKind.Workspace,
                    "repo://tianshu",
                    "Sidecar Workspace",
                    isReadOnly: true),
            ]),
            ResolveMemoryOverlayAsyncHandler = (query, _) => Task.FromResult(
                new MemoryOverlay(
                [
                    new FactMemoryRecord(
                        "repo_root",
                        StructuredValue.FromPlainObject("D:/Work/TianShu"),
                        query.MemorySpaceId ?? memorySpaceId),
                ],
                new HabitProfile(
                    AccountId: accountId,
                    PreferredVerbosity: "verbose"),
                MemoryMergeDecision.Applied)),
        };

        var accountTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "identity/account/read",
            ReflectionTestHelper.ParseJsonElement("""{"accountId":"account-sidecar-formal"}"""),
            CancellationToken.None);
        var accountResult = await ReflectionTestHelper.AwaitTaskResultAsync(accountTask);
        var accountJson = JsonSerializer.Serialize(accountResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("account-sidecar-formal", accountJson);
        Assert.Contains("Sidecar Identity", accountJson);

        var devicesTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "identity/devices/list",
            ReflectionTestHelper.ParseJsonElement("""{"accountId":"account-sidecar-formal"}"""),
            CancellationToken.None);
        var devicesResult = await ReflectionTestHelper.AwaitTaskResultAsync(devicesTask);
        var devicesJson = JsonSerializer.Serialize(devicesResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("device-sidecar-formal", devicesJson);

        var spacesTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "memory/spaces/list",
            ReflectionTestHelper.ParseJsonElement("""{"scopeKind":"workspace"}"""),
            CancellationToken.None);
        var spacesResult = await ReflectionTestHelper.AwaitTaskResultAsync(spacesTask);
        var spacesJson = JsonSerializer.Serialize(spacesResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("memory-space-sidecar-formal", spacesJson);
        Assert.Contains("Sidecar Workspace", spacesJson);

        var overlayTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "memory/overlay/read",
            ReflectionTestHelper.ParseJsonElement("""{"memorySpaceId":"memory-space-sidecar-formal","spaceId":"space-sidecar-memory"}"""),
            CancellationToken.None);
        var overlayResult = await ReflectionTestHelper.AwaitTaskResultAsync(overlayTask);
        var overlayJson = JsonSerializer.Serialize(overlayResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("repo_root", overlayJson);
        Assert.Contains("verbose", overlayJson);

        Assert.Equal("account-sidecar-formal", Assert.Single(runtime.AccountProfileCalls).AccountId.Value);
        Assert.Equal("account-sidecar-formal", Assert.Single(runtime.BoundDeviceListCalls).AccountId.Value);
        Assert.Equal(MemoryScopeKind.Workspace, Assert.Single(runtime.MemorySpaceListCalls).ScopeKind);
        var overlayRequest = Assert.Single(runtime.MemoryOverlayCalls);
        Assert.Equal("memory-space-sidecar-formal", overlayRequest.MemorySpaceId?.Value);
        Assert.Equal("space-sidecar-memory", overlayRequest.CollaborationSpaceId?.Value);
    }

    [Fact]
    public async Task InvokeRuntimeSurfaceAsync_WhenMemoryFormalCommandsRequested_UsesTypedControlPlaneRequests()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var memorySpaceId = new MemorySpaceId("memory-space-sidecar-formal");
        var memoryRecordId = new MemoryRecordId("memory-record-sidecar-formal");
        var runtime = new CliConsumerFakeRuntime
        {
            ListMemoryProvidersAsyncHandler = (query, _) => Task.FromResult<IReadOnlyList<MemoryProviderDescriptor>>(
            [
                new MemoryProviderDescriptor(
                    "provider-sidecar-local",
                    "Sidecar Local Provider",
                    "1.0",
                    MemoryProviderCapability.Add | MemoryProviderCapability.Filter,
                    [query.ScopeKind ?? MemoryScopeKind.Workspace]),
            ]),
            AddMemoryAsyncHandler = (command, _) => Task.FromResult(new MemoryMutationResult(true, memoryRecordId, MemoryLifecycleStatus.Active)),
            ListMemoryReviewsAsyncHandler = (query, _) => Task.FromResult(new MemoryReviewQueryResult([
                new MemoryReviewItem(new FactMemoryRecord("pref.shell", StructuredValue.FromString("pwsh"), memorySpaceId, id: memoryRecordId)),
            ])),
            ApproveMemoryReviewAsyncHandler = (command, _) => Task.FromResult(new MemoryMutationResult(true, command.MemoryRecordId, MemoryLifecycleStatus.Active)),
            DemoteMemoryReviewAsyncHandler = (command, _) => Task.FromResult(new MemoryMutationResult(true, command.MemoryRecordId, MemoryLifecycleStatus.Archived)),
            MergeMemoryReviewAsyncHandler = (command, _) => Task.FromResult(new MemoryMutationResult(true, command.TargetRecordId, MemoryLifecycleStatus.Active)),
            RestoreMemoryReviewAsyncHandler = (command, _) => Task.FromResult(new MemoryMutationResult(true, command.MemoryRecordId, MemoryLifecycleStatus.PendingReview)),
            RecordMemoryFeedbackAsyncHandler = (command, _) => Task.FromResult(new MemoryMutationResult(true, command.MemoryRecordId, MemoryLifecycleStatus.Active)),
            RecordMemoryCitationAsyncHandler = (command, _) => Task.FromResult(new MemoryMutationResult(true, memoryRecordId, MemoryLifecycleStatus.Active)),
        };

        async Task<object?> InvokeAsync(string method, string payloadJson)
        {
            var task = ReflectionTestHelper.InvokeStaticMethod(
                hostType,
                "InvokeRuntimeSurfaceAsync",
                runtime,
                method,
                ReflectionTestHelper.ParseJsonElement(payloadJson),
                CancellationToken.None);
            return await ReflectionTestHelper.AwaitTaskResultAsync(task);
        }

        var providersResult = await InvokeAsync("memory/providers/list", """{"scopeKind":"workspace"}""");
        var providersJson = JsonSerializer.Serialize(providersResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("provider-sidecar-local", providersJson);

        await InvokeAsync("memory/filter", """{"memorySpaceId":{"value":"memory-space-sidecar-formal"},"key":"pref.shell","sourceKind":"conversation","scopeKind":"workspace"}""");
        await InvokeAsync("memory/review/list", """{"memorySpaceId":{"value":"memory-space-sidecar-formal"},"key":"pref.shell"}""");
        await InvokeAsync("memory/add", """{"memorySpaceId":{"value":"memory-space-sidecar-formal"},"key":"pref.shell","value":"pwsh","confidence":0.9}""");
        await InvokeAsync("memory/extract", """{"memorySpaceId":{"value":"memory-space-sidecar-formal"},"source":{"sourceKind":"conversation","sourceId":"turn-sidecar-001"},"content":"记住我更喜欢 pwsh"}""");
        await InvokeAsync("memory/import", """{"memorySpaceId":{"value":"memory-space-sidecar-formal"},"source":{"sourceKind":"file","sourceId":"memory.json"}}""");
        await InvokeAsync("memory/export", """{"memorySpaceId":{"value":"memory-space-sidecar-formal"},"destination":{"sourceKind":"externalProvider","sourceId":"provider-sidecar-export"},"filter":{"key":"pref.shell"}}""");
        await InvokeAsync("memory/provider/bind", """{"providerId":"provider-sidecar-local","memorySpaceId":{"value":"memory-space-sidecar-formal"},"mode":"readWrite","allowedCapabilities":10}""");
        await InvokeAsync("memory/forget", """{"memoryRecordId":{"value":"memory-record-sidecar-formal"}}""");
        await InvokeAsync("memory/delete", """{"memoryRecordId":{"value":"memory-record-sidecar-formal"},"reason":"cleanup"}""");
        await InvokeAsync("memory/review/approve", """{"memoryRecordId":{"value":"memory-record-sidecar-formal"},"memorySpaceId":{"value":"memory-space-sidecar-formal"},"key":"pref.shell","reason":"accepted"}""");
        await InvokeAsync("memory/review/demote", """{"memoryRecordId":{"value":"memory-record-sidecar-formal"},"memorySpaceId":{"value":"memory-space-sidecar-formal"},"key":"pref.shell","reason":"low-confidence"}""");
        await InvokeAsync("memory/review/merge", """{"reviewRecordId":{"value":"memory-record-sidecar-formal"},"targetRecordId":{"value":"memory-record-sidecar-target"},"memorySpaceId":{"value":"memory-space-sidecar-formal"},"reason":"duplicate"}""");
        await InvokeAsync("memory/review/restore", """{"memoryRecordId":{"value":"memory-record-sidecar-formal"},"memorySpaceId":{"value":"memory-space-sidecar-formal"},"key":"pref.shell","reason":"retry"}""");
        await InvokeAsync("memory/feedback/record", """{"memoryRecordId":{"value":"memory-record-sidecar-formal"},"decision":"applied","feedback":"accepted"}""");
        await InvokeAsync("memory/citation/record", """{"citation":{"entries":[{"memoryRecordId":{"value":"memory-record-sidecar-formal"},"memorySpaceId":{"value":"memory-space-sidecar-formal"},"key":"pref.shell"}]}}""");

        Assert.Equal(MemoryScopeKind.Workspace, Assert.Single(runtime.MemoryProviderListCalls).ScopeKind);
        var filterRequest = Assert.Single(runtime.MemoryFilterCalls);
        Assert.Equal(memorySpaceId, filterRequest.MemorySpaceId);
        Assert.Equal("pref.shell", filterRequest.Key);
        Assert.Equal(MemorySourceKind.Conversation, filterRequest.SourceKind);

        var reviewListRequest = Assert.Single(runtime.MemoryReviewListCalls);
        Assert.Equal(memorySpaceId, reviewListRequest.MemorySpaceId);
        Assert.Equal("pref.shell", reviewListRequest.Key);

        var addRequest = Assert.Single(runtime.MemoryAddCalls);
        Assert.Equal(memorySpaceId, addRequest.MemorySpaceId);
        Assert.Equal("pref.shell", addRequest.Key);

        var extractRequest = Assert.Single(runtime.MemoryExtractCalls);
        Assert.Equal(memorySpaceId, extractRequest.MemorySpaceId);
        Assert.Equal(MemorySourceKind.Conversation, extractRequest.Source.SourceKind);
        Assert.Equal("turn-sidecar-001", extractRequest.Source.SourceId);

        var importRequest = Assert.Single(runtime.MemoryImportCalls);
        Assert.Equal(memorySpaceId, importRequest.MemorySpaceId);
        Assert.Equal(MemorySourceKind.File, importRequest.Source.SourceKind);

        var exportRequest = Assert.Single(runtime.MemoryExportCalls);
        Assert.Equal(memorySpaceId, exportRequest.MemorySpaceId);
        Assert.Equal("provider-sidecar-export", exportRequest.Destination?.SourceId);
        Assert.Equal("pref.shell", exportRequest.Filter?.Key);

        var bindRequest = Assert.Single(runtime.MemoryBindProviderCalls);
        Assert.Equal("provider-sidecar-local", bindRequest.ProviderId);
        Assert.Equal(memorySpaceId, bindRequest.MemorySpaceId);
        Assert.Equal(MemoryProviderBindingMode.ReadWrite, bindRequest.Mode);
        Assert.Equal(MemoryProviderCapability.Add | MemoryProviderCapability.Filter, bindRequest.AllowedCapabilities);

        Assert.Equal(memoryRecordId, Assert.Single(runtime.MemoryForgetCalls).MemoryRecordId);
        Assert.Equal("cleanup", Assert.Single(runtime.MemoryDeleteCalls).Reason);

        var approveRequest = Assert.Single(runtime.MemoryApproveReviewCalls);
        Assert.Equal(memoryRecordId, approveRequest.MemoryRecordId);
        Assert.Equal("accepted", approveRequest.Reason);

        var demoteRequest = Assert.Single(runtime.MemoryDemoteReviewCalls);
        Assert.Equal(memoryRecordId, demoteRequest.MemoryRecordId);
        Assert.Equal("low-confidence", demoteRequest.Reason);

        var mergeRequest = Assert.Single(runtime.MemoryMergeReviewCalls);
        Assert.Equal(memoryRecordId, mergeRequest.ReviewRecordId);
        Assert.Equal(new MemoryRecordId("memory-record-sidecar-target"), mergeRequest.TargetRecordId);

        var restoreRequest = Assert.Single(runtime.MemoryRestoreReviewCalls);
        Assert.Equal(memoryRecordId, restoreRequest.MemoryRecordId);
        Assert.Equal("retry", restoreRequest.Reason);

        var feedbackRequest = Assert.Single(runtime.MemoryFeedbackCalls);
        Assert.Equal(memoryRecordId, feedbackRequest.MemoryRecordId);
        Assert.Equal(MemoryMergeDecision.Applied, feedbackRequest.Decision);
        Assert.Equal("accepted", feedbackRequest.Feedback);

        var citationRequest = Assert.Single(runtime.MemoryCitationCalls);
        var citationEntry = Assert.Single(citationRequest.Citation.Entries);
        Assert.Equal(memoryRecordId, citationEntry.MemoryRecordId);
        Assert.Equal(memorySpaceId, citationEntry.MemorySpaceId);
        Assert.Equal("pref.shell", citationEntry.Key);
    }

    [Fact]
    public async Task InvokeRuntimeSurfaceAsync_WhenCatalogAndAgentFormalQueriesRequested_UsesTypedControlPlaneRequests()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var runtime = new CliConsumerFakeRuntime
        {
            GetCapabilityCatalogAsyncHandler = (query, _) => Task.FromResult(
                new CapabilityCatalogSnapshot(
                    activeProviderKey: "openai",
                    activeModel: "gpt-5",
                    providers:
                    [
                        new ProviderProfile(
                            "openai",
                            "OpenAI",
                            "responses",
                            transportModes: ["http", "websocket"],
                            models:
                            [
                                new ModelProfile(
                                    "gpt-5",
                                    "gpt-5",
                                    "GPT-5",
                                    defaultReasoningEffort: "high",
                                    supportsReasoningSummaries: true,
                                    defaultReasoningSummary: "detailed",
                                    supportsVerbosity: true,
                                    defaultVerbosity: "verbose",
                                    preferWebsocketTransport: true),
                            ],
                            supportsWebsockets: true),
                    ])),
            ResolveEngineBindingAsyncHandler = (query, _) => Task.FromResult(
                new ResolvedEngineBinding(
                    new EngineBinding(
                        "openai:gpt-5",
                        query.PreferredProviderKey ?? "openai",
                        query.PreferredModelKey ?? "gpt-5",
                        "gpt-5",
                        "responses",
                        new CatalogStreamingPreference("websocket", query.PreferWebsocketTransport, useWebsocketTransport: true),
                        supportsWebsockets: true,
                        reasoning: new CatalogReasoningProfile(
                            query.ReasoningEffort,
                            query.ReasoningSummary,
                            query.Verbosity)))),
            ListAgentsAsyncHandler = (query, _) => Task.FromResult(
                new ControlPlaneAgentRosterResult
                {
                    NextCursor = "agent-sidecar-next",
                    Agents =
                    [
                        new ControlPlaneAgentDescriptor
                        {
                            ThreadId = new ThreadId("agent-sidecar-formal"),
                            AgentNickname = "Hermes",
                            AgentRole = query.IncludePrimaryThreads ? "primary" : "worker",
                            Preview = "sidecar formal agent",
                            UpdatedAt = DateTimeOffset.UtcNow,
                        },
                    ],
                }),
        };

        var catalogTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "model/catalog/read",
            ReflectionTestHelper.ParseJsonElement("""{"cwd":"D:/Work/TianShu","limit":11,"includeHidden":true}"""),
            CancellationToken.None);
        var catalogResult = await ReflectionTestHelper.AwaitTaskResultAsync(catalogTask);
        var catalogJson = JsonSerializer.Serialize(catalogResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("OpenAI", catalogJson, StringComparison.Ordinal);
        Assert.Contains("GPT-5", catalogJson, StringComparison.Ordinal);

        var resolveTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "model/binding/resolve",
            ReflectionTestHelper.ParseJsonElement("""{"cwd":"D:/Work/TianShu","providerKey":"openai","modelKey":"gpt-5","reasoningEffort":"high","reasoningSummary":"detailed","verbosity":"verbose","preferWebsocketTransport":true}"""),
            CancellationToken.None);
        var resolveResult = await ReflectionTestHelper.AwaitTaskResultAsync(resolveTask);
        var resolveJson = JsonSerializer.Serialize(resolveResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"providerKey\":\"openai\"", resolveJson, StringComparison.Ordinal);
        Assert.Contains("\"transportMode\":\"websocket\"", resolveJson, StringComparison.Ordinal);

        var agentsTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "agent/list",
            ReflectionTestHelper.ParseJsonElement("""{"limit":5,"cursor":"agent-sidecar-cursor","includePrimaryThreads":true}"""),
            CancellationToken.None);
        var agentsResult = await ReflectionTestHelper.AwaitTaskResultAsync(agentsTask);
        var agentsJson = JsonSerializer.Serialize(agentsResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("Hermes", agentsJson, StringComparison.Ordinal);
        Assert.Contains("agent-sidecar-next", agentsJson, StringComparison.Ordinal);

        var catalogRequest = Assert.Single(runtime.ProviderCatalogCalls);
        Assert.Equal("D:/Work/TianShu", catalogRequest.WorkspacePath);
        Assert.True(catalogRequest.IncludeHiddenModels);
        Assert.Equal(11, catalogRequest.ModelLimit);

        var resolveRequest = Assert.Single(runtime.EngineBindingCalls);
        Assert.Equal("D:/Work/TianShu", resolveRequest.WorkspacePath);
        Assert.Equal("openai", resolveRequest.PreferredProviderKey);
        Assert.Equal("gpt-5", resolveRequest.PreferredModelKey);
        Assert.Equal("high", resolveRequest.ReasoningEffort);
        Assert.Equal("detailed", resolveRequest.ReasoningSummary);
        Assert.Equal("verbose", resolveRequest.Verbosity);
        Assert.True(resolveRequest.PreferWebsocketTransport);

        var agentListRequest = Assert.Single(runtime.AgentListCalls);
        Assert.Equal(5, agentListRequest.Limit);
        Assert.Equal("agent-sidecar-cursor", agentListRequest.Cursor);
        Assert.True(agentListRequest.IncludePrimaryThreads);
    }

    [Fact]
    public async Task InvokeRuntimeSurfaceAsync_WhenAgentFormalCommandsRequested_UsesTypedControlPlaneRequests()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var runtime = new CliConsumerFakeRuntime
        {
            RegisterAgentThreadAsyncHandler = (request, _) => Task.FromResult(
                new ControlPlaneAgentThreadRegistrationResult
                {
                    Agent = new ControlPlaneAgentDescriptor
                    {
                        ThreadId = request.ThreadId,
                        AgentNickname = request.AgentNickname,
                        AgentRole = request.AgentRole,
                        UpdatedAt = new DateTimeOffset(2026, 4, 29, 0, 0, 0, TimeSpan.Zero),
                    },
                }),
            CreateAgentJobAsyncHandler = (request, _) => Task.FromResult(
                new ControlPlaneJobOperationResult
                {
                    Job = new ControlPlaneJobDetails
                    {
                        Id = request.JobId ?? new JobId("job-sidecar-runtime-1"),
                        Name = request.Name ?? "runtime-job",
                        Status = "created",
                        Instruction = request.Instruction,
                    },
                }),
            DispatchAgentJobAsyncHandler = (request, _) => Task.FromResult(
                new ControlPlaneJobOperationResult
                {
                    Items =
                    [
                        new ControlPlaneJobItemDetails
                        {
                            ItemId = new JobItemId("item-sidecar-runtime-1"),
                            AssignedThreadId = request.ThreadIds[0],
                            Status = "running",
                        },
                    ],
                }),
            ReportAgentJobItemAsyncHandler = (request, _) => Task.FromResult(
                new ControlPlaneJobOperationResult
                {
                    Item = new ControlPlaneJobItemDetails
                    {
                        ItemId = request.ItemId,
                        Status = request.Status,
                        Result = request.Result,
                    },
                }),
            ReadAgentJobAsyncHandler = (request, _) => Task.FromResult(
                new ControlPlaneJobOperationResult
                {
                    Job = new ControlPlaneJobDetails
                    {
                        Id = request.JobId,
                        Name = "runtime-job",
                        Status = "completed",
                        Instruction = "Check completion",
                    },
                }),
        };

        var registerTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "agent/thread/register",
            ReflectionTestHelper.ParseJsonElement("""{"threadId":"thread-sidecar-runtime-1","agentNickname":"Runtime Hermes","agentRole":"reviewer"}"""),
            CancellationToken.None);
        var registerResult = await ReflectionTestHelper.AwaitTaskResultAsync(registerTask);
        var registerJson = JsonSerializer.Serialize(registerResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"thread\"", registerJson, StringComparison.Ordinal);
        Assert.Contains("thread-sidecar-runtime-1", registerJson, StringComparison.Ordinal);

        var createTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "agent/job/create",
            ReflectionTestHelper.ParseJsonElement("""{"jobId":"job-sidecar-runtime-1","name":"runtime-job","instruction":"Check runtime surface","items":[{"path":"src/A.cs"},{"path":"src/B.cs"}],"autoExport":true}"""),
            CancellationToken.None);
        var createResult = await ReflectionTestHelper.AwaitTaskResultAsync(createTask);
        var createJson = JsonSerializer.Serialize(createResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("job-sidecar-runtime-1", createJson, StringComparison.Ordinal);

        var dispatchTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "agent/job/dispatch",
            ReflectionTestHelper.ParseJsonElement("""{"jobId":"job-sidecar-runtime-1","threadIds":["thread-sidecar-runtime-1","THREAD-SIDECAR-RUNTIME-1","thread-sidecar-runtime-2"]}"""),
            CancellationToken.None);
        var dispatchResult = await ReflectionTestHelper.AwaitTaskResultAsync(dispatchTask);
        var dispatchJson = JsonSerializer.Serialize(dispatchResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("item-sidecar-runtime-1", dispatchJson, StringComparison.Ordinal);

        var reportTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "agent/job/item/report",
            ReflectionTestHelper.ParseJsonElement("""{"jobId":"job-sidecar-runtime-1","itemId":"item-sidecar-runtime-1","status":"completed","result":{"score":98}}"""),
            CancellationToken.None);
        var reportResult = await ReflectionTestHelper.AwaitTaskResultAsync(reportTask);
        var reportJson = JsonSerializer.Serialize(reportResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"completed\"", reportJson, StringComparison.Ordinal);

        var readTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "agent/job/read",
            ReflectionTestHelper.ParseJsonElement("""{"jobId":"job-sidecar-runtime-1"}"""),
            CancellationToken.None);
        var readResult = await ReflectionTestHelper.AwaitTaskResultAsync(readTask);
        var readJson = JsonSerializer.Serialize(readResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("runtime-job", readJson, StringComparison.Ordinal);
        Assert.Contains("\"completed\"", readJson, StringComparison.Ordinal);

        var registerRequest = Assert.Single(runtime.AgentThreadRegistrationCalls);
        Assert.Equal("thread-sidecar-runtime-1", registerRequest.ThreadId.Value);
        Assert.Equal("Runtime Hermes", registerRequest.AgentNickname);
        Assert.Equal("reviewer", registerRequest.AgentRole);

        var createRequest = Assert.Single(runtime.AgentJobCreateCalls);
        Assert.Equal("job-sidecar-runtime-1", createRequest.JobId?.Value);
        Assert.Equal("runtime-job", createRequest.Name);
        Assert.Equal("Check runtime surface", createRequest.Instruction);
        Assert.True(createRequest.AutoExport);
        Assert.Equal(2, createRequest.Items.Count);

        var dispatchRequest = Assert.Single(runtime.AgentJobDispatchCalls);
        Assert.Equal("job-sidecar-runtime-1", dispatchRequest.JobId.Value);
        Assert.Equal(new[] { "thread-sidecar-runtime-1", "thread-sidecar-runtime-2" }, dispatchRequest.ThreadIds.Select(static item => item.Value).ToArray());

        var reportRequest = Assert.Single(runtime.AgentJobItemReportCalls);
        Assert.Equal("item-sidecar-runtime-1", reportRequest.ItemId.Value);
        Assert.Equal("completed", reportRequest.Status);

        var readRequest = Assert.Single(runtime.AgentJobReadCalls);
        Assert.Equal("job-sidecar-runtime-1", readRequest.JobId.Value);
    }

    [Fact]
    public async Task InvokeRuntimeSurfaceAsync_WhenCollaborationQueriesRequested_UsesTypedControlPlaneRequests()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var spaceId = new CollaborationSpaceId("space-collab-1");
        var runtime = new CliConsumerFakeRuntime
        {
            GetCollaborationSpaceOverviewAsyncHandler = (query, _) => Task.FromResult<CollaborationSpaceOverviewProjection?>(
                new CollaborationSpaceOverviewProjection(
                    query.SpaceId,
                    "space-collab-1",
                    "Collab Space",
                    IsArchived: false)),
            GetCollaborationSpaceProjectionAsyncHandler = (query, _) => Task.FromResult<CollaborationSpaceProjection?>(
                new CollaborationSpaceProjection(
                    new CollaborationSpaceRef(query.SpaceId, "space-collab-1", "Collab Space"),
                    ActiveSessionCount: 2,
                    ActiveThreadCount: 3,
                    IsArchived: false)),
            ListCollaborationSpacesAsyncHandler = (query, _) => Task.FromResult<IReadOnlyList<CollaborationSpaceOverviewProjection>>(
            [
                new CollaborationSpaceOverviewProjection(
                    spaceId,
                    "space-collab-1",
                    "Collab Space",
                    IsArchived: query.IncludeArchived),
            ]),
        };

        var overviewTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "collaboration/overview/read",
            ReflectionTestHelper.ParseJsonElement("""{"spaceId":"space-collab-1"}"""),
            CancellationToken.None);
        var overviewResult = await ReflectionTestHelper.AwaitTaskResultAsync(overviewTask);
        var overviewJson = JsonSerializer.Serialize(overviewResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("space-collab-1", overviewJson);

        var spaceTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "collaboration/space/read",
            ReflectionTestHelper.ParseJsonElement("""{"collaborationSpaceId":"space-collab-1"}"""),
            CancellationToken.None);
        var spaceResult = await ReflectionTestHelper.AwaitTaskResultAsync(spaceTask);
        var spaceJson = JsonSerializer.Serialize(spaceResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("Collab Space", spaceJson);

        var listTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "collaboration/list",
            ReflectionTestHelper.ParseJsonElement("""{"includeArchived":true}"""),
            CancellationToken.None);
        var listResult = await ReflectionTestHelper.AwaitTaskResultAsync(listTask);
        var listJson = JsonSerializer.Serialize(listResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("space-collab-1", listJson);

        Assert.Equal("space-collab-1", Assert.Single(runtime.CollaborationSpaceOverviewCalls).SpaceId.Value);
        Assert.Equal("space-collab-1", Assert.Single(runtime.CollaborationSpaceProjectionCalls).SpaceId.Value);
        Assert.True(Assert.Single(runtime.CollaborationSpaceListCalls).IncludeArchived);
    }

    [Fact]
    public async Task InvokeRuntimeSurfaceAsync_WhenCollaborationCommandsRequested_UsesTypedControlPlaneRequests()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var runtime = new CliConsumerFakeRuntime
        {
            CreateCollaborationSpaceAsyncHandler = (command, _) => Task.FromResult(
                new CollaborationSpace(command.SpaceId, command.Key, command.DisplayName, command.Profile, command.Defaults, command.PolicyRef)),
            ConfigureCollaborationSpaceAsyncHandler = (command, _) => Task.FromResult(
                new CollaborationSpace(
                    command.SpaceId,
                    "team-alpha",
                    command.DisplayName ?? "Team Alpha",
                    command.Profile ?? new CollaborationSpaceProfile("Updated purpose"),
                    command.Defaults ?? CollaborationDefaultSet.Empty,
                    command.PolicyRef)),
            ArchiveCollaborationSpaceAsyncHandler = (_, _) => Task.FromResult(true),
            BindParticipantToSessionAsyncHandler = (_, _) => Task.FromResult(true),
            BindParticipantToWorkflowAsyncHandler = (_, _) => Task.FromResult(true),
            UpdateParticipantRoleAsyncHandler = (_, _) => Task.FromResult(true),
        };

        var createTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "collaboration/create",
            ReflectionTestHelper.ParseJsonElement("""{"spaceId":"space-collab-2","key":"team-alpha","displayName":"Team Alpha","purpose":"Cross repo collaboration","defaultWorkspace":"D:/Repos/TianShu","defaultExecutionProfile":"review","policyKey":"policy-alpha"}"""),
            CancellationToken.None);
        var createResult = await ReflectionTestHelper.AwaitTaskResultAsync(createTask);
        var createJson = JsonSerializer.Serialize(createResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("Team Alpha", createJson);

        var configureTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "collaboration/configure",
            ReflectionTestHelper.ParseJsonElement("""{"spaceId":"space-collab-2","displayName":"Team Alpha v2","purpose":"Updated purpose"}"""),
            CancellationToken.None);
        await ReflectionTestHelper.AwaitTaskResultAsync(configureTask);

        var archiveTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "collaboration/archive",
            ReflectionTestHelper.ParseJsonElement("""{"spaceId":"space-collab-2"}"""),
            CancellationToken.None);
        await ReflectionTestHelper.AwaitTaskResultAsync(archiveTask);

        var bindSessionTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "participant/bindSession",
            ReflectionTestHelper.ParseJsonElement("""{"sessionId":"session-sidecar-1","participantId":"participant-sidecar-1"}"""),
            CancellationToken.None);
        await ReflectionTestHelper.AwaitTaskResultAsync(bindSessionTask);

        var bindWorkflowTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "participant/bindWorkflow",
            ReflectionTestHelper.ParseJsonElement("""{"workflowId":"workflow-sidecar-1","participantId":"participant-sidecar-1"}"""),
            CancellationToken.None);
        await ReflectionTestHelper.AwaitTaskResultAsync(bindWorkflowTask);

        var updateRoleTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "participant/updateRole",
            ReflectionTestHelper.ParseJsonElement("""{"participantId":"participant-sidecar-1","role":"owner"}"""),
            CancellationToken.None);
        await ReflectionTestHelper.AwaitTaskResultAsync(updateRoleTask);

        var createRequest = Assert.Single(runtime.CollaborationSpaceCreateCalls);
        Assert.Equal("space-collab-2", createRequest.SpaceId.Value);
        Assert.Equal("team-alpha", createRequest.Key);
        Assert.Equal("Cross repo collaboration", createRequest.Profile.Purpose);
        Assert.Equal("D:/Repos/TianShu", createRequest.Defaults.DefaultWorkspace);
        Assert.Equal("review", createRequest.Defaults.DefaultExecutionProfile);
        Assert.Equal("policy-alpha", createRequest.PolicyRef?.PolicyKey);

        Assert.Equal("Team Alpha v2", Assert.Single(runtime.CollaborationSpaceConfigureCalls).DisplayName);
        Assert.Equal("space-collab-2", Assert.Single(runtime.CollaborationSpaceArchiveCalls).SpaceId.Value);
        Assert.Equal("session-sidecar-1", Assert.Single(runtime.ParticipantSessionBindCalls).SessionId.Value);
        Assert.Equal("workflow-sidecar-1", Assert.Single(runtime.ParticipantWorkflowBindCalls).WorkflowId.Value);
        Assert.Equal("owner", Assert.Single(runtime.ParticipantRoleUpdateCalls).Role);
    }

    [Fact]
    public async Task InvokeRuntimeSurfaceAsync_WhenParticipantQueriesRequested_UsesTypedControlPlaneRequests()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var participantRef = new ParticipantRef(new ParticipantId("participant-sidecar-1"), ParticipantKind.Agent, "Hermes");
        var runtime = new CliConsumerFakeRuntime
        {
            GetParticipantProjectionAsyncHandler = (query, _) => Task.FromResult<TianShu.Contracts.Participants.ParticipantProjection?>(
                new TianShu.Contracts.Participants.ParticipantProjection(query.ParticipantId, ParticipantKind.Agent, "Hermes", "reviewer")),
            GetParticipantViewProjectionAsyncHandler = (query, _) => Task.FromResult<TianShu.Contracts.Projections.ParticipantProjection?>(
                new TianShu.Contracts.Projections.ParticipantProjection(
                    participantRef,
                    ScopeKind: "participant",
                    ScopeKey: query.ParticipantId.Value,
                    Role: "reviewer",
                    IsActive: true)),
            ListParticipantsInScopeAsyncHandler = (query, _) => Task.FromResult<IReadOnlyList<TianShu.Contracts.Participants.ParticipantProjection>>(
            [
                new TianShu.Contracts.Participants.ParticipantProjection(new ParticipantId("participant-sidecar-1"), ParticipantKind.Agent, query.CollaborationSpaceId.Value, "reviewer"),
            ]),
        };

        var participantTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "participant/read",
            ReflectionTestHelper.ParseJsonElement("""{"participantId":"participant-sidecar-1"}"""),
            CancellationToken.None);
        var participantResult = await ReflectionTestHelper.AwaitTaskResultAsync(participantTask);
        var participantJson = JsonSerializer.Serialize(participantResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("participant-sidecar-1", participantJson);

        var participantViewTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "participant/view/read",
            ReflectionTestHelper.ParseJsonElement("""{"participantId":"participant-sidecar-1"}"""),
            CancellationToken.None);
        var participantViewResult = await ReflectionTestHelper.AwaitTaskResultAsync(participantViewTask);
        var participantViewJson = JsonSerializer.Serialize(participantViewResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("Hermes", participantViewJson);

        var participantListTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "participant/list",
            ReflectionTestHelper.ParseJsonElement("""{"spaceId":"space-collab-1"}"""),
            CancellationToken.None);
        var participantListResult = await ReflectionTestHelper.AwaitTaskResultAsync(participantListTask);
        var participantListJson = JsonSerializer.Serialize(participantListResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("space-collab-1", participantListJson);

        Assert.Equal("participant-sidecar-1", Assert.Single(runtime.ParticipantProjectionCalls).ParticipantId.Value);
        Assert.Equal("participant-sidecar-1", Assert.Single(runtime.ParticipantViewProjectionCalls).ParticipantId.Value);
        Assert.Equal("space-collab-1", Assert.Single(runtime.ParticipantListCalls).CollaborationSpaceId.Value);
    }

    [Fact]
    public async Task InvokeRuntimeSurfaceAsync_WhenWorkflowProjectionQueriesRequested_UsesTypedControlPlaneRequests()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var space = new CollaborationSpaceRef(new CollaborationSpaceId("space-workflow"), "space-workflow", "Workflow Space");
        var runtime = new CliConsumerFakeRuntime
        {
            GetWorkflowBoardProjectionAsyncHandler = (query, _) => Task.FromResult<WorkflowBoardProjection?>(
                new WorkflowBoardProjection(query.WorkflowId, "Board Demo", space, 1, 2, 3)),
            GetTaskBoardProjectionAsyncHandler = (query, _) => Task.FromResult<TaskBoardProjection?>(
                new TaskBoardProjection(
                    query.WorkflowId,
                    [new TaskBoardItem(new TaskId("task-1"), "Implement direct query", "running")])),
            GetPlanProjectionAsyncHandler = (query, _) => Task.FromResult<PlanProjection?>(
                new PlanProjection(
                    query.WorkflowId,
                    new Plan("Plan Demo", [new PlanStep(0, "Route sidecar formal query")]))),
        };

        var workflowBoardTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "workflow/board/read",
            ReflectionTestHelper.ParseJsonElement("""{"workflowId":"workflow-1"}"""),
            CancellationToken.None);
        var workflowBoardResult = await ReflectionTestHelper.AwaitTaskResultAsync(workflowBoardTask);
        var workflowBoardJson = JsonSerializer.Serialize(workflowBoardResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("workflow-1", workflowBoardJson);
        Assert.Contains("Board Demo", workflowBoardJson);

        var taskBoardTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "workflow/taskBoard/read",
            ReflectionTestHelper.ParseJsonElement("""{"workflowId":"workflow-1"}"""),
            CancellationToken.None);
        var taskBoardResult = await ReflectionTestHelper.AwaitTaskResultAsync(taskBoardTask);
        var taskBoardJson = JsonSerializer.Serialize(taskBoardResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("task-1", taskBoardJson);

        var planTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "workflow/plan/read",
            ReflectionTestHelper.ParseJsonElement("""{"workflowId":"workflow-1"}"""),
            CancellationToken.None);
        var planResult = await ReflectionTestHelper.AwaitTaskResultAsync(planTask);
        var planJson = JsonSerializer.Serialize(planResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("Plan Demo", planJson);

        Assert.Equal("workflow-1", Assert.Single(runtime.WorkflowBoardProjectionCalls).WorkflowId.Value);
        Assert.Equal("workflow-1", Assert.Single(runtime.TaskBoardProjectionCalls).WorkflowId.Value);
        Assert.Equal("workflow-1", Assert.Single(runtime.PlanProjectionCalls).WorkflowId.Value);
    }

    [Fact]
    public async Task InvokeRuntimeSurfaceAsync_WhenWorkflowWriteCommandsRequested_UsesTypedControlPlaneRequests()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var runtime = new CliConsumerFakeRuntime
        {
            CreateWorkflowAsyncHandler = (request, _) => Task.FromResult(
                new Workflow(
                    request.WorkflowId,
                    new CollaborationSpaceRef(request.CollaborationSpaceId, request.CollaborationSpaceId.Value, "Workflow Write Space"),
                    request.DisplayName,
                    WorkflowState.Draft,
                    request.OwnerParticipant,
                    request.ThreadId)),
            PublishPlanAsyncHandler = (request, _) => Task.FromResult(new PlanProjection(request.WorkflowId, request.Plan)),
            CreateTaskAsyncHandler = (request, _) => Task.FromResult(request.Task),
            UpdateTaskStateAsyncHandler = (request, _) => Task.FromResult<TianShu.Contracts.Workflows.Task?>(
                new TianShu.Contracts.Workflows.Task(
                    request.TaskId,
                    new WorkflowId("workflow-write-sidecar-1"),
                    "Mirror workflow write commands",
                    request.State,
                    request.OwnerParticipant)),
        };

        var createTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "workflow/create",
            ReflectionTestHelper.ParseJsonElement("""{"workflowId":"workflow-write-sidecar-1","spaceId":"space-write-sidecar-1","displayName":"Workflow Write Demo","threadId":"thread-write-sidecar-1","participantId":"participant-write-sidecar-1"}"""),
            CancellationToken.None);
        var createResult = await ReflectionTestHelper.AwaitTaskResultAsync(createTask);
        var createJson = JsonSerializer.Serialize(createResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("Workflow Write Demo", createJson);

        var publishTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "workflow/plan/publish",
            ReflectionTestHelper.ParseJsonElement("""{"workflowId":"workflow-write-sidecar-1","title":"Workflow Write Plan","steps":[{"title":"Mirror workflow write commands","description":"direct and runtime-surface"}]}"""),
            CancellationToken.None);
        var publishResult = await ReflectionTestHelper.AwaitTaskResultAsync(publishTask);
        var publishJson = JsonSerializer.Serialize(publishResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("Workflow Write Plan", publishJson);

        var createWorkflowTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "workflow/task/create",
            ReflectionTestHelper.ParseJsonElement("""{"taskId":"task-write-sidecar-1","workflowId":"workflow-write-sidecar-1","title":"Mirror workflow write commands","state":"in-progress","participantId":"participant-write-sidecar-1"}"""),
            CancellationToken.None);
        var createWorkflowTaskResult = await ReflectionTestHelper.AwaitTaskResultAsync(createWorkflowTask);
        var createWorkflowTaskJson = JsonSerializer.Serialize(createWorkflowTaskResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("Mirror workflow write commands", createWorkflowTaskJson);

        var updateTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "workflow/task/updateState",
            ReflectionTestHelper.ParseJsonElement("""{"taskId":"task-write-sidecar-1","state":"done","participantId":"participant-write-sidecar-1"}"""),
            CancellationToken.None);
        var updateTaskResult = await ReflectionTestHelper.AwaitTaskResultAsync(updateTask);
        var updateTaskJson = JsonSerializer.Serialize(updateTaskResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("Mirror workflow write commands", updateTaskJson);

        var createRequest = Assert.Single(runtime.WorkflowCreateCalls);
        Assert.Equal("workflow-write-sidecar-1", createRequest.WorkflowId.Value);
        Assert.Equal("space-write-sidecar-1", createRequest.CollaborationSpaceId.Value);
        Assert.Equal("thread-write-sidecar-1", createRequest.ThreadId?.Value);
        Assert.Equal("participant-write-sidecar-1", createRequest.OwnerParticipant?.Id.Value);

        var publishRequest = Assert.Single(runtime.WorkflowPublishPlanCalls);
        Assert.Equal("Workflow Write Plan", publishRequest.Plan.Title);
        Assert.Equal("Mirror workflow write commands", Assert.Single(publishRequest.Plan.Steps).Title);

        var createTaskRequest = Assert.Single(runtime.WorkflowCreateTaskCalls);
        Assert.Equal("task-write-sidecar-1", createTaskRequest.Task.Id.Value);
        Assert.Equal(TaskState.InProgress, createTaskRequest.Task.State);

        var updateTaskRequest = Assert.Single(runtime.WorkflowUpdateTaskStateCalls);
        Assert.Equal("task-write-sidecar-1", updateTaskRequest.TaskId.Value);
        Assert.Equal(TaskState.Done, updateTaskRequest.State);
        Assert.Equal("participant-write-sidecar-1", updateTaskRequest.OwnerParticipant?.Id.Value);
    }

    [Fact]
    public async Task InvokeRuntimeSurfaceAsync_WhenAgentProjectionQueriesRequested_UsesTypedControlPlaneRequests()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var agentParticipant = new ParticipantRef(new ParticipantId("participant-1"), ParticipantKind.Agent, "Hermes");
        var runtime = new CliConsumerFakeRuntime
        {
            GetAgentRosterProjectionAsyncHandler = (_, _) => Task.FromResult<AgentRosterProjection?>(
                new AgentRosterProjection(
                [
                    new AgentRosterEntry(
                        new AgentId("agent-1"),
                        agentParticipant,
                        "reviewer",
                        1,
                        true),
                ])),
            GetTeamProjectionAsyncHandler = (query, _) => Task.FromResult<TeamProjection?>(
                new TeamProjection(
                    new Team(query.TeamId, "Team Demo", [new AgentId("agent-1")]),
                    [
                        new Agent(
                            new AgentId("agent-1"),
                            agentParticipant,
                            "Hermes",
                            AgentRole.Reviewer),
                    ])),
        };

        var rosterTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "agent/roster/read",
            ReflectionTestHelper.ParseJsonElement("""{"workflowId":"workflow-agents"}"""),
            CancellationToken.None);
        var rosterResult = await ReflectionTestHelper.AwaitTaskResultAsync(rosterTask);
        var rosterJson = JsonSerializer.Serialize(rosterResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("Hermes", rosterJson);

        var teamTask = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "agent/team/read",
            ReflectionTestHelper.ParseJsonElement("""{"teamId":"team-1"}"""),
            CancellationToken.None);
        var teamResult = await ReflectionTestHelper.AwaitTaskResultAsync(teamTask);
        var teamJson = JsonSerializer.Serialize(teamResult, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("team-1", teamJson);
        Assert.Contains("Hermes", teamJson);

        Assert.Equal("workflow-agents", Assert.Single(runtime.AgentRosterProjectionCalls).WorkflowId?.Value);
        Assert.Equal("team-1", Assert.Single(runtime.TeamProjectionCalls).TeamId.Value);
    }

    [Fact]
    public async Task HandleRequestAsync_WhenInvokeCapabilityRealtimeStartCommandArrives_UsesTypedControlPlaneRequest()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var runtime = new CliConsumerFakeRuntime
        {
            ActiveThreadId = "thread-realtime-cap-1",
        };
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        var request = Activator.CreateInstance(requestType);
        Assert.NotNull(request);
        ReflectionTestHelper.SetProperty(request!, "RequestId", "req-sidecar-realtime-1");
        ReflectionTestHelper.SetProperty(request, "Command", "invokeCapability");
        ReflectionTestHelper.SetProperty(
            request,
            "Payload",
            ReflectionTestHelper.ParseJsonElement(
                """
                {
                  "capability": "realtime",
                  "method": "thread/realtime/start",
                  "parametersJson": "{\"sessionId\":\"session-1\",\"prompt\":\"开始语音协作\"}"
                }
                """));

        var task = ReflectionTestHelper.InvokeMethod(host!, "HandleRequestAsync", request);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        var typedRequest = Assert.Single(runtime.RealtimeStartCalls);
        Assert.Equal("thread-realtime-cap-1", typedRequest.ThreadId.Value);
        Assert.Equal("session-1", typedRequest.SessionId);
        Assert.Equal("开始语音协作", typedRequest.Prompt);

        using var responseDocument = JsonDocument.Parse(Assert.Single(
            output.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)));
        Assert.Equal("response", responseDocument.RootElement.GetProperty("messageType").GetString());
        Assert.True(responseDocument.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task HandleRequestAsync_WhenInvokeCapabilityFuzzyFileSearchCommandsArrive_UseTypedControlPlaneRequests()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var runtime = new CliConsumerFakeRuntime();
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        object BuildRequest(string requestId, string method, string parametersJson)
        {
            var request = Activator.CreateInstance(requestType);
            Assert.NotNull(request);
            ReflectionTestHelper.SetProperty(request!, "RequestId", requestId);
            ReflectionTestHelper.SetProperty(request, "Command", "invokeCapability");
            ReflectionTestHelper.SetProperty(
                request,
                "Payload",
                ReflectionTestHelper.ParseJsonElement(
                    $$"""
                    {
                      "capability": "fuzzyfilesearch",
                      "method": "{{method}}",
                      "parametersJson": {{JsonSerializer.Serialize(parametersJson)}}
                    }
                    """));
            return request!;
        }

        var requests = new[]
        {
            BuildRequest("req-sidecar-fuzzy-search-1", "fuzzyfilesearch", """{"query":"TianShu","cwd":"src","limit":7,"roots":["src","tests"]}"""),
            BuildRequest("req-sidecar-fuzzy-start-1", "fuzzyfilesearch/sessionstart", """{"sessionId":"fuzzy-session-1","cwd":"src"}"""),
            BuildRequest("req-sidecar-fuzzy-update-1", "fuzzyfilesearch/sessionupdate", """{"sessionId":"fuzzy-session-1","query":"Sidecar"}"""),
            BuildRequest("req-sidecar-fuzzy-stop-1", "fuzzyfilesearch/sessionstop", """{"sessionId":"fuzzy-session-1"}"""),
        };

        foreach (var request in requests)
        {
            await ReflectionTestHelper.AwaitTaskResultAsync(ReflectionTestHelper.InvokeMethod(host, "HandleRequestAsync", request));
        }

        var searchRequest = Assert.Single(runtime.FuzzyFileSearchCalls);
        Assert.Equal("TianShu", searchRequest.Query);
        Assert.Equal("src", searchRequest.WorkingDirectory);
        Assert.Equal(7, searchRequest.Limit);
        Assert.Equal(["src", "tests"], searchRequest.Roots);

        var startRequest = Assert.Single(runtime.FuzzyFileSearchSessionStartCalls);
        Assert.Equal("fuzzy-session-1", startRequest.SessionId);
        Assert.Equal(["src"], startRequest.Roots);

        var updateRequest = Assert.Single(runtime.FuzzyFileSearchSessionUpdateCalls);
        Assert.Equal("fuzzy-session-1", updateRequest.SessionId);
        Assert.Equal("Sidecar", updateRequest.Query);

        var stopRequest = Assert.Single(runtime.FuzzyFileSearchSessionStopCalls);
        Assert.Equal("fuzzy-session-1", stopRequest.SessionId);

        var responseLines = output.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(requests.Length, responseLines.Length);
        foreach (var responseLine in responseLines)
        {
            using var responseDocument = JsonDocument.Parse(responseLine);
            Assert.Equal("response", responseDocument.RootElement.GetProperty("messageType").GetString());
            Assert.True(responseDocument.RootElement.GetProperty("success").GetBoolean());
        }
    }

    [Fact]
    public async Task HandleRequestAsync_WhenFeedbackCapabilityArrives_UsesDiagnosticsControlPlaneRequest()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var runtime = new CliConsumerFakeRuntime();
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        var request = Activator.CreateInstance(requestType);
        Assert.NotNull(request);
        ReflectionTestHelper.SetProperty(request!, "RequestId", "req-sidecar-feedback-1");
        ReflectionTestHelper.SetProperty(request, "Command", "invokeCapability");
        ReflectionTestHelper.SetProperty(
            request,
            "Payload",
            ReflectionTestHelper.ParseJsonElement(
                """
                {
                  "capability": "feedback",
                  "method": "upload",
                  "parametersJson": "{\"classification\":\"quality\",\"includeLogs\":true,\"threadId\":\"thread-feedback-1\",\"reason\":\"needs follow-up\",\"extraLogFiles\":[\"D:/logs/a.log\"]}"
                }
                """));

        var task = ReflectionTestHelper.InvokeMethod(host!, "HandleRequestAsync", request);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        Assert.Empty(runtime.GovernanceFeedbackUploadCalls);
        var typedRequest = Assert.Single(runtime.DiagnosticsFeedbackUploadCalls);
        Assert.Equal("quality", typedRequest.Classification);
        Assert.True(typedRequest.IncludeLogs);
        Assert.Equal("thread-feedback-1", typedRequest.ThreadId);
        Assert.Equal("needs follow-up", typedRequest.Reason);
        Assert.Equal("D:/logs/a.log", Assert.Single(typedRequest.ExtraLogFiles));

        using var responseDocument = JsonDocument.Parse(Assert.Single(
            output.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)));
        Assert.Equal("response", responseDocument.RootElement.GetProperty("messageType").GetString());
        Assert.True(responseDocument.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task InvokeRuntimeSurfaceAsync_WhenFeedbackUploadRequested_UsesDiagnosticsControlPlaneRequest()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var runtime = new CliConsumerFakeRuntime();

        var task = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeRuntimeSurfaceAsync",
            runtime,
            "feedback/upload",
            ReflectionTestHelper.ParseJsonElement("""{"classification":"quality","includeLogs":true,"threadId":"thread-feedback-runtime-1","reason":"runtime feedback","extraLogFiles":["D:/logs/runtime.log"]}"""),
            CancellationToken.None);
        var result = await ReflectionTestHelper.AwaitTaskResultAsync(task);
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("threadId", json, StringComparison.Ordinal);

        Assert.Empty(runtime.GovernanceFeedbackUploadCalls);
        var typedRequest = Assert.Single(runtime.DiagnosticsFeedbackUploadCalls);
        Assert.Equal("quality", typedRequest.Classification);
        Assert.True(typedRequest.IncludeLogs);
        Assert.Equal("thread-feedback-runtime-1", typedRequest.ThreadId);
        Assert.Equal("runtime feedback", typedRequest.Reason);
        Assert.Equal("D:/logs/runtime.log", Assert.Single(typedRequest.ExtraLogFiles));
    }

    [Fact]
    public async Task HandleRequestAsync_WhenUploadFeedbackCommandArrives_UsesDiagnosticsControlPlaneRequest()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var runtime = new CliConsumerFakeRuntime();
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        var request = Activator.CreateInstance(requestType);
        Assert.NotNull(request);
        ReflectionTestHelper.SetProperty(request!, "RequestId", "req-sidecar-feedback-direct-1");
        ReflectionTestHelper.SetProperty(request, "Command", "uploadFeedback");
        ReflectionTestHelper.SetProperty(
            request,
            "Payload",
            ReflectionTestHelper.ParseJsonElement(
                """
                {
                  "classification": "quality",
                  "includeLogs": true,
                  "threadId": "thread-feedback-direct-1",
                  "reason": "direct feedback",
                  "extraLogFiles": ["D:/logs/direct.log"]
                }
                """));

        var task = ReflectionTestHelper.InvokeMethod(host!, "HandleRequestAsync", request);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        Assert.Empty(runtime.GovernanceFeedbackUploadCalls);
        var typedRequest = Assert.Single(runtime.DiagnosticsFeedbackUploadCalls);
        Assert.Equal("quality", typedRequest.Classification);
        Assert.True(typedRequest.IncludeLogs);
        Assert.Equal("thread-feedback-direct-1", typedRequest.ThreadId);
        Assert.Equal("direct feedback", typedRequest.Reason);
        Assert.Equal("D:/logs/direct.log", Assert.Single(typedRequest.ExtraLogFiles));

        using var responseDocument = JsonDocument.Parse(Assert.Single(
            output.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)));
        Assert.Equal("response", responseDocument.RootElement.GetProperty("messageType").GetString());
        Assert.True(responseDocument.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task HandleRequestAsync_WhenInvokeRuntimeSurfaceTargetsFormalMethod_UsesFormalDispatchWithoutLegacyRpc()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var legacyRpcCalled = false;
        var runtime = new CliConsumerFakeRuntime
        {
            InvokeDiagnosticRpcAsyncHandler = (_, _, _) =>
            {
                legacyRpcCalled = true;
                throw new InvalidOperationException("formal diagnostics method 不应再回退到 legacy diagnostics RPC。");
            },
        };
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        var request = Activator.CreateInstance(requestType);
        Assert.NotNull(request);
        ReflectionTestHelper.SetProperty(request!, "RequestId", "req-sidecar-diagnostic-rpc-formal-1");
        ReflectionTestHelper.SetProperty(request, "Command", "invokeRuntimeSurface");
        ReflectionTestHelper.SetProperty(
            request,
            "Payload",
            ReflectionTestHelper.ParseJsonElement(
                """
                {
                  "method": "diagnostics/trace/read",
                  "parametersJson": "{\"traceId\":\"trace-rpc-formal-1\"}"
                }
                """));

        var task = ReflectionTestHelper.InvokeMethod(host!, "HandleRequestAsync", request);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        Assert.False(legacyRpcCalled);
        var traceRequest = Assert.Single(runtime.ExecutionTraceCalls);
        Assert.Equal("trace-rpc-formal-1", traceRequest.TraceId.Value);

        using var responseDocument = JsonDocument.Parse(Assert.Single(
            output.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)));
        Assert.Equal("response", responseDocument.RootElement.GetProperty("messageType").GetString());
        Assert.True(responseDocument.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task HandleRequestAsync_WhenInvokeRuntimeSurfaceTargetsKernelOnlyMethod_RejectsWithoutDiagnosticsFallback()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var fallbackCalled = false;
        var runtime = new CliConsumerFakeRuntime
        {
            InvokeDiagnosticRpcAsyncHandler = (_, _, _) =>
            {
                fallbackCalled = true;
                throw new InvalidOperationException("未 formalized 的 tool method 不应落到 diagnostics fallback。");
            },
        };
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        var request = Activator.CreateInstance(requestType);
        Assert.NotNull(request);
        ReflectionTestHelper.SetProperty(request!, "RequestId", "req-sidecar-diagnostic-rpc-legacy-1");
        ReflectionTestHelper.SetProperty(request, "Command", "invokeRuntimeSurface");
        ReflectionTestHelper.SetProperty(
            request,
            "Payload",
            ReflectionTestHelper.ParseJsonElement(
                """
                {
                  "method": "tool/ping",
                  "parametersJson": "{\"ok\":true}"
                }
                """));

        var task = ReflectionTestHelper.InvokeMethod(host!, "HandleRequestAsync", request);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        Assert.False(fallbackCalled);
        Assert.Empty(runtime.ExecutionTraceCalls);

        using var responseDocument = JsonDocument.Parse(Assert.Single(
            output.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)));
        Assert.Equal("response", responseDocument.RootElement.GetProperty("messageType").GetString());
        Assert.False(responseDocument.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(
            "正式 runtime surface",
            responseDocument.RootElement.GetProperty("message").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleRequestAsync_WhenInvokeRuntimeSurfaceTargetsDisallowedLegacyMethod_RejectsBeforeFallback()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var legacyRpcCalled = false;
        var runtime = new CliConsumerFakeRuntime
        {
            InvokeDiagnosticRpcAsyncHandler = (_, _, _) =>
            {
                legacyRpcCalled = true;
                throw new InvalidOperationException("不允许的 legacy RPC method 不应落到 diagnostics fallback。");
            },
        };
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        var request = Activator.CreateInstance(requestType);
        Assert.NotNull(request);
        ReflectionTestHelper.SetProperty(request!, "RequestId", "req-sidecar-diagnostic-rpc-disallowed-1");
        ReflectionTestHelper.SetProperty(request, "Command", "invokeRuntimeSurface");
        ReflectionTestHelper.SetProperty(
            request,
            "Payload",
            ReflectionTestHelper.ParseJsonElement(
                """
                {
                  "method": "diagnostics/ping",
                  "parametersJson": "{\"ok\":true}"
                }
                """));

        var task = ReflectionTestHelper.InvokeMethod(host!, "HandleRequestAsync", request);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        Assert.False(legacyRpcCalled);
        Assert.Empty(runtime.ExecutionTraceCalls);

        using var responseDocument = JsonDocument.Parse(Assert.Single(
            output.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)));
        Assert.Equal("response", responseDocument.RootElement.GetProperty("messageType").GetString());
        Assert.False(responseDocument.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(
            "正式 runtime surface",
            responseDocument.RootElement.GetProperty("message").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeWindowsSandboxSetupAsync_WhenModeProvided_UsesTypedRuntimeRequest()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var runtime = new CliConsumerFakeRuntime
        {
            StartWindowsSandboxSetupAsyncHandler = (request, _) => Task.FromResult(
                new ControlPlaneWindowsSandboxSetupStartResult
                {
                    Started = true,
                }),
        };
        var environment = runtime.AsNorthboundSurface().Environment;

        var parameters = ReflectionTestHelper.ParseJsonElement("""{"mode":"elevated","cwd":"D:/Work/TianShu"}""");
        var task = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeWindowsSandboxSetupAsync",
            environment,
            parameters,
            CancellationToken.None);

        var result = await ReflectionTestHelper.AwaitTaskResultAsync(task);
        var typedResult = Assert.IsType<ControlPlaneWindowsSandboxSetupStartResult>(result);
        Assert.True(typedResult.Started);

        var request = Assert.Single(runtime.WindowsSandboxSetupCalls);
        Assert.Equal(WindowsSandboxSetupMode.Elevated, request.Mode);
        Assert.Equal("D:/Work/TianShu", request.WorkingDirectory);
    }

    [Fact]
    public async Task InvokeCommandExecutionAsync_WhenExecRequested_UsesTypedRuntimeRequest()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var runtime = new CliConsumerFakeRuntime
        {
            StartCommandExecutionAsyncHandler = (request, _) => Task.FromResult(
                new ControlPlaneCommandExecutionResult
                {
                    Started = true,
                    ProcessId = request.ProcessId,
                    Pid = 4321,
                }),
        };
        var execution = runtime.AsNorthboundSurface().Execution;

        var parameters = ReflectionTestHelper.ParseJsonElement(
            """{"cwd":"D:/Work/TianShu","command":["cmd.exe","/c","echo hello"],"processId":"proc-sidecar-1","tty":true,"size":{"rows":24,"cols":80},"streamStdin":true,"streamStdoutStderr":true,"approvalPolicy":"on-request","env":{"DEMO":"1"},"sandbox":{"mode":"workspace-write"}}""");
        var task = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeCommandExecutionAsync",
            execution,
            "exec",
            parameters,
            CancellationToken.None);

        var result = await ReflectionTestHelper.AwaitTaskResultAsync(task);
        var typedResult = Assert.IsType<ControlPlaneCommandExecutionResult>(result);
        Assert.True(typedResult.Started);
        Assert.Equal("proc-sidecar-1", typedResult.ProcessId);

        var request = Assert.Single(runtime.CommandExecutionStartCalls);
        Assert.Equal("D:/Work/TianShu", request.WorkingDirectory);
        Assert.Collection(
            request.CommandArgs,
            item => Assert.Equal("cmd.exe", item),
            item => Assert.Equal("/c", item),
            item => Assert.Equal("echo hello", item));
        Assert.Equal("proc-sidecar-1", request.ProcessId);
        Assert.True(request.Tty);
        Assert.NotNull(request.Size);
        Assert.Equal((ushort)24, request.Size!.Rows);
        Assert.Equal((ushort)80, request.Size.Cols);
        Assert.True(request.StreamStdin);
        Assert.True(request.StreamStdoutStderr);
        Assert.Equal("on-request", request.ApprovalPolicy);
        Assert.Equal("1", request.EnvironmentVariables["DEMO"]);
        Assert.NotNull(request.Sandbox);
        Assert.Equal("workspace-write", request.Sandbox!.Properties["mode"].StringValue);
    }

    [Fact]
    public async Task InvokeCommandExecutionAsync_WhenWriteRequested_UsesTypedRuntimeRequest()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var runtime = new CliConsumerFakeRuntime
        {
            WriteCommandExecutionAsyncHandler = (_, _) => Task.FromResult(new ControlPlaneCommandExecutionCommandAcceptedResult()),
        };
        var execution = runtime.AsNorthboundSurface().Execution;

        var parameters = ReflectionTestHelper.ParseJsonElement("""{"processId":"proc-sidecar-2","deltaBase64":"aGVsbG8=","closeStdin":true}""");
        var task = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeCommandExecutionAsync",
            execution,
            "write",
            parameters,
            CancellationToken.None);

        var result = await ReflectionTestHelper.AwaitTaskResultAsync(task);
        Assert.IsType<ControlPlaneCommandExecutionCommandAcceptedResult>(result);

        var request = Assert.Single(runtime.CommandExecutionWriteCalls);
        Assert.Equal("proc-sidecar-2", request.ProcessId);
        Assert.Equal("aGVsbG8=", request.DeltaBase64);
        Assert.True(request.CloseStdin);
    }

    [Fact]
    public async Task InvokeCommandExecutionAsync_WhenTerminateRequested_UsesTypedRuntimeRequest()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var runtime = new CliConsumerFakeRuntime
        {
            TerminateCommandExecutionAsyncHandler = (_, _) => Task.FromResult(new ControlPlaneCommandExecutionCommandAcceptedResult()),
        };
        var execution = runtime.AsNorthboundSurface().Execution;

        var parameters = ReflectionTestHelper.ParseJsonElement("""{"processId":"proc-sidecar-3"}""");
        var task = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeCommandExecutionAsync",
            execution,
            "terminate",
            parameters,
            CancellationToken.None);

        var result = await ReflectionTestHelper.AwaitTaskResultAsync(task);
        Assert.IsType<ControlPlaneCommandExecutionCommandAcceptedResult>(result);

        var request = Assert.Single(runtime.CommandExecutionTerminateCalls);
        Assert.Equal("proc-sidecar-3", request.ProcessId);
    }

    [Fact]
    public async Task InvokeCommandExecutionAsync_WhenResizeRequested_UsesTypedRuntimeRequest()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var runtime = new CliConsumerFakeRuntime
        {
            ResizeCommandExecutionAsyncHandler = (_, _) => Task.FromResult(new ControlPlaneCommandExecutionCommandAcceptedResult()),
        };
        var execution = runtime.AsNorthboundSurface().Execution;

        var parameters = ReflectionTestHelper.ParseJsonElement("""{"processId":"proc-sidecar-4","size":{"rows":40,"cols":120}}""");
        var task = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeCommandExecutionAsync",
            execution,
            "resize",
            parameters,
            CancellationToken.None);

        var result = await ReflectionTestHelper.AwaitTaskResultAsync(task);
        Assert.IsType<ControlPlaneCommandExecutionCommandAcceptedResult>(result);

        var request = Assert.Single(runtime.CommandExecutionResizeCalls);
        Assert.Equal("proc-sidecar-4", request.ProcessId);
        Assert.Equal((ushort)40, request.Size.Rows);
        Assert.Equal((ushort)120, request.Size.Cols);
    }

    [Fact]
    public async Task InvokeAgentOperationAsync_WhenThreadRegisterRequested_UsesTypedRuntimeRequest_AndReturnsCompatibilityEnvelope()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var runtime = new CliConsumerFakeRuntime
        {
            RegisterAgentThreadAsyncHandler = (request, _) => Task.FromResult(
                new ControlPlaneAgentThreadRegistrationResult
                {
                    Agent = new ControlPlaneAgentDescriptor
                    {
                        ThreadId = request.ThreadId,
                        AgentNickname = request.AgentNickname,
                        AgentRole = request.AgentRole,
                        UpdatedAt = new DateTimeOffset(2026, 4, 9, 0, 0, 0, TimeSpan.Zero),
                    },
                }),
        };

        var parameters = ReflectionTestHelper.ParseJsonElement("""{"threadId":"thread-agent-1","agentNickname":"demo-agent","agentRole":"reviewer"}""");
        var task = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeAgentOperationAsync",
            runtime,
            "agent/thread/register",
            parameters,
            CancellationToken.None);

        var result = await ReflectionTestHelper.AwaitTaskResultAsync(task);
        var typedResult = JsonSerializer.SerializeToElement(result);
        Assert.Equal("thread-agent-1", typedResult.GetProperty("thread").GetProperty("id").GetString());
        Assert.Equal("demo-agent", typedResult.GetProperty("thread").GetProperty("agentNickname").GetString());
        Assert.Equal("reviewer", typedResult.GetProperty("thread").GetProperty("agentRole").GetString());
        Assert.Equal(0, typedResult.GetProperty("thread").GetProperty("turns").GetArrayLength());

        var request = Assert.Single(runtime.AgentThreadRegistrationCalls);
        Assert.Equal("thread-agent-1", request.ThreadId.Value);
        Assert.Equal("demo-agent", request.AgentNickname);
        Assert.Equal("reviewer", request.AgentRole);
    }

    [Fact]
    public async Task InvokeAgentOperationAsync_WhenJobCreateRequested_UsesTypedRuntimeRequest()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var runtime = new CliConsumerFakeRuntime
        {
            CreateAgentJobAsyncHandler = (request, _) => Task.FromResult(
                new ControlPlaneJobOperationResult
                {
                    Job = new ControlPlaneJobDetails
                    {
                        Id = request.JobId ?? new JobId("job-created-1"),
                        Name = request.Name ?? "batch-review",
                        Status = "created",
                        Instruction = request.Instruction,
                    },
                }),
        };

        var parameters = ReflectionTestHelper.ParseJsonElement("""{"jobId":"job-created-1","name":"batch-review","instruction":"Review all files","inputHeaders":{"source":"sheet"},"items":[{"id":"a"},{"id":"b"}],"autoExport":true}""");
        var task = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeAgentOperationAsync",
            runtime,
            "agent/job/create",
            parameters,
            CancellationToken.None);

        var result = await ReflectionTestHelper.AwaitTaskResultAsync(task);
        using (var document = JsonDocument.Parse(JsonSerializer.Serialize(result)))
        {
            Assert.Equal("job-created-1", document.RootElement.GetProperty("job").GetProperty("id").GetString());
            Assert.Equal("batch-review", document.RootElement.GetProperty("job").GetProperty("name").GetString());
            Assert.Equal("Review all files", document.RootElement.GetProperty("job").GetProperty("instruction").GetString());
        }

        var request = Assert.Single(runtime.AgentJobCreateCalls);
        Assert.Equal("job-created-1", request.JobId?.Value);
        Assert.Equal("batch-review", request.Name);
        Assert.Equal("Review all files", request.Instruction);
        Assert.True(request.AutoExport);
        Assert.Equal(2, request.Items.Count);
    }

    [Fact]
    public async Task InvokeAgentOperationAsync_WhenJobDispatchRequested_UsesTypedRuntimeRequest()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var runtime = new CliConsumerFakeRuntime
        {
            DispatchAgentJobAsyncHandler = (request, _) => Task.FromResult(
                new ControlPlaneJobOperationResult
                {
                    Items =
                    [
                        new ControlPlaneJobItemDetails
                        {
                            ItemId = new JobItemId("item-1"),
                            AssignedThreadId = request.ThreadIds[0],
                            Status = "running",
                        },
                    ],
                }),
        };

        var parameters = ReflectionTestHelper.ParseJsonElement("""{"jobId":"job-1","threadIds":["thread-a","THREAD-A","thread-b"]}""");
        var task = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeAgentOperationAsync",
            runtime,
            "agent/job/dispatch",
            parameters,
            CancellationToken.None);

        var result = await ReflectionTestHelper.AwaitTaskResultAsync(task);
        using (var document = JsonDocument.Parse(JsonSerializer.Serialize(result)))
        {
            Assert.Equal("item-1", document.RootElement.GetProperty("items")[0].GetProperty("itemId").GetString());
            Assert.Equal("thread-a", document.RootElement.GetProperty("items")[0].GetProperty("assignedThreadId").GetString());
            Assert.Equal("running", document.RootElement.GetProperty("items")[0].GetProperty("status").GetString());
        }

        var request = Assert.Single(runtime.AgentJobDispatchCalls);
        Assert.Equal("job-1", request.JobId.Value);
        Assert.Equal(new[] { "thread-a", "thread-b" }, request.ThreadIds.Select(static item => item.Value).ToArray());
    }

    [Fact]
    public async Task InvokeAgentOperationAsync_WhenJobItemReportRequested_UsesTypedRuntimeRequest()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var runtime = new CliConsumerFakeRuntime
        {
            ReportAgentJobItemAsyncHandler = (request, _) => Task.FromResult(
                new ControlPlaneJobOperationResult
                {
                    Item = new ControlPlaneJobItemDetails
                    {
                        ItemId = request.ItemId,
                        Status = request.Status,
                        Result = request.Result,
                    },
                }),
        };

        var parameters = ReflectionTestHelper.ParseJsonElement("""{"jobId":"job-1","itemId":"item-1","status":"completed","result":{"score":99},"lastError":"none"}""");
        var task = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeAgentOperationAsync",
            runtime,
            "agent/job/item/report",
            parameters,
            CancellationToken.None);

        var result = await ReflectionTestHelper.AwaitTaskResultAsync(task);
        using (var document = JsonDocument.Parse(JsonSerializer.Serialize(result)))
        {
            Assert.Equal("item-1", document.RootElement.GetProperty("item").GetProperty("itemId").GetString());
            Assert.Equal("completed", document.RootElement.GetProperty("item").GetProperty("status").GetString());
            Assert.Equal(99, document.RootElement.GetProperty("item").GetProperty("result").GetProperty("score").GetInt32());
        }

        var request = Assert.Single(runtime.AgentJobItemReportCalls);
        Assert.Equal("job-1", request.JobId.Value);
        Assert.Equal("item-1", request.ItemId.Value);
        Assert.Equal("completed", request.Status);
        Assert.NotNull(request.Result);
        using (var document = JsonDocument.Parse(JsonSerializer.Serialize(request.Result.ToPlainObject())))
        {
            Assert.Equal(99, document.RootElement.GetProperty("score").GetInt32());
        }
        Assert.Equal("none", request.LastError);
    }

    [Fact]
    public async Task InvokeAgentOperationAsync_WhenJobReadRequested_UsesTypedRuntimeRequest()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var runtime = new CliConsumerFakeRuntime
        {
            ReadAgentJobAsyncHandler = (request, _) => Task.FromResult(
                new ControlPlaneJobOperationResult
                {
                    Job = new ControlPlaneJobDetails
                    {
                        Id = request.JobId,
                        Name = "readback-job",
                        Status = "running",
                        Instruction = "Read back status",
                    },
                }),
        };

        var parameters = ReflectionTestHelper.ParseJsonElement("""{"jobId":"job-read-1"}""");
        var task = ReflectionTestHelper.InvokeStaticMethod(
            hostType,
            "InvokeAgentOperationAsync",
            runtime,
            "agent/job/read",
            parameters,
            CancellationToken.None);

        var result = await ReflectionTestHelper.AwaitTaskResultAsync(task);
        using (var document = JsonDocument.Parse(JsonSerializer.Serialize(result)))
        {
            Assert.Equal("job-read-1", document.RootElement.GetProperty("job").GetProperty("id").GetString());
            Assert.Equal("running", document.RootElement.GetProperty("job").GetProperty("status").GetString());
        }

        var request = Assert.Single(runtime.AgentJobReadCalls);
        Assert.Equal("job-read-1", request.JobId.Value);
    }

    [Fact]
    public async Task HandleResumeThreadAsync_WhenPendingInteractiveRequestsExist_WritesTypedReplayPayload()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var runtime = new CliConsumerFakeRuntime
        {
            ResumeThreadAsyncHandler = static (_, _) => Task.FromResult<ControlPlaneThreadSnapshot?>(new ControlPlaneThreadSnapshot
            {
                Thread = new ControlPlaneThreadSummary
                {
                    ThreadId = new ThreadId("thread-resume-sidecar-1"),
                    Preview = "恢复 typed interactive replay",
                },
                PendingInteractiveRequests =
                [
                    new ControlPlanePendingInteractiveRequest
                    {
                        RequestId = 901,
                        RequestKind = "approval_requested",
                        RequestMethod = "item/tool/requestApproval",
                        CallId = "approval-resume-sidecar-1",
                        ThreadId = "thread-resume-sidecar-1",
                        TurnId = "turn-resume-sidecar-1",
                        ToolName = "browser",
                        Text = "browser | open https://example.com | mcp_tool_call_requires_approval",
                        Status = "awaitingApproval",
                        Phase = "request_approval",
                        RequiresApproval = true,
                        ApprovalKind = "tool",
                        AvailableDecisions = ["accept", "decline"],
                        AvailableDecisionOptions =
                        [
                            new ControlPlaneApprovalDecisionOption("accept"),
                            new ControlPlaneApprovalDecisionOption("decline"),
                        ],
                    },
                    new ControlPlanePendingInteractiveRequest
                    {
                        RequestId = 902,
                        RequestKind = "permission_requested",
                        RequestMethod = "item/permissions/requestApproval",
                        CallId = "permission-resume-sidecar-1",
                        ThreadId = "thread-resume-sidecar-1",
                        TurnId = "turn-resume-sidecar-1",
                        ToolName = "request_permissions",
                        Text = "Need broader access | {\"network\":{\"enabled\":true}}",
                        Status = "awaitingPermission",
                        Phase = "request_permission",
                    },
                    new ControlPlanePendingInteractiveRequest
                    {
                        RequestId = 903,
                        RequestKind = "request_user_input",
                        RequestMethod = "item/tool/requestUserInput",
                        CallId = "input-resume-sidecar-1",
                        ThreadId = "thread-resume-sidecar-1",
                        TurnId = "turn-resume-sidecar-1",
                        ToolName = "requestUserInput",
                        Text = "- 选择配置文件",
                        Status = "awaitingUserInput",
                        Phase = "request_user_input",
                    },
                ],
            }),
        };
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        var request = Activator.CreateInstance(requestType);
        Assert.NotNull(request);
        ReflectionTestHelper.SetProperty(request!, "RequestId", "req-resume-sidecar-1");
        ReflectionTestHelper.SetProperty(request, "Command", "resumeThread");
        ReflectionTestHelper.SetProperty(
            request,
            "Payload",
            ReflectionTestHelper.ParseJsonElement("""{"threadId":"thread-resume-sidecar-1"}"""));

        var task = ReflectionTestHelper.InvokeMethod(host!, "HandleResumeThreadAsync", "req-resume-sidecar-1", request);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        var responseLine = output
            .ToString()
            .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => JsonDocument.Parse(line))
            .First(static document => document.RootElement.GetProperty("messageType").GetString() == "response");

        using (responseLine)
        {
            var root = responseLine.RootElement;
            Assert.True(root.GetProperty("success").GetBoolean());
            var data = root.GetProperty("data");
            Assert.Equal("thread-resume-sidecar-1", data.GetProperty("threadId").GetString());
            Assert.False(data.TryGetProperty("messages", out _));

            var requests = data.GetProperty("pendingInteractiveRequests");
            Assert.Equal(3, requests.GetArrayLength());

            var approval = requests.EnumerateArray().Single(static item => item.GetProperty("requestId").GetInt64() == 901);
            Assert.Equal("approval_requested", approval.GetProperty("requestKind").GetString());
            Assert.Equal("tool", approval.GetProperty("approvalKind").GetString());
            Assert.Equal("accept", approval.GetProperty("availableDecisionOptions")[0].GetProperty("type").GetString());
            Assert.Equal(
                "browser | open https://example.com | mcp_tool_call_requires_approval",
                approval.GetProperty("text").GetString());

            var permission = requests.EnumerateArray().Single(static item => item.GetProperty("requestId").GetInt64() == 902);
            Assert.Equal("permission_requested", permission.GetProperty("requestKind").GetString());
            Assert.Equal(
                "Need broader access | {\"network\":{\"enabled\":true}}",
                permission.GetProperty("text").GetString());

            var userInput = requests.EnumerateArray().Single(static item => item.GetProperty("requestId").GetInt64() == 903);
            Assert.Equal("request_user_input", userInput.GetProperty("requestKind").GetString());
            Assert.Equal(
                "- 选择配置文件",
                userInput.GetProperty("text").GetString());
        }
    }

    [Fact]
    public async Task HandleResumeThreadAsync_WhenPendingInteractiveRequestUsesStringRequestId_WritesRawRequestId()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var runtime = new CliConsumerFakeRuntime
        {
            ResumeThreadAsyncHandler = static (_, _) => Task.FromResult<ControlPlaneThreadSnapshot?>(new ControlPlaneThreadSnapshot
            {
                Thread = new ControlPlaneThreadSummary
                {
                    ThreadId = new ThreadId("thread-resume-sidecar-string-1"),
                    Preview = "恢复 typed interactive replay（字符串 requestId）",
                },
                PendingInteractiveRequests =
                [
                    new ControlPlanePendingInteractiveRequest
                    {
                        RequestId = 0,
                        RequestIdRaw = "permission-resume-sidecar-string-1",
                        RequestKind = "permission_requested",
                        RequestMethod = "item/permissions/requestApproval",
                        CallId = "permission-call-resume-sidecar-string-1",
                        ThreadId = "thread-resume-sidecar-string-1",
                        TurnId = "turn-resume-sidecar-string-1",
                        ToolName = "request_permissions",
                        Text = "Need broader access | {\"network\":{\"enabled\":true}}",
                        Status = "awaitingPermission",
                        Phase = "request_permission",
                    },
                ],
            }),
        };
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        var request = Activator.CreateInstance(requestType);
        Assert.NotNull(request);
        ReflectionTestHelper.SetProperty(request!, "RequestId", "req-resume-sidecar-string-1");
        ReflectionTestHelper.SetProperty(request, "Command", "resumeThread");
        ReflectionTestHelper.SetProperty(
            request,
            "Payload",
            ReflectionTestHelper.ParseJsonElement("""{"threadId":"thread-resume-sidecar-string-1"}"""));

        var task = ReflectionTestHelper.InvokeMethod(host!, "HandleResumeThreadAsync", "req-resume-sidecar-string-1", request);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        var responseLine = output
            .ToString()
            .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => JsonDocument.Parse(line))
            .First(static document => document.RootElement.GetProperty("messageType").GetString() == "response");

        using (responseLine)
        {
            var requests = responseLine.RootElement.GetProperty("data").GetProperty("pendingInteractiveRequests");
            var permission = Assert.Single(requests.EnumerateArray());
            Assert.Equal("permission-resume-sidecar-string-1", permission.GetProperty("requestId").GetString());
            Assert.Equal("permission_requested", permission.GetProperty("requestKind").GetString());
            Assert.Equal("permission-call-resume-sidecar-string-1", permission.GetProperty("callId").GetString());
        }
    }

    [Fact]
    public async Task HandleResumeThreadAsync_WhenReplayMessagesExist_WritesAuthoritativeMessagesPayload()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var runtime = new CliConsumerFakeRuntime
        {
            ResumeThreadAsyncHandler = static (_, _) => Task.FromResult<ControlPlaneThreadSnapshot?>(new ControlPlaneThreadSnapshot
            {
                Thread = new ControlPlaneThreadSummary
                {
                    ThreadId = new ThreadId("thread-resume-authoritative-1"),
                    Preview = "恢复 authoritative messages",
                },
                Messages =
                [
                    new ControlPlaneConversationMessage
                    {
                        Role = ControlPlaneConversationRole.User,
                        Content = "[mention:$worker-1](app://worker-1)\n请继续处理",
                        ContentItems =
                        [
                            new ControlPlaneMentionInput("$worker-1", "app://worker-1"),
                            new ControlPlaneTextInput("请继续处理"),
                        ],
                    },
                    new ControlPlaneConversationMessage
                    {
                        Role = ControlPlaneConversationRole.Assistant,
                        Content = "我会先补齐 typed replay。",
                    },
                ],
            }),
        };
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        var request = Activator.CreateInstance(requestType);
        Assert.NotNull(request);
        ReflectionTestHelper.SetProperty(request!, "RequestId", "req-resume-authoritative-1");
        ReflectionTestHelper.SetProperty(request, "Command", "resumeThread");
        ReflectionTestHelper.SetProperty(
            request,
            "Payload",
            ReflectionTestHelper.ParseJsonElement("""{"threadId":"thread-resume-authoritative-1"}"""));

        var task = ReflectionTestHelper.InvokeMethod(host!, "HandleResumeThreadAsync", "req-resume-authoritative-1", request);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        var responseLine = output
            .ToString()
            .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => JsonDocument.Parse(line))
            .First(static document => document.RootElement.GetProperty("messageType").GetString() == "response");

        using (responseLine)
        {
            var data = responseLine.RootElement.GetProperty("data");
            Assert.True(data.GetProperty("messagesAreAuthoritative").GetBoolean());
            var messages = data.GetProperty("messages");
            Assert.Equal(2, messages.GetArrayLength());
            var userMessage = messages[0];
            Assert.Equal("user", userMessage.GetProperty("role").GetString());
            Assert.Equal("[mention:$worker-1](app://worker-1)\n请继续处理", userMessage.GetProperty("content").GetString());
            var inputs = userMessage.GetProperty("inputs");
            Assert.Equal(2, inputs.GetArrayLength());
            Assert.Equal("mention", inputs[0].GetProperty("type").GetString());
            Assert.Equal("$worker-1", inputs[0].GetProperty("name").GetString());
            Assert.Equal("text", inputs[1].GetProperty("type").GetString());
            Assert.Equal("请继续处理", inputs[1].GetProperty("text").GetString());
        }
    }

    [Fact]
    public async Task HandleResumeThreadAsync_WhenThreadMetadataExists_WritesTypedThreadSummaryFields()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var runtime = new CliConsumerFakeRuntime
        {
            ResumeThreadAsyncHandler = static (_, _) => Task.FromResult<ControlPlaneThreadSnapshot?>(new ControlPlaneThreadSnapshot
            {
                Thread = new ControlPlaneThreadSummary
                {
                    ThreadId = new ThreadId("thread-resume-summary-1"),
                    Preview = "恢复 typed summary",
                    Name = "恢复会话",
                    WorkingDirectory = "D:/repo",
                    Path = "D:/sessions/thread-resume-summary-1.jsonl",
                    ModelProvider = "openai",
                    Source = ControlPlaneThreadSourceKind.AppServer,
                    CliVersion = "0.1.0",
                    AgentNickname = "Halley",
                    AgentRole = "architect",
                    CreatedAt = DateTimeOffset.FromUnixTimeSeconds(1774160000),
                    UpdatedAt = DateTimeOffset.FromUnixTimeSeconds(1774160300),
                    IsEphemeral = true,
                    Status = "active",
                    ActiveFlags = ["running"],
                    GitSha = "abc123",
                    GitBranch = "main",
                    GitOriginUrl = "https://example.com/repo.git",
                },
            }),
        };
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        var request = Activator.CreateInstance(requestType);
        Assert.NotNull(request);
        ReflectionTestHelper.SetProperty(request!, "RequestId", "req-resume-summary-1");
        ReflectionTestHelper.SetProperty(request, "Command", "resumeThread");
        ReflectionTestHelper.SetProperty(
            request,
            "Payload",
            ReflectionTestHelper.ParseJsonElement("""{"threadId":"thread-resume-summary-1"}"""));

        var task = ReflectionTestHelper.InvokeMethod(host!, "HandleResumeThreadAsync", "req-resume-summary-1", request);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        var responseLine = output
            .ToString()
            .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => JsonDocument.Parse(line))
            .First(static document => document.RootElement.GetProperty("messageType").GetString() == "response");

        using (responseLine)
        {
            var data = responseLine.RootElement.GetProperty("data");
            Assert.Equal("openai", data.GetProperty("modelProvider").GetString());
            Assert.Equal("appServer", data.GetProperty("source").GetString());
            Assert.Equal("0.1.0", data.GetProperty("cliVersion").GetString());
            Assert.Equal("Halley", data.GetProperty("agentNickname").GetString());
            Assert.Equal("architect", data.GetProperty("agentRole").GetString());
            Assert.Equal(1774160000, data.GetProperty("createdAt").GetInt64());
            Assert.Equal(1774160300, data.GetProperty("updatedAt").GetInt64());
            Assert.Equal("active", data.GetProperty("status").GetProperty("type").GetString());
            Assert.Equal("running", data.GetProperty("status").GetProperty("activeFlags")[0].GetString());
            Assert.Equal("abc123", data.GetProperty("gitInfo").GetProperty("sha").GetString());
            Assert.Equal("main", data.GetProperty("gitInfo").GetProperty("branch").GetString());
        }
    }

    [Fact]
    public void BuildThreadStartCommand_WhenPayloadCarriesTypedOverrides_MapsTypedCommand()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var payloadType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StartNewThreadPayload");
        var payload = JsonSerializer.Deserialize(
            """
            {
              "model": "gpt-5.4",
              "modelProvider": "openai",
              "serviceTier": null,
              "workingDirectory": "D:/typed/thread-start",
              "approvalPolicy": {
                "granular": {
                  "sandbox_approval": true,
                  "rules": false,
                  "skill_approval": true,
                  "request_permissions": false,
                  "mcp_elicitations": true
                }
              },
              "sandboxMode": "danger-full-access",
              "config": {
                "sandbox_workspace_write": {
                  "writable_roots": [
                    "D:/typed/write"
                  ]
                }
              },
              "serviceName": "tianshu-vs",
              "baseInstructions": "保持输出精炼。",
              "developerInstructions": "typed-first only",
              "personality": "pragmatic",
              "ephemeral": true,
              "dynamicTools": [
                {
                  "name": "mcp__calendar__find_events",
                  "description": "搜索日历事件。",
                  "inputSchema": {
                    "type": "object"
                  }
                }
              ]
            }
            """,
            payloadType,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(payload);

        var request = Assert.IsType<ControlPlaneStartThreadCommand>(
            ReflectionTestHelper.InvokeStaticMethod(hostType, "BuildThreadStartCommand", payload!));
        Assert.Equal("gpt-5.4", request.Model);
        Assert.Equal("openai", request.ModelProvider);
        Assert.Equal("null", request.ServiceTier);
        Assert.NotNull(request.ApprovalPolicy);
        using (var approvalDocument = JsonDocument.Parse(request.ApprovalPolicy!))
        {
            Assert.False(approvalDocument.RootElement.GetProperty("granular").GetProperty("request_permissions").GetBoolean());
        }
        Assert.NotNull(request.Configuration);
        var writableRoots = request.Configuration!["sandbox_workspace_write"].GetProperty("writable_roots");
        Assert.Equal(StructuredValueKind.Array, writableRoots.Kind);
        Assert.Equal("D:/typed/write", Assert.Single(writableRoots.Items).StringValue);
        Assert.Equal("pragmatic", request.Personality);
        Assert.True(request.Ephemeral);
        Assert.Equal("mcp__calendar__find_events", Assert.Single(request.DynamicTools!).Name);
    }

    [Fact]
    public void SidecarThreadPayloadModels_WhenTypedCarriersAreInternalized_UseSidecarLocalTypes()
    {
        var threadRequestPayloadBaseType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.ThreadRequestPayloadBase");
        var startNewThreadPayloadType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StartNewThreadPayload");
        var resumeThreadPayloadType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.ResumeThreadPayload");

        Assert.Equal(
            "TianShu.VSSDK.Sidecar.SidecarServiceTierOverride",
            threadRequestPayloadBaseType.GetProperty("ServiceTier")!.PropertyType.FullName);
        Assert.Equal(
            "TianShu.VSSDK.Sidecar.SidecarApprovalPolicy",
            threadRequestPayloadBaseType.GetProperty("ApprovalPolicy")!.PropertyType.FullName);
        Assert.Equal(
            "TianShu.VSSDK.Sidecar.SidecarPersonality",
            startNewThreadPayloadType.GetProperty("Personality")!.PropertyType.FullName);
        Assert.Equal(
            "TianShu.VSSDK.Sidecar.SidecarPersonality",
            resumeThreadPayloadType.GetProperty("Personality")!.PropertyType.FullName);
        Assert.Equal(
            "TianShu.VSSDK.Sidecar.SidecarThreadHistoryItem",
            resumeThreadPayloadType.GetProperty("History")!.PropertyType.GenericTypeArguments[0].FullName);
    }

    [Fact]
    public void SidecarApprovalPayloadModels_WhenTypedAmendmentsAreInternalized_UseSidecarLocalTypes()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var respondApprovalPayloadType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.RespondApprovalPayload");
        var approvalRequestPayloadType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarApprovalRequestPayload");
        var approvalDecisionOptionPayloadType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarApprovalDecisionOptionPayload");
        var execPolicyPayloadType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarExecPolicyAmendmentPayload");
        var networkPolicyPayloadType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarNetworkPolicyAmendmentPayload");

        Assert.Equal(
            "TianShu.VSSDK.Sidecar.SidecarExecPolicyAmendmentPayload",
            respondApprovalPayloadType.GetProperty("ExecPolicyAmendment")!.PropertyType.FullName);
        Assert.Equal(
            "TianShu.VSSDK.Sidecar.SidecarNetworkPolicyAmendmentPayload",
            respondApprovalPayloadType.GetProperty("NetworkPolicyAmendment")!.PropertyType.FullName);
        Assert.Equal(
            "TianShu.VSSDK.Sidecar.SidecarExecPolicyAmendmentPayload",
            approvalRequestPayloadType.GetProperty("ProposedExecPolicyAmendment")!.PropertyType.FullName);
        Assert.Equal(
            "TianShu.VSSDK.Sidecar.SidecarNetworkPolicyAmendmentPayload",
            approvalRequestPayloadType.GetProperty("ProposedNetworkPolicyAmendments")!.PropertyType.GetElementType()!.FullName);
        Assert.Equal(
            approvalRequestPayloadType,
            hostType.GetMethod("BuildSidecarApprovalRequestPayload", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetParameters()[0]
                .ParameterType);
        Assert.Equal(
            approvalDecisionOptionPayloadType,
            hostType.GetMethod("BuildSidecarApprovalDecisionOptions", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetParameters()[0]
                .ParameterType
                .GenericTypeArguments[0]);
        Assert.Equal(
            execPolicyPayloadType,
            hostType.GetMethod("BuildSidecarExecPolicyAmendment", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetParameters()[0]
                .ParameterType);
        Assert.Equal(
            networkPolicyPayloadType,
            hostType.GetMethod("BuildSidecarNetworkPolicyAmendment", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetParameters()[0]
                .ParameterType);
    }

    [Fact]
    public async Task HandleRequestAsync_WhenRespondApprovalCommandCarriesExecPolicyAmendment_UsesTypedRuntimeRequest()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var runtime = new CliConsumerFakeRuntime();
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        var request = Activator.CreateInstance(requestType);
        Assert.NotNull(request);
        ReflectionTestHelper.SetProperty(request!, "RequestId", "req-respond-approval-exec-1");
        ReflectionTestHelper.SetProperty(request, "Command", "respondApproval");
        ReflectionTestHelper.SetProperty(
            request,
            "Payload",
            ReflectionTestHelper.ParseJsonElement(
                """
                {
                  "callId": "approval-sidecar-exec-001",
                  "decision": "acceptWithExecPolicyAmendment",
                  "note": "仅允许 git status",
                  "execPolicyAmendment": {
                    "commandPrefix": ["git", "status"]
                  }
                }
                """));

        var task = ReflectionTestHelper.InvokeMethod(host!, "HandleRequestAsync", request);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        var resolution = Assert.Single(runtime.ApprovalResponses);
        Assert.Equal("approval-sidecar-exec-001", resolution.CallId.Value);
        Assert.Equal("host-approval-approval-sidecar-exec-001", resolution.Envelope?.Id.Value);
        Assert.Equal("sidecar", resolution.Envelope?.Source.Surface);
        Assert.Equal("approval_response", Assert.IsType<StructuredInteractionItem>(Assert.Single(resolution.Envelope!.Items)).SemanticKind);
        Assert.Equal(ControlPlaneApprovalDecision.ApproveWithExecutionPolicyAmendment, resolution.Decision);
        Assert.Equal("仅允许 git status", resolution.Note);
        Assert.Collection(
            resolution.CommandPrefix,
            item => Assert.Equal("git", item),
            item => Assert.Equal("status", item));
        Assert.Null(resolution.NetworkHost);
        Assert.Null(resolution.NetworkAction);

        var responseLine = output
            .ToString()
            .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => JsonDocument.Parse(line))
            .First(static document => document.RootElement.GetProperty("messageType").GetString() == "response");

        using (responseLine)
        {
            var root = responseLine.RootElement;
            Assert.True(root.GetProperty("success").GetBoolean());
            Assert.Equal(
                "acceptWithExecpolicyAmendment",
                root.GetProperty("data").GetProperty("decision").GetString());
        }
    }

    [Fact]
    public async Task HandleRequestAsync_WhenRespondApprovalCommandCarriesNetworkPolicyAmendment_UsesTypedRuntimeRequest()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var runtime = new CliConsumerFakeRuntime();
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        var request = Activator.CreateInstance(requestType);
        Assert.NotNull(request);
        ReflectionTestHelper.SetProperty(request!, "RequestId", "req-respond-approval-network-1");
        ReflectionTestHelper.SetProperty(request, "Command", "respondApproval");
        ReflectionTestHelper.SetProperty(
            request,
            "Payload",
            ReflectionTestHelper.ParseJsonElement(
                """
                {
                  "callId": "approval-sidecar-network-001",
                  "decision": "applyNetworkPolicyAmendment",
                  "networkPolicyAmendment": {
                    "host": "api.example.com",
                    "action": "allow"
                  }
                }
                """));

        var task = ReflectionTestHelper.InvokeMethod(host!, "HandleRequestAsync", request);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        var resolution = Assert.Single(runtime.ApprovalResponses);
        Assert.Equal("approval-sidecar-network-001", resolution.CallId.Value);
        Assert.Equal("host-approval-approval-sidecar-network-001", resolution.Envelope?.Id.Value);
        Assert.Equal("approval_response", Assert.IsType<StructuredInteractionItem>(Assert.Single(resolution.Envelope!.Items)).SemanticKind);
        Assert.Equal(ControlPlaneApprovalDecision.ApplyNetworkPolicyAmendment, resolution.Decision);
        Assert.Equal("api.example.com", resolution.NetworkHost);
        Assert.Equal("allow", resolution.NetworkAction);
        Assert.Empty(resolution.CommandPrefix);

        var responseLine = output
            .ToString()
            .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => JsonDocument.Parse(line))
            .First(static document => document.RootElement.GetProperty("messageType").GetString() == "response");

        using (responseLine)
        {
            var root = responseLine.RootElement;
            Assert.True(root.GetProperty("success").GetBoolean());
            Assert.Equal(
                "applyNetworkPolicyAmendment",
                root.GetProperty("data").GetProperty("decision").GetString());
        }
    }

    [Fact]
    public async Task RelayRuntimeEventAsync_WhenApprovalRequestedCarriesAmendments_WritesTypedApprovalPayload()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var streamEvent = CreateStreamEvent(
            ControlPlaneConversationStreamEventKind.ApprovalRequested,
            threadId: "thread-approval-1",
            turnId: "turn-approval-1",
            callId: "approval-call-1",
            toolName: "shell_command",
            message: "等待审批",
            requiresApproval: true,
            approvalKind: "command_execution",
            availableDecisions: ["acceptWithExecPolicyAmendment", "applyNetworkPolicyAmendment", "decline"],
            availableDecisionOptions:
            [
                new ControlPlaneApprovalDecisionOption(
                    "acceptWithExecPolicyAmendment",
                    new ControlPlaneExecPolicyAmendment(["git", "status"])),
                new ControlPlaneApprovalDecisionOption(
                    "applyNetworkPolicyAmendment",
                    null,
                    new ControlPlaneNetworkPolicyAmendment("api.example.com", "allow")),
            ],
            payloadKind: ControlPlaneConversationStreamPayloadKind.ApprovalRequest,
            payload: new ApprovalRequestPayload
            {
                ToolName = "shell_command",
                ApprovalKind = "command_execution",
                AvailableDecisions = ["acceptWithExecPolicyAmendment", "applyNetworkPolicyAmendment", "decline"],
                Summary = "需要确认命令执行。",
                MetadataFields =
                [
                    new ApprovalMetadataFieldPayload
                    {
                        Key = "cwd",
                        ValueType = "string",
                        ValueText = "D:/Work/TianShu",
                    },
                ],
                AvailableDecisionOptions =
                [
                    new ApprovalDecisionOptionPayload
                    {
                        Type = "acceptWithExecPolicyAmendment",
                        ExecPolicyAmendment = new ExecPolicyAmendmentPayload
                        {
                            CommandPrefix = ["git", "status"],
                        },
                    },
                    new ApprovalDecisionOptionPayload
                    {
                        Type = "applyNetworkPolicyAmendment",
                        NetworkPolicyAmendment = new NetworkPolicyAmendmentPayload
                        {
                            Host = "api.example.com",
                            Action = "allow",
                        },
                    },
                ],
                ProposedExecPolicyAmendment = new ExecPolicyAmendmentPayload
                {
                    CommandPrefix = ["git", "status"],
                },
                ProposedNetworkPolicyAmendments =
                [
                    new NetworkPolicyAmendmentPayload
                    {
                        Host = "api.example.com",
                        Action = "allow",
                    },
                ],
            });

        var task = ReflectionTestHelper.InvokeMethod(host!, "RelayRuntimeEventAsync", streamEvent);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        var eventLine = output
            .ToString()
            .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => JsonDocument.Parse(line))
            .First(static document => document.RootElement.GetProperty("messageType").GetString() == "event");

        using (eventLine)
        {
            var root = eventLine.RootElement;
            Assert.Equal("approval_requested", root.GetProperty("eventType").GetString());

            var data = root.GetProperty("data");
            Assert.Equal("acceptWithExecPolicyAmendment", data.GetProperty("availableDecisionOptions")[0].GetProperty("type").GetString());
            Assert.Equal("git", data.GetProperty("availableDecisionOptions")[0].GetProperty("execPolicyAmendment").GetProperty("commandPrefix")[0].GetString());
            Assert.Equal("api.example.com", data.GetProperty("availableDecisionOptions")[1].GetProperty("networkPolicyAmendment").GetProperty("host").GetString());

            var approvalRequest = data.GetProperty("approvalRequest");
            Assert.Equal("shell_command", approvalRequest.GetProperty("toolName").GetString());
            Assert.Equal("cwd", approvalRequest.GetProperty("metadataFields")[0].GetProperty("key").GetString());
            Assert.Equal("git", approvalRequest.GetProperty("proposedExecPolicyAmendment").GetProperty("commandPrefix")[0].GetString());
            Assert.Equal(
                "api.example.com",
                approvalRequest.GetProperty("proposedNetworkPolicyAmendments")[0].GetProperty("host").GetString());
        }
    }

    [Fact]
    public void SidecarInteractiveStateBuilderMethods_WhenTypedCarriersAreInternalized_ReturnSidecarLocalTypes()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var permissionPayloadType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarPermissionRequestPayload");
        var userInputPayloadType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarUserInputRequestPayload");
        var serverRequestResolvedPayloadType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarServerRequestResolvedPayload");
        var pendingFollowUpPayloadType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarPendingFollowUpPayload");
        var pendingInputStatePayloadType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarPendingInputStatePayload");

        Assert.Equal(
            permissionPayloadType,
            hostType.GetMethod("BuildSidecarPermissionRequestPayload", BindingFlags.Static | BindingFlags.NonPublic)!.ReturnType);
        Assert.Equal(
            permissionPayloadType,
            hostType.GetMethod("BuildSidecarPermissionRequestPayload", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetParameters()[0]
                .ParameterType);
        Assert.Equal(
            userInputPayloadType,
            hostType.GetMethod("BuildSidecarUserInputRequestPayload", BindingFlags.Static | BindingFlags.NonPublic)!.ReturnType);
        Assert.Equal(
            userInputPayloadType,
            hostType.GetMethod("BuildSidecarUserInputRequestPayload", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetParameters()[0]
                .ParameterType);
        Assert.Equal(
            serverRequestResolvedPayloadType,
            hostType.GetMethod("BuildSidecarServerRequestResolvedPayload", BindingFlags.Static | BindingFlags.NonPublic)!.ReturnType);
        Assert.Equal(
            serverRequestResolvedPayloadType,
            hostType.GetMethod("BuildSidecarServerRequestResolvedPayload", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetParameters()[0]
                .ParameterType);
        Assert.Equal(
            pendingFollowUpPayloadType,
            hostType.GetMethod("BuildSidecarPendingFollowUpPayload", BindingFlags.Static | BindingFlags.NonPublic)!.ReturnType);
        Assert.Equal(
            pendingFollowUpPayloadType,
            hostType.GetMethod("BuildSidecarPendingFollowUpPayload", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetParameters()[0]
                .ParameterType);
        Assert.Equal(
            pendingInputStatePayloadType,
            hostType.GetMethod("BuildSidecarPendingInputStatePayload", BindingFlags.Static | BindingFlags.NonPublic)!.ReturnType);
        Assert.Equal(
            pendingInputStatePayloadType,
            hostType.GetMethod("BuildSidecarPendingInputStatePayload", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetParameters()[0]
                .ParameterType);
    }

    [Fact]
    public async Task RelayRuntimeEventAsync_WhenPermissionRequestedArrives_WritesTypedPermissionPayload()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var streamEvent = CreateStreamEvent(
            ControlPlaneConversationStreamEventKind.PermissionRequested,
            threadId: "thread-permission-1",
            turnId: "turn-permission-1",
            callId: "permission-call-1",
            toolName: "request_permissions",
            message: "等待权限确认",
            payloadKind: ControlPlaneConversationStreamPayloadKind.PermissionRequest,
            payload: new PermissionRequestPayload
            {
                Reason = "需要更高的网络访问权限",
                Fields =
                [
                    new PermissionFieldPayload
                    {
                        Key = "network",
                        ValueType = "json",
                        ValueText = "{\"enabled\":true}",
                    },
                ],
                PermissionsJson = "{\"network\":{\"enabled\":true}}",
                Summary = "需要更高的网络访问权限 | {\"network\":{\"enabled\":true}}",
            });

        var task = ReflectionTestHelper.InvokeMethod(host!, "RelayRuntimeEventAsync", streamEvent);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        var eventLine = output
            .ToString()
            .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => JsonDocument.Parse(line))
            .First(static document => document.RootElement.GetProperty("messageType").GetString() == "event");

        using (eventLine)
        {
            var root = eventLine.RootElement;
            Assert.Equal("permission_requested", root.GetProperty("eventType").GetString());

            var data = root.GetProperty("data");
            Assert.Equal("PermissionRequested", data.GetProperty("kind").GetString());
            var permissionRequest = data.GetProperty("permissionRequest");
            Assert.Equal("需要更高的网络访问权限", permissionRequest.GetProperty("reason").GetString());
            Assert.Equal("network", permissionRequest.GetProperty("fields")[0].GetProperty("key").GetString());
            Assert.Equal("json", permissionRequest.GetProperty("fields")[0].GetProperty("valueType").GetString());
            Assert.Equal("{\"network\":{\"enabled\":true}}", permissionRequest.GetProperty("permissionsJson").GetString());
            Assert.Equal(
                "需要更高的网络访问权限 | {\"network\":{\"enabled\":true}}",
                permissionRequest.GetProperty("summary").GetString());
        }
    }

    [Fact]
    public async Task RelayRuntimeEventAsync_WhenUserInputRequestedArrives_WritesTypedUserInputPayload()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var requestedSchema = StructuredValue.FromObject(
            new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["type"] = StructuredValue.FromString("object"),
                ["required"] = StructuredValue.FromArray([StructuredValue.FromString("profile")]),
            });

        var streamEvent = CreateStreamEvent(
            ControlPlaneConversationStreamEventKind.UserInputRequested,
            threadId: "thread-user-input-1",
            turnId: "turn-user-input-1",
            callId: "user-input-call-1",
            toolName: "requestUserInput",
            message: "等待人工补录",
            payloadKind: ControlPlaneConversationStreamPayloadKind.UserInputRequest,
            payload: new UserInputRequestPayload
            {
                Questions =
                [
                    new UserInputQuestionPayload
                    {
                        Id = "profile",
                        Header = "选择配置",
                        Prompt = "请选择要继续使用的配置文件",
                        IsSecret = false,
                        IsOther = true,
                        Options =
                        [
                            new UserInputOptionPayload
                            {
                                Label = "default",
                                Description = "默认配置",
                            },
                            new UserInputOptionPayload
                            {
                                Label = "ci",
                                Description = "CI 配置",
                            },
                        ],
                    },
                ],
                Summary = "请选择配置文件",
                Mode = "select",
                RequestedSchema = requestedSchema,
                Url = "https://example.com/config",
                ServerName = "filesystem",
                ElicitationId = "elicitation-1",
            });

        var task = ReflectionTestHelper.InvokeMethod(host!, "RelayRuntimeEventAsync", streamEvent);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        var eventLine = output
            .ToString()
            .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => JsonDocument.Parse(line))
            .First(static document => document.RootElement.GetProperty("messageType").GetString() == "event");

        using (eventLine)
        {
            var root = eventLine.RootElement;
            Assert.Equal("request_user_input", root.GetProperty("eventType").GetString());

            var data = root.GetProperty("data");
            Assert.Equal("UserInputRequested", data.GetProperty("kind").GetString());
            var request = data.GetProperty("userInputRequest");
            Assert.Equal("请选择配置文件", request.GetProperty("summary").GetString());
            Assert.Equal("select", request.GetProperty("mode").GetString());
            Assert.Equal("https://example.com/config", request.GetProperty("url").GetString());
            Assert.Equal("filesystem", request.GetProperty("serverName").GetString());
            Assert.Equal("elicitation-1", request.GetProperty("elicitationId").GetString());
            Assert.Equal("选择配置", request.GetProperty("questions")[0].GetProperty("header").GetString());
            Assert.Equal("default", request.GetProperty("questions")[0].GetProperty("options")[0].GetProperty("label").GetString());
            var requestedSchemaElement = request.GetProperty("requestedSchema");
            Assert.Equal("object", requestedSchemaElement.GetProperty("type").GetString());
            Assert.Equal("profile", requestedSchemaElement.GetProperty("required")[0].GetString());
        }
    }

    [Fact]
    public async Task RelayRuntimeEventAsync_WhenServerRequestResolvedArrives_WritesTypedPayload()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var streamEvent = CreateStreamEvent(
            ControlPlaneConversationStreamEventKind.ServerRequestResolved,
            threadId: "thread-server-request-1",
            turnId: "turn-server-request-1",
            message: "服务端请求已解决",
            payloadKind: ControlPlaneConversationStreamPayloadKind.ServerRequestResolved,
            payload: new ServerRequestResolvedPayload(
                915,
                "permission_requested",
                "permission-call-1",
                "permission-resolve-915"));

        var task = ReflectionTestHelper.InvokeMethod(host!, "RelayRuntimeEventAsync", streamEvent);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        var line = Assert.Single(
            output.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));

        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        Assert.Equal("server_request_resolved", root.GetProperty("eventType").GetString());

        var data = root.GetProperty("data");
        Assert.Equal("ServerRequestResolved", data.GetProperty("kind").GetString());
        var payload = data.GetProperty("serverRequestResolved");
        Assert.Equal(915, payload.GetProperty("requestId").GetInt64());
        Assert.Equal("permission_requested", payload.GetProperty("requestKind").GetString());
        Assert.Equal("permission-call-1", payload.GetProperty("callId").GetString());
        Assert.Equal("permission-resolve-915", payload.GetProperty("requestIdRaw").GetString());
    }

    [Fact]
    public async Task HandleRequestAsync_WhenRespondPermissionCommandArrives_UsesContractsStructuredValues()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var runtime = new CliConsumerFakeRuntime();
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        var request = Activator.CreateInstance(requestType);
        Assert.NotNull(request);
        ReflectionTestHelper.SetProperty(request!, "RequestId", "req-respond-permission-1");
        ReflectionTestHelper.SetProperty(request, "Command", "respondPermission");
        ReflectionTestHelper.SetProperty(
            request,
            "Payload",
            ReflectionTestHelper.ParseJsonElement(
                """
                {
                  "callId": "permission-sidecar-001",
                  "permissions": {
                    "network": {
                      "action": "allow",
                      "host": "api.example.com"
                    },
                    "sandbox_workspace_write": true
                  },
                  "scope": "session"
                }
                """));

        var task = ReflectionTestHelper.InvokeMethod(host!, "HandleRequestAsync", request);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        var response = Assert.Single(runtime.PermissionResponses);
        Assert.Equal("permission-sidecar-001", response.CallId.Value);
        Assert.Equal("host-permission-permission-sidecar-001", response.Envelope?.Id.Value);
        Assert.Equal("permission_response", Assert.IsType<StructuredInteractionItem>(Assert.Single(response.Envelope!.Items)).SemanticKind);
        Assert.Equal(ControlPlanePermissionScope.Session, response.Scope);
        Assert.Equal("allow", response.Permissions["network"].GetProperty("action").StringValue);
        Assert.Equal("api.example.com", response.Permissions["network"].GetProperty("host").StringValue);
        Assert.True(response.Permissions["sandbox_workspace_write"].BooleanValue);
    }

    [Fact]
    public async Task HandleRequestAsync_WhenRespondUserInputCommandArrives_UsesContractsStructuredValues()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var runtime = new CliConsumerFakeRuntime();
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        var request = Activator.CreateInstance(requestType);
        Assert.NotNull(request);
        ReflectionTestHelper.SetProperty(request!, "RequestId", "req-respond-userinput-1");
        ReflectionTestHelper.SetProperty(request, "Command", "respondUserInput");
        ReflectionTestHelper.SetProperty(
            request,
            "Payload",
            ReflectionTestHelper.ParseJsonElement(
                """
                {
                  "callId": "userinput-sidecar-001",
                  "answers": {
                    "choice": "A",
                    "metadata": {
                      "score": 1
                    }
                  }
                }
                """));

        var task = ReflectionTestHelper.InvokeMethod(host!, "HandleRequestAsync", request);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        var response = Assert.Single(runtime.UserInputResponses);
        Assert.Equal("userinput-sidecar-001", response.CallId.Value);
        Assert.Equal("host-userinput-userinput-sidecar-001", response.Envelope?.Id.Value);
        Assert.Equal("user_input_submission", Assert.IsType<StructuredInteractionItem>(Assert.Single(response.Envelope!.Items)).SemanticKind);
        Assert.Equal("A", response.Answers["choice"].StringValue);
        Assert.Equal("1", response.Answers["metadata"].GetProperty("score").NumberValue);
    }

    [Fact]
    public void BuildThreadResumeCommand_WhenPayloadCarriesTypedHistory_MapsTypedCommand()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var payloadType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.ResumeThreadPayload");
        var payload = JsonSerializer.Deserialize(
            """
            {
              "threadId": "thread-resume-typed-001",
              "serviceTier": null,
              "approvalPolicy": "never",
              "personality": "friendly",
              "history": [
                {
                  "type": "message",
                  "role": "user",
                  "content": [
                    {
                      "type": "input_text",
                      "text": "resume typed history"
                    }
                  ]
                }
              ]
            }
            """,
            payloadType,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(payload);

        var request = Assert.IsType<ControlPlaneResumeThreadCommand>(
            ReflectionTestHelper.InvokeStaticMethod(hostType, "BuildThreadResumeCommand", payload!, "thread-resume-typed-001"));
        Assert.Equal("thread-resume-typed-001", request.ThreadId.Value);
        Assert.Equal("null", request.ServiceTier);
        Assert.Equal("never", request.ApprovalPolicy);
        Assert.Equal("friendly", request.Personality);
        var history = Assert.Single(request.History!);
        Assert.Equal("message", history.Properties["type"].StringValue);
        Assert.Equal("user", history.Properties["role"].StringValue);
    }

    [Fact]
    public void BuildThreadForkCommand_WhenPayloadCarriesGranularApprovalAndClearedTier_MapsTypedCommand()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var payloadType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.ForkThreadPayload");
        var payload = JsonSerializer.Deserialize(
            """
            {
              "threadId": "thread-fork-typed-001",
              "serviceTier": null,
              "approvalPolicy": {
                "granular": {
                  "sandbox_approval": false,
                  "rules": true,
                  "skill_approval": false,
                  "request_permissions": true,
                  "mcp_elicitations": false
                }
              },
              "ephemeral": true
            }
            """,
            payloadType,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(payload);

        var request = Assert.IsType<ControlPlaneForkThreadCommand>(
            ReflectionTestHelper.InvokeStaticMethod(hostType, "BuildThreadForkCommand", payload!, "thread-fork-typed-001"));
        Assert.Equal("thread-fork-typed-001", request.ThreadId.Value);
        Assert.Equal("null", request.ServiceTier);
        Assert.NotNull(request.ApprovalPolicy);
        using (var approvalDocument = JsonDocument.Parse(request.ApprovalPolicy!))
        {
            Assert.True(approvalDocument.RootElement.GetProperty("granular").GetProperty("request_permissions").GetBoolean());
        }
        Assert.True(request.Ephemeral);
    }

    [Fact]
    public async Task HandleRequestAsync_WhenReadConfigCommandArrives_UsesTypedRuntimeRequest()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var runtime = new CliConsumerFakeRuntime
        {
            ReadConfigAsyncHandler = (_, _) => Task.FromResult(
                new ControlPlaneConfigSnapshotResult
                {
                    Config = StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                    {
                        ["model"] = StructuredValue.FromString("gpt-5"),
                    }),
                }),
        };
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        var request = Activator.CreateInstance(requestType);
        Assert.NotNull(request);
        ReflectionTestHelper.SetProperty(request!, "RequestId", "req-read-config-1");
        ReflectionTestHelper.SetProperty(request, "Command", "readConfig");
        ReflectionTestHelper.SetProperty(
            request,
            "Payload",
            ReflectionTestHelper.ParseJsonElement(
                """
                {
                  "workingDirectory": "D:/workspace",
                  "includeLayers": true
                }
                """));

        var task = ReflectionTestHelper.InvokeMethod(host!, "HandleRequestAsync", request);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        var readRequest = Assert.Single(runtime.ConfigReadCalls);
        Assert.Equal("D:/workspace", readRequest.WorkingDirectory);
        Assert.True(readRequest.IncludeLayers);

        using var responseDocument = JsonDocument.Parse(Assert.Single(
            output.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)));
        Assert.Equal("response", responseDocument.RootElement.GetProperty("messageType").GetString());
        Assert.True(responseDocument.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task HandleRequestAsync_WhenWriteConfigBatchCommandArrives_UsesTypedRuntimeRequest()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var runtime = new CliConsumerFakeRuntime
        {
            WriteConfigBatchAsyncHandler = static (request, _) => Task.FromResult(
                new ControlPlaneConfigWriteResult
                {
                    Status = "ok",
                    Version = request.ExpectedVersion ?? "v-next",
                    FilePath = request.FilePath,
                }),
        };
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        var request = Activator.CreateInstance(requestType);
        Assert.NotNull(request);
        ReflectionTestHelper.SetProperty(request!, "RequestId", "req-write-config-batch-1");
        ReflectionTestHelper.SetProperty(request, "Command", "writeConfigBatch");
        ReflectionTestHelper.SetProperty(
            request,
            "Payload",
            ReflectionTestHelper.ParseJsonElement(
                """
                {
                  "items": [
                    {
                      "keyPath": "profiles.default.model",
                      "value": "gpt-5"
                    }
                  ],
                  "workingDirectory": "D:/workspace",
                  "filePath": "D:/workspace/.tianshu/tianshu.toml",
                  "expectedVersion": "v2",
                  "reloadUserConfig": true
                }
                """));

        var task = ReflectionTestHelper.InvokeMethod(host!, "HandleRequestAsync", request);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        var writeRequest = Assert.Single(runtime.ConfigBatchWriteCalls);
        Assert.Equal("D:/workspace", writeRequest.WorkingDirectory);
        Assert.Equal("D:/workspace/.tianshu/tianshu.toml", writeRequest.FilePath);
        Assert.Equal("v2", writeRequest.ExpectedVersion);
        Assert.True(writeRequest.ReloadUserConfig);
        var item = Assert.Single(writeRequest.Items);
        Assert.Equal("profiles.default.model", item.KeyPath);
        Assert.Equal("gpt-5", item.Value?.StringValue);

        using var responseDocument = JsonDocument.Parse(Assert.Single(
            output.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)));
        Assert.Equal("response", responseDocument.RootElement.GetProperty("messageType").GetString());
        Assert.True(responseDocument.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task HandleRequestAsync_WhenListSkillsCommandArrives_UsesTypedRuntimeRequest()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var runtime = new CliConsumerFakeRuntime
        {
            ListSkillsAsyncHandler = static (request, _) => Task.FromResult(
                new ControlPlaneSkillCatalogResult
                {
                    Entries =
                    [
                        new ControlPlaneSkillCatalogEntry
                        {
                            WorkingDirectory = request.WorkingDirectories.FirstOrDefault() ?? string.Empty,
                        },
                    ],
                }),
        };
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        var request = Activator.CreateInstance(requestType);
        Assert.NotNull(request);
        ReflectionTestHelper.SetProperty(request!, "RequestId", "req-list-skills-1");
        ReflectionTestHelper.SetProperty(request, "Command", "listSkills");
        ReflectionTestHelper.SetProperty(
            request,
            "Payload",
            ReflectionTestHelper.ParseJsonElement(
                """
                {
                  "workingDirectories": ["D:/Work/TianShu"],
                  "forceReload": true
                }
                """));

        var task = ReflectionTestHelper.InvokeMethod(host!, "HandleRequestAsync", request);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        var skillsRequest = Assert.Single(runtime.SkillsListCalls);
        Assert.Equal("D:/Work/TianShu", Assert.Single(skillsRequest.WorkingDirectories));
        Assert.True(skillsRequest.ForceReload);

        using var responseDocument = JsonDocument.Parse(Assert.Single(
            output.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)));
        Assert.Equal("response", responseDocument.RootElement.GetProperty("messageType").GetString());
        Assert.True(responseDocument.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task HandleRequestAsync_WhenCatalogFormalWriteCommandsArrive_UseTypedControlPlaneRequests()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var runtime = new CliConsumerFakeRuntime();
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        object BuildRequest(string requestId, string command, string payloadJson)
        {
            var request = Activator.CreateInstance(requestType);
            Assert.NotNull(request);
            ReflectionTestHelper.SetProperty(request!, "RequestId", requestId);
            ReflectionTestHelper.SetProperty(request, "Command", command);
            ReflectionTestHelper.SetProperty(request, "Payload", ReflectionTestHelper.ParseJsonElement(payloadJson));
            return request!;
        }

        await ReflectionTestHelper.AwaitTaskResultAsync(
            ReflectionTestHelper.InvokeMethod(
                host,
                "HandleRequestAsync",
                BuildRequest(
                    "req-write-skill-config-1",
                    "writeSkillConfig",
                    """{"path":"skills/tianshu-contracts-first-helper-migration","enabled":true,"workingDirectory":"D:/Work/TianShu"}""")));
        await ReflectionTestHelper.AwaitTaskResultAsync(
            ReflectionTestHelper.InvokeMethod(
                host,
                "HandleRequestAsync",
                BuildRequest(
                    "req-uninstall-plugin-1",
                    "uninstallPlugin",
                    """{"pluginId":"plugin-tianshu-1","workingDirectory":"D:/Work/TianShu"}""")));

        var skillConfigRequest = Assert.Single(runtime.SkillConfigWriteCalls);
        Assert.Equal("skills/tianshu-contracts-first-helper-migration", skillConfigRequest.Path);
        Assert.True(skillConfigRequest.Enabled);
        Assert.Equal("D:/Work/TianShu", skillConfigRequest.WorkingDirectory);

        var uninstallRequest = Assert.Single(runtime.PluginUninstallCalls);
        Assert.Equal("plugin-tianshu-1", uninstallRequest.PluginId);
        Assert.Equal("D:/Work/TianShu", uninstallRequest.WorkingDirectory);

        var responseLines = output.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, responseLines.Length);
        foreach (var responseLine in responseLines)
        {
            using var responseDocument = JsonDocument.Parse(responseLine);
            Assert.Equal("response", responseDocument.RootElement.GetProperty("messageType").GetString());
            Assert.True(responseDocument.RootElement.GetProperty("success").GetBoolean());
        }
    }

    [Fact]
    public async Task HandleRequestAsync_WhenCatalogAndAgentFormalCommandsArrive_UseTypedControlPlaneRequests()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var runtime = new CliConsumerFakeRuntime
        {
            GetCapabilityCatalogAsyncHandler = (query, _) => Task.FromResult(
                new CapabilityCatalogSnapshot(
                    activeProviderKey: "openai",
                    activeModel: "gpt-5",
                    providers:
                    [
                        new ProviderProfile(
                            "openai",
                            "OpenAI",
                            "responses",
                            models:
                            [
                                new ModelProfile("gpt-5", "gpt-5", "GPT-5"),
                            ]),
                    ])),
            ResolveEngineBindingAsyncHandler = (query, _) => Task.FromResult(
                new ResolvedEngineBinding(
                    new EngineBinding(
                        "openai:gpt-5",
                        query.PreferredProviderKey ?? "openai",
                        query.PreferredModelKey ?? "gpt-5",
                        "gpt-5",
                        "responses",
                        new CatalogStreamingPreference("websocket", query.PreferWebsocketTransport, useWebsocketTransport: true),
                        reasoning: new CatalogReasoningProfile(
                            query.ReasoningEffort,
                            query.ReasoningSummary,
                            query.Verbosity)))),
            ListAgentsAsyncHandler = (query, _) => Task.FromResult(
                new ControlPlaneAgentRosterResult
                {
                    Agents =
                    [
                        new ControlPlaneAgentDescriptor
                        {
                            ThreadId = new ThreadId("agent-direct-sidecar"),
                            AgentNickname = "Direct Hermes",
                            AgentRole = query.IncludePrimaryThreads ? "primary" : "worker",
                            UpdatedAt = DateTimeOffset.UtcNow,
                        },
                    ],
                }),
        };
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        object BuildRequest(string requestId, string command, string payloadJson)
        {
            var request = Activator.CreateInstance(requestType);
            Assert.NotNull(request);
            ReflectionTestHelper.SetProperty(request!, "RequestId", requestId);
            ReflectionTestHelper.SetProperty(request, "Command", command);
            ReflectionTestHelper.SetProperty(request, "Payload", ReflectionTestHelper.ParseJsonElement(payloadJson));
            return request!;
        }

        var catalogRequest = BuildRequest(
            "req-capability-catalog-1",
            "getCapabilityCatalog",
            """{"workspacePath":"D:/Work/TianShu","modelLimit":7,"includeHiddenModels":true}""");
        var resolveRequest = BuildRequest(
            "req-engine-binding-1",
            "resolveEngineBinding",
            """{"workspacePath":"D:/Work/TianShu","providerKey":"openai","modelKey":"gpt-5","reasoningEffort":"high","reasoningSummary":"detailed","verbosity":"verbose","preferWebsocketTransport":true}""");
        var agentListRequest = BuildRequest(
            "req-agent-list-1",
            "listAgents",
            """{"limit":6,"cursor":"sidecar-direct-cursor","includePrimaryThreads":true}""");

        await ReflectionTestHelper.AwaitTaskResultAsync(ReflectionTestHelper.InvokeMethod(host, "HandleRequestAsync", catalogRequest));
        await ReflectionTestHelper.AwaitTaskResultAsync(ReflectionTestHelper.InvokeMethod(host, "HandleRequestAsync", resolveRequest));
        await ReflectionTestHelper.AwaitTaskResultAsync(ReflectionTestHelper.InvokeMethod(host, "HandleRequestAsync", agentListRequest));

        var providerCatalogRequest = Assert.Single(runtime.ProviderCatalogCalls);
        Assert.Equal("D:/Work/TianShu", providerCatalogRequest.WorkspacePath);
        Assert.True(providerCatalogRequest.IncludeHiddenModels);
        Assert.Equal(7, providerCatalogRequest.ModelLimit);

        var engineBindingRequest = Assert.Single(runtime.EngineBindingCalls);
        Assert.Equal("D:/Work/TianShu", engineBindingRequest.WorkspacePath);
        Assert.Equal("openai", engineBindingRequest.PreferredProviderKey);
        Assert.Equal("gpt-5", engineBindingRequest.PreferredModelKey);
        Assert.Equal("high", engineBindingRequest.ReasoningEffort);
        Assert.Equal("detailed", engineBindingRequest.ReasoningSummary);
        Assert.Equal("verbose", engineBindingRequest.Verbosity);
        Assert.True(engineBindingRequest.PreferWebsocketTransport);

        var listAgentsRequest = Assert.Single(runtime.AgentListCalls);
        Assert.Equal(6, listAgentsRequest.Limit);
        Assert.Equal("sidecar-direct-cursor", listAgentsRequest.Cursor);
        Assert.True(listAgentsRequest.IncludePrimaryThreads);

        var responseLines = output.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, responseLines.Length);
        foreach (var responseLine in responseLines)
        {
            using var responseDocument = JsonDocument.Parse(responseLine);
            Assert.Equal("response", responseDocument.RootElement.GetProperty("messageType").GetString());
            Assert.True(responseDocument.RootElement.GetProperty("success").GetBoolean());
        }
    }

    [Fact]
    public async Task HandleRequestAsync_WhenFormalReadQueriesArrive_UseTypedControlPlaneRequests()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var runtime = new CliConsumerFakeRuntime();
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        object BuildRequest(string requestId, string command, string payloadJson)
        {
            var request = Activator.CreateInstance(requestType);
            Assert.NotNull(request);
            ReflectionTestHelper.SetProperty(request!, "RequestId", requestId);
            ReflectionTestHelper.SetProperty(request, "Command", command);
            ReflectionTestHelper.SetProperty(request, "Payload", ReflectionTestHelper.ParseJsonElement(payloadJson));
            return request!;
        }

        var requests = new[]
        {
            BuildRequest("req-thread-projection-1", "getThreadProjection", """{"threadId":"thread-projection-1"}"""),
            BuildRequest("req-session-overview-1", "getSessionOverview", """{"sessionId":"session-direct-1"}"""),
            BuildRequest("req-session-list-1", "listSessions", """{"spaceId":"space-direct-1","includeClosed":true}"""),
            BuildRequest("req-approval-queue-1", "getApprovalQueueProjection", """{"requestedFromParticipantId":"participant-owner-1"}"""),
            BuildRequest("req-user-input-list-1", "listUserInputRequests", """{"participantId":"participant-input-1"}"""),
            BuildRequest("req-collaboration-overview-1", "getCollaborationSpaceOverview", """{"spaceId":"space-direct-2"}"""),
            BuildRequest("req-collaboration-projection-1", "getCollaborationSpaceProjection", """{"collaborationId":"space-direct-3"}"""),
            BuildRequest("req-collaboration-list-1", "listCollaborationSpaces", """{"includeArchived":true}"""),
            BuildRequest("req-participant-projection-1", "getParticipantProjection", """{"participantId":"participant-direct-1"}"""),
            BuildRequest("req-participant-view-1", "getParticipantViewProjection", """{"participantId":"participant-direct-2"}"""),
            BuildRequest("req-participant-list-1", "listParticipantsInScope", """{"collaborationSpaceId":"space-direct-4"}"""),
            BuildRequest("req-artifact-projection-1", "getArtifactProjection", """{"artifactId":"artifact-direct-1"}"""),
            BuildRequest("req-artifact-collection-1", "getArtifactCollectionProjection", """{"spaceId":"space-direct-5","producedByParticipantId":"participant-direct-3"}"""),
            BuildRequest("req-workflow-board-1", "getWorkflowBoard", """{"workflowId":"workflow-direct-1"}"""),
            BuildRequest("req-task-board-1", "getTaskBoard", """{"workflowId":"workflow-direct-2"}"""),
            BuildRequest("req-plan-projection-1", "getPlanProjection", """{"workflowId":"workflow-direct-3"}"""),
            BuildRequest("req-agent-roster-1", "getAgentRosterProjection", """{"workflowId":"workflow-direct-4"}"""),
            BuildRequest("req-team-projection-1", "getTeamProjection", """{"teamId":"team-direct-1"}"""),
            BuildRequest("req-account-profile-1", "getAccountProfile", """{"accountId":"account-direct-1"}"""),
            BuildRequest("req-bound-devices-1", "listBoundDevices", """{"accountId":"account-direct-2"}"""),
            BuildRequest("req-memory-spaces-1", "listMemorySpaces", """{"scopeKind":"collaboration"}"""),
            BuildRequest("req-memory-overlay-1", "resolveMemoryOverlay", """{"memorySpaceId":"memory-space-1","spaceId":"space-direct-6"}"""),
            BuildRequest("req-execution-trace-1", "getExecutionTrace", """{"traceId":"trace-direct-1"}"""),
            BuildRequest("req-attempt-summaries-1", "listAttemptSummaries", """{"executionId":"exec-direct-1"}"""),
        };

        foreach (var request in requests)
        {
            await ReflectionTestHelper.AwaitTaskResultAsync(ReflectionTestHelper.InvokeMethod(host, "HandleRequestAsync", request));
        }

        Assert.Equal("thread-projection-1", Assert.Single(runtime.ThreadProjectionCalls).ThreadId.Value);
        Assert.Equal("session-direct-1", Assert.Single(runtime.SessionOverviewCalls).SessionId.Value);
        Assert.Equal("space-direct-1", Assert.Single(runtime.SessionListCalls).CollaborationSpaceId?.Value);
        Assert.True(Assert.Single(runtime.SessionListCalls).IncludeClosed);
        Assert.Equal("participant-owner-1", Assert.Single(runtime.ApprovalQueueProjectionCalls).RequestedFromParticipantId?.Value);
        Assert.Equal("participant-input-1", Assert.Single(runtime.UserInputListCalls).RequestedFromParticipantId?.Value);
        Assert.Equal("space-direct-2", Assert.Single(runtime.CollaborationSpaceOverviewCalls).SpaceId.Value);
        Assert.Equal("space-direct-3", Assert.Single(runtime.CollaborationSpaceProjectionCalls).SpaceId.Value);
        Assert.True(Assert.Single(runtime.CollaborationSpaceListCalls).IncludeArchived);
        Assert.Equal("participant-direct-1", Assert.Single(runtime.ParticipantProjectionCalls).ParticipantId.Value);
        Assert.Equal("participant-direct-2", Assert.Single(runtime.ParticipantViewProjectionCalls).ParticipantId.Value);
        Assert.Equal("space-direct-4", Assert.Single(runtime.ParticipantListCalls).CollaborationSpaceId.Value);
        Assert.Equal("artifact-direct-1", Assert.Single(runtime.ArtifactProjectionCalls).ArtifactId.Value);
        Assert.Equal("space-direct-5", Assert.Single(runtime.ArtifactCollectionProjectionCalls).CollaborationSpaceId?.Value);
        Assert.Equal("participant-direct-3", Assert.Single(runtime.ArtifactCollectionProjectionCalls).ProducedByParticipantId?.Value);
        Assert.Equal("workflow-direct-1", Assert.Single(runtime.WorkflowBoardProjectionCalls).WorkflowId.Value);
        Assert.Equal("workflow-direct-2", Assert.Single(runtime.TaskBoardProjectionCalls).WorkflowId.Value);
        Assert.Equal("workflow-direct-3", Assert.Single(runtime.PlanProjectionCalls).WorkflowId.Value);
        Assert.Equal("workflow-direct-4", Assert.Single(runtime.AgentRosterProjectionCalls).WorkflowId?.Value);
        Assert.Equal("team-direct-1", Assert.Single(runtime.TeamProjectionCalls).TeamId.Value);
        Assert.Equal("account-direct-1", Assert.Single(runtime.AccountProfileCalls).AccountId.Value);
        Assert.Equal("account-direct-2", Assert.Single(runtime.BoundDeviceListCalls).AccountId.Value);
        Assert.Equal(MemoryScopeKind.Collaboration, Assert.Single(runtime.MemorySpaceListCalls).ScopeKind);
        Assert.Equal("memory-space-1", Assert.Single(runtime.MemoryOverlayCalls).MemorySpaceId?.Value);
        Assert.Equal("space-direct-6", Assert.Single(runtime.MemoryOverlayCalls).CollaborationSpaceId?.Value);
        Assert.Equal("trace-direct-1", Assert.Single(runtime.ExecutionTraceCalls).TraceId.Value);
        Assert.Equal("exec-direct-1", Assert.Single(runtime.AttemptSummaryListCalls).ExecutionId.Value);

        var responseLines = output.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(requests.Length, responseLines.Length);
        foreach (var responseLine in responseLines)
        {
            using var responseDocument = JsonDocument.Parse(responseLine);
            Assert.Equal("response", responseDocument.RootElement.GetProperty("messageType").GetString());
            Assert.True(responseDocument.RootElement.GetProperty("success").GetBoolean());
        }
    }

    [Fact]
    public async Task HandleRequestAsync_WhenMemoryFormalCommandsArrive_UseTypedControlPlaneRequests()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var runtime = new CliConsumerFakeRuntime();
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        object BuildRequest(string requestId, string command, string payloadJson)
        {
            var request = Activator.CreateInstance(requestType);
            Assert.NotNull(request);
            ReflectionTestHelper.SetProperty(request!, "RequestId", requestId);
            ReflectionTestHelper.SetProperty(request, "Command", command);
            ReflectionTestHelper.SetProperty(request, "Payload", ReflectionTestHelper.ParseJsonElement(payloadJson));
            return request!;
        }

        var requests = new[]
        {
            BuildRequest("req-memory-providers-1", "listMemoryProviders", """{"scopeKind":"workspace"}"""),
            BuildRequest("req-memory-filter-1", "filterMemory", """{"memorySpaceId":{"value":"memory-space-1"},"key":"pref.shell","scopeKind":"workspace"}"""),
            BuildRequest("req-memory-add-1", "addMemory", """{"memorySpaceId":{"value":"memory-space-1"},"key":"pref.shell","value":"pwsh","confidence":0.9}"""),
            BuildRequest("req-memory-extract-1", "extractMemory", """{"memorySpaceId":{"value":"memory-space-1"},"source":{"sourceKind":"conversation","sourceId":"turn-direct-1"},"content":"记住我更喜欢 pwsh"}"""),
            BuildRequest("req-memory-import-1", "importMemory", """{"memorySpaceId":{"value":"memory-space-1"},"source":{"sourceKind":"file","sourceId":"memory.json"}}"""),
            BuildRequest("req-memory-export-1", "exportMemory", """{"memorySpaceId":{"value":"memory-space-1"},"destination":{"sourceKind":"externalProvider","sourceId":"provider-direct-1"},"filter":{"key":"pref.shell"}}"""),
            BuildRequest("req-memory-bind-1", "bindMemoryProvider", """{"providerId":"provider-direct-1","memorySpaceId":{"value":"memory-space-1"},"mode":"readWrite","allowedCapabilities":10}"""),
            BuildRequest("req-memory-forget-1", "forgetMemory", """{"memoryRecordId":{"value":"memory-record-1"}}"""),
            BuildRequest("req-memory-delete-1", "deleteMemory", """{"memoryRecordId":{"value":"memory-record-1"},"reason":"cleanup"}"""),
            BuildRequest("req-memory-feedback-1", "recordMemoryFeedback", """{"memoryRecordId":{"value":"memory-record-1"},"decision":"applied","feedback":"accepted"}"""),
            BuildRequest("req-memory-citation-1", "recordMemoryCitation", """{"citation":{"entries":[{"memoryRecordId":{"value":"memory-record-1"},"memorySpaceId":{"value":"memory-space-1"},"key":"pref.shell"}]}}"""),
        };

        foreach (var request in requests)
        {
            await ReflectionTestHelper.AwaitTaskResultAsync(ReflectionTestHelper.InvokeMethod(host, "HandleRequestAsync", request));
        }

        Assert.Equal(MemoryScopeKind.Workspace, Assert.Single(runtime.MemoryProviderListCalls).ScopeKind);
        Assert.Equal("pref.shell", Assert.Single(runtime.MemoryFilterCalls).Key);
        Assert.Equal("memory-space-1", Assert.Single(runtime.MemoryAddCalls).MemorySpaceId.Value);
        Assert.Equal("turn-direct-1", Assert.Single(runtime.MemoryExtractCalls).Source.SourceId);
        Assert.Equal(MemorySourceKind.File, Assert.Single(runtime.MemoryImportCalls).Source.SourceKind);
        Assert.Equal("provider-direct-1", Assert.Single(runtime.MemoryExportCalls).Destination?.SourceId);
        Assert.Equal(MemoryProviderCapability.Add | MemoryProviderCapability.Filter, Assert.Single(runtime.MemoryBindProviderCalls).AllowedCapabilities);
        Assert.Equal("memory-record-1", Assert.Single(runtime.MemoryForgetCalls).MemoryRecordId?.Value);
        Assert.Equal("cleanup", Assert.Single(runtime.MemoryDeleteCalls).Reason);
        Assert.Equal(MemoryMergeDecision.Applied, Assert.Single(runtime.MemoryFeedbackCalls).Decision);
        Assert.Equal("pref.shell", Assert.Single(Assert.Single(runtime.MemoryCitationCalls).Citation.Entries).Key);

        var responseLines = output.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(requests.Length, responseLines.Length);
        foreach (var responseLine in responseLines)
        {
            using var responseDocument = JsonDocument.Parse(responseLine);
            Assert.Equal("response", responseDocument.RootElement.GetProperty("messageType").GetString());
            Assert.True(responseDocument.RootElement.GetProperty("success").GetBoolean());
        }
    }

    [Fact]
    public async Task HandleRequestAsync_WhenGetSessionSnapshotArrives_ReturnsTypedSessionState()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var runtime = new CliConsumerFakeRuntime
        {
            ActiveThreadId = "thread-session-direct-1",
            HasActiveTurn = true,
        };
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        var request = Activator.CreateInstance(requestType);
        Assert.NotNull(request);
        ReflectionTestHelper.SetProperty(request!, "RequestId", "req-session-snapshot-1");
        ReflectionTestHelper.SetProperty(request, "Command", "getSessionSnapshot");
        ReflectionTestHelper.SetProperty(request, "Payload", ReflectionTestHelper.ParseJsonElement("""{}"""));

        await ReflectionTestHelper.AwaitTaskResultAsync(ReflectionTestHelper.InvokeMethod(host, "HandleRequestAsync", request));

        using var responseDocument = JsonDocument.Parse(Assert.Single(
            output.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)));
        Assert.Equal("response", responseDocument.RootElement.GetProperty("messageType").GetString());
        Assert.True(responseDocument.RootElement.GetProperty("success").GetBoolean());
        var payload = responseDocument.RootElement.GetProperty("data");
        Assert.Equal("thread-session-direct-1", payload.GetProperty("activeThreadId").GetProperty("value").GetString());
        Assert.True(payload.GetProperty("hasActiveTurn").GetBoolean());
    }

    [Fact]
    public async Task HandleRequestAsync_WhenConversationThreadFormalCommandsArrive_UseTypedControlPlaneRequests()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var runtime = new CliConsumerFakeRuntime
        {
            ListLoadedThreadsAsyncHandler = (_, _) => Task.FromResult(new ControlPlaneLoadedThreadListResult
            {
                ThreadIds = [new ThreadId("thread-loaded-1"), new ThreadId("thread-loaded-2")],
                NextCursor = "loaded-cursor-1",
            }),
            UnsubscribeThreadAsyncHandler = (_, _) => Task.FromResult(new ControlPlaneThreadUnsubscribeResult
            {
                Status = "unsubscribed",
            }),
            IncrementThreadElicitationAsyncHandler = (_, _) => Task.FromResult(new ControlPlaneThreadElicitationResult
            {
                Count = 3,
                Paused = true,
            }),
            DecrementThreadElicitationAsyncHandler = (_, _) => Task.FromResult(new ControlPlaneThreadElicitationResult
            {
                Count = 2,
                Paused = false,
            }),
        };
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        object BuildRequest(string requestId, string command, string payloadJson)
        {
            var request = Activator.CreateInstance(requestType);
            Assert.NotNull(request);
            ReflectionTestHelper.SetProperty(request!, "RequestId", requestId);
            ReflectionTestHelper.SetProperty(request, "Command", command);
            ReflectionTestHelper.SetProperty(request, "Payload", ReflectionTestHelper.ParseJsonElement(payloadJson));
            return request!;
        }

        var requests = new[]
        {
            BuildRequest("req-loaded-threads-1", "listLoadedThreads", """{"limit":2,"cursor":"loaded-cursor-0"}"""),
            BuildRequest("req-compact-thread-1", "compactThread", """{"threadId":"thread-compact-1","keepRecentTurns":6}"""),
            BuildRequest("req-clean-background-1", "cleanBackgroundTerminals", """{"threadId":"thread-background-1"}"""),
            BuildRequest("req-unsubscribe-thread-1", "unsubscribeThread", """{"threadId":"thread-unsubscribe-1"}"""),
            BuildRequest("req-increment-elicitation-1", "incrementThreadElicitation", """{"threadId":"thread-elicitation-1"}"""),
            BuildRequest("req-decrement-elicitation-1", "decrementThreadElicitation", """{"threadId":"thread-elicitation-1"}"""),
        };

        foreach (var request in requests)
        {
            await ReflectionTestHelper.AwaitTaskResultAsync(ReflectionTestHelper.InvokeMethod(host, "HandleRequestAsync", request));
        }

        var loadedRequest = Assert.Single(runtime.ThreadLoadedListCalls);
        Assert.Equal(2, loadedRequest.Limit);
        Assert.Equal("loaded-cursor-0", loadedRequest.Cursor);

        var compactRequest = Assert.Single(runtime.ThreadCompactCalls);
        Assert.Equal("thread-compact-1", compactRequest.ThreadId.Value);
        Assert.Equal(6, compactRequest.KeepRecentTurns);

        var cleanRequest = Assert.Single(runtime.ThreadCleanBackgroundTerminalCalls);
        Assert.Equal("thread-background-1", cleanRequest.ThreadId.Value);

        var unsubscribeRequest = Assert.Single(runtime.ThreadUnsubscribeCalls);
        Assert.Equal("thread-unsubscribe-1", unsubscribeRequest.ThreadId.Value);

        var incrementRequest = Assert.Single(runtime.ThreadIncrementElicitationCalls);
        Assert.Equal("thread-elicitation-1", incrementRequest.ThreadId.Value);

        var decrementRequest = Assert.Single(runtime.ThreadDecrementElicitationCalls);
        Assert.Equal("thread-elicitation-1", decrementRequest.ThreadId.Value);

        var responseLines = output.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(requests.Length, responseLines.Length);
        foreach (var responseLine in responseLines)
        {
            using var responseDocument = JsonDocument.Parse(responseLine);
            Assert.Equal("response", responseDocument.RootElement.GetProperty("messageType").GetString());
            Assert.True(responseDocument.RootElement.GetProperty("success").GetBoolean());
        }
    }

    [Fact]
    public async Task HandleRequestAsync_WhenConversationFuzzyFormalCommandsArrive_UseTypedControlPlaneRequests()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var runtime = new CliConsumerFakeRuntime
        {
            SearchFuzzyFilesAsyncHandler = (_, _) => Task.FromResult(new ControlPlaneFuzzyFileSearchResult
            {
                Files =
                [
                    new ControlPlaneFuzzyFileSearchFile
                    {
                        Path = "src/TianShuExecutionRuntime.cs",
                        FileName = "TianShuExecutionRuntime.cs",
                    },
                ],
            }),
        };
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        object BuildRequest(string requestId, string command, string payloadJson)
        {
            var request = Activator.CreateInstance(requestType);
            Assert.NotNull(request);
            ReflectionTestHelper.SetProperty(request!, "RequestId", requestId);
            ReflectionTestHelper.SetProperty(request, "Command", command);
            ReflectionTestHelper.SetProperty(request, "Payload", ReflectionTestHelper.ParseJsonElement(payloadJson));
            return request!;
        }

        var requests = new[]
        {
            BuildRequest("req-direct-fuzzy-search-1", "searchFuzzyFiles", """{"query":"TianShuExecutionRuntime","cwd":"src","limit":5,"roots":["src","tests"]}"""),
            BuildRequest("req-direct-fuzzy-start-1", "startFuzzyFileSearchSession", """{"sessionId":"fuzzy-direct-1","cwd":"src"}"""),
            BuildRequest("req-direct-fuzzy-update-1", "updateFuzzyFileSearchSession", """{"sessionId":"fuzzy-direct-1","query":"RuntimeControlPlaneAdapter"}"""),
            BuildRequest("req-direct-fuzzy-stop-1", "stopFuzzyFileSearchSession", """{"sessionId":"fuzzy-direct-1"}"""),
        };

        foreach (var request in requests)
        {
            await ReflectionTestHelper.AwaitTaskResultAsync(ReflectionTestHelper.InvokeMethod(host, "HandleRequestAsync", request));
        }

        var searchRequest = Assert.Single(runtime.FuzzyFileSearchCalls);
        Assert.Equal("TianShuExecutionRuntime", searchRequest.Query);
        Assert.Equal("src", searchRequest.WorkingDirectory);
        Assert.Equal(5, searchRequest.Limit);
        Assert.Equal(["src", "tests"], searchRequest.Roots);

        var startRequest = Assert.Single(runtime.FuzzyFileSearchSessionStartCalls);
        Assert.Equal("fuzzy-direct-1", startRequest.SessionId);
        Assert.Equal(["src"], startRequest.Roots);

        var updateRequest = Assert.Single(runtime.FuzzyFileSearchSessionUpdateCalls);
        Assert.Equal("fuzzy-direct-1", updateRequest.SessionId);
        Assert.Equal("RuntimeControlPlaneAdapter", updateRequest.Query);

        var stopRequest = Assert.Single(runtime.FuzzyFileSearchSessionStopCalls);
        Assert.Equal("fuzzy-direct-1", stopRequest.SessionId);

        var responseLines = output.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(requests.Length, responseLines.Length);
        foreach (var responseLine in responseLines)
        {
            using var responseDocument = JsonDocument.Parse(responseLine);
            Assert.Equal("response", responseDocument.RootElement.GetProperty("messageType").GetString());
            Assert.True(responseDocument.RootElement.GetProperty("success").GetBoolean());
        }
    }

    [Fact]
    public async Task HandleRequestAsync_WhenConversationRealtimeFormalCommandsArrive_UseTypedControlPlaneRequests()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var runtime = new CliConsumerFakeRuntime
        {
            ActiveThreadId = "thread-realtime-direct-1",
        };
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        object BuildRequest(string requestId, string command, string payloadJson)
        {
            var request = Activator.CreateInstance(requestType);
            Assert.NotNull(request);
            ReflectionTestHelper.SetProperty(request!, "RequestId", requestId);
            ReflectionTestHelper.SetProperty(request, "Command", command);
            ReflectionTestHelper.SetProperty(request, "Payload", ReflectionTestHelper.ParseJsonElement(payloadJson));
            return request!;
        }

        var requests = new[]
        {
            BuildRequest("req-direct-realtime-start-1", "startRealtime", """{"sessionId":"realtime-session-1","prompt":"开始语音协作"}"""),
            BuildRequest("req-direct-realtime-text-1", "appendRealtimeText", """{"threadId":"thread-realtime-direct-1","sessionId":"realtime-session-1","text":"继续输出"}"""),
            BuildRequest("req-direct-realtime-audio-1", "appendRealtimeAudio", """{"threadId":"thread-realtime-direct-1","sessionId":"realtime-session-1","audio":{"data":"UklGRg==","sampleRate":24000,"numChannels":1,"samplesPerChannel":480}}"""),
            BuildRequest("req-direct-realtime-handoff-1", "handoffRealtimeOutput", """{"threadId":"thread-realtime-direct-1","sessionId":"realtime-session-1","handoffId":"handoff-direct-1","output":"delegated result"}"""),
            BuildRequest("req-direct-realtime-stop-1", "stopRealtime", """{"threadId":"thread-realtime-direct-1","sessionId":"realtime-session-1"}"""),
        };

        foreach (var request in requests)
        {
            await ReflectionTestHelper.AwaitTaskResultAsync(ReflectionTestHelper.InvokeMethod(host, "HandleRequestAsync", request));
        }

        var startRequest = Assert.Single(runtime.RealtimeStartCalls);
        Assert.Equal("thread-realtime-direct-1", startRequest.ThreadId.Value);
        Assert.Equal("realtime-session-1", startRequest.SessionId);
        Assert.Equal("开始语音协作", startRequest.Prompt);

        var appendTextRequest = Assert.Single(runtime.RealtimeAppendTextCalls);
        Assert.Equal("thread-realtime-direct-1", appendTextRequest.ThreadId.Value);
        Assert.Equal("继续输出", appendTextRequest.Text);

        var appendAudioRequest = Assert.Single(runtime.RealtimeAppendAudioCalls);
        Assert.Equal("thread-realtime-direct-1", appendAudioRequest.ThreadId.Value);
        Assert.Equal("UklGRg==", appendAudioRequest.Audio.Data);
        Assert.Equal(24000, appendAudioRequest.Audio.SampleRate);

        var handoffRequest = Assert.Single(runtime.RealtimeHandoffOutputCalls);
        Assert.Equal("handoff-direct-1", handoffRequest.HandoffId);
        Assert.Equal("delegated result", handoffRequest.Output);

        var stopRequest = Assert.Single(runtime.RealtimeStopCalls);
        Assert.Equal("thread-realtime-direct-1", stopRequest.ThreadId.Value);
        Assert.Equal("realtime-session-1", stopRequest.SessionId);

        var responseLines = output.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(requests.Length, responseLines.Length);
        foreach (var responseLine in responseLines)
        {
            using var responseDocument = JsonDocument.Parse(responseLine);
            Assert.Equal("response", responseDocument.RootElement.GetProperty("messageType").GetString());
            Assert.True(responseDocument.RootElement.GetProperty("success").GetBoolean());
        }
    }

    [Fact]
    public async Task HandleRequestAsync_WhenCollaborationFormalCommandsArrive_UseTypedControlPlaneRequests()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var runtime = new CliConsumerFakeRuntime
        {
            CreateCollaborationSpaceAsyncHandler = (command, _) => Task.FromResult(
                new CollaborationSpace(command.SpaceId, command.Key, command.DisplayName, command.Profile, command.Defaults, command.PolicyRef)),
            ConfigureCollaborationSpaceAsyncHandler = (command, _) => Task.FromResult(
                new CollaborationSpace(
                    command.SpaceId,
                    "team-alpha",
                    command.DisplayName ?? "Team Alpha",
                    command.Profile ?? new CollaborationSpaceProfile("Updated purpose"),
                    command.Defaults ?? CollaborationDefaultSet.Empty,
                    command.PolicyRef)),
            ArchiveCollaborationSpaceAsyncHandler = (_, _) => Task.FromResult(true),
            BindParticipantToSessionAsyncHandler = (_, _) => Task.FromResult(true),
            BindParticipantToWorkflowAsyncHandler = (_, _) => Task.FromResult(true),
            UpdateParticipantRoleAsyncHandler = (_, _) => Task.FromResult(true),
        };
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        object BuildRequest(string requestId, string command, string payloadJson)
        {
            var request = Activator.CreateInstance(requestType);
            Assert.NotNull(request);
            ReflectionTestHelper.SetProperty(request!, "RequestId", requestId);
            ReflectionTestHelper.SetProperty(request, "Command", command);
            ReflectionTestHelper.SetProperty(request, "Payload", ReflectionTestHelper.ParseJsonElement(payloadJson));
            return request!;
        }

        await ReflectionTestHelper.AwaitTaskResultAsync(ReflectionTestHelper.InvokeMethod(host, "HandleRequestAsync", BuildRequest("req-collab-create-1", "createCollaborationSpace", """{"spaceId":"space-direct-1","key":"team-alpha","displayName":"Team Alpha","purpose":"Cross repo collaboration","defaultWorkspace":"D:/Repos/TianShu","defaultExecutionProfile":"review","policyKey":"policy-alpha"}""")));
        await ReflectionTestHelper.AwaitTaskResultAsync(ReflectionTestHelper.InvokeMethod(host, "HandleRequestAsync", BuildRequest("req-collab-configure-1", "configureCollaborationSpace", """{"spaceId":"space-direct-1","displayName":"Team Alpha v2","purpose":"Updated purpose"}""")));
        await ReflectionTestHelper.AwaitTaskResultAsync(ReflectionTestHelper.InvokeMethod(host, "HandleRequestAsync", BuildRequest("req-collab-archive-1", "archiveCollaborationSpace", """{"spaceId":"space-direct-1"}""")));
        await ReflectionTestHelper.AwaitTaskResultAsync(ReflectionTestHelper.InvokeMethod(host, "HandleRequestAsync", BuildRequest("req-bind-session-1", "bindParticipantToSession", """{"sessionId":"session-direct-1","participantId":"participant-direct-1"}""")));
        await ReflectionTestHelper.AwaitTaskResultAsync(ReflectionTestHelper.InvokeMethod(host, "HandleRequestAsync", BuildRequest("req-bind-workflow-1", "bindParticipantToWorkflow", """{"workflowId":"workflow-direct-1","participantId":"participant-direct-1"}""")));
        await ReflectionTestHelper.AwaitTaskResultAsync(ReflectionTestHelper.InvokeMethod(host, "HandleRequestAsync", BuildRequest("req-update-role-1", "updateParticipantRole", """{"participantId":"participant-direct-1","role":"owner"}""")));

        Assert.Equal("space-direct-1", Assert.Single(runtime.CollaborationSpaceCreateCalls).SpaceId.Value);
        Assert.Equal("Team Alpha v2", Assert.Single(runtime.CollaborationSpaceConfigureCalls).DisplayName);
        Assert.Equal("space-direct-1", Assert.Single(runtime.CollaborationSpaceArchiveCalls).SpaceId.Value);
        Assert.Equal("session-direct-1", Assert.Single(runtime.ParticipantSessionBindCalls).SessionId.Value);
        Assert.Equal("workflow-direct-1", Assert.Single(runtime.ParticipantWorkflowBindCalls).WorkflowId.Value);
        Assert.Equal("owner", Assert.Single(runtime.ParticipantRoleUpdateCalls).Role);

        var responseLines = output.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(6, responseLines.Length);
        foreach (var responseLine in responseLines)
        {
            using var responseDocument = JsonDocument.Parse(responseLine);
            Assert.Equal("response", responseDocument.RootElement.GetProperty("messageType").GetString());
            Assert.True(responseDocument.RootElement.GetProperty("success").GetBoolean());
        }
    }

    [Fact]
    public async Task HandleRequestAsync_WhenWorkflowFormalWriteCommandsArrive_UseTypedControlPlaneRequests()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var runtime = new CliConsumerFakeRuntime
        {
            CreateWorkflowAsyncHandler = (request, _) => Task.FromResult(
                new Workflow(
                    request.WorkflowId,
                    new CollaborationSpaceRef(request.CollaborationSpaceId, request.CollaborationSpaceId.Value, "Workflow Direct Space"),
                    request.DisplayName,
                    WorkflowState.Draft,
                    request.OwnerParticipant,
                    request.ThreadId)),
            PublishPlanAsyncHandler = (request, _) => Task.FromResult(new PlanProjection(request.WorkflowId, request.Plan)),
            CreateTaskAsyncHandler = (request, _) => Task.FromResult(request.Task),
            UpdateTaskStateAsyncHandler = (request, _) => Task.FromResult<TianShu.Contracts.Workflows.Task?>(
                new TianShu.Contracts.Workflows.Task(
                    request.TaskId,
                    new WorkflowId("workflow-direct-write-1"),
                    "Mirror workflow direct commands",
                    request.State,
                    request.OwnerParticipant)),
        };
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        object BuildRequest(string requestId, string command, string payloadJson)
        {
            var request = Activator.CreateInstance(requestType);
            Assert.NotNull(request);
            ReflectionTestHelper.SetProperty(request!, "RequestId", requestId);
            ReflectionTestHelper.SetProperty(request, "Command", command);
            ReflectionTestHelper.SetProperty(request, "Payload", ReflectionTestHelper.ParseJsonElement(payloadJson));
            return request!;
        }

        await ReflectionTestHelper.AwaitTaskResultAsync(ReflectionTestHelper.InvokeMethod(host, "HandleRequestAsync", BuildRequest("req-workflow-create-1", "createWorkflow", """{"workflowId":"workflow-direct-write-1","spaceId":"space-direct-write-1","displayName":"Workflow Direct Write","threadId":"thread-direct-write-1","participantId":"participant-direct-write-1"}""")));
        await ReflectionTestHelper.AwaitTaskResultAsync(ReflectionTestHelper.InvokeMethod(host, "HandleRequestAsync", BuildRequest("req-workflow-plan-1", "publishWorkflowPlan", """{"workflowId":"workflow-direct-write-1","title":"Workflow Direct Plan","steps":[{"title":"Mirror workflow direct commands","description":"sidecar direct host mirror"}]}""")));
        await ReflectionTestHelper.AwaitTaskResultAsync(ReflectionTestHelper.InvokeMethod(host, "HandleRequestAsync", BuildRequest("req-workflow-task-create-1", "createWorkflowTask", """{"taskId":"task-direct-write-1","workflowId":"workflow-direct-write-1","title":"Mirror workflow direct commands","state":"in-progress","participantId":"participant-direct-write-1"}""")));
        await ReflectionTestHelper.AwaitTaskResultAsync(ReflectionTestHelper.InvokeMethod(host, "HandleRequestAsync", BuildRequest("req-workflow-task-update-1", "updateWorkflowTaskState", """{"taskId":"task-direct-write-1","state":"done","participantId":"participant-direct-write-1"}""")));

        var createRequest = Assert.Single(runtime.WorkflowCreateCalls);
        Assert.Equal("workflow-direct-write-1", createRequest.WorkflowId.Value);
        Assert.Equal("space-direct-write-1", createRequest.CollaborationSpaceId.Value);
        Assert.Equal("thread-direct-write-1", createRequest.ThreadId?.Value);
        Assert.Equal("participant-direct-write-1", createRequest.OwnerParticipant?.Id.Value);

        var publishRequest = Assert.Single(runtime.WorkflowPublishPlanCalls);
        Assert.Equal("Workflow Direct Plan", publishRequest.Plan.Title);
        Assert.Equal("Mirror workflow direct commands", Assert.Single(publishRequest.Plan.Steps).Title);

        var createTaskRequest = Assert.Single(runtime.WorkflowCreateTaskCalls);
        Assert.Equal("task-direct-write-1", createTaskRequest.Task.Id.Value);
        Assert.Equal(TaskState.InProgress, createTaskRequest.Task.State);

        var updateTaskRequest = Assert.Single(runtime.WorkflowUpdateTaskStateCalls);
        Assert.Equal("task-direct-write-1", updateTaskRequest.TaskId.Value);
        Assert.Equal(TaskState.Done, updateTaskRequest.State);
        Assert.Equal("participant-direct-write-1", updateTaskRequest.OwnerParticipant?.Id.Value);

        var responseLines = output.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, responseLines.Length);

        Assert.Contains("Workflow Direct Write", responseLines[0], StringComparison.Ordinal);
        Assert.Contains("Workflow Direct Plan", responseLines[1], StringComparison.Ordinal);
        Assert.Contains("Mirror workflow direct commands", responseLines[2], StringComparison.Ordinal);
        Assert.Contains("Mirror workflow direct commands", responseLines[3], StringComparison.Ordinal);

        foreach (var responseLine in responseLines)
        {
            using var responseDocument = JsonDocument.Parse(responseLine);
            Assert.Equal("response", responseDocument.RootElement.GetProperty("messageType").GetString());
            Assert.True(responseDocument.RootElement.GetProperty("success").GetBoolean());
        }
    }

    [Fact]
    public async Task HandleRequestAsync_WhenAgentFormalCommandsArrive_UseTypedControlPlaneRequests()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var runtime = new CliConsumerFakeRuntime
        {
            RegisterAgentThreadAsyncHandler = (request, _) => Task.FromResult(
                new ControlPlaneAgentThreadRegistrationResult
                {
                    Agent = new ControlPlaneAgentDescriptor
                    {
                        ThreadId = request.ThreadId,
                        AgentNickname = request.AgentNickname,
                        AgentRole = request.AgentRole,
                        UpdatedAt = new DateTimeOffset(2026, 4, 29, 0, 0, 0, TimeSpan.Zero),
                    },
                }),
            CreateAgentJobAsyncHandler = (request, _) => Task.FromResult(
                new ControlPlaneJobOperationResult
                {
                    Job = new ControlPlaneJobDetails
                    {
                        Id = request.JobId ?? new JobId("job-direct-1"),
                        Name = request.Name ?? "direct-job",
                        Status = "created",
                        Instruction = request.Instruction,
                    },
                }),
            DispatchAgentJobAsyncHandler = (request, _) => Task.FromResult(
                new ControlPlaneJobOperationResult
                {
                    Items =
                    [
                        new ControlPlaneJobItemDetails
                        {
                            ItemId = new JobItemId("item-direct-1"),
                            AssignedThreadId = request.ThreadIds[0],
                            Status = "running",
                        },
                    ],
                }),
            ReportAgentJobItemAsyncHandler = (request, _) => Task.FromResult(
                new ControlPlaneJobOperationResult
                {
                    Item = new ControlPlaneJobItemDetails
                    {
                        ItemId = request.ItemId,
                        Status = request.Status,
                        Result = request.Result,
                    },
                }),
            ReadAgentJobAsyncHandler = (request, _) => Task.FromResult(
                new ControlPlaneJobOperationResult
                {
                    Job = new ControlPlaneJobDetails
                    {
                        Id = request.JobId,
                        Name = "direct-job",
                        Status = "completed",
                        Instruction = "Check completion",
                    },
                }),
        };
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        object BuildRequest(string requestId, string command, string payloadJson)
        {
            var request = Activator.CreateInstance(requestType);
            Assert.NotNull(request);
            ReflectionTestHelper.SetProperty(request!, "RequestId", requestId);
            ReflectionTestHelper.SetProperty(request, "Command", command);
            ReflectionTestHelper.SetProperty(request, "Payload", ReflectionTestHelper.ParseJsonElement(payloadJson));
            return request!;
        }

        await ReflectionTestHelper.AwaitTaskResultAsync(ReflectionTestHelper.InvokeMethod(host, "HandleRequestAsync", BuildRequest("req-agent-thread-1", "registerAgentThread", """{"threadId":"thread-direct-1","agentNickname":"Direct Hermes","agentRole":"reviewer"}""")));
        await ReflectionTestHelper.AwaitTaskResultAsync(ReflectionTestHelper.InvokeMethod(host, "HandleRequestAsync", BuildRequest("req-agent-job-create-1", "createAgentJob", """{"jobId":"job-direct-1","name":"direct-job","instruction":"Check all pending files","items":[{"path":"src/A.cs"},{"path":"src/B.cs"}],"autoExport":true}""")));
        await ReflectionTestHelper.AwaitTaskResultAsync(ReflectionTestHelper.InvokeMethod(host, "HandleRequestAsync", BuildRequest("req-agent-job-dispatch-1", "dispatchAgentJob", """{"jobId":"job-direct-1","threadIds":["thread-direct-1","THREAD-DIRECT-1","thread-direct-2"]}""")));
        await ReflectionTestHelper.AwaitTaskResultAsync(ReflectionTestHelper.InvokeMethod(host, "HandleRequestAsync", BuildRequest("req-agent-job-report-1", "reportAgentJobItem", """{"jobId":"job-direct-1","itemId":"item-direct-1","status":"completed","result":{"score":98}}""")));
        await ReflectionTestHelper.AwaitTaskResultAsync(ReflectionTestHelper.InvokeMethod(host, "HandleRequestAsync", BuildRequest("req-agent-job-read-1", "readAgentJob", """{"jobId":"job-direct-1"}""")));

        var registerRequest = Assert.Single(runtime.AgentThreadRegistrationCalls);
        Assert.Equal("thread-direct-1", registerRequest.ThreadId.Value);
        Assert.Equal("Direct Hermes", registerRequest.AgentNickname);
        Assert.Equal("reviewer", registerRequest.AgentRole);

        var createRequest = Assert.Single(runtime.AgentJobCreateCalls);
        Assert.Equal("job-direct-1", createRequest.JobId?.Value);
        Assert.Equal("direct-job", createRequest.Name);
        Assert.Equal("Check all pending files", createRequest.Instruction);
        Assert.True(createRequest.AutoExport);
        Assert.Equal(2, createRequest.Items.Count);

        var dispatchRequest = Assert.Single(runtime.AgentJobDispatchCalls);
        Assert.Equal("job-direct-1", dispatchRequest.JobId.Value);
        Assert.Equal(new[] { "thread-direct-1", "thread-direct-2" }, dispatchRequest.ThreadIds.Select(static item => item.Value).ToArray());

        var reportRequest = Assert.Single(runtime.AgentJobItemReportCalls);
        Assert.Equal("item-direct-1", reportRequest.ItemId.Value);
        Assert.Equal("completed", reportRequest.Status);

        var readRequest = Assert.Single(runtime.AgentJobReadCalls);
        Assert.Equal("job-direct-1", readRequest.JobId.Value);

        var responseLines = output.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(5, responseLines.Length);

        using (var registerResponse = JsonDocument.Parse(responseLines[0]))
        {
            Assert.Equal("thread-direct-1", registerResponse.RootElement.GetProperty("data").GetProperty("thread").GetProperty("id").GetString());
        }

        using (var createResponse = JsonDocument.Parse(responseLines[1]))
        {
            Assert.Equal("job-direct-1", createResponse.RootElement.GetProperty("data").GetProperty("job").GetProperty("id").GetString());
        }

        using (var dispatchResponse = JsonDocument.Parse(responseLines[2]))
        {
            Assert.Equal("item-direct-1", dispatchResponse.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("itemId").GetString());
        }

        using (var reportResponse = JsonDocument.Parse(responseLines[3]))
        {
            Assert.Equal("completed", reportResponse.RootElement.GetProperty("data").GetProperty("item").GetProperty("status").GetString());
        }

        using (var readResponse = JsonDocument.Parse(responseLines[4]))
        {
            Assert.Equal("completed", readResponse.RootElement.GetProperty("data").GetProperty("job").GetProperty("status").GetString());
        }
    }

    [Fact]
    public async Task HandleRequestAsync_WhenReadThreadCommandArrives_UsesTypedRuntimeRequest()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var runtime = new CliConsumerFakeRuntime
        {
            ReadThreadAsyncHandler = static (request, _) => Task.FromResult(
                new ControlPlaneThreadOperationResult
                {
                    Thread = new ControlPlaneThreadDetail
                    {
                        ThreadId = request.ThreadId,
                    },
                }),
        };
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        var request = Activator.CreateInstance(requestType);
        Assert.NotNull(request);
        ReflectionTestHelper.SetProperty(request!, "RequestId", "req-read-thread-1");
        ReflectionTestHelper.SetProperty(request, "Command", "readThread");
        ReflectionTestHelper.SetProperty(
            request,
            "Payload",
            ReflectionTestHelper.ParseJsonElement("""{"threadId":"thread-gap01-1","includeTurns":true}"""));

        var task = ReflectionTestHelper.InvokeMethod(host!, "HandleRequestAsync", request);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        var readRequest = Assert.Single(runtime.ThreadReadCalls);
        Assert.Equal("thread-gap01-1", readRequest.ThreadId.Value);
        Assert.True(readRequest.IncludeTurns);

        using var responseDocument = JsonDocument.Parse(Assert.Single(
            output.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)));
        Assert.Equal("response", responseDocument.RootElement.GetProperty("messageType").GetString());
        Assert.True(responseDocument.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("thread-gap01-1", responseDocument.RootElement.GetProperty("data").GetProperty("thread").GetProperty("id").GetString());
    }

    [Fact]
    public async Task HandleRequestAsync_WhenListThreadsCommandArrives_WithExplicitCwd_UsesTypedRuntimeRequest()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var runtime = new CliConsumerFakeRuntime
        {
            ListThreadsRequestAsyncHandler = static (request, _) => Task.FromResult(
                new ControlPlaneThreadListResult
                {
                    Threads =
                    [
                        new ControlPlaneThreadSummary
                        {
                            ThreadId = new ThreadId("thread-list-cwd-1"),
                            Preview = "list cwd",
                            WorkingDirectory = request.WorkingDirectory,
                            Source = ControlPlaneThreadSourceKind.SubAgentReview,
                            UpdatedAt = DateTimeOffset.FromUnixTimeSeconds(1774160400),
                        },
                    ],
                    NextCursor = "cursor-next-1",
                }),
        };
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        var request = Activator.CreateInstance(requestType);
        Assert.NotNull(request);
        ReflectionTestHelper.SetProperty(request!, "RequestId", "req-list-threads-1");
        ReflectionTestHelper.SetProperty(request, "Command", "listThreads");
        ReflectionTestHelper.SetProperty(
            request,
            "Payload",
            ReflectionTestHelper.ParseJsonElement("""{"limit":3,"archived":true,"cwd":"D:/explicit/repo","matchCurrentCwd":false,"sortKey":"updated_at","sourceKinds":["subAgentReview","appServer","subAgentReview"],"searchTerm":"gap"}"""));

        var task = ReflectionTestHelper.InvokeMethod(host!, "HandleRequestAsync", request);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        var listRequest = Assert.Single(runtime.ThreadListRequestCalls);
        Assert.Equal(3, listRequest.Limit);
        Assert.True(listRequest.Archived);
        Assert.Equal("D:/explicit/repo", listRequest.WorkingDirectory);
        Assert.Equal("updated_at", listRequest.SortKey);
        Assert.Equal(["subAgentReview", "appServer"], listRequest.SourceKinds);
        Assert.Equal("gap", listRequest.SearchTerm);

        using var responseDocument = JsonDocument.Parse(Assert.Single(
            output.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)));
        Assert.Equal("response", responseDocument.RootElement.GetProperty("messageType").GetString());
        Assert.True(responseDocument.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("cursor-next-1", responseDocument.RootElement.GetProperty("data").GetProperty("nextCursor").GetString());
        var thread = responseDocument.RootElement.GetProperty("data").GetProperty("threads")[0];
        Assert.Equal(
            "D:/explicit/repo",
            thread.GetProperty("cwd").GetString());
        Assert.Equal("subAgentReview", thread.GetProperty("source").GetString());
    }

    [Fact]
    public async Task HandleRequestAsync_WhenUpdateThreadMetadataCommandArrives_UsesTypedRuntimeRequest()
    {
        var hostType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.StdioSidecarHost");
        var requestType = ReflectionTestHelper.GetRequiredType(SidecarAssembly, "TianShu.VSSDK.Sidecar.SidecarRequestEnvelope");
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Activator.CreateInstance(
            hostType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [input, output, error],
            culture: null);
        Assert.NotNull(host);

        var runtime = new CliConsumerFakeRuntime
        {
            UpdateThreadMetadataAsyncHandler = static (request, _) => Task.FromResult(
                new ControlPlaneThreadOperationResult
                {
                    Thread = new ControlPlaneThreadDetail
                    {
                        ThreadId = request.ThreadId,
                    },
                }),
        };
        ReflectionTestHelper.SetField(host!, "runtime", runtime);

        var request = Activator.CreateInstance(requestType);
        Assert.NotNull(request);
        ReflectionTestHelper.SetProperty(request!, "RequestId", "req-update-thread-metadata-1");
        ReflectionTestHelper.SetProperty(request, "Command", "updateThreadMetadata");
        ReflectionTestHelper.SetProperty(
            request,
            "Payload",
            ReflectionTestHelper.ParseJsonElement(
                """
                {
                  "threadId": "thread-gap01-meta-1",
                  "hasGitSha": true,
                  "gitSha": "abc123",
                  "hasGitBranch": true,
                  "gitBranch": "feature/gap01",
                  "hasGitOriginUrl": true,
                  "gitOriginUrl": "https://example.com/repo.git"
                }
                """));

        var task = ReflectionTestHelper.InvokeMethod(host!, "HandleRequestAsync", request);
        await ReflectionTestHelper.AwaitTaskResultAsync(task);

        var metadataRequest = Assert.Single(runtime.ThreadMetadataUpdateCalls);
        Assert.Equal("thread-gap01-meta-1", metadataRequest.ThreadId.Value);
        Assert.True(metadataRequest.HasGitSha);
        Assert.Equal("abc123", metadataRequest.GitSha);
        Assert.True(metadataRequest.HasGitBranch);
        Assert.Equal("feature/gap01", metadataRequest.GitBranch);
        Assert.True(metadataRequest.HasGitOriginUrl);
        Assert.Equal("https://example.com/repo.git", metadataRequest.GitOriginUrl);

        using var responseDocument = JsonDocument.Parse(Assert.Single(
            output.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)));
        Assert.Equal("response", responseDocument.RootElement.GetProperty("messageType").GetString());
        Assert.True(responseDocument.RootElement.GetProperty("success").GetBoolean());
    }

    private static ControlPlaneInputItem ToControlPlaneInputItem(AgentUserInput input)
        => input switch
        {
            TextUserInput text => new ControlPlaneTextInput(
                text.Text,
                text.TextElements.Select(static element => new ControlPlaneTextElement(
                    new ControlPlaneByteRange(element.ByteRange.Start, element.ByteRange.End),
                    element.Placeholder)).ToArray()),
            ImageUserInput image => new ControlPlaneImageInput(image.Url),
            LocalImageUserInput localImage => new ControlPlaneLocalImageInput(localImage.Path),
            SkillUserInput skill => new ControlPlaneSkillInput(skill.Name, skill.Path),
            MentionUserInput mention => new ControlPlaneMentionInput(mention.Name, mention.Path),
            _ => new ControlPlaneTextInput(input.Type),
        };

    private static ControlPlaneConversationStreamEvent CreateStreamEvent(
        ControlPlaneConversationStreamEventKind kind,
        string? threadId = null,
        string? turnId = null,
        string? callId = null,
        string? toolName = null,
        string? text = null,
        string? status = null,
        string? message = null,
        bool? requiresApproval = null,
        string? approvalKind = null,
        IReadOnlyList<string>? availableDecisions = null,
        IReadOnlyList<ControlPlaneApprovalDecisionOption>? availableDecisionOptions = null,
        ControlPlaneConversationStreamPayloadKind? payloadKind = null,
        object? payload = null,
        string? dataJson = null,
        string? metadataJson = null,
        string? rawJson = null,
        ControlPlaneThreadTurnError? turnError = null)
        => new()
        {
            Kind = kind,
            Timestamp = DateTimeOffset.Now,
            ThreadId = string.IsNullOrWhiteSpace(threadId) ? null : new ThreadId(threadId),
            TurnId = string.IsNullOrWhiteSpace(turnId) ? null : new TurnId(turnId),
            CallId = string.IsNullOrWhiteSpace(callId) ? null : new CallId(callId),
            ToolName = toolName,
            Text = text,
            Status = status,
            Message = message,
            RequiresApproval = requiresApproval,
            ApprovalKind = approvalKind,
            AvailableDecisions = availableDecisions,
            AvailableDecisionOptions = availableDecisionOptions,
            PayloadKind = payloadKind,
            Payload = payload switch
            {
                null => null,
                StructuredValue structured => structured,
                _ => StructuredValue.FromJsonElement(JsonSerializer.SerializeToElement(payload, StructuredJsonOptions)),
            },
            Diagnostics = string.IsNullOrWhiteSpace(dataJson)
                && string.IsNullOrWhiteSpace(metadataJson)
                && string.IsNullOrWhiteSpace(rawJson)
                ? null
                : new ControlPlaneConversationStreamDiagnostics
                {
                    DataJson = dataJson,
                    MetadataJson = metadataJson,
                    RawJson = rawJson,
                },
            TurnError = turnError,
        };

    private static ControlPlanePendingInputState? ToControlPlanePendingInputState(PendingInputStatePayload? payload)
        => payload is null
            ? null
            : new ControlPlanePendingInputState(
                payload.Entries.Select(ToControlPlanePendingInputStateEntry).ToArray(),
                payload.InterruptRequestPending,
                payload.SubmitPendingSteersAfterInterrupt,
                payload.QueuedUserMessages?.Select(ToControlPlanePendingInputStateEntry).ToArray(),
                payload.PendingSteers?.Select(ToControlPlanePendingInputStateEntry).ToArray());

    private static ControlPlanePendingInputStateEntry ToControlPlanePendingInputStateEntry(PendingInputStateEntryPayload entry)
        => new(
            entry.CorrelationId,
            entry.RequestedMode,
            entry.EffectiveMode,
            entry.LifecycleState,
            entry.ExpectedTurnId,
            entry.TurnId,
            entry.CompareKey is null
                ? null
                : StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["message"] = entry.CompareKey.Message,
                    ["imageCount"] = entry.CompareKey.ImageCount,
                }),
            entry.PendingBucket,
            entry.Inputs?.Select(ToControlPlaneInputItem).ToArray());

}
