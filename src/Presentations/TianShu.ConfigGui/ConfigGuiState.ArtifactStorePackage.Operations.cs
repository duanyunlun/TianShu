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
    private void RefreshArtifactStorePackageProjection(string? selectedManifestPath)
    {
        var root = TianShuArtifactStoreManifestConfiguration.ResolveRootDirectory(ConfigPath);
        artifactStorePackageProjection = artifactStoreManifestConfiguration.Load(root, selectedManifestPath);
        ArtifactStorePackageRootText.Value = $"扫描目录：{artifactStorePackageProjection.ArtifactStoreRootDirectory}";
        ArtifactStorePackageLabels = artifactStorePackageProjection.Files.Select(static file => file.DisplayName).ToArray();
        selectedArtifactStoreManifestPath = artifactStorePackageProjection.SelectedManifestPath;

        var packageIndex = artifactStorePackageProjection.SelectedManifestPath is null
            ? 0
            : artifactStorePackageProjection.Files
                .Select((file, index) => new { file, index })
                .FirstOrDefault(item => string.Equals(item.file.Path, artifactStorePackageProjection.SelectedManifestPath, StringComparison.OrdinalIgnoreCase))
                ?.index ?? 0;
        ArtifactStorePackageIndex.Value = packageIndex;

        if (artifactStorePackageProjection.SelectedPackage is null)
        {
            BeginNewArtifactStorePackage();
            ArtifactStorePackageStatusText.Value = artifactStorePackageProjection.Issues.Count == 0
                ? "未发现工件存储包 manifest；可新建用户工件存储包。"
                : string.Join(Environment.NewLine, artifactStorePackageProjection.Issues.Select(static issue => issue.Message));
            return;
        }

        LoadArtifactStorePackageIntoEditor(artifactStorePackageProjection.SelectedPackage);
        var issueText = artifactStorePackageProjection.Issues.Count == 0
            ? string.Empty
            : $"，{artifactStorePackageProjection.Issues.Count} 条问题";
        ArtifactStorePackageStatusText.Value = $"已读取 {artifactStorePackageProjection.Files.Count} 个工件存储包 manifest{issueText}。";
    }

    public void SelectArtifactStorePackageIndex(int index)
    {
        if (artifactStorePackageProjection is null || artifactStorePackageProjection.Files.Count == 0)
        {
            BeginNewArtifactStorePackage();
            return;
        }

        var packageIndex = Math.Clamp(index, 0, artifactStorePackageProjection.Files.Count - 1);
        RefreshArtifactStorePackageProjection(artifactStorePackageProjection.Files[packageIndex].Path);
    }

    public void BeginNewArtifactStorePackage()
    {
        selectedArtifactStoreManifestPath = null;
        selectedArtifactStoreId = null;
        ArtifactStorePackageIndex.Value = 0;
        ArtifactStorePackageId.Value = CreateUniqueArtifactStorePackageId("custom-artifacts");
        ArtifactStorePackageDisplayName.Value = ArtifactStorePackageId.Value;
        ArtifactStorePackageEnabledIndex.Value = 0;
        SelectArtifactStorePackageType("filesystem");
        ArtifactStorePackagePriority.Value = "0";
        ArtifactStoreLabels = [];
        BeginNewArtifactStore();
        ArtifactStorePackageManifestPathText.Value = "保存目标：新建后写入 modules/artifacts/stores/<package-id>/store.toml";
        ArtifactStorePackageStatusText.Value = "正在新建工件存储包；保存时会写入 modules/artifacts/stores 目录。";
    }

    public void CopySelectedArtifactStorePackageToDraft()
    {
        if (artifactStorePackageProjection?.SelectedPackage is not { } package)
        {
            ArtifactStorePackageStatusText.Value = "没有可复制的工件存储包。";
            return;
        }

        selectedArtifactStoreManifestPath = null;
        ArtifactStorePackageId.Value = CreateUniqueArtifactStorePackageId(package.Id);
        ArtifactStorePackageDisplayName.Value = package.DisplayName;
        ArtifactStorePackageManifestPathText.Value = "保存目标：复制后写入 modules/artifacts/stores/<package-id>/store.toml";
        ArtifactStorePackageStatusText.Value = $"已复制工件存储包到草稿：{package.Id} -> {ArtifactStorePackageId.Value}";
    }

    public void DeleteSelectedArtifactStorePackage()
    {
        if (string.IsNullOrWhiteSpace(selectedArtifactStoreManifestPath))
        {
            ArtifactStorePackageStatusText.Value = "没有可删除的工件存储包 manifest。";
            return;
        }

        try
        {
            var root = TianShuArtifactStoreManifestConfiguration.ResolveRootDirectory(ConfigPath);
            artifactStoreManifestConfiguration.DeletePackage(root, selectedArtifactStoreManifestPath);
            selectedArtifactStoreManifestPath = null;
            RefreshArtifactStorePackageProjection(null);
            ArtifactStorePackageStatusText.Value = "已删除用户工件存储包 manifest。";
        }
        catch (Exception ex)
        {
            ArtifactStorePackageStatusText.Value = $"删除工件存储包失败：{ex.Message}";
        }
    }

    public void SelectArtifactStoreIndex(int index)
    {
        var stores = artifactStorePackageProjection?.SelectedPackage?.Stores ?? [];
        if (stores.Count == 0)
        {
            BeginNewArtifactStore();
            return;
        }

        var storeIndex = Math.Clamp(index, 0, stores.Count - 1);
        LoadArtifactStoreIntoEditor(stores[storeIndex], storeIndex);
    }

    public void BeginNewArtifactStore()
    {
        selectedArtifactStoreId = null;
        ArtifactStoreIndex.Value = 0;
        ArtifactStoreId.Value = CreateUniqueArtifactStoreId("store");
        ArtifactStoreDisplayName.Value = ArtifactStoreId.Value;
        ArtifactStoreEnabledIndex.Value = 0;
        SelectArtifactStoreType("filesystem");
        ArtifactStoreRoot.Value = "./data";
        ArtifactStoreAssemblyPath.Value = string.Empty;
        ArtifactStoreProviderType.Value = string.Empty;
        ArtifactStoreMaxHistoryVersions.Value = "20";
        ArtifactStorePriority.Value = "0";
        ArtifactStoreCrossProcessSyncIndex.Value = 0;
        ArtifactStoreResolvedPathText.Value = "Store 保存后会写入当前工件存储包的 [[stores]]。";
    }

    public void DeleteSelectedArtifactStore()
    {
        if (artifactStorePackageProjection?.SelectedPackage is not { } package || string.IsNullOrWhiteSpace(selectedArtifactStoreId))
        {
            ArtifactStorePackageStatusText.Value = "没有可删除的 Store 条目。";
            return;
        }

        package.Stores = package.Stores
            .Where(store => !string.Equals(store.Id, selectedArtifactStoreId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        selectedArtifactStoreId = null;
        SaveArtifactStorePackageValue(package);
    }

    public void SaveArtifactStorePackage()
    {
        try
        {
            var package = BuildArtifactStorePackageFromEditor();
            SaveArtifactStorePackageValue(package);
        }
        catch (Exception ex)
        {
            ArtifactStorePackageStatusText.Value = $"保存工件存储包失败：{ex.Message}";
            UpdateContextPanel();
        }
    }

    private void SaveArtifactStorePackageValue(ArtifactStorePackageManifestValue package)
    {
        var root = TianShuArtifactStoreManifestConfiguration.ResolveRootDirectory(ConfigPath);
        var targetPath = string.IsNullOrWhiteSpace(selectedArtifactStoreManifestPath)
            ? artifactStoreManifestConfiguration.CreatePackage(root, package.Id, overwrite: false)
            : selectedArtifactStoreManifestPath;
        package.ManifestPath = targetPath;
        package.PackageDirectory = Path.GetDirectoryName(targetPath)!;
        artifactStoreManifestConfiguration.SavePackage(targetPath, package);
        RefreshArtifactStorePackageProjection(targetPath);
        ArtifactStorePackageStatusText.Value = $"已保存工件存储包 manifest：{targetPath}";
        UpdateContextPanel();
    }

    private ArtifactStorePackageManifestValue BuildArtifactStorePackageFromEditor()
    {
        var package = artifactStorePackageProjection?.SelectedPackage is { } selectedPackage
            ? CloneArtifactStorePackage(selectedPackage)
            : new ArtifactStorePackageManifestValue();
        package.Id = ArtifactStorePackageId.Value.Trim();
        package.DisplayName = ArtifactStorePackageDisplayName.Value.Trim();
        package.Enabled = ArtifactStorePackageEnabledIndex.Value != 1;
        package.Type = GetSelectedArtifactStorePackageType();
        package.Priority = ParseIntOrDefault(ArtifactStorePackagePriority.Value, 0);

        var store = new ArtifactStoreManifestValue
        {
            Id = ArtifactStoreId.Value.Trim(),
            DisplayName = ArtifactStoreDisplayName.Value.Trim(),
            Enabled = ArtifactStoreEnabledIndex.Value != 1,
            Type = GetSelectedArtifactStoreType(),
            Root = NullIfWhiteSpace(ArtifactStoreRoot.Value),
            AssemblyPath = NullIfWhiteSpace(ArtifactStoreAssemblyPath.Value),
            ProviderType = NullIfWhiteSpace(ArtifactStoreProviderType.Value),
            MaxHistoryVersions = ParseNullableInt(ArtifactStoreMaxHistoryVersions.Value),
            EnableCrossProcessSync = ArtifactStoreCrossProcessSyncIndex.Value != 1,
            Priority = ParseIntOrDefault(ArtifactStorePriority.Value, 0),
        };

        var stores = package.Stores.ToList();
        if (!string.IsNullOrWhiteSpace(store.Id))
        {
            var index = stores.FindIndex(item => string.Equals(item.Id, selectedArtifactStoreId ?? store.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                stores[index] = store;
            }
            else
            {
                stores.Add(store);
            }
        }

        package.Stores = stores;
        return package;
    }

    private void LoadArtifactStorePackageIntoEditor(ArtifactStorePackageManifestValue package)
    {
        ArtifactStorePackageId.Value = package.Id;
        ArtifactStorePackageDisplayName.Value = package.DisplayName;
        ArtifactStorePackageEnabledIndex.Value = package.Enabled ? 0 : 1;
        SelectArtifactStorePackageType(package.Type);
        ArtifactStorePackagePriority.Value = package.Priority.ToString();
        ArtifactStorePackageManifestPathText.Value = $"Manifest：{package.ManifestPath}";
        ArtifactStoreLabels = package.Stores.Select(static store => store.Id).ToArray();
        if (package.Stores.Count == 0)
        {
            BeginNewArtifactStore();
        }
        else
        {
            var index = string.IsNullOrWhiteSpace(selectedArtifactStoreId)
                ? 0
                : package.Stores
                    .Select((store, storeIndex) => new { store, storeIndex })
                    .FirstOrDefault(item => string.Equals(item.store.Id, selectedArtifactStoreId, StringComparison.OrdinalIgnoreCase))
                    ?.storeIndex ?? 0;
            LoadArtifactStoreIntoEditor(package.Stores[index], index);
        }
    }

    private void LoadArtifactStoreIntoEditor(ArtifactStoreManifestValue store, int index)
    {
        selectedArtifactStoreId = store.Id;
        ArtifactStoreIndex.Value = index;
        ArtifactStoreId.Value = store.Id;
        ArtifactStoreDisplayName.Value = store.DisplayName;
        ArtifactStoreEnabledIndex.Value = store.Enabled ? 0 : 1;
        SelectArtifactStoreType(store.Type);
        ArtifactStoreRoot.Value = store.Root ?? "./data";
        ArtifactStoreAssemblyPath.Value = store.AssemblyPath ?? string.Empty;
        ArtifactStoreProviderType.Value = store.ProviderType ?? string.Empty;
        ArtifactStoreMaxHistoryVersions.Value = store.MaxHistoryVersions?.ToString() ?? string.Empty;
        ArtifactStorePriority.Value = store.Priority.ToString();
        ArtifactStoreCrossProcessSyncIndex.Value = store.EnableCrossProcessSync ? 0 : 1;
        ArtifactStoreResolvedPathText.Value = artifactStorePackageProjection?.SelectedPackage is { } package
            ? $"数据根解析路径：{TianShuArtifactStoreManifestConfiguration.ResolveStoreRootFullPath(package, store)}；程序集解析路径：{TianShuArtifactStoreManifestConfiguration.ResolveStoreAssemblyFullPath(package, store)}"
            : "Store 保存后会写入当前工件存储包的 [[stores]]。";
    }

    private void SelectArtifactStorePackageType(string? type)
    {
        var index = FindLabelIndex(ArtifactStorePackageTypeLabels, type, fallbackIndex: 1);
        ArtifactStorePackageTypeIndex.Value = index;
        ArtifactStorePackageType.Value = ArtifactStorePackageTypeLabels[index];
    }

    private void SelectArtifactStoreType(string? type)
    {
        var index = FindLabelIndex(ArtifactStoreTypeLabels, type, fallbackIndex: 0);
        ArtifactStoreTypeIndex.Value = index;
        ArtifactStoreType.Value = ArtifactStoreTypeLabels[index];
    }
}
