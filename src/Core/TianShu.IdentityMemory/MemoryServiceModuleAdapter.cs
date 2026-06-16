using TianShu.Contracts.Memory;
using TianShu.Contracts.Modules;

namespace TianShu.IdentityMemory;

/// <summary>
/// 将现有 IMemoryService 包裹为 Memory Module 统一入口。
/// Adapts the existing IMemoryService to the unified Memory Module entry point.
/// </summary>
public sealed class MemoryServiceModuleAdapter : IMemoryModule
{
    private readonly IMemoryService memoryService;

    public MemoryServiceModuleAdapter(IMemoryService memoryService, ModuleDescriptor? descriptor = null)
    {
        this.memoryService = memoryService ?? throw new ArgumentNullException(nameof(memoryService));
        Descriptor = descriptor ?? BuiltInModuleDescriptors.MemoryIdentity();
    }

    public ModuleDescriptor Descriptor { get; }

    public ValueTask<ModuleSmokeCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var providers = memoryService.ListProviders();
        var passed = providers.Count > 0;
        return ValueTask.FromResult(new ModuleSmokeCheckResult(
            Descriptor.ModuleId,
            passed,
            passed ? ModuleHealthStatus.Healthy : ModuleHealthStatus.Degraded,
            passed ? null : "memory_provider_unavailable"));
    }

    public async ValueTask<MemoryModuleQueryResult> QueryAsync(
        MemoryModuleQueryInvocation invocation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        return invocation.Query switch
        {
            ListMemoryProvidersModuleQuery query => new MemoryModuleQueryResult(
                providers: memoryService.ListProviders(query.Query)),
            ListMemorySpacesModuleQuery query => new MemoryModuleQueryResult(
                spaces: await memoryService.ListSpacesAsync(query.Query, cancellationToken).ConfigureAwait(false)),
            FilterMemoryModuleQuery query => new MemoryModuleQueryResult(
                records: await memoryService.FilterAsync(query.Query, invocation.Context.OperationContext, cancellationToken).ConfigureAwait(false)),
            ListMemoryReviewsModuleQuery query => new MemoryModuleQueryResult(
                reviews: await memoryService.ListReviewsAsync(query.Query, invocation.Context.OperationContext, cancellationToken).ConfigureAwait(false)),
            ExportMemoryModuleQuery query => new MemoryModuleQueryResult(
                exported: await memoryService.ExportAsync(query.Query, invocation.Context.OperationContext, cancellationToken).ConfigureAwait(false)),
            ResolveMemoryOverlayModuleQuery => new MemoryModuleQueryResult(
                degradedProviders: ["unsupported_capability:overlay_resolve"]),
            _ => new MemoryModuleQueryResult(degradedProviders: ["unsupported_query"]),
        };
    }

    public ValueTask<MemoryMutationResult> MutateAsync(
        MemoryModuleMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        var context = invocation.Context.OperationContext;
        var result = invocation.Mutation switch
        {
            AddMemoryModuleMutation mutation => memoryService.AddAsync(mutation.Command, context, cancellationToken),
            ImportMemoryModuleMutation mutation => memoryService.ImportAsync(mutation.Command, context, cancellationToken),
            BindMemoryProviderModuleMutation mutation => memoryService.BindProviderAsync(mutation.Command, context, cancellationToken),
            ForgetMemoryModuleMutation mutation => memoryService.ForgetAsync(mutation.Command, context, cancellationToken),
            DeleteMemoryModuleMutation mutation => memoryService.DeleteAsync(mutation.Command, context, cancellationToken),
            SupersedeMemoryModuleMutation mutation => memoryService.SupersedeAsync(mutation.Command, context, cancellationToken),
            ApproveMemoryReviewModuleMutation mutation => memoryService.ApproveReviewAsync(mutation.Command, context, cancellationToken),
            RecordMemoryFeedbackModuleMutation mutation => memoryService.RecordFeedbackAsync(mutation.Command, context, cancellationToken),
            RecordMemoryCitationModuleMutation mutation => memoryService.RecordCitationAsync(mutation.Command, context, cancellationToken),
            _ => Task.FromResult(new MemoryMutationResult(false, DegradedReason: "unsupported_mutation", Effect: MemoryMutationEffect.Degraded)),
        };

        return new ValueTask<MemoryMutationResult>(result);
    }
}
