namespace TianShu.AppHost.Tools;

/// <summary>
/// managed-network execution lease 的宿主侧最小行为契约。
/// Minimal host-side behavior contract for managed-network execution leases.
/// </summary>
internal interface IKernelManagedNetworkExecutionLease : IAsyncDisposable
{
    bool IsActive { get; }

    string? HttpProxyUrl { get; }

    string? SocksProxyUrl { get; }

    bool HasRejectedOutcome { get; }

    long GetBlockedRequestTotal();

    IReadOnlyDictionary<string, string> ApplyToEnvironment(IReadOnlyDictionary<string, string>? baseEnvironment);

    KernelExecToolCallOutput ApplyOutcome(KernelExecToolCallOutput output);

    string ConsumeOutcomeMessage();
}
