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
    private static Border BuildArtifactStorePackageCollectionPanel()
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
                        new TextBlock().Text("工件存储包").Bold().FontSize(18),
                        new TextBlock().BindText(State.ArtifactStorePackageRootText).FontSize(11).TextWrapping(TextWrapping.Wrap)),
                new ComboBox()
                    .Row(1)
                    .Column(0)
                    .Ref(out artifactStorePackageComboBox)
                    .Height(EditControlHeight)
                    .StableItems(State.ArtifactStorePackageLabels.ToArray())
                    .BindSelectedIndex(State.ArtifactStorePackageIndex)
                    .OnSelectionChanged(_ => ApplyCollectionSelection(
                        () => State.SelectArtifactStorePackageIndex(State.ArtifactStorePackageIndex.Value),
                        RefreshArtifactStorePackageControls)),
                new StackPanel()
                    .Row(1)
                    .Column(1)
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Button().Content("新建").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.BeginNewArtifactStorePackage, RefreshArtifactStorePackageControls)),
                        new Button().Content("复制").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.CopySelectedArtifactStorePackageToDraft, RefreshArtifactStorePackageControls)),
                        new Button().Content("删除").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.DeleteSelectedArtifactStorePackage, RefreshViewControls))),
                new TextBlock().Row(2).ColumnSpan(2).BindText(State.ArtifactStorePackageManifestPathText).FontSize(11).TextWrapping(TextWrapping.Wrap));

        var packageEditor = new Grid()
            .DockTop()
            .Columns("*,300")
            .Spacing(24)
            .Children(
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text("存储包基本信息").Bold().FontSize(13),
                        new Grid()
                            .Columns("140,*")
                            .Rows("Auto,Auto,Auto")
                            .Spacing(10)
                            .Children(
                                new TextBlock().Text("包 ID").Bold().CenterVertical(),
                                new TextBox().Column(1).BindText(State.ArtifactStorePackageId).Placeholder("例如 builtin"),
                                new TextBlock().Row(1).Text("显示名").Bold().CenterVertical(),
                                new TextBox().Row(1).Column(1).BindText(State.ArtifactStorePackageDisplayName).Placeholder("显示名称"),
                                new TextBlock().Row(2).Text("优先级").Bold().CenterVertical(),
                                new TextBox().Row(2).Column(1).BindText(State.ArtifactStorePackagePriority).Placeholder("0"))),
                new StackPanel()
                    .Column(1)
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text("存储包加载设置").Bold().FontSize(13),
                        new Grid()
                            .Columns("120,*")
                            .Rows("Auto,Auto")
                            .Spacing(10)
                            .Children(
                                new TextBlock().Text("启用状态").Bold().CenterVertical(),
                                new ComboBox()
                                    .Column(1)
                                    .Ref(out artifactStorePackageEnabledComboBox)
                                    .StableItems(State.ToolBooleanLabels.ToArray())
                                    .BindSelectedIndex(State.ArtifactStorePackageEnabledIndex),
                                new TextBlock().Row(1).Text("类型").Bold().CenterVertical(),
                                new ComboBox()
                                    .Row(1)
                                    .Column(1)
                                    .Ref(out artifactStorePackageTypeComboBox)
                                    .StableItems(State.ArtifactStorePackageTypeLabels.ToArray())
                                    .BindSelectedIndex(State.ArtifactStorePackageTypeIndex))));

        var storeHeader = new Grid()
            .DockTop()
            .Columns("*,Auto")
            .Rows("Auto,Auto")
            .Spacing(8)
            .Children(
                new TextBlock().Text("Store 条目").Bold().FontSize(15),
                new ComboBox()
                    .Row(1)
                    .Column(0)
                    .Ref(out artifactStoreComboBox)
                    .Height(EditControlHeight)
                    .StableItems(State.ArtifactStoreLabels.ToArray())
                    .BindSelectedIndex(State.ArtifactStoreIndex)
                    .OnSelectionChanged(_ => ApplyCollectionSelection(
                        () => State.SelectArtifactStoreIndex(State.ArtifactStoreIndex.Value),
                        RefreshArtifactStorePackageControls)),
                new StackPanel()
                    .Row(1)
                    .Column(1)
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Button().Content("新增 Store").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.BeginNewArtifactStore, RefreshArtifactStorePackageControls)),
                        new Button().Content("删除 Store").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.DeleteSelectedArtifactStore, RefreshArtifactStorePackageControls))));

        var storeEditor = new Grid()
            .DockTop()
            .Columns("*,300")
            .Spacing(24)
            .Children(
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text("Store 基本信息").Bold().FontSize(13),
                        new Grid()
                            .Columns("140,*")
                            .Rows("Auto,Auto,Auto,Auto,Auto,Auto")
                            .Spacing(10)
                            .Children(
                                new TextBlock().Text("Store ID").Bold().CenterVertical(),
                                new TextBox().Column(1).BindText(State.ArtifactStoreId).Placeholder("例如 local-filesystem"),
                                new TextBlock().Row(1).Text("显示名").Bold().CenterVertical(),
                                new TextBox().Row(1).Column(1).BindText(State.ArtifactStoreDisplayName).Placeholder("显示名称"),
                                new TextBlock().Row(2).Text("类型").Bold().CenterVertical(),
                                new ComboBox()
                                    .Row(2)
                                    .Column(1)
                                    .Ref(out artifactStoreTypeComboBox)
                                    .StableItems(State.ArtifactStoreTypeLabels.ToArray())
                                    .BindSelectedIndex(State.ArtifactStoreTypeIndex),
                                new TextBlock().Row(3).Text("数据根目录").Bold().CenterVertical(),
                                new TextBox().Row(3).Column(1).BindText(State.ArtifactStoreRoot).Placeholder("./data"),
                                new TextBlock().Row(4).Text("程序集相对路径").Bold().CenterVertical(),
                                new TextBox().Row(4).Column(1).BindText(State.ArtifactStoreAssemblyPath).Placeholder("./store/TianShu.ArtifactStore.Custom.dll"),
                                new TextBlock().Row(5).Text("提供方类型").Bold().CenterVertical(),
                                new TextBox().Row(5).Column(1).BindText(State.ArtifactStoreProviderType).Placeholder("Company.Store.CustomArtifactStoreProvider"))),
                new StackPanel()
                    .Column(1)
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text("Store 运行设置").Bold().FontSize(13),
                        new Grid()
                            .Columns("120,*")
                            .Rows("Auto,Auto,Auto")
                            .Spacing(10)
                            .Children(
                                new TextBlock().Text("启用状态").Bold().CenterVertical(),
                                new ComboBox()
                                    .Column(1)
                                    .Ref(out artifactStoreEnabledComboBox)
                                    .StableItems(State.ToolBooleanLabels.ToArray())
                                    .BindSelectedIndex(State.ArtifactStoreEnabledIndex),
                                new TextBlock().Row(1).Text("跨进程同步").Bold().CenterVertical(),
                                new ComboBox()
                                    .Row(1)
                                    .Column(1)
                                    .Ref(out artifactStoreCrossProcessSyncComboBox)
                                    .StableItems(State.ToolBooleanLabels.ToArray())
                                    .BindSelectedIndex(State.ArtifactStoreCrossProcessSyncIndex),
                                new TextBlock().Row(2).Text("历史版本数").Bold().CenterVertical(),
                                new TextBox().Row(2).Column(1).BindText(State.ArtifactStoreMaxHistoryVersions).Placeholder("20"),
                                new TextBlock().Row(3).Text("优先级").Bold().CenterVertical(),
                                new TextBox().Row(3).Column(1).BindText(State.ArtifactStorePriority).Placeholder("0"))));

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
                        new Button().Content("保存工件存储包").OnClick(() => RunCollectionCommand(State.SaveArtifactStorePackage, RefreshViewControls))),
                storeHeader,
                storeEditor,
                new TextBlock().DockTop().BindText(State.ArtifactStoreResolvedPathText).FontSize(11).TextWrapping(TextWrapping.Wrap),
                new TextBlock().DockTop().BindText(State.ArtifactStorePackageStatusText).FontSize(11).TextWrapping(TextWrapping.Wrap));

        return new Border()
            .Padding(12)
            .BorderThickness(1)
            .CornerRadius(8)
            .WithTheme((theme, border) => border.Background(theme.Palette.ContainerBackground).BorderBrush(theme.Palette.ControlBorder))
            .Child(content);
    }
}
