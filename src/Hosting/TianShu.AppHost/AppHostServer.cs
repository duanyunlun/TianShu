using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using TianShu.AppHost.Configuration;
using TianShu.AppHost.Tools;
using TianShu.AppHost.Tools.Runtime;
using TianShu.AppHost.Tools.Runtime.Diagnostics;
using TianShu.Contracts.Catalog;
using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Governance;
using TianShu.Contracts.Identity;
using TianShu.Contracts.Interactions;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Projections;
using TianShu.Contracts.Primitives;
using TianShu.ControlPlane;
using TianShu.Diagnostics;
using TianShu.Execution.Runtime;
using TianShu.IdentityMemory;
using TianShu.Provider.Abstractions;
using TianShu.RuntimeComposition;
using Tomlyn;
using Tomlyn.Model;

namespace TianShu.AppHost;

internal sealed partial class AppHostServer
{
    private static readonly StringComparer EnvironmentVariableComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private static readonly StringComparer KernelPathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private static readonly StringComparison KernelPathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private static readonly Regex LinkedPluginMentionRegex = new(
        """\[(?:\$|@)[^\]]+\]\s*\(\s*(?<path>plugin://[^)\s]+)\s*\)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LinkedToolMentionRegex = new(
        """\[(?:\$|@)(?<name>[^\]]+)\]\s*\(\s*(?<path>[^)]+?)\s*\)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PlainSkillMentionRegex = new(
        """(?<![A-Za-z0-9_])\$(?<name>[A-Za-z0-9][A-Za-z0-9:_-]*)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private const int DefaultResponsesStreamMaxRetries = 5;
    private static readonly TimeSpan DefaultResponsesStreamIdleTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultResponsesStreamRetryBaseDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan MaxResponsesStreamRetryDelay = TimeSpan.FromSeconds(30);
    private const string DefaultSpawnAgentRoleName = "default";
    private static readonly string[] DefaultSpawnAgentNicknameCandidates =
    [
        "Euclid", "Archimedes", "Ptolemy", "Hypatia", "Avicenna", "Averroes", "Aquinas", "Copernicus",
        "Kepler", "Galileo", "Bacon", "Descartes", "Pascal", "Fermat", "Huygens", "Leibniz",
        "Newton", "Halley", "Euler", "Lagrange", "Laplace", "Volta", "Gauss", "Ampere",
        "Faraday", "Darwin", "Lovelace", "Boole", "Pasteur", "Maxwell", "Mendel", "Curie",
        "Planck", "Tesla", "Poincare", "Noether", "Hilbert", "Einstein", "Raman", "Bohr",
        "Turing", "Hubble", "Feynman", "Franklin", "McClintock", "Meitner", "Herschel", "Linnaeus",
        "Wegener", "Chandrasekhar", "Sagan", "Goodall", "Carson", "Carver", "Socrates", "Plato",
        "Aristotle", "Epicurus", "Cicero", "Confucius", "Mencius", "Zeno", "Locke", "Hume",
        "Kant", "Hegel", "Kierkegaard", "Mill", "Nietzsche", "Peirce", "James", "Dewey",
        "Russell", "Popper", "Sartre", "Beauvoir", "Arendt", "Rawls", "Singer", "Anscombe",
        "Parfit", "Kuhn", "Boyle", "Hooke", "Harvey", "Dalton", "Ohm", "Helmholtz",
        "Gibbs", "Lorentz", "Schrodinger", "Heisenberg", "Pauli", "Dirac", "Bernoulli", "Godel",
        "Nash", "Banach", "Ramanujan", "Erdos",
    ];

    private readonly TextReader input;
    private readonly TextWriter output;
    private readonly KernelThreadStore threadStore;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly JsonSerializerOptions strictInputJsonOptions;
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly SemaphoreSlim configGate = new(1, 1);
    private readonly KernelGlobalNotificationHub? globalNotificationHub;
    private readonly Action? globalConnectionDisconnect;
    private readonly IDiagnosticEventSink diagnosticEventSink;
    private readonly IDiagnosticOperationScopeFactory diagnosticOperationScopeFactory;
    private readonly IDiagnosticArtifactWriter providerRequestPayloadArtifactWriter;
    private readonly ITianShuIdentityMemoryPlane identityMemoryPlane;
    private KernelQueuePair<KernelInboundMessage, string>? activeQueues;
    private KernelGlobalNotificationRegistration? globalNotificationRegistration;
    private readonly object agentNicknameGate = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> runningTurns = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task> runningTurnTasks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> steerInputsByTurn = new(StringComparer.Ordinal);
    private readonly KernelThreadManager threadManager;
    private readonly KernelToolRegistry toolRegistry;
    private readonly IReadOnlyList<IKernelToolExecutionHook> toolExecutionHooks;
    private readonly IReadOnlyDictionary<string, string> cliConfigOverrides;
    private readonly string? cliConfigFilePath;
    private readonly PolicyStrategyRuntimePackage policyStrategyPackage;
    private readonly HttpClient providerHttpClient;
    private readonly int responsesStreamMaxRetries;
    private readonly TimeSpan responsesStreamIdleTimeout;
    private readonly TimeSpan responsesStreamRetryBaseDelay;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pendingServerResponses = new();
    private readonly ConcurrentDictionary<string, long> approvalRequestIdsByCallId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<long, string> approvalCallIdsByRequestId = new();
    private readonly ConcurrentDictionary<long, KernelPendingUserInputServerRequest> pendingUserInputRequestsByRequestId = new();
    private readonly ConcurrentDictionary<string, KernelPendingPermissionRequest> pendingPermissionRequestsByCallId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> commandApprovalSessionKeysByThread = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> fileChangeApprovalSessionPathsByThread = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> mcpToolApprovalSessionKeysByThread = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, KernelPermissionGrantProfile> grantedPermissionSessionByThread = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, KernelPermissionGrantProfile> grantedPermissionTurnByTurn = new(StringComparer.Ordinal);
    private readonly object threadSubscriptionGate = new();
    private readonly HashSet<string> subscribedThreadIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> emittedDeprecationNotices = new(StringComparer.Ordinal);
    private readonly KernelPluginsManager pluginsManager;
    private readonly KernelSkillsManager skillsManager;
    private readonly KernelMcpManager mcpManager;
    private readonly KernelArtifactsAppHostRuntime artifactsRuntime;
    private readonly KernelCodeModeAppHostRuntime codeModeRuntime;
    private readonly KernelCodeModeProtocolAppHostRuntime codeModeProtocolAppHostRuntime;
    private readonly KernelExecPolicyManager execPolicyManager;
    private readonly KernelJsReplAppHostRuntime jsReplRuntime;
    private readonly KernelManagedNetworkAppHostRuntime managedNetworkRuntime;
    private readonly KernelCatalogSurfaceAppHostRuntime catalogSurfaceAppHostRuntime;
    private readonly KernelConfigSurfaceAppHostRuntime configSurfaceAppHostRuntime;
    private readonly KernelArtifactSurfaceAppHostRuntime artifactSurfaceAppHostRuntime;
    private readonly KernelFeedbackAppHostRuntime feedbackAppHostRuntime;
    private readonly KernelDiagnosticsTraceQueryService diagnosticsTraceQueryService;
    private readonly McpServerSurfaceAppHostRuntime mcpServerSurfaceAppHostRuntime;
    private readonly KernelPendingInteractiveReplayAppHostRuntime pendingInteractiveReplayAppHostRuntime;
    private readonly KernelPluginsAppHostRuntime pluginsAppHostRuntime;
    private readonly KernelRealtimeAppHostRuntime realtimeAppHostRuntime;
    private readonly KernelAutoCompactionAppHostRuntime autoCompactionAppHostRuntime;
    private readonly KernelCommandExecAppHostRuntime commandExecAppHostRuntime;
    private readonly KernelCommandExecSurfaceAppHostRuntime commandExecSurfaceAppHostRuntime;
    private readonly KernelAgentJobsAppHostRuntime agentJobsAppHostRuntime;
    private readonly KernelFileSystemAppHostRuntime fileSystemAppHostRuntime;
    private readonly KernelProcessExecutionAppHostRuntime processExecutionAppHostRuntime;
    private readonly KernelNativeToolOptionsAppHostRuntime nativeToolOptionsAppHostRuntime;
    private readonly KernelThreadHistoryAppHostRuntime threadHistoryAppHostRuntime;
    private readonly KernelThreadLifecycleAppHostRuntime threadLifecycleAppHostRuntime;
    private readonly KernelTurnReviewSurfaceAppHostRuntime turnReviewSurfaceAppHostRuntime;
    private readonly KernelTurnExecutionAppHostRuntime turnExecutionAppHostRuntime;
    private readonly AppHostCoreLoopRoutingRuntime coreLoopRoutingAppHostRuntime;
    private readonly KernelToolItemLifecycleAppHostRuntime toolItemLifecycleAppHostRuntime;
    private readonly KernelToolCallAppHostRuntime toolCallAppHostRuntime;
    private readonly KernelToolExecutionAppHostRuntime toolExecutionAppHostRuntime;
    private readonly KernelModelFunctionToolCallRuntime modelFunctionToolCallRuntime;
    private readonly KernelToolRuntimeAppHostRuntime toolRuntimeAppHostRuntime;
    private readonly KernelToolRuntimeServicesAppHostRuntime toolRuntimeServicesAppHostRuntime;
    private readonly KernelSubagentNotificationAppHostRuntime subagentNotificationAppHostRuntime;
    private readonly KernelUserShellAppHostRuntime userShellAppHostRuntime;
    private readonly KernelWindowsSandboxSurfaceAppHostRuntime windowsSandboxSurfaceAppHostRuntime;
    private readonly KernelAgentOrchestrationManager agentOrchestrationManager;
    private readonly KernelSpawnAgentGuardAppHostRuntime spawnAgentGuardAppHostRuntime;
    private readonly KernelSpawnAgentsOnCsvAppHostRuntime spawnAgentsOnCsvAppHostRuntime;
    private readonly HashSet<string> optedOutNotificationMethods = new(StringComparer.Ordinal);
    private readonly HashSet<string> usedAgentNicknames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> threadAgentNicknames = new(StringComparer.Ordinal);
    private bool initializeReceived;
    private bool experimentalApiEnabled;
    private int agentNicknameResetCount;

    private long userMessageItemSequence;
    private long serverRequestSequence;
    private const string DefaultModel = "gpt-5";
    private const string DefaultModelProvider = "openai";
    private static readonly IReadOnlyList<KernelThreadSourceKind> InteractiveThreadSourceKinds =
    [
        KernelThreadSourceKind.Cli,
        KernelThreadSourceKind.VsCode,
    ];
    private static readonly KernelApprovalPolicy DefaultApprovalPolicy = KernelApprovalPolicy.OnRequest;
    private const string OriginatorOverrideEnvironmentVariable = "TIANSHU_INTERNAL_ORIGINATOR_OVERRIDE";
    private const string CliVersion = "0.1.0";
    private const int MaxUserInputTextChars = 1 << 20;
    private const string InputTooLargeErrorCode = "input_too_large";

    private sealed record KernelPendingUserInputServerRequest(
        long RequestId,
        string ThreadId,
        string? TurnId,
        string CallId);

    private sealed record KernelInboundMessage(string Method, JsonElement Params, JsonElement? Id)
    {
        public bool IsRequest => Id.HasValue;
    }

    public AppHostServer(
        TextReader input,
        TextWriter output,
        KernelThreadStore threadStore,
        IReadOnlyDictionary<string, string>? cliConfigOverrides = null,
        string? cliConfigFilePath = null,
        HttpClient? httpClient = null,
        KernelGlobalNotificationHub? globalNotificationHub = null,
        Action? globalConnectionDisconnect = null,
        int? responsesStreamMaxRetries = null,
        TimeSpan? responsesStreamRetryBaseDelay = null,
        TimeSpan? responsesStreamIdleTimeout = null,
        IReadOnlyList<IKernelToolExecutionHook>? toolExecutionHooks = null)
    {
        var sharedHttpClient = httpClient ?? KernelCustomCaSupport.CreateHttpClient(TimeSpan.FromSeconds(90));
        this.input = input;
        this.output = output;
        this.threadStore = threadStore;
        this.cliConfigOverrides = cliConfigOverrides is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(cliConfigOverrides, StringComparer.Ordinal);
        this.cliConfigFilePath = NormalizeCliConfigFilePath(cliConfigFilePath);
        var tianShuHome = ResolveTianShuHomePath();
        policyStrategyPackage = PolicyStrategyRuntimeComposition.ResolveEffectivePackage(tianShuHome);
        strictInputJsonOptions = new JsonSerializerOptions(jsonOptions)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        strictInputJsonOptions.Converters.Add(new KernelOptionalJsonConverterFactory());
        this.globalNotificationHub = globalNotificationHub;
        this.globalConnectionDisconnect = globalConnectionDisconnect;
        var diagnosticPolicy = new DefaultDiagnosticCollectionPolicy(
            () => DiagnosticCollectionOptionsReader.FromConfig(BuildConfigReadSnapshotForRuntime(cwd: null).Config));
        var diagnosticRuntimeSinks = DiagnosticRuntimeComposition.CreateDiagnosticSinks(
            ResolveTianShuHomePath(),
            diagnosticPolicy,
            PersistTurnLogAsync,
            WriteNotificationAsync,
            ResolveDiagnosticModuleName,
            ResolveDiagnosticRequiredLevel);
        this.diagnosticEventSink = diagnosticRuntimeSinks.EventSink;
        this.diagnosticOperationScopeFactory = new DefaultDiagnosticOperationScopeFactory(diagnosticEventSink);
        this.providerRequestPayloadArtifactWriter = diagnosticRuntimeSinks.ProviderRequestPayloadArtifactWriter;
        this.identityMemoryPlane = new DefaultTianShuIdentityMemoryPlane(
            new FileSystemTianShuLocalMemoryStore(
                TianShuHomePathUtilities.ResolveDataPathFromHome(
                    ResolveTianShuHomePath())),
            diagnosticEventSink,
            diagnosticOperationScopeFactory,
            LoadExternalMemoryProviderOptions(),
            context => ResolveMemoryRuntimeOptions(context.WorkingDirectory));
        this.toolRegistry = ToolRuntimeComposition.CreateDefaultToolRegistry(
            BuildConfigReadSnapshotForRuntime(cwd: null).Config,
            workspacePath: null);
        this.toolRegistry.Register(new KernelTestSyncRuntimeEndpoint());
        this.toolExecutionHooks = toolExecutionHooks is null
            ? CreateToolExecutionHooks()
            : new List<IKernelToolExecutionHook>(toolExecutionHooks);
        this.pluginsManager = new KernelPluginsManager(
            cancellationToken => LoadEffectiveConfigValuesAsync(cancellationToken),
            cancellationToken => pluginsAppHostRuntime!.SyncRemotePluginStatesAsync(cancellationToken));
        this.skillsManager = new KernelSkillsManager(
            (cwd, cancellationToken) => LoadNonProjectConfigValuesAsync(cwd, cancellationToken),
            (values, _, cancellationToken) => SaveConfigValuesAsync(
                values,
                cancellationToken,
                filePath: ResolveActiveUserConfigPath()),
            pluginsManager: pluginsManager,
            loadProjectRootConfigOverridesAsync: (cwd, cancellationToken) => LoadNonProjectConfigValuesAsync(cwd, cancellationToken),
            loadWritableConfigOverridesAsync: (_, cancellationToken) => LoadWritablePersistedConfigValuesAsync(
                cancellationToken,
                filePath: ResolveActiveUserConfigPath()));
        this.pluginsAppHostRuntime = new KernelPluginsAppHostRuntime(
            sharedHttpClient,
            async (threadId, cancellationToken) =>
            {
                var thread = await threadStore.GetThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
                return (thread is not null, thread?.Cwd);
            },
            (cwd, cancellationToken) => LoadEffectiveConfigValuesAsync(cancellationToken, cwd: cwd),
            (cwd, cancellationToken) => LoadMergedPersistedConfigTextAsync(
                cancellationToken,
                cwd ?? Environment.CurrentDirectory),
            pluginsManager,
            skillsManager,
            (method, payload, cancellationToken) => WriteNotificationAsync(method, payload, cancellationToken),
            (id, code, message, cancellationToken) => WriteErrorAsync(id, code, message, cancellationToken),
            WriteResultAsync);
        this.catalogSurfaceAppHostRuntime = new KernelCatalogSurfaceAppHostRuntime(
            cancellationToken => LoadEffectiveConfigValuesAsync(cancellationToken),
            (cwd, includeHiddenTools) => toolRegistry.BuildResolvedToolCatalog(
                includeHiddenTools,
                toolProfileOptions: KernelToolProfileOptions.FromConfigValues(BuildConfigReadSnapshotForRuntime(cwd).Config)),
            (id, code, message, cancellationToken) => WriteErrorAsync(id, code, message, cancellationToken),
            WriteResultAsync);
        this.configSurfaceAppHostRuntime = new KernelConfigSurfaceAppHostRuntime(
            configGate,
            BuildConfigReadSnapshotAsync,
            ReloadLoadedThreadsUserConfigAsync,
            WriteResultAsync,
            WriteErrorAsync,
            WriteNotificationAsync);
        this.artifactSurfaceAppHostRuntime = new KernelArtifactSurfaceAppHostRuntime(
            threadStore,
            ResolveTianShuHomePath,
            BuildThreadPreview,
            CaptureThreadGitDiffAsync,
            WriteResultAsync,
            (id, code, message, cancellationToken) => WriteErrorAsync(id, code, message, cancellationToken));
        this.spawnAgentGuardAppHostRuntime = new KernelSpawnAgentGuardAppHostRuntime(
            threadStore.SpawnAgentGuardState,
            async (cwd, cancellationToken) =>
            {
                var snapshot = await BuildConfigReadSnapshotAsync(includeLayers: false, cwd, cancellationToken).ConfigureAwait(false);
                return snapshot.Config;
            },
            ReleaseSpawnAgentNicknameReservation);
        this.processExecutionAppHostRuntime = new KernelProcessExecutionAppHostRuntime();
        this.threadManager = new KernelThreadManager(
            new KernelFileWatcher(
                ResolveTianShuHomePath(),
                resolveSkillRoots: cwd => skillsManager.ResolveWatchRootsAsync(cwd, CancellationToken.None).GetAwaiter().GetResult()));
        this.subagentNotificationAppHostRuntime = new KernelSubagentNotificationAppHostRuntime(
            threadStore,
            threadManager,
            (agentId, treatArchivedAsNotFound, cancellationToken) => toolRuntimeAppHostRuntime!.GetAgentStatusNodeAsync(
                agentId,
                treatArchivedAsNotFound,
                cancellationToken));
        this.autoCompactionAppHostRuntime = new KernelAutoCompactionAppHostRuntime(
            threadStore,
            configCwd => KernelAutoCompactionRuntimeHelpers.ResolveConfiguredModelAutoCompactTokenLimit(
                BuildConfigReadSnapshotForRuntime(configCwd).Config),
            (method, payload, cancellationToken) => WriteNotificationAsync(method, payload, cancellationToken));
        this.mcpManager = new KernelMcpManager(
            cancellationToken => LoadEffectiveConfigValuesAsync(cancellationToken),
            httpClient: sharedHttpClient,
            loadPluginMcpServersAsync: pluginsManager.GetEffectiveMcpServersAsync,
            readEnvironmentVariable: KernelDependencyEnvironmentScope.ReadEnvironmentVariable);
        this.nativeToolOptionsAppHostRuntime = new KernelNativeToolOptionsAppHostRuntime(
            mcpManager,
            (cwd, cancellationToken) => LoadMergedPersistedConfigTextAsync(cancellationToken, cwd),
            spawnAgentGuardAppHostRuntime.ResolveSpawnAgentGuardConfigurationAsync,
            BuildSpawnAgentTypeDescriptionAsync,
            cancellationToken => pluginsAppHostRuntime.LoadToolSuggestDiscoverableConnectorsAsync(cancellationToken),
            (dynamicTools, nativeToolOptions) => codeModeRuntime!.BuildEnabledTools(dynamicTools, nativeToolOptions),
            DefaultModel);
        this.threadHistoryAppHostRuntime = new KernelThreadHistoryAppHostRuntime(
            jsonOptions,
            runningTurns);
        this.coreLoopRoutingAppHostRuntime = new AppHostCoreLoopRoutingRuntime(
            threadStore,
            cwd => BuildConfigReadSnapshotForRuntime(cwd).Config,
            Normalize,
            BuildStageCorrelationId);
        this.threadLifecycleAppHostRuntime = new KernelThreadLifecycleAppHostRuntime(
            threadStore,
            threadManager,
            runningTurns,
            runningTurnTasks,
            NextThreadId,
            (id, code, message, cancellationToken) => WriteErrorAsync(id, code, message, cancellationToken),
            WriteResultAsync,
            WriteNotificationAsync,
            WriteBroadcastNotificationAsync,
            ValidateThreadIdAsync,
            cwd => ResolveConfiguredThreadDefaultsWithThreadError(cwd).ConfigSnapshot.ModelProviderId,
            BuildThreadSessionStateForNewThread,
            (threadId, request, cwdOverride) => BuildThreadSessionStateForNewThread(threadId, request, cwdOverride),
            BuildThreadSessionStateWithConfigLoadHandling,
            BuildDefaultThreadSession,
            (currentSession, cwd, request) =>
            {
                var builder = KernelThreadSessionBuilder.FromSession(currentSession);
                var providerConnectionSnapshot = ResolveThreadResumeProviderConnectionSnapshot(cwd, request);
                if (request.Config is not null)
                {
                    var configuredDefaults = ResolveConfiguredThreadDefaultsWithThreadError(cwd, request.Config);
                    builder = builder.ApplyConfigSnapshot(configuredDefaults.ConfigSnapshot);
                }

                builder = builder.ApplyProviderConnectionSnapshot(providerConnectionSnapshot);
                return builder.ApplyThreadResume(request).Build();
            },
            (sourceSession, cwd, request) =>
            {
                var builder = KernelThreadSessionBuilder.FromSession(sourceSession);
                if (request.Config is not null)
                {
                    var configuredDefaults = ResolveConfiguredThreadDefaultsWithThreadError(cwd, request.Config);
                    builder = builder.ApplyConfigSnapshot(configuredDefaults.ConfigSnapshot);
                }

                return builder
                    .ApplyThreadFork(request)
                    .Build() with
                    {
                        Cwd = cwd ?? sourceSession.Cwd,
                    };
            },
            (record, includeTurns, session, activeTurn) => BuildThreadSessionResponse(record, includeTurns, session, activeTurn),
            EnsureRequiredMcpServersInitializedWithThreadErrorAsync,
            (session, cancellationToken) => mcpServerSurfaceAppHostRuntime!.UpdateMcpSandboxStateAsync(session, cancellationToken),
            TrackThreadSubscription,
            (threadId, turnId) => threadHistoryAppHostRuntime.HasTrackedTurnActivity(threadId, turnId),
            threadHistoryAppHostRuntime.BuildTrackedActiveTurnSnapshot,
            (threadId, cancellationToken) => pendingInteractiveReplayAppHostRuntime!.ReplayPendingInteractiveRequestsAsync(threadId, cancellationToken),
            EmitExperimentalInstructionsDeprecationNoticeIfNeededAsync,
            RemoveTrackedThreadSubscription,
            ForgetThreadSubscription,
            WriteThreadStatusChangedAsync,
            (threadId, lifecycleTurnId, lifecyclePhase, cancellationToken, clearAllForThread) => pendingInteractiveReplayAppHostRuntime!.ResolvePendingInteractiveRequestsForThreadLifecycleAsync(
                threadId,
                lifecycleTurnId,
                lifecyclePhase,
                cancellationToken,
                clearAllForThread),
            ResolvePendingUserInputRequestsForThreadLifecycleAsync,
            (realtimeSession, closeReason) => realtimeAppHostRuntime!.CloseRealtimeTransportAsync(realtimeSession, closeReason),
            spawnAgentGuardAppHostRuntime.ReleaseSpawnedAgentThread,
            (record, includeTurns) => ToThreadPayload(record, includeTurns),
            threadHistoryAppHostRuntime.TryBeginThreadRollback,
            threadHistoryAppHostRuntime.EndThreadRollback,
            threadId => _ = processExecutionAppHostRuntime.CleanBackgroundTerminals(threadId),
            InteractiveThreadSourceKinds);
        this.turnReviewSurfaceAppHostRuntime = new KernelTurnReviewSurfaceAppHostRuntime(
            threadStore,
            threadManager,
            runningTurns,
            runningTurnTasks,
            NextThreadId,
            NextTurnId,
            (id, code, message, cancellationToken) => WriteErrorAsync(id, code, message, cancellationToken),
            WriteErrorAsync,
            WriteResultAsync,
            WriteNotificationAsync,
            KernelTurnExecutionRuntimeHelpers.CountInputTextChars,
            KernelTurnExecutionRuntimeHelpers.ExtractInputText,
            EnqueueSteerInput,
            BuildDefaultThreadSession,
            BuildReviewTurnRequestContextAsync,
            (cwd, cancellationToken) => LoadEffectiveConfigValuesAsync(cancellationToken, cwd: cwd),
            cwd => BuildConfigReadSnapshotForRuntime(cwd).Config,
            async (command, effectiveCwd, ct) =>
            {
                var result = await processExecutionAppHostRuntime.ExecuteCommandAsync(command, effectiveCwd, timeoutMs: 8000, environment: null, ct).ConfigureAwait(false);
                return new KernelReviewCommandResult(result.ExitCode, result.StdOut, result.StdErr, result.TimedOut);
            },
            (threadId, turnId, userText, turnContext, persistExtendedHistory, cts) => turnExecutionAppHostRuntime!.RunTurnAsync(
                threadId,
                turnId,
                userText,
                turnContext,
                persistExtendedHistory,
                cts),
            WriteThreadStatusChangedAsync,
            (record, includeTurns) => ToThreadPayload(record, includeTurns),
            MaxUserInputTextChars,
            InputTooLargeErrorCode);
        this.realtimeAppHostRuntime = new KernelRealtimeAppHostRuntime(
            threadStore,
            threadManager,
            (thread, threadId, sessionId, requestPrompt) => KernelRealtimeContextRuntimeHelpers.BuildConfiguredRealtimeSessionState(
                thread,
                threadId,
                sessionId,
                requestPrompt,
                cwd => BuildConfigReadSnapshotForRuntime(cwd).Config),
            BuildDefaultThreadSession,
            Normalize,
            WriteNotificationAsync,
            WriteResultAsync,
            (id, code, message, cancellationToken) => WriteErrorAsync(id, code, message, cancellationToken));
        this.toolItemLifecycleAppHostRuntime = new KernelToolItemLifecycleAppHostRuntime(
            (method, payload, cancellationToken) => WriteNotificationAsync(method, payload, cancellationToken));
        this.responsesStreamMaxRetries = Math.Max(responsesStreamMaxRetries ?? DefaultResponsesStreamMaxRetries, 0);
        this.responsesStreamRetryBaseDelay = responsesStreamRetryBaseDelay ?? DefaultResponsesStreamRetryBaseDelay;
        this.responsesStreamIdleTimeout = responsesStreamIdleTimeout is { } customIdleTimeout && customIdleTimeout > TimeSpan.Zero
            ? customIdleTimeout
            : DefaultResponsesStreamIdleTimeout;
        this.turnExecutionAppHostRuntime = new KernelTurnExecutionAppHostRuntime(
            threadStore,
            threadManager,
            runningTurns,
            runningTurnTasks,
            steerInputsByTurn,
            grantedPermissionTurnByTurn,
            (id, code, message, data, cancellationToken) => WriteErrorAsync(id, code, message, data, cancellationToken),
            WriteResultAsync,
            NextTurnId,
            () => Interlocked.Increment(ref userMessageItemSequence),
            KernelTurnExecutionRuntimeHelpers.CountTextChars,
            request => request.Input is { Count: > 0 } inputItems
                ? KernelTurnExecutionRuntimeHelpers.CountInputTextChars(inputItems)
                : 0,
            MaxUserInputTextChars,
            Normalize,
            IsPlanCollaborationMode,
            BuildDefaultThreadSession,
            ApplyTurnOverrides,
            (runtimeThread, session, request, cancellationToken) => BuildTurnRequestContextAsync(runtimeThread, session, request, cancellationToken),
            (session, cancellationToken) => mcpServerSurfaceAppHostRuntime!.UpdateMcpSandboxStateAsync(
                session,
                cancellationToken),
            ExtractUserText,
            (threadId, turnId, userText, inputItems) => threadHistoryAppHostRuntime.SeedTrackedTurnUserMessage(threadId, turnId, userText, inputItems),
            DrainSteerInputs,
            threadHistoryAppHostRuntime.BuildTrackedActiveTurnSnapshot,
            (role, contentType, text) => KernelTurnExecutionRuntimeHelpers.CreateResponsesMessage(role, contentType, text),
            IsEphemeralThreadAsync,
            WriteNotificationAsync,
            PersistTurnLogAsync,
            PersistRolloutAsync,
            PersistRuntimeThreadSessionSnapshotAsync,
            WriteThreadStatusChangedAsync,
            (threadId, turnId, terminalStatus, cancellationToken, clearAllForThread) => pendingInteractiveReplayAppHostRuntime!.ResolvePendingInteractiveRequestsForThreadLifecycleAsync(
                threadId,
                turnId,
                terminalStatus,
                cancellationToken,
                clearAllForThread),
            FlushPendingTurnInterruptResponsesAsync,
            threadHistoryAppHostRuntime.RegisterPendingTurnInterrupt,
            threadHistoryAppHostRuntime.RegisterPendingTurnInterruptResponse,
            threadHistoryAppHostRuntime.ClearPendingTurnInterrupt,
            threadHistoryAppHostRuntime.ClearPendingTurnInterruptResponses,
            PersistTurnSessionBeforeTerminalAsync,
            threadHistoryAppHostRuntime.GetTrackedAgentMessageText,
            EnsureAgentMessageStartedAsync,
            CompletePlanItemAsync,
            StartTurnActivity,
            CaptureThreadGitDiffAsync,
            turnId => codeModeRuntime!.DeactivateTurn(turnId),
            turnId => jsReplRuntime!.DisposeManagerAsync(turnId).AsTask(),
            BuildExplicitPluginInstructionsAsync,
            ResolveMentionedSkillsAsync,
            BuildSkillInjectionMessages,
            ResolveSkillEnvironmentDependenciesAsync,
            ResolveSkillMcpDependenciesAsync,
            cwd => ContextSlicingRuntimeHelpers.ResolveConfiguredBudgetProfile(BuildConfigReadSnapshotForRuntime(cwd).Config),
            ResolveContextOverlaySegmentsAsync,
            MaybeExtractMemoryFromCompletedTurnAsync,
            SendServerRequestAsync,
            (threadId, turnId, toolCallGate, toolName, arguments, context, cancellationToken) => toolExecutionAppHostRuntime!.ExecuteInlineToolCallAsync(
                threadId,
                turnId,
                toolCallGate,
                toolName,
                arguments,
                context,
                cancellationToken),
            ExtractProposedPlanText,
            autoCompactionAppHostRuntime.MaybeRunPreSamplingAutoCompactAsync,
            cwd => KernelRealtimeContextRuntimeHelpers.BuildRealtimeStartDeveloperInstruction(
                cwd,
                configCwd => BuildConfigReadSnapshotForRuntime(configCwd).Config),
            (context, cancellationToken) => toolExecutionAppHostRuntime!.ResolveResponsesNativeToolOptionsAsync(context, cancellationToken),
            toolItemLifecycleAppHostRuntime.EmitWebSearchOutputItemNotificationsAsync,
            toolItemLifecycleAppHostRuntime.EmitImageGenerationOutputItemNotificationsAsync,
            autoCompactionAppHostRuntime.MaybeBuildMidTurnAutoCompactedFollowUpInputAsync,
            (call, supportsParallelToolCalls, parallelExecution, state, context, cancellationToken) => modelFunctionToolCallRuntime!.ExecuteWithParallelLockAsync(
                call,
                supportsParallelToolCalls,
                parallelExecution,
                state,
                context,
                cancellationToken),
            diagnosticEventSink,
            diagnosticOperationScopeFactory,
            providerRequestPayloadArtifactWriter,
            sharedHttpClient,
            jsonOptions,
            DefaultModel,
            this.responsesStreamMaxRetries,
            this.responsesStreamIdleTimeout,
            this.responsesStreamRetryBaseDelay,
            MaxResponsesStreamRetryDelay,
            ApplyW3cTraceContext,
            toolRegistry);
        this.mcpServerSurfaceAppHostRuntime = new McpServerSurfaceAppHostRuntime(
            WriteErrorAsync,
            WriteResultAsync,
            WriteNotificationAsync,
            cancellationToken => LoadEffectiveConfigValuesAsync(cancellationToken),
            ResolveTianShuHomePath,
            threadStore,
            threadManager,
            mcpManager,
            runningTurnTasks,
            NextThreadId,
            BuildThreadSessionStateForNewThread,
            threadLifecycleAppHostRuntime.EnsureThreadRolloutMaterializedAsync,
            EmitExperimentalInstructionsDeprecationNoticeIfNeededAsync,
            threadLifecycleAppHostRuntime.LoadThreadRecordPreferringRolloutAsync,
            BuildDefaultThreadSession,
            (_, session, cancellationToken) => mcpServerSurfaceAppHostRuntime!.UpdateMcpSandboxStateAsync(
                session,
                cancellationToken),
            async (record, runtimeThread, prompt, session, cancellationToken) => await turnExecutionAppHostRuntime.StartBackgroundTurnAsync(
                record,
                runtimeThread,
                prompt,
                await BuildTurnRequestContextAsync(runtimeThread, session, JsonSerializer.SerializeToElement(new { }), cancellationToken).ConfigureAwait(false),
                runtimeThread.Session.PersistExtendedHistory,
                cancellationToken).ConfigureAwait(false),
            (threadId, turnId, prompt) => threadHistoryAppHostRuntime.SeedTrackedTurnUserMessage(threadId, turnId, prompt));
        this.commandExecAppHostRuntime = new KernelCommandExecAppHostRuntime(
            WriteErrorAsync,
            WriteResultAsync,
            WriteNotificationAsync,
            toolItemLifecycleAppHostRuntime.EmitCommandExecutionStartedNotificationAsync,
            toolItemLifecycleAppHostRuntime.EmitCommandExecutionCompletedNotificationAsync);
        this.fileSystemAppHostRuntime = new KernelFileSystemAppHostRuntime(
            WriteErrorAsync,
            WriteResultAsync,
            WriteNotificationAsync);
        this.feedbackAppHostRuntime = new KernelFeedbackAppHostRuntime(
            threadStore,
            WriteResultAsync,
            (id, code, message, cancellationToken) => WriteErrorAsync(id, code, message, cancellationToken));
        this.diagnosticsTraceQueryService = new KernelDiagnosticsTraceQueryService(threadStore.StateStore);
        this.userShellAppHostRuntime = new KernelUserShellAppHostRuntime(
            strictInputJsonOptions,
            threadStore,
            threadManager,
            runningTurns,
            NextTurnId,
            WriteErrorAsync,
            WriteResultAsync,
            BuildDefaultThreadSession,
            WriteThreadStatusChangedAsync,
            WriteNotificationAsync,
            FlushPendingTurnInterruptResponsesAsync,
            processExecutionAppHostRuntime.ExecuteCommandAsync,
            toolItemLifecycleAppHostRuntime.EmitCommandExecutionStartedNotificationAsync,
            toolItemLifecycleAppHostRuntime.EmitCommandExecutionCompletedNotificationAsync,
            threadHistoryAppHostRuntime.OverrideTrackedTurnItemRecordType,
            threadHistoryAppHostRuntime.FinalizeTrackedTurnHistory,
            IsEphemeralThreadAsync);
        this.windowsSandboxSurfaceAppHostRuntime = new KernelWindowsSandboxSurfaceAppHostRuntime(
            processExecutionAppHostRuntime.ExecuteCommandAsync,
            WriteResultAsync,
            (id, code, message, cancellationToken) => WriteErrorAsync(id, code, message, cancellationToken),
            WriteNotificationAsync);
        this.artifactsRuntime = new KernelArtifactsAppHostRuntime(
            threadStore.RolloutRecorder,
            _ => KernelArtifactsRuntimeHelpers.ResolveArtifactsRuntimeOptions(
                ResolveTianShuHomePath(),
                ReadTianShuEnvironment("TIANSHU_ARTIFACT_RUNTIME_VERSION"),
                ReadTianShuEnvironment("TIANSHU_ARTIFACT_CACHE_ROOT"),
                ReadTianShuEnvironment("TIANSHU_ARTIFACT_NODE_PATH")));
        this.codeModeRuntime = new KernelCodeModeAppHostRuntime(
            threadStore.RolloutRecorder,
            toolRegistry);
        this.codeModeProtocolAppHostRuntime = new KernelCodeModeProtocolAppHostRuntime(
            threadStore,
            threadManager,
            BuildDefaultThreadSession,
            NextTurnId,
            WriteErrorAsync,
            WriteResultAsync,
            (threadId, session) => BuildTurnRequestContext(threadId, session),
            (threadId, turnId, turnContext, request, cancellationToken) => toolRuntimeServicesAppHostRuntime!.ExecuteCodeModeAsync(
                threadId,
                turnId,
                turnContext,
                request,
                cancellationToken),
            (threadId, turnId, turnContext, request, cancellationToken) => toolRuntimeServicesAppHostRuntime!.WaitOnCodeModeAsync(
                threadId,
                turnId,
                turnContext,
                request,
                cancellationToken),
            turnId => codeModeRuntime.DeactivateTurn(turnId));
        this.execPolicyManager = PolicyStrategyRuntimeComposition.CreateExecPolicyManager(
            KernelStoragePaths.ResolveDefault().StateDirectory,
            policyStrategyPackage);
        this.jsReplRuntime = new KernelJsReplAppHostRuntime(threadStore.RolloutRecorder);
        this.managedNetworkRuntime = new KernelManagedNetworkAppHostRuntime(
            execPolicyManager,
            threadStore.RolloutRecorder,
            (cwd, skillAllowedDomains, skillDeniedDomains) => KernelManagedNetworkSettingsUtilities.ResolveManagedNetworkSettingsWithSkillOverride(
                BuildConfigReadSnapshotForRuntime(cwd),
                skillAllowedDomains,
                skillDeniedDomains),
            (method, payload, threadId, cancellationToken) => SendServerRequestAsync(method, payload, threadId, cancellationToken),
            (method, payload, cancellationToken) => WriteNotificationAsync(method, payload, cancellationToken));
        this.commandExecSurfaceAppHostRuntime = new KernelCommandExecSurfaceAppHostRuntime(
            threadId =>
            {
                if (!threadManager.TryGetThread(threadId, out var runtimeThread) || runtimeThread?.Session is null)
                {
                    return null;
                }

                var session = runtimeThread.Session;
                return new KernelCommandExecThreadSessionSnapshot(
                    session.Cwd,
                    session.ApprovalPolicy,
                    session.SandboxPolicy,
                    session.SandboxMode,
                    session.ShellEnvironmentPolicy);
            },
            ResolveConfiguredPermissionSettings,
            execPolicyManager,
            managedNetworkRuntime,
            commandExecAppHostRuntime,
            toolItemLifecycleAppHostRuntime,
            commandApprovalSessionKeysByThread,
            (method, payload, threadId, cancellationToken) => SendServerRequestAsync(method, payload, threadId, cancellationToken),
            processExecutionAppHostRuntime.ExecuteCommandAsync,
            async (command, cwd, threadId, processId, environment, managedNetworkLease, onExited, turnId, itemId, commandText) =>
            {
                return await processExecutionAppHostRuntime.StartBackgroundCommandAsync(
                    command,
                    cwd,
                    threadId,
                    processId,
                    environment,
                    managedNetworkLease,
                    onExited,
                    turnId,
                    itemId,
                    commandText,
                    toolItemLifecycleAppHostRuntime.EmitCommandExecutionStartedNotificationAsync).ConfigureAwait(false);
            },
            WriteResultAsync,
            (id, code, message, cancellationToken) => WriteErrorAsync(id, code, message, cancellationToken),
            WriteNotificationAsync);
        this.agentOrchestrationManager = new KernelAgentOrchestrationManager(threadStore.StateStore);
        this.agentJobsAppHostRuntime = new KernelAgentJobsAppHostRuntime(
            agentOrchestrationManager,
            (id, code, message, cancellationToken) => WriteErrorAsync(id, code, message, cancellationToken),
            WriteResultAsync);
        this.toolRuntimeAppHostRuntime = new KernelToolRuntimeAppHostRuntime(
            threadStore,
            threadManager,
            agentOrchestrationManager,
            pendingPermissionRequestsByCallId,
            runningTurns,
            runningTurnTasks,
            NextThreadId,
            spawnAgentGuardAppHostRuntime.ResolveSpawnAgentGuardConfigurationAsync,
            spawnAgentGuardAppHostRuntime.ReserveSpawnAgentSlot,
            spawnAgentGuardAppHostRuntime.IsTrackedSpawnAgentThread,
            spawnAgentGuardAppHostRuntime.ReleaseSpawnedAgentThread,
            ResolveSpawnAgentRoleAsync,
            LoadSpawnAgentRoleOverridesAsync,
            ResolveSpawnAgentNicknameCandidates,
            ReserveSpawnAgentNickname,
            RegisterSpawnAgentNickname,
            subagentNotificationAppHostRuntime.MaybeStartSubagentCompletionWatcher,
            threadHistoryAppHostRuntime.RegisterPendingTurnInterrupt,
            EnqueueSteerInput,
            BuildDefaultThreadSession,
            threadLifecycleAppHostRuntime.PersistThreadConfigSnapshotAsync,
            threadHistoryAppHostRuntime.BuildTrackedActiveTurnSnapshot,
            async (record, runtimeThread, userText, session, inputItems, enableAgentJobWorkerTools, cancellationToken) => await turnExecutionAppHostRuntime.StartBackgroundTurnAsync(
                record,
                runtimeThread,
                userText,
                (await BuildTurnRequestContextAsync(record.Id, session, cancellationToken).ConfigureAwait(false)) with
                {
                    InputItems = inputItems,
                    EnableAgentJobWorkerTools = enableAgentJobWorkerTools,
                },
                runtimeThread.Session.PersistExtendedHistory,
                cancellationToken).ConfigureAwait(false),
            agentId => threadLifecycleAppHostRuntime.TryGetRunningThread(agentId, out _),
            (method, payload, threadId, cancellationToken, timeoutOverride) => SendServerRequestAsync(
                method,
                payload,
                threadId,
                cancellationToken,
                timeoutOverride),
            (method, payload, cancellationToken) => WriteNotificationAsync(method, payload, cancellationToken));
        this.spawnAgentsOnCsvAppHostRuntime = new KernelSpawnAgentsOnCsvAppHostRuntime(
            threadStore,
            agentOrchestrationManager,
            spawnAgentGuardAppHostRuntime,
            (parentThreadId, requestContext, request, cancellationToken) => toolRuntimeAppHostRuntime.SpawnAgentAsync(
                parentThreadId,
                requestContext,
                request,
                cancellationToken),
            (agentId, cancellationToken) => toolRuntimeAppHostRuntime.CloseAgentAsync(agentId, cancellationToken),
            (agentId, treatArchivedAsNotFound, cancellationToken) => toolRuntimeAppHostRuntime.GetAgentStatusNodeAsync(
                agentId,
                treatArchivedAsNotFound,
                cancellationToken),
            (method, payload, cancellationToken) => WriteNotificationAsync(method, payload, cancellationToken),
            async (cwd, cancellationToken) =>
            {
                var snapshot = await BuildConfigReadSnapshotAsync(includeLayers: false, cwd, cancellationToken).ConfigureAwait(false);
                return snapshot.Config;
            });
        this.toolCallAppHostRuntime = new KernelToolCallAppHostRuntime(
            toolRegistry,
            this.toolExecutionHooks,
            execPolicyManager,
            commandApprovalSessionKeysByThread,
            fileChangeApprovalSessionPathsByThread,
            mcpToolApprovalSessionKeysByThread,
            grantedPermissionSessionByThread,
            grantedPermissionTurnByTurn,
            (descriptor, cwd, cancellationToken) => toolExecutionAppHostRuntime!.TryPersistDynamicToolApprovalAsync(
                descriptor,
                cwd,
                cancellationToken),
            (method, payload, threadId, cancellationToken, timeoutOverride) => SendServerRequestAsync(
                method,
                payload,
                threadId,
                cancellationToken,
                timeoutOverride),
            (method, payload, cancellationToken) => WriteNotificationAsync(method, payload, cancellationToken),
            toolItemLifecycleAppHostRuntime.EmitCollabToolCallStartedNotificationAsync,
            toolItemLifecycleAppHostRuntime.EmitCollabToolCallCompletedNotificationAsync,
            toolItemLifecycleAppHostRuntime.EmitMcpToolCallStartedNotificationAsync,
            toolItemLifecycleAppHostRuntime.EmitMcpToolCallCompletedNotificationAsync,
            toolItemLifecycleAppHostRuntime.EmitDynamicToolCallStartedNotificationAsync,
            toolItemLifecycleAppHostRuntime.EmitDynamicToolCallCompletedNotificationAsync,
            toolItemLifecycleAppHostRuntime.EmitFileChangeStartedNotificationAsync,
            toolItemLifecycleAppHostRuntime.EmitFileChangeCompletedNotificationAsync,
            toolItemLifecycleAppHostRuntime.EmitImageViewLifecycleNotificationsAsync);
        this.toolRuntimeServicesAppHostRuntime = new KernelToolRuntimeServicesAppHostRuntime(
            toolRuntimeAppHostRuntime,
            spawnAgentsOnCsvAppHostRuntime,
            mcpManager,
            artifactsRuntime,
            codeModeRuntime,
            jsReplRuntime,
            pluginsAppHostRuntime,
            threadManager,
            (context, cancellationToken) => toolExecutionAppHostRuntime!.ResolveResponsesNativeToolOptionsAsync(context, cancellationToken),
            (cwd, cancellationToken) => LoadMergedPersistedConfigTextAsync(cancellationToken, cwd),
            managedNetworkRuntime.BeginExecutionAsync,
            (threadId, turnId, itemId, toolName, arguments, turnContext, cancellationToken, customInput, isCustomToolCall) => ExecuteToolCallAsync(
                threadId,
                turnId,
                itemId,
                toolName,
                arguments,
                turnContext,
                toolCallGate: null,
                cancellationToken,
                customInput,
                isCustomToolCall),
            FilterMemoryForToolAsync,
            ResolveMemoryOverlayForToolAsync,
            RecordMemoryFeedbackForToolAsync);
        this.toolExecutionAppHostRuntime = new KernelToolExecutionAppHostRuntime(
            nativeToolOptionsAppHostRuntime,
            toolRuntimeServicesAppHostRuntime,
            toolRuntimeAppHostRuntime,
            toolCallAppHostRuntime,
            cwd => BuildConfigReadSnapshotForRuntime(cwd).Config,
            RequestMcpServerElicitationAsync,
            (threadId, turnId, request, cancellationToken) => toolRuntimeAppHostRuntime.RequestUserInputAsync(
                threadId,
                turnId,
                request,
                cancellationToken),
            (threadId, turnId, request, cancellationToken) => toolRuntimeAppHostRuntime.RequestPermissionsAsync(
                threadId,
                turnId,
                request,
                cancellationToken),
            (cancellationToken, cwd) => LoadWritablePersistedConfigValuesAsync(cancellationToken, cwd: cwd),
            (values, cancellationToken, cwd) => SaveConfigValuesAsync(values, cancellationToken, cwd: cwd));
        this.modelFunctionToolCallRuntime = new KernelModelFunctionToolCallRuntime(
            PersistTurnLogAsync,
            (threadId, turnId, itemId, toolName, arguments, turnContext, toolCallGate, cancellationToken, customInput, isCustomToolCall, externalCallId) =>
                toolExecutionAppHostRuntime.ExecuteToolCallAsync(
                    threadId,
                    turnId,
                    itemId,
                    toolName,
                    arguments,
                    turnContext,
                    toolCallGate,
                    cancellationToken,
                    customInput,
                    isCustomToolCall,
                    externalCallId));
        this.pendingInteractiveReplayAppHostRuntime = new KernelPendingInteractiveReplayAppHostRuntime(
            jsonOptions,
            pendingServerResponses,
            pendingPermissionRequestsByCallId,
            CleanupApprovalRequestMapping,
            CleanupPendingUserInputRequestMapping,
            RecordGrantedPermissions,
            (threadId, turnId) => threadHistoryAppHostRuntime.HasPendingTurnInterrupt(threadId, turnId),
            WriteMessageAsync,
            WriteErrorAsync,
            WriteResultAsync,
            WriteNotificationAsync);
        this.providerHttpClient = sharedHttpClient;
    }

    private async Task HandleThreadStartAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var request = await TryDeserializeStrictParamsAsync<KernelThreadStartRequest>(id, @params, "thread/start", cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return;
        }

        await threadLifecycleAppHostRuntime.HandleThreadStartAsync(id, request, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleThreadListAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        await threadLifecycleAppHostRuntime.HandleThreadListAsync(id, @params, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleThreadResumeAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var request = await TryDeserializeStrictParamsAsync<KernelThreadResumeRequest>(id, @params, "thread/resume", cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return;
        }

        await threadLifecycleAppHostRuntime.HandleThreadResumeAsync(id, request, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleThreadReadAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var threadId = await ValidateThreadIdAsync(id, ReadString(@params, "threadId"), cancellationToken).ConfigureAwait(false);
        if (threadId is null)
        {
            return;
        }

        var includeTurns = ReadBool(@params, "includeTurns")
                           ?? ReadBool(@params, "include_turns")
                           ?? false;
        await threadLifecycleAppHostRuntime.HandleThreadReadAsync(id, threadId, includeTurns, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleThreadForkAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var request = await TryDeserializeStrictParamsAsync<KernelThreadForkRequest>(id, @params, "thread/fork", cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return;
        }

        await threadLifecycleAppHostRuntime.HandleThreadForkAsync(id, request, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleThreadIncrementElicitationAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var request = await TryDeserializeStrictParamsAsync<KernelThreadElicitationRequest>(
                id,
                @params,
                "thread/increment_elicitation",
                cancellationToken)
            .ConfigureAwait(false);
        if (request is null)
        {
            return;
        }

        await threadLifecycleAppHostRuntime.HandleThreadIncrementElicitationAsync(id, request, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleThreadDecrementElicitationAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var request = await TryDeserializeStrictParamsAsync<KernelThreadElicitationRequest>(
                id,
                @params,
                "thread/decrement_elicitation",
                cancellationToken)
            .ConfigureAwait(false);
        if (request is null)
        {
            return;
        }

        await threadLifecycleAppHostRuntime.HandleThreadDecrementElicitationAsync(id, request, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleAgentThreadRegisterAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        await threadLifecycleAppHostRuntime.HandleAgentThreadRegisterAsync(id, @params, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleThreadSetNameAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        await threadLifecycleAppHostRuntime.HandleThreadSetNameAsync(id, @params, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleThreadMetadataUpdateAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var request = await TryDeserializeStrictParamsAsync<KernelThreadMetadataUpdateRequest>(
                id,
                @params,
                "thread/metadata/update",
                cancellationToken)
            .ConfigureAwait(false);
        if (request is null)
        {
            return;
        }

        await threadLifecycleAppHostRuntime.HandleThreadMetadataUpdateAsync(id, request, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleThreadPendingInputStateUpdateAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var request = await TryDeserializeStrictParamsAsync<KernelThreadPendingInputStateUpdateRequest>(
                id,
                @params,
                "tianshu/thread/pending_input/update",
                cancellationToken)
            .ConfigureAwait(false);
        if (request is null)
        {
            return;
        }

        await threadLifecycleAppHostRuntime.HandleThreadPendingInputStateUpdateAsync(id, request, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleTurnStartAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var request = await TryDeserializeStrictParamsAsync<KernelTurnStartRequest>(id, @params, "turn/start", cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return;
        }

        await turnExecutionAppHostRuntime.HandleTurnStartAsync(id, request, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleTurnInterruptAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var threadId = ReadString(@params, "threadId");
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await WriteErrorAsync(id, -32602, "threadId 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var turnId = ReadString(@params, "turnId");
        if (string.IsNullOrWhiteSpace(turnId))
        {
            await WriteErrorAsync(id, -32602, "turnId 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        await turnExecutionAppHostRuntime.HandleTurnInterruptAsync(id, threadId!, turnId!, cancellationToken).ConfigureAwait(false);
    }

    private static string? NormalizeCliConfigFilePath(string? value)
    {
        var normalized = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return Path.GetFullPath(normalized);
    }

    private static IReadOnlyList<TianShuExternalMemoryProviderOptions> LoadExternalMemoryProviderOptions(string? cwd = null)
    {
        try
        {
            return TianShuMemoryProviderConfigurationLoader.LoadDefault(cwd)
                .Where(static provider => provider.Enabled && !IsLocalMemoryProviderKind(provider.Kind))
                .Select(static provider => new TianShuExternalMemoryProviderOptions
                {
                    ProviderId = provider.ProviderId,
                    Kind = provider.Kind,
                    DisplayName = provider.DisplayName,
                    Enabled = provider.Enabled,
                    Host = provider.Host,
                    Port = provider.Port,
                    GrpcPort = provider.GrpcPort,
                    ApiKeyEnvironmentVariable = provider.ApiKeyEnvironmentVariable,
                    AuthorizationEnvironmentVariable = provider.AuthorizationEnvironmentVariable,
                    Mode = ParseMemoryProviderMode(provider.Mode),
                    Capabilities = provider.Capabilities,
                    ConnectTimeout = TimeSpan.FromMilliseconds(Math.Max(1, provider.ConnectTimeoutMs ?? 750)),
                })
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Array.Empty<TianShuExternalMemoryProviderOptions>();
        }
    }

    private TianShuMemoryRuntimeOptions ResolveMemoryRuntimeOptions(string? cwd)
    {
        var config = BuildConfigReadSnapshotForRuntime(cwd).Config;
        var profiles = ReadMemoryProfiles(config);
        return new TianShuMemoryRuntimeOptions
        {
            Enabled = ReadConfigBoolean(config, "memory.enabled") ?? true,
            DefaultProfileId = ReadConfigString(config, "memory.default_profile") ?? "workspace",
            Profiles = profiles,
            Spaces = ReadMemorySpaces(config),
            Bindings = ReadMemoryBindings(config),
        };
    }

    private static IReadOnlyList<TianShuMemoryProfileOptions> ReadMemoryProfiles(Dictionary<string, object?> config)
    {
        var profileIds = ReadConfigSectionIds(config, "memory_profiles");
        if (profileIds.Count == 0)
        {
            return [TianShuMemoryProfileOptions.Default];
        }

        return profileIds
            .Select(profileId => new TianShuMemoryProfileOptions(
                profileId,
                ReadConfigBoolean(config, $"memory_profiles.{profileId}.enabled") ?? true,
                ReadConfigString(config, $"memory_profiles.{profileId}.default_space"),
                ReadConfigBoolean(config, $"memory_profiles.{profileId}.overlay") ?? true,
                ParseMemoryExtractMode(ReadConfigString(config, $"memory_profiles.{profileId}.extract")),
                ReadConfigString(config, $"memory_profiles.{profileId}.retention") ?? "keep"))
            .ToArray();
    }

    private static IReadOnlyList<TianShuMemorySpaceOptions> ReadMemorySpaces(Dictionary<string, object?> config)
        => ReadConfigSectionIds(config, "memory.spaces")
            .Select(spaceId => new TianShuMemorySpaceOptions(
                spaceId,
                ParseMemoryScopeKind(ReadConfigString(config, $"memory.spaces.{spaceId}.scope")),
                ReadConfigString(config, $"memory.spaces.{spaceId}.provider"),
                ReadConfigBoolean(config, $"memory.spaces.{spaceId}.read_only") ?? false,
                ReadConfigString(config, $"memory.spaces.{spaceId}.display_name"),
                ReadConfigString(config, $"memory.spaces.{spaceId}.scope_key")))
            .ToArray();

    private static IReadOnlyList<TianShuMemoryProviderBindingOptions> ReadMemoryBindings(Dictionary<string, object?> config)
        => ReadConfigSectionIds(config, "memory.bindings")
            .Select(bindingId => new TianShuMemoryProviderBindingOptions(
                bindingId,
                ReadConfigString(config, $"memory.bindings.{bindingId}.space") ?? bindingId,
                ReadConfigString(config, $"memory.bindings.{bindingId}.provider") ?? TianShuLocalMemoryProvider.DefaultProviderId,
                ReadConfigString(config, $"memory.bindings.{bindingId}.mode") ?? "read-only",
                ReadConfigStringArray(config, $"memory.bindings.{bindingId}.capabilities")))
            .ToArray();

    private static IReadOnlyList<string> ReadConfigSectionIds(Dictionary<string, object?> config, string prefix)
    {
        var matchPrefix = prefix + ".";
        return config.Keys
            .Where(key => key.StartsWith(matchPrefix, StringComparison.Ordinal))
            .Select(key => key[matchPrefix.Length..])
            .Select(key =>
            {
                var dot = key.IndexOf('.');
                return dot < 0 ? key : key[..dot];
            })
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ReadConfigString(Dictionary<string, object?> config, string key)
    {
        if (!config.TryGetValue(key, out var value))
        {
            return null;
        }

        var text = value?.ToString()?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static bool? ReadConfigBoolean(Dictionary<string, object?> config, string key)
    {
        if (!config.TryGetValue(key, out var value))
        {
            return null;
        }

        if (value is bool boolean)
        {
            return boolean;
        }

        return bool.TryParse(value?.ToString(), out var parsed) ? parsed : null;
    }

    private static IReadOnlyList<string> ReadConfigStringArray(Dictionary<string, object?> config, string key)
    {
        if (!config.TryGetValue(key, out var value) || value is null)
        {
            return Array.Empty<string>();
        }

        if (value is IEnumerable<string> strings)
        {
            return strings.Where(static item => !string.IsNullOrWhiteSpace(item)).ToArray();
        }

        if (value is IEnumerable<object?> values && value is not string)
        {
            return values
                .Select(static item => item?.ToString())
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToArray();
        }

        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ? Array.Empty<string>() : [text.Trim()];
    }

    private static TianShuMemoryExtractMode ParseMemoryExtractMode(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "off" => TianShuMemoryExtractMode.Off,
            "background" => TianShuMemoryExtractMode.Background,
            _ => TianShuMemoryExtractMode.Manual,
        };

    private static MemoryScopeKind ParseMemoryScopeKind(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "user" => MemoryScopeKind.User,
            "team" => MemoryScopeKind.Team,
            "session" => MemoryScopeKind.Session,
            "agent" => MemoryScopeKind.Agent,
            "collaboration" => MemoryScopeKind.Collaboration,
            _ => MemoryScopeKind.Workspace,
        };

    private static bool IsLocalMemoryProviderKind(string? kind)
        => string.Equals(kind, "local", StringComparison.OrdinalIgnoreCase)
           || string.Equals(kind, "tianshu.local", StringComparison.OrdinalIgnoreCase);

    private static MemoryProviderBindingMode ParseMemoryProviderMode(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "read-write" or "readwrite" => MemoryProviderBindingMode.ReadWrite,
            "mirror" => MemoryProviderBindingMode.Mirror,
            "import-export" or "importexport" => MemoryProviderBindingMode.ImportExport,
            _ => MemoryProviderBindingMode.ReadOnly,
        };

    internal static string? BuildScopedDeveloperInstructions(
        string? cwd,
        string? configuredDeveloperInstructions,
        Dictionary<string, object?>? scopedConfig = null)
        => KernelInstructionScopeUtilities.BuildScopedDeveloperInstructions(cwd, configuredDeveloperInstructions, scopedConfig);

    internal static string? BuildScopedUserInstructions(
        string? cwd,
        Dictionary<string, object?>? scopedConfig = null,
        string? homePath = null)
        => KernelInstructionScopeUtilities.BuildScopedUserInstructions(cwd, scopedConfig, homePath);

    private static string? SerializeUserInstructions(string? cwd, string? instructions)
        => KernelInstructionScopeUtilities.SerializeUserInstructions(cwd, instructions);

    private static bool IsPlanCollaborationMode(KernelCollaborationModeState? state)
        => string.Equals(
            Normalize(state?.Mode),
            KernelCollaborationModeState.PlanMode,
            StringComparison.OrdinalIgnoreCase);

    private static KernelProposedPlanExtraction ExtractProposedPlanText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new KernelProposedPlanExtraction(string.Empty, string.Empty, false);
        }

        var parser = new KernelProposedPlanStreamParser();
        _ = parser.Append(text);
        return parser.Complete();
    }

    private async Task HandleDebugClearMemoriesAsync(JsonElement id, CancellationToken cancellationToken)
    {
        var stateDbPath = threadStore.StateStore.DatabasePath;
        var clearedStage1OutputCount = await threadStore.StateStore.ClearMemoriesAsync(cancellationToken).ConfigureAwait(false);
        var disabledThreadCount = await threadStore.DisableEnabledThreadMemoryModesAsync(cancellationToken).ConfigureAwait(false);

        var memoryRootPath = Path.GetFullPath(TianShuHomePathUtilities.ResolveDataPathFromHome(
            TianShuHomePathUtilities.ResolveTianShuHomePath(),
            "memory"));
        var removedMemoryRoot = false;
        if (Directory.Exists(memoryRootPath))
        {
            Directory.Delete(memoryRootPath, recursive: true);
            removedMemoryRoot = true;
        }

        await WriteResultAsync(id, new
        {
            stateDbPath,
            clearedStage1OutputCount,
            disabledThreadCount,
            memoryRootPath,
            removedMemoryRoot,
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ContextSegment>> ResolveContextOverlaySegmentsAsync(
        string threadId,
        TurnRequestContext context,
        CancellationToken cancellationToken)
    {
        var memoryMode = await threadStore.GetThreadMemoryModeAsync(threadId, cancellationToken).ConfigureAwait(false);
        if (memoryMode is null or ThreadMemoryMode.Disabled)
        {
            return [];
        }

        var identityContext = BuildIdentityMemoryContextForTurn(threadId, context);
        var overlay = await identityMemoryPlane.ResolveMemoryOverlayAsync(
                new ResolveMemoryOverlay(QueryText: BuildMemoryOverlayQueryText(context)),
                identityContext,
                cancellationToken)
            .ConfigureAwait(false);
        await RecordMemoryOverlayCitationAsync(overlay, identityContext, cancellationToken).ConfigureAwait(false);
        var overlayText = BuildMemoryOverlayText(overlay);
        if (string.IsNullOrWhiteSpace(overlayText))
        {
            var memory = await threadStore.StateStore.GetMemoryAsync(threadId, cancellationToken).ConfigureAwait(false);
            overlayText = Normalize(memory?.RawMemory);
            if (string.IsNullOrWhiteSpace(overlayText))
            {
                return [];
            }
        }

        var contextSignature = Normalize(context.Cwd);
        return
        [
            ContextSlicingRuntimeHelpers.CreateMemoryOverlaySegment(
                id: $"memory-overlay-{threadId}",
                text: overlayText!,
                sourceId: threadId,
                contextSignature: contextSignature),
        ];
    }

    private async Task MaybeExtractMemoryFromCompletedTurnAsync(
        string threadId,
        string turnId,
        string userText,
        string assistantText,
        TurnRequestContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var memoryMode = await threadStore.GetThreadMemoryModeAsync(threadId, cancellationToken).ConfigureAwait(false);
            if (memoryMode is not null and not ThreadMemoryMode.ReadWrite)
            {
                return;
            }

            var options = ResolveMemoryRuntimeOptions(context.Cwd);
            var profile = options.ResolveDefaultProfile();
            if (!options.Enabled || !profile.Enabled || profile.Extract != TianShuMemoryExtractMode.Background)
            {
                return;
            }

            var identityContext = BuildIdentityMemoryContextForTurn(threadId, context);
            var spaces = await identityMemoryPlane
                .ListMemorySpacesAsync(new ListMemorySpaces(), identityContext, cancellationToken)
                .ConfigureAwait(false);
            var targetSpace = ResolveMemoryExtractionTargetSpace(profile.DefaultSpace, spaces);
            if (targetSpace is null || string.IsNullOrWhiteSpace(userText))
            {
                return;
            }

            var source = new MemorySourceRef(
                MemorySourceKind.Conversation,
                $"{threadId}:{turnId}",
                role: "user",
                snippet: userText,
                capturedAt: DateTimeOffset.UtcNow,
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["threadId"] = threadId,
                    ["turnId"] = turnId,
                    ["assistantTextLength"] = assistantText.Length.ToString(CultureInfo.InvariantCulture),
                    ["extractMode"] = "background",
                });
            var candidates = await identityMemoryPlane
                .ExtractMemoryAsync(
                    new ExtractMemory(targetSpace.Id, source, StructuredValue.FromString(userText)),
                    identityContext,
                    cancellationToken)
                .ConfigureAwait(false);
            if (candidates.Count > 0)
            {
                await WriteNotificationAsync("memory/extraction/completed", new
                {
                    threadId,
                    turnId,
                    memorySpaceId = targetSpace.Id.Value,
                    candidates = candidates.Count,
                }, cancellationToken).ConfigureAwait(false);
            }

            if (ShouldRunRetentionConsolidation(profile.Retention))
            {
                var consolidation = await identityMemoryPlane
                    .RunMemoryConsolidationAsync(
                        new RunMemoryConsolidation(targetSpace.Id),
                        identityContext,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (consolidation.ProposalsCreated > 0)
                {
                    await WriteNotificationAsync("memory/retention/proposed", new
                    {
                        threadId,
                        turnId,
                        memorySpaceId = targetSpace.Id.Value,
                        proposals = consolidation.ProposalsCreated,
                    }, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            // 记忆抽取是 turn 完成后的维护动作，失败不回滚已完成对话。
        }
    }

    private static MemorySpace? ResolveMemoryExtractionTargetSpace(
        string? defaultSpace,
        IReadOnlyList<MemorySpace> spaces)
    {
        if (spaces.Count == 0)
        {
            return null;
        }

        var normalizedDefaultSpace = Normalize(defaultSpace);
        if (normalizedDefaultSpace is not null)
        {
            var configured = spaces.FirstOrDefault(space => string.Equals(space.Id.Value, normalizedDefaultSpace, StringComparison.Ordinal))
                             ?? spaces.FirstOrDefault(space => string.Equals(space.ScopeKey, normalizedDefaultSpace, StringComparison.Ordinal))
                             ?? spaces.FirstOrDefault(space => string.Equals(ToMemoryScopeSegment(space.ScopeKind), normalizedDefaultSpace, StringComparison.OrdinalIgnoreCase));
            if (configured is not null)
            {
                return configured;
            }
        }

        return spaces.FirstOrDefault(static space => space.ScopeKind == MemoryScopeKind.Workspace)
               ?? spaces.FirstOrDefault(static space => space.ScopeKind == MemoryScopeKind.Session)
               ?? spaces[0];
    }

    private static bool ShouldRunRetentionConsolidation(string? retention)
        => string.Equals(retention, "archive", StringComparison.OrdinalIgnoreCase)
           || string.Equals(retention, "forget", StringComparison.OrdinalIgnoreCase);

    private static string ToMemoryScopeSegment(MemoryScopeKind scopeKind)
        => scopeKind switch
        {
            MemoryScopeKind.User => "user",
            MemoryScopeKind.Team => "team",
            MemoryScopeKind.Session => "session",
            MemoryScopeKind.Agent => "agent",
            MemoryScopeKind.Collaboration => "collaboration",
            _ => "workspace",
        };

    private static string? BuildMemoryOverlayQueryText(TurnRequestContext context)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(context.UserInstructions))
        {
            parts.Add(context.UserInstructions!);
        }

        if (!string.IsNullOrWhiteSpace(context.ReviewDisplayText))
        {
            parts.Add(context.ReviewDisplayText!);
        }

        if (!string.IsNullOrWhiteSpace(context.Cwd))
        {
            parts.Add(context.Cwd!);
        }

        foreach (var item in context.InputItems ?? Array.Empty<KernelTurnInputItem>())
        {
            AppendMemoryOverlayQueryText(parts, item);
        }

        return parts.Count == 0 ? null : string.Join(Environment.NewLine, parts);
    }

    private static void AppendMemoryOverlayQueryText(List<string> parts, KernelTurnInputItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Text))
        {
            parts.Add(item.Text!);
        }

        if (!string.IsNullOrWhiteSpace(item.Name))
        {
            parts.Add(item.Name!);
        }

        if (!string.IsNullOrWhiteSpace(item.CanonicalPath))
        {
            parts.Add(item.CanonicalPath!);
        }
        else if (!string.IsNullOrWhiteSpace(item.Path))
        {
            parts.Add(item.Path!);
        }

        foreach (var child in item.ContentItems)
        {
            AppendMemoryOverlayQueryText(parts, child);
        }
    }

    private Task<MemoryQueryResult> FilterMemoryForToolAsync(
        string threadId,
        TurnRequestContext context,
        FilterMemory query,
        CancellationToken cancellationToken)
        => identityMemoryPlane.FilterMemoryAsync(
            query,
            BuildIdentityMemoryContextForTurn(threadId, context),
            cancellationToken);

    private Task<MemoryOverlay> ResolveMemoryOverlayForToolAsync(
        string threadId,
        TurnRequestContext context,
        ResolveMemoryOverlay query,
        CancellationToken cancellationToken)
        => identityMemoryPlane.ResolveMemoryOverlayAsync(
            string.IsNullOrWhiteSpace(query.QueryText)
                ? query with { QueryText = BuildMemoryOverlayQueryText(context) }
                : query,
            BuildIdentityMemoryContextForTurn(threadId, context),
            cancellationToken);

    private Task<MemoryMutationResult> RecordMemoryFeedbackForToolAsync(
        string threadId,
        TurnRequestContext context,
        RecordMemoryFeedback command,
        CancellationToken cancellationToken)
        => identityMemoryPlane.RecordMemoryFeedbackAsync(
            command,
            BuildIdentityMemoryContextForTurn(threadId, context),
            cancellationToken);

    private TianShuIdentityMemoryContext BuildIdentityMemoryContextForTurn(string threadId, TurnRequestContext context)
    {
        var userName = Normalize(ReadTianShuEnvironment("TIANSHU_IDENTITY_DISPLAY_NAME"))
                       ?? Normalize(Environment.UserName)
                       ?? "local-user";
        return new TianShuIdentityMemoryContext(
            runtimeName: "tianshu-apphost",
            accountId: new AccountId($"local-account:{NormalizeMemorySegment(userName)}"),
            accountDisplayName: userName,
            deviceName: Normalize(ReadTianShuEnvironment("TIANSHU_DEVICE_NAME"))
                        ?? Normalize(Environment.MachineName)
                        ?? "local-device",
            platform: Environment.OSVersion.Platform.ToString(),
            workingDirectory: Normalize(context.Cwd),
            activeThreadId: Normalize(threadId),
            teamKey: Normalize(ReadTianShuEnvironment("TIANSHU_TEAM_KEY")),
            collaborationSpaceId: Normalize(ReadTianShuEnvironment("TIANSHU_COLLABORATION_SPACE_ID")),
            preferredVerbosity: Normalize(context.Verbosity)
                                ?? Normalize(ReadTianShuEnvironment("TIANSHU_MEMORY_PREFERRED_VERBOSITY")),
            preferredTools: ResolveIdentityMemoryPreferredTools(),
            snapshotTime: DateTimeOffset.UtcNow);
    }

    private async Task RecordMemoryOverlayCitationAsync(
        MemoryOverlay overlay,
        TianShuIdentityMemoryContext context,
        CancellationToken cancellationToken)
    {
        if (overlay.Facts.Count == 0)
        {
            return;
        }

        var citation = new MemoryCitation(
            overlay.Facts.Select(static fact => new MemoryCitationEntry(
                    fact.Id,
                    fact.MemorySpaceId,
                    fact.Key,
                    fact.Sources.FirstOrDefault(),
                    fact.IsCounterexample ? "context-overlay-counterexample" : "context-overlay"))
                .ToArray());

        await identityMemoryPlane.RecordMemoryCitationAsync(
                new RecordMemoryCitation(citation),
                context,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private string? BuildMemoryOverlayText(MemoryOverlay overlay)
    {
        if (overlay.Facts.Count == 0
            && overlay.HabitProfile is null)
        {
            return null;
        }

        var builder = new StringBuilder();
        if (overlay.Facts.Count > 0)
        {
            builder.AppendLine("记忆事实：");
            foreach (var fact in overlay.Facts)
            {
                builder.Append("- ");
                if (fact.IsCounterexample)
                {
                    builder.Append("[反例] ");
                }

                builder.Append(fact.Key);
                builder.Append(" = ");
                builder.Append(JsonSerializer.Serialize(fact.Value.ToPlainObject(), jsonOptions));
                builder.Append(" (confidence=");
                builder.Append(fact.Confidence.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine(")");
            }
        }

        var habit = overlay.HabitProfile;
        if (habit is not null
            && (!string.IsNullOrWhiteSpace(habit.PreferredVerbosity) || habit.PreferredTools.Count > 0 || habit.Labels.Values.Count > 0))
        {
            builder.AppendLine("用户习惯：");
            if (!string.IsNullOrWhiteSpace(habit.PreferredVerbosity))
            {
                builder.AppendLine($"- verbosity = {habit.PreferredVerbosity}");
            }

            if (habit.PreferredTools.Count > 0)
            {
                builder.AppendLine($"- preferredTools = {string.Join(", ", habit.PreferredTools)}");
            }

            foreach (var label in habit.Labels.Values)
            {
                builder.AppendLine($"- label = {label}");
            }
        }

        return Normalize(builder.ToString());
    }

    private static IReadOnlyList<string> ResolveIdentityMemoryPreferredTools()
    {
        var raw = Normalize(ReadTianShuEnvironment("TIANSHU_MEMORY_PREFERRED_TOOLS"));
        return raw is null
            ? []
            : raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static string NormalizeMemorySegment(string value)
    {
        var normalized = Regex.Replace(value.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-", RegexOptions.CultureInvariant);
        normalized = normalized.Trim('-');
        return normalized.Length == 0 ? "local-user" : normalized;
    }

    private static string? ReadTianShuEnvironment(string name)
        => Environment.GetEnvironmentVariable(name);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await threadStore.InitializeAsync(cancellationToken).ConfigureAwait(false);

        var queues = new KernelQueuePair<KernelInboundMessage, string>();
        activeQueues = queues;
        globalNotificationRegistration = globalNotificationHub?.Register(TryPublishGlobalMessage, globalConnectionDisconnect);
        var createdThreadEvents = threadManager.SubscribeCreated();
        var fileWatcherEvents = threadManager.SubscribeFileWatcher();

        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var processingTask = ProcessInboundMessagesAsync(queues, runCts.Token);
        var outputTask = ProcessOutboundWritesAsync(queues);
        var threadCreatedTask = ProcessThreadCreatedEventsAsync(createdThreadEvents, runCts.Token);
        var fileWatcherTask = ProcessFileWatcherEventsAsync(fileWatcherEvents, runCts.Token);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await input.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                line = line.TrimStart('\uFEFF');

                JsonDocument? document = null;
                try
                {
                    document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (root.TryGetProperty("method", out var methodElement))
                    {
                        var method = methodElement.GetString();
                        if (string.IsNullOrWhiteSpace(method))
                        {
                            continue;
                        }

                        root.TryGetProperty("params", out var paramsElement);

                        JsonElement? idClone = null;
                        if (root.TryGetProperty("id", out var idElement))
                        {
                            idClone = idElement.Clone();
                        }

                        var paramsClone = paramsElement.ValueKind == JsonValueKind.Undefined
                            ? default
                            : paramsElement.Clone();

                        var inbound = new KernelInboundMessage(method, paramsClone, idClone);
                        if (inbound.IsRequest)
                        {
                            // 对齐 AppHost 的 bounded ingress：当队列已满时，拒绝新请求并返回 retryable 错误。
                            if (!queues.TryEnqueueSubmission(inbound))
                            {
                                await WriteErrorAsync(
                                        inbound.Id,
                                        -32001,
                                        "Server overloaded; retry later.",
                                        cancellationToken)
                                    .ConfigureAwait(false);
                            }

                            continue;
                        }

                        if (!queues.TryEnqueueSubmission(inbound))
                        {
                            await queues.EnqueueSubmissionAsync(inbound, cancellationToken).ConfigureAwait(false);
                        }

                        continue;
                    }

                    if (TryResolveServerResponse(root))
                    {
                        continue;
                    }
                }
                catch (JsonException ex)
                {
                    await WriteErrorAsync(null, -32700, $"无效 JSON：{ex.Message}", cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    document?.Dispose();
                }
            }
        }
        finally
        {
            queues.CompleteSubmissions();

            try
            {
                await processingTask.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                runCts.Cancel();

                try
                {
                    await processingTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                }
                catch
                {
                    // ignore forced shutdown wait failures
                }
            }
            catch
            {
                // ignore background processing failures
            }

            await ShutdownAsync().ConfigureAwait(false);

            queues.CompleteEvents();

            try
            {
                await outputTask.ConfigureAwait(false);
            }
            catch
            {
                // ignore outbound write failures
            }

            runCts.Cancel();

            try
            {
                await threadCreatedTask.ConfigureAwait(false);
            }
            catch
            {
                // ignore thread-created broadcast shutdown failures
            }

            try
            {
                await fileWatcherTask.ConfigureAwait(false);
            }
            catch
            {
                // ignore file watcher shutdown failures
            }

            try
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // rollout writer 清理失败不应覆盖主关闭路径。
            }

            try
            {
                await fileSystemAppHostRuntime.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // ignore fs watch shutdown failures
            }

            globalNotificationRegistration?.Dispose();
            globalNotificationRegistration = null;
            activeQueues = null;
        }
    }

    private async Task ProcessInboundMessagesAsync(
        KernelQueuePair<KernelInboundMessage, string> queues,
        CancellationToken cancellationToken)
    {
        await foreach (var message in queues.ReadSubmissionsAsync(cancellationToken))
        {
            try
            {
                if (message.IsRequest)
                {
                    await HandleRequestAsync(message.Id!.Value, message.Method, message.Params, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await HandleNotificationAsync(message.Method, message.Params, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (KernelJsonRpcException ex)
            {
                if (message.IsRequest)
                {
                    await WriteErrorAsync(message.Id, ex.Code, ex.Message, ex.DataPayload, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                if (message.IsRequest)
                {
                    await WriteErrorAsync(message.Id, -32603, ex.Message, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task ProcessOutboundWritesAsync(KernelQueuePair<KernelInboundMessage, string> queues)
    {
        await foreach (var json in queues.ReadEventsAsync(CancellationToken.None))
        {
            await WriteMessageDirectAsync(json, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task ProcessThreadCreatedEventsAsync(ChannelReader<string> createdThreads, CancellationToken cancellationToken)
    {
        await foreach (var _ in createdThreads.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            pluginsAppHostRuntime.ClearAppListCache();
        }
    }

    private async Task ProcessFileWatcherEventsAsync(ChannelReader<KernelFileWatcherEvent> fileWatcherEvents, CancellationToken cancellationToken)
    {
        await foreach (var evt in fileWatcherEvents.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (evt.Kind != KernelFileWatcherEventKind.SkillsChanged)
            {
                continue;
            }

            skillsManager.ClearCache();
            await WriteBroadcastNotificationAsync("skills/changed", new { }, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PersistTurnLogAsync(
        string threadId,
        string turnId,
        string phase,
        string status,
        string? summary,
        object payload,
        CancellationToken cancellationToken)
    {
        if (await IsEphemeralThreadAsync(threadId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await threadStore.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await threadStore.StateStore.AppendTurnLogAsync(
            threadId,
            turnId,
            phase,
            status,
            summary,
            payload,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task TryPersistTurnLogDiagnosticAsync(
        string threadId,
        string turnId,
        string phase,
        string status,
        string? summary,
        object payload,
        CancellationToken cancellationToken)
    {
        try
        {
            await PersistTurnLogAsync(
                threadId,
                turnId,
                phase,
                status,
                summary,
                payload,
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // 诊断日志失败不能反过来影响主链路收尾。
        }
    }

    private async Task PersistRolloutAsync(
        string threadId,
        string turnId,
        string source,
        string rolloutPath,
        string? preview,
        object payload,
        CancellationToken cancellationToken)
    {
        if (await IsEphemeralThreadAsync(threadId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await threadStore.StateStore.UpsertRolloutAsync(
            $"{threadId}/{turnId}",
            threadId,
            turnId,
            source,
            rolloutPath,
            Normalize(preview),
            payload,
            cancellationToken).ConfigureAwait(false);
    }
    private async Task ShutdownAsync()
    {
        try
        {
            var tasks = runningTurnTasks.Values.ToArray();
            if (tasks.Length > 0)
            {
                try
                {
                    // 尽量优雅收敛：先给进行中的 Turn 一段时间完成输出，再考虑强制取消。
                    await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    CancelRunningTurns();

                    try
                    {
                        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    }
                    catch
                    {
                        // ignore forced shutdown wait failures
                    }
                }
            }
        }
        catch
        {
            // ignore shutdown wait failures
        }

        CancelRunningTurns();
        await codeModeRuntime.DisposeAsync().ConfigureAwait(false);
        await realtimeAppHostRuntime.ShutdownRealtimeSessionsAsync().ConfigureAwait(false);
        await managedNetworkRuntime.DisposeAsync().ConfigureAwait(false);
        commandExecAppHostRuntime.DisposeTrackedCommandExecSessions();
        await processExecutionAppHostRuntime.DisposeAsync().ConfigureAwait(false);

        try
        {
            await mcpServerSurfaceAppHostRuntime.WaitForPendingOauthNotificationsAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // ignore oauth completion wait failures
        }

    }

    private void CancelRunningTurns()
    {
        foreach (var entry in runningTurns.Values)
        {
            try
            {
                entry.Cancel();
            }
            catch
            {
                // ignore cancellation failures
            }
        }
    }

    private async Task HandleRequestAsync(
        JsonElement id,
        string method,
        JsonElement @params,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(method, "initialize", StringComparison.Ordinal)
            && !initializeReceived)
        {
            await WriteErrorAsync(id, -32600, "Not initialized", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!string.Equals(method, "initialize", StringComparison.Ordinal)
            && initializeReceived
            && !experimentalApiEnabled
            && TryGetExperimentalCapabilityReason(method, @params, out var experimentalReason))
        {
            await WriteErrorAsync(
                    id,
                    -32600,
                    $"{experimentalReason} requires experimentalApi capability",
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        switch (method)
        {
            case "initialize":
                if (initializeReceived)
                {
                    await WriteErrorAsync(id, -32600, "Already initialized", cancellationToken).ConfigureAwait(false);
                    return;
                }

                var clientName = Normalize(ReadString(@params, "clientInfo", "name"));
                if (!string.IsNullOrWhiteSpace(clientName)
                    && !IsValidHttpHeaderValue(clientName))
                {
                    await WriteErrorAsync(
                            id,
                            -32600,
                            $"Invalid clientInfo.name: '{clientName}'. Must be a valid HTTP header value.",
                            cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }

                UpdateClientCapabilities(@params);
                initializeReceived = true;
                await WriteResultAsync(id, new
                {
                    userAgent = BuildInitializeUserAgent(clientName),
                    tianShuHome = Path.GetFullPath(ResolveTianShuHomePath()),
                    platformFamily = KernelFileSystemUtilities.ResolveInitializePlatformFamily(),
                    platformOs = KernelFileSystemUtilities.ResolveInitializePlatformOs(),
                }, cancellationToken).ConfigureAwait(false);
                await EmitExperimentalInstructionsDeprecationNoticeIfNeededAsync(
                        Environment.CurrentDirectory,
                        cancellationToken)
                    .ConfigureAwait(false);
                globalNotificationRegistration?.MarkInitialized();
                return;

            case "thread/start":
                await HandleThreadStartAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "thread/list":
                await HandleThreadListAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "thread/resume":
                await HandleThreadResumeAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "thread/read":
                await HandleThreadReadAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "tianshu/userShell/run":
                await userShellAppHostRuntime.HandleUserShellRunAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "thread/fork":
                await HandleThreadForkAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "thread/archive":
                await threadLifecycleAppHostRuntime.HandleThreadArchiveAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "thread/increment_elicitation":
                await HandleThreadIncrementElicitationAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "thread/decrement_elicitation":
                await HandleThreadDecrementElicitationAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "thread/delete":
                await threadLifecycleAppHostRuntime.HandleThreadDeleteAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "thread/clear":
                await threadLifecycleAppHostRuntime.HandleThreadClearAsync(id, cancellationToken).ConfigureAwait(false);
                return;

            case "thread/unsubscribe":
                await threadLifecycleAppHostRuntime.HandleThreadUnsubscribeAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "thread/unarchive":
                await threadLifecycleAppHostRuntime.HandleThreadUnarchiveAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "thread/name/set":
                await HandleThreadSetNameAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "agent/thread/register":
                await HandleAgentThreadRegisterAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "agent/job/create":
                await agentJobsAppHostRuntime.HandleAgentJobCreateAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "agent/job/dispatch":
                await agentJobsAppHostRuntime.HandleAgentJobDispatchAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "agent/job/item/report":
                await agentJobsAppHostRuntime.HandleAgentJobItemReportAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "agent/job/read":
                await agentJobsAppHostRuntime.HandleAgentJobReadAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "agent/jobs/list":
                await agentJobsAppHostRuntime.HandleAgentJobsListAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "thread/metadata/update":
                await HandleThreadMetadataUpdateAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "tianshu/thread/pending_input/update":
                await HandleThreadPendingInputStateUpdateAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "thread/rollback":
                await threadLifecycleAppHostRuntime.HandleThreadRollbackAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "thread/compact/start":
                await threadLifecycleAppHostRuntime.HandleThreadCompactStartAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "thread/backgroundTerminals/clean":
                await threadLifecycleAppHostRuntime.HandleThreadBackgroundTerminalsCleanAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "thread/loaded/list":
                await threadLifecycleAppHostRuntime.HandleThreadLoadedListAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "fs/readFile":
                await fileSystemAppHostRuntime.HandleFsReadFileAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "fs/writeFile":
                await fileSystemAppHostRuntime.HandleFsWriteFileAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "fs/createDirectory":
                await fileSystemAppHostRuntime.HandleFsCreateDirectoryAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "fs/getMetadata":
                await fileSystemAppHostRuntime.HandleFsGetMetadataAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "fs/readDirectory":
                await fileSystemAppHostRuntime.HandleFsReadDirectoryAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "fs/remove":
                await fileSystemAppHostRuntime.HandleFsRemoveAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "fs/copy":
                await fileSystemAppHostRuntime.HandleFsCopyAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "fs/watch":
                await fileSystemAppHostRuntime.HandleFsWatchAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "fs/unwatch":
                await fileSystemAppHostRuntime.HandleFsUnwatchAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "turn/start":
                await HandleTurnStartAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "mcpServer/tools/list":
                await mcpServerSurfaceAppHostRuntime.HandleMcpServerToolsListAsync(id, cancellationToken).ConfigureAwait(false);
                return;

            case "mcpServer/tools/call":
                await mcpServerSurfaceAppHostRuntime.HandleMcpServerToolsCallAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "turn/steer":
                await turnReviewSurfaceAppHostRuntime.HandleTurnSteerAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "turn/interrupt":
                await HandleTurnInterruptAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "skills/list":
                await pluginsAppHostRuntime.HandleSkillsListAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "skills/remote/list":
                await pluginsAppHostRuntime.HandleSkillsRemoteListAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "skills/remote/export":
                await pluginsAppHostRuntime.HandleSkillsRemoteExportAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "skills/config/write":
                await pluginsAppHostRuntime.HandleSkillsConfigWriteAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "plugin/list":
                await pluginsAppHostRuntime.HandlePluginListAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "plugin/read":
                await pluginsAppHostRuntime.HandlePluginReadAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "plugin/install":
                await pluginsAppHostRuntime.HandlePluginInstallAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "plugin/uninstall":
                await pluginsAppHostRuntime.HandlePluginUninstallAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "app/list":
                await pluginsAppHostRuntime.HandleAppListAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "review/start":
                await turnReviewSurfaceAppHostRuntime.HandleReviewStartAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "model/list":
                await catalogSurfaceAppHostRuntime.HandleModelListAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "tools/catalog/read":
                await catalogSurfaceAppHostRuntime.HandleToolsCatalogReadAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "experimentalfeature/list":
                await catalogSurfaceAppHostRuntime.HandleExperimentalFeatureListAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "collaborationmode/list":
                await catalogSurfaceAppHostRuntime.HandleCollaborationModeListAsync(id, cancellationToken).ConfigureAwait(false);
                return;

            case "mcpServer/oauth/login":
                await mcpServerSurfaceAppHostRuntime.HandleMcpServerOauthLoginAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "config/mcpserver/reload":
                await mcpServerSurfaceAppHostRuntime.HandleConfigMcpServerReloadAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "config/provider/reload":
                await HandleConfigProviderReloadAsync(id, cancellationToken).ConfigureAwait(false);
                return;

            case "mcpserverstatus/list":
                await mcpServerSurfaceAppHostRuntime.HandleMcpServerStatusListAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;
            case "command/exec":
                await commandExecSurfaceAppHostRuntime.HandleCommandExecAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "exec":
                await codeModeProtocolAppHostRuntime.HandleCodeModeExecAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "exec_wait":
                await codeModeProtocolAppHostRuntime.HandleCodeModeWaitAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "command/exec/write":
                await commandExecAppHostRuntime.HandleCommandExecWriteAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "command/exec/terminate":
                await commandExecAppHostRuntime.HandleCommandExecTerminateAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "command/exec/resize":
                await commandExecAppHostRuntime.HandleCommandExecResizeAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "config/read":
                await configSurfaceAppHostRuntime.HandleConfigReadAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "config/value/write":
                await configSurfaceAppHostRuntime.HandleConfigValueWriteAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "config/batchWrite":
                await configSurfaceAppHostRuntime.HandleConfigBatchWriteAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "configRequirements/read":
                await configSurfaceAppHostRuntime.HandleConfigRequirementsReadAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "fuzzyFileSearch":
                await fileSystemAppHostRuntime.HandleFuzzyFileSearchLegacyAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "fuzzyFileSearch/sessionStart":
                await fileSystemAppHostRuntime.HandleFuzzyFileSearchSessionStartAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "fuzzyFileSearch/sessionUpdate":
                await fileSystemAppHostRuntime.HandleFuzzyFileSearchSessionUpdateAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "fuzzyFileSearch/sessionStop":
                await fileSystemAppHostRuntime.HandleFuzzyFileSearchSessionStopAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "thread/realtime/start":
                await realtimeAppHostRuntime.HandleThreadRealtimeStartAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "thread/realtime/appendAudio":
                await realtimeAppHostRuntime.HandleThreadRealtimeAppendAudioAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "thread/realtime/appendText":
                await realtimeAppHostRuntime.HandleThreadRealtimeAppendTextAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "thread/realtime/handoffOutput":
                await realtimeAppHostRuntime.HandleThreadRealtimeHandoffOutputAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "thread/realtime/stop":
                await realtimeAppHostRuntime.HandleThreadRealtimeStopAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "windowsSandbox/setupStart":
                await windowsSandboxSurfaceAppHostRuntime.HandleWindowsSandboxSetupStartAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "feedback/upload":
                await feedbackAppHostRuntime.HandleFeedbackUploadAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "diagnostics/trace/read":
                await HandleDiagnosticsTraceReadAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "diagnostics/attempts/list":
                await HandleDiagnosticsAttemptListAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "governance/approvalQueue/read":
                await HandleGovernanceApprovalQueueReadAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "governance/userInputs/list":
                await HandleGovernanceUserInputsListAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "identity/accountProfile/read":
                await HandleIdentityMemorySurfaceAsync<GetAccountProfile, Account?>(
                    id,
                    @params,
                    method,
                    (request, context, token) => identityMemoryPlane.GetAccountProfileAsync(request, context, token),
                    cancellationToken).ConfigureAwait(false);
                return;

            case "identity/devices/list":
                await HandleIdentityMemorySurfaceAsync<ListBoundDevices, IReadOnlyList<DeviceBinding>>(
                    id,
                    @params,
                    method,
                    (request, context, token) => identityMemoryPlane.ListBoundDevicesAsync(request, context, token),
                    cancellationToken).ConfigureAwait(false);
                return;

            case "memory/providers/list":
                await HandleIdentityMemorySurfaceAsync<ListMemoryProviders, IReadOnlyList<MemoryProviderDescriptor>>(
                    id,
                    @params,
                    method,
                    (request, context, token) => identityMemoryPlane.ListMemoryProvidersAsync(request, context, token),
                    cancellationToken).ConfigureAwait(false);
                return;

            case "memory/spaces/list":
                await HandleIdentityMemorySurfaceAsync<ListMemorySpaces, IReadOnlyList<MemorySpace>>(
                    id,
                    @params,
                    method,
                    (request, context, token) => identityMemoryPlane.ListMemorySpacesAsync(request, context, token),
                    cancellationToken).ConfigureAwait(false);
                return;

            case "memory/overlay/read":
                await HandleIdentityMemorySurfaceAsync<ResolveMemoryOverlay, MemoryOverlay>(
                    id,
                    @params,
                    method,
                    (request, context, token) => identityMemoryPlane.ResolveMemoryOverlayAsync(request, context, token),
                    cancellationToken).ConfigureAwait(false);
                return;

            case "memory/filter":
                await HandleIdentityMemorySurfaceAsync<FilterMemory, MemoryQueryResult>(
                    id,
                    @params,
                    method,
                    (request, context, token) => identityMemoryPlane.FilterMemoryAsync(request, context, token),
                    cancellationToken).ConfigureAwait(false);
                return;

            case "memory/review/list":
                await HandleIdentityMemorySurfaceAsync<ListMemoryReviews, MemoryReviewQueryResult>(
                    id,
                    @params,
                    method,
                    (request, context, token) => identityMemoryPlane.ListMemoryReviewsAsync(request, context, token),
                    cancellationToken).ConfigureAwait(false);
                return;

            case "memory/add":
                await HandleIdentityMemorySurfaceAsync<AddMemory, MemoryMutationResult>(
                    id,
                    @params,
                    method,
                    (request, context, token) => identityMemoryPlane.AddMemoryAsync(request, context, token),
                    cancellationToken).ConfigureAwait(false);
                return;

            case "memory/extract":
                await HandleIdentityMemorySurfaceAsync<ExtractMemory, IReadOnlyList<MemoryCandidate>>(
                    id,
                    @params,
                    method,
                    (request, context, token) => identityMemoryPlane.ExtractMemoryAsync(request, context, token),
                    cancellationToken).ConfigureAwait(false);
                return;

            case "memory/import":
                await HandleIdentityMemorySurfaceAsync<ImportMemory, MemoryMutationResult>(
                    id,
                    @params,
                    method,
                    (request, context, token) => identityMemoryPlane.ImportMemoryAsync(request, context, token),
                    cancellationToken).ConfigureAwait(false);
                return;

            case "memory/export":
                await HandleIdentityMemorySurfaceAsync<ExportMemory, MemoryQueryResult>(
                    id,
                    @params,
                    method,
                    (request, context, token) => identityMemoryPlane.ExportMemoryAsync(request, context, token),
                    cancellationToken).ConfigureAwait(false);
                return;

            case "memory/provider/bind":
                await HandleIdentityMemorySurfaceAsync<BindMemoryProvider, MemoryMutationResult>(
                    id,
                    @params,
                    method,
                    (request, context, token) => identityMemoryPlane.BindMemoryProviderAsync(request, context, token),
                    cancellationToken).ConfigureAwait(false);
                return;

            case "memory/consolidation/run":
                await HandleIdentityMemorySurfaceAsync<RunMemoryConsolidation, MemoryConsolidationRunResult>(
                    id,
                    @params,
                    method,
                    (request, context, token) => identityMemoryPlane.RunMemoryConsolidationAsync(request, context, token),
                    cancellationToken).ConfigureAwait(false);
                return;

            case "memory/forget":
                await HandleIdentityMemorySurfaceAsync<ForgetMemory, MemoryMutationResult>(
                    id,
                    @params,
                    method,
                    (request, context, token) => identityMemoryPlane.ForgetMemoryAsync(request, context, token),
                    cancellationToken).ConfigureAwait(false);
                return;

            case "memory/delete":
                await HandleIdentityMemorySurfaceAsync<DeleteMemory, MemoryMutationResult>(
                    id,
                    @params,
                    method,
                    (request, context, token) => identityMemoryPlane.DeleteMemoryAsync(request, context, token),
                    cancellationToken).ConfigureAwait(false);
                return;

            case "memory/supersede":
                await HandleIdentityMemorySurfaceAsync<SupersedeMemory, MemoryMutationResult>(
                    id,
                    @params,
                    method,
                    (request, context, token) => identityMemoryPlane.SupersedeMemoryAsync(request, context, token),
                    cancellationToken).ConfigureAwait(false);
                return;

            case "memory/review/approve":
                await HandleIdentityMemorySurfaceAsync<ApproveMemoryReview, MemoryMutationResult>(
                    id,
                    @params,
                    method,
                    (request, context, token) => identityMemoryPlane.ApproveMemoryReviewAsync(request, context, token),
                    cancellationToken).ConfigureAwait(false);
                return;

            case "memory/review/demote":
                await HandleIdentityMemorySurfaceAsync<DemoteMemoryReview, MemoryMutationResult>(
                    id,
                    @params,
                    method,
                    (request, context, token) => identityMemoryPlane.DemoteMemoryReviewAsync(request, context, token),
                    cancellationToken).ConfigureAwait(false);
                return;

            case "memory/review/merge":
                await HandleIdentityMemorySurfaceAsync<MergeMemoryReview, MemoryMutationResult>(
                    id,
                    @params,
                    method,
                    (request, context, token) => identityMemoryPlane.MergeMemoryReviewAsync(request, context, token),
                    cancellationToken).ConfigureAwait(false);
                return;

            case "memory/review/restore":
                await HandleIdentityMemorySurfaceAsync<RestoreMemoryReview, MemoryMutationResult>(
                    id,
                    @params,
                    method,
                    (request, context, token) => identityMemoryPlane.RestoreMemoryReviewAsync(request, context, token),
                    cancellationToken).ConfigureAwait(false);
                return;

            case "memory/feedback/record":
                await HandleIdentityMemorySurfaceAsync<RecordMemoryFeedback, MemoryMutationResult>(
                    id,
                    @params,
                    method,
                    (request, context, token) => identityMemoryPlane.RecordMemoryFeedbackAsync(request, context, token),
                    cancellationToken).ConfigureAwait(false);
                return;

            case "memory/citation/record":
                await HandleIdentityMemorySurfaceAsync<RecordMemoryCitation, MemoryMutationResult>(
                    id,
                    @params,
                    method,
                    (request, context, token) => identityMemoryPlane.RecordMemoryCitationAsync(request, context, token),
                    cancellationToken).ConfigureAwait(false);
                return;

            case "tianshu/debug/clear-memories":
                await HandleDebugClearMemoriesAsync(id, cancellationToken).ConfigureAwait(false);
                return;

            case "artifact/conversationsummary/read":
                await artifactSurfaceAppHostRuntime.HandleConversationSummaryReadAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "artifact/gitdifftoremote/read":
                await artifactSurfaceAppHostRuntime.HandleGitDiffToRemoteReadAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "turn/approval/respond":
            case "turn/approveToolCall":
            case "turn/approve":
                await HandleTurnApprovalRespondAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            case "serverRequest/respond":
                await pendingInteractiveReplayAppHostRuntime.HandleServerRequestRespondAsync(id, @params, cancellationToken).ConfigureAwait(false);
                return;

            default:
                await WriteErrorAsync(id, -32601, $"未知方法：{method}", cancellationToken).ConfigureAwait(false);
                return;
        }
    }

    private async Task HandleNotificationAsync(string method, JsonElement @params, CancellationToken cancellationToken)
    {
        _ = method;
        _ = @params;
        _ = cancellationToken;
        await Task.CompletedTask;
    }

    private async Task HandleDiagnosticsTraceReadAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var traceId = Normalize(ReadString(@params, "traceId"));
        if (string.IsNullOrWhiteSpace(traceId))
        {
            await WriteErrorAsync(id, -32602, "缺少必填参数：traceId。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var trace = await diagnosticsTraceQueryService.GetTraceAsync(
                traceId!,
                ReadString(@params, "threadId"),
                ReadString(@params, "turnId"),
                ReadString(@params, "operationId"),
                cancellationToken)
            .ConfigureAwait(false);
        await WriteResultAsync(id, trace, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleDiagnosticsAttemptListAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var executionId = Normalize(ReadString(@params, "executionId"));
        if (string.IsNullOrWhiteSpace(executionId))
        {
            await WriteErrorAsync(id, -32602, "缺少必填参数：executionId。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var attempts = await diagnosticsTraceQueryService.ListAttemptsAsync(executionId!, cancellationToken).ConfigureAwait(false);
        await WriteResultAsync(id, attempts, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleGovernanceApprovalQueueReadAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var requestedFromParticipantId = Normalize(ReadString(@params, "requestedFromParticipantId"));
        var requestedFrom = BuildPendingGovernanceRequestedFrom();
        var payloads = pendingInteractiveReplayAppHostRuntime.BuildAllPendingInteractiveRequestPayloads();
        var items = payloads
            .Select(payload => JsonSerializer.SerializeToElement(payload, jsonOptions))
            .Where(static payload => IsApprovalLikePendingRequest(payload))
            .Where(payload => string.IsNullOrWhiteSpace(requestedFromParticipantId)
                              || string.Equals(requestedFrom.Id.Value, requestedFromParticipantId, StringComparison.Ordinal))
            .Select(payload => ToApprovalQueueItem(payload, requestedFrom))
            .Where(static item => item is not null)
            .Cast<ApprovalQueueItem>()
            .OrderBy(static item => item.RequestedAt)
            .ToArray();

        await WriteResultAsync(id, new ApprovalQueueProjection(items), cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleGovernanceUserInputsListAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var requestedFromParticipantId = Normalize(ReadString(@params, "requestedFromParticipantId"));
        var requestedFrom = BuildPendingGovernanceRequestedFrom();
        var requests = pendingInteractiveReplayAppHostRuntime.BuildAllPendingInteractiveRequestPayloads()
            .Select(payload => JsonSerializer.SerializeToElement(payload, jsonOptions))
            .Where(static payload => string.Equals(ReadString(payload, "requestKind"), "request_user_input", StringComparison.Ordinal))
            .Where(payload => string.IsNullOrWhiteSpace(requestedFromParticipantId)
                              || string.Equals(requestedFrom.Id.Value, requestedFromParticipantId, StringComparison.Ordinal))
            .Select(payload => ToUserInputRequest(payload, requestedFrom))
            .Where(static item => item is not null)
            .Cast<UserInputRequest>()
            .OrderBy(static item => item.RequestedAt)
            .ToArray();

        await WriteResultAsync(id, requests, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsApprovalLikePendingRequest(JsonElement payload)
    {
        var requestKind = ReadString(payload, "requestKind");
        return string.Equals(requestKind, "approval_requested", StringComparison.Ordinal)
               || string.Equals(requestKind, "permission_requested", StringComparison.Ordinal);
    }

    private static ApprovalQueueItem? ToApprovalQueueItem(JsonElement payload, ParticipantRef requestedFrom)
    {
        var callId = Normalize(ReadString(payload, "callId"))
                     ?? Normalize(ReadString(payload, "requestId"));
        if (string.IsNullOrWhiteSpace(callId))
        {
            return null;
        }

        var toolName = Normalize(ReadString(payload, "toolName"));
        var requestKind = Normalize(ReadString(payload, "requestKind"));
        var title = requestKind switch
        {
            "permission_requested" => "权限申请",
            _ when !string.IsNullOrWhiteSpace(toolName) => $"{toolName} 审批",
            _ => "等待审批",
        };
        var reason = Normalize(ReadString(payload, "text"))
                     ?? Normalize(ReadString(payload, "approvalRequest", "summary"))
                     ?? Normalize(ReadString(payload, "permissionRequest", "summary"))
                     ?? "Provider 请求继续执行。";

        return new ApprovalQueueItem(
            new ApprovalId(callId!),
            title,
            reason,
            requestedFrom,
            ReadDateTimeOffset(payload, "requestedAt") ?? DateTimeOffset.UtcNow);
    }

    private static UserInputRequest? ToUserInputRequest(JsonElement payload, ParticipantRef requestedFrom)
    {
        var callId = Normalize(ReadString(payload, "callId"))
                     ?? Normalize(ReadString(payload, "requestId"));
        if (string.IsNullOrWhiteSpace(callId))
        {
            return null;
        }

        var prompt = Normalize(ReadString(payload, "text"))
                     ?? Normalize(ReadString(payload, "userInputRequest", "summary"))
                     ?? "等待用户补充输入。";
        return new UserInputRequest(
            new UserInputRequestId(callId!),
            prompt,
            requestedFrom,
            requestedAt: ReadDateTimeOffset(payload, "requestedAt") ?? DateTimeOffset.UtcNow);
    }

    private static ParticipantRef BuildPendingGovernanceRequestedFrom()
        => new(new ParticipantId("tianshu-user"), ParticipantKind.Human, "TianShu User");

    private async Task HandleTurnApprovalRespondAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var callId = Normalize(ReadString(@params, "callId"))
            ?? Normalize(ReadString(@params, "approvalId"))
            ?? Normalize(ReadString(@params, "itemId"));
        var explicitRequestId = ReadLong(@params, "requestId");

        long requestId;
        if (explicitRequestId.HasValue)
        {
            requestId = explicitRequestId.Value;
        }
        else if (!string.IsNullOrWhiteSpace(callId)
                 && approvalRequestIdsByCallId.TryGetValue(callId, out var mappedRequestId))
        {
            requestId = mappedRequestId;
        }
        else
        {
            await WriteErrorAsync(id, -32004, "未找到待处理的审批请求。", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!pendingServerResponses.TryRemove(requestId, out var pendingResponse))
        {
            CleanupApprovalRequestMapping(requestId);
            pendingInteractiveReplayAppHostRuntime.CleanupPendingInteractiveRequestMapping(requestId);
            await WriteErrorAsync(id, -32004, "审批请求已失效或不存在。", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!string.IsNullOrWhiteSpace(callId)
            && pendingPermissionRequestsByCallId.TryRemove(callId, out var permissionRequest))
        {
            var scope = KernelPermissionGrantScope.Turn;
            var grantedPermissions = KernelPermissionGrantProfile.Empty;
            if (KernelPermissionGrantProfile.TryParseResponse(@params, permissionRequest.Cwd, out var requestedResponsePermissions, out var parsedScope, out _))
            {
                scope = parsedScope;
                grantedPermissions = KernelPermissionGrantProfile.Intersect(permissionRequest.RequestedPermissions, requestedResponsePermissions);
            }

            if (!grantedPermissions.IsEmpty)
            {
                RecordGrantedPermissions(permissionRequest.ThreadId, permissionRequest.TurnId, grantedPermissions, scope);
            }

            var permissionsResponseElement = JsonSerializer.SerializeToElement(grantedPermissions.BuildResponsePayload(scope), jsonOptions);
            pendingResponse.TrySetResult(permissionsResponseElement);

            CleanupApprovalRequestMapping(requestId);
            pendingInteractiveReplayAppHostRuntime.CleanupPendingInteractiveRequestMapping(requestId);
            approvalRequestIdsByCallId.TryRemove(callId, out _);

            await WriteResultAsync(id, new
            {
                ok = true,
                requestId,
                callId,
                scope = scope == KernelPermissionGrantScope.Session ? "session" : "turn",
                permissions = grantedPermissions.BuildServerPayload(),
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        var approved = ReadBool(@params, "approved");
        var requestedDecision = Normalize(ReadString(@params, "decision"));
        var applyProposedExecPolicyAmendment = ReadBool(@params, "applyProposedExecPolicyAmendment") == true;
        if (string.IsNullOrWhiteSpace(requestedDecision)
            && TryReadObject(@params, "decision", out var decisionObject))
        {
            requestedDecision = TryReadApprovalDecisionObjectType(decisionObject);
            if (string.Equals(requestedDecision, "acceptWithExecpolicyAmendment", StringComparison.OrdinalIgnoreCase))
            {
                applyProposedExecPolicyAmendment = true;
            }
        }

        var decision = NormalizeApprovalDecision(requestedDecision)
            ?? (approved == false ? "decline" : "accept");

        var responsePayload = new Dictionary<string, object?>
        {
            ["decision"] = decision,
        };

        if (KernelManagedNetworkAppHostUtilities.TryReadNetworkPolicyAmendment(@params, out var networkPolicyAmendment) && networkPolicyAmendment is not null)
        {
            responsePayload["networkPolicyAmendment"] = networkPolicyAmendment.ToPayload();
        }

        if (applyProposedExecPolicyAmendment)
        {
            responsePayload["applyProposedExecPolicyAmendment"] = true;
        }

        var reason = Normalize(ReadString(@params, "reason")) ?? Normalize(ReadString(@params, "note"));
        if (!string.IsNullOrWhiteSpace(reason))
        {
            responsePayload["reason"] = reason;
        }

        var responseElement = JsonSerializer.SerializeToElement(responsePayload, jsonOptions);
        pendingResponse.TrySetResult(responseElement);

        CleanupApprovalRequestMapping(requestId);
        pendingInteractiveReplayAppHostRuntime.CleanupPendingInteractiveRequestMapping(requestId);
        if (!string.IsNullOrWhiteSpace(callId))
        {
            approvalRequestIdsByCallId.TryRemove(callId, out _);
        }

        await WriteResultAsync(id, new
        {
            ok = true,
            requestId,
            callId,
            decision,
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T?> TryDeserializeStrictParamsAsync<T>(
        JsonElement id,
        JsonElement @params,
        string methodName,
        CancellationToken cancellationToken)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(@params.GetRawText(), strictInputJsonOptions);
        }
        catch (JsonException ex)
        {
            await WriteErrorAsync(id, -32602, $"{methodName} 参数无效：{ex.Message}", cancellationToken).ConfigureAwait(false);
            return default;
        }
        catch (NotSupportedException ex)
        {
            await WriteErrorAsync(id, -32602, $"{methodName} 参数无效：{ex.Message}", cancellationToken).ConfigureAwait(false);
            return default;
        }
    }

    private async Task HandleIdentityMemorySurfaceAsync<TRequest, TResult>(
        JsonElement id,
        JsonElement @params,
        string methodName,
        Func<TRequest, TianShuIdentityMemoryContext, CancellationToken, Task<TResult>> handler,
        CancellationToken cancellationToken)
    {
        var requestElement = SelectIdentityMemoryRequestElement(@params);
        var request = await TryDeserializeStrictParamsAsync<TRequest>(id, requestElement, methodName, cancellationToken)
            .ConfigureAwait(false);
        if (request is null)
        {
            return;
        }

        var result = await handler(request, BuildIdentityMemoryContextForRuntimeSurface(@params), cancellationToken)
            .ConfigureAwait(false);
        await WriteResultAsync(id, result, cancellationToken).ConfigureAwait(false);
    }

    private static JsonElement SelectIdentityMemoryRequestElement(JsonElement @params)
    {
        if (@params.ValueKind != JsonValueKind.Object)
        {
            return @params;
        }

        foreach (var propertyName in new[] { "request", "query", "command" })
        {
            if (@params.TryGetProperty(propertyName, out var nested))
            {
                return nested;
            }
        }

        return @params;
    }

    private TianShuIdentityMemoryContext BuildIdentityMemoryContextForRuntimeSurface(JsonElement @params)
    {
        var userName = Normalize(ReadTianShuEnvironment("TIANSHU_IDENTITY_DISPLAY_NAME"))
                       ?? Normalize(Environment.UserName)
                       ?? "local-user";
        return new TianShuIdentityMemoryContext(
            runtimeName: "tianshu-apphost",
            accountId: new AccountId($"local-account:{NormalizeMemorySegment(userName)}"),
            accountDisplayName: userName,
            deviceName: Normalize(ReadTianShuEnvironment("TIANSHU_DEVICE_NAME"))
                        ?? Normalize(Environment.MachineName)
                        ?? "local-device",
            platform: Environment.OSVersion.Platform.ToString(),
            workingDirectory: Normalize(ReadString(@params, "cwd"))
                              ?? Normalize(ReadString(@params, "workingDirectory")),
            activeThreadId: Normalize(ReadString(@params, "threadId")),
            teamKey: Normalize(ReadTianShuEnvironment("TIANSHU_TEAM_KEY")),
            collaborationSpaceId: Normalize(ReadTianShuEnvironment("TIANSHU_COLLABORATION_SPACE_ID")),
            preferredVerbosity: Normalize(ReadString(@params, "preferredVerbosity"))
                                ?? Normalize(ReadTianShuEnvironment("TIANSHU_MEMORY_PREFERRED_VERBOSITY")),
            preferredTools: ResolveIdentityMemoryPreferredTools(),
            snapshotTime: DateTimeOffset.UtcNow);
    }

    private async Task FlushPendingTurnInterruptResponsesAsync(
        string? threadId,
        string? turnId,
        CancellationToken cancellationToken)
    {
        var responseIds = threadHistoryAppHostRuntime.DrainPendingTurnInterruptResponses(threadId, turnId);
        foreach (var responseId in responseIds)
        {
            await WriteResultAsync(responseId, new { }, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PersistTurnSessionBeforeTerminalAsync(
        string threadId,
        string turnId,
        TurnRequestContext turnContext,
        string reviewExitItemId,
        string? reviewOutputText,
        string? reviewFailureMessage,
        string? effectiveUserText,
        string? finalAssistantText,
        string finalTurnStatus,
        KernelTurnErrorRecord? finalTurnError,
        bool persistExtendedHistory)
    {
        if (turnContext.IsReview)
        {
            var reviewText = !string.IsNullOrWhiteSpace(reviewOutputText)
                ? KernelReviewOutputParity.RenderReviewOutputText(reviewOutputText)
                : KernelReviewOutputParity.ReviewFallbackMessage;
            var reviewItem = new
            {
                id = reviewExitItemId,
                type = "exitedReviewMode",
                review = reviewText,
            };

            try
            {
                await WriteNotificationAsync("item/started", new
                {
                    threadId,
                    turnId,
                    item = reviewItem,
                }, CancellationToken.None).ConfigureAwait(false);
                await WriteNotificationAsync("item/completed", new
                {
                    threadId,
                    turnId,
                    item = reviewItem,
                }, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // review 生命周期通知失败不应中断主链路收尾。
            }
        }

        KernelTrackedTurnHistory? trackedTurnHistory = null;
        var rolloutTurnPersisted = false;
        var rolloutPath = threadStore.RolloutRecorder.GetRolloutPath(threadId);
        var isEphemeral = await IsEphemeralThreadAsync(threadId, CancellationToken.None).ConfigureAwait(false);
        try
        {
            trackedTurnHistory = threadHistoryAppHostRuntime.FinalizeTrackedTurnHistory(threadId, turnId);
            if (trackedTurnHistory is not null)
            {
                await threadStore.AppendCompletedTurnAsync(
                    threadId,
                    turnId,
                    effectiveUserText,
                    finalAssistantText,
                    finalTurnStatus,
                    CancellationToken.None,
                    items: trackedTurnHistory.Items,
                    error: finalTurnError,
                    startedAt: trackedTurnHistory.StartedAt,
                    completedAt: trackedTurnHistory.CompletedAt).ConfigureAwait(false);

                if (persistExtendedHistory)
                {
                    await threadStore.RolloutRecorder.AppendTurnResultAsync(
                        threadId,
                        turnId,
                        finalTurnStatus,
                        effectiveUserText,
                        finalAssistantText,
                        CancellationToken.None,
                        items: trackedTurnHistory.Items.Select(KernelRolloutStateMapper.ToRolloutTurnItemRecord).ToArray(),
                        error: KernelRolloutStateMapper.ToRolloutTurnErrorRecord(finalTurnError),
                        startedAt: trackedTurnHistory.StartedAt,
                        completedAt: trackedTurnHistory.CompletedAt).ConfigureAwait(false);
                    rolloutTurnPersisted = true;
                    var rolloutFileInfo = new FileInfo(rolloutPath);
                    await TryPersistTurnLogDiagnosticAsync(
                        threadId,
                        turnId,
                        phase: "turn.rollout.persist",
                        status: "completed",
                        summary: "append_turn_result_with_items",
                        payload: new
                        {
                            threadId,
                            turnId,
                            rolloutPath,
                            withItems = true,
                            trackedItemCount = trackedTurnHistory.Items.Count,
                            fileExists = rolloutFileInfo.Exists,
                            fileLength = rolloutFileInfo.Exists ? rolloutFileInfo.Length : 0L,
                        },
                        CancellationToken.None).ConfigureAwait(false);
                }
            }

        }
        catch (Exception ex)
        {
            // turn items 二次落盘失败不应影响主链路收尾。
            await TryPersistTurnLogDiagnosticAsync(
                threadId,
                turnId,
                phase: "turn.rollout.persist",
                status: "failed",
                summary: Normalize(ex.Message) ?? "append_turn_result_with_items_failed",
                payload: new
                {
                    threadId,
                    turnId,
                    rolloutPath,
                    withItems = true,
                    trackedItemCount = trackedTurnHistory?.Items.Count ?? 0,
                    exceptionType = ex.GetType().FullName,
                    error = ex.Message,
                },
                CancellationToken.None).ConfigureAwait(false);
        }

        await turnExecutionAppHostRuntime.TryCommitTerminalTurnProjectionAsync(
                threadId,
                turnId,
                turnContext,
                reviewOutputText,
                reviewFailureMessage,
                effectiveUserText,
                finalAssistantText,
                finalTurnStatus,
                finalTurnError,
                trackedTurnHistory?.StartedAt,
                trackedTurnHistory?.CompletedAt)
            .ConfigureAwait(false);

        if (isEphemeral)
        {
            return;
        }

        try
        {
            if (!rolloutTurnPersisted)
            {
                await threadStore.RolloutRecorder.AppendTurnResultAsync(
                    threadId,
                    turnId,
                    finalTurnStatus,
                    effectiveUserText,
                    finalAssistantText,
                    CancellationToken.None,
                    items: null,
                    error: KernelRolloutStateMapper.ToRolloutTurnErrorRecord(finalTurnError),
                    startedAt: trackedTurnHistory?.StartedAt,
                    completedAt: trackedTurnHistory?.CompletedAt).ConfigureAwait(false);
                var rolloutFileInfo = new FileInfo(rolloutPath);
                await TryPersistTurnLogDiagnosticAsync(
                    threadId,
                    turnId,
                    phase: "turn.rollout.persist",
                    status: "completed",
                    summary: "append_turn_result_snapshot",
                    payload: new
                    {
                        threadId,
                        turnId,
                        rolloutPath,
                        withItems = false,
                        fileExists = rolloutFileInfo.Exists,
                        fileLength = rolloutFileInfo.Exists ? rolloutFileInfo.Length : 0L,
                    },
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // turn 主快照落盘失败不应影响主链路收尾。
            await TryPersistTurnLogDiagnosticAsync(
                threadId,
                turnId,
                phase: "turn.rollout.persist",
                status: "failed",
                summary: Normalize(ex.Message) ?? "append_turn_result_snapshot_failed",
                payload: new
                {
                    threadId,
                    turnId,
                    rolloutPath,
                    withItems = false,
                    exceptionType = ex.GetType().FullName,
                    error = ex.Message,
                },
                CancellationToken.None).ConfigureAwait(false);
        }

        try
        {
            await threadStore.RolloutRecorder.CloseThreadWriterAsync(threadId).ConfigureAwait(false);
            await TryPersistTurnLogDiagnosticAsync(
                threadId,
                turnId,
                phase: "turn.rollout.close",
                status: "completed",
                summary: "close_thread_writer",
                payload: new
                {
                    threadId,
                    turnId,
                    rolloutPath,
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 关闭 rollout writer 失败不应影响主链路收尾。
            await TryPersistTurnLogDiagnosticAsync(
                threadId,
                turnId,
                phase: "turn.rollout.close",
                status: "failed",
                summary: Normalize(ex.Message) ?? "close_thread_writer_failed",
                payload: new
                {
                    threadId,
                    turnId,
                    rolloutPath,
                    exceptionType = ex.GetType().FullName,
                    error = ex.Message,
                },
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<JsonElement> SendServerRequestAsync(
        string method,
        object @params,
        string threadId,
        CancellationToken cancellationToken,
        TimeSpan? timeoutOverride = null)
    {
        var requestId = Interlocked.Increment(ref serverRequestSequence);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!pendingServerResponses.TryAdd(requestId, tcs))
        {
            throw new InvalidOperationException($"无法登记 server request：id={requestId}");
        }

        TryTrackApprovalRequest(method, @params, requestId);
        TryTrackPendingUserInputRequest(method, @params, threadId, requestId);
        pendingInteractiveReplayAppHostRuntime.TryTrackPendingInteractiveRequest(method, @params, threadId, requestId);

        try
        {
            await WriteMessageAsync(new Dictionary<string, object?>
            {
                ["id"] = requestId,
                ["method"] = method,
                ["params"] = @params,
            }, CancellationToken.None).ConfigureAwait(false);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeoutOverride ?? TimeSpan.FromSeconds(45));
            var result = await tcs.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);

            await WriteNotificationAsync("serverRequest/resolved", new
            {
                threadId,
                requestId,
            }, CancellationToken.None).ConfigureAwait(false);

            CleanupApprovalRequestMapping(requestId);
            CleanupPendingUserInputRequestMapping(requestId);
            pendingInteractiveReplayAppHostRuntime.CleanupPendingInteractiveRequestMapping(requestId);
            return result;
        }
        catch (KernelPendingServerRequestResolvedException ex)
        {
            pendingServerResponses.TryRemove(requestId, out _);
            CleanupApprovalRequestMapping(requestId);
            CleanupPendingUserInputRequestMapping(requestId);
            pendingInteractiveReplayAppHostRuntime.CleanupPendingInteractiveRequestMapping(requestId);
            throw new OperationCanceledException(ex.Message, ex, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var resolvedOnInterrupt = await pendingInteractiveReplayAppHostRuntime.TryResolvePendingInteractiveRequestOnInterruptAsync(requestId, CancellationToken.None).ConfigureAwait(false);
            if (!resolvedOnInterrupt)
            {
                pendingServerResponses.TryRemove(requestId, out _);
                CleanupApprovalRequestMapping(requestId);
                CleanupPendingUserInputRequestMapping(requestId);
                pendingInteractiveReplayAppHostRuntime.CleanupPendingInteractiveRequestMapping(requestId);
            }

            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            pendingServerResponses.TryRemove(requestId, out _);
            CleanupApprovalRequestMapping(requestId);
            CleanupPendingUserInputRequestMapping(requestId);
            pendingInteractiveReplayAppHostRuntime.CleanupPendingInteractiveRequestMapping(requestId);
            throw new TimeoutException($"server request 超时：method={method}, id={requestId}");
        }
        catch
        {
            pendingServerResponses.TryRemove(requestId, out _);
            CleanupApprovalRequestMapping(requestId);
            CleanupPendingUserInputRequestMapping(requestId);
            pendingInteractiveReplayAppHostRuntime.CleanupPendingInteractiveRequestMapping(requestId);
            throw;
        }
    }

    private bool TryResolveServerResponse(JsonElement root)
    {
        if (!TryReadServerResponseId(root, out var responseId))
        {
            return false;
        }

        if (!pendingServerResponses.TryRemove(responseId, out var pending))
        {
            return false;
        }

        CleanupApprovalRequestMapping(responseId);
        CleanupPendingUserInputRequestMapping(responseId);
        pendingInteractiveReplayAppHostRuntime.CleanupPendingInteractiveRequestMapping(responseId);

        if (root.TryGetProperty("error", out var errorElement))
        {
            var message = ReadString(errorElement, "message") ?? errorElement.GetRawText();
            pending.TrySetException(new InvalidOperationException($"server request 返回错误：{message}"));
            return true;
        }

        pending.TrySetResult(root.TryGetProperty("result", out var result) ? result.Clone() : root.Clone());
        return true;
    }

    private static bool TryReadServerResponseId(JsonElement json, out long id)
    {
        id = 0;
        if (!json.TryGetProperty("id", out var idElement))
        {
            return false;
        }

        if (idElement.ValueKind == JsonValueKind.Number && idElement.TryGetInt64(out var numeric))
        {
            id = numeric;
            return true;
        }

        if (idElement.ValueKind == JsonValueKind.String
            && long.TryParse(idElement.GetString(), out var parsed))
        {
            id = parsed;
            return true;
        }

        return false;
    }

    private void TryTrackApprovalRequest(string method, object @params, long requestId)
    {
        if (!string.Equals(method, "item/commandExecution/requestApproval", StringComparison.Ordinal)
            && !string.Equals(method, "item/fileChange/requestApproval", StringComparison.Ordinal)
            && !string.Equals(method, "item/tool/requestApproval", StringComparison.Ordinal)
            && !string.Equals(method, "item/permissions/requestApproval", StringComparison.Ordinal))
        {
            return;
        }

        var serializedParams = JsonSerializer.SerializeToElement(@params, jsonOptions);
        var callId = Normalize(ReadString(serializedParams, "approvalId"))
            ?? Normalize(ReadString(serializedParams, "callId"))
            ?? Normalize(ReadString(serializedParams, "itemId"));
        if (string.IsNullOrWhiteSpace(callId))
        {
            return;
        }

        approvalRequestIdsByCallId[callId] = requestId;
        approvalCallIdsByRequestId[requestId] = callId;
    }

    private void CleanupApprovalRequestMapping(long requestId)
    {
        if (!approvalCallIdsByRequestId.TryRemove(requestId, out var mappedCallId)
            || string.IsNullOrWhiteSpace(mappedCallId))
        {
            return;
        }

        approvalRequestIdsByCallId.TryRemove(mappedCallId, out _);
    }

    private void TryTrackPendingUserInputRequest(string method, object @params, string threadId, long requestId)
    {
        if (!string.Equals(method, "item/tool/requestUserInput", StringComparison.Ordinal))
        {
            return;
        }

        var serializedParams = JsonSerializer.SerializeToElement(@params, jsonOptions);
        var callId = Normalize(ReadString(serializedParams, "itemId"));
        if (string.IsNullOrWhiteSpace(callId))
        {
            return;
        }

        pendingUserInputRequestsByRequestId[requestId] = new KernelPendingUserInputServerRequest(
            requestId,
            threadId,
            Normalize(ReadString(serializedParams, "turnId")),
            callId);
    }

    private void CleanupPendingUserInputRequestMapping(long requestId)
    {
        pendingUserInputRequestsByRequestId.TryRemove(requestId, out _);
    }

    private async Task ResolvePendingUserInputRequestsForThreadLifecycleAsync(
        string? threadId,
        string? lifecycleTurnId,
        string lifecyclePhase,
        CancellationToken cancellationToken,
        bool includeLifecycleTurn = true)
    {
        var normalizedThreadId = Normalize(threadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            return;
        }

        var pendingRequests = pendingUserInputRequestsByRequestId.Values
            .Where(static request => !string.IsNullOrWhiteSpace(request.ThreadId))
            .Where(request => string.Equals(request.ThreadId, normalizedThreadId, StringComparison.Ordinal))
            .Where(request => includeLifecycleTurn || !string.Equals(request.TurnId, lifecycleTurnId, StringComparison.Ordinal))
            .OrderBy(request => request.RequestId)
            .ToArray();

        foreach (var pendingRequest in pendingRequests)
        {
            if (!pendingServerResponses.TryRemove(pendingRequest.RequestId, out var pendingResponse))
            {
                CleanupPendingUserInputRequestMapping(pendingRequest.RequestId);
                pendingInteractiveReplayAppHostRuntime.CleanupPendingInteractiveRequestMapping(pendingRequest.RequestId);
                continue;
            }

            CleanupPendingUserInputRequestMapping(pendingRequest.RequestId);
            pendingInteractiveReplayAppHostRuntime.CleanupPendingInteractiveRequestMapping(pendingRequest.RequestId);
            pendingResponse.TrySetException(
                new KernelPendingServerRequestResolvedException(
                    $"pending server request resolved during {lifecyclePhase}: requestId={pendingRequest.RequestId}, threadId={pendingRequest.ThreadId}, turnId={pendingRequest.TurnId ?? string.Empty}"));

            await WriteNotificationAsync("serverRequest/resolved", new
            {
                threadId = pendingRequest.ThreadId,
                requestId = pendingRequest.RequestId,
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ResolvePendingInteractiveRequestsForThreadLifecycleAsync(
        string? threadId,
        string? lifecycleTurnId,
        string lifecyclePhase,
        CancellationToken cancellationToken,
        bool includeLifecycleTurn = true)
    {
        await pendingInteractiveReplayAppHostRuntime.ResolvePendingInteractiveRequestsForThreadLifecycleAsync(
            threadId,
            lifecycleTurnId,
            lifecyclePhase,
            cancellationToken,
            includeLifecycleTurn).ConfigureAwait(false);
        await ResolvePendingUserInputRequestsForThreadLifecycleAsync(
            threadId,
            lifecycleTurnId,
            lifecyclePhase,
            cancellationToken,
            includeLifecycleTurn).ConfigureAwait(false);
    }

    private void RecordGrantedPermissions(
        string threadId,
        string turnId,
        KernelPermissionGrantProfile permissions,
        KernelPermissionGrantScope scope)
    {
        if (permissions.IsEmpty)
        {
            return;
        }

        if (scope == KernelPermissionGrantScope.Session)
        {
            grantedPermissionSessionByThread.AddOrUpdate(
                threadId,
                _ => KernelPermissionGrantProfile.Clone(permissions),
                (_, existing) => KernelPermissionGrantProfile.Merge(existing, permissions));
            return;
        }

        grantedPermissionTurnByTurn.AddOrUpdate(
            turnId,
            _ => KernelPermissionGrantProfile.Clone(permissions),
            (_, existing) => KernelPermissionGrantProfile.Merge(existing, permissions));
    }

    private KernelThreadSessionResponsePayload BuildThreadSessionResponse(
        KernelThreadRecord record,
        bool includeTurns,
        KernelThreadSessionState? session = null,
        KernelTurnRecord? activeTurn = null)
    {
        var effectiveSession = session ?? GetOrCreateThreadSession(record);
        var effectiveActiveTurn = activeTurn
                                  ?? TryBuildLiveActiveTurnSnapshot(record.Id)
                                  ?? TryBuildPersistedActiveTurnSnapshot(record);
        var normalizedRecord = NormalizeThreadRecordForSessionResponse(record, effectiveActiveTurn);
        var snapshot = normalizedRecord.ConfigSnapshot?.DeepClone() ?? KernelThreadConfigSnapshotFactory.FromSession(effectiveSession);
        var sessionConfiguration = BuildThreadSessionConfigurationPayload(normalizedRecord, snapshot);
        var messages = BuildThreadReplayMessagesPayload(normalizedRecord, effectiveActiveTurn);
        var pendingInputState = normalizedRecord.PendingInputState;
        var pendingInteractiveRequests = pendingInteractiveReplayAppHostRuntime.BuildPendingInteractiveRequestPayloads(normalizedRecord.Id);
        return new KernelThreadSessionResponsePayload(
            Thread: ToThreadPayload(
                normalizedRecord,
                includeTurns,
                effectiveSession,
                effectiveActiveTurn,
                sessionConfiguration,
                messages,
                pendingInputState: null,
                pendingInteractiveRequests),
            Model: effectiveSession.Model,
            ModelProvider: effectiveSession.ModelProvider,
            ServiceTier: effectiveSession.ServiceTier,
            Cwd: effectiveSession.Cwd,
            ApprovalPolicy: effectiveSession.ApprovalPolicy,
            Sandbox: KernelSandboxPolicyOverride.FromElement(effectiveSession.SandboxPolicy),
            ReasoningEffort: effectiveSession.CollaborationMode?.Settings.ReasoningEffort,
            SessionConfiguration: sessionConfiguration,
            Messages: messages,
            PendingInputState: pendingInputState,
            PendingInteractiveRequests: pendingInteractiveRequests);
    }

    private KernelTurnRecord? TryBuildLiveActiveTurnSnapshot(string threadId)
    {
        if (!threadLifecycleAppHostRuntime.TryGetRunningThread(threadId, out var runtimeThread)
            || runtimeThread?.ActiveTurnId is not { Length: > 0 } activeTurnId)
        {
            return null;
        }

        return threadHistoryAppHostRuntime.BuildTrackedActiveTurnSnapshot(threadId, activeTurnId);
    }

    private static KernelTurnRecord? TryBuildPersistedActiveTurnSnapshot(KernelThreadRecord record)
    {
        for (var index = record.Turns.Count - 1; index >= 0; index--)
        {
            var turn = record.Turns[index];
            if (!ShouldReplayPersistedActiveTurn(turn))
            {
                continue;
            }

            return KernelToolRuntimeInteractionHelpers.CloneTurnRecordForResponse(turn);
        }

        return null;
    }

    private static bool ShouldReplayPersistedActiveTurn(KernelTurnRecord turn)
    {
        if (IsTerminalPersistedTurnStatus(turn.Status))
        {
            return false;
        }

        // 只有当前 runtime 自己持久化出来的 active snapshot 才会稳定带上结构化 turn items。
        // 纯文本的非终态 turn 更可能是陈旧残留，应在响应归一化时收口为 interrupted / idle。
        return turn.Items.Count > 0;
    }

    private static bool IsTerminalPersistedTurnStatus(string? status)
        => string.Equals(Normalize(status), "completed", StringComparison.OrdinalIgnoreCase)
           || string.Equals(Normalize(status), "failed", StringComparison.OrdinalIgnoreCase)
           || string.Equals(Normalize(status), "interrupted", StringComparison.OrdinalIgnoreCase);

    private KernelThreadRecord NormalizeThreadRecordForSessionResponse(KernelThreadRecord record, KernelTurnRecord? activeTurn)
    {
        var normalized = CloneThreadRecordForResponse(record);
        var hasLiveInProgressTurn = activeTurn is not null;
        normalized.StatusType = ResolveThreadStatusTypeForSessionResponse(record.Id, normalized.StatusType, hasLiveInProgressTurn);
        if (!string.Equals(normalized.StatusType, "active", StringComparison.OrdinalIgnoreCase))
        {
            normalized.ActiveFlags = [];
            foreach (var turn in normalized.Turns)
            {
                if (string.Equals(turn.Status, "inProgress", StringComparison.OrdinalIgnoreCase))
                {
                    turn.Status = "interrupted";
                }
            }
        }

        return normalized;
    }

    private string ResolveThreadStatusTypeForSessionResponse(string threadId, string? currentStatusType, bool hasLiveInProgressTurn)
    {
        if (!threadManager.IsLoaded(threadId))
        {
            return "notLoaded";
        }

        if (hasLiveInProgressTurn)
        {
            return "active";
        }

        return string.Equals(Normalize(currentStatusType), "systemError", StringComparison.OrdinalIgnoreCase)
            ? "systemError"
            : "idle";
    }

    private static KernelThreadRecord CloneThreadRecordForResponse(KernelThreadRecord source)
        => new()
        {
            Id = source.Id,
            Cwd = source.Cwd,
            ForkedFromThreadId = source.ForkedFromThreadId,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt,
            LastUserMessage = source.LastUserMessage,
            LastAssistantMessage = source.LastAssistantMessage,
            Name = source.Name,
            AgentNickname = source.AgentNickname,
            AgentRole = source.AgentRole,
            IsArchived = source.IsArchived,
            StatusType = source.StatusType,
            ActiveFlags = source.ActiveFlags.ToList(),
            GitInfo = source.GitInfo is null
                ? null
                : new KernelGitInfoRecord
                {
                    Sha = source.GitInfo.Sha,
                    Branch = source.GitInfo.Branch,
                    OriginUrl = source.GitInfo.OriginUrl,
                },
            Turns = source.Turns.Select(KernelToolRuntimeInteractionHelpers.CloneTurnRecordForResponse).ToList(),
            SeedHistory = source.SeedHistory.Select(KernelConversationHistoryUtilities.Clone).ToList(),
            ConfigSnapshot = source.ConfigSnapshot?.DeepClone(),
            PendingInputState = source.PendingInputState?.DeepClone(),
        };

    private KernelThreadPayload ToThreadPayload(
        KernelThreadRecord record,
        bool includeTurns,
        KernelThreadSessionState? session = null,
        KernelTurnRecord? activeTurn = null,
        KernelThreadSessionConfigurationPayload? sessionConfiguration = null,
        object[]? messages = null,
        KernelPendingInputStateRecord? pendingInputState = null,
        object[]? pendingInteractiveRequests = null)
    {
        var effectiveSnapshot = session is null
            ? record.ConfigSnapshot?.DeepClone()
            : KernelThreadConfigSnapshotFactory.FromSession(session);
        var visibleRolloutPath = ResolveVisibleThreadRolloutPath(record, effectiveSnapshot?.Ephemeral ?? false);
        var turns = includeTurns
            ? EnumerateThreadTurns(record, activeTurn)
            : Array.Empty<KernelTurnRecord>();

        return new KernelThreadPayload(
            Id: record.Id,
            Preview: BuildThreadPreview(record),
            Ephemeral: effectiveSnapshot?.Ephemeral ?? false,
            ModelProvider: effectiveSnapshot?.ModelProviderId ?? DefaultModelProvider,
            CreatedAt: record.CreatedAt.ToUnixTimeSeconds(),
            UpdatedAt: record.UpdatedAt.ToUnixTimeSeconds(),
            Status: ToThreadStatusPayload(record),
            Path: visibleRolloutPath,
            Cwd: effectiveSnapshot?.Cwd ?? record.Cwd ?? Environment.CurrentDirectory,
            CliVersion: CliVersion,
            Source: effectiveSnapshot?.SessionSource ?? KernelSessionSource.VsCode,
            AgentNickname: record.AgentNickname,
            AgentRole: record.AgentRole,
            GitInfo: ToGitInfoPayload(record.GitInfo),
            Name: record.Name,
            Turns: includeTurns
                ? BuildThreadTurnsPayload(record, turns)
                : Array.Empty<object>(),
            SessionState: KernelThreadProjectionPayloadFactory.ToSessionProjectionPayload(record.SessionState),
            SessionConfiguration: sessionConfiguration,
            Messages: messages,
            PendingInputState: pendingInputState,
            PendingInteractiveRequests: pendingInteractiveRequests);
    }

    private KernelThreadSessionConfigurationPayload? BuildThreadSessionConfigurationPayload(
        KernelThreadRecord record,
        KernelThreadConfigSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        var rolloutPath = ResolveVisibleThreadRolloutPath(record, snapshot.Ephemeral);

        return new KernelThreadSessionConfigurationPayload(
            Model: snapshot.Model,
            ModelProvider: snapshot.ModelProviderId,
            ModelProviderId: snapshot.ModelProviderId,
            ServiceTier: snapshot.ServiceTier,
            ApprovalPolicy: snapshot.ApprovalPolicy,
            SandboxPolicy: snapshot.SandboxMode,
            SandboxPolicyPayload: KernelSandboxPolicyOverride.FromElement(snapshot.SandboxPolicy),
            ReasoningEffort: snapshot.ReasoningEffort,
            HistoryLogId: ComputeSessionHistoryLogId(rolloutPath),
            HistoryEntryCount: ComputeSessionHistoryEntryCount(record),
            RolloutPath: rolloutPath,
            ForkedFromId: record.ForkedFromThreadId,
            Cwd: snapshot.Cwd,
            Ephemeral: snapshot.Ephemeral,
            AllowLoginShell: snapshot.AllowLoginShell,
            ShellEnvironmentPolicy: snapshot.ShellEnvironmentPolicy,
            ProviderBaseUrl: snapshot.ProviderBaseUrl,
            ProviderApiKeyEnvironmentVariable: snapshot.ProviderApiKeyEnvironmentVariable,
            ProviderWireApi: snapshot.ProviderWireApi,
            ProviderRequestMaxRetries: snapshot.ProviderRequestMaxRetries,
            ProviderStreamMaxRetries: snapshot.ProviderStreamMaxRetries,
            ProviderStreamIdleTimeoutMs: snapshot.ProviderStreamIdleTimeoutMs,
            ProviderWebsocketConnectTimeoutMs: snapshot.ProviderWebsocketConnectTimeoutMs,
            ProviderSupportsWebsockets: snapshot.ProviderSupportsWebsockets,
            WebSearchMode: snapshot.WebSearchMode,
            ServiceName: snapshot.ServiceName,
            BaseInstructions: snapshot.BaseInstructions,
            DeveloperInstructions: snapshot.DeveloperInstructions,
            UserInstructions: snapshot.UserInstructions,
            ReasoningSummary: snapshot.ReasoningSummary,
            Verbosity: snapshot.Verbosity,
            Personality: snapshot.Personality,
            DynamicTools: snapshot.DynamicTools,
            CollaborationMode: snapshot.CollaborationMode,
            PersistExtendedHistory: snapshot.PersistExtendedHistory,
            SessionSource: snapshot.SessionSource,
            WindowsSandboxLevel: snapshot.WindowsSandboxLevel,
            DefaultModeRequestUserInputEnabled: snapshot.DefaultModeRequestUserInputEnabled,
            ModelRouteSetId: snapshot.ModelRouteSetId);
    }

    private string? ComputeSessionHistoryLogId(string? rolloutPath)
    {
        if (string.IsNullOrWhiteSpace(rolloutPath))
        {
            return null;
        }

        if (!OperatingSystem.IsWindows())
        {
            return "0";
        }

        var rolloutFile = new FileInfo(rolloutPath);
        return rolloutFile.Exists
            ? rolloutFile.CreationTimeUtc.ToFileTimeUtc().ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private string ResolveThreadRolloutPath(KernelThreadRecord record)
        => threadStore.RolloutRecorder.ResolveRolloutPath(record.Id, record.IsArchived);

    private string? ResolveVisibleThreadRolloutPath(KernelThreadRecord record, bool isEphemeral)
        => isEphemeral ? null : ResolveThreadRolloutPath(record);

    private int ComputeSessionHistoryEntryCount(KernelThreadRecord record)
    {
        var count = record.SeedHistory.Count + record.Turns.Count;
        if (!threadManager.TryGetThread(record.Id, out var runtimeThread)
            || runtimeThread?.ActiveTurnId is not { Length: > 0 } activeTurnId)
        {
            return count;
        }

        return record.Turns.Any(turn => string.Equals(turn.Id, activeTurnId, StringComparison.Ordinal))
            ? count
            : count + 1;
    }

    private static object ToConversationHistoryPayload(KernelConversationHistoryItem item)
        => KernelConversationHistoryUtilities.SerializeHistoryItem(item);

    private static object[] BuildThreadTurnsPayload(
        KernelThreadRecord record,
        IReadOnlyList<KernelTurnRecord> turns)
    {
        var payloads = new List<object>(record.SeedHistory.Count + turns.Count);
        var (seedHistory, tailContextHistory) = SplitTailContextHistory(record.SeedHistory);
        payloads.AddRange(BuildSeedHistoryTurnPayloads(seedHistory));
        payloads.AddRange(turns.Select(ToTurnPayload));
        payloads.AddRange(BuildSeedHistoryTurnPayloads(tailContextHistory));
        return payloads.ToArray();
    }

    private static IReadOnlyList<object> BuildSeedHistoryTurnPayloads(
        IReadOnlyList<KernelConversationHistoryItem> historyItems)
    {
        if (historyItems.Count == 0)
        {
            return Array.Empty<object>();
        }

        var turns = new List<object>(historyItems.Count);
        for (var index = 0; index < historyItems.Count; index++)
        {
            var historyItem = historyItems[index];
            if (!KernelConversationHistoryUtilities.HasMeaningfulContent(historyItem))
            {
                continue;
            }

            if (!TryBuildSeedHistoryTurnItemPayload(historyItem, index, out var itemPayload))
            {
                continue;
            }

            turns.Add(new
            {
                id = $"seed_history_{index + 1:D6}",
                items = new[] { itemPayload },
                status = "completed",
                error = (object?)null,
            });
        }

        return turns;
    }

    private static bool TryBuildSeedHistoryTurnItemPayload(
        KernelConversationHistoryItem historyItem,
        int index,
        out object payload)
    {
        var normalizedRole = Normalize(historyItem.Role)?.ToLowerInvariant();
        var text = KernelConversationHistoryUtilities.BuildDisplayText(historyItem);
        if (string.Equals(normalizedRole, "assistant", StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                payload = default!;
                return false;
            }

            payload = new
            {
                id = $"seed_history_item_{index + 1:D6}",
                type = "agentMessage",
                text,
                phase = (string?)null,
            };
            return true;
        }

        var content = historyItem.Inputs.Count > 0
            ? historyItem.Inputs.Select(ToThreadUserInputPayload).ToArray()
            : string.IsNullOrWhiteSpace(text)
                ? Array.Empty<object>()
                : new object[]
                {
                    new
                    {
                        type = "text",
                        text,
                        textElements = Array.Empty<object>(),
                    },
                };
        if (content.Length == 0)
        {
            payload = default!;
            return false;
        }

        payload = new
        {
            id = $"seed_history_item_{index + 1:D6}",
            type = "userMessage",
            content,
        };
        return true;
    }

    private static object ToThreadUserInputPayload(KernelConversationInputRecord input)
        => new
        {
            type = input.Type,
            text = input.Text,
            url = input.Url,
            path = input.Path,
            name = input.Name,
            textElements = input.TextElements.Select(
                static element => new
                {
                    byteRange = element.ByteRange is null
                        ? null
                        : new
                        {
                            start = element.ByteRange.Start,
                            end = element.ByteRange.End,
                        },
                    placeholder = element.Placeholder,
                }).ToArray(),
        };

    private static IReadOnlyList<KernelTurnRecord> EnumerateThreadTurns(KernelThreadRecord record, KernelTurnRecord? activeTurn = null)
    {
        if (activeTurn is null)
        {
            return record.Turns;
        }

        var turns = record.Turns.ToList();
        var existingIndex = turns.FindIndex(turn => string.Equals(turn.Id, activeTurn.Id, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            turns[existingIndex] = activeTurn;
        }
        else
        {
            turns.Add(activeTurn);
        }

        return turns;
    }

    private static object[] BuildThreadReplayMessagesPayload(KernelThreadRecord record, KernelTurnRecord? activeTurn = null)
    {
        var messages = new List<object>();
        var turns = EnumerateThreadTurns(record, activeTurn);
        var (seedHistory, tailContextHistory) = SplitTailContextHistory(record.SeedHistory);

        foreach (var historyItem in seedHistory)
        {
            if (!KernelConversationHistoryUtilities.HasReplayablePayload(historyItem))
            {
                continue;
            }

            messages.Add(KernelConversationHistoryUtilities.SerializeHistoryItem(historyItem));
        }

        foreach (var turn in turns)
        {
            foreach (var historyItem in EnumerateTurnConversationHistoryItems(turn))
            {
                if (!KernelConversationHistoryUtilities.HasMeaningfulContent(historyItem))
                {
                    continue;
                }

                messages.Add(KernelConversationHistoryUtilities.SerializeHistoryItem(historyItem));
            }
        }

        foreach (var historyItem in tailContextHistory)
        {
            if (!KernelConversationHistoryUtilities.HasReplayablePayload(historyItem))
            {
                continue;
            }

            messages.Add(KernelConversationHistoryUtilities.SerializeHistoryItem(historyItem));
        }

        return messages.ToArray();
    }

    private static object ToTurnPayload(KernelTurnRecord turn)
    {
        var items = new List<object>();
        var seenTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in turn.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Type))
            {
                continue;
            }

            var payload = KernelToolItemLifecycleHelpers.ConvertJsonElementToObject(item.Payload);
            if (payload is null)
            {
                continue;
            }

            items.Add(payload);
            seenTypes.Add(item.Type);
        }

        if (turn.IsContextCompaction && !seenTypes.Contains("contextCompaction"))
        {
            items.Add(new
            {
                id = $"item_{turn.Id}_context_compaction",
                type = "contextCompaction",
            });
        }

        if (!string.IsNullOrWhiteSpace(turn.UserMessage) && !seenTypes.Contains("userMessage"))
        {
            items.Add(new
            {
                id = turn.Id,
                type = "userMessage",
                content = new object[]
                {
                    new
                    {
                        type = "text",
                        text = turn.UserMessage,
                        textElements = Array.Empty<object>(),
                    },
                },
            });
        }

        if (!string.IsNullOrWhiteSpace(turn.AssistantMessage) && !seenTypes.Contains("agentMessage"))
        {
            items.Add(new
            {
                id = $"item_{turn.Id}_assistant",
                type = "agentMessage",
                text = turn.AssistantMessage,
                phase = (string?)null,
            });
        }

        return new
        {
            id = turn.Id,
            items = items.ToArray(),
            status = turn.Status,
            error = turn.Error is null
                ? null
                : new
                {
                    message = turn.Error.Message,
                    providerErrorInfo = (object?)null,
                    additionalDetails = turn.Error.AdditionalDetails,
                },
        };
    }
    private static KernelThreadGitInfoPayload? ToGitInfoPayload(KernelGitInfoRecord? gitInfo)
    {
        if (gitInfo is null)
        {
            return null;
        }

        return new KernelThreadGitInfoPayload(
            Sha: gitInfo.Sha,
            Branch: gitInfo.Branch,
            OriginUrl: gitInfo.OriginUrl);
    }

    private KernelThreadStatusPayload ToThreadStatusPayload(KernelThreadRecord record)
    {
        if (!threadManager.IsLoaded(record.Id))
        {
            return new KernelThreadStatusPayload(Type: "notLoaded");
        }

        return record.StatusType switch
        {
            "systemError" => new KernelThreadStatusPayload(Type: "systemError"),
            "active" => new KernelThreadStatusPayload(Type: "active", ActiveFlags: record.ActiveFlags),
            _ => new KernelThreadStatusPayload(Type: "idle"),
        };
    }

    private void TrackThreadSubscription(string threadId)
    {
        lock (threadSubscriptionGate)
        {
            subscribedThreadIds.Add(threadId);
        }
    }

    private bool RemoveTrackedThreadSubscription(string threadId)
    {
        lock (threadSubscriptionGate)
        {
            return subscribedThreadIds.Remove(threadId);
        }
    }

    private void ForgetThreadSubscription(string threadId)
    {
        lock (threadSubscriptionGate)
        {
            subscribedThreadIds.Remove(threadId);
        }
    }

    private static object BuildSandboxPolicyPayload()
        => new
        {
            type = "workspaceWrite",
            writableRoots = Array.Empty<string>(),
            readOnlyAccess = new { type = "fullAccess" },
            networkAccess = false,
            excludeTmpdirEnvVar = false,
            excludeSlashTmp = false,
        };

    private async Task WriteThreadStatusChangedAsync(KernelThreadRecord record, CancellationToken cancellationToken)
    {
        await WriteNotificationAsync("thread/status/changed", new
        {
            threadId = record.Id,
            status = ToThreadStatusPayload(record),
        }, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildThreadPreview(KernelThreadRecord record)
    {
        var seedHistoryPreview = record.SeedHistory.Count == 0
            ? null
            : KernelConversationHistoryUtilities.BuildDisplayText(record.SeedHistory[^1]);
        var candidates = new[]
        {
            Normalize(record.Name),
            Normalize(record.LastUserMessage),
            Normalize(record.LastAssistantMessage),
            seedHistoryPreview,
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    private static string ExtractUserText(JsonElement @params)
    {
        if (@params.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        if (@params.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Array)
        {
            var text = KernelTurnExecutionRuntimeHelpers.ExtractInputText(input.EnumerateArray());
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return string.Empty;
    }

    private static string ExtractUserText(KernelTurnStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Input is { Count: > 0 } input)
        {
            var text = KernelTurnExecutionRuntimeHelpers.ExtractInputText(input);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return string.Empty;
    }

    private static IReadOnlyList<KernelConversationHistoryItem> ReadConversationHistoryArray(JsonElement? history)
    {
        if (history is not { ValueKind: JsonValueKind.Array } historyArray)
        {
            return Array.Empty<KernelConversationHistoryItem>();
        }

        var messages = new List<KernelConversationHistoryItem>();
        foreach (var item in historyArray.EnumerateArray())
        {
            var parsed = KernelConversationHistoryUtilities.ParseHistoryItem(item);
            if (parsed is null)
            {
                continue;
            }

            messages.Add(parsed);
        }

        return messages;
    }

    private static bool HasHistoryOverride(KernelThreadResumeRequest request, out IReadOnlyList<KernelConversationHistoryItem> history)
    {
        history = request.History?.Items ?? Array.Empty<KernelConversationHistoryItem>();
        return request.History is not null;
    }

    private static string NormalizeConversationRole(string? role)
        => KernelTurnExecutionRuntimeHelpers.NormalizeConversationRole(role);

    private async Task<string?> BuildExplicitPluginInstructionsAsync(
        IReadOnlyList<KernelTurnInputItem>? inputItems,
        string? userText,
        CancellationToken cancellationToken)
    {
        var mentionedConfigNames = CollectExplicitPluginConfigNames(inputItems, userText);
        if (mentionedConfigNames.Count == 0)
        {
            return null;
        }

        var summaries = await pluginsManager.GetEffectiveCapabilitySummariesAsync(cancellationToken).ConfigureAwait(false);
        var promptConfiguration = TianShuPromptConfigUtilities.FromConfig(BuildConfigReadSnapshotForRuntime(null).Config);
        var instructions = summaries
            .Where(summary => mentionedConfigNames.Contains(summary.ConfigName))
            .Select(summary => RenderExplicitPluginInstructions(summary, promptConfiguration))
            .Where(static text => !string.IsNullOrWhiteSpace(text))
            .ToArray();
        return instructions.Length == 0
            ? null
            : string.Join(Environment.NewLine + Environment.NewLine, instructions);
    }

    private static HashSet<string> CollectExplicitPluginConfigNames(
        IReadOnlyList<KernelTurnInputItem>? inputItems,
        string? userText)
    {
        var configNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (inputItems is not null)
        {
            foreach (var item in inputItems)
            {
                CollectExplicitPluginConfigNames(item, configNames);
            }
        }

        CollectExplicitPluginConfigNames(userText, configNames);
        return configNames;
    }

    private static void CollectExplicitPluginConfigNames(KernelTurnInputItem item, HashSet<string> configNames)
    {
        CollectExplicitPluginConfigName(item.Path, configNames);
        CollectExplicitPluginConfigNames(item.Text, configNames);
        foreach (var segment in item.ContentItems)
        {
            CollectExplicitPluginConfigNames(segment, configNames);
        }
    }

    private static void CollectExplicitPluginConfigName(string? path, HashSet<string> configNames)
    {
        var configName = TryGetPluginConfigNameFromPath(path);
        if (!string.IsNullOrWhiteSpace(configName))
        {
            configNames.Add(configName!);
        }
    }

    private static void CollectExplicitPluginConfigNames(string? text, HashSet<string> configNames)
    {
        var normalized = Normalize(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        foreach (Match match in LinkedPluginMentionRegex.Matches(normalized!))
        {
            CollectExplicitPluginConfigName(match.Groups["path"].Value, configNames);
        }
    }

    private static string? TryGetPluginConfigNameFromPath(string? path)
    {
        var normalized = Normalize(path);
        const string prefix = "plugin://";
        if (string.IsNullOrWhiteSpace(normalized) || !normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Normalize(normalized[prefix.Length..]);
    }

    private static string? RenderExplicitPluginInstructions(
        KernelPluginCapabilitySummary summary,
        TianShuPromptConfiguration promptConfiguration)
    {
        var lines = new List<string>
        {
            $"`{summary.DisplayName}` 插件提供的能力：",
        };

        if (summary.HasSkills)
        {
            lines.Add($"- 此插件的技能以 `{summary.DisplayName}:` 为前缀。");
        }

        if (summary.McpServerNames.Count > 0)
        {
            lines.Add($"- 本会话可用的此插件 MCP 服务器：{string.Join(", ", summary.McpServerNames.Select(static name => $"`{name}`"))}。");
        }

        if (summary.AppIds.Count > 0)
        {
            lines.Add($"- 本会话可用的此插件应用：{string.Join(", ", summary.AppIds.Select(static appId => $"`{appId}`"))}。");
        }

        if (lines.Count == 1)
        {
            return null;
        }

        lines.Add("请结合这些插件关联能力完成当前任务。");
        var builtIn = string.Join(Environment.NewLine, lines);
        var template = Normalize(promptConfiguration.PluginExplicitCapabilityTemplate);
        if (template is null)
        {
            return builtIn;
        }

        var capabilities = string.Join(Environment.NewLine, lines.Skip(1).Take(lines.Count - 2));
        return template
            .Replace("{display_name}", summary.DisplayName, StringComparison.Ordinal)
            .Replace("{capabilities}", capabilities, StringComparison.Ordinal);
    }

    private async Task<List<KernelSkillDescriptor>> ResolveMentionedSkillsAsync(
        TurnRequestContext context,
        string userText,
        CancellationToken cancellationToken)
    {
        var cwd = Normalize(context.Cwd) ?? Environment.CurrentDirectory;
        var scan = await skillsManager.ScanAsync(cwd, Array.Empty<string>(), forceReload: false, cancellationToken).ConfigureAwait(false);
        var enabledSkills = scan.Skills
            .Where(static skill => skill.Enabled)
            .ToArray();
        if (enabledSkills.Length == 0)
        {
            return [];
        }

        var matches = new List<KernelSkillDescriptor>();
        var matchedPaths = new HashSet<string>(KernelPathComparer);
        var blockedPlainNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skillNameCounts = BuildSkillNameCounts(enabledSkills);
        foreach (var item in context.InputItems ?? [])
        {
            CollectStructuredSkillMentions(item, enabledSkills, cwd, matches, matchedPaths, blockedPlainNames);
        }

        foreach (Match match in LinkedToolMentionRegex.Matches(userText ?? string.Empty))
        {
            var linkedPath = match.Groups["path"].Value;
            var pathKind = GetMentionPathKind(linkedPath);
            if (pathKind is not KernelMentionPathKind.App
                and not KernelMentionPathKind.Mcp
                and not KernelMentionPathKind.Plugin)
            {
                AddMatchedSkill(linkedPath, enabledSkills, cwd, matches, matchedPaths);
            }
        }

        var connectorSlugCounts = BuildConnectorSlugCounts(
            await pluginsAppHostRuntime.LoadAppsAsync(forceRefetch: false, cwd, cancellationToken).ConfigureAwait(false));
        var ambiguousMentions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in PlainSkillMentionRegex.Matches(StripLinkedToolMentions(userText) ?? string.Empty))
        {
            var mentionName = Normalize(match.Groups["name"].Value);
            if (string.IsNullOrWhiteSpace(mentionName))
            {
                continue;
            }

            if (IsCommonEnvironmentVariableMention(mentionName))
            {
                continue;
            }

            if (blockedPlainNames.Contains(mentionName!))
            {
                continue;
            }

            if (connectorSlugCounts.TryGetValue(mentionName!.ToLowerInvariant(), out var connectorCount)
                && connectorCount > 0)
            {
                continue;
            }

            if (ambiguousMentions.Contains(mentionName!))
            {
                continue;
            }

            if (skillNameCounts.TryGetValue(mentionName!, out var skillCount) && skillCount != 1)
            {
                ambiguousMentions.Add(mentionName!);
                continue;
            }

            var candidates = enabledSkills
                .Where(skill => string.Equals(skill.Name, mentionName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (candidates.Length == 1)
            {
                AddMatchedSkill(candidates[0], matches, matchedPaths);
                continue;
            }

            if (candidates.Length > 1)
            {
                ambiguousMentions.Add(mentionName!);
            }
        }

        return matches;
    }

    private static void CollectStructuredSkillMentions(
        KernelTurnInputItem item,
        IReadOnlyList<KernelSkillDescriptor> skills,
        string cwd,
        List<KernelSkillDescriptor> matches,
        HashSet<string> matchedPaths,
        HashSet<string> blockedPlainNames)
    {
        var itemType = Normalize(item.Type)?.ToLowerInvariant();
        var path = item.Path;
        var canonicalPath = item.CanonicalPath;
        var resolvedPath = !string.IsNullOrWhiteSpace(canonicalPath)
            ? canonicalPath
            : path;
        var name = item.Name;
        if (string.Equals(itemType, "skill", StringComparison.Ordinal)
            || (string.Equals(itemType, "mention", StringComparison.Ordinal)
                && (!string.IsNullOrWhiteSpace(canonicalPath) || LooksLikeSkillPath(path))))
        {
            var blockedName = ResolveStructuredSkillPlainName(resolvedPath, name);
            if (!string.IsNullOrWhiteSpace(blockedName))
            {
                blockedPlainNames.Add(blockedName!);
            }

            if (!string.IsNullOrWhiteSpace(resolvedPath))
            {
                var matched = AddMatchedSkill(resolvedPath!, skills, cwd, matches, matchedPaths);
                if (matched is not null)
                {
                    blockedPlainNames.Add(matched.Name);
                }
            }
            else if (!string.IsNullOrWhiteSpace(name))
            {
                var matched = skills.SingleOrDefault(skill => string.Equals(skill.Name, name, StringComparison.OrdinalIgnoreCase));
                if (matched is not null)
                {
                    AddMatchedSkill(matched, matches, matchedPaths);
                    blockedPlainNames.Add(matched.Name);
                }
            }
        }

        foreach (var segment in item.ContentItems)
        {
            CollectStructuredSkillMentions(segment, skills, cwd, matches, matchedPaths, blockedPlainNames);
        }
    }

    private static bool LooksLikeSkillPath(string? path)
    {
        var normalized = Normalize(path);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return GetMentionPathKind(normalized) is not KernelMentionPathKind.App
            and not KernelMentionPathKind.Mcp
            and not KernelMentionPathKind.Plugin;
    }

    private static string? ResolveStructuredSkillPlainName(string? path, string? name)
    {
        var normalizedName = Normalize(name);
        if (!string.IsNullOrWhiteSpace(normalizedName))
        {
            return normalizedName;
        }

        var normalizedPath = Normalize(path);
        const string skillScheme = "skill://";
        if (string.IsNullOrWhiteSpace(normalizedPath)
            || !normalizedPath.StartsWith(skillScheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Normalize(normalizedPath[skillScheme.Length..]);
    }

    private static KernelSkillDescriptor? AddMatchedSkill(
        string path,
        IReadOnlyList<KernelSkillDescriptor> skills,
        string cwd,
        List<KernelSkillDescriptor> matches,
        HashSet<string> matchedPaths)
    {
        var matched = TryResolveSkillByPath(path, skills, cwd);
        if (matched is not null)
        {
            AddMatchedSkill(matched, matches, matchedPaths);
        }

        return matched;
    }

    private static void AddMatchedSkill(
        KernelSkillDescriptor skill,
        List<KernelSkillDescriptor> matches,
        HashSet<string> matchedPaths)
    {
        if (!matchedPaths.Add(skill.PathToSkillsMd))
        {
            return;
        }

        matches.Add(skill);
    }

    private static KernelSkillDescriptor? TryResolveSkillByPath(
        string path,
        IReadOnlyList<KernelSkillDescriptor> skills,
        string cwd)
    {
        var normalized = Normalize(path);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var canonicalPath = TryNormalizeCanonicalSkillPath(normalized, cwd);
        if (!string.IsNullOrWhiteSpace(canonicalPath))
        {
            return skills.SingleOrDefault(skill => KernelPathUtilities.AreEquivalentForComparison(skill.PathToSkillsMd, canonicalPath));
        }

        const string skillScheme = "skill://";
        if (normalized.StartsWith(skillScheme, StringComparison.OrdinalIgnoreCase))
        {
            var skillName = Normalize(normalized[skillScheme.Length..]);
            return string.IsNullOrWhiteSpace(skillName)
                ? null
                : skills.SingleOrDefault(skill => string.Equals(skill.Name, skillName, StringComparison.OrdinalIgnoreCase));
        }

        try
        {
            var skillPath = KernelPathUtilities.NormalizeSkillDocumentPath(normalized, cwd);
            return skills.SingleOrDefault(skill => KernelPathUtilities.AreEquivalentForComparison(skill.PathToSkillsMd, skillPath));
        }
        catch
        {
            return null;
        }
    }

    private static string? TryNormalizeCanonicalSkillPath(string path, string cwd)
    {
        var normalized = Normalize(path);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        const string skillScheme = "skill://";
        if (normalized.StartsWith(skillScheme, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[skillScheme.Length..];
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var looksLikePath = Path.IsPathRooted(normalized)
            || normalized.Contains('/') 
            || normalized.Contains('\\')
            || normalized.EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase);
        if (!looksLikePath)
        {
            return null;
        }

        try
        {
            return KernelPathUtilities.NormalizeSkillDocumentPath(normalized, cwd);
        }
        catch
        {
            return null;
        }
    }

    private static string? StripLinkedToolMentions(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? text
            : LinkedToolMentionRegex.Replace(text, " ");

    private static Dictionary<string, int> BuildConnectorSlugCounts(IReadOnlyList<ControlPlaneAppDescriptor> apps)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in apps)
        {
            var slug = Normalize(app.Id)?.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(slug))
            {
                continue;
            }

            counts[slug!] = counts.TryGetValue(slug!, out var count) ? count + 1 : 1;
        }

        return counts;
    }

    private static Dictionary<string, int> BuildSkillNameCounts(IReadOnlyList<KernelSkillDescriptor> skills)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var skill in skills)
        {
            var name = Normalize(skill.Name);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            counts[name!] = counts.TryGetValue(name!, out var count) ? count + 1 : 1;
        }

        return counts;
    }

    private static bool IsCommonEnvironmentVariableMention(string? name)
    {
        var normalized = Normalize(name);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return normalized.ToUpperInvariant() switch
        {
            "PATH" => true,
            "HOME" => true,
            "USER" => true,
            "SHELL" => true,
            "PWD" => true,
            "TMPDIR" => true,
            "TEMP" => true,
            "TMP" => true,
            "LANG" => true,
            "TERM" => true,
            "XDG_CONFIG_HOME" => true,
            _ => false,
        };
    }

    private static KernelMentionPathKind GetMentionPathKind(string? path)
    {
        var normalized = Normalize(path);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return KernelMentionPathKind.Other;
        }

        if (normalized.StartsWith("skill://", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase))
        {
            return KernelMentionPathKind.Skill;
        }

        if (normalized.StartsWith("app://", StringComparison.OrdinalIgnoreCase))
        {
            return KernelMentionPathKind.App;
        }

        if (normalized.StartsWith("mcp://", StringComparison.OrdinalIgnoreCase))
        {
            return KernelMentionPathKind.Mcp;
        }

        if (normalized.StartsWith("plugin://", StringComparison.OrdinalIgnoreCase))
        {
            return KernelMentionPathKind.Plugin;
        }

        return KernelMentionPathKind.Other;
    }

    private enum KernelMentionPathKind
    {
        Other,
        Skill,
        App,
        Mcp,
        Plugin,
    }

    private static List<string> BuildSkillInjectionMessages(IReadOnlyList<KernelSkillDescriptor> skills)
    {
        var messages = new List<string>(skills.Count);
        foreach (var skill in skills)
        {
            var contents = Normalize(File.Exists(skill.PathToSkillsMd) ? File.ReadAllText(skill.PathToSkillsMd) : null);
            if (string.IsNullOrWhiteSpace(contents))
            {
                continue;
            }

            messages.Add(
                "<skill>" + Environment.NewLine
                + $"<name>{skill.Name}</name>" + Environment.NewLine
                + $"<path>{skill.PathToSkillsMd}</path>" + Environment.NewLine
                + contents + Environment.NewLine
                + "</skill>");
        }

        return messages;
    }

    private async Task ResolveSkillEnvironmentDependenciesAsync(
        TurnOperationState state,
        TurnRequestContext context,
        IReadOnlyList<KernelSkillDescriptor> skills,
        CancellationToken cancellationToken)
    {
        var dependencyEnvironment = context.DependencyEnvironment
            ?? throw new InvalidOperationException("turn context missing dependency environment collection");
        var requiredVariables = skills
            .SelectMany(static skill => skill.Dependencies?.Tools ?? [])
            .Where(static dependency => string.Equals(dependency.Type, "env_var", StringComparison.OrdinalIgnoreCase))
            .GroupBy(static dependency => dependency.Value, EnvironmentVariableComparer)
            .Select(static group => group.First())
            .Where(static dependency => !string.IsNullOrWhiteSpace(Normalize(dependency.Value)))
            .ToArray();
        if (requiredVariables.Length == 0)
        {
            return;
        }

        var missing = requiredVariables
            .Where(dependency => !HasResolvedDependencyEnvironmentValue(dependencyEnvironment, dependency.Value))
            .ToArray();
        if (missing.Length == 0)
        {
            return;
        }

        var response = await SendServerRequestAsync(
            "item/tool/requestUserInput",
            new
            {
                threadId = state.ThreadId,
                turnId = state.TurnId,
                itemId = $"skill_dependency_env_{state.TurnId}",
                questions = missing.Select(static dependency => new
                {
                    id = dependency.Value,
                    header = dependency.Value.Length <= 12 ? dependency.Value : dependency.Value[..12],
                    question = string.IsNullOrWhiteSpace(dependency.Description)
                        ? $"当前技能需要环境变量 {dependency.Value}，请在 Other 输入框中填写实际值。"
                        : $"当前技能需要环境变量 {dependency.Value}：{dependency.Description}。请在 Other 输入框中填写实际值。",
                    isSecret = true,
                    options = new[]
                    {
                        new
                        {
                            label = "在 Other 中填写值 (Recommended)",
                            description = "在客户端自动提供的 Other 输入框中粘贴真实值并继续本回合。",
                        },
                        new
                        {
                            label = "取消本回合",
                            description = "不提供该变量，当前回合将停止。",
                        },
                    },
                }).ToArray(),
            },
            state.ThreadId,
            cancellationToken,
            timeoutOverride: TimeSpan.FromMinutes(2)).ConfigureAwait(false);

        var resolved = new Dictionary<string, string>(EnvironmentVariableComparer);
        foreach (var dependency in missing)
        {
            var answer = Normalize(ReadFirstRequestUserInputAnswer(response, dependency.Value));
            if (string.IsNullOrWhiteSpace(answer)
                || string.Equals(answer, "取消本回合", StringComparison.OrdinalIgnoreCase)
                || string.Equals(answer, "在 Other 中填写值 (Recommended)", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"技能依赖环境变量 `{dependency.Value}` 未提供，当前回合已停止。");
            }

            resolved[dependency.Value] = answer!;
            dependencyEnvironment[dependency.Value] = answer!;
        }

        if (threadManager.TryGetThread(state.ThreadId, out var runtimeThread) && runtimeThread is not null)
        {
            runtimeThread.UpsertDependencyEnvironment(resolved);
        }
    }

    private static bool HasResolvedDependencyEnvironmentValue(
        IReadOnlyDictionary<string, string> dependencyEnvironment,
        string variableName)
    {
        if (dependencyEnvironment.TryGetValue(variableName, out var value)
            && !string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variableName));
    }

    private async Task ResolveSkillMcpDependenciesAsync(
        TurnOperationState state,
        TurnRequestContext context,
        IReadOnlyList<KernelSkillDescriptor> skills,
        CancellationToken cancellationToken)
    {
        var requiredServers = skills
            .SelectMany(static skill => skill.Dependencies?.Tools ?? [])
            .Where(static dependency => string.Equals(dependency.Type, "mcp", StringComparison.OrdinalIgnoreCase))
            .GroupBy(static dependency => dependency.Value, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .Where(static dependency => !string.IsNullOrWhiteSpace(Normalize(dependency.Value)))
            .ToArray();
        if (requiredServers.Length == 0)
        {
            return;
        }

        var configuredServers = (await mcpManager.ListServerNamesAsync(cancellationToken).ConfigureAwait(false))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingServers = requiredServers
            .Where(dependency => !configuredServers.Contains(dependency.Value))
            .ToArray();
        if (missingServers.Length == 0)
        {
            return;
        }

        var userConfigPath = ResolveActiveUserConfigPath();
        var response = await SendServerRequestAsync(
            "item/tool/requestUserInput",
            new
            {
                threadId = state.ThreadId,
                turnId = state.TurnId,
                itemId = $"skill_dependency_mcp_{state.TurnId}",
                questions = missingServers.Select(dependency => new
                {
                    id = dependency.Value,
                    header = dependency.Value.Length <= 12 ? dependency.Value : dependency.Value[..12],
                    question = $"当前技能需要 MCP 服务器 {dependency.Value}，是否写入全局 {userConfigPath} 并立即重载？",
                    isSecret = false,
                    options = new[]
                    {
                        new
                        {
                            label = "立即安装 (Recommended)",
                            description = "写入用户级 tianshu.toml 并立即重载 MCP server 列表。",
                        },
                        new
                        {
                            label = "暂不安装",
                            description = "跳过安装，当前回合将停止。",
                        },
                    },
                }).ToArray(),
            },
            state.ThreadId,
            cancellationToken,
            timeoutOverride: TimeSpan.FromMinutes(2)).ConfigureAwait(false);

        var values = await LoadWritablePersistedConfigValuesAsync(
                cancellationToken,
                filePath: userConfigPath)
            .ConfigureAwait(false);
        var installedAny = false;
        foreach (var dependency in missingServers)
        {
            var answer = Normalize(ReadFirstRequestUserInputAnswer(response, dependency.Value));
            if (string.IsNullOrWhiteSpace(answer)
                || string.Equals(answer, "暂不安装", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"技能依赖 MCP 服务器 `{dependency.Value}` 未安装，当前回合已停止。");
            }

            if (!TryApplyMcpDependencyConfigValues(values, dependency))
            {
                throw new InvalidOperationException($"技能依赖 MCP 服务器 `{dependency.Value}` 缺少可写入的 transport 信息。");
            }

            installedAny = true;
        }

        if (!installedAny)
        {
            return;
        }

        await SaveConfigValuesAsync(
                values,
                cancellationToken,
                filePath: userConfigPath)
            .ConfigureAwait(false);
        var reloadResult = await mcpManager.ReloadAsync(cancellationToken).ConfigureAwait(false);
        await WriteNotificationAsync("mcpServerStatus/list/updated", new
        {
            data = reloadResult.Data,
        }, CancellationToken.None).ConfigureAwait(false);
    }

    private static bool TryApplyMcpDependencyConfigValues(
        Dictionary<string, string> values,
        KernelSkillToolDependency dependency)
    {
        var serverName = Normalize(dependency.Value);
        if (string.IsNullOrWhiteSpace(serverName))
        {
            return false;
        }

        values[$"mcp_servers.{serverName}.enabled"] = "true";
        if (!string.IsNullOrWhiteSpace(Normalize(dependency.Command)))
        {
            values[$"mcp_servers.{serverName}.command"] = JsonSerializer.Serialize(dependency.Command);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(Normalize(dependency.Url)))
        {
            values[$"mcp_servers.{serverName}.url"] = JsonSerializer.Serialize(dependency.Url);
            return true;
        }

        return false;
    }

    private static string? ReadFirstRequestUserInputAnswer(JsonElement response, string questionId)
    {
        if (!response.TryGetProperty("answers", out var answers)
            || answers.ValueKind != JsonValueKind.Object
            || !answers.TryGetProperty(questionId, out var answer)
            || !answer.TryGetProperty("answers", out var values)
            || values.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var entry in values.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var normalized = Normalize(entry.GetString());
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        return null;
    }

    private static bool TryValidateInputLimit(JsonElement @params, out int actualChars)
    {
        if (KernelToolJsonHelpers.TryReadInputArray(@params, out var inputItems))
        {
            actualChars = KernelTurnExecutionRuntimeHelpers.CountInputTextChars(inputItems);
        }
        else
        {
            actualChars = 0;
        }

        return actualChars <= MaxUserInputTextChars;
    }

    private static bool TryValidateInputLimit(KernelTurnStartRequest request, out int actualChars)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Input is { Count: > 0 } inputItems)
        {
            actualChars = KernelTurnExecutionRuntimeHelpers.CountInputTextChars(inputItems);
        }
        else
        {
            actualChars = 0;
        }

        return actualChars <= MaxUserInputTextChars;
    }

    private void EnqueueSteerInput(string turnId, string text)
    {
        if (string.IsNullOrWhiteSpace(turnId) || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var queue = steerInputsByTurn.GetOrAdd(turnId, static _ => new ConcurrentQueue<string>());
        queue.Enqueue(text);
    }

    private IReadOnlyList<string> DrainSteerInputs(string turnId)
    {
        if (!steerInputsByTurn.TryGetValue(turnId, out var queue))
        {
            return Array.Empty<string>();
        }

        var inputs = new List<string>();
        while (queue.TryDequeue(out var steerText))
        {
            var normalized = Normalize(steerText);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                inputs.Add(normalized!);
            }
        }

        return inputs;
    }

    private KernelThreadSessionState BuildThreadSessionState(
        KernelThreadRecord record,
        KernelThreadResumeRequest request,
        ConfiguredThreadDefaults configuredDefaults)
    {
        var configuredPermissions = configuredDefaults.Permissions;
        var builder = KernelThreadSessionBuilder
            .FromRecord(
                record,
                DefaultModel,
                DefaultModelProvider,
                configuredPermissions.ApprovalPolicy,
                configuredPermissions.SandboxPolicy,
                configuredPermissions.SandboxMode,
                configuredPermissions.AllowLoginShell,
                configuredPermissions.ShellEnvironmentPolicy);

        // 与已加载线程的 resume 路径保持一致：当 request.Config 存在时，请求层配置应覆盖持久化快照。
        builder = request.Config is null
            ? builder
                .ApplyConfigSnapshot(configuredDefaults.ConfigSnapshot)
                .ApplyConfigSnapshot(record.ConfigSnapshot)
            : builder
                .ApplyConfigSnapshot(record.ConfigSnapshot)
                .ApplyConfigSnapshot(configuredDefaults.ConfigSnapshot);

        var session = builder
            .ApplyProviderConnectionSnapshot(ResolveThreadResumeProviderConnectionSnapshot(configuredDefaults, request))
            .ApplyThreadResume(request)
            .Build();
        return session;
    }

    private KernelThreadConfigSnapshot? ResolveThreadResumeProviderConnectionSnapshot(
        string? cwd,
        KernelThreadResumeRequest request)
    {
        var requestedProvider = Normalize(request.ModelProvider);
        if (requestedProvider is null)
        {
            return null;
        }

        var configuredDefaults = ResolveConfiguredThreadDefaultsWithThreadError(cwd, request.Config);
        return ResolveThreadResumeProviderConnectionSnapshot(configuredDefaults, request);
    }

    private static KernelThreadConfigSnapshot? ResolveThreadResumeProviderConnectionSnapshot(
        ConfiguredThreadDefaults configuredDefaults,
        KernelThreadResumeRequest request)
    {
        var requestedProvider = Normalize(request.ModelProvider);
        if (requestedProvider is null)
        {
            return null;
        }

        var configuredSnapshot = configuredDefaults.ConfigSnapshot;
        return string.Equals(
            Normalize(configuredSnapshot.ModelProviderId),
            requestedProvider,
            StringComparison.OrdinalIgnoreCase)
            ? ResolveProviderConnectionSnapshotForModel(configuredSnapshot, configuredDefaults.RawConfig, request.Model, requestedProvider)
            : null;
    }

    private KernelThreadSessionState BuildThreadSessionStateWithConfigLoadHandling(KernelThreadRecord record, KernelThreadResumeRequest request)
    {
        var configuredDefaults = ResolveConfiguredThreadDefaultsWithThreadError(record.Cwd, request.Config);
        return BuildThreadSessionState(record, request, configuredDefaults);
    }

    private static KernelThreadSessionState BuildThreadSessionStateFromConfiguredDefaults(
        KernelThreadRecord record,
        ConfiguredThreadDefaults configuredDefaults)
    {
        var configuredPermissions = configuredDefaults.Permissions;
        var session = KernelThreadSessionBuilder
            .FromRecord(
                record,
                DefaultModel,
                DefaultModelProvider,
                configuredPermissions.ApprovalPolicy,
                configuredPermissions.SandboxPolicy,
                configuredPermissions.SandboxMode,
                configuredPermissions.AllowLoginShell,
                configuredPermissions.ShellEnvironmentPolicy)
            .ApplyConfigSnapshot(ResolveProviderConnectionSnapshotForModel(
                configuredDefaults.ConfigSnapshot,
                configuredDefaults.RawConfig,
                configuredDefaults.ConfigSnapshot.Model,
                configuredDefaults.ConfigSnapshot.ModelProviderId))
            .Build();
        return session;
    }

    private KernelThreadSessionState BuildThreadSessionStateForNewThread(string threadId, KernelThreadStartRequest request)
    {
        var normalizedCwd = Normalize(request.Cwd);
        var configuredDefaults = ResolveConfiguredThreadDefaultsWithThreadError(normalizedCwd, request.Config);
        var record = new KernelThreadRecord
        {
            Id = threadId,
            Cwd = normalizedCwd,
        };

        var configuredPermissions = configuredDefaults.Permissions;
        return KernelThreadSessionBuilder
            .FromRecord(
                record,
                DefaultModel,
                DefaultModelProvider,
                configuredPermissions.ApprovalPolicy,
                configuredPermissions.SandboxPolicy,
                configuredPermissions.SandboxMode,
                configuredPermissions.AllowLoginShell,
                configuredPermissions.ShellEnvironmentPolicy)
            .ApplyConfigSnapshot(ResolveProviderConnectionSnapshotForModel(
                configuredDefaults.ConfigSnapshot,
                configuredDefaults.RawConfig,
                request.Model,
                request.ModelProvider))
            .ApplyConfigSnapshot(record.ConfigSnapshot)
            .ApplyThreadStart(request)
            .Build();
    }

    private KernelThreadSessionState BuildThreadSessionStateForNewThread(string threadId, KernelThreadForkRequest request, string? cwdOverride = null)
    {
        var normalizedCwd = Normalize(cwdOverride ?? request.Cwd);
        var configuredDefaults = ResolveConfiguredThreadDefaultsWithThreadError(normalizedCwd, request.Config);
        var record = new KernelThreadRecord
        {
            Id = threadId,
            Cwd = normalizedCwd,
        };

        var configuredPermissions = configuredDefaults.Permissions;
        return KernelThreadSessionBuilder
            .FromRecord(
                record,
                DefaultModel,
                DefaultModelProvider,
                configuredPermissions.ApprovalPolicy,
                configuredPermissions.SandboxPolicy,
                configuredPermissions.SandboxMode,
                configuredPermissions.AllowLoginShell,
                configuredPermissions.ShellEnvironmentPolicy)
            .ApplyConfigSnapshot(ResolveProviderConnectionSnapshotForModel(
                configuredDefaults.ConfigSnapshot,
                configuredDefaults.RawConfig,
                request.Model,
                request.ModelProvider))
            .ApplyConfigSnapshot(record.ConfigSnapshot)
            .ApplyThreadFork(request)
            .Build();
    }

    private KernelThreadSessionState ApplyTurnOverrides(KernelThreadSessionState current, KernelTurnStartRequest request)
    {
        return KernelThreadSessionBuilder
            .FromSession(current)
            .ApplyTurnOverrides(request)
            .Build();
    }

    private KernelThreadSessionState GetOrCreateThreadSession(KernelThreadRecord record)
    {
        return threadManager.GetOrAttachThread(record, BuildDefaultThreadSession, loaded: false).Session;
    }

    private async Task PersistRuntimeThreadSessionSnapshotAsync(
        KernelRuntimeThread runtimeThread,
        CancellationToken cancellationToken)
    {
        var record = await threadStore.GetThreadAsync(runtimeThread.Id, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return;
        }

        var configSnapshot = runtimeThread.ConfigSnapshot.DeepClone();
        record.ConfigSnapshot = configSnapshot;
        var updated = await threadStore.UpsertThreadAsync(record, cancellationToken).ConfigureAwait(false);
        runtimeThread.Update(updated);
        if (configSnapshot.Ephemeral)
        {
            return;
        }

        await threadStore.RolloutRecorder
            .AppendSessionStateAsync(
                updated.Id,
                KernelRolloutStateMapper.ToRolloutThreadRecord(updated, configSnapshot),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> IsEphemeralThreadAsync(string threadId, CancellationToken cancellationToken)
    {
        if (threadManager.TryGetThread(threadId, out var runtimeThread) && runtimeThread is not null)
        {
            return runtimeThread.Session.Ephemeral;
        }

        return await threadStore.IsEphemeralThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
    }

    private KernelThreadSessionState BuildDefaultThreadSession(KernelThreadRecord record)
    {
        var configuredDefaults = ResolveConfiguredThreadDefaults(record.Cwd);
        var configuredPermissions = configuredDefaults.Permissions;
        var session = KernelThreadSessionBuilder
            .FromRecord(
                record,
                DefaultModel,
                DefaultModelProvider,
                configuredPermissions.ApprovalPolicy,
                configuredPermissions.SandboxPolicy,
                configuredPermissions.SandboxMode,
                configuredPermissions.AllowLoginShell,
                configuredPermissions.ShellEnvironmentPolicy)
            .ApplyConfigSnapshot(ResolveProviderConnectionSnapshotForModel(
                configuredDefaults.ConfigSnapshot,
                configuredDefaults.RawConfig,
                configuredDefaults.ConfigSnapshot.Model,
                configuredDefaults.ConfigSnapshot.ModelProviderId))
            .ApplyConfigSnapshot(record.ConfigSnapshot)
            .Build();
        return session;
    }

    private ConfiguredThreadDefaults ResolveConfiguredThreadDefaults(string? cwd)
        => ResolveConfiguredThreadDefaults(cwd, requestConfig: null);

    private ConfiguredThreadDefaults ResolveConfiguredThreadDefaults(
        string? cwd,
        KernelConfigOverridePayload? requestConfig)
    {
        var effectiveCwd = Normalize(cwd) ?? Environment.CurrentDirectory;
        var snapshot = BuildConfigReadSnapshotForRuntime(effectiveCwd, requestConfig);
        var permissions = ResolveConfiguredPermissionSettings(snapshot, effectiveCwd);
        return new ConfiguredThreadDefaults(
            BuildConfiguredThreadConfigSnapshot(snapshot, permissions, effectiveCwd),
            permissions,
            snapshot.Config);
    }

    private ConfiguredThreadDefaults ResolveConfiguredThreadDefaultsWithThreadError(string? cwd)
        => ResolveConfiguredThreadDefaultsWithThreadError(cwd, requestConfig: null);

    private ConfiguredThreadDefaults ResolveConfiguredThreadDefaultsWithThreadError(
        string? cwd,
        KernelConfigOverridePayload? requestConfig)
    {
        try
        {
            return ResolveConfiguredThreadDefaults(cwd, requestConfig);
        }
        catch (Exception ex) when (IsThreadConfigLoadException(ex))
        {
            throw CreateThreadConfigLoadRpcException(ex);
        }
    }

    private async Task EnsureRequiredMcpServersInitializedWithThreadErrorAsync(
        KernelThreadSessionState session,
        CancellationToken cancellationToken)
    {
        try
        {
            await mcpManager
                .EnsureRequiredServersInitializedAsync(
                    KernelMcpSandboxState.Create(session.SandboxPolicy, session.Cwd),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("required MCP servers failed to initialize", StringComparison.Ordinal))
        {
            throw new KernelJsonRpcException(-32600, $"failed to load configuration: {ex.Message}", dataPayload: null);
        }
    }

    private static KernelThreadConfigSnapshot ResolveProviderConnectionSnapshotForModel(
        KernelThreadConfigSnapshot snapshot,
        Dictionary<string, object?> rawConfig,
        string? modelOverride,
        string? providerOverride)
    {
        var model = Normalize(modelOverride) ?? snapshot.Model;
        var provider = Normalize(providerOverride) ?? snapshot.ModelProviderId;
        var providerWireApi = KernelModelProtocolResolver.ResolveModelProtocol(rawConfig, provider, model);
        return snapshot with
        {
            Model = model,
            ModelProviderId = provider,
            ProviderWireApi = providerWireApi,
        };
    }

    private KernelThreadConfigSnapshot BuildConfiguredThreadConfigSnapshot(
        KernelConfigReadSnapshot snapshot,
        KernelResolvedPermissionRuntimeSettings permissions,
        string cwd)
    {
        var model = ReadStringExact(snapshot.Config, "model") ?? DefaultModel;
        var modelProvider = ReadStringExact(snapshot.Config, "provider")
                            ?? DefaultModelProvider;
        var modelRouteSetId = ResolveConfiguredModelRouteSetId(snapshot.Config);
        var serviceTier = ReadConfiguredServiceTier(snapshot.Config, "service_tier");
        var webSearchMode = ReadStringExact(snapshot.Config, "web_search");
        var reasoningEffort = ReadStringExact(snapshot.Config, "model_reasoning_effort")
                              ?? ReadConfiguredModelProviderNestedSetting(snapshot.Config, modelProvider, "reasoning", "effort");
        var reasoningSummary = ReadStringExact(snapshot.Config, "model_reasoning_summary")
                               ?? ReadConfiguredModelProviderNestedSetting(snapshot.Config, modelProvider, "reasoning", "summary")
                               ?? GetDefaultReasoningSummary(model);
        var verbosity = ReadStringExact(snapshot.Config, "model_verbosity")
                        ?? ReadConfiguredModelProviderNestedSetting(snapshot.Config, modelProvider, "reasoning", "verbosity")
                        ?? GetDefaultVerbosity(model);
        var explicitBaseInstructions = ReadConfiguredStringWithActiveProfile(
            snapshot.Config,
            out _,
            "base_instructions");
        var modelInstructionsFile = ReadConfiguredStringWithActiveProfile(
            snapshot.Config,
            out var modelInstructionsKeyPath,
            "model_instructions_file");
        var fileBaseInstructions = explicitBaseInstructions is null
            ? LoadInstructionFileIfConfigured(
                snapshot,
                modelInstructionsKeyPath,
                modelInstructionsFile,
                cwd,
                "model_instructions_file")
            : null;
        var promptConfiguration = TianShuPromptConfigUtilities.FromConfig(snapshot.Config);
        var builtInBaseInstructions = GetBaseInstructions(model);
        var promptBaseInstructions = TianShuPromptConfigUtilities.ApplySection(
            promptConfiguration.Base,
            builtInBaseInstructions);
        var baseInstructions = explicitBaseInstructions
            ?? fileBaseInstructions
            ?? promptBaseInstructions
            ?? builtInBaseInstructions;
        var projectDocScopedConfig = BuildProjectDocScopedConfig(snapshot);
        var userInstructions = BuildScopedUserInstructions(
            cwd,
            projectDocScopedConfig,
            ResolveTianShuHomePath());
        var configuredDeveloperInstructions = ReadConfiguredStringWithActiveProfile(
            snapshot.Config,
            out _,
            "developer_instructions");
        var promptDeveloperInstructions = TianShuPromptConfigUtilities.ApplySection(
            promptConfiguration.Developer,
            null);
        var developerInstructions = BuildScopedDeveloperInstructions(
            cwd,
            JoinOptionalInstructionSections(promptDeveloperInstructions, configuredDeveloperInstructions),
            projectDocScopedConfig);
        var providerBaseUrl = ReadConfiguredModelProviderSetting(snapshot.Config, modelProvider, "base_url");
        var providerApiKeyEnvironmentVariable = ReadConfiguredModelProviderSetting(snapshot.Config, modelProvider, "api_key_env");
        var providerWireApi = KernelModelProtocolResolver.ResolveModelProtocol(snapshot.Config, modelProvider, model);
        var providerRequestMaxRetries = ReadConfiguredModelProviderInt(snapshot.Config, modelProvider, "request_max_retries");
        var providerStreamMaxRetries = ReadConfiguredModelProviderInt(snapshot.Config, modelProvider, "stream_max_retries");
        var providerStreamIdleTimeoutMs = ReadConfiguredModelProviderLong(snapshot.Config, modelProvider, "stream_idle_timeout_ms");
        var providerWebsocketConnectTimeoutMs = ReadConfiguredModelProviderLong(snapshot.Config, modelProvider, "websocket_connect_timeout_ms");
        var providerSupportsWebsockets = ReadConfiguredModelProviderBoolean(snapshot.Config, modelProvider, "supports_websockets");
        var windowsSandboxLevel = ResolveWindowsSandboxLevel(snapshot.Config);
        var defaultModeRequestUserInputEnabled = ResolveDefaultModeRequestUserInputEnabled(snapshot.Config);

        return new KernelThreadConfigSnapshot(
            Model: model,
            ModelProviderId: modelProvider,
            ServiceTier: serviceTier,
            ApprovalPolicy: permissions.ApprovalPolicy,
            SandboxPolicy: permissions.SandboxPolicy.Clone(),
            SandboxMode: permissions.SandboxMode,
            Cwd: cwd,
            Ephemeral: false,
            AllowLoginShell: permissions.AllowLoginShell,
            ShellEnvironmentPolicy: KernelThreadConfigSnapshotFactory.CloneShellEnvironmentPolicy(permissions.ShellEnvironmentPolicy),
            ProviderBaseUrl: providerBaseUrl,
            ProviderApiKeyEnvironmentVariable: providerApiKeyEnvironmentVariable,
            ProviderWireApi: providerWireApi,
            ProviderRequestMaxRetries: providerRequestMaxRetries,
            ProviderStreamMaxRetries: providerStreamMaxRetries,
            ProviderStreamIdleTimeoutMs: providerStreamIdleTimeoutMs,
            ProviderWebsocketConnectTimeoutMs: providerWebsocketConnectTimeoutMs,
            ProviderSupportsWebsockets: providerSupportsWebsockets,
            ProviderHttpFallbackEnabled: false,
            WebSearchMode: webSearchMode,
            ServiceName: null,
            BaseInstructions: baseInstructions,
            DeveloperInstructions: developerInstructions,
            UserInstructions: userInstructions,
            ReasoningEffort: reasoningEffort,
            ReasoningSummary: reasoningSummary,
            Verbosity: verbosity,
            Personality: null,
            DynamicTools: null,
            CollaborationMode: KernelCollaborationModeState.CreateDefault(model, reasoningEffort),
            PersistExtendedHistory: false,
            SessionSource: KernelSessionSource.AppServer,
            WindowsSandboxLevel: windowsSandboxLevel,
            DefaultModeRequestUserInputEnabled: defaultModeRequestUserInputEnabled,
            ModelRouteSetId: modelRouteSetId);
    }

    private async Task ReloadLoadedThreadsUserConfigAsync(CancellationToken cancellationToken)
    {
        skillsManager.ClearCache();
        ReloadProviderPackages();

        foreach (var threadId in threadManager.GetLoadedThreadIds())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!threadManager.TryGetThread(threadId, out var thread) || thread is null)
            {
                continue;
            }

            var currentSession = thread.Session;
            var record = thread.Record;
            var configuredDefaults = ResolveConfiguredThreadDefaultsWithThreadError(record.Cwd);
            var reloadedSession = BuildThreadSessionStateFromConfiguredDefaults(record, configuredDefaults) with
            {
                ProviderHttpFallbackEnabled = currentSession.ProviderHttpFallbackEnabled,
                DynamicTools = KernelDynamicToolResolver.Clone(currentSession.DynamicTools),
                CollaborationMode = currentSession.CollaborationMode is null
                    ? null
                    : KernelThreadConfigSnapshotFactory.CloneCollaborationMode(currentSession.CollaborationMode),
                PersistExtendedHistory = currentSession.PersistExtendedHistory,
                SessionSource = currentSession.SessionSource ?? KernelSessionSource.VsCode,
            };

            var reloadedThread = threadManager.AttachThread(record, reloadedSession, loaded: true, publishCreated: false);
            record.ConfigSnapshot = reloadedThread.ConfigSnapshot.DeepClone();
            await PersistRuntimeThreadSessionSnapshotAsync(reloadedThread, cancellationToken).ConfigureAwait(false);
            await EmitExperimentalInstructionsDeprecationNoticeIfNeededAsync(record.Cwd, cancellationToken).ConfigureAwait(false);
        }

        var reloadResult = await mcpManager.ReloadAsync(cancellationToken).ConfigureAwait(false);
        await WriteNotificationAsync("mcpServerStatus/list/updated", new
        {
            data = reloadResult.Data,
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleConfigProviderReloadAsync(JsonElement id, CancellationToken cancellationToken)
    {
        var result = ReloadProviderPackages();
        await WriteResultAsync(id, result, cancellationToken).ConfigureAwait(false);
    }

    private static ControlPlaneProviderPackageReloadResult ReloadProviderPackages()
        => ProviderRuntimeComposition.ReloadProviderPackages();

    private async Task EmitExperimentalInstructionsDeprecationNoticeIfNeededAsync(string? cwd, CancellationToken cancellationToken)
    {
        var effectiveCwd = Normalize(cwd) ?? Environment.CurrentDirectory;
        var snapshot = BuildConfigReadSnapshotForRuntime(effectiveCwd);
        if (!HasExperimentalInstructionsFile(snapshot))
        {
            return;
        }

        const string summary = "`experimental_instructions_file` is deprecated and ignored. Use `model_instructions_file` instead.";
        const string details = "Move the setting to `model_instructions_file` in tianshu.toml (or under a profile) to load instructions from a file.";
        var noticeKey = summary + "\n" + details;
        if (!emittedDeprecationNotices.TryAdd(noticeKey, 0))
        {
            return;
        }

        await WriteNotificationAsync("deprecationNotice", new
        {
            summary,
            details,
        }, cancellationToken).ConfigureAwait(false);
    }

    private static KernelServiceTier? ReadConfiguredServiceTier(
        Dictionary<string, object?> config,
        params string[] propertyNames)
    {
        var configuredValue = ReadConfiguredServiceTierValue(config, propertyNames);
        return string.IsNullOrWhiteSpace(configuredValue)
            ? null
            : KernelServiceTier.Parse(configuredValue);
    }

    private static JsonElement CreateEmptyObjectElement()
        => JsonSerializer.SerializeToElement(new Dictionary<string, object?>());

    private async Task<TurnRequestContext> BuildTurnRequestContextAsync(
        KernelRuntimeThread runtimeThread,
        KernelThreadSessionState session,
        JsonElement @params,
        CancellationToken cancellationToken)
    {
        var realtimeDeveloperInstructions = KernelRealtimeContextRuntimeHelpers.ResolveRealtimeDeveloperInstructions(
            runtimeThread,
            session.Cwd,
            cwd => BuildConfigReadSnapshotForRuntime(cwd).Config);
        var context = AppHostTurnRequestContextFactory.CreateFromTransportParams(
            runtimeThread,
            session,
            @params,
            ResolvePromptConfiguration(session.Cwd),
            realtimeDeveloperInstructions);
        var routedContext = await coreLoopRoutingAppHostRuntime.ApplyDefaultOrchestrationAndModelRouteAsync(
                runtimeThread.Id,
                session,
                context,
                cancellationToken)
            .ConfigureAwait(false);
        return AttachEnvironmentContextSubagents(runtimeThread.Id, routedContext);
    }

    private async Task<TurnRequestContext> BuildTurnRequestContextAsync(
        KernelRuntimeThread runtimeThread,
        KernelThreadSessionState session,
        KernelTurnStartRequest request,
        CancellationToken cancellationToken)
    {
        var realtimeDeveloperInstructions = KernelRealtimeContextRuntimeHelpers.ResolveRealtimeDeveloperInstructions(
            runtimeThread,
            session.Cwd,
            cwd => BuildConfigReadSnapshotForRuntime(cwd).Config);
        var context = AppHostTurnRequestContextFactory.CreateFromTurnStartRequest(
            runtimeThread,
            session,
            request,
            ResolvePromptConfiguration(session.Cwd),
            realtimeDeveloperInstructions);
        var routedContext = await coreLoopRoutingAppHostRuntime.ApplyDefaultOrchestrationAndModelRouteAsync(
                runtimeThread.Id,
                session,
                context,
                cancellationToken)
            .ConfigureAwait(false);
        return AttachEnvironmentContextSubagents(runtimeThread.Id, routedContext);
    }

    private TurnRequestContext BuildTurnRequestContext(KernelThreadSessionState session)
    {
        return coreLoopRoutingAppHostRuntime.ApplyDefaultOrchestrationAndModelRoute(
            null,
            session,
            BuildBaseTurnRequestContext(session));
    }

    private TurnRequestContext BuildBaseTurnRequestContext(KernelThreadSessionState session)
        => AppHostTurnRequestContextFactory.CreateBase(
            session,
            ResolvePromptConfiguration(session.Cwd));

    private TurnRequestContext BuildTurnRequestContext(string threadId, KernelThreadSessionState session)
        => AttachEnvironmentContextSubagents(
            threadId,
            coreLoopRoutingAppHostRuntime.ApplyDefaultOrchestrationAndModelRoute(
                threadId,
                session,
                BuildBaseTurnRequestContext(session)));

    private async Task<TurnRequestContext> BuildTurnRequestContextAsync(
        string threadId,
        KernelThreadSessionState session,
        CancellationToken cancellationToken)
    {
        var context = await coreLoopRoutingAppHostRuntime.ApplyDefaultOrchestrationAndModelRouteAsync(
                threadId,
                session,
                BuildBaseTurnRequestContext(session),
                cancellationToken)
            .ConfigureAwait(false);
        return AttachEnvironmentContextSubagents(threadId, context);
    }

    private TianShuPromptConfiguration ResolvePromptConfiguration(string? cwd)
        => TianShuPromptConfigUtilities.FromConfig(BuildConfigReadSnapshotForRuntime(cwd).Config);

    private async Task<TurnRequestContext> BuildReviewTurnRequestContextAsync(
        string threadId,
        KernelThreadSessionState session,
        string? modelOverride,
        string reviewDisplayText,
        CancellationToken cancellationToken)
    {
        var context = AttachEnvironmentContextSubagents(
            threadId,
            await coreLoopRoutingAppHostRuntime.ApplyReviewOrchestrationAndModelRouteAsync(
                    threadId,
                    session,
                    BuildBaseTurnRequestContext(session),
                    cancellationToken)
                .ConfigureAwait(false));
        return context with
        {
            Model = Normalize(modelOverride) ?? context.Model,
            IsReview = true,
            ReviewDisplayText = reviewDisplayText,
            DynamicTools = null,
            OutputSchema = KernelReviewOutputParity.CreateOutputSchema(),
        };
    }

    private static string BuildStageCorrelationId(string threadId)
        => $"turn-{threadId}-{Guid.NewGuid():N}";

    private TurnRequestContext AttachEnvironmentContextSubagents(string threadId, TurnRequestContext context)
    {
        var formatted = subagentNotificationAppHostRuntime.FormatEnvironmentContextSubagents(threadId);
        return string.IsNullOrWhiteSpace(formatted)
            ? context
            : context with { EnvironmentContextSubagents = formatted };
    }

    private static JsonElement? TryReadSandboxPolicy(JsonElement @params, params string[] candidateNames)
    {
        foreach (var name in candidateNames)
        {
            if (!TryReadJsonProperty(@params, name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var mode = Normalize(value.GetString()) ?? "workspaceWrite";
                return JsonSerializer.SerializeToElement(new
                {
                    type = mode,
                });
            }

            return value;
        }

        return null;
    }

    private static bool TryReadJsonProperty(JsonElement json, string propertyName, out JsonElement value)
    {
        value = default;
        if (json.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        value = property.Clone();
        return true;
    }

    private static JsonElement? TryReadJsonProperty(JsonElement json, string propertyName)
    {
        return TryReadJsonProperty(json, propertyName, out var value) ? value : null;
    }

    private static string? ResolveSandboxMode(JsonElement policy)
    {
        if (policy.ValueKind == JsonValueKind.String)
        {
            return Normalize(policy.GetString());
        }

        return Normalize(ReadString(policy, "type"));
    }

    internal async Task<McpServerElicitationResponse> RequestMcpServerElicitationAsync(
        string threadId,
        string? turnId,
        McpServerElicitationRequest request,
        CancellationToken cancellationToken)
    {
        var payload = BuildMcpServerElicitationPayload(threadId, turnId, request);
        try
        {
            var response = await SendServerRequestAsync(
                "mcpServer/elicitation/request",
                payload,
                threadId,
                cancellationToken,
                timeoutOverride: TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            return ParseMcpServerElicitationResponse(response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new McpServerElicitationResponse("cancel", null);
        }
        catch (TimeoutException)
        {
            return new McpServerElicitationResponse("decline", null, null);
        }
        catch (Exception)
        {
            return new McpServerElicitationResponse("decline", null, null);
        }
    }

    private static object BuildMcpServerElicitationPayload(
        string threadId,
        string? turnId,
        McpServerElicitationRequest request)
    {
        var mode = Normalize(request.Mode)?.ToLowerInvariant() ?? "form";
        return mode switch
        {
            "url" => new
            {
                threadId,
                turnId = Normalize(turnId),
                serverName = request.ServerName,
                mode = "url",
                _meta = request.Meta,
                message = request.Message,
                url = request.Url,
                elicitationId = request.ElicitationId,
            },
            _ => new
            {
                threadId,
                turnId = Normalize(turnId),
                serverName = request.ServerName,
                mode = "form",
                _meta = request.Meta,
                message = request.Message,
                requestedSchema = McpElicitationSchemaCodec.NormalizeFormRequestedSchema(request.RequestedSchema),
            },
        };
    }

    private static McpServerElicitationResponse ParseMcpServerElicitationResponse(JsonElement response)
    {
        if (response.ValueKind != JsonValueKind.Object)
        {
            return new McpServerElicitationResponse("decline", null, null);
        }

        var action = Normalize(ReadString(response, "action"))?.ToLowerInvariant();
        action = action switch
        {
            "accept" => "accept",
            "cancel" => "cancel",
            _ => "decline",
        };

        JsonElement? content = null;
        if (response.TryGetProperty("content", out var contentElement)
            && contentElement.ValueKind != JsonValueKind.Null
            && contentElement.ValueKind != JsonValueKind.Undefined)
        {
            content = contentElement.Clone();
        }

        JsonElement? meta = null;
        if (response.TryGetProperty("_meta", out var metaElement)
            && metaElement.ValueKind != JsonValueKind.Null
            && metaElement.ValueKind != JsonValueKind.Undefined)
        {
            meta = metaElement.Clone();
        }

        return new McpServerElicitationResponse(action, content, meta);
    }

    private static bool ResolveDefaultModeRequestUserInputEnabled(Dictionary<string, object?> config)
    {
        return ReadConfiguredNestedBooleanWithActiveProfile(
            config,
            ["features", "default_mode_request_user_input"],
            ["experimental_features", "default_mode_request_user_input"]) == true;
    }

    private static string? ResolveConfiguredModelRouteSetId(Dictionary<string, object?> config)
        => TianShu.Configuration.TianShuModelRouteSetDefaults.ResolveActiveRouteSetId(config);

    private static string? ResolveExecutionAgentId(Dictionary<string, object?> config, string executionId)
    {
        if (!TryReadObjectExact(config, "execution_profiles", out var executionProfiles)
            || !TryReadObjectExact(executionProfiles, executionId, out var executionProfile))
        {
            return null;
        }

        return Normalize(ReadStringExact(executionProfile, "agent"));
    }

    private static string? ResolveScopedModelRouteSetId(Dictionary<string, object?> config, string sectionName, string id)
    {
        if (!TryReadObjectExact(config, sectionName, out var section)
            || !TryReadObjectExact(section, id, out var item))
        {
            return null;
        }

        return Normalize(ReadStringExact(item, "model_route_set"));
    }

    private static KernelWindowsSandboxLevel ResolveWindowsSandboxLevel(Dictionary<string, object?> config)
    {
        var explicitMode = Normalize(ReadConfiguredNestedStringWithActiveProfile(
            config,
            ["permissions", "windows_sandbox_mode"],
            ["windows", "sandbox"]));
        if (TryMapWindowsSandboxMode(explicitMode, out var explicitLevel))
        {
            return explicitLevel;
        }

        if (TryResolveLegacyWindowsSandboxLevelFromActiveProfile(config, out var profileLevel))
        {
            return profileLevel;
        }

        return ResolveLegacyWindowsSandboxLevel(config);
    }

    private static bool TryResolveLegacyWindowsSandboxLevelFromActiveProfile(
        Dictionary<string, object?> config,
        out KernelWindowsSandboxLevel level)
    {
        var activeProfile = Normalize(ReadStringExact(config, "profile"));
        if (!string.IsNullOrWhiteSpace(activeProfile)
            && TryReadObjectExact(config, "profiles", out var profiles)
            && TryReadObjectExact(profiles, activeProfile!, out var profileConfig)
            && TryReadObjectExact(profileConfig, "features", out var profileFeatures))
        {
            level = ResolveLegacyWindowsSandboxLevel(profileFeatures);
            return ContainsLegacyWindowsSandboxKeys(profileFeatures);
        }

        level = KernelWindowsSandboxLevel.Disabled;
        return false;
    }

    private static KernelWindowsSandboxLevel ResolveLegacyWindowsSandboxLevel(Dictionary<string, object?> config)
    {
        if (TryReadObjectExact(config, "features", out var features))
        {
            if (ReadBooleanExact(features, "elevated_windows_sandbox") == true)
            {
                return KernelWindowsSandboxLevel.Elevated;
            }

            if (ReadBooleanExact(features, "experimental_windows_sandbox") == true)
            {
                return KernelWindowsSandboxLevel.Unelevated;
            }
        }

        return KernelWindowsSandboxLevel.Disabled;
    }

    private static bool ContainsLegacyWindowsSandboxKeys(Dictionary<string, object?> features)
    {
        return features.ContainsKey("elevated_windows_sandbox")
               || features.ContainsKey("experimental_windows_sandbox");
    }

    private static string? ReadStringExact(Dictionary<string, object?> config, string propertyName)
        => config.TryGetValue(propertyName, out var rawValue)
           && TryReadString(rawValue, out var value)
            ? value
            : null;

    private static bool? ReadBooleanExact(Dictionary<string, object?> config, string propertyName)
        => config.TryGetValue(propertyName, out var rawValue)
           && TryReadBoolean(rawValue, out var value)
            ? value
            : null;

    private static bool TryReadObjectExact(
        Dictionary<string, object?> config,
        string propertyName,
        out Dictionary<string, object?> value)
    {
        if (config.TryGetValue(propertyName, out var rawValue)
            && TryAsConfigDictionaryExact(rawValue, out value))
        {
            return true;
        }

        value = null!;
        return false;
    }

    private static bool TryAsConfigDictionaryExact(object? value, out Dictionary<string, object?> dictionary)
    {
        switch (value)
        {
            case Dictionary<string, object?> concrete:
                dictionary = concrete;
                return true;
            case IReadOnlyDictionary<string, object?> readOnly:
                dictionary = readOnly.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
                return true;
            case IDictionary<string, object?> mutable:
                dictionary = mutable.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.Object:
                dictionary = element.EnumerateObject().ToDictionary(
                    static property => property.Name,
                    static property => JsonSerializer.Deserialize<object?>(property.Value.GetRawText()),
                    StringComparer.Ordinal);
                return true;
            default:
                dictionary = null!;
                return false;
        }
    }

    private static bool TryReadString(object? value, out string text)
    {
        switch (value)
        {
            case string stringValue:
                text = stringValue;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.String:
                text = element.GetString() ?? string.Empty;
                return true;
            default:
                text = string.Empty;
                return false;
        }
    }

    private static bool TryReadBoolean(object? value, out bool booleanValue)
    {
        switch (value)
        {
            case bool native:
                booleanValue = native;
                return true;
            case JsonElement element when element.ValueKind is JsonValueKind.True or JsonValueKind.False:
                booleanValue = element.GetBoolean();
                return true;
            case string text when bool.TryParse(text, out var parsed):
                booleanValue = parsed;
                return true;
            default:
                booleanValue = default;
                return false;
        }
    }

    private static bool TryMapWindowsSandboxMode(string? mode, out KernelWindowsSandboxLevel level)
    {
        switch (Normalize(mode)?.ToLowerInvariant())
        {
            case "elevated":
                level = KernelWindowsSandboxLevel.Elevated;
                return true;
            case "unelevated":
            case "restrictedtoken":
            case "restricted_token":
            case "restricted-token":
                level = KernelWindowsSandboxLevel.Unelevated;
                return true;
            case "disabled":
            case "off":
            case "none":
                level = KernelWindowsSandboxLevel.Disabled;
                return true;
            default:
                level = KernelWindowsSandboxLevel.Disabled;
                return false;
        }
    }

    private static (IReadOnlyList<KernelConversationHistoryItem> SeedHistory, IReadOnlyList<KernelConversationHistoryItem> TailContextHistory) SplitTailContextHistory(
        IEnumerable<KernelConversationHistoryItem> historyItems)
        => KernelTurnExecutionRuntimeHelpers.SplitTailContextHistory(historyItems);

    private static IReadOnlyList<KernelTurnRecord> SelectTurnsForPromptWindow(
        IReadOnlyList<KernelTurnRecord> turns,
        int maxTurns,
        string? currentUserText,
        IReadOnlyList<KernelTurnInputItem>? currentInputItems)
        => KernelTurnExecutionRuntimeHelpers.SelectTurnsForPromptWindow(
            turns,
            maxTurns,
            currentUserText,
            currentInputItems);

    private static bool IsContextCompactionTurn(KernelTurnRecord turn)
    {
        return turn.IsContextCompaction
            || turn.Id.StartsWith("compact_", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Normalize(turn.UserMessage), "上下文压缩摘要", StringComparison.Ordinal);
    }

    private static IReadOnlyList<KernelConversationHistoryItem> EnumerateTurnConversationHistoryItems(KernelTurnRecord turn)
        => KernelTurnExecutionRuntimeHelpers.EnumerateTurnConversationHistoryItems(turn);

    private async Task EnsureAgentMessageStartedAsync(TurnOperationState state)
    {
        if (state.AgentMessageStarted)
        {
            return;
        }

        state.AgentMessageStarted = true;
        await WriteNotificationAsync("item/started", new
        {
            threadId = state.ThreadId,
            turnId = state.TurnId,
            item = new
            {
                id = state.ItemId,
                type = "agentMessage",
                text = string.Empty,
                phase = (string?)null,
            },
        }, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task EnsurePlanItemStartedAsync(TurnOperationState state)
    {
        if (state.PlanItemStarted || state.PlanItemCompleted)
        {
            return;
        }

        state.PlanItemStarted = true;
        await WriteNotificationAsync("item/started", new
        {
            threadId = state.ThreadId,
            turnId = state.TurnId,
            item = new
            {
                id = state.PlanItemId,
                type = "plan",
                text = string.Empty,
            },
        }, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task CompletePlanItemAsync(TurnOperationState state)
    {
        if (state.PlanItemCompleted)
        {
            return;
        }

        if (state.ProposedPlanParser is not null)
        {
            var parsed = state.ProposedPlanParser.Complete();
            if (string.IsNullOrWhiteSpace(state.PlanText))
            {
                state.PlanText = parsed.PlanText;
            }
        }

        if (string.IsNullOrWhiteSpace(state.PlanText))
        {
            return;
        }

        await EnsurePlanItemStartedAsync(state).ConfigureAwait(false);
        state.PlanItemCompleted = true;
        await WriteNotificationAsync("item/completed", new
        {
            threadId = state.ThreadId,
            turnId = state.TurnId,
            item = new
            {
                id = state.PlanItemId,
                type = "plan",
                text = state.PlanText,
            },
        }, CancellationToken.None).ConfigureAwait(false);
    }
    private static async Task HandleImageGenerationOutputItemsAsync(
        IEnumerable<JsonElement> outputItemsDone,
        string? cwd,
        CancellationToken cancellationToken)
    {
        var normalizedCwd = Normalize(cwd);
        foreach (var item in outputItemsDone)
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var type = Normalize(ReadString(item, "type"));
            if (!string.Equals(type, "image_generation_call", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(normalizedCwd))
            {
                continue;
            }

            var callId = Normalize(ReadString(item, "id") ?? ReadString(item, "call_id"));
            var result = Normalize(ReadString(item, "result"));
            if (string.IsNullOrWhiteSpace(callId) || string.IsNullOrWhiteSpace(result))
            {
                continue;
            }

            try
            {
                _ = await KernelToolItemLifecycleHelpers.SaveImageGenerationResultToCwdAsync(
                    normalizedCwd!,
                    callId!,
                    result!,
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    private static Activity? StartTurnActivity(string threadId, string turnId, TurnRequestContext context)
    {
        var activity = new Activity("tianshu.turn");
        activity.SetTag("thread_id", threadId);
        activity.SetTag("turn_id", turnId);
        if (!string.IsNullOrWhiteSpace(context.Model))
        {
            activity.SetTag("model", context.Model);
        }

        return activity.Start();
    }

    private static void ApplyW3cTraceContext(HttpRequestMessage request)
    {
        var activity = Activity.Current;
        if (activity is null)
        {
            return;
        }

        var traceParent = Normalize(activity.Id);
        if (!string.IsNullOrWhiteSpace(traceParent))
        {
            request.Headers.TryAddWithoutValidation("traceparent", traceParent);
        }

        var traceState = Normalize(activity.TraceStateString);
        if (!string.IsNullOrWhiteSpace(traceState))
        {
            request.Headers.TryAddWithoutValidation("tracestate", traceState);
        }
    }

    private async Task<string> CaptureThreadGitDiffAsync(string threadId, CancellationToken cancellationToken)
    {
        var thread = await threadStore.GetThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        var session = thread is null
            ? null
            : (threadManager.TryGetThread(threadId, out var runtimeThread) ? runtimeThread?.Session : null);
        var cwd = Normalize(session?.Cwd)
            ?? Normalize(thread?.Cwd)
            ?? Environment.CurrentDirectory;
        var target = await TryResolveGitDiffTargetAsync(cwd, cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            return string.Empty;
        }

        var diff = await CaptureGitDiffAgainstRemoteReferenceAsync(
            target.Value.RepoRoot,
            target.Value.RemoteRef,
            cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(diff))
        {
            return string.Empty;
        }

        const int maxChars = 24000;
        if (diff.Length > maxChars)
        {
            return diff[..maxChars];
        }

        return diff;
    }

    private async Task<(string RepoRoot, string RemoteRef)?> TryResolveGitDiffTargetAsync(string cwd, CancellationToken cancellationToken)
    {
        const int gitMetadataTimeoutMs = 5000;

        var repoRoot = await TryReadGitCommandOutputAsync(
            ["git", "rev-parse", "--show-toplevel"],
            cwd,
            gitMetadataTimeoutMs,
            cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            return null;
        }

        var upstreamRef = await TryReadGitCommandOutputAsync(
            ["git", "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}"],
            repoRoot,
            gitMetadataTimeoutMs,
            cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(upstreamRef))
        {
            return (repoRoot, upstreamRef);
        }

        var remotes = await ReadGitOutputLinesAsync(
            ["git", "remote"],
            repoRoot,
            gitMetadataTimeoutMs,
            cancellationToken).ConfigureAwait(false);
        if (remotes.Count == 0)
        {
            return null;
        }

        var branches = await ReadGitOutputLinesAsync(
            ["git", "branch", "--format", "%(refname:short)", "--contains", "HEAD"],
            repoRoot,
            gitMetadataTimeoutMs,
            cancellationToken).ConfigureAwait(false);
        if (branches.Count == 0)
        {
            return null;
        }

        string? bestRemoteRef = null;
        var bestDistance = int.MaxValue;
        foreach (var branch in branches)
        {
            foreach (var remote in remotes)
            {
                var remoteRef = $"refs/remotes/{remote}/{branch}";
                var verifyResult = await processExecutionAppHostRuntime.ExecuteCommandAsync(
                    ["git", "rev-parse", "--verify", "--quiet", remoteRef],
                    repoRoot,
                    gitMetadataTimeoutMs,
                    environment: null,
                    cancellationToken).ConfigureAwait(false);
                if (verifyResult.ExitCode != 0 || string.IsNullOrWhiteSpace(Normalize(verifyResult.StdOut)))
                {
                    continue;
                }

                var distanceText = await TryReadGitCommandOutputAsync(
                    ["git", "rev-list", "--count", $"{remoteRef}..HEAD"],
                    repoRoot,
                    gitMetadataTimeoutMs,
                    cancellationToken).ConfigureAwait(false);
                if (!int.TryParse(distanceText, out var distance))
                {
                    continue;
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestRemoteRef = remoteRef;
                }

                break;
            }
        }

        return string.IsNullOrWhiteSpace(bestRemoteRef)
            ? null
            : (repoRoot, bestRemoteRef);
    }

    private async Task<string> CaptureGitDiffAgainstRemoteReferenceAsync(
        string repoRoot,
        string remoteRef,
        CancellationToken cancellationToken)
    {
        const int gitDiffTimeoutMs = 15000;
        var trackedResult = await processExecutionAppHostRuntime.ExecuteCommandAsync(
            ["git", "diff", "--no-textconv", "--no-ext-diff", remoteRef],
            repoRoot,
            gitDiffTimeoutMs,
            environment: null,
            cancellationToken).ConfigureAwait(false);
        if (trackedResult.ExitCode != 0)
        {
            return string.Empty;
        }

        var diff = new StringBuilder(trackedResult.StdOut);
        var untrackedFiles = await ReadGitOutputLinesAsync(
            ["git", "ls-files", "--others", "--exclude-standard"],
            repoRoot,
            gitDiffTimeoutMs,
            cancellationToken).ConfigureAwait(false);
        var nullDevice = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        foreach (var untrackedFile in untrackedFiles)
        {
            var extraResult = await processExecutionAppHostRuntime.ExecuteCommandAsync(
                ["git", "diff", "--no-textconv", "--no-ext-diff", "--binary", "--no-index", "--", nullDevice, untrackedFile],
                repoRoot,
                gitDiffTimeoutMs,
                environment: null,
                cancellationToken).ConfigureAwait(false);
            if (extraResult.ExitCode is 0 or 1)
            {
                diff.Append(extraResult.StdOut);
            }
        }

        return diff.ToString();
    }

    private async Task<IReadOnlyList<string>> ReadGitOutputLinesAsync(
        IReadOnlyList<string> command,
        string cwd,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var output = await TryReadGitCommandOutputAsync(command, cwd, timeoutMs, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(output)
            ? Array.Empty<string>()
            : output
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private async Task<string?> TryReadGitCommandOutputAsync(
        IReadOnlyList<string> command,
        string cwd,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var result = await processExecutionAppHostRuntime.ExecuteCommandAsync(command, cwd, timeoutMs, environment: null, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return null;
        }

        return Normalize(result.StdOut);
    }

    private static IEnumerable<string> SplitIntoChunks(string text, int chunkSize)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        var size = Math.Clamp(chunkSize, 1, 128);
        for (var i = 0; i < text.Length; i += size)
        {
            var length = Math.Min(size, text.Length - i);
            yield return text.Substring(i, length);
        }
    }
    private async Task WriteResultAsync(JsonElement id, object? result, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["id"] = ConvertId(id),
            ["result"] = result,
        };

        await WriteMessageAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteErrorAsync(JsonElement? id, int code, string message, CancellationToken cancellationToken)
        => await WriteErrorAsync(id, code, message, data: null, cancellationToken).ConfigureAwait(false);

    private async Task WriteErrorAsync(
        JsonElement? id,
        int code,
        string message,
        object? data,
        CancellationToken cancellationToken)
    {
        var error = new Dictionary<string, object?>
        {
            ["code"] = code,
            ["message"] = message,
        };
        if (data is not null)
        {
            error["data"] = data;
        }

        var payload = new Dictionary<string, object?>
        {
            ["id"] = id.HasValue ? ConvertId(id.Value) : null,
            ["error"] = error,
        };

        await WriteMessageAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsThreadConfigLoadException(Exception ex)
        => ex is InvalidOperationException or JsonException;

    private KernelResolvedPermissionRuntimeSettings ResolveConfiguredPermissionSettingsWithThreadError(string? cwd)
    {
        try
        {
            return ResolveConfiguredPermissionSettings(cwd);
        }
        catch (Exception ex) when (IsThreadConfigLoadException(ex))
        {
            throw CreateThreadConfigLoadRpcException(ex);
        }
    }

    private KernelJsonRpcException CreateThreadConfigLoadRpcException(Exception ex)
    {
        return new KernelJsonRpcException(
            -32600,
            $"failed to load configuration: {ex.Message}",
            BuildThreadConfigLoadErrorData(ex));
    }

    private object? BuildThreadConfigLoadErrorData(Exception ex)
    {
        object? requirements = null;
        try
        {
            requirements = KernelConfigRequirementsUtilities.BuildConfigRequirementsPayload(
                KernelConfigRequirementsUtilities.LoadMergedConfigRequirements());
        }
        catch
        {
        }

        return new Dictionary<string, object?>
        {
            ["reason"] = "cloudRequirements",
            ["detail"] = ex.Message,
            ["requirements"] = requirements,
        };
    }

    private sealed class KernelJsonRpcException : Exception
    {
        public KernelJsonRpcException(int code, string message, object? dataPayload)
            : base(message)
        {
            Code = code;
            DataPayload = dataPayload;
        }

        public int Code { get; }

        public object? DataPayload { get; }
    }

    private async Task WriteNotificationAsync(string method, object @params, CancellationToken cancellationToken)
    {
        threadHistoryAppHostRuntime.TryTrackTurnNotification(method, @params);
        if (ShouldSuppressNotification(method))
        {
            return;
        }

        await EmitRuntimeNotificationStatsAsync(method, @params, cancellationToken).ConfigureAwait(false);
        await WriteMessageAsync(CreateNotificationPayload(method, @params), cancellationToken).ConfigureAwait(false);
    }

    private async Task EmitRuntimeNotificationStatsAsync(string method, object @params, CancellationToken cancellationToken)
    {
        if (ShouldSkipRuntimeNotificationDiagnostics(method))
        {
            return;
        }

        var payloadElement = JsonSerializer.SerializeToElement(@params, jsonOptions);
        var threadId = ReadString(payloadElement, "threadId") ?? ReadString(payloadElement, "thread_id");
        var turnId = ReadString(payloadElement, "turnId") ?? ReadString(payloadElement, "turn_id");
        var moduleName = DefaultDiagnosticCollectionPolicy.InferModuleName(method);
        var operationCategory = ResolveRuntimeNotificationOperationCategory(method, moduleName);
        var callId = ReadString(payloadElement, "callId") ?? ReadString(payloadElement, "call_id");
        var stats = new RuntimeNotificationStats
        {
            ModuleName = moduleName,
            Method = method,
            ThreadId = threadId,
            TurnId = turnId,
            ItemId = ReadString(payloadElement, "itemId") ?? ReadString(payloadElement, "item_id"),
            CallId = callId,
            RequestId = ReadLong(payloadElement, "requestId") ?? ReadLong(payloadElement, "request_id"),
            OperationCategory = operationCategory,
            ParameterSummary = BuildDiagnosticParameterSummary(payloadElement),
            PermissionDecision = ReadString(payloadElement, "permissionDecision")
                                 ?? ReadString(payloadElement, "permission_decision")
                                 ?? ReadString(payloadElement, "approvalMode")
                                 ?? ReadString(payloadElement, "approval_mode"),
            ExecutionResult = ReadString(payloadElement, "executionResult")
                              ?? ReadString(payloadElement, "execution_result")
                              ?? ReadString(payloadElement, "status")
                              ?? ReadString(payloadElement, "outcome"),
            ArtifactReference = ReadString(payloadElement, "artifact")
                                ?? ReadString(payloadElement, "artifactPath")
                                ?? ReadString(payloadElement, "artifact_path"),
            RiskSource = ReadString(payloadElement, "riskSource")
                         ?? ReadString(payloadElement, "risk_source")
                         ?? ReadString(payloadElement, "approvalKind")
                         ?? ReadString(payloadElement, "approval_kind"),
            PolicyRule = ReadString(payloadElement, "policyRule")
                         ?? ReadString(payloadElement, "policy_rule")
                         ?? ReadString(payloadElement, "ruleId")
                         ?? ReadString(payloadElement, "rule_id"),
            UserDecision = ReadString(payloadElement, "userDecision")
                           ?? ReadString(payloadElement, "user_decision")
                           ?? ReadString(payloadElement, "decision"),
            MemoryAuditId = ReadString(payloadElement, "memoryAuditId")
                            ?? ReadString(payloadElement, "memory_audit_id")
                            ?? ReadString(payloadElement, "auditId")
                            ?? ReadString(payloadElement, "audit_id"),
            PayloadTopLevelKeys = EnumerateNonSensitiveTopLevelKeys(payloadElement),
            SerializedPayloadChars = payloadElement.GetRawText().Length,
            EstimatedPayloadTokens = EstimateDiagnosticTokens(payloadElement.GetRawText().Length),
        };
        await using var operation = diagnosticOperationScopeFactory.BeginOperation(new DiagnosticOperationStart
        {
            OperationName = operationCategory,
            OperationKind = $"runtime.{moduleName}",
            ThreadId = threadId,
            TurnId = turnId,
            Producer = nameof(AppHostServer),
        });
        stats = stats with
        {
            DiagnosticOperationId = operation.Context.OperationId,
        };
        var metadata = new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["diagnosticModule"] = StructuredValue.FromString(moduleName),
            ["summary"] = StructuredValue.FromString(operationCategory),
            ["status"] = StructuredValue.FromString(stats.ExecutionResult ?? "info"),
        });

        await diagnosticEventSink.EmitAsync(
            DiagnosticEventEnvelopeFactory.FromStats(
                    DiagnosticStatisticsEventNames.RuntimeNotificationStats,
                    stats,
                    operation.Context,
                    jsonOptions)
                with
                {
                    Metadata = metadata,
                },
            cancellationToken).ConfigureAwait(false);
        await operation.CompleteAsync(new DiagnosticOperationCompletion(), cancellationToken).ConfigureAwait(false);
    }

    private static string ResolveRuntimeNotificationOperationCategory(string method, string moduleName)
    {
        if (string.Equals(moduleName, DiagnosticModuleNames.Tool, StringComparison.OrdinalIgnoreCase))
        {
            if (method.Contains("requestApproval", StringComparison.OrdinalIgnoreCase)
                || method.Contains("requestUserInput", StringComparison.OrdinalIgnoreCase))
            {
                return "tool_permission_decision";
            }

            if (method.Contains("completed", StringComparison.OrdinalIgnoreCase))
            {
                return "tool_call_completed";
            }

            if (method.Contains("started", StringComparison.OrdinalIgnoreCase)
                || method.Contains("/call", StringComparison.OrdinalIgnoreCase))
            {
                return "tool_call_started";
            }

            return "tool_runtime_event";
        }

        if (string.Equals(moduleName, DiagnosticModuleNames.Governance, StringComparison.OrdinalIgnoreCase))
        {
            if (method.Contains("respond", StringComparison.OrdinalIgnoreCase)
                || method.Contains("decision", StringComparison.OrdinalIgnoreCase))
            {
                return "governance_decision";
            }

            if (method.Contains("approval", StringComparison.OrdinalIgnoreCase))
            {
                return "governance_approval_request";
            }

            if (method.Contains("userInput", StringComparison.OrdinalIgnoreCase)
                || method.Contains("userinputs", StringComparison.OrdinalIgnoreCase))
            {
                return "governance_user_input_request";
            }

            return "governance_event";
        }

        if (string.Equals(moduleName, DiagnosticModuleNames.Memory, StringComparison.OrdinalIgnoreCase))
        {
            if (method.Contains("review", StringComparison.OrdinalIgnoreCase))
            {
                return "memory_review_event";
            }

            if (method.Contains("mutation", StringComparison.OrdinalIgnoreCase)
                || method.Contains("add", StringComparison.OrdinalIgnoreCase)
                || method.Contains("delete", StringComparison.OrdinalIgnoreCase)
                || method.Contains("forget", StringComparison.OrdinalIgnoreCase))
            {
                return "memory_mutation_event";
            }

            return "memory_event";
        }

        return string.Equals(moduleName, DiagnosticModuleNames.Presentation, StringComparison.OrdinalIgnoreCase)
            ? "presentation_event"
            : $"runtime_{moduleName}_event";
    }

    private static string? BuildDiagnosticParameterSummary(JsonElement payloadElement)
    {
        if (payloadElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var parts = new List<string>();
        AddSummaryPart(parts, "tool", ReadString(payloadElement, "toolName") ?? ReadString(payloadElement, "tool_name"));
        AddSummaryPart(parts, "command", ReadString(payloadElement, "command") ?? ReadString(payloadElement, "cmd"));
        AddSummaryPart(parts, "path", ReadString(payloadElement, "path") ?? ReadString(payloadElement, "filePath") ?? ReadString(payloadElement, "file_path"));
        AddSummaryPart(parts, "memorySpace", ReadString(payloadElement, "memorySpaceId") ?? ReadString(payloadElement, "memory_space_id"));
        AddSummaryPart(parts, "record", ReadString(payloadElement, "memoryRecordId") ?? ReadString(payloadElement, "memory_record_id"));
        AddSummaryPart(parts, "approval", ReadString(payloadElement, "approvalKind") ?? ReadString(payloadElement, "approval_kind"));
        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    private static void AddSummaryPart(List<string> parts, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        parts.Add($"{key}={TruncateDiagnosticValue(value)}");
    }

    private static string TruncateDiagnosticValue(string value)
    {
        var normalized = value.Trim();
        return normalized.Length <= 96 ? normalized : normalized[..96] + "...";
    }

    private static bool ShouldSkipRuntimeNotificationDiagnostics(string method)
        => method.StartsWith("diagnostics/", StringComparison.OrdinalIgnoreCase)
           || method.StartsWith("runtime/notification/", StringComparison.OrdinalIgnoreCase)
           || string.Equals(method, DiagnosticStatisticsEventNames.ContextSlicingReport, StringComparison.Ordinal)
           || string.Equals(method, DiagnosticStatisticsEventNames.ProviderRequestContextStats, StringComparison.Ordinal)
           || string.Equals(method, DiagnosticStatisticsEventNames.RuntimeNotificationStats, StringComparison.Ordinal);

    private static DiagnosticCollectionLevel ResolveDiagnosticRequiredLevel(DiagnosticEventEnvelope diagnosticEvent)
    {
        if (string.Equals(diagnosticEvent.EventName, "diagnostics/artifact/written", StringComparison.Ordinal))
        {
            return DiagnosticCollectionLevel.Artifact;
        }

        if (string.Equals(diagnosticEvent.EventName, "diagnostics/operation/completed", StringComparison.Ordinal))
        {
            return DiagnosticCollectionLevel.Verbose;
        }

        if (string.Equals(diagnosticEvent.EventName, "diagnostics/operation/failed", StringComparison.Ordinal))
        {
            return DiagnosticCollectionLevel.Summary;
        }

        return diagnosticEvent.EventName.EndsWith("/notification/stats", StringComparison.OrdinalIgnoreCase)
               || string.Equals(diagnosticEvent.EventName, DiagnosticStatisticsEventNames.RuntimeNotificationStats, StringComparison.Ordinal)
            ? DiagnosticCollectionLevel.Summary
            : DiagnosticCollectionLevel.Stats;
    }

    private static string ResolveDiagnosticModuleName(DiagnosticEventEnvelope diagnosticEvent)
        => diagnosticEvent.Metadata.TryGetValue("diagnosticModule", out var module)
           && !string.IsNullOrWhiteSpace(module.StringValue)
            ? module.StringValue
            : DefaultDiagnosticCollectionPolicy.InferModuleName(diagnosticEvent.EventName);

    private static IReadOnlyList<string> EnumerateNonSensitiveTopLevelKeys(JsonElement payloadElement)
    {
        if (payloadElement.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<string>();
        }

        return payloadElement.EnumerateObject()
            .Select(static property => property.Name)
            .Where(static name => !IsDiagnosticSensitiveKey(name))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsDiagnosticSensitiveKey(string key)
        => key.Contains("authorization", StringComparison.OrdinalIgnoreCase)
           || key.Contains("api_key", StringComparison.OrdinalIgnoreCase)
           || key.Contains("apikey", StringComparison.OrdinalIgnoreCase)
           || key.Contains("token", StringComparison.OrdinalIgnoreCase)
           || key.Contains("secret", StringComparison.OrdinalIgnoreCase)
           || key.Contains("cookie", StringComparison.OrdinalIgnoreCase)
           || key.Contains("password", StringComparison.OrdinalIgnoreCase);

    private static int EstimateDiagnosticTokens(int chars)
        => chars <= 0 ? 0 : Math.Max(1, (int)Math.Ceiling(chars / 3.0d));

    private async Task WriteBroadcastNotificationAsync(string method, object @params, CancellationToken cancellationToken)
    {
        if (ShouldSuppressNotification(method))
        {
            return;
        }

        var payload = CreateNotificationPayload(method, @params);
        var json = SerializeMessage(payload);
        var hub = globalNotificationHub;
        var registration = globalNotificationRegistration;
        if (hub is null || registration is null)
        {
            await WriteSerializedMessageAsync(json, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!registration.IsInitialized)
        {
            await WriteSerializedMessageAsync(json, cancellationToken).ConfigureAwait(false);
            await hub.BroadcastAsync(json, registration.Id, cancellationToken).ConfigureAwait(false);
            return;
        }

        await hub.BroadcastAsync(json, excludedRegistrationId: null, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteMessageAsync(object payload, CancellationToken cancellationToken)
    {
        await WriteSerializedMessageAsync(SerializeMessage(payload), cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteSerializedMessageAsync(string json, CancellationToken cancellationToken)
    {
        var queues = activeQueues;
        if (queues is not null)
        {
            await queues.PublishEventAsync(json, cancellationToken).ConfigureAwait(false);
            return;
        }

        await WriteMessageDirectAsync(json, cancellationToken).ConfigureAwait(false);
    }

    private bool TryPublishGlobalMessage(string json)
    {
        var queues = activeQueues;
        return queues is not null && queues.TryPublishEvent(json);
    }

    private Dictionary<string, object?> CreateNotificationPayload(string method, object @params)
        => new()
        {
            ["method"] = method,
            ["params"] = @params,
        };

    private string SerializeMessage(object payload)
        => JsonSerializer.Serialize(payload, jsonOptions);

    private async Task WriteMessageDirectAsync(string json, CancellationToken cancellationToken)
    {
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
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

    private static object? ConvertId(JsonElement id)
    {
        return id.ValueKind switch
        {
            JsonValueKind.Number when id.TryGetInt64(out var value) => value,
            JsonValueKind.String => id.GetString(),
            _ => id.GetRawText(),
        };
    }

    private void UpdateClientCapabilities(JsonElement @params)
    {
        optedOutNotificationMethods.Clear();
        experimentalApiEnabled = false;
        if (@params.ValueKind != JsonValueKind.Object
            || !@params.TryGetProperty("capabilities", out var capabilities)
            || capabilities.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        experimentalApiEnabled = ReadBool(capabilities, "experimentalApi") == true;

        foreach (var method in KernelToolJsonHelpers.ReadStringArray(capabilities, "optOutNotificationMethods"))
        {
            if (!string.IsNullOrWhiteSpace(method))
            {
                optedOutNotificationMethods.Add(method);
            }
        }
    }

    private static bool TryGetExperimentalCapabilityReason(string method, JsonElement @params, out string reason)
    {
        reason = method switch
        {
            "thread/increment_elicitation" => "thread/increment_elicitation",
            "thread/decrement_elicitation" => "thread/decrement_elicitation",
            "thread/backgroundTerminals/clean" => "thread/backgroundTerminals/clean",
            "thread/realtime/start" => "thread/realtime/start",
            "thread/realtime/appendAudio" => "thread/realtime/appendAudio",
            "thread/realtime/appendText" => "thread/realtime/appendText",
            "thread/realtime/stop" => "thread/realtime/stop",
            "collaborationmode/list" => "collaborationmode/list",
            "thread/start" => GetThreadStartExperimentalReason(@params),
            "thread/resume" => GetThreadResumeExperimentalReason(@params),
            "thread/fork" => GetThreadForkExperimentalReason(@params),
            _ => string.Empty,
        };

        return !string.IsNullOrWhiteSpace(reason);
    }

    private static string GetThreadStartExperimentalReason(JsonElement @params)
    {
        if (HasNonNullProperty(@params, "dynamicTools"))
        {
            return "thread/start.dynamicTools";
        }

        if (HasNonNullProperty(@params, "mockExperimentalField"))
        {
            return "thread/start.mockExperimentalField";
        }

        if (ReadBool(@params, "experimentalRawEvents") == true)
        {
            return "thread/start.experimentalRawEvents";
        }

        if (ReadBool(@params, "persistExtendedHistory") == true)
        {
            return "thread/start.persistFullHistory";
        }

        if (HasGranularApprovalPolicy(@params))
        {
            return "askForApproval.granular";
        }

        return string.Empty;
    }

    private static string GetThreadResumeExperimentalReason(JsonElement @params)
    {
        if (HasNonNullProperty(@params, "history"))
        {
            return "thread/resume.history";
        }

        if (HasNonNullProperty(@params, "path"))
        {
            return "thread/resume.path";
        }

        if (ReadBool(@params, "persistExtendedHistory") == true)
        {
            return "thread/resume.persistFullHistory";
        }

        if (HasGranularApprovalPolicy(@params))
        {
            return "askForApproval.granular";
        }

        return string.Empty;
    }

    private static string GetThreadForkExperimentalReason(JsonElement @params)
    {
        if (HasNonNullProperty(@params, "path"))
        {
            return "thread/fork.path";
        }

        if (ReadBool(@params, "persistExtendedHistory") == true)
        {
            return "thread/fork.persistFullHistory";
        }

        if (HasGranularApprovalPolicy(@params))
        {
            return "askForApproval.granular";
        }

        return string.Empty;
    }

    private static bool HasGranularApprovalPolicy(JsonElement @params)
        => @params.ValueKind == JsonValueKind.Object
           && @params.TryGetProperty("approvalPolicy", out var approvalPolicy)
           && approvalPolicy.ValueKind == JsonValueKind.Object
           && approvalPolicy.TryGetProperty("granular", out var granular)
           && granular.ValueKind == JsonValueKind.Object;

    private static bool HasNonNullProperty(JsonElement json, string propertyName)
        => json.ValueKind == JsonValueKind.Object
           && json.TryGetProperty(propertyName, out var property)
           && property.ValueKind != JsonValueKind.Null;

    private static string BuildInitializeUserAgent(string? clientName)
    {
        var originator = Normalize(Environment.GetEnvironmentVariable(OriginatorOverrideEnvironmentVariable));
        if (string.IsNullOrWhiteSpace(originator))
        {
            originator = Normalize(clientName);
        }

        if (string.IsNullOrWhiteSpace(originator))
        {
            originator = "tianshu-dotnet-kernel";
        }

        return $"{originator}/{CliVersion}";
    }

    private static bool IsValidHttpHeaderValue(string value)
    {
        try
        {
            using var request = new HttpRequestMessage();
            request.Headers.Add("X-TianShu-Client", value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private bool ShouldSuppressNotification(string method)
        => !string.IsNullOrWhiteSpace(method)
           && optedOutNotificationMethods.Contains(method);

    private string NextThreadId()
        => Guid.CreateVersion7().ToString();

    private string NextTurnId()
        => Guid.CreateVersion7().ToString();

    private async Task<string?> ValidateThreadIdAsync(
        JsonElement id,
        string? threadId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await WriteErrorAsync(id, -32602, "threadId 不能为空。", cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (!Guid.TryParse(threadId, out var parsedThreadId))
        {
            await WriteErrorAsync(id, -32600, $"invalid thread id: {threadId}", cancellationToken).ConfigureAwait(false);
            return null;
        }

        return parsedThreadId.ToString();
    }

    private static string? ReadString(JsonElement json, params string[] path)
    {
        var current = json;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
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
            JsonValueKind.Null => null,
            _ => null,
        };
    }

    private static int? ReadInt(JsonElement json, params string[] path)
    {
        var current = json;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.Number when current.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(current.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static long? ReadLong(JsonElement json, params string[] path)
    {
        var current = json;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.Number when current.TryGetInt64(out var value) => value,
            JsonValueKind.String when long.TryParse(current.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement json, params string[] path)
    {
        var value = ReadString(json, path);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    private static bool? ReadBool(JsonElement json, params string[] path)
    {
        var current = json;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
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

    private static bool TryReadObject(JsonElement json, string propertyName, out JsonElement value)
    {
        value = default;
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var candidate))
        {
            return false;
        }

        if (candidate.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        value = candidate;
        return true;
    }

    private static bool TryReadPatchString(JsonElement json, string propertyName, out string? value)
    {
        value = null;
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var element))
        {
            return false;
        }

        value = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Null => null,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null,
        };

        return true;
    }

    private static bool HasProperty(JsonElement json, string propertyName)
        => json.ValueKind == JsonValueKind.Object && json.TryGetProperty(propertyName, out _);

    private static string? TryReadApprovalDecisionObjectType(JsonElement json)
    {
        var typedDecision = Normalize(ReadString(json, "type"));
        if (!string.IsNullOrWhiteSpace(typedDecision))
        {
            return typedDecision;
        }

        foreach (var property in json.EnumerateObject())
        {
            return Normalize(property.Name);
        }

        return null;
    }

    private static KernelConfigOverridePayload? TryReadConfigOverridePayload(JsonElement @params)
    {
        if (!TryReadJsonProperty(@params, "config", out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("config 必须是对象。");
        }

        return KernelConfigOverridePayload.FromElement(value);
    }

    private static bool TryReadThreadSourceKinds(
        JsonElement json,
        string propertyName,
        out IReadOnlyList<KernelThreadSourceKind> sourceKinds,
        out string? error)
    {
        error = null;
        sourceKinds = Array.Empty<KernelThreadSourceKind>();
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var property))
        {
            return true;
        }

        if (property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            error = "sourceKinds 必须是字符串数组。";
            return false;
        }

        var parsed = new List<KernelThreadSourceKind>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                error = "sourceKinds 必须是字符串数组。";
                return false;
            }

            if (!KernelThreadSourceKind.TryParse(item.GetString(), out var kind) || kind is null)
            {
                error = $"不支持的 sourceKind：{item.GetString()}";
                return false;
            }

            if (!parsed.Contains(kind))
            {
                parsed.Add(kind);
            }
        }

        sourceKinds = parsed;
        return true;
    }

    private static bool TryReadOptionalStringArray(
        JsonElement json,
        string propertyName,
        out IReadOnlyList<string>? values,
        out string? error)
    {
        error = null;
        values = null;
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var property))
        {
            return true;
        }

        if (property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            error = $"{propertyName} 必须是字符串数组。";
            return false;
        }

        var parsed = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                error = $"{propertyName} 必须是字符串数组。";
                return false;
            }

            var normalized = Normalize(item.GetString());
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                parsed.Add(normalized!);
            }
        }

        values = parsed;
        return true;
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

    private static string? JoinOptionalInstructionSections(params string?[] sections)
    {
        var normalized = sections
            .Select(Normalize)
            .Where(static section => !string.IsNullOrWhiteSpace(section))
            .Cast<string>()
            .ToArray();
        return normalized.Length == 0
            ? null
            : string.Join(Environment.NewLine + Environment.NewLine, normalized);
    }

    private static string? NormalizeApprovalDecision(string? decision)
    {
        var normalized = Normalize(decision);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized.ToLowerInvariant() switch
        {
            "accept" => "accept",
            "approved" => "accept",
            "approve" => "accept",
            "acceptforsession" => "acceptForSession",
            "accept_for_session" => "acceptForSession",
            "acceptandremember" => "acceptAndRemember",
            "accept_and_remember" => "acceptAndRemember",
            "acceptwithexecpolicyamendment" => "acceptWithExecpolicyAmendment",
            "accept_with_execpolicy_amendment" => "acceptWithExecpolicyAmendment",
            "applynetworkpolicyamendment" => "applyNetworkPolicyAmendment",
            "apply_network_policy_amendment" => "applyNetworkPolicyAmendment",
            "decline" => "decline",
            "denied" => "decline",
            "deny" => "decline",
            "reject" => "decline",
            "rejected" => "decline",
            "cancel" => "cancel",
            _ => normalized,
        };
    }

    private IReadOnlyList<IKernelToolExecutionHook> CreateToolExecutionHooks()
        => KernelToolExecutionAppHostRuntime.CreateDefaultExecutionHooks(WriteNotificationAsync);

    internal async Task<KernelToolResult> ExecuteToolCallAsync(
        string threadId,
        string turnId,
        string itemId,
        string toolName,
        JsonElement arguments,
        TurnRequestContext context,
        KernelReadinessFlag? toolCallGate,
        CancellationToken cancellationToken,
        string? customInput = null,
        bool isCustomToolCall = false,
        string? externalCallId = null)
        => await toolExecutionAppHostRuntime.ExecuteToolCallAsync(
            threadId,
            turnId,
            itemId,
            toolName,
            arguments,
            context,
            toolCallGate,
            cancellationToken,
            customInput,
            isCustomToolCall,
            externalCallId).ConfigureAwait(false);

    private async Task<Dictionary<string, string>> LoadEffectiveConfigValuesAsync(
        CancellationToken cancellationToken,
        string? filePath = null,
        string? cwd = null)
    {
        var effectiveCwd = string.IsNullOrWhiteSpace(cwd)
            ? Environment.CurrentDirectory
            : cwd;
        var mergedValues = await LoadPersistedConfigValuesAsync(cancellationToken, filePath, effectiveCwd).ConfigureAwait(false);
        mergedValues = MergeConfigValueLayers(mergedValues, LoadCliSessionConfigValuesSynchronously(effectiveCwd));
        return mergedValues;
    }

    private Task<string> SaveConfigValuesAsync(
        Dictionary<string, string> values,
        CancellationToken cancellationToken,
        string? filePath = null,
        string? cwd = null)
        => SavePersistedConfigValuesAsync(values, cancellationToken, filePath, cwd);

    private void ApplyCliConfigOverrides(Dictionary<string, string> values, string? cwd)
    {
        var baseDirectory = ResolveCliConfigOverrideBaseDirectory(cwd);
        foreach (var (key, rawValue) in cliConfigOverrides)
        {
            var canonicalKey = CanonicalizePersistedConfigKeyPath(key);
            if (string.IsNullOrWhiteSpace(canonicalKey))
            {
                continue;
            }

            values[canonicalKey] = KernelConfigOverrideUtilities.ConvertRawOverrideToJson(
                KernelConfigOverrideUtilities.RebaseCliConfigOverrideRawValue(canonicalKey, rawValue, baseDirectory));
        }
    }

    private async Task<KernelConfigReadSnapshot> BuildConfigReadSnapshotAsync(
        bool includeLayers,
        string? cwd,
        CancellationToken cancellationToken)
    {
        var processOverrideValues = await LoadProcessConfigOverridesAsync(cwd, cancellationToken).ConfigureAwait(false);
        return BuildConfigReadSnapshot(includeLayers, cwd, processOverrideValues);
    }

    private KernelConfigReadSnapshot BuildConfigReadSnapshotForRuntime(string? cwd, KernelConfigOverridePayload? requestOverrides = null)
    {
        var processOverrideValues = LoadProcessConfigOverridesSynchronously(cwd);
        var snapshot = BuildConfigReadSnapshot(includeLayers: false, cwd, processOverrideValues);
        return ApplyRequestConfigOverrides(snapshot, requestOverrides);
    }

    private KernelConfigReadSnapshot BuildConfigReadSnapshot(
        bool includeLayers,
        string? cwd,
        Dictionary<string, string> processOverrideValues)
    {
        return KernelConfigSnapshotUtilities.BuildConfigReadSnapshot(
            includeLayers,
            cwd,
            processOverrideValues,
            ResolveActiveUserConfigPath());
    }

    private async Task<Dictionary<string, string>> LoadNonProjectConfigValuesAsync(string? cwd, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var userValues = await LoadWritablePersistedConfigValuesAsync(
                cancellationToken,
                filePath: ResolveActiveUserConfigPath())
            .ConfigureAwait(false);
        return MergeConfigValueLayers(userValues, LoadCliSessionConfigValuesSynchronously(cwd));
    }

    private async Task<Dictionary<string, string>> LoadProcessConfigOverridesAsync(string? cwd, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Task.FromResult(LoadProcessConfigOverridesSynchronously(cwd)).ConfigureAwait(false);
    }

    private Dictionary<string, string> LoadProcessConfigOverridesSynchronously(string? cwd)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        ApplyCliConfigOverrides(values, cwd);
        return values;
    }

    private Dictionary<string, string> LoadCliSessionConfigValuesSynchronously(string? cwd)
    {
        return new Dictionary<string, string>(LoadProcessConfigOverridesSynchronously(cwd), StringComparer.Ordinal);
    }

    private string ResolveActiveUserConfigPath()
        => string.IsNullOrWhiteSpace(cliConfigFilePath)
            ? TianShuConfigTomlPathResolver.ResolveUserConfigTomlPath()
            : cliConfigFilePath!;

    private KernelConfigReadSnapshot ApplyRequestConfigOverrides(
        KernelConfigReadSnapshot snapshot,
        KernelConfigOverridePayload? requestOverrides)
    {
        return KernelConfigSnapshotUtilities.ApplyRequestConfigOverrides(
            snapshot,
            requestOverrides?.ToJsonElement());
    }

    private async Task<string> SavePersistedConfigValuesAsync(
        Dictionary<string, string> values,
        CancellationToken cancellationToken,
        string? filePath = null,
        string? cwd = null)
    {
        await configGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = ResolvePersistedConfigTomlPath(filePath, cwd);
            var root = ReadPersistedConfigTable(path);
            ApplyPersistedConfigValues(root, values);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var serialized = Toml.FromModel(root).TrimEnd() + Environment.NewLine;
            await File.WriteAllTextAsync(path, serialized, cancellationToken).ConfigureAwait(false);
            return path;
        }
        finally
        {
            configGate.Release();
        }
    }

    private async Task<Dictionary<string, string>> LoadPersistedConfigValuesAsync(
        CancellationToken cancellationToken,
        string? filePath = null,
        string? cwd = null)
    {
        await configGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                return ReadPersistedConfigValues(Path.GetFullPath(filePath));
            }

            return ReadMergedPersistedConfigValues(cwd);
        }
        finally
        {
            configGate.Release();
        }
    }

    private async Task<Dictionary<string, string>> LoadWritablePersistedConfigValuesAsync(
        CancellationToken cancellationToken,
        string? filePath = null,
        string? cwd = null)
    {
        await configGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = ResolvePersistedConfigTomlPath(filePath, cwd);
            return ReadPersistedConfigValues(path);
        }
        finally
        {
            configGate.Release();
        }
    }

    private Dictionary<string, string> LoadPersistedConfigValuesSynchronously(string? filePath = null, string? cwd = null)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            return ReadPersistedConfigValues(Path.GetFullPath(filePath));
        }

        return ReadMergedPersistedConfigValues(cwd);
    }

    private async Task<string?> LoadMergedPersistedConfigTextAsync(
        CancellationToken cancellationToken,
        string? cwd = null)
    {
        await configGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return ReadMergedPersistedConfigText(cwd);
        }
        finally
        {
            configGate.Release();
        }
    }

    private string? ReadMergedPersistedConfigText(string? cwd = null)
    {
        return KernelConfigPersistenceOrchestrationUtilities.ReadMergedPersistedConfigText(
            cwd,
            ResolveActiveUserConfigPath(),
            LoadProcessConfigOverridesSynchronously(cwd),
            ApplyPersistedConfigValues);
    }

    private Dictionary<string, string> ReadMergedPersistedConfigValues(string? cwd)
    {
        return KernelConfigPersistenceOrchestrationUtilities.ReadMergedPersistedConfigValues(
            cwd,
            ResolveActiveUserConfigPath(),
            MergePersistedConfigValues);
    }

    private TomlTable ReadMergedPersistedConfigTable(string? cwd)
    {
        return KernelConfigPersistenceOrchestrationUtilities.ReadMergedPersistedConfigTable(
            cwd,
            ResolveActiveUserConfigPath());
    }

    private IReadOnlyList<string> ResolveSpawnAgentNicknameCandidates(KernelSpawnAgentRoleDefinition? role)
    {
        if (role?.NicknameCandidates is { Count: > 0 } configuredCandidates)
        {
            return configuredCandidates
                .Select(Normalize)
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToArray();
        }

        return DefaultSpawnAgentNicknameCandidates;
    }

    private string ReserveSpawnAgentNickname(IReadOnlyList<string> candidateNames, string? preferred = null)
    {
        lock (agentNicknameGate)
        {
            var normalizedPreferred = Normalize(preferred);
            string nickname;
            if (!string.IsNullOrWhiteSpace(normalizedPreferred))
            {
                nickname = normalizedPreferred!;
            }
            else
            {
                if (candidateNames.Count == 0)
                {
                    throw new InvalidOperationException("no available agent nicknames");
                }

                var availableNames = candidateNames
                    .Select(name => FormatSpawnAgentNickname(name, agentNicknameResetCount))
                    .Where(name => !usedAgentNicknames.Contains(name))
                    .ToArray();
                if (availableNames.Length > 0)
                {
                    nickname = availableNames[Random.Shared.Next(availableNames.Length)];
                }
                else
                {
                    usedAgentNicknames.Clear();
                    agentNicknameResetCount++;
                    nickname = FormatSpawnAgentNickname(
                        candidateNames[Random.Shared.Next(candidateNames.Count)],
                        agentNicknameResetCount);
                }
            }

            usedAgentNicknames.Add(nickname);
            return nickname;
        }
    }

    private void RegisterSpawnAgentNickname(string threadId, string? nickname)
    {
        var normalizedThreadId = Normalize(threadId);
        var normalizedNickname = Normalize(nickname);
        if (string.IsNullOrWhiteSpace(normalizedThreadId) || string.IsNullOrWhiteSpace(normalizedNickname))
        {
            return;
        }

        lock (agentNicknameGate)
        {
            usedAgentNicknames.Add(normalizedNickname!);
            threadAgentNicknames[normalizedThreadId!] = normalizedNickname!;
        }
    }

    private void ReleaseSpawnAgentNicknameReservation(string threadId)
    {
        var normalizedThreadId = Normalize(threadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            return;
        }

        lock (agentNicknameGate)
        {
            threadAgentNicknames.Remove(normalizedThreadId!);
        }
    }

    private static string FormatSpawnAgentNickname(string name, int nicknameResetCount)
    {
        return nicknameResetCount switch
        {
            0 => name,
            _ => $"{name} the {FormatOrdinal(nicknameResetCount + 1)}",
        };
    }

    private static string FormatOrdinal(int value)
    {
        var suffix = (value % 100) switch
        {
            >= 11 and <= 13 => "th",
            _ => (value % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th",
            },
        };
        return $"{value}{suffix}";
    }

    private async Task<KernelSpawnAgentRoleDefinition> ResolveSpawnAgentRoleAsync(
        string? cwd,
        string? requestedAgentType,
        CancellationToken cancellationToken)
    {
        var normalizedAgentType = Normalize(requestedAgentType) ?? DefaultSpawnAgentRoleName;

        var effectiveCwd = Normalize(cwd) ?? Environment.CurrentDirectory;
        var snapshot = await BuildConfigReadSnapshotAsync(includeLayers: false, effectiveCwd, cancellationToken).ConfigureAwait(false);
        var roles = KernelSpawnAgentRoleConfigurationUtilities.ResolveSpawnAgentRoleDefinitions(snapshot, effectiveCwd);
        if (roles.TryGetValue(normalizedAgentType, out var role))
        {
            return role;
        }

        throw new InvalidOperationException($"unknown agent_type '{normalizedAgentType}'");
    }

    private async Task<KernelSpawnAgentRoleOverrides> LoadSpawnAgentRoleOverridesAsync(
        KernelSpawnAgentRoleDefinition role,
        CancellationToken cancellationToken)
    {
        return await KernelSpawnAgentRoleConfigurationUtilities
            .LoadSpawnAgentRoleOverridesAsync(role, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<string> BuildSpawnAgentTypeDescriptionAsync(
        string? cwd,
        CancellationToken cancellationToken)
    {
        var effectiveCwd = Normalize(cwd) ?? Environment.CurrentDirectory;
        var snapshot = await BuildConfigReadSnapshotAsync(includeLayers: false, effectiveCwd, cancellationToken).ConfigureAwait(false);
        return await KernelSpawnAgentRoleConfigurationUtilities
            .BuildSpawnAgentTypeDescriptionAsync(snapshot, effectiveCwd, cancellationToken)
            .ConfigureAwait(false);
    }

    private KernelResolvedPermissionRuntimeSettings ResolveConfiguredPermissionSettings(string? cwd)
    {
        var effectiveCwd = Normalize(cwd) ?? Environment.CurrentDirectory;
        var snapshot = BuildConfigReadSnapshotForRuntime(effectiveCwd);
        return ResolveConfiguredPermissionSettings(snapshot, effectiveCwd);
    }

    private KernelResolvedPermissionRuntimeSettings ResolveConfiguredPermissionSettings(
        KernelConfigReadSnapshot snapshot,
        string? cwd)
    {
        var effectiveCwd = Normalize(cwd) ?? Environment.CurrentDirectory;
        var resolvedConfiguration = KernelPermissionProfileResolver.ResolveConfiguredPermissionConfiguration(
            snapshot,
            effectiveCwd,
            KernelApprovalPolicyHelpers.ToPayloadValue(DefaultApprovalPolicy),
            ResolveTianShuHomePath(),
            policyStrategyPackage.PermissionDefaults);
        return KernelPermissionRuntimeAdapter.CreateResolvedPermissionSettings(
            resolvedConfiguration.ApprovalPolicyValue,
            resolvedConfiguration.SandboxPolicy,
            resolvedConfiguration.SandboxMode,
            resolvedConfiguration.AllowLoginShell,
            ToRuntimeShellEnvironmentPolicySettings(resolvedConfiguration.ShellEnvironmentPolicy),
            DefaultApprovalPolicy);
    }

    private static KernelRuntimeConfiguredShellEnvironmentPolicySettings ToRuntimeShellEnvironmentPolicySettings(
        KernelConfiguredShellEnvironmentPolicySettings settings)
    {
        return new KernelRuntimeConfiguredShellEnvironmentPolicySettings(
            settings.Inherit switch
            {
                KernelConfiguredShellEnvironmentPolicyInherit.Core => KernelRuntimeConfiguredShellEnvironmentPolicyInherit.Core,
                KernelConfiguredShellEnvironmentPolicyInherit.None => KernelRuntimeConfiguredShellEnvironmentPolicyInherit.None,
                _ => KernelRuntimeConfiguredShellEnvironmentPolicyInherit.All,
            },
            settings.IgnoreDefaultExcludes,
            settings.ExcludePatterns,
            settings.SetVariables,
            settings.IncludeOnlyPatterns,
            settings.UseProfile);
    }

    private sealed record ConfiguredThreadDefaults(
        KernelThreadConfigSnapshot ConfigSnapshot,
        KernelResolvedPermissionRuntimeSettings Permissions,
        Dictionary<string, object?> RawConfig);
}
