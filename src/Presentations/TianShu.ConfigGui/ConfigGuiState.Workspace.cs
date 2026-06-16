using TianShu.Contracts.Configuration;
using TianShu.Contracts.Primitives;

namespace TianShu.ConfigGui;

internal sealed partial class ConfigGuiState
{
    public void CreateWorkspaceConfigurationForCurrentSelection()
    {
        var spec = ResolveWorkspaceLifecycleSpec();
        var id = CreateUniqueConfigObjectId(spec.Prefix, spec.BaseId);
        ApplyWorkspaceChanges(BuildDefaultWorkspaceChanges(spec, id), $"已新增{spec.DisplayName}：{id}");
    }

    public void CopyWorkspaceConfigurationForCurrentSelection()
    {
        var spec = ResolveWorkspaceLifecycleSpec();
        var sourceId = ResolveSelectedConfigObjectId(spec.Prefix) ?? ExistingConfigObjectIds(spec.Prefix).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            CreateWorkspaceConfigurationForCurrentSelection();
            return;
        }

        var id = CreateUniqueConfigObjectId(spec.Prefix, $"{sourceId}-copy");
        var changes = CloneConfigObjectChanges(spec.Prefix, sourceId, id);
        if (changes.Count == 0)
        {
            changes = BuildDefaultWorkspaceChanges(spec, id).ToList();
        }

        ApplyWorkspaceChanges(changes, $"已复制{spec.DisplayName}：{sourceId} -> {id}");
    }

    public void DeleteWorkspaceConfigurationForCurrentSelection()
    {
        var spec = ResolveWorkspaceLifecycleSpec();
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
        if (string.Equals(spec.Prefix, "workspace_profiles.", StringComparison.OrdinalIgnoreCase))
        {
            var activeProfileId = ResolveActiveProfileId();
            var profileSlotKey = $"profiles.{activeProfileId}.workspace";
            if (string.Equals(ReadConfiguredString(profileSlotKey), id, StringComparison.OrdinalIgnoreCase))
            {
                changes.Add(Set(profileSlotKey, StructuredValue.FromString(nextId)));
            }
        }

        ApplyWorkspaceChanges(changes, $"已删除{spec.DisplayName}：{id}");
    }

    private WorkspaceConfigurationLifecycleSpec ResolveWorkspaceLifecycleSpec()
    {
        var selectedKey = selectedField?.Key ?? string.Empty;
        if (selectedKey.StartsWith("projects.", StringComparison.OrdinalIgnoreCase))
        {
            return new WorkspaceConfigurationLifecycleSpec("projects.", "project", "项目覆盖");
        }

        return new WorkspaceConfigurationLifecycleSpec("workspace_profiles.", "workspace", "工作空间配置文件");
    }

    private IReadOnlyList<ConfigurationChange> BuildDefaultWorkspaceChanges(WorkspaceConfigurationLifecycleSpec spec, string id)
        => spec.Prefix switch
        {
            "projects." =>
            [
                Set($"projects.{id}.path", StructuredValue.FromString(".")),
                Set($"projects.{id}.trust_level", StructuredValue.FromString("trusted")),
                Set($"projects.{id}.profile", StructuredValue.FromString(ReadConfiguredString("profile") ?? "default")),
            ],
            _ =>
            [
                Set($"workspace_profiles.{id}.root_markers", ArrayValue([".git", ".tianshu"])),
                Set($"workspace_profiles.{id}.trust_policy", StructuredValue.FromString("prompt")),
                Set($"workspace_profiles.{id}.artifact_root", StructuredValue.FromString(".tianshu/artifacts")),
                Set($"workspace_profiles.{id}.state_root", StructuredValue.FromString(".tianshu/state")),
                Set($"workspace_profiles.{id}.model", StructuredValue.FromString("inherit")),
                Set($"workspace_profiles.{id}.model_lock", StructuredValue.FromString("snapshot-on-create")),
            ],
        };

    private void ApplyWorkspaceChanges(IReadOnlyList<ConfigurationChange> changes, string successMessage)
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

    private sealed record WorkspaceConfigurationLifecycleSpec(string Prefix, string BaseId, string DisplayName);
}
