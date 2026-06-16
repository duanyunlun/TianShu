using TianShu.Contracts.Identity;
using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Primitives;

namespace TianShu.IdentityMemory;

/// <summary>
/// 默认的本机 Identity / Memory 服务实现。
/// Default local implementation for the identity and memory plane.
/// </summary>
public sealed class DefaultTianShuIdentityMemoryPlane : ITianShuIdentityMemoryPlane
{
    private readonly ITianShuLocalMemoryStore memoryStore;
    private readonly IDiagnosticEventSink? diagnosticEventSink;
    private readonly IDiagnosticOperationScopeFactory? diagnosticOperationScopeFactory;
    private readonly IReadOnlyList<TianShuExternalMemoryProviderOptions> externalMemoryProviders;
    private readonly Func<TianShuIdentityMemoryContext, TianShuMemoryRuntimeOptions> memoryOptionsResolver;
    private readonly object providerBindingGate = new();
    private readonly List<MemoryProviderBinding> providerBindings = [];
    private bool providerBindingsLoaded;

    /// <summary>
    /// 初始化默认 Identity / Memory 服务实现。
    /// Initializes the default identity-memory service implementation.
    /// </summary>
    public DefaultTianShuIdentityMemoryPlane(
        ITianShuLocalMemoryStore? memoryStore = null,
        IDiagnosticEventSink? diagnosticEventSink = null,
        IDiagnosticOperationScopeFactory? diagnosticOperationScopeFactory = null,
        IEnumerable<TianShuExternalMemoryProviderOptions>? externalMemoryProviders = null,
        Func<TianShuIdentityMemoryContext, TianShuMemoryRuntimeOptions>? memoryOptionsResolver = null)
    {
        this.memoryStore = memoryStore ?? EmptyTianShuLocalMemoryStore.Instance;
        this.diagnosticEventSink = diagnosticEventSink;
        this.diagnosticOperationScopeFactory = diagnosticOperationScopeFactory;
        this.externalMemoryProviders = (externalMemoryProviders ?? Array.Empty<TianShuExternalMemoryProviderOptions>()).ToArray();
        this.memoryOptionsResolver = memoryOptionsResolver ?? (_ => TianShuMemoryRuntimeOptions.Default);
    }

    public Task<Account?> GetAccountProfileAsync(
        GetAccountProfile query,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(query.AccountId.Value, context.AccountId.Value, StringComparison.Ordinal))
        {
            return Task.FromResult<Account?>(null);
        }

        var metadataEntries = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["runtimeName"] = StructuredValue.FromString(context.RuntimeName),
            ["deviceName"] = StructuredValue.FromString(context.DeviceName),
            ["platform"] = StructuredValue.FromString(context.Platform),
            ["source"] = StructuredValue.FromString("local-default"),
            ["identityScope"] = StructuredValue.FromString("user"),
            ["syncPolicy"] = StructuredValue.FromString("manual"),
            ["teamKey"] = StructuredValue.FromString(context.TeamKey),
        };

        if (!string.IsNullOrWhiteSpace(context.WorkingDirectory))
        {
            metadataEntries["workspacePath"] = StructuredValue.FromString(context.WorkingDirectory!);
            metadataEntries["workspaceKey"] = StructuredValue.FromString(NormalizeSegment(context.WorkingDirectory!));
        }

        if (!string.IsNullOrWhiteSpace(context.CollaborationSpaceId))
        {
            metadataEntries["collaborationSpaceId"] = StructuredValue.FromString(context.CollaborationSpaceId!);
        }

        var metadata = new MetadataBag(metadataEntries);

        return Task.FromResult<Account?>(new Account(
            context.AccountId,
            context.AccountDisplayName,
            email: null,
            metadata));
    }

    public Task<IReadOnlyList<DeviceBinding>> ListBoundDevicesAsync(
        ListBoundDevices query,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(query.AccountId.Value, context.AccountId.Value, StringComparison.Ordinal))
        {
            return Task.FromResult<IReadOnlyList<DeviceBinding>>(Array.Empty<DeviceBinding>());
        }

        IReadOnlyList<DeviceBinding> results =
        [
            new DeviceBinding(
                new DeviceId($"device:{NormalizeSegment(context.DeviceName)}"),
                context.AccountId,
                context.DeviceName,
                context.Platform,
                context.SnapshotTime),
        ];
        return Task.FromResult(results);
    }

    public Task<IReadOnlyList<MemoryProviderDescriptor>> ListMemoryProvidersAsync(
        ListMemoryProviders query,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsMemoryEnabled(ResolveMemoryRuntimeOptions(context)))
        {
            return Task.FromResult<IReadOnlyList<MemoryProviderDescriptor>>(Array.Empty<MemoryProviderDescriptor>());
        }

        return Task.FromResult<IReadOnlyList<MemoryProviderDescriptor>>(CreateMemoryService(context).ListProviders(query));
    }

    public Task<IReadOnlyList<MemorySpace>> ListMemorySpacesAsync(
        ListMemorySpaces query,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsMemoryEnabled(ResolveMemoryRuntimeOptions(context)))
        {
            return Task.FromResult<IReadOnlyList<MemorySpace>>(Array.Empty<MemorySpace>());
        }

        return CreateMemoryService(context).ListSpacesAsync(query, cancellationToken);
    }

    public async Task<MemoryOverlay> ResolveMemoryOverlayAsync(
        ResolveMemoryOverlay query,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var options = ResolveMemoryRuntimeOptions(context);
        var runtimeProfile = options.ResolveDefaultProfile();
        var habitProfile = TianShuIdentityMemoryDecisionResolver.BuildHabitProfile(context);
        if (!IsMemoryEnabled(options) || !runtimeProfile.Overlay)
        {
            return new MemoryOverlay(
                Facts: Array.Empty<FactMemoryRecord>(),
                HabitProfile: habitProfile,
                MergeDecision: MemoryMergeDecision.Ignored);
        }

        var spaces = await ListMemorySpacesAsync(new ListMemorySpaces(), context, cancellationToken).ConfigureAwait(false);
        var selectedSpaces = ResolveTargetSpaces(query, context, spaces);

        if (selectedSpaces.Count == 0 || !query.AllowInjection)
        {
            return new MemoryOverlay(
                Facts: Array.Empty<FactMemoryRecord>(),
                HabitProfile: habitProfile,
                MergeDecision: MemoryMergeDecision.Ignored);
        }

        var defaultFacts = selectedSpaces
            .SelectMany(space => BuildFacts(space, context))
            .ToArray();
        var serviceQuery = query.MemorySpaceId is null && query.CollaborationSpaceId is null
            ? query
            : query with { MemorySpaceId = selectedSpaces[0].Id };
        var profile = MemoryOverlayResolutionProfile.Create(
            selectedSpaces.Select(static space => space.Id),
            query.QueryText,
            maxFacts: query.MemorySpaceId is null && query.CollaborationSpaceId is null ? 24 : null);
        return await CreateMemoryService(context, spaces).ResolveOverlayAsync(
            serviceQuery,
            habitProfile,
            defaultFacts,
            CreateMemoryOperationContext(context),
            cancellationToken,
            profile).ConfigureAwait(false);
    }

    public Task<MemoryQueryResult> FilterMemoryAsync(
        FilterMemory query,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsMemoryEnabled(ResolveMemoryRuntimeOptions(context)))
        {
            return Task.FromResult(new MemoryQueryResult(degradedProviders: ["memory_disabled"]));
        }

        return CreateMemoryService(context).FilterAsync(query, CreateMemoryOperationContext(context), cancellationToken);
    }

    public Task<MemoryReviewQueryResult> ListMemoryReviewsAsync(
        ListMemoryReviews query,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsMemoryEnabled(ResolveMemoryRuntimeOptions(context)))
        {
            return Task.FromResult(new MemoryReviewQueryResult(degradedProviders: ["memory_disabled"]));
        }

        return CreateMemoryService(context).ListReviewsAsync(query, CreateMemoryOperationContext(context), cancellationToken);
    }

    public Task<MemoryMutationResult> AddMemoryAsync(
        AddMemory command,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsMemoryEnabled(ResolveMemoryRuntimeOptions(context)))
        {
            return Task.FromResult(MemoryDisabledMutationResult());
        }

        return CreateMemoryService(context).AddAsync(command, CreateMemoryOperationContext(context), cancellationToken);
    }

    public Task<IReadOnlyList<MemoryCandidate>> ExtractMemoryAsync(
        ExtractMemory command,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var options = ResolveMemoryRuntimeOptions(context);
        if (!IsMemoryEnabled(options) || options.ResolveDefaultProfile().Extract == TianShuMemoryExtractMode.Off)
        {
            return Task.FromResult<IReadOnlyList<MemoryCandidate>>(Array.Empty<MemoryCandidate>());
        }

        return CreateMemoryService(context).ExtractAsync(command, CreateMemoryOperationContext(context), cancellationToken);
    }

    public Task<MemoryMutationResult> ImportMemoryAsync(
        ImportMemory command,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsMemoryEnabled(ResolveMemoryRuntimeOptions(context)))
        {
            return Task.FromResult(MemoryDisabledMutationResult());
        }

        return CreateMemoryService(context).ImportAsync(command, CreateMemoryOperationContext(context), cancellationToken);
    }

    public Task<MemoryQueryResult> ExportMemoryAsync(
        ExportMemory command,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsMemoryEnabled(ResolveMemoryRuntimeOptions(context)))
        {
            return Task.FromResult(new MemoryQueryResult(degradedProviders: ["memory_disabled"]));
        }

        return CreateMemoryService(context).ExportAsync(command, CreateMemoryOperationContext(context), cancellationToken);
    }

    public Task<MemoryMutationResult> BindMemoryProviderAsync(
        BindMemoryProvider command,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsMemoryEnabled(ResolveMemoryRuntimeOptions(context)))
        {
            return Task.FromResult(MemoryDisabledMutationResult());
        }

        return CreateMemoryService(context).BindProviderAsync(command, CreateMemoryOperationContext(context), cancellationToken);
    }

    public async Task<MemoryConsolidationRunResult> RunMemoryConsolidationAsync(
        RunMemoryConsolidation command,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var runtimeOptions = ResolveMemoryRuntimeOptions(context);
        if (!IsMemoryEnabled(runtimeOptions))
        {
            return new MemoryConsolidationRunResult(0, 0, LeaseAcquired: false, SkippedByLease: true);
        }

        var worker = new MemoryConsolidationWorker(memoryStore);
        var result = await worker.RunOnceAsync(
                command.MemorySpaceId,
                CreateMemoryOperationContext(context),
                cancellationToken,
                BuildConsolidationOptions(command, runtimeOptions))
            .ConfigureAwait(false);
        var contractResult = new MemoryConsolidationRunResult(
            result.CandidatesScanned,
            result.ProposalsCreated,
            result.LeaseAcquired,
            result.SkippedByLease,
            result.CandidatesSkippedByCooldown,
            result.RetriesDeferred,
            result.FailuresRecorded);
        await EmitConsolidationStatsAsync(command, contractResult, cancellationToken).ConfigureAwait(false);
        return contractResult;
    }

    public Task<MemoryMutationResult> ForgetMemoryAsync(
        ForgetMemory command,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsMemoryEnabled(ResolveMemoryRuntimeOptions(context)))
        {
            return Task.FromResult(MemoryDisabledMutationResult());
        }

        return CreateMemoryService(context).ForgetAsync(command, CreateMemoryOperationContext(context), cancellationToken);
    }

    public Task<MemoryMutationResult> DeleteMemoryAsync(
        DeleteMemory command,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsMemoryEnabled(ResolveMemoryRuntimeOptions(context)))
        {
            return Task.FromResult(MemoryDisabledMutationResult());
        }

        return CreateMemoryService(context).DeleteAsync(command, CreateMemoryOperationContext(context), cancellationToken);
    }

    public Task<MemoryMutationResult> SupersedeMemoryAsync(
        SupersedeMemory command,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsMemoryEnabled(ResolveMemoryRuntimeOptions(context)))
        {
            return Task.FromResult(MemoryDisabledMutationResult());
        }

        return CreateMemoryService(context).SupersedeAsync(command, CreateMemoryOperationContext(context), cancellationToken);
    }

    public Task<MemoryMutationResult> ApproveMemoryReviewAsync(
        ApproveMemoryReview command,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsMemoryEnabled(ResolveMemoryRuntimeOptions(context)))
        {
            return Task.FromResult(MemoryDisabledMutationResult());
        }

        return CreateMemoryService(context).ApproveReviewAsync(command, CreateMemoryOperationContext(context), cancellationToken);
    }

    public Task<MemoryMutationResult> DemoteMemoryReviewAsync(
        DemoteMemoryReview command,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsMemoryEnabled(ResolveMemoryRuntimeOptions(context)))
        {
            return Task.FromResult(MemoryDisabledMutationResult());
        }

        return CreateMemoryService(context).DemoteReviewAsync(command, CreateMemoryOperationContext(context), cancellationToken);
    }

    public Task<MemoryMutationResult> MergeMemoryReviewAsync(
        MergeMemoryReview command,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsMemoryEnabled(ResolveMemoryRuntimeOptions(context)))
        {
            return Task.FromResult(MemoryDisabledMutationResult());
        }

        return CreateMemoryService(context).MergeReviewAsync(command, CreateMemoryOperationContext(context), cancellationToken);
    }

    public Task<MemoryMutationResult> RestoreMemoryReviewAsync(
        RestoreMemoryReview command,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsMemoryEnabled(ResolveMemoryRuntimeOptions(context)))
        {
            return Task.FromResult(MemoryDisabledMutationResult());
        }

        return CreateMemoryService(context).RestoreReviewAsync(command, CreateMemoryOperationContext(context), cancellationToken);
    }

    public Task<MemoryMutationResult> RecordMemoryFeedbackAsync(
        RecordMemoryFeedback command,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsMemoryEnabled(ResolveMemoryRuntimeOptions(context)))
        {
            return Task.FromResult(MemoryDisabledMutationResult());
        }

        return CreateMemoryService(context).RecordFeedbackAsync(command, CreateMemoryOperationContext(context), cancellationToken);
    }

    public Task<MemoryMutationResult> RecordMemoryCitationAsync(
        RecordMemoryCitation command,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsMemoryEnabled(ResolveMemoryRuntimeOptions(context)))
        {
            return Task.FromResult(MemoryDisabledMutationResult());
        }

        return CreateMemoryService(context).RecordCitationAsync(command, CreateMemoryOperationContext(context), cancellationToken);
    }

    private static List<MemorySpace> BuildMemorySpaces(
        TianShuIdentityMemoryContext context,
        TianShuMemoryRuntimeOptions? options = null)
    {
        var results = new List<MemorySpace>
        {
            new(
                new MemorySpaceId($"memory:user:{NormalizeSegment(context.AccountId.Value)}"),
                MemoryScopeKind.User,
                context.AccountId.Value,
                "User Memory"),
            new(
                new MemorySpaceId($"memory:team:{NormalizeSegment(context.TeamKey)}"),
                MemoryScopeKind.Team,
                context.TeamKey,
                "Team Memory",
                isReadOnly: true),
        };

        if (!string.IsNullOrWhiteSpace(context.WorkingDirectory))
        {
            results.Add(new MemorySpace(
                new MemorySpaceId($"memory:workspace:{NormalizeSegment(context.WorkingDirectory!)}"),
                MemoryScopeKind.Workspace,
                context.WorkingDirectory!,
                $"{Path.GetFileName(context.WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))} Workspace Memory"));
        }

        if (!string.IsNullOrWhiteSpace(context.ActiveThreadId))
        {
            results.Add(new MemorySpace(
                new MemorySpaceId($"memory:session:{NormalizeSegment(context.ActiveThreadId!)}"),
                MemoryScopeKind.Session,
                context.ActiveThreadId!,
                "Session Memory"));
            results.Add(new MemorySpace(
                new MemorySpaceId($"memory:agent:{NormalizeSegment(context.ActiveThreadId!)}"),
                MemoryScopeKind.Agent,
                context.ActiveThreadId!,
                "Agent Memory",
                isReadOnly: true));
        }

        if (!string.IsNullOrWhiteSpace(context.CollaborationSpaceId))
        {
            results.Add(new MemorySpace(
                new MemorySpaceId($"memory:collaboration:{NormalizeSegment(context.CollaborationSpaceId!)}"),
                MemoryScopeKind.Collaboration,
                context.CollaborationSpaceId!,
                "Collaboration Memory",
                isReadOnly: true));
        }

        if (options?.Spaces is { Count: > 0 } configuredSpaces)
        {
            foreach (var configuredSpace in configuredSpaces)
            {
                var space = BuildConfiguredMemorySpace(configuredSpace, context);
                var existingIndex = results.FindIndex(existing => string.Equals(existing.Id.Value, space.Id.Value, StringComparison.Ordinal));
                if (existingIndex >= 0)
                {
                    results[existingIndex] = space;
                    continue;
                }

                results.Add(space);
            }
        }

        return results;
    }

    private static MemorySpace BuildConfiguredMemorySpace(
        TianShuMemorySpaceOptions configuredSpace,
        TianShuIdentityMemoryContext context)
    {
        var scopeKey = ResolveConfiguredScopeKey(configuredSpace, context);
        var id = ResolveConfiguredMemorySpaceId(configuredSpace.SpaceKey, configuredSpace.ScopeKind, scopeKey);
        var displayName = string.IsNullOrWhiteSpace(configuredSpace.DisplayName)
            ? $"{configuredSpace.SpaceKey.Trim()} Memory"
            : configuredSpace.DisplayName.Trim();
        return new MemorySpace(
            id,
            configuredSpace.ScopeKind,
            scopeKey,
            displayName,
            configuredSpace.ReadOnly);
    }

    private static string ResolveConfiguredScopeKey(
        TianShuMemorySpaceOptions configuredSpace,
        TianShuIdentityMemoryContext context)
    {
        var explicitScopeKey = NormalizeConfiguredValue(configuredSpace.ScopeKey);
        if (explicitScopeKey is not null)
        {
            return explicitScopeKey;
        }

        return configuredSpace.ScopeKind switch
        {
            MemoryScopeKind.User => context.AccountId.Value,
            MemoryScopeKind.Workspace when !string.IsNullOrWhiteSpace(context.WorkingDirectory) => context.WorkingDirectory!,
            MemoryScopeKind.Team => context.TeamKey,
            MemoryScopeKind.Session when !string.IsNullOrWhiteSpace(context.ActiveThreadId) => context.ActiveThreadId!,
            MemoryScopeKind.Agent when !string.IsNullOrWhiteSpace(context.ActiveThreadId) => context.ActiveThreadId!,
            MemoryScopeKind.Collaboration when !string.IsNullOrWhiteSpace(context.CollaborationSpaceId) => context.CollaborationSpaceId!,
            _ => configuredSpace.SpaceKey,
        };
    }

    private static MemorySpaceId ResolveConfiguredMemorySpaceId(
        string spaceKey,
        MemoryScopeKind scopeKind,
        string scopeKey)
    {
        var normalizedSpaceKey = NormalizeConfiguredValue(spaceKey) ?? "workspace";
        if (normalizedSpaceKey.StartsWith("memory:", StringComparison.Ordinal))
        {
            return new MemorySpaceId(normalizedSpaceKey);
        }

        return new MemorySpaceId($"memory:{ScopeSegment(scopeKind)}:{NormalizeSegment(scopeKey)}");
    }

    private static IReadOnlyList<MemorySpace> ResolveTargetSpaces(
        ResolveMemoryOverlay query,
        TianShuIdentityMemoryContext context,
        IReadOnlyList<MemorySpace> spaces)
    {
        if (query.MemorySpaceId is { } memorySpaceId)
        {
            var explicitSpace = spaces.FirstOrDefault(space => string.Equals(space.Id.Value, memorySpaceId.Value, StringComparison.Ordinal));
            return explicitSpace is null ? Array.Empty<MemorySpace>() : new[] { explicitSpace };
        }

        if (query.CollaborationSpaceId is { } collaborationSpaceId)
        {
            var collaborationSpace = spaces.FirstOrDefault(
                space => space.ScopeKind == MemoryScopeKind.Collaboration
                         && string.Equals(space.ScopeKey, collaborationSpaceId.Value, StringComparison.Ordinal));
            return collaborationSpace is null ? Array.Empty<MemorySpace>() : new[] { collaborationSpace };
        }

        return spaces
            .Where(space => IsTurnAnchorSpace(space, context))
            .OrderBy(space => AnchorPriority(space.ScopeKind))
            .ThenBy(static space => space.Id.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsTurnAnchorSpace(MemorySpace space, TianShuIdentityMemoryContext context)
        => space.ScopeKind switch
        {
            MemoryScopeKind.Session or MemoryScopeKind.Agent => !string.IsNullOrWhiteSpace(context.ActiveThreadId)
                                                               && string.Equals(space.ScopeKey, context.ActiveThreadId, StringComparison.Ordinal),
            MemoryScopeKind.Workspace => !string.IsNullOrWhiteSpace(context.WorkingDirectory)
                                         && string.Equals(space.ScopeKey, context.WorkingDirectory, StringComparison.Ordinal),
            MemoryScopeKind.Collaboration => !string.IsNullOrWhiteSpace(context.CollaborationSpaceId)
                                             && string.Equals(space.ScopeKey, context.CollaborationSpaceId, StringComparison.Ordinal),
            MemoryScopeKind.Team => string.Equals(space.ScopeKey, context.TeamKey, StringComparison.Ordinal),
            MemoryScopeKind.User => string.Equals(space.ScopeKey, context.AccountId.Value, StringComparison.Ordinal),
            _ => false,
        };

    private static int AnchorPriority(MemoryScopeKind scopeKind)
    {
        return scopeKind switch
        {
            MemoryScopeKind.Session => 0,
            MemoryScopeKind.Workspace => 1,
            MemoryScopeKind.Collaboration => 2,
            MemoryScopeKind.Team => 3,
            MemoryScopeKind.User => 4,
            MemoryScopeKind.Agent => 5,
            _ => 6,
        };
    }

    private static IReadOnlyList<FactMemoryRecord> BuildFacts(MemorySpace space, TianShuIdentityMemoryContext context)
    {
        var facts = new List<FactMemoryRecord>
        {
            CreateFact("runtime.name", context.RuntimeName, space.Id, context.SnapshotTime),
            CreateFact("device.platform", context.Platform, space.Id, context.SnapshotTime),
        };

        switch (space.ScopeKind)
        {
            case MemoryScopeKind.User:
                facts.Add(CreateFact("identity.account_id", context.AccountId.Value, space.Id, context.SnapshotTime));
                facts.Add(CreateFact("identity.display_name", context.AccountDisplayName, space.Id, context.SnapshotTime));
                break;
            case MemoryScopeKind.Workspace:
                if (!string.IsNullOrWhiteSpace(context.WorkingDirectory))
                {
                    facts.Add(CreateFact("workspace.cwd", context.WorkingDirectory!, space.Id, context.SnapshotTime));
                }
                break;
            case MemoryScopeKind.Team:
                facts.Add(CreateFact("team.key", context.TeamKey, space.Id, context.SnapshotTime));
                break;
            case MemoryScopeKind.Session:
                if (!string.IsNullOrWhiteSpace(context.ActiveThreadId))
                {
                    facts.Add(CreateFact("session.thread_id", context.ActiveThreadId!, space.Id, context.SnapshotTime));
                }
                break;
            case MemoryScopeKind.Agent:
                if (!string.IsNullOrWhiteSpace(context.ActiveThreadId))
                {
                    facts.Add(CreateFact("agent.thread_id", context.ActiveThreadId!, space.Id, context.SnapshotTime));
                }
                break;
            case MemoryScopeKind.Collaboration:
                if (!string.IsNullOrWhiteSpace(context.CollaborationSpaceId))
                {
                    facts.Add(CreateFact("collaboration.space_id", context.CollaborationSpaceId!, space.Id, context.SnapshotTime));
                }
                break;
        }

        return facts;
    }

    private static FactMemoryRecord CreateFact(string key, string value, MemorySpaceId memorySpaceId, DateTimeOffset recordedAt)
        => new(
            key,
            StructuredValue.FromString(value),
            memorySpaceId,
            confidence: 1m,
            recordedAt);

    private DefaultMemoryService CreateMemoryService(
        TianShuIdentityMemoryContext context,
        IReadOnlyList<MemorySpace>? spaces = null)
    {
        EnsureProviderBindingsLoaded();

        var policy = new MemoryPolicyEngine();
        var options = ResolveMemoryRuntimeOptions(context);
        var resolvedSpaces = spaces ?? BuildMemorySpaces(context, options);
        var resolvedBindings = ResolveConfiguredProviderBindings(options, resolvedSpaces);
        var providers = new List<IMemoryProvider>
        {
            new TianShuLocalMemoryProvider(memoryStore, resolvedSpaces, policy),
        };
        providers.AddRange(externalMemoryProviders
            .Where(static providerOptions => providerOptions.Enabled && !IsLocalProviderKind(providerOptions.Kind))
            .Select(providerOptions => new TianShuExternalSemanticMemoryProvider(providerOptions, resolvedSpaces)));
        return new DefaultMemoryService(
            new MemoryProviderRegistry(
                providers,
                MergeProviderBindings(providerBindings, resolvedBindings),
                PersistProviderBindingsAsync,
                diagnosticEventSink,
                diagnosticOperationScopeFactory),
            new MemoryOverlayResolver(policy),
            auditSink: new TianShuLocalMemoryAuditSink(memoryStore),
            policy: policy,
            diagnosticEventSink: diagnosticEventSink,
            diagnosticOperationScopeFactory: diagnosticOperationScopeFactory);
    }

    private TianShuMemoryRuntimeOptions ResolveMemoryRuntimeOptions(TianShuIdentityMemoryContext context)
        => memoryOptionsResolver(context) ?? TianShuMemoryRuntimeOptions.Default;

    private static bool IsMemoryEnabled(TianShuMemoryRuntimeOptions options)
    {
        var profile = options.ResolveDefaultProfile();
        return options.Enabled && profile.Enabled;
    }

    private static MemoryMutationResult MemoryDisabledMutationResult()
        => new(false, DegradedReason: "memory_disabled", Effect: MemoryMutationEffect.Degraded);

    private static IReadOnlyList<MemoryProviderBinding> ResolveConfiguredProviderBindings(
        TianShuMemoryRuntimeOptions options,
        IReadOnlyList<MemorySpace> spaces)
    {
        if (options.Bindings.Count == 0)
        {
            return Array.Empty<MemoryProviderBinding>();
        }

        var result = new List<MemoryProviderBinding>();
        foreach (var binding in options.Bindings)
        {
            var providerId = NormalizeConfiguredValue(binding.ProviderId);
            if (providerId is null)
            {
                continue;
            }

            var space = ResolveConfiguredBindingSpace(binding.Space, spaces);
            if (space is null)
            {
                continue;
            }

            result.Add(new MemoryProviderBinding(
                NormalizeProviderId(providerId),
                space.Id,
                ParseBindingMode(binding.Mode),
                ParseCapabilities(binding.Capabilities)));
        }

        return result;
    }

    private static MemorySpace? ResolveConfiguredBindingSpace(
        string spaceReference,
        IReadOnlyList<MemorySpace> spaces)
    {
        var normalized = NormalizeConfiguredValue(spaceReference);
        if (normalized is null)
        {
            return null;
        }

        return spaces.FirstOrDefault(space => string.Equals(space.Id.Value, normalized, StringComparison.Ordinal))
               ?? spaces.FirstOrDefault(space => string.Equals(space.ScopeKey, normalized, StringComparison.Ordinal))
               ?? spaces.FirstOrDefault(space => string.Equals(ScopeSegment(space.ScopeKind), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<MemoryProviderBinding> MergeProviderBindings(
        IReadOnlyList<MemoryProviderBinding> persistedBindings,
        IReadOnlyList<MemoryProviderBinding> configuredBindings)
    {
        if (persistedBindings.Count == 0)
        {
            return configuredBindings;
        }

        if (configuredBindings.Count == 0)
        {
            return persistedBindings;
        }

        var result = new Dictionary<string, MemoryProviderBinding>(StringComparer.Ordinal);
        foreach (var binding in persistedBindings.Concat(configuredBindings))
        {
            result[$"{binding.ProviderId}\u001f{binding.MemorySpaceId.Value}"] = binding;
        }

        return result.Values.ToArray();
    }

    private static MemoryProviderBindingMode ParseBindingMode(string? mode)
        => NormalizeConfiguredValue(mode)?.ToLowerInvariant() switch
        {
            "read-write" or "readwrite" => MemoryProviderBindingMode.ReadWrite,
            "mirror" => MemoryProviderBindingMode.Mirror,
            "import-export" or "importexport" => MemoryProviderBindingMode.ImportExport,
            _ => MemoryProviderBindingMode.ReadOnly,
        };

    private static string NormalizeProviderId(string providerId)
        => string.Equals(providerId, "local", StringComparison.OrdinalIgnoreCase)
            ? TianShuLocalMemoryProvider.DefaultProviderId
            : providerId;

    private static MemoryProviderCapability ParseCapabilities(IReadOnlyList<string>? capabilities)
    {
        if (capabilities is null || capabilities.Count == 0)
        {
            return MemoryProviderCapability.ReadOnlyAccess;
        }

        var result = MemoryProviderCapability.ListSpaces;
        foreach (var capability in capabilities)
        {
            result |= NormalizeConfiguredValue(capability)?.ToLowerInvariant() switch
            {
                "list-spaces" or "listspaces" => MemoryProviderCapability.ListSpaces,
                "add" => MemoryProviderCapability.Add,
                "extract" => MemoryProviderCapability.Extract,
                "filter" => MemoryProviderCapability.Filter,
                "forget" => MemoryProviderCapability.Forget,
                "delete" => MemoryProviderCapability.Delete,
                "feedback" => MemoryProviderCapability.Feedback,
                "citation" => MemoryProviderCapability.Citation,
                "import" => MemoryProviderCapability.Import,
                "export" => MemoryProviderCapability.Export,
                "supersede" => MemoryProviderCapability.Supersede,
                "review" => MemoryProviderCapability.Review,
                "keyword-search" or "keywordsearch" => MemoryProviderCapability.KeywordSearch,
                "semantic-search" or "semanticsearch" => MemoryProviderCapability.SemanticSearch,
                "embedding-indexing" or "embeddingindexing" => MemoryProviderCapability.EmbeddingIndexing,
                "llm-extraction" or "llmextraction" => MemoryProviderCapability.LlmExtraction,
                "read-only" or "readonly" => MemoryProviderCapability.ReadOnlyAccess,
                "read-write" or "readwrite" => MemoryProviderCapability.ReadWriteAccess,
                _ => MemoryProviderCapability.None,
            };
        }

        return result == MemoryProviderCapability.ListSpaces
            ? MemoryProviderCapability.ListSpaces | MemoryProviderCapability.ReadOnlyAccess
            : result;
    }

    private void EnsureProviderBindingsLoaded()
    {
        lock (providerBindingGate)
        {
            if (providerBindingsLoaded)
            {
                return;
            }

            var bindings = memoryStore.ListProviderBindingsAsync(CancellationToken.None).GetAwaiter().GetResult();
            providerBindings.Clear();
            providerBindings.AddRange(bindings);
            providerBindingsLoaded = true;
        }
    }

    private async Task PersistProviderBindingsAsync(
        IReadOnlyList<MemoryProviderBinding> bindings,
        CancellationToken cancellationToken)
    {
        await memoryStore.ReplaceProviderBindingsAsync(bindings, cancellationToken).ConfigureAwait(false);
        lock (providerBindingGate)
        {
            providerBindings.Clear();
            providerBindings.AddRange(bindings);
            providerBindingsLoaded = true;
        }
    }

    private static bool IsLocalProviderKind(string? kind)
        => string.Equals(kind, "local", StringComparison.OrdinalIgnoreCase)
           || string.Equals(kind, "tianshu.local", StringComparison.OrdinalIgnoreCase);

    private static MemoryOperationContext CreateMemoryOperationContext(TianShuIdentityMemoryContext context)
        => new(
            actorId: context.AccountId.Value,
            timestamp: context.SnapshotTime);

    private static MemoryConsolidationOptions BuildConsolidationOptions(
        RunMemoryConsolidation command,
        TianShuMemoryRuntimeOptions runtimeOptions)
    {
        var retention = ParseRetentionMode(runtimeOptions.ResolveDefaultProfile().Retention);
        return new MemoryConsolidationOptions(
            enableLease: command.EnableLease,
            includeArchiveProposals: command.IncludeArchiveProposals || retention == TianShuMemoryRetentionMode.Archive,
            includeForgetProposals: command.IncludeForgetProposals || retention == TianShuMemoryRetentionMode.Forget,
            includeOverlayCacheRebuildProposals: command.IncludeOverlayCacheRebuildProposals,
            emitOverlayCacheSnapshot: command.EmitOverlayCacheSnapshot,
            recordFailureDiagnostics: command.RecordFailureDiagnostics,
            archiveUnusedFactsOlderThan: command.ArchiveUnusedFactsOlderThanSeconds is { } archiveSeconds
                ? TimeSpan.FromSeconds(Math.Max(0, archiveSeconds))
                : null,
            archiveUnusedFactsWithUsageCountAtMost: command.ArchiveUnusedFactsWithUsageCountAtMost ?? 0,
            leaseDuration: command.LeaseDurationSeconds is { } leaseSeconds
                ? TimeSpan.FromSeconds(Math.Max(0, leaseSeconds))
                : null,
            cooldownWindow: command.CooldownWindowSeconds is { } cooldownSeconds
                ? TimeSpan.FromSeconds(Math.Max(0, cooldownSeconds))
                : null,
            maxRetryAttempts: command.MaxRetryAttempts ?? 3,
            retryDelay: command.RetryDelaySeconds is { } retrySeconds
                ? TimeSpan.FromSeconds(Math.Max(0, retrySeconds))
                : null);
    }

    private static TianShuMemoryRetentionMode ParseRetentionMode(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "archive" => TianShuMemoryRetentionMode.Archive,
            "forget" => TianShuMemoryRetentionMode.Forget,
            _ => TianShuMemoryRetentionMode.Keep,
        };

    private enum TianShuMemoryRetentionMode
    {
        Keep,
        Archive,
        Forget,
    }

    private async ValueTask EmitConsolidationStatsAsync(
        RunMemoryConsolidation command,
        MemoryConsolidationRunResult result,
        CancellationToken cancellationToken)
    {
        if (diagnosticEventSink is null)
        {
            return;
        }

        var metadata = new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["diagnosticModule"] = StructuredValue.FromString(DiagnosticModuleNames.Memory),
            ["status"] = StructuredValue.FromString(result.SkippedByLease ? "skipped" : "completed"),
            ["summary"] = StructuredValue.FromString("memory consolidation run"),
            ["degraded"] = StructuredValue.FromString("false"),
            ["permissionBoundary"] = StructuredValue.FromString(result.PermissionBoundary),
        });
        var operationStart = new DiagnosticOperationStart
        {
            OperationName = "memory_consolidation",
            OperationKind = "memory.consolidation",
            Producer = nameof(DefaultTianShuIdentityMemoryPlane),
            Metadata = metadata,
        };

        if (diagnosticOperationScopeFactory is null)
        {
            await EmitConsolidationStatsEventAsync(
                    command,
                    result,
                    new DiagnosticOperationContext
                    {
                        OperationId = $"memory-consolidation-{Guid.NewGuid():N}",
                        OperationName = operationStart.OperationName,
                        OperationKind = operationStart.OperationKind,
                        Producer = operationStart.Producer,
                        Metadata = metadata,
                    },
                    metadata,
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await using var operation = diagnosticOperationScopeFactory.BeginOperation(operationStart);
        await EmitConsolidationStatsEventAsync(command, result, operation.Context, metadata, cancellationToken).ConfigureAwait(false);
        await operation.CompleteAsync(new DiagnosticOperationCompletion
        {
            Status = result.SkippedByLease ? "skipped" : "completed",
            Metadata = metadata,
        }, cancellationToken).ConfigureAwait(false);
    }

    private ValueTask EmitConsolidationStatsEventAsync(
        RunMemoryConsolidation command,
        MemoryConsolidationRunResult result,
        DiagnosticOperationContext operation,
        MetadataBag metadata,
        CancellationToken cancellationToken)
        => diagnosticEventSink!.EmitAsync(new DiagnosticEventEnvelope
        {
            EventName = "memory/consolidation/stats",
            Payload = StructuredValue.FromPlainObject(new Dictionary<string, object?>
            {
                ["schemaVersion"] = 1,
                ["eventName"] = "memory/consolidation/stats",
                ["memorySpaceId"] = command.MemorySpaceId?.Value,
                ["candidatesScanned"] = result.CandidatesScanned,
                ["proposalsCreated"] = result.ProposalsCreated,
                ["leaseAcquired"] = result.LeaseAcquired,
                ["skippedByLease"] = result.SkippedByLease,
                ["candidatesSkippedByCooldown"] = result.CandidatesSkippedByCooldown,
                ["retriesDeferred"] = result.RetriesDeferred,
                ["failuresRecorded"] = result.FailuresRecorded,
                ["permissionBoundary"] = result.PermissionBoundary,
            }),
            Operation = operation,
            Producer = nameof(DefaultTianShuIdentityMemoryPlane),
            Metadata = metadata,
        }, cancellationToken);

    private static string NormalizeSegment(string value)
    {
        var normalized = value
            .Trim()
            .Replace('\\', '/')
            .Replace(' ', '-')
            .ToLowerInvariant();

        if (normalized.Length >= 3
            && char.IsLetter(normalized[0])
            && normalized[1] == ':'
            && normalized[2] == '/')
        {
            normalized = normalized[0] + normalized[2..];
        }

        return normalized.Replace(':', '_');
    }

    private static string? NormalizeConfiguredValue(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string ScopeSegment(MemoryScopeKind scopeKind)
        => scopeKind switch
        {
            MemoryScopeKind.User => "user",
            MemoryScopeKind.Workspace => "workspace",
            MemoryScopeKind.Team => "team",
            MemoryScopeKind.Session => "session",
            MemoryScopeKind.Agent => "agent",
            MemoryScopeKind.Collaboration => "collaboration",
            _ => "workspace",
        };
}
