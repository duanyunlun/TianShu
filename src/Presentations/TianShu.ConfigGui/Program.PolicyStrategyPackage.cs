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
    private static Border BuildPolicyStrategyPackageCollectionPanel()
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
                        new TextBlock().Text("审批策略包").Bold().FontSize(18),
                        new TextBlock().BindText(State.PolicyStrategyPackageRootText).FontSize(11).TextWrapping(TextWrapping.Wrap)),
                new ComboBox()
                    .Row(1)
                    .Column(0)
                    .Ref(out policyStrategyPackageComboBox)
                    .Height(EditControlHeight)
                    .StableItems(State.PolicyStrategyPackageLabels.ToArray())
                    .BindSelectedIndex(State.PolicyStrategyPackageIndex)
                    .OnSelectionChanged(_ => ApplyCollectionSelection(
                        () => State.SelectPolicyStrategyPackageIndex(State.PolicyStrategyPackageIndex.Value),
                        RefreshPolicyStrategyPackageControls)),
                new StackPanel()
                    .Row(1)
                    .Column(1)
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Button().Content("新建").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.BeginNewPolicyStrategyPackage, RefreshPolicyStrategyPackageControls)),
                        new Button().Content("复制").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.CopySelectedPolicyStrategyPackageToDraft, RefreshPolicyStrategyPackageControls)),
                        new Button().Content("删除").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.DeleteSelectedPolicyStrategyPackage, RefreshViewControls))),
                new TextBlock().Row(2).ColumnSpan(2).BindText(State.PolicyStrategyPackageManifestPathText).FontSize(11).TextWrapping(TextWrapping.Wrap));

        var packageEditor = new Grid()
            .DockTop()
            .Columns("*,300")
            .Spacing(24)
            .Children(
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text("策略包基本信息").Bold().FontSize(13),
                        new Grid()
                            .Columns("140,*")
                            .Rows("Auto,Auto,Auto")
                            .Spacing(10)
                            .Children(
                                new TextBlock().Text("包 ID").Bold().CenterVertical(),
                                new TextBox().Column(1).BindText(State.PolicyStrategyPackageId).Placeholder("例如 builtin"),
                                new TextBlock().Row(1).Text("显示名").Bold().CenterVertical(),
                                new TextBox().Row(1).Column(1).BindText(State.PolicyStrategyPackageDisplayName).Placeholder("显示名称"),
                                new TextBlock().Row(2).Text("优先级").Bold().CenterVertical(),
                                new TextBox().Row(2).Column(1).BindText(State.PolicyStrategyPackagePriority).Placeholder("0"))),
                new StackPanel()
                    .Column(1)
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text("策略包加载设置").Bold().FontSize(13),
                        new Grid()
                            .Columns("120,*")
                            .Rows("Auto,Auto")
                            .Spacing(10)
                            .Children(
                                new TextBlock().Text("启用状态").Bold().CenterVertical(),
                                new ComboBox()
                                    .Column(1)
                                    .Ref(out policyStrategyPackageEnabledComboBox)
                                    .StableItems(State.ToolBooleanLabels.ToArray())
                                    .BindSelectedIndex(State.PolicyStrategyPackageEnabledIndex),
                                new TextBlock().Row(1).Text("类型").Bold().CenterVertical(),
                                new ComboBox()
                                    .Row(1)
                                    .Column(1)
                                    .Ref(out policyStrategyPackageTypeComboBox)
                                    .StableItems(State.PolicyStrategyPackageTypeLabels.ToArray())
                                    .BindSelectedIndex(State.PolicyStrategyPackageTypeIndex))));

        var strategyHeader = new Grid()
            .DockTop()
            .Columns("*,Auto")
            .Rows("Auto,Auto")
            .Spacing(8)
            .Children(
                new TextBlock().Text("策略条目").Bold().FontSize(15),
                new ComboBox()
                    .Row(1)
                    .Column(0)
                    .Ref(out policyStrategyComboBox)
                    .Height(EditControlHeight)
                    .StableItems(State.PolicyStrategyLabels.ToArray())
                    .BindSelectedIndex(State.PolicyStrategyIndex)
                    .OnSelectionChanged(_ => ApplyCollectionSelection(
                        () => State.SelectPolicyStrategyIndex(State.PolicyStrategyIndex.Value),
                        RefreshPolicyStrategyPackageControls)),
                new StackPanel()
                    .Row(1)
                    .Column(1)
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Button().Content("新增策略").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.BeginNewPolicyStrategy, RefreshPolicyStrategyPackageControls)),
                        new Button().Content("删除策略").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.DeleteSelectedPolicyStrategy, RefreshPolicyStrategyPackageControls))));

        var strategyEditor = new Grid()
            .DockTop()
            .Columns("*,300")
            .Spacing(24)
            .Children(
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text("策略默认值").Bold().FontSize(13),
                        new Grid()
                            .Columns("150,*")
                            .Rows("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto")
                            .Spacing(10)
                            .Children(
                                new TextBlock().Text("策略 ID").Bold().CenterVertical(),
                                new TextBox().Column(1).BindText(State.PolicyStrategyId).Placeholder("例如 default"),
                                new TextBlock().Row(1).Text("显示名").Bold().CenterVertical(),
                                new TextBox().Row(1).Column(1).BindText(State.PolicyStrategyDisplayName).Placeholder("显示名称"),
                                new TextBlock().Row(2).Text("类型").Bold().CenterVertical(),
                                new ComboBox()
                                    .Row(2)
                                    .Column(1)
                                    .Ref(out policyStrategyTypeComboBox)
                                    .StableItems(State.PolicyStrategyTypeLabels.ToArray())
                                    .BindSelectedIndex(State.PolicyStrategyTypeIndex),
                                new TextBlock().Row(3).Text("默认审批").Bold().CenterVertical(),
                                new ComboBox()
                                    .Row(3)
                                    .Column(1)
                                    .Ref(out policyStrategyApprovalPolicyComboBox)
                                    .StableItems(State.PolicyStrategyApprovalPolicyLabels.ToArray())
                                    .BindSelectedIndex(State.PolicyStrategyApprovalPolicyIndex),
                                new TextBlock().Row(4).Text("默认沙箱").Bold().CenterVertical(),
                                new ComboBox()
                                    .Row(4)
                                    .Column(1)
                                    .Ref(out policyStrategySandboxModeComboBox)
                                    .StableItems(State.PolicyStrategySandboxModeLabels.ToArray())
                                    .BindSelectedIndex(State.PolicyStrategySandboxModeIndex),
                                new TextBlock().Row(5).Text("默认网络").Bold().CenterVertical(),
                                new ComboBox()
                                    .Row(5)
                                    .Column(1)
                                    .Ref(out policyStrategyNetworkAccessComboBox)
                                    .StableItems(State.ToolBooleanLabels.ToArray())
                                    .BindSelectedIndex(State.PolicyStrategyNetworkAccessIndex),
                                new TextBlock().Row(6).Text("登录 Shell").Bold().CenterVertical(),
                                new ComboBox()
                                    .Row(6)
                                    .Column(1)
                                    .Ref(out policyStrategyAllowLoginShellComboBox)
                                    .StableItems(State.ToolBooleanLabels.ToArray())
                                    .BindSelectedIndex(State.PolicyStrategyAllowLoginShellIndex),
                                new TextBlock().Row(7).Text("优先级").Bold().CenterVertical(),
                                new TextBox().Row(7).Column(1).BindText(State.PolicyStrategyPriority).Placeholder("0"))),
                new StackPanel()
                    .Column(1)
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text("附加规则").Bold().FontSize(13),
                        new Grid()
                            .Columns("120,*")
                            .Rows("Auto,Auto,Auto,Auto,Auto")
                            .Spacing(10)
                            .Children(
                                new TextBlock().Text("启用状态").Bold().CenterVertical(),
                                new ComboBox()
                                    .Column(1)
                                    .Ref(out policyStrategyEnabledComboBox)
                                    .StableItems(State.ToolBooleanLabels.ToArray())
                                    .BindSelectedIndex(State.PolicyStrategyEnabledIndex),
                                new TextBlock().Row(1).Text("写入审批").Bold().CenterVertical(),
                                new TextBox().Row(1).Column(1).BindText(State.PolicyStrategyWriteApprovalGlobs).Placeholder("**/*, src/**"),
                                new TextBlock().Row(2).Text("危险命令").Bold().CenterVertical(),
                                new TextBox().Row(2).Column(1).BindText(State.PolicyStrategyDangerousCommandPatterns).Placeholder("rm, git reset"),
                                new TextBlock().Row(3).Text("命令规则").Bold().CenterVertical(),
                                new TextBox().Row(3).Column(1).BindText(State.PolicyStrategyCommandRules).Placeholder("ask:git reset; deny:rm -rf"),
                                new TextBlock().Row(4).Text("网络规则").Bold().CenterVertical(),
                                new TextBox().Row(4).Column(1).BindText(State.PolicyStrategyNetworkRules).Placeholder("deny:https:example.com"))));

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
                        new Button().Content("保存审批策略包").OnClick(() => RunCollectionCommand(State.SavePolicyStrategyPackage, RefreshViewControls))),
                strategyHeader,
                strategyEditor,
                new TextBlock().DockTop().BindText(State.PolicyStrategyResolvedPathText).FontSize(11).TextWrapping(TextWrapping.Wrap),
                new TextBlock().DockTop().BindText(State.PolicyStrategyPackageStatusText).FontSize(11).TextWrapping(TextWrapping.Wrap));

        return new Border()
            .Padding(12)
            .BorderThickness(1)
            .CornerRadius(8)
            .WithTheme((theme, border) => border.Background(theme.Palette.ContainerBackground).BorderBrush(theme.Palette.ControlBorder))
            .Child(content);
    }
}
