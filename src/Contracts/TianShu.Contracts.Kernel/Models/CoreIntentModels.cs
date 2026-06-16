using TianShu.Contracts.Governance;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Kernel;

/// <summary>
/// Kernel 可处理的核心意图种类。
/// Core-intent kinds accepted by Kernel.
/// </summary>
public enum CoreIntentKind
{
    Unspecified = 0,
    Turn = 1,
    Resume = 2,
    Interrupt = 3,
    Review = 4,
    Compaction = 5,
    Recovery = 6,
    Evaluation = 7,
}

/// <summary>
/// Kernel 操作对象引用，只保存稳定标识，不嵌入宿主对象。
/// Kernel subject reference carrying stable identifiers without embedding host objects.
/// </summary>
public sealed record KernelSubjectRef
{
    public KernelSubjectRef(SessionId sessionId, ThreadId threadId, WorkflowId? workflowId = null, TurnId? turnId = null)
    {
        SessionId = sessionId;
        ThreadId = threadId;
        WorkflowId = workflowId;
        TurnId = turnId;
    }

    public SessionId SessionId { get; }

    public ThreadId ThreadId { get; }

    public WorkflowId? WorkflowId { get; }

    public TurnId? TurnId { get; }
}

/// <summary>
/// Kernel 治理信封，表达本次意图允许的策略、模块、工具和副作用上限。
/// Kernel governance envelope that describes allowed policies, modules, tools, and side-effect ceiling.
/// </summary>
public sealed record GovernanceEnvelope
{
    public GovernanceEnvelope(
        string envelopeId,
        IReadOnlyList<string>? policyIds = null,
        IReadOnlyList<string>? allowedToolIds = null,
        IReadOnlyList<string>? allowedModuleIds = null,
        SideEffectLevel maxSideEffectLevel = SideEffectLevel.Unspecified,
        bool requiresHumanGate = true,
        IReadOnlyList<ApprovalId>? approvalIds = null,
        IReadOnlyList<AuditRecordId>? auditRecordIds = null,
        IReadOnlyList<PolicyDecision>? policyDecisions = null)
    {
        EnvelopeId = KernelContractGuard.RequiredText(envelopeId, nameof(envelopeId));
        PolicyIds = KernelContractGuard.ListOrEmpty(policyIds);
        AllowedToolIds = KernelContractGuard.ListOrEmpty(allowedToolIds);
        AllowedModuleIds = KernelContractGuard.ListOrEmpty(allowedModuleIds);
        MaxSideEffectLevel = maxSideEffectLevel;
        RequiresHumanGate = requiresHumanGate;
        ApprovalIds = KernelContractGuard.ListOrEmpty(approvalIds);
        AuditRecordIds = KernelContractGuard.ListOrEmpty(auditRecordIds);
        PolicyDecisions = KernelContractGuard.ListOrEmpty(policyDecisions);
    }

    public string EnvelopeId { get; }

    public IReadOnlyList<string> PolicyIds { get; }

    public IReadOnlyList<string> AllowedToolIds { get; }

    public IReadOnlyList<string> AllowedModuleIds { get; }

    public SideEffectLevel MaxSideEffectLevel { get; }

    public bool RequiresHumanGate { get; }

    public IReadOnlyList<ApprovalId> ApprovalIds { get; }

    public IReadOnlyList<AuditRecordId> AuditRecordIds { get; }

    public IReadOnlyList<PolicyDecision> PolicyDecisions { get; }
}

/// <summary>
/// Kernel 预算对象，默认零预算以便验证器 fail closed。
/// Kernel budget object; defaults to zero budget so validators can fail closed.
/// </summary>
public sealed record KernelBudget
{
    public KernelBudget(
        int tokenBudget = 0,
        long timeBudgetMs = 0,
        decimal costBudget = 0,
        int retryBudget = 0,
        int toolCallBudget = 0)
    {
        TokenBudget = KernelContractGuard.NonNegative(tokenBudget, nameof(tokenBudget));
        TimeBudgetMs = KernelContractGuard.NonNegative(timeBudgetMs, nameof(timeBudgetMs));
        CostBudget = KernelContractGuard.NonNegative(costBudget, nameof(costBudget));
        RetryBudget = KernelContractGuard.NonNegative(retryBudget, nameof(retryBudget));
        ToolCallBudget = KernelContractGuard.NonNegative(toolCallBudget, nameof(toolCallBudget));
    }

    public int TokenBudget { get; }

    public long TimeBudgetMs { get; }

    public decimal CostBudget { get; }

    public int RetryBudget { get; }

    public int ToolCallBudget { get; }

    public static KernelBudget Zero { get; } = new();
}

/// <summary>
/// Control Plane 传给 Kernel 的归一化核心意图基类。
/// Base normalized core intent passed from Control Plane to Kernel.
/// </summary>
public abstract record CoreIntent
{
    protected CoreIntent(
        CoreIntentId intentId,
        CoreIntentKind intentKind,
        KernelSubjectRef subject,
        GovernanceEnvelope governance,
        KernelBudget? budget = null,
        MetadataBag? metadata = null)
    {
        IntentId = intentId;
        IntentKind = intentKind;
        Subject = KernelContractGuard.NotNull(subject, nameof(subject));
        Governance = KernelContractGuard.NotNull(governance, nameof(governance));
        Budget = budget ?? KernelBudget.Zero;
        Metadata = KernelContractGuard.MetadataOrEmpty(metadata);
    }

    public CoreIntentId IntentId { get; }

    public CoreIntentKind IntentKind { get; }

    public KernelSubjectRef Subject { get; }

    public GovernanceEnvelope Governance { get; }

    public KernelBudget Budget { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// 用户 turn 意图。
/// User-turn intent.
/// </summary>
public sealed record TurnIntent : CoreIntent
{
    public TurnIntent(
        CoreIntentId intentId,
        KernelSubjectRef subject,
        GovernanceEnvelope governance,
        string userInputRef,
        KernelBudget? budget = null,
        MetadataBag? metadata = null)
        : base(intentId, CoreIntentKind.Turn, subject, governance, budget, metadata)
    {
        UserInputRef = KernelContractGuard.RequiredText(userInputRef, nameof(userInputRef));
    }

    public string UserInputRef { get; }
}

/// <summary>
/// 恢复执行意图。
/// Resume intent.
/// </summary>
public sealed record ResumeIntent : CoreIntent
{
    public ResumeIntent(
        CoreIntentId intentId,
        KernelSubjectRef subject,
        GovernanceEnvelope governance,
        string resumeToken,
        string checkpointRef,
        KernelBudget? budget = null,
        MetadataBag? metadata = null)
        : base(intentId, CoreIntentKind.Resume, subject, governance, budget, metadata)
    {
        ResumeToken = KernelContractGuard.RequiredText(resumeToken, nameof(resumeToken));
        CheckpointRef = KernelContractGuard.RequiredText(checkpointRef, nameof(checkpointRef));
    }

    public string ResumeToken { get; }

    public string CheckpointRef { get; }
}

/// <summary>
/// 中断执行意图。
/// Interrupt intent.
/// </summary>
public sealed record InterruptIntent : CoreIntent
{
    public InterruptIntent(
        CoreIntentId intentId,
        KernelSubjectRef subject,
        GovernanceEnvelope governance,
        string interruptReason,
        KernelBudget? budget = null,
        MetadataBag? metadata = null)
        : base(intentId, CoreIntentKind.Interrupt, subject, governance, budget, metadata)
    {
        InterruptReason = KernelContractGuard.RequiredText(interruptReason, nameof(interruptReason));
    }

    public string InterruptReason { get; }
}

/// <summary>
/// 审查意图，用于请求 Kernel 审查 proposal、operation 或 run。
/// Review intent used to request Kernel review of a proposal, operation, or run.
/// </summary>
public sealed record ReviewIntent : CoreIntent
{
    public ReviewIntent(
        CoreIntentId intentId,
        KernelSubjectRef subject,
        GovernanceEnvelope governance,
        string reviewTargetRef,
        KernelBudget? budget = null,
        MetadataBag? metadata = null)
        : base(intentId, CoreIntentKind.Review, subject, governance, budget, metadata)
    {
        ReviewTargetRef = KernelContractGuard.RequiredText(reviewTargetRef, nameof(reviewTargetRef));
    }

    public string ReviewTargetRef { get; }
}

/// <summary>
/// 上下文压缩意图。
/// Compaction intent.
/// </summary>
public sealed record CompactionIntent : CoreIntent
{
    public CompactionIntent(
        CoreIntentId intentId,
        KernelSubjectRef subject,
        GovernanceEnvelope governance,
        string contextScopeRef,
        KernelBudget? budget = null,
        MetadataBag? metadata = null)
        : base(intentId, CoreIntentKind.Compaction, subject, governance, budget, metadata)
    {
        ContextScopeRef = KernelContractGuard.RequiredText(contextScopeRef, nameof(contextScopeRef));
    }

    public string ContextScopeRef { get; }
}

/// <summary>
/// 失败恢复意图。
/// Failure-recovery intent.
/// </summary>
public sealed record RecoveryIntent : CoreIntent
{
    public RecoveryIntent(
        CoreIntentId intentId,
        KernelSubjectRef subject,
        GovernanceEnvelope governance,
        KernelRunId failedRunId,
        StageId failedStageId,
        string errorSignalRef,
        KernelBudget? budget = null,
        MetadataBag? metadata = null)
        : base(intentId, CoreIntentKind.Recovery, subject, governance, budget, metadata)
    {
        FailedRunId = failedRunId;
        FailedStageId = failedStageId;
        ErrorSignalRef = KernelContractGuard.RequiredText(errorSignalRef, nameof(errorSignalRef));
    }

    public KernelRunId FailedRunId { get; }

    public StageId FailedStageId { get; }

    public string ErrorSignalRef { get; }
}

/// <summary>
/// 评估意图。
/// Evaluation intent.
/// </summary>
public sealed record EvaluationIntent : CoreIntent
{
    public EvaluationIntent(
        CoreIntentId intentId,
        KernelSubjectRef subject,
        GovernanceEnvelope governance,
        KernelRunId runId,
        KernelBudget? budget = null,
        MetadataBag? metadata = null)
        : base(intentId, CoreIntentKind.Evaluation, subject, governance, budget, metadata)
    {
        RunId = runId;
    }

    public KernelRunId RunId { get; }
}
