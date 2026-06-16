using TianShu.Contracts.Identity;
using TianShu.Contracts.Memory;

namespace TianShu.Execution.Runtime.ControlPlane;

public sealed partial class RuntimeControlPlaneAdapter
{
    public Task<Account?> GetAccountProfileAsync(GetAccountProfile query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return runtime.GetAccountProfileAsync(query, cancellationToken);
    }

    public Task<IReadOnlyList<DeviceBinding>> ListBoundDevicesAsync(ListBoundDevices query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return runtime.ListBoundDevicesAsync(query, cancellationToken);
    }

    public Task<IReadOnlyList<MemoryProviderDescriptor>> ListMemoryProvidersAsync(ListMemoryProviders query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return runtime.ListMemoryProvidersAsync(query, cancellationToken);
    }

    public Task<IReadOnlyList<MemorySpace>> ListMemorySpacesAsync(ListMemorySpaces query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return runtime.ListMemorySpacesAsync(query, cancellationToken);
    }

    public Task<MemoryOverlay> ResolveMemoryOverlayAsync(ResolveMemoryOverlay query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return runtime.ResolveMemoryOverlayAsync(query, cancellationToken);
    }

    public Task<MemoryQueryResult> FilterMemoryAsync(FilterMemory query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return runtime.FilterMemoryAsync(query, cancellationToken);
    }

    public Task<MemoryReviewQueryResult> ListMemoryReviewsAsync(ListMemoryReviews query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return runtime.ListMemoryReviewsAsync(query, cancellationToken);
    }

    public Task<MemoryMutationResult> AddMemoryAsync(AddMemory command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.AddMemoryAsync(command, cancellationToken);
    }

    public Task<IReadOnlyList<MemoryCandidate>> ExtractMemoryAsync(ExtractMemory command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.ExtractMemoryAsync(command, cancellationToken);
    }

    public Task<MemoryMutationResult> ImportMemoryAsync(ImportMemory command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.ImportMemoryAsync(command, cancellationToken);
    }

    public Task<MemoryQueryResult> ExportMemoryAsync(ExportMemory command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.ExportMemoryAsync(command, cancellationToken);
    }

    public Task<MemoryMutationResult> BindMemoryProviderAsync(BindMemoryProvider command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.BindMemoryProviderAsync(command, cancellationToken);
    }

    public Task<MemoryConsolidationRunResult> RunMemoryConsolidationAsync(RunMemoryConsolidation command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.RunMemoryConsolidationAsync(command, cancellationToken);
    }

    public Task<MemoryMutationResult> ForgetMemoryAsync(ForgetMemory command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.ForgetMemoryAsync(command, cancellationToken);
    }

    public Task<MemoryMutationResult> DeleteMemoryAsync(DeleteMemory command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.DeleteMemoryAsync(command, cancellationToken);
    }

    public Task<MemoryMutationResult> SupersedeMemoryAsync(SupersedeMemory command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.SupersedeMemoryAsync(command, cancellationToken);
    }

    public Task<MemoryMutationResult> ApproveMemoryReviewAsync(ApproveMemoryReview command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.ApproveMemoryReviewAsync(command, cancellationToken);
    }

    public Task<MemoryMutationResult> DemoteMemoryReviewAsync(DemoteMemoryReview command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.DemoteMemoryReviewAsync(command, cancellationToken);
    }

    public Task<MemoryMutationResult> MergeMemoryReviewAsync(MergeMemoryReview command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.MergeMemoryReviewAsync(command, cancellationToken);
    }

    public Task<MemoryMutationResult> RestoreMemoryReviewAsync(RestoreMemoryReview command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.RestoreMemoryReviewAsync(command, cancellationToken);
    }

    public Task<MemoryMutationResult> RecordMemoryFeedbackAsync(RecordMemoryFeedback command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.RecordMemoryFeedbackAsync(command, cancellationToken);
    }

    public Task<MemoryMutationResult> RecordMemoryCitationAsync(RecordMemoryCitation command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.RecordMemoryCitationAsync(command, cancellationToken);
    }
}
