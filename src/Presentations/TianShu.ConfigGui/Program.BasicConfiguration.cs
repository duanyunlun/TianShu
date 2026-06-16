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
    private static Border BuildFieldPanel()
        => new Border()
            .Padding(12)
            .BorderThickness(1)
            .CornerRadius(8)
            .WithTheme((theme, border) => border.Background(theme.Palette.ContainerBackground).BorderBrush(theme.Palette.ControlBorder))
            .Child(new DockPanel()
                .Spacing(10)
                .Children(
                    new TextBlock()
                        .DockTop()
                        .BindText(State.SelectedPageTitle)
                        .Bold()
                        .FontSize(ContentTitleFontSize),
                    new StackPanel()
                        .DockTop()
                        .Horizontal()
                        .Spacing(8)
                        .BindIsVisible(State.IsProfileCompositionCurrentPageView)
                        .Children(
                            new Button()
                                .Content("保存当前选择")
                                .Height(EditControlHeight)
                                .OnClick(() =>
                                {
                                    State.SaveCurrentProfileSelection();
                                    RefreshViewControls();
                                }),
                            new Button()
                                .Content("新增配置方案")
                                .Height(EditControlHeight)
                                .OnClick(() =>
                                {
                                    State.CreateProfileCompositionProfile();
                                    RefreshViewControls();
                                }),
                            new Button()
                                .Content("删除当前方案")
                                .Height(EditControlHeight)
                                .OnClick(() =>
                                {
                                    State.DeleteCurrentProfileCompositionProfile();
                                    RefreshViewControls();
                                })),
                    new StackPanel()
                        .DockTop()
                        .Horizontal()
                        .Spacing(8)
                        .BindIsVisible(State.IsAgentFieldPageView)
                        .Children(
                            new Button()
                                .Content("新增当前类型")
                                .Height(EditControlHeight)
                                .OnClick(() =>
                                {
                                    State.CreateAgentConfigurationForCurrentSelection();
                                    RefreshViewControls();
                                }),
                            new Button()
                                .Content("复制当前类型")
                                .Height(EditControlHeight)
                                .OnClick(() =>
                                {
                                    State.CopyAgentConfigurationForCurrentSelection();
                                    RefreshViewControls();
                                }),
                            new Button()
                                .Content("删除当前类型")
                                .Height(EditControlHeight)
                                .OnClick(() =>
                                {
                                    State.DeleteAgentConfigurationForCurrentSelection();
                                    RefreshViewControls();
                                })),
                    new StackPanel()
                        .DockTop()
                        .Horizontal()
                        .Spacing(8)
                        .BindIsVisible(State.IsMemoryFieldPageView)
                        .Children(
                            new Button()
                                .Content("补齐默认记忆配置")
                                .Height(EditControlHeight)
                                .OnClick(() =>
                                {
                                    State.EnsureDefaultMemoryConfiguration();
                                    RefreshViewControls();
                                }),
                            new Button()
                                .Content("新增当前类型")
                                .Height(EditControlHeight)
                                .BindIsVisible(State.IsMemoryInstanceCreationVisible)
                                .OnClick(() =>
                                {
                                    State.CreateMemoryConfigurationForCurrentPage();
                                    RefreshViewControls();
                                }),
                            new Button()
                                .Content("复制当前类型")
                                .Height(EditControlHeight)
                                .BindIsVisible(State.IsMemoryInstanceCreationVisible)
                                .OnClick(() =>
                                {
                                    State.CopyMemoryConfigurationForCurrentPage();
                                    RefreshViewControls();
                                }),
                            new Button()
                                .Content("重命名当前类型")
                                .Height(EditControlHeight)
                                .BindIsVisible(State.IsMemoryInstanceCreationVisible)
                                .OnClick(() =>
                                {
                                    State.RenameMemoryConfigurationForCurrentPage();
                                    RefreshViewControls();
                                }),
                            new Button()
                                .Content("删除当前类型")
                                .Height(EditControlHeight)
                                .BindIsVisible(State.IsMemoryInstanceCreationVisible)
                                .OnClick(() =>
                                {
                                    State.DeleteMemoryConfigurationForCurrentPage();
                                    RefreshViewControls();
                                })),
                    new GridView()
                        .Ref(out fieldGrid)
                        .ItemsSource(State.FilteredFields)
                        .Apply(grid => grid.SelectionChanged += selected =>
                        {
                            if (selected is ConfigFieldRow row)
                            {
                                State.SelectField(row);
                                RefreshDetailControls();
                            }
                        })
                        .OnMouseDoubleClick(BeginGridCellEdit)
                        .Columns(
                            new GridViewColumn<ConfigFieldRow>().Header("配置项").Width(MainTableNameColumnWidth).Resizable(false).Text(row => row.DisplayName),
                            new GridViewColumn<ConfigFieldRow>().Header("当前值").Width(MainTableValueColumnWidth).Resizable(false).Text(row => row.CurrentValueSummary))));

    private static void BeginGridCellEdit(MouseEventArgs args)
        => BeginGridCellEdit(args, fieldGrid, State.FilteredFields);

    private static void BeginWorkspaceGridCellEdit(MouseEventArgs args)
        => BeginGridCellEdit(args, workspaceFieldGrid, State.WorkspaceFields);

    private static void BeginGridCellEdit(MouseEventArgs args, GridView grid, IReadOnlyList<ConfigFieldRow> rows)
    {
        if (!grid.TryGetCellIndexAt(args, out var rowIndex, out _, out var isHeader)
            || isHeader
            || rowIndex < 0
            || rowIndex >= rows.Count)
        {
            return;
        }

        var row = rows[rowIndex];
        if (!State.BeginEdit(row))
        {
            return;
        }

        RefreshDetailControls();
        if (State.IsEnumEditorVisible.Value && detailEnumEditor.FindVisualRoot() is Window enumWindow)
        {
            enumWindow.FocusManager.SetFocus(detailEnumEditor);
        }
        else if (detailTextEditor.FindVisualRoot() is Window window)
        {
            window.FocusManager.SetFocus(detailTextEditor);
        }

        args.Handled = true;
    }

    private static Element BuildDetailPanel()
        => new Border()
            .Width(390)
            .BindIsVisible(State.IsDetailViewVisible)
            .Padding(14)
            .BorderThickness(1)
            .CornerRadius(8)
            .WithTheme((theme, border) => border.Background(theme.Palette.ContainerBackground).BorderBrush(theme.Palette.ControlBorder))
            .Child(new ScrollViewer()
                .Ref(out detailScrollViewer)
                .VerticalScroll(ScrollMode.Auto)
                .OnMouseWheel(args => HandleAutoHidingVerticalScroll(
                    args,
                    detailScrollViewer,
                    detailScrollState,
                    CloseDetailEnumEditorDropDown))
                .OnMouseLeave(() => RestoreAutoHidingScrollOffset(detailScrollViewer, detailScrollState))
                .Content(new StackPanel()
                    .Vertical()
                    .Spacing(10)
                    .Children(
                        new TextBlock().BindText(State.ContextTitle).FontSize(18).Bold().TextWrapping(TextWrapping.Wrap),
                        new TextBlock().BindText(State.ContextSummary).FontSize(12).TextWrapping(TextWrapping.Wrap),
                        new TextBlock().Text("生效说明").Bold(),
                        BuildContextBlock(State.ContextDescription),
                        new TextBlock().Text("保存与来源").Bold(),
                        new Grid()
                            .Columns("*,*")
                            .Rows("Auto,Auto,Auto")
                            .Spacing(8)
                            .Children(
                                FieldMeta("保存目标", State.ContextSaveTarget),
                                FieldMeta("来源层", State.ContextSourceName).Column(1),
                                FieldMeta("覆盖关系", State.ContextOverrideBoundary).Row(1),
                                FieldMeta("写回边界", State.ContextWriteBoundary).Row(1).Column(1),
                                FieldMeta("配置 Key", State.SelectedKey).Row(2).BindIsVisible(State.IsFieldDetailEditorVisible),
                                FieldMeta("类型", State.SelectedValueKind).Row(2).Column(1).BindIsVisible(State.IsFieldDetailEditorVisible)),
                        new TextBlock().Text("诊断与结果").Bold(),
                        BuildContextBlock(State.ContextDiagnostics),
                        new TextBlock().Text("字段编辑").Bold().BindIsVisible(State.IsFieldDetailEditorVisible),
                        new MultiLineTextBox()
                            .Ref(out detailTextEditor)
                            .Height(90)
                            .BindText(State.SelectedEditValue)
                            .BindIsVisible(State.IsTextEditorVisible),
                        new Border()
                            .Ref(out detailEnumEditorHost)
                            .Height(EditControlHeight)
                            .OnMouseWheel(CloseDetailEnumEditorDropDownOnWheel)
                            .Child(CreateDetailEnumEditor().Ref(out detailEnumEditor))
                            .BindIsVisible(State.IsEnumEditorVisible),
                        new StackPanel()
                            .Horizontal()
                            .CenterVertical()
                            .Spacing(8)
                            .BindIsVisible(State.IsFieldDetailEditorVisible)
                            .Children(
                                new Button().Content("保存配置项").Height(EditControlHeight).OnClick(() => SaveSelected()),
                                new Button().Content("撤销修改").Height(EditControlHeight).OnClick(() =>
                                {
                                    State.RevertSelected();
                                    RefreshFieldGrid();
                                    RefreshDetailControls();
                                }),
                                new Button().Content("重置默认").Height(EditControlHeight).OnClick(() =>
                                {
                                    State.ResetSelected();
                                    RefreshFieldGrid();
                                    RefreshDetailControls();
                                })),
                        new TextBlock()
                            .BindText(State.SelectedDefaultValue, value => $"默认值：{value}")
                            .FontSize(11)
                            .TextWrapping(TextWrapping.Wrap)
                            .BindIsVisible(State.IsFieldDetailEditorVisible),
                        new TextBlock()
                            .BindText(State.ContextWriteBackResult)
                            .FontSize(11)
                            .TextWrapping(TextWrapping.Wrap))));

    private static Element BuildDescriptionBlock()
        => BuildContextBlock(State.SelectedHelpText);

    private static Element BuildContextBlock(ObservableValue<string> text)
        => new Border()
            .Padding(10)
            .BorderThickness(1)
            .CornerRadius(6)
            .WithTheme((theme, border) => border.Background(theme.Palette.ControlBackground).BorderBrush(theme.Palette.ControlBorder))
            .Child(new TextBlock()
                .BindText(text)
                .TextWrapping(TextWrapping.Wrap)
                .FontSize(12));

    private static StackPanel FieldMeta(string label, ObservableValue<string> value)
        => new StackPanel()
            .Vertical()
            .Spacing(2)
            .Children(
                new TextBlock().Text(label).Bold().FontSize(11),
                new TextBlock().BindText(value).FontSize(11).TextWrapping(TextWrapping.Wrap));

    private static Border BuildWorkspaceCollectionPanel()
    {
        var content = new DockPanel()
            .Spacing(10)
            .Children(
                new StackPanel()
                    .DockTop()
                    .Vertical()
                    .Spacing(4)
                    .Children(
                        new TextBlock().Text("工作空间").Bold().FontSize(18),
                        new TextBlock()
                            .Text("管理默认工作空间配置文件。项目路径级覆盖后续会在这里继续扩展，不再混在普通配置主表里。")
                            .FontSize(11)
                            .TextWrapping(TextWrapping.Wrap)),
                new StackPanel()
                    .DockTop()
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Button()
                            .Content("新增当前类型")
                            .Height(EditControlHeight)
                            .OnClick(() =>
                            {
                                State.CreateWorkspaceConfigurationForCurrentSelection();
                                RefreshViewControls();
                            }),
                        new Button()
                            .Content("复制当前类型")
                            .Height(EditControlHeight)
                            .OnClick(() =>
                            {
                                State.CopyWorkspaceConfigurationForCurrentSelection();
                                RefreshViewControls();
                            }),
                        new Button()
                            .Content("删除当前类型")
                            .Height(EditControlHeight)
                            .OnClick(() =>
                            {
                                State.DeleteWorkspaceConfigurationForCurrentSelection();
                                RefreshViewControls();
                            })),
                new GridView()
                    .Ref(out workspaceFieldGrid)
                    .ItemsSource(State.WorkspaceFields)
                    .Apply(grid => grid.SelectionChanged += selected =>
                    {
                        if (selected is ConfigFieldRow row)
                        {
                            State.SelectField(row);
                            RefreshDetailControls();
                        }
                    })
                    .OnMouseDoubleClick(BeginWorkspaceGridCellEdit)
                    .Columns(
                        new GridViewColumn<ConfigFieldRow>().Header("配置项").Width(MainTableNameColumnWidth).Resizable(false).Text(row => row.DisplayName),
                        new GridViewColumn<ConfigFieldRow>().Header("当前值").Width(MainTableValueColumnWidth).Resizable(false).Text(row => row.CurrentValueSummary)));

        return new Border()
            .Padding(12)
            .BorderThickness(1)
            .CornerRadius(8)
            .WithTheme((theme, border) => border.Background(theme.Palette.ContainerBackground).BorderBrush(theme.Palette.ControlBorder))
            .Child(content);
    }
}
