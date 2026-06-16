using TianShu.Contracts.Memory;
using TianShu.Contracts.Primitives;

namespace TianShu.IdentityMemory;

/// <summary>
/// 进程内本地记忆 store，适合测试、嵌入式宿主和短期会话。
/// In-process local memory store for tests, embedded hosts, and short-lived sessions.
/// </summary>
public sealed class InMemoryTianShuLocalMemoryStore : ITianShuLocalMemoryStore
{
    private readonly object gate = new();
    private readonly Dictionary<string, MemorySpace> spaces = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FactMemoryRecord> facts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MemoryCandidate> candidates = new(StringComparer.Ordinal);
    private readonly List<MemoryEvidenceRecord> evidenceRecords = [];
    private readonly List<(MemorySpaceId MemorySpaceId, MemorySupersedeLink Link)> supersedeLinks = [];
    private readonly List<TianShuMemoryAuditRecord> auditRecords = [];
    private readonly List<MemoryProviderBinding> providerBindings = [];

    public Task<IReadOnlyList<MemorySpace>> ListSpacesAsync(
        MemorySpaceId? memorySpaceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            var results = spaces.Values
                .Where(space => memorySpaceId is null || string.Equals(space.Id.Value, memorySpaceId.Value.Value, StringComparison.Ordinal))
                .OrderBy(static space => space.Id.Value, StringComparer.Ordinal)
                .ToArray();
            return Task.FromResult<IReadOnlyList<MemorySpace>>(results);
        }
    }

    public Task<IReadOnlyList<FactMemoryRecord>> ListFactsAsync(
        MemorySpace memorySpace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(memorySpace);
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            var results = facts.Values
                .Where(fact => string.Equals(fact.MemorySpaceId.Value, memorySpace.Id.Value, StringComparison.Ordinal))
                .OrderBy(static fact => fact.Key, StringComparer.Ordinal)
                .ToArray();
            return Task.FromResult<IReadOnlyList<FactMemoryRecord>>(results);
        }
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

        var normalizedKey = IdentifierGuard.AgainstNullOrWhiteSpace(key, nameof(key));
        var normalizedSource = IdentifierGuard.AgainstNullOrWhiteSpace(source, nameof(source));
        var fact = new FactMemoryRecord(
            normalizedKey,
            value,
            memorySpace.Id,
            confidence,
            context.SnapshotTime,
            sources:
            [
                new MemorySourceRef(
                    MemorySourceKind.System,
                    normalizedSource,
                    role: "system",
                    capturedAt: context.SnapshotTime)
            ]);
        var audit = MemoryAuditRecords.Create(
            "upsert_fact",
            memorySpace.Id,
            normalizedKey,
            context.AccountId.Value,
            normalizedSource,
            context.SnapshotTime,
            value,
            confidence,
            MemoryMutationEffect.Upserted);

        lock (gate)
        {
            spaces[memorySpace.Id.Value] = memorySpace;
            facts[BuildRecordKey(fact.Id)] = fact;
            auditRecords.Add(audit);
        }

        return Task.FromResult(fact);
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

        if (!string.Equals(fact.MemorySpaceId.Value, memorySpace.Id.Value, StringComparison.Ordinal))
        {
            throw new ArgumentException("事实记录必须属于目标记忆空间。", nameof(fact));
        }

        var normalizedActor = IdentifierGuard.AgainstNullOrWhiteSpace(actor, nameof(actor));
        var normalizedSource = IdentifierGuard.AgainstNullOrWhiteSpace(source, nameof(source));
        var audit = MemoryAuditRecords.Create(
            "upsert_fact",
            memorySpace.Id,
            fact.Key,
            normalizedActor,
            normalizedSource,
            occurredAt,
            fact.Value,
            fact.Confidence,
            MemoryMutationEffect.Upserted);

        lock (gate)
        {
            spaces[memorySpace.Id.Value] = memorySpace;
            facts[BuildRecordKey(fact.Id)] = fact;
            auditRecords.Add(audit);
        }

        return Task.FromResult(fact);
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

        var normalizedActor = IdentifierGuard.AgainstNullOrWhiteSpace(actor, nameof(actor));
        var normalizedSource = IdentifierGuard.AgainstNullOrWhiteSpace(source, nameof(source));
        var normalizedKey = string.IsNullOrWhiteSpace(key) ? null : IdentifierGuard.AgainstNullOrWhiteSpace(key!, nameof(key));

        lock (gate)
        {
            var existing = facts.Values.FirstOrDefault(fact =>
                string.Equals(fact.MemorySpaceId.Value, memorySpace.Id.Value, StringComparison.Ordinal)
                && (memoryRecordId is not { } id || string.Equals(fact.Id.Value, id.Value, StringComparison.Ordinal))
                && (normalizedKey is null || string.Equals(fact.Key, normalizedKey, StringComparison.Ordinal)));

            if (existing is null)
            {
                return Task.FromResult<FactMemoryRecord?>(null);
            }

            var updated = existing.WithLifecycle(lifecycleStatus, occurredAt);
            facts[BuildRecordKey(updated.Id)] = updated;
            var effect = lifecycleStatus == MemoryLifecycleStatus.Deleted
                ? MemoryMutationEffect.SoftDeleted
                : MemoryMutationEffect.LifecycleChanged;
            auditRecords.Add(MemoryAuditRecords.Create(
                ResolveLifecycleOperation(lifecycleStatus),
                memorySpace.Id,
                updated.Key,
                normalizedActor,
                normalizedSource,
                occurredAt,
                effect: effect,
                reason: reason));
            return Task.FromResult<FactMemoryRecord?>(updated);
        }
    }

    public Task<IReadOnlyList<TianShuMemoryAuditRecord>> ListAuditRecordsAsync(
        MemorySpaceId? memorySpaceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            var results = auditRecords
                .Where(record => memorySpaceId is null || string.Equals(record.MemorySpaceId.Value, memorySpaceId.Value.Value, StringComparison.Ordinal))
                .OrderBy(static record => record.OccurredAt)
                .ToArray();
            return Task.FromResult<IReadOnlyList<TianShuMemoryAuditRecord>>(results);
        }
    }

    public Task AppendAuditRecordAsync(
        TianShuMemoryAuditRecord auditRecord,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditRecord);
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            auditRecords.Add(auditRecord);
        }

        return Task.CompletedTask;
    }

    public Task AppendEvidenceRecordAsync(
        MemoryEvidenceRecord evidenceRecord,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evidenceRecord);
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            evidenceRecords.Add(evidenceRecord);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MemoryEvidenceRecord>> ListEvidenceRecordsAsync(
        MemorySpaceId? memorySpaceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            var results = evidenceRecords
                .Where(record => memorySpaceId is null || string.Equals(record.MemorySpaceId.Value, memorySpaceId.Value.Value, StringComparison.Ordinal))
                .OrderBy(static record => record.CapturedAt)
                .ToArray();
            return Task.FromResult<IReadOnlyList<MemoryEvidenceRecord>>(results);
        }
    }

    public Task UpsertCandidateAsync(
        MemoryCandidate candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            candidates[BuildFactKey(candidate.MemorySpaceId, candidate.Key)] = candidate;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MemoryCandidate>> ListCandidatesAsync(
        MemorySpaceId? memorySpaceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            var results = candidates.Values
                .Where(candidate => memorySpaceId is null || string.Equals(candidate.MemorySpaceId.Value, memorySpaceId.Value.Value, StringComparison.Ordinal))
                .OrderBy(static candidate => candidate.Key, StringComparer.Ordinal)
                .ToArray();
            return Task.FromResult<IReadOnlyList<MemoryCandidate>>(results);
        }
    }

    public Task AppendSupersedeLinkAsync(
        MemorySpaceId memorySpaceId,
        MemorySupersedeLink link,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            supersedeLinks.Add((memorySpaceId, link));
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MemorySupersedeLink>> ListSupersedeLinksAsync(
        MemorySpaceId? memorySpaceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            var results = supersedeLinks
                .Where(item => memorySpaceId is null || string.Equals(item.MemorySpaceId.Value, memorySpaceId.Value.Value, StringComparison.Ordinal))
                .OrderBy(static item => item.Link.Timestamp)
                .Select(static item => item.Link)
                .ToArray();
            return Task.FromResult<IReadOnlyList<MemorySupersedeLink>>(results);
        }
    }

    public Task<IReadOnlyList<MemoryProviderBinding>> ListProviderBindingsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<MemoryProviderBinding>>(providerBindings.ToArray());
        }
    }

    public Task ReplaceProviderBindingsAsync(
        IReadOnlyList<MemoryProviderBinding> bindings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            providerBindings.Clear();
            providerBindings.AddRange(bindings);
        }

        return Task.CompletedTask;
    }

    private static string BuildFactKey(MemorySpaceId memorySpaceId, string key)
        => $"{memorySpaceId.Value}\n{key}";

    private static string BuildRecordKey(MemoryRecordId memoryRecordId)
        => memoryRecordId.Value;

    private static string ResolveLifecycleOperation(MemoryLifecycleStatus lifecycleStatus)
        => lifecycleStatus switch
        {
            MemoryLifecycleStatus.Forgotten => "forget_fact",
            MemoryLifecycleStatus.Deleted => "delete_fact",
            _ => "change_fact_lifecycle",
        };
}
