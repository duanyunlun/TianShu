using TianShu.Contracts.Memory;

namespace TianShu.IdentityMemory;

/// <summary>
/// 外部记忆 provider 的最小运行时配置。
/// Minimal runtime configuration for an external memory provider.
/// </summary>
public sealed record TianShuExternalMemoryProviderOptions
{
    public string ProviderId { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public string? DisplayName { get; init; }

    public bool Enabled { get; init; } = true;

    public string? Host { get; init; }

    public int? Port { get; init; }

    public int? GrpcPort { get; init; }

    public string? ApiKeyEnvironmentVariable { get; init; }

    public string? AuthorizationEnvironmentVariable { get; init; }

    public MemoryProviderBindingMode Mode { get; init; } = MemoryProviderBindingMode.ReadOnly;

    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromMilliseconds(750);
}
