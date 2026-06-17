using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Provider;

/// <summary>
/// Provider 模块 manifest；只声明公开接入面，不包含 API key 或 raw provider 请求。
/// Provider module manifest; declares only the public access surface and never contains API keys or raw provider requests.
/// </summary>
public sealed record ProviderModuleManifest
{
    public ProviderModuleManifest(
        string providerId,
        string displayName,
        string version,
        string minimumTianShuVersion,
        IReadOnlyList<ProviderProtocolBinding> protocolBindings,
        IReadOnlyList<ProviderModelRouteSet> modelRouteSets,
        ProviderEndpointDescriptor endpoint,
        IReadOnlyList<ProviderErrorSpec>? errorSpecs = null,
        IReadOnlyList<string>? diagnostics = null)
    {
        ProviderId = IdentifierGuard.AgainstNullOrWhiteSpace(providerId, nameof(providerId));
        DisplayName = IdentifierGuard.AgainstNullOrWhiteSpace(displayName, nameof(displayName));
        Version = IdentifierGuard.AgainstNullOrWhiteSpace(version, nameof(version));
        MinimumTianShuVersion = IdentifierGuard.AgainstNullOrWhiteSpace(minimumTianShuVersion, nameof(minimumTianShuVersion));
        ProtocolBindings = RequireNonEmpty(protocolBindings, nameof(protocolBindings));
        ModelRouteSets = RequireNonEmpty(modelRouteSets, nameof(modelRouteSets));
        Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        ErrorSpecs = errorSpecs ?? Array.Empty<ProviderErrorSpec>();
        Diagnostics = diagnostics ?? Array.Empty<string>();
    }

    public string ProviderId { get; }

    public string DisplayName { get; }

    public string Version { get; }

    public string MinimumTianShuVersion { get; }

    public IReadOnlyList<ProviderProtocolBinding> ProtocolBindings { get; }

    public IReadOnlyList<ProviderModelRouteSet> ModelRouteSets { get; }

    public ProviderEndpointDescriptor Endpoint { get; }

    public IReadOnlyList<ProviderErrorSpec> ErrorSpecs { get; }

    public IReadOnlyList<string> Diagnostics { get; }

    private static IReadOnlyList<T> RequireNonEmpty<T>(IReadOnlyList<T>? values, string name)
        => values is { Count: > 0 } ? values : throw new ArgumentException("Provider module manifest requires at least one item.", name);
}

/// <summary>
/// Provider wire 协议绑定；用于声明 provider module 能处理的外部协议。
/// Provider wire protocol binding declaring the external protocol a provider module can handle.
/// </summary>
public sealed record ProviderProtocolBinding
{
    public ProviderProtocolBinding(
        string wireApi,
        ProviderProtocolKind protocolKind,
        ProviderCapabilityProfile capabilities,
        bool enabled = true)
    {
        WireApi = IdentifierGuard.AgainstNullOrWhiteSpace(wireApi, nameof(wireApi));
        ProtocolKind = protocolKind;
        Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        Enabled = enabled;
    }

    public string WireApi { get; }

    public ProviderProtocolKind ProtocolKind { get; }

    public ProviderCapabilityProfile Capabilities { get; }

    public bool Enabled { get; }
}

/// <summary>
/// Provider 模型路由候选。
/// Provider model route candidate.
/// </summary>
public sealed record ProviderModelRouteCandidate
{
    public ProviderModelRouteCandidate(
        string providerId,
        string model,
        string wireApi,
        int priority = 0,
        bool enabled = true)
    {
        ProviderId = IdentifierGuard.AgainstNullOrWhiteSpace(providerId, nameof(providerId));
        Model = IdentifierGuard.AgainstNullOrWhiteSpace(model, nameof(model));
        WireApi = IdentifierGuard.AgainstNullOrWhiteSpace(wireApi, nameof(wireApi));
        Priority = priority;
        Enabled = enabled;
    }

    public string ProviderId { get; }

    public string Model { get; }

    public string WireApi { get; }

    public int Priority { get; }

    public bool Enabled { get; }
}

/// <summary>
/// Provider 模型路由集合；Kernel 只能从已批准 route set 物化 ModelInvocationStep。
/// Provider model route set; Kernel can materialize ModelInvocationStep only from approved route sets.
/// </summary>
public sealed record ProviderModelRouteSet
{
    public ProviderModelRouteSet(
        string routeSetId,
        IReadOnlyList<ProviderModelRouteCandidate> candidates,
        string? defaultModel = null)
    {
        RouteSetId = IdentifierGuard.AgainstNullOrWhiteSpace(routeSetId, nameof(routeSetId));
        Candidates = candidates is { Count: > 0 } ? candidates : throw new ArgumentException("Provider route set requires candidates.", nameof(candidates));
        DefaultModel = defaultModel;
    }

    public string RouteSetId { get; }

    public IReadOnlyList<ProviderModelRouteCandidate> Candidates { get; }

    public string? DefaultModel { get; }
}

/// <summary>
/// Provider usage 投影；真实 usage 与估算 usage 必须明确区分。
/// Provider usage projection; real and estimated usage must be explicitly distinguished.
/// </summary>
public sealed record ProviderUsageProjection
{
    public ProviderUsageProjection(
        bool available,
        bool estimated,
        long? inputTokens,
        long? outputTokens,
        long? reasoningTokens,
        long? totalTokens,
        string source,
        string? missingReason = null)
    {
        Available = available;
        Estimated = estimated;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        ReasoningTokens = reasoningTokens;
        TotalTokens = totalTokens;
        Source = IdentifierGuard.AgainstNullOrWhiteSpace(source, nameof(source));
        MissingReason = missingReason;
    }

    public bool Available { get; }

    public bool Estimated { get; }

    public long? InputTokens { get; }

    public long? OutputTokens { get; }

    public long? ReasoningTokens { get; }

    public long? TotalTokens { get; }

    public string Source { get; }

    public string? MissingReason { get; }
}

/// <summary>
/// Provider 成本投影；只有真实 usage 和 price model 都存在时才能 available。
/// Provider cost projection; available only when real usage and a price model are both present.
/// </summary>
public sealed record ProviderCostProjection(
    bool Available,
    decimal? EstimatedCost,
    string? Currency,
    string? PriceModelVersion,
    string? MissingReason = null);

/// <summary>
/// Provider 调用指标投影。
/// Provider invocation metrics projection.
/// </summary>
public sealed record ProviderMetricsProjection
{
    public ProviderMetricsProjection(
        string providerId,
        string model,
        string wireApi,
        ProviderUsageProjection usage,
        ProviderCostProjection cost,
        TimeSpan latency,
        int attemptIndex,
        IReadOnlyList<string>? diagnosticsRefs = null)
    {
        ProviderId = IdentifierGuard.AgainstNullOrWhiteSpace(providerId, nameof(providerId));
        Model = IdentifierGuard.AgainstNullOrWhiteSpace(model, nameof(model));
        WireApi = IdentifierGuard.AgainstNullOrWhiteSpace(wireApi, nameof(wireApi));
        Usage = usage ?? throw new ArgumentNullException(nameof(usage));
        Cost = cost ?? throw new ArgumentNullException(nameof(cost));
        Latency = latency < TimeSpan.Zero ? throw new ArgumentOutOfRangeException(nameof(latency), "Latency cannot be negative.") : latency;
        AttemptIndex = attemptIndex <= 0 ? throw new ArgumentOutOfRangeException(nameof(attemptIndex), "AttemptIndex must be greater than zero.") : attemptIndex;
        DiagnosticsRefs = diagnosticsRefs ?? Array.Empty<string>();
    }

    public string ProviderId { get; }

    public string Model { get; }

    public string WireApi { get; }

    public ProviderUsageProjection Usage { get; }

    public ProviderCostProjection Cost { get; }

    public TimeSpan Latency { get; }

    public int AttemptIndex { get; }

    public IReadOnlyList<string> DiagnosticsRefs { get; }
}

/// <summary>
/// Provider 错误分类。
/// Provider error classification.
/// </summary>
public enum ProviderErrorKind
{
    Unknown = 0,
    Authentication = 1,
    Authorization = 2,
    RateLimited = 3,
    InvalidRequest = 4,
    Transport = 5,
    Timeout = 6,
    ProviderUnavailable = 7,
    ProtocolViolation = 8,
    ContextLengthExceeded = 9,
    ToolCallInvalid = 10,
}

/// <summary>
/// Provider 错误规范。
/// Provider error specification.
/// </summary>
public sealed record ProviderErrorSpec
{
    public ProviderErrorSpec(
        string code,
        ProviderErrorKind kind,
        bool retryable,
        bool safeForUser,
        string? remediation = null)
    {
        Code = IdentifierGuard.AgainstNullOrWhiteSpace(code, nameof(code));
        Kind = kind;
        Retryable = retryable;
        SafeForUser = safeForUser;
        Remediation = remediation;
    }

    public string Code { get; }

    public ProviderErrorKind Kind { get; }

    public bool Retryable { get; }

    public bool SafeForUser { get; }

    public string? Remediation { get; }
}

/// <summary>
/// Provider 模块公开接入描述；后续 Runtime binding 只能消费 validated access。
/// Provider module public access descriptor; later Runtime binding can consume only validated access.
/// </summary>
public sealed record ProviderModuleAccessDescriptor
{
    public ProviderModuleAccessDescriptor(
        ProviderModuleManifest manifest,
        ProviderDescriptor descriptor,
        ProviderProtocolBinding protocolBinding,
        ProviderModelRouteSet modelRouteSet,
        IReadOnlyList<ProviderErrorSpec>? errorSpecs = null)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        ProtocolBinding = protocolBinding ?? throw new ArgumentNullException(nameof(protocolBinding));
        ModelRouteSet = modelRouteSet ?? throw new ArgumentNullException(nameof(modelRouteSet));
        ErrorSpecs = errorSpecs ?? Array.Empty<ProviderErrorSpec>();
    }

    public ProviderModuleManifest Manifest { get; }

    public ProviderDescriptor Descriptor { get; }

    public ProviderProtocolBinding ProtocolBinding { get; }

    public ProviderModelRouteSet ModelRouteSet { get; }

    public IReadOnlyList<ProviderErrorSpec> ErrorSpecs { get; }
}

/// <summary>
/// Provider 模块公开接入校验结果。
/// Provider module public access validation result.
/// </summary>
public sealed record ProviderModuleAccessValidationResult(
    ProviderModuleAccessDescriptor? Access,
    IReadOnlyList<ProviderModuleAccessIssue> Issues)
{
    public bool IsValid => Access is not null && Issues.All(static issue => issue.Severity != ProviderModuleAccessIssueSeverity.Error);
}

/// <summary>
/// Provider 模块公开接入问题严重性。
/// Provider module public access issue severity.
/// </summary>
public enum ProviderModuleAccessIssueSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

/// <summary>
/// Provider 模块公开接入问题。
/// Provider module public access issue.
/// </summary>
public sealed record ProviderModuleAccessIssue(
    string Code,
    string Message,
    ProviderModuleAccessIssueSeverity Severity = ProviderModuleAccessIssueSeverity.Error)
{
    public string Code { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Code, nameof(Code));

    public string Message { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Message, nameof(Message));
}

/// <summary>
/// Provider 模块公开接入校验器。
/// Provider module public access validator.
/// </summary>
public static class ProviderModuleAccessValidator
{
    public static ProviderModuleAccessValidationResult Validate(
        ProviderModuleManifest manifest,
        ProviderDescriptor descriptor,
        string routeSetId)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(routeSetId);

        List<ProviderModuleAccessIssue> issues = [];
        if (!string.Equals(manifest.ProviderId, descriptor.ProviderId, StringComparison.Ordinal))
        {
            issues.Add(Error("provider_access.provider_id_mismatch", "Provider manifest 与 ProviderDescriptor 的 providerId 不一致。"));
        }

        if (manifest.Endpoint.ProtocolKind != descriptor.ProtocolKind
            || !string.Equals(manifest.Endpoint.ProviderId, descriptor.ProviderId, StringComparison.Ordinal))
        {
            issues.Add(Error("provider_access.endpoint_mismatch", "Provider endpoint 与 descriptor 协议或 providerId 不一致。"));
        }

        var protocolBinding = manifest.ProtocolBindings
            .Where(static binding => binding.Enabled)
            .FirstOrDefault(binding => binding.ProtocolKind == descriptor.ProtocolKind);
        if (protocolBinding is null)
        {
            issues.Add(Error("provider_access.protocol_binding_missing", "Provider manifest 缺少匹配 descriptor 的 enabled protocol binding。"));
        }

        var routeSet = manifest.ModelRouteSets.FirstOrDefault(route => string.Equals(route.RouteSetId, routeSetId, StringComparison.Ordinal));
        if (routeSet is null)
        {
            issues.Add(Error("provider_access.route_set_missing", "Provider manifest 缺少指定 model route set。"));
        }
        else if (!routeSet.Candidates.Any(candidate => candidate.Enabled
                                                       && string.Equals(candidate.ProviderId, manifest.ProviderId, StringComparison.Ordinal)
                                                       && (protocolBinding is null || string.Equals(candidate.WireApi, protocolBinding.WireApi, StringComparison.Ordinal))))
        {
            issues.Add(Error("provider_access.route_set_no_enabled_candidate", "Provider model route set 没有可用候选。"));
        }

        if (issues.Any(static issue => issue.Severity == ProviderModuleAccessIssueSeverity.Error))
        {
            return new ProviderModuleAccessValidationResult(null, issues);
        }

        return new ProviderModuleAccessValidationResult(
            new ProviderModuleAccessDescriptor(manifest, descriptor, protocolBinding!, routeSet!, manifest.ErrorSpecs),
            issues);
    }

    public static ProviderUsageProjection ProjectUsage(ProviderUsage? usage, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (usage is null)
        {
            return new ProviderUsageProjection(
                available: false,
                estimated: false,
                inputTokens: null,
                outputTokens: null,
                reasoningTokens: null,
                totalTokens: null,
                source: source,
                missingReason: "provider_usage_missing");
        }

        var total = usage.InputTokens + usage.OutputTokens + (usage.ReasoningTokens ?? 0);
        return new ProviderUsageProjection(
            available: true,
            estimated: false,
            inputTokens: usage.InputTokens,
            outputTokens: usage.OutputTokens,
            reasoningTokens: usage.ReasoningTokens,
            totalTokens: total,
            source: source);
    }

    public static ProviderCostProjection ProjectCost(
        ProviderUsageProjection usage,
        decimal? estimatedCost,
        string? currency,
        string? priceModelVersion)
    {
        ArgumentNullException.ThrowIfNull(usage);
        if (!usage.Available || usage.Estimated)
        {
            return new ProviderCostProjection(false, null, null, priceModelVersion, "provider_usage_not_real");
        }

        if (estimatedCost is null || string.IsNullOrWhiteSpace(currency) || string.IsNullOrWhiteSpace(priceModelVersion))
        {
            return new ProviderCostProjection(false, null, currency, priceModelVersion, "price_model_missing");
        }

        return new ProviderCostProjection(true, estimatedCost, currency, priceModelVersion);
    }

    private static ProviderModuleAccessIssue Error(string code, string message)
        => new(code, message, ProviderModuleAccessIssueSeverity.Error);
}
