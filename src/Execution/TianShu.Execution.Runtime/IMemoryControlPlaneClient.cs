using TianShu.Contracts.Memory;

namespace TianShu.Execution.Runtime;

public interface IMemoryControlPlaneClient
{
    Task<IReadOnlyList<MemoryProviderDescriptor>> ListMemoryProvidersAsync(ListMemoryProviders query, CancellationToken cancellationToken);

    Task<IReadOnlyList<MemorySpace>> ListMemorySpacesAsync(ListMemorySpaces query, CancellationToken cancellationToken);

    Task<MemoryOverlay> ResolveMemoryOverlayAsync(ResolveMemoryOverlay query, CancellationToken cancellationToken);

    Task<MemoryQueryResult> FilterMemoryAsync(FilterMemory query, CancellationToken cancellationToken);

    Task<MemoryReviewQueryResult> ListMemoryReviewsAsync(ListMemoryReviews query, CancellationToken cancellationToken)
        => Task.FromResult(new MemoryReviewQueryResult(degradedProviders: ["unsupported_capability:review_list"]));

    Task<MemoryMutationResult> AddMemoryAsync(AddMemory command, CancellationToken cancellationToken);

    Task<IReadOnlyList<MemoryCandidate>> ExtractMemoryAsync(ExtractMemory command, CancellationToken cancellationToken);

    Task<MemoryMutationResult> ImportMemoryAsync(ImportMemory command, CancellationToken cancellationToken);

    Task<MemoryQueryResult> ExportMemoryAsync(ExportMemory command, CancellationToken cancellationToken);

    Task<MemoryMutationResult> BindMemoryProviderAsync(BindMemoryProvider command, CancellationToken cancellationToken);

    Task<MemoryConsolidationRunResult> RunMemoryConsolidationAsync(RunMemoryConsolidation command, CancellationToken cancellationToken);

    Task<MemoryMutationResult> ForgetMemoryAsync(ForgetMemory command, CancellationToken cancellationToken);

    Task<MemoryMutationResult> DeleteMemoryAsync(DeleteMemory command, CancellationToken cancellationToken);

    Task<MemoryMutationResult> SupersedeMemoryAsync(SupersedeMemory command, CancellationToken cancellationToken);

    Task<MemoryMutationResult> ApproveMemoryReviewAsync(ApproveMemoryReview command, CancellationToken cancellationToken);

    Task<MemoryMutationResult> DemoteMemoryReviewAsync(DemoteMemoryReview command, CancellationToken cancellationToken)
        => Task.FromResult(new MemoryMutationResult(false, DegradedReason: "unsupported_capability:review_demote", Effect: MemoryMutationEffect.Degraded));

    Task<MemoryMutationResult> MergeMemoryReviewAsync(MergeMemoryReview command, CancellationToken cancellationToken)
        => Task.FromResult(new MemoryMutationResult(false, DegradedReason: "unsupported_capability:review_merge", Effect: MemoryMutationEffect.Degraded));

    Task<MemoryMutationResult> RestoreMemoryReviewAsync(RestoreMemoryReview command, CancellationToken cancellationToken)
        => Task.FromResult(new MemoryMutationResult(false, DegradedReason: "unsupported_capability:review_restore", Effect: MemoryMutationEffect.Degraded));

    Task<MemoryMutationResult> RecordMemoryFeedbackAsync(RecordMemoryFeedback command, CancellationToken cancellationToken);

    Task<MemoryMutationResult> RecordMemoryCitationAsync(RecordMemoryCitation command, CancellationToken cancellationToken);
}
