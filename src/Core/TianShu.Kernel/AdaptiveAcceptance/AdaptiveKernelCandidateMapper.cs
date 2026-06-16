using System.Text.Json;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;

namespace TianShu.Kernel.AdaptiveAcceptance;

/// <summary>
/// 自适应内核验收候选映射器，只把 LLM JSON 候选转换为正式 Kernel contract。
/// Adaptive-kernel acceptance mapper that only converts LLM JSON candidates into formal Kernel contracts.
/// </summary>
public static class AdaptiveKernelCandidateMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static StageGraph MapStageGraph(AdaptiveKernelStageGraphCandidate candidate, string source = "adaptive-kernel-candidate")
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var graphId = Required(candidate.GraphId, nameof(candidate.GraphId));
        var version = Required(candidate.Version, nameof(candidate.Version));
        var intentKind = ParseEnum<CoreIntentKind>(candidate.IntentKind, nameof(candidate.IntentKind));
        var entryStageId = new StageId(Required(candidate.EntryStageId, nameof(candidate.EntryStageId)));
        var stages = RequiredArray(candidate.Stages, nameof(candidate.Stages)).Select(MapStage).ToArray();
        var edges = RequiredArray(candidate.Edges, nameof(candidate.Edges))
            .Select(MapEdge)
            .ToArray();

        return new StageGraph(
            new StageGraphId(graphId),
            version,
            intentKind,
            entryStageId,
            stages,
            edges,
            MapPolicies(candidate.Policies, stages),
            MapBudget(candidate.Budgets, nameof(candidate.Budgets)),
            MapCheckpointRules(candidate.CheckpointRules, stages),
            MapRecoveryRules(candidate.RecoveryRules),
            MapEvaluationRules(candidate.EvaluationRules),
            new StageGraphMetadata("adaptive-kernel-acceptance", source));
    }

    public static StageGraphPatchProposal MapPatchProposal(AdaptiveKernelStageGraphPatchCandidate candidate, CoreIntentId sourceIntentId)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var targetGraphId = new StageGraphId(Required(candidate.TargetGraphId, nameof(candidate.TargetGraphId)));
        var proposalId = string.IsNullOrWhiteSpace(candidate.ProposalId)
            ? $"proposal.patch.{targetGraphId.Value}"
            : candidate.ProposalId.Trim();
        var operations = RequiredArray(candidate.Operations, nameof(candidate.Operations))
            .Select(static operation => new StageGraphPatchOperation(
                Required(operation.OperationKind, nameof(operation.OperationKind)),
                string.IsNullOrWhiteSpace(operation.TargetStageId) ? null : new StageId(operation.TargetStageId.Trim()),
                operation.Payload is { } payload ? StructuredValue.FromJsonElement(payload) : StructuredValue.Null))
            .ToArray();

        return new StageGraphPatchProposal(
            new KernelProposalId(proposalId),
            sourceIntentId,
            targetGraphId,
            operations,
            new RiskProfile("acceptance-candidate", requiresHumanGate: true),
            new KernelBudgetImpact(reason: "acceptance patch candidate"),
            new RollbackPlan("rollback.acceptance.patch", reversible: true),
            new EvaluationPlan("evaluation.acceptance.patch", ["acceptance.patch.validation"]));
    }

    public static StageGraph ApplyPatch(StageGraph baseGraph, AdaptiveKernelStageGraphPatchCandidate patch)
    {
        ArgumentNullException.ThrowIfNull(baseGraph);
        ArgumentNullException.ThrowIfNull(patch);

        var targetGraphId = Required(patch.TargetGraphId, nameof(patch.TargetGraphId));
        if (!StringComparer.Ordinal.Equals(baseGraph.GraphId.Value, targetGraphId))
        {
            throw new InvalidDataException($"Patch targetGraphId '{targetGraphId}' does not match base graph '{baseGraph.GraphId.Value}'.");
        }

        var stages = baseGraph.Stages.ToDictionary(static stage => stage.StageId.Value, StringComparer.Ordinal);
        var edges = baseGraph.Edges.ToDictionary(static edge => edge.EdgeId.Value, StringComparer.Ordinal);
        var entryStageId = baseGraph.EntryStageId;
        var policies = baseGraph.Policies;
        var budgets = baseGraph.Budgets;

        foreach (var operation in RequiredArray(patch.Operations, nameof(patch.Operations)))
        {
            var operationKind = Required(operation.OperationKind, nameof(operation.OperationKind));
            switch (operationKind)
            {
                case "add_stage":
                case "replace_stage":
                    var stage = MapStage(ReadPayload<AdaptiveKernelStageCandidate>(operation.Payload, operationKind));
                    stages[stage.StageId.Value] = stage;
                    break;
                case "remove_stage":
                    stages.Remove(Required(operation.TargetStageId, nameof(operation.TargetStageId)));
                    break;
                case "add_edge":
                case "replace_edge":
                    var edge = MapEdge(ReadPayload<AdaptiveKernelEdgeCandidate>(operation.Payload, operationKind));
                    edges[edge.EdgeId.Value] = edge;
                    break;
                case "remove_edge":
                    edges.Remove(Required(operation.TargetEdgeId, nameof(operation.TargetEdgeId)));
                    break;
                case "set_entry_stage":
                    entryStageId = new StageId(Required(operation.TargetStageId, nameof(operation.TargetStageId)));
                    break;
                case "replace_policies":
                    policies = MapPolicies(ReadPayload<AdaptiveKernelPolicyCandidate>(operation.Payload, operationKind), stages.Values.ToArray());
                    break;
                case "replace_budgets":
                    budgets = MapBudget(ReadPayload<AdaptiveKernelBudgetCandidate>(operation.Payload, operationKind), operationKind);
                    break;
                default:
                    throw new InvalidDataException($"Unsupported patch operation kind: {operationKind}");
            }
        }

        return new StageGraph(
            baseGraph.GraphId,
            baseGraph.Version,
            baseGraph.IntentKind,
            entryStageId,
            stages.Values.ToArray(),
            edges.Values.ToArray(),
            policies,
            budgets,
            baseGraph.CheckpointRules,
            baseGraph.RecoveryRules,
            baseGraph.EvaluationRules,
            new StageGraphMetadata(baseGraph.Metadata.Owner, "adaptive-kernel-patch-apply"));
    }

    public static RecoveryProposal MapRecoveryProposal(AdaptiveKernelRecoveryProposalCandidate candidate, CoreIntentId sourceIntentId)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var proposalId = string.IsNullOrWhiteSpace(candidate.ProposalId)
            ? "proposal.recovery.acceptance"
            : candidate.ProposalId.Trim();

        return new RecoveryProposal(
            new KernelProposalId(proposalId),
            sourceIntentId,
            new RecoveryPlan(
                Required(candidate.RecoveryKind, nameof(candidate.RecoveryKind)),
                candidate.ActionRefs,
                candidate.RequiresHumanGate),
            new RiskProfile("acceptance-recovery", requiresHumanGate: true),
            new KernelBudgetImpact(reason: "acceptance recovery candidate"),
            new RollbackPlan("rollback.acceptance.recovery", reversible: false),
            new EvaluationPlan("evaluation.acceptance.recovery", ["acceptance.recovery.validation"]));
    }

    public static CheckpointProposalOperation MapCheckpointProposal(AdaptiveKernelCheckpointProposalCandidate candidate, CoreIntentId sourceIntentId)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        return new CheckpointProposalOperation(
            new KernelOperationId(Required(candidate.OperationId, nameof(candidate.OperationId))),
            sourceIntentId,
            new StageId(Required(candidate.SourceStageId, nameof(candidate.SourceStageId))),
            Required(candidate.CheckpointRef, nameof(candidate.CheckpointRef)),
            new PermissionEnvelope(requiresHumanGate: false, reason: "Acceptance checkpoint proposal."),
            new SideEffectProfile(SideEffectLevel.None, requiresAudit: true));
    }

    public static ContextPolicy MapContextPolicy(AdaptiveKernelContextPolicyCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return new ContextPolicy(
            maxInputTokens: candidate.MaxInputTokens,
            allowedSourceKinds: candidate.AllowedSourceKinds,
            preserveLatestUserCorrection: candidate.PreserveLatestUserCorrection,
            requireEvidenceRefs: candidate.RequireEvidenceRefs,
            policyId: candidate.PolicyId,
            failClosed: candidate.FailClosed);
    }

    private static StageNode MapStage(AdaptiveKernelStageCandidate stage)
    {
        var stageId = Required(stage.StageId, nameof(stage.StageId));
        var kind = Required(stage.Kind, nameof(stage.Kind));
        var budget = MapBudget(stage.Budget, nameof(stage.Budget));
        var capabilityToolIds = RequiredArray(stage.CapabilityToolIds, nameof(stage.CapabilityToolIds));
        return new StageNode(
            new StageId(stageId),
            kind,
            Required(stage.Objective, nameof(stage.Objective)),
            new ContractRef($"contract.{kind}.input", "1"),
            new ContractRef($"contract.{kind}.output", "1"),
            stage.AllowedKernelToolIds,
            capabilityToolIds,
            MapModelRoutePolicy(stage.ModelRoutePolicy),
            stage.ContextPolicy is null
                ? new ContextPolicy(maxInputTokens: Math.Max(1, budget.TokenBudget), allowedSourceKinds: ["CurrentUserInput", "ConversationHistory", "ToolEvidence"])
                : MapContextPolicy(stage.ContextPolicy),
            ParseEnum<SideEffectLevel>(stage.SideEffectLevel, nameof(stage.SideEffectLevel)),
            budget,
            new SuccessCriteria([$"{kind}.completed"]),
            new FailureHandlerRef($"handler.{kind}.recover", mayRecover: true));
    }

    private static StageEdge MapEdge(AdaptiveKernelEdgeCandidate edge)
    {
        var from = Required(edge.FromStageId, nameof(edge.FromStageId));
        var to = Required(edge.ToStageId, nameof(edge.ToStageId));
        var edgeId = string.IsNullOrWhiteSpace(edge.EdgeId)
            ? $"edge-{from}-{to}"
            : edge.EdgeId.Trim();
        return new StageEdge(
            new StageEdgeId(edgeId),
            new StageId(from),
            new StageId(to),
            new TransitionCondition(string.IsNullOrWhiteSpace(edge.ConditionKind) ? "on_success" : edge.ConditionKind.Trim()),
            new TransitionGuard(edge.RequiredSignals),
            ParseEnum<StageTransitionKind>(edge.TransitionKind, nameof(edge.TransitionKind)));
    }

    private static GraphPolicySet MapPolicies(AdaptiveKernelPolicyCandidate? policies, IReadOnlyList<StageNode> stages)
    {
        if (policies is null)
        {
            return new GraphPolicySet(
                PolicyEnforcementMode.AllowListed,
                allowedCapabilityToolIds: stages.SelectMany(static stage => stage.AllowedCapabilityToolIds).Distinct(StringComparer.Ordinal).ToArray(),
                maxSideEffectLevel: (SideEffectLevel)stages.Select(static stage => (int)stage.SideEffectLevel).DefaultIfEmpty((int)SideEffectLevel.None).Max(),
                requiresHumanGate: false);
        }

        return new GraphPolicySet(
            PolicyEnforcementMode.AllowListed,
            requiredPolicyIds: policies.RequiredPolicyIds,
            allowedKernelToolIds: policies.AllowedKernelToolIds,
            allowedCapabilityToolIds: policies.AllowedCapabilityToolIds,
            allowedModuleIds: policies.AllowedModuleIds,
            maxSideEffectLevel: ParseEnum<SideEffectLevel>(policies.MaxSideEffectLevel, nameof(policies.MaxSideEffectLevel)),
            requiresHumanGate: policies.RequiresHumanGate);
    }

    private static KernelBudget MapBudget(AdaptiveKernelBudgetCandidate? budget, string fieldName)
    {
        if (budget is null)
        {
            throw new InvalidDataException($"Candidate missing {fieldName}.");
        }

        return new KernelBudget(
            tokenBudget: budget.TokenBudget,
            timeBudgetMs: budget.TimeBudgetMs,
            costBudget: budget.CostBudget,
            retryBudget: budget.RetryBudget,
            toolCallBudget: budget.ToolCallBudget);
    }

    private static ModelRoutePolicy MapModelRoutePolicy(AdaptiveKernelModelRoutePolicyCandidate? policy)
        => policy is null
            ? new ModelRoutePolicy(routeCandidateIds: ["route.acceptance"], preferredRouteId: "route.acceptance")
            : new ModelRoutePolicy(
                routeCandidateIds: policy.RouteCandidateIds,
                preferredRouteId: policy.PreferredRouteId,
                fallbackRouteId: policy.FallbackRouteId,
                routeKind: policy.RouteKind);

    private static CheckpointRules MapCheckpointRules(AdaptiveKernelCheckpointRulesCandidate? candidate, IReadOnlyList<StageNode> stages)
        => candidate is null
            ? new CheckpointRules(enabled: true, requiredStageIds: stages.Select(static stage => stage.StageId).ToArray())
            : new CheckpointRules(
                candidate.Enabled,
                candidate.RequiredStageIds?.Select(static stageId => new StageId(stageId)).ToArray());

    private static RecoveryRules MapRecoveryRules(AdaptiveKernelRecoveryRulesCandidate? candidate)
        => candidate is null
            ? new RecoveryRules(enabled: true, maxRecoveryAttempts: 1)
            : new RecoveryRules(candidate.Enabled, candidate.MaxRecoveryAttempts);

    private static EvaluationRules MapEvaluationRules(AdaptiveKernelEvaluationRulesCandidate? candidate)
        => candidate is null
            ? new EvaluationRules(enabled: true, metricIds: ["acceptance.candidate.validation"])
            : new EvaluationRules(candidate.Enabled, candidate.MetricIds);

    private static T ReadPayload<T>(JsonElement? payload, string operationKind)
    {
        if (payload is null)
        {
            throw new InvalidDataException($"Patch operation {operationKind} missing payload.");
        }

        return payload.Value.Deserialize<T>(JsonOptions)
            ?? throw new InvalidDataException($"Patch operation {operationKind} payload cannot deserialize to {typeof(T).Name}.");
    }

    private static string Required(string? value, string fieldName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException($"Candidate missing {fieldName}.")
            : value.Trim();

    private static IReadOnlyList<T> RequiredArray<T>(IReadOnlyList<T>? values, string fieldName)
        => values is null || values.Count == 0
            ? throw new InvalidDataException($"Candidate missing {fieldName}.")
            : values;

    private static TEnum ParseEnum<TEnum>(string? value, string fieldName)
        where TEnum : struct
    {
        var text = Required(value, fieldName);
        if (!Enum.TryParse<TEnum>(text, ignoreCase: true, out var parsed))
        {
            throw new InvalidDataException($"Invalid {fieldName}: {text}.");
        }

        return parsed;
    }
}
