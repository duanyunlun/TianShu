using TianShu.Contracts.Configuration;
using TianShu.Contracts.Primitives;

namespace TianShu.ConfigGui;

internal sealed partial class ConfigGuiState
{
    public const string ProfileCompositionCurrentCategoryId = "__profile_composition.current";
    public const string ProfileCompositionBindingsCategoryId = "__profile_composition.bindings";
    public const string ProfileCompositionStagesCategoryId = "__profile_composition.stages";
    public const string ProfileCompositionPreviewCategoryId = "__profile_composition.preview";
    public const string ProfileCompositionValidationCategoryId = "__profile_composition.validation";

    private static readonly IReadOnlyDictionary<string, string> ProfileBindingTargetPrefixes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["agent"] = "agents.",
        ["execution"] = "execution_profiles.",
        ["conversation"] = "conversation_profiles.",
        ["permissions"] = "permission_profiles.",
        ["model_route_set"] = "model_route_sets.",
        ["memory"] = "memory_profiles.",
        ["tools"] = "tool_profiles.",
        ["tui"] = "tui_profiles.",
        ["workspace"] = "workspace_profiles.",
        ["session"] = "session_profiles.",
        ["collaboration"] = "collaboration_profiles.",
        ["workflow"] = "workflow_profiles.",
        ["identity"] = "identity_profiles.",
        ["governance"] = "governance_profiles.",
        ["features"] = "feature_profiles.",
        ["realtime"] = "realtime_profiles.",
    };

    private static readonly IReadOnlyList<string> ProfileCompositionStageKeys =
    [
        "planning",
        "execution",
        "review",
        "summary",
    ];

    private void AddProfileCompositionNavigationModule()
        => AddNavigationModule(
            "profile_composition",
            "配置方案编排",
            "选择当前配置方案，并把各模块配置文件组合成一个可运行方案。",
            ProfileCompositionCurrentCategoryId,
            ProfileCompositionBindingsCategoryId,
            ProfileCompositionStagesCategoryId,
            ProfileCompositionPreviewCategoryId,
            ProfileCompositionValidationCategoryId);

    private void AddProfileCompositionPageCategories(IReadOnlyList<ConfigFieldRow> fields)
    {
        AddProfileCompositionPageCategory(ProfileCompositionCurrentCategoryId, "当前配置方案", "选择当前默认启用的配置方案。", fields);
        AddProfileCompositionPageCategory(ProfileCompositionBindingsCategoryId, "模块绑定", "选择该配置方案绑定的模型、记忆、提示词、技能、工具、工作空间与审批策略配置文件。", fields);
        AddProfileCompositionPageCategory(ProfileCompositionStagesCategoryId, "阶段编排", "后续用于为规划、执行、总结等阶段绑定不同模块组合。", fields);
        AddProfileCompositionPageCategory(ProfileCompositionPreviewCategoryId, "有效配置预览", "后续展示当前方案最终解析出的完整有效配置。", fields);
        AddProfileCompositionPageCategory(ProfileCompositionValidationCategoryId, "完整性检查", "后续检查引用缺失、模块缺失、schema 错位和循环继承。", fields);
    }

    private void AddProfileCompositionProjectionFields()
    {
        var activeProfileId = ReadConfiguredString("profile");
        var profileIds = ExtractConfiguredProfileIds().ToArray();
        if (string.IsNullOrWhiteSpace(activeProfileId))
        {
            activeProfileId = profileIds.FirstOrDefault() ?? "default";
        }

        var activePrefix = $"profiles.{activeProfileId}.";
        AddProfileCompositionInheritanceField(activeProfileId, profileIds);
        AddProfileCompositionStageFields(activeProfileId, profileIds);

        foreach (var slot in ProfileBindingTargetPrefixes.Keys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase))
        {
            var value = ReadConfiguredString($"{activePrefix}{slot}");
            AddProfileCompositionVirtualField(
                $"__profile_composition.preview.{slot}",
                $"{GetProfileBindingSlotDisplayName(slot)}",
                $"当前配置方案 `{activeProfileId}` 的 `{slot}` 槽位最终引用值。",
                ProfileCompositionPreviewCategoryId,
                "有效配置预览",
                string.IsNullOrWhiteSpace(value) ? "<未绑定>" : value,
                string.IsNullOrWhiteSpace(value) ? "未配置该槽位。" : $"来自 {activePrefix}{slot}");
        }

        foreach (var stage in ProfileCompositionStageKeys)
        {
            var value = ReadConfiguredString($"{activePrefix}stages.{stage}");
            AddProfileCompositionVirtualField(
                $"__profile_composition.preview.stages.{stage}",
                $"{GetProfileStageDisplayName(stage)}",
                $"当前配置方案 `{activeProfileId}` 的 `{stage}` 阶段绑定。",
                ProfileCompositionPreviewCategoryId,
                "有效配置预览",
                string.IsNullOrWhiteSpace(value) ? "<继承当前配置方案>" : value,
                string.IsNullOrWhiteSpace(value) ? "未显式配置阶段绑定，运行时应继承当前配置方案。" : $"来自 {activePrefix}stages.{stage}");
        }

        var issues = BuildProfileCompositionValidationRows(activeProfileId).ToArray();
        if (issues.Length == 0)
        {
            AddProfileCompositionVirtualField(
                "__profile_composition.validation.ok",
                "检查结果",
                "当前配置方案引用完整性检查结果。",
                ProfileCompositionValidationCategoryId,
                "完整性检查",
                "通过",
                "未发现缺失引用、空引用或继承引用问题。");
            return;
        }

        foreach (var issue in issues)
        {
            AddProfileCompositionVirtualField(
                $"__profile_composition.validation.{issue.Key}",
                issue.DisplayName,
                issue.Description,
                ProfileCompositionValidationCategoryId,
                "完整性检查",
                issue.Value,
                issue.Issue);
        }
    }

    private void ApplyProfileCompositionChoices()
    {
        var profileIds = allFields
            .Where(static field => field.IsConfigured)
            .Select(static field => TryExtractProfileId(field.Key))
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .Select(static id => new AllowedOptionRow(id!, $"选择配置方案：{id}", id!))
            .ToArray();

        ReplaceFieldChoices("profile", profileIds);
        ReplaceProfileCompositionFieldChoices(
            static field => field.Key.StartsWith("profiles.", StringComparison.OrdinalIgnoreCase)
                            && field.Key.EndsWith(".extends", StringComparison.OrdinalIgnoreCase),
            BuildProfileInheritanceOptions(profileIds, ReadConfiguredString("profile")));
        ReplaceProfileCompositionFieldChoices(
            static field => IsProfileStageField(field),
            BuildProfileCompositionOptions(profileIds, "选择阶段配置方案"));

        foreach (var (slot, prefix) in ProfileBindingTargetPrefixes)
        {
            ReplaceProfileCompositionFieldChoices(
                field => IsProfileBindingSlot(field.Key, slot),
                BuildProfileBindingOptions(slot, prefix));
        }
    }

    public void SaveCurrentProfileSelection()
    {
        var profileField = allFields.FirstOrDefault(static field => string.Equals(field.Key, "profile", StringComparison.OrdinalIgnoreCase));
        if (profileField is null)
        {
            StatusText.Value = "未找到当前配置方案字段。";
            UpdateContextPanel();
            return;
        }

        var selectedProfileId = string.Equals(selectedField?.Key, "profile", StringComparison.OrdinalIgnoreCase)
            ? SelectedEditValue.Value
            : profileField.EditValue;
        if (string.IsNullOrWhiteSpace(selectedProfileId))
        {
            StatusText.Value = "当前配置方案不能为空。";
            UpdateContextPanel();
            return;
        }

        var result = applier.Apply(ConfigPath, new ConfigurationChangeSet
        {
            Changes = [Set("profile", StructuredValue.FromString(selectedProfileId))],
        });
        StatusText.Value = result.Applied
            ? $"已保存当前配置方案：{selectedProfileId}"
            : string.Join(Environment.NewLine, result.Issues.Select(static issue => issue.Message));
        Refresh();
    }

    public void CreateProfileCompositionProfile()
    {
        var sourceProfileId = ResolveActiveProfileId();
        var newProfileId = CreateUniqueProfileId("custom");
        var changes = new List<ConfigurationChange>
        {
            Set("profile", StructuredValue.FromString(newProfileId)),
            Set($"profiles.{newProfileId}.description", StructuredValue.FromString($"通过 ConfigGUI 新增的配置方案：{newProfileId}")),
        };

        foreach (var slot in ProfileBindingTargetPrefixes.Keys)
        {
            var value = ReadConfiguredString($"profiles.{sourceProfileId}.{slot}");
            if (!string.IsNullOrWhiteSpace(value))
            {
                changes.Add(Set($"profiles.{newProfileId}.{slot}", StructuredValue.FromString(value)));
            }
        }

        foreach (var stage in ProfileCompositionStageKeys)
        {
            var value = ReadConfiguredString($"profiles.{sourceProfileId}.stages.{stage}");
            if (!string.IsNullOrWhiteSpace(value))
            {
                changes.Add(Set($"profiles.{newProfileId}.stages.{stage}", StructuredValue.FromString(value)));
            }
        }

        ApplyProfileCompositionChanges(changes, $"已新增并切换到配置方案：{newProfileId}");
    }

    public void DeleteCurrentProfileCompositionProfile()
    {
        var profileIds = ExtractConfiguredProfileIds().ToArray();
        var activeProfileId = ResolveActiveProfileId();
        if (profileIds.Length <= 1)
        {
            StatusText.Value = "至少需要保留一个配置方案，不能删除当前唯一方案。";
            UpdateContextPanel();
            return;
        }

        if (!profileIds.Contains(activeProfileId, StringComparer.OrdinalIgnoreCase))
        {
            StatusText.Value = $"当前配置方案不存在，无法删除：{activeProfileId}";
            UpdateContextPanel();
            return;
        }

        var nextProfileId = profileIds.First(id => !string.Equals(id, activeProfileId, StringComparison.OrdinalIgnoreCase));
        ApplyProfileCompositionChanges(
            [
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Unset,
                    Key = $"profiles.{activeProfileId}",
                },
                Set("profile", StructuredValue.FromString(nextProfileId)),
            ],
            $"已删除配置方案：{activeProfileId}，当前切换为：{nextProfileId}");
    }

    private void AddProfileCompositionPageCategory(string categoryId, string displayName, string description, IReadOnlyList<ConfigFieldRow> fields)
        => AddCategoryRow(categoryId, displayName, description, fields.Count(field => IsProfileCompositionFieldInPage(field, categoryId)));

    private static bool IsProfileCompositionPageCategory(string categoryId)
        => string.Equals(categoryId, ProfileCompositionCurrentCategoryId, StringComparison.OrdinalIgnoreCase)
           || string.Equals(categoryId, ProfileCompositionBindingsCategoryId, StringComparison.OrdinalIgnoreCase)
           || string.Equals(categoryId, ProfileCompositionStagesCategoryId, StringComparison.OrdinalIgnoreCase)
           || string.Equals(categoryId, ProfileCompositionPreviewCategoryId, StringComparison.OrdinalIgnoreCase)
           || string.Equals(categoryId, ProfileCompositionValidationCategoryId, StringComparison.OrdinalIgnoreCase);

    private static bool IsProfileCompositionFieldInPage(ConfigFieldRow row, string categoryId)
        => categoryId switch
        {
            ProfileCompositionCurrentCategoryId => string.Equals(row.Key, "profile", StringComparison.OrdinalIgnoreCase)
                                                   || string.Equals(row.CategoryId, ProfileCompositionCurrentCategoryId, StringComparison.OrdinalIgnoreCase)
                                                      && row.Key.StartsWith("profiles.", StringComparison.OrdinalIgnoreCase)
                                                      && row.Key.EndsWith(".extends", StringComparison.OrdinalIgnoreCase),
            ProfileCompositionBindingsCategoryId => IsProfileBindingField(row),
            ProfileCompositionStagesCategoryId => IsProfileStageField(row),
            ProfileCompositionPreviewCategoryId => row.Key.StartsWith("__profile_composition.preview.", StringComparison.OrdinalIgnoreCase),
            ProfileCompositionValidationCategoryId => row.Key.StartsWith("__profile_composition.validation.", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };

    private static bool IsProfileCompositionField(ConfigFieldRow row)
        => string.Equals(row.Key, "profile", StringComparison.OrdinalIgnoreCase)
           || row.Key.StartsWith("profiles.", StringComparison.OrdinalIgnoreCase)
           || row.Key.StartsWith("__profile_composition.", StringComparison.OrdinalIgnoreCase);

    private static bool IsProfileBindingField(ConfigFieldRow row)
    {
        if (!row.Key.StartsWith("profiles.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = row.Key.Split('.');
        return parts.Length == 3
               && !string.Equals(parts[2], "description", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(parts[2], "extends", StringComparison.OrdinalIgnoreCase)
               && ProfileBindingTargetPrefixes.ContainsKey(parts[2]);
    }

    private void ReplaceProfileCompositionFieldChoices(Func<ConfigFieldRow, bool> predicate, IReadOnlyList<AllowedOptionRow> choices)
    {
        foreach (var field in allFields.Where(predicate))
        {
            field.ReplaceAllowedOptions(EnsureCurrentValueOption(field, choices));
        }
    }

    private IReadOnlyList<AllowedOptionRow> BuildProfileBindingOptions(string slot, string targetPrefix)
        => allFields
            .Where(static field => field.IsConfigured)
            .Select(field => TryExtractWildcardInstanceId(field.Key, targetPrefix))
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .Select(id => new AllowedOptionRow(id!, $"选择 {slot} 槽位引用的配置文件：{id}", id!))
            .ToArray();

    private static IReadOnlyList<AllowedOptionRow> BuildProfileCompositionOptions(
        IReadOnlyList<AllowedOptionRow> source,
        string descriptionPrefix)
        => source
            .Select(option => new AllowedOptionRow(option.Value, $"{descriptionPrefix}：{option.Value}", option.DisplayLabel))
            .ToArray();

    private static IReadOnlyList<AllowedOptionRow> BuildProfileInheritanceOptions(IReadOnlyList<AllowedOptionRow> source, string? activeProfileId)
        => new[] { new AllowedOptionRow(string.Empty, "不继承其它配置方案。", "不继承") }
            .Concat(source
                .Where(option => !string.Equals(option.Value, activeProfileId, StringComparison.OrdinalIgnoreCase))
                .Select(option => new AllowedOptionRow(option.Value, $"选择继承配置方案：{option.Value}", option.DisplayLabel)))
            .ToArray();

    private static IReadOnlyList<AllowedOptionRow> EnsureCurrentValueOption(ConfigFieldRow field, IReadOnlyList<AllowedOptionRow> choices)
    {
        if (string.IsNullOrWhiteSpace(field.EditValue)
            || choices.Any(option => string.Equals(option.Value, field.EditValue, StringComparison.OrdinalIgnoreCase)))
        {
            return choices;
        }

        return choices
            .Append(new AllowedOptionRow(field.EditValue, $"当前已填写的引用值：{field.EditValue}", field.EditValue))
            .OrderBy(static option => option.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsProfileBindingSlot(string key, string slot)
    {
        var parts = key.Split('.');
        return parts.Length == 3
               && string.Equals(parts[0], "profiles", StringComparison.OrdinalIgnoreCase)
               && string.Equals(parts[2], slot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProfileStageField(ConfigFieldRow row)
    {
        var parts = row.Key.Split('.');
        return parts.Length == 4
               && string.Equals(parts[0], "profiles", StringComparison.OrdinalIgnoreCase)
               && string.Equals(parts[2], "stages", StringComparison.OrdinalIgnoreCase);
    }

    private void AddProfileCompositionStageFields(string activeProfileId, IReadOnlyList<string> profileIds)
    {
        var options = profileIds
            .Select(id => new AllowedOptionRow(id, $"选择阶段配置方案：{id}", id))
            .ToArray();

        foreach (var stage in ProfileCompositionStageKeys)
        {
            var key = $"profiles.{activeProfileId}.stages.{stage}";
            var existing = allFields.FirstOrDefault(field => string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                existing.ReplaceAllowedOptions(EnsureCurrentValueOption(existing, options));
                continue;
            }

            allFields.Add(new ConfigFieldRow(
                key,
                GetProfileStageDisplayName(stage),
                $"为 `{activeProfileId}` 配置方案设置 {GetProfileStageDisplayName(stage)} 使用的配置方案；留空时继承当前配置方案。",
                ProfileCompositionStagesCategoryId,
                "配置方案编排",
                1,
                "profile",
                "阶段编排",
                0,
                ConfigurationValueKind.String,
                ConfigurationFieldEditMode.Editable,
                false,
                options,
                false,
                false,
                "默认值",
                string.Empty,
                string.Empty,
                "未显式配置时继承当前配置方案。"));
        }
    }

    private void AddProfileCompositionInheritanceField(string activeProfileId, IReadOnlyList<string> profileIds)
    {
        var key = $"profiles.{activeProfileId}.extends";
        var options = new[] { new AllowedOptionRow(string.Empty, "不继承其它配置方案。", "不继承") }
            .Concat(profileIds
            .Where(id => !string.Equals(id, activeProfileId, StringComparison.OrdinalIgnoreCase))
            .Select(id => new AllowedOptionRow(id, $"选择继承配置方案：{id}", id)))
            .ToArray();
        var existing = allFields.FirstOrDefault(field => string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            var normalized = new ConfigFieldRow(
                existing.Key,
                "继承配置方案",
                $"为 `{activeProfileId}` 配置方案选择继承的基线配置方案；留空表示不继承。",
                ProfileCompositionCurrentCategoryId,
                "配置方案编排",
                1,
                existing.GroupId,
                "当前配置方案",
                1,
                existing.ValueKind,
                existing.EditMode,
                existing.IsAdvanced,
                EnsureCurrentValueOption(existing, options),
                existing.IsConfigured,
                existing.IsSensitive,
                existing.SourceName,
                existing.CurrentValue,
                existing.DefaultValue,
                string.IsNullOrWhiteSpace(existing.Issues) ? "留空表示当前配置方案不继承其它配置方案。" : existing.Issues);
            allFields.Remove(existing);
            allFields.Add(normalized);
            return;
        }

        allFields.Add(new ConfigFieldRow(
            key,
            "继承配置方案",
            $"为 `{activeProfileId}` 配置方案选择继承的基线配置方案；留空表示不继承。",
            ProfileCompositionCurrentCategoryId,
            "配置方案编排",
            1,
            "profile",
            "当前配置方案",
            1,
            ConfigurationValueKind.String,
            ConfigurationFieldEditMode.Editable,
            false,
            options,
            false,
            false,
            "默认值",
            string.Empty,
            string.Empty,
            "留空表示当前配置方案不继承其它配置方案。"));
    }

    private void AddProfileCompositionVirtualField(
        string key,
        string displayName,
        string description,
        string categoryId,
        string groupName,
        string currentValue,
        string issues)
        => allFields.Add(new ConfigFieldRow(
            key,
            displayName,
            description,
            categoryId,
            "配置方案编排",
            1,
            "profile",
            groupName,
            0,
            ConfigurationValueKind.String,
            ConfigurationFieldEditMode.ReadOnly,
            false,
            [],
            false,
            false,
            "配置方案投影",
            currentValue,
            string.Empty,
            issues));

    private IEnumerable<ProfileCompositionValidationRow> BuildProfileCompositionValidationRows(string activeProfileId)
    {
        var profileIds = ExtractConfiguredProfileIds().ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!profileIds.Contains(activeProfileId))
        {
            yield return new ProfileCompositionValidationRow(
                "active_profile_missing",
                "当前配置方案缺失",
                "当前配置方案引用不存在。",
                activeProfileId,
                $"profile = `{activeProfileId}`，但未找到 profiles.{activeProfileId}.*。");
        }

        var activePrefix = $"profiles.{activeProfileId}.";
        foreach (var (slot, targetPrefix) in ProfileBindingTargetPrefixes.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var key = $"{activePrefix}{slot}";
            var value = ReadConfiguredString(key);
            if (string.IsNullOrWhiteSpace(value))
            {
                yield return new ProfileCompositionValidationRow(
                    $"missing_{slot}",
                    $"{GetProfileBindingSlotDisplayName(slot)}未绑定",
                    $"配置方案 `{activeProfileId}` 未设置 `{slot}` 槽位。",
                    "<未绑定>",
                    $"未配置 {key}。");
                continue;
            }

            if (!TargetInstanceExists(targetPrefix, value))
            {
                yield return new ProfileCompositionValidationRow(
                    $"target_missing_{slot}",
                    $"{GetProfileBindingSlotDisplayName(slot)}目标缺失",
                    $"配置方案 `{activeProfileId}` 的 `{slot}` 槽位引用不存在。",
                    value,
                    $"{key} = `{value}`，但未找到 {targetPrefix}{value}.*。");
            }
        }

        var extends = ReadConfiguredString($"{activePrefix}extends");
        if (!string.IsNullOrWhiteSpace(extends) && !profileIds.Contains(extends))
        {
            yield return new ProfileCompositionValidationRow(
                "extends_missing",
                "继承配置方案缺失",
                "继承引用不存在。",
                extends,
                $"{activePrefix}extends = `{extends}`，但未找到 profiles.{extends}.*。");
        }

        foreach (var stage in ProfileCompositionStageKeys)
        {
            var value = ReadConfiguredString($"{activePrefix}stages.{stage}");
            if (!string.IsNullOrWhiteSpace(value) && !profileIds.Contains(value))
            {
                yield return new ProfileCompositionValidationRow(
                    $"stage_missing_{stage}",
                    $"{GetProfileStageDisplayName(stage)}目标缺失",
                    $"阶段 `{stage}` 引用的配置方案不存在。",
                    value,
                    $"{activePrefix}stages.{stage} = `{value}`，但未找到 profiles.{value}.*。");
            }
        }
    }

    private IEnumerable<string> ExtractConfiguredProfileIds()
        => allFields
            .Where(static field => field.IsConfigured)
            .Select(static field => TryExtractProfileId(field.Key))
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase);

    private void ApplyProfileCompositionChanges(IReadOnlyList<ConfigurationChange> changes, string successMessage)
    {
        var result = applier.Apply(ConfigPath, new ConfigurationChangeSet
        {
            Changes = changes,
        });
        StatusText.Value = result.Applied
            ? successMessage
            : string.Join(Environment.NewLine, result.Issues.Select(static issue => issue.Message));
        Refresh();
    }

    private string ResolveActiveProfileId()
    {
        var activeProfileId = ReadConfiguredString("profile");
        if (!string.IsNullOrWhiteSpace(activeProfileId))
        {
            return activeProfileId;
        }

        return ExtractConfiguredProfileIds().FirstOrDefault() ?? "default";
    }

    private string CreateUniqueProfileId(string baseId)
    {
        var existingIds = ExtractConfiguredProfileIds().ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < 1000; index++)
        {
            var candidate = index == 0 ? baseId : $"{baseId}-{index + 1}";
            if (!existingIds.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{baseId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    }

    private string? ReadConfiguredString(string key)
        => allFields.FirstOrDefault(field => string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase))?.EditValue;

    private bool TargetInstanceExists(string targetPrefix, string targetId)
        => allFields
            .Where(static field => field.IsConfigured)
            .Select(field => TryExtractWildcardInstanceId(field.Key, targetPrefix))
            .Any(id => string.Equals(id, targetId, StringComparison.OrdinalIgnoreCase));

    private static string GetProfileBindingSlotDisplayName(string slot)
        => slot.ToLowerInvariant() switch
        {
            "agent" => "Agent 配置文件",
            "execution" => "执行配置文件",
            "conversation" => "对话配置文件",
            "permissions" => "权限配置文件",
            "model_route_set" => "模型路由方案",
            "memory" => "记忆配置文件",
            "tools" => "工具配置文件",
            "tui" => "TUI 配置文件",
            "workspace" => "工作空间配置文件",
            "session" => "会话配置文件",
            "collaboration" => "协作配置文件",
            "workflow" => "工作流配置文件",
            "identity" => "身份配置文件",
            "governance" => "治理配置文件",
            "features" => "功能配置文件",
            "realtime" => "Realtime 配置文件",
            _ => $"{slot} 配置文件",
        };

    private static string GetProfileStageDisplayName(string stage)
        => stage.ToLowerInvariant() switch
        {
            "planning" => "规划阶段",
            "execution" => "执行阶段",
            "review" => "审阅阶段",
            "summary" => "总结阶段",
            _ => stage,
        };

    private static string? TryExtractProfileId(string key)
        => TryExtractWildcardInstanceId(key, "profiles.");

    private static string? TryExtractWildcardInstanceId(string key, string prefix)
    {
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rest = key[prefix.Length..];
        var dotIndex = rest.IndexOf('.', StringComparison.Ordinal);
        return dotIndex <= 0 ? null : rest[..dotIndex];
    }

    private sealed record ProfileCompositionValidationRow(
        string Key,
        string DisplayName,
        string Description,
        string Value,
        string Issue);
}
