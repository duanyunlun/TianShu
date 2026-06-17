using System.Globalization;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Primitives;

namespace TianShu.Execution.Runtime;

public sealed partial class TianShuExecutionRuntime
{
    public async Task<ExecutionRunResult> ExecuteAsync(
        ExecutionPlan plan,
        ExecutionRuntimeContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(context);

        cancellationToken.ThrowIfCancellationRequested();

        var planFailure = ValidatePlan(plan, context);
        if (planFailure is not null)
        {
            return CreateRunResult(
                plan.PlanId,
                context.ExecutionId,
                RuntimeStepResultStatus.Blocked,
                Array.Empty<RuntimeStepResult>(),
                planFailure);
        }

        var runtimeContext = WithPlanMetadata(context, plan.PlanId);
        var stepResults = new List<RuntimeStepResult>(plan.Steps.Count);
        foreach (var step in plan.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stepResult = await ExecuteStepAsync(step, runtimeContext, cancellationToken).ConfigureAwait(false);
            stepResults.Add(stepResult);

            if (plan.Policy.StopOnFailure && stepResult.Status is not RuntimeStepResultStatus.Succeeded)
            {
                break;
            }
        }

        var status = ResolveRunStatus(stepResults);

        return new ExecutionRunResult(
            plan.PlanId,
            context.ExecutionId,
            status,
            stepResults,
            CreateDiagnosticsRef(context.ExecutionId, plan.PlanId),
            CreateTraceRef(context.ExecutionId, plan.PlanId));
    }

    public async Task<RuntimeStepResult> ExecuteStepAsync(
        RuntimeStep step,
        ExecutionRuntimeContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);

        cancellationToken.ThrowIfCancellationRequested();

        var validationFailure = ValidateStep(step, context);
        if (validationFailure is not null)
        {
            return CreateBlockedStepResult(step, context.ExecutionId, validationFailure);
        }

        if (step is ModelInvocationStep boundModelStep
            && stepBindingRegistry.TryGetProvider(boundModelStep.ProviderModuleId, out var provider))
        {
            return await providerBridge.ExecuteAsync(boundModelStep, context, provider, cancellationToken).ConfigureAwait(false);
        }

        if (step is ToolInvocationStep boundToolStep
            && stepBindingRegistry.TryGetTool(boundToolStep.CapabilityToolId, out var tool))
        {
            return await toolBridge.ExecuteAsync(boundToolStep, context, tool, cancellationToken).ConfigureAwait(false);
        }

        if (step is ToolInvocationStep unboundShellStep
            && IsShellToolId(unboundShellStep.CapabilityToolId))
        {
            return CreateBlockedStepResult(
                unboundShellStep,
                context.ExecutionId,
                new ExecutionFailure("shell_tool_not_opened", "Shell ToolInvocationStep 缺少真实 shell tool binding，已失败关闭。"));
        }

        if (step is ToolInvocationStep unboundMcpStep
            && IsMcpToolId(unboundMcpStep.CapabilityToolId))
        {
            return CreateBlockedStepResult(
                unboundMcpStep,
                context.ExecutionId,
                new ExecutionFailure(
                    unboundMcpStep.CapabilityToolId.StartsWith("mcp.", StringComparison.Ordinal)
                        ? "mcp_tool_not_opened"
                        : "mcp_resource_not_opened",
                    "MCP ToolInvocationStep 缺少真实 MCP tool/resource binding，已失败关闭。"));
        }

        if (step is ModuleCapabilityStep subAgentStep
            && stepBindingRegistry.TryGetSubAgentModule(subAgentStep.ModuleId, out var subAgentModule))
        {
            return await subAgentModuleBridge.ExecuteSpawnAsync(subAgentStep, context, subAgentModule, cancellationToken).ConfigureAwait(false);
        }

        if (step is ModuleCapabilityStep memoryStep
            && stepBindingRegistry.TryGetMemoryModule(memoryStep.ModuleId, out var memoryModule))
        {
            var memoryOperation = CreateMemoryModuleOperation(memoryStep);
            if (memoryOperation.Failure is not null)
            {
                return CreateBlockedStepResult(memoryStep, context.ExecutionId, memoryOperation.Failure);
            }

            var operationContext = CreateMemoryOperationContext(memoryStep);
            if (memoryOperation.Query is not null)
            {
                return await memoryModuleBridge.ExecuteQueryAsync(memoryStep, context, memoryModule, memoryOperation.Query, operationContext, cancellationToken).ConfigureAwait(false);
            }

            return await memoryModuleBridge.ExecuteMutationAsync(memoryStep, context, memoryModule, memoryOperation.Mutation!, operationContext, cancellationToken).ConfigureAwait(false);
        }

        if (step is ModuleCapabilityStep { ModuleId: "memory.identity" } unboundMemoryStep)
        {
            return CreateBlockedStepResult(
                unboundMemoryStep,
                context.ExecutionId,
                new ExecutionFailure("memory_module_not_bound", "Memory ModuleCapabilityStep 缺少真实 IMemoryModule 绑定。"));
        }

        if (step is ModuleCapabilityStep { ModuleId: "module.sub_agent" } unboundSubAgentStep)
        {
            return CreateBlockedStepResult(
                unboundSubAgentStep,
                context.ExecutionId,
                new ExecutionFailure("subagent_module_not_bound", "Sub-Agent ModuleCapabilityStep 缺少真实 ISubAgentModule 绑定。"));
        }

        return step switch
        {
            ModelInvocationStep modelStep => CreateSucceededStepResult(
                modelStep,
                context.ExecutionId,
                "model.invocation",
                new Dictionary<string, object?>
                {
                    ["providerModuleId"] = modelStep.ProviderModuleId,
                    ["providerKey"] = modelStep.InputEnvelope.ProviderKey,
                    ["model"] = modelStep.InputEnvelope.Model,
                }),
            ToolInvocationStep toolStep => CreateSucceededStepResult(
                toolStep,
                context.ExecutionId,
                "tool.invocation",
                new Dictionary<string, object?>
                {
                    ["capabilityToolId"] = toolStep.CapabilityToolId,
                    ["toolId"] = toolStep.InputEnvelope.ToolId,
                    ["operation"] = toolStep.InputEnvelope.Operation,
                }),
            StateCommitStep stateStep => CreateSucceededStepResult(
                stateStep,
                context.ExecutionId,
                "state.commit",
                new Dictionary<string, object?>
                {
                    ["stateStoreId"] = stateStep.StateStoreId,
                }),
            ArtifactStep artifactStep => CreateSucceededStepResult(
                artifactStep,
                context.ExecutionId,
                "artifact.operation",
                new Dictionary<string, object?>
                {
                    ["artifactOperation"] = artifactStep.ArtifactOperation,
                }),
            DiagnosticStep diagnosticStep => CreateSucceededStepResult(
                diagnosticStep,
                context.ExecutionId,
                "diagnostic.emit",
                new Dictionary<string, object?>
                {
                    ["diagnosticKind"] = diagnosticStep.DiagnosticKind,
                }),
            HostInteractionStep hostInteractionStep => CreateSucceededStepResult(
                hostInteractionStep,
                context.ExecutionId,
                "host.interaction",
                new Dictionary<string, object?>
                {
                    ["interactionKind"] = hostInteractionStep.InteractionKind,
                }),
            ModuleCapabilityStep moduleStep => CreateSucceededStepResult(
                moduleStep,
                context.ExecutionId,
                "module.capability",
                new Dictionary<string, object?>
                {
                    ["moduleId"] = moduleStep.ModuleId,
                    ["capabilityId"] = moduleStep.CapabilityId,
                }),
            _ => CreateBlockedStepResult(
                step,
                context.ExecutionId,
                new ExecutionFailure("runtime_step_kind_not_supported", "Execution Runtime 不支持该 RuntimeStep 类型。")),
        };
    }

    private static ExecutionRuntimeContext WithPlanMetadata(ExecutionRuntimeContext context, string planId)
    {
        if (context.Metadata.TryGetValue("planId", out var existing)
            && string.Equals(existing.StringValue, planId, StringComparison.Ordinal))
        {
            return context;
        }

        var entries = new Dictionary<string, StructuredValue>(context.Metadata.Entries, StringComparer.Ordinal)
        {
            ["planId"] = StructuredValue.FromString(planId),
        };
        return new ExecutionRuntimeContext(
            context.ExecutionId,
            context.KernelRunId,
            context.Governance,
            context.WorkingDirectory,
            new MetadataBag(entries));
    }

    private static RuntimeStepResultStatus ResolveRunStatus(IReadOnlyList<RuntimeStepResult> stepResults)
    {
        if (stepResults.All(static result => result.Status == RuntimeStepResultStatus.Succeeded))
        {
            return RuntimeStepResultStatus.Succeeded;
        }

        if (stepResults.Any(static result => result.Status == RuntimeStepResultStatus.Failed))
        {
            return RuntimeStepResultStatus.Failed;
        }

        if (stepResults.Any(static result => result.Status == RuntimeStepResultStatus.Cancelled))
        {
            return RuntimeStepResultStatus.Cancelled;
        }

        return RuntimeStepResultStatus.Blocked;
    }

    private static ExecutionFailure? ValidatePlan(ExecutionPlan plan, ExecutionRuntimeContext context)
    {
        if (string.IsNullOrWhiteSpace(plan.SourceGraphId.Value))
        {
            return new ExecutionFailure("execution_plan_missing_source_graph", "ExecutionPlan 缺少 SourceGraphId。");
        }

        if (string.IsNullOrWhiteSpace(plan.SourceIntentId.Value))
        {
            return new ExecutionFailure("execution_plan_missing_source_intent", "ExecutionPlan 缺少 SourceIntentId。");
        }

        if (plan.TracePolicy is null)
        {
            return new ExecutionFailure("execution_plan_missing_trace_policy", "ExecutionPlan 缺少 TracePolicy。");
        }

        if (context.Governance is null)
        {
            return new ExecutionFailure("execution_context_missing_governance", "ExecutionRuntimeContext 缺少 GovernanceEnvelope。");
        }

        if (context.Governance.MaxSideEffectLevel == SideEffectLevel.Unspecified)
        {
            return new ExecutionFailure("execution_context_unspecified_side_effect_ceiling", "GovernanceEnvelope 未声明副作用上限。");
        }

        foreach (var step in plan.Steps)
        {
            if (!string.Equals(step.SourceIntentId.Value, plan.SourceIntentId.Value, StringComparison.Ordinal)
                || !string.Equals(step.SourceGraphId.Value, plan.SourceGraphId.Value, StringComparison.Ordinal))
            {
                return new ExecutionFailure("execution_plan_step_source_mismatch", "RuntimeStep 来源与 ExecutionPlan 不一致。");
            }
        }

        return null;
    }

    internal static ExecutionFailure? ValidateStep(RuntimeStep step, ExecutionRuntimeContext context)
    {
        if (string.IsNullOrWhiteSpace(step.StepId))
        {
            return new ExecutionFailure("runtime_step_missing_step_id", "RuntimeStep 缺少 StepId。");
        }

        if (string.IsNullOrWhiteSpace(step.SourceIntentId.Value))
        {
            return new ExecutionFailure("runtime_step_missing_source_intent", "RuntimeStep 缺少 SourceIntentId。");
        }

        if (string.IsNullOrWhiteSpace(step.SourceGraphId.Value))
        {
            return new ExecutionFailure("runtime_step_missing_source_graph", "RuntimeStep 缺少 SourceGraphId。");
        }

        if (string.IsNullOrWhiteSpace(step.SourceStageId.Value))
        {
            return new ExecutionFailure("runtime_step_missing_source_stage", "RuntimeStep 缺少 SourceStageId。");
        }

        if (string.IsNullOrWhiteSpace(step.SourceKernelOperationId.Value))
        {
            return new ExecutionFailure("runtime_step_missing_source_kernel_operation", "RuntimeStep 缺少 SourceKernelOperationId。");
        }

        if (step.Permission is null)
        {
            return new ExecutionFailure("runtime_step_missing_permission", "RuntimeStep 缺少 PermissionEnvelope。");
        }

        if (step.SideEffect is null)
        {
            return new ExecutionFailure("runtime_step_missing_side_effect", "RuntimeStep 缺少 SideEffectProfile。");
        }

        if (step.SideEffect.Level == SideEffectLevel.Unspecified)
        {
            return new ExecutionFailure("runtime_step_unspecified_side_effect", "RuntimeStep 未声明副作用等级。");
        }

        if (step.Budget is null)
        {
            return new ExecutionFailure("runtime_step_missing_budget", "RuntimeStep 缺少 KernelBudget。");
        }

        if (step.ExpectedOutputContract is null)
        {
            return new ExecutionFailure("runtime_step_missing_output_contract", "RuntimeStep 缺少 ExpectedOutputContract。");
        }

        if (step.TracePolicy is null)
        {
            return new ExecutionFailure("runtime_step_missing_trace_policy", "RuntimeStep 缺少 TracePolicy。");
        }

        if (context.Governance is null)
        {
            return new ExecutionFailure("execution_context_missing_governance", "ExecutionRuntimeContext 缺少 GovernanceEnvelope。");
        }

        if (context.Governance.MaxSideEffectLevel == SideEffectLevel.Unspecified)
        {
            return new ExecutionFailure("execution_context_unspecified_side_effect_ceiling", "GovernanceEnvelope 未声明副作用上限。");
        }

        if (step.SideEffect.Level > context.Governance.MaxSideEffectLevel)
        {
            return new ExecutionFailure("runtime_step_side_effect_exceeds_governance", "RuntimeStep 副作用等级超过 GovernanceEnvelope 上限。");
        }

        if (step.Permission.RequiresHumanGate)
        {
            if (context.Governance.RequiresHumanGate is false)
            {
                return new ExecutionFailure("runtime_step_human_gate_not_granted", "RuntimeStep 需要人工 gate，但 GovernanceEnvelope 未授予。");
            }

            if (context.Governance.ApprovalIds.Count == 0)
            {
                return new ExecutionFailure("runtime_step_missing_approval", "RuntimeStep 需要人工 gate，但 GovernanceEnvelope 缺少审批引用。");
            }
        }

        if (step is ModelInvocationStep modelStep)
        {
            if (!context.Governance.AllowedModuleIds.Contains(modelStep.ProviderModuleId, StringComparer.Ordinal))
            {
                return new ExecutionFailure("runtime_step_provider_module_not_allowed", "ModelInvocationStep 的 provider module 不在 GovernanceEnvelope allow-list 内。");
            }

            if (context.Governance.PolicyIds.Count > 0
                && !context.Governance.PolicyIds.Contains(modelStep.ModelRoute.PolicyId, StringComparer.Ordinal))
            {
                return new ExecutionFailure("runtime_step_model_route_policy_not_allowed", "ModelInvocationStep 的 ModelRoutePolicy 不在 GovernanceEnvelope 内。");
            }
        }

        if (step is ToolInvocationStep toolStep)
        {
            if (!context.Governance.AllowedToolIds.Contains(toolStep.CapabilityToolId, StringComparer.Ordinal))
            {
                return new ExecutionFailure("runtime_step_capability_tool_not_allowed", "ToolInvocationStep 的 capability tool 不在 GovernanceEnvelope allow-list 内。");
            }

            if (!context.Governance.AllowedToolIds.Contains(toolStep.InputEnvelope.ToolId, StringComparer.Ordinal))
            {
                return new ExecutionFailure("runtime_step_tool_not_allowed", "ToolInvocationStep 的 tool 不在 GovernanceEnvelope allow-list 内。");
            }
        }

        if (step is ModuleCapabilityStep moduleStep
            && !context.Governance.AllowedModuleIds.Contains(moduleStep.ModuleId, StringComparer.Ordinal))
        {
            return new ExecutionFailure("runtime_step_module_not_allowed", "ModuleCapabilityStep 的 module 不在 GovernanceEnvelope allow-list 内。");
        }

        return null;
    }

    private static RuntimeStepResult CreateSucceededStepResult(
        RuntimeStep step,
        ExecutionId executionId,
        string runtimeOperation,
        IReadOnlyDictionary<string, object?> facts)
    {
        var output = new Dictionary<string, object?>(facts, StringComparer.Ordinal)
        {
            ["runtimeBoundary"] = "execution.runtime.approved_step",
            ["runtimeOperation"] = runtimeOperation,
            ["stepId"] = step.StepId,
            ["stepKind"] = step.StepKind.ToString(),
            ["sourceIntentId"] = step.SourceIntentId.Value,
            ["sourceGraphId"] = step.SourceGraphId.Value,
            ["sourceStageId"] = step.SourceStageId.Value,
            ["sourceKernelOperationId"] = step.SourceKernelOperationId.Value,
        };

        return new RuntimeStepResult(
            step.StepId,
            step.StepKind,
            RuntimeStepResultStatus.Succeeded,
            StructuredValue.FromPlainObject(output),
            diagnosticsRef: CreateDiagnosticsRef(executionId, step.StepId),
            traceRef: CreateTraceRef(executionId, step.StepId));
    }

    private static RuntimeStepResult CreateBlockedStepResult(
        RuntimeStep step,
        ExecutionId executionId,
        ExecutionFailure failure)
        => new(
            step.StepId,
            step.StepKind,
            RuntimeStepResultStatus.Blocked,
            failure: failure,
            diagnosticsRef: CreateDiagnosticsRef(executionId, step.StepId),
            traceRef: CreateTraceRef(executionId, step.StepId));

    private static ExecutionRunResult CreateRunResult(
        string planId,
        ExecutionId executionId,
        RuntimeStepResultStatus status,
        IReadOnlyList<RuntimeStepResult> stepResults,
        ExecutionFailure failure)
        => new(
            planId,
            executionId,
            status,
            stepResults,
            CreateDiagnosticsRef(executionId, planId, failure.Code),
            CreateTraceRef(executionId, planId, failure.Code));

    private static string CreateDiagnosticsRef(ExecutionId executionId, string subject, string? suffix = null)
        => string.IsNullOrWhiteSpace(suffix)
            ? $"diagnostics://execution/{executionId.Value}/{subject}"
            : $"diagnostics://execution/{executionId.Value}/{subject}/{suffix}";

    private static string CreateTraceRef(ExecutionId executionId, string subject, string? suffix = null)
        => string.IsNullOrWhiteSpace(suffix)
            ? $"trace://execution/{executionId.Value}/{subject}"
            : $"trace://execution/{executionId.Value}/{subject}/{suffix}";

    private static bool IsShellToolId(string toolId)
        => string.Equals(toolId, "shell", StringComparison.Ordinal)
           || string.Equals(toolId, "local_shell", StringComparison.Ordinal)
           || string.Equals(toolId, "shell_command", StringComparison.Ordinal)
           || string.Equals(toolId, "exec_command", StringComparison.Ordinal)
           || string.Equals(toolId, "write_stdin", StringComparison.Ordinal);

    private static bool IsMcpToolId(string toolId)
        => toolId.StartsWith("mcp.", StringComparison.Ordinal)
           || string.Equals(toolId, "list_mcp_resources", StringComparison.Ordinal)
           || string.Equals(toolId, "list_mcp_resource_templates", StringComparison.Ordinal)
           || string.Equals(toolId, "read_mcp_resource", StringComparison.Ordinal);

    private sealed record MemoryRuntimeOperation(
        MemoryModuleQuery? Query = null,
        MemoryModuleMutation? Mutation = null,
        ExecutionFailure? Failure = null);

    private static MemoryRuntimeOperation CreateMemoryModuleOperation(ModuleCapabilityStep step)
        => step.CapabilityId switch
        {
            "memory.retrieve" => CreateMemoryRetrieveOperation(step),
            "memory.form" => CreateMemoryFormOperation(step),
            "memory.supersede" => CreateMemorySupersedeOperation(step),
            _ => new MemoryRuntimeOperation(Failure: new ExecutionFailure(
                "memory_module_capability_not_opened",
                $"Memory Module capability 未作为 P27.14 正式能力开放：{step.CapabilityId}。")),
        };

    private static MemoryRuntimeOperation CreateMemoryRetrieveOperation(ModuleCapabilityStep step)
    {
        var queryText = ReadString(step.InputEnvelope, "queryText")
                        ?? ReadString(step.InputEnvelope, "query")
                        ?? ReadString(step.InputEnvelope, "objective");
        var memorySpaceId = ReadString(step.InputEnvelope, "memorySpaceId");
        var key = ReadString(step.InputEnvelope, "key");
        return new MemoryRuntimeOperation(Query: new FilterMemoryModuleQuery(new FilterMemory(
            string.IsNullOrWhiteSpace(memorySpaceId) ? null : new MemorySpaceId(memorySpaceId),
            key,
            queryText)));
    }

    private static MemoryRuntimeOperation CreateMemoryFormOperation(ModuleCapabilityStep step)
    {
        var memorySpaceId = ReadString(step.InputEnvelope, "memorySpaceId");
        var key = ReadString(step.InputEnvelope, "key");
        var value = ReadValue(step.InputEnvelope, "value");
        if (string.IsNullOrWhiteSpace(memorySpaceId) || string.IsNullOrWhiteSpace(key) || value is null)
        {
            return new MemoryRuntimeOperation(Failure: new ExecutionFailure(
                "memory_form_payload_invalid",
                "memory.form 需要 memorySpaceId、key 和 value。"));
        }

        return new MemoryRuntimeOperation(Mutation: new AddMemoryModuleMutation(new AddMemory(
            new MemorySpaceId(memorySpaceId),
            key,
            value,
            ReadDecimal(step.InputEnvelope, "confidence") ?? 1m,
            CreateMemorySource(step))));
    }

    private static MemoryRuntimeOperation CreateMemorySupersedeOperation(ModuleCapabilityStep step)
    {
        var oldRecordId = ReadString(step.InputEnvelope, "oldRecordId");
        var memorySpaceId = ReadString(step.InputEnvelope, "memorySpaceId");
        var newKey = ReadString(step.InputEnvelope, "newKey") ?? ReadString(step.InputEnvelope, "key");
        var newValue = ReadValue(step.InputEnvelope, "newValue") ?? ReadValue(step.InputEnvelope, "value");
        var reason = ReadString(step.InputEnvelope, "reason");
        if (string.IsNullOrWhiteSpace(oldRecordId)
            || string.IsNullOrWhiteSpace(memorySpaceId)
            || string.IsNullOrWhiteSpace(newKey)
            || newValue is null
            || string.IsNullOrWhiteSpace(reason))
        {
            return new MemoryRuntimeOperation(Failure: new ExecutionFailure(
                "memory_supersede_payload_invalid",
                "memory.supersede 需要 oldRecordId、memorySpaceId、newKey/newValue 和 reason。"));
        }

        return new MemoryRuntimeOperation(Mutation: new SupersedeMemoryModuleMutation(new SupersedeMemory(
            new MemoryRecordId(oldRecordId),
            new MemorySpaceId(memorySpaceId),
            newKey,
            newValue,
            reason,
            ReadDecimal(step.InputEnvelope, "confidence") ?? 1m,
            CreateMemorySource(step))));
    }

    private static MemoryOperationContext CreateMemoryOperationContext(ModuleCapabilityStep step)
        => new(
            ReadString(step.InputEnvelope, "actorId") ?? "execution.runtime",
            correlationId: step.StepId,
            policyOverrides: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sourceIntentId"] = step.SourceIntentId.Value,
                ["sourceGraphId"] = step.SourceGraphId.Value,
                ["sourceStageId"] = step.SourceStageId.Value,
                ["sourceKernelOperationId"] = step.SourceKernelOperationId.Value,
                ["capabilityId"] = step.CapabilityId,
            });

    private static MemorySourceRef? CreateMemorySource(ModuleCapabilityStep step)
    {
        var sourceId = ReadString(step.InputEnvelope, "sourceId");
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return null;
        }

        var sourceKind = ReadEnum(step.InputEnvelope, "sourceKind", MemorySourceKind.Conversation);
        return new MemorySourceRef(
            sourceKind,
            sourceId,
            role: ReadString(step.InputEnvelope, "sourceRole"),
            path: ReadString(step.InputEnvelope, "sourcePath"),
            url: ReadString(step.InputEnvelope, "sourceUrl"),
            snippet: ReadString(step.InputEnvelope, "sourceSnippet"));
    }

    private static StructuredValue? ReadValue(StructuredValue envelope, string key)
        => envelope.Kind == StructuredValueKind.Object && envelope.Properties.TryGetValue(key, out var value)
            ? value
            : null;

    private static string? ReadString(StructuredValue envelope, string key)
    {
        var value = ReadValue(envelope, key);
        if (value is null)
        {
            return null;
        }

        try
        {
            return value.GetString();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static decimal? ReadDecimal(StructuredValue envelope, string key)
    {
        var value = ReadString(envelope, key);
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static TEnum ReadEnum<TEnum>(StructuredValue envelope, string key, TEnum fallback)
        where TEnum : struct
        => Enum.TryParse<TEnum>(ReadString(envelope, key), ignoreCase: true, out var parsed)
            ? parsed
            : fallback;
}
