using TianShu.Contracts.Memory;

namespace TianShu.IdentityMemory;

/// <summary>
/// 记忆来源强度，用于默认提升策略判断是否可自动落地。
/// Memory source strength used by the default promotion policy to decide whether auto promotion is allowed.
/// </summary>
public enum MemorySourceStrength
{
    Unknown = 0,
    Weak = 1,
    Normal = 2,
    Strong = 3,
}

/// <summary>
/// 录入证据的策略上下文。
/// Policy context for evidence ingestion.
/// </summary>
public sealed record MemoryIngestionPolicyContext(
    MemoryOperationContext OperationContext,
    MemoryProviderDescriptor ProviderDescriptor,
    MemoryScopeKind? SourceScopeKind = null,
    MemoryScopeKind? TargetScopeKind = null,
    MemorySourceStrength SourceStrength = MemorySourceStrength.Unknown,
    bool TargetSpaceIsReadOnly = false)
{
    public MemoryOperationContext OperationContext { get; } =
        OperationContext ?? throw new ArgumentNullException(nameof(OperationContext));

    public MemoryProviderDescriptor ProviderDescriptor { get; } =
        ProviderDescriptor ?? throw new ArgumentNullException(nameof(ProviderDescriptor));
}

/// <summary>
/// 候选提升的策略上下文。
/// Policy context for memory-candidate promotion.
/// </summary>
public sealed record MemoryPromotionPolicyContext(
    MemoryOperationContext OperationContext,
    MemoryProviderDescriptor ProviderDescriptor,
    IReadOnlyList<FactMemoryRecord>? ExistingFacts = null,
    MemoryScopeKind? SourceScopeKind = null,
    MemoryScopeKind? TargetScopeKind = null,
    MemorySourceStrength SourceStrength = MemorySourceStrength.Unknown,
    bool TargetSpaceIsReadOnly = false)
{
    public MemoryOperationContext OperationContext { get; } =
        OperationContext ?? throw new ArgumentNullException(nameof(OperationContext));

    public MemoryProviderDescriptor ProviderDescriptor { get; } =
        ProviderDescriptor ?? throw new ArgumentNullException(nameof(ProviderDescriptor));

    public IReadOnlyList<FactMemoryRecord> ExistingFacts { get; } =
        ExistingFacts ?? Array.Empty<FactMemoryRecord>();
}
