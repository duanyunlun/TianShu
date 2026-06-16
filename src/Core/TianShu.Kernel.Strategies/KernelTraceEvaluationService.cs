using TianShu.Contracts.Kernel;
using TianShu.Kernel.Abstractions;

namespace TianShu.Kernel.Strategies;

/// <summary>
/// 从 Kernel trace store 读取 trace 并执行评价的服务。
/// Service that reads Kernel trace from a trace store and evaluates it.
/// </summary>
public sealed class KernelTraceEvaluationService
{
    private readonly IKernelTraceStore traceStore;
    private readonly IKernelEvaluator evaluator;

    public KernelTraceEvaluationService(IKernelTraceStore traceStore, IKernelEvaluator? evaluator = null)
    {
        this.traceStore = traceStore ?? throw new ArgumentNullException(nameof(traceStore));
        this.evaluator = evaluator ?? new KernelEvaluator();
    }

    public async Task<KernelEvaluationResult> EvaluateRunAsync(KernelRunResult result, EvaluationPlan evaluationPlan, CancellationToken cancellationToken = default)
    {
        var trace = await traceStore.ReadRunTraceAsync(result.RunId, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Kernel run trace not found: {result.RunId.Value}");
        return await evaluator.EvaluateAsync(result, trace, evaluationPlan, cancellationToken).ConfigureAwait(false);
    }
}
