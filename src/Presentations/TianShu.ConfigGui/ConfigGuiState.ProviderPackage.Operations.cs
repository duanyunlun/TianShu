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
    private void RefreshProviderPackageProjection(string? selectedManifestPath)
    {
        var root = TianShuProviderManifestConfiguration.ResolveRootDirectory(ConfigPath);
        providerPackageProjection = providerManifestConfiguration.Load(root, selectedManifestPath);
        ModelProviderPackageRootText.Value = $"扫描目录：{providerPackageProjection.ProviderRootDirectory}";
        ModelProviderPackageLabels = providerPackageProjection.Files.Select(static file => file.DisplayName).ToArray();
        selectedProviderManifestPath = providerPackageProjection.SelectedManifestPath;

        var packageIndex = providerPackageProjection.SelectedManifestPath is null
            ? 0
            : providerPackageProjection.Files
                .Select((file, index) => new { file, index })
                .FirstOrDefault(item => string.Equals(item.file.Path, providerPackageProjection.SelectedManifestPath, StringComparison.OrdinalIgnoreCase))
                ?.index ?? 0;
        ModelProviderPackageIndex.Value = packageIndex;

        if (providerPackageProjection.SelectedPackage is null)
        {
            BeginNewModelProviderPackage();
            ModelProviderPackageStatusText.Value = providerPackageProjection.Issues.Count == 0
                ? "未发现协议适配器包 manifest；可新建用户适配器包。"
                : string.Join(Environment.NewLine, providerPackageProjection.Issues.Select(static issue => issue.Message));
            return;
        }

        LoadModelProviderPackageIntoEditor(providerPackageProjection.SelectedPackage);
        var issueText = providerPackageProjection.Issues.Count == 0
            ? string.Empty
            : $"，{providerPackageProjection.Issues.Count} 条问题";
        ModelProviderPackageStatusText.Value = $"已读取 {providerPackageProjection.Files.Count} 个协议适配器包 manifest{issueText}。";
    }

    public void SelectModelProviderPackageIndex(int index)
    {
        if (providerPackageProjection is null || providerPackageProjection.Files.Count == 0)
        {
            BeginNewModelProviderPackage();
            return;
        }

        var packageIndex = Math.Clamp(index, 0, providerPackageProjection.Files.Count - 1);
        RefreshProviderPackageProjection(providerPackageProjection.Files[packageIndex].Path);
    }

    public void BeginNewModelProviderPackage()
    {
        selectedProviderManifestPath = null;
        selectedProviderAdapterId = null;
        ModelProviderPackageIndex.Value = 0;
        ModelProviderPackageId.Value = CreateUniqueModelProviderPackageId("custom-providers");
        ModelProviderPackageDisplayName.Value = ModelProviderPackageId.Value;
        ModelProviderPackageEnabledIndex.Value = 0;
        SelectModelProviderPackageType("assembly");
        ModelProviderPackagePriority.Value = "0";
        ModelProviderAdapterLabels = [];
        BeginNewModelProviderAdapter();
        ModelProviderPackageManifestPathText.Value = "保存目标：新建后写入 modules/model/provider-adapters/<package-id>/provider.toml";
        ModelProviderPackageStatusText.Value = "正在新建协议适配器包；保存时会写入 providers 目录。";
    }

    public void CopySelectedModelProviderPackageToDraft()
    {
        if (providerPackageProjection?.SelectedPackage is not { } package)
        {
            ModelProviderPackageStatusText.Value = "没有可复制的协议适配器包。";
            return;
        }

        selectedProviderManifestPath = null;
        ModelProviderPackageId.Value = CreateUniqueModelProviderPackageId(package.Id);
        ModelProviderPackageDisplayName.Value = package.DisplayName;
        ModelProviderPackageManifestPathText.Value = "保存目标：复制后写入 modules/model/provider-adapters/<package-id>/provider.toml";
        ModelProviderPackageStatusText.Value = $"已复制协议适配器包到草稿：{package.Id} -> {ModelProviderPackageId.Value}";
    }

    public void DeleteSelectedModelProviderPackage()
    {
        if (string.IsNullOrWhiteSpace(selectedProviderManifestPath))
        {
            ModelProviderPackageStatusText.Value = "没有可删除的协议适配器包 manifest。";
            return;
        }

        try
        {
            var root = TianShuProviderManifestConfiguration.ResolveRootDirectory(ConfigPath);
            providerManifestConfiguration.DeletePackage(root, selectedProviderManifestPath);
            selectedProviderManifestPath = null;
            RefreshProviderPackageProjection(null);
            ModelProviderPackageStatusText.Value = "已删除用户协议适配器包 manifest。";
        }
        catch (Exception ex)
        {
            ModelProviderPackageStatusText.Value = $"删除协议适配器包失败：{ex.Message}";
        }
    }

    public void SelectModelProviderAdapterIndex(int index)
    {
        var adapters = providerPackageProjection?.SelectedPackage?.Adapters ?? [];
        if (adapters.Count == 0)
        {
            BeginNewModelProviderAdapter();
            return;
        }

        var adapterIndex = Math.Clamp(index, 0, adapters.Count - 1);
        LoadModelProviderAdapterIntoEditor(adapters[adapterIndex], adapterIndex);
    }

    public void BeginNewModelProviderAdapter()
    {
        selectedProviderAdapterId = null;
        ModelProviderAdapterIndex.Value = 0;
        ModelProviderAdapterId.Value = CreateUniqueModelProviderAdapterId("adapter");
        ModelProviderAdapterDisplayName.Value = ModelProviderAdapterId.Value;
        ModelProviderAdapterEnabledIndex.Value = 0;
        SelectModelProviderAdapterType("assembly");
        ModelProviderAdapterAssemblyPath.Value = string.Empty;
        ModelProviderAdapterPriority.Value = "10";
        ModelProviderAdapterResolvedPathText.Value = "Adapter 保存后会写入当前协议适配器包的 [[adapters]]。";
    }

    public void DeleteSelectedModelProviderAdapter()
    {
        if (providerPackageProjection?.SelectedPackage is not { } package || string.IsNullOrWhiteSpace(selectedProviderAdapterId))
        {
            ModelProviderPackageStatusText.Value = "没有可删除的 Adapter 条目。";
            return;
        }

        package.Adapters = package.Adapters
            .Where(adapter => !string.Equals(adapter.Id, selectedProviderAdapterId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        selectedProviderAdapterId = null;
        SaveModelProviderPackageValue(package);
    }

    public void SaveModelProviderPackage()
    {
        try
        {
            var package = BuildModelProviderPackageFromEditor();
            SaveModelProviderPackageValue(package);
        }
        catch (Exception ex)
        {
            ModelProviderPackageStatusText.Value = $"保存协议适配器包失败：{ex.Message}";
            UpdateContextPanel();
        }
    }

    private void SaveModelProviderPackageValue(ProviderPackageManifestValue package)
    {
        var root = TianShuProviderManifestConfiguration.ResolveRootDirectory(ConfigPath);
        var targetPath = string.IsNullOrWhiteSpace(selectedProviderManifestPath)
            ? providerManifestConfiguration.CreatePackage(root, package.Id, overwrite: false)
            : selectedProviderManifestPath;
        package.ManifestPath = targetPath;
        package.PackageDirectory = Path.GetDirectoryName(targetPath)!;
        providerManifestConfiguration.SavePackage(targetPath, package);
        RefreshProviderPackageProjection(targetPath);
        ModelProviderPackageStatusText.Value = $"已保存协议适配器包 manifest：{targetPath}";
        UpdateContextPanel();
    }

    private ProviderPackageManifestValue BuildModelProviderPackageFromEditor()
    {
        var package = providerPackageProjection?.SelectedPackage is { } selectedPackage
            ? CloneModelProviderPackage(selectedPackage)
            : new ProviderPackageManifestValue();
        package.Id = ModelProviderPackageId.Value.Trim();
        package.DisplayName = ModelProviderPackageDisplayName.Value.Trim();
        package.Enabled = ModelProviderPackageEnabledIndex.Value != 1;
        package.Type = GetSelectedModelProviderPackageType();
        package.Priority = ParseIntOrDefault(ModelProviderPackagePriority.Value, 0);

        var adapter = new ProviderAdapterManifestValue
        {
            Id = ModelProviderAdapterId.Value.Trim(),
            DisplayName = ModelProviderAdapterDisplayName.Value.Trim(),
            Enabled = ModelProviderAdapterEnabledIndex.Value != 1,
            Type = GetSelectedModelProviderAdapterType(),
            AssemblyPath = NullIfWhiteSpace(ModelProviderAdapterAssemblyPath.Value),
            Priority = ParseIntOrDefault(ModelProviderAdapterPriority.Value, 10),
        };

        var adapters = package.Adapters.ToList();
        if (!string.IsNullOrWhiteSpace(adapter.Id))
        {
            var index = adapters.FindIndex(item => string.Equals(item.Id, selectedProviderAdapterId ?? adapter.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                adapters[index] = adapter;
            }
            else
            {
                adapters.Add(adapter);
            }
        }

        package.Adapters = adapters;
        return package;
    }

    private void LoadModelProviderPackageIntoEditor(ProviderPackageManifestValue package)
    {
        ModelProviderPackageId.Value = package.Id;
        ModelProviderPackageDisplayName.Value = package.DisplayName;
        ModelProviderPackageEnabledIndex.Value = package.Enabled ? 0 : 1;
        SelectModelProviderPackageType(package.Type);
        ModelProviderPackagePriority.Value = package.Priority.ToString();
        ModelProviderPackageManifestPathText.Value = $"Manifest：{package.ManifestPath}";
        ModelProviderAdapterLabels = package.Adapters.Select(static adapter => adapter.Id).ToArray();
        if (package.Adapters.Count == 0)
        {
            BeginNewModelProviderAdapter();
        }
        else
        {
            var index = string.IsNullOrWhiteSpace(selectedProviderAdapterId)
                ? 0
                : package.Adapters
                    .Select((adapter, adapterIndex) => new { adapter, adapterIndex })
                    .FirstOrDefault(item => string.Equals(item.adapter.Id, selectedProviderAdapterId, StringComparison.OrdinalIgnoreCase))
                    ?.adapterIndex ?? 0;
            LoadModelProviderAdapterIntoEditor(package.Adapters[index], index);
        }
    }

    private void LoadModelProviderAdapterIntoEditor(ProviderAdapterManifestValue adapter, int index)
    {
        selectedProviderAdapterId = adapter.Id;
        ModelProviderAdapterIndex.Value = index;
        ModelProviderAdapterId.Value = adapter.Id;
        ModelProviderAdapterDisplayName.Value = adapter.DisplayName;
        ModelProviderAdapterEnabledIndex.Value = adapter.Enabled ? 0 : 1;
        SelectModelProviderAdapterType(adapter.Type);
        ModelProviderAdapterAssemblyPath.Value = adapter.AssemblyPath ?? string.Empty;
        ModelProviderAdapterPriority.Value = adapter.Priority.ToString();
        ModelProviderAdapterResolvedPathText.Value = providerPackageProjection?.SelectedPackage is { } package
            ? $"程序集解析路径：{TianShuProviderManifestConfiguration.ResolveAdapterAssemblyFullPath(package, adapter)}"
            : "Adapter 保存后会写入当前协议适配器包的 [[adapters]]。";
    }

    private void SelectModelProviderPackageType(string? type)
    {
        var index = FindLabelIndex(ModelProviderPackageTypeLabels, type, fallbackIndex: 1);
        ModelProviderPackageTypeIndex.Value = index;
        ModelProviderPackageType.Value = ModelProviderPackageTypeLabels[index];
    }

    private void SelectModelProviderAdapterType(string? type)
    {
        var index = FindLabelIndex(ModelProviderAdapterTypeLabels, type, fallbackIndex: 0);
        ModelProviderAdapterTypeIndex.Value = index;
        ModelProviderAdapterType.Value = ModelProviderAdapterTypeLabels[index];
    }
}
