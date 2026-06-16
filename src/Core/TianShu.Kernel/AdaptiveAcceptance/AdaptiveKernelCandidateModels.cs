using System.Text.Json;

namespace TianShu.Kernel.AdaptiveAcceptance;

/// <summary>
/// 自适应内核验收使用的 StageGraph 候选 DTO。
/// StageGraph candidate DTO used by adaptive kernel acceptance.
/// </summary>
public sealed record AdaptiveKernelStageGraphCandidate
{
    public string? GraphId { get; init; }

    public string? Version { get; init; }

    public string? IntentKind { get; init; }

    public string? EntryStageId { get; init; }

    public IReadOnlyList<AdaptiveKernelStageCandidate>? Stages { get; init; }

    public IReadOnlyList<AdaptiveKernelEdgeCandidate>? Edges { get; init; }

    public AdaptiveKernelPolicyCandidate? Policies { get; init; }

    public AdaptiveKernelBudgetCandidate? Budgets { get; init; }

    public AdaptiveKernelCheckpointRulesCandidate? CheckpointRules { get; init; }

    public AdaptiveKernelRecoveryRulesCandidate? RecoveryRules { get; init; }

    public AdaptiveKernelEvaluationRulesCandidate? EvaluationRules { get; init; }
}

/// <summary>
/// Stage 候选 DTO。
/// Stage candidate DTO.
/// </summary>
public sealed record AdaptiveKernelStageCandidate
{
    public string? StageId { get; init; }

    public string? Kind { get; init; }

    public string? Objective { get; init; }

    public IReadOnlyList<string>? AllowedKernelToolIds { get; init; }

    public IReadOnlyList<string>? CapabilityToolIds { get; init; }

    public string? SideEffectLevel { get; init; }

    public AdaptiveKernelBudgetCandidate? Budget { get; init; }

    public AdaptiveKernelContextPolicyCandidate? ContextPolicy { get; init; }

    public AdaptiveKernelModelRoutePolicyCandidate? ModelRoutePolicy { get; init; }
}

/// <summary>
/// Stage edge 候选 DTO。
/// Stage-edge candidate DTO.
/// </summary>
public sealed record AdaptiveKernelEdgeCandidate
{
    public string? EdgeId { get; init; }

    public string? FromStageId { get; init; }

    public string? ToStageId { get; init; }

    public string? TransitionKind { get; init; }

    public string? ConditionKind { get; init; }

    public IReadOnlyList<string>? RequiredSignals { get; init; }
}

/// <summary>
/// Graph policy 候选 DTO。
/// Graph policy candidate DTO.
/// </summary>
public sealed record AdaptiveKernelPolicyCandidate
{
    public IReadOnlyList<string>? RequiredPolicyIds { get; init; }

    public IReadOnlyList<string>? AllowedKernelToolIds { get; init; }

    public IReadOnlyList<string>? AllowedCapabilityToolIds { get; init; }

    public IReadOnlyList<string>? AllowedModuleIds { get; init; }

    public string? MaxSideEffectLevel { get; init; }

    public bool RequiresHumanGate { get; init; } = true;
}

/// <summary>
/// Kernel budget 候选 DTO。
/// Kernel budget candidate DTO.
/// </summary>
public sealed record AdaptiveKernelBudgetCandidate
{
    public int TokenBudget { get; init; }

    public long TimeBudgetMs { get; init; }

    public decimal CostBudget { get; init; }

    public int RetryBudget { get; init; }

    public int ToolCallBudget { get; init; }
}

/// <summary>
/// Context policy 候选 DTO。
/// Context policy candidate DTO.
/// </summary>
public sealed record AdaptiveKernelContextPolicyCandidate
{
    public int MaxInputTokens { get; init; }

    public IReadOnlyList<string>? AllowedSourceKinds { get; init; }

    public bool PreserveLatestUserCorrection { get; init; } = true;

    public bool RequireEvidenceRefs { get; init; } = true;

    public bool FailClosed { get; init; } = true;

    public string? PolicyId { get; init; }
}

/// <summary>
/// Model route policy 候选 DTO。
/// Model route policy candidate DTO.
/// </summary>
public sealed record AdaptiveKernelModelRoutePolicyCandidate
{
    public IReadOnlyList<string>? RouteCandidateIds { get; init; }

    public string? PreferredRouteId { get; init; }

    public string? FallbackRouteId { get; init; }

    public string? RouteKind { get; init; }
}

public sealed record AdaptiveKernelCheckpointRulesCandidate
{
    public bool Enabled { get; init; }

    public IReadOnlyList<string>? RequiredStageIds { get; init; }
}

public sealed record AdaptiveKernelRecoveryRulesCandidate
{
    public bool Enabled { get; init; }

    public int MaxRecoveryAttempts { get; init; } = 1;
}

public sealed record AdaptiveKernelEvaluationRulesCandidate
{
    public bool Enabled { get; init; }

    public IReadOnlyList<string>? MetricIds { get; init; }
}

/// <summary>
/// StageGraph patch 候选 DTO。
/// StageGraph patch candidate DTO.
/// </summary>
public sealed record AdaptiveKernelStageGraphPatchCandidate
{
    public string? ProposalId { get; init; }

    public string? TargetGraphId { get; init; }

    public IReadOnlyList<AdaptiveKernelPatchOperationCandidate>? Operations { get; init; }
}

public sealed record AdaptiveKernelPatchOperationCandidate
{
    public string? OperationKind { get; init; }

    public string? TargetStageId { get; init; }

    public string? TargetEdgeId { get; init; }

    public JsonElement? Payload { get; init; }
}

public sealed record AdaptiveKernelRecoveryProposalCandidate
{
    public string? ProposalId { get; init; }

    public string? RecoveryKind { get; init; }

    public IReadOnlyList<string>? ActionRefs { get; init; }

    public bool RequiresHumanGate { get; init; } = true;
}

public sealed record AdaptiveKernelCheckpointProposalCandidate
{
    public string? OperationId { get; init; }

    public string? SourceStageId { get; init; }

    public string? CheckpointRef { get; init; }
}
