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
    private static Border BuildToolPackageCollectionPanel()
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
                        new TextBlock().Text("工具包").Bold().FontSize(18),
                        new TextBlock().BindText(State.ToolRootText).FontSize(11).TextWrapping(TextWrapping.Wrap)),
                new ComboBox()
                    .Row(1)
                    .Column(0)
                    .Ref(out toolPackageComboBox)
                    .Height(EditControlHeight)
                    .StableItems(State.ToolPackageLabels.ToArray())
                    .BindSelectedIndex(State.ToolPackageIndex)
                    .OnSelectionChanged(_ => ApplyCollectionSelection(
                        () => State.SelectToolPackageIndex(State.ToolPackageIndex.Value),
                        RefreshToolPackageControls)),
                new StackPanel()
                    .Row(1)
                    .Column(1)
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Button().Content("新建").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.BeginNewToolPackage, RefreshToolPackageControls)),
                        new Button().Content("复制").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.CopySelectedToolPackageToDraft, RefreshToolPackageControls)),
                        new Button().Content("删除").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.DeleteSelectedToolPackage, RefreshViewControls))),
                new TextBlock().Row(2).ColumnSpan(2).BindText(State.ToolManifestPathText).FontSize(11).TextWrapping(TextWrapping.Wrap));

        var packageEditor = new Grid()
            .DockTop()
            .Columns("*,320")
            .Spacing(24)
            .Children(
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text("工具包基本信息").Bold().FontSize(13),
                        new Grid()
                            .Columns("140,*")
                            .Rows("Auto,Auto,Auto")
                            .Spacing(10)
                            .Children(
                                new TextBlock().Text("工具包 ID").Bold().CenterVertical(),
                                new TextBox().Column(1).BindText(State.ToolPackageId).Placeholder("例如 company-file-tools"),
                                new TextBlock().Row(1).Text("显示名").Bold().CenterVertical(),
                                new TextBox().Row(1).Column(1).BindText(State.ToolPackageDisplayName).Placeholder("显示名称"),
                                new TextBlock().Row(2).Text("优先级").Bold().CenterVertical(),
                                new TextBox().Row(2).Column(1).BindText(State.ToolPackagePriority).Placeholder("0"))),
                new StackPanel()
                    .Column(1)
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text("工具包加载设置").Bold().FontSize(13),
                        new Grid()
                            .Columns("120,*")
                            .Rows("Auto,Auto,Auto,Auto")
                            .Spacing(10)
                            .Children(
                                new TextBlock().Text("启用状态").Bold().CenterVertical(),
                                new ComboBox()
                                    .Column(1)
                                    .Ref(out toolPackageEnabledComboBox)
                                    .StableItems(State.ToolBooleanLabels.ToArray())
                                    .BindSelectedIndex(State.ToolPackageEnabledIndex),
                                new TextBlock().Row(1).Text("类型").Bold().CenterVertical(),
                                new ComboBox()
                                    .Row(1)
                                    .Column(1)
                                    .Ref(out toolPackageTypeComboBox)
                                    .StableItems(State.ToolPackageTypeLabels.ToArray())
                                    .BindSelectedIndex(State.ToolPackageTypeIndex),
                                new TextBlock().Row(2).Text("根提供方").Bold().CenterVertical(),
                                new TextBox().Row(2).Column(1).BindText(State.ToolPackageProviderType).Placeholder("可选"))));

        var providerHeader = new Grid()
            .DockTop()
            .Columns("*,Auto")
            .Rows("Auto,Auto")
            .Spacing(8)
            .Children(
                new TextBlock().Text("提供方条目").Bold().FontSize(15),
                new ComboBox()
                    .Row(1)
                    .Column(0)
                    .Ref(out toolProviderComboBox)
                    .Height(EditControlHeight)
                    .StableItems(State.ToolProviderLabels.ToArray())
                    .BindSelectedIndex(State.ToolProviderIndex)
                    .OnSelectionChanged(_ => ApplyCollectionSelection(
                        () => State.SelectToolProviderIndex(State.ToolProviderIndex.Value),
                        RefreshToolPackageControls)),
                new StackPanel()
                    .Row(1)
                    .Column(1)
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Button().Content("新增提供方").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.BeginNewToolProvider, RefreshToolPackageControls)),
                        new Button().Content("删除提供方").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.DeleteSelectedToolProvider, RefreshToolPackageControls))));

        var providerEditor = new Grid()
            .DockTop()
            .Columns("*,300")
            .Spacing(24)
            .Children(
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text("提供方基本信息").Bold().FontSize(13),
                        new Grid()
                            .Columns("140,*")
                            .Rows("Auto,Auto,Auto,Auto")
                            .Spacing(10)
                            .Children(
                                new TextBlock().Text("提供方 ID").Bold().CenterVertical(),
                                new TextBox().Column(1).BindText(State.ToolProviderId).Placeholder("例如 search"),
                                new TextBlock().Row(1).Text("类型").Bold().CenterVertical(),
                                new ComboBox()
                                    .Row(1)
                                    .Column(1)
                                    .Ref(out toolProviderTypeComboBox)
                                    .StableItems(State.ToolProviderTypeLabels.ToArray())
                                    .BindSelectedIndex(State.ToolProviderTypeIndex),
                                new TextBlock().Row(2).Text("程序集相对路径").Bold().CenterVertical(),
                                new TextBox().Row(2).Column(1).BindText(State.ToolProviderAssemblyPath).Placeholder("./search/TianShu.Tools.Search.dll"),
                                new TextBlock().Row(3).Text("提供方类型名").Bold().CenterVertical(),
                                new TextBox().Row(3).Column(1).BindText(State.ToolProviderTypeName).Placeholder("Namespace.TypeName"))),
                new StackPanel()
                    .Column(1)
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text("提供方调用设置").Bold().FontSize(13),
                        new Grid()
                            .Columns("120,*")
                            .Rows("Auto,Auto,Auto")
                            .Spacing(10)
                            .Children(
                                new TextBlock().Text("启用状态").Bold().CenterVertical(),
                                new ComboBox()
                                    .Column(1)
                                    .Ref(out toolProviderEnabledComboBox)
                                    .StableItems(State.ToolBooleanLabels.ToArray())
                                    .BindSelectedIndex(State.ToolProviderEnabledIndex),
                                new TextBlock().Row(1).Text("替换现有实现").Bold().CenterVertical(),
                                new ComboBox()
                                    .Row(1)
                                    .Column(1)
                                    .Ref(out toolProviderReplaceExistingComboBox)
                                    .StableItems(State.ToolReplaceExistingLabels.ToArray())
                                    .BindSelectedIndex(State.ToolProviderReplaceExistingIndex),
                                new TextBlock().Row(2).Text("优先级").Bold().CenterVertical(),
                                new TextBox().Row(2).Column(1).BindText(State.ToolProviderPriority).Placeholder("10"))));

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
                        new Button().Content("保存工具包").OnClick(() => RunCollectionCommand(State.SaveToolPackage, RefreshViewControls))),
                providerHeader,
                providerEditor,
                new TextBlock().DockTop().BindText(State.ToolProviderResolvedPathText).FontSize(11).TextWrapping(TextWrapping.Wrap),
                new TextBlock().DockTop().BindText(State.ToolStatusText).FontSize(11).TextWrapping(TextWrapping.Wrap));

        return new Border()
            .Padding(12)
            .BorderThickness(1)
            .CornerRadius(8)
            .WithTheme((theme, border) => border.Background(theme.Palette.ContainerBackground).BorderBrush(theme.Palette.ControlBorder))
            .Child(content);
    }
}
