using TianShu.Contracts.Kernel;

namespace TianShu.Kernel.Abstractions;

/// <summary>
/// Kernel strategy 评价统计聚合服务；只输出策略级比较证据，不执行 promotion。
/// Kernel strategy evaluation aggregation service; emits strategy-level comparison evidence without executing promotion.
/// </summary>
public interface IKernelStrategyEvaluationAggregationService
{
    Task<KernelStrategyEvaluationAggregationReport> AggregateAsync(
        KernelStrategyEvaluationAggregationRequest request,
        CancellationToken cancellationToken = default);
}
