using TianShu.Contracts.Configuration;
using TianShu.Contracts.Primitives;

namespace TianShu.ConfigGui;

internal sealed partial class ConfigGuiState
{
    private static readonly IReadOnlyDictionary<string, AgentConfigurationLifecycleSpec> AgentLifecycleSpecs =
        new Dictionary<string, AgentConfigurationLifecycleSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["agents."] = new("agents.", "agent", "agent", "Agent", "agent"),
            ["execution_profiles."] = new("execution_profiles.", "execution", "execution", "执行配置文件", "execution"),
            ["session_profiles."] = new("session_profiles.", "session", "session", "会话配置文件", "session"),
            ["conversation_profiles."] = new("conversation_profiles.", "conversation", "conversation", "对话配置文件", "conversation"),
        };

    private void AddAgentNavigationModule()
        => AddNavigationModule(
            "agent",
            "代理",
            "管理 Agent 行为、推理偏好、上下文预算与输出策略。",
            ConfigurationCategoryIds.AgentBehavior);

    public void CreateAgentConfigurationForCurrentSelection()
    {
        var spec = ResolveAgentLifecycleSpec();
        var id = CreateUniqueConfigObjectId(spec.Prefix, spec.BaseId);
        ApplyAgentChanges(BuildDefaultAgentChanges(spec, id), $"已新增{spec.DisplayName}：{id}");
    }

    public void CopyAgentConfigurationForCurrentSelection()
    {
        var spec = ResolveAgentLifecycleSpec();
        var sourceId = ResolveSelectedConfigObjectId(spec.Prefix) ?? ExistingConfigObjectIds(spec.Prefix).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            CreateAgentConfigurationForCurrentSelection();
            return;
        }

        var id = CreateUniqueConfigObjectId(spec.Prefix, $"{sourceId}-copy");
        var changes = CloneConfigObjectChanges(spec.Prefix, sourceId, id);
        if (changes.Count == 0)
        {
            changes = BuildDefaultAgentChanges(spec, id).ToList();
        }

        ApplyAgentChanges(changes, $"已复制{spec.DisplayName}：{sourceId} -> {id}");
    }

    public void DeleteAgentConfigurationForCurrentSelection()
    {
        var spec = ResolveAgentLifecycleSpec();
        var ids = ExistingConfigObjectIds(spec.Prefix).ToArray();
        var id = ResolveSelectedConfigObjectId(spec.Prefix) ?? ids.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id))
        {
            StatusText.Value = $"没有可删除的{spec.DisplayName}。";
            UpdateContextPanel();
            return;
        }

        if (ids.Length <= 1)
        {
            StatusText.Value = $"至少需要保留一个{spec.DisplayName}，不能删除当前唯一实例。";
            UpdateContextPanel();
            return;
        }

        var nextId = ids.First(candidate => !string.Equals(candidate, id, StringComparison.OrdinalIgnoreCase));
        var changes = new List<ConfigurationChange>
        {
            new()
            {
                Operation = ConfigurationChangeOperation.Unset,
                Key = $"{spec.Prefix}{id}",
            },
        };
        var activeProfileId = ResolveActiveProfileId();
        var profileSlotKey = $"profiles.{activeProfileId}.{spec.ProfileSlot}";
        if (string.Equals(ReadConfiguredString(profileSlotKey), id, StringComparison.OrdinalIgnoreCase))
        {
            changes.Add(Set(profileSlotKey, StructuredValue.FromString(nextId)));
        }

        ApplyAgentChanges(changes, $"已删除{spec.DisplayName}：{id}");
    }

    private AgentConfigurationLifecycleSpec ResolveAgentLifecycleSpec()
    {
        var selectedKey = selectedField?.Key ?? string.Empty;
        foreach (var spec in AgentLifecycleSpecs.Values)
        {
            if (selectedKey.StartsWith(spec.Prefix, StringComparison.OrdinalIgnoreCase))
            {
                return spec;
            }
        }

        return AgentLifecycleSpecs["agents."];
    }

    private IReadOnlyList<ConfigurationChange> BuildDefaultAgentChanges(AgentConfigurationLifecycleSpec spec, string id)
        => spec.Prefix switch
        {
            "agents." =>
            [
                Set($"agents.{id}.display_name", StructuredValue.FromString(id)),
                Set($"agents.{id}.model_route_set", StructuredValue.FromString(ReadConfiguredString("model_route_set") ?? "default")),
                Set($"agents.{id}.personality", StructuredValue.FromString("default")),
                Set($"agents.{id}.max_output_tokens", StructuredValue.FromNumber("4096")),
            ],
            "execution_profiles." =>
            [
                Set($"execution_profiles.{id}.agent", StructuredValue.FromString(ExistingConfigObjectIds("agents.").FirstOrDefault() ?? "default")),
                Set($"execution_profiles.{id}.model_route_set", StructuredValue.FromString(ReadConfiguredString("model_route_set") ?? "default")),
                Set($"execution_profiles.{id}.approval", StructuredValue.FromString("on-request")),
                Set($"execution_profiles.{id}.sandbox", StructuredValue.FromString("workspace-write")),
                Set($"execution_profiles.{id}.web_search", StructuredValue.FromString("auto")),
                Set($"execution_profiles.{id}.parallel_tool_calls", StructuredValue.FromBoolean(true)),
            ],
            "session_profiles." =>
            [
                Set($"session_profiles.{id}.model_binding", StructuredValue.FromString("snapshot-on-create")),
                Set($"session_profiles.{id}.memory_mode", StructuredValue.FromString("read-write")),
                Set($"session_profiles.{id}.auto_resume", StructuredValue.FromString("ask")),
            ],
            "conversation_profiles." =>
            [
                Set($"conversation_profiles.{id}.thread_source", StructuredValue.FromString("local")),
                Set($"conversation_profiles.{id}.history", StructuredValue.FromString("sliced")),
                Set($"conversation_profiles.{id}.fuzzy_file_search", StructuredValue.FromBoolean(true)),
                Set($"conversation_profiles.{id}.pending_input_timeout_seconds", StructuredValue.FromNumber("120")),
            ],
            _ => [],
        };

    private void ApplyAgentChanges(IReadOnlyList<ConfigurationChange> changes, string successMessage)
    {
        var result = applier.ApplyRouted(ConfigPath, new ConfigurationChangeSet
        {
            Changes = changes,
        });
        StatusText.Value = result.Applied
            ? successMessage
            : string.Join(Environment.NewLine, result.Issues.Select(static issue => issue.Message));
        Refresh();
    }

    private sealed record AgentConfigurationLifecycleSpec(
        string Prefix,
        string BaseId,
        string ProfileSlot,
        string DisplayName,
        string DefaultReference);
}
