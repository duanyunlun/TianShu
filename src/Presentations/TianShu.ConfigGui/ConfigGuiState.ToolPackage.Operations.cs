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
    private void RefreshToolProjection(string? selectedManifestPath)
    {
        var root = TianShuToolManifestConfiguration.ResolveRootDirectory(ConfigPath);
        toolProjection = toolManifestConfiguration.Load(root, selectedManifestPath);
        ToolRootText.Value = $"扫描目录：{toolProjection.ToolRootDirectory}";
        ToolPackageLabels = toolProjection.Files.Select(static file => file.DisplayName).ToArray();
        selectedToolManifestPath = toolProjection.SelectedManifestPath;

        var packageIndex = toolProjection.SelectedManifestPath is null
            ? 0
            : toolProjection.Files
                .Select((file, index) => new { file, index })
                .FirstOrDefault(item => string.Equals(item.file.Path, toolProjection.SelectedManifestPath, StringComparison.OrdinalIgnoreCase))
                ?.index ?? 0;
        ToolPackageIndex.Value = packageIndex;

        if (toolProjection.SelectedPackage is null)
        {
            BeginNewToolPackage();
            ToolStatusText.Value = toolProjection.Issues.Count == 0
                ? "未发现工具包 manifest；可新建用户工具包。"
                : string.Join(Environment.NewLine, toolProjection.Issues.Select(static issue => issue.Message));
            return;
        }

        LoadToolPackageIntoEditor(toolProjection.SelectedPackage);
        var issueText = toolProjection.Issues.Count == 0
            ? string.Empty
            : $"，{toolProjection.Issues.Count} 条问题";
        ToolStatusText.Value = $"已读取 {toolProjection.Files.Count} 个工具包 manifest{issueText}。";
    }

    public void SelectToolPackageIndex(int index)
    {
        if (toolProjection is null || toolProjection.Files.Count == 0)
        {
            BeginNewToolPackage();
            return;
        }

        var packageIndex = Math.Clamp(index, 0, toolProjection.Files.Count - 1);
        RefreshToolProjection(toolProjection.Files[packageIndex].Path);
    }

    public void BeginNewToolPackage()
    {
        selectedToolManifestPath = null;
        selectedToolProviderId = null;
        ToolPackageIndex.Value = 0;
        ToolPackageId.Value = CreateUniqueToolPackageId("custom-tools");
        ToolPackageDisplayName.Value = ToolPackageId.Value;
        ToolPackageEnabledIndex.Value = 0;
        SelectToolPackageType("assembly");
        ToolPackagePriority.Value = "0";
        ToolPackageProviderType.Value = string.Empty;
        ToolProviderLabels = [];
        BeginNewToolProvider();
        ToolManifestPathText.Value = "保存目标：新建后写入 modules/tools/packages/<package-id>/tool.toml";
        ToolStatusText.Value = "正在新建工具包；保存时会写入 modules/tools/packages 目录。";
    }

    public void CopySelectedToolPackageToDraft()
    {
        if (toolProjection?.SelectedPackage is not { } package)
        {
            ToolStatusText.Value = "没有可复制的工具包。";
            return;
        }

        selectedToolManifestPath = null;
        ToolPackageId.Value = CreateUniqueToolPackageId(package.Id);
        ToolPackageDisplayName.Value = package.DisplayName;
        ToolManifestPathText.Value = "保存目标：复制后写入 modules/tools/packages/<package-id>/tool.toml";
        ToolStatusText.Value = $"已复制工具包到草稿：{package.Id} -> {ToolPackageId.Value}";
    }

    public void DeleteSelectedToolPackage()
    {
        if (string.IsNullOrWhiteSpace(selectedToolManifestPath))
        {
            ToolStatusText.Value = "没有可删除的工具包 manifest。";
            return;
        }

        try
        {
            var root = TianShuToolManifestConfiguration.ResolveRootDirectory(ConfigPath);
            toolManifestConfiguration.DeletePackage(root, selectedToolManifestPath);
            selectedToolManifestPath = null;
            RefreshToolProjection(null);
            ToolStatusText.Value = "已删除用户工具包 manifest。";
        }
        catch (Exception ex)
        {
            ToolStatusText.Value = $"删除工具包失败：{ex.Message}";
        }
    }

    public void SelectToolProviderIndex(int index)
    {
        var providers = toolProjection?.SelectedPackage?.Providers ?? [];
        if (providers.Count == 0)
        {
            BeginNewToolProvider();
            return;
        }

        var providerIndex = Math.Clamp(index, 0, providers.Count - 1);
        LoadToolProviderIntoEditor(providers[providerIndex], providerIndex);
    }

    public void BeginNewToolProvider()
    {
        selectedToolProviderId = null;
        ToolProviderIndex.Value = 0;
        ToolProviderId.Value = CreateUniqueToolProviderId("provider");
        ToolProviderEnabledIndex.Value = 0;
        SelectToolProviderType("assembly");
        ToolProviderAssemblyPath.Value = string.Empty;
        ToolProviderTypeName.Value = string.Empty;
        ToolProviderPriority.Value = "10";
        ToolProviderReplaceExistingIndex.Value = 0;
        ToolProviderResolvedPathText.Value = "提供方保存后会写入当前工具包的 [[providers]]。";
    }

    public void DeleteSelectedToolProvider()
    {
        if (toolProjection?.SelectedPackage is not { } package || string.IsNullOrWhiteSpace(selectedToolProviderId))
        {
            ToolStatusText.Value = "没有可删除的提供方条目。";
            return;
        }

        package.Providers = package.Providers
            .Where(provider => !string.Equals(provider.Id, selectedToolProviderId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        selectedToolProviderId = null;
        SavePackageValue(package);
    }

    public void SaveToolPackage()
    {
        try
        {
            var package = BuildToolPackageFromEditor();
            SavePackageValue(package);
        }
        catch (Exception ex)
        {
            ToolStatusText.Value = $"保存工具包失败：{ex.Message}";
            UpdateContextPanel();
        }
    }

    private void SavePackageValue(ToolPackageManifestValue package)
    {
        var root = TianShuToolManifestConfiguration.ResolveRootDirectory(ConfigPath);
        var targetPath = string.IsNullOrWhiteSpace(selectedToolManifestPath)
            ? toolManifestConfiguration.CreatePackage(root, package.Id, overwrite: false)
            : selectedToolManifestPath;
        package.ManifestPath = targetPath;
        package.PackageDirectory = Path.GetDirectoryName(targetPath)!;
        toolManifestConfiguration.SavePackage(targetPath, package);
        RefreshToolProjection(targetPath);
        ToolStatusText.Value = $"已保存工具包 manifest：{targetPath}";
        UpdateContextPanel();
    }

    private ToolPackageManifestValue BuildToolPackageFromEditor()
    {
        var package = toolProjection?.SelectedPackage is { } selectedPackage
            ? CloneToolPackage(selectedPackage)
            : new ToolPackageManifestValue();
        package.Id = ToolPackageId.Value.Trim();
        package.DisplayName = ToolPackageDisplayName.Value.Trim();
        package.Enabled = ToolPackageEnabledIndex.Value != 1;
        package.Type = GetSelectedToolPackageType();
        package.Priority = ParseIntOrDefault(ToolPackagePriority.Value, 0);
        package.ProviderType = NullIfWhiteSpace(ToolPackageProviderType.Value);

        var provider = new ToolProviderManifestValue
        {
            Id = ToolProviderId.Value.Trim(),
            Enabled = ToolProviderEnabledIndex.Value != 1,
            Type = GetSelectedToolProviderType(),
            AssemblyPath = NullIfWhiteSpace(ToolProviderAssemblyPath.Value),
            ProviderType = NullIfWhiteSpace(ToolProviderTypeName.Value),
            Priority = ParseIntOrDefault(ToolProviderPriority.Value, 10),
            ReplaceExisting = ToolProviderReplaceExistingIndex.Value != 1,
        };

        var providers = package.Providers.ToList();
        if (!string.IsNullOrWhiteSpace(provider.Id))
        {
            var index = providers.FindIndex(item => string.Equals(item.Id, selectedToolProviderId ?? provider.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                providers[index] = provider;
            }
            else
            {
                providers.Add(provider);
            }
        }

        package.Providers = providers;
        return package;
    }

    private void LoadToolPackageIntoEditor(ToolPackageManifestValue package)
    {
        ToolPackageId.Value = package.Id;
        ToolPackageDisplayName.Value = package.DisplayName;
        ToolPackageEnabledIndex.Value = package.Enabled ? 0 : 1;
        SelectToolPackageType(package.Type);
        ToolPackagePriority.Value = package.Priority.ToString();
        ToolPackageProviderType.Value = package.ProviderType ?? string.Empty;
        ToolManifestPathText.Value = $"Manifest：{package.ManifestPath}";
        ToolProviderLabels = package.Providers.Select(static provider => provider.Id).ToArray();
        if (package.Providers.Count == 0)
        {
            BeginNewToolProvider();
        }
        else
        {
            var index = string.IsNullOrWhiteSpace(selectedToolProviderId)
                ? 0
                : package.Providers
                    .Select((provider, providerIndex) => new { provider, providerIndex })
                    .FirstOrDefault(item => string.Equals(item.provider.Id, selectedToolProviderId, StringComparison.OrdinalIgnoreCase))
                    ?.providerIndex ?? 0;
            LoadToolProviderIntoEditor(package.Providers[index], index);
        }
    }

    private void LoadToolProviderIntoEditor(ToolProviderManifestValue provider, int index)
    {
        selectedToolProviderId = provider.Id;
        ToolProviderIndex.Value = index;
        ToolProviderId.Value = provider.Id;
        ToolProviderEnabledIndex.Value = provider.Enabled ? 0 : 1;
        SelectToolProviderType(provider.Type);
        ToolProviderAssemblyPath.Value = provider.AssemblyPath ?? string.Empty;
        ToolProviderTypeName.Value = provider.ProviderType ?? string.Empty;
        ToolProviderPriority.Value = provider.Priority.ToString();
        ToolProviderReplaceExistingIndex.Value = provider.ReplaceExisting ? 0 : 1;
        ToolProviderResolvedPathText.Value = toolProjection?.SelectedPackage is { } package
            ? $"程序集解析路径：{TianShuToolManifestConfiguration.ResolveProviderAssemblyFullPath(package, provider)}"
            : "提供方保存后会写入当前工具包的 [[providers]]。";
    }

    private void SelectToolPackageType(string? type)
    {
        var index = FindLabelIndex(ToolPackageTypeLabels, type, fallbackIndex: 1);
        ToolPackageTypeIndex.Value = index;
        ToolPackageType.Value = ToolPackageTypeLabels[index];
    }

    private void SelectToolProviderType(string? type)
    {
        var index = FindLabelIndex(ToolProviderTypeLabels, type, fallbackIndex: 0);
        ToolProviderTypeIndex.Value = index;
        ToolProviderType.Value = ToolProviderTypeLabels[index];
    }
}
