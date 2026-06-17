using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;

namespace TianShu.Kernel.Abstractions;

/// <summary>
/// Adaptive 候选验证检查类别。
/// Adaptive candidate validation check category.
/// </summary>
public enum AdaptiveCandidateValidationCheckKind
{
    Unspecified = 0,
    Schema = 1,
    DeterministicKernel = 2,
    Governance = 3,
    Budget = 4,
    Capability = 5,
}

/// <summary>
/// Adaptive 候选验证状态。
/// Adaptive candidate validation status.
/// </summary>
public enum AdaptiveCandidateValidationStatus
{
    Unspecified = 0,
    Accepted = 1,
    Rejected = 2,
    Skipped = 3,
}

/// <summary>
/// Adaptive 候选验证请求。
/// Adaptive candidate validation request.
/// </summary>
public sealed record AdaptiveCandidateValidationRequest
{
    public AdaptiveCandidateValidationRequest(
        KernelProposalSet proposalSet,
        KernelValidationContext context,
        MetadataBag? metadata = null)
    {
        ProposalSet = proposalSet ?? throw new ArgumentNullException(nameof(proposalSet));
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public KernelProposalSet ProposalSet { get; }

    public KernelValidationContext Context { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// 单项候选检查记录。
/// Single candidate check record.
/// </summary>
public sealed record AdaptiveCandidateValidationCheckRecord
{
    public AdaptiveCandidateValidationCheckRecord(
        AdaptiveCandidateValidationCheckKind checkKind,
        AdaptiveCandidateValidationStatus status,
        KernelValidationResult result,
        string? sourceRef = null,
        MetadataBag? metadata = null)
    {
        CheckKind = checkKind;
        Status = status;
        Result = result ?? throw new ArgumentNullException(nameof(result));
        SourceRef = sourceRef;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public AdaptiveCandidateValidationCheckKind CheckKind { get; }

    public AdaptiveCandidateValidationStatus Status { get; }

    public KernelValidationResult Result { get; }

    public string? SourceRef { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// 单个 Adaptive 候选的验证记录。
/// Validation record for one adaptive candidate.
/// </summary>
public sealed record AdaptiveCandidateValidationRecord
{
    public AdaptiveCandidateValidationRecord(
        KernelProposalId proposalId,
        KernelProposalKind proposalKind,
        AdaptiveCandidateValidationStatus status,
        IReadOnlyList<AdaptiveCandidateValidationCheckRecord>? checks = null,
        StageGraphId? graphId = null,
        MetadataBag? metadata = null)
    {
        ProposalId = proposalId;
        ProposalKind = proposalKind;
        Status = status;
        Checks = checks ?? Array.Empty<AdaptiveCandidateValidationCheckRecord>();
        GraphId = graphId;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public KernelProposalId ProposalId { get; }

    public KernelProposalKind ProposalKind { get; }

    public AdaptiveCandidateValidationStatus Status { get; }

    public IReadOnlyList<AdaptiveCandidateValidationCheckRecord> Checks { get; }

    public StageGraphId? GraphId { get; }

    public MetadataBag Metadata { get; }

    public bool IsAccepted => Status == AdaptiveCandidateValidationStatus.Accepted;
}

/// <summary>
/// Adaptive 候选验证报告；报告只作为证据，不代表候选已经执行或提升。
/// Adaptive candidate validation report; evidence only, not execution or promotion.
/// </summary>
public sealed record AdaptiveCandidateValidationReport
{
    public AdaptiveCandidateValidationReport(
        IReadOnlyList<AdaptiveCandidateValidationRecord>? records = null,
        string? rationaleRef = null,
        MetadataBag? metadata = null)
    {
        Records = records ?? Array.Empty<AdaptiveCandidateValidationRecord>();
        RationaleRef = rationaleRef;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public IReadOnlyList<AdaptiveCandidateValidationRecord> Records { get; }

    public string? RationaleRef { get; }

    public MetadataBag Metadata { get; }

    public int AcceptedCount => Records.Count(static record => record.IsAccepted);

    public int RejectedCount => Records.Count(static record => record.Status == AdaptiveCandidateValidationStatus.Rejected);

    public bool HasAcceptedCandidate => AcceptedCount > 0;
}
