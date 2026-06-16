using System.Security.Cryptography;
using System.Text;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Primitives;

namespace TianShu.IdentityMemory;

/// <summary>
/// TianShu 内置本地结构化记忆 provider。
/// Built-in local structured memory provider for TianShu.
/// </summary>
public sealed class TianShuLocalMemoryProvider : IMemoryProvider, IMemoryProviderTargetSpaceResolver, IMemoryProviderStateStore
{
    public const string DefaultProviderId = "tianshu.local";

    private readonly ITianShuLocalMemoryStore store;
    private readonly MemoryPolicyEngine policy;
    private readonly IReadOnlyList<MemorySpace> spaces;

    public TianShuLocalMemoryProvider(
        ITianShuLocalMemoryStore store,
        IEnumerable<MemorySpace> spaces,
        MemoryPolicyEngine? policy = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.policy = policy ?? new MemoryPolicyEngine();
        this.spaces = (spaces ?? throw new ArgumentNullException(nameof(spaces))).ToArray();
    }

    public MemoryProviderDescriptor Descriptor { get; } = new(
        DefaultProviderId,
        "TianShu Local Memory",
        "1.0",
        MemoryProviderCapability.ListSpaces
        | MemoryProviderCapability.Add
        | MemoryProviderCapability.Extract
        | MemoryProviderCapability.Filter
        | MemoryProviderCapability.Forget
        | MemoryProviderCapability.Delete
        | MemoryProviderCapability.Feedback
        | MemoryProviderCapability.Citation
        | MemoryProviderCapability.Export
        | MemoryProviderCapability.Supersede
        | MemoryProviderCapability.Review
        | MemoryProviderCapability.KeywordSearch
        | MemoryProviderCapability.ReadOnlyAccess
        | MemoryProviderCapability.ReadWriteAccess,
        [
            MemoryScopeKind.User,
            MemoryScopeKind.Workspace,
            MemoryScopeKind.Team,
            MemoryScopeKind.Session,
            MemoryScopeKind.Agent,
            MemoryScopeKind.Collaboration
        ],
        RequiresNetwork: false,
        RequiresCredentials: false,
        TrustLevel: MemoryProviderTrustLevel.BuiltIn,
        SupportedLifecycleStatuses:
        [
            MemoryLifecycleStatus.Active,
            MemoryLifecycleStatus.PendingReview,
            MemoryLifecycleStatus.Archived,
            MemoryLifecycleStatus.Forgotten,
            MemoryLifecycleStatus.Deleted
        ],
        DegradationStrategy: MemoryProviderDegradationStrategy.UnsupportedResult,
        Features: MemoryProviderFeature.SourceTracking
            | MemoryProviderFeature.CitationWriteBack
            | MemoryProviderFeature.UsageTelemetry
            | MemoryProviderFeature.SecretRedaction);

    public Task<IReadOnlyList<MemorySpace>> ListSpacesAsync(
        MemorySpaceId? memorySpaceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ListSpacesCoreAsync(memorySpaceId, includeVirtualSpace: true, cancellationToken);
    }

    public async Task<MemorySpace?> ResolveStateSpaceAsync(
        MemorySpaceId memorySpaceId,
        CancellationToken cancellationToken)
        => await ResolveSpaceAsync(memorySpaceId, cancellationToken).ConfigureAwait(false);

    public Task<IReadOnlyList<FactMemoryRecord>> ListStateFactsAsync(
        MemorySpace memorySpace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(memorySpace);
        return store.ListFactsAsync(memorySpace, cancellationToken);
    }

    public Task AppendEvidenceRecordAsync(
        MemoryEvidenceRecord evidenceRecord,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evidenceRecord);
        return store.AppendEvidenceRecordAsync(evidenceRecord, cancellationToken);
    }

    public Task UpsertCandidateAsync(
        MemoryCandidate candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return store.UpsertCandidateAsync(candidate, cancellationToken);
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
        return store.UpsertFactAsync(memorySpace, fact, actor, source, occurredAt, cancellationToken);
    }

    public Task AppendSupersedeLinkAsync(
        MemorySpaceId memorySpaceId,
        MemorySupersedeLink link,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);
        return store.AppendSupersedeLinkAsync(memorySpaceId, link, cancellationToken);
    }

    public async Task<MemoryMutationResult> AddAsync(
        AddMemory command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!policy.Supports(Descriptor, MemoryProviderCapability.Add))
        {
            return Unsupported(MemoryProviderCapability.Add);
        }

        var space = await ResolveSpaceAsync(command.MemorySpaceId, cancellationToken).ConfigureAwait(false);
        if (space is null)
        {
            return new MemoryMutationResult(false, DegradedReason: "memory_space_not_found");
        }

        if (space.IsReadOnly)
        {
            return ReadOnlySpace();
        }

        var source = command.Source ?? new MemorySourceRef(
            MemorySourceKind.System,
            context.ActorId,
            role: "system",
            capturedAt: context.Timestamp);
        var fact = new FactMemoryRecord(
            command.Key,
            command.Value,
            command.MemorySpaceId,
            command.Confidence,
            context.Timestamp,
            sources: [source]);
        var persisted = await store.UpsertFactAsync(
            space,
            fact,
            context.ActorId,
            source.SourceId,
            context.Timestamp,
            cancellationToken).ConfigureAwait(false);

        return new MemoryMutationResult(true, persisted.Id, persisted.LifecycleStatus, Effect: MemoryMutationEffect.Upserted);
    }

    public Task<MemoryMutationResult> ImportAsync(
        ImportMemory command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
        => Task.FromResult(Unsupported(MemoryProviderCapability.Import));

    public async Task<MemoryQueryResult> ExportAsync(
        ExportMemory command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!policy.Supports(Descriptor, MemoryProviderCapability.Export))
        {
            return new MemoryQueryResult(degradedProviders: ["unsupported_capability:export"]);
        }

        var filter = command.Filter is null
            ? new FilterMemory(command.MemorySpaceId)
            : command.Filter with { MemorySpaceId = command.MemorySpaceId };
        return await FilterAsync(filter, context, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MemoryQueryResult> FilterAsync(
        FilterMemory query,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!policy.Supports(Descriptor, MemoryProviderCapability.Filter))
        {
            return new MemoryQueryResult(degradedProviders: [Descriptor.ProviderId]);
        }

        var selectedSpaces = await ListSpacesCoreAsync(query.MemorySpaceId, includeVirtualSpace: query.MemorySpaceId is not null, cancellationToken).ConfigureAwait(false);
        selectedSpaces = selectedSpaces
            .Where(space => query.ScopeKind is null || space.ScopeKind == query.ScopeKind)
            .ToArray();
        var records = new List<FactMemoryRecord>();
        foreach (var space in selectedSpaces)
        {
            var facts = await store.ListFactsAsync(space, cancellationToken).ConfigureAwait(false);
            records.AddRange(facts.Where(fact => policy.CanRead(fact, query)));
        }

        if (query.LifecycleStatus == MemoryLifecycleStatus.PendingReview)
        {
            var activeRecordsBySpaceAndKey = records
                .Where(static record => record.LifecycleStatus == MemoryLifecycleStatus.Active)
                .Select(static record => BuildFactKey(record.MemorySpaceId, record.Key))
                .ToHashSet(StringComparer.Ordinal);

            foreach (var space in selectedSpaces)
            {
                var rejectedCandidateKeys = await ListRejectedCandidateKeysAsync(space.Id, cancellationToken).ConfigureAwait(false);
                var allFacts = await store.ListFactsAsync(space, cancellationToken).ConfigureAwait(false);
                foreach (var activeFact in allFacts.Where(static fact => fact.LifecycleStatus == MemoryLifecycleStatus.Active))
                {
                    activeRecordsBySpaceAndKey.Add(BuildFactKey(activeFact.MemorySpaceId, activeFact.Key));
                }

                var candidates = await store.ListCandidatesAsync(space.Id, cancellationToken).ConfigureAwait(false);
                foreach (var candidate in candidates)
                {
                    if (rejectedCandidateKeys.Contains(BuildFactKey(candidate.MemorySpaceId, candidate.Key)))
                    {
                        continue;
                    }

                    if (activeRecordsBySpaceAndKey.Contains(BuildFactKey(candidate.MemorySpaceId, candidate.Key)))
                    {
                        continue;
                    }

                    var pendingFact = ToPendingReviewFact(candidate, context.Timestamp);
                    if (policy.CanRead(pendingFact, query))
                    {
                        records.Add(pendingFact);
                    }
                }
            }
        }

        var effectiveSearchMode = string.IsNullOrWhiteSpace(query.QueryText)
            ? MemorySearchMode.Structured
            : MemorySearchMode.Keyword;
        return new MemoryQueryResult(
            records.OrderBy(static fact => fact.Key, StringComparer.Ordinal).ToArray(),
            EffectiveSearchMode: effectiveSearchMode);
    }

    public async Task<MemoryReviewQueryResult> ListReviewsAsync(
        ListMemoryReviews query,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!policy.Supports(Descriptor, MemoryProviderCapability.Review))
        {
            return new MemoryReviewQueryResult(degradedProviders: [$"{Descriptor.ProviderId}:unsupported_capability:review"]);
        }

        var filter = new FilterMemory(
            query.MemorySpaceId,
            query.Key,
            LifecycleStatus: query.LifecycleStatus,
            SearchMode: MemorySearchMode.Structured);
        var reviewRecords = await FilterAsync(filter, context, cancellationToken).ConfigureAwait(false);
        var candidates = query.LifecycleStatus is null or MemoryLifecycleStatus.PendingReview
            ? await store.ListCandidatesAsync(query.MemorySpaceId, cancellationToken).ConfigureAwait(false)
            : Array.Empty<MemoryCandidate>();
        var candidateByRecordId = candidates
            .Where(candidate => string.IsNullOrWhiteSpace(query.Key) || string.Equals(candidate.Key, query.Key, StringComparison.Ordinal))
            .ToDictionary(candidate => ToPendingReviewFact(candidate, context.Timestamp).Id.Value, StringComparer.Ordinal);

        var items = new List<MemoryReviewItem>();
        foreach (var record in reviewRecords.Records)
        {
            candidateByRecordId.TryGetValue(record.Id.Value, out var candidate);
            items.Add(await BuildReviewItemAsync(record, candidate, query, cancellationToken).ConfigureAwait(false));
        }

        if (query.IncludeAudit
            && query.LifecycleStatus is null or MemoryLifecycleStatus.PendingReview)
        {
            var audit = await store.ListAuditRecordsAsync(query.MemorySpaceId, cancellationToken).ConfigureAwait(false);
            var proposalItems = audit
                .Where(record => IsPendingReviewProposal(record, audit))
                .Where(record => string.IsNullOrWhiteSpace(query.Key) || string.Equals(record.Key, query.Key, StringComparison.Ordinal))
                .Select(ToProposalReviewItem)
                .ToArray();
            items.AddRange(proposalItems);
        }

        return new MemoryReviewQueryResult(
            items
                .OrderBy(static item => item.Record.RecordedAt)
                .ThenBy(static item => item.Record.Key, StringComparer.Ordinal)
                .ToArray(),
            degradedProviders: reviewRecords.DegradedProviders);
    }

    public async Task<MemoryMutationResult> ForgetAsync(
        ForgetMemory command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await ChangeLifecycleAsync(
            command.MemorySpaceId,
            command.MemoryRecordId,
            command.Key,
            MemoryLifecycleStatus.Forgotten,
            MemoryProviderCapability.Forget,
            reason: null,
            context,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<MemoryMutationResult> DeleteAsync(
        DeleteMemory command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await ChangeLifecycleAsync(
            command.MemorySpaceId,
            command.MemoryRecordId,
            command.Key,
            MemoryLifecycleStatus.Deleted,
            MemoryProviderCapability.Delete,
            command.Reason,
            context,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<MemoryMutationResult> SupersedeAsync(
        SupersedeMemory command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!policy.Supports(Descriptor, MemoryProviderCapability.Supersede))
        {
            return Unsupported(MemoryProviderCapability.Supersede);
        }

        var space = await ResolveSpaceAsync(command.MemorySpaceId, cancellationToken).ConfigureAwait(false);
        if (space is null)
        {
            return new MemoryMutationResult(false, DegradedReason: "memory_space_not_found", Effect: MemoryMutationEffect.Degraded);
        }

        if (space.IsReadOnly)
        {
            return ReadOnlySpace();
        }

        var oldFactMatch = await FindFactAsync(command.OldRecordId, command.MemorySpaceId, null, cancellationToken).ConfigureAwait(false);
        if (oldFactMatch is null)
        {
            return new MemoryMutationResult(false, DegradedReason: "memory_record_not_found", Effect: MemoryMutationEffect.Degraded);
        }

        var oldFact = oldFactMatch.Value.Fact;

        var archived = await store.ChangeFactLifecycleAsync(
            space,
            command.OldRecordId,
            oldFact.Key,
            MemoryLifecycleStatus.Archived,
            context.ActorId,
            "supersede",
            context.Timestamp,
            command.Reason,
            cancellationToken).ConfigureAwait(false);
        if (archived is null)
        {
            return new MemoryMutationResult(false, DegradedReason: "memory_record_not_found", Effect: MemoryMutationEffect.Degraded);
        }

        var source = command.Source ?? new MemorySourceRef(
            MemorySourceKind.System,
            context.ActorId,
            role: "system",
            capturedAt: context.Timestamp);
        var newFact = await store.UpsertFactAsync(
            space,
            new FactMemoryRecord(
                command.NewKey,
                command.NewValue,
                command.MemorySpaceId,
                command.Confidence,
                context.Timestamp,
                new MemoryRecordId($"{command.OldRecordId.Value}:superseded:{Guid.NewGuid():N}"),
                sources: [source]),
            context.ActorId,
            source.SourceId,
            context.Timestamp,
            cancellationToken).ConfigureAwait(false);
        var link = new MemorySupersedeLink(
            command.OldRecordId,
            newFact.Id,
            command.Reason,
            context.ActorId,
            source,
            context.Timestamp);
        await store.AppendSupersedeLinkAsync(command.MemorySpaceId, link, cancellationToken).ConfigureAwait(false);
        return new MemoryMutationResult(
            true,
            newFact.Id,
            newFact.LifecycleStatus,
            Effect: MemoryMutationEffect.Superseded,
            SupersedeLink: link);
    }

    public async Task<MemoryMutationResult> ApproveReviewAsync(
        ApproveMemoryReview command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!policy.Supports(Descriptor, MemoryProviderCapability.Review))
        {
            return Unsupported(MemoryProviderCapability.Review);
        }

        var factMatch = await FindReviewFactAsync(
            command.MemoryRecordId,
            command.MemorySpaceId,
            command.Key,
            cancellationToken).ConfigureAwait(false);
        if (factMatch is not null)
        {
            if (factMatch.Value.Fact.LifecycleStatus == MemoryLifecycleStatus.Active)
            {
                return new MemoryMutationResult(true, factMatch.Value.Fact.Id, factMatch.Value.Fact.LifecycleStatus, Effect: MemoryMutationEffect.None);
            }

            if (factMatch.Value.Space.IsReadOnly)
            {
                return ReadOnlySpace();
            }

            var approvedFact = await store.ChangeFactLifecycleAsync(
                factMatch.Value.Space,
                factMatch.Value.Fact.Id,
                factMatch.Value.Fact.Key,
                MemoryLifecycleStatus.Active,
                context.ActorId,
                command.Source?.SourceId ?? "memory-review-approve",
                context.Timestamp,
                command.Reason,
                cancellationToken).ConfigureAwait(false);
            return approvedFact is null
                ? new MemoryMutationResult(false, DegradedReason: "memory_record_not_found", Effect: MemoryMutationEffect.Degraded)
                : new MemoryMutationResult(true, approvedFact.Id, approvedFact.LifecycleStatus, Effect: MemoryMutationEffect.LifecycleChanged);
        }

        var candidateMatch = await FindReviewCandidateAsync(
            command.MemoryRecordId,
            command.MemorySpaceId,
            command.Key,
            cancellationToken).ConfigureAwait(false);
        if (candidateMatch is null)
        {
            return await ApproveProposalReviewAsync(command, context, cancellationToken).ConfigureAwait(false);
        }

        if (candidateMatch.Value.Space.IsReadOnly)
        {
            return ReadOnlySpace();
        }

        var candidate = candidateMatch.Value.Candidate;
        var source = command.Source ?? candidate.Source ?? new MemorySourceRef(
            MemorySourceKind.System,
            context.ActorId,
            role: "memory-review",
            capturedAt: context.Timestamp);
        var persisted = await store.UpsertFactAsync(
            candidateMatch.Value.Space,
            new FactMemoryRecord(
                candidate.Key,
                candidate.Value,
                candidate.MemorySpaceId,
                candidate.Confidence,
                context.Timestamp,
                sources: [source],
                tags: candidate.ContextSignature?.Tags,
                formationPath: candidate.FormationPath,
                contextSignature: candidate.ContextSignature,
                validationEvidence: candidate.ValidationEvidence,
                isCounterexample: candidate.IsCounterexample),
            context.ActorId,
            source.SourceId,
            context.Timestamp,
            cancellationToken).ConfigureAwait(false);

        await store.AppendAuditRecordAsync(
            MemoryAuditRecords.FromContext(
                "approve_memory_review",
                candidate.MemorySpaceId,
                candidate.Key,
                context,
                source,
                value: candidate.Value,
                confidence: candidate.Confidence,
                effect: MemoryMutationEffect.Upserted,
                reason: command.Reason),
            cancellationToken).ConfigureAwait(false);

        return new MemoryMutationResult(true, persisted.Id, persisted.LifecycleStatus, Effect: MemoryMutationEffect.Upserted);
    }

    public async Task<MemoryMutationResult> DemoteReviewAsync(
        DemoteMemoryReview command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!policy.Supports(Descriptor, MemoryProviderCapability.Review))
        {
            return Unsupported(MemoryProviderCapability.Review);
        }

        var factMatch = await FindReviewFactAsync(
            command.MemoryRecordId,
            command.MemorySpaceId,
            command.Key,
            cancellationToken).ConfigureAwait(false);
        if (factMatch is not null)
        {
            if (factMatch.Value.Space.IsReadOnly)
            {
                return ReadOnlySpace();
            }

            var demoted = await store.ChangeFactLifecycleAsync(
                factMatch.Value.Space,
                factMatch.Value.Fact.Id,
                factMatch.Value.Fact.Key,
                MemoryLifecycleStatus.Archived,
                context.ActorId,
                command.Source?.SourceId ?? "memory-review-demote",
                context.Timestamp,
                command.Reason,
                cancellationToken).ConfigureAwait(false);
            return demoted is null
                ? new MemoryMutationResult(false, DegradedReason: "memory_record_not_found", Effect: MemoryMutationEffect.Degraded)
                : new MemoryMutationResult(true, demoted.Id, demoted.LifecycleStatus, Effect: MemoryMutationEffect.LifecycleChanged);
        }

        var proposalResult = await DemoteProposalReviewAsync(command, context, cancellationToken).ConfigureAwait(false);
        if (proposalResult is not null)
        {
            return proposalResult;
        }

        return await WriteCandidateReviewAuditAsync(
            command.MemoryRecordId,
            command.MemorySpaceId,
            command.Key,
            "demote_memory_review",
            MemoryLifecycleStatus.Archived,
            MemoryMutationEffect.LifecycleChanged,
            command.Reason,
            command.Source,
            context,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<MemoryMutationResult> MergeReviewAsync(
        MergeMemoryReview command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!policy.Supports(Descriptor, MemoryProviderCapability.Review | MemoryProviderCapability.Supersede))
        {
            return Unsupported(MemoryProviderCapability.Review | MemoryProviderCapability.Supersede);
        }

        var targetMatch = await FindFactAsync(
            command.TargetRecordId,
            command.MemorySpaceId,
            null,
            cancellationToken).ConfigureAwait(false);
        if (targetMatch is null)
        {
            return new MemoryMutationResult(false, DegradedReason: "memory_merge_target_not_found", Effect: MemoryMutationEffect.Degraded);
        }

        var reviewFact = await FindReviewFactAsync(
            command.ReviewRecordId,
            command.MemorySpaceId,
            null,
            cancellationToken).ConfigureAwait(false);
        var reviewCandidate = reviewFact is null
            ? await FindReviewCandidateAsync(command.ReviewRecordId, command.MemorySpaceId, null, cancellationToken).ConfigureAwait(false)
            : null;
        if (reviewFact is null && reviewCandidate is null)
        {
            return new MemoryMutationResult(false, DegradedReason: "memory_review_target_not_found", Effect: MemoryMutationEffect.Degraded);
        }

        if (targetMatch.Value.Space.IsReadOnly
            || reviewFact?.Space.IsReadOnly == true
            || reviewCandidate?.Space.IsReadOnly == true)
        {
            return ReadOnlySpace();
        }

        var reviewKey = reviewFact?.Fact.Key ?? reviewCandidate!.Value.Candidate.Key;
        var reviewValue = reviewFact?.Fact.Value ?? reviewCandidate!.Value.Candidate.Value;
        var reviewConfidence = reviewFact?.Fact.Confidence ?? reviewCandidate!.Value.Candidate.Confidence;
        var merge = await SupersedeAsync(
            new SupersedeMemory(
                command.TargetRecordId,
                command.MemorySpaceId,
                command.MergedKey ?? targetMatch.Value.Fact.Key,
                command.MergedValue ?? reviewValue,
                command.Reason,
                command.Confidence ?? Math.Max(targetMatch.Value.Fact.Confidence, reviewConfidence),
                command.Source),
            context,
            cancellationToken).ConfigureAwait(false);
        if (!merge.Success)
        {
            return merge;
        }

        if (reviewFact is not null
            && reviewFact.Value.Fact.Id != command.TargetRecordId)
        {
            await store.ChangeFactLifecycleAsync(
                reviewFact.Value.Space,
                reviewFact.Value.Fact.Id,
                reviewFact.Value.Fact.Key,
                MemoryLifecycleStatus.Archived,
                context.ActorId,
                command.Source?.SourceId ?? "memory-review-merge",
                context.Timestamp,
                command.Reason,
                cancellationToken).ConfigureAwait(false);
        }

        await store.AppendAuditRecordAsync(
            MemoryAuditRecords.Create(
                "merge_memory_review",
                command.MemorySpaceId,
                reviewKey,
                context.ActorId,
                command.Source?.SourceId ?? "memory-review-merge",
                context.Timestamp,
                value: command.MergedValue ?? reviewValue,
                confidence: command.Confidence ?? reviewConfidence,
                effect: MemoryMutationEffect.Superseded,
                reason: command.Reason,
                snippet: command.Source?.Snippet,
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["reviewRecordId"] = command.ReviewRecordId.Value,
                    ["targetRecordId"] = command.TargetRecordId.Value,
                    ["newRecordId"] = merge.RecordId?.Value ?? string.Empty,
                }),
            cancellationToken).ConfigureAwait(false);

        return merge;
    }

    public async Task<MemoryMutationResult> RestoreReviewAsync(
        RestoreMemoryReview command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!policy.Supports(Descriptor, MemoryProviderCapability.Review))
        {
            return Unsupported(MemoryProviderCapability.Review);
        }

        var factMatch = await FindAnyFactAsync(
            command.MemoryRecordId,
            command.MemorySpaceId,
            command.Key,
            cancellationToken).ConfigureAwait(false);
        if (factMatch is not null)
        {
            if (factMatch.Value.Space.IsReadOnly)
            {
                return ReadOnlySpace();
            }

            var restored = await store.ChangeFactLifecycleAsync(
                factMatch.Value.Space,
                factMatch.Value.Fact.Id,
                factMatch.Value.Fact.Key,
                MemoryLifecycleStatus.PendingReview,
                context.ActorId,
                command.Source?.SourceId ?? "memory-review-restore",
                context.Timestamp,
                command.Reason,
                cancellationToken).ConfigureAwait(false);
            return restored is null
                ? new MemoryMutationResult(false, DegradedReason: "memory_record_not_found", Effect: MemoryMutationEffect.Degraded)
                : new MemoryMutationResult(true, restored.Id, restored.LifecycleStatus, Effect: MemoryMutationEffect.LifecycleChanged);
        }

        var proposalResult = await RestoreProposalReviewAsync(command, context, cancellationToken).ConfigureAwait(false);
        if (proposalResult is not null)
        {
            return proposalResult;
        }

        return await WriteCandidateReviewAuditAsync(
            command.MemoryRecordId,
            command.MemorySpaceId,
            command.Key,
            "restore_memory_review",
            MemoryLifecycleStatus.PendingReview,
            MemoryMutationEffect.LifecycleChanged,
            command.Reason,
            command.Source,
            context,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<MemoryMutationResult> RecordFeedbackAsync(
        RecordMemoryFeedback command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!policy.Supports(Descriptor, MemoryProviderCapability.Feedback))
        {
            return Unsupported(MemoryProviderCapability.Feedback);
        }

        var match = await FindFactAsync(command.MemoryRecordId, null, null, cancellationToken).ConfigureAwait(false);
        if (match is null)
        {
            var proposalResult = await RecordProposalFeedbackAsync(command, context, cancellationToken).ConfigureAwait(false);
            return proposalResult ?? new MemoryMutationResult(false, DegradedReason: "memory_record_not_found");
        }

        if (match.Value.Space.IsReadOnly)
        {
            return ReadOnlySpace();
        }

        await store.AppendAuditRecordAsync(
            MemoryAuditRecords.FromContext(
                "record_feedback",
                match.Value.Space.Id,
                match.Value.Fact.Key,
                context,
                command.Source,
                sourceFallback: "memory-feedback",
                reason: command.Feedback),
            cancellationToken).ConfigureAwait(false);

        return new MemoryMutationResult(true, match.Value.Fact.Id, match.Value.Fact.LifecycleStatus);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemorySpaceId>> ResolveFeedbackTargetSpacesAsync(
        RecordMemoryFeedback command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var match = await FindFactAsync(command.MemoryRecordId, null, null, cancellationToken).ConfigureAwait(false);
        if (match is not null)
        {
            return new[] { match.Value.Space.Id };
        }

        var proposal = await FindReviewProposalAsync(command.MemoryRecordId, null, null, cancellationToken).ConfigureAwait(false);
        return proposal is null ? Array.Empty<MemorySpaceId>() : new[] { proposal.MemorySpaceId };
    }

    public async Task<MemoryMutationResult> RecordCitationAsync(
        RecordMemoryCitation command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!policy.Supports(Descriptor, MemoryProviderCapability.Citation))
        {
            return Unsupported(MemoryProviderCapability.Citation);
        }

        var updatedRecordId = default(MemoryRecordId?);
        var updatedStatus = default(MemoryLifecycleStatus?);
        var skippedReadOnlySpace = false;
        foreach (var entry in command.Citation.Entries)
        {
            var match = await FindFactAsync(entry.MemoryRecordId, entry.MemorySpaceId, entry.Key, cancellationToken).ConfigureAwait(false);
            if (match is null)
            {
                continue;
            }

            if (match.Value.Space.IsReadOnly)
            {
                skippedReadOnlySpace = true;
                continue;
            }

            var updated = match.Value.Fact.WithUsage(
                match.Value.Fact.UsageCount + 1,
                context.Timestamp,
                context.Timestamp);
            var persisted = await store.UpsertFactAsync(
                match.Value.Space,
                updated,
                context.ActorId,
                "memory-citation",
                context.Timestamp,
                cancellationToken).ConfigureAwait(false);
            await store.AppendAuditRecordAsync(
                MemoryAuditRecords.FromContext(
                    "record_citation",
                    match.Value.Space.Id,
                    persisted.Key,
                    context,
                    entry.Source,
                    sourceFallback: "memory-citation",
                    effect: MemoryMutationEffect.LifecycleChanged,
                    reason: entry.Note),
                cancellationToken).ConfigureAwait(false);

            updatedRecordId ??= persisted.Id;
            updatedStatus ??= persisted.LifecycleStatus;
        }

        return updatedRecordId is null
            ? skippedReadOnlySpace ? ReadOnlySpace() : new MemoryMutationResult(false, DegradedReason: "memory_record_not_found")
            : new MemoryMutationResult(true, updatedRecordId, updatedStatus, Effect: MemoryMutationEffect.LifecycleChanged);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemorySpaceId>> ResolveCitationTargetSpacesAsync(
        RecordMemoryCitation command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var targetSpaces = new Dictionary<string, MemorySpaceId>(StringComparer.Ordinal);
        foreach (var entry in command.Citation.Entries)
        {
            var match = await FindFactAsync(
                entry.MemoryRecordId,
                entry.MemorySpaceId,
                entry.Key,
                cancellationToken).ConfigureAwait(false);
            if (match is not null)
            {
                targetSpaces[match.Value.Space.Id.Value] = match.Value.Space.Id;
            }
        }

        return targetSpaces.Values.ToArray();
    }

    private async Task<MemoryMutationResult> ChangeLifecycleAsync(
        MemorySpaceId? memorySpaceId,
        MemoryRecordId? memoryRecordId,
        string? key,
        MemoryLifecycleStatus lifecycleStatus,
        MemoryProviderCapability capability,
        string? reason,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!policy.Supports(Descriptor, capability))
        {
            return Unsupported(capability);
        }

        if (memorySpaceId is null)
        {
            return new MemoryMutationResult(false, DegradedReason: "memory_space_id_required");
        }

        var space = await ResolveSpaceAsync(memorySpaceId.Value, cancellationToken).ConfigureAwait(false);
        if (space is null)
        {
            return new MemoryMutationResult(false, DegradedReason: "memory_space_not_found");
        }

        if (space.IsReadOnly)
        {
            return ReadOnlySpace();
        }

        var updated = await store.ChangeFactLifecycleAsync(
            space,
            memoryRecordId,
            key,
            lifecycleStatus,
            context.ActorId,
            lifecycleStatus.ToString(),
            context.Timestamp,
            reason,
            cancellationToken).ConfigureAwait(false);
        var effect = lifecycleStatus == MemoryLifecycleStatus.Deleted
            ? MemoryMutationEffect.SoftDeleted
            : MemoryMutationEffect.LifecycleChanged;
        if (updated is null
            && lifecycleStatus is MemoryLifecycleStatus.Forgotten or MemoryLifecycleStatus.Deleted)
        {
            var rejected = await RejectCandidateReviewAsync(
                memoryRecordId,
                memorySpaceId.Value,
                key,
                lifecycleStatus,
                effect,
                reason,
                context,
                cancellationToken).ConfigureAwait(false);
            if (rejected is not null)
            {
                return rejected;
            }

            var rejectedProposal = await RejectProposalReviewAsync(
                memoryRecordId,
                memorySpaceId.Value,
                key,
                lifecycleStatus,
                effect,
                reason,
                context,
                cancellationToken).ConfigureAwait(false);
            if (rejectedProposal is not null)
            {
                return rejectedProposal;
            }
        }

        return updated is null
            ? new MemoryMutationResult(false, DegradedReason: "memory_record_not_found")
            : new MemoryMutationResult(true, updated.Id, updated.LifecycleStatus, Effect: effect);
    }

    private async Task<MemoryMutationResult?> RejectCandidateReviewAsync(
        MemoryRecordId? memoryRecordId,
        MemorySpaceId memorySpaceId,
        string? key,
        MemoryLifecycleStatus lifecycleStatus,
        MemoryMutationEffect effect,
        string? reason,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        var candidateMatch = await FindReviewCandidateAsync(memoryRecordId, memorySpaceId, key, cancellationToken).ConfigureAwait(false);
        if (candidateMatch is null)
        {
            return null;
        }

        if (candidateMatch.Value.Space.IsReadOnly)
        {
            return ReadOnlySpace();
        }

        var candidate = candidateMatch.Value.Candidate;
        var pendingFact = ToPendingReviewFact(candidate, context.Timestamp);
        await store.AppendAuditRecordAsync(
            MemoryAuditRecords.FromContext(
                "reject_memory_review",
                candidate.MemorySpaceId,
                candidate.Key,
                context,
                candidate.Source,
                sourceFallback: "memory-review-reject",
                value: candidate.Value,
                confidence: candidate.Confidence,
                effect: effect,
                reason: reason ?? lifecycleStatus.ToString()),
            cancellationToken).ConfigureAwait(false);

        return new MemoryMutationResult(
            true,
            pendingFact.Id,
            lifecycleStatus,
            Effect: effect);
    }

    private async Task<MemoryMutationResult?> RejectProposalReviewAsync(
        MemoryRecordId? memoryRecordId,
        MemorySpaceId memorySpaceId,
        string? key,
        MemoryLifecycleStatus lifecycleStatus,
        MemoryMutationEffect effect,
        string? reason,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        var proposal = await FindReviewProposalAsync(memoryRecordId, memorySpaceId, key, cancellationToken).ConfigureAwait(false);
        if (proposal is null)
        {
            return null;
        }

        var proposalId = BuildProposalReviewRecordId(proposal);
        await AppendProposalReviewAuditAsync(
            "reject_memory_review",
            proposal,
            proposalId,
            context,
            effect,
            reason ?? lifecycleStatus.ToString(),
            cancellationToken).ConfigureAwait(false);
        return new MemoryMutationResult(true, proposalId, lifecycleStatus, Effect: effect);
    }

    private async Task<MemoryMutationResult> ApproveProposalReviewAsync(
        ApproveMemoryReview command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        var proposal = await FindReviewProposalAsync(
            command.MemoryRecordId,
            command.MemorySpaceId,
            command.Key,
            cancellationToken).ConfigureAwait(false);
        if (proposal is null)
        {
            return new MemoryMutationResult(false, DegradedReason: "memory_review_target_not_found", Effect: MemoryMutationEffect.Degraded);
        }

        var proposalId = BuildProposalReviewRecordId(proposal);
        var proposalKind = TryGetMetadataValue(proposal.Metadata, "proposalKind");
        if (proposalKind is "archive_proposal" or "forget_proposal")
        {
            if (!proposal.Metadata.TryGetValue("targetRecordId", out var targetRecordId)
                || string.IsNullOrWhiteSpace(targetRecordId))
            {
                return new MemoryMutationResult(false, proposalId, DegradedReason: "memory_proposal_target_missing", Effect: MemoryMutationEffect.Degraded);
            }

            var targetStatus = proposalKind == "forget_proposal"
                ? MemoryLifecycleStatus.Forgotten
                : MemoryLifecycleStatus.Archived;
            var targetMatch = await FindFactAsync(
                new MemoryRecordId(targetRecordId),
                proposal.MemorySpaceId,
                proposal.Key,
                cancellationToken).ConfigureAwait(false);
            if (targetMatch is null)
            {
                return new MemoryMutationResult(false, proposalId, DegradedReason: "memory_proposal_target_not_found", Effect: MemoryMutationEffect.Degraded);
            }

            if (targetMatch.Value.Space.IsReadOnly)
            {
                return ReadOnlySpace();
            }

            var updated = await store.ChangeFactLifecycleAsync(
                targetMatch.Value.Space,
                targetMatch.Value.Fact.Id,
                targetMatch.Value.Fact.Key,
                targetStatus,
                context.ActorId,
                command.Source?.SourceId ?? "memory-review-approve-proposal",
                context.Timestamp,
                command.Reason ?? proposal.Reason,
                cancellationToken).ConfigureAwait(false);
            if (updated is null)
            {
                return new MemoryMutationResult(false, proposalId, DegradedReason: "memory_proposal_target_not_found", Effect: MemoryMutationEffect.Degraded);
            }

            await AppendProposalReviewAuditAsync(
                "approve_memory_review",
                proposal,
                proposalId,
                context,
                MemoryMutationEffect.LifecycleChanged,
                command.Reason ?? proposal.Reason,
                cancellationToken,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["targetRecordId"] = updated.Id.Value,
                    ["targetLifecycleStatus"] = updated.LifecycleStatus.ToString(),
                }).ConfigureAwait(false);
            return new MemoryMutationResult(true, updated.Id, updated.LifecycleStatus, Effect: MemoryMutationEffect.LifecycleChanged);
        }

        if (proposalKind == "overlay_cache_rebuild_proposal")
        {
            await AppendProposalReviewAuditAsync(
                "approve_memory_review",
                proposal,
                proposalId,
                context,
                MemoryMutationEffect.None,
                command.Reason ?? proposal.Reason,
                cancellationToken).ConfigureAwait(false);
            return new MemoryMutationResult(true, proposalId, MemoryLifecycleStatus.PendingReview, Effect: MemoryMutationEffect.None);
        }

        return new MemoryMutationResult(false, proposalId, DegradedReason: "unsupported_memory_proposal_kind", Effect: MemoryMutationEffect.Degraded);
    }

    private async Task<MemoryMutationResult?> DemoteProposalReviewAsync(
        DemoteMemoryReview command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        var proposal = await FindReviewProposalAsync(
            command.MemoryRecordId,
            command.MemorySpaceId,
            command.Key,
            cancellationToken).ConfigureAwait(false);
        if (proposal is null)
        {
            return null;
        }

        var proposalId = BuildProposalReviewRecordId(proposal);
        await AppendProposalReviewAuditAsync(
            "demote_memory_review",
            proposal,
            proposalId,
            context,
            MemoryMutationEffect.None,
            command.Reason ?? proposal.Reason,
            cancellationToken).ConfigureAwait(false);
        return new MemoryMutationResult(true, proposalId, MemoryLifecycleStatus.Archived, Effect: MemoryMutationEffect.None);
    }

    private async Task<MemoryMutationResult?> RestoreProposalReviewAsync(
        RestoreMemoryReview command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        var proposal = await FindReviewProposalAsync(
            command.MemoryRecordId,
            command.MemorySpaceId,
            command.Key,
            cancellationToken,
            includeClosed: true).ConfigureAwait(false);
        if (proposal is null)
        {
            return null;
        }

        var proposalId = BuildProposalReviewRecordId(proposal);
        await AppendProposalReviewAuditAsync(
            "restore_memory_review",
            proposal,
            proposalId,
            context,
            MemoryMutationEffect.None,
            command.Reason ?? proposal.Reason,
            cancellationToken).ConfigureAwait(false);
        return new MemoryMutationResult(true, proposalId, MemoryLifecycleStatus.PendingReview, Effect: MemoryMutationEffect.None);
    }

    private async Task<MemoryMutationResult?> RecordProposalFeedbackAsync(
        RecordMemoryFeedback command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        var proposal = await FindReviewProposalAsync(
            command.MemoryRecordId,
            null,
            null,
            cancellationToken,
            includeClosed: true).ConfigureAwait(false);
        if (proposal is null)
        {
            return null;
        }

        var proposalId = BuildProposalReviewRecordId(proposal);
        await AppendProposalReviewAuditAsync(
            "record_feedback",
            proposal,
            proposalId,
            context,
            MemoryMutationEffect.None,
            command.Feedback,
            cancellationToken,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["mergeDecision"] = command.Decision.ToString(),
            }).ConfigureAwait(false);
        return new MemoryMutationResult(true, proposalId, MemoryLifecycleStatus.PendingReview, Effect: MemoryMutationEffect.None);
    }

    private async Task<MemoryMutationResult> WriteCandidateReviewAuditAsync(
        MemoryRecordId? memoryRecordId,
        MemorySpaceId? memorySpaceId,
        string? key,
        string operation,
        MemoryLifecycleStatus lifecycleStatus,
        MemoryMutationEffect effect,
        string? reason,
        MemorySourceRef? source,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        if (memorySpaceId is null)
        {
            return new MemoryMutationResult(false, DegradedReason: "memory_space_id_required", Effect: MemoryMutationEffect.Degraded);
        }

        var candidateMatch = await FindReviewCandidateAsync(memoryRecordId, memorySpaceId.Value, key, cancellationToken).ConfigureAwait(false);
        if (candidateMatch is null)
        {
            return new MemoryMutationResult(false, DegradedReason: "memory_review_target_not_found", Effect: MemoryMutationEffect.Degraded);
        }

        if (candidateMatch.Value.Space.IsReadOnly)
        {
            return ReadOnlySpace();
        }

        var candidate = candidateMatch.Value.Candidate;
        var pendingFact = ToPendingReviewFact(candidate, context.Timestamp);
        await store.AppendAuditRecordAsync(
            MemoryAuditRecords.Create(
                operation,
                candidate.MemorySpaceId,
                candidate.Key,
                context.ActorId,
                source?.SourceId ?? candidate.Source?.SourceId ?? operation,
                context.Timestamp,
                value: candidate.Value,
                confidence: candidate.Confidence,
                effect: effect,
                reason: reason ?? lifecycleStatus.ToString(),
                snippet: source?.Snippet ?? candidate.Source?.Snippet,
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["reviewRecordId"] = pendingFact.Id.Value,
                    ["targetLifecycleStatus"] = lifecycleStatus.ToString(),
                }),
            cancellationToken).ConfigureAwait(false);

        return new MemoryMutationResult(
            true,
            pendingFact.Id,
            lifecycleStatus,
            Effect: effect);
    }

    private async Task AppendProposalReviewAuditAsync(
        string operation,
        TianShuMemoryAuditRecord proposal,
        MemoryRecordId proposalId,
        MemoryOperationContext context,
        MemoryMutationEffect effect,
        string? reason,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? additionalMetadata = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["reviewRecordId"] = proposalId.Value,
            ["proposalKind"] = TryGetMetadataValue(proposal.Metadata, "proposalKind") ?? string.Empty,
            ["proposalIdempotencyKey"] = TryGetMetadataValue(proposal.Metadata, "idempotencyKey") ?? string.Empty,
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
                operation,
                proposal.MemorySpaceId,
                proposal.Key,
                context.ActorId,
                operation,
                context.Timestamp,
                confidence: proposal.Confidence,
                effect: effect,
                reason: reason,
                metadata: metadata),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<TianShuMemoryAuditRecord?> FindReviewProposalAsync(
        MemoryRecordId? memoryRecordId,
        MemorySpaceId? memorySpaceId,
        string? key,
        CancellationToken cancellationToken,
        bool includeClosed = false)
    {
        var audit = await store.ListAuditRecordsAsync(memorySpaceId, cancellationToken).ConfigureAwait(false);
        return audit
            .Where(record => includeClosed
                ? IsReviewProposal(record)
                : IsPendingReviewProposal(record, audit))
            .Where(record => memoryRecordId is null || BuildProposalReviewRecordId(record) == memoryRecordId.Value)
            .FirstOrDefault(record => string.IsNullOrWhiteSpace(key) || string.Equals(record.Key, key, StringComparison.Ordinal));
    }

    private static MemoryReviewItem ToProposalReviewItem(TianShuMemoryAuditRecord proposal)
    {
        var proposalKind = TryGetMetadataValue(proposal.Metadata, "proposalKind") ?? "proposal";
        var targetRecordId = TryGetMetadataValue(proposal.Metadata, "targetRecordId");
        var targetLifecycle = TryGetMetadataValue(proposal.Metadata, "targetLifecycleStatus");
        var valueText = proposalKind switch
        {
            "archive_proposal" => $"归档提案：{proposal.Reason ?? proposal.Key}",
            "forget_proposal" => $"遗忘提案：{proposal.Reason ?? proposal.Key}",
            "overlay_cache_rebuild_proposal" => $"重建 overlay cache 提案：{proposal.Reason ?? proposal.Key}",
            "supersede_proposal" => $"取代提案：{proposal.Reason ?? proposal.Key}",
            _ => $"{proposalKind}：{proposal.Reason ?? proposal.Key}",
        };
        var source = new MemorySourceRef(
            MemorySourceKind.System,
            proposal.Source,
            role: "memory-consolidation",
            snippet: proposal.Reason,
            capturedAt: proposal.OccurredAt,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["proposalKind"] = proposalKind,
                ["targetRecordId"] = targetRecordId ?? string.Empty,
                ["targetLifecycleStatus"] = targetLifecycle ?? string.Empty,
            });
        var record = new FactMemoryRecord(
            proposal.Key,
            StructuredValue.FromString(valueText),
            proposal.MemorySpaceId,
            proposal.Confidence ?? 0.5m,
            proposal.OccurredAt,
            BuildProposalReviewRecordId(proposal),
            MemoryLifecycleStatus.PendingReview,
            sources: [source],
            tags: ["memory-proposal", proposalKind],
            updatedAt: proposal.OccurredAt);

        return new MemoryReviewItem(
            record,
            Audit:
            [
                new MemoryReviewAuditSummary(
                    proposal.Operation,
                    proposal.Effect,
                    proposal.Actor,
                    proposal.Source,
                    proposal.OccurredAt,
                    proposal.Reason,
                    proposal.ReasonCodes,
                    proposal.Metadata)
            ]);
    }

    private async Task<MemoryReviewItem> BuildReviewItemAsync(
        FactMemoryRecord record,
        MemoryCandidate? candidate,
        ListMemoryReviews query,
        CancellationToken cancellationToken)
    {
        var evidence = query.IncludeEvidence
            ? await store.ListEvidenceRecordsAsync(record.MemorySpaceId, cancellationToken).ConfigureAwait(false)
            : Array.Empty<MemoryEvidenceRecord>();
        var links = query.IncludeSupersedeLinks
            ? await store.ListSupersedeLinksAsync(record.MemorySpaceId, cancellationToken).ConfigureAwait(false)
            : Array.Empty<MemorySupersedeLink>();
        var audit = query.IncludeAudit
            ? await store.ListAuditRecordsAsync(record.MemorySpaceId, cancellationToken).ConfigureAwait(false)
            : Array.Empty<TianShuMemoryAuditRecord>();

        return new MemoryReviewItem(
            record,
            candidate,
            evidence
                .Where(item => IsRelatedEvidence(item, record))
                .OrderBy(static item => item.CapturedAt)
                .ToArray(),
            links
                .Where(link => link.OldRecordId == record.Id || link.NewRecordId == record.Id)
                .OrderBy(static link => link.Timestamp)
                .ToArray(),
            audit
                .Where(item => IsRelatedAudit(item, record))
                .OrderBy(static item => item.OccurredAt)
                .Select(static item => new MemoryReviewAuditSummary(
                    item.Operation,
                    item.Effect,
                    item.Actor,
                    item.Source,
                    item.OccurredAt,
                    item.Reason,
                    item.ReasonCodes,
                    item.Metadata))
                .ToArray());
    }

    private async Task<MemorySpace?> ResolveSpaceAsync(
        MemorySpaceId memorySpaceId,
        CancellationToken cancellationToken)
        => (await ListSpacesCoreAsync(memorySpaceId, includeVirtualSpace: true, cancellationToken).ConfigureAwait(false)).FirstOrDefault();

    private async Task<IReadOnlyList<MemorySpace>> ListSpacesCoreAsync(
        MemorySpaceId? memorySpaceId,
        bool includeVirtualSpace,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, MemorySpace>(StringComparer.Ordinal);
        foreach (var space in spaces)
        {
            if (memorySpaceId is not { } targetSpaceId
                || string.Equals(space.Id.Value, targetSpaceId.Value, StringComparison.Ordinal))
            {
                results[space.Id.Value] = space;
            }
        }

        var storedSpaces = await store.ListSpacesAsync(memorySpaceId, cancellationToken).ConfigureAwait(false);
        foreach (var space in storedSpaces)
        {
            results[space.Id.Value] = space;
        }

        if (results.Count == 0
            && includeVirtualSpace
            && memorySpaceId is { } virtualSpaceId
            && TryCreateLazySpace(virtualSpaceId, out var lazySpace))
        {
            results[lazySpace.Id.Value] = lazySpace;
        }

        return results.Values
            .OrderBy(static space => space.Id.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool TryCreateLazySpace(MemorySpaceId memorySpaceId, out MemorySpace memorySpace)
    {
        memorySpace = null!;
        var segments = memorySpaceId.Value.Split(':', 3, StringSplitOptions.TrimEntries);
        if (segments.Length != 3
            || !string.Equals(segments[0], "memory", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(segments[2])
            || !TryParseScope(segments[1], out var scopeKind))
        {
            return false;
        }

        var normalizedScopeKey = IdentifierGuard.AgainstNullOrWhiteSpace(segments[2], "scopeKey");
        memorySpace = new MemorySpace(
            memorySpaceId,
            scopeKind,
            normalizedScopeKey,
            $"TianShu {scopeKind} Memory");
        return true;
    }

    private static bool TryParseScope(string scope, out MemoryScopeKind scopeKind)
    {
        if (Enum.TryParse(scope, ignoreCase: true, out scopeKind))
        {
            return true;
        }

        scopeKind = default;
        return false;
    }

    private async Task<(MemorySpace Space, FactMemoryRecord Fact)?> FindFactAsync(
        MemoryRecordId memoryRecordId,
        MemorySpaceId? memorySpaceId,
        string? key,
        CancellationToken cancellationToken)
    {
        foreach (var space in await ListSpacesCoreAsync(memorySpaceId, includeVirtualSpace: false, cancellationToken).ConfigureAwait(false))
        {
            var facts = await store.ListFactsAsync(space, cancellationToken).ConfigureAwait(false);
            var fact = facts.FirstOrDefault(candidate =>
                string.Equals(candidate.Id.Value, memoryRecordId.Value, StringComparison.Ordinal)
                && (string.IsNullOrWhiteSpace(key) || string.Equals(candidate.Key, key, StringComparison.Ordinal)));
            if (fact is not null)
            {
                return (space, fact);
            }
        }

        return null;
    }

    private async Task<(MemorySpace Space, FactMemoryRecord Fact)?> FindAnyFactAsync(
        MemoryRecordId? memoryRecordId,
        MemorySpaceId? memorySpaceId,
        string? key,
        CancellationToken cancellationToken)
    {
        foreach (var space in await ListSpacesCoreAsync(memorySpaceId, includeVirtualSpace: false, cancellationToken).ConfigureAwait(false))
        {
            var facts = await store.ListFactsAsync(space, cancellationToken).ConfigureAwait(false);
            var fact = facts.FirstOrDefault(candidate =>
                (memoryRecordId is null || string.Equals(candidate.Id.Value, memoryRecordId.Value.Value, StringComparison.Ordinal))
                && (string.IsNullOrWhiteSpace(key) || string.Equals(candidate.Key, key, StringComparison.Ordinal)));
            if (fact is not null)
            {
                return (space, fact);
            }
        }

        return null;
    }

    private async Task<(MemorySpace Space, FactMemoryRecord Fact)?> FindReviewFactAsync(
        MemoryRecordId? memoryRecordId,
        MemorySpaceId? memorySpaceId,
        string? key,
        CancellationToken cancellationToken)
    {
        foreach (var space in await ListSpacesCoreAsync(memorySpaceId, includeVirtualSpace: false, cancellationToken).ConfigureAwait(false))
        {
            var facts = await store.ListFactsAsync(space, cancellationToken).ConfigureAwait(false);
            var fact = facts.FirstOrDefault(candidate =>
                (memoryRecordId is null || string.Equals(candidate.Id.Value, memoryRecordId.Value.Value, StringComparison.Ordinal))
                && (string.IsNullOrWhiteSpace(key) || string.Equals(candidate.Key, key, StringComparison.Ordinal))
                && candidate.LifecycleStatus is MemoryLifecycleStatus.PendingReview or MemoryLifecycleStatus.Active);
            if (fact is not null)
            {
                return (space, fact);
            }
        }

        return null;
    }

    private async Task<(MemorySpace Space, MemoryCandidate Candidate)?> FindReviewCandidateAsync(
        MemoryRecordId? memoryRecordId,
        MemorySpaceId? memorySpaceId,
        string? key,
        CancellationToken cancellationToken)
    {
        var candidates = await store.ListCandidatesAsync(memorySpaceId, cancellationToken).ConfigureAwait(false);
        foreach (var candidate in candidates)
        {
            var candidateRecordId = ToPendingReviewFact(candidate, DateTimeOffset.UtcNow).Id;
            if (memoryRecordId is not null
                && !string.Equals(candidateRecordId.Value, memoryRecordId.Value.Value, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(key)
                && !string.Equals(candidate.Key, key, StringComparison.Ordinal))
            {
                continue;
            }

            var space = await ResolveSpaceAsync(candidate.MemorySpaceId, cancellationToken).ConfigureAwait(false);
            if (space is not null)
            {
                return (space, candidate);
            }
        }

        return null;
    }

    private static FactMemoryRecord ToPendingReviewFact(MemoryCandidate candidate, DateTimeOffset recordedAt)
        => new(
            candidate.Key,
            candidate.Value,
            candidate.MemorySpaceId,
            candidate.Confidence,
            recordedAt,
            lifecycleStatus: MemoryLifecycleStatus.PendingReview,
            sources: candidate.Source is null ? Array.Empty<MemorySourceRef>() : [candidate.Source],
            tags: candidate.ContextSignature?.Tags,
            formationPath: candidate.FormationPath,
            contextSignature: candidate.ContextSignature,
            validationEvidence: candidate.ValidationEvidence,
            isCounterexample: candidate.IsCounterexample);

    private async Task<IReadOnlySet<string>> ListRejectedCandidateKeysAsync(
        MemorySpaceId memorySpaceId,
        CancellationToken cancellationToken)
    {
        var audit = await store.ListAuditRecordsAsync(memorySpaceId, cancellationToken).ConfigureAwait(false);
        var states = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var record in audit
                     .Where(static record => IsCandidateReviewStateOperation(record.Operation))
                     .OrderBy(static record => record.OccurredAt))
        {
            states[BuildFactKey(record.MemorySpaceId, record.Key)] = record.Operation;
        }

        return states
            .Where(static pair => pair.Value is "reject_memory_review" or "demote_memory_review" or "merge_memory_review")
            .Select(static pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string BuildFactKey(MemorySpaceId memorySpaceId, string key)
        => string.Concat(memorySpaceId.Value, '\u001f', key);

    private static bool IsRelatedEvidence(MemoryEvidenceRecord evidence, FactMemoryRecord record)
        => evidence.MemorySpaceId == record.MemorySpaceId
           && (string.Equals(TryGetMetadataValue(evidence.Metadata, "key"), record.Key, StringComparison.Ordinal)
               || string.Equals(TryGetMetadataValue(evidence.Metadata, "memoryKey"), record.Key, StringComparison.Ordinal)
               || record.ValidationEvidence.Any(item => string.Equals(item.EvidenceId, evidence.EvidenceId, StringComparison.Ordinal)));

    private static bool IsRelatedAudit(TianShuMemoryAuditRecord audit, FactMemoryRecord record)
        => audit.MemorySpaceId == record.MemorySpaceId
           && (string.Equals(audit.Key, record.Key, StringComparison.Ordinal)
               || audit.Metadata.Values.Any(value => string.Equals(value, record.Id.Value, StringComparison.Ordinal)));

    private static bool IsPendingReviewProposal(
        TianShuMemoryAuditRecord proposal,
        IReadOnlyList<TianShuMemoryAuditRecord> audit)
    {
        if (!IsReviewProposal(proposal))
        {
            return false;
        }

        var proposalId = BuildProposalReviewRecordId(proposal);
        var latestState = audit
            .Where(record => IsProposalReviewStateOperation(record.Operation)
                             && record.Metadata.TryGetValue("reviewRecordId", out var reviewRecordId)
                             && string.Equals(reviewRecordId, proposalId.Value, StringComparison.Ordinal))
            .OrderBy(static record => record.OccurredAt)
            .LastOrDefault();
        return latestState is null || string.Equals(latestState.Operation, "restore_memory_review", StringComparison.Ordinal);
    }

    private static bool IsReviewProposal(TianShuMemoryAuditRecord audit)
        => string.Equals(audit.Operation, MemoryConsolidationWorker.ProposalOperationName, StringComparison.Ordinal)
           && string.Equals(TryGetMetadataValue(audit.Metadata, "permissionBoundary"), "audit-only", StringComparison.Ordinal);

    private static MemoryRecordId BuildProposalReviewRecordId(TianShuMemoryAuditRecord proposal)
    {
        var stableKey = TryGetMetadataValue(proposal.Metadata, "idempotencyKey")
                        ?? string.Concat(proposal.MemorySpaceId.Value, '\u001f', proposal.Key, '\u001f', proposal.OccurredAt.ToString("O"));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stableKey))).ToLowerInvariant();
        return new MemoryRecordId($"memory-proposal-{hash[..24]}");
    }

    private static string? TryGetMetadataValue(IReadOnlyDictionary<string, string> metadata, string key)
        => metadata.TryGetValue(key, out var value) ? value : null;

    private static bool IsCandidateReviewStateOperation(string operation)
        => operation is "reject_memory_review" or "demote_memory_review" or "merge_memory_review" or "restore_memory_review";

    private static bool IsProposalReviewStateOperation(string operation)
        => operation is "approve_memory_review" or "reject_memory_review" or "demote_memory_review" or "merge_memory_review" or "restore_memory_review";

    private static MemoryMutationResult Unsupported(MemoryProviderCapability capability)
        => new(false, UnsupportedCapability: capability, DegradedReason: "unsupported_capability", Effect: MemoryMutationEffect.Degraded);

    private static MemoryMutationResult ReadOnlySpace()
        => new(false, DegradedReason: "memory_space_read_only", Effect: MemoryMutationEffect.Degraded);
}
