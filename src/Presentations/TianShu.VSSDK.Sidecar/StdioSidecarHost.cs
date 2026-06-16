using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using TianShu.AppHost.Configuration;
using TianShu.Configuration;
using TianShu.ControlPlane;
using TianShu.ControlPlane.Abstractions;
using TianShu.Execution.Runtime;
using TianShu.Execution.Runtime.Diagnostics;
using TianShu.Execution.Runtime.Events;
using TianShu.Contracts.Agents;
using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Catalog;
using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Environment;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Governance;
using TianShu.Contracts.Host;
using TianShu.Contracts.Identity;
using TianShu.Contracts.Interactions;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;
using TianShu.HostGateway;
using TianShu.Provider.Abstractions;
using TianShu.Contracts.Sessions;
using TianShu.Contracts.Workflows;
using TianShu.RuntimeComposition;
using Task = System.Threading.Tasks.Task;

namespace TianShu.VSSDK.Sidecar;

internal sealed class StdioSidecarHost : IAsyncDisposable
{
    private readonly record struct FormalRuntimeDispatchResult(bool Handled, object? Result);

    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions TypedPayloadJsonOptions = CreateTypedPayloadJsonOptions();
    private static readonly ConditionalWeakTable<IExecutionRuntime, ITianShuControlPlane> ControlPlaneCache = new();
    private const string EventAssistantDelta = "assistant_text_delta";
    private const string EventAssistantCompleted = "assistant_text_completed";
    private const string EventInfo = "info";
    private const string EventTurnStarted = "turn_started";
    private const string EventError = "error";
    private const string EventApprovalRequested = "approval_requested";
    private const string EventPermissionRequested = "permission_requested";
    private const string EventRequestUserInput = "request_user_input";
    private const string EventServerRequestResolved = "server_request_resolved";
    private const string EventTurnCompleted = "turn_completed";
    private const string EventTurnSteered = "turn_steered";
    private const string EventRuntimeState = "runtime_state";
    private const string EventToolCallStarted = "tool_call_started";
    private const string EventToolCallOutputDelta = "tool_call_output_delta";
    private const string EventToolCallCompleted = "tool_call_completed";
    private const string EventPlanUpdated = "plan_updated";
    private const string EventDiffUpdated = "diff_updated";
    private const string EventOperationReported = "operation_reported";
    private const string EventMcpServerStatusUpdated = "mcp_server_status_updated";
    private const string EventRawNotification = "raw_notification";
    private const string EventReasoningDelta = "reasoning_delta";
    private const string EventTaskStarted = "task_started";
    private const string EventTaskCompleted = "task_completed";
    private const string EventItemStarted = "item_started";
    private const string EventItemCompleted = "item_completed";
    private const string EventUserMessageCommitted = "user_message_committed";
    private const string EventPendingFollowUpUpdated = "pending_followup_updated";
    private const string EventAgentJobProgress = "agent_job_progress";
    private const string EventDeprecationNotice = "deprecation_notice";
    private const string EventConfigWarning = "config_warning";
    private const string EventThreadStatusChanged = "thread_status_changed";
    private const string EventThreadNameUpdated = "thread_name_updated";
    private const string EventThreadTokenUsageUpdated = "thread_token_usage_updated";
    private const string EventThreadCompacted = "thread_compacted";
    private const string EventSkillsChanged = "skills_changed";
    private const string EventCommandExecOutputDelta = "command_exec_output_delta";
    private const string EventAppListUpdated = "app_list_updated";
    private const string EventWindowsSandboxSetup = "windows_sandbox_setup";
    private const string EventMcpServerOauthLogin = "mcp_server_oauth_login";
    private const string EventRealtimeSession = "realtime_session";
    private const string EventFuzzyFileSearchSession = "fuzzy_file_search_session";
    private const string EventThreadRealtimeItemAdded = "thread_realtime_item_added";
    private const string EventThreadRealtimeOutputAudioDelta = "thread_realtime_output_audio_delta";
    private const string EventThreadRealtimeError = "thread_realtime_error";
    private const string EventThreadRealtimeClosed = "thread_realtime_closed";
    private const string EventHookStarted = "hook_started";
    private const string EventHookCompleted = "hook_completed";
    private const string EventModelRerouted = "model_rerouted";

    private readonly TextReader input;
    private readonly TextWriter output;
    private readonly TextWriter error;
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private IExecutionRuntime? runtime;
    private ITianShuHostGateway? hostGateway;
    private CancellationTokenSource? runtimeStreamRelayCts;
    private Task? runtimeStreamRelayTask;
    private Task? activeSendTask;
    private bool shutdownRequested;
    private string? runtimeWorkingDirectory;

    public StdioSidecarHost(TextReader input, TextWriter output, TextWriter error)
    {
        this.input = input;
        this.output = output;
        this.error = error;
    }

    public async Task<int> RunAsync()
    {
        await WriteRuntimeStateAsync(
                "waiting_for_initialize",
                "sidecar 已就绪，等待 initialize。",
                new { runtime = ".NET 10 sidecar" })
            .ConfigureAwait(false);

        while (!shutdownRequested)
        {
            var line = await input.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            line = line.TrimStart('﻿', '\0');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            SidecarRequestEnvelope? request;
            try
            {
                request = JsonSerializer.Deserialize<SidecarRequestEnvelope>(line, jsonOptions);
                if (request is null)
                {
                    throw new InvalidOperationException("请求体为空。");
                }

                _ = request.GetRequiredRequestId();
                _ = request.GetRequiredCommand();
            }
            catch (Exception ex)
            {
                LogError("请求解析失败。", ex);
                await WriteResponseAsync(string.Empty, false, "请求格式无效。", new { detail = ex.Message }).ConfigureAwait(false);
                await WriteEventAsync(EventError, message: "请求格式无效。", data: new { detail = ex.Message }).ConfigureAwait(false);
                continue;
            }

            await HandleRequestAsync(request).ConfigureAwait(false);
        }

        return 0;
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeRuntimeAsync().ConfigureAwait(false);

        if (activeSendTask is not null)
        {
            try
            {
                await activeSendTask.ConfigureAwait(false);
            }
            catch
            {
                // 后台发送任务内部已经记录错误，这里只做清理等待。
            }
        }

        writeGate.Dispose();
    }

    private async Task HandleRequestAsync(SidecarRequestEnvelope request)
    {
        var requestId = request.GetRequiredRequestId();
        var command = request.GetRequiredCommand();

        try
        {
            switch (command.ToLowerInvariant())
            {
                case "initialize":
                    await HandleInitializeAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "send":
                    await HandleSendAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "followup":
                    await HandleFollowUpAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "listthreads":
                    await HandleListThreadsAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "resumethread":
                    await HandleResumeThreadAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "forkthread":
                    await HandleForkThreadAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "startnewthread":
                    await HandleStartNewThreadAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "renamethread":
                    await HandleRenameThreadAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "archivethread":
                    await HandleArchiveThreadAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "deletethread":
                    await HandleDeleteThreadAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "interrupt":
                    await HandleInterruptAsync(requestId).ConfigureAwait(false);
                    break;

                case "respondapproval":
                    await HandleRespondApprovalAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "respondpermission":
                    await HandleRespondPermissionAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "responduserinput":
                    await HandleRespondUserInputAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "readthread":
                    await HandleReadThreadAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "listloadedthreads":
                    await HandleListLoadedThreadsAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "compactthread":
                    await HandleCompactThreadAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "cleanbackgroundterminals":
                    await HandleCleanBackgroundTerminalsAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "unsubscribethread":
                    await HandleUnsubscribeThreadAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "incrementthreadelicitation":
                    await HandleIncrementThreadElicitationAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "decrementthreadelicitation":
                    await HandleDecrementThreadElicitationAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "getthreadprojection":
                    await HandleGetThreadProjectionAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "unarchivethread":
                    await HandleUnarchiveThreadAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "rollbackthread":
                    await HandleRollbackThreadAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "updatethreadmetadata":
                    await HandleUpdateThreadMetadataAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "readconfig":
                    await HandleReadConfigAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "listmodels":
                    await HandleListModelsAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "getcapabilitycatalog":
                    await HandleGetCapabilityCatalogAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "resolveenginebinding":
                    await HandleResolveEngineBindingAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "listagents":
                    await HandleListAgentsAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "getagentrosterprojection":
                    await HandleGetAgentRosterProjectionAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "getteamprojection":
                    await HandleGetTeamProjectionAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "registeragentthread":
                    await HandleRegisterAgentThreadAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "createagentjob":
                    await HandleCreateAgentJobAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "dispatchagentjob":
                    await HandleDispatchAgentJobAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "reportagentjobitem":
                    await HandleReportAgentJobItemAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "readagentjob":
                    await HandleReadAgentJobAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "createworkflow":
                    await HandleCreateWorkflowAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "publishworkflowplan":
                    await HandlePublishWorkflowPlanAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "createworkflowtask":
                    await HandleCreateWorkflowTaskAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "updateworkflowtaskstate":
                    await HandleUpdateWorkflowTaskStateAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "createcollaborationspace":
                    await HandleCreateCollaborationSpaceAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "configurecollaborationspace":
                    await HandleConfigureCollaborationSpaceAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "archivecollaborationspace":
                    await HandleArchiveCollaborationSpaceAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "getcollaborationspaceoverview":
                    await HandleGetCollaborationSpaceOverviewAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "getcollaborationspaceprojection":
                    await HandleGetCollaborationSpaceProjectionAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "listcollaborationspaces":
                    await HandleListCollaborationSpacesAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "bindparticipanttosession":
                    await HandleBindParticipantToSessionAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "bindparticipanttoworkflow":
                    await HandleBindParticipantToWorkflowAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "updateparticipantrole":
                    await HandleUpdateParticipantRoleAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "getparticipantprojection":
                    await HandleGetParticipantProjectionAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "getparticipantviewprojection":
                    await HandleGetParticipantViewProjectionAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "listparticipantsinscope":
                    await HandleListParticipantsInScopeAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "getsessionoverview":
                    await HandleGetSessionOverviewAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "getsessionsnapshot":
                    await HandleGetSessionSnapshotAsync(requestId).ConfigureAwait(false);
                    break;

                case "listsessions":
                    await HandleListSessionsAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "getapprovalqueueprojection":
                    await HandleGetApprovalQueueProjectionAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "listuserinputrequests":
                    await HandleListUserInputRequestsAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "getartifactprojection":
                    await HandleGetArtifactProjectionAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "getartifactcollectionprojection":
                    await HandleGetArtifactCollectionProjectionAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "getworkflowboard":
                    await HandleGetWorkflowBoardAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "gettaskboard":
                    await HandleGetTaskBoardAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "getplanprojection":
                    await HandleGetPlanProjectionAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "getaccountprofile":
                    await HandleGetAccountProfileAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "listbounddevices":
                    await HandleListBoundDevicesAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "listmemoryspaces":
                    await HandleListMemorySpacesAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "resolvememoryoverlay":
                    await HandleResolveMemoryOverlayAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "listmemoryproviders":
                    await HandleListMemoryProvidersAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "filtermemory":
                    await HandleFilterMemoryAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "listmemoryreviews":
                    await HandleListMemoryReviewsAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "addmemory":
                    await HandleAddMemoryAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "extractmemory":
                    await HandleExtractMemoryAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "importmemory":
                    await HandleImportMemoryAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "exportmemory":
                    await HandleExportMemoryAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "bindmemoryprovider":
                    await HandleBindMemoryProviderAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "forgetmemory":
                    await HandleForgetMemoryAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "deletememory":
                    await HandleDeleteMemoryAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "supersedememory":
                    await HandleSupersedeMemoryAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "approvememoryreview":
                    await HandleApproveMemoryReviewAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "demotememoryreview":
                    await HandleDemoteMemoryReviewAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "mergememoryreview":
                    await HandleMergeMemoryReviewAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "restorememoryreview":
                    await HandleRestoreMemoryReviewAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "recordmemoryfeedback":
                    await HandleRecordMemoryFeedbackAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "recordmemorycitation":
                    await HandleRecordMemoryCitationAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "getexecutiontrace":
                    await HandleGetExecutionTraceAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "listattemptsummaries":
                    await HandleListAttemptSummariesAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "uploadfeedback":
                    await HandleUploadFeedbackAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "searchfuzzyfiles":
                    await HandleSearchFuzzyFilesAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "startfuzzyfilesearchsession":
                    await HandleStartFuzzyFileSearchSessionAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "updatefuzzyfilesearchsession":
                    await HandleUpdateFuzzyFileSearchSessionAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "stopfuzzyfilesearchsession":
                    await HandleStopFuzzyFileSearchSessionAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "startrealtime":
                    await HandleStartRealtimeAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "appendrealtimetext":
                    await HandleAppendRealtimeTextAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "appendrealtimeaudio":
                    await HandleAppendRealtimeAudioAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "handoffrealtimeoutput":
                    await HandleHandoffRealtimeOutputAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "stoprealtime":
                    await HandleStopRealtimeAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "writeconfigvalue":
                    await HandleWriteConfigValueAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "writeconfigbatch":
                    await HandleWriteConfigBatchAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "readconfigrequirements":
                    await HandleReadConfigRequirementsAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "listexperimentalfeatures":
                    await HandleListExperimentalFeaturesAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "listcollaborationmodes":
                    await HandleListCollaborationModesAsync(requestId).ConfigureAwait(false);
                    break;

                case "listmcpserverstatus":
                    await HandleListMcpServerStatusAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "reloadmcpservers":
                    await HandleReloadMcpServersAsync(requestId).ConfigureAwait(false);
                    break;

                case "listskills":
                    await HandleListSkillsAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "writeskillconfig":
                    await HandleWriteSkillConfigAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "listremoteskills":
                    await HandleListRemoteSkillsAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "exportremoteskill":
                    await HandleExportRemoteSkillAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "listplugins":
                    await HandleListPluginsAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "readplugin":
                    await HandleReadPluginAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "installplugin":
                    await HandleInstallPluginAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "uninstallplugin":
                    await HandleUninstallPluginAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "listapps":
                    await HandleListAppsAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "startreview":
                    await HandleStartReviewAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "startmcpserveroauthlogin":
                    await HandleStartMcpServerOauthLoginAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "artifact/conversationsummary/read":
                    await HandleGetConversationSummaryAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "artifact/gitdifftoremote/read":
                    await HandleGetGitDiffToRemoteAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "invokecapability":
                    await HandleInvokeCapabilityAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "invokeruntimesurface":
                    await HandleInvokeRuntimeSurfaceAsync(requestId, request).ConfigureAwait(false);
                    break;

                case "shutdown":
                    await HandleShutdownAsync(requestId).ConfigureAwait(false);
                    break;

                default:
                    await WriteResponseAsync(requestId, false, $"未知命令：{command}", null).ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex)
        {
            LogError($"命令执行失败：{command}", ex);
            await WriteResponseAsync(requestId, false, ex.Message, new { command }).ConfigureAwait(false);
            await WriteEventAsync(EventError, message: ex.Message, data: new { command }).ConfigureAwait(false);
        }
    }

    private async Task HandleInitializeAsync(string requestId, SidecarRequestEnvelope request)
    {
        var payload = request.DeserializePayload<InitializePayload>(jsonOptions);
        await DisposeRuntimeAsync().ConfigureAwait(false);

        var (options, config) = BuildRuntimeOptions(payload);
        var newRuntime = TianShuAppHostRuntimeClientFactory.Create();

        try
        {
            await newRuntime.InitializeAsync(options, dynamicToolCallHandler: null, CancellationToken.None).ConfigureAwait(false);
            runtime = newRuntime;
            hostGateway = CreateHostGateway(newRuntime);
            runtimeWorkingDirectory = options.WorkingDirectory;
            var sessionSnapshot = await GetSessionSnapshotAsync(newRuntime).ConfigureAwait(false);

            await WriteResponseAsync(
                    requestId,
                    true,
                    "运行时初始化成功。",
                    new
                    {
                        runtimeName = sessionSnapshot.RuntimeName,
                        threadId = sessionSnapshot.ActiveThreadId?.Value,
                        workingDirectory = options.WorkingDirectory,
                        configPath = config.ConfigFilePath,
                        profileName = config.ActiveProfile,
                        appHostProjectPath = options.AppHostProjectPath,
                        model = options.Model,
                        modelProvider = options.ModelProvider,
                        approvalPolicy = options.ApprovalPolicy,
                        sandboxMode = options.SandboxMode,
                        webSearchMode = options.WebSearchMode,
                        serviceTier = options.ServiceTier,
                        collaborationMode = options.CollaborationMode,
                        protocolAdapter = options.ProtocolAdapter,
                    })
                .ConfigureAwait(false);

            await WriteRuntimeStateAsync(
                    "initialized",
                    "运行时初始化成功。",
                    new
                    {
                        threadId = sessionSnapshot.ActiveThreadId?.Value,
                        workingDirectory = options.WorkingDirectory,
                        configPath = config.ConfigFilePath,
                    })
                .ConfigureAwait(false);
        }
        catch
        {
            await StopRuntimeStreamRelayAsync().ConfigureAwait(false);
            await newRuntime.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task HandleSendAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<SendPayload>(jsonOptions);
        var userInputs = ResolveConversationInputs(payload.Inputs, payload.Message);
        if (userInputs.Count == 0)
        {
            await WriteResponseAsync(requestId, false, "发送内容不能为空。", null).ConfigureAwait(false);
            return;
        }

        var history = BuildConversationHistory(payload.HistoryMessages);
        activeSendTask = Task.Run(() => ExecuteSendAsync(requestId, userInputs, history));
        var sessionSnapshot = await GetSessionSnapshotAsync(runtime).ConfigureAwait(false);
        await WriteResponseAsync(
                requestId,
                true,
                "消息已进入发送队列。",
                new
                {
                    queued = true,
                    threadId = sessionSnapshot.ActiveThreadId?.Value,
                    historyCount = history.Count,
                })
            .ConfigureAwait(false);
    }

    private async Task HandleFollowUpAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<FollowUpPayload>(jsonOptions);
        var userInputs = ResolveConversationInputs(payload.Inputs, payload.Message);
        if (userInputs.Count == 0)
        {
            await WriteResponseAsync(requestId, false, "跟进内容不能为空。", null).ConfigureAwait(false);
            return;
        }

        var mode = ParseFollowUpMode(payload.Mode);
        var correlationId = Normalize(payload.CorrelationId) ?? requestId;
        activeSendTask = Task.Run(() => ExecuteFollowUpAsync(requestId, userInputs, mode, correlationId));
        var sessionSnapshot = await GetSessionSnapshotAsync(runtime).ConfigureAwait(false);
        await WriteResponseAsync(
                requestId,
                true,
                "跟进请求已受理。",
                new
                {
                    queued = true,
                    mode = payload.Mode ?? "queue",
                    threadId = sessionSnapshot.ActiveThreadId?.Value,
                    correlationId,
                })
            .ConfigureAwait(false);
    }

    private async Task ExecuteSendAsync(
        string requestId,
        IReadOnlyList<ControlPlaneInputItem> userInputs,
        IReadOnlyList<ControlPlaneConversationMessage> history)
        => await ExecuteConversationRequestAsync(
                requestId,
                busyMessage: "正在处理当前回合。",
                executeAsync: (_, gateway) => gateway.SubmitTurnAsync(
                    new HostSubmitTurn(
                        BuildHostInteractionEnvelope(requestId, userInputs),
                        history),
                    CancellationToken.None),
                errorLogPrefix: "发送请求失败。")
            .ConfigureAwait(false);

    private async Task ExecuteFollowUpAsync(
        string requestId,
        IReadOnlyList<ControlPlaneInputItem> userInputs,
        ControlPlaneFollowUpMode mode,
        string correlationId)
    {
        var busyMessage = mode switch
        {
            ControlPlaneFollowUpMode.Steer => "正在引导当前回合。",
            ControlPlaneFollowUpMode.Interrupt => "正在中断并提交新的跟进消息。",
            _ => "正在提交跟进消息。",
        };

        await ExecuteConversationRequestAsync(
                requestId,
                busyMessage,
                executeAsync: (_, gateway) => gateway.SubmitFollowUpAsync(
                    new HostSubmitFollowUp(
                        BuildHostInteractionEnvelope(requestId, userInputs),
                        mode switch
                        {
                            ControlPlaneFollowUpMode.Steer => HostFollowUpMode.Steer,
                            ControlPlaneFollowUpMode.Interrupt => HostFollowUpMode.Interrupt,
                            _ => HostFollowUpMode.Queue,
                        },
                        correlationId),
                    CancellationToken.None),
                errorLogPrefix: "跟进请求失败。")
            .ConfigureAwait(false);
    }

    private async Task ExecuteConversationRequestAsync(
        string requestId,
        string busyMessage,
        Func<IExecutionRuntime, ITianShuHostGateway, Task<HostTurnSubmissionResult>> executeAsync,
        string errorLogPrefix)
    {
        try
        {
            var currentRuntime = runtime;
            if (currentRuntime is null)
            {
                await WriteEventAsync(EventError, message: "发送时运行时不存在。", data: new { requestId }).ConfigureAwait(false);
                return;
            }

            var sessionSnapshot = await GetSessionSnapshotAsync(currentRuntime).ConfigureAwait(false);

            await WriteRuntimeStateAsync(
                    "busy",
                    busyMessage,
                    new
                    {
                        requestId,
                        threadId = sessionSnapshot.ActiveThreadId?.Value,
                    })
                .ConfigureAwait(false);

            var result = await executeAsync(currentRuntime, GetHostGateway()).ConfigureAwait(false);
            sessionSnapshot = await GetSessionSnapshotAsync(currentRuntime).ConfigureAwait(false);
            if (!result.Accepted)
            {
                await WriteEventAsync(
                        EventError,
                        threadId: sessionSnapshot.ActiveThreadId?.Value,
                        turnId: result.TurnId?.Value,
                        message: result.Message,
                        data: new
                        {
                            requestId,
                            turnStatus = result.TurnStatus,
                            correlationId = result.CorrelationId,
                            requestedMode = result.RequestedMode?.ToString(),
                            effectiveMode = result.EffectiveMode?.ToString(),
                        })
                    .ConfigureAwait(false);

                await WriteRuntimeStateAsync(
                        "idle",
                        "当前回合处理失败。",
                        new
                        {
                            requestId,
                            turnId = result.TurnId?.Value,
                            turnStatus = result.TurnStatus,
                            success = false,
                        })
                    .ConfigureAwait(false);

                return;
            }

            if (result.TurnId is null)
            {
                await WriteEventAsync(
                        EventInfo,
                        message: result.Message,
                        data: new
                        {
                            requestId,
                            correlationId = result.CorrelationId,
                            requestedMode = result.RequestedMode?.ToString(),
                            effectiveMode = result.EffectiveMode?.ToString(),
                        })
                    .ConfigureAwait(false);

                await WriteRuntimeStateAsync(
                        "idle",
                        "当前回合已完成。",
                        new
                        {
                            requestId,
                            success = true,
                        })
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            LogError(errorLogPrefix, ex);
            await WriteEventAsync(
                    EventError,
                    message: ex.Message,
                    data: new
                    {
                        requestId,
                        exceptionType = ex.GetType().FullName,
                    })
                .ConfigureAwait(false);

            await WriteRuntimeStateAsync(
                    "idle",
                    "当前回合异常结束。",
                    new
                    {
                        requestId,
                        success = false,
                    })
                .ConfigureAwait(false);
        }
    }

    private static ControlPlaneFollowUpMode ParseFollowUpMode(string? mode)
        => (mode ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "steer" => ControlPlaneFollowUpMode.Steer,
            "interrupt" => ControlPlaneFollowUpMode.Interrupt,
            _ => ControlPlaneFollowUpMode.Queue,
        };
    private async Task HandleListThreadsAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<ListThreadsPayload>(jsonOptions);
        var cwd = Normalize(payload.Cwd)
                  ?? (payload.MatchCurrentCwd ? Normalize(runtimeWorkingDirectory) : null);
        if (!TryParseThreadSourceKinds(payload.SourceKinds, out var sourceKinds, out var sourceKindsError))
        {
            await WriteResponseAsync(requestId, false, sourceKindsError ?? "sourceKinds 无效。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetHostGateway().ListThreadsAsync(
                new HostListThreadsQuery
                {
                    Limit = Math.Max(1, payload.Limit),
                    Cursor = Normalize(payload.Cursor),
                    Archived = payload.Archived,
                    WorkingDirectory = cwd,
                    SortKey = Normalize(payload.SortKey) ?? "updated_at",
                    ModelProviders = (payload.ModelProviders ?? Array.Empty<string>())
                        .Select(Normalize)
                        .Where(static value => !string.IsNullOrWhiteSpace(value))
                        .Select(static value => value!)
                        .ToArray(),
                    SourceKinds = sourceKinds.ToArray(),
                    SearchTerm = Normalize(payload.SearchTerm),
                },
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(
                requestId,
                true,
                $"已加载 {result.Threads.Count} 条线程。",
                new
                {
                    threads = result.Threads.Select(BuildThreadResponseObject),
                    nextCursor = result.NextCursor,
                })
            .ConfigureAwait(false);
    }

    private static bool TryParseThreadSourceKinds(
        IReadOnlyList<ControlPlaneThreadSourceKind>? values,
        out IReadOnlyList<ControlPlaneThreadSourceKind> sourceKinds,
        out string? error)
    {
        error = null;
        sourceKinds = Array.Empty<ControlPlaneThreadSourceKind>();
        if (values is null || values.Count == 0)
        {
            return true;
        }

        var parsed = new List<ControlPlaneThreadSourceKind>(values.Count);
        foreach (var value in values)
        {
            if (value is null)
            {
                continue;
            }

            if (!parsed.Contains(value))
            {
                parsed.Add(value);
            }
        }

        sourceKinds = parsed;
        return true;
    }

    private async Task HandleResumeThreadAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<ResumeThreadPayload>(jsonOptions);
        var threadId = Normalize(payload.ThreadId);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await WriteResponseAsync(requestId, false, "恢复线程时 threadId 不能为空。", null).ConfigureAwait(false);
            return;
        }

        var result = (await GetHostGateway().ResumeThreadAsync(
                BuildHostResumeThreadCommand(payload, threadId),
                CancellationToken.None)
            .ConfigureAwait(false)).Snapshot;
        if (result is null)
        {
            await WriteResponseAsync(requestId, false, $"未能恢复线程：{threadId}", new { threadId }).ConfigureAwait(false);
            return;
        }

        var thread = result.Thread;
        var responsePayload = new Dictionary<string, object?>
        {
            ["threadId"] = thread.ThreadId.Value,
            ["preview"] = thread.Preview,
            ["name"] = thread.Name,
            ["cwd"] = thread.WorkingDirectory,
            ["path"] = thread.Path,
            ["modelProvider"] = thread.ModelProvider,
            ["source"] = thread.Source,
            ["cliVersion"] = thread.CliVersion,
            ["agentNickname"] = thread.AgentNickname,
            ["agentRole"] = thread.AgentRole,
            ["createdAt"] = thread.CreatedAt?.ToUnixTimeSeconds(),
            ["updatedAt"] = thread.UpdatedAt.ToUnixTimeSeconds(),
            ["ephemeral"] = thread.IsEphemeral,
            ["sessionConfiguration"] = BuildThreadSessionConfigurationObject(thread.SessionConfiguration),
            ["status"] = string.IsNullOrWhiteSpace(thread.Status)
                ? null
                : new
                {
                    type = thread.Status,
                    activeFlags = thread.ActiveFlags,
                },
            ["gitInfo"] = string.IsNullOrWhiteSpace(thread.GitSha)
                          && string.IsNullOrWhiteSpace(thread.GitBranch)
                          && string.IsNullOrWhiteSpace(thread.GitOriginUrl)
                ? null
                : new
                {
                    sha = thread.GitSha,
                    branch = thread.GitBranch,
                    originUrl = thread.GitOriginUrl,
                },
            ["turns"] = result.Turns.Select(turn => new
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
                items = turn.Items.Select(item => SerializeThreadTurnItem(item)),
            }),
            ["seedHistory"] = result.SeedHistory.Select(item => SerializeSeedHistoryItem(item)),
            ["pendingInputState"] = BuildPendingInputStateObject(result.PendingInputState),
            ["pendingInteractiveRequests"] = BuildPendingInteractiveRequestsObject(result.PendingInteractiveRequests),
        };
        if (result.Messages.Count > 0)
        {
            responsePayload["messagesAreAuthoritative"] = true;
            responsePayload["messages"] = result.Messages.Select(BuildConversationMessageObject);
        }

        await WriteResponseAsync(
                requestId,
                true,
                "线程恢复成功。",
                responsePayload)
            .ConfigureAwait(false);

        await WriteRuntimeStateAsync(
                "initialized",
                "线程已恢复。",
                new
                {
                    threadId = thread.ThreadId.Value,
                    cwd = thread.WorkingDirectory,
                })
            .ConfigureAwait(false);
    }

    private JsonElement SerializeThreadTurnItem(ControlPlaneThreadTurnItem item)
        => JsonSerializer.SerializeToElement(item, item.GetType(), jsonOptions);

    private JsonElement SerializeSeedHistoryItem(ControlPlaneSeedHistoryItem item)
        => JsonSerializer.SerializeToElement(item, item.GetType(), jsonOptions);

    private async Task HandleStartNewThreadAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<StartNewThreadPayload>(jsonOptions);
        var result = (await GetHostGateway().StartThreadAsync(
                BuildHostStartThreadCommand(payload),
                CancellationToken.None)
            .ConfigureAwait(false)).Thread;
        if (result is null)
        {
            await WriteResponseAsync(requestId, false, "未能创建新线程。", null).ConfigureAwait(false);
            return;
        }

        await WriteResponseAsync(
                requestId,
                true,
                "已创建新线程。",
                BuildThreadResponseObject(result))
            .ConfigureAwait(false);

        await WriteRuntimeStateAsync(
                "initialized",
                "已创建新线程。",
                new
                {
                    threadId = result.ThreadId.Value,
                    cwd = result.WorkingDirectory,
                })
            .ConfigureAwait(false);
    }

    private async Task HandleForkThreadAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<ForkThreadPayload>(jsonOptions);
        var threadId = Normalize(payload.ThreadId);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await WriteResponseAsync(requestId, false, "分叉线程时 threadId 不能为空。", null).ConfigureAwait(false);
            return;
        }

        var result = (await GetHostGateway().ForkThreadAsync(
                BuildHostForkThreadCommand(payload, threadId),
                CancellationToken.None)
            .ConfigureAwait(false)).Thread;
        if (result is null)
        {
            await WriteResponseAsync(requestId, false, $"未能分叉线程：{threadId}", new { threadId }).ConfigureAwait(false);
            return;
        }

        await WriteResponseAsync(
                requestId,
                true,
                "线程分叉成功。",
                BuildThreadResponseObject(result))
            .ConfigureAwait(false);

        await WriteRuntimeStateAsync(
                "initialized",
                "线程分叉成功。",
                new
                {
                    threadId = result.ThreadId.Value,
                    cwd = result.WorkingDirectory,
                })
            .ConfigureAwait(false);
    }
    
    private async Task HandleRenameThreadAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<RenameThreadPayload>(jsonOptions);
        var threadId = Normalize(payload.ThreadId);
        var name = Normalize(payload.Name);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await WriteResponseAsync(requestId, false, "重命名线程时 threadId 不能为空。", null).ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            await WriteResponseAsync(requestId, false, "重命名线程时 name 不能为空。", null).ConfigureAwait(false);
            return;
        }

        var renamed = await GetHostGateway().RenameThreadAsync(
                new HostRenameThread
                {
                    ThreadId = new ThreadId(threadId),
                    Name = name,
                },
                CancellationToken.None)
            .ConfigureAwait(false);
        if (!renamed.Accepted)
        {
            await WriteResponseAsync(requestId, false, $"未能重命名线程：{threadId}", new { threadId, name }).ConfigureAwait(false);
            return;
        }

        await WriteResponseAsync(
                requestId,
                true,
                "线程已重命名。",
                new
                {
                    threadId,
                    name,
                })
            .ConfigureAwait(false);

        await WriteEventAsync(
                "thread_renamed",
                threadId: threadId,
                message: "线程已重命名。",
                data: new
                {
                    threadId,
                    name,
                })
            .ConfigureAwait(false);
    }

    private async Task HandleArchiveThreadAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<ArchiveThreadPayload>(jsonOptions);
        var threadId = Normalize(payload.ThreadId);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await WriteResponseAsync(requestId, false, "归档线程时 threadId 不能为空。", null).ConfigureAwait(false);
            return;
        }

        var archived = await GetHostGateway().ArchiveThreadAsync(
                new HostArchiveThread
                {
                    ThreadId = new ThreadId(threadId),
                },
                CancellationToken.None)
            .ConfigureAwait(false);
        if (!archived.Accepted)
        {
            await WriteResponseAsync(requestId, false, $"未能归档线程：{threadId}", new { threadId }).ConfigureAwait(false);
            return;
        }

        var sessionSnapshot = await GetSessionSnapshotAsync(runtime).ConfigureAwait(false);

        await WriteResponseAsync(
                requestId,
                true,
                "线程已归档。",
                new
                {
                    threadId,
                    activeThreadId = sessionSnapshot.ActiveThreadId?.Value,
                })
            .ConfigureAwait(false);

        await WriteEventAsync(
                "thread_archived",
                threadId: threadId,
                message: "线程已归档。",
                data: new
                {
                    threadId,
                    activeThreadId = sessionSnapshot.ActiveThreadId?.Value,
                })
            .ConfigureAwait(false);
    }

    private async Task HandleDeleteThreadAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<DeleteThreadPayload>(jsonOptions);
        var threadId = Normalize(payload.ThreadId);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await WriteResponseAsync(requestId, false, "删除线程时 threadId 不能为空。", null).ConfigureAwait(false);
            return;
        }

        var deleted = await GetHostGateway().DeleteThreadAsync(
                new HostDeleteThread
                {
                    ThreadId = new ThreadId(threadId),
                },
                CancellationToken.None)
            .ConfigureAwait(false);
        if (!deleted.Accepted)
        {
            await WriteResponseAsync(requestId, false, $"未能删除线程：{threadId}", new { threadId }).ConfigureAwait(false);
            return;
        }

        var sessionSnapshot = await GetSessionSnapshotAsync(runtime).ConfigureAwait(false);

        await WriteResponseAsync(
                requestId,
                true,
                "线程已删除。",
                new
                {
                    threadId,
                    activeThreadId = sessionSnapshot.ActiveThreadId?.Value,
                })
            .ConfigureAwait(false);

        await WriteEventAsync(
                "thread_deleted",
                threadId: threadId,
                message: "线程已删除。",
                data: new
                {
                    threadId,
                    activeThreadId = sessionSnapshot.ActiveThreadId?.Value,
                })
            .ConfigureAwait(false);
    }

    private async Task HandleInterruptAsync(string requestId)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        await GetHostGateway().InterruptTurnAsync(new HostInterruptTurn(), CancellationToken.None).ConfigureAwait(false);
        var sessionSnapshot = await GetSessionSnapshotAsync(runtime).ConfigureAwait(false);
        await WriteResponseAsync(
                requestId,
                true,
                "已发送中断请求。",
                new { threadId = sessionSnapshot.ActiveThreadId?.Value })
            .ConfigureAwait(false);

        await WriteRuntimeStateAsync(
                "interrupt_requested",
                "已发送中断请求。",
                new { threadId = sessionSnapshot.ActiveThreadId?.Value })
            .ConfigureAwait(false);
    }

    private async Task HandleShutdownAsync(string requestId)
    {
        shutdownRequested = true;

        await WriteResponseAsync(requestId, true, "sidecar 即将关闭。", null).ConfigureAwait(false);
        await WriteRuntimeStateAsync("stopping", "sidecar 即将关闭。", null).ConfigureAwait(false);
        await DisposeRuntimeAsync().ConfigureAwait(false);
    }

    private async Task HandleRespondApprovalAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<RespondApprovalPayload>(jsonOptions);
        var callId = Normalize(payload.CallId);
        if (string.IsNullOrWhiteSpace(callId))
        {
            await WriteResponseAsync(requestId, false, "审批 callId 不能为空。", null).ConfigureAwait(false);
            return;
        }

        var decision = ParseApprovalResolution(callId, payload);
        var accepted = await GetHostGateway().ResolveApprovalAsync(
                new HostResolveApproval(
                    new CallId(callId),
                    ToProtocolDecisionToken(decision.Decision),
                    commandPrefix: decision.CommandPrefix.ToArray(),
                    networkHost: decision.NetworkHost,
                    networkAction: decision.NetworkAction,
                    note: decision.Note,
                    context: new HostContext(HostSurfaceKind.Vsix, "sidecar")),
                CancellationToken.None)
            .ConfigureAwait(false);
        if (!accepted.Accepted)
        {
            await WriteResponseAsync(requestId, false, $"未找到待处理审批：{callId}", new { callId }).ConfigureAwait(false);
            return;
        }

        var approved = IsApproved(decision);
        var decisionToken = ToProtocolDecisionToken(decision.Decision);
        var sessionSnapshot = await GetSessionSnapshotAsync(runtime).ConfigureAwait(false);

        await WriteResponseAsync(
                requestId,
                true,
                approved ? "已提交批准结果。" : "已提交拒绝结果。",
                new
                {
                    callId,
                    approved,
                    decision = decisionToken,
                })
            .ConfigureAwait(false);

        await WriteRuntimeStateAsync(
                "busy",
                approved ? "已提交批准结果，等待运行时继续执行。" : "已提交拒绝结果，等待运行时继续执行。",
                new
                {
                    callId,
                    approved,
                    decision = decisionToken,
                    threadId = sessionSnapshot.ActiveThreadId?.Value,
                })
            .ConfigureAwait(false);
    }

    private async Task HandleRespondPermissionAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<RespondPermissionPayload>(jsonOptions);
        var callId = Normalize(payload.CallId);
        if (string.IsNullOrWhiteSpace(callId))
        {
            await WriteResponseAsync(requestId, false, "权限请求 callId 不能为空。", null).ConfigureAwait(false);
            return;
        }

        var response = new ControlPlanePermissionGrant
        {
            CallId = new CallId(callId),
            Permissions = ToStructuredDictionary(payload.Permissions) ?? new Dictionary<string, StructuredValue>(StringComparer.Ordinal),
            Scope = ParsePermissionGrantScope(payload.Scope),
        };

        var accepted = await GetHostGateway().GrantPermissionAsync(
                new HostGrantPermission(
                    new CallId(callId),
                    response.Permissions,
                    response.Scope == ControlPlanePermissionScope.Session
                        ? HostPermissionScope.Session
                        : HostPermissionScope.Turn,
                    context: new HostContext(HostSurfaceKind.Vsix, "sidecar")),
                CancellationToken.None)
            .ConfigureAwait(false);
        if (!accepted.Accepted)
        {
            await WriteResponseAsync(requestId, false, $"未找到待处理权限请求：{callId}", new { callId }).ConfigureAwait(false);
            return;
        }

        await WriteResponseAsync(
                requestId,
                true,
                "已提交权限响应。",
                new
                {
                    callId,
                    scope = response.Scope == ControlPlanePermissionScope.Session ? "session" : "turn",
                    permissionCount = response.Permissions.Count,
                })
            .ConfigureAwait(false);

        var sessionSnapshot = await GetSessionSnapshotAsync(runtime).ConfigureAwait(false);
        await WriteRuntimeStateAsync(
                "busy",
                "已提交权限响应，等待运行时继续执行。",
                new
                {
                    callId,
                    scope = response.Scope == ControlPlanePermissionScope.Session ? "session" : "turn",
                    permissionCount = response.Permissions.Count,
                    threadId = sessionSnapshot.ActiveThreadId?.Value,
                })
            .ConfigureAwait(false);
    }

    private async Task HandleRespondUserInputAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<RespondUserInputPayload>(jsonOptions);
        var callId = Normalize(payload.CallId);
        if (string.IsNullOrWhiteSpace(callId))
        {
            await WriteResponseAsync(requestId, false, "补录 callId 不能为空。", null).ConfigureAwait(false);
            return;
        }

        var answers = new ControlPlaneUserInputSubmission
        {
            CallId = new CallId(callId),
            Answers = ToStructuredDictionary(payload.Answers) ?? new Dictionary<string, StructuredValue>(StringComparer.Ordinal),
        };
        var accepted = await GetHostGateway().SubmitUserInputAsync(
                new HostSubmitUserInput(
                    new CallId(callId),
                    answers.Answers,
                    context: new HostContext(HostSurfaceKind.Vsix, "sidecar")),
                CancellationToken.None)
            .ConfigureAwait(false);
        if (!accepted.Accepted)
        {
            await WriteResponseAsync(requestId, false, $"未找到待处理补录：{callId}", new { callId }).ConfigureAwait(false);
            return;
        }

        await WriteResponseAsync(
                requestId,
                true,
                "已提交补录内容。",
                new
                {
                    callId,
                    answerCount = answers.Answers.Count,
                })
            .ConfigureAwait(false);

        var sessionSnapshot = await GetSessionSnapshotAsync(runtime).ConfigureAwait(false);
        await WriteRuntimeStateAsync(
                "busy",
                "已提交补录内容，等待运行时继续执行。",
                new
                {
                    callId,
                    answerCount = answers.Answers.Count,
                    threadId = sessionSnapshot.ActiveThreadId?.Value,
                })
            .ConfigureAwait(false);
    }

    private async Task HandleReadThreadAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<ReadThreadPayload>(jsonOptions);
        var threadId = Normalize(payload.ThreadId);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await WriteResponseAsync(requestId, false, "读取线程时 threadId 不能为空。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetHostGateway().ReadThreadAsync(
                new HostReadThreadQuery
                {
                    ThreadId = new ThreadId(threadId),
                    IncludeTurns = payload.IncludeTurns,
                },
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "线程读取成功。", BuildThreadOperationResponse(result)).ConfigureAwait(false);
    }

    private async Task HandleListLoadedThreadsAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<LoadedThreadListPayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Conversations.ListLoadedThreadsAsync(
                BuildDirectThreadLoadedListRequest(payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(
                requestId,
                true,
                $"已加载 {result.ThreadIds.Count} 条已载入线程。",
                BuildLoadedThreadListResponse(result))
            .ConfigureAwait(false);
    }

    private async Task HandleCompactThreadAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<ThreadCompactPayload>(jsonOptions);
        var threadId = Normalize(payload.ThreadId);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await WriteResponseAsync(requestId, false, "压缩线程时 threadId 不能为空。", null).ConfigureAwait(false);
            return;
        }

        if (payload.KeepRecentTurns is null)
        {
            await WriteResponseAsync(requestId, false, "压缩线程时 keepRecentTurns 不能为空。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Conversations.CompactThreadAsync(
                BuildDirectThreadCompactRequest(payload, threadId),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "线程压缩请求已提交。", result).ConfigureAwait(false);
    }

    private async Task HandleCleanBackgroundTerminalsAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<ThreadIdPayload>(jsonOptions);
        var threadId = Normalize(payload.ThreadId);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await WriteResponseAsync(requestId, false, "清理后台终端时 threadId 不能为空。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Conversations.CleanBackgroundTerminalsAsync(
                BuildDirectCleanBackgroundTerminalsRequest(threadId),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "后台终端清理请求已提交。", result).ConfigureAwait(false);
    }

    private async Task HandleUnsubscribeThreadAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<ThreadIdPayload>(jsonOptions);
        var threadId = Normalize(payload.ThreadId);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await WriteResponseAsync(requestId, false, "取消订阅线程时 threadId 不能为空。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Conversations.UnsubscribeThreadAsync(
                BuildDirectThreadUnsubscribeRequest(threadId),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "线程已取消订阅。", BuildThreadUnsubscribeResponse(result)).ConfigureAwait(false);
    }

    private async Task HandleIncrementThreadElicitationAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<ThreadIdPayload>(jsonOptions);
        var threadId = Normalize(payload.ThreadId);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await WriteResponseAsync(requestId, false, "增加挂起交互计数时 threadId 不能为空。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Conversations.IncrementThreadElicitationAsync(
                BuildDirectIncrementThreadElicitationRequest(threadId),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "线程挂起交互计数已增加。", BuildThreadElicitationResponse(result)).ConfigureAwait(false);
    }

    private async Task HandleDecrementThreadElicitationAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<ThreadIdPayload>(jsonOptions);
        var threadId = Normalize(payload.ThreadId);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await WriteResponseAsync(requestId, false, "减少挂起交互计数时 threadId 不能为空。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Conversations.DecrementThreadElicitationAsync(
                BuildDirectDecrementThreadElicitationRequest(threadId),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "线程挂起交互计数已减少。", BuildThreadElicitationResponse(result)).ConfigureAwait(false);
    }

    private async Task HandleGetThreadProjectionAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Conversations.GetThreadProjectionAsync(
                BuildRuntimeThreadProjectionRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "线程投影读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleUnarchiveThreadAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<UnarchiveThreadPayload>(jsonOptions);
        var threadId = Normalize(payload.ThreadId);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await WriteResponseAsync(requestId, false, "取消归档线程时 threadId 不能为空。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetHostGateway().UnarchiveThreadAsync(
                new HostUnarchiveThread
                {
                    ThreadId = new ThreadId(threadId),
                },
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "线程已取消归档。", BuildThreadOperationResponse(result)).ConfigureAwait(false);
    }

    private async Task HandleRollbackThreadAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<RollbackThreadPayload>(jsonOptions);
        var threadId = Normalize(payload.ThreadId);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await WriteResponseAsync(requestId, false, "回滚线程时 threadId 不能为空。", null).ConfigureAwait(false);
            return;
        }

        if (payload.NumTurns <= 0)
        {
            await WriteResponseAsync(requestId, false, "回滚线程时 numTurns 必须大于 0。", null).ConfigureAwait(false);
            return;
        }

        var rollbackRequest = BuildDirectThreadRollbackRequest(payload, threadId);
        var result = await GetHostGateway().RollbackThreadAsync(
                new HostRollbackThread
                {
                    ThreadId = rollbackRequest.ThreadId,
                    NumTurns = rollbackRequest.NumTurns,
                },
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "线程回滚请求已完成。", BuildThreadOperationResponse(result)).ConfigureAwait(false);
    }

    private async Task HandleUpdateThreadMetadataAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<ThreadMetadataUpdatePayload>(jsonOptions);
        var threadId = Normalize(payload.ThreadId);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await WriteResponseAsync(requestId, false, "更新线程元数据时 threadId 不能为空。", null).ConfigureAwait(false);
            return;
        }

        var metadataRequest = BuildDirectThreadMetadataUpdateRequest(payload, threadId);
        var result = await GetHostGateway().UpdateThreadMetadataAsync(
                new HostUpdateThreadMetadata
                {
                    ThreadId = metadataRequest.ThreadId,
                    HasGitSha = metadataRequest.HasGitSha,
                    GitSha = metadataRequest.GitSha,
                    HasGitBranch = metadataRequest.HasGitBranch,
                    GitBranch = metadataRequest.GitBranch,
                    HasGitOriginUrl = metadataRequest.HasGitOriginUrl,
                    GitOriginUrl = metadataRequest.GitOriginUrl,
                },
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "线程元数据已更新。", BuildThreadOperationResponse(result)).ConfigureAwait(false);
    }

    private async Task HandleReadConfigAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<ConfigReadPayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Catalog.ReadConfigAsync(BuildDirectConfigReadRequest(payload), CancellationToken.None).ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "配置读取成功。", BuildConfigReadResponse(result, payload.IncludeLayers)).ConfigureAwait(false);
    }

    private async Task HandleListModelsAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<ModelListPayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Catalog.ListModelsAsync(BuildDirectModelListRequest(payload), CancellationToken.None).ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "模型列表读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleGetCapabilityCatalogAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<CapabilityCatalogPayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Catalog.GetCapabilityCatalogAsync(BuildDirectCapabilityCatalogRequest(payload), CancellationToken.None).ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "能力目录读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleResolveEngineBindingAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<ResolveEngineBindingPayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Catalog.ResolveEngineBindingAsync(BuildDirectResolveEngineBindingRequest(payload), CancellationToken.None).ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "引擎绑定解析成功。", result).ConfigureAwait(false);
    }

    private async Task HandleListAgentsAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<AgentListPayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Agents.ListAgentsAsync(BuildDirectAgentListRequest(payload), CancellationToken.None).ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "代理列表读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleGetAgentRosterProjectionAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Agents.GetAgentRosterProjectionAsync(
                BuildRuntimeAgentRosterRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "代理花名册读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleGetTeamProjectionAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Agents.GetTeamProjectionAsync(
                BuildRuntimeTeamProjectionRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "团队投影读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleRegisterAgentThreadAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<RegisterAgentThreadPayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Agents.RegisterAgentThreadAsync(BuildDirectRegisterAgentThreadRequest(payload), CancellationToken.None).ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "Agent 线程登记成功。", BuildAgentThreadRegistrationResponse(result)).ConfigureAwait(false);
    }

    private async Task HandleCreateAgentJobAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<CreateAgentJobPayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Workflows.CreateJobAsync(BuildDirectCreateAgentJobRequest(payload), CancellationToken.None).ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "Agent 作业创建成功。", BuildAgentJobOperationResponse(result)).ConfigureAwait(false);
    }

    private async Task HandleDispatchAgentJobAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<DispatchAgentJobPayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Workflows.DispatchJobAsync(BuildDirectDispatchAgentJobRequest(payload), CancellationToken.None).ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "Agent 作业派发成功。", BuildAgentJobOperationResponse(result)).ConfigureAwait(false);
    }

    private async Task HandleReportAgentJobItemAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<ReportAgentJobItemPayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Workflows.ReportJobItemAsync(BuildDirectReportAgentJobItemRequest(payload), CancellationToken.None).ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "Agent 作业项回报成功。", BuildAgentJobOperationResponse(result)).ConfigureAwait(false);
    }

    private async Task HandleReadAgentJobAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<ReadAgentJobPayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Workflows.ReadJobAsync(BuildDirectReadAgentJobRequest(payload), CancellationToken.None).ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "Agent 作业读取成功。", BuildAgentJobOperationResponse(result)).ConfigureAwait(false);
    }

    private async Task HandleCreateWorkflowAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<CreateWorkflowPayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Workflows.CreateWorkflowAsync(BuildDirectCreateWorkflowRequest(payload), CancellationToken.None).ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "工作流创建成功。", result).ConfigureAwait(false);
    }

    private async Task HandlePublishWorkflowPlanAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<PublishWorkflowPlanPayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Workflows.PublishPlanAsync(BuildDirectPublishWorkflowPlanRequest(payload), CancellationToken.None).ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "工作流计划发布成功。", result).ConfigureAwait(false);
    }

    private async Task HandleCreateWorkflowTaskAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<CreateWorkflowTaskPayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Workflows.CreateTaskAsync(BuildDirectCreateWorkflowTaskRequest(payload), CancellationToken.None).ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "工作流任务创建成功。", result).ConfigureAwait(false);
    }

    private async Task HandleUpdateWorkflowTaskStateAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<UpdateWorkflowTaskStatePayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Workflows.UpdateTaskStateAsync(BuildDirectUpdateWorkflowTaskStateRequest(payload), CancellationToken.None).ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "工作流任务状态更新成功。", result).ConfigureAwait(false);
    }

    private async Task HandleCreateCollaborationSpaceAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<CreateCollaborationSpacePayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Collaboration.CreateSpaceAsync(BuildDirectCreateCollaborationSpaceRequest(payload), CancellationToken.None).ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "协作空间创建成功。", result).ConfigureAwait(false);
    }

    private async Task HandleConfigureCollaborationSpaceAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<ConfigureCollaborationSpacePayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Collaboration.ConfigureSpaceAsync(BuildDirectConfigureCollaborationSpaceRequest(payload), CancellationToken.None).ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "协作空间配置成功。", result).ConfigureAwait(false);
    }

    private async Task HandleArchiveCollaborationSpaceAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<ArchiveCollaborationSpacePayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Collaboration.ArchiveSpaceAsync(BuildDirectArchiveCollaborationSpaceRequest(payload), CancellationToken.None).ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "协作空间归档完成。", result).ConfigureAwait(false);
    }

    private async Task HandleGetCollaborationSpaceOverviewAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Collaboration.GetSpaceOverviewAsync(
                BuildRuntimeCollaborationSpaceOverviewRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "协作空间概览读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleGetCollaborationSpaceProjectionAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Collaboration.GetSpaceProjectionAsync(
                BuildRuntimeCollaborationSpaceProjectionRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "协作空间投影读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleListCollaborationSpacesAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Collaboration.ListSpacesAsync(
                BuildRuntimeCollaborationSpaceListRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "协作空间列表读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleBindParticipantToSessionAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<BindParticipantToSessionPayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Collaboration.BindParticipantToSessionAsync(BuildDirectBindParticipantToSessionRequest(payload), CancellationToken.None).ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "参与者已绑定到会话。", result).ConfigureAwait(false);
    }

    private async Task HandleBindParticipantToWorkflowAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<BindParticipantToWorkflowPayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Collaboration.BindParticipantToWorkflowAsync(BuildDirectBindParticipantToWorkflowRequest(payload), CancellationToken.None).ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "参与者已绑定到工作流。", result).ConfigureAwait(false);
    }

    private async Task HandleUpdateParticipantRoleAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<UpdateParticipantRolePayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Collaboration.UpdateParticipantRoleAsync(BuildDirectUpdateParticipantRoleRequest(payload), CancellationToken.None).ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "参与者角色更新成功。", result).ConfigureAwait(false);
    }

    private async Task HandleGetParticipantProjectionAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Collaboration.GetParticipantProjectionAsync(
                BuildRuntimeParticipantProjectionRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "参与者投影读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleGetParticipantViewProjectionAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Collaboration.GetParticipantViewProjectionAsync(
                BuildRuntimeParticipantViewProjectionRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "参与者视图读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleListParticipantsInScopeAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Collaboration.ListParticipantsInScopeAsync(
                BuildRuntimeParticipantListRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "参与者列表读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleGetSessionOverviewAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Sessions.GetSessionOverviewAsync(
                BuildRuntimeSessionOverviewRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "会话概览读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleGetSessionSnapshotAsync(string requestId)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Sessions.GetSnapshotAsync(CancellationToken.None).ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "会话快照读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleListSessionsAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Sessions.ListSessionsAsync(
                BuildRuntimeSessionListRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "会话列表读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleGetApprovalQueueProjectionAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Governance.GetApprovalQueueProjectionAsync(
                BuildRuntimeApprovalQueueRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "审批队列读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleListUserInputRequestsAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Governance.ListUserInputRequestsAsync(
                BuildRuntimeUserInputListRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "补录请求列表读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleGetArtifactProjectionAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Artifacts.GetArtifactProjectionAsync(
                BuildRuntimeArtifactDetailRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "工件投影读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleGetArtifactCollectionProjectionAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Artifacts.GetArtifactCollectionProjectionAsync(
                BuildRuntimeArtifactListRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "工件集合读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleGetWorkflowBoardAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Workflows.GetWorkflowBoardProjectionAsync(
                BuildRuntimeWorkflowBoardRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "工作流看板读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleGetTaskBoardAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Workflows.GetTaskBoardProjectionAsync(
                BuildRuntimeTaskBoardRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "任务看板读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleGetPlanProjectionAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Workflows.GetPlanProjectionAsync(
                BuildRuntimePlanProjectionRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "计划投影读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleGetAccountProfileAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Identity.GetAccountProfileAsync(
                BuildRuntimeAccountProfileRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "账户资料读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleListBoundDevicesAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Identity.ListBoundDevicesAsync(
                BuildRuntimeBoundDeviceListRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "绑定设备列表读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleListMemorySpacesAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Memory.ListMemorySpacesAsync(
                BuildRuntimeMemorySpaceListRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "记忆空间列表读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleResolveMemoryOverlayAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Memory.ResolveMemoryOverlayAsync(
                BuildRuntimeMemoryOverlayRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "记忆 overlay 读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleListMemoryProvidersAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Memory.ListMemoryProvidersAsync(
                BuildRuntimeMemoryProviderListRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "记忆 provider 列表读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleFilterMemoryAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Memory.FilterMemoryAsync(
                BuildRuntimeFilterMemoryRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "记忆筛选成功。", result).ConfigureAwait(false);
    }

    private async Task HandleListMemoryReviewsAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Memory.ListMemoryReviewsAsync(
                BuildRuntimeMemoryReviewListRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "记忆审核列表读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleAddMemoryAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Memory.AddMemoryAsync(
                BuildRuntimeAddMemoryRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "记忆写入成功。", result).ConfigureAwait(false);
    }

    private async Task HandleExtractMemoryAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Memory.ExtractMemoryAsync(
                BuildRuntimeExtractMemoryRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "记忆抽取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleImportMemoryAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Memory.ImportMemoryAsync(
                BuildRuntimeImportMemoryRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "记忆导入已提交。", result).ConfigureAwait(false);
    }

    private async Task HandleExportMemoryAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Memory.ExportMemoryAsync(
                BuildRuntimeExportMemoryRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "记忆导出成功。", result).ConfigureAwait(false);
    }

    private async Task HandleBindMemoryProviderAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Memory.BindMemoryProviderAsync(
                BuildRuntimeBindMemoryProviderRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "记忆 provider 绑定成功。", result).ConfigureAwait(false);
    }

    private async Task HandleForgetMemoryAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Memory.ForgetMemoryAsync(
                BuildRuntimeForgetMemoryRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "记忆遗忘成功。", result).ConfigureAwait(false);
    }

    private async Task HandleDeleteMemoryAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Memory.DeleteMemoryAsync(
                BuildRuntimeDeleteMemoryRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "记忆删除成功。", result).ConfigureAwait(false);
    }

    private async Task HandleSupersedeMemoryAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Memory.SupersedeMemoryAsync(
                BuildRuntimeSupersedeMemoryRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "记忆取代链记录成功。", result).ConfigureAwait(false);
    }

    private async Task HandleApproveMemoryReviewAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Memory.ApproveMemoryReviewAsync(
                BuildRuntimeApproveMemoryReviewRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "记忆审核批准成功。", result).ConfigureAwait(false);
    }

    private async Task HandleDemoteMemoryReviewAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Memory.DemoteMemoryReviewAsync(
                BuildRuntimeDemoteMemoryReviewRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "记忆审核降权成功。", result).ConfigureAwait(false);
    }

    private async Task HandleMergeMemoryReviewAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Memory.MergeMemoryReviewAsync(
                BuildRuntimeMergeMemoryReviewRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "记忆审核合并成功。", result).ConfigureAwait(false);
    }

    private async Task HandleRestoreMemoryReviewAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Memory.RestoreMemoryReviewAsync(
                BuildRuntimeRestoreMemoryReviewRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "记忆审核恢复成功。", result).ConfigureAwait(false);
    }

    private async Task HandleRecordMemoryFeedbackAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Memory.RecordMemoryFeedbackAsync(
                BuildRuntimeRecordMemoryFeedbackRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "记忆反馈记录成功。", result).ConfigureAwait(false);
    }

    private async Task HandleRecordMemoryCitationAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Memory.RecordMemoryCitationAsync(
                BuildRuntimeRecordMemoryCitationRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "记忆引用记录成功。", result).ConfigureAwait(false);
    }

    private async Task HandleGetExecutionTraceAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Diagnostics.GetExecutionTraceAsync(
                BuildRuntimeExecutionTraceRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "执行轨迹读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleListAttemptSummariesAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Diagnostics.ListAttemptSummariesAsync(
                BuildRuntimeAttemptSummaryListRequest(request.Payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "尝试摘要列表读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleUploadFeedbackAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<FeedbackUploadPayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Diagnostics.UploadFeedbackAsync(
                BuildDirectFeedbackUploadRequest(payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "feedback 上传请求已提交。", result).ConfigureAwait(false);
    }

    private async Task HandleSearchFuzzyFilesAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<FuzzyFileSearchPayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Conversations.SearchFuzzyFilesAsync(
                BuildDirectFuzzyFileSearchSearchRequest(payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "模糊文件搜索成功。", result).ConfigureAwait(false);
    }

    private async Task HandleStartFuzzyFileSearchSessionAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<FuzzyFileSearchSessionStartPayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Conversations.StartFuzzyFileSearchSessionAsync(
                BuildDirectFuzzyFileSearchSessionStartRequest(payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "模糊文件搜索会话已启动。", result).ConfigureAwait(false);
    }

    private async Task HandleUpdateFuzzyFileSearchSessionAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<FuzzyFileSearchSessionUpdatePayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Conversations.UpdateFuzzyFileSearchSessionAsync(
                BuildDirectFuzzyFileSearchSessionUpdateRequest(payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "模糊文件搜索会话已更新。", result).ConfigureAwait(false);
    }

    private async Task HandleStopFuzzyFileSearchSessionAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<FuzzyFileSearchSessionStopPayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Conversations.StopFuzzyFileSearchSessionAsync(
                BuildDirectFuzzyFileSearchSessionStopRequest(payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "模糊文件搜索会话已停止。", result).ConfigureAwait(false);
    }

    private async Task HandleStartRealtimeAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<RealtimeStartPayload>(jsonOptions);
        var sessionSnapshot = await GetSessionSnapshotAsync(runtime).ConfigureAwait(false);
        var result = await GetControlPlane(runtime).Conversations.StartRealtimeAsync(
                BuildDirectRealtimeStartRequest(payload, sessionSnapshot.ActiveThreadId?.Value),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "realtime 会话已启动。", result).ConfigureAwait(false);
    }

    private async Task HandleAppendRealtimeTextAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<RealtimeAppendTextPayload>(jsonOptions);
        var sessionSnapshot = await GetSessionSnapshotAsync(runtime).ConfigureAwait(false);
        var result = await GetControlPlane(runtime).Conversations.AppendRealtimeTextAsync(
                BuildDirectRealtimeAppendTextRequest(payload, sessionSnapshot.ActiveThreadId?.Value),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "realtime 文本追加请求已提交。", result).ConfigureAwait(false);
    }

    private async Task HandleAppendRealtimeAudioAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<RealtimeAppendAudioPayload>(jsonOptions);
        var sessionSnapshot = await GetSessionSnapshotAsync(runtime).ConfigureAwait(false);
        var result = await GetControlPlane(runtime).Conversations.AppendRealtimeAudioAsync(
                BuildDirectRealtimeAppendAudioRequest(payload, sessionSnapshot.ActiveThreadId?.Value),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "realtime 音频追加请求已提交。", result).ConfigureAwait(false);
    }

    private async Task HandleHandoffRealtimeOutputAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<RealtimeHandoffOutputPayload>(jsonOptions);
        var sessionSnapshot = await GetSessionSnapshotAsync(runtime).ConfigureAwait(false);
        var result = await GetControlPlane(runtime).Conversations.HandoffRealtimeOutputAsync(
                BuildDirectRealtimeHandoffOutputRequest(payload, sessionSnapshot.ActiveThreadId?.Value),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "realtime 输出移交请求已提交。", result).ConfigureAwait(false);
    }

    private async Task HandleStopRealtimeAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<RealtimeStopPayload>(jsonOptions);
        var sessionSnapshot = await GetSessionSnapshotAsync(runtime).ConfigureAwait(false);
        var result = await GetControlPlane(runtime).Conversations.StopRealtimeAsync(
                BuildDirectRealtimeStopRequest(payload, sessionSnapshot.ActiveThreadId?.Value),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "realtime 停止请求已提交。", result).ConfigureAwait(false);
    }

    private async Task HandleWriteConfigValueAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<ConfigValueWritePayload>(jsonOptions);
        if (string.IsNullOrWhiteSpace(Normalize(payload.KeyPath)))
        {
            await WriteResponseAsync(requestId, false, "写入配置项时 keyPath 不能为空。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Catalog.WriteConfigValueAsync(BuildDirectConfigValueWriteRequest(payload), CancellationToken.None).ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "配置项写入成功。", BuildConfigWriteResponse(result)).ConfigureAwait(false);
    }

    private async Task HandleWriteConfigBatchAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<ConfigBatchWritePayload>(jsonOptions);
        var items = payload.Items ?? [];
        if (items.Count == 0)
        {
            await WriteResponseAsync(requestId, false, "批量写入配置项时 items 不能为空。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Catalog.WriteConfigBatchAsync(BuildDirectConfigBatchWriteRequest(payload), CancellationToken.None).ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "配置批量写入成功。", BuildConfigWriteResponse(result)).ConfigureAwait(false);
    }

    private async Task HandleReadConfigRequirementsAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<ConfigRequirementsReadPayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Catalog.ReadConfigRequirementsAsync(
                BuildDirectConfigRequirementsReadRequest(payload),
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "配置要求读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleListExperimentalFeaturesAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<ExperimentalFeatureListPayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Catalog.ListExperimentalFeaturesAsync(
                new ControlPlaneExperimentalFeatureQuery
                {
                    Limit = payload.Limit,
                    Cursor = Normalize(payload.Cursor),
                },
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "实验特性列表读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleListCollaborationModesAsync(string requestId)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Catalog.ListCollaborationModesAsync(CancellationToken.None).ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "协作模式列表读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleListMcpServerStatusAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<McpServerStatusListPayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Catalog.ListMcpServerStatusAsync(
                new ControlPlaneMcpServerStatusQuery
                {
                    Limit = payload.Limit,
                    Cursor = Normalize(payload.Cursor),
                },
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "MCP Server 状态读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleReloadMcpServersAsync(string requestId)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Catalog.ReloadMcpServersAsync(CancellationToken.None).ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "MCP Servers 已刷新。", result).ConfigureAwait(false);
    }

    private async Task HandleListSkillsAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<SkillsListPayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Catalog.ListSkillsAsync(BuildDirectSkillsListRequest(payload), CancellationToken.None).ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "Skills 列表读取成功。", BuildDirectSkillsListResponse(result)).ConfigureAwait(false);
    }

    private async Task HandleWriteSkillConfigAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<SkillsConfigWritePayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Catalog.WriteSkillConfigAsync(BuildDirectSkillsConfigWriteRequest(payload), CancellationToken.None).ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "Skills 配置写入成功。", result).ConfigureAwait(false);
    }

    private async Task HandleListRemoteSkillsAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<SkillsRemoteListPayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Catalog.ListRemoteSkillsAsync(
                new ControlPlaneRemoteSkillCatalogQuery
                {
                    HazelnutScope = Normalize(payload.HazelnutScope),
                    ProductSurface = Normalize(payload.ProductSurface),
                    Enabled = payload.Enabled,
                },
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "远程 Skills 列表读取成功。", result).ConfigureAwait(false);
    }

    private async Task HandleExportRemoteSkillAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<SkillsRemoteExportPayload>(jsonOptions);
        var hazelnutId = Normalize(payload.HazelnutId);
        if (string.IsNullOrWhiteSpace(hazelnutId))
        {
            await WriteResponseAsync(requestId, false, "导出远程 Skill 时 hazelnutId 不能为空。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Catalog.ExportRemoteSkillAsync(
                new ControlPlaneRemoteSkillExportCommand
                {
                    HazelnutId = hazelnutId!,
                },
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "远程 Skill 导出成功。", result).ConfigureAwait(false);
    }

    private async Task HandleListPluginsAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<PluginListPayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Catalog.ListPluginsAsync(BuildDirectPluginListRequest(payload), CancellationToken.None).ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "插件列表读取成功。", BuildPluginListResponse(result)).ConfigureAwait(false);
    }

    private async Task HandleReadPluginAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<PluginReadPayload>(jsonOptions);
        if (string.IsNullOrWhiteSpace(Normalize(payload.MarketplacePath)) || string.IsNullOrWhiteSpace(Normalize(payload.PluginName)))
        {
            await WriteResponseAsync(requestId, false, "读取插件详情时 marketplacePath 和 pluginName 不能为空。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Catalog.ReadPluginAsync(BuildDirectPluginReadRequest(payload), CancellationToken.None).ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "插件详情读取成功。", BuildPluginReadResponse(result)).ConfigureAwait(false);
    }

    private async Task HandleInstallPluginAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<PluginInstallPayload>(jsonOptions);
        if (string.IsNullOrWhiteSpace(Normalize(payload.MarketplacePath)) || string.IsNullOrWhiteSpace(Normalize(payload.PluginName)))
        {
            await WriteResponseAsync(requestId, false, "安装插件时 marketplacePath 和 pluginName 不能为空。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Catalog.InstallPluginAsync(BuildDirectPluginInstallRequest(payload), CancellationToken.None).ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "插件安装已提交。", BuildPluginInstallResponse(result)).ConfigureAwait(false);
    }

    private async Task HandleUninstallPluginAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<PluginUninstallPayload>(jsonOptions);
        var result = await GetControlPlane(runtime).Catalog.UninstallPluginAsync(BuildDirectPluginUninstallRequest(payload), CancellationToken.None).ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "插件卸载已提交。", result).ConfigureAwait(false);
    }

    private async Task HandleListAppsAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<AppListPayload>(jsonOptions);
        var threadId = Normalize(payload.ThreadId);
        var result = await GetControlPlane(runtime).Catalog.ListAppsAsync(
                new ControlPlaneAppCatalogQuery
                {
                    Limit = payload.Limit,
                    Cursor = Normalize(payload.Cursor),
                    ThreadId = string.IsNullOrWhiteSpace(threadId) ? null : new ThreadId(threadId),
                    ForceRefetch = payload.ForceRefetch,
                },
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "应用列表读取成功。", BuildDirectAppListResponse(result)).ConfigureAwait(false);
    }

    private async Task HandleStartReviewAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<ReviewStartPayload>(jsonOptions);
        var threadId = Normalize(payload.ThreadId);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await WriteResponseAsync(requestId, false, "启动审查时 threadId 不能为空。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Workflows.StartReviewAsync(
                new ControlPlaneReviewStartCommand
                {
                    ThreadId = threadId!,
                    Delivery = Normalize(payload.Delivery),
                    Target = BuildDirectReviewTarget(payload),
                },
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "审查线程已启动。", result).ConfigureAwait(false);
    }

    private async Task HandleStartMcpServerOauthLoginAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<McpServerOauthLoginPayload>(jsonOptions);
        var name = Normalize(payload.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            await WriteResponseAsync(requestId, false, "MCP OAuth 登录时 name 不能为空。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetControlPlane(runtime).Catalog.StartMcpServerOauthLoginAsync(
                new ControlPlaneMcpServerOauthLoginStartCommand
                {
                    Name = name!,
                    TimeoutSecs = payload.TimeoutSecs,
                },
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "MCP OAuth 登录已启动。", result).ConfigureAwait(false);
    }

    private async Task HandleGetConversationSummaryAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<ConversationSummaryPayload>(jsonOptions);
        var result = await GetHostGateway().GetConversationSummaryAsync(
                new HostReadConversationSummaryQuery
                {
                    ThreadId = string.IsNullOrWhiteSpace(Normalize(payload.ThreadId)) ? null : new ThreadId(payload.ThreadId!),
                    RolloutPath = Normalize(payload.RolloutPath),
                },
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "会话摘要读取成功。", BuildConversationSummaryResponse(result.Artifact)).ConfigureAwait(false);
    }

    private async Task HandleGetGitDiffToRemoteAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<GitDiffToRemotePayload>(jsonOptions);
        var threadId = Normalize(payload.ThreadId);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await WriteResponseAsync(requestId, false, "读取远端差异时 threadId 不能为空。", null).ConfigureAwait(false);
            return;
        }

        var result = await GetHostGateway().GetGitDiffToRemoteAsync(
                new HostReadGitDiffToRemoteQuery
                {
                    ThreadId = new ThreadId(threadId!),
                },
                CancellationToken.None)
            .ConfigureAwait(false);
        await WriteResponseAsync(requestId, true, "远端差异读取成功。", result.Artifact).ConfigureAwait(false);
    }

    private async Task HandleInvokeCapabilityAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<InvokeCapabilityPayload>(jsonOptions);
        var capability = Normalize(payload.Capability);
        if (string.IsNullOrWhiteSpace(capability))
        {
            await WriteResponseAsync(requestId, false, "capability 不能为空。", null).ConfigureAwait(false);
            return;
        }

        var method = Normalize(payload.Method);
        var parameters = ParseParametersJson(payload.ParametersJson);

        switch (capability!.ToLowerInvariant())
        {
            case "runtimesurface":
            {
                var result = await InvokeRuntimeSurfaceAsync(runtime, method, parameters, CancellationToken.None).ConfigureAwait(false);
                await WriteResponseAsync(
                        requestId,
                        true,
                        $"runtime surface 调用成功：{method ?? "<null>"}",
                        result)
                    .ConfigureAwait(false);
                return;
            }
            case "commandexecution":
            {
                var result = await InvokeCommandExecutionAsync(runtime.AsNorthboundSurface().Execution, method, parameters, CancellationToken.None).ConfigureAwait(false);
                await WriteResponseAsync(
                        requestId,
                        true,
                        $"command execution 调用成功：{method ?? "<null>"}",
                        result)
                    .ConfigureAwait(false);
                return;
            }
            case "codemodeexec":
            {
                var sessionSnapshot = await GetSessionSnapshotAsync(runtime).ConfigureAwait(false);
                var requestModel = BuildCodeModeExecRequest(parameters, sessionSnapshot.ActiveThreadId?.Value);
                var result = await runtime.AsNorthboundSurface().Execution.ExecuteCodeModeAsync(requestModel, CancellationToken.None).ConfigureAwait(false);
                await WriteResponseAsync(requestId, true, "code mode exec 调用成功。", BuildCodeModeResultPayload(result)).ConfigureAwait(false);
                return;
            }
            case "codemodewait":
            {
                var sessionSnapshot = await GetSessionSnapshotAsync(runtime).ConfigureAwait(false);
                var requestModel = BuildCodeModeWaitRequest(parameters, sessionSnapshot.ActiveThreadId?.Value);
                var result = await runtime.AsNorthboundSurface().Execution.WaitCodeModeAsync(requestModel, CancellationToken.None).ConfigureAwait(false);
                await WriteResponseAsync(requestId, true, "code mode wait 调用成功。", BuildCodeModeResultPayload(result)).ConfigureAwait(false);
                return;
            }
            case "fuzzyfilesearch":
            {
                var result = await InvokeFuzzyFileSearchAsync(runtime, method, parameters, CancellationToken.None).ConfigureAwait(false);
                await WriteResponseAsync(
                        requestId,
                        true,
                        $"fuzzy file search 调用成功：{method ?? "<null>"}",
                        result)
                    .ConfigureAwait(false);
                return;
            }
            case "threadoperation":
            {
                var result = await InvokeThreadOperationAsync(runtime, method, parameters, CancellationToken.None).ConfigureAwait(false);
                await WriteResponseAsync(
                        requestId,
                        true,
                        $"thread operation 调用成功：{method ?? "<null>"}",
                        result)
                    .ConfigureAwait(false);
                return;
            }
            case "realtime":
            {
                var result = await InvokeRealtimeAsync(runtime, method, parameters, CancellationToken.None).ConfigureAwait(false);
                await WriteResponseAsync(
                        requestId,
                        true,
                        $"realtime 调用成功：{method ?? "<null>"}",
                        result)
                    .ConfigureAwait(false);
                return;
            }
            case "agentoperation":
            {
                var result = await InvokeAgentOperationAsync(runtime, method, parameters, CancellationToken.None).ConfigureAwait(false);
                await WriteResponseAsync(
                        requestId,
                        true,
                        $"agent operation 调用成功：{method ?? "<null>"}",
                        result)
                    .ConfigureAwait(false);
                return;
            }
            case "feedback":
            {
                var result = await GetControlPlane(runtime).Diagnostics.UploadFeedbackAsync(BuildRuntimeFeedbackUploadRequest(parameters), CancellationToken.None).ConfigureAwait(false);
                await WriteResponseAsync(requestId, true, "feedback 上传请求已提交。", result).ConfigureAwait(false);
                return;
            }
            case "windowssandbox":
            {
                var result = await InvokeWindowsSandboxSetupAsync(runtime.AsNorthboundSurface().Environment, parameters, CancellationToken.None).ConfigureAwait(false);
                await WriteResponseAsync(requestId, true, "Windows Sandbox setup 请求已提交。", result).ConfigureAwait(false);
                return;
            }
            default:
                await WriteResponseAsync(requestId, false, $"未知 capability：{capability}", null).ConfigureAwait(false);
                return;
        }
    }

    private async Task HandleInvokeRuntimeSurfaceAsync(string requestId, SidecarRequestEnvelope request)
    {
        if (runtime is null)
        {
            await WriteResponseAsync(requestId, false, "运行时尚未初始化。", null).ConfigureAwait(false);
            return;
        }

        var payload = request.DeserializePayload<InvokeRuntimeSurfacePayload>(jsonOptions);
        var method = Normalize(payload.Method);
        if (string.IsNullOrWhiteSpace(method))
        {
            await WriteResponseAsync(requestId, false, "RPC method 不能为空。", null).ConfigureAwait(false);
            return;
        }

        var formalParameters = ParseParametersJson(payload.ParametersJson);
        var formalDispatch = await TryInvokeFormalRuntimeDispatchAsync(runtime, method, formalParameters, CancellationToken.None).ConfigureAwait(false);
        if (formalDispatch.Handled)
        {
            await WriteResponseAsync(requestId, true, $"runtime surface 调用成功：{method}", formalDispatch.Result).ConfigureAwait(false);
            return;
        }

        await WriteResponseAsync(requestId, false, BuildFormalRpcUnavailableMessage(method), null).ConfigureAwait(false);
    }

    private async Task<FormalRuntimeDispatchResult> TryInvokeFormalRuntimeDispatchAsync(
        IExecutionRuntime runtime,
        string method,
        object parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            var runtimeSurfaceResult = await InvokeRuntimeSurfaceAsync(runtime, method, parameters, cancellationToken).ConfigureAwait(false);
            return new FormalRuntimeDispatchResult(true, runtimeSurfaceResult);
        }
        catch (InvalidOperationException ex) when (IsUnknownRuntimeSurfaceMethod(ex))
        {
        }

        var normalizedMethod = Normalize(method)?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedMethod))
        {
            return default;
        }

        switch (normalizedMethod)
        {
            case "fuzzyfilesearch":
            case "fuzzyfilesearch/sessionstart":
            case "fuzzyfilesearch/sessionupdate":
            case "fuzzyfilesearch/sessionstop":
                return new FormalRuntimeDispatchResult(
                    true,
                    await InvokeFuzzyFileSearchAsync(runtime, normalizedMethod, parameters, cancellationToken).ConfigureAwait(false));
            case "thread/realtime/start":
            case "thread/realtime/appendtext":
            case "thread/realtime/appendaudio":
            case "thread/realtime/handoffoutput":
            case "thread/realtime/stop":
                return new FormalRuntimeDispatchResult(
                    true,
                    await InvokeRealtimeAsync(runtime, normalizedMethod, parameters, cancellationToken).ConfigureAwait(false));
            case "command/exec":
            case "command/exec/write":
            case "command/exec/terminate":
            case "command/exec/resize":
                return new FormalRuntimeDispatchResult(
                    true,
                    await InvokeLegacyCommandExecutionRpcAsync(runtime, normalizedMethod, parameters, cancellationToken).ConfigureAwait(false));
            case "windowssandbox/setupstart":
                return new FormalRuntimeDispatchResult(
                    true,
                    await InvokeWindowsSandboxSetupAsync(runtime.AsNorthboundSurface().Environment, parameters, cancellationToken).ConfigureAwait(false));
        }

        if (normalizedMethod.StartsWith("thread/", StringComparison.Ordinal))
        {
            try
            {
                var threadResult = await InvokeThreadOperationAsync(runtime, normalizedMethod, parameters, cancellationToken).ConfigureAwait(false);
                return new FormalRuntimeDispatchResult(true, threadResult);
            }
            catch (InvalidOperationException ex) when (IsUnknownThreadOperationMethod(ex))
            {
            }
        }

        return default;
    }

    private static string BuildFormalRpcUnavailableMessage(string? method)
        => $"RPC 方法未映射到正式 runtime surface / control-plane：{Normalize(method) ?? "<null>"}。";

    private async Task<object> InvokeFuzzyFileSearchAsync(
        IExecutionRuntime runtime,
        string? method,
        object parameters,
        CancellationToken cancellationToken)
    {
        var controlPlane = GetControlPlane(runtime).Conversations;
        var normalizedMethod = Normalize(method)?.ToLowerInvariant()
            ?? throw new InvalidOperationException($"无效的 fuzzy file search method：{method ?? "<null>"}");

        return normalizedMethod switch
        {
            "fuzzyfilesearch" => await controlPlane.SearchFuzzyFilesAsync(BuildFuzzyFileSearchSearchRequest(parameters), cancellationToken).ConfigureAwait(false),
            "fuzzyfilesearch/sessionstart" => await controlPlane.StartFuzzyFileSearchSessionAsync(BuildFuzzyFileSearchSessionStartRequest(parameters), cancellationToken).ConfigureAwait(false),
            "fuzzyfilesearch/sessionupdate" => await controlPlane.UpdateFuzzyFileSearchSessionAsync(BuildFuzzyFileSearchSessionUpdateRequest(parameters), cancellationToken).ConfigureAwait(false),
            "fuzzyfilesearch/sessionstop" => await controlPlane.StopFuzzyFileSearchSessionAsync(BuildFuzzyFileSearchSessionStopRequest(parameters), cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"无效的 fuzzy file search method：{method ?? "<null>"}"),
        };
    }

    private async Task<object> InvokeRealtimeAsync(
        IExecutionRuntime runtime,
        string? method,
        object parameters,
        CancellationToken cancellationToken)
    {
        var controlPlane = GetControlPlane(runtime).Conversations;
        var sessionSnapshot = await GetSessionSnapshotAsync(runtime, cancellationToken).ConfigureAwait(false);
        return Normalize(method)?.ToLowerInvariant() switch
        {
            "thread/realtime/start" => await controlPlane.StartRealtimeAsync(BuildRealtimeStartRequest(parameters, sessionSnapshot.ActiveThreadId?.Value), cancellationToken).ConfigureAwait(false),
            "thread/realtime/appendtext" => await controlPlane.AppendRealtimeTextAsync(BuildRealtimeAppendTextRequest(parameters, sessionSnapshot.ActiveThreadId?.Value), cancellationToken).ConfigureAwait(false),
            "thread/realtime/appendaudio" => await controlPlane.AppendRealtimeAudioAsync(BuildRealtimeAppendAudioRequest(parameters, sessionSnapshot.ActiveThreadId?.Value), cancellationToken).ConfigureAwait(false),
            "thread/realtime/handoffoutput" => await controlPlane.HandoffRealtimeOutputAsync(BuildRealtimeHandoffOutputRequest(parameters, sessionSnapshot.ActiveThreadId?.Value), cancellationToken).ConfigureAwait(false),
            "thread/realtime/stop" => await controlPlane.StopRealtimeAsync(BuildRealtimeStopRequest(parameters, sessionSnapshot.ActiveThreadId?.Value), cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"无效的 realtime method：{method ?? "<null>"}"),
        };
    }

    private static Task<object> InvokeLegacyCommandExecutionRpcAsync(
        IExecutionRuntime runtime,
        string method,
        object parameters,
        CancellationToken cancellationToken)
        => method switch
        {
            "command/exec" => InvokeCommandExecutionAsync(runtime.AsNorthboundSurface().Execution, "exec", parameters, cancellationToken),
            "command/exec/write" => InvokeCommandExecutionAsync(runtime.AsNorthboundSurface().Execution, "write", parameters, cancellationToken),
            "command/exec/terminate" => InvokeCommandExecutionAsync(runtime.AsNorthboundSurface().Execution, "terminate", parameters, cancellationToken),
            "command/exec/resize" => InvokeCommandExecutionAsync(runtime.AsNorthboundSurface().Execution, "resize", parameters, cancellationToken),
            _ => throw new InvalidOperationException($"无效的 command execution RPC method：{method}"),
        };

    private static bool IsUnknownRuntimeSurfaceMethod(InvalidOperationException exception)
        => exception.Message.StartsWith("无效的 runtime surface method：", StringComparison.Ordinal);

    private static bool IsUnknownThreadOperationMethod(InvalidOperationException exception)
        => exception.Message.StartsWith("无效的 thread operation method：", StringComparison.Ordinal);

    private (ControlPlaneInitializeRuntimeCommand Options, ResolvedTianShuConfig Config) BuildRuntimeOptions(InitializePayload payload)
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var options = new ControlPlaneInitializeRuntimeCommand
        {
            ExecutablePath = "dotnet",
            UseDotNetProjectLauncher = true,
            WorkingDirectory = Normalize(payload.WorkingDirectory) ?? repositoryRoot ?? Environment.CurrentDirectory,
            ConfigFilePath = Normalize(payload.ConfigPath) ?? RuntimeConfigurationComposition.ResolveDefaultPath(),
            ProfileName = Normalize(payload.ProfileName),
            AppHostProjectPath = Normalize(payload.AppHostProjectPath) ?? ResolveAppHostProjectPath(repositoryRoot),
            CreateThreadOnInitialize = payload.CreateThreadOnInitialize ?? true,
            Model = Normalize(payload.Model),
            ModelProvider = Normalize(payload.ModelProvider),
            ApprovalPolicy = Normalize(payload.ApprovalPolicy),
            SandboxMode = Normalize(payload.SandboxMode),
            WebSearchMode = Normalize(payload.WebSearchMode),
            ServiceTier = Normalize(payload.ServiceTier),
            CollaborationMode = Normalize(payload.CollaborationMode),
            SessionSource = ControlPlaneSessionSource.VsCode,
        };

        if (string.IsNullOrWhiteSpace(options.AppHostProjectPath))
        {
            throw new FileNotFoundException("未找到 TianShu 宿主项目文件。", options.AppHostProjectPath);
        }

        RuntimeHostLaunchLocator.ApplyPreferredLaunchMode(options, options.AppHostProjectPath);

        var loader = new RuntimeConfigurationComposition();
        var config = loader.Load(options.ConfigFilePath, options.ProfileName, workingDirectory: options.WorkingDirectory);
        RuntimeConfigurationComposition.ApplyToOptions(options, config);

        return (options, config);
    }

    private async Task RelayRuntimeEventSafelyAsync(ControlPlaneConversationStreamEvent streamEvent)
    {
        try
        {
            await RelayRuntimeEventAsync(streamEvent).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogError("运行时事件转发失败。", ex);
        }
    }

    private async Task RelayRuntimeEventAsync(ControlPlaneConversationStreamEvent streamEvent)
    {
        var approvalRequest = BuildSidecarApprovalRequestPayload(
            ReadTypedPayload<SidecarApprovalRequestPayload>(
                streamEvent,
                ControlPlaneConversationStreamPayloadKind.ApprovalRequest));
        var permissionRequest = BuildSidecarPermissionRequestPayload(
            ReadTypedPayload<SidecarPermissionRequestPayload>(
                streamEvent,
                ControlPlaneConversationStreamPayloadKind.PermissionRequest));
        var userInputRequest = BuildSidecarUserInputRequestPayload(
            ReadTypedPayload<SidecarUserInputRequestPayload>(
                streamEvent,
                ControlPlaneConversationStreamPayloadKind.UserInputRequest));
        var serverRequestResolved = BuildSidecarServerRequestResolvedPayload(
            ReadTypedPayload<SidecarServerRequestResolvedPayload>(
                streamEvent,
                ControlPlaneConversationStreamPayloadKind.ServerRequestResolved));
        var pendingFollowUp = BuildSidecarPendingFollowUpPayload(
            ReadTypedPayload<SidecarPendingFollowUpPayload>(
                streamEvent,
                ControlPlaneConversationStreamPayloadKind.PendingFollowUp));
        var pendingInputState = BuildSidecarPendingInputStatePayload(
            ReadPendingInputStatePayload(streamEvent));
        var committedUserMessage = ResolveStructuredPayloadObject(
            streamEvent,
            ControlPlaneConversationStreamPayloadKind.CommittedUserMessage);
        var eventType = streamEvent.Kind switch
        {
            ControlPlaneConversationStreamEventKind.TurnStarted => EventTurnStarted,
            ControlPlaneConversationStreamEventKind.AssistantTextDelta => EventAssistantDelta,
            ControlPlaneConversationStreamEventKind.AssistantTextCompleted => EventAssistantCompleted,
            ControlPlaneConversationStreamEventKind.ApprovalRequested => EventApprovalRequested,
            ControlPlaneConversationStreamEventKind.PermissionRequested => EventPermissionRequested,
            ControlPlaneConversationStreamEventKind.UserInputRequested => EventRequestUserInput,
            ControlPlaneConversationStreamEventKind.ServerRequestResolved => EventServerRequestResolved,
            ControlPlaneConversationStreamEventKind.TurnCompleted => EventTurnCompleted,
            ControlPlaneConversationStreamEventKind.TurnSteered => EventTurnSteered,
            ControlPlaneConversationStreamEventKind.ToolCallStarted => EventToolCallStarted,
            ControlPlaneConversationStreamEventKind.ToolCallOutputDelta => EventToolCallOutputDelta,
            ControlPlaneConversationStreamEventKind.ToolCallCompleted => EventToolCallCompleted,
            ControlPlaneConversationStreamEventKind.PlanUpdated => EventPlanUpdated,
            ControlPlaneConversationStreamEventKind.DiffUpdated => EventDiffUpdated,
            ControlPlaneConversationStreamEventKind.OperationReported => EventOperationReported,
            ControlPlaneConversationStreamEventKind.McpServerStatusUpdated => EventMcpServerStatusUpdated,
            ControlPlaneConversationStreamEventKind.RawNotification => EventRawNotification,
            ControlPlaneConversationStreamEventKind.ReasoningDelta => EventReasoningDelta,
            ControlPlaneConversationStreamEventKind.TaskStarted => EventTaskStarted,
            ControlPlaneConversationStreamEventKind.TaskCompleted => EventTaskCompleted,
            ControlPlaneConversationStreamEventKind.ItemStarted => EventItemStarted,
            ControlPlaneConversationStreamEventKind.ItemCompleted => EventItemCompleted,
            ControlPlaneConversationStreamEventKind.UserMessageCommitted => EventUserMessageCommitted,
            ControlPlaneConversationStreamEventKind.PendingFollowUpUpdated => EventPendingFollowUpUpdated,
            ControlPlaneConversationStreamEventKind.AgentJobProgress => EventAgentJobProgress,
            ControlPlaneConversationStreamEventKind.DeprecationNotice => EventDeprecationNotice,
            ControlPlaneConversationStreamEventKind.ConfigWarning => EventConfigWarning,
            ControlPlaneConversationStreamEventKind.ThreadStatusChanged => EventThreadStatusChanged,
            ControlPlaneConversationStreamEventKind.ThreadNameUpdated => EventThreadNameUpdated,
            ControlPlaneConversationStreamEventKind.ThreadTokenUsageUpdated => EventThreadTokenUsageUpdated,
            ControlPlaneConversationStreamEventKind.ThreadCompacted => EventThreadCompacted,
            ControlPlaneConversationStreamEventKind.SkillsChanged => EventSkillsChanged,
            ControlPlaneConversationStreamEventKind.CommandExecOutputDelta => EventCommandExecOutputDelta,
            ControlPlaneConversationStreamEventKind.AppListUpdated => EventAppListUpdated,
            ControlPlaneConversationStreamEventKind.ThreadRealtimeItemAdded => EventThreadRealtimeItemAdded,
            ControlPlaneConversationStreamEventKind.ThreadRealtimeOutputAudioDelta => EventThreadRealtimeOutputAudioDelta,
            ControlPlaneConversationStreamEventKind.ThreadRealtimeError => EventThreadRealtimeError,
            ControlPlaneConversationStreamEventKind.ThreadRealtimeClosed => EventThreadRealtimeClosed,
            ControlPlaneConversationStreamEventKind.HookStarted => EventHookStarted,
            ControlPlaneConversationStreamEventKind.HookCompleted => EventHookCompleted,
            ControlPlaneConversationStreamEventKind.ModelRerouted => EventModelRerouted,
            ControlPlaneConversationStreamEventKind.Info when streamEvent.PayloadKind == ControlPlaneConversationStreamPayloadKind.WindowsSandboxSetup
                => EventWindowsSandboxSetup,
            ControlPlaneConversationStreamEventKind.Info when streamEvent.PayloadKind == ControlPlaneConversationStreamPayloadKind.McpServerOauthLogin
                => EventMcpServerOauthLogin,
            ControlPlaneConversationStreamEventKind.Info when streamEvent.PayloadKind == ControlPlaneConversationStreamPayloadKind.RealtimeSession
                => EventRealtimeSession,
            ControlPlaneConversationStreamEventKind.Info when streamEvent.PayloadKind == ControlPlaneConversationStreamPayloadKind.FuzzyFileSearchSession
                => EventFuzzyFileSearchSession,
            ControlPlaneConversationStreamEventKind.Error => EventError,
            _ => EventInfo,
        };

        await WriteEventAsync(
                eventType,
                threadId: streamEvent.ThreadId?.Value,
                turnId: streamEvent.TurnId?.Value,
                callId: streamEvent.CallId?.Value,
                toolName: streamEvent.ToolName,
                text: streamEvent.Text,
                message: streamEvent.Message,
                data: new
                {
                    kind = streamEvent.Kind.ToString(),
                    itemId = streamEvent.ItemId,
                    status = streamEvent.Status,
                    phase = streamEvent.Phase,
                    willRetry = streamEvent.WillRetry,
                    requiresApproval = streamEvent.RequiresApproval,
                    approvalKind = streamEvent.ApprovalKind,
                    availableDecisions = streamEvent.AvailableDecisions,
                    availableDecisionOptions = BuildSidecarApprovalDecisionOptions(
                        SidecarApprovalRequestPayload.MapDecisionOptions(streamEvent.AvailableDecisionOptions)),
                    sourceMethod = streamEvent.SourceMethod,
                    taskType = streamEvent.TaskType,
                    operationName = streamEvent.OperationName,
                    source = streamEvent.Source,
                    serverName = streamEvent.ServerName,
                    plan = ResolveStructuredPayloadObject(streamEvent, ControlPlaneConversationStreamPayloadKind.Plan),
                    toolCall = ResolveStructuredPayloadObject(streamEvent, ControlPlaneConversationStreamPayloadKind.ToolCall),
                    approvalRequest = approvalRequest,
                    permissionRequest = permissionRequest,
                    userInputRequest = userInputRequest,
                    serverRequestResolved = serverRequestResolved,
                    task = ResolveStructuredPayloadObject(streamEvent, ControlPlaneConversationStreamPayloadKind.Task),
                    operation = ResolveStructuredPayloadObject(streamEvent, ControlPlaneConversationStreamPayloadKind.Operation),
                    hookRun = ResolveStructuredPayloadObject(streamEvent, ControlPlaneConversationStreamPayloadKind.HookRun),
                    reasoning = ResolveStructuredPayloadObject(streamEvent, ControlPlaneConversationStreamPayloadKind.Reasoning),
                    modelRerouted = ResolveStructuredPayloadObject(streamEvent, ControlPlaneConversationStreamPayloadKind.ModelRerouted),
                    mcpServerStatus = ResolveStructuredPayloadObject(streamEvent, ControlPlaneConversationStreamPayloadKind.McpServerStatus),
                    item = ResolveSidecarItemPayload(streamEvent, committedUserMessage),
                    committedUserMessage = committedUserMessage,
                    pendingFollowUp = pendingFollowUp,
                    pendingInputState = pendingInputState,
                    turnError = streamEvent.TurnError,
                    agentJobProgress = ResolveStructuredPayloadObject(streamEvent, ControlPlaneConversationStreamPayloadKind.AgentJobProgress),
                    deprecationNotice = ResolveStructuredPayloadObject(streamEvent, ControlPlaneConversationStreamPayloadKind.DeprecationNotice),
                    configWarning = ResolveStructuredPayloadObject(streamEvent, ControlPlaneConversationStreamPayloadKind.ConfigWarning),
                    threadStatusChanged = ResolveStructuredPayloadObject(streamEvent, ControlPlaneConversationStreamPayloadKind.ThreadStatusChanged),
                    threadNameUpdated = ResolveStructuredPayloadObject(streamEvent, ControlPlaneConversationStreamPayloadKind.ThreadNameUpdated),
                    threadTokenUsage = ResolveStructuredPayloadObject(streamEvent, ControlPlaneConversationStreamPayloadKind.ThreadTokenUsage),
                    commandExecOutputDelta = ResolveStructuredPayloadObject(streamEvent, ControlPlaneConversationStreamPayloadKind.CommandExecOutputDelta),
                    appListUpdated = ResolveStructuredPayloadObject(streamEvent, ControlPlaneConversationStreamPayloadKind.AppListUpdated),
                    windowsSandboxSetup = ResolveStructuredPayloadObject(streamEvent, ControlPlaneConversationStreamPayloadKind.WindowsSandboxSetup),
                    mcpServerOauthLogin = ResolveStructuredPayloadObject(streamEvent, ControlPlaneConversationStreamPayloadKind.McpServerOauthLogin),
                    realtimeSession = ResolveStructuredPayloadObject(streamEvent, ControlPlaneConversationStreamPayloadKind.RealtimeSession),
                    fuzzyFileSearchSession = ResolveStructuredPayloadObject(streamEvent, ControlPlaneConversationStreamPayloadKind.FuzzyFileSearchSession),
                    threadRealtimeItemAdded = ResolveStructuredPayloadObject(streamEvent, ControlPlaneConversationStreamPayloadKind.ThreadRealtimeItemAdded),
                    threadRealtimeOutputAudioDelta = ResolveStructuredPayloadObject(streamEvent, ControlPlaneConversationStreamPayloadKind.ThreadRealtimeOutputAudioDelta),
                    threadRealtimeError = ResolveStructuredPayloadObject(streamEvent, ControlPlaneConversationStreamPayloadKind.ThreadRealtimeError),
                    threadRealtimeClosed = ResolveStructuredPayloadObject(streamEvent, ControlPlaneConversationStreamPayloadKind.ThreadRealtimeClosed),
                    diagnostics = BuildStreamEventDiagnostics(streamEvent),
                })
            .ConfigureAwait(false);

        if (streamEvent.Kind == ControlPlaneConversationStreamEventKind.ApprovalRequested)
        {
            await WriteRuntimeStateAsync(
                    "waiting_approval",
                    streamEvent.Message ?? "等待人工确认。",
                    new
                    {
                        threadId = streamEvent.ThreadId?.Value,
                        turnId = streamEvent.TurnId?.Value,
                        callId = streamEvent.CallId?.Value,
                        toolName = streamEvent.ToolName,
                    })
                .ConfigureAwait(false);
        }
        else if (streamEvent.Kind == ControlPlaneConversationStreamEventKind.PermissionRequested)
        {
            await WriteRuntimeStateAsync(
                    "waiting_permission",
                    streamEvent.Message ?? "等待权限确认。",
                    new
                    {
                        threadId = streamEvent.ThreadId?.Value,
                        turnId = streamEvent.TurnId?.Value,
                        callId = streamEvent.CallId?.Value,
                        toolName = streamEvent.ToolName,
                    })
                .ConfigureAwait(false);
        }
        else if (streamEvent.Kind == ControlPlaneConversationStreamEventKind.UserInputRequested)
        {
            await WriteRuntimeStateAsync(
                    "waiting_user_input",
                    streamEvent.Message ?? "等待人工补录。",
                    new
                    {
                        threadId = streamEvent.ThreadId?.Value,
                        turnId = streamEvent.TurnId?.Value,
                        callId = streamEvent.CallId?.Value,
                        toolName = streamEvent.ToolName,
                    })
                .ConfigureAwait(false);
        }
        else if (streamEvent.Kind == ControlPlaneConversationStreamEventKind.TurnCompleted)
        {
            await WriteRuntimeStateAsync(
                    "idle",
                    ResolveTurnCompletedStateMessage(streamEvent),
                    new
                    {
                        threadId = streamEvent.ThreadId?.Value,
                        turnId = streamEvent.TurnId?.Value,
                        status = streamEvent.Status,
                        pendingInputState = pendingInputState,
                        turnError = streamEvent.TurnError,
                    })
                .ConfigureAwait(false);
        }
        else if (streamEvent.Kind == ControlPlaneConversationStreamEventKind.TurnSteered)
        {
            await WriteRuntimeStateAsync(
                    "busy",
                    "已接收引导输入，正在继续当前回合。",
                    new
                    {
                        threadId = streamEvent.ThreadId?.Value,
                        turnId = streamEvent.TurnId?.Value,
                        status = streamEvent.Status,
                        source = streamEvent.Source,
                    })
                .ConfigureAwait(false);
        }
    }

    private async Task DisposeRuntimeAsync()
    {
        await StopRuntimeStreamRelayAsync().ConfigureAwait(false);

        if (runtime is null)
        {
            hostGateway = null;
            return;
        }

        await runtime.DisposeAsync().ConfigureAwait(false);
        runtime = null;
        hostGateway = null;
        runtimeWorkingDirectory = null;
    }

    private static ITianShuControlPlane GetControlPlane(IExecutionRuntime executionRuntime)
    {
        ArgumentNullException.ThrowIfNull(executionRuntime);
        return ControlPlaneCache.GetValue(executionRuntime, TianShuControlPlaneClientFactory.Create);
    }

    private ITianShuHostGateway GetHostGateway()
    {
        if (runtime is null)
        {
            throw new InvalidOperationException("运行时尚未初始化。");
        }

        return hostGateway ??= CreateHostGateway(runtime);
    }

    private ITianShuHostGateway CreateHostGateway(IExecutionRuntime executionRuntime)
    {
        var gateway = new TianShuHostGateway(GetControlPlane(executionRuntime));
        EnsureRuntimeStreamRelayStarted(gateway);
        return gateway;
    }

    private static Task<ControlPlaneSessionSnapshot> GetSessionSnapshotAsync(
        IExecutionRuntime executionRuntime,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executionRuntime);
        return GetControlPlane(executionRuntime).Sessions.GetSnapshotAsync(cancellationToken);
    }

    private void EnsureRuntimeStreamRelayStarted(ITianShuHostGateway gateway)
    {
        if (runtimeStreamRelayTask is { IsCompleted: false })
        {
            return;
        }

        runtimeStreamRelayCts?.Dispose();
        runtimeStreamRelayCts = new CancellationTokenSource();
        runtimeStreamRelayTask = Task.Run(() => RelayRuntimeStreamLoopAsync(gateway, runtimeStreamRelayCts.Token));
    }

    private async Task RelayRuntimeStreamLoopAsync(ITianShuHostGateway gateway, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var streamEvent in gateway.SubscribeConversationStreamAsync(
                               new HostConversationStreamSubscription(),
                               cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                await RelayRuntimeEventSafelyAsync(streamEvent).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            LogError("运行时事件订阅循环失败。", ex);
        }
    }

    private async Task StopRuntimeStreamRelayAsync()
    {
        var relayCts = runtimeStreamRelayCts;
        var relayTask = runtimeStreamRelayTask;
        runtimeStreamRelayCts = null;
        runtimeStreamRelayTask = null;

        if (relayCts is null)
        {
            return;
        }

        relayCts.Cancel();

        if (relayTask is not null)
        {
            try
            {
                await relayTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        relayCts.Dispose();
    }

    private Task WriteResponseAsync(string requestId, bool success, string message, object? data)
        => WriteEnvelopeAsync(new SidecarResponseEnvelope
        {
            RequestId = requestId,
            Success = success,
            Message = message,
            Data = data,
        });

    private Task WriteRuntimeStateAsync(string state, string message, object? data)
        => WriteEventAsync(EventRuntimeState, state: state, message: message, data: data);

    private static string ResolveTurnCompletedStateMessage(ControlPlaneConversationStreamEvent streamEvent)
    {
        var errorMessage = streamEvent.TurnError?.Message;
        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            return errorMessage!;
        }

        if (string.Equals(streamEvent.Status, "interrupted", StringComparison.OrdinalIgnoreCase))
        {
            return "当前回合已中断。";
        }

        if (string.Equals(streamEvent.Status, "failed", StringComparison.OrdinalIgnoreCase))
        {
            return "当前回合执行失败。";
        }

        return streamEvent.Message ?? "当前回合已完成。";
    }

    [DiagnosticsJsonAccessAllowed]
    private static object? BuildStreamEventDiagnostics(ControlPlaneConversationStreamEvent streamEvent)
    {
        if (streamEvent.Diagnostics is not { } diagnostics
            || (string.IsNullOrWhiteSpace(diagnostics.DataJson)
                && string.IsNullOrWhiteSpace(diagnostics.MetadataJson)
                && string.IsNullOrWhiteSpace(diagnostics.RawJson)))
        {
            return null;
        }

        return new
        {
            // Sidecar 仅向上暴露诊断快照，业务逻辑必须消费 typed payload。
            dataJson = diagnostics.DataJson,
            metadataJson = diagnostics.MetadataJson,
            rawJson = diagnostics.RawJson,
        };
    }

    private static TPayload? ReadTypedPayload<TPayload>(
        ControlPlaneConversationStreamEvent streamEvent,
        ControlPlaneConversationStreamPayloadKind payloadKind)
        where TPayload : class
    {
        if (streamEvent.PayloadKind != payloadKind || streamEvent.Payload is null)
        {
            return null;
        }

        var payloadElement = JsonSerializer.SerializeToElement(streamEvent.Payload.ToPlainObject(), PayloadJsonOptions);
        return JsonSerializer.Deserialize<TPayload>(payloadElement, PayloadJsonOptions);
    }

    private static object? ResolveStructuredPayloadObject(
        ControlPlaneConversationStreamEvent streamEvent,
        ControlPlaneConversationStreamPayloadKind payloadKind)
        => streamEvent.PayloadKind == payloadKind
            ? streamEvent.Payload?.ToPlainObject()
            : null;

    private static SidecarPendingInputStatePayload? ReadPendingInputStatePayload(ControlPlaneConversationStreamEvent streamEvent)
    {
        if (streamEvent.PayloadKind != ControlPlaneConversationStreamPayloadKind.PendingInputState
            || streamEvent.Payload is null)
        {
            return null;
        }

        var payloadElement = JsonSerializer.SerializeToElement(streamEvent.Payload.ToPlainObject(), PayloadJsonOptions);
        return new SidecarPendingInputStatePayload
        {
            Entries = ReadPendingInputStateEntries(payloadElement, "entries"),
            QueuedUserMessages = ReadPendingInputStateEntries(payloadElement, "queuedUserMessages"),
            PendingSteers = ReadPendingInputStateEntries(payloadElement, "pendingSteers"),
            InterruptRequestPending = ReadBoolean(payloadElement, "interruptRequestPending"),
            SubmitPendingSteersAfterInterrupt = ReadBoolean(payloadElement, "submitPendingSteersAfterInterrupt"),
        };
    }

    private static SidecarPendingInputStateEntryPayload[] ReadPendingInputStateEntries(JsonElement payloadElement, string propertyName)
    {
        if (!payloadElement.TryGetProperty(propertyName, out var entriesElement)
            || entriesElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<SidecarPendingInputStateEntryPayload>();
        }

        return entriesElement
            .EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.Object)
            .Select(ReadPendingInputStateEntryPayload)
            .ToArray();
    }

    private static SidecarPendingInputStateEntryPayload ReadPendingInputStateEntryPayload(JsonElement entryElement)
        => new()
        {
            CorrelationId = Normalize(ReadString(entryElement, "correlationId")) ?? string.Empty,
            RequestedMode = Normalize(ReadString(entryElement, "requestedMode")) ?? string.Empty,
            EffectiveMode = Normalize(ReadString(entryElement, "effectiveMode")) ?? string.Empty,
            LifecycleState = Normalize(ReadString(entryElement, "lifecycleState")) ?? string.Empty,
            ExpectedTurnId = Normalize(ReadString(entryElement, "expectedTurnId")),
            TurnId = Normalize(ReadString(entryElement, "turnId")),
            PendingBucket = Normalize(ReadString(entryElement, "pendingBucket")) ?? "QueuedUserMessage",
            CompareKey = ReadPendingFollowUpCompareKeyPayload(entryElement, "compareKey"),
            Inputs = ReadSidecarUserInputPayloads(entryElement, "inputs"),
        };

    private static SidecarPendingFollowUpCompareKeyPayload? ReadPendingFollowUpCompareKeyPayload(
        JsonElement payloadElement,
        string propertyName)
    {
        if (!payloadElement.TryGetProperty(propertyName, out var compareKeyElement)
            || compareKeyElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new SidecarPendingFollowUpCompareKeyPayload
        {
            Message = Normalize(ReadString(compareKeyElement, "message")),
            ImageCount = ReadInt32(compareKeyElement, "imageCount") ?? 0,
        };
    }

    private static SidecarUserInputPayload[] ReadSidecarUserInputPayloads(JsonElement payloadElement, string propertyName)
    {
        if (!payloadElement.TryGetProperty(propertyName, out var inputsElement)
            || inputsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<SidecarUserInputPayload>();
        }

        return inputsElement
            .EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.Object)
            .Select(ReadSidecarUserInputPayload)
            .ToArray();
    }

    private static SidecarUserInputPayload ReadSidecarUserInputPayload(JsonElement inputElement)
        => new()
        {
            Type = Normalize(ReadString(inputElement, "type")),
            Text = Normalize(ReadString(inputElement, "text")),
            Url = Normalize(ReadString(inputElement, "url")),
            Path = Normalize(ReadString(inputElement, "path")),
            Name = Normalize(ReadString(inputElement, "name")),
            TextElements = ReadSidecarTextElements(inputElement, "textElements"),
        };

    private static List<SidecarTextElementPayload>? ReadSidecarTextElements(JsonElement payloadElement, string propertyName)
    {
        if (!payloadElement.TryGetProperty(propertyName, out var textElementsElement)
            || textElementsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var textElements = textElementsElement
            .EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.Object)
            .Select(ReadSidecarTextElementPayload)
            .ToList();
        return textElements.Count == 0 ? null : textElements;
    }

    private static SidecarTextElementPayload ReadSidecarTextElementPayload(JsonElement textElement)
        => new()
        {
            ByteRange = ReadSidecarByteRangePayload(textElement, "byteRange"),
            Placeholder = Normalize(ReadString(textElement, "placeholder")),
        };

    private static SidecarByteRangePayload? ReadSidecarByteRangePayload(JsonElement payloadElement, string propertyName)
    {
        if (!payloadElement.TryGetProperty(propertyName, out var byteRangeElement)
            || byteRangeElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new SidecarByteRangePayload
        {
            Start = ReadInt32(byteRangeElement, "start") ?? 0,
            End = ReadInt32(byteRangeElement, "end") ?? 0,
        };
    }

    private static object? ResolveSidecarItemPayload(
        ControlPlaneConversationStreamEvent streamEvent,
        object? committedUserMessage)
    {
        if (streamEvent.PayloadKind == ControlPlaneConversationStreamPayloadKind.Item
            && streamEvent.Payload is not null)
        {
            return streamEvent.Payload.ToPlainObject();
        }

        if (streamEvent.Kind != ControlPlaneConversationStreamEventKind.UserMessageCommitted
            || string.IsNullOrWhiteSpace(streamEvent.ItemId))
        {
            return null;
        }

        return new
        {
            itemId = streamEvent.ItemId,
            itemType = streamEvent.Status ?? "user_message",
            status = streamEvent.Phase ?? "completed",
            phase = streamEvent.Phase ?? "completed",
            text = streamEvent.Text,
            imageCount = ReadPlainObjectInt32(committedUserMessage, "imageCount") ?? 0,
            inputs = ReadPlainObjectProperty(committedUserMessage, "inputs"),
        };
    }

    private static object? ReadPlainObjectProperty(object? payload, string propertyName)
        => payload switch
        {
            IReadOnlyDictionary<string, object?> readOnlyDictionary when readOnlyDictionary.TryGetValue(propertyName, out var value) => value,
            IDictionary<string, object?> dictionary when dictionary.TryGetValue(propertyName, out var value) => value,
            _ => null,
        };

    private static int? ReadPlainObjectInt32(object? payload, string propertyName)
    {
        var value = ReadPlainObjectProperty(payload, propertyName);
        return value switch
        {
            int intValue => intValue,
            long longValue when longValue is >= int.MinValue and <= int.MaxValue => (int)longValue,
            JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.Number && jsonElement.TryGetInt32(out var jsonInt32) => jsonInt32,
            _ => null,
        };
    }

    private Task WriteEventAsync(
        string eventType,
        string? state = null,
        string? threadId = null,
        string? turnId = null,
        string? callId = null,
        string? toolName = null,
        string? text = null,
        string? message = null,
        object? data = null)
        => WriteEnvelopeAsync(new SidecarEventEnvelope
        {
            EventType = eventType,
            State = state,
            ThreadId = threadId,
            TurnId = turnId,
            CallId = callId,
            ToolName = toolName,
            Text = text,
            Message = message,
            Data = data,
        });

    private async Task WriteEnvelopeAsync<TEnvelope>(TEnvelope envelope)
    {
        var json = JsonSerializer.Serialize(envelope, jsonOptions);
        await writeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await output.WriteLineAsync(json).ConfigureAwait(false);
            await output.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            writeGate.Release();
        }
    }

    private void LogError(string message, Exception? exception = null)
    {
        if (exception is null)
        {
            error.WriteLine($"[TianShu.VSSDK.Sidecar] {message}");
            return;
        }

        error.WriteLine($"[TianShu.VSSDK.Sidecar] {message} {exception}");
    }

    private static string? ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TianShu.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string? ResolveAppHostProjectPath(string? repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            return null;
        }

        return RuntimeHostLaunchLocator.ResolvePreferredHostProjectPath(repositoryRoot);
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

    private static ControlPlaneReadThreadQuery BuildDirectThreadReadRequest(ReadThreadPayload payload, string threadId)
        => new()
        {
            ThreadId = new ThreadId(threadId),
            IncludeTurns = payload.IncludeTurns,
        };

    private static ControlPlaneLoadedThreadListQuery BuildDirectThreadLoadedListRequest(LoadedThreadListPayload payload)
        => new()
        {
            Limit = payload.Limit,
            Cursor = Normalize(payload.Cursor),
        };

    private static ControlPlaneCompactThreadCommand BuildDirectThreadCompactRequest(ThreadCompactPayload payload, string threadId)
        => new()
        {
            ThreadId = new ThreadId(threadId),
            KeepRecentTurns = payload.KeepRecentTurns ?? 0,
        };

    private static ControlPlaneCleanBackgroundTerminalsCommand BuildDirectCleanBackgroundTerminalsRequest(string threadId)
        => new()
        {
            ThreadId = new ThreadId(threadId),
        };

    private static ControlPlaneUnsubscribeThreadCommand BuildDirectThreadUnsubscribeRequest(string threadId)
        => new()
        {
            ThreadId = new ThreadId(threadId),
        };

    private static ControlPlaneIncrementThreadElicitationCommand BuildDirectIncrementThreadElicitationRequest(string threadId)
        => new()
        {
            ThreadId = new ThreadId(threadId),
        };

    private static ControlPlaneDecrementThreadElicitationCommand BuildDirectDecrementThreadElicitationRequest(string threadId)
        => new()
        {
            ThreadId = new ThreadId(threadId),
        };

    private static HostResumeThread BuildHostResumeThreadCommand(ResumeThreadPayload payload, string threadId)
    {
        var request = BuildThreadResumeCommand(payload, threadId);
        return new HostResumeThread
        {
            ThreadId = request.ThreadId,
            History = request.History,
            Path = request.Path,
            Model = request.Model,
            ModelProvider = request.ModelProvider,
            ServiceTier = request.ServiceTier,
            WorkingDirectory = request.WorkingDirectory,
            ApprovalPolicy = request.ApprovalPolicy,
            SandboxMode = request.SandboxMode,
            Configuration = request.Configuration,
            BaseInstructions = request.BaseInstructions,
            DeveloperInstructions = request.DeveloperInstructions,
            Personality = request.Personality,
            PersistExtendedHistory = request.PersistExtendedHistory,
        };
    }

    private static HostForkThread BuildHostForkThreadCommand(ForkThreadPayload payload, string threadId)
    {
        var request = BuildThreadForkCommand(payload, threadId);
        return new HostForkThread
        {
            ThreadId = request.ThreadId,
            Path = request.Path,
            Model = request.Model,
            ModelProvider = request.ModelProvider,
            ServiceTier = request.ServiceTier,
            WorkingDirectory = request.WorkingDirectory,
            ApprovalPolicy = request.ApprovalPolicy,
            SandboxMode = request.SandboxMode,
            Configuration = request.Configuration,
            BaseInstructions = request.BaseInstructions,
            DeveloperInstructions = request.DeveloperInstructions,
            Ephemeral = request.Ephemeral,
            PersistExtendedHistory = request.PersistExtendedHistory,
        };
    }

    private static HostStartThread BuildHostStartThreadCommand(StartNewThreadPayload payload)
    {
        var request = BuildThreadStartCommand(payload);
        return new HostStartThread
        {
            Model = request.Model,
            ModelProvider = request.ModelProvider,
            ServiceTier = request.ServiceTier,
            WorkingDirectory = request.WorkingDirectory,
            ApprovalPolicy = request.ApprovalPolicy,
            SandboxMode = request.SandboxMode,
            Configuration = request.Configuration,
            ServiceName = request.ServiceName,
            BaseInstructions = request.BaseInstructions,
            DeveloperInstructions = request.DeveloperInstructions,
            Personality = request.Personality,
            Ephemeral = request.Ephemeral,
            DynamicTools = request.DynamicTools,
            MockExperimentalField = request.MockExperimentalField,
            PersistExtendedHistory = request.PersistExtendedHistory,
            ExperimentalRawEvents = request.ExperimentalRawEvents,
        };
    }

    private static ControlPlaneRollbackThreadCommand BuildDirectThreadRollbackRequest(RollbackThreadPayload payload, string threadId)
        => new()
        {
            ThreadId = new ThreadId(threadId),
            NumTurns = payload.NumTurns,
        };

    private static ControlPlaneUpdateThreadMetadataCommand BuildDirectThreadMetadataUpdateRequest(
        ThreadMetadataUpdatePayload payload,
        string threadId)
    {
        var gitInfo = payload.GitInfo;
        var hasGitSha = payload.HasGitSha ?? gitInfo is not null && gitInfo.Sha is not null;
        var hasGitBranch = payload.HasGitBranch ?? gitInfo is not null && gitInfo.Branch is not null;
        var hasGitOriginUrl = payload.HasGitOriginUrl ?? gitInfo is not null && gitInfo.OriginUrl is not null;
        return new ControlPlaneUpdateThreadMetadataCommand
        {
            ThreadId = new ThreadId(threadId),
            HasGitSha = hasGitSha,
            GitSha = hasGitSha ? Normalize(payload.GitSha) ?? Normalize(gitInfo?.Sha) : null,
            HasGitBranch = hasGitBranch,
            GitBranch = hasGitBranch ? Normalize(payload.GitBranch) ?? Normalize(gitInfo?.Branch) : null,
            HasGitOriginUrl = hasGitOriginUrl,
            GitOriginUrl = hasGitOriginUrl ? Normalize(payload.GitOriginUrl) ?? Normalize(gitInfo?.OriginUrl) : null,
        };
    }

    private static ControlPlaneFuzzyFileSearchQuery BuildDirectFuzzyFileSearchSearchRequest(FuzzyFileSearchPayload payload)
    {
        var query = RequireDirectPayloadValue(payload.Query, "query", "模糊文件搜索");
        return new ControlPlaneFuzzyFileSearchQuery
        {
            Query = query,
            WorkingDirectory = Normalize(payload.Cwd),
            Limit = payload.Limit,
            Roots = (payload.Roots ?? Array.Empty<string>())
                .Select(Normalize)
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Select(static item => item!)
                .ToArray(),
        };
    }

    private static ControlPlaneStartFuzzyFileSearchSessionCommand BuildDirectFuzzyFileSearchSessionStartRequest(FuzzyFileSearchSessionStartPayload payload)
    {
        var sessionId = RequireDirectPayloadValue(payload.SessionId, "sessionId", "模糊文件搜索会话启动");
        var roots = (payload.Roots ?? Array.Empty<string>())
            .Select(Normalize)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item!)
            .ToList();
        var cwd = Normalize(payload.Cwd);
        if (roots.Count == 0 && !string.IsNullOrWhiteSpace(cwd))
        {
            roots.Add(cwd!);
        }

        return new ControlPlaneStartFuzzyFileSearchSessionCommand
        {
            SessionId = sessionId,
            Roots = roots,
        };
    }

    private static ControlPlaneUpdateFuzzyFileSearchSessionCommand BuildDirectFuzzyFileSearchSessionUpdateRequest(FuzzyFileSearchSessionUpdatePayload payload)
        => new()
        {
            SessionId = RequireDirectPayloadValue(payload.SessionId, "sessionId", "模糊文件搜索会话更新"),
            Query = RequireDirectPayloadValue(payload.Query, "query", "模糊文件搜索会话更新"),
        };

    private static ControlPlaneStopFuzzyFileSearchSessionCommand BuildDirectFuzzyFileSearchSessionStopRequest(FuzzyFileSearchSessionStopPayload payload)
        => new()
        {
            SessionId = RequireDirectPayloadValue(payload.SessionId, "sessionId", "模糊文件搜索会话停止"),
        };

    private static ControlPlaneRealtimeStartCommand BuildDirectRealtimeStartRequest(RealtimeStartPayload payload, string? fallbackThreadId)
    {
        var threadId = Normalize(payload.ThreadId) ?? Normalize(fallbackThreadId);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            throw new InvalidOperationException("realtime 启动缺少 threadId。");
        }

        return new ControlPlaneRealtimeStartCommand
        {
            ThreadId = new ThreadId(threadId),
            SessionId = Normalize(payload.SessionId),
            Prompt = Normalize(payload.Prompt),
        };
    }

    private static ControlPlaneRealtimeAppendTextCommand BuildDirectRealtimeAppendTextRequest(RealtimeAppendTextPayload payload, string? fallbackThreadId)
    {
        var threadId = Normalize(payload.ThreadId) ?? Normalize(fallbackThreadId);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            throw new InvalidOperationException("realtime 文本追加缺少 threadId。");
        }

        var text = payload.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("realtime 文本追加缺少 text。");
        }

        return new ControlPlaneRealtimeAppendTextCommand
        {
            ThreadId = new ThreadId(threadId),
            SessionId = Normalize(payload.SessionId),
            Text = text,
        };
    }

    private static ControlPlaneRealtimeAppendAudioCommand BuildDirectRealtimeAppendAudioRequest(RealtimeAppendAudioPayload payload, string? fallbackThreadId)
    {
        var threadId = Normalize(payload.ThreadId) ?? Normalize(fallbackThreadId);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            throw new InvalidOperationException("realtime 音频追加缺少 threadId。");
        }

        if (payload.Audio is null)
        {
            throw new InvalidOperationException("realtime 音频追加缺少 audio。");
        }

        return new ControlPlaneRealtimeAppendAudioCommand
        {
            ThreadId = new ThreadId(threadId),
            SessionId = Normalize(payload.SessionId),
            Audio = BuildDirectRealtimeAudioInput(payload.Audio),
        };
    }

    private static ControlPlaneRealtimeHandoffOutputCommand BuildDirectRealtimeHandoffOutputRequest(RealtimeHandoffOutputPayload payload, string? fallbackThreadId)
    {
        var threadId = Normalize(payload.ThreadId) ?? Normalize(fallbackThreadId);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            throw new InvalidOperationException("realtime 输出移交缺少 threadId。");
        }

        return new ControlPlaneRealtimeHandoffOutputCommand
        {
            ThreadId = new ThreadId(threadId),
            SessionId = Normalize(payload.SessionId),
            HandoffId = RequireDirectPayloadValue(payload.HandoffId, "handoffId", "realtime 输出移交"),
            Output = payload.Output ?? string.Empty,
        };
    }

    private static ControlPlaneRealtimeStopCommand BuildDirectRealtimeStopRequest(RealtimeStopPayload payload, string? fallbackThreadId)
    {
        var threadId = Normalize(payload.ThreadId) ?? Normalize(fallbackThreadId);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            throw new InvalidOperationException("realtime 停止缺少 threadId。");
        }

        return new ControlPlaneRealtimeStopCommand
        {
            ThreadId = new ThreadId(threadId),
            SessionId = Normalize(payload.SessionId),
        };
    }

    private static ControlPlaneRealtimeAudioInput BuildDirectRealtimeAudioInput(RealtimeAudioPayload payload)
        => new()
        {
            Data = payload.Data ?? string.Empty,
            SampleRate = payload.SampleRate,
            NumChannels = payload.NumChannels,
            SamplesPerChannel = payload.SamplesPerChannel,
        };

    private static ControlPlaneFeedbackUploadCommand BuildDirectFeedbackUploadRequest(FeedbackUploadPayload payload)
        => new()
        {
            Classification = RequireDirectPayloadValue(payload.Classification, "classification", "feedback 上传"),
            IncludeLogs = payload.IncludeLogs,
            ThreadId = Normalize(payload.ThreadId),
            Reason = Normalize(payload.Reason),
            ExtraLogFiles = (payload.ExtraLogFiles ?? Array.Empty<string>())
                .Select(Normalize)
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Select(static item => item!)
                .ToArray(),
        };

    private static ControlPlaneConfigReadQuery BuildDirectConfigReadRequest(ConfigReadPayload payload)
        => new()
        {
            WorkingDirectory = Normalize(payload.WorkingDirectory),
            IncludeLayers = payload.IncludeLayers,
        };

    private static ControlPlaneModelCatalogQuery BuildDirectModelListRequest(ModelListPayload payload)
        => new()
        {
            Limit = Math.Clamp(payload.Limit, 1, 200),
            IncludeHidden = payload.IncludeHidden,
        };

    private static GetCapabilityCatalog BuildDirectCapabilityCatalogRequest(CapabilityCatalogPayload payload)
        => new(
            workspacePath: Normalize(payload.WorkspacePath),
            includeHiddenModels: payload.IncludeHiddenModels,
            modelLimit: Math.Clamp(payload.ModelLimit, 1, 200));

    private static ResolveEngineBinding BuildDirectResolveEngineBindingRequest(ResolveEngineBindingPayload payload)
        => new(
            WorkspacePath: Normalize(payload.WorkspacePath),
            PreferredProviderKey: Normalize(payload.ProviderKey),
            PreferredModelKey: Normalize(payload.ModelKey),
            ReasoningEffort: Normalize(payload.ReasoningEffort),
            ReasoningSummary: Normalize(payload.ReasoningSummary),
            Verbosity: Normalize(payload.Verbosity),
            PreferWebsocketTransport: payload.PreferWebsocketTransport);

    private static ControlPlaneAgentListQuery BuildDirectAgentListRequest(AgentListPayload payload)
        => new()
        {
            Limit = payload.Limit,
            Cursor = Normalize(payload.Cursor),
            IncludePrimaryThreads = payload.IncludePrimaryThreads,
        };

    private static ControlPlaneRegisterAgentThreadCommand BuildDirectRegisterAgentThreadRequest(RegisterAgentThreadPayload payload)
        => new()
        {
            ThreadId = new ThreadId(RequireDirectPayloadValue(payload.ThreadId, nameof(payload.ThreadId), "register agent thread")),
            AgentNickname = Normalize(payload.AgentNickname),
            AgentRole = Normalize(payload.AgentRole),
        };

    private static ControlPlaneCreateJobCommand BuildDirectCreateAgentJobRequest(CreateAgentJobPayload payload)
        => new()
        {
            JobId = string.IsNullOrWhiteSpace(Normalize(payload.JobId)) ? null : new JobId(Normalize(payload.JobId)!),
            Name = Normalize(payload.Name),
            Instruction = RequireDirectPayloadValue(payload.Instruction, nameof(payload.Instruction), "create agent job"),
            InputHeaders = ToContractsStructuredValue(payload.InputHeaders),
            InputCsvPath = Normalize(payload.InputCsvPath),
            OutputCsvPath = Normalize(payload.OutputCsvPath),
            AutoExport = payload.AutoExport,
            OutputSchema = ToContractsStructuredValue(payload.OutputSchema),
            Items = (payload.Items ?? [])
                .Select(static item => item)
                .ToArray(),
        };

    private static ControlPlaneDispatchJobCommand BuildDirectDispatchAgentJobRequest(DispatchAgentJobPayload payload)
    {
        var jobId = RequireDirectPayloadValue(payload.JobId, nameof(payload.JobId), "dispatch agent job");
        var threadIds = (payload.ThreadIds ?? [])
            .Select(Normalize)
            .Where(static threadId => !string.IsNullOrWhiteSpace(threadId))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static threadId => new ThreadId(threadId))
            .ToArray();
        if (threadIds.Length == 0)
        {
            throw new InvalidOperationException("dispatch agent job 缺少 threadIds。");
        }

        return new ControlPlaneDispatchJobCommand
        {
            JobId = new JobId(jobId),
            ThreadIds = threadIds,
        };
    }

    private static ControlPlaneReportJobItemCommand BuildDirectReportAgentJobItemRequest(ReportAgentJobItemPayload payload)
        => new()
        {
            JobId = new JobId(RequireDirectPayloadValue(payload.JobId, nameof(payload.JobId), "report agent job item")),
            ItemId = new JobItemId(RequireDirectPayloadValue(payload.ItemId, nameof(payload.ItemId), "report agent job item")),
            Status = RequireDirectPayloadValue(payload.Status, nameof(payload.Status), "report agent job item"),
            Result = ToContractsStructuredValue(payload.Result),
            LastError = Normalize(payload.LastError),
        };

    private static ControlPlaneReadJobQuery BuildDirectReadAgentJobRequest(ReadAgentJobPayload payload)
        => new()
        {
            JobId = new JobId(RequireDirectPayloadValue(payload.JobId, nameof(payload.JobId), "read agent job")),
        };

    private static CreateWorkflow BuildDirectCreateWorkflowRequest(CreateWorkflowPayload payload)
        => new(
            new WorkflowId(RequireDirectPayloadValue(payload.WorkflowId, nameof(payload.WorkflowId), "create workflow")),
            new CollaborationSpaceId(RequireDirectPayloadValue(payload.SpaceId, nameof(payload.SpaceId), "create workflow")),
            RequireDirectPayloadValue(payload.DisplayName, nameof(payload.DisplayName), "create workflow"),
            BuildOptionalWorkflowOwner(payload.ParticipantId),
            string.IsNullOrWhiteSpace(Normalize(payload.ThreadId)) ? null : new ThreadId(Normalize(payload.ThreadId)!));

    private static PublishPlan BuildDirectPublishWorkflowPlanRequest(PublishWorkflowPlanPayload payload)
        => new(
            new WorkflowId(RequireDirectPayloadValue(payload.WorkflowId, nameof(payload.WorkflowId), "publish workflow plan")),
            new Plan(
                RequireDirectPayloadValue(payload.Title, nameof(payload.Title), "publish workflow plan"),
                BuildDirectWorkflowPlanSteps(payload.Steps, "publish workflow plan")));

    private static CreateTask BuildDirectCreateWorkflowTaskRequest(CreateWorkflowTaskPayload payload)
        => new(
            new TianShu.Contracts.Workflows.Task(
                new TaskId(RequireDirectPayloadValue(payload.TaskId, nameof(payload.TaskId), "create workflow task")),
                new WorkflowId(RequireDirectPayloadValue(payload.WorkflowId, nameof(payload.WorkflowId), "create workflow task")),
                RequireDirectPayloadValue(payload.Title, nameof(payload.Title), "create workflow task"),
                ParseWorkflowTaskState(payload.State, "create workflow task"),
                BuildOptionalWorkflowOwner(payload.ParticipantId)));

    private static UpdateTaskState BuildDirectUpdateWorkflowTaskStateRequest(UpdateWorkflowTaskStatePayload payload)
        => new(
            new TaskId(RequireDirectPayloadValue(payload.TaskId, nameof(payload.TaskId), "update workflow task state")),
            ParseWorkflowTaskState(payload.State, "update workflow task state"),
            BuildOptionalWorkflowOwner(payload.ParticipantId));

    private static CreateCollaborationSpace BuildDirectCreateCollaborationSpaceRequest(CreateCollaborationSpacePayload payload)
        => new(
            new CollaborationSpaceId(RequireDirectPayloadValue(payload.SpaceId, nameof(payload.SpaceId), "create collaboration space")),
            RequireDirectPayloadValue(payload.Key, nameof(payload.Key), "create collaboration space"),
            RequireDirectPayloadValue(payload.DisplayName, nameof(payload.DisplayName), "create collaboration space"),
            new CollaborationSpaceProfile(RequireDirectPayloadValue(payload.Purpose, nameof(payload.Purpose), "create collaboration space")),
            new CollaborationDefaultSet(
                Normalize(payload.DefaultWorkspace),
                Normalize(payload.DefaultExecutionProfile)),
            string.IsNullOrWhiteSpace(Normalize(payload.PolicyKey)) ? null : new CollaborationPolicyRef(Normalize(payload.PolicyKey)!));

    private static ConfigureCollaborationSpace BuildDirectConfigureCollaborationSpaceRequest(ConfigureCollaborationSpacePayload payload)
        => new(
            new CollaborationSpaceId(RequireDirectPayloadValue(payload.SpaceId, nameof(payload.SpaceId), "configure collaboration space")),
            DisplayName: Normalize(payload.DisplayName),
            Profile: string.IsNullOrWhiteSpace(Normalize(payload.Purpose)) ? null : new CollaborationSpaceProfile(Normalize(payload.Purpose)!),
            Defaults: HasDirectCollaborationDefaults(payload.DefaultWorkspace, payload.DefaultExecutionProfile)
                ? new CollaborationDefaultSet(Normalize(payload.DefaultWorkspace), Normalize(payload.DefaultExecutionProfile))
                : null,
            PolicyRef: string.IsNullOrWhiteSpace(Normalize(payload.PolicyKey)) ? null : new CollaborationPolicyRef(Normalize(payload.PolicyKey)!));

    private static ArchiveCollaborationSpace BuildDirectArchiveCollaborationSpaceRequest(ArchiveCollaborationSpacePayload payload)
        => new(new CollaborationSpaceId(RequireDirectPayloadValue(payload.SpaceId, nameof(payload.SpaceId), "archive collaboration space")));

    private static BindParticipantToSession BuildDirectBindParticipantToSessionRequest(BindParticipantToSessionPayload payload)
        => new(
            new SessionId(RequireDirectPayloadValue(payload.SessionId, nameof(payload.SessionId), "bind participant to session")),
            new ParticipantId(RequireDirectPayloadValue(payload.ParticipantId, nameof(payload.ParticipantId), "bind participant to session")));

    private static BindParticipantToWorkflow BuildDirectBindParticipantToWorkflowRequest(BindParticipantToWorkflowPayload payload)
        => new(
            new WorkflowId(RequireDirectPayloadValue(payload.WorkflowId, nameof(payload.WorkflowId), "bind participant to workflow")),
            new ParticipantId(RequireDirectPayloadValue(payload.ParticipantId, nameof(payload.ParticipantId), "bind participant to workflow")));

    private static UpdateParticipantRole BuildDirectUpdateParticipantRoleRequest(UpdateParticipantRolePayload payload)
        => new(
            new ParticipantId(RequireDirectPayloadValue(payload.ParticipantId, nameof(payload.ParticipantId), "update participant role")),
            RequireDirectPayloadValue(payload.Role, nameof(payload.Role), "update participant role"));

    private static ControlPlaneConfigValueWriteCommand BuildDirectConfigValueWriteRequest(ConfigValueWritePayload payload)
        => new()
        {
            KeyPath = Normalize(payload.KeyPath) ?? string.Empty,
            Value = ToContractsStructuredValue(payload.Value),
            MergeStrategy = Normalize(payload.MergeStrategy) ?? "replace",
            WorkingDirectory = Normalize(payload.WorkingDirectory),
            FilePath = Normalize(payload.FilePath),
            ExpectedVersion = Normalize(payload.ExpectedVersion),
        };

    private static ControlPlaneConfigBatchWriteCommand BuildDirectConfigBatchWriteRequest(ConfigBatchWritePayload payload)
    {
        var defaultMergeStrategy = Normalize(payload.MergeStrategy) ?? "replace";
        var items = (payload.Items ?? [])
            .Where(static item => !string.IsNullOrWhiteSpace(item.KeyPath))
            .Select(item => new ControlPlaneConfigWriteItem
            {
                KeyPath = Normalize(item.KeyPath) ?? string.Empty,
                Value = ToContractsStructuredValue(item.Value),
                MergeStrategy = Normalize(item.MergeStrategy) ?? defaultMergeStrategy,
            })
            .ToArray();

        return new ControlPlaneConfigBatchWriteCommand
        {
            Items = items,
            WorkingDirectory = Normalize(payload.WorkingDirectory),
            FilePath = Normalize(payload.FilePath),
            ExpectedVersion = Normalize(payload.ExpectedVersion),
            ReloadUserConfig = payload.ReloadUserConfig,
        };
    }

    private static ControlPlaneConfigRequirementsQuery BuildDirectConfigRequirementsReadRequest(ConfigRequirementsReadPayload payload)
        => new()
        {
            WorkingDirectory = Normalize(payload.WorkingDirectory),
        };

    private static ControlPlaneSkillCatalogQuery BuildDirectSkillsListRequest(SkillsListPayload payload)
        => new()
        {
            WorkingDirectories = (payload.WorkingDirectories ?? [])
                .Select(Normalize)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            ForceReload = payload.ForceReload,
        };

    private static ControlPlaneSkillConfigWriteCommand BuildDirectSkillsConfigWriteRequest(SkillsConfigWritePayload payload)
        => new()
        {
            Path = RequireDirectPayloadValue(payload.Path, nameof(payload.Path), "write skill config"),
            Enabled = payload.Enabled ?? false,
            WorkingDirectory = Normalize(payload.WorkingDirectory),
        };

    private static ControlPlanePluginCatalogQuery BuildDirectPluginListRequest(PluginListPayload payload)
        => new()
        {
            WorkingDirectories = (payload.WorkingDirectories ?? [])
                .Select(Normalize)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            ForceRemoteSync = payload.ForceRemoteSync,
        };

    private static ControlPlanePluginReadQuery BuildDirectPluginReadRequest(PluginReadPayload payload)
        => new()
        {
            MarketplacePath = Normalize(payload.MarketplacePath) ?? string.Empty,
            PluginName = Normalize(payload.PluginName) ?? string.Empty,
        };

    private static ControlPlanePluginInstallCommand BuildDirectPluginInstallRequest(PluginInstallPayload payload)
        => new()
        {
            MarketplacePath = Normalize(payload.MarketplacePath) ?? string.Empty,
            PluginName = Normalize(payload.PluginName) ?? string.Empty,
            WorkingDirectory = Normalize(payload.WorkingDirectory),
        };

    private static ControlPlanePluginUninstallCommand BuildDirectPluginUninstallRequest(PluginUninstallPayload payload)
        => new()
        {
            PluginId = RequireDirectPayloadValue(payload.PluginId, nameof(payload.PluginId), "uninstall plugin"),
            WorkingDirectory = Normalize(payload.WorkingDirectory),
        };

    private static ControlPlaneReviewTarget BuildDirectReviewTarget(ReviewStartPayload payload)
        => Normalize(payload.TargetType)?.ToLowerInvariant() switch
        {
            "uncommittedchanges" => new ControlPlaneReviewUncommittedChangesTarget(),
            _ => new ControlPlaneReviewCustomTarget(),
        };

    private static ControlPlaneStartThreadCommand BuildThreadStartCommand(StartNewThreadPayload payload)
        => new()
        {
            Model = Normalize(payload.Model),
            ModelProvider = Normalize(payload.ModelProvider),
            ServiceTier = SerializeThreadServiceTier(payload.ServiceTier),
            WorkingDirectory = Normalize(payload.WorkingDirectory),
            ApprovalPolicy = SerializeThreadApprovalPolicy(payload.ApprovalPolicy),
            SandboxMode = Normalize(payload.SandboxMode),
            Configuration = NormalizeStructuredDictionary(payload.Config),
            ServiceName = Normalize(payload.ServiceName),
            BaseInstructions = Normalize(payload.BaseInstructions),
            DeveloperInstructions = Normalize(payload.DeveloperInstructions),
            Personality = payload.Personality?.Value,
            Ephemeral = payload.Ephemeral,
            DynamicTools = NormalizeList(payload.DynamicTools),
            PersistExtendedHistory = payload.PersistExtendedHistory ?? false,
            ExperimentalRawEvents = payload.ExperimentalRawEvents,
        };

    private static ControlPlaneResumeThreadCommand BuildThreadResumeCommand(ResumeThreadPayload payload, string threadId)
        => new()
        {
            ThreadId = new ThreadId(threadId),
            History = NormalizeList(payload.History)?.Select(ToContractsStructuredValue).ToArray(),
            Path = Normalize(payload.Path),
            Model = Normalize(payload.Model),
            ModelProvider = Normalize(payload.ModelProvider),
            ServiceTier = SerializeThreadServiceTier(payload.ServiceTier),
            WorkingDirectory = Normalize(payload.WorkingDirectory),
            ApprovalPolicy = SerializeThreadApprovalPolicy(payload.ApprovalPolicy),
            SandboxMode = Normalize(payload.SandboxMode),
            Configuration = NormalizeStructuredDictionary(payload.Config),
            BaseInstructions = Normalize(payload.BaseInstructions),
            DeveloperInstructions = Normalize(payload.DeveloperInstructions),
            Personality = payload.Personality?.Value,
            PersistExtendedHistory = payload.PersistExtendedHistory ?? false,
        };

    private static ControlPlaneForkThreadCommand BuildThreadForkCommand(ForkThreadPayload payload, string threadId)
        => new()
        {
            ThreadId = new ThreadId(threadId),
            Path = Normalize(payload.Path),
            Model = Normalize(payload.Model),
            ModelProvider = Normalize(payload.ModelProvider),
            ServiceTier = SerializeThreadServiceTier(payload.ServiceTier),
            WorkingDirectory = Normalize(payload.WorkingDirectory),
            ApprovalPolicy = SerializeThreadApprovalPolicy(payload.ApprovalPolicy),
            SandboxMode = Normalize(payload.SandboxMode),
            Configuration = NormalizeStructuredDictionary(payload.Config),
            BaseInstructions = Normalize(payload.BaseInstructions),
            DeveloperInstructions = Normalize(payload.DeveloperInstructions),
            Ephemeral = payload.Ephemeral ?? false,
            PersistExtendedHistory = payload.PersistExtendedHistory ?? false,
        };

    private static string? SerializeThreadServiceTier(SidecarServiceTierOverride serviceTier)
        => !serviceTier.IsSpecified ? null : serviceTier.IsCleared ? "null" : serviceTier.Value?.Value;

    private static string? SerializeThreadApprovalPolicy(SidecarApprovalPolicy? approvalPolicy)
    {
        if (approvalPolicy is null)
        {
            return null;
        }

        var element = JsonSerializer.SerializeToElement(approvalPolicy);
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Object => element.GetRawText(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.GetRawText(),
        };
    }

    private static IReadOnlyDictionary<string, StructuredValue>? NormalizeStructuredDictionary(
        IReadOnlyDictionary<string, StructuredValue>? value)
    {
        if (value is null || value.Count == 0)
        {
            return null;
        }

        return value
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
    }

    private static IReadOnlyList<T>? NormalizeList<T>(IReadOnlyList<T>? value)
        => value is { Count: > 0 } ? value : null;

    private static object BuildThreadResponseObject(ControlPlaneThreadSummary result)
        => new
        {
            threadId = result.ThreadId.Value,
            preview = result.Preview,
            name = result.Name,
            cwd = result.WorkingDirectory,
            path = result.Path,
            modelProvider = result.ModelProvider,
            source = result.Source,
            parentThreadId = result.ParentThreadId?.Value,
            lineageDepth = result.LineageDepth,
            cliVersion = result.CliVersion,
            agentNickname = result.AgentNickname,
            agentRole = result.AgentRole,
            createdAt = result.CreatedAt?.ToUnixTimeSeconds(),
            updatedAt = result.UpdatedAt.ToUnixTimeSeconds(),
            ephemeral = result.IsEphemeral,
            status = string.IsNullOrWhiteSpace(result.Status)
                ? null
                : new
                {
                    type = result.Status,
                    activeFlags = result.ActiveFlags.ToArray(),
                },
            gitInfo = string.IsNullOrWhiteSpace(result.GitSha)
                      && string.IsNullOrWhiteSpace(result.GitBranch)
                      && string.IsNullOrWhiteSpace(result.GitOriginUrl)
                ? null
                : new
                {
                    sha = result.GitSha,
                    branch = result.GitBranch,
                    originUrl = result.GitOriginUrl,
                },
            sessionConfiguration = BuildThreadSessionConfigurationObject(result.SessionConfiguration),
        };

    private object BuildThreadDetailObject(ControlPlaneThreadDetail result)
        => new
        {
            id = result.ThreadId.Value,
            preview = result.Preview,
            name = result.Name,
            cwd = result.WorkingDirectory,
            path = result.Path,
            modelProvider = result.ModelProvider,
            source = result.Source,
            parentThreadId = result.ParentThreadId?.Value,
            lineageDepth = result.LineageDepth,
            cliVersion = result.CliVersion,
            agentNickname = result.AgentNickname,
            agentRole = result.AgentRole,
            createdAt = result.CreatedAt?.ToUnixTimeSeconds(),
            updatedAt = result.UpdatedAt.ToUnixTimeSeconds(),
            ephemeral = result.IsEphemeral,
            status = string.IsNullOrWhiteSpace(result.Status)
                ? null
                : new
                {
                    type = result.Status,
                    activeFlags = result.ActiveFlags.ToArray(),
                },
            gitInfo = string.IsNullOrWhiteSpace(result.GitSha)
                      && string.IsNullOrWhiteSpace(result.GitBranch)
                      && string.IsNullOrWhiteSpace(result.GitOriginUrl)
                ? null
                : new
                {
                    sha = result.GitSha,
                    branch = result.GitBranch,
                    originUrl = result.GitOriginUrl,
                },
            turns = result.Turns.Select(turn => new
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
                items = turn.Items.Select(item => SerializeThreadTurnItem(item)),
            }),
            seedHistory = result.SeedHistory.Select(item => SerializeSeedHistoryItem(item)),
            pendingInputState = BuildPendingInputStateObject(result.PendingInputState),
            pendingInteractiveRequests = BuildPendingInteractiveRequestsObject(result.PendingInteractiveRequests),
            sessionConfiguration = BuildThreadSessionConfigurationObject(result.SessionConfiguration),
        };

    private object BuildThreadOperationResponse(ControlPlaneThreadOperationResult result)
        => new
        {
            thread = result.Thread is null ? null : BuildThreadDetailObject(result.Thread),
        };

    private object BuildThreadOperationResponse(HostThreadOperationResult result)
        => new
        {
            thread = result.Thread is null ? null : BuildThreadDetailObject(result.Thread),
        };

    private static object BuildLoadedThreadListResponse(HostLoadedThreadListResult result)
        => new
        {
            data = result.ThreadIds.Select(static item => item.Value).ToArray(),
            nextCursor = result.NextCursor,
        };

    private static object BuildLoadedThreadListResponse(ControlPlaneLoadedThreadListResult result)
        => new
        {
            data = result.ThreadIds.Select(static item => item.Value).ToArray(),
            nextCursor = result.NextCursor,
        };

    private static object BuildThreadUnsubscribeResponse(HostThreadUnsubscribeResult result)
        => new
        {
            status = result.Status,
        };

    private static object BuildThreadUnsubscribeResponse(ControlPlaneThreadUnsubscribeResult result)
        => new
        {
            status = result.Status,
        };

    private static object BuildThreadElicitationResponse(ControlPlaneThreadElicitationResult result)
        => new
        {
            count = result.Count,
            paused = result.Paused,
        };

    private static object? BuildThreadSessionConfigurationObject(ControlPlaneThreadSessionConfiguration? sessionConfiguration)
        => sessionConfiguration is null
            ? null
            : new
            {
                model = sessionConfiguration.Model,
                modelProvider = sessionConfiguration.ModelProvider,
                modelProviderId = sessionConfiguration.ModelProviderId,
                serviceTier = sessionConfiguration.ServiceTier,
                approvalPolicy = sessionConfiguration.ApprovalPolicy,
                sandboxPolicy = sessionConfiguration.SandboxPolicy,
                sandboxPolicyPayload = sessionConfiguration.SandboxPolicyPayload?.ToPlainObject(),
                reasoningEffort = sessionConfiguration.ReasoningEffort,
                historyLogId = sessionConfiguration.HistoryLogId,
                historyEntryCount = sessionConfiguration.HistoryEntryCount,
                rolloutPath = sessionConfiguration.RolloutPath,
                reasoningSummary = sessionConfiguration.ReasoningSummary,
                verbosity = sessionConfiguration.Verbosity,
                personality = sessionConfiguration.Personality,
                allowLoginShell = sessionConfiguration.AllowLoginShell,
                shellEnvironmentPolicy = sessionConfiguration.ShellEnvironmentPolicy?.ToPlainObject(),
                providerBaseUrl = sessionConfiguration.ProviderBaseUrl,
                providerApiKeyEnvironmentVariable = sessionConfiguration.ProviderApiKeyEnvironmentVariable,
                providerWireApi = sessionConfiguration.ProviderWireApi,
                providerRequestMaxRetries = sessionConfiguration.ProviderRequestMaxRetries,
                providerStreamMaxRetries = sessionConfiguration.ProviderStreamMaxRetries,
                providerStreamIdleTimeoutMs = sessionConfiguration.ProviderStreamIdleTimeoutMs,
                providerWebsocketConnectTimeoutMs = sessionConfiguration.ProviderWebsocketConnectTimeoutMs,
                providerSupportsWebsockets = sessionConfiguration.ProviderSupportsWebsockets,
                webSearchMode = sessionConfiguration.WebSearchMode,
                serviceName = sessionConfiguration.ServiceName,
                baseInstructions = sessionConfiguration.BaseInstructions,
                developerInstructions = sessionConfiguration.DeveloperInstructions,
                userInstructions = sessionConfiguration.UserInstructions,
                dynamicTools = sessionConfiguration.DynamicTools?.Select(static item => item.ToPlainObject()).ToArray(),
                collaborationMode = sessionConfiguration.CollaborationMode?.ToPlainObject(),
                persistExtendedHistory = sessionConfiguration.PersistExtendedHistory,
                forkedFromId = sessionConfiguration.ForkedFromThreadId?.Value,
                cwd = sessionConfiguration.WorkingDirectory,
                sessionSource = sessionConfiguration.SessionSource,
                windowsSandboxLevel = sessionConfiguration.WindowsSandboxLevel,
                defaultModeRequestUserInputEnabled = sessionConfiguration.DefaultModeRequestUserInputEnabled,
            };

    private static object BuildConversationMessageObject(ControlPlaneConversationMessage message)
        => new
        {
            role = message.Role.ToString().ToLowerInvariant(),
            content = message.Content,
            timestamp = message.Timestamp.ToUnixTimeSeconds(),
            inputs = (message.ContentItems ?? Array.Empty<ControlPlaneInputItem>())
                .Select(BuildUserInputObject)
                .ToArray(),
        };

    private static object ParseParametersJson(string? parametersJson)
    {
        var normalized = Normalize(parametersJson);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        using var document = JsonDocument.Parse(normalized!);
        return document.RootElement.Clone();
    }

    private static ControlPlaneCodeModeExecCommand BuildCodeModeExecRequest(object parameters, string? fallbackThreadId)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("code mode exec 的 parametersJson 必须是 JSON 对象。");
        }

        var threadId = Normalize(ReadString(json, "threadId")) ?? Normalize(fallbackThreadId);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            throw new InvalidOperationException("code mode exec 缺少 threadId。");
        }

        var input = ReadString(json, "input");
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new InvalidOperationException("code mode exec 缺少 input。");
        }

        return new ControlPlaneCodeModeExecCommand
        {
            ThreadId = new ThreadId(threadId!),
            Input = input!,
            YieldTimeMs = ReadInt32(json, "yieldTimeMs"),
            MaxOutputTokens = ReadInt32(json, "maxOutputTokens"),
        };
    }

    private static ControlPlaneCodeModeWaitCommand BuildCodeModeWaitRequest(object parameters, string? fallbackThreadId)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("code mode wait 的 parametersJson 必须是 JSON 对象。");
        }

        var threadId = Normalize(ReadString(json, "threadId")) ?? Normalize(fallbackThreadId);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            throw new InvalidOperationException("code mode wait 缺少 threadId。");
        }

        var cellId = Normalize(ReadString(json, "cellId"));
        if (string.IsNullOrWhiteSpace(cellId))
        {
            throw new InvalidOperationException("code mode wait 缺少 cellId。");
        }

        return new ControlPlaneCodeModeWaitCommand
        {
            ThreadId = new ThreadId(threadId!),
            CellId = cellId!,
            YieldTimeMs = ReadInt32(json, "yieldTimeMs"),
            MaxTokens = ReadInt32(json, "maxTokens"),
            Terminate = ReadBoolean(json, "terminate"),
        };
    }

    private static object BuildCodeModeResultPayload(ControlPlaneCodeModeResult result)
        => new Dictionary<string, object?>
        {
            ["success"] = result.Success,
            ["status"] = result.Status,
            ["threadId"] = result.ThreadId?.Value,
            ["turnId"] = result.TurnId?.Value,
            ["cellId"] = result.CellId,
            ["output"] = result.Output,
            ["contentItems"] = result.ContentItems.Select(
                static item => new Dictionary<string, object?>
                {
                    ["type"] = item.Type,
                    ["text"] = item.Text,
                    ["imageUrl"] = item.ImageUrl,
                    ["detail"] = item.Detail,
                }).ToArray(),
        };

    private static ControlPlaneConfigReadQuery BuildRuntimeConfigReadRequest(object parameters)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            return new ControlPlaneConfigReadQuery();
        }

        return new ControlPlaneConfigReadQuery
        {
            WorkingDirectory = Normalize(ReadString(json, "cwd")),
            IncludeLayers = ReadBoolean(json, "includeLayers") || ReadBoolean(json, "include_layers"),
        };
    }

    private static ControlPlaneModelCatalogQuery BuildRuntimeModelListRequest(object parameters)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            return new ControlPlaneModelCatalogQuery();
        }

        return new ControlPlaneModelCatalogQuery
        {
            Limit = Math.Clamp(ReadInt32(json, "limit") ?? 50, 1, 200),
            IncludeHidden = ReadBoolean(json, "includeHidden") || ReadBoolean(json, "include_hidden"),
        };
    }

    private static GetCapabilityCatalog BuildRuntimeCapabilityCatalogRequest(object parameters)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            return new GetCapabilityCatalog();
        }

        return new GetCapabilityCatalog(
            workspacePath: Normalize(ReadString(json, "workspacePath")) ?? Normalize(ReadString(json, "cwd")),
            includeHiddenModels: ReadBoolean(json, "includeHiddenModels") || ReadBoolean(json, "includeHidden") || ReadBoolean(json, "include_hidden"),
            modelLimit: Math.Clamp(ReadInt32(json, "modelLimit") ?? ReadInt32(json, "limit") ?? 200, 1, 200));
    }

    private static ResolveEngineBinding BuildRuntimeResolveEngineBindingRequest(object parameters)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            return new ResolveEngineBinding();
        }

        return new ResolveEngineBinding(
            WorkspacePath: Normalize(ReadString(json, "workspacePath")) ?? Normalize(ReadString(json, "cwd")),
            PreferredProviderKey: Normalize(ReadString(json, "providerKey")),
            PreferredModelKey: Normalize(ReadString(json, "modelKey")),
            ReasoningEffort: Normalize(ReadString(json, "reasoningEffort")),
            ReasoningSummary: Normalize(ReadString(json, "reasoningSummary")),
            Verbosity: Normalize(ReadString(json, "verbosity")),
            PreferWebsocketTransport: ReadBoolean(json, "preferWebsocketTransport") || ReadBoolean(json, "prefer_websocket_transport"));
    }

    private static ControlPlaneAgentListQuery BuildRuntimeAgentListRequest(object parameters)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            return new ControlPlaneAgentListQuery();
        }

        return new ControlPlaneAgentListQuery
        {
            Limit = ReadInt32(json, "limit"),
            Cursor = Normalize(ReadString(json, "cursor")),
            IncludePrimaryThreads = ReadBoolean(json, "includePrimaryThreads") || ReadBoolean(json, "include_primary_threads"),
        };
    }

    private static ControlPlaneSkillCatalogQuery BuildRuntimeSkillsListRequest(object parameters)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            return new ControlPlaneSkillCatalogQuery();
        }

        var workingDirectories = new List<string>(ReadStringArray(json, "cwds"));
        var singleWorkingDirectory = Normalize(ReadString(json, "cwd"));
        if (!string.IsNullOrWhiteSpace(singleWorkingDirectory)
            && !workingDirectories.Contains(singleWorkingDirectory!, StringComparer.OrdinalIgnoreCase))
        {
            workingDirectories.Add(singleWorkingDirectory!);
        }

        var extraRoots = new List<ControlPlaneSkillsExtraRootsForWorkingDirectory>();
        if (json.TryGetProperty("perCwdExtraUserRoots", out var perCwdExtraUserRoots)
            && perCwdExtraUserRoots.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in perCwdExtraUserRoots.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var cwd = Normalize(ReadString(item, "cwd"));
                if (string.IsNullOrWhiteSpace(cwd))
                {
                    continue;
                }

                extraRoots.Add(new ControlPlaneSkillsExtraRootsForWorkingDirectory
                {
                    WorkingDirectory = cwd!,
                    ExtraUserRoots = ReadStringArray(item, "extraUserRoots"),
                });
            }
        }

        return new ControlPlaneSkillCatalogQuery
        {
            WorkingDirectories = workingDirectories,
            ForceReload = ReadBoolean(json, "forceReload") || ReadBoolean(json, "force_reload"),
            ExtraRootsByWorkingDirectory = extraRoots,
        };
    }

    private static ControlPlaneSkillConfigWriteCommand BuildRuntimeSkillsConfigWriteRequest(object parameters)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            return new ControlPlaneSkillConfigWriteCommand();
        }

        return new ControlPlaneSkillConfigWriteCommand
        {
            Path = Normalize(ReadString(json, "path")) ?? string.Empty,
            Enabled = ReadBoolean(json, "enabled"),
            WorkingDirectory = Normalize(ReadString(json, "cwd")),
        };
    }

    private static ControlPlaneRemoteSkillCatalogQuery BuildRuntimeSkillsRemoteListRequest(object parameters)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            return new ControlPlaneRemoteSkillCatalogQuery();
        }

        return new ControlPlaneRemoteSkillCatalogQuery
        {
            HazelnutScope = Normalize(ReadString(json, "hazelnutScope")),
            ProductSurface = Normalize(ReadString(json, "productSurface")),
            Enabled = ReadNullableBoolean(json, "enabled"),
        };
    }

    private static ControlPlaneRemoteSkillExportCommand BuildRuntimeSkillsRemoteExportRequest(object parameters)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            return new ControlPlaneRemoteSkillExportCommand();
        }

        return new ControlPlaneRemoteSkillExportCommand
        {
            HazelnutId = Normalize(ReadString(json, "hazelnutId")) ?? string.Empty,
        };
    }

    private static ControlPlanePluginCatalogQuery BuildRuntimePluginListRequest(object parameters)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            return new ControlPlanePluginCatalogQuery();
        }

        var workingDirectories = new List<string>(ReadStringArray(json, "cwds"));
        var singleWorkingDirectory = Normalize(ReadString(json, "cwd"));
        if (!string.IsNullOrWhiteSpace(singleWorkingDirectory)
            && !workingDirectories.Contains(singleWorkingDirectory!, StringComparer.OrdinalIgnoreCase))
        {
            workingDirectories.Add(singleWorkingDirectory!);
        }

        return new ControlPlanePluginCatalogQuery
        {
            WorkingDirectories = workingDirectories,
            ForceRemoteSync = ReadBoolean(json, "forceRemoteSync") || ReadBoolean(json, "force_remote_sync"),
        };
    }

    private static ControlPlanePluginReadQuery BuildRuntimePluginReadRequest(object parameters)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            return new ControlPlanePluginReadQuery();
        }

        return new ControlPlanePluginReadQuery
        {
            MarketplacePath = Normalize(ReadString(json, "marketplacePath")) ?? string.Empty,
            PluginName = Normalize(ReadString(json, "pluginName")) ?? string.Empty,
        };
    }

    private static ControlPlanePluginInstallCommand BuildRuntimePluginInstallRequest(object parameters)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            return new ControlPlanePluginInstallCommand();
        }

        return new ControlPlanePluginInstallCommand
        {
            MarketplacePath = Normalize(ReadString(json, "marketplacePath")) ?? string.Empty,
            PluginName = Normalize(ReadString(json, "pluginName")) ?? string.Empty,
            WorkingDirectory = Normalize(ReadString(json, "cwd")),
        };
    }

    private static ControlPlanePluginUninstallCommand BuildRuntimePluginUninstallRequest(object parameters)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            return new ControlPlanePluginUninstallCommand();
        }

        return new ControlPlanePluginUninstallCommand
        {
            PluginId = Normalize(ReadString(json, "pluginId")) ?? Normalize(ReadString(json, "plugin_id")) ?? string.Empty,
            WorkingDirectory = Normalize(ReadString(json, "cwd")),
        };
    }

    private static ControlPlaneAppCatalogQuery BuildRuntimeAppListRequest(object parameters)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            return new ControlPlaneAppCatalogQuery();
        }

        var threadId = Normalize(ReadString(json, "threadId"));
        return new ControlPlaneAppCatalogQuery
        {
            Limit = ReadInt32(json, "limit"),
            Cursor = Normalize(ReadString(json, "cursor")),
            ThreadId = string.IsNullOrWhiteSpace(threadId) ? null : new ThreadId(threadId),
            ForceRefetch = ReadBoolean(json, "forceRefetch") || ReadBoolean(json, "force_refetch"),
        };
    }

    private static ControlPlaneReviewStartCommand BuildRuntimeReviewStartRequest(object parameters)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            return new ControlPlaneReviewStartCommand();
        }

        return new ControlPlaneReviewStartCommand
        {
            ThreadId = Normalize(ReadString(json, "threadId")) ?? string.Empty,
            Target = BuildRuntimeReviewTargetRequest(json),
            Delivery = Normalize(ReadString(json, "delivery")),
        };
    }

    private static ControlPlaneReviewTarget BuildRuntimeReviewTargetRequest(JsonElement json)
    {
        if (!json.TryGetProperty("target", out var target) || target.ValueKind != JsonValueKind.Object)
        {
            return new ControlPlaneReviewCustomTarget();
        }

        return Normalize(ReadString(target, "type")) switch
        {
            "uncommittedChanges" => new ControlPlaneReviewUncommittedChangesTarget(),
            "baseBranch" => new ControlPlaneReviewBaseBranchTarget
            {
                Branch = Normalize(ReadString(target, "branch")) ?? string.Empty,
            },
            "commit" => new ControlPlaneReviewCommitTarget
            {
                Sha = Normalize(ReadString(target, "sha")) ?? string.Empty,
                Title = Normalize(ReadString(target, "title")),
            },
            "custom" => new ControlPlaneReviewCustomTarget
            {
                Instructions = Normalize(ReadString(target, "instructions")) ?? string.Empty,
            },
            _ => new ControlPlaneReviewCustomTarget(),
        };
    }

    private static ControlPlaneExperimentalFeatureQuery BuildRuntimeExperimentalFeatureListRequest(object parameters)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            return new ControlPlaneExperimentalFeatureQuery();
        }

        return new ControlPlaneExperimentalFeatureQuery
        {
            Limit = ReadInt32(json, "limit"),
            Cursor = Normalize(ReadString(json, "cursor")),
        };
    }

    private static ControlPlaneMcpServerStatusQuery BuildRuntimeMcpServerStatusListRequest(object parameters)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            return new ControlPlaneMcpServerStatusQuery();
        }

        return new ControlPlaneMcpServerStatusQuery
        {
            Limit = ReadInt32(json, "limit"),
            Cursor = Normalize(ReadString(json, "cursor")),
        };
    }

    private static ControlPlaneMcpServerOauthLoginStartCommand BuildRuntimeMcpServerOauthLoginRequest(object parameters)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            return new ControlPlaneMcpServerOauthLoginStartCommand();
        }

        return new ControlPlaneMcpServerOauthLoginStartCommand
        {
            Name = Normalize(ReadString(json, "name")) ?? string.Empty,
            TimeoutSecs = ReadInt64(json, "timeoutSecs"),
        };
    }

    private static ControlPlaneConversationArtifactQuery BuildRuntimeConversationSummaryRequest(object parameters)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            return new ControlPlaneConversationArtifactQuery();
        }

        return new ControlPlaneConversationArtifactQuery
        {
            ThreadId = string.IsNullOrWhiteSpace(Normalize(ReadString(json, "conversationId")) ?? Normalize(ReadString(json, "threadId")))
                ? null
                : new ThreadId((Normalize(ReadString(json, "conversationId")) ?? Normalize(ReadString(json, "threadId")))!),
            RolloutPath = Normalize(ReadString(json, "rolloutPath")),
        };
    }

    private static GetSessionOverview BuildRuntimeSessionOverviewRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "runtime surface session overview");
        return new GetSessionOverview(new SessionId(ReadRequiredString(json, "sessionId", "runtime surface session overview")));
    }

    private static GetThreadProjection BuildRuntimeThreadProjectionRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "runtime surface thread projection");
        var threadId = Normalize(ReadString(json, "threadId")) ?? Normalize(ReadString(json, "conversationId"));
        if (string.IsNullOrWhiteSpace(threadId))
        {
            throw new InvalidOperationException("runtime surface thread projection 缺少必填字段：threadId");
        }

        return new GetThreadProjection(new ThreadId(threadId));
    }

    private static ListPendingApprovals BuildRuntimeApprovalQueueRequest(object parameters)
    {
        var json = TryGetOptionalJsonObject(parameters, "runtime surface approval queue");
        if (json is null)
        {
            return new ListPendingApprovals();
        }

        var participantId = Normalize(ReadString(json.Value, "requestedFromParticipantId"))
                            ?? Normalize(ReadString(json.Value, "requested_from_participant_id"))
                            ?? Normalize(ReadString(json.Value, "participantId"))
                            ?? Normalize(ReadString(json.Value, "participant_id"));
        return new ListPendingApprovals(string.IsNullOrWhiteSpace(participantId) ? null : new ParticipantId(participantId!));
    }

    private static ListUserInputRequests BuildRuntimeUserInputListRequest(object parameters)
    {
        var json = TryGetOptionalJsonObject(parameters, "runtime surface user-input request list");
        if (json is null)
        {
            return new ListUserInputRequests();
        }

        var participantId = Normalize(ReadString(json.Value, "requestedFromParticipantId"))
                            ?? Normalize(ReadString(json.Value, "requested_from_participant_id"))
                            ?? Normalize(ReadString(json.Value, "participantId"))
                            ?? Normalize(ReadString(json.Value, "participant_id"));
        return new ListUserInputRequests(string.IsNullOrWhiteSpace(participantId) ? null : new ParticipantId(participantId!));
    }

    private static ListSessions BuildRuntimeSessionListRequest(object parameters)
    {
        var json = TryGetOptionalJsonObject(parameters, "runtime surface session list");
        if (json is null)
        {
            return new ListSessions();
        }

        var collaborationSpaceId = Normalize(ReadString(json.Value, "collaborationSpaceId"))
                                   ?? Normalize(ReadString(json.Value, "collaborationId"))
                                   ?? Normalize(ReadString(json.Value, "spaceId"));
        return new ListSessions(
            CollaborationSpaceId: string.IsNullOrWhiteSpace(collaborationSpaceId) ? null : new CollaborationSpaceId(collaborationSpaceId!),
            IncludeClosed: ReadBoolean(json.Value, "includeClosed") || ReadBoolean(json.Value, "include_closed"));
    }

    private static GetWorkflowBoard BuildRuntimeWorkflowBoardRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "runtime surface workflow board");
        return new GetWorkflowBoard(new WorkflowId(ReadRequiredString(json, "workflowId", "runtime surface workflow board")));
    }

    private static GetTaskBoard BuildRuntimeTaskBoardRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "runtime surface task board");
        return new GetTaskBoard(new WorkflowId(ReadRequiredString(json, "workflowId", "runtime surface task board")));
    }

    private static GetPlanProjection BuildRuntimePlanProjectionRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "runtime surface plan projection");
        return new GetPlanProjection(new WorkflowId(ReadRequiredString(json, "workflowId", "runtime surface plan projection")));
    }

    private static CreateWorkflow BuildRuntimeCreateWorkflowRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "runtime surface create workflow");
        return new CreateWorkflow(
            new WorkflowId(ReadRequiredString(json, "workflowId", "runtime surface create workflow")),
            new CollaborationSpaceId(ReadRequiredString(json, "spaceId", "runtime surface create workflow")),
            ReadRequiredString(json, "displayName", "runtime surface create workflow"),
            BuildOptionalWorkflowOwner(ReadString(json, "participantId")),
            string.IsNullOrWhiteSpace(Normalize(ReadString(json, "threadId"))) ? null : new ThreadId(Normalize(ReadString(json, "threadId"))!));
    }

    private static PublishPlan BuildRuntimePublishWorkflowPlanRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "runtime surface publish workflow plan");
        return new PublishPlan(
            new WorkflowId(ReadRequiredString(json, "workflowId", "runtime surface publish workflow plan")),
            new Plan(
                ReadRequiredString(json, "title", "runtime surface publish workflow plan"),
                BuildRuntimeWorkflowPlanSteps(json, "runtime surface publish workflow plan")));
    }

    private static CreateTask BuildRuntimeCreateWorkflowTaskRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "runtime surface create workflow task");
        return new CreateTask(
            new TianShu.Contracts.Workflows.Task(
                new TaskId(ReadRequiredString(json, "taskId", "runtime surface create workflow task")),
                new WorkflowId(ReadRequiredString(json, "workflowId", "runtime surface create workflow task")),
                ReadRequiredString(json, "title", "runtime surface create workflow task"),
                ParseWorkflowTaskState(ReadString(json, "state"), "runtime surface create workflow task"),
                BuildOptionalWorkflowOwner(ReadString(json, "participantId"))));
    }

    private static UpdateTaskState BuildRuntimeUpdateWorkflowTaskStateRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "runtime surface update workflow task state");
        return new UpdateTaskState(
            new TaskId(ReadRequiredString(json, "taskId", "runtime surface update workflow task state")),
            ParseWorkflowTaskState(ReadString(json, "state"), "runtime surface update workflow task state"),
            BuildOptionalWorkflowOwner(ReadString(json, "participantId")));
    }

    private static GetAgentRoster BuildRuntimeAgentRosterRequest(object parameters)
    {
        var json = TryGetOptionalJsonObject(parameters, "runtime surface agent roster");
        if (json is null)
        {
            return new GetAgentRoster();
        }

        var workflowId = Normalize(ReadString(json.Value, "workflowId"));
        return new GetAgentRoster(string.IsNullOrWhiteSpace(workflowId) ? null : new WorkflowId(workflowId!));
    }

    private static GetTeamProjection BuildRuntimeTeamProjectionRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "runtime surface team projection");
        return new GetTeamProjection(new TeamId(ReadRequiredString(json, "teamId", "runtime surface team projection")));
    }

    private static GetAccountProfile BuildRuntimeAccountProfileRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "runtime surface account profile");
        return new GetAccountProfile(new AccountId(ReadRequiredString(json, "accountId", "runtime surface account profile")));
    }

    private static ListBoundDevices BuildRuntimeBoundDeviceListRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "runtime surface bound-device list");
        return new ListBoundDevices(new AccountId(ReadRequiredString(json, "accountId", "runtime surface bound-device list")));
    }

    private static ListMemorySpaces BuildRuntimeMemorySpaceListRequest(object parameters)
    {
        var json = TryGetOptionalJsonObject(parameters, "runtime surface memory space list");
        if (json is null)
        {
            return new ListMemorySpaces();
        }

        var rawScopeKind = Normalize(ReadString(json.Value, "scopeKind"))
                           ?? Normalize(ReadString(json.Value, "memoryScopeKind"))
                           ?? Normalize(ReadString(json.Value, "scope_kind"))
                           ?? Normalize(ReadString(json.Value, "memory_scope_kind"));
        return new ListMemorySpaces(string.IsNullOrWhiteSpace(rawScopeKind) ? null : ParseMemoryScopeKind(rawScopeKind));
    }

    private static ListMemoryProviders BuildRuntimeMemoryProviderListRequest(object parameters)
    {
        var json = TryGetOptionalJsonObject(parameters, "runtime surface memory provider list");
        if (json is null)
        {
            return new ListMemoryProviders();
        }

        var rawScopeKind = Normalize(ReadString(json.Value, "scopeKind"))
                           ?? Normalize(ReadString(json.Value, "memoryScopeKind"))
                           ?? Normalize(ReadString(json.Value, "scope_kind"))
                           ?? Normalize(ReadString(json.Value, "memory_scope_kind"));
        return new ListMemoryProviders(string.IsNullOrWhiteSpace(rawScopeKind) ? null : ParseMemoryScopeKind(rawScopeKind));
    }

    private static ResolveMemoryOverlay BuildRuntimeMemoryOverlayRequest(object parameters)
    {
        var json = TryGetOptionalJsonObject(parameters, "runtime surface memory overlay");
        if (json is null)
        {
            return new ResolveMemoryOverlay();
        }

        var memorySpaceId = Normalize(ReadString(json.Value, "memorySpaceId"));
        var collaborationSpaceId = Normalize(ReadString(json.Value, "collaborationSpaceId"))
                                   ?? Normalize(ReadString(json.Value, "collaborationId"))
                                   ?? Normalize(ReadString(json.Value, "spaceId"));
        return new ResolveMemoryOverlay(
            MemorySpaceId: string.IsNullOrWhiteSpace(memorySpaceId) ? null : new MemorySpaceId(memorySpaceId!),
            CollaborationSpaceId: string.IsNullOrWhiteSpace(collaborationSpaceId) ? null : new CollaborationSpaceId(collaborationSpaceId!));
    }

    private static FilterMemory BuildRuntimeFilterMemoryRequest(object parameters)
        => ReadTypedPayload<FilterMemory>(parameters, "runtime surface memory filter");

    private static ListMemoryReviews BuildRuntimeMemoryReviewListRequest(object parameters)
        => ReadTypedPayload<ListMemoryReviews>(parameters, "runtime surface memory review list");

    private static AddMemory BuildRuntimeAddMemoryRequest(object parameters)
        => ReadTypedPayload<AddMemory>(parameters, "runtime surface memory add");

    private static ExtractMemory BuildRuntimeExtractMemoryRequest(object parameters)
        => ReadTypedPayload<ExtractMemory>(parameters, "runtime surface memory extract");

    private static ImportMemory BuildRuntimeImportMemoryRequest(object parameters)
        => ReadTypedPayload<ImportMemory>(parameters, "runtime surface memory import");

    private static ExportMemory BuildRuntimeExportMemoryRequest(object parameters)
        => ReadTypedPayload<ExportMemory>(parameters, "runtime surface memory export");

    private static BindMemoryProvider BuildRuntimeBindMemoryProviderRequest(object parameters)
        => ReadTypedPayload<BindMemoryProvider>(parameters, "runtime surface memory provider bind");

    private static RunMemoryConsolidation BuildRuntimeRunMemoryConsolidationRequest(object parameters)
        => ReadTypedPayload<RunMemoryConsolidation>(parameters, "runtime surface memory consolidation");

    private static ForgetMemory BuildRuntimeForgetMemoryRequest(object parameters)
        => ReadTypedPayload<ForgetMemory>(parameters, "runtime surface memory forget");

    private static DeleteMemory BuildRuntimeDeleteMemoryRequest(object parameters)
        => ReadTypedPayload<DeleteMemory>(parameters, "runtime surface memory delete");

    private static SupersedeMemory BuildRuntimeSupersedeMemoryRequest(object parameters)
        => ReadTypedPayload<SupersedeMemory>(parameters, "runtime surface memory supersede");

    private static ApproveMemoryReview BuildRuntimeApproveMemoryReviewRequest(object parameters)
        => ReadTypedPayload<ApproveMemoryReview>(parameters, "runtime surface memory review approve");

    private static DemoteMemoryReview BuildRuntimeDemoteMemoryReviewRequest(object parameters)
        => ReadTypedPayload<DemoteMemoryReview>(parameters, "runtime surface memory review demote");

    private static MergeMemoryReview BuildRuntimeMergeMemoryReviewRequest(object parameters)
        => ReadTypedPayload<MergeMemoryReview>(parameters, "runtime surface memory review merge");

    private static RestoreMemoryReview BuildRuntimeRestoreMemoryReviewRequest(object parameters)
        => ReadTypedPayload<RestoreMemoryReview>(parameters, "runtime surface memory review restore");

    private static RecordMemoryFeedback BuildRuntimeRecordMemoryFeedbackRequest(object parameters)
        => ReadTypedPayload<RecordMemoryFeedback>(parameters, "runtime surface memory feedback");

    private static RecordMemoryCitation BuildRuntimeRecordMemoryCitationRequest(object parameters)
        => ReadTypedPayload<RecordMemoryCitation>(parameters, "runtime surface memory citation");

    private static GetExecutionTrace BuildRuntimeExecutionTraceRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "runtime surface execution trace");
        return new GetExecutionTrace(new ExecutionTraceId(ReadRequiredString(json, "traceId", "runtime surface execution trace")));
    }

    private static ListAttemptSummaries BuildRuntimeAttemptSummaryListRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "runtime surface attempt summary list");
        return new ListAttemptSummaries(new ExecutionId(ReadRequiredString(json, "executionId", "runtime surface attempt summary list")));
    }

    private static GetCollaborationSpaceOverview BuildRuntimeCollaborationSpaceOverviewRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "runtime surface collaboration space overview");
        return new GetCollaborationSpaceOverview(
            new CollaborationSpaceId(
                ReadRequiredString(
                    json,
                    ResolveSpaceIdPropertyName(json),
                    "runtime surface collaboration space overview")));
    }

    private static GetCollaborationSpaceProjection BuildRuntimeCollaborationSpaceProjectionRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "runtime surface collaboration space projection");
        return new GetCollaborationSpaceProjection(
            new CollaborationSpaceId(
                ReadRequiredString(
                    json,
                    ResolveSpaceIdPropertyName(json),
                    "runtime surface collaboration space projection")));
    }

    private static ListCollaborationSpaces BuildRuntimeCollaborationSpaceListRequest(object parameters)
    {
        var json = TryGetOptionalJsonObject(parameters, "runtime surface collaboration list");
        return json is null
            ? new ListCollaborationSpaces()
            : new ListCollaborationSpaces(ReadBoolean(json.Value, "includeArchived") || ReadBoolean(json.Value, "include_archived"));
    }

    private static GetParticipantProjection BuildRuntimeParticipantProjectionRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "runtime surface participant projection");
        return new GetParticipantProjection(new ParticipantId(ReadRequiredString(json, "participantId", "runtime surface participant projection")));
    }

    private static GetParticipantViewProjection BuildRuntimeParticipantViewProjectionRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "runtime surface participant view projection");
        return new GetParticipantViewProjection(new ParticipantId(ReadRequiredString(json, "participantId", "runtime surface participant view projection")));
    }

    private static ListParticipantsInScope BuildRuntimeParticipantListRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "runtime surface participant list");
        return new ListParticipantsInScope(
            new CollaborationSpaceId(
                ReadRequiredString(
                    json,
                    ResolveSpaceIdPropertyName(json),
                    "runtime surface participant list")));
    }

    private static CreateCollaborationSpace BuildRuntimeCreateCollaborationSpaceRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "runtime surface create collaboration space");
        return new CreateCollaborationSpace(
            new CollaborationSpaceId(
                ReadRequiredString(
                    json,
                    ResolveSpaceIdPropertyName(json),
                    "runtime surface create collaboration space")),
            ReadRequiredString(json, "key", "runtime surface create collaboration space"),
            ReadRequiredString(json, "displayName", "runtime surface create collaboration space"),
            new CollaborationSpaceProfile(ReadRequiredString(json, "purpose", "runtime surface create collaboration space")),
            new CollaborationDefaultSet(
                Normalize(ReadString(json, "defaultWorkspace")),
                Normalize(ReadString(json, "defaultExecutionProfile"))),
            string.IsNullOrWhiteSpace(Normalize(ReadString(json, "policyKey"))) ? null : new CollaborationPolicyRef(Normalize(ReadString(json, "policyKey"))!));
    }

    private static ConfigureCollaborationSpace BuildRuntimeConfigureCollaborationSpaceRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "runtime surface configure collaboration space");
        return new ConfigureCollaborationSpace(
            new CollaborationSpaceId(
                ReadRequiredString(
                    json,
                    ResolveSpaceIdPropertyName(json),
                    "runtime surface configure collaboration space")),
            DisplayName: Normalize(ReadString(json, "displayName")),
            Profile: string.IsNullOrWhiteSpace(Normalize(ReadString(json, "purpose"))) ? null : new CollaborationSpaceProfile(Normalize(ReadString(json, "purpose"))!),
            Defaults: HasRuntimeCollaborationDefaults(json)
                ? new CollaborationDefaultSet(
                    Normalize(ReadString(json, "defaultWorkspace")),
                    Normalize(ReadString(json, "defaultExecutionProfile")))
                : null,
            PolicyRef: string.IsNullOrWhiteSpace(Normalize(ReadString(json, "policyKey"))) ? null : new CollaborationPolicyRef(Normalize(ReadString(json, "policyKey"))!));
    }

    private static ArchiveCollaborationSpace BuildRuntimeArchiveCollaborationSpaceRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "runtime surface archive collaboration space");
        return new ArchiveCollaborationSpace(
            new CollaborationSpaceId(
                ReadRequiredString(
                    json,
                    ResolveSpaceIdPropertyName(json),
                    "runtime surface archive collaboration space")));
    }

    private static BindParticipantToSession BuildRuntimeBindParticipantToSessionRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "runtime surface bind participant to session");
        return new BindParticipantToSession(
            new SessionId(ReadRequiredString(json, "sessionId", "runtime surface bind participant to session")),
            new ParticipantId(ReadRequiredString(json, "participantId", "runtime surface bind participant to session")));
    }

    private static BindParticipantToWorkflow BuildRuntimeBindParticipantToWorkflowRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "runtime surface bind participant to workflow");
        return new BindParticipantToWorkflow(
            new WorkflowId(ReadRequiredString(json, "workflowId", "runtime surface bind participant to workflow")),
            new ParticipantId(ReadRequiredString(json, "participantId", "runtime surface bind participant to workflow")));
    }

    private static UpdateParticipantRole BuildRuntimeUpdateParticipantRoleRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "runtime surface update participant role");
        return new UpdateParticipantRole(
            new ParticipantId(ReadRequiredString(json, "participantId", "runtime surface update participant role")),
            ReadRequiredString(json, "role", "runtime surface update participant role"));
    }

    private static GetArtifactDetail BuildRuntimeArtifactDetailRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "runtime surface artifact detail");
        return new GetArtifactDetail(new ArtifactId(ReadRequiredString(json, "artifactId", "runtime surface artifact detail")));
    }

    private static ListArtifacts BuildRuntimeArtifactListRequest(object parameters)
    {
        var json = TryGetOptionalJsonObject(parameters, "runtime surface artifact list");
        if (json is null)
        {
            return new ListArtifacts();
        }

        var collaborationSpaceId = Normalize(ReadString(json.Value, "collaborationSpaceId"))
                                   ?? Normalize(ReadString(json.Value, "collaborationId"))
                                   ?? Normalize(ReadString(json.Value, "spaceId"));
        var participantId = Normalize(ReadString(json.Value, "producedByParticipantId"))
                            ?? Normalize(ReadString(json.Value, "produced_by_participant_id"))
                            ?? Normalize(ReadString(json.Value, "participantId"))
                            ?? Normalize(ReadString(json.Value, "participant_id"));
        return new ListArtifacts(
            CollaborationSpaceId: string.IsNullOrWhiteSpace(collaborationSpaceId) ? null : new CollaborationSpaceId(collaborationSpaceId!),
            ProducedByParticipantId: string.IsNullOrWhiteSpace(participantId) ? null : new ParticipantId(participantId!));
    }

    private static ControlPlaneGitDiffArtifactQuery BuildRuntimeGitDiffToRemoteRequest(object parameters)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            return new ControlPlaneGitDiffArtifactQuery();
        }

        var threadId = Normalize(ReadString(json, "threadId"));
        return new ControlPlaneGitDiffArtifactQuery
        {
            ThreadId = string.IsNullOrWhiteSpace(threadId) ? default : new ThreadId(threadId!),
        };
    }

    private static ControlPlaneConfigRequirementsQuery BuildRuntimeConfigRequirementsReadRequest(object parameters)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            return new ControlPlaneConfigRequirementsQuery();
        }

        return new ControlPlaneConfigRequirementsQuery
        {
            WorkingDirectory = Normalize(ReadString(json, "cwd")),
        };
    }

    private static ControlPlaneConfigValueWriteCommand BuildRuntimeConfigValueWriteRequest(object parameters)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            return new ControlPlaneConfigValueWriteCommand();
        }

        StructuredValue? value = null;
        if (json.TryGetProperty("value", out var valueProperty))
        {
            value = ToContractsStructuredValue(valueProperty);
        }

        return new ControlPlaneConfigValueWriteCommand
        {
            KeyPath = Normalize(ReadString(json, "keyPath"))
                      ?? Normalize(ReadString(json, "key"))
                      ?? Normalize(ReadString(json, "path"))
                      ?? string.Empty,
            Value = value,
            MergeStrategy = Normalize(ReadString(json, "mergeStrategy"))
                            ?? Normalize(ReadString(json, "merge_strategy"))
                            ?? "replace",
            WorkingDirectory = Normalize(ReadString(json, "cwd")),
            FilePath = Normalize(ReadString(json, "filePath")),
            ExpectedVersion = Normalize(ReadString(json, "expectedVersion")),
        };
    }

    private static ControlPlaneConfigBatchWriteCommand BuildRuntimeConfigBatchWriteRequest(object parameters)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            return new ControlPlaneConfigBatchWriteCommand();
        }

        var defaultMergeStrategy = Normalize(ReadString(json, "mergeStrategy"))
                                   ?? Normalize(ReadString(json, "merge_strategy"))
                                   ?? "replace";
        var items = BuildRuntimeConfigWriteItems(json, defaultMergeStrategy);
        return new ControlPlaneConfigBatchWriteCommand
        {
            Items = items,
            WorkingDirectory = Normalize(ReadString(json, "cwd")),
            FilePath = Normalize(ReadString(json, "filePath")),
            ExpectedVersion = Normalize(ReadString(json, "expectedVersion")),
            ReloadUserConfig = ReadBoolean(json, "reloadUserConfig") || ReadBoolean(json, "reload_user_config"),
        };
    }

    private static async Task<object> InvokeRuntimeSurfaceAsync(
        IExecutionRuntime runtime,
        string? method,
        object parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var controlPlane = GetControlPlane(runtime);
        var hostGateway = new TianShuHostGateway(controlPlane);
        var normalizedMethod = Normalize(method)?.ToLowerInvariant()
            ?? throw new InvalidOperationException($"无效的 runtime surface method：{method ?? "<null>"}");

        var result = normalizedMethod switch
        {
            "config/read" => BuildConfigReadResponse(
                await controlPlane.Catalog.ReadConfigAsync(BuildRuntimeConfigReadRequest(parameters), cancellationToken).ConfigureAwait(false),
                BuildRuntimeConfigReadRequest(parameters).IncludeLayers),
            "configrequirements/read" => BuildConfigRequirementsResponse(await controlPlane.Catalog.ReadConfigRequirementsAsync(BuildRuntimeConfigRequirementsReadRequest(parameters), cancellationToken).ConfigureAwait(false)),
            "model/list" => await controlPlane.Catalog.ListModelsAsync(BuildRuntimeModelListRequest(parameters), cancellationToken).ConfigureAwait(false),
            "model/catalog/read" => await controlPlane.Catalog.GetCapabilityCatalogAsync(BuildRuntimeCapabilityCatalogRequest(parameters), cancellationToken).ConfigureAwait(false),
            "model/binding/resolve" => await controlPlane.Catalog.ResolveEngineBindingAsync(BuildRuntimeResolveEngineBindingRequest(parameters), cancellationToken).ConfigureAwait(false),
            "skills/list" => BuildSkillsListResponse(await controlPlane.Catalog.ListSkillsAsync(BuildRuntimeSkillsListRequest(parameters), cancellationToken).ConfigureAwait(false)),
            "skills/config/write" => await controlPlane.Catalog.WriteSkillConfigAsync(BuildRuntimeSkillsConfigWriteRequest(parameters), cancellationToken).ConfigureAwait(false),
            "skills/remote/list" => BuildSkillsRemoteListResponse(await controlPlane.Catalog.ListRemoteSkillsAsync(BuildRuntimeSkillsRemoteListRequest(parameters), cancellationToken).ConfigureAwait(false)),
            "skills/remote/export" => await controlPlane.Catalog.ExportRemoteSkillAsync(BuildRuntimeSkillsRemoteExportRequest(parameters), cancellationToken).ConfigureAwait(false),
            "plugin/list" => BuildPluginListResponse(await controlPlane.Catalog.ListPluginsAsync(BuildRuntimePluginListRequest(parameters), cancellationToken).ConfigureAwait(false)),
            "plugin/read" => BuildPluginReadResponse(await controlPlane.Catalog.ReadPluginAsync(BuildRuntimePluginReadRequest(parameters), cancellationToken).ConfigureAwait(false)),
            "plugin/install" => BuildPluginInstallResponse(await controlPlane.Catalog.InstallPluginAsync(BuildRuntimePluginInstallRequest(parameters), cancellationToken).ConfigureAwait(false)),
            "plugin/uninstall" => await controlPlane.Catalog.UninstallPluginAsync(BuildRuntimePluginUninstallRequest(parameters), cancellationToken).ConfigureAwait(false),
            "app/list" => BuildAppListResponse(await controlPlane.Catalog.ListAppsAsync(BuildRuntimeAppListRequest(parameters), cancellationToken).ConfigureAwait(false)),
            "conversation/thread/read" => await controlPlane.Conversations.GetThreadProjectionAsync(BuildRuntimeThreadProjectionRequest(parameters), cancellationToken).ConfigureAwait(false),
            "collaboration/create" => await controlPlane.Collaboration.CreateSpaceAsync(BuildRuntimeCreateCollaborationSpaceRequest(parameters), cancellationToken).ConfigureAwait(false),
            "collaboration/configure" => await controlPlane.Collaboration.ConfigureSpaceAsync(BuildRuntimeConfigureCollaborationSpaceRequest(parameters), cancellationToken).ConfigureAwait(false),
            "collaboration/archive" => await controlPlane.Collaboration.ArchiveSpaceAsync(BuildRuntimeArchiveCollaborationSpaceRequest(parameters), cancellationToken).ConfigureAwait(false),
            "collaboration/overview/read" => await controlPlane.Collaboration.GetSpaceOverviewAsync(BuildRuntimeCollaborationSpaceOverviewRequest(parameters), cancellationToken).ConfigureAwait(false),
            "collaboration/space/read" => await controlPlane.Collaboration.GetSpaceProjectionAsync(BuildRuntimeCollaborationSpaceProjectionRequest(parameters), cancellationToken).ConfigureAwait(false),
            "collaboration/list" => await controlPlane.Collaboration.ListSpacesAsync(BuildRuntimeCollaborationSpaceListRequest(parameters), cancellationToken).ConfigureAwait(false),
            "participant/bindsession" => await controlPlane.Collaboration.BindParticipantToSessionAsync(BuildRuntimeBindParticipantToSessionRequest(parameters), cancellationToken).ConfigureAwait(false),
            "participant/bindworkflow" => await controlPlane.Collaboration.BindParticipantToWorkflowAsync(BuildRuntimeBindParticipantToWorkflowRequest(parameters), cancellationToken).ConfigureAwait(false),
            "participant/updaterole" => await controlPlane.Collaboration.UpdateParticipantRoleAsync(BuildRuntimeUpdateParticipantRoleRequest(parameters), cancellationToken).ConfigureAwait(false),
            "session/snapshot/read" => await controlPlane.Sessions.GetSnapshotAsync(cancellationToken).ConfigureAwait(false),
            "session/overview/read" => await controlPlane.Sessions.GetSessionOverviewAsync(BuildRuntimeSessionOverviewRequest(parameters), cancellationToken).ConfigureAwait(false),
            "session/list" => await controlPlane.Sessions.ListSessionsAsync(BuildRuntimeSessionListRequest(parameters), cancellationToken).ConfigureAwait(false),
            "governance/approvalqueue/read" => await controlPlane.Governance.GetApprovalQueueProjectionAsync(BuildRuntimeApprovalQueueRequest(parameters), cancellationToken).ConfigureAwait(false),
            "governance/userinputs/list" => await controlPlane.Governance.ListUserInputRequestsAsync(BuildRuntimeUserInputListRequest(parameters), cancellationToken).ConfigureAwait(false),
            "participant/read" => await controlPlane.Collaboration.GetParticipantProjectionAsync(BuildRuntimeParticipantProjectionRequest(parameters), cancellationToken).ConfigureAwait(false),
            "participant/view/read" => await controlPlane.Collaboration.GetParticipantViewProjectionAsync(BuildRuntimeParticipantViewProjectionRequest(parameters), cancellationToken).ConfigureAwait(false),
            "participant/list" => await controlPlane.Collaboration.ListParticipantsInScopeAsync(BuildRuntimeParticipantListRequest(parameters), cancellationToken).ConfigureAwait(false),
            "artifact/detail/read" => await controlPlane.Artifacts.GetArtifactProjectionAsync(BuildRuntimeArtifactDetailRequest(parameters), cancellationToken).ConfigureAwait(false),
            "artifact/collection/read" => await controlPlane.Artifacts.GetArtifactCollectionProjectionAsync(BuildRuntimeArtifactListRequest(parameters), cancellationToken).ConfigureAwait(false),
            "workflow/create" => await controlPlane.Workflows.CreateWorkflowAsync(BuildRuntimeCreateWorkflowRequest(parameters), cancellationToken).ConfigureAwait(false),
            "workflow/plan/publish" => await controlPlane.Workflows.PublishPlanAsync(BuildRuntimePublishWorkflowPlanRequest(parameters), cancellationToken).ConfigureAwait(false),
            "workflow/task/create" => await controlPlane.Workflows.CreateTaskAsync(BuildRuntimeCreateWorkflowTaskRequest(parameters), cancellationToken).ConfigureAwait(false),
            "workflow/task/updatestate" => await controlPlane.Workflows.UpdateTaskStateAsync(BuildRuntimeUpdateWorkflowTaskStateRequest(parameters), cancellationToken).ConfigureAwait(false),
            "workflow/board/read" => await controlPlane.Workflows.GetWorkflowBoardProjectionAsync(BuildRuntimeWorkflowBoardRequest(parameters), cancellationToken).ConfigureAwait(false),
            "workflow/taskboard/read" => await controlPlane.Workflows.GetTaskBoardProjectionAsync(BuildRuntimeTaskBoardRequest(parameters), cancellationToken).ConfigureAwait(false),
            "workflow/plan/read" => await controlPlane.Workflows.GetPlanProjectionAsync(BuildRuntimePlanProjectionRequest(parameters), cancellationToken).ConfigureAwait(false),
            "agent/list" => await controlPlane.Agents.ListAgentsAsync(BuildRuntimeAgentListRequest(parameters), cancellationToken).ConfigureAwait(false),
            "agent/roster/read" => await controlPlane.Agents.GetAgentRosterProjectionAsync(BuildRuntimeAgentRosterRequest(parameters), cancellationToken).ConfigureAwait(false),
            "agent/team/read" => await controlPlane.Agents.GetTeamProjectionAsync(BuildRuntimeTeamProjectionRequest(parameters), cancellationToken).ConfigureAwait(false),
            "agent/thread/register" => BuildAgentThreadRegistrationResponse(
                await controlPlane.Agents.RegisterAgentThreadAsync(BuildControlPlaneRegisterAgentThreadCommand(parameters), cancellationToken).ConfigureAwait(false)),
            "agent/job/create" => BuildAgentJobOperationResponse(
                await controlPlane.Workflows.CreateJobAsync(BuildControlPlaneCreateJobCommand(parameters), cancellationToken).ConfigureAwait(false)),
            "agent/job/dispatch" => BuildAgentJobOperationResponse(
                await controlPlane.Workflows.DispatchJobAsync(BuildControlPlaneDispatchJobCommand(parameters), cancellationToken).ConfigureAwait(false)),
            "agent/job/item/report" => BuildAgentJobOperationResponse(
                await controlPlane.Workflows.ReportJobItemAsync(BuildControlPlaneReportJobItemCommand(parameters), cancellationToken).ConfigureAwait(false)),
            "agent/job/read" => BuildAgentJobOperationResponse(
                await controlPlane.Workflows.ReadJobAsync(BuildControlPlaneReadJobQuery(parameters), cancellationToken).ConfigureAwait(false)),
            "identity/account/read" => await controlPlane.Identity.GetAccountProfileAsync(BuildRuntimeAccountProfileRequest(parameters), cancellationToken).ConfigureAwait(false),
            "identity/devices/list" => await controlPlane.Identity.ListBoundDevicesAsync(BuildRuntimeBoundDeviceListRequest(parameters), cancellationToken).ConfigureAwait(false),
            "memory/providers/list" => await controlPlane.Memory.ListMemoryProvidersAsync(BuildRuntimeMemoryProviderListRequest(parameters), cancellationToken).ConfigureAwait(false),
            "memory/spaces/list" => await controlPlane.Memory.ListMemorySpacesAsync(BuildRuntimeMemorySpaceListRequest(parameters), cancellationToken).ConfigureAwait(false),
            "memory/overlay/read" => await controlPlane.Memory.ResolveMemoryOverlayAsync(BuildRuntimeMemoryOverlayRequest(parameters), cancellationToken).ConfigureAwait(false),
            "memory/filter" => await controlPlane.Memory.FilterMemoryAsync(BuildRuntimeFilterMemoryRequest(parameters), cancellationToken).ConfigureAwait(false),
            "memory/review/list" => await controlPlane.Memory.ListMemoryReviewsAsync(BuildRuntimeMemoryReviewListRequest(parameters), cancellationToken).ConfigureAwait(false),
            "memory/add" => await controlPlane.Memory.AddMemoryAsync(BuildRuntimeAddMemoryRequest(parameters), cancellationToken).ConfigureAwait(false),
            "memory/extract" => await controlPlane.Memory.ExtractMemoryAsync(BuildRuntimeExtractMemoryRequest(parameters), cancellationToken).ConfigureAwait(false),
            "memory/import" => await controlPlane.Memory.ImportMemoryAsync(BuildRuntimeImportMemoryRequest(parameters), cancellationToken).ConfigureAwait(false),
            "memory/export" => await controlPlane.Memory.ExportMemoryAsync(BuildRuntimeExportMemoryRequest(parameters), cancellationToken).ConfigureAwait(false),
            "memory/provider/bind" => await controlPlane.Memory.BindMemoryProviderAsync(BuildRuntimeBindMemoryProviderRequest(parameters), cancellationToken).ConfigureAwait(false),
            "memory/consolidation/run" => await controlPlane.Memory.RunMemoryConsolidationAsync(BuildRuntimeRunMemoryConsolidationRequest(parameters), cancellationToken).ConfigureAwait(false),
            "memory/forget" => await controlPlane.Memory.ForgetMemoryAsync(BuildRuntimeForgetMemoryRequest(parameters), cancellationToken).ConfigureAwait(false),
            "memory/delete" => await controlPlane.Memory.DeleteMemoryAsync(BuildRuntimeDeleteMemoryRequest(parameters), cancellationToken).ConfigureAwait(false),
            "memory/supersede" => await controlPlane.Memory.SupersedeMemoryAsync(BuildRuntimeSupersedeMemoryRequest(parameters), cancellationToken).ConfigureAwait(false),
            "memory/review/approve" => await controlPlane.Memory.ApproveMemoryReviewAsync(BuildRuntimeApproveMemoryReviewRequest(parameters), cancellationToken).ConfigureAwait(false),
            "memory/review/demote" => await controlPlane.Memory.DemoteMemoryReviewAsync(BuildRuntimeDemoteMemoryReviewRequest(parameters), cancellationToken).ConfigureAwait(false),
            "memory/review/merge" => await controlPlane.Memory.MergeMemoryReviewAsync(BuildRuntimeMergeMemoryReviewRequest(parameters), cancellationToken).ConfigureAwait(false),
            "memory/review/restore" => await controlPlane.Memory.RestoreMemoryReviewAsync(BuildRuntimeRestoreMemoryReviewRequest(parameters), cancellationToken).ConfigureAwait(false),
            "memory/feedback/record" => await controlPlane.Memory.RecordMemoryFeedbackAsync(BuildRuntimeRecordMemoryFeedbackRequest(parameters), cancellationToken).ConfigureAwait(false),
            "memory/citation/record" => await controlPlane.Memory.RecordMemoryCitationAsync(BuildRuntimeRecordMemoryCitationRequest(parameters), cancellationToken).ConfigureAwait(false),
            "diagnostics/trace/read" => await controlPlane.Diagnostics.GetExecutionTraceAsync(BuildRuntimeExecutionTraceRequest(parameters), cancellationToken).ConfigureAwait(false),
            "diagnostics/attempts/list" => await controlPlane.Diagnostics.ListAttemptSummariesAsync(BuildRuntimeAttemptSummaryListRequest(parameters), cancellationToken).ConfigureAwait(false),
            "tianshu/debug/clear-memories" => await controlPlane.Diagnostics.ClearDebugMemoriesAsync(cancellationToken).ConfigureAwait(false),
            "feedback/upload" => await controlPlane.Diagnostics.UploadFeedbackAsync(BuildRuntimeFeedbackUploadRequest(parameters), cancellationToken).ConfigureAwait(false),
            "exec_wait" => BuildCodeModeResultPayload(
                await runtime.AsNorthboundSurface()
                    .Execution
                    .WaitCodeModeAsync(
                        BuildCodeModeWaitRequest(
                            parameters,
                            (await GetSessionSnapshotAsync(runtime, cancellationToken).ConfigureAwait(false)).ActiveThreadId?.Value),
                        cancellationToken)
                    .ConfigureAwait(false)),
            "review/start" => await controlPlane.Workflows.StartReviewAsync(BuildRuntimeReviewStartRequest(parameters), cancellationToken).ConfigureAwait(false),
            "config/value/write" => BuildConfigWriteResponse(await controlPlane.Catalog.WriteConfigValueAsync(BuildRuntimeConfigValueWriteRequest(parameters), cancellationToken).ConfigureAwait(false)),
            "config/batchwrite" => BuildConfigWriteResponse(await controlPlane.Catalog.WriteConfigBatchAsync(BuildRuntimeConfigBatchWriteRequest(parameters), cancellationToken).ConfigureAwait(false)),
            "experimentalfeature/list" => BuildExperimentalFeatureListResponse(await controlPlane.Catalog.ListExperimentalFeaturesAsync(BuildRuntimeExperimentalFeatureListRequest(parameters), cancellationToken).ConfigureAwait(false)),
            "collaborationmode/list" => BuildCollaborationModeListResponse(await controlPlane.Catalog.ListCollaborationModesAsync(cancellationToken).ConfigureAwait(false)),
            "mcpserverstatus/list" => BuildMcpServerStatusListResponse(await controlPlane.Catalog.ListMcpServerStatusAsync(BuildRuntimeMcpServerStatusListRequest(parameters), cancellationToken).ConfigureAwait(false)),
            "config/mcpserver/reload" => await controlPlane.Catalog.ReloadMcpServersAsync(cancellationToken).ConfigureAwait(false),
            "mcpserver/oauth/login" => await controlPlane.Catalog.StartMcpServerOauthLoginAsync(BuildRuntimeMcpServerOauthLoginRequest(parameters), cancellationToken).ConfigureAwait(false),
            "artifact/conversationsummary/read" => BuildConversationSummaryResponse(
                (await hostGateway.GetConversationSummaryAsync(
                    new HostReadConversationSummaryQuery
                    {
                        ThreadId = BuildRuntimeConversationSummaryRequest(parameters).ThreadId,
                        RolloutPath = BuildRuntimeConversationSummaryRequest(parameters).RolloutPath,
                    },
                    cancellationToken).ConfigureAwait(false)).Artifact),
            "artifact/gitdifftoremote/read" => (await hostGateway.GetGitDiffToRemoteAsync(
                new HostReadGitDiffToRemoteQuery
                {
                    ThreadId = BuildRuntimeGitDiffToRemoteRequest(parameters).ThreadId,
                },
                cancellationToken).ConfigureAwait(false)).Artifact,
            _ => throw new InvalidOperationException($"无效的 runtime surface method：{method ?? "<null>"}"),
        };

        return result!;
    }

    private async Task<object> InvokeThreadOperationAsync(
        IExecutionRuntime runtime,
        string? method,
        object parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var hostGateway = new TianShuHostGateway(GetControlPlane(runtime));

        switch (Normalize(method)?.ToLowerInvariant())
        {
            case "thread/loaded/list":
                var loadedListRequest = BuildThreadLoadedListRequest(parameters);
                return BuildLoadedThreadListResponse(
                    await hostGateway.ListLoadedThreadsAsync(
                        new HostListLoadedThreadsQuery
                        {
                            Limit = loadedListRequest.Limit,
                            Cursor = loadedListRequest.Cursor,
                        },
                        cancellationToken).ConfigureAwait(false));
            case "thread/compact/start":
                var compactRequest = BuildThreadCompactRequest(parameters);
                await hostGateway.CompactThreadAsync(
                    new HostCompactThread
                    {
                        ThreadId = compactRequest.ThreadId,
                        KeepRecentTurns = compactRequest.KeepRecentTurns,
                    },
                    cancellationToken).ConfigureAwait(false);
                return new ControlPlaneThreadCommandAcceptedResult();
            case "thread/backgroundterminals/clean":
                await hostGateway.CleanBackgroundTerminalsAsync(
                    new HostCleanBackgroundTerminals
                    {
                        ThreadId = new ThreadId(ReadRequiredThreadId(parameters, "thread operation BackgroundTerminalsClean")),
                    },
                    cancellationToken).ConfigureAwait(false);
                return new ControlPlaneThreadCommandAcceptedResult();
            case "thread/unsubscribe":
                return BuildThreadUnsubscribeResponse(
                    await hostGateway.UnsubscribeThreadAsync(
                            new HostUnsubscribeThread
                            {
                                ThreadId = new ThreadId(ReadRequiredThreadId(parameters, "thread operation Unsubscribe")),
                            },
                            cancellationToken)
                        .ConfigureAwait(false));
            case "thread/increment_elicitation":
                return BuildThreadElicitationResponse(
                    await GetControlPlane(runtime).Conversations.IncrementThreadElicitationAsync(
                            new ControlPlaneIncrementThreadElicitationCommand
                            {
                                ThreadId = new ThreadId(ReadRequiredThreadId(parameters, "thread operation IncrementElicitation")),
                            },
                            cancellationToken)
                        .ConfigureAwait(false));
            case "thread/decrement_elicitation":
                return BuildThreadElicitationResponse(
                    await GetControlPlane(runtime).Conversations.DecrementThreadElicitationAsync(
                            new ControlPlaneDecrementThreadElicitationCommand
                            {
                                ThreadId = new ThreadId(ReadRequiredThreadId(parameters, "thread operation DecrementElicitation")),
                            },
                            cancellationToken)
                        .ConfigureAwait(false));
            case "thread/read":
                return BuildThreadOperationResponse(
                    await hostGateway.ReadThreadAsync(
                        new HostReadThreadQuery
                        {
                            ThreadId = BuildThreadReadRequest(parameters).ThreadId,
                            IncludeTurns = BuildThreadReadRequest(parameters).IncludeTurns,
                        },
                        cancellationToken).ConfigureAwait(false));
            case "thread/delete":
                return await InvokeThreadDeleteOperationAsync(runtime, parameters, cancellationToken).ConfigureAwait(false);
            case "thread/unarchive":
                return BuildThreadOperationResponse(
                    await hostGateway.UnarchiveThreadAsync(
                            new HostUnarchiveThread
                            {
                                ThreadId = new ThreadId(ReadRequiredThreadId(parameters, "thread operation Unarchive")),
                            },
                            cancellationToken)
                        .ConfigureAwait(false));
            case "thread/metadata/update":
                return BuildThreadOperationResponse(
                    await hostGateway.UpdateThreadMetadataAsync(
                        new HostUpdateThreadMetadata
                        {
                            ThreadId = BuildThreadMetadataUpdateRequest(parameters).ThreadId,
                            HasGitSha = BuildThreadMetadataUpdateRequest(parameters).HasGitSha,
                            GitSha = BuildThreadMetadataUpdateRequest(parameters).GitSha,
                            HasGitBranch = BuildThreadMetadataUpdateRequest(parameters).HasGitBranch,
                            GitBranch = BuildThreadMetadataUpdateRequest(parameters).GitBranch,
                            HasGitOriginUrl = BuildThreadMetadataUpdateRequest(parameters).HasGitOriginUrl,
                            GitOriginUrl = BuildThreadMetadataUpdateRequest(parameters).GitOriginUrl,
                        },
                        cancellationToken).ConfigureAwait(false));
            case "thread/rollback":
                return BuildThreadOperationResponse(
                    await hostGateway.RollbackThreadAsync(
                        new HostRollbackThread
                        {
                            ThreadId = BuildThreadRollbackRequest(parameters).ThreadId,
                            NumTurns = BuildThreadRollbackRequest(parameters).NumTurns,
                        },
                        cancellationToken).ConfigureAwait(false));
            default:
                throw new InvalidOperationException($"无效的 thread operation method：{method ?? "<null>"}");
        }
    }

    private static async Task<object> InvokeAgentOperationAsync(
        IExecutionRuntime runtime,
        string? method,
        object parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var controlPlane = GetControlPlane(runtime).Agents;
        var workflowPlane = GetControlPlane(runtime).Workflows;

        return Normalize(method)?.ToLowerInvariant() switch
        {
            "agent/thread/register" => BuildAgentThreadRegistrationResponse(
                await controlPlane.RegisterAgentThreadAsync(BuildControlPlaneRegisterAgentThreadCommand(parameters), cancellationToken).ConfigureAwait(false)),
            "agent/job/create" => BuildAgentJobOperationResponse(
                await workflowPlane.CreateJobAsync(BuildControlPlaneCreateJobCommand(parameters), cancellationToken).ConfigureAwait(false)),
            "agent/job/dispatch" => BuildAgentJobOperationResponse(
                await workflowPlane.DispatchJobAsync(BuildControlPlaneDispatchJobCommand(parameters), cancellationToken).ConfigureAwait(false)),
            "agent/job/item/report" => BuildAgentJobOperationResponse(
                await workflowPlane.ReportJobItemAsync(BuildControlPlaneReportJobItemCommand(parameters), cancellationToken).ConfigureAwait(false)),
            "agent/job/read" => BuildAgentJobOperationResponse(
                await workflowPlane.ReadJobAsync(BuildControlPlaneReadJobQuery(parameters), cancellationToken).ConfigureAwait(false)),
            _ => throw new InvalidOperationException($"无效的 agent operation method：{method ?? "<null>"}"),
        };
    }

    private static Task<ControlPlaneWindowsSandboxSetupStartResult> InvokeWindowsSandboxSetupAsync(
        IEnvironmentRuntimeSurface environment,
        object parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return environment.StartWindowsSandboxSetupAsync(BuildWindowsSandboxSetupRequest(parameters), cancellationToken);
    }

    private static Task<object> InvokeCommandExecutionAsync(
        IExecutionRuntimeSurface execution,
        string? method,
        object parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);

        return Normalize(method)?.ToLowerInvariant() switch
        {
            "exec" => InvokeCommandExecutionStartAsync(execution, parameters, cancellationToken),
            "write" => InvokeCommandExecutionWriteAsync(execution, parameters, cancellationToken),
            "terminate" => InvokeCommandExecutionTerminateAsync(execution, parameters, cancellationToken),
            "resize" => InvokeCommandExecutionResizeAsync(execution, parameters, cancellationToken),
            _ => throw new InvalidOperationException($"无效的 command execution method：{method ?? "<null>"}"),
        };
    }

    private static async Task<object> InvokeCommandExecutionStartAsync(
        IExecutionRuntimeSurface execution,
        object parameters,
        CancellationToken cancellationToken)
        => await execution.StartCommandExecutionAsync(BuildCommandExecutionStartRequest(parameters), cancellationToken).ConfigureAwait(false);

    private static async Task<object> InvokeCommandExecutionWriteAsync(
        IExecutionRuntimeSurface execution,
        object parameters,
        CancellationToken cancellationToken)
        => await execution.WriteCommandExecutionAsync(BuildCommandExecutionWriteRequest(parameters), cancellationToken).ConfigureAwait(false);

    private static async Task<object> InvokeCommandExecutionTerminateAsync(
        IExecutionRuntimeSurface execution,
        object parameters,
        CancellationToken cancellationToken)
        => await execution.TerminateCommandExecutionAsync(BuildCommandExecutionTerminateRequest(parameters), cancellationToken).ConfigureAwait(false);

    private static async Task<object> InvokeCommandExecutionResizeAsync(
        IExecutionRuntimeSurface execution,
        object parameters,
        CancellationToken cancellationToken)
        => await execution.ResizeCommandExecutionAsync(BuildCommandExecutionResizeRequest(parameters), cancellationToken).ConfigureAwait(false);

    private static ControlPlaneCommandExecutionStartCommand BuildCommandExecutionStartRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "command execution exec");
        if (!TryGetProperty(json, "command", out var commandProperty))
        {
            throw new InvalidOperationException("command execution exec 缺少 command。");
        }

        string? commandText = null;
        IReadOnlyList<string> commandArgs = Array.Empty<string>();
        switch (commandProperty.ValueKind)
        {
            case JsonValueKind.String:
                commandText = commandProperty.GetString();
                break;
            case JsonValueKind.Array:
                commandArgs = ReadStringArray(commandProperty);
                break;
            default:
                throw new InvalidOperationException("command execution exec 的 command 必须是字符串或字符串数组。");
        }

        return new ControlPlaneCommandExecutionStartCommand
        {
            WorkingDirectory = Normalize(ReadString(json, "cwd")),
            CommandText = Normalize(commandText),
            CommandArgs = commandArgs,
            ProcessId = Normalize(ReadString(json, "processId")),
            Tty = ReadBoolean(json, "tty"),
            Size = ReadOptionalCommandExecutionTerminalSize(json, "size"),
            StreamStdin = ReadBoolean(json, "streamStdin"),
            StreamStdoutStderr = ReadBoolean(json, "streamStdoutStderr"),
            Background = ReadBoolean(json, "background"),
            DisableTimeout = ReadBoolean(json, "disableTimeout"),
            TimeoutMs = ReadInt32(json, "timeoutMs"),
            DisableOutputCap = ReadBoolean(json, "disableOutputCap"),
            OutputBytesCap = ReadInt32(json, "outputBytesCap"),
            ThreadId = Normalize(ReadString(json, "threadId")) is { } threadId ? new ThreadId(threadId) : null,
            TurnId = Normalize(ReadString(json, "turnId")) is { } turnId ? new TurnId(turnId) : null,
            ItemId = Normalize(ReadString(json, "itemId")),
            ApprovalPolicy = Normalize(ReadString(json, "approvalPolicy")),
            Approved = ReadBoolean(json, "approved"),
            Login = TryGetProperty(json, "login", out var login) && login.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? login.GetBoolean()
                : null,
            EnvironmentVariables = ReadOptionalStringOrNullDictionary(json, "env"),
            Sandbox = ReadOptionalStructuredValue(json, "sandbox"),
        };
    }

    private static ControlPlaneCommandExecutionWriteCommand BuildCommandExecutionWriteRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "command execution write");
        var processId = Normalize(ReadString(json, "processId"));
        if (string.IsNullOrWhiteSpace(processId))
        {
            throw new InvalidOperationException("command execution write 缺少 processId。");
        }

        return new ControlPlaneCommandExecutionWriteCommand
        {
            ProcessId = processId!,
            DeltaBase64 = Normalize(ReadString(json, "deltaBase64")),
            CloseStdin = ReadBoolean(json, "closeStdin"),
        };
    }

    private static ControlPlaneCommandExecutionTerminateCommand BuildCommandExecutionTerminateRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "command execution terminate");
        var processId = Normalize(ReadString(json, "processId"));
        if (string.IsNullOrWhiteSpace(processId))
        {
            throw new InvalidOperationException("command execution terminate 缺少 processId。");
        }

        return new ControlPlaneCommandExecutionTerminateCommand
        {
            ProcessId = processId!,
        };
    }

    private static ControlPlaneCommandExecutionResizeCommand BuildCommandExecutionResizeRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "command execution resize");
        var processId = Normalize(ReadString(json, "processId"));
        if (string.IsNullOrWhiteSpace(processId))
        {
            throw new InvalidOperationException("command execution resize 缺少 processId。");
        }

        return new ControlPlaneCommandExecutionResizeCommand
        {
            ProcessId = processId!,
            Size = ReadRequiredCommandExecutionTerminalSize(json, "size"),
        };
    }

    private static ControlPlaneWindowsSandboxSetupStartCommand BuildWindowsSandboxSetupRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "windows sandbox setup");
        var mode = Normalize(ReadString(json, "mode"));
        if (string.IsNullOrWhiteSpace(mode))
        {
            throw new InvalidOperationException("windows sandbox setup 缺少 mode。");
        }

        return new ControlPlaneWindowsSandboxSetupStartCommand
        {
            Mode = mode.Equals("elevated", StringComparison.OrdinalIgnoreCase)
                ? WindowsSandboxSetupMode.Elevated
                : mode.Equals("unelevated", StringComparison.OrdinalIgnoreCase)
                    ? WindowsSandboxSetupMode.Unelevated
                    : throw new InvalidOperationException($"不支持的 windows sandbox mode：{mode}"),
            WorkingDirectory = Normalize(ReadString(json, "cwd")),
        };
    }

    private static async Task<object> InvokeThreadDeleteOperationAsync(IExecutionRuntime runtime, object parameters, CancellationToken cancellationToken)
    {
        var threadId = ReadRequiredThreadId(parameters, "thread operation Delete");
        var gateway = new TianShuHostGateway(GetControlPlane(runtime));
        var deleted = await gateway.DeleteThreadAsync(
                new HostDeleteThread
                {
                    ThreadId = new ThreadId(threadId),
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (!deleted.Accepted)
        {
            throw new InvalidOperationException($"未能删除线程：{threadId}");
        }

        var sessionSnapshot = await GetSessionSnapshotAsync(runtime, cancellationToken).ConfigureAwait(false);

        return new
        {
            threadId,
            activeThreadId = sessionSnapshot.ActiveThreadId?.Value,
        };
    }

    private static ControlPlaneRegisterAgentThreadCommand BuildControlPlaneRegisterAgentThreadCommand(object parameters)
    {
        var json = RequireJsonObject(parameters, "agent thread register");
        return new ControlPlaneRegisterAgentThreadCommand
        {
            ThreadId = new ThreadId(ReadRequiredThreadId(json, "agent thread register")),
            AgentNickname = Normalize(ReadString(json, "agentNickname")),
            AgentRole = Normalize(ReadString(json, "agentRole")),
        };
    }

    private static ControlPlaneCreateJobCommand BuildControlPlaneCreateJobCommand(object parameters)
    {
        var json = RequireJsonObject(parameters, "agent job create");
        var instruction = Normalize(ReadString(json, "instruction"));
        if (string.IsNullOrWhiteSpace(instruction))
        {
            throw new InvalidOperationException("agent job create 缺少 instruction。");
        }

        return new ControlPlaneCreateJobCommand
        {
            JobId = string.IsNullOrWhiteSpace(Normalize(ReadString(json, "jobId")))
                ? null
                : new JobId(Normalize(ReadString(json, "jobId"))!),
            Name = Normalize(ReadString(json, "name")),
            Instruction = instruction!,
            InputHeaders = ReadOptionalStructuredValue(json, "inputHeaders"),
            InputCsvPath = Normalize(ReadString(json, "inputCsvPath")),
            OutputCsvPath = Normalize(ReadString(json, "outputCsvPath")),
            AutoExport = TryGetProperty(json, "autoExport", out var autoExport) && autoExport.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? autoExport.GetBoolean()
                : null,
            OutputSchema = ReadOptionalStructuredValue(json, "outputSchema"),
            Items = ReadOptionalStructuredArray(json, "items")
                ?? Array.Empty<StructuredValue>(),
        };
    }

    private static ControlPlaneDispatchJobCommand BuildControlPlaneDispatchJobCommand(object parameters)
    {
        var json = RequireJsonObject(parameters, "agent job dispatch");
        var jobId = Normalize(ReadString(json, "jobId"));
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new InvalidOperationException("agent job dispatch 缺少 jobId。");
        }

        var threadIds = ReadStringArray(json, "threadIds");
        if (threadIds.Count == 0)
        {
            throw new InvalidOperationException("agent job dispatch 缺少 threadIds。");
        }

        return new ControlPlaneDispatchJobCommand
        {
            JobId = new JobId(jobId!),
            ThreadIds = threadIds
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(static threadId => new ThreadId(threadId))
                .ToArray(),
        };
    }

    private static ControlPlaneReportJobItemCommand BuildControlPlaneReportJobItemCommand(object parameters)
    {
        var json = RequireJsonObject(parameters, "agent job item report");
        var jobId = Normalize(ReadString(json, "jobId"));
        var itemId = Normalize(ReadString(json, "itemId"));
        var status = Normalize(ReadString(json, "status"));
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new InvalidOperationException("agent job item report 缺少 jobId。");
        }

        if (string.IsNullOrWhiteSpace(itemId))
        {
            throw new InvalidOperationException("agent job item report 缺少 itemId。");
        }

        if (string.IsNullOrWhiteSpace(status))
        {
            throw new InvalidOperationException("agent job item report 缺少 status。");
        }

        return new ControlPlaneReportJobItemCommand
        {
            JobId = new JobId(jobId!),
            ItemId = new JobItemId(itemId!),
            Status = status!,
            Result = ReadOptionalStructuredValue(json, "result"),
            LastError = Normalize(ReadString(json, "lastError")),
        };
    }

    private static ControlPlaneReadJobQuery BuildControlPlaneReadJobQuery(object parameters)
    {
        var json = RequireJsonObject(parameters, "agent job read");
        var jobId = Normalize(ReadString(json, "jobId"));
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new InvalidOperationException("agent job read 缺少 jobId。");
        }

        return new ControlPlaneReadJobQuery
        {
            JobId = new JobId(jobId!),
        };
    }

    private static object BuildAgentJobOperationResponse(ControlPlaneJobOperationResult result)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["job"] = result.Job is null ? null : BuildAgentJobResponse(result.Job),
            ["items"] = result.Items.Select(BuildAgentJobItemResponse).ToArray(),
            ["item"] = result.Item is null ? null : BuildAgentJobItemResponse(result.Item),
        };

    private static object BuildAgentThreadRegistrationResponse(ControlPlaneAgentThreadRegistrationResult result)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["thread"] = result.Agent is null ? null : BuildAgentThreadRegistrationThreadResponse(result.Agent),
        };

    private static object BuildAgentThreadRegistrationThreadResponse(ControlPlaneAgentDescriptor agent)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = agent.ThreadId.Value,
            ["preview"] = agent.Preview,
            ["name"] = agent.Name,
            ["cwd"] = agent.WorkingDirectory,
            ["path"] = agent.Path,
            ["source"] = agent.Source,
            ["agentNickname"] = agent.AgentNickname,
            ["agentRole"] = agent.AgentRole,
            ["createdAt"] = agent.CreatedAt?.ToUnixTimeSeconds(),
            ["updatedAt"] = agent.UpdatedAt == default ? null : agent.UpdatedAt.ToUnixTimeSeconds(),
            ["ephemeral"] = agent.IsEphemeral,
            ["status"] = string.IsNullOrWhiteSpace(agent.Status)
                ? null
                : new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["type"] = agent.Status,
                    ["activeFlags"] = agent.ActiveFlags.ToArray(),
                },
            ["turns"] = Array.Empty<object>(),
        };

    private static object BuildAgentJobResponse(ControlPlaneJobDetails job)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = job.Id.Value,
            ["name"] = job.Name,
            ["status"] = job.Status,
            ["instruction"] = job.Instruction,
        };

    private static object BuildAgentJobItemResponse(ControlPlaneJobItemDetails item)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["itemId"] = item.ItemId.Value,
            ["sourceId"] = item.SourceId,
            ["threadId"] = item.ThreadId?.Value,
            ["assignedThreadId"] = item.AssignedThreadId?.Value,
            ["status"] = item.Status,
            ["lastError"] = item.LastError,
            ["result"] = item.Result?.ToPlainObject(),
        };

    private static ControlPlaneLoadedThreadListQuery BuildThreadLoadedListRequest(object parameters)
    {
        var json = TryGetOptionalJsonObject(parameters, "thread operation LoadedList");
        return new ControlPlaneLoadedThreadListQuery
        {
            Limit = json.HasValue ? ReadInt32(json.Value, "limit") : null,
            Cursor = json.HasValue ? Normalize(ReadString(json.Value, "cursor")) : null,
        };
    }

    private static ControlPlaneReadThreadQuery BuildThreadReadRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "thread operation Read");
        return new ControlPlaneReadThreadQuery
        {
            ThreadId = new ThreadId(ReadRequiredThreadId(json, "thread operation Read")),
            IncludeTurns = ReadBoolean(json, "includeTurns"),
        };
    }

    private static ControlPlaneCompactThreadCommand BuildThreadCompactRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "thread operation CompactStart");
        return new ControlPlaneCompactThreadCommand
        {
            ThreadId = new ThreadId(ReadRequiredThreadId(json, "thread operation CompactStart")),
            KeepRecentTurns = ReadRequiredInt32(json, "keepRecentTurns", "thread operation CompactStart"),
        };
    }

    private static ControlPlaneUpdateThreadMetadataCommand BuildThreadMetadataUpdateRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "thread operation MetadataUpdate");
        var threadId = ReadRequiredThreadId(json, "thread operation MetadataUpdate");
        var gitInfo = json.TryGetProperty("gitInfo", out var gitInfoElement) && gitInfoElement.ValueKind == JsonValueKind.Object
            ? gitInfoElement
            : default;
        var hasGitInfo = gitInfo.ValueKind == JsonValueKind.Object;
        var hasGitSha = hasGitInfo && gitInfo.TryGetProperty("sha", out _);
        var hasGitBranch = hasGitInfo && gitInfo.TryGetProperty("branch", out _);
        var hasGitOriginUrl = hasGitInfo && gitInfo.TryGetProperty("originUrl", out _);

        return new ControlPlaneUpdateThreadMetadataCommand
        {
            ThreadId = new ThreadId(threadId),
            HasGitSha = hasGitSha,
            GitSha = hasGitSha ? Normalize(ReadString(gitInfo, "sha")) : null,
            HasGitBranch = hasGitBranch,
            GitBranch = hasGitBranch ? Normalize(ReadString(gitInfo, "branch")) : null,
            HasGitOriginUrl = hasGitOriginUrl,
            GitOriginUrl = hasGitOriginUrl ? Normalize(ReadString(gitInfo, "originUrl")) : null,
        };
    }

    private static ControlPlaneRollbackThreadCommand BuildThreadRollbackRequest(object parameters)
    {
        var json = RequireJsonObject(parameters, "thread operation Rollback");
        return new ControlPlaneRollbackThreadCommand
        {
            ThreadId = new ThreadId(ReadRequiredThreadId(json, "thread operation Rollback")),
            NumTurns = ReadRequiredInt32(json, "numTurns", "thread operation Rollback"),
        };
    }

    private static string ReadRequiredThreadId(object parameters, string operationName)
        => ReadRequiredThreadId(RequireJsonObject(parameters, operationName), operationName);

    private static string ReadRequiredThreadId(JsonElement json, string operationName)
    {
        var threadId = Normalize(ReadString(json, "threadId"));
        if (string.IsNullOrWhiteSpace(threadId))
        {
            throw new InvalidOperationException($"{operationName} 缺少 threadId。");
        }

        return threadId!;
    }

    private static int ReadRequiredInt32(JsonElement json, string propertyName, string operationName)
    {
        var value = ReadInt32(json, propertyName);
        if (value is null)
        {
            throw new InvalidOperationException($"{operationName} 缺少 {propertyName}。");
        }

        return value.Value;
    }

    private static string ReadRequiredString(JsonElement json, string propertyName, string operationName)
    {
        var value = Normalize(ReadString(json, propertyName));
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{operationName} 缺少 {propertyName}。");
        }

        return value!;
    }

    private static string RequireDirectPayloadValue(string? value, string propertyName, string operationName)
    {
        var normalized = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"{operationName} 缺少 {propertyName}。");
        }

        return normalized!;
    }

    private static ParticipantRef? BuildOptionalWorkflowOwner(string? participantId)
    {
        var normalizedParticipantId = Normalize(participantId);
        return string.IsNullOrWhiteSpace(normalizedParticipantId)
            ? null
            : new ParticipantRef(
                new ParticipantId(normalizedParticipantId!),
                ParticipantKind.Agent,
                normalizedParticipantId!);
    }

    private static IReadOnlyList<PlanStep> BuildDirectWorkflowPlanSteps(IReadOnlyList<WorkflowPlanStepPayload>? steps, string operationName)
    {
        if (steps is null || steps.Count == 0)
        {
            return Array.Empty<PlanStep>();
        }

        return steps
            .Select(
                (step, index) => new PlanStep(
                    step.Order ?? index,
                    RequireDirectPayloadValue(step.Title, nameof(step.Title), $"{operationName} step"),
                    Normalize(step.Description)))
            .ToArray();
    }

    private static IReadOnlyList<PlanStep> BuildRuntimeWorkflowPlanSteps(JsonElement json, string operationName)
    {
        if (!json.TryGetProperty("steps", out var stepsElement) || stepsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<PlanStep>();
        }

        var steps = new List<PlanStep>();
        var index = 0;
        foreach (var stepElement in stepsElement.EnumerateArray())
        {
            if (stepElement.ValueKind != JsonValueKind.Object)
            {
                index++;
                continue;
            }

            steps.Add(
                new PlanStep(
                    ReadInt32(stepElement, "order") ?? index,
                    ReadRequiredString(stepElement, "title", operationName),
                    Normalize(ReadString(stepElement, "description"))));
            index++;
        }

        return steps;
    }

    private static TaskState ParseWorkflowTaskState(string? value, string operationName)
        => Normalize(value)?.Trim().ToLowerInvariant() switch
        {
            "todo" => TaskState.Todo,
            "in-progress" => TaskState.InProgress,
            "inprogress" => TaskState.InProgress,
            "blocked" => TaskState.Blocked,
            "done" => TaskState.Done,
            "cancelled" => TaskState.Cancelled,
            "canceled" => TaskState.Cancelled,
            _ => throw new InvalidOperationException($"{operationName} 的 state 只支持 todo、in-progress、blocked、done、cancelled。"),
        };

    private static bool HasDirectCollaborationDefaults(string? defaultWorkspace, string? defaultExecutionProfile)
        => !string.IsNullOrWhiteSpace(Normalize(defaultWorkspace))
           || !string.IsNullOrWhiteSpace(Normalize(defaultExecutionProfile));

    private static bool HasRuntimeCollaborationDefaults(JsonElement json)
        => !string.IsNullOrWhiteSpace(Normalize(ReadString(json, "defaultWorkspace")))
           || !string.IsNullOrWhiteSpace(Normalize(ReadString(json, "defaultExecutionProfile")));

    private static string ResolveSpaceIdPropertyName(JsonElement json)
    {
        if (json.TryGetProperty("spaceId", out _))
        {
            return "spaceId";
        }

        if (json.TryGetProperty("collaborationSpaceId", out _))
        {
            return "collaborationSpaceId";
        }

        if (json.TryGetProperty("collaborationId", out _))
        {
            return "collaborationId";
        }

        return "spaceId";
    }

    private static MemoryScopeKind ParseMemoryScopeKind(string rawScopeKind)
    {
        if (Enum.TryParse<MemoryScopeKind>(rawScopeKind, ignoreCase: true, out var scopeKind))
        {
            return scopeKind;
        }

        throw new InvalidOperationException("runtime surface memory space list 的 scopeKind 必须是 user/workspace/team/session/agent/collaboration 之一。");
    }

    private static T ReadTypedPayload<T>(object parameters, string operationName)
        where T : class
    {
        var json = RequireJsonObject(parameters, operationName);
        var value = JsonSerializer.Deserialize<T>(json.GetRawText(), TypedPayloadJsonOptions);
        return value ?? throw new InvalidOperationException($"{operationName} 的 parametersJson 无法反序列化为 {typeof(T).Name}。");
    }

    private static JsonSerializerOptions CreateTypedPayloadJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new MemorySpaceIdJsonConverter());
        options.Converters.Add(new MemoryRecordIdJsonConverter());
        return options;
    }

    private sealed class MemorySpaceIdJsonConverter : JsonConverter<MemorySpaceId>
    {
        public override MemorySpaceId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(ReadIdentifierValue(ref reader, "memorySpaceId"));

        public override void Write(Utf8JsonWriter writer, MemorySpaceId value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }

    private sealed class MemoryRecordIdJsonConverter : JsonConverter<MemoryRecordId>
    {
        public override MemoryRecordId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(ReadIdentifierValue(ref reader, "memoryRecordId"));

        public override void Write(Utf8JsonWriter writer, MemoryRecordId value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }

    private static string ReadIdentifierValue(ref Utf8JsonReader reader, string subject)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString() ?? throw new JsonException($"{subject} 不能为空。");
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"{subject} 必须是字符串或包含 value 的对象。");
        }

        using var document = JsonDocument.ParseValue(ref reader);
        if (!document.RootElement.TryGetProperty("value", out var valueElement) || valueElement.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"{subject} 对象必须包含字符串 value 字段。");
        }

        return valueElement.GetString() ?? throw new JsonException($"{subject}.value 不能为空。");
    }

    private static JsonElement RequireJsonObject(object parameters, string operationName)
        => TryGetOptionalJsonObject(parameters, operationName)
           ?? throw new InvalidOperationException($"{operationName} 的 parametersJson 必须是 JSON 对象。");

    private static JsonElement? TryGetOptionalJsonObject(object parameters, string operationName)
    {
        if (parameters is JsonElement json && json.ValueKind == JsonValueKind.Object)
        {
            return json;
        }

        if (parameters is IDictionary<string, object?> dictionary && dictionary.Count == 0)
        {
            return null;
        }

        throw new InvalidOperationException($"{operationName} 的 parametersJson 必须是 JSON 对象。");
    }

    private static ControlPlaneFuzzyFileSearchQuery BuildFuzzyFileSearchSearchRequest(object parameters)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("fuzzy file search search 的 parametersJson 必须是 JSON 对象。");
        }

        var query = Normalize(ReadString(json, "query"));
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new InvalidOperationException("fuzzy file search search 缺少 query。");
        }

        return new ControlPlaneFuzzyFileSearchQuery
        {
            Query = query!,
            WorkingDirectory = Normalize(ReadString(json, "cwd")),
            Limit = ReadInt32(json, "limit"),
            Roots = ReadStringArray(json, "roots"),
        };
    }

    private static ControlPlaneStartFuzzyFileSearchSessionCommand BuildFuzzyFileSearchSessionStartRequest(object parameters)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("fuzzy file search sessionStart 的 parametersJson 必须是 JSON 对象。");
        }

        var sessionId = Normalize(ReadString(json, "sessionId"));
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("fuzzy file search sessionStart 缺少 sessionId。");
        }

        var roots = ReadStringArray(json, "roots");
        if (roots.Count == 0)
        {
            var cwd = Normalize(ReadString(json, "cwd"));
            if (!string.IsNullOrWhiteSpace(cwd))
            {
                roots = [cwd!];
            }
        }

        return new ControlPlaneStartFuzzyFileSearchSessionCommand
        {
            SessionId = sessionId!,
            Roots = roots,
        };
    }

    private static ControlPlaneUpdateFuzzyFileSearchSessionCommand BuildFuzzyFileSearchSessionUpdateRequest(object parameters)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("fuzzy file search sessionUpdate 的 parametersJson 必须是 JSON 对象。");
        }

        var sessionId = Normalize(ReadString(json, "sessionId"));
        var query = Normalize(ReadString(json, "query"));
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("fuzzy file search sessionUpdate 缺少 sessionId。");
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            throw new InvalidOperationException("fuzzy file search sessionUpdate 缺少 query。");
        }

        return new ControlPlaneUpdateFuzzyFileSearchSessionCommand
        {
            SessionId = sessionId!,
            Query = query!,
        };
    }

    private static ControlPlaneStopFuzzyFileSearchSessionCommand BuildFuzzyFileSearchSessionStopRequest(object parameters)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("fuzzy file search sessionStop 的 parametersJson 必须是 JSON 对象。");
        }

        var sessionId = Normalize(ReadString(json, "sessionId"));
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("fuzzy file search sessionStop 缺少 sessionId。");
        }

        return new ControlPlaneStopFuzzyFileSearchSessionCommand
        {
            SessionId = sessionId!,
        };
    }

    private static ControlPlaneRealtimeStartCommand BuildRealtimeStartRequest(object parameters, string? fallbackThreadId)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("realtime start 的 parametersJson 必须是 JSON 对象。");
        }

        var threadId = Normalize(ReadString(json, "threadId")) ?? Normalize(fallbackThreadId);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            throw new InvalidOperationException("realtime start 缺少 threadId。");
        }

        return new ControlPlaneRealtimeStartCommand
        {
            ThreadId = new ThreadId(threadId!),
            SessionId = Normalize(ReadString(json, "sessionId")),
            Prompt = Normalize(ReadString(json, "prompt")),
        };
    }

    private static ControlPlaneRealtimeAppendTextCommand BuildRealtimeAppendTextRequest(object parameters, string? fallbackThreadId)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("realtime appendText 的 parametersJson 必须是 JSON 对象。");
        }

        var threadId = Normalize(ReadString(json, "threadId")) ?? Normalize(fallbackThreadId);
        var text = ReadString(json, "text");
        if (string.IsNullOrWhiteSpace(threadId))
        {
            throw new InvalidOperationException("realtime appendText 缺少 threadId。");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("realtime appendText 缺少 text。");
        }

        return new ControlPlaneRealtimeAppendTextCommand
        {
            ThreadId = new ThreadId(threadId!),
            SessionId = Normalize(ReadString(json, "sessionId")),
            Text = text!,
        };
    }

    private static ControlPlaneRealtimeAppendAudioCommand BuildRealtimeAppendAudioRequest(object parameters, string? fallbackThreadId)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("realtime appendAudio 的 parametersJson 必须是 JSON 对象。");
        }

        var threadId = Normalize(ReadString(json, "threadId")) ?? Normalize(fallbackThreadId);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            throw new InvalidOperationException("realtime appendAudio 缺少 threadId。");
        }

        if (!json.TryGetProperty("audio", out var audio) || audio.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("realtime appendAudio 缺少 audio。");
        }

        return new ControlPlaneRealtimeAppendAudioCommand
        {
            ThreadId = new ThreadId(threadId!),
            SessionId = Normalize(ReadString(json, "sessionId")),
            Audio = BuildRealtimeAudioInput(audio),
        };
    }

    private static ControlPlaneRealtimeHandoffOutputCommand BuildRealtimeHandoffOutputRequest(object parameters, string? fallbackThreadId)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("realtime handoffOutput 的 parametersJson 必须是 JSON 对象。");
        }

        var threadId = Normalize(ReadString(json, "threadId")) ?? Normalize(fallbackThreadId);
        var handoffId = Normalize(ReadString(json, "handoffId"));
        if (string.IsNullOrWhiteSpace(threadId))
        {
            throw new InvalidOperationException("realtime handoffOutput 缺少 threadId。");
        }

        if (string.IsNullOrWhiteSpace(handoffId))
        {
            throw new InvalidOperationException("realtime handoffOutput 缺少 handoffId。");
        }

        return new ControlPlaneRealtimeHandoffOutputCommand
        {
            ThreadId = new ThreadId(threadId!),
            SessionId = Normalize(ReadString(json, "sessionId")),
            HandoffId = handoffId!,
            Output = ReadString(json, "output") ?? string.Empty,
        };
    }

    private static ControlPlaneRealtimeStopCommand BuildRealtimeStopRequest(object parameters, string? fallbackThreadId)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("realtime stop 的 parametersJson 必须是 JSON 对象。");
        }

        var threadId = Normalize(ReadString(json, "threadId")) ?? Normalize(fallbackThreadId);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            throw new InvalidOperationException("realtime stop 缺少 threadId。");
        }

        return new ControlPlaneRealtimeStopCommand
        {
            ThreadId = new ThreadId(threadId!),
            SessionId = Normalize(ReadString(json, "sessionId")),
        };
    }

    private static ControlPlaneRealtimeAudioInput BuildRealtimeAudioInput(JsonElement audio)
        => new()
        {
            Data = ReadString(audio, "data") ?? string.Empty,
            SampleRate = ReadInt32(audio, "sampleRate"),
            NumChannels = ReadInt32(audio, "numChannels"),
            SamplesPerChannel = ReadInt32(audio, "samplesPerChannel"),
        };

    private static ControlPlaneFeedbackUploadCommand BuildRuntimeFeedbackUploadRequest(object parameters)
    {
        if (parameters is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("feedback 的 parametersJson 必须是 JSON 对象。");
        }

        var classification = Normalize(ReadString(json, "classification"));
        if (string.IsNullOrWhiteSpace(classification))
        {
            throw new InvalidOperationException("feedback 缺少 classification。");
        }

        return new ControlPlaneFeedbackUploadCommand
        {
            Classification = classification!,
            IncludeLogs = ReadBoolean(json, "includeLogs"),
            ThreadId = Normalize(ReadString(json, "threadId")),
            Reason = Normalize(ReadString(json, "reason")),
            ExtraLogFiles = ReadStringArray(json, "extraLogFiles"),
        };
    }

    private static IReadOnlyList<ControlPlaneConfigWriteItem> BuildRuntimeConfigWriteItems(JsonElement json, string defaultMergeStrategy = "replace")
    {
        if (json.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            return ParseRuntimeConfigWriteItems(items, defaultMergeStrategy);
        }

        if (json.TryGetProperty("edits", out var edits) && edits.ValueKind == JsonValueKind.Array)
        {
            return ParseRuntimeConfigWriteItems(edits, defaultMergeStrategy);
        }

        return Array.Empty<ControlPlaneConfigWriteItem>();
    }

    private static IReadOnlyList<ControlPlaneConfigWriteItem> ParseRuntimeConfigWriteItems(JsonElement array, string defaultMergeStrategy)
    {
        var results = new List<ControlPlaneConfigWriteItem>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var keyPath = Normalize(ReadString(item, "keyPath"))
                          ?? Normalize(ReadString(item, "key"))
                          ?? Normalize(ReadString(item, "path"));
            if (string.IsNullOrWhiteSpace(keyPath))
            {
                continue;
            }

            StructuredValue? value = null;
            if (item.TryGetProperty("value", out var valueProperty))
            {
                value = ToContractsStructuredValue(valueProperty);
            }

            results.Add(new ControlPlaneConfigWriteItem
            {
                KeyPath = keyPath!,
                Value = value,
                MergeStrategy = Normalize(ReadString(item, "mergeStrategy"))
                                ?? Normalize(ReadString(item, "merge_strategy"))
                                ?? defaultMergeStrategy,
            });
        }

        return results;
    }

    private static StructuredValue? ToContractsStructuredValue(StructuredValue? value)
        => value;

    private static StructuredValue ToContractsStructuredValue(SidecarThreadHistoryItem value)
        => StructuredValue.FromPlainObject(ConvertJsonElementToPlainObject(JsonSerializer.SerializeToElement(value)));

    private static StructuredValue ToContractsStructuredValue(JsonElement element)
        => StructuredValue.FromPlainObject(ConvertJsonElementToPlainObject(element));

    private static object? ConvertJsonElementToPlainObject(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(static property => property.Name, static property => ConvertJsonElementToPlainObject(property.Value)),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElementToPlainObject).ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var int64Value)
                ? int64Value
                : element.TryGetDecimal(out var decimalValue)
                    ? decimalValue
                    : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.GetRawText(),
        };

    private static object BuildConfigReadResponse(ControlPlaneConfigSnapshotResult result, bool includeLayers)
    {
        var config = BuildConfigSnapshotResponse(result.Config);
        var origins = BuildConfigOriginsResponse(result.Origins);
        foreach (var field in result.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.KeyPath))
            {
                continue;
            }

            if (!config.ContainsKey(field.KeyPath))
            {
                config[field.KeyPath] = field.Value?.ToPlainObject() ?? field.ValueText;
            }

            if (!origins.ContainsKey(field.KeyPath))
            {
                var name = new Dictionary<string, object?>();
                AddIfNotBlank(name, "type", field.SourceType);
                AddIfNotBlank(name, "file", field.SourcePath);
                if (name.Count > 0)
                {
                    origins[field.KeyPath] = new Dictionary<string, object?>
                    {
                        ["name"] = name,
                    };
                }
            }
        }

        var response = new Dictionary<string, object?>
        {
            ["config"] = config,
            ["origins"] = origins,
        };

        if (includeLayers)
        {
            response["layers"] = result.Layers.Select(static layer =>
            {
                var item = new Dictionary<string, object?>
                {
                    ["name"] = layer.Name?.ToPlainObject(),
                    ["version"] = layer.Version,
                    ["config"] = layer.Config?.ToPlainObject() ?? new Dictionary<string, object?>(),
                };

                AddIfNotBlank(item, "disabledReason", layer.DisabledReason);
                return item;
            }).ToArray();
        }

        return response;
    }

    private static Dictionary<string, object?> BuildConfigSnapshotResponse(StructuredValue? snapshot)
    {
        if (snapshot?.Kind != StructuredValueKind.Object)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        return snapshot.Properties.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.ToPlainObject(),
            StringComparer.Ordinal);
    }

    private static Dictionary<string, object?> BuildConfigOriginsResponse(IReadOnlyDictionary<string, ControlPlaneConfigOrigin> origins)
    {
        var response = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in origins)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            var name = new Dictionary<string, object?>(StringComparer.Ordinal);
            AddIfNotBlank(name, "type", pair.Value.Type);
            AddIfNotBlank(name, "file", pair.Value.File);
            AddIfNotBlank(name, "dotTianShuFolder", pair.Value.DotTianShuFolder);

            var item = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (name.Count > 0)
            {
                item["name"] = name;
            }

            AddIfNotBlank(item, "version", pair.Value.Version);
            if (item.Count > 0)
            {
                response[pair.Key] = item;
            }
        }

        return response;
    }

    private static object BuildConfigRequirementsResponse(ControlPlaneConfigRequirementsResult result)
    {
        if (!result.IsDefined)
        {
            return new Dictionary<string, object?>
            {
                ["requirements"] = null,
            };
        }

        var requirements = new Dictionary<string, object?>();
        if (result.AllowedApprovalPolicies.Count > 0)
        {
            requirements["allowedApprovalPolicies"] = result.AllowedApprovalPolicies;
        }

        if (result.AllowedSandboxModes.Count > 0)
        {
            requirements["allowedSandboxModes"] = result.AllowedSandboxModes;
        }

        if (result.AllowedWebSearchModes.Count > 0)
        {
            requirements["allowedWebSearchModes"] = result.AllowedWebSearchModes;
        }

        if (result.FeatureRequirements.Count > 0)
        {
            requirements["featureRequirements"] = result.FeatureRequirements;
        }

        if (!string.IsNullOrWhiteSpace(result.EnforceResidency))
        {
            requirements["enforceResidency"] = result.EnforceResidency;
        }

        if (result.Network is not null)
        {
            var network = new Dictionary<string, object?>();
            AddIfHasValue(network, "enabled", result.Network.Enabled);
            AddIfHasValue(network, "httpPort", result.Network.HttpPort);
            AddIfHasValue(network, "socksPort", result.Network.SocksPort);
            AddIfHasValue(network, "allowUpstreamProxy", result.Network.AllowUpstreamProxy);
            AddIfHasValue(network, "dangerouslyAllowNonLoopbackProxy", result.Network.DangerouslyAllowNonLoopbackProxy);
            AddIfHasValue(network, "dangerouslyAllowNonLoopbackAdmin", result.Network.DangerouslyAllowNonLoopbackAdmin);
            AddIfHasValue(network, "dangerouslyAllowAllUnixSockets", result.Network.DangerouslyAllowAllUnixSockets);
            if (result.Network.AllowedDomains.Count > 0)
            {
                network["allowedDomains"] = result.Network.AllowedDomains;
            }

            if (result.Network.DeniedDomains.Count > 0)
            {
                network["deniedDomains"] = result.Network.DeniedDomains;
            }

            if (result.Network.AllowUnixSockets.Count > 0)
            {
                network["allowUnixSockets"] = result.Network.AllowUnixSockets;
            }

            AddIfHasValue(network, "allowLocalBinding", result.Network.AllowLocalBinding);
            if (network.Count > 0)
            {
                requirements["network"] = network;
            }
        }

        return new Dictionary<string, object?>
        {
            ["requirements"] = requirements,
        };
    }

    private static object BuildConfigWriteResponse(ControlPlaneConfigWriteResult result)
    {
        var response = new Dictionary<string, object?>
        {
            ["status"] = result.Status,
            ["version"] = result.Version,
            ["filePath"] = result.FilePath,
            ["isOverridden"] = result.IsOverridden,
        };

        if (result.OverriddenMetadata is not null)
        {
            var metadata = new Dictionary<string, object?>();
            AddIfNotBlank(metadata, "message", result.OverriddenMetadata.Message);
            if (result.OverriddenMetadata.OverridingLayer is not null)
            {
                metadata["overridingLayer"] = new Dictionary<string, object?>
                {
                    ["type"] = result.OverriddenMetadata.OverridingLayer.Type,
                    ["file"] = result.OverriddenMetadata.OverridingLayer.File,
                    ["dotTianShuFolder"] = result.OverriddenMetadata.OverridingLayer.DotTianShuFolder,
                    ["version"] = result.OverriddenMetadata.OverridingLayer.Version,
                };
            }

            if (result.OverriddenMetadata.EffectiveValue is not null)
            {
                metadata["effectiveValue"] = result.OverriddenMetadata.EffectiveValue.ToPlainObject();
            }

            response["overriddenMetadata"] = metadata;
        }

        return response;
    }

    private static object BuildDirectSkillsListResponse(ControlPlaneSkillCatalogResult result)
        => new Dictionary<string, object?>
        {
            ["entries"] = result.Entries.Select(static entry => new Dictionary<string, object?>
            {
                ["cwd"] = entry.WorkingDirectory,
                ["skills"] = entry.Skills.Select(static skill => new Dictionary<string, object?>
                {
                    ["name"] = skill.Name,
                    ["description"] = skill.Description,
                    ["shortDescription"] = skill.ShortDescription,
                    ["interface"] = skill.Interface?.ToPlainObject(),
                    ["dependencies"] = skill.Dependencies?.ToPlainObject(),
                    ["permissionProfile"] = skill.PermissionProfile?.ToPlainObject(),
                    ["managedNetworkOverride"] = skill.ManagedNetworkOverride?.ToPlainObject(),
                    ["pathToSkillsMd"] = skill.PathToSkillsMd,
                    ["path"] = skill.Path,
                    ["scope"] = skill.Scope,
                    ["enabled"] = skill.Enabled,
                }).ToArray(),
                ["errors"] = entry.Errors.Select(static error => new Dictionary<string, object?>
                {
                    ["path"] = error.Path,
                    ["message"] = error.Message,
                }).ToArray(),
            }).ToArray(),
        };

    private static object BuildSkillsListResponse(ControlPlaneSkillCatalogResult result)
        => new Dictionary<string, object?>
        {
            ["data"] = result.Entries.Select(static entry => new Dictionary<string, object?>
            {
                ["cwd"] = entry.WorkingDirectory,
                ["skills"] = entry.Skills.Select(static skill => new Dictionary<string, object?>
                {
                    ["name"] = skill.Name,
                    ["description"] = skill.Description,
                    ["shortDescription"] = skill.ShortDescription,
                    ["interface"] = skill.Interface?.ToPlainObject(),
                    ["dependencies"] = skill.Dependencies?.ToPlainObject(),
                    ["permissionProfile"] = skill.PermissionProfile?.ToPlainObject(),
                    ["managedNetworkOverride"] = skill.ManagedNetworkOverride?.ToPlainObject(),
                    ["pathToSkillsMd"] = skill.PathToSkillsMd,
                    ["path"] = skill.Path,
                    ["scope"] = skill.Scope,
                    ["enabled"] = skill.Enabled,
                }).ToArray(),
                ["errors"] = entry.Errors.Select(static error => new Dictionary<string, object?>
                {
                    ["path"] = error.Path,
                    ["message"] = error.Message,
                }).ToArray(),
            }).ToArray(),
        };

    private static object BuildSkillsRemoteListResponse(ControlPlaneRemoteSkillCatalogResult result)
        => new Dictionary<string, object?>
        {
            ["data"] = result.Items,
            ["nextCursor"] = result.NextCursor,
        };

    private static object BuildDirectAppListResponse(ControlPlaneAppCatalogResult result)
        => new Dictionary<string, object?>
        {
            ["items"] = result.Items.Select(static item => new Dictionary<string, object?>
            {
                ["id"] = item.Id,
                ["name"] = item.Name,
                ["description"] = item.Description,
                ["installUrl"] = item.InstallUrl,
                ["distributionChannel"] = item.DistributionChannel,
                ["isAccessible"] = item.IsAccessible,
                ["isEnabled"] = item.IsEnabled,
                ["pluginDisplayNames"] = item.PluginDisplayNames.ToArray(),
            }).ToArray(),
            ["nextCursor"] = result.NextCursor,
        };

    private static object BuildAppListResponse(ControlPlaneAppCatalogResult result)
        => new Dictionary<string, object?>
        {
            ["data"] = result.Items.Select(static item => new Dictionary<string, object?>
            {
                ["id"] = item.Id,
                ["name"] = item.Name,
                ["description"] = item.Description,
                ["logoUrl"] = item.LogoUrl,
                ["logoUrlDark"] = item.LogoUrlDark,
                ["distributionChannel"] = item.DistributionChannel,
                ["branding"] = item.Branding?.ToPlainObject(),
                ["appMetadata"] = item.Metadata?.ToPlainObject(),
                ["labels"] = item.Labels,
                ["installUrl"] = item.InstallUrl,
                ["isAccessible"] = item.IsAccessible,
                ["isEnabled"] = item.IsEnabled,
                ["pluginDisplayNames"] = item.PluginDisplayNames.ToArray(),
            }).ToArray(),
            ["nextCursor"] = result.NextCursor,
        };

    private static object BuildPluginListResponse(ControlPlanePluginCatalogResult result)
        => new Dictionary<string, object?>
        {
            ["marketplaces"] = result.Marketplaces.Select(static marketplace => new Dictionary<string, object?>
            {
                ["name"] = marketplace.Name,
                ["path"] = marketplace.Path,
                ["plugins"] = marketplace.Plugins.Select(static plugin => new Dictionary<string, object?>
                {
                    ["id"] = plugin.Id,
                    ["name"] = plugin.Name,
                    ["source"] = plugin.Source?.ToPlainObject(),
                    ["installed"] = plugin.Installed,
                    ["enabled"] = plugin.Enabled,
                    ["installPolicy"] = plugin.InstallPolicy,
                    ["authPolicy"] = plugin.AuthPolicy,
                    ["interface"] = plugin.Interface?.ToPlainObject(),
                }).ToArray(),
            }).ToArray(),
            ["remoteSyncError"] = result.RemoteSyncError,
        };

    private static object BuildPluginReadResponse(ControlPlanePluginReadResult result)
        => new Dictionary<string, object?>
        {
            ["plugin"] = result.Plugin is null
                ? null
                : new Dictionary<string, object?>
                {
                    ["marketplaceName"] = result.Plugin.MarketplaceName,
                    ["marketplacePath"] = result.Plugin.MarketplacePath,
                    ["summary"] = new Dictionary<string, object?>
                    {
                        ["id"] = result.Plugin.Summary.Id,
                        ["name"] = result.Plugin.Summary.Name,
                        ["source"] = result.Plugin.Summary.Source?.ToPlainObject(),
                        ["installed"] = result.Plugin.Summary.Installed,
                        ["enabled"] = result.Plugin.Summary.Enabled,
                        ["installPolicy"] = result.Plugin.Summary.InstallPolicy,
                        ["authPolicy"] = result.Plugin.Summary.AuthPolicy,
                        ["interface"] = result.Plugin.Summary.Interface?.ToPlainObject(),
                    },
                    ["description"] = result.Plugin.Description,
                    ["skills"] = result.Plugin.Skills.Select(static skill => new Dictionary<string, object?>
                    {
                        ["name"] = skill.Name,
                        ["description"] = skill.Description,
                        ["shortDescription"] = skill.ShortDescription,
                        ["interface"] = skill.Interface?.ToPlainObject(),
                        ["path"] = skill.Path,
                    }).ToArray(),
                    ["apps"] = result.Plugin.Apps.Select(static app => new Dictionary<string, object?>
                    {
                        ["id"] = app.Id,
                        ["name"] = app.Name,
                        ["description"] = app.Description,
                        ["installUrl"] = app.InstallUrl,
                    }).ToArray(),
                    ["mcpServers"] = result.Plugin.McpServers.ToArray(),
                },
        };

    private static object BuildPluginInstallResponse(ControlPlanePluginInstallResult result)
        => new Dictionary<string, object?>
        {
            ["authPolicy"] = result.AuthPolicy,
            ["appsNeedingAuth"] = result.AppsNeedingAuth.Select(static app => new Dictionary<string, object?>
            {
                ["id"] = app.Id,
                ["name"] = app.Name,
                ["description"] = app.Description,
                ["installUrl"] = app.InstallUrl,
            }).ToArray(),
        };

    private static object BuildExperimentalFeatureListResponse(ControlPlaneExperimentalFeatureCatalogResult result)
        => new Dictionary<string, object?>
        {
            ["data"] = result.Items,
            ["nextCursor"] = result.NextCursor,
        };

    private static object BuildCollaborationModeListResponse(ControlPlaneCollaborationModeCatalogResult result)
        => new Dictionary<string, object?>
        {
            ["data"] = result.Items.Select(static item => new Dictionary<string, object?>
            {
                ["name"] = item.Name,
                ["mode"] = item.Mode,
                ["model"] = item.Model,
                ["reasoning_effort"] = item.ReasoningEffort,
            }).ToArray(),
        };

    private static object BuildMcpServerStatusListResponse(ControlPlaneMcpServerCatalogResult result)
        => new Dictionary<string, object?>
        {
            ["data"] = result.Items.Select(static item => new Dictionary<string, object?>
            {
                ["name"] = item.Name,
                ["authStatus"] = item.AuthStatus,
                ["tools"] = item.ToolNames.ToDictionary(static toolName => toolName, static _ => new object(), StringComparer.Ordinal),
                ["resources"] = item.ResourceUris.Select(static uri => new Dictionary<string, object?> { ["uri"] = uri }).ToArray(),
                ["resourceTemplates"] = item.ResourceTemplateUris.Select(static uriTemplate => new Dictionary<string, object?> { ["uriTemplate"] = uriTemplate }).ToArray(),
            }).ToArray(),
            ["nextCursor"] = result.NextCursor,
        };

    private static object BuildConversationSummaryResponse(ControlPlaneConversationArtifact? result)
        => new Dictionary<string, object?>
        {
            ["summary"] = result,
        };

    private static void AddIfHasValue<T>(IDictionary<string, object?> dictionary, string key, T? value)
        where T : struct
    {
        if (value.HasValue)
        {
            dictionary[key] = value.Value;
        }
    }

    private static void AddIfNotBlank(IDictionary<string, object?> dictionary, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            dictionary[key] = value;
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static int? ReadInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
        {
            return value;
        }

        if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out value))
        {
            return value;
        }

        return null;
    }

    private static long? ReadInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var value))
        {
            return value;
        }

        if (property.ValueKind == JsonValueKind.String && long.TryParse(property.GetString(), out value))
        {
            return value;
        }

        return null;
    }

    private static ushort? ReadUInt16(JsonElement element, string propertyName)
    {
        var value = ReadInt32(element, propertyName);
        if (!value.HasValue || value.Value < 0 || value.Value > ushort.MaxValue)
        {
            return null;
        }

        return (ushort)value.Value;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return property.EnumerateArray()
            .Select(static item => item.ValueKind == JsonValueKind.String ? Normalize(item.GetString()) : Normalize(item.ToString()))
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return array.EnumerateArray()
            .Select(static item => item.ValueKind == JsonValueKind.String ? Normalize(item.GetString()) : Normalize(item.ToString()))
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();
    }

    private static bool ReadBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var value) => value,
            _ => false,
        };
    }

    private static bool? ReadNullableBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var value) => value,
            _ => null,
        };
    }

    private static ControlPlaneCommandExecutionTerminalSize? ReadOptionalCommandExecutionTerminalSize(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"{propertyName} 必须是 JSON 对象。");
        }

        return ReadRequiredCommandExecutionTerminalSize(property);
    }

    private static ControlPlaneCommandExecutionTerminalSize ReadRequiredCommandExecutionTerminalSize(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var property) || property.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"{propertyName} 必须是 JSON 对象。");
        }

        return ReadRequiredCommandExecutionTerminalSize(property);
    }

    private static ControlPlaneCommandExecutionTerminalSize ReadRequiredCommandExecutionTerminalSize(JsonElement size)
    {
        var rows = ReadUInt16(size, "rows");
        var cols = ReadUInt16(size, "cols");
        if (!rows.HasValue || !cols.HasValue)
        {
            throw new InvalidOperationException("command execution size 缺少有效的 rows/cols。");
        }

        return new ControlPlaneCommandExecutionTerminalSize
        {
            Rows = rows.Value,
            Cols = cols.Value,
        };
    }

    private static IReadOnlyDictionary<string, string?> ReadOptionalStringOrNullDictionary(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var property))
        {
            return new Dictionary<string, string?>();
        }

        if (property.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"{propertyName} 必须是 JSON 对象。");
        }

        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var item in property.EnumerateObject())
        {
            values[item.Name] = item.Value.ValueKind switch
            {
                JsonValueKind.String => item.Value.GetString(),
                JsonValueKind.Null => null,
                _ => throw new InvalidOperationException($"{propertyName} 的值必须是字符串或 null。key={item.Name}"),
            };
        }

        return values;
    }

    private static StructuredValue? ReadOptionalStructuredValue(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var property))
        {
            return null;
        }

        return StructuredValue.FromJsonElement(property);
    }

    private static IReadOnlyList<StructuredValue>? ReadOptionalStructuredArray(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"{propertyName} 必须是 JSON 数组。");
        }

        return property.EnumerateArray().Select(StructuredValue.FromJsonElement).ToArray();
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement property)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out property))
        {
            return true;
        }

        property = default;
        return false;
    }

    private static object? ReadJsonValue(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(static property => property.Name, static property => ReadJsonValue(property.Value)),
            JsonValueKind.Array => element.EnumerateArray().Select(ReadJsonValue).ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var intValue) => intValue,
            JsonValueKind.Number when element.TryGetDouble(out var doubleValue) => doubleValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText(),
        };

    private static ControlPlaneApprovalResolution ParseApprovalResolution(string callId, RespondApprovalPayload payload)
    {
        var normalizedDecision = Normalize(payload.Decision)?
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        var note = Normalize(payload.Note);
        var decision = normalizedDecision switch
        {
            "accept" or "approve" => ControlPlaneApprovalDecision.Approve,
            "session" or "acceptforsession" or "approvesession" => ControlPlaneApprovalDecision.ApproveForSession,
            "always" or "acceptandremember" or "approvealways" => ControlPlaneApprovalDecision.ApproveAndRemember,
            "acceptwithexecpolicyamendment" or "acceptwithexecpolicy" or "approvewithexecpolicy"
                => ControlPlaneApprovalDecision.ApproveWithExecutionPolicyAmendment,
            "applynetworkpolicyamendment" or "approvenetworkrule" or "applynetworkrule"
                => ControlPlaneApprovalDecision.ApplyNetworkPolicyAmendment,
            "cancel" => ControlPlaneApprovalDecision.Cancel,
            "decline" or "reject" => ControlPlaneApprovalDecision.Decline,
            _ when payload.ExecPolicyAmendment?.CommandPrefix is { Length: > 0 }
                => ControlPlaneApprovalDecision.ApproveWithExecutionPolicyAmendment,
            _ when payload.NetworkPolicyAmendment is not null
                => ControlPlaneApprovalDecision.ApplyNetworkPolicyAmendment,
            _ => payload.Approved ? ControlPlaneApprovalDecision.Approve : ControlPlaneApprovalDecision.Decline,
        };

        return new ControlPlaneApprovalResolution
        {
            CallId = new CallId(callId),
            Decision = decision,
            Note = note,
            CommandPrefix = payload.ExecPolicyAmendment?.CommandPrefix?.ToArray() ?? Array.Empty<string>(),
            NetworkHost = payload.NetworkPolicyAmendment?.Host,
            NetworkAction = IsSupportedNetworkPolicyAction(payload.NetworkPolicyAmendment?.Action)
                ? payload.NetworkPolicyAmendment?.Action
                : null,
        };
    }

    private static SidecarApprovalRequestPayload? BuildSidecarApprovalRequestPayload(SidecarApprovalRequestPayload? request)
        => request is null
            ? null
            : new SidecarApprovalRequestPayload
            {
                ToolName = request.ToolName,
                ApprovalKind = request.ApprovalKind,
                AvailableDecisions = request.AvailableDecisions?.ToArray(),
                AvailableDecisionOptions = BuildSidecarApprovalDecisionOptions(request.AvailableDecisionOptions),
                Summary = request.Summary,
                MetadataFields = (request.MetadataFields ?? Array.Empty<SidecarApprovalMetadataFieldPayload>())
                    .Select(static field => new SidecarApprovalMetadataFieldPayload
                    {
                        Key = field.Key,
                        ValueType = field.ValueType,
                        ValueText = field.ValueText,
                    })
                    .ToArray(),
                ProposedExecPolicyAmendment = BuildSidecarExecPolicyAmendment(request.ProposedExecPolicyAmendment),
                ProposedNetworkPolicyAmendments = request.ProposedNetworkPolicyAmendments is { Length: > 0 } amendments
                    ? amendments
                        .Select(BuildSidecarNetworkPolicyAmendment)
                        .Where(static item => item is not null)
                        .Cast<SidecarNetworkPolicyAmendmentPayload>()
                        .ToArray()
                    : null,
            };

    private static SidecarApprovalDecisionOptionPayload[]? BuildSidecarApprovalDecisionOptions(
        IReadOnlyList<SidecarApprovalDecisionOptionPayload>? options)
        => options is not { Count: > 0 }
            ? null
            : options
                .Select(static option => new SidecarApprovalDecisionOptionPayload
                {
                    Type = option.Type,
                    ExecPolicyAmendment = BuildSidecarExecPolicyAmendment(option.ExecPolicyAmendment),
                    NetworkPolicyAmendment = BuildSidecarNetworkPolicyAmendment(option.NetworkPolicyAmendment),
                })
                .ToArray();

    private static SidecarExecPolicyAmendmentPayload? BuildSidecarExecPolicyAmendment(
        SidecarExecPolicyAmendmentPayload? amendment)
        => amendment is not { CommandPrefix.Length: > 0 }
            ? null
            : new SidecarExecPolicyAmendmentPayload
            {
                CommandPrefix = amendment.CommandPrefix.ToArray(),
            };

    private static SidecarNetworkPolicyAmendmentPayload? BuildSidecarNetworkPolicyAmendment(
        SidecarNetworkPolicyAmendmentPayload? amendment)
        => amendment is null
            ? null
            : new SidecarNetworkPolicyAmendmentPayload
            {
                Host = amendment.Host,
                Action = amendment.Action,
            };

    private static bool IsSupportedNetworkPolicyAction(string? action)
        => string.Equals(action, "allow", StringComparison.OrdinalIgnoreCase)
           || string.Equals(action, "deny", StringComparison.OrdinalIgnoreCase);

    private static ControlPlanePermissionScope ParsePermissionGrantScope(string? value)
        => string.Equals(Normalize(value), "session", StringComparison.OrdinalIgnoreCase)
            ? ControlPlanePermissionScope.Session
            : ControlPlanePermissionScope.Turn;

    private static IReadOnlyDictionary<string, StructuredValue>? ToStructuredDictionary(IReadOnlyDictionary<string, StructuredValue>? values)
    {
        if (values is null)
        {
            return null;
        }

        return values.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.Ordinal);
    }

    private static bool IsApproved(ControlPlaneApprovalResolution decision)
        => decision.Decision switch
        {
            ControlPlaneApprovalDecision.Approve => true,
            ControlPlaneApprovalDecision.ApproveForSession => true,
            ControlPlaneApprovalDecision.ApproveAndRemember => true,
            ControlPlaneApprovalDecision.ApproveWithExecutionPolicyAmendment => decision.CommandPrefix.Count > 0,
            ControlPlaneApprovalDecision.ApplyNetworkPolicyAmendment => !string.Equals(decision.NetworkAction, "deny", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };

    private static string ToProtocolDecisionToken(ControlPlaneApprovalDecision decision)
        => decision switch
        {
            ControlPlaneApprovalDecision.Approve => "accept",
            ControlPlaneApprovalDecision.ApproveForSession => "acceptForSession",
            ControlPlaneApprovalDecision.ApproveAndRemember => "acceptAndRemember",
            ControlPlaneApprovalDecision.ApproveWithExecutionPolicyAmendment => "acceptWithExecpolicyAmendment",
            ControlPlaneApprovalDecision.ApplyNetworkPolicyAmendment => "applyNetworkPolicyAmendment",
            ControlPlaneApprovalDecision.Cancel => "cancel",
            _ => "decline",
        };

    private static SidecarPermissionRequestPayload? BuildSidecarPermissionRequestPayload(SidecarPermissionRequestPayload? request)
        => request is null
            ? null
            : new SidecarPermissionRequestPayload
            {
                Reason = request.Reason,
                Fields = (request.Fields ?? Array.Empty<SidecarPermissionFieldPayload>())
                    .Select(static field => new SidecarPermissionFieldPayload
                    {
                        Key = field.Key,
                        ValueType = field.ValueType,
                        ValueText = field.ValueText,
                    })
                    .ToArray(),
                PermissionsJson = request.PermissionsJson,
                Summary = request.Summary,
            };

    private static SidecarUserInputRequestPayload? BuildSidecarUserInputRequestPayload(SidecarUserInputRequestPayload? request)
        => request is null
            ? null
            : new SidecarUserInputRequestPayload
            {
                Questions = (request.Questions ?? Array.Empty<SidecarUserInputQuestionPayload>())
                    .Select(static question => new SidecarUserInputQuestionPayload
                    {
                        Id = question.Id,
                        Header = question.Header,
                        Prompt = question.Prompt,
                        IsSecret = question.IsSecret,
                        IsOther = question.IsOther,
                        Options = question.Options is { Length: > 0 } options
                            ? options
                                .Select(static option => new SidecarUserInputOptionPayload
                                {
                                    Label = option.Label,
                                    Description = option.Description,
                                })
                                .ToArray()
                            : null,
                    })
                    .ToArray(),
                Summary = request.Summary,
                Mode = request.Mode,
                RequestedSchema = request.RequestedSchema is null
                    ? null
                    : StructuredValue.FromPlainObject(request.RequestedSchema.ToPlainObject()),
                Url = request.Url,
                ServerName = request.ServerName,
                ElicitationId = request.ElicitationId,
            };

    private static SidecarServerRequestResolvedPayload? BuildSidecarServerRequestResolvedPayload(SidecarServerRequestResolvedPayload? request)
        => request is null
            ? null
            : new SidecarServerRequestResolvedPayload
            {
                RequestId = request.RequestId,
                RequestKind = request.RequestKind,
                CallId = request.CallId,
                RequestIdRaw = request.RequestIdRaw,
            };

    private static SidecarPendingFollowUpPayload? BuildSidecarPendingFollowUpPayload(SidecarPendingFollowUpPayload? payload)
        => payload is null
            ? null
            : new SidecarPendingFollowUpPayload
            {
                CorrelationId = payload.CorrelationId,
                RequestedMode = payload.RequestedMode,
                EffectiveMode = payload.EffectiveMode,
                LifecycleState = payload.LifecycleState,
                ExpectedTurnId = payload.ExpectedTurnId,
                TurnId = payload.TurnId,
                CompareKey = BuildSidecarPendingFollowUpCompareKeyPayload(payload.CompareKey),
            };

    private static SidecarPendingFollowUpCompareKeyPayload? BuildSidecarPendingFollowUpCompareKeyPayload(
        SidecarPendingFollowUpCompareKeyPayload? payload)
        => payload is null
            ? null
            : new SidecarPendingFollowUpCompareKeyPayload
            {
                Message = payload.Message,
                ImageCount = payload.ImageCount,
            };

    private static SidecarPendingInputStatePayload? BuildSidecarPendingInputStatePayload(SidecarPendingInputStatePayload? state)
        => state is null
            ? null
            : new SidecarPendingInputStatePayload
            {
                Entries = (state.Entries ?? Array.Empty<SidecarPendingInputStateEntryPayload>())
                    .Select(BuildSidecarPendingInputStateEntryPayload)
                    .ToArray(),
                QueuedUserMessages = (state.QueuedUserMessages ?? Array.Empty<SidecarPendingInputStateEntryPayload>())
                    .Select(BuildSidecarPendingInputStateEntryPayload)
                    .ToArray(),
                PendingSteers = (state.PendingSteers ?? Array.Empty<SidecarPendingInputStateEntryPayload>())
                    .Select(BuildSidecarPendingInputStateEntryPayload)
                    .ToArray(),
                InterruptRequestPending = state.InterruptRequestPending,
                SubmitPendingSteersAfterInterrupt = state.SubmitPendingSteersAfterInterrupt,
            };

    private static SidecarPendingInputStateEntryPayload BuildSidecarPendingInputStateEntryPayload(SidecarPendingInputStateEntryPayload entry)
        => new()
        {
            CorrelationId = entry.CorrelationId,
            RequestedMode = entry.RequestedMode,
            EffectiveMode = entry.EffectiveMode,
            LifecycleState = entry.LifecycleState,
            ExpectedTurnId = entry.ExpectedTurnId,
            TurnId = entry.TurnId,
            PendingBucket = entry.PendingBucket,
            CompareKey = BuildSidecarPendingFollowUpCompareKeyPayload(entry.CompareKey),
            Inputs = (entry.Inputs ?? Array.Empty<SidecarUserInputPayload>())
                .Select(BuildSidecarUserInputPayload)
                .ToArray(),
        };

    private static object? BuildPendingInputStateObject(ControlPlanePendingInputState? state)
    {
        if (state is null)
        {
            return null;
        }

        return new
        {
            entries = state.Entries.Select(BuildPendingInputStateEntryObject).ToArray(),
            queuedUserMessages = (state.QueuedUserMessages ?? Array.Empty<ControlPlanePendingInputStateEntry>())
                .Select(BuildPendingInputStateEntryObject)
                .ToArray(),
            pendingSteers = (state.PendingSteers ?? Array.Empty<ControlPlanePendingInputStateEntry>())
                .Select(BuildPendingInputStateEntryObject)
                .ToArray(),
            interruptRequestPending = state.InterruptRequestPending,
            submitPendingSteersAfterInterrupt = state.SubmitPendingSteersAfterInterrupt,
        };
    }

    private static object BuildPendingInputStateEntryObject(ControlPlanePendingInputStateEntry entry)
        => new
        {
            correlationId = entry.CorrelationId,
            requestedMode = entry.RequestedMode,
            effectiveMode = entry.EffectiveMode,
            lifecycleState = entry.LifecycleState,
            expectedTurnId = entry.ExpectedTurnId,
            turnId = entry.TurnId,
            compareKey = entry.CompareKey?.ToPlainObject(),
            pendingBucket = entry.PendingBucket,
            inputs = (entry.Inputs ?? Array.Empty<ControlPlaneInputItem>())
                .Select(BuildUserInputObject)
                .ToArray(),
        };

    private static object[] BuildPendingInteractiveRequestsObject(
        IReadOnlyList<ControlPlanePendingInteractiveRequest>? requests)
        => requests is not { Count: > 0 }
            ? Array.Empty<object>()
            : requests.Select(BuildPendingInteractiveRequestObject).ToArray();

    private static object BuildPendingInteractiveRequestObject(ControlPlanePendingInteractiveRequest request)
        => new
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
            availableDecisions = request.AvailableDecisions?.ToArray(),
            availableDecisionOptions = request.AvailableDecisionOptions?
                .Select(BuildApprovalDecisionOptionObject)
                .ToArray(),
        };

    private static object BuildApprovalDecisionOptionObject(ControlPlaneApprovalDecisionOption option)
        => new
        {
            type = option.Type,
            execPolicyAmendment = option.ExecPolicyAmendment is null
                ? null
                : new
                {
                    commandPrefix = option.ExecPolicyAmendment.CommandPrefix.ToArray(),
                },
            networkPolicyAmendment = option.NetworkPolicyAmendment is null
                ? null
                : new
                {
                    host = option.NetworkPolicyAmendment.Host,
                    action = option.NetworkPolicyAmendment.Action,
                },
        };

    private static SidecarUserInputPayload BuildSidecarUserInputPayload(SidecarUserInputPayload input)
        => new SidecarUserInputPayload
        {
            Type = input.Type,
            Text = input.Text,
            Url = input.Url,
            Path = input.Path,
            Name = input.Name,
            TextElements = input.TextElements?
                .Select(static item => new SidecarTextElementPayload
                {
                    ByteRange = item.ByteRange is null
                        ? null
                        : new SidecarByteRangePayload
                        {
                            Start = item.ByteRange.Start,
                            End = item.ByteRange.End,
                        },
                    Placeholder = item.Placeholder,
                })
                .ToList(),
        };

    private static object BuildUserInputObject(ControlPlaneInputItem input)
        => input switch
        {
            ControlPlaneTextInput textInput => new
            {
                type = Normalize(textInput.Type) ?? "text",
                text = textInput.Text,
                textElements = (textInput.TextElements ?? Array.Empty<ControlPlaneTextElement>())
                    .Select(static item => new
                    {
                        byteRange = new
                        {
                            start = item.ByteRange.Start,
                            end = item.ByteRange.End,
                        },
                        placeholder = item.Placeholder,
                    })
                    .ToArray(),
            },
            ControlPlaneImageInput imageInput => new
            {
                type = Normalize(imageInput.Type) ?? "image",
                url = imageInput.Url,
            },
            ControlPlaneLocalImageInput localImageInput => new
            {
                type = Normalize(localImageInput.Type) ?? "local_image",
                path = localImageInput.Path,
            },
            ControlPlaneSkillInput skillInput => new
            {
                type = Normalize(skillInput.Type) ?? "skill",
                name = skillInput.Name,
                path = skillInput.Path,
            },
            ControlPlaneMentionInput mentionInput => new
            {
                type = Normalize(mentionInput.Type) ?? "mention",
                name = mentionInput.Name,
                path = mentionInput.Path,
            },
            _ => new
            {
                type = Normalize(input.Type) ?? string.Empty,
            },
        };

    private static IReadOnlyList<ControlPlaneInputItem> ResolveConversationInputs(
        IEnumerable<SidecarUserInputPayload>? items,
        string? fallbackMessage)
    {
        var inputs = BuildConversationInputs(items);
        return inputs.Count > 0 ? inputs : CreateTextConversationInputs(fallbackMessage);
    }

    private static IReadOnlyList<ControlPlaneConversationMessage> BuildConversationHistory(IEnumerable<ConversationHistoryMessagePayload>? items)
    {
        if (items is null)
        {
            return Array.Empty<ControlPlaneConversationMessage>();
        }

        var history = new List<ControlPlaneConversationMessage>();
        foreach (var item in items)
        {
            var contentItems = ResolveConversationInputs(item.Inputs, item.Content);
            var content = Normalize(item.Content) ?? Normalize(ExtractConversationText(contentItems));
            if (string.IsNullOrWhiteSpace(content) && contentItems.Count == 0)
            {
                continue;
            }

            history.Add(new ControlPlaneConversationMessage
            {
                Role = ParseConversationRole(item.Role),
                Content = content ?? string.Empty,
                ContentItems = contentItems.ToArray(),
            });
        }

        return history;
    }

    private static IReadOnlyList<ControlPlaneInputItem> CreateTextConversationInputs(string? text)
    {
        var normalizedText = Normalize(text);
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return Array.Empty<ControlPlaneInputItem>();
        }

        return
        [
            new ControlPlaneTextInput(normalizedText!),
        ];
    }

    private static IReadOnlyList<ControlPlaneInputItem> BuildConversationInputs(IEnumerable<SidecarUserInputPayload>? items)
    {
        if (items is null)
        {
            return Array.Empty<ControlPlaneInputItem>();
        }

        var inputs = new List<ControlPlaneInputItem>();
        foreach (var item in items)
        {
            var parsed = ParseConversationInput(item);
            if (parsed is not null)
            {
                inputs.Add(parsed);
            }
        }

        return inputs;
    }

    private static ControlPlaneInputItem? ParseConversationInput(SidecarUserInputPayload? item)
    {
        if (item is null)
        {
            return null;
        }

        var inputType = Normalize(item.Type);
        return inputType?.ToLowerInvariant() switch
        {
            "text" => new ControlPlaneTextInput(
                Normalize(item.Text) ?? string.Empty,
                item.TextElements?
                    .Select(static element => new ControlPlaneTextElement(
                        new ControlPlaneByteRange(
                            element.ByteRange?.Start ?? 0,
                            element.ByteRange?.End ?? 0),
                        element.Placeholder))
                    .ToArray() ?? Array.Empty<ControlPlaneTextElement>()),
            "image" => new ControlPlaneImageInput(Normalize(item.Url) ?? string.Empty),
            "localimage" or "local_image" => new ControlPlaneLocalImageInput(Normalize(item.Path) ?? string.Empty),
            "skill" => new ControlPlaneSkillInput(
                Normalize(item.Name) ?? string.Empty,
                Normalize(item.Path) ?? string.Empty),
            "mention" => new ControlPlaneMentionInput(
                Normalize(item.Name) ?? string.Empty,
                Normalize(item.Path) ?? string.Empty),
            _ => null,
        };
    }

    private static string? ExtractConversationText(IReadOnlyList<ControlPlaneInputItem> inputs)
    {
        var parts = inputs
            .OfType<ControlPlaneTextInput>()
            .Select(static item => Normalize(item.Text))
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToArray();
        return parts.Length == 0 ? null : string.Join(Environment.NewLine, parts);
    }

    private static HostInteractionEnvelope BuildHostInteractionEnvelope(
        string requestId,
        IReadOnlyList<ControlPlaneInputItem> inputs)
        => new(
            new InteractionEnvelopeId(requestId),
            new HostContext(HostSurfaceKind.Vsix, "vssdk.sidecar"),
            BuildHostInteractionItems(inputs));

    private static IReadOnlyList<InteractionItem> BuildHostInteractionItems(IReadOnlyList<ControlPlaneInputItem> inputs)
    {
        var items = new List<InteractionItem>(inputs.Count);
        foreach (var input in inputs)
        {
            switch (input)
            {
                case ControlPlaneTextInput text when !string.IsNullOrWhiteSpace(text.Text):
                    items.Add(new TextInteractionItem(
                        text.Text,
                        (text.TextElements ?? Array.Empty<ControlPlaneTextElement>()).Select(static element => new TextInteractionElement(
                            new InteractionByteRange(element.ByteRange.Start, element.ByteRange.End),
                            element.Placeholder)).ToArray()));
                    break;
                case ControlPlaneImageInput image when !string.IsNullOrWhiteSpace(image.Url):
                    items.Add(new ImageInteractionItem(image.Url));
                    break;
                case ControlPlaneLocalImageInput localImage when !string.IsNullOrWhiteSpace(localImage.Path):
                    items.Add(new LocalImageInteractionItem(localImage.Path));
                    break;
                case ControlPlaneSkillInput skill
                    when !string.IsNullOrWhiteSpace(skill.Name) && !string.IsNullOrWhiteSpace(skill.Path):
                    items.Add(new SkillInteractionItem(skill.Name, skill.Path));
                    break;
                case ControlPlaneMentionInput mention
                    when !string.IsNullOrWhiteSpace(mention.Name) && !string.IsNullOrWhiteSpace(mention.Path):
                    items.Add(new MentionInteractionItem(mention.Name, mention.Path));
                    break;
            }
        }

        if (items.Count == 0)
        {
            throw new InvalidOperationException("宿主输入转换后为空。");
        }

        return items;
    }

    private static ControlPlaneConversationRole ParseConversationRole(string? value)
        => Normalize(value)?.ToLowerInvariant() switch
        {
            "system" => ControlPlaneConversationRole.System,
            "assistant" => ControlPlaneConversationRole.Assistant,
            _ => ControlPlaneConversationRole.User,
        };
}
