using TianShu.Contracts.Memory;
using TianShu.Contracts.Primitives;

namespace TianShu.IdentityMemory;

/// <summary>
/// Identity / Memory 本地事实 store。
/// Local fact store for the identity-memory plane.
/// </summary>
public interface ITianShuLocalMemoryStore
{
    /// <summary>
    /// 读取本地已注册的记忆空间元数据。
    /// Lists locally registered memory-space metadata.
    /// </summary>
    Task<IReadOnlyList<MemorySpace>> ListSpacesAsync(
        MemorySpaceId? memorySpaceId,
        CancellationToken cancellationToken);

    /// <summary>
    /// 读取指定记忆空间中的结构化事实。
    /// Lists structured facts for a memory space.
    /// </summary>
    Task<IReadOnlyList<FactMemoryRecord>> ListFactsAsync(
        MemorySpace memorySpace,
        CancellationToken cancellationToken);

    /// <summary>
    /// 写入或覆盖一个结构化事实，并产生审计记录。
    /// Upserts a structured fact and emits an audit entry.
    /// </summary>
    Task<FactMemoryRecord> UpsertFactAsync(
        MemorySpace memorySpace,
        string key,
        StructuredValue value,
        TianShuIdentityMemoryContext context,
        string source,
        decimal confidence,
        CancellationToken cancellationToken);

    /// <summary>
    /// 写入或覆盖一个完整事实记录，并产生审计记录。
    /// Upserts a full fact record and emits an audit entry.
    /// </summary>
    Task<FactMemoryRecord> UpsertFactAsync(
        MemorySpace memorySpace,
        FactMemoryRecord fact,
        string actor,
        string source,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// 修改事实生命周期，并产生审计记录。
    /// Changes a fact lifecycle and emits an audit entry.
    /// </summary>
    Task<FactMemoryRecord?> ChangeFactLifecycleAsync(
        MemorySpace memorySpace,
        MemoryRecordId? memoryRecordId,
        string? key,
        MemoryLifecycleStatus lifecycleStatus,
        string actor,
        string source,
        DateTimeOffset occurredAt,
        string? reason,
        CancellationToken cancellationToken);

    /// <summary>
    /// 追加一条不直接修改事实载荷的记忆审计记录。
    /// Appends a memory audit entry that does not directly mutate fact payload.
    /// </summary>
    Task AppendAuditRecordAsync(
        TianShuMemoryAuditRecord auditRecord,
        CancellationToken cancellationToken);

    /// <summary>
    /// 读取本地记忆写入审计记录。
    /// Lists local memory write audit entries.
    /// </summary>
    Task<IReadOnlyList<TianShuMemoryAuditRecord>> ListAuditRecordsAsync(
        MemorySpaceId? memorySpaceId,
        CancellationToken cancellationToken);

    /// <summary>
    /// 追加一条记忆证据记录。
    /// Appends a memory evidence record.
    /// </summary>
    Task AppendEvidenceRecordAsync(
        MemoryEvidenceRecord evidenceRecord,
        CancellationToken cancellationToken);

    /// <summary>
    /// 读取记忆证据记录。
    /// Lists memory evidence records.
    /// </summary>
    Task<IReadOnlyList<MemoryEvidenceRecord>> ListEvidenceRecordsAsync(
        MemorySpaceId? memorySpaceId,
        CancellationToken cancellationToken);

    /// <summary>
    /// 写入或覆盖一个候选记忆。
    /// Upserts a candidate memory.
    /// </summary>
    Task UpsertCandidateAsync(
        MemoryCandidate candidate,
        CancellationToken cancellationToken);

    /// <summary>
    /// 读取候选记忆。
    /// Lists candidate memories.
    /// </summary>
    Task<IReadOnlyList<MemoryCandidate>> ListCandidatesAsync(
        MemorySpaceId? memorySpaceId,
        CancellationToken cancellationToken);

    /// <summary>
    /// 追加一条取代链路。
    /// Appends a supersede link.
    /// </summary>
    Task AppendSupersedeLinkAsync(
        MemorySpaceId memorySpaceId,
        MemorySupersedeLink link,
        CancellationToken cancellationToken);

    /// <summary>
    /// 读取取代链路。
    /// Lists supersede links.
    /// </summary>
    Task<IReadOnlyList<MemorySupersedeLink>> ListSupersedeLinksAsync(
        MemorySpaceId? memorySpaceId,
        CancellationToken cancellationToken);

    /// <summary>
    /// 读取已持久化的 provider 绑定。
    /// Lists persisted provider bindings.
    /// </summary>
    Task<IReadOnlyList<MemoryProviderBinding>> ListProviderBindingsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 用新的绑定快照替换已持久化的 provider 绑定。
    /// Replaces persisted provider bindings with the provided snapshot.
    /// </summary>
    Task ReplaceProviderBindingsAsync(
        IReadOnlyList<MemoryProviderBinding> bindings,
        CancellationToken cancellationToken);
}
