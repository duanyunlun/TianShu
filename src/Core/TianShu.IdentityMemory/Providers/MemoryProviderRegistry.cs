using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Primitives;

namespace TianShu.IdentityMemory;

/// <summary>
/// 记忆 provider 注册表。
/// Memory-provider registry.
/// </summary>
public sealed class MemoryProviderRegistry
{
    private readonly object bindingGate = new();
    private readonly List<IMemoryProvider> providers;
    private readonly IList<MemoryProviderBinding> bindings;
    private readonly Func<IReadOnlyList<MemoryProviderBinding>, CancellationToken, Task>? persistBindingsAsync;
    private readonly IDiagnosticEventSink? diagnosticEventSink;
    private readonly IDiagnosticOperationScopeFactory? diagnosticOperationScopeFactory;

    public MemoryProviderRegistry(
        IEnumerable<IMemoryProvider> providers,
        IEnumerable<MemoryProviderBinding>? bindings = null,
        Func<IReadOnlyList<MemoryProviderBinding>, CancellationToken, Task>? persistBindingsAsync = null,
        IDiagnosticEventSink? diagnosticEventSink = null,
        IDiagnosticOperationScopeFactory? diagnosticOperationScopeFactory = null)
    {
        this.providers = (providers ?? throw new ArgumentNullException(nameof(providers))).ToList();
        this.bindings = bindings is List<MemoryProviderBinding> mutableBindings
            ? mutableBindings
            : (bindings ?? Array.Empty<MemoryProviderBinding>()).ToList();
        this.persistBindingsAsync = persistBindingsAsync;
        this.diagnosticEventSink = diagnosticEventSink;
        this.diagnosticOperationScopeFactory = diagnosticOperationScopeFactory;
    }

    public IReadOnlyList<IMemoryProvider> Providers => providers;

    public IReadOnlyList<MemoryProviderBinding> Bindings
    {
        get
        {
            lock (bindingGate)
            {
                return bindings.ToArray();
            }
        }
    }

    public IReadOnlyList<MemoryProviderDescriptor> ListProviders()
        => ListProviders(new ListMemoryProviders());

    public IReadOnlyList<MemoryProviderDescriptor> ListProviders(ListMemoryProviders query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return providers
            .Select(static provider => provider.Descriptor)
            .Where(descriptor => query.ScopeKind is not { } scopeKind || descriptor.SupportedScopes.Contains(scopeKind))
            .ToArray();
    }

    public async Task<IMemoryProvider?> ResolveProviderAsync(
        MemorySpaceId memorySpaceId,
        MemoryProviderCapability capability,
        CancellationToken cancellationToken)
        => (await ResolveProvidersAsync(memorySpaceId, capability, cancellationToken).ConfigureAwait(false)).FirstOrDefault();

    public async Task<MemoryMutationResult> BindProviderAsync(
        BindMemoryProvider command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (command.AllowedCapabilities == MemoryProviderCapability.None)
        {
            return new MemoryMutationResult(false, DegradedReason: "memory_provider_capability_required", Effect: MemoryMutationEffect.Degraded);
        }

        var allowedByMode = CapabilitiesAllowedByMode(command.Mode);
        var unsupportedByMode = command.AllowedCapabilities & ~allowedByMode;
        if (unsupportedByMode != MemoryProviderCapability.None)
        {
            return new MemoryMutationResult(
                false,
                DegradedReason: "memory_provider_binding_mode_capability_mismatch",
                UnsupportedCapability: unsupportedByMode,
                Effect: MemoryMutationEffect.Degraded);
        }

        var provider = providers.FirstOrDefault(provider =>
            string.Equals(provider.Descriptor.ProviderId, command.ProviderId, StringComparison.Ordinal));
        if (provider is null)
        {
            return new MemoryMutationResult(false, DegradedReason: "memory_provider_not_found", Effect: MemoryMutationEffect.Degraded);
        }

        if ((provider.Descriptor.Capabilities & command.AllowedCapabilities) != command.AllowedCapabilities)
        {
            var unsupported = command.AllowedCapabilities & ~provider.Descriptor.Capabilities;
            return new MemoryMutationResult(
                false,
                DegradedReason: "memory_provider_capability_not_supported",
                UnsupportedCapability: unsupported,
                Effect: MemoryMutationEffect.Degraded);
        }

        IReadOnlyList<MemorySpace> spaces;
        try
        {
            spaces = await provider.ListSpacesAsync(command.MemorySpaceId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new MemoryMutationResult(false, DegradedReason: "memory_provider_unreachable", Effect: MemoryMutationEffect.Degraded);
        }

        if (spaces.Count == 0)
        {
            return new MemoryMutationResult(false, DegradedReason: "memory_space_not_found", Effect: MemoryMutationEffect.Degraded);
        }

        IReadOnlyList<MemoryProviderBinding> bindingSnapshot;
        lock (bindingGate)
        {
            for (var i = bindings.Count - 1; i >= 0; i--)
            {
                var binding = bindings[i];
                if (string.Equals(binding.ProviderId, command.ProviderId, StringComparison.Ordinal)
                    && string.Equals(binding.MemorySpaceId.Value, command.MemorySpaceId.Value, StringComparison.Ordinal))
                {
                    bindings.RemoveAt(i);
                }
            }

            bindings.Add(new MemoryProviderBinding(
                command.ProviderId,
                command.MemorySpaceId,
                command.Mode,
                command.AllowedCapabilities));
            bindingSnapshot = bindings.ToArray();
        }

        if (persistBindingsAsync is not null)
        {
            await persistBindingsAsync(bindingSnapshot, cancellationToken).ConfigureAwait(false);
        }

        return new MemoryMutationResult(true);
    }

    public async Task<IReadOnlyList<IMemoryProvider>> ResolveProvidersAsync(
        MemorySpaceId? memorySpaceId,
        MemoryProviderCapability capability,
        CancellationToken cancellationToken)
        => (await ResolveProvidersWithDiagnosticsAsync(memorySpaceId, capability, cancellationToken).ConfigureAwait(false)).Providers;

    public async Task<MemoryProviderResolutionResult> ResolveProvidersWithDiagnosticsAsync(
        MemorySpaceId? memorySpaceId,
        MemoryProviderCapability capability,
        CancellationToken cancellationToken)
    {
        var results = new List<IMemoryProvider>();
        var degradedProviders = new List<string>();
        foreach (var provider in providers)
        {
            if ((provider.Descriptor.Capabilities & capability) != capability)
            {
                continue;
            }

            if (!BindingAllows(provider.Descriptor.ProviderId, memorySpaceId, capability))
            {
                continue;
            }

            IReadOnlyList<MemorySpace> spaces;
            try
            {
                spaces = await provider.ListSpacesAsync(memorySpaceId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                degradedProviders.Add($"memory_provider_unreachable:{provider.Descriptor.ProviderId}");
                continue;
            }

            if (spaces.Count > 0)
            {
                results.Add(provider);
            }
        }

        var result = new MemoryProviderResolutionResult(results, degradedProviders);
        await EmitResolutionStatsAsync(memorySpaceId, capability, result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task EmitResolutionStatsAsync(
        MemorySpaceId? memorySpaceId,
        MemoryProviderCapability capability,
        MemoryProviderResolutionResult result,
        CancellationToken cancellationToken)
    {
        if (diagnosticEventSink is null)
        {
            return;
        }

        var stats = new MemoryProviderResolutionStats
        {
            MemorySpaceId = memorySpaceId?.Value,
            Capability = capability.ToString(),
            MatchedProviderCount = result.Providers.Count,
            DegradedProviders = result.DegradedProviders,
        };
        var operationStart = new DiagnosticOperationStart
        {
            OperationName = "memory_provider_resolution",
            OperationKind = "memory.provider_resolution",
            Producer = nameof(MemoryProviderRegistry),
        };
        var metadata = new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["diagnosticModule"] = StructuredValue.FromString(DiagnosticModuleNames.Memory),
            ["status"] = StructuredValue.FromString(result.DegradedProviders.Count == 0 ? "info" : "degraded"),
            ["summary"] = StructuredValue.FromString("memory provider resolution"),
            ["degraded"] = StructuredValue.FromString(result.DegradedProviders.Count == 0 ? "false" : "true"),
        });

        if (diagnosticOperationScopeFactory is null)
        {
            await EmitResolutionStatsEventAsync(
                    stats,
                    new DiagnosticOperationContext
                    {
                        OperationId = $"memory-provider-resolution-{Guid.NewGuid():N}",
                        OperationName = operationStart.OperationName,
                        OperationKind = operationStart.OperationKind,
                        Producer = operationStart.Producer,
                    },
                    metadata,
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await using var operation = diagnosticOperationScopeFactory.BeginOperation(operationStart);
        await EmitResolutionStatsEventAsync(stats, operation.Context, metadata, cancellationToken).ConfigureAwait(false);
        await operation.CompleteAsync(new DiagnosticOperationCompletion
        {
            Status = result.DegradedProviders.Count == 0 ? "completed" : "degraded",
            Metadata = metadata,
        }, cancellationToken).ConfigureAwait(false);
    }

    private ValueTask EmitResolutionStatsEventAsync(
        MemoryProviderResolutionStats stats,
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
                ["memorySpaceId"] = stats.MemorySpaceId,
                ["capability"] = stats.Capability,
                ["matchedProviderCount"] = stats.MatchedProviderCount,
                ["degradedProviders"] = stats.DegradedProviders,
            }),
            Operation = operation,
            Producer = nameof(MemoryProviderRegistry),
            Metadata = metadata,
        }, cancellationToken);

    /// <summary>
    /// 判断指定 provider 是否允许在目标空间使用某项能力。
    /// Determines whether the provider may use a capability for the target memory space.
    /// </summary>
    public bool BindingAllows(
        string providerId,
        MemorySpaceId? memorySpaceId,
        MemoryProviderCapability capability)
    {
        lock (bindingGate)
        {
            if (bindings.Count == 0)
            {
                return true;
            }

            return bindings.Any(binding =>
                string.Equals(binding.ProviderId, providerId, StringComparison.Ordinal)
                && (memorySpaceId is null || string.Equals(binding.MemorySpaceId.Value, memorySpaceId.Value, StringComparison.Ordinal))
                && (CapabilitiesAllowedByMode(binding.Mode) & capability) == capability
                && (binding.AllowedCapabilities & capability) == capability);
        }
    }

    private static MemoryProviderCapability CapabilitiesAllowedByMode(MemoryProviderBindingMode mode)
        => mode switch
        {
            MemoryProviderBindingMode.ReadOnly => MemoryProviderCapability.ListSpaces
                | MemoryProviderCapability.Filter
                | MemoryProviderCapability.KeywordSearch
                | MemoryProviderCapability.SemanticSearch
                | MemoryProviderCapability.ReadOnlyAccess,
            MemoryProviderBindingMode.ReadWrite => MemoryProviderCapability.ListSpaces
                | MemoryProviderCapability.Add
                | MemoryProviderCapability.Extract
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
                | MemoryProviderCapability.Extract
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
}

/// <summary>
/// 记忆 provider 解析结果，携带可用 provider 与不可达 provider 的降级信息。
/// Memory-provider resolution result carrying usable providers and degraded unreachable-provider diagnostics.
/// </summary>
public sealed record MemoryProviderResolutionResult(
    IReadOnlyList<IMemoryProvider> Providers,
    IReadOnlyList<string> DegradedProviders);
