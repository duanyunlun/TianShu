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
    private void RefreshDiagnosticSinkPackageProjection(string? selectedManifestPath)
    {
        var root = TianShuDiagnosticSinkManifestConfiguration.ResolveRootDirectory(ConfigPath);
        diagnosticSinkPackageProjection = diagnosticSinkManifestConfiguration.Load(root, selectedManifestPath);
        DiagnosticSinkPackageRootText.Value = $"扫描目录：{diagnosticSinkPackageProjection.DiagnosticSinkRootDirectory}";
        DiagnosticSinkPackageLabels = diagnosticSinkPackageProjection.Files.Select(static file => file.DisplayName).ToArray();
        selectedDiagnosticSinkManifestPath = diagnosticSinkPackageProjection.SelectedManifestPath;

        var packageIndex = diagnosticSinkPackageProjection.SelectedManifestPath is null
            ? 0
            : diagnosticSinkPackageProjection.Files
                .Select((file, index) => new { file, index })
                .FirstOrDefault(item => string.Equals(item.file.Path, diagnosticSinkPackageProjection.SelectedManifestPath, StringComparison.OrdinalIgnoreCase))
                ?.index ?? 0;
        DiagnosticSinkPackageIndex.Value = packageIndex;

        if (diagnosticSinkPackageProjection.SelectedPackage is null)
        {
            BeginNewDiagnosticSinkPackage();
            DiagnosticSinkPackageStatusText.Value = diagnosticSinkPackageProjection.Issues.Count == 0
                ? "未发现诊断输出包 manifest；可新建用户诊断输出包。"
                : string.Join(Environment.NewLine, diagnosticSinkPackageProjection.Issues.Select(static issue => issue.Message));
            return;
        }

        LoadDiagnosticSinkPackageIntoEditor(diagnosticSinkPackageProjection.SelectedPackage);
        var issueText = diagnosticSinkPackageProjection.Issues.Count == 0
            ? string.Empty
            : $"，{diagnosticSinkPackageProjection.Issues.Count} 条问题";
        DiagnosticSinkPackageStatusText.Value = $"已读取 {diagnosticSinkPackageProjection.Files.Count} 个诊断输出包 manifest{issueText}。";
    }

    public void SelectDiagnosticSinkPackageIndex(int index)
    {
        if (diagnosticSinkPackageProjection is null || diagnosticSinkPackageProjection.Files.Count == 0)
        {
            BeginNewDiagnosticSinkPackage();
            return;
        }

        var packageIndex = Math.Clamp(index, 0, diagnosticSinkPackageProjection.Files.Count - 1);
        RefreshDiagnosticSinkPackageProjection(diagnosticSinkPackageProjection.Files[packageIndex].Path);
    }

    public void BeginNewDiagnosticSinkPackage()
    {
        selectedDiagnosticSinkManifestPath = null;
        selectedDiagnosticSinkId = null;
        DiagnosticSinkPackageIndex.Value = 0;
        DiagnosticSinkPackageId.Value = CreateUniqueDiagnosticSinkPackageId("custom-diagnostics");
        DiagnosticSinkPackageDisplayName.Value = DiagnosticSinkPackageId.Value;
        DiagnosticSinkPackageEnabledIndex.Value = 0;
        SelectDiagnosticSinkPackageType("package");
        DiagnosticSinkPackagePriority.Value = "0";
        DiagnosticSinkLabels = [];
        BeginNewDiagnosticSink();
        DiagnosticSinkPackageManifestPathText.Value = "保存目标：新建后写入 modules/diagnostics/sinks/<package-id>/sink.toml";
        DiagnosticSinkPackageStatusText.Value = "正在新建诊断输出包；保存时会写入 modules/diagnostics/sinks 目录。";
    }

    public void CopySelectedDiagnosticSinkPackageToDraft()
    {
        if (diagnosticSinkPackageProjection?.SelectedPackage is not { } package)
        {
            DiagnosticSinkPackageStatusText.Value = "没有可复制的诊断输出包。";
            return;
        }

        selectedDiagnosticSinkManifestPath = null;
        DiagnosticSinkPackageId.Value = CreateUniqueDiagnosticSinkPackageId(package.Id);
        DiagnosticSinkPackageDisplayName.Value = package.DisplayName;
        DiagnosticSinkPackageManifestPathText.Value = "保存目标：复制后写入 modules/diagnostics/sinks/<package-id>/sink.toml";
        DiagnosticSinkPackageStatusText.Value = $"已复制诊断输出包到草稿：{package.Id} -> {DiagnosticSinkPackageId.Value}";
    }

    public void DeleteSelectedDiagnosticSinkPackage()
    {
        if (string.IsNullOrWhiteSpace(selectedDiagnosticSinkManifestPath))
        {
            DiagnosticSinkPackageStatusText.Value = "没有可删除的诊断输出包 manifest。";
            return;
        }

        try
        {
            var root = TianShuDiagnosticSinkManifestConfiguration.ResolveRootDirectory(ConfigPath);
            diagnosticSinkManifestConfiguration.DeletePackage(root, selectedDiagnosticSinkManifestPath);
            selectedDiagnosticSinkManifestPath = null;
            RefreshDiagnosticSinkPackageProjection(null);
            DiagnosticSinkPackageStatusText.Value = "已删除用户诊断输出包 manifest。";
        }
        catch (Exception ex)
        {
            DiagnosticSinkPackageStatusText.Value = $"删除诊断输出包失败：{ex.Message}";
        }
    }

    public void SelectDiagnosticSinkIndex(int index)
    {
        var sinks = diagnosticSinkPackageProjection?.SelectedPackage?.Sinks ?? [];
        if (sinks.Count == 0)
        {
            BeginNewDiagnosticSink();
            return;
        }

        var sinkIndex = Math.Clamp(index, 0, sinks.Count - 1);
        LoadDiagnosticSinkIntoEditor(sinks[sinkIndex], sinkIndex);
    }

    public void BeginNewDiagnosticSink()
    {
        selectedDiagnosticSinkId = null;
        DiagnosticSinkIndex.Value = 0;
        DiagnosticSinkId.Value = CreateUniqueDiagnosticSinkId("sink");
        DiagnosticSinkDisplayName.Value = DiagnosticSinkId.Value;
        DiagnosticSinkEnabledIndex.Value = 0;
        SelectDiagnosticSinkType("turn-log");
        SelectDiagnosticSinkLevel("stats");
        DiagnosticSinkTarget.Value = string.Empty;
        DiagnosticSinkAssemblyPath.Value = string.Empty;
        DiagnosticSinkProviderType.Value = string.Empty;
        DiagnosticSinkEndpoint.Value = string.Empty;
        DiagnosticSinkModules.Value = string.Empty;
        DiagnosticSinkMaxBytes.Value = string.Empty;
        DiagnosticSinkPriority.Value = "0";
        DiagnosticSinkResolvedPathText.Value = "Sink 保存后会写入当前诊断输出包的 [[sinks]]。";
    }

    public void DeleteSelectedDiagnosticSink()
    {
        if (diagnosticSinkPackageProjection?.SelectedPackage is not { } package || string.IsNullOrWhiteSpace(selectedDiagnosticSinkId))
        {
            DiagnosticSinkPackageStatusText.Value = "没有可删除的 Sink 条目。";
            return;
        }

        package.Sinks = package.Sinks
            .Where(sink => !string.Equals(sink.Id, selectedDiagnosticSinkId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        selectedDiagnosticSinkId = null;
        SaveDiagnosticSinkPackageValue(package);
    }

    public void SaveDiagnosticSinkPackage()
    {
        try
        {
            var package = BuildDiagnosticSinkPackageFromEditor();
            SaveDiagnosticSinkPackageValue(package);
        }
        catch (Exception ex)
        {
            DiagnosticSinkPackageStatusText.Value = $"保存诊断输出包失败：{ex.Message}";
            UpdateContextPanel();
        }
    }

    private void SaveDiagnosticSinkPackageValue(DiagnosticSinkPackageManifestValue package)
    {
        var root = TianShuDiagnosticSinkManifestConfiguration.ResolveRootDirectory(ConfigPath);
        var targetPath = string.IsNullOrWhiteSpace(selectedDiagnosticSinkManifestPath)
            ? diagnosticSinkManifestConfiguration.CreatePackage(root, package.Id, overwrite: false)
            : selectedDiagnosticSinkManifestPath;
        package.ManifestPath = targetPath;
        package.PackageDirectory = Path.GetDirectoryName(targetPath)!;
        diagnosticSinkManifestConfiguration.SavePackage(targetPath, package);
        RefreshDiagnosticSinkPackageProjection(targetPath);
        DiagnosticSinkPackageStatusText.Value = $"已保存诊断输出包 manifest：{targetPath}";
        UpdateContextPanel();
    }

    private DiagnosticSinkPackageManifestValue BuildDiagnosticSinkPackageFromEditor()
    {
        var package = diagnosticSinkPackageProjection?.SelectedPackage is { } selectedPackage
            ? CloneDiagnosticSinkPackage(selectedPackage)
            : new DiagnosticSinkPackageManifestValue();
        package.Id = DiagnosticSinkPackageId.Value.Trim();
        package.DisplayName = DiagnosticSinkPackageDisplayName.Value.Trim();
        package.Enabled = DiagnosticSinkPackageEnabledIndex.Value != 1;
        package.Type = GetSelectedDiagnosticSinkPackageType();
        package.Priority = ParseIntOrDefault(DiagnosticSinkPackagePriority.Value, 0);

        var sink = new DiagnosticSinkManifestValue
        {
            Id = DiagnosticSinkId.Value.Trim(),
            DisplayName = DiagnosticSinkDisplayName.Value.Trim(),
            Enabled = DiagnosticSinkEnabledIndex.Value != 1,
            Type = GetSelectedDiagnosticSinkType(),
            Level = GetSelectedDiagnosticSinkLevel(),
            Target = NullIfWhiteSpace(DiagnosticSinkTarget.Value),
            AssemblyPath = NullIfWhiteSpace(DiagnosticSinkAssemblyPath.Value),
            ProviderType = NullIfWhiteSpace(DiagnosticSinkProviderType.Value),
            Endpoint = NullIfWhiteSpace(DiagnosticSinkEndpoint.Value),
            Modules = SplitCommaList(DiagnosticSinkModules.Value),
            MaxBytes = ParseNullableLong(DiagnosticSinkMaxBytes.Value),
            Priority = ParseIntOrDefault(DiagnosticSinkPriority.Value, 0),
        };

        var sinks = package.Sinks.ToList();
        if (!string.IsNullOrWhiteSpace(sink.Id))
        {
            var index = sinks.FindIndex(item => string.Equals(item.Id, selectedDiagnosticSinkId ?? sink.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                sinks[index] = sink;
            }
            else
            {
                sinks.Add(sink);
            }
        }

        package.Sinks = sinks;
        return package;
    }

    private void LoadDiagnosticSinkPackageIntoEditor(DiagnosticSinkPackageManifestValue package)
    {
        DiagnosticSinkPackageId.Value = package.Id;
        DiagnosticSinkPackageDisplayName.Value = package.DisplayName;
        DiagnosticSinkPackageEnabledIndex.Value = package.Enabled ? 0 : 1;
        SelectDiagnosticSinkPackageType(package.Type);
        DiagnosticSinkPackagePriority.Value = package.Priority.ToString();
        DiagnosticSinkPackageManifestPathText.Value = $"Manifest：{package.ManifestPath}";
        DiagnosticSinkLabels = package.Sinks.Select(static sink => sink.Id).ToArray();
        if (package.Sinks.Count == 0)
        {
            BeginNewDiagnosticSink();
        }
        else
        {
            var index = string.IsNullOrWhiteSpace(selectedDiagnosticSinkId)
                ? 0
                : package.Sinks
                    .Select((sink, sinkIndex) => new { sink, sinkIndex })
                    .FirstOrDefault(item => string.Equals(item.sink.Id, selectedDiagnosticSinkId, StringComparison.OrdinalIgnoreCase))
                    ?.sinkIndex ?? 0;
            LoadDiagnosticSinkIntoEditor(package.Sinks[index], index);
        }
    }

    private void LoadDiagnosticSinkIntoEditor(DiagnosticSinkManifestValue sink, int index)
    {
        selectedDiagnosticSinkId = sink.Id;
        DiagnosticSinkIndex.Value = index;
        DiagnosticSinkId.Value = sink.Id;
        DiagnosticSinkDisplayName.Value = sink.DisplayName;
        DiagnosticSinkEnabledIndex.Value = sink.Enabled ? 0 : 1;
        SelectDiagnosticSinkType(sink.Type);
        SelectDiagnosticSinkLevel(sink.Level);
        DiagnosticSinkTarget.Value = sink.Target ?? string.Empty;
        DiagnosticSinkAssemblyPath.Value = sink.AssemblyPath ?? string.Empty;
        DiagnosticSinkProviderType.Value = sink.ProviderType ?? string.Empty;
        DiagnosticSinkEndpoint.Value = sink.Endpoint ?? string.Empty;
        DiagnosticSinkModules.Value = JoinList(sink.Modules);
        DiagnosticSinkMaxBytes.Value = sink.MaxBytes?.ToString() ?? string.Empty;
        DiagnosticSinkPriority.Value = sink.Priority.ToString();
        DiagnosticSinkResolvedPathText.Value = diagnosticSinkPackageProjection?.SelectedPackage is { } package
            ? $"目标解析路径：{TianShuDiagnosticSinkManifestConfiguration.ResolveSinkTargetFullPath(package, sink)}；程序集解析路径：{TianShuDiagnosticSinkManifestConfiguration.ResolveSinkAssemblyFullPath(package, sink)}"
            : "Sink 保存后会写入当前诊断输出包的 [[sinks]]。";
    }

    private void SelectDiagnosticSinkPackageType(string? type)
    {
        var index = FindLabelIndex(DiagnosticSinkPackageTypeLabels, type, fallbackIndex: 1);
        DiagnosticSinkPackageTypeIndex.Value = index;
        DiagnosticSinkPackageType.Value = DiagnosticSinkPackageTypeLabels[index];
    }

    private void SelectDiagnosticSinkType(string? type)
    {
        var index = FindLabelIndex(DiagnosticSinkTypeLabels, type, fallbackIndex: 0);
        DiagnosticSinkTypeIndex.Value = index;
        DiagnosticSinkType.Value = DiagnosticSinkTypeLabels[index];
    }

    private void SelectDiagnosticSinkLevel(string? level)
    {
        var index = FindLabelIndex(DiagnosticSinkLevelLabels, level, fallbackIndex: 2);
        DiagnosticSinkLevelIndex.Value = index;
        DiagnosticSinkLevel.Value = DiagnosticSinkLevelLabels[index];
    }
}
