using TianShu.Contracts.Kernel;

namespace TianShu.Kernel.Abstractions;

/// <summary>
/// Adaptive 候选验证服务，负责把多个 proposal 统一送入 Stable Kernel Core 的验证门禁。
/// Adaptive candidate validation service that routes multiple proposals through Stable Kernel Core gates.
/// </summary>
public interface IAdaptiveCandidateValidationService
{
    /// <summary>
    /// 验证一组 Adaptive 候选，但不执行、不试运行、不提升为默认策略。
    /// Validates an adaptive candidate set without executing, trial-running, or promoting it.
    /// </summary>
    Task<AdaptiveCandidateValidationReport> ValidateCandidatesAsync(
        AdaptiveCandidateValidationRequest request,
        CancellationToken cancellationToken = default);
}
