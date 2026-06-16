using TianShu.Contracts.Configuration;
using TianShu.Contracts.Primitives;

namespace TianShu.ConfigGui;

internal sealed partial class ConfigGuiState
{
    public const string MemoryProfilesCategoryId = "__memory.profiles";
    public const string MemorySpacesCategoryId = "__memory.spaces";
    public const string MemoryProvidersCategoryId = "__memory.providers";
    public const string MemoryBindingsCategoryId = "__memory.bindings";

    private void AddMemoryNavigationModule()
        => AddNavigationModule(
            "memory",
            "记忆",
            "管理默认本地 Memory 配置、记忆配置文件、空间与绑定规则，作为第三方记忆模块的参考入口。",
            MemoryProfilesCategoryId,
            MemorySpacesCategoryId,
            MemoryProvidersCategoryId,
            MemoryBindingsCategoryId);

    private void AddMemoryPageCategories(IReadOnlyList<ConfigFieldRow> fields)
    {
        AddMemoryPageCategory(MemoryProfilesCategoryId, "记忆配置文件", "选择当前记忆配置文件，并管理 memory_profiles.* 的启用、overlay、抽取和保留策略。", fields);
        AddMemoryPageCategory(MemorySpacesCategoryId, "记忆空间", "管理 memory.spaces.* 的作用域、提供方、只读状态与标签。", fields);
        AddMemoryPageCategory(MemoryProvidersCategoryId, "记忆提供方", "管理 memory.providers.* 默认本地提供方和第三方提供方参考配置。", fields);
        AddMemoryPageCategory(MemoryBindingsCategoryId, "记忆绑定", "管理 memory.bindings.* 的 space、提供方、能力与读写模式。", fields);
    }

    private void AddMemoryPageCategory(string categoryId, string displayName, string description, IReadOnlyList<ConfigFieldRow> fields)
        => AddCategoryRow(categoryId, displayName, description, fields.Count(field => IsMemoryFieldInPage(field, categoryId)));

    private static bool IsMemoryPageCategory(string categoryId)
        => string.Equals(categoryId, MemoryProfilesCategoryId, StringComparison.OrdinalIgnoreCase)
           || string.Equals(categoryId, MemorySpacesCategoryId, StringComparison.OrdinalIgnoreCase)
           || string.Equals(categoryId, MemoryProvidersCategoryId, StringComparison.OrdinalIgnoreCase)
           || string.Equals(categoryId, MemoryBindingsCategoryId, StringComparison.OrdinalIgnoreCase);

    private static bool IsMemoryFieldInPage(ConfigFieldRow row, string categoryId)
    {
        if (!string.Equals(row.CategoryId, ConfigurationCategoryIds.IdentityMemory, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return categoryId switch
        {
            MemoryProfilesCategoryId => string.Equals(row.Key, "memory.enabled", StringComparison.OrdinalIgnoreCase)
                                        || string.Equals(row.Key, "memory.default_profile", StringComparison.OrdinalIgnoreCase)
                                        || row.Key.StartsWith("memory_profiles.", StringComparison.OrdinalIgnoreCase),
            MemorySpacesCategoryId => row.Key.StartsWith("memory.spaces.", StringComparison.OrdinalIgnoreCase),
            MemoryProvidersCategoryId => row.Key.StartsWith("memory.providers.", StringComparison.OrdinalIgnoreCase),
            MemoryBindingsCategoryId => row.Key.StartsWith("memory.bindings.", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private void ApplyMemoryProfileChoices()
    {
        var profileIds = allFields
            .Where(static field => field.IsConfigured)
            .Select(static field => TryExtractMemoryId(field.Key, "memory_profiles."))
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .Select(static id => new AllowedOptionRow(id!, $"选择记忆配置文件：{id}", id!))
            .ToArray();

        ReplaceFieldChoices("memory.default_profile", profileIds);
    }

    public void EnsureDefaultMemoryConfiguration()
    {
        var changes = BuildDefaultMemoryConfigurationChanges(includeExisting: false);
        if (changes.Count == 0)
        {
            StatusText.Value = "默认 Memory 配置已经存在。";
            UpdateContextPanel();
            return;
        }

        ApplyMemoryChanges(changes, "已补齐默认 Memory 配置。");
    }

    public void CreateMemoryConfigurationForCurrentPage()
    {
        if (string.Equals(selectedCategoryId, MemoryProfilesCategoryId, StringComparison.OrdinalIgnoreCase))
        {
            var id = CreateUniqueMemoryId("memory_profiles.", "profile");
            ApplyMemoryChanges(
                [
                    Set($"memory_profiles.{id}.enabled", StructuredValue.FromBoolean(true)),
                    Set($"memory_profiles.{id}.default_space", StructuredValue.FromString("default")),
                    Set($"memory_profiles.{id}.overlay", StructuredValue.FromBoolean(true)),
                    Set($"memory_profiles.{id}.extract", StructuredValue.FromString("manual")),
                    Set($"memory_profiles.{id}.retention", StructuredValue.FromString("keep")),
                ],
                $"已新增记忆配置文件：{id}");
            return;
        }

        if (string.Equals(selectedCategoryId, MemorySpacesCategoryId, StringComparison.OrdinalIgnoreCase))
        {
            var id = CreateUniqueMemoryId("memory.spaces.", "space");
            ApplyMemoryChanges(
                [
                    Set($"memory.spaces.{id}.scope", StructuredValue.FromString("user")),
                    Set($"memory.spaces.{id}.provider", StructuredValue.FromString("local")),
                    Set($"memory.spaces.{id}.read_only", StructuredValue.FromBoolean(false)),
                    Set($"memory.spaces.{id}.tags", ArrayValue(["custom"])),
                ],
                $"已新增 Memory Space：{id}");
            return;
        }

        if (string.Equals(selectedCategoryId, MemoryProvidersCategoryId, StringComparison.OrdinalIgnoreCase))
        {
            var id = CreateUniqueMemoryId("memory.providers.", "provider");
            ApplyMemoryChanges(
                [
                    Set($"memory.providers.{id}.kind", StructuredValue.FromString("local")),
                    Set($"memory.providers.{id}.display_name", StructuredValue.FromString(id)),
                    Set($"memory.providers.{id}.enabled", StructuredValue.FromBoolean(true)),
                    Set($"memory.providers.{id}.mode", StructuredValue.FromString("read-write")),
                    Set($"memory.providers.{id}.root", StructuredValue.FromString($"./data/memory/{id}")),
                    Set($"memory.providers.{id}.capabilities", ArrayValue(["filter", "add", "feedback"])),
                ],
                $"已新增记忆提供方：{id}");
            return;
        }

        if (string.Equals(selectedCategoryId, MemoryBindingsCategoryId, StringComparison.OrdinalIgnoreCase))
        {
            var id = CreateUniqueMemoryId("memory.bindings.", "binding");
            ApplyMemoryChanges(
                [
                    Set($"memory.bindings.{id}.space", StructuredValue.FromString("default")),
                    Set($"memory.bindings.{id}.provider", StructuredValue.FromString("local")),
                    Set($"memory.bindings.{id}.capabilities", ArrayValue(["filter", "add", "feedback"])),
                    Set($"memory.bindings.{id}.mode", StructuredValue.FromString("read-write")),
                ],
                $"已新增 Memory Binding：{id}");
            return;
        }

        EnsureDefaultMemoryConfiguration();
    }

    public void CopyMemoryConfigurationForCurrentPage()
    {
        if (!TryResolveMemoryLifecycle(out var prefix, out var baseId, out var displayName))
        {
            StatusText.Value = "当前页面没有可复制的 Memory 配置对象。";
            UpdateContextPanel();
            return;
        }

        var sourceId = ResolveSelectedConfigObjectId(prefix) ?? ExistingConfigObjectIds(prefix).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            CreateMemoryConfigurationForCurrentPage();
            return;
        }

        var id = CreateUniqueConfigObjectId(prefix, $"{sourceId}-copy");
        var changes = CloneConfigObjectChanges(prefix, sourceId, id);
        if (changes.Count == 0)
        {
            StatusText.Value = $"未找到可复制的{displayName}：{sourceId}";
            UpdateContextPanel();
            return;
        }

        ApplyMemoryChanges(changes, $"已复制{displayName}：{sourceId} -> {id}");
    }

    public void RenameMemoryConfigurationForCurrentPage()
    {
        if (!TryResolveMemoryLifecycle(out var prefix, out _, out var displayName))
        {
            StatusText.Value = "当前页面没有可重命名的 Memory 配置对象。";
            UpdateContextPanel();
            return;
        }

        var sourceId = ResolveSelectedConfigObjectId(prefix) ?? ExistingConfigObjectIds(prefix).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            StatusText.Value = $"没有可重命名的{displayName}。";
            UpdateContextPanel();
            return;
        }

        var id = CreateUniqueConfigObjectId(prefix, $"{sourceId}-renamed");
        var changes = CloneConfigObjectChanges(prefix, sourceId, id);
        if (changes.Count == 0)
        {
            StatusText.Value = $"未找到可重命名的{displayName}：{sourceId}";
            UpdateContextPanel();
            return;
        }

        changes.Add(new ConfigurationChange
        {
            Operation = ConfigurationChangeOperation.Unset,
            Key = $"{prefix}{sourceId}",
        });
        AddMemoryReferenceFallbackChanges(prefix, sourceId, id, changes);
        ApplyMemoryChanges(changes, $"已重命名{displayName}：{sourceId} -> {id}");
    }

    public void DeleteMemoryConfigurationForCurrentPage()
    {
        if (!TryResolveMemoryLifecycle(out var prefix, out _, out var displayName))
        {
            StatusText.Value = "当前页面没有可删除的 Memory 配置对象。";
            UpdateContextPanel();
            return;
        }

        var ids = ExistingConfigObjectIds(prefix).ToArray();
        var id = ResolveSelectedConfigObjectId(prefix) ?? ids.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id))
        {
            StatusText.Value = $"没有可删除的{displayName}。";
            UpdateContextPanel();
            return;
        }

        if (ids.Length <= 1)
        {
            StatusText.Value = $"至少需要保留一个{displayName}，不能删除当前唯一实例。";
            UpdateContextPanel();
            return;
        }

        var nextId = ids.First(candidate => !string.Equals(candidate, id, StringComparison.OrdinalIgnoreCase));
        var changes = new List<ConfigurationChange>
        {
            new()
            {
                Operation = ConfigurationChangeOperation.Unset,
                Key = $"{prefix}{id}",
            },
        };
        AddMemoryReferenceFallbackChanges(prefix, id, nextId, changes);
        ApplyMemoryChanges(changes, $"已删除{displayName}：{id}");
    }

    private IReadOnlyList<ConfigurationChange> BuildDefaultMemoryConfigurationChanges(bool includeExisting)
    {
        var configuredKeys = allFields
            .Where(static field => field.IsConfigured)
            .Select(static field => field.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var changes = new List<ConfigurationChange>
        {
            Set("memory.enabled", StructuredValue.FromBoolean(true)),
            Set("memory.default_profile", StructuredValue.FromString("default")),
            Set("memory_profiles.default.enabled", StructuredValue.FromBoolean(true)),
            Set("memory_profiles.default.default_space", StructuredValue.FromString("default")),
            Set("memory_profiles.default.overlay", StructuredValue.FromBoolean(true)),
            Set("memory_profiles.default.extract", StructuredValue.FromString("manual")),
            Set("memory_profiles.default.retention", StructuredValue.FromString("keep")),
            Set("memory.spaces.default.scope", StructuredValue.FromString("user")),
            Set("memory.spaces.default.provider", StructuredValue.FromString("local")),
            Set("memory.spaces.default.read_only", StructuredValue.FromBoolean(false)),
            Set("memory.spaces.default.tags", ArrayValue(["default", "local"])),
            Set("memory.providers.local.kind", StructuredValue.FromString("local")),
            Set("memory.providers.local.display_name", StructuredValue.FromString("Local Memory")),
            Set("memory.providers.local.enabled", StructuredValue.FromBoolean(true)),
            Set("memory.providers.local.mode", StructuredValue.FromString("read-write")),
            Set("memory.providers.local.root", StructuredValue.FromString("./data/memory")),
            Set("memory.providers.local.capabilities", ArrayValue(["filter", "add", "feedback"])),
            Set("memory.bindings.default.space", StructuredValue.FromString("default")),
            Set("memory.bindings.default.provider", StructuredValue.FromString("local")),
            Set("memory.bindings.default.capabilities", ArrayValue(["filter", "add", "feedback"])),
            Set("memory.bindings.default.mode", StructuredValue.FromString("read-write")),
        };

        return includeExisting
            ? changes
            : changes.Where(change => !configuredKeys.Contains(change.Key)).ToArray();
    }

    private bool TryResolveMemoryLifecycle(out string prefix, out string baseId, out string displayName)
    {
        if (string.Equals(selectedCategoryId, MemoryProfilesCategoryId, StringComparison.OrdinalIgnoreCase))
        {
            prefix = "memory_profiles.";
            baseId = "profile";
            displayName = "记忆配置文件";
            return true;
        }

        if (string.Equals(selectedCategoryId, MemorySpacesCategoryId, StringComparison.OrdinalIgnoreCase))
        {
            prefix = "memory.spaces.";
            baseId = "space";
            displayName = "记忆空间";
            return true;
        }

        if (string.Equals(selectedCategoryId, MemoryProvidersCategoryId, StringComparison.OrdinalIgnoreCase))
        {
            prefix = "memory.providers.";
            baseId = "provider";
            displayName = "记忆提供方";
            return true;
        }

        if (string.Equals(selectedCategoryId, MemoryBindingsCategoryId, StringComparison.OrdinalIgnoreCase))
        {
            prefix = "memory.bindings.";
            baseId = "binding";
            displayName = "记忆绑定";
            return true;
        }

        prefix = string.Empty;
        baseId = string.Empty;
        displayName = string.Empty;
        return false;
    }

    private void AddMemoryReferenceFallbackChanges(string prefix, string deletedId, string nextId, List<ConfigurationChange> changes)
    {
        if (string.Equals(prefix, "memory_profiles.", StringComparison.OrdinalIgnoreCase)
            && string.Equals(ReadConfiguredString("memory.default_profile"), deletedId, StringComparison.OrdinalIgnoreCase))
        {
            changes.Add(Set("memory.default_profile", StructuredValue.FromString(nextId)));
            return;
        }

        if (string.Equals(prefix, "memory.spaces.", StringComparison.OrdinalIgnoreCase))
        {
            AddStringReferenceFallbackChanges(".default_space", deletedId, nextId, changes);
            AddStringReferenceFallbackChanges(".space", deletedId, nextId, changes);
            return;
        }

        if (string.Equals(prefix, "memory.providers.", StringComparison.OrdinalIgnoreCase))
        {
            AddStringReferenceFallbackChanges(".provider", deletedId, nextId, changes);
        }
    }

    private void AddStringReferenceFallbackChanges(string keySuffix, string deletedId, string nextId, List<ConfigurationChange> changes)
    {
        foreach (var field in allFields.Where(field => field.IsConfigured && field.Key.EndsWith(keySuffix, StringComparison.OrdinalIgnoreCase)))
        {
            if (string.Equals(field.CurrentValue, deletedId, StringComparison.OrdinalIgnoreCase))
            {
                changes.Add(Set(field.Key, StructuredValue.FromString(nextId)));
            }
        }
    }

    private void ApplyMemoryChanges(IReadOnlyList<ConfigurationChange> changes, string successMessage)
    {
        if (changes.Count == 0)
        {
            StatusText.Value = successMessage;
            UpdateContextPanel();
            return;
        }

        var result = applier.ApplyRouted(ConfigPath, new ConfigurationChangeSet
        {
            Changes = changes,
        });

        StatusText.Value = result.Applied
            ? successMessage
            : string.Join(Environment.NewLine, result.Issues.Select(static issue => issue.Message));
        Refresh();
    }

    private string CreateUniqueMemoryId(string prefix, string baseId)
    {
        var existingIds = allFields
            .Where(static field => field.IsConfigured)
            .Select(field => TryExtractMemoryId(field.Key, prefix))
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

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

    private static string? TryExtractMemoryId(string key, string prefix)
    {
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rest = key[prefix.Length..];
        var dotIndex = rest.IndexOf('.', StringComparison.Ordinal);
        return dotIndex <= 0 ? null : rest[..dotIndex];
    }

    private static ConfigurationChange Set(string key, StructuredValue value)
        => new()
        {
            Operation = ConfigurationChangeOperation.Set,
            Key = key,
            Value = value,
        };

    private static StructuredValue ArrayValue(IReadOnlyList<string> values)
        => StructuredValue.FromArray(values.Select(StructuredValue.FromString).ToArray());
}
