using TianShu.Configuration;
using TianShu.Contracts.Catalog;
using TianShu.Contracts.Primitives;
using TianShu.Provider.Abstractions;

namespace TianShu.Execution.Runtime;

internal interface IModelRouter
{
    ModelRouteResolutionOutcome Resolve(DefaultModelRouteResolutionContext context);
}

internal sealed record DefaultModelRouteResolutionContext(
    StructuredValue Config,
    ModelRouteResolutionRequest Request,
    string? SessionReasoningEffort = null,
    string? SessionReasoningSummary = null,
    string? SessionVerbosity = null,
    string? DiagnosticsCorrelationId = null,
    Func<string, string?>? ReadEnvironmentVariable = null,
    bool RequireEnvironmentSecretValue = false,
    bool ValidateProtocolBinding = false);

internal sealed record ModelRouteResolutionOutcome(
    ModelRouteResolutionResult? Result,
    ModelRouteResolutionFailure? Failure)
{
    public bool Succeeded => Result is not null;

    public static ModelRouteResolutionOutcome Success(ModelRouteResolutionResult result)
        => new(result, null);

    public static ModelRouteResolutionOutcome Failed(ModelRouteResolutionFailure failure)
        => new(null, failure);
}

internal sealed class DefaultModelRouter : IModelRouter
{
    public static DefaultModelRouter Instance { get; } = new();

    public ModelRouteResolutionOutcome Resolve(DefaultModelRouteResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Config);
        ArgumentNullException.ThrowIfNull(context.Request);

        var correlationId = Normalize(context.DiagnosticsCorrelationId) ?? CreateCorrelationId();
        if (context.Request.RegisteredRouteKinds.Count > 0
            && !context.Request.RegisteredRouteKinds.Any(routeKind =>
                string.Equals(routeKind.Value, context.Request.RouteKind.Value, StringComparison.OrdinalIgnoreCase)))
        {
            return ModelRouteResolutionOutcome.Failed(new ModelRouteResolutionFailure(
                context.Request.RouteSetId,
                context.Request.RouteKind,
                "model_route_kind_unregistered",
                $"route kind `{context.Request.RouteKind.Value}` 未注册到当前 Stage Registry route projection。",
                correlationId));
        }

        var routeSet = TianShuModelRouteSetDefaults.ResolveRouteSet(
            ToPlainConfigDictionary(context.Config),
            context.Request.RouteSetId);
        var route = ResolveRoute(routeSet, context.Request.RouteKind);
        if (route is null)
        {
            return ModelRouteResolutionOutcome.Failed(new ModelRouteResolutionFailure(
                routeSet.RouteSetId,
                context.Request.RouteKind,
                "model_route_not_found",
                $"route set `{routeSet.RouteSetId}` 中没有当前 Stage 需要的 route `{context.Request.RouteKind.Value}`。",
                correlationId));
        }

        var filtered = new List<ModelRouteCandidateFilterReason>();
        for (var index = 0; index < route.Candidates.Count; index++)
        {
            var candidate = route.Candidates[index];
            if (!CandidateCapabilitiesMatch(context.Request.RequiredCapabilities, candidate.Capabilities))
            {
                filtered.Add(Filter(index, candidate, "model_capability_mismatch", "候选模型不满足当前 route 的能力要求。"));
                continue;
            }

            var provider = Normalize(candidate.Provider);
            var model = Normalize(candidate.Model);
            if (provider is null || model is null)
            {
                filtered.Add(Filter(index, candidate, "model_route_candidate_invalid", "候选模型缺少 provider 或 model。"));
                continue;
            }

            if (IsProviderDisabled(context.Config, provider))
            {
                filtered.Add(Filter(index, candidate, "provider_disabled", "候选 provider 已被配置禁用。"));
                continue;
            }

            var protocols = ResolveProtocols(context, candidate, provider, model);
            if (protocols.Count == 0)
            {
                filtered.Add(Filter(index, candidate, "protocol_unavailable", "候选模型无法解析为受支持的 provider protocol。"));
                continue;
            }

            var protocol = protocols.FirstOrDefault(protocol => !context.ValidateProtocolBinding || HasProtocolBinding(protocol));
            if (protocol is null)
            {
                filtered.Add(Filter(index, candidate, "protocol_unavailable", "候选模型的 provider protocol 当前没有可用 adapter binding。"));
                continue;
            }

            var apiKeyEnvironmentVariable = ResolveApiKeyEnvironmentVariable(context, provider, protocol);
            if (apiKeyEnvironmentVariable is null)
            {
                filtered.Add(Filter(index, candidate, "provider_missing_secret", "候选 provider 没有可用的 secret env binding。"));
                continue;
            }

            if (context.RequireEnvironmentSecretValue
                && string.IsNullOrWhiteSpace(context.ReadEnvironmentVariable?.Invoke(apiKeyEnvironmentVariable)))
            {
                filtered.Add(Filter(index, candidate, "provider_missing_secret", $"环境变量 `{apiKeyEnvironmentVariable}` 未设置。"));
                continue;
            }

            return ModelRouteResolutionOutcome.Success(new ModelRouteResolutionResult(
                routeSet.RouteSetId,
                context.Request.RouteKind,
                provider,
                model,
                index,
                protocol,
                ResolveBaseUrl(context, provider, protocol),
                apiKeyEnvironmentVariable,
                ResolveReasoningEffort(context, provider),
                ResolveReasoningSummary(context, provider),
                ResolveVerbosity(context, provider),
                correlationId,
                filtered));
        }

        return ModelRouteResolutionOutcome.Failed(new ModelRouteResolutionFailure(
            routeSet.RouteSetId,
            context.Request.RouteKind,
            "model_route_no_available_candidate",
            $"route set `{routeSet.RouteSetId}` route `{route.Kind}` 没有可用候选模型。",
            correlationId,
            filtered));
    }

    private static TianShuModelRouteSnapshot? ResolveRoute(
        TianShuModelRouteSetSnapshot routeSet,
        ModelRouteKind routeKind)
    {
        return routeSet.Routes.FirstOrDefault(route => string.Equals(route.Kind, routeKind.Value, StringComparison.OrdinalIgnoreCase));
    }

    private static bool CandidateCapabilitiesMatch(
        IReadOnlyList<string> requiredCapabilities,
        IReadOnlyList<string> candidateCapabilities)
    {
        if (requiredCapabilities.Count == 0 || candidateCapabilities.Count == 0)
        {
            return true;
        }

        var available = new HashSet<string>(candidateCapabilities.Select(static item => item.Trim()), StringComparer.OrdinalIgnoreCase);
        return requiredCapabilities
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .All(available.Contains);
    }

    private static IReadOnlyList<string> ResolveProtocols(
        DefaultModelRouteResolutionContext context,
        TianShuModelRouteCandidateSnapshot candidate,
        string provider,
        string model)
    {
        try
        {
            return KernelModelProtocolResolver.ResolveModelProtocolCandidates(
                ToPlainConfigDictionary(context.Config),
                provider,
                model,
                Normalize(candidate.Protocol));
        }
        catch (InvalidOperationException)
        {
            return [];
        }
    }

    private static bool HasProtocolBinding(string protocol)
    {
        try
        {
            _ = ProviderResponsesRequestComposers.Resolve(protocol, "model route protocol");
            _ = ProviderResponsesTransportProtocolBindings.Resolve(protocol, "model route protocol");
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsProviderDisabled(StructuredValue config, string provider)
    {
        var providerConfig = ReadProviderConfig(config, provider);
        return ReadStructuredBool(providerConfig, "enabled") == false;
    }

    private static string? ResolveBaseUrl(DefaultModelRouteResolutionContext context, string provider, string protocol)
        => ReadProviderString(context.Config, provider, "base_url")
           ?? DefaultBaseUrl(provider, protocol);

    private static string? ResolveApiKeyEnvironmentVariable(DefaultModelRouteResolutionContext context, string provider, string protocol)
        => ReadProviderString(context.Config, provider, "api_key_env")
           ?? DefaultApiKeyEnvironmentVariable(provider, protocol);

    private static string? ResolveReasoningEffort(DefaultModelRouteResolutionContext context, string provider)
        => ReadProviderNestedString(context.Config, provider, "reasoning", "effort")
           ?? context.SessionReasoningEffort;

    private static string? ResolveReasoningSummary(DefaultModelRouteResolutionContext context, string provider)
        => ReadProviderNestedString(context.Config, provider, "reasoning", "summary")
           ?? context.SessionReasoningSummary;

    private static string? ResolveVerbosity(DefaultModelRouteResolutionContext context, string provider)
        => ReadProviderNestedString(context.Config, provider, "reasoning", "verbosity")
           ?? context.SessionVerbosity;

    private static StructuredValue? ReadProviderConfig(StructuredValue config, string provider)
        => TryReadObjectMember(ReadStructuredValue(config, "providers"), provider);

    private static string? ReadProviderString(StructuredValue config, string provider, params string[] propertyNames)
    {
        var providerConfig = ReadProviderConfig(config, provider);
        foreach (var propertyName in propertyNames)
        {
            if (Normalize(ReadStructuredString(providerConfig, propertyName)) is { } text)
            {
                return text;
            }
        }

        return null;
    }

    private static string? ReadProviderNestedString(
        StructuredValue config,
        string provider,
        string childName,
        params string[] propertyNames)
    {
        var child = TryReadObjectMember(ReadProviderConfig(config, provider), childName);
        foreach (var propertyName in propertyNames)
        {
            if (Normalize(ReadStructuredString(child, propertyName)) is { } text)
            {
                return text;
            }
        }

        return null;
    }

    private static string? DefaultBaseUrl(string provider, string protocol)
    {
        _ = protocol;
        if (string.Equals(provider, "anthropic", StringComparison.OrdinalIgnoreCase))
        {
            return "https://api.anthropic.com";
        }

        if (string.Equals(provider, "google", StringComparison.OrdinalIgnoreCase))
        {
            return "https://generativelanguage.googleapis.com";
        }

        return string.Equals(provider, "openai", StringComparison.OrdinalIgnoreCase)
            ? "https://api.openai.com/v1"
            : null;
    }

    private static string? DefaultApiKeyEnvironmentVariable(string provider, string protocol)
    {
        _ = protocol;
        if (string.Equals(provider, "anthropic", StringComparison.OrdinalIgnoreCase))
        {
            return "ANTHROPIC_API_KEY";
        }

        if (string.Equals(provider, "google", StringComparison.OrdinalIgnoreCase))
        {
            return "GEMINI_API_KEY";
        }

        return string.Equals(provider, "openai", StringComparison.OrdinalIgnoreCase)
            ? "OPENAI_API_KEY"
            : null;
    }

    private static ModelRouteCandidateFilterReason Filter(
        int candidateIndex,
        TianShuModelRouteCandidateSnapshot candidate,
        string reasonCode,
        string message)
        => new(candidateIndex, candidate.Provider, candidate.Model, reasonCode, message);

    private static string CreateCorrelationId()
        => $"model-route-{Guid.NewGuid():N}";

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static Dictionary<string, object?> ToPlainConfigDictionary(StructuredValue config)
    {
        return config.ToPlainObject() is Dictionary<string, object?> dictionary
            ? dictionary
            : new Dictionary<string, object?>(StringComparer.Ordinal);
    }

    private static StructuredValue? ReadStructuredValue(StructuredValue? value, string propertyName)
    {
        return value?.Kind == StructuredValueKind.Object && value.Properties.TryGetValue(propertyName, out var property)
            ? property
            : null;
    }

    private static StructuredValue? TryReadObjectMember(StructuredValue? value, string key)
    {
        if (value?.Kind != StructuredValueKind.Object || string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return value.Properties.TryGetValue(key, out var exact)
            ? exact
            : value.Properties.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
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
    {
        var property = ReadStructuredValue(value, propertyName);
        if (property?.Kind == StructuredValueKind.Boolean)
        {
            return property.BooleanValue;
        }

        return property?.Kind == StructuredValueKind.String && bool.TryParse(property.StringValue, out var parsed)
            ? parsed
            : null;
    }
}
