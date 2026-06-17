using TianShu.Contracts.Kernel;
using TianShu.Kernel.Abstractions;

namespace TianShu.Kernel.Strategies;

/// <summary>
/// 默认异质交叉评审实验服务；只聚合已提交的模型裁判信号，不直接调用 provider。
/// Default heterogeneous cross-review experiment service; aggregates submitted model-judge signals without directly invoking providers.
/// </summary>
public sealed class KernelCrossReviewExperimentService : IKernelCrossReviewExperimentService
{
    public Task<KernelCrossReviewExperimentReport> RunAsync(
        KernelCrossReviewExperimentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var reviewers = request.Reviewers.ToDictionary(static item => item.ReviewerId, StringComparer.Ordinal);
        var reviewerReports = new List<KernelCrossReviewReviewerReport>();
        var observations = new List<KernelEvaluationMetricObservation>();

        foreach (var submission in request.Submissions.OrderBy(static item => item.ReviewerId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!reviewers.TryGetValue(submission.ReviewerId, out var reviewer))
            {
                throw new InvalidOperationException($"未知交叉评审者：{submission.ReviewerId}");
            }

            if (!string.Equals(submission.ModelRouteRef, reviewer.ModelRouteRef, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"交叉评审者 {submission.ReviewerId} 的 model route 与声明不一致。");
            }

            var reviewerObservations = submission.Scores
                .Select(score => CreateObservation(request, reviewer, submission, score))
                .ToArray();
            observations.AddRange(reviewerObservations);
            reviewerReports.Add(new KernelCrossReviewReviewerReport(
                reviewer.ReviewerId,
                reviewer.ModelRouteRef,
                reviewerObservations,
                $"cross-review.summary.{request.ExperimentId}.{submission.ReviewId}",
                Average(submission.Scores.Select(static item => item.Score)),
                Average(submission.Scores.Select(static item => item.Confidence)),
                Average(submission.Scores.Select(static item => item.Uncertainty))));
        }

        var disagreements = DetectDisagreements(request, observations);
        var evidence = new KernelEvaluationEvidenceSet(
            traceRefs: new[] { $"trace://{request.TraceId.Value}" },
            modelJudgeRefs: request.Submissions.Select(static item => item.EvidenceRef).Distinct(StringComparer.Ordinal).ToArray(),
            diagnosticRefs: disagreements.Select(static item => item.EvidenceRef).ToArray());

        var report = new KernelCrossReviewExperimentReport(
            request.ExperimentId,
            request.RunId,
            request.TraceId,
            request.ExecutorRef,
            evidence,
            reviewerReports,
            observations,
            disagreements,
            Average(request.Submissions.SelectMany(static item => item.Scores).Select(static item => item.Score)),
            Average(request.Submissions.SelectMany(static item => item.Scores).Select(static item => item.Confidence)),
            Average(request.Submissions.SelectMany(static item => item.Scores).Select(static item => item.Uncertainty)),
            requiresHumanGate: disagreements.Any(static item => item.RequiresHumanGate));

        return Task.FromResult(report);
    }

    private static KernelEvaluationMetricObservation CreateObservation(
        KernelCrossReviewExperimentRequest request,
        KernelCrossReviewReviewerSpec reviewer,
        KernelCrossReviewSubmission submission,
        KernelCrossReviewMetricScore score)
        => new(
            $"cross_review.{request.ExperimentId}.{submission.ReviewerId}.{score.MetricId}",
            KernelEvaluationMetricKind.ModelJudge,
            KernelEvaluationSignalKind.ModelJudge,
            score.EvidenceRef,
            score: score.Score,
            confidence: score.Confidence,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["experimentId"] = request.ExperimentId,
                ["executorRef"] = request.ExecutorRef,
                ["reviewId"] = submission.ReviewId,
                ["reviewerId"] = reviewer.ReviewerId,
                ["modelRouteRef"] = reviewer.ModelRouteRef,
                ["providerKey"] = reviewer.ProviderKey,
                ["model"] = reviewer.Model,
                ["sourceMetricId"] = score.MetricId,
                ["reason"] = score.Reason,
                ["uncertainty"] = score.Uncertainty.ToString("0.#############################", System.Globalization.CultureInfo.InvariantCulture),
            });

    private static IReadOnlyList<KernelEvaluationDisagreement> DetectDisagreements(
        KernelCrossReviewExperimentRequest request,
        IReadOnlyList<KernelEvaluationMetricObservation> observations)
    {
        var disagreements = new List<KernelEvaluationDisagreement>();
        var groups = observations
            .Where(static item => item.Metadata.ContainsKey("sourceMetricId"))
            .GroupBy(static item => item.Metadata["sourceMetricId"], StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var metricObservations = group.ToArray();
            if (metricObservations.Length < 2)
            {
                continue;
            }

            var scored = metricObservations.Where(static item => item.Score.HasValue).ToArray();
            if (scored.Length < 2)
            {
                continue;
            }

            var min = scored.Min(static item => item.Score!.Value);
            var max = scored.Max(static item => item.Score!.Value);
            var spread = max - min;
            if (spread < request.DisagreementThreshold)
            {
                continue;
            }

            disagreements.Add(new KernelEvaluationDisagreement(
                $"cross-review.disagreement.{request.ExperimentId}.{group.Key}",
                KernelEvaluationDisagreementKind.ModelJudgeDisagreement,
                scored.Select(static item => item.MetricId).ToArray(),
                $"交叉评审指标 {group.Key} 的模型裁判分歧为 {spread:0.###}，达到阈值 {request.DisagreementThreshold:0.###}。",
                $"diagnostics://cross-review/{request.ExperimentId}/disagreement/{group.Key}",
                severity: spread,
                requiresHumanGate: true));
        }

        return disagreements;
    }

    private static decimal Average(IEnumerable<decimal> values)
    {
        var array = values.ToArray();
        return array.Length == 0 ? 0m : array.Average();
    }
}
