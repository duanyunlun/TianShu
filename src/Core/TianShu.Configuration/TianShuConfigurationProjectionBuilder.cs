using System.Globalization;
using TianShu.Contracts.Configuration;
using TianShu.Contracts.Primitives;

namespace TianShu.Configuration;

/// <summary>
/// 从已解析配置层构造 typed 配置投影。
/// Builds typed configuration projections from resolved configuration layers.
/// </summary>
public sealed class TianShuConfigurationProjectionBuilder
{
    private readonly ITianShuConfigurationSchemaCatalog schemaCatalog;

    public TianShuConfigurationProjectionBuilder(ITianShuConfigurationSchemaCatalog? schemaCatalog = null)
    {
        this.schemaCatalog = schemaCatalog ?? new TianShuConfigurationSchemaCatalog();
    }

    public ConfigurationProjection Build(ConfigurationProjectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var schema = schemaCatalog.GetSnapshot();
        var catalogFields = schema.Fields.ToArray();
        var groupsById = schema.Groups.ToDictionary(static group => group.Id, StringComparer.OrdinalIgnoreCase);
        var categoriesById = schema.Categories.ToDictionary(static category => category.Id, StringComparer.OrdinalIgnoreCase);
        var fields = catalogFields.ToDictionary(static field => field.Key, StringComparer.OrdinalIgnoreCase);
        var valuesByKey = new Dictionary<string, ConfigurationFieldValue>(StringComparer.OrdinalIgnoreCase);
        var issues = new List<ConfigurationIssue>();

        foreach (var layer in request.Layers.OrderBy(static layer => layer.Source.Order))
        {
            foreach (var pair in layer.Values)
            {
                var key = pair.Key.Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (!fields.TryGetValue(key, out var field))
                {
                    field = TryCreateWildcardField(key, catalogFields) ?? CreateUnmappedField(key);
                    fields[key] = field;
                    if (field.GroupId == TianShuConfigurationSchemaCatalog.RawUnmappedGroupId)
                    {
                        issues.Add(new ConfigurationIssue
                        {
                            Severity = ConfigurationIssueSeverity.Info,
                            Code = ConfigurationIssueCodes.FieldUnmapped,
                            Message = $"配置键 `{key}` 尚未被 schema catalog 精确覆盖，已按未映射配置展示。",
                            FieldKey = key,
                            SourceLayerId = layer.Source.Id,
                        });
                    }
                }

                var isSensitive = IsSensitive(field, key);
                valuesByKey[key] = new ConfigurationFieldValue
                {
                    Key = key,
                    Value = isSensitive ? StructuredValue.FromString("<redacted>") : pair.Value,
                    DefaultValue = field.DefaultValue,
                    SourceLayerId = layer.Source.Id,
                    IsConfigured = true,
                    IsSensitive = isSensitive,
                };
            }
        }

        foreach (var field in fields.Values.Where(static field => !field.Key.Contains('*', StringComparison.Ordinal)))
        {
            if (!valuesByKey.ContainsKey(field.Key))
            {
                valuesByKey[field.Key] = new ConfigurationFieldValue
                {
                    Key = field.Key,
                    Value = field.DefaultValue,
                    DefaultValue = field.DefaultValue,
                    IsConfigured = false,
                    IsSensitive = IsSensitive(field, field.Key),
                };
            }
        }

        TianShuModelRouteSetConfiguration.AddVirtualDefaultRouteSetIfMissing(valuesByKey);
        foreach (var key in valuesByKey.Keys.ToArray())
        {
            if (!fields.ContainsKey(key))
            {
                fields[key] = TryCreateWildcardField(key, catalogFields) ?? CreateUnmappedField(key);
            }
        }

        var providerOptions = BuildProviderReferenceOptions(valuesByKey);
        if (providerOptions.Count > 0)
        {
            foreach (var field in fields.Values.Where(static field => !field.Key.Contains('*', StringComparison.Ordinal)).ToArray())
            {
                if (IsModelProviderReferenceKey(field.Key))
                {
                    fields[field.Key] = field with { AllowedValues = providerOptions };
                }
            }
        }

        issues.AddRange(TianShuModelRouteSetConfiguration.Validate(valuesByKey));
        issues.AddRange(ValidateFormalSchema(valuesByKey, fields));

        return new ConfigurationProjection
        {
            Profile = request.Profile ?? TryReadProfile(valuesByKey),
            Categories = schema.Categories.OrderBy(static category => category.Order).ThenBy(static category => category.Id, StringComparer.Ordinal).ToArray(),
            Groups = schema.Groups.OrderBy(static group => group.Order).ThenBy(static group => group.Id, StringComparer.Ordinal).ToArray(),
            Fields = fields.Values
                .Where(static field => !field.Key.Contains('*', StringComparison.Ordinal))
                .OrderBy(field => GetCategoryOrder(field, groupsById, categoriesById))
                .ThenBy(field => GetGroupOrder(field, groupsById))
                .ThenBy(static field => field.Key, StringComparer.Ordinal)
                .ToArray(),
            Values = valuesByKey.Values.OrderBy(static value => value.Key, StringComparer.Ordinal).ToArray(),
            Sources = request.Layers.Select(static layer => layer.Source).OrderBy(static source => source.Order).ToArray(),
            Issues = issues,
        };
    }

    private static ConfigurationFieldDescriptor CreateUnmappedField(string key)
        => new()
        {
            Key = key,
            GroupId = TianShuConfigurationSchemaCatalog.RawUnmappedGroupId,
            DisplayName = key,
            Description = "该键来自配置文件，但当前 schema catalog 尚未提供专门字段描述。",
            ValueKind = GuessValueKind(key),
            EditMode = ConfigurationFieldEditMode.RequiresPreview,
            IsSecret = IsSensitiveKey(key),
            IsAdvanced = true,
        };

    private static int GetCategoryOrder(
        ConfigurationFieldDescriptor field,
        IReadOnlyDictionary<string, ConfigurationGroupDescriptor> groupsById,
        IReadOnlyDictionary<string, ConfigurationCategoryDescriptor> categoriesById)
        => groupsById.TryGetValue(field.GroupId, out var group)
            && categoriesById.TryGetValue(group.CategoryId, out var category)
                ? category.Order
                : int.MaxValue;

    private static int GetGroupOrder(
        ConfigurationFieldDescriptor field,
        IReadOnlyDictionary<string, ConfigurationGroupDescriptor> groupsById)
        => groupsById.TryGetValue(field.GroupId, out var group)
            ? group.Order
            : int.MaxValue;

    private static ConfigurationFieldDescriptor? TryCreateWildcardField(string key, IReadOnlyList<ConfigurationFieldDescriptor> catalogFields)
    {
        foreach (var template in catalogFields.Where(static field => field.Key.Contains('*', StringComparison.Ordinal)))
        {
            var patternParts = template.Key.Split('.');
            var keyParts = key.Split('.');
            if (patternParts.Length != keyParts.Length)
            {
                continue;
            }

            var matches = true;
            for (var index = 0; index < patternParts.Length; index++)
            {
                if (patternParts[index] == "*")
                {
                    continue;
                }

                if (!string.Equals(patternParts[index], keyParts[index], StringComparison.OrdinalIgnoreCase))
                {
                    matches = false;
                    break;
                }
            }

            if (!matches)
            {
                continue;
            }

            var wildcardValue = keyParts[Array.IndexOf(patternParts, "*")];
            return template with
            {
                Key = key,
                DisplayName = template.DisplayName.Contains(wildcardValue, StringComparison.OrdinalIgnoreCase)
                    ? template.DisplayName
                    : $"{wildcardValue} / {template.DisplayName}",
            };
        }

        return null;
    }

    private static ConfigurationValueKind GuessValueKind(string key)
    {
        if (key.EndsWith("_path", StringComparison.OrdinalIgnoreCase)
            || key.EndsWith("_file", StringComparison.OrdinalIgnoreCase)
            || key.EndsWith(".path", StringComparison.OrdinalIgnoreCase)
            || key.EndsWith(".file", StringComparison.OrdinalIgnoreCase))
        {
            return ConfigurationValueKind.Path;
        }

        if (key.EndsWith("_enabled", StringComparison.OrdinalIgnoreCase)
            || key.EndsWith(".enabled", StringComparison.OrdinalIgnoreCase))
        {
            return ConfigurationValueKind.Boolean;
        }

        if (key.EndsWith("_env", StringComparison.OrdinalIgnoreCase)
            || key.EndsWith(".env", StringComparison.OrdinalIgnoreCase))
        {
            return ConfigurationValueKind.SecretReference;
        }

        return ConfigurationValueKind.String;
    }

    private static bool IsSensitive(ConfigurationFieldDescriptor field, string key)
        => field.IsSecret && !IsSecretReferenceKey(key)
            || IsSensitiveKey(key) && !IsSecretReferenceKey(key) && !IsTokenBudgetKey(field, key);

    private static bool IsSensitiveKey(string key)
        => key.Contains("api_key", StringComparison.OrdinalIgnoreCase)
            || key.Contains("token", StringComparison.OrdinalIgnoreCase)
            || key.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || key.Contains("password", StringComparison.OrdinalIgnoreCase)
            || key.Contains("authorization", StringComparison.OrdinalIgnoreCase);

    private static bool IsTokenBudgetKey(ConfigurationFieldDescriptor field, string key)
        => field.ValueKind is ConfigurationValueKind.Integer or ConfigurationValueKind.Number
           && (key.Contains("token_budget", StringComparison.OrdinalIgnoreCase)
               || key.EndsWith("_tokens", StringComparison.OrdinalIgnoreCase)
               || key.EndsWith(".tokens", StringComparison.OrdinalIgnoreCase)
               || key.EndsWith("_budget_tokens", StringComparison.OrdinalIgnoreCase)
               || key.EndsWith(".budget_tokens", StringComparison.OrdinalIgnoreCase));

    private static bool IsSecretReferenceKey(string key)
        => key.EndsWith("_env", StringComparison.OrdinalIgnoreCase)
            || key.EndsWith(".env", StringComparison.OrdinalIgnoreCase)
            || key.EndsWith("_file", StringComparison.OrdinalIgnoreCase)
            || key.EndsWith(".file", StringComparison.OrdinalIgnoreCase)
            || key.EndsWith("_ref", StringComparison.OrdinalIgnoreCase)
            || key.EndsWith(".ref", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<ConfigurationIssue> ValidateFormalSchema(
        IReadOnlyDictionary<string, ConfigurationFieldValue> valuesByKey,
        IReadOnlyDictionary<string, ConfigurationFieldDescriptor> fields)
    {
        var issues = new List<ConfigurationIssue>();

        foreach (var value in valuesByKey.Values.Where(static value => value.IsConfigured))
        {
            if (!fields.TryGetValue(value.Key, out var field) || value.Value is null)
            {
                continue;
            }

            issues.AddRange(ValidateValueKind(field, value));
            issues.AddRange(ValidateAllowedValues(field, value));
            issues.AddRange(ValidateBudget(field, value));
            issues.AddRange(ValidateSecretReference(field, value));
        }

        issues.AddRange(ValidateMutualExclusion(valuesByKey));
        issues.AddRange(ValidateRequiredFields(valuesByKey));
        return issues;
    }

    private static IEnumerable<ConfigurationIssue> ValidateValueKind(
        ConfigurationFieldDescriptor field,
        ConfigurationFieldValue value)
    {
        if (value.Value is null || IsValueKindCompatible(field.ValueKind, value.Value))
        {
            yield break;
        }

        yield return new ConfigurationIssue
        {
            Severity = ConfigurationIssueSeverity.Error,
            Code = ConfigurationIssueCodes.ValueKindInvalid,
            Message = $"配置键 `{value.Key}` 的值类型与 schema 不一致，期望 `{field.ValueKind}`。",
            FieldKey = value.Key,
            SourceLayerId = value.SourceLayerId,
        };
    }

    private static bool IsValueKindCompatible(ConfigurationValueKind expectedKind, StructuredValue value)
        => expectedKind switch
        {
            ConfigurationValueKind.String or ConfigurationValueKind.Path or ConfigurationValueKind.SecretReference => value.Kind == StructuredValueKind.String,
            ConfigurationValueKind.Boolean => value.Kind == StructuredValueKind.Boolean,
            ConfigurationValueKind.Integer => value.Kind == StructuredValueKind.Number
                                              && long.TryParse(value.NumberValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            ConfigurationValueKind.Number => value.Kind == StructuredValueKind.Number
                                             && decimal.TryParse(value.NumberValue, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
            ConfigurationValueKind.Enum => value.Kind == StructuredValueKind.String,
            ConfigurationValueKind.Array => value.Kind == StructuredValueKind.Array,
            ConfigurationValueKind.Object => value.Kind == StructuredValueKind.Object,
            ConfigurationValueKind.Duration => value.Kind is StructuredValueKind.String or StructuredValueKind.Number,
            _ => true,
        };

    private static IEnumerable<ConfigurationIssue> ValidateAllowedValues(
        ConfigurationFieldDescriptor field,
        ConfigurationFieldValue value)
    {
        if (field.ValueKind != ConfigurationValueKind.Enum
            || field.AllowedValues.Count == 0
            || value.Value?.Kind != StructuredValueKind.String)
        {
            yield break;
        }

        var text = value.Value.StringValue;
        if (field.AllowedValues.Any(allowed => string.Equals(allowed.Value.StringValue, text, StringComparison.OrdinalIgnoreCase)))
        {
            yield break;
        }

        yield return new ConfigurationIssue
        {
            Severity = ConfigurationIssueSeverity.Error,
            Code = ConfigurationIssueCodes.EnumInvalid,
            Message = $"配置键 `{value.Key}` 的枚举值 `{text}` 不在 schema 允许范围内。",
            FieldKey = value.Key,
            SourceLayerId = value.SourceLayerId,
        };
    }

    private static IEnumerable<ConfigurationIssue> ValidateBudget(
        ConfigurationFieldDescriptor field,
        ConfigurationFieldValue value)
    {
        if (value.Value?.Kind != StructuredValueKind.Number || !IsBudgetLikeKey(value.Key))
        {
            yield break;
        }

        if (decimal.TryParse(value.Value.NumberValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && number >= 0
            && (!value.Key.EndsWith(".max_parallelism", StringComparison.OrdinalIgnoreCase) || number >= 1))
        {
            yield break;
        }

        var message = value.Key.EndsWith(".max_parallelism", StringComparison.OrdinalIgnoreCase)
            ? $"配置键 `{value.Key}` 必须大于等于 1。"
            : $"配置键 `{value.Key}` 必须是非负预算。";
        yield return new ConfigurationIssue
        {
            Severity = ConfigurationIssueSeverity.Error,
            Code = ConfigurationIssueCodes.BudgetNegative,
            Message = message,
            FieldKey = value.Key,
            SourceLayerId = value.SourceLayerId,
        };
    }

    private static bool IsBudgetLikeKey(string key)
        => key.Contains("budget", StringComparison.OrdinalIgnoreCase)
            || key.EndsWith("_timeout_ms", StringComparison.OrdinalIgnoreCase)
            || key.EndsWith(".timeout_ms", StringComparison.OrdinalIgnoreCase)
            || key.EndsWith(".max_parallelism", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<ConfigurationIssue> ValidateSecretReference(
        ConfigurationFieldDescriptor field,
        ConfigurationFieldValue value)
    {
        if (!IsSensitiveKey(value.Key))
        {
            yield break;
        }

        if (IsSecretReferenceKey(value.Key))
        {
            if (value.Value?.Kind == StructuredValueKind.String && !string.IsNullOrWhiteSpace(value.Value.StringValue))
            {
                yield break;
            }

            yield return new ConfigurationIssue
            {
                Severity = ConfigurationIssueSeverity.Error,
                Code = ConfigurationIssueCodes.SecretReferenceMissing,
                Message = $"配置键 `{value.Key}` 必须提供非空 secret reference。",
                FieldKey = value.Key,
                SourceLayerId = value.SourceLayerId,
            };
            yield break;
        }

        yield return new ConfigurationIssue
        {
            Severity = ConfigurationIssueSeverity.Error,
            Code = ConfigurationIssueCodes.SecretPlaintextForbidden,
            Message = $"配置键 `{value.Key}` 疑似包含 secret 明文，正式 schema 只允许 secret reference。",
            FieldKey = value.Key,
            SourceLayerId = value.SourceLayerId,
        };
    }

    private static IEnumerable<ConfigurationIssue> ValidateMutualExclusion(
        IReadOnlyDictionary<string, ConfigurationFieldValue> valuesByKey)
    {
        foreach (var envValue in valuesByKey.Values.Where(static value => value.IsConfigured && value.Key.EndsWith(".api_key_env", StringComparison.OrdinalIgnoreCase)))
        {
            var secretKey = envValue.Key[..^".api_key_env".Length] + ".api_key_secret";
            if (!valuesByKey.TryGetValue(secretKey, out var secretValue) || !secretValue.IsConfigured)
            {
                continue;
            }

            yield return new ConfigurationIssue
            {
                Severity = ConfigurationIssueSeverity.Error,
                Code = ConfigurationIssueCodes.MutualExclusionConflict,
                Message = $"配置键 `{envValue.Key}` 与 `{secretKey}` 互斥，只能配置一种 secret reference。",
                FieldKey = envValue.Key,
                SourceLayerId = envValue.SourceLayerId,
            };
        }
    }

    private static IEnumerable<ConfigurationIssue> ValidateRequiredFields(
        IReadOnlyDictionary<string, ConfigurationFieldValue> valuesByKey)
    {
        foreach (var enabledValue in valuesByKey.Values.Where(static value =>
                     value.IsConfigured
                     && value.Key.StartsWith("modules.", StringComparison.OrdinalIgnoreCase)
                     && value.Key.EndsWith(".enabled", StringComparison.OrdinalIgnoreCase)))
        {
            if (ReadBoolean(enabledValue.Value) != true)
            {
                continue;
            }

            var prefix = enabledValue.Key[..^".enabled".Length];
            var descriptorRefKey = $"{prefix}.descriptor_ref";
            if (valuesByKey.TryGetValue(descriptorRefKey, out var descriptorValue)
                && descriptorValue.IsConfigured
                && descriptorValue.Value?.Kind == StructuredValueKind.String
                && !string.IsNullOrWhiteSpace(descriptorValue.Value.StringValue))
            {
                continue;
            }

            yield return new ConfigurationIssue
            {
                Severity = ConfigurationIssueSeverity.Error,
                Code = ConfigurationIssueCodes.RequiredFieldMissing,
                Message = $"配置键 `{enabledValue.Key}` 启用模块时必须同时配置 `{descriptorRefKey}`。",
                FieldKey = descriptorRefKey,
                SourceLayerId = enabledValue.SourceLayerId,
            };
        }
    }

    private static bool? ReadBoolean(StructuredValue? value)
        => value?.Kind == StructuredValueKind.Boolean ? value.BooleanValue : null;

    private static IReadOnlyList<ConfigurationAllowedValue> BuildProviderReferenceOptions(
        IReadOnlyDictionary<string, ConfigurationFieldValue> valuesByKey)
    {
        var providerIds = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var displayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in valuesByKey)
        {
            if (TryReadProviderId(pair.Key, out var providerId))
            {
                providerIds.Add(providerId);
            }

            if (TryReadProviderDisplayName(pair.Key, out providerId)
                && pair.Value.Value?.Kind == StructuredValueKind.String
                && !string.IsNullOrWhiteSpace(pair.Value.Value.StringValue))
            {
                displayNames[providerId] = pair.Value.Value.StringValue;
            }
        }

        return providerIds
            .Select(providerId => new ConfigurationAllowedValue
            {
                Value = StructuredValue.FromString(providerId),
                DisplayName = displayNames.TryGetValue(providerId, out var displayName)
                    ? $"{providerId} ({displayName})"
                    : providerId,
                Description = $"使用 providers.{providerId} 下定义的模型 provider 连接配置。",
            })
            .ToArray();
    }

    private static bool IsModelProviderReferenceKey(string key)
        => string.Equals(key, "provider", StringComparison.OrdinalIgnoreCase)
            || IsScopedModelProviderReferenceKey(key, "agents")
            || IsScopedModelProviderReferenceKey(key, "execution_profiles")
            || IsScopedModelProviderReferenceKey(key, "models")
            || IsScopedModelProviderReferenceKey(key, "realtime_profiles");

    private static bool IsScopedModelProviderReferenceKey(string key, string prefix)
    {
        var parts = key.Split('.');
        return parts.Length == 3
            && string.Equals(parts[0], prefix, StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[2], "provider", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadProviderId(string key, out string providerId)
    {
        providerId = string.Empty;
        var parts = key.Split('.');
        if (parts.Length < 3 || !string.Equals(parts[0], "providers", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        providerId = parts[1];
        return !string.IsNullOrWhiteSpace(providerId);
    }

    private static bool TryReadProviderDisplayName(string key, out string providerId)
    {
        providerId = string.Empty;
        var parts = key.Split('.');
        if (parts.Length != 3
            || !string.Equals(parts[0], "providers", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(parts[2], "display_name", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        providerId = parts[1];
        return !string.IsNullOrWhiteSpace(providerId);
    }

    private static string? TryReadProfile(IReadOnlyDictionary<string, ConfigurationFieldValue> valuesByKey)
        => valuesByKey.TryGetValue("profile", out var value) && value.Value?.Kind == StructuredValueKind.String
            ? value.Value.StringValue
            : null;
}
