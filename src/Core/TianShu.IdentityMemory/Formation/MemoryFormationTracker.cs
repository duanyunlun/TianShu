using TianShu.Contracts.Memory;

namespace TianShu.IdentityMemory;

/// <summary>
/// 生成 Phase A 记忆形成路径摘要，把类比迁移和探索学习压缩为可审计 metadata。
/// Produces Phase A memory-formation summaries by compressing analogical transfer and exploratory learning into auditable metadata.
/// </summary>
public sealed class MemoryFormationTracker
{
    private readonly MemoryTransferLinkBuilder transferLinkBuilder;
    private readonly MemoryLearningTraceBuilder learningTraceBuilder;

    public MemoryFormationTracker(
        MemoryTransferLinkBuilder? transferLinkBuilder = null,
        MemoryLearningTraceBuilder? learningTraceBuilder = null)
    {
        this.transferLinkBuilder = transferLinkBuilder ?? new MemoryTransferLinkBuilder();
        this.learningTraceBuilder = learningTraceBuilder ?? new MemoryLearningTraceBuilder();
    }

    public MemoryFormationSnapshot Track(
        MemoryCandidate candidate,
        IReadOnlyList<FactMemoryRecord> existingFacts,
        IReadOnlyList<MemoryEvidenceRecord> evidenceRecords,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(existingFacts);
        ArgumentNullException.ThrowIfNull(evidenceRecords);

        var transferLink = transferLinkBuilder.Build(candidate, existingFacts, timestamp);
        var learningTrace = learningTraceBuilder.Build(candidate, evidenceRecords, timestamp);
        var formationPath = candidate.FormationPath;
        if (formationPath == MemoryFormationPath.Unknown)
        {
            formationPath = transferLink is not null
                ? MemoryFormationPath.AnalogicalTransfer
                : MemoryFormationPath.ExploratoryLearning;
        }

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["formationPath"] = formationPath.ToString(),
            ["problemSignature"] = learningTrace.ProblemSignature,
        };
        if (transferLink is not null)
        {
            metadata["transferBasis"] = transferLink.SimilarityBasis;
            metadata["transferSourceCount"] = transferLink.SourceRecordIds.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return new MemoryFormationSnapshot(formationPath, transferLink, learningTrace, metadata);
    }
}

public sealed record MemoryFormationSnapshot(
    MemoryFormationPath FormationPath,
    MemoryTransferLink? TransferLink,
    MemoryLearningTrace LearningTrace,
    IReadOnlyDictionary<string, string> Metadata);
