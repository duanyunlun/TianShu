using TianShu.Contracts.Execution;
using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Environment;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Modules;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Projections;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Provider;
using TianShu.Contracts.Tools;
using TianShu.Provider.Abstractions;

namespace TianShu.Execution.Runtime.Tests;

public sealed class RuntimeStepExecutionTests
{
    [Fact]
    public async Task ExecuteStepAsync_ShouldMaterializeAllApprovedRuntimeStepKinds()
    {
        var runtime = new TianShuExecutionRuntime();
        var context = CreateContext(SideEffectLevel.Privileged);
        var steps = CreateAllStepKinds().ToArray();

        foreach (var step in steps)
        {
            var result = await runtime.ExecuteStepAsync(step, context, CancellationToken.None);

            Assert.Equal(RuntimeStepResultStatus.Succeeded, result.Status);
            Assert.Equal(step.StepId, result.StepId);
            Assert.Equal(step.StepKind, result.StepKind);
            Assert.NotNull(result.Output);
            Assert.StartsWith("diagnostics://execution/", result.DiagnosticsRef, StringComparison.Ordinal);
            Assert.StartsWith("trace://execution/", result.TraceRef, StringComparison.Ordinal);
            Assert.True(result.Output!.TryGetProperty("runtimeBoundary", out var boundary));
            Assert.NotNull(boundary);
            Assert.Equal("execution.runtime.approved_step", boundary.StringValue);
        }
    }

    [Fact]
    public async Task ExecuteStepAsync_ShouldRejectStepWhenSideEffectExceedsGovernance()
    {
        var runtime = new TianShuExecutionRuntime();
        var context = CreateContext(SideEffectLevel.ReadOnly);
        var step = CreateToolStep("tool-external-mutation", SideEffectLevel.ExternalMutation);

        var result = await runtime.ExecuteStepAsync(step, context, CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Blocked, result.Status);
        Assert.NotNull(result.Failure);
        Assert.Equal("runtime_step_side_effect_exceeds_governance", result.Failure!.Code);
        Assert.StartsWith("diagnostics://execution/", result.DiagnosticsRef, StringComparison.Ordinal);
        Assert.StartsWith("trace://execution/", result.TraceRef, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteStepAsync_ShouldRejectToolOutsideGovernanceAllowList()
    {
        var runtime = new TianShuExecutionRuntime();
        var context = CreateContext(SideEffectLevel.Privileged, allowedToolIds: ["tool.other"]);
        var step = CreateToolStep("tool-not-allowed", SideEffectLevel.ReadOnly);

        var result = await runtime.ExecuteStepAsync(step, context, CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Blocked, result.Status);
        Assert.NotNull(result.Failure);
        Assert.Equal("runtime_step_capability_tool_not_allowed", result.Failure!.Code);
    }

    [Fact]
    public async Task ExecuteStepAsync_ShouldRejectModuleOutsideGovernanceAllowList()
    {
        var runtime = new TianShuExecutionRuntime();
        var context = CreateContext(SideEffectLevel.Privileged, allowedModuleIds: ["module.other"]);
        var step = new ModuleCapabilityStep(
            "module-not-allowed",
            SourceIntentId,
            SourceGraphId,
            SourceStageId,
            SourceKernelOperationId,
            "module.test",
            "capability.test",
            Payload("module"),
            Permission,
            SideEffect(SideEffectLevel.ReadOnly),
            Budget,
            OutputContract,
            TracePolicy);

        var result = await runtime.ExecuteStepAsync(step, context, CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Blocked, result.Status);
        Assert.NotNull(result.Failure);
        Assert.Equal("runtime_step_module_not_allowed", result.Failure!.Code);
    }

    [Fact]
    public async Task ExecuteStepAsync_ShouldRejectHumanGateStepWithoutApprovalReference()
    {
        var runtime = new TianShuExecutionRuntime();
        var context = CreateContext(SideEffectLevel.Privileged, requiresHumanGate: true);
        var step = CreateToolStep(
            "tool-needs-approval",
            SideEffectLevel.WorkspaceWrite,
            new PermissionEnvelope(scopes: ["tool.test"], requiresHumanGate: true));

        var result = await runtime.ExecuteStepAsync(step, context, CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Blocked, result.Status);
        Assert.NotNull(result.Failure);
        Assert.Equal("runtime_step_missing_approval", result.Failure!.Code);
    }

    [Fact]
    public async Task ExecuteStepAsync_ShouldAllowHumanGateStepWithApprovalReference()
    {
        var runtime = new TianShuExecutionRuntime();
        var context = CreateContext(
            SideEffectLevel.Privileged,
            requiresHumanGate: true,
            approvalIds: [new ApprovalId("approval-runtime-001")]);
        var step = CreateToolStep(
            "tool-approved",
            SideEffectLevel.WorkspaceWrite,
            new PermissionEnvelope(scopes: ["tool.test"], requiresHumanGate: true));

        var result = await runtime.ExecuteStepAsync(step, context, CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Succeeded, result.Status);
    }

    [Fact]
    public async Task DisposeAsync_ShouldDisposeStepBindingRegistryBindings()
    {
        var provider = new DisposableProviderModule("provider.disposable");
        var runtime = new TianShuExecutionRuntime(new ExecutionRuntimeStepBindingRegistry(
            providers: new Dictionary<string, IProviderModule>(StringComparer.Ordinal)
            {
                [provider.Descriptor.ProviderId] = provider,
            }));

        await runtime.DisposeAsync();

        Assert.True(provider.Disposed);
    }

    [Fact]
    public async Task ExecuteStepAsync_ShouldRejectUnspecifiedSideEffect()
    {
        var runtime = new TianShuExecutionRuntime();
        var context = CreateContext(SideEffectLevel.Privileged);
        var step = CreateToolStep("tool-unspecified", SideEffectLevel.Unspecified);

        var result = await runtime.ExecuteStepAsync(step, context, CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Blocked, result.Status);
        Assert.NotNull(result.Failure);
        Assert.Equal("runtime_step_unspecified_side_effect", result.Failure!.Code);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldStopOnFirstBlockedStepWhenPolicyRequiresStopOnFailure()
    {
        var runtime = new TianShuExecutionRuntime();
        var context = CreateContext(SideEffectLevel.ReadOnly);
        var plan = new ExecutionPlan(
            "plan-stop-on-failure",
            SourceGraphId,
            SourceIntentId,
            new RuntimeStep[]
            {
                CreateDiagnosticStep("diagnostic-allowed", SideEffectLevel.ReadOnly),
                CreateToolStep("tool-blocked", SideEffectLevel.ExternalMutation),
                CreateDiagnosticStep("diagnostic-skipped", SideEffectLevel.ReadOnly),
            },
            new ExecutionPlanPolicy(stopOnFailure: true),
            new TracePolicy());

        var result = await runtime.ExecuteAsync(plan, context, CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Blocked, result.Status);
        Assert.Equal(2, result.StepResults.Count);
        Assert.Equal(RuntimeStepResultStatus.Succeeded, result.StepResults[0].Status);
        Assert.Equal(RuntimeStepResultStatus.Blocked, result.StepResults[1].Status);
        Assert.Equal("tool-blocked", result.StepResults[1].StepId);
    }

    [Fact]
    public void RuntimeStepContracts_ShouldRejectMissingKernelSourceIdsBeforeRuntimeExecution()
    {
        Assert.Throws<ArgumentException>(() => new StageGraphId(""));
        Assert.Throws<ArgumentException>(() => new StageId(""));
        Assert.Throws<ArgumentException>(() => new KernelOperationId(""));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectPlanWhenStepSourceDoesNotMatchPlan()
    {
        var runtime = new TianShuExecutionRuntime();
        var context = CreateContext(SideEffectLevel.Privileged);
        var step = CreateDiagnosticStep("diagnostic-mismatch", SideEffectLevel.ReadOnly);
        var plan = new ExecutionPlan(
            "plan-mismatch",
            new StageGraphId("another-graph"),
            SourceIntentId,
            new[] { step },
            new ExecutionPlanPolicy(),
            new TracePolicy());

        var result = await runtime.ExecuteAsync(plan, context, CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Blocked, result.Status);
        Assert.Empty(result.StepResults);
        Assert.Contains("execution_plan_step_source_mismatch", result.DiagnosticsRef, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecutionRuntimeToolBridge_ShouldExecuteToolOnlyThroughToolInvocationStep()
    {
        var bridge = new ExecutionRuntimeToolBridge();
        var context = CreateContext(SideEffectLevel.Privileged);
        var step = CreateToolStep("tool-bridge-step", SideEffectLevel.ReadOnly);
        var tool = new EchoTool();

        var result = await bridge.ExecuteAsync(step, context, tool, CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Succeeded, result.Status);
        Assert.NotNull(result.Output);
        Assert.True(result.Output!.TryGetProperty("runtimeBoundary", out var boundary));
        Assert.Equal("execution.runtime.tool_bridge", boundary!.StringValue);
    }

    [Fact]
    public async Task ExecutionRuntimeToolBridge_ShouldEmitMetricsWithoutInventingTokenUsage()
    {
        var sink = new RecordingMetricsSink();
        var bridge = new ExecutionRuntimeToolBridge(sink);
        var context = CreateContext(SideEffectLevel.Privileged);
        var step = CreateToolStep("tool-bridge-metrics", SideEffectLevel.ReadOnly);
        var tool = new EchoTool();

        var result = await bridge.ExecuteAsync(step, context, tool, CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Succeeded, result.Status);
        var metrics = Assert.Single(sink.Events);
        Assert.Equal(context.KernelRunId.Value, metrics.RunId);
        Assert.Equal(context.ExecutionId.Value, metrics.ExecutionId);
        Assert.Equal(step.SourceGraphId.Value, metrics.GraphId);
        Assert.Equal(step.SourceStageId.Value, metrics.StageId);
        Assert.Equal(step.StepId, metrics.StepId);
        Assert.Equal(0, metrics.ModelCallCount);
        Assert.False(metrics.TokenUsage.Available);
        Assert.False(metrics.TokenUsage.Estimated);
        Assert.Equal("tool_usage_not_applicable", metrics.TokenUsage.MissingReason);
        Assert.Contains("token_usage_missing", metrics.MissingReasons);
    }

    [Fact]
    public async Task ExecutionRuntimeToolBridge_ShouldRejectStepOutsideGovernanceEnvelope()
    {
        var bridge = new ExecutionRuntimeToolBridge();
        var context = CreateContext(SideEffectLevel.Privileged, allowedToolIds: ["tool.other"]);
        var step = CreateToolStep("tool-bridge-governance-denied", SideEffectLevel.ReadOnly);
        var tool = new RecordingTool();

        var result = await bridge.ExecuteAsync(step, context, tool, CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Blocked, result.Status);
        Assert.NotNull(result.Failure);
        Assert.Equal("runtime_step_capability_tool_not_allowed", result.Failure!.Code);
        Assert.Equal(0, tool.InvokeCount);
    }

    [Fact]
    public async Task ExecutionRuntimeToolBridge_ShouldRejectDescriptorMismatch()
    {
        var bridge = new ExecutionRuntimeToolBridge();
        var context = CreateContext(SideEffectLevel.Privileged);
        var step = CreateToolStep("tool-bridge-mismatch", SideEffectLevel.ReadOnly);
        var tool = new MismatchedTool();

        var result = await bridge.ExecuteAsync(step, context, tool, CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Blocked, result.Status);
        Assert.NotNull(result.Failure);
        Assert.Equal("tool_descriptor_mismatch", result.Failure!.Code);
    }

    [Fact]
    public async Task ExecutionRuntimeToolBridge_ShouldProjectGovernanceDenialAsStructuredToolResult()
    {
        var bridge = new ExecutionRuntimeToolBridge();
        var context = CreateContext(SideEffectLevel.Privileged, requiresHumanGate: true);
        var step = CreateToolStep(
            "tool-bridge-approval-required",
            SideEffectLevel.WorkspaceWrite,
            new PermissionEnvelope(["tool.test"], requiresHumanGate: true));
        var tool = new RecordingTool();

        var result = await bridge.ExecuteAsync(step, context, tool, CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Blocked, result.Status);
        Assert.Equal("runtime_step_missing_approval", result.Failure?.Code);
        Assert.Equal(0, tool.InvokeCount);
        Assert.NotNull(result.Output);
        var toolResult = Assert.Single(result.Output!.GetProperty("toolResults").Items);
        Assert.Equal("approval-required", toolResult.GetProperty("status").GetString());
        Assert.Equal("runtime_step_missing_approval", toolResult.GetProperty("failure").GetProperty("code").GetString());
        Assert.StartsWith("audit://execution/", toolResult.GetProperty("auditRef").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("tool.test.invalid_request", RuntimeStepResultStatus.Failed, "failed")]
    [InlineData("tool.test.timeout", RuntimeStepResultStatus.Failed, "timeout")]
    [InlineData("tool.test.cancelled", RuntimeStepResultStatus.Cancelled, "cancelled")]
    [InlineData("tool.test.sandbox_denied", RuntimeStepResultStatus.Blocked, "blocked")]
    public async Task ExecutionRuntimeToolBridge_ShouldProjectToolFailuresAsStructuredToolResults(
        string failureCode,
        RuntimeStepResultStatus expectedRuntimeStatus,
        string expectedToolStatus)
    {
        var bridge = new ExecutionRuntimeToolBridge();
        var context = CreateContext(SideEffectLevel.Privileged);
        var step = CreateToolStep($"tool-bridge-{expectedToolStatus}", SideEffectLevel.ReadOnly);
        var tool = new FailingTool(failureCode);

        var result = await bridge.ExecuteAsync(step, context, tool, CancellationToken.None);

        Assert.Equal(expectedRuntimeStatus, result.Status);
        Assert.Equal(failureCode, result.Failure?.Code);
        Assert.NotNull(result.Output);
        var toolResult = Assert.Single(result.Output!.GetProperty("toolResults").Items);
        Assert.Equal(step.InputEnvelope.CallId.Value, toolResult.GetProperty("callId").GetString());
        Assert.Equal("tool.test", toolResult.GetProperty("toolId").GetString());
        Assert.Equal(expectedToolStatus, toolResult.GetProperty("status").GetString());
        Assert.Equal(failureCode, toolResult.GetProperty("failure").GetProperty("code").GetString());
        Assert.StartsWith("audit://execution/", toolResult.GetProperty("auditRef").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecutionRuntimeProviderBridge_ShouldPassProviderInvocationRequestWithRuntimeContext()
    {
        var bridge = new ExecutionRuntimeProviderBridge();
        var context = CreateContext(SideEffectLevel.Privileged);
        var step = CreateModelStep("provider-bridge-step", SideEffectLevel.ExternalNetwork);
        var provider = new RecordingProviderModule("provider.module");

        var result = await bridge.ExecuteAsync(step, context, provider, CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Succeeded, result.Status);
        Assert.NotNull(provider.LastRequest);
        Assert.NotNull(provider.LastRequest!.InvocationContext);
        Assert.Equal(step.StepId, provider.LastRequest.InvocationContext!.RuntimeStepId);
        Assert.Equal(step.SourceKernelOperationId.Value, provider.LastRequest.InvocationContext.SourceKernelOperationId);
        Assert.Same(step.InputEnvelope.Inputs, provider.LastRequest.Inputs);
        var input = Assert.IsType<TextProviderInputItem>(Assert.Single(provider.LastRequest.Inputs));
        Assert.Equal("hello", input.Text);
    }

    [Fact]
    public async Task ExecutionRuntimeProviderBridge_ShouldEmitRuntimeMetricsFromProviderUsage()
    {
        var sink = new RecordingMetricsSink();
        var bridge = new ExecutionRuntimeProviderBridge(sink);
        var context = CreateContext(SideEffectLevel.Privileged);
        var step = CreateModelStep("provider-bridge-metrics", SideEffectLevel.ExternalNetwork);
        var provider = new RecordingProviderModule("provider.module", new ProviderUsage(11, 23, 7));

        var result = await bridge.ExecuteAsync(step, context, provider, CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Succeeded, result.Status);
        var metrics = Assert.Single(sink.Events);
        Assert.Equal(context.KernelRunId.Value, metrics.RunId);
        Assert.Equal(context.ExecutionId.Value, metrics.ExecutionId);
        Assert.Equal(step.SourceGraphId.Value, metrics.GraphId);
        Assert.Equal(step.SourceStageId.Value, metrics.StageId);
        Assert.Equal(step.StepId, metrics.StepId);
        Assert.Equal(step.InputEnvelope.Model, metrics.ModelId);
        Assert.Equal(1, metrics.ModelCallCount);
        Assert.True(metrics.TokenUsage.Available);
        Assert.False(metrics.TokenUsage.Estimated);
        Assert.Equal(11, metrics.TokenUsage.InputTokens);
        Assert.Equal(23, metrics.TokenUsage.OutputTokens);
        Assert.Equal(7, metrics.TokenUsage.ReasoningOutputTokens);
        Assert.Equal(41, metrics.TokenUsage.TotalTokens);
        Assert.Equal("provider.completion.usage", metrics.TokenUsage.Source);
        Assert.False(metrics.Cost.Available);
        Assert.Equal("price_model_missing", metrics.Cost.MissingReason);
    }

    [Fact]
    public async Task ExecutionRuntimeProviderBridge_ShouldRetryRetryableProviderFailuresInsideModelStage()
    {
        var sink = new RecordingMetricsSink();
        var bridge = new ExecutionRuntimeProviderBridge(sink);
        var context = CreateContext(SideEffectLevel.Privileged);
        var step = CreateModelStep(
            "provider-bridge-retry-success",
            SideEffectLevel.ExternalNetwork,
            new KernelBudget(tokenBudget: 1000, timeBudgetMs: 1000, costBudget: 1, retryBudget: 5, toolCallBudget: 1));
        var provider = new RetryThenSuccessProviderModule("provider.module", failuresBeforeSuccess: 4);

        var result = await bridge.ExecuteAsync(step, context, provider, CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Succeeded, result.Status);
        Assert.Equal(5, provider.InvokeCount);
        Assert.NotNull(result.Output);
        Assert.Equal(5, result.Output!.GetProperty("providerAttemptCount").GetInt32());
        Assert.Equal(4, result.Output.GetProperty("providerRetryCount").GetInt32());
        Assert.Equal([1, 2, 3, 4, 5], sink.Events.Select(static item => item.AttemptIndex).ToArray());
        Assert.Equal(5, sink.Events.Sum(static item => item.ModelCallCount));
    }

    [Fact]
    public async Task ExecutionRuntimeProviderBridge_ShouldFailAfterRetryBudgetIsExhausted()
    {
        var sink = new RecordingMetricsSink();
        var bridge = new ExecutionRuntimeProviderBridge(sink);
        var context = CreateContext(SideEffectLevel.Privileged);
        var step = CreateModelStep(
            "provider-bridge-retry-exhausted",
            SideEffectLevel.ExternalNetwork,
            new KernelBudget(tokenBudget: 1000, timeBudgetMs: 1000, costBudget: 1, retryBudget: 5, toolCallBudget: 1));
        var provider = new RetryThenSuccessProviderModule("provider.module", failuresBeforeSuccess: 5);

        var result = await bridge.ExecuteAsync(step, context, provider, CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Failed, result.Status);
        Assert.Equal("provider_retry_budget_exhausted", result.Failure?.Code);
        Assert.Equal(5, provider.InvokeCount);
        Assert.NotNull(result.Output);
        Assert.Equal(5, result.Output!.GetProperty("providerAttemptCount").GetInt32());
        Assert.Equal(4, result.Output.GetProperty("providerRetryCount").GetInt32());
        Assert.Equal([1, 2, 3, 4, 5], sink.Events.Select(static item => item.AttemptIndex).ToArray());
    }

    [Fact]
    public async Task ExecutionRuntimeProviderBridge_ShouldFailWhenModelAttemptTimesOut()
    {
        var bridge = new ExecutionRuntimeProviderBridge();
        var context = CreateContext(SideEffectLevel.Privileged);
        var step = CreateModelStep(
            "provider-bridge-timeout",
            SideEffectLevel.ExternalNetwork,
            new KernelBudget(tokenBudget: 1000, timeBudgetMs: 1, costBudget: 1, retryBudget: 1, toolCallBudget: 1));
        var provider = new TimeoutProviderModule("provider.module");

        var result = await bridge.ExecuteAsync(step, context, provider, CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Failed, result.Status);
        Assert.Equal("provider_model_attempt_timeout", result.Failure?.Code);
        Assert.Equal(1, provider.InvokeCount);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRouteRegisteredModelStepThroughProviderBridgeAndMetricsSink()
    {
        var sink = new RecordingMetricsSink();
        var provider = new RecordingProviderModule("provider.module", new ProviderUsage(3, 5, 2));
        var runtime = new TianShuExecutionRuntime(
            new ExecutionRuntimeStepBindingRegistry(
                providers: new Dictionary<string, IProviderModule>(StringComparer.Ordinal)
                {
                    ["provider.module"] = provider,
                }),
            sink);
        var context = CreateContext(SideEffectLevel.Privileged);
        var step = CreateModelStep("provider-live-plan-step", SideEffectLevel.ExternalNetwork);
        var plan = new ExecutionPlan(
            "plan-provider-live",
            SourceGraphId,
            SourceIntentId,
            [step],
            new ExecutionPlanPolicy(),
            TracePolicy);

        var result = await runtime.ExecuteAsync(plan, context, CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Succeeded, result.Status);
        Assert.NotNull(provider.LastRequest);
        Assert.Equal(step.StepId, provider.LastRequest!.InvocationContext?.RuntimeStepId);
        var metrics = Assert.Single(sink.Events);
        Assert.Equal("plan-provider-live", metrics.PlanId);
        Assert.Equal(step.StepId, metrics.StepId);
        Assert.True(metrics.TokenUsage.Available);
        Assert.False(metrics.TokenUsage.Estimated);
        Assert.Equal(10, metrics.TokenUsage.TotalTokens);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRouteRegisteredToolStepThroughToolBridgeAndMetricsSink()
    {
        var sink = new RecordingMetricsSink();
        var tool = new RecordingTool();
        var runtime = new TianShuExecutionRuntime(
            new ExecutionRuntimeStepBindingRegistry(
                tools: new Dictionary<string, ITianShuTool>(StringComparer.Ordinal)
                {
                    ["tool.test"] = tool,
                }),
            sink);
        var context = CreateContext(SideEffectLevel.Privileged);
        var step = CreateToolStep("tool-live-plan-step", SideEffectLevel.ReadOnly);
        var plan = new ExecutionPlan(
            "plan-tool-live",
            SourceGraphId,
            SourceIntentId,
            [step],
            new ExecutionPlanPolicy(),
            TracePolicy);

        var result = await runtime.ExecuteAsync(plan, context, CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Succeeded, result.Status);
        Assert.Equal(1, tool.InvokeCount);
        var metrics = Assert.Single(sink.Events);
        Assert.Equal("plan-tool-live", metrics.PlanId);
        Assert.Equal(step.StepId, metrics.StepId);
        Assert.Equal(0, metrics.ModelCallCount);
        Assert.False(metrics.TokenUsage.Available);
        Assert.Equal("tool_usage_not_applicable", metrics.TokenUsage.MissingReason);
    }

    [Fact]
    public async Task ExecutionRuntimeProviderBridge_ShouldRejectDescriptorMismatch()
    {
        var bridge = new ExecutionRuntimeProviderBridge();
        var context = CreateContext(SideEffectLevel.Privileged);
        var step = CreateModelStep("provider-bridge-mismatch", SideEffectLevel.ExternalNetwork);
        var provider = new RecordingProviderModule("provider.other");

        var result = await bridge.ExecuteAsync(step, context, provider, CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Blocked, result.Status);
        Assert.NotNull(result.Failure);
        Assert.Equal("provider_descriptor_mismatch", result.Failure!.Code);
        Assert.Null(provider.LastRequest);
    }

    [Fact]
    public async Task ExecutionRuntimeProviderBridge_ShouldRejectGovernanceFailureBeforeInvokingProvider()
    {
        var bridge = new ExecutionRuntimeProviderBridge();
        var context = CreateContext(SideEffectLevel.Privileged, allowedModuleIds: ["provider.other"]);
        var step = CreateModelStep("provider-bridge-governance-denied", SideEffectLevel.ExternalNetwork);
        var provider = new RecordingProviderModule("provider.module");

        var result = await bridge.ExecuteAsync(step, context, provider, CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Blocked, result.Status);
        Assert.Equal("runtime_step_provider_module_not_allowed", result.Failure?.Code);
        Assert.Null(provider.LastRequest);
    }

    [Fact]
    public async Task ExecutionRuntimeMemoryModuleBridge_ShouldInvokeMemoryModuleThroughModuleCapabilityStep()
    {
        var bridge = new ExecutionRuntimeMemoryModuleBridge();
        var context = CreateContext(SideEffectLevel.Privileged);
        var step = CreateMemoryModuleStep("memory-module-query", SideEffectLevel.ReadOnly);
        var module = new RecordingMemoryModule("memory.identity");

        var result = await bridge.ExecuteQueryAsync(
            step,
            context,
            module,
            new FilterMemoryModuleQuery(new FilterMemory(QueryText: "architecture")),
            new MemoryOperationContext("tester", correlationId: step.StepId),
            CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Succeeded, result.Status);
        Assert.NotNull(module.LastQuery);
        Assert.Equal(step.StepId, module.LastQuery!.Context.RuntimeStepId);
        Assert.Equal(step.SourceKernelOperationId.Value, module.LastQuery.Context.SourceKernelOperationId);
    }

    [Fact]
    public async Task ExecutionRuntimeMemoryModuleBridge_ShouldRejectDescriptorMismatch()
    {
        var bridge = new ExecutionRuntimeMemoryModuleBridge();
        var context = CreateContext(SideEffectLevel.Privileged);
        var step = CreateMemoryModuleStep("memory-module-mismatch", SideEffectLevel.ReadOnly);
        var module = new RecordingMemoryModule("memory.other");

        var result = await bridge.ExecuteQueryAsync(
            step,
            context,
            module,
            new FilterMemoryModuleQuery(new FilterMemory(QueryText: "architecture")),
            new MemoryOperationContext("tester", correlationId: step.StepId),
            CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Blocked, result.Status);
        Assert.NotNull(result.Failure);
        Assert.Equal("memory_module_descriptor_mismatch", result.Failure!.Code);
        Assert.Null(module.LastQuery);
    }

    [Fact]
    public async Task ExecutionRuntimeMemoryModuleBridge_ShouldRejectGovernanceFailureBeforeInvokingModule()
    {
        var bridge = new ExecutionRuntimeMemoryModuleBridge();
        var context = CreateContext(SideEffectLevel.Privileged, allowedModuleIds: ["memory.other"]);
        var step = CreateMemoryModuleStep("memory-module-governance-denied", SideEffectLevel.ReadOnly);
        var module = new RecordingMemoryModule("memory.identity");

        var result = await bridge.ExecuteQueryAsync(
            step,
            context,
            module,
            new FilterMemoryModuleQuery(new FilterMemory(QueryText: "architecture")),
            new MemoryOperationContext("tester", correlationId: step.StepId),
            CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Blocked, result.Status);
        Assert.Equal("runtime_step_module_not_allowed", result.Failure?.Code);
        Assert.Null(module.LastQuery);
    }

    [Fact]
    public async Task ExecutionRuntimeArtifactModuleBridge_ShouldInvokeArtifactModuleThroughArtifactStep()
    {
        var bridge = new ExecutionRuntimeArtifactModuleBridge();
        var context = CreateContext(SideEffectLevel.Privileged);
        var step = CreateArtifactStep("artifact-module-publish", "publish", SideEffectLevel.WorkspaceWrite);
        var module = new RecordingArtifactModule(ModuleKind.ArtifactStateProjection);
        var artifact = CreateArtifact("artifact-runtime-001");

        var result = await bridge.ExecuteArtifactAsync(
            step,
            context,
            module,
            new PublishArtifactModuleMutation(new PublishArtifact(artifact)),
            CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Succeeded, result.Status);
        Assert.NotNull(module.LastMutation);
        Assert.Equal(step.StepId, module.LastMutation!.Context.RuntimeStepId);
        Assert.Equal(context.KernelRunId, module.LastMutation.Context.KernelRunId);
        Assert.Equal(step.SourceGraphId, module.LastMutation.Context.SourceGraphId);
        Assert.Equal(step.SourceStageId, module.LastMutation.Context.SourceStageId);
    }

    [Fact]
    public async Task ExecutionRuntimeArtifactModuleBridge_ShouldRejectOperationMismatch()
    {
        var bridge = new ExecutionRuntimeArtifactModuleBridge();
        var context = CreateContext(SideEffectLevel.Privileged);
        var step = CreateArtifactStep("artifact-module-mismatch", "publish", SideEffectLevel.WorkspaceWrite);
        var module = new RecordingArtifactModule(ModuleKind.ArtifactStateProjection);

        var result = await bridge.ExecuteArtifactAsync(
            step,
            context,
            module,
            new PromoteArtifactModuleMutation(new PromoteArtifact(new ArtifactId("artifact-runtime-001"), "stable")),
            CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Blocked, result.Status);
        Assert.Equal("artifact_operation_mismatch", result.Failure?.Code);
        Assert.Null(module.LastMutation);
    }

    [Fact]
    public async Task ExecutionRuntimeArtifactModuleBridge_ShouldRejectGovernanceFailureBeforeInvokingModule()
    {
        var bridge = new ExecutionRuntimeArtifactModuleBridge();
        var context = CreateContext(SideEffectLevel.ReadOnly);
        var step = CreateArtifactStep("artifact-module-governance-denied", "publish", SideEffectLevel.WorkspaceWrite);
        var module = new RecordingArtifactModule(ModuleKind.ArtifactStateProjection);

        var result = await bridge.ExecuteArtifactAsync(
            step,
            context,
            module,
            new PublishArtifactModuleMutation(new PublishArtifact(CreateArtifact("artifact-runtime-001"))),
            CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Blocked, result.Status);
        Assert.Equal("runtime_step_side_effect_exceeds_governance", result.Failure?.Code);
        Assert.Null(module.LastMutation);
    }

    [Fact]
    public async Task ExecutionRuntimeArtifactModuleBridge_ShouldReadProjectionThroughModuleCapabilityStep()
    {
        var bridge = new ExecutionRuntimeArtifactModuleBridge();
        var context = CreateContext(SideEffectLevel.Privileged);
        var step = new ModuleCapabilityStep(
            "artifact-projection-query",
            SourceIntentId,
            SourceGraphId,
            SourceStageId,
            SourceKernelOperationId,
            "artifact.state.projection",
            "artifact.state.projection.read",
            Payload("projection"),
            Permission,
            SideEffect(SideEffectLevel.ReadOnly),
            Budget,
            OutputContract,
            TracePolicy);
        var module = new RecordingArtifactModule(ModuleKind.ArtifactStateProjection);

        var result = await bridge.ExecuteProjectionQueryAsync(
            step,
            context,
            module,
            new ReadProjectionSnapshotModuleQuery(ProjectionScopeKind.Artifact, "artifact-runtime-001"),
            CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Succeeded, result.Status);
        Assert.NotNull(module.LastProjectionQuery);
        Assert.Equal(step.StepId, module.LastProjectionQuery!.Context.RuntimeStepId);
        Assert.Equal("artifact-runtime-001", ((ReadProjectionSnapshotModuleQuery)module.LastProjectionQuery.Query).ScopeKey);
    }

    [Fact]
    public async Task ExecutionRuntimeDiagnosticsModuleBridge_ShouldInvokeDiagnosticsModuleThroughDiagnosticStep()
    {
        var bridge = new ExecutionRuntimeDiagnosticsModuleBridge();
        var context = CreateContext(SideEffectLevel.Privileged);
        var step = CreateDiagnosticStep("diagnostics-runtime-step", SideEffectLevel.ReadOnly);
        var module = new RecordingDiagnosticsModule(ModuleKind.Diagnostics);
        var diagnosticEvent = CreateDiagnosticsModuleEvent(step, context, DiagnosticsModuleEventKind.ExecutionRuntimeStep);

        var result = await bridge.ExecuteAsync(step, context, module, diagnosticEvent, CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Succeeded, result.Status);
        Assert.NotNull(module.LastEvent);
        Assert.Equal(step.StepId, module.LastEvent!.Context.RuntimeStepId);
        Assert.Equal("diagnostics://recorded", result.DiagnosticsRef);
        Assert.Equal("trace://recorded", result.TraceRef);
    }

    [Fact]
    public async Task ExecutionRuntimeDiagnosticsModuleBridge_ShouldRejectDescriptorMismatch()
    {
        var bridge = new ExecutionRuntimeDiagnosticsModuleBridge();
        var context = CreateContext(SideEffectLevel.Privileged);
        var step = CreateDiagnosticStep("diagnostics-kind-mismatch", SideEffectLevel.ReadOnly);
        var module = new RecordingDiagnosticsModule(ModuleKind.MemoryIdentity);
        var diagnosticEvent = CreateDiagnosticsModuleEvent(step, context, DiagnosticsModuleEventKind.ExecutionRuntimeStep);

        var result = await bridge.ExecuteAsync(step, context, module, diagnosticEvent, CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Blocked, result.Status);
        Assert.Equal("diagnostics_module_kind_mismatch", result.Failure?.Code);
        Assert.Null(module.LastEvent);
    }

    [Fact]
    public async Task ExecutionRuntimeDiagnosticsModuleBridge_ShouldRejectGovernanceFailureBeforeInvokingModule()
    {
        var bridge = new ExecutionRuntimeDiagnosticsModuleBridge();
        var context = CreateContext(SideEffectLevel.None);
        var step = CreateDiagnosticStep("diagnostics-governance-denied", SideEffectLevel.ReadOnly);
        var module = new RecordingDiagnosticsModule(ModuleKind.Diagnostics);
        var diagnosticEvent = CreateDiagnosticsModuleEvent(step, context, DiagnosticsModuleEventKind.ExecutionRuntimeStep);

        var result = await bridge.ExecuteAsync(step, context, module, diagnosticEvent, CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Blocked, result.Status);
        Assert.Equal("runtime_step_side_effect_exceeds_governance", result.Failure?.Code);
        Assert.Null(module.LastEvent);
    }

    [Fact]
    public async Task ExecutionRuntimeDiagnosticsModuleBridge_ShouldRejectDiagnosticKindMismatch()
    {
        var bridge = new ExecutionRuntimeDiagnosticsModuleBridge();
        var context = CreateContext(SideEffectLevel.Privileged);
        var step = CreateDiagnosticStep("diagnostics-event-kind-mismatch", SideEffectLevel.ReadOnly);
        var module = new RecordingDiagnosticsModule(ModuleKind.Diagnostics);
        var diagnosticEvent = CreateDiagnosticsModuleEvent(step, context, DiagnosticsModuleEventKind.ModuleCall);

        var result = await bridge.ExecuteAsync(step, context, module, diagnosticEvent, CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Blocked, result.Status);
        Assert.Equal("diagnostics_event_kind_mismatch", result.Failure?.Code);
        Assert.Null(module.LastEvent);
    }

    [Fact]
    public async Task ExecutionRuntimeWorkspaceModuleBridge_ShouldResolveWorkspaceThroughModuleCapabilityStep()
    {
        var bridge = new ExecutionRuntimeWorkspaceModuleBridge();
        var context = CreateContext(SideEffectLevel.Privileged, allowedModuleIds: ["workspace.environment"]);
        var step = CreateWorkspaceModuleStep("workspace-resolution", SideEffectLevel.ReadOnly);
        var module = new RecordingWorkspaceModule();

        var result = await bridge.ResolveAsync(
            step,
            context,
            module,
            new WorkspaceResolutionRequest("D:/Work/TianShu"),
            CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Succeeded, result.Status);
        Assert.NotNull(module.LastContext);
        Assert.Equal(step.StepId, module.LastContext!.RuntimeStepId);
        Assert.Equal(step.SourceKernelOperationId.Value, module.LastContext.SourceKernelOperationId);
        Assert.NotNull(result.Output);
        Assert.True(result.Output!.TryGetProperty("factCount", out var factCount));
        Assert.Equal("1", factCount!.NumberValue);
    }

    [Fact]
    public async Task ExecutionRuntimeWorkspaceModuleBridge_ShouldRejectWorkspaceWriteStep()
    {
        var bridge = new ExecutionRuntimeWorkspaceModuleBridge();
        var context = CreateContext(SideEffectLevel.Privileged, allowedModuleIds: ["workspace.environment"]);
        var step = CreateWorkspaceModuleStep("workspace-write-denied", SideEffectLevel.WorkspaceWrite);
        var module = new RecordingWorkspaceModule();

        var result = await bridge.ResolveAsync(
            step,
            context,
            module,
            new WorkspaceResolutionRequest("D:/Work/TianShu"),
            CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Blocked, result.Status);
        Assert.Equal("workspace_module_step_not_read_only", result.Failure?.Code);
        Assert.Null(module.LastContext);
    }

    [Fact]
    public async Task ExecutionRuntimeWorkspaceModuleBridge_ShouldRejectGovernanceFailureBeforeResolvingWorkspace()
    {
        var bridge = new ExecutionRuntimeWorkspaceModuleBridge();
        var context = CreateContext(SideEffectLevel.Privileged, allowedModuleIds: ["workspace.other"]);
        var step = CreateWorkspaceModuleStep("workspace-governance-denied", SideEffectLevel.ReadOnly);
        var module = new RecordingWorkspaceModule();

        var result = await bridge.ResolveAsync(
            step,
            context,
            module,
            new WorkspaceResolutionRequest("D:/Work/TianShu"),
            CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Blocked, result.Status);
        Assert.Equal("runtime_step_module_not_allowed", result.Failure?.Code);
        Assert.Null(module.LastContext);
    }

    private static IEnumerable<RuntimeStep> CreateAllStepKinds()
    {
        yield return CreateModelStep("model-step", SideEffectLevel.ExternalNetwork);
        yield return CreateToolStep("tool-step", SideEffectLevel.WorkspaceWrite);
        yield return new StateCommitStep(
            "state-step",
            SourceIntentId,
            SourceGraphId,
            SourceStageId,
            SourceKernelOperationId,
            "state-store",
            Payload("state"),
            Permission,
            SideEffect(SideEffectLevel.WorkspaceWrite),
            Budget,
            OutputContract,
            TracePolicy);
        yield return new ArtifactStep(
            "artifact-step",
            SourceIntentId,
            SourceGraphId,
            SourceStageId,
            SourceKernelOperationId,
            "publish",
            Payload("artifact"),
            Permission,
            SideEffect(SideEffectLevel.WorkspaceWrite),
            Budget,
            OutputContract,
            TracePolicy);
        yield return CreateDiagnosticStep("diagnostic-step", SideEffectLevel.ReadOnly);
        yield return new HostInteractionStep(
            "host-interaction-step",
            SourceIntentId,
            SourceGraphId,
            SourceStageId,
            SourceKernelOperationId,
            "request_input",
            Payload("host"),
            Permission,
            SideEffect(SideEffectLevel.None),
            Budget,
            OutputContract,
            TracePolicy);
        yield return new ModuleCapabilityStep(
            "module-capability-step",
            SourceIntentId,
            SourceGraphId,
            SourceStageId,
            SourceKernelOperationId,
            "module.test",
            "capability.test",
            Payload("module"),
            Permission,
            SideEffect(SideEffectLevel.ExternalNetwork),
            Budget,
            OutputContract,
            TracePolicy);
    }

    private static ModelInvocationStep CreateModelStep(
        string stepId,
        SideEffectLevel sideEffectLevel,
        KernelBudget? budget = null)
        => new(
            stepId,
            SourceIntentId,
            SourceGraphId,
            SourceStageId,
            SourceKernelOperationId,
            "provider.module",
            new ModelRoutePolicy(routeCandidateIds: new[] { "provider-route" }, preferredRouteId: "provider-route"),
            new ProviderInvocationRequest(
                new ExecutionId("provider-execution"),
                "provider-key",
                "model-name",
                new ProviderConversationContext(),
                new ProviderInputItem[] { new TextProviderInputItem("hello") }),
            Permission,
            SideEffect(sideEffectLevel),
            budget ?? Budget,
            OutputContract,
            TracePolicy);

    private static ToolInvocationStep CreateToolStep(
        string stepId,
        SideEffectLevel sideEffectLevel,
        PermissionEnvelope? permission = null)
        => new(
            stepId,
            SourceIntentId,
            SourceGraphId,
            SourceStageId,
            SourceKernelOperationId,
            "tool.test",
            new ToolInvocationEnvelope(
                new CallId($"call-{stepId}"),
                "tool.test",
                "run",
                Payload("tool"),
                permission ?? Permission,
                SideEffect(sideEffectLevel)),
            permission ?? Permission,
            SideEffect(sideEffectLevel),
            Budget,
            OutputContract,
            TracePolicy);

    private static ArtifactStep CreateArtifactStep(string stepId, string operationName, SideEffectLevel sideEffectLevel)
        => new(
            stepId,
            SourceIntentId,
            SourceGraphId,
            SourceStageId,
            SourceKernelOperationId,
            operationName,
            Payload("artifact"),
            Permission,
            SideEffect(sideEffectLevel),
            Budget,
            OutputContract,
            TracePolicy);

    private static ModuleCapabilityStep CreateMemoryModuleStep(string stepId, SideEffectLevel sideEffectLevel)
        => new(
            stepId,
            SourceIntentId,
            SourceGraphId,
            SourceStageId,
            SourceKernelOperationId,
            "memory.identity",
            "memory.identity.filter",
            Payload("memory"),
            Permission,
            SideEffect(sideEffectLevel),
            Budget,
            OutputContract,
            TracePolicy);

    private static ModuleCapabilityStep CreateWorkspaceModuleStep(string stepId, SideEffectLevel sideEffectLevel)
        => new(
            stepId,
            SourceIntentId,
            SourceGraphId,
            SourceStageId,
            SourceKernelOperationId,
            "workspace.environment",
            "workspace.environment.resolve",
            Payload("workspace"),
            new PermissionEnvelope(["module.workspace.environment"], requiresHumanGate: false),
            SideEffect(sideEffectLevel),
            Budget,
            OutputContract,
            TracePolicy);

    private static DiagnosticStep CreateDiagnosticStep(string stepId, SideEffectLevel sideEffectLevel)
        => new(
            stepId,
            SourceIntentId,
            SourceGraphId,
            SourceStageId,
            SourceKernelOperationId,
            "runtime",
            Payload("diagnostic"),
            Permission,
            SideEffect(sideEffectLevel),
            Budget,
            OutputContract,
            TracePolicy);

    private static DiagnosticsModuleEvent CreateDiagnosticsModuleEvent(
        DiagnosticStep step,
        ExecutionRuntimeContext context,
        DiagnosticsModuleEventKind kind)
        => new(
            kind,
            kind is DiagnosticsModuleEventKind.ModuleCall ? "diagnostics/module/call" : "diagnostics/runtime/step",
            Payload("diagnostics"),
            new DiagnosticsModuleEventContext(
                kernelRunId: context.KernelRunId,
                executionId: context.ExecutionId,
                runtimeStepId: step.StepId,
                sourceIntentId: step.SourceIntentId,
                sourceGraphId: step.SourceGraphId,
                sourceStageId: step.SourceStageId,
                sourceKernelOperationId: step.SourceKernelOperationId,
                moduleId: kind is DiagnosticsModuleEventKind.ModuleCall ? "memory.identity" : null,
                capabilityId: kind is DiagnosticsModuleEventKind.ModuleCall ? "memory.identity.filter" : null));

    private static ExecutionRuntimeContext CreateContext(
        SideEffectLevel maxSideEffectLevel,
        IReadOnlyList<string>? allowedToolIds = null,
        IReadOnlyList<string>? allowedModuleIds = null,
        bool requiresHumanGate = false,
        IReadOnlyList<ApprovalId>? approvalIds = null)
        => new(
            new ExecutionId("execution-test"),
            new KernelRunId("kernel-run-test"),
            new GovernanceEnvelope(
                "governance-test",
                allowedToolIds: allowedToolIds ?? new[] { "tool.test" },
                allowedModuleIds: allowedModuleIds ?? new[] { "provider.module", "module.test", "memory.identity", "artifact.state.projection", "diagnostics" },
                maxSideEffectLevel: maxSideEffectLevel,
                requiresHumanGate: requiresHumanGate,
                approvalIds: approvalIds));

    private static SideEffectProfile SideEffect(SideEffectLevel level)
        => new(level, affectedResources: new[] { "runtime-step-test" }, reversible: true, requiresAudit: true);

    private static StructuredValue Payload(string value)
        => StructuredValue.FromPlainObject(new Dictionary<string, object?>
        {
            ["value"] = value,
        });

    private static readonly CoreIntentId SourceIntentId = new("intent-test");
    private static readonly StageGraphId SourceGraphId = new("graph-test");
    private static readonly StageId SourceStageId = new("stage-test");
    private static readonly KernelOperationId SourceKernelOperationId = new("operation-test");
    private static readonly PermissionEnvelope Permission = new(
        scopes: new[] { "runtime.execute" },
        grants: new[] { "test" },
        requiresHumanGate: false,
        reason: "runtime step test");
    private static readonly KernelBudget Budget = new(tokenBudget: 1000, timeBudgetMs: 1000, costBudget: 1, retryBudget: 1, toolCallBudget: 1);
    private static readonly ContractRef OutputContract = new("runtime.output", "v1");
    private static readonly TracePolicy TracePolicy = new();

    private sealed class EchoTool : ITianShuTool
    {
        public ToolDescriptor Descriptor { get; } = new(
            "tool.test",
            "Tool Test",
            "Echoes the input.",
            inputSchemaRef: new JsonSchemaRef("schema.tool.test.input"),
            outputSchemaRef: new JsonSchemaRef("schema.tool.test.output"),
            permissions: new PermissionDeclaration(["tool.test"], requiresHumanGate: false),
            sideEffects: new SideEffectProfile(SideEffectLevel.ReadOnly),
            audit: new AuditProfile(eventKinds: ["tool.test.invoked"]));

        public ValueTask<ToolInvocationResult> InvokeAsync(
            ToolInvocationEnvelope invocation,
            ToolInvocationContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new ToolInvocationResult(
                invocation.CallId,
                invocation.ToolId,
                [new ToolStreamItem("text", invocation.Input, isTerminal: true)]));
    }

    private sealed class RecordingTool : ITianShuTool
    {
        public int InvokeCount { get; private set; }

        public ToolDescriptor Descriptor { get; } = new(
            "tool.test",
            "Tool Test",
            "Records invocation count.",
            inputSchemaRef: new JsonSchemaRef("schema.tool.test.input"),
            outputSchemaRef: new JsonSchemaRef("schema.tool.test.output"),
            permissions: new PermissionDeclaration(["tool.test"], requiresHumanGate: false),
            sideEffects: new SideEffectProfile(SideEffectLevel.ReadOnly),
            audit: new AuditProfile(eventKinds: ["tool.test.invoked"]));

        public ValueTask<ToolInvocationResult> InvokeAsync(
            ToolInvocationEnvelope invocation,
            ToolInvocationContext context,
            CancellationToken cancellationToken)
        {
            InvokeCount++;
            return ValueTask.FromResult(new ToolInvocationResult(
                invocation.CallId,
                invocation.ToolId,
                [new ToolStreamItem("text", invocation.Input, isTerminal: true)]));
        }
    }

    private sealed class FailingTool : ITianShuTool
    {
        private readonly string failureCode;

        public FailingTool(string failureCode)
        {
            this.failureCode = failureCode;
        }

        public ToolDescriptor Descriptor { get; } = new(
            "tool.test",
            "Tool Test",
            "Returns a controlled failure.",
            inputSchemaRef: new JsonSchemaRef("schema.tool.test.input"),
            outputSchemaRef: new JsonSchemaRef("schema.tool.test.output"),
            permissions: new PermissionDeclaration(["tool.test"], requiresHumanGate: false),
            sideEffects: new SideEffectProfile(SideEffectLevel.ReadOnly),
            audit: new AuditProfile(eventKinds: ["tool.test.invoked"]));

        public ValueTask<ToolInvocationResult> InvokeAsync(
            ToolInvocationEnvelope invocation,
            ToolInvocationContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new ToolInvocationResult(
                invocation.CallId,
                invocation.ToolId,
                failure: new ToolInvocationFailure(failureCode, $"controlled failure: {failureCode}")));
    }

    private sealed class MismatchedTool : ITianShuTool
    {
        public ToolDescriptor Descriptor { get; } = new(
            "tool.other",
            "Tool Other",
            "Does not match the step.",
            inputSchemaRef: new JsonSchemaRef("schema.tool.other.input"),
            outputSchemaRef: new JsonSchemaRef("schema.tool.other.output"),
            permissions: new PermissionDeclaration(["tool.other"], requiresHumanGate: false),
            sideEffects: new SideEffectProfile(SideEffectLevel.ReadOnly),
            audit: new AuditProfile(eventKinds: ["tool.other.invoked"]));

        public ValueTask<ToolInvocationResult> InvokeAsync(
            ToolInvocationEnvelope invocation,
            ToolInvocationContext context,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("Mismatched tool should not be invoked.");
    }

    private sealed class RecordingMemoryModule : IMemoryModule
    {
        public RecordingMemoryModule(string moduleId)
        {
            Descriptor = new ModuleDescriptor(
                moduleId,
                ModuleKind.MemoryIdentity,
                "Recording Memory",
                "1.0",
                capabilities:
                [
                    new ModuleCapabilityDescriptor(
                        "memory.identity.filter",
                        "Filter memory",
                        permission: Permission,
                        sideEffects: SideEffect(SideEffectLevel.ReadOnly)),
                ],
                permission: Permission,
                sideEffects: SideEffect(SideEffectLevel.ReadOnly),
                trustLevel: ModuleTrustLevel.BuiltIn);
        }

        public ModuleDescriptor Descriptor { get; }

        public MemoryModuleQueryInvocation? LastQuery { get; private set; }

        public ValueTask<ModuleSmokeCheckResult> CheckAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult(new ModuleSmokeCheckResult(Descriptor.ModuleId, true, ModuleHealthStatus.Healthy));

        public ValueTask<MemoryModuleQueryResult> QueryAsync(
            MemoryModuleQueryInvocation invocation,
            CancellationToken cancellationToken)
        {
            LastQuery = invocation;
            return ValueTask.FromResult(new MemoryModuleQueryResult(records: new MemoryQueryResult()));
        }

        public ValueTask<MemoryMutationResult> MutateAsync(
            MemoryModuleMutationInvocation invocation,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new MemoryMutationResult(true, Effect: MemoryMutationEffect.None));
    }

    private sealed class RecordingArtifactModule : IArtifactStateProjectionModule
    {
        public RecordingArtifactModule(ModuleKind moduleKind)
        {
            Descriptor = new ModuleDescriptor(
                "artifact.state.projection",
                moduleKind,
                "Recording Artifact",
                "1.0",
                capabilities:
                [
                    new ModuleCapabilityDescriptor(
                        "artifact.state.projection.read",
                        "Read artifact projection",
                        permission: Permission,
                        sideEffects: SideEffect(SideEffectLevel.ReadOnly)),
                ],
                permission: Permission,
                sideEffects: SideEffect(SideEffectLevel.WorkspaceWrite),
                trustLevel: ModuleTrustLevel.BuiltIn);
        }

        public ModuleDescriptor Descriptor { get; }

        public ArtifactModuleMutationInvocation? LastMutation { get; private set; }

        public ArtifactProjectionModuleQueryInvocation? LastProjectionQuery { get; private set; }

        public ValueTask<ModuleSmokeCheckResult> CheckAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult(new ModuleSmokeCheckResult(Descriptor.ModuleId, true, ModuleHealthStatus.Healthy));

        public ValueTask<ArtifactModuleMutationResult> ExecuteArtifactAsync(
            ArtifactModuleMutationInvocation invocation,
            CancellationToken cancellationToken)
        {
            LastMutation = invocation;
            return ValueTask.FromResult(new ArtifactModuleMutationResult(
                true,
                invocation.Mutation.OperationName,
                new ArtifactModuleRecord(CreateArtifact("artifact-runtime-001"))));
        }

        public ValueTask<ArtifactProjectionModuleQueryResult> QueryProjectionAsync(
            ArtifactProjectionModuleQueryInvocation invocation,
            CancellationToken cancellationToken)
        {
            LastProjectionQuery = invocation;
            return ValueTask.FromResult(new ArtifactProjectionModuleQueryResult());
        }

        public ValueTask<ArtifactCheckpointMaterializationResult> MaterializeCheckpointAsync(
            ArtifactCheckpointMaterializationInvocation invocation,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class RecordingDiagnosticsModule : IDiagnosticsModule
    {
        public RecordingDiagnosticsModule(ModuleKind moduleKind)
        {
            Descriptor = new ModuleDescriptor(
                "diagnostics",
                moduleKind,
                "Recording Diagnostics",
                "1.0",
                capabilities:
                [
                    new ModuleCapabilityDescriptor(
                        "diagnostics.capability",
                        "Emit diagnostics",
                        permission: Permission,
                        sideEffects: SideEffect(SideEffectLevel.WorkspaceWrite)),
                ],
                permission: Permission,
                sideEffects: SideEffect(SideEffectLevel.WorkspaceWrite),
                trustLevel: ModuleTrustLevel.BuiltIn);
        }

        public ModuleDescriptor Descriptor { get; }

        public DiagnosticsModuleEvent? LastEvent { get; private set; }

        public ValueTask<ModuleSmokeCheckResult> CheckAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult(new ModuleSmokeCheckResult(Descriptor.ModuleId, true, ModuleHealthStatus.Healthy));

        public ValueTask<DiagnosticsModuleEmitResult> EmitAsync(DiagnosticsModuleEvent diagnosticEvent, CancellationToken cancellationToken)
        {
            LastEvent = diagnosticEvent;
            return ValueTask.FromResult(new DiagnosticsModuleEmitResult(
                true,
                diagnosticEvent.EventName,
                diagnosticEvent.Kind,
                diagnosticsRef: "diagnostics://recorded",
                traceRef: "trace://recorded"));
        }
    }

    private sealed class RecordingWorkspaceModule : IWorkspaceModule
    {
        public ModuleDescriptor Descriptor { get; } = BuiltInModuleDescriptors.WorkspaceEnvironment();

        public WorkspaceModuleInvocationContext? LastContext { get; private set; }

        public ValueTask<ModuleSmokeCheckResult> CheckAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult(new ModuleSmokeCheckResult(Descriptor.ModuleId, true, ModuleHealthStatus.Healthy));

        public ValueTask<WorkspaceResolutionResult> ResolveAsync(
            WorkspaceResolutionRequest request,
            WorkspaceModuleInvocationContext context,
            CancellationToken cancellationToken)
        {
            LastContext = context;
            var source = new WorkspaceFactSource("workspace-resolver:test", "test");
            return ValueTask.FromResult(new WorkspaceResolutionResult(
                WorkspaceResolutionStatus.Resolved,
                facts:
                [
                    new WorkspaceFact(
                        "workspace.root",
                        WorkspaceFactKind.WorkspaceRoot,
                        request.WorkspacePath,
                        source),
                ],
                sources: [source],
                diagnosticsRefs: ["diagnostics://workspace/test"]));
        }
    }

    private static Artifact CreateArtifact(string artifactId)
        => new(
            new ArtifactId(artifactId),
            new CollaborationSpaceRef(
                new CollaborationSpaceId("space-runtime"),
                "runtime-space",
                "Runtime Space"),
            "runtime.md",
            ArtifactKind.Document,
            ParticipantRef.From(new ServiceParticipant(
                new ParticipantId("participant-runtime"),
                "runtime",
                "service")));

    private sealed class RecordingMetricsSink : IExecutionRuntimeMetricsSink
    {
        public List<RuntimeMetricsEvent> Events { get; } = [];

        public ValueTask RecordAsync(RuntimeMetricsEvent metricsEvent, CancellationToken cancellationToken)
        {
            Events.Add(metricsEvent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingProviderModule : IProviderModule
    {
        private readonly ProviderUsage? usage;

        public RecordingProviderModule(string providerId, ProviderUsage? usage = null)
        {
            this.usage = usage;
            Descriptor = new ProviderDescriptor(
                providerId,
                "Recording Provider",
                ProviderProtocolKind.OpenAiResponses,
                new ProviderCapabilityProfile(SupportsStreaming: true),
                [new TianShu.Contracts.Provider.ProviderModelDescriptor("model-name")]);
        }

        public ProviderDescriptor Descriptor { get; }

        public ProviderInvocationRequest? LastRequest { get; private set; }

        public async IAsyncEnumerable<ProviderStreamEvent> InvokeAsync(
            ProviderInvocationRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            LastRequest = request;
            await Task.Yield();
            yield return new ProviderCompletionEvent(new ProviderCompletion("done", usage));
        }
    }

    private sealed class DisposableProviderModule : IProviderModule, IDisposable
    {
        public DisposableProviderModule(string providerId)
        {
            Descriptor = new ProviderDescriptor(
                providerId,
                "Disposable Provider",
                ProviderProtocolKind.OpenAiResponses,
                new ProviderCapabilityProfile(SupportsStreaming: true),
                [new TianShu.Contracts.Provider.ProviderModelDescriptor("model-name")]);
        }

        public ProviderDescriptor Descriptor { get; }

        public bool Disposed { get; private set; }

        public void Dispose()
            => Disposed = true;

        public async IAsyncEnumerable<ProviderStreamEvent> InvokeAsync(
            ProviderInvocationRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return new ProviderCompletionEvent(new ProviderCompletion("done"));
        }
    }

    private sealed class RetryThenSuccessProviderModule : IProviderModule
    {
        private readonly int failuresBeforeSuccess;

        public RetryThenSuccessProviderModule(string providerId, int failuresBeforeSuccess)
        {
            this.failuresBeforeSuccess = failuresBeforeSuccess;
            Descriptor = new ProviderDescriptor(
                providerId,
                "Retry Provider",
                ProviderProtocolKind.OpenAiResponses,
                new ProviderCapabilityProfile(SupportsStreaming: true),
                [new TianShu.Contracts.Provider.ProviderModelDescriptor("model-name")]);
        }

        public ProviderDescriptor Descriptor { get; }

        public int InvokeCount { get; private set; }

        public async IAsyncEnumerable<ProviderStreamEvent> InvokeAsync(
            ProviderInvocationRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            InvokeCount++;
            await Task.Yield();
            if (InvokeCount <= failuresBeforeSuccess)
            {
                yield return new ProviderFailureEvent(new ProviderFailure(
                    "provider_transient_failure",
                    "Transient provider failure.",
                    isRetryable: true));
                yield break;
            }

            yield return new ProviderCompletionEvent(new ProviderCompletion("done", new ProviderUsage(2, 3)));
        }
    }

    private sealed class TimeoutProviderModule : IProviderModule
    {
        public TimeoutProviderModule(string providerId)
        {
            Descriptor = new ProviderDescriptor(
                providerId,
                "Timeout Provider",
                ProviderProtocolKind.OpenAiResponses,
                new ProviderCapabilityProfile(SupportsStreaming: true),
                [new TianShu.Contracts.Provider.ProviderModelDescriptor("model-name")]);
        }

        public ProviderDescriptor Descriptor { get; }

        public int InvokeCount { get; private set; }

        public async IAsyncEnumerable<ProviderStreamEvent> InvokeAsync(
            ProviderInvocationRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            InvokeCount++;
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            yield return new ProviderCompletionEvent(new ProviderCompletion("late"));
        }
    }
}
