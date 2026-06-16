using System.Text;
using System.Text.Json;
using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Primitives;

namespace TianShu.Configuration;

/// <summary>
/// 模型路由方案默认值与诊断辅助。
/// Default model route set and diagnostics helpers.
/// </summary>
public static class TianShuModelRouteSetDefaults
{
    public const string DefaultRouteSetId = "default";
    public const string DefaultRouteKind = "default";

    public static readonly IReadOnlyList<string> DefaultRouteKinds = BuiltInStageDefinitions.All
        .Select(static stage => stage.ModelRouteKind.Value)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public static IReadOnlyList<string> BuildRegisteredRouteKinds(IEnumerable<StageDefinition> stageDefinitions)
    {
        ArgumentNullException.ThrowIfNull(stageDefinitions);

        return NormalizeRegisteredRouteKinds(stageDefinitions.Select(static stage => stage.ModelRouteKind.Value));
    }

    public static IReadOnlyList<string> NormalizeRegisteredRouteKinds(IEnumerable<string>? routeKinds)
    {
        if (routeKinds is null)
        {
            return DefaultRouteKinds;
        }

        var normalized = routeKinds
            .Select(Normalize)
            .Where(static routeKind => routeKind is not null)
            .Select(static routeKind => routeKind!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return normalized.Length == 0 ? DefaultRouteKinds : normalized;
    }

    public static TianShuModelRouteSetSnapshot CreateDefaultRouteSet(
        string? provider,
        string? model,
        string? protocol = null,
        string routeSetId = DefaultRouteSetId)
    {
        var normalizedProvider = Normalize(provider) ?? "openai";
        var normalizedModel = Normalize(model) ?? "gpt-5";
        var normalizedProtocol = Normalize(protocol);
        var routes = DefaultRouteKinds
            .Select(routeKind => new TianShuModelRouteSnapshot(
                routeKind,
                [
                    new TianShuModelRouteCandidateSnapshot(
                        normalizedProvider,
                        normalizedModel,
                        normalizedProtocol,
                        Array.Empty<string>(),
                        CandidateIndex: 0,
                        UnavailableReason: null),
                ],
                Fallback: null))
            .ToArray();

        return new TianShuModelRouteSetSnapshot(
            Normalize(routeSetId) ?? DefaultRouteSetId,
            "Default Model Route Set",
            "TianShu route-set-first model routing template.",
            routes,
            IsVirtual: true);
    }

    public static string ResolveActiveRouteSetId(Dictionary<string, object?> config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var rootRouteSet = Normalize(ReadStringExact(config, "model_route_set"));
        var activeProfile = Normalize(ReadStringExact(config, "profile")) ?? "default";
        Dictionary<string, object?>? profileConfig = null;
        if (TryReadObjectExact(config, "profiles", out var profiles))
        {
            _ = TryReadObjectExact(profiles, activeProfile, out profileConfig);
        }

        var sessionId = Normalize(ReadStringExact(profileConfig ?? [], "session")) ?? "default";
        var executionId = Normalize(ReadStringExact(profileConfig ?? [], "execution")) ?? "default";
        var agentId = Normalize(ReadStringExact(profileConfig ?? [], "agent"))
                      ?? ResolveExecutionAgentId(config, executionId)
                      ?? "default";

        return ResolveScopedModelRouteSetId(config, "session_profiles", sessionId)
               ?? ResolveScopedModelRouteSetId(config, "execution_profiles", executionId)
               ?? ResolveScopedModelRouteSetId(config, "agents", agentId)
               ?? Normalize(ReadStringExact(profileConfig ?? [], "model_route_set"))
               ?? rootRouteSet
               ?? DefaultRouteSetId;
    }

    public static TianShuModelRouteSetSnapshot ResolveRouteSet(
        Dictionary<string, object?> config,
        string? routeSetId = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var resolvedRouteSetId = Normalize(routeSetId) ?? ResolveActiveRouteSetId(config);
        if (TryReadConfiguredRouteSet(config, resolvedRouteSetId, out var routeSet))
        {
            return routeSet;
        }

        return CreateMissingRouteSet(resolvedRouteSetId);
    }

    public static StructuredValue CreateDefaultRoutesValue(string? provider, string? model, string? protocol = null)
    {
        var routeSet = CreateDefaultRouteSet(provider, model, protocol);
        return RoutesToStructuredValue(routeSet.Routes);
    }

    public static string ExportDefaultRouteSetToml(
        string? provider,
        string? model,
        string? protocol = null,
        string routeSetId = DefaultRouteSetId)
    {
        var routeSet = CreateDefaultRouteSet(provider, model, protocol, routeSetId);
        var builder = new StringBuilder();
        builder.AppendLine("# TianShu 默认模型路由方案模板。");
        builder.AppendLine("# 该模板只包含 provider/model/protocol 引用，不保存 API key、base URL 或其它 secret。");
        builder.AppendLine($"model_route_set = {QuoteString(routeSet.RouteSetId)}");
        builder.AppendLine();
        builder.AppendLine($"[model_route_sets.{QuoteBareKey(routeSet.RouteSetId)}]");
        builder.AppendLine($"display_name = {QuoteString(routeSet.DisplayName)}");
        builder.AppendLine($"description = {QuoteString(routeSet.Description)}");
        builder.AppendLine("routes = [");
        foreach (var route in routeSet.Routes)
        {
            builder.AppendLine("  {");
            builder.AppendLine($"    kind = {QuoteString(route.Kind)},");
            builder.AppendLine("    candidates = [");
            foreach (var candidate in route.Candidates)
            {
                builder.Append("      { ");
                builder.Append($"provider = {QuoteString(candidate.Provider)}, ");
                builder.Append($"model = {QuoteString(candidate.Model)}");
                if (!string.IsNullOrWhiteSpace(candidate.Protocol))
                {
                    builder.Append($", protocol = {QuoteString(candidate.Protocol!)}");
                }

                builder.AppendLine(" },");
            }

            builder.AppendLine("    ],");
            builder.AppendLine("  },");
        }

        builder.AppendLine("]");
        return builder.ToString();
    }

    public static TianShuModelRouteDiagnostic BuildRouteDiagnostic(
        Dictionary<string, object?> config,
        string? routeKind = null,
        string? routeSetId = null,
        IReadOnlyList<string>? registeredRouteKinds = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var routeSet = ResolveRouteSet(config, routeSetId);
        var coverage = BuildRouteCoverage(routeSet, registeredRouteKinds);
        var requestedRouteKind = Normalize(routeKind) ?? DefaultRouteKind;
        var routeKindRegistered = coverage.RegisteredRouteKinds.Contains(requestedRouteKind, StringComparer.OrdinalIgnoreCase);
        var route = routeKindRegistered
            ? routeSet.Routes.FirstOrDefault(candidate => string.Equals(candidate.Kind, requestedRouteKind, StringComparison.OrdinalIgnoreCase))
            : null;
        var fallbackReason = routeKindRegistered
            ? route is null
                ? routeSet.Routes.Count == 0
                    ? $"route set `{routeSet.RouteSetId}` is not configured"
                    : $"registered route `{requestedRouteKind}` is not configured"
                : null
            : $"route kind `{requestedRouteKind}` is not registered in Stage Registry";

        return new TianShuModelRouteDiagnostic(
            routeSet.RouteSetId,
            routeSet.IsVirtual,
            requestedRouteKind,
            route?.Kind,
            fallbackReason,
            route?.Candidates.FirstOrDefault(),
            route?.Candidates.Skip(1).ToArray() ?? Array.Empty<TianShuModelRouteCandidateSnapshot>(),
            route?.Candidates ?? Array.Empty<TianShuModelRouteCandidateSnapshot>(),
            coverage.RegisteredRouteKinds,
            coverage.ConfiguredRegisteredRouteKinds,
            coverage.MissingRegisteredRouteKinds,
            coverage.UnknownRouteKinds);
    }

    public static TianShuModelRouteCoverage BuildRouteCoverage(TianShuModelRouteSetSnapshot routeSet)
        => BuildRouteCoverage(routeSet, DefaultRouteKinds);

    public static TianShuModelRouteCoverage BuildRouteCoverage(
        TianShuModelRouteSetSnapshot routeSet,
        IReadOnlyList<string>? registeredRouteKinds)
    {
        ArgumentNullException.ThrowIfNull(routeSet);

        var normalizedRegisteredRouteKinds = NormalizeRegisteredRouteKinds(registeredRouteKinds);
        var configuredKinds = routeSet.Routes
            .Select(static route => route.Kind)
            .Where(static kind => !string.IsNullOrWhiteSpace(kind))
            .ToArray();
        var configuredKindSet = configuredKinds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var registeredKindSet = normalizedRegisteredRouteKinds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new TianShuModelRouteCoverage(
            normalizedRegisteredRouteKinds,
            normalizedRegisteredRouteKinds.Where(configuredKindSet.Contains).ToArray(),
            normalizedRegisteredRouteKinds.Where(kind => !configuredKindSet.Contains(kind)).ToArray(),
            configuredKinds
                .Where(kind => !registeredKindSet.Contains(kind))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static TianShuModelRouteSetSnapshot CreateMissingRouteSet(string routeSetId)
        => new(
            Normalize(routeSetId) ?? DefaultRouteSetId,
            "Missing Model Route Set",
            "No explicit model_route_sets entry is configured for the active route set.",
            Array.Empty<TianShuModelRouteSnapshot>(),
            IsVirtual: true);

    private static bool TryReadConfiguredRouteSet(
        Dictionary<string, object?> config,
        string routeSetId,
        out TianShuModelRouteSetSnapshot routeSet)
    {
        routeSet = null!;
        if (!TryReadRouteSetConfig(config, routeSetId, out var routeSetConfig))
        {
            return false;
        }

        var routes = ReadRoutes(routeSetConfig);
        if (routes.Count == 0)
        {
            return false;
        }

        routeSet = new TianShuModelRouteSetSnapshot(
            routeSetId,
            Normalize(ReadStringExact(routeSetConfig, "display_name")) ?? routeSetId,
            Normalize(ReadStringExact(routeSetConfig, "description")) ?? string.Empty,
            routes,
            IsVirtual: false);
        return true;
    }

    private static bool TryReadRouteSetConfig(
        Dictionary<string, object?> config,
        string routeSetId,
        out Dictionary<string, object?> routeSetConfig)
    {
        if (TryReadObjectExact(config, "model_route_sets", out var routeSets)
            && TryReadObjectExact(routeSets, routeSetId, out routeSetConfig))
        {
            return true;
        }

        var prefix = $"model_route_sets.{routeSetId}.";
        var flattenedConfig = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in config)
        {
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var propertyName = key[prefix.Length..];
            if (!string.IsNullOrWhiteSpace(propertyName))
            {
                flattenedConfig[propertyName] = value;
            }
        }

        if (flattenedConfig.Count > 0)
        {
            routeSetConfig = flattenedConfig;
            return true;
        }

        routeSetConfig = null!;
        return false;
    }

    private static IReadOnlyList<TianShuModelRouteSnapshot> ReadRoutes(Dictionary<string, object?> routeSetConfig)
    {
        if (!TryReadValueExact(routeSetConfig, "routes", out var rawRoutes)
            || rawRoutes is not IEnumerable<object?> routeValues)
        {
            return Array.Empty<TianShuModelRouteSnapshot>();
        }

        var routes = new List<TianShuModelRouteSnapshot>();
        foreach (var rawRoute in routeValues)
        {
            if (!TryAsDictionary(rawRoute, out var routeConfig))
            {
                continue;
            }

            var kind = Normalize(ReadStringExact(routeConfig, "kind"));
            if (kind is null)
            {
                continue;
            }

            var candidates = ReadCandidates(routeConfig);
            if (candidates.Count == 0)
            {
                continue;
            }

            routes.Add(new TianShuModelRouteSnapshot(
                kind,
                candidates,
                Normalize(ReadStringExact(routeConfig, "fallback"))));
        }

        return routes;
    }

    private static IReadOnlyList<TianShuModelRouteCandidateSnapshot> ReadCandidates(Dictionary<string, object?> routeConfig)
    {
        if (!TryReadValueExact(routeConfig, "candidates", out var rawCandidates)
            || rawCandidates is not IEnumerable<object?> candidateValues)
        {
            return Array.Empty<TianShuModelRouteCandidateSnapshot>();
        }

        var candidates = new List<TianShuModelRouteCandidateSnapshot>();
        var index = 0;
        foreach (var rawCandidate in candidateValues)
        {
            if (!TryAsDictionary(rawCandidate, out var candidateConfig))
            {
                continue;
            }

            var provider = Normalize(ReadStringExact(candidateConfig, "provider"));
            var model = Normalize(ReadStringExact(candidateConfig, "model"));
            if (provider is null || model is null)
            {
                continue;
            }

            candidates.Add(new TianShuModelRouteCandidateSnapshot(
                provider,
                model,
                Normalize(ReadStringExact(candidateConfig, "protocol")),
                ReadStringArrayExact(candidateConfig, "capabilities"),
                index,
                UnavailableReason: null));
            index++;
        }

        return candidates;
    }

    private static StructuredValue RoutesToStructuredValue(IReadOnlyList<TianShuModelRouteSnapshot> routes)
        => StructuredValue.FromArray(routes.Select(RouteToStructuredValue).ToArray());

    private static StructuredValue RouteToStructuredValue(TianShuModelRouteSnapshot route)
    {
        var properties = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["kind"] = StructuredValue.FromString(route.Kind),
            ["candidates"] = StructuredValue.FromArray(route.Candidates.Select(CandidateToStructuredValue).ToArray()),
        };
        if (!string.IsNullOrWhiteSpace(route.Fallback))
        {
            properties["fallback"] = StructuredValue.FromString(route.Fallback!);
        }

        return StructuredValue.FromObject(properties);
    }

    private static StructuredValue CandidateToStructuredValue(TianShuModelRouteCandidateSnapshot candidate)
    {
        var properties = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["provider"] = StructuredValue.FromString(candidate.Provider),
            ["model"] = StructuredValue.FromString(candidate.Model),
        };
        if (!string.IsNullOrWhiteSpace(candidate.Protocol))
        {
            properties["protocol"] = StructuredValue.FromString(candidate.Protocol!);
        }

        if (candidate.Capabilities.Count > 0)
        {
            properties["capabilities"] = StructuredValue.FromArray(candidate.Capabilities.Select(StructuredValue.FromString).ToArray());
        }

        return StructuredValue.FromObject(properties);
    }

    private static string? ResolveExecutionAgentId(Dictionary<string, object?> config, string executionId)
    {
        if (!TryReadObjectExact(config, "execution_profiles", out var executionProfiles)
            || !TryReadObjectExact(executionProfiles, executionId, out var executionProfile))
        {
            return null;
        }

        return Normalize(ReadStringExact(executionProfile, "agent"));
    }

    private static string? ResolveScopedModelRouteSetId(Dictionary<string, object?> config, string sectionName, string id)
    {
        if (!TryReadObjectExact(config, sectionName, out var section)
            || !TryReadObjectExact(section, id, out var item))
        {
            return null;
        }

        return Normalize(ReadStringExact(item, "model_route_set"));
    }

    private static string? ReadStringExact(Dictionary<string, object?> config, string key)
        => TryReadValueExact(config, key, out var value)
           && TryReadString(value, out var text)
            ? text
            : null;

    private static string[] ReadStringArrayExact(Dictionary<string, object?> config, string key)
    {
        return TryReadValueExact(config, key, out var value)
               && TryReadStringArray(value, out var values)
            ? values
            : Array.Empty<string>();
    }

    private static bool TryReadObjectExact(
        Dictionary<string, object?> config,
        string key,
        out Dictionary<string, object?> value)
    {
        if (TryReadValueExact(config, key, out var rawValue)
            && TryAsDictionary(rawValue, out value))
        {
            return true;
        }

        value = null!;
        return false;
    }

    private static bool TryReadValueExact(Dictionary<string, object?> config, string key, out object? value)
        => config.TryGetValue(key, out value);

    private static bool TryAsDictionary(object? value, out Dictionary<string, object?> dictionary)
    {
        switch (value)
        {
            case Dictionary<string, object?> concrete:
                dictionary = concrete;
                return true;
            case IReadOnlyDictionary<string, object?> readOnly:
                dictionary = readOnly.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
                return true;
            case IDictionary<string, object?> mutable:
                dictionary = mutable.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
                return true;
            case IEnumerable<KeyValuePair<string, object?>> pairs:
                dictionary = pairs.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.Object:
                dictionary = ConvertJsonObject(element);
                return true;
            default:
                dictionary = null!;
                return false;
        }
    }

    private static bool TryReadString(object? value, out string text)
    {
        switch (value)
        {
            case string stringValue:
                text = stringValue;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.String:
                text = element.GetString() ?? string.Empty;
                return true;
            default:
                text = string.Empty;
                return false;
        }
    }

    private static bool TryReadStringArray(object? value, out string[] values)
    {
        if (value is string)
        {
            values = Array.Empty<string>();
            return false;
        }

        if (value is IEnumerable<object?> items)
        {
            values = items
                .Select(static item => TryReadString(item, out var text) ? Normalize(text) : null)
                .Where(static item => item is not null)
                .Cast<string>()
                .ToArray();
            return true;
        }

        if (value is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
            values = element
                .EnumerateArray()
                .Select(static item => item.ValueKind == JsonValueKind.String ? Normalize(item.GetString()) : null)
                .Where(static item => item is not null)
                .Cast<string>()
                .ToArray();
            return true;
        }

        values = Array.Empty<string>();
        return false;
    }

    private static Dictionary<string, object?> ConvertJsonObject(JsonElement element)
    {
        var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            dictionary[property.Name] = ConvertJsonValue(property.Value);
        }

        return dictionary;
    }

    private static object? ConvertJsonValue(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => ConvertJsonObject(element),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonValue).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.TryGetInt64(out var intValue)
                ? intValue
                : element.TryGetDouble(out var doubleValue)
                    ? doubleValue
                    : element.GetRawText(),
            JsonValueKind.Null => null,
            _ => element.GetRawText(),
        };

    private static string QuoteBareKey(string value)
        => value.All(static ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-') ? value : QuoteString(value);

    private static string QuoteString(string value)
        => $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record TianShuModelRouteSetSnapshot(
    string RouteSetId,
    string DisplayName,
    string Description,
    IReadOnlyList<TianShuModelRouteSnapshot> Routes,
    bool IsVirtual);

public sealed record TianShuModelRouteSnapshot(
    string Kind,
    IReadOnlyList<TianShuModelRouteCandidateSnapshot> Candidates,
    string? Fallback);

public sealed record TianShuModelRouteCandidateSnapshot(
    string Provider,
    string Model,
    string? Protocol,
    IReadOnlyList<string> Capabilities,
    int CandidateIndex,
    string? UnavailableReason);

public sealed record TianShuModelRouteDiagnostic(
    string RouteSetId,
    bool RouteSetIsVirtual,
    string RequestedRouteKind,
    string? ResolvedRouteKind,
    string? RouteFallbackReason,
    TianShuModelRouteCandidateSnapshot? PreferredCandidate,
    IReadOnlyList<TianShuModelRouteCandidateSnapshot> FallbackCandidates,
    IReadOnlyList<TianShuModelRouteCandidateSnapshot> Candidates,
    IReadOnlyList<string> RegisteredRouteKinds,
    IReadOnlyList<string> ConfiguredRegisteredRouteKinds,
    IReadOnlyList<string> MissingRegisteredRouteKinds,
    IReadOnlyList<string> UnknownRouteKinds);

public sealed record TianShuModelRouteCoverage(
    IReadOnlyList<string> RegisteredRouteKinds,
    IReadOnlyList<string> ConfiguredRegisteredRouteKinds,
    IReadOnlyList<string> MissingRegisteredRouteKinds,
    IReadOnlyList<string> UnknownRouteKinds);



