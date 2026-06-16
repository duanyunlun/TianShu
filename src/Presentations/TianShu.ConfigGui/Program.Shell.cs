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
    private static Element BuildRoot()
        => new DockPanel()
            .LastChildFill()
            .Children(
                BuildHeader().DockTop(),
                BuildFooter().DockBottom(),
                new DockPanel()
                    .Padding(18)
                    .Spacing(16)
                    .Children(
                        BuildCategoryPanel().DockLeft(),
                        BuildDetailPanel().DockRight(),
                        BuildContentPanel()));

    private static Element BuildContentPanel()
        => new ScrollViewer()
            .Ref(out contentScrollViewer)
            .VerticalScroll(ScrollMode.Auto)
            .OnMouseWheel(args => HandleAutoHidingVerticalScroll(
                args,
                contentScrollViewer,
                contentScrollState))
            .OnMouseLeave(() => RestoreAutoHidingScrollOffset(contentScrollViewer, contentScrollState))
            .Content(new StackPanel()
                .Vertical()
                .Children(
                    BuildFieldPanel().BindIsVisible(State.IsFieldView),
                    BuildWorkspaceCollectionPanel().BindIsVisible(State.IsWorkspaceView),
                    BuildProviderCollectionPanel().BindIsVisible(State.IsProviderView),
                    BuildModelProtocolMappingPanel().BindIsVisible(State.IsModelProtocolMappingView),
                    BuildDefaultModelProtocolRulesPanel().BindIsVisible(State.IsDefaultModelProtocolRulesView),
                    BuildModelRouteSetCollectionPanel().BindIsVisible(State.IsModelRouteSetView),
                    BuildPromptCollectionPanel().BindIsVisible(State.IsPromptView),
                    BuildSkillPackageCollectionPanel().BindIsVisible(State.IsSkillPackageView),
                    BuildToolPackageCollectionPanel().BindIsVisible(State.IsToolPackageView),
                    BuildProviderPackageCollectionPanel().BindIsVisible(State.IsProviderPackageView),
                    BuildMcpServerPackageCollectionPanel().BindIsVisible(State.IsMcpServerPackageView),
                    BuildArtifactStorePackageCollectionPanel().BindIsVisible(State.IsArtifactStorePackageView),
                    BuildDiagnosticSinkPackageCollectionPanel().BindIsVisible(State.IsDiagnosticSinkPackageView),
                    BuildWorkspaceResolverPackageCollectionPanel().BindIsVisible(State.IsWorkspaceResolverPackageView),
                    BuildPolicyStrategyPackageCollectionPanel().BindIsVisible(State.IsPolicyStrategyPackageView)));

    private static Element BuildHeader()
        => new Border()
            .Padding(18, 16)
            .Apply(border => border.NonUniformBorderThickness = new Thickness(0, 0, 0, 1))
            .WithTheme((theme, border) => border.Background(theme.Palette.ContainerBackground).BorderBrush(theme.Palette.ControlBorder))
            .Child(new DockPanel()
                .Children(
                    new Button()
                        .DockRight()
                        .Content("刷新")
                        .OnClick(() =>
                        {
                            State.Refresh();
                            RefreshViewControls();
                        }),
                    new StackPanel()
                        .Vertical()
                        .CenterVertical()
                        .Children(
                            new TextBlock()
                                .Text("天枢 TianShu 配置中心")
                                .FontSize(24)
                                .Bold())));

    private static Element BuildFooter()
        => new Border()
            .Padding(14, 10)
            .Apply(border => border.NonUniformBorderThickness = new Thickness(1, 0, 0, 0))
            .WithTheme((theme, border) => border.Background(theme.Palette.ContainerBackground).BorderBrush(theme.Palette.ControlBorder))
            .Child(new StackPanel()
                .Vertical()
                .Spacing(3)
                .Children(
                    new TextBlock().BindText(State.ConfigPathText).FontSize(11),
                    new TextBlock().BindText(State.StatusText).FontSize(11)));

    private static Element BuildCategoryPanel()
        => new Border()
            .Width(230)
            .Padding(12)
            .BorderThickness(1)
            .CornerRadius(8)
            .WithTheme((theme, border) => border.Background(theme.Palette.ContainerBackground).BorderBrush(theme.Palette.ControlBorder))
            .Child(new DockPanel()
                .LastChildFill()
                .Spacing(6)
                .Children(
                    new TextBlock().Text("配置模块").Bold().FontSize(15).DockTop(),
                    new ScrollViewer()
                        .Ref(out categoryScrollViewer)
                        .VerticalScroll(ScrollMode.Auto)
                        .OnMouseWheel(args => HandleAutoHidingVerticalScroll(
                            args,
                            categoryScrollViewer,
                            categoryScrollState))
                        .OnMouseLeave(() => RestoreAutoHidingScrollOffset(categoryScrollViewer, categoryScrollState))
                        .Content(new StackPanel()
                            .Vertical()
                            .Spacing(6)
                            .Children(State.NavigationModules.Select(BuildNavigationModule).ToArray()))));

    private static void HandleAutoHidingVerticalScroll(
        MouseWheelEventArgs args,
        ScrollViewer scrollViewer,
        AutoHidingScrollState state,
        Action? beforeScroll = null)
    {
        if (args.IsHorizontal)
        {
            return;
        }

        beforeScroll?.Invoke();
        ShowAutoHidingScrollBar(scrollViewer, state);
        var notches = args.Delta / 120.0;
        var nextOffset = state.VerticalOffset - notches * 48.0;
        scrollViewer.SetScrollOffsets(state.HorizontalOffset, nextOffset);
        RememberAutoHidingScrollOffset(scrollViewer, state);
        args.Handled = true;
        ScheduleAutoHidingScrollBarHide(scrollViewer, state);
    }

    private static void ShowAutoHidingScrollBar(ScrollViewer scrollViewer, AutoHidingScrollState state)
    {
        scrollViewer.VerticalScroll = ScrollMode.Auto;
        scrollViewer.SetScrollOffsets(state.HorizontalOffset, state.VerticalOffset);
        scrollViewer.InvalidateMeasure();
        scrollViewer.InvalidateVisual();
    }

    private static void RememberAutoHidingScrollOffset(
        ScrollViewer scrollViewer,
        AutoHidingScrollState state)
    {
        state.HorizontalOffset = scrollViewer.HorizontalOffset;
        state.VerticalOffset = scrollViewer.VerticalOffset;
    }

    private static void ScheduleAutoHidingScrollBarHide(ScrollViewer scrollViewer, AutoHidingScrollState state)
    {
        state.HideTimer?.Stop();
        state.HideTimer?.Dispose();
        state.HideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(900),
        };
        state.HideTimer.Tick += () =>
        {
            state.HideTimer?.Stop();
            HideAutoHidingScrollBar(scrollViewer, state);
            ScheduleAutoHidingScrollOffsetRestore(scrollViewer, state);
        };
        state.HideTimer.Start();
    }

    private static void HideAutoHidingScrollBar(ScrollViewer scrollViewer, AutoHidingScrollState state)
    {
        scrollViewer.SetScrollOffsets(state.HorizontalOffset, state.VerticalOffset);
        scrollViewer.InvalidateMeasure();
        scrollViewer.InvalidateVisual();
    }

    private static void RestoreAutoHidingScrollOffset(ScrollViewer scrollViewer, AutoHidingScrollState state)
        => ScheduleAutoHidingScrollOffsetRestore(scrollViewer, state);

    private static void ScheduleAutoHidingScrollOffsetRestore(
        ScrollViewer scrollViewer,
        AutoHidingScrollState state)
    {
        state.RestoreTimer?.Stop();
        state.RestoreTimer?.Dispose();
        state.RestoreTicksRemaining = 3;

        state.RestoreTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        state.RestoreTimer.Tick += () =>
        {
            scrollViewer.SetScrollOffsets(state.HorizontalOffset, state.VerticalOffset);
            scrollViewer.InvalidateArrange();
            scrollViewer.InvalidateVisual();
            state.RestoreTicksRemaining--;
            if (state.RestoreTicksRemaining <= 0)
            {
                state.RestoreTimer?.Stop();
            }
        };
        state.RestoreTimer.Start();
    }

    private sealed class AutoHidingScrollState
    {
        public DispatcherTimer? HideTimer { get; set; }

        public DispatcherTimer? RestoreTimer { get; set; }

        public double HorizontalOffset { get; set; }

        public double VerticalOffset { get; set; }

        public int RestoreTicksRemaining { get; set; }
    }

    private static Element BuildNavigationModule(ConfigNavigationModuleRow module)
        => new StackPanel()
            .Vertical()
            .Spacing(3)
            .Children(
                BuildNavigationModuleButton(module),
                new Border()
                    .BindIsVisible(module.IsExpanded)
                    .Child(new StackPanel()
                        .Vertical()
                        .Spacing(3)
                        .Children(module.Pages.Select(BuildCategoryButton).ToArray())));

    private static Button BuildNavigationModuleButton(ConfigNavigationModuleRow module)
        => new Button()
            .Content(module.DisplayName)
            .Height(26)
            .FontSize(12)
            .Padding(8, 2)
            .Background(Color.FromRgb(38, 52, 71))
            .Foreground(Color.FromRgb(244, 248, 255))
            .BorderBrush(Color.FromRgb(79, 131, 199))
            .OnClick(() => module.IsExpanded.Value = !module.IsExpanded.Value);

    private static Element BuildCategoryButton(ConfigCategoryRow category)
    {
        var item = new Border()
            .Height(26)
            .Padding(8, 2)
            .BorderThickness(1)
            .CornerRadius(4)
            .Background(SecondaryNavigationBackground)
            .Bind(Control.BackgroundProperty,
                category.IsNavigationHighlighted,
                highlighted => highlighted ? NavigationHighlightBackground : SecondaryNavigationBackground,
                _ => false,
                BindingMode.OneWay)
            .Bind(Control.BorderBrushProperty,
                category.IsNavigationHighlighted,
                highlighted => highlighted ? NavigationHighlightBorder : SecondaryNavigationBorder,
                _ => false,
                BindingMode.OneWay)
            .Cursor(CursorType.Hand)
            .Child(new TextBlock()
                .BindText(category.CaptionText)
                .FontSize(12)
                .Bold()
                .TextAlignment(TextAlignment.Center)
                .CenterVertical()
                .StretchHorizontal());

        return new Border()
            .Padding(16, 0)
            .Child(item
                .OnMouseEnter(() =>
                {
                    State.HoverCategory(category.Id);
                })
                .OnMouseLeave(() =>
                {
                    State.ClearHoveredCategory(category.Id);
                })
                .OnMouseDown(_ =>
                {
                    CloseDetailEnumEditorDropDown();
                    State.SelectCategory(category.Id);
                    RefreshViewControls();
                }));
    }
}
