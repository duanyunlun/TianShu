using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Remote;

/// <summary>
/// 远程线程运行生命周期，只表达远端可见状态，不暴露 Kernel / Runtime 内部状态机。
/// Remote-visible thread run lifecycle without exposing Kernel / Runtime internal state machines.
/// </summary>
public enum RemoteRunLifecycle
{
    Unknown = 0,
    Idle = 1,
    Queued = 2,
    Running = 3,
    WaitingForApproval = 4,
    WaitingForInput = 5,
    Interrupted = 6,
    Completed = 7,
    Failed = 8,
    Cancelled = 9,
}

/// <summary>
/// 远程可见 Stage 状态。
/// Remote-visible stage status.
/// </summary>
public enum RemoteStageStatus
{
    Unknown = 0,
    Pending = 1,
    Running = 2,
    Blocked = 3,
    Succeeded = 4,
    Failed = 5,
    Skipped = 6,
}

/// <summary>
/// 远程可见工具或模块调用状态。
/// Remote-visible tool or module invocation status.
/// </summary>
public enum RemoteInvocationStatus
{
    Unknown = 0,
    Pending = 1,
    Running = 2,
    ApprovalRequired = 3,
    Succeeded = 4,
    Failed = 5,
    Blocked = 6,
    Cancelled = 7,
}

/// <summary>
/// 远程可见审批状态。
/// Remote-visible approval state.
/// </summary>
public enum RemoteApprovalState
{
    Unknown = 0,
    Pending = 1,
    Approved = 2,
    Denied = 3,
    Deferred = 4,
    Expired = 5,
}

/// <summary>
/// 远程线程运行态摘要。
/// Remote thread run-state summary.
/// </summary>
public sealed record RemoteRunState
{
    public RemoteRunState(
        RemoteRunLifecycle lifecycle,
        string? activeRunRef = null,
        TurnId? activeTurnId = null,
        ExecutionId? activeExecutionId = null,
        string? notificationCode = null,
        DateTimeOffset? updatedAt = null)
    {
        Lifecycle = lifecycle;
        ActiveRunRef = NormalizeOptional(activeRunRef);
        ActiveTurnId = activeTurnId;
        ActiveExecutionId = activeExecutionId;
        NotificationCode = NormalizeOptional(notificationCode);
        UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow;
    }

    public RemoteRunLifecycle Lifecycle { get; }

    public string? ActiveRunRef { get; }

    public TurnId? ActiveTurnId { get; }

    public ExecutionId? ActiveExecutionId { get; }

    public string? NotificationCode { get; }

    public DateTimeOffset UpdatedAt { get; }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// 当前 Stage 的远程可见摘要。
/// Remote-visible summary of the current stage.
/// </summary>
public sealed record RemoteStageState
{
    public RemoteStageState(
        string graphId,
        string stageId,
        RemoteStageStatus status,
        string? stageKind = null,
        string? objective = null,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? updatedAt = null,
        IReadOnlyList<string>? diagnosticsRefs = null)
    {
        GraphId = IdentifierGuard.AgainstNullOrWhiteSpace(graphId, nameof(graphId));
        StageId = IdentifierGuard.AgainstNullOrWhiteSpace(stageId, nameof(stageId));
        Status = status;
        StageKind = NormalizeOptional(stageKind);
        Objective = NormalizeOptional(objective);
        StartedAt = startedAt;
        UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow;
        DiagnosticsRefs = diagnosticsRefs ?? Array.Empty<string>();
    }

    public string GraphId { get; }

    public string StageId { get; }

    public RemoteStageStatus Status { get; }

    public string? StageKind { get; }

    public string? Objective { get; }

    public DateTimeOffset? StartedAt { get; }

    public DateTimeOffset UpdatedAt { get; }

    public IReadOnlyList<string> DiagnosticsRefs { get; }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// 工具调用的远程可见摘要。
/// Remote-visible summary of a tool invocation.
/// </summary>
public sealed record RemoteToolState
{
    public RemoteToolState(
        string toolId,
        string toolName,
        RemoteInvocationStatus status,
        CallId? callId = null,
        SideEffectLevel sideEffectLevel = SideEffectLevel.Unspecified,
        bool requiresHumanGate = true,
        string? approvalRef = null,
        string? resultRef = null,
        string? failureCode = null)
    {
        ToolId = IdentifierGuard.AgainstNullOrWhiteSpace(toolId, nameof(toolId));
        ToolName = IdentifierGuard.AgainstNullOrWhiteSpace(toolName, nameof(toolName));
        Status = status;
        CallId = callId;
        SideEffectLevel = sideEffectLevel;
        RequiresHumanGate = requiresHumanGate;
        ApprovalRef = NormalizeOptional(approvalRef);
        ResultRef = NormalizeOptional(resultRef);
        FailureCode = NormalizeOptional(failureCode);
    }

    public string ToolId { get; }

    public string ToolName { get; }

    public RemoteInvocationStatus Status { get; }

    public CallId? CallId { get; }

    public SideEffectLevel SideEffectLevel { get; }

    public bool RequiresHumanGate { get; }

    public string? ApprovalRef { get; }

    public string? ResultRef { get; }

    public string? FailureCode { get; }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// 子 Agent 的远程可见摘要。
/// Remote-visible summary of a sub-agent.
/// </summary>
public sealed record RemoteSubAgentState
{
    public RemoteSubAgentState(
        AgentId agentId,
        string role,
        RemoteInvocationStatus status,
        int depth,
        string? parentAgentRef = null,
        string? taskRef = null,
        string? diagnosticsRef = null)
    {
        AgentId = agentId;
        Role = IdentifierGuard.AgainstNullOrWhiteSpace(role, nameof(role));
        Status = status;
        Depth = depth < 0 ? throw new ArgumentOutOfRangeException(nameof(depth), "深度不能为负。") : depth;
        ParentAgentRef = NormalizeOptional(parentAgentRef);
        TaskRef = NormalizeOptional(taskRef);
        DiagnosticsRef = NormalizeOptional(diagnosticsRef);
    }

    public AgentId AgentId { get; }

    public string Role { get; }

    public RemoteInvocationStatus Status { get; }

    public int Depth { get; }

    public string? ParentAgentRef { get; }

    public string? TaskRef { get; }

    public string? DiagnosticsRef { get; }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// 远程可见审批条目；只能承载最小风险摘要、引用和可选决策，不承载原始敏感 payload。
/// Remote-visible approval entry carrying only minimal risk summaries, references, and decision options.
/// </summary>
public sealed record RemotePendingApproval
{
    public RemotePendingApproval(
        ApprovalId approvalId,
        string title,
        RemoteApprovalState state,
        SideEffectLevel sideEffectLevel,
        bool requiresHumanGate,
        IReadOnlyList<string>? decisionOptions = null,
        string? riskSummary = null,
        string? diffRef = null,
        string? artifactRef = null,
        DateTimeOffset? expiresAt = null)
    {
        if (sideEffectLevel == SideEffectLevel.Unspecified)
        {
            throw new ArgumentException("远程审批必须声明明确的副作用等级。", nameof(sideEffectLevel));
        }

        if (!requiresHumanGate)
        {
            throw new ArgumentException("远程审批不得关闭 human gate。", nameof(requiresHumanGate));
        }

        ApprovalId = approvalId;
        Title = IdentifierGuard.AgainstNullOrWhiteSpace(title, nameof(title));
        State = state;
        SideEffectLevel = sideEffectLevel;
        RequiresHumanGate = requiresHumanGate;
        DecisionOptions = decisionOptions ?? Array.Empty<string>();
        RiskSummary = NormalizeOptional(riskSummary);
        DiffRef = NormalizeOptional(diffRef);
        ArtifactRef = NormalizeOptional(artifactRef);
        ExpiresAt = expiresAt;
    }

    public ApprovalId ApprovalId { get; }

    public string Title { get; }

    public RemoteApprovalState State { get; }

    public SideEffectLevel SideEffectLevel { get; }

    public bool RequiresHumanGate { get; }

    public IReadOnlyList<string> DecisionOptions { get; }

    public string? RiskSummary { get; }

    public string? DiffRef { get; }

    public string? ArtifactRef { get; }

    public DateTimeOffset? ExpiresAt { get; }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// 远程 artifact 引用，只暴露安全引用和摘要，不携带文件内容。
/// Remote artifact reference exposing safe references and summaries without file contents.
/// </summary>
public sealed record RemoteArtifactRef
{
    public RemoteArtifactRef(
        ArtifactId artifactId,
        string name,
        string kind,
        string state,
        string? uriRef = null,
        string? summary = null)
    {
        ArtifactId = artifactId;
        Name = IdentifierGuard.AgainstNullOrWhiteSpace(name, nameof(name));
        Kind = IdentifierGuard.AgainstNullOrWhiteSpace(kind, nameof(kind));
        State = IdentifierGuard.AgainstNullOrWhiteSpace(state, nameof(state));
        UriRef = NormalizeOptional(uriRef);
        Summary = NormalizeOptional(summary);
    }

    public ArtifactId ArtifactId { get; }

    public string Name { get; }

    public string Kind { get; }

    public string State { get; }

    public string? UriRef { get; }

    public string? Summary { get; }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// 远程诊断摘要，只包含安全引用、失败码和缺失原因。
/// Remote diagnostics summary containing only safe references, failure codes, and missing reasons.
/// </summary>
public sealed record RemoteDiagnosticsSummary
{
    public RemoteDiagnosticsSummary(
        IReadOnlyList<string>? runtimeTraceRefs = null,
        IReadOnlyList<string>? diagnosticsRefs = null,
        IReadOnlyList<string>? metricsEventIds = null,
        IReadOnlyList<string>? failureCodes = null,
        IReadOnlyList<string>? missingReasons = null)
    {
        RuntimeTraceRefs = runtimeTraceRefs ?? Array.Empty<string>();
        DiagnosticsRefs = diagnosticsRefs ?? Array.Empty<string>();
        MetricsEventIds = metricsEventIds ?? Array.Empty<string>();
        FailureCodes = failureCodes ?? Array.Empty<string>();
        MissingReasons = missingReasons ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> RuntimeTraceRefs { get; }

    public IReadOnlyList<string> DiagnosticsRefs { get; }

    public IReadOnlyList<string> MetricsEventIds { get; }

    public IReadOnlyList<string> FailureCodes { get; }

    public IReadOnlyList<string> MissingReasons { get; }
}

/// <summary>
/// 远程证据摘要，承载 turn log、rollout、audit 与降级原因引用。
/// Remote evidence summary carrying turn-log, rollout, audit, and downgrade references.
/// </summary>
public sealed record RemoteEvidenceSummary
{
    public RemoteEvidenceSummary(
        string? turnLogRef = null,
        string? rolloutRef = null,
        IReadOnlyList<string>? auditRefs = null,
        IReadOnlyList<string>? downgradeReasons = null)
    {
        TurnLogRef = NormalizeOptional(turnLogRef);
        RolloutRef = NormalizeOptional(rolloutRef);
        AuditRefs = auditRefs ?? Array.Empty<string>();
        DowngradeReasons = downgradeReasons ?? Array.Empty<string>();
    }

    public string? TurnLogRef { get; }

    public string? RolloutRef { get; }

    public IReadOnlyList<string> AuditRefs { get; }

    public IReadOnlyList<string> DowngradeReasons { get; }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// 远程快照脱敏摘要，用于说明哪些类别被裁剪或替换为安全引用。
/// Remote snapshot redaction summary describing categories trimmed or replaced by safe references.
/// </summary>
public sealed record RemoteSnapshotRedaction
{
    public RemoteSnapshotRedaction(
        bool hasRedactedFields = false,
        IReadOnlyList<string>? redactedKinds = null,
        IReadOnlyList<string>? policyRefs = null)
    {
        HasRedactedFields = hasRedactedFields;
        RedactedKinds = redactedKinds ?? Array.Empty<string>();
        PolicyRefs = policyRefs ?? Array.Empty<string>();
    }

    public bool HasRedactedFields { get; }

    public IReadOnlyList<string> RedactedKinds { get; }

    public IReadOnlyList<string> PolicyRefs { get; }
}

/// <summary>
/// 远程线程快照，是远端消费者可见的完整线程状态边界。
/// Remote thread snapshot representing the full thread-state boundary visible to remote consumers.
/// </summary>
public sealed record RemoteThreadSnapshot
{
    public RemoteThreadSnapshot(
        string snapshotId,
        ThreadId threadId,
        RemoteRunState runState,
        RemoteStageState? currentStage = null,
        IReadOnlyList<RemoteToolState>? toolStates = null,
        IReadOnlyList<RemoteSubAgentState>? subAgentStates = null,
        IReadOnlyList<RemotePendingApproval>? pendingApprovals = null,
        IReadOnlyList<RemoteArtifactRef>? artifacts = null,
        RemoteDiagnosticsSummary? diagnostics = null,
        RemoteEvidenceSummary? evidence = null,
        RemoteSnapshotRedaction? redaction = null,
        DateTimeOffset? capturedAt = null,
        VersionStamp? version = null)
    {
        SnapshotId = IdentifierGuard.AgainstNullOrWhiteSpace(snapshotId, nameof(snapshotId));
        ThreadId = threadId;
        RunState = runState ?? throw new ArgumentNullException(nameof(runState));
        CurrentStage = currentStage;
        ToolStates = toolStates ?? Array.Empty<RemoteToolState>();
        SubAgentStates = subAgentStates ?? Array.Empty<RemoteSubAgentState>();
        PendingApprovals = pendingApprovals ?? Array.Empty<RemotePendingApproval>();
        Artifacts = artifacts ?? Array.Empty<RemoteArtifactRef>();
        Diagnostics = diagnostics ?? new RemoteDiagnosticsSummary();
        Evidence = evidence ?? new RemoteEvidenceSummary();
        Redaction = redaction ?? new RemoteSnapshotRedaction();
        CapturedAt = capturedAt ?? DateTimeOffset.UtcNow;
        Version = version ?? new VersionStamp(0);
    }

    public string SnapshotId { get; }

    public ThreadId ThreadId { get; }

    public RemoteRunState RunState { get; }

    public RemoteStageState? CurrentStage { get; }

    public IReadOnlyList<RemoteToolState> ToolStates { get; }

    public IReadOnlyList<RemoteSubAgentState> SubAgentStates { get; }

    public IReadOnlyList<RemotePendingApproval> PendingApprovals { get; }

    public IReadOnlyList<RemoteArtifactRef> Artifacts { get; }

    public RemoteDiagnosticsSummary Diagnostics { get; }

    public RemoteEvidenceSummary Evidence { get; }

    public RemoteSnapshotRedaction Redaction { get; }

    public DateTimeOffset CapturedAt { get; }

    public VersionStamp Version { get; }
}
