using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Diagnostics;

/// <summary>
/// 控制平面反馈上传命令。
/// Control-plane command that uploads user feedback.
/// </summary>
public sealed record ControlPlaneFeedbackUploadCommand
{
    public string Classification { get; init; } = string.Empty;

    public bool IncludeLogs { get; init; }

    public string? ThreadId { get; init; }

    public string? Reason { get; init; }

    public IReadOnlyList<string> ExtraLogFiles { get; init; } = Array.Empty<string>();
}
