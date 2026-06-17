using TianShu.Contracts.Kernel;
using TianShu.Kernel.Abstractions;

namespace TianShu.Kernel.Strategies;

/// <summary>
/// 默认客观锚点校准服务；只消费已采集的 build/test/golden answer/human label 锚点，不直接执行外部命令。
/// Default objective-anchor calibration service; consumes collected build/test/golden-answer/human-label anchors without directly executing external commands.
/// </summary>
public sealed class KernelObjectiveAnchorCalibrationService : IKernelObjectiveAnchorCalibrationService
{
    public Task<KernelObjectiveAnchorCalibrationReport> CalibrateAsync(
        KernelObjectiveAnchorCalibrationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var anchorObservations = request.ObjectiveAnchors
            .Select(anchor => CreateAnchorObservation(request, anchor))
            .ToArray();
        var objectiveAnchorScore = Average(request.ObjectiveAnchors.Select(static item => item.Score));
        var modelJudgeScore = Average(request.ModelJudgeObservations.Select(static item => item.Score ?? 0m));
        var originalConfidence = Average(request.ModelJudgeObservations.Select(static item => item.Confidence));
        var disagreements = DetectConflicts(request, anchorObservations).ToArray();
        var maxConflictSeverity = disagreements.Length == 0 ? 0m : disagreements.Max(static item => item.Severity);
        var calibratedConfidence = Clamp(originalConfidence * (1m - maxConflictSeverity));
        var calibratedModelJudgeObservations = request.ModelJudgeObservations
            .Select(item => CalibrateModelJudgeObservation(request, item, calibratedConfidence))
            .ToArray();
        var evidence = new KernelEvaluationEvidenceSet(
            traceRefs: new[] { $"trace://{request.TraceId.Value}" },
            objectiveAnchorRefs: request.ObjectiveAnchors.Select(static item => item.EvidenceRef).Distinct(StringComparer.Ordinal).ToArray(),
            modelJudgeRefs: request.ModelJudgeObservations.Select(static item => item.EvidenceRef).Distinct(StringComparer.Ordinal).ToArray(),
            diagnosticRefs: disagreements.Select(static item => item.EvidenceRef).ToArray());

        var report = new KernelObjectiveAnchorCalibrationReport(
            request.CalibrationId,
            request.RunId,
            request.TraceId,
            evidence,
            anchorObservations,
            calibratedModelJudgeObservations,
            disagreements,
            objectiveAnchorScore,
            modelJudgeScore,
            originalConfidence,
            calibratedConfidence,
            requiresHumanGate: disagreements.Any(static item => item.RequiresHumanGate));

        return Task.FromResult(report);
    }

    private static KernelEvaluationMetricObservation CreateAnchorObservation(
        KernelObjectiveAnchorCalibrationRequest request,
        KernelObjectiveAnchorObservation anchor)
        => new(
            $"objective_anchor.{request.CalibrationId}.{anchor.AnchorId}.{anchor.MetricId}",
            KernelEvaluationMetricKind.ObjectiveAnchor,
            KernelEvaluationSignalKind.ObjectiveAnchor,
            anchor.EvidenceRef,
            score: anchor.Score,
            confidence: anchor.Confidence,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["calibrationId"] = request.CalibrationId,
                ["anchorId"] = anchor.AnchorId,
                ["anchorKind"] = anchor.AnchorKind.ToString(),
                ["sourceMetricId"] = anchor.MetricId,
                ["reason"] = anchor.Reason,
            });

    private static KernelEvaluationMetricObservation CalibrateModelJudgeObservation(
        KernelObjectiveAnchorCalibrationRequest request,
        KernelEvaluationMetricObservation observation,
        decimal calibratedConfidence)
    {
        var metadata = new Dictionary<string, string>(observation.Metadata, StringComparer.Ordinal)
        {
            ["calibrationId"] = request.CalibrationId,
            ["originalConfidence"] = observation.Confidence.ToString("0.#############################", System.Globalization.CultureInfo.InvariantCulture),
            ["calibratedConfidence"] = calibratedConfidence.ToString("0.#############################", System.Globalization.CultureInfo.InvariantCulture),
        };

        return new KernelEvaluationMetricObservation(
            $"{observation.MetricId}.calibrated.{request.CalibrationId}",
            observation.MetricKind,
            observation.SignalKind,
            observation.EvidenceRef,
            score: observation.Score,
            observedValue: observation.ObservedValue,
            unit: observation.Unit,
            weight: observation.Weight,
            confidence: calibratedConfidence,
            estimated: observation.Estimated,
            metadata: metadata);
    }

    private static IEnumerable<KernelEvaluationDisagreement> DetectConflicts(
        KernelObjectiveAnchorCalibrationRequest request,
        IReadOnlyList<KernelEvaluationMetricObservation> anchorObservations)
    {
        var modelGroups = request.ModelJudgeObservations
            .Where(static item => item.Score.HasValue)
            .GroupBy(SourceMetricId, StringComparer.Ordinal)
            .ToDictionary(static item => item.Key, static item => item.ToArray(), StringComparer.Ordinal);

        foreach (var anchorGroup in anchorObservations.GroupBy(SourceMetricId, StringComparer.Ordinal))
        {
            if (!modelGroups.TryGetValue(anchorGroup.Key, out var modelGroup))
            {
                continue;
            }

            var anchorScore = Average(anchorGroup.Select(static item => item.Score ?? 0m));
            var modelScore = Average(modelGroup.Select(static item => item.Score!.Value));
            var spread = Math.Abs(anchorScore - modelScore);
            if (spread < request.ConflictThreshold)
            {
                continue;
            }

            yield return new KernelEvaluationDisagreement(
                $"objective-anchor.conflict.{request.CalibrationId}.{anchorGroup.Key}",
                KernelEvaluationDisagreementKind.ObjectiveAnchorConflict,
                anchorGroup.Select(static item => item.MetricId).Concat(modelGroup.Select(static item => item.MetricId)).ToArray(),
                $"客观锚点指标 {anchorGroup.Key} 与模型裁判分歧为 {spread:0.###}，达到阈值 {request.ConflictThreshold:0.###}。",
                $"diagnostics://objective-anchor/{request.CalibrationId}/conflict/{anchorGroup.Key}",
                severity: spread,
                requiresHumanGate: true);
        }
    }

    private static string SourceMetricId(KernelEvaluationMetricObservation observation)
        => observation.Metadata.TryGetValue("sourceMetricId", out var sourceMetricId) && !string.IsNullOrWhiteSpace(sourceMetricId)
            ? sourceMetricId
            : observation.MetricId;

    private static decimal Average(IEnumerable<decimal> values)
    {
        var array = values.ToArray();
        return array.Length == 0 ? 0m : array.Average();
    }

    private static decimal Clamp(decimal value)
        => value < 0m ? 0m : value > 1m ? 1m : value;
}
