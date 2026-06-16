using TianShu.Contracts.Memory;

namespace TianShu.IdentityMemory;

/// <summary>
/// 从安全 evidence 摘要构建探索学习轨迹，不保存完整思考链或原始 token 流。
/// Builds exploratory-learning traces from safe evidence summaries without storing chain-of-thought or raw token streams.
/// </summary>
public sealed class MemoryLearningTraceBuilder
{
    public MemoryLearningTrace Build(
        MemoryCandidate candidate,
        IEnumerable<MemoryEvidenceRecord> evidenceRecords,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(evidenceRecords);

        var evidence = evidenceRecords.ToArray();
        var attempts = evidence
            .Where(static item => item.EvidenceKind is MemoryEvidenceKind.CommandFailure or MemoryEvidenceKind.CommandSuccess or MemoryEvidenceKind.TestResult)
            .Select(static item => new MemoryLearningAttemptSummary(
                item.SafeSummary,
                item.EvidenceKind.ToString(),
                item.EvidenceKind == MemoryEvidenceKind.CommandFailure ? "failed evidence" : null))
            .ToArray();
        var rejected = evidence
            .Where(static item => item.EvidenceKind == MemoryEvidenceKind.CommandFailure)
            .Select(static item => item.SafeSummary)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var validationIds = evidence
            .Select(static item => item.EvidenceId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new MemoryLearningTrace(
            $"{candidate.MemorySpaceId.Value}:{candidate.Key}",
            attempts,
            rejected,
            candidate.IsCounterexample ? null : candidate.ExtractionReason,
            validationIds,
            createdAt: timestamp);
    }
}
