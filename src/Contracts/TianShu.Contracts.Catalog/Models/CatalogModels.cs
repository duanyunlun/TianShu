using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;

namespace TianShu.Contracts.Catalog;

/// <summary>
/// 能力描述符，表达某项能力是否受支持以及允许值范围。
/// Capability descriptor that expresses whether a capability is supported and what value range is allowed.
/// </summary>
public sealed record CapabilityDescriptor
{
    /// <summary>
    /// 初始化能力描述符。
    /// Initializes a capability descriptor.
    /// </summary>
    public CapabilityDescriptor(
        string name,
        bool supported,
        IReadOnlyList<string>? values = null,
        string? defaultValue = null,
        string? description = null)
    {
        Name = IdentifierGuard.AgainstNullOrWhiteSpace(name, nameof(name));
        Supported = supported;
        Values = values ?? Array.Empty<string>();
        DefaultValue = defaultValue;
        Description = description;
    }

    public string Name { get; }

    public bool Supported { get; }

    public IReadOnlyList<string> Values { get; }

    public string? DefaultValue { get; }

    public string? Description { get; }
}

/// <summary>
/// 模型画像，表达某个模型在 TianShu 目录中的能力信息。
/// Model profile that describes the capabilities of a model in the TianShu catalog.
/// </summary>
public sealed record ModelProfile
{
    /// <summary>
    /// 初始化模型画像。
    /// Initializes a model profile.
    /// </summary>
    public ModelProfile(
        string key,
        string model,
        string displayName,
        string? description = null,
        bool hidden = false,
        bool isDefault = false,
        string? defaultReasoningEffort = null,
        IReadOnlyList<string>? supportedReasoningEfforts = null,
        IReadOnlyList<string>? inputModalities = null,
        IReadOnlyList<CapabilityDescriptor>? supportedCapabilities = null,
        bool supportsPersonality = false,
        bool supportsParallelToolCalls = false,
        bool supportsReasoningSummaries = false,
        string? defaultReasoningSummary = null,
        bool supportsVerbosity = false,
        string? defaultVerbosity = null,
        bool preferWebsocketTransport = false)
    {
        Key = IdentifierGuard.AgainstNullOrWhiteSpace(key, nameof(key));
        Model = IdentifierGuard.AgainstNullOrWhiteSpace(model, nameof(model));
        DisplayName = IdentifierGuard.AgainstNullOrWhiteSpace(displayName, nameof(displayName));
        Description = description;
        Hidden = hidden;
        IsDefault = isDefault;
        DefaultReasoningEffort = defaultReasoningEffort;
        SupportedReasoningEfforts = supportedReasoningEfforts ?? Array.Empty<string>();
        InputModalities = inputModalities ?? Array.Empty<string>();
        SupportedCapabilities = supportedCapabilities ?? Array.Empty<CapabilityDescriptor>();
        SupportsPersonality = supportsPersonality;
        SupportsParallelToolCalls = supportsParallelToolCalls;
        SupportsReasoningSummaries = supportsReasoningSummaries;
        DefaultReasoningSummary = defaultReasoningSummary;
        SupportsVerbosity = supportsVerbosity;
        DefaultVerbosity = defaultVerbosity;
        PreferWebsocketTransport = preferWebsocketTransport;
    }

    public string Key { get; }

    public string Model { get; }

    public string DisplayName { get; }

    public string? Description { get; }

    public bool Hidden { get; }

    public bool IsDefault { get; }

    public string? DefaultReasoningEffort { get; }

    public IReadOnlyList<string> SupportedReasoningEfforts { get; }

    public IReadOnlyList<string> InputModalities { get; }

    public IReadOnlyList<CapabilityDescriptor> SupportedCapabilities { get; }

    public bool SupportsPersonality { get; }

    public bool SupportsParallelToolCalls { get; }

    public bool SupportsReasoningSummaries { get; }

    public string? DefaultReasoningSummary { get; }

    public bool SupportsVerbosity { get; }

    public string? DefaultVerbosity { get; }

    public bool PreferWebsocketTransport { get; }
}

/// <summary>
/// Provider 画像，表达某个南向能力供应者及其模型清单。
/// Provider profile that describes a southbound capability provider and its model catalog.
/// </summary>
public sealed record ProviderProfile
{
    /// <summary>
    /// 初始化 Provider 画像。
    /// Initializes a provider profile.
    /// </summary>
    public ProviderProfile(
        string key,
        string displayName,
        string transportFamily,
        IReadOnlyList<string>? transportModes = null,
        IReadOnlyList<CapabilityDescriptor>? supportedCapabilities = null,
        IReadOnlyList<ModelProfile>? models = null,
        string? baseUrl = null,
        string? apiKeyEnvironmentVariable = null,
        bool supportsWebsockets = false)
    {
        Key = IdentifierGuard.AgainstNullOrWhiteSpace(key, nameof(key));
        DisplayName = IdentifierGuard.AgainstNullOrWhiteSpace(displayName, nameof(displayName));
        TransportFamily = IdentifierGuard.AgainstNullOrWhiteSpace(transportFamily, nameof(transportFamily));
        TransportModes = transportModes ?? Array.Empty<string>();
        SupportedCapabilities = supportedCapabilities ?? Array.Empty<CapabilityDescriptor>();
        Models = models ?? Array.Empty<ModelProfile>();
        BaseUrl = baseUrl;
        ApiKeyEnvironmentVariable = apiKeyEnvironmentVariable;
        SupportsWebsockets = supportsWebsockets;
    }

    public string Key { get; }

    public string DisplayName { get; }

    public string TransportFamily { get; }

    public IReadOnlyList<string> TransportModes { get; }

    public IReadOnlyList<CapabilityDescriptor> SupportedCapabilities { get; }

    public IReadOnlyList<ModelProfile> Models { get; }

    public string? BaseUrl { get; }

    public string? ApiKeyEnvironmentVariable { get; }

    public bool SupportsWebsockets { get; }
}

/// <summary>
/// 推理配置画像，表达绑定解析后的推理档位偏好。
/// Reasoning profile that expresses the resolved reasoning preference of a binding.
/// </summary>
public sealed record CatalogReasoningProfile(string? Effort = null, string? Summary = null, string? Verbosity = null);

/// <summary>
/// 流式偏好，表达引擎绑定解析出的 transport 选择。
/// Streaming preference that expresses the transport choice resolved for an engine binding.
/// </summary>
public sealed record CatalogStreamingPreference
{
    /// <summary>
    /// 初始化流式偏好。
    /// Initializes a streaming preference.
    /// </summary>
    public CatalogStreamingPreference(
        string transportMode,
        bool preferWebsocketTransport = false,
        bool useWebsocketTransport = false)
    {
        TransportMode = IdentifierGuard.AgainstNullOrWhiteSpace(transportMode, nameof(transportMode));
        PreferWebsocketTransport = preferWebsocketTransport;
        UseWebsocketTransport = useWebsocketTransport;
    }

    public string TransportMode { get; }

    public bool PreferWebsocketTransport { get; }

    public bool UseWebsocketTransport { get; }
}

/// <summary>
/// 模型路由用途，表达当前阶段需要哪类模型能力。
/// Model route kind that describes what type of model capability the current stage needs.
/// </summary>
public sealed record ModelRouteKind
{
    /// <summary>
    /// 初始化模型路由用途。
    /// Initializes a model route kind.
    /// </summary>
    public ModelRouteKind(string value)
    {
        Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));
    }

    public string Value { get; }

    public static ModelRouteKind Default { get; } = new("default");

    public static ModelRouteKind Planning { get; } = new("planning");

    public static ModelRouteKind Coding { get; } = new("coding");

    public static ModelRouteKind Review { get; } = new("review");

    public static ModelRouteKind Summarization { get; } = new("summarization");

    public static ModelRouteKind MemoryExtraction { get; } = new("memory_extraction");

    public static ModelRouteKind LongContext { get; } = new("long_context");

    public static ModelRouteKind Fast { get; } = new("fast");
}

/// <summary>
/// 模型路由候选项。数组顺序就是 fallback 顺序，首个候选是首选模型。
/// Model route candidate. Array order is the fallback order, and the first candidate is preferred.
/// </summary>
public sealed record ModelRouteCandidate
{
    /// <summary>
    /// 初始化模型路由候选项。
    /// Initializes a model route candidate.
    /// </summary>
    public ModelRouteCandidate(
        string providerId,
        string model,
        string? protocol = null,
        IReadOnlyList<string>? capabilities = null,
        int? maxContextTokens = null,
        string? costTier = null,
        string? latencyTier = null)
    {
        ProviderId = IdentifierGuard.AgainstNullOrWhiteSpace(providerId, nameof(providerId));
        Model = IdentifierGuard.AgainstNullOrWhiteSpace(model, nameof(model));
        Protocol = protocol;
        Capabilities = capabilities ?? Array.Empty<string>();
        MaxContextTokens = maxContextTokens;
        CostTier = costTier;
        LatencyTier = latencyTier;
    }

    public string ProviderId { get; }

    public string Model { get; }

    public string? Protocol { get; }

    public IReadOnlyList<string> Capabilities { get; }

    public int? MaxContextTokens { get; }

    public string? CostTier { get; }

    public string? LatencyTier { get; }
}

/// <summary>
/// 单条模型路由，按用途保存有序候选模型列表。
/// Single model route that stores an ordered candidate list for a route kind.
/// </summary>
public sealed record ModelRoute
{
    /// <summary>
    /// 初始化模型路由。
    /// Initializes a model route.
    /// </summary>
    public ModelRoute(
        ModelRouteKind kind,
        IReadOnlyList<ModelRouteCandidate> candidates,
        string? fallbackRouteKind = null)
    {
        Kind = kind ?? throw new ArgumentNullException(nameof(kind));
        Candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        if (Candidates.Count == 0)
        {
            throw new ArgumentException("模型路由候选列表不能为空。", nameof(candidates));
        }

        FallbackRouteKind = fallbackRouteKind;
    }

    public ModelRouteKind Kind { get; }

    public IReadOnlyList<ModelRouteCandidate> Candidates { get; }

    public string? FallbackRouteKind { get; }
}

/// <summary>
/// 模型路由方案，包含多个按阶段选择模型的 route。
/// Model route set that contains multiple stage-oriented routes.
/// </summary>
public sealed record ModelRouteSet
{
    /// <summary>
    /// 初始化模型路由方案。
    /// Initializes a model route set.
    /// </summary>
    public ModelRouteSet(
        string id,
        IReadOnlyList<ModelRoute> routes,
        string? displayName = null,
        string? description = null)
    {
        Id = IdentifierGuard.AgainstNullOrWhiteSpace(id, nameof(id));
        Routes = routes ?? throw new ArgumentNullException(nameof(routes));
        if (Routes.Count == 0)
        {
            throw new ArgumentException("模型路由方案至少需要一条 route。", nameof(routes));
        }

        DisplayName = displayName;
        Description = description;
    }

    public string Id { get; }

    public string? DisplayName { get; }

    public string? Description { get; }

    public IReadOnlyList<ModelRoute> Routes { get; }
}

/// <summary>
/// 模型路由解析请求，表达当前阶段需要解析哪一个 route set 与 route。
/// Model route resolution request that describes the route set and route to resolve for a stage.
/// </summary>
public sealed record ModelRouteResolutionRequest
{
    /// <summary>
    /// 初始化模型路由解析请求。
    /// Initializes a model route resolution request.
    /// </summary>
    public ModelRouteResolutionRequest(
        string routeSetId,
        ModelRouteKind routeKind,
        IReadOnlyList<string>? requiredCapabilities = null,
        int? requiredContextTokens = null,
        string? workspacePath = null,
        string? threadId = null,
        IReadOnlyList<ModelRouteKind>? registeredRouteKinds = null)
    {
        RouteSetId = IdentifierGuard.AgainstNullOrWhiteSpace(routeSetId, nameof(routeSetId));
        RouteKind = routeKind ?? throw new ArgumentNullException(nameof(routeKind));
        RequiredCapabilities = requiredCapabilities ?? Array.Empty<string>();
        RequiredContextTokens = requiredContextTokens;
        WorkspacePath = workspacePath;
        ThreadId = threadId;
        RegisteredRouteKinds = NormalizeRegisteredRouteKinds(registeredRouteKinds);
    }

    public string RouteSetId { get; }

    public ModelRouteKind RouteKind { get; }

    public IReadOnlyList<string> RequiredCapabilities { get; }

    public int? RequiredContextTokens { get; }

    public string? WorkspacePath { get; }

    public string? ThreadId { get; }

    public IReadOnlyList<ModelRouteKind> RegisteredRouteKinds { get; }

    private static IReadOnlyList<ModelRouteKind> NormalizeRegisteredRouteKinds(IReadOnlyList<ModelRouteKind>? routeKinds)
    {
        if (routeKinds is null || routeKinds.Count == 0)
        {
            return Array.Empty<ModelRouteKind>();
        }

        return routeKinds
            .Where(static routeKind => routeKind is not null)
            .DistinctBy(static routeKind => routeKind.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

/// <summary>
/// 模型路由候选过滤原因。
/// Reason why a model route candidate was filtered.
/// </summary>
public sealed record ModelRouteCandidateFilterReason(
    int CandidateIndex,
    string ProviderId,
    string Model,
    string ReasonCode,
    string Message);

/// <summary>
/// 模型路由解析结果，固化最终 provider/model/protocol 绑定与诊断信息。
/// Model route resolution result that pins the final provider/model/protocol binding and diagnostics.
/// </summary>
public sealed record ModelRouteResolutionResult
{
    /// <summary>
    /// 初始化模型路由解析结果。
    /// Initializes a model route resolution result.
    /// </summary>
    public ModelRouteResolutionResult(
        string routeSetId,
        ModelRouteKind routeKind,
        string providerId,
        string model,
        int candidateIndex,
        string? protocol = null,
        string? baseUrl = null,
        string? apiKeyEnvironmentVariable = null,
        string? reasoningEffort = null,
        string? reasoningSummary = null,
        string? verbosity = null,
        string? diagnosticsCorrelationId = null,
        IReadOnlyList<ModelRouteCandidateFilterReason>? filteredCandidates = null)
    {
        RouteSetId = IdentifierGuard.AgainstNullOrWhiteSpace(routeSetId, nameof(routeSetId));
        RouteKind = routeKind ?? throw new ArgumentNullException(nameof(routeKind));
        ProviderId = IdentifierGuard.AgainstNullOrWhiteSpace(providerId, nameof(providerId));
        Model = IdentifierGuard.AgainstNullOrWhiteSpace(model, nameof(model));
        if (candidateIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(candidateIndex), "候选序号不能小于零。");
        }

        CandidateIndex = candidateIndex;
        Protocol = protocol;
        BaseUrl = baseUrl;
        ApiKeyEnvironmentVariable = apiKeyEnvironmentVariable;
        ReasoningEffort = reasoningEffort;
        ReasoningSummary = reasoningSummary;
        Verbosity = verbosity;
        DiagnosticsCorrelationId = diagnosticsCorrelationId;
        FilteredCandidates = filteredCandidates ?? Array.Empty<ModelRouteCandidateFilterReason>();
    }

    public string RouteSetId { get; }

    public ModelRouteKind RouteKind { get; }

    public string ProviderId { get; }

    public string Model { get; }

    public int CandidateIndex { get; }

    public bool UsedFallbackCandidate => CandidateIndex > 0;

    public string? Protocol { get; }

    public string? BaseUrl { get; }

    public string? ApiKeyEnvironmentVariable { get; }

    public string? ReasoningEffort { get; }

    public string? ReasoningSummary { get; }

    public string? Verbosity { get; }

    public string? DiagnosticsCorrelationId { get; }

    public IReadOnlyList<ModelRouteCandidateFilterReason> FilteredCandidates { get; }
}

/// <summary>
/// 模型路由结构化失败结果。
/// Structured model route resolution failure result.
/// </summary>
public sealed record ModelRouteResolutionFailure
{
    /// <summary>
    /// 初始化模型路由失败结果。
    /// Initializes a model route resolution failure.
    /// </summary>
    public ModelRouteResolutionFailure(
        string routeSetId,
        ModelRouteKind routeKind,
        string reasonCode,
        string message,
        string? diagnosticsCorrelationId = null,
        IReadOnlyList<ModelRouteCandidateFilterReason>? filteredCandidates = null)
    {
        RouteSetId = IdentifierGuard.AgainstNullOrWhiteSpace(routeSetId, nameof(routeSetId));
        RouteKind = routeKind ?? throw new ArgumentNullException(nameof(routeKind));
        ReasonCode = IdentifierGuard.AgainstNullOrWhiteSpace(reasonCode, nameof(reasonCode));
        Message = IdentifierGuard.AgainstNullOrWhiteSpace(message, nameof(message));
        DiagnosticsCorrelationId = diagnosticsCorrelationId;
        FilteredCandidates = filteredCandidates ?? Array.Empty<ModelRouteCandidateFilterReason>();
    }

    public string RouteSetId { get; }

    public ModelRouteKind RouteKind { get; }

    public string ReasonCode { get; }

    public string Message { get; }

    public string? DiagnosticsCorrelationId { get; }

    public IReadOnlyList<ModelRouteCandidateFilterReason> FilteredCandidates { get; }
}

/// <summary>
/// 引擎绑定候选项，表示某次绑定解析时可回退的候选方案。
/// Engine-binding candidate representing a fallback option produced during binding resolution.
/// </summary>
public sealed record EngineBindingCandidate
{
    /// <summary>
    /// 初始化引擎绑定候选项。
    /// Initializes an engine-binding candidate.
    /// </summary>
    public EngineBindingCandidate(
        string providerKey,
        string modelKey,
        string model,
        string transportFamily,
        string transportMode,
        string selectionReason,
        bool isSelected = false,
        bool supportsWebsockets = false)
    {
        ProviderKey = IdentifierGuard.AgainstNullOrWhiteSpace(providerKey, nameof(providerKey));
        ModelKey = IdentifierGuard.AgainstNullOrWhiteSpace(modelKey, nameof(modelKey));
        Model = IdentifierGuard.AgainstNullOrWhiteSpace(model, nameof(model));
        TransportFamily = IdentifierGuard.AgainstNullOrWhiteSpace(transportFamily, nameof(transportFamily));
        TransportMode = IdentifierGuard.AgainstNullOrWhiteSpace(transportMode, nameof(transportMode));
        SelectionReason = IdentifierGuard.AgainstNullOrWhiteSpace(selectionReason, nameof(selectionReason));
        IsSelected = isSelected;
        SupportsWebsockets = supportsWebsockets;
    }

    public string ProviderKey { get; }

    public string ModelKey { get; }

    public string Model { get; }

    public string TransportFamily { get; }

    public string TransportMode { get; }

    public string SelectionReason { get; }

    public bool IsSelected { get; }

    public bool SupportsWebsockets { get; }
}

/// <summary>
/// 引擎绑定结果，表达当前控制平面应采用的 provider/model/transport 组合。
/// Engine binding that expresses the provider, model, and transport combination chosen by the control plane.
/// </summary>
public sealed record EngineBinding
{
    /// <summary>
    /// 初始化引擎绑定。
    /// Initializes an engine binding.
    /// </summary>
    public EngineBinding(
        string engineKey,
        string providerKey,
        string modelKey,
        string model,
        string transportFamily,
        CatalogStreamingPreference streaming,
        string? baseUrl = null,
        string? apiKeyEnvironmentVariable = null,
        bool supportsWebsockets = false,
        CatalogReasoningProfile? reasoning = null,
        IReadOnlyList<EngineBindingCandidate>? fallbackPlan = null)
    {
        EngineKey = IdentifierGuard.AgainstNullOrWhiteSpace(engineKey, nameof(engineKey));
        ProviderKey = IdentifierGuard.AgainstNullOrWhiteSpace(providerKey, nameof(providerKey));
        ModelKey = IdentifierGuard.AgainstNullOrWhiteSpace(modelKey, nameof(modelKey));
        Model = IdentifierGuard.AgainstNullOrWhiteSpace(model, nameof(model));
        TransportFamily = IdentifierGuard.AgainstNullOrWhiteSpace(transportFamily, nameof(transportFamily));
        Streaming = streaming ?? throw new ArgumentNullException(nameof(streaming));
        BaseUrl = baseUrl;
        ApiKeyEnvironmentVariable = apiKeyEnvironmentVariable;
        SupportsWebsockets = supportsWebsockets;
        Reasoning = reasoning ?? new CatalogReasoningProfile();
        FallbackPlan = fallbackPlan ?? Array.Empty<EngineBindingCandidate>();
    }

    public string EngineKey { get; }

    public string ProviderKey { get; }

    public string ModelKey { get; }

    public string Model { get; }

    public string TransportFamily { get; }

    public CatalogStreamingPreference Streaming { get; }

    public string? BaseUrl { get; }

    public string? ApiKeyEnvironmentVariable { get; }

    public bool SupportsWebsockets { get; }

    public CatalogReasoningProfile Reasoning { get; }

    public IReadOnlyList<EngineBindingCandidate> FallbackPlan { get; }
}

/// <summary>
/// 已解析工具目录项，表达某个工具在当前平台与模型工具面的真实可见状态。
/// Resolved tool catalog item that describes the real platform and model-surface visibility of a tool.
/// </summary>
public sealed record ResolvedToolCatalogItem
{
    /// <summary>
    /// 初始化已解析工具目录项。
    /// Initializes a resolved tool catalog item.
    /// </summary>
    public ResolvedToolCatalogItem(
        string name,
        string description,
        ToolImplementationKind implementationKind,
        bool available,
        bool modelVisible,
        string? reason = null,
        string? implementationId = null,
        IReadOnlyList<ToolRuntimeRequirement>? requirements = null,
        ToolFallbackPolicy? fallbackPolicy = null,
        PlatformToolProfile? platformProfile = null)
    {
        Name = IdentifierGuard.AgainstNullOrWhiteSpace(name, nameof(name));
        Description = IdentifierGuard.AgainstNullOrWhiteSpace(description, nameof(description));
        ImplementationKind = implementationKind;
        Available = available;
        ModelVisible = modelVisible;
        Reason = reason;
        ImplementationId = implementationId;
        Requirements = requirements ?? Array.Empty<ToolRuntimeRequirement>();
        FallbackPolicy = fallbackPolicy;
        PlatformProfile = platformProfile;
    }

    public string Name { get; }

    public string Description { get; }

    public ToolImplementationKind ImplementationKind { get; }

    public bool Available { get; }

    public bool ModelVisible { get; }

    public string? Reason { get; }

    public string? ImplementationId { get; }

    public IReadOnlyList<ToolRuntimeRequirement> Requirements { get; }

    public ToolFallbackPolicy? FallbackPolicy { get; }

    public PlatformToolProfile? PlatformProfile { get; }
}

/// <summary>
/// 已解析工具目录快照，表达当前平台可审计的工具能力集合。
/// Resolved tool catalog snapshot that exposes the auditable tool capability set for the current platform.
/// </summary>
public sealed record ResolvedToolCatalogSnapshot
{
    /// <summary>
    /// 初始化已解析工具目录快照。
    /// Initializes a resolved tool catalog snapshot.
    /// </summary>
    public ResolvedToolCatalogSnapshot(IReadOnlyList<ResolvedToolCatalogItem>? items = null)
    {
        Items = items ?? Array.Empty<ResolvedToolCatalogItem>();
    }

    public IReadOnlyList<ResolvedToolCatalogItem> Items { get; }
}

/// <summary>
/// 能力目录中的模型路由候选只读投影，保留候选顺序但不暴露 secret。
/// Read-only model-route candidate projection in the capability catalog; preserves order without exposing secrets.
/// </summary>
public sealed record CapabilityModelRouteCandidate
{
    /// <summary>
    /// 初始化模型路由候选投影。
    /// Initializes a model-route candidate projection.
    /// </summary>
    public CapabilityModelRouteCandidate(
        string providerId,
        string model,
        int candidateIndex,
        string? protocol = null,
        IReadOnlyList<string>? capabilities = null,
        string? unavailableReason = null)
    {
        ProviderId = IdentifierGuard.AgainstNullOrWhiteSpace(providerId, nameof(providerId));
        Model = IdentifierGuard.AgainstNullOrWhiteSpace(model, nameof(model));
        if (candidateIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(candidateIndex), "候选序号不能小于零。");
        }

        CandidateIndex = candidateIndex;
        Protocol = protocol;
        Capabilities = capabilities ?? Array.Empty<string>();
        UnavailableReason = unavailableReason;
    }

    public string ProviderId { get; }

    public string Model { get; }

    public int CandidateIndex { get; }

    public string? Protocol { get; }

    public IReadOnlyList<string> Capabilities { get; }

    public string? UnavailableReason { get; }
}

/// <summary>
/// 能力目录中的单条模型路由只读投影。
/// Read-only single model-route projection in the capability catalog.
/// </summary>
public sealed record CapabilityModelRoute
{
    /// <summary>
    /// 初始化模型路由投影。
    /// Initializes a model-route projection.
    /// </summary>
    public CapabilityModelRoute(
        string kind,
        IReadOnlyList<CapabilityModelRouteCandidate> candidates,
        string? fallbackRouteKind = null,
        string? stageId = null)
    {
        Kind = IdentifierGuard.AgainstNullOrWhiteSpace(kind, nameof(kind));
        Candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        if (Candidates.Count == 0)
        {
            throw new ArgumentException("模型路由候选列表不能为空。", nameof(candidates));
        }

        FallbackRouteKind = fallbackRouteKind;
        StageId = string.IsNullOrWhiteSpace(stageId) ? null : stageId.Trim();
    }

    public string Kind { get; }

    public IReadOnlyList<CapabilityModelRouteCandidate> Candidates { get; }

    public string? FallbackRouteKind { get; }

    public string? StageId { get; }
}

/// <summary>
/// 能力目录中的模型路由方案只读投影，供消费层查询而非重新解析配置。
/// Read-only model-route-set projection in the capability catalog for consumers to query instead of re-parsing config.
/// </summary>
public sealed record CapabilityModelRouteSet
{
    /// <summary>
    /// 初始化模型路由方案投影。
    /// Initializes a model-route-set projection.
    /// </summary>
    public CapabilityModelRouteSet(
        string id,
        IReadOnlyList<CapabilityModelRoute> routes,
        string? displayName = null,
        string? description = null,
        bool isVirtual = false)
    {
        Id = IdentifierGuard.AgainstNullOrWhiteSpace(id, nameof(id));
        Routes = routes ?? throw new ArgumentNullException(nameof(routes));
        if (Routes.Count == 0)
        {
            throw new ArgumentException("模型路由方案至少需要一条 route。", nameof(routes));
        }

        DisplayName = displayName;
        Description = description;
        IsVirtual = isVirtual;
    }

    public string Id { get; }

    public string? DisplayName { get; }

    public string? Description { get; }

    public bool IsVirtual { get; }

    public IReadOnlyList<CapabilityModelRoute> Routes { get; }
}

/// <summary>
/// 能力目录快照，表示当前可见的 provider、模型与工具能力清单。
/// Capability-catalog snapshot representing the currently visible provider, model, and tool catalog.
/// </summary>
public sealed record CapabilityCatalogSnapshot
{
    /// <summary>
    /// 初始化能力目录快照。
    /// Initializes a capability-catalog snapshot.
    /// </summary>
    public CapabilityCatalogSnapshot(
        string? activeProviderKey = null,
        string? activeModel = null,
        IReadOnlyList<ProviderProfile>? providers = null,
        ResolvedToolCatalogSnapshot? tools = null,
        CapabilityModelRouteSet? modelRoutes = null)
    {
        ActiveProviderKey = activeProviderKey;
        ActiveModel = activeModel;
        Providers = providers ?? Array.Empty<ProviderProfile>();
        Tools = tools ?? new ResolvedToolCatalogSnapshot();
        ModelRoutes = modelRoutes;
    }

    public string? ActiveProviderKey { get; }

    public string? ActiveModel { get; }

    public IReadOnlyList<ProviderProfile> Providers { get; }

    public ResolvedToolCatalogSnapshot Tools { get; }

    public CapabilityModelRouteSet? ModelRoutes { get; }
}

/// <summary>
/// 引擎绑定解析结果，包含主选结果和全部候选项。
/// Engine-binding resolution result that contains the selected binding and all candidates.
/// </summary>
public sealed record ResolvedEngineBinding
{
    /// <summary>
    /// 初始化引擎绑定解析结果。
    /// Initializes an engine-binding resolution result.
    /// </summary>
    public ResolvedEngineBinding(
        EngineBinding? binding,
        IReadOnlyList<EngineBindingCandidate>? candidates = null)
    {
        Binding = binding;
        Candidates = candidates ?? Array.Empty<EngineBindingCandidate>();
    }

    public EngineBinding? Binding { get; }

    public IReadOnlyList<EngineBindingCandidate> Candidates { get; }
}
