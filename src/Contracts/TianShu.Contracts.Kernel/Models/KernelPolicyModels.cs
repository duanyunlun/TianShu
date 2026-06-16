using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Kernel;

/// <summary>
/// 副作用等级；默认 Unspecified 必须由验证器拒绝。
/// Side-effect level; default Unspecified must be rejected by validators.
/// </summary>
public enum SideEffectLevel
{
    Unspecified = 0,
    None = 1,
    ReadOnly = 2,
    WorkspaceWrite = 3,
    ExternalNetwork = 4,
    ExternalMutation = 5,
    HostMutation = 6,
    Privileged = 7,
}

/// <summary>
/// 图策略执行模式；默认 Deny 表示 fail closed。
/// Graph-policy enforcement mode; default Deny represents fail closed.
/// </summary>
public enum PolicyEnforcementMode
{
    Deny = 0,
    AllowListed = 1,
    HumanGate = 2,
}

/// <summary>
/// Kernel 审查决策；默认 Unspecified 不能被当成批准。
/// Kernel review decision; default Unspecified must not be treated as approved.
/// </summary>
public enum KernelReviewDecision
{
    Unspecified = 0,
    Rejected = 1,
    NeedsRevision = 2,
    RequiresHumanGate = 3,
    Approved = 4,
}

/// <summary>
/// 合同引用，指向输入、输出或模块能力 schema。
/// Contract reference pointing to input, output, or module-capability schema.
/// </summary>
public sealed record ContractRef
{
    public ContractRef(string contractId, string version, string? schemaRef = null)
    {
        ContractId = KernelContractGuard.RequiredText(contractId, nameof(contractId));
        Version = KernelContractGuard.RequiredText(version, nameof(version));
        SchemaRef = schemaRef;
    }

    public string ContractId { get; }

    public string Version { get; }

    public string? SchemaRef { get; }
}

/// <summary>
/// 权限信封，声明 Kernel operation 可使用的权限边界。
/// Permission envelope declaring the permission boundary available to a Kernel operation.
/// </summary>
public sealed record PermissionEnvelope
{
    public PermissionEnvelope(
        IReadOnlyList<string>? scopes = null,
        IReadOnlyList<string>? grants = null,
        bool requiresHumanGate = true,
        string? reason = null)
    {
        Scopes = KernelContractGuard.ListOrEmpty(scopes);
        Grants = KernelContractGuard.ListOrEmpty(grants);
        RequiresHumanGate = requiresHumanGate;
        Reason = reason;
    }

    public IReadOnlyList<string> Scopes { get; }

    public IReadOnlyList<string> Grants { get; }

    public bool RequiresHumanGate { get; }

    public string? Reason { get; }
}

/// <summary>
/// 副作用画像，供 Kernel 和 Execution Runtime 判定是否允许物化操作。
/// Side-effect profile used by Kernel and Execution Runtime to decide whether an operation may materialize.
/// </summary>
public sealed record SideEffectProfile
{
    public SideEffectProfile(
        SideEffectLevel level = SideEffectLevel.Unspecified,
        IReadOnlyList<string>? affectedResources = null,
        bool reversible = false,
        bool requiresAudit = true)
    {
        Level = level;
        AffectedResources = KernelContractGuard.ListOrEmpty(affectedResources);
        Reversible = reversible;
        RequiresAudit = requiresAudit;
    }

    public SideEffectLevel Level { get; }

    public IReadOnlyList<string> AffectedResources { get; }

    public bool Reversible { get; }

    public bool RequiresAudit { get; }
}

/// <summary>
/// 图级策略集合。
/// Graph-level policy set.
/// </summary>
public sealed record GraphPolicySet
{
    public GraphPolicySet(
        PolicyEnforcementMode enforcementMode = PolicyEnforcementMode.Deny,
        IReadOnlyList<string>? requiredPolicyIds = null,
        IReadOnlyList<string>? allowedKernelToolIds = null,
        IReadOnlyList<string>? allowedCapabilityToolIds = null,
        IReadOnlyList<string>? allowedModuleIds = null,
        SideEffectLevel maxSideEffectLevel = SideEffectLevel.Unspecified,
        bool requiresHumanGate = true)
    {
        EnforcementMode = enforcementMode;
        RequiredPolicyIds = KernelContractGuard.ListOrEmpty(requiredPolicyIds);
        AllowedKernelToolIds = KernelContractGuard.ListOrEmpty(allowedKernelToolIds);
        AllowedCapabilityToolIds = KernelContractGuard.ListOrEmpty(allowedCapabilityToolIds);
        AllowedModuleIds = KernelContractGuard.ListOrEmpty(allowedModuleIds);
        MaxSideEffectLevel = maxSideEffectLevel;
        RequiresHumanGate = requiresHumanGate;
    }

    public PolicyEnforcementMode EnforcementMode { get; }

    public IReadOnlyList<string> RequiredPolicyIds { get; }

    public IReadOnlyList<string> AllowedKernelToolIds { get; }

    public IReadOnlyList<string> AllowedCapabilityToolIds { get; }

    public IReadOnlyList<string> AllowedModuleIds { get; }

    public SideEffectLevel MaxSideEffectLevel { get; }

    public bool RequiresHumanGate { get; }
}

/// <summary>
/// 模型路由策略，表达 Kernel 已批准的候选和选择约束。
/// Model-route policy describing Kernel-approved candidates and selection constraints.
/// </summary>
public sealed record ModelRoutePolicy
{
    public ModelRoutePolicy(
        IReadOnlyList<string>? routeCandidateIds = null,
        string? preferredRouteId = null,
        string? fallbackRouteId = null,
        bool failClosedWhenMissingCandidate = true,
        MetadataBag? metadata = null,
        string? policyId = null,
        string? routeKind = null,
        IReadOnlyList<ModelRouteCandidateBinding>? candidates = null)
    {
        RouteCandidateIds = KernelContractGuard.ListOrEmpty(routeCandidateIds);
        PreferredRouteId = preferredRouteId;
        FallbackRouteId = fallbackRouteId;
        FailClosedWhenMissingCandidate = failClosedWhenMissingCandidate;
        Metadata = KernelContractGuard.MetadataOrEmpty(metadata);
        PolicyId = string.IsNullOrWhiteSpace(policyId) ? "model.route.policy.default" : policyId.Trim();
        RouteKind = string.IsNullOrWhiteSpace(routeKind) ? null : routeKind.Trim();
        Candidates = KernelContractGuard.ListOrEmpty(candidates);
    }

    public string PolicyId { get; }

    public string? RouteKind { get; }

    public IReadOnlyList<string> RouteCandidateIds { get; }

    public IReadOnlyList<ModelRouteCandidateBinding> Candidates { get; }

    public string? PreferredRouteId { get; }

    public string? FallbackRouteId { get; }

    public bool FailClosedWhenMissingCandidate { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// Kernel 已批准的模型路由候选绑定，不携带 secret 明文。
/// Kernel-approved model route candidate binding that never carries secret values.
/// </summary>
public sealed record ModelRouteCandidateBinding
{
    public ModelRouteCandidateBinding(
        string candidateId,
        string providerModuleId,
        string providerKey,
        string model,
        int candidateIndex = 0,
        string? protocol = null,
        string? endpointRef = null,
        string? secretRef = null,
        IReadOnlyList<string>? capabilities = null,
        string? unavailableReason = null,
        MetadataBag? metadata = null)
    {
        CandidateId = KernelContractGuard.RequiredText(candidateId, nameof(candidateId));
        ProviderModuleId = KernelContractGuard.RequiredText(providerModuleId, nameof(providerModuleId));
        ProviderKey = KernelContractGuard.RequiredText(providerKey, nameof(providerKey));
        Model = KernelContractGuard.RequiredText(model, nameof(model));
        CandidateIndex = KernelContractGuard.NonNegative(candidateIndex, nameof(candidateIndex));
        Protocol = protocol;
        EndpointRef = endpointRef;
        SecretRef = secretRef;
        Capabilities = KernelContractGuard.ListOrEmpty(capabilities);
        UnavailableReason = unavailableReason;
        Metadata = KernelContractGuard.MetadataOrEmpty(metadata);
    }

    public string CandidateId { get; }

    public string ProviderModuleId { get; }

    public string ProviderKey { get; }

    public string Model { get; }

    public int CandidateIndex { get; }

    public string? Protocol { get; }

    public string? EndpointRef { get; }

    public string? SecretRef { get; }

    public IReadOnlyList<string> Capabilities { get; }

    public string? UnavailableReason { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// Kernel 已批准的模型路由策略信封，Execution Runtime 只能消费该类型物化模型调用。
/// Kernel-approved model route policy envelope; Execution Runtime may only consume this type to materialize model calls.
/// </summary>
public sealed record ApprovedModelRoutePolicy
{
    public ApprovedModelRoutePolicy(
        ModelRoutePolicy policy,
        CoreIntentId sourceIntentId,
        StageGraphId sourceGraphId,
        StageId sourceStageId,
        KernelOperationId sourceKernelOperationId,
        DateTimeOffset? approvedAt = null,
        IReadOnlyList<string>? validationRefs = null)
    {
        Policy = KernelContractGuard.NotNull(policy, nameof(policy));
        SourceIntentId = sourceIntentId;
        SourceGraphId = sourceGraphId;
        SourceStageId = sourceStageId;
        SourceKernelOperationId = sourceKernelOperationId;
        ApprovedAt = approvedAt ?? DateTimeOffset.UtcNow;
        ValidationRefs = KernelContractGuard.ListOrEmpty(validationRefs);
    }

    public ModelRoutePolicy Policy { get; }

    public CoreIntentId SourceIntentId { get; }

    public StageGraphId SourceGraphId { get; }

    public StageId SourceStageId { get; }

    public KernelOperationId SourceKernelOperationId { get; }

    public DateTimeOffset ApprovedAt { get; }

    public IReadOnlyList<string> ValidationRefs { get; }
}

/// <summary>
/// ModelRoutePolicy 物化报告，供 diagnostics、trace 和 Host projection 使用。
/// ModelRoutePolicy materialization report used by diagnostics, trace, and Host projection.
/// </summary>
public sealed record ModelRoutePolicyApplicationReport
{
    public ModelRoutePolicyApplicationReport(
        string policyId,
        string? routeKind,
        string selectedCandidateId,
        string providerModuleId,
        string providerKey,
        string model,
        int candidateIndex,
        string? protocol = null,
        string? endpointRef = null,
        string? diagnosticsCorrelationId = null,
        IReadOnlyList<string>? filteredCandidateRefs = null)
    {
        PolicyId = KernelContractGuard.RequiredText(policyId, nameof(policyId));
        RouteKind = routeKind;
        SelectedCandidateId = KernelContractGuard.RequiredText(selectedCandidateId, nameof(selectedCandidateId));
        ProviderModuleId = KernelContractGuard.RequiredText(providerModuleId, nameof(providerModuleId));
        ProviderKey = KernelContractGuard.RequiredText(providerKey, nameof(providerKey));
        Model = KernelContractGuard.RequiredText(model, nameof(model));
        CandidateIndex = KernelContractGuard.NonNegative(candidateIndex, nameof(candidateIndex));
        Protocol = protocol;
        EndpointRef = endpointRef;
        DiagnosticsCorrelationId = diagnosticsCorrelationId;
        FilteredCandidateRefs = KernelContractGuard.ListOrEmpty(filteredCandidateRefs);
    }

    public string PolicyId { get; }

    public string? RouteKind { get; }

    public string SelectedCandidateId { get; }

    public string ProviderModuleId { get; }

    public string ProviderKey { get; }

    public string Model { get; }

    public int CandidateIndex { get; }

    public string? Protocol { get; }

    public string? EndpointRef { get; }

    public string? DiagnosticsCorrelationId { get; }

    public IReadOnlyList<string> FilteredCandidateRefs { get; }
}

/// <summary>
/// 上下文来源类别，用于 Kernel 明确哪些材料可进入模型输入。
/// Context source kind used by Kernel to declare which materials may enter model input.
/// </summary>
public enum ContextSourceKind
{
    Unspecified = 0,
    CurrentUserInput = 1,
    LatestUserCorrection = 2,
    ToolEvidence = 3,
    ArtifactReference = 4,
    MemoryRecord = 5,
    ConversationHistory = 6,
    WorkspaceFact = 7,
    SystemInstruction = 8,
}

/// <summary>
/// 上下文投影模式，决定内容以全文、摘要、引用或排除方式进入执行输入。
/// Context projection mode deciding whether content enters execution input as full text, summary, reference, or exclusion.
/// </summary>
public enum ContextProjectionMode
{
    Unspecified = 0,
    Full = 1,
    Summary = 2,
    ReferenceOnly = 3,
    Excluded = 4,
}

/// <summary>
/// 上下文材料丢弃或降级原因。
/// Reason why a context material is dropped or downgraded.
/// </summary>
public enum ContextDropReason
{
    Unspecified = 0,
    BudgetExceeded = 1,
    PolicyExcluded = 2,
    MissingEvidenceRef = 3,
    LowConfidenceReferenceOnly = 4,
    Duplicate = 5,
}

/// <summary>
/// 上下文来源规则，定义某类来源的优先级、投影模式和证据要求。
/// Context source rule defining priority, projection mode, and evidence requirements for a source kind.
/// </summary>
public sealed record ContextSourceRule
{
    public ContextSourceRule(
        ContextSourceKind sourceKind,
        int priority = 100,
        ContextProjectionMode projectionMode = ContextProjectionMode.Full,
        decimal minConfidence = 0,
        bool requireEvidenceRef = false,
        int maxTokens = 0)
    {
        if (sourceKind is ContextSourceKind.Unspecified)
        {
            throw new ArgumentException("Context source kind must be specified.", nameof(sourceKind));
        }

        SourceKind = sourceKind;
        Priority = KernelContractGuard.NonNegative(priority, nameof(priority));
        ProjectionMode = projectionMode is ContextProjectionMode.Unspecified
            ? ContextProjectionMode.Full
            : projectionMode;
        MinConfidence = minConfidence < 0 || minConfidence > 1
            ? throw new ArgumentOutOfRangeException(nameof(minConfidence), "置信度必须在 0 到 1 之间。")
            : minConfidence;
        RequireEvidenceRef = requireEvidenceRef;
        MaxTokens = KernelContractGuard.NonNegative(maxTokens, nameof(maxTokens));
    }

    public ContextSourceKind SourceKind { get; }

    public int Priority { get; }

    public ContextProjectionMode ProjectionMode { get; }

    public decimal MinConfidence { get; }

    public bool RequireEvidenceRef { get; }

    public int MaxTokens { get; }
}

/// <summary>
/// 上下文策略，描述可读取、裁切、摘要和引用化的输入边界。
/// Context policy describing read, trimming, summarization, and reference-only boundaries.
/// </summary>
public sealed record ContextPolicy
{
    public ContextPolicy(
        int maxInputTokens = 0,
        IReadOnlyList<string>? priorityRefs = null,
        IReadOnlyList<string>? allowedSourceKinds = null,
        bool preserveLatestUserCorrection = true,
        bool requireEvidenceRefs = true,
        MetadataBag? metadata = null,
        string? policyId = null,
        IReadOnlyList<ContextSourceRule>? sourceRules = null,
        ContextProjectionMode lowConfidenceMode = ContextProjectionMode.ReferenceOnly,
        bool failClosed = true)
    {
        MaxInputTokens = KernelContractGuard.NonNegative(maxInputTokens, nameof(maxInputTokens));
        PriorityRefs = KernelContractGuard.ListOrEmpty(priorityRefs);
        AllowedSourceKinds = KernelContractGuard.ListOrEmpty(allowedSourceKinds);
        PreserveLatestUserCorrection = preserveLatestUserCorrection;
        RequireEvidenceRefs = requireEvidenceRefs;
        Metadata = KernelContractGuard.MetadataOrEmpty(metadata);
        PolicyId = string.IsNullOrWhiteSpace(policyId) ? "context.policy.default" : policyId.Trim();
        SourceRules = KernelContractGuard.ListOrEmpty(sourceRules);
        LowConfidenceMode = lowConfidenceMode is ContextProjectionMode.Unspecified
            ? ContextProjectionMode.ReferenceOnly
            : lowConfidenceMode;
        FailClosed = failClosed;
    }

    public string PolicyId { get; }

    public int MaxInputTokens { get; }

    public IReadOnlyList<string> PriorityRefs { get; }

    public IReadOnlyList<string> AllowedSourceKinds { get; }

    public IReadOnlyList<ContextSourceRule> SourceRules { get; }

    public bool PreserveLatestUserCorrection { get; }

    public bool RequireEvidenceRefs { get; }

    public ContextProjectionMode LowConfidenceMode { get; }

    public bool FailClosed { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// Kernel 已批准的上下文策略信封，Execution Runtime 只能消费该类型。
/// Kernel-approved context policy envelope; Execution Runtime may only consume this type.
/// </summary>
public sealed record ApprovedContextPolicy
{
    public ApprovedContextPolicy(
        ContextPolicy policy,
        CoreIntentId sourceIntentId,
        StageGraphId sourceGraphId,
        StageId sourceStageId,
        KernelOperationId sourceKernelOperationId,
        DateTimeOffset? approvedAt = null,
        IReadOnlyList<string>? validationRefs = null)
    {
        Policy = KernelContractGuard.NotNull(policy, nameof(policy));
        SourceIntentId = sourceIntentId;
        SourceGraphId = sourceGraphId;
        SourceStageId = sourceStageId;
        SourceKernelOperationId = sourceKernelOperationId;
        ApprovedAt = approvedAt ?? DateTimeOffset.UtcNow;
        ValidationRefs = KernelContractGuard.ListOrEmpty(validationRefs);
    }

    public ContextPolicy Policy { get; }

    public CoreIntentId SourceIntentId { get; }

    public StageGraphId SourceGraphId { get; }

    public StageId SourceStageId { get; }

    public KernelOperationId SourceKernelOperationId { get; }

    public DateTimeOffset ApprovedAt { get; }

    public IReadOnlyList<string> ValidationRefs { get; }
}

/// <summary>
/// 候选上下文片段，进入 Execution Runtime 前必须仍保持 provider-neutral。
/// Candidate context segment that remains provider-neutral before entering Execution Runtime.
/// </summary>
public sealed record ContextSourceCandidate
{
    public ContextSourceCandidate(
        string segmentId,
        ContextSourceKind sourceKind,
        string content,
        int estimatedTokens,
        decimal confidence = 1,
        string? evidenceRef = null,
        string? artifactRef = null,
        bool isLatestUserCorrection = false,
        MetadataBag? metadata = null)
    {
        SegmentId = KernelContractGuard.RequiredText(segmentId, nameof(segmentId));
        if (sourceKind is ContextSourceKind.Unspecified)
        {
            throw new ArgumentException("Context source kind must be specified.", nameof(sourceKind));
        }

        SourceKind = sourceKind;
        Content = KernelContractGuard.RequiredText(content, nameof(content));
        EstimatedTokens = KernelContractGuard.NonNegative(estimatedTokens, nameof(estimatedTokens));
        Confidence = confidence < 0 || confidence > 1
            ? throw new ArgumentOutOfRangeException(nameof(confidence), "置信度必须在 0 到 1 之间。")
            : confidence;
        EvidenceRef = evidenceRef;
        ArtifactRef = artifactRef;
        IsLatestUserCorrection = isLatestUserCorrection;
        Metadata = KernelContractGuard.MetadataOrEmpty(metadata);
    }

    public string SegmentId { get; }

    public ContextSourceKind SourceKind { get; }

    public string Content { get; }

    public int EstimatedTokens { get; }

    public decimal Confidence { get; }

    public string? EvidenceRef { get; }

    public string? ArtifactRef { get; }

    public bool IsLatestUserCorrection { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// 已物化的上下文片段，记录投影模式和证据引用。
/// Materialized context segment that records projection mode and evidence references.
/// </summary>
public sealed record MaterializedContextSegment(
    string SegmentId,
    ContextSourceKind SourceKind,
    ContextProjectionMode ProjectionMode,
    int EstimatedTokens,
    string? EvidenceRef = null,
    string? ArtifactRef = null);

/// <summary>
/// 被排除的上下文片段，必须保留可诊断原因。
/// Dropped context segment that must retain a diagnostic reason.
/// </summary>
public sealed record DroppedContextSegment(
    string SegmentId,
    ContextSourceKind SourceKind,
    ContextDropReason Reason,
    int EstimatedTokens,
    string? EvidenceRef = null,
    string? ArtifactRef = null);

/// <summary>
/// ContextPolicy 执行报告，供 diagnostics、trace 和 Host projection 使用。
/// ContextPolicy application report used by diagnostics, trace, and Host projection.
/// </summary>
public sealed record ContextPolicyApplicationReport
{
    public ContextPolicyApplicationReport(
        string policyId,
        int maxInputTokens,
        int estimatedTotalTokens,
        int estimatedIncludedTokens,
        IReadOnlyList<MaterializedContextSegment>? includedSegments = null,
        IReadOnlyList<DroppedContextSegment>? droppedSegments = null)
    {
        PolicyId = KernelContractGuard.RequiredText(policyId, nameof(policyId));
        MaxInputTokens = KernelContractGuard.NonNegative(maxInputTokens, nameof(maxInputTokens));
        EstimatedTotalTokens = KernelContractGuard.NonNegative(estimatedTotalTokens, nameof(estimatedTotalTokens));
        EstimatedIncludedTokens = KernelContractGuard.NonNegative(estimatedIncludedTokens, nameof(estimatedIncludedTokens));
        IncludedSegments = KernelContractGuard.ListOrEmpty(includedSegments);
        DroppedSegments = KernelContractGuard.ListOrEmpty(droppedSegments);
    }

    public string PolicyId { get; }

    public int MaxInputTokens { get; }

    public int EstimatedTotalTokens { get; }

    public int EstimatedIncludedTokens { get; }

    public IReadOnlyList<MaterializedContextSegment> IncludedSegments { get; }

    public IReadOnlyList<DroppedContextSegment> DroppedSegments { get; }
}

/// <summary>
/// Checkpoint 规则。
/// Checkpoint rules.
/// </summary>
public sealed record CheckpointRules
{
    public CheckpointRules(bool enabled = false, IReadOnlyList<StageId>? requiredStageIds = null, string? materializationPolicy = null)
    {
        Enabled = enabled;
        RequiredStageIds = KernelContractGuard.ListOrEmpty(requiredStageIds);
        MaterializationPolicy = materializationPolicy;
    }

    public bool Enabled { get; }

    public IReadOnlyList<StageId> RequiredStageIds { get; }

    public string? MaterializationPolicy { get; }
}

/// <summary>
/// 恢复规则。
/// Recovery rules.
/// </summary>
public sealed record RecoveryRules
{
    public RecoveryRules(bool enabled = false, int maxRecoveryAttempts = 0, IReadOnlyList<string>? allowedRecoveryKinds = null)
    {
        Enabled = enabled;
        MaxRecoveryAttempts = KernelContractGuard.NonNegative(maxRecoveryAttempts, nameof(maxRecoveryAttempts));
        AllowedRecoveryKinds = KernelContractGuard.ListOrEmpty(allowedRecoveryKinds);
    }

    public bool Enabled { get; }

    public int MaxRecoveryAttempts { get; }

    public IReadOnlyList<string> AllowedRecoveryKinds { get; }
}

/// <summary>
/// 评估规则。
/// Evaluation rules.
/// </summary>
public sealed record EvaluationRules
{
    public EvaluationRules(bool enabled = false, IReadOnlyList<string>? metricIds = null, bool requireTrace = true)
    {
        Enabled = enabled;
        MetricIds = KernelContractGuard.ListOrEmpty(metricIds);
        RequireTrace = requireTrace;
    }

    public bool Enabled { get; }

    public IReadOnlyList<string> MetricIds { get; }

    public bool RequireTrace { get; }
}

/// <summary>
/// 风险画像，默认需要人工 gate。
/// Risk profile; defaults to requiring a human gate.
/// </summary>
public sealed record RiskProfile
{
    public RiskProfile(string riskLevel = "unknown", IReadOnlyList<string>? riskRefs = null, bool requiresHumanGate = true)
    {
        RiskLevel = KernelContractGuard.RequiredText(riskLevel, nameof(riskLevel));
        RiskRefs = KernelContractGuard.ListOrEmpty(riskRefs);
        RequiresHumanGate = requiresHumanGate;
    }

    public string RiskLevel { get; }

    public IReadOnlyList<string> RiskRefs { get; }

    public bool RequiresHumanGate { get; }
}

/// <summary>
/// Kernel 预算影响。
/// Kernel budget impact.
/// </summary>
public sealed record KernelBudgetImpact
{
    public KernelBudgetImpact(KernelBudget? requestedBudget = null, string? reason = null)
    {
        RequestedBudget = requestedBudget ?? KernelBudget.Zero;
        Reason = reason;
    }

    public KernelBudget RequestedBudget { get; }

    public string? Reason { get; }
}

/// <summary>
/// 回滚计划引用。
/// Rollback-plan reference.
/// </summary>
public sealed record RollbackPlan
{
    public RollbackPlan(string planRef, bool reversible = false, IReadOnlyList<string>? rollbackStepRefs = null)
    {
        PlanRef = KernelContractGuard.RequiredText(planRef, nameof(planRef));
        Reversible = reversible;
        RollbackStepRefs = KernelContractGuard.ListOrEmpty(rollbackStepRefs);
    }

    public string PlanRef { get; }

    public bool Reversible { get; }

    public IReadOnlyList<string> RollbackStepRefs { get; }
}

/// <summary>
/// 评估计划引用。
/// Evaluation-plan reference.
/// </summary>
public sealed record EvaluationPlan
{
    public EvaluationPlan(string planRef, IReadOnlyList<string>? metricIds = null, bool requireReplay = true)
    {
        PlanRef = KernelContractGuard.RequiredText(planRef, nameof(planRef));
        MetricIds = KernelContractGuard.ListOrEmpty(metricIds);
        RequireReplay = requireReplay;
    }

    public string PlanRef { get; }

    public IReadOnlyList<string> MetricIds { get; }

    public bool RequireReplay { get; }
}
