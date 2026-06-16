using TianShu.Contracts.Memory;

namespace TianShu.IdentityMemory;

/// <summary>
/// 记忆系统的默认服务入口，消费方应通过它使用语义动作。
/// Default memory service entrypoint for semantic memory operations.
/// </summary>
public interface IMemoryService
{
    IReadOnlyList<MemoryProviderDescriptor> ListProviders();

    IReadOnlyList<MemoryProviderDescriptor> ListProviders(ListMemoryProviders query);

    Task<IReadOnlyList<MemorySpace>> ListSpacesAsync(
        ListMemorySpaces query,
        CancellationToken cancellationToken);

    Task<MemoryMutationResult> AddAsync(
        AddMemory command,
        MemoryOperationContext context,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MemoryCandidate>> ExtractAsync(
        ExtractMemory command,
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

    Task<MemoryMutationResult> BindProviderAsync(
        BindMemoryProvider command,
        MemoryOperationContext context,
        CancellationToken cancellationToken);

    Task<MemoryQueryResult> FilterAsync(
        FilterMemory query,
        MemoryOperationContext context,
        CancellationToken cancellationToken);

    Task<MemoryReviewQueryResult> ListReviewsAsync(
        ListMemoryReviews query,
        MemoryOperationContext context,
        CancellationToken cancellationToken);

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
        CancellationToken cancellationToken);

    Task<MemoryMutationResult> MergeReviewAsync(
        MergeMemoryReview command,
        MemoryOperationContext context,
        CancellationToken cancellationToken);

    Task<MemoryMutationResult> RestoreReviewAsync(
        RestoreMemoryReview command,
        MemoryOperationContext context,
        CancellationToken cancellationToken);

    Task<MemoryMutationResult> RecordFeedbackAsync(
        RecordMemoryFeedback command,
        MemoryOperationContext context,
        CancellationToken cancellationToken);

    Task<MemoryMutationResult> RecordCitationAsync(
        RecordMemoryCitation command,
        MemoryOperationContext context,
        CancellationToken cancellationToken);
}
