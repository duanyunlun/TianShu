using TianShu.Contracts.Agents;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Modules;
using TianShu.Contracts.Primitives;
using TianShu.Kernel.Abstractions;
using TianShu.RuntimeComposition;

namespace TianShu.SubAgent;

/// <summary>
/// 默认 Sub-Agent 编排模块：派生受限子 turn，并复用 Kernel/Runtime 组合循环执行。
/// Default Sub-Agent orchestration module: derives a narrowed child turn and runs it through the Kernel/Runtime composition loop.
/// </summary>
public sealed class SubAgentOrchestrationModule : ISubAgentModule
{
    private readonly IKernelRuntimeExecutionLoop executionLoop;

    public SubAgentOrchestrationModule(IKernelRuntimeExecutionLoop executionLoop)
    {
        this.executionLoop = executionLoop ?? throw new ArgumentNullException(nameof(executionLoop));
    }

    public ModuleDescriptor Descriptor { get; } = BuiltInModuleDescriptors.SubAgent();

    public ValueTask<ModuleSmokeCheckResult> CheckAsync(CancellationToken cancellationToken)
        => ValueTask.FromResult(new ModuleSmokeCheckResult(Descriptor.ModuleId, true, ModuleHealthStatus.Healthy));

    public async ValueTask<SubAgentRunResult> SpawnAsync(
        SubAgentSpawnRequest request,
        SubAgentLineage childLineage,
        SubAgentSpawnQuota quota,
        SubAgentModuleInvocationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(childLineage);
        ArgumentNullException.ThrowIfNull(quota);
        ArgumentNullException.ThrowIfNull(context);

        var lineageFailure = ValidateLineage(request.ParentLineage, childLineage, quota);
        if (lineageFailure is not null)
        {
            return CreateBlockedResult(request, childLineage, lineageFailure);
        }

        var childGovernance = SubAgentGovernanceNarrowing.Narrow(
            context.Governance,
            request.RequestedGovernance,
            childLineage.CurrentRunId,
            request.RequiresHumanGate);
        var childSubject = CreateChildSubject(request.ParentSubject, childLineage);
        var childIntent = CreateChildIntent(request, childLineage, childSubject, childGovernance);
        var runId = new KernelRunId(childLineage.CurrentRunId);
        var result = await executionLoop.RunReactiveAsync(
                childIntent,
                new KernelRuntimeExecutionOptions(
                    new KernelRunOptions(
                        runId,
                        preferredGraphId: new StageGraphId("graph.turn.default"),
                        budgetOverride: request.RequestedBudget,
                        enableAdaptive: false,
                        requireHumanGate: childGovernance.RequiresHumanGate,
                        metadata: CreateKernelOptionsMetadata(request, childLineage)),
                    new ExecutionRuntimeContext(
                        new ExecutionId($"execution-{childLineage.CurrentRunId}"),
                        runId,
                        childGovernance,
                        context.WorkingDirectory,
                        CreateRuntimeContextMetadata(request, childLineage, childGovernance, context)),
                    ExecuteRuntimePlan: true),
                cancellationToken)
            .ConfigureAwait(false);

        return new SubAgentRunResult(
            request.SpawnCallId,
            childLineage.CurrentRunId,
            childSubject.ThreadId.Value,
            MapStatus(result.Disposition),
            ResolveAssistantText(result),
            result.RuntimeTraceRef ?? result.KernelTraceId?.Value,
            ResolveDiagnosticsRefs(result),
            ResolveFailure(result),
            ResolveArtifactRefs(result));
    }

    private static ExecutionFailure? ValidateLineage(
        SubAgentLineage parent,
        SubAgentLineage child,
        SubAgentSpawnQuota quota)
    {
        if (!string.Equals(child.RootRunId, parent.RootRunId, StringComparison.Ordinal)
            || !string.Equals(child.ParentRunId, parent.CurrentRunId, StringComparison.Ordinal)
            || !string.Equals(child.LedgerRef, parent.LedgerRef, StringComparison.Ordinal)
            || child.Depth != parent.Depth + 1
            || child.Depth > quota.MaxSpawnDepth)
        {
            return new ExecutionFailure("subagent_child_lineage_invalid", "childLineage 与 parentLineage 或结构配额不匹配。");
        }

        return null;
    }

    private static KernelSubjectRef CreateChildSubject(KernelSubjectRef parentSubject, SubAgentLineage childLineage)
        => new(
            parentSubject.SessionId,
            new ThreadId($"thread-{childLineage.CurrentRunId}"),
            parentSubject.WorkflowId,
            new TurnId($"turn-{childLineage.CurrentRunId}"));

    private static TurnIntent CreateChildIntent(
        SubAgentSpawnRequest request,
        SubAgentLineage childLineage,
        KernelSubjectRef childSubject,
        GovernanceEnvelope childGovernance)
        => new(
            new CoreIntentId($"intent-{childLineage.CurrentRunId}"),
            childSubject,
            childGovernance,
            $"subagent://{request.SpawnCallId}/task",
            request.RequestedBudget,
            CreateIntentMetadata(request, childLineage));

    private static MetadataBag CreateIntentMetadata(SubAgentSpawnRequest request, SubAgentLineage childLineage)
        => new(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["message"] = StructuredValue.FromString(request.TaskBrief),
            ["subAgent.spawnCallId"] = StructuredValue.FromString(request.SpawnCallId),
            ["subAgent.parentRunId"] = StructuredValue.FromString(request.ParentLineage.CurrentRunId),
            ["subAgent.childRunId"] = StructuredValue.FromString(childLineage.CurrentRunId),
            ["subAgent.evidenceRefs"] = StructuredValue.FromPlainObject(request.EvidenceRefs),
        });

    private static MetadataBag CreateKernelOptionsMetadata(SubAgentSpawnRequest request, SubAgentLineage childLineage)
        => new(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["subAgent.spawnCallId"] = StructuredValue.FromString(request.SpawnCallId),
            ["subAgent.parentRunId"] = StructuredValue.FromString(request.ParentLineage.CurrentRunId),
            ["subAgent.childRunId"] = StructuredValue.FromString(childLineage.CurrentRunId),
        });

    private static MetadataBag CreateRuntimeContextMetadata(
        SubAgentSpawnRequest request,
        SubAgentLineage childLineage,
        GovernanceEnvelope childGovernance,
        SubAgentModuleInvocationContext parentContext)
    {
        var entries = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["subAgent.spawnCallId"] = StructuredValue.FromString(request.SpawnCallId),
            ["subAgent.parentRunId"] = StructuredValue.FromString(request.ParentLineage.CurrentRunId),
            ["subAgent.childRunId"] = StructuredValue.FromString(childLineage.CurrentRunId),
            ["subAgent.parentExecutionId"] = StructuredValue.FromString(parentContext.ExecutionId),
            ["subAgent.parentRuntimeStepId"] = StructuredValue.FromString(parentContext.RuntimeStepId),
            ["subAgent.parentGovernance"] = StructuredValue.FromPlainObject(CreateGovernancePlainObject(parentContext.Governance)),
            ["subAgent.childGovernance"] = StructuredValue.FromPlainObject(CreateGovernancePlainObject(childGovernance)),
            ["subAgent.childBudget"] = StructuredValue.FromPlainObject(CreateBudgetPlainObject(request.RequestedBudget)),
            ["subAgent.parentPermissionRequiresHumanGate"] = StructuredValue.FromBoolean(parentContext.Permission.RequiresHumanGate),
            ["subAgent.parentSideEffectLevel"] = StructuredValue.FromString(parentContext.SideEffect.Level.ToString()),
        };

        CopyParentMetadata(parentContext.Metadata, entries, "subAgent.parentTracePolicy");
        CopyParentMetadata(parentContext.Metadata, entries, "subAgent.parentRuntimeStepBudget");
        CopyParentMetadata(parentContext.Metadata, entries, "subAgent.parentPermission");
        CopyParentMetadata(parentContext.Metadata, entries, "subAgent.parentSideEffect");
        if (!string.IsNullOrWhiteSpace(parentContext.WorkingDirectory))
        {
            entries["subAgent.parentWorkingDirectory"] = StructuredValue.FromString(parentContext.WorkingDirectory!);
        }

        return new MetadataBag(entries);
    }

    private static void CopyParentMetadata(MetadataBag metadata, IDictionary<string, StructuredValue> target, string key)
    {
        if (metadata.TryGetValue(key, out var value))
        {
            target[key] = value;
        }
    }

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

    private static Dictionary<string, object?> CreateBudgetPlainObject(KernelBudget budget)
        => new(StringComparer.Ordinal)
        {
            ["tokenBudget"] = budget.TokenBudget,
            ["timeBudgetMs"] = budget.TimeBudgetMs,
            ["costBudget"] = budget.CostBudget,
            ["retryBudget"] = budget.RetryBudget,
            ["toolCallBudget"] = budget.ToolCallBudget,
        };

    private static SubAgentRunResult CreateBlockedResult(
        SubAgentSpawnRequest request,
        SubAgentLineage childLineage,
        ExecutionFailure failure)
        => new(
            request.SpawnCallId,
            childLineage.CurrentRunId,
            $"thread-{childLineage.CurrentRunId}",
            SubAgentRunStatus.Blocked,
            failure: new SubAgentFailure(failure.Code, failure.Message));

    private static SubAgentRunStatus MapStatus(KernelRuntimeExecutionDisposition disposition)
        => disposition switch
        {
            KernelRuntimeExecutionDisposition.RuntimeCompleted => SubAgentRunStatus.Completed,
            KernelRuntimeExecutionDisposition.RuntimeBlocked or KernelRuntimeExecutionDisposition.KernelRejected or KernelRuntimeExecutionDisposition.ApprovalOnly => SubAgentRunStatus.Blocked,
            KernelRuntimeExecutionDisposition.RuntimeFailed or KernelRuntimeExecutionDisposition.Unspecified => SubAgentRunStatus.Failed,
            _ => SubAgentRunStatus.Failed,
        };

    private static string? ResolveAssistantText(KernelRuntimeExecutionResult result)
        => result.RuntimeResult?.StepResults
            .Select(static step => step.Output)
            .Where(static output => output is not null && output.TryGetProperty("assistantText", out _))
            .Select(static output => output!.GetProperty("assistantText").GetString())
            .LastOrDefault(static text => !string.IsNullOrWhiteSpace(text));

    private static IReadOnlyList<string> ResolveDiagnosticsRefs(KernelRuntimeExecutionResult result)
        => new[] { result.DiagnosticsRef }
            .Concat(result.RuntimeResult?.StepResults.Select(static step => step.DiagnosticsRef) ?? Array.Empty<string?>())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ResolveArtifactRefs(KernelRuntimeExecutionResult result)
        => result.RuntimeResult?.StepResults
            .SelectMany(static step => ReadStringArray(step.Output, "artifactRefs").Concat(ReadStringArray(step.Output, "artifacts")))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray()
           ?? Array.Empty<string>();

    private static IReadOnlyList<string> ReadStringArray(StructuredValue? output, string propertyName)
    {
        if (output is null
            || !output.TryGetProperty(propertyName, out var value)
            || value is not { Kind: StructuredValueKind.Array })
        {
            return Array.Empty<string>();
        }

        return value.Items
            .Select(static item => item.Kind is StructuredValueKind.String or StructuredValueKind.Number ? item.GetString() : null)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item!)
            .ToArray();
    }

    private static SubAgentFailure? ResolveFailure(KernelRuntimeExecutionResult result)
    {
        if (result.Disposition == KernelRuntimeExecutionDisposition.RuntimeCompleted)
        {
            return null;
        }

        var failure = result.RuntimeResult?.StepResults
            .Select(static step => step.Failure)
            .FirstOrDefault(static failure => failure is not null);
        return failure is not null
            ? new SubAgentFailure(failure.Code, failure.Message)
            : new SubAgentFailure("subagent_run_not_completed", $"Sub-Agent run ended with disposition {result.Disposition}.");
    }
}
