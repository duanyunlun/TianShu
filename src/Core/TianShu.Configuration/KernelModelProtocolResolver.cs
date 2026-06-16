using System.Text.RegularExpressions;
using TianShu.Provider.Abstractions;

namespace TianShu.Configuration;

/// <summary>
/// 解析单个模型在当前 provider 下应使用的 wire protocol。
/// Resolves the wire protocol that a specific model should use for the active provider.
/// </summary>
public static class KernelModelProtocolResolver
{
    public static string ResolveModelProtocol(
        Dictionary<string, object?> config,
        string? modelProvider,
        string? model,
        string? snapshotProtocol = null)
        => ResolveModelProtocolCandidates(config, modelProvider, model, snapshotProtocol)[0];

    public static IReadOnlyList<string> ResolveModelProtocolCandidates(
        Dictionary<string, object?> config,
        string? modelProvider,
        string? model,
        string? snapshotProtocol = null)
    {
        var snapshot = Normalize(snapshotProtocol);
        if (snapshot is not null
            && !string.Equals(snapshot, "<default>", StringComparison.OrdinalIgnoreCase)
            && !IsAuto(snapshot))
        {
            return [NormalizeProtocol(snapshot, "session provider protocol")];
        }

        var normalizedProvider = Normalize(modelProvider);
        var normalizedModel = Normalize(model);
        var providerConfig = ReadProviderConfig(config, normalizedProvider);

        if (TryResolveModelOverride(providerConfig, normalizedModel, out var overrideProtocols))
        {
            return AppendProviderFallbacks(overrideProtocols, providerConfig);
        }

        if (TryResolveTopLevelModelOverride(config, normalizedModel, out overrideProtocols))
        {
            return AppendProviderFallbacks(overrideProtocols, providerConfig);
        }

        if (TryResolveProtocolRule(providerConfig, normalizedModel, out var ruleProtocols))
        {
            return AppendProviderFallbacks(ruleProtocols, providerConfig);
        }

        if (TryResolveDefaultProtocolRule(config, normalizedModel, out var defaultRuleProtocols))
        {
            return AppendProviderFallbacks(defaultRuleProtocols, providerConfig);
        }

        var defaultProtocol = ReadProviderString(providerConfig, "default_protocol");
        if (!string.IsNullOrWhiteSpace(defaultProtocol) && !IsAuto(defaultProtocol))
        {
            return AppendProviderFallbacks(
                [NormalizeProtocol(defaultProtocol, "providers.<id>.default_protocol")],
                providerConfig);
        }

        if (TryResolveBuiltInProtocol(normalizedModel, out var builtInProtocol))
        {
            return AppendProviderFallbacks([builtInProtocol], providerConfig);
        }

        if (TryResolveFallbackProtocols(providerConfig, out var fallbackProtocols))
        {
            return fallbackProtocols;
        }

        return [ProviderWireApi.OpenAiChatCompletions];
    }

    private static Dictionary<string, object?>? ReadProviderConfig(
        Dictionary<string, object?> config,
        string? modelProvider)
    {
        if (string.IsNullOrWhiteSpace(modelProvider))
        {
            return null;
        }

        return TryReadNestedValueExact(config, ["providers", modelProvider!], out var providerRaw)
               && TryAsDictionary(providerRaw, out var provider)
            ? provider
            : null;
    }

    private static bool TryResolveModelOverride(
        Dictionary<string, object?>? providerConfig,
        string? model,
        out IReadOnlyList<string> protocols)
    {
        protocols = [];
        if (providerConfig is null || string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        if (!TryReadValueExact(providerConfig, "model_overrides", out var rawOverrides)
            || rawOverrides is not IEnumerable<object?> overrides)
        {
            return false;
        }

        foreach (var item in overrides)
        {
            if (!TryAsDictionary(item, out var overrideConfig))
            {
                continue;
            }

            var name = ReadStringExact(overrideConfig, "name", "model");
            if (!string.Equals(name, model, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryReadProtocolPriority(overrideConfig, "providers.<id>.model_overrides[].protocols", out protocols))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool TryResolveTopLevelModelOverride(
        Dictionary<string, object?> config,
        string? model,
        out IReadOnlyList<string> protocols)
    {
        protocols = [];
        if (string.IsNullOrWhiteSpace(model)
            || !TryReadValueExact(config, "models", out var rawModels)
            || !TryAsDictionary(rawModels, out var models))
        {
            return false;
        }

        foreach (var pair in models)
        {
            if (!TryAsDictionary(pair.Value, out var modelConfig))
            {
                continue;
            }

            var configuredName = ReadStringExact(modelConfig, "name", "model") ?? pair.Key;
            if (!string.Equals(configuredName, model, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(pair.Key, model, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryReadProtocolPriority(modelConfig, "models.<id>.protocols", out protocols))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool TryResolveProtocolRule(
        Dictionary<string, object?>? providerConfig,
        string? model,
        out IReadOnlyList<string> protocols)
    {
        protocols = [];
        if (providerConfig is null || string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        if (!TryReadValueExact(providerConfig, "protocol_rules", out var rawRules)
            || rawRules is not IEnumerable<object?> rules)
        {
            return false;
        }

        foreach (var item in rules)
        {
            if (!TryAsDictionary(item, out var ruleConfig))
            {
                continue;
            }

            var pattern = ReadStringExact(ruleConfig, "match", "pattern");
            if (!IsGlobMatch(model!, pattern))
            {
                continue;
            }

            if (!TryReadProtocolPriority(ruleConfig, "providers.<id>.protocol_rules[].protocols", out protocols))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool TryResolveDefaultProtocolRule(
        Dictionary<string, object?> config,
        string? model,
        out IReadOnlyList<string> protocols)
    {
        protocols = [];
        if (string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        var ruleSetId = Normalize(ReadStringExact(config, "model_protocol_rule_set")) ?? "default";
        if (!TryReadNestedValueExact(config, ["model_protocol_rule_sets", ruleSetId, "rules"], out var rawRules)
            || rawRules is not IEnumerable<object?> rules)
        {
            return false;
        }

        foreach (var item in rules)
        {
            if (!TryAsDictionary(item, out var ruleConfig))
            {
                continue;
            }

            var pattern = ReadStringExact(ruleConfig, "match", "pattern");
            if (!IsGlobMatch(model!, pattern))
            {
                continue;
            }

            if (!TryReadProtocolPriority(ruleConfig, "model_protocol_rule_sets.<id>.rules[].protocols", out protocols))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool TryResolveBuiltInProtocol(string? model, out string protocol)
    {
        protocol = string.Empty;
        if (string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        var normalized = model!.Trim().ToLowerInvariant();
        if (normalized.Contains("claude", StringComparison.Ordinal)
            || normalized.StartsWith("anthropic/", StringComparison.Ordinal))
        {
            protocol = ProviderWireApi.AnthropicMessages;
            return true;
        }

        if (normalized.Contains("gemini", StringComparison.Ordinal)
            || normalized.StartsWith("google/", StringComparison.Ordinal))
        {
            protocol = ProviderWireApi.GoogleGenerative;
            return true;
        }

        if (normalized.StartsWith("gpt", StringComparison.Ordinal)
            || normalized.StartsWith("openai/", StringComparison.Ordinal)
            || Regex.IsMatch(normalized, @"^o[1-9]($|[-_.])", RegexOptions.CultureInvariant))
        {
            protocol = ProviderWireApi.Responses;
            return true;
        }

        if (normalized.Contains("deepseek", StringComparison.Ordinal))
        {
            protocol = ProviderWireApi.AnthropicMessages;
            return true;
        }

        if (normalized.Contains("qwen", StringComparison.Ordinal)
            || normalized.Contains("kimi", StringComparison.Ordinal)
            || normalized.Contains("moonshot", StringComparison.Ordinal)
            || normalized.Contains("glm", StringComparison.Ordinal)
            || normalized.Contains("minimax", StringComparison.Ordinal)
            || normalized.Contains("mimo", StringComparison.Ordinal)
            || normalized.Contains("grok", StringComparison.Ordinal)
            || normalized.Contains("yi-", StringComparison.Ordinal)
            || normalized.Contains("baichuan", StringComparison.Ordinal)
            || normalized.Contains("doubao", StringComparison.Ordinal))
        {
            protocol = ProviderWireApi.AnthropicMessages;
            return true;
        }

        return false;
    }

    private static bool TryResolveFallbackProtocols(Dictionary<string, object?>? providerConfig, out IReadOnlyList<string> protocols)
    {
        protocols = [];
        if (providerConfig is null
            || !TryReadValueExact(providerConfig, "protocol_fallbacks", out var rawFallbacks)
            || !TryReadStringArray(rawFallbacks, out var fallbacks))
        {
            return false;
        }

        protocols = NormalizeProtocolPriority(fallbacks, "providers.<id>.protocol_fallbacks[]");
        return protocols.Count > 0;
    }

    private static bool TryReadProtocolPriority(
        Dictionary<string, object?> config,
        string source,
        out IReadOnlyList<string> protocols)
    {
        protocols = [];
        if (TryReadValueExact(config, "protocols", out var rawProtocols)
            && TryReadStringArray(rawProtocols, out var priority))
        {
            protocols = NormalizeProtocolPriority(priority, source);
            if (protocols.Count > 0)
            {
                return true;
            }
        }

        var rawProtocol = ReadStringExact(config, "default_protocol");
        if (string.IsNullOrWhiteSpace(rawProtocol))
        {
            return false;
        }

        protocols = NormalizeProtocolPriority([rawProtocol], source);
        return protocols.Count > 0;
    }

    private static IReadOnlyList<string> AppendProviderFallbacks(
        IReadOnlyList<string> primaryProtocols,
        Dictionary<string, object?>? providerConfig)
    {
        if (!TryResolveFallbackProtocols(providerConfig, out var fallbackProtocols))
        {
            return primaryProtocols;
        }

        return NormalizeProtocolPriority(
            primaryProtocols.Concat(fallbackProtocols),
            "providers.<id>.protocol priority");
    }

    private static string? ReadProviderString(Dictionary<string, object?>? providerConfig, params string[] names)
        => providerConfig is null ? null : ReadStringExact(providerConfig, names);

    private static string? ReadStringExact(Dictionary<string, object?> config, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (TryReadValueExact(config, key, out var value)
                && TryReadString(value, out var text))
            {
                return text;
            }
        }

        return null;
    }

    private static bool TryReadValueExact(Dictionary<string, object?> config, string key, out object? value)
        => config.TryGetValue(key, out value);

    private static bool TryReadNestedValueExact(
        Dictionary<string, object?> config,
        IReadOnlyList<string> path,
        out object? value)
    {
        var current = config;
        for (var index = 0; index < path.Count; index++)
        {
            if (!TryReadValueExact(current, path[index], out value))
            {
                return false;
            }

            if (index == path.Count - 1)
            {
                return true;
            }

            if (!TryAsDictionary(value, out current))
            {
                value = null;
                return false;
            }
        }

        value = null;
        return false;
    }

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
            case int or long or float or double or decimal or bool:
                text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                return true;
            default:
                text = string.Empty;
                return false;
        }
    }

    private static bool TryReadStringArray(object? value, out IReadOnlyList<string> values)
    {
        if (value is string)
        {
            values = Array.Empty<string>();
            return false;
        }

        if (value is IEnumerable<string> strings)
        {
            values = strings
                .Select(Normalize)
                .Where(static item => item is not null)
                .Cast<string>()
                .ToArray();
            return true;
        }

        if (value is IEnumerable<object?> objects)
        {
            values = objects
                .Select(static item => TryReadString(item, out var text) ? Normalize(text) : null)
                .Where(static item => item is not null)
                .Cast<string>()
                .ToArray();
            return true;
        }

        values = Array.Empty<string>();
        return false;
    }

    private static bool IsGlobMatch(string value, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        if (string.Equals(pattern, "*", StringComparison.Ordinal))
        {
            return true;
        }

        var regex = "^" + Regex.Escape(pattern.Trim())
            .Replace("\\*", ".*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string NormalizeProtocol(string value, string source)
    {
        var normalized = ProviderWireApi.NormalizeOrThrow(value, source);
        return normalized ?? ProviderWireApi.OpenAiChatCompletions;
    }

    private static IReadOnlyList<string> NormalizeProtocolPriority(IEnumerable<string?> values, string source)
    {
        var protocols = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value) || IsAuto(value!))
            {
                continue;
            }

            var protocol = NormalizeProtocol(value!, source);
            if (seen.Add(protocol))
            {
                protocols.Add(protocol);
            }
        }

        return protocols;
    }

    private static bool IsAuto(string value)
        => string.Equals(value.Trim(), "auto", StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
