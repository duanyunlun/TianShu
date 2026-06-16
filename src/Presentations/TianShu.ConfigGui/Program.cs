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
    private const int MainTableNameColumnWidth = 190;
    private const int MainTableValueColumnWidth = 320;
    private const int EditControlHeight = 32;
    private const int ContentTitleFontSize = 18;

    private static readonly Color SecondaryNavigationBackground = Color.FromRgb(43, 45, 49);
    private static readonly Color NavigationHighlightBackground = Color.FromRgb(38, 52, 71);
    private static readonly Color SecondaryNavigationBorder = Color.FromRgb(68, 71, 78);
    private static readonly Color NavigationHighlightBorder = Color.FromRgb(79, 131, 199);

    private static readonly ConfigGuiState State = new();
    private static GridView fieldGrid = null!;
    private static GridView workspaceFieldGrid = null!;
    private static ScrollViewer categoryScrollViewer = null!;
    private static readonly AutoHidingScrollState categoryScrollState = new();
    private static ScrollViewer contentScrollViewer = null!;
    private static readonly AutoHidingScrollState contentScrollState = new();
    private static MultiLineTextBox detailTextEditor = null!;
    private static ComboBox detailEnumEditor = null!;
    private static Border detailEnumEditorHost = null!;
    private static ScrollViewer detailScrollViewer = null!;
    private static readonly AutoHidingScrollState detailScrollState = new();
    private static ComboBox providerComboBox = null!;
    private static ComboBox providerProtocolComboBox = null!;
    private static ListBox providerModelsListBox = null!;
    private static ComboBox modelProtocolProviderComboBox = null!;
    private static ComboBox modelProtocolOverrideComboBox = null!;
    private static ComboBox modelProtocolModelComboBox = null!;
    private static ComboBox modelProtocolRuleComboBox = null!;
    private static ListBox modelProtocolModelsListBox = null!;
    private static ListBox modelProtocolOverrideListBox = null!;
    private static ListBox modelProtocolRuleListBox = null!;
    private static ComboBox defaultModelProtocolRuleSetComboBox = null!;
    private static ListBox defaultModelProtocolRuleListBox = null!;
    private static ComboBox modelRouteSetComboBox = null!;
    private static ComboBox modelRouteSetRouteComboBox = null!;
    private static ComboBox modelRouteSetCandidateComboBox = null!;
    private static ComboBox modelRouteSetCandidateProviderComboBox = null!;
    private static ComboBox modelRouteSetCandidateModelComboBox = null!;
    private static ComboBox modelRouteSetCandidateProtocolComboBox = null!;
    private static ListBox modelRouteSetRouteListBox = null!;
    private static ListBox modelRouteSetCandidateListBox = null!;
    private static ComboBox promptFileComboBox = null!;
    private static ComboBox promptSectionComboBox = null!;
    private static ComboBox promptEnabledComboBox = null!;
    private static ComboBox promptModeComboBox = null!;
    private static ComboBox skillPackageComboBox = null!;
    private static ComboBox skillPackageEnabledComboBox = null!;
    private static ComboBox toolPackageComboBox = null!;
    private static ComboBox toolPackageEnabledComboBox = null!;
    private static ComboBox toolPackageTypeComboBox = null!;
    private static ComboBox toolProviderComboBox = null!;
    private static ComboBox toolProviderEnabledComboBox = null!;
    private static ComboBox toolProviderTypeComboBox = null!;
    private static ComboBox toolProviderReplaceExistingComboBox = null!;
    private static ComboBox modelProviderPackageComboBox = null!;
    private static ComboBox modelProviderPackageEnabledComboBox = null!;
    private static ComboBox modelProviderPackageTypeComboBox = null!;
    private static ComboBox modelProviderAdapterComboBox = null!;
    private static ComboBox modelProviderAdapterEnabledComboBox = null!;
    private static ComboBox modelProviderAdapterTypeComboBox = null!;
    private static ComboBox mcpServerPackageComboBox = null!;
    private static ComboBox mcpServerPackageEnabledComboBox = null!;
    private static ComboBox mcpServerPackageTypeComboBox = null!;
    private static ComboBox mcpServerComboBox = null!;
    private static ComboBox mcpServerEnabledComboBox = null!;
    private static ComboBox mcpServerRequiredComboBox = null!;
    private static ComboBox mcpServerTransportComboBox = null!;
    private static ComboBox artifactStorePackageComboBox = null!;
    private static ComboBox artifactStorePackageEnabledComboBox = null!;
    private static ComboBox artifactStorePackageTypeComboBox = null!;
    private static ComboBox artifactStoreComboBox = null!;
    private static ComboBox artifactStoreEnabledComboBox = null!;
    private static ComboBox artifactStoreTypeComboBox = null!;
    private static ComboBox artifactStoreCrossProcessSyncComboBox = null!;
    private static ComboBox diagnosticSinkPackageComboBox = null!;
    private static ComboBox diagnosticSinkPackageEnabledComboBox = null!;
    private static ComboBox diagnosticSinkPackageTypeComboBox = null!;
    private static ComboBox diagnosticSinkComboBox = null!;
    private static ComboBox diagnosticSinkEnabledComboBox = null!;
    private static ComboBox diagnosticSinkTypeComboBox = null!;
    private static ComboBox diagnosticSinkLevelComboBox = null!;
    private static ComboBox workspaceResolverPackageComboBox = null!;
    private static ComboBox workspaceResolverPackageEnabledComboBox = null!;
    private static ComboBox workspaceResolverPackageTypeComboBox = null!;
    private static ComboBox workspaceResolverComboBox = null!;
    private static ComboBox workspaceResolverEnabledComboBox = null!;
    private static ComboBox workspaceResolverTypeComboBox = null!;
    private static ComboBox policyStrategyPackageComboBox = null!;
    private static ComboBox policyStrategyPackageEnabledComboBox = null!;
    private static ComboBox policyStrategyPackageTypeComboBox = null!;
    private static ComboBox policyStrategyComboBox = null!;
    private static ComboBox policyStrategyEnabledComboBox = null!;
    private static ComboBox policyStrategyTypeComboBox = null!;
    private static ComboBox policyStrategyApprovalPolicyComboBox = null!;
    private static ComboBox policyStrategySandboxModeComboBox = null!;
    private static ComboBox policyStrategyNetworkAccessComboBox = null!;
    private static ComboBox policyStrategyAllowLoginShellComboBox = null!;
    private static bool suppressEnumSelectionEvents;
    private static bool suppressCollectionSelectionEvents;
    private static bool isApplyingCollectionSelection;

    [STAThread]
    private static int Main(string[] args)
    {
        if (ConfigGuiSmokeCommand.TryRun(args, out var smokeExitCode))
        {
            return smokeExitCode;
        }

        Win32Platform.Register();
        Direct2DBackend.Register();

        State.Refresh();

        var window = new Window()
            .Title("天枢 TianShu ConfigGUI")
            .Resizable(1260, 780)
            .Padding(0)
            .Content(BuildRoot());

        Application.Run(window);
        return 0;
    }

    private static void SaveSelected()
    {
        State.SyncEnumSelection();
        State.SaveSelected();
        RefreshFieldGrid();
    }

    private static void RefreshFieldGrid()
    {
        if (fieldGrid is not null)
        {
            fieldGrid.ItemsSource(State.FilteredFields);
        }

        if (workspaceFieldGrid is not null)
        {
            workspaceFieldGrid.ItemsSource(State.WorkspaceFields);
        }

        RefreshDetailControls();
    }

    private static void RefreshViewControls()
    {
        CloseAllComboBoxDropDowns();
        RefreshFieldGrid();
        RefreshProviderControls();
        RefreshModelProtocolMappingControls();
        RefreshDefaultModelProtocolRuleControls();
        RefreshModelRouteSetControls();
        RefreshPromptControls();
        RefreshSkillPackageControls();
        RefreshToolPackageControls();
        RefreshProviderPackageControls();
        RefreshMcpServerPackageControls();
        RefreshArtifactStorePackageControls();
        RefreshDiagnosticSinkPackageControls();
        RefreshWorkspaceResolverPackageControls();
        RefreshPolicyStrategyPackageControls();
    }

    private static void RefreshProviderControls()
    {
        ReplaceComboBoxItems(providerComboBox, State.ProviderItemLabels, State.ProviderItemIndex.Value);
        ReplaceComboBoxItems(providerProtocolComboBox, State.ProviderProtocols, State.ProviderProtocolIndex.Value);

        if (providerModelsListBox is not null)
        {
            ConfigGuiListBoxUtilities.ReplaceItemsAndSelect(providerModelsListBox, State.ProviderModelListItems, -1);
        }
    }

    private static void RefreshModelProtocolMappingControls()
    {
        ReplaceComboBoxItems(modelProtocolProviderComboBox, State.ModelProtocolProviderLabels, State.ModelProtocolProviderIndex.Value);
        ReplaceComboBoxItems(modelProtocolOverrideComboBox, State.ModelProtocolOverrideLabels, State.ModelProtocolOverrideIndex.Value);
        ReplaceComboBoxItems(modelProtocolModelComboBox, State.ModelProtocolModelLabels, State.ModelProtocolModelIndex.Value);
        ReplaceComboBoxItems(modelProtocolRuleComboBox, State.ModelProtocolRuleLabels, State.ModelProtocolRuleIndex.Value);

        if (modelProtocolOverrideListBox is not null)
        {
            ConfigGuiListBoxUtilities.ReplaceItemsAndSelect(modelProtocolOverrideListBox, State.ModelProtocolOverrideLabels, State.ModelProtocolOverrideIndex.Value);
        }

        if (modelProtocolModelsListBox is not null)
        {
            var selectedIndex = State.ModelProtocolModelLabels.Count == 0 ? -1 : State.ModelProtocolModelIndex.Value;
            ConfigGuiListBoxUtilities.ReplaceItemsAndSelect(modelProtocolModelsListBox, State.ModelProtocolModelListItems, selectedIndex);
        }

        if (modelProtocolRuleListBox is not null)
        {
            ConfigGuiListBoxUtilities.ReplaceItemsAndSelect(modelProtocolRuleListBox, State.ModelProtocolRuleLabels, State.ModelProtocolRuleIndex.Value);
        }
    }

    private static void RefreshDefaultModelProtocolRuleControls()
    {
        ReplaceComboBoxItems(defaultModelProtocolRuleSetComboBox, State.DefaultModelProtocolRuleSetLabels, State.DefaultModelProtocolRuleSetIndex.Value);

        if (defaultModelProtocolRuleListBox is not null)
        {
            ConfigGuiListBoxUtilities.ReplaceItemsAndSelect(defaultModelProtocolRuleListBox, State.DefaultModelProtocolRuleLabels, State.DefaultModelProtocolRuleIndex.Value);
        }
    }

    private static void RefreshModelRouteSetControls()
    {
        ReplaceComboBoxItems(modelRouteSetComboBox, State.ModelRouteSetLabels, State.ModelRouteSetIndex.Value);
        ReplaceComboBoxItems(modelRouteSetRouteComboBox, State.ModelRouteSetRouteLabels, State.ModelRouteSetRouteIndex.Value);
        ReplaceComboBoxItems(modelRouteSetCandidateComboBox, State.ModelRouteSetCandidateLabels, State.ModelRouteSetCandidateIndex.Value);
        ReplaceComboBoxItems(modelRouteSetCandidateProviderComboBox, State.ModelRouteSetProviderOptions, State.ModelRouteSetCandidateProviderIndex.Value);
        ReplaceComboBoxItems(modelRouteSetCandidateModelComboBox, State.ModelRouteSetModelOptions, State.ModelRouteSetCandidateModelIndex.Value);
        ReplaceComboBoxItems(modelRouteSetCandidateProtocolComboBox, State.ModelRouteSetProtocolOptions, State.ModelRouteSetCandidateProtocolIndex.Value);

        if (modelRouteSetRouteListBox is not null)
        {
            ConfigGuiListBoxUtilities.ReplaceItemsAndSelect(modelRouteSetRouteListBox, State.ModelRouteSetRouteLabels, State.ModelRouteSetRouteIndex.Value);
        }

        if (modelRouteSetCandidateListBox is not null)
        {
            ConfigGuiListBoxUtilities.ReplaceItemsAndSelect(modelRouteSetCandidateListBox, State.ModelRouteSetCandidateLabels, State.ModelRouteSetCandidateIndex.Value);
        }
    }

    internal static (bool Passed, string Detail) RunModelRouteSetRouteBindingSmoke()
    {
        State.Refresh();
        var categoryId = State.Categories.FirstOrDefault(static category => category.DisplayName == "模型路由方案")?.Id;
        if (string.IsNullOrWhiteSpace(categoryId))
        {
            return (false, "未找到模型路由方案分类。");
        }

        State.SelectCategory(categoryId);
        BuildModelRouteSetCollectionPanel();
        var expectedRouteKinds = TianShuModelRouteSetDefaults.DefaultRouteKinds;
        var stateRouteKinds = State.ModelRouteSetRouteLabels.Select(ExtractModelRouteSetRouteKind).ToArray();
        var labels = ConfigGuiComboBoxUtilities.SnapshotLabels(modelRouteSetRouteComboBox, Math.Max(expectedRouteKinds.Count + 2, 4));
        var controlRouteKinds = labels.Select(ExtractModelRouteSetRouteKind).ToArray();
        var ok = stateRouteKinds.SequenceEqual(expectedRouteKinds, StringComparer.OrdinalIgnoreCase)
            && controlRouteKinds.SequenceEqual(expectedRouteKinds, StringComparer.OrdinalIgnoreCase)
            && State.ModelRouteSetRouteIndex.Value == 0
            && modelRouteSetRouteComboBox.SelectedIndex == 0
            && string.Equals(State.ModelRouteSetRouteKind.Value, expectedRouteKinds[0], StringComparison.OrdinalIgnoreCase);

        return (ok, $"expected={string.Join(", ", expectedRouteKinds)}; state={string.Join(", ", stateRouteKinds)}; control={string.Join(", ", controlRouteKinds)}; stateIndex={State.ModelRouteSetRouteIndex.Value}; controlIndex={modelRouteSetRouteComboBox.SelectedIndex}");
    }

    internal static (bool Passed, string Detail) RunModelRouteSetRouteSelectionSmoke()
    {
        State.Refresh();
        var categoryId = State.Categories.FirstOrDefault(static category => category.DisplayName == "模型路由方案")?.Id;
        if (string.IsNullOrWhiteSpace(categoryId))
        {
            return (false, "未找到模型路由方案分类。");
        }

        State.SelectCategory(categoryId);
        BuildModelRouteSetCollectionPanel();
        var labels = State.ModelRouteSetRouteLabels;
        var targetIndex = Enumerable.Range(0, labels.Count)
            .FirstOrDefault(index => !labels[index].Contains(". default ", StringComparison.OrdinalIgnoreCase));
        if (targetIndex <= 0 && labels.Count > 1)
        {
            targetIndex = 1;
        }

        var expectedKind = ExtractModelRouteSetRouteKind(labels[targetIndex]);
        modelRouteSetRouteComboBox.SelectedIndex = targetIndex;
        ApplyCollectionSelectionFromControl(modelRouteSetRouteComboBox, State.SelectModelRouteSetRouteIndex, RefreshModelRouteSetControls);
        var labelsAfterSelection = State.ModelRouteSetRouteLabels;

        var ok = State.ModelRouteSetRouteIndex.Value == targetIndex
            && modelRouteSetRouteComboBox.SelectedIndex == targetIndex
            && string.Equals(State.ModelRouteSetRouteKind.Value, expectedKind, StringComparison.OrdinalIgnoreCase)
            && targetIndex < labelsAfterSelection.Count
            && string.Equals(labelsAfterSelection[targetIndex], labels[targetIndex], StringComparison.Ordinal);

        return (ok,
            $"targetIndex={targetIndex}; expected={expectedKind}; actual={State.ModelRouteSetRouteKind.Value}; stateIndex={State.ModelRouteSetRouteIndex.Value}; controlIndex={modelRouteSetRouteComboBox.SelectedIndex}; labelsBefore={string.Join(", ", labels)}; labelsAfter={string.Join(", ", labelsAfterSelection)}");
    }

    private static string ExtractModelRouteSetRouteKind(string label)
    {
        var trimmed = label.Trim();
        var dotIndex = trimmed.IndexOf('.');
        if (dotIndex >= 0)
        {
            trimmed = trimmed[(dotIndex + 1)..].TrimStart();
        }

        var parenIndex = trimmed.IndexOf(" (", StringComparison.Ordinal);
        return parenIndex > 0 ? trimmed[..parenIndex].Trim() : trimmed;
    }

    internal static (bool Passed, string Detail) RunCollectionCommandBindingSmoke()
    {
        State.Refresh();
        var details = new List<string>();
        var allPassed = true;

        CheckCollectionCommand(
            "model-route-set-candidate-add",
            BuildModelRouteSetCollectionPanel,
            () => State.ModelRouteSetCandidateLabels.Count,
            () => RunCollectionCommand(State.BeginNewModelRouteSetCandidate, RefreshModelRouteSetControls),
            () => State.ModelRouteSetCandidateIndex.Value,
            () => modelRouteSetCandidateComboBox?.SelectedIndex ?? -1,
            () => modelRouteSetCandidateComboBox,
            details,
            ref allPassed);

        CheckCollectionCommand(
            "tool-provider-add",
            BuildToolPackageCollectionPanel,
            () => State.ToolProviderLabels.Count,
            () => RunCollectionCommand(State.BeginNewToolProvider, RefreshToolPackageControls),
            () => State.ToolProviderIndex.Value,
            () => toolProviderComboBox?.SelectedIndex ?? -1,
            () => toolProviderComboBox,
            details,
            ref allPassed);

        CheckCollectionCommand(
            "mcp-server-add",
            BuildMcpServerPackageCollectionPanel,
            () => State.McpServerLabels.Count,
            () => RunCollectionCommand(State.BeginNewMcpServer, RefreshMcpServerPackageControls),
            () => State.McpServerIndex.Value,
            () => mcpServerComboBox?.SelectedIndex ?? -1,
            () => mcpServerComboBox,
            details,
            ref allPassed);

        CheckCollectionCommand(
            "artifact-store-add",
            BuildArtifactStorePackageCollectionPanel,
            () => State.ArtifactStoreLabels.Count,
            () => RunCollectionCommand(State.BeginNewArtifactStore, RefreshArtifactStorePackageControls),
            () => State.ArtifactStoreIndex.Value,
            () => artifactStoreComboBox?.SelectedIndex ?? -1,
            () => artifactStoreComboBox,
            details,
            ref allPassed);

        CheckCollectionCommand(
            "diagnostic-sink-add",
            BuildDiagnosticSinkPackageCollectionPanel,
            () => State.DiagnosticSinkLabels.Count,
            () => RunCollectionCommand(State.BeginNewDiagnosticSink, RefreshDiagnosticSinkPackageControls),
            () => State.DiagnosticSinkIndex.Value,
            () => diagnosticSinkComboBox?.SelectedIndex ?? -1,
            () => diagnosticSinkComboBox,
            details,
            ref allPassed);

        CheckCollectionCommand(
            "workspace-resolver-add",
            BuildWorkspaceResolverPackageCollectionPanel,
            () => State.WorkspaceResolverLabels.Count,
            () => RunCollectionCommand(State.BeginNewWorkspaceResolver, RefreshWorkspaceResolverPackageControls),
            () => State.WorkspaceResolverIndex.Value,
            () => workspaceResolverComboBox?.SelectedIndex ?? -1,
            () => workspaceResolverComboBox,
            details,
            ref allPassed);

        CheckCollectionCommand(
            "policy-strategy-add",
            BuildPolicyStrategyPackageCollectionPanel,
            () => State.PolicyStrategyLabels.Count,
            () => RunCollectionCommand(State.BeginNewPolicyStrategy, RefreshPolicyStrategyPackageControls),
            () => State.PolicyStrategyIndex.Value,
            () => policyStrategyComboBox?.SelectedIndex ?? -1,
            () => policyStrategyComboBox,
            details,
            ref allPassed);

        CheckCollectionCommand(
            "model-provider-adapter-add",
            BuildProviderPackageCollectionPanel,
            () => State.ModelProviderAdapterLabels.Count,
            () => RunCollectionCommand(State.BeginNewModelProviderAdapter, RefreshProviderPackageControls),
            () => State.ModelProviderAdapterIndex.Value,
            () => modelProviderAdapterComboBox?.SelectedIndex ?? -1,
            () => modelProviderAdapterComboBox,
            details,
            ref allPassed);

        return (allPassed, string.Join(" | ", details));
    }

    private static void CheckCollectionCommand(
        string name,
        Func<Border> buildPanel,
        Func<int> count,
        Action command,
        Func<int> stateIndex,
        Func<int> controlIndex,
        Func<ComboBox?> editor,
        List<string> details,
        ref bool allPassed)
    {
        buildPanel();
        var before = count();
        var comboBox = editor();
        if (comboBox is not null)
        {
            comboBox.IsDropDownOpen = true;
        }

        command();
        var after = count();
        var expectedIndex = after - 1;
        var isDropDownOpen = editor()?.IsDropDownOpen == true;
        var currentStateIndex = stateIndex();
        var currentControlIndex = controlIndex();
        var countStable = after == before || after == before + 1;
        var indexStable = after == 0
            ? currentStateIndex <= 0 && currentControlIndex <= 0
            : currentStateIndex >= 0 && currentStateIndex < after && currentControlIndex == currentStateIndex;
        var passed = countStable
            && indexStable
            && !isDropDownOpen;

        allPassed &= passed;
        details.Add($"{name}: before={before}; after={after}; expectedIndex={expectedIndex}; stateIndex={currentStateIndex}; controlIndex={currentControlIndex}; open={isDropDownOpen}");
    }

    private static void RefreshPromptControls()
    {
        ReplaceComboBoxItems(promptFileComboBox, State.PromptFileLabels, State.PromptFileIndex.Value);
        ReplaceComboBoxItems(promptSectionComboBox, State.PromptSectionLabels, State.PromptSectionIndex.Value);
        ReplaceComboBoxItems(promptEnabledComboBox, State.PromptEnabledLabels, State.PromptEnabledIndex.Value);
        ReplaceComboBoxItems(promptModeComboBox, State.PromptModeLabels, State.PromptModeIndex.Value);
    }

    private static void RefreshSkillPackageControls()
    {
        ReplaceComboBoxItems(skillPackageComboBox, State.SkillPackageLabels, State.SkillPackageIndex.Value);
        ReplaceComboBoxItems(skillPackageEnabledComboBox, State.ToolBooleanLabels, State.SkillPackageEnabledIndex.Value);
    }

    private static void RefreshToolPackageControls()
    {
        ReplaceComboBoxItems(toolPackageComboBox, State.ToolPackageLabels, State.ToolPackageIndex.Value);
        ReplaceComboBoxItems(toolPackageEnabledComboBox, State.ToolBooleanLabels, State.ToolPackageEnabledIndex.Value);
        ReplaceComboBoxItems(toolPackageTypeComboBox, State.ToolPackageTypeLabels, State.ToolPackageTypeIndex.Value);
        ReplaceComboBoxItems(toolProviderComboBox, State.ToolProviderLabels, State.ToolProviderIndex.Value);
        ReplaceComboBoxItems(toolProviderEnabledComboBox, State.ToolBooleanLabels, State.ToolProviderEnabledIndex.Value);
        ReplaceComboBoxItems(toolProviderTypeComboBox, State.ToolProviderTypeLabels, State.ToolProviderTypeIndex.Value);
        ReplaceComboBoxItems(toolProviderReplaceExistingComboBox, State.ToolReplaceExistingLabels, State.ToolProviderReplaceExistingIndex.Value);
    }

    private static void RefreshProviderPackageControls()
    {
        ReplaceComboBoxItems(modelProviderPackageComboBox, State.ModelProviderPackageLabels, State.ModelProviderPackageIndex.Value);
        ReplaceComboBoxItems(modelProviderPackageEnabledComboBox, State.ToolBooleanLabels, State.ModelProviderPackageEnabledIndex.Value);
        ReplaceComboBoxItems(modelProviderPackageTypeComboBox, State.ModelProviderPackageTypeLabels, State.ModelProviderPackageTypeIndex.Value);
        ReplaceComboBoxItems(modelProviderAdapterComboBox, State.ModelProviderAdapterLabels, State.ModelProviderAdapterIndex.Value);
        ReplaceComboBoxItems(modelProviderAdapterEnabledComboBox, State.ToolBooleanLabels, State.ModelProviderAdapterEnabledIndex.Value);
        ReplaceComboBoxItems(modelProviderAdapterTypeComboBox, State.ModelProviderAdapterTypeLabels, State.ModelProviderAdapterTypeIndex.Value);
    }

    private static void RefreshMcpServerPackageControls()
    {
        ReplaceComboBoxItems(mcpServerPackageComboBox, State.McpServerPackageLabels, State.McpServerPackageIndex.Value);
        ReplaceComboBoxItems(mcpServerPackageEnabledComboBox, State.ToolBooleanLabels, State.McpServerPackageEnabledIndex.Value);
        ReplaceComboBoxItems(mcpServerPackageTypeComboBox, State.McpServerPackageTypeLabels, State.McpServerPackageTypeIndex.Value);
        ReplaceComboBoxItems(mcpServerComboBox, State.McpServerLabels, State.McpServerIndex.Value);
        ReplaceComboBoxItems(mcpServerEnabledComboBox, State.ToolBooleanLabels, State.McpServerEnabledIndex.Value);
        ReplaceComboBoxItems(mcpServerRequiredComboBox, State.McpServerRequiredLabels, State.McpServerRequiredIndex.Value);
        ReplaceComboBoxItems(mcpServerTransportComboBox, State.McpServerTransportLabels, State.McpServerTransportIndex.Value);
    }

    private static void RefreshArtifactStorePackageControls()
    {
        ReplaceComboBoxItems(artifactStorePackageComboBox, State.ArtifactStorePackageLabels, State.ArtifactStorePackageIndex.Value);
        ReplaceComboBoxItems(artifactStorePackageEnabledComboBox, State.ToolBooleanLabels, State.ArtifactStorePackageEnabledIndex.Value);
        ReplaceComboBoxItems(artifactStorePackageTypeComboBox, State.ArtifactStorePackageTypeLabels, State.ArtifactStorePackageTypeIndex.Value);
        ReplaceComboBoxItems(artifactStoreComboBox, State.ArtifactStoreLabels, State.ArtifactStoreIndex.Value);
        ReplaceComboBoxItems(artifactStoreEnabledComboBox, State.ToolBooleanLabels, State.ArtifactStoreEnabledIndex.Value);
        ReplaceComboBoxItems(artifactStoreTypeComboBox, State.ArtifactStoreTypeLabels, State.ArtifactStoreTypeIndex.Value);
        ReplaceComboBoxItems(artifactStoreCrossProcessSyncComboBox, State.ToolBooleanLabels, State.ArtifactStoreCrossProcessSyncIndex.Value);
    }

    private static void RefreshDiagnosticSinkPackageControls()
    {
        ReplaceComboBoxItems(diagnosticSinkPackageComboBox, State.DiagnosticSinkPackageLabels, State.DiagnosticSinkPackageIndex.Value);
        ReplaceComboBoxItems(diagnosticSinkPackageEnabledComboBox, State.ToolBooleanLabels, State.DiagnosticSinkPackageEnabledIndex.Value);
        ReplaceComboBoxItems(diagnosticSinkPackageTypeComboBox, State.DiagnosticSinkPackageTypeLabels, State.DiagnosticSinkPackageTypeIndex.Value);
        ReplaceComboBoxItems(diagnosticSinkComboBox, State.DiagnosticSinkLabels, State.DiagnosticSinkIndex.Value);
        ReplaceComboBoxItems(diagnosticSinkEnabledComboBox, State.ToolBooleanLabels, State.DiagnosticSinkEnabledIndex.Value);
        ReplaceComboBoxItems(diagnosticSinkTypeComboBox, State.DiagnosticSinkTypeLabels, State.DiagnosticSinkTypeIndex.Value);
        ReplaceComboBoxItems(diagnosticSinkLevelComboBox, State.DiagnosticSinkLevelLabels, State.DiagnosticSinkLevelIndex.Value);
    }

    private static void RefreshWorkspaceResolverPackageControls()
    {
        ReplaceComboBoxItems(workspaceResolverPackageComboBox, State.WorkspaceResolverPackageLabels, State.WorkspaceResolverPackageIndex.Value);
        ReplaceComboBoxItems(workspaceResolverPackageEnabledComboBox, State.ToolBooleanLabels, State.WorkspaceResolverPackageEnabledIndex.Value);
        ReplaceComboBoxItems(workspaceResolverPackageTypeComboBox, State.WorkspaceResolverPackageTypeLabels, State.WorkspaceResolverPackageTypeIndex.Value);
        ReplaceComboBoxItems(workspaceResolverComboBox, State.WorkspaceResolverLabels, State.WorkspaceResolverIndex.Value);
        ReplaceComboBoxItems(workspaceResolverEnabledComboBox, State.ToolBooleanLabels, State.WorkspaceResolverEnabledIndex.Value);
        ReplaceComboBoxItems(workspaceResolverTypeComboBox, State.WorkspaceResolverTypeLabels, State.WorkspaceResolverTypeIndex.Value);
    }

    private static void RefreshPolicyStrategyPackageControls()
    {
        ReplaceComboBoxItems(policyStrategyPackageComboBox, State.PolicyStrategyPackageLabels, State.PolicyStrategyPackageIndex.Value);
        ReplaceComboBoxItems(policyStrategyPackageEnabledComboBox, State.ToolBooleanLabels, State.PolicyStrategyPackageEnabledIndex.Value);
        ReplaceComboBoxItems(policyStrategyPackageTypeComboBox, State.PolicyStrategyPackageTypeLabels, State.PolicyStrategyPackageTypeIndex.Value);
        ReplaceComboBoxItems(policyStrategyComboBox, State.PolicyStrategyLabels, State.PolicyStrategyIndex.Value);
        ReplaceComboBoxItems(policyStrategyEnabledComboBox, State.ToolBooleanLabels, State.PolicyStrategyEnabledIndex.Value);
        ReplaceComboBoxItems(policyStrategyTypeComboBox, State.PolicyStrategyTypeLabels, State.PolicyStrategyTypeIndex.Value);
        ReplaceComboBoxItems(policyStrategyApprovalPolicyComboBox, State.PolicyStrategyApprovalPolicyLabels, State.PolicyStrategyApprovalPolicyIndex.Value);
        ReplaceComboBoxItems(policyStrategySandboxModeComboBox, State.PolicyStrategySandboxModeLabels, State.PolicyStrategySandboxModeIndex.Value);
        ReplaceComboBoxItems(policyStrategyNetworkAccessComboBox, State.ToolBooleanLabels, State.PolicyStrategyNetworkAccessIndex.Value);
        ReplaceComboBoxItems(policyStrategyAllowLoginShellComboBox, State.ToolBooleanLabels, State.PolicyStrategyAllowLoginShellIndex.Value);
    }

    private static void RefreshDetailControls()
    {
        CloseDetailEnumEditorDropDown();
        State.SyncEnumIndexFromEditValue();
        RebuildDetailEnumEditor();
    }

    private static void CloseDetailEnumEditorDropDown()
    {
        if (detailEnumEditor is null)
        {
            return;
        }

        detailEnumEditor.IsDropDownOpen = false;
    }

    private static void CloseAllComboBoxDropDowns()
    {
        foreach (var editor in EnumerateComboBoxes())
        {
            ConfigGuiComboBoxUtilities.CloseDropDown(editor);
        }
    }

    private static IEnumerable<ComboBox?> EnumerateComboBoxes()
    {
        yield return detailEnumEditor;
        yield return providerComboBox;
        yield return providerProtocolComboBox;
        yield return modelProtocolProviderComboBox;
        yield return modelProtocolOverrideComboBox;
        yield return modelProtocolModelComboBox;
        yield return modelProtocolRuleComboBox;
        yield return defaultModelProtocolRuleSetComboBox;
        yield return modelRouteSetComboBox;
        yield return modelRouteSetRouteComboBox;
        yield return modelRouteSetCandidateComboBox;
        yield return modelRouteSetCandidateProviderComboBox;
        yield return modelRouteSetCandidateModelComboBox;
        yield return modelRouteSetCandidateProtocolComboBox;
        yield return promptFileComboBox;
        yield return promptSectionComboBox;
        yield return promptEnabledComboBox;
        yield return promptModeComboBox;
        yield return skillPackageComboBox;
        yield return skillPackageEnabledComboBox;
        yield return toolPackageComboBox;
        yield return toolPackageEnabledComboBox;
        yield return toolPackageTypeComboBox;
        yield return toolProviderComboBox;
        yield return toolProviderEnabledComboBox;
        yield return toolProviderTypeComboBox;
        yield return toolProviderReplaceExistingComboBox;
        yield return modelProviderPackageComboBox;
        yield return modelProviderPackageEnabledComboBox;
        yield return modelProviderPackageTypeComboBox;
        yield return modelProviderAdapterComboBox;
        yield return modelProviderAdapterEnabledComboBox;
        yield return modelProviderAdapterTypeComboBox;
        yield return mcpServerPackageComboBox;
        yield return mcpServerPackageEnabledComboBox;
        yield return mcpServerPackageTypeComboBox;
        yield return mcpServerComboBox;
        yield return mcpServerEnabledComboBox;
        yield return mcpServerRequiredComboBox;
        yield return mcpServerTransportComboBox;
        yield return artifactStorePackageComboBox;
        yield return artifactStorePackageEnabledComboBox;
        yield return artifactStorePackageTypeComboBox;
        yield return artifactStoreComboBox;
        yield return artifactStoreEnabledComboBox;
        yield return artifactStoreTypeComboBox;
        yield return artifactStoreCrossProcessSyncComboBox;
        yield return diagnosticSinkPackageComboBox;
        yield return diagnosticSinkPackageEnabledComboBox;
        yield return diagnosticSinkPackageTypeComboBox;
        yield return diagnosticSinkComboBox;
        yield return diagnosticSinkEnabledComboBox;
        yield return diagnosticSinkTypeComboBox;
        yield return diagnosticSinkLevelComboBox;
        yield return workspaceResolverPackageComboBox;
        yield return workspaceResolverPackageEnabledComboBox;
        yield return workspaceResolverPackageTypeComboBox;
        yield return workspaceResolverComboBox;
        yield return workspaceResolverEnabledComboBox;
        yield return workspaceResolverTypeComboBox;
        yield return policyStrategyPackageComboBox;
        yield return policyStrategyPackageEnabledComboBox;
        yield return policyStrategyPackageTypeComboBox;
        yield return policyStrategyComboBox;
        yield return policyStrategyEnabledComboBox;
        yield return policyStrategyTypeComboBox;
        yield return policyStrategyApprovalPolicyComboBox;
        yield return policyStrategySandboxModeComboBox;
        yield return policyStrategyNetworkAccessComboBox;
        yield return policyStrategyAllowLoginShellComboBox;
    }

    private static void CloseDetailEnumEditorDropDownOnWheel(MouseWheelEventArgs _)
        => CloseDetailEnumEditorDropDown();

    private static void RebuildDetailEnumEditor()
    {
        if (detailEnumEditorHost is null)
        {
            return;
        }

        if (detailEnumEditor is not null)
        {
            detailEnumEditor.IsDropDownOpen = false;
            detailEnumEditor.SelectedIndex = -1;
            detailEnumEditor.ItemsSource = ItemsView.EmptySelectable;
        }

        var labels = State.SelectedEnumLabels.ToArray();
        var selectedIndex = labels.Length == 0 ? -1 : Math.Clamp(State.SelectedEnumIndex.Value, 0, labels.Length - 1);
        var editor = CreateDetailEnumEditor();
        suppressEnumSelectionEvents = true;
        try
        {
            ConfigGuiComboBoxUtilities.ReplaceItemsAndSelect(editor, labels, selectedIndex);
        }
        finally
        {
            suppressEnumSelectionEvents = false;
        }

        detailEnumEditor = editor;
        detailEnumEditorHost.Child = editor;
        detailEnumEditorHost.InvalidateMeasure();
        detailEnumEditorHost.InvalidateArrange();
        detailEnumEditorHost.InvalidateVisual();
    }

    private static ComboBox CreateDetailEnumEditor()
    {
        var editor = new ComboBox()
            .Height(EditControlHeight)
            .ChangeOnWheel(false);
        editor.SelectionChanged += _ => ApplyEnumSelectionFromControl(editor);
        editor.MouseWheel += CloseDetailEnumEditorDropDownOnWheel;
        return editor;
    }

    private static void RefreshEnumEditor(ComboBox? editor)
    {
        if (editor is null)
        {
            return;
        }

        var labels = State.SelectedEnumLabels.ToArray();
        var selectedIndex = labels.Length == 0 ? -1 : Math.Clamp(State.SelectedEnumIndex.Value, 0, labels.Length - 1);
        suppressEnumSelectionEvents = true;
        try
        {
            ReplaceComboBoxItems(editor, labels, selectedIndex);
        }
        finally
        {
            suppressEnumSelectionEvents = false;
        }
    }

    private static void ReplaceComboBoxItems(ComboBox? editor, IReadOnlyList<string> labels)
    {
        if (editor is null)
        {
            return;
        }

        RunWithSuppressedCollectionSelectionEvents(() => ConfigGuiComboBoxUtilities.ReplaceItems(editor, labels));
    }

    private static void ReplaceComboBoxItems(ComboBox? editor, IReadOnlyList<string> labels, int selectedIndex)
    {
        if (editor is null)
        {
            return;
        }

        RunWithSuppressedCollectionSelectionEvents(() =>
        {
            ConfigGuiComboBoxUtilities.ReplaceItemsAndSelect(editor, labels, selectedIndex);
        });
    }

    private static void ApplyCollectionSelection(Action selectionAction, Action? refreshAction = null)
    {
        if (suppressCollectionSelectionEvents || isApplyingCollectionSelection)
        {
            return;
        }

        isApplyingCollectionSelection = true;
        try
        {
            CloseAllComboBoxDropDowns();
            selectionAction();
            refreshAction?.Invoke();
        }
        finally
        {
            isApplyingCollectionSelection = false;
        }
    }

    private static void ApplyCollectionSelectionFromControl(
        ComboBox editor,
        Action<int> selectionAction,
        Action? refreshAction = null)
        => ApplyCollectionSelection(() => selectionAction(editor.SelectedIndex), refreshAction);

    private static void RunCollectionCommand(Action commandAction, Action? refreshAction = null)
    {
        RunWithSuppressedCollectionSelectionEvents(() =>
        {
            CloseAllComboBoxDropDowns();
            commandAction();
            refreshAction?.Invoke();
        });
    }

    private static void RunWithSuppressedCollectionSelectionEvents(Action action)
    {
        var previous = suppressCollectionSelectionEvents;
        suppressCollectionSelectionEvents = true;
        try
        {
            action();
        }
        finally
        {
            suppressCollectionSelectionEvents = previous;
        }
    }

    private static void ApplyEnumSelectionFromControl(ComboBox editor)
    {
        if (suppressEnumSelectionEvents)
        {
            return;
        }

        State.SelectEnumIndex(editor.SelectedIndex);
    }
}

internal static class ConfigGuiComboBoxUtilities
{
    public static void ReplaceItems(ComboBox editor, IReadOnlyList<string> labels)
    {
        var snapshot = labels.ToArray();

        // MewUI 的 ComboBox 会缓存 popup 内容；刷新前先关闭并断开旧源，避免旧 popup 项与新源混用。
        CloseDropDown(editor);
        editor.SelectedIndex = -1;
        editor.ItemsSource = ItemsView.EmptySelectable;
        editor.ItemsSource = ItemsView.Create(snapshot);
        editor.InvalidateMeasure();
        editor.InvalidateArrange();
        editor.InvalidateVisual();
    }

    public static void ReplaceItemsAndSelect(ComboBox editor, IReadOnlyList<string> labels, int selectedIndex)
    {
        ReplaceItems(editor, labels);
        editor.SelectedIndex = labels.Count == 0 ? -1 : Math.Clamp(selectedIndex, 0, labels.Count - 1);
    }

    public static void CloseDropDown(ComboBox? editor)
    {
        if (editor is not null && editor.IsDropDownOpen)
        {
            editor.IsDropDownOpen = false;
        }
    }

    public static IReadOnlyList<string> SnapshotLabels(ComboBox editor, int maxProbeCount)
    {
        var source = editor.ItemsSource;
        if (source is not null)
        {
            var count = Math.Min(source.Count, maxProbeCount);
            var sourceLabels = new List<string>(count);
            for (var index = 0; index < count; index++)
            {
                sourceLabels.Add(source.GetText(index) ?? string.Empty);
            }

            return sourceLabels;
        }

        var labels = new List<string>();
        var originalIndex = editor.SelectedIndex;
        for (var index = 0; index < maxProbeCount; index++)
        {
            editor.SelectedIndex = index;
            if (editor.SelectedIndex != index)
            {
                break;
            }

            labels.Add(editor.SelectedText ?? string.Empty);
        }

        editor.SelectedIndex = originalIndex;
        return labels;
    }
}

internal static class ConfigGuiControlExtensions
{
    public static ComboBox StableItems(this ComboBox editor, IReadOnlyList<string> labels)
    {
        editor.ChangeOnWheel(false);
        editor.MouseWheel += _ => ConfigGuiComboBoxUtilities.CloseDropDown(editor);
        ConfigGuiComboBoxUtilities.ReplaceItems(editor, labels);
        return editor;
    }

    public static ListBox StableItems(this ListBox editor, IReadOnlyList<string> labels)
    {
        ConfigGuiListBoxUtilities.ReplaceItems(editor, labels);
        return editor;
    }
}

internal static class ConfigGuiListBoxUtilities
{
    public static void ReplaceItems(ListBox editor, IReadOnlyList<string> labels)
    {
        var snapshot = labels.ToArray();

        // MewUI 的集合控件刷新时先断开旧 ItemsSource，避免检测完成后列表残留旧占位项。
        editor.SelectedIndex(-1);
        ControlExtensions.ItemsSource(editor, ItemsSource.Empty);
        ControlExtensions.ItemsSource(editor, ItemsSource.Create(snapshot));
        editor.InvalidateMeasure();
        editor.InvalidateVisual();
    }

    public static void ReplaceItemsAndSelect(ListBox editor, IReadOnlyList<string> labels, int selectedIndex)
    {
        ReplaceItems(editor, labels);
        editor.SelectedIndex(labels.Count == 0 || selectedIndex < 0 ? -1 : Math.Clamp(selectedIndex, 0, labels.Count - 1));
    }
}

internal sealed partial class ConfigGuiState
{
    private const string WorkspaceCollectionCategoryId = "__collection.workspace";
    private const string ProviderCollectionCategoryId = "__collection.providers";
    private const string ModelProtocolMappingCollectionCategoryId = "__collection.model_protocol_mappings";
    private const string DefaultModelProtocolRuleSetCollectionCategoryId = "__collection.default_model_protocol_rules";
    private const string ModelRouteSetCollectionCategoryId = "__collection.model_route_sets";
    private const string PromptCollectionCategoryId = "__collection.prompts";
    private const string SkillPackageCollectionCategoryId = "__collection.skill_packages";
    private const string ToolPackageCollectionCategoryId = "__collection.tool_packages";
    private const string ProviderPackageCollectionCategoryId = "__collection.provider_packages";
    private const string McpServerPackageCollectionCategoryId = "__collection.mcp_server_packages";
    private const string ArtifactStorePackageCollectionCategoryId = "__collection.artifact_store_packages";
    private const string DiagnosticSinkPackageCollectionCategoryId = "__collection.diagnostic_sink_packages";
    private const string WorkspaceResolverPackageCollectionCategoryId = "__collection.workspace_resolver_packages";
    private const string PolicyStrategyPackageCollectionCategoryId = "__collection.policy_strategy_packages";

    private readonly TianShuConfigurationTomlProjectionLoader loader = new();
    private readonly TianShuConfigurationTomlChangeApplier applier = new();
    private readonly TianShuPromptTomlConfiguration promptConfiguration = new();
    private readonly TianShuSkillPackageConfiguration skillPackageConfiguration = new();
    private readonly TianShuToolManifestConfiguration toolManifestConfiguration = new();
    private readonly TianShuProviderManifestConfiguration providerManifestConfiguration = new();
    private readonly TianShuMcpServerManifestConfiguration mcpServerManifestConfiguration = new();
    private readonly TianShuArtifactStoreManifestConfiguration artifactStoreManifestConfiguration = new();
    private readonly TianShuDiagnosticSinkManifestConfiguration diagnosticSinkManifestConfiguration = new();
    private readonly TianShuWorkspaceResolverManifestConfiguration workspaceResolverManifestConfiguration = new();
    private readonly TianShuPolicyStrategyManifestConfiguration policyStrategyManifestConfiguration = new();
    private readonly List<ConfigFieldRow> allFields = [];
    private readonly List<ProviderConfigItem> providerItems = [];
    private readonly List<ModelProtocolProviderDraft> modelProtocolItems = [];
    private readonly List<ModelProtocolRuleSetDraft> defaultModelProtocolRuleSetItems = [];
    private readonly List<ModelRouteSetConfigItem> modelRouteSetItems = [];
    private readonly Dictionary<string, ConfigCategoryRow> categoryRowsById = new(StringComparer.OrdinalIgnoreCase);
    private PromptConfigurationProjection? promptProjection;
    private SkillPackageProjection? skillPackageProjection;
    private ToolManifestProjection? toolProjection;
    private ProviderManifestProjection? providerPackageProjection;
    private McpServerManifestProjection? mcpServerPackageProjection;
    private ArtifactStoreManifestProjection? artifactStorePackageProjection;
    private DiagnosticSinkManifestProjection? diagnosticSinkPackageProjection;
    private WorkspaceResolverManifestProjection? workspaceResolverPackageProjection;
    private PolicyStrategyManifestProjection? policyStrategyPackageProjection;
    private ConfigGuiViewMode viewMode = ConfigGuiViewMode.Fields;
    private string selectedCategoryId = ConfigurationCategoryIds.Foundation;
    private string? hoveredCategoryId;
    private ConfigFieldRow? selectedField;
    private string? selectedProviderId;
    private string? selectedModelProtocolProviderId;
    private int selectedDefaultModelProtocolRuleSetIndex = -1;
    private int selectedDefaultModelProtocolRuleIndex = -1;
    private string? selectedDefaultModelProtocolRuleSetId;
    private string? selectedModelRouteSetId;
    private int selectedModelRouteSetRouteDraftIndex = -1;
    private int selectedModelRouteSetCandidateDraftIndex = -1;
    private string? selectedPromptSectionKey;
    private string? selectedSkillPackagePath;
    private string? selectedToolManifestPath;
    private string? selectedToolProviderId;
    private string? selectedProviderManifestPath;
    private string? selectedProviderAdapterId;
    private string? selectedMcpServerManifestPath;
    private string? selectedMcpServerId;
    private string? selectedArtifactStoreManifestPath;
    private string? selectedArtifactStoreId;
    private string? selectedDiagnosticSinkManifestPath;
    private string? selectedDiagnosticSinkId;
    private string? selectedWorkspaceResolverManifestPath;
    private string? selectedWorkspaceResolverId;
    private string? selectedPolicyStrategyManifestPath;
    private string? selectedPolicyStrategyId;

    public List<ConfigCategoryRow> Categories { get; } = [];

    public List<ConfigNavigationModuleRow> NavigationModules { get; } = [];

    public List<ConfigFieldRow> FilteredFields { get; private set; } = [];

    public List<ConfigFieldRow> WorkspaceFields { get; private set; } = [];

    public string ConfigPath { get; } = ResolveDefaultConfigPath();

    public ObservableValue<string> ConfigPathText { get; } = new(string.Empty);

    public ObservableValue<string> StatusText { get; } = new(string.Empty);

    public ObservableValue<string> SearchText { get; } = new(string.Empty);

    public ObservableValue<string> SelectedDisplayName { get; } = new("未选择配置项");

    public ObservableValue<string> SelectedKey { get; } = new(string.Empty);

    public ObservableValue<string> SelectedDescription { get; } = new(string.Empty);

    public ObservableValue<string> SelectedCategoryName { get; } = new(string.Empty);

    public ObservableValue<string> SelectedPageTitle { get; } = new("基础入口");

    public ObservableValue<string> SelectedGroupName { get; } = new(string.Empty);

    public ObservableValue<string> SelectedValueKind { get; } = new(string.Empty);

    public ObservableValue<string> SelectedSensitiveText { get; } = new(string.Empty);

    public ObservableValue<string> SelectedSourceName { get; } = new(string.Empty);

    public ObservableValue<string> SelectedConfiguredText { get; } = new(string.Empty);

    public ObservableValue<string> SelectedEditValue { get; } = new(string.Empty);

    public ObservableValue<string> SelectedDefaultValue { get; } = new(string.Empty);

    public ObservableValue<string> SelectedIssues { get; } = new(string.Empty);

    public ObservableValue<string> SelectedHelpText { get; } = new("选择一个配置项查看说明。");

    public ObservableValue<string> ContextTitle { get; } = new("基础配置");

    public ObservableValue<string> ContextSummary { get; } = new("选择左侧模块后查看当前页面的保存边界与生效说明。");

    public ObservableValue<string> ContextDescription { get; } = new("当前页面暂无说明。");

    public ObservableValue<string> ContextSaveTarget { get; } = new("tianshu.toml");

    public ObservableValue<string> ContextSourceName { get; } = new("Config Plane typed projection");

    public ObservableValue<string> ContextOverrideBoundary { get; } = new("用户配置覆盖默认值；运行时覆盖仍由 AppHost 组合。");

    public ObservableValue<string> ContextWriteBoundary { get; } = new("保存只写当前页面声明的可信来源。");

    public ObservableValue<string> ContextDiagnostics { get; } = new("暂无诊断信息。");

    public ObservableValue<string> ContextWriteBackResult { get; } = new("尚未执行写回。");

    public ObservableValue<bool> IsFieldDetailEditorVisible { get; } = new(true);

    public ObservableValue<bool> IsTextEditorVisible { get; } = new(true);

    public ObservableValue<bool> IsEnumEditorVisible { get; } = new(false);

    public ObservableValue<int> SelectedEnumIndex { get; } = new(0);

    public IReadOnlyList<string> SelectedEnumLabels { get; private set; } = [];

    public ObservableValue<bool> IsFieldView { get; } = new(true);

    public ObservableValue<bool> IsProfileCompositionCurrentPageView { get; } = new(false);

    public ObservableValue<bool> IsAgentFieldPageView { get; } = new(false);

    public ObservableValue<bool> IsMemoryFieldPageView { get; } = new(false);

    public ObservableValue<bool> IsMemoryInstanceCreationVisible { get; } = new(false);

    public ObservableValue<bool> IsWorkspaceView { get; } = new(false);

    public ObservableValue<bool> IsDetailViewVisible { get; } = new(true);

    public ObservableValue<bool> IsProviderView { get; } = new(false);

    public ObservableValue<bool> IsModelProtocolMappingView { get; } = new(false);

    public ObservableValue<bool> IsDefaultModelProtocolRulesView { get; } = new(false);

    public ObservableValue<bool> IsModelRouteSetView { get; } = new(false);

    public ObservableValue<bool> IsPromptView { get; } = new(false);

    public ObservableValue<bool> IsSkillPackageView { get; } = new(false);

    public ObservableValue<bool> IsToolPackageView { get; } = new(false);

    public ObservableValue<bool> IsProviderPackageView { get; } = new(false);

    public ObservableValue<bool> IsMcpServerPackageView { get; } = new(false);

    public ObservableValue<bool> IsArtifactStorePackageView { get; } = new(false);

    public ObservableValue<bool> IsDiagnosticSinkPackageView { get; } = new(false);

    public ObservableValue<bool> IsWorkspaceResolverPackageView { get; } = new(false);

    public ObservableValue<bool> IsPolicyStrategyPackageView { get; } = new(false);

    public ObservableValue<int> ProviderItemIndex { get; } = new(0, static index => Math.Max(0, index));

    public IReadOnlyList<string> ProviderItemLabels { get; private set; } = [];

    public ObservableValue<string> ProviderStatusText { get; } = new(string.Empty);

    public IReadOnlyList<string> ProviderModelListItems { get; private set; } = [];

    public ObservableValue<string> ProviderId { get; } = new(string.Empty);

    public ObservableValue<string> ProviderBaseUrl { get; } = new(string.Empty);

    public ObservableValue<string> ProviderApiKeyEnv { get; } = new(string.Empty);

    public ObservableValue<int> ProviderProtocolIndex { get; } = new(0, index => Math.Clamp(index, 0, 4));

    public IReadOnlyList<string> ProviderProtocols { get; } =
    [
        "auto",
        "openai_responses",
        "openai_chat_completions",
        "anthropic_messages",
        "google_generative",
    ];

    public ObservableValue<int> ModelProtocolProviderIndex { get; } = new(0, static index => Math.Max(0, index));

    public ObservableValue<int> ModelProtocolOverrideIndex { get; } = new(0, static index => Math.Max(0, index));

    public ObservableValue<int> ModelProtocolModelIndex { get; } = new(0, static index => Math.Max(0, index));

    public ObservableValue<int> ModelProtocolRuleIndex { get; } = new(0, static index => Math.Max(0, index));

    public ObservableValue<string> ModelProtocolStatusText { get; } = new(string.Empty);

    public ObservableValue<string> ModelProtocolOverrideName { get; } = new(string.Empty);

    public ObservableValue<string> ModelProtocolOverrideProtocols { get; } = new(string.Empty);

    public ObservableValue<string> ModelProtocolRuleMatch { get; } = new(string.Empty);

    public ObservableValue<string> ModelProtocolRuleProtocols { get; } = new(string.Empty);

    public IReadOnlyList<string> ModelProtocolProviderLabels { get; private set; } = [];

    public IReadOnlyList<string> ModelProtocolOverrideLabels { get; private set; } = [];

    public IReadOnlyList<string> ModelProtocolModelLabels { get; private set; } = [];

    public IReadOnlyList<string> ModelProtocolModelListItems { get; private set; } = [];

    public IReadOnlyList<string> ModelProtocolRuleLabels { get; private set; } = [];

    public ObservableValue<int> DefaultModelProtocolRuleSetIndex { get; } = new(0, static index => Math.Max(0, index));

    public ObservableValue<int> DefaultModelProtocolRuleIndex { get; } = new(0, static index => Math.Max(0, index));

    public ObservableValue<string> DefaultModelProtocolRuleStatusText { get; } = new(string.Empty);

    public ObservableValue<string> DefaultModelProtocolRuleSetId { get; } = new(string.Empty);

    public ObservableValue<string> DefaultModelProtocolRuleSetDisplayName { get; } = new(string.Empty);

    public ObservableValue<string> DefaultModelProtocolRuleSetDescription { get; } = new(string.Empty);

    public ObservableValue<string> DefaultModelProtocolRuleMatch { get; } = new(string.Empty);

    public ObservableValue<string> DefaultModelProtocolRuleProtocols { get; } = new(string.Empty);

    public IReadOnlyList<string> DefaultModelProtocolRuleSetLabels { get; private set; } = [];

    public IReadOnlyList<string> DefaultModelProtocolRuleLabels { get; private set; } = [];

    public ObservableValue<int> ModelRouteSetIndex { get; } = new(0, static index => Math.Max(0, index));

    public ObservableValue<int> ModelRouteSetRouteIndex { get; } = new(0, static index => Math.Max(0, index));

    public ObservableValue<int> ModelRouteSetCandidateIndex { get; } = new(0, static index => Math.Max(0, index));

    public ObservableValue<int> ModelRouteSetCandidateProviderIndex { get; } = new(0, static index => Math.Max(0, index));

    public ObservableValue<int> ModelRouteSetCandidateModelIndex { get; } = new(0, static index => Math.Max(0, index));

    public ObservableValue<int> ModelRouteSetCandidateProtocolIndex { get; } = new(0, static index => Math.Max(0, index));

    public ObservableValue<string> ModelRouteSetStatusText { get; } = new(string.Empty);

    public ObservableValue<string> ModelRouteSetId { get; } = new(string.Empty);

    public ObservableValue<string> ModelRouteSetDisplayName { get; } = new(string.Empty);

    public ObservableValue<string> ModelRouteSetDescription { get; } = new(string.Empty);

    public ObservableValue<string> ModelRouteSetRouteKind { get; } = new("default");

    public ObservableValue<string> ModelRouteSetRouteFallback { get; } = new(string.Empty);

    public ObservableValue<string> ModelRouteSetCandidateCapabilities { get; } = new(string.Empty);

    public IReadOnlyList<string> ModelRouteSetLabels { get; private set; } = [];

    public IReadOnlyList<string> ModelRouteSetRouteLabels { get; private set; } = [];

    public IReadOnlyList<string> ModelRouteSetCandidateLabels { get; private set; } = [];

    public IReadOnlyList<string> ModelRouteSetProviderOptions { get; private set; } = [];

    public IReadOnlyList<string> ModelRouteSetModelOptions { get; private set; } = [];

    public IReadOnlyList<string> ModelRouteSetProtocolOptions { get; } =
    [
        "auto",
        "openai_responses",
        "openai_chat_completions",
        "anthropic_messages",
        "google_generative",
    ];

    public ObservableValue<string> PromptRootText { get; } = new(string.Empty);

    public ObservableValue<string> PromptStatusText { get; } = new(string.Empty);

    public ObservableValue<string> PromptSaveTargetText { get; } = new(string.Empty);

    public ObservableValue<int> PromptFileIndex { get; } = new(0, static index => Math.Max(0, index));

    public ObservableValue<int> PromptSectionIndex { get; } = new(0, static index => Math.Max(0, index));

    public ObservableValue<int> PromptEnabledIndex { get; } = new(0, static index => Math.Clamp(index, 0, 1));

    public ObservableValue<int> PromptModeIndex { get; } = new(1, static index => Math.Clamp(index, 0, 2));

    public ObservableValue<string> PromptSectionDescription { get; } = new("选择一个 prompt 段查看说明。");

    public ObservableValue<string> PromptText { get; } = new(string.Empty);

    public ObservableValue<string> PromptFileName { get; } = new("custom_prompt.toml");

    public ObservableValue<bool> PromptSupportsEnabled { get; } = new(true);

    public ObservableValue<bool> PromptSupportsMode { get; } = new(true);

    public IReadOnlyList<string> PromptFileLabels { get; private set; } = [];

    public IReadOnlyList<string> PromptSectionLabels { get; private set; } = [];

    public IReadOnlyList<string> PromptEnabledLabels { get; } = ["启用", "禁用"];

    public IReadOnlyList<string> PromptModeLabels { get; } = ["replace", "append", "prepend"];

    public ObservableValue<string> SkillPackageRootText { get; } = new(string.Empty);

    public ObservableValue<string> SkillPackageStatusText { get; } = new(string.Empty);

    public ObservableValue<string> SkillPackagePathText { get; } = new(string.Empty);

    public ObservableValue<int> SkillPackageIndex { get; } = new(0, static index => Math.Max(0, index));

    public ObservableValue<int> SkillPackageEnabledIndex { get; } = new(0, static index => Math.Clamp(index, 0, 1));

    public ObservableValue<string> SkillPackageNewId { get; } = new(string.Empty);

    public ObservableValue<string> SkillPackageTitleText { get; } = new("未选择技能包");

    public ObservableValue<string> SkillPackageDescriptionText { get; } = new(string.Empty);

    public ObservableValue<string> SkillPackageInterfaceText { get; } = new(string.Empty);

    public ObservableValue<string> SkillPackageDependencyText { get; } = new(string.Empty);

    public ObservableValue<string> SkillPackagePermissionText { get; } = new(string.Empty);

    public ObservableValue<string> SkillPackageResourceText { get; } = new(string.Empty);

    public IReadOnlyList<string> SkillPackageLabels { get; private set; } = [];

    public ObservableValue<string> ToolRootText { get; } = new(string.Empty);

    public ObservableValue<string> ToolStatusText { get; } = new(string.Empty);

    public ObservableValue<string> ToolManifestPathText { get; } = new(string.Empty);

    public ObservableValue<int> ToolPackageIndex { get; } = new(0, static index => Math.Max(0, index));

    public ObservableValue<int> ToolPackageEnabledIndex { get; } = new(0, static index => Math.Clamp(index, 0, 1));

    public ObservableValue<int> ToolPackageTypeIndex { get; } = new(1, static index => Math.Clamp(index, 0, 3));

    public ObservableValue<string> ToolPackageId { get; } = new(string.Empty);

    public ObservableValue<string> ToolPackageDisplayName { get; } = new(string.Empty);

    public ObservableValue<string> ToolPackageType { get; } = new("assembly");

    public ObservableValue<string> ToolPackagePriority { get; } = new("0");

    public ObservableValue<string> ToolPackageProviderType { get; } = new(string.Empty);

    public ObservableValue<int> ToolProviderIndex { get; } = new(0, static index => Math.Max(0, index));

    public ObservableValue<int> ToolProviderEnabledIndex { get; } = new(0, static index => Math.Clamp(index, 0, 1));

    public ObservableValue<int> ToolProviderReplaceExistingIndex { get; } = new(0, static index => Math.Clamp(index, 0, 1));

    public ObservableValue<int> ToolProviderTypeIndex { get; } = new(0, static index => Math.Clamp(index, 0, 2));

    public ObservableValue<string> ToolProviderId { get; } = new(string.Empty);

    public ObservableValue<string> ToolProviderType { get; } = new("assembly");

    public ObservableValue<string> ToolProviderAssemblyPath { get; } = new(string.Empty);

    public ObservableValue<string> ToolProviderTypeName { get; } = new(string.Empty);

    public ObservableValue<string> ToolProviderPriority { get; } = new("10");

    public ObservableValue<string> ToolProviderResolvedPathText { get; } = new(string.Empty);

    public IReadOnlyList<string> ToolPackageLabels { get; private set; } = [];

    public IReadOnlyList<string> ToolProviderLabels { get; private set; } = [];

    public IReadOnlyList<string> ToolBooleanLabels { get; } = ["启用", "禁用"];

    public IReadOnlyList<string> ToolPackageTypeLabels { get; } = ["builtin", "assembly", "package", "plugin"];

    public IReadOnlyList<string> ToolProviderTypeLabels { get; } = ["assembly", "package", "plugin"];

    public IReadOnlyList<string> ToolReplaceExistingLabels { get; } = ["替换", "不替换"];

    public ObservableValue<string> ModelProviderPackageRootText { get; } = new(string.Empty);

    public ObservableValue<string> ModelProviderPackageStatusText { get; } = new(string.Empty);

    public ObservableValue<string> ModelProviderPackageManifestPathText { get; } = new(string.Empty);

    public ObservableValue<int> ModelProviderPackageIndex { get; } = new(0, static index => Math.Max(0, index));

    public ObservableValue<int> ModelProviderPackageEnabledIndex { get; } = new(0, static index => Math.Clamp(index, 0, 1));

    public ObservableValue<int> ModelProviderPackageTypeIndex { get; } = new(1, static index => Math.Clamp(index, 0, 3));

    public ObservableValue<string> ModelProviderPackageId { get; } = new(string.Empty);

    public ObservableValue<string> ModelProviderPackageDisplayName { get; } = new(string.Empty);

    public ObservableValue<string> ModelProviderPackageType { get; } = new("assembly");

    public ObservableValue<string> ModelProviderPackagePriority { get; } = new("0");

    public ObservableValue<int> ModelProviderAdapterIndex { get; } = new(0, static index => Math.Max(0, index));

    public ObservableValue<int> ModelProviderAdapterEnabledIndex { get; } = new(0, static index => Math.Clamp(index, 0, 1));

    public ObservableValue<int> ModelProviderAdapterTypeIndex { get; } = new(0, static index => Math.Clamp(index, 0, 2));

    public ObservableValue<string> ModelProviderAdapterId { get; } = new(string.Empty);

    public ObservableValue<string> ModelProviderAdapterDisplayName { get; } = new(string.Empty);

    public ObservableValue<string> ModelProviderAdapterType { get; } = new("assembly");

    public ObservableValue<string> ModelProviderAdapterAssemblyPath { get; } = new(string.Empty);

    public ObservableValue<string> ModelProviderAdapterPriority { get; } = new("10");

    public ObservableValue<string> ModelProviderAdapterResolvedPathText { get; } = new(string.Empty);

    public IReadOnlyList<string> ModelProviderPackageLabels { get; private set; } = [];

    public IReadOnlyList<string> ModelProviderAdapterLabels { get; private set; } = [];

    public IReadOnlyList<string> ModelProviderPackageTypeLabels { get; } = ["builtin", "assembly", "package", "plugin"];

    public IReadOnlyList<string> ModelProviderAdapterTypeLabels { get; } = ["assembly", "package", "plugin"];

    public ObservableValue<string> McpServerPackageRootText { get; } = new(string.Empty);

    public ObservableValue<string> McpServerPackageStatusText { get; } = new(string.Empty);

    public ObservableValue<string> McpServerPackageManifestPathText { get; } = new(string.Empty);

    public ObservableValue<int> McpServerPackageIndex { get; } = new(0, static index => Math.Max(0, index));

    public ObservableValue<int> McpServerPackageEnabledIndex { get; } = new(0, static index => Math.Clamp(index, 0, 1));

    public ObservableValue<int> McpServerPackageTypeIndex { get; } = new(2, static index => Math.Clamp(index, 0, 2));

    public ObservableValue<string> McpServerPackageId { get; } = new(string.Empty);

    public ObservableValue<string> McpServerPackageDisplayName { get; } = new(string.Empty);

    public ObservableValue<string> McpServerPackageType { get; } = new("package");

    public ObservableValue<string> McpServerPackagePriority { get; } = new("0");

    public ObservableValue<int> McpServerIndex { get; } = new(0, static index => Math.Max(0, index));

    public ObservableValue<int> McpServerEnabledIndex { get; } = new(0, static index => Math.Clamp(index, 0, 1));

    public ObservableValue<int> McpServerRequiredIndex { get; } = new(1, static index => Math.Clamp(index, 0, 1));

    public ObservableValue<int> McpServerTransportIndex { get; } = new(0, static index => Math.Clamp(index, 0, 1));

    public ObservableValue<string> McpServerId { get; } = new(string.Empty);

    public ObservableValue<string> McpServerDisplayName { get; } = new(string.Empty);

    public ObservableValue<string> McpServerTransport { get; } = new("stdio");

    public ObservableValue<string> McpServerCommand { get; } = new(string.Empty);

    public ObservableValue<string> McpServerArgs { get; } = new(string.Empty);

    public ObservableValue<string> McpServerCwd { get; } = new(string.Empty);

    public ObservableValue<string> McpServerUrl { get; } = new(string.Empty);

    public ObservableValue<string> McpServerBearerTokenEnvVar { get; } = new(string.Empty);

    public ObservableValue<string> McpServerEnvVars { get; } = new(string.Empty);

    public ObservableValue<string> McpServerStartupTimeoutMs { get; } = new("10000");

    public ObservableValue<string> McpServerToolTimeoutMs { get; } = new("120000");

    public ObservableValue<string> McpServerEnabledTools { get; } = new(string.Empty);

    public ObservableValue<string> McpServerDisabledTools { get; } = new(string.Empty);

    public ObservableValue<string> McpServerResolvedPathText { get; } = new(string.Empty);

    public IReadOnlyList<string> McpServerPackageLabels { get; private set; } = [];

    public IReadOnlyList<string> McpServerLabels { get; private set; } = [];

    public IReadOnlyList<string> McpServerPackageTypeLabels { get; } = ["builtin", "plugin", "package"];

    public IReadOnlyList<string> McpServerTransportLabels { get; } = ["stdio", "http"];

    public IReadOnlyList<string> McpServerRequiredLabels { get; } = ["必需", "可选"];

    public ObservableValue<string> ArtifactStorePackageRootText { get; } = new(string.Empty);

    public ObservableValue<string> ArtifactStorePackageStatusText { get; } = new(string.Empty);

    public ObservableValue<string> ArtifactStorePackageManifestPathText { get; } = new(string.Empty);

    public ObservableValue<int> ArtifactStorePackageIndex { get; } = new(0, static index => Math.Max(0, index));

    public ObservableValue<int> ArtifactStorePackageEnabledIndex { get; } = new(0, static index => Math.Clamp(index, 0, 1));

    public ObservableValue<int> ArtifactStorePackageTypeIndex { get; } = new(1, static index => Math.Clamp(index, 0, 4));

    public ObservableValue<string> ArtifactStorePackageId { get; } = new(string.Empty);

    public ObservableValue<string> ArtifactStorePackageDisplayName { get; } = new(string.Empty);

    public ObservableValue<string> ArtifactStorePackageType { get; } = new("filesystem");

    public ObservableValue<string> ArtifactStorePackagePriority { get; } = new("0");

    public ObservableValue<int> ArtifactStoreIndex { get; } = new(0, static index => Math.Max(0, index));

    public ObservableValue<int> ArtifactStoreEnabledIndex { get; } = new(0, static index => Math.Clamp(index, 0, 1));

    public ObservableValue<int> ArtifactStoreTypeIndex { get; } = new(0, static index => Math.Clamp(index, 0, 4));

    public ObservableValue<int> ArtifactStoreCrossProcessSyncIndex { get; } = new(0, static index => Math.Clamp(index, 0, 1));

    public ObservableValue<string> ArtifactStoreId { get; } = new(string.Empty);

    public ObservableValue<string> ArtifactStoreDisplayName { get; } = new(string.Empty);

    public ObservableValue<string> ArtifactStoreType { get; } = new("filesystem");

    public ObservableValue<string> ArtifactStoreRoot { get; } = new("./data");

    public ObservableValue<string> ArtifactStoreAssemblyPath { get; } = new(string.Empty);

    public ObservableValue<string> ArtifactStoreProviderType { get; } = new(string.Empty);

    public ObservableValue<string> ArtifactStoreMaxHistoryVersions { get; } = new("20");

    public ObservableValue<string> ArtifactStorePriority { get; } = new("0");

    public ObservableValue<string> ArtifactStoreResolvedPathText { get; } = new(string.Empty);

    public IReadOnlyList<string> ArtifactStorePackageLabels { get; private set; } = [];

    public IReadOnlyList<string> ArtifactStoreLabels { get; private set; } = [];

    public IReadOnlyList<string> ArtifactStorePackageTypeLabels { get; } = ["builtin", "filesystem", "assembly", "package", "plugin"];

    public IReadOnlyList<string> ArtifactStoreTypeLabels { get; } = ["filesystem", "local-filesystem", "assembly", "package", "plugin"];

    public ObservableValue<string> DiagnosticSinkPackageRootText { get; } = new(string.Empty);

    public ObservableValue<string> DiagnosticSinkPackageStatusText { get; } = new(string.Empty);

    public ObservableValue<string> DiagnosticSinkPackageManifestPathText { get; } = new(string.Empty);

    public ObservableValue<int> DiagnosticSinkPackageIndex { get; } = new(0, static index => Math.Max(0, index));

    public ObservableValue<int> DiagnosticSinkPackageEnabledIndex { get; } = new(0, static index => Math.Clamp(index, 0, 1));

    public ObservableValue<int> DiagnosticSinkPackageTypeIndex { get; } = new(1, static index => Math.Clamp(index, 0, 4));

    public ObservableValue<string> DiagnosticSinkPackageId { get; } = new(string.Empty);

    public ObservableValue<string> DiagnosticSinkPackageDisplayName { get; } = new(string.Empty);

    public ObservableValue<string> DiagnosticSinkPackageType { get; } = new("package");

    public ObservableValue<string> DiagnosticSinkPackagePriority { get; } = new("0");

    public ObservableValue<int> DiagnosticSinkIndex { get; } = new(0, static index => Math.Max(0, index));

    public ObservableValue<int> DiagnosticSinkEnabledIndex { get; } = new(0, static index => Math.Clamp(index, 0, 1));

    public ObservableValue<int> DiagnosticSinkTypeIndex { get; } = new(0, static index => Math.Clamp(index, 0, 6));

    public ObservableValue<int> DiagnosticSinkLevelIndex { get; } = new(2, static index => Math.Clamp(index, 0, 4));

    public ObservableValue<string> DiagnosticSinkId { get; } = new(string.Empty);

    public ObservableValue<string> DiagnosticSinkDisplayName { get; } = new(string.Empty);

    public ObservableValue<string> DiagnosticSinkType { get; } = new("turn-log");

    public ObservableValue<string> DiagnosticSinkLevel { get; } = new("stats");

    public ObservableValue<string> DiagnosticSinkTarget { get; } = new(string.Empty);

    public ObservableValue<string> DiagnosticSinkAssemblyPath { get; } = new(string.Empty);

    public ObservableValue<string> DiagnosticSinkProviderType { get; } = new(string.Empty);

    public ObservableValue<string> DiagnosticSinkEndpoint { get; } = new(string.Empty);

    public ObservableValue<string> DiagnosticSinkModules { get; } = new(string.Empty);

    public ObservableValue<string> DiagnosticSinkMaxBytes { get; } = new(string.Empty);

    public ObservableValue<string> DiagnosticSinkPriority { get; } = new("0");

    public ObservableValue<string> DiagnosticSinkResolvedPathText { get; } = new(string.Empty);

    public IReadOnlyList<string> DiagnosticSinkPackageLabels { get; private set; } = [];

    public IReadOnlyList<string> DiagnosticSinkLabels { get; private set; } = [];

    public IReadOnlyList<string> DiagnosticSinkPackageTypeLabels { get; } = ["builtin", "package", "assembly", "plugin", "telemetry"];

    public IReadOnlyList<string> DiagnosticSinkTypeLabels { get; } = ["turn-log", "artifact-file", "telemetry", "otlp", "assembly", "package", "plugin"];

    public IReadOnlyList<string> DiagnosticSinkLevelLabels { get; } = ["off", "summary", "stats", "artifact", "verbose"];

    public ObservableValue<string> WorkspaceResolverPackageRootText { get; } = new(string.Empty);

    public ObservableValue<string> WorkspaceResolverPackageStatusText { get; } = new(string.Empty);

    public ObservableValue<string> WorkspaceResolverPackageManifestPathText { get; } = new(string.Empty);

    public ObservableValue<int> WorkspaceResolverPackageIndex { get; } = new(0, static index => Math.Max(0, index));

    public ObservableValue<int> WorkspaceResolverPackageEnabledIndex { get; } = new(0, static index => Math.Clamp(index, 0, 1));

    public ObservableValue<int> WorkspaceResolverPackageTypeIndex { get; } = new(1, static index => Math.Clamp(index, 0, 3));

    public ObservableValue<string> WorkspaceResolverPackageId { get; } = new(string.Empty);

    public ObservableValue<string> WorkspaceResolverPackageDisplayName { get; } = new(string.Empty);

    public ObservableValue<string> WorkspaceResolverPackageType { get; } = new("package");

    public ObservableValue<string> WorkspaceResolverPackagePriority { get; } = new("0");

    public ObservableValue<int> WorkspaceResolverIndex { get; } = new(0, static index => Math.Max(0, index));

    public ObservableValue<int> WorkspaceResolverEnabledIndex { get; } = new(0, static index => Math.Clamp(index, 0, 1));

    public ObservableValue<int> WorkspaceResolverTypeIndex { get; } = new(0, static index => Math.Clamp(index, 0, 3));

    public ObservableValue<string> WorkspaceResolverId { get; } = new(string.Empty);

    public ObservableValue<string> WorkspaceResolverDisplayName { get; } = new(string.Empty);

    public ObservableValue<string> WorkspaceResolverType { get; } = new("marker");

    public ObservableValue<string> WorkspaceResolverRootMarkers { get; } = new(string.Empty);

    public ObservableValue<string> WorkspaceResolverProfile { get; } = new("default");

    public ObservableValue<string> WorkspaceResolverTrustPolicy { get; } = new("prompt");

    public ObservableValue<string> WorkspaceResolverArtifactRoot { get; } = new(".tianshu/artifacts");

    public ObservableValue<string> WorkspaceResolverStateRoot { get; } = new(".tianshu/state");

    public ObservableValue<string> WorkspaceResolverIgnoreGlobs { get; } = new(string.Empty);

    public ObservableValue<string> WorkspaceResolverLanguageMarkers { get; } = new(string.Empty);

    public ObservableValue<string> WorkspaceResolverFrameworkMarkers { get; } = new(string.Empty);

    public ObservableValue<string> WorkspaceResolverAssemblyPath { get; } = new(string.Empty);

    public ObservableValue<string> WorkspaceResolverProviderType { get; } = new(string.Empty);

    public ObservableValue<string> WorkspaceResolverPriority { get; } = new("0");

    public ObservableValue<string> WorkspaceResolverResolvedPathText { get; } = new(string.Empty);

    public IReadOnlyList<string> WorkspaceResolverPackageLabels { get; private set; } = [];

    public IReadOnlyList<string> WorkspaceResolverLabels { get; private set; } = [];

    public IReadOnlyList<string> WorkspaceResolverPackageTypeLabels { get; } = ["builtin", "package", "assembly", "plugin"];

    public IReadOnlyList<string> WorkspaceResolverTypeLabels { get; } = ["marker", "assembly", "package", "plugin"];

    public ObservableValue<string> PolicyStrategyPackageRootText { get; } = new(string.Empty);

    public ObservableValue<string> PolicyStrategyPackageStatusText { get; } = new(string.Empty);

    public ObservableValue<string> PolicyStrategyPackageManifestPathText { get; } = new(string.Empty);

    public ObservableValue<int> PolicyStrategyPackageIndex { get; } = new(0, static index => Math.Max(0, index));

    public ObservableValue<int> PolicyStrategyPackageEnabledIndex { get; } = new(0, static index => Math.Clamp(index, 0, 1));

    public ObservableValue<int> PolicyStrategyPackageTypeIndex { get; } = new(1, static index => Math.Clamp(index, 0, 3));

    public ObservableValue<string> PolicyStrategyPackageId { get; } = new(string.Empty);

    public ObservableValue<string> PolicyStrategyPackageDisplayName { get; } = new(string.Empty);

    public ObservableValue<string> PolicyStrategyPackageType { get; } = new("package");

    public ObservableValue<string> PolicyStrategyPackagePriority { get; } = new("0");

    public ObservableValue<int> PolicyStrategyIndex { get; } = new(0, static index => Math.Max(0, index));

    public ObservableValue<int> PolicyStrategyEnabledIndex { get; } = new(0, static index => Math.Clamp(index, 0, 1));

    public ObservableValue<int> PolicyStrategyTypeIndex { get; } = new(0, static index => Math.Clamp(index, 0, 3));

    public ObservableValue<int> PolicyStrategyApprovalPolicyIndex { get; } = new(1, static index => Math.Clamp(index, 0, 3));

    public ObservableValue<int> PolicyStrategySandboxModeIndex { get; } = new(1, static index => Math.Clamp(index, 0, 1));

    public ObservableValue<int> PolicyStrategyNetworkAccessIndex { get; } = new(1, static index => Math.Clamp(index, 0, 1));

    public ObservableValue<int> PolicyStrategyAllowLoginShellIndex { get; } = new(0, static index => Math.Clamp(index, 0, 1));

    public ObservableValue<string> PolicyStrategyId { get; } = new(string.Empty);

    public ObservableValue<string> PolicyStrategyDisplayName { get; } = new(string.Empty);

    public ObservableValue<string> PolicyStrategyType { get; } = new("rules");

    public ObservableValue<string> PolicyStrategyPriority { get; } = new("0");

    public ObservableValue<string> PolicyStrategyWriteApprovalGlobs { get; } = new(string.Empty);

    public ObservableValue<string> PolicyStrategyDangerousCommandPatterns { get; } = new(string.Empty);

    public ObservableValue<string> PolicyStrategyCommandRules { get; } = new(string.Empty);

    public ObservableValue<string> PolicyStrategyNetworkRules { get; } = new(string.Empty);

    public ObservableValue<string> PolicyStrategyResolvedPathText { get; } = new(string.Empty);

    public IReadOnlyList<string> PolicyStrategyPackageLabels { get; private set; } = [];

    public IReadOnlyList<string> PolicyStrategyLabels { get; private set; } = [];

    public IReadOnlyList<string> PolicyStrategyPackageTypeLabels { get; } = ["builtin", "package", "assembly", "plugin"];

    public IReadOnlyList<string> PolicyStrategyTypeLabels { get; } = ["rules", "assembly", "package", "plugin"];

    public IReadOnlyList<string> PolicyStrategyApprovalPolicyLabels { get; } = ["never", "on-request", "on-failure", "untrusted"];

    public IReadOnlyList<string> PolicyStrategySandboxModeLabels { get; } = ["read-only", "workspace-write"];

    public void Refresh()
    {
        var selectedKey = selectedField?.Key;
        var projection = loader.LoadUserFileWithModules(ConfigPath);
        var groups = projection.Groups.ToDictionary(static group => group.Id, StringComparer.OrdinalIgnoreCase);
        var categories = projection.Categories.ToDictionary(static category => category.Id, StringComparer.OrdinalIgnoreCase);
        var values = projection.Values.ToDictionary(static value => value.Key, StringComparer.OrdinalIgnoreCase);
        var sources = projection.Sources.ToDictionary(static source => source.Id, StringComparer.OrdinalIgnoreCase);

        ConfigPathText.Value = $"配置路径：{ConfigPath}";
        allFields.Clear();
        foreach (var field in projection.Fields)
        {
            groups.TryGetValue(field.GroupId, out var group);
            var categoryId = group?.CategoryId ?? ConfigurationCategoryIds.ExtensionsImports;
            categories.TryGetValue(categoryId, out var category);
            values.TryGetValue(field.Key, out var value);
            var sourceName = value?.SourceLayerId is { Length: > 0 } sourceId && sources.TryGetValue(sourceId, out var source)
                ? source.DisplayName
                : "默认值";

            allFields.Add(new ConfigFieldRow(
                field.Key,
                GetFieldDisplayName(field.Key, field.DisplayName),
                field.Description ?? string.Empty,
                categoryId,
                category?.DisplayName ?? "扩展导入",
                category?.Order ?? int.MaxValue,
                field.GroupId,
                group?.DisplayName ?? "未分组",
                group?.Order ?? int.MaxValue,
                field.ValueKind,
                field.EditMode,
                field.IsAdvanced,
                field.AllowedValues.Select(AllowedOptionRow.FromContract).ToArray(),
                value?.IsConfigured == true,
                value?.IsSensitive == true,
                sourceName,
                FormatValue(value?.Value),
                FormatValue(field.DefaultValue),
                string.Join(Environment.NewLine, value?.Issues.Select(static issue => issue.Message) ?? [])));
        }
        AddProfileCompositionProjectionFields();

        WorkspaceFields = allFields
            .Where(static row => row.CategoryId == ConfigurationCategoryIds.Workspace)
            .OrderBy(static row => EnabledFieldSortRank(row))
            .ThenBy(static row => row.GroupOrder)
            .ThenBy(static row => row.Key, StringComparer.Ordinal)
            .ToList();

        var ordinaryFields = allFields.Where(static row => row.CategoryId != ConfigurationCategoryIds.Workspace).ToArray();
        Categories.Clear();
        foreach (var category in projection.Categories.Where(static category =>
                     category.Id != ConfigurationCategoryIds.Workspace
                     && category.Id != ConfigurationCategoryIds.IdentityMemory))
        {
            var count = ordinaryFields.Count(field => field.CategoryId == category.Id && IsVisibleFieldInCategory(field, category.Id));
            AddCategoryRow(category.Id, GetNavigationDisplayName(category.Id, category.DisplayName), category.Description ?? string.Empty, count);
        }

        AddProfileCompositionPageCategories(ordinaryFields);
        ApplyProfileCompositionChoices();
        AddMemoryPageCategories(ordinaryFields);
        ApplyMemoryProfileChoices();
        AddCategoryRow(WorkspaceCollectionCategoryId, "工作空间配置", "管理工作空间配置文件与项目路径覆盖。", WorkspaceFields.Count);
        BuildProviderItems();
        AddCategoryRow(ProviderCollectionCategoryId, "提供方实例", "管理 modules/model/provider-instances/<id>.toml 中的 providers.<id> 集合。", providerItems.Count);
        BuildModelProtocolMappingItems(values);
        AddCategoryRow(ModelProtocolMappingCollectionCategoryId, "模型协议适配", "管理 modules/model/provider-instances/<id>.toml 中的 providers.<id>.model_overrides 与提供方局部 protocol_rules。", ModelProtocolMappingCount());
        BuildDefaultModelProtocolRuleSetItems(values);
        AddCategoryRow(DefaultModelProtocolRuleSetCollectionCategoryId, "默认协议规则", "管理 modules/model/protocol-rules/<id>.toml 中跨提供方生效的默认通配规则。", DefaultModelProtocolRuleSetCount());
        BuildModelRouteSetItems(values);
        ApplyDailyModelSelectionChoices();
        AddCategoryRow(ModelRouteSetCollectionCategoryId, "模型路由方案", "管理 modules/model/route-sets/<id>.toml 的 routes、route 与有序候选列表。", modelRouteSetItems.Count);
        RefreshPromptProjection(promptProjection?.SelectedFilePath);
        AddCategoryRow(PromptCollectionCategoryId, "Prompt Pack", "管理 TianShu Prompt Pack。", promptProjection?.Files.Count ?? 0);
        RefreshSkillPackageProjection(selectedSkillPackagePath);
        AddCategoryRow(SkillPackageCollectionCategoryId, "技能包", "管理 modules/skills/<package>/SKILL.md 内容能力包启用状态与元数据投影。", skillPackageProjection?.Packages.Count ?? 0);
        RefreshToolProjection(selectedToolManifestPath);
        AddCategoryRow(ToolPackageCollectionCategoryId, "工具包", "管理 modules/tools/packages/<package>/tool.toml 与提供方 manifest。", toolProjection?.Files.Count ?? 0);
        RefreshProviderPackageProjection(selectedProviderManifestPath);
        AddCategoryRow(ProviderPackageCollectionCategoryId, "协议适配器包", "管理 modules/model/provider-adapters/<package>/provider.toml 与 adapter manifest。", providerPackageProjection?.Files.Count ?? 0);
        RefreshMcpServerPackageProjection(selectedMcpServerManifestPath);
        AddCategoryRow(McpServerPackageCollectionCategoryId, "MCP Server 包", "管理 modules/mcp-servers/<package>/server.toml 与 server manifest。", mcpServerPackageProjection?.Files.Count ?? 0);
        RefreshArtifactStorePackageProjection(selectedArtifactStoreManifestPath);
        AddCategoryRow(ArtifactStorePackageCollectionCategoryId, "工件存储包", "管理 modules/artifacts/stores/<package>/store.toml 与 runtime store manifest。", artifactStorePackageProjection?.Files.Count ?? 0);
        RefreshDiagnosticSinkPackageProjection(selectedDiagnosticSinkManifestPath);
        AddCategoryRow(DiagnosticSinkPackageCollectionCategoryId, "诊断输出包", "管理 modules/diagnostics/sinks/<package>/sink.toml 与 diagnostics / telemetry sink manifest。", diagnosticSinkPackageProjection?.Files.Count ?? 0);
        RefreshWorkspaceResolverPackageProjection(selectedWorkspaceResolverManifestPath);
        AddCategoryRow(WorkspaceResolverPackageCollectionCategoryId, "工作空间解析包", "管理 modules/workspace/resolvers/<package>/resolver.toml 与项目根解析策略。", workspaceResolverPackageProjection?.Files.Count ?? 0);
        RefreshPolicyStrategyPackageProjection(selectedPolicyStrategyManifestPath);
        AddCategoryRow(PolicyStrategyPackageCollectionCategoryId, "审批策略包", "管理 modules/policies/strategies/<package>/policy.toml 与受控审批策略。", policyStrategyPackageProjection?.Files.Count ?? 0);
        PruneCategoryRowCache();
        BuildNavigationModules();

        if (!Categories.Any(category => string.Equals(category.Id, selectedCategoryId, StringComparison.OrdinalIgnoreCase)))
        {
            selectedCategoryId = Categories.FirstOrDefault()?.Id ?? ConfigurationCategoryIds.Foundation;
            viewMode = selectedCategoryId switch
            {
                WorkspaceCollectionCategoryId => ConfigGuiViewMode.Workspace,
                ProviderCollectionCategoryId => ConfigGuiViewMode.Providers,
                ModelProtocolMappingCollectionCategoryId => ConfigGuiViewMode.ModelProtocolMappings,
                DefaultModelProtocolRuleSetCollectionCategoryId => ConfigGuiViewMode.DefaultModelProtocolRules,
                ModelRouteSetCollectionCategoryId => ConfigGuiViewMode.ModelRouteSets,
                PromptCollectionCategoryId => ConfigGuiViewMode.Prompts,
                SkillPackageCollectionCategoryId => ConfigGuiViewMode.SkillPackages,
                ToolPackageCollectionCategoryId => ConfigGuiViewMode.ToolPackages,
                ProviderPackageCollectionCategoryId => ConfigGuiViewMode.ProviderPackages,
                McpServerPackageCollectionCategoryId => ConfigGuiViewMode.McpServerPackages,
                ArtifactStorePackageCollectionCategoryId => ConfigGuiViewMode.ArtifactStorePackages,
                DiagnosticSinkPackageCollectionCategoryId => ConfigGuiViewMode.DiagnosticSinkPackages,
                WorkspaceResolverPackageCollectionCategoryId => ConfigGuiViewMode.WorkspaceResolverPackages,
                PolicyStrategyPackageCollectionCategoryId => ConfigGuiViewMode.PolicyStrategyPackages,
                _ => ConfigGuiViewMode.Fields,
            };
        }

        RefreshSelectedPageState();
        RefreshViewMode();
        ApplyFilter();
        SelectField(FilteredFields.FirstOrDefault(row => string.Equals(row.Key, selectedKey, StringComparison.OrdinalIgnoreCase)) ?? FilteredFields.FirstOrDefault());
        StatusText.Value = File.Exists(ConfigPath)
            ? $"已读取 {projection.Fields.Count} 个字段，{projection.Issues.Count} 条提示。修改后需点击保存，字段会写入主配置或对应模块 TOML。"
            : "配置文件不存在；保存时会创建 tianshu.toml，不会写入 prompt 文件。";
        UpdateContextPanel();
    }

    private void RefreshSelectedPageState()
    {
        var selectedCategory = Categories.FirstOrDefault(row => string.Equals(row.Id, selectedCategoryId, StringComparison.OrdinalIgnoreCase));
        SelectedPageTitle.Value = selectedCategory?.DisplayName ?? "配置项";
        RefreshNavigationHighlights();
    }

    private void AddCategoryRow(string id, string displayName, string description, int fieldCount)
    {
        if (!categoryRowsById.TryGetValue(id, out var row))
        {
            row = new ConfigCategoryRow(id, displayName, description, fieldCount);
            categoryRowsById[id] = row;
        }
        else
        {
            row.Update(displayName, description, fieldCount);
        }

        Categories.Add(row);
    }

    private void PruneCategoryRowCache()
    {
        var activeIds = Categories.Select(static category => category.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in categoryRowsById.Keys.Where(id => !activeIds.Contains(id)).ToArray())
        {
            categoryRowsById.Remove(id);
        }
    }

    private void RefreshNavigationHighlights()
    {
        foreach (var category in Categories)
        {
            category.IsNavigationHighlighted.Value = IsCategoryHighlighted(category.Id);
        }
    }

    private static string GetNavigationDisplayName(string categoryId, string fallback)
        => categoryId switch
        {
            ConfigurationCategoryIds.ConnectivityModel => "模型选择",
            ConfigurationCategoryIds.AgentBehavior => "代理行为",
            ConfigurationCategoryIds.CapabilitiesTools => "工具配置",
            ConfigurationCategoryIds.DiagnosticsState => "诊断配置",
            _ => fallback,
        };

    private static string GetFieldDisplayName(string key, string fallback)
    {
        var displayName = TryGetProfileReferenceDisplayName(key, out var profileReferenceName)
            ? profileReferenceName
            : TryGetProfileStageDisplayName(key, out var profileStageName)
                ? profileStageName
                : key switch
                {
                    "model_route_set" => "当前模型路由方案",
                    "model_protocol_rule_set" => "模型协议",
                    "memory.enabled" => "启用记忆",
                    "memory.default_profile" => "当前记忆配置文件",
                    _ => fallback,
                };

        return StripDefaultInstancePrefix(displayName);
    }

    private static string StripDefaultInstancePrefix(string displayName)
    {
        const string DefaultInstancePrefix = "default / ";
        return displayName.StartsWith(DefaultInstancePrefix, StringComparison.OrdinalIgnoreCase)
            ? displayName[DefaultInstancePrefix.Length..]
            : displayName;
    }

    private static bool TryGetProfileReferenceDisplayName(string key, out string displayName)
    {
        displayName = string.Empty;
        var parts = key.Split('.');
        if (parts.Length != 3 || !string.Equals(parts[0], "profiles", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        displayName = parts[2].ToLowerInvariant() switch
        {
            "extends" => "继承配置文件",
            "agent" => "Agent 配置文件",
            "execution" => "执行配置文件",
            "conversation" => "对话配置文件",
            "permissions" => "权限配置文件",
            "model_route_set" => "模型路由方案",
            "memory" => "记忆配置文件",
            "tools" => "工具配置文件",
            "tui" => "TUI 配置文件",
            "workspace" => "工作空间配置文件",
            "session" => "会话配置文件",
            "collaboration" => "协作配置文件",
            "workflow" => "工作流配置文件",
            "identity" => "身份配置文件",
            "governance" => "治理配置文件",
            "features" => "功能配置文件",
            "realtime" => "Realtime 配置文件",
            _ => string.Empty,
        };
        return displayName.Length > 0;
    }

    private static bool TryGetProfileStageDisplayName(string key, out string displayName)
    {
        displayName = string.Empty;
        var parts = key.Split('.');
        if (parts.Length != 4
            || !string.Equals(parts[0], "profiles", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(parts[2], "stages", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        displayName = parts[3].ToLowerInvariant() switch
        {
            "planning" => "规划阶段",
            "execution" => "执行阶段",
            "review" => "审阅阶段",
            "summary" => "总结阶段",
            _ => string.Empty,
        };
        return displayName.Length > 0;
    }

    private void ApplyDailyModelSelectionChoices()
    {
        ReplaceFieldChoices(
            "model_route_set",
            modelRouteSetItems.Select(static routeSet => new AllowedOptionRow(
                routeSet.Id,
                string.IsNullOrWhiteSpace(routeSet.Description) ? $"选择模型路由方案：{routeSet.Id}" : routeSet.Description,
                routeSet.DisplayLabel)).ToArray());
        ReplaceFieldChoices(
            "model_protocol_rule_set",
            defaultModelProtocolRuleSetItems.Select(static ruleSet => new AllowedOptionRow(
                ruleSet.Id,
                string.IsNullOrWhiteSpace(ruleSet.Description) ? $"选择默认模型协议规则集：{ruleSet.Id}" : ruleSet.Description,
                ruleSet.DisplayLabel)).ToArray());
    }

    private void ReplaceFieldChoices(string key, IReadOnlyList<AllowedOptionRow> choices)
    {
        var field = allFields.FirstOrDefault(row => string.Equals(row.Key, key, StringComparison.OrdinalIgnoreCase));
        if (field is null)
        {
            return;
        }

        field.ReplaceAllowedOptions(choices);
        if (string.IsNullOrWhiteSpace(field.EditValue) && choices.Count > 0)
        {
            field.EditValue = choices[0].Value;
        }
    }

    public void SelectCategory(string categoryId)
    {
        hoveredCategoryId = null;
        selectedCategoryId = categoryId;
        viewMode = categoryId switch
        {
            WorkspaceCollectionCategoryId => ConfigGuiViewMode.Workspace,
            ProviderCollectionCategoryId => ConfigGuiViewMode.Providers,
            ModelProtocolMappingCollectionCategoryId => ConfigGuiViewMode.ModelProtocolMappings,
            DefaultModelProtocolRuleSetCollectionCategoryId => ConfigGuiViewMode.DefaultModelProtocolRules,
            ModelRouteSetCollectionCategoryId => ConfigGuiViewMode.ModelRouteSets,
            PromptCollectionCategoryId => ConfigGuiViewMode.Prompts,
            SkillPackageCollectionCategoryId => ConfigGuiViewMode.SkillPackages,
            ToolPackageCollectionCategoryId => ConfigGuiViewMode.ToolPackages,
            ProviderPackageCollectionCategoryId => ConfigGuiViewMode.ProviderPackages,
            McpServerPackageCollectionCategoryId => ConfigGuiViewMode.McpServerPackages,
            ArtifactStorePackageCollectionCategoryId => ConfigGuiViewMode.ArtifactStorePackages,
            DiagnosticSinkPackageCollectionCategoryId => ConfigGuiViewMode.DiagnosticSinkPackages,
            WorkspaceResolverPackageCollectionCategoryId => ConfigGuiViewMode.WorkspaceResolverPackages,
            PolicyStrategyPackageCollectionCategoryId => ConfigGuiViewMode.PolicyStrategyPackages,
            _ => ConfigGuiViewMode.Fields,
        };
        RefreshSelectedPageState();
        RefreshViewMode();
        ApplyFilter();
        if (viewMode is ConfigGuiViewMode.Fields or ConfigGuiViewMode.Workspace)
        {
            SelectField(viewMode == ConfigGuiViewMode.Workspace ? WorkspaceFields.FirstOrDefault() : FilteredFields.FirstOrDefault());
        }
        else
        {
            UpdateContextPanel();
        }
    }

    public bool IsCategorySelected(string categoryId)
        => string.Equals(selectedCategoryId, categoryId, StringComparison.OrdinalIgnoreCase);

    public bool IsCategoryHighlighted(string categoryId)
        => IsCategorySelected(categoryId)
           || string.Equals(hoveredCategoryId, categoryId, StringComparison.OrdinalIgnoreCase);

    public void HoverCategory(string categoryId)
    {
        hoveredCategoryId = categoryId;
        RefreshNavigationHighlights();
    }

    public void ClearHoveredCategory(string categoryId)
    {
        if (string.Equals(hoveredCategoryId, categoryId, StringComparison.OrdinalIgnoreCase))
        {
            hoveredCategoryId = null;
            RefreshNavigationHighlights();
        }
    }

    public void SelectField(ConfigFieldRow? row)
    {
        selectedField = row;
        if (row is null)
        {
            SelectedDisplayName.Value = "未选择配置项";
            SelectedKey.Value = string.Empty;
            SelectedDescription.Value = string.Empty;
            SelectedCategoryName.Value = string.Empty;
            SelectedGroupName.Value = string.Empty;
            SelectedValueKind.Value = string.Empty;
            SelectedSensitiveText.Value = string.Empty;
            SelectedSourceName.Value = string.Empty;
            SelectedConfiguredText.Value = string.Empty;
            SelectedEditValue.Value = string.Empty;
            SelectedDefaultValue.Value = string.Empty;
            SelectedIssues.Value = string.Empty;
            SelectedHelpText.Value = "选择一个配置项查看说明。";
            SelectedEnumLabels = [];
            IsTextEditorVisible.Value = IsFieldDetailEditorVisible.Value;
            IsEnumEditorVisible.Value = false;
            SelectedEnumIndex.Value = 0;
            UpdateContextPanel();
            return;
        }

        SelectedDisplayName.Value = row.DisplayName;
        SelectedKey.Value = row.Key;
        SelectedDescription.Value = row.Description;
        SelectedCategoryName.Value = row.CategoryName;
        SelectedGroupName.Value = row.GroupName;
        SelectedValueKind.Value = row.ValueKind.ToString();
        SelectedSensitiveText.Value = row.SensitiveText;
        SelectedSourceName.Value = row.SourceName;
        SelectedConfiguredText.Value = row.ConfiguredText;
        SelectedEditValue.Value = row.EditValue;
        SelectedDefaultValue.Value = row.DefaultValue;
        SelectedIssues.Value = row.Issues;
        SelectedHelpText.Value = string.Join(Environment.NewLine, row.BuildHelpLines());
        SelectedEnumLabels = row.ChoiceOptions.Select(static option => option.DisplayLabel).ToArray();
        IsEnumEditorVisible.Value = row.UsesChoiceEditor;
        IsTextEditorVisible.Value = !row.UsesChoiceEditor;
        SyncEnumIndexFromEditValue();
        UpdateContextPanel();
    }

    public void SyncEnumIndexFromEditValue()
    {
        if (selectedField is null || !selectedField.UsesChoiceEditor)
        {
            SelectedEnumIndex.Value = 0;
            return;
        }

        var index = -1;
        var options = selectedField.ChoiceOptions;
        for (var optionIndex = 0; optionIndex < options.Count; optionIndex++)
        {
            if (string.Equals(options[optionIndex].Value, SelectedEditValue.Value, StringComparison.OrdinalIgnoreCase))
            {
                index = optionIndex;
                break;
            }
        }

        SelectedEnumIndex.Value = index < 0 ? 0 : index;
    }

    public void SyncEnumSelection()
    {
        if (selectedField is null || !selectedField.UsesChoiceEditor)
        {
            return;
        }

        var options = selectedField.ChoiceOptions;
        var index = Math.Clamp(SelectedEnumIndex.Value, 0, options.Count - 1);
        if (options.Count > 0)
        {
            SelectedEditValue.Value = options[index].Value;
        }
    }

    public void SelectEnumIndex(int index)
    {
        if (index < 0 || selectedField is null || !selectedField.UsesChoiceEditor || selectedField.ChoiceOptions.Count == 0)
        {
            return;
        }

        SelectedEnumIndex.Value = Math.Clamp(index, 0, selectedField.ChoiceOptions.Count - 1);
        SyncEnumSelection();
    }

    public bool BeginEdit(ConfigFieldRow row)
    {
        SelectField(row);
        if (!row.CanEdit)
        {
            StatusText.Value = $"该配置项为只读：{row.DisplayName}";
            UpdateContextPanel();
            return false;
        }

        StatusText.Value = $"正在编辑：{row.DisplayName}。修改后点击保存才会写入 {Path.GetFileName(ResolveFieldSaveTarget(row.Key))}。";
        UpdateContextPanel();
        return true;
    }

    public void SaveSelected()
    {
        if (selectedField is null)
        {
            return;
        }

        if (!selectedField.CanEdit)
        {
            StatusText.Value = $"该配置项为只读：{selectedField.DisplayName}";
            UpdateContextPanel();
            return;
        }

        try
        {
            selectedField.EditValue = SelectedEditValue.Value;
            var value = TianShuConfigurationTomlChangeApplier.ParseUserInput(selectedField.EditValue, selectedField.ValueKind);
            var result = applier.ApplyRouted(ConfigPath, new ConfigurationChangeSet
            {
                Changes =
                [
                    new ConfigurationChange
                    {
                        Operation = ConfigurationChangeOperation.Set,
                        Key = selectedField.Key,
                        Value = value,
                    },
                ],
            });

            StatusText.Value = result.Applied
                ? $"已保存：{selectedField.DisplayName}"
                : string.Join(Environment.NewLine, result.Issues.Select(static issue => issue.Message));
            Refresh();
        }
        catch (Exception ex)
        {
            StatusText.Value = $"保存失败：{ex.Message}";
            UpdateContextPanel();
        }
    }

    public void ResetSelected()
    {
        if (selectedField is null)
        {
            return;
        }

        var result = applier.ApplyRouted(ConfigPath, new ConfigurationChangeSet
        {
            Changes =
            [
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.ResetToDefault,
                    Key = selectedField.Key,
                },
            ],
        });

        StatusText.Value = result.Applied
            ? $"已重置为默认：{selectedField.DisplayName}"
            : string.Join(Environment.NewLine, result.Issues.Select(static issue => issue.Message));
        Refresh();
    }

    public void RevertSelected()
    {
        if (selectedField is null)
        {
            return;
        }

        selectedField.EditValue = selectedField.CurrentValue;
        SelectedEditValue.Value = selectedField.CurrentValue;
        StatusText.Value = $"已撤销未保存修改：{selectedField.DisplayName}";
        UpdateContextPanel();
    }

    public void BeginNewProvider()
    {
        var source = providerItems.FirstOrDefault(provider => string.Equals(provider.Id, selectedProviderId, StringComparison.OrdinalIgnoreCase));
        if (source is null && providerItems.Count > 0)
        {
            var providerIndex = Math.Clamp(ProviderItemIndex.Value, 0, providerItems.Count - 1);
            source = providerItems[providerIndex];
        }

        selectedProviderId = null;
        if (source is null)
        {
            ProviderItemIndex.Value = 0;
            ProviderId.Value = string.Empty;
            ProviderBaseUrl.Value = string.Empty;
            ProviderApiKeyEnv.Value = string.Empty;
            ProviderProtocolIndex.Value = 0;
            ClearProviderModelList();
            ProviderStatusText.Value = "正在新建提供方；填写后点击保存。";
            return;
        }

        ProviderId.Value = CreateUniqueProviderDraftId(source.Id);
        ProviderBaseUrl.Value = source.BaseUrl;
        ProviderApiKeyEnv.Value = source.ApiKeyEnv;
        ProviderProtocolIndex.Value = Math.Max(0, ProviderProtocols.ToList().FindIndex(protocol => string.Equals(protocol, source.DefaultProtocol, StringComparison.OrdinalIgnoreCase)));
        ClearProviderModelList();
        ProviderStatusText.Value = $"已基于当前提供方生成草稿：{source.Id} -> {ProviderId.Value}；调整后点击保存。";
    }

    public void SelectProviderIndex(int index)
    {
        if (providerItems.Count == 0)
        {
            BeginNewProvider();
            return;
        }

        var providerIndex = Math.Clamp(index, 0, providerItems.Count - 1);
        var provider = providerItems[providerIndex];
        selectedProviderId = provider.Id;
        ProviderItemIndex.Value = providerIndex;
        ProviderId.Value = provider.Id;
        ProviderBaseUrl.Value = provider.BaseUrl;
        ProviderApiKeyEnv.Value = provider.ApiKeyEnv;
        ProviderProtocolIndex.Value = Math.Max(0, ProviderProtocols.ToList().FindIndex(protocol => string.Equals(protocol, provider.DefaultProtocol, StringComparison.OrdinalIgnoreCase)));
        ClearProviderModelList();
        ProviderStatusText.Value = $"正在编辑提供方：{provider.Id}";
    }

    public void CopySelectedProviderToDraft()
    {
        if (string.IsNullOrWhiteSpace(selectedProviderId))
        {
            ProviderStatusText.Value = "没有可复制的提供方。";
            return;
        }

        ProviderId.Value = CreateUniqueProviderDraftId(selectedProviderId);
        selectedProviderId = null;
        ClearProviderModelList();
        ProviderStatusText.Value = "已复制到草稿；调整提供方 ID 后点击保存。";
    }

    private string CreateUniqueProviderDraftId(string baseId)
    {
        var normalizedBaseId = string.IsNullOrWhiteSpace(baseId) ? "provider" : baseId.Trim();
        var existingIds = providerItems.Select(static provider => provider.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var firstCandidate = $"{normalizedBaseId}-copy";
        if (!existingIds.Contains(firstCandidate))
        {
            return firstCandidate;
        }

        for (var index = 2; index < 1000; index++)
        {
            var candidate = $"{normalizedBaseId}-copy-{index}";
            if (!existingIds.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{normalizedBaseId}-copy-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    }

    public void SaveProvider()
    {
        var id = ProviderId.Value.Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            ProviderStatusText.Value = "提供方 id 不能为空。";
            UpdateContextPanel();
            return;
        }

        if (id.Any(static ch => !(char.IsLetterOrDigit(ch) || ch is '_' or '-')))
        {
            ProviderStatusText.Value = "提供方 id 只能包含字母、数字、下划线或短横线。";
            UpdateContextPanel();
            return;
        }

        if (string.IsNullOrWhiteSpace(ProviderBaseUrl.Value))
        {
            ProviderStatusText.Value = "提供方 base_url 不能为空。";
            UpdateContextPanel();
            return;
        }

        var result = SaveProviderInstanceModuleFile(
            id,
            ProviderBaseUrl.Value.Trim(),
            ProviderApiKeyEnv.Value.Trim(),
            ProviderProtocols[ProviderProtocolIndex.Value]);

        if (!result.Applied)
        {
            ProviderStatusText.Value = string.Join(Environment.NewLine, result.Issues.Select(static issue => issue.Message));
            UpdateContextPanel();
            return;
        }

        var referenceResult = SaveActiveProviderInstanceReference();
        if (referenceResult.Applied)
        {
            selectedProviderId = id;
            Refresh();
            ClearProviderModelList();
            ProviderStatusText.Value = $"已保存提供方：{id}；模块文件：{result.TargetPath}";
            UpdateContextPanel();
            return;
        }

        ProviderStatusText.Value = string.Join(Environment.NewLine, referenceResult.Issues.Select(static issue => issue.Message));
        UpdateContextPanel();
    }

    public void DeleteSelectedProvider()
    {
        var id = selectedProviderId ?? ProviderId.Value.Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            ProviderStatusText.Value = "没有可删除的提供方。";
            return;
        }

        var result = DeleteProviderInstanceModuleEntry(id);

        if (result.Applied)
        {
            selectedProviderId = null;
            Refresh();
            ClearProviderModelList();
            ProviderStatusText.Value = $"已删除提供方：{id}";
            return;
        }

        ProviderStatusText.Value = string.Join(Environment.NewLine, result.Issues.Select(static issue => issue.Message));
    }

    public void TestModelProtocolProviderModels()
    {
        var provider = CurrentModelProtocolProvider();
        if (provider is null)
        {
            ModelProtocolStatusText.Value = "请先选择提供方。";
            UpdateContextPanel();
            return;
        }

        var providerConfig = providerItems.FirstOrDefault(item => string.Equals(item.Id, provider.Id, StringComparison.OrdinalIgnoreCase));
        if (providerConfig is null)
        {
            ModelProtocolStatusText.Value = $"未找到提供方配置：{provider.Id}";
            UpdateContextPanel();
            return;
        }

        try
        {
            var result = FetchProviderModels(providerConfig);
            provider.Models.Clear();
            provider.Models.AddRange(result.Models.Take(200));
            RefreshModelProtocolModelCandidates(provider);
            ModelProtocolStatusText.Value = result.Models.Count == 0
                ? $"检测完成：{result.Endpoint} 未返回可识别模型 id。"
                : $"检测完成：{provider.Id} 返回 {result.Models.Count} 个模型，可从“检测模型”下拉选择。";
            UpdateContextPanel();
        }
        catch (Exception ex)
        {
            provider.Models.Clear();
            RefreshModelProtocolModelCandidates(provider, $"检测失败：{ex.Message}");
            ModelProtocolStatusText.Value = $"检测失败：{ex.Message}";
            UpdateContextPanel();
        }
    }

    public void SelectModelProtocolProviderIndex(int index, bool syncCurrentEditor = true)
    {
        if (modelProtocolItems.Count == 0)
        {
            ModelProtocolStatusText.Value = "尚未配置提供方；请先创建提供方实例。";
            return;
        }

        if (syncCurrentEditor)
        {
            SyncModelProtocolDraftFromEditor();
        }

        var providerIndex = Math.Clamp(index, 0, modelProtocolItems.Count - 1);
        var provider = modelProtocolItems[providerIndex];
        selectedModelProtocolProviderId = provider.Id;
        ModelProtocolProviderIndex.Value = providerIndex;
        LoadModelProtocolOverrideIndex(provider.Overrides.Count == 0 ? -1 : Math.Clamp(ModelProtocolOverrideIndex.Value, 0, provider.Overrides.Count - 1));
        LoadModelProtocolRuleIndex(provider.Rules.Count == 0 ? -1 : Math.Clamp(ModelProtocolRuleIndex.Value, 0, provider.Rules.Count - 1));
        RefreshModelProtocolModelCandidates(provider);
        ModelProtocolStatusText.Value = $"正在编辑提供方协议适配：{provider.Id}";
        UpdateContextPanel();
    }

    public void SelectModelProtocolOverrideIndex(int index)
    {
        SyncModelProtocolDraftFromEditor();
        LoadModelProtocolOverrideIndex(index);
    }

    public void SelectModelProtocolDetectedModelIndex(int index)
    {
        var provider = CurrentModelProtocolProvider();
        if (provider is null || provider.Models.Count == 0)
        {
            return;
        }

        var modelIndex = Math.Clamp(index, 0, provider.Models.Count - 1);
        var model = provider.Models[modelIndex];
        if (provider.Overrides.Count == 0)
        {
            provider.Overrides.Add(new ModelProtocolOverrideDraft(model, "anthropic_messages, openai_chat_completions"));
            LoadModelProtocolOverrideIndex(provider.Overrides.Count - 1);
        }
        else
        {
            ModelProtocolOverrideName.Value = model;
            SyncModelProtocolDraftFromEditor();
        }

        ModelProtocolModelIndex.Value = modelIndex;
        ModelProtocolStatusText.Value = $"已选择检测模型：{model}";
        UpdateContextPanel();
    }

    public void SelectModelProtocolRuleIndex(int index)
    {
        SyncModelProtocolDraftFromEditor();
        LoadModelProtocolRuleIndex(index);
    }

    public void BeginNewModelProtocolOverride()
    {
        var provider = CurrentModelProtocolProvider();
        if (provider is null)
        {
            ModelProtocolStatusText.Value = "请先选择提供方。";
            UpdateContextPanel();
            return;
        }

        SyncModelProtocolDraftFromEditor();
        var selectedModel = provider.Models.Count == 0
            ? string.Empty
            : provider.Models[Math.Clamp(ModelProtocolModelIndex.Value, 0, provider.Models.Count - 1)];
        provider.Overrides.Add(new ModelProtocolOverrideDraft(selectedModel, "anthropic_messages, openai_chat_completions"));
        LoadModelProtocolOverrideIndex(provider.Overrides.Count - 1);
        ModelProtocolStatusText.Value = string.IsNullOrWhiteSpace(selectedModel)
            ? "已新增空精确模型覆写草稿；请选择检测模型或填写手工模型名。"
            : $"已新增精确模型覆写草稿：{selectedModel}；保存前不会写入配置。";
        UpdateContextPanel();
    }

    public void DeleteSelectedModelProtocolOverride()
    {
        var provider = CurrentModelProtocolProvider();
        if (provider is null || provider.Overrides.Count == 0)
        {
            ModelProtocolStatusText.Value = "没有可删除的精确模型覆写。";
            UpdateContextPanel();
            return;
        }

        var index = Math.Clamp(ModelProtocolOverrideIndex.Value, 0, provider.Overrides.Count - 1);
        provider.Overrides.RemoveAt(index);
        LoadModelProtocolOverrideIndex(provider.Overrides.Count == 0 ? -1 : Math.Min(index, provider.Overrides.Count - 1));
        ModelProtocolOverrideLabels = BuildModelProtocolOverrideLabels(provider);
        ModelProtocolStatusText.Value = "已删除精确模型覆写草稿；保存前不会写入配置。";
        UpdateContextPanel();
    }

    public void BeginNewModelProtocolRule()
    {
        var provider = CurrentModelProtocolProvider();
        if (provider is null)
        {
            ModelProtocolStatusText.Value = "请先选择提供方。";
            UpdateContextPanel();
            return;
        }

        SyncModelProtocolDraftFromEditor();
        provider.Rules.Add(new ModelProtocolRuleDraft("deepseek-*", "openai_chat_completions, anthropic_messages"));
        LoadModelProtocolRuleIndex(provider.Rules.Count - 1);
        ModelProtocolStatusText.Value = "已新增通配规则草稿；保存前不会写入配置。";
        UpdateContextPanel();
    }

    public void DeleteSelectedModelProtocolRule()
    {
        var provider = CurrentModelProtocolProvider();
        if (provider is null || provider.Rules.Count == 0)
        {
            ModelProtocolStatusText.Value = "没有可删除的通配规则。";
            UpdateContextPanel();
            return;
        }

        var index = Math.Clamp(ModelProtocolRuleIndex.Value, 0, provider.Rules.Count - 1);
        provider.Rules.RemoveAt(index);
        LoadModelProtocolRuleIndex(provider.Rules.Count == 0 ? -1 : Math.Min(index, provider.Rules.Count - 1));
        ModelProtocolStatusText.Value = "已删除通配规则草稿；保存前不会写入配置。";
        UpdateContextPanel();
    }

    public void SaveModelProtocolMappings()
    {
        SyncModelProtocolDraftFromEditor();
        var provider = CurrentModelProtocolProvider();
        if (provider is null)
        {
            ModelProtocolStatusText.Value = "没有可保存的提供方协议适配。";
            UpdateContextPanel();
            return;
        }

        var invalidOverride = provider.Overrides.FirstOrDefault(static item => string.IsNullOrWhiteSpace(item.Name) || SplitProtocols(item.ProtocolsText).Count == 0);
        if (invalidOverride is not null)
        {
            ModelProtocolStatusText.Value = "精确模型覆写必须填写模型名，并至少提供一个协议。";
            UpdateContextPanel();
            return;
        }

        var invalidRule = provider.Rules.FirstOrDefault(static item => string.IsNullOrWhiteSpace(item.Match) || SplitProtocols(item.ProtocolsText).Count == 0);
        if (invalidRule is not null)
        {
            ModelProtocolStatusText.Value = "通配规则必须填写匹配表达式，并至少提供一个协议。";
            UpdateContextPanel();
            return;
        }

        var result = SaveProviderProtocolMappingsModuleFile(provider);

        if (!result.Applied)
        {
            ModelProtocolStatusText.Value = string.Join(Environment.NewLine, result.Issues.Select(static issue => issue.Message));
            UpdateContextPanel();
            return;
        }

        var referenceResult = SaveActiveProviderInstanceReference();
        if (referenceResult.Applied)
        {
            selectedModelProtocolProviderId = provider.Id;
            Refresh();
            ModelProtocolStatusText.Value = $"已保存模型协议适配：{provider.Id}；模块文件：{result.TargetPath}";
            UpdateContextPanel();
            return;
        }

        ModelProtocolStatusText.Value = string.Join(Environment.NewLine, referenceResult.Issues.Select(static issue => issue.Message));
        UpdateContextPanel();
    }

    public void SelectDefaultModelProtocolRuleSetIndex(int index)
    {
        if (defaultModelProtocolRuleSetItems.Count == 0)
        {
            selectedDefaultModelProtocolRuleSetIndex = -1;
            selectedDefaultModelProtocolRuleIndex = -1;
            DefaultModelProtocolRuleStatusText.Value = "尚未配置默认模型协议规则集。";
            return;
        }

        if (selectedDefaultModelProtocolRuleSetIndex >= 0)
        {
            SyncDefaultModelProtocolRuleSetDraftFromEditor();
        }
        var ruleSetIndex = Math.Clamp(index, 0, defaultModelProtocolRuleSetItems.Count - 1);
        var ruleSet = defaultModelProtocolRuleSetItems[ruleSetIndex];
        var ruleIndex = selectedDefaultModelProtocolRuleIndex < 0
            ? DefaultModelProtocolRuleIndex.Value
            : selectedDefaultModelProtocolRuleIndex;
        selectedDefaultModelProtocolRuleSetIndex = ruleSetIndex;
        selectedDefaultModelProtocolRuleSetId = ruleSet.Id;
        DefaultModelProtocolRuleSetIndex.Value = ruleSetIndex;
        DefaultModelProtocolRuleSetId.Value = ruleSet.Id;
        DefaultModelProtocolRuleSetDisplayName.Value = ruleSet.DisplayName;
        DefaultModelProtocolRuleSetDescription.Value = ruleSet.Description;
        LoadDefaultModelProtocolRuleIndex(ruleSet.Rules.Count == 0 ? -1 : Math.Clamp(ruleIndex, 0, ruleSet.Rules.Count - 1));
        DefaultModelProtocolRuleStatusText.Value = $"正在编辑默认协议规则集：{ruleSet.Id}";
        UpdateContextPanel();
    }

    public void SelectDefaultModelProtocolRuleIndex(int index)
    {
        SyncDefaultModelProtocolRuleSetDraftFromEditor();
        LoadDefaultModelProtocolRuleIndex(index);
    }

    public void BeginNewDefaultModelProtocolRule()
    {
        var ruleSet = CurrentDefaultModelProtocolRuleSet();
        if (ruleSet is null)
        {
            DefaultModelProtocolRuleStatusText.Value = "请先选择默认协议规则集。";
            UpdateContextPanel();
            return;
        }

        SyncDefaultModelProtocolRuleSetDraftFromEditor();
        ruleSet.Rules.Add(new ModelProtocolRuleDraft("model-*", "openai_chat_completions, anthropic_messages"));
        LoadDefaultModelProtocolRuleIndex(ruleSet.Rules.Count - 1);
        DefaultModelProtocolRuleStatusText.Value = "已新增默认通配规则草稿；保存前不会写入配置。";
        UpdateContextPanel();
    }

    public void DeleteSelectedDefaultModelProtocolRule()
    {
        var ruleSet = CurrentDefaultModelProtocolRuleSet();
        if (ruleSet is null || ruleSet.Rules.Count == 0)
        {
            DefaultModelProtocolRuleStatusText.Value = "没有可删除的默认通配规则。";
            UpdateContextPanel();
            return;
        }

        var index = Math.Clamp(
            selectedDefaultModelProtocolRuleIndex < 0 ? DefaultModelProtocolRuleIndex.Value : selectedDefaultModelProtocolRuleIndex,
            0,
            ruleSet.Rules.Count - 1);
        ruleSet.Rules.RemoveAt(index);
        LoadDefaultModelProtocolRuleIndex(ruleSet.Rules.Count == 0 ? -1 : Math.Min(index, ruleSet.Rules.Count - 1));
        DefaultModelProtocolRuleStatusText.Value = "已删除默认通配规则草稿；保存前不会写入配置。";
        UpdateContextPanel();
    }

    public void RestoreDefaultModelProtocolRules()
    {
        var ruleSet = CurrentDefaultModelProtocolRuleSet();
        if (ruleSet is null)
        {
            return;
        }

        var defaults = CreateDefaultModelProtocolRuleSetDraft();
        ruleSet.Rules.Clear();
        ruleSet.Rules.AddRange(defaults.Rules.Select(static rule => rule.Clone()));
        LoadDefaultModelProtocolRuleIndex(ruleSet.Rules.Count == 0 ? -1 : 0);
        DefaultModelProtocolRuleStatusText.Value = "已恢复内置默认规则草稿；保存前不会写入配置。";
        UpdateContextPanel();
    }

    public void SaveDefaultModelProtocolRuleSet()
    {
        SyncDefaultModelProtocolRuleSetDraftFromEditor();
        var ruleSet = CurrentDefaultModelProtocolRuleSet();
        if (ruleSet is null)
        {
            DefaultModelProtocolRuleStatusText.Value = "没有可保存的默认协议规则集。";
            UpdateContextPanel();
            return;
        }

        ruleSet.Id = DefaultModelProtocolRuleSetId.Value.Trim();
        ruleSet.DisplayName = DefaultModelProtocolRuleSetDisplayName.Value.Trim();
        ruleSet.Description = DefaultModelProtocolRuleSetDescription.Value.Trim();
        if (!IsValidModelRouteSetId(ruleSet.Id))
        {
            DefaultModelProtocolRuleStatusText.Value = "规则集 ID 只能包含字母、数字、下划线或短横线。";
            UpdateContextPanel();
            return;
        }

        var invalidRule = ruleSet.Rules.FirstOrDefault(static item => string.IsNullOrWhiteSpace(item.Match) || SplitProtocols(item.ProtocolsText).Count == 0);
        if (invalidRule is not null)
        {
            DefaultModelProtocolRuleStatusText.Value = "默认通配规则必须填写匹配表达式，并至少提供一个协议。";
            UpdateContextPanel();
            return;
        }

        var moduleResult = SaveDefaultModelProtocolRuleSetModuleFile(ruleSet);
        if (!moduleResult.Applied)
        {
            DefaultModelProtocolRuleStatusText.Value = string.Join(Environment.NewLine, moduleResult.Issues.Select(static issue => issue.Message));
            UpdateContextPanel();
            return;
        }

        var referenceResult = SaveActiveDefaultModelProtocolRuleSetReference(ruleSet);
        if (referenceResult.Applied)
        {
            selectedDefaultModelProtocolRuleSetId = ruleSet.Id;
            Refresh();
            DefaultModelProtocolRuleStatusText.Value = $"已保存默认协议规则集：{ruleSet.Id}；模块文件：{moduleResult.TargetPath}";
            UpdateContextPanel();
            return;
        }

        DefaultModelProtocolRuleStatusText.Value = string.Join(Environment.NewLine, referenceResult.Issues.Select(static issue => issue.Message));
        UpdateContextPanel();
    }

    private ModelProtocolProviderDraft? CurrentModelProtocolProvider()
    {
        if (modelProtocolItems.Count == 0)
        {
            return null;
        }

        var index = Math.Clamp(ModelProtocolProviderIndex.Value, 0, modelProtocolItems.Count - 1);
        return modelProtocolItems[index];
    }

    private ModelProtocolRuleSetDraft? CurrentDefaultModelProtocolRuleSet()
    {
        if (defaultModelProtocolRuleSetItems.Count == 0)
        {
            return null;
        }

        var index = selectedDefaultModelProtocolRuleSetIndex < 0
            ? Math.Clamp(DefaultModelProtocolRuleSetIndex.Value, 0, defaultModelProtocolRuleSetItems.Count - 1)
            : Math.Clamp(selectedDefaultModelProtocolRuleSetIndex, 0, defaultModelProtocolRuleSetItems.Count - 1);
        return defaultModelProtocolRuleSetItems[index];
    }

    private void LoadDefaultModelProtocolRuleIndex(int index)
    {
        var ruleSet = CurrentDefaultModelProtocolRuleSet();
        if (ruleSet is null || ruleSet.Rules.Count == 0 || index < 0)
        {
            selectedDefaultModelProtocolRuleIndex = -1;
            DefaultModelProtocolRuleIndex.Value = 0;
            DefaultModelProtocolRuleLabels = [];
            DefaultModelProtocolRuleMatch.Value = string.Empty;
            DefaultModelProtocolRuleProtocols.Value = string.Empty;
            return;
        }

        var ruleIndex = Math.Clamp(index, 0, ruleSet.Rules.Count - 1);
        var item = ruleSet.Rules[ruleIndex];
        DefaultModelProtocolRuleLabels = BuildModelProtocolRuleLabels(ruleSet.Rules);
        selectedDefaultModelProtocolRuleIndex = ruleIndex;
        DefaultModelProtocolRuleIndex.Value = ruleIndex;
        DefaultModelProtocolRuleMatch.Value = item.Match;
        DefaultModelProtocolRuleProtocols.Value = item.ProtocolsText;
    }

    private void SyncDefaultModelProtocolRuleSetDraftFromEditor()
    {
        var ruleSet = CurrentDefaultModelProtocolRuleSet();
        if (ruleSet is null)
        {
            return;
        }

        ruleSet.Id = DefaultModelProtocolRuleSetId.Value.Trim();
        ruleSet.DisplayName = DefaultModelProtocolRuleSetDisplayName.Value.Trim();
        ruleSet.Description = DefaultModelProtocolRuleSetDescription.Value.Trim();
        if (ruleSet.Rules.Count > 0)
        {
            var ruleIndex = Math.Clamp(
                selectedDefaultModelProtocolRuleIndex < 0 ? DefaultModelProtocolRuleIndex.Value : selectedDefaultModelProtocolRuleIndex,
                0,
                ruleSet.Rules.Count - 1);
            ruleSet.Rules[ruleIndex].Match = DefaultModelProtocolRuleMatch.Value.Trim();
            ruleSet.Rules[ruleIndex].ProtocolsText = JoinProtocols(SplitProtocols(DefaultModelProtocolRuleProtocols.Value));
            DefaultModelProtocolRuleLabels = BuildModelProtocolRuleLabels(ruleSet.Rules);
        }
    }

    private void LoadModelProtocolOverrideIndex(int index)
    {
        var provider = CurrentModelProtocolProvider();
        if (provider is null || provider.Overrides.Count == 0 || index < 0)
        {
            ModelProtocolOverrideIndex.Value = 0;
            ModelProtocolOverrideLabels = [];
            ModelProtocolOverrideName.Value = string.Empty;
            ModelProtocolOverrideProtocols.Value = string.Empty;
            RefreshModelProtocolModelCandidates(provider);
            return;
        }

        var overrideIndex = Math.Clamp(index, 0, provider.Overrides.Count - 1);
        var item = provider.Overrides[overrideIndex];
        ModelProtocolOverrideLabels = BuildModelProtocolOverrideLabels(provider);
        ModelProtocolOverrideIndex.Value = overrideIndex;
        ModelProtocolOverrideName.Value = item.Name;
        ModelProtocolOverrideProtocols.Value = item.ProtocolsText;
        RefreshModelProtocolModelCandidates(provider);
    }

    private void LoadModelProtocolRuleIndex(int index)
    {
        var provider = CurrentModelProtocolProvider();
        if (provider is null || provider.Rules.Count == 0 || index < 0)
        {
            ModelProtocolRuleIndex.Value = 0;
            ModelProtocolRuleLabels = [];
            ModelProtocolRuleMatch.Value = string.Empty;
            ModelProtocolRuleProtocols.Value = string.Empty;
            return;
        }

        var ruleIndex = Math.Clamp(index, 0, provider.Rules.Count - 1);
        var item = provider.Rules[ruleIndex];
        ModelProtocolRuleLabels = BuildModelProtocolRuleLabels(provider);
        ModelProtocolRuleIndex.Value = ruleIndex;
        ModelProtocolRuleMatch.Value = item.Match;
        ModelProtocolRuleProtocols.Value = item.ProtocolsText;
    }

    private void RefreshModelProtocolModelCandidates(ModelProtocolProviderDraft? provider, string? emptyMessage = null)
    {
        if (provider is null)
        {
            ModelProtocolModelLabels = [];
            ModelProtocolModelListItems = [];
            ModelProtocolModelIndex.Value = 0;
            return;
        }

        ModelProtocolModelLabels = provider.Models.ToArray();
        ModelProtocolModelListItems = provider.Models.ToArray();
        if (provider.Models.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(emptyMessage))
            {
                ModelProtocolStatusText.Value = emptyMessage;
            }

            ModelProtocolModelIndex.Value = 0;
            return;
        }

        var overrideName = ModelProtocolOverrideName.Value.Trim();
        var index = provider.Models.FindIndex(model => string.Equals(model, overrideName, StringComparison.OrdinalIgnoreCase));
        ModelProtocolModelIndex.Value = index < 0 ? 0 : index;
    }

    private void SyncModelProtocolDraftFromEditor()
    {
        var provider = CurrentModelProtocolProvider();
        if (provider is null)
        {
            return;
        }

        if (provider.Overrides.Count > 0)
        {
            var overrideIndex = Math.Clamp(ModelProtocolOverrideIndex.Value, 0, provider.Overrides.Count - 1);
            provider.Overrides[overrideIndex].Name = ModelProtocolOverrideName.Value.Trim();
            provider.Overrides[overrideIndex].ProtocolsText = JoinProtocols(SplitProtocols(ModelProtocolOverrideProtocols.Value));
            ModelProtocolOverrideLabels = BuildModelProtocolOverrideLabels(provider);
        }

        if (provider.Rules.Count > 0)
        {
            var ruleIndex = Math.Clamp(ModelProtocolRuleIndex.Value, 0, provider.Rules.Count - 1);
            provider.Rules[ruleIndex].Match = ModelProtocolRuleMatch.Value.Trim();
            provider.Rules[ruleIndex].ProtocolsText = JoinProtocols(SplitProtocols(ModelProtocolRuleProtocols.Value));
            ModelProtocolRuleLabels = BuildModelProtocolRuleLabels(provider);
        }
    }

    private static IReadOnlyList<string> BuildModelProtocolOverrideLabels(ModelProtocolProviderDraft provider)
        => provider.Overrides.Select((item, index) =>
        {
            var name = string.IsNullOrWhiteSpace(item.Name) ? "未填写模型名" : item.Name;
            return $"{index + 1}. {name} -> {item.ProtocolsText}";
        }).ToArray();

    private static IReadOnlyList<string> BuildModelProtocolRuleLabels(ModelProtocolProviderDraft provider)
        => BuildModelProtocolRuleLabels(provider.Rules);

    private static IReadOnlyList<string> BuildModelProtocolRuleLabels(IReadOnlyList<ModelProtocolRuleDraft> rules)
        => rules.Select((item, index) => $"{index + 1}. {item.Match} -> {item.ProtocolsText}").ToArray();

    private static StructuredValue BuildModelProtocolOverridesValue(ModelProtocolProviderDraft provider)
        => StructuredValue.FromArray(provider.Overrides
            .Where(static item => !string.IsNullOrWhiteSpace(item.Name))
            .Select(static item => StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["name"] = StructuredValue.FromString(item.Name.Trim()),
                ["protocols"] = StructuredValue.FromArray(SplitProtocols(item.ProtocolsText).Select(StructuredValue.FromString).ToArray()),
            }))
            .ToArray());

    private static StructuredValue BuildModelProtocolRulesValue(ModelProtocolProviderDraft provider)
        => BuildModelProtocolRulesValue(provider.Rules);

    private static StructuredValue BuildModelProtocolRulesValue(IReadOnlyList<ModelProtocolRuleDraft> rules)
        => StructuredValue.FromArray(rules
            .Where(static item => !string.IsNullOrWhiteSpace(item.Match))
            .Select(static item => StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["match"] = StructuredValue.FromString(item.Match.Trim()),
                ["protocols"] = StructuredValue.FromArray(SplitProtocols(item.ProtocolsText).Select(StructuredValue.FromString).ToArray()),
            }))
            .ToArray());

    public void TestProviderConnection()
    {
        try
        {
            var result = FetchProviderModels();
            ProviderModelListItems = result.Models.Count == 0
                ? ["连接成功，但 endpoint 返回 JSON 中未识别到模型 id。"]
                : result.Models.Take(200).ToArray();
            ProviderStatusText.Value = $"连接成功：{result.Endpoint} 返回 {result.Models.Count} 个模型。";
            UpdateContextPanel();
        }
        catch (Exception ex)
        {
            ProviderModelListItems = ["连接失败，未更新模型列表。"];
            ProviderStatusText.Value = $"连接失败：{ex.Message}";
            UpdateContextPanel();
        }
    }

    private void ClearProviderModelList()
        => ProviderModelListItems = [];

    private ProviderModelProbeResult FetchProviderModels()
    {
        var providerId = ProviderId.Value.Trim();
        var baseUrl = ProviderBaseUrl.Value.Trim();
        var protocol = ProviderProtocols[Math.Clamp(ProviderProtocolIndex.Value, 0, ProviderProtocols.Count - 1)];
        var apiKeyEnv = ProviderApiKeyEnv.Value.Trim();
        return FetchProviderModels(new ProviderConfigItem(providerId, baseUrl, apiKeyEnv, protocol));
    }

    private ProviderModelProbeResult FetchProviderModels(ProviderConfigItem provider)
    {
        var providerId = provider.Id.Trim();
        var baseUrl = provider.BaseUrl.Trim();
        if (string.IsNullOrWhiteSpace(providerId))
        {
            throw new InvalidOperationException("提供方 id 不能为空。");
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException("Base URL 必须是完整 URL，例如 https://api.openai.com。");
        }

        var protocol = string.IsNullOrWhiteSpace(provider.DefaultProtocol) ? "auto" : provider.DefaultProtocol.Trim();
        var apiKeyEnv = provider.ApiKeyEnv.Trim();
        var apiKey = string.IsNullOrWhiteSpace(apiKeyEnv) ? null : Environment.GetEnvironmentVariable(apiKeyEnv);
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
        };

        httpClient.DefaultRequestHeaders.Accept.Clear();
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        ConfigureProviderAuthorization(httpClient, protocol, apiKey);

        var attempted = new List<string>();
        var lastFailure = string.Empty;
        foreach (var endpoint in BuildModelRouteSetEndpoints(baseUri, protocol))
        {
            attempted.Add(endpoint.ToString());
            try
            {
                using var response = httpClient.GetAsync(endpoint).GetAwaiter().GetResult();
                var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    lastFailure = $"{endpoint} -> HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                    continue;
                }

                using var document = JsonDocument.Parse(body);
                var models = ParseModelIds(document.RootElement);
                return new ProviderModelProbeResult(endpoint, models);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                lastFailure = $"{endpoint} -> {ex.Message}";
            }
        }

        var envHint = string.IsNullOrWhiteSpace(apiKeyEnv) ? "未配置 api_key_env" : $"api_key_env={apiKeyEnv}";
        throw new InvalidOperationException($"所有 models endpoint 均失败；提供方={providerId}，{envHint}，已尝试：{string.Join(", ", attempted)}。最后错误：{lastFailure}");
    }

    private static void ConfigureProviderAuthorization(HttpClient httpClient, string protocol, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        if (protocol.Contains("google", StringComparison.OrdinalIgnoreCase))
        {
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("x-goog-api-key", apiKey);
            return;
        }

        if (protocol.Contains("anthropic", StringComparison.OrdinalIgnoreCase))
        {
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("x-api-key", apiKey);
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            return;
        }

        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    private static IReadOnlyList<Uri> BuildModelRouteSetEndpoints(Uri baseUri, string protocol)
    {
        var candidates = protocol.Contains("google", StringComparison.OrdinalIgnoreCase)
            ? new[] { BuildEndpoint(baseUri, "v1beta"), BuildEndpoint(baseUri, null) }
            : new[] { BuildEndpoint(baseUri, "v1"), BuildEndpoint(baseUri, null) };

        return candidates
            .DistinctBy(static uri => uri.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Uri BuildEndpoint(Uri baseUri, string? version)
    {
        var builder = new UriBuilder(baseUri);
        var path = builder.Path.TrimEnd('/');
        if (path.EndsWith("/models", StringComparison.OrdinalIgnoreCase) || string.Equals(path, "models", StringComparison.OrdinalIgnoreCase))
        {
            builder.Path = path;
            return builder.Uri;
        }

        var targetPath = path.Trim('/');
        if (!string.IsNullOrWhiteSpace(version)
            && !targetPath.EndsWith(version, StringComparison.OrdinalIgnoreCase))
        {
            targetPath = string.IsNullOrWhiteSpace(targetPath) ? version : $"{targetPath}/{version}";
        }

        targetPath = string.IsNullOrWhiteSpace(targetPath) ? "models" : $"{targetPath}/models";
        builder.Path = targetPath;
        return builder.Uri;
    }

    private static IReadOnlyList<string> ParseModelIds(JsonElement root)
    {
        var models = new List<string>();
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (TryReadString(item, "id", out var id))
                {
                    models.Add(id);
                }
            }
        }

        if (root.TryGetProperty("models", out var googleModels) && googleModels.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in googleModels.EnumerateArray())
            {
                if (TryReadString(item, "name", out var name))
                {
                    models.Add(name);
                }
            }
        }

        return models
            .Where(static model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryReadString(JsonElement item, string propertyName, out string value)
    {
        value = string.Empty;
        if (!item.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private void BuildProviderItems()
    {
        var previousProviderId = selectedProviderId ?? ProviderId.Value;
        providerItems.Clear();

        var providerIds = allFields
            .Where(static row => row.Key.StartsWith("providers.", StringComparison.OrdinalIgnoreCase))
            .Select(static row => row.Key.Split('.', 3, StringSplitOptions.RemoveEmptyEntries))
            .Where(static segments => segments.Length >= 2)
            .Select(static segments => segments[1])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var id in providerIds)
        {
            var prefix = $"providers.{id}.";
            string Read(string suffix)
                => allFields.FirstOrDefault(row => string.Equals(row.Key, prefix + suffix, StringComparison.OrdinalIgnoreCase))?.CurrentValue ?? string.Empty;

            providerItems.Add(new ProviderConfigItem(
                id,
                Read("base_url"),
                Read("api_key_env"),
                Read("default_protocol")));
        }

        ProviderItemLabels = providerItems.Select(static provider => provider.Id).ToArray();
        if (providerItems.Count == 0)
        {
            BeginNewProvider();
            ProviderStatusText.Value = "尚未配置提供方；填写后点击保存。";
            return;
        }

        var index = providerItems.FindIndex(provider => string.Equals(provider.Id, previousProviderId, StringComparison.OrdinalIgnoreCase));
        SelectProviderIndex(index < 0 ? 0 : index);
    }

    private void BuildModelProtocolMappingItems(IReadOnlyDictionary<string, ConfigurationFieldValue> values)
    {
        var previousProviderId = selectedModelProtocolProviderId
            ?? CurrentModelProtocolProvider()?.Id
            ?? selectedProviderId
            ?? ProviderId.Value;
        var previousModels = modelProtocolItems.ToDictionary(static provider => provider.Id, static provider => provider.Models.ToArray(), StringComparer.OrdinalIgnoreCase);
        modelProtocolItems.Clear();

        foreach (var provider in providerItems)
        {
            var overrides = ReadModelProtocolOverrides(values.TryGetValue($"providers.{provider.Id}.model_overrides", out var overridesValue) ? overridesValue.Value : null);
            var rules = ReadModelProtocolRules(values.TryGetValue($"providers.{provider.Id}.protocol_rules", out var rulesValue) ? rulesValue.Value : null);
            var draft = new ModelProtocolProviderDraft(provider.Id, overrides, rules);
            if (previousModels.TryGetValue(provider.Id, out var models))
            {
                draft.Models.AddRange(models);
            }

            modelProtocolItems.Add(draft);
        }

        ModelProtocolProviderLabels = modelProtocolItems.Select(static provider => provider.Id).ToArray();
        if (modelProtocolItems.Count == 0)
        {
            ModelProtocolProviderIndex.Value = 0;
            ModelProtocolModelIndex.Value = 0;
            ModelProtocolOverrideLabels = [];
            ModelProtocolRuleLabels = [];
            ModelProtocolModelLabels = [];
            ModelProtocolModelListItems = [];
            ModelProtocolOverrideName.Value = string.Empty;
            ModelProtocolOverrideProtocols.Value = string.Empty;
            ModelProtocolRuleMatch.Value = string.Empty;
            ModelProtocolRuleProtocols.Value = string.Empty;
            ModelProtocolStatusText.Value = "尚未配置提供方；请先在“提供方实例”中创建 endpoint。";
            return;
        }

        var index = modelProtocolItems.FindIndex(provider => string.Equals(provider.Id, previousProviderId, StringComparison.OrdinalIgnoreCase));
        SelectModelProtocolProviderIndex(index < 0 ? 0 : index, syncCurrentEditor: false);
    }

    private int ModelProtocolMappingCount()
        => modelProtocolItems.Sum(static provider => provider.Overrides.Count + provider.Rules.Count);

    private void BuildDefaultModelProtocolRuleSetItems(IReadOnlyDictionary<string, ConfigurationFieldValue> values)
    {
        var previousRuleSetId = selectedDefaultModelProtocolRuleSetId
            ?? CurrentDefaultModelProtocolRuleSet()?.Id
            ?? ReadProjectionString(values, "model_protocol_rule_set");
        if (string.IsNullOrWhiteSpace(previousRuleSetId))
        {
            previousRuleSetId = "default";
        }

        defaultModelProtocolRuleSetItems.Clear();
        var ruleSetIds = values.Keys
            .Where(static key => key.StartsWith("model_protocol_rule_sets.", StringComparison.OrdinalIgnoreCase))
            .Select(static key => key.Split('.', 3, StringSplitOptions.RemoveEmptyEntries))
            .Where(static segments => segments.Length >= 3)
            .Select(static segments => segments[1])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var ruleSetId in ruleSetIds)
        {
            var displayName = ReadProjectionString(values, $"model_protocol_rule_sets.{ruleSetId}.display_name");
            var description = ReadProjectionString(values, $"model_protocol_rule_sets.{ruleSetId}.description");
            var rules = ReadModelProtocolRules(values.TryGetValue($"model_protocol_rule_sets.{ruleSetId}.rules", out var rulesValue) ? rulesValue.Value : null);
            defaultModelProtocolRuleSetItems.Add(new ModelProtocolRuleSetDraft(ruleSetId, displayName, description, rules));
        }

        if (defaultModelProtocolRuleSetItems.Count == 0)
        {
            defaultModelProtocolRuleSetItems.Add(CreateDefaultModelProtocolRuleSetDraft());
        }

        DefaultModelProtocolRuleSetLabels = defaultModelProtocolRuleSetItems.Select(static ruleSet => ruleSet.DisplayLabel).ToArray();
        var index = defaultModelProtocolRuleSetItems.FindIndex(ruleSet => string.Equals(ruleSet.Id, previousRuleSetId, StringComparison.OrdinalIgnoreCase));
        SelectDefaultModelProtocolRuleSetIndex(index < 0 ? 0 : index);
    }

    private int DefaultModelProtocolRuleSetCount()
        => defaultModelProtocolRuleSetItems.Sum(static ruleSet => ruleSet.Rules.Count);

    private void BuildModelRouteSetItems(IReadOnlyDictionary<string, ConfigurationFieldValue> values)
    {
        var previousRouteSetId = selectedModelRouteSetId ?? ModelRouteSetId.Value;
        modelRouteSetItems.Clear();
        ModelRouteSetProviderOptions = BuildModelRouteSetProviderOptions(values);
        ModelRouteSetModelOptions = BuildModelRouteSetModelOptions(values);

        var routeSetIds = values.Keys
            .Where(static key => key.StartsWith("model_route_sets.", StringComparison.OrdinalIgnoreCase))
            .Select(static key => key.Split('.', 3, StringSplitOptions.RemoveEmptyEntries))
            .Where(static segments => segments.Length >= 3)
            .Select(static segments => segments[1])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var routeSetId in routeSetIds)
        {
            var displayName = ReadProjectionString(values, $"model_route_sets.{routeSetId}.display_name");
            var description = ReadProjectionString(values, $"model_route_sets.{routeSetId}.description");
            var routes = NormalizeModelRouteSetRoutes(ReadModelRouteSetRoutes(values.TryGetValue($"model_route_sets.{routeSetId}.routes", out var routesValue) ? routesValue.Value : null));

            modelRouteSetItems.Add(new ModelRouteSetConfigItem(routeSetId, displayName, description, routes));
        }

        if (modelRouteSetItems.Count == 0)
        {
            modelRouteSetItems.Add(new ModelRouteSetConfigItem(
                "default",
                "Default Model Route Scheme",
                "新建配置应通过 model_route_set 引用模块化 route set；缺少 route set 时运行时会暴露配置缺口。",
                CreateDefaultModelRouteSetRoutes()));
        }

        ModelRouteSetLabels = modelRouteSetItems.Select(static routeSet => routeSet.DisplayLabel).ToArray();
        var index = modelRouteSetItems.FindIndex(routeSet => string.Equals(routeSet.Id, previousRouteSetId, StringComparison.OrdinalIgnoreCase));
        SelectModelRouteSetIndex(index < 0 ? 0 : index);
    }

    private IReadOnlyList<string> BuildModelRouteSetProviderOptions(IReadOnlyDictionary<string, ConfigurationFieldValue> values)
    {
        var providers = providerItems.Select(static provider => provider.Id).ToList();

        return providers
            .Where(static provider => !string.IsNullOrWhiteSpace(provider))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .DefaultIfEmpty("openai")
            .ToArray();
    }

    private IReadOnlyList<string> BuildModelRouteSetModelOptions(IReadOnlyDictionary<string, ConfigurationFieldValue> values)
    {
        var models = new List<string>();

        foreach (var pair in values)
        {
            if (string.Equals(pair.Key, "model", StringComparison.OrdinalIgnoreCase))
            {
                var model = FormatValue(pair.Value.Value);
                if (!string.IsNullOrWhiteSpace(model))
                {
                    models.Add(model);
                }
            }

            if (pair.Key.StartsWith("models.", StringComparison.OrdinalIgnoreCase)
                && pair.Key.EndsWith(".name", StringComparison.OrdinalIgnoreCase))
            {
                var model = FormatValue(pair.Value.Value);
                if (!string.IsNullOrWhiteSpace(model))
                {
                    models.Add(model);
                }
            }

            if (pair.Key.StartsWith("model_route_sets.", StringComparison.OrdinalIgnoreCase)
                && pair.Key.EndsWith(".routes", StringComparison.OrdinalIgnoreCase)
                && pair.Value.Value is { Kind: StructuredValueKind.Array } routes)
            {
                foreach (var route in routes.Items)
                {
                    if (route.Kind != StructuredValueKind.Object
                        || !route.TryGetProperty("candidates", out var candidates)
                        || candidates?.Kind != StructuredValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var candidate in candidates.Items)
                    {
                        if (candidate.Kind == StructuredValueKind.Object
                            && candidate.TryGetProperty("model", out var modelValue)
                            && !string.IsNullOrWhiteSpace(modelValue?.StringValue))
                        {
                            models.Add(modelValue.StringValue);
                        }
                    }
                }
            }
        }

        return models
            .Where(static model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .DefaultIfEmpty("gpt-5")
            .ToArray();
    }

    private static string ReadProjectionString(IReadOnlyDictionary<string, ConfigurationFieldValue> values, string key)
        => values.TryGetValue(key, out var value) ? FormatValue(value.Value) : string.Empty;

    private static List<ModelProtocolOverrideDraft> ReadModelProtocolOverrides(StructuredValue? value)
    {
        var overrides = new List<ModelProtocolOverrideDraft>();
        if (value?.Kind != StructuredValueKind.Array)
        {
            return overrides;
        }

        foreach (var item in value.Items)
        {
            if (item.Kind != StructuredValueKind.Object)
            {
                continue;
            }

            var name = ReadObjectString(item, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            overrides.Add(new ModelProtocolOverrideDraft(name, JoinProtocols(ReadProtocolPriority(item))));
        }

        return overrides;
    }

    private static List<ModelProtocolRuleDraft> ReadModelProtocolRules(StructuredValue? value)
    {
        var rules = new List<ModelProtocolRuleDraft>();
        if (value?.Kind != StructuredValueKind.Array)
        {
            return rules;
        }

        foreach (var item in value.Items)
        {
            if (item.Kind != StructuredValueKind.Object)
            {
                continue;
            }

            var match = ReadObjectString(item, "match");
            if (string.IsNullOrWhiteSpace(match))
            {
                continue;
            }

            rules.Add(new ModelProtocolRuleDraft(match, JoinProtocols(ReadProtocolPriority(item))));
        }

        return rules;
    }

    private static ModelProtocolRuleSetDraft CreateDefaultModelProtocolRuleSetDraft()
        => new(
            "default",
            "Default Model Protocol Rules",
            "Common model-family to wire protocol priority rules.",
            [
                new("anthropic/claude*", "anthropic_messages, openai_chat_completions"),
                new("claude*", "anthropic_messages, openai_chat_completions"),
                new("openai/gpt-*", "openai_responses, openai_chat_completions"),
                new("gpt-*", "openai_responses, openai_chat_completions"),
                new("o1*", "openai_responses, openai_chat_completions"),
                new("o3*", "openai_responses, openai_chat_completions"),
                new("o4*", "openai_responses, openai_chat_completions"),
                new("google/gemini*", "google_generative, openai_chat_completions"),
                new("gemini*", "google_generative, openai_chat_completions"),
                new("deepseek*", "anthropic_messages, openai_chat_completions"),
                new("qwen*", "anthropic_messages, openai_chat_completions"),
                new("kimi*", "anthropic_messages, openai_chat_completions"),
                new("moonshot*", "anthropic_messages, openai_chat_completions"),
                new("glm*", "anthropic_messages, openai_chat_completions"),
                new("minimax*", "anthropic_messages, openai_chat_completions"),
                new("mimo*", "anthropic_messages, openai_chat_completions"),
                new("grok*", "anthropic_messages, openai_chat_completions"),
                new("baichuan*", "anthropic_messages, openai_chat_completions"),
                new("doubao*", "anthropic_messages, openai_chat_completions"),
                new("mistral*", "openai_chat_completions, anthropic_messages"),
                new("mixtral*", "openai_chat_completions, anthropic_messages"),
                new("llama*", "openai_chat_completions, anthropic_messages"),
                new("yi*", "openai_chat_completions, anthropic_messages"),
            ]);

    private static IReadOnlyList<string> ReadProtocolPriority(StructuredValue value)
    {
        var protocols = ReadObjectStringArray(value, "protocols");
        if (protocols.Count > 0)
        {
            return protocols;
        }

        var protocol = ReadObjectString(value, "protocol");
        return string.IsNullOrWhiteSpace(protocol) ? [] : [protocol];
    }

    private List<ModelRouteSetRouteDraft> ReadModelRouteSetRoutes(StructuredValue? routesValue)
    {
        var routes = new List<ModelRouteSetRouteDraft>();
        if (routesValue?.Kind != StructuredValueKind.Array)
        {
            return routes;
        }

        foreach (var routeValue in routesValue.Items)
        {
            if (routeValue.Kind != StructuredValueKind.Object)
            {
                continue;
            }

            var kind = ReadObjectString(routeValue, "kind");
            if (string.IsNullOrWhiteSpace(kind))
            {
                continue;
            }

            var route = new ModelRouteSetRouteDraft(kind, ReadObjectString(routeValue, "fallback"));
            if (routeValue.TryGetProperty("candidates", out var candidatesValue)
                && candidatesValue?.Kind == StructuredValueKind.Array)
            {
                foreach (var candidateValue in candidatesValue.Items)
                {
                    if (candidateValue.Kind != StructuredValueKind.Object)
                    {
                        continue;
                    }

                    var provider = ReadObjectString(candidateValue, "provider");
                    var model = ReadObjectString(candidateValue, "model");
                    if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(model))
                    {
                        continue;
                    }

                    route.Candidates.Add(new ModelRouteSetCandidateDraft(
                        provider,
                        model,
                        ReadObjectString(candidateValue, "protocol"),
                        ReadObjectStringArray(candidateValue, "capabilities")));
                }
            }

            if (route.Candidates.Count == 0)
            {
                route.Candidates.Add(CreateDefaultModelRouteSetCandidate());
            }

            routes.Add(route);
        }

        return routes;
    }

    private IReadOnlyList<ModelRouteSetRouteDraft> NormalizeModelRouteSetRoutes(IReadOnlyList<ModelRouteSetRouteDraft> routes)
    {
        var normalized = new List<ModelRouteSetRouteDraft>();
        foreach (var routeKind in TianShuModelRouteSetDefaults.DefaultRouteKinds)
        {
            var configuredRoute = routes.FirstOrDefault(route => string.Equals(route.Kind, routeKind, StringComparison.OrdinalIgnoreCase));
            normalized.Add(configuredRoute?.Clone() ?? CreateDefaultModelRouteSetRoute(routeKind));
        }

        return normalized;
    }

    private static string ReadObjectString(StructuredValue value, string propertyName)
        => value.TryGetProperty(propertyName, out var property) ? FormatValue(property) : string.Empty;

    private static IReadOnlyList<string> ReadObjectStringArray(StructuredValue value, string propertyName)
    {
        if (!value.TryGetProperty(propertyName, out var property) || property?.Kind != StructuredValueKind.Array)
        {
            return [];
        }

        return property.Items
            .Select(FormatValue)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
    }

    private static IReadOnlyList<string> SplitProtocols(string value)
        => value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static protocol => !string.IsNullOrWhiteSpace(protocol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string JoinProtocols(IReadOnlyList<string> protocols)
        => string.Join(", ", protocols);

    private ModelRouteSetRouteDraft CreateDefaultModelRouteSetRoute(string kind)
    {
        var route = new ModelRouteSetRouteDraft(kind, string.Empty);
        route.Candidates.Add(CreateDefaultModelRouteSetCandidate());
        return route;
    }

    private IReadOnlyList<ModelRouteSetRouteDraft> CreateDefaultModelRouteSetRoutes()
        => TianShuModelRouteSetDefaults.DefaultRouteKinds
            .Select(CreateDefaultModelRouteSetRoute)
            .ToArray();

    private ModelRouteSetCandidateDraft CreateDefaultModelRouteSetCandidate()
        => new(
            ModelRouteSetProviderOptions.FirstOrDefault() ?? "openai",
            ModelRouteSetModelOptions.FirstOrDefault() ?? "gpt-5",
            string.Empty,
            []);

    public void SelectModelRouteSetIndex(int index)
    {
        if (modelRouteSetItems.Count == 0)
        {
            BeginNewModelRouteSet();
            return;
        }

        var routeSetIndex = Math.Clamp(index, 0, modelRouteSetItems.Count - 1);
        var routeSet = modelRouteSetItems[routeSetIndex];
        selectedModelRouteSetId = routeSet.Id;
        ModelRouteSetIndex.Value = routeSetIndex;
        ModelRouteSetId.Value = routeSet.Id;
        ModelRouteSetDisplayName.Value = routeSet.DisplayName;
        ModelRouteSetDescription.Value = routeSet.Description;
        LoadModelRouteSetRouteIndex(0);
        ModelRouteSetStatusText.Value = $"正在编辑模型路由方案：{routeSet.Id}";
        UpdateContextPanel();
    }

    public void BeginNewModelRouteSet()
    {
        selectedModelRouteSetId = null;
        var id = CreateUniqueModelRouteSetDraftId("route_set");
        var routeSet = new ModelRouteSetConfigItem(id, "New Model Route Scheme", string.Empty, CreateDefaultModelRouteSetRoutes());
        modelRouteSetItems.Add(routeSet);
        ModelRouteSetLabels = modelRouteSetItems.Select(static item => item.DisplayLabel).ToArray();
        SelectModelRouteSetIndex(modelRouteSetItems.Count - 1);
        ModelRouteSetStatusText.Value = "正在新建模型路由方案；保存前不会写入配置。";
    }

    public void CopySelectedModelRouteSetToDraft()
    {
        var source = CurrentModelRouteSet();
        if (source is null)
        {
            ModelRouteSetStatusText.Value = "没有可复制的模型路由方案。";
            return;
        }

        SyncModelRouteSetDraftFromEditor();
        var copy = source.Clone(CreateUniqueModelRouteSetDraftId(source.Id));
        modelRouteSetItems.Add(copy);
        selectedModelRouteSetId = null;
        ModelRouteSetLabels = modelRouteSetItems.Select(static item => item.DisplayLabel).ToArray();
        SelectModelRouteSetIndex(modelRouteSetItems.Count - 1);
        ModelRouteSetStatusText.Value = $"已复制为草稿：{copy.Id}；保存前不会写入配置。";
    }

    public void DeleteSelectedModelRouteSet()
    {
        var id = selectedModelRouteSetId ?? ModelRouteSetId.Value.Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            ModelRouteSetStatusText.Value = "没有可删除的模型路由方案。";
            return;
        }

        var changes = new List<ConfigurationChange>
        {
            new()
            {
                Operation = ConfigurationChangeOperation.Unset,
                Key = $"model_route_sets.{id}",
            },
        };
        if (string.Equals(allFields.FirstOrDefault(static field => string.Equals(field.Key, "model_route_set", StringComparison.OrdinalIgnoreCase))?.CurrentValue, id, StringComparison.OrdinalIgnoreCase))
        {
            changes.Add(new ConfigurationChange
            {
                Operation = ConfigurationChangeOperation.Unset,
                Key = "model_route_set",
            });
        }

        var result = applier.Apply(ConfigPath, new ConfigurationChangeSet
        {
            Changes = changes,
        });

        if (result.Applied)
        {
            var routeSetPath = ResolveModelRouteSetPath(id);
            if (File.Exists(routeSetPath))
            {
                File.Delete(routeSetPath);
            }

            selectedModelRouteSetId = null;
            Refresh();
            ModelRouteSetStatusText.Value = $"已删除模型路由方案：{id}；模块文件：{routeSetPath}";
            return;
        }

        ModelRouteSetStatusText.Value = string.Join(Environment.NewLine, result.Issues.Select(static issue => issue.Message));
        UpdateContextPanel();
    }

    public void SelectModelRouteSetRouteIndex(int index)
    {
        SyncModelRouteSetDraftFromEditor();
        LoadModelRouteSetRouteIndex(index);
    }

    private void LoadModelRouteSetRouteIndex(int index)
    {
        var routeSet = CurrentModelRouteSet();
        if (routeSet is null || routeSet.Routes.Count == 0)
        {
            ModelRouteSetRouteLabels = [];
            ModelRouteSetCandidateLabels = [];
            selectedModelRouteSetRouteDraftIndex = -1;
            selectedModelRouteSetCandidateDraftIndex = -1;
            return;
        }

        var routeIndex = Math.Clamp(index, 0, routeSet.Routes.Count - 1);
        var route = routeSet.Routes[routeIndex];
        selectedModelRouteSetRouteDraftIndex = routeIndex;
        selectedModelRouteSetCandidateDraftIndex = -1;
        ModelRouteSetRouteIndex.Value = routeIndex;
        ModelRouteSetRouteKind.Value = route.Kind;
        ModelRouteSetRouteFallback.Value = route.FallbackRouteKind;
        ModelRouteSetRouteLabels = BuildModelRouteSetRouteLabels(routeSet);
        LoadModelRouteSetCandidateIndex(0);
    }

    public void SelectModelRouteSetCandidateIndex(int index)
    {
        SyncCandidateDraftFromEditor();
        LoadModelRouteSetCandidateIndex(index);
    }

    private void LoadModelRouteSetCandidateIndex(int index)
    {
        var route = CurrentModelRouteSetRoute();
        if (route is null || route.Candidates.Count == 0)
        {
            ModelRouteSetCandidateLabels = [];
            selectedModelRouteSetCandidateDraftIndex = -1;
            return;
        }

        var candidateIndex = Math.Clamp(index, 0, route.Candidates.Count - 1);
        var candidate = route.Candidates[candidateIndex];
        selectedModelRouteSetCandidateDraftIndex = candidateIndex;
        ModelRouteSetCandidateIndex.Value = candidateIndex;
        ModelRouteSetCandidateLabels = BuildModelRouteSetCandidateLabels(route);
        ModelRouteSetCandidateCapabilities.Value = string.Join(",", candidate.Capabilities);
        SetModelRouteSetOptionIndex(ModelRouteSetProviderOptions, ModelRouteSetCandidateProviderIndex, candidate.Provider);
        SetModelRouteSetOptionIndex(ModelRouteSetModelOptions, ModelRouteSetCandidateModelIndex, candidate.Model);
        SetModelRouteSetOptionIndex(ModelRouteSetProtocolOptions, ModelRouteSetCandidateProtocolIndex, string.IsNullOrWhiteSpace(candidate.Protocol) ? "auto" : candidate.Protocol);
    }

    public void SelectModelRouteSetCandidateProviderIndex(int index)
    {
        if (ModelRouteSetProviderOptions.Count == 0)
        {
            return;
        }

        ModelRouteSetCandidateProviderIndex.Value = Math.Clamp(index, 0, ModelRouteSetProviderOptions.Count - 1);
        SyncCandidateDraftFromEditor();
    }

    public void SelectModelRouteSetCandidateModelIndex(int index)
    {
        if (ModelRouteSetModelOptions.Count == 0)
        {
            return;
        }

        ModelRouteSetCandidateModelIndex.Value = Math.Clamp(index, 0, ModelRouteSetModelOptions.Count - 1);
        SyncCandidateDraftFromEditor();
    }

    public void SelectModelRouteSetCandidateProtocolIndex(int index)
    {
        ModelRouteSetCandidateProtocolIndex.Value = Math.Clamp(index, 0, ModelRouteSetProtocolOptions.Count - 1);
        SyncCandidateDraftFromEditor();
    }

    public void BeginNewModelRouteSetCandidate()
    {
        var route = CurrentModelRouteSetRoute();
        if (route is null)
        {
            return;
        }

        SyncModelRouteSetDraftFromEditor();
        route.Candidates.Add(CreateDefaultModelRouteSetCandidate());
        SelectModelRouteSetCandidateIndex(route.Candidates.Count - 1);
        ModelRouteSetStatusText.Value = "已新增候选草稿；保存前不会写入配置。";
        UpdateContextPanel();
    }

    public void DeleteSelectedModelRouteSetCandidate()
    {
        var route = CurrentModelRouteSetRoute();
        if (route is null || route.Candidates.Count <= 1)
        {
            ModelRouteSetStatusText.Value = "每条 route 至少需要保留一个候选模型。";
            UpdateContextPanel();
            return;
        }

        var sourceIndex = selectedModelRouteSetCandidateDraftIndex >= 0
            ? selectedModelRouteSetCandidateDraftIndex
            : ModelRouteSetCandidateIndex.Value;
        var candidateIndex = Math.Clamp(sourceIndex, 0, route.Candidates.Count - 1);
        route.Candidates.RemoveAt(candidateIndex);
        SelectModelRouteSetCandidateIndex(Math.Min(candidateIndex, route.Candidates.Count - 1));
        ModelRouteSetStatusText.Value = "已删除候选草稿；保存前不会写入配置。";
        UpdateContextPanel();
    }

    public void MoveSelectedModelRouteSetCandidate(int delta)
    {
        var route = CurrentModelRouteSetRoute();
        if (route is null || route.Candidates.Count < 2)
        {
            return;
        }

        SyncModelRouteSetDraftFromEditor();
        var from = Math.Clamp(ModelRouteSetCandidateIndex.Value, 0, route.Candidates.Count - 1);
        var to = Math.Clamp(from + delta, 0, route.Candidates.Count - 1);
        if (from == to)
        {
            return;
        }

        var candidate = route.Candidates[from];
        route.Candidates.RemoveAt(from);
        route.Candidates.Insert(to, candidate);
        LoadModelRouteSetCandidateIndex(to);
        ModelRouteSetStatusText.Value = $"已调整候选顺序：{from + 1} -> {to + 1}；保存前不会写入配置。";
        UpdateContextPanel();
    }

    public void SaveModelRouteSet()
    {
        SyncModelRouteSetDraftFromEditor();
        var routeSet = CurrentModelRouteSet();
        if (routeSet is null)
        {
            ModelRouteSetStatusText.Value = "没有可保存的模型路由方案。";
            UpdateContextPanel();
            return;
        }

        routeSet.Id = ModelRouteSetId.Value.Trim();
        routeSet.DisplayName = ModelRouteSetDisplayName.Value.Trim();
        routeSet.Description = ModelRouteSetDescription.Value.Trim();
        ApplyRegisteredModelRouteSetRoutes(routeSet);
        if (!IsValidModelRouteSetId(routeSet.Id))
        {
            ModelRouteSetStatusText.Value = "方案 ID 只能包含字母、数字、下划线或短横线。";
            UpdateContextPanel();
            return;
        }

        if (routeSet.Routes.Count == 0 || routeSet.Routes.Any(static route => route.Candidates.Count == 0))
        {
            ModelRouteSetStatusText.Value = "每个模型路由方案至少需要一条 route，且每条 route 至少需要一个候选模型。";
            UpdateContextPanel();
            return;
        }

        var moduleResult = SaveModelRouteSetModuleFile(routeSet);
        if (!moduleResult.Applied)
        {
            ModelRouteSetStatusText.Value = string.Join(Environment.NewLine, moduleResult.Issues.Select(static issue => issue.Message));
            UpdateContextPanel();
            return;
        }

        var referenceResult = SaveActiveModelRouteSetReference(routeSet);
        if (referenceResult.Applied)
        {
            selectedModelRouteSetId = routeSet.Id;
            Refresh();
            ModelRouteSetStatusText.Value = $"已保存模型路由方案：{routeSet.Id}；模块文件：{moduleResult.TargetPath}";
            UpdateContextPanel();
            return;
        }

        ModelRouteSetStatusText.Value = string.Join(Environment.NewLine, referenceResult.Issues.Select(static issue => issue.Message));
        UpdateContextPanel();
    }

    private void ApplyRegisteredModelRouteSetRoutes(ModelRouteSetConfigItem routeSet)
    {
        var selectedKind = CurrentModelRouteSetRoute()?.Kind;
        var normalizedRoutes = NormalizeModelRouteSetRoutes(routeSet.Routes);
        routeSet.Routes.Clear();
        routeSet.Routes.AddRange(normalizedRoutes);
        var selectedIndex = string.IsNullOrWhiteSpace(selectedKind)
            ? 0
            : routeSet.Routes.FindIndex(route => string.Equals(route.Kind, selectedKind, StringComparison.OrdinalIgnoreCase));
        LoadModelRouteSetRouteIndex(selectedIndex < 0 ? 0 : selectedIndex);
    }

    private static bool IsValidModelRouteSetId(string id)
        => !string.IsNullOrWhiteSpace(id)
           && id.All(static ch => char.IsLetterOrDigit(ch) || ch is '_' or '-');

    private static StructuredValue BuildModelRouteSetRoutesValue(ModelRouteSetConfigItem routeSet)
        => StructuredValue.FromArray(routeSet.Routes.Select(static route =>
        {
            var properties = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["kind"] = StructuredValue.FromString(route.Kind.Trim()),
                ["candidates"] = StructuredValue.FromArray(route.Candidates.Select(static candidate =>
                {
                    var candidateProperties = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                    {
                        ["provider"] = StructuredValue.FromString(candidate.Provider.Trim()),
                        ["model"] = StructuredValue.FromString(candidate.Model.Trim()),
                    };
                    if (!string.IsNullOrWhiteSpace(candidate.Protocol))
                    {
                        candidateProperties["protocol"] = StructuredValue.FromString(candidate.Protocol.Trim());
                    }

                    if (candidate.Capabilities.Count > 0)
                    {
                        candidateProperties["capabilities"] = StructuredValue.FromArray(candidate.Capabilities.Select(StructuredValue.FromString).ToArray());
                    }

                    return StructuredValue.FromObject(candidateProperties);
                }).ToArray()),
            };
            if (!string.IsNullOrWhiteSpace(route.FallbackRouteKind))
            {
                properties["fallback"] = StructuredValue.FromString(route.FallbackRouteKind.Trim());
            }

            return StructuredValue.FromObject(properties);
        }).ToArray());

    private void SyncModelRouteSetDraftFromEditor()
    {
        var routeSet = CurrentModelRouteSet();
        if (routeSet is null)
        {
            return;
        }

        routeSet.Id = ModelRouteSetId.Value.Trim();
        routeSet.DisplayName = ModelRouteSetDisplayName.Value.Trim();
        routeSet.Description = ModelRouteSetDescription.Value.Trim();
        var route = CurrentModelRouteSetRoute();
        if (route is not null)
        {
            route.FallbackRouteKind = ModelRouteSetRouteFallback.Value.Trim();
        }

        SyncCandidateDraftFromEditor();
        ModelRouteSetRouteLabels = BuildModelRouteSetRouteLabels(routeSet);
    }

    private void SyncCandidateDraftFromEditor()
    {
        var route = CurrentModelRouteSetRoute();
        if (route is null || route.Candidates.Count == 0)
        {
            return;
        }

        var sourceIndex = selectedModelRouteSetCandidateDraftIndex >= 0
            ? selectedModelRouteSetCandidateDraftIndex
            : ModelRouteSetCandidateIndex.Value;
        var candidateIndex = Math.Clamp(sourceIndex, 0, route.Candidates.Count - 1);
        var candidate = route.Candidates[candidateIndex];
        candidate.Provider = ModelRouteSetProviderOptions.Count == 0
            ? candidate.Provider
            : ModelRouteSetProviderOptions[Math.Clamp(ModelRouteSetCandidateProviderIndex.Value, 0, ModelRouteSetProviderOptions.Count - 1)];
        candidate.Model = ModelRouteSetModelOptions.Count == 0
            ? candidate.Model
            : ModelRouteSetModelOptions[Math.Clamp(ModelRouteSetCandidateModelIndex.Value, 0, ModelRouteSetModelOptions.Count - 1)];
        var protocol = ModelRouteSetProtocolOptions[Math.Clamp(ModelRouteSetCandidateProtocolIndex.Value, 0, ModelRouteSetProtocolOptions.Count - 1)];
        candidate.Protocol = string.Equals(protocol, "auto", StringComparison.OrdinalIgnoreCase) ? string.Empty : protocol;
        candidate.Capabilities = ModelRouteSetCandidateCapabilities.Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        ModelRouteSetCandidateLabels = BuildModelRouteSetCandidateLabels(route);
    }

    private ModelRouteSetConfigItem? CurrentModelRouteSet()
    {
        if (modelRouteSetItems.Count == 0)
        {
            return null;
        }

        var index = Math.Clamp(ModelRouteSetIndex.Value, 0, modelRouteSetItems.Count - 1);
        return modelRouteSetItems[index];
    }

    private ModelRouteSetRouteDraft? CurrentModelRouteSetRoute()
    {
        var routeSet = CurrentModelRouteSet();
        if (routeSet is null || routeSet.Routes.Count == 0)
        {
            return null;
        }

        var sourceIndex = selectedModelRouteSetRouteDraftIndex >= 0
            ? selectedModelRouteSetRouteDraftIndex
            : ModelRouteSetRouteIndex.Value;
        var index = Math.Clamp(sourceIndex, 0, routeSet.Routes.Count - 1);
        return routeSet.Routes[index];
    }

    private static IReadOnlyList<string> BuildModelRouteSetRouteLabels(ModelRouteSetConfigItem routeSet)
        => routeSet.Routes.Select((route, index) => $"{index + 1}. {route.Kind} ({route.Candidates.Count} candidates)").ToArray();

    private static IReadOnlyList<string> BuildModelRouteSetCandidateLabels(ModelRouteSetRouteDraft route)
        => route.Candidates.Select((candidate, index) =>
        {
            var prefix = index == 0 ? "首选" : $"fallback {index}";
            var protocol = string.IsNullOrWhiteSpace(candidate.Protocol) ? "auto" : candidate.Protocol;
            return $"{index + 1}. {prefix}: {candidate.Provider}/{candidate.Model} [{protocol}]";
        }).ToArray();

    private void SetModelRouteSetOptionIndex(IReadOnlyList<string> options, ObservableValue<int> target, string value)
    {
        var index = options.ToList().FindIndex(option => string.Equals(option, value, StringComparison.OrdinalIgnoreCase));
        target.Value = index < 0 ? 0 : index;
    }

    private string CreateUniqueModelRouteSetDraftId(string baseId)
    {
        var normalizedBaseId = string.IsNullOrWhiteSpace(baseId) ? "route_set" : baseId.Trim();
        var existingIds = modelRouteSetItems.Select(static routeSet => routeSet.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = normalizedBaseId;
        if (!existingIds.Contains(candidate))
        {
            return candidate;
        }

        for (var index = 2; index < 1000; index++)
        {
            candidate = $"{normalizedBaseId}-{index}";
            if (!existingIds.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{normalizedBaseId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    }

    private void RefreshViewMode()
    {
        IsFieldView.Value = viewMode == ConfigGuiViewMode.Fields;
        IsProfileCompositionCurrentPageView.Value = IsFieldView.Value
                                                    && string.Equals(selectedCategoryId, ProfileCompositionCurrentCategoryId, StringComparison.OrdinalIgnoreCase);
        IsAgentFieldPageView.Value = IsFieldView.Value
                                     && string.Equals(selectedCategoryId, ConfigurationCategoryIds.AgentBehavior, StringComparison.OrdinalIgnoreCase);
        IsMemoryFieldPageView.Value = IsFieldView.Value && IsMemoryPageCategory(selectedCategoryId);
        IsMemoryInstanceCreationVisible.Value = IsMemoryFieldPageView.Value;
        IsWorkspaceView.Value = viewMode == ConfigGuiViewMode.Workspace;
        IsDetailViewVisible.Value = true;
        IsFieldDetailEditorVisible.Value = viewMode is ConfigGuiViewMode.Fields or ConfigGuiViewMode.Workspace;
        if (!IsFieldDetailEditorVisible.Value)
        {
            IsTextEditorVisible.Value = false;
            IsEnumEditorVisible.Value = false;
        }

        IsProviderView.Value = viewMode == ConfigGuiViewMode.Providers;
        IsModelProtocolMappingView.Value = viewMode == ConfigGuiViewMode.ModelProtocolMappings;
        IsDefaultModelProtocolRulesView.Value = viewMode == ConfigGuiViewMode.DefaultModelProtocolRules;
        IsModelRouteSetView.Value = viewMode == ConfigGuiViewMode.ModelRouteSets;
        IsPromptView.Value = viewMode == ConfigGuiViewMode.Prompts;
        IsSkillPackageView.Value = viewMode == ConfigGuiViewMode.SkillPackages;
        IsToolPackageView.Value = viewMode == ConfigGuiViewMode.ToolPackages;
        IsProviderPackageView.Value = viewMode == ConfigGuiViewMode.ProviderPackages;
        IsMcpServerPackageView.Value = viewMode == ConfigGuiViewMode.McpServerPackages;
        IsArtifactStorePackageView.Value = viewMode == ConfigGuiViewMode.ArtifactStorePackages;
        IsDiagnosticSinkPackageView.Value = viewMode == ConfigGuiViewMode.DiagnosticSinkPackages;
        IsWorkspaceResolverPackageView.Value = viewMode == ConfigGuiViewMode.WorkspaceResolverPackages;
        IsPolicyStrategyPackageView.Value = viewMode == ConfigGuiViewMode.PolicyStrategyPackages;
        UpdateContextPanel();
    }

    public void UpdateContextPanel()
    {
        var category = Categories.FirstOrDefault(row => string.Equals(row.Id, selectedCategoryId, StringComparison.OrdinalIgnoreCase));
        var categoryName = category?.DisplayName ?? SelectedCategoryName.Value;
        var categoryDescription = string.IsNullOrWhiteSpace(category?.Description) ? "当前页面暂无说明。" : category.Description;
        var tianShuTomlBoundary = selectedField is not null
            && TianShuKnownModuleConfigurationPaths.TryResolveWriteTargetPath(ConfigPath, selectedField.Key, out _)
                ? "通过 Config Plane 写回对应模块 TOML；tianshu.toml 只保留主入口与最终覆盖。"
                : "通过 Config Plane 写回 tianshu.toml；运行时覆盖仍由 AppHost 组合。";

        ContextTitle.Value = viewMode switch
        {
            ConfigGuiViewMode.Fields => selectedField?.DisplayName ?? categoryName,
            ConfigGuiViewMode.Workspace => selectedField?.DisplayName ?? "工作空间配置",
            ConfigGuiViewMode.ModelProtocolMappings => CurrentModelProtocolProvider() is { } provider ? $"模型协议适配：{provider.Id}" : "模型协议适配",
            ConfigGuiViewMode.DefaultModelProtocolRules => CurrentDefaultModelProtocolRuleSet() is { } ruleSet ? $"默认协议规则：{ruleSet.Id}" : "默认协议规则",
            ConfigGuiViewMode.ModelRouteSets => string.IsNullOrWhiteSpace(ModelRouteSetId.Value) ? "模型路由方案" : $"模型路由方案：{ModelRouteSetId.Value}",
            _ => categoryName,
        };
        ContextSummary.Value = viewMode switch
        {
            ConfigGuiViewMode.Fields => $"字段型页面：{categoryName}",
            ConfigGuiViewMode.Workspace => "字段型页面：工作空间配置文件与项目覆盖",
            _ => $"集合型页面：{categoryName}",
        };

        ContextDescription.Value = viewMode switch
        {
            ConfigGuiViewMode.Fields or ConfigGuiViewMode.Workspace => string.IsNullOrWhiteSpace(SelectedHelpText.Value) ? categoryDescription : SelectedHelpText.Value,
            _ => categoryDescription,
        };
        ContextSaveTarget.Value = viewMode switch
        {
            ConfigGuiViewMode.Fields or ConfigGuiViewMode.Workspace => selectedField is null ? ConfigPath : ResolveFieldSaveTarget(selectedField.Key),
            ConfigGuiViewMode.Providers or ConfigGuiViewMode.ModelProtocolMappings => CurrentProviderInstanceSaveTarget(),
            ConfigGuiViewMode.DefaultModelProtocolRules => CurrentDefaultModelProtocolRuleSetSaveTarget(),
            ConfigGuiViewMode.ModelRouteSets => CurrentModelRouteSetSaveTarget(),
            ConfigGuiViewMode.Prompts => PromptSaveTargetText.Value,
            ConfigGuiViewMode.SkillPackages => SkillPackagePathText.Value,
            ConfigGuiViewMode.ToolPackages => ToolManifestPathText.Value,
            ConfigGuiViewMode.ProviderPackages => ModelProviderPackageManifestPathText.Value,
            ConfigGuiViewMode.McpServerPackages => McpServerPackageManifestPathText.Value,
            ConfigGuiViewMode.ArtifactStorePackages => ArtifactStorePackageManifestPathText.Value,
            ConfigGuiViewMode.DiagnosticSinkPackages => DiagnosticSinkPackageManifestPathText.Value,
            ConfigGuiViewMode.WorkspaceResolverPackages => WorkspaceResolverPackageManifestPathText.Value,
            ConfigGuiViewMode.PolicyStrategyPackages => PolicyStrategyPackageManifestPathText.Value,
            _ => ConfigPath,
        };
        if (string.IsNullOrWhiteSpace(ContextSaveTarget.Value))
        {
            ContextSaveTarget.Value = "当前页面尚未选中可写入目标。";
        }

        ContextSourceName.Value = viewMode switch
        {
            ConfigGuiViewMode.Fields or ConfigGuiViewMode.Workspace => string.IsNullOrWhiteSpace(SelectedSourceName.Value) ? "默认值" : SelectedSourceName.Value,
            ConfigGuiViewMode.Providers => "modules/model/provider-instances/<id>.toml / providers.<id>",
            ConfigGuiViewMode.ModelProtocolMappings => "modules/model/provider-instances/<id>.toml / providers.<id>.model_overrides 与 providers.<id>.protocol_rules",
            ConfigGuiViewMode.DefaultModelProtocolRules => "modules/model/protocol-rules/<id>.toml / model_protocol_rule_sets.<id>.rules",
            ConfigGuiViewMode.ModelRouteSets => "modules/model/route-sets/<id>.toml / Config Plane typed projection",
            ConfigGuiViewMode.Prompts => "modules/prompts/<package>/prompt.toml",
            ConfigGuiViewMode.SkillPackages => "modules/skills/<package>/SKILL.md",
            ConfigGuiViewMode.ToolPackages => "modules/tools/packages/<package>/tool.toml",
            ConfigGuiViewMode.ProviderPackages => "modules/model/provider-adapters/<package>/provider.toml",
            ConfigGuiViewMode.McpServerPackages => "modules/mcp-servers/<package>/server.toml",
            ConfigGuiViewMode.ArtifactStorePackages => "modules/artifacts/stores/<package>/store.toml",
            ConfigGuiViewMode.DiagnosticSinkPackages => "modules/diagnostics/sinks/<package>/sink.toml",
            ConfigGuiViewMode.WorkspaceResolverPackages => "modules/workspace/resolvers/<package>/resolver.toml",
            ConfigGuiViewMode.PolicyStrategyPackages => "modules/policies/strategies/<package>/policy.toml",
            _ => "Config Plane",
        };
        ContextOverrideBoundary.Value = viewMode switch
        {
            ConfigGuiViewMode.Fields or ConfigGuiViewMode.Workspace => selectedField is null ? "用户配置覆盖默认值。" : $"{SelectedConfiguredText.Value}；{SelectedSourceName.Value} 覆盖默认值。",
            ConfigGuiViewMode.Providers => "提供方实例由 modules/model/provider-instances 模块承载；协议适配器包只提供 wire protocol adapter。",
            ConfigGuiViewMode.ModelProtocolMappings => "提供方精确覆写和提供方通配规则优先于默认协议规则集。",
            ConfigGuiViewMode.DefaultModelProtocolRules => "默认协议规则集只在提供方显式覆写未命中时生效；优先于提供方默认协议和内置启发式。",
            ConfigGuiViewMode.ModelRouteSets => "模型路由方案、route 与候选顺序由 modules/model/route-sets/<id>.toml 承载；tianshu.toml 只选择当前模型路由方案。",
            ConfigGuiViewMode.Prompts => "Prompt Pack 由包 manifest 承载；不再通过旧 prompt_file 写入。",
            ConfigGuiViewMode.SkillPackages => "技能包天然是能力包；当前页只管理启用状态与元数据投影。",
            _ => "能力包 manifest 是当前模块可信来源；tianshu.toml 仅保留最终覆盖层。",
        };
        ContextWriteBoundary.Value = viewMode switch
        {
            ConfigGuiViewMode.Fields or ConfigGuiViewMode.Workspace => tianShuTomlBoundary,
            ConfigGuiViewMode.Providers or ConfigGuiViewMode.ModelProtocolMappings => "保存写入当前提供方实例模块文件，并只在 tianshu.toml 更新 active provider instance 引用。",
            ConfigGuiViewMode.DefaultModelProtocolRules => "保存写入当前默认协议规则模块文件，并只在 tianshu.toml 更新 active rule set 引用。",
            ConfigGuiViewMode.ModelRouteSets => "保存写入当前模型路由方案模块文件，并只在 tianshu.toml 更新当前模型路由方案引用。",
            ConfigGuiViewMode.Prompts => "保存只写当前 Prompt Pack，不隐式修改 tianshu.toml。",
            ConfigGuiViewMode.SkillPackages => "保存只写技能包启用状态，不修改技能正文以外的配置源。",
            _ => "保存只写当前能力包 manifest，不隐式修改 tianshu.toml。",
        };
        ContextDiagnostics.Value = viewMode switch
        {
            ConfigGuiViewMode.Fields or ConfigGuiViewMode.Workspace => string.IsNullOrWhiteSpace(SelectedIssues.Value) ? "当前字段暂无校验问题。" : SelectedIssues.Value,
            ConfigGuiViewMode.Providers => EmptyAs(ProviderStatusText.Value, "可使用“测试连接”验证当前提供方的模型列表。"),
            ConfigGuiViewMode.ModelProtocolMappings => EmptyAs(ModelProtocolStatusText.Value, "提供方精确模型覆写优先于提供方通配规则；二者都优先于默认规则集。"),
            ConfigGuiViewMode.DefaultModelProtocolRules => EmptyAs(DefaultModelProtocolRuleStatusText.Value, "默认规则只在提供方显式覆盖未命中时生效；协议顺序就是 fallback 优先级。"),
            ConfigGuiViewMode.ModelRouteSets => EmptyAs(ModelRouteSetStatusText.Value, "候选提供方/model 下拉来自 Config Plane typed projection；不会访问外网或显示 secret。"),
            ConfigGuiViewMode.Prompts => EmptyAs(PromptStatusText.Value, "Prompt Pack 保存后会直接影响后续会话提示词组合。"),
            ConfigGuiViewMode.SkillPackages => EmptyAs(SkillPackageStatusText.Value, "技能包列表来自 modules/skills/<package>/SKILL.md。"),
            ConfigGuiViewMode.ToolPackages => EmptyAs(ToolStatusText.Value, "工具包提供方的程序集路径相对 tool.toml 解析。"),
            ConfigGuiViewMode.ProviderPackages => EmptyAs(ModelProviderPackageStatusText.Value, "模型提供方 adapter 路径相对 provider.toml 解析。"),
            ConfigGuiViewMode.McpServerPackages => EmptyAs(McpServerPackageStatusText.Value, "MCP Server 包保存后需通过 reload / 新会话重新加载。"),
            ConfigGuiViewMode.ArtifactStorePackages => EmptyAs(ArtifactStorePackageStatusText.Value, "工件存储包决定 runtime artifact store 候选。"),
            ConfigGuiViewMode.DiagnosticSinkPackages => EmptyAs(DiagnosticSinkPackageStatusText.Value, "诊断输出包决定 diagnostics / telemetry sink 候选。"),
            ConfigGuiViewMode.WorkspaceResolverPackages => EmptyAs(WorkspaceResolverPackageStatusText.Value, "工作空间解析包影响项目根与 workspace 配置文件推导。"),
            ConfigGuiViewMode.PolicyStrategyPackages => EmptyAs(PolicyStrategyPackageStatusText.Value, "审批策略包只提供受控默认策略，不绕过核心审批。"),
            _ => "暂无诊断信息。",
        };
        ContextWriteBackResult.Value = viewMode switch
        {
            ConfigGuiViewMode.Fields or ConfigGuiViewMode.Workspace => EmptyAs(StatusText.Value, "尚未执行字段写回。"),
            ConfigGuiViewMode.Providers => EmptyAs(ProviderStatusText.Value, "尚未执行提供方写回。"),
            ConfigGuiViewMode.ModelProtocolMappings => EmptyAs(ModelProtocolStatusText.Value, "尚未执行模型协议适配写回。"),
            ConfigGuiViewMode.DefaultModelProtocolRules => EmptyAs(DefaultModelProtocolRuleStatusText.Value, "尚未执行默认协议规则写回。"),
            ConfigGuiViewMode.ModelRouteSets => EmptyAs(ModelRouteSetStatusText.Value, "尚未执行模型路由方案写回。"),
            ConfigGuiViewMode.Prompts => EmptyAs(PromptStatusText.Value, "尚未执行 Prompt Pack 写回。"),
            ConfigGuiViewMode.SkillPackages => EmptyAs(SkillPackageStatusText.Value, "尚未执行技能包状态写回。"),
            ConfigGuiViewMode.ToolPackages => EmptyAs(ToolStatusText.Value, "尚未执行工具包写回。"),
            ConfigGuiViewMode.ProviderPackages => EmptyAs(ModelProviderPackageStatusText.Value, "尚未执行协议适配器包写回。"),
            ConfigGuiViewMode.McpServerPackages => EmptyAs(McpServerPackageStatusText.Value, "尚未执行 MCP Server 包写回。"),
            ConfigGuiViewMode.ArtifactStorePackages => EmptyAs(ArtifactStorePackageStatusText.Value, "尚未执行工件存储包写回。"),
            ConfigGuiViewMode.DiagnosticSinkPackages => EmptyAs(DiagnosticSinkPackageStatusText.Value, "尚未执行诊断输出包写回。"),
            ConfigGuiViewMode.WorkspaceResolverPackages => EmptyAs(WorkspaceResolverPackageStatusText.Value, "尚未执行工作空间解析包写回。"),
            ConfigGuiViewMode.PolicyStrategyPackages => EmptyAs(PolicyStrategyPackageStatusText.Value, "尚未执行审批策略包写回。"),
            _ => EmptyAs(StatusText.Value, "尚未执行写回。"),
        };
    }

    private string ResolveFieldSaveTarget(string key)
        => TianShuKnownModuleConfigurationPaths.TryResolveWriteTargetPath(ConfigPath, key, out var modulePath)
            ? modulePath
            : ConfigPath;

    private static string EmptyAs(string value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private void ApplyFilter()
    {
        var query = SearchText.Value.Trim();
        FilteredFields = allFields
            .Where(row =>
                viewMode == ConfigGuiViewMode.Fields
                && row.CategoryId != ConfigurationCategoryIds.Workspace
                && IsFieldInSelectedCategory(row, selectedCategoryId)
                && IsVisibleFieldInCategory(row, selectedCategoryId)
                && (string.IsNullOrWhiteSpace(query)
                    || row.Key.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || row.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || row.Description.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(static row => row.CategoryOrder)
            .ThenBy(static row => EnabledFieldSortRank(row))
            .ThenBy(static row => DailyModelSelectionFieldSortRank(row))
            .ThenBy(static row => row.GroupOrder)
            .ThenBy(static row => row.Key, StringComparer.Ordinal)
            .ToList();
    }

    private static int EnabledFieldSortRank(ConfigFieldRow row)
        => IsEnabledField(row) ? 0 : 1;

    private static int DailyModelSelectionFieldSortRank(ConfigFieldRow row)
        => string.Equals(row.Key, "model_route_set", StringComparison.OrdinalIgnoreCase)
            ? 0
            : string.Equals(row.Key, "model_protocol_rule_set", StringComparison.OrdinalIgnoreCase)
                ? 1
                : 2;

    private static bool IsEnabledField(ConfigFieldRow row)
        => string.Equals(row.Key, "memory.enabled", StringComparison.OrdinalIgnoreCase)
           || row.Key.EndsWith(".enabled", StringComparison.OrdinalIgnoreCase)
           || row.DisplayName.StartsWith("启用", StringComparison.OrdinalIgnoreCase);

    private static bool IsFieldInSelectedCategory(ConfigFieldRow row, string categoryId)
        => IsMemoryPageCategory(categoryId)
            ? IsMemoryFieldInPage(row, categoryId)
            : IsProfileCompositionPageCategory(categoryId)
                ? IsProfileCompositionFieldInPage(row, categoryId)
            : string.Equals(row.CategoryId, categoryId, StringComparison.OrdinalIgnoreCase);

    private static bool IsVisibleFieldInCategory(ConfigFieldRow row, string categoryId)
    {
        if (!IsProfileCompositionPageCategory(categoryId) && IsProfileCompositionField(row))
        {
            return false;
        }

        if (string.Equals(categoryId, ConfigurationCategoryIds.ExtensionsImports, StringComparison.OrdinalIgnoreCase)
            && IsKnownModuleProfileField(row))
        {
            return false;
        }

        if (IsProfileDescriptionField(row))
        {
            return false;
        }

        if (IsProfileCompositionPageCategory(categoryId))
        {
            return true;
        }

        if (IsMemoryPageCategory(categoryId))
        {
            return true;
        }

        if (!string.Equals(categoryId, ConfigurationCategoryIds.ConnectivityModel, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(row.Key, "model_route_set", StringComparison.OrdinalIgnoreCase)
               || string.Equals(row.Key, "model_protocol_rule_set", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProfileDescriptionField(ConfigFieldRow row)
        => row.Key.StartsWith("profiles.", StringComparison.OrdinalIgnoreCase)
           && row.Key.EndsWith(".description", StringComparison.OrdinalIgnoreCase);

    private static bool IsKnownModuleProfileField(ConfigFieldRow row)
        => row.Key.StartsWith("tool_profiles.", StringComparison.OrdinalIgnoreCase)
           || row.Key.StartsWith("session_profiles.", StringComparison.OrdinalIgnoreCase)
           || row.Key.StartsWith("conversation_profiles.", StringComparison.OrdinalIgnoreCase)
           || row.Key.StartsWith("execution_profiles.", StringComparison.OrdinalIgnoreCase)
           || row.Key.StartsWith("permission_profiles.", StringComparison.OrdinalIgnoreCase)
           || row.Key.StartsWith("governance_profiles.", StringComparison.OrdinalIgnoreCase)
           || row.Key.StartsWith("feature_profiles.", StringComparison.OrdinalIgnoreCase)
           || row.Key.StartsWith("realtime_profiles.", StringComparison.OrdinalIgnoreCase)
           || row.Key.StartsWith("tui_profiles.", StringComparison.OrdinalIgnoreCase)
           || row.Key.StartsWith("collaboration_profiles.", StringComparison.OrdinalIgnoreCase)
           || row.Key.StartsWith("workflow_profiles.", StringComparison.OrdinalIgnoreCase)
           || row.Key.StartsWith("identity_profiles.", StringComparison.OrdinalIgnoreCase)
           || row.Key.StartsWith("memory_profiles.", StringComparison.OrdinalIgnoreCase)
           || row.Key.StartsWith("workspace_profiles.", StringComparison.OrdinalIgnoreCase);

    private ToolPackageManifestValue CloneToolPackage(ToolPackageManifestValue source)
        => new()
        {
            Id = source.Id,
            DisplayName = source.DisplayName,
            Enabled = source.Enabled,
            Type = source.Type,
            Priority = source.Priority,
            AssemblyPath = source.AssemblyPath,
            ProviderType = source.ProviderType,
            ReplaceExisting = source.ReplaceExisting,
            ManifestPath = source.ManifestPath,
            PackageDirectory = source.PackageDirectory,
            IsLegacy = source.IsLegacy,
            Providers = source.Providers.Select(static provider => new ToolProviderManifestValue
            {
                Id = provider.Id,
                Enabled = provider.Enabled,
                Type = provider.Type,
                AssemblyPath = provider.AssemblyPath,
                ProviderType = provider.ProviderType,
                Priority = provider.Priority,
                ReplaceExisting = provider.ReplaceExisting,
            }).ToArray(),
        };

    private string GetSelectedToolPackageType()
    {
        var index = Math.Clamp(ToolPackageTypeIndex.Value, 0, ToolPackageTypeLabels.Count - 1);
        ToolPackageType.Value = ToolPackageTypeLabels[index];
        return ToolPackageType.Value;
    }

    private string GetSelectedToolProviderType()
    {
        var index = Math.Clamp(ToolProviderTypeIndex.Value, 0, ToolProviderTypeLabels.Count - 1);
        ToolProviderType.Value = ToolProviderTypeLabels[index];
        return ToolProviderType.Value;
    }

    private static int FindLabelIndex(IReadOnlyList<string> labels, string? value, int fallbackIndex)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            for (var index = 0; index < labels.Count; index++)
            {
                if (string.Equals(labels[index], value.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }
        }

        return Math.Clamp(fallbackIndex, 0, labels.Count - 1);
    }

    private string CreateUniqueToolPackageId(string baseId)
    {
        var root = TianShuToolManifestConfiguration.ResolveRootDirectory(ConfigPath);
        var existingIds = toolProjection?.Files
            .Select(file => Path.GetFileName(Path.GetDirectoryName(file.Path)))
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var normalized = string.IsNullOrWhiteSpace(baseId) ? "custom-tools" : baseId.Trim();
        var packageRoot = TianShuToolManifestConfiguration.ResolveToolRootDirectory(root);
        for (var index = 0; index < 1000; index++)
        {
            var candidate = index == 0 ? normalized : $"{normalized}-{index + 1}";
            if (!existingIds.Contains(candidate) && !Directory.Exists(Path.Combine(packageRoot, candidate)))
            {
                return candidate;
            }
        }

        return $"{normalized}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    }

    private string CreateUniqueToolProviderId(string baseId)
    {
        var existingIds = toolProjection?.SelectedPackage?.Providers
            .Select(static provider => provider.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var normalized = string.IsNullOrWhiteSpace(baseId) ? "provider" : baseId.Trim();
        for (var index = 0; index < 1000; index++)
        {
            var candidate = index == 0 ? normalized : $"{normalized}-{index + 1}";
            if (!existingIds.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{normalized}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    }

    private ProviderPackageManifestValue CloneModelProviderPackage(ProviderPackageManifestValue source)
        => new()
        {
            Id = source.Id,
            DisplayName = source.DisplayName,
            Enabled = source.Enabled,
            Type = source.Type,
            Priority = source.Priority,
            ManifestPath = source.ManifestPath,
            PackageDirectory = source.PackageDirectory,
            Adapters = source.Adapters.Select(static adapter => new ProviderAdapterManifestValue
            {
                Id = adapter.Id,
                DisplayName = adapter.DisplayName,
                Enabled = adapter.Enabled,
                Type = adapter.Type,
                AssemblyPath = adapter.AssemblyPath,
                Priority = adapter.Priority,
            }).ToArray(),
        };

    private string GetSelectedModelProviderPackageType()
    {
        var index = Math.Clamp(ModelProviderPackageTypeIndex.Value, 0, ModelProviderPackageTypeLabels.Count - 1);
        ModelProviderPackageType.Value = ModelProviderPackageTypeLabels[index];
        return ModelProviderPackageType.Value;
    }

    private string GetSelectedModelProviderAdapterType()
    {
        var index = Math.Clamp(ModelProviderAdapterTypeIndex.Value, 0, ModelProviderAdapterTypeLabels.Count - 1);
        ModelProviderAdapterType.Value = ModelProviderAdapterTypeLabels[index];
        return ModelProviderAdapterType.Value;
    }

    private string CreateUniqueModelProviderPackageId(string baseId)
    {
        var root = TianShuProviderManifestConfiguration.ResolveRootDirectory(ConfigPath);
        var existingIds = providerPackageProjection?.Files
            .Select(file => Path.GetFileName(Path.GetDirectoryName(file.Path)))
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var normalized = string.IsNullOrWhiteSpace(baseId) ? "custom-providers" : baseId.Trim();
        var packageRoot = TianShuProviderManifestConfiguration.ResolveProviderRootDirectory(root);
        for (var index = 0; index < 1000; index++)
        {
            var candidate = index == 0 ? normalized : $"{normalized}-{index + 1}";
            if (!existingIds.Contains(candidate) && !Directory.Exists(Path.Combine(packageRoot, candidate)))
            {
                return candidate;
            }
        }

        return $"{normalized}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    }

    private string CreateUniqueModelProviderAdapterId(string baseId)
    {
        var existingIds = providerPackageProjection?.SelectedPackage?.Adapters
            .Select(static adapter => adapter.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var normalized = string.IsNullOrWhiteSpace(baseId) ? "adapter" : baseId.Trim();
        for (var index = 0; index < 1000; index++)
        {
            var candidate = index == 0 ? normalized : $"{normalized}-{index + 1}";
            if (!existingIds.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{normalized}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    }

    private McpServerPackageManifestValue CloneMcpServerPackage(McpServerPackageManifestValue source)
        => new()
        {
            Id = source.Id,
            DisplayName = source.DisplayName,
            Enabled = source.Enabled,
            Type = source.Type,
            Priority = source.Priority,
            ManifestPath = source.ManifestPath,
            PackageDirectory = source.PackageDirectory,
            Servers = source.Servers.Select(static server => new McpServerManifestValue
            {
                Id = server.Id,
                DisplayName = server.DisplayName,
                Enabled = server.Enabled,
                Required = server.Required,
                Transport = server.Transport,
                Command = server.Command,
                Args = server.Args.ToArray(),
                Env = new Dictionary<string, string>(server.Env, StringComparer.OrdinalIgnoreCase),
                EnvVars = server.EnvVars.ToArray(),
                Cwd = server.Cwd,
                Url = server.Url,
                BearerTokenEnvVar = server.BearerTokenEnvVar,
                HttpHeaders = new Dictionary<string, string>(server.HttpHeaders, StringComparer.OrdinalIgnoreCase),
                EnvHttpHeaders = new Dictionary<string, string>(server.EnvHttpHeaders, StringComparer.OrdinalIgnoreCase),
                StartupTimeoutMs = server.StartupTimeoutMs,
                ToolTimeoutMs = server.ToolTimeoutMs,
                EnabledTools = server.EnabledTools.ToArray(),
                DisabledTools = server.DisabledTools.ToArray(),
            }).ToArray(),
        };

    private string GetSelectedMcpServerPackageType()
    {
        var index = Math.Clamp(McpServerPackageTypeIndex.Value, 0, McpServerPackageTypeLabels.Count - 1);
        McpServerPackageType.Value = McpServerPackageTypeLabels[index];
        return McpServerPackageType.Value;
    }

    private string GetSelectedMcpServerTransport()
    {
        var index = Math.Clamp(McpServerTransportIndex.Value, 0, McpServerTransportLabels.Count - 1);
        McpServerTransport.Value = McpServerTransportLabels[index];
        return McpServerTransport.Value;
    }

    private string CreateUniqueMcpServerPackageId(string baseId)
    {
        var root = TianShuMcpServerManifestConfiguration.ResolveRootDirectory(ConfigPath);
        var existingIds = mcpServerPackageProjection?.Files
            .Select(file => Path.GetFileName(Path.GetDirectoryName(file.Path)))
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var normalized = string.IsNullOrWhiteSpace(baseId) ? "custom-mcp" : baseId.Trim();
        var packageRoot = TianShuMcpServerManifestConfiguration.ResolveMcpServerRootDirectory(root);
        for (var index = 0; index < 1000; index++)
        {
            var candidate = index == 0 ? normalized : $"{normalized}-{index + 1}";
            if (!existingIds.Contains(candidate) && !Directory.Exists(Path.Combine(packageRoot, candidate)))
            {
                return candidate;
            }
        }

        return $"{normalized}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    }

    private string CreateUniqueMcpServerId(string baseId)
    {
        var existingIds = mcpServerPackageProjection?.SelectedPackage?.Servers
            .Select(static server => server.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var normalized = string.IsNullOrWhiteSpace(baseId) ? "server" : baseId.Trim();
        for (var index = 0; index < 1000; index++)
        {
            var candidate = index == 0 ? normalized : $"{normalized}-{index + 1}";
            if (!existingIds.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{normalized}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    }

    private ArtifactStorePackageManifestValue CloneArtifactStorePackage(ArtifactStorePackageManifestValue source)
        => new()
        {
            Id = source.Id,
            DisplayName = source.DisplayName,
            Enabled = source.Enabled,
            Type = source.Type,
            Priority = source.Priority,
            ManifestPath = source.ManifestPath,
            PackageDirectory = source.PackageDirectory,
            Stores = source.Stores.Select(static store => new ArtifactStoreManifestValue
            {
                Id = store.Id,
                DisplayName = store.DisplayName,
                Enabled = store.Enabled,
                Type = store.Type,
                Root = store.Root,
                AssemblyPath = store.AssemblyPath,
                ProviderType = store.ProviderType,
                MaxHistoryVersions = store.MaxHistoryVersions,
                EnableCrossProcessSync = store.EnableCrossProcessSync,
                Priority = store.Priority,
            }).ToArray(),
        };

    private string GetSelectedArtifactStorePackageType()
    {
        var index = Math.Clamp(ArtifactStorePackageTypeIndex.Value, 0, ArtifactStorePackageTypeLabels.Count - 1);
        ArtifactStorePackageType.Value = ArtifactStorePackageTypeLabels[index];
        return ArtifactStorePackageType.Value;
    }

    private string GetSelectedArtifactStoreType()
    {
        var index = Math.Clamp(ArtifactStoreTypeIndex.Value, 0, ArtifactStoreTypeLabels.Count - 1);
        ArtifactStoreType.Value = ArtifactStoreTypeLabels[index];
        return ArtifactStoreType.Value;
    }

    private DiagnosticSinkPackageManifestValue CloneDiagnosticSinkPackage(DiagnosticSinkPackageManifestValue source)
        => new()
        {
            Id = source.Id,
            DisplayName = source.DisplayName,
            Enabled = source.Enabled,
            Type = source.Type,
            Priority = source.Priority,
            ManifestPath = source.ManifestPath,
            PackageDirectory = source.PackageDirectory,
            Sinks = source.Sinks.Select(static sink => new DiagnosticSinkManifestValue
            {
                Id = sink.Id,
                DisplayName = sink.DisplayName,
                Enabled = sink.Enabled,
                Type = sink.Type,
                Level = sink.Level,
                Target = sink.Target,
                AssemblyPath = sink.AssemblyPath,
                ProviderType = sink.ProviderType,
                Endpoint = sink.Endpoint,
                Modules = sink.Modules.ToArray(),
                MaxBytes = sink.MaxBytes,
                Priority = sink.Priority,
            }).ToArray(),
        };

    private string GetSelectedDiagnosticSinkPackageType()
    {
        var index = Math.Clamp(DiagnosticSinkPackageTypeIndex.Value, 0, DiagnosticSinkPackageTypeLabels.Count - 1);
        DiagnosticSinkPackageType.Value = DiagnosticSinkPackageTypeLabels[index];
        return DiagnosticSinkPackageType.Value;
    }

    private string GetSelectedDiagnosticSinkType()
    {
        var index = Math.Clamp(DiagnosticSinkTypeIndex.Value, 0, DiagnosticSinkTypeLabels.Count - 1);
        DiagnosticSinkType.Value = DiagnosticSinkTypeLabels[index];
        return DiagnosticSinkType.Value;
    }

    private string GetSelectedDiagnosticSinkLevel()
    {
        var index = Math.Clamp(DiagnosticSinkLevelIndex.Value, 0, DiagnosticSinkLevelLabels.Count - 1);
        DiagnosticSinkLevel.Value = DiagnosticSinkLevelLabels[index];
        return DiagnosticSinkLevel.Value;
    }

    private string CreateUniqueDiagnosticSinkPackageId(string baseId)
    {
        var root = TianShuDiagnosticSinkManifestConfiguration.ResolveRootDirectory(ConfigPath);
        var existingIds = diagnosticSinkPackageProjection?.Files
            .Select(file => Path.GetFileName(Path.GetDirectoryName(file.Path)))
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var normalized = string.IsNullOrWhiteSpace(baseId) ? "custom-diagnostics" : baseId.Trim();
        var packageRoot = TianShuDiagnosticSinkManifestConfiguration.ResolveDiagnosticSinkRootDirectory(root);
        for (var index = 0; index < 1000; index++)
        {
            var candidate = index == 0 ? normalized : $"{normalized}-{index + 1}";
            if (!existingIds.Contains(candidate) && !Directory.Exists(Path.Combine(packageRoot, candidate)))
            {
                return candidate;
            }
        }

        return $"{normalized}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    }

    private string CreateUniqueDiagnosticSinkId(string baseId)
    {
        var existingIds = diagnosticSinkPackageProjection?.SelectedPackage?.Sinks
            .Select(static sink => sink.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var normalized = string.IsNullOrWhiteSpace(baseId) ? "sink" : baseId.Trim();
        for (var index = 0; index < 1000; index++)
        {
            var candidate = index == 0 ? normalized : $"{normalized}-{index + 1}";
            if (!existingIds.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{normalized}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    }

    private string CreateUniqueArtifactStorePackageId(string baseId)
    {
        var root = TianShuArtifactStoreManifestConfiguration.ResolveRootDirectory(ConfigPath);
        var existingIds = artifactStorePackageProjection?.Files
            .Select(file => Path.GetFileName(Path.GetDirectoryName(file.Path)))
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var normalized = string.IsNullOrWhiteSpace(baseId) ? "custom-artifacts" : baseId.Trim();
        var packageRoot = TianShuArtifactStoreManifestConfiguration.ResolveArtifactStoreRootDirectory(root);
        for (var index = 0; index < 1000; index++)
        {
            var candidate = index == 0 ? normalized : $"{normalized}-{index + 1}";
            if (!existingIds.Contains(candidate) && !Directory.Exists(Path.Combine(packageRoot, candidate)))
            {
                return candidate;
            }
        }

        return $"{normalized}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    }

    private string CreateUniqueArtifactStoreId(string baseId)
    {
        var existingIds = artifactStorePackageProjection?.SelectedPackage?.Stores
            .Select(static store => store.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var normalized = string.IsNullOrWhiteSpace(baseId) ? "store" : baseId.Trim();
        for (var index = 0; index < 1000; index++)
        {
            var candidate = index == 0 ? normalized : $"{normalized}-{index + 1}";
            if (!existingIds.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{normalized}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    }

    private static int ParseIntOrDefault(string value, int fallback)
        => int.TryParse(value.Trim(), out var result) ? result : fallback;

    private static int? ParseNullableInt(string value)
        => int.TryParse(value.Trim(), out var result) ? result : null;

    private static long? ParseNullableLong(string value)
        => long.TryParse(value.Trim(), out var result) ? result : null;

    private static IReadOnlyList<string> SplitCommaList(string value)
        => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static string JoinList(IReadOnlyList<string> values)
        => string.Join(", ", values);

    private static string? NullIfWhiteSpace(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ResolveDefaultConfigPath()
    {
        var TianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        if (string.IsNullOrWhiteSpace(TianShuHome))
        {
            TianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        }

        if (!string.IsNullOrWhiteSpace(TianShuHome))
        {
            return Path.Combine(TianShuHome, "tianshu.toml");
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".tianshu", "tianshu.toml");
    }

    private static string FormatValue(StructuredValue? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return value.Kind switch
        {
            StructuredValueKind.Null => string.Empty,
            StructuredValueKind.String => value.StringValue ?? string.Empty,
            StructuredValueKind.Number => value.NumberValue ?? string.Empty,
            StructuredValueKind.Boolean => value.BooleanValue?.ToString() ?? string.Empty,
            StructuredValueKind.Array => FormatArrayValue(value),
            StructuredValueKind.Object => FormatObjectValue(value),
            _ => string.Empty,
        };
    }

    private static string FormatArrayValue(StructuredValue value)
        => $"[{string.Join(", ", value.Items.Select(FormatValue))}]";

    private static string FormatObjectValue(StructuredValue value)
    {
        var builder = new StringBuilder();
        builder.Append('{');
        var first = true;
        foreach (var pair in value.Properties)
        {
            if (!first)
            {
                builder.Append(", ");
            }

            first = false;
            builder.Append(pair.Key);
            builder.Append(": ");
            builder.Append(FormatValue(pair.Value));
        }

        builder.Append('}');
        return builder.ToString();
    }
}

internal enum ConfigGuiViewMode
{
    Fields,
    Workspace,
    Providers,
    ModelProtocolMappings,
    DefaultModelProtocolRules,
    ModelRouteSets,
    Prompts,
    SkillPackages,
    ToolPackages,
    ProviderPackages,
    McpServerPackages,
    ArtifactStorePackages,
    DiagnosticSinkPackages,
    WorkspaceResolverPackages,
    PolicyStrategyPackages,
}

internal sealed record ProviderModelProbeResult(Uri Endpoint, IReadOnlyList<string> Models);

internal sealed record ProviderConfigItem(string Id, string BaseUrl, string ApiKeyEnv, string DefaultProtocol);

internal sealed class ModelProtocolProviderDraft(string id, IReadOnlyList<ModelProtocolOverrideDraft> overrides, IReadOnlyList<ModelProtocolRuleDraft> rules)
{
    public string Id { get; } = id;

    public List<string> Models { get; } = [];

    public List<ModelProtocolOverrideDraft> Overrides { get; } = overrides.Select(static item => item.Clone()).ToList();

    public List<ModelProtocolRuleDraft> Rules { get; } = rules.Select(static item => item.Clone()).ToList();
}

internal sealed class ModelProtocolOverrideDraft(string name, string protocolsText)
{
    public string Name { get; set; } = name;

    public string ProtocolsText { get; set; } = protocolsText;

    public ModelProtocolOverrideDraft Clone()
        => new(Name, ProtocolsText);
}

internal sealed class ModelProtocolRuleDraft(string match, string protocolsText)
{
    public string Match { get; set; } = match;

    public string ProtocolsText { get; set; } = protocolsText;

    public ModelProtocolRuleDraft Clone()
        => new(Match, ProtocolsText);
}

internal sealed class ModelProtocolRuleSetDraft
{
    public ModelProtocolRuleSetDraft(
        string id,
        string displayName,
        string description,
        IReadOnlyList<ModelProtocolRuleDraft> rules)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        Rules = rules.Select(static rule => rule.Clone()).ToList();
    }

    public string Id { get; set; }

    public string DisplayName { get; set; }

    public string Description { get; set; }

    public List<ModelProtocolRuleDraft> Rules { get; }

    public string DisplayLabel => string.IsNullOrWhiteSpace(DisplayName) ? Id : $"{Id} - {DisplayName}";
}

internal sealed class ModelRouteSetConfigItem
{
    public ModelRouteSetConfigItem(
        string id,
        string displayName,
        string description,
        IReadOnlyList<ModelRouteSetRouteDraft> routes)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        Routes = routes.Select(static route => route.Clone()).ToList();
    }

    public string Id { get; set; }

    public string DisplayName { get; set; }

    public string Description { get; set; }

    public List<ModelRouteSetRouteDraft> Routes { get; }

    public string DisplayLabel => string.IsNullOrWhiteSpace(DisplayName) ? Id : $"{Id} - {DisplayName}";

    public ModelRouteSetConfigItem Clone(string id)
        => new(id, DisplayName, Description, Routes.Select(static route => route.Clone()).ToArray());
}

internal sealed class ModelRouteSetRouteDraft
{
    public ModelRouteSetRouteDraft(string kind, string fallbackRouteKind)
    {
        Kind = kind;
        FallbackRouteKind = fallbackRouteKind;
    }

    public string Kind { get; set; }

    public string FallbackRouteKind { get; set; }

    public List<ModelRouteSetCandidateDraft> Candidates { get; } = [];

    public ModelRouteSetRouteDraft Clone()
    {
        var clone = new ModelRouteSetRouteDraft(Kind, FallbackRouteKind);
        clone.Candidates.AddRange(Candidates.Select(static candidate => candidate.Clone()));
        return clone;
    }
}

internal sealed class ModelRouteSetCandidateDraft
{
    public ModelRouteSetCandidateDraft(
        string provider,
        string model,
        string protocol,
        IReadOnlyList<string> capabilities)
    {
        Provider = provider;
        Model = model;
        Protocol = protocol;
        Capabilities = capabilities.ToArray();
    }

    public string Provider { get; set; }

    public string Model { get; set; }

    public string Protocol { get; set; }

    public IReadOnlyList<string> Capabilities { get; set; }

    public ModelRouteSetCandidateDraft Clone()
        => new(Provider, Model, Protocol, Capabilities);
}

internal sealed class ConfigCategoryRow
{
    public ConfigCategoryRow(string id, string displayName, string description, int fieldCount)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        FieldCount = fieldCount;
        CaptionText.Value = BuildCaption(displayName, fieldCount);
    }

    public string Id { get; }

    public string DisplayName { get; private set; }

    public string Description { get; private set; }

    public int FieldCount { get; private set; }

    public ObservableValue<string> CaptionText { get; } = new(string.Empty);

    public ObservableValue<bool> IsNavigationHighlighted { get; } = new(false);

    public void Update(string displayName, string description, int fieldCount)
    {
        DisplayName = displayName;
        Description = description;
        FieldCount = fieldCount;
        CaptionText.Value = BuildCaption(displayName, fieldCount);
    }

    private static string BuildCaption(string displayName, int fieldCount)
        => displayName;
}

internal sealed record ConfigNavigationModuleRow(string Id, string DisplayName, string Description, IReadOnlyList<ConfigCategoryRow> Pages, bool IsInitiallyExpanded)
{
    public ObservableValue<bool> IsExpanded { get; } = new(IsInitiallyExpanded);
}

internal sealed class ConfigFieldRow
{
    public ConfigFieldRow(
        string key,
        string displayName,
        string description,
        string categoryId,
        string categoryName,
        int categoryOrder,
        string groupId,
        string groupName,
        int groupOrder,
        ConfigurationValueKind valueKind,
        ConfigurationFieldEditMode editMode,
        bool isAdvanced,
        IReadOnlyList<AllowedOptionRow> allowedOptions,
        bool isConfigured,
        bool isSensitive,
        string sourceName,
        string currentValue,
        string defaultValue,
        string issues)
    {
        Key = key;
        DisplayName = displayName;
        Description = description;
        CategoryId = categoryId;
        CategoryName = categoryName;
        CategoryOrder = categoryOrder;
        GroupId = groupId;
        GroupName = groupName;
        GroupOrder = groupOrder;
        ValueKind = valueKind;
        EditMode = editMode;
        IsAdvanced = isAdvanced;
        AllowedOptions = allowedOptions;
        IsConfigured = isConfigured;
        IsSensitive = isSensitive;
        SourceName = sourceName;
        CurrentValue = currentValue;
        DefaultValue = defaultValue;
        Issues = issues;
        EditValue = currentValue;
    }

    public string Key { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public string CategoryId { get; }

    public string CategoryName { get; }

    public int CategoryOrder { get; }

    public string GroupId { get; }

    public string GroupName { get; }

    public int GroupOrder { get; }

    public ConfigurationValueKind ValueKind { get; }

    public ConfigurationFieldEditMode EditMode { get; }

    public bool CanEdit => EditMode != ConfigurationFieldEditMode.ReadOnly
                           && !string.Equals(GroupId, TianShuConfigurationSchemaCatalog.RawUnmappedGroupId, StringComparison.OrdinalIgnoreCase);

    public bool IsEnumChoice => AllowedOptions.Count > 0;

    public bool IsBooleanChoice => ValueKind == ConfigurationValueKind.Boolean;

    public bool UsesChoiceEditor => IsEnumChoice || IsBooleanChoice;

    public bool IsAdvanced { get; }

    public IReadOnlyList<AllowedOptionRow> AllowedOptions { get; private set; }

    public IReadOnlyList<AllowedOptionRow> ChoiceOptions
        => IsEnumChoice
            ? AllowedOptions
            : IsBooleanChoice
                ? BooleanChoiceOptions
                : [];

    public bool IsConfigured { get; }

    public bool IsSensitive { get; }

    public string SourceName { get; }

    public string CurrentValue { get; }

    public string DefaultValue { get; }

    public string Issues { get; }

    public string EditValue { get; set; }

    public string ConfiguredText => IsConfigured ? "已配置" : "默认";

    public string SensitiveText => IsSensitive ? "敏感" : string.Empty;

    public string DirtyText => string.Equals(EditValue, CurrentValue, StringComparison.Ordinal) ? string.Empty : "未保存";

    public string CurrentValueSummary
    {
        get
        {
            if (string.IsNullOrEmpty(CurrentValue))
            {
                if (IsDailyModelSelectionReference && ChoiceOptions.Count > 0)
                {
                    return $"{ChoiceOptions[0].Value} <默认>";
                }

                return IsConfigured ? "<空>" : "<默认>";
            }

            return CurrentValue.Length <= 96 ? CurrentValue : $"{CurrentValue[..93]}...";
        }
    }

    public void ReplaceAllowedOptions(IReadOnlyList<AllowedOptionRow> allowedOptions)
        => AllowedOptions = allowedOptions;

    private bool IsDailyModelSelectionReference
        => string.Equals(Key, "model_route_set", StringComparison.OrdinalIgnoreCase)
           || string.Equals(Key, "model_protocol_rule_set", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<string> BuildHelpLines()
    {
        if (IsEnumChoice)
        {
            return AllowedOptions
                .Select(static option => $"{option.Value}：{option.Description}")
                .ToArray();
        }

        if (IsBooleanChoice)
        {
            return BooleanChoiceOptions
                .Select(static option => $"{option.Value}：{option.Description}")
                .ToArray();
        }

        return [string.IsNullOrWhiteSpace(Description) ? "该配置项暂无说明。" : Description];
    }

    private static readonly IReadOnlyList<AllowedOptionRow> BooleanChoiceOptions =
    [
        new("true", "启用或打开该配置项。"),
        new("false", "禁用或关闭该配置项。"),
    ];
}

internal sealed record AllowedOptionRow(string Value, string Description, string? DisplayLabelOverride = null)
{
    public string DisplayLabel => string.IsNullOrWhiteSpace(DisplayLabelOverride) ? Value : DisplayLabelOverride;

    public static AllowedOptionRow FromContract(ConfigurationAllowedValue value)
        => new(
            value.Value.StringValue ?? value.Value.ToPlainObject()?.ToString() ?? string.Empty,
            value.Description ?? value.DisplayName);
}
