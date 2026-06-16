using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using TianShu.AppHost.Configuration;
using TianShu.Configuration;
using TianShu.ControlPlane;
using TianShu.Execution.Runtime;
using TianShu.Execution.Runtime.Events;
using TianShu.Contracts.Catalog;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Governance;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Sessions;
using TianShu.Provider.Abstractions;
using TianShu.Cli.Interaction;
using TianShu.Cli.Interaction.Commands;
using TianShu.Cli.Interaction.Commands.Config;
using TianShu.Cli.Interaction.Commands.Governance;
using TianShu.Cli.Interaction.Commands.FollowUp;
using TianShu.Cli.Interaction.Commands.Init;
using TianShu.Cli.Interaction.Commands.Memory;
using TianShu.Cli.Interaction.Commands.Model;
using TianShu.Cli.Interaction.Commands.ModelStatus;
using TianShu.Cli.Interaction.Commands.Rpc;
using TianShu.Cli.Interaction.Commands.State;
using TianShu.Cli.Interaction.Commands.Threads;
using TianShu.Cli.Interaction.Commands.Wait;
using TianShu.Cli.Interaction.Orchestration;
using TianShu.Cli.Interaction.Presenters;
using TianShu.Cli.Interaction.Projection;
using TianShu.RuntimeComposition;
using TianShu.Cli.Interaction.Recording;
using TianShu.Cli.Interaction.Rendering;
using TianShu.Cli.Terminal;
using TianShu.Cli;

namespace TianShu.Cli.Interaction.Host;

internal sealed partial class InteractiveChatSessionHost
{
    private readonly Func<IExecutionRuntime> runtimeFactory;
    private readonly object consoleGate = new();
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
    private readonly PendingInteractiveRequestStore pendingInteractiveRequests = new();
    private readonly PendingGuidanceMessageTracker pendingGuidanceMessages = new();
    private readonly ModelStatusTableRenderer modelStatusTableRenderer = new();
    private readonly ModelStatusConsoleWriter modelStatusConsoleWriter;
    private readonly ModelStatusCommandHandler modelStatusCommandHandler;
    private readonly TerminalInteractionHost terminalHost;
    private readonly TerminalChatInputLoop terminalInputLoop = new();
    private readonly TerminalInputHistoryStore inputHistoryStore = new();
    private readonly ChatOutputWriter chatOutputWriter;
    private InteractionPipeline presentationPipeline = new();
    private readonly ChatSlashCommandDispatcher slashCommandDispatcher = new();
    private readonly InteractiveGovernanceCommandHandler governanceCommandHandler = new();
    private readonly InteractiveWaitCommandHandler waitCommandHandler = new();
    private readonly InteractiveStateCommandHandler stateCommandHandler = new();
    private readonly InteractiveConfigCommandHandler configCommandHandler = new();
    private readonly InteractiveModelCommandHandler modelCommandHandler = new();
    private readonly InteractiveMemoryCommandHandler memoryCommandHandler = new();
    private readonly InteractiveRpcCommandHandler rpcCommandHandler = new();
    private readonly ChatInteractionRecorder interactionRecorder;
    private readonly ChatSessionState sessionState = new();
    private readonly ConversationActivityTracker conversationActivity = new();
    private readonly ConversationEventWaiter eventWaiter = new();
    private readonly RestoredFollowUpCoordinator restoredFollowUps = new();
    private readonly QueuedFollowUpDockStore queuedFollowUpDockStore = new();
    private readonly PendingInteractiveReplayCoordinator pendingInteractiveReplay = new();
    private readonly PendingInteractiveAutomationHandler pendingInteractiveAutomation = new();
    private readonly ChatStreamEventConsumer streamEventConsumer = new();
    private readonly ThreadResumeCoordinator threadResumeCoordinator = new();
    private readonly ConversationOperationCoordinator conversationOperationCoordinator = new();
    private readonly InteractiveFollowUpCommandHandler followUpCommandHandler = new();
    private ChatOutputProtocol outputProtocol = ChatOutputProtocol.Human;
    private int scriptFailureCount;
    private int failureCount;
    private bool assistantLineOpen;
    private bool scriptMode;
    private string? lastCompletedAssistantText;
    private PlanDockSummary? currentPlanDockSummary;
    private ModelDockSummary? currentModelDockSummary;
    private bool assistantLeadingSpacerPending;
    private string? inputNotice;
    private Func<CancellationToken, Task<string?>>? readBlockingConfirmationAsync;
    private readonly AgentsGuideInitializer agentsGuideInitializer = new();
    private readonly InteractiveThreadCommandHandler threadCommandHandler = new();
    private readonly StartupThreadSelector startupThreadSelector = new();

    private sealed record DecodedLinkedInputs(
        string Text,
        IReadOnlyList<ControlPlaneInputItem> LinkedInputs);

    internal InteractiveChatSessionHost()
        : this(TianShuAppHostRuntimeClientFactory.Create)
    {
    }

    internal InteractiveChatSessionHost(Func<IExecutionRuntime> runtimeFactory)
    {
        this.runtimeFactory = runtimeFactory ?? throw new ArgumentNullException(nameof(runtimeFactory));
        modelStatusCommandHandler = new ModelStatusCommandHandler(
            modelStatusTableRenderer,
            LoadResolvedConfig,
            GetCurrentSessionThreadId);
        interactionRecorder = new ChatInteractionRecorder(jsonOptions);
        terminalHost = new TerminalInteractionHost(
            consoleGate,
            () => outputProtocol == ChatOutputProtocol.Human,
            () => scriptMode,
            IsConversationBusy,
            () => currentModelDockSummary ?? new ModelDockSummary(sessionState.CurrentDisplayModel),
            () => currentPlanDockSummary,
            queuedFollowUpDockStore.Capture,
            () => inputNotice,
            () => conversationActivity.WorkingElapsed,
            () => presentationPipeline,
            () => assistantLineOpen,
            TerminalConsoleRefreshScope.HideCursorForRefresh);
        chatOutputWriter = new ChatOutputWriter(new ChatOutputWriterContext
        {
            ConsoleGate = consoleGate,
            TerminalHost = terminalHost,
            GetPresentationPipeline = () => presentationPipeline,
            GetOutputProtocol = () => outputProtocol,
            IsScriptMode = () => scriptMode,
            GetCurrentPlanDockSummary = () => currentPlanDockSummary,
            AppendTranscript = AppendTranscript,
            MarkFailure = MarkFailure,
            SetLastFailureMessage = sessionState.SetLastFailureMessage,
            GetAssistantLineOpen = () => assistantLineOpen,
            SetAssistantLineOpen = value => assistantLineOpen = value,
            SetLastCompletedAssistantText = value => lastCompletedAssistantText = value,
            SetAssistantLeadingSpacerPending = value => assistantLeadingSpacerPending = value,
        });
        modelStatusConsoleWriter = new ModelStatusConsoleWriter(
            consoleGate,
            modelStatusTableRenderer,
            clearLine => StopTerminalWaitingPlaceholderUnsafe(clearLine),
            row => WriteControlPlaneLine(row),
            terminalHost.BeginCommandOverlay,
            terminalHost.SetCommandOverlayLines);
    }

    public async Task<int> RunAsync(ChatCommandOptions options, CancellationToken cancellationToken)
    {
        outputProtocol = options.OutputProtocol;
        ResetSessionState();

        var startedAt = DateTimeOffset.Now;
        ChatScriptCommandFile? script = null;
        CliRuntimeBootstrapResult? bootstrap = null;
        IExecutionRuntime? runtime = null;
        var useNativeTerminalChat = false;
        string? failureMessage = null;
        string? finalThreadId = null;
        var exitCode = 1;

        ProbePermissionRequestScript? permissionScript = null;
        if (!string.IsNullOrWhiteSpace(options.PermissionsJsonPath))
        {
            permissionScript = ProbePermissionRequestScript.Load(options.PermissionsJsonPath);
        }

        ProbeUserInputScript? userInputScript = null;
        if (!string.IsNullOrWhiteSpace(options.UserInputJsonPath))
        {
            userInputScript = ProbeUserInputScript.Load(options.UserInputJsonPath);
        }

        try
        {
            script = ChatScriptCommandFile.Load(options.ScriptPath);
            scriptMode = script is not null;
            bootstrap = CliRuntimeBootstrapper.Prepare(options);
            options.RuntimeModel = Normalize(options.RuntimeModel);
            options.RuntimeModelProvider = Normalize(options.RuntimeModelProvider);
            options.RuntimeProviderWireApi = Normalize(options.RuntimeProviderWireApi);
            ResolveCurrentModelForDock(options);
            runtime = runtimeFactory();
            using var cancelInterceptor = ConsoleCancelInterceptor.Register(() => TryHandleConsoleCancel(runtime));
            runtime.StreamEventReceived += (_, args) => OnStreamEvent(runtime, options, permissionScript, userInputScript, args.StreamEvent, cancellationToken);

            await runtime.InitializeAsync(bootstrap.RuntimeOptions, dynamicToolCallHandler: null, cancellationToken).ConfigureAwait(false);
            var startupActionOutcome = await TryExecuteStartupThreadActionAsync(
                runtime,
                options,
                permissionScript,
                userInputScript,
                cancellationToken).ConfigureAwait(false);
            if (startupActionOutcome == StartupThreadActionOutcome.Cancelled)
            {
                return 0;
            }

            if (startupActionOutcome == StartupThreadActionOutcome.Failed)
            {
                return 1;
            }

            await TryConsumeStartupResumedThreadStateAsync(runtime, options, permissionScript, userInputScript, cancellationToken).ConfigureAwait(false);
            await RefreshSessionSnapshotAsync(runtime, cancellationToken).ConfigureAwait(false);
            useNativeTerminalChat = ShouldUseTerminalChatTui(options);
            if (useNativeTerminalChat)
            {
                WriteNativeTerminalStartupBanner(options);
            }
            else
            {
                WriteLine("天枢 TianShu 已启动。");
                WriteLine("输入普通文本将发送消息，输入 !命令 执行本地 shell，输入 /help 查看命令。\n");
            }

            var initialUserInputs = BuildInitialUserInputs(options);
            if (initialUserInputs.Count > 0)
            {
                if (!string.IsNullOrWhiteSpace(options.InitialMessage))
                {
                    RecordExecutedInput(options.InitialMessage!);
                    WriteUserMessageBlock(options.InitialMessage!);
                }

                if (initialUserInputs.Count == 1 && initialUserInputs[0] is ControlPlaneTextInput textInput)
                {
                    StartConversationOperation(
                        runtime,
                        options,
                        label: "send",
                        () => SubmitTurnWithSelectedModelAsync(
                            runtime,
                            options,
                            [new ControlPlaneTextInput(textInput.Text)],
                            cancellationToken),
                        cancellationToken);
                }
                else
                {
                    StartConversationOperation(
                        runtime,
                        options,
                        label: "send",
                        () => SubmitTurnWithSelectedModelAsync(runtime, options, initialUserInputs, cancellationToken),
                        cancellationToken);
                }
            }

            if (script is not null)
            {
                var exitRequested = false;
                foreach (var line in script.Commands)
                {
                    var shouldExit = await ExecuteInputLineAsync(runtime, options, permissionScript, userInputScript, line, cancellationToken).ConfigureAwait(false);
                    if (shouldExit)
                    {
                        exitRequested = true;
                        break;
                    }
                }

                if (!exitRequested)
                {
                    if (Volatile.Read(ref failureCount) > 0)
                    {
                        exitCode = 1;
                    }
                    else
                    {
                        var drained = await WaitForIdleAsync(runtime, TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
                        if (!drained)
                        {
                            WriteLine(BuildPendingConversationMessage("脚本执行结束，但仍有未完成回合。", runtime), isError: true);
                            exitCode = 1;
                        }
                        else
                        {
                            exitCode = Volatile.Read(ref failureCount) == 0 ? 0 : 1;
                        }
                    }
                }
                else
                {
                    exitCode = Volatile.Read(ref failureCount) == 0 ? 0 : 1;
                }
            }
            else
            {
                var exitRequested = false;
                if (useNativeTerminalChat)
                {
                    exitRequested = await RunTianShuTerminalChatAsync(
                            runtime,
                            options,
                            permissionScript,
                            userInputScript,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        WritePrompt();
                        var line = await ReadConsoleLineAsync(cancellationToken).ConfigureAwait(false);
                        if (line is null)
                        {
                            break;
                        }

                        var shouldExit = await ExecuteInputLineAsync(runtime, options, permissionScript, userInputScript, line, cancellationToken).ConfigureAwait(false);

                        if (shouldExit)
                        {
                            exitRequested = true;
                            break;
                        }
                    }
                }

                if (!exitRequested && HasRunningConversation(runtime))
                {
                    var drained = await WaitForIdleAsync(runtime, TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
                    if (!drained)
                    {
                        WriteLine(BuildPendingConversationMessage("输入流已结束，但仍有未完成回合。", runtime), isError: true);
                        exitCode = 1;
                    }
                }

                if (exitCode != 1 || Volatile.Read(ref failureCount) == 0)
                {
                    exitCode = Volatile.Read(ref failureCount) == 0 ? 0 : 1;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            failureMessage = ex.Message;
            WriteLine($"chat 执行失败：{ex.Message}", isError: true);
            exitCode = 1;
        }
        finally
        {
            StopTerminalWaitingPlaceholder(clearLine: false);
            if (runtime is not null)
            {
                await RefreshSessionSnapshotAsync(runtime, CancellationToken.None).ConfigureAwait(false);
                finalThreadId = GetCurrentSessionThreadId();
                await runtime.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                finalThreadId = GetCurrentSessionThreadId();
            }
        }

        if (bootstrap is not null && !string.IsNullOrWhiteSpace(options.ArtifactsRoot))
        {
            await WriteArtifactsAsync(
                    options,
                    bootstrap,
                    script,
                    startedAt,
                    exitCode,
                    failureMessage,
                    finalThreadId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return exitCode;
    }

    private Task<bool> RunTianShuTerminalChatAsync(
        IExecutionRuntime runtime,
        ChatCommandOptions options,
        ProbePermissionRequestScript? permissionScript,
        ProbeUserInputScript? userInputScript,
        CancellationToken cancellationToken)
        => RunTianShuTerminalChatCoreAsync(runtime, options, permissionScript, userInputScript, cancellationToken);

    private static bool ShouldShowTerminalWaitingPlaceholder(string line)
        => TerminalInteractionHost.ShouldShowWaitingPlaceholder(line);

    private void StartTerminalWaitingPlaceholder()
        => terminalHost.StartWaitingPlaceholder();

    private void StopTerminalWaitingPlaceholder(bool clearLine)
        => terminalHost.StopWaitingPlaceholder(clearLine);

    private void StopTerminalWaitingPlaceholderUnsafe(bool clearLine)
        => terminalHost.StopWaitingPlaceholderUnsafe(clearLine);

    private void WriteNativeTerminalStartupBanner(ChatCommandOptions options)
    {
        var plainBanner = TerminalStartupBanner.Build(options).TrimEnd();
        var styledBanner = TerminalStartupBanner.BuildStyled(options).TrimEnd();
        WriteDisplayLine(plainBanner, styledBanner);
        WriteDisplayLine(string.Empty, string.Empty);
    }

    private async Task<bool> ExecuteSlashCommandAsync(
        IExecutionRuntime runtime,
        ChatCommandOptions options,
        ProbePermissionRequestScript? permissionScript,
        ProbeUserInputScript? userInputScript,
        string line,
        CancellationToken cancellationToken)
    {
        using var controlOutputScope = chatOutputWriter.BeginControlOutputScope(buffered: true, queueExternalOutput: false);
        return await slashCommandDispatcher.ExecuteAsync(
                line,
                CreateSlashCommandContext(runtime, options, permissionScript, userInputScript),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private ChatSlashCommandContext CreateSlashCommandContext(
        IExecutionRuntime runtime,
        ChatCommandOptions options,
        ProbePermissionRequestScript? permissionScript,
        ProbeUserInputScript? userInputScript)
        => new(
            PrintHelp,
            async token =>
            {
                if (HasRunningConversation(runtime))
                {
                    RememberUserRequestedInterrupt(sessionState.LastObservedTurnId);
                }

                await TianShuControlPlaneClientFactory.Create(runtime).Conversations.InterruptTurnAsync(token).ConfigureAwait(false);
                WriteLine("已请求中断当前回合，等待确认。");
            },
            (rest, token) => followUpCommandHandler.HandleFollowUpAsync(CreateFollowUpCommandContext(runtime, options), rest, token),
            token =>
            {
                HandleInitCommand(runtime, options, token);
                return Task.CompletedTask;
            },
            () => followUpCommandHandler.HandleRestoredDraftState(CreateFollowUpCommandContext(runtime, options)),
            token =>
            {
                followUpCommandHandler.HandleSendRestoredFollowUp(CreateFollowUpCommandContext(runtime, options), token);
                return Task.CompletedTask;
            },
            () => followUpCommandHandler.HandleDropRestoredFollowUp(CreateFollowUpCommandContext(runtime, options)),
            (rest, decision, token) => governanceCommandHandler.HandleApprovalAsync(runtime, pendingInteractiveRequests, rest, decision, (text, isError) => WriteLine(text, isError), token),
            (rest, token) => governanceCommandHandler.HandlePermissionRequestAsync(runtime, pendingInteractiveRequests, rest, (text, isError) => WriteLine(text, isError), token),
            (rest, token) => governanceCommandHandler.HandleUserInputAsync(runtime, pendingInteractiveRequests, rest, (text, isError) => WriteLine(text, isError), token),
            (rest, token) => HandleThreadsCommandAsync(runtime, options, permissionScript, userInputScript, rest, token),
            (rest, token) => HandleThreadLifecycleCommandAsync(runtime, rest, token),
            (rest, token) => modelCommandHandler.HandleModelCommandAsync(runtime, options, rest, BuildInteractiveModelCommandContext(runtime, options), token),
            (rest, token) => configCommandHandler.HandleConfigCommandAsync(runtime, options, rest, BuildInteractiveConfigCommandContext(runtime, options), token),
            (rest, token) => configCommandHandler.HandleConfigReloadCommandAsync(runtime, options, rest, BuildInteractiveConfigCommandContext(runtime, options), token),
            token => HandleNewThreadAsync(runtime, options, token),
            (rest, token) => HandleForkThreadAsync(runtime, rest, token),
            (rest, token) => HandleArchiveThreadAsync(runtime, rest, token),
            (rest, token) => HandleRenameThreadAsync(runtime, rest, token),
            (rest, token) => HandleResumeThreadAsync(runtime, options, permissionScript, userInputScript, rest, token),
            (rest, token) => memoryCommandHandler.HandleMemoryCommandAsync(runtime, rest, new InteractiveMemoryCommandContext(jsonOptions, (message, isError) => WriteLine(message, isError)), token),
            (rest, token) => rpcCommandHandler.HandleRpcAsync(runtime, rest, jsonOptions, (message, isError) => WriteLine(message, isError), token),
            token => stateCommandHandler.HandleStateCommandAsync(runtime, BuildInteractiveStateCommandContext(options, permissionScript, userInputScript), token),
            (rest, token) => waitCommandHandler.HandleWaitAsync(rest, (text, isError) => WriteLine(text, isError), token),
            (rest, token) => waitCommandHandler.HandleWaitEventAsync(eventWaiter, rest, FormatVerboseEvent, (text, isError) => WriteLine(text, isError), token),
            (rest, token) => waitCommandHandler.HandleWaitNextToolCallAsync(eventWaiter, rest, FormatVerboseEvent, (text, isError) => WriteLine(text, isError), token),
            (rest, token) => waitCommandHandler.HandleWaitCompleteAsync(eventWaiter, rest, refreshToken => RefreshSessionSnapshotAsync(runtime, refreshToken), () => IsConversationIdle(runtime), (text, isError) => WriteLine(text, isError), token),
            (message, isError) => WriteLine(message, isError));

    private InteractiveStateCommandContext BuildInteractiveStateCommandContext(
        ChatCommandOptions options,
        ProbePermissionRequestScript? permissionScript,
        ProbeUserInputScript? userInputScript)
        => new(
            options,
            pendingInteractiveRequests,
            restoredFollowUps,
            permissionScript,
            userInputScript,
            jsonOptions,
            ApplySessionSnapshot,
            (message, isError) => WriteLine(message, isError));

    private async Task<bool> ExecuteInputLineAsync(
        IExecutionRuntime runtime,
        ChatCommandOptions options,
        ProbePermissionRequestScript? permissionScript,
        ProbeUserInputScript? userInputScript,
        string line,
        CancellationToken cancellationToken,
        ControlPlaneFollowUpMode? runningPlainTextMode = null)
    {
        var pipeline = new InteractionPipeline(new ChatCommandExecutionContext(
            RecordExecutedInput,
            WriteUserMessageBlock,
            (input, token) => ExecuteSlashCommandAsync(runtime, options, permissionScript, userInputScript, input, token),
            (input, token) => TryExecutePlainThreadLifecycleInputAsync(runtime, input, token),
            (input, token) => TryExecuteShellInputAsync(runtime, options, input, token),
            (input, mode, token) => followUpCommandHandler.TryExecuteRunningFollowUpInputAsync(CreateFollowUpCommandContext(runtime, options), input, mode, token),
            (input, token) => followUpCommandHandler.TryExecuteRestoredDraftInputAsync(CreateFollowUpCommandContext(runtime, options), input, token),
            (input, token) => ExecuteNewTurnInputAsync(runtime, options, input, token)));

        return await pipeline.ExecuteInputLineAsync(line, cancellationToken, runningPlainTextMode).ConfigureAwait(false);
    }

    private async Task<bool> TryExecutePlainThreadLifecycleInputAsync(
        IExecutionRuntime runtime,
        string trimmed,
        CancellationToken cancellationToken)
    {
        if (!InteractiveThreadCommandHandler.TryReadPlainThreadLifecycleCommand(trimmed, out var threadCommandRest))
        {
            return false;
        }

        using var controlOutputScope = chatOutputWriter.BeginControlOutputScope(buffered: true, queueExternalOutput: false);
        await HandleThreadLifecycleCommandAsync(runtime, threadCommandRest, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private Task<bool> TryExecuteShellInputAsync(
        IExecutionRuntime runtime,
        ChatCommandOptions options,
        string trimmed,
        CancellationToken cancellationToken)
    {
        if (!trimmed.StartsWith("!", StringComparison.Ordinal))
        {
            return Task.FromResult(false);
        }

        var command = trimmed[1..].Trim();
        if (command.Length == 0)
        {
            WriteLine("! 后面必须跟 shell 命令。", isError: true);
            return Task.FromResult(true);
        }

        StartConversationOperation(
            runtime,
            options,
            label: "user-shell",
            () => runtime.RunUserShellCommandAsync(command, cancellationToken),
            cancellationToken);
        return Task.FromResult(true);
    }

    private Task ExecuteNewTurnInputAsync(
        IExecutionRuntime runtime,
        ChatCommandOptions options,
        string trimmed,
        CancellationToken cancellationToken)
    {
        var userInputs = BuildStructuredUserInputsFromText(trimmed);
        StartConversationOperation(
            runtime,
            options,
            label: "send",
            () => SubmitTurnWithSelectedModelAsync(runtime, options, userInputs, cancellationToken),
            cancellationToken);
        return Task.CompletedTask;
    }

    private InteractiveFollowUpCommandContext CreateFollowUpCommandContext(
        IExecutionRuntime runtime,
        ChatCommandOptions options)
        => new(
            restoredFollowUps,
            jsonOptions,
            () => HasSteerableConversation(runtime),
            () => HasRunningConversation(runtime),
            GetCurrentSessionThreadId(),
            sessionState.LastObservedTurnId,
            RememberUserRequestedInterrupt,
            (kind, threadId, turnId, message, text) => RecordSyntheticEvent(kind, threadId, turnId, message, text),
            (inputs, mode, token, correlationId) => SubmitFollowUpAsync(runtime, inputs, mode, token, correlationId),
            (correlationId, kind, token) => MutatePendingFollowUpAsync(runtime, correlationId, kind, token),
            (correlationId, text) =>
            {
                queuedFollowUpDockStore.Add(correlationId, text);
                RefreshAndRestoreInlineTailPrompt(runtime, options);
            },
            correlationId =>
            {
                if (queuedFollowUpDockStore.Remove(correlationId))
                {
                    RefreshAndRestoreInlineTailPrompt(runtime, options);
                }
            },
            index => queuedFollowUpDockStore.TryGetByIndex(index),
            TrackPendingGuidance,
            (label, operation, token, onResult, onCancelled, onException) => StartConversationOperation(
                runtime,
                options,
                label,
                () => operation(token),
                token,
                onResult,
                onCancelled,
                onException,
                clearPlanDockOnStart: false),
            IsTerminalTurnStatus,
            WriteUserMessageBlock,
            (message, isError, countAsFailure) => WriteControlOutputLine(message, isError, isError && countAsFailure));

    private static IReadOnlyList<ControlPlaneInputItem> BuildInitialUserInputs(ChatCommandOptions options)
        => CliConversationInputUtilities.BuildTextAndImageInputs(options.ImagePaths, options.InitialMessage);

    private void OnStreamEvent(
        IExecutionRuntime runtime,
        ChatCommandOptions options,
        ProbePermissionRequestScript? permissionScript,
        ProbeUserInputScript? userInputScript,
        ControlPlaneConversationStreamEvent streamEvent,
        CancellationToken cancellationToken)
        => streamEventConsumer.Handle(
            new ChatStreamEventConsumerContext
            {
                Runtime = runtime,
                Options = options,
                PermissionScript = permissionScript,
                UserInputScript = userInputScript,
                SessionState = sessionState,
                ConversationActivity = conversationActivity,
                InteractionRecorder = interactionRecorder,
                EventWaiter = eventWaiter,
                PendingInteractiveRequests = pendingInteractiveRequests,
                PendingInteractiveAutomation = pendingInteractiveAutomation,
                TouchConversationActivity = TouchConversationActivity,
                RecordPresentationProjection = RecordPresentationProjection,
                WriteProjectionCommittedBlocks = (blocks, countErrorsAsFailure) => WriteProjectionCommittedBlocks(runtime, options, blocks, countErrorsAsFailure),
                WriteVerboseToolEventIfRequested = streamEvent => WriteVerboseToolEventIfRequested(options, streamEvent),
                WriteVerboseEventIfRequested = streamEvent =>
                {
                    if (options.VerboseEvents)
                    {
                        WriteVerboseOrImportant(options, FormatVerboseEvent(streamEvent), important: false);
                    }
                },
                WriteLine = (message, isError) => WriteLine(message, isError),
                Write = Write,
                WriteTerminalVisualSpacerLine = WriteTerminalVisualSpacerLine,
                CloseAssistantLineIfOpen = () => CloseAssistantLineIfOpen(runtime, options),
                RefreshInlineTailPrompt = () => RefreshAndRestoreInlineTailPrompt(runtime, options),
                ClearPendingInteractiveRequest = ClearPendingInteractiveRequest,
                ClearPendingInteractiveRequestsForTurn = ClearPendingInteractiveRequestsForTurn,
                WriteLifecycleDebug = message => WriteVerboseOrDebug(options, message, CliTranscriptRecordKind.LifecycleDebug),
                IsFailedTurnStatus = IsFailedTurnStatus,
                MarkFailure = MarkFailure,
                TryWriteCommittedGuidanceMessage = TryWriteCommittedGuidanceMessage,
                TrackPendingGuidanceMessage = TrackPendingGuidanceMessage,
                TryPromotePendingRestoredFollowUpAfterTurnCompleted = streamEvent =>
                {
                    if (queuedFollowUpDockStore.Clear())
                    {
                        RefreshAndRestoreInlineTailPrompt(runtime, options);
                    }

                    followUpCommandHandler.TryPromotePendingRestoredFollowUpAfterTurnCompleted(
                        CreateFollowUpCommandContext(runtime, options),
                        streamEvent.TurnId?.Value);
                },
                ClearPlanDockState = ClearCurrentPlanDockState,
                WriteInterruptedControlOutput = WriteInterruptedControlOutput,
                GetAssistantLineOpen = () => assistantLineOpen,
                SetAssistantLineOpen = value => assistantLineOpen = value,
                GetAssistantLeadingSpacerPending = () => assistantLeadingSpacerPending,
                SetAssistantLeadingSpacerPending = value => assistantLeadingSpacerPending = value,
                HasRetainedTailFrame = () => terminalHost.HasRetainedTailFrame,
            },
            streamEvent,
            cancellationToken);

    private void CloseAssistantLineIfOpen(IExecutionRuntime runtime, ChatCommandOptions options)
        => chatOutputWriter.CloseAssistantLineIfOpen();

    private void WriteVerboseToolEventIfRequested(ChatCommandOptions options, ControlPlaneConversationStreamEvent streamEvent)
    {
        if (options.VerboseEvents)
        {
            WriteVerboseOrImportant(options, FormatVerboseEvent(streamEvent), important: false);
        }
    }

    private void WriteErrorLineOnce(string message, bool countAsFailure = true)
        => chatOutputWriter.WriteErrorLineOnce(message, countAsFailure);

    private void ClearPendingInteractiveRequest(string callId)
        => pendingInteractiveRequests.Remove(callId);

    private void ClearPendingInteractiveRequestsForTurn(string? threadId, string? turnId)
        => pendingInteractiveRequests.ClearForTurn(threadId, turnId);

    private void TrackPendingGuidance(string correlationId, string text)
        => pendingGuidanceMessages.Track(correlationId, text);

    private void TrackPendingGuidanceMessage(ControlPlaneConversationStreamEvent streamEvent)
        => pendingGuidanceMessages.TrackPendingState(streamEvent);

    private void TryWriteCommittedGuidanceMessage(ControlPlaneConversationStreamEvent streamEvent)
    {
        if (pendingGuidanceMessages.TryConsumeCommittedMessage(streamEvent, out var message))
        {
            WriteUserMessageBlock(message, "引导成功");
        }
    }

    private void HandleInitCommand(IExecutionRuntime runtime, ChatCommandOptions options, CancellationToken cancellationToken)
    {
        if (HasRunningConversation(runtime))
        {
            WriteLine("'/init' is disabled while a task is in progress.", isError: true);
            return;
        }

        var initRequest = agentsGuideInitializer.BuildRequest(options.WorkingDirectory, cancellationToken);
        if (!initRequest.ShouldSubmitPrompt)
        {
            WriteLine(initRequest.Message ?? "AGENTS.md already exists here. Skipping /init to avoid overwriting it.");
            return;
        }

        StartConversationOperation(
            runtime,
            options,
            label: "send",
            () => SubmitTurnWithSelectedModelAsync(
                runtime,
                options,
                [new ControlPlaneTextInput(initRequest.Prompt ?? AgentsGuideInitializer.InitPrompt)],
                cancellationToken),
            cancellationToken);
    }

    private async Task HandleThreadsCommandAsync(
        IExecutionRuntime runtime,
        ChatCommandOptions options,
        ProbePermissionRequestScript? permissionScript,
        ProbeUserInputScript? userInputScript,
        string rest,
        CancellationToken cancellationToken)
    {
        var archived = rest.Contains("--archived", StringComparison.OrdinalIgnoreCase);
        var showAll = rest.Contains("--all", StringComparison.OrdinalIgnoreCase);
        var threads = await TianShuControlPlaneClientFactory.Create(runtime).Conversations.ListThreadsAsync(
                new ControlPlaneThreadListQuery
                {
                    Limit = 20,
                    Archived = archived,
                    WorkingDirectory = showAll ? null : Directory.GetCurrentDirectory(),
                    SortKey = "updated_at",
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (threads.Threads.Count == 0)
        {
            WriteControlPlaneLine("未找到线程。");
            return;
        }

        if (ShouldUseTerminalThreadPicker()
            && (await StartupThreadSelector.TrySelectThreadWithTianShuTerminalAsync(
                    threads.Threads,
                    showAll,
                    "选择要恢复的线程",
                    cancellationToken,
                    () => chatOutputWriter.BeginExclusiveTerminalFrameScope()).ConfigureAwait(false)) is { } selectedThread)
        {
            await ResumeThreadByIdAsync(
                    runtime,
                    options,
                    permissionScript,
                    userInputScript,
                    selectedThread.ThreadId.Value,
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        foreach (var thread in threads.Threads)
        {
            WriteControlPlaneLine(SelectionPickerRowRenderer.BuildThreadListRow(thread, showAll));
        }
    }

    private InteractiveConfigCommandContext BuildInteractiveConfigCommandContext(IExecutionRuntime runtime, ChatCommandOptions options)
        => new(
            LoadResolvedConfig,
            () => HasRunningConversation(runtime),
            GetCurrentSessionThreadId,
            sessionState.SetSessionActiveThreadId,
            model => SetCurrentDisplayModelForDock(options, model),
            conversationActivity.MarkTerminalTurn,
            (message, isError) => WriteLine(message, isError),
            (message, isError) => WriteControlPlaneLine(message, isError),
            InteractiveConfigCommandHandler.ResolveConfigGuiExecutable,
            InteractiveConfigCommandHandler.StartConfigGuiProcess);

    private InteractiveModelCommandContext BuildInteractiveModelCommandContext(
        IExecutionRuntime runtime,
        ChatCommandOptions options)
        => new(
            (mode, token) => HandleModelStatusCommandAsync(runtime, options, mode, token),
            LoadResolvedConfig,
            ShouldUseTerminalThreadPicker,
            () => HasRunningConversation(runtime),
            GetCurrentSessionThreadId,
            sessionState.SetSessionActiveThreadId,
            model => SetCurrentDisplayModelForDock(options, model),
            conversationActivity.MarkTerminalTurn,
            (message, isError) => WriteControlPlaneLine(message, isError),
            () => chatOutputWriter.BeginExclusiveTerminalFrameScope());

    private async Task HandleModelStatusCommandAsync(
        IExecutionRuntime runtime,
        ChatCommandOptions options,
        ModelStatusMode mode,
        CancellationToken cancellationToken)
    {
        var output = modelStatusConsoleWriter.CreateOutput(ShouldUseStyledControlPlaneOutput(), cancellationToken);
        await modelStatusCommandHandler.HandleAsync(runtime, options, mode, output, cancellationToken).ConfigureAwait(false);
    }

    private InteractiveThreadCommandContext CreateThreadCommandContext(IExecutionRuntime runtime)
        => CreateThreadCommandContext(
            runtime,
            static (_, _) => Task.FromResult<ControlPlaneThreadSnapshot?>(null));

    private InteractiveThreadCommandContext CreateThreadCommandContext(
        IExecutionRuntime runtime,
        ChatCommandOptions options,
        ProbePermissionRequestScript? permissionScript,
        ProbeUserInputScript? userInputScript)
        => CreateThreadCommandContext(
            runtime,
            (threadId, token) => ResumeThreadByIdAsync(runtime, options, permissionScript, userInputScript, threadId, token));

    private InteractiveThreadCommandContext CreateThreadCommandContext(
        IExecutionRuntime runtime,
        Func<string, CancellationToken, Task<ControlPlaneThreadSnapshot?>> resumeThreadByIdAsync)
        => new(
            () => HasRunningConversation(runtime),
            threadId => IsCurrentThread(runtime, threadId),
            ClearCurrentThreadState,
            threadId =>
            {
                sessionState.SetSessionActiveThreadId(threadId);
                conversationActivity.MarkTerminalTurn();
            },
            ConfirmChatDestructiveOperationAsync,
            resumeThreadByIdAsync,
            inputHistoryStore.ClearThread, inputHistoryStore.ClearAll,
            (message, isError) => WriteControlPlaneLine(message, isError));

    private async Task HandleNewThreadAsync(IExecutionRuntime runtime, ChatCommandOptions options, CancellationToken cancellationToken)
    {
        await threadCommandHandler.HandleNewThreadAsync(
                TianShuControlPlaneClientFactory.Create(runtime).Conversations,
                options,
                CreateThreadCommandContext(runtime),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task HandleForkThreadAsync(IExecutionRuntime runtime, string rest, CancellationToken cancellationToken)
    {
        await threadCommandHandler.HandleForkThreadAsync(
                TianShuControlPlaneClientFactory.Create(runtime).Conversations,
                rest,
                CreateThreadCommandContext(runtime),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task HandleArchiveThreadAsync(IExecutionRuntime runtime, string rest, CancellationToken cancellationToken)
    {
        await threadCommandHandler.HandleArchiveThreadAsync(
                TianShuControlPlaneClientFactory.Create(runtime).Conversations,
                rest,
                CreateThreadCommandContext(runtime),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task HandleThreadLifecycleCommandAsync(IExecutionRuntime runtime, string rest, CancellationToken cancellationToken)
    {
        await threadCommandHandler.HandleThreadLifecycleCommandAsync(
                TianShuControlPlaneClientFactory.Create(runtime).Conversations,
                rest,
                CreateThreadCommandContext(runtime),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task HandleRenameThreadAsync(IExecutionRuntime runtime, string rest, CancellationToken cancellationToken)
    {
        await threadCommandHandler.HandleRenameThreadAsync(
                TianShuControlPlaneClientFactory.Create(runtime).Conversations,
                rest,
                CreateThreadCommandContext(runtime),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task HandleResumeThreadAsync(
        IExecutionRuntime runtime,
        ChatCommandOptions options,
        ProbePermissionRequestScript? permissionScript,
        ProbeUserInputScript? userInputScript,
        string rest,
        CancellationToken cancellationToken)
    {
        await threadCommandHandler.HandleResumeThreadAsync(
                rest,
                CreateThreadCommandContext(runtime, options, permissionScript, userInputScript),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ControlPlaneThreadSnapshot?> ResumeThreadByIdAsync(
        IExecutionRuntime runtime,
        ChatCommandOptions options,
        ProbePermissionRequestScript? permissionScript,
        ProbeUserInputScript? userInputScript,
        string threadId,
        CancellationToken cancellationToken)
        => await threadResumeCoordinator.ResumeThreadByIdAsync(
                CreateThreadResumeCoordinatorContext(runtime, options, permissionScript, userInputScript),
                threadId,
                cancellationToken)
            .ConfigureAwait(false);

    private StartupThreadSelectorContext CreateStartupThreadSelectorContext(
        IExecutionRuntime runtime,
        ChatCommandOptions options,
        ProbePermissionRequestScript? permissionScript,
        ProbeUserInputScript? userInputScript)
        => new(
            Directory.GetCurrentDirectory,
            ShouldUseTerminalThreadPicker,
            (threads, includeCwd, title, token) => StartupThreadSelector.TrySelectThreadWithTianShuTerminalAsync(
                threads,
                includeCwd,
                title,
                token,
                () => chatOutputWriter.BeginExclusiveTerminalFrameScope()),
            ReadConsoleLineAsync,
            (resumed, token) => threadResumeCoordinator.ConsumeResumedThreadState(
                CreateThreadResumeCoordinatorContext(runtime, options, permissionScript, userInputScript),
                resumed,
                token),
            () => (pendingInteractiveRequests.ApprovalCount, pendingInteractiveRequests.PermissionCount, pendingInteractiveRequests.UserInputCount),
            (message, isError) => WriteLine(message, isError));

    private async Task<StartupThreadActionOutcome> TryExecuteStartupThreadActionAsync(
        IExecutionRuntime runtime,
        ChatCommandOptions options,
        ProbePermissionRequestScript? permissionScript,
        ProbeUserInputScript? userInputScript,
        CancellationToken cancellationToken)
    {
        return await startupThreadSelector.ExecuteAsync(
                TianShuControlPlaneClientFactory.Create(runtime).Conversations,
                options,
                CreateStartupThreadSelectorContext(runtime, options, permissionScript, userInputScript),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task TryConsumeStartupResumedThreadStateAsync(
        IExecutionRuntime runtime,
        ChatCommandOptions options,
        ProbePermissionRequestScript? permissionScript,
        ProbeUserInputScript? userInputScript,
        CancellationToken cancellationToken)
        => await threadResumeCoordinator.TryConsumeStartupResumedThreadStateAsync(
                CreateThreadResumeCoordinatorContext(runtime, options, permissionScript, userInputScript),
                shouldConsumeStartupResumedThread: !string.IsNullOrWhiteSpace(Normalize(options.ResumeThreadId)) || options.ResumeLatestThread,
                cancellationToken)
            .ConfigureAwait(false);

    private ThreadResumeCoordinatorContext CreateThreadResumeCoordinatorContext(
        IExecutionRuntime runtime,
        ChatCommandOptions options,
        ProbePermissionRequestScript? permissionScript,
        ProbeUserInputScript? userInputScript)
        => new(
            async (threadId, token) => await TianShuControlPlaneClientFactory.Create(runtime).Conversations.ResumeThreadAsync(
                    new ControlPlaneResumeThreadCommand
                    {
                        ThreadId = new ThreadId(threadId),
                    },
                    token)
                .ConfigureAwait(false),
            token => CliSessionSnapshotUtilities.GetSnapshotAsync(runtime, token),
            token => RefreshSessionSnapshotAsync(runtime, token),
            ApplySessionSnapshot,
            pendingInteractiveRequests,
            pendingInteractiveReplay,
            restoredFollowUps,
            (replayEvent, token) => OnStreamEvent(runtime, options, permissionScript, userInputScript, replayEvent, token),
            draft => followUpCommandHandler.WriteRestoredFollowUpPromotion(
                CreateFollowUpCommandContext(runtime, options),
                draft),
            (message, isError) => WriteLine(message, isError));


    private async Task<bool> WaitForIdleAsync(IExecutionRuntime runtime, TimeSpan timeout, CancellationToken cancellationToken)
        => await waitCommandHandler.WaitForIdleAsync(
                eventWaiter,
                token => RefreshSessionSnapshotAsync(runtime, token),
                () => IsConversationIdle(runtime),
                timeout,
                cancellationToken)
            .ConfigureAwait(false);

    private void WritePrompt()
    {
        if (outputProtocol != ChatOutputProtocol.Human)
        {
            return;
        }

        lock (consoleGate)
        {
        var prompt = restoredFollowUps.BuildPrompt("> ");
            Console.Write(prompt);
        }
    }

    private void StartWorkingDockTimer(IExecutionRuntime runtime, ChatCommandOptions options)
        => terminalHost.StartWorkingDockTimer();

    private void StopWorkingDockTimer()
        => terminalHost.StopWorkingDockTimer();

    private void RefreshWorkingDockTick(IExecutionRuntime runtime, ChatCommandOptions options)
        => terminalHost.RefreshWorkingDockTick();

    private void StartConversationOperation(
        IExecutionRuntime runtime,
        ChatCommandOptions options,
        string label,
        Func<Task<ControlPlaneTurnSubmissionResult>> action,
        CancellationToken cancellationToken,
        Action<ControlPlaneTurnSubmissionResult>? onResult = null,
        Action? onCancelled = null,
        Action<Exception>? onException = null,
        Action? onFinally = null,
        bool clearPlanDockOnStart = true)
    {
        if (clearPlanDockOnStart)
        {
            ClearCurrentPlanDockState();
            RefreshAndRestoreInlineTailPrompt(runtime, options);
        }

        var renderOperationErrorsAsControlOutput = label.Contains("follow-up", StringComparison.OrdinalIgnoreCase);
        conversationOperationCoordinator.Start(
            CreateConversationOperationCoordinatorContext(runtime, options, renderOperationErrorsAsControlOutput),
            label,
            action,
            cancellationToken,
            onResult,
            onCancelled,
            onException,
            onFinally);
    }

    private ConversationOperationCoordinatorContext CreateConversationOperationCoordinatorContext(
        IExecutionRuntime runtime,
        ChatCommandOptions options,
        bool renderErrorsAsControlOutput = false)
        => new()
        {
            ConversationActivity = conversationActivity,
            CurrentSessionThreadId = GetCurrentSessionThreadId(),
            LastObservedTurnId = sessionState.LastObservedTurnId,
            TouchConversationActivity = TouchConversationActivity,
            RecordSyntheticEvent = RecordSyntheticEvent,
            StartWorkingDockTimer = () => StartWorkingDockTimer(runtime, options),
            StopWorkingDockTimer = StopWorkingDockTimer,
            RefreshSessionSnapshotAsync = token => RefreshSessionSnapshotAsync(runtime, token),
            ApplyTurnResult = sessionState.ApplyTurnResult,
            TryConsumeUserRequestedInterrupt = TryConsumeUserRequestedInterrupt,
            WriteVerboseOrImportant = (message, important) => WriteVerboseOrImportant(options, message, important),
            IsTerminalTurnStatus = IsTerminalTurnStatus,
            IsFailedTurnStatus = IsFailedTurnStatus,
            MarkFailure = MarkFailure,
            WriteLine = message => WriteLine(message),
            WriteErrorLineOnce = message => WriteErrorLineOnce(message),
            BeginControlOutputScope = renderErrorsAsControlOutput
                ? () => chatOutputWriter.BeginControlOutputScope(buffered: true)
                : null,
            RefreshInlineTailPrompt = () => RefreshAndRestoreInlineTailPrompt(runtime, options),
            GetAssistantLineOpen = () => assistantLineOpen,
            SetAssistantLineOpen = value => assistantLineOpen = value,
        };

    private void PrintHelp()
    {
        WriteLine(SlashCommandRegistry.Default.BuildHelpText());
    }

    private static async Task<string?> ReadConsoleLineAsync(CancellationToken cancellationToken)
        => await Task.Run(Console.ReadLine, cancellationToken).ConfigureAwait(false);

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static ResolvedTianShuConfig LoadResolvedConfig(ChatCommandOptions options)
        => new RuntimeConfigurationComposition().Load(
            options.ConfigFilePath,
            options.ProfileName,
            options.ConfigOverrides,
            options.WorkingDirectory);

    private bool IsCurrentThread(IExecutionRuntime runtime, string threadId)
        => string.Equals(Normalize(GetCurrentSessionThreadId()) ?? Normalize(runtime.ActiveThreadId), threadId, StringComparison.Ordinal);

    private async Task<bool> ConfirmChatDestructiveOperationAsync(
        string expectedConfirmation,
        string? configuredConfirmation,
        string prompt,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(configuredConfirmation))
        {
            return string.Equals(configuredConfirmation, expectedConfirmation, StringComparison.Ordinal);
        }

        if (readBlockingConfirmationAsync is not null && outputProtocol == ChatOutputProtocol.Human)
        {
            inputNotice = prompt;
            try
            {
                var entered = await readBlockingConfirmationAsync(cancellationToken).ConfigureAwait(false);
                return string.Equals(entered, expectedConfirmation, StringComparison.Ordinal);
            }
            finally
            {
                inputNotice = null;
            }
        }

        WriteLine(prompt);
        var fallbackEntered = await ReadConsoleLineAsync(cancellationToken).ConfigureAwait(false);
        return string.Equals(fallbackEntered, expectedConfirmation, StringComparison.Ordinal);
    }

    private static string FormatConfigValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? "<config>" : value;

    internal static IReadOnlyList<ControlPlaneInputItem> BuildStructuredUserInputsFromText(string? text)
        => CliConversationInputUtilities.BuildStructuredInputsFromText(text);

    private static string ResolveCurrentModel(ChatCommandOptions options)
        => string.IsNullOrWhiteSpace(options.RuntimeModel) ? "<config>" : options.RuntimeModel!;

    internal static string ResolveCurrentModelForDisplay(ChatCommandOptions options, ResolvedTianShuConfig config)
        => ResolveCurrentModelDockSummary(options, config).Model ?? "<config>";

    internal static ModelDockSummary ResolveCurrentModelDockSummary(ChatCommandOptions options, ResolvedTianShuConfig config)
    {
        var runtimeModel = Normalize(options.RuntimeModel);
        var diagnostic = TianShuModelRouteSetDefaults.BuildRouteDiagnostic(
            config.RawConfig,
            TianShuModelRouteSetDefaults.DefaultRouteKind);
        var routeCandidate = diagnostic.PreferredCandidate;
        var usesRouteCandidate = runtimeModel is null
                                 && routeCandidate is not null
                                 && !diagnostic.RouteSetIsVirtual;
        var model = runtimeModel
                    ?? Normalize(routeCandidate?.Model)
                    ?? "<config>";
        var provider = Normalize(options.RuntimeModelProvider)
                       ?? (usesRouteCandidate ? Normalize(routeCandidate?.Provider) : null)
                       ?? null;
        var route = usesRouteCandidate ? Normalize(diagnostic.ResolvedRouteKind) : null;
        var protocol = usesRouteCandidate
            ? ResolveDisplayProtocol(config.RawConfig, provider, model, routeCandidate?.Protocol)
            : NormalizeExplicitProtocol(routeCandidate?.Protocol)
              ?? Normalize(options.RuntimeProviderWireApi);

        return new ModelDockSummary(model, provider, route, protocol);
    }

    private string ResolveCurrentModelForDock(ChatCommandOptions options)
    {
        try
        {
            currentModelDockSummary = ResolveCurrentModelDockSummary(options, LoadResolvedConfig(options));
            sessionState.SetCurrentDisplayModel(currentModelDockSummary.Model ?? "<config>");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException)
        {
            currentModelDockSummary = new ModelDockSummary(
                ResolveCurrentModel(options),
                Normalize(options.RuntimeModelProvider),
                Protocol: Normalize(options.RuntimeProviderWireApi));
            sessionState.SetCurrentDisplayModel(currentModelDockSummary.Model ?? "<config>");
        }

        return sessionState.CurrentDisplayModel;
    }

    private void SetCurrentDisplayModelForDock(ChatCommandOptions options, string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            ResolveCurrentModelForDock(options);
            return;
        }

        currentModelDockSummary = new ModelDockSummary(
            Normalize(model),
            Normalize(options.RuntimeModelProvider),
            Protocol: Normalize(options.RuntimeProviderWireApi));
        sessionState.SetCurrentDisplayModel(currentModelDockSummary.Model ?? "<config>");
    }

    private static string? NormalizeExplicitProtocol(string? protocol)
    {
        var normalized = Normalize(protocol);
        return normalized is null || string.Equals(normalized, "auto", StringComparison.OrdinalIgnoreCase)
            ? null
            : normalized;
    }

    private static string? ResolveDisplayProtocol(
        Dictionary<string, object?> config,
        string? provider,
        string? model,
        string? candidateProtocol)
    {
        try
        {
            return Normalize(KernelModelProtocolResolver.ResolveModelProtocol(config, provider, model, candidateProtocol));
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            return NormalizeExplicitProtocol(candidateProtocol);
        }
    }

    private bool ShouldUseTerminalThreadPicker()
        => outputProtocol == ChatOutputProtocol.Human
           && !scriptMode
           && !Console.IsInputRedirected
           && !Console.IsOutputRedirected;

    private bool ShouldUseStyledControlPlaneOutput()
        => outputProtocol == ChatOutputProtocol.Human
           && !Console.IsOutputRedirected;

    private bool ShouldUseTerminalChatTui(ChatCommandOptions options)
        => ShouldUseTerminalChatTui(
            options,
            scriptMode,
            Console.IsInputRedirected,
            Console.IsOutputRedirected);

    internal static bool ShouldUseTerminalChatTui(
        ChatCommandOptions options,
        bool hasScript,
        bool isInputRedirected,
        bool isOutputRedirected)
        => options.OutputProtocol == ChatOutputProtocol.Human
           && !hasScript
           && !isInputRedirected
           && !isOutputRedirected;

    private void WriteVerboseOrImportant(ChatCommandOptions options, string message, bool important)
    {
        if (important || options.VerboseEvents)
        {
            WriteLine(message);
        }
    }

    private void WriteVerboseOrDebug(ChatCommandOptions options, string message, CliTranscriptRecordKind kind)
    {
        if (options.VerboseEvents)
        {
            WriteLine(message, transcriptKind: kind);
            return;
        }

        AppendTranscript(message, appendNewLine: true, kind, isError: false);
    }

    private void MarkFailure()
    {
        Interlocked.Exchange(ref failureCount, 1);
        MarkScriptFailure();
    }

    private void MarkScriptFailure()
    {
        if (scriptMode)
        {
            Interlocked.Exchange(ref scriptFailureCount, 1);
        }
    }

    private void RememberUserRequestedInterrupt(string? turnId)
        => sessionState.RememberUserRequestedInterrupt(turnId);

    private bool TryConsumeUserRequestedInterrupt(string? turnId, string? turnStatus)
        => sessionState.TryConsumeUserRequestedInterrupt(turnId, turnStatus);

    private static bool IsFailedTurnStatus(string? status)
        => ChatSessionState.IsFailedTurnStatus(status);

    private static bool IsTerminalTurnStatus(string? status)
        => ChatSessionState.IsTerminalTurnStatus(status);

    private bool TryHandleConsoleCancel(IExecutionRuntime runtime)
    {
        if (!HasRunningConversation(runtime))
        {
            return false;
        }

        RememberUserRequestedInterrupt(sessionState.LastObservedTurnId);
        _ = Task.Run(async () =>
        {
            try
            {
                await TianShuControlPlaneClientFactory.Create(runtime).Conversations.InterruptTurnAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }
        });
        WriteLine("已请求中断当前回合，等待确认。");
        return true;
    }

    private bool HasRunningConversation(IExecutionRuntime runtime)
        => IsConversationBusy();

    private bool IsConversationBusy()
        => conversationActivity.HasActiveConversation(sessionState.LastObservedTurnStatus);

    private bool HasSteerableConversation(IExecutionRuntime runtime)
        => conversationActivity.HasSteerableConversation(sessionState.LastObservedTurnId, sessionState.LastObservedTurnStatus);

    private bool IsConversationIdle(IExecutionRuntime runtime)
        => !HasRunningConversation(runtime)
           && pendingInteractiveRequests.IsEmpty;

    private void TouchConversationActivity()
        => conversationActivity.Touch();

    private async Task<ControlPlaneTurnSubmissionResult> SubmitTurnWithSelectedModelAsync(
        IExecutionRuntime runtime,
        ChatCommandOptions options,
        IReadOnlyList<ControlPlaneInputItem> userInputs,
        CancellationToken cancellationToken)
    {
        var selectedModel = Normalize(options.RuntimeModel);
        var selectedModelProvider = Normalize(options.RuntimeModelProvider);
        if ((selectedModel is not null || selectedModelProvider is not null) && GetCurrentSessionThreadId() is null)
        {
            var thread = await TianShuControlPlaneClientFactory.Create(runtime).Conversations.StartThreadAsync(
                    new ControlPlaneStartThreadCommand
                    {
                        Model = selectedModel,
                        ModelProvider = selectedModelProvider,
                        WorkingDirectory = options.WorkingDirectory,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            if (thread is null)
            {
                return new ControlPlaneTurnSubmissionResult
                {
                    Accepted = false,
                    Message = $"无法使用模型 {FormatConfigValue(selectedModel)} 创建线程。",
                    TurnStatus = "failed",
                };
            }

            sessionState.SetSessionActiveThreadId(thread.ThreadId);
            conversationActivity.MarkTerminalTurn();
        }

        return await SubmitTurnAsync(runtime, userInputs, cancellationToken).ConfigureAwait(false);
    }

    private static Task<ControlPlaneTurnSubmissionResult> SubmitTurnAsync(
        IExecutionRuntime runtime,
        string userMessage,
        CancellationToken cancellationToken)
        => TianShuControlPlaneClientFactory.Create(runtime).Conversations.SubmitTurnAsync(
            CliConversationEnvelopeFactory.Normalize(new ControlPlaneSubmitTurnCommand
            {
                Inputs = [new ControlPlaneTextInput(userMessage)],
            }),
            cancellationToken);

    private static Task<ControlPlaneTurnSubmissionResult> SubmitTurnAsync(
        IExecutionRuntime runtime,
        IReadOnlyList<ControlPlaneInputItem> userInputs,
        CancellationToken cancellationToken)
        => TianShuControlPlaneClientFactory.Create(runtime).Conversations.SubmitTurnAsync(
            CliConversationEnvelopeFactory.Normalize(new ControlPlaneSubmitTurnCommand
            {
                Inputs = userInputs,
            }),
            cancellationToken);

    private static Task<ControlPlaneTurnSubmissionResult> SubmitFollowUpAsync(
        IExecutionRuntime runtime,
        IReadOnlyList<ControlPlaneInputItem> userInputs,
        ControlPlaneFollowUpMode mode,
        CancellationToken cancellationToken,
        string? correlationId)
        => TianShuControlPlaneClientFactory.Create(runtime).Conversations.SubmitFollowUpAsync(
            CliConversationEnvelopeFactory.Normalize(new ControlPlaneSubmitFollowUpCommand
            {
                Inputs = userInputs,
                Mode = mode,
                CorrelationId = correlationId,
            }),
            cancellationToken);

    private static Task<ControlPlanePendingFollowUpMutationResult> MutatePendingFollowUpAsync(
        IExecutionRuntime runtime,
        string correlationId,
        ControlPlanePendingFollowUpMutationKind kind,
        CancellationToken cancellationToken)
        => TianShuControlPlaneClientFactory.Create(runtime).Conversations.MutatePendingFollowUpAsync(
            new ControlPlaneMutatePendingFollowUpCommand
            {
                CorrelationId = correlationId,
                Kind = kind,
            },
            cancellationToken);

    private TimeSpan GetConversationIdleDuration()
        => conversationActivity.GetIdleDuration();

    private string BuildPendingConversationMessage(string prefix, IExecutionRuntime runtime)
    {
        var builder = new StringBuilder(prefix);
        var threadId = GetCurrentSessionThreadId();
        var turnId = sessionState.LastObservedTurnId;
        var status = sessionState.LastObservedTurnStatus;
        if (!string.IsNullOrWhiteSpace(threadId) || !string.IsNullOrWhiteSpace(turnId) || !string.IsNullOrWhiteSpace(status))
        {
            builder.Append(" 当前状态：");
            var parts = new List<string>(3);
            if (!string.IsNullOrWhiteSpace(threadId))
            {
                parts.Add($"thread={threadId}");
            }

            if (!string.IsNullOrWhiteSpace(turnId))
            {
                parts.Add($"turn={turnId}");
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                parts.Add($"status={status}");
            }

            builder.Append(string.Join(", ", parts));
            builder.Append('。');
        }

        return builder.ToString();
    }

    private static string FormatVerboseEvent(ControlPlaneConversationStreamEvent streamEvent)
        => CliStreamEventFormatter.Format(ProbeEventRecord.FromStreamEvent(streamEvent));

    private void Write(string text)
        => chatOutputWriter.Write(text);

    private void WriteDisplayLine(string plainText, string displayText)
        => chatOutputWriter.WriteDisplayLine(plainText, displayText);

    private void WriteDisplayBlock(
        ChatPresentationBlock block,
        bool isError = false,
        bool? countAsFailure = null,
        CliTranscriptRecordKind? transcriptKind = null)
        => chatOutputWriter.WriteDisplayBlock(block, isError, countAsFailure, transcriptKind);

    private void WriteUserMessageBlock(string text)
        => WriteUserMessageBlock(text, label: null);

    private void WriteUserMessageBlock(string text, string? label)
    {
        var trimmed = Normalize(text);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return;
        }

        WriteDisplayBlock(
            new UserMessageBlock(trimmed, label),
            isError: false,
            countAsFailure: false,
            transcriptKind: CliTranscriptRecordKind.UserMessage);
    }

    private void WriteInterruptedControlOutput()
        => WriteInControlOutputScope(() => WriteLine("已中断当前回合。"));

    private void WriteProjectionCommittedBlocks(
        IExecutionRuntime runtime,
        ChatCommandOptions options,
        IReadOnlyList<ChatPresentationBlock> blocks,
        bool countErrorsAsFailure = true)
        => chatOutputWriter.WriteProjectionCommittedBlocks(blocks, countErrorsAsFailure);

    private void WriteCommittedAssistantBlock(
        IExecutionRuntime runtime,
        ChatCommandOptions options,
        AssistantMessageBlock block)
        => chatOutputWriter.WriteCommittedAssistantBlock(block);

    private void WriteTerminalVisualSpacerLine()
        => chatOutputWriter.WriteTerminalVisualSpacerLine();

    private void RefreshAndRestoreInlineTailPrompt(IExecutionRuntime runtime, ChatCommandOptions options)
        => terminalHost.RefreshAndRestoreInlineTailPrompt();

    private void WriteControlPlaneLine(
        string text,
        bool isError = false,
        bool? countAsFailure = null)
        => chatOutputWriter.WriteControlPlaneLine(text, isError, countAsFailure);

    private void WriteControlOutputLine(string text, bool isError = false, bool? countAsFailure = null)
        => WriteInControlOutputScope(() => WriteLine(text, isError, countAsFailure), buffered: true);

    private void WriteInControlOutputScope(Action write, bool buffered = false) { using var controlOutputScope = chatOutputWriter.BeginControlOutputScope(buffered); write(); }

    private void WriteLine(
        string text,
        bool isError = false,
        bool? countAsFailure = null,
        CliTranscriptRecordKind? transcriptKind = null,
        bool includeInTranscript = true)
        => chatOutputWriter.WriteLine(text, isError, countAsFailure, transcriptKind, includeInTranscript);

    private void ResetSessionState()
    {
        ClearCurrentThreadState();
        interactionRecorder.Reset();
        presentationPipeline = new InteractionPipeline();
        sessionState.Reset();
        currentPlanDockSummary = null;
        currentModelDockSummary = null;
        lastCompletedAssistantText = null;
        assistantLineOpen = false;
        assistantLeadingSpacerPending = false;
        pendingGuidanceMessages.Clear();

        conversationActivity.Reset();
        terminalHost.Reset();
        chatOutputWriter.Reset();
        scriptMode = false;
        Interlocked.Exchange(ref scriptFailureCount, 0);
        Interlocked.Exchange(ref failureCount, 0);
    }

    private void ClearCurrentPlanDockState()
    {
        presentationPipeline.ClearPlanDockState();
        currentPlanDockSummary = null;
    }

    private void ClearCurrentThreadState()
    {
        pendingInteractiveRequests.Clear();
        restoredFollowUps.Clear();
        eventWaiter.Reset();
        sessionState.ClearCurrentThread();

        conversationActivity.Reset();
    }

    private void RecordExecutedInput(string input)
    {
        interactionRecorder.RecordExecutedInput(input, sessionState.LastObservedThreadId, sessionState.LastObservedTurnId);
    }

    private void RecordSyntheticEvent(string kind, string? threadId, string? turnId, string? message, string? text = null, string? status = null)
    {
        interactionRecorder.RecordSyntheticEvent(kind, threadId, turnId, message, text, status);
    }

    private InteractionPipelineProjectionResult RecordPresentationProjection(ControlPlaneConversationStreamEvent streamEvent)
    {
        var result = interactionRecorder.RecordPresentationProjection(presentationPipeline, streamEvent);
        currentPlanDockSummary = result.Projection.Plan ?? currentPlanDockSummary;

        return result;
    }

    private void RecordIncompletePresentationSnapshot(DateTimeOffset timestamp, string reason)
    {
        interactionRecorder.RecordIncompletePresentationSnapshot(presentationPipeline, timestamp, reason);
    }

    private string? GetCurrentSessionThreadId()
        => sessionState.CurrentThreadId;

    private void ApplySessionSnapshot(ControlPlaneSessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        sessionState.ApplySessionSnapshot(snapshot);
        conversationActivity.ApplySessionActiveTurn(snapshot.HasActiveTurn);
    }

    private async Task RefreshSessionSnapshotAsync(IExecutionRuntime runtime, CancellationToken cancellationToken)
        => ApplySessionSnapshot(await CliSessionSnapshotUtilities.GetSnapshotAsync(runtime, cancellationToken).ConfigureAwait(false));

    private void AppendTranscript(string text, bool appendNewLine, CliTranscriptRecordKind kind, bool isError)
    {
        interactionRecorder.AppendTranscript(text, appendNewLine, kind, isError);
    }

    private async Task WriteArtifactsAsync(
        ChatCommandOptions options,
        CliRuntimeBootstrapResult bootstrap,
        ChatScriptCommandFile? script,
        DateTimeOffset startedAt,
        int exitCode,
        string? failureMessage,
        string? threadId,
        CancellationToken cancellationToken)
    {
        var finalFailureMessage = failureMessage ?? sessionState.LastFailureMessage;
        var resultText = !string.IsNullOrWhiteSpace(lastCompletedAssistantText)
            ? lastCompletedAssistantText!
            : !string.IsNullOrWhiteSpace(presentationPipeline.PresentationState.ActiveAssistantText)
                ? presentationPipeline.PresentationState.ActiveAssistantText
                : finalFailureMessage ?? string.Empty;

        await interactionRecorder.WriteArtifactsAsync(
                options,
                bootstrap,
                script,
                presentationPipeline,
                startedAt,
                exitCode,
                finalFailureMessage,
                threadId,
                sessionState.LastObservedTurnId,
                sessionState.LastObservedTurnStatus,
                Volatile.Read(ref failureCount),
                resultText,
                cancellationToken)
            .ConfigureAwait(false);
    }

}
