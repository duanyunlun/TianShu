using TianShu.Contracts.Kernel;
using TianShu.Kernel.Abstractions;

namespace TianShu.Kernel.Strategies;

/// <summary>
/// 默认策略级评价聚合服务；从多次评价样本生成统计比较证据，不执行 strategy promotion。
/// Default strategy-level evaluation aggregation service; creates statistical comparison evidence from multiple samples without promoting strategies.
/// </summary>
public sealed class KernelStrategyEvaluationAggregationService : IKernelStrategyEvaluationAggregationService
{
    public Task<KernelStrategyEvaluationAggregationReport> AggregateAsync(
        KernelStrategyEvaluationAggregationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var comparisons = request.Samples
            .GroupBy(static item => item.StrategyId)
            .Select(group => CreateComparison(request, group.Key, group.ToArray(), cancellationToken))
            .OrderByDescending(static item => item.PromotionReady)
            .ThenByDescending(static item => item.AverageScore)
            .ThenByDescending(static item => item.AverageConfidence)
            .ThenBy(static item => item.StrategyId.Value, StringComparer.Ordinal)
            .ToArray();
        var bestCandidate = comparisons.FirstOrDefault(static item => item.PromotionReady)?.StrategyId;

        return Task.FromResult(new KernelStrategyEvaluationAggregationReport(
            request.AggregationId,
            comparisons,
            bestCandidate,
            hasPromotionReadyCandidate: bestCandidate.HasValue,
            requiresHumanGate: comparisons.Any(static item => item.RequiresHumanGate)));
    }

    private static KernelStrategyComparison CreateComparison(
        KernelStrategyEvaluationAggregationRequest request,
        StrategyId strategyId,
        IReadOnlyList<KernelStrategyEvaluationSample> samples,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var observations = samples.SelectMany(CollectObservations).ToArray();
        var disagreements = samples.SelectMany(CollectDisagreements).ToArray();
        var scoredObservations = observations.Where(static item => item.Score.HasValue).ToArray();
        var aggregates = CreateMetricAggregates(scoredObservations);
        var missingEvidenceCount = samples.Count(SampleHasMissingEvidence);
        var hasObjectiveAnchor = observations.Any(static item => item.MetricKind == KernelEvaluationMetricKind.ObjectiveAnchor);
        var hasNonModelJudge = observations.Any(static item => item.MetricKind != KernelEvaluationMetricKind.ModelJudge);
        var hasModelJudge = observations.Any(static item => item.MetricKind == KernelEvaluationMetricKind.ModelJudge);
        var modelJudgeOnly = hasModelJudge && !hasObjectiveAnchor && !hasNonModelJudge;
        var objectiveAnchorConflictCount = disagreements.Count(static item => item.DisagreementKind == KernelEvaluationDisagreementKind.ObjectiveAnchorConflict);
        var requiresHumanGateFromDisagreements = disagreements.Any(static item => item.RequiresHumanGate);
        var blockingReasons = new List<string>();

        if (samples.Count < request.MinimumSamplesPerStrategy)
        {
            blockingReasons.Add("kernel.strategy_aggregation.insufficient_samples");
        }

        if (missingEvidenceCount > 0)
        {
            blockingReasons.Add("kernel.strategy_aggregation.missing_evidence");
        }

        if (modelJudgeOnly)
        {
            blockingReasons.Add("kernel.strategy_aggregation.model_judge_only");
        }

        if (objectiveAnchorConflictCount > 0)
        {
            blockingReasons.Add("kernel.strategy_aggregation.objective_anchor_conflict");
        }

        if (requiresHumanGateFromDisagreements)
        {
            blockingReasons.Add("kernel.strategy_aggregation.disagreement_requires_human_gate");
        }

        var gateState = ResolveGateState(samples.Count, request.MinimumSamplesPerStrategy, objectiveAnchorConflictCount, requiresHumanGateFromDisagreements, blockingReasons);
        var promotionReady = gateState == KernelStrategyEvaluationGateState.PromotionReady;

        return new KernelStrategyComparison(
            strategyId,
            samples.Count,
            aggregates,
            Average(scoredObservations.Select(static item => item.Score!.Value)),
            Average(observations.Select(static item => item.Confidence)),
            disagreements.Length,
            objectiveAnchorConflictCount,
            missingEvidenceCount,
            modelJudgeOnly,
            gateState,
            promotionReady,
            requiresHumanGate: gateState is KernelStrategyEvaluationGateState.RequiresHumanGate or KernelStrategyEvaluationGateState.BlockedByDisagreement,
            blockingReasons);
    }

    private static KernelStrategyEvaluationGateState ResolveGateState(
        int sampleCount,
        int minimumSamples,
        int objectiveAnchorConflictCount,
        bool requiresHumanGateFromDisagreements,
        IReadOnlyList<string> blockingReasons)
    {
        if (sampleCount < minimumSamples)
        {
            return KernelStrategyEvaluationGateState.InsufficientEvidence;
        }

        if (objectiveAnchorConflictCount > 0 || requiresHumanGateFromDisagreements)
        {
            return KernelStrategyEvaluationGateState.BlockedByDisagreement;
        }

        if (blockingReasons.Count > 0)
        {
            return KernelStrategyEvaluationGateState.RequiresHumanGate;
        }

        return KernelStrategyEvaluationGateState.PromotionReady;
    }

    private static IReadOnlyList<KernelStrategyMetricAggregate> CreateMetricAggregates(
        IReadOnlyList<KernelEvaluationMetricObservation> scoredObservations)
        => scoredObservations
            .GroupBy(SourceMetricId, StringComparer.Ordinal)
            .Select(group =>
            {
                var items = group.ToArray();
                var scores = items.Select(static item => item.Score!.Value).ToArray();
                return new KernelStrategyMetricAggregate(
                    group.Key,
                    items.Length,
                    Average(scores),
                    scores.Min(),
                    scores.Max(),
                    Average(items.Select(static item => item.Confidence)),
                    items.Count(static item => item.Estimated),
                    items.Select(static item => item.SignalKind).Distinct().OrderBy(static item => item).ToArray());
            })
            .OrderBy(static item => item.MetricId, StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<KernelEvaluationMetricObservation> CollectObservations(KernelStrategyEvaluationSample sample)
    {
        foreach (var observation in sample.Evaluation.Observations)
        {
            yield return observation;
        }

        if (sample.CrossReviewReport is not null)
        {
            foreach (var observation in sample.CrossReviewReport.Observations)
            {
                yield return observation;
            }
        }

        if (sample.ObjectiveAnchorCalibrationReport is not null)
        {
            foreach (var observation in sample.ObjectiveAnchorCalibrationReport.ObjectiveAnchorObservations)
            {
                yield return observation;
            }

            foreach (var observation in sample.ObjectiveAnchorCalibrationReport.CalibratedModelJudgeObservations)
            {
                yield return observation;
            }
        }
    }

    private static IEnumerable<KernelEvaluationDisagreement> CollectDisagreements(KernelStrategyEvaluationSample sample)
    {
        foreach (var disagreement in sample.Evaluation.Disagreements)
        {
            yield return disagreement;
        }

        if (sample.CrossReviewReport is not null)
        {
            foreach (var disagreement in sample.CrossReviewReport.Disagreements)
            {
                yield return disagreement;
            }
        }

        if (sample.ObjectiveAnchorCalibrationReport is not null)
        {
            foreach (var disagreement in sample.ObjectiveAnchorCalibrationReport.Disagreements)
            {
                yield return disagreement;
            }
        }
    }

    private static bool SampleHasMissingEvidence(KernelStrategyEvaluationSample sample)
        => sample.Evaluation.Observations.Count == 0
            || sample.Evaluation.Disagreements.Any(static item => item.DisagreementKind == KernelEvaluationDisagreementKind.MissingEvidence)
            || sample.Evaluation.Evidence.TraceRefs.Count == 0;

    private static string SourceMetricId(KernelEvaluationMetricObservation observation)
        => observation.Metadata.TryGetValue("sourceMetricId", out var sourceMetricId) && !string.IsNullOrWhiteSpace(sourceMetricId)
            ? sourceMetricId
            : observation.MetricId;

    private static decimal Average(IEnumerable<decimal> values)
    {
        var array = values.ToArray();
        return array.Length == 0 ? 0m : array.Average();
    }
}
