using TianShu.Contracts.Kernel;
using TianShu.Kernel.Abstractions;

namespace TianShu.Kernel.Strategies;

/// <summary>
/// 默认 Kernel evaluator，从 run result 和 trace 生成 evaluation result。
/// Default Kernel evaluator that creates evaluation results from run result and trace.
/// </summary>
public sealed class KernelEvaluator : IKernelEvaluator
{
    private readonly ReplayCompatibilityChecker replayChecker;

    public KernelEvaluator(ReplayCompatibilityChecker? replayChecker = null)
    {
        this.replayChecker = replayChecker ?? new ReplayCompatibilityChecker();
    }

    public Task<KernelEvaluationResult> EvaluateAsync(
        KernelRunResult result,
        KernelRunTrace trace,
        EvaluationPlan evaluationPlan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(evaluationPlan);

        var replay = replayChecker.Check(trace);
        var hasRejection = trace.Events.Any(static item => item.Kind == KernelTraceEventKind.Rejected);
        var succeeded = result.LifecycleState is KernelRunLifecycleState.Executing or KernelRunLifecycleState.Completed
                        && !hasRejection;

        var decision = succeeded && replay.Compatible
            ? KernelReviewDecision.Approved
            : evaluationPlan.RequireReplay && !replay.Compatible
                ? KernelReviewDecision.RequiresHumanGate
                : KernelReviewDecision.Rejected;

        var scores = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["success"] = succeeded ? 1m : 0m,
            ["replay_compatible"] = replay.Compatible ? 1m : 0m,
            ["policy_violation_attempt"] = hasRejection ? 1m : 0m,
        };

        var evaluation = new KernelEvaluationResult(
            $"evaluation-{result.RunId.Value}",
            result.RunId,
            result.TraceId ?? new KernelTraceId($"trace-{result.RunId.Value}"),
            decision,
            scores,
            summaryRef: $"evaluation.summary.{result.RunId.Value}");

        return Task.FromResult(evaluation);
    }
}
