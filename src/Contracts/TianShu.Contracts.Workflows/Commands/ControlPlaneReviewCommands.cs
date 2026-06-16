namespace TianShu.Contracts.Workflows;

/// <summary>
/// 控制平面审查启动命令。
/// Control-plane command that starts a review workflow.
/// </summary>
public sealed record ControlPlaneReviewStartCommand
{
    /// <summary>
    /// 待审查的源线程标识。
    /// Identifier of the source thread being reviewed.
    /// </summary>
    public string ThreadId { get; init; } = string.Empty;

    /// <summary>
    /// 审查目标定义。
    /// Review target definition.
    /// </summary>
    public ControlPlaneReviewTarget Target { get; init; } = new ControlPlaneReviewCustomTarget();

    /// <summary>
    /// 结果投递策略。
    /// Delivery policy for the review result.
    /// </summary>
    public string? Delivery { get; init; }
}
