using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Provider;

namespace TianShu.Execution.Runtime;

/// <summary>
/// Execution Runtime 的 ContextPolicy 执行入口，只消费 Kernel 已批准策略。
/// Execution Runtime entry point for ContextPolicy application; only consumes Kernel-approved policies.
/// </summary>
public interface IExecutionRuntimeContextPolicyBridge
{
    Task<ExecutionRuntimeContextPolicyResult> PrepareProviderInputAsync(
        ModelInvocationStep step,
        ExecutionRuntimeContext context,
        ApprovedContextPolicy approvedPolicy,
        IReadOnlyList<ContextSourceCandidate> candidates,
        CancellationToken cancellationToken);
}

/// <summary>
/// ContextPolicy 执行结果，输出 provider-neutral input 与可诊断报告。
/// ContextPolicy application result that returns provider-neutral input and a diagnostic report.
/// </summary>
public sealed record ExecutionRuntimeContextPolicyResult
{
    public ExecutionRuntimeContextPolicyResult(
        bool success,
        IReadOnlyList<ProviderInputItem>? inputs = null,
        ContextPolicyApplicationReport? report = null,
        ExecutionFailure? failure = null)
    {
        Success = success;
        Inputs = inputs ?? Array.Empty<ProviderInputItem>();
        Report = report;
        Failure = failure;
    }

    public bool Success { get; }

    public IReadOnlyList<ProviderInputItem> Inputs { get; }

    public ContextPolicyApplicationReport? Report { get; }

    public ExecutionFailure? Failure { get; }
}

/// <summary>
/// 默认 ContextPolicy 桥接器，负责排序、预算裁切、引用化和 dropped reason 记录。
/// Default ContextPolicy bridge responsible for ordering, budget trimming, reference-only projection, and dropped reasons.
/// </summary>
public sealed class ExecutionRuntimeContextPolicyBridge : IExecutionRuntimeContextPolicyBridge
{
    public Task<ExecutionRuntimeContextPolicyResult> PrepareProviderInputAsync(
        ModelInvocationStep step,
        ExecutionRuntimeContext context,
        ApprovedContextPolicy approvedPolicy,
        IReadOnlyList<ContextSourceCandidate> candidates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(approvedPolicy);
        ArgumentNullException.ThrowIfNull(candidates);
        cancellationToken.ThrowIfCancellationRequested();

        var failure = Validate(step, approvedPolicy);
        if (failure is not null)
        {
            return Task.FromResult(CreateFailure(failure));
        }

        var policy = approvedPolicy.Policy;
        var dropped = new List<DroppedContextSegment>();
        var included = new List<MaterializedContextSegment>();
        var inputs = new List<ProviderInputItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var includedTokens = 0;
        var totalTokens = candidates.Sum(static candidate => candidate.EstimatedTokens);

        foreach (var candidate in candidates.OrderBy(candidate => Rank(policy, candidate)).ThenBy(static candidate => candidate.SegmentId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!seen.Add(candidate.SegmentId))
            {
                dropped.Add(ToDropped(candidate, ContextDropReason.Duplicate));
                continue;
            }

            if (!IsSourceAllowed(policy, candidate))
            {
                dropped.Add(ToDropped(candidate, ContextDropReason.PolicyExcluded));
                continue;
            }

            var rule = FindRule(policy, candidate.SourceKind);
            if (RequiresEvidence(policy, rule, candidate) && string.IsNullOrWhiteSpace(candidate.EvidenceRef) && string.IsNullOrWhiteSpace(candidate.ArtifactRef))
            {
                dropped.Add(ToDropped(candidate, ContextDropReason.MissingEvidenceRef));
                continue;
            }

            var projectionMode = ResolveProjectionMode(policy, rule, candidate);
            if (projectionMode is ContextProjectionMode.Excluded)
            {
                dropped.Add(ToDropped(candidate, ContextDropReason.PolicyExcluded));
                continue;
            }

            var projectedText = ProjectText(candidate, projectionMode);
            var projectedTokens = EstimateProjectedTokens(candidate, rule, projectionMode);
            var preserve = ShouldPreserve(policy, candidate);

            if (!preserve && policy.MaxInputTokens > 0 && includedTokens + projectedTokens > policy.MaxInputTokens)
            {
                dropped.Add(ToDropped(candidate, ContextDropReason.BudgetExceeded));
                continue;
            }

            includedTokens += projectedTokens;
            inputs.Add(new TextProviderInputItem(projectedText));
            included.Add(new MaterializedContextSegment(
                candidate.SegmentId,
                candidate.SourceKind,
                projectionMode,
                projectedTokens,
                candidate.EvidenceRef,
                candidate.ArtifactRef));
        }

        var report = new ContextPolicyApplicationReport(
            policy.PolicyId,
            policy.MaxInputTokens,
            totalTokens,
            includedTokens,
            included,
            dropped);

        return Task.FromResult(new ExecutionRuntimeContextPolicyResult(
            success: inputs.Count > 0,
            inputs,
            report,
            inputs.Count == 0 && policy.FailClosed
                ? new ExecutionFailure("context_policy_no_input", "ContextPolicy 没有生成任何 provider-neutral 输入。")
                : null));
    }

    private static ExecutionFailure? Validate(ModelInvocationStep step, ApprovedContextPolicy approvedPolicy)
    {
        if (approvedPolicy.SourceIntentId != step.SourceIntentId
            || approvedPolicy.SourceGraphId != step.SourceGraphId
            || approvedPolicy.SourceStageId != step.SourceStageId
            || approvedPolicy.SourceKernelOperationId != step.SourceKernelOperationId)
        {
            return new ExecutionFailure("context_policy_source_mismatch", "ApprovedContextPolicy 来源与 ModelInvocationStep 不一致。");
        }

        if (approvedPolicy.Policy.MaxInputTokens <= 0 && approvedPolicy.Policy.FailClosed)
        {
            return new ExecutionFailure("context_policy_missing_budget", "ContextPolicy 必须声明正数上下文预算。");
        }

        return null;
    }

    private static ExecutionRuntimeContextPolicyResult CreateFailure(ExecutionFailure failure)
        => new(success: false, failure: failure);

    private static bool IsSourceAllowed(ContextPolicy policy, ContextSourceCandidate candidate)
        => policy.AllowedSourceKinds.Count == 0
           || policy.AllowedSourceKinds.Contains(candidate.SourceKind.ToString(), StringComparer.OrdinalIgnoreCase);

    private static bool RequiresEvidence(ContextPolicy policy, ContextSourceRule? rule, ContextSourceCandidate candidate)
        => policy.RequireEvidenceRefs
           && (rule?.RequireEvidenceRef == true
               || candidate.SourceKind is ContextSourceKind.ToolEvidence or ContextSourceKind.ArtifactReference);

    private static bool ShouldPreserve(ContextPolicy policy, ContextSourceCandidate candidate)
        => candidate.SourceKind is ContextSourceKind.CurrentUserInput
           || (policy.PreserveLatestUserCorrection
               && (candidate.IsLatestUserCorrection || candidate.SourceKind is ContextSourceKind.LatestUserCorrection));

    private static int Rank(ContextPolicy policy, ContextSourceCandidate candidate)
    {
        if (ShouldPreserve(policy, candidate))
        {
            return 0;
        }

        if (policy.PriorityRefs.Contains(candidate.SegmentId, StringComparer.Ordinal)
            || (!string.IsNullOrWhiteSpace(candidate.EvidenceRef) && policy.PriorityRefs.Contains(candidate.EvidenceRef, StringComparer.Ordinal)))
        {
            return 1;
        }

        return FindRule(policy, candidate.SourceKind)?.Priority ?? candidate.SourceKind switch
        {
            ContextSourceKind.ToolEvidence => 20,
            ContextSourceKind.ArtifactReference => 30,
            ContextSourceKind.WorkspaceFact => 40,
            ContextSourceKind.MemoryRecord => 60,
            ContextSourceKind.ConversationHistory => 80,
            _ => 100,
        };
    }

    private static ContextSourceRule? FindRule(ContextPolicy policy, ContextSourceKind sourceKind)
        => policy.SourceRules.FirstOrDefault(rule => rule.SourceKind == sourceKind);

    private static ContextProjectionMode ResolveProjectionMode(
        ContextPolicy policy,
        ContextSourceRule? rule,
        ContextSourceCandidate candidate)
    {
        var minConfidence = rule?.MinConfidence ?? 0.5m;
        if (!ShouldPreserve(policy, candidate) && candidate.Confidence < minConfidence)
        {
            return policy.LowConfidenceMode;
        }

        return rule?.ProjectionMode is { } mode and not ContextProjectionMode.Unspecified
            ? mode
            : ContextProjectionMode.Full;
    }

    private static int EstimateProjectedTokens(
        ContextSourceCandidate candidate,
        ContextSourceRule? rule,
        ContextProjectionMode projectionMode)
        => projectionMode switch
        {
            ContextProjectionMode.ReferenceOnly => Math.Min(Math.Max(1, candidate.EstimatedTokens / 10), 32),
            ContextProjectionMode.Summary => Math.Min(candidate.EstimatedTokens, rule?.MaxTokens > 0 ? rule.MaxTokens : Math.Max(1, candidate.EstimatedTokens / 2)),
            ContextProjectionMode.Excluded => 0,
            _ => rule?.MaxTokens > 0 ? Math.Min(candidate.EstimatedTokens, rule.MaxTokens) : candidate.EstimatedTokens,
        };

    private static string ProjectText(ContextSourceCandidate candidate, ContextProjectionMode projectionMode)
        => projectionMode switch
        {
            ContextProjectionMode.ReferenceOnly => $"[reference-only:{candidate.SourceKind}] {candidate.EvidenceRef ?? candidate.ArtifactRef ?? candidate.SegmentId}",
            ContextProjectionMode.Summary => $"[summary:{candidate.SourceKind}] {candidate.Content}",
            _ => candidate.Content,
        };

    private static DroppedContextSegment ToDropped(ContextSourceCandidate candidate, ContextDropReason reason)
        => new(
            candidate.SegmentId,
            candidate.SourceKind,
            reason,
            candidate.EstimatedTokens,
            candidate.EvidenceRef,
            candidate.ArtifactRef);
}
