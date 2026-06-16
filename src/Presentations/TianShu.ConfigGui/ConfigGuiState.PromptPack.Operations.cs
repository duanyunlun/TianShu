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
    public void SelectPromptFileIndex(int index)
    {
        if (promptProjection is null || promptProjection.Files.Count == 0)
        {
            return;
        }

        var fileIndex = Math.Clamp(index, 0, promptProjection.Files.Count - 1);
        var selectedFile = promptProjection.Files[fileIndex].Path;
        RefreshPromptProjection(selectedFile);
    }

    public void SelectPromptSectionIndex(int index)
    {
        if (promptProjection is null || promptProjection.Sections.Count == 0)
        {
            PromptSectionDescription.Value = "未发现可编辑 prompt 段。";
            PromptText.Value = string.Empty;
            PromptSupportsEnabled.Value = false;
            PromptSupportsMode.Value = false;
            return;
        }

        var sectionIndex = Math.Clamp(index, 0, promptProjection.Sections.Count - 1);
        var section = promptProjection.Sections[sectionIndex];
        var value = promptProjection.Values.FirstOrDefault(item => string.Equals(item.Key, section.Key, StringComparison.OrdinalIgnoreCase));

        selectedPromptSectionKey = section.Key;
        PromptSectionIndex.Value = sectionIndex;
        PromptSectionDescription.Value = section.Description ?? "该 prompt 段暂无说明。";
        PromptSupportsEnabled.Value = section.SupportsEnabled;
        PromptSupportsMode.Value = section.SupportsMode;
        PromptEnabledIndex.Value = value?.Enabled == false ? 1 : 0;
        PromptModeIndex.Value = value?.Mode switch
        {
            PromptConfigurationSectionMergeMode.Replace => 0,
            PromptConfigurationSectionMergeMode.Prepend => 2,
            _ => 1,
        };
        PromptText.Value = value?.Text ?? string.Empty;
    }

    public void SavePromptSection()
    {
        if (promptProjection is null)
        {
            return;
        }

        var sectionKey = selectedPromptSectionKey
            ?? promptProjection.Sections.FirstOrDefault()?.Key;
        if (string.IsNullOrWhiteSpace(sectionKey))
        {
            PromptStatusText.Value = "没有可保存的 prompt 段。";
            UpdateContextPanel();
            return;
        }

        var targetFilePath = promptProjection.SelectedFilePath
            ?? TianShuPromptTomlConfiguration.ResolveDefaultPromptFilePath(promptProjection.RootDirectory);

        try
        {
            promptConfiguration.SaveSection(targetFilePath, new PromptConfigurationSectionChange
            {
                SectionKey = sectionKey,
                Enabled = PromptEnabledIndex.Value != 1,
                Mode = PromptModeIndex.Value switch
                {
                    0 => PromptConfigurationSectionMergeMode.Replace,
                    2 => PromptConfigurationSectionMergeMode.Prepend,
                    _ => PromptConfigurationSectionMergeMode.Append,
                },
                Text = PromptText.Value,
            });
            RefreshPromptProjection(targetFilePath);
            PromptStatusText.Value = $"已保存 Prompt 段：{sectionKey} -> {targetFilePath}";
            UpdateContextPanel();
        }
        catch (Exception ex)
        {
            PromptStatusText.Value = $"保存 Prompt 失败：{ex.Message}";
            UpdateContextPanel();
        }
    }

    public void CreatePromptFile()
    {
        var root = promptProjection?.RootDirectory ?? TianShuPromptTomlConfiguration.ResolveRootDirectory(ConfigPath);
        try
        {
            string createdPath;
            if (promptProjection?.SelectedFilePath is { Length: > 0 } selectedFilePath && File.Exists(selectedFilePath))
            {
                createdPath = promptConfiguration.CopyPromptFile(root, selectedFilePath, PromptFileName.Value);
                RefreshPromptProjection(createdPath);
                PromptStatusText.Value = $"已基于当前 Prompt Pack 新建：{createdPath}";
                return;
            }

            createdPath = promptConfiguration.CreatePromptFile(root, PromptFileName.Value);
            RefreshPromptProjection(createdPath);
            PromptStatusText.Value = $"已新建空 Prompt Pack：{createdPath}";
        }
        catch (Exception ex)
        {
            PromptStatusText.Value = $"新建 Prompt Pack 失败：{ex.Message}";
        }
    }

    public void CopyPromptFile()
    {
        if (promptProjection?.SelectedFilePath is not { Length: > 0 } selectedFilePath)
        {
            PromptStatusText.Value = "没有可复制的 Prompt Pack。";
            return;
        }

        var root = promptProjection.RootDirectory;
        try
        {
            var copiedPath = promptConfiguration.CopyPromptFile(root, selectedFilePath, PromptFileName.Value);
            RefreshPromptProjection(copiedPath);
            PromptStatusText.Value = $"已复制 Prompt Pack：{copiedPath}";
        }
        catch (Exception ex)
        {
            PromptStatusText.Value = $"复制 Prompt Pack 失败：{ex.Message}";
        }
    }

    public void DeletePromptFile()
    {
        if (promptProjection?.SelectedFilePath is not { Length: > 0 } selectedFilePath)
        {
            PromptStatusText.Value = "没有可删除的 Prompt Pack。";
            return;
        }

        try
        {
            promptConfiguration.DeletePromptFile(promptProjection.RootDirectory, selectedFilePath);
            RefreshPromptProjection(null);
            PromptStatusText.Value = $"已删除 Prompt Pack：{selectedFilePath}";
        }
        catch (Exception ex)
        {
            PromptStatusText.Value = $"删除 Prompt Pack 失败：{ex.Message}";
        }
    }

    private void RefreshPromptProjection(string? selectedFilePath)
    {
        var root = TianShuPromptTomlConfiguration.ResolveRootDirectory(ConfigPath);
        var sectionKey = selectedPromptSectionKey;
        promptProjection = promptConfiguration.Load(root, selectedFilePath);
        PromptRootText.Value = $"扫描目录：{TianShuPromptTomlConfiguration.ResolvePromptModuleRootDirectory(root)}；只显示 modules/prompts/<package>/prompt.toml。";
        PromptFileLabels = promptProjection.Files.Select(static file => file.DisplayName).ToArray();
        PromptSectionLabels = promptProjection.Sections.Select(static section => section.DisplayName).ToArray();

        var fileIndex = promptProjection.SelectedFilePath is null
            ? 0
            : promptProjection.Files
                .Select((file, index) => new { file, index })
                .FirstOrDefault(item => string.Equals(item.file.Path, promptProjection.SelectedFilePath, StringComparison.OrdinalIgnoreCase))
                ?.index ?? 0;
        PromptFileIndex.Value = fileIndex;
        if (promptProjection.SelectedFilePath is { Length: > 0 } selectedPromptPath)
        {
            PromptSaveTargetText.Value = selectedPromptPath;
            var packageId = Path.GetFileName(Path.GetDirectoryName(selectedPromptPath));
            PromptFileName.Value = string.IsNullOrWhiteSpace(packageId)
                ? "default"
                : $"{packageId}_copy";
        }
        else
        {
            PromptSaveTargetText.Value = TianShuPromptTomlConfiguration.ResolveDefaultPromptFilePath(promptProjection.RootDirectory);
        }

        var sectionIndex = 0;
        if (!string.IsNullOrWhiteSpace(sectionKey))
        {
            sectionIndex = promptProjection.Sections
                .Select((section, index) => new { section, index })
                .FirstOrDefault(item => string.Equals(item.section.Key, sectionKey, StringComparison.OrdinalIgnoreCase))
                ?.index ?? 0;
        }

        SelectPromptSectionIndex(sectionIndex);
        var issues = promptProjection.Issues.Count == 0
            ? string.Empty
            : $"，{promptProjection.Issues.Count} 条问题";
        PromptStatusText.Value = promptProjection.Files.Count == 0
            ? "未发现 Prompt Pack；保存时会创建 modules/prompts/default/prompt.toml。"
            : $"已发现 {promptProjection.Files.Count} 份 Prompt Pack{issues}。";
    }
}
