using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Workflows;

/// <summary>
/// 控制平面作业操作结果。
/// Control-plane result returned by job operations.
/// </summary>
public sealed record ControlPlaneJobOperationResult
{
    /// <summary>
    /// 作业主体信息。
    /// Job-level details.
    /// </summary>
    public ControlPlaneJobDetails? Job { get; init; }

    /// <summary>
    /// 作业条目集合。
    /// Job item collection.
    /// </summary>
    public IReadOnlyList<ControlPlaneJobItemDetails> Items { get; init; } = Array.Empty<ControlPlaneJobItemDetails>();

    /// <summary>
    /// 单个作业条目详情。
    /// Single job-item detail.
    /// </summary>
    public ControlPlaneJobItemDetails? Item { get; init; }
}

/// <summary>
/// 控制平面作业列表结果。
/// Control-plane result returned by job-list queries.
/// </summary>
public sealed record ControlPlaneJobListResult
{
    /// <summary>
    /// 作业集合。
    /// Job collection.
    /// </summary>
    public IReadOnlyList<ControlPlaneJobDetails> Jobs { get; init; } = Array.Empty<ControlPlaneJobDetails>();
}

/// <summary>
/// 控制平面作业详情。
/// Control-plane job details.
/// </summary>
public sealed record ControlPlaneJobDetails
{
    /// <summary>
    /// 作业标识。
    /// Job identifier.
    /// </summary>
    public JobId Id { get; init; }

    /// <summary>
    /// 作业显示名称。
    /// Human-readable job name.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// 作业状态。
    /// Job state.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// 作业指令。
    /// Job instruction.
    /// </summary>
    public string? Instruction { get; init; }
}

/// <summary>
/// 控制平面作业条目详情。
/// Control-plane job-item details.
/// </summary>
public sealed record ControlPlaneJobItemDetails
{
    /// <summary>
    /// 条目标识。
    /// Item identifier.
    /// </summary>
    public JobItemId ItemId { get; init; }

    /// <summary>
    /// 外部源数据标识。
    /// External source-data identifier.
    /// </summary>
    public string? SourceId { get; init; }

    /// <summary>
    /// 实际执行线程标识。
    /// Identifier of the executing thread.
    /// </summary>
    public ThreadId? ThreadId { get; init; }

    /// <summary>
    /// 派发目标线程标识。
    /// Identifier of the assigned target thread.
    /// </summary>
    public ThreadId? AssignedThreadId { get; init; }

    /// <summary>
    /// 条目状态。
    /// Item state.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// 最近一次错误。
    /// Most recent error message.
    /// </summary>
    public string? LastError { get; init; }

    /// <summary>
    /// 条目结果载荷。
    /// Item result payload.
    /// </summary>
    public StructuredValue? Result { get; init; }
}
