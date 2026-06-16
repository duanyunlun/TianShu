using TianShu.Contracts.Kernel;

namespace TianShu.Kernel.Abstractions;

/// <summary>
/// Kernel 评估器抽象，基于运行结果和 trace 生成 evaluation evidence。
/// Kernel evaluator abstraction that creates evaluation evidence from run results and traces.
/// </summary>
public interface IKernelEvaluator
{
    Task<KernelEvaluationResult> EvaluateAsync(
        KernelRunResult result,
        KernelRunTrace trace,
        EvaluationPlan evaluationPlan,
        CancellationToken cancellationToken = default);
}
