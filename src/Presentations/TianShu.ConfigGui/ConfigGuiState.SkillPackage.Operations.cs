using TianShu.Configuration;
using TianShu.Contracts.Configuration;
using TianShu.Contracts.Primitives;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TianShu.ConfigGui;

internal sealed partial class ConfigGuiState
{
    private void RefreshSkillPackageProjection(string? selectedSkillPath)
    {
        var root = TianShuSkillPackageConfiguration.ResolveRootDirectory(ConfigPath);
        skillPackageProjection = skillPackageConfiguration.Load(root, ConfigPath, selectedSkillPath);
        SkillPackageRootText.Value = $"扫描目录：{skillPackageProjection.SkillRootDirectory}；兼容 repo .agents/skills 与额外 skill roots。";
        SkillPackageLabels = skillPackageProjection.Packages
            .Select(static package => $"{package.DisplayName} [{package.Scope}]" + (package.Enabled ? string.Empty : "（禁用）"))
            .ToArray();
        selectedSkillPackagePath = skillPackageProjection.SelectedSkillPath;

        var packageIndex = skillPackageProjection.SelectedSkillPath is null
            ? 0
            : skillPackageProjection.Packages
                .Select((package, index) => new { package, index })
                .FirstOrDefault(item => string.Equals(item.package.SkillMarkdownPath, skillPackageProjection.SelectedSkillPath, StringComparison.OrdinalIgnoreCase))
                ?.index ?? 0;
        SkillPackageIndex.Value = packageIndex;

        if (skillPackageProjection.SelectedPackage is null)
        {
            ClearSkillPackageView();
            SkillPackageStatusText.Value = skillPackageProjection.Issues.Count == 0
                ? "未发现技能包；可在 modules/skills/<package>/SKILL.md 下新增内容能力包。"
                : string.Join(Environment.NewLine, skillPackageProjection.Issues.Select(static issue => issue.Message));
            return;
        }

        LoadSkillPackageIntoView(skillPackageProjection.SelectedPackage);
        var issueText = skillPackageProjection.Issues.Count == 0
            ? string.Empty
            : $"，{skillPackageProjection.Issues.Count} 条问题";
        SkillPackageStatusText.Value = $"已读取 {skillPackageProjection.Packages.Count} 个技能包{issueText}。";
    }

    public void SelectSkillPackageIndex(int index)
    {
        if (skillPackageProjection is null || skillPackageProjection.Packages.Count == 0)
        {
            ClearSkillPackageView();
            return;
        }

        var packageIndex = Math.Clamp(index, 0, skillPackageProjection.Packages.Count - 1);
        RefreshSkillPackageProjection(skillPackageProjection.Packages[packageIndex].SkillMarkdownPath);
    }

    public void SaveSkillPackageEnabled()
    {
        if (skillPackageProjection?.SelectedPackage is not { } package)
        {
            SkillPackageStatusText.Value = "没有可保存状态的技能包。";
            UpdateContextPanel();
            return;
        }

        try
        {
            var enabled = SkillPackageEnabledIndex.Value != 1;
            skillPackageConfiguration.SaveEnabled(ConfigPath, package.SkillMarkdownPath, enabled);
            selectedSkillPackagePath = package.SkillMarkdownPath;
            RefreshSkillPackageProjection(package.SkillMarkdownPath);
            SkillPackageStatusText.Value = enabled
                ? "已启用技能包；如果存在禁用覆盖，已从 tianshu.toml 移除。"
                : "已禁用技能包；状态已写入 tianshu.toml 的 [[skills.config]]。";
            UpdateContextPanel();
        }
        catch (Exception ex)
        {
            SkillPackageStatusText.Value = $"保存技能包状态失败：{ex.Message}";
            UpdateContextPanel();
        }
    }

    public void CreateSkillPackage()
    {
        try
        {
            var root = TianShuSkillPackageConfiguration.ResolveRootDirectory(ConfigPath);
            var skillRoot = TianShuSkillPackageConfiguration.ResolveUserSkillRootDirectory(root);
            var requestedId = SanitizeSkillPackageId(SkillPackageNewId.Value);
            var id = CreateUniqueSkillPackageId(skillRoot, requestedId);
            var packageDirectory = Path.Combine(skillRoot, id);
            var skillMarkdownPath = Path.Combine(packageDirectory, TianShuSkillPackageConfiguration.SkillMarkdownFileName);

            Directory.CreateDirectory(packageDirectory);
            File.WriteAllText(
                skillMarkdownPath,
                $"""
                ---
                name: {id}
                description: TianShu skill package
                ---

                在这里编写技能说明、触发条件和操作约束。
                """ + Environment.NewLine,
                Encoding.UTF8);

            SkillPackageNewId.Value = string.Empty;
            selectedSkillPackagePath = skillMarkdownPath;
            RefreshSkillPackageProjection(skillMarkdownPath);
            SkillPackageStatusText.Value = $"已新增技能包：{id}。";
            UpdateContextPanel();
        }
        catch (Exception ex)
        {
            SkillPackageStatusText.Value = $"新增技能包失败：{ex.Message}";
            UpdateContextPanel();
        }
    }

    public void DeleteSelectedSkillPackage()
    {
        if (skillPackageProjection?.SelectedPackage is not { } package)
        {
            SkillPackageStatusText.Value = "没有可删除的技能包。";
            UpdateContextPanel();
            return;
        }

        try
        {
            var root = TianShuSkillPackageConfiguration.ResolveRootDirectory(ConfigPath);
            var skillRoot = Path.GetFullPath(TianShuSkillPackageConfiguration.ResolveUserSkillRootDirectory(root));
            var systemSkillRoot = Path.GetFullPath(Path.Combine(skillRoot, TianShuSkillPackageConfiguration.SystemSkillDirectoryName));
            var packageDirectory = Path.GetFullPath(package.PackageDirectory);

            if (!IsSameOrChildPath(skillRoot, packageDirectory) || IsSameOrChildPath(systemSkillRoot, packageDirectory))
            {
                SkillPackageStatusText.Value = "只能删除当前用户 modules/skills 目录下的普通技能包；系统、repo、旧 skills 或外部 skill root 不会被 ConfigGUI 删除。";
                UpdateContextPanel();
                return;
            }

            Directory.Delete(packageDirectory, recursive: true);
            selectedSkillPackagePath = null;
            RefreshSkillPackageProjection(null);
            SkillPackageStatusText.Value = $"已删除技能包：{package.Id}。";
            UpdateContextPanel();
        }
        catch (Exception ex)
        {
            SkillPackageStatusText.Value = $"删除技能包失败：{ex.Message}";
            UpdateContextPanel();
        }
    }

    private void LoadSkillPackageIntoView(SkillPackageDescriptor package)
    {
        SkillPackageEnabledIndex.Value = package.Enabled ? 0 : 1;
        SkillPackagePathText.Value = $"SKILL.md：{package.SkillMarkdownPath}；Metadata：{(File.Exists(package.MetadataPath) ? package.MetadataPath : "未提供 agents/tianshu.yaml")}";
        SkillPackageTitleText.Value = $"{package.DisplayName}（{package.Id}）";
        SkillPackageDescriptionText.Value = string.Join(
            Environment.NewLine,
            new[]
            {
                string.IsNullOrWhiteSpace(package.ShortDescription) ? null : $"短描述：{package.ShortDescription}",
                string.IsNullOrWhiteSpace(package.Description) ? null : $"说明：{package.Description}",
                $"来源：{package.Scope}",
            }.OfType<string>());
        SkillPackageInterfaceText.Value = FormatSkillInterface(package.Interface);
        SkillPackageDependencyText.Value = FormatSkillDependencies(package.Dependencies);
        SkillPackagePermissionText.Value = FormatSkillPermissions(package.PermissionProfile, package.ManagedNetworkOverride);
        SkillPackageResourceText.Value = string.Join(
            Environment.NewLine,
            [
                $"包目录：{package.PackageDirectory}",
                $"来源根目录：{package.SourceRoot}",
                $"assets：{FormatExists(package.HasAssetsDirectory)}",
                $"scripts：{FormatExists(package.HasScriptsDirectory)}",
                $"templates：{FormatExists(package.HasTemplatesDirectory)}",
            ]);
    }

    private void ClearSkillPackageView()
    {
        SkillPackageEnabledIndex.Value = 0;
        SkillPackagePathText.Value = string.Empty;
        SkillPackageTitleText.Value = "未选择技能包";
        SkillPackageDescriptionText.Value = string.Empty;
        SkillPackageInterfaceText.Value = "未发现可展示接口元数据。";
        SkillPackageDependencyText.Value = "未声明依赖。";
        SkillPackagePermissionText.Value = "未声明权限。";
        SkillPackageResourceText.Value = string.Empty;
    }

    private static string FormatSkillInterface(SkillInterfaceInfo? info)
    {
        if (info is null || !info.HasValues)
        {
            return "未发现 agents/tianshu.yaml interface 元数据。";
        }

        return string.Join(
            Environment.NewLine,
            new[]
            {
                FormatOptionalLine("显示名", info.DisplayName),
                FormatOptionalLine("短描述", info.ShortDescription),
                FormatOptionalLine("小图标", info.IconSmall),
                FormatOptionalLine("大图标", info.IconLarge),
                FormatOptionalLine("品牌色", info.BrandColor),
                FormatOptionalLine("默认提示词", info.DefaultPrompt),
            }.OfType<string>());
    }

    private static string FormatSkillDependencies(SkillDependencies? dependencies)
    {
        if (dependencies?.Tools.Count is not > 0)
        {
            return "未声明工具依赖。";
        }

        return string.Join(
            Environment.NewLine,
            dependencies.Tools.Select(static tool =>
                $"- {tool.Type}:{tool.Value}"
                + (string.IsNullOrWhiteSpace(tool.Description) ? string.Empty : $"，{tool.Description}")
                + (string.IsNullOrWhiteSpace(tool.Transport) ? string.Empty : $"，transport={tool.Transport}")));
    }

    private static string FormatSkillPermissions(SkillPermissionProfile? permissions, SkillManagedNetworkOverride? networkOverride)
    {
        var lines = new List<string>();
        if (permissions?.Network?.Enabled is { } networkEnabled)
        {
            lines.Add($"网络权限：{(networkEnabled ? "需要" : "不需要")}");
        }

        if (networkOverride?.AllowedDomains is { Count: > 0 } allowedDomains)
        {
            lines.Add($"允许域：{string.Join(", ", allowedDomains)}");
        }

        if (networkOverride?.DeniedDomains is { Count: > 0 } deniedDomains)
        {
            lines.Add($"拒绝域：{string.Join(", ", deniedDomains)}");
        }

        return lines.Count == 0 ? "未声明权限覆盖。" : string.Join(Environment.NewLine, lines);
    }

    private static string? FormatOptionalLine(string name, string? value)
        => string.IsNullOrWhiteSpace(value) ? null : $"{name}：{value}";

    private static string FormatExists(bool exists)
        => exists ? "存在" : "未提供";

    private static string SanitizeSkillPackageId(string rawId)
    {
        var value = string.IsNullOrWhiteSpace(rawId) ? "skill" : rawId.Trim();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_')
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
            else if (char.IsWhiteSpace(ch) || ch is '.' or '/')
            {
                builder.Append('-');
            }
        }

        var id = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(id) ? "skill" : id;
    }

    private static string CreateUniqueSkillPackageId(string skillRoot, string baseId)
    {
        for (var index = 0; index < 1000; index++)
        {
            var candidate = index == 0 ? baseId : $"{baseId}-{index + 1}";
            if (!Directory.Exists(Path.Combine(skillRoot, candidate)))
            {
                return candidate;
            }
        }

        return $"{baseId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    }

    private static bool IsSameOrChildPath(string parentPath, string childPath)
    {
        var parent = Path.GetFullPath(parentPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var child = Path.GetFullPath(childPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return child.StartsWith(parent, comparison);
    }
}
