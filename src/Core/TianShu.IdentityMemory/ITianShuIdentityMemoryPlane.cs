using TianShu.Contracts.Identity;
using TianShu.Contracts.Memory;

namespace TianShu.IdentityMemory;

/// <summary>
/// Identity / Memory 正式服务边界。
/// Formal service boundary for the identity and memory plane.
/// </summary>
public interface ITianShuIdentityMemoryPlane
{
    /// <summary>
    /// 读取账户画像。
    /// Gets an account profile.
    /// </summary>
    Task<Account?> GetAccountProfileAsync(
        GetAccountProfile query,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// 读取账户绑定设备列表。
    /// Gets the devices bound to an account.
    /// </summary>
    Task<IReadOnlyList<DeviceBinding>> ListBoundDevicesAsync(
        ListBoundDevices query,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// 查询可用记忆 provider。
    /// Lists available memory providers.
    /// </summary>
    Task<IReadOnlyList<MemoryProviderDescriptor>> ListMemoryProvidersAsync(
        ListMemoryProviders query,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// 查询记忆空间列表。
    /// Lists memory spaces.
    /// </summary>
    Task<IReadOnlyList<MemorySpace>> ListMemorySpacesAsync(
        ListMemorySpaces query,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// 解析记忆覆盖层。
    /// Resolves a memory overlay.
    /// </summary>
    Task<MemoryOverlay> ResolveMemoryOverlayAsync(
        ResolveMemoryOverlay query,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// 筛选结构化记忆事实。
    /// Filters structured memory facts.
    /// </summary>
    Task<MemoryQueryResult> FilterMemoryAsync(
        FilterMemory query,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// 查询记忆审核项。
    /// Lists memory review items.
    /// </summary>
    Task<MemoryReviewQueryResult> ListMemoryReviewsAsync(
        ListMemoryReviews query,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken)
        => Task.FromResult(new MemoryReviewQueryResult(degradedProviders: ["unsupported_capability:review_list"]));

    /// <summary>
    /// 添加一条结构化记忆事实。
    /// Adds a structured memory fact.
    /// </summary>
    Task<MemoryMutationResult> AddMemoryAsync(
        AddMemory command,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// 从来源中抽取候选记忆。
    /// Extracts candidate memories from a source.
    /// </summary>
    Task<IReadOnlyList<MemoryCandidate>> ExtractMemoryAsync(
        ExtractMemory command,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// 导入外部记忆事实。
    /// Imports memory facts from an external source.
    /// </summary>
    Task<MemoryMutationResult> ImportMemoryAsync(
        ImportMemory command,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// 导出记忆事实。
    /// Exports memory facts.
    /// </summary>
    Task<MemoryQueryResult> ExportMemoryAsync(
        ExportMemory command,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// 绑定记忆 provider。
    /// Binds a memory provider.
    /// </summary>
    Task<MemoryMutationResult> BindMemoryProviderAsync(
        BindMemoryProvider command,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// 触发一轮记忆整理。
    /// Runs one memory consolidation pass.
    /// </summary>
    Task<MemoryConsolidationRunResult> RunMemoryConsolidationAsync(
        RunMemoryConsolidation command,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// 遗忘一条记忆。
    /// Forgets a memory.
    /// </summary>
    Task<MemoryMutationResult> ForgetMemoryAsync(
        ForgetMemory command,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// 删除一条记忆。
    /// Deletes a memory.
    /// </summary>
    Task<MemoryMutationResult> DeleteMemoryAsync(
        DeleteMemory command,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// 用一条新事实取代旧事实。
    /// Supersedes an old fact with a new fact.
    /// </summary>
    Task<MemoryMutationResult> SupersedeMemoryAsync(
        SupersedeMemory command,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// 批准待审记忆。
    /// Approves a pending-review memory.
    /// </summary>
    Task<MemoryMutationResult> ApproveMemoryReviewAsync(
        ApproveMemoryReview command,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// 降权待审记忆。
    /// Demotes a pending-review memory.
    /// </summary>
    Task<MemoryMutationResult> DemoteMemoryReviewAsync(
        DemoteMemoryReview command,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken)
        => Task.FromResult(new MemoryMutationResult(false, DegradedReason: "unsupported_capability:review_demote", Effect: MemoryMutationEffect.Degraded));

    /// <summary>
    /// 合并待审记忆。
    /// Merges a pending-review memory.
    /// </summary>
    Task<MemoryMutationResult> MergeMemoryReviewAsync(
        MergeMemoryReview command,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken)
        => Task.FromResult(new MemoryMutationResult(false, DegradedReason: "unsupported_capability:review_merge", Effect: MemoryMutationEffect.Degraded));

    /// <summary>
    /// 恢复待审记忆。
    /// Restores a memory review item.
    /// </summary>
    Task<MemoryMutationResult> RestoreMemoryReviewAsync(
        RestoreMemoryReview command,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken)
        => Task.FromResult(new MemoryMutationResult(false, DegradedReason: "unsupported_capability:review_restore", Effect: MemoryMutationEffect.Degraded));

    /// <summary>
    /// 记录记忆反馈。
    /// Records memory feedback.
    /// </summary>
    Task<MemoryMutationResult> RecordMemoryFeedbackAsync(
        RecordMemoryFeedback command,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// 记录记忆引用。
    /// Records memory citations.
    /// </summary>
    Task<MemoryMutationResult> RecordMemoryCitationAsync(
        RecordMemoryCitation command,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken);
}
