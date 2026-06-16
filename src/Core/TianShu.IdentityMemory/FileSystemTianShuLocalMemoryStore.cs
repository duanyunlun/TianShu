using System.Text.Json;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Primitives;

namespace TianShu.IdentityMemory;

/// <summary>
/// 文件系统本地记忆 store，以 JSON 文件保存事实与审计记录。
/// File-system local memory store that persists facts and audit entries as JSON.
/// </summary>
public sealed class FileSystemTianShuLocalMemoryStore : ITianShuLocalMemoryStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions JsonLineSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string memoryRootDirectory;
    private readonly string spacesPath;
    private readonly string factsDirectory;
    private readonly string evidenceDirectory;
    private readonly string candidatesDirectory;
    private readonly string linksDirectory;
    private readonly string auditDirectory;
    private readonly string providerBindingsPath;
    private readonly string auditLogPath;
    private readonly string legacyFactsPath;
    private readonly string legacyAuditPath;

    /// <summary>
    /// 初始化文件系统本地记忆 store。
    /// Initializes a file-system local memory store.
    /// </summary>
    public FileSystemTianShuLocalMemoryStore(string rootDirectory)
    {
        var normalizedRoot = string.IsNullOrWhiteSpace(rootDirectory)
            ? throw new ArgumentException("本地记忆目录不能为空。", nameof(rootDirectory))
            : rootDirectory;
        memoryRootDirectory = Path.Combine(normalizedRoot, "memory");
        spacesPath = Path.Combine(memoryRootDirectory, "spaces.json");
        factsDirectory = Path.Combine(memoryRootDirectory, "facts");
        evidenceDirectory = Path.Combine(memoryRootDirectory, "evidence");
        candidatesDirectory = Path.Combine(memoryRootDirectory, "candidates");
        linksDirectory = Path.Combine(memoryRootDirectory, "links");
        auditDirectory = Path.Combine(memoryRootDirectory, "audit");
        providerBindingsPath = Path.Combine(memoryRootDirectory, "provider-bindings.json");
        auditLogPath = Path.Combine(auditDirectory, "audit-log.jsonl");
        legacyFactsPath = Path.Combine(normalizedRoot, "facts.json");
        legacyAuditPath = Path.Combine(normalizedRoot, "audit.json");
    }

    public async Task<IReadOnlyList<MemorySpace>> ListSpacesAsync(
        MemorySpaceId? memorySpaceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var spaces = await ReadSpacesUnsafeAsync(cancellationToken).ConfigureAwait(false);
            return spaces
                .Where(space => memorySpaceId is null || string.Equals(space.Id, memorySpaceId.Value.Value, StringComparison.Ordinal))
                .OrderBy(static space => space.Id, StringComparer.Ordinal)
                .Select(static space => space.ToSpace())
                .ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<FactMemoryRecord>> ListFactsAsync(
        MemorySpace memorySpace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(memorySpace);
        cancellationToken.ThrowIfCancellationRequested();

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var facts = await ReadFactsUnsafeAsync(memorySpace.Id, cancellationToken).ConfigureAwait(false);
            return facts
                .OrderBy(static fact => fact.Key, StringComparer.Ordinal)
                .Select(static fact => fact.ToFact())
                .ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<FactMemoryRecord> UpsertFactAsync(
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

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureSpaceUnsafeAsync(memorySpace, cancellationToken).ConfigureAwait(false);
            var facts = await ReadFactsUnsafeAsync(memorySpace.Id, cancellationToken).ConfigureAwait(false);
            facts.RemoveAll(item =>
                string.Equals(item.MemorySpaceId, memorySpace.Id.Value, StringComparison.Ordinal)
                && string.Equals(item.Id, fact.Id.Value, StringComparison.Ordinal));
            facts.Add(LocalFactDto.FromFact(fact));
            await WriteJsonLinesUnsafeAsync(FactsPath(memorySpace.Id), facts, cancellationToken).ConfigureAwait(false);

            var auditRecords = await ReadAuditUnsafeAsync(cancellationToken).ConfigureAwait(false);
            auditRecords.Add(LocalAuditDto.FromAudit(audit));
            await WriteJsonLinesUnsafeAsync(auditLogPath, auditRecords, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }

        return fact;
    }

    public async Task<FactMemoryRecord> UpsertFactAsync(
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

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureSpaceUnsafeAsync(memorySpace, cancellationToken).ConfigureAwait(false);
            var facts = await ReadFactsUnsafeAsync(memorySpace.Id, cancellationToken).ConfigureAwait(false);
            facts.RemoveAll(item =>
                string.Equals(item.MemorySpaceId, memorySpace.Id.Value, StringComparison.Ordinal)
                && string.Equals(item.Id, fact.Id.Value, StringComparison.Ordinal));
            facts.Add(LocalFactDto.FromFact(fact));
            await WriteJsonLinesUnsafeAsync(FactsPath(memorySpace.Id), facts, cancellationToken).ConfigureAwait(false);

            var auditRecords = await ReadAuditUnsafeAsync(cancellationToken).ConfigureAwait(false);
            auditRecords.Add(LocalAuditDto.FromAudit(audit));
            await WriteJsonLinesUnsafeAsync(auditLogPath, auditRecords, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }

        return fact;
    }

    public async Task<FactMemoryRecord?> ChangeFactLifecycleAsync(
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

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureSpaceUnsafeAsync(memorySpace, cancellationToken).ConfigureAwait(false);
            var facts = await ReadFactsUnsafeAsync(memorySpace.Id, cancellationToken).ConfigureAwait(false);
            var index = facts.FindIndex(item =>
                string.Equals(item.MemorySpaceId, memorySpace.Id.Value, StringComparison.Ordinal)
                && (memoryRecordId is not { } id || string.Equals(item.Id, id.Value, StringComparison.Ordinal))
                && (normalizedKey is null || string.Equals(item.Key, normalizedKey, StringComparison.Ordinal)));

            if (index < 0)
            {
                return null;
            }

            var updated = facts[index].ToFact().WithLifecycle(lifecycleStatus, occurredAt);
            facts[index] = LocalFactDto.FromFact(updated);
            await WriteJsonLinesUnsafeAsync(FactsPath(memorySpace.Id), facts, cancellationToken).ConfigureAwait(false);

            var auditRecords = await ReadAuditUnsafeAsync(cancellationToken).ConfigureAwait(false);
            var effect = lifecycleStatus == MemoryLifecycleStatus.Deleted
                ? MemoryMutationEffect.SoftDeleted
                : MemoryMutationEffect.LifecycleChanged;
            auditRecords.Add(LocalAuditDto.FromAudit(MemoryAuditRecords.Create(
                ResolveLifecycleOperation(lifecycleStatus),
                memorySpace.Id,
                updated.Key,
                normalizedActor,
                normalizedSource,
                occurredAt,
                effect: effect,
                reason: reason)));
            await WriteJsonLinesUnsafeAsync(auditLogPath, auditRecords, cancellationToken).ConfigureAwait(false);
            return updated;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<TianShuMemoryAuditRecord>> ListAuditRecordsAsync(
        MemorySpaceId? memorySpaceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var auditRecords = await ReadAuditUnsafeAsync(cancellationToken).ConfigureAwait(false);
            return auditRecords
                .Where(record => memorySpaceId is null || string.Equals(record.MemorySpaceId, memorySpaceId.Value.Value, StringComparison.Ordinal))
                .OrderBy(static record => record.OccurredAt)
                .Select(static record => new TianShuMemoryAuditRecord(
                    record.Operation,
                    new MemorySpaceId(record.MemorySpaceId),
                    record.Key,
                    record.Actor,
                    record.Source,
                    record.OccurredAt,
                    valueKind: record.ValueKind,
                    valueHash: record.ValueHash,
                    confidence: record.Confidence,
                    effect: record.Effect,
                    reasonCodes: record.ReasonCodes,
                    reason: record.Reason,
                    snippet: record.Snippet,
                    metadata: record.Metadata))
                .ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task AppendAuditRecordAsync(
        TianShuMemoryAuditRecord auditRecord,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditRecord);
        cancellationToken.ThrowIfCancellationRequested();

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureStoreDirectories();
            var auditRecords = await ReadAuditUnsafeAsync(cancellationToken).ConfigureAwait(false);
            auditRecords.Add(LocalAuditDto.FromAudit(auditRecord));
            await WriteJsonLinesUnsafeAsync(auditLogPath, auditRecords, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task AppendEvidenceRecordAsync(
        MemoryEvidenceRecord evidenceRecord,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evidenceRecord);
        cancellationToken.ThrowIfCancellationRequested();

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureStoreDirectories();
            var records = await ReadJsonLinesUnsafeAsync<MemoryEvidenceRecord>(
                EvidencePath(evidenceRecord.MemorySpaceId),
                cancellationToken).ConfigureAwait(false);
            records.Add(evidenceRecord);
            await WriteJsonLinesUnsafeAsync(EvidencePath(evidenceRecord.MemorySpaceId), records, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<MemoryEvidenceRecord>> ListEvidenceRecordsAsync(
        MemorySpaceId? memorySpaceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await ReadScopedJsonLinesUnsafeAsync<MemoryEvidenceRecord>(
                evidenceDirectory,
                memorySpaceId,
                cancellationToken).ConfigureAwait(false);
            return records
                .OrderBy(static record => record.CapturedAt)
                .ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task UpsertCandidateAsync(
        MemoryCandidate candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureStoreDirectories();
            var records = await ReadJsonLinesUnsafeAsync<MemoryCandidate>(
                CandidatesPath(candidate.MemorySpaceId),
                cancellationToken).ConfigureAwait(false);
            records.RemoveAll(item =>
                string.Equals(item.MemorySpaceId.Value, candidate.MemorySpaceId.Value, StringComparison.Ordinal)
                && string.Equals(item.Key, candidate.Key, StringComparison.Ordinal));
            records.Add(candidate);
            await WriteJsonLinesUnsafeAsync(CandidatesPath(candidate.MemorySpaceId), records, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<MemoryCandidate>> ListCandidatesAsync(
        MemorySpaceId? memorySpaceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await ReadScopedJsonLinesUnsafeAsync<MemoryCandidate>(
                candidatesDirectory,
                memorySpaceId,
                cancellationToken).ConfigureAwait(false);
            return records
                .OrderBy(static candidate => candidate.Key, StringComparer.Ordinal)
                .ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task AppendSupersedeLinkAsync(
        MemorySpaceId memorySpaceId,
        MemorySupersedeLink link,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);
        cancellationToken.ThrowIfCancellationRequested();

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureStoreDirectories();
            var records = await ReadJsonLinesUnsafeAsync<LocalSupersedeLinkDto>(
                SupersedeLinksPath(memorySpaceId),
                cancellationToken).ConfigureAwait(false);
            records.Add(LocalSupersedeLinkDto.FromLink(link));
            await WriteJsonLinesUnsafeAsync(SupersedeLinksPath(memorySpaceId), records, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<MemorySupersedeLink>> ListSupersedeLinksAsync(
        MemorySpaceId? memorySpaceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await ReadScopedJsonLinesUnsafeAsync<LocalSupersedeLinkDto>(
                linksDirectory,
                memorySpaceId,
                cancellationToken).ConfigureAwait(false);
            return records
                .Select(static record => record.ToLink())
                .OrderBy(static link => link.Timestamp)
                .ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<MemoryProviderBinding>> ListProviderBindingsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadJsonUnsafeAsync<MemoryProviderBinding>(providerBindingsPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ReplaceProviderBindingsAsync(
        IReadOnlyList<MemoryProviderBinding> bindings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        cancellationToken.ThrowIfCancellationRequested();

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureStoreDirectories();
            await WriteJsonUnsafeAsync(providerBindingsPath, bindings, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task EnsureSpaceUnsafeAsync(MemorySpace memorySpace, CancellationToken cancellationToken)
    {
        EnsureStoreDirectories();
        var spaces = await ReadSpacesUnsafeAsync(cancellationToken).ConfigureAwait(false);
        spaces.RemoveAll(space => string.Equals(space.Id, memorySpace.Id.Value, StringComparison.Ordinal));
        spaces.Add(LocalSpaceDto.FromSpace(memorySpace));
        await WriteJsonUnsafeAsync(spacesPath, spaces.OrderBy(static space => space.Id, StringComparer.Ordinal).ToArray(), cancellationToken).ConfigureAwait(false);
    }

    private Task<List<LocalSpaceDto>> ReadSpacesUnsafeAsync(CancellationToken cancellationToken)
        => ReadJsonUnsafeAsync<LocalSpaceDto>(spacesPath, cancellationToken);

    private async Task<List<LocalFactDto>> ReadFactsUnsafeAsync(MemorySpaceId memorySpaceId, CancellationToken cancellationToken)
    {
        var path = FactsPath(memorySpaceId);
        if (File.Exists(path))
        {
            return await ReadJsonLinesUnsafeAsync<LocalFactDto>(path, cancellationToken).ConfigureAwait(false);
        }

        var legacyFacts = await ReadJsonUnsafeAsync<LocalFactDto>(legacyFactsPath, cancellationToken).ConfigureAwait(false);
        return legacyFacts
            .Where(fact => string.Equals(fact.MemorySpaceId, memorySpaceId.Value, StringComparison.Ordinal))
            .ToList();
    }

    private async Task<List<LocalAuditDto>> ReadAuditUnsafeAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(auditLogPath))
        {
            return await ReadJsonLinesUnsafeAsync<LocalAuditDto>(auditLogPath, cancellationToken).ConfigureAwait(false);
        }

        return await ReadJsonUnsafeAsync<LocalAuditDto>(legacyAuditPath, cancellationToken).ConfigureAwait(false);
    }

    private string FactsPath(MemorySpaceId memorySpaceId)
        => Path.Combine(factsDirectory, $"{Uri.EscapeDataString(memorySpaceId.Value)}.jsonl");

    private string EvidencePath(MemorySpaceId memorySpaceId)
        => Path.Combine(evidenceDirectory, $"{Uri.EscapeDataString(memorySpaceId.Value)}.jsonl");

    private string CandidatesPath(MemorySpaceId memorySpaceId)
        => Path.Combine(candidatesDirectory, $"{Uri.EscapeDataString(memorySpaceId.Value)}.jsonl");

    private string SupersedeLinksPath(MemorySpaceId memorySpaceId)
        => Path.Combine(linksDirectory, $"{Uri.EscapeDataString(memorySpaceId.Value)}.jsonl");

    private void EnsureStoreDirectories()
    {
        Directory.CreateDirectory(memoryRootDirectory);
        Directory.CreateDirectory(factsDirectory);
        Directory.CreateDirectory(evidenceDirectory);
        Directory.CreateDirectory(candidatesDirectory);
        Directory.CreateDirectory(linksDirectory);
        Directory.CreateDirectory(auditDirectory);
    }

    private static async Task<List<T>> ReadJsonUnsafeAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<T>>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false)
               ?? [];
    }

    private static async Task WriteJsonUnsafeAsync<T>(string path, IReadOnlyList<T> values, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, values, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    private static async Task<List<T>> ReadJsonLinesUnsafeAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var results = new List<T>();
        foreach (var line in await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var value = JsonSerializer.Deserialize<T>(line, JsonLineSerializerOptions);
                if (value is not null)
                {
                    results.Add(value);
                }
            }
            catch (JsonException)
            {
                // 损坏的 JSONL 行不应污染其它可恢复记录。
                // A corrupt JSONL line must not block other recoverable records.
            }
        }

        return results;
    }

    private async Task<List<T>> ReadScopedJsonLinesUnsafeAsync<T>(
        string directory,
        MemorySpaceId? memorySpaceId,
        CancellationToken cancellationToken)
    {
        if (memorySpaceId is { } id)
        {
            return await ReadJsonLinesUnsafeAsync<T>(
                Path.Combine(directory, $"{Uri.EscapeDataString(id.Value)}.jsonl"),
                cancellationToken).ConfigureAwait(false);
        }

        if (!Directory.Exists(directory))
        {
            return [];
        }

        var results = new List<T>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.jsonl").Order(StringComparer.Ordinal))
        {
            results.AddRange(await ReadJsonLinesUnsafeAsync<T>(path, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private static async Task WriteJsonLinesUnsafeAsync<T>(string path, IReadOnlyList<T> values, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(tempPath))
        await using (var writer = new StreamWriter(stream))
        {
            foreach (var value in values)
            {
                var line = JsonSerializer.Serialize(value, JsonLineSerializerOptions);
                await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
        }

        File.Move(tempPath, path, overwrite: true);
    }

    private static string ResolveLifecycleOperation(MemoryLifecycleStatus lifecycleStatus)
        => lifecycleStatus switch
        {
            MemoryLifecycleStatus.Forgotten => "forget_fact",
            MemoryLifecycleStatus.Deleted => "delete_fact",
            _ => "change_fact_lifecycle",
        };

    private sealed class LocalFactDto
    {
        public string MemorySpaceId { get; set; } = string.Empty;

        public string Id { get; set; } = string.Empty;

        public string Key { get; set; } = string.Empty;

        public StructuredValue? Value { get; set; }

        public decimal Confidence { get; set; } = 1m;

        public DateTimeOffset RecordedAt { get; set; }

        public MemoryLifecycleStatus LifecycleStatus { get; set; } = MemoryLifecycleStatus.Active;

        public IReadOnlyList<MemorySourceRef>? Sources { get; set; }

        public IReadOnlyList<string>? Tags { get; set; }

        public long UsageCount { get; set; }

        public DateTimeOffset? LastUsedAt { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        public MemoryFormationPath FormationPath { get; set; } = MemoryFormationPath.Unknown;

        public MemoryContextSignature? ContextSignature { get; set; }

        public IReadOnlyList<MemoryValidationEvidence>? ValidationEvidence { get; set; }

        public bool IsCounterexample { get; set; }

        public FactMemoryRecord ToFact()
        {
            var recordedAt = RecordedAt == default ? DateTimeOffset.UtcNow : RecordedAt;
            return new FactMemoryRecord(
                Key,
                Value ?? StructuredValue.Null,
                new MemorySpaceId(MemorySpaceId),
                Confidence,
                recordedAt,
                string.IsNullOrWhiteSpace(Id) ? null : new MemoryRecordId(Id),
                LifecycleStatus,
                Sources,
                Tags,
                UsageCount,
                LastUsedAt,
                CreatedAt == default ? null : CreatedAt,
                UpdatedAt == default ? null : UpdatedAt,
                FormationPath,
                ContextSignature,
                ValidationEvidence,
                IsCounterexample);
        }

        public static LocalFactDto FromFact(FactMemoryRecord fact)
            => new()
            {
                MemorySpaceId = fact.MemorySpaceId.Value,
                Id = fact.Id.Value,
                Key = fact.Key,
                Value = fact.Value,
                Confidence = fact.Confidence,
                RecordedAt = fact.RecordedAt,
                LifecycleStatus = fact.LifecycleStatus,
                Sources = fact.Sources,
                Tags = fact.Tags,
                UsageCount = fact.UsageCount,
                LastUsedAt = fact.LastUsedAt,
                CreatedAt = fact.CreatedAt,
                UpdatedAt = fact.UpdatedAt,
                FormationPath = fact.FormationPath,
                ContextSignature = fact.ContextSignature,
                ValidationEvidence = fact.ValidationEvidence,
                IsCounterexample = fact.IsCounterexample,
            };
    }

    private sealed class LocalSpaceDto
    {
        public string Id { get; set; } = string.Empty;

        public MemoryScopeKind ScopeKind { get; set; }

        public string ScopeKey { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public bool IsReadOnly { get; set; }

        public MemorySpace ToSpace()
            => new(
                new MemorySpaceId(Id),
                ScopeKind,
                ScopeKey,
                DisplayName,
                IsReadOnly);

        public static LocalSpaceDto FromSpace(MemorySpace space)
            => new()
            {
                Id = space.Id.Value,
                ScopeKind = space.ScopeKind,
                ScopeKey = space.ScopeKey,
                DisplayName = space.DisplayName,
                IsReadOnly = space.IsReadOnly,
            };
    }

    private sealed class LocalAuditDto
    {
        public string Operation { get; set; } = string.Empty;

        public string MemorySpaceId { get; set; } = string.Empty;

        public string Key { get; set; } = string.Empty;

        public string Actor { get; set; } = string.Empty;

        public string Source { get; set; } = string.Empty;

        public DateTimeOffset OccurredAt { get; set; }

        public StructuredValueKind? ValueKind { get; set; }

        public string? ValueHash { get; set; }

        public decimal? Confidence { get; set; }

        public MemoryMutationEffect Effect { get; set; }

        public IReadOnlyList<MemoryRiskReasonCode> ReasonCodes { get; set; } = Array.Empty<MemoryRiskReasonCode>();

        public string? Reason { get; set; }

        public string? Snippet { get; set; }

        public IReadOnlyDictionary<string, string> Metadata { get; set; } =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public static LocalAuditDto FromAudit(TianShuMemoryAuditRecord audit)
            => new()
            {
                Operation = audit.Operation,
                MemorySpaceId = audit.MemorySpaceId.Value,
                Key = audit.Key,
                Actor = audit.Actor,
                Source = audit.Source,
                OccurredAt = audit.OccurredAt,
                ValueKind = audit.ValueKind,
                ValueHash = audit.ValueHash,
                Confidence = audit.Confidence,
                Effect = audit.Effect,
                ReasonCodes = audit.ReasonCodes,
                Reason = audit.Reason,
                Snippet = audit.Snippet,
                Metadata = audit.Metadata,
            };
    }

    private sealed class LocalSupersedeLinkDto
    {
        public string OldRecordId { get; set; } = string.Empty;

        public string NewRecordId { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;

        public string ActorId { get; set; } = string.Empty;

        public MemorySourceRef? Source { get; set; }

        public DateTimeOffset Timestamp { get; set; }

        public MemorySupersedeLink ToLink()
            => new(
                new MemoryRecordId(OldRecordId),
                new MemoryRecordId(NewRecordId),
                Reason,
                ActorId,
                Source,
                Timestamp);

        public static LocalSupersedeLinkDto FromLink(MemorySupersedeLink link)
            => new()
            {
                OldRecordId = link.OldRecordId.Value,
                NewRecordId = link.NewRecordId.Value,
                Reason = link.Reason,
                ActorId = link.ActorId,
                Source = link.Source,
                Timestamp = link.Timestamp,
            };
    }
}
