using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
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

internal static partial class Program
{
    private static Border BuildDiagnosticSinkPackageCollectionPanel()
    {
        var packageHeader = new Grid()
            .DockTop()
            .Columns("*,Auto")
            .Rows("Auto,Auto,Auto")
            .Spacing(8)
            .Children(
                new StackPanel()
                    .Vertical()
                    .Spacing(4)
                    .Children(
                        new TextBlock().Text("诊断输出包").Bold().FontSize(18),
                        new TextBlock().BindText(State.DiagnosticSinkPackageRootText).FontSize(11).TextWrapping(TextWrapping.Wrap)),
                new ComboBox()
                    .Row(1)
                    .Column(0)
                    .Ref(out diagnosticSinkPackageComboBox)
                    .Height(EditControlHeight)
                    .StableItems(State.DiagnosticSinkPackageLabels.ToArray())
                    .BindSelectedIndex(State.DiagnosticSinkPackageIndex)
                    .OnSelectionChanged(_ => ApplyCollectionSelection(
                        () => State.SelectDiagnosticSinkPackageIndex(State.DiagnosticSinkPackageIndex.Value),
                        RefreshDiagnosticSinkPackageControls)),
                new StackPanel()
                    .Row(1)
                    .Column(1)
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Button().Content("新建").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.BeginNewDiagnosticSinkPackage, RefreshDiagnosticSinkPackageControls)),
                        new Button().Content("复制").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.CopySelectedDiagnosticSinkPackageToDraft, RefreshDiagnosticSinkPackageControls)),
                        new Button().Content("删除").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.DeleteSelectedDiagnosticSinkPackage, RefreshViewControls))),
                new TextBlock().Row(2).ColumnSpan(2).BindText(State.DiagnosticSinkPackageManifestPathText).FontSize(11).TextWrapping(TextWrapping.Wrap));

        var packageEditor = new Grid()
            .DockTop()
            .Columns("*,300")
            .Spacing(24)
            .Children(
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text("Sink 包基本信息").Bold().FontSize(13),
                        new Grid()
                            .Columns("140,*")
                            .Rows("Auto,Auto,Auto")
                            .Spacing(10)
                            .Children(
                                new TextBlock().Text("包 ID").Bold().CenterVertical(),
                                new TextBox().Column(1).BindText(State.DiagnosticSinkPackageId).Placeholder("例如 builtin"),
                                new TextBlock().Row(1).Text("显示名").Bold().CenterVertical(),
                                new TextBox().Row(1).Column(1).BindText(State.DiagnosticSinkPackageDisplayName).Placeholder("显示名称"),
                                new TextBlock().Row(2).Text("优先级").Bold().CenterVertical(),
                                new TextBox().Row(2).Column(1).BindText(State.DiagnosticSinkPackagePriority).Placeholder("0"))),
                new StackPanel()
                    .Column(1)
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text("Sink 包加载设置").Bold().FontSize(13),
                        new Grid()
                            .Columns("120,*")
                            .Rows("Auto,Auto")
                            .Spacing(10)
                            .Children(
                                new TextBlock().Text("启用状态").Bold().CenterVertical(),
                                new ComboBox()
                                    .Column(1)
                                    .Ref(out diagnosticSinkPackageEnabledComboBox)
                                    .StableItems(State.ToolBooleanLabels.ToArray())
                                    .BindSelectedIndex(State.DiagnosticSinkPackageEnabledIndex),
                                new TextBlock().Row(1).Text("类型").Bold().CenterVertical(),
                                new ComboBox()
                                    .Row(1)
                                    .Column(1)
                                    .Ref(out diagnosticSinkPackageTypeComboBox)
                                    .StableItems(State.DiagnosticSinkPackageTypeLabels.ToArray())
                                    .BindSelectedIndex(State.DiagnosticSinkPackageTypeIndex))));

        var sinkHeader = new Grid()
            .DockTop()
            .Columns("*,Auto")
            .Rows("Auto,Auto")
            .Spacing(8)
            .Children(
                new TextBlock().Text("Sink 条目").Bold().FontSize(15),
                new ComboBox()
                    .Row(1)
                    .Column(0)
                    .Ref(out diagnosticSinkComboBox)
                    .Height(EditControlHeight)
                    .StableItems(State.DiagnosticSinkLabels.ToArray())
                    .BindSelectedIndex(State.DiagnosticSinkIndex)
                    .OnSelectionChanged(_ => ApplyCollectionSelection(
                        () => State.SelectDiagnosticSinkIndex(State.DiagnosticSinkIndex.Value),
                        RefreshDiagnosticSinkPackageControls)),
                new StackPanel()
                    .Row(1)
                    .Column(1)
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Button().Content("新增 Sink").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.BeginNewDiagnosticSink, RefreshDiagnosticSinkPackageControls)),
                        new Button().Content("删除 Sink").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.DeleteSelectedDiagnosticSink, RefreshDiagnosticSinkPackageControls))));

        var sinkEditor = new Grid()
            .DockTop()
            .Columns("*,300")
            .Spacing(24)
            .Children(
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text("Sink 基本信息").Bold().FontSize(13),
                        new Grid()
                            .Columns("140,*")
                            .Rows("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto")
                            .Spacing(10)
                            .Children(
                                new TextBlock().Text("Sink ID").Bold().CenterVertical(),
                                new TextBox().Column(1).BindText(State.DiagnosticSinkId).Placeholder("例如 turn-log"),
                                new TextBlock().Row(1).Text("显示名").Bold().CenterVertical(),
                                new TextBox().Row(1).Column(1).BindText(State.DiagnosticSinkDisplayName).Placeholder("显示名称"),
                                new TextBlock().Row(2).Text("类型").Bold().CenterVertical(),
                                new ComboBox()
                                    .Row(2)
                                    .Column(1)
                                    .Ref(out diagnosticSinkTypeComboBox)
                                    .StableItems(State.DiagnosticSinkTypeLabels.ToArray())
                                    .BindSelectedIndex(State.DiagnosticSinkTypeIndex),
                                new TextBlock().Row(3).Text("采集级别").Bold().CenterVertical(),
                                new ComboBox()
                                    .Row(3)
                                    .Column(1)
                                    .Ref(out diagnosticSinkLevelComboBox)
                                    .StableItems(State.DiagnosticSinkLevelLabels.ToArray())
                                    .BindSelectedIndex(State.DiagnosticSinkLevelIndex),
                                new TextBlock().Row(4).Text("输出目标").Bold().CenterVertical(),
                                new TextBox().Row(4).Column(1).BindText(State.DiagnosticSinkTarget).Placeholder("./artifacts/provider-requests"),
                                new TextBlock().Row(5).Text("程序集相对路径").Bold().CenterVertical(),
                                new TextBox().Row(5).Column(1).BindText(State.DiagnosticSinkAssemblyPath).Placeholder("./sink/Company.Diagnostics.dll"),
                                new TextBlock().Row(6).Text("提供方类型").Bold().CenterVertical(),
                                new TextBox().Row(6).Column(1).BindText(State.DiagnosticSinkProviderType).Placeholder("Company.Diagnostics.CustomSinkProvider"),
                                new TextBlock().Row(7).Text("遥测端点").Bold().CenterVertical(),
                                new TextBox().Row(7).Column(1).BindText(State.DiagnosticSinkEndpoint).Placeholder("https://telemetry.example.com"))),
                new StackPanel()
                    .Column(1)
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text("Sink 运行设置").Bold().FontSize(13),
                        new Grid()
                            .Columns("120,*")
                            .Rows("Auto,Auto,Auto,Auto")
                            .Spacing(10)
                            .Children(
                                new TextBlock().Text("启用状态").Bold().CenterVertical(),
                                new ComboBox()
                                    .Column(1)
                                    .Ref(out diagnosticSinkEnabledComboBox)
                                    .StableItems(State.ToolBooleanLabels.ToArray())
                                    .BindSelectedIndex(State.DiagnosticSinkEnabledIndex),
                                new TextBlock().Row(1).Text("模块").Bold().CenterVertical(),
                                new TextBox().Row(1).Column(1).BindText(State.DiagnosticSinkModules).Placeholder("provider, runtime"),
                                new TextBlock().Row(2).Text("最大字节").Bold().CenterVertical(),
                                new TextBox().Row(2).Column(1).BindText(State.DiagnosticSinkMaxBytes).Placeholder("1048576"),
                                new TextBlock().Row(3).Text("优先级").Bold().CenterVertical(),
                                new TextBox().Row(3).Column(1).BindText(State.DiagnosticSinkPriority).Placeholder("0"))));

        var content = new DockPanel()
            .Spacing(10)
            .Children(
                packageHeader,
                packageEditor,
                new StackPanel()
                    .DockTop()
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Button().Content("保存诊断输出包").OnClick(() => RunCollectionCommand(State.SaveDiagnosticSinkPackage, RefreshViewControls))),
                sinkHeader,
                sinkEditor,
                new TextBlock().DockTop().BindText(State.DiagnosticSinkResolvedPathText).FontSize(11).TextWrapping(TextWrapping.Wrap),
                new TextBlock().DockTop().BindText(State.DiagnosticSinkPackageStatusText).FontSize(11).TextWrapping(TextWrapping.Wrap));

        return new Border()
            .Padding(12)
            .BorderThickness(1)
            .CornerRadius(8)
            .WithTheme((theme, border) => border.Background(theme.Palette.ContainerBackground).BorderBrush(theme.Palette.ControlBorder))
            .Child(content);
    }
}
