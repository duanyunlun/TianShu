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
    private static Border BuildProviderCollectionPanel()
    {
        var header = new Grid()
            .DockTop()
            .Columns("*,Auto")
            .Rows("Auto,Auto")
            .Spacing(8)
            .Children(
                new TextBlock().Text("提供方").Bold().FontSize(18),
                new ComboBox()
                    .Row(1)
                    .Column(0)
                    .Ref(out providerComboBox)
                    .Height(EditControlHeight)
                    .StableItems(State.ProviderItemLabels.ToArray())
                    .BindSelectedIndex(State.ProviderItemIndex)
                    .OnSelectionChanged(_ => ApplyCollectionSelection(() => State.SelectProviderIndex(State.ProviderItemIndex.Value))),
                new StackPanel()
                    .Row(1)
                    .Column(1)
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Button().Content("新建").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.BeginNewProvider, RefreshProviderControls)),
                        new Button().Content("复制").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.CopySelectedProviderToDraft, RefreshProviderControls)),
                        new Button().Content("删除").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.DeleteSelectedProvider, RefreshViewControls))));

        var editor = new Grid()
            .DockTop()
            .Columns("180,*")
            .Rows("Auto,Auto,Auto,Auto")
            .Spacing(10)
            .Children(
                new TextBlock().Text("提供方 ID").Bold().CenterVertical(),
                new TextBox().Column(1).BindText(State.ProviderId).Placeholder("例如 openai"),
                new TextBlock().Row(1).Text("Base URL").Bold().CenterVertical(),
                new TextBox().Row(1).Column(1).BindText(State.ProviderBaseUrl).Placeholder("https://api.openai.com"),
                new TextBlock().Row(2).Text("API Key 环境变量名").Bold().CenterVertical(),
                new TextBox().Row(2).Column(1).BindText(State.ProviderApiKeyEnv).Placeholder("OPENAI_API_KEY"),
                new TextBlock().Row(3).Text("默认协议").Bold().CenterVertical(),
                new ComboBox()
                    .Row(3)
                    .Column(1)
                    .Ref(out providerProtocolComboBox)
                    .StableItems(State.ProviderProtocols.ToArray())
                    .BindSelectedIndex(State.ProviderProtocolIndex));

        var content = new DockPanel()
            .Spacing(10)
            .Children(
                header,
                editor,
                new StackPanel()
                    .DockTop()
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Button().Content("保存提供方").OnClick(() => RunCollectionCommand(State.SaveProvider, RefreshViewControls)),
                        new Button().Content("测试连接").OnClick(() => RunCollectionCommand(State.TestProviderConnection, RefreshProviderControls))),
                new TextBlock().DockTop().BindText(State.ProviderStatusText).FontSize(11).TextWrapping(TextWrapping.Wrap),
                BuildProviderModelListPanel());

        return new Border()
            .Padding(12)
            .BorderThickness(1)
            .CornerRadius(8)
            .WithTheme((theme, border) => border.Background(theme.Palette.ContainerBackground).BorderBrush(theme.Palette.ControlBorder))
            .Child(content);
    }

    private static Border BuildProviderModelListPanel()
        => new Border()
            .Padding(10)
            .BorderThickness(1)
            .CornerRadius(6)
            .WithTheme((theme, border) => border.Background(theme.Palette.ControlBackground).BorderBrush(theme.Palette.ControlBorder))
            .Child(new StackPanel()
                .Vertical()
                .Spacing(6)
                .Children(
                    new TextBlock().Text("模型列表").Bold(),
                    new ListBox()
                        .Ref(out providerModelsListBox)
                        .Height(180)
                        .ItemHeight(26)
                        .StableItems(State.ProviderModelListItems.ToArray())));

    private static Border BuildModelProtocolMappingPanel()
    {
        var header = new Grid()
            .DockTop()
            .Columns("*,Auto,Auto")
            .Rows("Auto,Auto")
            .Spacing(8)
            .Children(
                new StackPanel()
                    .Vertical()
                    .Spacing(3)
                    .Children(
                        new TextBlock().Text("模型协议适配").Bold().FontSize(18),
                        new TextBlock().Text("为单个提供方配置精确模型覆写与通配规则；协议逗号顺序就是 fallback 优先级。").FontSize(11).TextWrapping(TextWrapping.Wrap)),
                new ComboBox()
                    .Row(1)
                    .Column(0)
                    .Ref(out modelProtocolProviderComboBox)
                    .Height(EditControlHeight)
                    .StableItems(State.ModelProtocolProviderLabels.ToArray())
                    .BindSelectedIndex(State.ModelProtocolProviderIndex)
                    .OnSelectionChanged(_ => ApplyCollectionSelection(
                        () => State.SelectModelProtocolProviderIndex(State.ModelProtocolProviderIndex.Value),
                        RefreshModelProtocolMappingControls)),
                new Button()
                    .Row(1)
                    .Column(1)
                    .Content("检测模型")
                    .Height(EditControlHeight)
                    .OnClick(() => RunCollectionCommand(State.TestModelProtocolProviderModels, RefreshModelProtocolMappingControls)),
                new Button()
                    .Row(1)
                    .Column(2)
                    .Content("保存协议适配")
                    .Height(EditControlHeight)
                    .OnClick(() => RunCollectionCommand(State.SaveModelProtocolMappings, RefreshViewControls)));

        var overrideHeader = new Grid()
            .DockTop()
            .Columns("*,Auto")
            .Rows("Auto,Auto")
            .Spacing(8)
            .Children(
                new TextBlock().Text("精确模型覆写").Bold().FontSize(14),
                new ComboBox()
                    .Row(1)
                    .Column(0)
                    .Ref(out modelProtocolOverrideComboBox)
                    .Height(EditControlHeight)
                    .StableItems(State.ModelProtocolOverrideLabels.ToArray())
                    .BindSelectedIndex(State.ModelProtocolOverrideIndex)
                    .OnSelectionChanged(_ => ApplyCollectionSelection(
                        () => State.SelectModelProtocolOverrideIndex(State.ModelProtocolOverrideIndex.Value),
                        RefreshModelProtocolMappingControls)),
                new StackPanel()
                    .Row(1)
                    .Column(1)
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Button().Content("新增精确覆写").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.BeginNewModelProtocolOverride, RefreshModelProtocolMappingControls)),
                        new Button().Content("删除精确覆写").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.DeleteSelectedModelProtocolOverride, RefreshModelProtocolMappingControls))));

        var overrideEditor = new Grid()
            .DockTop()
            .Columns("180,*")
            .Rows("Auto,Auto,Auto")
            .Spacing(10)
            .Children(
                new TextBlock().Text("模型").Bold().CenterVertical(),
                new ComboBox()
                    .Column(1)
                    .Ref(out modelProtocolModelComboBox)
                    .Height(EditControlHeight)
                    .StableItems(State.ModelProtocolModelLabels.ToArray())
                    .BindSelectedIndex(State.ModelProtocolModelIndex)
                    .OnSelectionChanged(_ => ApplyCollectionSelection(
                        () => State.SelectModelProtocolDetectedModelIndex(State.ModelProtocolModelIndex.Value),
                        RefreshModelProtocolMappingControls)),
                new TextBlock().Row(1).Text("手工模型名").Bold().CenterVertical(),
                new TextBox().Row(1).Column(1).BindText(State.ModelProtocolOverrideName).Placeholder("未检测到或需覆盖时填写，例如 openai-compatible-default"),
                new TextBlock().Row(2).Text("协议优先级").Bold().CenterVertical(),
                new TextBox().Row(2).Column(1).BindText(State.ModelProtocolOverrideProtocols).Placeholder("anthropic_messages, openai_chat_completions"));

        var ruleHeader = new Grid()
            .DockTop()
            .Columns("*,Auto")
            .Rows("Auto,Auto")
            .Spacing(8)
            .Children(
                new TextBlock().Text("通配规则").Bold().FontSize(14),
                new ComboBox()
                    .Row(1)
                    .Column(0)
                    .Ref(out modelProtocolRuleComboBox)
                    .Height(EditControlHeight)
                    .StableItems(State.ModelProtocolRuleLabels.ToArray())
                    .BindSelectedIndex(State.ModelProtocolRuleIndex)
                    .OnSelectionChanged(_ => ApplyCollectionSelection(
                        () => State.SelectModelProtocolRuleIndex(State.ModelProtocolRuleIndex.Value),
                        RefreshModelProtocolMappingControls)),
                new StackPanel()
                    .Row(1)
                    .Column(1)
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Button().Content("新增通配规则").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.BeginNewModelProtocolRule, RefreshModelProtocolMappingControls)),
                        new Button().Content("删除通配规则").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.DeleteSelectedModelProtocolRule, RefreshModelProtocolMappingControls))));

        var ruleEditor = new Grid()
            .DockTop()
            .Columns("180,*")
            .Rows("Auto,Auto")
            .Spacing(10)
            .Children(
                new TextBlock().Text("匹配").Bold().CenterVertical(),
                new TextBox().Column(1).BindText(State.ModelProtocolRuleMatch).Placeholder("例如 deepseek-* 或 qwen*"),
                new TextBlock().Row(1).Text("协议优先级").Bold().CenterVertical(),
                new TextBox().Row(1).Column(1).BindText(State.ModelProtocolRuleProtocols).Placeholder("anthropic_messages, openai_chat_completions"));

        var lists = new Grid()
            .DockTop()
            .Columns("*,*,*")
            .Spacing(12)
            .Children(
                new Border()
                    .Padding(10)
                    .BorderThickness(1)
                    .CornerRadius(6)
                    .WithTheme((theme, border) => border.Background(theme.Palette.ControlBackground).BorderBrush(theme.Palette.ControlBorder))
                    .Child(new StackPanel()
                        .Vertical()
                        .Spacing(6)
                        .Children(
                            new TextBlock().Text("精确覆写列表").Bold(),
                            new ListBox()
                                .Ref(out modelProtocolOverrideListBox)
                                .Height(120)
                                .ItemHeight(24)
                                .StableItems(State.ModelProtocolOverrideLabels.ToArray()))),
                new Border()
                    .Column(1)
                    .Padding(10)
                    .BorderThickness(1)
                    .CornerRadius(6)
                    .WithTheme((theme, border) => border.Background(theme.Palette.ControlBackground).BorderBrush(theme.Palette.ControlBorder))
                    .Child(new StackPanel()
                        .Vertical()
                        .Spacing(6)
                        .Children(
                            new TextBlock().Text("检测模型列表").Bold(),
                            new ListBox()
                                .Ref(out modelProtocolModelsListBox)
                                .Height(120)
                                .ItemHeight(24)
                                .StableItems(State.ModelProtocolModelListItems.ToArray()))),
                new Border()
                    .Column(2)
                    .Padding(10)
                    .BorderThickness(1)
                    .CornerRadius(6)
                    .WithTheme((theme, border) => border.Background(theme.Palette.ControlBackground).BorderBrush(theme.Palette.ControlBorder))
                    .Child(new StackPanel()
                        .Vertical()
                        .Spacing(6)
                        .Children(
                            new TextBlock().Text("通配规则列表").Bold(),
                            new ListBox()
                                .Ref(out modelProtocolRuleListBox)
                                .Height(120)
                                .ItemHeight(24)
                                .StableItems(State.ModelProtocolRuleLabels.ToArray()))));

        var content = new DockPanel()
            .Spacing(10)
            .Children(
                header,
                overrideHeader,
                overrideEditor,
                ruleHeader,
                ruleEditor,
                lists,
                new TextBlock().DockTop().BindText(State.ModelProtocolStatusText).FontSize(11).TextWrapping(TextWrapping.Wrap));

        return new Border()
            .Padding(12)
            .BorderThickness(1)
            .CornerRadius(8)
            .WithTheme((theme, border) => border.Background(theme.Palette.ContainerBackground).BorderBrush(theme.Palette.ControlBorder))
            .Child(content);
    }

    private static Border BuildDefaultModelProtocolRulesPanel()
    {
        var header = new Grid()
            .DockTop()
            .Columns("*,Auto,Auto")
            .Rows("Auto,Auto")
            .Spacing(8)
            .Children(
                new StackPanel()
                    .Vertical()
                    .Spacing(3)
                    .Children(
                        new TextBlock().Text("默认协议规则").Bold().FontSize(18),
                        new TextBlock().Text("配置跨提供方的默认模型通配规则；提供方精确覆写与提供方通配规则会优先覆盖这里。").FontSize(11).TextWrapping(TextWrapping.Wrap)),
                new ComboBox()
                    .Row(1)
                    .Column(0)
                    .Ref(out defaultModelProtocolRuleSetComboBox)
                    .Height(EditControlHeight)
                    .StableItems(State.DefaultModelProtocolRuleSetLabels.ToArray())
                    .BindSelectedIndex(State.DefaultModelProtocolRuleSetIndex)
                    .OnSelectionChanged(_ => ApplyCollectionSelection(
                        () => State.SelectDefaultModelProtocolRuleSetIndex(State.DefaultModelProtocolRuleSetIndex.Value),
                        RefreshDefaultModelProtocolRuleControls)),
                new Button()
                    .Row(1)
                    .Column(1)
                    .Content("恢复默认规则")
                    .Height(EditControlHeight)
                    .OnClick(() => RunCollectionCommand(State.RestoreDefaultModelProtocolRules, RefreshDefaultModelProtocolRuleControls)),
                new Button()
                    .Row(1)
                    .Column(2)
                    .Content("保存规则集")
                    .Height(EditControlHeight)
                    .OnClick(() => RunCollectionCommand(State.SaveDefaultModelProtocolRuleSet, RefreshViewControls)));

        var metaEditor = new Grid()
            .DockTop()
            .Columns("180,*")
            .Rows("Auto,Auto,Auto")
            .Spacing(10)
            .Children(
                new TextBlock().Text("规则集 ID").Bold().CenterVertical(),
                new TextBox().Column(1).BindText(State.DefaultModelProtocolRuleSetId).Placeholder("default"),
                new TextBlock().Row(1).Text("显示名").Bold().CenterVertical(),
                new TextBox().Row(1).Column(1).BindText(State.DefaultModelProtocolRuleSetDisplayName).Placeholder("Default Model Protocol Rules"),
                new TextBlock().Row(2).Text("说明").Bold().CenterVertical(),
                new TextBox().Row(2).Column(1).BindText(State.DefaultModelProtocolRuleSetDescription).Placeholder("Common model-family to wire protocol priority rules."));

        var ruleHeader = new Grid()
            .DockTop()
            .Columns("*,Auto")
            .Rows("Auto")
            .Spacing(8)
            .Children(
                new TextBlock().Text("通配规则").Bold().FontSize(14),
                new StackPanel()
                    .Column(1)
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Button().Content("新增规则").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.BeginNewDefaultModelProtocolRule, RefreshDefaultModelProtocolRuleControls)),
                        new Button().Content("删除规则").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.DeleteSelectedDefaultModelProtocolRule, RefreshDefaultModelProtocolRuleControls))));

        var ruleEditor = new Grid()
            .DockTop()
            .Columns("180,*")
            .Rows("Auto,Auto")
            .Spacing(10)
            .Children(
                new TextBlock().Text("匹配").Bold().CenterVertical(),
                new TextBox().Column(1).BindText(State.DefaultModelProtocolRuleMatch).Placeholder("例如 qwen*、anthropic/claude* 或 openai/gpt-*"),
                new TextBlock().Row(1).Text("协议优先级").Bold().CenterVertical(),
                new TextBox().Row(1).Column(1).BindText(State.DefaultModelProtocolRuleProtocols).Placeholder("anthropic_messages, openai_chat_completions"));

        var list = new Border()
            .DockTop()
            .Padding(10)
            .BorderThickness(1)
            .CornerRadius(6)
            .WithTheme((theme, border) => border.Background(theme.Palette.ControlBackground).BorderBrush(theme.Palette.ControlBorder))
            .Child(new StackPanel()
                .Vertical()
                .Spacing(6)
                .Children(
                    new TextBlock().Text("默认规则列表").Bold(),
                    new ListBox()
                        .Ref(out defaultModelProtocolRuleListBox)
                        .Height(180)
                        .ItemHeight(24)
                        .StableItems(State.DefaultModelProtocolRuleLabels.ToArray())
                        .BindSelectedIndex(State.DefaultModelProtocolRuleIndex)
                        .OnSelectionChanged(_ => ApplyCollectionSelection(
                            () => State.SelectDefaultModelProtocolRuleIndex(State.DefaultModelProtocolRuleIndex.Value),
                            RefreshDefaultModelProtocolRuleControls))));

        var content = new DockPanel()
            .Spacing(10)
            .Children(
                header,
                metaEditor,
                ruleHeader,
                ruleEditor,
                list,
                new TextBlock().DockTop().BindText(State.DefaultModelProtocolRuleStatusText).FontSize(11).TextWrapping(TextWrapping.Wrap));

        return new Border()
            .Padding(12)
            .BorderThickness(1)
            .CornerRadius(8)
            .WithTheme((theme, border) => border.Background(theme.Palette.ContainerBackground).BorderBrush(theme.Palette.ControlBorder))
            .Child(content);
    }

    private static Border BuildModelRouteSetCollectionPanel()
    {
        var header = new Grid()
            .DockTop()
            .Columns("*,Auto")
            .Rows("Auto,Auto")
            .Spacing(8)
            .Children(
                new StackPanel()
                    .Vertical()
                    .Spacing(3)
                    .Children(
                        new TextBlock().Text("模型路由方案").Bold().FontSize(18),
                        new TextBlock().Text("按 route set、route 与候选顺序编辑模型路由；候选列表第一项就是首选模型。").FontSize(11).TextWrapping(TextWrapping.Wrap)),
                new ComboBox()
                    .Row(1)
                    .Column(0)
                    .Ref(out modelRouteSetComboBox)
                    .Height(EditControlHeight)
                    .StableItems(State.ModelRouteSetLabels.ToArray())
                    .BindSelectedIndex(State.ModelRouteSetIndex)
                    .OnSelectionChanged(_ => ApplyCollectionSelectionFromControl(
                        modelRouteSetComboBox,
                        State.SelectModelRouteSetIndex,
                        RefreshModelRouteSetControls)),
                new StackPanel()
                    .Row(1)
                    .Column(1)
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Button().Content("新建方案").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.BeginNewModelRouteSet, RefreshModelRouteSetControls)),
                        new Button().Content("复制").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.CopySelectedModelRouteSetToDraft, RefreshModelRouteSetControls)),
                        new Button().Content("保存方案").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.SaveModelRouteSet, RefreshViewControls)),
                        new Button().Content("删除").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.DeleteSelectedModelRouteSet, RefreshViewControls))));

        var catalogEditor = new Grid()
            .DockTop()
            .Columns("160,*")
            .Rows("Auto,Auto,Auto")
            .Spacing(10)
            .Children(
                new TextBlock().Text("方案 ID").Bold().CenterVertical(),
                new TextBox().Column(1).BindText(State.ModelRouteSetId).Placeholder("例如 workbench"),
                new TextBlock().Row(1).Text("显示名").Bold().CenterVertical(),
                new TextBox().Row(1).Column(1).BindText(State.ModelRouteSetDisplayName).Placeholder("Workbench Route Scheme"),
                new TextBlock().Row(2).Text("说明").Bold().CenterVertical(),
                new TextBox().Row(2).Column(1).BindText(State.ModelRouteSetDescription).Placeholder("当前方案的用途说明"));

        var routeEditor = new Grid()
            .DockTop()
            .Columns("160,*")
            .Rows("Auto,Auto,Auto,Auto")
            .Spacing(8)
            .Children(
                new TextBlock().Text("Route").Bold().FontSize(15),
                new ComboBox()
                    .Row(1)
                    .ColumnSpan(2)
                    .Ref(out modelRouteSetRouteComboBox)
                    .Height(EditControlHeight)
                    .StableItems(State.ModelRouteSetRouteLabels.ToArray())
                    .BindSelectedIndex(State.ModelRouteSetRouteIndex)
                    .OnSelectionChanged(_ => ApplyCollectionSelectionFromControl(
                        modelRouteSetRouteComboBox,
                        State.SelectModelRouteSetRouteIndex,
                        RefreshModelRouteSetControls)),
                new TextBlock().Row(2).Text("Route Kind").Bold().CenterVertical(),
                new TextBlock().Row(2).Column(1).BindText(State.ModelRouteSetRouteKind).CenterVertical(),
                new TextBlock().Row(3).Text("Fallback Route").Bold().CenterVertical(),
                new TextBox().Row(3).Column(1).BindText(State.ModelRouteSetRouteFallback).Placeholder("可选，例如 default"));

        var candidateEditor = new Grid()
            .DockTop()
            .Columns("160,*")
            .Rows("Auto,Auto,Auto,Auto,Auto,Auto")
            .Spacing(8)
            .Children(
                new TextBlock().Text("候选").Bold().FontSize(15),
                new ComboBox()
                    .Row(1)
                    .ColumnSpan(2)
                    .Ref(out modelRouteSetCandidateComboBox)
                    .Height(EditControlHeight)
                    .StableItems(State.ModelRouteSetCandidateLabels.ToArray())
                    .BindSelectedIndex(State.ModelRouteSetCandidateIndex)
                    .OnSelectionChanged(_ => ApplyCollectionSelectionFromControl(
                        modelRouteSetCandidateComboBox,
                        State.SelectModelRouteSetCandidateIndex,
                        RefreshModelRouteSetControls)),
                new TextBlock().Row(2).Text("提供方").Bold().CenterVertical(),
                new ComboBox()
                    .Row(2)
                    .Column(1)
                    .Ref(out modelRouteSetCandidateProviderComboBox)
                    .Height(EditControlHeight)
                    .StableItems(State.ModelRouteSetProviderOptions.ToArray())
                    .BindSelectedIndex(State.ModelRouteSetCandidateProviderIndex)
                    .OnSelectionChanged(_ => ApplyCollectionSelectionFromControl(modelRouteSetCandidateProviderComboBox, State.SelectModelRouteSetCandidateProviderIndex)),
                new TextBlock().Row(3).Text("Model").Bold().CenterVertical(),
                new ComboBox()
                    .Row(3)
                    .Column(1)
                    .Ref(out modelRouteSetCandidateModelComboBox)
                    .Height(EditControlHeight)
                    .StableItems(State.ModelRouteSetModelOptions.ToArray())
                    .BindSelectedIndex(State.ModelRouteSetCandidateModelIndex)
                    .OnSelectionChanged(_ => ApplyCollectionSelectionFromControl(modelRouteSetCandidateModelComboBox, State.SelectModelRouteSetCandidateModelIndex)),
                new TextBlock().Row(4).Text("Protocol").Bold().CenterVertical(),
                new ComboBox()
                    .Row(4)
                    .Column(1)
                    .Ref(out modelRouteSetCandidateProtocolComboBox)
                    .Height(EditControlHeight)
                    .StableItems(State.ModelRouteSetProtocolOptions.ToArray())
                    .BindSelectedIndex(State.ModelRouteSetCandidateProtocolIndex)
                    .OnSelectionChanged(_ => ApplyCollectionSelectionFromControl(modelRouteSetCandidateProtocolComboBox, State.SelectModelRouteSetCandidateProtocolIndex)),
                new TextBlock().Row(5).Text("Capabilities").Bold().CenterVertical(),
                new TextBox().Row(5).Column(1).BindText(State.ModelRouteSetCandidateCapabilities).Placeholder("逗号分隔，例如 code,fast"));

        var candidateButtons = new StackPanel()
            .DockTop()
            .Horizontal()
            .Spacing(8)
            .Children(
                new Button().Content("新增候选").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.BeginNewModelRouteSetCandidate, RefreshModelRouteSetControls)),
                new Button().Content("删除候选").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.DeleteSelectedModelRouteSetCandidate, RefreshModelRouteSetControls)),
                new Button().Content("上移").Height(EditControlHeight).OnClick(() => RunCollectionCommand(() => State.MoveSelectedModelRouteSetCandidate(-1), RefreshModelRouteSetControls)),
                new Button().Content("下移").Height(EditControlHeight).OnClick(() => RunCollectionCommand(() => State.MoveSelectedModelRouteSetCandidate(1), RefreshModelRouteSetControls)));

        var summaryLists = new Grid()
            .DockTop()
            .Columns("*,*")
            .Spacing(10)
            .Children(
                new Border()
                    .Padding(10)
                    .BorderThickness(1)
                    .CornerRadius(6)
                    .WithTheme((theme, border) => border.Background(theme.Palette.ControlBackground).BorderBrush(theme.Palette.ControlBorder))
                    .Child(new StackPanel()
                        .Vertical()
                        .Spacing(6)
                        .Children(
                            new TextBlock().Text("Route 列表").Bold(),
                            new ListBox().Ref(out modelRouteSetRouteListBox).Height(120).ItemHeight(24).StableItems(State.ModelRouteSetRouteLabels.ToArray()))),
                new Border()
                    .Column(1)
                    .Padding(10)
                    .BorderThickness(1)
                    .CornerRadius(6)
                    .WithTheme((theme, border) => border.Background(theme.Palette.ControlBackground).BorderBrush(theme.Palette.ControlBorder))
                    .Child(new StackPanel()
                        .Vertical()
                        .Spacing(6)
                        .Children(
                            new TextBlock().Text("候选顺序").Bold(),
                            new ListBox().Ref(out modelRouteSetCandidateListBox).Height(120).ItemHeight(24).StableItems(State.ModelRouteSetCandidateLabels.ToArray()))));

        return new Border()
            .Padding(12)
            .BorderThickness(1)
            .CornerRadius(8)
            .WithTheme((theme, border) => border.Background(theme.Palette.ContainerBackground).BorderBrush(theme.Palette.ControlBorder))
            .Child(new DockPanel()
                .Spacing(10)
                .Children(
                    header,
                    catalogEditor,
                    routeEditor,
                    candidateEditor,
                    candidateButtons,
                    summaryLists,
                    new TextBlock().DockTop().BindText(State.ModelRouteSetStatusText).FontSize(11).TextWrapping(TextWrapping.Wrap)));
    }

    private static Border BuildProviderPackageCollectionPanel()
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
                        new TextBlock().Text("协议适配器包").Bold().FontSize(18),
                        new TextBlock().BindText(State.ModelProviderPackageRootText).FontSize(11).TextWrapping(TextWrapping.Wrap)),
                new ComboBox()
                    .Row(1)
                    .Column(0)
                    .Ref(out modelProviderPackageComboBox)
                    .Height(EditControlHeight)
                    .StableItems(State.ModelProviderPackageLabels.ToArray())
                    .BindSelectedIndex(State.ModelProviderPackageIndex)
                    .OnSelectionChanged(_ => ApplyCollectionSelection(
                        () => State.SelectModelProviderPackageIndex(State.ModelProviderPackageIndex.Value),
                        RefreshProviderPackageControls)),
                new StackPanel()
                    .Row(1)
                    .Column(1)
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Button().Content("新建").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.BeginNewModelProviderPackage, RefreshProviderPackageControls)),
                        new Button().Content("复制").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.CopySelectedModelProviderPackageToDraft, RefreshProviderPackageControls)),
                        new Button().Content("删除").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.DeleteSelectedModelProviderPackage, RefreshViewControls))),
                new TextBlock().Row(2).ColumnSpan(2).BindText(State.ModelProviderPackageManifestPathText).FontSize(11).TextWrapping(TextWrapping.Wrap));

        var packageEditor = new Grid()
            .DockTop()
            .Columns("*,300")
            .Spacing(24)
            .Children(
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text("适配器包基本信息").Bold().FontSize(13),
                        new Grid()
                            .Columns("140,*")
                            .Rows("Auto,Auto,Auto")
                            .Spacing(10)
                            .Children(
                                new TextBlock().Text("包 ID").Bold().CenterVertical(),
                                new TextBox().Column(1).BindText(State.ModelProviderPackageId).Placeholder("例如 company-providers"),
                                new TextBlock().Row(1).Text("显示名").Bold().CenterVertical(),
                                new TextBox().Row(1).Column(1).BindText(State.ModelProviderPackageDisplayName).Placeholder("显示名称"),
                                new TextBlock().Row(2).Text("优先级").Bold().CenterVertical(),
                                new TextBox().Row(2).Column(1).BindText(State.ModelProviderPackagePriority).Placeholder("0"))),
                new StackPanel()
                    .Column(1)
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text("适配器包加载设置").Bold().FontSize(13),
                        new Grid()
                            .Columns("120,*")
                            .Rows("Auto,Auto")
                            .Spacing(10)
                            .Children(
                                new TextBlock().Text("启用状态").Bold().CenterVertical(),
                                new ComboBox()
                                    .Column(1)
                                    .Ref(out modelProviderPackageEnabledComboBox)
                                    .StableItems(State.ToolBooleanLabels.ToArray())
                                    .BindSelectedIndex(State.ModelProviderPackageEnabledIndex),
                                new TextBlock().Row(1).Text("类型").Bold().CenterVertical(),
                                new ComboBox()
                                    .Row(1)
                                    .Column(1)
                                    .Ref(out modelProviderPackageTypeComboBox)
                                    .StableItems(State.ModelProviderPackageTypeLabels.ToArray())
                                    .BindSelectedIndex(State.ModelProviderPackageTypeIndex))));

        var adapterHeader = new Grid()
            .DockTop()
            .Columns("*,Auto")
            .Rows("Auto,Auto")
            .Spacing(8)
            .Children(
                new TextBlock().Text("Adapter 条目").Bold().FontSize(15),
                new ComboBox()
                    .Row(1)
                    .Column(0)
                    .Ref(out modelProviderAdapterComboBox)
                    .Height(EditControlHeight)
                    .StableItems(State.ModelProviderAdapterLabels.ToArray())
                    .BindSelectedIndex(State.ModelProviderAdapterIndex)
                    .OnSelectionChanged(_ => ApplyCollectionSelection(
                        () => State.SelectModelProviderAdapterIndex(State.ModelProviderAdapterIndex.Value),
                        RefreshProviderPackageControls)),
                new StackPanel()
                    .Row(1)
                    .Column(1)
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Button().Content("新增 Adapter").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.BeginNewModelProviderAdapter, RefreshProviderPackageControls)),
                        new Button().Content("删除 Adapter").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.DeleteSelectedModelProviderAdapter, RefreshProviderPackageControls))));

        var adapterEditor = new Grid()
            .DockTop()
            .Columns("*,300")
            .Spacing(24)
            .Children(
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text("Adapter 基本信息").Bold().FontSize(13),
                        new Grid()
                            .Columns("140,*")
                            .Rows("Auto,Auto,Auto,Auto")
                            .Spacing(10)
                            .Children(
                                new TextBlock().Text("Adapter ID").Bold().CenterVertical(),
                                new TextBox().Column(1).BindText(State.ModelProviderAdapterId).Placeholder("例如 openai_responses"),
                                new TextBlock().Row(1).Text("显示名").Bold().CenterVertical(),
                                new TextBox().Row(1).Column(1).BindText(State.ModelProviderAdapterDisplayName).Placeholder("显示名称"),
                                new TextBlock().Row(2).Text("类型").Bold().CenterVertical(),
                                new ComboBox()
                                    .Row(2)
                                    .Column(1)
                                    .Ref(out modelProviderAdapterTypeComboBox)
                                    .StableItems(State.ModelProviderAdapterTypeLabels.ToArray())
                                    .BindSelectedIndex(State.ModelProviderAdapterTypeIndex),
                                new TextBlock().Row(3).Text("程序集相对路径").Bold().CenterVertical(),
                                new TextBox().Row(3).Column(1).BindText(State.ModelProviderAdapterAssemblyPath).Placeholder("./openai/TianShu.Provider.OpenAI.dll"))),
                new StackPanel()
                    .Column(1)
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text("Adapter 加载设置").Bold().FontSize(13),
                        new Grid()
                            .Columns("120,*")
                            .Rows("Auto,Auto")
                            .Spacing(10)
                            .Children(
                                new TextBlock().Text("启用状态").Bold().CenterVertical(),
                                new ComboBox()
                                    .Column(1)
                                    .Ref(out modelProviderAdapterEnabledComboBox)
                                    .StableItems(State.ToolBooleanLabels.ToArray())
                                    .BindSelectedIndex(State.ModelProviderAdapterEnabledIndex),
                                new TextBlock().Row(1).Text("优先级").Bold().CenterVertical(),
                                new TextBox().Row(1).Column(1).BindText(State.ModelProviderAdapterPriority).Placeholder("10"))));

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
                        new Button().Content("保存适配器包").OnClick(() => RunCollectionCommand(State.SaveModelProviderPackage, RefreshViewControls))),
                adapterHeader,
                adapterEditor,
                new TextBlock().DockTop().BindText(State.ModelProviderAdapterResolvedPathText).FontSize(11).TextWrapping(TextWrapping.Wrap),
                new TextBlock().DockTop().BindText(State.ModelProviderPackageStatusText).FontSize(11).TextWrapping(TextWrapping.Wrap));

        return new Border()
            .Padding(12)
            .BorderThickness(1)
            .CornerRadius(8)
            .WithTheme((theme, border) => border.Background(theme.Palette.ContainerBackground).BorderBrush(theme.Palette.ControlBorder))
            .Child(content);
    }
}
