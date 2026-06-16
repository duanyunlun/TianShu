using TianShu.Contracts.Catalog;
using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Primitives;
using TianShu.Configuration;

namespace TianShu.Execution.Runtime;

internal static class ExecutionProviderCatalogResolver
{
    private const string DefaultProviderId = "openai";
    private const string DefaultBaseUrl = "https://api.openai.com/v1";
    private const string DefaultTransportFamily = "responses";
    private const string DefaultApiKeyEnvironmentVariable = "OPENAI_API_KEY";
    private const string EngineId = "kernel";

    public static CapabilityCatalogSnapshot BuildCapabilityCatalog(
        StructuredValue? config,
        ControlPlaneModelCatalogResult modelCatalog,
        bool includeHiddenModels,
        ResolvedToolCatalogSnapshot? tools = null)
    {
        ArgumentNullException.ThrowIfNull(modelCatalog);

        var activeProviderKey = Normalize(ReadStructuredString(config, "provider")) ?? DefaultProviderId;
        var activeModel = Normalize(ReadStructuredString(config, "model"));
        var activeModelRouteSetId = ResolveActiveModelRouteSetId(config);
        var modelProfiles = BuildModelProfiles(modelCatalog.Items, includeHiddenModels);
        var providers = BuildProviderProfiles(config, activeProviderKey, modelProfiles);
        var modelRoutes = BuildModelRouteSet(config, activeModelRouteSetId);

        return new CapabilityCatalogSnapshot(
            activeProviderKey,
            activeModel,
            providers,
            tools,
            modelRoutes);
    }

    public static ResolvedEngineBinding ResolveEngineBinding(
        StructuredValue? config,
        ControlPlaneModelCatalogResult modelCatalog,
        ResolveEngineBinding request)
    {
        ArgumentNullException.ThrowIfNull(modelCatalog);
        ArgumentNullException.ThrowIfNull(request);

        var providerCatalog = BuildCapabilityCatalog(config, modelCatalog, includeHiddenModels: true);
        if (providerCatalog.Providers.Count == 0)
        {
            return new ResolvedEngineBinding(null);
        }

        var requestedProviderKey = Normalize(request.PreferredProviderKey) ?? providerCatalog.ActiveProviderKey ?? DefaultProviderId;
        var selectedProvider = ResolveProviderProfile(providerCatalog.Providers, requestedProviderKey) ?? providerCatalog.Providers[0];
        var requestedModel = Normalize(request.PreferredModelKey) ?? providerCatalog.ActiveModel;
        var selectedModel = ResolveModelProfile(selectedProvider.Models, requestedModel)
                            ?? selectedProvider.Models.FirstOrDefault(static item => item.IsDefault)
                            ?? selectedProvider.Models.FirstOrDefault();
        if (selectedModel is null)
        {
            return new ResolvedEngineBinding(null);
        }

        var preferWebsocketTransport = request.PreferWebsocketTransport || selectedModel.PreferWebsocketTransport;
        var useWebsocketTransport = preferWebsocketTransport && selectedProvider.SupportsWebsockets;
        var transportMode = BuildTransportMode(selectedProvider.TransportFamily, useWebsocketTransport);
        var candidates = BuildBindingCandidates(providerCatalog.Providers, selectedProvider, selectedModel);
        var binding = new EngineBinding(
            engineKey: EngineId,
            providerKey: selectedProvider.Key,
            modelKey: selectedModel.Key,
            model: selectedModel.Model,
            transportFamily: selectedProvider.TransportFamily,
            streaming: new CatalogStreamingPreference(
                transportMode,
                preferWebsocketTransport: preferWebsocketTransport,
                useWebsocketTransport: useWebsocketTransport),
            baseUrl: selectedProvider.BaseUrl,
            apiKeyEnvironmentVariable: selectedProvider.ApiKeyEnvironmentVariable,
            supportsWebsockets: selectedProvider.SupportsWebsockets,
            reasoning: new CatalogReasoningProfile(
                ResolveReasoningEffort(request, config, selectedModel),
                ResolveReasoningSummary(request, config, selectedModel),
                ResolveVerbosity(request, config, selectedModel)),
            fallbackPlan: candidates.Where(static item => !item.IsSelected).ToArray());

        return new ResolvedEngineBinding(binding, candidates);
    }

    private static IReadOnlyList<ProviderProfile> BuildProviderProfiles(
        StructuredValue? config,
        string activeProviderKey,
        IReadOnlyList<ModelProfile> modelProfiles)
    {
        var orderedProviderKeys = CollectProviderIds(config, activeProviderKey);
        if (orderedProviderKeys.Count == 0)
        {
            orderedProviderKeys.Add(DefaultProviderId);
        }

        return orderedProviderKeys
            .Select(providerKey => BuildProviderProfile(config, providerKey, modelProfiles))
            .ToArray();
    }

    private static ProviderProfile BuildProviderProfile(
        StructuredValue? config,
        string providerKey,
        IReadOnlyList<ModelProfile> modelProfiles)
    {
        var providerConfig = TryGetProviderConfig(config, providerKey);
        var transportFamily = Normalize(ReadStructuredString(providerConfig, "default_protocol")) ?? DefaultTransportFamily;
        var supportsWebsockets = ReadStructuredBool(providerConfig, "supports_websockets") ?? false;

        return new ProviderProfile(
            key: providerKey,
            displayName: Normalize(ReadStructuredString(providerConfig, "name")) ?? BuildProviderDisplayName(providerKey),
            transportFamily: transportFamily,
            transportModes: BuildTransportModes(transportFamily, supportsWebsockets),
            supportedCapabilities: BuildProviderCapabilities(modelProfiles, transportFamily, supportsWebsockets),
            models: modelProfiles,
            baseUrl: Normalize(ReadStructuredString(providerConfig, "base_url")) ?? DefaultBaseUrl,
            apiKeyEnvironmentVariable: Normalize(ReadStructuredString(providerConfig, "api_key_env")) ?? DefaultApiKeyEnvironmentVariable,
            supportsWebsockets: supportsWebsockets);
    }

    private static IReadOnlyList<ModelProfile> BuildModelProfiles(
        IReadOnlyList<ControlPlaneModelCatalogItem> items,
        bool includeHiddenModels)
    {
        if (items.Count == 0)
        {
            return Array.Empty<ModelProfile>();
        }

        return items
            .Where(item => includeHiddenModels || !item.Hidden)
            .Select(BuildModelProfile)
            .ToArray();
    }

    private static ModelProfile BuildModelProfile(ControlPlaneModelCatalogItem item)
    {
        var inputModalities = item.InputModalities.Count == 0
            ? ["text"]
            : item.InputModalities.ToArray();
        return new ModelProfile(
            key: item.Id,
            model: item.Model,
            displayName: item.DisplayName,
            description: item.Description,
            hidden: item.Hidden,
            isDefault: item.IsDefault,
            defaultReasoningEffort: item.DefaultReasoningEffort,
            supportedReasoningEfforts: item.SupportedReasoningEfforts.ToArray(),
            inputModalities: inputModalities,
            supportedCapabilities: BuildModelCapabilities(item, inputModalities),
            supportsPersonality: item.SupportsPersonality,
            supportsParallelToolCalls: item.SupportsParallelToolCalls,
            supportsReasoningSummaries: item.SupportsReasoningSummaries,
            defaultReasoningSummary: item.DefaultReasoningSummary,
            supportsVerbosity: item.SupportsVerbosity,
            defaultVerbosity: item.DefaultVerbosity,
            preferWebsocketTransport: item.PreferWebsocketTransport);
    }

    private static CapabilityModelRouteSet? BuildModelRouteSet(
        StructuredValue? config,
        string routeSetId)
    {
        if (TryReadModelRouteSet(config, routeSetId, out var configuredRouteSet))
        {
            return configuredRouteSet;
        }

        return null;
    }

    private static bool TryReadModelRouteSet(
        StructuredValue? config,
        string routeSetId,
        out CapabilityModelRouteSet routeSet)
    {
        routeSet = null!;
        var routeSetConfig = TryReadModelRouteSetConfig(config, routeSetId);
        if (routeSetConfig is null)
        {
            return false;
        }

        var routesValue = ReadStructuredValue(routeSetConfig, "routes");
        if (routesValue?.Kind != StructuredValueKind.Array)
        {
            return false;
        }

        var routes = new List<CapabilityModelRoute>();
        foreach (var routeValue in routesValue.Items)
        {
            if (routeValue.Kind != StructuredValueKind.Object)
            {
                continue;
            }

            var kind = Normalize(ReadStructuredString(routeValue, "kind"));
            var candidates = ReadModelRouteCandidates(routeValue);
            if (kind is null || candidates.Count == 0)
            {
                continue;
            }

            routes.Add(new CapabilityModelRoute(
                kind,
                candidates,
                fallbackRouteKind: Normalize(ReadStructuredString(routeValue, "fallback")),
                stageId: ResolveStageIdForRouteKind(kind)));
        }

        if (routes.Count == 0)
        {
            return false;
        }

        routeSet = new CapabilityModelRouteSet(
            routeSetId,
            routes,
            displayName: Normalize(ReadStructuredString(routeSetConfig, "display_name")) ?? routeSetId,
            description: Normalize(ReadStructuredString(routeSetConfig, "description")),
            isVirtual: false);
        return true;
    }

    private static IReadOnlyList<CapabilityModelRouteCandidate> ReadModelRouteCandidates(StructuredValue routeValue)
    {
        var candidatesValue = ReadStructuredValue(routeValue, "candidates");
        if (candidatesValue?.Kind != StructuredValueKind.Array)
        {
            return Array.Empty<CapabilityModelRouteCandidate>();
        }

        var candidates = new List<CapabilityModelRouteCandidate>();
        foreach (var candidateValue in candidatesValue.Items)
        {
            if (candidateValue.Kind != StructuredValueKind.Object)
            {
                continue;
            }

            var provider = Normalize(ReadStructuredString(candidateValue, "provider"));
            var model = Normalize(ReadStructuredString(candidateValue, "model"));
            if (provider is null || model is null)
            {
                continue;
            }

            candidates.Add(new CapabilityModelRouteCandidate(
                provider,
                model,
                candidateIndex: candidates.Count,
                protocol: Normalize(ReadStructuredString(candidateValue, "protocol")),
                capabilities: ReadStructuredStringArray(candidateValue, "capabilities"),
                unavailableReason: Normalize(ReadStructuredString(candidateValue, "unavailable_reason"))));
        }

        return candidates;
    }

    private static StructuredValue? TryReadModelRouteSetConfig(StructuredValue? config, string routeSetId)
    {
        var routeSets = ReadStructuredValue(config, "model_route_sets");
        if (routeSets?.Kind != StructuredValueKind.Object)
        {
            return null;
        }

        foreach (var pair in routeSets.Properties)
        {
            if (string.Equals(pair.Key, routeSetId, StringComparison.OrdinalIgnoreCase)
                && pair.Value.Kind == StructuredValueKind.Object)
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static string? ResolveStageIdForRouteKind(string routeKind)
        => BuiltInStageDefinitions.All.FirstOrDefault(stage =>
            string.Equals(stage.ModelRouteKind.Value, routeKind, StringComparison.OrdinalIgnoreCase))?.Id;

    private static IReadOnlyList<CapabilityDescriptor> BuildProviderCapabilities(
        IReadOnlyList<ModelProfile> modelProfiles,
        string transportFamily,
        bool supportsWebsockets)
    {
        var supportsImageInput = modelProfiles.Any(
            static model => model.InputModalities.Contains("image", StringComparer.OrdinalIgnoreCase));
        var supportsPersonality = modelProfiles.Any(static model => model.SupportsPersonality);
        var supportsParallelToolCalls = modelProfiles.Any(static model => model.SupportsParallelToolCalls);
        var supportsReasoningSummaries = modelProfiles.Any(static model => model.SupportsReasoningSummaries);
        var supportsVerbosity = modelProfiles.Any(static model => model.SupportsVerbosity);

        return
        [
            CreateCapability(
                "conversation_generation",
                supported: true,
                description: "Provider 可承载标准文本生成。"),
            CreateCapability(
                "streaming_transport",
                supported: true,
                values: BuildTransportModes(transportFamily, supportsWebsockets),
                defaultValue: BuildTransportMode(transportFamily, useWebsocketTransport: false),
                description: "Provider 对执行引擎暴露的 transport 模式。"),
            CreateCapability(
                "image_input",
                supportsImageInput,
                description: "至少存在一个模型支持图片输入。"),
            CreateCapability(
                "personality",
                supportsPersonality,
                description: "至少存在一个模型支持 personality。"),
            CreateCapability(
                "parallel_tool_calls",
                supportsParallelToolCalls,
                description: "至少存在一个模型支持并行工具调用。"),
            CreateCapability(
                "reasoning_summaries",
                supportsReasoningSummaries,
                description: "至少存在一个模型支持 reasoning summary。"),
            CreateCapability(
                "verbosity",
                supportsVerbosity,
                description: "至少存在一个模型支持 verbosity。"),
            CreateCapability(
                "websocket_transport",
                supportsWebsockets,
                description: "Provider 是否允许 websocket streaming。"),
        ];
    }

    private static IReadOnlyList<CapabilityDescriptor> BuildModelCapabilities(
        ControlPlaneModelCatalogItem item,
        IReadOnlyList<string> inputModalities)
    {
        return
        [
            CreateCapability(
                "text_input",
                inputModalities.Contains("text", StringComparer.OrdinalIgnoreCase),
                description: "模型支持文本输入。"),
            CreateCapability(
                "image_input",
                inputModalities.Contains("image", StringComparer.OrdinalIgnoreCase),
                description: "模型支持图片输入。"),
            CreateCapability(
                "reasoning_effort",
                item.SupportedReasoningEfforts.Count > 0,
                values: item.SupportedReasoningEfforts,
                defaultValue: item.DefaultReasoningEffort,
                description: "模型支持的 reasoning effort。"),
            CreateCapability(
                "personality",
                item.SupportsPersonality,
                description: "模型是否支持 personality。"),
            CreateCapability(
                "parallel_tool_calls",
                item.SupportsParallelToolCalls,
                description: "模型是否支持并行工具调用。"),
            CreateCapability(
                "reasoning_summaries",
                item.SupportsReasoningSummaries,
                defaultValue: item.DefaultReasoningSummary,
                description: "模型是否支持 reasoning summary。"),
            CreateCapability(
                "verbosity",
                item.SupportsVerbosity,
                defaultValue: item.DefaultVerbosity,
                description: "模型是否支持 verbosity。"),
            CreateCapability(
                "websocket_transport_preference",
                item.PreferWebsocketTransport,
                description: "模型是否偏好 websocket transport。"),
        ];
    }

    private static CapabilityDescriptor CreateCapability(
        string name,
        bool supported,
        IReadOnlyList<string>? values = null,
        string? defaultValue = null,
        string? description = null)
        => new(name, supported, values?.ToArray() ?? Array.Empty<string>(), defaultValue, description);

    private static IReadOnlyList<EngineBindingCandidate> BuildBindingCandidates(
        IReadOnlyList<ProviderProfile> providers,
        ProviderProfile selectedProvider,
        ModelProfile selectedModel)
    {
        var candidates = new List<EngineBindingCandidate>(providers.Count);
        foreach (var provider in providers)
        {
            var providerModel = ResolveModelProfile(provider.Models, selectedModel.Model)
                                ?? provider.Models.FirstOrDefault(static item => item.IsDefault)
                                ?? provider.Models.FirstOrDefault();
            if (providerModel is null)
            {
                continue;
            }

            var isSelected = string.Equals(provider.Key, selectedProvider.Key, StringComparison.OrdinalIgnoreCase);
            var preferWebsocketTransport = providerModel.PreferWebsocketTransport;
            var useWebsocketTransport = preferWebsocketTransport && provider.SupportsWebsockets;
            candidates.Add(new EngineBindingCandidate(
                providerKey: provider.Key,
                modelKey: providerModel.Key,
                model: providerModel.Model,
                transportFamily: provider.TransportFamily,
                transportMode: BuildTransportMode(provider.TransportFamily, useWebsocketTransport),
                selectionReason: isSelected ? "selected" : "fallback",
                isSelected: isSelected,
                supportsWebsockets: provider.SupportsWebsockets));
        }

        return candidates
            .OrderByDescending(static item => item.IsSelected)
            .ThenBy(static item => item.ProviderKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ProviderProfile? ResolveProviderProfile(
        IReadOnlyList<ProviderProfile> providers,
        string? providerKey)
    {
        var normalizedProviderKey = Normalize(providerKey);
        if (string.IsNullOrWhiteSpace(normalizedProviderKey))
        {
            return providers.FirstOrDefault();
        }

        return providers.FirstOrDefault(item =>
            string.Equals(item.Key, normalizedProviderKey, StringComparison.OrdinalIgnoreCase));
    }

    private static ModelProfile? ResolveModelProfile(
        IReadOnlyList<ModelProfile> models,
        string? model)
    {
        var normalizedModel = Normalize(model);
        if (string.IsNullOrWhiteSpace(normalizedModel))
        {
            return null;
        }

        return models.FirstOrDefault(item =>
            string.Equals(item.Model, normalizedModel, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Key, normalizedModel, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveReasoningEffort(
        ResolveEngineBinding request,
        StructuredValue? config,
        ModelProfile model)
    {
        return Normalize(request.ReasoningEffort)
               ?? Normalize(ReadStructuredString(config, "model_reasoning_effort"))
               ?? Normalize(model.DefaultReasoningEffort)
               ?? "medium";
    }

    private static string? ResolveReasoningSummary(
        ResolveEngineBinding request,
        StructuredValue? config,
        ModelProfile model)
    {
        if (!model.SupportsReasoningSummaries)
        {
            return null;
        }

        return Normalize(request.ReasoningSummary)
               ?? Normalize(ReadStructuredString(config, "model_reasoning_summary"))
               ?? Normalize(model.DefaultReasoningSummary);
    }

    private static string? ResolveVerbosity(
        ResolveEngineBinding request,
        StructuredValue? config,
        ModelProfile model)
    {
        if (!model.SupportsVerbosity)
        {
            return null;
        }

        return Normalize(request.Verbosity)
               ?? Normalize(ReadStructuredString(config, "model_verbosity"))
               ?? Normalize(model.DefaultVerbosity);
    }

    private static List<string> CollectProviderIds(StructuredValue? config, string activeProviderKey)
    {
        var orderedProviderIds = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddProviderId(orderedProviderIds, seen, activeProviderKey);
        var providers = ReadStructuredValue(config, "providers");
        if (providers?.Kind != StructuredValueKind.Object)
        {
            return orderedProviderIds;
        }

        foreach (var pair in providers.Properties)
        {
            AddProviderId(orderedProviderIds, seen, pair.Key);
        }

        return orderedProviderIds;
    }

    private static void AddProviderId(ICollection<string> orderedProviderIds, ISet<string> seen, string? providerId)
    {
        var normalized = Normalize(providerId);
        if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized))
        {
            return;
        }

        orderedProviderIds.Add(normalized);
    }

    private static string ResolveActiveModelRouteSetId(StructuredValue? config)
    {
        var rootRouteSet = Normalize(ReadStructuredString(config, "model_route_set"));
        var activeProfile = Normalize(ReadStructuredString(config, "profile")) ?? "default";
        var profileConfig = TryReadObjectMember(ReadStructuredValue(config, "profiles"), activeProfile);
        var sessionId = Normalize(ReadStructuredString(profileConfig, "session")) ?? "default";
        var executionId = Normalize(ReadStructuredString(profileConfig, "execution")) ?? "default";
        var agentId = Normalize(ReadStructuredString(profileConfig, "agent"))
                      ?? ResolveExecutionAgentId(config, executionId)
                      ?? "default";

        return ResolveScopedModelRouteSetId(config, "session_profiles", sessionId)
               ?? ResolveScopedModelRouteSetId(config, "execution_profiles", executionId)
               ?? ResolveScopedModelRouteSetId(config, "agents", agentId)
               ?? Normalize(ReadStructuredString(profileConfig, "model_route_set"))
               ?? rootRouteSet
               ?? TianShuModelRouteSetDefaults.DefaultRouteSetId;
    }

    private static string? ResolveExecutionAgentId(StructuredValue? config, string executionId)
        => Normalize(ReadStructuredString(TryReadObjectMember(ReadStructuredValue(config, "execution_profiles"), executionId), "agent"));

    private static string? ResolveScopedModelRouteSetId(StructuredValue? config, string sectionName, string id)
        => Normalize(ReadStructuredString(TryReadObjectMember(ReadStructuredValue(config, sectionName), id), "model_route_set"));

    private static StructuredValue? TryGetProviderConfig(
        StructuredValue? config,
        string providerId)
    {
        var providers = ReadStructuredValue(config, "providers");
        if (providers?.Kind != StructuredValueKind.Object || string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        foreach (var pair in providers.Properties)
        {
            if (string.Equals(pair.Key, providerId, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static StructuredValue? TryReadObjectMember(StructuredValue? value, string key)
    {
        if (value?.Kind != StructuredValueKind.Object)
        {
            return null;
        }

        foreach (var pair in value.Properties)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> BuildTransportModes(string transportFamily, bool supportsWebsockets)
    {
        var normalizedTransportFamily = Normalize(transportFamily) ?? DefaultTransportFamily;
        var modes = new List<string>(capacity: supportsWebsockets ? 2 : 1)
        {
            BuildTransportMode(normalizedTransportFamily, useWebsocketTransport: false),
        };

        if (supportsWebsockets)
        {
            modes.Add(BuildTransportMode(normalizedTransportFamily, useWebsocketTransport: true));
        }

        return modes;
    }

    private static string BuildTransportMode(string transportFamily, bool useWebsocketTransport)
    {
        var normalizedTransportFamily = Normalize(transportFamily) ?? DefaultTransportFamily;
        return useWebsocketTransport
            ? $"{normalizedTransportFamily}.websocket"
            : $"{normalizedTransportFamily}.http";
    }

    private static StructuredValue? ReadStructuredValue(StructuredValue? value, string propertyName)
    {
        if (value?.Kind != StructuredValueKind.Object || !value.Properties.TryGetValue(propertyName, out var property))
        {
            return null;
        }

        return property;
    }

    private static string? ReadStructuredString(StructuredValue? value, string propertyName)
    {
        var property = ReadStructuredValue(value, propertyName);
        return property?.Kind switch
        {
            StructuredValueKind.String => property.StringValue,
            StructuredValueKind.Number => property.NumberValue,
            StructuredValueKind.Boolean => property.BooleanValue?.ToString(),
            _ => null,
        };
    }

    private static bool? ReadStructuredBool(StructuredValue? value, string propertyName)
        => ReadStructuredValue(value, propertyName)?.BooleanValue;

    private static IReadOnlyList<string> ReadStructuredStringArray(StructuredValue? value, string propertyName)
    {
        var property = ReadStructuredValue(value, propertyName);
        if (property?.Kind != StructuredValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return property.Items
            .Select(static item => Normalize(item.StringValue))
            .Where(static item => item is not null)
            .Select(static item => item!)
            .ToArray();
    }

    private static string BuildProviderDisplayName(string providerId)
    {
        if (string.Equals(providerId, "openai", StringComparison.OrdinalIgnoreCase))
        {
            return "OpenAI";
        }

        var segments = providerId
            .Split(['-', '_', '.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return providerId;
        }

        return string.Join(
            " ",
            segments.Select(static segment =>
                segment.Length == 0
                    ? string.Empty
                    : segment.Length == 1
                        ? char.ToUpperInvariant(segment[0]).ToString()
                        : char.ToUpperInvariant(segment[0]) + segment[1..]));
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
