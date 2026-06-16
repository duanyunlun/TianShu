namespace TianShu.Contracts.Primitives;

/// <summary>
/// 问题代码枚举，定义跨 Contracts 可复用的稳定错误分类。
/// Problem-code enumeration that defines reusable stable error categories across contracts.
/// </summary>
public enum ProblemCode
{
    Unknown = 0,
    ValidationFailed = 1,
    NotFound = 2,
    Conflict = 3,
    PermissionDenied = 4,
    NotSupported = 5,
    Timeout = 6,
    ExternalDependencyFailed = 7,
    Cancelled = 8,
}

/// <summary>
/// 统一问题详情模型，用于在 typed-first 边界上传递失败信息。
/// Unified problem-details model used to move failure information across typed-first boundaries.
/// </summary>
public sealed record ProblemDetails
{
    /// <summary>
    /// 问题类别代码。
    /// Problem category code.
    /// </summary>
    public ProblemCode Code { get; init; } = ProblemCode.Unknown;

    /// <summary>
    /// 人类可读的问题描述。
    /// Human-readable problem description.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// 可选的问题目标字段或资源。
    /// Optional target field or resource associated with the problem.
    /// </summary>
    public string? Target { get; init; }

    /// <summary>
    /// 指示该问题是否适合重试。
    /// Indicates whether the problem is retryable.
    /// </summary>
    public bool IsTransient { get; init; }

    /// <summary>
    /// 附加的结构化问题细节。
    /// Additional structured problem details.
    /// </summary>
    public MetadataBag Details { get; init; } = MetadataBag.Empty;
}
