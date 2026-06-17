using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Kernel.Abstractions;

namespace TianShu.Kernel.Validation;

/// <summary>
/// Kernel 默认验证器，采用 fail-closed 策略验证 intent、proposal、StageGraph、operation 和 RuntimeStep。
/// Default Kernel validator that uses fail-closed checks for intents, proposals, StageGraph, operations, and RuntimeStep.
/// </summary>
public sealed class KernelValidator : IKernelValidator
{
    public Task<KernelValidationResult> ValidateIntentAsync(CoreIntent intent, KernelValidationContext context, CancellationToken cancellationToken = default)
    {
        if (intent is null)
        {
            return RejectedAsync("kernel.intent.missing", "CoreIntent 不能为空。", "intent");
        }

        if (intent.Governance is null)
        {
            return RejectedAsync("kernel.intent.missing_governance", "CoreIntent 必须携带 GovernanceEnvelope。", "intent.governance");
        }

        if (intent.IntentKind == CoreIntentKind.Unspecified)
        {
            return RejectedAsync("kernel.intent.unspecified_kind", "CoreIntentKind 不能为 Unspecified。", "intent.intentKind");
        }

        return ApprovedAsync();
    }

    public Task<KernelValidationResult> ValidateProposalAsync(KernelProposal proposal, KernelValidationContext context, CancellationToken cancellationToken = default)
    {
        if (proposal is null)
        {
            return RejectedAsync("kernel.proposal.missing", "KernelProposal 不能为空。", "proposal");
        }

        if (proposal.ProposalKind == KernelProposalKind.Unspecified)
        {
            return RejectedAsync("kernel.proposal.unspecified_kind", "KernelProposalKind 不能为 Unspecified。", "proposal.proposalKind");
        }

        if (proposal.RiskProfile.RequiresHumanGate && context.Governance.RequiresHumanGate is false)
        {
            return RejectedAsync("kernel.proposal.human_gate_not_granted", "Proposal 需要人工 gate，但当前治理信封未授予。", "proposal.riskProfile");
        }

        return ApprovedAsync();
    }

    public Task<KernelValidationResult> ValidateGraphAsync(StageGraph graph, KernelValidationContext context, CancellationToken cancellationToken = default)
    {
        if (graph is null)
        {
            return RejectedAsync("kernel.graph.missing", "StageGraph 不能为空。", "graph");
        }

        if (graph.IntentKind != context.Intent.IntentKind)
        {
            return RejectedAsync("kernel.graph.intent_kind_mismatch", "StageGraph 的 intent kind 与当前 CoreIntent 不一致。", "graph.intentKind");
        }

        if (graph.Stages.Count == 0)
        {
            return RejectedAsync("kernel.graph.empty", "StageGraph 至少需要一个 Stage。", "graph.stages");
        }

        var stageIds = graph.Stages.Select(static stage => stage.StageId).ToArray();
        if (stageIds.Distinct().Count() != stageIds.Length)
        {
            return RejectedAsync("kernel.graph.duplicate_stage", "StageGraph 不能包含重复 StageId。", "graph.stages");
        }

        if (!stageIds.Contains(graph.EntryStageId))
        {
            return RejectedAsync("kernel.graph.entry_missing", "StageGraph 入口 Stage 必须存在。", "graph.entryStageId");
        }

        var invalidEdge = graph.Edges.FirstOrDefault(edge => !stageIds.Contains(edge.FromStageId) || !stageIds.Contains(edge.ToStageId));
        if (invalidEdge is not null)
        {
            return RejectedAsync("kernel.graph.invalid_edge", "StageEdge 必须只连接当前 graph 内的 Stage。", invalidEdge.EdgeId.Value);
        }

        var reachable = ComputeReachableStages(graph);
        if (reachable.Count != graph.Stages.Count)
        {
            return RejectedAsync("kernel.graph.unreachable_stage", "StageGraph 存在从入口不可达的 Stage。", "graph.stages");
        }

        var terminalStageIds = graph.Stages
            .Select(static stage => stage.StageId)
            .Where(stageId => graph.Edges.All(edge => edge.FromStageId != stageId))
            .ToArray();
        if (terminalStageIds.Length == 0)
        {
            return RejectedAsync("kernel.graph.missing_terminal", "StageGraph 必须至少包含一个终态 Stage。", "graph.edges");
        }

        if (HasUnboundedCycle(graph))
        {
            return RejectedAsync("kernel.graph.unbounded_cycle", "StageGraph 存在无界循环。", "graph.edges");
        }

        if (graph.Budgets.TokenBudget <= 0 && graph.Budgets.TimeBudgetMs <= 0 && graph.Budgets.ToolCallBudget <= 0)
        {
            return RejectedAsync("kernel.graph.unbounded_budget", "StageGraph 必须声明有界预算。", "graph.budgets");
        }

        var policyValidation = ValidateGraphPolicyAgainstGovernance(graph, context);
        if (!policyValidation.IsApproved)
        {
            return Task.FromResult(policyValidation);
        }

        foreach (var stage in graph.Stages)
        {
            var stageValidation = ValidateStage(graph, stage);
            if (!stageValidation.IsApproved)
            {
                return Task.FromResult(stageValidation);
            }
        }

        return ApprovedAsync();
    }

    public Task<KernelValidationResult> ValidateOperationAsync(KernelOperation operation, KernelValidationContext context, CancellationToken cancellationToken = default)
    {
        if (operation is null)
        {
            return RejectedAsync("kernel.operation.missing", "KernelOperation 不能为空。", "operation");
        }

        if (context.Stage is not null && operation.SourceStageId != context.Stage.StageId)
        {
            return RejectedAsync("kernel.operation.stage_mismatch", "KernelOperation 来源 Stage 与验证上下文不一致。", "operation.sourceStageId");
        }

        if (operation.SideEffect.Level == SideEffectLevel.Unspecified)
        {
            return RejectedAsync("kernel.operation.unspecified_side_effect", "KernelOperation 必须声明副作用等级。", "operation.sideEffect");
        }

        if (context.Stage is not null && operation.SideEffect.Level > context.Stage.SideEffectLevel)
        {
            return RejectedAsync("kernel.operation.side_effect_exceeds_stage", "KernelOperation 副作用等级超过当前 Stage 上限。", "operation.sideEffect");
        }

        if (operation is RequestCapabilityCallOperation capabilityCall
            && context.Stage is not null
            && !context.Stage.AllowedCapabilityToolIds.Contains(capabilityCall.CapabilityToolId, StringComparer.Ordinal))
        {
            return RejectedAsync("kernel.operation.capability_not_allowed", "请求的能力工具不在当前 Stage allow-list 内。", "operation.capabilityToolId");
        }

        return ApprovedAsync();
    }

    public Task<KernelValidationResult> ValidateRuntimeStepAsync(RuntimeStep step, KernelValidationContext context, CancellationToken cancellationToken = default)
    {
        if (step is null)
        {
            return RejectedAsync("kernel.runtime_step.missing", "RuntimeStep 不能为空。", "runtimeStep");
        }

        if (step.SourceIntentId != context.Intent.IntentId)
        {
            return RejectedAsync("kernel.runtime_step.intent_mismatch", "RuntimeStep 来源 intent 与当前 Kernel 上下文不一致。", "runtimeStep.sourceIntentId");
        }

        if (context.Graph is not null && step.SourceGraphId != context.Graph.GraphId)
        {
            return RejectedAsync("kernel.runtime_step.graph_mismatch", "RuntimeStep 来源 graph 与当前 Kernel 上下文不一致。", "runtimeStep.sourceGraphId");
        }

        if (step.SideEffect.Level == SideEffectLevel.Unspecified)
        {
            return RejectedAsync("kernel.runtime_step.unspecified_side_effect", "RuntimeStep 必须声明副作用等级。", "runtimeStep.sideEffect");
        }

        if (step.TracePolicy is null)
        {
            return RejectedAsync("kernel.runtime_step.missing_trace_policy", "RuntimeStep 必须携带 TracePolicy。", "runtimeStep.tracePolicy");
        }

        if (step.Permission is null)
        {
            return RejectedAsync("kernel.runtime_step.missing_permission", "RuntimeStep 必须携带 PermissionEnvelope。", "runtimeStep.permission");
        }

        if (context.Graph is not null && step.SideEffect.Level > context.Graph.Policies.MaxSideEffectLevel)
        {
            return RejectedAsync("kernel.runtime_step.side_effect_exceeds_graph", "RuntimeStep 副作用等级超过 StageGraph 上限。", "runtimeStep.sideEffect");
        }

        if (step.SideEffect.Level > context.Governance.MaxSideEffectLevel)
        {
            return RejectedAsync("kernel.runtime_step.side_effect_exceeds_governance", "RuntimeStep 副作用等级超过 GovernanceEnvelope 上限。", "runtimeStep.sideEffect");
        }

        if (step.Permission.RequiresHumanGate && context.Governance.RequiresHumanGate is false)
        {
            return RejectedAsync("kernel.runtime_step.human_gate_not_granted", "RuntimeStep 需要人工 gate，但当前治理信封未授予。", "runtimeStep.permission");
        }

        if (step.Permission.RequiresHumanGate && context.Governance.ApprovalIds.Count == 0)
        {
            return RejectedAsync("kernel.runtime_step.missing_approval", "RuntimeStep 需要人工 gate，但 GovernanceEnvelope 缺少审批引用。", "intent.governance.approvalIds");
        }

        return ApprovedAsync();
    }

    public Task<KernelValidationResult> ValidateStrategyTransitionAsync(
        StrategyRecord strategy,
        StrategyLifecycleState targetState,
        IReadOnlyList<StrategyTransitionEvidence> evidence,
        KernelValidationContext context,
        CancellationToken cancellationToken = default)
    {
        if (strategy is null)
        {
            return RejectedAsync("kernel.strategy.missing", "StrategyRecord 不能为空。", "strategy");
        }

        if (targetState == StrategyLifecycleState.Unspecified)
        {
            return RejectedAsync("kernel.strategy.unspecified_target", "Strategy lifecycle target 不能为 Unspecified。", "strategy.targetState");
        }

        if (!IsAllowedStrategyTransition(strategy.LifecycleState, targetState))
        {
            return RejectedAsync("kernel.strategy.illegal_transition", $"Strategy 不能从 {strategy.LifecycleState} 转换到 {targetState}。", "strategy.targetState");
        }

        if (targetState is StrategyLifecycleState.Candidate or StrategyLifecycleState.Validated or StrategyLifecycleState.Trial or StrategyLifecycleState.Promoted or StrategyLifecycleState.Deprecated or StrategyLifecycleState.RolledBack
            && (evidence is null || evidence.Count == 0))
        {
            return RejectedAsync("kernel.strategy.missing_evidence", "Strategy lifecycle transition 必须包含 trace 或 evaluation evidence。", "strategy.transitionEvidence");
        }

        if (targetState == StrategyLifecycleState.Promoted && evidence!.Any(static item => item.MetricRefs.Count == 0))
        {
            return RejectedAsync("kernel.strategy.missing_evaluation", "Strategy 晋升必须包含 evaluation metric evidence。", "strategy.transitionEvidence.metricRefs");
        }

        if (targetState == StrategyLifecycleState.Promoted
            && context.Governance.RequiresHumanGate
            && evidence.All(static item => !item.HumanApproved))
        {
            return RejectedAsync("kernel.strategy.missing_human_gate", "高风险 Strategy 晋升必须经过人工 gate。", "strategy.transitionEvidence");
        }

        return ApprovedAsync();
    }

    private static bool IsAllowedStrategyTransition(StrategyLifecycleState current, StrategyLifecycleState target)
        => (current, target) switch
        {
            (StrategyLifecycleState.Draft, StrategyLifecycleState.Candidate) => true,
            (StrategyLifecycleState.Draft, StrategyLifecycleState.Validated) => true,
            (StrategyLifecycleState.Validated, StrategyLifecycleState.Candidate) => true,
            (StrategyLifecycleState.Candidate, StrategyLifecycleState.Trial) => true,
            (StrategyLifecycleState.Validated, StrategyLifecycleState.Trial) => true,
            (StrategyLifecycleState.Trial, StrategyLifecycleState.Promoted) => true,
            (StrategyLifecycleState.Promoted, StrategyLifecycleState.Deprecated) => true,
            (_, StrategyLifecycleState.RolledBack) when current is not StrategyLifecycleState.Unspecified => true,
            _ => false,
        };

    private static KernelValidationResult ValidateStage(StageGraph graph, StageNode stage)
    {
        if (stage.SideEffectLevel == SideEffectLevel.Unspecified)
        {
            return KernelValidationResults.Rejected("kernel.stage.unspecified_side_effect", "Stage 必须声明副作用等级。", stage.StageId.Value);
        }

        if (stage.SideEffectLevel > graph.Policies.MaxSideEffectLevel)
        {
            return KernelValidationResults.Rejected("kernel.stage.side_effect_exceeds_graph", "Stage 副作用等级超过 graph policy 上限。", stage.StageId.Value);
        }

        if (stage.Budget.TokenBudget <= 0 && stage.Budget.TimeBudgetMs <= 0 && stage.Budget.ToolCallBudget <= 0)
        {
            return KernelValidationResults.Rejected("kernel.stage.unbounded_budget", "Stage 必须声明有界预算。", stage.StageId.Value);
        }

        var modelRouteValidation = ValidateModelRoutePolicy(stage);
        if (!modelRouteValidation.IsApproved)
        {
            return modelRouteValidation;
        }

        var contextPolicyValidation = ValidateContextPolicy(stage);
        if (!contextPolicyValidation.IsApproved)
        {
            return contextPolicyValidation;
        }

        var kernelTool = stage.AllowedKernelToolIds.FirstOrDefault(toolId => !graph.Policies.AllowedKernelToolIds.Contains(toolId, StringComparer.Ordinal));
        if (kernelTool is not null)
        {
            return KernelValidationResults.Rejected("kernel.stage.kernel_tool_not_allowed", "Stage 包含 graph policy 未允许的 KernelTool。", kernelTool);
        }

        var capabilityTool = stage.AllowedCapabilityToolIds.FirstOrDefault(toolId => !graph.Policies.AllowedCapabilityToolIds.Contains(toolId, StringComparer.Ordinal));
        if (capabilityTool is not null)
        {
            return KernelValidationResults.Rejected("kernel.stage.capability_tool_not_allowed", "Stage 包含 graph policy 未允许的 CapabilityTool。", capabilityTool);
        }

        return KernelValidationResults.Approved();
    }

    private static KernelValidationResult ValidateGraphPolicyAgainstGovernance(StageGraph graph, KernelValidationContext context)
    {
        if (context.Governance.MaxSideEffectLevel == SideEffectLevel.Unspecified)
        {
            return KernelValidationResults.Rejected(
                "kernel.governance.unspecified_side_effect_ceiling",
                "GovernanceEnvelope 必须声明副作用上限。",
                "intent.governance.maxSideEffectLevel");
        }

        if (graph.Policies.MaxSideEffectLevel > context.Governance.MaxSideEffectLevel)
        {
            return KernelValidationResults.Rejected(
                "kernel.graph.side_effect_exceeds_governance",
                "StageGraph 副作用上限超过 GovernanceEnvelope。",
                "graph.policies.maxSideEffectLevel");
        }

        var missingPolicyId = graph.Policies.RequiredPolicyIds.FirstOrDefault(policyId => !context.Governance.PolicyIds.Contains(policyId, StringComparer.Ordinal));
        if (missingPolicyId is not null)
        {
            return KernelValidationResults.Rejected(
                "kernel.graph.policy_not_in_governance",
                "StageGraph 要求的 policy 不在 GovernanceEnvelope 内。",
                missingPolicyId);
        }

        var missingKernelToolId = graph.Policies.AllowedKernelToolIds.FirstOrDefault(toolId => !context.Governance.AllowedToolIds.Contains(toolId, StringComparer.Ordinal));
        if (missingKernelToolId is not null)
        {
            return KernelValidationResults.Rejected(
                "kernel.graph.kernel_tool_not_in_governance",
                "StageGraph 允许的 KernelTool 超出 GovernanceEnvelope。",
                missingKernelToolId);
        }

        var missingCapabilityToolId = graph.Policies.AllowedCapabilityToolIds.FirstOrDefault(toolId => !context.Governance.AllowedToolIds.Contains(toolId, StringComparer.Ordinal));
        if (missingCapabilityToolId is not null)
        {
            return KernelValidationResults.Rejected(
                "kernel.graph.capability_tool_not_in_governance",
                "StageGraph 允许的 CapabilityTool 超出 GovernanceEnvelope。",
                missingCapabilityToolId);
        }

        var missingModuleId = graph.Policies.AllowedModuleIds.FirstOrDefault(moduleId => !context.Governance.AllowedModuleIds.Contains(moduleId, StringComparer.Ordinal));
        if (missingModuleId is not null)
        {
            return KernelValidationResults.Rejected(
                "kernel.graph.module_not_in_governance",
                "StageGraph 允许的 Module 超出 GovernanceEnvelope。",
                missingModuleId);
        }

        if (graph.Policies.RequiresHumanGate && context.Governance.RequiresHumanGate is false)
        {
            return KernelValidationResults.Rejected(
                "kernel.graph.human_gate_not_granted",
                "StageGraph 需要人工 gate，但 GovernanceEnvelope 未授予。",
                "graph.policies.requiresHumanGate");
        }

        return KernelValidationResults.Approved();
    }

    private static KernelValidationResult ValidateModelRoutePolicy(StageNode stage)
    {
        var policy = stage.ModelRoutePolicy;
        if (policy.FailClosedWhenMissingCandidate
            && policy.RouteCandidateIds.Count == 0
            && policy.Candidates.Count == 0)
        {
            return KernelValidationResults.Rejected(
                "kernel.stage.model_route_missing_candidate",
                "ModelRoutePolicy fail-closed 时必须声明至少一个 route candidate。",
                stage.StageId.Value);
        }

        if (!string.IsNullOrWhiteSpace(policy.PreferredRouteId)
            && policy.Candidates.Count > 0
            && policy.Candidates.All(candidate => !string.Equals(candidate.CandidateId, policy.PreferredRouteId, StringComparison.Ordinal)))
        {
            return KernelValidationResults.Rejected(
                "kernel.stage.model_route_preferred_candidate_missing",
                "ModelRoutePolicy 首选 candidate 必须存在于已批准候选列表。",
                policy.PreferredRouteId);
        }

        return KernelValidationResults.Approved();
    }

    private static KernelValidationResult ValidateContextPolicy(StageNode stage)
    {
        if (stage.ContextPolicy.FailClosed && stage.ContextPolicy.MaxInputTokens <= 0)
        {
            return KernelValidationResults.Rejected(
                "kernel.stage.context_policy_missing_budget",
                "ContextPolicy fail-closed 时必须声明正数上下文预算。",
                stage.StageId.Value);
        }

        foreach (var sourceKind in stage.ContextPolicy.AllowedSourceKinds)
        {
            if (!Enum.TryParse<ContextSourceKind>(sourceKind, ignoreCase: true, out var parsed)
                || parsed is ContextSourceKind.Unspecified)
            {
                return KernelValidationResults.Rejected(
                    "kernel.stage.context_policy_unspecified_source",
                    "ContextPolicy allowed source kind 必须是正式来源类别，且不能为 Unspecified。",
                    sourceKind);
            }
        }

        return KernelValidationResults.Approved();
    }

    private static HashSet<StageId> ComputeReachableStages(StageGraph graph)
    {
        var reachable = new HashSet<StageId> { graph.EntryStageId };
        var queue = new Queue<StageId>();
        queue.Enqueue(graph.EntryStageId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var edge in graph.Edges.Where(edge => edge.FromStageId == current))
            {
                if (reachable.Add(edge.ToStageId))
                {
                    queue.Enqueue(edge.ToStageId);
                }
            }
        }

        return reachable;
    }

    private static bool HasUnboundedCycle(StageGraph graph)
        => HasCycle(graph) && graph.Budgets.ToolCallBudget <= 0;

    private static bool HasCycle(StageGraph graph)
    {
        var visiting = new HashSet<StageId>();
        var visited = new HashSet<StageId>();

        bool Visit(StageId stageId)
        {
            if (visiting.Contains(stageId))
            {
                return true;
            }

            if (!visited.Add(stageId))
            {
                return false;
            }

            visiting.Add(stageId);
            foreach (var edge in graph.Edges.Where(edge => edge.FromStageId == stageId))
            {
                if (Visit(edge.ToStageId))
                {
                    return true;
                }
            }

            visiting.Remove(stageId);
            return false;
        }

        return Visit(graph.EntryStageId);
    }

    private static Task<KernelValidationResult> ApprovedAsync()
        => Task.FromResult(KernelValidationResults.Approved());

    private static Task<KernelValidationResult> RejectedAsync(string code, string message, string? sourceRef)
        => Task.FromResult(KernelValidationResults.Rejected(code, message, sourceRef));
}
