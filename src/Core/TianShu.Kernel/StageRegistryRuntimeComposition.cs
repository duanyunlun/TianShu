using TianShu.Contracts.Catalog;
using TianShu.Contracts.Orchestration;

namespace TianShu.Kernel;

/// <summary>
/// Stage Registry 的运行时组合入口。
/// Runtime composition entry point for the Stage Registry.
/// </summary>
public static class StageRegistryRuntimeComposition
{
    /// <summary>
    /// 创建只包含内置 Stage 的注册表快照。
    /// Creates a registry snapshot that contains only built-in stages.
    /// </summary>
    public static RuntimeStageRegistrySnapshot CreateDefaultRegistry()
        => CreateRegistry(BuiltInStageDefinitions.All);

    /// <summary>
    /// 基于运行时配置创建 Stage Registry，并合并受信扩展 Stage 定义。
    /// Creates a Stage Registry from runtime config and merges trusted extension stage definitions.
    /// </summary>
    public static RuntimeStageRegistrySnapshot CreateRegistryFromConfig(Dictionary<string, object?>? config)
    {
        var extensionIssues = new List<RuntimeStageRegistryIssue>();
        var stageDefinitions = new List<StageDefinition>(BuiltInStageDefinitions.All);
        if (config is { Count: > 0 })
        {
            stageDefinitions.AddRange(ReadTrustedExtensionStages(config, extensionIssues));
        }

        var registry = CreateRegistry(stageDefinitions);
        return registry with
        {
            Issues = extensionIssues.Concat(registry.Issues).ToArray(),
        };
    }

    /// <summary>
    /// 基于传入 Stage 定义创建注册表快照，并返回结构化诊断。
    /// Creates a registry snapshot from stage definitions and returns structured diagnostics.
    /// </summary>
    public static RuntimeStageRegistrySnapshot CreateRegistry(IEnumerable<StageDefinition> stageDefinitions)
    {
        ArgumentNullException.ThrowIfNull(stageDefinitions);

        var issues = new List<RuntimeStageRegistryIssue>();
        var stagesById = new Dictionary<string, StageDefinition>(StringComparer.OrdinalIgnoreCase);
        var registrationOrderByStageId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var registrationIndex = 0;
        foreach (var stage in stageDefinitions)
        {
            if (stage is null)
            {
                issues.Add(new RuntimeStageRegistryIssue("stage_definition_null", "Stage 定义不能为空。"));
                continue;
            }

            if (!stagesById.TryAdd(stage.Id, stage))
            {
                issues.Add(new RuntimeStageRegistryIssue(
                    "duplicate_stage_id",
                    $"Stage `{stage.Id}` 被重复注册。",
                    stage.Id));
                registrationIndex++;
                continue;
            }

            registrationOrderByStageId[stage.Id] = registrationIndex++;
        }

        var transitions = new List<StageTransitionDefinition>();
        var transitionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stage in OrderStages(stagesById.Values, registrationOrderByStageId))
        {
            foreach (var nextStageId in stage.AllowedNext)
            {
                if (!stagesById.ContainsKey(nextStageId))
                {
                    issues.Add(new RuntimeStageRegistryIssue(
                        "transition_target_missing",
                        $"Stage `{stage.Id}` 声明的下一阶段 `{nextStageId}` 未注册。",
                        stage.Id));
                    continue;
                }

                AddTransition(
                    transitions,
                    transitionKeys,
                    stage.Id,
                    nextStageId,
                    "declared-allowed-next",
                    stage.LifecycleOrder);
            }

            foreach (var previousStageId in stage.AllowedPrevious)
            {
                if (!stagesById.ContainsKey(previousStageId))
                {
                    issues.Add(new RuntimeStageRegistryIssue(
                        "transition_source_missing",
                        $"Stage `{stage.Id}` 声明的上一阶段 `{previousStageId}` 未注册。",
                        stage.Id));
                    continue;
                }

                AddTransition(
                    transitions,
                    transitionKeys,
                    previousStageId,
                    stage.Id,
                    "declared-allowed-previous",
                    stage.LifecycleOrder);
            }
        }

        return new RuntimeStageRegistrySnapshot(
            OrderStages(stagesById.Values, registrationOrderByStageId).ToArray(),
            transitions
                .OrderBy(static item => item.Priority)
                .ThenBy(static item => item.FromStageId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.ToStageId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            issues);
    }

    private static IOrderedEnumerable<StageDefinition> OrderStages(
        IEnumerable<StageDefinition> stages,
        IReadOnlyDictionary<string, int> registrationOrderByStageId)
        => stages
            .OrderBy(static item => item.LifecycleOrder)
            .ThenBy(item => registrationOrderByStageId.TryGetValue(item.Id, out var order) ? order : int.MaxValue);

    private static void AddTransition(
        List<StageTransitionDefinition> transitions,
        ISet<string> transitionKeys,
        string fromStageId,
        string toStageId,
        string reasonCode,
        int priority)
    {
        if (!transitionKeys.Add($"{fromStageId}\u001f{toStageId}"))
        {
            return;
        }

        transitions.Add(new StageTransitionDefinition(
            fromStageId,
            toStageId,
            reasonCode,
            priority: priority));
    }

    private static IReadOnlyList<StageDefinition> ReadTrustedExtensionStages(
        Dictionary<string, object?> config,
        List<RuntimeStageRegistryIssue> issues)
    {
        if (!TryReadStageRegistryConfig(config, out var stageRegistryConfig))
        {
            return Array.Empty<StageDefinition>();
        }

        if (ReadBooleanExact(stageRegistryConfig, "enabled") == false)
        {
            issues.Add(new RuntimeStageRegistryIssue(
                "stage_registry_disabled",
                "Stage Registry 扩展配置已禁用。",
                Severity: RuntimeStageRegistryIssueSeverity.Warning));
            return Array.Empty<StageDefinition>();
        }

        if (!TryReadConfiguredStagesValue(config, stageRegistryConfig, out var rawStages))
        {
            return Array.Empty<StageDefinition>();
        }

        if (TryAsDictionary(rawStages, out var stagesById))
        {
            return stagesById
                .Select(pair => ReadTrustedExtensionStage(pair.Value, pair.Key, issues))
                .Where(static stage => stage is not null)
                .Cast<StageDefinition>()
                .ToArray();
        }

        if (rawStages is not string && rawStages is IEnumerable<object?> stageItems)
        {
            var stages = new List<StageDefinition>();
            var index = 0;
            foreach (var rawStage in stageItems)
            {
                var stage = ReadTrustedExtensionStage(rawStage, sourceStageId: null, issues, index);
                if (stage is not null)
                {
                    stages.Add(stage);
                }

                index++;
            }

            return stages;
        }

        issues.Add(new RuntimeStageRegistryIssue(
            "stage_registry_stages_invalid",
            "stage_registry.stages 必须是 Stage 定义对象表或对象数组。"));
        return Array.Empty<StageDefinition>();
    }

    private static bool TryReadStageRegistryConfig(
        Dictionary<string, object?> config,
        out Dictionary<string, object?> stageRegistryConfig)
    {
        if (TryReadObjectExact(config, "stage_registry", out stageRegistryConfig))
        {
            return true;
        }

        if (!TryReadValueExact(config, "stage_registry.stages", out var rawStages))
        {
            stageRegistryConfig = null!;
            return false;
        }

        stageRegistryConfig = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["stages"] = rawStages,
        };
        return true;
    }

    private static bool TryReadConfiguredStagesValue(
        Dictionary<string, object?> rootConfig,
        Dictionary<string, object?> stageRegistryConfig,
        out object? rawStages)
    {
        return TryReadValueExact(stageRegistryConfig, "stages", out rawStages)
               || TryReadNestedValueExact(rootConfig, ["stage_registry", "stages"], out rawStages)
               || TryReadValueExact(rootConfig, "stage_registry.stages", out rawStages);
    }

    private static StageDefinition? ReadTrustedExtensionStage(
        object? rawStage,
        string? sourceStageId,
        List<RuntimeStageRegistryIssue> issues,
        int? index = null)
    {
        if (!TryAsDictionary(rawStage, out var stageConfig))
        {
            issues.Add(new RuntimeStageRegistryIssue(
                "extension_stage_invalid",
                $"扩展 Stage `{FormatStageSource(sourceStageId, index)}` 必须是对象。",
                sourceStageId));
            return null;
        }

        var id = Normalize(ReadStringExact(stageConfig, "id"))
                 ?? Normalize(sourceStageId);
        if (id is null)
        {
            issues.Add(new RuntimeStageRegistryIssue(
                "extension_stage_id_missing",
                $"扩展 Stage `{FormatStageSource(sourceStageId, index)}` 缺少 id。"));
            return null;
        }

        if (ReadBooleanExact(stageConfig, "enabled") == false)
        {
            issues.Add(new RuntimeStageRegistryIssue(
                "extension_stage_disabled",
                $"扩展 Stage `{id}` 已禁用。",
                id,
                RuntimeStageRegistryIssueSeverity.Warning));
            return null;
        }

        if (!TryReadRequiredInt(stageConfig, "lifecycle_order", out var lifecycleOrder))
        {
            issues.Add(new RuntimeStageRegistryIssue(
                "extension_stage_lifecycle_order_missing",
                $"扩展 Stage `{id}` 缺少 lifecycle_order。",
                id));
            return null;
        }

        var contextProjectionMode = StageContextProjectionMode.SelectedSegments;
        var rawProjectionMode = Normalize(ReadStringExact(stageConfig, "context_projection_mode"));
        if (rawProjectionMode is not null
            && !TryParseContextProjectionMode(rawProjectionMode, out contextProjectionMode))
        {
            issues.Add(new RuntimeStageRegistryIssue(
                "extension_stage_context_projection_mode_invalid",
                $"扩展 Stage `{id}` 的 context_projection_mode `{rawProjectionMode}` 无效。",
                id));
            return null;
        }

        try
        {
            var modelRouteKind = Normalize(ReadStringExact(stageConfig, "model_route_kind"));
            return new StageDefinition(
                id,
                Normalize(ReadStringExact(stageConfig, "display_name")) ?? id,
                lifecycleOrder,
                modelRouteKind is null ? null : new ModelRouteKind(modelRouteKind),
                allowedPrevious: ReadStringArray(stageConfig, "allowed_previous"),
                allowedNext: ReadStringArray(stageConfig, "allowed_next"),
                requiredCapabilities: ReadStringArray(stageConfig, "required_capabilities"),
                contextProjectionMode: contextProjectionMode,
                executorBinding: Normalize(ReadStringExact(stageConfig, "executor_binding")));
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            issues.Add(new RuntimeStageRegistryIssue(
                "extension_stage_definition_invalid",
                $"扩展 Stage `{id}` 定义无效：{ex.Message}",
                id));
            return null;
        }
    }

    private static bool TryReadRequiredInt(
        Dictionary<string, object?> config,
        string propertyName,
        out int value)
    {
        if (TryReadValueExact(config, propertyName, out var rawValue)
            && TryReadInt(rawValue, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static IReadOnlyList<string> ReadStringArray(
        Dictionary<string, object?> config,
        params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (TryReadValueExact(config, propertyName, out var rawValue)
                && TryReadStringArray(rawValue, out var values))
            {
                return values;
            }
        }

        return Array.Empty<string>();
    }

    private static bool TryParseContextProjectionMode(string value, out StageContextProjectionMode mode)
    {
        var normalized = value.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        foreach (var candidate in Enum.GetValues<StageContextProjectionMode>())
        {
            var candidateName = candidate.ToString().Replace("_", string.Empty, StringComparison.Ordinal);
            if (string.Equals(candidateName, normalized, StringComparison.OrdinalIgnoreCase))
            {
                mode = candidate;
                return true;
            }
        }

        mode = default;
        return false;
    }

    private static string FormatStageSource(string? sourceStageId, int? index)
        => Normalize(sourceStageId) ?? (index.HasValue ? $"#{index.Value + 1}" : "<unknown>");

    private static string? ReadStringExact(Dictionary<string, object?> config, string key)
        => TryReadValueExact(config, key, out var value)
           && TryReadString(value, out var text)
            ? text
            : null;

    private static bool? ReadBooleanExact(Dictionary<string, object?> config, string key)
        => TryReadValueExact(config, key, out var value)
           && TryReadBoolean(value, out var booleanValue)
            ? booleanValue
            : null;

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

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

    private static bool TryReadBoolean(object? value, out bool booleanValue)
    {
        switch (value)
        {
            case bool native:
                booleanValue = native;
                return true;
            case string text when bool.TryParse(text, out var parsed):
                booleanValue = parsed;
                return true;
            default:
                booleanValue = default;
                return false;
        }
    }

    private static bool TryReadInt(object? value, out int intValue)
    {
        switch (value)
        {
            case int native:
                intValue = native;
                return true;
            case long native when native is >= int.MinValue and <= int.MaxValue:
                intValue = (int)native;
                return true;
            case double native when native % 1 == 0 && native is >= int.MinValue and <= int.MaxValue:
                intValue = (int)native;
                return true;
            case string text when int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed):
                intValue = parsed;
                return true;
            default:
                intValue = default;
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
}

/// <summary>
/// Stage Registry 的运行时快照。
/// Runtime snapshot of the Stage Registry.
/// </summary>
public sealed record RuntimeStageRegistrySnapshot(
    IReadOnlyList<StageDefinition> Stages,
    IReadOnlyList<StageTransitionDefinition> Transitions,
    IReadOnlyList<RuntimeStageRegistryIssue> Issues)
{
    /// <summary>
    /// 注册表是否没有诊断错误。
    /// Whether the registry has no diagnostic errors.
    /// </summary>
    public bool IsValid => !Issues.Any(static issue => issue.Severity == RuntimeStageRegistryIssueSeverity.Error);

    /// <summary>
    /// 根据 Stage id 查找定义。
    /// Finds a definition by stage id.
    /// </summary>
    public StageDefinition? FindStage(string stageId)
        => string.IsNullOrWhiteSpace(stageId)
            ? null
            : Stages.FirstOrDefault(stage => string.Equals(stage.Id, stageId.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Stage Registry 诊断问题。
/// Diagnostic issue emitted while building the Stage Registry.
/// </summary>
public sealed record RuntimeStageRegistryIssue(
    string Code,
    string Message,
    string? StageId = null,
    RuntimeStageRegistryIssueSeverity Severity = RuntimeStageRegistryIssueSeverity.Error);

/// <summary>
/// Stage Registry 诊断级别。
/// Diagnostic severity emitted while building the Stage Registry.
/// </summary>
public enum RuntimeStageRegistryIssueSeverity
{
    Error = 0,
    Warning = 1,
}
