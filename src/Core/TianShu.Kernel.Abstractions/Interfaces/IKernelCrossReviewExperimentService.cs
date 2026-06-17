using TianShu.Contracts.Kernel;

namespace TianShu.Kernel.Abstractions;

/// <summary>
/// Kernel 异质交叉评审实验服务，聚合多个评审者的结构化评分、理由、分歧与不确定性。
/// Kernel heterogeneous cross-review experiment service that aggregates structured scores, reasons, disagreements, and uncertainty from multiple reviewers.
/// </summary>
public interface IKernelCrossReviewExperimentService
{
    Task<KernelCrossReviewExperimentReport> RunAsync(
        KernelCrossReviewExperimentRequest request,
        CancellationToken cancellationToken = default);
}
