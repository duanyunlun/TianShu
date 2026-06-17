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
        cancellationToken.ThrowIfCancellationRequested();

        var replay = replayChecker.Check(trace);
        var hasRejection = trace.Events.Any(static item => item.Kind == KernelTraceEventKind.Rejected);
        var succeeded = result.LifecycleState is KernelRunLifecycleState.Executing or KernelRunLifecycleState.Completed
                        && !hasRejection;
        var traceId = result.TraceId ?? new KernelTraceId($"trace-{result.RunId.Value}");

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

        var evidence = new KernelEvaluationEvidenceSet(
            traceRefs: new[] { $"trace://{traceId.Value}" },
            diagnosticRefs: hasRejection
                ? trace.Events
                    .Where(static item => item.Kind == KernelTraceEventKind.Rejected)
                    .Select(static (_, index) => $"diagnostics://kernel/rejection/{index}")
                    .ToArray()
                : Array.Empty<string>(),
            objectiveAnchorRefs: new[] { $"objective://kernel/replay/{trace.RunId.Value}" });
        var observations = new[]
        {
            new KernelEvaluationMetricObservation(
                "success",
                KernelEvaluationMetricKind.Observable,
                KernelEvaluationSignalKind.RuntimeTrace,
                $"trace://{traceId.Value}/lifecycle",
                score: succeeded ? 1m : 0m,
                confidence: 1m),
            new KernelEvaluationMetricObservation(
                "replay_compatible",
                KernelEvaluationMetricKind.ObjectiveAnchor,
                KernelEvaluationSignalKind.RuntimeTrace,
                $"objective://kernel/replay/{trace.RunId.Value}",
                score: replay.Compatible ? 1m : 0m,
                confidence: 1m),
            new KernelEvaluationMetricObservation(
                "policy_violation_attempt",
                KernelEvaluationMetricKind.ObjectiveAnchor,
                KernelEvaluationSignalKind.RuntimeTrace,
                $"trace://{traceId.Value}/rejections",
                score: hasRejection ? 1m : 0m,
                confidence: 1m),
        };

        var evaluation = new KernelEvaluationResult(
            $"evaluation-{result.RunId.Value}",
            result.RunId,
            traceId,
            decision,
            scores,
            summaryRef: $"evaluation.summary.{result.RunId.Value}",
            evidence: evidence,
            observations: observations,
            overallConfidence: replay.Compatible ? 1m : 0.5m,
            disagreementScore: 0m);

        return Task.FromResult(evaluation);
    }
}
