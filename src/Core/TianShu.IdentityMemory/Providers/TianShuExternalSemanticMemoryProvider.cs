using System.Net.Sockets;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Primitives;

namespace TianShu.IdentityMemory;

/// <summary>
/// 外部语义记忆 provider 的只读适配器。
/// Read-only adapter for external semantic memory providers.
/// </summary>
public sealed class TianShuExternalSemanticMemoryProvider : IMemoryProvider
{
    private static readonly MemoryScopeKind[] SupportedScopes =
    [
        MemoryScopeKind.User,
        MemoryScopeKind.Workspace,
        MemoryScopeKind.Team,
        MemoryScopeKind.Session,
        MemoryScopeKind.Agent,
        MemoryScopeKind.Collaboration
    ];

    private readonly TianShuExternalMemoryProviderOptions options;
    private readonly IReadOnlyList<MemorySpace> spaces;
    private readonly Dictionary<string, string> externalRecordIdsBySanitizedId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> sanitizedRecordIdsByExternalId = new(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public TianShuExternalSemanticMemoryProvider(
        TianShuExternalMemoryProviderOptions options,
        IEnumerable<MemorySpace> spaces)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.spaces = (spaces ?? throw new ArgumentNullException(nameof(spaces))).ToArray();

        Descriptor = new MemoryProviderDescriptor(
            NormalizeProviderId(options.ProviderId),
            string.IsNullOrWhiteSpace(options.DisplayName) ? BuildDisplayName(options) : options.DisplayName!,
            "1.0",
            BuildCapabilities(options),
            SupportedScopes,
            RequiresNetwork: true,
            RequiresCredentials: HasSecretReference(options),
            TrustLevel: MemoryProviderTrustLevel.External,
            SupportedLifecycleStatuses: [MemoryLifecycleStatus.Active],
            DegradationStrategy: MemoryProviderDegradationStrategy.DegradedRead,
            Features: MemoryProviderFeature.UsageTelemetry | MemoryProviderFeature.SecretRedaction);
    }

    public MemoryProviderDescriptor Descriptor { get; }

    public async Task<IReadOnlyList<MemorySpace>> ListSpacesAsync(
        MemorySpaceId? memorySpaceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var discovery = await TryReadDiscoveryAsync(cancellationToken).ConfigureAwait(false);
        var discoveredSpaces = discovery?.Spaces is { Count: > 0 }
            ? discovery.Spaces.Select(ToMemorySpace).Where(static space => space is not null).Cast<MemorySpace>().ToArray()
            : Array.Empty<MemorySpace>();
        var resolvedSpaces = discoveredSpaces.Length > 0 ? discoveredSpaces : spaces;

        await ProbeHttpConnectivityAsync(cancellationToken).ConfigureAwait(false);
        return resolvedSpaces
            .Where(space => memorySpaceId is null || string.Equals(space.Id.Value, memorySpaceId.Value.Value, StringComparison.Ordinal))
            .Select(static space => new MemorySpace(space.Id, space.ScopeKind, space.ScopeKey, space.DisplayName, isReadOnly: true))
            .ToArray();
    }

    public Task<MemoryMutationResult> AddAsync(AddMemory command, MemoryOperationContext context, CancellationToken cancellationToken)
        => ExecuteMutationAsync(
            MemoryProviderCapability.Add,
            "v1/memory/add",
            new ExternalMemoryOperationRequest<AddMemory>(command, context),
            cancellationToken);

    public Task<MemoryMutationResult> ImportAsync(ImportMemory command, MemoryOperationContext context, CancellationToken cancellationToken)
        => ExecuteMutationAsync(
            MemoryProviderCapability.Import,
            "v1/memory/import",
            new ExternalMemoryOperationRequest<ImportMemory>(command, context),
            cancellationToken);

    public async Task<MemoryQueryResult> ExportAsync(ExportMemory command, MemoryOperationContext context, CancellationToken cancellationToken)
    {
        if (!Supports(MemoryProviderCapability.Export, out var unsupported))
        {
            return new MemoryQueryResult(degradedProviders: [$"{Descriptor.ProviderId}:{unsupported}"]);
        }

        var result = await PostAsync<ExternalMemoryOperationRequest<ExportMemory>, MemoryQueryResult>(
            "v1/memory/export",
            new ExternalMemoryOperationRequest<ExportMemory>(command, context),
            cancellationToken).ConfigureAwait(false);
        return result.Value is null
            ? new MemoryQueryResult(degradedProviders: [$"{Descriptor.ProviderId}:{result.DegradedReason}"])
            : SanitizeQueryResult(result.Value, command.MemorySpaceId);
    }

    public async Task<MemoryQueryResult> FilterAsync(
        FilterMemory query,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        if (!Supports(MemoryProviderCapability.Filter, out var unsupported))
        {
            return new MemoryQueryResult(degradedProviders: [$"{Descriptor.ProviderId}:{unsupported}"]);
        }

        if (query.SearchMode == MemorySearchMode.Semantic
            && !Descriptor.Capabilities.HasFlag(MemoryProviderCapability.SemanticSearch))
        {
            return new MemoryQueryResult(
                degradedProviders: [$"{Descriptor.ProviderId}:unsupported_query_mode:semantic"],
                EffectiveSearchMode: query.SearchMode);
        }

        var result = await PostAsync<ExternalMemoryOperationRequest<FilterMemory>, MemoryQueryResult>(
            "v1/memory/filter",
            new ExternalMemoryOperationRequest<FilterMemory>(query, context),
            cancellationToken).ConfigureAwait(false);
        return result.Value is null
            ? new MemoryQueryResult(
                degradedProviders: [$"{Descriptor.ProviderId}:{result.DegradedReason}"],
                EffectiveSearchMode: query.SearchMode)
            : SanitizeQueryResult(result.Value, query.MemorySpaceId);
    }

    public Task<MemoryMutationResult> ForgetAsync(ForgetMemory command, MemoryOperationContext context, CancellationToken cancellationToken)
        => ExecuteMutationAsync(
            MemoryProviderCapability.Forget,
            "v1/memory/forget",
            new ExternalMemoryOperationRequest<ForgetMemory>(Rewrite(command), context),
            cancellationToken);

    public Task<MemoryMutationResult> DeleteAsync(DeleteMemory command, MemoryOperationContext context, CancellationToken cancellationToken)
        => ExecuteMutationAsync(
            MemoryProviderCapability.Delete,
            "v1/memory/delete",
            new ExternalMemoryOperationRequest<DeleteMemory>(Rewrite(command), context),
            cancellationToken);

    public Task<MemoryMutationResult> SupersedeAsync(SupersedeMemory command, MemoryOperationContext context, CancellationToken cancellationToken)
        => ExecuteMutationAsync(
            MemoryProviderCapability.Supersede,
            "v1/memory/supersede",
            new ExternalMemoryOperationRequest<SupersedeMemory>(Rewrite(command), context),
            cancellationToken);

    public Task<MemoryMutationResult> ApproveReviewAsync(ApproveMemoryReview command, MemoryOperationContext context, CancellationToken cancellationToken)
        => ExecuteMutationAsync(
            MemoryProviderCapability.Review,
            "v1/memory/review/approve",
            new ExternalMemoryOperationRequest<ApproveMemoryReview>(Rewrite(command), context),
            cancellationToken);

    public async Task<MemoryReviewQueryResult> ListReviewsAsync(
        ListMemoryReviews query,
        MemoryOperationContext context,
        CancellationToken cancellationToken)
    {
        if (!Supports(MemoryProviderCapability.Review, out var unsupported))
        {
            return new MemoryReviewQueryResult(degradedProviders: [$"{Descriptor.ProviderId}:{unsupported}"]);
        }

        var result = await PostAsync<ExternalMemoryOperationRequest<ListMemoryReviews>, MemoryReviewQueryResult>(
            "v1/memory/review/list",
            new ExternalMemoryOperationRequest<ListMemoryReviews>(query, context),
            cancellationToken).ConfigureAwait(false);
        return result.Value is null
            ? new MemoryReviewQueryResult(degradedProviders: [$"{Descriptor.ProviderId}:{result.DegradedReason}"])
            : SanitizeReviewResult(result.Value, query.MemorySpaceId);
    }

    public Task<MemoryMutationResult> DemoteReviewAsync(DemoteMemoryReview command, MemoryOperationContext context, CancellationToken cancellationToken)
        => ExecuteMutationAsync(
            MemoryProviderCapability.Review,
            "v1/memory/review/demote",
            new ExternalMemoryOperationRequest<DemoteMemoryReview>(Rewrite(command), context),
            cancellationToken);

    public Task<MemoryMutationResult> MergeReviewAsync(MergeMemoryReview command, MemoryOperationContext context, CancellationToken cancellationToken)
        => ExecuteMutationAsync(
            MemoryProviderCapability.Review | MemoryProviderCapability.Supersede,
            "v1/memory/review/merge",
            new ExternalMemoryOperationRequest<MergeMemoryReview>(Rewrite(command), context),
            cancellationToken);

    public Task<MemoryMutationResult> RestoreReviewAsync(RestoreMemoryReview command, MemoryOperationContext context, CancellationToken cancellationToken)
        => ExecuteMutationAsync(
            MemoryProviderCapability.Review,
            "v1/memory/review/restore",
            new ExternalMemoryOperationRequest<RestoreMemoryReview>(Rewrite(command), context),
            cancellationToken);

    public Task<MemoryMutationResult> RecordFeedbackAsync(RecordMemoryFeedback command, MemoryOperationContext context, CancellationToken cancellationToken)
        => ExecuteMutationAsync(
            MemoryProviderCapability.Feedback,
            "v1/memory/feedback",
            new ExternalMemoryOperationRequest<RecordMemoryFeedback>(Rewrite(command), context),
            cancellationToken);

    public Task<MemoryMutationResult> RecordCitationAsync(RecordMemoryCitation command, MemoryOperationContext context, CancellationToken cancellationToken)
        => ExecuteMutationAsync(
            MemoryProviderCapability.Citation,
            "v1/memory/citation",
            new ExternalMemoryOperationRequest<RecordMemoryCitation>(Rewrite(command), context),
            cancellationToken);

    private async Task ProbeHttpConnectivityAsync(CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            throw new InvalidOperationException("external memory provider is disabled");
        }

        if (string.IsNullOrWhiteSpace(options.Host))
        {
            throw new InvalidOperationException("external memory provider host is required");
        }

        var endpointPort = options.Port;
        if (endpointPort is null or <= 0)
        {
            throw new InvalidOperationException("external memory provider http port is required");
        }

        using var client = new TcpClient();
        var connectTask = client.ConnectAsync(options.Host, endpointPort.Value, cancellationToken).AsTask();
        var timeoutTask = Task.Delay(options.ConnectTimeout, cancellationToken);
        var completed = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);
        if (completed != connectTask)
        {
            throw new TimeoutException($"external memory provider `{Descriptor.ProviderId}` connection timed out");
        }

        await connectTask.ConfigureAwait(false);
    }

    private async Task<MemoryMutationResult> ExecuteMutationAsync<TCommand>(
        MemoryProviderCapability capability,
        string path,
        ExternalMemoryOperationRequest<TCommand> payload,
        CancellationToken cancellationToken)
    {
        if (!Supports(capability, out var unsupported))
        {
            return UnsupportedMutation(capability, unsupported);
        }

        var result = await PostAsync<ExternalMemoryOperationRequest<TCommand>, MemoryMutationResult>(
            path,
            payload,
            cancellationToken).ConfigureAwait(false);
        if (result.Value is null)
        {
            return new MemoryMutationResult(
                false,
                DegradedReason: result.DegradedReason,
                UnsupportedCapability: capability,
                Effect: MemoryMutationEffect.Degraded);
        }

        return result.Value;
    }

    private bool Supports(MemoryProviderCapability capability, out string degradedReason)
    {
        if ((Descriptor.Capabilities & capability) == capability)
        {
            degradedReason = string.Empty;
            return true;
        }

        degradedReason = Descriptor.Capabilities.HasFlag(MemoryProviderCapability.ReadOnlyAccess)
            && !Descriptor.Capabilities.HasFlag(MemoryProviderCapability.ReadWriteAccess)
            && IsMutationCapability(capability)
                ? "external_memory_provider_read_only"
                : "unsupported_capability";
        return false;
    }

    private static bool IsMutationCapability(MemoryProviderCapability capability)
        => (capability & (MemoryProviderCapability.Add
                          | MemoryProviderCapability.Forget
                          | MemoryProviderCapability.Delete
                          | MemoryProviderCapability.Feedback
                          | MemoryProviderCapability.Citation
                          | MemoryProviderCapability.Supersede
                          | MemoryProviderCapability.Review
                          | MemoryProviderCapability.Import)) != MemoryProviderCapability.None;

    private static MemoryMutationResult UnsupportedMutation(MemoryProviderCapability capability, string reason)
        => new(false, DegradedReason: reason, UnsupportedCapability: capability, Effect: MemoryMutationEffect.Degraded);

    private async Task<ExternalProviderCallResult<TResponse>> PostAsync<TPayload, TResponse>(
        string path,
        TPayload payload,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Host) || options.Port is null or <= 0)
        {
            return new ExternalProviderCallResult<TResponse>(default, "external_memory_provider_http_endpoint_required");
        }

        try
        {
            using var client = new HttpClient
            {
                Timeout = options.ConnectTimeout,
            };
            var uri = new UriBuilder("http", options.Host, options.Port.Value, path).Uri;
            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = JsonContent.Create(payload, options: JsonOptions),
            };
            ApplyAuthenticationHeaders(request);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new ExternalProviderCallResult<TResponse>(
                    default,
                    $"external_memory_provider_http_{(int)response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var value = DeserializeResponse<TResponse>(content);
            return value is null
                ? new ExternalProviderCallResult<TResponse>(default, "external_memory_provider_empty_response")
                : new ExternalProviderCallResult<TResponse>(value, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ExternalProviderCallResult<TResponse>(default, "external_memory_provider_timeout");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException or SocketException or InvalidOperationException)
        {
            return new ExternalProviderCallResult<TResponse>(default, "external_memory_provider_call_failed");
        }
    }

    private async Task<ExternalMemoryProviderDiscoveryDto?> TryReadDiscoveryAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Host) || options.Port is null or <= 0)
        {
            return null;
        }

        try
        {
            using var client = new HttpClient
            {
                Timeout = options.ConnectTimeout,
            };
            var uri = new UriBuilder("http", options.Host, options.Port.Value, "v1/memory/provider").Uri;
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            ApplyAuthenticationHeaders(request);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var discovery = JsonSerializer.Deserialize<ExternalMemoryProviderDiscoveryDto>(content, JsonOptions);
            if (discovery is null || !discovery.HasDiscoveryShape)
            {
                return null;
            }

            if (!IsSupportedDiscoverySchema(discovery.SchemaVersion))
            {
                throw new InvalidOperationException("external memory provider schema version mismatch");
            }

            if (IsUnhealthy(discovery.Health))
            {
                throw new InvalidOperationException("external memory provider is unhealthy");
            }

            return discovery;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException or SocketException or TimeoutException
                                          || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private void ApplyAuthenticationHeaders(HttpRequestMessage request)
    {
        var authorization = ReadEnvironmentSecret(options.AuthorizationEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(authorization))
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
            return;
        }

        var apiKey = ReadEnvironmentSecret(options.ApiKeyEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    private static TResponse? DeserializeResponse<TResponse>(string content)
    {
        if (typeof(TResponse) == typeof(MemoryQueryResult))
        {
            var dto = JsonSerializer.Deserialize<ExternalMemoryQueryResultDto>(content, JsonOptions);
            var value = new MemoryQueryResult(
                dto?.Records?.Select(static record => record.ToFact()).ToArray(),
                dto?.TotalCount ?? -1,
                dto?.DegradedProviders,
                dto?.Citation,
                dto?.Explanations,
                dto?.EffectiveSearchMode ?? MemorySearchMode.Structured);
            return (TResponse)(object)value;
        }

        if (typeof(TResponse) == typeof(MemoryReviewQueryResult))
        {
            var dto = JsonSerializer.Deserialize<ExternalMemoryReviewQueryResultDto>(content, JsonOptions);
            var value = new MemoryReviewQueryResult(
                dto?.Items?.Select(static item => item.ToReviewItem()).ToArray(),
                dto?.TotalCount ?? -1,
                dto?.DegradedProviders);
            return (TResponse)(object)value;
        }

        return JsonSerializer.Deserialize<TResponse>(content, JsonOptions);
    }

    private MemoryQueryResult SanitizeQueryResult(MemoryQueryResult result, MemorySpaceId? fallbackSpaceId)
    {
        var records = result.Records.Select(record => SanitizeFact(record, fallbackSpaceId)).ToArray();
        return new MemoryQueryResult(
            records,
            result.TotalCount,
            result.DegradedProviders,
            SanitizeCitation(result.Citation, fallbackSpaceId),
            SanitizeExplanations(result.Explanations, fallbackSpaceId),
            result.EffectiveSearchMode);
    }

    private MemoryReviewQueryResult SanitizeReviewResult(MemoryReviewQueryResult result, MemorySpaceId? fallbackSpaceId)
        => new(
            result.Items
                .Select(item =>
                {
                    var record = SanitizeFact(item.Record, fallbackSpaceId);
                    var candidate = item.Candidate is null
                        ? null
                        : new MemoryCandidate(
                            item.Candidate.Key,
                            item.Candidate.Value,
                            ResolveSafeSpaceId(item.Candidate.MemorySpaceId, fallbackSpaceId),
                            item.Candidate.Confidence,
                            SanitizeSource(item.Candidate.Source),
                            item.Candidate.ExtractionReason,
                            item.Candidate.RuleId,
                            item.Candidate.FormationPath,
                            item.Candidate.ValidationEvidence,
                            item.Candidate.ContextSignature,
                            item.Candidate.IsCounterexample);
                    return new MemoryReviewItem(
                        record,
                        candidate,
                        Array.Empty<MemoryEvidenceRecord>(),
                        Array.Empty<MemorySupersedeLink>(),
                        item.Audit);
                })
                .ToArray(),
            result.TotalCount,
            result.DegradedProviders);

    private FactMemoryRecord SanitizeFact(FactMemoryRecord fact, MemorySpaceId? fallbackSpaceId)
    {
        var sanitizedId = SanitizeRecordId(fact.Id);
        var safeSpaceId = ResolveSafeSpaceId(fact.MemorySpaceId, fallbackSpaceId);
        return new FactMemoryRecord(
            fact.Key,
            fact.Value,
            safeSpaceId,
            fact.Confidence,
            fact.RecordedAt,
            sanitizedId,
            fact.LifecycleStatus,
            fact.Sources.Select(SanitizeSource).Where(static source => source is not null).Cast<MemorySourceRef>().ToArray(),
            fact.Tags,
            fact.UsageCount,
            fact.LastUsedAt,
            fact.CreatedAt,
            fact.UpdatedAt,
            fact.FormationPath,
            fact.ContextSignature,
            fact.ValidationEvidence,
            fact.IsCounterexample);
    }

    private MemoryRecordId SanitizeRecordId(MemoryRecordId recordId)
    {
        var externalId = string.IsNullOrWhiteSpace(recordId.Value)
            ? $"missing-external-id:{Guid.NewGuid():N}"
            : recordId.Value;

        lock (externalRecordIdsBySanitizedId)
        {
            if (sanitizedRecordIdsByExternalId.TryGetValue(externalId, out var existing))
            {
                return new MemoryRecordId(existing);
            }

            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{Descriptor.ProviderId}:{externalId}")))[..24].ToLowerInvariant();
            var sanitized = $"memory-record:external:{Descriptor.ProviderId}:{hash}";
            sanitizedRecordIdsByExternalId[externalId] = sanitized;
            externalRecordIdsBySanitizedId[sanitized] = externalId;
            return new MemoryRecordId(sanitized);
        }
    }

    private MemoryRecordId RewriteRecordId(MemoryRecordId recordId)
    {
        lock (externalRecordIdsBySanitizedId)
        {
            return externalRecordIdsBySanitizedId.TryGetValue(recordId.Value, out var externalId)
                ? new MemoryRecordId(externalId)
                : recordId;
        }
    }

    private MemorySpaceId ResolveSafeSpaceId(MemorySpaceId candidate, MemorySpaceId? fallbackSpaceId)
    {
        if (spaces.Any(space => string.Equals(space.Id.Value, candidate.Value, StringComparison.Ordinal)))
        {
            return candidate;
        }

        if (fallbackSpaceId is { } fallback)
        {
            return fallback;
        }

        return spaces.FirstOrDefault()?.Id ?? new MemorySpaceId($"memory:external:{Descriptor.ProviderId}");
    }

    private MemorySourceRef? SanitizeSource(MemorySourceRef? source)
    {
        if (source is null)
        {
            return null;
        }

        var metadata = source.Metadata
            .Where(static pair => !IsExternalPrivateMetadataKey(pair.Key))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        return new MemorySourceRef(
            source.SourceKind == MemorySourceKind.ExternalProvider ? MemorySourceKind.ExternalProvider : source.SourceKind,
            source.SourceKind == MemorySourceKind.ExternalProvider ? Descriptor.ProviderId : source.SourceId,
            source.Role,
            source.Path,
            source.Url,
            source.Snippet,
            source.CapturedAt,
            metadata);
    }

    private MemoryCitation? SanitizeCitation(MemoryCitation? citation, MemorySpaceId? fallbackSpaceId)
    {
        if (citation is null || citation.Entries.Count == 0)
        {
            return null;
        }

        var entries = citation.Entries
            .Select(entry => new MemoryCitationEntry(
                SanitizeRecordId(entry.MemoryRecordId),
                ResolveSafeSpaceId(entry.MemorySpaceId, fallbackSpaceId),
                entry.Key,
                SanitizeSource(entry.Source),
                SanitizeFreeText(entry.Note)))
            .ToArray();
        return entries.Length == 0 ? null : new MemoryCitation(entries);
    }

    private IReadOnlyList<MemoryOverlayExplanation> SanitizeExplanations(
        IReadOnlyList<MemoryOverlayExplanation> explanations,
        MemorySpaceId? fallbackSpaceId)
    {
        if (explanations.Count == 0)
        {
            return Array.Empty<MemoryOverlayExplanation>();
        }

        return explanations
            .Select(explanation => new MemoryOverlayExplanation(
                SanitizeRecordId(explanation.MemoryRecordId),
                ResolveSafeSpaceId(explanation.MemorySpaceId, fallbackSpaceId),
                explanation.Key,
                explanation.Rank,
                explanation.Score,
                SanitizeExplanationFactors(explanation.Factors),
                SanitizeFreeText(explanation.RetrievalMode)))
            .ToArray();
    }

    private static IReadOnlyList<string> SanitizeExplanationFactors(IReadOnlyList<string> factors)
        => factors
            .Select(SanitizeFreeText)
            .Where(static factor => !string.IsNullOrWhiteSpace(factor))
            .Cast<string>()
            .Where(static factor => !IsExternalPrivateMetadataKey(factor))
            .Take(16)
            .ToArray();

    private static string? SanitizeFreeText(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || IsExternalPrivateMetadataKey(normalized!))
        {
            return null;
        }

        return normalized!.Length <= 256 ? normalized : normalized[..256];
    }

    private static bool IsExternalPrivateMetadataKey(string key)
        => key.Contains("external", StringComparison.OrdinalIgnoreCase)
           || key.Contains("namespace", StringComparison.OrdinalIgnoreCase)
           || key.Contains("embedding", StringComparison.OrdinalIgnoreCase)
           || key.Contains("database", StringComparison.OrdinalIgnoreCase)
           || key.Contains("query", StringComparison.OrdinalIgnoreCase)
           || key.Contains("dsl", StringComparison.OrdinalIgnoreCase);

    private static bool HasSecretReference(TianShuExternalMemoryProviderOptions options)
        => !string.IsNullOrWhiteSpace(options.ApiKeyEnvironmentVariable)
           || !string.IsNullOrWhiteSpace(options.AuthorizationEnvironmentVariable);

    private static string? ReadEnvironmentSecret(string? environmentVariable)
        => string.IsNullOrWhiteSpace(environmentVariable)
            ? null
            : Environment.GetEnvironmentVariable(environmentVariable.Trim());

    private static bool IsSupportedDiscoverySchema(string? schemaVersion)
    {
        if (string.IsNullOrWhiteSpace(schemaVersion))
        {
            return true;
        }

        return schemaVersion.Trim().StartsWith("1.", StringComparison.Ordinal);
    }

    private static bool IsUnhealthy(string? health)
        => string.Equals(health, "unhealthy", StringComparison.OrdinalIgnoreCase)
           || string.Equals(health, "failed", StringComparison.OrdinalIgnoreCase)
           || string.Equals(health, "offline", StringComparison.OrdinalIgnoreCase);

    private static MemorySpace? ToMemorySpace(ExternalMemorySpaceDto space)
    {
        var id = space.Id?.Trim();
        var scopeKey = space.ScopeKey?.Trim();
        var displayName = space.DisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(scopeKey) || string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        return new MemorySpace(
            new MemorySpaceId(id!),
            ParseScopeKind(space.Scope),
            scopeKey!,
            displayName!,
            isReadOnly: space.IsReadOnly ?? true);
    }

    private static MemoryScopeKind ParseScopeKind(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "user" => MemoryScopeKind.User,
            "team" => MemoryScopeKind.Team,
            "session" => MemoryScopeKind.Session,
            "agent" => MemoryScopeKind.Agent,
            "collaboration" => MemoryScopeKind.Collaboration,
            _ => MemoryScopeKind.Workspace,
        };

    private static MemoryRecordId? ParseMemoryRecordId(JsonElement element)
    {
        var value = TryReadIdentifierValue(element);
        return string.IsNullOrWhiteSpace(value) ? null : new MemoryRecordId(value);
    }

    private static MemorySpaceId? ParseMemorySpaceId(JsonElement element)
    {
        var value = TryReadIdentifierValue(element);
        return string.IsNullOrWhiteSpace(value) ? null : new MemorySpaceId(value);
    }

    private static string? TryReadIdentifierValue(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }

        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("value", out var valueProperty)
            && valueProperty.ValueKind == JsonValueKind.String)
        {
            return valueProperty.GetString();
        }

        return null;
    }

    private ForgetMemory Rewrite(ForgetMemory command)
        => command.MemoryRecordId is null ? command : command with { MemoryRecordId = RewriteRecordId(command.MemoryRecordId.Value) };

    private DeleteMemory Rewrite(DeleteMemory command)
        => command.MemoryRecordId is null ? command : command with { MemoryRecordId = RewriteRecordId(command.MemoryRecordId.Value) };

    private SupersedeMemory Rewrite(SupersedeMemory command)
        => command with { OldRecordId = RewriteRecordId(command.OldRecordId) };

    private ApproveMemoryReview Rewrite(ApproveMemoryReview command)
        => command.MemoryRecordId is null ? command : command with { MemoryRecordId = RewriteRecordId(command.MemoryRecordId.Value) };

    private DemoteMemoryReview Rewrite(DemoteMemoryReview command)
        => command.MemoryRecordId is null ? command : command with { MemoryRecordId = RewriteRecordId(command.MemoryRecordId.Value) };

    private MergeMemoryReview Rewrite(MergeMemoryReview command)
        => command with
        {
            ReviewRecordId = RewriteRecordId(command.ReviewRecordId),
            TargetRecordId = RewriteRecordId(command.TargetRecordId),
        };

    private RestoreMemoryReview Rewrite(RestoreMemoryReview command)
        => command.MemoryRecordId is null ? command : command with { MemoryRecordId = RewriteRecordId(command.MemoryRecordId.Value) };

    private RecordMemoryFeedback Rewrite(RecordMemoryFeedback command)
        => command with { MemoryRecordId = RewriteRecordId(command.MemoryRecordId) };

    private RecordMemoryCitation Rewrite(RecordMemoryCitation command)
        => new(new MemoryCitation(
            command.Citation.Entries
                .Select(entry => new MemoryCitationEntry(
                    RewriteRecordId(entry.MemoryRecordId),
                    entry.MemorySpaceId,
                    entry.Key,
                    entry.Source,
                    entry.Note))
                .ToArray()));

    private static string NormalizeProviderId(string providerId)
        => string.IsNullOrWhiteSpace(providerId)
            ? "tianshu.external.semantic"
            : providerId.Trim();

    private static string BuildDisplayName(TianShuExternalMemoryProviderOptions options)
        => string.IsNullOrWhiteSpace(options.Kind)
            ? "External Semantic Memory"
            : $"External {options.Kind.Trim()} Memory";

    private static MemoryProviderCapability BuildCapabilities(TianShuExternalMemoryProviderOptions options)
    {
        var configuredCapabilities = options.Capabilities;
        var capabilities = MemoryProviderCapability.ListSpaces | AccessCapabilitiesForMode(options.Mode);
        if (options.Mode != MemoryProviderBindingMode.ImportExport)
        {
            capabilities |= MemoryProviderCapability.Filter;
        }

        if (configuredCapabilities.Count == 0)
        {
            var defaults = options.Mode == MemoryProviderBindingMode.ReadOnly
                ? MemoryProviderCapability.SemanticSearch
                  | MemoryProviderCapability.EmbeddingIndexing
                  | MemoryProviderCapability.LlmExtraction
                : CapabilitiesAllowedByMode(options.Mode);
            return capabilities
                | defaults
                | AccessCapabilitiesForMode(options.Mode);
        }

        foreach (var configuredCapability in configuredCapabilities)
        {
            capabilities |= ParseCapability(configuredCapability);
        }

        return (capabilities & CapabilitiesAllowedByMode(options.Mode))
               | MemoryProviderCapability.ListSpaces
               | AccessCapabilitiesForMode(options.Mode);
    }

    private static MemoryProviderCapability AccessCapabilitiesForMode(MemoryProviderBindingMode mode)
        => mode switch
        {
            MemoryProviderBindingMode.ReadOnly => MemoryProviderCapability.ReadOnlyAccess,
            MemoryProviderBindingMode.ReadWrite or MemoryProviderBindingMode.Mirror => MemoryProviderCapability.ReadOnlyAccess | MemoryProviderCapability.ReadWriteAccess,
            MemoryProviderBindingMode.ImportExport => MemoryProviderCapability.ReadOnlyAccess,
            _ => MemoryProviderCapability.ReadOnlyAccess,
        };

    private static MemoryProviderCapability CapabilitiesAllowedByMode(MemoryProviderBindingMode mode)
        => mode switch
        {
            MemoryProviderBindingMode.ReadOnly => MemoryProviderCapability.ListSpaces
                | MemoryProviderCapability.Filter
                | MemoryProviderCapability.KeywordSearch
                | MemoryProviderCapability.SemanticSearch
                | MemoryProviderCapability.EmbeddingIndexing
                | MemoryProviderCapability.LlmExtraction
                | MemoryProviderCapability.ReadOnlyAccess,
            MemoryProviderBindingMode.ReadWrite => MemoryProviderCapability.ListSpaces
                | MemoryProviderCapability.Add
                | MemoryProviderCapability.Filter
                | MemoryProviderCapability.Forget
                | MemoryProviderCapability.Delete
                | MemoryProviderCapability.Feedback
                | MemoryProviderCapability.Citation
                | MemoryProviderCapability.Supersede
                | MemoryProviderCapability.Review
                | MemoryProviderCapability.KeywordSearch
                | MemoryProviderCapability.SemanticSearch
                | MemoryProviderCapability.EmbeddingIndexing
                | MemoryProviderCapability.LlmExtraction
                | MemoryProviderCapability.ReadOnlyAccess
                | MemoryProviderCapability.ReadWriteAccess,
            MemoryProviderBindingMode.Mirror => MemoryProviderCapability.ListSpaces
                | MemoryProviderCapability.Add
                | MemoryProviderCapability.Filter
                | MemoryProviderCapability.Forget
                | MemoryProviderCapability.Delete
                | MemoryProviderCapability.Feedback
                | MemoryProviderCapability.Citation
                | MemoryProviderCapability.Supersede
                | MemoryProviderCapability.Review
                | MemoryProviderCapability.Import
                | MemoryProviderCapability.Export
                | MemoryProviderCapability.KeywordSearch
                | MemoryProviderCapability.SemanticSearch
                | MemoryProviderCapability.EmbeddingIndexing
                | MemoryProviderCapability.LlmExtraction
                | MemoryProviderCapability.ReadOnlyAccess
                | MemoryProviderCapability.ReadWriteAccess,
            MemoryProviderBindingMode.ImportExport => MemoryProviderCapability.ListSpaces
                | MemoryProviderCapability.Import
                | MemoryProviderCapability.Export
                | MemoryProviderCapability.ReadOnlyAccess,
            _ => MemoryProviderCapability.None,
        };

    private static MemoryProviderCapability ParseCapability(string configuredCapability)
        => configuredCapability.Trim().ToLowerInvariant() switch
        {
            "keyword-search" or "keyword" => MemoryProviderCapability.KeywordSearch,
            "semantic-search" or "semantic" => MemoryProviderCapability.SemanticSearch,
            "embedding-indexing" or "embedding" or "vector" => MemoryProviderCapability.EmbeddingIndexing,
            "llm-extraction" or "llm" => MemoryProviderCapability.LlmExtraction,
            "read-only" or "readonly" => MemoryProviderCapability.ReadOnlyAccess,
            "read-write" or "readwrite" => MemoryProviderCapability.ReadWriteAccess,
            "add" => MemoryProviderCapability.Add,
            "filter" => MemoryProviderCapability.Filter,
            "forget" => MemoryProviderCapability.Forget,
            "delete" => MemoryProviderCapability.Delete,
            "feedback" => MemoryProviderCapability.Feedback,
            "citation" => MemoryProviderCapability.Citation,
            "import" => MemoryProviderCapability.Import,
            "export" => MemoryProviderCapability.Export,
            "supersede" => MemoryProviderCapability.Supersede,
            "review" => MemoryProviderCapability.Review,
            _ => MemoryProviderCapability.None,
        };

    private sealed record ExternalMemoryOperationRequest<TCommand>(
        TCommand Command,
        MemoryOperationContext Context);

    private sealed record ExternalProviderCallResult<TResponse>(
        TResponse? Value,
        string? DegradedReason);

    private sealed class ExternalMemoryProviderDiscoveryDto
    {
        public string? SchemaVersion { get; set; }

        public string? Health { get; set; }

        public IReadOnlyList<string>? Capabilities { get; set; }

        public IReadOnlyList<ExternalMemorySpaceDto>? Spaces { get; set; }

        public IReadOnlyList<ExternalMemoryCollectionDto>? Collections { get; set; }

        public bool HasDiscoveryShape
            => !string.IsNullOrWhiteSpace(SchemaVersion)
               || !string.IsNullOrWhiteSpace(Health)
               || Spaces is { Count: > 0 }
               || Capabilities is { Count: > 0 }
               || Collections is { Count: > 0 };
    }

    private sealed class ExternalMemorySpaceDto
    {
        public string? Id { get; set; }

        public string? Scope { get; set; }

        public string? ScopeKey { get; set; }

        public string? DisplayName { get; set; }

        public bool? IsReadOnly { get; set; }
    }

    private sealed class ExternalMemoryCollectionDto
    {
        public string? Name { get; set; }

        public string? DisplayName { get; set; }
    }

    private sealed class ExternalMemoryQueryResultDto
    {
        public IReadOnlyList<ExternalFactMemoryRecordDto>? Records { get; set; }

        public int? TotalCount { get; set; }

        public IReadOnlyList<string>? DegradedProviders { get; set; }

        public MemoryCitation? Citation { get; set; }

        public IReadOnlyList<MemoryOverlayExplanation>? Explanations { get; set; }

        public MemorySearchMode? EffectiveSearchMode { get; set; }
    }

    private sealed class ExternalMemoryReviewQueryResultDto
    {
        public IReadOnlyList<ExternalMemoryReviewItemDto>? Items { get; set; }

        public int? TotalCount { get; set; }

        public IReadOnlyList<string>? DegradedProviders { get; set; }
    }

    private sealed class ExternalMemoryReviewItemDto
    {
        public ExternalFactMemoryRecordDto? Record { get; set; }

        public ExternalMemoryCandidateDto? Candidate { get; set; }

        public IReadOnlyList<MemoryReviewAuditSummary>? Audit { get; set; }

        public MemoryReviewItem ToReviewItem()
            => new(
                Record?.ToFact()
                    ?? new FactMemoryRecord("external.review", StructuredValue.Null, new MemorySpaceId("memory:external:unknown")),
                Candidate?.ToCandidate(),
                Evidence: Array.Empty<MemoryEvidenceRecord>(),
                SupersedeLinks: Array.Empty<MemorySupersedeLink>(),
                Audit);
    }

    private sealed class ExternalMemoryCandidateDto
    {
        public string? Key { get; set; }

        public StructuredValue? Value { get; set; }

        public JsonElement MemorySpaceId { get; set; }

        public decimal Confidence { get; set; } = 1m;

        public MemorySourceRef? Source { get; set; }

        public string? ExtractionReason { get; set; }

        public string? RuleId { get; set; }

        public MemoryFormationPath FormationPath { get; set; } = MemoryFormationPath.Unknown;

        public IReadOnlyList<MemoryValidationEvidence>? ValidationEvidence { get; set; }

        public MemoryContextSignature? ContextSignature { get; set; }

        public bool IsCounterexample { get; set; }

        public MemoryCandidate ToCandidate()
            => new(
                Key ?? "external.review.candidate",
                Value ?? StructuredValue.Null,
                ParseMemorySpaceId(MemorySpaceId) ?? new MemorySpaceId("memory:external:unknown"),
                Confidence,
                Source,
                ExtractionReason,
                RuleId,
                FormationPath,
                ValidationEvidence,
                ContextSignature,
                IsCounterexample);
    }

    private sealed class ExternalFactMemoryRecordDto
    {
        public JsonElement Id { get; set; }

        public string? Key { get; set; }

        public StructuredValue? Value { get; set; }

        public JsonElement MemorySpaceId { get; set; }

        public decimal Confidence { get; set; } = 1m;

        public DateTimeOffset? RecordedAt { get; set; }

        public MemoryLifecycleStatus LifecycleStatus { get; set; } = MemoryLifecycleStatus.Active;

        public IReadOnlyList<MemorySourceRef>? Sources { get; set; }

        public IReadOnlyList<string>? Tags { get; set; }

        public long UsageCount { get; set; }

        public DateTimeOffset? LastUsedAt { get; set; }

        public DateTimeOffset? CreatedAt { get; set; }

        public DateTimeOffset? UpdatedAt { get; set; }

        public MemoryFormationPath FormationPath { get; set; } = MemoryFormationPath.Unknown;

        public MemoryContextSignature? ContextSignature { get; set; }

        public IReadOnlyList<MemoryValidationEvidence>? ValidationEvidence { get; set; }

        public bool IsCounterexample { get; set; }

        public FactMemoryRecord ToFact()
            => new(
                Key ?? "external.memory",
                Value ?? StructuredValue.Null,
                ParseMemorySpaceId(MemorySpaceId) ?? new MemorySpaceId("memory:external:unknown"),
                Confidence,
                RecordedAt,
                ParseMemoryRecordId(Id),
                LifecycleStatus,
                Sources,
                Tags,
                UsageCount,
                LastUsedAt,
                CreatedAt,
                UpdatedAt,
                FormationPath,
                ContextSignature,
                ValidationEvidence,
                IsCounterexample);

    }
}
