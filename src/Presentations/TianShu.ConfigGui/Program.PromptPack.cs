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
    private static Border BuildPromptCollectionPanel()
    {
        var header = new StackPanel()
            .DockTop()
            .Vertical()
            .Spacing(8)
            .Children(
                new TextBlock().Text("提示词").Bold().FontSize(18),
                new TextBlock().BindText(State.PromptRootText).FontSize(11).TextWrapping(TextWrapping.Wrap));

        var fileSelector = new Grid()
            .DockTop()
            .Columns("180,*,Auto")
            .Rows("Auto,Auto,Auto")
            .Spacing(10)
            .Children(
                new TextBlock().Text("Prompt Pack").Bold().CenterVertical(),
                new ComboBox()
                    .Column(1)
                    .Ref(out promptFileComboBox)
                    .Height(EditControlHeight)
                    .StableItems(State.PromptFileLabels.ToArray())
                    .BindSelectedIndex(State.PromptFileIndex)
                    .OnSelectionChanged(_ => ApplyCollectionSelection(
                        () => State.SelectPromptFileIndex(State.PromptFileIndex.Value),
                        RefreshPromptControls)),
                new StackPanel()
                    .Column(2)
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Button().Content("新建").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.CreatePromptFile, RefreshViewControls)),
                        new Button().Content("复制").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.CopyPromptFile, RefreshViewControls)),
                        new Button().Content("删除").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.DeletePromptFile, RefreshViewControls))),
                new TextBlock().Row(1).Text("保存目标").Bold().CenterVertical(),
                new TextBlock().Row(1).Column(1).BindText(State.PromptSaveTargetText).FontSize(11).TextWrapping(TextWrapping.Wrap).CenterVertical(),
                new TextBlock().Row(2).Text("新建/复制包 ID").Bold().CenterVertical(),
                new TextBox().Row(2).Column(1).BindText(State.PromptFileName).Placeholder("例如 default 或 team-prompts"));

        var options = new Grid()
            .DockTop()
            .Columns("180,*")
            .Rows("Auto,Auto")
            .Spacing(10)
            .Children(
                new TextBlock().Text("启用状态").Bold().CenterVertical().BindIsVisible(State.PromptSupportsEnabled),
                new ComboBox()
                    .Column(1)
                    .Ref(out promptEnabledComboBox)
                    .StableItems(State.PromptEnabledLabels.ToArray())
                    .BindSelectedIndex(State.PromptEnabledIndex)
                    .BindIsVisible(State.PromptSupportsEnabled),
                new TextBlock().Row(1).Text("合并模式").Bold().CenterVertical().BindIsVisible(State.PromptSupportsMode),
                new ComboBox()
                    .Row(1)
                    .Column(1)
                    .Ref(out promptModeComboBox)
                    .StableItems(State.PromptModeLabels.ToArray())
                    .BindSelectedIndex(State.PromptModeIndex)
                    .BindIsVisible(State.PromptSupportsMode));

        var content = new DockPanel()
            .Spacing(10)
            .Children(
                header,
                fileSelector,
                new TextBlock().Text("Prompt 段").Bold().FontSize(11).DockTop(),
                new ComboBox()
                    .DockTop()
                    .Ref(out promptSectionComboBox)
                    .StableItems(State.PromptSectionLabels.ToArray())
                    .BindSelectedIndex(State.PromptSectionIndex)
                    .OnSelectionChanged(_ => ApplyCollectionSelection(() => State.SelectPromptSectionIndex(State.PromptSectionIndex.Value))),
                new TextBlock().BindText(State.PromptSectionDescription).FontSize(11).TextWrapping(TextWrapping.Wrap).DockTop(),
                options,
                new TextBlock().Text("Prompt 内容").Bold().FontSize(11).DockTop(),
                new MultiLineTextBox()
                    .DockTop()
                    .Height(220)
                    .BindText(State.PromptText),
                new StackPanel()
                    .DockTop()
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Button()
                            .Content("保存 Prompt 段")
                            .OnClick(() => RunCollectionCommand(State.SavePromptSection, RefreshPromptControls))),
                new TextBlock().BindText(State.PromptStatusText).FontSize(11).TextWrapping(TextWrapping.Wrap));

        return new Border()
            .Padding(12)
            .BorderThickness(1)
            .CornerRadius(8)
            .WithTheme((theme, border) => border.Background(theme.Palette.ContainerBackground).BorderBrush(theme.Palette.ControlBorder))
            .Child(content);
    }
}
