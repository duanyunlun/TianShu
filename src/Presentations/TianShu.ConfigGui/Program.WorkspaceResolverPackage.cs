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
    private static Border BuildWorkspaceResolverPackageCollectionPanel()
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
                        new TextBlock().Text("工作空间解析包").Bold().FontSize(18),
                        new TextBlock().BindText(State.WorkspaceResolverPackageRootText).FontSize(11).TextWrapping(TextWrapping.Wrap)),
                new ComboBox()
                    .Row(1)
                    .Column(0)
                    .Ref(out workspaceResolverPackageComboBox)
                    .Height(EditControlHeight)
                    .StableItems(State.WorkspaceResolverPackageLabels.ToArray())
                    .BindSelectedIndex(State.WorkspaceResolverPackageIndex)
                    .OnSelectionChanged(_ => ApplyCollectionSelection(
                        () => State.SelectWorkspaceResolverPackageIndex(State.WorkspaceResolverPackageIndex.Value),
                        RefreshWorkspaceResolverPackageControls)),
                new StackPanel()
                    .Row(1)
                    .Column(1)
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Button().Content("新建").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.BeginNewWorkspaceResolverPackage, RefreshWorkspaceResolverPackageControls)),
                        new Button().Content("复制").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.CopySelectedWorkspaceResolverPackageToDraft, RefreshWorkspaceResolverPackageControls)),
                        new Button().Content("删除").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.DeleteSelectedWorkspaceResolverPackage, RefreshViewControls))),
                new TextBlock().Row(2).ColumnSpan(2).BindText(State.WorkspaceResolverPackageManifestPathText).FontSize(11).TextWrapping(TextWrapping.Wrap));

        var packageEditor = new Grid()
            .DockTop()
            .Columns("*,300")
            .Spacing(24)
            .Children(
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text("Resolver 包基本信息").Bold().FontSize(13),
                        new Grid()
                            .Columns("140,*")
                            .Rows("Auto,Auto,Auto")
                            .Spacing(10)
                            .Children(
                                new TextBlock().Text("包 ID").Bold().CenterVertical(),
                                new TextBox().Column(1).BindText(State.WorkspaceResolverPackageId).Placeholder("例如 builtin"),
                                new TextBlock().Row(1).Text("显示名").Bold().CenterVertical(),
                                new TextBox().Row(1).Column(1).BindText(State.WorkspaceResolverPackageDisplayName).Placeholder("显示名称"),
                                new TextBlock().Row(2).Text("优先级").Bold().CenterVertical(),
                                new TextBox().Row(2).Column(1).BindText(State.WorkspaceResolverPackagePriority).Placeholder("0"))),
                new StackPanel()
                    .Column(1)
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text("Resolver 包加载设置").Bold().FontSize(13),
                        new Grid()
                            .Columns("120,*")
                            .Rows("Auto,Auto")
                            .Spacing(10)
                            .Children(
                                new TextBlock().Text("启用状态").Bold().CenterVertical(),
                                new ComboBox()
                                    .Column(1)
                                    .Ref(out workspaceResolverPackageEnabledComboBox)
                                    .StableItems(State.ToolBooleanLabels.ToArray())
                                    .BindSelectedIndex(State.WorkspaceResolverPackageEnabledIndex),
                                new TextBlock().Row(1).Text("类型").Bold().CenterVertical(),
                                new ComboBox()
                                    .Row(1)
                                    .Column(1)
                                    .Ref(out workspaceResolverPackageTypeComboBox)
                                    .StableItems(State.WorkspaceResolverPackageTypeLabels.ToArray())
                                    .BindSelectedIndex(State.WorkspaceResolverPackageTypeIndex))));

        var resolverHeader = new Grid()
            .DockTop()
            .Columns("*,Auto")
            .Rows("Auto,Auto")
            .Spacing(8)
            .Children(
                new TextBlock().Text("Resolver 条目").Bold().FontSize(15),
                new ComboBox()
                    .Row(1)
                    .Column(0)
                    .Ref(out workspaceResolverComboBox)
                    .Height(EditControlHeight)
                    .StableItems(State.WorkspaceResolverLabels.ToArray())
                    .BindSelectedIndex(State.WorkspaceResolverIndex)
                    .OnSelectionChanged(_ => ApplyCollectionSelection(
                        () => State.SelectWorkspaceResolverIndex(State.WorkspaceResolverIndex.Value),
                        RefreshWorkspaceResolverPackageControls)),
                new StackPanel()
                    .Row(1)
                    .Column(1)
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Button().Content("新增 Resolver").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.BeginNewWorkspaceResolver, RefreshWorkspaceResolverPackageControls)),
                        new Button().Content("删除 Resolver").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.DeleteSelectedWorkspaceResolver, RefreshWorkspaceResolverPackageControls))));

        var resolverEditor = new Grid()
            .DockTop()
            .Columns("*,300")
            .Spacing(24)
            .Children(
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text("Resolver 基本信息").Bold().FontSize(13),
                        new Grid()
                            .Columns("140,*")
                            .Rows("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto")
                            .Spacing(10)
                            .Children(
                                new TextBlock().Text("Resolver ID").Bold().CenterVertical(),
                                new TextBox().Column(1).BindText(State.WorkspaceResolverId).Placeholder("例如 default"),
                                new TextBlock().Row(1).Text("显示名").Bold().CenterVertical(),
                                new TextBox().Row(1).Column(1).BindText(State.WorkspaceResolverDisplayName).Placeholder("显示名称"),
                                new TextBlock().Row(2).Text("类型").Bold().CenterVertical(),
                                new ComboBox()
                                    .Row(2)
                                    .Column(1)
                                    .Ref(out workspaceResolverTypeComboBox)
                                    .StableItems(State.WorkspaceResolverTypeLabels.ToArray())
                                    .BindSelectedIndex(State.WorkspaceResolverTypeIndex),
                                new TextBlock().Row(3).Text("根目录标记").Bold().CenterVertical(),
                                new TextBox().Row(3).Column(1).BindText(State.WorkspaceResolverRootMarkers).Placeholder(".git, .tianshu, TianShu.sln"),
                                new TextBlock().Row(4).Text("默认配置文件").Bold().CenterVertical(),
                                new TextBox().Row(4).Column(1).BindText(State.WorkspaceResolverProfile).Placeholder("default"),
                                new TextBlock().Row(5).Text("信任策略").Bold().CenterVertical(),
                                new TextBox().Row(5).Column(1).BindText(State.WorkspaceResolverTrustPolicy).Placeholder("prompt"),
                                new TextBlock().Row(6).Text("Artifact 根").Bold().CenterVertical(),
                                new TextBox().Row(6).Column(1).BindText(State.WorkspaceResolverArtifactRoot).Placeholder(".tianshu/artifacts"),
                                new TextBlock().Row(7).Text("State 根").Bold().CenterVertical(),
                                new TextBox().Row(7).Column(1).BindText(State.WorkspaceResolverStateRoot).Placeholder(".tianshu/state"),
                                new TextBlock().Row(8).Text("程序集相对路径").Bold().CenterVertical(),
                                new TextBox().Row(8).Column(1).BindText(State.WorkspaceResolverAssemblyPath).Placeholder("./resolver/Company.Workspace.dll"))),
                new StackPanel()
                    .Column(1)
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text("Resolver 推导线索").Bold().FontSize(13),
                        new Grid()
                            .Columns("120,*")
                            .Rows("Auto,Auto,Auto,Auto,Auto")
                            .Spacing(10)
                            .Children(
                                new TextBlock().Text("启用状态").Bold().CenterVertical(),
                                new ComboBox()
                                    .Column(1)
                                    .Ref(out workspaceResolverEnabledComboBox)
                                    .StableItems(State.ToolBooleanLabels.ToArray())
                                    .BindSelectedIndex(State.WorkspaceResolverEnabledIndex),
                                new TextBlock().Row(1).Text("忽略规则").Bold().CenterVertical(),
                                new TextBox().Row(1).Column(1).BindText(State.WorkspaceResolverIgnoreGlobs).Placeholder("bin/**, obj/**"),
                                new TextBlock().Row(2).Text("语言标记").Bold().CenterVertical(),
                                new TextBox().Row(2).Column(1).BindText(State.WorkspaceResolverLanguageMarkers).Placeholder("*.sln, *.csproj"),
                                new TextBlock().Row(3).Text("框架标记").Bold().CenterVertical(),
                                new TextBox().Row(3).Column(1).BindText(State.WorkspaceResolverFrameworkMarkers).Placeholder("Directory.Build.props"),
                                new TextBlock().Row(4).Text("优先级").Bold().CenterVertical(),
                                new TextBox().Row(4).Column(1).BindText(State.WorkspaceResolverPriority).Placeholder("0"))));

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
                        new Button().Content("保存工作空间解析包").OnClick(() => RunCollectionCommand(State.SaveWorkspaceResolverPackage, RefreshViewControls))),
                resolverHeader,
                resolverEditor,
                new TextBlock().DockTop().BindText(State.WorkspaceResolverResolvedPathText).FontSize(11).TextWrapping(TextWrapping.Wrap),
                new TextBlock().DockTop().BindText(State.WorkspaceResolverPackageStatusText).FontSize(11).TextWrapping(TextWrapping.Wrap));

        return new Border()
            .Padding(12)
            .BorderThickness(1)
            .CornerRadius(8)
            .WithTheme((theme, border) => border.Background(theme.Palette.ContainerBackground).BorderBrush(theme.Palette.ControlBorder))
            .Child(content);
    }
}
