using TianShu.Contracts.Memory;
using TianShu.Contracts.Primitives;

namespace TianShu.IdentityMemory;

/// <summary>
/// 空本地记忆 store，用于保持默认实现的无持久化兼容行为。
/// Empty local memory store that preserves the default non-persistent behavior.
/// </summary>
public sealed class EmptyTianShuLocalMemoryStore : ITianShuLocalMemoryStore
{
    public static EmptyTianShuLocalMemoryStore Instance { get; } = new();

    private EmptyTianShuLocalMemoryStore()
    {
    }

    public Task<IReadOnlyList<MemorySpace>> ListSpacesAsync(
        MemorySpaceId? memorySpaceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<MemorySpace>>(Array.Empty<MemorySpace>());
    }

    public Task<IReadOnlyList<FactMemoryRecord>> ListFactsAsync(
        MemorySpace memorySpace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(memorySpace);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<FactMemoryRecord>>(Array.Empty<FactMemoryRecord>());
    }

    public Task<FactMemoryRecord> UpsertFactAsync(
        MemorySpace memorySpace,
        string key,
        StructuredValue value,
        TianShuIdentityMemoryContext context,
        string source,
        decimal confidence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(memorySpace);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("默认空记忆 store 不支持写入。");
    }

    public Task<FactMemoryRecord> UpsertFactAsync(
        MemorySpace memorySpace,
        FactMemoryRecord fact,
        string actor,
        string source,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(memorySpace);
        ArgumentNullException.ThrowIfNull(fact);
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("默认空记忆 store 不支持写入。");
    }

    public Task<FactMemoryRecord?> ChangeFactLifecycleAsync(
        MemorySpace memorySpace,
        MemoryRecordId? memoryRecordId,
        string? key,
        MemoryLifecycleStatus lifecycleStatus,
        string actor,
        string source,
        DateTimeOffset occurredAt,
        string? reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(memorySpace);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<FactMemoryRecord?>(null);
    }

    public Task AppendAuditRecordAsync(
        TianShuMemoryAuditRecord auditRecord,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditRecord);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TianShuMemoryAuditRecord>> ListAuditRecordsAsync(
        MemorySpaceId? memorySpaceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<TianShuMemoryAuditRecord>>(Array.Empty<TianShuMemoryAuditRecord>());
    }

    public Task AppendEvidenceRecordAsync(
        MemoryEvidenceRecord evidenceRecord,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evidenceRecord);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MemoryEvidenceRecord>> ListEvidenceRecordsAsync(
        MemorySpaceId? memorySpaceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<MemoryEvidenceRecord>>(Array.Empty<MemoryEvidenceRecord>());
    }

    public Task UpsertCandidateAsync(
        MemoryCandidate candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MemoryCandidate>> ListCandidatesAsync(
        MemorySpaceId? memorySpaceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<MemoryCandidate>>(Array.Empty<MemoryCandidate>());
    }

    public Task AppendSupersedeLinkAsync(
        MemorySpaceId memorySpaceId,
        MemorySupersedeLink link,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MemorySupersedeLink>> ListSupersedeLinksAsync(
        MemorySpaceId? memorySpaceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<MemorySupersedeLink>>(Array.Empty<MemorySupersedeLink>());
    }

    public Task<IReadOnlyList<MemoryProviderBinding>> ListProviderBindingsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<MemoryProviderBinding>>(Array.Empty<MemoryProviderBinding>());
    }

    public Task ReplaceProviderBindingsAsync(
        IReadOnlyList<MemoryProviderBinding> bindings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
