using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Primitives;

namespace TianShu.IdentityMemory;

/// <summary>
/// 默认记忆服务 façade，串联 provider registry、policy 与 overlay 解析。
/// Default memory-service facade that composes provider registry, policy, and overlay resolution.
/// </summary>
public sealed class DefaultMemoryService : IMemoryService
{
    private readonly MemoryProviderRegistry registry;
    private readonly MemoryOverlayResolver overlayResolver;
    private readonly IMemoryExtractor extractor;
    private readonly IMemoryAuditSink auditSink;
    private readonly MemoryPolicyEngine policy;
    private readonly MemoryFormationTracker formationTracker;
    private readonly IDiagnosticEventSink? diagnosticEventSink;
    private readonly IDiagnosticOperationScopeFactory? diagnosticOperationScopeFactory;

    public DefaultMemoryService(
        MemoryProviderRegistry registry,
        MemoryOverlayResolver? overlayResolver = null,
        IMemoryExtractor? extractor = null,
        IMemoryAuditSink? auditSink = null,
        MemoryPolicyEngine? policy = null,
        MemoryFormationTracker? formationTracker = null,
        IDiagnosticEventSink? diagnosticEventSink = null,
        IDiagnosticOperationScopeFactory? diagnosticOperationScopeFactory = null)
    {
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        this.overlayResolver = overlayResolver ?? new MemoryOverlayResolver();
        this.extractor = extractor ?? new DefaultMemoryExtractor();
        this.auditSink = auditSink ?? NullMemoryAuditSink.Instance;
        this.policy = policy ?? new MemoryPolicyEngine();
        this.formationTracker = formationTracker ?? new MemoryFormationTracker();
        this.diagnosticEventSink = diagnosticEventSink;
        this.diagnosticOperationScopeFactory = diagnosticOperationScopeFactory;
    }

    public IReadOnlyList<MemoryProviderDescriptor> ListProviders() => registry.ListProviders();

    public IReadOnlyList<MemoryProviderDescriptor> ListProviders(ListMemoryProviders query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return registry.ListProviders(query);
    }

    public async Task<IReadOnlyList<MemorySpace>> ListSpacesAsync(
        ListMemorySpaces query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var results = new Dictionary<string, MemorySpace>(StringComparer.Ordinal);
        var providers = await registry.ResolveProvidersAsync(null, MemoryProviderCapability.ListSpaces, cancellationToken).ConfigureAwait(false);
        foreach (var provider in providers)
        {
            var spaces = await provider.ListSpacesAsync(null, cancellationToken).ConfigureAwait(false);
            foreach (var space in spaces)
            {
                if (query.ScopeKind is { } scopeKind && space.ScopeKind != scopeKind)
                {
                    continue;
                }

                results[space.Id.Value] = space;
            }
        }

        return results.Values.ToArray();
    }

    public async Task<MemoryMutationResult> AddAsync(
        AddMemory command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var resolution = await registry.ResolveProvidersWithDiagnosticsAsync(command.MemorySpaceId, MemoryProviderCapability.Add, cancellationToken).ConfigureAwait(false);
        var provider = resolution.Providers.FirstOrDefault();
        var result = provider is null
            ? MissingProviderMutation(MemoryProviderCapability.Add, resolution.DegradedProviders)
            : await provider.AddAsync(command, context, cancellationToken).ConfigureAwait(false);
        await EmitMutationStatsAsync(
            "add_memory",
            command.MemorySpaceId,
            command.Key,
            result,
            MemoryProviderCapability.Add,
            cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<IReadOnlyList<MemoryCandidate>> ExtractAsync(
        ExtractMemory command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var job = MemoryExtractionJob.Start(
            command.MemorySpaceId,
            command.Source,
            extractor.GetType().Name,
            context.Timestamp);
        var candidates = await extractor.ExtractAsync(command, context, cancellationToken).ConfigureAwait(false);
        var completedJob = job.Complete(candidates.Count, context.Timestamp);

        var resolution = await registry.ResolveProvidersWithDiagnosticsAsync(
            command.MemorySpaceId,
            MemoryProviderCapability.Extract,
            cancellationToken).ConfigureAwait(false);
        var provider = resolution.Providers.FirstOrDefault();
        var stateProvider = provider as IMemoryProviderStateStore;
        var memorySpace = stateProvider is null
            ? null
            : await stateProvider.ResolveStateSpaceAsync(command.MemorySpaceId, cancellationToken).ConfigureAwait(false);

        var projectedCandidates = new List<MemoryCandidate>(candidates.Count);
        foreach (var candidate in candidates)
        {
            if (stateProvider is null || memorySpace is null)
            {
                projectedCandidates.Add(candidate);
                continue;
            }

            var existingFacts = await stateProvider.ListStateFactsAsync(memorySpace, cancellationToken).ConfigureAwait(false);
            var initialEvidence = CreateExtractionEvidence(command, candidate, completedJob, context);
            var formation = formationTracker.Track(candidate, existingFacts, [initialEvidence], context.Timestamp);
            var trackedCandidate = ApplyFormation(candidate, formation);
            projectedCandidates.Add(trackedCandidate);

            var evidence = CreateExtractionEvidence(command, trackedCandidate, completedJob, context, formation.Metadata);
            var ingestionDecision = policy.Ingestion.EvaluateEvidence(
                evidence,
                new MemoryIngestionPolicyContext(
                    context,
                    provider!.Descriptor,
                    SourceScopeKind: MemoryScopeKind.Session,
                    TargetScopeKind: memorySpace.ScopeKind,
                    SourceStrength: MemorySourceStrength.Strong,
                    TargetSpaceIsReadOnly: memorySpace.IsReadOnly));
            await stateProvider.AppendEvidenceRecordAsync(evidence, cancellationToken).ConfigureAwait(false);

            var promotionDecision = ingestionDecision.Kind == MemoryIngestionDecisionKind.Reject
                ? new MemoryPromotionDecision(
                    MemoryPromotionDecisionKind.Reject,
                    ingestionDecision.ReasonCodes,
                    ingestionDecision.TargetLifecycleStatus,
                    ingestionDecision.Explanation)
                : policy.Promotion.EvaluateCandidate(
                    trackedCandidate,
                    new MemoryPromotionPolicyContext(
                        context,
                        provider!.Descriptor,
                        existingFacts,
                        SourceScopeKind: MemoryScopeKind.Session,
                        TargetScopeKind: memorySpace.ScopeKind,
                        SourceStrength: MemorySourceStrength.Strong,
                        TargetSpaceIsReadOnly: memorySpace.IsReadOnly));

            await stateProvider.UpsertCandidateAsync(trackedCandidate, cancellationToken).ConfigureAwait(false);
            var promotionAuditMetadata = CreatePromotionAuditMetadata(
                completedJob,
                evidence,
                promotionDecision);
            await auditSink.AppendAsync(
                MemoryAuditRecords.Create(
                    "promote_memory_candidate",
                    trackedCandidate.MemorySpaceId,
                    trackedCandidate.Key,
                    context.ActorId,
                    trackedCandidate.Source?.SourceId ?? command.Source.SourceId,
                    context.Timestamp,
                    trackedCandidate.Value,
                    trackedCandidate.Confidence,
                    effect: PromotionEffect(promotionDecision),
                    reasonCodes: promotionDecision.ReasonCodes,
                    reason: promotionDecision.Explanation,
                    snippet: command.Source.Snippet,
                    metadata: promotionAuditMetadata),
                cancellationToken).ConfigureAwait(false);
            await EmitMutationStatsAsync(
                "promote_memory_candidate",
                trackedCandidate.MemorySpaceId,
                trackedCandidate.Key,
                new MemoryMutationResult(
                    true,
                    LifecycleStatus: promotionDecision.TargetLifecycleStatus ?? trackedCandidate.LifecycleStatus,
                    Effect: PromotionEffect(promotionDecision)),
                MemoryProviderCapability.Extract,
                cancellationToken,
                promotionAuditMetadata).ConfigureAwait(false);

            if (promotionDecision.Kind == MemoryPromotionDecisionKind.AutoPromote)
            {
                await stateProvider.UpsertFactAsync(
                    memorySpace,
                    new FactMemoryRecord(
                        trackedCandidate.Key,
                        trackedCandidate.Value,
                        trackedCandidate.MemorySpaceId,
                        trackedCandidate.Confidence,
                        context.Timestamp,
                        lifecycleStatus: promotionDecision.TargetLifecycleStatus ?? MemoryLifecycleStatus.Active,
                        sources: [trackedCandidate.Source ?? command.Source],
                        tags: trackedCandidate.ContextSignature?.Tags,
                        formationPath: trackedCandidate.FormationPath,
                        contextSignature: trackedCandidate.ContextSignature,
                        validationEvidence: trackedCandidate.ValidationEvidence,
                        isCounterexample: trackedCandidate.IsCounterexample),
                    context.ActorId,
                    (trackedCandidate.Source ?? command.Source).SourceId,
                    context.Timestamp,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        var auditKey = candidates.Count == 1
            ? candidates[0].Key
            : completedJob.JobId.Value;
        await auditSink.AppendAsync(
            MemoryAuditRecords.FromContext(
                "extract_memory",
                command.MemorySpaceId,
                auditKey,
                context,
                command.Source,
                value: command.Content),
            cancellationToken).ConfigureAwait(false);
        return projectedCandidates;
    }

    private static MemoryEvidenceRecord CreateExtractionEvidence(
        ExtractMemory command,
        MemoryCandidate candidate,
        MemoryExtractionJob completedJob,
        MemoryOperationContext context,
        IReadOnlyDictionary<string, string>? formationMetadata = null)
    {
        var evidenceId = $"{completedJob.JobId.Value}:{candidate.Key}";
        var summary = candidate.ExtractionReason
            ?? command.Source.Snippet
            ?? $"Extracted candidate {candidate.Key}";
        return new MemoryEvidenceRecord(
            evidenceId,
            candidate.MemorySpaceId,
            candidate.Source ?? command.Source,
            candidate.IsCounterexample ? MemoryEvidenceKind.CommandFailure : MemoryEvidenceKind.UserCorrection,
            summary,
            MemoryPolicySafety.ResolveScopeKind(candidate.MemorySpaceId),
            context.Timestamp,
            candidate.ContextSignature?.Tags,
            candidate.ValidationEvidence,
            MergeMetadata(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["jobId"] = completedJob.JobId.Value,
                    ["candidateKey"] = candidate.Key,
                    ["ruleId"] = candidate.RuleId ?? string.Empty,
                    ["formationPath"] = candidate.FormationPath.ToString(),
                },
                formationMetadata));
    }

    private static MemoryCandidate ApplyFormation(
        MemoryCandidate candidate,
        MemoryFormationSnapshot formation)
        => new(
            candidate.Key,
            candidate.Value,
            candidate.MemorySpaceId,
            candidate.Confidence,
            candidate.Source,
            candidate.ExtractionReason,
            candidate.RuleId,
            formation.FormationPath,
            candidate.ValidationEvidence,
            candidate.ContextSignature,
            candidate.IsCounterexample);

    private static IReadOnlyDictionary<string, string> MergeMetadata(
        Dictionary<string, string> metadata,
        IReadOnlyDictionary<string, string>? additionalMetadata)
    {
        if (additionalMetadata is not null)
        {
            foreach (var item in additionalMetadata)
            {
                metadata[item.Key] = item.Value;
            }
        }

        return metadata;
    }

    private static MemoryMutationEffect PromotionEffect(MemoryPromotionDecision decision)
        => decision.Kind switch
        {
            MemoryPromotionDecisionKind.AutoPromote => MemoryMutationEffect.Upserted,
            MemoryPromotionDecisionKind.Reject => MemoryMutationEffect.Degraded,
            _ => MemoryMutationEffect.None,
        };

    private static IReadOnlyDictionary<string, string> CreatePromotionAuditMetadata(
        MemoryExtractionJob completedJob,
        MemoryEvidenceRecord evidence,
        MemoryPromotionDecision promotionDecision)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["jobId"] = completedJob.JobId.Value,
            ["decisionKind"] = promotionDecision.Kind.ToString(),
            ["evidenceId"] = evidence.EvidenceId,
        };
        if (promotionDecision.Kind is MemoryPromotionDecisionKind.NeedsReview
            or MemoryPromotionDecisionKind.SupersedeProposal
            or MemoryPromotionDecisionKind.Reject)
        {
            metadata["governanceCheckpointKind"] = "Approval";
            metadata["riskSource"] = "policy_rule";
            metadata["approvalQueueProjection"] = "memory.review";
            metadata["userDecision"] = "pending";
            metadata["executionResult"] = "not_executed";
        }

        return metadata;
    }

    public async Task<MemoryMutationResult> ImportAsync(
        ImportMemory command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var resolution = await registry.ResolveProvidersWithDiagnosticsAsync(command.MemorySpaceId, MemoryProviderCapability.Import, cancellationToken).ConfigureAwait(false);
        var provider = resolution.Providers.FirstOrDefault();
        var result = provider is null
            ? MissingProviderMutation(MemoryProviderCapability.Import, resolution.DegradedProviders)
            : await provider.ImportAsync(command, context, cancellationToken).ConfigureAwait(false);
        await auditSink.AppendAsync(
            MemoryAuditRecords.FromContext(
                "import_memory",
                command.MemorySpaceId,
                ImportAuditKey(command),
                context,
                command.Source,
                effect: result.Effect,
                reasonCodes: result.UnsupportedCapability is null && string.IsNullOrWhiteSpace(result.DegradedReason)
                    ? null
                    : [MemoryRiskReasonCode.ProviderCapabilityMissing],
                reason: result.DegradedReason),
            cancellationToken).ConfigureAwait(false);
        await EmitMutationStatsAsync(
            "import_memory",
            command.MemorySpaceId,
            ImportAuditKey(command),
            result,
            MemoryProviderCapability.Import,
            cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<MemoryQueryResult> ExportAsync(
        ExportMemory command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var resolution = await registry.ResolveProvidersWithDiagnosticsAsync(command.MemorySpaceId, MemoryProviderCapability.Export, cancellationToken).ConfigureAwait(false);
        var provider = resolution.Providers.FirstOrDefault();
        return provider is null
            ? new MemoryQueryResult(degradedProviders: MissingProviderQueryDegradedProviders(resolution.DegradedProviders, "export"))
            : await provider.ExportAsync(command, context, cancellationToken).ConfigureAwait(false);
    }

    public Task<MemoryMutationResult> BindProviderAsync(
        BindMemoryProvider command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        return BindProviderCoreAsync(command, context, cancellationToken);
    }

    private async Task<MemoryMutationResult> BindProviderCoreAsync(
        BindMemoryProvider command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        var result = await registry.BindProviderAsync(command, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            await EmitMutationStatsAsync(
                "bind_memory_provider",
                command.MemorySpaceId,
                command.ProviderId,
                result,
                command.AllowedCapabilities,
                cancellationToken).ConfigureAwait(false);
            return result;
        }

        await auditSink.AppendAsync(
            MemoryAuditRecords.FromContext(
                "bind_memory_provider",
                command.MemorySpaceId,
                command.ProviderId,
                context,
                sourceFallback: "memory-provider-binding",
                effect: result.Effect),
            cancellationToken).ConfigureAwait(false);
        await EmitMutationStatsAsync(
            "bind_memory_provider",
            command.MemorySpaceId,
            command.ProviderId,
            result,
            command.AllowedCapabilities,
            cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<MemoryQueryResult> FilterAsync(
        FilterMemory query,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.MemorySpaceId is { } memorySpaceId)
        {
            var searchResolution = await ResolveFilterProvidersAsync(query, memorySpaceId, cancellationToken).ConfigureAwait(false);
            var scopedResolution = searchResolution.Resolution;
            var effectiveQuery = searchResolution.Query;
            var provider = scopedResolution.Providers.FirstOrDefault();
            var result = provider is null
                ? new MemoryQueryResult(
                    degradedProviders: MergeDegradedProviders(scopedResolution.DegradedProviders, searchResolution.DegradedProviders, MissingProviderQueryDegradedProviders(scopedResolution.DegradedProviders)),
                    EffectiveSearchMode: searchResolution.EffectiveSearchMode)
                : await provider.FilterAsync(effectiveQuery, context, cancellationToken).ConfigureAwait(false);
            var scopedDegraded = MergeDegradedProviders(result.DegradedProviders, searchResolution.DegradedProviders);
            var scopedResult = new MemoryQueryResult(
                result.Records,
                result.TotalCount,
                scopedDegraded,
                result.Citation,
                result.Explanations,
                searchResolution.EffectiveSearchMode);
            await AuditFilterAsync(effectiveQuery, context, scopedResult.Records, cancellationToken).ConfigureAwait(false);
            return scopedResult;
        }

        var records = new List<FactMemoryRecord>();
        var aggregateResolution = await ResolveFilterProvidersAsync(query, null, cancellationToken).ConfigureAwait(false);
        var resolution = aggregateResolution.Resolution;
        var effectiveAggregateQuery = aggregateResolution.Query;
        var degradedProviders = new List<string>(resolution.DegradedProviders);
        degradedProviders.AddRange(aggregateResolution.DegradedProviders);
        foreach (var provider in resolution.Providers)
        {
            var result = await provider.FilterAsync(effectiveAggregateQuery, context, cancellationToken).ConfigureAwait(false);
            records.AddRange(result.Records);
            degradedProviders.AddRange(result.DegradedProviders);
        }

        var queryResult = new MemoryQueryResult(
            records.OrderBy(static fact => fact.Key, StringComparer.Ordinal).ToArray(),
            degradedProviders: degradedProviders.Distinct(StringComparer.Ordinal).ToArray(),
            EffectiveSearchMode: aggregateResolution.EffectiveSearchMode);
        await AuditFilterAsync(effectiveAggregateQuery, context, queryResult.Records, cancellationToken).ConfigureAwait(false);
        return queryResult;
    }

    private async Task<MemoryFilterProviderResolution> ResolveFilterProvidersAsync(
        FilterMemory query,
        MemorySpaceId? memorySpaceId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.QueryText))
        {
            var structured = await registry.ResolveProvidersWithDiagnosticsAsync(
                memorySpaceId,
                MemoryProviderCapability.Filter,
                cancellationToken).ConfigureAwait(false);
            return new MemoryFilterProviderResolution(structured, query, MemorySearchMode.Structured);
        }

        if (query.SearchMode == MemorySearchMode.Semantic)
        {
            var semantic = await registry.ResolveProvidersWithDiagnosticsAsync(
                memorySpaceId,
                MemoryProviderCapability.Filter | MemoryProviderCapability.SemanticSearch,
                cancellationToken).ConfigureAwait(false);
            if (semantic.Providers.Count > 0)
            {
                return new MemoryFilterProviderResolution(semantic, query, MemorySearchMode.Semantic);
            }

            var keyword = await registry.ResolveProvidersWithDiagnosticsAsync(
                memorySpaceId,
                MemoryProviderCapability.Filter | MemoryProviderCapability.KeywordSearch,
                cancellationToken).ConfigureAwait(false);
            return new MemoryFilterProviderResolution(
                keyword,
                query with { SearchMode = MemorySearchMode.Keyword },
                MemorySearchMode.Keyword,
                MergeDegradedProviders(semantic.DegradedProviders, ["semantic_search_degraded:keyword"]));
        }

        if (query.SearchMode == MemorySearchMode.Keyword)
        {
            var keyword = await registry.ResolveProvidersWithDiagnosticsAsync(
                memorySpaceId,
                MemoryProviderCapability.Filter | MemoryProviderCapability.KeywordSearch,
                cancellationToken).ConfigureAwait(false);
            if (keyword.Providers.Count > 0)
            {
                return new MemoryFilterProviderResolution(keyword, query, MemorySearchMode.Keyword);
            }

            var structured = await registry.ResolveProvidersWithDiagnosticsAsync(
                memorySpaceId,
                MemoryProviderCapability.Filter,
                cancellationToken).ConfigureAwait(false);
            return new MemoryFilterProviderResolution(
                structured,
                query with { SearchMode = MemorySearchMode.Structured },
                MemorySearchMode.Structured,
                ["keyword_search_degraded:structured"]);
        }

        var fallback = await registry.ResolveProvidersWithDiagnosticsAsync(
            memorySpaceId,
            MemoryProviderCapability.Filter,
            cancellationToken).ConfigureAwait(false);
        return new MemoryFilterProviderResolution(fallback, query, MemorySearchMode.Structured);
    }

    public async Task<MemoryMutationResult> ForgetAsync(
        ForgetMemory command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.MemorySpaceId is not { } memorySpaceId)
        {
            return new MemoryMutationResult(false, DegradedReason: "memory_space_id_required");
        }

        var resolution = await registry.ResolveProvidersWithDiagnosticsAsync(memorySpaceId, MemoryProviderCapability.Forget, cancellationToken).ConfigureAwait(false);
        var provider = resolution.Providers.FirstOrDefault();
        var result = provider is null
            ? MissingProviderMutation(MemoryProviderCapability.Forget, resolution.DegradedProviders)
            : await provider.ForgetAsync(command, context, cancellationToken).ConfigureAwait(false);
        await EmitMutationStatsAsync(
            "forget_memory",
            memorySpaceId,
            command.Key ?? command.MemoryRecordId?.Value,
            result,
            MemoryProviderCapability.Forget,
            cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<MemoryMutationResult> DeleteAsync(
        DeleteMemory command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.MemorySpaceId is not { } memorySpaceId)
        {
            return new MemoryMutationResult(false, DegradedReason: "memory_space_id_required");
        }

        var resolution = await registry.ResolveProvidersWithDiagnosticsAsync(memorySpaceId, MemoryProviderCapability.Delete, cancellationToken).ConfigureAwait(false);
        var provider = resolution.Providers.FirstOrDefault();
        var result = provider is null
            ? MissingProviderMutation(MemoryProviderCapability.Delete, resolution.DegradedProviders)
            : await provider.DeleteAsync(command, context, cancellationToken).ConfigureAwait(false);
        await EmitMutationStatsAsync(
            "delete_memory",
            memorySpaceId,
            command.Key ?? command.MemoryRecordId?.Value,
            result,
            MemoryProviderCapability.Delete,
            cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<MemoryMutationResult> SupersedeAsync(
        SupersedeMemory command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var resolution = await registry.ResolveProvidersWithDiagnosticsAsync(
            command.MemorySpaceId,
            MemoryProviderCapability.Supersede,
            cancellationToken).ConfigureAwait(false);
        var provider = resolution.Providers.FirstOrDefault();
        var result = provider is null
            ? MissingProviderMutation(MemoryProviderCapability.Supersede, resolution.DegradedProviders)
            : await provider.SupersedeAsync(command, context, cancellationToken).ConfigureAwait(false);
        await auditSink.AppendAsync(
            MemoryAuditRecords.Supersede(command, context),
            cancellationToken).ConfigureAwait(false);
        await EmitMutationStatsAsync(
            "supersede_memory",
            command.MemorySpaceId,
            command.NewKey,
            result,
            MemoryProviderCapability.Supersede,
            cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<MemoryReviewQueryResult> ListReviewsAsync(
        ListMemoryReviews query,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);

        var resolution = await registry.ResolveProvidersWithDiagnosticsAsync(
            query.MemorySpaceId,
            MemoryProviderCapability.Review,
            cancellationToken).ConfigureAwait(false);
        var items = new List<MemoryReviewItem>();
        var degradedProviders = new List<string>(resolution.DegradedProviders);
        foreach (var provider in resolution.Providers)
        {
            var result = await provider.ListReviewsAsync(query, context, cancellationToken).ConfigureAwait(false);
            items.AddRange(result.Items);
            degradedProviders.AddRange(result.DegradedProviders);
        }

        return new MemoryReviewQueryResult(
            items
                .OrderBy(static item => item.Record.RecordedAt)
                .ThenBy(static item => item.Record.Key, StringComparer.Ordinal)
                .ToArray(),
            degradedProviders: degradedProviders.Distinct(StringComparer.Ordinal).ToArray());
    }

    public async Task<MemoryMutationResult> ApproveReviewAsync(
        ApproveMemoryReview command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var resolution = await registry.ResolveProvidersWithDiagnosticsAsync(
            command.MemorySpaceId,
            MemoryProviderCapability.Review,
            cancellationToken).ConfigureAwait(false);
        var provider = resolution.Providers.FirstOrDefault();
        var result = provider is null
            ? MissingProviderMutation(MemoryProviderCapability.Review, resolution.DegradedProviders)
            : await provider.ApproveReviewAsync(command, context, cancellationToken).ConfigureAwait(false);
        await EmitMutationStatsAsync(
            "approve_memory_review",
            command.MemorySpaceId,
            command.Key ?? command.MemoryRecordId?.Value,
            result,
            MemoryProviderCapability.Review,
            cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<MemoryMutationResult> DemoteReviewAsync(
        DemoteMemoryReview command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var resolution = await registry.ResolveProvidersWithDiagnosticsAsync(
            command.MemorySpaceId,
            MemoryProviderCapability.Review,
            cancellationToken).ConfigureAwait(false);
        var provider = resolution.Providers.FirstOrDefault();
        var result = provider is null
            ? MissingProviderMutation(MemoryProviderCapability.Review, resolution.DegradedProviders)
            : await provider.DemoteReviewAsync(command, context, cancellationToken).ConfigureAwait(false);
        await EmitMutationStatsAsync(
            "demote_memory_review",
            command.MemorySpaceId,
            command.Key ?? command.MemoryRecordId?.Value,
            result,
            MemoryProviderCapability.Review,
            cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<MemoryMutationResult> MergeReviewAsync(
        MergeMemoryReview command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var resolution = await registry.ResolveProvidersWithDiagnosticsAsync(
            command.MemorySpaceId,
            MemoryProviderCapability.Review | MemoryProviderCapability.Supersede,
            cancellationToken).ConfigureAwait(false);
        var provider = resolution.Providers.FirstOrDefault();
        var result = provider is null
            ? MissingProviderMutation(MemoryProviderCapability.Review | MemoryProviderCapability.Supersede, resolution.DegradedProviders)
            : await provider.MergeReviewAsync(command, context, cancellationToken).ConfigureAwait(false);
        await EmitMutationStatsAsync(
            "merge_memory_review",
            command.MemorySpaceId,
            command.MergedKey ?? command.ReviewRecordId.Value,
            result,
            MemoryProviderCapability.Review | MemoryProviderCapability.Supersede,
            cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<MemoryMutationResult> RestoreReviewAsync(
        RestoreMemoryReview command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var resolution = await registry.ResolveProvidersWithDiagnosticsAsync(
            command.MemorySpaceId,
            MemoryProviderCapability.Review,
            cancellationToken).ConfigureAwait(false);
        var provider = resolution.Providers.FirstOrDefault();
        var result = provider is null
            ? MissingProviderMutation(MemoryProviderCapability.Review, resolution.DegradedProviders)
            : await provider.RestoreReviewAsync(command, context, cancellationToken).ConfigureAwait(false);
        await EmitMutationStatsAsync(
            "restore_memory_review",
            command.MemorySpaceId,
            command.Key ?? command.MemoryRecordId?.Value,
            result,
            MemoryProviderCapability.Review,
            cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<MemoryMutationResult> RecordFeedbackAsync(
        RecordMemoryFeedback command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var resolution = await registry.ResolveProvidersWithDiagnosticsAsync(null, MemoryProviderCapability.Feedback, cancellationToken).ConfigureAwait(false);
        var firstFailure = default(MemoryMutationResult);
        foreach (var provider in resolution.Providers)
        {
            if (!await ProviderCanMutateTargetSpacesAsync(
                    provider,
                    MemoryProviderCapability.Feedback,
                    command,
                    cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var result = await provider.RecordFeedbackAsync(command, context, cancellationToken).ConfigureAwait(false);
            if (result.Success)
            {
                await EmitMutationStatsAsync(
                    "record_memory_feedback",
                    memorySpaceId: null,
                    key: command.MemoryRecordId.Value,
                    result: result,
                    capability: MemoryProviderCapability.Feedback,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                return result;
            }

            firstFailure ??= result;
        }

        var fallback = firstFailure
                       ?? MissingProviderMutation(MemoryProviderCapability.Feedback, resolution.DegradedProviders);
        await EmitMutationStatsAsync(
            "record_memory_feedback",
            memorySpaceId: null,
            key: command.MemoryRecordId.Value,
            result: fallback,
            capability: MemoryProviderCapability.Feedback,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return fallback;
    }

    public async Task<MemoryMutationResult> RecordCitationAsync(
        RecordMemoryCitation command,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var resolution = await registry.ResolveProvidersWithDiagnosticsAsync(null, MemoryProviderCapability.Citation, cancellationToken).ConfigureAwait(false);
        var updatedRecordId = default(MemoryRecordId?);
        var updatedStatus = default(MemoryLifecycleStatus?);
        var firstFailure = default(MemoryMutationResult);
        foreach (var provider in resolution.Providers)
        {
            if (!await ProviderCanMutateTargetSpacesAsync(
                    provider,
                    MemoryProviderCapability.Citation,
                    command,
                    cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var result = await provider.RecordCitationAsync(command, context, cancellationToken).ConfigureAwait(false);
            if (!result.Success)
            {
                firstFailure ??= result;
                continue;
            }

            updatedRecordId ??= result.RecordId;
            updatedStatus ??= result.LifecycleStatus;
        }

        var finalResult = updatedRecordId is null && updatedStatus is null
            ? firstFailure
              ?? MissingProviderMutation(MemoryProviderCapability.Citation, resolution.DegradedProviders)
            : new MemoryMutationResult(true, updatedRecordId, updatedStatus);
        await EmitMutationStatsAsync(
            "record_memory_citation",
            command.Citation.Entries.FirstOrDefault()?.MemorySpaceId,
            command.Citation.Entries.FirstOrDefault()?.MemoryRecordId.Value,
            finalResult,
            MemoryProviderCapability.Citation,
            cancellationToken).ConfigureAwait(false);
        return finalResult;
    }

    private async Task EmitMutationStatsAsync(
        string operationName,
        MemorySpaceId? memorySpaceId,
        string? key,
        MemoryMutationResult result,
        MemoryProviderCapability capability,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? governanceMetadata = null)
    {
        if (diagnosticEventSink is null)
        {
            return;
        }

        var stats = new MemoryMutationStats
        {
            OperationName = operationName,
            MemorySpaceId = memorySpaceId?.Value,
            MemoryRecordId = result.RecordId?.Value,
            Key = key,
            Capability = capability.ToString(),
            Success = result.Success,
            Effect = result.Effect.ToString(),
            LifecycleStatus = result.LifecycleStatus?.ToString(),
            DegradedReason = result.DegradedReason,
            UnsupportedCapability = result.UnsupportedCapability?.ToString(),
            GovernanceCheckpointKind = GetMetadataValue(governanceMetadata, "governanceCheckpointKind"),
            RiskSource = GetMetadataValue(governanceMetadata, "riskSource"),
            ApprovalQueueProjection = GetMetadataValue(governanceMetadata, "approvalQueueProjection"),
            UserDecision = GetMetadataValue(governanceMetadata, "userDecision"),
            ExecutionResult = GetMetadataValue(governanceMetadata, "executionResult"),
        };
        var metadataEntries = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["diagnosticModule"] = StructuredValue.FromString(DiagnosticModuleNames.Memory),
            ["status"] = StructuredValue.FromString(result.Success ? "completed" : "degraded"),
            ["summary"] = StructuredValue.FromString(operationName),
            ["degraded"] = StructuredValue.FromString(result.Success ? "false" : "true"),
        };
        if (governanceMetadata is not null)
        {
            foreach (var (metadataKey, value) in governanceMetadata)
            {
                metadataEntries[metadataKey] = StructuredValue.FromString(value);
            }
        }

        var metadata = new MetadataBag(metadataEntries);

        if (diagnosticOperationScopeFactory is null)
        {
            await EmitMutationStatsEventAsync(
                    stats,
                    new DiagnosticOperationContext
                    {
                        OperationId = $"memory-mutation-{Guid.NewGuid():N}",
                        OperationName = operationName,
                        OperationKind = "memory.mutation",
                        Producer = nameof(DefaultMemoryService),
                    },
                    metadata,
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await using var operation = diagnosticOperationScopeFactory.BeginOperation(new DiagnosticOperationStart
        {
            OperationName = operationName,
            OperationKind = "memory.mutation",
            Producer = nameof(DefaultMemoryService),
        });
        await EmitMutationStatsEventAsync(stats, operation.Context, metadata, cancellationToken).ConfigureAwait(false);
        await operation.CompleteAsync(new DiagnosticOperationCompletion
        {
            Status = result.Success ? "completed" : "degraded",
            Metadata = metadata,
        }, cancellationToken).ConfigureAwait(false);
    }

    private ValueTask EmitMutationStatsEventAsync(
        MemoryMutationStats stats,
        DiagnosticOperationContext operation,
        MetadataBag metadata,
        CancellationToken cancellationToken)
        => diagnosticEventSink!.EmitAsync(new DiagnosticEventEnvelope
        {
            EventName = stats.EventName,
            Payload = StructuredValue.FromPlainObject(new Dictionary<string, object?>
            {
                ["schemaVersion"] = stats.SchemaVersion,
                ["eventName"] = stats.EventName,
                ["operationName"] = stats.OperationName,
                ["memorySpaceId"] = stats.MemorySpaceId,
                ["memoryRecordId"] = stats.MemoryRecordId,
                ["key"] = stats.Key,
                ["capability"] = stats.Capability,
                ["success"] = stats.Success,
                ["effect"] = stats.Effect,
                ["lifecycleStatus"] = stats.LifecycleStatus,
                ["degradedReason"] = stats.DegradedReason,
                ["unsupportedCapability"] = stats.UnsupportedCapability,
                ["governanceCheckpointKind"] = stats.GovernanceCheckpointKind,
                ["riskSource"] = stats.RiskSource,
                ["approvalQueueProjection"] = stats.ApprovalQueueProjection,
                ["userDecision"] = stats.UserDecision,
                ["executionResult"] = stats.ExecutionResult,
            }),
            Operation = operation,
            Producer = nameof(DefaultMemoryService),
            Metadata = metadata,
        }, cancellationToken);

    private static string? GetMetadataValue(
        IReadOnlyDictionary<string, string>? metadata,
        string key)
        => metadata is not null && metadata.TryGetValue(key, out var value) ? value : null;

    private async Task<bool> ProviderCanMutateTargetSpacesAsync(
        IMemoryProvider provider,
        MemoryProviderCapability capability,
        RecordMemoryFeedback command,
        CancellationToken cancellationToken)
    {
        if (provider is not IMemoryProviderTargetSpaceResolver targetResolver)
        {
            return registry.Bindings.Count == 0;
        }

        var targetSpaces = await targetResolver.ResolveFeedbackTargetSpacesAsync(command, cancellationToken).ConfigureAwait(false);
        return TargetSpacesAllowProvider(provider, capability, targetSpaces);
    }

    private async Task<bool> ProviderCanMutateTargetSpacesAsync(
        IMemoryProvider provider,
        MemoryProviderCapability capability,
        RecordMemoryCitation command,
        CancellationToken cancellationToken)
    {
        if (provider is not IMemoryProviderTargetSpaceResolver targetResolver)
        {
            return registry.Bindings.Count == 0;
        }

        var targetSpaces = await targetResolver.ResolveCitationTargetSpacesAsync(command, cancellationToken).ConfigureAwait(false);
        return TargetSpacesAllowProvider(provider, capability, targetSpaces);
    }

    private bool TargetSpacesAllowProvider(
        IMemoryProvider provider,
        MemoryProviderCapability capability,
        IReadOnlyList<MemorySpaceId> targetSpaces)
    {
        if (targetSpaces.Count == 0)
        {
            return registry.Bindings.Count == 0;
        }

        return targetSpaces.All(spaceId =>
            registry.BindingAllows(provider.Descriptor.ProviderId, spaceId, capability));
    }

    public async Task<MemoryOverlay> ResolveOverlayAsync(
        ResolveMemoryOverlay query,
        HabitProfile? habitProfile,
        IReadOnlyList<FactMemoryRecord>? defaultFacts,
        MemoryOperationContext context,
        CancellationToken cancellationToken,
        MemoryOverlayResolutionProfile? profile = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);

        var memorySpaceId = query.MemorySpaceId;
        var providerFacts = new List<FactMemoryRecord>();
        var resolution = await registry.ResolveProvidersWithDiagnosticsAsync(memorySpaceId, MemoryProviderCapability.Filter, cancellationToken).ConfigureAwait(false);
        foreach (var provider in resolution.Providers)
        {
            var result = await provider.FilterAsync(
                new FilterMemory(memorySpaceId),
                context,
                cancellationToken).ConfigureAwait(false);
            providerFacts.AddRange(result.Records);
        }

        return overlayResolver.Resolve(defaultFacts ?? Array.Empty<FactMemoryRecord>(), providerFacts, habitProfile, profile);
    }

    private async Task AuditFilterAsync(
        FilterMemory query,
        MemoryOperationContext context,
        IReadOnlyList<FactMemoryRecord> records,
        CancellationToken cancellationToken)
    {
        var memorySpaceIds = query.MemorySpaceId is { } memorySpaceId
            ? new[] { memorySpaceId }
            : records
                .Select(static record => record.MemorySpaceId)
                .Distinct()
                .DefaultIfEmpty(new MemorySpaceId("memory:query:filter"))
                .ToArray();

        var auditKey = FilterAuditKey(query);
        var source = !string.IsNullOrWhiteSpace(context.CorrelationId)
            ? context.CorrelationId!
            : context.SessionId?.Value ?? "memory-filter";
        foreach (var spaceId in memorySpaceIds)
        {
            await auditSink.AppendAsync(
                MemoryAuditRecords.FromContext(
                    "filter_memory",
                    spaceId,
                    auditKey,
                    context,
                    sourceFallback: source),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static string FilterAuditKey(FilterMemory query)
        => !string.IsNullOrWhiteSpace(query.Key)
            ? query.Key
            : query.ScopeKind?.ToString() ?? query.LifecycleStatus?.ToString() ?? "memory-filter";

    private static string ImportAuditKey(ImportMemory command)
        => command.Records.Count == 1
            ? command.Records[0].Key
            : $"records:{command.Records.Count}";

    private static MemoryMutationResult MissingProviderMutation(
        MemoryProviderCapability capability,
        IReadOnlyList<string> degradedProviders)
        => degradedProviders.Count > 0
            ? new MemoryMutationResult(false, DegradedReason: "memory_provider_unreachable", UnsupportedCapability: capability, Effect: MemoryMutationEffect.Degraded)
            : new MemoryMutationResult(false, DegradedReason: "memory_provider_not_found", UnsupportedCapability: capability, Effect: MemoryMutationEffect.Degraded);

    private static IReadOnlyList<string> MissingProviderQueryDegradedProviders(
        IReadOnlyList<string> degradedProviders,
        string? operation = null)
        => degradedProviders.Count > 0
            ? degradedProviders
            : [string.IsNullOrWhiteSpace(operation) ? "memory_provider_not_found" : $"memory_provider_not_found:{operation}"];

    private static IReadOnlyList<string> MergeDegradedProviders(params IReadOnlyList<string>[] degradedProviderGroups)
        => degradedProviderGroups
            .SelectMany(static group => group)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private sealed record MemoryFilterProviderResolution(
        MemoryProviderResolutionResult Resolution,
        FilterMemory Query,
        MemorySearchMode EffectiveSearchMode,
        IReadOnlyList<string>? DegradedProviders = null)
    {
        public IReadOnlyList<string> DegradedProviders { get; } = DegradedProviders ?? Array.Empty<string>();
    }
}
