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
    private static Border BuildSkillPackageCollectionPanel()
    {
        var header = new Grid()
            .DockTop()
            .Columns("*,360")
            .Rows("Auto,Auto,Auto,Auto")
            .Spacing(8)
            .Children(
                new StackPanel()
                    .Vertical()
                    .Spacing(4)
                    .Children(
                        new TextBlock().Text("技能包").Bold().FontSize(18),
                        new TextBlock().BindText(State.SkillPackageRootText).FontSize(11).TextWrapping(TextWrapping.Wrap)),
                new ComboBox()
                    .Row(1)
                    .Column(0)
                    .Ref(out skillPackageComboBox)
                    .Height(EditControlHeight)
                    .StableItems(State.SkillPackageLabels.ToArray())
                    .BindSelectedIndex(State.SkillPackageIndex)
                    .OnSelectionChanged(_ => ApplyCollectionSelection(
                        () => State.SelectSkillPackageIndex(State.SkillPackageIndex.Value),
                        RefreshSkillPackageControls)),
                new StackPanel()
                    .Row(1)
                    .Column(1)
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new ComboBox()
                            .Ref(out skillPackageEnabledComboBox)
                            .Width(110)
                            .Height(EditControlHeight)
                            .StableItems(State.ToolBooleanLabels.ToArray())
                            .BindSelectedIndex(State.SkillPackageEnabledIndex),
                        new Button()
                            .Content("保存状态")
                            .Height(EditControlHeight)
                            .OnClick(() => RunCollectionCommand(State.SaveSkillPackageEnabled, RefreshViewControls))),
                new TextBox()
                    .Row(2)
                    .Column(0)
                    .Height(EditControlHeight)
                    .BindText(State.SkillPackageNewId)
                    .Placeholder("新技能包 ID，例如 team-skill"),
                new StackPanel()
                    .Row(2)
                    .Column(1)
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Button()
                            .Content("新建")
                            .Height(EditControlHeight)
                            .OnClick(() => RunCollectionCommand(State.CreateSkillPackage, RefreshSkillPackageControls)),
                        new Button()
                            .Content("删除")
                            .Height(EditControlHeight)
                            .OnClick(() => RunCollectionCommand(State.DeleteSelectedSkillPackage, RefreshSkillPackageControls))),
                new TextBlock().Row(3).ColumnSpan(2).BindText(State.SkillPackagePathText).FontSize(11).TextWrapping(TextWrapping.Wrap));

        var details = new Grid()
            .DockTop()
            .Columns("*,*")
            .Spacing(18)
            .Children(
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().BindText(State.SkillPackageTitleText).Bold().FontSize(14),
                        new TextBlock().BindText(State.SkillPackageDescriptionText).FontSize(12).TextWrapping(TextWrapping.Wrap),
                        new TextBlock().Text("接口信息").Bold().FontSize(13),
                        new TextBlock().BindText(State.SkillPackageInterfaceText).FontSize(11).TextWrapping(TextWrapping.Wrap),
                        new TextBlock().Text("依赖").Bold().FontSize(13),
                        new TextBlock().BindText(State.SkillPackageDependencyText).FontSize(11).TextWrapping(TextWrapping.Wrap)),
                new StackPanel()
                    .Column(1)
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text("权限").Bold().FontSize(13),
                        new TextBlock().BindText(State.SkillPackagePermissionText).FontSize(11).TextWrapping(TextWrapping.Wrap),
                        new TextBlock().Text("资源目录").Bold().FontSize(13),
                        new TextBlock().BindText(State.SkillPackageResourceText).FontSize(11).TextWrapping(TextWrapping.Wrap),
                        new TextBlock().Text("说明").Bold().FontSize(13),
                        new TextBlock()
                            .Text("此页只管理技能包启用状态和可治理元数据；SKILL.md 正文、脚本和资源文件需要在对应目录中维护。")
                            .FontSize(11)
                            .TextWrapping(TextWrapping.Wrap)));

        var content = new DockPanel()
            .Spacing(10)
            .Children(
                header,
                details,
                new TextBlock().DockTop().BindText(State.SkillPackageStatusText).FontSize(11).TextWrapping(TextWrapping.Wrap));

        return new Border()
            .Padding(12)
            .BorderThickness(1)
            .CornerRadius(8)
            .WithTheme((theme, border) => border.Background(theme.Palette.ContainerBackground).BorderBrush(theme.Palette.ControlBorder))
            .Child(content);
    }
}
