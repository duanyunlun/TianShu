using System.Globalization;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Primitives;

namespace TianShu.IdentityMemory;

/// <summary>
/// 记忆长期整理 worker；只生成可审计 proposal，不静默改写 Active fact。
/// </summary>
public sealed class MemoryConsolidationWorker
{
    public const string ProposalOperationName = "memory_consolidation_proposal";
    public const string LeaseOperationName = "memory_consolidation_lease";
    public const string FailureOperationName = "memory_consolidation_failure";
    public const string MaintenanceOperationName = "memory_consolidation_maintenance";

    private readonly ITianShuLocalMemoryStore store;

    public MemoryConsolidationWorker(ITianShuLocalMemoryStore store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<MemoryConsolidationResult> RunOnceAsync(
        MemorySpaceId? memorySpaceId,
        MemoryOperationContext context,
        CancellationToken cancellationToken,
        MemoryConsolidationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        options ??= MemoryConsolidationOptions.Default;

        var auditRecords = await store.ListAuditRecordsAsync(memorySpaceId, cancellationToken).ConfigureAwait(false);
        var leaseKey = BuildLeaseKey(memorySpaceId);
        if (options.EnableLease
            && HasActiveLease(auditRecords, leaseKey, context, options.LeaseDuration))
        {
            return new MemoryConsolidationResult(
                CandidatesScanned: 0,
                ProposalsCreated: 0,
                LeaseAcquired: false,
                SkippedByLease: true);
        }

        if (options.EnableLease)
        {
            await AppendLeaseAsync(memorySpaceId, context, leaseKey, options, cancellationToken).ConfigureAwait(false);
            auditRecords = await store.ListAuditRecordsAsync(memorySpaceId, cancellationToken).ConfigureAwait(false);
        }

        var candidates = await store.ListCandidatesAsync(memorySpaceId, cancellationToken).ConfigureAwait(false);
        var knownProposalKeys = auditRecords
            .Where(static record => string.Equals(record.Operation, ProposalOperationName, StringComparison.Ordinal))
            .Select(static record => record.Metadata.TryGetValue("idempotencyKey", out var key) ? key : string.Empty)
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.Ordinal);

        var proposalsCreated = 0;
        var candidatesSkippedByCooldown = 0;
        var retriesDeferred = 0;
        var failuresRecorded = 0;
        var uniqueCandidates = candidates
            .GroupBy(static candidate => BuildIdempotencyKey(candidate), StringComparer.Ordinal)
            .Select(static group => group
                .OrderByDescending(static candidate => candidate.Confidence)
                .ThenByDescending(static candidate => candidate.Source?.CapturedAt ?? DateTimeOffset.MinValue)
                .First())
            .ToArray();

        foreach (var candidate in uniqueCandidates)
        {
            var idempotencyKey = BuildIdempotencyKey(candidate);
            var cooldownKey = BuildCooldownKey(candidate.MemorySpaceId, candidate.Key);
            if (knownProposalKeys.Contains(idempotencyKey))
            {
                continue;
            }

            if (IsCoolingDown(auditRecords, cooldownKey, context.Timestamp, options.CooldownWindow))
            {
                candidatesSkippedByCooldown++;
                continue;
            }

            if (!CanRetry(auditRecords, idempotencyKey, context.Timestamp, options, out var retryDeferred))
            {
                retriesDeferred += retryDeferred ? 1 : 0;
                continue;
            }

            try
            {
                var proposalKind = await ResolveProposalKindAsync(candidate, cancellationToken).ConfigureAwait(false);
                await AppendProposalAsync(
                    candidate.MemorySpaceId,
                    candidate.Key,
                    context,
                    idempotencyKey,
                    proposalKind,
                    candidate.Source?.SourceId ?? "memory-consolidation",
                    candidate.Value,
                    candidate.Confidence,
                    proposalKind == "supersede_proposal" ? [MemoryRiskReasonCode.ConflictsWithActiveFact] : null,
                    "consolidation worker proposed a review action",
                    candidate.Source?.Snippet,
                    leaseKey,
                    cooldownKey,
                    options,
                    cancellationToken).ConfigureAwait(false);
                knownProposalKeys.Add(idempotencyKey);
                proposalsCreated++;
            }
            catch (Exception ex) when (options.RecordFailureDiagnostics)
            {
                failuresRecorded++;
                await AppendFailureAsync(
                    candidate.MemorySpaceId,
                    candidate.Key,
                    context,
                    idempotencyKey,
                    leaseKey,
                    cooldownKey,
                    ex,
                    CountFailures(auditRecords, idempotencyKey) + 1,
                    options,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        if (options.IncludeArchiveProposals || options.IncludeForgetProposals || options.IncludeOverlayCacheRebuildProposals)
        {
            foreach (var space in await store.ListSpacesAsync(memorySpaceId, cancellationToken).ConfigureAwait(false))
            {
                var facts = await store.ListFactsAsync(space, cancellationToken).ConfigureAwait(false);
                if (options.IncludeArchiveProposals || options.IncludeForgetProposals)
                {
                    var proposalKind = options.IncludeForgetProposals ? "forget_proposal" : "archive_proposal";
                    var proposalReason = options.IncludeForgetProposals
                        ? "consolidation worker proposed forgetting a stale low-usage fact"
                        : "consolidation worker proposed archiving a stale low-usage fact";
                    foreach (var fact in facts.Where(fact => ShouldProposeRetention(fact, context.Timestamp, options)))
                    {
                        var idempotencyKey = $"{proposalKind}\u001f{fact.Id.Value}\u001f{fact.UpdatedAt:O}";
                        if (knownProposalKeys.Contains(idempotencyKey))
                        {
                            continue;
                        }

                        await AppendProposalAsync(
                            space.Id,
                            fact.Key,
                            context,
                            idempotencyKey,
                            proposalKind,
                            "memory-consolidation",
                            fact.Value,
                            fact.Confidence,
                            null,
                            proposalReason,
                            null,
                            leaseKey,
                            BuildCooldownKey(space.Id, fact.Key),
                            options,
                            cancellationToken,
                            new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["targetRecordId"] = fact.Id.Value,
                                ["targetLifecycleStatus"] = options.IncludeForgetProposals
                                    ? MemoryLifecycleStatus.Forgotten.ToString()
                                    : MemoryLifecycleStatus.Archived.ToString(),
                            }).ConfigureAwait(false);
                        knownProposalKeys.Add(idempotencyKey);
                        proposalsCreated++;
                    }
                }

                if (options.IncludeOverlayCacheRebuildProposals
                    && facts.Any(static fact => fact.LifecycleStatus == MemoryLifecycleStatus.Active))
                {
                    var idempotencyKey = $"overlay-cache-rebuild\u001f{space.Id.Value}\u001f{context.Timestamp:yyyyMMddHH}";
                    if (!knownProposalKeys.Contains(idempotencyKey))
                    {
                        await AppendProposalAsync(
                            space.Id,
                            "overlay-cache",
                            context,
                            idempotencyKey,
                            "overlay_cache_rebuild_proposal",
                            "memory-consolidation",
                            null,
                            null,
                            null,
                            "consolidation worker proposed rebuilding the derived overlay cache",
                            null,
                            leaseKey,
                            BuildCooldownKey(space.Id, "overlay-cache"),
                            options,
                            cancellationToken).ConfigureAwait(false);
                        knownProposalKeys.Add(idempotencyKey);
                        proposalsCreated++;
                    }

                    if (options.EmitOverlayCacheSnapshot)
                    {
                        await AppendMaintenanceSnapshotAsync(space, facts, context, leaseKey, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        return new MemoryConsolidationResult(
            uniqueCandidates.Length,
            proposalsCreated,
            LeaseAcquired: options.EnableLease,
            SkippedByLease: false,
            CandidatesSkippedByCooldown: candidatesSkippedByCooldown,
            RetriesDeferred: retriesDeferred,
            FailuresRecorded: failuresRecorded);
    }

    private async Task AppendProposalAsync(
        MemorySpaceId memorySpaceId,
        string key,
        MemoryOperationContext context,
        string idempotencyKey,
        string proposalKind,
        string source,
        StructuredValue? value,
        decimal? confidence,
        IReadOnlyList<MemoryRiskReasonCode>? reasonCodes,
        string reason,
        string? snippet,
        string leaseKey,
        string cooldownKey,
        MemoryConsolidationOptions options,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? additionalMetadata = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["idempotencyKey"] = idempotencyKey,
            ["proposalKind"] = proposalKind,
            ["cooldownKey"] = cooldownKey,
            ["cooldownExpiresAt"] = context.Timestamp.Add(options.CooldownWindow).ToString("O"),
            ["leaseKey"] = leaseKey,
            ["permissionBoundary"] = "audit-only",
        };
        if (additionalMetadata is not null)
        {
            foreach (var (metadataKey, metadataValue) in additionalMetadata)
            {
                metadata[metadataKey] = metadataValue;
            }
        }

        await store.AppendAuditRecordAsync(
            MemoryAuditRecords.Create(
                ProposalOperationName,
                memorySpaceId,
                key,
                context.ActorId,
                source,
                context.Timestamp,
                value,
                confidence,
                effect: MemoryMutationEffect.None,
                reasonCodes: reasonCodes,
                reason: reason,
                snippet: snippet,
                metadata: metadata),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task AppendLeaseAsync(
        MemorySpaceId? memorySpaceId,
        MemoryOperationContext context,
        string leaseKey,
        MemoryConsolidationOptions options,
        CancellationToken cancellationToken)
        => await store.AppendAuditRecordAsync(
            MemoryAuditRecords.Create(
                LeaseOperationName,
                memorySpaceId ?? new MemorySpaceId("memory:all"),
                "memory-consolidation",
                context.ActorId,
                "memory-consolidation",
                context.Timestamp,
                effect: MemoryMutationEffect.None,
                reason: "consolidation worker acquired an audit-backed lease",
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["leaseKey"] = leaseKey,
                    ["leaseOwner"] = ResolveLeaseOwner(context),
                    ["leaseExpiresAt"] = context.Timestamp.Add(options.LeaseDuration).ToString("O"),
                    ["permissionBoundary"] = "audit-only",
                }),
            cancellationToken).ConfigureAwait(false);

    private async Task AppendFailureAsync(
        MemorySpaceId memorySpaceId,
        string key,
        MemoryOperationContext context,
        string idempotencyKey,
        string leaseKey,
        string cooldownKey,
        Exception exception,
        int attempt,
        MemoryConsolidationOptions options,
        CancellationToken cancellationToken)
        => await store.AppendAuditRecordAsync(
            MemoryAuditRecords.Create(
                FailureOperationName,
                memorySpaceId,
                key,
                context.ActorId,
                "memory-consolidation",
                context.Timestamp,
                effect: MemoryMutationEffect.None,
                reason: "consolidation worker failed to emit a proposal",
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["idempotencyKey"] = idempotencyKey,
                    ["leaseKey"] = leaseKey,
                    ["cooldownKey"] = cooldownKey,
                    ["attempt"] = attempt.ToString(CultureInfo.InvariantCulture),
                    ["nextRetryAt"] = context.Timestamp.Add(options.RetryDelay).ToString("O"),
                    ["errorType"] = exception.GetType().Name,
                    ["errorMessage"] = TrimDiagnostic(exception.Message, 240),
                    ["permissionBoundary"] = "audit-only",
                }),
            cancellationToken).ConfigureAwait(false);

    private async Task AppendMaintenanceSnapshotAsync(
        MemorySpace memorySpace,
        IReadOnlyList<FactMemoryRecord> facts,
        MemoryOperationContext context,
        string leaseKey,
        CancellationToken cancellationToken)
    {
        var activeFacts = facts.Where(static fact => fact.LifecycleStatus == MemoryLifecycleStatus.Active).ToArray();
        var hotFacts = activeFacts.Count(static fact => fact.UsageCount > 0);
        var coldFacts = activeFacts.Length - hotFacts;
        await store.AppendAuditRecordAsync(
            MemoryAuditRecords.Create(
                MaintenanceOperationName,
                memorySpace.Id,
                "overlay-cache",
                context.ActorId,
                "memory-consolidation",
                context.Timestamp,
                effect: MemoryMutationEffect.None,
                reason: "consolidation worker refreshed the derived overlay-cache maintenance snapshot",
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["maintenanceKind"] = "overlay_cache_snapshot",
                    ["leaseKey"] = leaseKey,
                    ["activeFactCount"] = activeFacts.Length.ToString(CultureInfo.InvariantCulture),
                    ["counterexampleCount"] = activeFacts.Count(static fact => fact.IsCounterexample).ToString(CultureInfo.InvariantCulture),
                    ["hotFactCount"] = hotFacts.ToString(CultureInfo.InvariantCulture),
                    ["coldFactCount"] = coldFacts.ToString(CultureInfo.InvariantCulture),
                    ["permissionBoundary"] = "audit-only",
                }),
            cancellationToken).ConfigureAwait(false);
    }

    private static bool ShouldProposeRetention(
        FactMemoryRecord fact,
        DateTimeOffset now,
        MemoryConsolidationOptions options)
    {
        if (fact.LifecycleStatus != MemoryLifecycleStatus.Active
            || fact.UsageCount > options.ArchiveUnusedFactsWithUsageCountAtMost)
        {
            return false;
        }

        var activityAt = fact.LastUsedAt ?? fact.UpdatedAt;
        return now - activityAt >= options.ArchiveUnusedFactsOlderThan;
    }

    private async Task<string> ResolveProposalKindAsync(
        MemoryCandidate candidate,
        CancellationToken cancellationToken)
    {
        var spaces = await store.ListSpacesAsync(candidate.MemorySpaceId, cancellationToken).ConfigureAwait(false);
        foreach (var space in spaces)
        {
            var facts = await store.ListFactsAsync(space, cancellationToken).ConfigureAwait(false);
            if (facts.Any(fact =>
                    fact.LifecycleStatus == MemoryLifecycleStatus.Active
                    && string.Equals(fact.Key, candidate.Key, StringComparison.Ordinal)
                    && !string.Equals(fact.Value.GetString(), candidate.Value.GetString(), StringComparison.Ordinal)))
            {
                return "supersede_proposal";
            }
        }

        return "review_candidate";
    }

    private static string BuildIdempotencyKey(MemoryCandidate candidate)
        => $"{candidate.MemorySpaceId.Value}\u001f{candidate.Key}\u001f{candidate.Value.GetString()}";

    private static string BuildLeaseKey(MemorySpaceId? memorySpaceId)
        => $"memory-consolidation:{memorySpaceId?.Value ?? "all"}";

    private static string BuildCooldownKey(MemorySpaceId memorySpaceId, string key)
        => $"{memorySpaceId.Value}:{key}";

    private static bool HasActiveLease(
        IReadOnlyList<TianShuMemoryAuditRecord> auditRecords,
        string leaseKey,
        MemoryOperationContext context,
        TimeSpan leaseDuration)
    {
        var latest = auditRecords
            .Where(record => string.Equals(record.Operation, LeaseOperationName, StringComparison.Ordinal)
                             && record.Metadata.TryGetValue("leaseKey", out var key)
                             && string.Equals(key, leaseKey, StringComparison.Ordinal))
            .OrderByDescending(static record => record.OccurredAt)
            .FirstOrDefault();
        if (latest is null)
        {
            return false;
        }

        if (latest.Metadata.TryGetValue("leaseExpiresAt", out var configuredExpiresAt)
            && DateTimeOffset.TryParse(configuredExpiresAt, out var expiresAt))
        {
            return expiresAt > context.Timestamp;
        }

        return latest.OccurredAt.Add(leaseDuration) > context.Timestamp;
    }

    private static bool IsCoolingDown(
        IReadOnlyList<TianShuMemoryAuditRecord> auditRecords,
        string cooldownKey,
        DateTimeOffset now,
        TimeSpan cooldownWindow)
        => auditRecords.Any(record =>
            string.Equals(record.Operation, ProposalOperationName, StringComparison.Ordinal)
            && record.Metadata.TryGetValue("cooldownKey", out var key)
            && string.Equals(key, cooldownKey, StringComparison.Ordinal)
            && record.OccurredAt.Add(cooldownWindow) > now);

    private static bool CanRetry(
        IReadOnlyList<TianShuMemoryAuditRecord> auditRecords,
        string idempotencyKey,
        DateTimeOffset now,
        MemoryConsolidationOptions options,
        out bool deferred)
    {
        deferred = false;
        var failures = auditRecords
            .Where(record => string.Equals(record.Operation, FailureOperationName, StringComparison.Ordinal)
                             && record.Metadata.TryGetValue("idempotencyKey", out var key)
                             && string.Equals(key, idempotencyKey, StringComparison.Ordinal))
            .OrderByDescending(static record => record.OccurredAt)
            .ToArray();
        if (failures.Length == 0)
        {
            return true;
        }

        if (failures.Length >= options.MaxRetryAttempts)
        {
            deferred = true;
            return false;
        }

        var latest = failures[0];
        if (latest.Metadata.TryGetValue("nextRetryAt", out var configuredNextRetryAt)
            && DateTimeOffset.TryParse(configuredNextRetryAt, out var nextRetryAt)
            && nextRetryAt > now)
        {
            deferred = true;
            return false;
        }

        return true;
    }

    private static int CountFailures(
        IReadOnlyList<TianShuMemoryAuditRecord> auditRecords,
        string idempotencyKey)
        => auditRecords.Count(record =>
            string.Equals(record.Operation, FailureOperationName, StringComparison.Ordinal)
            && record.Metadata.TryGetValue("idempotencyKey", out var key)
            && string.Equals(key, idempotencyKey, StringComparison.Ordinal));

    private static string ResolveLeaseOwner(MemoryOperationContext context)
        => !string.IsNullOrWhiteSpace(context.CorrelationId)
            ? context.CorrelationId!
            : !string.IsNullOrWhiteSpace(context.SessionId?.Value)
                ? context.SessionId.Value
                : context.ActorId;

    private static string TrimDiagnostic(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<empty>";
        }

        var normalized = value.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}

/// <summary>
/// 单轮 consolidation 结果。
/// </summary>
public sealed record MemoryConsolidationResult(
    int CandidatesScanned,
    int ProposalsCreated,
    bool LeaseAcquired = true,
    bool SkippedByLease = false,
    int CandidatesSkippedByCooldown = 0,
    int RetriesDeferred = 0,
    int FailuresRecorded = 0);

/// <summary>
/// 记忆整理 worker 的单轮运行选项。
/// </summary>
public sealed record MemoryConsolidationOptions
{
    public static MemoryConsolidationOptions Default { get; } = new();

    public MemoryConsolidationOptions(
        bool enableLease = true,
        bool includeArchiveProposals = false,
        bool includeForgetProposals = false,
        bool includeOverlayCacheRebuildProposals = false,
        bool emitOverlayCacheSnapshot = true,
        bool recordFailureDiagnostics = true,
        TimeSpan? archiveUnusedFactsOlderThan = null,
        long archiveUnusedFactsWithUsageCountAtMost = 0,
        TimeSpan? leaseDuration = null,
        TimeSpan? cooldownWindow = null,
        int maxRetryAttempts = 3,
        TimeSpan? retryDelay = null,
        TimeSpan? scheduleInterval = null)
    {
        EnableLease = enableLease;
        IncludeArchiveProposals = includeArchiveProposals;
        IncludeForgetProposals = includeForgetProposals;
        IncludeOverlayCacheRebuildProposals = includeOverlayCacheRebuildProposals;
        EmitOverlayCacheSnapshot = emitOverlayCacheSnapshot;
        RecordFailureDiagnostics = recordFailureDiagnostics;
        ArchiveUnusedFactsOlderThan = archiveUnusedFactsOlderThan ?? TimeSpan.FromDays(90);
        ArchiveUnusedFactsWithUsageCountAtMost = archiveUnusedFactsWithUsageCountAtMost;
        LeaseDuration = leaseDuration ?? TimeSpan.FromMinutes(15);
        CooldownWindow = cooldownWindow ?? TimeSpan.FromHours(6);
        MaxRetryAttempts = maxRetryAttempts < 1
            ? throw new ArgumentOutOfRangeException(nameof(maxRetryAttempts), "重试次数必须大于 0。")
            : maxRetryAttempts;
        RetryDelay = retryDelay ?? TimeSpan.FromMinutes(5);
        ScheduleInterval = scheduleInterval ?? TimeSpan.FromMinutes(30);
    }

    public bool EnableLease { get; }

    public bool IncludeArchiveProposals { get; }

    public bool IncludeForgetProposals { get; }

    public bool IncludeOverlayCacheRebuildProposals { get; }

    public bool EmitOverlayCacheSnapshot { get; }

    public bool RecordFailureDiagnostics { get; }

    public TimeSpan ArchiveUnusedFactsOlderThan { get; }

    public long ArchiveUnusedFactsWithUsageCountAtMost { get; }

    public TimeSpan LeaseDuration { get; }

    public TimeSpan CooldownWindow { get; }

    public int MaxRetryAttempts { get; }

    public TimeSpan RetryDelay { get; }

    public TimeSpan ScheduleInterval { get; }
}

/// <summary>
/// 记忆整理 worker 的最小后台调度器；调度本身只驱动单轮运行，权限仍由 worker 审计边界约束。
/// </summary>
public sealed class MemoryConsolidationWorkerRunner
{
    private readonly MemoryConsolidationWorker worker;

    public MemoryConsolidationWorkerRunner(MemoryConsolidationWorker worker)
    {
        this.worker = worker ?? throw new ArgumentNullException(nameof(worker));
    }

    public async Task RunUntilCancelledAsync(
        MemorySpaceId? memorySpaceId,
        Func<MemoryOperationContext> contextFactory,
        MemoryConsolidationOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        options ??= MemoryConsolidationOptions.Default;

        while (!cancellationToken.IsCancellationRequested)
        {
            await worker.RunOnceAsync(memorySpaceId, contextFactory(), cancellationToken, options).ConfigureAwait(false);
            await Task.Delay(options.ScheduleInterval, cancellationToken).ConfigureAwait(false);
        }
    }
}
