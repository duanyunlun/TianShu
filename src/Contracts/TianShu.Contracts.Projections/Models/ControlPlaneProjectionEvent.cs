using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Projections;

/// <summary>
/// 控制平面统一投影事件包络。
/// Unified control-plane projection event envelope.
/// </summary>
public sealed record ControlPlaneProjectionEvent
{
    /// <summary>
    /// 事件种类。
    /// Event kind.
    /// </summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>
    /// 事件时间戳。
    /// Event timestamp.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// 所属线程标识。
    /// Associated thread identifier.
    /// </summary>
    public ThreadId? ThreadId { get; init; }

    /// <summary>
    /// 所属 turn 标识。
    /// Associated turn identifier.
    /// </summary>
    public TurnId? TurnId { get; init; }

    /// <summary>
    /// 关联条目标识。
    /// Related item identifier.
    /// </summary>
    public string? ItemId { get; init; }

    /// <summary>
    /// 关联交互调用标识。
    /// Related interactive-call identifier.
    /// </summary>
    public CallId? CallId { get; init; }

    /// <summary>
    /// 关联工具名称。
    /// Related tool name.
    /// </summary>
    public string? ToolName { get; init; }

    /// <summary>
    /// 关联服务端名称。
    /// Related server name.
    /// </summary>
    public string? ServerName { get; init; }

    /// <summary>
    /// 文本增量或摘要文本。
    /// Text delta or summary text.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// 事件状态。
    /// Event status.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// 事件阶段。
    /// Event phase.
    /// </summary>
    public string? Phase { get; init; }

    /// <summary>
    /// 面向用户或日志的补充消息。
    /// Additional user-facing or log-facing message.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// 是否将继续重试。
    /// Whether the source will retry.
    /// </summary>
    public bool? WillRetry { get; init; }

    /// <summary>
    /// 是否需要审批。
    /// Whether approval is required.
    /// </summary>
    public bool? RequiresApproval { get; init; }

    /// <summary>
    /// 审批类型。
    /// Approval kind.
    /// </summary>
    public string? ApprovalKind { get; init; }

    /// <summary>
    /// 事件来源方法。
    /// Source method that produced the event.
    /// </summary>
    public string? SourceMethod { get; init; }

    /// <summary>
    /// 操作名称。
    /// Operation name.
    /// </summary>
    public string? OperationName { get; init; }

    /// <summary>
    /// 事件载荷。
    /// Event payload.
    /// </summary>
    public StructuredValue? Payload { get; init; }

    /// <summary>
    /// 真实的投影视图增量；仅当上游已经完成 typed 物化时提供。
    /// Real materialized projection delta, provided only when the upstream already produced a typed view delta.
    /// </summary>
    public ProjectionDelta? Delta { get; init; }

    /// <summary>
    /// 真实的投影视图重置；仅当上游已经完成 typed 物化时提供。
    /// Real materialized projection reset, provided only when the upstream already produced a typed view reset.
    /// </summary>
    public ProjectionReset? Reset { get; init; }
}
