namespace TianShu.Contracts.Workflows;

/// <summary>
/// 控制平面审查目标。
/// Control-plane review target.
/// </summary>
public abstract record ControlPlaneReviewTarget
{
    /// <summary>
    /// 目标类型标识。
    /// Target discriminator.
    /// </summary>
    public abstract string Type { get; }
}

/// <summary>
/// 未提交改动审查目标。
/// Review target for uncommitted changes.
/// </summary>
public sealed record ControlPlaneReviewUncommittedChangesTarget : ControlPlaneReviewTarget
{
    public override string Type => "uncommittedChanges";
}

/// <summary>
/// 基线分支审查目标。
/// Review target that compares against a base branch.
/// </summary>
public sealed record ControlPlaneReviewBaseBranchTarget : ControlPlaneReviewTarget
{
    public override string Type => "baseBranch";

    public string Branch { get; init; } = string.Empty;
}

/// <summary>
/// 指定提交审查目标。
/// Review target that compares against a specific commit.
/// </summary>
public sealed record ControlPlaneReviewCommitTarget : ControlPlaneReviewTarget
{
    public override string Type => "commit";

    public string Sha { get; init; } = string.Empty;

    public string? Title { get; init; }
}

/// <summary>
/// 自定义审查目标。
/// Review target driven by custom instructions.
/// </summary>
public sealed record ControlPlaneReviewCustomTarget : ControlPlaneReviewTarget
{
    public override string Type => "custom";

    public string Instructions { get; init; } = string.Empty;
}

/// <summary>
/// 控制平面审查启动结果。
/// Control-plane result returned after review start.
/// </summary>
public sealed record ControlPlaneReviewStartResult
{
    /// <summary>
    /// 新建的审查线程标识。
    /// Identifier of the newly created review thread.
    /// </summary>
    public string ReviewThreadId { get; init; } = string.Empty;

    /// <summary>
    /// 首个审查轮次。
    /// First review turn.
    /// </summary>
    public ControlPlaneReviewTurn? Turn { get; init; }
}

/// <summary>
/// 控制平面审查轮次。
/// Control-plane review turn.
/// </summary>
public sealed record ControlPlaneReviewTurn
{
    /// <summary>
    /// 轮次标识。
    /// Turn identifier.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// 轮次状态。
    /// Turn status.
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// 供消费端展示的文本。
    /// Display text for consumer surfaces.
    /// </summary>
    public string? DisplayText { get; init; }
}
