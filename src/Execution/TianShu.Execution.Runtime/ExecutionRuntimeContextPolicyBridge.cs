using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Provider;
using TianShu.Contracts.Primitives;

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
        var degradationDecisions = new List<ContextDegradationDecision>();
        var supersedeDecisions = new List<ContextSupersedeDecision>();
        var compressionCandidates = new List<ContextCompressionCandidate>();
        var compressionCheckpoints = new List<ContextCompressionCheckpoint>();
        var inputs = new List<ProviderInputItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var supersedeDecisionIds = new HashSet<string>(StringComparer.Ordinal);
        var compressionCandidateIds = new HashSet<string>(StringComparer.Ordinal);
        var compressionCheckpointIds = new HashSet<string>(StringComparer.Ordinal);
        var includedTokens = 0;
        var totalTokens = candidates.Sum(static candidate => candidate.EstimatedTokens);
        var usageSignal = CreateUsageSignal(context, policy, totalTokens);
        var triggers = CreateTriggers(context, policy, totalTokens, usageSignal);
        var triggerRefs = triggers.Select(static trigger => trigger.TriggerId).ToArray();
        var planId = ReadString(context.Metadata, "context.planId")
                     ?? $"context-plan:{context.ExecutionId.Value}:{step.StepId}";
        var auditRef = ReadString(context.Metadata, "context.auditRef")
                       ?? $"audit://context/{context.ExecutionId.Value}/{step.StepId}";
        var defaultTraceRef = $"trace://context/{context.ExecutionId.Value}/{step.StepId}";
        var traceRefs = new HashSet<string>(StringComparer.Ordinal) { defaultTraceRef };
        var diagnosticsRefs = new HashSet<string>(StringComparer.Ordinal)
        {
            $"diagnostics://context/{context.ExecutionId.Value}/{step.StepId}",
        };

        foreach (var candidate in candidates.OrderBy(candidate => Rank(policy, candidate)).ThenBy(static candidate => candidate.SegmentId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourceLayer = ResolveSourceLayer(candidate);
            var triggerRef = ResolveTriggerRef(candidate, triggerRefs);
            var candidateAuditRef = ReadString(candidate.Metadata, "context.auditRef") ?? auditRef;
            var candidateTraceRef = ReadString(candidate.Metadata, "context.traceRef")
                                    ?? $"trace://context/{context.ExecutionId.Value}/{step.StepId}/{candidate.SegmentId}";
            traceRefs.Add(candidateTraceRef);
            var supersede = TryCreateSupersedeDecision(candidate);
            if (supersede is not null && supersedeDecisionIds.Add(supersede.DecisionId))
            {
                supersedeDecisions.Add(supersede);
            }

            var compression = TryCreateCompressionCandidate(candidate);
            if (compression is not null && compressionCandidateIds.Add(compression.CandidateId))
            {
                compressionCandidates.Add(compression);
            }

            var checkpoint = TryCreateCompressionCheckpoint(candidate, compression, policy.PolicyId, candidateAuditRef);
            if (checkpoint is not null && compressionCheckpointIds.Add(checkpoint.CheckpointId))
            {
                compressionCheckpoints.Add(checkpoint);
            }

            if (!seen.Add(candidate.SegmentId))
            {
                dropped.Add(ToDropped(
                    candidate,
                    ContextDropReason.Duplicate,
                    sourceLayer,
                    triggerRef,
                    supersede,
                    compression,
                    checkpoint,
                    candidateAuditRef,
                    candidateTraceRef));
                continue;
            }

            if (!IsSourceAllowed(policy, candidate))
            {
                dropped.Add(ToDropped(
                    candidate,
                    ContextDropReason.PolicyExcluded,
                    sourceLayer,
                    triggerRef,
                    supersede,
                    compression,
                    checkpoint,
                    candidateAuditRef,
                    candidateTraceRef));
                continue;
            }

            var rule = FindRule(policy, candidate.SourceKind);
            if (RequiresEvidence(policy, rule, candidate) && string.IsNullOrWhiteSpace(candidate.EvidenceRef) && string.IsNullOrWhiteSpace(candidate.ArtifactRef))
            {
                dropped.Add(ToDropped(
                    candidate,
                    ContextDropReason.MissingEvidenceRef,
                    sourceLayer,
                    triggerRef,
                    supersede,
                    compression,
                    checkpoint,
                    candidateAuditRef,
                    candidateTraceRef));
                continue;
            }

            var projectionMode = ResolveProjectionMode(policy, rule, candidate);
            var originalProjectionMode = projectionMode;
            if (supersede is { Disposition: ContextSupersedeDisposition.DropSuperseded })
            {
                dropped.Add(ToDropped(
                    candidate,
                    ContextDropReason.Superseded,
                    sourceLayer,
                    triggerRef,
                    supersede,
                    compression,
                    checkpoint,
                    candidateAuditRef,
                    candidateTraceRef));
                degradationDecisions.Add(ToDegradationDecision(candidate, sourceLayer, originalProjectionMode, ContextProjectionMode.Excluded, ContextDropReason.Superseded));
                continue;
            }

            if (supersede is { Disposition: ContextSupersedeDisposition.ReferenceOnlySuperseded })
            {
                projectionMode = ContextProjectionMode.ReferenceOnly;
            }

            if (projectionMode is ContextProjectionMode.Excluded)
            {
                dropped.Add(ToDropped(
                    candidate,
                    ContextDropReason.PolicyExcluded,
                    sourceLayer,
                    triggerRef,
                    supersede,
                    compression,
                    checkpoint,
                    candidateAuditRef,
                    candidateTraceRef));
                degradationDecisions.Add(ToDegradationDecision(candidate, sourceLayer, originalProjectionMode, projectionMode, ContextDropReason.PolicyExcluded));
                continue;
            }

            var projectedText = ProjectText(candidate, projectionMode);
            var projectedTokens = EstimateProjectedTokens(candidate, rule, projectionMode);
            var preserve = ShouldPreserve(policy, candidate);

            if (preserve && policy.MaxInputTokens > 0 && includedTokens + projectedTokens > policy.MaxInputTokens)
            {
                degradationDecisions.Add(ToDegradationDecision(candidate, sourceLayer, originalProjectionMode, projectionMode, ContextDropReason.BudgetExceeded));
                var protectedBudgetReport = new ContextPolicyApplicationReport(
                    policy.PolicyId,
                    policy.MaxInputTokens,
                    totalTokens,
                    includedTokens,
                    included,
                    dropped,
                    degradationDecisions,
                    supersedeDecisions,
                    compressionCandidates,
                    compressionCheckpoints,
                    triggerRefs,
                    diagnosticsRefs.ToArray(),
                    traceRefs.ToArray(),
                    planId,
                    auditRef,
                    usageSignal);
                return Task.FromResult(new ExecutionRuntimeContextPolicyResult(
                    success: false,
                    Array.Empty<ProviderInputItem>(),
                    protectedBudgetReport,
                    new ExecutionFailure("context_policy_protected_segment_budget_exceeded", "受保护上下文片段超过预算，ContextPolicy 必须 fail closed。")));
            }

            if (!preserve && policy.MaxInputTokens > 0 && includedTokens + projectedTokens > policy.MaxInputTokens)
            {
                dropped.Add(ToDropped(
                    candidate,
                    ContextDropReason.BudgetExceeded,
                    sourceLayer,
                    triggerRef,
                    supersede,
                    compression,
                    checkpoint,
                    candidateAuditRef,
                    candidateTraceRef));
                degradationDecisions.Add(ToDegradationDecision(candidate, sourceLayer, originalProjectionMode, projectionMode, ContextDropReason.BudgetExceeded));
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
                candidate.ArtifactRef,
                sourceLayer,
                triggerRef,
                supersede?.DecisionId,
                supersede?.Disposition,
                checkpoint?.CheckpointId,
                candidateAuditRef,
                candidateTraceRef));

            if (projectionMode != originalProjectionMode)
            {
                degradationDecisions.Add(ToDegradationDecision(candidate, sourceLayer, originalProjectionMode, projectionMode, null));
            }
        }

        var report = new ContextPolicyApplicationReport(
            policy.PolicyId,
            policy.MaxInputTokens,
            totalTokens,
            includedTokens,
            included,
            dropped,
            degradationDecisions,
            supersedeDecisions,
            compressionCandidates,
            compressionCheckpoints,
            triggerRefs,
            diagnosticsRefs.ToArray(),
            traceRefs.ToArray(),
            planId,
            auditRef,
            usageSignal);

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

    private static DroppedContextSegment ToDropped(
        ContextSourceCandidate candidate,
        ContextDropReason reason,
        string sourceLayer,
        string? triggerRef,
        ContextSupersedeDecision? supersede,
        ContextCompressionCandidate? compression,
        ContextCompressionCheckpoint? checkpoint,
        string auditRef,
        string traceRef)
        => new(
            candidate.SegmentId,
            candidate.SourceKind,
            reason,
            candidate.EstimatedTokens,
            candidate.EvidenceRef,
            candidate.ArtifactRef,
            sourceLayer,
            triggerRef,
            supersede?.DecisionId,
            supersede?.Disposition,
            compression?.CandidateId,
            checkpoint?.CheckpointId,
            auditRef,
            traceRef);

    private static ContextDegradationDecision ToDegradationDecision(
        ContextSourceCandidate candidate,
        string sourceLayer,
        ContextProjectionMode originalMode,
        ContextProjectionMode effectiveMode,
        ContextDropReason? dropReason)
        => new(
            candidate.SegmentId,
            sourceLayer,
            originalMode,
            effectiveMode,
            dropReason,
            candidate.EvidenceRef,
            candidate.ArtifactRef);

    private static ContextUsageSignal CreateUsageSignal(
        ExecutionRuntimeContext context,
        ContextPolicy policy,
        int estimatedTotalTokens)
    {
        var source = ReadString(context.Metadata, "context.usage.source") ?? "runtime.context.estimated";
        var estimated = ReadBoolean(context.Metadata, "context.usage.estimated") ?? true;
        var inputTokens = ReadInt32(context.Metadata, "context.usage.inputTokens") ?? estimatedTotalTokens;
        var outputTokens = ReadInt32(context.Metadata, "context.usage.outputTokens");
        var reasoningTokens = ReadInt32(context.Metadata, "context.usage.reasoningTokens");
        var totalTokens = ReadInt32(context.Metadata, "context.usage.totalTokens")
                          ?? SumKnown(inputTokens, outputTokens, reasoningTokens);
        return new ContextUsageSignal(
            ReadString(context.Metadata, "context.usage.signalId") ?? $"context-usage:{context.ExecutionId.Value}",
            source,
            estimated,
            inputTokens,
            outputTokens,
            reasoningTokens,
            totalTokens,
            ReadInt32(context.Metadata, "context.usage.modelContextWindow"),
            ReadString(context.Metadata, "context.usage.missingReason") ?? (estimated ? "provider_usage_missing" : null),
            new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["policyId"] = StructuredValue.FromString(policy.PolicyId),
                ["executionId"] = StructuredValue.FromString(context.ExecutionId.Value),
            }));
    }

    private static IReadOnlyList<ContextPressureTrigger> CreateTriggers(
        ExecutionRuntimeContext context,
        ContextPolicy policy,
        int estimatedTotalTokens,
        ContextUsageSignal usageSignal)
    {
        var triggers = new List<ContextPressureTrigger>();
        if (policy.MaxInputTokens > 0 && estimatedTotalTokens > policy.MaxInputTokens)
        {
            triggers.Add(new ContextPressureTrigger(
                ReadString(context.Metadata, "context.triggerRef") ?? $"context-trigger:{policy.PolicyId}:budget",
                ContextPressureTriggerKind.EstimatedInputBudgetExceeded,
                thresholdRatio: 1,
                thresholdTokens: policy.MaxInputTokens,
                observedTokens: estimatedTotalTokens,
                usageSignal.ModelContextWindow,
                policy.FailClosed));
        }

        if (usageSignal.MissingReason is not null)
        {
            triggers.Add(new ContextPressureTrigger(
                $"context-trigger:{policy.PolicyId}:missing-usage",
                ContextPressureTriggerKind.MissingUsageFallback,
                thresholdRatio: 0,
                thresholdTokens: null,
                observedTokens: usageSignal.TotalTokens ?? estimatedTotalTokens,
                usageSignal.ModelContextWindow,
                failClosed: false));
        }

        return triggers;
    }

    private static string ResolveSourceLayer(ContextSourceCandidate candidate)
        => ReadString(candidate.Metadata, "context.layer") ?? candidate.SourceKind switch
        {
            ContextSourceKind.CurrentUserInput or ContextSourceKind.LatestUserCorrection => "L0",
            ContextSourceKind.ToolEvidence or ContextSourceKind.ArtifactReference => "L1",
            ContextSourceKind.WorkspaceFact or ContextSourceKind.MemoryRecord => "L2",
            ContextSourceKind.ConversationHistory => "L3",
            _ => "L4",
        };

    private static string? ResolveTriggerRef(ContextSourceCandidate candidate, IReadOnlyList<string> triggerRefs)
        => ReadString(candidate.Metadata, "context.triggerRef")
           ?? (triggerRefs.Count > 0 ? triggerRefs[0] : null);

    private static ContextSupersedeDecision? TryCreateSupersedeDecision(ContextSourceCandidate candidate)
    {
        var decisionId = ReadString(candidate.Metadata, "context.supersedeDecisionId");
        var replacementId = ReadString(candidate.Metadata, "context.supersedeReplacementSegmentId");
        if (decisionId is null || replacementId is null)
        {
            return null;
        }

        return new ContextSupersedeDecision(
            decisionId,
            candidate.SegmentId,
            replacementId,
            ReadSupersedeDisposition(candidate.Metadata) ?? ContextSupersedeDisposition.ReferenceOnlySuperseded,
            ReadString(candidate.Metadata, "context.supersedeReason") ?? "Superseded by a newer context segment.",
            candidate.EvidenceRef,
            ReadString(candidate.Metadata, "context.auditRef"));
    }

    private static ContextCompressionCandidate? TryCreateCompressionCandidate(ContextSourceCandidate candidate)
    {
        if (ShouldProtectFromCompression(candidate))
        {
            return null;
        }

        var candidateId = ReadString(candidate.Metadata, "context.compressionCandidateId");
        if (candidateId is null)
        {
            return null;
        }

        return new ContextCompressionCandidate(
            candidateId,
            [candidate.SegmentId],
            candidate.EstimatedTokens,
            ReadInt32(candidate.Metadata, "context.compressionTargetTokens") ?? Math.Max(1, candidate.EstimatedTokens / 2),
            ReadBoolean(candidate.Metadata, "context.compressionReversible") ?? true,
            ReadString(candidate.Metadata, "context.compressionReason") ?? "Context pressure compression candidate.",
            ReadString(candidate.Metadata, "context.compressionArtifactRef") ?? candidate.ArtifactRef,
            candidate.EvidenceRef);
    }

    private static bool ShouldProtectFromCompression(ContextSourceCandidate candidate)
        => candidate.SourceKind is ContextSourceKind.CurrentUserInput or ContextSourceKind.LatestUserCorrection
           || candidate.IsLatestUserCorrection;

    private static ContextCompressionCheckpoint? TryCreateCompressionCheckpoint(
        ContextSourceCandidate candidate,
        ContextCompressionCandidate? compression,
        string policyId,
        string auditRef)
    {
        if (compression is null)
        {
            return null;
        }

        var checkpointRef = ReadString(candidate.Metadata, "context.compressionCheckpointRef");
        var artifactRef = ReadString(candidate.Metadata, "context.compressionArtifactRef") ?? candidate.ArtifactRef;
        if (checkpointRef is null || artifactRef is null)
        {
            return null;
        }

        return new ContextCompressionCheckpoint(
            checkpointRef,
            compression.CandidateId,
            compression.SourceSegmentIds,
            artifactRef,
            compression.Reversible,
            policyId,
            auditRef);
    }

    private static ContextSupersedeDisposition? ReadSupersedeDisposition(MetadataBag metadata)
    {
        var value = ReadString(metadata, "context.supersedeDisposition");
        return Enum.TryParse<ContextSupersedeDisposition>(value, ignoreCase: true, out var disposition)
            ? disposition
            : null;
    }

    private static string? ReadString(MetadataBag metadata, string key)
        => metadata.TryGetValue(key, out var value) ? value.GetString() : null;

    private static int? ReadInt32(MetadataBag metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value))
        {
            return null;
        }

        try
        {
            return value.GetInt32();
        }
        catch (InvalidOperationException)
        {
            return int.TryParse(value.GetString(), out var parsed) ? parsed : null;
        }
    }

    private static bool? ReadBoolean(MetadataBag metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value))
        {
            return null;
        }

        try
        {
            return value.GetBoolean();
        }
        catch (InvalidOperationException)
        {
            return bool.TryParse(value.GetString(), out var parsed) ? parsed : null;
        }
    }

    private static int? SumKnown(params int?[] values)
    {
        var total = 0;
        var any = false;
        foreach (var value in values)
        {
            if (value is null)
            {
                continue;
            }

            total += value.Value;
            any = true;
        }

        return any ? total : null;
    }
}
