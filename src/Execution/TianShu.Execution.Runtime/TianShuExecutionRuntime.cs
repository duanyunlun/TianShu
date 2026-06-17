using System.Collections.Concurrent;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using TianShu.ArtifactStore;
using TianShu.Configuration;
using TianShu.IdentityMemory;
using TianShu.Contracts.Agents;
using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Catalog;
using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Environment;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Governance;
using TianShu.Contracts.Interactions;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Provider;
using TianShu.Contracts.Projections;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Sessions;
using TianShu.Contracts.Tools;
using TianShu.Contracts.Workflows;
using TianShu.Execution.Runtime.Models;
using TianShu.Execution.Runtime.Events;
using TianShu.Execution.Runtime.Providers;
using TianShu.Execution.Protocol;
using TianShu.ProjectionStores;
using Task = System.Threading.Tasks.Task;
using TianShu.Provider.Abstractions;

namespace TianShu.Execution.Runtime;

public sealed partial class TianShuExecutionRuntime : IExecutionRuntimeDiagnostics
{
    private const string AgentJobProgressPrefix = "agent_job_progress:";
    private static readonly TimeSpan UserShellRpcTimeout = TimeSpan.FromMinutes(65);
    private static readonly string[] DefaultOptOutNotificationMethods = [];
    private static readonly JsonSerializerOptions StructuredJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<ControlPlaneConversationStreamEventKind> SuppressedTerminalTurnEventKinds =
    [
        ControlPlaneConversationStreamEventKind.AssistantTextDelta,
        ControlPlaneConversationStreamEventKind.AssistantTextCompleted,
        ControlPlaneConversationStreamEventKind.ReasoningDelta,
        ControlPlaneConversationStreamEventKind.PlanUpdated,
        ControlPlaneConversationStreamEventKind.ToolCallStarted,
        ControlPlaneConversationStreamEventKind.ToolCallOutputDelta,
        ControlPlaneConversationStreamEventKind.ToolCallCompleted,
        ControlPlaneConversationStreamEventKind.ApprovalRequested,
        ControlPlaneConversationStreamEventKind.PermissionRequested,
        ControlPlaneConversationStreamEventKind.UserInputRequested,
        ControlPlaneConversationStreamEventKind.ItemStarted,
        ControlPlaneConversationStreamEventKind.ItemCompleted,
        ControlPlaneConversationStreamEventKind.TaskStarted,
        ControlPlaneConversationStreamEventKind.TaskCompleted,
        ControlPlaneConversationStreamEventKind.OperationReported,
        ControlPlaneConversationStreamEventKind.CommandExecOutputDelta,
    ];
    private static readonly Lazy<HashSet<string>> TypedConfigTopLevelKeys = new(CreateTypedConfigTopLevelKeys);

    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pendingResponses = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<TurnCompletion>> pendingTurns = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TurnCompletion> completedTurns = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> terminalTurnStatuses = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, StringBuilder> turnTextBuffers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ApprovalContext> pendingApprovals = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ApprovalResponse>> pendingApprovalRequests = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<PermissionGrantResponse>> pendingPermissionRequests = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<UserInputSubmission>> pendingUserInputRequests = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<long, PendingInteractiveServerRequest> pendingInteractiveServerRequestsByRequestId = new();
    private readonly ConcurrentDictionary<string, PendingInteractiveServerRequest> pendingInteractiveServerRequestsByRequestToken = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> pendingInteractiveServerRequestIdsByCallId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PendingInteractiveReplayContext> pendingInteractiveReplayContextsByCallId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> interruptRequestedTurns = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<PendingFollowUpCommit>> pendingFollowUpCommitsByThread = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> pendingFollowUpDispatchCancellationsByCorrelation = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> pendingFollowUpDispatchMutationReasonsByCorrelation = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> pendingInputPersistenceVersionsByThread = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingInputStatePayload> pendingInputStatesByThread = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> observedTurnIdsByThread = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim rpcWriteGate = new(1, 1);
    private readonly SemaphoreSlim pendingFollowUpDispatchGate = new(1, 1);
    private readonly SemaphoreSlim pendingInputPersistenceGate = new(1, 1);
    private readonly ConcurrentQueue<string> stderrBuffer = new();
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private readonly object pendingInputStateGate = new();
    private readonly object observedTurnStateGate = new();
    private readonly object traceGate = new();
    private readonly IProviderExecutionEventProjector providerExecutionEventProjector = new ProviderExecutionEventProjector();
    private readonly IProjectionRuntimeStores projectionRuntimeStores;
    private readonly ExecutionRuntimeStepBindingRegistry stepBindingRegistry;
    private readonly IExecutionRuntimeProviderBridge providerBridge;
    private readonly IExecutionRuntimeToolBridge toolBridge;
    private readonly IExecutionRuntimeSubAgentModuleBridge subAgentModuleBridge;
    private readonly IExecutionRuntimeMemoryModuleBridge memoryModuleBridge;
    private readonly ConcurrentDictionary<string, string> threadProjectionTitles = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ThreadRuntimeStatusProjection> threadProjectionStatuses = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ThreadTokenUsageProjection> threadProjectionTokenUsage = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PendingApprovalProjectionState> pendingApprovalProjectionStates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PendingUserInputProjectionState> pendingUserInputProjectionStates = new(StringComparer.Ordinal);

    private Process? process;
    private StreamWriter? stdin;
    private StreamReader? stdout;
    private StreamReader? stderr;
    private Task? stdoutLoop;
    private Task? stderrLoop;
    private CancellationTokenSource? runtimeCts;
    private ExecutionRuntimeOptions options = new();
    private IProviderRuntimeBootstrap providerBootstrap = null!;
    private IProviderNotificationInterpreter providerNotificationInterpreter = null!;
    private IProviderToolEventFactory providerToolEventFactory = null!;
    private IProviderServerRequestRouter providerServerRequestRouter = null!;
    private IProviderServerRequestInterpreter providerServerRequestInterpreter = null!;
    private IProviderServerRequestResponseSerializer providerServerRequestResponseSerializer = null!;
    private IProtocolAdapter protocolAdapter = null!;
    private long rpcId;
    private string? activeThreadId;
    private string? activeTurnId;
    private string? submittedTurnId;
    private string? tracePath;
    private bool pendingInputStateKernelPersistenceEnabled = true;
    private long diagnosticAuditSequence;

    private sealed record PendingInteractiveServerRequest(
        object RequestIdValue,
        string RequestIdToken,
        long? NumericRequestId,
        string CallId,
        string? ThreadId,
        string? TurnId,
        string RequestKind,
        string? ToolName);

    private sealed record PendingInteractiveReplayContext(
        object RequestIdValue,
        string RequestIdToken,
        long? NumericRequestId,
        string CallId,
        string? ThreadId,
        string? TurnId,
        string RequestKind,
        string? ToolName,
        string? RequestMethod);

    private sealed record PendingInteractiveReplaySeed(
        string CallId,
        object RequestIdValue,
        string RequestIdToken,
        long? NumericRequestId,
        string RequestKind,
        string? ThreadId,
        string? TurnId,
        string? ToolName,
        string? RequestMethod);

    private sealed record PendingApprovalProjectionState(
        string CallId,
        string? ThreadId,
        string Title,
        string Reason,
        ParticipantRef RequestedFromParticipant,
        DateTimeOffset RequestedAt);

    private sealed record PendingUserInputProjectionState(
        string CallId,
        string? ThreadId,
        string Prompt,
        ParticipantRef RequestedFromParticipant,
        DateTimeOffset RequestedAt);

    public string RuntimeName => "TianShuAppHost(.NET)";

    public string? ActiveThreadId => activeThreadId;

    public bool HasActiveTurn
        => !string.IsNullOrWhiteSpace(activeTurnId)
           || !string.IsNullOrWhiteSpace(submittedTurnId);

    public event EventHandler<ControlPlaneConversationStreamEventArgs>? StreamEventReceived;

    public TianShuExecutionRuntime()
        : this(identityMemoryPlane: null, projectionRuntimeStores: null)
    {
    }

    public TianShuExecutionRuntime(
        ExecutionRuntimeStepBindingRegistry stepBindingRegistry,
        IExecutionRuntimeMetricsSink? metricsSink = null)
        : this(identityMemoryPlane: null, projectionRuntimeStores: null, stepBindingRegistry, metricsSink)
    {
    }

    public TianShuExecutionRuntime(ITianShuIdentityMemoryPlane? identityMemoryPlane)
        : this(identityMemoryPlane, projectionRuntimeStores: null)
    {
    }

    public TianShuExecutionRuntime(ITianShuIdentityMemoryPlane? identityMemoryPlane, IProjectionRuntimeStores? projectionRuntimeStores)
        : this(identityMemoryPlane, projectionRuntimeStores, stepBindingRegistry: null, metricsSink: null)
    {
    }

    public TianShuExecutionRuntime(
        ITianShuIdentityMemoryPlane? identityMemoryPlane,
        IProjectionRuntimeStores? projectionRuntimeStores,
        ExecutionRuntimeStepBindingRegistry? stepBindingRegistry,
        IExecutionRuntimeMetricsSink? metricsSink)
    {
        this.identityMemoryPlane = identityMemoryPlane;
        this.projectionRuntimeStores = projectionRuntimeStores ?? new InMemoryProjectionRuntimeStores();
        this.stepBindingRegistry = stepBindingRegistry ?? ExecutionRuntimeStepBindingRegistry.Empty;
        providerBridge = new ExecutionRuntimeProviderBridge(metricsSink);
        toolBridge = new ExecutionRuntimeToolBridge(metricsSink);
        subAgentModuleBridge = new ExecutionRuntimeSubAgentModuleBridge();
        memoryModuleBridge = new ExecutionRuntimeMemoryModuleBridge();
        workflowPlane = new InMemoryTianShuWorkflowPlane(collaborationPlane);
        ApplyProviderRuntimeState(ProviderRuntimeBootstrapRegistry.CreateRuntimeState(null));
    }

    private void ApplyProviderRuntimeState(ProviderRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        providerBootstrap = state.Bootstrap;
        providerNotificationInterpreter = state.NotificationInterpreter;
        providerToolEventFactory = state.ToolEventFactory;
        providerServerRequestRouter = state.ServerRequestRouter;
        providerServerRequestInterpreter = state.ServerRequestInterpreter;
        providerServerRequestResponseSerializer = state.ServerRequestResponseSerializer;
        protocolAdapter = state.ProtocolAdapter;
    }

    private void ApplyArtifactRuntimeStoreState(ExecutionRuntimeOptions runtimeOptions)
    {
        artifactRuntimeStoreSet.Dispose();
        artifactRuntimeStoreSet = ArtifactRuntimeStoreResolver.CreateFromConfigPath(runtimeOptions.ConfigFilePath, projectionRuntimeStores);
        TraceRuntime("artifact-store", $"type={artifactRuntimeStoreSet.SourceType}; detail={artifactRuntimeStoreSet.SourceDetail}");
    }

    public async Task InitializeAsync(
        ControlPlaneInitializeRuntimeCommand command,
        Func<ToolInvocationRequest, CancellationToken, Task<ToolInvocationResult>>? dynamicToolCallHandler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var options = ExecutionRuntimeOptions.FromControlPlaneCommand(command, dynamicToolCallHandler);

        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DisposeCoreAsync().ConfigureAwait(false);

            this.options = options;
            ApplyArtifactRuntimeStoreState(options);
            ApplyProviderRuntimeState(ProviderRuntimeBootstrapRegistry.CreateRuntimeState(options.ProtocolAdapter));
            if (protocolAdapter.IsExperimental)
            {
                RaiseEvent(new ControlPlaneConversationStreamEvent
                {
                    Kind = ControlPlaneConversationStreamEventKind.Info,
                    Message = $"协议适配器 {protocolAdapter.Id} 处于实验状态：{protocolAdapter.CapabilitySummary}",
                });
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = ResolveExecutablePath(options),
                Arguments = BuildArguments(options),
                WorkingDirectory = ResolveWorkingDirectory(options),
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            tracePath = Normalize(Environment.GetEnvironmentVariable("TIANSHU_RUNTIME_TRACE_PATH"))
                        ?? Normalize(Environment.GetEnvironmentVariable("TIANSHU_RUNTIME_TRACE_PATH"));
            TraceRuntime("startup", $"file={startInfo.FileName}");
            TraceRuntime("startup", $"args={startInfo.Arguments}");
            TraceRuntime("startup", $"cwd={startInfo.WorkingDirectory}");
            if (options.UseIsolatedSessionStorage)
            {
                var isolatedStorageRoot = ResolveIsolatedSessionStorageRoot(options)
                    ?? throw new InvalidOperationException("隔离会话目录无效。");
                var sqliteHome = Path.Combine(isolatedStorageRoot, "sqlite");
                Directory.CreateDirectory(sqliteHome);
                startInfo.Environment["TIANSHU_SQLITE_HOME"] = sqliteHome;
                TraceRuntime("startup", $"TIANSHU_SQLITE_HOME={sqliteHome}");
            }

            process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true,
            };

            if (!process.Start())
            {
                throw new InvalidOperationException("无法启动 TianShu app-host 进程。");
            }

            if (options.UseIsolatedSessionStorage)
            {
                var isolatedStorageRoot = ResolveIsolatedSessionStorageRoot(options);
                RaiseEvent(new ControlPlaneConversationStreamEvent
                {
                    Kind = ControlPlaneConversationStreamEventKind.Info,
                    Message = $"已启用隔离会话存储：{isolatedStorageRoot}",
                });
            }

            runtimeCts = new CancellationTokenSource();
            stdin = process.StandardInput;
            stdout = process.StandardOutput;
            stderr = process.StandardError;

            stdoutLoop = Task.Run(() => ReadStdoutLoopAsync(runtimeCts.Token));
            stderrLoop = Task.Run(() => ReadStderrLoopAsync(runtimeCts.Token));

            var startupTimeout = options.StartupTimeout <= TimeSpan.Zero
                ? TimeSpan.FromSeconds(45)
                : options.StartupTimeout;

            using var startupDelayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startupDelayCts.CancelAfter(startupTimeout);

            try
            {
                await Task.Delay(150, startupDelayCts.Token).ConfigureAwait(false);
                if (process.HasExited)
                {
                    throw CreateStartupFailureException("TianShu app-host 启动后立即退出。");
                }

                _ = await SendRpcAsync("initialize", BuildInitializeParams(), startupDelayCts.Token, startupTimeout).ConfigureAwait(false);

                await SendNotificationAsync("initialized", null, startupDelayCts.Token).ConfigureAwait(false);

                JsonElement? threadResult = null;
                var resumed = false;
                var explicitResumeThreadId = Normalize(options.ResumeThreadId);
                var resumeThreadId = await ResolveResumeThreadIdAsync(options, startupDelayCts.Token).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(resumeThreadId))
                {
                    try
                    {
                        threadResult = await SendRpcAsync(
                                "thread/resume",
                                BuildThreadResumeParams(
                                    new ControlPlaneResumeThreadCommand
                                    {
                                        ThreadId = new ThreadId(resumeThreadId),
                                    },
                                    options),
                                startupDelayCts.Token,
                                startupTimeout)
                            .ConfigureAwait(false);
                        resumed = true;
                    }
                    catch when (string.IsNullOrWhiteSpace(explicitResumeThreadId))
                    {
                        if (options.CreateThreadOnInitialize)
                        {
                            RaiseEvent(new ControlPlaneConversationStreamEvent
                            {
                                Kind = ControlPlaneConversationStreamEventKind.Info,
                                Message = $"恢复最近会话失败，回退到新会话。threadId={resumeThreadId}",
                            });

                            threadResult = await SendRpcAsync(
                                    "thread/start",
                                    BuildThreadStartParams(new ControlPlaneStartThreadCommand(), options),
                                    startupDelayCts.Token,
                                    startupTimeout)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            RaiseEvent(new ControlPlaneConversationStreamEvent
                            {
                                Kind = ControlPlaneConversationStreamEventKind.Info,
                                Message = $"恢复最近会话失败，不自动创建新会话。threadId={resumeThreadId}",
                            });
                        }
                    }
                }
                else if (options.CreateThreadOnInitialize)
                {
                    threadResult = await SendRpcAsync(
                            "thread/start",
                            BuildThreadStartParams(new ControlPlaneStartThreadCommand(), options),
                            startupDelayCts.Token,
                            startupTimeout)
                        .ConfigureAwait(false);
                }
                else
                {
                    RaiseEvent(new ControlPlaneConversationStreamEvent
                    {
                        Kind = ControlPlaneConversationStreamEventKind.Info,
                        Message = "未自动创建线程，等待首次发送。",
                    });
                }

                if (threadResult.HasValue)
                {
                    var startupResponse = TryDeserializeThreadResponse(threadResult.Value);
                    activeThreadId = Normalize(startupResponse?.Thread?.Id)
                        ?? throw new InvalidOperationException("线程初始化返回结果中缺少 thread.id。");
                    activeTurnId = null;
                    submittedTurnId = null;

                    RaiseEvent(new ControlPlaneConversationStreamEvent
                    {
                        Kind = ControlPlaneConversationStreamEventKind.Info,
                        ThreadId = ToThreadId(activeThreadId),
                        Message = resumed ? $"已恢复线程：{activeThreadId}" : $"已建立线程：{activeThreadId}",
                    });
                }
                else
                {
                    activeThreadId = null;
                    activeTurnId = null;
                    submittedTurnId = null;
                }
            }
            catch (OperationCanceledException ex) when (startupDelayCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw CreateStartupTimeoutException(startupTimeout, ex);
            }
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public async Task<ControlPlaneTurnSubmissionResult> SendAsync(ControlPlaneSubmitTurnCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await SendAsync(
                ToRuntimeUserInputs(command.Inputs),
                ToRuntimeConversationHistory(command.History),
                cancellationToken,
                command.Envelope is null ? null : InteractionEnvelopeRef.From(command.Envelope))
            .ConfigureAwait(false);
        return ToControlPlaneTurnSubmissionResult(result);
    }

    internal Task<AgentSendResult> SendAsync(string userMessage, IReadOnlyList<ConversationMessage> history, CancellationToken cancellationToken)
        => SendAsync(CreateTextUserInputs(userMessage), history, cancellationToken);

    internal async Task<AgentSendResult> SendAsync(
        IReadOnlyList<AgentUserInput> userInputs,
        IReadOnlyList<ConversationMessage> history,
        CancellationToken cancellationToken,
        InteractionEnvelopeRef? interactionEnvelope = null)
    {
        return await StartTurnAsync(
                NormalizeUserInputs(userInputs),
                waitForCompletion: true,
                history,
                cancellationToken,
                interactionEnvelope: interactionEnvelope)
            .ConfigureAwait(false);
    }

    public async Task<ControlPlaneTurnSubmissionResult> RunUserShellCommandAsync(string command, CancellationToken cancellationToken)
    {
        var result = await RunUserShellCommandCoreAsync(command, cancellationToken).ConfigureAwait(false);
        return ToControlPlaneTurnSubmissionResult(result);
    }

    internal async Task<AgentSendResult> RunUserShellCommandCoreAsync(string command, CancellationToken cancellationToken)
    {
        var normalizedCommand = Normalize(command);
        if (string.IsNullOrWhiteSpace(normalizedCommand))
        {
            return AgentSendResult.Fail("shell 命令为空。");
        }

        if (process is null || stdin is null)
        {
            return AgentSendResult.Fail("运行时未初始化。");
        }

        var currentThreadId = await EnsureActiveThreadAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(currentThreadId))
        {
            return AgentSendResult.Fail("当前线程不存在，且无法自动创建，请重新初始化。");
        }

        try
        {
            var result = await SendRpcAsync(
                    "tianshu/userShell/run",
                    new Dictionary<string, object?>
                    {
                        ["threadId"] = currentThreadId,
                        ["command"] = normalizedCommand,
                    },
                    cancellationToken,
                    UserShellRpcTimeout)
                .ConfigureAwait(false);

            var turnId = ReadString(result, "turnId");
            var turnStatus = ReadString(result, "turnStatus");
            var exitCode = ReadInt32(result, "exitCode");
            var stderr = Normalize(ReadString(result, "stderr"));
            if (!string.IsNullOrWhiteSpace(turnId) && !IsTerminalResumedTurnStatus(turnStatus))
            {
                activeTurnId = turnId;
            }

            if (exitCode is > 0 or < 0)
            {
                return AgentSendResult.Fail(
                    !string.IsNullOrWhiteSpace(stderr)
                        ? stderr!
                        : $"user shell 执行失败，退出码：{exitCode}",
                    result.GetRawText(),
                    turnId,
                    turnStatus);
            }

            return AgentSendResult.Ok(
                "user shell 已执行，结果见流式输出。",
                result.GetRawText(),
                turnId,
                turnStatus);
        }
        catch (Exception ex)
        {
            var stderrTail = string.Join(Environment.NewLine, stderrBuffer.TakeLast(12));
            var detail = string.IsNullOrWhiteSpace(stderrTail)
                ? ex.Message
                : $"{ex.Message}{Environment.NewLine}---- stderr ----{Environment.NewLine}{stderrTail}";
            return AgentSendResult.Fail($"user shell 执行失败：{detail}");
        }
    }

    public async Task<ControlPlaneTurnSubmissionResult> SendFollowUpAsync(ControlPlaneSubmitFollowUpCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await SendFollowUpAsync(
                ToRuntimeUserInputs(command.Inputs),
                ToRuntimeFollowUpMode(command.Mode),
                cancellationToken,
                command.CorrelationId,
                command.Envelope is null ? null : InteractionEnvelopeRef.From(command.Envelope))
            .ConfigureAwait(false);
        return ToControlPlaneTurnSubmissionResult(result);
    }

    public async Task<ControlPlanePendingFollowUpMutationResult> MutatePendingFollowUpAsync(
        ControlPlaneMutatePendingFollowUpCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var correlationId = Normalize(command.CorrelationId);
        var threadId = Normalize(command.ThreadId?.Value) ?? Normalize(activeThreadId);
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return BuildPendingFollowUpMutationResult(
                accepted: false,
                "待发送项 correlationId 为空。",
                command.Kind,
                threadId,
                correlationId,
                turnId: null);
        }

        if (string.IsNullOrWhiteSpace(threadId))
        {
            return BuildPendingFollowUpMutationResult(
                accepted: false,
                "当前没有可治理的活动线程。",
                command.Kind,
                threadId,
                correlationId,
                turnId: null);
        }

        if (!string.Equals(threadId, Normalize(activeThreadId), StringComparison.Ordinal))
        {
            return BuildPendingFollowUpMutationResult(
                accepted: false,
                "只能治理当前活动线程的待发送项。",
                command.Kind,
                threadId,
                correlationId,
                turnId: null);
        }

        var entry = TryFindQueuedPendingInputStateEntry(threadId, correlationId);
        if (entry is null)
        {
            return BuildPendingFollowUpMutationResult(
                accepted: false,
                $"未找到编号对应的待发送项：{correlationId}。",
                command.Kind,
                threadId,
                correlationId,
                turnId: null);
        }

        return command.Kind switch
        {
            ControlPlanePendingFollowUpMutationKind.Drop => DropQueuedPendingFollowUp(threadId, correlationId, entry),
            ControlPlanePendingFollowUpMutationKind.PromoteToSteer => await PromoteQueuedPendingFollowUpToSteerAsync(
                    threadId,
                    correlationId,
                    entry,
                    cancellationToken)
                .ConfigureAwait(false),
            _ => BuildPendingFollowUpMutationResult(
                accepted: false,
                $"不支持的待发送项变更类型：{command.Kind}。",
                command.Kind,
                threadId,
                correlationId,
                turnId: null),
        };
    }

    internal Task<AgentSendResult> SendFollowUpAsync(string userMessage, FollowUpMode mode, CancellationToken cancellationToken, string? correlationId = null)
        => SendFollowUpAsync(CreateTextUserInputs(userMessage), mode, cancellationToken, correlationId, null);

    internal async Task<AgentSendResult> SendFollowUpAsync(
        IReadOnlyList<AgentUserInput> userInputs,
        FollowUpMode mode,
        CancellationToken cancellationToken,
        string? correlationId = null,
        InteractionEnvelopeRef? interactionEnvelope = null)
    {
        var normalizedInputs = NormalizeUserInputs(userInputs);
        var requestedCorrelationId = Normalize(correlationId);
        if (normalizedInputs.Count == 0)
        {
            return AgentSendResult.Fail("输入消息为空。", correlationId: requestedCorrelationId, requestedMode: mode, effectiveMode: mode);
        }

        if (process is null || stdin is null)
        {
            return AgentSendResult.Fail("运行时未初始化。", correlationId: requestedCorrelationId, requestedMode: mode, effectiveMode: mode);
        }

        var effectiveCorrelationId = requestedCorrelationId ?? $"followup-{Guid.NewGuid():N}";

        try
        {
            return mode switch
            {
                FollowUpMode.Queue => await EnqueuePendingFollowUpDispatchAsync(normalizedInputs, FollowUpMode.Queue, cancellationToken, effectiveCorrelationId, interactionEnvelope).ConfigureAwait(false),
                FollowUpMode.Steer when !string.IsNullOrWhiteSpace(activeThreadId) && !string.IsNullOrWhiteSpace(activeTurnId)
                    => await SteerTurnAsync(normalizedInputs, cancellationToken, effectiveCorrelationId).ConfigureAwait(false),
                FollowUpMode.Steer => AgentSendResult.Fail(
                    "当前没有可引导的活动回合。",
                    correlationId: effectiveCorrelationId,
                    requestedMode: FollowUpMode.Steer,
                    effectiveMode: FollowUpMode.Steer),
                FollowUpMode.Interrupt => await EnqueuePendingFollowUpDispatchAsync(normalizedInputs, FollowUpMode.Interrupt, cancellationToken, effectiveCorrelationId, interactionEnvelope).ConfigureAwait(false),
                _ => await StartTurnAsync(
                        normalizedInputs,
                        waitForCompletion: false,
                        Array.Empty<ConversationMessage>(),
                        cancellationToken,
                        correlationId: effectiveCorrelationId,
                        requestedMode: mode,
                        effectiveMode: FollowUpMode.Queue,
                        interactionEnvelope: interactionEnvelope)
                    .ConfigureAwait(false),
            };
        }
        catch (Exception ex)
        {
            var stderrTail = string.Join(Environment.NewLine, stderrBuffer.TakeLast(12));
            var detail = string.IsNullOrWhiteSpace(stderrTail)
                ? ex.Message
                : $"{ex.Message}{Environment.NewLine}---- stderr ----{Environment.NewLine}{stderrTail}";
            return AgentSendResult.Fail(
                $"发送失败：{detail}",
                correlationId: effectiveCorrelationId,
                requestedMode: mode,
                effectiveMode: mode);
        }
    }

    private ControlPlanePendingFollowUpMutationResult DropQueuedPendingFollowUp(
        string threadId,
        string correlationId,
        PendingInputStateEntryPayload entry)
    {
        var inputs = NormalizeUserInputs(entry.Inputs);
        CancelPendingFollowUpDispatch(correlationId, "删除");
        RaisePendingFollowUpLifecycle(
            correlationId,
            FollowUpMode.Queue,
            FollowUpMode.Queue,
            "dropped",
            entry.ExpectedTurnId,
            entry.TurnId,
            BuildUserInputPreview(inputs),
            CountUserInputImages(inputs),
            inputs);

        return BuildPendingFollowUpMutationResult(
            accepted: true,
            "已删除待发送项。",
            ControlPlanePendingFollowUpMutationKind.Drop,
            threadId,
            correlationId,
            turnId: null);
    }

    private async Task<ControlPlanePendingFollowUpMutationResult> PromoteQueuedPendingFollowUpToSteerAsync(
        string threadId,
        string correlationId,
        PendingInputStateEntryPayload entry,
        CancellationToken cancellationToken)
    {
        var currentTurnId = ResolveCurrentTurnControlId();
        if (string.IsNullOrWhiteSpace(activeThreadId)
            || !string.Equals(threadId, Normalize(activeThreadId), StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(currentTurnId))
        {
            return BuildPendingFollowUpMutationResult(
                accepted: false,
                "当前没有可引导的活动回合。",
                ControlPlanePendingFollowUpMutationKind.PromoteToSteer,
                threadId,
                correlationId,
                turnId: null);
        }

        var inputs = NormalizeUserInputs(entry.Inputs);
        if (inputs.Count == 0)
        {
            return BuildPendingFollowUpMutationResult(
                accepted: false,
                "待发送项缺少可发送的输入内容。",
                ControlPlanePendingFollowUpMutationKind.PromoteToSteer,
                threadId,
                correlationId,
                turnId: null);
        }

        CancelPendingFollowUpDispatch(correlationId, "转换为引导");
        RaisePendingFollowUpLifecycle(
            correlationId,
            FollowUpMode.Queue,
            FollowUpMode.Queue,
            "promoted",
            entry.ExpectedTurnId,
            entry.TurnId,
            BuildUserInputPreview(inputs),
            CountUserInputImages(inputs),
            inputs);

        var steerResult = await SteerTurnAsync(inputs, cancellationToken, correlationId).ConfigureAwait(false);
        return BuildPendingFollowUpMutationResult(
            steerResult.Success,
            steerResult.Success ? "已将待发送项转为引导。" : steerResult.Message,
            ControlPlanePendingFollowUpMutationKind.PromoteToSteer,
            threadId,
            correlationId,
            steerResult.TurnId);
    }

    private PendingInputStateEntryPayload? TryFindQueuedPendingInputStateEntry(string? threadId, string? correlationId)
    {
        var normalizedCorrelationId = Normalize(correlationId);
        if (string.IsNullOrWhiteSpace(normalizedCorrelationId))
        {
            return null;
        }

        var state = GetPendingInputStateSnapshot(threadId);
        return state.QueuedUserMessages?.FirstOrDefault(entry =>
            string.Equals(entry.CorrelationId, normalizedCorrelationId, StringComparison.Ordinal));
    }

    private void CancelPendingFollowUpDispatch(string correlationId, string reason)
    {
        if (!pendingFollowUpDispatchCancellationsByCorrelation.TryGetValue(correlationId, out var cancellation))
        {
            return;
        }

        pendingFollowUpDispatchMutationReasonsByCorrelation[correlationId] = reason;
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            pendingFollowUpDispatchMutationReasonsByCorrelation.TryRemove(correlationId, out _);
        }
    }

    private ControlPlanePendingFollowUpMutationResult BuildPendingFollowUpMutationResult(
        bool accepted,
        string message,
        ControlPlanePendingFollowUpMutationKind kind,
        string? threadId,
        string? correlationId,
        string? turnId)
        => new()
        {
            Accepted = accepted,
            Message = message,
            ThreadId = string.IsNullOrWhiteSpace(threadId) ? null : new ThreadId(threadId),
            TurnId = string.IsNullOrWhiteSpace(turnId) ? null : new TurnId(turnId),
            CorrelationId = correlationId,
            Kind = kind,
            PendingInputState = ToControlPlanePendingInputState(GetPendingInputStateSnapshot(threadId)),
        };

    private async Task<AgentSendResult> QueueFollowUpTurnAsync(
        IReadOnlyList<AgentUserInput> userInputs,
        CancellationToken cancellationToken,
        string? correlationId,
        InteractionEnvelopeRef? interactionEnvelope)
    {
        var previewText = BuildUserInputPreview(userInputs);
        var imageCount = CountUserInputImages(userInputs);
        var currentTurnId = ResolveCurrentTurnControlId();
        if (string.IsNullOrWhiteSpace(currentTurnId))
        {
            var queuedResult = await StartTurnAsync(
                    userInputs,
                    waitForCompletion: false,
                    Array.Empty<ConversationMessage>(),
                    cancellationToken,
                    correlationId: correlationId,
                    requestedMode: FollowUpMode.Queue,
                    effectiveMode: FollowUpMode.Queue,
                    interactionEnvelope: interactionEnvelope)
                .ConfigureAwait(false);
            return TrackPendingFollowUpCommit(queuedResult, userInputs, previewText, imageCount, expectedTurnId: null);
        }

        var completion = await WaitTurnCompletionAsync(currentTurnId, cancellationToken).ConfigureAwait(false);
        if (completion is null)
        {
            return AgentSendResult.Fail(
                $"排队跟进超时：等待当前回合 {currentTurnId} 完成失败。",
                turnId: currentTurnId,
                turnStatus: "pending",
                correlationId: correlationId,
                requestedMode: FollowUpMode.Queue,
                effectiveMode: FollowUpMode.Queue);
        }

        var startResult = await StartTurnAsync(
                userInputs,
                waitForCompletion: false,
                Array.Empty<ConversationMessage>(),
                cancellationToken,
                correlationId: correlationId,
                requestedMode: FollowUpMode.Queue,
                effectiveMode: FollowUpMode.Queue,
                interactionEnvelope: interactionEnvelope)
            .ConfigureAwait(false);
        return TrackPendingFollowUpCommit(startResult, userInputs, previewText, imageCount, currentTurnId);
    }

    private async Task<AgentSendResult> EnqueuePendingFollowUpDispatchAsync(
        IReadOnlyList<AgentUserInput> userInputs,
        FollowUpMode requestedMode,
        CancellationToken cancellationToken,
        string? correlationId,
        InteractionEnvelopeRef? interactionEnvelope)
    {
        var normalizedInputs = NormalizeUserInputs(userInputs);
        var previewText = BuildUserInputPreview(normalizedInputs) ?? string.Empty;
        var imageCount = CountUserInputImages(normalizedInputs);
        var normalizedCorrelationId = Normalize(correlationId);
        using var dispatchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (!string.IsNullOrWhiteSpace(normalizedCorrelationId))
        {
            pendingFollowUpDispatchCancellationsByCorrelation[normalizedCorrelationId!] = dispatchCancellation;
        }

        RaisePendingFollowUpLifecycle(
            normalizedCorrelationId,
            requestedMode,
            requestedMode,
            "queued",
            expectedTurnId: null,
            turnId: null,
            previewText,
            imageCount,
            normalizedInputs);

        var gateHeld = false;
        var holdUntilTurnComplete = false;
        try
        {
            await pendingFollowUpDispatchGate.WaitAsync(dispatchCancellation.Token).ConfigureAwait(false);
            gateHeld = true;

            var result = requestedMode switch
            {
                FollowUpMode.Interrupt => await InterruptAndRestartTurnAsync(normalizedInputs, dispatchCancellation.Token, normalizedCorrelationId, interactionEnvelope).ConfigureAwait(false),
                _ => await QueueFollowUpTurnAsync(normalizedInputs, dispatchCancellation.Token, normalizedCorrelationId, interactionEnvelope).ConfigureAwait(false),
            };

            if (result.Success && !string.IsNullOrWhiteSpace(Normalize(result.TurnId)))
            {
                holdUntilTurnComplete = true;
                _ = HoldPendingFollowUpDispatchGateUntilTurnCompleteAsync(result);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            if (!string.IsNullOrWhiteSpace(normalizedCorrelationId)
                && pendingFollowUpDispatchMutationReasonsByCorrelation.TryRemove(normalizedCorrelationId!, out var mutationReason))
            {
                return AgentSendResult.Fail(
                    $"跟进发送已{mutationReason}。",
                    correlationId: normalizedCorrelationId,
                    requestedMode: requestedMode,
                    effectiveMode: requestedMode);
            }

            RaisePendingFollowUpLifecycle(
                normalizedCorrelationId,
                requestedMode,
                requestedMode,
                "cancelled",
                expectedTurnId: null,
                turnId: null,
                previewText,
                imageCount,
                normalizedInputs);
            return AgentSendResult.Fail(
                "跟进发送已取消。",
                correlationId: normalizedCorrelationId,
                requestedMode: requestedMode,
                effectiveMode: requestedMode);
        }
        catch (Exception ex)
        {
            RaisePendingFollowUpLifecycle(
                normalizedCorrelationId,
                requestedMode,
                requestedMode,
                "dispatch_failed",
                expectedTurnId: null,
                turnId: null,
                previewText,
                imageCount,
                normalizedInputs);
            return AgentSendResult.Fail(
                $"发送失败：{ex.Message}",
                correlationId: normalizedCorrelationId,
                requestedMode: requestedMode,
                effectiveMode: requestedMode);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(normalizedCorrelationId))
            {
                pendingFollowUpDispatchCancellationsByCorrelation.TryRemove(
                    new KeyValuePair<string, CancellationTokenSource>(normalizedCorrelationId!, dispatchCancellation));
            }

            if (gateHeld && !holdUntilTurnComplete)
            {
                pendingFollowUpDispatchGate.Release();
            }
        }
    }

    private async Task HoldPendingFollowUpDispatchGateUntilTurnCompleteAsync(AgentSendResult result)
    {
        try
        {
            await WaitForQueuedFollowUpTurnCompletionAsync(result).ConfigureAwait(false);
        }
        finally
        {
            pendingFollowUpDispatchGate.Release();
        }
    }

    private async Task WaitForQueuedFollowUpTurnCompletionAsync(AgentSendResult result)
    {
        var turnId = Normalize(result.TurnId);
        if (!result.Success || string.IsNullOrWhiteSpace(turnId))
        {
            return;
        }

        var shutdownToken = runtimeCts?.Token ?? CancellationToken.None;
        try
        {
            _ = await WaitTurnCompletionAsync(turnId!, shutdownToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // runtime 退出时无需继续维持 follow-up 串行链。
        }
    }

    private async Task<AgentSendResult> StartTurnAsync(
        IReadOnlyList<AgentUserInput> userInputs,
        bool waitForCompletion,
        IReadOnlyList<ConversationMessage> history,
        CancellationToken cancellationToken,
        string? correlationId = null,
        FollowUpMode? requestedMode = null,
        FollowUpMode? effectiveMode = null,
        InteractionEnvelopeRef? interactionEnvelope = null)
    {
        var normalizedInputs = NormalizeUserInputs(userInputs);
        if (normalizedInputs.Count == 0)
        {
            return AgentSendResult.Fail("输入消息为空。");
        }

        if (process is null || stdin is null)
        {
            return AgentSendResult.Fail("运行时未初始化。");
        }

        var currentThreadId = await EnsureActiveThreadAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(currentThreadId))
        {
            return AgentSendResult.Fail("当前线程不存在，且无法自动创建，请重新初始化。");
        }

        try
        {
            var payload = BuildTurnStartParams(currentThreadId, normalizedInputs, history, interactionEnvelope);

            var result = await SendRpcAsync("turn/start", payload, cancellationToken).ConfigureAwait(false);
            var turnId = ReadString(result, "turn", "id");
            var status = ReadString(result, "turn", "status");

            if (string.IsNullOrWhiteSpace(turnId))
            {
                return AgentSendResult.Ok(
                    "消息已发送（未返回 turnId）。",
                    result.GetRawText(),
                    turnStatus: status,
                    correlationId: correlationId,
                    requestedMode: requestedMode,
                    effectiveMode: effectiveMode);
            }

            submittedTurnId = turnId;

            if (!waitForCompletion)
            {
                return AgentSendResult.Ok(
                    $"已提交新消息，turnId={turnId}",
                    result.GetRawText(),
                    turnId,
                    status,
                    correlationId,
                    requestedMode,
                    effectiveMode);
            }

            var completion = await WaitTurnCompletionAsync(turnId, cancellationToken).ConfigureAwait(false);
            if (completion is null)
            {
                return AgentSendResult.Ok(
                    $"消息已发送，turnId={turnId}，请关注流式事件。",
                    result.GetRawText(),
                    turnId,
                    status,
                    correlationId,
                    requestedMode,
                    effectiveMode);
            }

            if (completion.Success)
            {
                var finalText = Normalize(completion.AssistantText) ?? "turn 已完成（未提取到文本）。";
                return AgentSendResult.Ok(
                    finalText,
                    completion.RawJson ?? result.GetRawText(),
                    turnId,
                    completion.Status ?? status,
                    correlationId,
                    requestedMode,
                    effectiveMode);
            }

            return AgentSendResult.Fail(
                Normalize(completion.ErrorMessage) ?? "turn 执行失败。",
                completion.RawJson ?? result.GetRawText(),
                turnId,
                completion.Status ?? status,
                correlationId,
                requestedMode,
                effectiveMode);
        }
        catch (Exception ex)
        {
            var stderrTail = string.Join(Environment.NewLine, stderrBuffer.TakeLast(12));
            var detail = string.IsNullOrWhiteSpace(stderrTail)
                ? ex.Message
                : $"{ex.Message}{Environment.NewLine}---- stderr ----{Environment.NewLine}{stderrTail}";
            return AgentSendResult.Fail(
                $"发送失败：{detail}",
                correlationId: correlationId,
                requestedMode: requestedMode,
                effectiveMode: effectiveMode);
        }
    }

    private async Task<AgentSendResult> SteerTurnAsync(
        IReadOnlyList<AgentUserInput> userInputs,
        CancellationToken cancellationToken,
        string? correlationId)
    {
        var currentTurnId = ResolveCurrentTurnControlId();
        if (string.IsNullOrWhiteSpace(activeThreadId) || string.IsNullOrWhiteSpace(currentTurnId))
        {
            return AgentSendResult.Fail(
                "当前没有可引导的活动回合。",
                correlationId: correlationId,
                requestedMode: FollowUpMode.Steer,
                effectiveMode: FollowUpMode.Steer);
        }

        var expectedTurnId = currentTurnId;
        var previewText = BuildUserInputPreview(userInputs);
        var payload = new Dictionary<string, object?>
        {
            ["threadId"] = activeThreadId,
            ["expectedTurnId"] = expectedTurnId,
            ["input"] = userInputs
                .Select(static input => ToControlPlaneInputItem(input))
                .Select(protocolAdapter.BuildUserInput)
                .ToArray(),
        };
        AddIfPresent(payload, "text", previewText);

        var result = await SendRpcAsync("turn/steer", payload, cancellationToken).ConfigureAwait(false);
        var steerTurnId = ReadString(result, "turnId") ?? expectedTurnId;
        if (!string.IsNullOrWhiteSpace(steerTurnId))
        {
            activeTurnId = steerTurnId;
            if (string.Equals(submittedTurnId, steerTurnId, StringComparison.Ordinal))
            {
                submittedTurnId = null;
            }
        }

        EnqueuePendingFollowUpCommit(
            activeThreadId,
            correlationId,
            userInputs,
            previewText,
            expectedTurnId,
            steerTurnId,
            FollowUpMode.Steer,
            FollowUpMode.Steer,
            CountUserInputImages(userInputs));
        var status = ReadString(result, "turn", "status");
        return AgentSendResult.Ok(
            $"已转向当前执行，turnId={steerTurnId}",
            result.GetRawText(),
            steerTurnId,
            status,
            correlationId,
            FollowUpMode.Steer,
            FollowUpMode.Steer);
    }

    private async Task<AgentSendResult> InterruptAndRestartTurnAsync(
        IReadOnlyList<AgentUserInput> userInputs,
        CancellationToken cancellationToken,
        string? correlationId,
        InteractionEnvelopeRef? interactionEnvelope)
    {
        var previewText = BuildUserInputPreview(userInputs) ?? string.Empty;
        var imageCount = CountUserInputImages(userInputs);
        var interruptedTurnId = ResolveCurrentTurnControlId();
        var interruptedThreadId = Normalize(activeThreadId);
        if (!string.IsNullOrWhiteSpace(interruptedTurnId) && !string.IsNullOrWhiteSpace(interruptedThreadId))
        {
            RaisePendingFollowUpLifecycle(
                correlationId,
                FollowUpMode.Interrupt,
                FollowUpMode.Interrupt,
                "interrupt_requested",
                interruptedTurnId,
                interruptedTurnId,
                previewText,
                imageCount,
                userInputs);
            try
            {
                await RequestInterruptAsync(interruptedThreadId!, interruptedTurnId!, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RaisePendingFollowUpLifecycle(
                    correlationId,
                    FollowUpMode.Interrupt,
                    FollowUpMode.Interrupt,
                    "interrupt_failed",
                    interruptedTurnId,
                    interruptedTurnId,
                    previewText,
                    imageCount,
                    userInputs);
                return AgentSendResult.Fail(
                    $"中断当前回合失败：{ex.Message}",
                    turnId: interruptedTurnId,
                    turnStatus: "interrupting",
                    correlationId: correlationId,
                    requestedMode: FollowUpMode.Interrupt,
                    effectiveMode: FollowUpMode.Interrupt);
            }

            var completion = await WaitTurnCompletionAsync(interruptedTurnId!, cancellationToken).ConfigureAwait(false);
            if (completion is null)
            {
                RaisePendingFollowUpLifecycle(
                    correlationId,
                    FollowUpMode.Interrupt,
                    FollowUpMode.Interrupt,
                    "interrupt_timeout",
                    interruptedTurnId,
                    interruptedTurnId,
                    previewText,
                    imageCount,
                    userInputs);
                return AgentSendResult.Fail(
                    $"中断当前回合超时：等待回合 {interruptedTurnId} 结束失败。",
                    turnId: interruptedTurnId,
                    turnStatus: "interrupting",
                    correlationId: correlationId,
                    requestedMode: FollowUpMode.Interrupt,
                    effectiveMode: FollowUpMode.Interrupt);
            }

            RaisePendingFollowUpLifecycle(
                correlationId,
                FollowUpMode.Interrupt,
                FollowUpMode.Interrupt,
                "interrupt_completed",
                interruptedTurnId,
                interruptedTurnId,
                previewText,
                imageCount,
                userInputs);
        }

        var restartResult = await StartTurnAsync(
                userInputs,
                waitForCompletion: false,
                Array.Empty<ConversationMessage>(),
                cancellationToken,
                correlationId: correlationId,
                requestedMode: FollowUpMode.Interrupt,
                effectiveMode: FollowUpMode.Queue,
                interactionEnvelope: interactionEnvelope)
            .ConfigureAwait(false);
        return TrackPendingFollowUpCommit(restartResult, userInputs, previewText, imageCount, interruptedTurnId);
    }

    public Task InterruptTurnAsync(CancellationToken cancellationToken)
        => InterruptAsync(cancellationToken);

    internal async Task InterruptAsync(CancellationToken cancellationToken)
    {
        var currentTurnId = ResolveCurrentTurnControlId();
        if (string.IsNullOrWhiteSpace(activeThreadId) || string.IsNullOrWhiteSpace(currentTurnId))
        {
            return;
        }

        var payload = new Dictionary<string, object?>
        {
            ["threadId"] = activeThreadId,
            ["turnId"] = currentTurnId,
        };

        try
        {
            await RequestInterruptAsync(activeThreadId, currentTurnId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // 中断失败不抛出，避免 UI 线程被异常打断。
        }
    }

    public async Task<bool> RespondToApprovalAsync(ControlPlaneApprovalResolution command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var normalized = Normalize(command.CallId.Value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var response = ToRuntimeApprovalResponse(command);
        var serializedApprovalResponse = providerServerRequestResponseSerializer.SerializeApprovalResponse(ToProviderApprovalOutcome(response));
        PendingInteractiveReplayContext? replayContext = null;

        if (pendingApprovalRequests.TryRemove(normalized, out var pendingApprovalRequest))
        {
            RemovePendingApprovalProjection(normalized);
            pendingApprovalRequest.TrySetResult(response);
            RemovePendingInteractiveReplayContext(normalized);
            return true;
        }

        if (TryGetPendingInteractiveReplayContext(normalized, out replayContext)
            && replayContext is not null)
        {
            var replayPayload = ResolveApprovalServerRequestPayload(replayContext.RequestMethod, serializedApprovalResponse);
            if (await TryRespondToPendingInteractiveReplayAsync(
                    normalized,
                    replayPayload.ToPlainObject(),
                    cancellationToken).ConfigureAwait(false))
            {
                pendingApprovals.TryRemove(normalized, out _);
                RemovePendingApprovalProjection(normalized);
                return true;
            }
        }

        pendingApprovals.TryGetValue(normalized, out var context);
        var payload = new Dictionary<string, object?>
        {
            ["callId"] = normalized,
            ["approved"] = response.IsApproved,
            ["decision"] = serializedApprovalResponse.DecisionPayload.ToPlainObject(),
        };

        AddIfPresent(payload, "threadId", context?.ThreadId ?? activeThreadId);
        AddIfPresent(payload, "turnId", context?.TurnId);
        if (replayContext is not null)
        {
            payload["requestId"] = replayContext.RequestIdValue;
        }
        AddIfPresent(payload, "reason", response.Note);
        AddIfPresent(payload, "note", response.Note);

        foreach (var method in new[] { "turn/approval/respond", "turn/approveToolCall", "turn/approve" })
        {
            try
            {
                _ = await SendRpcAsync(method, payload, cancellationToken, TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                pendingApprovals.TryRemove(normalized, out _);
                RemovePendingApprovalProjection(normalized);
                RemovePendingInteractiveReplayContext(normalized);
                return true;
            }
            catch
            {
                // 尝试下一个兼容方法。
            }
        }

        return false;
    }

    public async Task<bool> RespondToPermissionRequestAsync(ControlPlanePermissionGrant command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = Normalize(command.CallId.Value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var response = ToRuntimePermissionGrantResponse(command);
        var serializedPermissionResponse = providerServerRequestResponseSerializer.SerializePermissionResponse(ToProviderPermissionGrantOutcome(response ?? PermissionGrantResponse.EmptyTurn));
        if (!pendingPermissionRequests.TryRemove(normalized, out var pendingRequest))
        {
            var result = await TryRespondToPendingInteractiveReplayAsync(
                normalized,
                serializedPermissionResponse.Payload.ToPlainObject(),
                cancellationToken).ConfigureAwait(false);
            if (result)
            {
                RemovePendingApprovalProjection(normalized);
            }

            return result;
        }

        RemovePendingApprovalProjection(normalized);
        pendingRequest.TrySetResult(response ?? PermissionGrantResponse.EmptyTurn);
        RemovePendingInteractiveReplayContext(normalized);
        return true;
    }

    public async Task<bool> RespondToUserInputAsync(ControlPlaneUserInputSubmission command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = Normalize(command.CallId.Value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var submission = ToRuntimeUserInputSubmission(command);
        var serializedUserInputResponse = providerServerRequestResponseSerializer.SerializeUserInputResponse(ToProviderUserInputOutcome(submission ?? UserInputSubmission.Empty));
        if (!pendingUserInputRequests.TryRemove(normalized, out var pendingRequest))
        {
            var replayPayload = TryGetPendingInteractiveReplayContext(normalized, out var replayContext)
                && replayContext is not null
                    ? ResolveUserInputServerRequestPayload(replayContext.RequestMethod, serializedUserInputResponse)
                    : serializedUserInputResponse.ToolRequestPayload;
            var result = await TryRespondToPendingInteractiveReplayAsync(
                normalized,
                replayPayload.ToPlainObject(),
                cancellationToken).ConfigureAwait(false);
            if (result)
            {
                RemovePendingUserInputProjection(normalized);
            }

            return result;
        }

        RemovePendingUserInputProjection(normalized);
        pendingRequest.TrySetResult(submission ?? UserInputSubmission.Empty);
        RemovePendingInteractiveReplayContext(normalized);
        return true;
    }

    public async Task<ControlPlaneThreadListResult> ListThreadsAsync(ControlPlaneThreadListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var result = await ListThreadsCoreAsync(query, cancellationToken).ConfigureAwait(false);
        return ToControlPlaneThreadListResult(result);
    }

    public async Task<IReadOnlyList<ControlPlaneThreadSummary>> ListThreadsAsync(int limit, bool archived, bool matchCurrentCwd, CancellationToken cancellationToken)
    {
        var result = await ListThreadsCoreAsync(
                new ControlPlaneThreadListQuery
                {
                    Limit = limit,
                    Archived = archived,
                    WorkingDirectory = matchCurrentCwd ? ResolveWorkingDirectory(options) : null,
                    SortKey = "updated_at",
                },
                cancellationToken)
            .ConfigureAwait(false);
        return result.Data.Select(ToControlPlaneThreadSummary).ToArray();
    }

    private async Task<AgentThreadListResult> ListThreadsCoreAsync(ControlPlaneThreadListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (process is null || stdin is null)
        {
            return new AgentThreadListResult();
        }

        var payload = new Dictionary<string, object?>
        {
            ["limit"] = Math.Clamp(query.Limit, 1, 200),
            ["sortKey"] = Normalize(query.SortKey) ?? "created_at",
            ["archived"] = query.Archived,
        };

        var cursor = Normalize(query.Cursor);
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            payload["cursor"] = cursor;
        }

        var cwd = Normalize(query.WorkingDirectory);
        if (!string.IsNullOrWhiteSpace(cwd))
        {
            payload["cwd"] = cwd;
        }

        var modelProviders = query.ModelProviders
            .Select(Normalize)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (modelProviders.Length > 0)
        {
            payload["modelProviders"] = modelProviders;
        }

        var sourceKinds = query.SourceKinds
            .Where(static value => value is not null)
            .Select(static value => value!.ToProtocolString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sourceKinds.Length > 0)
        {
            payload["sourceKinds"] = sourceKinds;
        }

        var searchTerm = Normalize(query.SearchTerm);
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            payload["searchTerm"] = searchTerm;
        }

        var result = await SendRpcAsync("thread/list", payload, cancellationToken, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        return ParseAgentThreadListResult(result);
    }

    private async Task AppendSessionProjectionsAsync(
        List<SessionOverviewProjection> projections,
        bool archived,
        ListSessions query,
        CancellationToken cancellationToken)
    {
        string? cursor = null;
        do
        {
            var page = await ListThreadsCoreAsync(
                    new ControlPlaneThreadListQuery
                    {
                        Limit = 200,
                        Cursor = cursor,
                        Archived = archived,
                        SortKey = "updated_at",
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            foreach (var thread in page.Data)
            {
                var projection = ToSessionOverviewProjection(thread.SessionState);
                if (projection is null)
                {
                    continue;
                }

                if (query.CollaborationSpaceId is not null
                    && !string.Equals(
                        projection.CollaborationSpace.Id.Value,
                        query.CollaborationSpaceId.Value,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (!query.IncludeClosed && projection.IsClosed)
                {
                    continue;
                }

                projections.Add(projection);
            }

            cursor = Normalize(page.NextCursor);
        }
        while (!string.IsNullOrWhiteSpace(cursor));
    }

    private static AgentThreadListResult ParseAgentThreadListResult(JsonElement result)
    {
        var response = TryDeserializeThreadResponse(result);
        if (response?.Data is null || response.Data.Count == 0)
        {
            return new AgentThreadListResult();
        }

        var list = new List<AgentThreadInfo>();
        foreach (var item in response.Data)
        {
            var threadId = Normalize(item.Id);
            if (string.IsNullOrWhiteSpace(threadId))
            {
                continue;
            }

            list.Add(BuildAgentThreadInfo(item, threadId));
        }

        return new AgentThreadListResult
        {
            Data = list,
            NextCursor = response.NextCursor,
        };
    }

    private static AppServerThreadResponseDto? TryDeserializeThreadResponse(JsonElement result)
    {
        try
        {
            return AppServerJsonHelpers.Deserialize<AppServerThreadResponseDto>(result);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AppServerThreadSessionConfigurationDto? TryDeserializeThreadSessionConfiguration(JsonElement result)
    {
        try
        {
            return AppServerJsonHelpers.Deserialize<AppServerThreadSessionConfigurationDto>(result);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<ControlPlaneThreadSummary?> StartNewThreadAsync(CancellationToken cancellationToken)
    {
        var result = await StartNewThreadCoreAsync(new ControlPlaneStartThreadCommand(), cancellationToken).ConfigureAwait(false);
        return result is null ? null : ToControlPlaneThreadSummary(result);
    }

    public async Task<ControlPlaneThreadSummary?> StartNewThreadAsync(ControlPlaneStartThreadCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var result = await StartNewThreadCoreAsync(command, cancellationToken).ConfigureAwait(false);
        return result is null ? null : ToControlPlaneThreadSummary(result);
    }

    private async Task<AgentThreadInfo?> StartNewThreadCoreAsync(ControlPlaneStartThreadCommand command, CancellationToken cancellationToken)
    {
        if (process is null || stdin is null)
        {
            return null;
        }

        var result = await SendRpcAsync(
                "thread/start",
                BuildThreadStartParams(command, options),
                cancellationToken,
                TimeSpan.FromSeconds(30))
            .ConfigureAwait(false);
        var response = TryDeserializeThreadResponse(result);
        if (response?.Thread is null)
        {
            return null;
        }

        var thread = response.Thread;
        var threadId = Normalize(thread.Id);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            return null;
        }

        activeThreadId = threadId;
        activeTurnId = null;
        submittedTurnId = null;
        return BuildAgentThreadInfo(
            thread,
            threadId,
            ParseThreadSessionConfiguration(result, TryGetObject(result, "thread"), response, thread));
    }

    private async Task<string?> EnsureActiveThreadAsync(CancellationToken cancellationToken)
    {
        var currentThreadId = Normalize(activeThreadId);
        if (!string.IsNullOrWhiteSpace(currentThreadId))
        {
            return currentThreadId;
        }

        if (process is null || stdin is null)
        {
            return null;
        }

        var result = await SendRpcAsync(
                "thread/start",
                BuildThreadStartParams(new ControlPlaneStartThreadCommand(), options),
                cancellationToken,
                TimeSpan.FromSeconds(30))
            .ConfigureAwait(false);
        var response = TryDeserializeThreadResponse(result);
        if (response?.Thread is null)
        {
            return null;
        }

        currentThreadId = Normalize(response.Thread.Id);
        if (string.IsNullOrWhiteSpace(currentThreadId))
        {
            return null;
        }

        activeThreadId = currentThreadId;
        activeTurnId = null;
        submittedTurnId = null;
        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.Info,
            ThreadId = ToThreadId(currentThreadId),
            Message = $"已按需建立线程：{currentThreadId}",
        });

        return currentThreadId;
    }

    public async Task<ControlPlaneThreadSummary?> ForkThreadAsync(string threadId, CancellationToken cancellationToken)
    {
        var result = await ForkThreadCoreAsync(
                new ControlPlaneForkThreadCommand
                {
                    ThreadId = new ThreadId(threadId),
                },
                cancellationToken)
            .ConfigureAwait(false);
        return result is null ? null : ToControlPlaneThreadSummary(result);
    }

    public async Task<ControlPlaneThreadSummary?> ForkThreadAsync(ControlPlaneForkThreadCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var result = await ForkThreadCoreAsync(command, cancellationToken).ConfigureAwait(false);
        return result is null ? null : ToControlPlaneThreadSummary(result);
    }

    private async Task<AgentThreadInfo?> ForkThreadCoreAsync(ControlPlaneForkThreadCommand command, CancellationToken cancellationToken)
    {
        var normalized = Normalize(command.ThreadId.Value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (process is null || stdin is null)
        {
            return null;
        }

        var result = await SendRpcAsync(
                "thread/fork",
                BuildThreadForkParams(
                    new ControlPlaneForkThreadCommand
                    {
                        ThreadId = new ThreadId(normalized),
                        Path = command.Path,
                        Model = command.Model,
                        ModelProvider = command.ModelProvider,
                        ServiceTier = command.ServiceTier,
                        WorkingDirectory = command.WorkingDirectory,
                        ApprovalPolicy = command.ApprovalPolicy,
                        SandboxMode = command.SandboxMode,
                        Configuration = command.Configuration,
                        BaseInstructions = command.BaseInstructions,
                        DeveloperInstructions = command.DeveloperInstructions,
                        Ephemeral = command.Ephemeral,
                        PersistExtendedHistory = command.PersistExtendedHistory,
                    },
                    options),
                cancellationToken,
                TimeSpan.FromSeconds(30))
            .ConfigureAwait(false);
        var response = TryDeserializeThreadResponse(result);
        if (response?.Thread is null)
        {
            return null;
        }

        var thread = response.Thread;
        var resolvedThreadId = Normalize(thread.Id);
        if (string.IsNullOrWhiteSpace(resolvedThreadId))
        {
            return null;
        }

        activeThreadId = resolvedThreadId;
        activeTurnId = null;
        submittedTurnId = null;
        return BuildAgentThreadInfo(
            thread,
            resolvedThreadId,
            ParseThreadSessionConfiguration(result, TryGetObject(result, "thread"), response, thread));
    }

    public async Task<bool> ArchiveThreadAsync(string threadId, CancellationToken cancellationToken)
    {
        var normalized = Normalize(threadId);
        if (string.IsNullOrWhiteSpace(normalized) || process is null || stdin is null)
        {
            return false;
        }

        var payload = new Dictionary<string, object?>
        {
            ["threadId"] = normalized,
        };

        await SendRpcAsync("thread/archive", payload, cancellationToken, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        if (string.Equals(activeThreadId, normalized, StringComparison.Ordinal))
        {
            activeTurnId = null;
            submittedTurnId = null;
        }

        return true;
    }

    public async Task<bool> DeleteThreadAsync(string threadId, CancellationToken cancellationToken)
    {
        var normalized = Normalize(threadId);
        if (string.IsNullOrWhiteSpace(normalized) || process is null || stdin is null)
        {
            return false;
        }

        var payload = new Dictionary<string, object?>
        {
            ["threadId"] = normalized,
        };

        await SendRpcAsync("thread/delete", payload, cancellationToken, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        RemovePendingFollowUpCommitThread(normalized);
        RemovePendingInputStateThread(normalized);
        RemoveObservedTurnsThread(normalized);
        if (string.Equals(activeThreadId, normalized, StringComparison.Ordinal))
        {
            activeThreadId = null;
            activeTurnId = null;
            submittedTurnId = null;
        }

        return true;
    }

    public async Task<ControlPlaneClearThreadsResult> ClearThreadsAsync(CancellationToken cancellationToken)
    {
        if (process is null || stdin is null)
        {
            return new ControlPlaneClearThreadsResult();
        }

        var result = await SendRpcAsync("thread/clear", new { }, cancellationToken, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        var deletedCount = 0;
        if (result.ValueKind == JsonValueKind.Object
            && result.TryGetProperty("deletedCount", out var deletedCountElement)
            && deletedCountElement.ValueKind == JsonValueKind.Number)
        {
            _ = deletedCountElement.TryGetInt32(out deletedCount);
        }

        ClearThreadLocalState();
        activeThreadId = null;
        activeTurnId = null;
        submittedTurnId = null;

        return new ControlPlaneClearThreadsResult
        {
            DeletedCount = deletedCount,
        };
    }

    public async Task<bool> RenameThreadAsync(string threadId, string name, CancellationToken cancellationToken)
    {
        var normalizedThreadId = Normalize(threadId);
        var normalizedName = Normalize(name);
        if (string.IsNullOrWhiteSpace(normalizedThreadId) || string.IsNullOrWhiteSpace(normalizedName) || process is null || stdin is null)
        {
            return false;
        }

        var payload = new Dictionary<string, object?>
        {
            ["threadId"] = normalizedThreadId,
            ["name"] = normalizedName,
        };

        await SendRpcAsync("thread/name/set", payload, cancellationToken, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        return true;
    }

    public async Task<ControlPlaneThreadSnapshot?> ResumeThreadAsync(string threadId, CancellationToken cancellationToken)
    {
        var result = await ResumeThreadCoreAsync(
                new ControlPlaneResumeThreadCommand
                {
                    ThreadId = new ThreadId(threadId),
                },
                cancellationToken)
            .ConfigureAwait(false);
        return result is null ? null : ToControlPlaneThreadSnapshot(result);
    }

    public async Task<ControlPlaneThreadSnapshot?> ResumeThreadAsync(ControlPlaneResumeThreadCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var result = await ResumeThreadCoreAsync(command, cancellationToken).ConfigureAwait(false);
        return result is null ? null : ToControlPlaneThreadSnapshot(result);
    }

    private async Task<AgentThreadResumeResult?> ResumeThreadCoreAsync(ControlPlaneResumeThreadCommand command, CancellationToken cancellationToken)
    {
        var normalized = Normalize(command.ThreadId.Value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (process is null || stdin is null)
        {
            return null;
        }

        var result = await SendRpcAsync(
            "thread/resume",
            BuildThreadResumeParams(
                new ControlPlaneResumeThreadCommand
                {
                    ThreadId = new ThreadId(normalized),
                    History = command.History,
                    Path = command.Path,
                    Model = command.Model,
                    ModelProvider = command.ModelProvider,
                    ServiceTier = command.ServiceTier,
                    WorkingDirectory = command.WorkingDirectory,
                    ApprovalPolicy = command.ApprovalPolicy,
                    SandboxMode = command.SandboxMode,
                    Configuration = command.Configuration,
                    BaseInstructions = command.BaseInstructions,
                    DeveloperInstructions = command.DeveloperInstructions,
                    Personality = command.Personality,
                    PersistExtendedHistory = command.PersistExtendedHistory,
                },
                options),
            cancellationToken,
            TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        var response = TryDeserializeThreadResponse(result);
        if (response?.Thread is null)
        {
            return null;
        }

        var threadElement = TryGetObject(result, "thread");
        if (!threadElement.HasValue)
        {
            return null;
        }

        var thread = threadElement.Value;
        var parsedThread = ParseThreadDetails(thread);
        if (parsedThread is null)
        {
            return null;
        }

        var resolvedThreadId = Normalize(response.Thread.Id)
                               ?? Normalize(parsedThread.Id)
                               ?? normalized;
        activeThreadId = resolvedThreadId;
        activeTurnId = ShouldRestoreResumedActiveTurn(parsedThread.Status)
            ? ResolveResumedActiveTurnId(parsedThread.Turns)
            : null;
        submittedTurnId = null;
        var pendingInputState = HydratePendingInputStateSnapshot(
            resolvedThreadId,
            parsedThread.PendingInputState);
        var pendingInteractiveRequests = HydratePendingInteractiveReplayContexts(
                ToPendingInteractiveRequestReplays(parsedThread.PendingInteractiveRequests),
                thread)
            .Select(ToControlPlanePendingInteractiveRequest)
            .ToArray();
        var sessionConfiguration = ParseThreadSessionConfiguration(result, thread, response, response.Thread)
                                   ?? parsedThread.SessionConfiguration;
        var messages = ParseThreadConversationMessages(
            response,
            response.Thread,
            result,
            thread,
            parsedThread.SeedHistory,
            parsedThread.Turns);

        return new AgentThreadResumeResult
        {
            ThreadId = resolvedThreadId,
            Preview = parsedThread.Preview,
            Name = parsedThread.Name,
            Cwd = parsedThread.Cwd,
            Path = parsedThread.Path,
            ModelProvider = parsedThread.ModelProvider,
            Source = parsedThread.Source,
            CliVersion = parsedThread.CliVersion,
            AgentNickname = parsedThread.AgentNickname,
            AgentRole = parsedThread.AgentRole,
            CreatedAt = parsedThread.CreatedAt,
            UpdatedAt = parsedThread.UpdatedAt,
            IsEphemeral = parsedThread.Ephemeral,
            Status = parsedThread.Status,
            GitInfo = parsedThread.GitInfo,
            Turns = parsedThread.Turns,
            SeedHistory = parsedThread.SeedHistory,
            Messages = messages,
            PendingInputState = pendingInputState,
            PendingInteractiveRequests = pendingInteractiveRequests,
            SessionConfiguration = sessionConfiguration,
        };
    }

    public async Task<ControlPlaneThreadOperationResult> ReadThreadAsync(ControlPlaneReadThreadQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var normalizedThreadId = Normalize(query.ThreadId.Value);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            throw new ArgumentException("threadId 不能为空。", nameof(query));
        }

        var payload = new Dictionary<string, object?>
        {
            ["threadId"] = normalizedThreadId,
        };
        if (query.IncludeTurns)
        {
            payload["includeTurns"] = true;
        }

        var result = await InvokeRpcAsync("thread/read", payload, cancellationToken).ConfigureAwait(false);
        return ParseThreadOperationResult(result);
    }

    public async Task<SessionOverviewProjection?> GetSessionOverviewAsync(GetSessionOverview query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var normalizedSessionId = Normalize(query.SessionId.Value);
        if (string.IsNullOrWhiteSpace(normalizedSessionId))
        {
            throw new ArgumentException("sessionId 不能为空。", nameof(query));
        }

        var result = await InvokeRpcAsync(
                "thread/read",
                new Dictionary<string, object?>
                {
                    ["threadId"] = normalizedSessionId,
                    ["includeTurns"] = false,
                },
                cancellationToken)
            .ConfigureAwait(false);
        var response = TryDeserializeThreadResponse(result);
        if (response?.Thread is null)
        {
            return null;
        }

        var detail = ParseThreadDetails(response.Thread, response);
        return ToSessionOverviewProjection(detail?.SessionState);
    }

    public async Task<IReadOnlyList<SessionOverviewProjection>> ListSessionsAsync(ListSessions query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var projections = new List<SessionOverviewProjection>();
        await AppendSessionProjectionsAsync(projections, archived: false, query, cancellationToken).ConfigureAwait(false);
        if (query.IncludeClosed)
        {
            await AppendSessionProjectionsAsync(projections, archived: true, query, cancellationToken).ConfigureAwait(false);
        }

        return projections;
    }

    private static string? ResolveResumedActiveTurnId(IReadOnlyList<AgentThreadTurn> turns)
    {
        for (var index = turns.Count - 1; index >= 0; index--)
        {
            var turn = turns[index];
            var turnId = Normalize(turn.Id);
            if (string.IsNullOrWhiteSpace(turnId) || IsTerminalResumedTurnStatus(turn.Status))
            {
                continue;
            }

            return turnId;
        }

        return null;
    }

    private static bool IsTerminalResumedTurnStatus(string? status)
        => Normalize(status)?.ToLowerInvariant() switch
        {
            "completed" or "failed" or "errored" or "cancelled" or "canceled" or "interrupted" => true,
            _ => false,
        };

    private static bool ShouldRestoreResumedActiveTurn(AgentThreadStatus? threadStatus)
    {
        var normalizedStatusType = Normalize(threadStatus?.Type);
        return string.IsNullOrWhiteSpace(normalizedStatusType)
               || string.Equals(normalizedStatusType, "active", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ControlPlaneLoadedThreadListResult> ListLoadedThreadsAsync(ControlPlaneLoadedThreadListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var payload = new Dictionary<string, object?>();
        if (query.Limit is > 0)
        {
            payload["limit"] = query.Limit.Value;
        }

        var normalizedCursor = Normalize(query.Cursor);
        if (!string.IsNullOrWhiteSpace(normalizedCursor))
        {
            payload["cursor"] = normalizedCursor;
        }

        var result = await InvokeRpcAsync("thread/loaded/list", payload, cancellationToken).ConfigureAwait(false);
        return ParseThreadLoadedListResult(result);
    }

    public async Task<ControlPlaneThreadCommandAcceptedResult> CompactThreadAsync(
        ControlPlaneCompactThreadCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var normalizedThreadId = Normalize(command.ThreadId.Value);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            throw new ArgumentException("threadId 不能为空。", nameof(command));
        }

        await InvokeRpcAsync(
                "thread/compact/start",
                new Dictionary<string, object?>
                {
                    ["threadId"] = normalizedThreadId,
                    ["keepRecentTurns"] = Math.Max(1, command.KeepRecentTurns),
                },
                cancellationToken)
            .ConfigureAwait(false);

        return new ControlPlaneThreadCommandAcceptedResult();
    }

    public async Task<ControlPlaneThreadCommandAcceptedResult> CleanBackgroundTerminalsAsync(
        ControlPlaneCleanBackgroundTerminalsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var normalizedThreadId = Normalize(command.ThreadId.Value);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            throw new ArgumentException("threadId 不能为空。", nameof(command));
        }

        await InvokeRpcAsync(
                "thread/backgroundTerminals/clean",
                new Dictionary<string, object?>
                {
                    ["threadId"] = normalizedThreadId,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return new ControlPlaneThreadCommandAcceptedResult();
    }

    public async Task<ControlPlaneThreadUnsubscribeResult> UnsubscribeThreadAsync(
        ControlPlaneUnsubscribeThreadCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var normalizedThreadId = Normalize(command.ThreadId.Value);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            throw new ArgumentException("threadId 不能为空。", nameof(command));
        }

        var result = await InvokeRpcAsync(
                "thread/unsubscribe",
                new Dictionary<string, object?>
                {
                    ["threadId"] = normalizedThreadId,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return ParseThreadUnsubscribeResult(result);
    }

    public async Task<ControlPlaneThreadElicitationResult> IncrementThreadElicitationAsync(
        ControlPlaneIncrementThreadElicitationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var normalizedThreadId = Normalize(command.ThreadId.Value);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            throw new ArgumentException("threadId 不能为空。", nameof(command));
        }

        var result = await InvokeRpcAsync(
                "thread/increment_elicitation",
                new Dictionary<string, object?>
                {
                    ["threadId"] = normalizedThreadId,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return ParseThreadElicitationResult(result);
    }

    public async Task<ControlPlaneThreadElicitationResult> DecrementThreadElicitationAsync(
        ControlPlaneDecrementThreadElicitationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var normalizedThreadId = Normalize(command.ThreadId.Value);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            throw new ArgumentException("threadId 不能为空。", nameof(command));
        }

        var result = await InvokeRpcAsync(
                "thread/decrement_elicitation",
                new Dictionary<string, object?>
                {
                    ["threadId"] = normalizedThreadId,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return ParseThreadElicitationResult(result);
    }

    public async Task<ControlPlaneThreadOperationResult> UnarchiveThreadAsync(
        ControlPlaneUnarchiveThreadCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var normalizedThreadId = Normalize(command.ThreadId.Value);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            throw new ArgumentException("threadId 不能为空。", nameof(command));
        }

        var result = await InvokeRpcAsync(
                "thread/unarchive",
                new Dictionary<string, object?>
                {
                    ["threadId"] = normalizedThreadId,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return ParseThreadOperationResult(result);
    }

    public async Task<ControlPlaneThreadOperationResult> UpdateThreadMetadataAsync(
        ControlPlaneUpdateThreadMetadataCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var normalizedThreadId = Normalize(command.ThreadId.Value);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            throw new ArgumentException("threadId 不能为空。", nameof(command));
        }

        var gitInfo = new Dictionary<string, object?>();
        if (command.HasGitSha)
        {
            gitInfo["sha"] = Normalize(command.GitSha);
        }

        if (command.HasGitBranch)
        {
            gitInfo["branch"] = Normalize(command.GitBranch);
        }

        if (command.HasGitOriginUrl)
        {
            gitInfo["originUrl"] = Normalize(command.GitOriginUrl);
        }

        var result = await InvokeRpcAsync(
                "thread/metadata/update",
                new Dictionary<string, object?>
                {
                    ["threadId"] = normalizedThreadId,
                    ["gitInfo"] = gitInfo,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return ParseThreadOperationResult(result);
    }

    public async Task<ControlPlaneThreadOperationResult> RollbackThreadAsync(
        ControlPlaneRollbackThreadCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var normalizedThreadId = Normalize(command.ThreadId.Value);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            throw new ArgumentException("threadId 不能为空。", nameof(command));
        }

        var result = await InvokeRpcAsync(
                "thread/rollback",
                new Dictionary<string, object?>
                {
                    ["threadId"] = normalizedThreadId,
                    ["numTurns"] = Math.Max(1, command.NumTurns),
                },
                cancellationToken)
            .ConfigureAwait(false);

        RemovePendingFollowUpCommitThread(normalizedThreadId);
        RemovePendingInputStateThread(normalizedThreadId);
        RemoveObservedTurnsThread(normalizedThreadId);
        ClearThreadPendingInteractiveState(normalizedThreadId);
        if (string.Equals(activeThreadId, normalizedThreadId, StringComparison.Ordinal))
        {
            activeTurnId = null;
            submittedTurnId = null;
        }

        pendingInputPersistenceVersionsByThread.TryRemove(normalizedThreadId, out _);
        return ParseThreadOperationResult(result);
    }

    public async Task<ControlPlaneRealtimeCommandAcceptedResult> StartRealtimeAsync(ControlPlaneRealtimeStartCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var normalizedThreadId = Normalize(command.ThreadId.Value);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            throw new ArgumentException("threadId 不能为空。", nameof(command));
        }

        var payload = new Dictionary<string, object?>
        {
            ["threadId"] = normalizedThreadId,
        };
        var sessionId = Normalize(command.SessionId);
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            payload["sessionId"] = sessionId;
        }

        var prompt = Normalize(command.Prompt);
        if (!string.IsNullOrWhiteSpace(prompt))
        {
            payload["prompt"] = prompt;
        }

        await InvokeRpcAsync("thread/realtime/start", payload, cancellationToken).ConfigureAwait(false);
        return new ControlPlaneRealtimeCommandAcceptedResult();
    }

    public async Task<ControlPlaneRealtimeCommandAcceptedResult> AppendRealtimeTextAsync(ControlPlaneRealtimeAppendTextCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var normalizedThreadId = Normalize(command.ThreadId.Value);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            throw new ArgumentException("threadId 不能为空。", nameof(command));
        }

        var text = Normalize(command.Text);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("text 不能为空。", nameof(command));
        }

        var payload = new Dictionary<string, object?>
        {
            ["threadId"] = normalizedThreadId,
            ["text"] = text,
        };
        var sessionId = Normalize(command.SessionId);
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            payload["sessionId"] = sessionId;
        }

        await InvokeRpcAsync("thread/realtime/appendText", payload, cancellationToken).ConfigureAwait(false);
        return new ControlPlaneRealtimeCommandAcceptedResult();
    }

    public async Task<ControlPlaneRealtimeCommandAcceptedResult> AppendRealtimeAudioAsync(ControlPlaneRealtimeAppendAudioCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Audio);

        var normalizedThreadId = Normalize(command.ThreadId.Value);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            throw new ArgumentException("threadId 不能为空。", nameof(command));
        }

        var audioData = Normalize(command.Audio.Data);
        if (string.IsNullOrWhiteSpace(audioData))
        {
            throw new ArgumentException("audio.data 不能为空。", nameof(command));
        }

        var payload = new Dictionary<string, object?>
        {
            ["threadId"] = normalizedThreadId,
            ["audio"] = new Dictionary<string, object?>
            {
                ["data"] = audioData,
                ["sampleRate"] = command.Audio.SampleRate,
                ["numChannels"] = command.Audio.NumChannels,
                ["samplesPerChannel"] = command.Audio.SamplesPerChannel,
            },
        };
        var sessionId = Normalize(command.SessionId);
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            payload["sessionId"] = sessionId;
        }

        await InvokeRpcAsync("thread/realtime/appendAudio", payload, cancellationToken).ConfigureAwait(false);
        return new ControlPlaneRealtimeCommandAcceptedResult();
    }

    public async Task<ControlPlaneRealtimeCommandAcceptedResult> HandoffRealtimeOutputAsync(ControlPlaneRealtimeHandoffOutputCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var normalizedThreadId = Normalize(command.ThreadId.Value);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            throw new ArgumentException("threadId 不能为空。", nameof(command));
        }

        var handoffId = Normalize(command.HandoffId);
        if (string.IsNullOrWhiteSpace(handoffId))
        {
            throw new ArgumentException("handoffId 不能为空。", nameof(command));
        }

        var payload = new Dictionary<string, object?>
        {
            ["threadId"] = normalizedThreadId,
            ["handoffId"] = handoffId,
            ["output"] = Normalize(command.Output) ?? string.Empty,
        };
        var sessionId = Normalize(command.SessionId);
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            payload["sessionId"] = sessionId;
        }

        await InvokeRpcAsync("thread/realtime/handoffOutput", payload, cancellationToken).ConfigureAwait(false);
        return new ControlPlaneRealtimeCommandAcceptedResult();
    }

    public async Task<ControlPlaneRealtimeCommandAcceptedResult> StopRealtimeAsync(ControlPlaneRealtimeStopCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var normalizedThreadId = Normalize(command.ThreadId.Value);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            throw new ArgumentException("threadId 不能为空。", nameof(command));
        }

        var payload = new Dictionary<string, object?>
        {
            ["threadId"] = normalizedThreadId,
        };
        var sessionId = Normalize(command.SessionId);
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            payload["sessionId"] = sessionId;
        }

        await InvokeRpcAsync("thread/realtime/stop", payload, cancellationToken).ConfigureAwait(false);
        return new ControlPlaneRealtimeCommandAcceptedResult();
    }

    private AgentThreadDetails SyncThreadPendingInputState(AgentThreadDetails thread, JsonElement? rawResult = null)
    {
        ArgumentNullException.ThrowIfNull(thread);

        thread.PendingInputState = HydratePendingInputStateSnapshot(
            thread.Id,
            thread.PendingInputState);
        var rawThread = rawResult.HasValue ? TryGetObject(rawResult.Value, "thread") : null;
        thread.PendingInteractiveRequests = HydratePendingInteractiveReplayContexts(
                ToPendingInteractiveRequestReplays(thread.PendingInteractiveRequests),
                rawThread)
            .Select(ToControlPlanePendingInteractiveRequest)
            .ToArray();
        return thread;
    }

    private IReadOnlyList<PendingInteractiveRequestReplay> HydratePendingInteractiveReplayContexts(
        IReadOnlyList<PendingInteractiveRequestReplay>? requests,
        JsonElement? rawThread = null)
    {
        var rawContextsByCallId = ParsePendingInteractiveReplayContextSeeds(rawThread);
        if (requests is not { Count: > 0 } && rawContextsByCallId.Count == 0)
        {
            return Array.Empty<PendingInteractiveRequestReplay>();
        }

        var hydrated = new List<PendingInteractiveRequestReplay>(requests?.Count ?? 0);
        var seenCallIds = new HashSet<string>(StringComparer.Ordinal);

        if (requests is { Count: > 0 })
        {
            foreach (var request in requests)
            {
                var normalizedCallId = Normalize(request.CallId);
                if (string.IsNullOrWhiteSpace(normalizedCallId))
                {
                    continue;
                }

                var normalizedKind = Normalize(request.RequestKind) ?? string.Empty;
                object requestIdValue = string.IsNullOrWhiteSpace(request.RequestIdRaw)
                    ? request.RequestId
                    : request.RequestIdRaw!;
                var requestIdToken = CreateRequestIdToken(requestIdValue);
                long? numericRequestId = request.RequestId > 0 ? request.RequestId : null;
                if (rawContextsByCallId.TryGetValue(normalizedCallId, out var rawContext))
                {
                    requestIdValue = rawContext.RequestIdValue;
                    requestIdToken = rawContext.RequestIdToken;
                    numericRequestId = rawContext.NumericRequestId;
                    normalizedKind = Normalize(rawContext.RequestKind) ?? normalizedKind;
                }

                TrackPendingInteractiveReplayContext(
                    requestIdValue,
                    requestIdToken,
                    numericRequestId,
                    normalizedCallId,
                    Normalize(request.ThreadId),
                    Normalize(request.TurnId),
                    normalizedKind,
                    Normalize(request.ToolName),
                    Normalize(request.RequestMethod));
                seenCallIds.Add(normalizedCallId);

                hydrated.Add(new PendingInteractiveRequestReplay
                {
                    RequestId = numericRequestId ?? request.RequestId,
                    RequestIdRaw = requestIdValue as string,
                    RequestKind = normalizedKind,
                    RequestMethod = request.RequestMethod,
                    CallId = normalizedCallId,
                    ThreadId = Normalize(request.ThreadId),
                    TurnId = Normalize(request.TurnId),
                    ToolName = Normalize(request.ToolName),
                    ServerName = Normalize(request.ServerName),
                    Text = request.Text,
                    Status = request.Status,
                    Phase = request.Phase,
                    RequiresApproval = request.RequiresApproval,
                    ApprovalKind = request.ApprovalKind,
                    AvailableDecisions = request.AvailableDecisions,
                    AvailableDecisionOptions = request.AvailableDecisionOptions,
                    ApprovalRequest = request.ApprovalRequest,
                    PermissionRequest = request.PermissionRequest,
                    UserInputRequest = request.UserInputRequest,
                });
            }
        }

        foreach (var rawContext in rawContextsByCallId.Values)
        {
            if (seenCallIds.Contains(rawContext.CallId))
            {
                continue;
            }

            TrackPendingInteractiveReplayContext(
                rawContext.RequestIdValue,
                rawContext.RequestIdToken,
                rawContext.NumericRequestId,
                rawContext.CallId,
                rawContext.ThreadId,
                rawContext.TurnId,
                rawContext.RequestKind,
                rawContext.ToolName,
                rawContext.RequestMethod);
        }

        return hydrated;
    }

    private static Dictionary<string, PendingInteractiveReplaySeed> ParsePendingInteractiveReplayContextSeeds(JsonElement? rawThread)
    {
        var contextsByCallId = new Dictionary<string, PendingInteractiveReplaySeed>(StringComparer.Ordinal);
        if (!rawThread.HasValue
            || !TryGetArray(rawThread.Value, "pendingInteractiveRequests", out var requestsElement)
            || requestsElement.ValueKind != JsonValueKind.Array)
        {
            return contextsByCallId;
        }

        foreach (var requestElement in requestsElement.EnumerateArray())
        {
            if (requestElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var callId = Normalize(ReadString(requestElement, "callId"));
            if (string.IsNullOrWhiteSpace(callId)
                || !TryGetProperty(requestElement, "requestId", out var requestIdElement)
                || !TryResolveServerRequestId(requestIdElement, out var requestIdValue, out var requestIdToken, out var numericRequestId))
            {
                continue;
            }

            contextsByCallId[callId!] = new PendingInteractiveReplaySeed(
                CallId: callId!,
                RequestIdValue: requestIdValue,
                RequestIdToken: requestIdToken,
                NumericRequestId: numericRequestId,
                RequestKind: Normalize(ReadString(requestElement, "requestKind")) ?? string.Empty,
                ThreadId: Normalize(ReadString(requestElement, "threadId")),
                TurnId: Normalize(ReadString(requestElement, "turnId")),
                ToolName: Normalize(ReadString(requestElement, "toolName")),
                RequestMethod: Normalize(ReadString(requestElement, "requestMethod")));
        }

        return contextsByCallId;
    }

    private void TrackPendingInteractiveReplayContext(
        object requestIdValue,
        string requestIdToken,
        long? numericRequestId,
        string callId,
        string? threadId,
        string? turnId,
        string requestKind,
        string? toolName,
        string? requestMethod)
    {
        pendingInteractiveReplayContextsByCallId[callId] = new PendingInteractiveReplayContext(
            requestIdValue,
            requestIdToken,
            numericRequestId,
            callId,
            threadId,
            turnId,
            requestKind,
            toolName,
            requestMethod);
    }

    private bool TryGetPendingInteractiveReplayContext(string callId, out PendingInteractiveReplayContext? context)
        => pendingInteractiveReplayContextsByCallId.TryGetValue(callId, out context);

    private void RemovePendingInteractiveReplayContext(string? callId)
    {
        var normalizedCallId = Normalize(callId);
        if (string.IsNullOrWhiteSpace(normalizedCallId))
        {
            return;
        }

        pendingInteractiveReplayContextsByCallId.TryRemove(normalizedCallId, out _);
    }

    private async Task<bool> TryRespondToPendingInteractiveReplayAsync(
        string normalizedCallId,
        object? resultPayload,
        CancellationToken cancellationToken)
    {
        if (!pendingInteractiveReplayContextsByCallId.TryGetValue(normalizedCallId, out var context))
        {
            return false;
        }

        var payload = new Dictionary<string, object?>
        {
            ["requestId"] = context.RequestIdValue,
            ["callId"] = normalizedCallId,
            ["requestKind"] = context.RequestKind,
            ["result"] = resultPayload,
        };
        AddIfPresent(payload, "threadId", context.ThreadId ?? activeThreadId);
        AddIfPresent(payload, "turnId", context.TurnId);

        try
        {
            _ = await SendRpcAsync("serverRequest/respond", payload, cancellationToken, TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            RemovePendingInteractiveReplayContext(normalizedCallId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private Task<JsonElement> ExecuteRuntimeSurfaceAsync(string method, object? parameters, CancellationToken cancellationToken)
        => InvokeRpcAsync(method, parameters, cancellationToken);

    public async Task<ControlPlaneConfigSnapshotResult> ReadConfigAsync(ControlPlaneConfigReadQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var result = await ExecuteRuntimeSurfaceAsync(
                "config/read",
                new Dictionary<string, object?>
                {
                    ["cwd"] = Normalize(query.WorkingDirectory),
                    ["includeLayers"] = query.IncludeLayers,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return ParseConfigReadResult(result);
    }

    public async Task<ControlPlaneConfigRequirementsResult> ReadConfigRequirementsAsync(ControlPlaneConfigRequirementsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var result = await ExecuteRuntimeSurfaceAsync(
                "configRequirements/read",
                new Dictionary<string, object?>
                {
                    ["cwd"] = Normalize(query.WorkingDirectory),
                },
                cancellationToken)
            .ConfigureAwait(false);

        return ParseConfigRequirementsReadResult(result);
    }

    public async Task<ControlPlaneModelCatalogResult> ListModelsAsync(ControlPlaneModelCatalogQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await ExecuteRuntimeSurfaceAsync(
                "model/list",
                new Dictionary<string, object?>
                {
                    ["limit"] = Math.Clamp(request.Limit, 1, 200),
                    ["cursor"] = Normalize(request.Cursor),
                    ["includeHidden"] = request.IncludeHidden,
                    ["requireEndpoint"] = request.RequireEndpoint,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return ParseModelCatalogResult(result);
    }

    public async Task<CapabilityCatalogSnapshot> GetCapabilityCatalogAsync(
        GetCapabilityCatalog request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var configTask = ReadConfigAsync(
            new ControlPlaneConfigReadQuery
            {
                WorkingDirectory = request.WorkspacePath,
                IncludeLayers = false,
            },
            cancellationToken);
        var modelsTask = ListModelsAsync(
            new ControlPlaneModelCatalogQuery
            {
                Limit = request.ModelLimit is <= 0 ? 200 : request.ModelLimit,
                IncludeHidden = request.IncludeHiddenModels,
            },
            cancellationToken);
        var toolsTask = ReadResolvedToolCatalogAsync(request.WorkspacePath, request.IncludeHiddenTools, cancellationToken);

        await Task.WhenAll(configTask, modelsTask, toolsTask).ConfigureAwait(false);
        return ExecutionProviderCatalogResolver.BuildCapabilityCatalog(
            configTask.Result.Config,
            modelsTask.Result,
            request.IncludeHiddenModels,
            toolsTask.Result);
    }

    private async Task<ResolvedToolCatalogSnapshot> ReadResolvedToolCatalogAsync(
        string? workspacePath,
        bool includeHiddenTools,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await ExecuteRuntimeSurfaceAsync(
                    "tools/catalog/read",
                    new Dictionary<string, object?>
                    {
                        ["cwd"] = workspacePath,
                        ["includeHidden"] = includeHiddenTools,
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            return ParseResolvedToolCatalogSnapshot(result);
        }
        catch (AppServerRpcException ex) when (ex.Code == -32601)
        {
            return new ResolvedToolCatalogSnapshot();
        }
    }

    public async Task<ResolvedEngineBinding> ResolveEngineBindingAsync(
        ResolveEngineBinding request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var configTask = ReadConfigAsync(
            new ControlPlaneConfigReadQuery
            {
                WorkingDirectory = request.WorkspacePath,
                IncludeLayers = false,
            },
            cancellationToken);
        var modelsTask = ListModelsAsync(
            new ControlPlaneModelCatalogQuery
            {
                Limit = 200,
                IncludeHidden = true,
            },
            cancellationToken);

        await Task.WhenAll(configTask, modelsTask).ConfigureAwait(false);
        return ExecutionProviderCatalogResolver.ResolveEngineBinding(configTask.Result.Config, modelsTask.Result, request);
    }

    public async Task<ControlPlaneAppCatalogResult> ListAppsAsync(ControlPlaneAppCatalogQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = new Dictionary<string, object?>();
        if (request.Limit is > 0)
        {
            payload["limit"] = Math.Clamp(request.Limit.Value, 1, 200);
        }

        var cursor = Normalize(request.Cursor);
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            payload["cursor"] = cursor;
        }

        var threadId = Normalize(request.ThreadId?.Value);
        if (!string.IsNullOrWhiteSpace(threadId))
        {
            payload["threadId"] = threadId;
        }

        if (request.ForceRefetch)
        {
            payload["forceRefetch"] = true;
        }

        var result = await ExecuteRuntimeSurfaceAsync("app/list", payload, cancellationToken).ConfigureAwait(false);
        return ParseAppListResult(result);
    }

    public async Task<ControlPlaneConfigWriteResult> WriteConfigValueAsync(ControlPlaneConfigValueWriteCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var keyPath = Normalize(command.KeyPath);
        if (string.IsNullOrWhiteSpace(keyPath))
        {
            throw new ArgumentException("keyPath 不能为空。", nameof(command));
        }

        var result = await ExecuteRuntimeSurfaceAsync(
                "config/value/write",
                new Dictionary<string, object?>
                {
                    ["keyPath"] = keyPath,
                    ["value"] = command.Value?.ToPlainObject(),
                    ["mergeStrategy"] = Normalize(command.MergeStrategy) ?? "replace",
                    ["cwd"] = Normalize(command.WorkingDirectory),
                    ["filePath"] = Normalize(command.FilePath),
                    ["expectedVersion"] = Normalize(command.ExpectedVersion),
                },
                cancellationToken)
            .ConfigureAwait(false);

        return ParseConfigWriteResult(result);
    }

    public async Task<ControlPlaneConfigWriteResult> WriteConfigBatchAsync(ControlPlaneConfigBatchWriteCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var items = command.Items
            .Where(static item => !string.IsNullOrWhiteSpace(item.KeyPath))
            .Select(static item => new Dictionary<string, object?>
            {
                ["keyPath"] = item.KeyPath,
                ["value"] = item.Value?.ToPlainObject(),
                ["mergeStrategy"] = string.IsNullOrWhiteSpace(item.MergeStrategy) ? "replace" : item.MergeStrategy,
            })
            .ToArray();
        if (items.Length == 0)
        {
            throw new ArgumentException("items 不能为空。", nameof(command));
        }

        var result = await ExecuteRuntimeSurfaceAsync(
                "config/batchWrite",
                new Dictionary<string, object?>
                {
                    ["items"] = items,
                    ["cwd"] = Normalize(command.WorkingDirectory),
                    ["filePath"] = Normalize(command.FilePath),
                    ["expectedVersion"] = Normalize(command.ExpectedVersion),
                    ["reloadUserConfig"] = command.ReloadUserConfig,
                },
                cancellationToken)
            .ConfigureAwait(false);

        var writeResult = ParseConfigWriteResult(result);
        ApplyRuntimeDefaultsFromConfigBatch(command);
        return writeResult;
    }

    private void ApplyRuntimeDefaultsFromConfigBatch(ControlPlaneConfigBatchWriteCommand command)
    {
        if (!command.ReloadUserConfig)
        {
            return;
        }

        var activeProfile = Normalize(options.ProfileName);
        foreach (var item in command.Items)
        {
            var keyPath = Normalize(item.KeyPath);
            var value = Normalize(item.Value?.StringValue);
            if (keyPath is null || value is null)
            {
                continue;
            }

            if (IsRuntimeModelKeyPath(keyPath, activeProfile))
            {
                options.Model = value;
                continue;
            }

            if (IsRuntimeModelProviderKeyPath(keyPath, activeProfile))
            {
                options.ModelProvider = value;
            }
        }
    }

    private static bool IsRuntimeModelKeyPath(string keyPath, string? activeProfile)
        => string.Equals(keyPath, "model", StringComparison.OrdinalIgnoreCase)
           || IsActiveProfileKeyPath(keyPath, activeProfile, "model");

    private static bool IsRuntimeModelProviderKeyPath(string keyPath, string? activeProfile)
        => string.Equals(keyPath, "provider", StringComparison.OrdinalIgnoreCase)
           || IsActiveProfileKeyPath(keyPath, activeProfile, "provider");

    private static bool IsActiveProfileKeyPath(string keyPath, string? activeProfile, string leaf)
        => !string.IsNullOrWhiteSpace(activeProfile)
           && string.Equals(keyPath, $"profiles.{activeProfile}.{leaf}", StringComparison.OrdinalIgnoreCase);

    public async Task<ControlPlaneSkillCatalogResult> ListSkillsAsync(ControlPlaneSkillCatalogQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = new Dictionary<string, object?>();
        var workingDirectories = request.WorkingDirectories
            .Select(Normalize)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (workingDirectories.Length > 0)
        {
            payload["cwds"] = workingDirectories;
        }

        if (request.ForceReload)
        {
            payload["forceReload"] = true;
        }

        var extraRoots = request.ExtraRootsByWorkingDirectory
            .Select(static item => new
            {
                Cwd = Normalize(item.WorkingDirectory),
                ExtraUserRoots = item.ExtraUserRoots
                    .Select(Normalize)
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Select(static value => value!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            })
            .Where(static item => !string.IsNullOrWhiteSpace(item.Cwd) && item.ExtraUserRoots.Length > 0)
            .Select(static item => new Dictionary<string, object?>
            {
                ["cwd"] = item.Cwd,
                ["extraUserRoots"] = item.ExtraUserRoots,
            })
            .ToArray();
        if (extraRoots.Length > 0)
        {
            payload["perCwdExtraUserRoots"] = extraRoots;
        }

        var result = await ExecuteRuntimeSurfaceAsync("skills/list", payload, cancellationToken).ConfigureAwait(false);
        return ParseSkillsListResult(result);
    }

    public async Task<ControlPlaneSkillConfigWriteResult> WriteSkillsConfigAsync(ControlPlaneSkillConfigWriteCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var path = Normalize(request.Path);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("path 不能为空。", nameof(request));
        }

        var result = await ExecuteRuntimeSurfaceAsync(
                "skills/config/write",
                new Dictionary<string, object?>
                {
                    ["path"] = path,
                    ["enabled"] = request.Enabled,
                    ["cwd"] = Normalize(request.WorkingDirectory),
                },
                cancellationToken)
            .ConfigureAwait(false);

        return ParseSkillsConfigWriteResult(result);
    }

    public async Task<ControlPlaneRemoteSkillCatalogResult> ListRemoteSkillsAsync(ControlPlaneRemoteSkillCatalogQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = new Dictionary<string, object?>();
        var hazelnutScope = Normalize(request.HazelnutScope);
        if (!string.IsNullOrWhiteSpace(hazelnutScope))
        {
            payload["hazelnutScope"] = hazelnutScope;
        }

        var productSurface = Normalize(request.ProductSurface);
        if (!string.IsNullOrWhiteSpace(productSurface))
        {
            payload["productSurface"] = productSurface;
        }

        if (request.Enabled.HasValue)
        {
            payload["enabled"] = request.Enabled.Value;
        }

        var result = await ExecuteRuntimeSurfaceAsync("skills/remote/list", payload, cancellationToken).ConfigureAwait(false);
        return ParseSkillsRemoteListResult(result);
    }

    public async Task<ControlPlaneRemoteSkillExportResult> ExportRemoteSkillAsync(ControlPlaneRemoteSkillExportCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var hazelnutId = Normalize(request.HazelnutId);
        if (string.IsNullOrWhiteSpace(hazelnutId))
        {
            throw new ArgumentException("hazelnutId 不能为空。", nameof(request));
        }

        var result = await ExecuteRuntimeSurfaceAsync(
                "skills/remote/export",
                new Dictionary<string, object?>
                {
                    ["hazelnutId"] = hazelnutId,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return ParseSkillsRemoteExportResult(result);
    }

    public async Task<ControlPlanePluginCatalogResult> ListPluginsAsync(ControlPlanePluginCatalogQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var workingDirectories = request.WorkingDirectories
            .Select(Normalize)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var result = await ExecuteRuntimeSurfaceAsync(
                "plugin/list",
                new Dictionary<string, object?>
                {
                    ["cwds"] = workingDirectories,
                    ["forceRemoteSync"] = request.ForceRemoteSync,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return ParsePluginListResult(result);
    }

    public async Task<ControlPlanePluginReadResult> ReadPluginAsync(ControlPlanePluginReadQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var marketplacePath = Normalize(request.MarketplacePath);
        var pluginName = Normalize(request.PluginName);
        if (string.IsNullOrWhiteSpace(marketplacePath) || string.IsNullOrWhiteSpace(pluginName))
        {
            throw new ArgumentException("marketplacePath/pluginName 不能为空。", nameof(request));
        }

        var result = await ExecuteRuntimeSurfaceAsync(
                "plugin/read",
                new Dictionary<string, object?>
                {
                    ["marketplacePath"] = marketplacePath,
                    ["pluginName"] = pluginName,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return ParsePluginReadResult(result);
    }

    public async Task<ControlPlanePluginInstallResult> InstallPluginAsync(ControlPlanePluginInstallCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var marketplacePath = Normalize(request.MarketplacePath);
        var pluginName = Normalize(request.PluginName);
        if (string.IsNullOrWhiteSpace(marketplacePath) || string.IsNullOrWhiteSpace(pluginName))
        {
            throw new ArgumentException("marketplacePath/pluginName 不能为空。", nameof(request));
        }

        var result = await ExecuteRuntimeSurfaceAsync(
                "plugin/install",
                new Dictionary<string, object?>
                {
                    ["marketplacePath"] = marketplacePath,
                    ["pluginName"] = pluginName,
                    ["cwd"] = Normalize(request.WorkingDirectory),
                },
                cancellationToken)
            .ConfigureAwait(false);

        return ParsePluginInstallResult(result);
    }

    public async Task<ControlPlanePluginUninstallResult> UninstallPluginAsync(ControlPlanePluginUninstallCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var pluginId = Normalize(request.PluginId);
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            throw new ArgumentException("pluginId 不能为空。", nameof(request));
        }

        await ExecuteRuntimeSurfaceAsync(
                "plugin/uninstall",
                new Dictionary<string, object?>
                {
                    ["pluginId"] = pluginId,
                    ["cwd"] = Normalize(request.WorkingDirectory),
                },
                cancellationToken)
            .ConfigureAwait(false);

        return new ControlPlanePluginUninstallResult();
    }

    public async Task<ControlPlaneReviewStartResult> StartReviewAsync(ControlPlaneReviewStartCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedThreadId = Normalize(request.ThreadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            throw new ArgumentException("threadId 不能为空。", nameof(request));
        }

        var result = await ExecuteRuntimeSurfaceAsync(
                "review/start",
                new Dictionary<string, object?>
                {
                    ["threadId"] = normalizedThreadId,
                    ["target"] = BuildReviewTargetPayload(request.Target),
                    ["delivery"] = Normalize(request.Delivery),
                },
                cancellationToken)
            .ConfigureAwait(false);

        var parsedResult = ParseReviewStartResult(result);
        var reviewTurnId = Normalize(parsedResult.Turn?.Id);
        if (!string.IsNullOrWhiteSpace(reviewTurnId))
        {
            activeThreadId = normalizedThreadId;
            activeTurnId = null;
            submittedTurnId = reviewTurnId;
        }

        return parsedResult;
    }

    public async Task<ControlPlaneExperimentalFeatureCatalogResult> ListExperimentalFeaturesAsync(ControlPlaneExperimentalFeatureQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = new Dictionary<string, object?>();
        if (request.Limit is > 0)
        {
            payload["limit"] = request.Limit.Value;
        }

        var normalizedCursor = Normalize(request.Cursor);
        if (!string.IsNullOrWhiteSpace(normalizedCursor))
        {
            payload["cursor"] = normalizedCursor;
        }

        var result = await ExecuteRuntimeSurfaceAsync("experimentalfeature/list", payload, cancellationToken).ConfigureAwait(false);
        return ParseExperimentalFeatureListResult(result);
    }

    public async Task<ControlPlaneCollaborationModeCatalogResult> ListCollaborationModesAsync(CancellationToken cancellationToken)
    {
        var result = await ExecuteRuntimeSurfaceAsync("collaborationmode/list", null, cancellationToken).ConfigureAwait(false);
        return ParseCollaborationModeListResult(result);
    }

    public async Task<ControlPlaneMcpServerCatalogResult> ListMcpServerStatusAsync(ControlPlaneMcpServerStatusQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = new Dictionary<string, object?>();
        if (request.Limit is > 0)
        {
            payload["limit"] = request.Limit.Value;
        }

        var normalizedCursor = Normalize(request.Cursor);
        if (!string.IsNullOrWhiteSpace(normalizedCursor))
        {
            payload["cursor"] = normalizedCursor;
        }

        var result = await ExecuteRuntimeSurfaceAsync("mcpserverstatus/list", payload, cancellationToken).ConfigureAwait(false);
        return ParseMcpServerStatusListResult(result);
    }

    public async Task<ControlPlaneMcpServerReloadResult> ReloadMcpServersAsync(ControlPlaneMcpServerReloadCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await ExecuteRuntimeSurfaceAsync("config/mcpserver/reload", null, cancellationToken).ConfigureAwait(false);
        return ParseMcpServerReloadResult(result);
    }

    public async Task<ControlPlaneProviderPackageReloadResult> ReloadProviderPackagesAsync(ControlPlaneProviderPackageReloadCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await ExecuteRuntimeSurfaceAsync("config/provider/reload", null, cancellationToken).ConfigureAwait(false);
        return ParseProviderPackageReloadResult(result);
    }

    public async Task<ControlPlaneMcpServerOauthLoginStartResult> StartMcpServerOauthLoginAsync(ControlPlaneMcpServerOauthLoginStartCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var name = Normalize(request.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("name 不能为空。", nameof(request));
        }

        var payload = new Dictionary<string, object?>
        {
            ["name"] = name,
        };

        if (request.TimeoutSecs.HasValue)
        {
            payload["timeoutSecs"] = request.TimeoutSecs.Value;
        }

        var result = await ExecuteRuntimeSurfaceAsync("mcpServer/oauth/login", payload, cancellationToken).ConfigureAwait(false);
        return ParseMcpServerOauthLoginStartResult(result);
    }

    public async Task<ControlPlaneConversationArtifact?> GetConversationSummaryAsync(
        ControlPlaneConversationArtifactQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var rolloutPath = Normalize(request.RolloutPath);
        var threadId = Normalize(request.ThreadId?.Value);
        if (string.IsNullOrWhiteSpace(rolloutPath) && string.IsNullOrWhiteSpace(threadId))
        {
            throw new ArgumentException("rolloutPath 与 threadId 不能同时为空。", nameof(request));
        }

        Dictionary<string, object?> payload;
        if (!string.IsNullOrWhiteSpace(rolloutPath))
        {
            payload = new Dictionary<string, object?>
            {
                ["rolloutPath"] = rolloutPath,
            };
        }
        else
        {
            payload = new Dictionary<string, object?>
            {
                ["threadId"] = threadId,
            };
        }

        var result = await InvokeRpcAsync("artifact/conversationsummary/read", payload, cancellationToken).ConfigureAwait(false);
        return ParseConversationSummaryResult(result);
    }

    public async Task<ControlPlaneGitDiffArtifact> GetGitDiffToRemoteAsync(
        ControlPlaneGitDiffArtifactQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var threadId = Normalize(request.ThreadId.Value);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            throw new ArgumentException("threadId 不能为空。", nameof(request));
        }

        var result = await InvokeRpcAsync(
                "artifact/gitdifftoremote/read",
                new Dictionary<string, object?>
                {
                    ["threadId"] = threadId,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return ParseGitDiffToRemoteResult(result);
    }

    public async Task<ControlPlaneCommandExecutionResult> StartCommandExecutionAsync(ControlPlaneCommandExecutionStartCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = BuildCommandExecutionStartPayload(request);
        var result = await InvokeRpcAsync("command/exec", payload, cancellationToken).ConfigureAwait(false);
        return ParseCommandExecutionResult(result);
    }

    public async Task<ControlPlaneCommandExecutionCommandAcceptedResult> WriteCommandExecutionAsync(ControlPlaneCommandExecutionWriteCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = BuildCommandExecutionWritePayload(request);
        _ = await InvokeRpcAsync("command/exec/write", payload, cancellationToken).ConfigureAwait(false);
        return new ControlPlaneCommandExecutionCommandAcceptedResult();
    }

    public async Task<ControlPlaneCommandExecutionCommandAcceptedResult> TerminateCommandExecutionAsync(ControlPlaneCommandExecutionTerminateCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = BuildCommandExecutionTerminatePayload(request);
        _ = await InvokeRpcAsync("command/exec/terminate", payload, cancellationToken).ConfigureAwait(false);
        return new ControlPlaneCommandExecutionCommandAcceptedResult();
    }

    public async Task<ControlPlaneCommandExecutionCommandAcceptedResult> ResizeCommandExecutionAsync(ControlPlaneCommandExecutionResizeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = BuildCommandExecutionResizePayload(request);
        _ = await InvokeRpcAsync("command/exec/resize", payload, cancellationToken).ConfigureAwait(false);
        return new ControlPlaneCommandExecutionCommandAcceptedResult();
    }

    public async Task<ControlPlaneCodeModeResult> ExecuteCodeModeAsync(ControlPlaneCodeModeExecCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await InvokeRpcAsync(
                "exec",
                new Dictionary<string, object?>
                {
                    ["threadId"] = Normalize(request.ThreadId),
                    ["input"] = request.Input,
                    ["yieldTimeMs"] = request.YieldTimeMs,
                    ["maxOutputTokens"] = request.MaxOutputTokens,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return ParseCodeModeOperationResult(result, request.ThreadId.Value, fallbackCellId: null);
    }

    public async Task<ControlPlaneCodeModeResult> WaitCodeModeAsync(ControlPlaneCodeModeWaitCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await InvokeRpcAsync(
                "exec_wait",
                new Dictionary<string, object?>
                {
                    ["threadId"] = Normalize(request.ThreadId),
                    ["cellId"] = Normalize(request.CellId),
                    ["yieldTimeMs"] = request.YieldTimeMs,
                    ["maxTokens"] = request.MaxTokens,
                    ["terminate"] = request.Terminate,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return ParseCodeModeOperationResult(result, request.ThreadId.Value, request.CellId);
    }

    public async Task<ControlPlaneFuzzyFileSearchResult> SearchFuzzyFilesAsync(ControlPlaneFuzzyFileSearchQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = Normalize(request.Query);
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("query 不能为空。", nameof(request));
        }

        var payload = new Dictionary<string, object?>
        {
            ["query"] = query,
        };

        var cwd = Normalize(request.WorkingDirectory);
        if (!string.IsNullOrWhiteSpace(cwd))
        {
            payload["cwd"] = cwd;
        }

        if (request.Limit is int limit)
        {
            if (limit <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(request), "limit 必须大于 0。");
            }

            payload["limit"] = limit;
        }

        var roots = NormalizeDistinctStrings(request.Roots);
        if (roots.Length > 0)
        {
            payload["roots"] = roots;
        }

        var result = await InvokeRpcAsync("fuzzyFileSearch", payload, cancellationToken).ConfigureAwait(false);
        return ParseFuzzyFileSearchResult(result);
    }

    public async Task<ControlPlaneFuzzyFileSearchCommandAcceptedResult> StartFuzzyFileSearchSessionAsync(ControlPlaneStartFuzzyFileSearchSessionCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sessionId = Normalize(request.SessionId);
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("sessionId 不能为空。", nameof(request));
        }

        var payload = new Dictionary<string, object?>
        {
            ["sessionId"] = sessionId,
        };

        var roots = NormalizeDistinctStrings(request.Roots);
        if (roots.Length > 0)
        {
            payload["roots"] = roots;
        }

        await InvokeRpcAsync("fuzzyFileSearch/sessionStart", payload, cancellationToken).ConfigureAwait(false);
        return new ControlPlaneFuzzyFileSearchCommandAcceptedResult();
    }

    public async Task<ControlPlaneFuzzyFileSearchCommandAcceptedResult> UpdateFuzzyFileSearchSessionAsync(ControlPlaneUpdateFuzzyFileSearchSessionCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sessionId = Normalize(request.SessionId);
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("sessionId 不能为空。", nameof(request));
        }

        var query = Normalize(request.Query);
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("query 不能为空。", nameof(request));
        }

        await InvokeRpcAsync(
                "fuzzyFileSearch/sessionUpdate",
                new Dictionary<string, object?>
                {
                    ["sessionId"] = sessionId,
                    ["query"] = query,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return new ControlPlaneFuzzyFileSearchCommandAcceptedResult();
    }

    public async Task<ControlPlaneFuzzyFileSearchCommandAcceptedResult> StopFuzzyFileSearchSessionAsync(ControlPlaneStopFuzzyFileSearchSessionCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sessionId = Normalize(request.SessionId);
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("sessionId 不能为空。", nameof(request));
        }

        await InvokeRpcAsync(
                "fuzzyFileSearch/sessionStop",
                new Dictionary<string, object?>
                {
                    ["sessionId"] = sessionId,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return new ControlPlaneFuzzyFileSearchCommandAcceptedResult();
    }

    public async Task<ControlPlaneAgentThreadRegistrationResult> RegisterAgentThreadAsync(ControlPlaneRegisterAgentThreadCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var threadId = Normalize(request.ThreadId.Value);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            throw new ArgumentException("threadId 不能为空。", nameof(request));
        }

        var payload = new Dictionary<string, object?>
        {
            ["threadId"] = threadId,
        };

        var agentNickname = Normalize(request.AgentNickname);
        if (!string.IsNullOrWhiteSpace(agentNickname))
        {
            payload["agentNickname"] = agentNickname;
        }

        var agentRole = Normalize(request.AgentRole);
        if (!string.IsNullOrWhiteSpace(agentRole))
        {
            payload["agentRole"] = agentRole;
        }

        var result = await InvokeRpcAsync("agent/thread/register", payload, cancellationToken).ConfigureAwait(false);
        return ParseAgentThreadRegistrationResult(result);
    }

    public async Task<ControlPlaneJobOperationResult> CreateAgentJobAsync(ControlPlaneCreateJobCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var instruction = Normalize(request.Instruction);
        if (string.IsNullOrWhiteSpace(instruction))
        {
            throw new ArgumentException("instruction 不能为空。", nameof(request));
        }

        var payload = new Dictionary<string, object?>
        {
            ["instruction"] = instruction,
        };

        var jobId = Normalize(request.JobId?.Value);
        if (!string.IsNullOrWhiteSpace(jobId))
        {
            payload["jobId"] = jobId;
        }

        var name = Normalize(request.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            payload["name"] = name;
        }

        var inputCsvPath = Normalize(request.InputCsvPath);
        if (!string.IsNullOrWhiteSpace(inputCsvPath))
        {
            payload["inputCsvPath"] = inputCsvPath;
        }

        var outputCsvPath = Normalize(request.OutputCsvPath);
        if (!string.IsNullOrWhiteSpace(outputCsvPath))
        {
            payload["outputCsvPath"] = outputCsvPath;
        }

        if (request.AutoExport.HasValue)
        {
            payload["autoExport"] = request.AutoExport.Value;
        }

        if (request.InputHeaders is not null)
        {
            payload["inputHeaders"] = request.InputHeaders.ToPlainObject();
        }

        if (request.OutputSchema is not null)
        {
            payload["outputSchema"] = request.OutputSchema.ToPlainObject();
        }

        if (request.Items.Count > 0)
        {
            payload["items"] = request.Items.Select(static item => item.ToPlainObject()).ToArray();
        }

        var result = await InvokeRpcAsync("agent/job/create", payload, cancellationToken).ConfigureAwait(false);
        return ParseJobOperationResult(result);
    }

    public async Task<ControlPlaneJobOperationResult> DispatchAgentJobAsync(ControlPlaneDispatchJobCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var jobId = Normalize(request.JobId.Value);
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("jobId 不能为空。", nameof(request));
        }

        var threadIds = request.ThreadIds
            .Select(static item => item.Value)
            .Select(Normalize)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (threadIds.Length == 0)
        {
            throw new ArgumentException("threadIds 不能为空。", nameof(request));
        }

        var result = await InvokeRpcAsync(
                "agent/job/dispatch",
                new Dictionary<string, object?>
                {
                    ["jobId"] = jobId,
                    ["threadIds"] = threadIds,
                },
                cancellationToken)
            .ConfigureAwait(false);
        return ParseJobOperationResult(result);
    }

    public async Task<ControlPlaneJobOperationResult> ReportAgentJobItemAsync(ControlPlaneReportJobItemCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var jobId = Normalize(request.JobId.Value);
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("jobId 不能为空。", nameof(request));
        }

        var itemId = Normalize(request.ItemId.Value);
        if (string.IsNullOrWhiteSpace(itemId))
        {
            throw new ArgumentException("itemId 不能为空。", nameof(request));
        }

        var status = Normalize(request.Status);
        if (string.IsNullOrWhiteSpace(status))
        {
            throw new ArgumentException("status 不能为空。", nameof(request));
        }

        var payload = new Dictionary<string, object?>
        {
            ["jobId"] = jobId,
            ["itemId"] = itemId,
            ["status"] = status,
        };

        if (request.Result is not null)
        {
            payload["result"] = request.Result.ToPlainObject();
        }

        var lastError = Normalize(request.LastError);
        if (!string.IsNullOrWhiteSpace(lastError))
        {
            payload["lastError"] = lastError;
        }

        var result = await InvokeRpcAsync("agent/job/item/report", payload, cancellationToken).ConfigureAwait(false);
        return ParseJobOperationResult(result);
    }

    public async Task<ControlPlaneJobOperationResult> ReadAgentJobAsync(ControlPlaneReadJobQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var jobId = Normalize(request.JobId.Value);
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("jobId 不能为空。", nameof(request));
        }

        var result = await InvokeRpcAsync(
                "agent/job/read",
                new Dictionary<string, object?>
                {
                    ["jobId"] = jobId,
                },
                cancellationToken)
            .ConfigureAwait(false);
        return ParseJobOperationResult(result);
    }

    public async Task<ControlPlaneJobListResult> ListAgentJobsAsync(ControlPlaneListJobsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = new Dictionary<string, object?>();
        var statuses = request.Statuses
            .Select(Normalize)
            .Where(static status => !string.IsNullOrWhiteSpace(status))
            .Select(static status => status!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (statuses.Length > 0)
        {
            payload["statuses"] = statuses;
        }

        if (request.Limit is > 0)
        {
            payload["limit"] = request.Limit.Value;
        }

        var result = await InvokeRpcAsync("agent/jobs/list", payload, cancellationToken).ConfigureAwait(false);
        return ParseJobListResult(result);
    }

    public async Task<ControlPlaneFeedbackUploadResult> UploadFeedbackAsync(ControlPlaneFeedbackUploadCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var classification = Normalize(request.Classification);
        if (string.IsNullOrWhiteSpace(classification))
        {
            throw new ArgumentException("classification 不能为空。", nameof(request));
        }

        var payload = new Dictionary<string, object?>
        {
            ["classification"] = classification,
            ["includeLogs"] = request.IncludeLogs,
        };
        var threadId = Normalize(request.ThreadId);
        if (!string.IsNullOrWhiteSpace(threadId))
        {
            payload["threadId"] = threadId;
        }

        var reason = Normalize(request.Reason);
        if (!string.IsNullOrWhiteSpace(reason))
        {
            payload["reason"] = reason;
        }

        if (request.ExtraLogFiles.Count > 0)
        {
            payload["extraLogFiles"] = request.ExtraLogFiles
                .Select(Normalize)
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Select(static path => path!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var result = await InvokeRpcAsync("feedback/upload", payload, cancellationToken).ConfigureAwait(false);
        return ParseFeedbackUploadResult(result);
    }

    public async Task<ControlPlaneWindowsSandboxSetupStartResult> StartWindowsSandboxSetupAsync(ControlPlaneWindowsSandboxSetupStartCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = new Dictionary<string, object?>
        {
            ["mode"] = request.Mode switch
            {
                WindowsSandboxSetupMode.Elevated => "elevated",
                WindowsSandboxSetupMode.Unelevated => "unelevated",
                _ => throw new ArgumentOutOfRangeException(nameof(request), request.Mode, "不支持的 windows sandbox mode。"),
            },
        };

        var cwd = Normalize(request.WorkingDirectory);
        if (!string.IsNullOrWhiteSpace(cwd))
        {
            payload["cwd"] = cwd;
        }

        var result = await InvokeRpcAsync("windowsSandbox/setupStart", payload, cancellationToken).ConfigureAwait(false);
        return ParseWindowsSandboxSetupStartResult(result);
    }

    private static Dictionary<string, object?> BuildCommandExecutionStartPayload(ControlPlaneCommandExecutionStartCommand request)
    {
        object command;
        if (!string.IsNullOrWhiteSpace(request.CommandText))
        {
            command = request.CommandText;
        }
        else if (request.CommandArgs.Count > 0)
        {
            command = request.CommandArgs.ToArray();
        }
        else
        {
            throw new ArgumentException("command 不能为空。", nameof(request));
        }

        var payload = new Dictionary<string, object?>
        {
            ["command"] = command,
        };

        var cwd = Normalize(request.WorkingDirectory);
        if (!string.IsNullOrWhiteSpace(cwd))
        {
            payload["cwd"] = cwd;
        }

        AddIfNotNull(payload, "processId", Normalize(request.ProcessId));
        AddIfTrue(payload, "tty", request.Tty);
        AddIfTrue(payload, "streamStdin", request.StreamStdin);
        AddIfTrue(payload, "streamStdoutStderr", request.StreamStdoutStderr);
        AddIfTrue(payload, "background", request.Background);
        AddIfTrue(payload, "disableTimeout", request.DisableTimeout);
        AddIfNotNull(payload, "timeoutMs", request.TimeoutMs);
        AddIfTrue(payload, "disableOutputCap", request.DisableOutputCap);
        AddIfNotNull(payload, "outputBytesCap", request.OutputBytesCap);
        AddIfNotNull(payload, "threadId", Normalize(request.ThreadId?.Value));
        AddIfNotNull(payload, "turnId", Normalize(request.TurnId?.Value));
        AddIfNotNull(payload, "itemId", Normalize(request.ItemId));
        AddIfNotNull(payload, "approvalPolicy", Normalize(request.ApprovalPolicy));
        AddIfTrue(payload, "approved", request.Approved);
        if (request.Login.HasValue)
        {
            payload["login"] = request.Login.Value;
        }

        if (request.EnvironmentVariables.Count > 0)
        {
            payload["env"] = request.EnvironmentVariables.ToDictionary(static pair => pair.Key, static pair => pair.Value);
        }

        if (request.Sandbox is not null)
        {
            payload["sandbox"] = request.Sandbox.ToPlainObject();
        }

        if (request.Size is not null)
        {
            payload["size"] = new Dictionary<string, object?>
            {
                ["rows"] = request.Size.Rows,
                ["cols"] = request.Size.Cols,
            };
        }

        return payload;
    }

    private static Dictionary<string, object?> BuildCommandExecutionWritePayload(ControlPlaneCommandExecutionWriteCommand request)
    {
        var processId = Normalize(request.ProcessId);
        if (string.IsNullOrWhiteSpace(processId))
        {
            throw new ArgumentException("processId 不能为空。", nameof(request));
        }

        var payload = new Dictionary<string, object?>
        {
            ["processId"] = processId,
        };

        AddIfTrue(payload, "closeStdin", request.CloseStdin);
        AddIfNotNull(payload, "deltaBase64", Normalize(request.DeltaBase64));
        return payload;
    }

    private static Dictionary<string, object?> BuildCommandExecutionTerminatePayload(ControlPlaneCommandExecutionTerminateCommand request)
    {
        var processId = Normalize(request.ProcessId);
        if (string.IsNullOrWhiteSpace(processId))
        {
            throw new ArgumentException("processId 不能为空。", nameof(request));
        }

        return new Dictionary<string, object?>
        {
            ["processId"] = processId,
        };
    }

    private static Dictionary<string, object?> BuildCommandExecutionResizePayload(ControlPlaneCommandExecutionResizeCommand request)
    {
        var processId = Normalize(request.ProcessId);
        if (string.IsNullOrWhiteSpace(processId))
        {
            throw new ArgumentException("processId 不能为空。", nameof(request));
        }

        return new Dictionary<string, object?>
        {
            ["processId"] = processId,
            ["size"] = new Dictionary<string, object?>
            {
                ["rows"] = request.Size.Rows,
                ["cols"] = request.Size.Cols,
            },
        };
    }

    public async Task<StructuredValue> InvokeDiagnosticRpcAsync(string method, StructuredValue? parameters, CancellationToken cancellationToken)
    {
        var normalizedMethod = Normalize(method);
        if (string.IsNullOrWhiteSpace(normalizedMethod))
        {
            throw new ArgumentException("method 不能为空。", nameof(method));
        }

        var result = await InvokeRpcAsync(normalizedMethod, ToAgentStructuredValue(parameters), cancellationToken).ConfigureAwait(false);
        return StructuredValue.FromJsonElement(result);
    }

    private Task<JsonElement> InvokeRpcAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        if (process is null || stdin is null)
        {
            throw new InvalidOperationException("运行时尚未初始化，无法调用内核 RPC。");
        }

        return SendRpcAsync(method, parameters, cancellationToken);
    }

    private static ControlPlaneCommandExecutionResult ParseCommandExecutionResult(JsonElement result)
        => new()
        {
            Started = ReadBool(result, "started") ?? false,
            ProcessId = ReadString(result, "processId"),
            Pid = ReadInt32(result, "pid"),
            ExitCode = ReadInt32(result, "exitCode"),
            Stdout = ReadString(result, "stdout"),
            Stderr = ReadString(result, "stderr"),
        };

    private static ControlPlaneCodeModeResult ParseCodeModeOperationResult(JsonElement result, string? fallbackThreadId, string? fallbackCellId)
    {
        var contentItems = new List<ControlPlaneCodeModeOutputItem>();
        if (result.TryGetProperty("contentItems", out var contentArray) && contentArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in contentArray.EnumerateArray())
            {
                contentItems.Add(new ControlPlaneCodeModeOutputItem
                {
                    Type = ReadString(item, "type") ?? string.Empty,
                    Text = ReadString(item, "text"),
                    ImageUrl = ReadString(item, "imageUrl") ?? ReadString(item, "image_url"),
                    Detail = ReadString(item, "detail"),
                });
            }
        }

        var status = ReadString(result, "status");
        var cellId = ReadString(result, "cellId") ?? fallbackCellId;
        if (string.IsNullOrWhiteSpace(status))
        {
            status = InferCodeModeStatus(contentItems, ReadBool(result, "success") ?? true);
        }

        if (string.IsNullOrWhiteSpace(cellId))
        {
            cellId = TryExtractCodeModeCellId(contentItems);
        }

        return new ControlPlaneCodeModeResult
        {
            Success = ReadBool(result, "success") ?? false,
            Status = status ?? "unknown",
            ThreadId = TryCreateThreadId(ReadString(result, "threadId") ?? fallbackThreadId),
            TurnId = TryCreateTurnId(ReadString(result, "turnId")),
            CellId = cellId,
            Output = ReadString(result, "output") ?? string.Empty,
            ContentItems = contentItems,
        };
    }

    private static ThreadId? TryCreateThreadId(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : new ThreadId(value);

    private static TurnId? TryCreateTurnId(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : new TurnId(value);

    private static ControlPlaneFuzzyFileSearchResult ParseFuzzyFileSearchResult(JsonElement result)
        => new()
        {
            Files = ReadFuzzyFileSearchFiles(result),
        };

    private static IReadOnlyList<ControlPlaneFuzzyFileSearchFile> ReadFuzzyFileSearchFiles(JsonElement result)
    {
        var filesElement = result;
        if (result.ValueKind == JsonValueKind.Object
            && result.TryGetProperty("files", out var nestedFiles))
        {
            filesElement = nestedFiles;
        }

        if (filesElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ControlPlaneFuzzyFileSearchFile>();
        }

        var files = new List<ControlPlaneFuzzyFileSearchFile>();
        foreach (var item in filesElement.EnumerateArray())
        {
            switch (item.ValueKind)
            {
                case JsonValueKind.String:
                    files.Add(new ControlPlaneFuzzyFileSearchFile
                    {
                        Path = item.GetString() ?? string.Empty,
                    });
                    break;

                case JsonValueKind.Object:
                    files.Add(new ControlPlaneFuzzyFileSearchFile
                    {
                        Path = ReadString(item, "path") ?? string.Empty,
                        FileName = ReadString(item, "fileName"),
                    });
                    break;
            }
        }

        return files;
    }

    private static ControlPlaneConfigSnapshotResult ParseConfigReadResult(JsonElement result)
    {
        var configSnapshot = ParseConfigSnapshot(result);
        var origins = ParseConfigOrigins(result);
        var fields = new List<ControlPlaneConfigField>();
        var config = TryGetObject(result, "config");
        if (config.HasValue)
        {
            foreach (var property in config.Value.EnumerateObject())
            {
                if (IsTypedConfigTopLevelKey(property.Name))
                {
                    continue;
                }

                var sourceType = string.Empty;
                var sourcePath = string.Empty;
                var sourceText = "来源未知";
                if (origins.TryGetValue(property.Name, out var originEntry))
                {
                    sourceType = Normalize(originEntry.Type) ?? string.Empty;
                    sourcePath = Normalize(originEntry.File)
                                 ?? Normalize(originEntry.DotTianShuFolder)
                                 ?? string.Empty;
                    sourceText = string.IsNullOrWhiteSpace(sourcePath)
                        ? (string.IsNullOrWhiteSpace(sourceType) ? "来源未知" : sourceType)
                        : $"{sourceType} · {sourcePath}";
                }

                fields.Add(new ControlPlaneConfigField
                {
                    KeyPath = property.Name,
                    ValueKind = property.Value.ValueKind.ToString(),
                    ValueText = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.GetRawText(),
                    Value = ParseStructuredValue(property.Value),
                    SourceType = sourceType,
                    SourcePath = sourcePath,
                    SourceText = sourceText,
                });
            }
        }

        var layers = new List<ControlPlaneConfigLayer>();
        if (result.TryGetProperty("layers", out var layersElement) && layersElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var layer in layersElement.EnumerateArray())
            {
                if (layer.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var layerVersion = ReadString(layer, "version") ?? string.Empty;
                var disabledReason = ReadString(layer, "disabledReason");

                layers.Add(new ControlPlaneConfigLayer
                {
                    Name = ReadCatalogStructuredValue(layer, "name"),
                    Version = layerVersion,
                    Config = ReadCatalogStructuredValue(layer, "config"),
                    DisabledReason = disabledReason,
                });
            }
        }

        return new ControlPlaneConfigSnapshotResult
        {
            Config = configSnapshot,
            Origins = origins,
            Fields = fields,
            Layers = layers,
        };
    }

    private static StructuredValue? ParseConfigSnapshot(JsonElement result)
        => ReadCatalogStructuredValue(result, "config");

    private static bool IsTypedConfigTopLevelKey(string keyPath)
    {
        return !string.IsNullOrWhiteSpace(keyPath) && TypedConfigTopLevelKeys.Value.Contains(keyPath);
    }

    private static HashSet<string> CreateTypedConfigTopLevelKeys()
        => new(StringComparer.Ordinal)
        {
            "analytics",
            "approval_policy",
            "compact_prompt",
            "default_permissions",
            "developer_instructions",
            "disable_paste_burst",
            "experimental_compact_prompt_file",
            "experimental_realtime_start_instructions",
            "experimental_realtime_ws_backend_prompt",
            "experimental_realtime_ws_base_url",
            "experimental_realtime_ws_mode",
            "experimental_realtime_ws_model",
            "experimental_realtime_ws_startup_context",
            "experimental_use_freeform_apply_patch",
            "experimental_use_unified_exec_tool",
            "feedback",
            "features",
            "file_opener",
            OpenAiAppCatalogCompatibilityKeys.ForcedChatGptWorkspaceIdConfigKey,
            OpenAiAppCatalogCompatibilityKeys.ForcedLoginMethodConfigKey,
            "instructions",
            "history",
            "hide_agent_reasoning",
            "js_repl_node_module_dirs",
            "js_repl_node_path",
            "log_dir",
            "memories",
            "model",
            "model_auto_compact_token_limit",
            "model_context_window",
            "model_instructions_file",
            "experimental_instructions_file",
            "model_route_set_json",
            "model_reasoning_effort",
            "model_reasoning_summary",
            "model_verbosity",
            "model_supports_reasoning_summaries",
            "profile",
            "personality",
            "plan_mode_reasoning_effort",
            "plugins",
            "profiles",
            "project_doc_fallback_filenames",
            "project_doc_max_bytes",
            "project_root_markers",
            "projects",
            "review_model",
            "sandbox_mode",
            "sandbox_workspace_write",
            "shell_environment_policy",
            "service_tier",
            "show_raw_agent_reasoning",
            "skills",
            "sqlite_home",
            "tools",
            "tool_output_token_limit",
            "tui",
            "agents",
            "web_search",
            "allow_login_shell",
            "permissions",
            "notify",
            "commit_attribution",
            "cli_auth_credentials_store",
            "mcp_oauth_credentials_store",
            "mcp_oauth_callback_port",
            "mcp_oauth_callback_url",
            "mcp_servers",
            "apps",
            OpenAiAppCatalogCompatibilityKeys.ChatGptBaseUrlConfigKey,
            "audio",
            "background_terminal_max_timeout",
            "check_for_update_on_startup",
            "ghost_snapshot",
            "notice",
            "oss_provider",
            "otel",
            "suppress_unstable_features_warning",
            "windows",
            "windows_wsl_setup_acknowledged",
            "zsh_path",
        };

    private static IReadOnlyDictionary<string, ControlPlaneConfigOrigin> ParseConfigOrigins(JsonElement result)
    {
        var origins = TryGetObject(result, "origins");
        if (!origins.HasValue)
        {
            return new Dictionary<string, ControlPlaneConfigOrigin>(StringComparer.Ordinal);
        }

        var parsed = new Dictionary<string, ControlPlaneConfigOrigin>(StringComparer.Ordinal);
        foreach (var property in origins.Value.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = TryGetObject(property.Value, "name");
            parsed[property.Name] = new ControlPlaneConfigOrigin
            {
                Type = name.HasValue ? ReadString(name.Value, "type") : null,
                File = name.HasValue ? ReadString(name.Value, "file") : null,
                DotTianShuFolder = name.HasValue ? ReadString(name.Value, "dotTianShuFolder") : null,
                Version = ReadString(property.Value, "version"),
            };
        }

        return parsed;
    }

    private static ControlPlaneConfigRequirementsResult ParseConfigRequirementsReadResult(JsonElement result)
    {
        var requirements = TryGetObject(result, "requirements");
        if (!requirements.HasValue)
        {
            return new ControlPlaneConfigRequirementsResult
            {
                IsDefined = false,
            };
        }

        return new ControlPlaneConfigRequirementsResult
        {
            IsDefined = true,
            AllowedApprovalPolicies = ReadStringArray(requirements.Value, "allowedApprovalPolicies"),
            AllowedSandboxModes = ReadStringArray(requirements.Value, "allowedSandboxModes"),
            AllowedWebSearchModes = ReadStringArray(requirements.Value, "allowedWebSearchModes"),
            FeatureRequirements = ReadBooleanMap(requirements.Value, "featureRequirements"),
            EnforceResidency = ReadString(requirements.Value, "enforceResidency"),
            Network = ParseConfigRequirementsNetwork(requirements.Value),
        };
    }

    private static ControlPlaneConfigWriteResult ParseConfigWriteResult(JsonElement result)
    {
        var status = ReadString(result, "status") ?? string.Empty;
        return new ControlPlaneConfigWriteResult
        {
            Status = status,
            Version = ReadString(result, "version") ?? string.Empty,
            FilePath = ReadString(result, "filePath") ?? string.Empty,
            IsOverridden = string.Equals(status, "okOverridden", StringComparison.OrdinalIgnoreCase),
            OverriddenMetadata = ParseConfigWriteOverriddenMetadata(result),
        };
    }

    private static ControlPlaneConfigWriteOverride? ParseConfigWriteOverriddenMetadata(JsonElement result)
    {
        if (!result.TryGetProperty("overriddenMetadata", out var metadata) || metadata.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        ControlPlaneConfigOrigin? overridingLayer = null;
        if (metadata.TryGetProperty("overridingLayer", out var layerElement) && layerElement.ValueKind == JsonValueKind.Object)
        {
            var name = TryGetObject(layerElement, "name");
            overridingLayer = new ControlPlaneConfigOrigin
            {
                Type = name.HasValue ? ReadString(name.Value, "type") : null,
                File = name.HasValue ? ReadString(name.Value, "file") : null,
                DotTianShuFolder = name.HasValue ? ReadString(name.Value, "dotTianShuFolder") : null,
                Version = ReadString(layerElement, "version"),
            };
        }

        return new ControlPlaneConfigWriteOverride
        {
            Message = ReadString(metadata, "message") ?? string.Empty,
            OverridingLayer = overridingLayer,
            EffectiveValue = metadata.TryGetProperty("effectiveValue", out var effectiveValueElement)
                ? ParseStructuredValue(effectiveValueElement)
                : null,
        };
    }

    private static ControlPlaneSkillCatalogResult ParseSkillsListResult(JsonElement result)
    {
        var entries = new List<ControlPlaneSkillCatalogEntry>();
        if (result.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in data.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                entries.Add(new ControlPlaneSkillCatalogEntry
                {
                    WorkingDirectory = ReadString(entry, "cwd") ?? string.Empty,
                    Skills = ParseSkillDescriptorArray(entry, "skills"),
                    Errors = ParseSkillErrorArray(entry, "errors"),
                });
            }
        }

        return new ControlPlaneSkillCatalogResult
        {
            Entries = entries,
        };
    }

    private static ControlPlaneSkillConfigWriteResult ParseSkillsConfigWriteResult(JsonElement result)
        => new()
        {
            EffectiveEnabled = ReadBool(result, "effectiveEnabled") ?? false,
        };

    private static ControlPlaneRemoteSkillCatalogResult ParseSkillsRemoteListResult(JsonElement result)
    {
        var items = new List<ControlPlaneRemoteSkillSummary>();
        if (result.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                items.Add(new ControlPlaneRemoteSkillSummary
                {
                    Id = ReadString(item, "id") ?? ReadString(item, "hazelnutId") ?? string.Empty,
                    Name = ReadString(item, "name") ?? string.Empty,
                    Description = ReadString(item, "description") ?? string.Empty,
                    HazelnutScope = ReadString(item, "hazelnutScope"),
                });
            }
        }

        return new ControlPlaneRemoteSkillCatalogResult
        {
            Items = items,
            NextCursor = ReadString(result, "nextCursor"),
        };
    }

    private static ControlPlaneRemoteSkillExportResult ParseSkillsRemoteExportResult(JsonElement result)
        => new()
        {
            Id = ReadString(result, "id") ?? string.Empty,
            Path = ReadString(result, "path") ?? string.Empty,
        };

    private static ControlPlanePluginCatalogResult ParsePluginListResult(JsonElement result)
        => new()
        {
            Marketplaces = ParsePluginMarketplaceArray(result, "marketplaces"),
            RemoteSyncError = ReadString(result, "remoteSyncError"),
        };

    private static ControlPlanePluginReadResult ParsePluginReadResult(JsonElement result)
        => new()
        {
            Plugin = TryGetObject(result, "plugin") is { } plugin ? ParsePluginDetail(plugin) : null,
        };

    private static ControlPlanePluginInstallResult ParsePluginInstallResult(JsonElement result)
        => new()
        {
            AuthPolicy = ReadString(result, "authPolicy") ?? string.Empty,
            AppsNeedingAuth = ParsePluginAppReferenceArray(result, "appsNeedingAuth"),
        };

    private static ControlPlaneFeedbackUploadResult ParseFeedbackUploadResult(JsonElement result)
        => new()
        {
            ThreadId = ReadString(result, "threadId") ?? string.Empty,
        };

    private static ControlPlaneJobOperationResult ParseJobOperationResult(JsonElement result)
        => new()
        {
            Job = TryGetObject(result, "job") is { } job ? ParseJobDetails(job) : null,
            Items = ParseJobItems(result, "items"),
            Item = TryGetObject(result, "item") is { } item ? ParseJobItemDetails(item) : null,
        };

    private static ControlPlaneJobListResult ParseJobListResult(JsonElement result)
        => new()
        {
            Jobs = ParseJobDetailsArray(result, "jobs"),
        };

    private static ControlPlaneWindowsSandboxSetupStartResult ParseWindowsSandboxSetupStartResult(JsonElement result)
        => new()
        {
            Started = ReadBool(result, "started") ?? false,
        };

    private static ControlPlaneJobDetails ParseJobDetails(JsonElement data)
        => new()
        {
            Id = new JobId(ReadIdentifierValue(data, "id") ?? string.Empty),
            Name = ReadString(data, "name"),
            Status = ReadString(data, "status"),
            Instruction = ReadString(data, "instruction"),
        };

    private static IReadOnlyList<ControlPlaneJobDetails> ParseJobDetailsArray(JsonElement data, string propertyName)
    {
        if (!TryGetArray(data, propertyName, out var jobs))
        {
            return Array.Empty<ControlPlaneJobDetails>();
        }

        var results = new List<ControlPlaneJobDetails>();
        foreach (var job in jobs.EnumerateArray())
        {
            if (job.ValueKind == JsonValueKind.Object)
            {
                results.Add(ParseJobDetails(job));
            }
        }

        return results;
    }

    private static IReadOnlyList<ControlPlaneJobItemDetails> ParseJobItems(JsonElement data, string propertyName)
    {
        if (!TryGetArray(data, propertyName, out var items))
        {
            return Array.Empty<ControlPlaneJobItemDetails>();
        }

        var results = new List<ControlPlaneJobItemDetails>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            results.Add(ParseJobItemDetails(item));
        }

        return results;
    }

    private static ControlPlaneJobItemDetails ParseJobItemDetails(JsonElement data)
        => new()
        {
            ItemId = new JobItemId(ReadIdentifierValue(data, "itemId") ?? string.Empty),
            SourceId = ReadString(data, "sourceId"),
            ThreadId = ReadIdentifierValue(data, "threadId") is { Length: > 0 } threadId ? new ThreadId(threadId) : null,
            AssignedThreadId = ReadIdentifierValue(data, "assignedThreadId") is { Length: > 0 } assignedThreadId ? new ThreadId(assignedThreadId) : null,
            Status = ReadString(data, "status"),
            LastError = ReadString(data, "lastError"),
            Result = TryGetProperty(data, "result", out var result) ? ParseStructuredValue(result) : ReadEmbeddedStructuredValue(data, "resultJson"),
        };

    private static string? ReadIdentifierValue(JsonElement data, string propertyName)
        => ReadString(data, propertyName) ?? ReadString(data, propertyName, "value");

    private static StructuredValue? ReadEmbeddedStructuredValue(JsonElement data, string propertyName)
    {
        var raw = ReadString(data, propertyName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            return ParseStructuredValue(document.RootElement);
        }
        catch (JsonException)
        {
            return StructuredValue.FromString(raw);
        }
    }

    private AgentSendResult TrackPendingFollowUpCommit(
        AgentSendResult result,
        IReadOnlyList<AgentUserInput> userInputs,
        string? compareMessage,
        int imageCount,
        string? expectedTurnId)
    {
        if (!result.Success
            || string.IsNullOrWhiteSpace(result.CorrelationId)
            || string.IsNullOrWhiteSpace(result.TurnId))
        {
            return result;
        }

        var requestedMode = result.RequestedMode ?? result.EffectiveMode;
        var effectiveMode = result.EffectiveMode ?? result.RequestedMode;
        if (!requestedMode.HasValue || !effectiveMode.HasValue)
        {
            return result;
        }

        EnqueuePendingFollowUpCommit(
            activeThreadId,
            result.CorrelationId,
            userInputs,
            compareMessage,
            expectedTurnId,
            result.TurnId,
            requestedMode.Value,
            effectiveMode.Value,
            imageCount);
        return result;
    }

    private void EnqueuePendingFollowUpCommit(
        string? threadId,
        string? correlationId,
        IReadOnlyList<AgentUserInput> userInputs,
        string? compareMessage,
        string? expectedTurnId,
        string? turnId,
        FollowUpMode requestedMode,
        FollowUpMode effectiveMode,
        int imageCount = 0)
    {
        var normalizedThreadId = Normalize(threadId);
        var normalizedCorrelationId = Normalize(correlationId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId) || string.IsNullOrWhiteSpace(normalizedCorrelationId))
        {
            return;
        }

        var queue = pendingFollowUpCommitsByThread.GetOrAdd(
            normalizedThreadId!,
            static _ => new ConcurrentQueue<PendingFollowUpCommit>());
        queue.Enqueue(new PendingFollowUpCommit(
            normalizedCorrelationId!,
            NormalizeUserInputs(userInputs),
            Normalize(compareMessage) ?? string.Empty,
            ImageCount: Math.Max(0, imageCount),
            Normalize(expectedTurnId),
            Normalize(turnId),
            requestedMode,
            effectiveMode));

        RaisePendingFollowUpLifecycle(
            normalizedCorrelationId,
            requestedMode,
            effectiveMode,
            "awaiting_commit",
            expectedTurnId,
            turnId,
            compareMessage,
            imageCount,
            userInputs);
    }

    private void EnqueuePendingFollowUpCommit(
        string? threadId,
        string? correlationId,
        string? compareMessage,
        string? expectedTurnId,
        string? turnId,
        FollowUpMode requestedMode,
        FollowUpMode effectiveMode,
        int imageCount = 0)
        => EnqueuePendingFollowUpCommit(
            threadId,
            correlationId,
            Array.Empty<AgentUserInput>(),
            compareMessage,
            expectedTurnId,
            turnId,
            requestedMode,
            effectiveMode,
            imageCount);

    private void EnqueuePendingFollowUpCommit(
        string? threadId,
        string? correlationId,
        string? compareMessage,
        string? expectedTurnId,
        string? turnId,
        FollowUpMode requestedMode,
        FollowUpMode effectiveMode)
        => EnqueuePendingFollowUpCommit(
            threadId,
            correlationId,
            compareMessage,
            expectedTurnId,
            turnId,
            requestedMode,
            effectiveMode,
            0);

    private string? TryConsumePendingFollowUpCorrelation(
        IReadOnlyList<AgentUserInput>? committedInputs,
        string? turnId,
        string? threadId)
    {
        var normalizedCommittedInputs = NormalizeUserInputs(committedInputs);
        var normalizedThreadId = Normalize(threadId);
        if (normalizedCommittedInputs.Count == 0 || string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            return null;
        }

        if (!pendingFollowUpCommitsByThread.TryGetValue(normalizedThreadId!, out var queue)
            || !queue.TryPeek(out var pending))
        {
            return null;
        }

        var normalizedTurnId = Normalize(turnId);
        if (!string.IsNullOrWhiteSpace(pending.TurnId)
            && !string.IsNullOrWhiteSpace(normalizedTurnId)
            && !string.Equals(pending.TurnId, normalizedTurnId, StringComparison.Ordinal))
        {
            return null;
        }

        if (!AreEquivalentUserInputs(pending.Inputs, normalizedCommittedInputs))
        {
            return null;
        }

        if (!queue.TryDequeue(out pending))
        {
            return null;
        }

        if (queue.IsEmpty)
        {
            pendingFollowUpCommitsByThread.TryRemove(new KeyValuePair<string, ConcurrentQueue<PendingFollowUpCommit>>(normalizedThreadId!, queue));
        }

        RaisePendingFollowUpLifecycle(
            pending.CorrelationId,
            pending.RequestedMode,
            pending.EffectiveMode,
            "committed",
            pending.ExpectedTurnId,
            normalizedTurnId ?? pending.TurnId,
            pending.CompareMessage,
            pending.ImageCount,
            pending.Inputs);
        return pending.CorrelationId;
    }

    private void RemovePendingFollowUpCommitThread(string? threadId)
    {
        var normalizedThreadId = Normalize(threadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            return;
        }

        pendingFollowUpCommitsByThread.TryRemove(normalizedThreadId!, out _);
    }

    private void TrackObservedTurnForThread(string? threadId, string? turnId)
    {
        var normalizedThreadId = Normalize(threadId);
        var normalizedTurnId = Normalize(turnId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId) || string.IsNullOrWhiteSpace(normalizedTurnId))
        {
            return;
        }

        lock (observedTurnStateGate)
        {
            if (!observedTurnIdsByThread.TryGetValue(normalizedThreadId!, out var observedTurnIds))
            {
                observedTurnIds = [];
                observedTurnIdsByThread[normalizedThreadId!] = observedTurnIds;
            }

            observedTurnIds.RemoveAll(candidate => string.Equals(candidate, normalizedTurnId, StringComparison.Ordinal));
            observedTurnIds.Add(normalizedTurnId!);
        }
    }

    private void RemoveObservedTurnForThread(string? threadId, string? turnId)
    {
        var normalizedThreadId = Normalize(threadId);
        var normalizedTurnId = Normalize(turnId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId) || string.IsNullOrWhiteSpace(normalizedTurnId))
        {
            return;
        }

        lock (observedTurnStateGate)
        {
            if (!observedTurnIdsByThread.TryGetValue(normalizedThreadId!, out var observedTurnIds))
            {
                return;
            }

            observedTurnIds.RemoveAll(candidate => string.Equals(candidate, normalizedTurnId, StringComparison.Ordinal));
            if (observedTurnIds.Count == 0)
            {
                observedTurnIdsByThread.Remove(normalizedThreadId!);
            }
        }
    }

    private void RemoveObservedTurnsThread(string? threadId)
    {
        var normalizedThreadId = Normalize(threadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            return;
        }

        lock (observedTurnStateGate)
        {
            observedTurnIdsByThread.Remove(normalizedThreadId!);
        }
    }

    private string? TryResolveCompletedTurnIdFromObservedTurns(string? threadId)
    {
        var normalizedThreadId = Normalize(threadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            return null;
        }

        var normalizedActiveTurnId = Normalize(activeTurnId);
        lock (observedTurnStateGate)
        {
            if (!observedTurnIdsByThread.TryGetValue(normalizedThreadId!, out var observedTurnIds)
                || observedTurnIds.Count == 0)
            {
                return null;
            }

            foreach (var observedTurnId in observedTurnIds)
            {
                if (!string.IsNullOrWhiteSpace(normalizedActiveTurnId)
                    && string.Equals(observedTurnId, normalizedActiveTurnId, StringComparison.Ordinal))
                {
                    continue;
                }

                return observedTurnId;
            }
        }

        return null;
    }

    private void RaisePendingFollowUpLifecycle(
        string? correlationId,
        FollowUpMode requestedMode,
        FollowUpMode effectiveMode,
        string lifecycleState,
        string? expectedTurnId,
        string? turnId,
        string? compareMessage,
        int imageCount,
        IReadOnlyList<AgentUserInput>? inputs = null)
    {
        var normalizedCorrelationId = Normalize(correlationId);
        if (string.IsNullOrWhiteSpace(normalizedCorrelationId))
        {
            return;
        }

        var normalizedThreadId = Normalize(activeThreadId);
        var pendingInputState = UpdatePendingInputStateSnapshot(
            normalizedCorrelationId!,
            requestedMode,
            effectiveMode,
            lifecycleState,
            expectedTurnId,
            turnId,
            compareMessage,
            imageCount,
            inputs);

        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.PendingFollowUpUpdated,
            ThreadId = ToThreadId(activeThreadId),
            TurnId = ToTurnId(turnId),
            ItemId = normalizedCorrelationId,
            Status = lifecycleState,
            Text = compareMessage,
            Message = $"pending follow-up {lifecycleState}",
            PayloadKind = ControlPlaneConversationStreamPayloadKind.PendingInputState,
            Payload = ToStructuredPayload(pendingInputState),
        });

        SchedulePendingInputStatePersistence(normalizedThreadId, pendingInputState);
    }

    private void RaisePendingFollowUpLifecycle(
        string? correlationId,
        FollowUpMode requestedMode,
        FollowUpMode effectiveMode,
        string lifecycleState,
        string? expectedTurnId,
        string? turnId,
        string? compareMessage,
        int imageCount)
        => RaisePendingFollowUpLifecycle(
            correlationId,
            requestedMode,
            effectiveMode,
            lifecycleState,
            expectedTurnId,
            turnId,
            compareMessage,
            imageCount,
            Array.Empty<AgentUserInput>());

    private PendingInputStatePayload UpdatePendingInputStateSnapshot(
        string correlationId,
        FollowUpMode requestedMode,
        FollowUpMode effectiveMode,
        string lifecycleState,
        string? expectedTurnId,
        string? turnId,
        string? compareMessage,
        int imageCount,
        IReadOnlyList<AgentUserInput>? inputs = null)
    {
        var normalizedThreadId = Normalize(activeThreadId);
        lock (pendingInputStateGate)
        {
            if (string.IsNullOrWhiteSpace(normalizedThreadId))
            {
                return EmptyPendingInputStatePayload();
            }

            var currentState = pendingInputStatesByThread.TryGetValue(normalizedThreadId!, out var existingState)
                ? existingState
                : EmptyPendingInputStatePayload();
            var (supplementalEntries, queuedUserMessages, pendingSteers) =
                ResolveAuthoritativePendingInputStateCollections(currentState);
            var existingEntry =
                TryRemovePendingInputStateEntryByCorrelation(supplementalEntries, correlationId, out var existingIndex)
                ?? TryRemovePendingInputStateEntryByCorrelation(queuedUserMessages, correlationId, out existingIndex)
                ?? TryRemovePendingInputStateEntryByCorrelation(pendingSteers, correlationId, out existingIndex);

            if (IsTerminalPendingInputLifecycle(lifecycleState))
            {
            }
            else
            {
                var currentBucket = existingEntry?.PendingBucket;
                var normalizedInputs = NormalizeUserInputs(inputs);
                var updatedEntry = new PendingInputStateEntryPayload(
                    correlationId,
                    requestedMode.ToString(),
                    effectiveMode.ToString(),
                    lifecycleState,
                    Normalize(expectedTurnId),
                    Normalize(turnId),
                    null,
                    ResolvePendingInputStateEntryBucket(requestedMode, effectiveMode, lifecycleState, currentBucket),
                    normalizedInputs.Count == 0
                        ? NormalizeUserInputs(existingEntry?.Inputs)
                        : normalizedInputs);

                var targetEntries = ResolvePendingInputStateEntryCollection(
                    updatedEntry,
                    supplementalEntries,
                    queuedUserMessages,
                    pendingSteers);
                if (existingIndex >= 0 && existingIndex <= targetEntries.Count)
                {
                    targetEntries.Insert(existingIndex, updatedEntry);
                }
                else
                {
                    targetEntries.Add(updatedEntry);
                }
            }

            if (supplementalEntries.Count == 0
                && queuedUserMessages.Count == 0
                && pendingSteers.Count == 0)
            {
                pendingInputStatesByThread.Remove(normalizedThreadId!);
                return EmptyPendingInputStatePayload();
            }

            var interruptRequestPending = currentState.InterruptRequestPending;
            var submitPendingSteersAfterInterrupt = currentState.SubmitPendingSteersAfterInterrupt;
            if (requestedMode == FollowUpMode.Interrupt)
            {
                if (IsInterruptPendingLifecycle(lifecycleState))
                {
                    interruptRequestPending = true;
                    submitPendingSteersAfterInterrupt = submitPendingSteersAfterInterrupt || pendingSteers.Count > 0;
                }
                else if (string.Equals(lifecycleState, "interrupt_completed", StringComparison.Ordinal))
                {
                    interruptRequestPending = false;
                    submitPendingSteersAfterInterrupt = submitPendingSteersAfterInterrupt || pendingSteers.Count > 0;
                }
                else
                {
                    interruptRequestPending = false;
                    submitPendingSteersAfterInterrupt = false;
                }
            }

            var updatedState = BuildPendingInputStatePayload(
                supplementalEntries,
                interruptRequestPending,
                submitPendingSteersAfterInterrupt,
                queuedUserMessages,
                pendingSteers);
            pendingInputStatesByThread[normalizedThreadId!] = updatedState;
            return ClonePendingInputStatePayload(updatedState);
        }
    }

    private PendingInputStatePayload UpdatePendingInputStateSnapshot(
        string correlationId,
        FollowUpMode requestedMode,
        FollowUpMode effectiveMode,
        string lifecycleState,
        string? expectedTurnId,
        string? turnId,
        string? compareMessage,
        int imageCount)
        => UpdatePendingInputStateSnapshot(
            correlationId,
            requestedMode,
            effectiveMode,
            lifecycleState,
            expectedTurnId,
            turnId,
            compareMessage,
            imageCount,
            Array.Empty<AgentUserInput>());

    private PendingInputStatePayload GetPendingInputStateSnapshot(string? threadId)
    {
        var normalizedThreadId = Normalize(threadId);
        lock (pendingInputStateGate)
        {
            if (string.IsNullOrWhiteSpace(normalizedThreadId)
                || !pendingInputStatesByThread.TryGetValue(normalizedThreadId!, out var state)
                || IsPendingInputStatePayloadEmpty(state))
            {
                return EmptyPendingInputStatePayload();
            }

            return ClonePendingInputStatePayload(state);
        }
    }

    private void SetPendingInputStateSnapshot(string? threadId, PendingInputStatePayload? state)
    {
        var normalizedThreadId = Normalize(threadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            return;
        }

        var normalizedState = state is null
            ? EmptyPendingInputStatePayload()
            : CanonicalizePendingInputStatePayload(state);
        lock (pendingInputStateGate)
        {
            if (IsPendingInputStatePayloadEmpty(normalizedState))
            {
                pendingInputStatesByThread.Remove(normalizedThreadId!);
                pendingInputPersistenceVersionsByThread.TryRemove(normalizedThreadId!, out _);
                return;
            }

            pendingInputStatesByThread[normalizedThreadId!] = ClonePendingInputStatePayload(normalizedState);
        }
    }

    private ControlPlanePendingInputState HydratePendingInputStateSnapshot(string? threadId, ControlPlanePendingInputState? state)
    {
        if (state is not null)
        {
            SetPendingInputStateSnapshot(threadId, ToRuntimePendingInputStatePayload(state));
        }

        return ToControlPlanePendingInputState(GetPendingInputStateSnapshot(threadId))
               ?? EmptyControlPlanePendingInputState();
    }

    private PendingInputStatePayload ReducePendingInputStateForCompletedTurn(string? threadId, string? completedTurnId, string? status)
    {
        var normalizedThreadId = Normalize(threadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            return EmptyPendingInputStatePayload();
        }

        if (!string.Equals(Normalize(status), "interrupted", StringComparison.OrdinalIgnoreCase))
        {
            return GetPendingInputStateSnapshot(normalizedThreadId);
        }

        PendingInputStatePayload reducedState;
        PendingInputStateEntryPayload[] consumedPendingSteers = [];
        lock (pendingInputStateGate)
        {
            if (!pendingInputStatesByThread.TryGetValue(normalizedThreadId!, out var currentState)
                || IsPendingInputStatePayloadEmpty(currentState))
            {
                pendingInputStatesByThread.Remove(normalizedThreadId!);
                reducedState = EmptyPendingInputStatePayload();
            }
            else
            {
                var (supplementalEntries, queuedUserMessages, pendingSteers) =
                    ResolveAuthoritativePendingInputStateCollections(currentState);
                var consumedSupplementalEntries = supplementalEntries
                    .Where(entry => ShouldConsumePendingInputStateEntryForCompletedTurn(entry, completedTurnId))
                    .ToArray();
                var reducedSupplementalEntries = supplementalEntries
                    .Except(consumedSupplementalEntries)
                    .ToArray();
                consumedPendingSteers = consumedSupplementalEntries
                    .Where(entry => IsPendingSteerBucket(entry.PendingBucket))
                    .Concat(pendingSteers.Where(entry => ShouldConsumePendingInputStateEntryForCompletedTurn(entry, completedTurnId)))
                    .ToArray();
                var reducedPendingSteers = pendingSteers
                    .Where(entry => !ShouldConsumePendingInputStateEntryForCompletedTurn(entry, completedTurnId))
                    .ToArray();

                reducedState = BuildPendingInputStatePayload(
                    reducedSupplementalEntries,
                    interruptRequestPending: false,
                    submitPendingSteersAfterInterrupt: false,
                    queuedUserMessages,
                    reducedPendingSteers);

                if (IsPendingInputStatePayloadEmpty(reducedState))
                {
                    pendingInputStatesByThread.Remove(normalizedThreadId!);
                }
                else
                {
                    pendingInputStatesByThread[normalizedThreadId!] = ClonePendingInputStatePayload(reducedState);
                }

            }
        }

        foreach (var entry in consumedPendingSteers)
        {
            RaiseCommittedPendingSteer(normalizedThreadId, entry, completedTurnId);
        }

        SchedulePendingInputStatePersistence(normalizedThreadId, reducedState);
        return ClonePendingInputStatePayload(reducedState);
    }

    private void RaiseCommittedPendingSteer(string threadId, PendingInputStateEntryPayload entry, string? completedTurnId)
    {
        var requestedMode = ParseFollowUpModeOrDefault(entry.RequestedMode, FollowUpMode.Steer);
        var effectiveMode = ParseFollowUpModeOrDefault(entry.EffectiveMode, requestedMode);
        var pending = RemovePendingFollowUpCommit(threadId, entry.CorrelationId);
        var inputs = NormalizeUserInputs(pending?.Inputs ?? entry.Inputs);
        var text = ExtractConversationText(inputs)
                   ?? Normalize(pending?.CompareMessage)
                   ?? Normalize(entry.CompareKey?.Message);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var turnId = Normalize(entry.TurnId)
                     ?? Normalize(completedTurnId)
                     ?? Normalize(pending?.TurnId);
        var expectedTurnId = Normalize(entry.ExpectedTurnId) ?? Normalize(pending?.ExpectedTurnId);
        var imageCount = pending?.ImageCount ?? entry.CompareKey?.ImageCount ?? 0;

        RaisePendingFollowUpLifecycle(
            entry.CorrelationId,
            requestedMode,
            effectiveMode,
            "committed",
            expectedTurnId,
            turnId,
            text,
            imageCount,
            inputs);
        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.UserMessageCommitted,
            ThreadId = ToThreadId(threadId),
            TurnId = ToTurnId(turnId),
            ItemId = $"pending-follow-up-{entry.CorrelationId}",
            Status = "user_message",
            Phase = "committed",
            Text = text,
            SourceMethod = "pending-follow-up/reducer",
            PayloadKind = ControlPlaneConversationStreamPayloadKind.CommittedUserMessage,
            Payload = ToStructuredPayload(new CommittedUserMessagePayload(
                $"pending-follow-up-{entry.CorrelationId}",
                text,
                imageCount,
                entry.CorrelationId,
                inputs)),
            Message = "pending-follow-up/reducer",
        });
    }

    private PendingFollowUpCommit? RemovePendingFollowUpCommit(string? threadId, string? correlationId)
    {
        var normalizedThreadId = Normalize(threadId);
        var normalizedCorrelationId = Normalize(correlationId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId)
            || string.IsNullOrWhiteSpace(normalizedCorrelationId)
            || !pendingFollowUpCommitsByThread.TryGetValue(normalizedThreadId!, out var queue))
        {
            return null;
        }

        var snapshot = queue.ToArray();
        var matchIndex = Array.FindIndex(
            snapshot,
            item => string.Equals(item.CorrelationId, normalizedCorrelationId, StringComparison.Ordinal));
        if (matchIndex < 0)
        {
            return null;
        }

        var removed = snapshot[matchIndex];
        var remaining = snapshot
            .Where((_, index) => index != matchIndex)
            .ToArray();
        if (remaining.Length == 0)
        {
            pendingFollowUpCommitsByThread.TryRemove(normalizedThreadId!, out _);
        }
        else
        {
            pendingFollowUpCommitsByThread[normalizedThreadId!] = new ConcurrentQueue<PendingFollowUpCommit>(remaining);
        }

        return removed;
    }

    private static FollowUpMode ParseFollowUpModeOrDefault(string? value, FollowUpMode fallback)
        => Enum.TryParse(value, ignoreCase: true, out FollowUpMode parsed) ? parsed : fallback;

    private static bool ShouldConsumePendingInputStateEntryForCompletedTurn(PendingInputStateEntryPayload entry, string? completedTurnId)
        => IsPendingInputStateEntryOwnedByTurn(entry, completedTurnId)
           && (IsInterruptPendingInputStateEntry(entry) || IsPendingSteerBucket(entry.PendingBucket));

    private static bool IsPendingInputStateEntryOwnedByTurn(PendingInputStateEntryPayload entry, string? turnId)
    {
        var normalizedTurnId = Normalize(turnId);
        if (string.IsNullOrWhiteSpace(normalizedTurnId))
        {
            return false;
        }

        var entryTurnId = Normalize(entry.TurnId);
        if (!string.IsNullOrWhiteSpace(entryTurnId))
        {
            return string.Equals(entryTurnId, normalizedTurnId, StringComparison.Ordinal);
        }

        return string.Equals(Normalize(entry.ExpectedTurnId), normalizedTurnId, StringComparison.Ordinal);
    }

    private void RemovePendingInputStateThread(string? threadId)
    {
        var normalizedThreadId = Normalize(threadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            return;
        }

        lock (pendingInputStateGate)
        {
            pendingInputStatesByThread.Remove(normalizedThreadId!);
        }

        pendingInputPersistenceVersionsByThread.TryRemove(normalizedThreadId!, out _);
    }

    private void ClearThreadLocalState()
    {
        pendingFollowUpCommitsByThread.Clear();
        pendingFollowUpDispatchCancellationsByCorrelation.Clear();
        pendingFollowUpDispatchMutationReasonsByCorrelation.Clear();
        pendingInputPersistenceVersionsByThread.Clear();
        lock (pendingInputStateGate)
        {
            pendingInputStatesByThread.Clear();
        }

        lock (observedTurnStateGate)
        {
            observedTurnIdsByThread.Clear();
        }
    }

    private static PendingInputStatePayload BuildPendingInputStatePayload(
        IReadOnlyList<PendingInputStateEntryPayload> entries,
        bool interruptRequestPending = false,
        bool submitPendingSteersAfterInterrupt = false,
        IReadOnlyList<PendingInputStateEntryPayload>? queuedUserMessages = null,
        IReadOnlyList<PendingInputStateEntryPayload>? pendingSteers = null)
    {
        var normalizedEntries = FilterSupplementalPendingInputStateEntries(
                NormalizePendingInputStateEntries(entries).ToArray())
            .ToArray();
        var normalizedQueuedUserMessages = NormalizePendingInputStateEntries(
                queuedUserMessages,
                forcedPendingBucket: "QueuedUserMessage")
            .ToArray();
        var normalizedPendingSteers = NormalizePendingInputStateEntries(
                pendingSteers,
                forcedPendingBucket: "PendingSteer")
            .ToArray();

        var hasPendingSteerEntries = normalizedPendingSteers.Length > 0;
        return new PendingInputStatePayload(
            normalizedEntries,
            interruptRequestPending,
            submitPendingSteersAfterInterrupt && hasPendingSteerEntries,
            normalizedQueuedUserMessages,
            normalizedPendingSteers);
    }

    private static PendingInputStatePayload CanonicalizePendingInputStatePayload(PendingInputStatePayload state)
    {
        var normalizedEntries = NormalizePendingInputStateEntries(state.Entries).ToArray();
        var normalizedQueuedUserMessages = NormalizePendingInputStateEntries(
                state.QueuedUserMessages,
                forcedPendingBucket: "QueuedUserMessage")
            .ToArray();
        var normalizedPendingSteers = NormalizePendingInputStateEntries(
                state.PendingSteers,
                forcedPendingBucket: "PendingSteer")
            .ToArray();

        return BuildPendingInputStatePayload(
            normalizedEntries,
            state.InterruptRequestPending,
            state.SubmitPendingSteersAfterInterrupt,
            normalizedQueuedUserMessages,
            normalizedPendingSteers);
    }

    private static (
        List<PendingInputStateEntryPayload> SupplementalEntries,
        List<PendingInputStateEntryPayload> QueuedUserMessages,
        List<PendingInputStateEntryPayload> PendingSteers)
        ResolveAuthoritativePendingInputStateCollections(PendingInputStatePayload state)
    {
        var normalizedEntries = FilterSupplementalPendingInputStateEntries(
                NormalizePendingInputStateEntries(state.Entries).ToArray())
            .ToArray();
        var normalizedQueuedUserMessages = NormalizePendingInputStateEntries(
                state.QueuedUserMessages,
                forcedPendingBucket: "QueuedUserMessage")
            .ToArray();
        var normalizedPendingSteers = NormalizePendingInputStateEntries(
                state.PendingSteers,
                forcedPendingBucket: "PendingSteer")
            .ToArray();

        return (
            normalizedEntries.ToList(),
            normalizedQueuedUserMessages.ToList(),
            normalizedPendingSteers.ToList());
    }

    private static PendingInputStatePayload EmptyPendingInputStatePayload()
        => new(
            Array.Empty<PendingInputStateEntryPayload>(),
            false,
            false,
            Array.Empty<PendingInputStateEntryPayload>(),
            Array.Empty<PendingInputStateEntryPayload>());

    private static bool IsPendingInputStatePayloadEmpty(PendingInputStatePayload state)
        => state.Entries.Count == 0
           && (state.QueuedUserMessages?.Count ?? 0) == 0
           && (state.PendingSteers?.Count ?? 0) == 0;

    private static bool IsTerminalPendingInputLifecycle(string lifecycleState)
        => lifecycleState is "committed"
            or "cancelled"
            or "dropped"
            or "promoted"
            or "dispatch_failed"
            or "interrupt_failed"
            or "interrupt_timeout";

    private static bool IsInterruptPendingLifecycle(string lifecycleState)
        => lifecycleState is "queued" or "interrupt_requested";

    private static bool IsInterruptPendingInputStateEntry(PendingInputStateEntryPayload entry)
        => string.Equals(entry.RequestedMode, nameof(FollowUpMode.Interrupt), StringComparison.OrdinalIgnoreCase)
            && (IsInterruptPendingLifecycle(entry.LifecycleState)
                || string.Equals(entry.LifecycleState, "interrupt_completed", StringComparison.Ordinal));

    private static PendingInputStateEntryPayload? TryRemovePendingInputStateEntryByCorrelation(
        List<PendingInputStateEntryPayload> entries,
        string correlationId,
        out int index)
    {
        index = entries.FindIndex(entry => string.Equals(entry.CorrelationId, correlationId, StringComparison.Ordinal));
        if (index < 0)
        {
            return null;
        }

        var entry = entries[index];
        entries.RemoveAt(index);
        return entry;
    }

    private static List<PendingInputStateEntryPayload> ResolvePendingInputStateEntryCollection(
        PendingInputStateEntryPayload entry,
        List<PendingInputStateEntryPayload> supplementalEntries,
        List<PendingInputStateEntryPayload> queuedUserMessages,
        List<PendingInputStateEntryPayload> pendingSteers)
    {
        if (IsPendingSteerBucket(entry.PendingBucket))
        {
            return pendingSteers;
        }

        if (IsQueuedUserMessageEntry(entry))
        {
            return queuedUserMessages;
        }

        return supplementalEntries;
    }

    private void SchedulePendingInputStatePersistence(string? threadId, PendingInputStatePayload state)
    {
        var normalizedThreadId = Normalize(threadId);
        if (!pendingInputStateKernelPersistenceEnabled
            || string.IsNullOrWhiteSpace(normalizedThreadId)
            || process is null
            || stdin is null)
        {
            return;
        }

        var snapshot = ClonePendingInputStatePayload(state);
        var version = pendingInputPersistenceVersionsByThread.AddOrUpdate(
            normalizedThreadId!,
            static _ => 1L,
            static (_, current) => current + 1L);

        _ = Task.Run(
            async () =>
            {
                try
                {
                    await PersistPendingInputStateSnapshotAsync(normalizedThreadId!, snapshot, version).ConfigureAwait(false);
                }
                catch
                {
                    // pending input 持久化失败不应阻塞主链路；后续状态变更会继续覆盖。
                }
            });
    }

    private async Task PersistPendingInputStateSnapshotAsync(
        string threadId,
        PendingInputStatePayload state,
        long version)
    {
        var gateHeld = false;
        try
        {
            await pendingInputPersistenceGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            gateHeld = true;

            if (!pendingInputPersistenceVersionsByThread.TryGetValue(threadId, out var latestVersion)
                || latestVersion != version)
            {
                return;
            }

            _ = await SendRpcAsync(
                    "tianshu/thread/pending_input/update",
                    new Dictionary<string, object?>
                    {
                        ["threadId"] = threadId,
                        ["pendingInputState"] = ToPendingInputStateObject(state),
                    },
                    CancellationToken.None,
                    TimeSpan.FromSeconds(15))
                .ConfigureAwait(false);
        }
        finally
        {
            if (gateHeld)
            {
                pendingInputPersistenceGate.Release();
            }
        }
    }

    private static object ToPendingInputStateObject(PendingInputStatePayload state)
        => new
        {
            entries = NormalizePendingInputStateEntries(state.Entries)
                .Select(SerializePendingInputStateEntry)
                .ToArray(),
            queuedUserMessages = NormalizePendingInputStateEntries(
                    state.QueuedUserMessages,
                    forcedPendingBucket: "QueuedUserMessage")
                .Select(SerializePendingInputStateEntry)
                .ToArray(),
            pendingSteers = NormalizePendingInputStateEntries(
                    state.PendingSteers,
                    forcedPendingBucket: "PendingSteer")
                .Select(SerializePendingInputStateEntry)
                .ToArray(),
            interruptRequestPending = state.InterruptRequestPending,
            submitPendingSteersAfterInterrupt = state.SubmitPendingSteersAfterInterrupt,
        };

    private static PendingInputStatePayload ClonePendingInputStatePayload(PendingInputStatePayload state)
        => new(
            NormalizePendingInputStateEntries(state.Entries).ToArray(),
            state.InterruptRequestPending,
            state.SubmitPendingSteersAfterInterrupt,
            NormalizePendingInputStateEntries(state.QueuedUserMessages, forcedPendingBucket: "QueuedUserMessage").ToArray(),
            NormalizePendingInputStateEntries(state.PendingSteers, forcedPendingBucket: "PendingSteer").ToArray());

    private static List<PendingInputStateEntryPayload> NormalizePendingInputStateEntries(
        IReadOnlyList<PendingInputStateEntryPayload>? entries,
        string? forcedPendingBucket = null)
    {
        if (entries is null || entries.Count == 0)
        {
            return [];
        }

        var normalized = new List<PendingInputStateEntryPayload>(entries.Count);
        foreach (var entry in entries)
        {
            var correlationId = Normalize(entry.CorrelationId);
            if (string.IsNullOrWhiteSpace(correlationId))
            {
                continue;
            }

            normalized.Add(new PendingInputStateEntryPayload(
                correlationId!,
                Normalize(entry.RequestedMode) ?? string.Empty,
                Normalize(entry.EffectiveMode) ?? string.Empty,
                Normalize(entry.LifecycleState) ?? string.Empty,
                Normalize(entry.ExpectedTurnId),
                Normalize(entry.TurnId),
                null,
                forcedPendingBucket
                ?? Normalize(entry.PendingBucket)
                ?? "QueuedUserMessage",
                NormalizeUserInputs(entry.Inputs)));
        }

        return normalized;
    }

    private static PendingInputStateEntryPayload[] FilterSupplementalPendingInputStateEntries(
        IReadOnlyList<PendingInputStateEntryPayload> entries)
        => entries
            .Where(static entry =>
                !IsQueuedUserMessageEntry(entry)
                && !IsPendingSteerBucket(entry.PendingBucket))
            .ToArray();

    private static object SerializePendingInputStateEntry(PendingInputStateEntryPayload entry)
        => new
        {
            correlationId = entry.CorrelationId,
            requestedMode = entry.RequestedMode,
            effectiveMode = entry.EffectiveMode,
            lifecycleState = entry.LifecycleState,
            expectedTurnId = entry.ExpectedTurnId,
            turnId = entry.TurnId,
            pendingBucket = entry.PendingBucket,
            inputs = SerializePendingInputStateInputs(entry.Inputs),
        };

    private static object[] SerializePendingInputStateInputs(IReadOnlyList<AgentUserInput>? inputs)
        => NormalizeUserInputs(inputs)
            .Select(SerializePendingInputStateInput)
            .ToArray();

    private static object SerializePendingInputStateInput(AgentUserInput input)
        => input switch
        {
            TextUserInput textInput => new
            {
                type = Normalize(textInput.Type) ?? "text",
                text = textInput.Text,
                textElements = textInput.TextElements.Select(static item => new
                {
                    byteRange = new
                    {
                        start = item.ByteRange.Start,
                        end = item.ByteRange.End,
                    },
                    placeholder = item.Placeholder,
                }).ToArray(),
            },
            ImageUserInput imageInput => new
            {
                type = Normalize(imageInput.Type) ?? "image",
                url = imageInput.Url,
            },
            LocalImageUserInput localImageInput => new
            {
                type = Normalize(localImageInput.Type) ?? "localImage",
                path = localImageInput.Path,
            },
            SkillUserInput skillInput => new
            {
                type = Normalize(skillInput.Type) ?? "skill",
                name = skillInput.Name,
                path = skillInput.Path,
            },
            MentionUserInput mentionInput => new
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

    private static string ResolvePendingInputStateEntryBucket(
        FollowUpMode requestedMode,
        FollowUpMode effectiveMode,
        string lifecycleState,
        string? currentBucket = null)
    {
        if (!IsTerminalPendingInputLifecycle(lifecycleState))
        {
            if (IsPendingSteerBucket(currentBucket))
            {
                return "PendingSteer";
            }

            if (IsQueuedUserMessageBucket(currentBucket))
            {
                return "QueuedUserMessage";
            }
        }

        return !IsTerminalPendingInputLifecycle(lifecycleState)
               && effectiveMode == FollowUpMode.Steer
               && requestedMode == FollowUpMode.Steer
            ? "PendingSteer"
            : "QueuedUserMessage";
    }

    private static bool IsQueuedUserMessageEntry(PendingInputStateEntryPayload entry)
        => IsQueuedUserMessageBucket(entry.PendingBucket)
           && !IsInterruptPendingInputStateEntry(entry);

    private static bool IsPendingSteerBucket(string? pendingBucket)
        => string.Equals(pendingBucket, "PendingSteer", StringComparison.OrdinalIgnoreCase);

    private static bool IsQueuedUserMessageBucket(string? pendingBucket)
        => string.Equals(pendingBucket, "QueuedUserMessage", StringComparison.OrdinalIgnoreCase);

    private static void AddStructuredValueIfPresent(IDictionary<string, object?> payload, string key, StructuredValue? value)
    {
        if (value is not null)
        {
            payload[key] = value.ToPlainObject();
        }
    }

    private static void AddStructuredObjectIfPresent(
        IDictionary<string, object?> payload,
        string key,
        IReadOnlyDictionary<string, AgentStructuredValue>? values)
    {
        if (values is null || values.Count == 0)
        {
            return;
        }

        payload[key] = values.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.ToPlainObject(),
            StringComparer.Ordinal);
    }

    private static void AddStructuredValuesIfPresent(IDictionary<string, object?> payload, string key, IReadOnlyList<AgentStructuredValue>? values)
    {
        if (values is null)
        {
            return;
        }

        payload[key] = values.Select(static value => value.ToPlainObject()).ToArray();
    }

    private static void AddDynamicToolsIfPresent(
        IDictionary<string, object?> payload,
        string key,
        IReadOnlyList<AgentDynamicToolSpec>? values)
    {
        if (values is null)
        {
            return;
        }

        payload[key] = values.Select(static value => value.ToPlainObject()).ToArray();
    }

    private static void AddSessionSourceIfPresent(
        IDictionary<string, object?> payload,
        string key,
        ControlPlaneSessionSource? value)
    {
        if (value is null)
        {
            return;
        }

        payload[key] = value;
    }

    private static void AddResponseItemsIfPresent(
        IDictionary<string, object?> payload,
        string key,
        IReadOnlyList<AgentResponseItem>? values)
    {
        if (values is null)
        {
            return;
        }

        payload[key] = values.Select(static value => value.ToPlainObject()).ToArray();
    }

    private static string? ResolveThreadOption(string? requestValue, string? optionsValue)
        => Normalize(requestValue) ?? Normalize(optionsValue);

    private static AgentServiceTierOverride ToAgentServiceTierOverride(string? value)
    {
        var normalized = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return AgentServiceTierOverride.Unspecified;
        }

        if (string.Equals(normalized, "null", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "<null>", StringComparison.OrdinalIgnoreCase))
        {
            return AgentServiceTierOverride.Clear;
        }

        return AgentServiceTierOverride.FromValue(AgentServiceTier.Parse(normalized));
    }

    private static AgentApprovalPolicy? ToAgentApprovalPolicy(string? value)
    {
        var normalized = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (AgentApprovalPolicy.TryParse(normalized, out var scalarPolicy))
        {
            return scalarPolicy;
        }

        var trimmed = normalized.Trim();
        if (trimmed.StartsWith('{'))
        {
            return JsonSerializer.Deserialize<AgentApprovalPolicy>(trimmed)
                   ?? throw new FormatException($"不支持的 approvalPolicy：{value}");
        }

        throw new FormatException($"不支持的 approvalPolicy：{value}");
    }

    private static AgentServiceTier? ToAgentServiceTier(string? value)
    {
        var normalized = Normalize(value);
        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : AgentServiceTier.Parse(normalized);
    }

    private static AgentPersonality? ToAgentPersonality(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : AgentPersonality.Parse(value);

    private static ApprovalResponse ToRuntimeApprovalResponse(ControlPlaneApprovalResolution command)
        => command.Decision switch
        {
            ControlPlaneApprovalDecision.Approve => ApprovalResponse.Accept(command.Note),
            ControlPlaneApprovalDecision.ApproveForSession => ApprovalResponse.AcceptForSession(command.Note),
            ControlPlaneApprovalDecision.ApproveAndRemember => ApprovalResponse.AcceptAndRemember(command.Note),
            ControlPlaneApprovalDecision.ApproveWithExecutionPolicyAmendment
                => ApprovalResponse.AcceptWithExecPolicyAmendment(
                    new ExecPolicyAmendmentPayload(command.CommandPrefix),
                    command.Note),
            ControlPlaneApprovalDecision.ApplyNetworkPolicyAmendment
                => ApprovalResponse.ApplyNetworkPolicyAmendment(
                    new NetworkPolicyAmendmentPayload(command.NetworkHost ?? string.Empty, command.NetworkAction ?? "allow"),
                    command.Note),
            ControlPlaneApprovalDecision.Cancel => ApprovalResponse.Cancel(command.Note),
            _ => ApprovalResponse.Decline(command.Note),
        };

    private static PermissionGrantResponse ToRuntimePermissionGrantResponse(ControlPlanePermissionGrant command)
        => new(
            ToAgentStructuredDictionary(command.Permissions)
            ?? new Dictionary<string, AgentStructuredValue>(StringComparer.Ordinal),
            command.Scope == ControlPlanePermissionScope.Session ? PermissionGrantScope.Session : PermissionGrantScope.Turn);

    private static UserInputSubmission ToRuntimeUserInputSubmission(ControlPlaneUserInputSubmission command)
        => new(
            ToAgentStructuredDictionary(command.Answers)
            ?? new Dictionary<string, AgentStructuredValue>(StringComparer.Ordinal));

    private static ProviderApprovalOutcome ToProviderApprovalOutcome(ApprovalResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return response.EffectiveDecision switch
        {
            ApprovalResponseDecision.Accept => new ProviderApprovalOutcome(ProviderApprovalDecision.Accept, response.Note),
            ApprovalResponseDecision.AcceptForSession => new ProviderApprovalOutcome(ProviderApprovalDecision.AcceptForSession, response.Note),
            ApprovalResponseDecision.AcceptAndRemember => new ProviderApprovalOutcome(ProviderApprovalDecision.AcceptAndRemember, response.Note),
            ApprovalResponseDecision.AcceptWithExecPolicyAmendment => new ProviderApprovalOutcome(
                ProviderApprovalDecision.AcceptWithExecPolicyAmendment,
                response.Note,
                response.ExecPolicyAmendment?.CommandPrefix?.ToArray()),
            ApprovalResponseDecision.ApplyNetworkPolicyAmendment => new ProviderApprovalOutcome(
                ProviderApprovalDecision.ApplyNetworkPolicyAmendment,
                response.Note,
                null,
                response.NetworkPolicyAmendment?.Host,
                response.NetworkPolicyAmendment?.Action),
            ApprovalResponseDecision.Cancel => new ProviderApprovalOutcome(ProviderApprovalDecision.Cancel, response.Note),
            _ => new ProviderApprovalOutcome(ProviderApprovalDecision.Decline, response.Note),
        };
    }

    private static ProviderPermissionGrantOutcome ToProviderPermissionGrantOutcome(PermissionGrantResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return new ProviderPermissionGrantOutcome(
            ToContractStructuredDictionary(response.Permissions),
            response.Scope == PermissionGrantScope.Session ? ProviderPermissionScope.Session : ProviderPermissionScope.Turn);
    }

    private static ProviderUserInputOutcome ToProviderUserInputOutcome(UserInputSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);

        return new ProviderUserInputOutcome(ToContractStructuredDictionary(submission.Answers));
    }

    private static IReadOnlyDictionary<string, AgentStructuredValue>? ToAgentStructuredDictionary(
        IReadOnlyDictionary<string, StructuredValue>? values)
    {
        if (values is null)
        {
            return null;
        }

        return values.ToDictionary(
            static pair => pair.Key,
            static pair => ToAgentStructuredValue(pair.Value) ?? AgentStructuredValue.Null,
            StringComparer.Ordinal);
    }

    private static AgentStructuredValue? ToAgentStructuredValue(StructuredValue? value)
        => value is null ? null : AgentStructuredValue.FromPlainObject(value.ToPlainObject());

    private static IReadOnlyDictionary<string, StructuredValue> ToContractStructuredDictionary(
        IReadOnlyDictionary<string, AgentStructuredValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count == 0)
        {
            return new Dictionary<string, StructuredValue>(StringComparer.Ordinal);
        }

        return values.ToDictionary(
            static pair => pair.Key,
            static pair => ToContractStructuredValue(pair.Value) ?? StructuredValue.Null,
            StringComparer.Ordinal);
    }

    private static IReadOnlyList<AgentResponseItem>? ToAgentResponseItems(IReadOnlyList<StructuredValue>? values)
    {
        if (values is null)
        {
            return null;
        }

        if (values.Count == 0)
        {
            return Array.Empty<AgentResponseItem>();
        }

        return values.Select(
            static value =>
            {
                var element = JsonSerializer.SerializeToElement(value.ToPlainObject(), StructuredJsonOptions);
                return element.Deserialize<AgentResponseItem>(StructuredJsonOptions)
                       ?? throw new InvalidOperationException("无法将线程 history 项还原为 AgentResponseItem。");
            }).ToArray();
    }

    private static IReadOnlyList<AgentDynamicToolSpec>? ToAgentDynamicTools(IReadOnlyList<ControlPlaneDynamicToolSpec>? values)
    {
        if (values is null)
        {
            return null;
        }

        return values.Select(static value => new AgentDynamicToolSpec
        {
            Name = value.Name,
            Description = value.Description,
            InputSchema = ToAgentStructuredValue(value.InputSchema) ?? AgentStructuredValue.Null,
        }).ToArray();
    }

    private static AgentServiceTierOverride ResolveThreadOption(
        AgentServiceTierOverride requestValue,
        AgentServiceTierOverride optionsValue)
        => requestValue.IsSpecified ? requestValue : optionsValue;

    private static void AddServiceTierIfPresent(
        IDictionary<string, object?> payload,
        string key,
        AgentServiceTierOverride requestValue,
        AgentServiceTierOverride optionsValue)
    {
        var resolved = ResolveThreadOption(requestValue, optionsValue);
        if (!resolved.IsSpecified)
        {
            return;
        }

        payload[key] = resolved.Value?.Value;
    }

    private static void AddApprovalPolicyIfPresent(
        IDictionary<string, object?> payload,
        string key,
        AgentApprovalPolicy? requestValue,
        AgentApprovalPolicy? optionsValue)
    {
        var resolved = requestValue ?? optionsValue;
        if (resolved is not null)
        {
            payload[key] = resolved.ToPlainObject();
        }
    }

    private static void AddPersonalityIfPresent(
        IDictionary<string, object?> payload,
        string key,
        AgentPersonality? value)
    {
        if (value is not null)
        {
            payload[key] = value.Value;
        }
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

    private static bool TryGetArray(JsonElement element, string propertyName, out JsonElement property)
    {
        if (TryGetProperty(element, propertyName, out property) && property.ValueKind == JsonValueKind.Array)
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

    private static ControlPlaneReviewStartResult ParseReviewStartResult(JsonElement result)
        => new()
        {
            ReviewThreadId = ReadString(result, "reviewThreadId") ?? string.Empty,
            Turn = TryGetObject(result, "turn") is { } turn ? ParseReviewTurn(turn) : null,
        };

    private static ControlPlaneReviewTurn ParseReviewTurn(JsonElement turn)
        => new()
        {
            Id = ReadString(turn, "id") ?? string.Empty,
            Status = ReadString(turn, "status") ?? string.Empty,
            DisplayText = ReadFirstTurnItemText(turn),
        };

    private static ControlPlaneExperimentalFeatureCatalogResult ParseExperimentalFeatureListResult(JsonElement result)
    {
        var items = new List<ControlPlaneExperimentalFeatureDescriptor>();
        if (result.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                items.Add(new ControlPlaneExperimentalFeatureDescriptor
                {
                    Name = ReadString(item, "name") ?? string.Empty,
                    Stage = ReadString(item, "stage") ?? string.Empty,
                    DisplayName = ReadString(item, "displayName"),
                    Description = ReadString(item, "description"),
                    Announcement = ReadString(item, "announcement"),
                    Enabled = ReadBool(item, "enabled") ?? false,
                    DefaultEnabled = ReadBool(item, "defaultEnabled") ?? false,
                });
            }
        }

        return new ControlPlaneExperimentalFeatureCatalogResult
        {
            NextCursor = ReadString(result, "nextCursor"),
            Items = items,
        };
    }

    private static ControlPlaneCollaborationModeCatalogResult ParseCollaborationModeListResult(JsonElement result)
    {
        var items = new List<ControlPlaneCollaborationModeDescriptor>();
        if (result.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                items.Add(new ControlPlaneCollaborationModeDescriptor
                {
                    Name = ReadString(item, "name") ?? string.Empty,
                    Mode = ReadString(item, "mode"),
                    Model = ReadString(item, "model"),
                    ReasoningEffort = ReadString(item, "reasoning_effort") ?? ReadString(item, "reasoningEffort"),
                });
            }
        }

        return new ControlPlaneCollaborationModeCatalogResult
        {
            Items = items,
        };
    }

    private static ControlPlaneMcpServerCatalogResult ParseMcpServerStatusListResult(JsonElement result)
    {
        var items = new List<ControlPlaneMcpServerDescriptor>();
        if (result.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                items.Add(new ControlPlaneMcpServerDescriptor
                {
                    Name = ReadString(item, "name") ?? string.Empty,
                    AuthStatus = ReadString(item, "authStatus") ?? string.Empty,
                    ToolNames = ReadObjectPropertyNames(item, "tools"),
                    ResourceUris = ReadObjectStringArray(item, "resources", "uri"),
                    ResourceTemplateUris = ReadObjectStringArray(item, "resourceTemplates", "uriTemplate"),
                });
            }
        }

        return new ControlPlaneMcpServerCatalogResult
        {
            NextCursor = ReadString(result, "nextCursor"),
            Items = items,
        };
    }

    private static ControlPlaneMcpServerReloadResult ParseMcpServerReloadResult(JsonElement result)
    {
        _ = result;
        return new ControlPlaneMcpServerReloadResult();
    }

    private static ControlPlaneProviderPackageReloadResult ParseProviderPackageReloadResult(JsonElement result)
        => new()
        {
            LoadedAssemblyCount = ReadInt32(result, "loadedAssemblyCount") ?? 0,
            IssueCount = ReadInt32(result, "issueCount") ?? 0,
            SupportedProtocolAdapterIds = ReadStringArray(result, "supportedProtocolAdapterIds"),
            SupportedWireApis = ReadStringArray(result, "supportedWireApis"),
            Issues = ReadStringArray(result, "issues"),
        };

    private static ControlPlaneMcpServerOauthLoginStartResult ParseMcpServerOauthLoginStartResult(JsonElement result)
        => new()
        {
            AuthorizationUrl = ReadString(result, "authorizationUrl"),
        };

    private static ControlPlaneConversationArtifact? ParseConversationSummaryResult(JsonElement result)
        => TryGetObject(result, "summary") is { } summary ? ParseConversationSummary(summary) : null;

    private static ControlPlaneConversationArtifact ParseConversationSummary(JsonElement summary)
    {
        var gitInfo = TryGetObject(summary, "gitInfo");
        var gitSha = gitInfo is { } gitInfoValue ? ReadString(gitInfoValue, "sha") : null;
        var gitBranch = gitInfo is { } gitInfoBranch ? ReadString(gitInfoBranch, "branch") : null;
        var gitOriginUrl = gitInfo is { } gitInfoOrigin ? ReadString(gitInfoOrigin, "originUrl") : null;
        return new ControlPlaneConversationArtifact
        {
            ConversationId = ReadString(summary, "conversationId") ?? string.Empty,
            Path = ReadString(summary, "path") ?? string.Empty,
            Preview = ReadString(summary, "preview") ?? string.Empty,
            Timestamp = ReadString(summary, "timestamp"),
            UpdatedAt = ReadString(summary, "updatedAt"),
            ModelProvider = ReadString(summary, "modelProvider") ?? string.Empty,
            WorkingDirectory = ReadString(summary, "cwd") ?? string.Empty,
            CliVersion = ReadString(summary, "cliVersion") ?? string.Empty,
            Source = ReadString(summary, "source") ?? string.Empty,
            GitSha = gitSha,
            GitBranch = gitBranch,
            GitOriginUrl = gitOriginUrl,
        };
    }

    private static ControlPlaneAppCatalogResult ParseAppListResult(JsonElement result)
    {
        var items = new List<ControlPlaneAppDescriptor>();
        if (result.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                items.Add(ParseAppDescriptor(item));
            }
        }

        return new ControlPlaneAppCatalogResult
        {
            NextCursor = ReadString(result, "nextCursor"),
            Items = items,
        };
    }

    private static ControlPlaneAppDescriptor ParseAppDescriptor(JsonElement item)
        => new()
        {
            Id = ReadString(item, "id") ?? string.Empty,
            Name = ReadString(item, "name") ?? string.Empty,
            Description = ReadString(item, "description"),
            LogoUrl = ReadString(item, "logoUrl"),
            LogoUrlDark = ReadString(item, "logoUrlDark"),
            DistributionChannel = ReadString(item, "distributionChannel"),
            Branding = ReadCatalogStructuredValue(item, "branding"),
            Metadata = ReadCatalogStructuredValue(item, "appMetadata") ?? ReadCatalogStructuredValue(item, "metadata"),
            Labels = ReadStringDictionary(item, "labels"),
            InstallUrl = ReadString(item, "installUrl"),
            IsAccessible = ReadBool(item, "isAccessible") ?? false,
            IsEnabled = ReadBool(item, "isEnabled") ?? true,
            PluginDisplayNames = ReadStringArray(item, "pluginDisplayNames"),
        };

    private static ControlPlaneGitDiffArtifact ParseGitDiffToRemoteResult(JsonElement result)
        => new()
        {
            HasChanges = ReadBool(result, "hasChanges") ?? false,
            Diff = ReadString(result, "diff") ?? string.Empty,
        };

    private static IReadOnlyList<ControlPlaneSkillDescriptor> ParseSkillDescriptorArray(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ControlPlaneSkillDescriptor>();
        }

        var items = new List<ControlPlaneSkillDescriptor>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            items.Add(new ControlPlaneSkillDescriptor
            {
                Name = ReadString(item, "name") ?? string.Empty,
                Description = ReadString(item, "description") ?? string.Empty,
                ShortDescription = ReadString(item, "shortDescription"),
                PathToSkillsMd = ReadString(item, "pathToSkillsMd") ?? string.Empty,
                Path = ReadString(item, "path") ?? string.Empty,
                Scope = ReadString(item, "scope") ?? string.Empty,
                Enabled = ReadBool(item, "enabled") ?? false,
                Interface = ReadCatalogStructuredValue(item, "interface"),
                Dependencies = ReadCatalogStructuredValue(item, "dependencies"),
                PermissionProfile = ReadCatalogStructuredValue(item, "permissionProfile"),
                ManagedNetworkOverride = ReadCatalogStructuredValue(item, "managedNetworkOverride"),
            });
        }

        return items;
    }

    private static IReadOnlyList<ControlPlaneSkillError> ParseSkillErrorArray(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ControlPlaneSkillError>();
        }

        var items = new List<ControlPlaneSkillError>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            items.Add(new ControlPlaneSkillError
            {
                Path = ReadString(item, "path") ?? string.Empty,
                Message = ReadString(item, "message") ?? string.Empty,
            });
        }

        return items;
    }

    private static IReadOnlyList<ControlPlanePluginMarketplace> ParsePluginMarketplaceArray(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ControlPlanePluginMarketplace>();
        }

        var items = new List<ControlPlanePluginMarketplace>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            items.Add(new ControlPlanePluginMarketplace
            {
                Name = ReadString(item, "name") ?? string.Empty,
                Path = ReadString(item, "path") ?? ReadString(item, "marketplacePath") ?? string.Empty,
                Plugins = ParsePluginSummaryArray(item, "plugins"),
            });
        }

        return items;
    }

    private static IReadOnlyList<ControlPlanePluginSummary> ParsePluginSummaryArray(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ControlPlanePluginSummary>();
        }

        var items = new List<ControlPlanePluginSummary>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            items.Add(ParsePluginSummary(item));
        }

        return items;
    }

    private static ControlPlanePluginSummary ParsePluginSummary(JsonElement item)
        => new()
        {
            Id = ReadString(item, "id") ?? string.Empty,
            Name = ReadString(item, "name") ?? string.Empty,
            Installed = ReadBool(item, "installed") ?? false,
            Enabled = ReadBool(item, "enabled") ?? false,
            InstallPolicy = ReadString(item, "installPolicy") ?? string.Empty,
            AuthPolicy = ReadString(item, "authPolicy") ?? string.Empty,
            Source = ReadCatalogStructuredValue(item, "source"),
            Interface = ReadCatalogStructuredValue(item, "interface"),
        };

    private static ControlPlanePluginDetail ParsePluginDetail(JsonElement item)
        => new()
        {
            MarketplaceName = ReadString(item, "marketplaceName") ?? string.Empty,
            MarketplacePath = ReadString(item, "marketplacePath") ?? string.Empty,
            Summary = TryGetObject(item, "summary") is { } summary ? ParsePluginSummary(summary) : new ControlPlanePluginSummary(),
            Description = ReadString(item, "description"),
            Skills = ParsePluginSkillReferenceArray(item, "skills"),
            Apps = ParsePluginAppReferenceArray(item, "apps"),
            McpServers = ReadStringArray(item, "mcpServers"),
        };

    private static IReadOnlyList<ControlPlanePluginSkillReference> ParsePluginSkillReferenceArray(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ControlPlanePluginSkillReference>();
        }

        var items = new List<ControlPlanePluginSkillReference>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            items.Add(new ControlPlanePluginSkillReference
            {
                Name = ReadString(item, "name") ?? string.Empty,
                Description = ReadString(item, "description") ?? string.Empty,
                ShortDescription = ReadString(item, "shortDescription"),
                Path = ReadString(item, "path") ?? string.Empty,
                Interface = ReadCatalogStructuredValue(item, "interface"),
            });
        }

        return items;
    }

    private static IReadOnlyList<ControlPlanePluginAppReference> ParsePluginAppReferenceArray(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ControlPlanePluginAppReference>();
        }

        var items = new List<ControlPlanePluginAppReference>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            items.Add(new ControlPlanePluginAppReference
            {
                Id = ReadString(item, "id") ?? string.Empty,
                Name = ReadString(item, "name") ?? string.Empty,
                Description = ReadString(item, "description"),
                InstallUrl = ReadString(item, "installUrl"),
            });
        }

        return items;
    }

    private static StructuredValue? ReadCatalogStructuredValue(JsonElement item, string propertyName)
        => TryGetProperty(item, propertyName, out var property) ? ParseStructuredValue(property) : null;

    private static StructuredValue ParseStructuredValue(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => StructuredValue.FromObject(
                element.EnumerateObject().ToDictionary(
                    static property => property.Name,
                    static property => ParseStructuredValue(property.Value),
                    StringComparer.Ordinal)),
            JsonValueKind.Array => StructuredValue.FromArray(element.EnumerateArray().Select(ParseStructuredValue).ToArray()),
            JsonValueKind.String => StructuredValue.FromString(element.GetString() ?? string.Empty),
            JsonValueKind.Number => StructuredValue.FromNumber(element.GetRawText()),
            JsonValueKind.True => StructuredValue.FromBoolean(true),
            JsonValueKind.False => StructuredValue.FromBoolean(false),
            JsonValueKind.Null or JsonValueKind.Undefined => StructuredValue.Null,
            _ => StructuredValue.FromString(element.GetRawText()),
        };

    private static ControlPlaneModelCatalogResult ParseModelCatalogResult(JsonElement result)
    {
        var items = new List<ControlPlaneModelCatalogItem>();
        if (result.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                items.Add(new ControlPlaneModelCatalogItem
                {
                    Id = ReadString(item, "id") ?? string.Empty,
                    Model = ReadString(item, "model") ?? string.Empty,
                    DisplayName = ReadString(item, "displayName") ?? string.Empty,
                    DefaultReasoningEffort = ReadString(item, "defaultReasoningEffort") ?? "medium",
                    SupportedReasoningEfforts = ReadObjectStringArray(item, "supportedReasoningEfforts", "reasoningEffort"),
                    InputModalities = ReadStringArray(item, "inputModalities"),
                    SupportsPersonality = ReadBool(item, "supportsPersonality") ?? false,
                    Hidden = ReadBool(item, "hidden") ?? false,
                    IsDefault = ReadBool(item, "isDefault") ?? false,
                    SupportsParallelToolCalls = ReadBool(item, "supportsParallelToolCalls") ?? false,
                    SupportsReasoningSummaries = ReadBool(item, "supportsReasoningSummaries") ?? false,
                    DefaultReasoningSummary = ReadString(item, "defaultReasoningSummary"),
                    SupportsVerbosity = ReadBool(item, "supportsVerbosity") ?? false,
                    DefaultVerbosity = ReadString(item, "defaultVerbosity"),
                    PreferWebsocketTransport = ReadBool(item, "preferWebsocketTransport") ?? false,
                    Description = ReadString(item, "description") ?? string.Empty,
                    AvailabilityNuxMessage = ReadString(item, "availabilityNux", "message"),
                    UpgradeModel = ReadString(item, "upgrade"),
                    UpgradeMigrationMarkdown = ReadString(item, "upgradeInfo", "migrationMarkdown"),
                });
            }
        }

        return new ControlPlaneModelCatalogResult
        {
            NextCursor = ReadString(result, "nextCursor"),
            Items = items,
        };
    }

    private static ResolvedToolCatalogSnapshot ParseResolvedToolCatalogSnapshot(JsonElement result)
    {
        if (!TryGetProperty(result, "items", out var itemsElement) || itemsElement.ValueKind != JsonValueKind.Array)
        {
            return new ResolvedToolCatalogSnapshot();
        }

        var items = new List<ResolvedToolCatalogItem>();
        foreach (var item in itemsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = Normalize(ReadString(item, "name"));
            var description = Normalize(ReadString(item, "description"));
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description))
            {
                continue;
            }

            items.Add(new ResolvedToolCatalogItem(
                name!,
                description!,
                ReadToolImplementationKind(item, "implementationKind"),
                ReadBool(item, "available") ?? false,
                ReadBool(item, "modelVisible") ?? false,
                ReadString(item, "reason"),
                ReadString(item, "implementationId"),
                ReadToolRuntimeRequirements(item, "requirements"),
                ReadToolFallbackPolicy(item, "fallbackPolicy"),
                ReadPlatformToolProfile(item, "platformProfile")));
        }

        return new ResolvedToolCatalogSnapshot(items);
    }

    private static ToolImplementationKind ReadToolImplementationKind(JsonElement json, string propertyName)
    {
        if (!TryGetProperty(json, propertyName, out var property))
        {
            return ToolImplementationKind.Unavailable;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var numeric))
        {
            return Enum.IsDefined(typeof(ToolImplementationKind), numeric)
                ? (ToolImplementationKind)numeric
                : ToolImplementationKind.Unavailable;
        }

        if (property.ValueKind == JsonValueKind.String
            && Enum.TryParse<ToolImplementationKind>(property.GetString(), ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return ToolImplementationKind.Unavailable;
    }

    private static IReadOnlyList<ToolRuntimeRequirement> ReadToolRuntimeRequirements(JsonElement json, string propertyName)
    {
        if (!TryGetProperty(json, propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ToolRuntimeRequirement>();
        }

        var requirements = new List<ToolRuntimeRequirement>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var key = Normalize(ReadString(item, "key"));
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            requirements.Add(new ToolRuntimeRequirement(
                key!,
                ReadString(item, "displayName"),
                ReadString(item, "description"),
                ReadBool(item, "required") ?? true));
        }

        return requirements;
    }

    private static ToolFallbackPolicy? ReadToolFallbackPolicy(JsonElement json, string propertyName)
    {
        if (!TryGetProperty(json, propertyName, out var policy) || policy.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var strategy = Normalize(ReadString(policy, "strategy"));
        if (string.IsNullOrWhiteSpace(strategy))
        {
            return null;
        }

        return new ToolFallbackPolicy(
            strategy!,
            ReadToolImplementationKindArray(policy, "preferredImplementationKinds"),
            ReadString(policy, "description"));
    }

    private static IReadOnlyList<ToolImplementationKind> ReadToolImplementationKindArray(JsonElement json, string propertyName)
    {
        if (!TryGetProperty(json, propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ToolImplementationKind>();
        }

        return array
            .EnumerateArray()
            .Select(static item =>
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var numeric))
                {
                    return Enum.IsDefined(typeof(ToolImplementationKind), numeric)
                        ? (ToolImplementationKind?)numeric
                        : null;
                }

                if (item.ValueKind == JsonValueKind.String
                    && Enum.TryParse<ToolImplementationKind>(item.GetString(), ignoreCase: true, out var parsed))
                {
                    return parsed;
                }

                return null;
            })
            .Where(static item => item.HasValue)
            .Select(static item => item!.Value)
            .ToArray();
    }

    private static PlatformToolProfile? ReadPlatformToolProfile(JsonElement json, string propertyName)
    {
        if (!TryGetProperty(json, propertyName, out var profile) || profile.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var platform = Normalize(ReadString(profile, "platform"));
        if (string.IsNullOrWhiteSpace(platform))
        {
            return null;
        }

        return new PlatformToolProfile(
            platform!,
            ReadStringArray(profile, "enabledToolKeys"),
            ReadStringArray(profile, "disabledToolKeys"),
            ReadToolImplementationKindArray(profile, "defaultImplementationKinds"));
    }

    private ControlPlaneThreadOperationResult ParseThreadOperationResult(JsonElement result)
    {
        var response = TryDeserializeThreadResponse(result);
        var parsedThread = TryGetObject(result, "thread") is { } thread
            ? ParseThreadDetails(thread, result)
            : response?.Thread is not null
                ? ParseThreadDetails(response.Thread, response)
                : null;
        if (parsedThread is null)
        {
            return new ControlPlaneThreadOperationResult();
        }

        return new ControlPlaneThreadOperationResult
        {
            Thread = ToControlPlaneThreadDetail(SyncThreadPendingInputState(parsedThread, result)),
        };
    }

    private ControlPlaneAgentThreadRegistrationResult ParseAgentThreadRegistrationResult(JsonElement result)
    {
        var threadResult = ParseThreadOperationResult(result);
        return new ControlPlaneAgentThreadRegistrationResult
        {
            Agent = ToControlPlaneAgentDescriptor(threadResult.Thread),
        };
    }

    private static ControlPlaneLoadedThreadListResult ParseThreadLoadedListResult(JsonElement result)
        => new()
        {
            ThreadIds = ReadStringArray(result, "data")
                .Select(static item => Normalize(item))
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Select(static item => new ThreadId(item!))
                .ToArray(),
            NextCursor = ReadString(result, "nextCursor"),
        };

    private static ControlPlaneThreadUnsubscribeResult ParseThreadUnsubscribeResult(JsonElement result)
        => new()
        {
            Status = ReadString(result, "status") ?? string.Empty,
        };

    private static ControlPlaneThreadElicitationResult ParseThreadElicitationResult(JsonElement result)
        => new()
        {
            Count = ReadUnsignedLong(result, "count") ?? 0,
            Paused = ReadBool(result, "paused") ?? false,
        };

    private AgentThreadDetails? ParseThreadDetails(JsonElement data, JsonElement? response = null)
    {
        var threadId = Normalize(ReadString(data, "id"));
        if (string.IsNullOrWhiteSpace(threadId))
        {
            return null;
        }

        return new AgentThreadDetails
        {
            Id = threadId,
            Preview = ReadString(data, "preview") ?? string.Empty,
            Name = ReadString(data, "name"),
            Cwd = ReadString(data, "cwd"),
            Path = ReadString(data, "path"),
            ModelProvider = ReadString(data, "modelProvider"),
            Source = TryGetPropertyValue(data, "source") is { } source ? ControlPlaneSessionSource.FromJsonElement(source) : null,
            CliVersion = ReadString(data, "cliVersion"),
            AgentNickname = ReadString(data, "agentNickname"),
            AgentRole = ReadString(data, "agentRole"),
            CreatedAt = ReadUnixTime(data, "createdAt"),
            UpdatedAt = ReadUnixTime(data, "updatedAt") ?? DateTimeOffset.Now,
            Ephemeral = ReadBool(data, "ephemeral") ?? false,
            Status = TryGetObject(data, "status") is { } status ? ParseThreadStatus(status) : null,
            GitInfo = TryGetObject(data, "gitInfo") is { } gitInfo ? ParseThreadGitInfo(gitInfo) : null,
            Turns = ParseThreadTurns(data),
            SeedHistory = ParseThreadSeedHistory(data),
            PendingInputState = ToControlPlanePendingInputState(ParsePendingInputStatePayload(data)),
            PendingInteractiveRequests = ParsePendingInteractiveRequests(data).Select(ToControlPlanePendingInteractiveRequest).ToArray(),
            SessionConfiguration = ParseThreadSessionConfiguration(response ?? data, data),
            SessionState = ParseThreadSessionProjection(TryGetObject(data, "sessionState")),
        };
    }

    private static ControlPlaneAgentDescriptor? ToControlPlaneAgentDescriptor(AgentThreadDetails? thread)
    {
        if (thread is null)
        {
            return null;
        }

        return new ControlPlaneAgentDescriptor
        {
            ThreadId = new ThreadId(thread.Id),
            Preview = thread.Preview,
            Name = thread.Name,
            WorkingDirectory = thread.Cwd,
            Path = thread.Path,
            Source = thread.Source?.GetThreadSourceKind().Value,
            AgentNickname = thread.AgentNickname,
            AgentRole = thread.AgentRole,
            CreatedAt = thread.CreatedAt,
            UpdatedAt = thread.UpdatedAt,
            IsEphemeral = thread.Ephemeral,
            Status = thread.Status?.Type,
            ActiveFlags = thread.Status?.ActiveFlags ?? Array.Empty<string>(),
            Lineage = thread.Source?.SubAgentSource is null
                ? null
                : new ControlPlaneAgentLineage
                {
                    ParentThreadId = string.IsNullOrWhiteSpace(thread.Source.SubAgentSource.ParentThreadId)
                        ? null
                        : new ThreadId(thread.Source.SubAgentSource.ParentThreadId),
                    Depth = thread.Source.SubAgentSource.Depth,
                },
        };
    }

    private static ControlPlaneAgentDescriptor? ToControlPlaneAgentDescriptor(ControlPlaneThreadDetail? thread)
    {
        if (thread is null)
        {
            return null;
        }

        return new ControlPlaneAgentDescriptor
        {
            ThreadId = thread.ThreadId,
            Preview = thread.Preview,
            Name = thread.Name,
            WorkingDirectory = thread.WorkingDirectory,
            Path = thread.Path,
            Source = thread.Source?.Value,
            AgentNickname = thread.AgentNickname,
            AgentRole = thread.AgentRole,
            CreatedAt = thread.CreatedAt,
            UpdatedAt = thread.UpdatedAt,
            IsEphemeral = thread.IsEphemeral,
            Status = thread.Status,
            ActiveFlags = thread.ActiveFlags,
            Lineage = thread.ParentThreadId is null && thread.LineageDepth is null
                ? null
                : new ControlPlaneAgentLineage
                {
                    ParentThreadId = thread.ParentThreadId,
                    Depth = thread.LineageDepth ?? 0,
                },
        };
    }

    private static AgentThreadDetails? ParseThreadDetails(
        AppServerThreadSummaryDto data,
        AppServerThreadResponseDto? response = null)
    {
        var threadId = Normalize(data.Id);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            return null;
        }

        return new AgentThreadDetails
        {
            Id = threadId,
            Preview = Normalize(data.Preview) ?? string.Empty,
            Name = Normalize(data.Name),
            Cwd = Normalize(data.Cwd),
            Path = Normalize(data.Path),
            ModelProvider = Normalize(data.ModelProvider),
            Source = ParseSessionSource(data.Source),
            CliVersion = Normalize(data.CliVersion),
            AgentNickname = Normalize(data.AgentNickname),
            AgentRole = Normalize(data.AgentRole),
            CreatedAt = ReadUnixTime(data.CreatedAt),
            UpdatedAt = ReadUnixTime(data.UpdatedAt) ?? DateTimeOffset.Now,
            Ephemeral = data.Ephemeral ?? false,
            Status = ParseThreadStatus(data.Status),
            GitInfo = ParseThreadGitInfo(data.GitInfo),
            Turns = ParseThreadTurnsElement(data.Turns),
            SeedHistory = Array.Empty<AgentThreadSeedHistoryItem>(),
            PendingInputState = null,
            PendingInteractiveRequests = Array.Empty<ControlPlanePendingInteractiveRequest>(),
            SessionConfiguration = ParseThreadSessionConfiguration(CreateEmptyObjectElement(), null, response, data),
            SessionState = ParseThreadSessionProjection(data.SessionState),
        };
    }

    private static AgentThreadSessionProjection? ParseThreadSessionProjection(AppServerThreadSessionProjectionDto? data)
    {
        if (data is null)
        {
            return null;
        }

        var sessionId = Normalize(data.SessionId);
        var title = Normalize(data.Title);
        var collaborationSpaceId = Normalize(data.CollaborationSpaceId);
        var collaborationSpaceKey = Normalize(data.CollaborationSpaceKey);
        var collaborationSpaceDisplayName = Normalize(data.CollaborationSpaceDisplayName);
        var sessionMode = Normalize(data.SessionMode)?.ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(sessionId)
            || string.IsNullOrWhiteSpace(title)
            || string.IsNullOrWhiteSpace(collaborationSpaceId)
            || string.IsNullOrWhiteSpace(collaborationSpaceKey)
            || string.IsNullOrWhiteSpace(collaborationSpaceDisplayName)
            || string.IsNullOrWhiteSpace(sessionMode))
        {
            return null;
        }

        return new AgentThreadSessionProjection
        {
            SessionId = sessionId,
            Title = title,
            CollaborationSpaceId = collaborationSpaceId,
            CollaborationSpaceKey = collaborationSpaceKey,
            CollaborationSpaceDisplayName = collaborationSpaceDisplayName,
            SessionMode = sessionMode,
            IsClosed = data.IsClosed ?? false,
            ActiveThreadId = Normalize(data.ActiveThreadId),
            HasActiveTurn = data.HasActiveTurn ?? false,
            Orchestration = ParseThreadOrchestrationProjection(data.Orchestration),
        };
    }

    private static AgentThreadOrchestrationProjection? ParseThreadOrchestrationProjection(
        AppServerThreadOrchestrationProjectionDto? data)
    {
        if (data is null)
        {
            return null;
        }

        var currentStageId = Normalize(data.CurrentStageId);
        var lastDecision = ParseThreadOrchestratorDecisionProjection(data.LastDecision);
        var lastContextPackage = ParseThreadStageContextPackageProjection(data.LastContextPackage);
        var contextLedgerSegments = data.ContextLedgerSegments?
            .Select(ParseThreadStageContextSegmentProjection)
            .Where(static item => item is not null)
            .Cast<AgentThreadStageContextSegmentProjection>()
            .ToArray() ?? Array.Empty<AgentThreadStageContextSegmentProjection>();
        var checkpoints = data.Checkpoints?
            .Select(ParseThreadStageCheckpointProjection)
            .Where(static item => item is not null)
            .Cast<AgentThreadStageCheckpointProjection>()
            .ToArray() ?? Array.Empty<AgentThreadStageCheckpointProjection>();

        if (currentStageId is null
            && lastDecision is null
            && lastContextPackage is null
            && contextLedgerSegments.Length == 0
            && checkpoints.Length == 0)
        {
            return null;
        }

        return new AgentThreadOrchestrationProjection
        {
            CurrentStageId = currentStageId,
            LastDecision = lastDecision,
            LastContextPackage = lastContextPackage,
            ContextLedgerSegments = contextLedgerSegments,
            Checkpoints = checkpoints,
        };
    }

    private static AgentThreadOrchestratorDecisionProjection? ParseThreadOrchestratorDecisionProjection(
        AppServerThreadOrchestratorDecisionProjectionDto? data)
    {
        var decisionId = Normalize(data?.DecisionId);
        var selectedStageId = Normalize(data?.SelectedStageId);
        if (decisionId is null || selectedStageId is null)
        {
            return null;
        }

        return new AgentThreadOrchestratorDecisionProjection
        {
            DecisionId = decisionId,
            SelectedStageId = selectedStageId,
            CandidateStageIds = NormalizeDistinctStrings(data?.CandidateStageIds),
            ReasonCode = Normalize(data?.ReasonCode) ?? "unspecified",
            PreviousStageId = Normalize(data?.PreviousStageId),
            ContextProjectionReason = Normalize(data?.ContextProjectionReason),
            PolicyHits = NormalizeDistinctStrings(data?.PolicyHits),
            DecidedAt = data?.DecidedAt ?? DateTimeOffset.UtcNow,
        };
    }

    private static AgentThreadStageContextPackageProjection? ParseThreadStageContextPackageProjection(
        AppServerThreadStageContextPackageProjectionDto? data)
    {
        var packageId = Normalize(data?.PackageId);
        var stageId = Normalize(data?.StageId);
        if (packageId is null || stageId is null)
        {
            return null;
        }

        return new AgentThreadStageContextPackageProjection
        {
            PackageId = packageId,
            StageId = stageId,
            ProjectionMode = Normalize(data?.ProjectionMode) ?? string.Empty,
            BudgetTokens = data?.BudgetTokens,
            SourceCheckpointIds = NormalizeDistinctStrings(data?.SourceCheckpointIds),
            SegmentCount = Math.Max(0, data?.SegmentCount ?? 0),
            ArtifactRefCount = Math.Max(0, data?.ArtifactRefCount ?? 0),
        };
    }

    private static AgentThreadStageContextSegmentProjection? ParseThreadStageContextSegmentProjection(
        AppServerThreadStageContextSegmentProjectionDto? data)
    {
        var kind = Normalize(data?.Kind);
        var content = Normalize(data?.Content);
        if (kind is null || content is null)
        {
            return null;
        }

        return new AgentThreadStageContextSegmentProjection
        {
            Kind = kind,
            Content = content,
            Title = Normalize(data?.Title),
            Source = ParseThreadResourceRefProjection(data?.Source),
            Required = data?.Required ?? false,
            EstimatedTokens = data?.EstimatedTokens,
        };
    }

    private static AgentThreadStageCheckpointProjection? ParseThreadStageCheckpointProjection(
        AppServerThreadStageCheckpointProjectionDto? data)
    {
        var checkpointId = Normalize(data?.CheckpointId);
        var stageId = Normalize(data?.StageId);
        if (checkpointId is null || stageId is null)
        {
            return null;
        }

        return new AgentThreadStageCheckpointProjection
        {
            CheckpointId = checkpointId,
            StageId = stageId,
            State = Normalize(data?.State) ?? string.Empty,
            StartedAt = data?.StartedAt ?? DateTimeOffset.MinValue,
            CompletedAt = data?.CompletedAt,
            Summary = Normalize(data?.Summary),
            ArtifactRefs = data?.ArtifactRefs?
                .Select(ParseThreadArtifactRefProjection)
                .Where(static item => item is not null)
                .Cast<AgentThreadArtifactRefProjection>()
                .ToArray() ?? Array.Empty<AgentThreadArtifactRefProjection>(),
            ModelRouteSetId = Normalize(data?.ModelRouteSetId),
            ModelRouteKind = Normalize(data?.ModelRouteKind),
            Diagnostics = data?.Diagnostics,
            NextStageSuggestions = NormalizeDistinctStrings(data?.NextStageSuggestions),
        };
    }

    private static AgentThreadArtifactRefProjection? ParseThreadArtifactRefProjection(
        AppServerThreadArtifactRefProjectionDto? data)
    {
        var id = Normalize(data?.Id);
        if (id is null)
        {
            return null;
        }

        return new AgentThreadArtifactRefProjection
        {
            Id = id,
            Name = Normalize(data?.Name),
            Kind = Normalize(data?.Kind),
        };
    }

    private static AgentThreadResourceRefProjection? ParseThreadResourceRefProjection(
        AppServerThreadResourceRefProjectionDto? data)
    {
        var kind = Normalize(data?.Kind);
        var key = Normalize(data?.Key);
        if (kind is null || key is null)
        {
            return null;
        }

        return new AgentThreadResourceRefProjection
        {
            Kind = kind,
            Key = key,
        };
    }

    private static AgentThreadSessionProjection? ParseThreadSessionProjection(JsonElement? data)
    {
        if (!data.HasValue || data.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        try
        {
            var dto = AppServerJsonHelpers.Deserialize<AppServerThreadSessionProjectionDto>(data.Value);
            return ParseThreadSessionProjection(dto);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ControlPlaneSessionSource? ParseSessionSource(JsonElement data)
        => data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? null
            : ControlPlaneSessionSource.FromJsonElement(data);

    private static SessionOverviewProjection? ToSessionOverviewProjection(AgentThreadSessionProjection? state)
    {
        if (state is null)
        {
            return null;
        }

        var sessionId = Normalize(state.SessionId);
        var title = Normalize(state.Title);
        var collaborationSpaceId = Normalize(state.CollaborationSpaceId);
        var collaborationSpaceKey = Normalize(state.CollaborationSpaceKey);
        var collaborationSpaceDisplayName = Normalize(state.CollaborationSpaceDisplayName);
        var sessionMode = ParseSessionMode(state.SessionMode);
        if (string.IsNullOrWhiteSpace(sessionId)
            || string.IsNullOrWhiteSpace(title)
            || string.IsNullOrWhiteSpace(collaborationSpaceId)
            || string.IsNullOrWhiteSpace(collaborationSpaceKey)
            || string.IsNullOrWhiteSpace(collaborationSpaceDisplayName)
            || sessionMode is null)
        {
            return null;
        }

        return new SessionOverviewProjection(
            new SessionId(sessionId),
            title,
            new CollaborationSpaceRef(
                new CollaborationSpaceId(collaborationSpaceId),
                collaborationSpaceKey,
                collaborationSpaceDisplayName),
            sessionMode.Value,
            string.IsNullOrWhiteSpace(state.ActiveThreadId) ? null : new ThreadId(state.ActiveThreadId),
            state.HasActiveTurn,
            state.IsClosed);
    }

    private static SessionMode? ParseSessionMode(string? mode)
        => Normalize(mode)?.ToLowerInvariant() switch
        {
            "interactive" => SessionMode.Interactive,
            "planning" => SessionMode.Planning,
            "review" => SessionMode.Review,
            "automation" => SessionMode.Automation,
            _ => null,
        };

    private static AgentThreadStatus ParseThreadStatus(JsonElement data)
        => new()
        {
            Type = ReadString(data, "type") ?? string.Empty,
            ActiveFlags = ReadStringArray(data, "activeFlags"),
        };

    private static AgentThreadStatus? ParseThreadStatus(AppServerThreadStatusDto? data)
    {
        if (data is null)
        {
            return null;
        }

        return new AgentThreadStatus
        {
            Type = Normalize(data.Type) ?? string.Empty,
            ActiveFlags = data.ActiveFlags?.Where(static flag => !string.IsNullOrWhiteSpace(flag)).ToArray() ?? Array.Empty<string>(),
        };
    }

    private static AgentThreadGitInfo ParseThreadGitInfo(JsonElement data)
        => new()
        {
            Sha = ReadString(data, "sha"),
            Branch = ReadString(data, "branch"),
            OriginUrl = ReadString(data, "originUrl"),
        };

    private static AgentThreadGitInfo? ParseThreadGitInfo(AppServerThreadGitInfoDto? data)
    {
        if (data is null)
        {
            return null;
        }

        return new AgentThreadGitInfo
        {
            Sha = Normalize(data.Sha),
            Branch = Normalize(data.Branch),
            OriginUrl = Normalize(data.OriginUrl),
        };
    }

    private static AgentThreadSessionConfiguration? ParseThreadSessionConfiguration(
        JsonElement response,
        JsonElement? thread,
        AppServerThreadResponseDto? responseDto = null,
        AppServerThreadSummaryDto? threadDto = null)
    {
        AppServerThreadSessionConfigurationDto? parsed = TryDeserializeThreadSessionConfiguration(response);
        var nestedSessionConfiguration = thread.HasValue
            ? TryGetObject(thread.Value, "sessionConfiguration")
            : null;
        if (nestedSessionConfiguration.HasValue)
        {
            parsed = TryDeserializeThreadSessionConfiguration(nestedSessionConfiguration.Value) ?? parsed;
        }

        var sandbox = TryGetObject(responseDto?.Sandbox)
                      ?? TryGetObject(response, "sandbox");

        if (parsed is null)
        {
            return null;
        }

        var rolloutPath = Normalize(parsed.RolloutPath)
            ?? Normalize(threadDto?.Path)
            ?? (thread.HasValue ? Normalize(ReadString(thread.Value, "path")) : null);
        var hasConfigurationSignal = parsed.HasAnyValue(rolloutPath);

        if (!hasConfigurationSignal)
        {
            return null;
        }

        return new AgentThreadSessionConfiguration
        {
            Model = Normalize(parsed.Model),
            ModelProvider = parsed.ResolvedModelProvider,
            ModelProviderId = Normalize(parsed.ModelProviderId)
                              ?? parsed.ResolvedModelProvider,
            ModelRouteSetId = parsed.ResolvedModelRouteSetId,
            ServiceTier = ToAgentServiceTier(parsed.ServiceTier),
            ApprovalPolicy = ToAgentApprovalPolicy(parsed.ApprovalPolicy),
            SandboxPolicy = parsed.ResolvedSandboxPolicy
                            ?? ResolveSandboxMode(sandbox),
            SandboxPolicyPayload = ToStructuredValue(parsed.SandboxPolicyPayload)
                                    ?? ToStructuredValue(parsed.SandboxPolicy)
                                    ?? ToStructuredValue(sandbox),
            ReasoningEffort = parsed.ResolvedReasoningEffort,
            HistoryLogId = Normalize(parsed.HistoryLogId),
            HistoryEntryCount = parsed.HistoryEntryCount,
            RolloutPath = rolloutPath,
            ForkedFromId = Normalize(parsed.ForkedFromId),
            Cwd = Normalize(parsed.Cwd)
                  ?? Normalize(responseDto?.Cwd)
                  ?? Normalize(threadDto?.Cwd)
                  ?? Normalize(ReadString(response, "cwd"))
                  ?? (thread.HasValue ? Normalize(ReadString(thread.Value, "cwd")) : null),
            Ephemeral = parsed.Ephemeral
                        ?? threadDto?.Ephemeral
                        ?? ReadBool(response, "ephemeral")
                        ?? (thread.HasValue ? ReadBool(thread.Value, "ephemeral") : null),
            AllowLoginShell = parsed.AllowLoginShell,
            ShellEnvironmentPolicy = ToStructuredValue(parsed.ShellEnvironmentPolicy),
            ProviderBaseUrl = Normalize(parsed.ProviderBaseUrl),
            ProviderApiKeyEnvironmentVariable = Normalize(parsed.ProviderApiKeyEnvironmentVariable),
            ProviderWireApi = Normalize(parsed.ProviderWireApi),
            ProviderRequestMaxRetries = parsed.ProviderRequestMaxRetries,
            ProviderStreamMaxRetries = parsed.ProviderStreamMaxRetries,
            ProviderStreamIdleTimeoutMs = parsed.ProviderStreamIdleTimeoutMs,
            ProviderWebsocketConnectTimeoutMs = parsed.ProviderWebsocketConnectTimeoutMs,
            ProviderSupportsWebsockets = parsed.ProviderSupportsWebsockets,
            WebSearchMode = Normalize(parsed.WebSearchMode),
            ServiceName = Normalize(parsed.ServiceName),
            BaseInstructions = Normalize(parsed.BaseInstructions),
            DeveloperInstructions = Normalize(parsed.DeveloperInstructions),
            UserInstructions = Normalize(parsed.UserInstructions),
            ReasoningSummary = Normalize(parsed.ReasoningSummary),
            Verbosity = Normalize(parsed.Verbosity),
            Personality = Normalize(parsed.Personality),
            DynamicTools = ToStructuredValueList(parsed.DynamicTools),
            CollaborationMode = ToStructuredValue(parsed.CollaborationMode),
            PersistExtendedHistory = parsed.PersistExtendedHistory,
            SessionSource = ParseSessionSource(parsed.SessionSource),
            WindowsSandboxLevel = Normalize(parsed.WindowsSandboxLevel),
            DefaultModeRequestUserInputEnabled = parsed.DefaultModeRequestUserInputEnabled,
        };
    }

    private static string? ResolveSandboxMode(JsonElement? element)
    {
        if (!element.HasValue)
        {
            return null;
        }

        return element.Value.ValueKind switch
        {
            JsonValueKind.String => Normalize(element.Value.GetString()),
            JsonValueKind.Object when element.Value.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
                => Normalize(type.GetString()),
            _ => null,
        };
    }

    private static AgentStructuredValue? ToStructuredValue(JsonElement element)
        => element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? null
            : AgentStructuredValue.FromJsonElement(element);

    private static AgentStructuredValue? ToStructuredValue(JsonElement? element)
        => element.HasValue ? ToStructuredValue(element.Value) : null;

    private static IReadOnlyList<AgentStructuredValue>? ToStructuredValueList(JsonElement element)
        => element.ValueKind == JsonValueKind.Array
            ? element.EnumerateArray().Select(AgentStructuredValue.FromJsonElement).ToArray()
            : null;

    private static IReadOnlyList<AgentStructuredValue>? ToStructuredValueList(JsonElement? element)
        => element.HasValue ? ToStructuredValueList(element.Value) : null;

    private static IReadOnlyList<ConversationMessage> ParseThreadConversationMessages(
        AppServerThreadResponseDto? responseDto,
        AppServerThreadSummaryDto? threadDto,
        JsonElement response,
        JsonElement? thread,
        IReadOnlyList<AgentThreadSeedHistoryItem> seedHistory,
        IReadOnlyList<AgentThreadTurn> turns)
    {
        JsonElement messagesArray;
        if (TryGetArray(response, "messages", out messagesArray)
            || (thread.HasValue && TryGetArray(thread.Value, "messages", out messagesArray)))
        {
            var parsedMessages = new List<ConversationMessage>();
            foreach (var messageElement in messagesArray.EnumerateArray())
            {
                if (!TryParseConversationMessage(messageElement, out var message))
                {
                    continue;
                }

                parsedMessages.Add(message);
            }

            if (parsedMessages.Count > 0 || messagesArray.GetArrayLength() == 0)
            {
                return parsedMessages;
            }
        }

        return BuildMessagesFromThreadHistory(seedHistory, turns);
    }

    private static IReadOnlyList<AgentThreadTurn> ParseThreadTurns(JsonElement data)
    {
        if (!data.TryGetProperty("turns", out var turnsElement) || turnsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<AgentThreadTurn>();
        }

        return ParseThreadTurnsElement(turnsElement);
    }

    private static IReadOnlyList<AgentThreadTurn> ParseThreadTurnsElement(JsonElement turnsElement)
    {
        if (turnsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<AgentThreadTurn>();
        }

        var turns = new List<AgentThreadTurn>();
        foreach (var turnElement in turnsElement.EnumerateArray())
        {
            if (turnElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            turns.Add(new AgentThreadTurn
            {
                Id = ReadString(turnElement, "id") ?? string.Empty,
                Status = ReadString(turnElement, "status") ?? string.Empty,
                Error = TryGetObject(turnElement, "error") is { } error ? ParseThreadTurnError(error) : null,
                Items = ParseThreadTurnItems(turnElement),
            });
        }

        return turns;
    }

    private static AgentThreadTurnError ParseThreadTurnError(JsonElement data)
        => new()
        {
            Message = ReadString(data, "message"),
            AdditionalDetails = ReadString(data, "additionalDetails"),
        };

    private static IReadOnlyList<AgentThreadTurnItem> ParseThreadTurnItems(JsonElement data)
    {
        if (!data.TryGetProperty("items", out var itemsElement) || itemsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<AgentThreadTurnItem>();
        }

        var items = new List<AgentThreadTurnItem>();
        foreach (var itemElement in itemsElement.EnumerateArray())
        {
            if (itemElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            items.Add(ParseThreadTurnItem(itemElement));
        }

        return items;
    }

    private static AgentThreadTurnItem ParseThreadTurnItem(JsonElement itemElement)
    {
        var itemId = ReadString(itemElement, "id") ?? string.Empty;
        var itemType = ReadString(itemElement, "type") ?? string.Empty;

        return NormalizeThreadTurnItemType(itemType) switch
        {
            "usermessage" => new UserMessageThreadItem
            {
                Id = itemId,
                Type = itemType,
                Content = ParseThreadUserInputs(itemElement),
            },
            "assistantmessage" or "agentmessage" => new AgentMessageThreadItem
            {
                Id = itemId,
                Type = itemType,
                MessageText = ReadThreadTurnItemText(itemElement) ?? string.Empty,
                MessagePhase = ReadString(itemElement, "phase"),
            },
            "plan" => new PlanThreadItem
            {
                Id = itemId,
                Type = itemType,
                PlanText = ReadThreadTurnItemText(itemElement) ?? string.Empty,
            },
            "reasoning" => new ReasoningThreadItem
            {
                Id = itemId,
                Type = itemType,
                Summary = ReadStringArray(itemElement, "summary"),
                Content = ReadStringArray(itemElement, "content"),
            },
            "commandexecution" => new CommandExecutionThreadItem
            {
                Id = itemId,
                Type = itemType,
                Command = ReadString(itemElement, "command") ?? string.Empty,
                CommandActions = ParseCommandActions(itemElement),
                Cwd = ReadString(itemElement, "cwd") ?? string.Empty,
                Status = ReadString(itemElement, "status") ?? string.Empty,
                AggregatedOutput = ReadString(itemElement, "aggregatedOutput") ?? ReadString(itemElement, "aggregated_output"),
                ExitCode = ReadInt32(itemElement, "exitCode") ?? ReadInt32(itemElement, "exit_code"),
                DurationMs = ReadInt32(itemElement, "durationMs") ?? ReadInt32(itemElement, "duration_ms"),
                ProcessId = ReadString(itemElement, "processId") ?? ReadString(itemElement, "process_id"),
            },
            "filechange" => new FileChangeThreadItem
            {
                Id = itemId,
                Type = itemType,
                Status = ReadString(itemElement, "status") ?? string.Empty,
                Changes = ParseFileUpdateChanges(itemElement),
            },
            "mcptoolcall" => new McpToolCallThreadItem
            {
                Id = itemId,
                Type = itemType,
                Server = ReadString(itemElement, "server") ?? string.Empty,
                Tool = ReadString(itemElement, "tool") ?? string.Empty,
                Arguments = ReadStructuredValue(itemElement, "arguments"),
                Status = ReadString(itemElement, "status") ?? string.Empty,
                Result = ParseMcpToolCallResult(itemElement),
                Error = ParseMcpToolCallError(itemElement),
                DurationMs = ReadInt32(itemElement, "durationMs") ?? ReadInt32(itemElement, "duration_ms"),
            },
            "dynamictoolcall" => new DynamicToolCallThreadItem
            {
                Id = itemId,
                Type = itemType,
                Tool = ReadString(itemElement, "tool") ?? string.Empty,
                Arguments = ReadStructuredValue(itemElement, "arguments"),
                ContentItems = ParseDynamicToolCallContentItems(itemElement),
                Status = ReadString(itemElement, "status") ?? string.Empty,
                Success = ReadBool(itemElement, "success"),
                DurationMs = ReadInt32(itemElement, "durationMs") ?? ReadInt32(itemElement, "duration_ms"),
            },
            "collabagenttoolcall" => new CollabAgentToolCallThreadItem
            {
                Id = itemId,
                Type = itemType,
                SenderThreadId = ReadString(itemElement, "senderThreadId") ?? ReadString(itemElement, "sender_thread_id") ?? string.Empty,
                ReceiverThreadIds = ReadStringArray(itemElement, "receiverThreadIds", "receiver_thread_ids"),
                Tool = ReadString(itemElement, "tool") ?? string.Empty,
                Status = ReadString(itemElement, "status") ?? string.Empty,
                AgentsStates = ParseCollabAgentStates(itemElement),
                Model = ReadString(itemElement, "model"),
                Prompt = ReadString(itemElement, "prompt"),
                ReasoningEffort = ReadString(itemElement, "reasoningEffort") ?? ReadString(itemElement, "reasoning_effort"),
            },
            "websearch" => new WebSearchThreadItem
            {
                Id = itemId,
                Type = itemType,
                Query = ReadString(itemElement, "query") ?? string.Empty,
                Action = ParseWebSearchAction(itemElement),
            },
            "imageview" => new ImageViewThreadItem
            {
                Id = itemId,
                Type = itemType,
                Path = ReadString(itemElement, "path") ?? string.Empty,
            },
            "imagegeneration" => new ImageGenerationThreadItem
            {
                Id = itemId,
                Type = itemType,
                Result = ReadString(itemElement, "result") ?? string.Empty,
                Status = ReadString(itemElement, "status") ?? string.Empty,
                RevisedPrompt = ReadString(itemElement, "revisedPrompt") ?? ReadString(itemElement, "revised_prompt"),
            },
            "enteredreviewmode" => new EnteredReviewModeThreadItem
            {
                Id = itemId,
                Type = itemType,
                Review = ReadString(itemElement, "review") ?? string.Empty,
            },
            "exitedreviewmode" => new ExitedReviewModeThreadItem
            {
                Id = itemId,
                Type = itemType,
                Review = ReadString(itemElement, "review") ?? string.Empty,
            },
            "contextcompaction" => new ContextCompactionThreadItem
            {
                Id = itemId,
                Type = itemType,
            },
            _ => new GenericThreadTurnItem
            {
                Id = itemId,
                Type = itemType,
                RawText = ReadThreadTurnItemText(itemElement),
                ItemPhase = ReadString(itemElement, "phase"),
                RawData = AgentStructuredValue.FromJsonElement(itemElement),
            },
        };
    }

    private static IReadOnlyList<AgentUserInput> ParseThreadUserInputs(JsonElement itemElement)
    {
        if (TryGetArray(itemElement, "content", out var contentArray))
        {
            var inputs = new List<AgentUserInput>();
            foreach (var contentItem in contentArray.EnumerateArray())
            {
                var parsed = ParseThreadUserInput(contentItem);
                if (parsed is not null)
                {
                    inputs.Add(parsed);
                }
            }

            return inputs;
        }

        var directText = ReadString(itemElement, "text");
        return string.IsNullOrWhiteSpace(directText)
            ? Array.Empty<AgentUserInput>()
            : new AgentUserInput[]
            {
                new TextUserInput
                {
                    Type = "text",
                    Text = directText!,
                },
            };
    }

    private static AgentUserInput? ParseThreadUserInput(JsonElement itemElement)
    {
        if (itemElement.ValueKind == JsonValueKind.String)
        {
            var text = itemElement.GetString();
            return string.IsNullOrWhiteSpace(text)
                ? null
                : new TextUserInput
                {
                    Type = "text",
                    Text = text!,
                };
        }

        if (itemElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var inputType = ReadString(itemElement, "type");
        var normalizedType = NormalizeUserInputType(inputType);
        if (string.IsNullOrWhiteSpace(normalizedType) && !string.IsNullOrWhiteSpace(ReadString(itemElement, "text")))
        {
            normalizedType = "text";
            inputType = "text";
        }

        return normalizedType switch
        {
            "text" => new TextUserInput
            {
                Type = inputType ?? "text",
                Text = ReadString(itemElement, "text") ?? string.Empty,
                TextElements = ParseTextElements(itemElement),
            },
            "image" => new ImageUserInput
            {
                Type = inputType ?? "image",
                Url = ReadString(itemElement, "url")
                    ?? ReadString(itemElement, "image_url")
                    ?? ReadString(itemElement, "imageUrl")
                    ?? string.Empty,
            },
            "localimage" => new LocalImageUserInput
            {
                Type = inputType ?? "localImage",
                Path = ReadString(itemElement, "path") ?? string.Empty,
            },
            "skill" => new SkillUserInput
            {
                Type = inputType ?? "skill",
                Name = ReadString(itemElement, "name") ?? string.Empty,
                Path = ReadString(itemElement, "path") ?? string.Empty,
            },
            "mention" => new MentionUserInput
            {
                Type = inputType ?? "mention",
                Name = ReadString(itemElement, "name") ?? string.Empty,
                Path = ReadString(itemElement, "path") ?? string.Empty,
            },
            _ => null,
        };
    }

    private static IReadOnlyList<AgentTextElement> ParseTextElements(JsonElement itemElement)
    {
        if (!TryGetArray(itemElement, "textElements", out var elementsArray)
            && !TryGetArray(itemElement, "text_elements", out elementsArray))
        {
            return Array.Empty<AgentTextElement>();
        }

        var elements = new List<AgentTextElement>();
        foreach (var element in elementsArray.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var byteRange = TryGetObject(element, "byteRange") ?? TryGetObject(element, "byte_range");
            elements.Add(new AgentTextElement
            {
                ByteRange = new AgentByteRange
                {
                    Start = byteRange is { } range ? ReadInt32(range, "start") ?? 0 : 0,
                    End = byteRange is { } range2 ? ReadInt32(range2, "end") ?? 0 : 0,
                },
                Placeholder = ReadString(element, "placeholder"),
            });
        }

        return elements;
    }

    private static IReadOnlyList<AgentCommandAction> ParseCommandActions(JsonElement itemElement)
    {
        if (!TryGetArray(itemElement, "commandActions", out var actionsArray)
            && !TryGetArray(itemElement, "command_actions", out actionsArray))
        {
            return Array.Empty<AgentCommandAction>();
        }

        var actions = new List<AgentCommandAction>();
        foreach (var action in actionsArray.EnumerateArray())
        {
            if (action.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            actions.Add(new AgentCommandAction
            {
                Type = ReadString(action, "type") ?? string.Empty,
                Command = ReadString(action, "command") ?? string.Empty,
                Name = ReadString(action, "name"),
                Path = ReadString(action, "path"),
                Query = ReadString(action, "query"),
            });
        }

        return actions;
    }

    private static IReadOnlyList<AgentFileUpdateChange> ParseFileUpdateChanges(JsonElement itemElement)
    {
        if (!TryGetArray(itemElement, "changes", out var changesArray))
        {
            return Array.Empty<AgentFileUpdateChange>();
        }

        var changes = new List<AgentFileUpdateChange>();
        foreach (var change in changesArray.EnumerateArray())
        {
            if (change.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            changes.Add(new AgentFileUpdateChange
            {
                Path = ReadString(change, "path") ?? string.Empty,
                Kind = ReadString(change, "kind") ?? string.Empty,
                Diff = ReadString(change, "diff") ?? string.Empty,
            });
        }

        return changes;
    }

    private static AgentMcpToolCallResult? ParseMcpToolCallResult(JsonElement itemElement)
    {
        var resultElement = TryGetObject(itemElement, "result");
        if (resultElement is null)
        {
            return null;
        }

        var content = new List<AgentStructuredValue>();
        if (TryGetArray(resultElement.Value, "content", out var contentArray))
        {
            foreach (var contentItem in contentArray.EnumerateArray())
            {
                content.Add(AgentStructuredValue.FromJsonElement(contentItem));
            }
        }

        return new AgentMcpToolCallResult
        {
            Content = content,
            StructuredContent = ReadStructuredValue(resultElement.Value, "structuredContent") ?? ReadStructuredValue(resultElement.Value, "structured_content"),
        };
    }

    private static AgentMcpToolCallError? ParseMcpToolCallError(JsonElement itemElement)
    {
        var errorElement = TryGetObject(itemElement, "error");
        return errorElement is null
            ? null
            : new AgentMcpToolCallError
            {
                Message = ReadString(errorElement.Value, "message") ?? string.Empty,
            };
    }

    private static IReadOnlyList<AgentDynamicToolCallContentItem> ParseDynamicToolCallContentItems(JsonElement itemElement)
    {
        if (!TryGetArray(itemElement, "contentItems", out var itemsArray)
            && !TryGetArray(itemElement, "content_items", out itemsArray))
        {
            return Array.Empty<AgentDynamicToolCallContentItem>();
        }

        var items = new List<AgentDynamicToolCallContentItem>();
        foreach (var contentItem in itemsArray.EnumerateArray())
        {
            if (contentItem.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            items.Add(new AgentDynamicToolCallContentItem
            {
                Type = ReadString(contentItem, "type") ?? string.Empty,
                Text = ReadString(contentItem, "text"),
                ImageUrl = ReadString(contentItem, "imageUrl") ?? ReadString(contentItem, "image_url"),
            });
        }

        return items;
    }

    private static IReadOnlyDictionary<string, AgentCollabAgentState> ParseCollabAgentStates(JsonElement itemElement)
    {
        var statesElement = TryGetObject(itemElement, "agentsStates") ?? TryGetObject(itemElement, "agents_states");
        if (statesElement is null)
        {
            return new Dictionary<string, AgentCollabAgentState>(StringComparer.Ordinal);
        }

        var states = new Dictionary<string, AgentCollabAgentState>(StringComparer.Ordinal);
        foreach (var property in statesElement.Value.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            states[property.Name] = new AgentCollabAgentState
            {
                Status = ReadString(property.Value, "status"),
                Message = ReadString(property.Value, "message"),
            };
        }

        return states;
    }

    private static AgentWebSearchAction? ParseWebSearchAction(JsonElement itemElement)
    {
        var actionElement = TryGetObject(itemElement, "action");
        return actionElement is null
            ? null
            : new AgentWebSearchAction
            {
                Type = ReadString(actionElement.Value, "type") ?? string.Empty,
                Query = ReadString(actionElement.Value, "query"),
                Queries = ReadStringArray(actionElement.Value, "queries"),
                Url = ReadString(actionElement.Value, "url"),
                Pattern = ReadString(actionElement.Value, "pattern"),
            };
    }

    private static string? ReadThreadTurnItemText(JsonElement data)
    {
        var directText = ReadString(data, "text");
        if (!string.IsNullOrWhiteSpace(directText))
        {
            return directText;
        }

        if (!data.TryGetProperty("content", out var contentElement) || contentElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var textParts = new List<string>();
        foreach (var item in contentElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var text = ReadString(item, "text");
            if (!string.IsNullOrWhiteSpace(text))
            {
                textParts.Add(text!);
            }
        }

        return textParts.Count == 0 ? null : string.Join(Environment.NewLine, textParts);
    }

    private static string NormalizeThreadTurnItemType(string? value)
        => Normalize(value)?
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant()
        ?? string.Empty;

    private static string NormalizeUserInputType(string? value)
        => NormalizeThreadTurnItemType(value) switch
        {
            "inputtext" => "text",
            "inputimage" => "image",
            var normalized => normalized,
        };

    private static string[] ReadStringArray(JsonElement data, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetArray(data, propertyName, out var arrayElement))
            {
                continue;
            }

            return arrayElement
                .EnumerateArray()
                .Select(ReadScalarString)
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToArray();
        }

        return Array.Empty<string>();
    }

    private static string? ReadScalarString(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null,
        };

    private static AgentStructuredValue? ReadStructuredValue(JsonElement data, string propertyName)
        => TryGetProperty(data, propertyName, out var value)
            ? AgentStructuredValue.FromJsonElement(value)
            : null;

    private static PendingInputStatePayload? ParsePendingInputStatePayload(JsonElement data)
    {
        if (TryGetObject(data, "pendingInputState") is not { } pendingInputState)
        {
            return null;
        }

        return ParsePendingInputStateElement(pendingInputState);
    }

    private static PendingInputStatePayload? ParsePendingInputStateElement(JsonElement pendingInputState)
    {
        if (pendingInputState.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var entries = ParsePendingInputStateEntries(pendingInputState, "entries") ?? Array.Empty<PendingInputStateEntryPayload>();
        var queuedUserMessages = ParsePendingInputStateEntries(
            pendingInputState,
            "queuedUserMessages",
            forcedPendingBucket: "QueuedUserMessage");
        var pendingSteers = ParsePendingInputStateEntries(
            pendingInputState,
            "pendingSteers",
            forcedPendingBucket: "PendingSteer");

        return CanonicalizePendingInputStatePayload(
            new PendingInputStatePayload(
                entries,
                ReadBool(pendingInputState, "interruptRequestPending") ?? false,
                ReadBool(pendingInputState, "submitPendingSteersAfterInterrupt") ?? false,
                queuedUserMessages,
                pendingSteers));
    }

    private IReadOnlyList<PendingInteractiveRequestReplay> ParsePendingInteractiveRequests(JsonElement data)
    {
        if (!TryGetArray(data, "pendingInteractiveRequests", out var requestsElement))
        {
            return Array.Empty<PendingInteractiveRequestReplay>();
        }

        return ParsePendingInteractiveRequestsElement(requestsElement);
    }

    private IReadOnlyList<PendingInteractiveRequestReplay> ParsePendingInteractiveRequestsElement(JsonElement requestsElement)
    {
        if (requestsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<PendingInteractiveRequestReplay>();
        }

        var requests = new List<PendingInteractiveRequestReplay>();
        foreach (var requestElement in requestsElement.EnumerateArray())
        {
            if (requestElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var callId = Normalize(ReadString(requestElement, "callId"));
            if (string.IsNullOrWhiteSpace(callId)
                || !TryGetProperty(requestElement, "requestId", out var requestIdElement)
                || !TryResolveServerRequestId(requestIdElement, out var requestIdValue, out _, out var numericRequestId))
            {
                continue;
            }

            var availableDecisionOptions = ParseApprovalDecisionOptions(requestElement, "availableDecisionOptions");
            var availableDecisions = ReadStringArray(requestElement, "availableDecisions")
                ?? AppServerServerRequestDtoHelpers.ResolveAvailableDecisions(availableDecisionOptions);
            var approvalRequest = TryGetObject(requestElement, "approvalRequest") is { } approvalRequestElement
                ? ParseApprovalRequestPayload(approvalRequestElement)
                : null;
            var permissionRequest = TryGetObject(requestElement, "permissionRequest") is { } permissionRequestElement
                ? ParsePermissionRequestPayload(permissionRequestElement)
                : null;
            var userInputRequest = TryGetObject(requestElement, "userInputRequest") is { } userInputRequestElement
                ? ParseUserInputRequestPayload(userInputRequestElement)
                : null;

            requests.Add(new PendingInteractiveRequestReplay
            {
                RequestId = numericRequestId ?? 0L,
                RequestIdRaw = requestIdValue as string,
                RequestKind = ResolvePendingInteractiveRequestKind(
                                  ReadString(requestElement, "requestKind"),
                                  ReadString(requestElement, "requestMethod"))
                              ?? string.Empty,
                RequestMethod = ReadString(requestElement, "requestMethod"),
                CallId = callId!,
                ThreadId = Normalize(ReadString(requestElement, "threadId")),
                TurnId = Normalize(ReadString(requestElement, "turnId")),
                ToolName = Normalize(ReadString(requestElement, "toolName")),
                ServerName = Normalize(ReadString(requestElement, "serverName")),
                Text = ReadString(requestElement, "text"),
                Status = ReadString(requestElement, "status"),
                Phase = ReadString(requestElement, "phase"),
                RequiresApproval = ReadBool(requestElement, "requiresApproval"),
                ApprovalKind = ReadString(requestElement, "approvalKind"),
                AvailableDecisions = availableDecisions,
                AvailableDecisionOptions = availableDecisionOptions,
                ApprovalRequest = approvalRequest,
                PermissionRequest = permissionRequest,
                UserInputRequest = userInputRequest,
            });
        }

        return requests;
    }

    private string? ResolvePendingInteractiveRequestKind(string? requestKind, string? requestMethod)
    {
        var normalizedKind = Normalize(requestKind);
        if (!string.IsNullOrWhiteSpace(normalizedKind))
        {
            return normalizedKind;
        }

        return providerServerRequestRouter.ResolvePendingRequestKind(requestMethod);
    }

    private StructuredValue ResolveApprovalServerRequestPayload(
        string? requestMethod,
        ProviderApprovalResponseFormats responseFormats)
    {
        ArgumentNullException.ThrowIfNull(responseFormats);

        return providerServerRequestRouter.Route(requestMethod)?.ApprovalResponsePayloadKind switch
        {
            ProviderApprovalResponsePayloadKind.Legacy => responseFormats.LegacyServerRequestPayload,
            ProviderApprovalResponsePayloadKind.McpServerElicitation => responseFormats.McpServerElicitationPayload,
            _ => responseFormats.StandardServerRequestPayload,
        };
    }

    private StructuredValue ResolveUserInputServerRequestPayload(
        string? requestMethod,
        ProviderUserInputResponseFormats responseFormats)
    {
        ArgumentNullException.ThrowIfNull(responseFormats);

        return providerServerRequestRouter.Route(requestMethod)?.Kind == ProviderServerRequestKind.McpServerElicitation
            ? responseFormats.McpServerElicitationPayload
            : responseFormats.ToolRequestPayload;
    }

    private static ApprovalRequestPayload ParseApprovalRequestPayload(JsonElement data)
    {
        var availableDecisionOptions = ParseApprovalDecisionOptions(data, "availableDecisionOptions");
        var availableDecisions = ReadStringArray(data, "availableDecisions")
            ?? AppServerServerRequestDtoHelpers.ResolveAvailableDecisions(availableDecisionOptions);
        return new ApprovalRequestPayload(
            ReadString(data, "toolName"),
            ReadString(data, "approvalKind"),
            availableDecisions,
            ReadString(data, "summary"),
            ParseApprovalMetadataFields(data),
            availableDecisionOptions,
            TryGetProperty(data, "proposedExecPolicyAmendment", out var proposedExecPolicyAmendment)
                ? AppServerServerRequestDtoHelpers.ResolveExecPolicyAmendment(proposedExecPolicyAmendment)
                : null,
            ParseNetworkPolicyAmendments(data, "proposedNetworkPolicyAmendments"));
    }

    private static PermissionRequestPayload ParsePermissionRequestPayload(JsonElement data)
        => new(
            ReadString(data, "reason"),
            ParsePermissionFields(data),
            ReadString(data, "permissionsJson"),
            ReadString(data, "summary"));

    private static UserInputRequestPayload ParseUserInputRequestPayload(JsonElement data)
        => new(
            ParseUserInputQuestions(data),
            ReadString(data, "summary"));

    private static IReadOnlyList<ApprovalDecisionOptionPayload>? ParseApprovalDecisionOptions(JsonElement data, string propertyName)
    {
        if (!TryGetProperty(data, propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return AppServerServerRequestDtoHelpers.ResolveAvailableDecisionOptions(property, default);
    }

    private static IReadOnlyList<ApprovalMetadataFieldPayload> ParseApprovalMetadataFields(JsonElement data)
    {
        if (!TryGetArray(data, "metadataFields", out var fieldsElement))
        {
            return Array.Empty<ApprovalMetadataFieldPayload>();
        }

        var fields = new List<ApprovalMetadataFieldPayload>();
        foreach (var field in fieldsElement.EnumerateArray())
        {
            if (field.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var key = Normalize(ReadString(field, "key"));
            var valueType = Normalize(ReadString(field, "valueType"));
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(valueType))
            {
                continue;
            }

            fields.Add(new ApprovalMetadataFieldPayload(
                key!,
                valueType!,
                ReadString(field, "valueText") ?? string.Empty));
        }

        return fields;
    }

    private static IReadOnlyList<PermissionFieldPayload> ParsePermissionFields(JsonElement data)
    {
        if (!TryGetArray(data, "fields", out var fieldsElement))
        {
            return Array.Empty<PermissionFieldPayload>();
        }

        var fields = new List<PermissionFieldPayload>();
        foreach (var field in fieldsElement.EnumerateArray())
        {
            if (field.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var key = Normalize(ReadString(field, "key"));
            var valueType = Normalize(ReadString(field, "valueType"));
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(valueType))
            {
                continue;
            }

            fields.Add(new PermissionFieldPayload(
                key!,
                valueType!,
                ReadString(field, "valueText") ?? string.Empty));
        }

        return fields;
    }

    private static IReadOnlyList<UserInputQuestionPayload> ParseUserInputQuestions(JsonElement data)
    {
        if (!TryGetArray(data, "questions", out var questionsElement))
        {
            return Array.Empty<UserInputQuestionPayload>();
        }

        var questions = new List<UserInputQuestionPayload>();
        foreach (var question in questionsElement.EnumerateArray())
        {
            if (question.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = Normalize(ReadString(question, "id"));
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            questions.Add(new UserInputQuestionPayload(
                id!,
                ReadString(question, "header") ?? string.Empty,
                ReadString(question, "prompt") ?? string.Empty,
                ReadBool(question, "isSecret") == true,
                ReadBool(question, "isOther") == true,
                ParseUserInputOptions(question)));
        }

        return questions;
    }

    private static IReadOnlyList<UserInputOptionPayload>? ParseUserInputOptions(JsonElement data)
    {
        if (!TryGetArray(data, "options", out var optionsElement))
        {
            return null;
        }

        var options = new List<UserInputOptionPayload>();
        foreach (var option in optionsElement.EnumerateArray())
        {
            if (option.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var label = Normalize(ReadString(option, "label"));
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            options.Add(new UserInputOptionPayload(label!, ReadString(option, "description")));
        }

        return options.Count > 0 ? options : Array.Empty<UserInputOptionPayload>();
    }

    private static IReadOnlyList<NetworkPolicyAmendmentPayload>? ParseNetworkPolicyAmendments(JsonElement data, string propertyName)
    {
        if (!TryGetArray(data, propertyName, out var amendmentsElement))
        {
            return null;
        }

        var amendments = new List<NetworkPolicyAmendmentPayload>();
        foreach (var amendment in amendmentsElement.EnumerateArray())
        {
            if (amendment.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var host = Normalize(ReadString(amendment, "host"));
            var action = Normalize(ReadString(amendment, "action"));
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(action))
            {
                continue;
            }

            amendments.Add(new NetworkPolicyAmendmentPayload(host!, action!));
        }

        return amendments.Count > 0 ? amendments : null;
    }

    private static PendingInputStateEntryPayload[]? ParsePendingInputStateEntries(
        JsonElement pendingInputState,
        string propertyName,
        string? forcedPendingBucket = null)
    {
        if (!TryGetArray(pendingInputState, propertyName, out var entriesElement))
        {
            return null;
        }

        var entries = new List<PendingInputStateEntryPayload>();
        foreach (var entryElement in entriesElement.EnumerateArray())
        {
            if (entryElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var correlationId = Normalize(ReadString(entryElement, "correlationId"));
            if (string.IsNullOrWhiteSpace(correlationId))
            {
                continue;
            }

            entries.Add(new PendingInputStateEntryPayload(
                correlationId!,
                ReadString(entryElement, "requestedMode") ?? string.Empty,
                ReadString(entryElement, "effectiveMode") ?? string.Empty,
                ReadString(entryElement, "lifecycleState") ?? string.Empty,
                ReadString(entryElement, "expectedTurnId"),
                ReadString(entryElement, "turnId"),
                TryGetObject(entryElement, "compareKey") is { } compareKey
                    ? new PendingFollowUpCompareKeyPayload(
                        ReadString(compareKey, "message"),
                        ReadInt32(compareKey, "imageCount") ?? 0)
                    : null,
                forcedPendingBucket
                ?? ReadString(entryElement, "pendingBucket")
                ?? "QueuedUserMessage",
                ParsePendingInputStateInputs(entryElement)));
        }

        return entries.ToArray();
    }

    private static IReadOnlyList<AgentUserInput> ParsePendingInputStateInputs(JsonElement entryElement)
    {
        if (!TryGetArray(entryElement, "inputs", out var inputsElement))
        {
            return Array.Empty<AgentUserInput>();
        }

        var inputs = new List<AgentUserInput>();
        foreach (var inputElement in inputsElement.EnumerateArray())
        {
            var parsed = ParseThreadUserInput(inputElement);
            if (parsed is not null)
            {
                inputs.Add(parsed);
            }
        }

        return inputs;
    }

    private static IReadOnlyList<AgentThreadSeedHistoryItem> ParseThreadSeedHistory(JsonElement data)
    {
        if (!data.TryGetProperty("seedHistory", out var seedHistoryElement) || seedHistoryElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<AgentThreadSeedHistoryItem>();
        }

        return ParseThreadSeedHistoryElement(seedHistoryElement);
    }

    private static IReadOnlyList<AgentThreadSeedHistoryItem> ParseThreadSeedHistoryElement(JsonElement seedHistoryElement)
    {
        if (seedHistoryElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<AgentThreadSeedHistoryItem>();
        }

        var items = new List<AgentThreadSeedHistoryItem>();
        foreach (var itemElement in seedHistoryElement.EnumerateArray())
        {
            if (itemElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            items.Add(new AgentThreadSeedHistoryItem
            {
                Role = ReadString(itemElement, "role") ?? string.Empty,
                Content = ReadString(itemElement, "content") ?? string.Empty,
                Inputs = ParseSeedHistoryInputs(itemElement),
            });
        }

        return items;
    }

    private static IReadOnlyList<AgentUserInput> ParseSeedHistoryInputs(JsonElement itemElement)
    {
        return ParseUserInputsElement(itemElement, "inputs");
    }

    private static IReadOnlyList<AgentUserInput> ParseUserInputsElement(JsonElement itemElement, string propertyName)
    {
        if (!TryGetArray(itemElement, propertyName, out var inputsElement))
        {
            return Array.Empty<AgentUserInput>();
        }

        var inputs = new List<AgentUserInput>();
        foreach (var inputElement in inputsElement.EnumerateArray())
        {
            var parsed = ParseThreadUserInput(inputElement);
            if (parsed is not null)
            {
                inputs.Add(parsed);
            }
        }

        return inputs;
    }

    private static object BuildReviewTargetPayload(ControlPlaneReviewTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        return target switch
        {
            ControlPlaneReviewUncommittedChangesTarget => new Dictionary<string, object?>
            {
                ["type"] = "uncommittedChanges",
            },
            ControlPlaneReviewBaseBranchTarget baseBranch => new Dictionary<string, object?>
            {
                ["type"] = "baseBranch",
                ["branch"] = Normalize(baseBranch.Branch),
            },
            ControlPlaneReviewCommitTarget commit => new Dictionary<string, object?>
            {
                ["type"] = "commit",
                ["sha"] = Normalize(commit.Sha),
                ["title"] = Normalize(commit.Title),
            },
            ControlPlaneReviewCustomTarget custom => new Dictionary<string, object?>
            {
                ["type"] = "custom",
                ["instructions"] = Normalize(custom.Instructions),
            },
            _ => throw new InvalidOperationException($"不支持的 review target：{target.GetType().Name}"),
        };
    }

    private static string? ReadFirstTurnItemText(JsonElement turn)
    {
        if (!turn.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var directText = ReadString(item, "text");
            if (!string.IsNullOrWhiteSpace(directText))
            {
                return directText;
            }

            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var segment in content.EnumerateArray())
            {
                if (segment.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var text = ReadString(segment, "text");
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    private static ControlPlaneConfigRequirementsNetwork? ParseConfigRequirementsNetwork(JsonElement requirements)
    {
        var network = TryGetObject(requirements, "network");
        if (!network.HasValue)
        {
            return null;
        }

        return new ControlPlaneConfigRequirementsNetwork
        {
            Enabled = ReadBool(network.Value, "enabled"),
            HttpPort = ReadUInt16(network.Value, "httpPort"),
            SocksPort = ReadUInt16(network.Value, "socksPort"),
            AllowUpstreamProxy = ReadBool(network.Value, "allowUpstreamProxy"),
            DangerouslyAllowNonLoopbackProxy = ReadBool(network.Value, "dangerouslyAllowNonLoopbackProxy"),
            DangerouslyAllowNonLoopbackAdmin = ReadBool(network.Value, "dangerouslyAllowNonLoopbackAdmin"),
            DangerouslyAllowAllUnixSockets = ReadBool(network.Value, "dangerouslyAllowAllUnixSockets"),
            AllowedDomains = ReadStringArray(network.Value, "allowedDomains"),
            DeniedDomains = ReadStringArray(network.Value, "deniedDomains"),
            AllowUnixSockets = ReadStringArray(network.Value, "allowUnixSockets"),
            AllowLocalBinding = ReadBool(network.Value, "allowLocalBinding"),
        };
    }

    private static string InferCodeModeStatus(IReadOnlyList<ControlPlaneCodeModeOutputItem> contentItems, bool success)
    {
        var header = contentItems
            .FirstOrDefault(static item => string.Equals(item.Type, "input_text", StringComparison.OrdinalIgnoreCase))
            ?.Text;
        if (!string.IsNullOrWhiteSpace(header))
        {
            if (header.StartsWith("Script running with cell ID ", StringComparison.Ordinal))
            {
                return "running";
            }

            if (header.StartsWith("Script completed", StringComparison.Ordinal))
            {
                return "completed";
            }

            if (header.StartsWith("Script terminated", StringComparison.Ordinal))
            {
                return "terminated";
            }

            if (header.StartsWith("Script failed", StringComparison.Ordinal))
            {
                return "failed";
            }
        }

        return success ? "completed" : "failed";
    }

    private static string? TryExtractCodeModeCellId(IReadOnlyList<ControlPlaneCodeModeOutputItem> contentItems)
    {
        var header = contentItems
            .FirstOrDefault(static item => string.Equals(item.Type, "input_text", StringComparison.OrdinalIgnoreCase))
            ?.Text;
        const string prefix = "Script running with cell ID ";
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var remainder = header[prefix.Length..];
        var lineBreakIndex = remainder.IndexOf('\n');
        var cellId = lineBreakIndex >= 0 ? remainder[..lineBreakIndex] : remainder;
        return Normalize(cellId);
    }

    public async ValueTask DisposeAsync()
    {
        await lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisposeCoreAsync().ConfigureAwait(false);
            await stepBindingRegistry.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            lifecycleGate.Release();
            lifecycleGate.Dispose();
        }
    }

    private object BuildInitializeParams()
    {
        var version = typeof(TianShuExecutionRuntime).Assembly.GetName().Version?.ToString() ?? "0.1.0";
        return new
        {
            clientInfo = new
            {
                name = "tianshu-vsextension",
                title = "TianShu VS Extension",
                version,
            },
            capabilities = new
            {
                experimentalApi = true,
                optOutNotificationMethods = DefaultOptOutNotificationMethods,
            },
        };
    }

    private static string ResolveWorkingDirectory(ExecutionRuntimeOptions options)
    {
        var cwd = Normalize(options.WorkingDirectory);
        return string.IsNullOrWhiteSpace(cwd) ? Environment.CurrentDirectory : cwd;
    }

    private static string? ResolveIsolatedSessionStorageRoot(ExecutionRuntimeOptions options)
    {
        var configuredRoot = Normalize(options.IsolatedSessionStorageRoot);
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            return configuredRoot;
        }

        return Path.Combine(
            TianShuHomePathUtilities.ResolveTianShuStateRootPath(),
            "isolated");
    }

    private static object BuildThreadStartParams(ControlPlaneStartThreadCommand command, ExecutionRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(command);

        var payload = new Dictionary<string, object?>
        {
            ["persistExtendedHistory"] = command.PersistExtendedHistory,
        };
        AddIfNotNull(payload, "experimentalRawEvents", command.ExperimentalRawEvents);

        AddIfPresent(payload, "model", ResolveThreadOption(command.Model, options.Model));
        AddIfPresent(payload, "modelProvider", ResolveThreadOption(command.ModelProvider, options.ModelProvider));
        AddServiceTierIfPresent(payload, "serviceTier", ToAgentServiceTierOverride(command.ServiceTier), options.ServiceTier);
        AddApprovalPolicyIfPresent(payload, "approvalPolicy", ToAgentApprovalPolicy(command.ApprovalPolicy), options.ApprovalPolicy);
        AddIfPresent(payload, "sandbox", ResolveThreadOption(command.SandboxMode, options.SandboxMode));
        AddStructuredObjectIfPresent(payload, "config", ToAgentStructuredDictionary(command.Configuration));
        AddIfPresent(payload, "serviceName", command.ServiceName);
        AddIfPresent(payload, "baseInstructions", command.BaseInstructions);
        AddIfPresent(payload, "developerInstructions", command.DeveloperInstructions);
        AddPersonalityIfPresent(payload, "personality", ToAgentPersonality(command.Personality));
        AddIfNotNull(payload, "ephemeral", command.Ephemeral);
        AddDynamicToolsIfPresent(payload, "dynamicTools", ToAgentDynamicTools(command.DynamicTools) ?? ToAgentDynamicTools(options.DynamicTools));
        AddIfPresent(payload, "mockExperimentalField", command.MockExperimentalField);
        AddSessionSourceIfPresent(payload, "sessionSource", options.SessionSource);

        var cwd = ResolveThreadOption(command.WorkingDirectory, options.WorkingDirectory);
        if (!string.IsNullOrWhiteSpace(cwd))
        {
            payload["cwd"] = cwd;
        }

        return payload;
    }

    private static object BuildThreadResumeParams(ControlPlaneResumeThreadCommand command, ExecutionRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(command);

        var threadId = Normalize(command.ThreadId.Value)
            ?? throw new ArgumentException("threadId 不能为空。", nameof(command));
        var payload = new Dictionary<string, object?>
        {
            ["threadId"] = threadId,
            ["persistExtendedHistory"] = command.PersistExtendedHistory,
        };

        AddResponseItemsIfPresent(payload, "history", ToAgentResponseItems(command.History));
        AddIfPresent(payload, "path", command.Path);
        AddIfPresent(payload, "model", ResolveThreadOption(command.Model, options.Model));
        AddIfPresent(payload, "modelProvider", ResolveThreadOption(command.ModelProvider, options.ModelProvider));
        AddServiceTierIfPresent(payload, "serviceTier", ToAgentServiceTierOverride(command.ServiceTier), options.ServiceTier);
        AddApprovalPolicyIfPresent(payload, "approvalPolicy", ToAgentApprovalPolicy(command.ApprovalPolicy), options.ApprovalPolicy);
        AddIfPresent(payload, "sandbox", ResolveThreadOption(command.SandboxMode, options.SandboxMode));
        AddStructuredObjectIfPresent(payload, "config", ToAgentStructuredDictionary(command.Configuration));
        AddIfPresent(payload, "baseInstructions", command.BaseInstructions);
        AddIfPresent(payload, "developerInstructions", command.DeveloperInstructions);
        AddPersonalityIfPresent(payload, "personality", ToAgentPersonality(command.Personality));
        AddSessionSourceIfPresent(payload, "sessionSource", options.SessionSource);

        var cwd = ResolveThreadOption(command.WorkingDirectory, options.WorkingDirectory);
        if (!string.IsNullOrWhiteSpace(cwd))
        {
            payload["cwd"] = cwd;
        }

        return payload;
    }

    private static object BuildThreadForkParams(ControlPlaneForkThreadCommand command, ExecutionRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(command);

        var threadId = Normalize(command.ThreadId.Value)
            ?? throw new ArgumentException("threadId 不能为空。", nameof(command));
        var payload = new Dictionary<string, object?>
        {
            ["threadId"] = threadId,
            ["persistExtendedHistory"] = command.PersistExtendedHistory,
        };

        AddIfPresent(payload, "path", command.Path);
        AddIfPresent(payload, "model", ResolveThreadOption(command.Model, options.Model));
        AddIfPresent(payload, "modelProvider", ResolveThreadOption(command.ModelProvider, options.ModelProvider));
        AddServiceTierIfPresent(payload, "serviceTier", ToAgentServiceTierOverride(command.ServiceTier), options.ServiceTier);
        AddApprovalPolicyIfPresent(payload, "approvalPolicy", ToAgentApprovalPolicy(command.ApprovalPolicy), options.ApprovalPolicy);
        AddIfPresent(payload, "sandbox", ResolveThreadOption(command.SandboxMode, options.SandboxMode));
        AddStructuredObjectIfPresent(payload, "config", ToAgentStructuredDictionary(command.Configuration));
        AddIfPresent(payload, "baseInstructions", command.BaseInstructions);
        AddIfPresent(payload, "developerInstructions", command.DeveloperInstructions);
        AddSessionSourceIfPresent(payload, "sessionSource", options.SessionSource);
        if (command.Ephemeral)
        {
            payload["ephemeral"] = true;
        }

        var cwd = ResolveThreadOption(command.WorkingDirectory, options.WorkingDirectory);
        if (!string.IsNullOrWhiteSpace(cwd))
        {
            payload["cwd"] = cwd;
        }

        return payload;
    }

    private object BuildTurnStartParams(string threadId, string text, IReadOnlyList<ConversationMessage>? history)
        => BuildTurnStartParams(threadId, CreateTextUserInputs(text), history, null);

    private object BuildTurnStartParams(
        string threadId,
        IReadOnlyList<AgentUserInput> userInputs,
        IReadOnlyList<ConversationMessage>? history,
        InteractionEnvelopeRef? interactionEnvelope = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["threadId"] = threadId,
            ["input"] = userInputs
                .Select(static input => ToControlPlaneInputItem(input))
                .Select(protocolAdapter.BuildUserInput)
                .ToArray(),
        };

        if (interactionEnvelope is not null)
        {
            payload["interactionEnvelope"] = KernelInteractionEnvelopePayload.FromContract(interactionEnvelope);
        }

        AddIfPresent(payload, "model", Normalize(options.Model));
        AddServiceTierIfPresent(payload, "serviceTier", AgentServiceTierOverride.Unspecified, options.ServiceTier);
        AddApprovalPolicyIfPresent(payload, "approvalPolicy", null, options.ApprovalPolicy);
        AddIfPresent(payload, "sandboxPolicy", Normalize(options.SandboxMode));
        AddStructuredValueIfPresent(payload, "outputSchema", options.OutputSchema);

        var cwd = Normalize(options.WorkingDirectory);
        if (!string.IsNullOrWhiteSpace(cwd))
        {
            payload["cwd"] = cwd;
        }

        AddIfPresent(payload, "providerBaseUrl", options.ProviderBaseUrl);
        AddIfPresent(payload, "providerApiKeyEnvironmentVariable", options.ProviderApiKeyEnvironmentVariable);
        AddIfPresent(payload, "providerWireApi", options.ProviderWireApi);
        AddInt32IfPresent(payload, "providerRequestMaxRetries", options.ProviderRequestMaxRetries);
        AddInt32IfPresent(payload, "providerStreamMaxRetries", options.ProviderStreamMaxRetries);
        AddIfNotNull(payload, "providerStreamIdleTimeoutMs", options.ProviderStreamIdleTimeoutMs);
        AddIfNotNull(payload, "providerWebsocketConnectTimeoutMs", options.ProviderWebsocketConnectTimeoutMs);
        AddIfNotNull(payload, "providerSupportsWebsockets", options.ProviderSupportsWebsockets);

        var summary = Normalize(options.ModelReasoningSummary);
        if (!string.IsNullOrWhiteSpace(summary))
        {
            payload["summary"] = summary;
        }

        var verbosity = TianShuIdentityMemoryDecisionResolver.ResolveExecutionVerbosity(BuildIdentityMemoryContext());
        if (!string.IsNullOrWhiteSpace(verbosity))
        {
            payload["verbosity"] = verbosity;
        }

        var collaborationMode = BuildTurnStartCollaborationMode(options);
        if (collaborationMode is not null)
        {
            payload["collaborationMode"] = collaborationMode;
        }

        return payload;
    }

    private List<Dictionary<string, object?>> BuildConversationHistoryPayload(IReadOnlyList<ConversationMessage>? history)
    {
        if (history is null || history.Count == 0)
        {
            return [];
        }

        var payload = new List<Dictionary<string, object?>>();
        foreach (var message in history)
        {
            var content = Normalize(message.Content);
            var contentItems = NormalizeUserInputs(message.ContentItems);
            var role = message.Role switch
            {
                ConversationRole.System => "system",
                ConversationRole.Assistant => "assistant",
                _ => "user",
            };

            if (role == "user" && contentItems.Count > 0)
            {
                payload.Add(new Dictionary<string, object?>
                {
                    ["role"] = role,
                    ["content"] = contentItems
                        .Select(static input => ToControlPlaneInputItem(input))
                        .Select(protocolAdapter.BuildUserInput)
                        .ToArray(),
                });
                continue;
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            payload.Add(new Dictionary<string, object?>
            {
                ["role"] = role,
                ["content"] = content,
            });
        }

        return payload;
    }

    private static Dictionary<string, object?>? BuildTurnStartCollaborationMode(ExecutionRuntimeOptions options)
    {
        var mode = Normalize(options.CollaborationMode);
        if (string.IsNullOrWhiteSpace(mode))
        {
            return null;
        }

        return new Dictionary<string, object?>
        {
            ["mode"] = mode,
            ["settings"] = new Dictionary<string, object?>
            {
                ["model"] = Normalize(options.Model),
                ["reasoning_effort"] = null,
                ["developer_instructions"] = null,
            },
        };
    }

    private static object BuildThreadListParams(ExecutionRuntimeOptions options)
    {
        var limit = options.ResumeThreadListLimit <= 0 ? 20 : options.ResumeThreadListLimit;
        if (limit > 200)
        {
            limit = 200;
        }

        var payload = new Dictionary<string, object?>
        {
            ["limit"] = limit,
            ["sortKey"] = "updated_at",
            ["archived"] = false,
        };

        if (options.ResumeLatestMatchCwd)
        {
            payload["cwd"] = ResolveWorkingDirectory(options);
        }

        return payload;
    }

    private async Task<string?> ResolveResumeThreadIdAsync(ExecutionRuntimeOptions options, CancellationToken cancellationToken)
    {
        var explicitResumeThreadId = Normalize(options.ResumeThreadId);
        if (!string.IsNullOrWhiteSpace(explicitResumeThreadId))
        {
            return explicitResumeThreadId;
        }

        if (!options.ResumeLatestThread)
        {
            return null;
        }

        try
        {
            var listResponse = await SendRpcAsync(
                    "thread/list",
                    BuildThreadListParams(options),
                    cancellationToken,
                    TimeSpan.FromSeconds(15))
                .ConfigureAwait(false);

            if (!listResponse.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var item in data.EnumerateArray())
            {
                var threadId = ReadString(item, "id");
                if (!string.IsNullOrWhiteSpace(threadId))
                {
                    return threadId;
                }
            }
        }
        catch (Exception ex)
        {
            RaiseEvent(new ControlPlaneConversationStreamEvent
            {
                Kind = ControlPlaneConversationStreamEventKind.Info,
                Message = $"读取会话列表失败：{ex.Message}",
            });
        }

        return null;
    }

    private static void AddIfPresent(IDictionary<string, object?> target, string key, string? value)
    {
        var normalized = Normalize(value);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            target[key] = normalized;
        }
    }

    private static void AddInt32IfPresent(IDictionary<string, object?> target, string key, long? value)
    {
        if (value is null)
        {
            return;
        }

        if (value < int.MinValue || value > int.MaxValue)
        {
            throw new InvalidOperationException($"{key} 超出 Int32 范围。");
        }

        target[key] = (int)value.Value;
    }

    private static string ResolveExecutablePath(ExecutionRuntimeOptions options)
    {
        if (options.UseDotNetProjectLauncher)
        {
            return "dotnet";
        }

        var configured = Normalize(options.ExecutablePath);
        return string.IsNullOrWhiteSpace(configured) ? "dotnet" : configured;
    }

    private string? ResolveAppHostProjectPath(ExecutionRuntimeOptions options)
    {
        var configured = RuntimeHostLaunchLocator.NormalizeProjectPath(options.AppHostProjectPath);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        var workingDirectory = ResolveWorkingDirectory(options);
        var candidate = Path.Combine(workingDirectory, "src", "Hosting", "TianShu.AppHost", "TianShu.AppHost.csproj");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        var solutionRoot = RuntimeHostLaunchLocator.TryLocateSolutionRoot(workingDirectory);
        if (solutionRoot is null)
        {
            return null;
        }

        return RuntimeHostLaunchLocator.ResolvePreferredHostProjectPath(solutionRoot);
    }

    private InvalidOperationException CreateStartupFailureException(string message)
    {
        var detail = BuildProcessDiagnosticDetail();
        return string.IsNullOrWhiteSpace(detail)
            ? new InvalidOperationException(message)
            : new InvalidOperationException($"{message}{Environment.NewLine}{detail}");
    }

    private TimeoutException CreateStartupTimeoutException(TimeSpan timeout, OperationCanceledException innerException)
    {
        var detail = BuildProcessDiagnosticDetail();
        var message = $"TianShu app-host 初始化超时（{timeout.TotalSeconds:0.#} 秒）。";
        if (!string.IsNullOrWhiteSpace(detail))
        {
            message = $"{message}{Environment.NewLine}{detail}";
        }

        return new TimeoutException(message, innerException);
    }

    private string BuildProcessDiagnosticDetail(int stderrLineCount = 12)
    {
        StringBuilder builder = new();
        if (process is not null && process.HasExited)
        {
            builder.Append("ExitCode=")
                .Append(process.ExitCode);
        }

        var stderrTail = string.Join(Environment.NewLine, stderrBuffer.TakeLast(stderrLineCount));
        if (!string.IsNullOrWhiteSpace(stderrTail))
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append("---- stderr ----")
                .AppendLine()
                .Append(stderrTail);
        }

        return builder.ToString();
    }
    private string BuildArguments(ExecutionRuntimeOptions options)
    {
        List<string> segments;
        if (options.UseDotNetProjectLauncher)
        {
            var projectPath = ResolveAppHostProjectPath(options);
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                throw new InvalidOperationException(
                    "未找到 TianShu 宿主项目文件，请在设置中指定“宿主项目路径”。");
            }

            segments =
            [
                "run",
                "--project",
                QuoteArg(projectPath),
                "--",
                "app-server",
                "--listen",
                "stdio://",
            ];
        }
        else
        {
            segments = ["app-server", "--listen", "stdio://"];
        }

        // tianshu.toml 与 profile 已在宿主侧解析并应用到运行时选项，
        // 内核 CLI 当前不接受 --config <文件路径> / --profile <名称> 语义。
        segments.AddRange(providerBootstrap.BuildCliArguments(CreateProviderCliArguments(options)));

        if (!string.IsNullOrWhiteSpace(options.AdditionalArguments))
        {
            segments.Add(options.AdditionalArguments.Trim());
        }

        return string.Join(" ", segments);
    }

    private static ProviderRuntimeCliArguments CreateProviderCliArguments(ExecutionRuntimeOptions options)
        => new()
        {
            ConfigOverrides = options.ConfigOverrides,
            ProfileName = options.ProfileName,
            ProfileNameResolvedFromConfig = options.ProfileNameResolvedFromConfig,
            ConfigFilePath = options.ConfigFilePath,
            DefaultConfigFilePath = ExecutionRuntimeOptions.ResolveDefaultConfigFilePath(),
        };

    private static string QuoteArg(string value)
    {
        if (value.Contains('"') || value.Contains(' '))
        {
            return $"\"{value.Replace("\"", "\\\"")}\"";
        }

        return value;
    }

    private async Task<JsonElement> SendRpcAsync(string method, object? parameters, CancellationToken cancellationToken, TimeSpan? timeoutOverride = null)
    {
        if (stdin is null)
        {
            throw new InvalidOperationException("stdin 未初始化。");
        }

        var id = Interlocked.Increment(ref rpcId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!pendingResponses.TryAdd(id, tcs))
        {
            throw new InvalidOperationException($"无法登记 RPC 请求：id={id}");
        }

        var request = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters ?? new { },
        };

        await WriteJsonLineAsync(request, cancellationToken).ConfigureAwait(false);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutOverride ?? options.RequestTimeout);

        try
        {
            return await tcs.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            pendingResponses.TryRemove(id, out _);
            TraceRuntime("timeout", $"method={method}; id={id}");
            throw new TimeoutException($"RPC 超时：method={method}, id={id}");
        }
    }

    private Task SendNotificationAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?> { ["method"] = method };
        if (parameters is not null)
        {
            payload["params"] = parameters;
        }

        return WriteJsonLineAsync(payload, cancellationToken);
    }

    private async Task WriteJsonLineAsync(object payload, CancellationToken cancellationToken)
    {
        if (stdin is null)
        {
            throw new InvalidOperationException("stdin 未初始化。");
        }

        await rpcWriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var json = JsonSerializer.Serialize(payload, jsonOptions);
            TraceRuntime("stdin", json);
            await stdin.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            await stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            rpcWriteGate.Release();
        }
    }

    private async Task ReadStdoutLoopAsync(CancellationToken cancellationToken)
    {
        if (stdout is null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await stdout.ReadLineAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                break;
            }

            if (line is null || string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            TraceRuntime("stdout", line);
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (AppServerProtocolParser.TryParseIncoming(root, line, out var incoming))
                {
                    if (incoming.RpcResponse is not null && TryResolveRpcResponse(incoming.RpcResponse))
                    {
                        continue;
                    }

                    if (incoming.ServerRequest is not null
                        && await TryHandleServerRequestAsync(incoming.ServerRequest, cancellationToken).ConfigureAwait(false))
                    {
                        continue;
                    }

                    if (incoming.Notification is not null)
                    {
                        HandleNotification(incoming.Notification);
                        continue;
                    }
                }

                if (TryResolveRpcResponse(root))
                {
                    continue;
                }

                if (await TryHandleServerRequestAsync(root, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                HandleNotification(root, line);
            }
            catch
            {
                // 忽略非 JSON 行。
            }
        }
    }

    private bool TryResolveRpcResponse(AppServerRpcResponseEnvelope response)
    {
        if (!pendingResponses.TryRemove(response.Id, out var tcs))
        {
            return false;
        }

        if (response.Error is not null)
        {
            tcs.TrySetException(new AppServerRpcException(
                response.Error.Code,
                response.Error.Message,
                response.Error.Data.HasValue ? StructuredValue.FromJsonElement(response.Error.Data.Value) : null));
            return true;
        }

        tcs.TrySetResult(response.Result?.Clone() ?? response.Root.Clone());
        return true;
    }

    private bool TryResolveRpcResponse(JsonElement root)
    {
        if (!root.TryGetProperty("id", out var idElement)
            || idElement.ValueKind != JsonValueKind.Number
            || !idElement.TryGetInt64(out var id)
            || !pendingResponses.TryRemove(id, out var tcs))
        {
            return false;
        }

        if (root.TryGetProperty("error", out var errorElement))
        {
            var message = ReadString(errorElement, "message") ?? errorElement.GetRawText();
            var code = errorElement.TryGetProperty("code", out var codeElement)
                && codeElement.ValueKind == JsonValueKind.Number
                && codeElement.TryGetInt32(out var parsedCode)
                ? parsedCode
                : -32603;
            var errorData = errorElement.TryGetProperty("data", out var dataElement)
                ? StructuredValue.FromJsonElement(dataElement)
                : null;
            tcs.TrySetException(new AppServerRpcException(code, message, errorData));
            return true;
        }

        tcs.TrySetResult(root.TryGetProperty("result", out var resultElement) ? resultElement.Clone() : root.Clone());
        return true;
    }

    private Task<bool> TryHandleServerRequestAsync(AppServerServerRequestEnvelope request, CancellationToken cancellationToken)
        => TryHandleServerRequestCoreAsync(request.Method, request.Id, request.Params, cancellationToken);

    private async Task<bool> TryHandleServerRequestAsync(JsonElement root, CancellationToken cancellationToken)
    {
        if (!root.TryGetProperty("method", out var methodElement)
            || methodElement.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("id", out var idElement))
        {
            return false;
        }

        root.TryGetProperty("params", out var parameters);
        return await TryHandleServerRequestCoreAsync(methodElement.GetString(), idElement.Clone(), parameters, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> TryHandleServerRequestCoreAsync(
        string? method,
        JsonElement idElement,
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        var route = providerServerRequestRouter.Route(method);
        if (route is null)
        {
            return false;
        }

        var interpretedRequest = providerServerRequestInterpreter.Interpret(route, parameters);
        if (interpretedRequest is null)
        {
            return false;
        }

        switch (interpretedRequest)
        {
            case ProviderCommandExecutionApprovalRequest payload:
            {
                await HandleCommandExecutionRequestApprovalAsync(route.Method, idElement.Clone(), payload, cancellationToken).ConfigureAwait(false);
                return true;
            }
            case ProviderFileChangeApprovalRequest payload:
            {
                await HandleFileChangeRequestApprovalAsync(route.Method, idElement.Clone(), payload, cancellationToken).ConfigureAwait(false);
                return true;
            }
            case ProviderToolApprovalRequest payload:
            {
                await HandleToolRequestApprovalAsync(route.Method, idElement.Clone(), payload, cancellationToken).ConfigureAwait(false);
                return true;
            }
            case ProviderMcpServerElicitationApprovalRequest payload:
            {
                await HandleMcpServerElicitationApprovalAsync(route.Method, idElement.Clone(), payload, cancellationToken).ConfigureAwait(false);
                return true;
            }
            case ProviderMcpServerElicitationUserInputRequest payload:
            {
                await HandleMcpServerElicitationUserInputAsync(route.Method, idElement.Clone(), payload, cancellationToken).ConfigureAwait(false);
                return true;
            }
            case ProviderPermissionRequestApprovalRequest payload:
            {
                await HandlePermissionRequestApprovalAsync(route.Method, idElement.Clone(), payload, cancellationToken).ConfigureAwait(false);
                return true;
            }
            case ProviderToolUserInputRequest payload:
            {
                await HandleToolRequestUserInputAsync(route.Method, idElement.Clone(), payload, cancellationToken).ConfigureAwait(false);
                return true;
            }
            case ProviderDynamicToolCallRequest payload:
            {
                await HandleDynamicToolCallAsync(route.Method, idElement.Clone(), payload, cancellationToken).ConfigureAwait(false);
                return true;
            }
            default:
                return false;
        }
    }

    private async Task HandleCommandExecutionRequestApprovalAsync(
        string requestMethod,
        JsonElement requestId,
        ProviderCommandExecutionApprovalRequest request,
        CancellationToken cancellationToken)
    {
        const string phase = "request_approval";
        var threadId = request.ThreadId ?? activeThreadId;
        var turnId = ResolveServerRequestTurnId(threadId, request.TurnId);
        var itemId = request.ItemId;
        var callId = request.CallId;
        var detail = request.Summary;
        const string startedStatus = "awaitingApproval";
        RaiseProjectedProviderEvents(
            new ProviderEventProjectionContext(
                ThreadId: threadId,
                TurnId: turnId,
                ItemId: itemId,
                CallId: callId,
                ToolName: "commandExecution",
                Status: startedStatus,
                Phase: phase,
                SourceMethod: requestMethod),
            providerToolEventFactory.CreateToolDirective(new ProviderToolDirectiveRequest(callId, itemId, "commandExecution", detail, true)),
            ControlPlaneConversationStreamEventKind.ToolCallStarted);

        RaiseApprovalRequestedEvent(
            new ProviderEventProjectionContext(
                ThreadId: threadId,
                TurnId: turnId,
                ItemId: itemId,
                CallId: callId,
                ToolName: "commandExecution",
                ServerName: null,
                Status: startedStatus,
                Phase: phase,
                SourceMethod: requestMethod),
            detail,
            request.ApprovalRequest);

        TryTrackPendingInteractiveServerRequest(
            requestId,
            callId,
            threadId,
            turnId,
            ProviderServerRequestPendingKinds.ApprovalRequested,
            "commandExecution",
            requestMethod);

        var decision = await WaitForApprovalDecisionAsync(callId, cancellationToken).ConfigureAwait(false);
        var serializedApprovalResponse = providerServerRequestResponseSerializer.SerializeApprovalResponse(ToProviderApprovalOutcome(decision));
        var completedStatus = ResolveApprovalStatus(decision);
        var decisionText = BuildApprovalDecisionText(decision);
        await SendServerRequestResponseAsync(
                requestId,
                ResolveApprovalServerRequestPayload(requestMethod, serializedApprovalResponse).ToPlainObject(),
                cancellationToken)
            .ConfigureAwait(false);
        RaiseProjectedProviderEvents(
            new ProviderEventProjectionContext(
                ThreadId: threadId,
                TurnId: turnId,
                ItemId: itemId,
                CallId: callId,
                ToolName: "commandExecution",
                Status: completedStatus,
                Phase: phase,
                SourceMethod: requestMethod),
            providerToolEventFactory.CreateToolResult(new ProviderToolResultRequest(callId, itemId, "commandExecution", null, detail, decisionText, true)),
            ControlPlaneConversationStreamEventKind.ToolCallCompleted);
    }

    private async Task HandleFileChangeRequestApprovalAsync(
        string requestMethod,
        JsonElement requestId,
        ProviderFileChangeApprovalRequest request,
        CancellationToken cancellationToken)
    {
        const string phase = "request_approval";
        var threadId = request.ThreadId ?? activeThreadId;
        var turnId = ResolveServerRequestTurnId(threadId, request.TurnId);
        var itemId = request.ItemId;
        var callId = request.CallId;
        var reason = request.Summary;
        const string startedStatus = "awaitingApproval";
        RaiseProjectedProviderEvents(
            new ProviderEventProjectionContext(
                ThreadId: threadId,
                TurnId: turnId,
                ItemId: itemId,
                CallId: callId,
                ToolName: "fileChange",
                Status: startedStatus,
                Phase: phase,
                SourceMethod: requestMethod),
            providerToolEventFactory.CreateToolDirective(new ProviderToolDirectiveRequest(callId, itemId, "fileChange", reason, true)),
            ControlPlaneConversationStreamEventKind.ToolCallStarted);

        RaiseApprovalRequestedEvent(
            new ProviderEventProjectionContext(
                ThreadId: threadId,
                TurnId: turnId,
                ItemId: itemId,
                CallId: callId,
                ToolName: "fileChange",
                ServerName: null,
                Status: startedStatus,
                Phase: phase,
                SourceMethod: requestMethod),
            reason,
            request.ApprovalRequest);

        TryTrackPendingInteractiveServerRequest(
            requestId,
            callId,
            threadId,
            turnId,
            ProviderServerRequestPendingKinds.ApprovalRequested,
            "fileChange",
            requestMethod);

        var decision = await WaitForApprovalDecisionAsync(callId, cancellationToken).ConfigureAwait(false);
        var serializedApprovalResponse = providerServerRequestResponseSerializer.SerializeApprovalResponse(ToProviderApprovalOutcome(decision));
        var completedStatus = ResolveApprovalStatus(decision);
        var decisionText = BuildApprovalDecisionText(decision);
        await SendServerRequestResponseAsync(
                requestId,
                ResolveApprovalServerRequestPayload(requestMethod, serializedApprovalResponse).ToPlainObject(),
                cancellationToken)
            .ConfigureAwait(false);
        RaiseProjectedProviderEvents(
            new ProviderEventProjectionContext(
                ThreadId: threadId,
                TurnId: turnId,
                ItemId: itemId,
                CallId: callId,
                ToolName: "fileChange",
                Status: completedStatus,
                Phase: phase,
                SourceMethod: requestMethod),
            providerToolEventFactory.CreateToolResult(new ProviderToolResultRequest(callId, itemId, "fileChange", null, reason, decisionText, true)),
            ControlPlaneConversationStreamEventKind.ToolCallCompleted);
    }

    private async Task HandleToolRequestApprovalAsync(
        string requestMethod,
        JsonElement requestId,
        ProviderToolApprovalRequest request,
        CancellationToken cancellationToken)
    {
        const string phase = "request_approval";
        var threadId = request.ThreadId ?? activeThreadId;
        var turnId = ResolveServerRequestTurnId(threadId, request.TurnId);
        var itemId = request.ItemId;
        var callId = request.CallId;
        var toolName = request.ToolName;
        var summary = request.Summary;
        const string startedStatus = "awaitingApproval";
        RaiseProjectedProviderEvents(
            new ProviderEventProjectionContext(
                ThreadId: threadId,
                TurnId: turnId,
                ItemId: itemId,
                CallId: callId,
                ToolName: toolName,
                ServerName: request.ServerName,
                Status: startedStatus,
                Phase: phase,
                SourceMethod: requestMethod),
            providerToolEventFactory.CreateToolDirective(new ProviderToolDirectiveRequest(callId, itemId, toolName, summary, true)),
            ControlPlaneConversationStreamEventKind.ToolCallStarted);

        RaiseApprovalRequestedEvent(
            new ProviderEventProjectionContext(
                ThreadId: threadId,
                TurnId: turnId,
                ItemId: itemId,
                CallId: callId,
                ToolName: toolName,
                ServerName: request.ServerName,
                Status: startedStatus,
                Phase: phase,
                SourceMethod: requestMethod),
            summary,
            request.ApprovalRequest);

        TryTrackPendingInteractiveServerRequest(
            requestId,
            callId,
            threadId,
            turnId,
            ProviderServerRequestPendingKinds.ApprovalRequested,
            toolName,
            requestMethod);

        var decision = await WaitForApprovalDecisionAsync(callId, cancellationToken).ConfigureAwait(false);
        var serializedApprovalResponse = providerServerRequestResponseSerializer.SerializeApprovalResponse(ToProviderApprovalOutcome(decision));
        var completedStatus = ResolveApprovalStatus(decision);
        var decisionText = BuildApprovalDecisionText(decision);

        await SendServerRequestResponseAsync(
                requestId,
                ResolveApprovalServerRequestPayload(requestMethod, serializedApprovalResponse).ToPlainObject(),
                cancellationToken)
            .ConfigureAwait(false);
        RaiseProjectedProviderEvents(
            new ProviderEventProjectionContext(
                ThreadId: threadId,
                TurnId: turnId,
                ItemId: itemId,
                CallId: callId,
                ToolName: toolName,
                ServerName: request.ServerName,
                Status: completedStatus,
                Phase: phase,
                SourceMethod: requestMethod),
            providerToolEventFactory.CreateToolResult(new ProviderToolResultRequest(callId, itemId, toolName, null, summary, decisionText, true)),
            ControlPlaneConversationStreamEventKind.ToolCallCompleted);
    }

    private async Task HandleMcpServerElicitationApprovalAsync(
        string requestMethod,
        JsonElement requestId,
        ProviderMcpServerElicitationApprovalRequest request,
        CancellationToken cancellationToken)
    {
        const string phase = "request_approval";
        var threadId = request.ThreadId ?? activeThreadId;
        var turnId = ResolveServerRequestTurnId(threadId, request.TurnId);
        var toolName = request.ToolName;
        var callId = request.CallId;
        var requestSummary = request.Summary;
        const string startedStatus = "awaitingApproval";
        RaiseProjectedProviderEvents(
            new ProviderEventProjectionContext(
                ThreadId: threadId,
                TurnId: turnId,
                CallId: callId,
                ToolName: toolName,
                ServerName: request.ServerName,
                Status: startedStatus,
                Phase: phase,
                SourceMethod: requestMethod),
            providerToolEventFactory.CreateToolDirective(new ProviderToolDirectiveRequest(callId, null, toolName, requestSummary, true)),
            ControlPlaneConversationStreamEventKind.ToolCallStarted);

        RaiseApprovalRequestedEvent(
            new ProviderEventProjectionContext(
                ThreadId: threadId,
                TurnId: turnId,
                CallId: callId,
                ToolName: toolName,
                ServerName: request.ServerName,
                Status: startedStatus,
                Phase: phase,
                SourceMethod: requestMethod),
            requestSummary,
            request.ApprovalRequest);

        TryTrackPendingInteractiveServerRequest(
            requestId,
            callId,
            threadId,
            turnId,
            ProviderServerRequestPendingKinds.ApprovalRequested,
            toolName,
            requestMethod);

        var decision = await WaitForApprovalDecisionAsync(callId, cancellationToken).ConfigureAwait(false);
        var serializedApprovalResponse = providerServerRequestResponseSerializer.SerializeApprovalResponse(ToProviderApprovalOutcome(decision));
        var completedStatus = ResolveApprovalStatus(decision);
        var decisionText = BuildApprovalDecisionText(decision);

        await SendServerRequestResponseAsync(
                requestId,
                ResolveApprovalServerRequestPayload(requestMethod, serializedApprovalResponse).ToPlainObject(),
                cancellationToken)
            .ConfigureAwait(false);
        RaiseProjectedProviderEvents(
            new ProviderEventProjectionContext(
                ThreadId: threadId,
                TurnId: turnId,
                CallId: callId,
                ToolName: toolName,
                ServerName: request.ServerName,
                Status: completedStatus,
                Phase: phase,
                SourceMethod: requestMethod),
            providerToolEventFactory.CreateToolResult(new ProviderToolResultRequest(callId, null, toolName, null, requestSummary, decisionText, true)),
            ControlPlaneConversationStreamEventKind.ToolCallCompleted);
    }

    private async Task HandleMcpServerElicitationUserInputAsync(
        string requestMethod,
        JsonElement requestId,
        ProviderMcpServerElicitationUserInputRequest request,
        CancellationToken cancellationToken)
    {
        const string phase = "request_user_input";
        var threadId = request.ThreadId ?? activeThreadId;
        var turnId = ResolveServerRequestTurnId(threadId, request.TurnId);
        var toolName = request.ToolName;
        var callId = request.CallId;
        var summary = request.Summary;
        const string startedStatus = "awaitingUserInput";
        RaiseProjectedProviderEvents(
            new ProviderEventProjectionContext(
                ThreadId: threadId,
                TurnId: turnId,
                CallId: callId,
                ToolName: toolName,
                ServerName: request.ServerName,
                Status: startedStatus,
                Phase: phase,
                SourceMethod: requestMethod),
            providerToolEventFactory.CreateToolDirective(new ProviderToolDirectiveRequest(callId, null, toolName, summary, false)),
            ControlPlaneConversationStreamEventKind.ToolCallStarted);

        var pendingRequest = new TaskCompletionSource<UserInputSubmission>(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingUserInputRequests[callId] = pendingRequest;
        TryTrackPendingInteractiveServerRequest(
            requestId,
            callId,
            threadId,
            turnId,
            ProviderServerRequestPendingKinds.UserInput,
            toolName,
            requestMethod);

        RaiseUserInputRequestedEvent(
            new ProviderEventProjectionContext(
                ThreadId: threadId,
                TurnId: turnId,
                CallId: callId,
                ToolName: toolName,
                ServerName: request.ServerName,
                Status: startedStatus,
                Phase: phase,
                SourceMethod: requestMethod),
            summary,
            request.UserInputRequest);

        UserInputSubmission submission;
        using (cancellationToken.Register(static state => ((TaskCompletionSource<UserInputSubmission>)state!).TrySetCanceled(), pendingRequest))
        {
            try
            {
                submission = await pendingRequest.Task.ConfigureAwait(false);
            }
            finally
            {
                pendingUserInputRequests.TryRemove(callId, out _);
                RemovePendingUserInputProjection(callId);
            }
        }

        var serializedUserInputResponse = providerServerRequestResponseSerializer.SerializeUserInputResponse(ToProviderUserInputOutcome(submission));
        await SendServerRequestResponseAsync(
                requestId,
                ResolveUserInputServerRequestPayload(requestMethod, serializedUserInputResponse).ToPlainObject(),
                cancellationToken)
            .ConfigureAwait(false);

        var action = serializedUserInputResponse.McpServerElicitationAction;
        var decisionText = serializedUserInputResponse.McpServerElicitationSummary;
        var completedStatus = serializedUserInputResponse.McpServerElicitationStatus;
        RaiseProjectedProviderEvents(
            new ProviderEventProjectionContext(
                ThreadId: threadId,
                TurnId: turnId,
                CallId: callId,
                ToolName: toolName,
                ServerName: request.ServerName,
                Status: completedStatus,
                Phase: phase,
                SourceMethod: requestMethod),
            providerToolEventFactory.CreateToolResult(new ProviderToolResultRequest(callId, null, toolName, null, summary, decisionText, false)),
            ControlPlaneConversationStreamEventKind.ToolCallCompleted);
    }

    private async Task HandlePermissionRequestApprovalAsync(
        string requestMethod,
        JsonElement requestId,
        ProviderPermissionRequestApprovalRequest request,
        CancellationToken cancellationToken)
    {
        const string phase = "request_permission";
        var threadId = request.ThreadId ?? activeThreadId;
        var turnId = ResolveServerRequestTurnId(threadId, request.TurnId);
        var itemId = request.ItemId;
        var callId = request.CallId;
        var summary = request.Summary;
        const string startedStatus = "awaitingPermission";
        RaiseProjectedProviderEvents(
            new ProviderEventProjectionContext(
                ThreadId: threadId,
                TurnId: turnId,
                ItemId: itemId,
                CallId: callId,
                ToolName: "request_permissions",
                Status: startedStatus,
                Phase: phase,
                SourceMethod: requestMethod),
            providerToolEventFactory.CreateToolDirective(new ProviderToolDirectiveRequest(callId, itemId, "request_permissions", summary, true)),
            ControlPlaneConversationStreamEventKind.ToolCallStarted);

        RaisePermissionRequestedEvent(
            new ProviderEventProjectionContext(
                ThreadId: threadId,
                TurnId: turnId,
                ItemId: itemId,
                CallId: callId,
                ToolName: "request_permissions",
                ServerName: null,
                Status: startedStatus,
                Phase: phase,
                SourceMethod: requestMethod),
            summary,
            request.PermissionRequest);

        var pendingRequest = new TaskCompletionSource<PermissionGrantResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingPermissionRequests[callId] = pendingRequest;
        TryTrackPendingInteractiveServerRequest(
            requestId,
            callId,
            threadId,
            turnId,
            ProviderServerRequestPendingKinds.PermissionRequested,
            "request_permissions",
            requestMethod);

        PermissionGrantResponse response;
        using (cancellationToken.Register(static state => ((TaskCompletionSource<PermissionGrantResponse>)state!).TrySetCanceled(), pendingRequest))
        {
            try
            {
                response = await pendingRequest.Task.ConfigureAwait(false);
            }
            finally
            {
                pendingPermissionRequests.TryRemove(callId, out _);
                RemovePendingApprovalProjection(callId);
            }
        }

        var serializedPermissionResponse = providerServerRequestResponseSerializer.SerializePermissionResponse(ToProviderPermissionGrantOutcome(response));
        await SendServerRequestResponseAsync(
                requestId,
                serializedPermissionResponse.Payload.ToPlainObject(),
                cancellationToken)
            .ConfigureAwait(false);
        var responseSummary = serializedPermissionResponse.Summary;
        RaiseProjectedProviderEvents(
            new ProviderEventProjectionContext(
                ThreadId: threadId,
                TurnId: turnId,
                ItemId: itemId,
                CallId: callId,
                ToolName: "request_permissions",
                Status: "completed",
                Phase: phase,
                SourceMethod: requestMethod),
            providerToolEventFactory.CreateToolResult(new ProviderToolResultRequest(callId, itemId, "request_permissions", null, summary, responseSummary, true)),
            ControlPlaneConversationStreamEventKind.ToolCallCompleted);
    }

    private async Task HandleToolRequestUserInputAsync(
        string requestMethod,
        JsonElement requestId,
        ProviderToolUserInputRequest request,
        CancellationToken cancellationToken)
    {
        const string phase = "request_user_input";
        var threadId = request.ThreadId ?? activeThreadId;
        var turnId = ResolveServerRequestTurnId(threadId, request.TurnId);
        var itemId = request.ItemId;
        var callId = request.CallId;
        var summary = request.Summary;
        const string startedStatus = "awaitingUserInput";
        RaiseProjectedProviderEvents(
            new ProviderEventProjectionContext(
                ThreadId: threadId,
                TurnId: turnId,
                ItemId: itemId,
                CallId: callId,
                ToolName: "requestUserInput",
                Status: startedStatus,
                Phase: phase,
                SourceMethod: requestMethod),
            providerToolEventFactory.CreateToolDirective(new ProviderToolDirectiveRequest(callId, itemId, "requestUserInput", summary, false)),
            ControlPlaneConversationStreamEventKind.ToolCallStarted);

        var pendingRequest = new TaskCompletionSource<UserInputSubmission>(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingUserInputRequests[callId] = pendingRequest;
        TryTrackPendingInteractiveServerRequest(
            requestId,
            callId,
            threadId,
            turnId,
            ProviderServerRequestPendingKinds.UserInput,
            "requestUserInput",
            requestMethod);

        RaiseUserInputRequestedEvent(
            new ProviderEventProjectionContext(
                ThreadId: threadId,
                TurnId: turnId,
                ItemId: itemId,
                CallId: callId,
                ToolName: "requestUserInput",
                ServerName: null,
                Status: startedStatus,
                Phase: phase,
                SourceMethod: requestMethod),
            summary,
            request.UserInputRequest);

        UserInputSubmission submission;
        using (cancellationToken.Register(static state => ((TaskCompletionSource<UserInputSubmission>)state!).TrySetCanceled(), pendingRequest))
        {
            try
            {
                submission = await pendingRequest.Task.ConfigureAwait(false);
            }
            finally
            {
                pendingUserInputRequests.TryRemove(callId, out _);
                RemovePendingUserInputProjection(callId);
            }
        }

        var serializedUserInputResponse = providerServerRequestResponseSerializer.SerializeUserInputResponse(ToProviderUserInputOutcome(submission));
        await SendServerRequestResponseAsync(
                requestId,
                ResolveUserInputServerRequestPayload(requestMethod, serializedUserInputResponse).ToPlainObject(),
                cancellationToken)
            .ConfigureAwait(false);
        var answerSummary = serializedUserInputResponse.ToolRequestSummary;
        RaiseProjectedProviderEvents(
            new ProviderEventProjectionContext(
                ThreadId: threadId,
                TurnId: turnId,
                ItemId: itemId,
                CallId: callId,
                ToolName: "requestUserInput",
                Status: "completed",
                Phase: phase,
                SourceMethod: requestMethod),
            providerToolEventFactory.CreateToolResult(new ProviderToolResultRequest(callId, itemId, "requestUserInput", null, summary, answerSummary, false)),
            ControlPlaneConversationStreamEventKind.ToolCallCompleted);
    }

    private async Task HandleDynamicToolCallAsync(
        string requestMethod,
        JsonElement requestId,
        ProviderDynamicToolCallRequest request,
        CancellationToken cancellationToken)
    {
        const string phase = "tool_call";
        var threadId = request.ThreadId ?? activeThreadId;
        var turnId = ResolveServerRequestTurnId(threadId, request.TurnId);
        var callId = request.CallId;
        var tool = request.ToolName;
        var arguments = request.Arguments;
        var inputText = request.InputText;
        RaiseProjectedProviderEvents(
            new ProviderEventProjectionContext(
                ThreadId: threadId,
                TurnId: turnId,
                CallId: callId,
                ToolName: tool,
                Status: "started",
                Phase: phase,
                SourceMethod: requestMethod),
            providerToolEventFactory.CreateToolDirective(new ProviderToolDirectiveRequest(callId, null, tool, inputText, false)),
            ControlPlaneConversationStreamEventKind.ToolCallStarted);

        var toolInput = StructuredValue.FromPlainObject(arguments.ToPlainObject());
        var parameterEntries = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["callId"] = StructuredValue.FromString(callId),
            ["tool"] = StructuredValue.FromString(tool),
            ["arguments"] = toolInput,
        };
        if (!string.IsNullOrWhiteSpace(threadId))
        {
            parameterEntries["threadId"] = StructuredValue.FromString(threadId);
        }

        if (!string.IsNullOrWhiteSpace(turnId))
        {
            parameterEntries["turnId"] = StructuredValue.FromString(turnId);
        }

        var toolMetadataEntries = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["requestMethod"] = StructuredValue.FromString(requestMethod),
            ["parameters"] = StructuredValue.FromObject(parameterEntries),
        };
        if (!string.IsNullOrWhiteSpace(threadId))
        {
            toolMetadataEntries["threadId"] = StructuredValue.FromString(threadId);
        }

        if (!string.IsNullOrWhiteSpace(turnId))
        {
            toolMetadataEntries["turnId"] = StructuredValue.FromString(turnId);
        }

        var toolMetadata = new MetadataBag(toolMetadataEntries);

        ToolInvocationResult response;
        try
        {
            if (options.DynamicToolCallHandler is null)
            {
                response = new ToolInvocationResult(
                    new CallId(callId),
                    tool,
                    failure: new ToolInvocationFailure(
                        "dynamic_tool_handler_not_configured",
                        $"dynamic tool call handler is not configured: {tool}"));
            }
            else
            {
                response = await options.DynamicToolCallHandler(
                        new ToolInvocationRequest(
                            new CallId(callId),
                            tool,
                            "invoke",
                            toolInput,
                            toolMetadata),
                        cancellationToken)
                    .ConfigureAwait(false)
                    ?? new ToolInvocationResult(
                        new CallId(callId),
                        tool,
                        failure: new ToolInvocationFailure(
                            "dynamic_tool_handler_returned_no_result",
                            $"dynamic tool call handler returned no result: {tool}"));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            response = new ToolInvocationResult(
                new CallId(callId),
                tool,
                failure: new ToolInvocationFailure(
                    "dynamic_tool_handler_failed",
                    Normalize(ex.Message) ?? $"dynamic tool call failed: {tool}"));
        }

        var serializedDynamicToolResponse = providerServerRequestResponseSerializer.SerializeDynamicToolCallResponse(response);
        await SendServerRequestResponseAsync(
                requestId,
                serializedDynamicToolResponse.Payload.ToPlainObject(),
                cancellationToken)
            .ConfigureAwait(false);
        var outputText = serializedDynamicToolResponse.OutputText;
        var completedStatus = serializedDynamicToolResponse.Success ? "completed" : "failed";
        RaiseProjectedProviderEvents(
            new ProviderEventProjectionContext(
                ThreadId: threadId,
                TurnId: turnId,
                CallId: callId,
                ToolName: tool,
                Status: completedStatus,
                Phase: phase,
                SourceMethod: requestMethod),
            providerToolEventFactory.CreateToolResult(new ProviderToolResultRequest(callId, null, tool, null, inputText, outputText, false)),
            ControlPlaneConversationStreamEventKind.ToolCallCompleted);
    }

    private async Task SendServerRequestResponseAsync(JsonElement requestId, object? result, CancellationToken cancellationToken)
    {
        await WriteJsonLineAsync(new Dictionary<string, object?>
        {
            ["id"] = ConvertRpcId(requestId),
            ["result"] = result,
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ApprovalResponse> WaitForApprovalDecisionAsync(string callId, CancellationToken cancellationToken)
    {
        var normalizedCallId = Normalize(callId) ?? callId;
        var pendingRequest = new TaskCompletionSource<ApprovalResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingApprovalRequests[normalizedCallId] = pendingRequest;

        using (cancellationToken.Register(static state => ((TaskCompletionSource<ApprovalResponse>)state!).TrySetCanceled(), pendingRequest))
        {
            try
            {
                return await pendingRequest.Task.ConfigureAwait(false);
            }
            finally
            {
                pendingApprovalRequests.TryRemove(normalizedCallId, out _);
                RemovePendingApprovalProjection(normalizedCallId);
            }
        }
    }

    private static string ResolveApprovalStatus(ApprovalResponse response)
    {
        return response.EffectiveDecision switch
        {
            ApprovalResponseDecision.Accept => "accepted",
            ApprovalResponseDecision.AcceptForSession => "acceptedForSession",
            ApprovalResponseDecision.AcceptAndRemember => "acceptedAndRemembered",
            ApprovalResponseDecision.AcceptWithExecPolicyAmendment => "acceptedWithExecPolicyAmendment",
            ApprovalResponseDecision.ApplyNetworkPolicyAmendment => "appliedNetworkPolicyAmendment",
            ApprovalResponseDecision.Decline => "declined",
            ApprovalResponseDecision.Cancel => "cancelled",
            _ => response.IsApproved ? "accepted" : "declined",
        };
    }

    private static string BuildApprovalDecisionText(ApprovalResponse response)
    {
        var decision = response.ToProtocolDecisionToken();
        var note = Normalize(response.Note);
        return string.IsNullOrWhiteSpace(note)
            ? decision
            : $"{decision} | {note}";
    }

    private void HandleNotification(AppServerNotificationEnvelope notification)
    {
        if (TryHandleTypedNotification(notification))
        {
            return;
        }

        var threadId = ResolveExplicitNotificationThreadId(notification);
        RaiseRawNotification(
            threadId,
            ResolveExplicitNotificationTurnId(notification),
            notification.Method,
            notification.RawJson);
    }

    private void HandleNotification(JsonElement root, string rawJson)
    {
        if (AppServerProtocolParser.TryParseNotification(root, rawJson, out var notification))
        {
            HandleNotification(notification);
            return;
        }

        var method = ReadString(root, "method");
        if (string.IsNullOrWhiteSpace(method))
        {
            return;
        }

        RaiseRawNotification(null, null, method, rawJson);
    }

    private bool TryHandleTypedNotification(AppServerNotificationEnvelope notification)
    {
        switch (notification.Method)
        {
            case "thread/started":
            {
                var parameters = AppServerJsonHelpers.Deserialize<ThreadStartedParamsDto>(notification.Params);
                if (parameters is null)
                {
                    return false;
                }

                HandleThreadStartedNotification(notification, parameters);
                return true;
            }
            case "thread/closed":
            {
                var parameters = AppServerJsonHelpers.Deserialize<ThreadClosedParamsDto>(notification.Params);
                if (parameters is null)
                {
                    return false;
                }

                HandleThreadClosedNotification(notification, parameters);
                return true;
            }
            case "serverRequest/resolved":
            {
                var parameters = AppServerJsonHelpers.Deserialize<ServerRequestResolvedParamsDto>(notification.Params);
                if (parameters is null)
                {
                    return false;
                }

                HandleServerRequestResolvedNotification(notification, parameters);
                return true;
            }
            case "turn/started":
            {
                var parameters = AppServerJsonHelpers.Deserialize<TurnStartedParamsDto>(notification.Params);
                if (parameters is null)
                {
                    return false;
                }

                HandleTurnStartedNotification(notification, parameters);
                return true;
            }
            case "hook/started":
            {
                var parameters = AppServerJsonHelpers.Deserialize<HookStartedParamsDto>(notification.Params);
                if (parameters is null)
                {
                    return false;
                }

                HandleHookStartedNotification(notification, parameters);
                return true;
            }
            case "turn/diff/updated":
            {
                var parameters = AppServerJsonHelpers.Deserialize<TurnDiffUpdatedParamsDto>(notification.Params);
                if (parameters is null)
                {
                    return false;
                }

                HandleTurnDiffUpdatedNotification(notification, parameters);
                return true;
            }
            case "turn/plan/updated":
            {
                var parameters = AppServerJsonHelpers.Deserialize<TurnPlanUpdatedParamsDto>(notification.Params);
                if (parameters is null)
                {
                    return false;
                }

                HandleTurnPlanUpdatedNotification(notification, parameters);
                return true;
            }
            case "turn/completed":
            {
                var parameters = AppServerJsonHelpers.Deserialize<TurnCompletedParamsDto>(notification.Params);
                if (parameters is null)
                {
                    return false;
                }

                HandleTurnCompletedNotification(notification, parameters);
                return true;
            }
            case "hook/completed":
            {
                var parameters = AppServerJsonHelpers.Deserialize<HookCompletedParamsDto>(notification.Params);
                if (parameters is null)
                {
                    return false;
                }

                HandleHookCompletedNotification(notification, parameters);
                return true;
            }
            case "rawResponseItem/completed":
            {
                var parameters = AppServerJsonHelpers.Deserialize<RawResponseItemCompletedParamsDto>(notification.Params);
                if (parameters is null)
                {
                    return false;
                }

                HandleRawResponseItemCompletedNotification(notification, parameters);
                return true;
            }
            case "error":
            {
                var parameters = AppServerJsonHelpers.Deserialize<ErrorNotificationParamsDto>(notification.Params);
                if (parameters is null)
                {
                    return false;
                }

                HandleErrorNotification(notification, parameters);
                return true;
            }
            case "turn/steered":
            {
                var parameters = AppServerJsonHelpers.Deserialize<TurnSteeredParamsDto>(notification.Params);
                if (parameters is null)
                {
                    return false;
                }

                HandleTurnSteeredNotification(notification, parameters);
                return true;
            }
            case "mcpServerStatus/list/updated":
            {
                var parameters = AppServerJsonHelpers.Deserialize<McpServerStatusListUpdatedParamsDto>(notification.Params);
                if (parameters is null)
                {
                    return false;
                }

                HandleMcpServerStatusUpdatedNotification(notification, parameters);
                return true;
            }
            case "thread/archived":
                HandleThreadArchivedNotification(notification);
                return true;
            case "thread/unarchived":
                HandleThreadUnarchivedNotification(notification);
                return true;
            case "thread/status/changed":
            {
                var parameters = AppServerJsonHelpers.Deserialize<ThreadStatusChangedParamsDto>(notification.Params);
                if (parameters is null)
                {
                    return false;
                }

                HandleThreadStatusChangedNotification(notification, parameters);
                return true;
            }
            case "thread/name/updated":
            {
                var parameters = AppServerJsonHelpers.Deserialize<ThreadNameUpdatedParamsDto>(notification.Params);
                if (parameters is null)
                {
                    return false;
                }

                HandleThreadNameUpdatedNotification(notification, parameters);
                return true;
            }
            case "thread/tokenUsage/updated":
            {
                var parameters = AppServerJsonHelpers.Deserialize<ThreadTokenUsageUpdatedParamsDto>(notification.Params);
                if (parameters is null)
                {
                    return false;
                }

                HandleThreadTokenUsageUpdatedNotification(notification, parameters);
                return true;
            }
            case "thread/compacted":
                HandleThreadCompactedNotification(notification);
                return true;
            case "skills/changed":
                HandleSkillsChangedNotification(notification);
                return true;
            case "command/exec/outputDelta":
            {
                var parameters = AppServerJsonHelpers.Deserialize<CommandExecOutputDeltaParamsDto>(notification.Params);
                if (parameters is null)
                {
                    return false;
                }

                HandleCommandExecOutputDeltaNotification(notification, parameters);
                return true;
            }
            case "app/list/updated":
            {
                var parameters = AppServerJsonHelpers.Deserialize<AppListUpdatedParamsDto>(notification.Params);
                if (parameters is null)
                {
                    return false;
                }

                HandleAppListUpdatedNotification(notification, parameters);
                return true;
            }
            case "configWarning":
            {
                var parameters = AppServerJsonHelpers.Deserialize<ConfigWarningParamsDto>(notification.Params);
                if (parameters is null)
                {
                    return false;
                }

                HandleConfigWarningNotification(notification, parameters);
                return true;
            }
            case "deprecationNotice":
            {
                var parameters = AppServerJsonHelpers.Deserialize<DeprecationNoticeParamsDto>(notification.Params);
                if (parameters is null)
                {
                    return false;
                }

                HandleDeprecationNoticeNotification(notification, parameters);
                return true;
            }
            case "windowsSandbox/setupCompleted":
            {
                var parameters = AppServerJsonHelpers.Deserialize<WindowsSandboxSetupCompletedParamsDto>(notification.Params);
                if (parameters is null)
                {
                    return false;
                }

                HandleWindowsSandboxSetupCompletedNotification(notification, parameters);
                return true;
            }
            case "mcpServer/oauthLogin/completed":
            {
                var parameters = AppServerJsonHelpers.Deserialize<McpServerOauthLoginCompletedParamsDto>(notification.Params);
                if (parameters is null)
                {
                    return false;
                }

                HandleMcpServerOauthLoginCompletedNotification(notification, parameters);
                return true;
            }
            case "thread/realtime/started":
            {
                var parameters = AppServerJsonHelpers.Deserialize<ThreadRealtimeStartedParamsDto>(notification.Params);
                if (parameters is null)
                {
                    return false;
                }

                HandleThreadRealtimeStartedNotification(notification, parameters);
                return true;
            }
            case "fuzzyFileSearch/sessionUpdated":
            {
                var parameters = AppServerJsonHelpers.Deserialize<FuzzyFileSearchSessionUpdatedParamsDto>(notification.Params);
                if (parameters is null)
                {
                    return false;
                }

                HandleFuzzyFileSearchSessionUpdatedNotification(notification, parameters);
                return true;
            }
            case "fuzzyFileSearch/sessionCompleted":
            {
                var parameters = AppServerJsonHelpers.Deserialize<FuzzyFileSearchSessionCompletedParamsDto>(notification.Params);
                if (parameters is null)
                {
                    return false;
                }

                HandleFuzzyFileSearchSessionCompletedNotification(notification, parameters);
                return true;
            }
            case "thread/realtime/itemAdded":
            {
                var parameters = AppServerJsonHelpers.Deserialize<ThreadRealtimeItemAddedParamsDto>(notification.Params);
                if (parameters is null)
                {
                    return false;
                }

                HandleThreadRealtimeItemAddedNotification(notification, parameters);
                return true;
            }
            case "thread/realtime/outputAudio/delta":
            {
                var parameters = AppServerJsonHelpers.Deserialize<ThreadRealtimeOutputAudioDeltaParamsDto>(notification.Params);
                if (parameters is null)
                {
                    return false;
                }

                HandleThreadRealtimeOutputAudioDeltaNotification(notification, parameters);
                return true;
            }
            case "thread/realtime/error":
            {
                var parameters = AppServerJsonHelpers.Deserialize<ThreadRealtimeErrorParamsDto>(notification.Params);
                if (parameters is null)
                {
                    return false;
                }

                HandleThreadRealtimeErrorNotification(notification, parameters);
                return true;
            }
            case "thread/realtime/closed":
            {
                var parameters = AppServerJsonHelpers.Deserialize<ThreadRealtimeClosedParamsDto>(notification.Params);
                if (parameters is null)
                {
                    return false;
                }

                HandleThreadRealtimeClosedNotification(notification, parameters);
                return true;
            }
            case "model/rerouted":
            {
                var parameters = AppServerJsonHelpers.Deserialize<ModelReroutedParamsDto>(notification.Params);
                if (parameters is null)
                {
                    return false;
                }

                HandleModelReroutedNotification(notification, parameters);
                return true;
            }
            default:
                if (IsTypedItemNotificationMethod(notification.Method))
                {
                    var parameters = AppServerJsonHelpers.Deserialize<ItemNotificationParamsDto>(notification.Params);
                    return parameters is not null && HandleTypedItemNotification(notification, parameters);
                }

                return false;
        }
    }

    private static bool IsTypedItemNotificationMethod(string method)
        => method.StartsWith("item/", StringComparison.Ordinal) || method.Contains("/item/", StringComparison.Ordinal);

    private string? ResolveCurrentTurnControlId()
        => Normalize(activeTurnId) ?? Normalize(submittedTurnId);

    private static string? ResolveExplicitNotificationTurnId(AppServerNotificationEnvelope notification, string? turnId = null, string? nestedTurnId = null)
        => Normalize(turnId)
           ?? Normalize(nestedTurnId)
           ?? Normalize(ReadString(notification.Params, "turnId"))
           ?? Normalize(ReadString(notification.Params, "turn", "id"));

    private string? TryResolveUniqueNonActiveObservedTurnIdForThread(string? threadId, out bool hasNonActiveObservedTurns)
    {
        hasNonActiveObservedTurns = false;
        var normalizedThreadId = Normalize(threadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            return null;
        }

        var normalizedActiveTurnId = Normalize(activeTurnId);
        lock (observedTurnStateGate)
        {
            if (!observedTurnIdsByThread.TryGetValue(normalizedThreadId!, out var observedTurnIds)
                || observedTurnIds.Count == 0)
            {
                return null;
            }

            string? candidate = null;
            foreach (var observedTurnId in observedTurnIds)
            {
                if (!string.IsNullOrWhiteSpace(normalizedActiveTurnId)
                    && string.Equals(observedTurnId, normalizedActiveTurnId, StringComparison.Ordinal))
                {
                    continue;
                }

                hasNonActiveObservedTurns = true;
                if (candidate is null)
                {
                    candidate = observedTurnId;
                    continue;
                }

                return null;
            }

            return candidate;
        }
    }

    private string? ResolveTypedItemTurnId(string? threadId, AppServerNotificationEnvelope notification, string? turnId = null)
    {
        var explicitTurnId = ResolveExplicitNotificationTurnId(notification, turnId);
        if (!string.IsNullOrWhiteSpace(explicitTurnId))
        {
            return explicitTurnId;
        }

        var observedTurnId = TryResolveUniqueNonActiveObservedTurnIdForThread(threadId, out var hasNonActiveObservedTurns);
        if (!string.IsNullOrWhiteSpace(observedTurnId))
        {
            return observedTurnId;
        }

        if (hasNonActiveObservedTurns)
        {
            return null;
        }

        return ResolveFallbackTurnIdForThread(threadId);
    }

    private string? ResolveStructuredNotificationTurnId(string? threadId, AppServerNotificationEnvelope notification, string? turnId = null)
        => ResolveTypedItemTurnId(threadId, notification, turnId);

    private string? ResolveFallbackTurnIdForThread(string? threadId)
    {
        var normalizedActiveTurnId = Normalize(activeTurnId);
        if (string.IsNullOrWhiteSpace(normalizedActiveTurnId))
        {
            return null;
        }

        var normalizedThreadId = Normalize(threadId);
        var normalizedActiveThreadId = Normalize(activeThreadId);
        if (!string.IsNullOrWhiteSpace(normalizedThreadId)
            && !string.IsNullOrWhiteSpace(normalizedActiveThreadId)
            && !string.Equals(normalizedThreadId, normalizedActiveThreadId, StringComparison.Ordinal))
        {
            return null;
        }

        return normalizedActiveTurnId;
    }

    private static string? ResolveExplicitNotificationThreadId(
        AppServerNotificationEnvelope notification,
        string? threadId = null,
        string? nestedThreadId = null)
        => threadId
           ?? nestedThreadId
           ?? ReadString(notification.Params, "threadId")
           ?? ReadString(notification.Params, "thread", "id");

    private string? ResolveNotificationThreadId(AppServerNotificationEnvelope notification, string? threadId = null, string? nestedThreadId = null)
        => ResolveExplicitNotificationThreadId(notification, threadId, nestedThreadId)
           ?? activeThreadId;

    private string? ResolveNotificationTurnId(string? threadId, AppServerNotificationEnvelope notification, string? turnId = null, string? nestedTurnId = null)
        => ResolveExplicitNotificationTurnId(notification, turnId, nestedTurnId)
           ?? ResolveFallbackTurnIdForThread(threadId);

    private string? ResolveTurnStartedTurnId(
        string? threadId,
        AppServerNotificationEnvelope notification,
        string? turnId = null,
        string? nestedTurnId = null)
    {
        var explicitTurnId = ResolveExplicitNotificationTurnId(notification, turnId, nestedTurnId);
        if (!string.IsNullOrWhiteSpace(explicitTurnId))
        {
            return explicitTurnId;
        }

        var normalizedSubmittedTurnId = Normalize(submittedTurnId);
        var normalizedThreadId = Normalize(threadId);
        var normalizedActiveThreadId = Normalize(activeThreadId);
        if (!string.IsNullOrWhiteSpace(normalizedSubmittedTurnId)
            && !string.IsNullOrWhiteSpace(normalizedThreadId)
            && (string.IsNullOrWhiteSpace(normalizedActiveThreadId)
                || string.Equals(normalizedThreadId, normalizedActiveThreadId, StringComparison.Ordinal)))
        {
            return normalizedSubmittedTurnId;
        }

        return null;
    }

    private string? ResolveServerRequestTurnId(string? threadId, string? turnId = null)
    {
        var normalizedTurnId = Normalize(turnId);
        if (!string.IsNullOrWhiteSpace(normalizedTurnId))
        {
            return normalizedTurnId;
        }

        _ = TryResolveUniqueNonActiveObservedTurnIdForThread(threadId, out var hasNonActiveObservedTurns);
        if (hasNonActiveObservedTurns)
        {
            return null;
        }

        return ResolveFallbackTurnIdForThread(threadId);
    }

    private void TryAdoptActiveThreadFromNotification(string? threadId)
    {
        var normalizedThreadId = Normalize(threadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(activeThreadId)
            || string.Equals(activeThreadId, normalizedThreadId, StringComparison.Ordinal))
        {
            activeThreadId = normalizedThreadId;
        }
    }

    private void TryAdoptActiveTurnFromNotification(string? threadId, string? turnId)
    {
        var normalizedTurnId = Normalize(turnId);
        if (string.IsNullOrWhiteSpace(normalizedTurnId))
        {
            return;
        }

        var normalizedThreadId = Normalize(threadId);
        if (!string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            if (string.IsNullOrWhiteSpace(activeThreadId))
            {
                activeThreadId = normalizedThreadId;
            }
            else if (!string.Equals(activeThreadId, normalizedThreadId, StringComparison.Ordinal))
            {
                return;
            }
        }

        activeTurnId = normalizedTurnId;
        if (string.Equals(submittedTurnId, normalizedTurnId, StringComparison.Ordinal))
        {
            submittedTurnId = null;
        }
    }

    private static ProviderNotificationItem? ToProviderNotificationItem(AppServerResponseItemDto? item)
        => item is null
            ? null
            : new ProviderNotificationItem(
                item.Id,
                item.Type,
                item.Status,
                item.Phase,
                item.Name,
                item.ToolName,
                item.CallId,
                item.Text,
                item.OutputText,
                item.Delta,
                item.Output,
                item.Arguments,
                item.Input);

    private static ProviderRawResponseItemCompletedNotification ToProviderRawResponseItemCompletedNotification(
        AppServerNotificationEnvelope notification,
        RawResponseItemCompletedParamsDto parameters)
        => new(
            notification.Method,
            parameters.ThreadId,
            parameters.TurnId,
            ToProviderNotificationItem(parameters.Item));

    private static ProviderErrorNotification ToProviderErrorNotification(
        AppServerNotificationEnvelope notification,
        ErrorNotificationParamsDto parameters)
        => new(
            notification.Method,
            parameters.ThreadId,
            parameters.TurnId,
            parameters.Message,
            parameters.Error is null
                ? null
                : new ProviderNotificationError(
                    parameters.Error.Message,
                    parameters.Error.AdditionalDetails),
            parameters.WillRetry);

    private static ProviderItemNotification ToProviderItemNotification(
        AppServerNotificationEnvelope notification,
        ItemNotificationParamsDto parameters)
    {
        var rawItemJson = parameters.Arguments is null
                          && parameters.Input is null
                          && parameters.Item?.Arguments is null
                          && parameters.Item?.Input is null
            ? TryReadRawItemJson(notification.Params)
            : null;
        return new(
            notification.Method,
            parameters.ThreadId,
            parameters.TurnId,
            parameters.ItemId,
            parameters.Type,
            parameters.Status,
            parameters.Phase,
            parameters.ToolName,
            parameters.Name,
            parameters.CallId,
            parameters.ToolCallId,
            parameters.Delta,
            parameters.Output,
            parameters.Arguments ?? rawItemJson,
            parameters.Input,
            parameters.RequiresApproval,
            parameters.ApprovalRequired,
            parameters.Approval?.Required,
            parameters.Message,
            parameters.SummaryIndex,
            parameters.ContentIndex,
            parameters.ProcessId,
            parameters.Stdin,
            ToProviderNotificationItem(parameters.Item));
    }

    private static string? TryReadRawItemJson(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("item", out var item)
            || item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return item.GetRawText();
    }

    private static string? ResolveItemId(ItemNotificationParamsDto parameters)
        => parameters.Item?.Id ?? parameters.ItemId;

    private static string? ResolveItemType(ItemNotificationParamsDto parameters)
        => parameters.Item?.Type ?? parameters.Type;

    private static string? ResolveItemPhase(ItemNotificationParamsDto parameters)
        => parameters.Item?.Phase ?? parameters.Phase;

    private static string? ResolveItemStatus(ItemNotificationParamsDto parameters)
        => parameters.Item?.Status ?? parameters.Status;

    private static string? ResolveItemCallId(ItemNotificationParamsDto parameters)
        => parameters.CallId ?? parameters.ToolCallId ?? parameters.Item?.CallId;

    private static string? ResolveItemToolName(ItemNotificationParamsDto parameters)
        => parameters.ToolName ?? parameters.Name ?? parameters.Item?.ToolName ?? parameters.Item?.Name;

    private static string? ResolveItemDelta(ItemNotificationParamsDto parameters)
        => parameters.Delta ?? parameters.Item?.Delta;

    private static string? ResolveItemOutput(ItemNotificationParamsDto parameters)
        => parameters.Output ?? parameters.Item?.Output;

    private static string? ResolveItemArguments(ItemNotificationParamsDto parameters)
        => parameters.Arguments
           ?? parameters.Input
           ?? parameters.Item?.Arguments
           ?? parameters.Item?.Input;

    private static string? ResolveReasoningText(ItemNotificationParamsDto parameters)
        => ResolveItemDelta(parameters)
           ?? parameters.Message
           ?? parameters.Item?.Text
           ?? parameters.Item?.OutputText
           ?? parameters.Output;

    private static bool? ResolveItemRequiresApproval(ItemNotificationParamsDto parameters)
        => parameters.RequiresApproval ?? parameters.ApprovalRequired ?? parameters.Approval?.Required;

    private static ToolCallEventPayload BuildToolCallEventPayload(
        string? itemId,
        string? callId,
        string? toolName,
        string? inputText,
        string? outputText,
        string? status,
        string? phase,
        bool? requiresApproval)
        => new(
            itemId,
            callId,
            toolName,
            null,
            inputText,
            outputText,
            status,
            phase,
            requiresApproval);

    private void RaiseApprovalRequestedEvent(
        ProviderEventProjectionContext context,
        string? text,
        ApprovalRequestPayload approvalRequest)
    {
        TrackPendingApprovalProjection(
            context.CallId ?? string.Empty,
            context.ThreadId,
            Normalize(approvalRequest.Summary) ?? Normalize(text) ?? Normalize(context.ToolName) ?? "等待审批",
            Normalize(text) ?? Normalize(approvalRequest.Summary) ?? Normalize(approvalRequest.ApprovalKind) ?? "Provider 请求继续执行。");

        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.ApprovalRequested,
            ThreadId = ToThreadId(context.ThreadId),
            TurnId = ToTurnId(context.TurnId),
            ItemId = context.ItemId,
            CallId = ToCallId(context.CallId),
            ToolName = context.ToolName ?? approvalRequest.ToolName,
            ServerName = context.ServerName,
            Text = text,
            Status = context.Status,
            Phase = context.Phase,
            RequiresApproval = true,
            ApprovalKind = approvalRequest.ApprovalKind,
            AvailableDecisions = approvalRequest.AvailableDecisions,
            AvailableDecisionOptions = approvalRequest.AvailableDecisionOptions?.Select(ToControlPlaneApprovalDecisionOption).ToArray(),
            Message = context.SourceMethod,
            SourceMethod = context.SourceMethod,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.ApprovalRequest,
            Payload = ToStructuredPayload(approvalRequest),
            Diagnostics = BuildStreamDiagnostics(context.RawJson),
        });
    }

    private void RaisePermissionRequestedEvent(
        ProviderEventProjectionContext context,
        string? text,
        PermissionRequestPayload permissionRequest)
    {
        TrackPendingApprovalProjection(
            context.CallId ?? string.Empty,
            context.ThreadId,
            "权限请求",
            Normalize(permissionRequest.Summary) ?? Normalize(permissionRequest.Reason) ?? Normalize(text) ?? "Provider 请求权限授权。");

        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.PermissionRequested,
            ThreadId = ToThreadId(context.ThreadId),
            TurnId = ToTurnId(context.TurnId),
            ItemId = context.ItemId,
            CallId = ToCallId(context.CallId),
            ToolName = context.ToolName,
            ServerName = context.ServerName,
            Text = text,
            Status = context.Status,
            Phase = context.Phase,
            Message = context.SourceMethod,
            SourceMethod = context.SourceMethod,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.PermissionRequest,
            Payload = ToStructuredPayload(permissionRequest),
            Diagnostics = BuildStreamDiagnostics(context.RawJson),
        });
    }

    private void RaiseUserInputRequestedEvent(
        ProviderEventProjectionContext context,
        string? text,
        UserInputRequestPayload userInputRequest)
    {
        TrackPendingUserInputProjection(
            context.CallId ?? string.Empty,
            context.ThreadId,
            Normalize(userInputRequest.Summary)
            ?? Normalize(text)
            ?? userInputRequest.Questions.Select(static question => Normalize(question.Prompt)).FirstOrDefault(static prompt => !string.IsNullOrWhiteSpace(prompt))
            ?? "等待用户补充输入。");

        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.UserInputRequested,
            ThreadId = ToThreadId(context.ThreadId),
            TurnId = ToTurnId(context.TurnId),
            ItemId = context.ItemId,
            CallId = ToCallId(context.CallId),
            ToolName = context.ToolName,
            ServerName = context.ServerName,
            Text = text,
            Status = context.Status,
            Phase = context.Phase,
            Message = context.SourceMethod,
            SourceMethod = context.SourceMethod,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.UserInputRequest,
            Payload = ToStructuredPayload(userInputRequest),
            Diagnostics = BuildStreamDiagnostics(context.RawJson),
        });
    }

    private static HookRunPayload? BuildHookRunPayload(HookRunSummaryDto? run)
    {
        if (run is null)
        {
            return null;
        }

        var entries = run.Entries?
            .Select(static entry => new HookOutputEntryPayload(
                entry.Kind ?? string.Empty,
                entry.Text ?? string.Empty))
            .ToArray()
            ?? Array.Empty<HookOutputEntryPayload>();

        return new HookRunPayload(
            run.Id,
            run.EventName,
            run.HandlerType,
            run.ExecutionMode,
            run.Scope,
            run.SourcePath,
            run.DisplayOrder,
            run.Status,
            run.StatusMessage,
            run.StartedAt,
            run.CompletedAt,
            run.DurationMs,
            entries);
    }

    private static ReasoningEventPayload BuildReasoningEventPayload(
        string? itemId,
        string? status,
        string? phase,
        string? text,
        string? sourceMethod,
        long? summaryIndex = null,
        long? contentIndex = null)
        => new(
            itemId,
            status,
            phase,
            text,
            sourceMethod,
            summaryIndex,
            contentIndex);

    private static ModelReroutedPayload BuildModelReroutedPayload(ModelReroutedParamsDto parameters)
        => new(
            parameters.FromModel,
            parameters.ToModel,
            parameters.Reason);

    private static string? BuildHookSummaryText(HookRunPayload? run)
        => Normalize(run?.SourcePath)
           ?? Normalize(run?.EventName)
           ?? Normalize(run?.Id);

    private static string? BuildModelReroutedText(ModelReroutedPayload? payload)
    {
        var fromModel = Normalize(payload?.FromModel);
        var toModel = Normalize(payload?.ToModel);
        if (!string.IsNullOrWhiteSpace(fromModel) && !string.IsNullOrWhiteSpace(toModel))
        {
            return $"{fromModel} -> {toModel}";
        }

        return fromModel ?? toModel ?? Normalize(payload?.Reason);
    }

    private void HandleThreadStartedNotification(AppServerNotificationEnvelope notification, ThreadStartedParamsDto parameters)
    {
        var threadId = ResolveNotificationThreadId(notification, parameters.ThreadId, parameters.Thread?.Id);
        var turnId = parameters.TurnId
                     ?? ReadString(notification.Params, "turnId")
                     ?? ReadString(notification.Params, "turn", "id");
        TryAdoptActiveThreadFromNotification(threadId);
        RaiseRawNotification(threadId, turnId, notification.Method, notification.RawJson);
    }

    private void HandleThreadClosedNotification(AppServerNotificationEnvelope notification, ThreadClosedParamsDto parameters)
    {
        var closedThreadId = ResolveExplicitNotificationThreadId(notification, parameters.ThreadId);
        var turnId = ResolveExplicitNotificationTurnId(notification, parameters.TurnId);
        RemoveObservedTurnsThread(closedThreadId);
        if (!string.IsNullOrWhiteSpace(closedThreadId)
            && string.Equals(activeThreadId, closedThreadId, StringComparison.Ordinal))
        {
            activeThreadId = null;
            activeTurnId = null;
            submittedTurnId = null;
        }

        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.Info,
            ThreadId = ToThreadId(closedThreadId),
            TurnId = ToTurnId(turnId),
            Message = notification.Method,
            Diagnostics = BuildStreamDiagnostics(notification.RawJson),
        });
    }

    private void HandleServerRequestResolvedNotification(AppServerNotificationEnvelope notification, ServerRequestResolvedParamsDto parameters)
    {
        var threadId = ResolveExplicitNotificationThreadId(notification, parameters.ThreadId);
        string? requestIdToken = null;
        string? requestIdRaw = null;
        long? requestId = null;
        PendingInteractiveServerRequest? pendingRequest = null;
        if (parameters.RequestId.HasValue
            && TryResolveServerRequestId(parameters.RequestId.Value, out var requestIdValue, out var resolvedRequestIdToken, out var numericRequestId))
        {
            requestIdToken = resolvedRequestIdToken;
            requestId = numericRequestId;
            requestIdRaw = requestIdValue as string;

            PendingInteractiveServerRequest? trackedPendingRequest = null;
            var matchedPendingRequest = pendingInteractiveServerRequestsByRequestToken.TryRemove(resolvedRequestIdToken, out trackedPendingRequest);
            if (!matchedPendingRequest
                && numericRequestId.HasValue)
            {
                matchedPendingRequest = pendingInteractiveServerRequestsByRequestId.TryRemove(numericRequestId.Value, out trackedPendingRequest);
            }

            if (matchedPendingRequest && trackedPendingRequest is not null)
            {
                pendingRequest = trackedPendingRequest;
                if (trackedPendingRequest.NumericRequestId.HasValue)
                {
                    pendingInteractiveServerRequestsByRequestId.TryRemove(trackedPendingRequest.NumericRequestId.Value, out _);
                }

                pendingInteractiveServerRequestsByRequestToken.TryRemove(trackedPendingRequest.RequestIdToken, out _);
                pendingInteractiveServerRequestIdsByCallId.TryRemove(trackedPendingRequest.CallId, out _);
                RemovePendingInteractiveReplayContext(trackedPendingRequest.CallId);
                CancelPendingInteractiveServerRequest(trackedPendingRequest);
            }
        }

        var payload = !string.IsNullOrWhiteSpace(requestIdToken)
            ? new ServerRequestResolvedPayload(
                requestId ?? pendingRequest?.NumericRequestId ?? 0L,
                pendingRequest?.RequestKind,
                pendingRequest?.CallId,
                requestIdRaw)
            : null;
        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.ServerRequestResolved,
            ThreadId = ToThreadId(threadId ?? pendingRequest?.ThreadId),
            TurnId = ToTurnId(pendingRequest?.TurnId),
            CallId = ToCallId(pendingRequest?.CallId),
            ToolName = pendingRequest?.ToolName,
            Message = notification.Method,
            PayloadKind = payload is null ? null : ControlPlaneConversationStreamPayloadKind.ServerRequestResolved,
            Payload = ToStructuredPayload(payload),
            Diagnostics = BuildStreamDiagnostics(notification.RawJson),
        });
    }

    private void HandleTurnStartedNotification(AppServerNotificationEnvelope notification, TurnStartedParamsDto parameters)
    {
        var threadId = ResolveNotificationThreadId(notification, parameters.ThreadId);
        var turnId = ResolveTurnStartedTurnId(threadId, notification, parameters.TurnId, parameters.Turn?.Id);
        var previousActiveThreadId = Normalize(activeThreadId);
        var previousActiveTurnId = Normalize(activeTurnId);
        var normalizedThreadId = Normalize(threadId);
        var normalizedTurnId = Normalize(turnId);
        if (!string.IsNullOrWhiteSpace(normalizedThreadId)
            && !string.IsNullOrWhiteSpace(normalizedTurnId)
            && !string.IsNullOrWhiteSpace(previousActiveTurnId)
            && string.Equals(previousActiveThreadId, normalizedThreadId, StringComparison.Ordinal)
            && !string.Equals(previousActiveTurnId, normalizedTurnId, StringComparison.Ordinal))
        {
            ClearTurnScopedPendingInteractiveState(normalizedThreadId, previousActiveTurnId);
        }

        if (!string.IsNullOrWhiteSpace(normalizedTurnId))
        {
            terminalTurnStatuses.TryRemove(normalizedTurnId!, out _);
        }

        TrackObservedTurnForThread(threadId, turnId);
        TryAdoptActiveTurnFromNotification(threadId, turnId);
        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.TurnStarted,
            ThreadId = ToThreadId(threadId),
            TurnId = ToTurnId(turnId),
            Status = parameters.Turn?.Status,
            Message = notification.Method,
            Diagnostics = BuildStreamDiagnostics(notification.RawJson),
        });
    }

    private void HandleHookStartedNotification(AppServerNotificationEnvelope notification, HookStartedParamsDto parameters)
    {
        var threadId = ResolveNotificationThreadId(notification, parameters.ThreadId);
        var turnId = ResolveStructuredNotificationTurnId(threadId, notification, parameters.TurnId);
        var hookRun = BuildHookRunPayload(parameters.Run);
        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.HookStarted,
            ThreadId = ToThreadId(threadId),
            TurnId = ToTurnId(turnId),
            Status = hookRun?.Status,
            Text = BuildHookSummaryText(hookRun),
            SourceMethod = notification.Method,
            Message = notification.Method,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.HookRun,
            Payload = ToStructuredPayload(hookRun),
            Diagnostics = BuildStreamDiagnostics(notification.RawJson, dataJson: notification.Params.GetRawText()),
        });
    }

    private void HandleTurnDiffUpdatedNotification(AppServerNotificationEnvelope notification, TurnDiffUpdatedParamsDto parameters)
    {
        var threadId = ResolveNotificationThreadId(notification, parameters.ThreadId);
        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.DiffUpdated,
            ThreadId = ToThreadId(threadId),
            TurnId = ToTurnId(ResolveStructuredNotificationTurnId(threadId, notification, parameters.TurnId)),
            Text = parameters.Diff,
            Message = notification.Method,
            Diagnostics = BuildStreamDiagnostics(notification.RawJson),
        });
    }

    private void HandleTurnPlanUpdatedNotification(AppServerNotificationEnvelope notification, TurnPlanUpdatedParamsDto parameters)
    {
        var threadId = ResolveNotificationThreadId(notification, parameters.ThreadId);
        var planPayload = BuildPlanEventPayload(parameters);
        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.PlanUpdated,
            ThreadId = ToThreadId(threadId),
            TurnId = ToTurnId(ResolveStructuredNotificationTurnId(threadId, notification, parameters.TurnId)),
            Text = parameters.Explanation,
            SourceMethod = notification.Method,
            Message = notification.Method,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.Plan,
            Payload = ToStructuredPayload(planPayload),
            Diagnostics = BuildStreamDiagnostics(notification.RawJson, dataJson: notification.Params.GetRawText(), metadataJson: notification.RawJson),
        });
    }

    private void HandleTurnCompletedNotification(AppServerNotificationEnvelope notification, TurnCompletedParamsDto parameters)
    {
        var threadId = ResolveNotificationThreadId(notification, parameters.ThreadId);
        var completedTurnId = parameters.Turn?.Id
                              ?? parameters.TurnId
                              ?? TryResolveCompletedTurnIdFromObservedTurns(threadId)
                              ?? activeTurnId;
        var status = parameters.Turn?.Status ?? "completed";
        var text = TakeTurnText(completedTurnId);
        var success = IsSuccessfulTurnCompletionStatus(status);
        var turnError = BuildTurnError(parameters.Turn?.Error, success ? null : ResolveTurnCompletionErrorMessage(status));
        CompleteTurn(
            completedTurnId,
            success,
            status,
            text,
            turnError?.Message,
            notification.RawJson);
        RemoveObservedTurnForThread(threadId, completedTurnId);
        ClearTurnScopedPendingInteractiveState(threadId, completedTurnId);
        _ = ReducePendingInputStateForCompletedTurn(threadId, completedTurnId, status);
        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.TurnCompleted,
            ThreadId = ToThreadId(threadId),
            TurnId = ToTurnId(completedTurnId),
            Status = status,
            Phase = string.IsNullOrWhiteSpace(text) ? null : "final_answer",
            Text = text,
            SourceMethod = notification.Method,
            Message = notification.Method,
            TurnError = turnError,
            Diagnostics = BuildStreamDiagnostics(notification.RawJson),
        });
    }

    private static bool IsSuccessfulTurnCompletionStatus(string? status)
        => string.Equals(Normalize(status) ?? "completed", "completed", StringComparison.OrdinalIgnoreCase);

    private void TryTrackPendingInteractiveServerRequest(
        JsonElement requestId,
        string callId,
        string? threadId,
        string? turnId,
        string requestKind,
        string? toolName,
        string? requestMethod)
    {
        if (!TryResolveServerRequestId(requestId, out var requestIdValue, out var requestIdToken, out var numericRequestId))
        {
            return;
        }

        TrackPendingInteractiveServerRequest(
            requestIdValue,
            requestIdToken,
            numericRequestId,
            callId,
            threadId,
            turnId,
            requestKind,
            toolName,
            requestMethod);
    }

    private void TrackPendingInteractiveServerRequest(
        long requestId,
        string callId,
        string? threadId,
        string? turnId,
        string requestKind,
        string? toolName,
        string? requestMethod)
        => TrackPendingInteractiveServerRequest(
            requestId,
            CreateRequestIdToken(requestId),
            requestId,
            callId,
            threadId,
            turnId,
            requestKind,
            toolName,
            requestMethod);

    private void TrackPendingInteractiveServerRequest(
        object requestIdValue,
        string requestIdToken,
        long? numericRequestId,
        string callId,
        string? threadId,
        string? turnId,
        string requestKind,
        string? toolName,
        string? requestMethod)
    {
        var normalizedCallId = Normalize(callId);
        if (string.IsNullOrWhiteSpace(normalizedCallId)
            || string.IsNullOrWhiteSpace(requestIdToken))
        {
            return;
        }

        var normalizedThreadId = Normalize(threadId);
        var normalizedTurnId = Normalize(turnId);
        var normalizedRequestKind = Normalize(requestKind) ?? string.Empty;
        var normalizedToolName = Normalize(toolName);
        var normalizedRequestMethod = Normalize(requestMethod);
        var pendingRequest = new PendingInteractiveServerRequest(
            requestIdValue,
            requestIdToken,
            numericRequestId,
            normalizedCallId,
            normalizedThreadId,
            normalizedTurnId,
            normalizedRequestKind,
            normalizedToolName);
        pendingInteractiveServerRequestsByRequestToken[requestIdToken] = pendingRequest;
        if (numericRequestId.HasValue)
        {
            pendingInteractiveServerRequestsByRequestId[numericRequestId.Value] = pendingRequest;
            pendingInteractiveServerRequestIdsByCallId[normalizedCallId] = numericRequestId.Value;
        }
        else
        {
            pendingInteractiveServerRequestIdsByCallId.TryRemove(normalizedCallId, out _);
        }

        pendingInteractiveReplayContextsByCallId[normalizedCallId] = new PendingInteractiveReplayContext(
            requestIdValue,
            requestIdToken,
            numericRequestId,
            normalizedCallId,
            normalizedThreadId,
            normalizedTurnId,
            normalizedRequestKind,
            normalizedToolName,
            normalizedRequestMethod);
    }

    private void CancelPendingInteractiveServerRequest(PendingInteractiveServerRequest request)
    {
        if (string.Equals(
                request.RequestKind,
                ProviderServerRequestPendingKinds.ApprovalRequested,
                StringComparison.Ordinal))
        {
            if (pendingApprovalRequests.TryRemove(request.CallId, out var pendingApprovalRequest))
            {
                pendingApprovalRequest.TrySetCanceled();
            }

            return;
        }

        if (string.Equals(
                request.RequestKind,
                ProviderServerRequestPendingKinds.PermissionRequested,
                StringComparison.Ordinal))
        {
            if (pendingPermissionRequests.TryRemove(request.CallId, out var pendingPermissionRequest))
            {
                pendingPermissionRequest.TrySetCanceled();
            }

            return;
        }

        if (string.Equals(
                request.RequestKind,
                ProviderServerRequestPendingKinds.UserInput,
                StringComparison.Ordinal)
            && pendingUserInputRequests.TryRemove(request.CallId, out var pendingUserInputRequest))
        {
            pendingUserInputRequest.TrySetCanceled();
        }
    }

    private void ClearTurnScopedPendingInteractiveState(string? threadId, string? turnId)
    {
        var normalizedThreadId = Normalize(threadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            return;
        }

        var matchingRequests = pendingInteractiveServerRequestsByRequestToken.Values
            .Where(request => IsTurnScopedPendingInteractiveMatch(request.ThreadId, request.TurnId, normalizedThreadId!, turnId))
            .ToArray();
        var matchingReplayContexts = pendingInteractiveReplayContextsByCallId.Values
            .Where(context => IsTurnScopedPendingInteractiveMatch(context.ThreadId, context.TurnId, normalizedThreadId!, turnId))
            .ToArray();
        var matchingApprovalContexts = pendingApprovals.Values
            .Where(context => IsTurnScopedPendingInteractiveMatch(context.ThreadId, context.TurnId, normalizedThreadId!, turnId))
            .ToArray();

        var callIdsToClear = new HashSet<string>(StringComparer.Ordinal);
        foreach (var request in matchingRequests)
        {
            pendingInteractiveServerRequestsByRequestToken.TryRemove(request.RequestIdToken, out _);
            if (request.NumericRequestId.HasValue)
            {
                pendingInteractiveServerRequestsByRequestId.TryRemove(request.NumericRequestId.Value, out _);
            }

            callIdsToClear.Add(request.CallId);
        }

        foreach (var context in matchingReplayContexts)
        {
            callIdsToClear.Add(context.CallId);
        }

        foreach (var context in matchingApprovalContexts)
        {
            callIdsToClear.Add(context.CallId);
        }

        foreach (var callId in callIdsToClear)
        {
            pendingInteractiveServerRequestIdsByCallId.TryRemove(callId, out _);
            RemovePendingInteractiveReplayContext(callId);
            CancelPendingInteractiveRequestByCallId(callId);
            pendingApprovals.TryRemove(callId, out _);
            RemovePendingApprovalProjection(callId);
            RemovePendingUserInputProjection(callId);
        }
    }

    private void ClearThreadPendingInteractiveState(string? threadId)
    {
        var normalizedThreadId = Normalize(threadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            return;
        }

        var matchingRequests = pendingInteractiveServerRequestsByRequestToken.Values
            .Where(request => string.Equals(Normalize(request.ThreadId), normalizedThreadId, StringComparison.Ordinal))
            .ToArray();
        var matchingReplayContexts = pendingInteractiveReplayContextsByCallId.Values
            .Where(context => string.Equals(Normalize(context.ThreadId), normalizedThreadId, StringComparison.Ordinal))
            .ToArray();
        var matchingApprovalContexts = pendingApprovals.Values
            .Where(context => string.Equals(Normalize(context.ThreadId), normalizedThreadId, StringComparison.Ordinal))
            .ToArray();

        var callIdsToClear = new HashSet<string>(StringComparer.Ordinal);
        foreach (var request in matchingRequests)
        {
            pendingInteractiveServerRequestsByRequestToken.TryRemove(request.RequestIdToken, out _);
            if (request.NumericRequestId.HasValue)
            {
                pendingInteractiveServerRequestsByRequestId.TryRemove(request.NumericRequestId.Value, out _);
            }

            callIdsToClear.Add(request.CallId);
        }

        foreach (var context in matchingReplayContexts)
        {
            callIdsToClear.Add(context.CallId);
        }

        foreach (var context in matchingApprovalContexts)
        {
            callIdsToClear.Add(context.CallId);
        }

        foreach (var callId in callIdsToClear)
        {
            pendingInteractiveServerRequestIdsByCallId.TryRemove(callId, out _);
            RemovePendingInteractiveReplayContext(callId);
            CancelPendingInteractiveRequestByCallId(callId);
            pendingApprovals.TryRemove(callId, out _);
            RemovePendingApprovalProjection(callId);
            RemovePendingUserInputProjection(callId);
        }
    }

    private void CancelPendingInteractiveRequestByCallId(string callId)
    {
        if (pendingApprovalRequests.TryRemove(callId, out var pendingApprovalRequest))
        {
            RemovePendingApprovalProjection(callId);
            pendingApprovalRequest.TrySetCanceled();
        }

        if (pendingPermissionRequests.TryRemove(callId, out var pendingPermissionRequest))
        {
            RemovePendingApprovalProjection(callId);
            pendingPermissionRequest.TrySetCanceled();
        }

        if (pendingUserInputRequests.TryRemove(callId, out var pendingUserInputRequest))
        {
            RemovePendingUserInputProjection(callId);
            pendingUserInputRequest.TrySetCanceled();
        }
    }

    private static bool IsTurnScopedPendingInteractiveMatch(
        string? entryThreadId,
        string? entryTurnId,
        string completedThreadId,
        string? completedTurnId)
    {
        var normalizedEntryThreadId = Normalize(entryThreadId);
        if (string.IsNullOrWhiteSpace(normalizedEntryThreadId)
            || !string.Equals(normalizedEntryThreadId, completedThreadId, StringComparison.Ordinal))
        {
            return false;
        }

        var normalizedCompletedTurnId = Normalize(completedTurnId);
        var normalizedEntryTurnId = Normalize(entryTurnId);
        if (string.IsNullOrWhiteSpace(normalizedCompletedTurnId))
        {
            return string.IsNullOrWhiteSpace(normalizedEntryTurnId);
        }

        return string.IsNullOrWhiteSpace(normalizedEntryTurnId)
               || string.Equals(normalizedEntryTurnId, normalizedCompletedTurnId, StringComparison.Ordinal);
    }

    private static string ResolveTurnCompletionErrorMessage(string? status)
    {
        var normalizedStatus = Normalize(status);
        if (string.Equals(normalizedStatus, "interrupted", StringComparison.OrdinalIgnoreCase))
        {
            return "回合已中断。";
        }

        if (string.Equals(normalizedStatus, "failed", StringComparison.OrdinalIgnoreCase))
        {
            return "回合执行失败。";
        }

        return string.IsNullOrWhiteSpace(normalizedStatus)
            ? "回合未成功完成。"
            : $"回合未成功完成，当前状态：{normalizedStatus}。";
    }

    private static ControlPlaneThreadTurnError? BuildTurnError(AppServerErrorDto? error, string? fallbackMessage = null)
    {
        var message = Normalize(error?.Message) ?? Normalize(fallbackMessage);
        var additionalDetails = Normalize(error?.AdditionalDetails);
        if (string.IsNullOrWhiteSpace(message)
            && string.IsNullOrWhiteSpace(additionalDetails))
        {
            return null;
        }

        return new ControlPlaneThreadTurnError
        {
            Message = message,
            AdditionalDetails = additionalDetails,
        };
    }

    private void HandleRawResponseItemCompletedNotification(AppServerNotificationEnvelope notification, RawResponseItemCompletedParamsDto parameters)
    {
        var projection = providerNotificationInterpreter.InterpretRawResponseItemCompleted(
            ToProviderRawResponseItemCompletedNotification(notification, parameters));
        if (projection is null)
        {
            return;
        }

        var threadId = ResolveNotificationThreadId(notification, parameters.ThreadId);
        var rawTurnId = parameters.TurnId
                        ?? TryResolveCompletedTurnIdFromObservedTurns(threadId)
                        ?? ResolveNotificationTurnId(threadId, notification, parameters.TurnId);
        var completedText = parameters.Item?.Text ?? parameters.Item?.OutputText;
        var text = PeekTurnText(rawTurnId);
        if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(completedText))
        {
            SetTurnText(rawTurnId, completedText);
            text = completedText;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var providerEvents = projection.CreateEvents(new ProviderNotificationProjectionInput(text));
        if (providerEvents.Count == 0)
        {
            return;
        }

        RaiseProviderProjectionEvents(
            new ProviderEventProjectionContext(
                ThreadId: threadId,
                TurnId: rawTurnId,
                ItemId: projection.ItemId,
                Status: projection.Status,
                Phase: projection.Phase,
                SourceMethod: notification.Method,
                RawJson: notification.RawJson),
            providerEvents);
    }

    private void HandleErrorNotification(AppServerNotificationEnvelope notification, ErrorNotificationParamsDto parameters)
    {
        var projection = providerNotificationInterpreter.InterpretError(
            ToProviderErrorNotification(notification, parameters));
        if (projection is null)
        {
            return;
        }

        var providerEvents = projection.CreateEvents();
        var failureEvent = providerEvents.OfType<ProviderFailureEvent>().LastOrDefault();
        if (failureEvent is null)
        {
            return;
        }

        var threadId = ResolveNotificationThreadId(notification, parameters.ThreadId);
        var errorTurnId = parameters.TurnId
                          ?? TryResolveCompletedTurnIdFromObservedTurns(threadId)
                          ?? ResolveNotificationTurnId(threadId, notification, parameters.TurnId);
        var failure = failureEvent.Failure;
        if (!failure.IsRetryable)
        {
            CompleteTurn(errorTurnId, false, "error", null, failure.Message, notification.RawJson);
            RemoveObservedTurnForThread(threadId, errorTurnId);
        }

        RaiseProviderProjectionEvents(
            new ProviderEventProjectionContext(
                ThreadId: threadId,
                TurnId: errorTurnId,
                Status: projection.Status,
                SourceMethod: notification.Method,
                RawJson: notification.RawJson),
            providerEvents);
    }

    private void HandleTurnSteeredNotification(AppServerNotificationEnvelope notification, TurnSteeredParamsDto parameters)
    {
        var threadId = ResolveNotificationThreadId(notification, parameters.ThreadId);
        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.TurnSteered,
            ThreadId = ToThreadId(threadId),
            TurnId = ToTurnId(ResolveStructuredNotificationTurnId(threadId, notification, parameters.TurnId)),
            Status = parameters.Status ?? "accepted",
            Text = parameters.Source,
            Source = parameters.Source,
            SourceMethod = notification.Method,
            Message = notification.Method,
            Diagnostics = BuildStreamDiagnostics(notification.RawJson, metadataJson: notification.RawJson),
        });
    }

    private void HandleMcpServerStatusUpdatedNotification(AppServerNotificationEnvelope notification, McpServerStatusListUpdatedParamsDto parameters)
    {
        var threadId = ResolveExplicitNotificationThreadId(notification, parameters.ThreadId);
        var payload = BuildMcpServerStatusPayload(parameters);
        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.McpServerStatusUpdated,
            ThreadId = ToThreadId(threadId),
            TurnId = ToTurnId(ResolveExplicitNotificationTurnId(notification, parameters.TurnId)),
            Text = BuildMcpServerStatusSummary(notification.Params),
            SourceMethod = notification.Method,
            Message = notification.Method,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.McpServerStatus,
            Payload = ToStructuredPayload(payload),
            Diagnostics = BuildStreamDiagnostics(
                notification.RawJson,
                dataJson: notification.Params.TryGetProperty("data", out var data) ? data.GetRawText() : null,
                metadataJson: notification.RawJson),
        });
    }

    private void HandleThreadArchivedNotification(AppServerNotificationEnvelope notification)
    {
        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.Info,
            ThreadId = ToThreadId(ResolveExplicitNotificationThreadId(notification)),
            TurnId = ToTurnId(ResolveExplicitNotificationTurnId(notification)),
            Text = ResolveInformationalText(notification.Method, notification.Params),
            Message = notification.Method,
            Diagnostics = BuildStreamDiagnostics(notification.RawJson),
        });
    }

    private void HandleThreadUnarchivedNotification(AppServerNotificationEnvelope notification)
    {
        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.Info,
            ThreadId = ToThreadId(ResolveExplicitNotificationThreadId(notification)),
            TurnId = ToTurnId(ResolveExplicitNotificationTurnId(notification)),
            Text = ResolveInformationalText(notification.Method, notification.Params),
            Message = notification.Method,
            Diagnostics = BuildStreamDiagnostics(notification.RawJson),
        });
    }

    private void HandleHookCompletedNotification(AppServerNotificationEnvelope notification, HookCompletedParamsDto parameters)
    {
        var threadId = ResolveNotificationThreadId(notification, parameters.ThreadId);
        var turnId = ResolveStructuredNotificationTurnId(threadId, notification, parameters.TurnId);
        var hookRun = BuildHookRunPayload(parameters.Run);
        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.HookCompleted,
            ThreadId = ToThreadId(threadId),
            TurnId = ToTurnId(turnId),
            Status = hookRun?.Status,
            Text = BuildHookSummaryText(hookRun),
            SourceMethod = notification.Method,
            Message = notification.Method,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.HookRun,
            Payload = ToStructuredPayload(hookRun),
            Diagnostics = BuildStreamDiagnostics(notification.RawJson, dataJson: notification.Params.GetRawText()),
        });
    }

    private void HandleModelReroutedNotification(AppServerNotificationEnvelope notification, ModelReroutedParamsDto parameters)
    {
        var threadId = ResolveNotificationThreadId(notification, parameters.ThreadId);
        var payload = BuildModelReroutedPayload(parameters);
        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.ModelRerouted,
            ThreadId = ToThreadId(threadId),
            TurnId = ToTurnId(ResolveStructuredNotificationTurnId(threadId, notification, parameters.TurnId)),
            Status = payload.Reason,
            Text = BuildModelReroutedText(payload),
            SourceMethod = notification.Method,
            Message = notification.Method,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.ModelRerouted,
            Payload = ToStructuredPayload(payload),
            Diagnostics = BuildStreamDiagnostics(notification.RawJson, dataJson: notification.Params.GetRawText()),
        });
    }

    private void HandleThreadStatusChangedNotification(AppServerNotificationEnvelope notification, ThreadStatusChangedParamsDto parameters)
    {
        var threadId = ResolveExplicitNotificationThreadId(notification, parameters.ThreadId);
        var payload = BuildThreadStatusChangedPayload(parameters.Status);
        CompleteInterruptedTurnWhenThreadBecomesInactive(threadId, payload.Type, notification.RawJson);
        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.ThreadStatusChanged,
            ThreadId = ToThreadId(threadId),
            TurnId = ToTurnId(ResolveExplicitNotificationTurnId(notification, parameters.TurnId)),
            Status = payload.Type,
            Text = payload.Type,
            Message = notification.Method,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.ThreadStatusChanged,
            Payload = ToStructuredPayload(payload),
            Diagnostics = BuildStreamDiagnostics(notification.RawJson),
        });
    }

    private void HandleThreadNameUpdatedNotification(AppServerNotificationEnvelope notification, ThreadNameUpdatedParamsDto parameters)
    {
        var threadName = Normalize(parameters.ThreadName) ?? Normalize(parameters.Name);
        var payload = new ThreadNameUpdatedPayload(threadName);
        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.ThreadNameUpdated,
            ThreadId = ToThreadId(ResolveExplicitNotificationThreadId(notification, parameters.ThreadId)),
            TurnId = ToTurnId(ResolveExplicitNotificationTurnId(notification, parameters.TurnId)),
            Text = threadName,
            Message = notification.Method,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.ThreadNameUpdated,
            Payload = ToStructuredPayload(payload),
            Diagnostics = BuildStreamDiagnostics(notification.RawJson),
        });
    }

    private void HandleThreadTokenUsageUpdatedNotification(AppServerNotificationEnvelope notification, ThreadTokenUsageUpdatedParamsDto parameters)
    {
        var payload = BuildThreadTokenUsagePayload(parameters.TokenUsage);
        if (payload is null)
        {
            return;
        }

        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.ThreadTokenUsageUpdated,
            ThreadId = ToThreadId(ResolveExplicitNotificationThreadId(notification, parameters.ThreadId)),
            TurnId = ToTurnId(ResolveExplicitNotificationTurnId(notification, parameters.TurnId)),
            Text = $"tokens total={payload.Total.TotalTokens}",
            Message = notification.Method,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.ThreadTokenUsage,
            Payload = ToStructuredPayload(payload),
            Diagnostics = BuildStreamDiagnostics(notification.RawJson),
        });
    }

    private void HandleThreadCompactedNotification(AppServerNotificationEnvelope notification)
    {
        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.ThreadCompacted,
            ThreadId = ToThreadId(ResolveExplicitNotificationThreadId(notification)),
            TurnId = ToTurnId(ResolveExplicitNotificationTurnId(notification)),
            Text = ResolveInformationalText(notification.Method, notification.Params) ?? notification.Method,
            Message = notification.Method,
            Diagnostics = BuildStreamDiagnostics(notification.RawJson),
        });
    }

    private void HandleSkillsChangedNotification(AppServerNotificationEnvelope notification)
    {
        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.SkillsChanged,
            Text = ResolveInformationalText(notification.Method, notification.Params) ?? notification.Method,
            Message = notification.Method,
            Diagnostics = BuildStreamDiagnostics(notification.RawJson),
        });
    }

    private void HandleCommandExecOutputDeltaNotification(AppServerNotificationEnvelope notification, CommandExecOutputDeltaParamsDto parameters)
    {
        var processId = Normalize(parameters.ProcessId) ?? string.Empty;
        var stream = Normalize(parameters.Stream) ?? string.Empty;
        var deltaBase64 = Normalize(parameters.DeltaBase64) ?? string.Empty;
        var payload = new CommandExecOutputDeltaPayload(
            processId,
            stream,
            deltaBase64,
            parameters.CapReached ?? false);
        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.CommandExecOutputDelta,
            CallId = ToCallId(processId),
            Status = stream,
            Text = string.IsNullOrWhiteSpace(stream) ? processId : $"{stream} output",
            Message = notification.Method,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.CommandExecOutputDelta,
            Payload = ToStructuredPayload(payload),
            Diagnostics = BuildStreamDiagnostics(notification.RawJson),
        });
    }

    private void HandleAppListUpdatedNotification(AppServerNotificationEnvelope notification, AppListUpdatedParamsDto parameters)
    {
        var items = BuildAppListUpdatedPayload(parameters);
        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.AppListUpdated,
            Text = $"apps updated: {items.Count}",
            Message = notification.Method,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.AppListUpdated,
            Payload = ToStructuredPayload(new AppListUpdatedPayload(items)),
            Diagnostics = BuildStreamDiagnostics(notification.RawJson),
        });
    }

    private void HandleConfigWarningNotification(AppServerNotificationEnvelope notification, ConfigWarningParamsDto parameters)
    {
        var summary = Normalize(parameters.Summary)
            ?? ResolveInformationalText(notification.Method, notification.Params)
            ?? notification.Method;
        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.ConfigWarning,
            Text = summary,
            Message = notification.Method,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.ConfigWarning,
            Payload = ToStructuredPayload(new ConfigWarningPayload(
                summary,
                Normalize(parameters.Details),
                Normalize(parameters.Path),
                BuildConfigRangePayload(parameters.Range))),
            Diagnostics = BuildStreamDiagnostics(notification.RawJson),
        });
    }

    private void HandleDeprecationNoticeNotification(AppServerNotificationEnvelope notification, DeprecationNoticeParamsDto parameters)
    {
        var summary = Normalize(parameters.Summary)
            ?? ResolveInformationalText(notification.Method, notification.Params)
            ?? notification.Method;
        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.DeprecationNotice,
            Text = summary,
            Message = notification.Method,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.DeprecationNotice,
            Payload = ToStructuredPayload(new DeprecationNoticePayload(summary, Normalize(parameters.Details))),
            Diagnostics = BuildStreamDiagnostics(notification.RawJson),
        });
    }

    private void HandleWindowsSandboxSetupCompletedNotification(AppServerNotificationEnvelope notification, WindowsSandboxSetupCompletedParamsDto parameters)
    {
        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.Info,
            Text = ResolveInformationalText(notification.Method, notification.Params),
            Message = notification.Method,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.WindowsSandboxSetup,
            Payload = ToStructuredPayload(new WindowsSandboxSetupPayload(parameters.Mode, parameters.Success, parameters.Error)),
            Diagnostics = BuildStreamDiagnostics(notification.RawJson),
        });
    }

    private void HandleMcpServerOauthLoginCompletedNotification(AppServerNotificationEnvelope notification, McpServerOauthLoginCompletedParamsDto parameters)
    {
        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.Info,
            Text = ResolveInformationalText(notification.Method, notification.Params),
            Message = notification.Method,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.McpServerOauthLogin,
            Payload = ToStructuredPayload(new McpServerOauthLoginPayload(parameters.Name, parameters.Success, parameters.Error)),
            Diagnostics = BuildStreamDiagnostics(notification.RawJson),
        });
    }

    private void HandleThreadRealtimeStartedNotification(AppServerNotificationEnvelope notification, ThreadRealtimeStartedParamsDto parameters)
    {
        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.Info,
            ThreadId = ToThreadId(ResolveNotificationThreadId(notification, parameters.ThreadId)),
            Text = ResolveInformationalText(notification.Method, notification.Params),
            Message = notification.Method,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.RealtimeSession,
            Payload = ToStructuredPayload(new RealtimeSessionPayload(parameters.ThreadId, parameters.SessionId)),
            Diagnostics = BuildStreamDiagnostics(notification.RawJson),
        });
    }

    private void HandleFuzzyFileSearchSessionUpdatedNotification(AppServerNotificationEnvelope notification, FuzzyFileSearchSessionUpdatedParamsDto parameters)
    {
        var files = parameters.Files?.Select(static file => new FuzzyFileSearchFilePayload(file.Path, file.FileName)).ToArray()
            ?? Array.Empty<FuzzyFileSearchFilePayload>();
        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.Info,
            Text = ResolveInformationalText(notification.Method, notification.Params),
            Message = notification.Method,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.FuzzyFileSearchSession,
            Payload = ToStructuredPayload(new FuzzyFileSearchSessionPayload(parameters.SessionId, files, IsCompleted: false)),
            Diagnostics = BuildStreamDiagnostics(notification.RawJson),
        });
    }

    private void HandleFuzzyFileSearchSessionCompletedNotification(AppServerNotificationEnvelope notification, FuzzyFileSearchSessionCompletedParamsDto parameters)
    {
        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.Info,
            Text = ResolveInformationalText(notification.Method, notification.Params),
            Message = notification.Method,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.FuzzyFileSearchSession,
            Payload = ToStructuredPayload(new FuzzyFileSearchSessionPayload(parameters.SessionId, Array.Empty<FuzzyFileSearchFilePayload>(), IsCompleted: true)),
            Diagnostics = BuildStreamDiagnostics(notification.RawJson),
        });
    }

    private void HandleThreadRealtimeItemAddedNotification(AppServerNotificationEnvelope notification, ThreadRealtimeItemAddedParamsDto parameters)
    {
        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.ThreadRealtimeItemAdded,
            ThreadId = ToThreadId(ResolveNotificationThreadId(notification, parameters.ThreadId)),
            Text = BuildRealtimeItemText(parameters.Item),
            Message = notification.Method,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.ThreadRealtimeItemAdded,
            Payload = ToStructuredPayload(BuildThreadRealtimeItemAddedPayload(parameters.Item)),
            Diagnostics = BuildStreamDiagnostics(notification.RawJson),
        });
    }

    private void HandleThreadRealtimeOutputAudioDeltaNotification(AppServerNotificationEnvelope notification, ThreadRealtimeOutputAudioDeltaParamsDto parameters)
    {
        var audio = parameters.Audio;
        if (audio is null)
        {
            return;
        }

        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.ThreadRealtimeOutputAudioDelta,
            ThreadId = ToThreadId(ResolveNotificationThreadId(notification, parameters.ThreadId)),
            Message = notification.Method,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.ThreadRealtimeOutputAudioDelta,
            Payload = ToStructuredPayload(new ThreadRealtimeOutputAudioDeltaPayload(
                Normalize(audio.Data) ?? string.Empty,
                audio.SampleRate ?? 0,
                audio.NumChannels ?? 0,
                audio.SamplesPerChannel)),
            Diagnostics = BuildStreamDiagnostics(notification.RawJson),
        });
    }

    private void HandleThreadRealtimeErrorNotification(AppServerNotificationEnvelope notification, ThreadRealtimeErrorParamsDto parameters)
    {
        var message = Normalize(parameters.Message) ?? "realtime error";
        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.ThreadRealtimeError,
            ThreadId = ToThreadId(ResolveNotificationThreadId(notification, parameters.ThreadId)),
            Text = message,
            Message = notification.Method,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.ThreadRealtimeError,
            Payload = ToStructuredPayload(new ThreadRealtimeErrorPayload(message)),
            Diagnostics = BuildStreamDiagnostics(notification.RawJson),
        });
    }

    private void HandleThreadRealtimeClosedNotification(AppServerNotificationEnvelope notification, ThreadRealtimeClosedParamsDto parameters)
    {
        var reason = Normalize(parameters.Reason);
        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.ThreadRealtimeClosed,
            ThreadId = ToThreadId(ResolveNotificationThreadId(notification, parameters.ThreadId)),
            Text = reason,
            Message = notification.Method,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.ThreadRealtimeClosed,
            Payload = ToStructuredPayload(new ThreadRealtimeClosedPayload(reason)),
            Diagnostics = BuildStreamDiagnostics(notification.RawJson),
        });
    }

    private bool HandleTypedItemNotification(AppServerNotificationEnvelope notification, ItemNotificationParamsDto parameters)
    {
        var threadId = ResolveNotificationThreadId(notification, parameters.ThreadId);
        var turnId = ResolveTypedItemTurnId(threadId, notification, parameters.TurnId);
        var method = notification.Method;
        var item = parameters.Item;
        var itemId = ResolveItemId(parameters);
        var itemType = ResolveItemType(parameters);
        var phase = method[(method.LastIndexOf('/') + 1)..];
        var eventPhase = ResolveItemPhase(parameters);
        var itemStatus = ResolveItemStatus(parameters);
        var delta = ResolveItemDelta(parameters);
        if (!string.IsNullOrWhiteSpace(delta) && IsCommentaryDelta(method, itemType))
        {
            if (TryParseAgentJobProgressPayload(delta, out var agentJobProgress))
            {
                RaiseEvent(new ControlPlaneConversationStreamEvent
                {
                    Kind = ControlPlaneConversationStreamEventKind.AgentJobProgress,
                    ThreadId = ToThreadId(threadId),
                    TurnId = ToTurnId(turnId),
                    ItemId = itemId,
                    Status = itemType,
                    Phase = eventPhase ?? "commentary",
                    Text = delta,
                    SourceMethod = method,
                    PayloadKind = ControlPlaneConversationStreamPayloadKind.AgentJobProgress,
                    Payload = ToStructuredPayload(agentJobProgress),
                    Diagnostics = BuildStreamDiagnostics(notification.RawJson),
                });
                return true;
            }
        }

        var reasoningText = ResolveReasoningText(parameters);
        var isReasoningSummaryPartAdded = string.Equals(method, "item/reasoning/summaryPartAdded", StringComparison.Ordinal);
        var providerProjection = providerNotificationInterpreter.InterpretItem(
            ToProviderItemNotification(notification, parameters));
        if (providerProjection is not null)
        {
            var providerEvents = providerProjection.CreateEvents();
            var assistantTextDelta = providerEvents.OfType<ProviderTextDeltaEvent>().LastOrDefault();
            if (assistantTextDelta is not null)
            {
                AppendTurnText(turnId, assistantTextDelta.TextDelta);
            }

            var approvalDirective = providerEvents
                .OfType<ProviderToolDirectiveEvent>()
                .LastOrDefault(static item => item.Directive.RequiresApproval);
            if (approvalDirective is not null
                && !string.IsNullOrWhiteSpace(providerProjection.CallId))
            {
                pendingApprovals[providerProjection.CallId] = new ApprovalContext(
                    threadId ?? string.Empty,
                    turnId ?? string.Empty,
                    providerProjection.CallId);
            }

            RaiseProviderProjectionEvents(
                new ProviderEventProjectionContext(
                    ThreadId: threadId,
                    TurnId: turnId,
                    ItemId: providerProjection.ItemId,
                    CallId: providerProjection.CallId,
                    ToolName: providerProjection.ToolName,
                    ServerName: providerProjection.ServerName,
                    Status: providerProjection.Status,
                    Phase: providerProjection.Phase,
                    SourceMethod: method,
                    RawJson: notification.RawJson,
                    SummaryIndex: providerProjection.SummaryIndex,
                    ContentIndex: providerProjection.ContentIndex),
                providerEvents);
            return true;
        }

        if (IsReasoningDelta(method, itemType))
        {
            if (isReasoningSummaryPartAdded)
            {
                RaiseEvent(new ControlPlaneConversationStreamEvent
                {
                    Kind = ControlPlaneConversationStreamEventKind.ReasoningDelta,
                    ThreadId = ToThreadId(threadId),
                    TurnId = ToTurnId(turnId),
                    ItemId = itemId,
                    Status = itemStatus ?? itemType ?? "reasoning",
                    Phase = eventPhase ?? "reasoning",
                    Text = reasoningText,
                    SourceMethod = method,
                    PayloadKind = ControlPlaneConversationStreamPayloadKind.Reasoning,
                    Payload = ToStructuredPayload(BuildReasoningEventPayload(
                        itemId,
                        itemStatus ?? itemType ?? "reasoning",
                        eventPhase ?? "reasoning",
                        reasoningText,
                        method,
                        parameters.SummaryIndex,
                        parameters.ContentIndex)),
                    Diagnostics = BuildStreamDiagnostics(notification.RawJson),
                });
            }

            return true;
        }

        if (string.Equals(method, "item/started", StringComparison.Ordinal)
            || string.Equals(method, "item/completed", StringComparison.Ordinal))
        {
            var itemPayload = BuildItemEventPayload(
                itemId,
                itemType,
                itemStatus,
                eventPhase,
                item,
                ResolveItemToolName(parameters),
                ResolveItemCallId(parameters),
                ResolveItemArguments(parameters),
                ResolveItemOutput(parameters));
            var itemEventKind = string.Equals(method, "item/completed", StringComparison.Ordinal)
                ? ControlPlaneConversationStreamEventKind.ItemCompleted
                : ControlPlaneConversationStreamEventKind.ItemStarted;

            RaiseEvent(new ControlPlaneConversationStreamEvent
            {
                Kind = itemEventKind,
                ThreadId = ToThreadId(threadId),
                TurnId = ToTurnId(turnId),
                ItemId = itemId,
                Status = itemType ?? phase,
                Phase = eventPhase,
                Text = itemPayload.Text,
                SourceMethod = method,
                PayloadKind = ControlPlaneConversationStreamPayloadKind.Item,
                Payload = ToItemStructuredPayload(notification, itemPayload),
                Message = method,
            });

            if (string.Equals(method, "item/completed", StringComparison.Ordinal)
                && IsUserMessageItem(itemType)
                && !string.IsNullOrWhiteSpace(itemPayload.Text))
            {
                var followUpCorrelationId = TryConsumePendingFollowUpCorrelation(
                    itemPayload.Inputs,
                    turnId,
                    threadId);
                RaiseEvent(new ControlPlaneConversationStreamEvent
                {
                    Kind = ControlPlaneConversationStreamEventKind.UserMessageCommitted,
                    ThreadId = ToThreadId(threadId),
                    TurnId = ToTurnId(turnId),
                    ItemId = itemId,
                    Status = itemType ?? phase,
                    Phase = eventPhase,
                    Text = itemPayload.Text,
                    SourceMethod = method,
                    PayloadKind = ControlPlaneConversationStreamPayloadKind.CommittedUserMessage,
                    Payload = ToStructuredPayload(new CommittedUserMessagePayload(
                        itemPayload.ItemId,
                        itemPayload.Text!,
                        itemPayload.ImageCount,
                        followUpCorrelationId,
                        itemPayload.Inputs)),
                    Message = method,
                });
            }

            return true;
        }

        if (string.Equals(method, "item/plan/delta", StringComparison.Ordinal))
        {
            var planPayload = BuildPlanEventPayload(delta);
            RaiseEvent(new ControlPlaneConversationStreamEvent
            {
                Kind = ControlPlaneConversationStreamEventKind.PlanUpdated,
                ThreadId = ToThreadId(threadId),
                TurnId = ToTurnId(turnId),
                ItemId = itemId,
                ToolName = "plan",
                Text = delta,
                Status = phase,
                Phase = eventPhase,
                SourceMethod = method,
                PayloadKind = ControlPlaneConversationStreamPayloadKind.Plan,
                Payload = ToStructuredPayload(planPayload),
                Message = method,
                Diagnostics = new ControlPlaneConversationStreamDiagnostics
                {
                    DataJson = notification.Params.GetRawText(),
                    MetadataJson = notification.RawJson,
                    RawJson = notification.RawJson,
                },
            });
            return true;
        }

        return false;
    }

    private void RaiseRawNotification(string? threadId, string? turnId, string method, string rawJson)
    {
        RaiseEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.RawNotification,
            ThreadId = ToThreadId(threadId),
            TurnId = ToTurnId(turnId),
            Message = method,
            Diagnostics = BuildStreamDiagnostics(rawJson),
        });
    }

    private static ThreadId? ToThreadId(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : new ThreadId(value);

    private static TurnId? ToTurnId(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : new TurnId(value);

    private static CallId? ToCallId(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : new CallId(value);

    private static StructuredValue? ToStructuredPayload(object? value)
        => value is null
            ? null
            : StructuredValue.FromJsonElement(JsonSerializer.SerializeToElement(value, StructuredJsonOptions));

    private static StructuredValue? ToItemStructuredPayload(AppServerNotificationEnvelope notification, ItemEventPayload itemPayload)
    {
        var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (notification.Params.ValueKind == JsonValueKind.Object
            && notification.Params.TryGetProperty("item", out var item)
            && item.ValueKind == JsonValueKind.Object)
        {
            CopyJsonObjectProperties(item, merged, overwrite: false);
        }

        var payloadElement = JsonSerializer.SerializeToElement(itemPayload, StructuredJsonOptions);
        CopyJsonObjectProperties(payloadElement, merged, overwrite: true);
        return StructuredValue.FromJsonElement(JsonSerializer.SerializeToElement(merged, StructuredJsonOptions));
    }

    private static void CopyJsonObjectProperties(JsonElement source, IDictionary<string, object?> target, bool overwrite)
    {
        if (source.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in source.EnumerateObject())
        {
            if (!overwrite && target.ContainsKey(property.Name))
            {
                continue;
            }

            target[property.Name] = JsonSerializer.Deserialize<object?>(property.Value.GetRawText(), StructuredJsonOptions);
        }
    }

    private static ControlPlaneConversationStreamDiagnostics? BuildStreamDiagnostics(
        string? rawJson,
        string? dataJson = null,
        string? metadataJson = null)
        => string.IsNullOrWhiteSpace(rawJson)
           && string.IsNullOrWhiteSpace(dataJson)
           && string.IsNullOrWhiteSpace(metadataJson)
            ? null
            : new ControlPlaneConversationStreamDiagnostics
            {
                DataJson = dataJson,
                MetadataJson = metadataJson,
                RawJson = rawJson,
            };
    private static bool IsReasoningDelta(string method, string? itemType)
    {
        if (string.Equals(method, "item/reasoning/summaryTextDelta", StringComparison.Ordinal)
            || string.Equals(method, "item/reasoning/textDelta", StringComparison.Ordinal)
            || string.Equals(method, "item/reasoning/summaryPartAdded", StringComparison.Ordinal))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(itemType)
               && itemType.Contains("reasoning", StringComparison.OrdinalIgnoreCase)
               && string.Equals(method, "item/delta", StringComparison.Ordinal);
    }

    private static bool TryParseAgentJobProgressPayload(string? delta, out AgentJobProgressPayload? payload)
    {
        payload = null;
        var normalizedDelta = Normalize(delta);
        if (string.IsNullOrWhiteSpace(normalizedDelta)
            || !normalizedDelta.StartsWith(AgentJobProgressPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var progressJson = normalizedDelta[AgentJobProgressPrefix.Length..];
        try
        {
            using var document = JsonDocument.Parse(progressJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var root = document.RootElement;
            var jobId = ReadString(root, "job_id");
            if (string.IsNullOrWhiteSpace(jobId))
            {
                return false;
            }

            payload = new AgentJobProgressPayload(
                JobId: jobId,
                TotalItems: ReadInt32(root, "total_items") ?? 0,
                PendingItems: ReadInt32(root, "pending_items") ?? 0,
                RunningItems: ReadInt32(root, "running_items") ?? 0,
                CompletedItems: ReadInt32(root, "completed_items") ?? 0,
                FailedItems: ReadInt32(root, "failed_items") ?? 0,
                EtaSeconds: ReadInt32(root, "eta_seconds"));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsAssistantTextDelta(string method, string? itemType)
    {
        if (!string.Equals(method, "item/delta", StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(itemType))
        {
            return false;
        }

        return !itemType.Contains("reasoning", StringComparison.OrdinalIgnoreCase)
               && (itemType.Contains("assistant", StringComparison.OrdinalIgnoreCase)
                   || itemType.Contains("agent", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCommentaryDelta(string method, string? itemType)
        => string.Equals(method, "item/agentMessage/delta", StringComparison.Ordinal)
           && IsAssistantCompletionItem(itemType);

    private static bool IsAssistantCompletionItem(string? itemType)
    {
        if (string.IsNullOrWhiteSpace(itemType))
        {
            return false;
        }

        return itemType.Contains("assistant", StringComparison.OrdinalIgnoreCase)
               || string.Equals(itemType, "agentMessage", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUserMessageItem(string? itemType)
    {
        var normalizedItemType = Normalize(itemType)?.ToLowerInvariant();
        return normalizedItemType is "usermessage" or "user_message";
    }

    private static ItemEventPayload BuildItemEventPayload(
        string? itemId,
        string? itemType,
        string? itemStatus,
        string? phase,
        AppServerResponseItemDto? item,
        string? toolName = null,
        string? callId = null,
        string? arguments = null,
        string? outputText = null)
    {
        var text = item is null
            ? null
            : ExtractResponseItemConversationText(
                item,
                preferTextProperty: !IsUserMessageItem(itemType));
        var imageCount = item is null ? 0 : CountResponseItemImages(item);
        var inputs = item is null ? Array.Empty<AgentUserInput>() : ExtractResponseItemUserInputs(item);
        return new ItemEventPayload(
            itemId ?? item?.Id,
            itemType ?? item?.Type,
            itemStatus ?? item?.Status,
            phase ?? item?.Phase,
            text,
            imageCount,
            toolName ?? item?.ToolName ?? item?.Name,
            callId ?? item?.CallId,
            arguments ?? item?.Arguments ?? item?.Input,
            outputText ?? item?.OutputText ?? item?.Output,
            item?.Input,
            inputs);
    }

    private static string? ExtractResponseItemConversationText(AppServerResponseItemDto item, bool preferTextProperty)
    {
        var directText = Normalize(item.Text);
        if (preferTextProperty && !string.IsNullOrWhiteSpace(directText))
        {
            return directText;
        }

        if (item.Content is { } content && content.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            var contentText = content.ValueKind switch
            {
                JsonValueKind.String => Normalize(content.GetString()),
                JsonValueKind.Array => ExtractContentPartsText(content),
                JsonValueKind.Object => ExtractContentObjectText(content),
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(contentText))
            {
                return contentText;
            }
        }

        return preferTextProperty ? null : directText;
    }

    private static int CountResponseItemImages(AppServerResponseItemDto item)
    {
        if (item.Content is not { } content || content.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return 0;
        }

        return CountContentImages(content);
    }

    private static IReadOnlyList<AgentUserInput> ExtractResponseItemUserInputs(AppServerResponseItemDto item)
    {
        if (item.Content is not { } content || content.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<AgentUserInput>();
        }

        var inputs = new List<AgentUserInput>();
        foreach (var contentItem in content.EnumerateArray())
        {
            if (contentItem.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var parsed = ParseThreadUserInput(contentItem);
            if (parsed is not null)
            {
                inputs.Add(parsed);
            }
        }

        return inputs;
    }

    private static int CountContentImages(JsonElement content)
    {
        return content.ValueKind switch
        {
            JsonValueKind.Array => content.EnumerateArray().Sum(CountContentImages),
            JsonValueKind.Object => CountContentObjectImages(content),
            _ => 0,
        };
    }

    private static int CountContentObjectImages(JsonElement content)
    {
        var partType = NormalizeUserInputType(ReadString(content, "type"));
        return partType switch
        {
            "image" or "localimage" => 1,
            _ => 0,
        };
    }

    private async Task<TurnCompletion?> WaitTurnCompletionAsync(string turnId, CancellationToken cancellationToken)
    {
        if (completedTurns.TryRemove(turnId, out var completed))
        {
            return completed;
        }

        var tcs = pendingTurns.GetOrAdd(turnId, static _ => new TaskCompletionSource<TurnCompletion>(TaskCreationOptions.RunContinuationsAsynchronously));
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(options.TurnTimeout);

        try
        {
            return await tcs.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            pendingTurns.TryRemove(turnId, out _);
        }
    }

    private void CompleteTurn(string? turnId, bool success, string? status, string? assistantText, string? errorMessage, string? rawJson)
    {
        var resolvedTurnId = Normalize(turnId) ?? activeTurnId;
        if (string.IsNullOrWhiteSpace(resolvedTurnId))
        {
            return;
        }

        interruptRequestedTurns.TryRemove(resolvedTurnId, out _);
        TrackTerminalTurnStatus(resolvedTurnId, status);
        var completion = new TurnCompletion(success, status, assistantText, errorMessage, rawJson);
        if (pendingTurns.TryRemove(resolvedTurnId, out var pending))
        {
            pending.TrySetResult(completion);
        }
        else
        {
            completedTurns[resolvedTurnId] = completion;
        }

        turnTextBuffers.TryRemove(resolvedTurnId, out _);
        if (string.Equals(activeTurnId, resolvedTurnId, StringComparison.Ordinal))
        {
            activeTurnId = null;
        }

        if (string.Equals(submittedTurnId, resolvedTurnId, StringComparison.Ordinal))
        {
            submittedTurnId = null;
        }
    }

    private void AppendTurnText(string? turnId, string delta)
    {
        var resolvedTurnId = Normalize(turnId) ?? activeTurnId;
        if (string.IsNullOrWhiteSpace(resolvedTurnId) || string.IsNullOrEmpty(delta))
        {
            return;
        }

        var builder = turnTextBuffers.GetOrAdd(resolvedTurnId, static _ => new StringBuilder());
        builder.Append(delta);
    }

    private void SetTurnText(string? turnId, string text)
    {
        var resolvedTurnId = Normalize(turnId) ?? activeTurnId;
        var normalizedText = Normalize(text);
        if (string.IsNullOrWhiteSpace(resolvedTurnId) || string.IsNullOrWhiteSpace(normalizedText))
        {
            return;
        }

        turnTextBuffers[resolvedTurnId] = new StringBuilder(normalizedText);
    }

    private string? PeekTurnText(string? turnId)
    {
        var resolvedTurnId = Normalize(turnId) ?? activeTurnId;
        if (string.IsNullOrWhiteSpace(resolvedTurnId))
        {
            return null;
        }

        return turnTextBuffers.TryGetValue(resolvedTurnId, out var builder)
            ? builder.ToString()
            : null;
    }

    private string? TakeTurnText(string? turnId)
    {
        var resolvedTurnId = Normalize(turnId) ?? activeTurnId;
        if (string.IsNullOrWhiteSpace(resolvedTurnId))
        {
            return null;
        }

        return turnTextBuffers.TryGetValue(resolvedTurnId, out var builder) ? Normalize(builder.ToString()) : null;
    }

    private static PlanEventPayload BuildPlanEventPayload(TurnPlanUpdatedParamsDto parameters, string? fallbackExplanation = null)
    {
        var explanation = Normalize(parameters.Explanation) ?? Normalize(fallbackExplanation);
        var steps = new List<PlanStepEventPayload>();

        if (parameters.Plan is { Count: > 0 })
        {
            var sequence = 0;
            foreach (var item in parameters.Plan)
            {
                var step = Normalize(item.Step) ?? Normalize(item.Text);
                if (string.IsNullOrWhiteSpace(step))
                {
                    continue;
                }

                sequence++;
                steps.Add(new PlanStepEventPayload(sequence, step, NormalizePlanStatus(item.Status)));
            }
        }

        return new PlanEventPayload(explanation, steps);
    }

    private static PlanEventPayload BuildPlanEventPayload(JsonElement parameters, string? fallbackExplanation = null)
    {
        var dto = AppServerJsonHelpers.Deserialize<TurnPlanUpdatedParamsDto>(parameters);
        return dto is null
            ? new PlanEventPayload(Normalize(fallbackExplanation), Array.Empty<PlanStepEventPayload>())
            : BuildPlanEventPayload(dto, fallbackExplanation);
    }

    private static PlanEventPayload BuildPlanEventPayload(string? explanation)
        => new(Normalize(explanation), Array.Empty<PlanStepEventPayload>());

    private static string NormalizePlanStatus(string? status)
    {
        return Normalize(status)?.ToLowerInvariant() switch
        {
            "inprogress" => "in_progress",
            "in_progress" => "in_progress",
            "completed" => "completed",
            "pending" => "pending",
            "doing" => "in_progress",
            "done" => "completed",
            "todo" => "pending",
            { Length: > 0 } value => value,
            _ => "pending",
        };
    }

    private static McpServerStatusPayload BuildMcpServerStatusPayload(McpServerStatusListUpdatedParamsDto parameters)
    {
        var servers = BuildMcpServerStatusEntries(parameters.Data);
        var count = parameters.Data?.Count ?? (servers.Count > 0 ? servers.Count : null);
        return new McpServerStatusPayload(count, servers);
    }

    private static McpServerStatusPayload BuildMcpServerStatusPayload(JsonElement parameters)
    {
        var dto = AppServerJsonHelpers.Deserialize<McpServerStatusListUpdatedParamsDto>(parameters);
        if (dto is not null)
        {
            return BuildMcpServerStatusPayload(dto);
        }

        var servers = parameters.TryGetProperty("data", out var dataElement)
            ? BuildMcpServerStatusEntries(dataElement)
            : Array.Empty<McpServerEntryPayload>();
        int? count = parameters.TryGetProperty("data", out var countElement)
            && countElement.ValueKind == JsonValueKind.Array
            ? countElement.GetArrayLength()
            : (servers.Count > 0 ? servers.Count : null);
        return new McpServerStatusPayload(count, servers);
    }

    private static IReadOnlyList<McpServerEntryPayload> BuildMcpServerStatusEntries(IReadOnlyList<McpServerStatusEntryDto>? items)
    {
        if (items is not { Count: > 0 })
        {
            return Array.Empty<McpServerEntryPayload>();
        }

        var servers = new List<McpServerEntryPayload>(items.Count);
        foreach (var item in items)
        {
            var name = Normalize(item.Name);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            servers.Add(new McpServerEntryPayload(
                name,
                Normalize(item.AuthStatus),
                item.Tools?.Count ?? 0,
                item.Resources?.Count ?? 0,
                item.ResourceTemplates?.Count ?? 0));
        }

        return servers;
    }

    private static IReadOnlyList<McpServerEntryPayload> BuildMcpServerStatusEntries(JsonElement dataElement)
    {
        if (dataElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<McpServerEntryPayload>();
        }

        var servers = new List<McpServerEntryPayload>();
        foreach (var item in dataElement.EnumerateArray())
        {
            var name = ReadString(item, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            servers.Add(new McpServerEntryPayload(
                name,
                ReadString(item, "authStatus"),
                CountObjectProperties(item, "tools"),
                CountArrayItems(item, "resources"),
                CountArrayItems(item, "resourceTemplates")));
        }

        return servers;
    }

    private static int CountObjectProperties(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        var count = 0;
        foreach (var _ in property.EnumerateObject())
        {
            count++;
        }

        return count;
    }

    private static int CountArrayItems(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        return property.GetArrayLength();
    }

    private static JsonElement CreateEmptyObjectElement()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private async Task ReadStderrLoopAsync(CancellationToken cancellationToken)
    {
        if (stderr is null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await stderr.ReadLineAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                break;
            }

            if (line is null)
            {
                break;
            }

            TraceRuntime("stderr", line);
            stderrBuffer.Enqueue(line);
            while (stderrBuffer.Count > 200)
            {
                stderrBuffer.TryDequeue(out _);
            }
        }
    }

    private void TraceRuntime(string channel, string message)
    {
        var path = tracePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var line = $"[{DateTimeOffset.Now:O}] [{channel}] {message}{Environment.NewLine}";
            lock (traceGate)
            {
                File.AppendAllText(path, line, Encoding.UTF8);
            }
        }
        catch
        {
            // trace 失败不能影响主链路。
        }
    }

    private async Task DisposeCoreAsync()
    {
        if (runtimeCts is not null)
        {
            try
            {
                await runtimeCts.CancelAsync().ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
        }

        activeThreadId = null;
        activeTurnId = null;
        submittedTurnId = null;

        if (stdin is not null)
        {
            await stdin.DisposeAsync().ConfigureAwait(false);
            stdin = null;
        }

        stdout?.Dispose();
        stdout = null;

        stderr?.Dispose();
        stderr = null;

        if (process is not null)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // ignore
            }

            process.Dispose();
            process = null;
        }

        if (stdoutLoop is not null)
        {
            try { await stdoutLoop.ConfigureAwait(false); } catch { }
            stdoutLoop = null;
        }

        if (stderrLoop is not null)
        {
            try { await stderrLoop.ConfigureAwait(false); } catch { }
            stderrLoop = null;
        }

        runtimeCts?.Dispose();
        runtimeCts = null;
        artifactRuntimeStoreSet.Dispose();
        artifactRuntimeStoreSet = CreateDefaultArtifactRuntimeStoreSet();

        foreach (var entry in pendingResponses)
        {
            entry.Value.TrySetCanceled();
        }

        foreach (var entry in pendingTurns)
        {
            entry.Value.TrySetCanceled();
        }

        pendingResponses.Clear();
        pendingTurns.Clear();
        completedTurns.Clear();
        terminalTurnStatuses.Clear();
        turnTextBuffers.Clear();
        pendingApprovals.Clear();
        foreach (var pendingApprovalRequest in pendingApprovalRequests.Values)
        {
            pendingApprovalRequest.TrySetCanceled();
        }

        pendingApprovalRequests.Clear();
        foreach (var pendingPermissionRequest in pendingPermissionRequests.Values)
        {
            pendingPermissionRequest.TrySetCanceled();
        }

        pendingPermissionRequests.Clear();
        foreach (var pendingRequest in pendingUserInputRequests.Values)
        {
            pendingRequest.TrySetCanceled();
        }

        pendingUserInputRequests.Clear();
        pendingInteractiveServerRequestsByRequestId.Clear();
        pendingInteractiveServerRequestsByRequestToken.Clear();
        pendingInteractiveServerRequestIdsByCallId.Clear();
        pendingInteractiveReplayContextsByCallId.Clear();
        pendingFollowUpCommitsByThread.Clear();
        pendingFollowUpDispatchCancellationsByCorrelation.Clear();
        pendingFollowUpDispatchMutationReasonsByCorrelation.Clear();
        pendingInputPersistenceVersionsByThread.Clear();
        lock (pendingInputStateGate)
        {
            pendingInputStatesByThread.Clear();
        }
        lock (observedTurnStateGate)
        {
            observedTurnIdsByThread.Clear();
        }
        stderrBuffer.Clear();
    }

    private void TrackTerminalTurnStatus(string? turnId, string? status)
    {
        var normalizedTurnId = Normalize(turnId);
        if (string.IsNullOrWhiteSpace(normalizedTurnId))
        {
            return;
        }

        var normalizedStatus = Normalize(status) ?? string.Empty;
        if (ShouldSuppressTerminalTurnProgress(normalizedStatus))
        {
            terminalTurnStatuses[normalizedTurnId!] = normalizedStatus;
            return;
        }

        terminalTurnStatuses.TryRemove(normalizedTurnId!, out _);
    }

    private bool ShouldSuppressTerminalTurnEvent(ControlPlaneConversationStreamEvent streamEvent)
    {
        if (!SuppressedTerminalTurnEventKinds.Contains(streamEvent.Kind))
        {
            return false;
        }

        var normalizedTurnId = Normalize(streamEvent.TurnId?.Value);
        return !string.IsNullOrWhiteSpace(normalizedTurnId)
               && terminalTurnStatuses.ContainsKey(normalizedTurnId!);
    }

    private static bool ShouldSuppressTerminalTurnProgress(string? status)
    {
        var normalizedStatus = Normalize(status);
        return !string.IsNullOrWhiteSpace(normalizedStatus)
               && !string.Equals(normalizedStatus, "completed", StringComparison.OrdinalIgnoreCase);
    }

    private static ControlPlaneConversationStreamEvent SanitizeDiagnostics(ControlPlaneConversationStreamEvent streamEvent)
    {
        if (ShouldExposeDiagnostics(streamEvent.Kind)
            || streamEvent.Diagnostics is null
            || (string.IsNullOrWhiteSpace(streamEvent.Diagnostics.DataJson)
                && string.IsNullOrWhiteSpace(streamEvent.Diagnostics.MetadataJson)
                && string.IsNullOrWhiteSpace(streamEvent.Diagnostics.RawJson)))
        {
            return streamEvent;
        }

        return streamEvent with
        {
            Diagnostics = null,
        };
    }

    private static bool ShouldExposeDiagnostics(ControlPlaneConversationStreamEventKind kind)
        => kind is ControlPlaneConversationStreamEventKind.RawNotification or ControlPlaneConversationStreamEventKind.Error;

    private void RaiseEvent(ControlPlaneConversationStreamEvent streamEvent)
    {
        TrackRuntimeProjection(streamEvent);

        if (ShouldSuppressTerminalTurnEvent(streamEvent))
        {
            return;
        }

        try
        {
            var sanitizedEvent = SanitizeDiagnostics(streamEvent);
            StreamEventReceived?.Invoke(this, new ControlPlaneConversationStreamEventArgs(sanitizedEvent));
        }
        catch
        {
            // 订阅异常不影响运行时。
        }
    }

    private void TrackPendingApprovalProjection(string callId, string? threadId, string title, string reason)
    {
        var normalizedCallId = Normalize(callId);
        if (string.IsNullOrWhiteSpace(normalizedCallId))
        {
            return;
        }

        pendingApprovalProjectionStates[normalizedCallId] = new PendingApprovalProjectionState(
            normalizedCallId,
            Normalize(threadId),
            Normalize(title) ?? "等待审批",
            Normalize(reason) ?? "Provider 请求继续执行。",
            BuildRuntimeParticipantRef(),
            DateTimeOffset.UtcNow);
        MaterializeApprovalQueueProjection();
    }

    private void TrackPendingUserInputProjection(string callId, string? threadId, string prompt)
    {
        var normalizedCallId = Normalize(callId);
        if (string.IsNullOrWhiteSpace(normalizedCallId))
        {
            return;
        }

        pendingUserInputProjectionStates[normalizedCallId] = new PendingUserInputProjectionState(
            normalizedCallId,
            Normalize(threadId),
            Normalize(prompt) ?? "等待用户补充输入。",
            BuildRuntimeParticipantRef(),
            DateTimeOffset.UtcNow);
    }

    private void RemovePendingApprovalProjection(string callId)
    {
        var normalizedCallId = Normalize(callId);
        if (string.IsNullOrWhiteSpace(normalizedCallId))
        {
            return;
        }

        if (pendingApprovalProjectionStates.TryRemove(normalizedCallId, out _))
        {
            MaterializeApprovalQueueProjection();
        }
    }

    private void RemovePendingUserInputProjection(string callId)
    {
        var normalizedCallId = Normalize(callId);
        if (!string.IsNullOrWhiteSpace(normalizedCallId))
        {
            pendingUserInputProjectionStates.TryRemove(normalizedCallId, out _);
        }
    }

    private void TrackRuntimeProjection(ControlPlaneConversationStreamEvent streamEvent)
    {
        try
        {
            MaterializeThreadProjection(streamEvent);
            RecordDiagnosticEvent(streamEvent);
        }
        catch
        {
            // 投影维护失败不应影响主执行流。
        }
    }

    private void MaterializeThreadProjection(ControlPlaneConversationStreamEvent streamEvent)
    {
        var threadId = streamEvent.ThreadId;
        if (threadId is null)
        {
            return;
        }

        if (streamEvent.Kind == ControlPlaneConversationStreamEventKind.ThreadNameUpdated)
        {
            var title = Normalize(streamEvent.Text) ?? Normalize(streamEvent.Message);
            if (!string.IsNullOrWhiteSpace(title))
            {
                threadProjectionTitles[threadId.Value.Value] = title!;
            }
        }

        UpdateRuntimeThreadProjectionState(threadId.Value.Value, streamEvent);

        var projection = BuildRuntimeThreadProjection(
            threadId.Value,
            Normalize(streamEvent.TurnId?.Value) ?? Normalize(activeTurnId) ?? Normalize(submittedTurnId));
        _ = projectionRuntimeStores.Snapshots.UpsertAsync(
                new ProjectionSnapshotKey(ProjectionScopeKind.Thread, threadId.Value.Value),
                new ProjectionDelta(new ThreadProjectionPayload(projection)),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private void MaterializeApprovalQueueProjection()
    {
        var projection = new ApprovalQueueProjection(
            pendingApprovalProjectionStates.Values
                .OrderBy(static item => item.RequestedAt)
                .Select(static item => new ApprovalQueueItem(
                    new ApprovalId(item.CallId),
                    item.Title,
                    item.Reason,
                    item.RequestedFromParticipant,
                    item.RequestedAt))
                .ToArray());
        _ = projectionRuntimeStores.Snapshots.UpsertAsync(
                new ProjectionSnapshotKey(ProjectionScopeKind.ApprovalQueue, "global"),
                new ProjectionDelta(new ApprovalQueueProjectionPayload(projection)),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private void RecordDiagnosticEvent(ControlPlaneConversationStreamEvent streamEvent)
    {
        var traceId = ResolveTraceId(streamEvent);
        if (traceId is null)
        {
            return;
        }

        var executionId = new ExecutionId($"execution:{traceId.Value.Value}");
        var timestamp = streamEvent.Timestamp == default ? DateTimeOffset.UtcNow : streamEvent.Timestamp;
        if (streamEvent.Kind == ControlPlaneConversationStreamEventKind.TurnCompleted)
        {
            _ = projectionRuntimeStores.ExecutionTraces.AppendAttemptAsync(
                    traceId.Value,
                    executionId,
                    new AttemptSummary(
                        executionId,
                        AttemptNumber: 1,
                        Succeeded: !string.Equals(streamEvent.Status, "failed", StringComparison.OrdinalIgnoreCase),
                        StartedAt: timestamp,
                        CompletedAt: timestamp),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        var sequence = Interlocked.Increment(ref diagnosticAuditSequence);
        _ = projectionRuntimeStores.ExecutionTraces.AppendAuditRecordAsync(
                traceId.Value,
                executionId,
                new AuditRecord(
                    new AuditRecordId($"{traceId.Value.Value}:event:{sequence.ToString(CultureInfo.InvariantCulture)}"),
                    streamEvent.Kind.ToString(),
                    Normalize(streamEvent.Message)
                    ?? Normalize(streamEvent.Text)
                    ?? Normalize(streamEvent.SourceMethod)
                    ?? streamEvent.Kind.ToString(),
                    timestamp,
                    BuildDiagnosticMetadata(streamEvent)),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private static ExecutionTraceId? ResolveTraceId(ControlPlaneConversationStreamEvent streamEvent)
    {
        var turnId = Normalize(streamEvent.TurnId?.Value);
        if (!string.IsNullOrWhiteSpace(turnId))
        {
            return new ExecutionTraceId($"trace:{turnId}");
        }

        var callId = Normalize(streamEvent.CallId?.Value);
        if (!string.IsNullOrWhiteSpace(callId))
        {
            return new ExecutionTraceId($"trace:{callId}");
        }

        return null;
    }

    private static MetadataBag BuildDiagnosticMetadata(ControlPlaneConversationStreamEvent streamEvent)
    {
        var entries = new Dictionary<string, StructuredValue>(StringComparer.Ordinal);
        AddMetadata(entries, "threadId", streamEvent.ThreadId?.Value);
        AddMetadata(entries, "turnId", streamEvent.TurnId?.Value);
        AddMetadata(entries, "callId", streamEvent.CallId?.Value);
        AddMetadata(entries, "toolName", streamEvent.ToolName);
        AddMetadata(entries, "status", streamEvent.Status);
        AddMetadata(entries, "phase", streamEvent.Phase);
        AddMetadata(entries, "sourceMethod", streamEvent.SourceMethod);
        return entries.Count == 0 ? MetadataBag.Empty : new MetadataBag(entries);
    }

    private static void AddMetadata(Dictionary<string, StructuredValue> entries, string key, string? value)
    {
        var normalized = Normalize(value);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            entries[key] = StructuredValue.FromString(normalized!);
        }
    }

    private void RaiseProviderProjectionEvents(ProviderEventProjectionContext context, ProviderStreamEvent providerEvent)
    {
        foreach (var projectedEvent in providerExecutionEventProjector.Project(context, providerEvent))
        {
            RaiseEvent(projectedEvent);
        }
    }

    private void RaiseProviderProjectionEvents(ProviderEventProjectionContext context, IReadOnlyList<ProviderStreamEvent> providerEvents)
    {
        ArgumentNullException.ThrowIfNull(providerEvents);

        foreach (var providerEvent in providerEvents)
        {
            RaiseProviderProjectionEvents(context, providerEvent);
        }
    }

    private void RaiseProjectedProviderEvents(
        ProviderEventProjectionContext context,
        ProviderStreamEvent providerEvent,
        params ControlPlaneConversationStreamEventKind[] kinds)
    {
        if (kinds is null || kinds.Length == 0)
        {
            RaiseProviderProjectionEvents(context, providerEvent);
            return;
        }

        foreach (var projectedEvent in providerExecutionEventProjector.Project(context, providerEvent))
        {
            if (Array.IndexOf(kinds, projectedEvent.Kind) >= 0)
            {
                RaiseEvent(projectedEvent);
            }
        }
    }

    private static string? ReadString(JsonElement json, params string[] path)
    {
        var current = json;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null,
        };
    }

    private static AgentServiceTier? ReadServiceTier(JsonElement json, params string[] path)
    {
        var value = ReadString(json, path);
        return AgentServiceTier.TryParse(value, out var tier) ? tier : null;
    }

    private static AgentApprovalPolicy? ReadApprovalPolicy(JsonElement json, params string[] path)
    {
        var current = json;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        try
        {
            return current.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => AgentApprovalPolicy.TryParse(current.GetString(), out var policy) ? policy : null,
                JsonValueKind.Object => JsonSerializer.Deserialize<AgentApprovalPolicy>(current.GetRawText()),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool? ReadBool(JsonElement json, params string[] path)
    {
        var current = json;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(current.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static ushort? ReadUInt16(JsonElement json, params string[] path)
    {
        var current = json;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        if (current.ValueKind == JsonValueKind.Number && current.TryGetUInt16(out var number))
        {
            return number;
        }

        if (current.ValueKind == JsonValueKind.String
            && ushort.TryParse(current.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static int? ReadInt32(JsonElement json, params string[] path)
    {
        var current = json;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        if (current.ValueKind == JsonValueKind.Number && current.TryGetInt32(out var number))
        {
            return number;
        }

        if (current.ValueKind == JsonValueKind.String
            && int.TryParse(current.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static long? ReadInt64(JsonElement json, params string[] path)
    {
        var current = json;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        if (current.ValueKind == JsonValueKind.Number && current.TryGetInt64(out var number))
        {
            return number;
        }

        if (current.ValueKind == JsonValueKind.String
            && long.TryParse(current.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static ulong? ReadUnsignedLong(JsonElement json, params string[] path)
    {
        var current = json;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        if (current.ValueKind == JsonValueKind.Number && current.TryGetUInt64(out var number))
        {
            return number;
        }

        if (current.ValueKind == JsonValueKind.String
            && ulong.TryParse(current.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static void AddIfNotNull(IDictionary<string, object?> payload, string name, object? value)
    {
        if (value is null)
        {
            return;
        }

        if (value is string text && string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        payload[name] = value;
    }

    private static void AddIfTrue(IDictionary<string, object?> payload, string name, bool value)
    {
        if (value)
        {
            payload[name] = true;
        }
    }

    private static IReadOnlyDictionary<string, bool> ReadBooleanMap(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, bool>();
        }

        var values = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in property.EnumerateObject())
        {
            var value = item.Value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(item.Value.GetString(), out var parsed) => parsed,
                _ => (bool?)null,
            };

            if (value.HasValue)
            {
                values[item.Name] = value.Value;
            }
        }

        return values;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return property
            .EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => item.GetString())
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item!)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadObjectStringArray(JsonElement json, string propertyName, string valuePropertyName)
    {
        if (!json.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var values = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var value = ReadString(item, valuePropertyName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static IReadOnlyList<string> ReadObjectPropertyNames(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<string>();
        }

        return property.EnumerateObject()
            .Select(static item => item.Name)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string> ReadStringDictionary(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>();
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in property.EnumerateObject())
        {
            if (item.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = item.Value.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[item.Name] = value;
            }
        }

        return values;
    }

    private static JsonElement? TryGetObject(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return value;
    }

    private static JsonElement? TryGetObject(JsonElement? json)
    {
        if (!json.HasValue || json.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return json.Value;
    }

    private static JsonElement? TryGetPropertyValue(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value;
    }

    private static DateTimeOffset? ReadUnixTime(JsonElement json, params string[] path)
    {
        var current = json;
        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        if (current.ValueKind == JsonValueKind.Number && current.TryGetInt64(out var seconds))
        {
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(seconds);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static DateTimeOffset? ReadUnixTime(long? seconds)
    {
        if (!seconds.HasValue)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds.Value);
        }
        catch
        {
            return null;
        }
    }

    private static ControlPlaneThreadListResult ToControlPlaneThreadListResult(AgentThreadListResult result)
        => new()
        {
            Threads = result.Data.Select(ToControlPlaneThreadSummary).ToArray(),
            NextCursor = result.NextCursor,
        };

    private static ControlPlaneThreadSummary ToControlPlaneThreadSummary(AgentThreadInfo thread)
        => new()
        {
            ThreadId = new ThreadId(thread.ThreadId),
            Preview = thread.Preview,
            Name = thread.Name,
            WorkingDirectory = thread.Cwd,
            Path = thread.Path,
            ModelProvider = thread.ModelProvider,
            Source = thread.Source?.GetThreadSourceKind(),
            ParentThreadId = thread.Source?.SubAgentSource is { ParentThreadId: { Length: > 0 } parentThreadId }
                ? new ThreadId(parentThreadId)
                : null,
            LineageDepth = thread.Source?.SubAgentSource?.Depth,
            CliVersion = thread.CliVersion,
            AgentNickname = thread.AgentNickname,
            AgentRole = thread.AgentRole,
            CreatedAt = thread.CreatedAt,
            UpdatedAt = thread.UpdatedAt,
            IsEphemeral = thread.IsEphemeral,
            Status = thread.Status?.Type,
            ActiveFlags = thread.Status?.ActiveFlags ?? Array.Empty<string>(),
            GitSha = thread.GitInfo?.Sha,
            GitBranch = thread.GitInfo?.Branch,
            GitOriginUrl = thread.GitInfo?.OriginUrl,
            SessionConfiguration = ToControlPlaneThreadSessionConfiguration(thread.SessionConfiguration),
        };

    private static ControlPlaneThreadSummary ToControlPlaneThreadSummary(AgentThreadResumeResult thread)
        => new()
        {
            ThreadId = new ThreadId(thread.ThreadId),
            Preview = thread.Preview,
            Name = thread.Name,
            WorkingDirectory = thread.Cwd,
            Path = thread.Path,
            ModelProvider = thread.ModelProvider,
            Source = thread.Source?.GetThreadSourceKind(),
            ParentThreadId = thread.Source?.SubAgentSource is { ParentThreadId: { Length: > 0 } parentThreadId }
                ? new ThreadId(parentThreadId)
                : null,
            LineageDepth = thread.Source?.SubAgentSource?.Depth,
            CliVersion = thread.CliVersion,
            AgentNickname = thread.AgentNickname,
            AgentRole = thread.AgentRole,
            CreatedAt = thread.CreatedAt,
            UpdatedAt = thread.UpdatedAt,
            IsEphemeral = thread.IsEphemeral,
            Status = thread.Status?.Type,
            ActiveFlags = thread.Status?.ActiveFlags ?? Array.Empty<string>(),
            GitSha = thread.GitInfo?.Sha,
            GitBranch = thread.GitInfo?.Branch,
            GitOriginUrl = thread.GitInfo?.OriginUrl,
            SessionConfiguration = ToControlPlaneThreadSessionConfiguration(thread.SessionConfiguration),
        };

    private static ControlPlaneThreadSnapshot ToControlPlaneThreadSnapshot(AgentThreadResumeResult result)
        => new()
        {
            Thread = ToControlPlaneThreadSummary(result),
            Messages = result.Messages.Select(ToControlPlaneConversationMessage).ToArray(),
            Turns = result.Turns.Select(ToControlPlaneThreadTurn).ToArray(),
            SeedHistory = result.SeedHistory.Select(ToControlPlaneSeedHistoryItem).ToArray(),
            PendingInputState = result.PendingInputState,
            PendingInteractiveRequests = result.PendingInteractiveRequests.ToArray(),
        };

    private static ControlPlaneThreadDetail ToControlPlaneThreadDetail(AgentThreadDetails thread)
        => new()
        {
            ThreadId = new ThreadId(thread.Id),
            Preview = thread.Preview,
            Name = thread.Name,
            WorkingDirectory = thread.Cwd,
            Path = thread.Path,
            ModelProvider = thread.ModelProvider,
            Source = thread.Source?.GetThreadSourceKind(),
            ParentThreadId = thread.Source?.SubAgentSource is { ParentThreadId: { Length: > 0 } parentThreadId }
                ? new ThreadId(parentThreadId)
                : null,
            LineageDepth = thread.Source?.SubAgentSource?.Depth,
            CliVersion = thread.CliVersion,
            AgentNickname = thread.AgentNickname,
            AgentRole = thread.AgentRole,
            CreatedAt = thread.CreatedAt,
            UpdatedAt = thread.UpdatedAt,
            IsEphemeral = thread.Ephemeral,
            Status = thread.Status?.Type,
            ActiveFlags = thread.Status?.ActiveFlags ?? Array.Empty<string>(),
            GitSha = thread.GitInfo?.Sha,
            GitBranch = thread.GitInfo?.Branch,
            GitOriginUrl = thread.GitInfo?.OriginUrl,
            Turns = thread.Turns.Select(ToControlPlaneThreadTurn).ToArray(),
            SeedHistory = thread.SeedHistory.Select(ToControlPlaneSeedHistoryItem).ToArray(),
            PendingInputState = thread.PendingInputState,
            PendingInteractiveRequests = thread.PendingInteractiveRequests.ToArray(),
            SessionConfiguration = ToControlPlaneThreadSessionConfiguration(thread.SessionConfiguration),
        };

    private static ControlPlaneThreadSessionConfiguration? ToControlPlaneThreadSessionConfiguration(AgentThreadSessionConfiguration? configuration)
    {
        if (configuration is null)
        {
            return null;
        }

        return new ControlPlaneThreadSessionConfiguration
        {
            Model = configuration.Model,
            ModelProvider = configuration.ModelProvider,
            ModelProviderId = configuration.ModelProviderId,
            ModelRouteSetId = configuration.ModelRouteSetId,
            ServiceTier = configuration.ServiceTier?.ToString(),
            ApprovalPolicy = configuration.ApprovalPolicy?.ToString(),
            SandboxPolicy = configuration.SandboxPolicy,
            SandboxPolicyPayload = configuration.SandboxPolicyPayload is null ? null : StructuredValue.FromPlainObject(configuration.SandboxPolicyPayload.ToPlainObject()),
            ReasoningEffort = configuration.ReasoningEffort,
            HistoryLogId = configuration.HistoryLogId,
            HistoryEntryCount = configuration.HistoryEntryCount,
            RolloutPath = configuration.RolloutPath,
            ReasoningSummary = configuration.ReasoningSummary,
            Verbosity = configuration.Verbosity,
            Personality = configuration.Personality,
            AllowLoginShell = configuration.AllowLoginShell,
            ShellEnvironmentPolicy = configuration.ShellEnvironmentPolicy is null ? null : StructuredValue.FromPlainObject(configuration.ShellEnvironmentPolicy.ToPlainObject()),
            ProviderBaseUrl = configuration.ProviderBaseUrl,
            ProviderApiKeyEnvironmentVariable = configuration.ProviderApiKeyEnvironmentVariable,
            ProviderWireApi = configuration.ProviderWireApi,
            ProviderRequestMaxRetries = configuration.ProviderRequestMaxRetries,
            ProviderStreamMaxRetries = configuration.ProviderStreamMaxRetries,
            ProviderStreamIdleTimeoutMs = configuration.ProviderStreamIdleTimeoutMs,
            ProviderWebsocketConnectTimeoutMs = configuration.ProviderWebsocketConnectTimeoutMs,
            ProviderSupportsWebsockets = configuration.ProviderSupportsWebsockets,
            WebSearchMode = configuration.WebSearchMode,
            ServiceName = configuration.ServiceName,
            BaseInstructions = configuration.BaseInstructions,
            DeveloperInstructions = configuration.DeveloperInstructions,
            UserInstructions = configuration.UserInstructions,
            DynamicTools = configuration.DynamicTools?.Select(static item => StructuredValue.FromPlainObject(item.ToPlainObject())).ToArray(),
            CollaborationMode = configuration.CollaborationMode is null ? null : StructuredValue.FromPlainObject(configuration.CollaborationMode.ToPlainObject()),
            PersistExtendedHistory = configuration.PersistExtendedHistory,
            ForkedFromThreadId = string.IsNullOrWhiteSpace(configuration.ForkedFromId)
                ? null
                : new ThreadId(configuration.ForkedFromId),
            WorkingDirectory = configuration.Cwd,
            SessionSource = configuration.SessionSource,
            WindowsSandboxLevel = configuration.WindowsSandboxLevel,
            DefaultModeRequestUserInputEnabled = configuration.DefaultModeRequestUserInputEnabled,
        };
    }

    private static ControlPlaneConversationMessage ToControlPlaneConversationMessage(ConversationMessage message)
        => new()
        {
            Role = message.Role switch
            {
                ConversationRole.System => ControlPlaneConversationRole.System,
                ConversationRole.Assistant => ControlPlaneConversationRole.Assistant,
                _ => ControlPlaneConversationRole.User,
            },
            Content = message.Content,
            ContentItems = message.ContentItems.Select(ToControlPlaneInputItem).ToArray(),
            Timestamp = message.Timestamp,
            IsStreaming = message.IsStreaming,
        };

    private static ControlPlaneInputItem ToControlPlaneInputItem(AgentUserInput input)
        => input switch
        {
            TextUserInput text => new ControlPlaneTextInput(
                text.Text,
                text.TextElements.Select(static element => new ControlPlaneTextElement(
                    new ControlPlaneByteRange(element.ByteRange.Start, element.ByteRange.End),
                    element.Placeholder)).ToArray()),
            ImageUserInput image => new ControlPlaneImageInput(image.Url),
            LocalImageUserInput localImage => new ControlPlaneLocalImageInput(localImage.Path),
            SkillUserInput skill => new ControlPlaneSkillInput(skill.Name, skill.Path),
            MentionUserInput mention => new ControlPlaneMentionInput(mention.Name, mention.Path),
            _ => new ControlPlaneTextInput(input.Type),
        };

    private static AgentUserInput ToAgentUserInput(ControlPlaneInputItem input)
        => input switch
        {
            ControlPlaneTextInput text => new TextUserInput
            {
                Type = text.Type,
                Text = text.Text,
                TextElements = (text.TextElements ?? Array.Empty<ControlPlaneTextElement>())
                    .Select(static element => new AgentTextElement
                    {
                        ByteRange = new AgentByteRange
                        {
                            Start = element.ByteRange.Start,
                            End = element.ByteRange.End,
                        },
                        Placeholder = element.Placeholder,
                    })
                    .ToArray(),
            },
            ControlPlaneImageInput image => new ImageUserInput
            {
                Type = image.Type,
                Url = image.Url,
            },
            ControlPlaneLocalImageInput localImage => new LocalImageUserInput
            {
                Type = localImage.Type,
                Path = localImage.Path,
            },
            ControlPlaneSkillInput skill => new SkillUserInput
            {
                Type = skill.Type,
                Name = skill.Name,
                Path = skill.Path,
            },
            ControlPlaneMentionInput mention => new MentionUserInput
            {
                Type = mention.Type,
                Name = mention.Name,
                Path = mention.Path,
            },
            _ => throw new NotSupportedException($"不支持的控制平面输入类型：{input.GetType().Name}"),
        };

    private static ControlPlaneThreadTurn ToControlPlaneThreadTurn(AgentThreadTurn turn)
        => new()
        {
            Id = turn.Id,
            Status = turn.Status,
            Error = turn.Error is null
                ? null
                : new ControlPlaneThreadTurnError
                {
                    Message = turn.Error.Message,
                    AdditionalDetails = turn.Error.AdditionalDetails,
                },
            Items = turn.Items.Select(ToControlPlaneThreadTurnItem).ToArray(),
        };

    private static ControlPlaneThreadTurnItem ToControlPlaneThreadTurnItem(AgentThreadTurnItem item)
        => new()
        {
            Id = item.Id,
            Type = item.Type,
            Text = item.Text,
            Phase = item.Phase,
            Data = ToControlPlaneThreadTurnItemData(item),
        };

    private static StructuredValue? ToControlPlaneThreadTurnItemData(AgentThreadTurnItem item)
        => item switch
        {
            GenericThreadTurnItem generic => ToContractStructuredValue(generic.RawData),
            UserMessageThreadItem userMessage => StructuredValue.FromObject(
                new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                {
                    ["content"] = StructuredValue.FromArray(
                        userMessage.Content.Select(ToControlPlaneInputItemStructuredValue).ToArray()),
                }),
            _ => null,
        };

    private static StructuredValue ToControlPlaneInputItemStructuredValue(AgentUserInput input)
        => input switch
        {
            TextUserInput text => StructuredValue.FromObject(
                new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                {
                    ["type"] = StructuredValue.FromString(Normalize(text.Type) ?? "text"),
                    ["text"] = StructuredValue.FromString(text.Text),
                    ["textElements"] = StructuredValue.FromArray(
                        text.TextElements.Select(static element => StructuredValue.FromObject(
                            new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                            {
                                ["byteRange"] = StructuredValue.FromObject(
                                    new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                                    {
                                        ["start"] = StructuredValue.FromNumber(element.ByteRange.Start.ToString(CultureInfo.InvariantCulture)),
                                        ["end"] = StructuredValue.FromNumber(element.ByteRange.End.ToString(CultureInfo.InvariantCulture)),
                                    }),
                                ["placeholder"] = element.Placeholder is null
                                    ? StructuredValue.Null
                                    : StructuredValue.FromString(element.Placeholder),
                            })).ToArray()),
                }),
            ImageUserInput image => StructuredValue.FromObject(
                new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                {
                    ["type"] = StructuredValue.FromString(Normalize(image.Type) ?? "image"),
                    ["url"] = StructuredValue.FromString(image.Url),
                }),
            LocalImageUserInput localImage => StructuredValue.FromObject(
                new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                {
                    ["type"] = StructuredValue.FromString(Normalize(localImage.Type) ?? "local_image"),
                    ["path"] = StructuredValue.FromString(localImage.Path),
                }),
            SkillUserInput skill => StructuredValue.FromObject(
                new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                {
                    ["type"] = StructuredValue.FromString(Normalize(skill.Type) ?? "skill"),
                    ["name"] = StructuredValue.FromString(skill.Name),
                    ["path"] = StructuredValue.FromString(skill.Path),
                }),
            MentionUserInput mention => StructuredValue.FromObject(
                new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                {
                    ["type"] = StructuredValue.FromString(Normalize(mention.Type) ?? "mention"),
                    ["name"] = StructuredValue.FromString(mention.Name),
                    ["path"] = StructuredValue.FromString(mention.Path),
                }),
            _ => StructuredValue.FromObject(
                new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                {
                    ["type"] = StructuredValue.FromString(Normalize(input.Type) ?? "unknown"),
                }),
        };

    private static ControlPlaneSeedHistoryItem ToControlPlaneSeedHistoryItem(AgentThreadSeedHistoryItem item)
        => new()
        {
            Role = item.Role,
            Content = item.Content,
            Inputs = item.Inputs.Select(ToControlPlaneInputItem).ToArray(),
        };

    private static ControlPlanePendingInputState? ToControlPlanePendingInputState(PendingInputStatePayload? payload)
    {
        if (payload is null)
        {
            return null;
        }

        return new ControlPlanePendingInputState(
            payload.Entries.Select(ToControlPlanePendingInputStateEntry).ToArray(),
            payload.InterruptRequestPending,
            payload.SubmitPendingSteersAfterInterrupt,
            payload.QueuedUserMessages?.Select(ToControlPlanePendingInputStateEntry).ToArray(),
            payload.PendingSteers?.Select(ToControlPlanePendingInputStateEntry).ToArray());
    }

    private static ControlPlanePendingInputStateEntry ToControlPlanePendingInputStateEntry(PendingInputStateEntryPayload entry)
        => new(
            entry.CorrelationId,
            entry.RequestedMode,
            entry.EffectiveMode,
            entry.LifecycleState,
            entry.ExpectedTurnId,
            entry.TurnId,
            entry.CompareKey is null
                ? null
                : StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["message"] = entry.CompareKey.Message,
                    ["imageCount"] = entry.CompareKey.ImageCount,
                }),
            entry.PendingBucket,
            entry.Inputs?.Select(ToControlPlaneInputItem).ToArray());

    private static ControlPlanePendingInputState EmptyControlPlanePendingInputState()
        => new(Array.Empty<ControlPlanePendingInputStateEntry>(), false);

    private static PendingInputStatePayload ToRuntimePendingInputStatePayload(ControlPlanePendingInputState payload)
        => CanonicalizePendingInputStatePayload(
            new PendingInputStatePayload(
                payload.Entries.Select(ToRuntimePendingInputStateEntryPayload).ToArray(),
                payload.InterruptRequestPending,
                payload.SubmitPendingSteersAfterInterrupt,
                payload.QueuedUserMessages?.Select(ToRuntimePendingInputStateEntryPayload).ToArray(),
                payload.PendingSteers?.Select(ToRuntimePendingInputStateEntryPayload).ToArray()));

    private static PendingInputStateEntryPayload ToRuntimePendingInputStateEntryPayload(ControlPlanePendingInputStateEntry entry)
        => new(
            entry.CorrelationId,
            entry.RequestedMode,
            entry.EffectiveMode,
            entry.LifecycleState,
            entry.ExpectedTurnId,
            entry.TurnId,
            ToRuntimePendingFollowUpCompareKeyPayload(entry.CompareKey),
            entry.PendingBucket,
            entry.Inputs?.Select(ToAgentUserInput).ToArray());

    private static PendingFollowUpCompareKeyPayload? ToRuntimePendingFollowUpCompareKeyPayload(StructuredValue? compareKey)
    {
        if (compareKey is null)
        {
            return null;
        }

        var plainObject = compareKey.ToPlainObject() as IReadOnlyDictionary<string, object?>;
        if (plainObject is null)
        {
            return null;
        }

        plainObject.TryGetValue("message", out var messageValue);
        plainObject.TryGetValue("imageCount", out var imageCountValue);
        var imageCount = imageCountValue switch
        {
            int value => value,
            long value => checked((int)value),
            double value => checked((int)value),
            decimal value => checked((int)value),
            _ => 0,
        };

        return new PendingFollowUpCompareKeyPayload(messageValue as string, imageCount);
    }

    private static ControlPlanePendingInteractiveRequest ToControlPlanePendingInteractiveRequest(PendingInteractiveRequestReplay request)
        => new()
        {
            RequestId = request.RequestId,
            RequestIdRaw = request.RequestIdRaw,
            RequestKind = request.RequestKind,
            RequestMethod = request.RequestMethod,
            CallId = request.CallId,
            ThreadId = request.ThreadId,
            TurnId = request.TurnId,
            ToolName = request.ToolName,
            ServerName = request.ServerName,
            Text = request.Text,
            Status = request.Status,
            Phase = request.Phase,
            RequiresApproval = request.RequiresApproval,
            ApprovalKind = request.ApprovalKind,
            AvailableDecisions = request.AvailableDecisions,
            AvailableDecisionOptions = request.AvailableDecisionOptions?.Select(ToControlPlaneApprovalDecisionOption).ToArray(),
        };

    private static IReadOnlyList<PendingInteractiveRequestReplay> ToPendingInteractiveRequestReplays(IReadOnlyList<ControlPlanePendingInteractiveRequest>? requests)
        => requests is { Count: > 0 }
            ? requests.Select(ToPendingInteractiveRequestReplay).ToArray()
            : Array.Empty<PendingInteractiveRequestReplay>();

    private static PendingInteractiveRequestReplay ToPendingInteractiveRequestReplay(ControlPlanePendingInteractiveRequest request)
        => new()
        {
            RequestId = request.RequestId,
            RequestIdRaw = request.RequestIdRaw,
            RequestKind = request.RequestKind,
            RequestMethod = request.RequestMethod,
            CallId = request.CallId,
            ThreadId = request.ThreadId,
            TurnId = request.TurnId,
            ToolName = request.ToolName,
            ServerName = request.ServerName,
            Text = request.Text,
            Status = request.Status,
            Phase = request.Phase,
            RequiresApproval = request.RequiresApproval,
            ApprovalKind = request.ApprovalKind,
            AvailableDecisions = request.AvailableDecisions,
            AvailableDecisionOptions = request.AvailableDecisionOptions?.Select(ToRuntimeApprovalDecisionOptionPayload).ToArray(),
        };

    private static ControlPlaneApprovalDecisionOption ToControlPlaneApprovalDecisionOption(ApprovalDecisionOptionPayload option)
        => new(
            option.Type,
            option.ExecPolicyAmendment is null
                ? null
                : new ControlPlaneExecPolicyAmendment(option.ExecPolicyAmendment.CommandPrefix),
            option.NetworkPolicyAmendment is null
                ? null
                : new ControlPlaneNetworkPolicyAmendment(
                    option.NetworkPolicyAmendment.Host,
                    option.NetworkPolicyAmendment.Action));

    private static ApprovalDecisionOptionPayload ToRuntimeApprovalDecisionOptionPayload(ControlPlaneApprovalDecisionOption option)
        => new(
            option.Type,
            option.ExecPolicyAmendment is null
                ? null
                : new ExecPolicyAmendmentPayload(option.ExecPolicyAmendment.CommandPrefix),
            option.NetworkPolicyAmendment is null
                ? null
                : new NetworkPolicyAmendmentPayload(
                    option.NetworkPolicyAmendment.Host,
                    option.NetworkPolicyAmendment.Action));

    private static StructuredValue? ToContractStructuredValue(AgentStructuredValue? value)
        => value is null ? null : StructuredValue.FromPlainObject(value.ToPlainObject());

    private static AgentThreadInfo BuildAgentThreadInfo(
        AppServerThreadSummaryDto thread,
        string threadId,
        AgentThreadSessionConfiguration? sessionConfiguration = null)
    {
        return new AgentThreadInfo
        {
            ThreadId = threadId,
            Preview = Normalize(thread.Preview) ?? string.Empty,
            Name = Normalize(thread.Name),
            Cwd = Normalize(thread.Cwd)
                  ?? Normalize(sessionConfiguration?.Cwd),
            Path = Normalize(thread.Path)
                   ?? Normalize(sessionConfiguration?.RolloutPath),
            ModelProvider = Normalize(thread.ModelProvider)
                            ?? Normalize(sessionConfiguration?.ModelProvider)
                            ?? Normalize(sessionConfiguration?.ModelProviderId),
            Source = ParseSessionSource(thread.Source)
                     ?? sessionConfiguration?.SessionSource,
            CliVersion = Normalize(thread.CliVersion),
            AgentNickname = Normalize(thread.AgentNickname),
            AgentRole = Normalize(thread.AgentRole),
            CreatedAt = ReadUnixTime(thread.CreatedAt),
            UpdatedAt = ReadUnixTime(thread.UpdatedAt) ?? DateTimeOffset.Now,
            IsEphemeral = thread.Ephemeral
                          ?? sessionConfiguration?.Ephemeral
                          ?? false,
            Status = ParseThreadStatus(thread.Status),
            GitInfo = ParseThreadGitInfo(thread.GitInfo),
            SessionConfiguration = sessionConfiguration,
            SessionState = ParseThreadSessionProjection(thread.SessionState),
        };
    }

    private static string BuildMcpServerStatusSummary(JsonElement parameters)
    {
        if (parameters.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            return $"mcp servers updated: {data.GetArrayLength()}";
        }

        return "mcp servers updated";
    }

    private static ThreadStatusChangedPayload BuildThreadStatusChangedPayload(AppServerThreadStatusDto? status)
        => new(
            Normalize(status?.Type) ?? "unknown",
            status?.ActiveFlags?.Where(static flag => !string.IsNullOrWhiteSpace(flag)).ToArray() ?? Array.Empty<string>());

    private static TokenUsageBreakdownPayload BuildTokenUsageBreakdownPayload(AppServerTokenUsageBreakdownDto? breakdown)
        => new(
            breakdown?.TotalTokens ?? 0,
            breakdown?.InputTokens ?? 0,
            breakdown?.CachedInputTokens ?? 0,
            breakdown?.OutputTokens ?? 0,
            breakdown?.ReasoningOutputTokens ?? 0);

    private static ThreadTokenUsagePayload? BuildThreadTokenUsagePayload(AppServerThreadTokenUsageDto? tokenUsage)
    {
        if (tokenUsage is null)
        {
            return null;
        }

        return new ThreadTokenUsagePayload(
            BuildTokenUsageBreakdownPayload(tokenUsage.Last),
            BuildTokenUsageBreakdownPayload(tokenUsage.Total),
            tokenUsage.ModelContextWindow,
            tokenUsage.Estimated ?? false,
            Normalize(tokenUsage.Source));
    }

    private static IReadOnlyList<AppListUpdatedEntryPayload> BuildAppListUpdatedPayload(AppListUpdatedParamsDto parameters)
    {
        if (parameters.Data is null || parameters.Data.Count == 0)
        {
            return Array.Empty<AppListUpdatedEntryPayload>();
        }

        return parameters.Data
            .Where(static item => !string.IsNullOrWhiteSpace(item.Id))
            .Select(static item => new AppListUpdatedEntryPayload(
                item.Id!,
                Normalize(item.Name) ?? item.Id!,
                Normalize(item.Description),
                Normalize(item.LogoUrl),
                Normalize(item.LogoUrlDark),
                Normalize(item.DistributionChannel),
                BuildAppBrandingPayload(item.Branding),
                BuildAppMetadataPayload(item.AppMetadata ?? item.Metadata),
                item.Labels is null
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    : new Dictionary<string, string>(
                        item.Labels
                            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key))
                            .ToDictionary(
                                static pair => pair.Key,
                                static pair => pair.Value ?? string.Empty,
                                StringComparer.Ordinal),
                        StringComparer.Ordinal),
                item.IsAccessible ?? false,
                item.IsEnabled ?? false,
                Normalize(item.InstallUrl),
                item.PluginDisplayNames?.Where(static name => !string.IsNullOrWhiteSpace(name)).ToArray() ?? Array.Empty<string>()))
            .ToArray();
    }

    private static AppBrandingPayload? BuildAppBrandingPayload(AppServerAppBrandingDto? branding)
    {
        if (branding is null)
        {
            return null;
        }

        return new AppBrandingPayload(
            Normalize(branding.Category),
            Normalize(branding.Developer),
            Normalize(branding.Website),
            Normalize(branding.PrivacyPolicy),
            Normalize(branding.TermsOfService),
            branding.IsDiscoverableApp);
    }

    private static AppMetadataPayload? BuildAppMetadataPayload(AppServerAppMetadataDto? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        var screenshots = metadata.Screenshots?
            .Select(static screenshot => new AppScreenshotPayload(
                Normalize(screenshot.Caption),
                Normalize(screenshot.Url)))
            .ToArray()
            ?? Array.Empty<AppScreenshotPayload>();

        return new AppMetadataPayload(
            metadata.Review is null
                ? null
                : new AppReviewPayload(
                    Normalize(metadata.Review.Status),
                    Normalize(metadata.Review.Message)),
            screenshots);
    }

    private static ConfigRangePayload? BuildConfigRangePayload(AppServerConfigRangeDto? range)
    {
        if (range?.Start?.Line is not int startLine
            || range.Start.Column is not int startColumn
            || range.End?.Line is not int endLine
            || range.End.Column is not int endColumn)
        {
            return null;
        }

        return new ConfigRangePayload(
            new ConfigRangePositionPayload(startLine, startColumn),
            new ConfigRangePositionPayload(endLine, endColumn));
    }

    private static ThreadRealtimeItemAddedPayload BuildThreadRealtimeItemAddedPayload(JsonElement? item)
    {
        if (item is not { ValueKind: JsonValueKind.Object } itemObject)
        {
            return new ThreadRealtimeItemAddedPayload(null, null, null, null, null);
        }

        return new ThreadRealtimeItemAddedPayload(
            ReadString(itemObject, "id") ?? ReadString(itemObject, "item_id"),
            ReadString(itemObject, "type"),
            ReadString(itemObject, "role"),
            ReadString(itemObject, "status"),
            BuildRealtimeItemText(itemObject));
    }

    private static string? BuildRealtimeItemText(JsonElement? item)
    {
        if (item is not { ValueKind: JsonValueKind.Object } itemObject)
        {
            return null;
        }

        return ReadString(itemObject, "text")
               ?? ReadString(itemObject, "input_transcript")
               ?? ReadString(itemObject, "content")
               ?? ReadString(itemObject, "name")
               ?? ReadString(itemObject, "type");
    }

    private static string? ResolveInformationalText(string method, JsonElement parameters)
    {
        if (string.Equals(method, "thread/name/updated", StringComparison.Ordinal))
        {
            return ReadString(parameters, "threadName")
                ?? ReadString(parameters, "name")
                ?? method;
        }

        if (string.Equals(method, "thread/archived", StringComparison.Ordinal)
            || string.Equals(method, "thread/unarchived", StringComparison.Ordinal))
        {
            return ReadString(parameters, "threadId")
                ?? ReadString(parameters, "summary")
                ?? method;
        }

        return ReadString(parameters, "summary")
            ?? ReadString(parameters, "details")
            ?? ReadString(parameters, "reason")
            ?? ReadString(parameters, "message")
            ?? ReadString(parameters, "status")
            ?? ReadString(parameters, "name")
            ?? ReadString(parameters, "sessionId")
            ?? ReadString(parameters, "method")
            ?? ReadString(parameters, "thread", "status");
    }

    private static bool TryParseConversationMessage(JsonElement data, out ConversationMessage message)
    {
        message = default!;

        if (data.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var role = ResolveConversationRole(ReadString(data, "role"));
        var contentItems = ParseConversationMessageInputs(data);
        var content = Normalize(ReadString(data, "content")) ?? Normalize(ExtractConversationText(contentItems));
        if (string.IsNullOrWhiteSpace(content) && contentItems.Count == 0)
        {
            return false;
        }

        message = new ConversationMessage
        {
            Role = role,
            Content = content ?? string.Empty,
            ContentItems = contentItems,
            Timestamp = ReadUnixTime(data, "timestamp") ?? DateTimeOffset.Now,
        };
        return true;
    }

    private static IReadOnlyList<AgentUserInput> ParseConversationMessageInputs(JsonElement data)
    {
        if (!TryGetArray(data, "inputs", out var inputsArray) && !TryGetArray(data, "contentItems", out inputsArray))
        {
            return Array.Empty<AgentUserInput>();
        }

        var inputs = new List<AgentUserInput>();
        foreach (var itemElement in inputsArray.EnumerateArray())
        {
            var parsed = ParseThreadUserInput(itemElement);
            if (parsed is not null)
            {
                inputs.Add(parsed);
            }
        }

        return inputs;
    }

    private static IReadOnlyList<ConversationMessage> BuildMessagesFromThreadTurns(JsonElement thread)
    {
        var messages = new List<ConversationMessage>();

        if (thread.TryGetProperty("seedHistory", out var seedHistory) && seedHistory.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in seedHistory.EnumerateArray())
            {
                var text = ExtractSeedHistoryText(item);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                messages.Add(new ConversationMessage
                {
                    Role = ResolveConversationRole(ReadString(item, "role")),
                    Content = text,
                    Timestamp = DateTimeOffset.Now,
                });
            }
        }

        if (!thread.TryGetProperty("turns", out var turns) || turns.ValueKind != JsonValueKind.Array)
        {
            return messages;
        }

        foreach (var turn in turns.EnumerateArray())
        {
            messages.AddRange(BuildTurnConversationMessages(turn));
        }

        return messages;
    }

    private static IReadOnlyList<ConversationMessage> BuildMessagesFromThreadHistory(
        IReadOnlyList<AgentThreadSeedHistoryItem> seedHistory,
        IReadOnlyList<AgentThreadTurn> turns)
    {
        var messages = new List<ConversationMessage>();
        foreach (var item in seedHistory)
        {
            var role = ResolveConversationRole(item.Role);
            var text = role == ConversationRole.User
                ? ExtractConversationText(item.Inputs) ?? Normalize(item.Content)
                : Normalize(item.Content);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            messages.Add(new ConversationMessage
            {
                Role = role,
                Content = text!,
                ContentItems = item.Inputs,
                Timestamp = DateTimeOffset.Now,
            });
        }

        foreach (var turn in turns)
        {
            messages.AddRange(BuildTurnConversationMessages(turn.Items));
        }

        return messages;
    }

    private static IReadOnlyList<ConversationMessage> BuildTurnConversationMessages(JsonElement turn)
    {
        if (!turn.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ConversationMessage>();
        }

        var messages = new List<ConversationMessage>();
        foreach (var item in items.EnumerateArray())
        {
            if (!TryBuildTurnConversationMessage(item, out var message))
            {
                continue;
            }

            messages.Add(message);
        }

        return NormalizeTurnConversationOrder(messages);
    }

    private static IReadOnlyList<ConversationMessage> BuildTurnConversationMessages(IReadOnlyList<AgentThreadTurnItem> items)
    {
        if (items.Count == 0)
        {
            return Array.Empty<ConversationMessage>();
        }

        var messages = new List<ConversationMessage>();
        foreach (var item in items)
        {
            if (!TryBuildTurnConversationMessage(item, out var message))
            {
                continue;
            }

            messages.Add(message);
        }

        return NormalizeTurnConversationOrder(messages);
    }

    private static IReadOnlyList<ConversationMessage> NormalizeTurnConversationOrder(List<ConversationMessage> messages)
    {
        if (messages.Count < 2)
        {
            return messages;
        }

        var firstUserIndex = -1;
        var firstAssistantIndex = -1;
        for (var index = 0; index < messages.Count; index++)
        {
            var role = messages[index].Role;
            if (role == ConversationRole.User && firstUserIndex < 0)
            {
                firstUserIndex = index;
            }
            else if (role == ConversationRole.Assistant && firstAssistantIndex < 0)
            {
                firstAssistantIndex = index;
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

        // 恢复线程时按会话语义稳定化顺序，避免同一 turn 中 assistant 落到 user 之前。
        var ordered = new List<ConversationMessage>(messages.Count);
        ordered.AddRange(messages.Where(static item => item.Role == ConversationRole.User));
        ordered.AddRange(messages.Where(static item => item.Role == ConversationRole.Assistant));
        ordered.AddRange(messages.Where(static item => item.Role != ConversationRole.User && item.Role != ConversationRole.Assistant));
        return ordered;
    }

    private static bool TryBuildTurnConversationMessage(JsonElement item, out ConversationMessage message)
    {
        message = default!;

        var itemType = Normalize(ReadString(item, "type"));
        var normalizedItemType = NormalizeThreadTurnItemType(itemType);
        var contentItems = normalizedItemType == "usermessage"
            ? ParseThreadUserInputs(item)
            : Array.Empty<AgentUserInput>();
        var text = normalizedItemType switch
        {
            "usermessage" => ExtractConversationText(item, preferTextProperty: false),
            "assistantmessage" or "agentmessage" => ExtractConversationText(item, preferTextProperty: true),
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        message = new ConversationMessage
        {
            Role = ResolveConversationRole(itemType),
            Content = text,
            ContentItems = contentItems,
            Timestamp = DateTimeOffset.Now,
        };
        return true;
    }

    private static bool TryBuildTurnConversationMessage(AgentThreadTurnItem item, out ConversationMessage message)
    {
        message = default!;

        var itemType = Normalize(item.Type);
        var normalizedItemType = NormalizeThreadTurnItemType(itemType);
        var text = item switch
        {
            UserMessageThreadItem userMessage => ExtractConversationText(userMessage.Content),
            AgentMessageThreadItem agentMessage => Normalize(agentMessage.MessageText),
            _ => normalizedItemType switch
            {
                "usermessage" => Normalize(item.Text),
                "assistantmessage" or "agentmessage" => Normalize(item.Text),
                _ => null,
            },
        };
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        message = new ConversationMessage
        {
            Role = ResolveConversationRole(itemType),
            Content = text,
            ContentItems = item is UserMessageThreadItem userThreadItem ? userThreadItem.Content : Array.Empty<AgentUserInput>(),
            Timestamp = DateTimeOffset.Now,
        };
        return true;
    }

    private static string? ExtractConversationText(IReadOnlyList<AgentUserInput> content)
    {
        if (content.Count == 0)
        {
            return null;
        }

        var parts = content
            .OfType<TextUserInput>()
            .Select(static item => Normalize(item.Text))
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToArray();
        return parts.Length == 0 ? null : string.Join(Environment.NewLine, parts);
    }

    private static IReadOnlyList<AgentUserInput> CreateTextUserInputs(string? text)
    {
        var normalized = Normalize(text);
        return string.IsNullOrWhiteSpace(normalized)
            ? Array.Empty<AgentUserInput>()
            : new AgentUserInput[]
            {
                new TextUserInput
                {
                    Type = "text",
                    Text = normalized!,
                },
            };
    }

    private static IReadOnlyList<AgentUserInput> NormalizeUserInputs(IReadOnlyList<AgentUserInput>? userInputs)
    {
        if (userInputs is null || userInputs.Count == 0)
        {
            return Array.Empty<AgentUserInput>();
        }

        var normalized = new List<AgentUserInput>(userInputs.Count);
        foreach (var input in userInputs)
        {
            switch (input)
            {
                case TextUserInput text:
                {
                    var normalizedText = Normalize(text.Text);
                    if (string.IsNullOrWhiteSpace(normalizedText))
                    {
                        break;
                    }

                    var textElements = text.TextElements
                        .Where(static element => element is not null)
                        .Select(static element => new AgentTextElement
                        {
                            ByteRange = new AgentByteRange
                            {
                                Start = Math.Max(0, element.ByteRange.Start),
                                End = Math.Max(0, element.ByteRange.End),
                            },
                            Placeholder = Normalize(element.Placeholder),
                        })
                        .ToArray();
                    normalized.Add(new TextUserInput
                    {
                        Type = string.IsNullOrWhiteSpace(input.Type) ? "text" : input.Type,
                        Text = normalizedText!,
                        TextElements = textElements,
                    });
                    break;
                }
                case ImageUserInput image:
                {
                    var normalizedUrl = Normalize(image.Url);
                    if (string.IsNullOrWhiteSpace(normalizedUrl))
                    {
                        break;
                    }

                    normalized.Add(new ImageUserInput
                    {
                        Type = string.IsNullOrWhiteSpace(input.Type) ? "image" : input.Type,
                        Url = normalizedUrl!,
                    });
                    break;
                }
                case LocalImageUserInput image:
                {
                    var normalizedPath = Normalize(image.Path);
                    if (string.IsNullOrWhiteSpace(normalizedPath))
                    {
                        break;
                    }

                    normalized.Add(new LocalImageUserInput
                    {
                        Type = string.IsNullOrWhiteSpace(input.Type) ? "localImage" : input.Type,
                        Path = normalizedPath!,
                    });
                    break;
                }
                case SkillUserInput skill:
                {
                    var normalizedName = Normalize(skill.Name);
                    var normalizedPath = Normalize(skill.Path);
                    if (string.IsNullOrWhiteSpace(normalizedName) || string.IsNullOrWhiteSpace(normalizedPath))
                    {
                        break;
                    }

                    normalized.Add(new SkillUserInput
                    {
                        Type = string.IsNullOrWhiteSpace(input.Type) ? "skill" : input.Type,
                        Name = normalizedName!,
                        Path = normalizedPath!,
                    });
                    break;
                }
                case MentionUserInput mention:
                {
                    var normalizedName = Normalize(mention.Name);
                    var normalizedPath = Normalize(mention.Path);
                    if (string.IsNullOrWhiteSpace(normalizedName) || string.IsNullOrWhiteSpace(normalizedPath))
                    {
                        break;
                    }

                    normalized.Add(new MentionUserInput
                    {
                        Type = string.IsNullOrWhiteSpace(input.Type) ? "mention" : input.Type,
                        Name = normalizedName!,
                        Path = normalizedPath!,
                    });
                    break;
                }
            }
        }

        return normalized;
    }

    private static bool AreEquivalentUserInputs(
        IReadOnlyList<AgentUserInput>? left,
        IReadOnlyList<AgentUserInput>? right)
    {
        var normalizedLeft = NormalizeUserInputs(left);
        var normalizedRight = NormalizeUserInputs(right);
        if (normalizedLeft.Count == 0 || normalizedRight.Count == 0 || normalizedLeft.Count != normalizedRight.Count)
        {
            return false;
        }

        for (var index = 0; index < normalizedLeft.Count; index++)
        {
            if (!AreEquivalentUserInput(normalizedLeft[index], normalizedRight[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreEquivalentUserInput(AgentUserInput left, AgentUserInput right)
    {
        if (!string.Equals(NormalizeUserInputType(left.Type), NormalizeUserInputType(right.Type), StringComparison.Ordinal))
        {
            return false;
        }

        return (left, right) switch
        {
            (TextUserInput leftText, TextUserInput rightText)
                => string.Equals(Normalize(leftText.Text), Normalize(rightText.Text), StringComparison.Ordinal)
                    && AreEquivalentTextElements(leftText.TextElements, rightText.TextElements),
            (ImageUserInput leftImage, ImageUserInput rightImage)
                => string.Equals(Normalize(leftImage.Url), Normalize(rightImage.Url), StringComparison.Ordinal),
            (LocalImageUserInput leftLocalImage, LocalImageUserInput rightLocalImage)
                => string.Equals(Normalize(leftLocalImage.Path), Normalize(rightLocalImage.Path), StringComparison.Ordinal),
            (SkillUserInput leftSkill, SkillUserInput rightSkill)
                => string.Equals(Normalize(leftSkill.Name), Normalize(rightSkill.Name), StringComparison.Ordinal)
                    && string.Equals(Normalize(leftSkill.Path), Normalize(rightSkill.Path), StringComparison.Ordinal),
            (MentionUserInput leftMention, MentionUserInput rightMention)
                => string.Equals(Normalize(leftMention.Name), Normalize(rightMention.Name), StringComparison.Ordinal)
                    && string.Equals(Normalize(leftMention.Path), Normalize(rightMention.Path), StringComparison.Ordinal),
            _ => false,
        };
    }

    private static bool AreEquivalentTextElements(
        IReadOnlyList<AgentTextElement>? left,
        IReadOnlyList<AgentTextElement>? right)
    {
        var normalizedLeft = left ?? Array.Empty<AgentTextElement>();
        var normalizedRight = right ?? Array.Empty<AgentTextElement>();
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

    private static string? BuildUserInputPreview(IReadOnlyList<AgentUserInput> userInputs)
        => Normalize(AgentThreadTurnItemTextFormatter.FormatUserInputs(userInputs));

    private static int CountUserInputImages(IReadOnlyList<AgentUserInput> userInputs)
        => userInputs.Count(static input => input is ImageUserInput or LocalImageUserInput);

    private static bool TryBuildTurnConversationMessageLegacyType(string? itemType, string? text, out ConversationMessage message)
    {
        message = default!;

        var normalizedItemType = NormalizeThreadTurnItemType(itemType);
        var normalizedText = normalizedItemType switch
        {
            "usermessage" => Normalize(text),
            "assistantmessage" or "agentmessage" => Normalize(text),
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return false;
        }

        message = new ConversationMessage
        {
            Role = ResolveConversationRole(itemType),
            Content = normalizedText,
            Timestamp = DateTimeOffset.Now,
        };
        return true;
    }

    private static string? ExtractSeedHistoryText(JsonElement item)
        => ExtractConversationText(item, preferTextProperty: false);

    private static string? ExtractConversationText(JsonElement item, bool preferTextProperty)
    {
        if (preferTextProperty)
        {
            var directText = Normalize(ReadString(item, "text"));
            if (!string.IsNullOrWhiteSpace(directText))
            {
                return directText;
            }
        }

        if (item.TryGetProperty("content", out var content))
        {
            var contentText = content.ValueKind switch
            {
                JsonValueKind.String => Normalize(content.GetString()),
                JsonValueKind.Array => ExtractContentPartsText(content),
                JsonValueKind.Object => ExtractContentObjectText(content),
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(contentText))
            {
                return contentText;
            }
        }

        return preferTextProperty
            ? null
            : Normalize(ReadString(item, "text"));
    }

    private static string? ExtractContentPartsText(JsonElement content)
    {
        var parts = new List<string>();
        foreach (var part in content.EnumerateArray())
        {
            var text = ExtractContentObjectText(part);
            if (!string.IsNullOrWhiteSpace(text))
            {
                parts.Add(text);
            }
        }

        return parts.Count == 0 ? null : string.Join(Environment.NewLine, parts);
    }

    private static string? ExtractContentObjectText(JsonElement content)
    {
        return content.ValueKind switch
        {
            JsonValueKind.String => Normalize(content.GetString()),
            JsonValueKind.Object => Normalize(ReadString(content, "text"))
                ?? Normalize(ReadString(content, "value"))
                ?? Normalize(ReadString(content, "content"))
                ?? Normalize(ReadString(content, "inputText"))
                ?? Normalize(ReadString(content, "input_text"))
                ?? Normalize(ReadString(content, "name"))
                ?? Normalize(ReadString(content, "path"))
                ?? Normalize(ReadString(content, "url"))
                ?? Normalize(ReadString(content, "imageUrl"))
                ?? Normalize(ReadString(content, "image_url")),
            _ => null,
        };
    }

    private static ConversationRole ResolveConversationRole(string? itemType)
    {
        if (string.IsNullOrWhiteSpace(itemType))
        {
            return ConversationRole.System;
        }

        if (itemType.Contains("user", StringComparison.OrdinalIgnoreCase))
        {
            return ConversationRole.User;
        }

        if (itemType.Contains("assistant", StringComparison.OrdinalIgnoreCase)
            || itemType.Contains("agent", StringComparison.OrdinalIgnoreCase))
        {
            return ConversationRole.Assistant;
        }

        return ConversationRole.System;
    }

    private static object? ConvertRpcId(JsonElement idElement)
    {
        return idElement.ValueKind switch
        {
            JsonValueKind.Number when idElement.TryGetInt64(out var number) => number,
            JsonValueKind.String => idElement.GetString(),
            _ => idElement.GetRawText(),
        };
    }

    private static string CreateRequestIdToken(object requestIdValue)
        => requestIdValue switch
        {
            long number => number.ToString(CultureInfo.InvariantCulture),
            int number => number.ToString(CultureInfo.InvariantCulture),
            string text => Normalize(text) ?? string.Empty,
            _ => Normalize(requestIdValue.ToString()) ?? string.Empty,
        };

    private static bool TryResolveServerRequestId(
        JsonElement requestIdElement,
        out object requestIdValue,
        out string requestIdToken,
        out long? numericRequestId)
    {
        requestIdValue = string.Empty;
        requestIdToken = string.Empty;
        numericRequestId = null;

        switch (requestIdElement.ValueKind)
        {
            case JsonValueKind.Number when requestIdElement.TryGetInt64(out var number):
                requestIdValue = number;
                requestIdToken = number.ToString(CultureInfo.InvariantCulture);
                numericRequestId = number;
                return true;

            case JsonValueKind.String:
            {
                var text = Normalize(requestIdElement.GetString());
                if (string.IsNullOrWhiteSpace(text))
                {
                    return false;
                }

                requestIdValue = text;
                requestIdToken = text;
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedNumber))
                {
                    numericRequestId = parsedNumber;
                }

                return true;
            }

            default:
                return false;
        }
    }

    private static bool TryConvertRpcRequestId(JsonElement idElement, out long requestId)
    {
        requestId = 0;
        return idElement.ValueKind switch
        {
            JsonValueKind.Number => idElement.TryGetInt64(out requestId),
            JsonValueKind.String => long.TryParse(idElement.GetString(), out requestId),
            _ => false,
        };
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

    private static string[] NormalizeDistinctStrings(IEnumerable<string>? values)
        => values is null
            ? Array.Empty<string>()
            : values
                .Select(Normalize)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

    private sealed record TurnCompletion(
        bool Success,
        string? Status,
        string? AssistantText,
        string? ErrorMessage,
        string? RawJson);

    private sealed record ApprovalContext(string ThreadId, string TurnId, string CallId);

    private sealed record PendingFollowUpCommit(
        string CorrelationId,
        IReadOnlyList<AgentUserInput> Inputs,
        string CompareMessage,
        int ImageCount,
        string? ExpectedTurnId,
        string? TurnId,
        FollowUpMode RequestedMode,
        FollowUpMode EffectiveMode);

    private async Task RequestInterruptAsync(string? threadId, string? turnId, CancellationToken cancellationToken)
    {
        var normalizedThreadId = Normalize(threadId);
        var normalizedTurnId = Normalize(turnId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId) || string.IsNullOrWhiteSpace(normalizedTurnId))
        {
            return;
        }

        var payload = new Dictionary<string, object?>
        {
            ["threadId"] = normalizedThreadId,
            ["turnId"] = normalizedTurnId,
        };

        interruptRequestedTurns[normalizedTurnId!] = 0;
        try
        {
            _ = await SendRpcAsync("turn/interrupt", payload, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            interruptRequestedTurns.TryRemove(normalizedTurnId!, out _);
            throw;
        }
    }

    private void CompleteInterruptedTurnWhenThreadBecomesInactive(string? threadId, string? statusType, string? rawJson)
    {
        if (string.Equals(Normalize(statusType), "active", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var normalizedThreadId = Normalize(threadId);
        var normalizedActiveThreadId = Normalize(activeThreadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId)
            || string.IsNullOrWhiteSpace(normalizedActiveThreadId)
            || !string.Equals(normalizedThreadId, normalizedActiveThreadId, StringComparison.Ordinal))
        {
            return;
        }

        var activeControlTurnId = ResolveCurrentTurnControlId();
        if (string.IsNullOrWhiteSpace(activeControlTurnId))
        {
            return;
        }

        if (interruptRequestedTurns.ContainsKey(activeControlTurnId))
        {
            RemoveObservedTurnForThread(normalizedThreadId, activeControlTurnId);
            ClearTurnScopedPendingInteractiveState(normalizedThreadId, activeControlTurnId);
            CompleteTurn(
                activeControlTurnId,
                success: false,
                status: "interrupted",
                assistantText: null,
                errorMessage: ResolveTurnCompletionErrorMessage("interrupted"),
                rawJson: rawJson);
            return;
        }

        if (!pendingTurns.ContainsKey(activeControlTurnId))
        {
            if (string.Equals(activeTurnId, activeControlTurnId, StringComparison.Ordinal))
            {
                activeTurnId = null;
            }

            if (string.Equals(submittedTurnId, activeControlTurnId, StringComparison.Ordinal))
            {
                submittedTurnId = null;
            }
        }
    }
}
