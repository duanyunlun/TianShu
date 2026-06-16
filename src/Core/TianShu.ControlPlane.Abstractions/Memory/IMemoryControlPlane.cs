using TianShu.Contracts.Memory;

namespace TianShu.ControlPlane.Abstractions.Memory;

/// <summary>
/// 记忆平面 northbound 抽象。
/// Northbound abstraction for the memory plane.
/// </summary>
public interface IMemoryControlPlane
{
    /// <summary>
    /// 查询可用记忆 provider。
    /// Lists available memory providers.
    /// </summary>
    Task<IReadOnlyList<MemoryProviderDescriptor>> ListMemoryProvidersAsync(ListMemoryProviders query, CancellationToken cancellationToken);

    /// <summary>
    /// 查询记忆空间列表。
    /// Lists memory spaces.
    /// </summary>
    Task<IReadOnlyList<MemorySpace>> ListMemorySpacesAsync(ListMemorySpaces query, CancellationToken cancellationToken);

    /// <summary>
    /// 解析记忆覆盖层。
    /// Resolves a memory overlay.
    /// </summary>
    Task<MemoryOverlay> ResolveMemoryOverlayAsync(ResolveMemoryOverlay query, CancellationToken cancellationToken);

    /// <summary>
    /// 筛选结构化记忆事实。
    /// Filters structured memory facts.
    /// </summary>
    Task<MemoryQueryResult> FilterMemoryAsync(FilterMemory query, CancellationToken cancellationToken);

    /// <summary>
    /// 查询记忆审核项。
    /// Lists memory review items.
    /// </summary>
    Task<MemoryReviewQueryResult> ListMemoryReviewsAsync(ListMemoryReviews query, CancellationToken cancellationToken)
        => Task.FromResult(new MemoryReviewQueryResult(degradedProviders: ["unsupported_capability:review_list"]));

    /// <summary>
    /// 添加一条结构化记忆事实。
    /// Adds a structured memory fact.
    /// </summary>
    Task<MemoryMutationResult> AddMemoryAsync(AddMemory command, CancellationToken cancellationToken);

    /// <summary>
    /// 从来源中抽取候选记忆。
    /// Extracts candidate memories from a source.
    /// </summary>
    Task<IReadOnlyList<MemoryCandidate>> ExtractMemoryAsync(ExtractMemory command, CancellationToken cancellationToken);

    /// <summary>
    /// 导入外部记忆事实。
    /// Imports memory facts from an external source.
    /// </summary>
    Task<MemoryMutationResult> ImportMemoryAsync(ImportMemory command, CancellationToken cancellationToken);

    /// <summary>
    /// 导出记忆事实。
    /// Exports memory facts.
    /// </summary>
    Task<MemoryQueryResult> ExportMemoryAsync(ExportMemory command, CancellationToken cancellationToken);

    /// <summary>
    /// 绑定记忆 provider。
    /// Binds a memory provider.
    /// </summary>
    Task<MemoryMutationResult> BindMemoryProviderAsync(BindMemoryProvider command, CancellationToken cancellationToken);

    /// <summary>
    /// 触发一轮记忆整理。
    /// Runs one memory consolidation pass.
    /// </summary>
    Task<MemoryConsolidationRunResult> RunMemoryConsolidationAsync(RunMemoryConsolidation command, CancellationToken cancellationToken);

    /// <summary>
    /// 遗忘一条记忆。
    /// Forgets a memory.
    /// </summary>
    Task<MemoryMutationResult> ForgetMemoryAsync(ForgetMemory command, CancellationToken cancellationToken);

    /// <summary>
    /// 删除一条记忆。
    /// Deletes a memory.
    /// </summary>
    Task<MemoryMutationResult> DeleteMemoryAsync(DeleteMemory command, CancellationToken cancellationToken);

    /// <summary>
    /// 用新事实取代旧记忆并保留取代链。
    /// Supersedes a memory with a replacement fact while preserving the supersede link.
    /// </summary>
    Task<MemoryMutationResult> SupersedeMemoryAsync(SupersedeMemory command, CancellationToken cancellationToken);

    /// <summary>
    /// 批准待审记忆。
    /// Approves a pending-review memory.
    /// </summary>
    Task<MemoryMutationResult> ApproveMemoryReviewAsync(ApproveMemoryReview command, CancellationToken cancellationToken);

    /// <summary>
    /// 降权待审记忆。
    /// Demotes a pending-review memory.
    /// </summary>
    Task<MemoryMutationResult> DemoteMemoryReviewAsync(DemoteMemoryReview command, CancellationToken cancellationToken)
        => Task.FromResult(new MemoryMutationResult(false, DegradedReason: "unsupported_capability:review_demote", Effect: MemoryMutationEffect.Degraded));

    /// <summary>
    /// 合并待审记忆。
    /// Merges a pending-review memory.
    /// </summary>
    Task<MemoryMutationResult> MergeMemoryReviewAsync(MergeMemoryReview command, CancellationToken cancellationToken)
        => Task.FromResult(new MemoryMutationResult(false, DegradedReason: "unsupported_capability:review_merge", Effect: MemoryMutationEffect.Degraded));

    /// <summary>
    /// 恢复待审记忆。
    /// Restores a memory review item.
    /// </summary>
    Task<MemoryMutationResult> RestoreMemoryReviewAsync(RestoreMemoryReview command, CancellationToken cancellationToken)
        => Task.FromResult(new MemoryMutationResult(false, DegradedReason: "unsupported_capability:review_restore", Effect: MemoryMutationEffect.Degraded));

    /// <summary>
    /// 记录记忆反馈。
    /// Records memory feedback.
    /// </summary>
    Task<MemoryMutationResult> RecordMemoryFeedbackAsync(RecordMemoryFeedback command, CancellationToken cancellationToken);

    /// <summary>
    /// 记录记忆引用。
    /// Records memory citations.
    /// </summary>
    Task<MemoryMutationResult> RecordMemoryCitationAsync(RecordMemoryCitation command, CancellationToken cancellationToken);
}
