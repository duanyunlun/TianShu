using TianShu.Contracts.Kernel;
using TianShu.Contracts.Modules;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Agents;

/// <summary>
/// 父 turn 发起的一次受治理 sub-agent spawn 请求；不包含 admission 后才生成的子血缘。
/// Governed sub-agent spawn request issued by a parent turn; it does not carry the child lineage generated after admission.
/// </summary>
public sealed record SubAgentSpawnRequest
{
    public SubAgentSpawnRequest(
        string spawnCallId,
        SubAgentLineage parentLineage,
        KernelSubjectRef parentSubject,
        string taskBrief,
        IReadOnlyList<string>? evidenceRefs,
        GovernanceEnvelope requestedGovernance,
        KernelBudget requestedBudget,
        bool requiresHumanGate,
        MetadataBag? metadata = null)
    {
        SpawnCallId = IdentifierGuard.AgainstNullOrWhiteSpace(spawnCallId, nameof(spawnCallId));
        ParentLineage = parentLineage ?? throw new ArgumentNullException(nameof(parentLineage));
        ParentSubject = parentSubject ?? throw new ArgumentNullException(nameof(parentSubject));
        TaskBrief = IdentifierGuard.AgainstNullOrWhiteSpace(taskBrief, nameof(taskBrief));
        EvidenceRefs = evidenceRefs ?? Array.Empty<string>();
        RequestedGovernance = requestedGovernance ?? throw new ArgumentNullException(nameof(requestedGovernance));
        RequestedBudget = requestedBudget ?? throw new ArgumentNullException(nameof(requestedBudget));
        RequiresHumanGate = requiresHumanGate;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string SpawnCallId { get; }

    public SubAgentLineage ParentLineage { get; }

    public KernelSubjectRef ParentSubject { get; }

    public string TaskBrief { get; }

    public IReadOnlyList<string> EvidenceRefs { get; }

    public GovernanceEnvelope RequestedGovernance { get; }

    public KernelBudget RequestedBudget { get; }

    public bool RequiresHumanGate { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// 不可变 sub-agent 血缘链；用于结构闸门 admission 与整树复盘。
/// Immutable sub-agent lineage used for structural-gate admission and tree replay.
/// </summary>
public sealed record SubAgentLineage
{
    public SubAgentLineage(
        string rootRunId,
        string currentRunId,
        string? parentRunId,
        int depth,
        int siblingIndex,
        string ledgerRef)
    {
        if (depth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(depth), "子代理深度不能为负。");
        }

        if (siblingIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(siblingIndex), "子代理同级序号不能为负。");
        }

        RootRunId = IdentifierGuard.AgainstNullOrWhiteSpace(rootRunId, nameof(rootRunId));
        CurrentRunId = IdentifierGuard.AgainstNullOrWhiteSpace(currentRunId, nameof(currentRunId));
        ParentRunId = string.IsNullOrWhiteSpace(parentRunId) ? null : parentRunId;
        Depth = depth;
        SiblingIndex = siblingIndex;
        LedgerRef = IdentifierGuard.AgainstNullOrWhiteSpace(ledgerRef, nameof(ledgerRef));
    }

    public string RootRunId { get; }

    public string CurrentRunId { get; }

    public string? ParentRunId { get; }

    public int Depth { get; }

    public int SiblingIndex { get; }

    public string LedgerRef { get; }

    public SubAgentLineage Descend(string childRunId, int siblingIndex)
        => new(
            RootRunId,
            IdentifierGuard.AgainstNullOrWhiteSpace(childRunId, nameof(childRunId)),
            CurrentRunId,
            Depth + 1,
            siblingIndex,
            LedgerRef);
}

/// <summary>
/// sub-agent 树级结构配额；单调计数与并发计数由 RuntimeComposition ledger 执行，0 表示关闭并发记账，负数非法。
/// Tree-level sub-agent structural quota; monotonic and concurrent counters are enforced by the RuntimeComposition ledger; 0 disables concurrent accounting and negative values are invalid.
/// </summary>
public sealed record SubAgentSpawnQuota
{
    public SubAgentSpawnQuota(int maxSpawnDepth, int maxFanoutPerAgent, int maxTreeNodes, int maxConcurrentAgents)
    {
        if (maxSpawnDepth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSpawnDepth), "最大 spawn 深度必须为正。");
        }

        if (maxFanoutPerAgent < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFanoutPerAgent), "最大扇出不能为负。");
        }

        if (maxTreeNodes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTreeNodes), "整棵树节点上限必须至少包含根节点。");
        }

        if (maxConcurrentAgents < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentAgents), "最大并发子代理数不能为负。");
        }

        MaxSpawnDepth = maxSpawnDepth;
        MaxFanoutPerAgent = maxFanoutPerAgent;
        MaxTreeNodes = maxTreeNodes;
        MaxConcurrentAgents = maxConcurrentAgents;
    }

    public int MaxSpawnDepth { get; }

    public int MaxFanoutPerAgent { get; }

    public int MaxTreeNodes { get; }

    public int MaxConcurrentAgents { get; }
}

/// <summary>
/// 父 turn 在一次 fanout 中请求的一组 sub-agent 子任务。
/// Set of sub-agent child tasks requested by a parent turn in one fan-out operation.
/// </summary>
public sealed record SubAgentFanoutRequest
{
    public SubAgentFanoutRequest(
        string fanoutCallId,
        SubAgentLineage parentLineage,
        KernelSubjectRef parentSubject,
        IReadOnlyList<SubAgentFanoutItem> items,
        SubAgentFanoutPolicy policy,
        SubAgentBudgetSplit budgetSplit,
        GovernanceEnvelope requestedGovernance,
        MetadataBag? metadata = null)
    {
        FanoutCallId = IdentifierGuard.AgainstNullOrWhiteSpace(fanoutCallId, nameof(fanoutCallId));
        ParentLineage = parentLineage ?? throw new ArgumentNullException(nameof(parentLineage));
        ParentSubject = parentSubject ?? throw new ArgumentNullException(nameof(parentSubject));
        Items = items is { Count: > 0 }
            ? items
            : throw new ArgumentException("fanout 至少需要一个子任务。", nameof(items));
        Policy = policy ?? throw new ArgumentNullException(nameof(policy));
        BudgetSplit = budgetSplit ?? throw new ArgumentNullException(nameof(budgetSplit));
        RequestedGovernance = requestedGovernance ?? throw new ArgumentNullException(nameof(requestedGovernance));
        Metadata = metadata ?? MetadataBag.Empty;

        if (Items.Count > Policy.MaxSubTasks)
        {
            throw new ArgumentOutOfRangeException(nameof(items), "fanout 子任务数超过策略上限。");
        }

        var duplicateItemId = Items.GroupBy(static item => item.ItemId, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1)
            ?.Key;
        if (!string.IsNullOrWhiteSpace(duplicateItemId))
        {
            throw new ArgumentException($"fanout 子任务存在重复 itemId：{duplicateItemId}。", nameof(items));
        }
    }

    public string FanoutCallId { get; }

    public SubAgentLineage ParentLineage { get; }

    public KernelSubjectRef ParentSubject { get; }

    public IReadOnlyList<SubAgentFanoutItem> Items { get; }

    public SubAgentFanoutPolicy Policy { get; }

    public SubAgentBudgetSplit BudgetSplit { get; }

    public GovernanceEnvelope RequestedGovernance { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// fanout 中单个可独立执行的 sub-agent 子任务。
/// One independently executable sub-agent child task inside a fan-out request.
/// </summary>
public sealed record SubAgentFanoutItem
{
    public SubAgentFanoutItem(
        string itemId,
        string taskBrief,
        IReadOnlyList<string>? evidenceRefs = null,
        GovernanceEnvelope? requestedGovernanceOverride = null,
        KernelBudget? requestedBudgetOverride = null,
        MetadataBag? metadata = null)
    {
        ItemId = IdentifierGuard.AgainstNullOrWhiteSpace(itemId, nameof(itemId));
        TaskBrief = IdentifierGuard.AgainstNullOrWhiteSpace(taskBrief, nameof(taskBrief));
        EvidenceRefs = evidenceRefs ?? Array.Empty<string>();
        RequestedGovernanceOverride = requestedGovernanceOverride;
        RequestedBudgetOverride = requestedBudgetOverride;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string ItemId { get; }

    public string TaskBrief { get; }

    public IReadOnlyList<string> EvidenceRefs { get; }

    public GovernanceEnvelope? RequestedGovernanceOverride { get; }

    public KernelBudget? RequestedBudgetOverride { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// sub-agent fanout 调度策略；P29.2 只定义 admission 闸门，P29.3 调度器必须消费该策略。
/// Sub-agent fan-out scheduling policy; P29.2 only defines admission gates and the P29.3 scheduler must consume this policy.
/// </summary>
public sealed record SubAgentFanoutPolicy
{
    public SubAgentFanoutPolicy(
        int maxConcurrentAgents,
        int maxSubTasks,
        TimeSpan itemTimeout,
        SubAgentFailureMode failureMode = SubAgentFailureMode.ContinueOnFailure,
        bool requireAllItemsToReport = true)
    {
        if (maxConcurrentAgents < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentAgents), "最大并发子代理数不能为负。");
        }

        if (maxSubTasks < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSubTasks), "最大子任务数必须为正。");
        }

        if (itemTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(itemTimeout), "子任务超时时间必须为正。");
        }

        if (!Enum.IsDefined(failureMode) || failureMode == SubAgentFailureMode.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(failureMode), "子任务失败模式必须是明确值。");
        }

        MaxConcurrentAgents = maxConcurrentAgents;
        MaxSubTasks = maxSubTasks;
        ItemTimeout = itemTimeout;
        FailureMode = failureMode;
        RequireAllItemsToReport = requireAllItemsToReport;
    }

    public int MaxConcurrentAgents { get; }

    public int MaxSubTasks { get; }

    public TimeSpan ItemTimeout { get; }

    public SubAgentFailureMode FailureMode { get; }

    public bool RequireAllItemsToReport { get; }
}

public enum SubAgentFailureMode
{
    Unspecified = 0,
    ContinueOnFailure = 1,
    CancelSiblingsOnCriticalFailure = 2,
}

/// <summary>
/// sub-agent 整树预算上限；结构闸门限制树形，预算闸门限制资源消耗。
/// Whole-tree sub-agent budget ceiling; structural gates limit shape while budget gates limit resource consumption.
/// </summary>
public sealed record SubAgentTreeBudget
{
    public SubAgentTreeBudget(
        KernelBudget rootBudget,
        int maxSubTasks,
        int maxDepth,
        int maxConcurrentAgents,
        KernelBudget? maxBudgetPerAgent = null,
        decimal? maxCost = null,
        MetadataBag? metadata = null)
    {
        if (maxSubTasks < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSubTasks), "最大子任务数必须为正。");
        }

        if (maxDepth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDepth), "最大子代理深度必须为正。");
        }

        if (maxConcurrentAgents < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentAgents), "最大并发子代理数不能为负。");
        }

        if (maxCost is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCost), "整树最大成本不能为负。");
        }

        if (maxBudgetPerAgent is not null && !HasAnyPositiveBudget(maxBudgetPerAgent))
        {
            throw new ArgumentOutOfRangeException(nameof(maxBudgetPerAgent), "每 agent 预算上限必须至少包含一个正预算维度。");
        }

        RootBudget = rootBudget ?? throw new ArgumentNullException(nameof(rootBudget));
        MaxSubTasks = maxSubTasks;
        MaxDepth = maxDepth;
        MaxConcurrentAgents = maxConcurrentAgents;
        MaxBudgetPerAgent = maxBudgetPerAgent;
        MaxCost = maxCost;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public KernelBudget RootBudget { get; }

    public int MaxSubTasks { get; }

    public int MaxDepth { get; }

    public int MaxConcurrentAgents { get; }

    public KernelBudget? MaxBudgetPerAgent { get; }

    public decimal? MaxCost { get; }

    public MetadataBag Metadata { get; }

    private static bool HasAnyPositiveBudget(KernelBudget budget)
        => budget.TokenBudget > 0
           || budget.TimeBudgetMs > 0
           || budget.CostBudget > 0
           || budget.RetryBudget > 0
           || budget.ToolCallBudget > 0;
}

/// <summary>
/// sub-agent 子预算拆分策略；显式预算和自动拆分都必须受父剩余预算与每 agent 上限约束。
/// Sub-agent child-budget split strategy; explicit and automatic splits must stay within parent remaining budget and per-agent ceilings.
/// </summary>
public sealed record SubAgentBudgetSplit
{
    public SubAgentBudgetSplit(
        SubAgentBudgetSplitMode mode,
        int? maxTokensPerAgent = null,
        decimal? maxCostPerAgent = null,
        TimeSpan? maxTimePerAgent = null,
        int? maxToolCallsPerAgent = null,
        int? maxRetriesPerAgent = null)
    {
        if (!Enum.IsDefined(mode) || mode == SubAgentBudgetSplitMode.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(mode), "预算拆分模式必须是明确值。");
        }

        if (maxTokensPerAgent is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTokensPerAgent), "每 agent token 上限必须为正。");
        }

        if (maxCostPerAgent is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCostPerAgent), "每 agent 成本上限必须为正。");
        }

        if (maxTimePerAgent is not null && maxTimePerAgent <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTimePerAgent), "每 agent 时间上限必须为正。");
        }

        if (maxToolCallsPerAgent is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxToolCallsPerAgent), "每 agent tool call 上限必须为正。");
        }

        if (maxRetriesPerAgent is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRetriesPerAgent), "每 agent retry 上限必须为正。");
        }

        Mode = mode;
        MaxTokensPerAgent = maxTokensPerAgent;
        MaxCostPerAgent = maxCostPerAgent;
        MaxTimePerAgent = maxTimePerAgent;
        MaxToolCallsPerAgent = maxToolCallsPerAgent;
        MaxRetriesPerAgent = maxRetriesPerAgent;
    }

    public SubAgentBudgetSplitMode Mode { get; }

    public int? MaxTokensPerAgent { get; }

    public decimal? MaxCostPerAgent { get; }

    public TimeSpan? MaxTimePerAgent { get; }

    public int? MaxToolCallsPerAgent { get; }

    public int? MaxRetriesPerAgent { get; }
}

public enum SubAgentBudgetSplitMode
{
    Unspecified = 0,
    EqualShare = 1,
    ExplicitPerItem = 2,
    ConservativeMinimum = 3,
}

/// <summary>
/// 子 run 终态投影，作为 tool result 回流父 turn。
/// Child-run terminal projection returned to the parent turn as a tool result.
/// </summary>
public sealed record SubAgentRunResult
{
    public SubAgentRunResult(
        string spawnCallId,
        string childRunId,
        string childThreadId,
        SubAgentRunStatus status,
        string? resultText = null,
        string? replaySummaryRef = null,
        IReadOnlyList<string>? diagnosticsRefs = null,
        SubAgentFailure? failure = null,
        IReadOnlyList<string>? artifactRefs = null)
    {
        SpawnCallId = IdentifierGuard.AgainstNullOrWhiteSpace(spawnCallId, nameof(spawnCallId));
        ChildRunId = IdentifierGuard.AgainstNullOrWhiteSpace(childRunId, nameof(childRunId));
        ChildThreadId = IdentifierGuard.AgainstNullOrWhiteSpace(childThreadId, nameof(childThreadId));
        Status = status;
        ResultText = resultText;
        ReplaySummaryRef = replaySummaryRef;
        DiagnosticsRefs = diagnosticsRefs ?? Array.Empty<string>();
        Failure = failure;
        ArtifactRefs = artifactRefs ?? Array.Empty<string>();
    }

    public string SpawnCallId { get; }

    public string ChildRunId { get; }

    public string ChildThreadId { get; }

    public SubAgentRunStatus Status { get; }

    public string? ResultText { get; }

    public string? ReplaySummaryRef { get; }

    public IReadOnlyList<string> DiagnosticsRefs { get; }

    public SubAgentFailure? Failure { get; }

    public IReadOnlyList<string> ArtifactRefs { get; }
}

public enum SubAgentRunStatus
{
    Unspecified = 0,
    Completed = 1,
    Failed = 2,
    Blocked = 3,
    Cancelled = 4,
}

/// <summary>
/// 父 run 对一次 sub-agent fanout 的结构化归并结果；模型裁判只能附加到 Metadata，不能替代确定性状态与冲突。
/// Structured parent-run fan-in summary for one sub-agent fanout; model review can only be attached to Metadata and cannot replace deterministic status and conflicts.
/// </summary>
public sealed record SubAgentFanInSummary
{
    public SubAgentFanInSummary(
        string fanoutCallId,
        SubAgentFanInStatus status,
        IReadOnlyList<SubAgentRunResult> results,
        IReadOnlyList<SubAgentConflict>? conflicts = null,
        IReadOnlyList<string>? evidenceRefs = null,
        IReadOnlyList<string>? diagnosticsRefs = null,
        string? summaryText = null,
        MetadataBag? metadata = null)
    {
        FanoutCallId = IdentifierGuard.AgainstNullOrWhiteSpace(fanoutCallId, nameof(fanoutCallId));
        if (!Enum.IsDefined(status) || status == SubAgentFanInStatus.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(status), "fan-in 状态必须是明确值。");
        }

        Results = results is { Count: > 0 }
            ? results
            : throw new ArgumentException("fan-in 必须保留至少一个子任务结果。", nameof(results));
        Status = status;
        Conflicts = conflicts ?? Array.Empty<SubAgentConflict>();
        EvidenceRefs = evidenceRefs ?? Array.Empty<string>();
        DiagnosticsRefs = diagnosticsRefs ?? Array.Empty<string>();
        SummaryText = summaryText;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string FanoutCallId { get; }

    public SubAgentFanInStatus Status { get; }

    public IReadOnlyList<SubAgentRunResult> Results { get; }

    public IReadOnlyList<SubAgentConflict> Conflicts { get; }

    public IReadOnlyList<string> EvidenceRefs { get; }

    public IReadOnlyList<string> DiagnosticsRefs { get; }

    public string? SummaryText { get; }

    public MetadataBag Metadata { get; }
}

public enum SubAgentFanInStatus
{
    Unspecified = 0,
    Completed = 1,
    CompletedWithFailures = 2,
    Blocked = 3,
    Cancelled = 4,
}

/// <summary>
/// fan-in 阶段由确定性规则识别出的子结果冲突。
/// Conflict between child results identified by deterministic fan-in rules.
/// </summary>
public sealed record SubAgentConflict
{
    public SubAgentConflict(
        string conflictId,
        IReadOnlyList<string> childRunIds,
        string conflictKind,
        string summary,
        IReadOnlyList<string>? evidenceRefs = null)
    {
        ConflictId = IdentifierGuard.AgainstNullOrWhiteSpace(conflictId, nameof(conflictId));
        ChildRunIds = childRunIds is { Count: > 0 }
            ? childRunIds
            : throw new ArgumentException("冲突必须引用至少一个子 run。", nameof(childRunIds));
        ConflictKind = IdentifierGuard.AgainstNullOrWhiteSpace(conflictKind, nameof(conflictKind));
        Summary = IdentifierGuard.AgainstNullOrWhiteSpace(summary, nameof(summary));
        EvidenceRefs = evidenceRefs ?? Array.Empty<string>();
    }

    public string ConflictId { get; }

    public IReadOnlyList<string> ChildRunIds { get; }

    public string ConflictKind { get; }

    public string Summary { get; }

    public IReadOnlyList<string> EvidenceRefs { get; }
}

/// <summary>
/// sub-agent 整树诊断投影；用于从根 run 复盘 fanout、fan-in、失败、artifact 与审计引用。
/// Whole-tree sub-agent diagnostics projection used to replay fan-out, fan-in, failures, artifacts, and audit references from the root run.
/// </summary>
public sealed record SubAgentTreeDiagnostics
{
    public SubAgentTreeDiagnostics(
        string rootRunId,
        string ledgerRef,
        SubAgentSpawnQuota quota,
        SubAgentTreeBudget budget,
        IReadOnlyList<SubAgentNodeDiagnostics> nodes,
        IReadOnlyList<SubAgentEdgeDiagnostics> edges,
        IReadOnlyList<string>? replayRefs = null,
        IReadOnlyList<string>? auditRefs = null,
        string? reportText = null,
        MetadataBag? metadata = null)
    {
        RootRunId = IdentifierGuard.AgainstNullOrWhiteSpace(rootRunId, nameof(rootRunId));
        LedgerRef = IdentifierGuard.AgainstNullOrWhiteSpace(ledgerRef, nameof(ledgerRef));
        Quota = quota ?? throw new ArgumentNullException(nameof(quota));
        Budget = budget ?? throw new ArgumentNullException(nameof(budget));
        Nodes = nodes is { Count: > 0 }
            ? nodes
            : throw new ArgumentException("sub-agent tree diagnostics 必须至少包含根节点。", nameof(nodes));
        Edges = edges ?? Array.Empty<SubAgentEdgeDiagnostics>();
        ReplayRefs = replayRefs ?? Array.Empty<string>();
        AuditRefs = auditRefs ?? Array.Empty<string>();
        ReportText = reportText;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string RootRunId { get; }

    public string LedgerRef { get; }

    public SubAgentSpawnQuota Quota { get; }

    public SubAgentTreeBudget Budget { get; }

    public IReadOnlyList<SubAgentNodeDiagnostics> Nodes { get; }

    public IReadOnlyList<SubAgentEdgeDiagnostics> Edges { get; }

    public IReadOnlyList<string> ReplayRefs { get; }

    public IReadOnlyList<string> AuditRefs { get; }

    public string? ReportText { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// sub-agent 树中一个 run 节点的诊断摘要。
/// Diagnostic summary for one run node in a sub-agent tree.
/// </summary>
public sealed record SubAgentNodeDiagnostics
{
    public SubAgentNodeDiagnostics(
        string runId,
        string? parentRunId,
        int depth,
        int siblingIndex,
        SubAgentRunStatus status,
        string? replaySummaryRef = null,
        IReadOnlyList<string>? diagnosticsRefs = null,
        IReadOnlyList<string>? artifactRefs = null,
        SubAgentFailure? failure = null)
    {
        if (depth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(depth), "sub-agent node depth 不能为负。");
        }

        if (siblingIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(siblingIndex), "sub-agent node siblingIndex 不能为负。");
        }

        if (!Enum.IsDefined(status) || status == SubAgentRunStatus.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(status), "sub-agent node 状态必须是明确值。");
        }

        RunId = IdentifierGuard.AgainstNullOrWhiteSpace(runId, nameof(runId));
        ParentRunId = string.IsNullOrWhiteSpace(parentRunId) ? null : parentRunId;
        Depth = depth;
        SiblingIndex = siblingIndex;
        Status = status;
        ReplaySummaryRef = replaySummaryRef;
        DiagnosticsRefs = diagnosticsRefs ?? Array.Empty<string>();
        ArtifactRefs = artifactRefs ?? Array.Empty<string>();
        Failure = failure;
    }

    public string RunId { get; }

    public string? ParentRunId { get; }

    public int Depth { get; }

    public int SiblingIndex { get; }

    public SubAgentRunStatus Status { get; }

    public string? ReplaySummaryRef { get; }

    public IReadOnlyList<string> DiagnosticsRefs { get; }

    public IReadOnlyList<string> ArtifactRefs { get; }

    public SubAgentFailure? Failure { get; }
}

/// <summary>
/// sub-agent 树中一次 spawn/fanout 边的诊断摘要。
/// Diagnostic summary for one spawn/fan-out edge in a sub-agent tree.
/// </summary>
public sealed record SubAgentEdgeDiagnostics
{
    public SubAgentEdgeDiagnostics(
        string parentRunId,
        string childRunId,
        string spawnCallId,
        string fanoutCallId,
        SubAgentRunStatus status,
        IReadOnlyList<string>? diagnosticsRefs = null,
        IReadOnlyList<string>? artifactRefs = null,
        SubAgentFailure? failure = null)
    {
        if (!Enum.IsDefined(status) || status == SubAgentRunStatus.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(status), "sub-agent edge 状态必须是明确值。");
        }

        ParentRunId = IdentifierGuard.AgainstNullOrWhiteSpace(parentRunId, nameof(parentRunId));
        ChildRunId = IdentifierGuard.AgainstNullOrWhiteSpace(childRunId, nameof(childRunId));
        SpawnCallId = IdentifierGuard.AgainstNullOrWhiteSpace(spawnCallId, nameof(spawnCallId));
        FanoutCallId = IdentifierGuard.AgainstNullOrWhiteSpace(fanoutCallId, nameof(fanoutCallId));
        Status = status;
        DiagnosticsRefs = diagnosticsRefs ?? Array.Empty<string>();
        ArtifactRefs = artifactRefs ?? Array.Empty<string>();
        Failure = failure;
    }

    public string ParentRunId { get; }

    public string ChildRunId { get; }

    public string SpawnCallId { get; }

    public string FanoutCallId { get; }

    public SubAgentRunStatus Status { get; }

    public IReadOnlyList<string> DiagnosticsRefs { get; }

    public IReadOnlyList<string> ArtifactRefs { get; }

    public SubAgentFailure? Failure { get; }
}

/// <summary>
/// sub-agent 失败投影。
/// Sub-agent failure projection.
/// </summary>
public sealed record SubAgentFailure
{
    public SubAgentFailure(string code, string message)
    {
        Code = IdentifierGuard.AgainstNullOrWhiteSpace(code, nameof(code));
        Message = IdentifierGuard.AgainstNullOrWhiteSpace(message, nameof(message));
    }

    public string Code { get; }

    public string Message { get; }
}

/// <summary>
/// Sub-agent Module 调用上下文，承载 RuntimeStep 来源追踪和治理边界。
/// Sub-agent Module invocation context carrying RuntimeStep source tracing and governance boundary.
/// </summary>
public sealed record SubAgentModuleInvocationContext
{
    public SubAgentModuleInvocationContext(
        string runtimeStepId,
        string sourceIntentId,
        string sourceGraphId,
        string sourceStageId,
        string sourceKernelOperationId,
        PermissionEnvelope permission,
        SideEffectProfile sideEffect,
        string executionId,
        string kernelRunId,
        GovernanceEnvelope governance,
        string? workingDirectory = null,
        MetadataBag? metadata = null)
    {
        RuntimeStepId = IdentifierGuard.AgainstNullOrWhiteSpace(runtimeStepId, nameof(runtimeStepId));
        SourceIntentId = IdentifierGuard.AgainstNullOrWhiteSpace(sourceIntentId, nameof(sourceIntentId));
        SourceGraphId = IdentifierGuard.AgainstNullOrWhiteSpace(sourceGraphId, nameof(sourceGraphId));
        SourceStageId = IdentifierGuard.AgainstNullOrWhiteSpace(sourceStageId, nameof(sourceStageId));
        SourceKernelOperationId = IdentifierGuard.AgainstNullOrWhiteSpace(sourceKernelOperationId, nameof(sourceKernelOperationId));
        Permission = permission ?? throw new ArgumentNullException(nameof(permission));
        SideEffect = sideEffect ?? throw new ArgumentNullException(nameof(sideEffect));
        ExecutionId = IdentifierGuard.AgainstNullOrWhiteSpace(executionId, nameof(executionId));
        KernelRunId = IdentifierGuard.AgainstNullOrWhiteSpace(kernelRunId, nameof(kernelRunId));
        Governance = governance ?? throw new ArgumentNullException(nameof(governance));
        WorkingDirectory = workingDirectory;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string RuntimeStepId { get; }

    public string SourceIntentId { get; }

    public string SourceGraphId { get; }

    public string SourceStageId { get; }

    public string SourceKernelOperationId { get; }

    public PermissionEnvelope Permission { get; }

    public SideEffectProfile SideEffect { get; }

    public string ExecutionId { get; }

    public string KernelRunId { get; }

    public GovernanceEnvelope Governance { get; }

    public string? WorkingDirectory { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// Sub-agent Module 统一入口，供 Execution Runtime 通过 ModuleCapabilityStep 调用。
/// Unified Sub-agent Module entry point invoked by Execution Runtime through ModuleCapabilityStep.
/// </summary>
public interface ISubAgentModule : IModuleHealthCheck
{
    ValueTask<SubAgentRunResult> SpawnAsync(
        SubAgentSpawnRequest request,
        SubAgentLineage childLineage,
        SubAgentSpawnQuota quota,
        SubAgentModuleInvocationContext context,
        CancellationToken cancellationToken);
}
