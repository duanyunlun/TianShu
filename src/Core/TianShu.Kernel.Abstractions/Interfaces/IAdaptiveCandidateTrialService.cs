namespace TianShu.Kernel.Abstractions;

/// <summary>
/// Adaptive 候选试运行服务，只生成 shadow / bounded trial 证据，不执行真实外部副作用。
/// Adaptive candidate trial service that produces shadow / bounded trial evidence without real external side effects.
/// </summary>
public interface IAdaptiveCandidateTrialService
{
    /// <summary>
    /// 对已验证候选执行受控试运行，并输出差异报告；不得晋升策略。
    /// Runs controlled trials for validated candidates and emits diff reports; must not promote strategies.
    /// </summary>
    Task<AdaptiveCandidateTrialReport> RunTrialsAsync(
        AdaptiveCandidateTrialRequest request,
        CancellationToken cancellationToken = default);
}
