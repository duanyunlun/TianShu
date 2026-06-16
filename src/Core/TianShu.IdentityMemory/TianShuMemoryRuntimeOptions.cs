using TianShu.Contracts.Memory;

namespace TianShu.IdentityMemory;

/// <summary>
/// 描述 TianShu 官方本地记忆运行时配置。
/// Describes runtime configuration for the built-in TianShu local memory implementation.
/// </summary>
public sealed record TianShuMemoryRuntimeOptions
{
    public static TianShuMemoryRuntimeOptions Default { get; } = new();

    public bool Enabled { get; init; } = true;

    public string DefaultProfileId { get; init; } = "workspace";

    public IReadOnlyList<TianShuMemoryProfileOptions> Profiles { get; init; } = Array.Empty<TianShuMemoryProfileOptions>();

    public IReadOnlyList<TianShuMemorySpaceOptions> Spaces { get; init; } = Array.Empty<TianShuMemorySpaceOptions>();

    public IReadOnlyList<TianShuMemoryProviderBindingOptions> Bindings { get; init; } = Array.Empty<TianShuMemoryProviderBindingOptions>();

    public string ResolvedDefaultProfileId
        => string.IsNullOrWhiteSpace(DefaultProfileId) ? "workspace" : DefaultProfileId.Trim();

    public TianShuMemoryProfileOptions ResolveDefaultProfile()
        => Profiles.FirstOrDefault(profile => string.Equals(profile.ProfileId, ResolvedDefaultProfileId, StringComparison.OrdinalIgnoreCase))
           ?? Profiles.FirstOrDefault()
           ?? TianShuMemoryProfileOptions.Default;
}

/// <summary>
/// 描述一个 memory profile 的本地运行策略。
/// Describes local runtime behavior for a memory profile.
/// </summary>
public sealed record TianShuMemoryProfileOptions(
    string ProfileId,
    bool Enabled = true,
    string? DefaultSpace = null,
    bool Overlay = true,
    TianShuMemoryExtractMode Extract = TianShuMemoryExtractMode.Manual,
    string Retention = "keep")
{
    public static TianShuMemoryProfileOptions Default { get; } = new("workspace");
}

public enum TianShuMemoryExtractMode
{
    Off = 0,
    Manual = 1,
    Background = 2,
}

/// <summary>
/// 描述配置文件声明的本地记忆空间。
/// Describes a memory space declared by configuration.
/// </summary>
public sealed record TianShuMemorySpaceOptions(
    string SpaceKey,
    MemoryScopeKind ScopeKind,
    string? ProviderId = null,
    bool ReadOnly = false,
    string? DisplayName = null,
    string? ScopeKey = null);

/// <summary>
/// 描述配置文件声明的 provider 绑定。
/// Describes a provider binding declared by configuration.
/// </summary>
public sealed record TianShuMemoryProviderBindingOptions(
    string BindingId,
    string Space,
    string ProviderId,
    string Mode = "read-only",
    IReadOnlyList<string>? Capabilities = null);
