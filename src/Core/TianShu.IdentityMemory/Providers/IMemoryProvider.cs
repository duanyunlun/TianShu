using TianShu.Contracts.Memory;
using TianShu.Contracts.Primitives;

namespace TianShu.IdentityMemory;

/// <summary>
/// Identity / Memory 记忆 provider 适配接口。
/// Memory-provider adapter interface for the identity-memory plane.
/// </summary>
public interface IMemoryProvider
{
    MemoryProviderDescriptor Descriptor { get; }

    Task<IReadOnlyList<MemorySpace>> ListSpacesAsync(
        MemorySpaceId? memorySpaceId,
        CancellationToken cancellationToken);

    Task<MemoryMutationResult> AddAsync(
        AddMemory command,
        MemoryOperationContext context,
        CancellationToken cancellationToken);

    Task<MemoryMutationResult> ImportAsync(
        ImportMemory command,
        MemoryOperationContext context,
        CancellationToken cancellationToken);

    Task<MemoryQueryResult> ExportAsync(
        ExportMemory command,
        MemoryOperationContext context,
        CancellationToken cancellationToken);

    Task<MemoryQueryResult> FilterAsync(
        FilterMemory query,
        MemoryOperationContext context,
        CancellationToken cancellationToken);

    Task<MemoryReviewQueryResult> ListReviewsAsync(
        ListMemoryReviews query,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
        => Task.FromResult(new MemoryReviewQueryResult(degradedProviders: [$"{Descriptor.ProviderId}:unsupported_capability:review_list"]));

    Task<MemoryMutationResult> ForgetAsync(
        ForgetMemory command,
        MemoryOperationContext context,
        CancellationToken cancellationToken);

    Task<MemoryMutationResult> DeleteAsync(
        DeleteMemory command,
        MemoryOperationContext context,
        CancellationToken cancellationToken);

    Task<MemoryMutationResult> SupersedeAsync(
        SupersedeMemory command,
        MemoryOperationContext context,
        CancellationToken cancellationToken);

    Task<MemoryMutationResult> ApproveReviewAsync(
        ApproveMemoryReview command,
        MemoryOperationContext context,
        CancellationToken cancellationToken);

    Task<MemoryMutationResult> DemoteReviewAsync(
        DemoteMemoryReview command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
        => Task.FromResult(new MemoryMutationResult(false, DegradedReason: "unsupported_capability:review_demote", Effect: MemoryMutationEffect.Degraded));

    Task<MemoryMutationResult> MergeReviewAsync(
        MergeMemoryReview command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
        => Task.FromResult(new MemoryMutationResult(false, DegradedReason: "unsupported_capability:review_merge", Effect: MemoryMutationEffect.Degraded));

    Task<MemoryMutationResult> RestoreReviewAsync(
        RestoreMemoryReview command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
        => Task.FromResult(new MemoryMutationResult(false, DegradedReason: "unsupported_capability:review_restore", Effect: MemoryMutationEffect.Degraded));

    Task<MemoryMutationResult> RecordFeedbackAsync(
        RecordMemoryFeedback command,
        MemoryOperationContext context,
        CancellationToken cancellationToken);

    Task<MemoryMutationResult> RecordCitationAsync(
        RecordMemoryCitation command,
        MemoryOperationContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// 可在 mutation 执行前解析目标记忆空间的 provider 辅助接口。
/// Provider helper interface for resolving target memory spaces before a mutation is executed.
/// </summary>
public interface IMemoryProviderTargetSpaceResolver
{
    /// <summary>
    /// 解析 feedback 命令将要影响的记忆空间。
    /// Resolves memory spaces affected by a feedback command.
    /// </summary>
    Task<IReadOnlyList<MemorySpaceId>> ResolveFeedbackTargetSpacesAsync(
        RecordMemoryFeedback command,
        CancellationToken cancellationToken);

    /// <summary>
    /// 解析 citation 命令将要影响的记忆空间。
    /// Resolves memory spaces affected by a citation command.
    /// </summary>
    Task<IReadOnlyList<MemorySpaceId>> ResolveCitationTargetSpacesAsync(
        RecordMemoryCitation command,
        CancellationToken cancellationToken);
}

/// <summary>
/// 暴露 provider 背后的结构化状态存储入口，供默认本地实现写入 evidence、candidate 与 links。
/// Exposes structured state-store operations behind a provider for the default local memory pipeline.
/// </summary>
public interface IMemoryProviderStateStore
{
    /// <summary>
    /// 解析本地状态写入目标空间。
    /// Resolves the target space for local state writes.
    /// </summary>
    Task<MemorySpace?> ResolveStateSpaceAsync(
        MemorySpaceId memorySpaceId,
        CancellationToken cancellationToken);

    /// <summary>
    /// 读取目标空间事实，用于 policy 冲突判断。
    /// Lists facts for policy conflict evaluation.
    /// </summary>
    Task<IReadOnlyList<FactMemoryRecord>> ListStateFactsAsync(
        MemorySpace memorySpace,
        CancellationToken cancellationToken);

    /// <summary>
    /// 追加证据记录。
    /// Appends an evidence record.
    /// </summary>
    Task AppendEvidenceRecordAsync(
        MemoryEvidenceRecord evidenceRecord,
        CancellationToken cancellationToken);

    /// <summary>
    /// 写入候选记忆。
    /// Upserts a memory candidate.
    /// </summary>
    Task UpsertCandidateAsync(
        MemoryCandidate candidate,
        CancellationToken cancellationToken);

    /// <summary>
    /// 写入完整事实记录。
    /// Upserts a full fact record.
    /// </summary>
    Task<FactMemoryRecord> UpsertFactAsync(
        MemorySpace memorySpace,
        FactMemoryRecord fact,
        string actor,
        string source,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// 追加取代链路。
    /// Appends a supersede link.
    /// </summary>
    Task AppendSupersedeLinkAsync(
        MemorySpaceId memorySpaceId,
        MemorySupersedeLink link,
        CancellationToken cancellationToken);
}
