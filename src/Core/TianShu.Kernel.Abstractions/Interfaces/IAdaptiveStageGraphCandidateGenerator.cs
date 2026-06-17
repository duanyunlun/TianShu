using TianShu.Contracts.Kernel;

namespace TianShu.Kernel.Abstractions;

/// <summary>
/// Adaptive StageGraph 候选生成接口，只能返回结构化 StageGraph proposal。
/// Adaptive StageGraph candidate generator that may only return structured StageGraph proposals.
/// </summary>
public interface IAdaptiveStageGraphCandidateGenerator
{
    /// <summary>
    /// 为当前核心意图生成多个候选 StageGraph proposal。
    /// Generates multiple candidate StageGraph proposals for the current core intent.
    /// </summary>
    Task<IReadOnlyList<StageGraphProposal>> GenerateCandidatesAsync(
        CoreIntent intent,
        KernelRunState state,
        KernelRunOptions options,
        CancellationToken cancellationToken = default);
}
