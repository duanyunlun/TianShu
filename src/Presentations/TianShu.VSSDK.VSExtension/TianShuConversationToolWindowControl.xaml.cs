using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Documents;
using EnvDTE;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.Win32;
using TianShu.VSSDK.VSExtension.Services;
using Forms = System.Windows.Forms;

namespace TianShu.VSSDK.VSExtension;

public partial class TianShuConversationToolWindowControl : UserControl, INotifyPropertyChanged
{
    private const int MaxAssistantReasoningChars = 24000;
    private const int MaxAssistantFinalChars = 64000;
    private const int MaxPersistedSidecarDiagnosticEntries = 4000;
    private const int SidecarEventLogPersistDelayMilliseconds = 250;
    private readonly Dictionary<string, ConversationEntry> assistantMessages = new(StringComparer.Ordinal);
    private readonly HashSet<string> finalizedTurnIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> handledTurnErrorKeys = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions SharedPrettyJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };
    private readonly JsonSerializerOptions prettyJsonOptions = SharedPrettyJsonOptions;
    private readonly ObservableCollection<string> actionMethods = new();
    private readonly ObservableCollection<TianShuApprovalDecisionOption> approvalDecisions = new();
    private readonly ObservableCollection<TianShuPermissionScopeOption> permissionScopes = new();
    private readonly ObservableCollection<ConversationEntry> messages = new();
    private readonly ObservableCollection<PendingContextEntry> pendingContextEntries = new();
    private readonly ObservableCollection<PendingFollowUpEntry> pendingFollowUpEntries = new();
    private readonly ObservableCollection<StructuredPermissionField> pendingPermissionFields = new();
    private readonly ObservableCollection<StructuredUserInputQuestion> pendingUserInputQuestions = new();
    private readonly List<PendingInteractiveRequestEntry> pendingInteractiveRequestEntries = new();
    private readonly ObservableCollection<ThreadListEntry> recentThreads = new();
    private readonly ObservableCollection<CollaborationThreadEntry> collaborationThreads = new();
    private readonly ObservableCollection<ConversationPlanStepEntry> currentPlanSteps = new();
    private readonly ObservableCollection<SettingsConfigFieldEntry> settingsConfigFields = new();
    private readonly ObservableCollection<SettingsModelCatalogEntry> settingsModelCatalog = new();
    private readonly ObservableCollection<SidecarEventEntry> sidecarEvents = new();
    private readonly List<PersistedSidecarDiagnosticEntry> sidecarDiagnosticEntries = new();
    private readonly Dictionary<string, TaskActivityEntry> taskActivityEntries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ThreadInputStateSnapshot> threadInputStates = new(StringComparer.Ordinal);
    private readonly TianShuVsTextContextService textContextService = new();
    private readonly HashSet<string> activeToolCallKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ToolActivityEntry> toolActivityEntries = new(StringComparer.Ordinal);
    private readonly StringBuilder reasoningBuffer = new();
    private readonly string sidecarEventLogFilePath = ResolveSidecarEventLogFilePath();
    private readonly object sidecarEventLogPersistGate = new();

    private TianShuSidecarBridge? sidecarBridge;
    private CancellationTokenSource? sidecarEventLogPersistCancellation;
    private Task sidecarEventLogPersistTask = Task.CompletedTask;
    private int sidecarEventLogPersistVersion;
    private string actionPayloadJson = string.Empty;
    private string actionResultText = string.Empty;
    private string approvalNoteText = string.Empty;
    private string capabilityStatusText = "功能面板：可直接触发执行运行时 typed 能力。";
    private string configBatchItemsJson = """
[
  {
    "keyPath": "",
    "value": null
  }
]
""";
    private string configKeyPath = string.Empty;
    private string configValueJson = "null";
    private string currentPlanExplanation = string.Empty;
    private Task? initializeTask;
    private bool hasLoaded;
    private BusySendMode busySendMode = BusySendMode.Queue;
    private bool enterSends = true;
    private bool includeArchivedThreads;
    private bool isInspectorOpen;
    private bool interruptRequestPending;
    private bool submitAwaitingSteerAfterInterrupt;
    private bool isActionRunning;
    private bool isBusy;
    private bool isInitialized;
    private bool isCollaborationPanelExpanded = true;
    private bool isPlanPanelExpanded = true;
    private bool shouldStartNewThreadOnNextSend;
    private string lastGeneratedActionPayload = string.Empty;
    private string latestDiffText = string.Empty;
    private string latestPlanText = string.Empty;
    private bool matchCurrentWorkingDirectory = true;
    private string metadataBranchText = string.Empty;
    private string metadataOriginUrlText = string.Empty;
    private string metadataShaText = string.Empty;
    private string mcpServerStatusText = string.Empty;
    private string pendingApprovalCallId = string.Empty;
    private string pendingApprovalDetailsText = string.Empty;
    private string pendingApprovalSummaryText = "当前没有待处理审批。";
    private bool pendingApprovalAwaitingResolution;
    private string pendingPermissionCallId = string.Empty;
    private string pendingPermissionDetailsText = string.Empty;
    private string pendingPermissionSummaryText = "当前没有待处理权限请求。";
    private bool pendingPermissionAwaitingResolution;
    private string pendingUserInputCallId = string.Empty;
    private string pendingUserInputDetailsText = string.Empty;
    private string pendingUserInputSummaryText = "当前没有待补录请求。";
    private bool pendingUserInputAwaitingResolution;
    private string configPath = string.Empty;
    private string inputText = string.Empty;
    private string appHostProjectPath = string.Empty;
    private string mcpOauthServerName = string.Empty;
    private string pluginMarketplacePath = string.Empty;
    private string pluginName = string.Empty;
    private string profileName = string.Empty;
    private string overrideApprovalPolicy = string.Empty;
    private string overrideCollaborationMode = string.Empty;
    private string effectiveConfigStatusText = "尚未读取当前配置。";
    private string modelCatalogStatusText = "尚未加载模型目录。";
    private string currentConfiguredModelId = string.Empty;
    private string overrideModel = string.Empty;
    private string overrideModelProvider = string.Empty;
    private string overrideSandboxMode = string.Empty;
    private string overrideServiceTier = string.Empty;
    private string overrideWebSearchMode = string.Empty;
    private string reasoningText = string.Empty;
    private string remoteSkillHazelnutId = string.Empty;
    private string rpcMethodText = string.Empty;
    private string rpcPayloadJson = "{}";
    private string rpcResultText = string.Empty;
    private string rollbackTurnsText = "1";
    private string settingsResultText = string.Empty;
    private TianShuApprovalDecisionOption? selectedApprovalDecision;
    private TianShuPermissionScopeOption? selectedPermissionScope;
    private string? selectedActionMethod;
    private TianShuSidecarCapability selectedCapability = TianShuSidecarCapability.RuntimeSurface;
    private SidecarEventEntry? selectedSidecarEvent;
    private ThreadListEntry? selectedThread;
    private bool showAllRecentThreads;
    private int nextSidecarEventSequence = 1;
    private int nextPendingInteractiveRequestSequence = 1;
    private string taskActivityText = string.Empty;
    private string threadOperationResultText = string.Empty;
    private string threadRenameText = string.Empty;
    private string statusText = "状态：正在准备 TianShu 运行时。";
    private string? threadId;
    private string toolActivityText = string.Empty;
    private string workingDirectory = string.Empty;
    private bool isDispatchingPendingFollowUp;
    private int nextSidecarDiagnosticSequence = 1;

    public TianShuConversationToolWindowControl()
    {
        InitializeComponent();
        DataContext = this;
        currentPlanSteps.CollectionChanged += OnCurrentPlanStepsCollectionChanged;
        messages.CollectionChanged += OnMessagesCollectionChanged;
        pendingFollowUpEntries.CollectionChanged += OnPendingFollowUpsCollectionChanged;
        recentThreads.CollectionChanged += OnRecentThreadsCollectionChanged;
        sidecarEvents.CollectionChanged += OnSidecarEventsCollectionChanged;
        permissionScopes.Add(new TianShuPermissionScopeOption(TianShuPermissionGrantScope.Turn));
        permissionScopes.Add(new TianShuPermissionScopeOption(TianShuPermissionGrantScope.Session));
        selectedPermissionScope = permissionScopes[0];
        RefreshActionMethodOptions();
        LoadPersistedSidecarEventLog();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ConversationEntry> Messages => messages;

    public ObservableCollection<ThreadListEntry> RecentThreads => recentThreads;

    public ObservableCollection<CollaborationThreadEntry> CollaborationThreads => collaborationThreads;

    public ObservableCollection<ConversationPlanStepEntry> CurrentPlanSteps => currentPlanSteps;

    public IReadOnlyList<ThreadListEntry> VisibleRecentThreads
        => showAllRecentThreads ? recentThreads.ToArray() : recentThreads.Take(3).ToArray();

    public ObservableCollection<SettingsConfigFieldEntry> SettingsConfigFields => settingsConfigFields;

    public ObservableCollection<SettingsModelCatalogEntry> SettingsModelCatalog => settingsModelCatalog;

    public ObservableCollection<string> ActionMethods => actionMethods;

    public ObservableCollection<TianShuApprovalDecisionOption> ApprovalDecisions => approvalDecisions;

    public ObservableCollection<TianShuPermissionScopeOption> PermissionScopes => permissionScopes;

    public ObservableCollection<PendingContextEntry> PendingContextEntries => pendingContextEntries;

    public ObservableCollection<PendingFollowUpEntry> PendingFollowUpEntries => pendingFollowUpEntries;

    public ObservableCollection<StructuredPermissionField> PendingPermissionFields => pendingPermissionFields;

    public ObservableCollection<StructuredUserInputQuestion> PendingUserInputQuestions => pendingUserInputQuestions;

    public ObservableCollection<SidecarEventEntry> SidecarEvents => sidecarEvents;

    public string StatusText
    {
        get => statusText;
        set => SetField(ref statusText, value);
    }

    public string WorkingDirectory
    {
        get => workingDirectory;
        set
        {
            if (SetField(ref workingDirectory, value))
            {
                ClearSettingsOverviewState();
                OnPropertyChanged(nameof(SettingsConfigScopeText));
                ApplySuggestedActionPayloadIfNeeded();
            }
        }
    }

    public string AppHostProjectPath
    {
        get => appHostProjectPath;
        set
        {
            if (SetField(ref appHostProjectPath, value))
            {
                RefreshCommandState();
            }
        }
    }

    public string ConfigPath
    {
        get => configPath;
        set
        {
            if (SetField(ref configPath, value))
            {
                ClearSettingsOverviewState();
                OnPropertyChanged(nameof(SettingsConfigScopeText));
                RefreshCommandState();
            }
        }
    }

    public string? ThreadId
    {
        get => threadId;
        set
        {
            if (SetField(ref threadId, value))
            {
                SelectCurrentThreadFromRecentList();
                RefreshCollaborationThreadsState();
                ApplySuggestedActionPayloadIfNeeded();
                RefreshCommandState();
            }
        }
    }

    public string CurrentThreadTitle
    {
        get
        {
            if (!HasConversationStarted)
            {
                return "新会话";
            }

            if (SelectedThread is not null && string.Equals(SelectedThread.ThreadId, ThreadId, StringComparison.Ordinal))
            {
                var selectedPreview = BuildThreadDisplayName(SelectedThread);
                if (!string.IsNullOrWhiteSpace(selectedPreview) && !LooksLikeGeneratedThreadId(selectedPreview))
                {
                    return selectedPreview;
                }
            }

            var latestUserPrompt = Messages
                .LastOrDefault(entry => string.Equals(entry.Role, "你", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(entry.Content))
                ?.Content;
            if (!string.IsNullOrWhiteSpace(latestUserPrompt))
            {
                return TrimSingleLine(latestUserPrompt!, 72);
            }

            return !string.IsNullOrWhiteSpace(ThreadId) ? "未命名会话" : "新会话";
        }
    }

    public string CurrentThreadSubtitle
    {
        get
        {
            return GetWorkingDirectoryDisplayName();
        }
    }

    public string StatusSummaryText
        => statusText.StartsWith("状态：", StringComparison.Ordinal) ? statusText.Substring(3).Trim() : statusText;

    public bool IsBusy => isBusy;

    public string ProfileName
    {
        get => profileName;
        set
        {
            if (SetField(ref profileName, value))
            {
                ClearSettingsOverviewState();
                OnPropertyChanged(nameof(SettingsConfigScopeText));
                RefreshCommandState();
            }
        }
    }

    public string EffectiveConfigStatusText
    {
        get => effectiveConfigStatusText;
        set => SetField(ref effectiveConfigStatusText, value);
    }

    public string ModelCatalogStatusText
    {
        get => modelCatalogStatusText;
        set => SetField(ref modelCatalogStatusText, value);
    }

    public string OverrideModel
    {
        get => overrideModel;
        set => SetField(ref overrideModel, value);
    }

    public string OverrideModelProvider
    {
        get => overrideModelProvider;
        set => SetField(ref overrideModelProvider, value);
    }

    public string OverrideApprovalPolicy
    {
        get => overrideApprovalPolicy;
        set => SetField(ref overrideApprovalPolicy, value);
    }

    public string OverrideSandboxMode
    {
        get => overrideSandboxMode;
        set => SetField(ref overrideSandboxMode, value);
    }

    public string OverrideWebSearchMode
    {
        get => overrideWebSearchMode;
        set => SetField(ref overrideWebSearchMode, value);
    }

    public string OverrideServiceTier
    {
        get => overrideServiceTier;
        set => SetField(ref overrideServiceTier, value);
    }

    public string OverrideCollaborationMode
    {
        get => overrideCollaborationMode;
        set
        {
            if (SetField(ref overrideCollaborationMode, value))
            {
                OnPropertyChanged(nameof(IsPlanModeEnabled));
            }
        }
    }

    public bool IsPlanModeEnabled
    {
        get => string.Equals(Normalize(OverrideCollaborationMode), "plan", StringComparison.OrdinalIgnoreCase);
        set
        {
            var nextMode = value ? "plan" : string.Empty;
            if (string.Equals(
                    Normalize(overrideCollaborationMode),
                    Normalize(nextMode),
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            OverrideCollaborationMode = nextMode;
        }
    }

    public ThreadListEntry? SelectedThread
    {
        get => selectedThread;
        set
        {
            if (SetField(ref selectedThread, value))
            {
                SyncSelectedThreadDraft();
                RefreshCommandState();
            }
        }
    }

    public string ThreadRenameText
    {
        get => threadRenameText;
        set
        {
            if (SetField(ref threadRenameText, value))
            {
                RefreshCommandState();
            }
        }
    }

    public string InputText
    {
        get => inputText;
        set
        {
            if (SetField(ref inputText, value))
            {
                RefreshCommandState();
            }
        }
    }

    public string CapabilityStatusText
    {
        get => capabilityStatusText;
        set => SetField(ref capabilityStatusText, value);
    }

    public TianShuSidecarCapability SelectedCapability
    {
        get => selectedCapability;
        set
        {
            if (SetField(ref selectedCapability, value))
            {
                RefreshActionMethodOptions();
                RefreshCommandState();
            }
        }
    }

    public string? SelectedActionMethod
    {
        get => selectedActionMethod;
        set
        {
            if (SetField(ref selectedActionMethod, value))
            {
                ApplySuggestedActionPayloadIfNeeded();
                RefreshCommandState();
            }
        }
    }

    public string ActionPayloadJson
    {
        get => actionPayloadJson;
        set => SetField(ref actionPayloadJson, value);
    }

    public string ActionResultText
    {
        get => actionResultText;
        set => SetField(ref actionResultText, value);
    }

    public string PendingApprovalCallId
    {
        get => pendingApprovalCallId;
        set
        {
            if (SetField(ref pendingApprovalCallId, value))
            {
                RefreshCommandState();
            }
        }
    }

    public string PendingApprovalDetailsText
    {
        get => pendingApprovalDetailsText;
        set => SetField(ref pendingApprovalDetailsText, value);
    }

    public string PendingPermissionCallId
    {
        get => pendingPermissionCallId;
        set
        {
            if (SetField(ref pendingPermissionCallId, value))
            {
                RefreshCommandState();
            }
        }
    }

    public string PendingPermissionDetailsText
    {
        get => pendingPermissionDetailsText;
        set => SetField(ref pendingPermissionDetailsText, value);
    }

    public string PendingPermissionSummaryText
    {
        get => pendingPermissionSummaryText;
        set => SetField(ref pendingPermissionSummaryText, value);
    }

    public string PendingApprovalSummaryText
    {
        get => pendingApprovalSummaryText;
        set => SetField(ref pendingApprovalSummaryText, value);
    }

    public string PendingUserInputSummaryText
    {
        get => pendingUserInputSummaryText;
        set => SetField(ref pendingUserInputSummaryText, value);
    }

    public string ReasoningText
    {
        get => reasoningText;
        set => SetRuntimeOverviewText(ref reasoningText, value, nameof(HasReasoning));
    }

    public string LatestPlanText
    {
        get => latestPlanText;
        set => SetRuntimeOverviewText(ref latestPlanText, value, nameof(HasLatestPlan));
    }

    public string CurrentPlanExplanation
    {
        get => currentPlanExplanation;
        set
        {
            if (!SetField(ref currentPlanExplanation, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasCurrentPlanExplanation));
            OnPropertyChanged(nameof(ShowPlanPanel));
            OnPropertyChanged(nameof(CurrentPlanSummaryText));
        }
    }

    public string LatestDiffText
    {
        get => latestDiffText;
        set => SetRuntimeOverviewText(ref latestDiffText, value, nameof(HasLatestDiff));
    }

    public string McpServerStatusText
    {
        get => mcpServerStatusText;
        set => SetRuntimeOverviewText(ref mcpServerStatusText, value, nameof(HasMcpServerStatus));
    }

    public string ToolActivityText
    {
        get => toolActivityText;
        set => SetRuntimeOverviewText(ref toolActivityText, value, nameof(HasToolActivity));
    }

    public string TaskActivityText
    {
        get => taskActivityText;
        set => SetRuntimeOverviewText(ref taskActivityText, value, nameof(HasTaskActivity));
    }

    public string RemoteSkillHazelnutId
    {
        get => remoteSkillHazelnutId;
        set
        {
            if (SetField(ref remoteSkillHazelnutId, value))
            {
                RefreshCommandState();
            }
        }
    }

    public string ApprovalNoteText
    {
        get => approvalNoteText;
        set => SetField(ref approvalNoteText, value);
    }

    public string PendingUserInputCallId
    {
        get => pendingUserInputCallId;
        set
        {
            if (SetField(ref pendingUserInputCallId, value))
            {
                RefreshCommandState();
            }
        }
    }

    public string PendingUserInputDetailsText
    {
        get => pendingUserInputDetailsText;
        set => SetField(ref pendingUserInputDetailsText, value);
    }

    public string PluginMarketplacePath
    {
        get => pluginMarketplacePath;
        set
        {
            if (SetField(ref pluginMarketplacePath, value))
            {
                RefreshCommandState();
            }
        }
    }

    public string PluginName
    {
        get => pluginName;
        set
        {
            if (SetField(ref pluginName, value))
            {
                RefreshCommandState();
            }
        }
    }

    public string McpOauthServerName
    {
        get => mcpOauthServerName;
        set
        {
            if (SetField(ref mcpOauthServerName, value))
            {
                RefreshCommandState();
            }
        }
    }

    public string ConfigKeyPath
    {
        get => configKeyPath;
        set
        {
            if (SetField(ref configKeyPath, value))
            {
                RefreshCommandState();
            }
        }
    }

    public string ConfigValueJson
    {
        get => configValueJson;
        set
        {
            if (SetField(ref configValueJson, value))
            {
                RefreshCommandState();
            }
        }
    }

    public string ConfigBatchItemsJson
    {
        get => configBatchItemsJson;
        set
        {
            if (SetField(ref configBatchItemsJson, value))
            {
                RefreshCommandState();
            }
        }
    }

    public TianShuApprovalDecisionOption? SelectedApprovalDecision
    {
        get => selectedApprovalDecision;
        set
        {
            if (SetField(ref selectedApprovalDecision, value))
            {
                RefreshCommandState();
            }
        }
    }

    public TianShuPermissionScopeOption? SelectedPermissionScope
    {
        get => selectedPermissionScope;
        set
        {
            if (SetField(ref selectedPermissionScope, value))
            {
                RefreshCommandState();
            }
        }
    }

    public string RpcMethodText
    {
        get => rpcMethodText;
        set
        {
            if (SetField(ref rpcMethodText, value))
            {
                RefreshCommandState();
            }
        }
    }

    public string RpcPayloadJson
    {
        get => rpcPayloadJson;
        set => SetField(ref rpcPayloadJson, value);
    }

    public string RpcResultText
    {
        get => rpcResultText;
        set => SetField(ref rpcResultText, value);
    }

    public string SettingsResultText
    {
        get => settingsResultText;
        set => SetField(ref settingsResultText, value);
    }

    public SidecarEventEntry? SelectedSidecarEvent
    {
        get => selectedSidecarEvent;
        set
        {
            if (SetField(ref selectedSidecarEvent, value))
            {
                OnPropertyChanged(nameof(SelectedSidecarEventDetails));
            }
        }
    }

    public string SelectedSidecarEventDetails => SelectedSidecarEvent?.Details ?? string.Empty;

    public string PendingContextSummary => pendingContextEntries.Count == 0
        ? "未附带 IDE 上下文"
        : $"已附带 {pendingContextEntries.Count} 条 IDE 上下文";

    public string PendingFollowUpSummary => pendingFollowUpEntries.Count == 0
        ? "当前没有待发送内容"
        : $"当前有 {pendingFollowUpEntries.Count} 条待发送内容";

    public string ComposerMetaText => $"{PendingContextSummary} · {EnterBehaviorHint}";

    public bool ShowAllRecentThreads
    {
        get => showAllRecentThreads;
        set
        {
            if (SetField(ref showAllRecentThreads, value))
            {
                NotifyRecentThreadsStateChanged();
            }
        }
    }

    public bool HasMessages => messages.Count > 0;

    public bool HasConversationStarted
        => messages.Any(static entry => !string.IsNullOrWhiteSpace(entry.Content) && !string.Equals(entry.Role, "系统", StringComparison.Ordinal));

    public bool IsInitialized => isInitialized;

    public bool ShowInitializationState => !isInitialized;

    public bool IsInitializationInProgress => !isInitialized && isBusy;

    public bool ShowHomeState => isInitialized && !HasConversationStarted;

    public bool ShowThreadState => isInitialized && HasConversationStarted;

    public bool HasRecentThreads => recentThreads.Count > 0;

    public bool HasNoRecentThreads => recentThreads.Count == 0;

    public bool CanToggleRecentThreads => recentThreads.Count > 3;

    public string RecentThreadsToggleText => showAllRecentThreads ? "收起" : recentThreads.Count > 3 ? $"显示更多 ({recentThreads.Count - 3})" : "显示更多";

    public string RecentThreadsSummaryText => recentThreads.Count switch
    {
        0 => "还没有历史会话。直接在下方输入并发送，即会创建新会话。",
        <= 3 => $"共 {recentThreads.Count} 个历史会话，选择一条即可恢复。",
        _ when showAllRecentThreads => $"共 {recentThreads.Count} 个历史会话，当前已全部展开。",
        _ => $"默认显示最近 3 条，当前共有 {recentThreads.Count} 个历史会话。",
    };

    public bool HasPendingApproval => !string.IsNullOrWhiteSpace(PendingApprovalCallId);

    public bool HasPendingPermission => !string.IsNullOrWhiteSpace(PendingPermissionCallId);

    public bool HasPendingUserInput => !string.IsNullOrWhiteSpace(PendingUserInputCallId);

    public bool HasPendingContext => pendingContextEntries.Count > 0;

    public bool HasPendingFollowUps => pendingFollowUpEntries.Count > 0;

    public bool ShowInlineStatusRegion => HasPendingContext || HasPendingApproval || HasPendingPermission || HasPendingUserInput;

    public bool HasSidecarEvents => sidecarEvents.Count > 0;

    public bool CanClearSidecarEvents => sidecarEvents.Count > 0;

    public bool HasReasoning => !string.IsNullOrWhiteSpace(ReasoningText);

    public bool HasLatestPlan => !string.IsNullOrWhiteSpace(LatestPlanText);

    public bool HasCurrentPlanSteps => currentPlanSteps.Count > 0;

    public bool HasCurrentPlanExplanation => !string.IsNullOrWhiteSpace(CurrentPlanExplanation);

    public bool ShowPlanPanel => HasCurrentPlanSteps || HasCurrentPlanExplanation;

    public bool HasCollaborationThreads => collaborationThreads.Count > 0;

    public bool ShowCollaborationPanel => HasCollaborationThreads;

    public bool HasLatestDiff => !string.IsNullOrWhiteSpace(LatestDiffText);

    public bool HasToolActivity => !string.IsNullOrWhiteSpace(ToolActivityText);

    public bool HasTaskActivity => !string.IsNullOrWhiteSpace(TaskActivityText);

    public bool HasSettingsResult => !string.IsNullOrWhiteSpace(SettingsResultText);

    public bool HasSettingsConfigFields => settingsConfigFields.Count > 0;

    public bool HasSettingsModelCatalog => settingsModelCatalog.Count > 0;

    public string SettingsConfigScopeText
    {
        get
        {
            var configPathText = string.IsNullOrWhiteSpace(ConfigPath) ? "未设置" : ConfigPath;
            var workingDirectoryText = string.IsNullOrWhiteSpace(WorkingDirectory) ? "未设置" : WorkingDirectory;
            var profileText = string.IsNullOrWhiteSpace(ProfileName) ? "默认" : ProfileName;
            return $"配置文件：{configPathText}{Environment.NewLine}工作目录：{workingDirectoryText}{Environment.NewLine}配置档：{profileText}";
        }
    }

    public bool HasMcpServerStatus => !string.IsNullOrWhiteSpace(McpServerStatusText);

    public bool HasRuntimeOverviewEntries => HasReasoning || HasLatestPlan || HasLatestDiff || HasToolActivity || HasTaskActivity || HasMcpServerStatus;

    public bool IsPlanPanelExpanded
    {
        get => isPlanPanelExpanded;
        set => SetField(ref isPlanPanelExpanded, value);
    }

    public bool IsCollaborationPanelExpanded
    {
        get => isCollaborationPanelExpanded;
        set => SetField(ref isCollaborationPanelExpanded, value);
    }

    public string CurrentPlanSummaryText
    {
        get
        {
            if (currentPlanSteps.Count == 0)
            {
                return "当前计划";
            }

            var completedCount = currentPlanSteps.Count(static item => item.IsCompleted);
            var inProgressCount = currentPlanSteps.Count(static item => item.IsInProgress);
            return inProgressCount > 0
                ? $"当前计划 · 已完成 {completedCount}/{currentPlanSteps.Count} · 进行中 {inProgressCount}"
                : $"当前计划 · 已完成 {completedCount}/{currentPlanSteps.Count}";
        }
    }

    public string CollaborationSummaryText
    {
        get
        {
            var subAgentCount = collaborationThreads.Count(static item => !item.IsPrimaryThread);
            if (subAgentCount <= 0)
            {
                return "子代理协作";
            }

            var currentEntry = collaborationThreads.FirstOrDefault(static item => item.IsCurrentThread);
            return currentEntry is not null
                ? $"子代理协作 · {subAgentCount} 个子代理 · 当前 {currentEntry.TitleText}"
                : $"子代理协作 · {subAgentCount} 个子代理";
        }
    }

    public bool IsInspectorOpen
    {
        get => isInspectorOpen;
        set => SetField(ref isInspectorOpen, value);
    }

    public bool IncludeArchivedThreads
    {
        get => includeArchivedThreads;
        set
        {
            if (SetField(ref includeArchivedThreads, value))
            {
                RefreshCommandState();
            }
        }
    }

    public bool MatchCurrentWorkingDirectory
    {
        get => matchCurrentWorkingDirectory;
        set
        {
            if (SetField(ref matchCurrentWorkingDirectory, value))
            {
                RefreshCommandState();
            }
        }
    }

    public string RollbackTurnsText
    {
        get => rollbackTurnsText;
        set
        {
            if (SetField(ref rollbackTurnsText, value))
            {
                RefreshCommandState();
            }
        }
    }

    public string MetadataBranchText
    {
        get => metadataBranchText;
        set => SetField(ref metadataBranchText, value);
    }

    public string MetadataShaText
    {
        get => metadataShaText;
        set => SetField(ref metadataShaText, value);
    }

    public string MetadataOriginUrlText
    {
        get => metadataOriginUrlText;
        set => SetField(ref metadataOriginUrlText, value);
    }

    public string ThreadOperationResultText
    {
        get => threadOperationResultText;
        set => SetField(ref threadOperationResultText, value);
    }

    public string ThreadOperationTargetText
    {
        get
        {
            var target = GetDisplayedThreadOperationTargetId();
            if (string.IsNullOrWhiteSpace(target))
            {
                return "未选择线程";
            }

            if (SelectedThread is not null && string.Equals(SelectedThread.ThreadId, target, StringComparison.Ordinal))
            {
                return $"{BuildThreadDisplayName(SelectedThread)} ({target})";
            }

            return string.Equals(ThreadId, target, StringComparison.Ordinal)
                ? $"当前线程 ({target})"
                : target;
        }
    }

    public string DisplayedThreadFilePathText => ResolveDisplayedThreadFilePath() ?? "当前没有可打开的线程文件。";

    public string RuntimeEventLogPathText => sidecarEventLogFilePath;

    public bool CanOpenDisplayedThreadFile => File.Exists(ResolveDisplayedThreadFilePath());

    public bool CanOpenThreadSessionsDirectory => Directory.Exists(ResolveThreadSessionsDirectoryPath());

    public bool CanOpenRuntimeEventLogFile => File.Exists(sidecarEventLogFilePath);

    public string EnterBehaviorHint => enterSends
        ? "Enter 发送，Shift+Enter 换行"
        : "Ctrl+Enter 发送，Enter 换行";

    public bool EnterSends => enterSends;

    public bool CtrlEnterSends => !enterSends;

    public string EnterBehaviorShortText => enterSends
        ? "Enter 发送"
        : "Ctrl+Enter 发送";

    public string SendButtonText => isBusy ? "加入待发送" : "发送";

    public string FollowUpModeText => busySendMode switch
    {
        BusySendMode.Steer => "忙时：引导当前回合",
        BusySendMode.Interrupt => "忙时：中断后跟进",
        _ => "忙时：排队跟进",
    };

    public string BusyModeShortText => busySendMode switch
    {
        BusySendMode.Steer => "忙时引导",
        BusySendMode.Interrupt => "忙时中断",
        _ => "忙时排队",
    };

    public bool CanSend => !string.IsNullOrWhiteSpace(InputText)
        && isInitialized
        && HasBootstrapInputs
        && !isActionRunning;

    public bool ShowSteerFollowUpButton => isBusy && isInitialized;

    public bool CanSteerFollowUp => ShowSteerFollowUpButton && CanSend;

    public bool CanInterrupt => isBusy && isInitialized && sidecarBridge is not null && !interruptRequestPending && !isActionRunning;

    public bool CanReset => !isBusy && !isActionRunning && (isInitialized || Messages.Count > 0 || !string.IsNullOrWhiteSpace(ThreadId));

    public bool CanRefreshThreads => !isBusy && !isActionRunning && isInitialized && sidecarBridge is not null;

    public bool CanClearAllThreads => !isBusy && !isActionRunning && isInitialized && sidecarBridge is not null && recentThreads.Count > 0;

    public bool CanResumeThread => !isBusy
        && !isActionRunning
        && isInitialized
        && sidecarBridge is not null
        && SelectedThread is not null
        && !string.Equals(SelectedThread.ThreadId, ThreadId, StringComparison.Ordinal);

    public bool CanRenameThread => !isBusy
        && !isActionRunning
        && isInitialized
        && sidecarBridge is not null
        && SelectedThread is not null
        && !string.IsNullOrWhiteSpace(ThreadRenameText)
        && !string.Equals((ThreadRenameText ?? string.Empty).Trim(), GetThreadEditableTitle(SelectedThread), StringComparison.Ordinal);

    public bool CanArchiveThread => !isBusy
        && !isActionRunning
        && isInitialized
        && sidecarBridge is not null
        && SelectedThread is not null
        && !SelectedThread.IsArchived;

    public bool CanDeleteThread => !isBusy
        && !isActionRunning
        && isInitialized
        && sidecarBridge is not null
        && !string.IsNullOrWhiteSpace(ResolveThreadOperationTargetId());

    public bool CanForkThread => !isBusy
        && !isActionRunning
        && isInitialized
        && sidecarBridge is not null
        && !string.IsNullOrWhiteSpace(ResolveThreadOperationTargetId());

    public bool CanReadThread => !isBusy
        && !isActionRunning
        && isInitialized
        && sidecarBridge is not null
        && !string.IsNullOrWhiteSpace(ResolveThreadOperationTargetId());

    public bool CanUnarchiveThread => !isBusy
        && !isActionRunning
        && isInitialized
        && sidecarBridge is not null
        && SelectedThread?.IsArchived == true;

    public bool CanRollbackThread => !isBusy
        && !isActionRunning
        && isInitialized
        && sidecarBridge is not null
        && !string.IsNullOrWhiteSpace(ResolveThreadOperationTargetId())
        && TryGetRollbackTurns(out _);

    public bool CanUpdateThreadMetadata => !isBusy
        && !isActionRunning
        && isInitialized
        && sidecarBridge is not null
        && !string.IsNullOrWhiteSpace(ResolveThreadOperationTargetId());

    public bool CanExecuteCapabilityAction => !isBusy
        && isInitialized
        && sidecarBridge is not null
        && !isActionRunning
        && (!CapabilityRequiresMethod(SelectedCapability) || !string.IsNullOrWhiteSpace(SelectedActionMethod));

    public bool CanFillActionPayload => !isBusy && !isActionRunning;

    public bool CanRespondToApproval => !isBusy
        && isInitialized
        && sidecarBridge is not null
        && !isActionRunning
        && !string.IsNullOrWhiteSpace(PendingApprovalCallId)
        && HasActionablePendingInteractiveRequest("approval_requested")
        && SelectedApprovalDecision is not null;

    public bool CanSubmitUserInput => !isBusy
        && isInitialized
        && sidecarBridge is not null
        && !isActionRunning
        && !string.IsNullOrWhiteSpace(PendingUserInputCallId)
        && HasActionablePendingInteractiveRequest("request_user_input")
        && pendingUserInputQuestions.Count > 0;

    public bool CanRespondToPermission => !isBusy
        && isInitialized
        && sidecarBridge is not null
        && !isActionRunning
        && !string.IsNullOrWhiteSpace(PendingPermissionCallId)
        && HasActionablePendingInteractiveRequest("permission_requested")
        && SelectedPermissionScope is not null;

    public bool CanAddCurrentFileContext => HasBootstrapInputs && !isActionRunning;

    public bool CanAddSelectionContext => HasBootstrapInputs && !isActionRunning;

    public bool CanAddSpecifiedFileContext => !isActionRunning;

    public bool CanBrowseBootstrapInputs => !isBusy && !isActionRunning;

    public bool CanReconnect => HasBootstrapInputs && !isBusy && !isActionRunning;

    public bool CanExecuteSettingsAction => HasBootstrapInputs && !isBusy && !isActionRunning;

    public bool CanClearPendingContext => pendingContextEntries.Count > 0;

    public bool CanInvokeRuntimeSurface => !isBusy
        && isInitialized
        && sidecarBridge is not null
        && !isActionRunning
        && !string.IsNullOrWhiteSpace(RpcMethodText);

    private bool HasBootstrapInputs => File.Exists(ConfigPath) && File.Exists(AppHostProjectPath) && Directory.Exists(WorkingDirectory);

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (hasLoaded)
        {
            return;
        }

        hasLoaded = true;
        ConfigureDefaults();

        if (!HasBootstrapInputs)
        {
            return;
        }

        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppendSystemMessage(ex.Message);
        }
    }

    private async void OnUnloaded(object sender, RoutedEventArgs e)
    {
        await DisposeBridgeAsync().ConfigureAwait(true);
    }

    private async void OnSendClick(object sender, RoutedEventArgs e)
    {
        await SendCurrentInputAsync().ConfigureAwait(true);
    }

    private async void OnInterruptClick(object sender, RoutedEventArgs e)
    {
        if (sidecarBridge is null || !isInitialized)
        {
            return;
        }

        SetInterruptRequestPending(true);
        submitAwaitingSteerAfterInterrupt = false;
        try
        {
            ForceInterruptCurrentTurnLocally("当前回合已被强制中断。");
            SetBusy(true, BuildStatusText("正在重建运行时，请稍候。", "正在重建运行时。"), keepInitialized: false);
            await sidecarBridge.ForceInterruptAsync(CancellationToken.None).ConfigureAwait(true);
            await CompleteForceInterruptRecoveryAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            submitAwaitingSteerAfterInterrupt = false;
            SetInterruptRequestPending(false);
            isInitialized = false;
            AppendSystemMessage($"强制中断失败：{ex.Message}", addBlankLineBefore: true);
            SetBusy(false, BuildStatusText("强制中断失败，请手动重连运行时。", "强制中断失败。"), keepInitialized: false);
        }
    }

    private async Task CompleteForceInterruptRecoveryAsync()
    {
        isInitialized = true;
        SetInterruptRequestPending(false);
        AppendSystemMessage("已强制中断当前回合，并完成运行时重建。", addBlankLineBefore: true);
        SetBusy(false, BuildStatusText("当前回合已中断，可继续发送。", "运行时已就绪。"), keepInitialized: true);
        await TryDispatchPendingFollowUpsAsync().ConfigureAwait(true);
    }

    private async void OnPromotePendingFollowUpClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not PendingFollowUpEntry entry)
        {
            return;
        }

        PromotePendingFollowUpToSteer(entry);
        await TryDispatchPendingFollowUpsAsync().ConfigureAwait(true);
    }

    private void OnDeletePendingFollowUpClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not PendingFollowUpEntry entry)
        {
            return;
        }

        pendingFollowUpEntries.Remove(entry);
    }

    private void OnOpenSendOptionsMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.ContextMenu is not { } contextMenu)
        {
            return;
        }

        contextMenu.PlacementTarget = element;
        contextMenu.IsOpen = true;
        e.Handled = true;
    }

    private void OnEnterBehaviorMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.Tag is not string tag)
        {
            return;
        }

        SetEnterBehavior(string.Equals(tag, "enter", StringComparison.OrdinalIgnoreCase));
        e.Handled = true;
    }

    private async void OnNewSessionClick(object sender, RoutedEventArgs e)
    {
        await StartNewSessionAsync().ConfigureAwait(true);
    }

    private async void OnRefreshThreadsClick(object sender, RoutedEventArgs e)
    {
        await RefreshRecentThreadsAsync(selectCurrentThread: true).ConfigureAwait(true);
    }

    private void OnToggleRecentThreadsViewClick(object sender, RoutedEventArgs e)
    {
        ShowAllRecentThreads = !ShowAllRecentThreads;
    }

    private async void OnRecentThreadClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not ThreadListEntry entry)
        {
            return;
        }

        SelectedThread = entry;
        if (CanResumeThread)
        {
            await ResumeSelectedThreadAsync().ConfigureAwait(true);
        }
    }

    private async void OnResumeThreadClick(object sender, RoutedEventArgs e)
    {
        await ResumeSelectedThreadAsync().ConfigureAwait(true);
    }

    private async void OnResumeThreadItemClick(object sender, RoutedEventArgs e)
    {
        if (!TrySelectThreadEntryFromSender(sender, out var entry) || entry.IsCurrentThread || !CanRefreshThreads)
        {
            return;
        }

        e.Handled = true;
        await ResumeSelectedThreadAsync().ConfigureAwait(true);
    }

    private async void OnJumpCollaborationThreadClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CollaborationThreadEntry entry }
            || !CanRefreshThreads
            || !entry.CanJump)
        {
            return;
        }

        var targetThread = FindThreadEntry(entry.ThreadId);
        if (targetThread is null)
        {
            return;
        }

        SelectedThread = targetThread;
        e.Handled = true;
        await ResumeSelectedThreadAsync().ConfigureAwait(true);
    }

    private void OnOpenConversationPanelClick(object sender, RoutedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        OpenInspectorTab(0);
    }

    private void OnOpenActivityPanelClick(object sender, RoutedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        OpenInspectorTab(1);
    }

    private async void OnOpenSettingsPanelClick(object sender, RoutedEventArgs e)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        OpenInspectorTab(2);

        if (!CanExecuteSettingsAction)
        {
            return;
        }

        await RefreshSettingsOverviewAsync(loadModelCatalog: settingsModelCatalog.Count == 0).ConfigureAwait(true);
        OpenInspectorTab(2);
    }

    private void OnClearSidecarEventsClick(object sender, RoutedEventArgs e)
    {
        ClearSidecarEventLog(deleteLocalFile: true);
    }

    private void OnCloseInspectorClick(object sender, RoutedEventArgs e)
    {
        CloseInspector();
    }

    private void OnRootPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsInspectorOpen || e.Key != Key.Escape)
        {
            return;
        }

        CloseInspector();
        e.Handled = true;
    }

    private void OnInspectorContentPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer hostScrollViewer)
        {
            return;
        }

        if (!IsDescendantOf(e.OriginalSource as DependencyObject, hostScrollViewer))
        {
            return;
        }

        var nestedScrollViewer = FindNestedScrollViewer(e.OriginalSource as DependencyObject, hostScrollViewer);
        if (nestedScrollViewer is not null)
        {
            var canNestedScrollUp = e.Delta > 0 && nestedScrollViewer.VerticalOffset > 0;
            var canNestedScrollDown = e.Delta < 0 && nestedScrollViewer.VerticalOffset < nestedScrollViewer.ScrollableHeight;
            if (canNestedScrollUp || canNestedScrollDown)
            {
                return;
            }
        }

        if (hostScrollViewer.ScrollableHeight <= 0)
        {
            return;
        }

        var nextOffset = hostScrollViewer.VerticalOffset - (e.Delta / 3d);
        nextOffset = Math.Max(0d, Math.Min(hostScrollViewer.ScrollableHeight, nextOffset));
        hostScrollViewer.ScrollToVerticalOffset(nextOffset);
        e.Handled = true;
    }

    private void OnConversationEmbeddedDocumentPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject source)
        {
            return;
        }

        var nestedScrollViewer = FindDescendantScrollViewer(source);
        if (nestedScrollViewer is null)
        {
            return;
        }

        var canNestedScrollUp = e.Delta > 0 && nestedScrollViewer.VerticalOffset > 0;
        var canNestedScrollDown = e.Delta < 0 && nestedScrollViewer.VerticalOffset < nestedScrollViewer.ScrollableHeight;
        if (canNestedScrollUp || canNestedScrollDown)
        {
            ScrollViewerByDelta(nestedScrollViewer, e.Delta);
            e.Handled = true;
            return;
        }

        var hostScrollViewer = FindAncestorScrollViewer(source);
        if (hostScrollViewer is null || hostScrollViewer.ScrollableHeight <= 0)
        {
            return;
        }

        ScrollViewerByDelta(hostScrollViewer, e.Delta);
        e.Handled = true;
    }

    private static ScrollViewer? FindNestedScrollViewer(DependencyObject? source, ScrollViewer rootScrollViewer)
    {
        for (var current = source; current is not null && current != rootScrollViewer; current = GetParentObject(current))
        {
            if (current is ScrollViewer scrollViewer && scrollViewer != rootScrollViewer)
            {
                return scrollViewer;
            }
        }

        return null;
    }

    private static ScrollViewer? FindAncestorScrollViewer(DependencyObject source)
    {
        for (var current = GetParentObject(source); current is not null; current = GetParentObject(current))
        {
            if (current is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }
        }

        return null;
    }

    private static ScrollViewer? FindDescendantScrollViewer(DependencyObject source)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(source); index++)
        {
            var child = VisualTreeHelper.GetChild(source, index);
            if (child is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }

            var descendant = FindDescendantScrollViewer(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static bool IsDescendantOf(DependencyObject? source, DependencyObject ancestor)
    {
        for (var current = source; current is not null; current = GetParentObject(current))
        {
            if (current == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    private static void ScrollViewerByDelta(ScrollViewer scrollViewer, int delta)
    {
        var nextOffset = scrollViewer.VerticalOffset - (delta / 3d);
        nextOffset = Math.Max(0d, Math.Min(scrollViewer.ScrollableHeight, nextOffset));
        scrollViewer.ScrollToVerticalOffset(nextOffset);
    }

    private static DependencyObject? GetParentObject(DependencyObject child)
    {
        return child switch
        {
            Visual visual => VisualTreeHelper.GetParent(visual) ?? LogicalTreeHelper.GetParent(visual),
            System.Windows.Media.Media3D.Visual3D visual3D => VisualTreeHelper.GetParent(visual3D) ?? LogicalTreeHelper.GetParent(visual3D),
            FrameworkContentElement contentElement => contentElement.Parent,
            _ => LogicalTreeHelper.GetParent(child),
        };
    }

    private async void OnRenameThreadClick(object sender, RoutedEventArgs e)
    {
        await RenameSelectedThreadAsync().ConfigureAwait(true);
    }

    private async void OnArchiveThreadClick(object sender, RoutedEventArgs e)
    {
        await ArchiveSelectedThreadAsync().ConfigureAwait(true);
    }

    private async void OnArchiveThreadItemClick(object sender, RoutedEventArgs e)
    {
        if (!TrySelectThreadEntryFromSender(sender, out var entry) || entry.IsArchived || !CanRefreshThreads)
        {
            return;
        }

        e.Handled = true;
        await ArchiveSelectedThreadAsync().ConfigureAwait(true);
    }

    private async void OnThreadListFilterChanged(object sender, RoutedEventArgs e)
    {
        RefreshCommandState();
        if (!isInitialized || sidecarBridge is null)
        {
            return;
        }

        await RefreshRecentThreadsAsync(selectCurrentThread: true).ConfigureAwait(true);
    }

    private async void OnForkThreadClick(object sender, RoutedEventArgs e)
    {
        await ForkSelectedThreadAsync().ConfigureAwait(true);
    }

    private async void OnReadThreadClick(object sender, RoutedEventArgs e)
    {
        await ReadSelectedThreadAsync().ConfigureAwait(true);
    }

    private async void OnDeleteThreadClick(object sender, RoutedEventArgs e)
    {
        await DeleteSelectedThreadAsync().ConfigureAwait(true);
    }

    private async void OnDeleteThreadItemClick(object sender, RoutedEventArgs e)
    {
        if (!TrySelectThreadEntryFromSender(sender, out _) || !CanRefreshThreads)
        {
            return;
        }

        e.Handled = true;
        await DeleteSelectedThreadAsync().ConfigureAwait(true);
    }

    private async void OnClearAllThreadsClick(object sender, RoutedEventArgs e)
    {
        await ClearAllThreadsAsync().ConfigureAwait(true);
    }

    private async void OnUnarchiveThreadClick(object sender, RoutedEventArgs e)
    {
        await UnarchiveSelectedThreadAsync().ConfigureAwait(true);
    }

    private async void OnUnarchiveThreadItemClick(object sender, RoutedEventArgs e)
    {
        if (!TrySelectThreadEntryFromSender(sender, out var entry) || !entry.IsArchived || !CanRefreshThreads)
        {
            return;
        }

        e.Handled = true;
        await UnarchiveSelectedThreadAsync().ConfigureAwait(true);
    }

    private async void OnRollbackThreadClick(object sender, RoutedEventArgs e)
    {
        await RollbackSelectedThreadAsync().ConfigureAwait(true);
    }

    private async void OnUpdateThreadMetadataClick(object sender, RoutedEventArgs e)
    {
        await UpdateSelectedThreadMetadataAsync().ConfigureAwait(true);
    }

    private void OnEnterBehaviorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            SetEnterBehavior(comboBox.SelectedIndex <= 0);
        }
    }

    private void OnBusySendModeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox)
        {
            return;
        }

        SetBusySendMode(comboBox.SelectedIndex switch
        {
            1 => BusySendMode.Steer,
            2 => BusySendMode.Interrupt,
            _ => BusySendMode.Queue,
        });
    }

    private void OnEnterBehaviorOptionChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton radioButton || radioButton.Tag is not string tag)
        {
            return;
        }

        SetEnterBehavior(!string.Equals(tag, "ctrl-enter", StringComparison.OrdinalIgnoreCase));
    }

    private void OnBusySendModeOptionChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton radioButton || radioButton.Tag is not string tag)
        {
            return;
        }

        SetBusySendMode(tag switch
        {
            "steer" => BusySendMode.Steer,
            "interrupt" => BusySendMode.Interrupt,
            _ => BusySendMode.Queue,
        });
    }

    private void OnToggleEnterBehaviorClick(object sender, RoutedEventArgs e)
    {
        SetEnterBehavior(!enterSends);
    }

    private void OnCycleBusySendModeClick(object sender, RoutedEventArgs e)
    {
        SetBusySendMode(busySendMode switch
        {
            BusySendMode.Queue => BusySendMode.Steer,
            BusySendMode.Steer => BusySendMode.Interrupt,
            _ => BusySendMode.Queue,
        });
    }

    private void OnCapabilitySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox
            || comboBox.SelectedItem is not ComboBoxItem item
            || item.Tag is not string tag
            || !Enum.TryParse<TianShuSidecarCapability>(tag, ignoreCase: true, out var capability))
        {
            return;
        }

        SelectedCapability = capability;
    }

    private void OnFillActionPayloadClick(object sender, RoutedEventArgs e)
    {
        ForceSuggestedActionPayload();
    }

    private async void OnExecuteCapabilityActionClick(object sender, RoutedEventArgs e)
    {
        await ExecuteCapabilityActionAsync().ConfigureAwait(true);
    }

    private async void OnSubmitApprovalClick(object sender, RoutedEventArgs e)
    {
        await SubmitApprovalAsync().ConfigureAwait(true);
    }

    private async void OnSubmitPendingUserInputClick(object sender, RoutedEventArgs e)
    {
        await SubmitPendingUserInputAsync().ConfigureAwait(true);
    }

    private async void OnSubmitPendingPermissionClick(object sender, RoutedEventArgs e)
    {
        await SubmitPendingPermissionAsync().ConfigureAwait(true);
    }

    private void OnAddCurrentFileContextClick(object sender, RoutedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        AddEditorContext(includeSelectionOnly: false);
    }

    private void OnAddSelectionContextClick(object sender, RoutedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        AddEditorContext(includeSelectionOnly: true);
    }

    private void OnAddSpecifiedFileContextClick(object sender, RoutedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        AddSpecifiedFileContext();
    }

    private void OnBrowseWorkingDirectoryClick(object sender, RoutedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        BrowseWorkingDirectory();
    }

    private void OnBrowseConfigPathClick(object sender, RoutedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        BrowseConfigPath();
    }

    private void OnBrowseAppHostProjectPathClick(object sender, RoutedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        BrowseAppHostProjectPath();
    }

    private void OnResetBootstrapDefaultsClick(object sender, RoutedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        ConfigureDefaults();
        AppendSystemMessage("已恢复默认启动配置。", addBlankLineBefore: true);
    }

    private async void OnReconnectClick(object sender, RoutedEventArgs e)
    {
        await ReconnectRuntimeAsync().ConfigureAwait(true);
    }

    private async void OnReadConfigClick(object sender, RoutedEventArgs e)
    {
        await RefreshCurrentConfigOverviewAsync().ConfigureAwait(true);
    }

    private async void OnReadConfigRequirementsClick(object sender, RoutedEventArgs e)
    {
        await ExecuteConfigRequirementsReadAsync().ConfigureAwait(true);
    }

    private async void OnListModelsClick(object sender, RoutedEventArgs e)
    {
        await RefreshModelCatalogAsync().ConfigureAwait(true);
    }

    private async Task RefreshSettingsOverviewAsync(bool loadModelCatalog)
    {
        await RefreshCurrentConfigOverviewAsync().ConfigureAwait(true);
        if (loadModelCatalog)
        {
            await RefreshModelCatalogAsync().ConfigureAwait(true);
        }
    }

    private async Task RefreshCurrentConfigOverviewAsync()
    {
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            SetActionRunning(true, "设置：正在读取当前配置。");
            var result = await sidecarBridge.ReadConfigAsync(WorkingDirectory, includeLayers: true, CancellationToken.None).ConfigureAwait(true);
            ApplyCurrentConfigOverview(result);
            SettingsResultText = string.Empty;
            CapabilityStatusText = $"设置：{result.Message}";
        }
        catch (Exception ex)
        {
            SettingsResultText = ex.Message;
            CapabilityStatusText = "设置：执行失败。";
        }
        finally
        {
            SetActionRunning(false, CapabilityStatusText);
        }
    }

    private async Task RefreshModelCatalogAsync()
    {
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            SetActionRunning(true, "设置：正在加载模型目录。");
            var result = await sidecarBridge.ListModelsAsync(limit: 50, includeHidden: false, CancellationToken.None).ConfigureAwait(true);
            ApplyModelCatalogOverview(result);
            SettingsResultText = string.Empty;
            CapabilityStatusText = $"设置：{result.Message}";
        }
        catch (Exception ex)
        {
            SettingsResultText = ex.Message;
            CapabilityStatusText = "设置：执行失败。";
        }
        finally
        {
            SetActionRunning(false, CapabilityStatusText);
        }
    }

    private async void OnListExperimentalFeaturesClick(object sender, RoutedEventArgs e)
    {
        await ExecuteExperimentalFeatureListAsync().ConfigureAwait(true);
    }

    private async void OnListCollaborationModesClick(object sender, RoutedEventArgs e)
    {
        await ExecuteCollaborationModeListAsync().ConfigureAwait(true);
    }

    private async void OnListMcpServerStatusClick(object sender, RoutedEventArgs e)
    {
        await ExecuteMcpServerStatusListAsync().ConfigureAwait(true);
    }

    private async void OnReloadMcpServerClick(object sender, RoutedEventArgs e)
    {
        await ExecuteReloadMcpServersAsync().ConfigureAwait(true);
    }

    private async void OnListSkillsClick(object sender, RoutedEventArgs e)
    {
        await ExecuteSkillsListAsync().ConfigureAwait(true);
    }

    private async void OnListRemoteSkillsClick(object sender, RoutedEventArgs e)
    {
        await ExecuteRemoteSkillsListAsync().ConfigureAwait(true);
    }

    private async void OnListPluginsClick(object sender, RoutedEventArgs e)
    {
        await ExecutePluginListAsync().ConfigureAwait(true);
    }

    private async void OnListAppsClick(object sender, RoutedEventArgs e)
    {
        await ExecuteAppListAsync().ConfigureAwait(true);
    }

    private async void OnExportRemoteSkillsClick(object sender, RoutedEventArgs e)
    {
        await ExecuteRemoteSkillExportAsync().ConfigureAwait(true);
    }

    private async void OnReadPluginDetailsClick(object sender, RoutedEventArgs e)
    {
        await ExecutePluginReadAsync().ConfigureAwait(true);
    }

    private async void OnInstallPluginClick(object sender, RoutedEventArgs e)
    {
        await ExecutePluginInstallAsync().ConfigureAwait(true);
    }

    private async void OnStartReviewClick(object sender, RoutedEventArgs e)
    {
        await ExecuteStartReviewAsync().ConfigureAwait(true);
    }

    private async void OnWriteConfigValueClick(object sender, RoutedEventArgs e)
    {
        await ExecuteConfigValueWriteAsync().ConfigureAwait(true);
    }

    private async void OnWriteConfigBatchClick(object sender, RoutedEventArgs e)
    {
        await ExecuteConfigBatchWriteAsync().ConfigureAwait(true);
    }

    private async void OnMcpOauthLoginClick(object sender, RoutedEventArgs e)
    {
        await ExecuteMcpOauthLoginAsync().ConfigureAwait(true);
    }

    private async void OnConversationSummaryClick(object sender, RoutedEventArgs e)
    {
        await ExecuteConversationSummaryAsync().ConfigureAwait(true);
    }

    private async void OnGitDiffToRemoteClick(object sender, RoutedEventArgs e)
    {
        await ExecuteGitDiffToRemoteAsync().ConfigureAwait(true);
    }

    private void OnClearPendingContextClick(object sender, RoutedEventArgs e)
    {
        pendingContextEntries.Clear();
        RefreshCommandState();
        OnPropertyChanged(nameof(PendingContextSummary));
    }

    private async void OnInvokeRuntimeSurfaceClick(object sender, RoutedEventArgs e)
    {
        await InvokeRuntimeSurfaceAsync().ConfigureAwait(true);
    }

    private async void OnInputPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var shouldSend = enterSends
            ? e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
            : e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (!shouldSend)
        {
            return;
        }

        e.Handled = true;
        await SendCurrentInputAsync().ConfigureAwait(true);
    }
    private void ConfigureDefaults()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        WorkingDirectory = TianShuDevPathLocator.ResolveDefaultWorkingDirectory();
        ConfigPath = TianShuDevPathLocator.ResolveTianShuConfigPath();
        AppHostProjectPath = TianShuDevPathLocator.ResolveAppHostProjectPath(WorkingDirectory) ?? string.Empty;
        ProfileName = string.Empty;
        ResetRuntimeOverrides();
        ThreadId = null;

        if (!Directory.Exists(WorkingDirectory))
        {
            StatusText = "状态：未找到可用的工作目录。";
            return;
        }

        if (!File.Exists(ConfigPath))
        {
            StatusText = $"状态：未找到配置文件 {ConfigPath}";
            return;
        }

        if (!File.Exists(AppHostProjectPath))
        {
            StatusText = "状态：未找到 TianShu.AppHost.csproj。";
            return;
        }

        StatusText = "状态：正在启动 TianShu 运行时。";
    }

    private async Task EnsureInitializedAsync()
    {
        if (isInitialized)
        {
            return;
        }

        if (initializeTask is null)
        {
            initializeTask = InitializeCoreAsync();
        }

        try
        {
            await initializeTask.ConfigureAwait(true);
        }
        finally
        {
            if (initializeTask?.IsCompleted == true)
            {
                initializeTask = null;
            }
        }
    }

    private async Task InitializeCoreAsync()
    {
        ValidateBootstrapInputs();

        SetBusy(true, "状态：正在初始化执行运行时……", keepInitialized: false);

        if (sidecarBridge is null)
        {
            sidecarBridge = new TianShuSidecarBridge();
            sidecarBridge.EventReceived += OnSidecarEventReceived;
        }

        try
        {
            await sidecarBridge.InitializeAsync(
                    new TianShuSidecarLaunchOptions
                    {
                        WorkingDirectory = WorkingDirectory,
                        ConfigPath = ConfigPath,
                        ProfileName = Normalize(ProfileName),
                        AppHostProjectPath = AppHostProjectPath,
                        CreateThreadOnInitialize = false,
                        Model = Normalize(OverrideModel),
                        ModelProvider = Normalize(OverrideModelProvider),
                        ApprovalPolicy = Normalize(OverrideApprovalPolicy),
                        SandboxMode = Normalize(OverrideSandboxMode),
                        WebSearchMode = Normalize(OverrideWebSearchMode),
                        ServiceTier = Normalize(OverrideServiceTier),
                        CollaborationMode = Normalize(OverrideCollaborationMode),
                    },
                    CancellationToken.None)
                .ConfigureAwait(true);

            isInitialized = true;
            SetBusy(false, "状态：运行时已连接，可直接发送消息。", keepInitialized: true);
            await RefreshRecentThreadsAsync(selectCurrentThread: true).ConfigureAwait(true);
            FocusInput();
        }
        catch
        {
            isInitialized = false;
            SetBusy(false, "状态：运行时初始化失败。", keepInitialized: false);
            throw;
        }
    }

    private async Task ReconnectRuntimeAsync()
    {
        try
        {
            ValidateBootstrapInputs();
            SettingsResultText = string.Empty;
            StatusText = "状态：正在重连 TianShu 运行时。";
            ClearRuntimeUiState();
            await DisposeBridgeAsync().ConfigureAwait(true);
            await EnsureInitializedAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppendSystemMessage($"重连失败：{ex.Message}", addBlankLineBefore: true);
            StatusText = "状态：运行时重连失败。";
        }
    }

    private void ClearRuntimeUiState()
    {
        shouldStartNewThreadOnNextSend = false;
        assistantMessages.Clear();
        finalizedTurnIds.Clear();
        handledTurnErrorKeys.Clear();
        activeToolCallKeys.Clear();
        taskActivityEntries.Clear();
        toolActivityEntries.Clear();
        threadInputStates.Clear();
        pendingFollowUpEntries.Clear();
        isDispatchingPendingFollowUp = false;
        reasoningBuffer.Clear();
        recentThreads.Clear();
        ClearSidecarEventLog(deleteLocalFile: true);
        ClearPendingContextEntries();
        ClearPendingActionRequests();
        Messages.Clear();
        ThreadId = null;
        SelectedThread = null;
        ThreadRenameText = string.Empty;
        InputText = string.Empty;
        LatestDiffText = string.Empty;
        LatestPlanText = string.Empty;
        ClearCurrentPlan();
        McpServerStatusText = string.Empty;
        ReasoningText = string.Empty;
        SettingsResultText = string.Empty;
        ClearSettingsOverviewState();
        ActionResultText = string.Empty;
        RpcResultText = string.Empty;
        TaskActivityText = string.Empty;
        ThreadOperationResultText = string.Empty;
        ToolActivityText = string.Empty;
    }

    private void ResetRuntimeOverrides()
    {
        OverrideModel = string.Empty;
        OverrideModelProvider = string.Empty;
        OverrideApprovalPolicy = string.Empty;
        OverrideSandboxMode = string.Empty;
        OverrideWebSearchMode = string.Empty;
        OverrideServiceTier = string.Empty;
        OverrideCollaborationMode = string.Empty;
    }

    private void ClearPendingActionRequests()
    {
        pendingInteractiveRequestEntries.Clear();
        nextPendingInteractiveRequestSequence = 1;
        RefreshPendingInteractiveRequestState();
    }

    private void ClearPendingApprovalRequestState()
    {
        PendingApprovalCallId = string.Empty;
        PendingApprovalDetailsText = string.Empty;
        PendingApprovalSummaryText = "当前没有待处理审批。";
        pendingApprovalAwaitingResolution = false;
        ApprovalNoteText = string.Empty;
        approvalDecisions.Clear();
        SelectedApprovalDecision = null;
    }

    private void ClearPendingPermissionRequestState()
    {
        PendingPermissionCallId = string.Empty;
        PendingPermissionDetailsText = string.Empty;
        PendingPermissionSummaryText = "当前没有待处理权限请求。";
        pendingPermissionAwaitingResolution = false;
        pendingPermissionFields.Clear();
        SelectedPermissionScope = permissionScopes.FirstOrDefault();
    }

    private void ClearPendingUserInputRequestState()
    {
        ClearActivePendingUserInputRequestState();
    }

    private void ClearActivePendingUserInputRequestState()
    {
        PendingUserInputCallId = string.Empty;
        PendingUserInputDetailsText = string.Empty;
        PendingUserInputSummaryText = "当前没有待补录请求。";
        pendingUserInputAwaitingResolution = false;
        pendingUserInputQuestions.Clear();
    }

    private void MarkPendingUserInputAwaitingResolution()
    {
        pendingUserInputAwaitingResolution = true;
        PendingUserInputSummaryText = "待补录请求已提交，等待运行时继续。";
        pendingUserInputQuestions.Clear();
    }

    private bool HasActionablePendingInteractiveRequest(string requestKind)
    {
        var entry = GetActivePendingInteractiveRequest(requestKind);
        return entry is not null && !entry.IsAwaitingResolution;
    }

    private void RefreshPendingInteractiveRequestState()
    {
        ApplyPendingApprovalProjection(GetActivePendingInteractiveRequest("approval_requested"));
        ApplyPendingPermissionProjection(GetActivePendingInteractiveRequest("permission_requested"));
        ApplyPendingUserInputProjection(GetActivePendingInteractiveRequest("request_user_input"));
        RefreshCommandState();
    }

    private void ApplyPendingApprovalProjection(PendingInteractiveRequestEntry? entry)
    {
        if (entry is null)
        {
            ClearPendingApprovalRequestState();
            return;
        }

        var sameCall = string.Equals(Normalize(PendingApprovalCallId), entry.CallId, StringComparison.Ordinal);
        var awaitingStateChanged = pendingApprovalAwaitingResolution != entry.IsAwaitingResolution;
        PendingApprovalCallId = entry.CallId;
        PendingApprovalDetailsText = BuildApprovalRequestDetailsJson(entry.Event);
        if (entry.IsAwaitingResolution)
        {
            pendingApprovalAwaitingResolution = true;
            PendingApprovalSummaryText = "待处理审批已提交，等待运行时继续。";
            ApprovalNoteText = string.Empty;
            approvalDecisions.Clear();
            SelectedApprovalDecision = null;
            return;
        }

        pendingApprovalAwaitingResolution = false;
        PendingApprovalSummaryText = BuildApprovalSummaryText(entry.Event);
        if (!sameCall || awaitingStateChanged || approvalDecisions.Count == 0)
        {
            ApprovalNoteText = string.Empty;
            RebuildApprovalDecisions(entry.Event);
        }
    }

    private void ApplyPendingPermissionProjection(PendingInteractiveRequestEntry? entry)
    {
        if (entry is null)
        {
            ClearPendingPermissionRequestState();
            return;
        }

        var sameCall = string.Equals(Normalize(PendingPermissionCallId), entry.CallId, StringComparison.Ordinal);
        var awaitingStateChanged = pendingPermissionAwaitingResolution != entry.IsAwaitingResolution;
        PendingPermissionCallId = entry.CallId;
        PendingPermissionDetailsText = BuildPermissionRequestDetailsJson(entry.Event);
        if (entry.IsAwaitingResolution)
        {
            pendingPermissionAwaitingResolution = true;
            PendingPermissionSummaryText = "待处理权限请求已提交，等待运行时继续。";
            pendingPermissionFields.Clear();
            SelectedPermissionScope = permissionScopes.FirstOrDefault();
            return;
        }

        pendingPermissionAwaitingResolution = false;
        PendingPermissionSummaryText = BuildPermissionSummaryText(entry.Event);
        if (!sameCall || awaitingStateChanged || pendingPermissionFields.Count == 0)
        {
            RebuildPermissionFields(entry.Event);
            SelectedPermissionScope = permissionScopes.FirstOrDefault();
        }
    }

    private void ApplyPendingUserInputProjection(PendingInteractiveRequestEntry? entry)
    {
        if (entry is null)
        {
            ClearPendingUserInputRequestState();
            return;
        }

        var sameCall = string.Equals(Normalize(PendingUserInputCallId), entry.CallId, StringComparison.Ordinal);
        var awaitingStateChanged = pendingUserInputAwaitingResolution != entry.IsAwaitingResolution;
        PendingUserInputCallId = entry.CallId;
        PendingUserInputDetailsText = BuildUserInputRequestDetailsJson(entry.Event);
        if (entry.IsAwaitingResolution)
        {
            MarkPendingUserInputAwaitingResolution();
            return;
        }

        pendingUserInputAwaitingResolution = false;
        PendingUserInputSummaryText = BuildUserInputSummaryText(entry.Event);
        if (!sameCall || awaitingStateChanged || pendingUserInputQuestions.Count == 0)
        {
            RebuildPendingUserInputQuestions(entry.Event);
        }
    }

    private PendingInteractiveRequestEntry? GetActivePendingInteractiveRequest(string requestKind)
    {
        var normalizedRequestKind = Normalize(requestKind);
        if (string.IsNullOrWhiteSpace(normalizedRequestKind))
        {
            return null;
        }

        return pendingInteractiveRequestEntries
            .Where(entry => string.Equals(entry.RequestKind, normalizedRequestKind, StringComparison.Ordinal))
            .OrderBy(entry => entry.Sequence)
            .FirstOrDefault();
    }

    private void UpsertPendingInteractiveRequest(TianShuSidecarEvent sidecarEvent, long requestId = 0)
    {
        var requestKind = Normalize(sidecarEvent.EventType)?.ToLowerInvariant();
        var callId = Normalize(sidecarEvent.CallId);
        if (!IsPendingInteractiveRequestKind(requestKind) || string.IsNullOrWhiteSpace(callId))
        {
            return;
        }

        var existingEntry = FindPendingInteractiveRequest(requestKind, callId, requestId);
        var isAwaitingResolution = ShouldAwaitInteractiveResolution(sidecarEvent);
        if (existingEntry is null)
        {
            pendingInteractiveRequestEntries.Add(new PendingInteractiveRequestEntry(
                requestId,
                requestKind!,
                callId!,
                Normalize(sidecarEvent.ThreadId),
                Normalize(sidecarEvent.TurnId),
                sidecarEvent,
                nextPendingInteractiveRequestSequence++,
                isAwaitingResolution));
        }
        else
        {
            existingEntry.Update(
                sidecarEvent,
                requestId,
                Normalize(sidecarEvent.ThreadId),
                Normalize(sidecarEvent.TurnId),
                isAwaitingResolution);
        }

        RefreshPendingInteractiveRequestState();
    }

    private PendingInteractiveRequestResolution ResolvePendingInteractiveRequest(string? resolvedCallId, string? requestKind)
    {
        var normalizedCallId = Normalize(resolvedCallId);
        var normalizedRequestKind = Normalize(requestKind)?.ToLowerInvariant();
        var resolvedEntry = !string.IsNullOrWhiteSpace(normalizedCallId)
            ? FindPendingInteractiveRequest(null, normalizedCallId, requestId: 0)
            : GetActivePendingInteractiveRequest(normalizedRequestKind ?? string.Empty);
        if (resolvedEntry is null)
        {
            return PendingInteractiveRequestResolution.None;
        }

        var wasActive = ReferenceEquals(GetActivePendingInteractiveRequest(resolvedEntry.RequestKind), resolvedEntry);
        pendingInteractiveRequestEntries.Remove(resolvedEntry);
        RefreshPendingInteractiveRequestState();
        var promotedNext = wasActive && GetActivePendingInteractiveRequest(resolvedEntry.RequestKind) is not null;
        return new PendingInteractiveRequestResolution(resolvedEntry.RequestKind, promotedNext, wasActive);
    }

    private bool MarkPendingInteractiveRequestAwaitingResolution(string requestKind, string? callId)
    {
        var normalizedCallId = Normalize(callId);
        var entry = !string.IsNullOrWhiteSpace(normalizedCallId)
            ? FindPendingInteractiveRequest(requestKind, normalizedCallId, requestId: 0)
            : GetActivePendingInteractiveRequest(requestKind);
        if (entry is null)
        {
            return false;
        }

        entry.IsAwaitingResolution = true;
        RefreshPendingInteractiveRequestState();
        return true;
    }

    private void ClearPendingInteractiveRequestsForTurn(string? turnId)
    {
        var normalizedTurnId = Normalize(turnId);
        if (string.IsNullOrWhiteSpace(normalizedTurnId))
        {
            return;
        }

        var removedCount = pendingInteractiveRequestEntries.RemoveAll(entry =>
            string.Equals(entry.TurnId, normalizedTurnId, StringComparison.Ordinal));
        if (removedCount > 0)
        {
            RefreshPendingInteractiveRequestState();
        }
    }

    private PendingInteractiveRequestEntry? FindPendingInteractiveRequest(string? requestKind, string? callId, long requestId)
    {
        var normalizedRequestKind = Normalize(requestKind)?.ToLowerInvariant();
        var normalizedCallId = Normalize(callId);
        return pendingInteractiveRequestEntries.FirstOrDefault(entry =>
            (requestId > 0 && entry.RequestId == requestId)
            || (!string.IsNullOrWhiteSpace(normalizedCallId)
                && string.Equals(entry.CallId, normalizedCallId, StringComparison.Ordinal)
                && (string.IsNullOrWhiteSpace(normalizedRequestKind)
                    || string.Equals(entry.RequestKind, normalizedRequestKind, StringComparison.Ordinal))));
    }

    private static bool IsPendingInteractiveRequestKind(string? requestKind)
        => requestKind is "approval_requested" or "permission_requested" or "request_user_input";

    private static bool ShouldAwaitInteractiveResolution(TianShuSidecarEvent sidecarEvent)
    {
        var status = Normalize(sidecarEvent.Status);
        var phase = Normalize(sidecarEvent.Phase);
        return string.Equals(status, "awaiting_resolution", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "submitted", StringComparison.OrdinalIgnoreCase)
            || string.Equals(phase, "awaiting_resolution", StringComparison.OrdinalIgnoreCase);
    }

    private void ClearCurrentPlan()
    {
        CurrentPlanExplanation = string.Empty;
        if (currentPlanSteps.Count > 0)
        {
            currentPlanSteps.Clear();
        }

        IsPlanPanelExpanded = true;
    }

    private async Task ExecuteExperimentalFeatureListAsync()
    {
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            SetActionRunning(true, "设置：正在加载实验特性。");
            var result = await sidecarBridge.ListExperimentalFeaturesAsync(null, null, CancellationToken.None).ConfigureAwait(true);
            SettingsResultText = FormatSettingsResultText(
                result.Message,
                SerializePrettyJson(new
                {
                    nextCursor = result.NextCursor,
                    items = result.Items.Select(static item => new
                    {
                        name = item.Name,
                        stage = item.Stage,
                        displayName = item.DisplayName,
                        description = item.Description,
                        announcement = item.Announcement,
                        enabled = item.Enabled,
                        defaultEnabled = item.DefaultEnabled,
                    }).ToArray(),
                }));
            CapabilityStatusText = $"设置：{result.Message}";
        }
        catch (Exception ex)
        {
            SettingsResultText = ex.Message;
            CapabilityStatusText = "设置：执行失败。";
        }
        finally
        {
            SetActionRunning(false, CapabilityStatusText);
        }
    }

    private async Task ExecuteCollaborationModeListAsync()
    {
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            SetActionRunning(true, "设置：正在加载协作模式。");
            var result = await sidecarBridge.ListCollaborationModesAsync(CancellationToken.None).ConfigureAwait(true);
            SettingsResultText = FormatSettingsResultText(
                result.Message,
                SerializePrettyJson(new
                {
                    items = result.Items.Select(static item => new
                    {
                        name = item.Name,
                        mode = item.Mode,
                        model = item.Model,
                        reasoningEffort = item.ReasoningEffort,
                    }).ToArray(),
                }));
            CapabilityStatusText = $"设置：{result.Message}";
        }
        catch (Exception ex)
        {
            SettingsResultText = ex.Message;
            CapabilityStatusText = "设置：执行失败。";
        }
        finally
        {
            SetActionRunning(false, CapabilityStatusText);
        }
    }

    private async Task ExecuteMcpServerStatusListAsync()
    {
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            SetActionRunning(true, "设置：正在加载 MCP 服务状态。");
            var result = await sidecarBridge.ListMcpServerStatusAsync(100, null, CancellationToken.None).ConfigureAwait(true);
            SettingsResultText = FormatSettingsResultText(
                result.Message,
                SerializePrettyJson(new
                {
                    nextCursor = result.NextCursor,
                    items = result.Items.Select(static item => new
                    {
                        name = item.Name,
                        authStatus = item.AuthStatus,
                        toolNames = item.ToolNames,
                        resourceUris = item.ResourceUris,
                        resourceTemplateUris = item.ResourceTemplateUris,
                    }).ToArray(),
                }));
            CapabilityStatusText = $"设置：{result.Message}";
        }
        catch (Exception ex)
        {
            SettingsResultText = ex.Message;
            CapabilityStatusText = "设置：执行失败。";
        }
        finally
        {
            SetActionRunning(false, CapabilityStatusText);
        }
    }

    private async Task ExecuteReloadMcpServersAsync()
    {
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            SetActionRunning(true, "设置：正在重载 MCP 配置。");
            var result = await sidecarBridge.ReloadMcpServersAsync(CancellationToken.None).ConfigureAwait(true);
            SettingsResultText = result.Message;
            CapabilityStatusText = $"设置：{result.Message}";
        }
        catch (Exception ex)
        {
            SettingsResultText = ex.Message;
            CapabilityStatusText = "设置：执行失败。";
        }
        finally
        {
            SetActionRunning(false, CapabilityStatusText);
        }
    }

    private async Task ExecuteSkillsListAsync()
    {
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            SetActionRunning(true, "设置：正在加载技能列表。");
            var workingDirectories = string.IsNullOrWhiteSpace(WorkingDirectory)
                ? Array.Empty<string>()
                : new[] { WorkingDirectory };
            var result = await sidecarBridge.ListSkillsAsync(workingDirectories, forceReload: false, CancellationToken.None).ConfigureAwait(true);
            SettingsResultText = FormatSettingsResultText(
                result.Message,
                SerializePrettyJson(new
                {
                    entries = result.Entries.Select(static entry => new
                    {
                        cwd = entry.Cwd,
                        skills = entry.Skills.Select(static skill => new
                        {
                            name = skill.Name,
                            description = skill.Description,
                            shortDescription = skill.ShortDescription,
                            pathToSkillsMd = skill.PathToSkillsMd,
                            path = skill.Path,
                            scope = skill.Scope,
                            enabled = skill.Enabled,
                        }).ToArray(),
                        errors = entry.Errors.Select(static error => new
                        {
                            path = error.Path,
                            message = error.Message,
                        }).ToArray(),
                    }).ToArray(),
                }));
            CapabilityStatusText = $"设置：{result.Message}";
        }
        catch (Exception ex)
        {
            SettingsResultText = ex.Message;
            CapabilityStatusText = "设置：执行失败。";
        }
        finally
        {
            SetActionRunning(false, CapabilityStatusText);
        }
    }

    private async Task ExecuteRemoteSkillsListAsync()
    {
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            SetActionRunning(true, "设置：正在加载远程技能列表。");
            var result = await sidecarBridge.ListRemoteSkillsAsync(null, null, null, CancellationToken.None).ConfigureAwait(true);
            SettingsResultText = FormatSettingsResultText(
                result.Message,
                SerializePrettyJson(new
                {
                    nextCursor = result.NextCursor,
                    items = result.Items.Select(static item => new
                    {
                        id = item.Id,
                        name = item.Name,
                        description = item.Description,
                        hazelnutScope = item.HazelnutScope,
                    }).ToArray(),
                }));
            CapabilityStatusText = $"设置：{result.Message}";
        }
        catch (Exception ex)
        {
            SettingsResultText = ex.Message;
            CapabilityStatusText = "设置：执行失败。";
        }
        finally
        {
            SetActionRunning(false, CapabilityStatusText);
        }
    }

    private async Task ExecutePluginListAsync()
    {
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            SetActionRunning(true, "设置：正在加载插件列表。");
            var workingDirectories = string.IsNullOrWhiteSpace(WorkingDirectory)
                ? Array.Empty<string>()
                : new[] { WorkingDirectory };
            var result = await sidecarBridge.ListPluginsAsync(workingDirectories, forceRemoteSync: false, CancellationToken.None).ConfigureAwait(true);
            SettingsResultText = FormatSettingsResultText(
                result.Message,
                SerializePrettyJson(new
                {
                    remoteSyncError = result.RemoteSyncError,
                    marketplaces = result.Marketplaces.Select(static marketplace => new
                    {
                        name = marketplace.Name,
                        path = marketplace.Path,
                        plugins = marketplace.Plugins.Select(static plugin => new
                        {
                            id = plugin.Id,
                            name = plugin.Name,
                            installed = plugin.Installed,
                            enabled = plugin.Enabled,
                            installPolicy = plugin.InstallPolicy,
                            authPolicy = plugin.AuthPolicy,
                        }).ToArray(),
                    }).ToArray(),
                }));
            CapabilityStatusText = $"设置：{result.Message}";
        }
        catch (Exception ex)
        {
            SettingsResultText = ex.Message;
            CapabilityStatusText = "设置：执行失败。";
        }
        finally
        {
            SetActionRunning(false, CapabilityStatusText);
        }
    }

    private async Task ExecuteAppListAsync()
    {
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            SetActionRunning(true, "设置：正在加载应用列表。");
            var result = await sidecarBridge.ListAppsAsync(null, null, ThreadId, false, CancellationToken.None).ConfigureAwait(true);
            SettingsResultText = FormatSettingsResultText(
                result.Message,
                SerializePrettyJson(new
                {
                    nextCursor = result.NextCursor,
                    items = result.Items.Select(static item => new
                    {
                        id = item.Id,
                        name = item.Name,
                        description = item.Description,
                        installUrl = item.InstallUrl,
                        distributionChannel = item.DistributionChannel,
                        isAccessible = item.IsAccessible,
                        isEnabled = item.IsEnabled,
                        pluginDisplayNames = item.PluginDisplayNames,
                    }).ToArray(),
                }));
            CapabilityStatusText = $"设置：{result.Message}";
        }
        catch (Exception ex)
        {
            SettingsResultText = ex.Message;
            CapabilityStatusText = "设置：执行失败。";
        }
        finally
        {
            SetActionRunning(false, CapabilityStatusText);
        }
    }

    private async Task ExecuteRemoteSkillExportAsync()
    {
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            var hazelnutId = Normalize(RemoteSkillHazelnutId) ?? "sample-hazelnut-id";
            SetActionRunning(true, "设置：正在导出远程技能。");
            var result = await sidecarBridge.ExportRemoteSkillAsync(hazelnutId, CancellationToken.None).ConfigureAwait(true);
            SettingsResultText = FormatSettingsResultText(
                result.Message,
                SerializePrettyJson(new
                {
                    id = result.Id,
                    path = result.Path,
                }));
            CapabilityStatusText = $"设置：{result.Message}";
        }
        catch (Exception ex)
        {
            SettingsResultText = ex.Message;
            CapabilityStatusText = "设置：执行失败。";
        }
        finally
        {
            SetActionRunning(false, CapabilityStatusText);
        }
    }

    private async Task ExecutePluginReadAsync()
    {
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            var marketplacePath = Normalize(PluginMarketplacePath) ?? string.Empty;
            var pluginName = Normalize(PluginName) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(marketplacePath) || string.IsNullOrWhiteSpace(pluginName))
            {
                SettingsResultText = "marketplacePath 和 pluginName 不能为空。";
                CapabilityStatusText = "设置：参数校验失败。";
                return;
            }

            SetActionRunning(true, "设置：正在读取插件详情。");
            var result = await sidecarBridge.ReadPluginAsync(marketplacePath, pluginName, CancellationToken.None).ConfigureAwait(true);
            SettingsResultText = FormatSettingsResultText(
                result.Message,
                SerializePrettyJson(new
                {
                    plugin = result.Plugin is null
                        ? null
                        : new
                        {
                            marketplaceName = result.Plugin.MarketplaceName,
                            marketplacePath = result.Plugin.MarketplacePath,
                            summary = new
                            {
                                id = result.Plugin.Summary.Id,
                                name = result.Plugin.Summary.Name,
                                installed = result.Plugin.Summary.Installed,
                                enabled = result.Plugin.Summary.Enabled,
                                installPolicy = result.Plugin.Summary.InstallPolicy,
                                authPolicy = result.Plugin.Summary.AuthPolicy,
                            },
                            description = result.Plugin.Description,
                            skills = result.Plugin.Skills.Select(static skill => new
                            {
                                name = skill.Name,
                                description = skill.Description,
                                shortDescription = skill.ShortDescription,
                                path = skill.Path,
                            }).ToArray(),
                            apps = result.Plugin.Apps.Select(static app => new
                            {
                                id = app.Id,
                                name = app.Name,
                                description = app.Description,
                                installUrl = app.InstallUrl,
                            }).ToArray(),
                            mcpServers = result.Plugin.McpServers,
                        },
                }));
            CapabilityStatusText = $"设置：{result.Message}";
        }
        catch (Exception ex)
        {
            SettingsResultText = ex.Message;
            CapabilityStatusText = "设置：执行失败。";
        }
        finally
        {
            SetActionRunning(false, CapabilityStatusText);
        }
    }

    private async Task ExecutePluginInstallAsync()
    {
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            var marketplacePath = Normalize(PluginMarketplacePath) ?? string.Empty;
            var pluginName = Normalize(PluginName) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(marketplacePath) || string.IsNullOrWhiteSpace(pluginName))
            {
                SettingsResultText = "marketplacePath 和 pluginName 不能为空。";
                CapabilityStatusText = "设置：参数校验失败。";
                return;
            }

            SetActionRunning(true, "设置：正在安装插件。");
            var result = await sidecarBridge.InstallPluginAsync(marketplacePath, pluginName, WorkingDirectory, CancellationToken.None).ConfigureAwait(true);
            SettingsResultText = FormatSettingsResultText(
                result.Message,
                SerializePrettyJson(new
                {
                    authPolicy = result.AuthPolicy,
                    appsNeedingAuth = result.AppsNeedingAuth.Select(static app => new
                    {
                        id = app.Id,
                        name = app.Name,
                        description = app.Description,
                        installUrl = app.InstallUrl,
                    }).ToArray(),
                }));
            CapabilityStatusText = $"设置：{result.Message}";
        }
        catch (Exception ex)
        {
            SettingsResultText = ex.Message;
            CapabilityStatusText = "设置：执行失败。";
        }
        finally
        {
            SetActionRunning(false, CapabilityStatusText);
        }
    }

    private async Task ExecuteStartReviewAsync()
    {
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            var threadId = Normalize(ThreadId);
            if (string.IsNullOrWhiteSpace(threadId))
            {
                SettingsResultText = "当前没有可用的 threadId。";
                CapabilityStatusText = "设置：参数校验失败。";
                return;
            }

            SetActionRunning(true, "设置：正在发起 review。");
            var result = await sidecarBridge.StartReviewAsync(threadId!, "inline", "uncommittedChanges", CancellationToken.None).ConfigureAwait(true);
            SettingsResultText = FormatSettingsResultText(
                result.Message,
                SerializePrettyJson(new
                {
                    reviewThreadId = result.ReviewThreadId,
                    turn = result.Turn is null
                        ? null
                        : new
                        {
                            id = result.Turn.Id,
                            status = result.Turn.Status,
                            displayText = result.Turn.DisplayText,
                        },
                }));
            CapabilityStatusText = $"设置：{result.Message}";
        }
        catch (Exception ex)
        {
            SettingsResultText = ex.Message;
            CapabilityStatusText = "设置：执行失败。";
        }
        finally
        {
            SetActionRunning(false, CapabilityStatusText);
        }
    }

    private async Task ExecuteMcpOauthLoginAsync()
    {
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            var serverName = Normalize(McpOauthServerName);
            if (string.IsNullOrWhiteSpace(serverName))
            {
                SettingsResultText = "MCP Server 名称不能为空。";
                CapabilityStatusText = "设置：参数校验失败。";
                return;
            }

            SetActionRunning(true, "设置：正在发起 MCP OAuth 登录。");
            var result = await sidecarBridge.StartMcpServerOauthLoginAsync(serverName!, null, CancellationToken.None).ConfigureAwait(true);
            SettingsResultText = FormatSettingsResultText(
                result.Message,
                SerializePrettyJson(new
                {
                    authorizationUrl = result.AuthorizationUrl,
                }));
            CapabilityStatusText = $"设置：{result.Message}";
        }
        catch (Exception ex)
        {
            SettingsResultText = ex.Message;
            CapabilityStatusText = "设置：执行失败。";
        }
        finally
        {
            SetActionRunning(false, CapabilityStatusText);
        }
    }

    private async Task ExecuteConversationSummaryAsync()
    {
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            SetActionRunning(true, "设置：正在读取当前会话摘要。");
            var result = await sidecarBridge.GetConversationSummaryAsync(ThreadId, null, CancellationToken.None).ConfigureAwait(true);
            SettingsResultText = FormatSettingsResultText(
                result.Message,
                SerializePrettyJson(new
                {
                    summary = result.Summary is null
                        ? null
                        : new
                        {
                            conversationId = result.Summary.ConversationId,
                            path = result.Summary.Path,
                            preview = result.Summary.Preview,
                            timestamp = result.Summary.Timestamp,
                            updatedAt = result.Summary.UpdatedAt,
                            modelProvider = result.Summary.ModelProvider,
                            cwd = result.Summary.Cwd,
                            cliVersion = result.Summary.CliVersion,
                            source = result.Summary.Source,
                            gitInfo = result.Summary.GitInfo is null
                                ? null
                                : new
                                {
                                    sha = result.Summary.GitInfo.Sha,
                                    branch = result.Summary.GitInfo.Branch,
                                    originUrl = result.Summary.GitInfo.OriginUrl,
                                },
                        },
                }));
            CapabilityStatusText = $"设置：{result.Message}";
        }
        catch (Exception ex)
        {
            SettingsResultText = ex.Message;
            CapabilityStatusText = "设置：执行失败。";
        }
        finally
        {
            SetActionRunning(false, CapabilityStatusText);
        }
    }

    private async Task ExecuteGitDiffToRemoteAsync()
    {
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            var threadId = Normalize(ThreadId);
            if (string.IsNullOrWhiteSpace(threadId))
            {
                SettingsResultText = "当前没有可用的 threadId。";
                CapabilityStatusText = "设置：参数校验失败。";
                return;
            }

            SetActionRunning(true, "设置：正在读取当前工作区 diff。");
            var result = await sidecarBridge.GetGitDiffToRemoteAsync(threadId!, CancellationToken.None).ConfigureAwait(true);
            SettingsResultText = FormatSettingsResultText(
                result.Message,
                SerializePrettyJson(new
                {
                    hasChanges = result.HasChanges,
                    diff = result.Diff,
                }));
            CapabilityStatusText = $"设置：{result.Message}";
        }
        catch (Exception ex)
        {
            SettingsResultText = ex.Message;
            CapabilityStatusText = "设置：执行失败。";
        }
        finally
        {
            SetActionRunning(false, CapabilityStatusText);
        }
    }

    private async Task ExecuteConfigRequirementsReadAsync()
    {
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            SetActionRunning(true, "设置：正在读取配置要求。");
            var result = await sidecarBridge.ReadConfigRequirementsAsync(WorkingDirectory, CancellationToken.None).ConfigureAwait(true);
            SettingsResultText = FormatSettingsResultText(result.Message, BuildConfigRequirementsDetailsJson(result));
            CapabilityStatusText = $"设置：{result.Message}";
        }
        catch (Exception ex)
        {
            SettingsResultText = ex.Message;
            CapabilityStatusText = "设置：执行失败。";
        }
        finally
        {
            SetActionRunning(false, CapabilityStatusText);
        }
    }

    private async Task ExecuteConfigValueWriteAsync()
    {
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            if (!TryBuildConfigValueWriteRequest(out var request, out var validationError))
            {
                SettingsResultText = validationError;
                CapabilityStatusText = "设置：参数校验失败。";
                return;
            }

            SetActionRunning(true, "设置：正在写入单个配置项。");
            var result = await sidecarBridge.WriteConfigValueAsync(request, CancellationToken.None).ConfigureAwait(true);
            SettingsResultText = FormatConfigWriteResult(result);
            CapabilityStatusText = $"设置：{result.Message}";
        }
        catch (Exception ex)
        {
            SettingsResultText = ex.Message;
            CapabilityStatusText = "设置：执行失败。";
        }
        finally
        {
            SetActionRunning(false, CapabilityStatusText);
        }
    }

    private async Task ExecuteConfigBatchWriteAsync()
    {
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            if (!TryBuildConfigBatchWriteRequest(out var request, out var validationError))
            {
                SettingsResultText = validationError;
                CapabilityStatusText = "设置：参数校验失败。";
                return;
            }

            SetActionRunning(true, "设置：正在批量写入配置项。");
            var result = await sidecarBridge.WriteConfigBatchAsync(request, CancellationToken.None).ConfigureAwait(true);
            SettingsResultText = FormatConfigWriteResult(result);
            CapabilityStatusText = $"设置：{result.Message}";
        }
        catch (Exception ex)
        {
            SettingsResultText = ex.Message;
            CapabilityStatusText = "设置：执行失败。";
        }
        finally
        {
            SetActionRunning(false, CapabilityStatusText);
        }
    }

    private bool TryBuildConfigValueWriteRequest(
        out TianShuSidecarConfigValueWriteRequest? request,
        out string? validationError)
    {
        request = null;
        validationError = null;

        var keyPath = Normalize(ConfigKeyPath);
        if (string.IsNullOrWhiteSpace(keyPath))
        {
            validationError = "keyPath 不能为空。";
            return false;
        }

        request = new TianShuSidecarConfigValueWriteRequest
        {
            KeyPath = keyPath,
            Value = ParseConfigInputValue(ConfigValueJson),
            WorkingDirectory = WorkingDirectory,
        };
        return true;
    }

    private bool TryBuildConfigBatchWriteRequest(
        out TianShuSidecarConfigBatchWriteRequest? request,
        out string? validationError)
    {
        request = null;
        validationError = null;

        if (!TryParseConfigBatchItems(ConfigBatchItemsJson, out var items, out validationError))
        {
            return false;
        }

        request = new TianShuSidecarConfigBatchWriteRequest
        {
            Items = items,
            WorkingDirectory = WorkingDirectory,
        };
        return true;
    }

    private static bool TryParseConfigBatchItems(
        string json,
        out IReadOnlyList<TianShuSidecarConfigWriteItem>? items,
        out string? validationError)
    {
        items = null;
        validationError = null;

        var normalized = NormalizeJsonInput(json) ?? "[]";
        try
        {
            using var document = JsonDocument.Parse(normalized);
            var root = document.RootElement;
            JsonElement sourceArray;
            if (root.ValueKind == JsonValueKind.Array)
            {
                sourceArray = root;
            }
            else if (root.ValueKind == JsonValueKind.Object
                     && root.TryGetProperty("items", out var itemArray)
                     && itemArray.ValueKind == JsonValueKind.Array)
            {
                sourceArray = itemArray;
            }
            else if (root.ValueKind == JsonValueKind.Object
                     && root.TryGetProperty("edits", out var editArray)
                     && editArray.ValueKind == JsonValueKind.Array)
            {
                sourceArray = editArray;
            }
            else
            {
                validationError = "批量写入输入必须是数组，或包含 items/edits 数组的对象。";
                return false;
            }

            var parsed = new List<TianShuSidecarConfigWriteItem>();
            foreach (var item in sourceArray.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var keyPath = ReadJsonString(item, "keyPath")
                    ?? ReadJsonString(item, "key")
                    ?? ReadJsonString(item, "path");
                if (string.IsNullOrWhiteSpace(keyPath))
                {
                    continue;
                }

                TianShuSidecarStructuredValue? value = null;
                if (item.TryGetProperty("value", out var valueElement))
                {
                    value = ConvertJsonElementToObject(valueElement);
                }

                parsed.Add(new TianShuSidecarConfigWriteItem
                {
                    KeyPath = keyPath,
                    Value = value,
                });
            }

            if (parsed.Count == 0)
            {
                validationError = "批量写入输入里没有有效的 keyPath/key/path 项。";
                return false;
            }

            items = parsed;
            return true;
        }
        catch (JsonException ex)
        {
            validationError = $"批量写入 JSON 解析失败：{ex.Message}";
            return false;
        }
    }

    private void ApplyCurrentConfigOverview(TianShuSidecarConfigReadResult result)
    {
        settingsConfigFields.Clear();

        var modelValue = AddSettingsConfigField(result, "当前模型", required: true, "model");
        AddSettingsConfigField(result, "模型提供方", required: true, "provider");
        AddSettingsConfigField(result, "推理强度", required: false, "model_reasoning_effort", "model_reasoning_summary");
        AddSettingsConfigField(result, "输出详细度", required: false, "model_verbosity");
        AddSettingsConfigField(result, "默认权限", required: false, "default_permissions");
        AddSettingsConfigField(result, "审批策略", required: false, "approval_policy");
        AddSettingsConfigField(result, "服务层级", required: false, "service_tier");
        AddSettingsConfigField(result, "Web 搜索", required: false, "web_search");

        currentConfiguredModelId = modelValue ?? string.Empty;
        EffectiveConfigStatusText = string.IsNullOrWhiteSpace(modelValue)
            ? "已读取当前配置，但未发现 model 字段。"
            : $"当前生效模型：{modelValue}。这里展示的是按当前工作目录合并后的 config/read 结果。";

        RefreshSettingsModelCatalogView();
        OnPropertyChanged(nameof(HasSettingsConfigFields));
    }

    private void ApplyModelCatalogOverview(TianShuSidecarModelCatalogResult result)
    {
        settingsModelCatalog.Clear();
        foreach (var item in result.Items)
        {
            var modelId = string.IsNullOrWhiteSpace(item.Model) ? item.Id : item.Model;
            var displayName = string.IsNullOrWhiteSpace(item.DisplayName) ? modelId : item.DisplayName;
            var inputModalitiesText = item.InputModalities.Count == 0 ? "text" : string.Join(" / ", item.InputModalities);
            var reasoningEffortsText = item.SupportedReasoningEfforts.Count == 0 ? "未声明" : string.Join(" / ", item.SupportedReasoningEfforts);

            settingsModelCatalog.Add(new SettingsModelCatalogEntry(
                displayName,
                modelId,
                string.IsNullOrWhiteSpace(item.DefaultReasoningEffort) ? "medium" : item.DefaultReasoningEffort,
                reasoningEffortsText,
                inputModalitiesText,
                item.SupportsPersonality,
                string.Equals(modelId, currentConfiguredModelId, StringComparison.OrdinalIgnoreCase),
                item.Description ?? string.Empty));
        }

        ModelCatalogStatusText = settingsModelCatalog.Count == 0
            ? "模型目录为空。"
            : string.IsNullOrWhiteSpace(currentConfiguredModelId)
                ? $"已加载 {settingsModelCatalog.Count} 个模型。"
                : $"已加载 {settingsModelCatalog.Count} 个模型；当前生效模型：{currentConfiguredModelId}。";

        OnPropertyChanged(nameof(HasSettingsModelCatalog));
    }

    private string? AddSettingsConfigField(
        TianShuSidecarConfigReadResult result,
        string label,
        bool required,
        params string[] keyPaths)
    {
        var selection = ResolveSettingsConfigField(result, keyPaths);
        if (string.IsNullOrWhiteSpace(selection.ValueText) && !required)
        {
            return null;
        }

        var displayValue = !string.IsNullOrWhiteSpace(selection.ValueText)
            ? selection.ValueText
            : "未设置";
        settingsConfigFields.Add(new SettingsConfigFieldEntry(
            label,
            selection.KeyPath ?? keyPaths.FirstOrDefault() ?? string.Empty,
            displayValue,
            selection.SourceText ?? "来源未知"));
        return selection.ValueText;
    }

    internal static (string? KeyPath, string? ValueText, string SourceText) ResolveSettingsConfigField(
        TianShuSidecarConfigReadResult result,
        params string[] keyPaths)
    {
        foreach (var keyPath in keyPaths)
        {
            if (TryGetTypedConfigValue(result.Config, keyPath, out var typedValue))
            {
                return (
                    keyPath,
                    typedValue,
                    ResolveTypedConfigSourceText(result.Origins, keyPath) ?? "来源未知");
            }
        }

        var field = result.Fields.FirstOrDefault(candidate =>
            keyPaths.Any(keyPath => string.Equals(candidate.KeyPath, keyPath, StringComparison.OrdinalIgnoreCase)));
        return (
            field?.KeyPath ?? keyPaths.FirstOrDefault(),
            field?.ValueText,
            field?.SourceText ?? "来源未知");
    }

    private static bool TryGetTypedConfigValue(TianShuSidecarConfigSnapshot? config, string keyPath, out string? valueText)
    {
        valueText = keyPath switch
        {
            "model" => config?.Model,
            "provider" => config?.ModelProvider,
            "model_reasoning_effort" => config?.ModelReasoningEffort,
            "model_reasoning_summary" => config?.ModelReasoningSummary,
            "model_verbosity" => config?.ModelVerbosity,
            "default_permissions" => config?.DefaultPermissions,
            "approval_policy" => config?.ApprovalPolicy,
            "service_tier" => config?.ServiceTier,
            "web_search" => config?.WebSearch,
            _ => null,
        };

        return !string.IsNullOrWhiteSpace(valueText);
    }

    private static string? ResolveTypedConfigSourceText(
        IReadOnlyDictionary<string, TianShuSidecarConfigOrigin> origins,
        string keyPath)
    {
        if (!origins.TryGetValue(keyPath, out var origin) || origin.Name is null)
        {
            return null;
        }

        var sourceType = origin.Name.Type;
        var sourcePath = origin.Name.File ?? origin.Name.DotTianShuFolder;
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return string.IsNullOrWhiteSpace(sourceType) ? null : sourceType;
        }

        return string.IsNullOrWhiteSpace(sourceType)
            ? sourcePath
            : $"{sourceType} · {sourcePath}";
    }

    private void RefreshSettingsModelCatalogView()
    {
        if (settingsModelCatalog.Count == 0)
        {
            OnPropertyChanged(nameof(HasSettingsModelCatalog));
            return;
        }

        var snapshot = settingsModelCatalog.ToArray();
        settingsModelCatalog.Clear();
        foreach (var item in snapshot)
        {
            settingsModelCatalog.Add(new SettingsModelCatalogEntry(
                item.DisplayName,
                item.ModelId,
                item.DefaultReasoningEffort,
                item.SupportedReasoningEffortsText,
                item.InputModalitiesText,
                item.SupportsPersonality,
                string.Equals(item.ModelId, currentConfiguredModelId, StringComparison.OrdinalIgnoreCase),
                item.Description));
        }

        ModelCatalogStatusText = settingsModelCatalog.Count == 0
            ? "模型目录为空。"
            : string.IsNullOrWhiteSpace(currentConfiguredModelId)
                ? $"已加载 {settingsModelCatalog.Count} 个模型。"
                : $"已加载 {settingsModelCatalog.Count} 个模型；当前生效模型：{currentConfiguredModelId}。";
        OnPropertyChanged(nameof(HasSettingsModelCatalog));
    }

    private void ClearSettingsOverviewState()
    {
        currentConfiguredModelId = string.Empty;
        settingsConfigFields.Clear();
        settingsModelCatalog.Clear();
        EffectiveConfigStatusText = "尚未读取当前配置。";
        ModelCatalogStatusText = "尚未加载模型目录。";
        OnPropertyChanged(nameof(HasSettingsConfigFields));
        OnPropertyChanged(nameof(HasSettingsModelCatalog));
    }

    private async Task SendCurrentInputAsync()
    {
        var message = (InputText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (isBusy && isInitialized)
        {
            QueuePendingFollowUp(message, busySendMode);
            if (busySendMode is BusySendMode.Steer or BusySendMode.Interrupt)
            {
                await TryDispatchPendingFollowUpsAsync().ConfigureAwait(true);
            }

            return;
        }

        await DispatchMessageAsync(message).ConfigureAwait(true);
    }

    private void QueuePendingFollowUp(string message, BusySendMode requestedMode)
    {
        var pendingFollowUp = new PendingFollowUpEntry(
            message,
            requestedMode,
            CreateTextUserInputs(message),
            SnapshotPendingContextEntries(),
            PendingFollowUpBucket.QueuedUserMessage);
        pendingFollowUpEntries.Add(pendingFollowUp);
        InputText = string.Empty;
        ClearPendingContextEntries();
        StatusText = requestedMode switch
        {
            BusySendMode.Steer => BuildStatusText("新的输入已加入引导列表。", "新的输入已加入引导列表。"),
            BusySendMode.Interrupt => BuildStatusText("新的输入将在当前回合中断后发送。", "新的输入将在当前回合中断后发送。"),
            _ => BuildStatusText("新的输入已加入待发送列表。", "新的输入已加入待发送列表。"),
        };
        FocusInput();
    }

    private async Task DispatchMessageAsync(string message)
    {
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。 ");
            }

            await EnsureConversationThreadForSendAsync().ConfigureAwait(true);
            var historyMessages = BuildPendingHistoryMessages(SnapshotPendingContextEntries());
            var userInputs = CreateTextUserInputs(message);
            AppendMessage("你", message);
            ClearCurrentPlan();
            InputText = string.Empty;
            SetBusy(true, "状态：正在处理当前回合……", keepInitialized: true);
            await sidecarBridge.SendAsync(userInputs, historyMessages, CancellationToken.None).ConfigureAwait(true);
            ClearPendingContextEntries();
            FocusInput();
        }
        catch (Exception ex)
        {
            AppendSystemMessage($"发送失败：{ex.Message}", addBlankLineBefore: true);
            SetBusy(false, "状态：发送失败。", keepInitialized: isInitialized);
        }
    }

    private async Task EnsureConversationThreadForSendAsync()
    {
        if (!shouldStartNewThreadOnNextSend)
        {
            return;
        }

        if (sidecarBridge is null)
        {
            throw new InvalidOperationException("TianShu sidecar 尚未建立。");
        }

        SetBusy(true, "状态：正在准备新会话……", keepInitialized: true);
        var newThread = await sidecarBridge.StartNewThreadAsync(CancellationToken.None).ConfigureAwait(true)
            ?? throw new InvalidOperationException("sidecar 未返回新线程。");

        if (string.IsNullOrWhiteSpace(newThread.ThreadId))
        {
            throw new InvalidOperationException("sidecar 返回的新线程缺少 threadId。");
        }

        shouldStartNewThreadOnNextSend = false;
        ThreadId = newThread.ThreadId;
        SelectedThread = null;
        ThreadRenameText = string.Empty;
    }

    private async Task<bool> DispatchFollowUpAsync(PendingFollowUpEntry pendingFollowUp)
    {
        var wasBusyBeforeDispatch = isBusy;

        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。 ");
            }

            var effectiveMessage = BuildFollowUpMessageWithPendingContext(pendingFollowUp.Message, pendingFollowUp.ContextEntries);
            var effectiveInputs = pendingFollowUp.ContextEntries.Count == 0
                ? pendingFollowUp.Inputs.Count > 0
                    ? pendingFollowUp.Inputs
                    : CreateTextUserInputs(effectiveMessage)
                : CreateTextUserInputs(effectiveMessage);
            var followUpMode = ResolvePendingFollowUpDispatchMode(pendingFollowUp);
            SetInterruptRequestPending(followUpMode == BusySendMode.Interrupt && wasBusyBeforeDispatch);
            SetBusy(true, BuildFollowUpStatusText(followUpMode), keepInitialized: true);
            var acceptedFollowUp = await sidecarBridge.SendFollowUpAsync(
                    effectiveInputs,
                    MapFollowUpMode(followUpMode),
                    CancellationToken.None)
                .ConfigureAwait(true);

            pendingFollowUp.MarkAsAwaitingCommit(
                effectiveMessage,
                imageCount: 0,
                acceptedFollowUp.CorrelationId,
                isTurnSteer: followUpMode == BusySendMode.Steer,
                inputs: effectiveInputs);
            FocusInput();
            return true;
        }
        catch (Exception ex)
        {
            if (pendingFollowUp.IsAwaitingCommit)
            {
                pendingFollowUp.ClearAwaitingCommit();
            }

            SetInterruptRequestPending(false);
            AppendSystemMessage($"跟进发送失败：{ex.Message}", addBlankLineBefore: true);
            SetBusy(
                wasBusyBeforeDispatch,
                BuildStatusText($"跟进发送失败：{ex.Message}", "跟进发送失败。"),
                keepInitialized: isInitialized);
            return false;
        }
    }

    private IReadOnlyList<PendingContextEntry> SnapshotPendingContextEntries()
        => pendingContextEntries.Count == 0 ? Array.Empty<PendingContextEntry>() : pendingContextEntries.ToArray();

    private void PromotePendingFollowUpToSteer(PendingFollowUpEntry entry)
    {
        if (!pendingFollowUpEntries.Contains(entry))
        {
            return;
        }

        entry.MarkAsSteer();
    }

    private async Task TryDispatchPendingFollowUpsAsync()
    {
        if (isDispatchingPendingFollowUp
            || pendingFollowUpEntries.Count == 0
            || HasAwaitingCommittedPendingFollowUp()
            || sidecarBridge is null
            || !isInitialized
            || isActionRunning
            || interruptRequestPending
            || HasPendingApproval
            || HasPendingPermission
            || HasPendingUserInput)
        {
            return;
        }

        var nextPendingFollowUp = SelectPendingFollowUpForDispatch();
        if (nextPendingFollowUp is null)
        {
            return;
        }

        isDispatchingPendingFollowUp = true;
        RefreshCommandState();

        try
        {
            await DispatchFollowUpAsync(nextPendingFollowUp).ConfigureAwait(true);
        }
        finally
        {
            isDispatchingPendingFollowUp = false;
            RefreshCommandState();
        }
    }

    private PendingFollowUpEntry? SelectPendingFollowUpForDispatch()
    {
        var dispatchCandidates = pendingFollowUpEntries
            .Where(static entry => !entry.IsAwaitingCommit)
            .ToArray();
        if (dispatchCandidates.Length == 0)
        {
            return null;
        }

        if (!isBusy)
        {
            return dispatchCandidates.FirstOrDefault(static entry => entry.IsSteer)
                ?? dispatchCandidates.FirstOrDefault(static entry => entry.IsInterrupt)
                ?? dispatchCandidates[0];
        }

        var interruptCandidate = dispatchCandidates.FirstOrDefault(static entry => entry.IsInterrupt);
        if (interruptCandidate is not null)
        {
            return interruptCandidate;
        }

        if (activeToolCallKeys.Count > 0)
        {
            return null;
        }

        return dispatchCandidates.FirstOrDefault(static entry => entry.IsSteer);
    }

    private BusySendMode ResolvePendingFollowUpDispatchMode(PendingFollowUpEntry pendingFollowUp)
    {
        if (pendingFollowUp.IsInterrupt)
        {
            return isBusy ? BusySendMode.Interrupt : BusySendMode.Queue;
        }

        if (pendingFollowUp.IsSteer)
        {
            return isBusy ? BusySendMode.Steer : BusySendMode.Queue;
        }

        return BusySendMode.Queue;
    }

    private void ResetFollowUpState(bool preserveAwaitingTurnSteer = false)
    {
        SetInterruptRequestPending(false);
        ReleaseAwaitingCommittedPendingFollowUps(preserveAwaitingTurnSteer);
    }

    private void AddEditorContext(bool includeSelectionOnly)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            var snapshot = textContextService.TryGetActiveTextContext(out var errorMessage);
            if (snapshot is null)
            {
                AppendSystemMessage(errorMessage ?? "未能读取当前编辑器上下文。", addBlankLineBefore: true);
                return;
            }

            if (includeSelectionOnly)
            {
                if (!snapshot.HasSelection)
                {
                    AppendSystemMessage("当前没有选中文本，无法加入选区上下文。", addBlankLineBefore: true);
                    return;
                }

                pendingContextEntries.Add(PendingContextEntry.CreateSelection(snapshot.FilePath, snapshot.SelectionText));
            }
            else
            {
                pendingContextEntries.Add(PendingContextEntry.CreateFile(snapshot.FilePath, snapshot.FileText));
            }

            RefreshCommandState();
            OnPropertyChanged(nameof(PendingContextSummary));
        }
        catch (Exception ex)
        {
            AppendSystemMessage($"读取 IDE 上下文失败：{ex.Message}", addBlankLineBefore: true);
        }
    }

    internal Task StartNewSessionFromCommandAsync()
        => StartNewSessionAsync();

    internal async Task AddCurrentFileContextFromCommandAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        AddEditorContext(includeSelectionOnly: false);
        FocusInput();
    }

    internal async Task AddSelectionContextFromCommandAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        AddEditorContext(includeSelectionOnly: true);
        FocusInput();
    }

    internal async Task AddSpecifiedFileContextFromCommandAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        AddSpecifiedFileContext();
        FocusInput();
    }

    internal Task ReconnectRuntimeFromCommandAsync()
        => ReconnectRuntimeAsync();

    internal async Task ShowSettingsFromCommandAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        OpenInspectorTab(2);
    }

    internal void FocusComposer()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        FocusInput();
    }

    private void AddSpecifiedFileContext()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            var dialog = new OpenFileDialog
            {
                CheckFileExists = true,
                Multiselect = false,
                Title = "选择要加入线程的文件",
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var filePath = dialog.FileName;
            if (!File.Exists(filePath))
            {
                AppendSystemMessage($"指定文件不存在：{filePath}", addBlankLineBefore: true);
                return;
            }

            var content = File.ReadAllText(filePath);
            if (content.IndexOf('\0') >= 0)
            {
                AppendSystemMessage($"指定文件看起来是二进制文件，无法直接加入：{filePath}", addBlankLineBefore: true);
                return;
            }

            pendingContextEntries.Add(PendingContextEntry.CreateSpecifiedFile(filePath, content));
            RefreshCommandState();
            OnPropertyChanged(nameof(PendingContextSummary));
        }
        catch (Exception ex)
        {
            AppendSystemMessage($"加入指定文件失败：{ex.Message}", addBlankLineBefore: true);
        }
    }

    private void BrowseWorkingDirectory()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "选择 TianShu 工作目录",
            SelectedPath = Directory.Exists(WorkingDirectory) ? WorkingDirectory : string.Empty,
            ShowNewFolderButton = false,
        };

        if (dialog.ShowDialog() != Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return;
        }

        WorkingDirectory = dialog.SelectedPath;
        AppHostProjectPath = TianShuDevPathLocator.ResolveAppHostProjectPath(WorkingDirectory) ?? AppHostProjectPath;
        StatusText = "状态：工作目录已更新。";
    }

    private void BrowseConfigPath()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Multiselect = false,
            Title = "选择 TianShu 配置文件",
            Filter = "TOML 文件|*.toml|所有文件|*.*",
            FileName = File.Exists(ConfigPath) ? ConfigPath : string.Empty,
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        ConfigPath = dialog.FileName;
        StatusText = "状态：配置文件路径已更新。";
    }

    private void BrowseAppHostProjectPath()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Multiselect = false,
            Title = "选择 TianShu.AppHost.csproj",
            Filter = "C# 项目|*.csproj|所有文件|*.*",
            FileName = File.Exists(AppHostProjectPath) ? AppHostProjectPath : string.Empty,
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        AppHostProjectPath = dialog.FileName;
        StatusText = "状态：宿主项目路径已更新。";
    }
    private async Task StartNewSessionAsync()
    {
        try
        {
            EnterDraftSessionState("状态：已切换到空白草稿，发送首条消息时会创建新会话。");
            if (isInitialized)
            {
                await RefreshRecentThreadsAsync().ConfigureAwait(true);
            }

            FocusInput();
        }
        catch (Exception ex)
        {
            AppendSystemMessage($"新会话初始化失败：{ex.Message}");
            SetBusy(false, "状态：新会话初始化失败。", keepInitialized: isInitialized);
        }
    }

    private void EnterDraftSessionState(string status)
    {
        CaptureCurrentThreadInputState();
        ResetFollowUpState();
        assistantMessages.Clear();
        finalizedTurnIds.Clear();
        handledTurnErrorKeys.Clear();
        activeToolCallKeys.Clear();
        pendingFollowUpEntries.Clear();
        isDispatchingPendingFollowUp = false;
        Messages.Clear();
        ClearCurrentPlan();
        ClearPendingActionRequests();
        ThreadId = null;
        SelectedThread = null;
        ThreadRenameText = string.Empty;
        InputText = string.Empty;
        shouldStartNewThreadOnNextSend = true;
        SetBusy(false, status, keepInitialized: isInitialized);
    }

    private async Task RefreshRecentThreadsAsync(bool selectCurrentThread = false)
    {
        if (sidecarBridge is null || !isInitialized)
        {
            recentThreads.Clear();
            SelectedThread = null;
            RefreshCommandState();
            return;
        }

        try
        {
            var threads = await LoadRecentThreadsAsync(CancellationToken.None).ConfigureAwait(true);
            recentThreads.Clear();
            foreach (var thread in threads)
            {
                var subAgentSource = thread.Source?.SubAgentSource;
                var isSpawnedSubAgentThread = subAgentSource?.Kind == TianShuSidecarSubAgentSourceKind.ThreadSpawn;
                recentThreads.Add(new ThreadListEntry(
                    thread.ThreadId,
                    thread.Preview,
                    thread.Name,
                    thread.Cwd,
                    thread.UpdatedAt,
                    IncludeArchivedThreads,
                    string.Equals(thread.ThreadId, ThreadId, StringComparison.Ordinal),
                    thread.CreatedAt,
                    thread.AgentNickname ?? subAgentSource?.AgentNickname,
                    thread.AgentRole ?? subAgentSource?.AgentRole,
                    isSpawnedSubAgentThread ? subAgentSource?.ParentThreadId : null,
                    isSpawnedSubAgentThread ? subAgentSource?.Depth : null));
            }

            if (selectCurrentThread)
            {
                SelectCurrentThreadFromRecentList();
            }
            else if (SelectedThread is not null)
            {
                var currentSelectionId = SelectedThread.ThreadId;
                SelectedThread = FindThreadEntry(currentSelectionId);
            }
        }
        catch (Exception ex)
        {
            StatusText = BuildStatusText($"加载线程列表失败：{ex.Message}", "加载线程列表失败。");
        }
        finally
        {
            RefreshCommandState();
        }
    }

    private async Task ResumeSelectedThreadAsync()
    {
        if (SelectedThread is null)
        {
            return;
        }

        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            SetBusy(true, "状态：正在恢复线程……", keepInitialized: true);
            var session = await sidecarBridge.ResumeThreadAsync(SelectedThread.ThreadId, CancellationToken.None).ConfigureAwait(true)
                ?? throw new InvalidOperationException("sidecar 未返回线程内容。 ");

            ResetFollowUpState();
            ApplyThreadSession(session);
            SetBusy(false, "状态：线程已恢复。", keepInitialized: true);
            await RefreshRecentThreadsAsync(selectCurrentThread: true).ConfigureAwait(true);
            FocusInput();
        }
        catch (Exception ex)
        {
            AppendSystemMessage($"恢复线程失败：{ex.Message}", addBlankLineBefore: true);
            SetBusy(false, "状态：恢复线程失败。", keepInitialized: isInitialized);
        }
    }

    
    private async Task RenameSelectedThreadAsync()
    {
        if (SelectedThread is null)
        {
            return;
        }

        var threadIdToRename = SelectedThread.ThreadId;
        var newName = (ThreadRenameText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            SetBusy(true, "状态：正在重命名线程……", keepInitialized: true);
            await sidecarBridge.RenameThreadAsync(threadIdToRename, newName, CancellationToken.None).ConfigureAwait(true);
            SetBusy(false, "状态：线程已重命名。", keepInitialized: true);
            await RefreshRecentThreadsAsync(selectCurrentThread: true).ConfigureAwait(true);
            SelectedThread = FindThreadEntry(threadIdToRename);
            FocusInput();
        }
        catch (Exception ex)
        {
            AppendSystemMessage($"线程重命名失败：{ex.Message}", addBlankLineBefore: true);
            SetBusy(false, "状态：线程重命名失败。", keepInitialized: isInitialized);
        }
    }

    private async Task ArchiveSelectedThreadAsync()
    {
        if (SelectedThread is null)
        {
            return;
        }

        var threadToArchive = SelectedThread;
        var displayName = BuildThreadDisplayName(threadToArchive);
        var confirm = MessageBox.Show(
            $"确认归档线程“{displayName}”？",
            "归档线程",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            var archivedCurrentThread = string.Equals(threadToArchive.ThreadId, ThreadId, StringComparison.Ordinal);
            SetBusy(true, "状态：正在归档线程……", keepInitialized: true);
            await sidecarBridge.ArchiveThreadAsync(threadToArchive.ThreadId, CancellationToken.None).ConfigureAwait(true);

            if (archivedCurrentThread)
            {
                EnterDraftSessionState("状态：当前线程已归档，等待创建新会话。");
                await RefreshRecentThreadsAsync().ConfigureAwait(true);
            }
            else
            {
                SetBusy(false, "状态：线程已归档。", keepInitialized: true);
                SelectedThread = null;
                await RefreshRecentThreadsAsync().ConfigureAwait(true);
            }

            FocusInput();
        }
        catch (Exception ex)
        {
            AppendSystemMessage($"线程归档失败：{ex.Message}", addBlankLineBefore: true);
            SetBusy(false, "状态：线程归档失败。", keepInitialized: isInitialized);
        }
    }

    private async Task<IReadOnlyList<TianShuSidecarThreadItem>> LoadRecentThreadsAsync(CancellationToken cancellationToken)
    {
        if (sidecarBridge is null)
        {
            return Array.Empty<TianShuSidecarThreadItem>();
        }

        var request = new TianShuSidecarThreadListRequest
        {
            Limit = 200,
            Archived = IncludeArchivedThreads,
            Cwd = MatchCurrentWorkingDirectory ? WorkingDirectory : null,
            MatchCurrentCwd = MatchCurrentWorkingDirectory,
            SortKey = "updated_at",
        };
        var items = new List<TianShuSidecarThreadItem>();
        string? previousCursor = null;
        while (true)
        {
            var page = await sidecarBridge.ListThreadsAsync(request, cancellationToken).ConfigureAwait(true);
            items.AddRange(page.Items);

            if (string.IsNullOrWhiteSpace(page.NextCursor)
                || string.Equals(previousCursor, page.NextCursor, StringComparison.Ordinal))
            {
                return items;
            }

            previousCursor = page.NextCursor;
            request.Cursor = page.NextCursor;
        }
    }

    private async Task ClearAllThreadsAsync()
    {
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            var visibleThreadIds = recentThreads.Select(static thread => thread.ThreadId).ToArray();
            var threads = await LoadRecentThreadsAsync(CancellationToken.None).ConfigureAwait(true);
            var targetThreadIds = ResolveClearAllTargetThreadIds(
                visibleThreadIds,
                threads.Select(static thread => thread.ThreadId));

            if (targetThreadIds.Count == 0)
            {
                CapabilityStatusText = "线程操作：当前列表没有可清空的会话。";
                ThreadOperationResultText = JsonSerializer.Serialize(new
                {
                    message = "当前列表没有可清空的会话。",
                    includeArchived = IncludeArchivedThreads,
                    matchCurrentWorkingDirectory = MatchCurrentWorkingDirectory,
                }, prettyJsonOptions);
                StatusText = "状态：当前列表没有可清空的会话。";
                FocusInput();
                return;
            }

            var scopeText = MatchCurrentWorkingDirectory ? "当前工作目录" : "全部工作目录";
            var archiveText = IncludeArchivedThreads ? "归档会话" : "活动会话";
            var confirm = MessageBox.Show(
                $"确认清空当前列表中的 {targetThreadIds.Count} 个会话吗？{Environment.NewLine}范围：{scopeText} / {archiveText}{Environment.NewLine}该操作不可恢复。",
                "清空会话",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            SetBusy(true, "状态：正在清空会话列表……", keepInitialized: true);

            var deletedCount = 0;
            var deletedCurrentThread = false;
            var failedThreadIds = new List<string>();
            IReadOnlyList<string> remainingThreadIds = targetThreadIds;
            while (remainingThreadIds.Count > 0)
            {
                var deletedThisPass = 0;
                foreach (var threadId in remainingThreadIds)
                {
                    try
                    {
                        await sidecarBridge.DeleteThreadAsync(threadId, CancellationToken.None).ConfigureAwait(true);
                        deletedCount++;
                        deletedThisPass++;
                        failedThreadIds.Remove(threadId);
                        if (string.Equals(threadId, ThreadId, StringComparison.Ordinal))
                        {
                            deletedCurrentThread = true;
                        }
                    }
                    catch
                    {
                        if (!failedThreadIds.Contains(threadId, StringComparer.Ordinal))
                        {
                            failedThreadIds.Add(threadId);
                        }
                    }
                }

                if (deletedThisPass == 0)
                {
                    break;
                }

                var refreshedRemainingThreads = await LoadRecentThreadsAsync(CancellationToken.None)
                    .ConfigureAwait(true);
                remainingThreadIds = refreshedRemainingThreads
                    .Select(static thread => thread.ThreadId)
                    .Where(static threadId => !string.IsNullOrWhiteSpace(threadId))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
            }

            var remainingThreads = await LoadRecentThreadsAsync(CancellationToken.None).ConfigureAwait(true);

            ThreadOperationResultText = JsonSerializer.Serialize(new
            {
                deletedCount,
                failedCount = failedThreadIds.Count,
                failedThreadIds,
                remainingCount = remainingThreads.Count,
                remainingThreadIds = remainingThreads.Select(static thread => thread.ThreadId).ToArray(),
                includeArchived = IncludeArchivedThreads,
                matchCurrentWorkingDirectory = MatchCurrentWorkingDirectory,
            }, prettyJsonOptions);

            if (deletedCurrentThread)
            {
                EnterDraftSessionState(
                    remainingThreads.Count == 0
                        ? "状态：会话已清空，等待创建新会话。"
                        : "状态：会话已部分清空，等待创建新会话。");
                await RefreshRecentThreadsAsync().ConfigureAwait(true);
            }
            else
            {
                SetBusy(false, remainingThreads.Count == 0 ? "状态：会话已清空。" : "状态：会话已部分清空。", keepInitialized: true);
                SelectedThread = null;
                await RefreshRecentThreadsAsync().ConfigureAwait(true);
            }

            CapabilityStatusText = remainingThreads.Count == 0
                ? $"线程操作：已清空 {deletedCount} 个会话。"
                : $"线程操作：已删除 {deletedCount} 个会话，仍剩 {remainingThreads.Count} 个。";
            FocusInput();
        }
        catch (Exception ex)
        {
            CapabilityStatusText = "线程操作：清空会话失败。";
            ThreadOperationResultText = ex.Message;
            AppendSystemMessage($"清空会话失败：{ex.Message}", addBlankLineBefore: true);
            SetBusy(false, "状态：清空会话失败。", keepInitialized: isInitialized);
        }
    }

    private IReadOnlyList<string> ResolveClearAllTargetThreadIds(IEnumerable<string> visibleThreadIds, IEnumerable<string> refreshedThreadIds)
    {
        var refreshedTargets = NormalizeThreadIds(refreshedThreadIds);
        if (refreshedTargets.Count > 0)
        {
            return refreshedTargets;
        }

        return NormalizeThreadIds(visibleThreadIds);
    }

    private static IReadOnlyList<string> NormalizeThreadIds(IEnumerable<string> threadIds)
    {
        return threadIds
            .Where(static threadId => !string.IsNullOrWhiteSpace(threadId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private async Task DeleteSelectedThreadAsync()
    {
        var targetThreadId = ResolveThreadOperationTargetId();
        if (string.IsNullOrWhiteSpace(targetThreadId))
        {
            return;
        }

        var targetEntry = FindThreadEntry(targetThreadId);
        var displayName = string.IsNullOrWhiteSpace(BuildThreadDisplayName(targetEntry))
            ? (string.Equals(targetThreadId, ThreadId, StringComparison.Ordinal) ? CurrentThreadTitle : targetThreadId)
            : BuildThreadDisplayName(targetEntry);
        var confirm = MessageBox.Show(
            $"确认永久删除线程“{displayName}”？{Environment.NewLine}该操作不可恢复。",
            "删除线程",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            var deletingCurrentThread = string.Equals(targetThreadId, ThreadId, StringComparison.Ordinal);
            SetBusy(true, "状态：正在删除线程……", keepInitialized: true);
            await sidecarBridge.DeleteThreadAsync(targetThreadId, CancellationToken.None).ConfigureAwait(true);

            ThreadOperationResultText = JsonSerializer.Serialize(new
            {
                message = "线程已删除。",
                threadId = targetThreadId,
            }, prettyJsonOptions);
            CapabilityStatusText = "线程操作：线程已删除。";

            if (deletingCurrentThread)
            {
                EnterDraftSessionState("状态：当前线程已删除，等待创建新会话。");
                await RefreshRecentThreadsAsync().ConfigureAwait(true);
            }
            else
            {
                SetBusy(false, "状态：线程已删除。", keepInitialized: true);
                SelectedThread = null;
                await RefreshRecentThreadsAsync().ConfigureAwait(true);
            }

            FocusInput();
        }
        catch (Exception ex)
        {
            CapabilityStatusText = "线程操作：删除线程失败。";
            ThreadOperationResultText = ex.Message;
            AppendSystemMessage($"删除线程失败：{ex.Message}", addBlankLineBefore: true);
            SetBusy(false, "状态：删除线程失败。", keepInitialized: isInitialized);
        }
    }

    private async Task ForkSelectedThreadAsync()
    {
        var targetThreadId = ResolveThreadOperationTargetId();
        if (string.IsNullOrWhiteSpace(targetThreadId))
        {
            return;
        }

        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            SetActionRunning(true, "线程操作：正在分叉线程。");
            var forkedThread = await sidecarBridge.ForkThreadAsync(targetThreadId, CancellationToken.None).ConfigureAwait(true)
                ?? throw new InvalidOperationException("sidecar 未返回分叉后的线程。");

            var session = await sidecarBridge.ResumeThreadAsync(forkedThread.ThreadId, CancellationToken.None).ConfigureAwait(true);
            if (session is not null)
            {
                ResetFollowUpState();
                ApplyThreadSession(session);
            }
            else
            {
                ThreadId = forkedThread.ThreadId;
            }

            ThreadOperationResultText = JsonSerializer.Serialize(new
            {
                message = "线程分叉成功。",
                threadId = forkedThread.ThreadId,
                preview = forkedThread.Preview,
                cwd = forkedThread.Cwd,
                updatedAt = forkedThread.UpdatedAt.ToUnixTimeSeconds(),
            }, prettyJsonOptions);
            CapabilityStatusText = "线程操作：线程分叉成功。";
            await RefreshRecentThreadsAsync(selectCurrentThread: true).ConfigureAwait(true);
            FocusInput();
        }
        catch (Exception ex)
        {
            CapabilityStatusText = "线程操作：线程分叉失败。";
            ThreadOperationResultText = ex.Message;
            AppendSystemMessage($"线程分叉失败：{ex.Message}", addBlankLineBefore: true);
        }
        finally
        {
            SetActionRunning(false, CapabilityStatusText);
        }
    }

    private async Task ReadSelectedThreadAsync()
    {
        var targetThreadId = ResolveThreadOperationTargetId();
        if (string.IsNullOrWhiteSpace(targetThreadId))
        {
            return;
        }

        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            SetActionRunning(true, "线程操作：正在查看线程详情。");
            var result = await sidecarBridge.ReadThreadAsync(targetThreadId!, includeTurns: true, CancellationToken.None)
                .ConfigureAwait(true);

            ThreadOperationResultText = FormatThreadOperationResult(result);
            CapabilityStatusText = "线程操作：线程详情已加载。";
        }
        catch (Exception ex)
        {
            CapabilityStatusText = "线程操作：查看线程详情失败。";
            ThreadOperationResultText = ex.Message;
            AppendSystemMessage($"查看线程详情失败：{ex.Message}", addBlankLineBefore: true);
        }
        finally
        {
            SetActionRunning(false, CapabilityStatusText);
        }
    }

    private async Task UnarchiveSelectedThreadAsync()
    {
        var targetThreadId = ResolveThreadOperationTargetId();
        if (string.IsNullOrWhiteSpace(targetThreadId))
        {
            return;
        }

        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            SetActionRunning(true, "线程操作：正在取消归档线程。");
            var result = await sidecarBridge.UnarchiveThreadAsync(targetThreadId!, CancellationToken.None)
                .ConfigureAwait(true);

            ThreadOperationResultText = FormatThreadOperationResult(result);
            CapabilityStatusText = "线程操作：线程已取消归档。";
            await RefreshRecentThreadsAsync(selectCurrentThread: true).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            CapabilityStatusText = "线程操作：取消归档失败。";
            ThreadOperationResultText = ex.Message;
            AppendSystemMessage($"取消归档失败：{ex.Message}", addBlankLineBefore: true);
        }
        finally
        {
            SetActionRunning(false, CapabilityStatusText);
        }
    }

    private async Task RollbackSelectedThreadAsync()
    {
        var targetThreadId = ResolveThreadOperationTargetId();
        if (string.IsNullOrWhiteSpace(targetThreadId) || !TryGetRollbackTurns(out var rollbackTurns))
        {
            return;
        }

        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            SetActionRunning(true, "线程操作：正在回滚线程。");
            var result = await sidecarBridge.RollbackThreadAsync(targetThreadId!, rollbackTurns, CancellationToken.None)
                .ConfigureAwait(true);

            ThreadOperationResultText = FormatThreadOperationResult(result);
            CapabilityStatusText = "线程操作：线程已回滚。";

            if (sidecarBridge is not null && string.Equals(targetThreadId, ThreadId, StringComparison.Ordinal))
            {
                var session = await sidecarBridge.ResumeThreadAsync(targetThreadId, CancellationToken.None).ConfigureAwait(true);
                if (session is not null)
                {
                    ResetFollowUpState();
                    ApplyThreadSession(session);
                }
            }

            await RefreshRecentThreadsAsync(selectCurrentThread: true).ConfigureAwait(true);
            FocusInput();
        }
        catch (Exception ex)
        {
            CapabilityStatusText = "线程操作：线程回滚失败。";
            ThreadOperationResultText = ex.Message;
            AppendSystemMessage($"线程回滚失败：{ex.Message}", addBlankLineBefore: true);
        }
        finally
        {
            SetActionRunning(false, CapabilityStatusText);
        }
    }

    private async Task UpdateSelectedThreadMetadataAsync()
    {
        var targetThreadId = ResolveThreadOperationTargetId();
        if (string.IsNullOrWhiteSpace(targetThreadId))
        {
            return;
        }

        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            SetActionRunning(true, "线程操作：正在更新线程元数据。");
            var result = await sidecarBridge.UpdateThreadMetadataAsync(
                    new TianShuSidecarThreadMetadataUpdateRequest
                    {
                        ThreadId = targetThreadId!,
                        GitInfo = new TianShuSidecarGitInfo
                        {
                            Branch = Normalize(MetadataBranchText) ?? string.Empty,
                            Sha = Normalize(MetadataShaText) ?? string.Empty,
                            OriginUrl = Normalize(MetadataOriginUrlText) ?? string.Empty,
                        },
                    },
                    CancellationToken.None)
                .ConfigureAwait(true);

            ThreadOperationResultText = FormatThreadOperationResult(result);
            CapabilityStatusText = "线程操作：线程元数据已更新。";
        }
        catch (Exception ex)
        {
            CapabilityStatusText = "线程操作：更新线程元数据失败。";
            ThreadOperationResultText = ex.Message;
            AppendSystemMessage($"更新线程元数据失败：{ex.Message}", addBlankLineBefore: true);
        }
        finally
        {
            SetActionRunning(false, CapabilityStatusText);
        }
    }

    private async Task ExecuteCapabilityActionAsync()
    {
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            SetActionRunning(true, "功能面板：正在执行 typed 能力。");
            var response = await sidecarBridge.InvokeCapabilityAsync(
                    SelectedCapability,
                    CapabilityRequiresMethod(SelectedCapability) ? GetCapabilityProtocolMethod(SelectedCapability, SelectedActionMethod) : null,
                    NormalizeJsonInput(ActionPayloadJson),
                    CancellationToken.None)
                .ConfigureAwait(true);

            CapabilityStatusText = $"功能面板：{response.Message}";
            ActionResultText = FormatActionResponse(response);
        }
        catch (Exception ex)
        {
            CapabilityStatusText = "功能面板：执行失败。";
            ActionResultText = ex.Message;
        }
        finally
        {
            SetActionRunning(false, CapabilityStatusText);
        }
    }

    private async Task SubmitApprovalAsync()
    {
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            if (SelectedApprovalDecision is null || string.IsNullOrWhiteSpace(PendingApprovalCallId))
            {
                return;
            }

            SetActionRunning(true, "功能面板：正在提交审批结果。");
            await sidecarBridge.RespondApprovalAsync(
                    PendingApprovalCallId,
                    SelectedApprovalDecision.Payload,
                    Normalize(ApprovalNoteText),
                    CancellationToken.None)
                .ConfigureAwait(true);

            MarkPendingInteractiveRequestAwaitingResolution("approval_requested", PendingApprovalCallId);
            CapabilityStatusText = $"功能面板：已提交审批结果（{SelectedApprovalDecision.DisplayName}）。";
            ActionResultText = PendingApprovalDetailsText;
        }
        catch (Exception ex)
        {
            CapabilityStatusText = "功能面板：审批提交失败。";
            ActionResultText = ex.Message;
        }
        finally
        {
            SetActionRunning(false, CapabilityStatusText);
        }
    }

    private async Task SubmitPendingUserInputAsync()
    {
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            if (string.IsNullOrWhiteSpace(PendingUserInputCallId))
            {
                return;
            }

            var answers = BuildUserInputAnswersPayload();
            SetActionRunning(true, "功能面板：正在提交补录输入。");
            await sidecarBridge.RespondUserInputAsync(PendingUserInputCallId, answers, CancellationToken.None).ConfigureAwait(true);

            MarkPendingInteractiveRequestAwaitingResolution("request_user_input", PendingUserInputCallId);
            CapabilityStatusText = "功能面板：已提交补录输入。";
            ActionResultText = PendingUserInputDetailsText;
        }
        catch (Exception ex)
        {
            CapabilityStatusText = "功能面板：补录提交失败。";
            ActionResultText = ex.Message;
        }
        finally
        {
            SetActionRunning(false, CapabilityStatusText);
        }
    }

    private async Task SubmitPendingPermissionAsync()
    {
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            if (string.IsNullOrWhiteSpace(PendingPermissionCallId) || SelectedPermissionScope is null)
            {
                return;
            }

            var permissions = BuildPermissionPayload();
            SetActionRunning(true, "功能面板：正在提交权限响应。");
            await sidecarBridge.RespondPermissionAsync(
                    PendingPermissionCallId,
                    permissions,
                    SelectedPermissionScope.Scope,
                    CancellationToken.None)
                .ConfigureAwait(true);

            MarkPendingInteractiveRequestAwaitingResolution("permission_requested", PendingPermissionCallId);
            CapabilityStatusText = $"功能面板：已提交权限响应（{SelectedPermissionScope.DisplayName}）。";
            ActionResultText = PendingPermissionDetailsText;
        }
        catch (Exception ex)
        {
            CapabilityStatusText = "功能面板：权限响应提交失败。";
            ActionResultText = ex.Message;
        }
        finally
        {
            SetActionRunning(false, CapabilityStatusText);
        }
    }

    private async Task InvokeRuntimeSurfaceAsync()
    {
        try
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            if (sidecarBridge is null)
            {
                throw new InvalidOperationException("TianShu sidecar 尚未建立。");
            }

            SetActionRunning(true, "功能面板：正在执行 runtime surface 调用。");
            var response = await sidecarBridge.InvokeRuntimeSurfaceAsync(
                    RpcMethodText.Trim(),
                    NormalizeJsonInput(RpcPayloadJson),
                    CancellationToken.None)
                .ConfigureAwait(true);

            RpcResultText = FormatDiagnosticResponse(response);
            CapabilityStatusText = $"功能面板：{response.Message}";
        }
        catch (Exception ex)
        {
            RpcResultText = ex.Message;
            CapabilityStatusText = "功能面板：runtime surface 调用失败。";
        }
        finally
        {
            SetActionRunning(false, CapabilityStatusText);
        }
    }

    private void RefreshActionMethodOptions()
    {
        actionMethods.Clear();
        foreach (var method in GetCapabilityMethods(SelectedCapability))
        {
            actionMethods.Add(method);
        }

        SelectedActionMethod = actionMethods.Count == 0
            ? null
            : actionMethods.Contains(SelectedActionMethod)
                ? SelectedActionMethod
                : actionMethods[0];

        ApplySuggestedActionPayloadIfNeeded();
    }

    private void ApplySuggestedActionPayloadIfNeeded()
    {
        var suggestedPayload = BuildSuggestedActionPayload();
        if (string.IsNullOrWhiteSpace(suggestedPayload))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ActionPayloadJson) || string.Equals(ActionPayloadJson, lastGeneratedActionPayload, StringComparison.Ordinal))
        {
            ActionPayloadJson = suggestedPayload;
            lastGeneratedActionPayload = suggestedPayload;
        }
    }

    private void ForceSuggestedActionPayload()
    {
        var suggestedPayload = BuildSuggestedActionPayload();
        if (string.IsNullOrWhiteSpace(suggestedPayload))
        {
            return;
        }

        ActionPayloadJson = suggestedPayload;
        lastGeneratedActionPayload = suggestedPayload;
    }

    private string BuildSuggestedActionPayload()
    {
        object payload = SelectedCapability switch
        {
            TianShuSidecarCapability.RuntimeSurface => BuildRuntimeSurfaceSuggestedPayload(SelectedActionMethod),
            TianShuSidecarCapability.CommandExecution => BuildCommandExecutionSuggestedPayload(SelectedActionMethod),
            TianShuSidecarCapability.CodeModeExec => new
            {
                threadId = ThreadId ?? string.Empty,
                input = "console.log('hello from code mode');",
                yieldTimeMs = 1000,
                maxOutputTokens = 512,
            },
            TianShuSidecarCapability.CodeModeWait => new
            {
                threadId = ThreadId ?? string.Empty,
                cellId = string.Empty,
                yieldTimeMs = 1000,
                maxTokens = 512,
                terminate = false,
            },
            TianShuSidecarCapability.FuzzyFileSearch => BuildFuzzyFileSearchSuggestedPayload(SelectedActionMethod),
            TianShuSidecarCapability.ThreadOperation => BuildThreadOperationSuggestedPayload(SelectedActionMethod),
            TianShuSidecarCapability.Realtime => BuildRealtimeSuggestedPayload(SelectedActionMethod),
            TianShuSidecarCapability.AgentOperation => BuildAgentOperationSuggestedPayload(SelectedActionMethod),
            TianShuSidecarCapability.Feedback => new
            {
                classification = "not_helpful",
                includeLogs = true,
                threadId = ThreadId ?? string.Empty,
                reason = "请填写反馈原因",
            },
            TianShuSidecarCapability.WindowsSandbox => new
            {
                mode = "unelevated",
                cwd = WorkingDirectory,
            },
            _ => new { },
        };

        return JsonSerializer.Serialize(payload, prettyJsonOptions);
    }

    private object BuildRuntimeSurfaceSuggestedPayload(string? method)
        => method switch
        {
            "ConfigRead" => new { cwd = WorkingDirectory, includeLayers = true },
            "ConfigRequirementsRead" => new { },
            "ModelList" => new { limit = 20, includeHidden = false },
            "ModelCatalogRead" => new { cwd = WorkingDirectory, includeHidden = false, limit = 50 },
            "ModelBindingResolve" => new
            {
                cwd = WorkingDirectory,
                providerKey = string.Empty,
                modelKey = string.Empty,
                reasoningEffort = string.Empty,
                reasoningSummary = string.Empty,
                verbosity = string.Empty,
                preferWebsocketTransport = false,
            },
            "SkillsList" => new { cwds = new[] { WorkingDirectory }, forceReload = false },
            "SkillsConfigWrite" => new { path = "skills/sample-skill", enabled = true, cwd = WorkingDirectory },
            "SkillsRemoteList" => new { hazelnutScope = string.Empty, productSurface = string.Empty, enabled = true },
            "SkillsRemoteExport" => new { hazelnutId = Normalize(RemoteSkillHazelnutId) ?? "sample-hazelnut-id" },
            "PluginList" => new { cwds = new[] { WorkingDirectory } },
            "PluginRead" => new { marketplacePath = string.Empty, pluginName = string.Empty },
            "PluginInstall" => new { marketplacePath = string.Empty, pluginName = string.Empty, cwd = WorkingDirectory },
            "PluginUninstall" => new { pluginId = "sample-plugin@sample-marketplace", cwd = WorkingDirectory },
            "AppList" => new { limit = 20, cursor = string.Empty, threadId = ThreadId ?? string.Empty, forceRefetch = false },
            "ConversationThreadRead" => new { threadId = ThreadId ?? string.Empty },
            "CollaborationCreate" => new
            {
                spaceId = "sample-space",
                key = "sample-space",
                displayName = "示例协作空间",
                purpose = "请填写协作目标",
                defaultWorkspace = WorkingDirectory,
                defaultExecutionProfile = string.Empty,
                policyKey = string.Empty,
            },
            "CollaborationConfigure" => new
            {
                spaceId = "sample-space",
                displayName = "更新后的协作空间名称",
                purpose = "更新后的协作目标",
                defaultWorkspace = WorkingDirectory,
                defaultExecutionProfile = string.Empty,
                policyKey = string.Empty,
            },
            "CollaborationArchive" => new { spaceId = "sample-space" },
            "CollaborationOverviewRead" => new { spaceId = "sample-space" },
            "CollaborationSpaceRead" => new { spaceId = "sample-space" },
            "CollaborationList" => new { includeArchived = false },
            "ParticipantBindSession" => new { sessionId = string.Empty, participantId = string.Empty },
            "ParticipantBindWorkflow" => new { workflowId = string.Empty, participantId = string.Empty },
            "ParticipantUpdateRole" => new { participantId = string.Empty, role = "owner" },
            "SessionSnapshotRead" => new { },
            "SessionOverviewRead" => new { sessionId = string.Empty },
            "SessionList" => new { collaborationSpaceId = string.Empty, includeClosed = false },
            "GovernanceApprovalQueueRead" => new { participantId = string.Empty },
            "GovernanceUserInputsList" => new { participantId = string.Empty },
            "ParticipantRead" => new { participantId = string.Empty },
            "ParticipantViewRead" => new { participantId = string.Empty },
            "ParticipantList" => new { spaceId = "sample-space" },
            "ArtifactDetailRead" => new { artifactId = string.Empty },
            "ArtifactCollectionRead" => new { collaborationSpaceId = "sample-space", producedByParticipantId = string.Empty },
            "WorkflowCreate" => new
            {
                workflowId = "sample-workflow",
                spaceId = "sample-space",
                displayName = "示例工作流",
                participantId = string.Empty,
                threadId = ThreadId ?? string.Empty,
            },
            "WorkflowPlanPublish" => new
            {
                workflowId = "sample-workflow",
                title = "示例计划",
                steps = new[]
                {
                    new
                    {
                        order = 0,
                        title = "请填写步骤标题",
                        description = "请填写步骤说明",
                    },
                },
            },
            "WorkflowTaskCreate" => new
            {
                taskId = "sample-task",
                workflowId = "sample-workflow",
                title = "请填写任务标题",
                state = "todo",
                participantId = string.Empty,
            },
            "WorkflowTaskUpdateState" => new
            {
                taskId = "sample-task",
                state = "in-progress",
                participantId = string.Empty,
            },
            "WorkflowBoardRead" => new { workflowId = "sample-workflow" },
            "WorkflowTaskBoardRead" => new { workflowId = "sample-workflow" },
            "WorkflowPlanRead" => new { workflowId = "sample-workflow" },
            "AgentList" => new { limit = 20, includePrimaryThreads = true },
            "AgentRosterRead" => new { workflowId = "sample-workflow" },
            "AgentTeamRead" => new { teamId = "sample-team" },
            "IdentityAccountRead" => new { accountId = "sample-account" },
            "IdentityDevicesList" => new { accountId = "sample-account" },
            "MemoryProvidersList" => new { scopeKind = "Workspace" },
            "MemorySpacesList" => new { scopeKind = "Collaboration" },
            "MemoryOverlayRead" => new { memorySpaceId = "memory:collaboration:sample-space", collaborationSpaceId = "sample-space" },
            "MemoryFilter" => new { memorySpaceId = new { value = "memory-space-capability-001" }, key = "pref.shell", sourceKind = "conversation", scopeKind = "Workspace" },
            "MemoryAdd" => new { memorySpaceId = new { value = "memory-space-capability-001" }, key = "pref.shell", value = "pwsh", confidence = 0.9 },
            "MemoryExtract" => new
            {
                memorySpaceId = new { value = "memory-space-capability-001" },
                source = new { sourceKind = "conversation", sourceId = "turn-capability-001" },
                content = "记住我更喜欢 pwsh",
            },
            "MemoryImport" => new
            {
                memorySpaceId = new { value = "memory-space-capability-001" },
                source = new { sourceKind = "file", sourceId = "memory.json" },
            },
            "MemoryExport" => new
            {
                memorySpaceId = new { value = "memory-space-capability-001" },
                destination = new { sourceKind = "externalProvider", sourceId = "provider-capability-export" },
                filter = new { key = "pref.shell" },
            },
            "MemoryBindProvider" => new
            {
                providerId = "provider-capability-local",
                memorySpaceId = new { value = "memory-space-capability-001" },
                mode = "readWrite",
                allowedCapabilities = 10,
            },
            "MemoryForget" => new { memoryRecordId = new { value = "memory-record-capability-001" } },
            "MemoryDelete" => new { memoryRecordId = new { value = "memory-record-capability-001" }, reason = "cleanup" },
            "MemoryFeedbackRecord" => new { memoryRecordId = new { value = "memory-record-capability-001" }, decision = "applied", feedback = "accepted" },
            "MemoryCitationRecord" => new
            {
                citation = new
                {
                    entries = new[]
                    {
                        new
                        {
                            memoryRecordId = new { value = "memory-record-capability-001" },
                            memorySpaceId = new { value = "memory-space-capability-001" },
                            key = "pref.shell",
                        },
                    },
                },
            },
            "DiagnosticsTraceRead" => new { traceId = string.Empty },
            "DiagnosticsAttemptsList" => new { executionId = string.Empty },
            "ReviewStart" => new
            {
                threadId = ThreadId ?? string.Empty,
                delivery = "inline",
                target = new
                {
                    type = "uncommittedChanges",
                },
            },
            "ConfigValueWrite" => new
            {
                keyPath = "features.example",
                value = true,
                mergeStrategy = "replace",
                cwd = WorkingDirectory,
            },
            "ConfigBatchWrite" => new
            {
                edits = new[]
                {
                    new
                    {
                        keyPath = "features.example",
                        value = true,
                        mergeStrategy = "replace",
                    },
                },
                cwd = WorkingDirectory,
            },
            "ExperimentalFeatureList" => new { limit = 20, cursor = string.Empty },
            "McpServerStatusList" => new { limit = 20 },
            "McpServerOauthLogin" => new { name = "sample-mcp-server" },
            "McpServerReload" => new { },
            "CollaborationModeList" => new { },
            "ConversationSummary" => new { threadId = ThreadId ?? string.Empty },
            "GitDiffToRemote" => new { threadId = ThreadId ?? string.Empty },
            _ => new { },
        };

    private object BuildCommandExecutionSuggestedPayload(string? method)
        => method switch
        {
            "Exec" => new
            {
                cwd = WorkingDirectory,
                command = new[] { "cmd.exe", "/c", "cd" },
                threadId = ThreadId ?? string.Empty,
                timeoutMs = 5000,
            },
            "Write" => new
            {
                processId = string.Empty,
                deltaBase64 = string.Empty,
                closeStdin = false,
            },
            "Terminate" => new
            {
                processId = string.Empty,
            },
            "Resize" => new
            {
                processId = string.Empty,
                size = new
                {
                    rows = 30,
                    cols = 120,
                },
            },
            _ => new { },
        };

    private object BuildFuzzyFileSearchSuggestedPayload(string? method)
        => method switch
        {
            "Search" => new { query = "TianShu", cwd = WorkingDirectory, limit = 20 },
            "SessionStart" => new { sessionId = Guid.NewGuid().ToString("N"), roots = new[] { WorkingDirectory } },
            "SessionUpdate" => new { sessionId = string.Empty, query = "TianShu" },
            "SessionStop" => new { sessionId = string.Empty },
            _ => new { },
        };

    private object BuildThreadOperationSuggestedPayload(string? method)
        => method switch
        {
            "LoadedList" => new { limit = 20 },
            "CompactStart" => new { threadId = ThreadId ?? string.Empty, keepRecentTurns = 8 },
            "BackgroundTerminalsClean" => new { threadId = ThreadId ?? string.Empty },
            "Unsubscribe" => new { threadId = ThreadId ?? string.Empty },
            "Read" => new { threadId = ThreadId ?? string.Empty, includeTurns = true },
            "Delete" => new { threadId = ThreadId ?? string.Empty },
            "Unarchive" => new { threadId = ThreadId ?? string.Empty },
            "MetadataUpdate" => new { threadId = ThreadId ?? string.Empty, gitInfo = new { branch = string.Empty, sha = string.Empty, originUrl = string.Empty } },
            "Rollback" => new { threadId = ThreadId ?? string.Empty, numTurns = 1 },
            _ => new { },
        };

    private object BuildRealtimeSuggestedPayload(string? method)
        => method switch
        {
            "Start" => new { threadId = ThreadId ?? string.Empty, sessionId = Guid.NewGuid().ToString("N"), prompt = "进入 realtime 模式" },
            "AppendText" => new { threadId = ThreadId ?? string.Empty, sessionId = string.Empty, text = "继续" },
            "AppendAudio" => new { threadId = ThreadId ?? string.Empty, sessionId = string.Empty, audio = new { } },
            "HandoffOutput" => new { threadId = ThreadId ?? string.Empty, sessionId = string.Empty, handoffId = string.Empty, output = string.Empty },
            "Stop" => new { threadId = ThreadId ?? string.Empty, sessionId = string.Empty },
            _ => new { },
        };

    private object BuildAgentOperationSuggestedPayload(string? method)
        => method switch
        {
            "ThreadRegister" => new { threadId = ThreadId ?? string.Empty, agentNickname = "worker-1", agentRole = "implementer" },
            "JobCreate" => new { instruction = "请填写任务说明" },
            "JobDispatch" => new { jobId = string.Empty, threadIds = Array.Empty<string>() },
            "JobItemReport" => new { jobId = string.Empty, itemId = string.Empty, status = "completed", result = new { } },
            "JobRead" => new { jobId = string.Empty },
            _ => new { },
        };

    private static IReadOnlyList<string> GetCapabilityMethods(TianShuSidecarCapability capability)
        => capability switch
        {
            TianShuSidecarCapability.RuntimeSurface => new[]
            {
                "ModelList",
                "ModelCatalogRead",
                "ModelBindingResolve",
                "SkillsList",
                "SkillsConfigWrite",
                "SkillsRemoteList",
                "SkillsRemoteExport",
                "PluginList",
                "PluginRead",
                "PluginInstall",
                "PluginUninstall",
                "AppList",
                "ReviewStart",
                "ConfigRead",
                "ConfigRequirementsRead",
                "ConfigValueWrite",
                "ConfigBatchWrite",
                "ExperimentalFeatureList",
                "CollaborationModeList",
                "McpServerStatusList",
                "McpServerReload",
                "McpServerOauthLogin",
                "ConversationThreadRead",
                "CollaborationCreate",
                "CollaborationConfigure",
                "CollaborationArchive",
                "CollaborationOverviewRead",
                "CollaborationSpaceRead",
                "CollaborationList",
                "ParticipantBindSession",
                "ParticipantBindWorkflow",
                "ParticipantUpdateRole",
                "SessionSnapshotRead",
                "SessionOverviewRead",
                "SessionList",
                "GovernanceApprovalQueueRead",
                "GovernanceUserInputsList",
                "ParticipantRead",
                "ParticipantViewRead",
                "ParticipantList",
                "ArtifactDetailRead",
                "ArtifactCollectionRead",
                "WorkflowCreate",
                "WorkflowPlanPublish",
                "WorkflowTaskCreate",
                "WorkflowTaskUpdateState",
                "WorkflowBoardRead",
                "WorkflowTaskBoardRead",
                "WorkflowPlanRead",
                "AgentList",
                "AgentRosterRead",
                "AgentTeamRead",
                "IdentityAccountRead",
                "IdentityDevicesList",
                "MemoryProvidersList",
                "MemorySpacesList",
                "MemoryOverlayRead",
                "MemoryFilter",
                "MemoryAdd",
                "MemoryExtract",
                "MemoryImport",
                "MemoryExport",
                "MemoryBindProvider",
                "MemoryForget",
                "MemoryDelete",
                "MemoryFeedbackRecord",
                "MemoryCitationRecord",
                "DiagnosticsTraceRead",
                "DiagnosticsAttemptsList",
                "ConversationSummary",
                "GitDiffToRemote",
            },
            TianShuSidecarCapability.CommandExecution => new[] { "Exec", "Write", "Terminate", "Resize" },
            TianShuSidecarCapability.FuzzyFileSearch => new[] { "Search", "SessionStart", "SessionUpdate", "SessionStop" },
            TianShuSidecarCapability.ThreadOperation => new[] { "LoadedList", "CompactStart", "BackgroundTerminalsClean", "Unsubscribe", "Read", "Delete", "Unarchive", "MetadataUpdate", "Rollback" },
            TianShuSidecarCapability.Realtime => new[] { "Start", "AppendText", "AppendAudio", "HandoffOutput", "Stop" },
            TianShuSidecarCapability.AgentOperation => new[] { "ThreadRegister", "JobCreate", "JobDispatch", "JobItemReport", "JobRead" },
            _ => Array.Empty<string>(),
        };

    private static bool CapabilityRequiresMethod(TianShuSidecarCapability capability)
        => capability is not TianShuSidecarCapability.CodeModeExec
            and not TianShuSidecarCapability.CodeModeWait
            and not TianShuSidecarCapability.Feedback
            and not TianShuSidecarCapability.WindowsSandbox;

    private static string? GetCapabilityProtocolMethod(TianShuSidecarCapability capability, string? method)
        => capability switch
        {
            TianShuSidecarCapability.RuntimeSurface => method switch
            {
                "ConfigRead" => "config/read",
                "ConfigRequirementsRead" => "configrequirements/read",
                "ModelList" => "model/list",
                "ModelCatalogRead" => "model/catalog/read",
                "ModelBindingResolve" => "model/binding/resolve",
                "SkillsList" => "skills/list",
                "SkillsConfigWrite" => "skills/config/write",
                "SkillsRemoteList" => "skills/remote/list",
                "SkillsRemoteExport" => "skills/remote/export",
                "PluginList" => "plugin/list",
                "PluginRead" => "plugin/read",
                "PluginInstall" => "plugin/install",
                "PluginUninstall" => "plugin/uninstall",
                "AppList" => "app/list",
                "ReviewStart" => "review/start",
                "ConfigValueWrite" => "config/value/write",
                "ConfigBatchWrite" => "config/batchwrite",
                "ExperimentalFeatureList" => "experimentalfeature/list",
                "CollaborationModeList" => "collaborationmode/list",
                "McpServerStatusList" => "mcpserverstatus/list",
                "McpServerReload" => "config/mcpserver/reload",
                "McpServerOauthLogin" => "mcpserver/oauth/login",
                "ConversationThreadRead" => "conversation/thread/read",
                "CollaborationCreate" => "collaboration/create",
                "CollaborationConfigure" => "collaboration/configure",
                "CollaborationArchive" => "collaboration/archive",
                "CollaborationOverviewRead" => "collaboration/overview/read",
                "CollaborationSpaceRead" => "collaboration/space/read",
                "CollaborationList" => "collaboration/list",
                "ParticipantBindSession" => "participant/bindsession",
                "ParticipantBindWorkflow" => "participant/bindworkflow",
                "ParticipantUpdateRole" => "participant/updaterole",
                "SessionSnapshotRead" => "session/snapshot/read",
                "SessionOverviewRead" => "session/overview/read",
                "SessionList" => "session/list",
                "GovernanceApprovalQueueRead" => "governance/approvalqueue/read",
                "GovernanceUserInputsList" => "governance/userinputs/list",
                "ParticipantRead" => "participant/read",
                "ParticipantViewRead" => "participant/view/read",
                "ParticipantList" => "participant/list",
                "ArtifactDetailRead" => "artifact/detail/read",
                "ArtifactCollectionRead" => "artifact/collection/read",
                "WorkflowCreate" => "workflow/create",
                "WorkflowPlanPublish" => "workflow/plan/publish",
                "WorkflowTaskCreate" => "workflow/task/create",
                "WorkflowTaskUpdateState" => "workflow/task/updatestate",
                "WorkflowBoardRead" => "workflow/board/read",
                "WorkflowTaskBoardRead" => "workflow/taskboard/read",
                "WorkflowPlanRead" => "workflow/plan/read",
                "AgentList" => "agent/list",
                "AgentRosterRead" => "agent/roster/read",
                "AgentTeamRead" => "agent/team/read",
                "IdentityAccountRead" => "identity/account/read",
                "IdentityDevicesList" => "identity/devices/list",
                "MemoryProvidersList" => "memory/providers/list",
                "MemorySpacesList" => "memory/spaces/list",
                "MemoryOverlayRead" => "memory/overlay/read",
                "MemoryFilter" => "memory/filter",
                "MemoryAdd" => "memory/add",
                "MemoryExtract" => "memory/extract",
                "MemoryImport" => "memory/import",
                "MemoryExport" => "memory/export",
                "MemoryBindProvider" => "memory/provider/bind",
                "MemoryForget" => "memory/forget",
                "MemoryDelete" => "memory/delete",
                "MemoryFeedbackRecord" => "memory/feedback/record",
                "MemoryCitationRecord" => "memory/citation/record",
                "DiagnosticsTraceRead" => "diagnostics/trace/read",
                "DiagnosticsAttemptsList" => "diagnostics/attempts/list",
                "ConversationSummary" => "artifact/conversationsummary/read",
                "GitDiffToRemote" => "artifact/gitdifftoremote/read",
                _ => method,
            },
            TianShuSidecarCapability.FuzzyFileSearch => method switch
            {
                "Search" => "fuzzyfilesearch",
                "SessionStart" => "fuzzyfilesearch/sessionstart",
                "SessionUpdate" => "fuzzyfilesearch/sessionupdate",
                "SessionStop" => "fuzzyfilesearch/sessionstop",
                _ => method,
            },
            TianShuSidecarCapability.ThreadOperation => method switch
            {
                "LoadedList" => "thread/loaded/list",
                "CompactStart" => "thread/compact/start",
                "BackgroundTerminalsClean" => "thread/backgroundterminals/clean",
                "Unsubscribe" => "thread/unsubscribe",
                "Read" => "thread/read",
                "Delete" => "thread/delete",
                "Unarchive" => "thread/unarchive",
                "MetadataUpdate" => "thread/metadata/update",
                "Rollback" => "thread/rollback",
                _ => method,
            },
            TianShuSidecarCapability.Realtime => method switch
            {
                "Start" => "thread/realtime/start",
                "AppendText" => "thread/realtime/appendtext",
                "AppendAudio" => "thread/realtime/appendaudio",
                "HandoffOutput" => "thread/realtime/handoffoutput",
                "Stop" => "thread/realtime/stop",
                _ => method,
            },
            TianShuSidecarCapability.AgentOperation => method switch
            {
                "ThreadRegister" => "agent/thread/register",
                "JobCreate" => "agent/job/create",
                "JobDispatch" => "agent/job/dispatch",
                "JobItemReport" => "agent/job/item/report",
                "JobRead" => "agent/job/read",
                _ => method,
            },
            _ => method,
        };

    private void SetActionRunning(bool isRunning, string status)
    {
        isActionRunning = isRunning;
        CapabilityStatusText = status;
        RefreshCommandState();
    }

    private string FormatActionResponse(TianShuSidecarResponse response)
    {
        var payloadJson = response.PayloadJson;
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return response.Message;
        }

        return $"{response.Message}{Environment.NewLine}{Environment.NewLine}{TryFormatDiagnosticJson(payloadJson!)}";
    }

    private string FormatDiagnosticResponse(TianShuSidecarResponse response)
    {
        var diagnosticJson = response.DiagnosticJson;
        if (string.IsNullOrWhiteSpace(diagnosticJson))
        {
            return response.Message;
        }

        return $"{response.Message}{Environment.NewLine}{Environment.NewLine}{TryFormatDiagnosticJson(diagnosticJson!)}";
    }

    private static string FormatSettingsResultText(string message, string? detailsJson)
        => string.IsNullOrWhiteSpace(detailsJson)
            ? message
            : $"{message}{Environment.NewLine}{Environment.NewLine}{detailsJson}";

    internal static string BuildConfigRequirementsDetailsJson(TianShuSidecarConfigRequirementsReadResult result)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        return SerializePrettyJson(new
        {
            isDefined = result.IsDefined,
            allowedApprovalPolicies = result.AllowedApprovalPolicies,
            allowedSandboxModes = result.AllowedSandboxModes,
            allowedWebSearchModes = result.AllowedWebSearchModes,
            featureRequirements = result.FeatureRequirements,
            enforceResidency = result.EnforceResidency,
            network = result.Network is null
                ? null
                : new
                {
                    enabled = result.Network.Enabled,
                    httpPort = result.Network.HttpPort,
                    socksPort = result.Network.SocksPort,
                    allowUpstreamProxy = result.Network.AllowUpstreamProxy,
                    dangerouslyAllowNonLoopbackProxy = result.Network.DangerouslyAllowNonLoopbackProxy,
                    dangerouslyAllowNonLoopbackAdmin = result.Network.DangerouslyAllowNonLoopbackAdmin,
                    dangerouslyAllowAllUnixSockets = result.Network.DangerouslyAllowAllUnixSockets,
                    allowedDomains = result.Network.AllowedDomains,
                    deniedDomains = result.Network.DeniedDomains,
                    allowUnixSockets = result.Network.AllowUnixSockets,
                    allowLocalBinding = result.Network.AllowLocalBinding,
                },
        });
    }

    internal static string BuildThreadDetailsJson(TianShuSidecarThreadItem thread)
    {
        if (thread is null)
        {
            throw new ArgumentNullException(nameof(thread));
        }

        return SerializePrettyJson(new
        {
            id = thread.ThreadId,
            preview = thread.Preview,
            name = thread.Name,
            cwd = thread.Cwd,
            path = thread.Path,
            modelProvider = thread.ModelProvider,
            source = thread.Source,
            cliVersion = thread.CliVersion,
            agentNickname = thread.AgentNickname,
            agentRole = thread.AgentRole,
            createdAt = thread.CreatedAt?.ToString("O"),
            updatedAt = thread.UpdatedAt.ToString("O"),
            ephemeral = thread.IsEphemeral,
            sessionConfiguration = thread.SessionConfiguration is null
                ? null
                : new
                {
                    model = thread.SessionConfiguration.Model,
                    modelProvider = thread.SessionConfiguration.ModelProvider,
                    modelProviderId = thread.SessionConfiguration.ModelProviderId,
                    serviceTier = thread.SessionConfiguration.ServiceTier,
                    approvalPolicy = thread.SessionConfiguration.ApprovalPolicy,
                    sandboxPolicy = thread.SessionConfiguration.SandboxPolicy,
                    sandboxPolicyPayload = thread.SessionConfiguration.SandboxPolicyPayload,
                    reasoningEffort = thread.SessionConfiguration.ReasoningEffort,
                    historyLogId = thread.SessionConfiguration.HistoryLogId,
                    historyEntryCount = thread.SessionConfiguration.HistoryEntryCount,
                    rolloutPath = thread.SessionConfiguration.RolloutPath,
                    forkedFromId = thread.SessionConfiguration.ForkedFromId,
                    cwd = thread.SessionConfiguration.Cwd,
                    ephemeral = thread.SessionConfiguration.Ephemeral,
                    allowLoginShell = thread.SessionConfiguration.AllowLoginShell,
                    shellEnvironmentPolicy = thread.SessionConfiguration.ShellEnvironmentPolicy,
                    providerBaseUrl = thread.SessionConfiguration.ProviderBaseUrl,
                    providerApiKeyEnvironmentVariable = thread.SessionConfiguration.ProviderApiKeyEnvironmentVariable,
                    providerWireApi = thread.SessionConfiguration.ProviderWireApi,
                    providerRequestMaxRetries = thread.SessionConfiguration.ProviderRequestMaxRetries,
                    providerStreamMaxRetries = thread.SessionConfiguration.ProviderStreamMaxRetries,
                    providerStreamIdleTimeoutMs = thread.SessionConfiguration.ProviderStreamIdleTimeoutMs,
                    providerSupportsWebsockets = thread.SessionConfiguration.ProviderSupportsWebsockets,
                    webSearchMode = thread.SessionConfiguration.WebSearchMode,
                    serviceName = thread.SessionConfiguration.ServiceName,
                    baseInstructions = thread.SessionConfiguration.BaseInstructions,
                    developerInstructions = thread.SessionConfiguration.DeveloperInstructions,
                    userInstructions = thread.SessionConfiguration.UserInstructions,
                    reasoningSummary = thread.SessionConfiguration.ReasoningSummary,
                    verbosity = thread.SessionConfiguration.Verbosity,
                    personality = thread.SessionConfiguration.Personality,
                    dynamicTools = thread.SessionConfiguration.DynamicTools,
                    collaborationMode = thread.SessionConfiguration.CollaborationMode,
                    persistExtendedHistory = thread.SessionConfiguration.PersistExtendedHistory,
                    sessionSource = thread.SessionConfiguration.SessionSource,
                    windowsSandboxLevel = thread.SessionConfiguration.WindowsSandboxLevel,
                    defaultModeRequestUserInputEnabled = thread.SessionConfiguration.DefaultModeRequestUserInputEnabled,
                },
            status = thread.Status is null
                ? null
                : new
                {
                    type = thread.Status.Type,
                    activeFlags = thread.Status.ActiveFlags,
                },
            gitInfo = thread.GitInfo is null
                ? null
                : new
                {
                    sha = thread.GitInfo.Sha,
                    branch = thread.GitInfo.Branch,
                    originUrl = thread.GitInfo.OriginUrl,
                },
            pendingInputState = thread.PendingInputState is null
                ? null
                : new
                {
                    interruptRequestPending = thread.PendingInputState.InterruptRequestPending,
                    submitPendingSteersAfterInterrupt = thread.PendingInputState.SubmitPendingSteersAfterInterrupt,
                    queuedUserMessages = thread.PendingInputState.QueuedUserMessages.Select(static entry => new
                    {
                        correlationId = entry.CorrelationId,
                        requestedMode = entry.RequestedMode,
                        effectiveMode = entry.EffectiveMode,
                        lifecycleState = entry.LifecycleState,
                        expectedTurnId = entry.ExpectedTurnId,
                        turnId = entry.TurnId,
                        pendingBucket = entry.PendingBucket,
                        compareKey = entry.CompareKey is null
                            ? null
                            : new
                            {
                                message = entry.CompareKey.Message,
                                imageCount = entry.CompareKey.ImageCount,
                            },
                    }).ToArray(),
                    pendingSteers = thread.PendingInputState.PendingSteers.Select(static entry => new
                    {
                        correlationId = entry.CorrelationId,
                        requestedMode = entry.RequestedMode,
                        effectiveMode = entry.EffectiveMode,
                        lifecycleState = entry.LifecycleState,
                        expectedTurnId = entry.ExpectedTurnId,
                        turnId = entry.TurnId,
                        pendingBucket = entry.PendingBucket,
                        compareKey = entry.CompareKey is null
                            ? null
                            : new
                            {
                                message = entry.CompareKey.Message,
                                imageCount = entry.CompareKey.ImageCount,
                            },
                    }).ToArray(),
                    entries = thread.PendingInputState.Entries.Select(static entry => new
                    {
                        correlationId = entry.CorrelationId,
                        requestedMode = entry.RequestedMode,
                        effectiveMode = entry.EffectiveMode,
                        lifecycleState = entry.LifecycleState,
                        expectedTurnId = entry.ExpectedTurnId,
                        turnId = entry.TurnId,
                        pendingBucket = entry.PendingBucket,
                        compareKey = entry.CompareKey is null
                            ? null
                            : new
                            {
                                message = entry.CompareKey.Message,
                                imageCount = entry.CompareKey.ImageCount,
                            },
                    }).ToArray(),
                    supplementalEntries = thread.PendingInputState.Entries.Select(static entry => new
                    {
                        correlationId = entry.CorrelationId,
                        requestedMode = entry.RequestedMode,
                        effectiveMode = entry.EffectiveMode,
                        lifecycleState = entry.LifecycleState,
                        expectedTurnId = entry.ExpectedTurnId,
                        turnId = entry.TurnId,
                        pendingBucket = entry.PendingBucket,
                        compareKey = entry.CompareKey is null
                            ? null
                            : new
                            {
                                message = entry.CompareKey.Message,
                                imageCount = entry.CompareKey.ImageCount,
                            },
                    }).ToArray(),
                },
            pendingInteractiveRequests = thread.PendingInteractiveRequests.Select(static request => new
            {
                requestId = string.IsNullOrWhiteSpace(request.RequestIdRaw) ? (object)request.RequestId : request.RequestIdRaw,
                requestKind = request.RequestKind,
                requestMethod = request.RequestMethod,
                callId = request.CallId,
                threadId = request.ThreadId,
                turnId = request.TurnId,
                toolName = request.ToolName,
                serverName = request.ServerName,
                text = request.Text,
                status = request.Status,
                phase = request.Phase,
                requiresApproval = request.RequiresApproval,
                approvalKind = request.ApprovalKind,
                availableDecisions = request.AvailableDecisions,
                availableDecisionOptions = request.AvailableDecisionOptions.Select(static option => new
                {
                    decision = option.Decision.ToProtocolValue(),
                    execPolicyAmendment = option.ExecPolicyAmendment is null
                        ? null
                        : new
                        {
                            commandPrefix = option.ExecPolicyAmendment.CommandPrefix,
                        },
                    networkPolicyAmendment = option.NetworkPolicyAmendment is null
                        ? null
                        : new
                        {
                            host = option.NetworkPolicyAmendment.Host,
                            action = option.NetworkPolicyAmendment.Action,
                        },
                }).ToArray(),
                approvalRequest = request.ApprovalRequest is null
                    ? null
                    : new
                    {
                        toolName = request.ApprovalRequest.ToolName,
                        approvalKind = request.ApprovalRequest.ApprovalKind,
                        availableDecisions = request.ApprovalRequest.AvailableDecisions,
                        availableDecisionOptions = request.ApprovalRequest.AvailableDecisionOptions?.Select(static option => new
                        {
                            decision = option.Decision.ToProtocolValue(),
                            execPolicyAmendment = option.ExecPolicyAmendment is null
                                ? null
                                : new
                                {
                                    commandPrefix = option.ExecPolicyAmendment.CommandPrefix,
                                },
                            networkPolicyAmendment = option.NetworkPolicyAmendment is null
                                ? null
                                : new
                                {
                                    host = option.NetworkPolicyAmendment.Host,
                                    action = option.NetworkPolicyAmendment.Action,
                                },
                        }).ToArray(),
                        summary = request.ApprovalRequest.Summary,
                        metadataFields = request.ApprovalRequest.MetadataFields.Select(static field => new
                        {
                            key = field.Key,
                            valueType = field.ValueType,
                            valueText = field.ValueText,
                        }).ToArray(),
                    },
                permissionRequest = request.PermissionRequest is null
                    ? null
                    : new
                    {
                        reason = request.PermissionRequest.Reason,
                        summary = request.PermissionRequest.Summary,
                        permissionsJson = request.PermissionRequest.PermissionsJson,
                        fields = request.PermissionRequest.Fields.Select(static field => new
                        {
                            key = field.Key,
                            valueType = field.ValueType,
                            valueText = field.ValueText,
                        }).ToArray(),
                    },
                userInputRequest = request.UserInputRequest is null
                    ? null
                    : new
                    {
                        summary = request.UserInputRequest.Summary,
                        questions = request.UserInputRequest.Questions.Select(static question => new
                        {
                            id = question.Id,
                            header = question.Header,
                            prompt = question.Prompt,
                            isSecret = question.IsSecret,
                            isOther = question.IsOther,
                            options = question.Options?.Select(static option => new
                            {
                                label = option.Label,
                                description = option.Description,
                            }).ToArray(),
                        }).ToArray(),
                    },
            }).ToArray(),
            turns = thread.Turns.Select(static turn => new
            {
                id = turn.Id,
                status = turn.Status,
                error = turn.Error is null
                    ? null
                    : new
                    {
                        message = turn.Error.Message,
                        additionalDetails = turn.Error.AdditionalDetails,
                    },
                items = turn.Items.Select(static item => new
                {
                    id = item.Id,
                    type = item.Type,
                    text = item.Text,
                    phase = item.Phase,
                }).ToArray(),
            }).ToArray(),
            seedHistory = thread.SeedHistory.Select(static item => new
            {
                role = item.Role,
                content = item.Content,
            }).ToArray(),
        });
    }

    private static string FormatConfigWriteResult(TianShuSidecarConfigWriteResult result)
    {
        var lines = new List<string>
        {
            result.Message,
            string.Empty,
            $"status: {result.Status}",
        };

        if (!string.IsNullOrWhiteSpace(result.Version))
        {
            lines.Add($"version: {result.Version}");
        }

        if (!string.IsNullOrWhiteSpace(result.FilePath))
        {
            lines.Add($"filePath: {result.FilePath}");
        }

        lines.Add($"isOverridden: {result.IsOverridden}");
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatThreadOperationResult(TianShuSidecarThreadOperationResult result)
        => result.Thread is null
            ? result.Message
            : FormatSettingsResultText(result.Message, BuildThreadDetailsJson(result.Thread));

    private static string? NormalizeJsonInput(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static object? ParseJsonValueOrString(string jsonOrText)
    {
        var normalized = (jsonOrText ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(normalized);
            return JsonSerializer.Deserialize<object>(document.RootElement.GetRawText());
        }
        catch (JsonException)
        {
            return jsonOrText;
        }
    }

    private static TianShuSidecarStructuredValue ParseConfigInputValue(string jsonOrText)
    {
        var normalized = (jsonOrText ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return TianShuSidecarStructuredValue.FromString(string.Empty);
        }

        try
        {
            using var document = JsonDocument.Parse(normalized);
            return TianShuSidecarStructuredValue.FromJsonElement(document.RootElement);
        }
        catch (JsonException)
        {
            return TianShuSidecarStructuredValue.FromString(jsonOrText ?? string.Empty);
        }
    }

    private static TianShuSidecarStructuredValue ConvertJsonElementToObject(JsonElement element)
        => TianShuSidecarStructuredValue.FromJsonElement(element);

    private Dictionary<string, TianShuSidecarStructuredValue> BuildUserInputAnswersPayload()
    {
        var result = new Dictionary<string, TianShuSidecarStructuredValue>(StringComparer.Ordinal);
        foreach (var question in pendingUserInputQuestions)
        {
            result[question.Id] = question.BuildStructuredAnswer();
        }

        return result;
    }

    private Dictionary<string, TianShuSidecarStructuredValue> BuildPermissionPayload()
    {
        var result = new Dictionary<string, TianShuSidecarStructuredValue>(StringComparer.Ordinal);
        foreach (var field in pendingPermissionFields)
        {
            result[field.Key] = field.GetTypedValue();
        }

        return result;
    }

    private IReadOnlyList<TianShuSidecarHistoryMessage>? BuildPendingHistoryMessages(IReadOnlyList<PendingContextEntry>? contextEntries = null)
    {
        var contextPayload = BuildPendingContextPayload(contextEntries ?? pendingContextEntries);
        if (string.IsNullOrWhiteSpace(contextPayload))
        {
            return null;
        }

        return new[]
        {
            new TianShuSidecarHistoryMessage
            {
                Role = "system",
                Content = contextPayload,
                Inputs = CreateTextUserInputs(contextPayload),
            },
        };
    }

    private string BuildFollowUpMessageWithPendingContext(string message, IReadOnlyList<PendingContextEntry>? contextEntries = null)
    {
        var contextPayload = BuildPendingContextPayload(contextEntries ?? pendingContextEntries);
        if (string.IsNullOrWhiteSpace(contextPayload))
        {
            return message;
        }

        return $"{contextPayload}{Environment.NewLine}{Environment.NewLine}[用户继续输入]{Environment.NewLine}{message}";
    }

    private string? BuildPendingContextPayload(IReadOnlyList<PendingContextEntry> contextEntries)
    {
        if (contextEntries.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.AppendLine("[IDE 附加上下文]");
        builder.AppendLine("以下内容来自当前 Visual Studio 编辑器，请将其作为本轮上下文。");

        for (var index = 0; index < contextEntries.Count; index++)
        {
            var entry = contextEntries[index];
            builder.AppendLine();
            builder.AppendLine($"[{index + 1}] {entry.Kind}");
            builder.AppendLine($"路径: {entry.FilePath}");
            builder.AppendLine("```text");
            builder.AppendLine(entry.Content);
            builder.AppendLine("```");
        }

        return builder.ToString().Trim();
    }

    private void ClearPendingContextEntries()
    {
        if (pendingContextEntries.Count == 0)
        {
            return;
        }

        pendingContextEntries.Clear();
        RefreshCommandState();
        OnPropertyChanged(nameof(PendingContextSummary));
    }

    private void ApplyThreadSession(TianShuSidecarThreadSession session)
    {
        CaptureCurrentThreadInputState();
        shouldStartNewThreadOnNextSend = false;
        assistantMessages.Clear();
        finalizedTurnIds.Clear();
        handledTurnErrorKeys.Clear();
        activeToolCallKeys.Clear();
        pendingFollowUpEntries.Clear();
        isDispatchingPendingFollowUp = false;
        Messages.Clear();
        ClearCurrentPlan();
        ClearPendingActionRequests();
        ThreadId = session.ThreadId;
        InputText = string.Empty;
        foreach (var message in BuildConversationMessages(session))
        {
            var entry = new ConversationEntry(MapConversationRole(message.Role), message.Content);
            Messages.Add(entry);
            RefreshConversationEntryPresentation(entry, forceReasoningRefresh: true, forceFinalRefresh: true);
        }

        RestoreThreadInputState(session.ThreadId, session.PendingInputState);
        ApplyPendingInputStateSnapshot(session.PendingInputState);
        ApplyPendingInteractiveRequestsSnapshot(session.PendingInteractiveRequests);
    }

    private static IReadOnlyList<TianShuSidecarConversationMessage> BuildConversationMessages(TianShuSidecarThreadSession session)
    {
        if (session.MessagesAreAuthoritative)
        {
            return session.Messages;
        }

        if (session.SeedHistory.Count == 0 && session.Turns.Count == 0)
        {
            return session.Messages;
        }

        var messages = new List<TianShuSidecarConversationMessage>();
        foreach (var item in session.SeedHistory)
        {
            if (TryBuildConversationMessage(item, out var message))
            {
                messages.Add(message);
            }
        }

        foreach (var turn in session.Turns)
        {
            messages.AddRange(BuildTurnConversationMessages(turn));
        }

        return messages;
    }

    private static IReadOnlyList<TianShuSidecarConversationMessage> BuildTurnConversationMessages(TianShuSidecarThreadTurn turn)
    {
        if (turn.Items.Count == 0)
        {
            return Array.Empty<TianShuSidecarConversationMessage>();
        }

        var messages = new List<TianShuSidecarConversationMessage>();
        foreach (var item in turn.Items)
        {
            if (TryBuildConversationMessage(item, out var message))
            {
                messages.Add(message);
            }
        }

        return NormalizeTurnConversationMessages(messages);
    }

    private static bool TryBuildConversationMessage(TianShuSidecarSeedHistoryItem item, out TianShuSidecarConversationMessage message)
        => TryBuildConversationMessage(
            item.Role,
            ExtractConversationText(item.Inputs) ?? item.Content,
            item.Inputs,
            out message);

    private static IReadOnlyList<TianShuSidecarConversationMessage> NormalizeTurnConversationMessages(List<TianShuSidecarConversationMessage> messages)
    {
        if (messages.Count < 2)
        {
            return messages;
        }

        var firstUserIndex = -1;
        var firstAssistantIndex = -1;
        for (var index = 0; index < messages.Count; index++)
        {
            var role = Normalize(messages[index].Role);
            if (string.Equals(role, "user", StringComparison.Ordinal))
            {
                if (firstUserIndex < 0)
                {
                    firstUserIndex = index;
                }
            }
            else if (string.Equals(role, "assistant", StringComparison.Ordinal))
            {
                if (firstAssistantIndex < 0)
                {
                    firstAssistantIndex = index;
                }
            }

            if (firstUserIndex >= 0 && firstAssistantIndex >= 0)
            {
                break;
            }
        }

        if (firstUserIndex < 0 || firstAssistantIndex < 0 || firstAssistantIndex > firstUserIndex)
        {
            return messages;
        }

        var ordered = new List<TianShuSidecarConversationMessage>(messages.Count);
        ordered.AddRange(messages.Where(static item => string.Equals(item.Role, "user", StringComparison.Ordinal)));
        ordered.AddRange(messages.Where(static item => string.Equals(item.Role, "assistant", StringComparison.Ordinal)));
        ordered.AddRange(messages.Where(static item => !string.Equals(item.Role, "user", StringComparison.Ordinal)
            && !string.Equals(item.Role, "assistant", StringComparison.Ordinal)));
        return ordered;
    }

    private static bool TryBuildConversationMessage(TianShuSidecarThreadTurnItem item, out TianShuSidecarConversationMessage message)
    {
        message = default!;

        var role = item.Type.IndexOf("user", StringComparison.OrdinalIgnoreCase) >= 0
            ? "user"
            : item.Type.IndexOf("assistant", StringComparison.OrdinalIgnoreCase) >= 0 || item.Type.IndexOf("agent", StringComparison.OrdinalIgnoreCase) >= 0
                ? "assistant"
                : null;
        if (role is null)
        {
            return false;
        }

        return TryBuildConversationMessage(role, ExtractConversationText(item.Inputs) ?? item.Text, item.Inputs, out message);
    }

    private static bool TryBuildConversationMessage(string? role, string? content, out TianShuSidecarConversationMessage message)
        => TryBuildConversationMessage(role, content, Array.Empty<TianShuSidecarUserInputPayload>(), out message);

    private static bool TryBuildConversationMessage(
        string? role,
        string? content,
        IReadOnlyList<TianShuSidecarUserInputPayload>? inputs,
        out TianShuSidecarConversationMessage message)
    {
        message = default!;

        var normalizedContent = Normalize(content);
        if (string.IsNullOrWhiteSpace(normalizedContent))
        {
            return false;
        }

        message = new TianShuSidecarConversationMessage
        {
            Role = Normalize(role) switch
            {
                "user" => "user",
                "assistant" => "assistant",
                "agent" => "assistant",
                _ => "system",
            },
            Content = normalizedContent!,
            Inputs = inputs ?? Array.Empty<TianShuSidecarUserInputPayload>(),
        };
        return true;
    }

    private static string? ExtractConversationText(IReadOnlyList<TianShuSidecarUserInputPayload>? inputs)
    {
        if (inputs is not { Count: > 0 })
        {
            return null;
        }

        var parts = inputs
            .Where(static item => string.Equals(item.Type, "text", StringComparison.OrdinalIgnoreCase))
            .Select(static item => Normalize(item.Text))
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToArray();
        return parts.Length == 0 ? null : string.Join(Environment.NewLine, parts);
    }

    private static string? BuildUserInputPreview(IReadOnlyList<TianShuSidecarUserInputPayload>? inputs)
    {
        if (inputs is not { Count: > 0 })
        {
            return null;
        }

        var parts = inputs
            .Select(BuildUserInputPreviewPart)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToArray();
        return parts.Length == 0 ? null : string.Join(Environment.NewLine, parts);
    }

    private static string? BuildUserInputPreviewPart(TianShuSidecarUserInputPayload item)
    {
        var text = Normalize(item.Text);
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var url = Normalize(item.Url);
        if (!string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        var path = Normalize(item.Path);
        if (!string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var name = Normalize(item.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        var type = Normalize(item.Type);
        return string.IsNullOrWhiteSpace(type) ? null : $"[{type}]";
    }

    private static IReadOnlyList<TianShuSidecarUserInputPayload> CreateTextUserInputs(string? text)
    {
        var normalizedText = Normalize(text);
        return string.IsNullOrWhiteSpace(normalizedText)
            ? Array.Empty<TianShuSidecarUserInputPayload>()
            : new[]
            {
                new TianShuSidecarUserInputPayload
                {
                    Type = "text",
                    Text = normalizedText,
                },
            };
    }

    private static IReadOnlyList<TianShuSidecarUserInputPayload> CloneUserInputs(IReadOnlyList<TianShuSidecarUserInputPayload>? inputs)
    {
        if (inputs is not { Count: > 0 })
        {
            return Array.Empty<TianShuSidecarUserInputPayload>();
        }

        return inputs
            .Select(static item => new TianShuSidecarUserInputPayload
            {
                Type = item.Type,
                Text = item.Text,
                Url = item.Url,
                Path = item.Path,
                Name = item.Name,
                TextElements = item.TextElements
                    .Select(static element => new TianShuSidecarTextElementPayload
                    {
                        Placeholder = element.Placeholder,
                        ByteRange = element.ByteRange is null
                            ? null
                            : new TianShuSidecarByteRangePayload
                            {
                                Start = element.ByteRange.Start,
                                End = element.ByteRange.End,
                            },
                    })
                    .ToArray(),
            })
            .ToArray();
    }

    private static bool AreEquivalentUserInputs(
        IReadOnlyList<TianShuSidecarUserInputPayload>? left,
        IReadOnlyList<TianShuSidecarUserInputPayload>? right)
    {
        var normalizedLeft = left ?? Array.Empty<TianShuSidecarUserInputPayload>();
        var normalizedRight = right ?? Array.Empty<TianShuSidecarUserInputPayload>();
        if (normalizedLeft.Count == 0 || normalizedRight.Count == 0 || normalizedLeft.Count != normalizedRight.Count)
        {
            return false;
        }

        for (var index = 0; index < normalizedLeft.Count; index++)
        {
            var leftItem = normalizedLeft[index];
            var rightItem = normalizedRight[index];
            if (!string.Equals(Normalize(leftItem.Type), Normalize(rightItem.Type), StringComparison.Ordinal)
                || !string.Equals(Normalize(leftItem.Text), Normalize(rightItem.Text), StringComparison.Ordinal)
                || !string.Equals(Normalize(leftItem.Url), Normalize(rightItem.Url), StringComparison.Ordinal)
                || !string.Equals(Normalize(leftItem.Path), Normalize(rightItem.Path), StringComparison.Ordinal)
                || !string.Equals(Normalize(leftItem.Name), Normalize(rightItem.Name), StringComparison.Ordinal)
                || !AreEquivalentTextElements(leftItem.TextElements, rightItem.TextElements))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreEquivalentTextElements(
        IReadOnlyList<TianShuSidecarTextElementPayload>? left,
        IReadOnlyList<TianShuSidecarTextElementPayload>? right)
    {
        var normalizedLeft = left ?? Array.Empty<TianShuSidecarTextElementPayload>();
        var normalizedRight = right ?? Array.Empty<TianShuSidecarTextElementPayload>();
        if (normalizedLeft.Count != normalizedRight.Count)
        {
            return false;
        }

        for (var index = 0; index < normalizedLeft.Count; index++)
        {
            var leftItem = normalizedLeft[index];
            var rightItem = normalizedRight[index];
            if (!string.Equals(Normalize(leftItem.Placeholder), Normalize(rightItem.Placeholder), StringComparison.Ordinal)
                || (leftItem.ByteRange?.Start ?? 0) != (rightItem.ByteRange?.Start ?? 0)
                || (leftItem.ByteRange?.End ?? 0) != (rightItem.ByteRange?.End ?? 0))
            {
                return false;
            }
        }

        return true;
    }

    private void CaptureCurrentThreadInputState()
    {
        var normalizedThreadId = Normalize(ThreadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            return;
        }

        var snapshot = CreateCurrentThreadInputStateSnapshot();
        if (snapshot is null)
        {
            threadInputStates.Remove(normalizedThreadId!);
            return;
        }

        threadInputStates[normalizedThreadId!] = snapshot;
    }

    private ThreadInputStateSnapshot? CreateCurrentThreadInputStateSnapshot()
    {
        var normalizedInput = Normalize(InputText) ?? string.Empty;
        var contextEntries = pendingContextEntries
            .Select(static item => new PendingContextEntryState(item.Kind, item.FilePath, item.Content))
            .ToArray();
        var followUps = pendingFollowUpEntries
            .Select(entry => new PendingFollowUpEntryState(
                entry.Message,
                entry.RequestedMode,
                entry.PendingBucket,
                entry.Inputs,
                entry.ContextEntries
                    .Select(static item => new PendingContextEntryState(item.Kind, item.FilePath, item.Content))
                    .ToArray(),
                entry.IsAwaitingCommit,
                entry.IsAwaitingTurnSteer,
                entry.CompareMessage,
                entry.CompareImageCount,
                entry.CorrelationId))
            .ToArray();

        if (string.IsNullOrWhiteSpace(normalizedInput)
            && contextEntries.Length == 0
            && followUps.Length == 0
            && busySendMode == BusySendMode.Queue
            && !submitAwaitingSteerAfterInterrupt)
        {
            return null;
        }

        return new ThreadInputStateSnapshot(
            normalizedInput,
            busySendMode,
            contextEntries,
            followUps,
            submitAwaitingSteerAfterInterrupt);
    }

    private void RestoreThreadInputState(string? threadId, TianShuSidecarPendingInputStatePayload? runtimePendingInputState)
    {
        pendingFollowUpEntries.Clear();
        pendingContextEntries.Clear();
        InputText = string.Empty;
        submitAwaitingSteerAfterInterrupt = false;

        var normalizedThreadId = Normalize(threadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId)
            || !threadInputStates.TryGetValue(normalizedThreadId!, out var snapshot))
        {
            SetBusySendMode(BusySendMode.Queue);
            return;
        }

        SetBusySendMode(snapshot.BusySendMode);
        foreach (var contextEntry in snapshot.PendingContextEntries)
        {
            pendingContextEntries.Add(CreatePendingContextEntry(contextEntry));
        }

        var hasAuthoritativePendingEntries = HasAuthoritativePendingInputSnapshot(runtimePendingInputState);
        foreach (var pendingFollowUp in snapshot.PendingFollowUpEntries
                     .Where(IsThreadSnapshotPendingSteerEntry))
        {
            RestoreThreadSnapshotPendingFollowUp(pendingFollowUp, hasAuthoritativePendingEntries, downgradeToQueue: true);
        }

        foreach (var pendingFollowUp in snapshot.PendingFollowUpEntries
                     .Where(static entry => !IsThreadSnapshotPendingSteerEntry(entry)))
        {
            RestoreThreadSnapshotPendingFollowUp(pendingFollowUp, hasAuthoritativePendingEntries, downgradeToQueue: false);
        }

        InputText = snapshot.InputText;
        submitAwaitingSteerAfterInterrupt = runtimePendingInputState?.SubmitPendingSteersAfterInterrupt == true
            || (runtimePendingInputState is null
                && snapshot.SubmitPendingSteersAfterInterrupt
                && snapshot.PendingFollowUpEntries.Any(IsThreadSnapshotPendingSteerEntry));
    }

    private void RestoreThreadSnapshotPendingFollowUp(
        PendingFollowUpEntryState pendingFollowUp,
        bool hasAuthoritativePendingEntries,
        bool downgradeToQueue)
    {
        if (hasAuthoritativePendingEntries && pendingFollowUp.IsAwaitingCommit)
        {
            return;
        }

        var restoredMode = downgradeToQueue ? BusySendMode.Queue : pendingFollowUp.RequestedMode;
        var restoredBucket = downgradeToQueue ? PendingFollowUpBucket.QueuedUserMessage : pendingFollowUp.PendingBucket;
        var restoredEntry = new PendingFollowUpEntry(
            pendingFollowUp.Message,
            restoredMode,
            pendingFollowUp.Inputs,
            pendingFollowUp.ContextEntries.Select(CreatePendingContextEntry).ToArray(),
            restoredBucket);
        // thread snapshot 只负责恢复本地 draft；若当前没有 runtime authoritative snapshot，
        // 则把旧的 awaiting_commit/turn-steer 状态降级回本地待发送，避免重复 owner。
        pendingFollowUpEntries.Add(restoredEntry);
    }

    private static PendingContextEntry CreatePendingContextEntry(PendingContextEntryState state)
        => state.Kind switch
        {
            "当前文件" => PendingContextEntry.CreateFile(state.FilePath, state.Content),
            "当前选区" => PendingContextEntry.CreateSelection(state.FilePath, state.Content),
            _ => PendingContextEntry.CreateSpecifiedFile(state.FilePath, state.Content),
        };

    private static bool IsThreadSnapshotPendingSteerEntry(PendingFollowUpEntryState state)
        => state.PendingBucket == PendingFollowUpBucket.PendingSteer;

    private string? ResolveThreadOperationTargetId()
        => Normalize(SelectedThread?.ThreadId) ?? Normalize(ThreadId);

    private string? GetDisplayedThreadOperationTargetId()
    {
        if (SelectedThread is not null && (HasConversationStarted || !SelectedThread.IsCurrentThread))
        {
            return Normalize(SelectedThread.ThreadId);
        }

        return HasConversationStarted ? Normalize(ThreadId) : null;
    }

    private bool TryGetRollbackTurns(out int turns)
    {
        return int.TryParse((RollbackTurnsText ?? string.Empty).Trim(), out turns) && turns > 0;
    }

    private void RecordSidecarEvent(TianShuSidecarEvent sidecarEvent)
    {
        RecordSidecarDiagnosticEvent(sidecarEvent);

        var eventType = (sidecarEvent.EventType ?? string.Empty).Trim().ToLowerInvariant();
        if (ShouldSkipSidecarEventEntry(eventType))
        {
            PersistSidecarEventLog();
            return;
        }

        var entry = new SidecarEventEntry(
            nextSidecarEventSequence++,
            DateTimeOffset.Now,
            sidecarEvent.EventType ?? string.Empty,
            sidecarEvent.Message ?? sidecarEvent.Text ?? string.Empty,
            BuildSidecarEventDetails(sidecarEvent));

        sidecarEvents.Add(entry);
        while (sidecarEvents.Count > 200)
        {
            sidecarEvents.RemoveAt(0);
        }

        SelectedSidecarEvent = entry;
        PersistSidecarEventLog();
    }

    private static bool ShouldSkipSidecarEventEntry(string eventType)
        => eventType is
            "assistant_text_delta"
            or "reasoning_delta"
            or "tool_call_output_delta"
            or "command_exec_output_delta";

    private void RecordSidecarDiagnosticEvent(TianShuSidecarEvent sidecarEvent)
    {
        sidecarDiagnosticEntries.Add(new PersistedSidecarDiagnosticEntry
        {
            Sequence = nextSidecarDiagnosticSequence++,
            Timestamp = DateTimeOffset.Now,
            EventType = sidecarEvent.EventType ?? string.Empty,
            ThreadId = sidecarEvent.ThreadId,
            TurnId = sidecarEvent.TurnId,
            CallId = sidecarEvent.CallId,
            ToolName = sidecarEvent.ToolName,
            ItemId = sidecarEvent.ItemId,
            State = sidecarEvent.State,
            Status = sidecarEvent.Status,
            Phase = sidecarEvent.Phase,
            SourceMethod = sidecarEvent.SourceMethod,
            Message = sidecarEvent.Message,
            Text = sidecarEvent.Text,
            ReasoningText = sidecarEvent.Reasoning?.Text,
            WillRetry = sidecarEvent.WillRetry,
            RequiresApproval = sidecarEvent.RequiresApproval,
            TurnErrorMessage = sidecarEvent.TurnError?.Message,
            TurnErrorAdditionalDetails = sidecarEvent.TurnError?.AdditionalDetails,
            DiagnosticJson = sidecarEvent.DiagnosticJson,
        });

        while (sidecarDiagnosticEntries.Count > MaxPersistedSidecarDiagnosticEntries)
        {
            sidecarDiagnosticEntries.RemoveAt(0);
        }
    }

    private void RecordConversationUiDiagnostic(TianShuSidecarEvent sidecarEvent, string normalizedEventType)
    {
        if (!ShouldCaptureConversationUiDiagnostic(normalizedEventType))
        {
            return;
        }

        var normalizedTurnId = NormalizeTurnKey(sidecarEvent.TurnId);
        assistantMessages.TryGetValue(normalizedTurnId, out var activeEntry);
        var latestAssistantEntry = Messages.LastOrDefault(static message => message.IsAssistantLike);

        var snapshot = new
        {
            sourceEventType = normalizedEventType,
            currentThreadId = ThreadId,
            currentMessageCount = Messages.Count,
            activeAssistantEntryCount = assistantMessages.Count,
            activeTurnIds = assistantMessages.Keys.Take(8).ToArray(),
            activeEntry = BuildConversationUiSnapshot(activeEntry),
            latestAssistantEntry = BuildConversationUiSnapshot(latestAssistantEntry),
        };

        sidecarDiagnosticEntries.Add(new PersistedSidecarDiagnosticEntry
        {
            Sequence = nextSidecarDiagnosticSequence++,
            Timestamp = DateTimeOffset.Now,
            EventType = "vsix_ui_state",
            ThreadId = sidecarEvent.ThreadId,
            TurnId = sidecarEvent.TurnId,
            CallId = sidecarEvent.CallId,
            ToolName = sidecarEvent.ToolName,
            ItemId = sidecarEvent.ItemId,
            Status = sidecarEvent.Status,
            Phase = sidecarEvent.Phase,
            SourceMethod = "vsix/ui",
            Message = normalizedEventType,
            DiagnosticJson = SerializePrettyJson(snapshot),
        });

        while (sidecarDiagnosticEntries.Count > MaxPersistedSidecarDiagnosticEntries)
        {
            sidecarDiagnosticEntries.RemoveAt(0);
        }
    }

    private static bool ShouldCaptureConversationUiDiagnostic(string normalizedEventType)
        => normalizedEventType is
            "tool_call_started"
            or "tool_call_completed"
            or "assistant_text_completed"
            or "turn_completed"
            or "runtime_state"
            or "error";

    private static object? BuildConversationUiSnapshot(ConversationEntry? entry)
    {
        if (entry is null)
        {
            return null;
        }

        return new
        {
            role = entry.Role,
            variant = entry.Variant,
            content = TruncateText(entry.Content, 400),
            pendingDraftContent = TruncateText(entry.PendingDraftContent, 400),
            pendingStreamingContent = TruncateText(entry.PendingStreamingContent, 400),
            assistantPendingText = entry.AssistantPendingText,
            showPendingDraftFallback = entry.ShowPendingDraftFallback,
            showInlineReasoningSection = entry.ShowInlineReasoningSection,
            showCollapsibleReasoningSection = entry.ShowCollapsibleReasoningSection,
            showAssistantFinalContent = entry.ShowAssistantFinalContent,
            processSegments = entry.ProcessSegments.Select(static segment => new
            {
                text = TruncateText(segment.Text, 400),
                hasToolCalls = segment.HasToolCalls,
                toolCallCount = segment.ToolCalls.Count,
                toolCalls = segment.ToolCalls.Select(static toolCall => new
                {
                    displayName = toolCall.DisplayName,
                    statusText = toolCall.StatusText,
                    outputText = TruncateText(toolCall.OutputText, 400),
                }).ToArray(),
            }).ToArray(),
        };
    }

    private string BuildSidecarEventDetails(TianShuSidecarEvent sidecarEvent)
    {
        var details = new
        {
            eventType = sidecarEvent.EventType,
            state = sidecarEvent.State,
            threadId = sidecarEvent.ThreadId,
            turnId = sidecarEvent.TurnId,
            callId = sidecarEvent.CallId,
            toolName = sidecarEvent.ToolName,
            itemId = sidecarEvent.ItemId,
            status = sidecarEvent.Status,
            phase = sidecarEvent.Phase,
            sourceMethod = sidecarEvent.SourceMethod,
            taskType = sidecarEvent.TaskType,
            operationName = sidecarEvent.OperationName,
            serverName = sidecarEvent.ServerName,
            requiresApproval = sidecarEvent.RequiresApproval,
            willRetry = sidecarEvent.WillRetry,
            message = sidecarEvent.Message,
            text = sidecarEvent.Text,
            plan = sidecarEvent.Plan,
            toolCall = sidecarEvent.ToolCall,
            approvalRequest = sidecarEvent.ApprovalRequest,
            permissionRequest = sidecarEvent.PermissionRequest,
            userInputRequest = sidecarEvent.UserInputRequest,
            serverRequestResolved = sidecarEvent.ServerRequestResolved,
            task = sidecarEvent.Task,
            operation = sidecarEvent.Operation,
            reasoning = sidecarEvent.Reasoning,
            mcpServerStatus = sidecarEvent.McpServerStatus,
            item = sidecarEvent.Item,
            committedUserMessage = sidecarEvent.CommittedUserMessage,
            pendingFollowUp = sidecarEvent.PendingFollowUp,
            pendingInputState = sidecarEvent.PendingInputState,
            turnError = sidecarEvent.TurnError,
            agentJobProgress = sidecarEvent.AgentJobProgress,
            deprecationNotice = sidecarEvent.DeprecationNotice,
            configWarning = sidecarEvent.ConfigWarning,
            threadStatusChanged = sidecarEvent.ThreadStatusChanged,
            threadNameUpdated = sidecarEvent.ThreadNameUpdated,
            threadTokenUsage = sidecarEvent.ThreadTokenUsage,
            commandExecOutputDelta = sidecarEvent.CommandExecOutputDelta,
            appListUpdated = sidecarEvent.AppListUpdated,
            windowsSandboxSetup = sidecarEvent.WindowsSandboxSetup,
            mcpServerOauthLogin = sidecarEvent.McpServerOauthLogin,
            realtimeSession = sidecarEvent.RealtimeSession,
            fuzzyFileSearchSession = sidecarEvent.FuzzyFileSearchSession,
            threadRealtimeItemAdded = sidecarEvent.ThreadRealtimeItemAdded,
            threadRealtimeOutputAudioDelta = sidecarEvent.ThreadRealtimeOutputAudioDelta,
            threadRealtimeError = sidecarEvent.ThreadRealtimeError,
            threadRealtimeClosed = sidecarEvent.ThreadRealtimeClosed,
        };

        var detailsJson = JsonSerializer.Serialize(details, prettyJsonOptions);
        var diagnosticJson = BuildSidecarEventDiagnosticJson(sidecarEvent);
        if (string.IsNullOrWhiteSpace(diagnosticJson))
        {
            return detailsJson;
        }

        return $"{detailsJson}{Environment.NewLine}{Environment.NewLine}诊断原文：{Environment.NewLine}{diagnosticJson}";
    }

    private static string? BuildSidecarEventDiagnosticJson(TianShuSidecarEvent sidecarEvent)
    {
        var diagnosticJson = sidecarEvent.DiagnosticJson;
        if (string.IsNullOrWhiteSpace(diagnosticJson))
        {
            return null;
        }

        if (!ShouldIncludeSidecarEventDiagnosticFallback(sidecarEvent))
        {
            return null;
        }

        return TryFormatDiagnosticJson(diagnosticJson!);
    }

    private static string TryFormatDiagnosticJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return SerializePrettyJson(JsonSerializer.Deserialize<object>(document.RootElement.GetRawText())!);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static bool ShouldIncludeSidecarEventDiagnosticFallback(TianShuSidecarEvent sidecarEvent)
        => string.IsNullOrWhiteSpace(sidecarEvent.State)
            && string.IsNullOrWhiteSpace(sidecarEvent.ThreadId)
            && string.IsNullOrWhiteSpace(sidecarEvent.TurnId)
            && string.IsNullOrWhiteSpace(sidecarEvent.CallId)
            && string.IsNullOrWhiteSpace(sidecarEvent.ToolName)
            && string.IsNullOrWhiteSpace(sidecarEvent.ItemId)
            && string.IsNullOrWhiteSpace(sidecarEvent.Status)
            && string.IsNullOrWhiteSpace(sidecarEvent.Phase)
            && string.IsNullOrWhiteSpace(sidecarEvent.SourceMethod)
            && string.IsNullOrWhiteSpace(sidecarEvent.TaskType)
            && string.IsNullOrWhiteSpace(sidecarEvent.OperationName)
            && string.IsNullOrWhiteSpace(sidecarEvent.ServerName)
            && sidecarEvent.RequiresApproval is null
            && sidecarEvent.WillRetry is null
            && string.IsNullOrWhiteSpace(sidecarEvent.Message)
            && string.IsNullOrWhiteSpace(sidecarEvent.Text)
            && sidecarEvent.Plan is null
            && sidecarEvent.ToolCall is null
            && sidecarEvent.ApprovalRequest is null
            && sidecarEvent.PermissionRequest is null
            && sidecarEvent.UserInputRequest is null
            && sidecarEvent.ServerRequestResolved is null
            && sidecarEvent.Task is null
            && sidecarEvent.Operation is null
            && sidecarEvent.Reasoning is null
            && sidecarEvent.McpServerStatus is null
            && sidecarEvent.Item is null
            && sidecarEvent.CommittedUserMessage is null
            && sidecarEvent.PendingFollowUp is null
            && sidecarEvent.PendingInputState is null
            && sidecarEvent.AgentJobProgress is null
            && sidecarEvent.DeprecationNotice is null
            && sidecarEvent.ConfigWarning is null
            && sidecarEvent.ThreadStatusChanged is null
            && sidecarEvent.ThreadNameUpdated is null
            && sidecarEvent.ThreadTokenUsage is null
            && sidecarEvent.CommandExecOutputDelta is null
            && sidecarEvent.AppListUpdated is null
            && sidecarEvent.WindowsSandboxSetup is null
            && sidecarEvent.McpServerOauthLogin is null
            && sidecarEvent.RealtimeSession is null
            && sidecarEvent.FuzzyFileSearchSession is null
            && sidecarEvent.ThreadRealtimeItemAdded is null
            && sidecarEvent.ThreadRealtimeOutputAudioDelta is null
            && sidecarEvent.ThreadRealtimeError is null
            && sidecarEvent.ThreadRealtimeClosed is null;

    private ThreadListEntry? FindThreadEntry(string? candidateThreadId)
    {
        if (string.IsNullOrWhiteSpace(candidateThreadId))
        {
            return null;
        }

        foreach (var thread in recentThreads)
        {
            if (string.Equals(thread.ThreadId, candidateThreadId, StringComparison.Ordinal))
            {
                return thread;
            }
        }

        return null;
    }

    private bool TrySelectThreadEntryFromSender(object sender, out ThreadListEntry entry)
    {
        if (sender is FrameworkElement { DataContext: ThreadListEntry rawEntry })
        {
            entry = FindThreadEntry(rawEntry.ThreadId) ?? rawEntry;
            SelectedThread = entry;
            return true;
        }

        entry = null!;
        return false;
    }

    private void SelectCurrentThreadFromRecentList()
    {
        SelectedThread = FindThreadEntry(ThreadId);
    }

    private void SyncSelectedThreadDraft()
    {
        ThreadRenameText = SelectedThread is null ? string.Empty : GetThreadEditableTitle(SelectedThread);
    }

    private void RefreshCollaborationThreadsState()
    {
        var entries = BuildCollaborationThreadEntries(recentThreads.ToArray(), ThreadId);
        collaborationThreads.Clear();
        foreach (var entry in entries)
        {
            collaborationThreads.Add(entry);
        }

        NotifyCollaborationThreadsStateChanged();
    }

    internal static IReadOnlyList<CollaborationThreadEntry> BuildCollaborationThreadEntries(
        IReadOnlyList<ThreadListEntry> threads,
        string? currentThreadId)
    {
        if (threads.Count == 0 || string.IsNullOrWhiteSpace(currentThreadId))
        {
            return Array.Empty<CollaborationThreadEntry>();
        }

        var threadsById = threads
            .GroupBy(static thread => thread.ThreadId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

        if (!threadsById.ContainsKey(currentThreadId))
        {
            return Array.Empty<CollaborationThreadEntry>();
        }

        var rootThreadId = ResolveCollaborationRootThreadId(currentThreadId, threadsById);
        var collaborationChildren = threads
            .Where(thread => thread.IsSpawnedSubAgentThread
                             && !string.Equals(thread.ThreadId, rootThreadId, StringComparison.Ordinal)
                             && BelongsToCollaborationTree(thread, rootThreadId, threadsById))
            .OrderBy(static thread => thread.ThreadDepth ?? int.MaxValue)
            .ThenBy(static thread => thread.CreatedAt ?? thread.UpdatedAt)
            .ThenBy(static thread => thread.ThreadId, StringComparer.Ordinal)
            .ToArray();

        if (collaborationChildren.Length == 0)
        {
            return Array.Empty<CollaborationThreadEntry>();
        }

        var entries = new List<CollaborationThreadEntry>(collaborationChildren.Length + 1);
        if (threadsById.TryGetValue(rootThreadId, out var rootThread))
        {
            entries.Add(CreateCollaborationThreadEntry(rootThread, currentThreadId, isPrimaryThread: true));
        }

        foreach (var thread in collaborationChildren)
        {
            entries.Add(CreateCollaborationThreadEntry(thread, currentThreadId, isPrimaryThread: false));
        }

        return entries;
    }

    private static string ResolveCollaborationRootThreadId(
        string currentThreadId,
        IReadOnlyDictionary<string, ThreadListEntry> threadsById)
    {
        var rootThreadId = currentThreadId;
        var visited = new HashSet<string>(StringComparer.Ordinal) { currentThreadId };
        while (threadsById.TryGetValue(rootThreadId, out var current)
               && !string.IsNullOrWhiteSpace(current.ParentThreadId)
               && threadsById.ContainsKey(current.ParentThreadId)
               && visited.Add(current.ParentThreadId))
        {
            rootThreadId = current.ParentThreadId;
        }

        return rootThreadId;
    }

    private static bool BelongsToCollaborationTree(
        ThreadListEntry candidate,
        string rootThreadId,
        IReadOnlyDictionary<string, ThreadListEntry> threadsById)
    {
        var cursor = candidate;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (!string.IsNullOrWhiteSpace(cursor.ParentThreadId) && visited.Add(cursor.ThreadId))
        {
            if (string.Equals(cursor.ParentThreadId, rootThreadId, StringComparison.Ordinal))
            {
                return true;
            }

            if (!threadsById.TryGetValue(cursor.ParentThreadId, out cursor!))
            {
                return false;
            }
        }

        return false;
    }

    private static CollaborationThreadEntry CreateCollaborationThreadEntry(
        ThreadListEntry thread,
        string currentThreadId,
        bool isPrimaryThread)
    {
        var isCurrentThread = string.Equals(thread.ThreadId, currentThreadId, StringComparison.Ordinal);
        var titleText = BuildCollaborationThreadTitle(thread, isPrimaryThread);
        return new CollaborationThreadEntry(
            thread.ThreadId,
            titleText,
            BuildCollaborationThreadMetaText(thread, titleText, isPrimaryThread),
            BuildCollaborationThreadStatusText(thread, isPrimaryThread, isCurrentThread),
            isPrimaryThread,
            isCurrentThread,
            thread.IsArchived);
    }

    private static string BuildCollaborationThreadTitle(ThreadListEntry thread, bool isPrimaryThread)
    {
        if (isPrimaryThread)
        {
            return "主线程 [default]";
        }

        var nickname = Normalize(thread.AgentNickname);
        var role = Normalize(thread.AgentRole);
        return (nickname, role) switch
        {
            ({ Length: > 0 } value, { Length: > 0 } roleText) => $"{value} [{roleText}]",
            ({ Length: > 0 } value, _) => value,
            (_, { Length: > 0 } roleText) => $"[{roleText}]",
            _ => "子代理",
        };
    }

    private static string BuildCollaborationThreadMetaText(ThreadListEntry thread, string titleText, bool isPrimaryThread)
    {
        var descriptors = new List<string>(4)
        {
            isPrimaryThread
                ? "主线程"
                : thread.ThreadDepth is > 0
                    ? $"第 {thread.ThreadDepth.Value} 层"
                    : "子代理",
        };

        var preview = GetThreadEditableTitle(thread);
        if (!string.IsNullOrWhiteSpace(preview)
            && !LooksLikeGeneratedThreadId(preview)
            && !string.Equals(preview, titleText, StringComparison.Ordinal))
        {
            descriptors.Add(preview);
        }

        descriptors.Add(thread.UpdatedAt.ToString("MM-dd HH:mm"));
        return string.Join(" · ", descriptors);
    }

    private static string BuildCollaborationThreadStatusText(ThreadListEntry thread, bool isPrimaryThread, bool isCurrentThread)
    {
        if (isCurrentThread)
        {
            return "当前";
        }

        if (thread.IsArchived)
        {
            return "归档";
        }

        if (isPrimaryThread)
        {
            return "主线程";
        }

        return Normalize(thread.AgentRole) ?? "子代理";
    }

    private static string BuildThreadDisplayName(ThreadListEntry? thread)
    {
        if (thread is null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(thread.Name))
        {
            return thread.Name.Trim();
        }

        return string.IsNullOrWhiteSpace(thread.Preview)
            ? thread.ThreadId
            : thread.Preview.Trim();
    }

    private static string GetThreadEditableTitle(ThreadListEntry? thread)
    {
        if (thread is null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(thread.Name))
        {
            return thread.Name.Trim();
        }

        return (thread.Preview ?? string.Empty).Trim();
    }

    private static string MapConversationRole(string? role)
    {
        return (role ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "assistant" => "TianShu",
            "user" => "你",
            _ => "系统",
        };
    }

    private async Task DisposeBridgeAsync()
    {
        if (sidecarBridge is null)
        {
            return;
        }

        sidecarBridge.EventReceived -= OnSidecarEventReceived;
        await sidecarBridge.DisposeAsync().ConfigureAwait(true);
        sidecarBridge = null;
        isInitialized = false;
        RefreshCommandState();
    }

    private void OnSidecarEventReceived(object? sender, TianShuSidecarEvent e)
    {
        if (sender is not TianShuSidecarBridge bridge
            || !ReferenceEquals(bridge, sidecarBridge))
        {
            return;
        }

        _ = Dispatcher.InvokeAsync(() =>
        {
            if (sidecarBridge is not TianShuSidecarBridge currentBridge
                || !ReferenceEquals(currentBridge, bridge)
                || e.BridgeGeneration != currentBridge.RuntimeGeneration)
            {
                return;
            }

            ApplySidecarEvent(e);
        });
    }

    private void ApplySidecarEvent(TianShuSidecarEvent sidecarEvent)
    {
        RecordSidecarEvent(sidecarEvent);

        var normalizedEventType = (sidecarEvent.EventType ?? string.Empty).Trim().ToLowerInvariant();
        var incomingThreadId = Normalize(sidecarEvent.ThreadId);
        var currentThreadId = Normalize(ThreadId);
        if (!string.IsNullOrWhiteSpace(incomingThreadId))
        {
            if (string.IsNullOrWhiteSpace(currentThreadId))
            {
                ThreadId = incomingThreadId;
            }
            else if (!string.Equals(currentThreadId, incomingThreadId, StringComparison.Ordinal))
            {
                if (normalizedEventType is "thread_status_changed" or "thread_name_updated" or "thread_deleted")
                {
                    _ = RefreshRecentThreadsAsync(selectCurrentThread: true);
                }

                return;
            }
        }

        if (ShouldIgnoreFinalizedTurnEvent(sidecarEvent))
        {
            return;
        }

        TryStartSteeredConversationPhaseFromContinuation(sidecarEvent, normalizedEventType);

        switch (normalizedEventType)
        {
            case "runtime_state":
                ApplyRuntimeState(sidecarEvent);
                break;
            case "pending_followup_updated":
                ApplyPendingFollowUpUpdated(sidecarEvent);
                break;
            case "turn_steered":
                StartSteeredConversationPhase(sidecarEvent);
                SetBusy(true, BuildStatusText(sidecarEvent.Message, "已接收引导输入，正在继续当前回合。"), keepInitialized: true);
                break;
            case "user_message_committed":
                ApplyCommittedUserMessage(sidecarEvent);
                break;
            case "assistant_text_delta":
                AppendAssistantDelta(sidecarEvent.TurnId, sidecarEvent.Text);
                UpdateConversationProgressStatus(sidecarEvent, "正在整理最终总结…", createIfMissing: false);
                break;
            case "assistant_text_completed":
                CompleteAssistantMessage(sidecarEvent.TurnId, sidecarEvent.Text ?? sidecarEvent.Message);
                break;
            case "turn_completed":
                MarkTurnFinalized(sidecarEvent.TurnId);
                CompleteAssistantMessage(
                    sidecarEvent.TurnId,
                    null,
                    Normalize(sidecarEvent.Status),
                    sidecarEvent.Message);
                ClearCurrentPlan();
                ClearPendingInteractiveRequestsForTurn(sidecarEvent.TurnId);
                ClearConversationProgressStatus(sidecarEvent.TurnId);
                ResetFollowUpState(preserveAwaitingTurnSteer: true);
                SetBusy(false, BuildStatusText(sidecarEvent.Message, "当前回合已完成。"), keepInitialized: true);
                _ = RefreshRecentThreadsAsync(selectCurrentThread: true);
                _ = TryDispatchPendingFollowUpsAsync();
                break;
            case "approval_requested":
                ResetFollowUpState();
                CaptureApprovalRequest(sidecarEvent);
                UpdateConversationProgressStatus(sidecarEvent, "等待审批…", createIfMissing: true);
                SetBusy(false, BuildStatusText(sidecarEvent.Message, "等待审批。"), keepInitialized: true);
                break;
            case "permission_requested":
                ResetFollowUpState();
                CapturePermissionRequest(sidecarEvent);
                UpdateConversationProgressStatus(sidecarEvent, "等待权限确认…", createIfMissing: true);
                SetBusy(false, BuildStatusText(sidecarEvent.Message, "等待权限确认。"), keepInitialized: true);
                break;
            case "request_user_input":
                ResetFollowUpState();
                CaptureUserInputRequest(sidecarEvent);
                UpdateConversationProgressStatus(sidecarEvent, "等待补录输入…", createIfMissing: true);
                SetBusy(false, BuildStatusText(sidecarEvent.Message, "等待补录输入。"), keepInitialized: true);
                break;
            case "server_request_resolved":
                ApplyServerRequestResolved(sidecarEvent);
                _ = TryDispatchPendingFollowUpsAsync();
                break;
            case "tool_call_started":
            case "tool_call_output_delta":
            case "tool_call_completed":
                ApplyToolCallEvent(sidecarEvent);
                _ = TryDispatchPendingFollowUpsAsync();
                break;
            case "plan_updated":
                ApplyPlanUpdated(sidecarEvent);
                break;
            case "diff_updated":
                ApplyDiffUpdated(sidecarEvent);
                break;
            case "operation_reported":
                ApplyOperationReported(sidecarEvent);
                break;
            case "reasoning_delta":
                ApplyReasoningDelta(sidecarEvent);
                break;
            case "task_started":
            case "task_completed":
                ApplyTaskEvent(sidecarEvent);
                break;
            case "mcp_server_status_updated":
                ApplyMcpServerStatusUpdated(sidecarEvent);
                break;
            case "thread_status_changed":
            case "thread_name_updated":
                _ = RefreshRecentThreadsAsync(selectCurrentThread: true);
                break;
            case "thread_deleted":
                ApplyThreadDeleted(sidecarEvent);
                break;
            case "thread_compacted":
            case "skills_changed":
            case "command_exec_output_delta":
            case "app_list_updated":
            case "windows_sandbox_setup":
            case "mcp_server_oauth_login":
            case "realtime_session":
            case "fuzzy_file_search_session":
            case "deprecation_notice":
            case "config_warning":
            case "thread_token_usage_updated":
            case "thread_realtime_item_added":
            case "thread_realtime_output_audio_delta":
            case "thread_realtime_error":
            case "thread_realtime_closed":
                break;
            case "error":
                var errorMessage = Normalize(sidecarEvent.Message)
                    ?? Normalize(sidecarEvent.Text)
                    ?? Normalize(sidecarEvent.TurnError?.Message)
                    ?? "运行时返回错误。";
                var willRetry = sidecarEvent.WillRetry ?? false;
                if (willRetry)
                {
                    UpdateConversationProgressStatus(
                        sidecarEvent,
                        errorMessage,
                        createIfMissing: false);
                    SetBusy(true, BuildStatusText(errorMessage, "Reconnecting..."), keepInitialized: isInitialized);
                    break;
                }

                ResetFollowUpState();
                UpdateConversationProgressStatus(sidecarEvent, "运行失败。", createIfMissing: false);
                if (TryRegisterTurnError(sidecarEvent, errorMessage))
                {
                    MarkTurnFinalized(sidecarEvent.TurnId);
                    ClearPendingInteractiveRequestsForTurn(sidecarEvent.TurnId);
                    var finalizedAssistantEntries = FinalizePendingAssistantEntries("failed", errorMessage);
                    if (!finalizedAssistantEntries)
                    {
                        AppendSystemMessage(errorMessage, addBlankLineBefore: true);
                    }
                }

                SetBusy(false, BuildStatusText(errorMessage, "运行时返回错误。"), keepInitialized: isInitialized);
                _ = TryDispatchPendingFollowUpsAsync();
                break;
        }

        RecordConversationUiDiagnostic(sidecarEvent, normalizedEventType);
        PersistSidecarEventLog();
    }

    private void ApplyThreadDeleted(TianShuSidecarEvent sidecarEvent)
    {
        var deletedThreadId = Normalize(sidecarEvent.ThreadId);
        var currentThreadId = Normalize(ThreadId);
        var selectedThreadId = Normalize(SelectedThread?.ThreadId);

        if (!string.IsNullOrWhiteSpace(deletedThreadId)
            && string.Equals(currentThreadId, deletedThreadId, StringComparison.Ordinal))
        {
            ThreadId = null;
            shouldStartNewThreadOnNextSend = true;
            SetInterruptRequestPending(false);
            submitAwaitingSteerAfterInterrupt = false;
            ResetFollowUpState();
            SetBusy(false, BuildStatusText(sidecarEvent.Message, "当前线程已失效，等待继续。"), keepInitialized: true);
        }

        if (!string.IsNullOrWhiteSpace(deletedThreadId)
            && string.Equals(selectedThreadId, deletedThreadId, StringComparison.Ordinal))
        {
            SelectedThread = null;
        }

        _ = RefreshRecentThreadsAsync(selectCurrentThread: true);
    }

    private void ApplyCommittedUserMessage(TianShuSidecarEvent sidecarEvent)
    {
        if (!HasAwaitingCommittedPendingFollowUp())
        {
            return;
        }

        var committedText = Normalize(sidecarEvent.CommittedUserMessage?.Text)
            ?? Normalize(sidecarEvent.Item?.Text)
            ?? Normalize(sidecarEvent.Text);
        if (string.IsNullOrWhiteSpace(committedText))
        {
            return;
        }

        var committedInputs = sidecarEvent.CommittedUserMessage?.Inputs
            ?? sidecarEvent.Item?.Inputs
            ?? CreateTextUserInputs(committedText);
        var correlationId = Normalize(sidecarEvent.CommittedUserMessage?.CorrelationId);
        var committedImageCount = sidecarEvent.CommittedUserMessage?.ImageCount
            ?? sidecarEvent.Item?.ImageCount
            ?? 0;
        var pendingFollowUp = SelectAwaitingCommittedPendingFollowUp(correlationId, committedText, committedImageCount, committedInputs);
        if (pendingFollowUp is null)
        {
            return;
        }

        if (!pendingFollowUp.IsAwaitingTurnSteer)
        {
            var committedPendingFollowUp = ConsumeAwaitingCommittedPendingFollowUp(pendingFollowUp);
            if (committedPendingFollowUp is null)
            {
                return;
            }

            AppendMessage("你", committedPendingFollowUp.Message);
            SetBusy(true, BuildStatusText(sidecarEvent.Message, "已接收跟进输入，正在处理当前回合。"), keepInitialized: true);
            FocusInput();
            return;
        }

        StartSteeredConversationPhase(sidecarEvent, pendingFollowUp);
        SetBusy(true, BuildStatusText(sidecarEvent.Message, "已接收引导输入，正在继续当前回合。"), keepInitialized: true);
    }

    private void ApplyPendingFollowUpUpdated(TianShuSidecarEvent sidecarEvent)
    {
        var pendingInputState = sidecarEvent.PendingInputState;
        var pendingFollowUp = sidecarEvent.PendingFollowUp;
        if (pendingFollowUp is null)
        {
            ApplyPendingInputStateSnapshot(pendingInputState);
            return;
        }

        var lifecycleState = Normalize(pendingFollowUp.LifecycleState)?.ToLowerInvariant();
        var correlationId = Normalize(pendingFollowUp.CorrelationId);
        if (string.Equals(lifecycleState, "awaiting_commit", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(correlationId))
        {
            var entry = FindPendingFollowUpByCorrelation(correlationId);
            if (entry is not null)
            {
                var isTurnSteer = IsSteerPendingFollowUp(pendingFollowUp);
                entry.MarkAsAwaitingCommit(
                    pendingFollowUp.CompareKey?.Message,
                    pendingFollowUp.CompareKey?.ImageCount ?? 0,
                    correlationId,
                    isTurnSteer,
                    entry.Inputs);
                if (!isTurnSteer)
                {
                    var committedPendingFollowUp = ConsumeAwaitingCommittedPendingFollowUp(entry);
                    if (committedPendingFollowUp is not null)
                    {
                        AppendMessage("你", committedPendingFollowUp.Message);
                        SetBusy(true, BuildStatusText(sidecarEvent.Message, "已接收跟进输入，正在处理当前回合。"), keepInitialized: true);
                        FocusInput();
                    }
                }
            }
        }

        if (IsInterruptPendingFollowUp(pendingFollowUp))
        {
            switch (lifecycleState)
            {
                case "interrupt_requested":
                    SetInterruptRequestPending(true);
                    break;
                case "interrupt_completed":
                case "interrupt_failed":
                case "interrupt_timeout":
                    SetInterruptRequestPending(false);
                    break;
            }
        }

        ApplyPendingInputStateSnapshot(pendingInputState);
    }

    private void ApplyPendingInputStateSnapshot(TianShuSidecarPendingInputStatePayload? pendingInputState)
    {
        if (pendingInputState is null)
        {
            return;
        }

        ApplyPendingInputEntriesSnapshot(ResolveAuthoritativePendingInputEntriesSnapshot(pendingInputState));
        SetInterruptRequestPending(pendingInputState.InterruptRequestPending);
        submitAwaitingSteerAfterInterrupt = pendingInputState.SubmitPendingSteersAfterInterrupt;
    }

    private void ApplyPendingInteractiveRequestsSnapshot(
        IReadOnlyList<TianShuSidecarPendingInteractiveRequestReplayPayload>? pendingInteractiveRequests)
    {
        if (pendingInteractiveRequests is not { Count: > 0 })
        {
            RefreshPendingInteractiveRequestState();
            return;
        }

        foreach (var pendingInteractiveRequest in pendingInteractiveRequests)
        {
            if (!TryBuildPendingInteractiveRequestReplayEvent(pendingInteractiveRequest, out var replayEvent))
            {
                continue;
            }

            UpsertPendingInteractiveRequest(replayEvent, pendingInteractiveRequest?.RequestId ?? 0);
        }
    }

    private static bool TryBuildPendingInteractiveRequestReplayEvent(
        TianShuSidecarPendingInteractiveRequestReplayPayload? pendingInteractiveRequest,
        out TianShuSidecarEvent replayEvent)
    {
        replayEvent = default!;

        var eventType = Normalize(pendingInteractiveRequest?.RequestKind);
        var callId = Normalize(pendingInteractiveRequest?.CallId);
        if (string.IsNullOrWhiteSpace(eventType) || string.IsNullOrWhiteSpace(callId))
        {
            return false;
        }

        replayEvent = new TianShuSidecarEvent
        {
            EventType = eventType!,
            CallId = callId,
            ThreadId = Normalize(pendingInteractiveRequest?.ThreadId),
            TurnId = Normalize(pendingInteractiveRequest?.TurnId),
            ToolName = Normalize(pendingInteractiveRequest?.ToolName),
            ServerName = Normalize(pendingInteractiveRequest?.ServerName),
            Message = Normalize(pendingInteractiveRequest?.Text),
            Text = Normalize(pendingInteractiveRequest?.Text),
            Status = Normalize(pendingInteractiveRequest?.Status),
            Phase = Normalize(pendingInteractiveRequest?.Phase),
            RequiresApproval = pendingInteractiveRequest?.RequiresApproval,
            ApprovalRequest = pendingInteractiveRequest?.ApprovalRequest,
            PermissionRequest = pendingInteractiveRequest?.PermissionRequest,
            UserInputRequest = pendingInteractiveRequest?.UserInputRequest,
        };
        return true;
    }

    private static bool HasAuthoritativePendingInputSnapshot(TianShuSidecarPendingInputStatePayload? pendingInputState)
        => pendingInputState is not null;

    private static IReadOnlyList<TianShuSidecarPendingInputStateEntryPayload> ResolveAuthoritativePendingInputEntriesSnapshot(
        TianShuSidecarPendingInputStatePayload? pendingInputState)
    {
        if (pendingInputState is null)
        {
            return Array.Empty<TianShuSidecarPendingInputStateEntryPayload>();
        }

        if (pendingInputState.Entries.Count == 0
            && pendingInputState.QueuedUserMessages.Count == 0
            && pendingInputState.PendingSteers.Count == 0)
        {
            return Array.Empty<TianShuSidecarPendingInputStateEntryPayload>();
        }

        var mergedEntries = new List<TianShuSidecarPendingInputStateEntryPayload>();
        if (pendingInputState.Entries.Count > 0)
        {
            mergedEntries.AddRange(pendingInputState.Entries);
        }

        if (pendingInputState.QueuedUserMessages.Count > 0)
        {
            mergedEntries.AddRange(pendingInputState.QueuedUserMessages);
        }

        if (pendingInputState.PendingSteers.Count > 0)
        {
            mergedEntries.AddRange(pendingInputState.PendingSteers);
        }

        return mergedEntries;
    }

    private void ApplyPendingInputEntriesSnapshot(IReadOnlyList<TianShuSidecarPendingInputStateEntryPayload>? pendingInputEntriesSnapshot)
    {
        var normalizedSnapshotEntries = new List<(string CorrelationId, TianShuSidecarPendingInputStateEntryPayload Entry)>();
        var entriesByCorrelation = new Dictionary<string, TianShuSidecarPendingInputStateEntryPayload>(StringComparer.Ordinal);
        if (pendingInputEntriesSnapshot is not null)
        {
            foreach (var entry in pendingInputEntriesSnapshot)
            {
                var correlationId = Normalize(entry.CorrelationId);
                if (string.IsNullOrWhiteSpace(correlationId))
                {
                    continue;
                }

                normalizedSnapshotEntries.Add((correlationId!, entry));
                entriesByCorrelation[correlationId!] = entry;
            }
        }

        foreach (var pendingFollowUp in pendingFollowUpEntries
                     .Where(static entry => !string.IsNullOrWhiteSpace(entry.CorrelationId))
                     .ToArray())
        {
            var correlationId = Normalize(pendingFollowUp.CorrelationId);
            if (string.IsNullOrWhiteSpace(correlationId))
            {
                continue;
            }

            if (!entriesByCorrelation.TryGetValue(correlationId!, out var snapshotEntry))
            {
                if (pendingFollowUp.IsAwaitingTurnSteer)
                {
                    pendingFollowUp.MarkAsQueue();
                }

                pendingFollowUp.ClearAwaitingCommit();
                continue;
            }

            pendingFollowUp.MarkAsAwaitingCommit(
                snapshotEntry.CompareKey?.Message,
                snapshotEntry.CompareKey?.ImageCount ?? 0,
                correlationId,
                IsSteerPendingInputStateEntry(snapshotEntry),
                snapshotEntry.Inputs);
        }

        foreach (var snapshotEntry in normalizedSnapshotEntries)
        {
            if (FindPendingFollowUpByCorrelation(snapshotEntry.CorrelationId) is not null)
            {
                continue;
            }

            var message = BuildUserInputPreview(snapshotEntry.Entry.Inputs)
                ?? Normalize(snapshotEntry.Entry.CompareKey?.Message);
            if (string.IsNullOrWhiteSpace(message))
            {
                continue;
            }

            var inputs = snapshotEntry.Entry.Inputs is { Count: > 0 }
                ? snapshotEntry.Entry.Inputs
                : CreateTextUserInputs(message);

            var pendingFollowUp = new PendingFollowUpEntry(
                message!,
                MapPendingInputStateEntryMode(snapshotEntry.Entry),
                inputs,
                Array.Empty<PendingContextEntry>(),
                MapPendingInputStateEntryBucket(snapshotEntry.Entry));
            pendingFollowUp.MarkAsAwaitingCommit(
                snapshotEntry.Entry.CompareKey?.Message ?? message,
                snapshotEntry.Entry.CompareKey?.ImageCount ?? 0,
                snapshotEntry.CorrelationId,
                IsSteerPendingInputStateEntry(snapshotEntry.Entry),
                inputs);
            pendingFollowUpEntries.Add(pendingFollowUp);
        }
    }

    private void ApplyRuntimeState(TianShuSidecarEvent sidecarEvent)
    {
        if (sidecarEvent.PendingInputState is not null)
        {
            ApplyPendingInputStateSnapshot(sidecarEvent.PendingInputState);
        }

        var state = (sidecarEvent.State ?? string.Empty).Trim().ToLowerInvariant();
        var message = sidecarEvent.Message ?? string.Empty;

        switch (state)
        {
            case "starting":
            case "waiting_for_initialize":
                SetBusy(true, BuildStatusText(message, "正在启动运行时，请稍候。"), keepInitialized: false);
                break;
            case "initialized":
                isInitialized = true;
                submitAwaitingSteerAfterInterrupt = false;
                ResetFollowUpState();
                SetBusy(false, BuildStatusText(message, "运行时已初始化。"), keepInitialized: true);
                break;
            case "busy":
                UpdateConversationProgressStatus(sidecarEvent, "正在处理当前回合…", createIfMissing: false);
                SetBusy(true, BuildStatusText(message, "正在处理当前回合。"), keepInitialized: true);
                break;
            case "interrupting":
            case "interrupt_requested":
                SetInterruptRequestPending(true);
                UpdateConversationProgressStatus(sidecarEvent, "正在中断当前回合…", createIfMissing: false);
                SetBusy(true, BuildStatusText(message, "已发送中断请求。"), keepInitialized: true);
                break;
            case "interrupted":
                HandleInterruptedRuntimeState(sidecarEvent, message);
                break;
            case "idle":
                if (string.Equals(Normalize(sidecarEvent.Status), "interrupted", StringComparison.OrdinalIgnoreCase))
                {
                    HandleInterruptedRuntimeState(sidecarEvent, message);
                    break;
                }

                submitAwaitingSteerAfterInterrupt = false;
                ResetFollowUpState(preserveAwaitingTurnSteer: true);
                ClearConversationProgressStatus(ResolveEventTurnId(sidecarEvent));
                SetBusy(false, BuildStatusText(message, "当前回合已完成。"), keepInitialized: true);
                _ = TryDispatchPendingFollowUpsAsync();
                break;
            case "waiting_approval":
            case "waiting_user_input":
                submitAwaitingSteerAfterInterrupt = false;
                ResetFollowUpState();
                UpdateConversationProgressStatus(sidecarEvent, message, createIfMissing: false);
                SetBusy(false, BuildStatusText(message, message), keepInitialized: true);
                break;
            case "stopped":
            case "stopping":
                isInitialized = false;
                submitAwaitingSteerAfterInterrupt = false;
                ResetFollowUpState();
                ClearPendingActionRequests();
                ClearConversationProgressStatus(ResolveEventTurnId(sidecarEvent));
                activeToolCallKeys.Clear();
                SetBusy(false, BuildStatusText(message, "运行时已停止。"), keepInitialized: false);
                break;
            case "waiting_permission":
            default:
                if (!string.IsNullOrWhiteSpace(message))
                {
                    UpdateConversationProgressStatus(sidecarEvent, message, createIfMissing: false);
                    StatusText = BuildStatusText(message, message);
                }
                break;
        }
    }

    private void HandleInterruptedRuntimeState(TianShuSidecarEvent sidecarEvent, string message)
    {
        var shouldResubmitAwaitingSteer = submitAwaitingSteerAfterInterrupt;
        submitAwaitingSteerAfterInterrupt = false;
        MarkTurnFinalized(sidecarEvent.TurnId);
        ClearPendingInteractiveRequestsForTurn(sidecarEvent.TurnId);
        if (shouldResubmitAwaitingSteer)
        {
            MergeAwaitingTurnSteerPendingFollowUpsToQueuedRedispatch();
        }

        ResetFollowUpState(preserveAwaitingTurnSteer: !shouldResubmitAwaitingSteer);
        FinalizePendingAssistantEntries("interrupted", sidecarEvent.Message);
        SetBusy(false, BuildStatusText(message, "当前回合已中断，可继续发送。"), keepInitialized: true);
        _ = TryDispatchPendingFollowUpsAsync();
    }

    private void ForceInterruptCurrentTurnLocally(string message)
    {
        submitAwaitingSteerAfterInterrupt = false;
        threadInputStates.Clear();
        ClearPendingActionRequests();
        FinalizePendingAssistantEntries("interrupted", message);
        activeToolCallKeys.Clear();
        SetInterruptRequestPending(false);
    }

    private void ApplyToolCallEvent(TianShuSidecarEvent sidecarEvent)
    {
        TrackToolCallState(sidecarEvent);

        var toolCall = sidecarEvent.ToolCall;
        var key = BuildToolActivityKey(sidecarEvent);
        if (!toolActivityEntries.TryGetValue(key, out var entry))
        {
            entry = new ToolActivityEntry(key);
            toolActivityEntries[key] = entry;
        }

        entry.ToolName = Normalize(toolCall?.ToolName)
            ?? Normalize(sidecarEvent.ToolName)
            ?? "tool";
        entry.TurnId = ResolveEventTurnId(sidecarEvent) ?? entry.TurnId;
        entry.Status = GetToolActivityStatus(sidecarEvent);
        entry.Summary = Normalize(sidecarEvent.SourceMethod)
            ?? Normalize(sidecarEvent.Message)
            ?? entry.Summary;
        var outputDelta = Normalize(toolCall?.OutputText) ?? Normalize(sidecarEvent.Text);
        if (!string.IsNullOrWhiteSpace(outputDelta))
        {
            entry.AppendOutput(outputDelta!);
        }

        entry.UpdatedAt = DateTimeOffset.Now;
        ToolActivityText = BuildToolActivityText();

        var conversationEntry = GetOrCreateAssistantEntry(sidecarEvent.TurnId);
        conversationEntry.FlushPendingDraftToProcessSegment(MaxAssistantReasoningChars);
        conversationEntry.UpsertProcessToolCall(
            key,
            entry.ToolName,
            entry.Status,
            outputDelta,
            Normalize(sidecarEvent.SourceMethod) ?? Normalize(sidecarEvent.Message));
        conversationEntry.SetPendingStatusText(BuildToolProgressStatusText(entry.ToolName, entry.Status));
        RefreshConversationEntryPresentation(conversationEntry, forceReasoningRefresh: false, forceFinalRefresh: false);
        SetBusy(true, BuildStatusText(conversationEntry.AssistantPendingText, "正在处理当前回合。"), keepInitialized: true);
        ScrollMessagesToEnd();
    }

    private void TrackToolCallState(TianShuSidecarEvent sidecarEvent)
    {
        var key = BuildToolActivityKey(sidecarEvent);
        var status = GetToolActivityStatus(sidecarEvent).ToLowerInvariant();
        if (IsTerminalToolStatus(status))
        {
            activeToolCallKeys.Remove(key);
            return;
        }

        activeToolCallKeys.Add(key);
    }

    private static bool IsTerminalToolStatus(string status)
        => status is "completed" or "finished" or "failed" or "errored" or "cancelled" or "interrupted";

    private bool ShouldIgnoreFinalizedTurnEvent(TianShuSidecarEvent sidecarEvent)
    {
        var turnId = ResolveEventTurnId(sidecarEvent);
        if (string.IsNullOrWhiteSpace(turnId))
        {
            return false;
        }

        if (!finalizedTurnIds.Contains(NormalizeTurnKey(turnId)))
        {
            return false;
        }

        return ((sidecarEvent.EventType ?? string.Empty).Trim().ToLowerInvariant()) switch
        {
            "assistant_text_delta" => true,
            "assistant_text_completed" => true,
            "turn_started" => true,
            "turn_steered" => true,
            "user_message_committed" => true,
            "approval_requested" => true,
            "permission_requested" => true,
            "request_user_input" => true,
            "tool_call_started" => true,
            "tool_call_output_delta" => true,
            "tool_call_completed" => true,
            "plan_updated" => true,
            "diff_updated" => true,
            "operation_reported" => true,
            "reasoning_delta" => true,
            "task_started" => true,
            "task_completed" => true,
            "error" => true,
            _ => false,
        };
    }

    private void MarkTurnFinalized(string? turnId)
    {
        var normalizedTurnId = Normalize(turnId);
        if (string.IsNullOrWhiteSpace(normalizedTurnId))
        {
            return;
        }

        finalizedTurnIds.Add(NormalizeTurnKey(normalizedTurnId));
        activeToolCallKeys.Clear();
    }

    private void ApplyTaskEvent(TianShuSidecarEvent sidecarEvent)
    {
        var task = sidecarEvent.Task;
        var key = BuildTaskActivityKey(sidecarEvent);
        if (!taskActivityEntries.TryGetValue(key, out var entry))
        {
            entry = new TaskActivityEntry(key);
            taskActivityEntries[key] = entry;
        }

        entry.TaskType = Normalize(task?.TaskType)
            ?? Normalize(sidecarEvent.TaskType)
            ?? entry.TaskType;
        entry.Status = Normalize(task?.Status)
            ?? Normalize(sidecarEvent.Status)
            ?? ((sidecarEvent.EventType ?? string.Empty).EndsWith("completed", StringComparison.OrdinalIgnoreCase) ? "completed" : "started");
        entry.Message = Normalize(sidecarEvent.Message)
            ?? Normalize(sidecarEvent.SourceMethod)
            ?? entry.Message;
        entry.UpdatedAt = DateTimeOffset.Now;
        TaskActivityText = BuildTaskActivityText();
        UpdateConversationProgressStatus(sidecarEvent, BuildTaskProgressStatusText(sidecarEvent), createIfMissing: true);
    }

    private void ApplyPlanUpdated(TianShuSidecarEvent sidecarEvent)
    {
        if (TryParsePlanUpdated(sidecarEvent, out var explanation, out var planSteps))
        {
            CurrentPlanExplanation = explanation ?? string.Empty;
            currentPlanSteps.Clear();
            foreach (var planStep in planSteps)
            {
                currentPlanSteps.Add(planStep);
            }

            LatestPlanText = BuildPlanOverviewText(CurrentPlanExplanation, planSteps);
            IsPlanPanelExpanded = true;
            SuppressDuplicatePlanNarration(sidecarEvent.TurnId, planSteps.Count);
            return;
        }

        CurrentPlanExplanation = Normalize(sidecarEvent.Text) ?? string.Empty;
        if (currentPlanSteps.Count > 0)
        {
            currentPlanSteps.Clear();
        }

        LatestPlanText = ExtractPreferredEventText(sidecarEvent);
        IsPlanPanelExpanded = true;
    }

    private void SuppressDuplicatePlanNarration(string? turnId, int planStepCount)
    {
        if (planStepCount <= 0)
        {
            return;
        }

        if (!assistantMessages.TryGetValue(NormalizeTurnKey(turnId), out var entry)
            && !assistantMessages.TryGetValue(NormalizeTurnKey(null), out entry))
        {
            return;
        }

        if (!entry.SuppressTrailingPlanNarration(planStepCount))
        {
            return;
        }

        RefreshConversationEntryPresentation(entry, forceReasoningRefresh: true, forceFinalRefresh: false);
    }

    private void ApplyDiffUpdated(TianShuSidecarEvent sidecarEvent)
    {
        LatestDiffText = ExtractPreferredEventText(sidecarEvent);
    }

    private void ApplyOperationReported(TianShuSidecarEvent sidecarEvent)
    {
        var statusText = BuildOperationProgressStatusText(sidecarEvent);
        if (string.IsNullOrWhiteSpace(statusText))
        {
            return;
        }

        UpdateConversationProgressStatus(sidecarEvent, statusText, createIfMissing: true);
        SetBusy(true, BuildStatusText(statusText, "正在处理当前回合。"), keepInitialized: true);
    }

    private void ApplyReasoningDelta(TianShuSidecarEvent sidecarEvent)
    {
        var sourceMethod = Normalize(sidecarEvent.SourceMethod)
            ?? Normalize(sidecarEvent.Reasoning?.SourceMethod);
        if (string.Equals(sourceMethod, "item/agentmessage/delta", StringComparison.OrdinalIgnoreCase))
        {
            ApplyCommentaryDelta(sidecarEvent);
            return;
        }

        if (sourceMethod is not null
            && sourceMethod.StartsWith("item/reasoning/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var delta = Normalize(sidecarEvent.Reasoning?.Text)
            ?? Normalize(sidecarEvent.Text)
            ?? Normalize(sidecarEvent.Message);
        if (string.IsNullOrWhiteSpace(delta))
        {
            return;
        }

        var text = MergeStreamingText(
            reasoningBuffer.ToString(),
            delta,
            Environment.NewLine);
        reasoningBuffer.Clear();
        reasoningBuffer.Append(text);
        ReasoningText = text.Length <= 8000 ? text : text.Substring(text.Length - 8000, 8000);

        var conversationEntry = GetOrCreateAssistantEntry(sidecarEvent.TurnId);
        conversationEntry.FlushPendingDraftToProcessSegment(MaxAssistantReasoningChars);
        conversationEntry.AppendProcessDelta(delta!, MaxAssistantReasoningChars);
        conversationEntry.SetPendingStatusText("正在思考…");
        RefreshConversationEntryPresentation(conversationEntry, forceReasoningRefresh: false, forceFinalRefresh: false);
        ScrollMessagesToEnd();
    }

    private void ApplyCommentaryDelta(TianShuSidecarEvent sidecarEvent)
    {
        var delta = Normalize(sidecarEvent.Reasoning?.Text)
            ?? Normalize(sidecarEvent.Text)
            ?? Normalize(sidecarEvent.Message);
        if (string.IsNullOrWhiteSpace(delta))
        {
            return;
        }

        var conversationEntry = GetOrCreateAssistantEntry(sidecarEvent.TurnId);
        conversationEntry.AppendCommentaryDelta(delta!, MaxAssistantReasoningChars);
        conversationEntry.SetPendingStatusText("处理中…");
        RefreshConversationEntryPresentation(conversationEntry, forceReasoningRefresh: false, forceFinalRefresh: false);
        SetBusy(true, BuildStatusText(conversationEntry.AssistantPendingText, "正在处理当前回合。"), keepInitialized: true);
        ScrollMessagesToEnd();
    }

    private static string MergeStreamingText(
        string? current,
        string? incoming,
        string? separator = null)
    {
        var existingText = current ?? string.Empty;
        var incomingText = incoming ?? string.Empty;
        if (string.IsNullOrEmpty(incomingText))
        {
            return existingText;
        }

        if (string.IsNullOrEmpty(existingText))
        {
            return incomingText;
        }

        if (string.Equals(existingText, incomingText, StringComparison.Ordinal))
        {
            return existingText;
        }

        if (incomingText.StartsWith(existingText, StringComparison.Ordinal))
        {
            return incomingText;
        }

        if (existingText.StartsWith(incomingText, StringComparison.Ordinal))
        {
            return existingText;
        }

        var overlapLength = FindStreamingOverlapLength(existingText, incomingText);
        if (overlapLength >= 2)
        {
            return existingText + incomingText.Substring(overlapLength);
        }

        return string.IsNullOrEmpty(separator)
            ? existingText + incomingText
            : existingText + separator + incomingText;
    }

    private static int FindStreamingOverlapLength(string current, string incoming)
    {
        var maxOverlap = Math.Min(current.Length, incoming.Length);
        for (var length = maxOverlap; length >= 1; length--)
        {
            if (string.CompareOrdinal(current, current.Length - length, incoming, 0, length) == 0)
            {
                return length;
            }
        }

        return 0;
    }

    private void StartSteeredConversationPhase(TianShuSidecarEvent sidecarEvent, PendingFollowUpEntry? pendingFollowUp = null)
    {
        StartSteeredConversationPhase(ResolveEventTurnId(sidecarEvent), pendingFollowUp, fallbackToLatestAssistantEntry: false);
    }

    private void TryStartSteeredConversationPhaseFromContinuation(TianShuSidecarEvent sidecarEvent, string normalizedEventType)
    {
        if (!HasAwaitingTurnSteerPendingFollowUp())
        {
            return;
        }

        var incomingTurnId = ResolveEventTurnId(sidecarEvent);
        if (string.IsNullOrWhiteSpace(incomingTurnId)
            || !IsImplicitSteerBoundaryEvent(sidecarEvent, normalizedEventType))
        {
            return;
        }

        var latestActiveTurnId = GetLatestActiveAssistantTurnId();
        if (!string.IsNullOrWhiteSpace(latestActiveTurnId)
            && string.Equals(latestActiveTurnId, incomingTurnId, StringComparison.Ordinal))
        {
            if (!IsSameTurnSteerContinuationEvent(sidecarEvent, normalizedEventType))
            {
                return;
            }

            StartSteeredConversationPhase(latestActiveTurnId, pendingFollowUp: null, fallbackToLatestAssistantEntry: true);
            return;
        }

        StartSteeredConversationPhase(latestActiveTurnId, pendingFollowUp: null, fallbackToLatestAssistantEntry: true);
    }

    private void StartSteeredConversationPhase(string? turnId, PendingFollowUpEntry? pendingFollowUp, bool fallbackToLatestAssistantEntry)
    {
        var previousEntry = DetachAssistantEntry(turnId);
        if (previousEntry is null && fallbackToLatestAssistantEntry)
        {
            previousEntry = DetachLatestAssistantEntry();
        }

        if (previousEntry is not null)
        {
            FinalizeAssistantEntryForSteerBoundary(previousEntry);
        }

        var steerMessage = Normalize(
            (pendingFollowUp is null
                ? ConsumeAwaitingTurnSteerPendingFollowUp()
                : ConsumeAwaitingTurnSteerPendingFollowUp(pendingFollowUp))
            ?.Message);
        if (!string.IsNullOrWhiteSpace(steerMessage))
        {
            AppendMessage("你", steerMessage!);
        }
    }

    private static bool IsImplicitSteerBoundaryEvent(TianShuSidecarEvent sidecarEvent, string normalizedEventType)
    {
        return normalizedEventType switch
        {
            "turn_started" => true,
            "info" => IsTurnStartedInfoEvent(sidecarEvent),
            "task_started" => true,
            "plan_updated" => true,
            "operation_reported" => true,
            "reasoning_delta" => true,
            "tool_call_started" => true,
            "tool_call_output_delta" => true,
            "tool_call_completed" => true,
            "assistant_text_delta" => true,
            "assistant_text_completed" => true,
            "approval_requested" => true,
            "permission_requested" => true,
            "request_user_input" => true,
            "turn_completed" => true,
            _ => false,
        };
    }

    private static bool IsSameTurnSteerContinuationEvent(TianShuSidecarEvent sidecarEvent, string normalizedEventType)
    {
        return normalizedEventType switch
        {
            "turn_started" => true,
            "info" => IsTurnStartedInfoEvent(sidecarEvent),
            "task_started" => true,
            "plan_updated" => true,
            "operation_reported" => true,
            "reasoning_delta" => true,
            "tool_call_started" => true,
            "tool_call_output_delta" => true,
            "tool_call_completed" => true,
            "assistant_text_delta" => true,
            "assistant_text_completed" => true,
            "approval_requested" => true,
            "permission_requested" => true,
            "request_user_input" => true,
            _ => false,
        };
    }

    private static bool IsTurnStartedInfoEvent(TianShuSidecarEvent sidecarEvent)
    {
        var message = Normalize(sidecarEvent.Message);
        return !string.IsNullOrWhiteSpace(message)
            && (message.StartsWith("turn/start", StringComparison.OrdinalIgnoreCase)
                || string.Equals(message, "turn/started", StringComparison.OrdinalIgnoreCase));
    }

    private bool TryParsePlanUpdated(
        TianShuSidecarEvent sidecarEvent,
        out string? explanation,
        out IReadOnlyList<ConversationPlanStepEntry> planSteps)
    {
        if (TryParsePlanPayload(sidecarEvent.Plan, out explanation, out planSteps))
        {
            return true;
        }

        explanation = null;
        planSteps = Array.Empty<ConversationPlanStepEntry>();
        return false;
    }

    private static bool TryParsePlanPayload(
        TianShuSidecarPlanPayload? payload,
        out string? explanation,
        out IReadOnlyList<ConversationPlanStepEntry> planSteps)
    {
        explanation = Normalize(payload?.Explanation);
        if (payload?.Steps is not { Length: > 0 } steps)
        {
            planSteps = Array.Empty<ConversationPlanStepEntry>();
            return !string.IsNullOrWhiteSpace(explanation);
        }

        var entries = new List<ConversationPlanStepEntry>(steps.Length);
        var fallbackSequence = 0;
        foreach (var step in steps)
        {
            var stepText = Normalize(step.Step);
            if (string.IsNullOrWhiteSpace(stepText))
            {
                continue;
            }

            fallbackSequence++;
            var sequence = step.Sequence > 0 ? step.Sequence : fallbackSequence;
            entries.Add(new ConversationPlanStepEntry(sequence, stepText!, NormalizePlanStatus(step.Status)));
        }

        planSteps = entries;
        return entries.Count > 0 || !string.IsNullOrWhiteSpace(explanation);
    }

    private static string NormalizePlanStatus(string? status)
    {
        var normalized = Normalize(status)?.ToLowerInvariant();
        return normalized switch
        {
            "inprogress" => "in_progress",
            "in_progress" => "in_progress",
            "doing" => "in_progress",
            "done" => "completed",
            "completed" => "completed",
            "pending" => "pending",
            "todo" => "pending",
            _ => normalized ?? "pending",
        };
    }

    private static string BuildPlanOverviewText(string? explanation, IReadOnlyList<ConversationPlanStepEntry> planSteps)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(explanation))
        {
            builder.AppendLine(explanation!.Trim());
        }

        foreach (var step in planSteps)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(step.StatusGlyph)
                .Append(' ')
                .Append(step.DisplayText)
                .Append(" (")
                .Append(step.StatusText)
                .Append(')');
        }

        return builder.ToString().Trim();
    }

    private static bool TryTrimTrailingPlanNarration(string text, int planStepCount, out string trimmed)
    {
        trimmed = text;
        var normalized = Normalize(text);
        if (string.IsNullOrWhiteSpace(normalized) || planStepCount <= 0)
        {
            return false;
        }

        var lines = ReplaceOrdinal(normalized!, "\r\n", "\n")
            .Split('\n');
        var lastNonEmptyIndex = Array.FindLastIndex(lines, static line => !string.IsNullOrWhiteSpace(line));
        if (lastNonEmptyIndex < 0 || !IsNumberedPlanLine(lines[lastNonEmptyIndex]))
        {
            return false;
        }

        var numberedCount = 0;
        var scanIndex = lastNonEmptyIndex;
        while (scanIndex >= 0)
        {
            var currentLine = lines[scanIndex];
            if (IsNumberedPlanLine(currentLine))
            {
                numberedCount++;
                scanIndex--;
                continue;
            }

            if (string.IsNullOrWhiteSpace(currentLine))
            {
                scanIndex--;
                continue;
            }

            break;
        }

        if (numberedCount < Math.Min(2, planStepCount))
        {
            return false;
        }

        var removeFromLine = scanIndex + 1;
        if (scanIndex >= 0 && LooksLikePlanIntroLine(lines[scanIndex]))
        {
            removeFromLine = scanIndex;
        }

        var remainingLines = lines.Take(removeFromLine).ToArray();
        var result = string.Join(Environment.NewLine, remainingLines).TrimEnd();
        if (string.Equals(result, normalized, StringComparison.Ordinal))
        {
            return false;
        }

        trimmed = result;
        return true;
    }

    private static bool IsNumberedPlanLine(string? line)
    {
        var normalized = Normalize(line);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var index = 0;
        while (index < normalized.Length && char.IsDigit(normalized[index]))
        {
            index++;
        }

        if (index == 0 || index >= normalized.Length)
        {
            return false;
        }

        return normalized[index] switch
        {
            '.' or '、' or ')' or '）' => true,
            _ => false,
        };
    }

    private static bool LooksLikePlanIntroLine(string? line)
    {
        var normalized = Normalize(line);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return normalized.IndexOf("接下来", StringComparison.Ordinal) >= 0
               || normalized.IndexOf("计划", StringComparison.Ordinal) >= 0
               || normalized.IndexOf("分步", StringComparison.Ordinal) >= 0
               || normalized.IndexOf("分三步", StringComparison.Ordinal) >= 0
               || normalized.IndexOf("下一步", StringComparison.Ordinal) >= 0
               || normalized.IndexOf("我会", StringComparison.Ordinal) >= 0
               || normalized.IndexOf("next", StringComparison.OrdinalIgnoreCase) >= 0
               || normalized.IndexOf("plan", StringComparison.OrdinalIgnoreCase) >= 0
               || normalized.IndexOf("steps", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string LocalizePlanStep(string step)
    {
        var normalized = Normalize(step) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized) || ContainsCjk(normalized))
        {
            return normalized.TrimEnd('.', '。');
        }

        var localized = normalized.Trim().TrimEnd('.', '。');
        localized = ReplaceInvariant(localized, "Create the .NET 10 WPF project", "创建 .NET 10 WPF 项目");
        localized = ReplaceInvariant(localized, "embedded TianShu config schema resource", "内嵌 TianShu 配置 schema 资源");
        localized = ReplaceInvariant(localized, "schema-driven editor UI", "schema 驱动的编辑器界面");
        localized = ReplaceInvariant(localized, "TOML preview", "TOML 预览");
        localized = ReplaceInvariant(localized, "save-to-Test/config.toml workflow", "保存到 Test/config.toml 的流程");
        localized = ReplaceInvariant(localized, "Build the project", "构建项目");
        localized = ReplaceInvariant(localized, "fix runtime issues", "修复运行时问题");
        localized = ReplaceInvariant(localized, "launch the executable", "启动可执行程序");
        localized = ReplaceInvariant(localized, "Create ", "创建 ");
        localized = ReplaceInvariant(localized, "Set up ", "搭建 ");
        localized = ReplaceInvariant(localized, "Implement ", "实现 ");
        localized = ReplaceInvariant(localized, "Build ", "构建 ");
        localized = ReplaceInvariant(localized, "Add ", "添加 ");
        localized = ReplaceInvariant(localized, "Update ", "更新 ");
        localized = ReplaceInvariant(localized, "Refactor ", "重构 ");
        localized = ReplaceInvariant(localized, "Fix ", "修复 ");
        localized = ReplaceInvariant(localized, "Verify ", "验证 ");
        localized = ReplaceInvariant(localized, "Run ", "运行 ");
        localized = ReplaceInvariant(localized, "Launch ", "启动 ");
        localized = ReplaceInvariant(localized, "under Test", "到 Test 目录下");
        localized = ReplaceInvariant(localized, " and ", "，并 ");
        localized = ReplaceInvariant(localized, ", and ", "，并 ");
        localized = ReplaceInvariant(localized, " workflow", " 流程");
        localized = ReplaceInvariant(localized, " project", " 项目");
        localized = ReplaceInvariant(localized, " resource", " 资源");
        localized = ReplaceInvariant(localized, " resources", " 资源");
        localized = ReplaceInvariant(localized, " executable", " 可执行程序");
        localized = ReplaceInvariant(localized, " runtime issues", " 运行时问题");
        localized = ReplaceInvariant(localized, " editor UI", " 编辑器界面");
        return localized;
    }

    private static string ReplaceInvariant(string text, string oldValue, string newValue)
        => ReplaceWithComparison(text, oldValue, newValue, StringComparison.OrdinalIgnoreCase);

    private static string ReplaceOrdinal(string text, string oldValue, string newValue)
        => ReplaceWithComparison(text, oldValue, newValue, StringComparison.Ordinal);

    private static string ReplaceWithComparison(string text, string oldValue, string newValue, StringComparison comparison)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(oldValue))
        {
            return text;
        }

        var startIndex = 0;
        var matchIndex = text.IndexOf(oldValue, comparison);
        if (matchIndex < 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        while (matchIndex >= 0)
        {
            builder.Append(text, startIndex, matchIndex - startIndex);
            builder.Append(newValue);
            startIndex = matchIndex + oldValue.Length;
            matchIndex = text.IndexOf(oldValue, startIndex, comparison);
        }

        builder.Append(text, startIndex, text.Length - startIndex);
        return builder.ToString();
    }

    private static bool ContainsCjk(string value)
    {
        foreach (var ch in value)
        {
            if ((ch >= 0x4E00 && ch <= 0x9FFF)
                || (ch >= 0x3400 && ch <= 0x4DBF)
                || (ch >= 0xF900 && ch <= 0xFAFF))
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyMcpServerStatusUpdated(TianShuSidecarEvent sidecarEvent)
    {
        McpServerStatusText = ExtractPreferredEventText(sidecarEvent);
    }

    private void CaptureApprovalRequest(TianShuSidecarEvent sidecarEvent)
    {
        UpsertPendingInteractiveRequest(sidecarEvent);
        CapabilityStatusText = "功能面板：收到审批请求，请在下方面板处理。";
        ActionResultText = PendingApprovalDetailsText;
    }

    private void CapturePermissionRequest(TianShuSidecarEvent sidecarEvent)
    {
        UpsertPendingInteractiveRequest(sidecarEvent);
        CapabilityStatusText = "功能面板：收到权限请求，请按字段确认后提交。";
        ActionResultText = PendingPermissionDetailsText;
    }

    private void CaptureUserInputRequest(TianShuSidecarEvent sidecarEvent)
    {
        UpsertPendingInteractiveRequest(sidecarEvent);
        CapabilityStatusText = "功能面板：收到补录请求，请逐项填写后提交。";
        ActionResultText = PendingUserInputDetailsText;
    }

    private void ApplyServerRequestResolved(TianShuSidecarEvent sidecarEvent)
    {
        var resolvedCallId = Normalize(sidecarEvent.ServerRequestResolved?.CallId)
            ?? Normalize(sidecarEvent.CallId);
        var requestKind = Normalize(sidecarEvent.ServerRequestResolved?.RequestKind);
        var resolution = ResolvePendingInteractiveRequest(resolvedCallId, requestKind);
        if (!resolution.Resolved)
        {
            return;
        }

        if (!HasPendingApproval && !HasPendingPermission && !HasPendingUserInput)
        {
            ClearConversationProgressStatus(ResolveEventTurnId(sidecarEvent));
        }

        if (string.Equals(resolution.RequestKind, "request_user_input", StringComparison.Ordinal))
        {
            CapabilityStatusText = resolution.PromotedNext
                ? "功能面板：上一条待补录请求已结束，已切换到下一条。"
                : HasPendingUserInput
                    ? "功能面板：排队中的待补录请求已结束。"
                    : "功能面板：待补录请求已结束。";
            return;
        }

        CapabilityStatusText = string.Equals(resolution.RequestKind, "permission_requested", StringComparison.Ordinal)
            ? "功能面板：待处理权限请求已结束。"
            : "功能面板：待处理审批已结束。";
    }

    private void RebuildApprovalDecisions(TianShuSidecarEvent sidecarEvent)
    {
        approvalDecisions.Clear();
        foreach (var option in ReadAvailableApprovalDecisions(sidecarEvent))
        {
            approvalDecisions.Add(option);
        }

        SelectedApprovalDecision = approvalDecisions.Count > 0 ? approvalDecisions[0] : null;
    }

    private void RebuildPermissionFields(TianShuSidecarEvent sidecarEvent)
    {
        pendingPermissionFields.Clear();

        foreach (var field in BuildPermissionFields(sidecarEvent))
        {
            pendingPermissionFields.Add(field);
        }
    }

    private void RebuildPendingUserInputQuestions(TianShuSidecarEvent sidecarEvent)
    {
        pendingUserInputQuestions.Clear();

        foreach (var question in BuildUserInputQuestions(sidecarEvent))
        {
            pendingUserInputQuestions.Add(question);
        }
    }

    private IEnumerable<StructuredPermissionField> BuildPermissionFields(TianShuSidecarEvent sidecarEvent)
    {
        if (sidecarEvent.PermissionRequest?.Fields is not { Length: > 0 } fields)
        {
            yield break;
        }

        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.Key))
            {
                continue;
            }

            yield return StructuredPermissionField.FromPayload(field);
        }
    }

    private IEnumerable<StructuredUserInputQuestion> BuildUserInputQuestions(TianShuSidecarEvent sidecarEvent)
    {
        if (sidecarEvent.UserInputRequest?.Questions is { Length: > 0 } typedQuestions)
        {
            foreach (var question in typedQuestions)
            {
                var id = Normalize(question.Id);
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                yield return StructuredUserInputQuestion.FromPayload(question);
            }

            yield break;
        }
    }

    private IEnumerable<TianShuApprovalDecisionOption> ReadAvailableApprovalDecisions(TianShuSidecarEvent sidecarEvent)
    {
        if (sidecarEvent.ApprovalRequest?.AvailableDecisionOptions is { Length: > 0 } typedDecisionOptions)
        {
            foreach (var item in typedDecisionOptions)
            {
                yield return new TianShuApprovalDecisionOption(item);
            }

            yield break;
        }

        if (sidecarEvent.ApprovalRequest?.AvailableDecisions is { Length: > 0 } typedAvailableDecisions)
        {
            var emittedTyped = new HashSet<TianShuApprovalDecision>();
            foreach (var item in typedAvailableDecisions)
            {
                if (!TryMapApprovalDecision(item, out var decision) || !emittedTyped.Add(decision))
                {
                    continue;
                }

                yield return new TianShuApprovalDecisionOption(
                    new TianShuSidecarApprovalDecisionOptionPayload
                    {
                        Decision = decision,
                    });
            }

            if (emittedTyped.Count > 0)
            {
                yield break;
            }
        }

        yield return new TianShuApprovalDecisionOption(
            new TianShuSidecarApprovalDecisionOptionPayload
            {
                Decision = TianShuApprovalDecision.Accept,
            });
        yield return new TianShuApprovalDecisionOption(
            new TianShuSidecarApprovalDecisionOptionPayload
            {
                Decision = TianShuApprovalDecision.Decline,
            });
    }

    private string BuildUserInputAnswersTemplate(TianShuSidecarEvent sidecarEvent)
    {
        if (sidecarEvent.UserInputRequest?.Questions is { Length: > 0 } typedQuestions)
        {
            var typedTemplate = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var question in typedQuestions)
            {
                var id = Normalize(question.Id);
                if (!string.IsNullOrWhiteSpace(id))
                {
                    typedTemplate[id!] = string.Empty;
                }
            }

            return typedTemplate.Count == 0
                ? "{}"
                : JsonSerializer.Serialize(typedTemplate, prettyJsonOptions);
        }

        return "{}";
    }

    private string BuildPermissionAnswersTemplate(TianShuSidecarEvent sidecarEvent)
    {
        if (sidecarEvent.PermissionRequest is { } permissionRequest)
        {
            return BuildPermissionTemplateJson(permissionRequest);
        }

        return "{}";
    }

    internal static string BuildApprovalRequestDetailsJson(TianShuSidecarEvent sidecarEvent)
    {
        if (sidecarEvent.ApprovalRequest is not { } approvalRequest)
        {
            return string.Empty;
        }

        return SerializePrettyJson(new
        {
            callId = sidecarEvent.CallId,
            toolName = Normalize(sidecarEvent.ToolName) ?? Normalize(approvalRequest.ToolName),
            approvalKind = Normalize(approvalRequest.ApprovalKind),
            availableDecisions = approvalRequest.AvailableDecisions ?? [],
            summary = Normalize(approvalRequest.Summary) ?? Normalize(sidecarEvent.Message),
            metadataFields = approvalRequest.MetadataFields.Select(static field => new
            {
                key = field.Key,
                valueType = field.ValueType,
                valueText = field.ValueText,
            }).ToArray(),
        });
    }

    internal static string BuildPermissionRequestDetailsJson(TianShuSidecarEvent sidecarEvent)
    {
        if (sidecarEvent.PermissionRequest is not { } permissionRequest)
        {
            return string.Empty;
        }

        return SerializePrettyJson(new
        {
            callId = sidecarEvent.CallId,
            reason = Normalize(permissionRequest.Reason),
            summary = Normalize(permissionRequest.Summary) ?? Normalize(sidecarEvent.Message),
            permissionsJson = BuildPermissionTemplateJson(permissionRequest),
            fields = permissionRequest.Fields.Select(static field => new
            {
                key = field.Key,
                valueType = field.ValueType,
                valueText = field.ValueText,
            }).ToArray(),
        });
    }

    internal static string BuildUserInputRequestDetailsJson(TianShuSidecarEvent sidecarEvent)
    {
        if (sidecarEvent.UserInputRequest is not { } userInputRequest)
        {
            return string.Empty;
        }

        return SerializePrettyJson(new
        {
            callId = sidecarEvent.CallId,
            summary = Normalize(userInputRequest.Summary) ?? Normalize(sidecarEvent.Message),
            questions = userInputRequest.Questions.Select(static question => new
            {
                id = question.Id,
                header = question.Header,
                prompt = question.Prompt,
                isSecret = question.IsSecret,
                isOther = question.IsOther,
                options = question.Options?.Select(static option => new
                {
                    label = option.Label,
                    description = option.Description,
                }).ToArray(),
            }).ToArray(),
        });
    }

    internal static string ExtractPreferredEventText(TianShuSidecarEvent sidecarEvent)
    {
        return BuildTypedEventText(sidecarEvent)
            ?? Normalize(sidecarEvent.Text)
            ?? Normalize(sidecarEvent.Message)
            ?? string.Empty;
    }

    private string BuildToolActivityText()
    {
        if (toolActivityEntries.Count == 0)
        {
            return "当前没有工具调用轨迹。";
        }

        var builder = new StringBuilder();
        foreach (var entry in toolActivityEntries.Values
                     .OrderByDescending(static item => item.UpdatedAt)
                     .Take(12))
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.Append('[').Append(FormatToolDisplayName(entry.ToolName)).Append("] ").Append(entry.Status);
            if (!string.IsNullOrWhiteSpace(entry.TurnId))
            {
                builder.Append("  turn=").Append(entry.TurnId);
            }

            if (!string.IsNullOrWhiteSpace(entry.Summary))
            {
                builder.AppendLine().Append(entry.Summary);
            }

            if (!string.IsNullOrWhiteSpace(entry.Output))
            {
                builder.AppendLine().Append("输出：").AppendLine();
                builder.Append(TruncateText(entry.Output, 1200));
            }
        }

        return builder.ToString();
    }

    private string BuildTaskActivityText()
    {
        if (taskActivityEntries.Count == 0)
        {
            return "当前没有任务轨迹。";
        }

        var builder = new StringBuilder();
        foreach (var entry in taskActivityEntries.Values
                     .OrderByDescending(static item => item.UpdatedAt)
                     .Take(12))
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append('[')
                .Append(entry.TaskType)
                .Append("] ")
                .Append(entry.Status);
            if (!string.IsNullOrWhiteSpace(entry.Message))
            {
                builder.Append(" - ").Append(entry.Message);
            }
        }

        return builder.ToString();
    }

    private void UpdateConversationProgressStatus(TianShuSidecarEvent sidecarEvent, string? statusText, bool createIfMissing)
    {
        var entry = ResolveAssistantEntryForStatus(ResolveEventTurnId(sidecarEvent), createIfMissing);
        if (entry is null)
        {
            return;
        }

        entry.SetPendingStatusText(statusText);
        RefreshConversationEntryPresentation(entry, forceReasoningRefresh: false, forceFinalRefresh: false);
    }

    private void ClearConversationProgressStatus(string? turnId)
    {
        var entry = ResolveAssistantEntryForStatus(turnId, createIfMissing: false);
        if (entry is null)
        {
            return;
        }

        entry.SetPendingStatusText(null);
        RefreshConversationEntryPresentation(entry, forceReasoningRefresh: false, forceFinalRefresh: false);
    }

    private ConversationEntry? ResolveAssistantEntryForStatus(string? turnId, bool createIfMissing)
    {
        var normalizedTurnId = Normalize(turnId);
        if (!string.IsNullOrWhiteSpace(normalizedTurnId))
        {
            var key = NormalizeTurnKey(normalizedTurnId);
            if (assistantMessages.TryGetValue(key, out var entry))
            {
                return entry;
            }

            return createIfMissing ? GetOrCreateAssistantEntry(normalizedTurnId) : null;
        }

        return assistantMessages.Count == 0 ? null : assistantMessages.Values.Last();
    }

    private ConversationEntry? DetachAssistantEntry(string? turnId)
    {
        var normalizedTurnId = Normalize(turnId);
        if (string.IsNullOrWhiteSpace(normalizedTurnId))
        {
            return null;
        }

        var key = NormalizeTurnKey(normalizedTurnId);
        if (!assistantMessages.TryGetValue(key, out var entry))
        {
            return null;
        }

        assistantMessages.Remove(key);
        return entry;
    }

    private ConversationEntry? DetachLatestAssistantEntry()
    {
        if (assistantMessages.Count == 0)
        {
            return null;
        }

        var latestEntry = assistantMessages.Last();
        assistantMessages.Remove(latestEntry.Key);
        return latestEntry.Value;
    }

    private string? GetLatestActiveAssistantTurnId()
    {
        if (assistantMessages.Count == 0)
        {
            return null;
        }

        var latestTurnKey = assistantMessages.Last().Key;
        return string.Equals(latestTurnKey, NormalizeTurnKey(null), StringComparison.Ordinal)
            ? null
            : latestTurnKey;
    }

    private bool HasAwaitingCommittedPendingFollowUp()
        => pendingFollowUpEntries.Any(static entry => entry.IsAwaitingCommit);

    private bool HasAwaitingTurnSteerPendingFollowUp()
        => pendingFollowUpEntries.Any(static entry => entry.IsAwaitingTurnSteer);

    private void MarkPendingFollowUpAsAwaitingTurnSteer(PendingFollowUpEntry pendingFollowUp)
    {
        var compareText = Normalize(BuildFollowUpMessageWithPendingContext(pendingFollowUp.Message, pendingFollowUp.ContextEntries));
        pendingFollowUp.MarkAsAwaitingCommit(
            compareText,
            imageCount: 0,
            isTurnSteer: true,
            inputs: pendingFollowUp.Inputs);
    }

    private void MarkPendingFollowUpAsAwaitingTurnSteerAccepted(PendingFollowUpEntry pendingFollowUp, string? compareMessage, string? correlationId)
    {
        pendingFollowUp.MarkAsAwaitingCommit(
            compareMessage,
            imageCount: 0,
            correlationId,
            isTurnSteer: true,
            inputs: pendingFollowUp.Inputs);
    }

    private PendingFollowUpEntry? ConsumeAwaitingTurnSteerPendingFollowUp()
    {
        var entry = pendingFollowUpEntries.FirstOrDefault(static item => item.IsAwaitingTurnSteer);
        if (entry is null)
        {
            return null;
        }

        pendingFollowUpEntries.Remove(entry);
        return entry;
    }

    private PendingFollowUpEntry? ConsumeAwaitingTurnSteerPendingFollowUp(PendingFollowUpEntry pendingFollowUp)
    {
        if (!pendingFollowUp.IsAwaitingTurnSteer || !pendingFollowUpEntries.Contains(pendingFollowUp))
        {
            return null;
        }

        pendingFollowUpEntries.Remove(pendingFollowUp);
        return pendingFollowUp;
    }

    private PendingFollowUpEntry? ConsumeAwaitingCommittedPendingFollowUp(PendingFollowUpEntry pendingFollowUp)
    {
        if (!pendingFollowUp.IsAwaitingCommit || !pendingFollowUpEntries.Contains(pendingFollowUp))
        {
            return null;
        }

        pendingFollowUpEntries.Remove(pendingFollowUp);
        return pendingFollowUp;
    }

    private PendingFollowUpEntry? SelectAwaitingCommittedPendingFollowUp(
        string? correlationId,
        string? committedText,
        int imageCount,
        IReadOnlyList<TianShuSidecarUserInputPayload>? committedInputs)
    {
        var normalizedCorrelationId = Normalize(correlationId);
        if (!string.IsNullOrWhiteSpace(normalizedCorrelationId))
        {
            return pendingFollowUpEntries.FirstOrDefault(entry => entry.MatchesAwaitingCommitCorrelation(normalizedCorrelationId));
        }

        if (committedInputs is { Count: > 0 })
        {
            var matchedByInputs = pendingFollowUpEntries.FirstOrDefault(entry => entry.MatchesAwaitingCommitInputs(committedInputs));
            if (matchedByInputs is not null)
            {
                return matchedByInputs;
            }
        }

        return pendingFollowUpEntries.FirstOrDefault(entry => entry.MatchesAwaitingCommitCompareKey(committedText, imageCount));
    }

    private PendingFollowUpEntry? SelectAwaitingTurnSteerPendingFollowUp(
        string? correlationId,
        string? committedText,
        int imageCount,
        IReadOnlyList<TianShuSidecarUserInputPayload>? committedInputs)
    {
        var normalizedCorrelationId = Normalize(correlationId);
        if (!string.IsNullOrWhiteSpace(normalizedCorrelationId))
        {
            return pendingFollowUpEntries.FirstOrDefault(entry => entry.MatchesAwaitingTurnSteerCorrelation(normalizedCorrelationId));
        }

        if (committedInputs is { Count: > 0 })
        {
            var matchedByInputs = pendingFollowUpEntries.FirstOrDefault(entry => entry.MatchesAwaitingTurnSteerInputs(committedInputs));
            if (matchedByInputs is not null)
            {
                return matchedByInputs;
            }
        }

        return pendingFollowUpEntries.FirstOrDefault(entry => entry.MatchesAwaitingTurnSteerCompareKey(committedText, imageCount));
    }

    private PendingFollowUpEntry? FindPendingFollowUpByCorrelation(string? correlationId)
    {
        var normalizedCorrelationId = Normalize(correlationId);
        if (string.IsNullOrWhiteSpace(normalizedCorrelationId))
        {
            return null;
        }

        return pendingFollowUpEntries.FirstOrDefault(entry => entry.MatchesCorrelation(normalizedCorrelationId));
    }

    private void ReleaseAwaitingCommittedPendingFollowUps(bool preserveAwaitingTurnSteer)
    {
        foreach (var entry in pendingFollowUpEntries.Where(item => item.IsAwaitingCommit && (!preserveAwaitingTurnSteer || !item.IsAwaitingTurnSteer)))
        {
            entry.ClearAwaitingCommit();
        }
    }

    private void ReleaseAwaitingTurnSteerPendingFollowUps()
    {
        foreach (var entry in pendingFollowUpEntries.Where(static item => item.IsAwaitingTurnSteer))
        {
            entry.ClearAwaitingCommit();
        }
    }

    private void MergeAwaitingTurnSteerPendingFollowUpsToQueuedRedispatch()
    {
        var pendingSteers = pendingFollowUpEntries.Where(static item => item.IsAwaitingTurnSteer).ToArray();
        if (pendingSteers.Length == 0)
        {
            return;
        }

        if (pendingSteers.Length == 1)
        {
            pendingSteers[0].MarkAsQueue();
            return;
        }

        var mergedMessage = string.Join(
            Environment.NewLine,
            pendingSteers
                .Select(static entry => entry.Message)
                .Where(static message => !string.IsNullOrWhiteSpace(message)));
        if (string.IsNullOrWhiteSpace(mergedMessage))
        {
            mergedMessage = pendingSteers[0].Message;
        }

        var mergedContextEntries = pendingSteers
            .SelectMany(static entry => entry.ContextEntries)
            .ToArray();

        foreach (var entry in pendingSteers)
        {
            pendingFollowUpEntries.Remove(entry);
        }

        pendingFollowUpEntries.Insert(
            0,
            new PendingFollowUpEntry(
                mergedMessage,
                BusySendMode.Queue,
                CreateTextUserInputs(mergedMessage),
                mergedContextEntries,
                PendingFollowUpBucket.QueuedUserMessage));
    }

    private void FinalizeAssistantEntryForSteerBoundary(ConversationEntry entry)
    {
        entry.FlushPendingDraftToProcessSegment(MaxAssistantReasoningChars);
        entry.SetPendingStatusText(null);
        entry.Role = "TianShu";
        entry.Variant = "assistant";
        if (entry.HasPendingFinalContent)
        {
            entry.CommitAssistantFinalContent(null, MaxAssistantFinalChars);
        }
        else
        {
            entry.ClearPendingAssistantFinalContent();
        }

        RefreshConversationEntryPresentation(entry, forceReasoningRefresh: true, forceFinalRefresh: true);
        if (entry.IsSpacerEntry)
        {
            Messages.Remove(entry);
        }
    }

    private static string? ResolveEventTurnId(TianShuSidecarEvent sidecarEvent)
        => Normalize(sidecarEvent.TurnId);

    private static string BuildToolProgressStatusText(string? toolName, string? status)
    {
        var normalizedToolName = Normalize(toolName);
        var displayName = FormatToolDisplayName(normalizedToolName);
        var normalizedStatus = Normalize(status)?.ToLowerInvariant();

        if (string.Equals(normalizedToolName, "contextCompaction", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedStatus switch
            {
                "failed" or "errored" or "cancelled" => "自动压缩失败，等待回合收口…",
                "completed" or "finished" => "自动压缩完成，正在继续处理…",
                _ => "正在自动压缩上下文…",
            };
        }

        return normalizedStatus switch
        {
            _ when string.Equals(normalizedToolName, "spawn_agent", StringComparison.OrdinalIgnoreCase)
                => normalizedStatus switch
                {
                    "failed" or "errored" or "cancelled" => "子代理启动失败，等待回合收口…",
                    "completed" or "finished" => "子代理已启动，正在继续处理…",
                    _ => "正在调度子代理…",
                },
            _ when string.Equals(normalizedToolName, "wait_agent", StringComparison.OrdinalIgnoreCase)
                => normalizedStatus switch
                {
                    "failed" or "errored" or "cancelled" => "等待子代理返回失败，等待回合收口…",
                    "completed" or "finished" => "子代理已返回，正在继续处理…",
                    _ => "正在等待子代理返回…",
                },
            _ when string.Equals(normalizedToolName, "send_input", StringComparison.OrdinalIgnoreCase)
                => normalizedStatus switch
                {
                    "failed" or "errored" or "cancelled" => "子代理输入下发失败，等待回合收口…",
                    "completed" or "finished" => "子代理输入已下发，正在继续处理…",
                    _ => "正在向子代理发送输入…",
                },
            _ when string.Equals(normalizedToolName, "shell_command", StringComparison.OrdinalIgnoreCase)
                => normalizedStatus switch
                {
                    "failed" or "errored" or "cancelled" => "命令执行失败，等待回合收口…",
                    "completed" or "finished" => "命令执行完成，正在继续处理…",
                    _ => "正在执行命令…",
                },
            _ when string.Equals(normalizedToolName, "apply_patch", StringComparison.OrdinalIgnoreCase)
                => normalizedStatus switch
                {
                    "failed" or "errored" or "cancelled" => "文件修改失败，等待回合收口…",
                    "completed" or "finished" => "文件修改完成，正在继续处理…",
                    _ => "正在修改文件…",
                },
            _ when string.Equals(normalizedToolName, "parallel", StringComparison.OrdinalIgnoreCase)
                => normalizedStatus switch
                {
                    "failed" or "errored" or "cancelled" => "并行工具执行失败，等待回合收口…",
                    "completed" or "finished" => "并行工具执行完成，正在继续处理…",
                    _ => "正在并行执行工具…",
                },
            "failed" or "errored" or "cancelled" => $"工具 {displayName} 失败，等待回合收口…",
            "completed" or "finished" => $"工具 {displayName} 已完成，正在继续处理…",
            _ => $"正在运行工具 {displayName}…",
        };
    }

    private static string BuildTaskProgressStatusText(TianShuSidecarEvent sidecarEvent)
    {
        var status = Normalize(sidecarEvent.Task?.Status)
            ?? Normalize(sidecarEvent.Status)
            ?? ((sidecarEvent.EventType ?? string.Empty).EndsWith("completed", StringComparison.OrdinalIgnoreCase) ? "completed" : "started");

        return status.ToLowerInvariant() switch
        {
            "interrupted" => "当前回合已中断。",
            "completed" => "任务已完成，正在收尾…",
            "failed" or "errored" => "任务执行失败。",
            _ => "正在执行当前任务…",
        };
    }

    private static string? BuildOperationProgressStatusText(TianShuSidecarEvent sidecarEvent)
    {
        var operationName = Normalize(sidecarEvent.Operation?.OperationName)
            ?? Normalize(sidecarEvent.OperationName)
            ?? Normalize(sidecarEvent.ToolName)
            ?? Normalize(sidecarEvent.Text);
        if (string.IsNullOrWhiteSpace(operationName))
        {
            return null;
        }

        var phase = Normalize(sidecarEvent.Operation?.Phase)
            ?? Normalize(sidecarEvent.Phase)
            ?? Normalize(sidecarEvent.Status)
            ?? "started";

        return (operationName.ToLowerInvariant(), phase.ToLowerInvariant()) switch
        {
            ("resolve_input", "completed") => "输入已整理，正在继续处理…",
            ("resolve_input", _) => "正在整理输入…",
            ("execute_assistant", "completed") => "思考完成，正在生成输出…",
            ("execute_assistant", _) => "正在思考…",
            ("stream_assistant_output", "completed") => "正在整理最终总结…",
            ("stream_assistant_output", _) => "正在生成回复…",
            ("plan", "completed") => "计划已更新，正在继续处理…",
            ("plan", _) => "正在更新计划…",
            ("wait_subagent", "completed") => "子代理已返回，正在继续处理…",
            ("wait_subagent", _) => "正在等待子代理返回…",
            ("context_compaction", "completed") => "自动压缩完成，正在继续处理…",
            ("context_compaction", _) => "正在自动压缩上下文…",
            (_, "completed") => "当前步骤已完成，正在继续处理…",
            _ => "正在处理当前步骤…",
        };
    }

    private static string BuildToolActivityKey(TianShuSidecarEvent sidecarEvent)
    {
        return Normalize(sidecarEvent.ToolCall?.CallId)
            ?? Normalize(sidecarEvent.CallId)
            ?? Normalize(sidecarEvent.ToolCall?.ItemId)
            ?? Normalize(sidecarEvent.ItemId)
            ?? $"{ResolveEventTurnId(sidecarEvent) ?? "turn"}::{Normalize(sidecarEvent.ToolCall?.ToolName) ?? Normalize(sidecarEvent.ToolName) ?? "tool"}";
    }

    private static string BuildTaskActivityKey(TianShuSidecarEvent sidecarEvent)
    {
        return Normalize(sidecarEvent.ItemId)
            ?? $"{ResolveEventTurnId(sidecarEvent) ?? "turn"}::{Normalize(sidecarEvent.Task?.TaskType) ?? Normalize(sidecarEvent.TaskType) ?? "task"}";
    }

    private static string GetToolActivityStatus(TianShuSidecarEvent sidecarEvent)
    {
        var eventType = (sidecarEvent.EventType ?? string.Empty).Trim().ToLowerInvariant();
        var status = Normalize(sidecarEvent.ToolCall?.Status)
            ?? Normalize(sidecarEvent.Status);
        if (!string.IsNullOrWhiteSpace(status))
        {
            return status!;
        }

        return eventType switch
        {
            "tool_call_completed" => "completed",
            "tool_call_output_delta" => "running",
            _ => "started",
        };
    }

    private string BuildApprovalSummaryText(TianShuSidecarEvent sidecarEvent)
    {
        var decisions = string.Join(" / ", approvalDecisions.Select(static decision => decision.DisplayName));
        var builder = new StringBuilder();
        builder.AppendLine($"调用 ID：{sidecarEvent.CallId ?? "未知"}");
        var toolName = Normalize(sidecarEvent.ToolName)
            ?? Normalize(sidecarEvent.ApprovalRequest?.ToolName);
        if (!string.IsNullOrWhiteSpace(toolName))
        {
            builder.AppendLine($"工具：{toolName}");
        }

        var summary = Normalize(sidecarEvent.ApprovalRequest?.Summary)
            ?? Normalize(sidecarEvent.Message)
            ?? "等待人工审批。";
        builder.AppendLine($"说明：{summary}");
        if (!string.IsNullOrWhiteSpace(decisions))
        {
            builder.AppendLine($"可选决策：{decisions}");
        }

        return builder.ToString().Trim();
    }

    private string BuildPermissionSummaryText(TianShuSidecarEvent sidecarEvent)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"调用 ID：{sidecarEvent.CallId ?? "未知"}");
        var summary = Normalize(sidecarEvent.PermissionRequest?.Summary)
            ?? Normalize(sidecarEvent.PermissionRequest?.Reason)
            ?? Normalize(sidecarEvent.Message)
            ?? "等待权限确认。";
        builder.AppendLine($"说明：{summary}");
        if (pendingPermissionFields.Count > 0)
        {
            builder.AppendLine($"字段数：{pendingPermissionFields.Count}");
        }

        return builder.ToString().Trim();
    }

    private string BuildUserInputSummaryText(TianShuSidecarEvent sidecarEvent)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"调用 ID：{sidecarEvent.CallId ?? "未知"}");
        var summary = Normalize(sidecarEvent.UserInputRequest?.Summary)
            ?? Normalize(sidecarEvent.Message)
            ?? "等待人工补录。";
        builder.AppendLine($"说明：{summary}");
        if (pendingUserInputQuestions.Count > 0)
        {
            builder.AppendLine($"问题数：{pendingUserInputQuestions.Count}");
        }

        return builder.ToString().Trim();
    }

    private static string? BuildTypedEventText(TianShuSidecarEvent sidecarEvent)
    {
        if (TryParsePlanPayload(sidecarEvent.Plan, out var explanation, out var planSteps))
        {
            return BuildPlanOverviewText(explanation, planSteps);
        }

        if (sidecarEvent.McpServerStatus is { } mcpServerStatus)
        {
            return BuildMcpServerStatusText(mcpServerStatus);
        }

        if (sidecarEvent.Reasoning is { } reasoning)
        {
            var reasoningText = Normalize(reasoning.Text);
            if (!string.IsNullOrWhiteSpace(reasoningText))
            {
                return reasoningText;
            }
        }

        if (sidecarEvent.DeprecationNotice is { } deprecationNotice)
        {
            return Normalize(deprecationNotice.Summary)
                ?? Normalize(deprecationNotice.Details);
        }

        if (sidecarEvent.ConfigWarning is { } configWarning)
        {
            return BuildConfigWarningText(configWarning);
        }

        if (sidecarEvent.ThreadStatusChanged is { } threadStatusChanged)
        {
            return BuildThreadStatusChangedText(threadStatusChanged);
        }

        if (sidecarEvent.ThreadNameUpdated is { } threadNameUpdated)
        {
            return string.IsNullOrWhiteSpace(threadNameUpdated.ThreadName)
                ? "线程标题已更新。"
                : $"线程标题：{threadNameUpdated.ThreadName}";
        }

        if (sidecarEvent.ThreadTokenUsage is { } threadTokenUsage)
        {
            return BuildThreadTokenUsageText(threadTokenUsage);
        }

        if (sidecarEvent.AgentJobProgress is { } agentJobProgress)
        {
            return $"Agent Job 进度：{agentJobProgress.CompletedItems}/{agentJobProgress.TotalItems}，运行中 {agentJobProgress.RunningItems}，待处理 {agentJobProgress.PendingItems}。";
        }

        if (sidecarEvent.CommandExecOutputDelta is { } commandExecOutputDelta)
        {
            return $"进程 {commandExecOutputDelta.ProcessId} 的 {commandExecOutputDelta.Stream} 输出已更新。";
        }

        if (sidecarEvent.AppListUpdated is { } appListUpdated)
        {
            return BuildAppListUpdatedText(appListUpdated);
        }

        if (sidecarEvent.WindowsSandboxSetup is { } windowsSandboxSetup)
        {
            return BuildWindowsSandboxSetupText(windowsSandboxSetup);
        }

        if (sidecarEvent.McpServerOauthLogin is { } mcpServerOauthLogin)
        {
            return BuildMcpServerOauthLoginText(mcpServerOauthLogin);
        }

        if (sidecarEvent.RealtimeSession is { } realtimeSession)
        {
            return BuildRealtimeSessionText(realtimeSession);
        }

        if (sidecarEvent.FuzzyFileSearchSession is { } fuzzyFileSearchSession)
        {
            return BuildFuzzyFileSearchSessionText(fuzzyFileSearchSession);
        }

        if (sidecarEvent.ThreadRealtimeItemAdded is { } threadRealtimeItemAdded)
        {
            return BuildThreadRealtimeItemAddedText(threadRealtimeItemAdded);
        }

        if (sidecarEvent.ThreadRealtimeOutputAudioDelta is { } threadRealtimeOutputAudioDelta)
        {
            return $"实时音频片段：{threadRealtimeOutputAudioDelta.SampleRate}Hz / {threadRealtimeOutputAudioDelta.NumChannels} 声道。";
        }

        if (sidecarEvent.ThreadRealtimeError is { } threadRealtimeError)
        {
            return Normalize(threadRealtimeError.Message);
        }

        if (sidecarEvent.ThreadRealtimeClosed is { } threadRealtimeClosed)
        {
            return string.IsNullOrWhiteSpace(threadRealtimeClosed.Reason)
                ? "实时会话已关闭。"
                : $"实时会话已关闭：{threadRealtimeClosed.Reason}";
        }

        return Normalize(sidecarEvent.ToolCall?.OutputText)
            ?? Normalize(sidecarEvent.ToolCall?.InputText)
            ?? Normalize(sidecarEvent.ApprovalRequest?.Summary)
            ?? Normalize(sidecarEvent.PermissionRequest?.Summary)
            ?? Normalize(sidecarEvent.UserInputRequest?.Summary);
    }

    private static string BuildPermissionTemplateJson(TianShuSidecarPermissionRequestPayload permissionRequest)
    {
        if (permissionRequest.Fields.Length == 0)
        {
            return "{}";
        }

        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var field in permissionRequest.Fields)
        {
            var key = Normalize(field.Key);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            values[key] = BuildPermissionFieldValue(field);
        }

        return values.Count == 0 ? "{}" : JsonSerializer.Serialize(values);
    }

    private static JsonElement BuildPermissionFieldValue(TianShuSidecarPermissionFieldPayload field)
    {
        var valueType = Normalize(field.ValueType)?.ToLowerInvariant();
        var valueText = Normalize(field.ValueText);

        return valueType switch
        {
            "bool" when bool.TryParse(valueText, out var boolValue) => JsonSerializer.SerializeToElement(boolValue),
            "number" when TryParseJsonFragment(valueText, out var numberValue) && numberValue.ValueKind == JsonValueKind.Number => numberValue,
            "json" when TryParseJsonFragment(valueText, out var jsonValue) => jsonValue,
            "null" => ParseJsonLiteral("null"),
            _ => JsonSerializer.SerializeToElement(valueText ?? string.Empty),
        };
    }

    private static bool TryParseJsonFragment(string? text, out JsonElement value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = default;
            return false;
        }

        try
        {
            value = ParseJsonLiteral(text).Clone();
            return true;
        }
        catch (JsonException)
        {
            value = default;
            return false;
        }
    }

    private static JsonElement ParseJsonLiteral(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static string? BuildMcpServerStatusText(TianShuSidecarMcpServerStatusPayload payload)
    {
        var builder = new StringBuilder();
        if (payload.Count is int count)
        {
            builder.Append("MCP 服务器数量：").Append(count);
        }

        if (payload.Servers.Length > 0)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine().AppendLine();
            }

            foreach (var server in payload.Servers.Where(static server => !string.IsNullOrWhiteSpace(server.Name)))
            {
                builder.Append("• ").Append(server.Name);

                var authStatus = Normalize(server.AuthStatus);
                if (!string.IsNullOrWhiteSpace(authStatus))
                {
                    builder.Append("  授权：").Append(authStatus);
                }

                builder.Append("  工具：").Append(server.ToolCount)
                    .Append("  资源：").Append(server.ResourceCount)
                    .Append("  模板：").Append(server.ResourceTemplateCount)
                    .AppendLine();
            }
        }

        var text = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string? BuildConfigWarningText(TianShuSidecarConfigWarningPayload payload)
    {
        var builder = new StringBuilder();
        AppendLineIfPresent(builder, payload.Summary);
        AppendLineIfPresent(builder, payload.Details);
        if (!string.IsNullOrWhiteSpace(payload.Path))
        {
            AppendLineIfPresent(builder, $"路径：{payload.Path}");
        }

        if (payload.Range?.Start is { } start && payload.Range.End is { } end)
        {
            AppendLineIfPresent(builder, $"范围：{start.Line}:{start.Column} - {end.Line}:{end.Column}");
        }

        var text = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string BuildThreadStatusChangedText(TianShuSidecarThreadStatusChangedPayload payload)
    {
        var builder = new StringBuilder();
        builder.Append("线程状态：").Append(payload.Type);
        if (payload.ActiveFlags.Count > 0)
        {
            builder.Append(" (").Append(string.Join(", ", payload.ActiveFlags)).Append(')');
        }

        return builder.ToString();
    }

    private static string BuildThreadTokenUsageText(TianShuSidecarThreadTokenUsagePayload payload)
    {
        var last = payload.Last;
        var total = payload.Total;
        if (last is null && total is null)
        {
            return "线程 token 使用量已更新。";
        }

        var builder = new StringBuilder();
        if (last is not null)
        {
            builder.Append("本轮 token：").Append(last.TotalTokens);
        }

        if (total is not null)
        {
            if (builder.Length > 0)
            {
                builder.Append("；");
            }

            builder.Append("累计 token：").Append(total.TotalTokens);
        }

        if (payload.ModelContextWindow is int contextWindow && contextWindow > 0)
        {
            builder.Append("；上下文窗口：").Append(contextWindow);
        }

        return builder.ToString();
    }

    private static string BuildAppListUpdatedText(TianShuSidecarAppListUpdatedPayload payload)
    {
        if (payload.Items.Count == 0)
        {
            return "Apps 列表已更新。";
        }

        var names = payload.Items
            .Select(static item => Normalize(item.Name) ?? Normalize(item.Id))
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Take(4)
            .ToArray();
        return names.Length == 0
            ? $"Apps 列表已更新，共 {payload.Items.Count} 项。"
            : $"Apps 列表已更新，共 {payload.Items.Count} 项：{string.Join("、", names)}";
    }

    private static string BuildWindowsSandboxSetupText(TianShuSidecarWindowsSandboxSetupPayload payload)
    {
        if (payload.Success == true)
        {
            return string.IsNullOrWhiteSpace(payload.Mode)
                ? "Windows Sandbox 初始化完成。"
                : $"Windows Sandbox 初始化完成，模式：{payload.Mode}。";
        }

        if (payload.Success == false)
        {
            return Normalize(payload.Error) ?? "Windows Sandbox 初始化失败。";
        }

        return "Windows Sandbox 状态已更新。";
    }

    private static string BuildMcpServerOauthLoginText(TianShuSidecarMcpServerOauthLoginPayload payload)
    {
        if (payload.Success == true)
        {
            return string.IsNullOrWhiteSpace(payload.Name)
                ? "MCP OAuth 登录完成。"
                : $"MCP OAuth 登录完成：{payload.Name}";
        }

        if (payload.Success == false)
        {
            return Normalize(payload.Error)
                ?? (string.IsNullOrWhiteSpace(payload.Name) ? "MCP OAuth 登录失败。" : $"MCP OAuth 登录失败：{payload.Name}");
        }

        return "MCP OAuth 状态已更新。";
    }

    private static string BuildRealtimeSessionText(TianShuSidecarRealtimeSessionPayload payload)
    {
        return string.IsNullOrWhiteSpace(payload.SessionId)
            ? "实时会话已启动。"
            : $"实时会话已启动：{payload.SessionId}";
    }

    private static string BuildFuzzyFileSearchSessionText(TianShuSidecarFuzzyFileSearchSessionPayload payload)
    {
        if (payload.IsCompleted)
        {
            return string.IsNullOrWhiteSpace(payload.SessionId)
                ? "模糊文件搜索已完成。"
                : $"模糊文件搜索已完成：{payload.SessionId}";
        }

        return string.IsNullOrWhiteSpace(payload.SessionId)
            ? $"模糊文件搜索已更新，当前 {payload.Files.Count} 个候选。"
            : $"模糊文件搜索 {payload.SessionId} 已更新，当前 {payload.Files.Count} 个候选。";
    }

    private static string BuildThreadRealtimeItemAddedText(TianShuSidecarThreadRealtimeItemAddedPayload payload)
    {
        return Normalize(payload.Text)
               ?? Normalize(payload.ItemType)
               ?? Normalize(payload.ItemId)
               ?? "实时会话新增了一条项目。";
    }

    private static void AppendLineIfPresent(StringBuilder builder, string? text)
    {
        var normalized = Normalize(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.AppendLine();
        }

        builder.Append(normalized);
    }

    private static string SerializePrettyJson(object payload)
        => JsonSerializer.Serialize(payload, SharedPrettyJsonOptions);

    private static string TruncateText(string text, int maxChars)
    {
        if (text.Length <= maxChars)
        {
            return text;
        }

        return $"{text.Substring(0, maxChars)}{Environment.NewLine}[truncated]";
    }

    private static string? ReadJsonString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static bool TryMapApprovalDecision(string? rawValue, out TianShuApprovalDecision decision)
    {
        decision = TianShuApprovalDecision.Decline;
        var normalized = Normalize(rawValue);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return normalized.ToLowerInvariant() switch
        {
            "accept" => TryAssign(TianShuApprovalDecision.Accept, out decision),
            "acceptforsession" => TryAssign(TianShuApprovalDecision.AcceptForSession, out decision),
            "acceptandremember" => TryAssign(TianShuApprovalDecision.AcceptAndRemember, out decision),
            "acceptwithexecpolicyamendment" => TryAssign(TianShuApprovalDecision.AcceptWithExecPolicyAmendment, out decision),
            "applynetworkpolicyamendment" => TryAssign(TianShuApprovalDecision.ApplyNetworkPolicyAmendment, out decision),
            "cancel" => TryAssign(TianShuApprovalDecision.Cancel, out decision),
            "decline" => TryAssign(TianShuApprovalDecision.Decline, out decision),
            _ => false,
        };
    }

    private static bool TryAssign(TianShuApprovalDecision value, out TianShuApprovalDecision decision)
    {
        decision = value;
        return true;
    }

    private void AppendAssistantDelta(string? turnId, string? delta)
    {
        if (string.IsNullOrWhiteSpace(delta))
        {
            return;
        }

        var entry = GetOrCreateAssistantEntry(turnId);
        entry.AppendAssistantFinalDelta(delta!, MaxAssistantFinalChars);
        RefreshConversationEntryPresentation(entry, forceReasoningRefresh: false, forceFinalRefresh: false);
        ScrollMessagesToEnd();
    }

    private void CompleteAssistantMessage(
        string? turnId,
        string? completedText,
        string? completionStatus = null,
        string? fallbackText = null)
    {
        var key = NormalizeTurnKey(turnId);
        if (!assistantMessages.TryGetValue(key, out var entry))
        {
            if (string.IsNullOrWhiteSpace(completedText))
            {
                return;
            }

            entry = new ConversationEntry("TianShu", string.Empty, "assistant");
            entry.CommitAssistantFinalContent(completedText, MaxAssistantFinalChars);
            Messages.Add(entry);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(completedText))
            {
                entry.Role = "TianShu";
                entry.Variant = "assistant";
                entry.CommitAssistantFinalContent(completedText, MaxAssistantFinalChars);
            }
            else
            {
                switch (Normalize(completionStatus)?.ToLowerInvariant())
                {
                    case "completed":
                        entry.Role = "TianShu";
                        entry.Variant = "assistant";
                        entry.CommitAssistantFinalContent(null, MaxAssistantFinalChars);
                        break;
                    case "failed":
                        entry.CompleteFailedRun(fallbackText, MaxAssistantFinalChars);
                        break;
                    case "interrupted":
                        entry.CompleteInterruptedRun(fallbackText, MaxAssistantFinalChars);
                        break;
                }
            }
        }

        entry.SetPendingStatusText(null);
        RefreshConversationEntryPresentation(entry, forceReasoningRefresh: true, forceFinalRefresh: true);
        assistantMessages.Remove(key);
        ScrollMessagesToEnd();
    }

    private ConversationEntry GetOrCreateAssistantEntry(string? turnId)
    {
        var key = NormalizeTurnKey(turnId);
        if (assistantMessages.TryGetValue(key, out var entry))
        {
            return entry;
        }

        entry = new ConversationEntry("处理中", string.Empty, "progress");
        assistantMessages[key] = entry;
        Messages.Add(entry);
        return entry;
    }

    private bool FinalizePendingAssistantEntries(string completionStatus, string? fallbackMessage = null)
    {
        if (assistantMessages.Count == 0)
        {
            ClearCurrentPlan();
            return false;
        }

        var normalizedStatus = Normalize(completionStatus)?.ToLowerInvariant() ?? "completed";
        foreach (var item in assistantMessages.ToArray())
        {
            var entry = item.Value;
            entry.FlushPendingDraftToProcessSegment(MaxAssistantReasoningChars);
            entry.SetPendingStatusText(null);
            switch (normalizedStatus)
            {
                case "interrupted":
                    entry.CompleteInterruptedRun(fallbackMessage, MaxAssistantFinalChars);
                    break;
                case "failed":
                    entry.CompleteFailedRun(fallbackMessage, MaxAssistantFinalChars);
                    break;
                default:
                    entry.Role = "TianShu";
                    entry.Variant = "assistant";
                    if (!entry.HasFinalContent)
                    {
                        if (!string.IsNullOrWhiteSpace(fallbackMessage))
                        {
                            entry.CommitAssistantFinalContent(fallbackMessage, MaxAssistantFinalChars);
                        }
                        else
                        {
                            entry.CommitAssistantFinalContent(null, MaxAssistantFinalChars);
                        }
                    }
                    break;
            }

            RefreshConversationEntryPresentation(entry, forceReasoningRefresh: true, forceFinalRefresh: true);
        }

        assistantMessages.Clear();
        ClearCurrentPlan();
        ScrollMessagesToEnd();
        return true;
    }

    private void RefreshConversationEntryPresentation(
        ConversationEntry entry,
        bool forceReasoningRefresh,
        bool forceFinalRefresh)
    {
        if (!entry.IsAssistantLike)
        {
            if (!string.IsNullOrWhiteSpace(entry.Content))
            {
                entry.FinalDocument = CreateConversationDocument(entry.Content);
            }

            return;
        }

        if (entry.ShouldRefreshFinalDocument(forceFinalRefresh))
        {
            entry.FinalDocument = CreateConversationDocument(entry.Content);
            entry.MarkFinalDocumentRendered();
        }

        if (entry.HasReasoningContent
            && (string.Equals(entry.Role, "已中断", StringComparison.Ordinal)
                || string.Equals(entry.Role, "已失败", StringComparison.Ordinal)))
        {
            // 中断/失败后必须默认展开过程内容，避免历史被状态文案盖住。
            entry.IsReasoningExpanded = true;
        }
        else if (entry.HasFinalContent && entry.HasReasoningContent)
        {
            entry.IsReasoningExpanded = false;
        }
        else if (!entry.HasFinalContent)
        {
            entry.IsReasoningExpanded = true;
        }
    }

    private FlowDocument CreateConversationDocument(string content)
    {
        return ConversationMarkdownRenderer.BuildDocument(
            content,
            WorkingDirectory,
            CreateConversationMarkdownTheme(),
            HandleConversationLinkActivated);
    }

    private ConversationMarkdownTheme CreateConversationMarkdownTheme()
    {
        var foregroundColor = TryFindResource(EnvironmentColors.ToolWindowTextColorKey) is Color foregroundColorValue
            ? foregroundColorValue
            : Colors.WhiteSmoke;
        var foregroundBrush = CreateThemeBrush(foregroundColor)
            ?? Brushes.WhiteSmoke;
        var borderBrush = TryFindResource(EnvironmentColors.ToolWindowBorderBrushKey) as Brush
            ?? Brushes.DimGray;
        var linkBrush = SystemColors.HotTrackBrush;
        var inlineCodeBackgroundBrush = TryFindResource(EnvironmentColors.CommandBarMenuBackgroundGradientBrushKey) as Brush
            ?? Brushes.Transparent;
        var codeBlockBackgroundBrush = TryFindResource(EnvironmentColors.CommandBarMenuBackgroundGradientBrushKey) as Brush
            ?? Brushes.Transparent;

        return new ConversationMarkdownTheme(
            foregroundBrush,
            linkBrush,
            inlineCodeBackgroundBrush,
            codeBlockBackgroundBrush,
            borderBrush);
    }

    private static Brush CreateThemeBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void HandleConversationLinkActivated(ConversationLinkTarget target)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (target.IsExternal)
        {
            try
            {
                System.Diagnostics.Process.Start(new ProcessStartInfo
                {
                    FileName = target.ExternalUri,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                AppendSystemMessage($"打开链接失败：{ex.Message}", addBlankLineBefore: true);
            }

            return;
        }

        OpenFileInVisualStudio(target.AbsolutePath, "代码文件", target.Line, target.Column);
    }

    private void AppendMessage(string role, string content)
    {
        Messages.Add(new ConversationEntry(role, content));
        ScrollMessagesToEnd();
    }

    private void AppendSystemMessage(string content, bool addBlankLineBefore = false)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        if (addBlankLineBefore && Messages.Count > 0)
        {
            Messages.Add(new ConversationEntry(string.Empty, string.Empty));
        }

        Messages.Add(new ConversationEntry("系统", content));
        ScrollMessagesToEnd();
    }

    private void ValidateBootstrapInputs()
    {
        if (!Directory.Exists(WorkingDirectory))
        {
            throw new DirectoryNotFoundException($"工作目录不存在：{WorkingDirectory}");
        }

        if (!File.Exists(ConfigPath))
        {
            throw new FileNotFoundException("未找到 ~/.tianshu/tianshu.toml。", ConfigPath);
        }

        if (!File.Exists(AppHostProjectPath))
        {
            throw new FileNotFoundException("未找到 TianShu.AppHost.csproj。", AppHostProjectPath);
        }
    }

    private void SetBusy(bool busy, string status, bool keepInitialized = true)
    {
        isBusy = busy;
        if (!keepInitialized && !busy)
        {
            isInitialized = false;
        }

        StatusText = status;
        RefreshCommandState();
    }

    private void RefreshCommandState()
    {
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(ShowSteerFollowUpButton));
        OnPropertyChanged(nameof(CanSteerFollowUp));
        OnPropertyChanged(nameof(CanInterrupt));
        OnPropertyChanged(nameof(CanReset));
        OnPropertyChanged(nameof(CanRefreshThreads));
        OnPropertyChanged(nameof(CanClearAllThreads));
        OnPropertyChanged(nameof(CanResumeThread));
        OnPropertyChanged(nameof(CanRenameThread));
        OnPropertyChanged(nameof(CanArchiveThread));
        OnPropertyChanged(nameof(CanDeleteThread));
        OnPropertyChanged(nameof(CanForkThread));
        OnPropertyChanged(nameof(CanReadThread));
        OnPropertyChanged(nameof(CanUnarchiveThread));
        OnPropertyChanged(nameof(CanRollbackThread));
        OnPropertyChanged(nameof(CanUpdateThreadMetadata));
        OnPropertyChanged(nameof(CanExecuteCapabilityAction));
        OnPropertyChanged(nameof(CanFillActionPayload));
        OnPropertyChanged(nameof(CanRespondToApproval));
        OnPropertyChanged(nameof(CanSubmitUserInput));
        OnPropertyChanged(nameof(CanRespondToPermission));
        OnPropertyChanged(nameof(CanAddCurrentFileContext));
        OnPropertyChanged(nameof(CanAddSelectionContext));
        OnPropertyChanged(nameof(CanAddSpecifiedFileContext));
        OnPropertyChanged(nameof(CanBrowseBootstrapInputs));
        OnPropertyChanged(nameof(CanReconnect));
        OnPropertyChanged(nameof(CanExecuteSettingsAction));
        OnPropertyChanged(nameof(CanClearPendingContext));
        OnPropertyChanged(nameof(CanInvokeRuntimeSurface));
        OnPropertyChanged(nameof(SendButtonText));
        OnPropertyChanged(nameof(FollowUpModeText));
        OnPropertyChanged(nameof(PendingContextSummary));
        OnPropertyChanged(nameof(PendingFollowUpSummary));
        OnPropertyChanged(nameof(ComposerMetaText));
        OnPropertyChanged(nameof(SelectedSidecarEventDetails));
        OnPropertyChanged(nameof(ThreadOperationTargetText));
        OnPropertyChanged(nameof(DisplayedThreadFilePathText));
        OnPropertyChanged(nameof(RuntimeEventLogPathText));
        OnPropertyChanged(nameof(CanOpenDisplayedThreadFile));
        OnPropertyChanged(nameof(CanOpenThreadSessionsDirectory));
        OnPropertyChanged(nameof(CanOpenRuntimeEventLogFile));
        OnPropertyChanged(nameof(CurrentThreadTitle));
        OnPropertyChanged(nameof(CurrentThreadSubtitle));
        OnPropertyChanged(nameof(StatusSummaryText));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsInitialized));
        OnPropertyChanged(nameof(ShowInitializationState));
        OnPropertyChanged(nameof(IsInitializationInProgress));
        OnPropertyChanged(nameof(HasMessages));
        OnPropertyChanged(nameof(HasConversationStarted));
        OnPropertyChanged(nameof(ShowHomeState));
        OnPropertyChanged(nameof(ShowThreadState));
        OnPropertyChanged(nameof(HasRecentThreads));
        NotifyRecentThreadsStateChanged();
        OnPropertyChanged(nameof(HasPendingApproval));
        OnPropertyChanged(nameof(HasPendingPermission));
        OnPropertyChanged(nameof(HasPendingUserInput));
        OnPropertyChanged(nameof(HasPendingContext));
        OnPropertyChanged(nameof(HasPendingFollowUps));
        OnPropertyChanged(nameof(ShowInlineStatusRegion));
        OnPropertyChanged(nameof(HasSidecarEvents));
        OnPropertyChanged(nameof(HasReasoning));
        OnPropertyChanged(nameof(HasLatestPlan));
        OnPropertyChanged(nameof(HasLatestDiff));
        OnPropertyChanged(nameof(HasToolActivity));
        OnPropertyChanged(nameof(HasTaskActivity));
        OnPropertyChanged(nameof(HasSettingsResult));
        OnPropertyChanged(nameof(HasMcpServerStatus));
        OnPropertyChanged(nameof(HasRuntimeOverviewEntries));
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshCommandState();
        ScrollMessagesToEnd();
    }

    private void OnPendingFollowUpsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshCommandState();
    }

    private void OnRecentThreadsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshCollaborationThreadsState();
        NotifyRecentThreadsStateChanged();
    }

    private void OnSidecarEventsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasSidecarEvents));
        OnPropertyChanged(nameof(CanClearSidecarEvents));
        OnPropertyChanged(nameof(CanOpenRuntimeEventLogFile));
    }

    private void OnCurrentPlanStepsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasCurrentPlanSteps));
        OnPropertyChanged(nameof(ShowPlanPanel));
        OnPropertyChanged(nameof(CurrentPlanSummaryText));
    }

    private void ScrollMessagesToEnd()
    {
        if (Messages.Count == 0)
        {
            return;
        }

        _ = Dispatcher.InvokeAsync(() =>
        {
            MessagesListBox.UpdateLayout();
            MessagesListBox.ScrollIntoView(Messages[Messages.Count - 1]);
        });
    }

    private void FocusInput()
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            InputTextBox.Focus();
            Keyboard.Focus(InputTextBox);
            InputTextBox.CaretIndex = InputTextBox.Text.Length;
        });
    }

    private void SetEnterBehavior(bool shouldEnterSend)
    {
        enterSends = shouldEnterSend;
        OnPropertyChanged(nameof(EnterSends));
        OnPropertyChanged(nameof(CtrlEnterSends));
        OnPropertyChanged(nameof(EnterBehaviorHint));
        OnPropertyChanged(nameof(EnterBehaviorShortText));
        OnPropertyChanged(nameof(ComposerMetaText));
    }

    private void SetBusySendMode(BusySendMode mode)
    {
        busySendMode = mode;
        OnPropertyChanged(nameof(FollowUpModeText));
        OnPropertyChanged(nameof(BusyModeShortText));
        RefreshCommandState();
    }

    private void OpenInspectorTab(int tabIndex)
    {
        IsInspectorOpen = true;
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (InspectorScrollViewer is null)
            {
                return;
            }

            InspectorScrollViewer.UpdateLayout();

            switch (tabIndex)
            {
                case 2:
                    InspectorSettingsSection?.BringIntoView();
                    break;
                case 1:
                    InspectorActivitySection?.BringIntoView();
                    break;
                default:
                    InspectorScrollViewer.ScrollToHome();
                    InspectorConversationSection?.BringIntoView();
                    break;
            }
        });
    }

    private void CloseInspector()
    {
        if (!IsInspectorOpen)
        {
            return;
        }

        IsInspectorOpen = false;
        FocusInput();
    }

    private void NotifyRecentThreadsStateChanged()
    {
        OnPropertyChanged(nameof(HasRecentThreads));
        OnPropertyChanged(nameof(HasNoRecentThreads));
        OnPropertyChanged(nameof(CanToggleRecentThreads));
        OnPropertyChanged(nameof(RecentThreadsToggleText));
        OnPropertyChanged(nameof(RecentThreadsSummaryText));
        OnPropertyChanged(nameof(VisibleRecentThreads));
        OnPropertyChanged(nameof(CanClearAllThreads));
    }

    private void NotifyCollaborationThreadsStateChanged()
    {
        OnPropertyChanged(nameof(HasCollaborationThreads));
        OnPropertyChanged(nameof(ShowCollaborationPanel));
        OnPropertyChanged(nameof(CollaborationSummaryText));
    }

    private string GetWorkingDirectoryDisplayName()
    {
        if (string.IsNullOrWhiteSpace(WorkingDirectory))
        {
            return "未配置工作目录";
        }

        var normalized = WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var leaf = Path.GetFileName(normalized);
        return string.IsNullOrWhiteSpace(leaf) ? WorkingDirectory : leaf;
    }

    private static bool LooksLikeGeneratedThreadId(string text)
        => text.StartsWith("thread_", StringComparison.OrdinalIgnoreCase);

    private static string TrimSingleLine(string text, int maxLength)
    {
        var flattened = string.Join(" ", text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            .Trim();
        if (flattened.Length <= maxLength)
        {
            return flattened;
        }

        return flattened.Substring(0, maxLength).TrimEnd() + "...";
    }

    private string BuildStatusText(string? message, string fallback)
        => $"状态：{(!string.IsNullOrWhiteSpace(message) ? message : fallback)}";

    private string BuildFollowUpStatusText(BusySendMode mode)
        => mode switch
        {
            BusySendMode.Steer => BuildStatusText("正在引导当前回合。", "正在引导当前回合。"),
            BusySendMode.Interrupt => BuildStatusText("正在中断并提交新的跟进消息。", "正在中断并提交新的跟进消息。"),
            _ => BuildStatusText("正在提交跟进消息。", "正在提交跟进消息。"),
        };

    private static TianShuSidecarFollowUpMode MapFollowUpMode(BusySendMode mode)
        => mode switch
        {
            BusySendMode.Steer => TianShuSidecarFollowUpMode.Steer,
            BusySendMode.Interrupt => TianShuSidecarFollowUpMode.Interrupt,
            _ => TianShuSidecarFollowUpMode.Queue,
        };

    private void SetInterruptRequestPending(bool isPending)
    {
        if (interruptRequestPending == isPending)
        {
            return;
        }

        interruptRequestPending = isPending;
        RefreshCommandState();
    }

    private static bool IsInterruptPendingFollowUp(TianShuSidecarPendingFollowUpPayload pendingFollowUp)
        => string.Equals(pendingFollowUp.RequestedMode, "Interrupt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(pendingFollowUp.EffectiveMode, "Interrupt", StringComparison.OrdinalIgnoreCase);

    private static bool IsSteerPendingFollowUp(TianShuSidecarPendingFollowUpPayload pendingFollowUp)
        => string.Equals(pendingFollowUp.EffectiveMode, "Steer", StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrWhiteSpace(pendingFollowUp.EffectiveMode)
                && string.Equals(pendingFollowUp.RequestedMode, "Steer", StringComparison.OrdinalIgnoreCase));

    private static bool IsSteerPendingInputStateEntry(TianShuSidecarPendingInputStateEntryPayload pendingInputEntry)
    {
        if (IsPendingSteerPendingInputStateBucket(pendingInputEntry.PendingBucket))
        {
            return true;
        }

        if (IsQueuedUserMessagePendingInputStateBucket(pendingInputEntry.PendingBucket))
        {
            return false;
        }

        return string.Equals(pendingInputEntry.EffectiveMode, "Steer", StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrWhiteSpace(pendingInputEntry.EffectiveMode)
                && string.Equals(pendingInputEntry.RequestedMode, "Steer", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsQueuedUserMessagePendingInputStateEntry(TianShuSidecarPendingInputStateEntryPayload pendingInputEntry)
        => IsQueuedUserMessagePendingInputStateBucket(pendingInputEntry.PendingBucket)
           && !IsInterruptPendingInputStateEntry(pendingInputEntry);

    private static bool IsInterruptPendingInputStateEntry(TianShuSidecarPendingInputStateEntryPayload pendingInputEntry)
        => string.Equals(pendingInputEntry.RequestedMode, "Interrupt", StringComparison.OrdinalIgnoreCase)
           && (pendingInputEntry.LifecycleState is "queued" or "interrupt_requested"
               || string.Equals(pendingInputEntry.LifecycleState, "interrupt_completed", StringComparison.OrdinalIgnoreCase));

    private static bool IsPendingSteerPendingInputStateBucket(string? pendingBucket)
        => string.Equals(pendingBucket, nameof(PendingFollowUpBucket.PendingSteer), StringComparison.OrdinalIgnoreCase)
           || string.Equals(pendingBucket, "PendingSteer", StringComparison.OrdinalIgnoreCase);

    private static bool IsQueuedUserMessagePendingInputStateBucket(string? pendingBucket)
        => string.Equals(pendingBucket, nameof(PendingFollowUpBucket.QueuedUserMessage), StringComparison.OrdinalIgnoreCase)
           || string.Equals(pendingBucket, "QueuedUserMessage", StringComparison.OrdinalIgnoreCase);

    private static BusySendMode MapPendingInputStateEntryMode(TianShuSidecarPendingInputStateEntryPayload pendingInputEntry)
    {
        if (IsInterruptPendingInputStateEntry(pendingInputEntry))
        {
            return BusySendMode.Interrupt;
        }

        if (string.Equals(pendingInputEntry.RequestedMode, "Steer", StringComparison.OrdinalIgnoreCase)
            || string.Equals(pendingInputEntry.EffectiveMode, "Steer", StringComparison.OrdinalIgnoreCase))
        {
            return BusySendMode.Steer;
        }

        return BusySendMode.Queue;
    }

    private static PendingFollowUpBucket MapPendingInputStateEntryBucket(TianShuSidecarPendingInputStateEntryPayload pendingInputEntry)
    {
        if (IsPendingSteerPendingInputStateBucket(pendingInputEntry.PendingBucket))
        {
            return PendingFollowUpBucket.PendingSteer;
        }

        if (IsQueuedUserMessagePendingInputStateBucket(pendingInputEntry.PendingBucket))
        {
            return PendingFollowUpBucket.QueuedUserMessage;
        }

        return IsSteerPendingInputStateEntry(pendingInputEntry)
            ? PendingFollowUpBucket.PendingSteer
            : PendingFollowUpBucket.QueuedUserMessage;
    }

    private static string NormalizeTurnKey(string? turnId)
        => string.IsNullOrWhiteSpace(turnId) ? "__active__" : turnId.Trim();

    private static string FormatToolDisplayName(string? toolName)
    {
        var normalizedToolName = Normalize(toolName);
        if (string.IsNullOrWhiteSpace(normalizedToolName))
        {
            return "tool";
        }

        return normalizedToolName.ToLowerInvariant() switch
        {
            "contextcompaction" => "自动压缩",
            "commandexecution" => "命令执行",
            "filechange" => "文件修改",
            "mcptoolcall" => "MCP 工具",
            "shell_command" => "执行命令",
            "apply_patch" => "修改文件",
            "update_plan" => "更新计划",
            "request_user_input" => "请求输入",
            "spawn_agent" => "启动子代理",
            "send_input" => "发送输入",
            "resume_agent" => "恢复子代理",
            "wait_agent" => "等待子代理",
            "close_agent" => "关闭子代理",
            "parallel" => "并行工具",
            _ => normalizedToolName,
        };
    }

    private bool TryRegisterTurnError(TianShuSidecarEvent sidecarEvent, string message)
    {
        var key = BuildTurnErrorKey(sidecarEvent, message);
        return string.IsNullOrWhiteSpace(key) || handledTurnErrorKeys.Add(key);
    }

    private static string BuildTurnErrorKey(TianShuSidecarEvent sidecarEvent, string message)
    {
        var normalizedMessage = Normalize(message) ?? "error";
        var normalizedThreadId = Normalize(sidecarEvent.ThreadId) ?? "thread";
        return $"{NormalizeTurnKey(sidecarEvent.TurnId)}::{normalizedThreadId}::{normalizedMessage}";
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void SetRuntimeOverviewText(ref string field, string value, string overviewPropertyName, [CallerMemberName] string? propertyName = null)
    {
        if (!SetField(ref field, value, propertyName))
        {
            return;
        }

        OnPropertyChanged(overviewPropertyName);
        OnPropertyChanged(nameof(HasRuntimeOverviewEntries));
    }

    private void LoadPersistedSidecarEventLog()
    {
        try
        {
            if (!File.Exists(sidecarEventLogFilePath))
            {
                return;
            }

            var json = File.ReadAllText(sidecarEventLogFilePath, Encoding.UTF8);
            PersistedSidecarEventLogSnapshot? snapshot;
            using (var document = JsonDocument.Parse(json))
            {
                snapshot = document.RootElement.ValueKind switch
                {
                    JsonValueKind.Array => new PersistedSidecarEventLogSnapshot
                    {
                        Events = JsonSerializer.Deserialize<List<PersistedSidecarEventEntry>>(document.RootElement.GetRawText(), prettyJsonOptions) ?? [],
                    },
                    JsonValueKind.Object => JsonSerializer.Deserialize<PersistedSidecarEventLogSnapshot>(document.RootElement.GetRawText(), prettyJsonOptions),
                    _ => null,
                };
            }

            var records = snapshot?.Events ?? [];
            var diagnosticRecords = snapshot?.DiagnosticTrail ?? [];
            if (records.Count == 0 && diagnosticRecords.Count == 0)
            {
                DeletePersistedSidecarEventLog();
                return;
            }

            sidecarEvents.Clear();
            sidecarDiagnosticEntries.Clear();
            nextSidecarEventSequence = 1;
            nextSidecarDiagnosticSequence = 1;

            foreach (var record in records.OrderBy(static entry => entry.Sequence <= 0 ? int.MaxValue : entry.Sequence).ThenBy(entry => entry.Timestamp))
            {
                var sequence = record.Sequence > 0 ? record.Sequence : nextSidecarEventSequence;
                sidecarEvents.Add(new SidecarEventEntry(
                    sequence,
                    record.Timestamp,
                    record.EventType ?? string.Empty,
                    record.Summary ?? string.Empty,
                    record.Details ?? string.Empty));
                nextSidecarEventSequence = Math.Max(nextSidecarEventSequence, sequence + 1);
            }

            foreach (var record in diagnosticRecords.OrderBy(static entry => entry.Sequence <= 0 ? int.MaxValue : entry.Sequence).ThenBy(entry => entry.Timestamp))
            {
                var sequence = record.Sequence > 0 ? record.Sequence : nextSidecarDiagnosticSequence;
                sidecarDiagnosticEntries.Add(record.CloneWithSequence(sequence));
                nextSidecarDiagnosticSequence = Math.Max(nextSidecarDiagnosticSequence, sequence + 1);
            }

            SelectedSidecarEvent = sidecarEvents.Count > 0 ? sidecarEvents[sidecarEvents.Count - 1] : null;
        }
        catch
        {
            sidecarEvents.Clear();
            sidecarDiagnosticEntries.Clear();
            SelectedSidecarEvent = null;
            nextSidecarEventSequence = 1;
            nextSidecarDiagnosticSequence = 1;
            DeletePersistedSidecarEventLog();
        }
    }

    private void PersistSidecarEventLog()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(PersistSidecarEventLog);
            return;
        }

        var version = Interlocked.Increment(ref sidecarEventLogPersistVersion);
        CancellationTokenSource cancellation;
        lock (sidecarEventLogPersistGate)
        {
            sidecarEventLogPersistCancellation?.Cancel();
            sidecarEventLogPersistCancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            sidecarEventLogPersistCancellation = cancellation;
            sidecarEventLogPersistTask = PersistSidecarEventLogAsync(version, cancellation.Token);
        }
    }

    private async Task FlushPendingSidecarEventLogPersistAsync()
    {
        if (!Dispatcher.CheckAccess())
        {
            await Dispatcher.InvokeAsync(async () => await FlushPendingSidecarEventLogPersistAsync().ConfigureAwait(true)).Task.Unwrap().ConfigureAwait(true);
            return;
        }

        var version = Interlocked.Increment(ref sidecarEventLogPersistVersion);
        CancellationTokenSource? cancellation = null;
        lock (sidecarEventLogPersistGate)
        {
            cancellation = sidecarEventLogPersistCancellation;
            sidecarEventLogPersistCancellation = null;
            sidecarEventLogPersistTask = Task.CompletedTask;
        }

        if (cancellation is not null)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }

        await PersistSidecarEventLogCoreAsync(version, CancellationToken.None).ConfigureAwait(true);
    }

    private void CancelPendingSidecarEventLogPersist()
    {
        Interlocked.Increment(ref sidecarEventLogPersistVersion);
        lock (sidecarEventLogPersistGate)
        {
            sidecarEventLogPersistCancellation?.Cancel();
            sidecarEventLogPersistCancellation?.Dispose();
            sidecarEventLogPersistCancellation = null;
            sidecarEventLogPersistTask = Task.CompletedTask;
        }
    }

    private async Task PersistSidecarEventLogAsync(int version, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SidecarEventLogPersistDelayMilliseconds, cancellationToken).ConfigureAwait(false);
            await PersistSidecarEventLogCoreAsync(version, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
        }
    }

    private async Task PersistSidecarEventLogCoreAsync(int version, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await CaptureSidecarEventLogSnapshotAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (version != Volatile.Read(ref sidecarEventLogPersistVersion))
            {
                return;
            }

            WriteSidecarEventLogSnapshot(snapshot);
            _ = Dispatcher.InvokeAsync(() => OnPropertyChanged(nameof(CanOpenRuntimeEventLogFile)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
        }
    }

    private async Task<PersistedSidecarEventLogSnapshot?> CaptureSidecarEventLogSnapshotAsync(CancellationToken cancellationToken)
    {
        if (Dispatcher.CheckAccess())
        {
            return BuildSidecarEventLogSnapshot();
        }

        return await Dispatcher
            .InvokeAsync(
                BuildSidecarEventLogSnapshot,
                System.Windows.Threading.DispatcherPriority.Background,
                cancellationToken)
            .Task
            .ConfigureAwait(false);
    }

    private PersistedSidecarEventLogSnapshot? BuildSidecarEventLogSnapshot()
    {
        if (sidecarEvents.Count == 0 && sidecarDiagnosticEntries.Count == 0)
        {
            return null;
        }

        return new PersistedSidecarEventLogSnapshot
        {
            Events =
            [
                .. sidecarEvents.Select(static entry => new PersistedSidecarEventEntry
                {
                    Sequence = entry.Sequence,
                    Timestamp = entry.Timestamp,
                    EventType = entry.EventType,
                    Summary = entry.Summary,
                    Details = entry.Details,
                }),
            ],
            DiagnosticTrail = [.. sidecarDiagnosticEntries.Select(static entry => entry.CloneWithSequence(entry.Sequence))],
        };
    }

    private void WriteSidecarEventLogSnapshot(PersistedSidecarEventLogSnapshot? snapshot)
    {
        try
        {
            if (snapshot is null || (snapshot.Events.Count == 0 && snapshot.DiagnosticTrail.Count == 0))
            {
                DeletePersistedSidecarEventLog();
                return;
            }

            var directory = Path.GetDirectoryName(sidecarEventLogFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(snapshot, prettyJsonOptions);
            var tempPath = $"{sidecarEventLogFilePath}.tmp";

            try
            {
                File.WriteAllText(tempPath, json, Encoding.UTF8);
                if (File.Exists(sidecarEventLogFilePath))
                {
                    File.Replace(tempPath, sidecarEventLogFilePath, null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tempPath, sidecarEventLogFilePath);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
        catch
        {
        }
    }

    private void ClearSidecarEventLog(bool deleteLocalFile)
    {
        CancelPendingSidecarEventLogPersist();
        sidecarEvents.Clear();
        sidecarDiagnosticEntries.Clear();
        SelectedSidecarEvent = null;
        nextSidecarEventSequence = 1;
        nextSidecarDiagnosticSequence = 1;

        if (deleteLocalFile)
        {
            DeletePersistedSidecarEventLog();
        }

        OnPropertyChanged(nameof(CanOpenRuntimeEventLogFile));
    }

    private void DeletePersistedSidecarEventLog()
    {
        try
        {
            if (File.Exists(sidecarEventLogFilePath))
            {
                File.Delete(sidecarEventLogFilePath);
            }
        }
        catch
        {
        }
    }

    private static string ResolveSidecarEventLogFilePath()
    {
        return Path.Combine(ResolveKernelStateRootPath(), "vsix-runtime-events.json");
    }

    private static string ResolveKernelStateRootPath()
        => TianShuDevPathLocator.ResolveTianShuStateRootPath();

    private static string ResolveThreadSessionsDirectoryPath()
        => TianShuDevPathLocator.ResolveTianShuSessionsRootPath();

    private string? ResolveDisplayedThreadFilePath()
    {
        var targetThreadId = GetDisplayedThreadOperationTargetId();
        return string.IsNullOrWhiteSpace(targetThreadId)
            ? null
            : Path.Combine(ResolveThreadSessionsDirectoryPath(), $"{targetThreadId}.jsonl");
    }

    private void OnOpenThreadFileClick(object sender, RoutedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        OpenFileInVisualStudio(ResolveDisplayedThreadFilePath(), "线程文件");
    }

    private void OnOpenThreadSessionsDirectoryClick(object sender, RoutedEventArgs e)
    {
        OpenDirectoryInShell(ResolveThreadSessionsDirectoryPath(), "会话目录");
    }

    private void OnOpenRuntimeEventLogClick(object sender, RoutedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        OpenFileInVisualStudio(sidecarEventLogFilePath, "运行时日志");
    }

    private static void OpenFileInVisualStudio(string? filePath, string description, int? line = null, int? column = null)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            MessageBox.Show($"{description}不存在。", description, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            if (line is null)
            {
                VsShellUtilities.OpenDocument(ServiceProvider.GlobalProvider, filePath);
                return;
            }

            var dte = ServiceProvider.GlobalProvider.GetService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
            if (dte?.ItemOperations.OpenFile(filePath) is EnvDTE.Window window
                && window.Document?.Selection is EnvDTE.TextSelection selection)
            {
                selection.MoveToLineAndOffset(Math.Max(1, line.Value), Math.Max(1, column ?? 1));
                return;
            }
        }
        catch
        {
        }

        VsShellUtilities.OpenDocument(ServiceProvider.GlobalProvider, filePath);
    }

    private static void OpenDirectoryInShell(string? directoryPath, string description)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            MessageBox.Show($"{description}不存在。", description, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        System.Diagnostics.Process.Start(new ProcessStartInfo
        {
            FileName = directoryPath,
            UseShellExecute = true,
        });
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    internal enum BusySendMode
    {
        Queue,
        Steer,
        Interrupt,
    }

    internal enum PendingFollowUpBucket
    {
        QueuedUserMessage,
        PendingSteer,
    }

    public sealed class ThreadListEntry
    {
        public ThreadListEntry(
            string threadId,
            string preview,
            string? name,
            string? cwd,
            DateTimeOffset updatedAt,
            bool isArchived,
            bool isCurrentThread,
            DateTimeOffset? createdAt = null,
            string? agentNickname = null,
            string? agentRole = null,
            string? parentThreadId = null,
            int? threadDepth = null)
        {
            ThreadId = threadId;
            Preview = preview;
            Name = name;
            Cwd = cwd;
            UpdatedAt = updatedAt;
            IsArchived = isArchived;
            IsCurrentThread = isCurrentThread;
            CreatedAt = createdAt;
            AgentNickname = TianShuConversationToolWindowControl.Normalize(agentNickname);
            AgentRole = TianShuConversationToolWindowControl.Normalize(agentRole);
            ParentThreadId = TianShuConversationToolWindowControl.Normalize(parentThreadId);
            ThreadDepth = threadDepth;
        }

        public string ThreadId { get; }

        public string Preview { get; }

        public string? Name { get; }

        public string? Cwd { get; }

        public DateTimeOffset UpdatedAt { get; }

        public bool IsArchived { get; }

        public bool IsCurrentThread { get; }

        public DateTimeOffset? CreatedAt { get; }

        public string? AgentNickname { get; }

        public string? AgentRole { get; }

        public string? ParentThreadId { get; }

        public int? ThreadDepth { get; }

        public bool IsSpawnedSubAgentThread => !string.IsNullOrWhiteSpace(ParentThreadId);

        public string TitleText
        {
            get
            {
                var title = BuildThreadDisplayName(this);
                return IsArchived ? $"[归档] {title}" : title;
            }
        }

        public string MetaText
        {
            get
            {
                var cwdText = string.IsNullOrWhiteSpace(Cwd)
                    ? "未知目录"
                    : Path.GetFileName(Cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(cwdText))
                {
                    cwdText = Cwd ?? "未知目录";
                }

                return $"{UpdatedAt:MM-dd HH:mm} · {cwdText}";
            }
        }

        public string DisplayText
        {
            get
            {
                return $"{TitleText}  |  {UpdatedAt:MM-dd HH:mm}";
            }
        }
    }

    public sealed class CollaborationThreadEntry
    {
        public CollaborationThreadEntry(
            string threadId,
            string titleText,
            string metaText,
            string statusText,
            bool isPrimaryThread,
            bool isCurrentThread,
            bool isArchived)
        {
            ThreadId = threadId;
            TitleText = titleText;
            MetaText = metaText;
            StatusText = statusText;
            IsPrimaryThread = isPrimaryThread;
            IsCurrentThread = isCurrentThread;
            IsArchived = isArchived;
        }

        public string ThreadId { get; }

        public string TitleText { get; }

        public string MetaText { get; }

        public string StatusText { get; }

        public bool IsPrimaryThread { get; }

        public bool IsCurrentThread { get; }

        public bool IsArchived { get; }

        public bool CanJump => !IsCurrentThread;

        public string JumpButtonText => IsCurrentThread ? "当前" : "跳转";
    }

    public sealed class SettingsConfigFieldEntry
    {
        public SettingsConfigFieldEntry(string label, string keyPath, string value, string source)
        {
            Label = label;
            KeyPath = keyPath;
            Value = value;
            Source = source;
        }

        public string Label { get; }

        public string KeyPath { get; }

        public string Value { get; }

        public string Source { get; }
    }

    public sealed class SettingsModelCatalogEntry
    {
        public SettingsModelCatalogEntry(
            string displayName,
            string modelId,
            string defaultReasoningEffort,
            string supportedReasoningEffortsText,
            string inputModalitiesText,
            bool supportsPersonality,
            bool isCurrent,
            string description)
        {
            DisplayName = displayName;
            ModelId = modelId;
            DefaultReasoningEffort = defaultReasoningEffort;
            SupportedReasoningEffortsText = supportedReasoningEffortsText;
            InputModalitiesText = inputModalitiesText;
            SupportsPersonality = supportsPersonality;
            IsCurrent = isCurrent;
            Description = description;
        }

        public string DisplayName { get; }

        public string ModelId { get; }

        public string DefaultReasoningEffort { get; }

        public string SupportedReasoningEffortsText { get; }

        public string InputModalitiesText { get; }

        public bool SupportsPersonality { get; }

        public bool IsCurrent { get; }

        public string Description { get; }
    }

    public sealed class ConversationEntry : INotifyPropertyChanged
    {
        private string content;
        private DateTime? completedSuccessfullyAtUtc;
        private FlowDocument? finalDocument;
        private bool isReasoningExpanded = true;
        private FlowDocument? pendingDraftDocument;
        private DateTime lastFinalRenderUtc = DateTime.MinValue;
        private DateTime lastPendingDraftRenderUtc = DateTime.MinValue;
        private int lastFinalRenderedLength;
        private int lastPendingDraftRenderedLength;
        private string pendingDraftContent = string.Empty;
        private string pendingFinalContent = string.Empty;
        private string pendingStatusText = string.Empty;
        private readonly Dictionary<string, ConversationToolCallEntry> processToolCalls = new(StringComparer.Ordinal);
        private readonly ObservableCollection<ConversationProcessSegment> processSegments = new();
        private string role;
        private readonly DateTime startedAtUtc = DateTime.UtcNow;
        private string variant;

        public ConversationEntry(string role, string content, string? variant = null)
        {
            this.role = role;
            this.content = content;
            this.variant = NormalizeVariant(variant) ?? InferVariant(role);
            processSegments.CollectionChanged += OnProcessSegmentsCollectionChanged;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Role
        {
            get => role;
            set
            {
                if (role == value)
                {
                    return;
                }

                role = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Role)));
                NotifyPropertyChanged(nameof(ShowAssistantPendingText));
                NotifyPropertyChanged(nameof(AssistantPendingText));
                NotifyPropertyChanged(nameof(IsSpacerEntry));
                if (string.IsNullOrWhiteSpace(variant))
                {
                    Variant = InferVariant(value);
                }
            }
        }

        public string Content
        {
            get => content;
            set
            {
                if (content == value)
                {
                    return;
                }

                content = value;
                NotifyPropertyChanged(nameof(Content));
                NotifyPropertyChanged(nameof(DisplayContent));
                NotifyPropertyChanged(nameof(HasFinalContent));
                NotifyPropertyChanged(nameof(ShowAssistantFinalContent));
                NotifyPropertyChanged(nameof(ShowAssistantPendingText));
                NotifyPropertyChanged(nameof(AssistantPendingText));
                NotifyPropertyChanged(nameof(ReasoningHeaderText));
                NotifyPropertyChanged(nameof(ShowReasoningSection));
                NotifyPropertyChanged(nameof(ShowPendingDraftFallback));
                NotifyPropertyChanged(nameof(ShowInlineReasoningSection));
                NotifyPropertyChanged(nameof(ShowCollapsibleReasoningSection));
                NotifyPropertyChanged(nameof(ShowAssistantRunSummary));
                NotifyPropertyChanged(nameof(IsSpacerEntry));
            }
        }

        public string Variant
        {
            get => variant;
            set
            {
                var normalized = NormalizeVariant(value) ?? "assistant";
                if (variant == normalized)
                {
                    return;
                }

                variant = normalized;
                NotifyPropertyChanged(nameof(Variant));
                NotifyPropertyChanged(nameof(DisplayContent));
                NotifyPropertyChanged(nameof(IsAssistantLike));
                NotifyPropertyChanged(nameof(ShowPlainTextContent));
                NotifyPropertyChanged(nameof(ShowAssistantLayout));
                NotifyPropertyChanged(nameof(ShowReasoningSection));
                NotifyPropertyChanged(nameof(ShowAssistantFinalContent));
                NotifyPropertyChanged(nameof(ShowPendingDraftFallback));
                NotifyPropertyChanged(nameof(ShowAssistantPendingText));
                NotifyPropertyChanged(nameof(AssistantPendingText));
                NotifyPropertyChanged(nameof(ShowInlineReasoningSection));
                NotifyPropertyChanged(nameof(ShowCollapsibleReasoningSection));
                NotifyPropertyChanged(nameof(ShowAssistantRunSummary));
            }
        }

        public FlowDocument? FinalDocument
        {
            get => finalDocument;
            set
            {
                if (ReferenceEquals(finalDocument, value))
                {
                    return;
                }

                finalDocument = value;
                NotifyPropertyChanged(nameof(FinalDocument));
            }
        }

        public FlowDocument? PendingDraftDocument
        {
            get => pendingDraftDocument;
            set
            {
                if (ReferenceEquals(pendingDraftDocument, value))
                {
                    return;
                }

                pendingDraftDocument = value;
                NotifyPropertyChanged(nameof(PendingDraftDocument));
            }
        }

        public bool IsReasoningExpanded
        {
            get => isReasoningExpanded;
            set
            {
                if (isReasoningExpanded == value)
                {
                    return;
                }

                isReasoningExpanded = value;
                NotifyPropertyChanged(nameof(IsReasoningExpanded));
            }
        }

        public bool IsAssistantLike => string.Equals(Variant, "assistant", StringComparison.Ordinal)
            || string.Equals(Variant, "progress", StringComparison.Ordinal);

        public bool ShowPlainTextContent => !IsAssistantLike;

        public bool ShowAssistantLayout => IsAssistantLike;

        public bool HasFinalContent => !string.IsNullOrWhiteSpace(Content);

        public bool HasPendingDraftContent => !string.IsNullOrWhiteSpace(pendingDraftContent);

        public string PendingStreamingContent
            => !string.IsNullOrWhiteSpace(pendingDraftContent)
                ? pendingDraftContent
                : pendingFinalContent;

        public bool HasPendingStreamingContent => !string.IsNullOrWhiteSpace(PendingStreamingContent);

        public bool HasPendingFinalContent => !string.IsNullOrWhiteSpace(pendingFinalContent);

        public bool HasPendingStatusText => !string.IsNullOrWhiteSpace(pendingStatusText);

        public ObservableCollection<ConversationProcessSegment> ProcessSegments => processSegments;

        public bool HasReasoningContent => HasPendingDraftContent || processSegments.Any(static segment => segment.HasVisibleContent);

        public bool HasProcessTextContent => HasPendingDraftContent || processSegments.Any(static segment => segment.HasText);

        public bool HasProcessToolCalls => processSegments.Any(static segment => segment.HasToolCalls);

        public bool HasPendingAssistantActivity => HasReasoningContent || HasPendingFinalContent;

        public bool HasVisiblePendingState => HasPendingAssistantActivity || HasPendingStatusText;

        public bool IsSpacerEntry => !HasFinalContent && !HasVisiblePendingState;

        public bool ShowReasoningSection => HasReasoningContent;

        public bool ShowAssistantFinalContent => IsAssistantLike && HasFinalContent;

        public bool ShowPendingDraftFallback
            => IsAssistantLike
               && !ShowAssistantFinalContent
               && HasPendingStreamingContent
               && !processSegments.Any(static segment => segment.HasText);

        public bool ShowInlineReasoningSection
            => !ShowAssistantFinalContent
               && (ShowReasoningSection || ShowPendingDraftFallback);

        public bool ShowCollapsibleReasoningSection
            => ShowReasoningSection
               && ShowAssistantFinalContent
               && !HasRedundantCompletedReasoning;

        public bool ShowAssistantPendingText
            => IsAssistantLike
               && string.Equals(Role, "处理中", StringComparison.Ordinal)
               && !ShowAssistantFinalContent
               && HasVisiblePendingState;

        public bool HasRunSummary => completedSuccessfullyAtUtc.HasValue && completedSuccessfullyAtUtc.Value >= startedAtUtc;

        public bool ShowAssistantRunSummary => ShowAssistantFinalContent && HasRunSummary;

        public string AssistantPendingText
            => !string.IsNullOrWhiteSpace(pendingStatusText)
                ? pendingStatusText
                : HasPendingFinalContent ? "正在整理最终总结…" : "正在思考…";

        public string ReasoningHeaderText => HasFinalContent ? "处理过程" : "处理中";

        public string RunSummaryText => !HasRunSummary
            ? string.Empty
            : $"本次运行 {FormatElapsed(completedSuccessfullyAtUtc!.Value - startedAtUtc)}";

        public string DisplayContent => string.Equals(Variant, "progress", StringComparison.Ordinal)
            ? BuildProgressPreview(Content)
            : Content;

        public string PendingDraftContent => pendingDraftContent;

        private bool HasRedundantCompletedReasoning
            => ShowAssistantFinalContent
               && !HasPendingDraftContent
               && !HasProcessToolCalls
               && processSegments.Any(static segment => segment.HasText)
               && string.Equals(
                   NormalizeComparisonText(string.Concat(processSegments.Select(static segment => segment.Text))),
                   NormalizeComparisonText(Content),
                   StringComparison.Ordinal);

        public void AppendAssistantFinalDelta(string delta, int maxChars)
        {
            SetPendingFinalContent(AppendCappedText(pendingFinalContent, delta, maxChars));
        }

        public void AppendCommentaryDelta(string delta, int maxChars)
        {
            SetPendingDraftContent(AppendCappedText(pendingDraftContent, delta, maxChars));
        }

        public void CommitAssistantFinalContent(string? completedText, int maxChars)
        {
            FlushPendingDraftToProcessSegment(TianShuConversationToolWindowControl.MaxAssistantReasoningChars);
            var finalText = string.IsNullOrWhiteSpace(completedText)
                ? pendingFinalContent
                : NormalizeAssistantContent(completedText!, maxChars);
            SetPendingFinalContent(string.Empty);
            SetPendingStatusText(string.Empty);
            MarkRunCompleted(success: true);
            if (!string.IsNullOrWhiteSpace(finalText))
            {
                Content = finalText;
            }
        }

        public void ClearPendingAssistantFinalContent()
        {
            SetPendingFinalContent(string.Empty);
        }

        public void CompleteInterruptedRun(string? fallbackMessage, int maxChars)
            => CompleteTerminalRun(
                "已中断",
                "progress",
                string.IsNullOrWhiteSpace(fallbackMessage) ? "当前回合已中断。" : fallbackMessage!,
                maxChars);

        public void CompleteFailedRun(string? fallbackMessage, int maxChars)
            => CompleteTerminalRun(
                "已失败",
                "progress",
                string.IsNullOrWhiteSpace(fallbackMessage) ? "当前回合执行失败。" : fallbackMessage!,
                maxChars);

        public void SetPendingStatusText(string? value)
        {
            var normalized = Normalize(value) ?? string.Empty;
            if (pendingStatusText == normalized)
            {
                return;
            }

            pendingStatusText = normalized;
            NotifyPropertyChanged(nameof(HasPendingStatusText));
            NotifyPropertyChanged(nameof(HasVisiblePendingState));
            NotifyPropertyChanged(nameof(ShowAssistantPendingText));
            NotifyPropertyChanged(nameof(AssistantPendingText));
            NotifyPropertyChanged(nameof(IsSpacerEntry));
        }

        public void AppendProcessDelta(string delta, int maxChars)
        {
            var normalized = Normalize(delta);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            var segment = GetOrCreateWritableProcessSegment();
            segment.AppendText(normalized!, maxChars);
            NotifyPropertyChanged(nameof(HasReasoningContent));
            NotifyPropertyChanged(nameof(HasProcessTextContent));
            NotifyPropertyChanged(nameof(ShowReasoningSection));
            NotifyPropertyChanged(nameof(ShowPendingDraftFallback));
            NotifyPropertyChanged(nameof(ShowInlineReasoningSection));
            NotifyPropertyChanged(nameof(ShowCollapsibleReasoningSection));
            NotifyPropertyChanged(nameof(ShowAssistantPendingText));
            NotifyPropertyChanged(nameof(AssistantPendingText));
            NotifyPropertyChanged(nameof(ReasoningHeaderText));
            NotifyPropertyChanged(nameof(IsSpacerEntry));
        }

        public bool SuppressTrailingPlanNarration(int planStepCount)
        {
            var changed = false;
            if (TryTrimTrailingPlanNarration(pendingDraftContent, planStepCount, out var trimmedDraft)
                && !string.Equals(trimmedDraft, pendingDraftContent, StringComparison.Ordinal))
            {
                SetPendingDraftContent(trimmedDraft);
                changed = true;
            }

            for (var index = processSegments.Count - 1; index >= 0; index--)
            {
                if (!processSegments[index].TryTrimTrailingPlanNarration(planStepCount))
                {
                    break;
                }

                if (!processSegments[index].HasVisibleContent)
                {
                    processSegments.RemoveAt(index);
                }

                changed = true;
                break;
            }

            if (changed)
            {
                NotifyProcessStateChanged();
            }

            return changed;
        }

        public void FlushPendingDraftToProcessSegment(int maxChars)
        {
            if (!HasPendingDraftContent)
            {
                return;
            }

            var segment = GetOrCreateWritableProcessSegment();
            segment.AppendText(pendingDraftContent, maxChars);
            SetPendingDraftContent(string.Empty);
        }

        public void UpsertProcessToolCall(
            string key,
            string toolName,
            string status,
            string? details,
            string? sourceMethod)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (!processToolCalls.TryGetValue(key, out var toolCall))
            {
                var segment = GetOrCreateCurrentProcessSegment();
                toolCall = new ConversationToolCallEntry(key);
                processToolCalls[key] = toolCall;
                segment.ToolCalls.Add(toolCall);
            }

            toolCall.ToolName = string.IsNullOrWhiteSpace(toolName) ? toolCall.ToolName : toolName;
            toolCall.Status = string.IsNullOrWhiteSpace(status) ? toolCall.Status : status;
            toolCall.SourceMethod = string.IsNullOrWhiteSpace(sourceMethod) ? toolCall.SourceMethod : sourceMethod;
            if (!string.IsNullOrWhiteSpace(details))
            {
                toolCall.AppendOutput(details!);
            }

            NotifyPropertyChanged(nameof(HasReasoningContent));
            NotifyPropertyChanged(nameof(ShowReasoningSection));
            NotifyPropertyChanged(nameof(ShowInlineReasoningSection));
            NotifyPropertyChanged(nameof(ShowCollapsibleReasoningSection));
            NotifyPropertyChanged(nameof(ShowAssistantPendingText));
            NotifyPropertyChanged(nameof(AssistantPendingText));
            NotifyPropertyChanged(nameof(ReasoningHeaderText));
            NotifyPropertyChanged(nameof(IsSpacerEntry));
        }

        public bool ShouldRefreshFinalDocument(bool force)
        {
            if (!HasFinalContent)
            {
                return false;
            }

            if (force || FinalDocument is null)
            {
                return true;
            }

            if (Content.Length - lastFinalRenderedLength >= 120)
            {
                return true;
            }

            if (Content.EndsWith("\n", StringComparison.Ordinal)
                || DateTime.UtcNow - lastFinalRenderUtc >= TimeSpan.FromMilliseconds(250))
            {
                return true;
            }

            return false;
        }

        public bool ShouldRefreshPendingDraftDocument(bool force)
        {
            if (!ShowPendingDraftFallback)
            {
                return false;
            }

            if (force || PendingDraftDocument is null)
            {
                return true;
            }

            if (pendingDraftContent.Length - lastPendingDraftRenderedLength >= 120)
            {
                return true;
            }

            if (pendingDraftContent.EndsWith("\n", StringComparison.Ordinal)
                || DateTime.UtcNow - lastPendingDraftRenderUtc >= TimeSpan.FromMilliseconds(250))
            {
                return true;
            }

            return false;
        }

        private ConversationProcessSegment GetOrCreateWritableProcessSegment()
        {
            if (processSegments.Count == 0)
            {
                return CreateProcessSegment();
            }

            var last = processSegments[processSegments.Count - 1];
            if (last.HasToolCalls)
            {
                return CreateProcessSegment();
            }

            return last;
        }

        private ConversationProcessSegment GetOrCreateCurrentProcessSegment()
        {
            if (processSegments.Count == 0)
            {
                return CreateProcessSegment();
            }

            return processSegments[processSegments.Count - 1];
        }

        private ConversationProcessSegment CreateProcessSegment()
        {
            var segment = new ConversationProcessSegment();
            processSegments.Add(segment);
            return segment;
        }

        public void MarkFinalDocumentRendered()
        {
            lastFinalRenderedLength = Content.Length;
            lastFinalRenderUtc = DateTime.UtcNow;
        }

        public void MarkPendingDraftDocumentRendered()
        {
            lastPendingDraftRenderedLength = pendingDraftContent.Length;
            lastPendingDraftRenderUtc = DateTime.UtcNow;
        }

        private void MarkRunCompleted(bool success)
        {
            completedSuccessfullyAtUtc = success ? DateTime.UtcNow : null;
            NotifyPropertyChanged(nameof(HasRunSummary));
            NotifyPropertyChanged(nameof(RunSummaryText));
            NotifyPropertyChanged(nameof(ShowAssistantRunSummary));
        }

        private void CompleteTerminalRun(string role, string variant, string fallbackMessage, int maxChars)
        {
            FlushPendingDraftToProcessSegment(TianShuConversationToolWindowControl.MaxAssistantReasoningChars);
            Role = role;
            Variant = variant;
            ClearPendingAssistantFinalContent();
            SetPendingStatusText(string.Empty);
            MarkRunCompleted(success: false);
            if (!HasFinalContent)
            {
                Content = NormalizeAssistantContent(fallbackMessage, maxChars);
            }
        }

        private void SetPendingDraftContent(string value)
        {
            if (pendingDraftContent == value)
            {
                return;
            }

            pendingDraftContent = value;
            if (string.IsNullOrWhiteSpace(value))
            {
                PendingDraftDocument = null;
                lastPendingDraftRenderedLength = 0;
                lastPendingDraftRenderUtc = DateTime.MinValue;
            }

            NotifyPropertyChanged(nameof(HasPendingDraftContent));
            NotifyPropertyChanged(nameof(PendingStreamingContent));
            NotifyPropertyChanged(nameof(HasPendingStreamingContent));
            NotifyPropertyChanged(nameof(HasPendingAssistantActivity));
            NotifyPropertyChanged(nameof(HasReasoningContent));
            NotifyPropertyChanged(nameof(HasProcessTextContent));
            NotifyPropertyChanged(nameof(ShowReasoningSection));
            NotifyPropertyChanged(nameof(ShowPendingDraftFallback));
            NotifyPropertyChanged(nameof(ShowInlineReasoningSection));
            NotifyPropertyChanged(nameof(ShowCollapsibleReasoningSection));
            NotifyPropertyChanged(nameof(ShowAssistantPendingText));
            NotifyPropertyChanged(nameof(AssistantPendingText));
            NotifyPropertyChanged(nameof(IsSpacerEntry));
        }

        private void OnProcessSegmentsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
            => NotifyProcessStateChanged();

        private void NotifyProcessStateChanged()
        {
            NotifyPropertyChanged(nameof(HasReasoningContent));
            NotifyPropertyChanged(nameof(HasProcessTextContent));
            NotifyPropertyChanged(nameof(ShowReasoningSection));
            NotifyPropertyChanged(nameof(ShowPendingDraftFallback));
            NotifyPropertyChanged(nameof(ShowInlineReasoningSection));
            NotifyPropertyChanged(nameof(ShowCollapsibleReasoningSection));
            NotifyPropertyChanged(nameof(ShowAssistantPendingText));
            NotifyPropertyChanged(nameof(AssistantPendingText));
            NotifyPropertyChanged(nameof(ReasoningHeaderText));
            NotifyPropertyChanged(nameof(IsSpacerEntry));
        }

        private void SetPendingFinalContent(string value)
        {
            if (pendingFinalContent == value)
            {
                return;
            }

            pendingFinalContent = value;
            NotifyPropertyChanged(nameof(HasPendingFinalContent));
            NotifyPropertyChanged(nameof(PendingStreamingContent));
            NotifyPropertyChanged(nameof(HasPendingStreamingContent));
            NotifyPropertyChanged(nameof(HasPendingAssistantActivity));
            NotifyPropertyChanged(nameof(ShowReasoningSection));
            NotifyPropertyChanged(nameof(ShowPendingDraftFallback));
            NotifyPropertyChanged(nameof(ShowInlineReasoningSection));
            NotifyPropertyChanged(nameof(ShowCollapsibleReasoningSection));
            NotifyPropertyChanged(nameof(ShowAssistantPendingText));
            NotifyPropertyChanged(nameof(AssistantPendingText));
            NotifyPropertyChanged(nameof(IsSpacerEntry));
        }

        public static string NormalizeAssistantContent(string value, int maxChars)
            => CapText(value ?? string.Empty, maxChars);

        private static string NormalizeComparisonText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                if (!char.IsWhiteSpace(ch))
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString();
        }

        private static string InferVariant(string role)
        {
            return role switch
            {
                "你" => "user",
                "系统" => "system",
                "处理中" => "progress",
                "已中断" => "progress",
                "已失败" => "progress",
                _ => "assistant",
            };
        }

        private static string? NormalizeVariant(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Trim().ToLowerInvariant();
        }

        private static string AppendCappedText(string current, string delta, int maxChars)
            => CapText(TianShuConversationToolWindowControl.MergeStreamingText(current, delta), maxChars);

        private static string CapText(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            {
                return text;
            }

            const string truncatedPrefix = "[已省略较早内容]\n";
            var keepLength = Math.Max(0, maxChars - truncatedPrefix.Length);
            return truncatedPrefix + text.Substring(text.Length - keepLength, keepLength);
        }

        private static string BuildProgressPreview(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            const int maxLength = 720;
            return value.Length <= maxLength
                ? value
                : $"…{value.Substring(value.Length - maxLength, maxLength)}";
        }

        private static string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed.TotalHours >= 1)
            {
                return $"{elapsed.TotalHours:F1} 小时";
            }

            if (elapsed.TotalMinutes >= 1)
            {
                return $"{elapsed.TotalMinutes:F1} 分钟";
            }

            return $"{Math.Max(elapsed.TotalSeconds, 0.1):F1} 秒";
        }

        private void NotifyPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed class ConversationProcessSegment : INotifyPropertyChanged
    {
        private FlowDocument? inlineDocument;
        private FlowDocument? collapsibleDocument;
        private bool isToolCallsExpanded;
        private DateTime lastRenderUtc = DateTime.MinValue;
        private int lastRenderedLength;
        private string text = string.Empty;

        public ConversationProcessSegment()
        {
            ToolCalls.CollectionChanged += OnToolCallsCollectionChanged;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<ConversationToolCallEntry> ToolCalls { get; } = new();

        public string Text
        {
            get => text;
            private set
            {
                if (text == value)
                {
                    return;
                }

                text = value;
                NotifyPropertyChanged(nameof(Text));
                NotifyPropertyChanged(nameof(HasText));
                NotifyPropertyChanged(nameof(HasVisibleContent));
            }
        }

        public FlowDocument? InlineDocument
        {
            get => inlineDocument;
            set
            {
                if (ReferenceEquals(inlineDocument, value))
                {
                    return;
                }

                inlineDocument = value;
                NotifyPropertyChanged(nameof(Document));
                NotifyPropertyChanged(nameof(InlineDocument));
            }
        }

        public FlowDocument? Document
        {
            get => InlineDocument;
            set => InlineDocument = value;
        }

        public FlowDocument? CollapsibleDocument
        {
            get => collapsibleDocument;
            set
            {
                if (ReferenceEquals(collapsibleDocument, value))
                {
                    return;
                }

                collapsibleDocument = value;
                NotifyPropertyChanged(nameof(MirrorDocument));
                NotifyPropertyChanged(nameof(CollapsibleDocument));
            }
        }

        public FlowDocument? MirrorDocument
        {
            get => CollapsibleDocument;
            set => CollapsibleDocument = value;
        }

        public bool HasText => !string.IsNullOrWhiteSpace(Text);

        public bool HasToolCalls => ToolCalls.Count > 0;

        public bool HasVisibleContent => HasText || HasToolCalls;

        public bool IsToolCallsExpanded
        {
            get => isToolCallsExpanded;
            set
            {
                if (isToolCallsExpanded == value)
                {
                    return;
                }

                isToolCallsExpanded = value;
                NotifyPropertyChanged(nameof(IsToolCallsExpanded));
            }
        }

        public string ToolCallsHeaderText
            => ToolCalls.Count switch
            {
                0 => string.Empty,
                1 => $"已运行 {ToolCalls[0].DisplayName}",
                _ => $"已运行 {ToolCalls.Count} 个工具",
            };

        public void AppendText(string delta, int maxChars)
        {
            Text = ConversationEntry.NormalizeAssistantContent(
                TianShuConversationToolWindowControl.MergeStreamingText(Text, delta),
                maxChars);
        }

        public bool TryTrimTrailingPlanNarration(int planStepCount)
        {
            if (!TianShuConversationToolWindowControl.TryTrimTrailingPlanNarration(Text, planStepCount, out var trimmed)
                || string.Equals(trimmed, Text, StringComparison.Ordinal))
            {
                return false;
            }

            Text = trimmed;
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                InlineDocument = null;
                CollapsibleDocument = null;
                lastRenderedLength = 0;
                lastRenderUtc = DateTime.MinValue;
            }

            return true;
        }

        public bool ShouldRefreshDocument(bool force)
        {
            if (!HasText)
            {
                return false;
            }

            if (force || CollapsibleDocument is null)
            {
                return true;
            }

            if (Text.Length - lastRenderedLength >= 120)
            {
                return true;
            }

            if (Text.EndsWith("\n", StringComparison.Ordinal)
                || DateTime.UtcNow - lastRenderUtc >= TimeSpan.FromMilliseconds(250))
            {
                return true;
            }

            return false;
        }

        public void MarkDocumentRendered()
        {
            lastRenderedLength = Text.Length;
            lastRenderUtc = DateTime.UtcNow;
        }

        private void OnToolCallsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            NotifyPropertyChanged(nameof(HasToolCalls));
            NotifyPropertyChanged(nameof(HasVisibleContent));
            NotifyPropertyChanged(nameof(ToolCallsHeaderText));
        }

        private void NotifyPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed class ConversationToolCallEntry : INotifyPropertyChanged
    {
        private FlowDocument? inlineOutputDocument;
        private FlowDocument? collapsibleOutputDocument;
        private DateTime lastRenderUtc = DateTime.MinValue;
        private int lastRenderedLength;
        private string outputText = string.Empty;
        private string sourceMethod = string.Empty;
        private string status = "started";
        private string toolName = "tool";

        public ConversationToolCallEntry(string key)
        {
            Key = key;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Key { get; }

        public string ToolName
        {
            get => toolName;
            set
            {
                if (toolName == value)
                {
                    return;
                }

                toolName = value;
                NotifyPropertyChanged(nameof(ToolName));
                NotifyPropertyChanged(nameof(DisplayName));
            }
        }

        public string Status
        {
            get => status;
            set
            {
                if (status == value)
                {
                    return;
                }

                status = value;
                NotifyPropertyChanged(nameof(Status));
                NotifyPropertyChanged(nameof(StatusText));
                NotifyPropertyChanged(nameof(DisplayName));
            }
        }

        public string SourceMethod
        {
            get => sourceMethod;
            set
            {
                if (sourceMethod == value)
                {
                    return;
                }

                sourceMethod = value;
                NotifyPropertyChanged(nameof(SourceMethod));
                NotifyPropertyChanged(nameof(HasSourceMethod));
            }
        }

        public string OutputText
        {
            get => outputText;
            private set
            {
                if (outputText == value)
                {
                    return;
                }

                outputText = value;
                NotifyPropertyChanged(nameof(OutputText));
                NotifyPropertyChanged(nameof(HasOutputText));
            }
        }

        public FlowDocument? InlineOutputDocument
        {
            get => inlineOutputDocument;
            set
            {
                if (ReferenceEquals(inlineOutputDocument, value))
                {
                    return;
                }

                inlineOutputDocument = value;
                NotifyPropertyChanged(nameof(OutputDocument));
                NotifyPropertyChanged(nameof(InlineOutputDocument));
            }
        }

        public FlowDocument? OutputDocument
        {
            get => InlineOutputDocument;
            set => InlineOutputDocument = value;
        }

        public FlowDocument? CollapsibleOutputDocument
        {
            get => collapsibleOutputDocument;
            set
            {
                if (ReferenceEquals(collapsibleOutputDocument, value))
                {
                    return;
                }

                collapsibleOutputDocument = value;
                NotifyPropertyChanged(nameof(MirrorOutputDocument));
                NotifyPropertyChanged(nameof(CollapsibleOutputDocument));
            }
        }

        public FlowDocument? MirrorOutputDocument
        {
            get => CollapsibleOutputDocument;
            set => CollapsibleOutputDocument = value;
        }

        public bool HasOutputText => !string.IsNullOrWhiteSpace(OutputText);

        public bool HasSourceMethod => !string.IsNullOrWhiteSpace(SourceMethod);

        public string StatusText => status switch
        {
            "completed" or "finished" => "已完成",
            "failed" or "errored" => "失败",
            "cancelled" => "已取消",
            "delta" or "updated" => "运行中",
            _ => "已启动",
        };

        public string DisplayName => TianShuConversationToolWindowControl.FormatToolDisplayName(ToolName);

        public void AppendOutput(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            OutputText = TianShuConversationToolWindowControl.MergeStreamingText(OutputText, text, Environment.NewLine);
        }

        public bool ShouldRefreshDocument(bool force)
        {
            if (!HasOutputText)
            {
                return false;
            }

            if (force || CollapsibleOutputDocument is null)
            {
                return true;
            }

            if (OutputText.Length - lastRenderedLength >= 120)
            {
                return true;
            }

            if (OutputText.EndsWith("\n", StringComparison.Ordinal)
                || DateTime.UtcNow - lastRenderUtc >= TimeSpan.FromMilliseconds(250))
            {
                return true;
            }

            return false;
        }

        public void MarkDocumentRendered()
        {
            lastRenderedLength = OutputText.Length;
            lastRenderUtc = DateTime.UtcNow;
        }

        private void NotifyPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed class ConversationPlanStepEntry
    {
        public ConversationPlanStepEntry(int sequence, string step, string status)
        {
            Sequence = sequence;
            Step = step;
            Status = status;
        }

        public int Sequence { get; }

        public string Step { get; }

        public string Status { get; }

        public string DisplayText => $"{Sequence}. {TianShuConversationToolWindowControl.LocalizePlanStep(Step)}";

        public bool IsCompleted => string.Equals(Status, "completed", StringComparison.Ordinal);

        public bool IsInProgress => string.Equals(Status, "in_progress", StringComparison.Ordinal);

        public string StatusGlyph => Status switch
        {
            "completed" => "☑",
            "in_progress" => "◐",
            _ => "☐",
        };

        public string StatusText => Status switch
        {
            "completed" => "已完成",
            "in_progress" => "进行中",
            _ => "待执行",
        };
    }

    public sealed class TianShuApprovalDecisionOption
    {
        public TianShuApprovalDecisionOption(TianShuSidecarApprovalDecisionOptionPayload payload)
        {
            Payload = payload;
        }

        public TianShuSidecarApprovalDecisionOptionPayload Payload { get; }

        public TianShuApprovalDecision Decision => Payload.Decision;

        public string DisplayName
            => Decision switch
            {
                TianShuApprovalDecision.Accept => "接受",
                TianShuApprovalDecision.AcceptForSession => "本次会话接受",
                TianShuApprovalDecision.AcceptAndRemember => "接受并记住",
                TianShuApprovalDecision.AcceptWithExecPolicyAmendment => BuildExecPolicyDisplayName(),
                TianShuApprovalDecision.ApplyNetworkPolicyAmendment => BuildNetworkPolicyDisplayName(),
                TianShuApprovalDecision.Cancel => "取消",
                _ => "拒绝",
            };

        private string BuildExecPolicyDisplayName()
        {
            if (Payload.ExecPolicyAmendment?.CommandPrefix is not { Length: > 0 } commandPrefix)
            {
                return "接受并放行相似命令";
            }

            return $"接受并放行相似命令：{string.Join(" ", commandPrefix)}";
        }

        private string BuildNetworkPolicyDisplayName()
        {
            var host = Normalize(Payload.NetworkPolicyAmendment?.Host) ?? "network";
            var action = Normalize(Payload.NetworkPolicyAmendment?.Action);
            return string.Equals(action, "deny", StringComparison.OrdinalIgnoreCase)
                ? $"拒绝并写入网络规则：{host}"
                : $"接受并写入网络规则：{host}";
        }
    }

    public sealed class TianShuPermissionScopeOption
    {
        public TianShuPermissionScopeOption(TianShuPermissionGrantScope scope)
        {
            Scope = scope;
        }

        public TianShuPermissionGrantScope Scope { get; }

        public string DisplayName => Scope == TianShuPermissionGrantScope.Session ? "会话级" : "当前回合";
    }

    private sealed class ThreadInputStateSnapshot
    {
        public ThreadInputStateSnapshot(
            string inputText,
            BusySendMode busySendMode,
            IReadOnlyList<PendingContextEntryState> pendingContextEntries,
            IReadOnlyList<PendingFollowUpEntryState> pendingFollowUpEntries,
            bool submitPendingSteersAfterInterrupt)
        {
            InputText = inputText;
            BusySendMode = busySendMode;
            PendingContextEntries = pendingContextEntries;
            PendingFollowUpEntries = pendingFollowUpEntries;
            SubmitPendingSteersAfterInterrupt = submitPendingSteersAfterInterrupt;
        }

        public string InputText { get; }

        public BusySendMode BusySendMode { get; }

        public IReadOnlyList<PendingContextEntryState> PendingContextEntries { get; }

        public IReadOnlyList<PendingFollowUpEntryState> PendingFollowUpEntries { get; }

        public bool SubmitPendingSteersAfterInterrupt { get; }
    }

    private sealed class PendingContextEntryState
    {
        public PendingContextEntryState(string kind, string filePath, string content)
        {
            Kind = kind;
            FilePath = filePath;
            Content = content;
        }

        public string Kind { get; }

        public string FilePath { get; }

        public string Content { get; }
    }

    private sealed class PendingFollowUpEntryState
    {
        public PendingFollowUpEntryState(
            string message,
            BusySendMode requestedMode,
            PendingFollowUpBucket pendingBucket,
            IReadOnlyList<TianShuSidecarUserInputPayload> inputs,
            IReadOnlyList<PendingContextEntryState> contextEntries,
            bool isAwaitingCommit,
            bool isAwaitingTurnSteer,
            string? compareMessage,
            int imageCount,
            string? correlationId)
        {
            Message = message;
            RequestedMode = requestedMode;
            PendingBucket = pendingBucket;
            Inputs = inputs;
            ContextEntries = contextEntries;
            IsAwaitingCommit = isAwaitingCommit;
            IsAwaitingTurnSteer = isAwaitingTurnSteer;
            CompareMessage = compareMessage;
            ImageCount = imageCount;
            CorrelationId = correlationId;
        }

        public string Message { get; }

        public BusySendMode RequestedMode { get; }

        public PendingFollowUpBucket PendingBucket { get; }

        public IReadOnlyList<TianShuSidecarUserInputPayload> Inputs { get; }

        public IReadOnlyList<PendingContextEntryState> ContextEntries { get; }

        public bool IsAwaitingCommit { get; }

        public bool IsAwaitingTurnSteer { get; }

        public string? CompareMessage { get; }

        public int ImageCount { get; }

        public string? CorrelationId { get; }
    }

    private sealed class PendingInteractiveRequestEntry
    {
        public PendingInteractiveRequestEntry(
            long requestId,
            string requestKind,
            string callId,
            string? threadId,
            string? turnId,
            TianShuSidecarEvent sidecarEvent,
            int sequence,
            bool isAwaitingResolution)
        {
            RequestId = requestId;
            RequestKind = requestKind;
            CallId = callId;
            ThreadId = threadId;
            TurnId = turnId;
            Event = sidecarEvent;
            Sequence = sequence;
            IsAwaitingResolution = isAwaitingResolution;
        }

        public long RequestId { get; private set; }

        public string RequestKind { get; }

        public string CallId { get; }

        public string? ThreadId { get; private set; }

        public string? TurnId { get; private set; }

        public TianShuSidecarEvent Event { get; private set; }

        public int Sequence { get; }

        public bool IsAwaitingResolution { get; set; }

        public void Update(
            TianShuSidecarEvent sidecarEvent,
            long requestId,
            string? threadId,
            string? turnId,
            bool isAwaitingResolution)
        {
            if (requestId > 0)
            {
                RequestId = requestId;
            }

            ThreadId = threadId;
            TurnId = turnId;
            Event = sidecarEvent;
            IsAwaitingResolution = isAwaitingResolution;
        }
    }

    private readonly struct PendingInteractiveRequestResolution
    {
        public PendingInteractiveRequestResolution(string requestKind, bool promotedNext, bool resolvedActive)
        {
            RequestKind = requestKind;
            PromotedNext = promotedNext;
            ResolvedActive = resolvedActive;
        }

        public string RequestKind { get; }

        public bool PromotedNext { get; }

        public bool ResolvedActive { get; }

        public bool Resolved => !string.IsNullOrWhiteSpace(RequestKind);

        public static PendingInteractiveRequestResolution None => new(string.Empty, false, false);
    }

    public sealed class PendingContextEntry
    {
        private PendingContextEntry(string kind, string filePath, string content)
        {
            Kind = kind;
            FilePath = filePath;
            Content = content;
        }

        public string Kind { get; }

        public string FilePath { get; }

        public string Content { get; }

        public string DisplayText => $"{Kind} | {Path.GetFileName(FilePath)} | {Content.Length} chars";

        public static PendingContextEntry CreateFile(string filePath, string content)
            => new("当前文件", filePath, content);

        public static PendingContextEntry CreateSelection(string filePath, string content)
            => new("当前选区", filePath, content);

        public static PendingContextEntry CreateSpecifiedFile(string filePath, string content)
            => new("指定文件", filePath, content);
    }

    public sealed class PendingFollowUpEntry : INotifyPropertyChanged
    {
        private BusySendMode requestedMode;
        private PendingFollowUpBucket pendingBucket;
        private bool isAwaitingCommit;
        private bool isAwaitingTurnSteer;
        private string? awaitingTurnSteerCompareMessage;
        private int awaitingTurnSteerImageCount;
        private string? correlationId;
        private IReadOnlyList<TianShuSidecarUserInputPayload> inputs;

        internal PendingFollowUpEntry(
            string message,
            BusySendMode requestedMode,
            IReadOnlyList<TianShuSidecarUserInputPayload> inputs,
            IReadOnlyList<PendingContextEntry> contextEntries,
            PendingFollowUpBucket pendingBucket = PendingFollowUpBucket.QueuedUserMessage)
        {
            Message = message;
            this.requestedMode = NormalizeRequestedMode(requestedMode);
            this.inputs = CloneUserInputs(inputs);
            this.pendingBucket = pendingBucket;
            ContextEntries = contextEntries;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Message { get; }

        public IReadOnlyList<PendingContextEntry> ContextEntries { get; }

        internal IReadOnlyList<TianShuSidecarUserInputPayload> Inputs => inputs;

        internal BusySendMode RequestedMode => requestedMode;

        internal PendingFollowUpBucket PendingBucket => pendingBucket;

        internal string? CompareMessage => awaitingTurnSteerCompareMessage;

        internal int CompareImageCount => awaitingTurnSteerImageCount;

        internal string? CorrelationId => correlationId;

        internal bool IsSteer
        {
            get => requestedMode == BusySendMode.Steer;
        }

        internal bool IsInterrupt
        {
            get => requestedMode == BusySendMode.Interrupt;
        }

        public bool IsAwaitingCommit
        {
            get => isAwaitingCommit;
            private set
            {
                if (isAwaitingCommit == value)
                {
                    return;
                }

                isAwaitingCommit = value;
                NotifyPropertyChanged(nameof(IsAwaitingCommit));
                NotifyPropertyChanged(nameof(ModeText));
                NotifyPropertyChanged(nameof(CanDelete));
                NotifyPropertyChanged(nameof(CanPromoteToSteer));
                NotifyPropertyChanged(nameof(PromoteActionText));
            }
        }

        public bool IsAwaitingTurnSteer
        {
            get => isAwaitingTurnSteer;
            private set
            {
                if (isAwaitingTurnSteer == value)
                {
                    return;
                }

                isAwaitingTurnSteer = value;
                NotifyPropertyChanged(nameof(IsAwaitingTurnSteer));
                NotifyPropertyChanged(nameof(ModeText));
                NotifyPropertyChanged(nameof(CanDelete));
            }
        }

        public string ModeText => IsAwaitingTurnSteer
            ? "等待引导"
            : IsAwaitingCommit
                ? IsInterrupt
                    ? "等待中断"
                    : "等待发送"
            : IsInterrupt
                ? "中断跟进"
                : IsSteer
                    ? "引导优先"
                    : "排队跟进";

        public string ContextSummary => ContextEntries.Count == 0
            ? "无附加上下文"
            : $"附带 {ContextEntries.Count} 条 IDE 上下文";

        public bool CanPromoteToSteer => !IsSteer && !IsAwaitingCommit;

        public string PromoteActionText => IsAwaitingCommit ? ModeText : IsSteer ? "等待引导" : "引导";

        public bool CanDelete => !IsAwaitingCommit;

        internal void MarkAsSteer()
            => SetRequestedMode(BusySendMode.Steer);

        internal void MarkAsQueue()
        {
            SetPendingBucket(PendingFollowUpBucket.QueuedUserMessage);
            SetRequestedMode(BusySendMode.Queue);
        }

        internal void MarkAsAwaitingCommit(
            string? compareMessage,
            int imageCount,
            string? correlationId = null,
            bool isTurnSteer = false,
            IReadOnlyList<TianShuSidecarUserInputPayload>? inputs = null)
        {
            awaitingTurnSteerCompareMessage = Normalize(compareMessage);
            awaitingTurnSteerImageCount = imageCount;
            this.correlationId = Normalize(correlationId);
            this.inputs = CloneUserInputs(inputs);
            if (isTurnSteer)
            {
                SetPendingBucket(PendingFollowUpBucket.PendingSteer);
            }

            IsAwaitingCommit = true;
            IsAwaitingTurnSteer = isTurnSteer;
        }

        internal bool MatchesAwaitingTurnSteerCorrelation(string? correlationId)
        {
            if (!IsAwaitingTurnSteer)
            {
                return false;
            }

            var normalizedCorrelationId = Normalize(correlationId);
            return !string.IsNullOrWhiteSpace(this.correlationId)
                && !string.IsNullOrWhiteSpace(normalizedCorrelationId)
                && string.Equals(this.correlationId, normalizedCorrelationId, StringComparison.Ordinal);
        }

        internal bool MatchesAwaitingTurnSteerCompareKey(string? committedMessage, int imageCount)
        {
            if (!IsAwaitingTurnSteer)
            {
                return false;
            }

            var normalizedCommittedMessage = Normalize(committedMessage);
            return !string.IsNullOrWhiteSpace(awaitingTurnSteerCompareMessage)
                && !string.IsNullOrWhiteSpace(normalizedCommittedMessage)
                && awaitingTurnSteerImageCount == imageCount
                && string.Equals(awaitingTurnSteerCompareMessage, normalizedCommittedMessage, StringComparison.Ordinal);
        }

        internal bool MatchesAwaitingTurnSteerInputs(IReadOnlyList<TianShuSidecarUserInputPayload>? committedInputs)
            => IsAwaitingTurnSteer && AreEquivalentUserInputs(inputs, committedInputs);

        internal bool MatchesAwaitingCommitCorrelation(string? correlationId)
        {
            if (!IsAwaitingCommit)
            {
                return false;
            }

            var normalizedCorrelationId = Normalize(correlationId);
            return !string.IsNullOrWhiteSpace(this.correlationId)
                && !string.IsNullOrWhiteSpace(normalizedCorrelationId)
                && string.Equals(this.correlationId, normalizedCorrelationId, StringComparison.Ordinal);
        }

        internal bool MatchesAwaitingCommitCompareKey(string? committedMessage, int imageCount)
        {
            if (!IsAwaitingCommit)
            {
                return false;
            }

            var normalizedCommittedMessage = Normalize(committedMessage);
            return !string.IsNullOrWhiteSpace(awaitingTurnSteerCompareMessage)
                && !string.IsNullOrWhiteSpace(normalizedCommittedMessage)
                && awaitingTurnSteerImageCount == imageCount
                && string.Equals(awaitingTurnSteerCompareMessage, normalizedCommittedMessage, StringComparison.Ordinal);
        }

        internal bool MatchesAwaitingCommitInputs(IReadOnlyList<TianShuSidecarUserInputPayload>? committedInputs)
            => IsAwaitingCommit && AreEquivalentUserInputs(inputs, committedInputs);

        internal bool MatchesCorrelation(string? correlationId)
        {
            var normalizedCorrelationId = Normalize(correlationId);
            return !string.IsNullOrWhiteSpace(this.correlationId)
                && !string.IsNullOrWhiteSpace(normalizedCorrelationId)
                && string.Equals(this.correlationId, normalizedCorrelationId, StringComparison.Ordinal);
        }

        internal void ClearAwaitingCommit()
        {
            awaitingTurnSteerCompareMessage = null;
            awaitingTurnSteerImageCount = 0;
            correlationId = null;
            IsAwaitingCommit = false;
            IsAwaitingTurnSteer = false;
        }

        private void SetRequestedMode(BusySendMode mode)
        {
            var normalizedMode = NormalizeRequestedMode(mode);
            if (requestedMode == normalizedMode)
            {
                return;
            }

            requestedMode = normalizedMode;
            NotifyPropertyChanged(nameof(IsSteer));
            NotifyPropertyChanged(nameof(IsInterrupt));
            NotifyPropertyChanged(nameof(ModeText));
            NotifyPropertyChanged(nameof(CanPromoteToSteer));
            NotifyPropertyChanged(nameof(PromoteActionText));
        }

        private void SetPendingBucket(PendingFollowUpBucket bucket)
        {
            if (pendingBucket == bucket)
            {
                return;
            }

            pendingBucket = bucket;
        }

        private static BusySendMode NormalizeRequestedMode(BusySendMode mode)
            => mode is BusySendMode.Steer or BusySendMode.Interrupt ? mode : BusySendMode.Queue;

        private void NotifyPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed class SidecarEventEntry
    {
        public SidecarEventEntry(int sequence, DateTimeOffset timestamp, string eventType, string summary, string details)
        {
            Sequence = sequence;
            Timestamp = timestamp;
            EventType = eventType;
            Summary = summary;
            Details = details;
        }

        public int Sequence { get; }

        public DateTimeOffset Timestamp { get; }

        public string EventType { get; }

        public string Summary { get; }

        public string Details { get; }

        public string SequenceText => $"#{Sequence:D3}";

        public string TimestampText => Timestamp.ToString("HH:mm:ss");

        public string SummaryText => string.IsNullOrWhiteSpace(Summary) ? "无附加说明" : Summary.Trim();

        public string EventTypeText => MapEventType(EventType);

        public string DisplayText
        {
            get
            {
                return $"{TimestampText} | {EventTypeText} | {SummaryText}";
            }
        }

        private static string MapEventType(string? eventType)
        {
            return (eventType ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "runtime_state" => "运行时状态",
                "info" => "提示信息",
                "turn_started" => "回合开始",
                "error" => "错误",
                "assistant_text_completed" => "回复完成",
                "assistant_text_delta" => "回复片段",
                "turn_completed" => "回合完成",
                "turn_steered" => "回合引导",
                "approval_requested" => "等待审批",
                "permission_requested" => "等待权限",
                "request_user_input" => "等待补录",
                "server_request_resolved" => "请求结束",
                "tool_call_started" => "工具开始",
                "tool_call_output_delta" => "工具输出",
                "tool_call_completed" => "工具完成",
                "plan_updated" => "计划更新",
                "diff_updated" => "差异更新",
                "operation_reported" => "操作回报",
                "mcp_server_status_updated" => "MCP 服务状态",
                "reasoning_delta" => "推理片段",
                "task_started" => "任务开始",
                "task_completed" => "任务完成",
                "raw_notification" => "底层通知",
                "agent_job_progress" => "Agent Job 进度",
                "deprecation_notice" => "废弃提示",
                "config_warning" => "配置警告",
                "thread_status_changed" => "线程状态",
                "thread_name_updated" => "线程标题",
                "thread_token_usage_updated" => "Token 使用量",
                "thread_compacted" => "线程压缩",
                "skills_changed" => "技能变更",
                "command_exec_output_delta" => "进程输出",
                "app_list_updated" => "应用列表",
                "windows_sandbox_setup" => "Windows Sandbox",
                "mcp_server_oauth_login" => "MCP OAuth",
                "realtime_session" => "实时会话",
                "fuzzy_file_search_session" => "模糊搜索",
                "thread_realtime_item_added" => "实时项目",
                "thread_realtime_output_audio_delta" => "实时音频",
                "thread_realtime_error" => "实时错误",
                "thread_realtime_closed" => "实时关闭",
                _ => string.IsNullOrWhiteSpace(eventType) ? "事件" : eventType.Trim(),
            };
        }
    }

    private sealed class PersistedSidecarEventLogSnapshot
    {
        public List<PersistedSidecarEventEntry> Events { get; set; } = [];

        public List<PersistedSidecarDiagnosticEntry> DiagnosticTrail { get; set; } = [];
    }

    private sealed class PersistedSidecarEventEntry
    {
        public int Sequence { get; set; }

        public DateTimeOffset Timestamp { get; set; }

        public string EventType { get; set; } = string.Empty;

        public string Summary { get; set; } = string.Empty;

        public string Details { get; set; } = string.Empty;
    }

    private sealed class PersistedSidecarDiagnosticEntry
    {
        public int Sequence { get; set; }

        public DateTimeOffset Timestamp { get; set; }

        public string EventType { get; set; } = string.Empty;

        public string? ThreadId { get; set; }

        public string? TurnId { get; set; }

        public string? CallId { get; set; }

        public string? ToolName { get; set; }

        public string? ItemId { get; set; }

        public string? State { get; set; }

        public string? Status { get; set; }

        public string? Phase { get; set; }

        public string? SourceMethod { get; set; }

        public string? Message { get; set; }

        public string? Text { get; set; }

        public string? ReasoningText { get; set; }

        public bool? WillRetry { get; set; }

        public bool? RequiresApproval { get; set; }

        public string? TurnErrorMessage { get; set; }

        public string? TurnErrorAdditionalDetails { get; set; }

        public string? DiagnosticJson { get; set; }

        public PersistedSidecarDiagnosticEntry CloneWithSequence(int sequence)
        {
            return new PersistedSidecarDiagnosticEntry
            {
                Sequence = sequence,
                Timestamp = Timestamp,
                EventType = EventType,
                ThreadId = ThreadId,
                TurnId = TurnId,
                CallId = CallId,
                ToolName = ToolName,
                ItemId = ItemId,
                State = State,
                Status = Status,
                Phase = Phase,
                SourceMethod = SourceMethod,
                Message = Message,
                Text = Text,
                ReasoningText = ReasoningText,
                WillRetry = WillRetry,
                RequiresApproval = RequiresApproval,
                TurnErrorMessage = TurnErrorMessage,
                TurnErrorAdditionalDetails = TurnErrorAdditionalDetails,
                DiagnosticJson = DiagnosticJson,
            };
        }
    }

    public sealed class StructuredPermissionField : INotifyPropertyChanged
    {
        private string valueText;

        private StructuredPermissionField(string key, string valueTypeDisplay, string valueText)
        {
            Key = key;
            ValueTypeDisplay = valueTypeDisplay;
            this.valueText = valueText;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Key { get; }

        public string ValueTypeDisplay { get; }

        public string ValueText
        {
            get => valueText;
            set
            {
                if (valueText == value)
                {
                    return;
                }

                valueText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ValueText)));
            }
        }

        internal TianShuSidecarStructuredValue GetTypedValue()
        {
            return ValueTypeDisplay switch
            {
                "bool" when bool.TryParse(ValueText, out var booleanValue) => TianShuSidecarStructuredValue.FromBoolean(booleanValue),
                "number" => ParseNumberValue(ValueText),
                "json" => ParseJsonValue(ValueText),
                "null" => TianShuSidecarStructuredValue.Null,
                _ => TianShuSidecarStructuredValue.FromString(ValueText ?? string.Empty),
            };
        }

        private static TianShuSidecarStructuredValue ParseJsonValue(string? valueText)
        {
            var normalized = (valueText ?? string.Empty).Trim();
            if (normalized.Length == 0)
            {
                return TianShuSidecarStructuredValue.FromString(string.Empty);
            }

            try
            {
                using var document = JsonDocument.Parse(normalized);
                return TianShuSidecarStructuredValue.FromJsonElement(document.RootElement);
            }
            catch (JsonException)
            {
                return TianShuSidecarStructuredValue.FromString(valueText ?? string.Empty);
            }
        }

        private static TianShuSidecarStructuredValue ParseNumberValue(string? valueText)
        {
            var normalized = (valueText ?? string.Empty).Trim();
            return TianShuSidecarStructuredValue.FromNumber(normalized.Length == 0 ? "0" : normalized);
        }

        public static StructuredPermissionField FromJson(string key, JsonElement value)
        {
            return value.ValueKind switch
            {
                JsonValueKind.True or JsonValueKind.False => new StructuredPermissionField(key, "bool", value.GetBoolean().ToString().ToLowerInvariant()),
                JsonValueKind.Number => new StructuredPermissionField(key, "number", value.ToString()),
                JsonValueKind.Object or JsonValueKind.Array => new StructuredPermissionField(key, "json", value.GetRawText()),
                JsonValueKind.Null => new StructuredPermissionField(key, "null", string.Empty),
                _ => new StructuredPermissionField(key, "string", value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString()),
            };
        }

        internal static StructuredPermissionField FromPayload(TianShuSidecarPermissionFieldPayload field)
        {
            var key = string.IsNullOrWhiteSpace(field.Key) ? string.Empty : field.Key;
            var valueType = string.IsNullOrWhiteSpace(field.ValueType) ? "string" : field.ValueType;
            var valueText = field.ValueText ?? string.Empty;
            return new StructuredPermissionField(key, valueType, valueText);
        }
    }

    public sealed class StructuredUserInputQuestion : INotifyPropertyChanged
    {
        private string answerText = string.Empty;
        private StructuredUserInputOption? selectedOption;

        private StructuredUserInputQuestion(
            string id,
            string headerText,
            string prompt,
            bool isOther,
            IEnumerable<(string Label, string? Description)> options)
        {
            Id = id;
            HeaderText = headerText;
            Prompt = prompt;
            IsOther = isOther;

            var groupName = $"pending-user-input-{Guid.NewGuid():N}";
            foreach (var option in options)
            {
                if (string.IsNullOrWhiteSpace(option.Label))
                {
                    continue;
                }

                Options.Add(new StructuredUserInputOption(this, option.Label, option.Description, groupName));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id { get; }

        public string HeaderText { get; }

        public string Prompt { get; }

        public bool IsOther { get; }

        public ObservableCollection<StructuredUserInputOption> Options { get; } = new();

        public bool HasOptions => Options.Count > 0;

        public string OptionsHint => HasOptions
            ? $"可选项：{string.Join(" / ", Options.Select(static option => option.Label))}"
            : string.Empty;

        public string FreeInputLabel => HasOptions ? "或填写自定义内容" : "请输入补录内容";

        public StructuredUserInputOption? SelectedOption
        {
            get => selectedOption;
            set
            {
                if (ReferenceEquals(selectedOption, value))
                {
                    return;
                }

                var previous = selectedOption;
                selectedOption = value;
                previous?.SetSelectedFromOwner(false);
                selectedOption?.SetSelectedFromOwner(true);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedOption)));
            }
        }

        public string AnswerText
        {
            get => answerText;
            set
            {
                if (answerText == value)
                {
                    return;
                }

                answerText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AnswerText)));
            }
        }

        public static StructuredUserInputQuestion FromJson(JsonElement question)
        {
            var id = TianShuConversationToolWindowControl.ReadJsonString(question, "id") ?? string.Empty;
            var header = TianShuConversationToolWindowControl.ReadJsonString(question, "header") ?? id;
            var prompt = TianShuConversationToolWindowControl.ReadJsonString(question, "question")
                ?? TianShuConversationToolWindowControl.ReadJsonString(question, "prompt")
                ?? string.Empty;
            var isOther = question.TryGetProperty("isOther", out var isOtherElement)
                && isOtherElement.ValueKind is JsonValueKind.True or JsonValueKind.False
                && isOtherElement.GetBoolean();
            return new StructuredUserInputQuestion(id, header, prompt, isOther, EnumerateOptions(question));
        }

        internal static StructuredUserInputQuestion FromPayload(TianShuSidecarUserInputQuestionPayload question)
        {
            var id = question.Id ?? string.Empty;
            var header = string.IsNullOrWhiteSpace(question.Header) ? id : question.Header;
            var prompt = question.Prompt ?? string.Empty;
            return new StructuredUserInputQuestion(id, header, prompt, question.IsOther, EnumerateOptions(question.Options));
        }

        internal TianShuSidecarStructuredValue BuildStructuredAnswer()
        {
            if (!string.IsNullOrWhiteSpace(AnswerText))
            {
                return TianShuConversationToolWindowControl.ParseConfigInputValue(AnswerText);
            }

            if (!string.IsNullOrWhiteSpace(SelectedOption?.Label))
            {
                return TianShuSidecarStructuredValue.FromString(SelectedOption.Label);
            }

            return TianShuSidecarStructuredValue.FromString(string.Empty);
        }

        internal void SelectOption(StructuredUserInputOption option)
            => SelectedOption = option;

        internal void ClearSelectedOption(StructuredUserInputOption option)
        {
            if (ReferenceEquals(selectedOption, option))
            {
                selectedOption = null;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedOption)));
            }
        }

        private static IEnumerable<(string Label, string? Description)> EnumerateOptions(JsonElement question)
        {
            if (!question.TryGetProperty("options", out var options) || options.ValueKind != JsonValueKind.Array)
            {
                yield break;
            }

            foreach (var option in options.EnumerateArray())
            {
                var label = TianShuConversationToolWindowControl.ReadJsonString(option, "label");
                if (string.IsNullOrWhiteSpace(label))
                {
                    continue;
                }

                yield return (label!, TianShuConversationToolWindowControl.ReadJsonString(option, "description"));
            }
        }

        private static IEnumerable<(string Label, string? Description)> EnumerateOptions(IEnumerable<TianShuSidecarUserInputOptionPayload>? options)
        {
            if (options is null)
            {
                yield break;
            }

            foreach (var option in options)
            {
                if (string.IsNullOrWhiteSpace(option.Label))
                {
                    continue;
                }

                yield return (option.Label, option.Description);
            }
        }
    }

    public sealed class StructuredUserInputOption : INotifyPropertyChanged
    {
        private readonly StructuredUserInputQuestion owner;
        private bool isSelected;

        internal StructuredUserInputOption(StructuredUserInputQuestion owner, string label, string? description, string groupName)
        {
            this.owner = owner;
            Label = label;
            Description = description;
            GroupName = groupName;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Label { get; }

        public string? Description { get; }

        public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

        public string GroupName { get; }

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (isSelected == value)
                {
                    return;
                }

                if (value)
                {
                    owner.SelectOption(this);
                    return;
                }

                owner.ClearSelectedOption(this);
                SetSelectedFromOwner(false);
            }
        }

        internal void SetSelectedFromOwner(bool value)
        {
            if (isSelected == value)
            {
                return;
            }

            isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    private sealed class ToolActivityEntry
    {
        private readonly StringBuilder outputBuilder = new();

        public ToolActivityEntry(string key)
        {
            Key = key;
        }

        public string Key { get; }

        public string ToolName { get; set; } = "tool";

        public string? TurnId { get; set; }

        public string Status { get; set; } = "started";

        public string? Summary { get; set; }

        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

        public string Output => outputBuilder.ToString();

        public void AppendOutput(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var merged = TianShuConversationToolWindowControl.MergeStreamingText(outputBuilder.ToString(), text, Environment.NewLine);
            outputBuilder.Clear();
            outputBuilder.Append(merged);
        }
    }

    private sealed class TaskActivityEntry
    {
        public TaskActivityEntry(string key)
        {
            Key = key;
        }

        public string Key { get; }

        public string TaskType { get; set; } = "task";

        public string Status { get; set; } = "started";

        public string? Message { get; set; }

        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    }

}
