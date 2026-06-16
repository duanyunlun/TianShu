using System.Text.Json;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Provider;
using TianShu.Contracts.Primitives;
using TianShu.Execution.Runtime.Providers;
using TianShu.Execution.Runtime.Events;

namespace TianShu.Execution.Integration.Tests;

public sealed class ProviderExecutionEventProjectorTests
{
    private static readonly JsonSerializerOptions StructuredJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Project_TextDelta_ProducesAssistantTextDeltaEvent()
    {
        var projector = new ProviderExecutionEventProjector();

        var events = projector.Project(
            new ProviderEventProjectionContext(
                ThreadId: "thread-provider-001",
                TurnId: "turn-provider-001",
                ItemId: "item-provider-text-001",
                Phase: "final_answer",
                SourceMethod: "provider/text_delta",
                RawJson: "{\"kind\":\"text_delta\"}"),
            new ProviderTextDeltaEvent("hello from provider"));

        var streamEvent = Assert.Single(events);
        Assert.Equal(ControlPlaneConversationStreamEventKind.AssistantTextDelta, streamEvent.Kind);
        Assert.Equal("thread-provider-001", streamEvent.ThreadId?.Value);
        Assert.Equal("turn-provider-001", streamEvent.TurnId?.Value);
        Assert.Equal("item-provider-text-001", streamEvent.ItemId);
        Assert.Equal("hello from provider", streamEvent.Text);
        Assert.Equal("final_answer", streamEvent.Phase);
        Assert.Equal("provider/text_delta", streamEvent.SourceMethod);
        Assert.Equal("{\"kind\":\"text_delta\"}", streamEvent.Diagnostics?.RawJson);
    }

    [Fact]
    public void Project_ReasoningDelta_ProducesReasoningEvent()
    {
        var projector = new ProviderExecutionEventProjector();

        var events = projector.Project(
            new ProviderEventProjectionContext(
                ThreadId: "thread-provider-002",
                TurnId: "turn-provider-002",
                ItemId: "item-provider-reasoning-001",
                Status: "reasoning",
                Phase: "reasoning",
                SourceMethod: "provider/reasoning_delta",
                SummaryIndex: 2,
                ContentIndex: 5),
            new ProviderReasoningDeltaEvent("thinking..."));

        var streamEvent = Assert.Single(events);
        Assert.Equal(ControlPlaneConversationStreamEventKind.ReasoningDelta, streamEvent.Kind);
        Assert.Equal("thinking...", streamEvent.Text);
        Assert.Equal(ControlPlaneConversationStreamPayloadKind.Reasoning, streamEvent.PayloadKind);
        var reasoning = DeserializePayload<ReasoningEventPayload>(streamEvent.Payload);
        Assert.NotNull(reasoning);
        Assert.Equal("item-provider-reasoning-001", reasoning!.ItemId);
        Assert.Equal("reasoning", reasoning.Status);
        Assert.Equal("reasoning", reasoning.Phase);
        Assert.Equal("provider/reasoning_delta", reasoning.SourceMethod);
        Assert.Equal(2, reasoning.SummaryIndex);
        Assert.Equal(5, reasoning.ContentIndex);
    }

    [Fact]
    public void Project_ToolDirectiveWithApproval_ProducesToolLifecycleAndApprovalEvents()
    {
        var projector = new ProviderExecutionEventProjector();
        var directive = new ProviderToolDirective(
            new CallId("call-provider-001"),
            "shell",
            StructuredValue.FromObject(
                new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                {
                    ["command"] = StructuredValue.FromString("dir"),
                }),
            requiresApproval: true);

        var events = projector.Project(
            new ProviderEventProjectionContext(
                ThreadId: "thread-provider-003",
                TurnId: "turn-provider-003",
                ItemId: "item-provider-tool-001",
                Phase: "tool_call",
                SourceMethod: "provider/tool_directive"),
            new ProviderToolDirectiveEvent(directive));

        Assert.Equal(2, events.Count);

        var started = events[0];
        Assert.Equal(ControlPlaneConversationStreamEventKind.ToolCallStarted, started.Kind);
        Assert.Equal("call-provider-001", started.CallId?.Value);
        Assert.Equal("shell", started.ToolName);
        Assert.Equal(ControlPlaneConversationStreamPayloadKind.ToolCall, started.PayloadKind);
        var startedToolCall = DeserializePayload<ToolCallEventPayload>(started.Payload);
        Assert.NotNull(startedToolCall);
        Assert.Contains("command", startedToolCall!.InputText, StringComparison.Ordinal);
        Assert.Contains("dir", startedToolCall.InputText, StringComparison.Ordinal);

        var approval = events[1];
        Assert.Equal(ControlPlaneConversationStreamEventKind.ApprovalRequested, approval.Kind);
        Assert.Equal("call-provider-001", approval.CallId?.Value);
        Assert.Equal("shell", approval.ToolName);
        Assert.True(approval.RequiresApproval);
        Assert.Equal(ControlPlaneConversationStreamPayloadKind.ApprovalRequest, approval.PayloadKind);
        var approvalRequest = DeserializePayload<ApprovalRequestPayload>(approval.Payload);
        Assert.NotNull(approvalRequest);
        Assert.Contains("dir", approvalRequest!.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_ToolDirectiveWithScalarInput_PreservesDisplayTextWithoutJsonQuotes()
    {
        var projector = new ProviderExecutionEventProjector();
        var directive = new ProviderToolDirective(
            new CallId("call-provider-001b"),
            "shell",
            StructuredValue.FromString("echo hi"),
            requiresApproval: true);

        var events = projector.Project(
            new ProviderEventProjectionContext(
                ThreadId: "thread-provider-003b",
                TurnId: "turn-provider-003b",
                ItemId: "item-provider-tool-001b",
                Status: "awaitingApproval",
                SourceMethod: "item/tool/requestApproval"),
            new ProviderToolDirectiveEvent(directive));

        Assert.Equal(2, events.Count);

        var started = events[0];
        Assert.Equal(ControlPlaneConversationStreamEventKind.ToolCallStarted, started.Kind);
        Assert.Equal("echo hi", started.Text);
        var startedToolCall = DeserializePayload<ToolCallEventPayload>(started.Payload);
        Assert.NotNull(startedToolCall);
        Assert.Equal("echo hi", startedToolCall!.InputText);
        Assert.Equal("request_approval", startedToolCall.Phase);

        var approval = events[1];
        Assert.Equal(ControlPlaneConversationStreamEventKind.ApprovalRequested, approval.Kind);
        Assert.Equal("echo hi", approval.Text);
        var approvalRequest = DeserializePayload<ApprovalRequestPayload>(approval.Payload);
        Assert.NotNull(approvalRequest);
        Assert.Equal("echo hi", approvalRequest!.Summary);
        Assert.Equal("request_approval", approval.Phase);
    }

    [Fact]
    public void Project_ToolOutputDelta_ProducesToolCallOutputDeltaEvent()
    {
        var projector = new ProviderExecutionEventProjector();

        var events = projector.Project(
            new ProviderEventProjectionContext(
                ThreadId: "thread-provider-003c",
                TurnId: "turn-provider-003c",
                ItemId: "item-provider-tool-003c",
                Status: "delta",
                Phase: "tool_call",
                SourceMethod: "item/commandExecution/outputDelta"),
            new ProviderToolOutputDeltaEvent(
                new ProviderToolOutputDelta(
                    new CallId("call-provider-003c"),
                    "commandExecution",
                    "stdout line",
                    StructuredValue.FromString("dir /b"))));

        var streamEvent = Assert.Single(events);
        Assert.Equal(ControlPlaneConversationStreamEventKind.ToolCallOutputDelta, streamEvent.Kind);
        Assert.Equal("call-provider-003c", streamEvent.CallId?.Value);
        Assert.Equal("commandExecution", streamEvent.ToolName);
        Assert.Equal("stdout line", streamEvent.Text);
        var toolCall = DeserializePayload<ToolCallEventPayload>(streamEvent.Payload);
        Assert.NotNull(toolCall);
        Assert.Equal("dir /b", toolCall!.InputText);
        Assert.Equal("stdout line", toolCall.OutputText);
        Assert.Equal("item/commandExecution/outputDelta", streamEvent.SourceMethod);
    }

    [Fact]
    public void Project_ToolResult_ProducesToolCallCompletedEvent()
    {
        var projector = new ProviderExecutionEventProjector();

        var events = projector.Project(
            new ProviderEventProjectionContext(
                ThreadId: "thread-provider-003d",
                TurnId: "turn-provider-003d",
                ItemId: "item-provider-tool-003d",
                Status: "failed",
                Phase: "tool_call",
                SourceMethod: "item/completed"),
            new ProviderToolResultEvent(
                new ProviderToolResult(
                    new CallId("call-provider-003d"),
                    "shell",
                    StructuredValue.FromString("echo hi"),
                    StructuredValue.FromString("search failed"),
                    outputText: "search failed")));

        var streamEvent = Assert.Single(events);
        Assert.Equal(ControlPlaneConversationStreamEventKind.ToolCallCompleted, streamEvent.Kind);
        Assert.Equal("call-provider-003d", streamEvent.CallId?.Value);
        Assert.Equal("shell", streamEvent.ToolName);
        Assert.Equal("search failed", streamEvent.Text);
        Assert.Equal("failed", streamEvent.Status);
        var toolCall = DeserializePayload<ToolCallEventPayload>(streamEvent.Payload);
        Assert.NotNull(toolCall);
        Assert.Equal("echo hi", toolCall!.InputText);
        Assert.Equal("search failed", toolCall.OutputText);
        Assert.Equal("item/completed", streamEvent.SourceMethod);
    }

    [Fact]
    public void Project_Completion_ProducesAssistantTextCompletedEvent()
    {
        var projector = new ProviderExecutionEventProjector();

        var events = projector.Project(
            new ProviderEventProjectionContext(
                ThreadId: "thread-provider-004",
                TurnId: "turn-provider-004",
                ItemId: "item-provider-completion-001",
                Status: "completed",
                Phase: "final_answer",
                SourceMethod: "provider/completion"),
            new ProviderCompletionEvent(
                new ProviderCompletion(
                    "final answer",
                    new ProviderUsage(11, 23),
                    new ProviderTurnState("provider-thread-1", "provider-turn-1"))));

        var streamEvent = Assert.Single(events);
        Assert.Equal(ControlPlaneConversationStreamEventKind.AssistantTextCompleted, streamEvent.Kind);
        Assert.Equal("final answer", streamEvent.Text);
        Assert.Equal("completed", streamEvent.Status);
        Assert.Equal("final_answer", streamEvent.Phase);
        Assert.Equal("provider/completion", streamEvent.SourceMethod);
    }

    [Fact]
    public void Project_Failure_ProducesErrorEvent()
    {
        var projector = new ProviderExecutionEventProjector();

        var events = projector.Project(
            new ProviderEventProjectionContext(
                ThreadId: "thread-provider-005",
                TurnId: "turn-provider-005",
                SourceMethod: "provider/failure"),
            new ProviderFailureEvent(
                new ProviderFailure(
                    "stream_closed",
                    "stream closed before response.completed",
                    isRetryable: true,
                    additionalDetails: "websocket closed unexpectedly")));

        var streamEvent = Assert.Single(events);
        Assert.Equal(ControlPlaneConversationStreamEventKind.Error, streamEvent.Kind);
        Assert.Equal("thread-provider-005", streamEvent.ThreadId?.Value);
        Assert.Equal("turn-provider-005", streamEvent.TurnId?.Value);
        Assert.Equal("stream closed before response.completed", streamEvent.Message);
        Assert.Equal("error", streamEvent.Status);
        Assert.True(streamEvent.WillRetry);
        Assert.NotNull(streamEvent.TurnError);
        Assert.Equal("stream closed before response.completed", streamEvent.TurnError!.Message);
        Assert.Equal("websocket closed unexpectedly", streamEvent.TurnError.AdditionalDetails);
    }

    [Fact]
    public void Project_FailureWithoutAdditionalDetails_FallsBackToCode()
    {
        var projector = new ProviderExecutionEventProjector();

        var events = projector.Project(
            new ProviderEventProjectionContext(
                ThreadId: "thread-provider-006",
                TurnId: "turn-provider-006",
                SourceMethod: "provider/failure"),
            new ProviderFailureEvent(
                new ProviderFailure(
                    "stream_closed",
                    "stream closed before response.completed",
                    isRetryable: true)));

        var streamEvent = Assert.Single(events);
        Assert.NotNull(streamEvent.TurnError);
        Assert.Equal("stream_closed", streamEvent.TurnError!.AdditionalDetails);
    }

    private static T? DeserializePayload<T>(StructuredValue? payload)
        where T : class
    {
        if (payload is null)
        {
            return null;
        }

        var element = JsonSerializer.SerializeToElement(payload, StructuredJsonOptions);
        return element.Deserialize<T>(StructuredJsonOptions);
    }
}
