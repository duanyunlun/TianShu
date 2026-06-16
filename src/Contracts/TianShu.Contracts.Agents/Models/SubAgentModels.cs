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
        SubAgentFailure? failure = null)
    {
        SpawnCallId = IdentifierGuard.AgainstNullOrWhiteSpace(spawnCallId, nameof(spawnCallId));
        ChildRunId = IdentifierGuard.AgainstNullOrWhiteSpace(childRunId, nameof(childRunId));
        ChildThreadId = IdentifierGuard.AgainstNullOrWhiteSpace(childThreadId, nameof(childThreadId));
        Status = status;
        ResultText = resultText;
        ReplaySummaryRef = replaySummaryRef;
        DiagnosticsRefs = diagnosticsRefs ?? Array.Empty<string>();
        Failure = failure;
    }

    public string SpawnCallId { get; }

    public string ChildRunId { get; }

    public string ChildThreadId { get; }

    public SubAgentRunStatus Status { get; }

    public string? ResultText { get; }

    public string? ReplaySummaryRef { get; }

    public IReadOnlyList<string> DiagnosticsRefs { get; }

    public SubAgentFailure? Failure { get; }
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
