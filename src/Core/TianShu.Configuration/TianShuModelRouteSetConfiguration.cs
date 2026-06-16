using System.Globalization;
using TianShu.Contracts.Configuration;
using TianShu.Contracts.Primitives;

namespace TianShu.Configuration;

/// <summary>
/// 模型路由方案的配置投影校验器。
/// Configuration projection validator for model route sets.
/// </summary>
internal static class TianShuModelRouteSetConfiguration
{
    private static readonly HashSet<string> KnownProtocols = new(StringComparer.OrdinalIgnoreCase)
    {
        "auto",
        "openai_responses",
        "openai_chat_completions",
        "anthropic_messages",
        "google_generative",
    };

    public static IReadOnlyList<ConfigurationIssue> Validate(
        IReadOnlyDictionary<string, ConfigurationFieldValue> valuesByKey)
    {
        ArgumentNullException.ThrowIfNull(valuesByKey);

        var issues = new List<ConfigurationIssue>();
        var routeSetIds = CollectRouteSetIds(valuesByKey);
        var providerIds = CollectProviderIds(valuesByKey);
        var registeredRouteKinds = ResolveRegisteredRouteKinds(valuesByKey);
        var knownRouteKinds = registeredRouteKinds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in valuesByKey)
        {
            if (TryReadRouteSetRoutesKey(pair.Key, out var routeSetId))
            {
                ValidateRoutes(routeSetId, pair.Key, pair.Value, registeredRouteKinds, knownRouteKinds, providerIds, issues);
            }
        }

        ValidateRouteSetReferences(valuesByKey, routeSetIds, issues);
        return issues;
    }

    public static void AddVirtualDefaultRouteSetIfMissing(IDictionary<string, ConfigurationFieldValue> valuesByKey)
    {
        ArgumentNullException.ThrowIfNull(valuesByKey);

        if (valuesByKey.Keys.Any(static key => key.StartsWith("model_route_sets.", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (!valuesByKey.TryGetValue("model_route_set", out var rootRouteSet)
            || rootRouteSet.Value?.Kind != StructuredValueKind.String
            || string.IsNullOrWhiteSpace(rootRouteSet.Value.StringValue))
        {
            valuesByKey["model_route_set"] = new ConfigurationFieldValue
            {
                Key = "model_route_set",
                Value = StructuredValue.FromString(TianShuModelRouteSetDefaults.DefaultRouteSetId),
                DefaultValue = StructuredValue.FromString(TianShuModelRouteSetDefaults.DefaultRouteSetId),
                IsConfigured = false,
            };
        }
    }

    private static SortedSet<string> CollectRouteSetIds(IReadOnlyDictionary<string, ConfigurationFieldValue> valuesByKey)
    {
        var routeSetIds = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in valuesByKey.Keys)
        {
            var parts = key.Split('.');
            if (parts.Length >= 3
                && string.Equals(parts[0], "model_route_sets", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(parts[1]))
            {
                routeSetIds.Add(parts[1]);
            }
        }

        return routeSetIds;
    }

    private static SortedSet<string> CollectProviderIds(IReadOnlyDictionary<string, ConfigurationFieldValue> valuesByKey)
    {
        var providerIds = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in valuesByKey)
        {
            var parts = pair.Key.Split('.');
            if (parts.Length >= 3
                && string.Equals(parts[0], "providers", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(parts[1]))
            {
                providerIds.Add(parts[1]);
                continue;
            }

            if (string.Equals(pair.Key, "provider", StringComparison.OrdinalIgnoreCase)
                && pair.Value.Value?.Kind == StructuredValueKind.String
                && !string.IsNullOrWhiteSpace(pair.Value.Value.StringValue))
            {
                providerIds.Add(pair.Value.Value.StringValue);
            }
        }

        return providerIds;
    }

    private static bool TryReadRouteSetRoutesKey(string key, out string routeSetId)
    {
        routeSetId = string.Empty;
        var parts = key.Split('.');
        if (parts.Length != 3
            || !string.Equals(parts[0], "model_route_sets", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(parts[2], "routes", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(parts[1]))
        {
            return false;
        }

        routeSetId = parts[1];
        return true;
    }

    private static void ValidateRoutes(
        string routeSetId,
        string fieldKey,
        ConfigurationFieldValue routeValue,
        IReadOnlyList<string> registeredRouteKinds,
        IReadOnlySet<string> knownRouteKinds,
        IReadOnlySet<string> providerIds,
        List<ConfigurationIssue> issues)
    {
        if (routeValue.Value?.Kind != StructuredValueKind.Array)
        {
            issues.Add(Error(
                "config.model_route_set.routes_not_array",
                $"模型路由方案 `{routeSetId}` 的 routes 必须是数组。",
                fieldKey,
                routeValue.SourceLayerId));
            return;
        }

        var routeKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var route in routeValue.Value.Items)
        {
            if (route.Kind == StructuredValueKind.Object
                && TryReadString(route, "kind", out var kind)
                && !string.IsNullOrWhiteSpace(kind))
            {
                if (!routeKinds.Add(kind))
                {
                    issues.Add(Error(
                        "config.model_route_set.route_kind_duplicate",
                        $"模型路由方案 `{routeSetId}` 重复配置了 route kind `{kind}`。",
                        fieldKey,
                        routeValue.SourceLayerId));
                }
            }
        }

        foreach (var requiredRouteKind in registeredRouteKinds)
        {
            if (!routeKinds.Contains(requiredRouteKind))
            {
                issues.Add(Error(
                    "config.model_route_set.route_kind_missing_registered",
                    $"模型路由方案 `{routeSetId}` 缺少已注册 Stage route `{requiredRouteKind}`。",
                    fieldKey,
                    routeValue.SourceLayerId));
            }
        }

        for (var routeIndex = 0; routeIndex < routeValue.Value.Items.Count; routeIndex++)
        {
            ValidateRoute(
                routeSetId,
                fieldKey,
                routeValue.Value.Items[routeIndex],
                routeIndex,
                routeKinds,
                knownRouteKinds,
                providerIds,
                routeValue.SourceLayerId,
                issues);
        }
    }

    private static void ValidateRoute(
        string routeSetId,
        string fieldKey,
        StructuredValue route,
        int routeIndex,
        IReadOnlySet<string> routeKinds,
        IReadOnlySet<string> knownRouteKinds,
        IReadOnlySet<string> providerIds,
        string? sourceLayerId,
        List<ConfigurationIssue> issues)
    {
        if (route.Kind != StructuredValueKind.Object)
        {
            issues.Add(Error(
                "config.model_route_set.route_not_object",
                $"模型路由方案 `{routeSetId}` 的第 {routeIndex + 1} 个 route 必须是对象。",
                fieldKey,
                sourceLayerId));
            return;
        }

        if (!TryReadString(route, "kind", out var kind) || string.IsNullOrWhiteSpace(kind))
        {
            issues.Add(Error(
                "config.model_route_set.route_kind_missing",
                $"模型路由方案 `{routeSetId}` 的第 {routeIndex + 1} 个 route 缺少 kind。",
                fieldKey,
                sourceLayerId));
        }
        else if (!knownRouteKinds.Contains(kind))
        {
            issues.Add(Error(
                "config.model_route_set.route_kind_unknown",
                $"模型路由方案 `{routeSetId}` 使用了未注册的 route kind `{kind}`；route kind 必须来自 Stage Registry，不能静默当作 default。",
                fieldKey,
                sourceLayerId));
        }

        if (TryReadString(route, "fallback", out var fallback)
            && !string.IsNullOrWhiteSpace(fallback)
            && !routeKinds.Contains(fallback))
        {
            issues.Add(Error(
                "config.model_route_set.fallback_route_unknown",
                $"模型路由方案 `{routeSetId}` 的 route `{kind ?? routeIndex.ToString(CultureInfo.InvariantCulture)}` 引用了不存在的 fallback route `{fallback}`。",
                fieldKey,
                sourceLayerId));
        }

        if (!route.Properties.TryGetValue("candidates", out var candidates) || candidates.Kind != StructuredValueKind.Array)
        {
            issues.Add(Error(
                "config.model_route_set.candidates_missing",
                $"模型路由方案 `{routeSetId}` 的 route `{kind ?? routeIndex.ToString(CultureInfo.InvariantCulture)}` 缺少 candidates 数组。",
                fieldKey,
                sourceLayerId));
            return;
        }

        if (candidates.Items.Count == 0)
        {
            issues.Add(Error(
                "config.model_route_set.candidates_empty",
                $"模型路由方案 `{routeSetId}` 的 route `{kind ?? routeIndex.ToString(CultureInfo.InvariantCulture)}` 至少需要一个候选模型。",
                fieldKey,
                sourceLayerId));
            return;
        }

        var seenCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var candidateIndex = 0; candidateIndex < candidates.Items.Count; candidateIndex++)
        {
            ValidateCandidate(
                routeSetId,
                kind,
                fieldKey,
                candidates.Items[candidateIndex],
                candidateIndex,
                providerIds,
                seenCandidates,
                sourceLayerId,
                issues);
        }
    }

    private static void ValidateCandidate(
        string routeSetId,
        string? routeKind,
        string fieldKey,
        StructuredValue candidate,
        int candidateIndex,
        IReadOnlySet<string> providerIds,
        ISet<string> seenCandidates,
        string? sourceLayerId,
        List<ConfigurationIssue> issues)
    {
        if (candidate.Kind != StructuredValueKind.Object)
        {
            issues.Add(Error(
                "config.model_route_set.candidate_not_object",
                $"模型路由方案 `{routeSetId}` 的 route `{routeKind}` 第 {candidateIndex + 1} 个候选必须是对象。",
                fieldKey,
                sourceLayerId));
            return;
        }

        if (!TryReadString(candidate, "provider", out var provider) || string.IsNullOrWhiteSpace(provider))
        {
            issues.Add(Error(
                "config.model_route_set.candidate_provider_missing",
                $"模型路由方案 `{routeSetId}` 的 route `{routeKind}` 第 {candidateIndex + 1} 个候选缺少 provider。",
                fieldKey,
                sourceLayerId));
        }
        else if (providerIds.Count > 0 && !providerIds.Contains(provider))
        {
            issues.Add(Error(
                "config.model_route_set.candidate_provider_unknown",
                $"模型路由方案 `{routeSetId}` 的 route `{routeKind}` 候选引用了未配置的 provider `{provider}`。",
                fieldKey,
                sourceLayerId));
        }

        if (!TryReadString(candidate, "model", out var model) || string.IsNullOrWhiteSpace(model))
        {
            issues.Add(Error(
                "config.model_route_set.candidate_model_missing",
                $"模型路由方案 `{routeSetId}` 的 route `{routeKind}` 第 {candidateIndex + 1} 个候选缺少 model。",
                fieldKey,
                sourceLayerId));
        }

        if (TryReadString(candidate, "protocol", out var protocol)
            && !string.IsNullOrWhiteSpace(protocol)
            && !KnownProtocols.Contains(protocol))
        {
            issues.Add(Error(
                "config.model_route_set.candidate_protocol_unknown",
                $"模型路由方案 `{routeSetId}` 的 route `{routeKind}` 候选使用了未知 protocol `{protocol}`。",
                fieldKey,
                sourceLayerId));
        }

        if (candidate.Properties.TryGetValue("capabilities", out var capabilities)
            && !IsStringArray(capabilities))
        {
            issues.Add(Error(
                "config.model_route_set.candidate_capabilities_invalid",
                $"模型路由方案 `{routeSetId}` 的 route `{routeKind}` 候选 capabilities 必须是字符串数组。",
                fieldKey,
                sourceLayerId));
        }

        if (!string.IsNullOrWhiteSpace(provider) && !string.IsNullOrWhiteSpace(model))
        {
            var duplicateKey = $"{provider}\u001f{model}\u001f{protocol}";
            if (!seenCandidates.Add(duplicateKey))
            {
                issues.Add(Error(
                    "config.model_route_set.candidate_duplicate",
                    $"模型路由方案 `{routeSetId}` 的 route `{routeKind}` 中重复配置了候选 `{provider}/{model}`。",
                    fieldKey,
                    sourceLayerId));
            }
        }
    }

    private static IReadOnlyList<string> ResolveRegisteredRouteKinds(
        IReadOnlyDictionary<string, ConfigurationFieldValue> valuesByKey)
    {
        var routeKinds = new List<string>(TianShuModelRouteSetDefaults.DefaultRouteKinds);
        if (valuesByKey.TryGetValue("stage_registry.stages", out var stagesValue)
            && stagesValue.Value is not null)
        {
            AddRouteKindsFromStageRegistryValue(routeKinds, stagesValue.Value);
        }

        AddRouteKindsFromFlattenedStageRegistryValues(routeKinds, valuesByKey);
        return TianShuModelRouteSetDefaults.NormalizeRegisteredRouteKinds(routeKinds);
    }

    private static void AddRouteKindsFromStageRegistryValue(
        List<string> routeKinds,
        StructuredValue stagesValue)
    {
        if (stagesValue.Kind == StructuredValueKind.Array)
        {
            foreach (var stageValue in stagesValue.Items)
            {
                AddRouteKindFromStageValue(routeKinds, stageValue, sourceStageId: null);
            }

            return;
        }

        if (stagesValue.Kind != StructuredValueKind.Object)
        {
            return;
        }

        foreach (var pair in stagesValue.Properties)
        {
            AddRouteKindFromStageValue(routeKinds, pair.Value, pair.Key);
        }
    }

    private static void AddRouteKindFromStageValue(
        List<string> routeKinds,
        StructuredValue stageValue,
        string? sourceStageId)
    {
        if (stageValue.Kind != StructuredValueKind.Object)
        {
            return;
        }

        if (TryReadBoolean(stageValue, out var enabled, "enabled") && enabled is false)
        {
            return;
        }

        var stageId = ReadString(stageValue, "id") ?? Normalize(sourceStageId);
        var routeKind = ReadString(stageValue, "model_route_kind", "modelRouteKind") ?? stageId;
        if (!string.IsNullOrWhiteSpace(routeKind))
        {
            routeKinds.Add(routeKind!);
        }
    }

    private static void AddRouteKindsFromFlattenedStageRegistryValues(
        List<string> routeKinds,
        IReadOnlyDictionary<string, ConfigurationFieldValue> valuesByKey)
    {
        const string prefix = "stage_registry.stages.";
        var stageProperties = new Dictionary<string, Dictionary<string, StructuredValue>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in valuesByKey)
        {
            if (!pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || pair.Value.Value is null)
            {
                continue;
            }

            var suffix = pair.Key[prefix.Length..];
            var separatorIndex = suffix.IndexOf('.');
            if (separatorIndex <= 0 || separatorIndex >= suffix.Length - 1)
            {
                continue;
            }

            var stageId = suffix[..separatorIndex];
            var propertyName = suffix[(separatorIndex + 1)..];
            if (!stageProperties.TryGetValue(stageId, out var properties))
            {
                properties = new Dictionary<string, StructuredValue>(StringComparer.OrdinalIgnoreCase);
                stageProperties[stageId] = properties;
            }

            properties[propertyName] = pair.Value.Value;
        }

        foreach (var pair in stageProperties)
        {
            if (TryReadBoolean(pair.Value, out var enabled, "enabled") && enabled is false)
            {
                continue;
            }

            var routeKind = ReadString(pair.Value, "model_route_kind", "modelRouteKind") ?? pair.Key;
            if (!string.IsNullOrWhiteSpace(routeKind))
            {
                routeKinds.Add(routeKind!);
            }
        }
    }

    private static string? ReadString(StructuredValue value, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (TryReadString(value, propertyName, out var text)
                && !string.IsNullOrWhiteSpace(text))
            {
                return text!.Trim();
            }
        }

        return null;
    }

    private static string? ReadString(
        IReadOnlyDictionary<string, StructuredValue> values,
        params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (values.TryGetValue(propertyName, out var value)
                && ReadScalarString(value) is { } text)
            {
                return text;
            }
        }

        return null;
    }

    private static bool TryReadBoolean(
        StructuredValue value,
        out bool boolean,
        params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!value.TryGetProperty(propertyName, out var property) || property is null)
            {
                continue;
            }

            if (ReadScalarBoolean(property, out boolean))
            {
                return true;
            }
        }

        boolean = false;
        return false;
    }

    private static bool TryReadBoolean(
        IReadOnlyDictionary<string, StructuredValue> values,
        out bool boolean,
        params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (values.TryGetValue(propertyName, out var value)
                && ReadScalarBoolean(value, out boolean))
            {
                return true;
            }
        }

        boolean = false;
        return false;
    }

    private static string? ReadScalarString(StructuredValue value)
    {
        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static bool ReadScalarBoolean(StructuredValue value, out bool boolean)
    {
        if (value.Kind == StructuredValueKind.Boolean && value.BooleanValue.HasValue)
        {
            boolean = value.BooleanValue.Value;
            return true;
        }

        if (value.Kind == StructuredValueKind.String && bool.TryParse(value.StringValue, out boolean))
        {
            return true;
        }

        boolean = false;
        return false;
    }

    private static void ValidateRouteSetReferences(
        IReadOnlyDictionary<string, ConfigurationFieldValue> valuesByKey,
        IReadOnlySet<string> routeSetIds,
        List<ConfigurationIssue> issues)
    {
        foreach (var pair in valuesByKey)
        {
            if (!IsModelRouteSetReferenceKey(pair.Key)
                || pair.Value.Value?.Kind != StructuredValueKind.String
                || string.IsNullOrWhiteSpace(pair.Value.Value.StringValue))
            {
                continue;
            }

            var routeSetId = pair.Value.Value.StringValue;
            if (!routeSetIds.Contains(routeSetId))
            {
                issues.Add(Error(
                    "config.model_route_set.reference_unknown",
                    $"配置键 `{pair.Key}` 引用了不存在的模型路由方案 `{routeSetId}`。",
                    pair.Key,
                    pair.Value.SourceLayerId));
            }
        }
    }

    private static bool IsModelRouteSetReferenceKey(string key)
    {
        var parts = key.Split('.');
        if (parts.Length == 1)
        {
            return string.Equals(parts[0], "model_route_set", StringComparison.OrdinalIgnoreCase);
        }

        return parts.Length == 3
               && string.Equals(parts[2], "model_route_set", StringComparison.OrdinalIgnoreCase)
               && (string.Equals(parts[0], "agents", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(parts[0], "execution_profiles", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(parts[0], "session_profiles", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryReadString(StructuredValue value, string propertyName, out string? text)
    {
        text = null;
        if (value.Kind != StructuredValueKind.Object
            || !value.Properties.TryGetValue(propertyName, out var property)
            || property.Kind != StructuredValueKind.String
            || string.IsNullOrWhiteSpace(property.StringValue))
        {
            return false;
        }

        text = property.StringValue;
        return true;
    }

    private static bool IsStringArray(StructuredValue value)
        => value.Kind == StructuredValueKind.Array
            && value.Items.All(static item => item.Kind == StructuredValueKind.String);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ConfigurationIssue Error(string code, string message, string fieldKey, string? sourceLayerId)
        => new()
        {
            Severity = ConfigurationIssueSeverity.Error,
            Code = code,
            Message = message,
            FieldKey = fieldKey,
            SourceLayerId = sourceLayerId,
        };
}



