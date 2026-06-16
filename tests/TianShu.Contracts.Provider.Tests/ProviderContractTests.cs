using TianShu.Contracts.Primitives;
using TianShu.Contracts.Provider;
using TianShu.Contracts.Kernel;

namespace TianShu.Contracts.Provider.Tests;

public sealed class ProviderContractTests
{
    [Fact]
    public void ProviderInvocationRequest_RequiresInputs()
    {
        Assert.Throws<ArgumentException>(() => new ProviderInvocationRequest(
            new ExecutionId("execution-provider"),
            "openai",
            "gpt-5",
            new ProviderConversationContext(),
            Array.Empty<ProviderInputItem>()));
    }

    [Fact]
    public void ProviderCompletionEvent_PreservesUsageAndState()
    {
        var completion = new ProviderCompletion(
            "done",
            new ProviderUsage(10, 20, 5),
            new ProviderTurnState("provider-thread", "provider-turn"));
        var @event = new ProviderCompletionEvent(completion);

        Assert.Equal(20, @event.Completion.Usage?.OutputTokens);
        Assert.Equal("provider-turn", @event.Completion.TurnState?.ProviderTurnId);
    }

    [Fact]
    public void ProviderFailure_PreservesAdditionalDetails()
    {
        var failure = new ProviderFailure(
            "stream_closed",
            "stream closed before response.completed",
            isRetryable: true,
            additionalDetails: "websocket closed unexpectedly");

        Assert.Equal("stream_closed", failure.Code);
        Assert.Equal("websocket closed unexpectedly", failure.AdditionalDetails);
        Assert.True(failure.IsRetryable);
    }

    [Fact]
    public void ProviderToolOutputDeltaEvent_PreservesToolPayload()
    {
        var @event = new ProviderToolOutputDeltaEvent(
            new ProviderToolOutputDelta(
                new CallId("call-provider-delta"),
                "commandExecution",
                "line 1",
                StructuredValue.FromString("dir"),
                requiresApproval: false));

        Assert.Equal("call-provider-delta", @event.Delta.CallId.Value);
        Assert.Equal("commandExecution", @event.Delta.ToolKey);
        Assert.Equal("line 1", @event.Delta.OutputText);
        Assert.Equal("dir", @event.Delta.Input?.StringValue);
    }

    [Fact]
    public void ProviderToolResultEvent_PreservesToolResultPayload()
    {
        var @event = new ProviderToolResultEvent(
            new ProviderToolResult(
                new CallId("call-provider-result"),
                "shell",
                StructuredValue.FromString("echo hi"),
                StructuredValue.FromString("done"),
                outputText: "done",
                requiresApproval: true));

        Assert.Equal("call-provider-result", @event.Result.CallId.Value);
        Assert.Equal("shell", @event.Result.ToolKey);
        Assert.Equal("echo hi", @event.Result.Input?.StringValue);
        Assert.Equal("done", @event.Result.Output?.StringValue);
        Assert.Equal("done", @event.Result.OutputText);
        Assert.True(@event.Result.RequiresApproval);
    }

    [Fact]
    public void ProviderDescriptor_DeclaresPermissionSideEffectAndModels()
    {
        var descriptor = new ProviderDescriptor(
            "openai",
            "OpenAI",
            ProviderProtocolKind.OpenAiResponses,
            new ProviderCapabilityProfile(SupportsStreaming: true, SupportsTools: true),
            [new ProviderModelDescriptor("gpt-5")],
            new ProviderEndpointDescriptor("openai", ProviderProtocolKind.OpenAiResponses, "https://api.openai.com"),
            new PermissionEnvelope(["network.openai"], requiresHumanGate: false),
            new SideEffectProfile(SideEffectLevel.ExternalNetwork));

        Assert.Equal("openai", descriptor.ProviderId);
        Assert.True(descriptor.Capabilities.SupportsTools);
        Assert.Equal("gpt-5", Assert.Single(descriptor.Models).Name);
        Assert.Equal(SideEffectLevel.ExternalNetwork, descriptor.SideEffects.Level);
    }

    [Fact]
    public void ProviderDescriptor_DefaultsGovernedPermissionAndExternalNetworkSideEffect()
    {
        var descriptor = new ProviderDescriptor(
            "openai",
            "OpenAI",
            ProviderProtocolKind.OpenAiResponses,
            new ProviderCapabilityProfile(SupportsStreaming: true));

        Assert.NotEmpty(descriptor.Permission.Scopes);
        Assert.False(descriptor.Permission.RequiresHumanGate);
        Assert.Equal(SideEffectLevel.ExternalNetwork, descriptor.SideEffects.Level);
        Assert.Contains("network", descriptor.SideEffects.AffectedResources);
        Assert.True(descriptor.SideEffects.RequiresAudit);
    }

    [Fact]
    public void ProviderInvocationRequest_PreservesExecutionRuntimeContext()
    {
        var context = new ProviderInvocationContext(
            "step-provider-001",
            "intent-provider-001",
            "graph-provider-001",
            "stage-provider-001",
            "operation-provider-001",
            new PermissionEnvelope(["network.openai"], requiresHumanGate: false),
            new SideEffectProfile(SideEffectLevel.ExternalNetwork));
        var request = new ProviderInvocationRequest(
            new ExecutionId("execution-provider-context"),
            "openai",
            "gpt-5",
            new ProviderConversationContext(),
            [new TextProviderInputItem("hello")],
            invocationContext: context);

        Assert.Equal(context, request.InvocationContext);
        Assert.Equal("operation-provider-001", request.InvocationContext?.SourceKernelOperationId);
    }
}
