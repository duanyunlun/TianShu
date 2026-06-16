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
    private void RefreshWorkspaceResolverPackageProjection(string? selectedManifestPath)
    {
        var root = TianShuWorkspaceResolverManifestConfiguration.ResolveRootDirectory(ConfigPath);
        workspaceResolverPackageProjection = workspaceResolverManifestConfiguration.Load(root, selectedManifestPath);
        WorkspaceResolverPackageRootText.Value = $"扫描目录：{workspaceResolverPackageProjection.WorkspaceResolverRootDirectory}";
        WorkspaceResolverPackageLabels = workspaceResolverPackageProjection.Files.Select(static file => file.DisplayName).ToArray();
        selectedWorkspaceResolverManifestPath = workspaceResolverPackageProjection.SelectedManifestPath;

        var packageIndex = workspaceResolverPackageProjection.SelectedManifestPath is null
            ? 0
            : workspaceResolverPackageProjection.Files
                .Select((file, index) => new { file, index })
                .FirstOrDefault(item => string.Equals(item.file.Path, workspaceResolverPackageProjection.SelectedManifestPath, StringComparison.OrdinalIgnoreCase))
                ?.index ?? 0;
        WorkspaceResolverPackageIndex.Value = packageIndex;

        if (workspaceResolverPackageProjection.SelectedPackage is null)
        {
            BeginNewWorkspaceResolverPackage();
            WorkspaceResolverPackageStatusText.Value = workspaceResolverPackageProjection.Issues.Count == 0
                ? "未发现工作空间解析包 manifest；可新建用户解析包。"
                : string.Join(Environment.NewLine, workspaceResolverPackageProjection.Issues.Select(static issue => issue.Message));
            return;
        }

        LoadWorkspaceResolverPackageIntoEditor(workspaceResolverPackageProjection.SelectedPackage);
        var issueText = workspaceResolverPackageProjection.Issues.Count == 0
            ? string.Empty
            : $"，{workspaceResolverPackageProjection.Issues.Count} 条问题";
        WorkspaceResolverPackageStatusText.Value = $"已读取 {workspaceResolverPackageProjection.Files.Count} 个工作空间解析包 manifest{issueText}。";
    }

    public void SelectWorkspaceResolverPackageIndex(int index)
    {
        if (workspaceResolverPackageProjection is null || workspaceResolverPackageProjection.Files.Count == 0)
        {
            BeginNewWorkspaceResolverPackage();
            return;
        }

        var packageIndex = Math.Clamp(index, 0, workspaceResolverPackageProjection.Files.Count - 1);
        RefreshWorkspaceResolverPackageProjection(workspaceResolverPackageProjection.Files[packageIndex].Path);
    }

    public void BeginNewWorkspaceResolverPackage()
    {
        selectedWorkspaceResolverManifestPath = null;
        selectedWorkspaceResolverId = null;
        WorkspaceResolverPackageIndex.Value = 0;
        WorkspaceResolverPackageId.Value = CreateUniqueWorkspaceResolverPackageId("custom-workspace");
        WorkspaceResolverPackageDisplayName.Value = WorkspaceResolverPackageId.Value;
        WorkspaceResolverPackageEnabledIndex.Value = 0;
        SelectWorkspaceResolverPackageType("package");
        WorkspaceResolverPackagePriority.Value = "0";
        WorkspaceResolverLabels = [];
        BeginNewWorkspaceResolver();
        WorkspaceResolverPackageManifestPathText.Value = "保存目标：新建后写入 modules/workspace/resolvers/<package-id>/resolver.toml";
        WorkspaceResolverPackageStatusText.Value = "正在新建工作空间解析包；保存时会写入 modules/workspace/resolvers 目录。";
    }

    public void CopySelectedWorkspaceResolverPackageToDraft()
    {
        if (workspaceResolverPackageProjection?.SelectedPackage is not { } package)
        {
            WorkspaceResolverPackageStatusText.Value = "没有可复制的工作空间解析包。";
            return;
        }

        selectedWorkspaceResolverManifestPath = null;
        WorkspaceResolverPackageId.Value = CreateUniqueWorkspaceResolverPackageId(package.Id);
        WorkspaceResolverPackageDisplayName.Value = package.DisplayName;
        WorkspaceResolverPackageManifestPathText.Value = "保存目标：复制后写入 modules/workspace/resolvers/<package-id>/resolver.toml";
        WorkspaceResolverPackageStatusText.Value = $"已复制工作空间解析包到草稿：{package.Id} -> {WorkspaceResolverPackageId.Value}";
    }

    public void DeleteSelectedWorkspaceResolverPackage()
    {
        if (string.IsNullOrWhiteSpace(selectedWorkspaceResolverManifestPath))
        {
            WorkspaceResolverPackageStatusText.Value = "没有可删除的工作空间解析包 manifest。";
            return;
        }

        try
        {
            var root = TianShuWorkspaceResolverManifestConfiguration.ResolveRootDirectory(ConfigPath);
            workspaceResolverManifestConfiguration.DeletePackage(root, selectedWorkspaceResolverManifestPath);
            selectedWorkspaceResolverManifestPath = null;
            RefreshWorkspaceResolverPackageProjection(null);
            WorkspaceResolverPackageStatusText.Value = "已删除用户工作空间解析包 manifest。";
        }
        catch (Exception ex)
        {
            WorkspaceResolverPackageStatusText.Value = $"删除工作空间解析包失败：{ex.Message}";
        }
    }

    public void SelectWorkspaceResolverIndex(int index)
    {
        var resolvers = workspaceResolverPackageProjection?.SelectedPackage?.Resolvers ?? [];
        if (resolvers.Count == 0)
        {
            BeginNewWorkspaceResolver();
            return;
        }

        var resolverIndex = Math.Clamp(index, 0, resolvers.Count - 1);
        LoadWorkspaceResolverIntoEditor(resolvers[resolverIndex], resolverIndex);
    }

    public void BeginNewWorkspaceResolver()
    {
        selectedWorkspaceResolverId = null;
        WorkspaceResolverIndex.Value = 0;
        WorkspaceResolverId.Value = CreateUniqueWorkspaceResolverId("resolver");
        WorkspaceResolverDisplayName.Value = WorkspaceResolverId.Value;
        WorkspaceResolverEnabledIndex.Value = 0;
        SelectWorkspaceResolverType("marker");
        WorkspaceResolverRootMarkers.Value = ".git, .tianshu";
        WorkspaceResolverProfile.Value = "default";
        WorkspaceResolverTrustPolicy.Value = "prompt";
        WorkspaceResolverArtifactRoot.Value = ".tianshu/artifacts";
        WorkspaceResolverStateRoot.Value = ".tianshu/state";
        WorkspaceResolverIgnoreGlobs.Value = "bin/**, obj/**";
        WorkspaceResolverLanguageMarkers.Value = string.Empty;
        WorkspaceResolverFrameworkMarkers.Value = string.Empty;
        WorkspaceResolverAssemblyPath.Value = string.Empty;
        WorkspaceResolverProviderType.Value = string.Empty;
        WorkspaceResolverPriority.Value = "0";
        WorkspaceResolverResolvedPathText.Value = "Resolver 保存后会写入当前工作空间解析包的 [[resolvers]]。";
    }

    public void DeleteSelectedWorkspaceResolver()
    {
        if (workspaceResolverPackageProjection?.SelectedPackage is not { } package || string.IsNullOrWhiteSpace(selectedWorkspaceResolverId))
        {
            WorkspaceResolverPackageStatusText.Value = "没有可删除的 Resolver 条目。";
            return;
        }

        package.Resolvers = package.Resolvers
            .Where(resolver => !string.Equals(resolver.Id, selectedWorkspaceResolverId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        selectedWorkspaceResolverId = null;
        SaveWorkspaceResolverPackageValue(package);
    }

    public void SaveWorkspaceResolverPackage()
    {
        try
        {
            var package = BuildWorkspaceResolverPackageFromEditor();
            SaveWorkspaceResolverPackageValue(package);
        }
        catch (Exception ex)
        {
            WorkspaceResolverPackageStatusText.Value = $"保存工作空间解析包失败：{ex.Message}";
            UpdateContextPanel();
        }
    }

    private void SaveWorkspaceResolverPackageValue(WorkspaceResolverPackageManifestValue package)
    {
        var root = TianShuWorkspaceResolverManifestConfiguration.ResolveRootDirectory(ConfigPath);
        var targetPath = string.IsNullOrWhiteSpace(selectedWorkspaceResolverManifestPath)
            ? workspaceResolverManifestConfiguration.CreatePackage(root, package.Id, overwrite: false)
            : selectedWorkspaceResolverManifestPath;
        package.ManifestPath = targetPath;
        package.PackageDirectory = Path.GetDirectoryName(targetPath)!;
        workspaceResolverManifestConfiguration.SavePackage(targetPath, package);
        RefreshWorkspaceResolverPackageProjection(targetPath);
        WorkspaceResolverPackageStatusText.Value = $"已保存工作空间解析包 manifest：{targetPath}";
        UpdateContextPanel();
    }

    private WorkspaceResolverPackageManifestValue BuildWorkspaceResolverPackageFromEditor()
    {
        var package = workspaceResolverPackageProjection?.SelectedPackage is { } selectedPackage
            ? CloneWorkspaceResolverPackage(selectedPackage)
            : new WorkspaceResolverPackageManifestValue();
        package.Id = WorkspaceResolverPackageId.Value.Trim();
        package.DisplayName = WorkspaceResolverPackageDisplayName.Value.Trim();
        package.Enabled = WorkspaceResolverPackageEnabledIndex.Value != 1;
        package.Type = GetSelectedWorkspaceResolverPackageType();
        package.Priority = ParseIntOrDefault(WorkspaceResolverPackagePriority.Value, 0);

        var resolver = new WorkspaceResolverManifestValue
        {
            Id = WorkspaceResolverId.Value.Trim(),
            DisplayName = WorkspaceResolverDisplayName.Value.Trim(),
            Enabled = WorkspaceResolverEnabledIndex.Value != 1,
            Type = GetSelectedWorkspaceResolverType(),
            RootMarkers = SplitCommaList(WorkspaceResolverRootMarkers.Value),
            Profile = NullIfWhiteSpace(WorkspaceResolverProfile.Value),
            TrustPolicy = NullIfWhiteSpace(WorkspaceResolverTrustPolicy.Value),
            ArtifactRoot = NullIfWhiteSpace(WorkspaceResolverArtifactRoot.Value),
            StateRoot = NullIfWhiteSpace(WorkspaceResolverStateRoot.Value),
            IgnoreGlobs = SplitCommaList(WorkspaceResolverIgnoreGlobs.Value),
            LanguageMarkers = SplitCommaList(WorkspaceResolverLanguageMarkers.Value),
            FrameworkMarkers = SplitCommaList(WorkspaceResolverFrameworkMarkers.Value),
            AssemblyPath = NullIfWhiteSpace(WorkspaceResolverAssemblyPath.Value),
            ProviderType = NullIfWhiteSpace(WorkspaceResolverProviderType.Value),
            Priority = ParseIntOrDefault(WorkspaceResolverPriority.Value, 0),
        };

        var resolvers = package.Resolvers.ToList();
        if (!string.IsNullOrWhiteSpace(resolver.Id))
        {
            var index = resolvers.FindIndex(item => string.Equals(item.Id, selectedWorkspaceResolverId ?? resolver.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                resolvers[index] = resolver;
            }
            else
            {
                resolvers.Add(resolver);
            }
        }

        package.Resolvers = resolvers;
        return package;
    }

    private void LoadWorkspaceResolverPackageIntoEditor(WorkspaceResolverPackageManifestValue package)
    {
        WorkspaceResolverPackageId.Value = package.Id;
        WorkspaceResolverPackageDisplayName.Value = package.DisplayName;
        WorkspaceResolverPackageEnabledIndex.Value = package.Enabled ? 0 : 1;
        SelectWorkspaceResolverPackageType(package.Type);
        WorkspaceResolverPackagePriority.Value = package.Priority.ToString();
        WorkspaceResolverPackageManifestPathText.Value = $"Manifest：{package.ManifestPath}";
        WorkspaceResolverLabels = package.Resolvers.Select(static resolver => resolver.Id).ToArray();
        if (package.Resolvers.Count == 0)
        {
            BeginNewWorkspaceResolver();
        }
        else
        {
            var index = string.IsNullOrWhiteSpace(selectedWorkspaceResolverId)
                ? 0
                : package.Resolvers
                    .Select((resolver, resolverIndex) => new { resolver, resolverIndex })
                    .FirstOrDefault(item => string.Equals(item.resolver.Id, selectedWorkspaceResolverId, StringComparison.OrdinalIgnoreCase))
                    ?.resolverIndex ?? 0;
            LoadWorkspaceResolverIntoEditor(package.Resolvers[index], index);
        }
    }

    private void LoadWorkspaceResolverIntoEditor(WorkspaceResolverManifestValue resolver, int index)
    {
        selectedWorkspaceResolverId = resolver.Id;
        WorkspaceResolverIndex.Value = index;
        WorkspaceResolverId.Value = resolver.Id;
        WorkspaceResolverDisplayName.Value = resolver.DisplayName;
        WorkspaceResolverEnabledIndex.Value = resolver.Enabled ? 0 : 1;
        SelectWorkspaceResolverType(resolver.Type);
        WorkspaceResolverRootMarkers.Value = JoinList(resolver.RootMarkers);
        WorkspaceResolverProfile.Value = resolver.Profile ?? string.Empty;
        WorkspaceResolverTrustPolicy.Value = resolver.TrustPolicy ?? string.Empty;
        WorkspaceResolverArtifactRoot.Value = resolver.ArtifactRoot ?? string.Empty;
        WorkspaceResolverStateRoot.Value = resolver.StateRoot ?? string.Empty;
        WorkspaceResolverIgnoreGlobs.Value = JoinList(resolver.IgnoreGlobs);
        WorkspaceResolverLanguageMarkers.Value = JoinList(resolver.LanguageMarkers);
        WorkspaceResolverFrameworkMarkers.Value = JoinList(resolver.FrameworkMarkers);
        WorkspaceResolverAssemblyPath.Value = resolver.AssemblyPath ?? string.Empty;
        WorkspaceResolverProviderType.Value = resolver.ProviderType ?? string.Empty;
        WorkspaceResolverPriority.Value = resolver.Priority.ToString();
        WorkspaceResolverResolvedPathText.Value = workspaceResolverPackageProjection?.SelectedPackage is { } package
            ? $"Artifact 根解析路径：{TianShuWorkspaceResolverManifestConfiguration.ResolveArtifactRootFullPath(package, resolver)}；State 根解析路径：{TianShuWorkspaceResolverManifestConfiguration.ResolveStateRootFullPath(package, resolver)}；程序集解析路径：{TianShuWorkspaceResolverManifestConfiguration.ResolveAssemblyFullPath(package, resolver)}"
            : "Resolver 保存后会写入当前工作空间解析包的 [[resolvers]]。";
    }

    private WorkspaceResolverPackageManifestValue CloneWorkspaceResolverPackage(WorkspaceResolverPackageManifestValue source)
        => new()
        {
            Id = source.Id,
            DisplayName = source.DisplayName,
            Enabled = source.Enabled,
            Type = source.Type,
            Priority = source.Priority,
            ManifestPath = source.ManifestPath,
            PackageDirectory = source.PackageDirectory,
            Resolvers = source.Resolvers.Select(static resolver => new WorkspaceResolverManifestValue
            {
                Id = resolver.Id,
                DisplayName = resolver.DisplayName,
                Enabled = resolver.Enabled,
                Type = resolver.Type,
                Priority = resolver.Priority,
                RootMarkers = resolver.RootMarkers.ToArray(),
                Profile = resolver.Profile,
                TrustPolicy = resolver.TrustPolicy,
                ArtifactRoot = resolver.ArtifactRoot,
                StateRoot = resolver.StateRoot,
                IgnoreGlobs = resolver.IgnoreGlobs.ToArray(),
                LanguageMarkers = resolver.LanguageMarkers.ToArray(),
                FrameworkMarkers = resolver.FrameworkMarkers.ToArray(),
                AssemblyPath = resolver.AssemblyPath,
                ProviderType = resolver.ProviderType,
            }).ToArray(),
        };

    private void SelectWorkspaceResolverPackageType(string? type)
    {
        var index = FindLabelIndex(WorkspaceResolverPackageTypeLabels, type, fallbackIndex: 1);
        WorkspaceResolverPackageTypeIndex.Value = index;
        WorkspaceResolverPackageType.Value = WorkspaceResolverPackageTypeLabels[index];
    }

    private void SelectWorkspaceResolverType(string? type)
    {
        var index = FindLabelIndex(WorkspaceResolverTypeLabels, type, fallbackIndex: 0);
        WorkspaceResolverTypeIndex.Value = index;
        WorkspaceResolverType.Value = WorkspaceResolverTypeLabels[index];
    }

    private string GetSelectedWorkspaceResolverPackageType()
    {
        var index = Math.Clamp(WorkspaceResolverPackageTypeIndex.Value, 0, WorkspaceResolverPackageTypeLabels.Count - 1);
        WorkspaceResolverPackageType.Value = WorkspaceResolverPackageTypeLabels[index];
        return WorkspaceResolverPackageType.Value;
    }

    private string GetSelectedWorkspaceResolverType()
    {
        var index = Math.Clamp(WorkspaceResolverTypeIndex.Value, 0, WorkspaceResolverTypeLabels.Count - 1);
        WorkspaceResolverType.Value = WorkspaceResolverTypeLabels[index];
        return WorkspaceResolverType.Value;
    }

    private string CreateUniqueWorkspaceResolverPackageId(string baseId)
    {
        var root = TianShuWorkspaceResolverManifestConfiguration.ResolveRootDirectory(ConfigPath);
        var existingIds = workspaceResolverPackageProjection?.Files
            .Select(file => Path.GetFileName(Path.GetDirectoryName(file.Path)))
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var normalized = string.IsNullOrWhiteSpace(baseId) ? "custom-workspace" : baseId.Trim();
        var packageRoot = TianShuWorkspaceResolverManifestConfiguration.ResolveWorkspaceResolverRootDirectory(root);
        for (var index = 0; index < 1000; index++)
        {
            var candidate = index == 0 ? normalized : $"{normalized}-{index + 1}";
            if (!existingIds.Contains(candidate) && !Directory.Exists(Path.Combine(packageRoot, candidate)))
            {
                return candidate;
            }
        }

        return $"{normalized}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    }

    private string CreateUniqueWorkspaceResolverId(string baseId)
    {
        var existingIds = workspaceResolverPackageProjection?.SelectedPackage?.Resolvers
            .Select(static resolver => resolver.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var normalized = string.IsNullOrWhiteSpace(baseId) ? "resolver" : baseId.Trim();
        for (var index = 0; index < 1000; index++)
        {
            var candidate = index == 0 ? normalized : $"{normalized}-{index + 1}";
            if (!existingIds.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{normalized}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    }
}
