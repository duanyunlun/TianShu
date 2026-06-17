using System.Globalization;
using TianShu.Contracts.Agents;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Modules;
using TianShu.Contracts.Primitives;

namespace TianShu.Execution.Runtime;

public interface IExecutionRuntimeSubAgentModuleBridge
{
    Task<RuntimeStepResult> ExecuteSpawnAsync(
        ModuleCapabilityStep step,
        ExecutionRuntimeContext context,
        ISubAgentModule module,
        CancellationToken cancellationToken);
}

/// <summary>
/// Execution Runtime 到 Sub-Agent Module 的受治理桥接入口。
/// Governed bridge from Execution Runtime to the Sub-Agent Module.
/// </summary>
public sealed class ExecutionRuntimeSubAgentModuleBridge : IExecutionRuntimeSubAgentModuleBridge
{
    public async Task<RuntimeStepResult> ExecuteSpawnAsync(
        ModuleCapabilityStep step,
        ExecutionRuntimeContext context,
        ISubAgentModule module,
        CancellationToken cancellationToken)
    {
        var validationFailure = Validate(step, context, module);
        if (validationFailure is not null)
        {
            return CreateBlockedResult(step, context, validationFailure);
        }

        if (!TryReadSpawnRequest(step.InputEnvelope, out var request, out var requestFailure))
        {
            return CreateBlockedResult(step, context, requestFailure);
        }

        if (!TryReadLineageFromMetadata(step.Metadata, "subAgent.childLineage", out var childLineage, out var lineageFailure))
        {
            return CreateBlockedResult(step, context, lineageFailure);
        }

        if (!TryReadQuota(step, out var quota, out var quotaFailure))
        {
            return CreateBlockedResult(step, context, quotaFailure);
        }

        var budgetFailure = ValidateRequestedBudgetWithinStepBudget(request.RequestedBudget, step.Budget);
        if (budgetFailure is not null)
        {
            return CreateBlockedResult(step, context, budgetFailure);
        }

        SubAgentRunResult result;
        try
        {
            result = await module.SpawnAsync(
                    request,
                    childLineage,
                    quota,
                    CreateInvocationContext(step, context),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CreateCancelledToolResult(step, context, request);
        }

        return CreateToolResult(step, context, request, result);
    }

    private static ExecutionFailure? Validate(
        ModuleCapabilityStep step,
        ExecutionRuntimeContext context,
        ISubAgentModule module)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(module);

        var governanceFailure = TianShuExecutionRuntime.ValidateStep(step, context);
        if (governanceFailure is not null)
        {
            return governanceFailure;
        }

        if (module.Descriptor.Kind != ModuleKind.SubAgentOrchestration)
        {
            return new ExecutionFailure("subagent_module_kind_mismatch", "ModuleCapabilityStep 指向的模块不是 Sub-Agent Orchestration Module。");
        }

        if (!string.Equals(step.ModuleId, module.Descriptor.ModuleId, StringComparison.Ordinal))
        {
            return new ExecutionFailure("subagent_module_descriptor_mismatch", "ModuleCapabilityStep 的 module 标识与 Sub-Agent ModuleDescriptor 不一致。");
        }

        if (!module.Descriptor.Capabilities.Any(capability => string.Equals(capability.CapabilityId, step.CapabilityId, StringComparison.Ordinal)))
        {
            return new ExecutionFailure("subagent_module_capability_missing", "Sub-Agent ModuleDescriptor 未声明当前 capability。");
        }

        if (!string.Equals(step.CapabilityId, "sub_agent.spawn", StringComparison.Ordinal))
        {
            return new ExecutionFailure("subagent_module_capability_mismatch", "Sub-Agent bridge 只接受 sub_agent.spawn capability。");
        }

        if (!module.Descriptor.IsAllowedBy(context.Governance))
        {
            return new ExecutionFailure("subagent_module_governance_denied", "Sub-Agent Module descriptor 超出 GovernanceEnvelope。");
        }

        return null;
    }

    private static SubAgentModuleInvocationContext CreateInvocationContext(
        ModuleCapabilityStep step,
        ExecutionRuntimeContext context)
        => new(
            step.StepId,
            step.SourceIntentId.Value,
            step.SourceGraphId.Value,
            step.SourceStageId.Value,
            step.SourceKernelOperationId.Value,
            step.Permission,
            step.SideEffect,
            context.ExecutionId.Value,
            context.KernelRunId.Value,
            context.Governance,
            context.WorkingDirectory,
            CreateInvocationMetadata(step, context));

    private static MetadataBag CreateInvocationMetadata(ModuleCapabilityStep step, ExecutionRuntimeContext context)
    {
        var entries = new Dictionary<string, StructuredValue>(step.Metadata.Entries, StringComparer.Ordinal)
        {
            ["subAgent.parentGovernance"] = StructuredValue.FromPlainObject(CreateGovernancePlainObject(context.Governance)),
            ["subAgent.parentTracePolicy"] = StructuredValue.FromPlainObject(CreateTracePolicyPlainObject(step.TracePolicy)),
            ["subAgent.parentRuntimeStepBudget"] = StructuredValue.FromPlainObject(CreateBudgetPlainObject(step.Budget)),
            ["subAgent.parentPermission"] = StructuredValue.FromPlainObject(new Dictionary<string, object?>
            {
                ["scopes"] = step.Permission.Scopes,
                ["grants"] = step.Permission.Grants,
                ["requiresHumanGate"] = step.Permission.RequiresHumanGate,
                ["reason"] = step.Permission.Reason,
            }),
            ["subAgent.parentSideEffect"] = StructuredValue.FromPlainObject(new Dictionary<string, object?>
            {
                ["level"] = step.SideEffect.Level.ToString(),
                ["affectedResources"] = step.SideEffect.AffectedResources,
                ["reversible"] = step.SideEffect.Reversible,
                ["requiresAudit"] = step.SideEffect.RequiresAudit,
            }),
            ["subAgent.parentExecutionId"] = StructuredValue.FromString(context.ExecutionId.Value),
            ["subAgent.parentKernelRunId"] = StructuredValue.FromString(context.KernelRunId.Value),
        };
        if (!string.IsNullOrWhiteSpace(context.WorkingDirectory))
        {
            entries["subAgent.parentWorkingDirectory"] = StructuredValue.FromString(context.WorkingDirectory!);
        }

        return new MetadataBag(entries);
    }

    private static RuntimeStepResult CreateToolResult(
        ModuleCapabilityStep step,
        ExecutionRuntimeContext context,
        SubAgentSpawnRequest request,
        SubAgentRunResult result)
    {
        var status = result.Status switch
        {
            SubAgentRunStatus.Completed => "succeeded",
            SubAgentRunStatus.Failed => "failed",
            SubAgentRunStatus.Blocked => "blocked",
            SubAgentRunStatus.Cancelled => "cancelled",
            _ => "failed",
        };
        var failure = result.Failure is null
            ? null
            : new ExecutionFailure(result.Failure.Code, result.Failure.Message);

        return new RuntimeStepResult(
            step.StepId,
            RuntimeStepKind.ModuleCapability,
            RuntimeStepResultStatus.Succeeded,
            CreateToolResultOutput(step, context, request.SpawnCallId, result.ChildRunId, result.ChildThreadId, status, result.ResultText, result.ReplaySummaryRef, result.DiagnosticsRefs, failure),
            diagnosticsRef: $"diagnostics://execution/{context.ExecutionId.Value}/{step.StepId}/subagent",
            traceRef: $"trace://execution/{context.ExecutionId.Value}/{step.StepId}/subagent/{result.ChildRunId}");
    }

    private static RuntimeStepResult CreateCancelledToolResult(
        ModuleCapabilityStep step,
        ExecutionRuntimeContext context,
        SubAgentSpawnRequest request)
    {
        var failure = new ExecutionFailure("subagent_spawn_cancelled", "Sub-Agent spawn 调用已取消。");
        return new RuntimeStepResult(
            step.StepId,
            RuntimeStepKind.ModuleCapability,
            RuntimeStepResultStatus.Cancelled,
            CreateToolResultOutput(step, context, request.SpawnCallId, childRunId: null, childThreadId: null, "cancelled", resultText: null, replaySummaryRef: null, Array.Empty<string>(), failure),
            failure,
            diagnosticsRef: $"diagnostics://execution/{context.ExecutionId.Value}/{step.StepId}/{failure.Code}",
            traceRef: $"trace://execution/{context.ExecutionId.Value}/{step.StepId}/{failure.Code}");
    }

    private static StructuredValue CreateToolResultOutput(
        ModuleCapabilityStep step,
        ExecutionRuntimeContext context,
        string callId,
        string? childRunId,
        string? childThreadId,
        string status,
        string? resultText,
        string? replaySummaryRef,
        IReadOnlyList<string> diagnosticsRefs,
        ExecutionFailure? failure)
        => StructuredValue.FromPlainObject(new Dictionary<string, object?>
        {
            ["runtimeBoundary"] = "execution.runtime.subagent_module_bridge",
            ["runtimePlanId"] = ResolvePlanId(context),
            ["stepId"] = step.StepId,
            ["stepKind"] = step.StepKind.ToString(),
            ["sourceIntentId"] = step.SourceIntentId.Value,
            ["sourceGraphId"] = step.SourceGraphId.Value,
            ["sourceStageId"] = step.SourceStageId.Value,
            ["sourceKernelOperationId"] = step.SourceKernelOperationId.Value,
            ["signals"] = new[] { "tool.results.materialized", $"tool.result.{status}", "subagent.result.materialized" },
            ["moduleId"] = step.ModuleId,
            ["capabilityId"] = step.CapabilityId,
            ["toolResults"] = new object?[]
            {
                new Dictionary<string, object?>
                {
                    ["callId"] = callId,
                    ["toolId"] = "spawn_agent",
                    ["status"] = status,
                    ["output"] = new Dictionary<string, object?>
                    {
                        ["resultText"] = resultText,
                        ["childRunId"] = childRunId,
                        ["childThreadId"] = childThreadId,
                        ["replaySummaryRef"] = replaySummaryRef,
                        ["diagnosticsRefs"] = diagnosticsRefs,
                    },
                    ["failure"] = failure is null
                        ? null
                        : new Dictionary<string, object?>
                        {
                            ["code"] = failure.Code,
                            ["message"] = failure.Message,
                            ["isRetryable"] = failure.IsRetryable,
                        },
                    ["auditRef"] = $"audit://execution/{context.ExecutionId.Value}/{step.StepId}/subagent/{callId}",
                },
            },
        });

    private static RuntimeStepResult CreateBlockedResult(
        ModuleCapabilityStep step,
        ExecutionRuntimeContext context,
        ExecutionFailure failure)
        => new(
            step.StepId,
            RuntimeStepKind.ModuleCapability,
            RuntimeStepResultStatus.Blocked,
            failure: failure,
            diagnosticsRef: $"diagnostics://execution/{context.ExecutionId.Value}/{step.StepId}/{failure.Code}",
            traceRef: $"trace://execution/{context.ExecutionId.Value}/{step.StepId}/{failure.Code}");

    private static string? ResolvePlanId(ExecutionRuntimeContext context)
        => context.Metadata.TryGetValue("planId", out var planId) ? planId.GetString() : null;

    private static ExecutionFailure? ValidateRequestedBudgetWithinStepBudget(KernelBudget requested, KernelBudget stepBudget)
        => requested.TokenBudget > stepBudget.TokenBudget
           || requested.TimeBudgetMs > stepBudget.TimeBudgetMs
           || requested.CostBudget > stepBudget.CostBudget
           || requested.RetryBudget > stepBudget.RetryBudget
           || requested.ToolCallBudget > stepBudget.ToolCallBudget
            ? new ExecutionFailure("subagent_requested_budget_exceeds_step_budget", "Sub-Agent requestedBudget 超出父 RuntimeStep 预算。")
            : null;

    private static Dictionary<string, object?> CreateGovernancePlainObject(GovernanceEnvelope governance)
        => new(StringComparer.Ordinal)
        {
            ["envelopeId"] = governance.EnvelopeId,
            ["policyIds"] = governance.PolicyIds,
            ["allowedToolIds"] = governance.AllowedToolIds,
            ["allowedModuleIds"] = governance.AllowedModuleIds,
            ["maxSideEffectLevel"] = governance.MaxSideEffectLevel.ToString(),
            ["requiresHumanGate"] = governance.RequiresHumanGate,
            ["approvalIds"] = governance.ApprovalIds.Select(static approval => approval.Value).ToArray(),
            ["auditRecordIds"] = governance.AuditRecordIds.Select(static audit => audit.Value).ToArray(),
        };

    private static Dictionary<string, object?> CreateTracePolicyPlainObject(TracePolicy tracePolicy)
        => new(StringComparer.Ordinal)
        {
            ["enabled"] = tracePolicy.Enabled,
            ["requireDiagnosticsRef"] = tracePolicy.RequireDiagnosticsRef,
            ["requireRuntimeTraceRef"] = tracePolicy.RequireRuntimeTraceRef,
            ["requiredEventKinds"] = tracePolicy.RequiredEventKinds,
        };

    private static Dictionary<string, object?> CreateBudgetPlainObject(KernelBudget budget)
        => new(StringComparer.Ordinal)
        {
            ["tokenBudget"] = budget.TokenBudget,
            ["timeBudgetMs"] = budget.TimeBudgetMs,
            ["costBudget"] = budget.CostBudget,
            ["retryBudget"] = budget.RetryBudget,
            ["toolCallBudget"] = budget.ToolCallBudget,
        };

    private static bool TryReadSpawnRequest(
        StructuredValue value,
        out SubAgentSpawnRequest request,
        out ExecutionFailure failure)
    {
        request = null!;
        if (value.Kind != StructuredValueKind.Object)
        {
            failure = InvalidPayload("subagent_spawn_request_invalid", "Sub-Agent spawn inputEnvelope 必须是对象。");
            return false;
        }

        if (!TryReadRequiredString(value, "spawnCallId", out var spawnCallId)
            || !TryReadRequiredString(value, "taskBrief", out var taskBrief)
            || !TryReadObject(value, "parentLineage", out var parentLineageValue)
            || !TryReadObject(value, "parentSubject", out var parentSubjectValue)
            || !TryReadObject(value, "requestedGovernance", out var governanceValue)
            || !TryReadObject(value, "requestedBudget", out var budgetValue))
        {
            failure = InvalidPayload("subagent_spawn_request_missing_required_field", "Sub-Agent spawn inputEnvelope 缺少必需字段。");
            return false;
        }

        if (!TryReadLineage(parentLineageValue, out var parentLineage, out failure)
            || !TryReadSubject(parentSubjectValue, out var parentSubject, out failure)
            || !TryReadGovernance(governanceValue, out var requestedGovernance, out failure)
            || !TryReadBudget(budgetValue, out var requestedBudget, out failure))
        {
            return false;
        }

        var evidenceRefs = TryReadStringList(value, "evidenceRefs", out var evidenceValues)
            ? evidenceValues
            : Array.Empty<string>();
        var requiresHumanGate = TryReadBool(value, "requiresHumanGate", out var humanGate) && humanGate;
        var metadata = TryReadObject(value, "metadata", out var metadataValue)
            ? new MetadataBag(metadataValue.Properties)
            : MetadataBag.Empty;

        request = new SubAgentSpawnRequest(
            spawnCallId,
            parentLineage,
            parentSubject,
            taskBrief,
            evidenceRefs,
            requestedGovernance,
            requestedBudget,
            requiresHumanGate,
            metadata);
        failure = null!;
        return true;
    }

    private static bool TryReadLineageFromMetadata(
        MetadataBag metadata,
        string key,
        out SubAgentLineage lineage,
        out ExecutionFailure failure)
    {
        lineage = null!;
        if (!metadata.TryGetValue(key, out var value) || value.Kind != StructuredValueKind.Object)
        {
            failure = InvalidPayload("subagent_child_lineage_missing", "ModuleCapabilityStep.Metadata 缺少 admission 生成的 childLineage。");
            return false;
        }

        return TryReadLineage(value, out lineage, out failure);
    }

    private static bool TryReadLineage(
        StructuredValue value,
        out SubAgentLineage lineage,
        out ExecutionFailure failure)
    {
        lineage = null!;
        if (!TryReadRequiredString(value, "rootRunId", out var rootRunId)
            || !TryReadRequiredString(value, "currentRunId", out var currentRunId)
            || !TryReadInt(value, "depth", out var depth)
            || !TryReadInt(value, "siblingIndex", out var siblingIndex)
            || !TryReadRequiredString(value, "ledgerRef", out var ledgerRef))
        {
            failure = InvalidPayload("subagent_lineage_invalid", "SubAgentLineage 缺少必需字段。");
            return false;
        }

        var parentRunId = TryReadRequiredString(value, "parentRunId", out var parent) ? parent : null;
        lineage = new SubAgentLineage(rootRunId, currentRunId, parentRunId, depth, siblingIndex, ledgerRef);
        failure = null!;
        return true;
    }

    private static bool TryReadSubject(
        StructuredValue value,
        out KernelSubjectRef subject,
        out ExecutionFailure failure)
    {
        subject = null!;
        if (!TryReadRequiredString(value, "sessionId", out var sessionId)
            || !TryReadRequiredString(value, "threadId", out var threadId))
        {
            failure = InvalidPayload("subagent_parent_subject_invalid", "KernelSubjectRef 缺少 sessionId 或 threadId。");
            return false;
        }

        WorkflowId? workflowId = TryReadRequiredString(value, "workflowId", out var workflow) ? new WorkflowId(workflow) : null;
        TurnId? turnId = TryReadRequiredString(value, "turnId", out var turn) ? new TurnId(turn) : null;
        subject = new KernelSubjectRef(new SessionId(sessionId), new ThreadId(threadId), workflowId, turnId);
        failure = null!;
        return true;
    }

    private static bool TryReadGovernance(
        StructuredValue value,
        out GovernanceEnvelope governance,
        out ExecutionFailure failure)
    {
        governance = null!;
        if (!TryReadRequiredString(value, "envelopeId", out var envelopeId)
            || !TryReadSideEffectLevel(value, "maxSideEffectLevel", out var sideEffectLevel))
        {
            failure = InvalidPayload("subagent_requested_governance_invalid", "GovernanceEnvelope 缺少 envelopeId 或 maxSideEffectLevel。");
            return false;
        }

        _ = TryReadStringList(value, "policyIds", out var policyIds);
        _ = TryReadStringList(value, "allowedToolIds", out var allowedToolIds);
        _ = TryReadStringList(value, "allowedModuleIds", out var allowedModuleIds);
        _ = TryReadStringList(value, "approvalIds", out var approvalIdValues);
        _ = TryReadStringList(value, "auditRecordIds", out var auditRecordIdValues);
        var requiresHumanGate = !TryReadBool(value, "requiresHumanGate", out var humanGate) || humanGate;

        governance = new GovernanceEnvelope(
            envelopeId,
            policyIds,
            allowedToolIds,
            allowedModuleIds,
            sideEffectLevel,
            requiresHumanGate,
            approvalIdValues.Select(static id => new ApprovalId(id)).ToArray(),
            auditRecordIdValues.Select(static id => new AuditRecordId(id)).ToArray());
        failure = null!;
        return true;
    }

    private static bool TryReadBudget(
        StructuredValue value,
        out KernelBudget budget,
        out ExecutionFailure failure)
    {
        budget = null!;
        if (!TryReadInt(value, "tokenBudget", out var tokenBudget)
            || !TryReadLong(value, "timeBudgetMs", out var timeBudgetMs)
            || !TryReadDecimal(value, "costBudget", out var costBudget)
            || !TryReadInt(value, "retryBudget", out var retryBudget)
            || !TryReadInt(value, "toolCallBudget", out var toolCallBudget))
        {
            failure = InvalidPayload("subagent_requested_budget_invalid", "KernelBudget 缺少必需预算字段。");
            return false;
        }

        budget = new KernelBudget(tokenBudget, timeBudgetMs, costBudget, retryBudget, toolCallBudget);
        failure = null!;
        return true;
    }

    private static bool TryReadQuota(
        ModuleCapabilityStep step,
        out SubAgentSpawnQuota quota,
        out ExecutionFailure failure)
    {
        quota = null!;
        if (!step.Metadata.TryGetValue("subAgent.quota", out var value))
        {
            quota = new SubAgentSpawnQuota(1, 8, 32, 0);
            failure = null!;
            return true;
        }

        if (value.Kind != StructuredValueKind.Object
            || !TryReadInt(value, "maxSpawnDepth", out var maxSpawnDepth)
            || !TryReadInt(value, "maxFanoutPerAgent", out var maxFanoutPerAgent)
            || !TryReadInt(value, "maxTreeNodes", out var maxTreeNodes)
            || !TryReadInt(value, "maxConcurrentAgents", out var maxConcurrentAgents))
        {
            failure = InvalidPayload("subagent_quota_invalid", "SubAgentSpawnQuota metadata 缺少必需字段。");
            return false;
        }

        quota = new SubAgentSpawnQuota(maxSpawnDepth, maxFanoutPerAgent, maxTreeNodes, maxConcurrentAgents);
        failure = null!;
        return true;
    }

    private static bool TryReadObject(StructuredValue value, string propertyName, out StructuredValue objectValue)
    {
        objectValue = null!;
        if (!value.TryGetProperty(propertyName, out var propertyValue) || propertyValue is null || propertyValue.Kind != StructuredValueKind.Object)
        {
            return false;
        }

        objectValue = propertyValue;
        return true;
    }

    private static bool TryReadRequiredString(StructuredValue value, string propertyName, out string result)
    {
        result = string.Empty;
        if (!value.TryGetProperty(propertyName, out var propertyValue) || propertyValue is null)
        {
            return false;
        }

        result = propertyValue.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(result);
    }

    private static bool TryReadStringList(StructuredValue value, string propertyName, out IReadOnlyList<string> results)
    {
        results = Array.Empty<string>();
        if (!value.TryGetProperty(propertyName, out var propertyValue) || propertyValue is null)
        {
            return false;
        }

        if (propertyValue.Kind != StructuredValueKind.Array)
        {
            return false;
        }

        results = propertyValue.Items
            .Select(static item => item.GetString())
            .Where(static text => !string.IsNullOrWhiteSpace(text))
            .Select(static text => text!)
            .ToArray();
        return true;
    }

    private static bool TryReadBool(StructuredValue value, string propertyName, out bool result)
    {
        result = false;
        if (!value.TryGetProperty(propertyName, out var propertyValue) || propertyValue is null)
        {
            return false;
        }

        if (propertyValue.Kind == StructuredValueKind.Boolean && propertyValue.BooleanValue is { } boolValue)
        {
            result = boolValue;
            return true;
        }

        return bool.TryParse(propertyValue.GetString(), out result);
    }

    private static bool TryReadInt(StructuredValue value, string propertyName, out int result)
    {
        result = 0;
        return value.TryGetProperty(propertyName, out var propertyValue)
               && propertyValue is not null
               && int.TryParse(propertyValue.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryReadLong(StructuredValue value, string propertyName, out long result)
    {
        result = 0;
        return value.TryGetProperty(propertyName, out var propertyValue)
               && propertyValue is not null
               && long.TryParse(propertyValue.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryReadDecimal(StructuredValue value, string propertyName, out decimal result)
    {
        result = 0;
        return value.TryGetProperty(propertyName, out var propertyValue)
               && propertyValue is not null
               && decimal.TryParse(propertyValue.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryReadSideEffectLevel(StructuredValue value, string propertyName, out SideEffectLevel result)
    {
        result = SideEffectLevel.Unspecified;
        if (!value.TryGetProperty(propertyName, out var propertyValue) || propertyValue is null)
        {
            return false;
        }

        var raw = propertyValue.GetString();
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            result = (SideEffectLevel)number;
            return true;
        }

        return Enum.TryParse(raw, ignoreCase: true, out result);
    }

    private static ExecutionFailure InvalidPayload(string code, string message)
        => new(code, message);
}
