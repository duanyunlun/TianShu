namespace TianShu.AppHost.Tools.Runtime;

internal sealed class ContextSlicePlanner
{
    private readonly IContextTokenEstimator? estimator;

    public ContextSlicePlanner(IContextTokenEstimator? estimator = null)
    {
        this.estimator = estimator;
    }

    public ContextSliceResult Plan(ContextSliceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var effectiveEstimator = estimator ?? new ApproximateContextTokenEstimator(request.ProviderId);
        var budget = request.BudgetProfile.GetEffectiveSoftBudgetTokens(request.ProviderEffectiveContextLimitTokens);
        var candidates = EstimateSegments(request.CandidateSegments, effectiveEstimator);
        var totalTokens = candidates.Sum(static item => item.Segment.EstimatedTokens);

        var included = new List<ContextSegment>();
        var summarized = new List<ContextSegment>();
        var referenceOnly = new List<ContextSegment>();
        var dropped = new List<DroppedContextSegment>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var criticalCandidates = candidates
            .Where(static item => IsCritical(item.Segment))
            .OrderBy(static item => item.Index)
            .ToArray();

        var includedTokens = 0;
        foreach (var candidate in criticalCandidates)
        {
            if (!seen.Add(candidate.Segment.Id))
            {
                dropped.Add(new DroppedContextSegment(candidate.Segment, DroppedContextReason.Duplicate));
                continue;
            }

            included.Add(candidate.Segment);
            includedTokens += candidate.Segment.EstimatedTokens;
        }

        var remaining = candidates
            .Where(static item => !IsCritical(item.Segment))
            .OrderByDescending(static item => Score(item.Segment))
            .ThenBy(static item => item.Index);

        foreach (var candidate in remaining)
        {
            var segment = candidate.Segment;
            if (!seen.Add(segment.Id))
            {
                dropped.Add(new DroppedContextSegment(segment, DroppedContextReason.Duplicate));
                continue;
            }

            if (segment.RetentionPolicy == ContextRetentionPolicy.DropFirst)
            {
                dropped.Add(new DroppedContextSegment(segment, DroppedContextReason.PolicyExcluded));
                continue;
            }

            if (includedTokens + segment.EstimatedTokens <= budget)
            {
                included.Add(segment);
                includedTokens += segment.EstimatedTokens;
                continue;
            }

            RouteOverflowSegment(segment, summarized, referenceOnly, dropped);
        }

        var overBudgetReasonCode = ResolveOverBudgetReasonCode(request, totalTokens, includedTokens, budget);
        var report = new ContextSlicingReport
        {
            ThreadId = request.ThreadId,
            TurnId = request.TurnId,
            ModelId = request.ModelId,
            ProviderId = request.ProviderId,
            ProviderEffectiveContextLimitTokens = request.ProviderEffectiveContextLimitTokens,
            TianShuBudgetTokens = budget,
            EstimatedTotalTokens = totalTokens,
            EstimatedIncludedTokens = includedTokens,
            IncludedSegments = included.Select(static segment => ToReportEntry(segment, null)).ToArray(),
            SummarizedSegments = summarized.Select(static segment => ToReportEntry(segment, DroppedContextReason.SupersededBySummary)).ToArray(),
            ReferenceOnlySegments = referenceOnly.Select(static segment => ToReportEntry(segment, DroppedContextReason.BudgetExceeded)).ToArray(),
            DroppedSegments = dropped.Select(static item => ToReportEntry(item.Segment, item.Reason)).ToArray(),
            OverBudgetReasonCode = overBudgetReasonCode,
        };

        return new ContextSliceResult
        {
            IncludedSegments = included,
            SummarizedSegments = summarized,
            ReferenceOnlySegments = referenceOnly,
            DroppedSegments = dropped,
            Report = report,
        };
    }

    private static IReadOnlyList<EstimatedSegment> EstimateSegments(
        IReadOnlyList<ContextSegment> segments,
        IContextTokenEstimator estimator)
    {
        var estimated = new List<EstimatedSegment>(segments.Count);
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            var estimate = estimator.Estimate(segment);
            var normalized = segment.EstimatedTokens == estimate.Tokens
                ? segment
                : segment with { EstimatedTokens = Math.Max(1, estimate.Tokens) };

            estimated.Add(new EstimatedSegment(index, normalized));
        }

        return estimated;
    }

    private static bool IsCritical(ContextSegment segment)
        => segment.Priority == ContextSegmentPriority.Critical
           || segment.RetentionPolicy == ContextRetentionPolicy.MustKeep;

    private static double Score(ContextSegment segment)
    {
        var priority = segment.Priority switch
        {
            ContextSegmentPriority.Critical => 500,
            ContextSegmentPriority.High => 400,
            ContextSegmentPriority.MediumHigh => 300,
            ContextSegmentPriority.Medium => 200,
            _ => 100,
        };

        var policy = segment.RetentionPolicy switch
        {
            ContextRetentionPolicy.KeepIfRelevant => 30,
            ContextRetentionPolicy.SummarizeIfDropped => 20,
            ContextRetentionPolicy.ReferenceOnlyIfDropped => 10,
            ContextRetentionPolicy.DropFirst => -1_000,
            _ => 0,
        };

        var confidence = Math.Clamp(segment.Confidence, 0.0d, 1.0d) * 50;
        var relevance = Math.Clamp(segment.RelevanceScore, 0.0d, 1.0d) * 100;
        return priority + policy + confidence + relevance + segment.AuthorityWeight + segment.RecencyWeight;
    }

    private static void RouteOverflowSegment(
        ContextSegment segment,
        List<ContextSegment> summarized,
        List<ContextSegment> referenceOnly,
        List<DroppedContextSegment> dropped)
    {
        switch (segment.RetentionPolicy)
        {
            case ContextRetentionPolicy.SummarizeIfDropped:
                summarized.Add(segment);
                break;
            case ContextRetentionPolicy.ReferenceOnlyIfDropped:
                referenceOnly.Add(segment);
                break;
            default:
                dropped.Add(new DroppedContextSegment(segment, DroppedContextReason.BudgetExceeded));
                break;
        }
    }

    private static OverBudgetReasonCode? ResolveOverBudgetReasonCode(
        ContextSliceRequest request,
        int totalTokens,
        int includedTokens,
        int budget)
    {
        if (request.ProviderEffectiveContextLimitTokens is > 0
            && includedTokens > request.ProviderEffectiveContextLimitTokens.Value)
        {
            return OverBudgetReasonCode.ProviderLimitReached;
        }

        if (totalTokens <= budget && includedTokens <= budget)
        {
            return null;
        }

        return request.RequestedOverBudgetReason ?? OverBudgetReasonCode.DefaultBudgetExceeded;
    }

    private static ContextSegmentReportEntry ToReportEntry(
        ContextSegment segment,
        DroppedContextReason? reason)
        => new(
            segment.Id,
            segment.Kind,
            segment.Priority,
            segment.EstimatedTokens,
            reason,
            segment.SourceRefs);

    private sealed record EstimatedSegment(int Index, ContextSegment Segment);
}
