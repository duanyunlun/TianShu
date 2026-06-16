using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Diagnostics;

/// <summary>
/// 尝试摘要。
/// Attempt summary.
/// </summary>
public sealed record AttemptSummary(
    ExecutionId ExecutionId,
    int AttemptNumber,
    bool Succeeded,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt = null);

/// <summary>
/// 使用量快照。
/// Usage snapshot.
/// </summary>
public sealed record UsageSnapshot(
    ExecutionId ExecutionId,
    string ProviderKey,
    int InputTokens,
    int OutputTokens,
    int? ReasoningTokens = null)
{
    public string ProviderKey { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(ProviderKey, nameof(ProviderKey));
}

/// <summary>
/// 恢复检查点。
/// Recovery checkpoint.
/// </summary>
public sealed record RecoveryCheckpoint
{
    /// <summary>
    /// 初始化恢复检查点。
    /// Initializes a recovery checkpoint.
    /// </summary>
    public RecoveryCheckpoint(
        ExecutionId executionId,
        string stage,
        StructuredValue? state = null,
        DateTimeOffset? createdAt = null)
    {
        ExecutionId = executionId;
        Stage = IdentifierGuard.AgainstNullOrWhiteSpace(stage, nameof(stage));
        State = state;
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
    }

    public ExecutionId ExecutionId { get; }

    public string Stage { get; }

    public StructuredValue? State { get; }

    public DateTimeOffset CreatedAt { get; }
}

/// <summary>
/// 审计记录。
/// Audit record.
/// </summary>
public sealed record AuditRecord
{
    /// <summary>
    /// 初始化审计记录。
    /// Initializes an audit record.
    /// </summary>
    public AuditRecord(
        AuditRecordId id,
        string category,
        string message,
        DateTimeOffset? timestamp = null,
        MetadataBag? metadata = null)
    {
        Id = id;
        Category = IdentifierGuard.AgainstNullOrWhiteSpace(category, nameof(category));
        Message = IdentifierGuard.AgainstNullOrWhiteSpace(message, nameof(message));
        Timestamp = timestamp ?? DateTimeOffset.UtcNow;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public AuditRecordId Id { get; }

    public string Category { get; }

    public string Message { get; }

    public DateTimeOffset Timestamp { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// 执行追踪。
/// Execution trace.
/// </summary>
public sealed record ExecutionTrace
{
    /// <summary>
    /// 初始化执行追踪。
    /// Initializes an execution trace.
    /// </summary>
    public ExecutionTrace(
        ExecutionTraceId id,
        ExecutionId executionId,
        IReadOnlyList<AttemptSummary>? attempts = null,
        IReadOnlyList<AuditRecord>? auditTrail = null,
        IReadOnlyList<RecoveryCheckpoint>? recoveryCheckpoints = null)
    {
        Id = id;
        ExecutionId = executionId;
        Attempts = attempts ?? Array.Empty<AttemptSummary>();
        AuditTrail = auditTrail ?? Array.Empty<AuditRecord>();
        RecoveryCheckpoints = recoveryCheckpoints ?? Array.Empty<RecoveryCheckpoint>();
    }

    public ExecutionTraceId Id { get; }

    public ExecutionId ExecutionId { get; }

    public IReadOnlyList<AttemptSummary> Attempts { get; }

    public IReadOnlyList<AuditRecord> AuditTrail { get; }

    public IReadOnlyList<RecoveryCheckpoint> RecoveryCheckpoints { get; }
}

/// <summary>
/// 控制平面反馈上传结果。
/// Control-plane result returned after feedback upload.
/// </summary>
public sealed record ControlPlaneFeedbackUploadResult
{
    public string? ThreadId { get; init; }
}

/// <summary>
/// Debug memory cleanup result returned through the diagnostics control plane.
/// 通过诊断控制平面返回的调试记忆清理结果。
/// </summary>
public sealed record ControlPlaneDebugClearMemoriesResult
{
    /// <summary>
    /// 宿主状态数据库路径。
    /// Host state database path.
    /// </summary>
    public string StateDbPath { get; init; } = string.Empty;

    /// <summary>
    /// 已清理的一阶段输出记录数量。
    /// Number of stage-one output records cleared.
    /// </summary>
    public long ClearedStage1OutputCount { get; init; }

    /// <summary>
    /// 已禁用记忆模式的线程数量。
    /// Number of threads whose memory mode was disabled.
    /// </summary>
    public int DisabledThreadCount { get; init; }

    /// <summary>
    /// 本地记忆根目录路径。
    /// Local memory root path.
    /// </summary>
    public string MemoryRootPath { get; init; } = string.Empty;

    /// <summary>
    /// 是否删除了本地记忆根目录。
    /// Indicates whether the local memory root directory was removed.
    /// </summary>
    public bool RemovedMemoryRoot { get; init; }
}
