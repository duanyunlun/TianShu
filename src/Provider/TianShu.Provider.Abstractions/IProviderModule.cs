using TianShu.Contracts.Provider;

namespace TianShu.Provider.Abstractions;

/// <summary>
/// Provider Module 统一入口，只接受 Execution Runtime 物化后的 ProviderInvocationRequest。
/// Unified provider-module entry point that only accepts ProviderInvocationRequest materialized by Execution Runtime.
/// </summary>
public interface IProviderModule
{
    ProviderDescriptor Descriptor { get; }

    IAsyncEnumerable<ProviderStreamEvent> InvokeAsync(
        ProviderInvocationRequest request,
        CancellationToken cancellationToken);
}
