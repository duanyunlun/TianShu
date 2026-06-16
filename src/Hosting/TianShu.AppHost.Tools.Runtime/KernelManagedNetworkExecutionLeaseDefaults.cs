using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tools.Runtime;

internal static class KernelManagedNetworkExecutionLeaseDefaults
{
    public static IKernelManagedNetworkExecutionLease Inactive { get; } = new InactiveKernelManagedNetworkExecutionLease();

    private sealed class InactiveKernelManagedNetworkExecutionLease : IKernelManagedNetworkExecutionLease
    {
        public bool IsActive => false;

        public string? HttpProxyUrl => null;

        public string? SocksProxyUrl => null;

        public bool HasRejectedOutcome => false;

        public long GetBlockedRequestTotal()
            => 0;

        public IReadOnlyDictionary<string, string> ApplyToEnvironment(IReadOnlyDictionary<string, string>? baseEnvironment)
            => baseEnvironment ?? new Dictionary<string, string>(StringComparer.Ordinal);

        public KernelExecToolCallOutput ApplyOutcome(KernelExecToolCallOutput output)
            => output;

        public string ConsumeOutcomeMessage()
            => string.Empty;

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }
}
