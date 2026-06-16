using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;
using System.Globalization;
using TianShu.Contracts.Agents;
using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Catalog;
using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Environment;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Governance;
using TianShu.Contracts.Host;
using TianShu.Contracts.Identity;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Projections;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Sessions;
using TianShu.Contracts.Tools;
using TianShu.Contracts.Workflows;
using TianShu.AppHost.Configuration;
using TianShu.Configuration;
using TianShu.ControlPlane;
using TianShu.Execution.Runtime;
using TianShu.Execution.Runtime.Events;
using TianShu.Cli.Terminal;
using TianShu.Contracts.Conversations;
using TianShu.RuntimeComposition;
using ControlPlaneCreateJobCommand = TianShu.Contracts.Workflows.ControlPlaneCreateJobCommand;
using ControlPlaneDispatchJobCommand = TianShu.Contracts.Workflows.ControlPlaneDispatchJobCommand;
using ControlPlaneJobOperationResult = TianShu.Contracts.Workflows.ControlPlaneJobOperationResult;
using ControlPlaneReportJobItemCommand = TianShu.Contracts.Workflows.ControlPlaneReportJobItemCommand;
using ControlPlaneReadJobQuery = TianShu.Contracts.Workflows.ControlPlaneReadJobQuery;
using TianShu.ControlPlane.Abstractions;
using TianShu.ControlPlane.Abstractions.Conversations;
using TianShu.ControlPlane.Abstractions.Governance;
using Task = System.Threading.Tasks.Task;

namespace TianShu.Cli;

internal sealed class CliRuntimeCommandRunner
{
    internal readonly record struct FormalRpcDispatchResult(bool Handled, object? Result);

    private readonly Func<IExecutionRuntime> runtimeFactory;
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions TypedPayloadJsonOptions = CreateTypedPayloadJsonOptions();
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
    private sealed class ThreadOperationCliResult
    {
        public CliThreadDetails? Thread { get; init; }
    }

    private sealed class ThreadLoadedListCliResult
    {
        public IReadOnlyList<string> Data { get; init; } = Array.Empty<string>();

        public string? NextCursor { get; init; }
    }

    private sealed class ThreadUnsubscribeCliResult
    {
        public string Status { get; init; } = string.Empty;
    }

    private sealed class ThreadElicitationCliResult
    {
        public ulong Count { get; init; }

        public bool Paused { get; init; }
    }

    private sealed class ThreadCommandAcceptedCliResult
    {
    }

    private sealed record McpDisplayEntry(
        string Name,
        McpDisplayConfig? Config,
        string AuthStatus);

    private sealed record McpDisplayConfig(
        string? Command,
        IReadOnlyList<string>? Args,
        IReadOnlyDictionary<string, string>? Env,
        IReadOnlyList<string>? EnvVars,
        string? Cwd,
        IReadOnlyDictionary<string, string>? HttpHeaders,
        IReadOnlyDictionary<string, string>? EnvHttpHeaders,
        string? Url,
        string? BearerTokenEnvVar,
        double? StartupTimeoutSec,
        double? ToolTimeoutSec,
        bool? Enabled,
        string? DisabledReason,
        IReadOnlyList<string>? EnabledTools,
        IReadOnlyList<string>? DisabledTools);

    private sealed record ThreadResumePendingFollowUp(
        IReadOnlyList<ControlPlaneInputItem> Inputs,
        ControlPlaneFollowUpMode Mode,
        string CorrelationId,
        string PreviewText,
        string PendingBucket);

    private sealed record DebugClearMemoriesCliResult(
        bool UsedRuntime,
        bool StateDbExists,
        string StateDbPath,
        string MemoryRoot,
        bool MemoryRootRemoved,
        string Message);

    private sealed record WorkflowPlanStepInput(
        int? Order,
        string? Title,
        string? Description);

    internal CliRuntimeCommandRunner()
        : this(TianShuAppHostRuntimeClientFactory.Create)
    {
    }

    internal CliRuntimeCommandRunner(Func<IExecutionRuntime> runtimeFactory)
    {
        this.runtimeFactory = runtimeFactory ?? throw new ArgumentNullException(nameof(runtimeFactory));
    }

    public async Task<int> RunRpcAsync(RpcCommandOptions options, CancellationToken cancellationToken)
    {
        var bootstrap = CliRuntimeBootstrapper.Prepare(options);
        await using var runtime = runtimeFactory();
        await runtime.InitializeAsync(bootstrap.RuntimeOptions, dynamicToolCallHandler: null, cancellationToken).ConfigureAwait(false);

        var parameters = ParseParamsJson(options.ParamsJson);
        var formalDispatch = await TryInvokeFormalRpcAsync(runtime, options.Method, parameters, cancellationToken).ConfigureAwait(false);
        if (formalDispatch.Handled)
        {
            Console.WriteLine(JsonSerializer.Serialize(formalDispatch.Result, jsonOptions));
            return 0;
        }

        throw new InvalidOperationException(BuildFormalRpcUnavailableMessage(options.Method));
    }

    public int RunModelRouteDiagnostic(ModelRouteDiagnosticCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var config = new RuntimeConfigurationComposition().Load(
            options.ConfigFilePath,
            options.ProfileName,
            options.ConfigOverrides,
            options.WorkingDirectory);
        var diagnostic = ModelRouteRuntimeComposition.BuildRouteDiagnostic(
            config.RawConfig,
            options.RouteKind,
            options.RouteSetId);
        var hostProjection = CreateModelRouteHostProjection(diagnostic);

        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(hostProjection.Projection!.ToPlainObject(), jsonOptions));
            return 0;
        }

        Console.WriteLine($"routeSet\t{diagnostic.RouteSetId}\tvirtual={FormatBool(diagnostic.RouteSetIsVirtual)}");
        Console.WriteLine($"routeRegistry\tregistered={diagnostic.ConfiguredRegisteredRouteKinds.Count}/{diagnostic.RegisteredRouteKinds.Count}\tmissing={FormatRouteKinds(diagnostic.MissingRegisteredRouteKinds)}\tunknown={FormatRouteKinds(diagnostic.UnknownRouteKinds)}");
        Console.WriteLine($"route\trequested={diagnostic.RequestedRouteKind}\tresolved={diagnostic.ResolvedRouteKind ?? "-"}");
        if (!string.IsNullOrWhiteSpace(diagnostic.RouteFallbackReason))
        {
            Console.WriteLine($"route_reason\t{diagnostic.RouteFallbackReason}");
        }

        Console.WriteLine("priority\tprovider\tmodel\tprotocol\tunavailableReason");
        foreach (var candidate in diagnostic.Candidates)
        {
            Console.WriteLine(
                $"{candidate.CandidateIndex + 1}\t{candidate.Provider}\t{candidate.Model}\t{candidate.Protocol ?? "auto"}\t{candidate.UnavailableReason ?? "-"}");
        }

        if (diagnostic.PreferredCandidate is null)
        {
            Console.WriteLine("未找到可展示的 route candidate。");
        }

        return 0;
    }

    private static HostOperationResult CreateModelRouteHostProjection(TianShuModelRouteDiagnostic diagnostic)
        => new(
            "model-route-diagnostic",
            HostOperationStatus.Completed,
            StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["projectionKind"] = "host.model_route.diagnostic",
                ["routeSetId"] = diagnostic.RouteSetId,
                ["routeSetIsVirtual"] = diagnostic.RouteSetIsVirtual,
                ["requestedRouteKind"] = diagnostic.RequestedRouteKind,
                ["resolvedRouteKind"] = diagnostic.ResolvedRouteKind,
                ["routeFallbackReason"] = diagnostic.RouteFallbackReason,
                ["registeredRouteKinds"] = diagnostic.RegisteredRouteKinds,
                ["configuredRegisteredRouteKinds"] = diagnostic.ConfiguredRegisteredRouteKinds,
                ["missingRegisteredRouteKinds"] = diagnostic.MissingRegisteredRouteKinds,
                ["unknownRouteKinds"] = diagnostic.UnknownRouteKinds,
                ["preferredCandidate"] = ToProjectionCandidate(diagnostic.PreferredCandidate),
                ["fallbackCandidates"] = diagnostic.FallbackCandidates.Select(ToProjectionCandidate).ToArray(),
                ["candidates"] = diagnostic.Candidates.Select(ToProjectionCandidate).ToArray(),
            }),
            message: "Host Gateway model route projection.");

    private static Dictionary<string, object?>? ToProjectionCandidate(TianShuModelRouteCandidateSnapshot? candidate)
        => candidate is null
            ? null
            : new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["provider"] = candidate.Provider,
                ["model"] = candidate.Model,
                ["protocol"] = candidate.Protocol,
                ["capabilities"] = candidate.Capabilities,
                ["candidateIndex"] = candidate.CandidateIndex,
                ["unavailableReason"] = candidate.UnavailableReason,
            };

    private static string FormatRouteKinds(IReadOnlyList<string> routeKinds)
        => routeKinds.Count == 0 ? "-" : string.Join(",", routeKinds);

    public async Task<int> RunDebugAsync(DebugCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.CommandKind switch
        {
            DebugCommandKind.ClearMemories => await RunDebugClearMemoriesAsync(options, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"不支持的 debug 命令：{options.CommandKind}"),
        };
    }

    internal static string BuildFormalRpcUnavailableMessage(string? method)
        => $"RPC 方法未映射到正式 runtime surface / control-plane：{Normalize(method) ?? "<null>"}。";

    private async Task<int> RunDebugClearMemoriesAsync(DebugCommandOptions options, CancellationToken cancellationToken)
    {
        var stateDbPath = ResolveKernelStateDbPath();
        var memoryRoot = ResolveMemoryRootPath();
        if (!File.Exists(stateDbPath))
        {
            var memoryRootRemoved = RemoveMemoryRoot(memoryRoot);
            var message = memoryRootRemoved
                ? $"No state db found at {stateDbPath}. Removed {memoryRoot}."
                : $"No state db found at {stateDbPath}. No memory directory found at {memoryRoot}.";
            return WriteDebugClearMemoriesCliResult(
                options,
                new DebugClearMemoriesCliResult(
                    UsedRuntime: false,
                    StateDbExists: false,
                    StateDbPath: stateDbPath,
                    MemoryRoot: memoryRoot,
                    MemoryRootRemoved: memoryRootRemoved,
                    Message: message));
        }

        var bootstrap = CliRuntimeBootstrapper.Prepare(options);
        await using var runtime = runtimeFactory();
        await runtime.InitializeAsync(bootstrap.RuntimeOptions, dynamicToolCallHandler: null, cancellationToken).ConfigureAwait(false);

        var result = await TianShuControlPlaneClientFactory.Create(runtime)
            .Diagnostics
            .ClearDebugMemoriesAsync(cancellationToken)
            .ConfigureAwait(false);
        return WriteDebugClearMemoriesResult(options, result);
    }

    private int WriteDebugClearMemoriesCliResult(DebugCommandOptions options, DebugClearMemoriesCliResult result)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
            return 0;
        }

        Console.WriteLine(result.Message);
        return 0;
    }

    private int WriteDebugClearMemoriesResult(DebugCommandOptions options, ControlPlaneDebugClearMemoriesResult result)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                usedRuntime = true,
                stateDbExists = true,
                stateDbPath = result.StateDbPath,
                clearedStage1OutputCount = result.ClearedStage1OutputCount,
                disabledThreadCount = result.DisabledThreadCount,
                memoryRoot = result.MemoryRootPath,
                memoryRootPath = result.MemoryRootPath,
                memoryRootRemoved = result.RemovedMemoryRoot,
                removedMemoryRoot = result.RemovedMemoryRoot,
                message = BuildDebugClearMemoriesMessage(
                    result.StateDbPath,
                    result.MemoryRootPath,
                    result.RemovedMemoryRoot,
                    clearedStateDb: true),
            }, jsonOptions));
            return 0;
        }

        Console.WriteLine(BuildDebugClearMemoriesMessage(
            result.StateDbPath,
            result.MemoryRootPath,
            result.RemovedMemoryRoot,
            clearedStateDb: true));
        return 0;
    }

    private static string ResolveKernelStateDbPath()
        => Path.Combine(ResolveKernelRootPath(), "state.db");

    private static string ResolveKernelRootPath()
        => TianShu.AppHost.Configuration.TianShuHomePathUtilities.ResolveTianShuStateRootPath();

    private static string ResolveMemoryRootPath()
        => TianShu.AppHost.Configuration.TianShuHomePathUtilities.ResolveDataPathFromHome(
            TianShu.AppHost.Configuration.TianShuHomePathUtilities.ResolveTianShuHomePath(),
            "memory");

    private static string? NormalizePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Path.GetFullPath(value.Trim());
    }

    private static bool RemoveMemoryRoot(string memoryRoot)
    {
        if (Directory.Exists(memoryRoot))
        {
            Directory.Delete(memoryRoot, recursive: true);
            return true;
        }

        if (File.Exists(memoryRoot))
        {
            File.Delete(memoryRoot);
            return true;
        }

        return false;
    }

    private static string BuildDebugClearMemoriesMessage(
        string stateDbPath,
        string memoryRootPath,
        bool removedMemoryRoot,
        bool clearedStateDb)
    {
        var message = clearedStateDb
            ? $"Cleared memory state from {stateDbPath}."
            : $"No state db found at {stateDbPath}.";

        if (removedMemoryRoot)
        {
            message += $" Removed {memoryRootPath}.";
        }
        else
        {
            message += $" No memory directory found at {memoryRootPath}.";
        }

        return message;
    }

    public async Task<int> RunRuntimeSurfaceAsync(RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var bootstrap = CliRuntimeBootstrapper.Prepare(options);
        await using var runtime = runtimeFactory();
        await runtime.InitializeAsync(bootstrap.RuntimeOptions, dynamicToolCallHandler: null, cancellationToken).ConfigureAwait(false);
        var controlPlane = TianShuControlPlaneClientFactory.Create(runtime);

        if (options.CommandKind == RuntimeSurfaceCommandKind.McpServerOauthLogin)
        {
            return await RunMcpServerOauthLoginAsync(controlPlane, runtime, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.ConfigValueWrite)
        {
            return await RunConfigValueWriteAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.ConfigBatchWrite)
        {
            return await RunConfigBatchWriteAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.ConfigRequirementsRead)
        {
            return await RunConfigRequirementsReadAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.ConfigRead)
        {
            return await RunConfigReadAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.ConversationThread)
        {
            return await RunConversationThreadAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.SessionSnapshot)
        {
            return await RunSessionSnapshotAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.SessionOverview)
        {
            return await RunSessionOverviewAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.SessionList)
        {
            return await RunSessionListAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.GovernanceApprovalQueue)
        {
            return await RunGovernanceApprovalQueueAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.GovernanceUserInputList)
        {
            return await RunGovernanceUserInputListAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.CollaborationCreate)
        {
            return await RunCollaborationCreateAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.CollaborationConfigure)
        {
            return await RunCollaborationConfigureAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.CollaborationArchive)
        {
            return await RunCollaborationArchiveAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.CollaborationOverview)
        {
            return await RunCollaborationOverviewAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.CollaborationSpace)
        {
            return await RunCollaborationSpaceAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.CollaborationList)
        {
            return await RunCollaborationListAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.ParticipantBindSession)
        {
            return await RunParticipantBindSessionAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.ParticipantBindWorkflow)
        {
            return await RunParticipantBindWorkflowAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.ParticipantUpdateRole)
        {
            return await RunParticipantUpdateRoleAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.ParticipantRead)
        {
            return await RunParticipantReadAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.ParticipantView)
        {
            return await RunParticipantViewAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.ParticipantList)
        {
            return await RunParticipantListAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.ArtifactRead)
        {
            return await RunArtifactReadAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.ArtifactList)
        {
            return await RunArtifactListAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.WorkflowCreate)
        {
            return await RunWorkflowCreateAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.WorkflowPublishPlan)
        {
            return await RunWorkflowPublishPlanAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.WorkflowCreateTask)
        {
            return await RunWorkflowCreateTaskAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.WorkflowUpdateTaskState)
        {
            return await RunWorkflowUpdateTaskStateAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.WorkflowBoard)
        {
            return await RunWorkflowBoardAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.WorkflowTaskBoard)
        {
            return await RunWorkflowTaskBoardAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.WorkflowPlan)
        {
            return await RunWorkflowPlanAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.AgentList)
        {
            return await RunAgentListAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.AgentRoster)
        {
            return await RunAgentRosterAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.AgentTeam)
        {
            return await RunAgentTeamAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.AgentThreadRegister)
        {
            return await RunAgentThreadRegisterAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.AgentJobCreate)
        {
            return await RunAgentJobCreateAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.AgentJobDispatch)
        {
            return await RunAgentJobDispatchAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.AgentJobItemReport)
        {
            return await RunAgentJobItemReportAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.AgentJobRead)
        {
            return await RunAgentJobReadAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.IdentityAccount)
        {
            return await RunIdentityAccountAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.IdentityDevices)
        {
            return await RunIdentityDevicesAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.MemoryProviders)
        {
            return await RunMemoryProvidersAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.MemorySpaces)
        {
            return await RunMemorySpacesAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.MemoryOverlay)
        {
            return await RunMemoryOverlayAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.MemoryFilter)
        {
            return await RunMemoryFilterAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.MemoryAdd)
        {
            return await RunMemoryAddAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.MemoryExtract)
        {
            return await RunMemoryExtractAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.MemoryImport)
        {
            return await RunMemoryImportAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.MemoryExport)
        {
            return await RunMemoryExportAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.MemoryBindProvider)
        {
            return await RunMemoryBindProviderAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.MemoryConsolidate)
        {
            return await RunMemoryConsolidateAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.MemoryForget)
        {
            return await RunMemoryForgetAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.MemoryDelete)
        {
            return await RunMemoryDeleteAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.MemorySupersede)
        {
            return await RunMemorySupersedeAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.MemoryReviewList)
        {
            return await RunMemoryReviewListAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.MemoryReviewApprove)
        {
            return await RunMemoryReviewApproveAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.MemoryReviewDemote)
        {
            return await RunMemoryReviewDemoteAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.MemoryReviewMerge)
        {
            return await RunMemoryReviewMergeAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.MemoryReviewRestore)
        {
            return await RunMemoryReviewRestoreAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.MemoryFeedback)
        {
            return await RunMemoryFeedbackAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.MemoryCitation)
        {
            return await RunMemoryCitationAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.DiagnosticsTrace)
        {
            return await RunDiagnosticsTraceAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.DiagnosticsAttemptList)
        {
            return await RunDiagnosticsAttemptListAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.ModelList)
        {
            return await RunModelListAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.ModelCatalog)
        {
            return await RunModelCatalogAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.ModelResolve)
        {
            return await RunModelResolveAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.ToolCatalog)
        {
            return await RunToolCatalogAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.ToolConfigExport)
        {
            return await RunToolConfigExportAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.AppList)
        {
            return await RunAppListAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.SkillsList)
        {
            return await RunSkillsListAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.SkillsConfigWrite)
        {
            return await RunSkillsConfigWriteAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.SkillsRemoteList)
        {
            return await RunSkillsRemoteListAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.SkillsRemoteExport)
        {
            return await RunSkillsRemoteExportAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.PluginList)
        {
            return await RunPluginListAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.PluginRead)
        {
            return await RunPluginReadAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.PluginInstall)
        {
            return await RunPluginInstallAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.PluginUninstall)
        {
            return await RunPluginUninstallAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.ReviewStart)
        {
            return await RunReviewStartAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.FeatureList)
        {
            return await RunFeatureListAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.FeatureConfigWrite)
        {
            return await RunFeatureConfigWriteAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.ExperimentalFeatureList)
        {
            return await RunExperimentalFeatureListAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.CollaborationModeList)
        {
            return await RunCollaborationModeListAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.McpServerStatusList)
        {
            return await RunMcpServerStatusListAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.McpServerReload)
        {
            return await RunMcpServerReloadAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.ConversationSummary)
        {
            return await RunConversationSummaryAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.CommandKind == RuntimeSurfaceCommandKind.GitDiffToRemote)
        {
            return await RunGitDiffToRemoteAsync(controlPlane, options, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException($"不支持的 runtime surface 命令：{options.CommandKind}");
    }

    public async Task<int> RunMcpAsync(McpCommandOptions options, CancellationToken cancellationToken)
    {
        var bootstrap = CliRuntimeBootstrapper.Prepare(options);
        await using var runtime = runtimeFactory();
        await runtime.InitializeAsync(bootstrap.RuntimeOptions, dynamicToolCallHandler: null, cancellationToken).ConfigureAwait(false);
        var controlPlane = TianShuControlPlaneClientFactory.Create(runtime);

        return options.CommandKind switch
        {
            McpCommandKind.List => await RunMcpListAsync(controlPlane, options, cancellationToken).ConfigureAwait(false),
            McpCommandKind.Get => await RunMcpGetAsync(controlPlane, options, cancellationToken).ConfigureAwait(false),
            McpCommandKind.Add => await RunMcpAddAsync(controlPlane, options, cancellationToken).ConfigureAwait(false),
            McpCommandKind.Remove => await RunMcpRemoveAsync(controlPlane, options, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"不支持的 MCP 命令：{options.CommandKind}"),
        };
    }

    private async Task<int> RunMcpListAsync(ITianShuControlPlane controlPlane, McpCommandOptions options, CancellationToken cancellationToken)
    {
        var snapshot = await controlPlane.Catalog.ReadConfigAsync(
                new ControlPlaneConfigReadQuery
                {
                    WorkingDirectory = null,
                },
                cancellationToken)
            .ConfigureAwait(false);
        var statusMap = await ReadAllMcpStatusesAsync(controlPlane, cancellationToken).ConfigureAwait(false);
        var entries = BuildMcpDisplayEntries(ReadConfiguredMcpServers(snapshot.Config), statusMap);

        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(entries.Select(ToMcpListJsonObject), jsonOptions));
            return 0;
        }

        if (entries.Count == 0)
        {
            Console.WriteLine("No MCP servers configured yet. Try `mcp add my-tool -- my-command`.");
            return 0;
        }

        WriteMcpListTable(entries);
        return 0;
    }

    private async Task<int> RunMcpGetAsync(ITianShuControlPlane controlPlane, McpCommandOptions options, CancellationToken cancellationToken)
    {
        var snapshot = await controlPlane.Catalog.ReadConfigAsync(
                new ControlPlaneConfigReadQuery
                {
                    WorkingDirectory = null,
                },
                cancellationToken)
            .ConfigureAwait(false);
        var statusMap = await ReadAllMcpStatusesAsync(controlPlane, cancellationToken).ConfigureAwait(false);
        var entries = BuildMcpDisplayEntries(ReadConfiguredMcpServers(snapshot.Config), statusMap);
        var entry = entries.FirstOrDefault(item => string.Equals(item.Name, options.Name, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            throw new InvalidOperationException($"No MCP server named '{options.Name}' found.");
        }

        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(ToMcpGetJsonObject(entry), jsonOptions));
            return 0;
        }

        WriteMcpGetResult(entry);
        return 0;
    }

    private async Task<int> RunMcpAddAsync(ITianShuControlPlane controlPlane, McpCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Catalog.WriteConfigBatchAsync(BuildMcpAddRequest(options), cancellationToken).ConfigureAwait(false);
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                operation = "add",
                name = options.Name,
                result,
            }, jsonOptions));
            return 0;
        }

        Console.WriteLine($"Added global MCP server '{options.Name}'.");
        return 0;
    }

    private async Task<int> RunMcpRemoveAsync(ITianShuControlPlane controlPlane, McpCommandOptions options, CancellationToken cancellationToken)
    {
        var snapshot = await controlPlane.Catalog.ReadConfigAsync(
                new ControlPlaneConfigReadQuery
                {
                    WorkingDirectory = null,
                },
                cancellationToken)
            .ConfigureAwait(false);
        var configuredServers = ReadConfiguredMcpServers(snapshot.Config);
        var hasExistingServer = configuredServers?.Keys.Any(
            name => string.Equals(name, options.Name, StringComparison.OrdinalIgnoreCase)) == true;

        ControlPlaneConfigWriteResult? result = null;
        if (hasExistingServer)
        {
            result = await controlPlane.Catalog.WriteConfigBatchAsync(BuildMcpRemoveRequest(options), cancellationToken).ConfigureAwait(false);
        }

        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                operation = "remove",
                name = options.Name,
                removed = hasExistingServer,
                result,
            }, jsonOptions));
            return 0;
        }

        if (hasExistingServer)
        {
            Console.WriteLine($"Removed global MCP server '{options.Name}'.");
        }
        else
        {
            Console.WriteLine($"No MCP server named '{options.Name}' found.");
        }

        return 0;
    }

    private async Task<int> RunConfigValueWriteAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Catalog.WriteConfigValueAsync(BuildConfigValueWriteRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteConfigMutationResult(options, result);
    }

    private async Task<int> RunConfigBatchWriteAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Catalog.WriteConfigBatchAsync(BuildConfigBatchWriteRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteConfigMutationResult(options, result);
    }

    private async Task<int> RunConfigRequirementsReadAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Catalog.ReadConfigRequirementsAsync(BuildConfigRequirementsReadRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteConfigRequirementsReadResult(options, result);
    }

    private async Task<int> RunConfigReadAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Catalog.ReadConfigAsync(BuildConfigReadRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteConfigReadResult(options, result);
    }

    private async Task<int> RunConversationThreadAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Conversations.GetThreadProjectionAsync(BuildConversationThreadRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunSessionSnapshotAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Sessions.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunSessionOverviewAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Sessions.GetSessionOverviewAsync(BuildSessionOverviewRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunSessionListAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Sessions.ListSessionsAsync(BuildSessionListRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunGovernanceApprovalQueueAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Governance.GetApprovalQueueProjectionAsync(BuildApprovalQueueRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunGovernanceUserInputListAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Governance.ListUserInputRequestsAsync(BuildUserInputRequestListRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunCollaborationCreateAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Collaboration.CreateSpaceAsync(BuildCollaborationCreateRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunCollaborationConfigureAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Collaboration.ConfigureSpaceAsync(BuildCollaborationConfigureRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunCollaborationArchiveAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Collaboration.ArchiveSpaceAsync(BuildCollaborationArchiveRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunCollaborationOverviewAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Collaboration.GetSpaceOverviewAsync(BuildCollaborationOverviewRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunCollaborationSpaceAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Collaboration.GetSpaceProjectionAsync(BuildCollaborationSpaceRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunCollaborationListAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Collaboration.ListSpacesAsync(BuildCollaborationListRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunParticipantBindSessionAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Collaboration.BindParticipantToSessionAsync(BuildParticipantBindSessionRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunParticipantBindWorkflowAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Collaboration.BindParticipantToWorkflowAsync(BuildParticipantBindWorkflowRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunParticipantUpdateRoleAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Collaboration.UpdateParticipantRoleAsync(BuildParticipantUpdateRoleRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunParticipantReadAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Collaboration.GetParticipantProjectionAsync(BuildParticipantReadRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunParticipantViewAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Collaboration.GetParticipantViewProjectionAsync(BuildParticipantViewRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunParticipantListAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Collaboration.ListParticipantsInScopeAsync(BuildParticipantListRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunArtifactReadAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Artifacts.GetArtifactProjectionAsync(BuildArtifactReadRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunArtifactListAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Artifacts.GetArtifactCollectionProjectionAsync(BuildArtifactListRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunWorkflowCreateAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Workflows.CreateWorkflowAsync(BuildWorkflowCreateRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunWorkflowPublishPlanAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Workflows.PublishPlanAsync(BuildWorkflowPublishPlanRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunWorkflowCreateTaskAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Workflows.CreateTaskAsync(BuildWorkflowCreateTaskRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunWorkflowUpdateTaskStateAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Workflows.UpdateTaskStateAsync(BuildWorkflowUpdateTaskStateRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunWorkflowBoardAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Workflows.GetWorkflowBoardProjectionAsync(BuildWorkflowBoardRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunWorkflowTaskBoardAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Workflows.GetTaskBoardProjectionAsync(BuildTaskBoardRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunWorkflowPlanAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Workflows.GetPlanProjectionAsync(BuildPlanProjectionRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunAgentListAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Agents.ListAgentsAsync(BuildAgentListRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunAgentRosterAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Agents.GetAgentRosterProjectionAsync(BuildAgentRosterRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunAgentTeamAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Agents.GetTeamProjectionAsync(BuildTeamProjectionRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunAgentThreadRegisterAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
        => WriteAgentThreadRegistrationResult(
            await controlPlane.Agents.RegisterAgentThreadAsync(BuildRuntimeSurfaceRegisterAgentThreadCommand(options), cancellationToken).ConfigureAwait(false),
            options.OutputJson,
            successText: "已登记线程 Agent 元数据。",
            missingText: "登记线程 Agent 元数据失败。");

    private async Task<int> RunAgentJobCreateAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
        => WriteRuntimeSurfaceAgentJobResult(
            options,
            await controlPlane.Workflows.CreateJobAsync(BuildRuntimeSurfaceCreateJobCommand(options), cancellationToken).ConfigureAwait(false));

    private async Task<int> RunAgentJobDispatchAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
        => WriteRuntimeSurfaceAgentJobResult(
            options,
            await controlPlane.Workflows.DispatchJobAsync(BuildRuntimeSurfaceDispatchJobCommand(options), cancellationToken).ConfigureAwait(false));

    private async Task<int> RunAgentJobItemReportAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
        => WriteRuntimeSurfaceAgentJobResult(
            options,
            await controlPlane.Workflows.ReportJobItemAsync(BuildRuntimeSurfaceReportJobItemCommand(options), cancellationToken).ConfigureAwait(false));

    private async Task<int> RunAgentJobReadAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
        => WriteRuntimeSurfaceAgentJobResult(
            options,
            await controlPlane.Workflows.ReadJobAsync(BuildRuntimeSurfaceReadJobQuery(options), cancellationToken).ConfigureAwait(false));

    private async Task<int> RunIdentityAccountAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Identity.GetAccountProfileAsync(BuildAccountProfileRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunIdentityDevicesAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Identity.ListBoundDevicesAsync(BuildBoundDeviceListRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunMemoryProvidersAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Memory.ListMemoryProvidersAsync(BuildMemoryProviderListRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteMemoryResult(options, result);
    }

    private async Task<int> RunMemorySpacesAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Memory.ListMemorySpacesAsync(BuildMemorySpaceListRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteMemoryResult(options, result);
    }

    private async Task<int> RunMemoryOverlayAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Memory.ResolveMemoryOverlayAsync(BuildMemoryOverlayRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteMemoryResult(options, result);
    }

    private async Task<int> RunMemoryFilterAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Memory.FilterMemoryAsync(BuildMemoryFilterRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteMemoryResult(options, result);
    }

    private async Task<int> RunMemoryAddAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var command = BuildMemoryAddRequest(options);
        var result = await controlPlane.Memory.AddMemoryAsync(command, cancellationToken).ConfigureAwait(false);
        return WriteMemoryMutationResult(options, result, command.MemorySpaceId, command.Key, command.Confidence, command.Source);
    }

    private async Task<int> RunMemoryExtractAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Memory.ExtractMemoryAsync(BuildMemoryExtractRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteMemoryResult(options, result);
    }

    private async Task<int> RunMemoryImportAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Memory.ImportMemoryAsync(BuildMemoryImportRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteMemoryResult(options, result);
    }

    private async Task<int> RunMemoryExportAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Memory.ExportMemoryAsync(BuildMemoryExportRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteMemoryResult(options, result);
    }

    private async Task<int> RunMemoryBindProviderAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Memory.BindMemoryProviderAsync(BuildMemoryBindProviderRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteMemoryResult(options, result);
    }

    private async Task<int> RunMemoryConsolidateAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Memory.RunMemoryConsolidationAsync(BuildMemoryConsolidationRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteMemoryResult(options, result);
    }

    private async Task<int> RunMemoryForgetAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var command = BuildMemoryForgetRequest(options);
        var result = await controlPlane.Memory.ForgetMemoryAsync(command, cancellationToken).ConfigureAwait(false);
        return WriteMemoryMutationResult(options, result, command.MemorySpaceId, command.Key);
    }

    private async Task<int> RunMemoryDeleteAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var command = BuildMemoryDeleteRequest(options);
        var result = await controlPlane.Memory.DeleteMemoryAsync(command, cancellationToken).ConfigureAwait(false);
        return WriteMemoryMutationResult(options, result, command.MemorySpaceId, command.Key);
    }

    private async Task<int> RunMemorySupersedeAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var command = BuildMemorySupersedeRequest(options);
        var result = await controlPlane.Memory.SupersedeMemoryAsync(command, cancellationToken).ConfigureAwait(false);
        return WriteMemoryMutationResult(options, result, command.MemorySpaceId, command.NewKey, command.Confidence, command.Source);
    }

    private async Task<int> RunMemoryReviewListAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Memory.ListMemoryReviewsAsync(BuildMemoryReviewListRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteMemoryResult(options, result);
    }

    private async Task<int> RunMemoryReviewApproveAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var command = BuildMemoryReviewApproveRequest(options);
        var result = await controlPlane.Memory.ApproveMemoryReviewAsync(command, cancellationToken).ConfigureAwait(false);
        return WriteMemoryMutationResult(options, result, command.MemorySpaceId, command.Key, source: command.Source);
    }

    private async Task<int> RunMemoryReviewDemoteAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var command = BuildMemoryReviewDemoteRequest(options);
        var result = await controlPlane.Memory.DemoteMemoryReviewAsync(command, cancellationToken).ConfigureAwait(false);
        return WriteMemoryMutationResult(options, result, command.MemorySpaceId, command.Key, source: command.Source);
    }

    private async Task<int> RunMemoryReviewMergeAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var command = BuildMemoryReviewMergeRequest(options);
        var result = await controlPlane.Memory.MergeMemoryReviewAsync(command, cancellationToken).ConfigureAwait(false);
        return WriteMemoryMutationResult(options, result, command.MemorySpaceId, command.MergedKey, command.Confidence, command.Source);
    }

    private async Task<int> RunMemoryReviewRestoreAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var command = BuildMemoryReviewRestoreRequest(options);
        var result = await controlPlane.Memory.RestoreMemoryReviewAsync(command, cancellationToken).ConfigureAwait(false);
        return WriteMemoryMutationResult(options, result, command.MemorySpaceId, command.Key, source: command.Source);
    }

    private async Task<int> RunMemoryFeedbackAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Memory.RecordMemoryFeedbackAsync(BuildMemoryFeedbackRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteMemoryResult(options, result);
    }

    private async Task<int> RunMemoryCitationAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Memory.RecordMemoryCitationAsync(BuildMemoryCitationRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteMemoryResult(options, result);
    }

    private async Task<int> RunDiagnosticsTraceAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Diagnostics.GetExecutionTraceAsync(BuildExecutionTraceRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunDiagnosticsAttemptListAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Diagnostics.ListAttemptSummariesAsync(BuildAttemptSummaryListRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunModelListAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Catalog.ListModelsAsync(BuildModelListRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteModelListResult(options, result);
    }

    private async Task<int> RunModelCatalogAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Catalog.GetCapabilityCatalogAsync(BuildModelCatalogRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunModelResolveAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Catalog.ResolveEngineBindingAsync(BuildModelResolveRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteRuntimeSurfaceObjectResult(options, result);
    }

    private async Task<int> RunToolCatalogAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Catalog.GetCapabilityCatalogAsync(BuildToolCatalogRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteToolCatalogResult(options, result.Tools);
    }

    private async Task<int> RunToolConfigExportAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Catalog.GetCapabilityCatalogAsync(BuildToolCatalogRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteToolConfigExportResult(options, result.Tools);
    }

    private async Task<int> RunAppListAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Catalog.ListAppsAsync(BuildAppListRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteAppListResult(options, result);
    }

    private async Task<int> RunSkillsListAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Catalog.ListSkillsAsync(BuildSkillsListRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteSkillsListResult(options, result);
    }

    private async Task<int> RunSkillsConfigWriteAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Catalog.WriteSkillConfigAsync(BuildSkillsConfigWriteRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteSkillsConfigWriteResult(options, result);
    }

    private async Task<int> RunSkillsRemoteListAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Catalog.ListRemoteSkillsAsync(BuildSkillsRemoteListRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteSkillsRemoteListResult(options, result);
    }

    private async Task<int> RunSkillsRemoteExportAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Catalog.ExportRemoteSkillAsync(BuildSkillsRemoteExportRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteSkillsRemoteExportResult(options, result);
    }

    private async Task<int> RunPluginListAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Catalog.ListPluginsAsync(BuildPluginListRequest(options), cancellationToken).ConfigureAwait(false);
        return WritePluginListResult(options, result);
    }

    private async Task<int> RunPluginReadAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Catalog.ReadPluginAsync(BuildPluginReadRequest(options), cancellationToken).ConfigureAwait(false);
        return WritePluginReadResult(options, result);
    }

    private async Task<int> RunPluginInstallAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Catalog.InstallPluginAsync(BuildPluginInstallRequest(options), cancellationToken).ConfigureAwait(false);
        return WritePluginInstallResult(options, result);
    }

    private async Task<int> RunPluginUninstallAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Catalog.UninstallPluginAsync(BuildPluginUninstallRequest(options), cancellationToken).ConfigureAwait(false);
        return WritePluginUninstallResult(options, result);
    }

    private async Task<int> RunReviewStartAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Workflows.StartReviewAsync(BuildReviewStartRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteReviewStartResult(options, result);
    }

    private async Task<int> RunFeatureListAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Catalog.ListExperimentalFeaturesAsync(new ControlPlaneExperimentalFeatureQuery(), cancellationToken).ConfigureAwait(false);
        return WriteFeatureListResult(options, result);
    }

    private async Task<int> RunFeatureConfigWriteAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var featureName = options.FeatureName ?? string.Empty;
        var feature = await FindExperimentalFeatureAsync(controlPlane, featureName, cancellationToken).ConfigureAwait(false);
        if (feature is null)
        {
            Console.Error.WriteLine($"Unknown feature flag: {featureName}");
            return 1;
        }

        var result = await controlPlane.Catalog.WriteConfigValueAsync(BuildFeatureConfigWriteRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteFeatureConfigWriteResult(options, result, feature);
    }

    private static async Task<ControlPlaneExperimentalFeatureDescriptor?> FindExperimentalFeatureAsync(
        ITianShuControlPlane controlPlane,
        string featureName,
        CancellationToken cancellationToken)
    {
        var result = await controlPlane.Catalog.ListExperimentalFeaturesAsync(new ControlPlaneExperimentalFeatureQuery(), cancellationToken).ConfigureAwait(false);
        return result.Items.FirstOrDefault(item => string.Equals(item.Name, featureName, StringComparison.Ordinal));
    }

    private async Task<int> RunExperimentalFeatureListAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Catalog.ListExperimentalFeaturesAsync(BuildExperimentalFeatureListRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteExperimentalFeatureListResult(options, result);
    }

    private async Task<int> RunCollaborationModeListAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Catalog.ListCollaborationModesAsync(cancellationToken).ConfigureAwait(false);
        return WriteCollaborationModeListResult(options, result);
    }

    private async Task<int> RunMcpServerStatusListAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Catalog.ListMcpServerStatusAsync(BuildMcpServerStatusListRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteMcpServerStatusListResult(options, result);
    }

    private async Task<int> RunMcpServerReloadAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Catalog.ReloadMcpServersAsync(cancellationToken).ConfigureAwait(false);
        return WriteMcpServerReloadResult(options, result);
    }

    private async Task<int> RunConversationSummaryAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Artifacts.GetConversationSummaryAsync(BuildConversationSummaryRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteConversationSummaryResult(options, result);
    }

    private async Task<int> RunGitDiffToRemoteAsync(ITianShuControlPlane controlPlane, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.Artifacts.GetGitDiffToRemoteAsync(BuildGitDiffToRemoteRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteGitDiffToRemoteResult(options, result);
    }

    public async Task<int> RunCommandExecAsync(CommandExecCommandOptions options, CancellationToken cancellationToken)
    {
        var bootstrap = CliRuntimeBootstrapper.Prepare(options);
        await using var runtime = runtimeFactory();
        await runtime.InitializeAsync(bootstrap.RuntimeOptions, dynamicToolCallHandler: null, cancellationToken).ConfigureAwait(false);
        var surface = runtime.AsNorthboundSurface();

        return options.CommandKind switch
        {
            CommandExecCommandKind.Exec => WriteCommandExecResult(
                options,
                await surface.Execution.StartCommandExecutionAsync(BuildCommandExecStartRequest(options), cancellationToken).ConfigureAwait(false)),
            CommandExecCommandKind.Write => WriteCommandExecAcceptedResult(
                options,
                await surface.Execution.WriteCommandExecutionAsync(BuildCommandExecWriteRequest(options), cancellationToken).ConfigureAwait(false)),
            CommandExecCommandKind.Terminate => WriteCommandExecAcceptedResult(
                options,
                await surface.Execution.TerminateCommandExecutionAsync(BuildCommandExecTerminateRequest(options), cancellationToken).ConfigureAwait(false)),
            CommandExecCommandKind.Resize => WriteCommandExecAcceptedResult(
                options,
                await surface.Execution.ResizeCommandExecutionAsync(BuildCommandExecResizeRequest(options), cancellationToken).ConfigureAwait(false)),
            _ => throw new InvalidOperationException($"不支持的 command exec 命令：{options.CommandKind}"),
        };
    }

    public async Task<int> RunCodeModeAsync(CodeModeCommandOptions options, CancellationToken cancellationToken)
    {
        var bootstrap = CliRuntimeBootstrapper.Prepare(options);
        await using var runtime = runtimeFactory();
        await runtime.InitializeAsync(bootstrap.RuntimeOptions, dynamicToolCallHandler: null, cancellationToken).ConfigureAwait(false);
        var surface = runtime.AsNorthboundSurface();
        var sessionSnapshot = await CliSessionSnapshotUtilities.GetSnapshotAsync(runtime, cancellationToken).ConfigureAwait(false);

        var threadId = Normalize(options.ThreadId) ?? sessionSnapshot.ActiveThreadId?.Value;
        if (string.IsNullOrWhiteSpace(threadId))
        {
            throw new InvalidOperationException("未找到可用的 threadId。请显式传入 --thread-id，或先恢复/创建会话线程。");
        }

        var result = options.CommandKind switch
        {
            CodeModeCommandKind.Exec => await surface.Execution.ExecuteCodeModeAsync(
                    new ControlPlaneCodeModeExecCommand
                    {
                        ThreadId = new ThreadId(threadId),
                        Input = ReadCodeModeInput(options),
                        YieldTimeMs = options.YieldTimeMs,
                        MaxOutputTokens = options.MaxOutputTokens,
                    },
                    cancellationToken)
                .ConfigureAwait(false),
            CodeModeCommandKind.Wait => await surface.Execution.WaitCodeModeAsync(
                    new ControlPlaneCodeModeWaitCommand
                    {
                        ThreadId = new ThreadId(threadId),
                        CellId = Normalize(options.CellId) ?? string.Empty,
                        YieldTimeMs = options.YieldTimeMs,
                        MaxTokens = options.MaxTokens,
                        Terminate = options.Terminate,
                    },
                    cancellationToken)
                .ConfigureAwait(false),
            _ => throw new InvalidOperationException($"不支持的 code_mode 命令：{options.CommandKind}"),
        };

        return WriteCodeModeResult(options, result);
    }

    public async Task<int> RunFuzzyFileSearchAsync(FuzzyFileSearchCommandOptions options, CancellationToken cancellationToken)
    {
        var bootstrap = CliRuntimeBootstrapper.Prepare(options);
        await using var runtime = runtimeFactory();
        await runtime.InitializeAsync(bootstrap.RuntimeOptions, dynamicToolCallHandler: null, cancellationToken).ConfigureAwait(false);
        var controlPlane = TianShuControlPlaneClientFactory.Create(runtime);

        switch (options.CommandKind)
        {
            case FuzzyFileSearchCommandKind.Search:
                return WriteFuzzyFileSearchResult(
                    options,
                    await controlPlane.Conversations.SearchFuzzyFilesAsync(BuildFuzzyFileSearchSearchRequest(options), cancellationToken).ConfigureAwait(false));

            case FuzzyFileSearchCommandKind.Start:
                return WriteFuzzyFileSearchResult(
                    options,
                    await controlPlane.Conversations.StartFuzzyFileSearchSessionAsync(BuildFuzzyFileSearchSessionStartRequest(options), cancellationToken).ConfigureAwait(false));

            case FuzzyFileSearchCommandKind.Update:
                return await RunFuzzyFileSearchUpdateAsync(controlPlane.Conversations, runtime, options, cancellationToken).ConfigureAwait(false);

            case FuzzyFileSearchCommandKind.Stop:
                return WriteFuzzyFileSearchResult(
                    options,
                    await controlPlane.Conversations.StopFuzzyFileSearchSessionAsync(BuildFuzzyFileSearchSessionStopRequest(options), cancellationToken).ConfigureAwait(false));

            default:
                throw new InvalidOperationException($"不支持的 fuzzy file search 命令：{options.CommandKind}");
        }
    }

    public async Task<int> RunFeedbackAsync(FeedbackCommandOptions options, CancellationToken cancellationToken)
    {
        var bootstrap = CliRuntimeBootstrapper.Prepare(options);
        await using var runtime = runtimeFactory();
        await runtime.InitializeAsync(bootstrap.RuntimeOptions, dynamicToolCallHandler: null, cancellationToken).ConfigureAwait(false);
        var controlPlane = TianShuControlPlaneClientFactory.Create(runtime);

        var result = await controlPlane.Diagnostics.UploadFeedbackAsync(BuildFeedbackRequest(options), cancellationToken).ConfigureAwait(false);
        return WriteFeedbackResult(options, result);
    }

    public async Task<int> RunWindowsSandboxAsync(WindowsSandboxCommandOptions options, CancellationToken cancellationToken)
    {
        var bootstrap = CliRuntimeBootstrapper.Prepare(options);
        await using var runtime = runtimeFactory();
        await runtime.InitializeAsync(bootstrap.RuntimeOptions, dynamicToolCallHandler: null, cancellationToken).ConfigureAwait(false);
        var surface = runtime.AsNorthboundSurface();

        var notificationSource = new TaskCompletionSource<WindowsSandboxSetupPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<ControlPlaneConversationStreamEventArgs>? handler = null;
        handler = (_, args) =>
        {
            ControlPlaneConversationStreamEvent streamEvent = args.StreamEvent;
            var notification = ReadStreamPayload<WindowsSandboxSetupPayload>(
                streamEvent,
                ControlPlaneConversationStreamPayloadKind.WindowsSandboxSetup);
            if (notification is not null)
            {
                notificationSource.TrySetResult(notification);
            }
        };

        runtime.StreamEventReceived += handler;
        try
        {
            var result = await surface.Environment.StartWindowsSandboxSetupAsync(BuildWindowsSandboxSetupRequest(options), cancellationToken).ConfigureAwait(false);
            var notification = await WaitForNotificationOrNullAsync(notificationSource.Task, TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
            return WriteWindowsSandboxResult(options, result, notification);
        }
        finally
        {
            runtime.StreamEventReceived -= handler;
        }
    }

    private async Task<int> RunMcpServerOauthLoginAsync(ITianShuControlPlane controlPlane, IExecutionRuntime runtime, RuntimeSurfaceCommandOptions options, CancellationToken cancellationToken)
    {
        var request = BuildMcpServerOauthLoginRequest(options);
        TaskCompletionSource<McpServerOauthLoginPayload>? notificationSource = null;
        EventHandler<ControlPlaneConversationStreamEventArgs>? handler = null;
        if (options.WaitForCompletion)
        {
            notificationSource = new TaskCompletionSource<McpServerOauthLoginPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
            handler = (_, args) =>
            {
                ControlPlaneConversationStreamEvent streamEvent = args.StreamEvent;
                var notification = ReadStreamPayload<McpServerOauthLoginPayload>(
                    streamEvent,
                    ControlPlaneConversationStreamPayloadKind.McpServerOauthLogin);
                if (notification is null)
                {
                    return;
                }

                if (!string.Equals(notification.Name, options.McpServerName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                notificationSource.TrySetResult(notification);
            };
            runtime.StreamEventReceived += handler;
        }

        try
        {
            var result = await controlPlane.Catalog.StartMcpServerOauthLoginAsync(request, cancellationToken).ConfigureAwait(false);
            var completion = notificationSource is null
                ? null
                : await WaitForNotificationOrNullAsync(notificationSource.Task, TimeSpan.FromSeconds((options.TimeoutSecs ?? 300) + 5), cancellationToken).ConfigureAwait(false);

            if (options.OutputJson)
            {
                if (completion is null)
                {
                    Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
                }
                else
                {
                    Console.WriteLine(JsonSerializer.Serialize(new { authorization = result, completion }, jsonOptions));
                }

                return 0;
            }

            Console.WriteLine($"已启动 MCP Server OAuth 登录：{options.McpServerName}");
            Console.WriteLine($"授权地址：{result.AuthorizationUrl ?? "<unknown>"}");
            if (completion is not null)
            {
                var success = completion.Success ?? false;
                var error = completion.Error;
                Console.WriteLine(success ? "OAuth 登录已完成。" : $"OAuth 登录失败：{error ?? "unknown"}");
                return success ? 0 : 1;
            }

            return 0;
        }
        finally
        {
            if (handler is not null)
            {
                runtime.StreamEventReceived -= handler;
            }
        }
    }

    public async Task<int> RunRealtimeAsync(RealtimeCommandOptions options, CancellationToken cancellationToken)
    {
        var bootstrap = CliRuntimeBootstrapper.Prepare(options);
        await using var runtime = runtimeFactory();
        await runtime.InitializeAsync(bootstrap.RuntimeOptions, dynamicToolCallHandler: null, cancellationToken).ConfigureAwait(false);

        if (options.CommandKind == RealtimeCommandKind.Start)
        {
            return await RunRealtimeStartAsync(runtime, options, cancellationToken).ConfigureAwait(false);
        }

        var result = await RunRealtimeCommandAsync(TianShuControlPlaneClientFactory.Create(runtime).Conversations, options, cancellationToken).ConfigureAwait(false);
        return WriteRealtimeCommandAcceptedResult(result, options);
    }

    private async Task<int> RunRealtimeStartAsync(IExecutionRuntime runtime, RealtimeCommandOptions options, CancellationToken cancellationToken)
    {
        var notificationSource = new TaskCompletionSource<RealtimeSessionPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<ControlPlaneConversationStreamEventArgs>? handler = null;
        handler = (_, args) =>
        {
            ControlPlaneConversationStreamEvent streamEvent = args.StreamEvent;
            var notification = ReadStreamPayload<RealtimeSessionPayload>(
                streamEvent,
                ControlPlaneConversationStreamPayloadKind.RealtimeSession);
            if (notification is null)
            {
                return;
            }

            var threadId = notification.ThreadId;
            var sessionId = notification.SessionId;
            if (!string.Equals(threadId, options.ThreadId, StringComparison.Ordinal))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(options.SessionId)
                && !string.Equals(sessionId, options.SessionId, StringComparison.Ordinal))
            {
                return;
            }

            notificationSource.TrySetResult(notification);
        };

        runtime.StreamEventReceived += handler;
        try
        {
            var result = await TianShuControlPlaneClientFactory.Create(runtime).Conversations.StartRealtimeAsync(
                    BuildRealtimeStartRequest(options),
                    cancellationToken)
                .ConfigureAwait(false);
            var notification = await WaitForNotificationOrNullAsync(notificationSource.Task, TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            return WriteRealtimeResult(options, result, notification);
        }
        finally
        {
            runtime.StreamEventReceived -= handler;
        }
    }

    private static Task<ControlPlaneRealtimeCommandAcceptedResult> RunRealtimeCommandAsync(IConversationControlPlane controlPlane, RealtimeCommandOptions options, CancellationToken cancellationToken)
        => options.CommandKind switch
        {
            RealtimeCommandKind.AppendText => controlPlane.AppendRealtimeTextAsync(BuildRealtimeAppendTextRequest(options), cancellationToken),
            RealtimeCommandKind.AppendAudio => controlPlane.AppendRealtimeAudioAsync(BuildRealtimeAppendAudioRequest(options), cancellationToken),
            RealtimeCommandKind.HandoffOutput => controlPlane.HandoffRealtimeOutputAsync(BuildRealtimeHandoffOutputRequest(options), cancellationToken),
            RealtimeCommandKind.Stop => controlPlane.StopRealtimeAsync(BuildRealtimeStopRequest(options), cancellationToken),
            _ => throw new InvalidOperationException($"不支持的 realtime 命令：{options.CommandKind}"),
        };

    private async Task<int> RunFuzzyFileSearchUpdateAsync(IConversationControlPlane controlPlane, IExecutionRuntime runtime, FuzzyFileSearchCommandOptions options, CancellationToken cancellationToken)
    {
        var notificationSource = new TaskCompletionSource<FuzzyFileSearchSessionPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<ControlPlaneConversationStreamEventArgs>? handler = null;
        handler = (_, args) =>
        {
            ControlPlaneConversationStreamEvent streamEvent = args.StreamEvent;
            var notification = ReadStreamPayload<FuzzyFileSearchSessionPayload>(
                streamEvent,
                ControlPlaneConversationStreamPayloadKind.FuzzyFileSearchSession);
            if (notification is not null
                && !notification.IsCompleted
                && string.Equals(notification.SessionId, options.SessionId, StringComparison.Ordinal))
            {
                notificationSource.TrySetResult(notification);
            }
        };

        runtime.StreamEventReceived += handler;
        try
        {
            var startRequest = BuildFuzzyFileSearchSessionStartRequest(options);
            await controlPlane.StartFuzzyFileSearchSessionAsync(startRequest, cancellationToken).ConfigureAwait(false);

            var result = await controlPlane.UpdateFuzzyFileSearchSessionAsync(BuildFuzzyFileSearchSessionUpdateRequest(options), cancellationToken).ConfigureAwait(false);
            IReadOnlyList<FuzzyFileSearchFilePayload>? files = null;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(1));
                files = (await notificationSource.Task.WaitAsync(timeout.Token).ConfigureAwait(false)).Files;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }

            return WriteFuzzyFileSearchUpdateResult(options, result, files);
        }
        finally
        {
            runtime.StreamEventReceived -= handler;
        }
    }

    public async Task<int> RunThreadAsync(ThreadCommandOptions options, CancellationToken cancellationToken)
    {
        var bootstrap = CliRuntimeBootstrapper.Prepare(options);
        await using var runtime = runtimeFactory();
        await runtime.InitializeAsync(bootstrap.RuntimeOptions, dynamicToolCallHandler: null, cancellationToken).ConfigureAwait(false);
        var controlPlane = TianShuControlPlaneClientFactory.Create(runtime);

        return options.CommandKind switch
        {
            ThreadCommandKind.List => await RunThreadListAsync(controlPlane.Conversations, options, cancellationToken).ConfigureAwait(false),
            ThreadCommandKind.Start => await RunThreadStartAsync(controlPlane.Conversations, options, cancellationToken).ConfigureAwait(false),
            ThreadCommandKind.Fork => await RunThreadForkAsync(controlPlane.Conversations, options, cancellationToken).ConfigureAwait(false),
            ThreadCommandKind.Archive => await RunThreadArchiveAsync(controlPlane.Conversations, options, cancellationToken).ConfigureAwait(false),
            ThreadCommandKind.Delete => await RunThreadDeleteAsync(controlPlane.Conversations, options, cancellationToken).ConfigureAwait(false),
            ThreadCommandKind.Clear => await RunThreadClearAsync(controlPlane.Conversations, options, cancellationToken).ConfigureAwait(false),
            ThreadCommandKind.Rename => await RunThreadRenameAsync(controlPlane.Conversations, options, cancellationToken).ConfigureAwait(false),
            ThreadCommandKind.Resume => await RunThreadResumeAsync(controlPlane.Conversations, controlPlane.Governance, runtime, options, cancellationToken).ConfigureAwait(false),
            ThreadCommandKind.LoadedList => await RunThreadLoadedListAsync(controlPlane.Conversations, options, cancellationToken).ConfigureAwait(false),
            ThreadCommandKind.Read => await RunThreadReadAsync(controlPlane.Conversations, options, cancellationToken).ConfigureAwait(false),
            ThreadCommandKind.Compact => await RunThreadCompactAsync(controlPlane.Conversations, options, cancellationToken).ConfigureAwait(false),
            ThreadCommandKind.CleanBackgroundTerminals => await RunThreadCleanBackgroundTerminalsAsync(controlPlane.Conversations, options, cancellationToken).ConfigureAwait(false),
            ThreadCommandKind.Unsubscribe => await RunThreadUnsubscribeAsync(controlPlane.Conversations, options, cancellationToken).ConfigureAwait(false),
            ThreadCommandKind.IncrementElicitation => await RunThreadIncrementElicitationAsync(controlPlane.Conversations, options, cancellationToken).ConfigureAwait(false),
            ThreadCommandKind.DecrementElicitation => await RunThreadDecrementElicitationAsync(controlPlane.Conversations, options, cancellationToken).ConfigureAwait(false),
            ThreadCommandKind.Unarchive => await RunThreadUnarchiveAsync(controlPlane.Conversations, options, cancellationToken).ConfigureAwait(false),
            ThreadCommandKind.Metadata => await RunThreadMetadataAsync(controlPlane.Conversations, options, cancellationToken).ConfigureAwait(false),
            ThreadCommandKind.Rollback => await RunThreadRollbackAsync(controlPlane.Conversations, options, cancellationToken).ConfigureAwait(false),
            _ => 2,
        };
    }

    internal static RuntimeSurfaceInvocation BuildRuntimeSurfaceInvocation(RuntimeSurfaceCommandOptions options)
        => options.CommandKind switch
        {
            RuntimeSurfaceCommandKind.ConversationThread => new("conversation/thread/read", BuildConversationThreadPayload(options)),
            RuntimeSurfaceCommandKind.SessionSnapshot => new("session/snapshot/read", BuildSessionSnapshotPayload()),
            RuntimeSurfaceCommandKind.SessionOverview => new("session/overview/read", BuildSessionOverviewPayload(options)),
            RuntimeSurfaceCommandKind.SessionList => new("session/list", BuildSessionListPayload(options)),
            RuntimeSurfaceCommandKind.GovernanceApprovalQueue => new("governance/approvalqueue/read", BuildApprovalQueuePayload(options)),
            RuntimeSurfaceCommandKind.GovernanceUserInputList => new("governance/userinputs/list", BuildUserInputRequestListPayload(options)),
            RuntimeSurfaceCommandKind.CollaborationCreate => new("collaboration/create", BuildCollaborationCreatePayload(options)),
            RuntimeSurfaceCommandKind.CollaborationConfigure => new("collaboration/configure", BuildCollaborationConfigurePayload(options)),
            RuntimeSurfaceCommandKind.CollaborationArchive => new("collaboration/archive", BuildCollaborationArchivePayload(options)),
            RuntimeSurfaceCommandKind.CollaborationOverview => new("collaboration/overview/read", BuildCollaborationOverviewPayload(options)),
            RuntimeSurfaceCommandKind.CollaborationSpace => new("collaboration/space/read", BuildCollaborationSpacePayload(options)),
            RuntimeSurfaceCommandKind.CollaborationList => new("collaboration/list", BuildCollaborationListPayload(options)),
            RuntimeSurfaceCommandKind.ParticipantBindSession => new("participant/bindsession", BuildParticipantBindSessionPayload(options)),
            RuntimeSurfaceCommandKind.ParticipantBindWorkflow => new("participant/bindworkflow", BuildParticipantBindWorkflowPayload(options)),
            RuntimeSurfaceCommandKind.ParticipantUpdateRole => new("participant/updaterole", BuildParticipantUpdateRolePayload(options)),
            RuntimeSurfaceCommandKind.ParticipantRead => new("participant/read", BuildParticipantReadPayload(options)),
            RuntimeSurfaceCommandKind.ParticipantView => new("participant/view/read", BuildParticipantViewPayload(options)),
            RuntimeSurfaceCommandKind.ParticipantList => new("participant/list", BuildParticipantListPayload(options)),
            RuntimeSurfaceCommandKind.ArtifactRead => new("artifact/detail/read", BuildArtifactReadPayload(options)),
            RuntimeSurfaceCommandKind.ArtifactList => new("artifact/collection/read", BuildArtifactListPayload(options)),
            RuntimeSurfaceCommandKind.WorkflowCreate => new("workflow/create", BuildWorkflowCreatePayload(options)),
            RuntimeSurfaceCommandKind.WorkflowPublishPlan => new("workflow/plan/publish", BuildWorkflowPublishPlanPayload(options)),
            RuntimeSurfaceCommandKind.WorkflowCreateTask => new("workflow/task/create", BuildWorkflowCreateTaskPayload(options)),
            RuntimeSurfaceCommandKind.WorkflowUpdateTaskState => new("workflow/task/updatestate", BuildWorkflowUpdateTaskStatePayload(options)),
            RuntimeSurfaceCommandKind.WorkflowBoard => new("workflow/board/read", BuildWorkflowBoardPayload(options)),
            RuntimeSurfaceCommandKind.WorkflowTaskBoard => new("workflow/taskboard/read", BuildTaskBoardPayload(options)),
            RuntimeSurfaceCommandKind.WorkflowPlan => new("workflow/plan/read", BuildPlanProjectionPayload(options)),
            RuntimeSurfaceCommandKind.AgentList => new("agent/list", BuildAgentListPayload(options)),
            RuntimeSurfaceCommandKind.AgentRoster => new("agent/roster/read", BuildAgentRosterPayload(options)),
            RuntimeSurfaceCommandKind.AgentTeam => new("agent/team/read", BuildTeamProjectionPayload(options)),
            RuntimeSurfaceCommandKind.AgentThreadRegister => new("agent/thread/register", BuildAgentThreadRegisterPayload(options)),
            RuntimeSurfaceCommandKind.AgentJobCreate => new("agent/job/create", BuildAgentJobCreatePayload(options)),
            RuntimeSurfaceCommandKind.AgentJobDispatch => new("agent/job/dispatch", BuildAgentJobDispatchPayload(options)),
            RuntimeSurfaceCommandKind.AgentJobItemReport => new("agent/job/item/report", BuildAgentJobItemReportPayload(options)),
            RuntimeSurfaceCommandKind.AgentJobRead => new("agent/job/read", BuildAgentJobReadPayload(options)),
            RuntimeSurfaceCommandKind.IdentityAccount => new("identity/account/read", BuildAccountProfilePayload(options)),
            RuntimeSurfaceCommandKind.IdentityDevices => new("identity/devices/list", BuildBoundDeviceListPayload(options)),
            RuntimeSurfaceCommandKind.MemoryProviders => new("memory/providers/list", BuildMemoryProviderListPayload(options)),
            RuntimeSurfaceCommandKind.MemorySpaces => new("memory/spaces/list", BuildMemorySpaceListPayload(options)),
            RuntimeSurfaceCommandKind.MemoryOverlay => new("memory/overlay/read", BuildMemoryOverlayPayload(options)),
            RuntimeSurfaceCommandKind.MemoryFilter => new("memory/filter", BuildTypedPayloadObject(options, "memory filter payload")),
            RuntimeSurfaceCommandKind.MemoryAdd => new("memory/add", BuildTypedPayloadObject(options, "memory add payload")),
            RuntimeSurfaceCommandKind.MemoryExtract => new("memory/extract", BuildTypedPayloadObject(options, "memory extract payload")),
            RuntimeSurfaceCommandKind.MemoryImport => new("memory/import", BuildTypedPayloadObject(options, "memory import payload")),
            RuntimeSurfaceCommandKind.MemoryExport => new("memory/export", BuildTypedPayloadObject(options, "memory export payload")),
            RuntimeSurfaceCommandKind.MemoryBindProvider => new("memory/provider/bind", BuildTypedPayloadObject(options, "memory provider binding payload")),
            RuntimeSurfaceCommandKind.MemoryConsolidate => new("memory/consolidation/run", BuildTypedPayloadObject(options, "memory consolidation payload")),
            RuntimeSurfaceCommandKind.MemoryForget => new("memory/forget", BuildTypedPayloadObject(options, "memory forget payload")),
            RuntimeSurfaceCommandKind.MemoryDelete => new("memory/delete", BuildTypedPayloadObject(options, "memory delete payload")),
            RuntimeSurfaceCommandKind.MemorySupersede => new("memory/supersede", BuildTypedPayloadObject(options, "memory supersede payload")),
            RuntimeSurfaceCommandKind.MemoryReviewList => new("memory/review/list", BuildTypedPayloadObject(options, "memory review list payload")),
            RuntimeSurfaceCommandKind.MemoryReviewApprove => new("memory/review/approve", BuildTypedPayloadObject(options, "memory review approve payload")),
            RuntimeSurfaceCommandKind.MemoryReviewDemote => new("memory/review/demote", BuildTypedPayloadObject(options, "memory review demote payload")),
            RuntimeSurfaceCommandKind.MemoryReviewMerge => new("memory/review/merge", BuildTypedPayloadObject(options, "memory review merge payload")),
            RuntimeSurfaceCommandKind.MemoryReviewRestore => new("memory/review/restore", BuildTypedPayloadObject(options, "memory review restore payload")),
            RuntimeSurfaceCommandKind.MemoryFeedback => new("memory/feedback/record", BuildTypedPayloadObject(options, "memory feedback payload")),
            RuntimeSurfaceCommandKind.MemoryCitation => new("memory/citation/record", BuildTypedPayloadObject(options, "memory citation payload")),
            RuntimeSurfaceCommandKind.DiagnosticsTrace => new("diagnostics/trace/read", BuildExecutionTracePayload(options)),
            RuntimeSurfaceCommandKind.DiagnosticsAttemptList => new("diagnostics/attempts/list", BuildAttemptSummaryListPayload(options)),
            RuntimeSurfaceCommandKind.ModelList => new("model/list", BuildModelListPayload(options)),
            RuntimeSurfaceCommandKind.ModelCatalog => new("model/catalog/read", BuildModelCatalogPayload(options)),
            RuntimeSurfaceCommandKind.ModelResolve => new("model/binding/resolve", BuildModelResolvePayload(options)),
            RuntimeSurfaceCommandKind.ToolCatalog => new("tools/catalog/read", BuildToolCatalogPayload(options)),
            RuntimeSurfaceCommandKind.ToolConfigExport => new("tools/config/export", BuildToolConfigExportPayload(options)),
            RuntimeSurfaceCommandKind.SkillsList => new("skills/list", BuildSkillsListPayload(options)),
            RuntimeSurfaceCommandKind.SkillsConfigWrite => new("skills/config/write", BuildSkillsConfigWritePayload(options)),
            RuntimeSurfaceCommandKind.SkillsRemoteList => new("skills/remote/list", BuildSkillsRemoteListPayload(options)),
            RuntimeSurfaceCommandKind.SkillsRemoteExport => new("skills/remote/export", BuildSkillsRemoteExportPayload(options)),
            RuntimeSurfaceCommandKind.PluginList => new("plugin/list", BuildPluginListPayload(options)),
            RuntimeSurfaceCommandKind.PluginRead => new("plugin/read", BuildPluginReadPayload(options)),
            RuntimeSurfaceCommandKind.PluginInstall => new("plugin/install", BuildPluginInstallPayload(options)),
            RuntimeSurfaceCommandKind.PluginUninstall => new("plugin/uninstall", BuildPluginUninstallPayload(options)),
            RuntimeSurfaceCommandKind.AppList => new("app/list", BuildAppListPayload(options)),
            RuntimeSurfaceCommandKind.ReviewStart => new("review/start", BuildReviewStartPayload(options)),
            RuntimeSurfaceCommandKind.FeatureList => new("experimentalfeature/list", BuildFeatureListPayload()),
            RuntimeSurfaceCommandKind.FeatureConfigWrite => new("config/value/write", BuildFeatureConfigWritePayload(options)),
            RuntimeSurfaceCommandKind.ConfigRead => new("config/read", BuildConfigReadPayload(options)),
            RuntimeSurfaceCommandKind.ConfigRequirementsRead => new("configrequirements/read", null),
            RuntimeSurfaceCommandKind.ConfigValueWrite => new("config/value/write", BuildConfigValueWritePayload(options)),
            RuntimeSurfaceCommandKind.ConfigBatchWrite => new("config/batchwrite", BuildConfigBatchWritePayload(options)),
            RuntimeSurfaceCommandKind.ExperimentalFeatureList => new("experimentalfeature/list", BuildListPayload(options)),
            RuntimeSurfaceCommandKind.CollaborationModeList => new("collaborationmode/list", null),
            RuntimeSurfaceCommandKind.McpServerStatusList => new("mcpserverstatus/list", BuildListPayload(options)),
            RuntimeSurfaceCommandKind.McpServerReload => new("config/mcpserver/reload", null),
            RuntimeSurfaceCommandKind.McpServerOauthLogin => new("mcpserver/oauth/login", BuildMcpServerOauthLoginPayload(options)),
            RuntimeSurfaceCommandKind.ConversationSummary => new("artifact/conversationsummary/read", BuildConversationSummaryPayload(options)),
            RuntimeSurfaceCommandKind.GitDiffToRemote => new("artifact/gitdifftoremote/read", BuildGitDiffToRemotePayload(options)),
            _ => throw new InvalidOperationException($"不支持的 CLI runtime surface 命令：{options.CommandKind}"),
        };

    internal static ControlPlaneCommandExecutionStartCommand BuildCommandExecStartRequest(CommandExecCommandOptions options)
        => new()
        {
            WorkingDirectory = options.WorkingDirectory,
            CommandText = Normalize(options.CommandText),
            CommandArgs = string.IsNullOrWhiteSpace(options.CommandText)
                ? ReadStringArrayPayload(options.CommandArgsJson, options.CommandArgsFilePath, "命令参数数组")
                : Array.Empty<string>(),
            ProcessId = Normalize(options.ProcessId),
            Tty = options.Tty,
            Size = BuildCommandExecTerminalSize(options.Rows, options.Cols),
            StreamStdin = options.StreamStdin,
            StreamStdoutStderr = options.StreamStdoutStderr,
            Background = options.Background,
            DisableTimeout = options.DisableTimeout,
            TimeoutMs = options.TimeoutMs,
            DisableOutputCap = options.DisableOutputCap,
            OutputBytesCap = options.OutputBytesCap,
            ThreadId = Normalize(options.ThreadId) is { } threadId ? new ThreadId(threadId) : null,
            TurnId = Normalize(options.TurnId) is { } turnId ? new TurnId(turnId) : null,
            ItemId = Normalize(options.ItemId),
            ApprovalPolicy = Normalize(options.ApprovalPolicy),
            Approved = options.Approved,
            Login = options.Login,
            EnvironmentVariables = ReadStringOrNullDictionaryPayload(options.EnvJson, options.EnvFilePath, "环境变量覆盖"),
            Sandbox = ToStructuredValue(ReadStructuredValuePayload(options.SandboxJson, options.SandboxFilePath, "sandbox 配置")),
        };

    internal static ControlPlaneCommandExecutionWriteCommand BuildCommandExecWriteRequest(CommandExecCommandOptions options)
        => new()
        {
            ProcessId = Normalize(options.ProcessId) ?? string.Empty,
            DeltaBase64 = ReadCommandInputBase64(options),
            CloseStdin = options.CloseStdin,
        };

    internal static ControlPlaneCommandExecutionTerminateCommand BuildCommandExecTerminateRequest(CommandExecCommandOptions options)
        => new()
        {
            ProcessId = Normalize(options.ProcessId) ?? string.Empty,
        };

    internal static ControlPlaneCommandExecutionResizeCommand BuildCommandExecResizeRequest(CommandExecCommandOptions options)
        => new()
        {
            ProcessId = Normalize(options.ProcessId) ?? string.Empty,
            Size = BuildRequiredCommandExecTerminalSize(options.Rows, options.Cols),
        };

    internal static RuntimeSurfaceInvocation BuildFuzzyFileSearchInvocation(FuzzyFileSearchCommandOptions options)
        => options.CommandKind switch
        {
            FuzzyFileSearchCommandKind.Search => new("fuzzyFileSearch", BuildFuzzyFileSearchSearchPayload(options)),
            FuzzyFileSearchCommandKind.Start => new("fuzzyFileSearch/sessionStart", BuildFuzzyFileSearchSessionStartPayload(options)),
            FuzzyFileSearchCommandKind.Update => new("fuzzyFileSearch/sessionUpdate", BuildFuzzyFileSearchSessionUpdatePayload(options)),
            FuzzyFileSearchCommandKind.Stop => new("fuzzyFileSearch/sessionStop", BuildFuzzyFileSearchSessionStopPayload(options)),
            _ => throw new InvalidOperationException($"不支持的 fuzzy file search 命令：{options.CommandKind}"),
        };

    internal static RuntimeSurfaceInvocation BuildFeedbackInvocation(FeedbackCommandOptions options)
        => new("feedback/upload", BuildFeedbackPayload(options));

    internal static RuntimeSurfaceInvocation BuildWindowsSandboxInvocation(WindowsSandboxCommandOptions options)
        => new("windowsSandbox/setupStart", BuildWindowsSandboxPayload(options));

    internal static ControlPlaneWindowsSandboxSetupStartCommand BuildWindowsSandboxSetupRequest(WindowsSandboxCommandOptions options)
        => new()
        {
            Mode = ParseWindowsSandboxSetupMode(options.Mode),
            WorkingDirectory = options.SandboxCwd,
        };

    internal static RuntimeSurfaceInvocation BuildThreadRpcInvocation(ThreadCommandOptions options)
        => options.CommandKind switch
        {
            ThreadCommandKind.LoadedList => new("thread/loaded/list", BuildThreadLoadedListPayload(options)),
            ThreadCommandKind.Compact => new("thread/compact/start", BuildThreadCompactPayload(options)),
            ThreadCommandKind.CleanBackgroundTerminals => new("thread/backgroundTerminals/clean", BuildThreadIdPayload(options)),
            ThreadCommandKind.Unsubscribe => new("thread/unsubscribe", BuildThreadIdPayload(options)),
            ThreadCommandKind.IncrementElicitation => new("thread/increment_elicitation", BuildThreadIdPayload(options)),
            ThreadCommandKind.DecrementElicitation => new("thread/decrement_elicitation", BuildThreadIdPayload(options)),
            ThreadCommandKind.Read => new("thread/read", BuildThreadReadPayload(options)),
            ThreadCommandKind.Unarchive => new("thread/unarchive", BuildThreadIdPayload(options)),
            ThreadCommandKind.Metadata => new("thread/metadata/update", BuildThreadMetadataPayload(options)),
            ThreadCommandKind.Rollback => new("thread/rollback", BuildThreadRollbackPayload(options)),
            _ => throw new InvalidOperationException($"不支持的 thread RPC 命令：{options.CommandKind}"),
        };

    private async Task<int> RunThreadListAsync(IConversationControlPlane controlPlane, ThreadCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.ListThreadsAsync(BuildControlPlaneThreadListQuery(options), cancellationToken).ConfigureAwait(false);
        var threads = result.Threads;
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(ToCliThreadListResult(result), jsonOptions));
            return 0;
        }

        if (threads.Count == 0)
        {
            Console.WriteLine("未找到线程。");
            return 0;
        }

        foreach (var thread in threads)
        {
            Console.WriteLine($"{thread.ThreadId.Value}\t{thread.UpdatedAt:yyyy-MM-dd HH:mm:ss}\t{thread.WorkingDirectory ?? "<none>"}\t{ResolveThreadTitle(thread.Name, thread.Preview, thread.ThreadId.Value)}");
        }

        WriteNextCursor(result.NextCursor);
        return 0;
    }

    private async Task<int> RunThreadStartAsync(IConversationControlPlane controlPlane, ThreadCommandOptions options, CancellationToken cancellationToken)
    {
        var thread = await controlPlane.StartThreadAsync(BuildControlPlaneThreadStartCommand(options), cancellationToken).ConfigureAwait(false);
        return WriteThreadSummaryResult(thread, options.OutputJson, successText: "已创建新线程。", missingText: "创建线程失败。");
    }

    private async Task<int> RunThreadForkAsync(IConversationControlPlane controlPlane, ThreadCommandOptions options, CancellationToken cancellationToken)
    {
        var thread = await controlPlane.ForkThreadAsync(BuildControlPlaneThreadForkCommand(options), cancellationToken).ConfigureAwait(false);
        return WriteThreadSummaryResult(thread, options.OutputJson, successText: "已分叉线程。", missingText: "分叉线程失败。");
    }

    private static ControlPlaneStartThreadCommand BuildControlPlaneThreadStartCommand(ThreadCommandOptions options)
        => new()
        {
            Model = options.ThreadModel,
            ModelProvider = options.ThreadModelProvider,
            ServiceTier = SerializeThreadServiceTier(options.ThreadServiceTier),
            WorkingDirectory = options.ThreadWorkingDirectory,
            ApprovalPolicy = SerializeThreadApprovalPolicy(options.ThreadApprovalPolicy),
            SandboxMode = options.ThreadSandboxMode,
            Configuration = options.ThreadConfig is null
                ? null
                : new Dictionary<string, StructuredValue>(options.ThreadConfig, StringComparer.Ordinal),
            ServiceName = options.ThreadServiceName,
            BaseInstructions = options.ThreadBaseInstructions,
            DeveloperInstructions = options.ThreadDeveloperInstructions,
            Personality = options.ThreadPersonality,
            Ephemeral = options.ThreadEphemeral,
            DynamicTools = options.ThreadDynamicTools ?? options.DynamicTools,
            MockExperimentalField = null,
            PersistExtendedHistory = options.ThreadPersistExtendedHistory ?? false,
            ExperimentalRawEvents = options.ThreadExperimentalRawEvents,
        };

    private static ControlPlaneThreadListQuery BuildControlPlaneThreadListQuery(ThreadCommandOptions options)
        => new()
        {
            Limit = options.Limit,
            Cursor = options.Cursor,
            Archived = options.Archived,
            WorkingDirectory = options.MatchCurrentCwd ? options.WorkingDirectory : null,
            SortKey = options.SortKey,
            ModelProviders = options.ModelProviders.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            SourceKinds = options.SourceKinds.Distinct().ToArray(),
            SearchTerm = options.SearchTerm,
        };

    private static ControlPlaneForkThreadCommand BuildControlPlaneThreadForkCommand(ThreadCommandOptions options)
        => new()
        {
            ThreadId = new ThreadId(options.ThreadId!),
            Path = options.ThreadPath,
            Model = options.ThreadModel,
            ModelProvider = options.ThreadModelProvider,
            ServiceTier = SerializeThreadServiceTier(options.ThreadServiceTier),
            WorkingDirectory = options.ThreadWorkingDirectory,
            ApprovalPolicy = SerializeThreadApprovalPolicy(options.ThreadApprovalPolicy),
            SandboxMode = options.ThreadSandboxMode,
            Configuration = options.ThreadConfig is null
                ? null
                : new Dictionary<string, StructuredValue>(options.ThreadConfig, StringComparer.Ordinal),
            BaseInstructions = options.ThreadBaseInstructions,
            DeveloperInstructions = options.ThreadDeveloperInstructions,
            Ephemeral = options.ThreadEphemeral ?? false,
            PersistExtendedHistory = options.ThreadPersistExtendedHistory ?? false,
        };

    private static ControlPlaneResumeThreadCommand BuildControlPlaneResumeThreadCommand(ThreadCommandOptions options)
        => new()
        {
            ThreadId = new ThreadId(options.ThreadId!),
            History = options.ThreadHistory?.Select(ToControlPlaneThreadHistoryItem).ToArray(),
            Path = options.ThreadPath,
            Model = options.ThreadModel,
            ModelProvider = options.ThreadModelProvider,
            ServiceTier = SerializeThreadServiceTier(options.ThreadServiceTier),
            WorkingDirectory = options.ThreadWorkingDirectory,
            ApprovalPolicy = SerializeThreadApprovalPolicy(options.ThreadApprovalPolicy),
            SandboxMode = options.ThreadSandboxMode,
            Configuration = options.ThreadConfig is null
                ? null
                : new Dictionary<string, StructuredValue>(options.ThreadConfig, StringComparer.Ordinal),
            BaseInstructions = options.ThreadBaseInstructions,
            DeveloperInstructions = options.ThreadDeveloperInstructions,
            Personality = options.ThreadPersonality,
            PersistExtendedHistory = options.ThreadPersistExtendedHistory ?? false,
        };

    private static string? SerializeThreadServiceTier(CliServiceTierOverride serviceTier)
        => !serviceTier.IsSpecified ? null : serviceTier.IsCleared ? "null" : serviceTier.Value;

    private static string? SerializeThreadApprovalPolicy(string? approvalPolicy)
        => Normalize(approvalPolicy);

    private static ControlPlaneCompactThreadCommand BuildControlPlaneCompactThreadCommand(ThreadCommandOptions options)
        => new()
        {
            ThreadId = new ThreadId(options.ThreadId!),
            KeepRecentTurns = options.KeepRecentTurns,
        };

    private static ControlPlaneLoadedThreadListQuery BuildControlPlaneLoadedThreadListQuery(ThreadCommandOptions options)
        => new()
        {
            Limit = options.Limit,
            Cursor = options.Cursor,
        };

    private static ControlPlaneReadThreadQuery BuildControlPlaneReadThreadQuery(ThreadCommandOptions options)
        => new()
        {
            ThreadId = new ThreadId(options.ThreadId!),
            IncludeTurns = options.IncludeTurns,
        };

    private static ControlPlaneCleanBackgroundTerminalsCommand BuildControlPlaneCleanBackgroundTerminalsCommand(ThreadCommandOptions options)
        => new()
        {
            ThreadId = new ThreadId(options.ThreadId!),
        };

    private static ControlPlaneUnsubscribeThreadCommand BuildControlPlaneUnsubscribeThreadCommand(ThreadCommandOptions options)
        => new()
        {
            ThreadId = new ThreadId(options.ThreadId!),
        };

    private static ControlPlaneIncrementThreadElicitationCommand BuildControlPlaneIncrementThreadElicitationCommand(ThreadCommandOptions options)
        => new()
        {
            ThreadId = new ThreadId(options.ThreadId!),
        };

    private static ControlPlaneDecrementThreadElicitationCommand BuildControlPlaneDecrementThreadElicitationCommand(ThreadCommandOptions options)
        => new()
        {
            ThreadId = new ThreadId(options.ThreadId!),
        };

    private static ControlPlaneUnarchiveThreadCommand BuildControlPlaneUnarchiveThreadCommand(ThreadCommandOptions options)
        => new()
        {
            ThreadId = new ThreadId(options.ThreadId!),
        };

    private static ControlPlaneUpdateThreadMetadataCommand BuildControlPlaneUpdateThreadMetadataCommand(ThreadCommandOptions options)
        => new()
        {
            ThreadId = new ThreadId(options.ThreadId!),
            HasGitSha = options.ClearGitSha || !string.IsNullOrWhiteSpace(options.GitSha),
            GitSha = options.ClearGitSha ? null : options.GitSha,
            HasGitBranch = options.ClearGitBranch || !string.IsNullOrWhiteSpace(options.GitBranch),
            GitBranch = options.ClearGitBranch ? null : options.GitBranch,
            HasGitOriginUrl = options.ClearGitOriginUrl || !string.IsNullOrWhiteSpace(options.GitOriginUrl),
            GitOriginUrl = options.ClearGitOriginUrl ? null : options.GitOriginUrl,
        };

    private static ControlPlaneRollbackThreadCommand BuildControlPlaneRollbackThreadCommand(ThreadCommandOptions options)
        => new()
        {
            ThreadId = new ThreadId(options.ThreadId!),
            NumTurns = options.NumTurns!.Value,
        };

    private static ControlPlaneArchiveThreadCommand BuildControlPlaneArchiveThreadCommand(ThreadCommandOptions options)
        => new()
        {
            ThreadId = new ThreadId(options.ThreadId!),
        };

    private static ControlPlaneDeleteThreadCommand BuildControlPlaneDeleteThreadCommand(ThreadCommandOptions options)
        => new()
        {
            ThreadId = new ThreadId(options.ThreadId!),
        };

    private static ControlPlaneRenameThreadCommand BuildControlPlaneRenameThreadCommand(ThreadCommandOptions options)
        => new()
        {
            ThreadId = new ThreadId(options.ThreadId!),
            Name = options.Name!,
        };

    private async Task<int> RunThreadArchiveAsync(IConversationControlPlane controlPlane, ThreadCommandOptions options, CancellationToken cancellationToken)
    {
        var archived = await controlPlane.ArchiveThreadAsync(BuildControlPlaneArchiveThreadCommand(options), cancellationToken).ConfigureAwait(false);
        return WriteBooleanResult(archived, options.OutputJson, successText: "已归档线程。", failureText: "归档线程失败。");
    }

    private async Task<int> RunThreadDeleteAsync(IConversationControlPlane controlPlane, ThreadCommandOptions options, CancellationToken cancellationToken)
    {
        if (!ConfirmThreadDelete(options))
        {
            return WriteDestructiveOperationCancelled(options.OutputJson, "已取消删除线程。");
        }

        var deleted = await controlPlane.DeleteThreadAsync(BuildControlPlaneDeleteThreadCommand(options), cancellationToken).ConfigureAwait(false);
        if (deleted)
        {
            new TerminalInputHistoryStore().ClearThread(options.ThreadId);
        }

        return WriteBooleanResult(deleted, options.OutputJson, successText: "已删除线程。", failureText: "删除线程失败。");
    }

    private async Task<int> RunThreadClearAsync(IConversationControlPlane controlPlane, ThreadCommandOptions options, CancellationToken cancellationToken)
    {
        if (!ConfirmThreadClear(options))
        {
            return WriteDestructiveOperationCancelled(options.OutputJson, "已取消清空线程。");
        }

        var result = await controlPlane.ClearThreadsAsync(new ControlPlaneClearThreadsCommand(), cancellationToken).ConfigureAwait(false);
        new TerminalInputHistoryStore().ClearAll();
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { success = true, result.DeletedCount }, jsonOptions));
            return 0;
        }

        Console.WriteLine($"已清空线程：{result.DeletedCount} 个。");
        return 0;
    }

    private async Task<int> RunThreadRenameAsync(IConversationControlPlane controlPlane, ThreadCommandOptions options, CancellationToken cancellationToken)
    {
        var renamed = await controlPlane.RenameThreadAsync(BuildControlPlaneRenameThreadCommand(options), cancellationToken).ConfigureAwait(false);
        return WriteBooleanResult(renamed, options.OutputJson, successText: "已重命名线程。", failureText: "重命名线程失败。");
    }

    private async Task<int> RunThreadResumeAsync(IConversationControlPlane controlPlane, IGovernanceControlPlane governance, IExecutionRuntime runtime, ThreadCommandOptions options, CancellationToken cancellationToken)
    {
        ProbePermissionRequestScript? permissionScript = null;
        ProbeUserInputScript? userInputScript = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(options.PermissionsJsonPath))
            {
                permissionScript = ProbePermissionRequestScript.Load(options.PermissionsJsonPath);
            }

            if (!string.IsNullOrWhiteSpace(options.UserInputJsonPath))
            {
                userInputScript = ProbeUserInputScript.Load(options.UserInputJsonPath);
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or FormatException or JsonException)
        {
            if (options.OutputJson)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { success = false, error = ex.Message }, jsonOptions));
            }
            else
            {
                Console.Error.WriteLine(ex.Message);
            }

            return 1;
        }

        var replayState = new ThreadResumeReplayState(runtime, governance, options, permissionScript, userInputScript, cancellationToken);
        runtime.StreamEventReceived += replayState.OnStreamEvent;
        try
        {
        var resumed = await controlPlane.ResumeThreadAsync(BuildControlPlaneResumeThreadCommand(options), cancellationToken).ConfigureAwait(false);
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(ToCliThreadResumeResult(resumed), jsonOptions));
            if (resumed is null)
            {
                return 1;
            }

            replayState.ConsumeResumedThreadState(resumed);
            var settled = await replayState.WaitForSettledAsync(TimeSpan.FromMinutes(5)).ConfigureAwait(false);
            replayState.WriteErrors();
            return replayState.HasFailures || !settled ? 1 : 0;
        }

        if (resumed is null)
        {
            Console.Error.WriteLine("恢复线程失败。");
            return 1;
        }

        Console.WriteLine("已恢复线程。");
        Console.WriteLine($"线程：{resumed.Thread.ThreadId.Value}");
        Console.WriteLine($"标题：{ResolveThreadTitle(resumed.Thread.Name, resumed.Thread.Preview, resumed.Thread.ThreadId.Value)}");
        Console.WriteLine($"工作目录：{resumed.Thread.WorkingDirectory ?? "<none>"}");
        Console.WriteLine($"种子历史：{resumed.SeedHistory.Count}");
        Console.WriteLine($"回合数：{resumed.Turns.Count}");
        replayState.ConsumeResumedThreadState(resumed);
        if (replayState.ReplayedInteractiveRequestCount > 0)
        {
            Console.WriteLine(
                $"已回放待处理交互：审批 {replayState.ReplayedApprovalCount}，权限 {replayState.ReplayedPermissionCount}，补录 {replayState.ReplayedUserInputCount}。");
        }

        var settledHuman = await replayState.WaitForSettledAsync(TimeSpan.FromMinutes(5)).ConfigureAwait(false);
        replayState.WriteErrors();
        if (!settledHuman)
        {
            Console.Error.WriteLine("恢复线程后仍有未完成回合或未处理交互。");
        }

        return replayState.HasFailures || !settledHuman ? 1 : 0;
        }
        finally
        {
            runtime.StreamEventReceived -= replayState.OnStreamEvent;
        }
    }

    private async Task<int> RunThreadLoadedListAsync(IConversationControlPlane controlPlane, ThreadCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.ListLoadedThreadsAsync(
                BuildControlPlaneLoadedThreadListQuery(options),
                cancellationToken)
            .ConfigureAwait(false);

        return WriteThreadLoadedListResult(result, options.OutputJson);
    }

    private async Task<int> RunThreadReadAsync(IConversationControlPlane controlPlane, ThreadCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.ReadThreadAsync(
                BuildControlPlaneReadThreadQuery(options),
                cancellationToken)
            .ConfigureAwait(false);

        return WriteThreadOperationResult(result, options.OutputJson, "已读取线程。", "读取线程失败。");
    }

    private async Task<int> RunThreadCompactAsync(IConversationControlPlane controlPlane, ThreadCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.CompactThreadAsync(BuildControlPlaneCompactThreadCommand(options), cancellationToken).ConfigureAwait(false);
        return WriteThreadCommandAcceptedResult(result, options.OutputJson, "已启动线程压缩。");
    }

    private async Task<int> RunThreadCleanBackgroundTerminalsAsync(IConversationControlPlane controlPlane, ThreadCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.CleanBackgroundTerminalsAsync(
                BuildControlPlaneCleanBackgroundTerminalsCommand(options),
                cancellationToken)
            .ConfigureAwait(false);
        return WriteThreadCommandAcceptedResult(result, options.OutputJson, "已请求清理线程后台终端。");
    }

    private async Task<int> RunThreadUnsubscribeAsync(IConversationControlPlane controlPlane, ThreadCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.UnsubscribeThreadAsync(
                BuildControlPlaneUnsubscribeThreadCommand(options),
                cancellationToken)
            .ConfigureAwait(false);
        return WriteThreadUnsubscribeResult(result, options.OutputJson);
    }

    private async Task<int> RunThreadIncrementElicitationAsync(IConversationControlPlane controlPlane, ThreadCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.IncrementThreadElicitationAsync(
                BuildControlPlaneIncrementThreadElicitationCommand(options),
                cancellationToken)
            .ConfigureAwait(false);
        return WriteThreadElicitationResult(result, options.OutputJson, "已递增线程挂起交互计数。");
    }

    private async Task<int> RunThreadDecrementElicitationAsync(IConversationControlPlane controlPlane, ThreadCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.DecrementThreadElicitationAsync(
                BuildControlPlaneDecrementThreadElicitationCommand(options),
                cancellationToken)
            .ConfigureAwait(false);
        return WriteThreadElicitationResult(result, options.OutputJson, "已递减线程挂起交互计数。");
    }

    private async Task<int> RunThreadUnarchiveAsync(IConversationControlPlane controlPlane, ThreadCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.UnarchiveThreadAsync(
                BuildControlPlaneUnarchiveThreadCommand(options),
                cancellationToken)
            .ConfigureAwait(false);
        return WriteThreadOperationResult(result, options.OutputJson, "已取消线程归档。", "取消线程归档失败。");
    }

    private async Task<int> RunThreadMetadataAsync(IConversationControlPlane controlPlane, ThreadCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.UpdateThreadMetadataAsync(
                BuildControlPlaneUpdateThreadMetadataCommand(options),
                cancellationToken)
            .ConfigureAwait(false);

        return WriteThreadOperationResult(result, options.OutputJson, "已更新线程元数据。", "更新线程元数据失败。");
    }

    private async Task<int> RunThreadRollbackAsync(IConversationControlPlane controlPlane, ThreadCommandOptions options, CancellationToken cancellationToken)
    {
        var result = await controlPlane.RollbackThreadAsync(
                BuildControlPlaneRollbackThreadCommand(options),
                cancellationToken)
            .ConfigureAwait(false);

        return WriteThreadOperationResult(result, options.OutputJson, "已回滚线程。", "回滚线程失败。");
    }

    private int WriteThreadLoadedListResult(ControlPlaneLoadedThreadListResult result, bool outputJson)
    {
        if (outputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(ToCliThreadLoadedListResult(result), jsonOptions));
            return 0;
        }

        if (result.ThreadIds.Count == 0)
        {
            Console.WriteLine("未发现已加载线程。");
            return 0;
        }

        foreach (var threadId in result.ThreadIds)
        {
            Console.WriteLine(threadId.Value);
        }

        if (!string.IsNullOrWhiteSpace(result.NextCursor))
        {
            Console.WriteLine($"nextCursor={result.NextCursor}");
        }

        return 0;
    }

    private int WriteThreadLoadedListResult(JsonElement result)
    {
        if (!TryGetArray(result, "data", out var data) || data.GetArrayLength() == 0)
        {
            Console.WriteLine("未发现已加载线程。");
            return 0;
        }

        foreach (var threadId in ReadStringArray(data))
        {
            Console.WriteLine(threadId);
        }

        WriteNextCursor(result);
        return 0;
    }

    private static int WriteThreadCompactResult(JsonElement result)
        => WriteActionResult("已启动线程压缩。");

    private int WriteThreadUnsubscribeResult(ControlPlaneThreadUnsubscribeResult result, bool outputJson)
    {
        if (outputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(ToCliThreadUnsubscribeResult(result), jsonOptions));
            return 0;
        }

        Console.WriteLine(result.Status switch
        {
            "unsubscribed" => "已取消订阅线程。",
            "notSubscribed" => "当前连接未订阅该线程。",
            "notLoaded" => "线程当前未加载，无需取消订阅。",
            _ => $"已处理取消订阅请求。status={result.Status}",
        });
        return 0;
    }

    private int WriteThreadUnsubscribeResult(JsonElement result)
        => WriteThreadUnsubscribeResult(
            new ControlPlaneThreadUnsubscribeResult
            {
                Status = ReadString(result, "status") ?? "unknown",
            },
            outputJson: false);

    private int WriteThreadElicitationResult(ControlPlaneThreadElicitationResult result, bool outputJson, string successText)
    {
        if (outputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(ToCliThreadElicitationResult(result), jsonOptions));
            return 0;
        }

        Console.WriteLine(successText);
        Console.WriteLine($"计数：{result.Count}");
        Console.WriteLine($"paused：{(result.Paused ? "true" : "false")}");
        return 0;
    }

    private static CliThreadListResult ToCliThreadListResult(ControlPlaneThreadListResult result)
        => new()
        {
            Data = result.Threads.Select(ToCliThreadInfo).Where(static item => item is not null).Cast<CliThreadInfo>().ToArray(),
            NextCursor = result.NextCursor,
        };

    private static CliThreadResumeResult? ToCliThreadResumeResult(ControlPlaneThreadSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        var thread = snapshot.Thread;
        return new CliThreadResumeResult
        {
            ThreadId = thread.ThreadId.Value,
            Preview = thread.Preview,
            Name = thread.Name,
            Cwd = thread.WorkingDirectory,
            Path = thread.Path,
            ModelProvider = thread.ModelProvider,
            Source = ToCliSessionSource(thread.Source),
            CliVersion = thread.CliVersion,
            AgentNickname = thread.AgentNickname,
            AgentRole = thread.AgentRole,
            CreatedAt = thread.CreatedAt,
            UpdatedAt = thread.UpdatedAt,
            IsEphemeral = thread.IsEphemeral,
            Status = ToCliThreadStatus(thread.Status, thread.ActiveFlags),
            GitInfo = ToCliThreadGitInfo(thread.GitSha, thread.GitBranch, thread.GitOriginUrl),
            Turns = snapshot.Turns.Select(ToCliThreadTurn).ToArray(),
            SeedHistory = snapshot.SeedHistory.Select(ToCliThreadSeedHistoryItem).ToArray(),
            Messages = snapshot.Messages.Select(CloneConversationMessageForCli).ToArray(),
            PendingInputState = ToCliPendingInputState(snapshot.PendingInputState),
            PendingInteractiveRequests = snapshot.PendingInteractiveRequests.Select(ToCliInteractiveRequestReplay).ToArray(),
            SessionConfiguration = ToCliThreadSessionConfiguration(thread.SessionConfiguration),
        };
    }

    private static ThreadOperationCliResult ToCliThreadOperationResult(ControlPlaneThreadOperationResult result)
        => new()
        {
            Thread = result.Thread is null ? null : ToCliThreadDetails(result.Thread),
        };

    private static CliThreadDetails ToCliThreadDetails(ControlPlaneThreadDetail thread)
        => new()
        {
            Id = thread.ThreadId.Value,
            Preview = thread.Preview,
            Name = thread.Name,
            Cwd = thread.WorkingDirectory,
            Path = thread.Path,
            ModelProvider = thread.ModelProvider,
            Source = ToCliSessionSource(thread.Source),
            CliVersion = thread.CliVersion,
            AgentNickname = thread.AgentNickname,
            AgentRole = thread.AgentRole,
            CreatedAt = thread.CreatedAt,
            UpdatedAt = thread.UpdatedAt,
            Ephemeral = thread.IsEphemeral,
            Status = ToCliThreadStatus(thread.Status, thread.ActiveFlags),
            GitInfo = ToCliThreadGitInfo(thread.GitSha, thread.GitBranch, thread.GitOriginUrl),
            Turns = thread.Turns.Select(ToCliThreadTurn).ToArray(),
            SeedHistory = thread.SeedHistory.Select(ToCliThreadSeedHistoryItem).ToArray(),
            PendingInputState = ToCliPendingInputState(thread.PendingInputState),
            PendingInteractiveRequests = thread.PendingInteractiveRequests.Select(ToCliInteractiveRequestReplay).ToArray(),
            SessionConfiguration = ToCliThreadSessionConfiguration(thread.SessionConfiguration),
        };

    private static ThreadLoadedListCliResult ToCliThreadLoadedListResult(ControlPlaneLoadedThreadListResult result)
        => new()
        {
            Data = result.ThreadIds.Select(static item => item.Value).ToArray(),
            NextCursor = result.NextCursor,
        };

    private static ThreadUnsubscribeCliResult ToCliThreadUnsubscribeResult(ControlPlaneThreadUnsubscribeResult result)
        => new()
        {
            Status = result.Status,
        };

    private static ThreadElicitationCliResult ToCliThreadElicitationResult(ControlPlaneThreadElicitationResult result)
        => new()
        {
            Count = result.Count,
            Paused = result.Paused,
        };

    private static ThreadCommandAcceptedCliResult ToCliThreadCommandAcceptedResult(ControlPlaneThreadCommandAcceptedResult _)
        => new();

    private static CliThreadInfo? ToCliThreadInfo(ControlPlaneThreadSummary? thread)
    {
        if (thread is null)
        {
            return null;
        }

        return new CliThreadInfo
        {
            ThreadId = thread.ThreadId.Value,
            Preview = thread.Preview,
            Name = thread.Name,
            Cwd = thread.WorkingDirectory,
            Path = thread.Path,
            ModelProvider = thread.ModelProvider,
            Source = ToCliSessionSource(thread.Source),
            CliVersion = thread.CliVersion,
            AgentNickname = thread.AgentNickname,
            AgentRole = thread.AgentRole,
            CreatedAt = thread.CreatedAt,
            UpdatedAt = thread.UpdatedAt,
            IsEphemeral = thread.IsEphemeral,
            Status = ToCliThreadStatus(thread.Status, thread.ActiveFlags),
            GitInfo = ToCliThreadGitInfo(thread.GitSha, thread.GitBranch, thread.GitOriginUrl),
            SessionConfiguration = ToCliThreadSessionConfiguration(thread.SessionConfiguration),
        };
    }

    private static CliThreadSessionConfiguration? ToCliThreadSessionConfiguration(ControlPlaneThreadSessionConfiguration? configuration)
    {
        if (configuration is null)
        {
            return null;
        }

        return new CliThreadSessionConfiguration
        {
            Model = configuration.Model,
            ModelProvider = configuration.ModelProvider,
            ModelProviderId = configuration.ModelProviderId,
            ServiceTier = Normalize(configuration.ServiceTier),
            ApprovalPolicy = Normalize(configuration.ApprovalPolicy),
            SandboxPolicy = configuration.SandboxPolicy,
            SandboxPolicyPayload = configuration.SandboxPolicyPayload,
            ReasoningEffort = configuration.ReasoningEffort,
            HistoryLogId = configuration.HistoryLogId,
            HistoryEntryCount = configuration.HistoryEntryCount,
            RolloutPath = configuration.RolloutPath,
            ReasoningSummary = configuration.ReasoningSummary,
            Verbosity = configuration.Verbosity,
            Personality = configuration.Personality,
            AllowLoginShell = configuration.AllowLoginShell,
            ShellEnvironmentPolicy = configuration.ShellEnvironmentPolicy,
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
            DynamicTools = configuration.DynamicTools?.ToArray(),
            CollaborationMode = configuration.CollaborationMode,
            PersistExtendedHistory = configuration.PersistExtendedHistory,
            ForkedFromId = configuration.ForkedFromThreadId?.Value,
            Cwd = configuration.WorkingDirectory,
            SessionSource = configuration.SessionSource,
            WindowsSandboxLevel = configuration.WindowsSandboxLevel,
            DefaultModeRequestUserInputEnabled = configuration.DefaultModeRequestUserInputEnabled,
        };
    }

    private static ControlPlaneConversationMessage CloneConversationMessageForCli(ControlPlaneConversationMessage message)
        => new()
        {
            Role = message.Role,
            Content = message.Content,
            ContentItems = message.ContentItems.ToArray(),
            Timestamp = message.Timestamp,
            IsStreaming = message.IsStreaming,
        };

    private static CliThreadStatus? ToCliThreadStatus(string? status, IReadOnlyList<string> activeFlags)
        => string.IsNullOrWhiteSpace(status)
            ? null
            : new CliThreadStatus
            {
                Type = status,
                ActiveFlags = activeFlags,
            };

    private static CliThreadGitInfo? ToCliThreadGitInfo(string? gitSha, string? gitBranch, string? gitOriginUrl)
        => string.IsNullOrWhiteSpace(gitSha)
           && string.IsNullOrWhiteSpace(gitBranch)
           && string.IsNullOrWhiteSpace(gitOriginUrl)
            ? null
            : new CliThreadGitInfo
            {
                Sha = gitSha,
                Branch = gitBranch,
                OriginUrl = gitOriginUrl,
            };

    private static CliThreadTurn ToCliThreadTurn(ControlPlaneThreadTurn turn)
        => new()
        {
            Id = turn.Id,
            Status = turn.Status,
            Error = turn.Error is null
                ? null
                : new CliThreadTurnError
                {
                    Message = turn.Error.Message,
                    AdditionalDetails = turn.Error.AdditionalDetails,
                },
            Items = turn.Items.Select(ToCliThreadTurnItem).ToArray(),
        };

    private static CliThreadTurnItem ToCliThreadTurnItem(ControlPlaneThreadTurnItem item)
        => new CliGenericThreadTurnItem
        {
            Id = item.Id,
            Type = item.Type,
            RawText = item.Text,
            ItemPhase = item.Phase,
            RawData = item.Data,
        };

    private static CliThreadSeedHistoryItem ToCliThreadSeedHistoryItem(ControlPlaneSeedHistoryItem item)
        => new()
        {
            Role = item.Role,
            Content = item.Content,
            Inputs = item.Inputs.ToArray(),
        };

    private static CliPendingInputStatePayload? ToCliPendingInputState(ControlPlanePendingInputState? payload)
        => CliInteractiveStateConverters.ToCliPendingInputStatePayload(payload);

    private static CliInteractiveRequestReplay ToCliInteractiveRequestReplay(ControlPlanePendingInteractiveRequest request)
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
            AvailableDecisions = request.AvailableDecisions?.ToArray(),
            AvailableDecisionOptions = request.AvailableDecisionOptions?.Select(CliApprovalDecisionOptionPayload.FromControlPlane).OfType<CliApprovalDecisionOptionPayload>().ToArray(),
        };

    private static ControlPlaneStructuredValue ToControlPlaneThreadHistoryItem(StructuredValue item)
        => ControlPlaneStructuredValue.FromPlainObject(item.ToPlainObject());

    private static ControlPlaneSessionSource? ToCliSessionSource(ControlPlaneThreadSourceKind? source)
        => source?.Value switch
        {
            "cli" => ControlPlaneSessionSource.Cli,
            "vscode" => ControlPlaneSessionSource.VsCode,
            "exec" => ControlPlaneSessionSource.Exec,
            "appServer" => ControlPlaneSessionSource.AppServer,
            "subAgentReview" => ControlPlaneSessionSource.SubAgent(ControlPlaneSubAgentSource.Review),
            "subAgentCompact" => ControlPlaneSessionSource.SubAgent(ControlPlaneSubAgentSource.Compact),
            "subAgent" => ControlPlaneSessionSource.SubAgent(ControlPlaneSubAgentSource.MemoryConsolidation),
            "subAgentOther" => ControlPlaneSessionSource.SubAgent(ControlPlaneSubAgentSource.Other("unknown")),
            "subAgentThreadSpawn" => ControlPlaneSessionSource.SubAgent(ControlPlaneSubAgentSource.Other("thread_spawn")),
            _ => source is null ? null : ControlPlaneSessionSource.TryParse(source.Value, out var parsed) ? parsed : null,
        };

    private int WriteThreadCommandAcceptedResult(ControlPlaneThreadCommandAcceptedResult result, bool outputJson, string successText)
    {
        if (outputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(ToCliThreadCommandAcceptedResult(result), jsonOptions));
            return 0;
        }

        Console.WriteLine(successText);
        return 0;
    }

    private int WriteThreadOperationResult(ControlPlaneThreadOperationResult result, bool outputJson, string successText, string missingText)
    {
        if (outputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(ToCliThreadOperationResult(result), jsonOptions));
            return result.Thread is null ? 1 : 0;
        }

        if (result.Thread is null)
        {
            Console.Error.WriteLine(missingText);
            return 1;
        }

        Console.WriteLine(successText);
        Console.WriteLine($"线程：{result.Thread.ThreadId.Value}");
        Console.WriteLine($"标题：{ResolveThreadTitle(result.Thread.Name, result.Thread.Preview, result.Thread.ThreadId.Value)}");
        Console.WriteLine($"工作目录：{result.Thread.WorkingDirectory ?? "<none>"}");
        Console.WriteLine($"轮次：{result.Thread.Turns.Count}");
        return 0;
    }

    private int WriteAgentThreadRegistrationResult(
        ControlPlaneAgentThreadRegistrationResult result,
        bool outputJson,
        string successText,
        string missingText)
    {
        if (outputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(BuildAgentThreadRegistrationJson(result), jsonOptions));
            return result.Agent is null ? 1 : 0;
        }

        if (result.Agent is null)
        {
            Console.Error.WriteLine(missingText);
            return 1;
        }

        Console.WriteLine(successText);
        Console.WriteLine($"线程：{result.Agent.ThreadId.Value}");
        Console.WriteLine($"标题：{ResolveThreadTitle(result.Agent.Name, result.Agent.Preview, result.Agent.ThreadId.Value)}");
        Console.WriteLine($"工作目录：{result.Agent.WorkingDirectory ?? "<none>"}");
        return 0;
    }

    private int WriteThreadEnvelopeResult(JsonElement result, string successText)
    {
        if (!TryGetProperty(result, "thread", out var thread) || thread.ValueKind != JsonValueKind.Object)
        {
            return WriteJsonFallback(result);
        }

        var turns = TryGetProperty(thread, "turns", out var turnArray) && turnArray.ValueKind == JsonValueKind.Array
            ? turnArray.GetArrayLength()
            : 0;

        Console.WriteLine(successText);
        var threadId = ReadString(thread, "id") ?? "<unknown>";
        Console.WriteLine($"线程：{threadId}");
        Console.WriteLine($"标题：{ResolveThreadTitle(ReadString(thread, "name"), ReadString(thread, "preview"), threadId)}");
        Console.WriteLine($"工作目录：{ReadString(thread, "cwd") ?? "<none>"}");
        Console.WriteLine($"轮次：{turns}");
        return 0;
    }


    private int WriteRuntimeSurfaceAgentJobResult(RuntimeSurfaceCommandOptions options, ControlPlaneJobOperationResult result)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(BuildAgentJobJson(result), jsonOptions));
            return 0;
        }

        return options.CommandKind switch
        {
            RuntimeSurfaceCommandKind.AgentJobCreate => WriteAgentJobEnvelopeResult(result, "已创建 Agent Job。"),
            RuntimeSurfaceCommandKind.AgentJobDispatch => WriteAgentJobDispatchResult(result),
            RuntimeSurfaceCommandKind.AgentJobItemReport => WriteAgentJobReportResult(result),
            RuntimeSurfaceCommandKind.AgentJobRead => WriteAgentJobEnvelopeResult(result, "已读取 Agent Job。"),
            _ => 2,
        };
    }

    private static object BuildAgentThreadRegistrationJson(ControlPlaneAgentThreadRegistrationResult result)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["thread"] = result.Agent is null ? null : BuildAgentThreadJson(result.Agent),
        };

    private static object BuildAgentThreadJson(ControlPlaneAgentDescriptor agent)
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

    private int WriteAgentJobEnvelopeResult(ControlPlaneJobOperationResult result, string successText)
    {
        if (result.Job is null)
        {
            Console.Error.WriteLine("Agent Job 返回缺少 job。");
            return 1;
        }

        Console.WriteLine(successText);
        Console.WriteLine($"Job：{result.Job.Id.Value}");
        Console.WriteLine($"名称：{result.Job.Name ?? "<unnamed>"}");
        Console.WriteLine($"状态：{result.Job.Status ?? "<unknown>"}");
        Console.WriteLine($"指令：{result.Job.Instruction ?? string.Empty}");
        Console.WriteLine($"条目数：{result.Items.Count}");
        return 0;
    }

    private int WriteAgentJobDispatchResult(ControlPlaneJobOperationResult result)
    {
        if (result.Items.Count == 0)
        {
            Console.Error.WriteLine("Agent Job 分发结果缺少 items。");
            return 1;
        }

        Console.WriteLine("已分发 Agent Job 条目。");
        foreach (var item in result.Items)
        {
            Console.WriteLine($"{item.ItemId.Value}	{item.ThreadId?.Value ?? item.AssignedThreadId?.Value ?? "<none>"}	{item.Status ?? "<unknown>"}");
        }

        return 0;
    }

    private int WriteAgentJobReportResult(ControlPlaneJobOperationResult result)
    {
        if (result.Item is null)
        {
            Console.Error.WriteLine("Agent Job 条目上报结果缺少 item。");
            return 1;
        }

        Console.WriteLine("已上报 Agent Job 条目结果。");
        if (result.Job is not null)
        {
            Console.WriteLine($"Job：{result.Job.Id.Value}");
        }

        Console.WriteLine($"条目：{result.Item.ItemId.Value}");
        Console.WriteLine($"状态：{result.Item.Status ?? "<unknown>"}");
        if (!string.IsNullOrWhiteSpace(result.Item.LastError))
        {
            Console.WriteLine($"错误：{result.Item.LastError}");
        }

        return 0;
    }

    private static object BuildAgentJobJson(ControlPlaneJobOperationResult result)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["job"] = result.Job is null ? null : BuildAgentJobJson(result.Job),
            ["items"] = result.Items.Select(BuildAgentJobItemJson).ToArray(),
            ["item"] = result.Item is null ? null : BuildAgentJobItemJson(result.Item),
        };

    private static object BuildAgentJobJson(ControlPlaneJobDetails job)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = job.Id.Value,
            ["name"] = job.Name,
            ["status"] = job.Status,
            ["instruction"] = job.Instruction,
        };

    private static object BuildAgentJobItemJson(TianShu.Contracts.Workflows.ControlPlaneJobItemDetails item)
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

    private static ControlPlaneStructuredValue? ToControlPlaneStructuredValue(StructuredValue? value)
        => value is null ? null : ControlPlaneStructuredValue.FromPlainObject(value.ToPlainObject());

    private int WriteRuntimeSurfaceResult(RuntimeSurfaceCommandOptions options, JsonElement result)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
            return 0;
        }

        return options.CommandKind switch
        {
            RuntimeSurfaceCommandKind.FeatureConfigWrite => WriteConfigMutationResult(result),
            RuntimeSurfaceCommandKind.ConfigValueWrite => WriteConfigMutationResult(result),
            RuntimeSurfaceCommandKind.ConfigBatchWrite => WriteConfigMutationResult(result),
            _ => WriteJsonFallback(result),
        };
    }

    private int WriteRuntimeSurfaceObjectResult(RuntimeSurfaceCommandOptions options, object? result)
    {
        if (!options.OutputJson)
        {
            return result switch
            {
                ExecutionTrace trace => WriteExecutionTraceResult(trace),
                IReadOnlyList<AttemptSummary> attempts => WriteAttemptSummaryListResult(attempts),
                ApprovalQueueProjection approvals => WriteApprovalQueueResult(approvals),
                IReadOnlyList<UserInputRequest> requests => WriteUserInputRequestListResult(requests),
                _ => WriteJsonObjectFallback(result),
            };
        }

        return WriteJsonObjectFallback(result);
    }

    private int WriteJsonObjectFallback(object? result)
    {
        Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
        return 0;
    }

    private int WriteMemoryResult(RuntimeSurfaceCommandOptions options, object? result)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
            return 0;
        }

        return result switch
        {
            IReadOnlyList<MemoryProviderDescriptor> providers => WriteMemoryProvidersResult(providers),
            IReadOnlyList<MemorySpace> spaces => WriteMemorySpacesResult(spaces),
            MemoryOverlay overlay => WriteMemoryOverlayResult(overlay),
            MemoryQueryResult query => WriteMemoryQueryResult(query),
            MemoryReviewQueryResult reviews => WriteMemoryReviewQueryResult(reviews),
            MemoryMutationResult mutation => WriteMemoryMutationResult(mutation),
            MemoryConsolidationRunResult consolidation => WriteMemoryConsolidationResult(consolidation),
            IReadOnlyList<MemoryCandidate> candidates => WriteMemoryCandidatesResult(candidates),
            _ => WriteJsonObjectFallback(result),
        };
    }

    private static int WriteExecutionTraceResult(ExecutionTrace trace)
    {
        Console.WriteLine($"执行追踪：{trace.Id.Value}");
        Console.WriteLine($"Execution：{trace.ExecutionId.Value}");
        Console.WriteLine($"Attempts：{trace.Attempts.Count}  Events：{trace.AuditTrail.Count}  Checkpoints：{trace.RecoveryCheckpoints.Count}");
        if (trace.Attempts.Count > 0)
        {
            WriteAttemptSummaryRows(trace.Attempts);
        }

        if (trace.AuditTrail.Count > 0)
        {
            Console.WriteLine("关键事件：");
            WriteAlignedRows(
                ["Time", "Category", "Message", "Details"],
                trace.AuditTrail
                    .OrderBy(static item => item.Timestamp)
                    .Select(static item => new[]
                    {
                        item.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        item.Category,
                        item.Message,
                        BuildExecutionTraceAuditDetails(item),
                    })
                    .ToArray());
        }

        if (trace.RecoveryCheckpoints.Count > 0)
        {
            Console.WriteLine("恢复检查点：");
            WriteAlignedRows(
                ["Time", "Stage"],
                trace.RecoveryCheckpoints
                    .OrderBy(static item => item.CreatedAt)
                    .Select(static item => new[]
                    {
                        item.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        item.Stage,
                    })
                    .ToArray());
        }

        return 0;
    }

    private static string BuildExecutionTraceAuditDetails(AuditRecord record)
    {
        var details = new List<string>();
        AddTraceDetail(details, record.Metadata, "seq", "providerRequestSequence");
        AddTraceDetail(details, record.Metadata, "op", "operationId");
        AddTraceDetail(details, record.Metadata, "method", "method");
        AddTraceDetail(details, record.Metadata, "call", "callId");
        AddTraceDetail(details, record.Metadata, "payloadTokens", "estimatedPayloadTokens");
        AddTraceDetail(details, record.Metadata, "payloadChars", "serializedPayloadChars");
        AddTraceDetail(details, record.Metadata, "includedTokens", "estimatedIncludedTokens");
        AddTraceDetail(details, record.Metadata, "totalTokens", "estimatedTotalTokens");
        AddTraceDetail(details, record.Metadata, "artifact", "artifactFileName");
        AddTraceDetail(details, record.Metadata, "path", "artifactRelativePath");
        AddTraceDetail(details, record.Metadata, "payloadError", "payloadError");
        return details.Count == 0 ? string.Empty : string.Join(" | ", details);
    }

    private static void AddTraceDetail(List<string> details, MetadataBag metadata, string label, string key)
    {
        if (!metadata.TryGetValue(key, out var value))
        {
            return;
        }

        var text = value.Kind switch
        {
            StructuredValueKind.String => value.StringValue,
            StructuredValueKind.Number => value.NumberValue,
            StructuredValueKind.Boolean => value.BooleanValue?.ToString(),
            _ => null,
        };
        if (!string.IsNullOrWhiteSpace(text))
        {
            details.Add($"{label}={text}");
        }
    }

    private static int WriteAttemptSummaryListResult(IReadOnlyList<AttemptSummary> attempts)
    {
        if (attempts.Count == 0)
        {
            Console.WriteLine("未发现执行尝试记录。");
            return 0;
        }

        Console.WriteLine($"执行尝试：{attempts.Count} 条");
        WriteAttemptSummaryRows(attempts);
        return 0;
    }

    private static void WriteAttemptSummaryRows(IReadOnlyList<AttemptSummary> attempts)
        => WriteAlignedRows(
            ["Execution", "Attempt", "Status", "Started", "Completed"],
            attempts
                .OrderBy(static item => item.AttemptNumber)
                .ThenBy(static item => item.StartedAt)
                .Select(static item => new[]
                {
                    item.ExecutionId.Value,
                    item.AttemptNumber.ToString(CultureInfo.InvariantCulture),
                    item.Succeeded ? "succeeded" : "failed",
                    item.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    item.CompletedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "<pending>",
                })
                .ToArray());

    private static int WriteApprovalQueueResult(ApprovalQueueProjection approvals)
    {
        if (approvals.Items.Count == 0)
        {
            Console.WriteLine("当前没有待审批请求。");
            return 0;
        }

        Console.WriteLine($"待审批请求：{approvals.Items.Count} 条");
        WriteAlignedRows(
            ["Approval", "Title", "Reason", "RequestedFrom", "RequestedAt"],
            approvals.Items
                .OrderBy(static item => item.RequestedAt)
                .Select(static item => new[]
                {
                    item.ApprovalId.Value,
                    item.Title,
                    item.Reason,
                    item.RequestedFrom.DisplayName,
                    item.RequestedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                })
                .ToArray());
        return 0;
    }

    private static int WriteUserInputRequestListResult(IReadOnlyList<UserInputRequest> requests)
    {
        if (requests.Count == 0)
        {
            Console.WriteLine("当前没有待补充输入请求。");
            return 0;
        }

        Console.WriteLine($"待补充输入请求：{requests.Count} 条");
        WriteAlignedRows(
            ["Request", "Prompt", "Status", "RequestedFrom", "RequestedAt"],
            requests
                .OrderBy(static item => item.RequestedAt)
                .Select(static item => new[]
                {
                    item.Id.Value,
                    item.Prompt,
                    item.Status.ToString(),
                    item.RequestedFromParticipant.DisplayName,
                    item.RequestedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                })
                .ToArray());
        return 0;
    }

    private int WriteMemoryMutationResult(
        RuntimeSurfaceCommandOptions options,
        MemoryMutationResult result,
        MemorySpaceId? memorySpaceId,
        string? key,
        decimal? confidence = null,
        MemorySourceRef? source = null)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
            return result.Success ? 0 : 1;
        }

        return WriteMemoryMutationResult(result, memorySpaceId, key, confidence, source);
    }

    private static int WriteMemoryProvidersResult(IReadOnlyList<MemoryProviderDescriptor> providers)
    {
        if (providers.Count == 0)
        {
            Console.WriteLine("未发现可用记忆 Provider。");
            return 0;
        }

        Console.WriteLine($"记忆 Provider：{providers.Count} 个");
        WriteAlignedRows(
            ["Provider", "Name", "Capabilities", "Scopes", "Trust"],
            providers
                .OrderBy(static provider => provider.ProviderId, StringComparer.OrdinalIgnoreCase)
                .Select(static provider => new[]
                {
                    provider.ProviderId,
                    provider.DisplayName,
                    provider.Capabilities.ToString(),
                    provider.SupportedScopes.Count == 0 ? "<any>" : string.Join(",", provider.SupportedScopes),
                    provider.TrustLevel.ToString(),
                })
                .ToArray());
        return 0;
    }

    private static int WriteMemorySpacesResult(IReadOnlyList<MemorySpace> spaces)
    {
        if (spaces.Count == 0)
        {
            Console.WriteLine("未发现记忆空间。");
            return 0;
        }

        Console.WriteLine($"记忆空间：{spaces.Count} 个");
        WriteAlignedRows(
            ["Space", "Scope", "Key", "Name", "Mode"],
            spaces
                .OrderBy(static space => space.ScopeKind)
                .ThenBy(static space => space.Id.Value, StringComparer.OrdinalIgnoreCase)
                .Select(static space => new[]
                {
                    space.Id.Value,
                    space.ScopeKind.ToString(),
                    space.ScopeKey,
                    space.DisplayName,
                    space.IsReadOnly ? "read-only" : "read-write",
                })
                .ToArray());
        return 0;
    }

    private int WriteMemoryOverlayResult(MemoryOverlay overlay)
    {
        Console.WriteLine($"记忆 Overlay：{overlay.Facts.Count} 条事实，merge={overlay.MergeDecision}");
        WriteMemoryFacts(overlay.Facts);
        if (overlay.HabitProfile is not null)
        {
            Console.WriteLine($"习惯画像：verbosity={overlay.HabitProfile.PreferredVerbosity ?? "<none>"} tools={FormatStringList(overlay.HabitProfile.PreferredTools)}");
        }

        return 0;
    }

    private int WriteMemoryQueryResult(MemoryQueryResult result)
    {
        Console.WriteLine($"记忆查询：{result.Records.Count} / {result.TotalCount} 条");
        WriteMemoryFacts(result.Records);
        if (result.Citation is not null && result.Citation.Entries.Count > 0)
        {
            Console.WriteLine($"引用：{result.Citation.Entries.Count} 条");
            foreach (var entry in result.Citation.Entries)
            {
                Console.WriteLine($"  - {entry.MemoryRecordId.Value}  space={entry.MemorySpaceId.Value}  key={entry.Key}  note={entry.Note ?? "<none>"}");
            }
        }

        if (result.DegradedProviders.Count > 0)
        {
            Console.WriteLine($"降级 Provider：{string.Join(", ", result.DegradedProviders)}");
        }

        return 0;
    }

    private static int WriteMemoryReviewQueryResult(MemoryReviewQueryResult result)
    {
        Console.WriteLine($"记忆审核项：{result.Items.Count} / {result.TotalCount} 条");
        if (result.Items.Count == 0)
        {
            Console.WriteLine("当前没有匹配的记忆审核项。");
        }
        else
        {
            WriteAlignedRows(
                ["Record", "Space", "Key", "Status", "Confidence", "Evidence", "Links", "Audit"],
                result.Items
                    .OrderBy(static item => item.Record.RecordedAt)
                    .ThenBy(static item => item.Record.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(static item => new[]
                    {
                        item.Record.Id.Value,
                        item.Record.MemorySpaceId.Value,
                        item.Record.Key,
                        item.Record.LifecycleStatus.ToString(),
                        FormatConfidence(item.Record.Confidence),
                        (item.Evidence.Count + item.Record.ValidationEvidence.Count).ToString(CultureInfo.InvariantCulture),
                        item.SupersedeLinks.Count.ToString(CultureInfo.InvariantCulture),
                        item.Audit.Count == 0 ? "<none>" : string.Join(",", item.Audit.Take(2).Select(static audit => audit.Operation)),
                    })
                    .ToArray());
        }

        if (result.DegradedProviders.Count > 0)
        {
            Console.WriteLine($"降级 Provider：{string.Join(", ", result.DegradedProviders)}");
        }

        return 0;
    }

    private static int WriteMemoryMutationResult(
        MemoryMutationResult result,
        MemorySpaceId? memorySpaceId = null,
        string? key = null,
        decimal? confidence = null,
        MemorySourceRef? source = null)
    {
        Console.WriteLine(result.Success ? "记忆操作成功。" : "记忆操作失败。");
        if (result.RecordId is not null)
        {
            Console.WriteLine($"Record：{result.RecordId.Value}");
        }

        if (memorySpaceId is not null)
        {
            Console.WriteLine($"Space：{memorySpaceId.Value}");
            Console.WriteLine($"Scope：{InferMemoryScope(memorySpaceId.Value)}");
        }

        if (!string.IsNullOrWhiteSpace(key))
        {
            Console.WriteLine($"Key：{key}");
        }

        if (confidence is not null)
        {
            Console.WriteLine($"Confidence：{FormatConfidence(confidence.Value)}");
        }

        if (source is not null)
        {
            Console.WriteLine($"Source：{FormatMemorySource(source)}");
        }

        Console.WriteLine($"状态：{result.LifecycleStatus?.ToString() ?? "<unknown>"}");
        Console.WriteLine($"效果：{result.Effect}");
        if (!string.IsNullOrWhiteSpace(result.DegradedReason))
        {
            Console.WriteLine($"降级原因：{FormatMemoryDegradedReason(result.DegradedReason)}");
        }

        if (result.UnsupportedCapability is not null)
        {
            Console.WriteLine($"不支持能力：{result.UnsupportedCapability}");
        }

        if (result.SupersedeLink is not null)
        {
            Console.WriteLine($"取代关系：{result.SupersedeLink.OldRecordId.Value} -> {result.SupersedeLink.NewRecordId.Value}");
            Console.WriteLine($"原因：{result.SupersedeLink.Reason}");
        }

        return result.Success ? 0 : 1;
    }

    private static int WriteMemoryConsolidationResult(MemoryConsolidationRunResult result)
    {
        Console.WriteLine(result.SkippedByLease ? "记忆整理已跳过：已有有效租约。" : "记忆整理已完成。");
        Console.WriteLine($"扫描候选：{result.CandidatesScanned}");
        Console.WriteLine($"生成 proposal：{result.ProposalsCreated}");
        Console.WriteLine($"Lease：{(result.LeaseAcquired ? "acquired" : "not-acquired")}");
        Console.WriteLine($"Cooldown 跳过：{result.CandidatesSkippedByCooldown}");
        Console.WriteLine($"延迟重试：{result.RetriesDeferred}");
        Console.WriteLine($"失败记录：{result.FailuresRecorded}");
        Console.WriteLine($"权限边界：{result.PermissionBoundary}");
        return 0;
    }

    private static string FormatMemoryDegradedReason(string reason)
        => string.Equals(reason, "memory_space_read_only", StringComparison.Ordinal)
            ? "目标记忆空间是只读空间。请通过 /memory spaces 查看 read-write 目标，通常优先写入当前 workspace，其次 user 或 session。"
            : reason;

    private int WriteMemoryCandidatesResult(IReadOnlyList<MemoryCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            Console.WriteLine("未抽取到候选记忆。");
            return 0;
        }

        Console.WriteLine($"候选记忆：{candidates.Count} 条");
        foreach (var candidate in candidates.OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"- space={candidate.MemorySpaceId.Value} scope={InferMemoryScope(candidate.MemorySpaceId)} key={candidate.Key}");
            Console.WriteLine($"  value={FormatStructuredValue(candidate.Value)}");
            Console.WriteLine($"  confidence={FormatConfidence(candidate.Confidence)} status={candidate.LifecycleStatus} formation={candidate.FormationPath} counterexample={(candidate.IsCounterexample ? "yes" : "no")}");
            Console.WriteLine($"  source={FormatMemorySource(candidate.Source)}");
            if (!string.IsNullOrWhiteSpace(candidate.ExtractionReason))
            {
                Console.WriteLine($"  reason={candidate.ExtractionReason}");
            }
        }

        return 0;
    }

    private void WriteMemoryFacts(IReadOnlyList<FactMemoryRecord> facts)
    {
        if (facts.Count == 0)
        {
            Console.WriteLine("未找到记忆记录。");
            return;
        }

        foreach (var fact in facts.OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"- {fact.Id.Value}");
            Console.WriteLine($"  space={fact.MemorySpaceId.Value} scope={InferMemoryScope(fact.MemorySpaceId)} key={fact.Key}");
            Console.WriteLine($"  value={FormatStructuredValue(fact.Value)}");
            Console.WriteLine($"  confidence={FormatConfidence(fact.Confidence)} status={fact.LifecycleStatus} usage={fact.UsageCount}");
            Console.WriteLine($"  source={FormatMemorySources(fact.Sources)}");
            if (fact.Tags.Count > 0)
            {
                Console.WriteLine($"  tags={string.Join(",", fact.Tags)}");
            }
        }
    }

    private string FormatStructuredValue(StructuredValue value)
    {
        var text = value.Kind switch
        {
            StructuredValueKind.String => value.StringValue ?? string.Empty,
            StructuredValueKind.Number => value.NumberValue ?? string.Empty,
            StructuredValueKind.Boolean => value.BooleanValue?.ToString() ?? "false",
            StructuredValueKind.Null => "null",
            _ => JsonSerializer.Serialize(value, jsonOptions),
        };
        return TruncateSingleLine(text, 160);
    }

    private static string FormatMemorySources(IReadOnlyList<MemorySourceRef> sources)
        => sources.Count == 0
            ? "<none>"
            : string.Join("; ", sources.Take(3).Select(FormatMemorySource));

    private static string FormatMemorySource(MemorySourceRef? source)
    {
        if (source is null)
        {
            return "<none>";
        }

        var snippet = string.IsNullOrWhiteSpace(source.Snippet)
            ? string.Empty
            : $" snippet={TruncateSingleLine(source.Snippet, 80)}";
        var role = string.IsNullOrWhiteSpace(source.Role)
            ? string.Empty
            : $" role={source.Role}";
        return $"{source.SourceKind}:{source.SourceId}{role}{snippet}";
    }

    private static string InferMemoryScope(MemorySpaceId memorySpaceId)
    {
        var parts = memorySpaceId.Value.Split(':', 3, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && string.Equals(parts[0], "memory", StringComparison.OrdinalIgnoreCase)
            ? parts[1]
            : "<unknown>";
    }

    private static string FormatConfidence(decimal confidence)
        => confidence.ToString("0.###", CultureInfo.InvariantCulture);

    private static string FormatStringList(IReadOnlyList<string> values)
        => values.Count == 0 ? "<none>" : string.Join(",", values);

    private static string TruncateSingleLine(string value, int maxLength)
    {
        var normalized = value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return normalized.Length <= maxLength
            ? normalized
            : string.Concat(normalized.AsSpan(0, Math.Max(0, maxLength - 1)), "…");
    }

    private int WriteCommandExecResult(CommandExecCommandOptions options, ControlPlaneCommandExecutionResult result)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
            return 0;
        }

        return WriteCommandExecRunResult(result);
    }

    private int WriteCommandExecAcceptedResult(CommandExecCommandOptions options, ControlPlaneCommandExecutionCommandAcceptedResult result)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
            return 0;
        }

        return options.CommandKind switch
        {
            CommandExecCommandKind.Write => WriteActionResult("已写入命令 stdin。"),
            CommandExecCommandKind.Terminate => WriteActionResult("已请求终止命令进程。"),
            CommandExecCommandKind.Resize => WriteActionResult("已调整终端尺寸。"),
            _ => WriteActionResult("命令请求已提交。"),
        };
    }

    private int WriteCodeModeResult(CodeModeCommandOptions options, ControlPlaneCodeModeResult result)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(BuildCodeModeResultJson(result), jsonOptions));
            return result.Success ? 0 : 1;
        }

        Console.WriteLine(
            $"status={result.Status}\tthreadId={result.ThreadId?.Value ?? "<unknown>"}\tturnId={result.TurnId?.Value ?? "<unknown>"}\tcellId={result.CellId ?? "<none>"}");

        if (!string.IsNullOrWhiteSpace(result.Output))
        {
            Console.WriteLine(result.Output);
        }
        else
        {
            var textItems = result.ContentItems
                .Select(static item => Normalize(item.Text))
                .Where(static text => !string.IsNullOrWhiteSpace(text))
                .ToArray();
            if (textItems.Length > 0)
            {
                foreach (var text in textItems)
                {
                    Console.WriteLine(text);
                }
            }
        }

        return result.Success ? 0 : 1;
    }

    private static object BuildCodeModeResultJson(ControlPlaneCodeModeResult result)
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

    private int WriteSkillsListResult(RuntimeSurfaceCommandOptions options, ControlPlaneSkillCatalogResult result)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(BuildSkillsListJson(result), jsonOptions));
            return 0;
        }

        if (result.Entries.Count == 0)
        {
            Console.WriteLine("未发现可用技能。");
            return 0;
        }

        foreach (var group in result.Entries)
        {
            Console.WriteLine($"[{group.WorkingDirectory}]");

            if (group.Skills.Count > 0)
            {
                foreach (var skill in group.Skills)
                {
                    Console.WriteLine($"  {skill.Scope}\t{skill.Name}\t{skill.Path}");
                }
            }
            else
            {
                Console.WriteLine("  <无技能>");
            }

            if (group.Errors.Count > 0)
            {
                foreach (var error in group.Errors)
                {
                    Console.WriteLine($"  ! {error.Path}: {error.Message}");
                }
            }
        }

        return 0;
    }

    private int WriteSkillsConfigWriteResult(RuntimeSurfaceCommandOptions options, ControlPlaneSkillConfigWriteResult result)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
            return 0;
        }

        var effectiveEnabled = result.EffectiveEnabled;
        var action = effectiveEnabled ? "启用" : "禁用";
        Console.WriteLine($"已{action}技能：{options.SkillPath ?? "<unknown>"}");
        return 0;
    }

    private int WriteFeatureListResult(RuntimeSurfaceCommandOptions options, ControlPlaneExperimentalFeatureCatalogResult result)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                data = result.Items,
                nextCursor = result.NextCursor,
            }, jsonOptions));
            return 0;
        }

        var rows = result.Items
            .Select(static item => new
            {
                item.Name,
                Stage = NormalizeFeatureStageLabel(item.Stage),
                item.Enabled,
            })
            .OrderBy(static item => item.Name, StringComparer.Ordinal)
            .ToArray();
        if (rows.Length == 0)
        {
            return 0;
        }

        var nameWidth = rows.Max(static item => item.Name?.Length ?? 0);
        var stageWidth = rows.Max(static item => item.Stage.Length);
        foreach (var row in rows)
        {
            var name = (row.Name ?? string.Empty).PadRight(nameWidth);
            var stage = row.Stage.PadRight(stageWidth);
            Console.WriteLine($"{name}  {stage}  {row.Enabled}");
        }

        return 0;
    }

    private int WriteFeatureConfigWriteResult(
        RuntimeSurfaceCommandOptions options,
        ControlPlaneConfigWriteResult result,
        ControlPlaneExperimentalFeatureDescriptor feature)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
            return 0;
        }

        var featureName = feature.Name ?? options.FeatureName ?? "<unknown>";
        if (options.Enabled == true)
        {
            Console.WriteLine($"Enabled feature `{featureName}` in tianshu.toml.");
            MaybeWriteUnderDevelopmentFeatureWarning(options, result, feature);
        }
        else
        {
            Console.WriteLine($"Disabled feature `{featureName}` in tianshu.toml.");
        }

        return 0;
    }

    private int WriteExperimentalFeatureListResult(RuntimeSurfaceCommandOptions options, ControlPlaneExperimentalFeatureCatalogResult result)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                data = result.Items,
                nextCursor = result.NextCursor,
            }, jsonOptions));
            return 0;
        }

        if (result.Items.Count == 0)
        {
            Console.WriteLine("未发现实验特性。");
            return 0;
        }

        foreach (var item in result.Items)
        {
            Console.WriteLine($"{item.Name}\t{item.Stage}\tenabled={item.Enabled}\tdefault={item.DefaultEnabled}\t{item.DisplayName ?? string.Empty}");
        }

        WriteNextCursor(result.NextCursor);
        return 0;
    }

    private static void MaybeWriteUnderDevelopmentFeatureWarning(
        RuntimeSurfaceCommandOptions options,
        ControlPlaneConfigWriteResult result,
        ControlPlaneExperimentalFeatureDescriptor feature)
    {
        if (!string.IsNullOrWhiteSpace(options.ProfileName)
            || !string.Equals(feature.Stage, "underDevelopment", StringComparison.Ordinal))
        {
            return;
        }

        var filePath = string.IsNullOrWhiteSpace(result.FilePath) ? "tianshu.toml" : result.FilePath;
        Console.Error.WriteLine(
            $"Under-development features enabled: {feature.Name}. Under-development features are incomplete and may behave unpredictably. To suppress this warning, set `suppress_unstable_features_warning = true` in {filePath}.");
    }

    private static string NormalizeFeatureStageLabel(string? stage)
        => stage switch
        {
            "underDevelopment" => "under development",
            "beta" => "experimental",
            "stable" => "stable",
            "deprecated" => "deprecated",
            "removed" => "removed",
            _ => string.IsNullOrWhiteSpace(stage) ? "unknown" : stage,
        };


    private int WriteSkillsRemoteListResult(RuntimeSurfaceCommandOptions options, ControlPlaneRemoteSkillCatalogResult result)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                data = result.Items,
                nextCursor = result.NextCursor,
            }, jsonOptions));
            return 0;
        }

        if (result.Items.Count == 0)
        {
            Console.WriteLine("未发现远程技能。");
            return 0;
        }

        foreach (var item in result.Items)
        {
            var scope = item.HazelnutScope ?? string.Empty;
            Console.WriteLine($"{item.Id}\t{item.Name}\t{scope}");
        }

        WriteNextCursor(result.NextCursor);
        return 0;
    }

    private int WriteModelListResult(RuntimeSurfaceCommandOptions options, ControlPlaneModelCatalogResult result)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                data = result.Items,
                nextCursor = result.NextCursor,
            }, jsonOptions));
            return 0;
        }

        if (result.Items.Count == 0)
        {
            Console.WriteLine("未找到模型。");
            return 0;
        }

        foreach (var item in result.Items)
        {
            Console.WriteLine($"{item.Id}\t{item.DisplayName}\t{item.Model}\t{item.DefaultReasoningEffort}");
        }

        WriteNextCursor(result.NextCursor);
        return 0;
    }

    private int WriteToolCatalogResult(RuntimeSurfaceCommandOptions options, ResolvedToolCatalogSnapshot result)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
            return 0;
        }

        if (result.Items.Count == 0)
        {
            Console.WriteLine("未发现工具能力。");
            return 0;
        }

        Console.WriteLine("name\timplementationId\tkind\tavailable\tmodelVisible\trequirements\tfallback\treason");
        foreach (var item in result.Items)
        {
            var requirements = item.Requirements.Count == 0
                ? "-"
                : string.Join(
                    ',',
                    item.Requirements.Select(static requirement => requirement.Required
                        ? requirement.Key
                        : $"{requirement.Key}?"));
            var fallback = item.FallbackPolicy is null
                ? "-"
                : item.FallbackPolicy.Strategy;
            Console.WriteLine(
                $"{item.Name}\t{item.ImplementationId ?? "-"}\t{FormatToolImplementationKind(item.ImplementationKind)}\t{FormatBool(item.Available)}\t{FormatBool(item.ModelVisible)}\t{requirements}\t{fallback}\t{item.Reason ?? "-"}");
        }

        return 0;
    }

    private int WriteToolConfigExportResult(RuntimeSurfaceCommandOptions options, ResolvedToolCatalogSnapshot result)
    {
        var toml = TianShuToolProfileTomlExporter.ExportBuiltinProfileToml(result);
        if (string.IsNullOrWhiteSpace(options.ToolConfigOutputPath))
        {
            if (options.OutputJson)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { toml }, jsonOptions));
            }
            else
            {
                Console.Write(toml);
            }

            return 0;
        }

        var outputPath = Path.GetFullPath(options.ToolConfigOutputPath);
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(outputPath, toml);
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { path = outputPath }, jsonOptions));
        }
        else
        {
            Console.WriteLine($"工具配置模板已导出：{outputPath}");
        }

        return 0;
    }

    private static string FormatToolImplementationKind(ToolImplementationKind kind)
        => kind.ToString();

    private static string FormatBool(bool value)
        => value ? "yes" : "no";

    private int WriteSkillsRemoteExportResult(RuntimeSurfaceCommandOptions options, ControlPlaneRemoteSkillExportResult result)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(result.Path))
        {
            Console.WriteLine($"远程技能已导出：{result.Path}");
            return 0;
        }

        Console.WriteLine($"远程技能导出请求已完成：{options.HazelnutId ?? "<unknown>"}");
        return 0;
    }

    private int WritePluginListResult(RuntimeSurfaceCommandOptions options, ControlPlanePluginCatalogResult result)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(BuildPluginListJson(result), jsonOptions));
            return 0;
        }

        if (result.Marketplaces.Count == 0)
        {
            Console.WriteLine("未发现插件市场。");
            if (!string.IsNullOrWhiteSpace(result.RemoteSyncError))
            {
                Console.WriteLine($"remoteSyncError={result.RemoteSyncError}");
            }
            return 0;
        }

        foreach (var marketplace in result.Marketplaces)
        {
            Console.WriteLine($"[{marketplace.Name}] {marketplace.Path}");
            if (marketplace.Plugins.Count == 0)
            {
                Console.WriteLine("  <无插件>");
                continue;
            }

            foreach (var plugin in marketplace.Plugins)
            {
                Console.WriteLine($"  {(plugin.Enabled ? "enabled" : "disabled")}\t{plugin.Name}\t{ReadStructuredValueString(plugin.Source, "path") ?? "<unknown>"}");
            }
        }

        if (!string.IsNullOrWhiteSpace(result.RemoteSyncError))
        {
            Console.WriteLine($"remoteSyncError={result.RemoteSyncError}");
        }

        return 0;
    }

    private int WritePluginInstallResult(RuntimeSurfaceCommandOptions options, ControlPlanePluginInstallResult result)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(BuildPluginInstallJson(result), jsonOptions));
            return 0;
        }

        Console.WriteLine("插件安装请求已完成。");
        if (result.AppsNeedingAuth.Count > 0)
        {
            Console.WriteLine("以下应用仍需授权：");
            foreach (var app in result.AppsNeedingAuth)
            {
                Console.WriteLine($"  {app.Id}\t{app.Name}\t{app.InstallUrl ?? "<none>"}");
            }

            return 0;
        }

        Console.WriteLine("无需额外授权的应用。");
        return 0;
    }

    private int WritePluginUninstallResult(RuntimeSurfaceCommandOptions options, ControlPlanePluginUninstallResult result)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
            return 0;
        }

        Console.WriteLine($"插件已卸载：{options.PluginId}");
        return 0;
    }

    private int WritePluginReadResult(RuntimeSurfaceCommandOptions options, ControlPlanePluginReadResult result)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(BuildPluginReadJson(result), jsonOptions));
            return 0;
        }

        var plugin = result.Plugin;
        if (plugin is null)
        {
            Console.WriteLine("未找到插件详情。");
            return 0;
        }

        var summary = plugin.Summary;
        if (string.IsNullOrWhiteSpace(summary.Id) && string.IsNullOrWhiteSpace(summary.Name))
        {
            Console.WriteLine("已读取插件。");
            Console.WriteLine($"市场：{plugin.MarketplaceName}");
            Console.WriteLine($"市场路径：{plugin.MarketplacePath}");
            if (!string.IsNullOrWhiteSpace(plugin.Description))
            {
                Console.WriteLine($"描述：{plugin.Description}");
            }

            Console.WriteLine($"技能数：{plugin.Skills.Count}");
            Console.WriteLine($"应用数：{plugin.Apps.Count}");
            Console.WriteLine($"MCP Server 数：{plugin.McpServers.Count}");
            return 0;
        }

        Console.WriteLine("已读取插件。");
        Console.WriteLine($"市场：{plugin.MarketplaceName}");
        Console.WriteLine($"市场路径：{plugin.MarketplacePath}");
        Console.WriteLine($"插件：{summary.Name}");
        Console.WriteLine($"键：{summary.Id}");
        var sourceType = ReadStructuredValueString(summary.Source, "type");
        var sourcePath = ReadStructuredValueString(summary.Source, "path");
        if (!string.IsNullOrWhiteSpace(sourceType) || !string.IsNullOrWhiteSpace(sourcePath))
        {
            Console.WriteLine($"来源：{sourceType ?? "<unknown>"}\t{sourcePath ?? "<unknown>"}");
        }

        Console.WriteLine($"installed={summary.Installed}\tenabled={summary.Enabled}\tinstallPolicy={summary.InstallPolicy}\tauthPolicy={summary.AuthPolicy}");
        if (!string.IsNullOrWhiteSpace(plugin.Description))
        {
            Console.WriteLine($"描述：{plugin.Description}");
        }

        if (plugin.Skills.Count > 0)
        {
            Console.WriteLine($"技能数：{plugin.Skills.Count}");
            foreach (var skill in plugin.Skills)
            {
                Console.WriteLine($"  skill\t{skill.Name}\t{skill.Path}");
            }
        }

        if (plugin.Apps.Count > 0)
        {
            Console.WriteLine($"应用数：{plugin.Apps.Count}");
            foreach (var app in plugin.Apps)
            {
                Console.WriteLine($"  app\t{app.Id}\t{app.Name}\t{app.InstallUrl ?? "<none>"}");
            }
        }

        if (plugin.McpServers.Count > 0)
        {
            Console.WriteLine($"MCP Server 数：{plugin.McpServers.Count}");
            foreach (var serverName in plugin.McpServers.Where(static item => !string.IsNullOrWhiteSpace(item)))
            {
                Console.WriteLine($"  mcp\t{serverName}");
            }
        }

        return 0;
    }

    private int WriteAppListResult(RuntimeSurfaceCommandOptions options, ControlPlaneAppCatalogResult result)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(BuildAppListJson(result), jsonOptions));
            return 0;
        }

        if (result.Items.Count == 0)
        {
            Console.WriteLine("未发现应用配置。");
            return 0;
        }

        foreach (var item in result.Items)
        {
            var pluginNames = string.Join(',', item.PluginDisplayNames.Where(static name => !string.IsNullOrWhiteSpace(name)));
            Console.WriteLine($"{item.Id}\tenabled={item.IsEnabled}\taccessible={item.IsAccessible}\tplugins={pluginNames}");
        }

        WriteNextCursor(result.NextCursor);
        return 0;
    }

    private static object BuildSkillsListJson(ControlPlaneSkillCatalogResult result)
        => new
        {
            data = result.Entries.Select(static entry => new Dictionary<string, object?>
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

    private static object BuildPluginListJson(ControlPlanePluginCatalogResult result)
        => new
        {
            marketplaces = result.Marketplaces.Select(static marketplace => new Dictionary<string, object?>
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
            remoteSyncError = result.RemoteSyncError,
        };

    private static object BuildPluginReadJson(ControlPlanePluginReadResult result)
        => new
        {
            plugin = result.Plugin is null
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

    private static object BuildPluginInstallJson(ControlPlanePluginInstallResult result)
        => new
        {
            authPolicy = result.AuthPolicy,
            appsNeedingAuth = result.AppsNeedingAuth.Select(static app => new Dictionary<string, object?>
            {
                ["id"] = app.Id,
                ["name"] = app.Name,
                ["description"] = app.Description,
                ["installUrl"] = app.InstallUrl,
            }).ToArray(),
        };

    private static object BuildAppListJson(ControlPlaneAppCatalogResult result)
        => new
        {
            data = result.Items.Select(static item => new Dictionary<string, object?>
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
            nextCursor = result.NextCursor,
        };

    private static StructuredValue? ReadStructuredValue(StructuredValue? value, string propertyName)
    {
        if (value?.Kind != StructuredValueKind.Object || !value.Properties.TryGetValue(propertyName, out var property))
        {
            return null;
        }

        return property;
    }

    private static string? ReadStructuredValueString(StructuredValue? value, string propertyName)
    {
        var property = ReadStructuredValue(value, propertyName);
        if (property is null)
        {
            return null;
        }

        return property.Kind == StructuredValueKind.String
            ? property.StringValue
            : Convert.ToString(property.ToPlainObject(), CultureInfo.InvariantCulture);
    }

    private static bool? ReadStructuredValueBoolean(StructuredValue? value, string propertyName)
    {
        var property = ReadStructuredValue(value, propertyName);
        return property?.BooleanValue;
    }

    private static double? ReadStructuredValueDouble(StructuredValue? value, string propertyName)
    {
        var property = ReadStructuredValue(value, propertyName);
        if (property is null)
        {
            return null;
        }

        if (property.Kind == StructuredValueKind.Number
            && double.TryParse(property.NumberValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        var text = Convert.ToString(property.ToPlainObject(), CultureInfo.InvariantCulture);
        return double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    private static IReadOnlyList<string>? ReadStructuredValueStringArray(StructuredValue? value, string propertyName)
    {
        var property = ReadStructuredValue(value, propertyName);
        if (property?.Kind != StructuredValueKind.Array)
        {
            return null;
        }

        var items = property.Items
            .Select(static item => item.Kind == StructuredValueKind.String
                ? item.StringValue
                : Convert.ToString(item.ToPlainObject(), CultureInfo.InvariantCulture))
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToArray();

        return items.Length == 0 ? null : items;
    }

    private static IReadOnlyDictionary<string, string>? ReadStructuredValueStringDictionary(StructuredValue? value, string propertyName)
    {
        var property = ReadStructuredValue(value, propertyName);
        if (property?.Kind != StructuredValueKind.Object)
        {
            return null;
        }

        var dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in property.Properties)
        {
            var itemValue = pair.Value.Kind == StructuredValueKind.String
                ? pair.Value.StringValue
                : Convert.ToString(pair.Value.ToPlainObject(), CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(itemValue))
            {
                continue;
            }

            dictionary[pair.Key] = itemValue;
        }

        return dictionary.Count == 0 ? null : dictionary;
    }

    private int WriteConfigReadResult(RuntimeSurfaceCommandOptions options, ControlPlaneConfigSnapshotResult result)
    {
        Console.WriteLine(JsonSerializer.Serialize(BuildConfigReadOutput(options, result), jsonOptions));
        return 0;
    }

    private int WriteConfigRequirementsReadResult(RuntimeSurfaceCommandOptions options, ControlPlaneConfigRequirementsResult result)
    {
        var requirements = BuildConfigRequirementsOutput(result);
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { requirements }, jsonOptions));
            return 0;
        }

        if (requirements is null)
        {
            Console.WriteLine("未定义额外的配置约束。");
            return 0;
        }

        Console.WriteLine(JsonSerializer.Serialize(requirements, jsonOptions));
        return 0;
    }

    private static object? BuildConfigRequirementsOutput(ControlPlaneConfigRequirementsResult result)
    {
        if (!result.IsDefined)
        {
            return null;
        }

        var network = result.Network is null
            ? null
            : new Dictionary<string, object?>
            {
                ["enabled"] = result.Network.Enabled,
                ["httpPort"] = result.Network.HttpPort,
                ["socksPort"] = result.Network.SocksPort,
                ["allowUpstreamProxy"] = result.Network.AllowUpstreamProxy,
                ["dangerouslyAllowNonLoopbackProxy"] = result.Network.DangerouslyAllowNonLoopbackProxy,
                ["dangerouslyAllowNonLoopbackAdmin"] = result.Network.DangerouslyAllowNonLoopbackAdmin,
                ["dangerouslyAllowAllUnixSockets"] = result.Network.DangerouslyAllowAllUnixSockets,
                ["allowedDomains"] = result.Network.AllowedDomains.Count == 0 ? null : result.Network.AllowedDomains,
                ["deniedDomains"] = result.Network.DeniedDomains.Count == 0 ? null : result.Network.DeniedDomains,
                ["allowUnixSockets"] = result.Network.AllowUnixSockets.Count == 0 ? null : result.Network.AllowUnixSockets,
                ["allowLocalBinding"] = result.Network.AllowLocalBinding,
            };

        return new Dictionary<string, object?>
        {
            ["allowedApprovalPolicies"] = result.AllowedApprovalPolicies.Count == 0 ? null : result.AllowedApprovalPolicies,
            ["allowedSandboxModes"] = result.AllowedSandboxModes.Count == 0 ? null : result.AllowedSandboxModes,
            ["allowedWebSearchModes"] = result.AllowedWebSearchModes.Count == 0 ? null : result.AllowedWebSearchModes,
            ["featureRequirements"] = result.FeatureRequirements.Count == 0 ? null : result.FeatureRequirements,
            ["enforceResidency"] = string.IsNullOrWhiteSpace(result.EnforceResidency) ? null : result.EnforceResidency,
            ["network"] = network,
        };
    }

    private static object BuildConfigReadOutput(RuntimeSurfaceCommandOptions options, ControlPlaneConfigSnapshotResult result)
    {
        var config = BuildConfigSnapshotOutput(result.Config);
        var origins = BuildConfigOriginsOutput(result.Origins);
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
                var sourceName = new Dictionary<string, object?>(StringComparer.Ordinal);
                if (!string.IsNullOrWhiteSpace(field.SourceType))
                {
                    sourceName["type"] = field.SourceType;
                }

                if (!string.IsNullOrWhiteSpace(field.SourcePath))
                {
                    sourceName["file"] = field.SourcePath;
                }

                if (sourceName.Count > 0)
                {
                    origins[field.KeyPath] = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["name"] = sourceName,
                    };
                }
            }
        }

        var output = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["config"] = config,
            ["origins"] = origins,
        };

        if (options.IncludeLayers)
        {
            output["layers"] = result.Layers
                .Select(static layer =>
                {
                    var entry = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                ["name"] = layer.Name?.ToPlainObject(),
                ["version"] = layer.Version,
                ["config"] = layer.Config?.ToPlainObject() ?? new Dictionary<string, object?>(StringComparer.Ordinal),
                    };

                    if (!string.IsNullOrWhiteSpace(layer.DisabledReason))
                    {
                        entry["disabledReason"] = layer.DisabledReason;
                    }

                    return entry;
                })
                .ToArray();
        }

        return output;
    }

    private static Dictionary<string, object?> BuildConfigSnapshotOutput(StructuredValue? snapshot)
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

    private static Dictionary<string, object?> BuildConfigOriginsOutput(IReadOnlyDictionary<string, ControlPlaneConfigOrigin> origins)
    {
        var output = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in origins)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            var name = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(pair.Value.Type))
            {
                name["type"] = pair.Value.Type;
            }

            if (!string.IsNullOrWhiteSpace(pair.Value.File))
            {
                name["file"] = pair.Value.File;
            }

            if (!string.IsNullOrWhiteSpace(pair.Value.DotTianShuFolder))
            {
                name["dotTianShuFolder"] = pair.Value.DotTianShuFolder;
            }

            var entry = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (name.Count > 0)
            {
                entry["name"] = name;
            }

            if (!string.IsNullOrWhiteSpace(pair.Value.Version))
            {
                entry["version"] = pair.Value.Version;
            }

            if (entry.Count == 0)
            {
                continue;
            }

            output[pair.Key] = entry;
        }

        return output;
    }

    private int WriteConfigMutationResult(JsonElement result)
    {
        Console.WriteLine($"status={ReadString(result, "status") ?? "ok"}\tversion={ReadString(result, "version") ?? "<unknown>"}\tfile={ReadString(result, "filePath") ?? "<unknown>"}");
        return 0;
    }

    private int WriteConfigMutationResult(RuntimeSurfaceCommandOptions options, ControlPlaneConfigWriteResult result)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
            return 0;
        }

        var message = $"status={result.Status}\tversion={result.Version}\tfile={result.FilePath}";
        if (!string.IsNullOrWhiteSpace(result.OverriddenMetadata?.Message))
        {
            message += $"\toverridden={result.OverriddenMetadata.Message}";
        }

        Console.WriteLine(message);
        return 0;
    }

    private int WriteCommandExecRunResult(ControlPlaneCommandExecutionResult result)
    {
        if (result.Started)
        {
            Console.WriteLine($"命令已启动。processId={result.ProcessId ?? "<unknown>"}\tpid={result.Pid?.ToString() ?? "<unknown>"}");
            return 0;
        }

        var exitCode = result.ExitCode ?? 0;
        Console.WriteLine($"exitCode={exitCode}");

        if (!string.IsNullOrWhiteSpace(result.Stdout))
        {
            Console.WriteLine("[stdout]");
            Console.WriteLine(result.Stdout);
        }

        if (!string.IsNullOrWhiteSpace(result.Stderr))
        {
            Console.WriteLine("[stderr]");
            Console.WriteLine(result.Stderr);
        }

        return exitCode == 0 ? 0 : 1;
    }

    private static int WriteActionResult(string message)
    {
        Console.WriteLine(message);
        return 0;
    }

    private int WriteMcpServerStatusListResult(RuntimeSurfaceCommandOptions options, ControlPlaneMcpServerCatalogResult result)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                data = result.Items,
                nextCursor = result.NextCursor,
            }, jsonOptions));
            return 0;
        }

        if (result.Items.Count == 0)
        {
            Console.WriteLine("未发现 MCP Server 状态。");
            return 0;
        }

        foreach (var item in result.Items)
        {
            Console.WriteLine($"{item.Name}\tauth={item.AuthStatus}\ttools={item.ToolNames.Count}\tresources={item.ResourceUris.Count}\ttemplates={item.ResourceTemplateUris.Count}");
        }

        WriteNextCursor(result.NextCursor);
        return 0;
    }

    private int WriteMcpServerReloadResult(RuntimeSurfaceCommandOptions options, ControlPlaneMcpServerReloadResult result)
    {
        _ = result;
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
            return 0;
        }

        Console.WriteLine("MCP Server 已重新加载。");
        return 0;
    }

    private static IReadOnlyList<McpDisplayEntry> BuildMcpDisplayEntries(
        IReadOnlyDictionary<string, McpDisplayConfig>? configuredServers,
        IReadOnlyDictionary<string, ControlPlaneMcpServerDescriptor> statusMap)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (configuredServers is not null)
        {
            foreach (var name in configuredServers.Keys)
            {
                names.Add(name);
            }
        }

        foreach (var name in statusMap.Keys)
        {
            names.Add(name);
        }

        return names
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new McpDisplayEntry(
                name,
                configuredServers is not null && configuredServers.TryGetValue(name, out var config) ? config : null,
                statusMap.TryGetValue(name, out var status) ? status.AuthStatus : "unsupported"))
            .ToArray();
    }

    private async Task<IReadOnlyDictionary<string, ControlPlaneMcpServerDescriptor>> ReadAllMcpStatusesAsync(
        ITianShuControlPlane controlPlane,
        CancellationToken cancellationToken)
    {
        var items = new Dictionary<string, ControlPlaneMcpServerDescriptor>(StringComparer.OrdinalIgnoreCase);
        string? cursor = null;
        do
        {
            var result = await controlPlane.Catalog.ListMcpServerStatusAsync(
                    new ControlPlaneMcpServerStatusQuery
                    {
                        Limit = 200,
                        Cursor = cursor,
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            foreach (var item in result.Items)
            {
                items[item.Name] = item;
            }

            cursor = Normalize(result.NextCursor);
        } while (!string.IsNullOrWhiteSpace(cursor));

        return items;
    }

    private object ToMcpListJsonObject(McpDisplayEntry entry)
        => new
        {
            name = entry.Name,
            enabled = entry.Config?.Enabled ?? true,
            disabled_reason = entry.Config?.DisabledReason,
            transport = BuildMcpTransportJsonObject(entry.Config),
            startup_timeout_sec = entry.Config?.StartupTimeoutSec,
            tool_timeout_sec = entry.Config?.ToolTimeoutSec,
            auth_status = entry.AuthStatus,
        };

    private object ToMcpGetJsonObject(McpDisplayEntry entry)
        => new
        {
            name = entry.Name,
            enabled = entry.Config?.Enabled ?? true,
            disabled_reason = entry.Config?.DisabledReason,
            transport = BuildMcpTransportJsonObject(entry.Config),
            enabled_tools = entry.Config?.EnabledTools,
            disabled_tools = entry.Config?.DisabledTools,
            startup_timeout_sec = entry.Config?.StartupTimeoutSec,
            tool_timeout_sec = entry.Config?.ToolTimeoutSec,
        };

    private static object BuildMcpTransportJsonObject(McpDisplayConfig? config)
    {
        if (config is null)
        {
            return new
            {
                type = "unknown",
            };
        }

        if (!string.IsNullOrWhiteSpace(config.Url))
        {
            return new
            {
                type = "streamable_http",
                url = config.Url,
                bearer_token_env_var = config.BearerTokenEnvVar,
                http_headers = config.HttpHeaders,
                env_http_headers = config.EnvHttpHeaders,
            };
        }

        return new
        {
            type = "stdio",
            command = config.Command,
            args = config.Args ?? Array.Empty<string>(),
            env = config.Env,
            env_vars = config.EnvVars ?? Array.Empty<string>(),
            cwd = config.Cwd,
        };
    }

    private void WriteMcpListTable(IReadOnlyList<McpDisplayEntry> entries)
    {
        var stdioRows = new List<string[]>();
        var httpRows = new List<string[]>();
        foreach (var entry in entries)
        {
            if (!string.IsNullOrWhiteSpace(entry.Config?.Url))
            {
                httpRows.Add(
                [
                    entry.Name,
                    entry.Config!.Url!,
                    entry.Config.BearerTokenEnvVar ?? "-",
                    FormatMcpStatus(entry.Config),
                    entry.AuthStatus,
                ]);
                continue;
            }

            stdioRows.Add(
            [
                entry.Name,
                entry.Config?.Command ?? "-",
                entry.Config?.Args is { Count: > 0 } args ? string.Join(" ", args) : "-",
                FormatMcpEnvDisplay(entry.Config),
                entry.Config?.Cwd ?? "-",
                FormatMcpStatus(entry.Config),
                entry.AuthStatus,
            ]);
        }

        if (stdioRows.Count > 0)
        {
            WriteAlignedRows(
                ["Name", "Command", "Args", "Env", "Cwd", "Status", "Auth"],
                stdioRows);
        }

        if (stdioRows.Count > 0 && httpRows.Count > 0)
        {
            Console.WriteLine();
        }

        if (httpRows.Count > 0)
        {
            WriteAlignedRows(
                ["Name", "Url", "Bearer Token Env Var", "Status", "Auth"],
                httpRows);
        }
    }

    private void WriteMcpGetResult(McpDisplayEntry entry)
    {
        if (entry.Config?.Enabled == false)
        {
            if (!string.IsNullOrWhiteSpace(entry.Config.DisabledReason))
            {
                Console.WriteLine($"{entry.Name} (disabled: {entry.Config.DisabledReason})");
            }
            else
            {
                Console.WriteLine($"{entry.Name} (disabled)");
            }

            return;
        }

        Console.WriteLine(entry.Name);
        Console.WriteLine($"  enabled: {entry.Config?.Enabled ?? true}");

        if (entry.Config?.EnabledTools is not null)
        {
            Console.WriteLine($"  enabled_tools: {FormatToolList(entry.Config.EnabledTools)}");
        }

        if (entry.Config?.DisabledTools is not null)
        {
            Console.WriteLine($"  disabled_tools: {FormatToolList(entry.Config.DisabledTools)}");
        }

        if (!string.IsNullOrWhiteSpace(entry.Config?.Url))
        {
            Console.WriteLine("  transport: streamable_http");
            Console.WriteLine($"  url: {entry.Config!.Url}");
            Console.WriteLine($"  bearer_token_env_var: {entry.Config.BearerTokenEnvVar ?? "-"}");
        }
        else if (!string.IsNullOrWhiteSpace(entry.Config?.Command))
        {
            Console.WriteLine("  transport: stdio");
            Console.WriteLine($"  command: {entry.Config!.Command}");
            Console.WriteLine($"  args: {FormatCommandArguments(entry.Config.Args)}");
            Console.WriteLine($"  cwd: {entry.Config.Cwd ?? "-"}");
            Console.WriteLine($"  env: {FormatMcpEnvDisplay(entry.Config)}");
        }
        else
        {
            Console.WriteLine("  transport: unknown");
        }

        if (entry.Config?.StartupTimeoutSec is not null)
        {
            Console.WriteLine($"  startup_timeout_sec: {entry.Config.StartupTimeoutSec}");
        }

        if (entry.Config?.ToolTimeoutSec is not null)
        {
            Console.WriteLine($"  tool_timeout_sec: {entry.Config.ToolTimeoutSec}");
        }

        Console.WriteLine($"  remove: mcp remove {entry.Name}");
    }

    private static string FormatToolList(IReadOnlyList<string>? tools)
        => tools is { Count: > 0 } ? string.Join(", ", tools) : "[]";

    private static string FormatCommandArguments(IReadOnlyList<string>? arguments)
        => arguments is { Count: > 0 } ? string.Join(" ", arguments) : "-";

    private static string FormatMcpEnvDisplay(McpDisplayConfig? config)
    {
        var parts = new List<string>();

        if (config?.Env is { Count: > 0 } env)
        {
            parts.AddRange(
                env
                    .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                    .Select(static pair => $"{pair.Key}=*****"));
        }

        if (config?.EnvVars is { Count: > 0 } envVars)
        {
            parts.AddRange(envVars.Select(static envVar => $"{envVar}=*****"));
        }

        return parts.Count == 0 ? "-" : string.Join(", ", parts);
    }

    private static string FormatMcpStatus(McpDisplayConfig? config)
    {
        if (config?.Enabled != false)
        {
            return "enabled";
        }

        return string.IsNullOrWhiteSpace(config.DisabledReason)
            ? "disabled"
            : $"disabled: {config.DisabledReason}";
    }

    private static IReadOnlyDictionary<string, McpDisplayConfig>? ReadConfiguredMcpServers(StructuredValue? snapshot)
    {
        var serversValue = ReadStructuredValue(snapshot, "mcp_servers");
        if (serversValue?.Kind != StructuredValueKind.Object)
        {
            return null;
        }

        var result = new Dictionary<string, McpDisplayConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in serversValue.Properties)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value.Kind != StructuredValueKind.Object)
            {
                continue;
            }

            result[pair.Key] = new McpDisplayConfig(
                ReadStructuredValueString(pair.Value, "command"),
                ReadStructuredValueStringArray(pair.Value, "args"),
                ReadStructuredValueStringDictionary(pair.Value, "env"),
                ReadStructuredValueStringArray(pair.Value, "env_vars"),
                ReadStructuredValueString(pair.Value, "cwd"),
                ReadStructuredValueStringDictionary(pair.Value, "http_headers"),
                ReadStructuredValueStringDictionary(pair.Value, "env_http_headers"),
                ReadStructuredValueString(pair.Value, "url"),
                ReadStructuredValueString(pair.Value, "bearer_token_env_var"),
                ReadStructuredValueDouble(pair.Value, "startup_timeout_sec"),
                ReadStructuredValueDouble(pair.Value, "tool_timeout_sec"),
                ReadStructuredValueBoolean(pair.Value, "enabled"),
                ReadStructuredValueString(pair.Value, "disabled_reason"),
                ReadStructuredValueStringArray(pair.Value, "enabled_tools"),
                ReadStructuredValueStringArray(pair.Value, "disabled_tools"));
        }

        return result;
    }

    private static void WriteAlignedRows(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
    {
        var widths = headers.Select(static header => header.Length).ToArray();
        foreach (var row in rows)
        {
            for (var index = 0; index < row.Length; index++)
            {
                widths[index] = Math.Max(widths[index], row[index].Length);
            }
        }

        Console.WriteLine(BuildAlignedRow(headers, widths));
        foreach (var row in rows)
        {
            Console.WriteLine(BuildAlignedRow(row, widths));
        }
    }

    private static string BuildAlignedRow(IReadOnlyList<string> columns, IReadOnlyList<int> widths)
        => string.Join("  ", columns.Select((value, index) => value.PadRight(widths[index], ' ')));

    private int WriteConversationSummaryResult(RuntimeSurfaceCommandOptions options, ControlPlaneConversationArtifact? result)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                summary = result,
            }, jsonOptions));
            return 0;
        }

        if (result is null)
        {
            Console.WriteLine("未找到会话摘要。");
            return 0;
        }

        Console.WriteLine($"会话：{result.ConversationId}");
        Console.WriteLine($"来源：{result.Source}");
        Console.WriteLine($"路径：{result.Path}");
        Console.WriteLine($"工作目录：{result.WorkingDirectory}");
        Console.WriteLine($"更新时间：{result.UpdatedAt ?? "<unknown>"}");
        Console.WriteLine($"摘要：{result.Preview}");
        return 0;
    }

    private int WriteGitDiffToRemoteResult(RuntimeSurfaceCommandOptions options, ControlPlaneGitDiffArtifact result)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
            return 0;
        }

        if (!result.HasChanges || string.IsNullOrWhiteSpace(result.Diff))
        {
            Console.WriteLine("远端对比无变更。");
            return 0;
        }

        Console.WriteLine(result.Diff);
        return 0;
    }

    private int WriteFuzzyFileSearchResult(FuzzyFileSearchCommandOptions options, ControlPlaneFuzzyFileSearchResult result)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
            return 0;
        }

        return WriteFuzzyFileSearchFilesResult(result.Files);
    }

    private int WriteFuzzyFileSearchResult(FuzzyFileSearchCommandOptions options, ControlPlaneFuzzyFileSearchCommandAcceptedResult result)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
            return 0;
        }

        return options.CommandKind switch
        {
            FuzzyFileSearchCommandKind.Start => WriteFuzzyFileSearchActionResult($"已启动模糊文件搜索会话：{options.SessionId}"),
            FuzzyFileSearchCommandKind.Stop => WriteFuzzyFileSearchActionResult($"已停止模糊文件搜索会话：{options.SessionId}"),
            _ => WriteTypedFallback(result),
        };
    }

    private int WriteFuzzyFileSearchUpdateResult(FuzzyFileSearchCommandOptions options, ControlPlaneFuzzyFileSearchCommandAcceptedResult result, IReadOnlyList<FuzzyFileSearchFilePayload>? files)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                sessionId = options.SessionId,
                query = options.Query,
                notificationReceived = files is not null,
                files = files ?? Array.Empty<FuzzyFileSearchFilePayload>(),
                rpcResult = result,
            }, jsonOptions));
            return 0;
        }

        if (files is null)
        {
            Console.WriteLine($"会话已更新，但未收到结果通知。sessionId={options.SessionId}");
            return 0;
        }

        return WriteFuzzyFileSearchFilesResult(files);
    }

    private int WriteFuzzyFileSearchFilesResult(JsonElement result)
    {
        if (!TryGetArray(result, "files", out var files))
        {
            files = result;
        }

        if (files.ValueKind != JsonValueKind.Array || files.GetArrayLength() == 0)
        {
            Console.WriteLine("未找到匹配文件。");
            return 0;
        }

        foreach (var file in ReadFuzzyFileDisplayItems(files))
        {
            Console.WriteLine(file);
        }

        return 0;
    }

    private int WriteFuzzyFileSearchFilesResult(IReadOnlyList<FuzzyFileSearchFilePayload> files)
    {
        if (files.Count == 0)
        {
            Console.WriteLine("未找到匹配文件。");
            return 0;
        }

        foreach (var file in ReadFuzzyFileDisplayItems(files))
        {
            Console.WriteLine(file);
        }

        return 0;
    }

    private int WriteFuzzyFileSearchFilesResult(IReadOnlyList<ControlPlaneFuzzyFileSearchFile> files)
    {
        if (files.Count == 0)
        {
            Console.WriteLine("未找到匹配文件。");
            return 0;
        }

        foreach (var file in ReadFuzzyFileDisplayItems(files))
        {
            Console.WriteLine(file);
        }

        return 0;
    }

    private int WriteFuzzyFileSearchActionResult(string message)
        => WriteActionResult(message);

    private int WriteTypedFallback<T>(T result)
    {
        Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
        return 0;
    }

    private int WriteFeedbackResult(FeedbackCommandOptions options, ControlPlaneFeedbackUploadResult result)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
            return 0;
        }

        return WriteActionResult($"Uploaded feedback. trackingThreadId={result.ThreadId}");
    }

    private int WriteWindowsSandboxResult(WindowsSandboxCommandOptions options, ControlPlaneWindowsSandboxSetupStartResult result, WindowsSandboxSetupPayload? notification)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                rpcResult = result,
                notificationReceived = notification is not null,
                completion = notification,
            }, jsonOptions));
            return 0;
        }

        if (notification is not null)
        {
            var mode = notification.Mode ?? options.Mode ?? "<unknown>";
            var success = notification.Success ?? false;
            var error = notification.Error;
            Console.WriteLine(success
                ? $"Windows Sandbox setup completed. mode={mode}	success=True"
                : $"Windows Sandbox setup failed. mode={mode}	success=False	error={error ?? "<unknown>"}");
            return success ? 0 : 1;
        }

        return WriteActionResult($"Submitted Windows Sandbox setup request. mode={options.Mode ?? "<unknown>"}");
    }

    private static WindowsSandboxSetupMode ParseWindowsSandboxSetupMode(string? mode)
        => Normalize(mode)?.ToLowerInvariant() switch
        {
            "elevated" => WindowsSandboxSetupMode.Elevated,
            "unelevated" => WindowsSandboxSetupMode.Unelevated,
            _ => throw new InvalidOperationException($"不支持的 windows sandbox mode：{mode ?? "<null>"}"),
        };

    private int WriteRealtimeResult(RealtimeCommandOptions options, ControlPlaneRealtimeCommandAcceptedResult result, RealtimeSessionPayload? notification)
    {
        if (options.CommandKind != RealtimeCommandKind.Start)
        {
            throw new InvalidOperationException($"不支持的 realtime start 结果输出：{options.CommandKind}");
        }

        return WriteRealtimeStartResult(options, result, notification);
    }

    private int WriteRealtimeCommandAcceptedResult(ControlPlaneRealtimeCommandAcceptedResult result, RealtimeCommandOptions options)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
            return 0;
        }

        return options.CommandKind switch
        {
            RealtimeCommandKind.AppendText => WriteActionResult($"Submitted realtime text append. threadId={options.ThreadId ?? "<unknown>"}	sessionId={options.SessionId ?? "<requested>"}"),
            RealtimeCommandKind.AppendAudio => WriteActionResult($"Submitted realtime audio append. threadId={options.ThreadId ?? "<unknown>"}	sessionId={options.SessionId ?? "<requested>"}"),
            RealtimeCommandKind.HandoffOutput => WriteActionResult($"Submitted realtime handoff output. threadId={options.ThreadId ?? "<unknown>"}	sessionId={options.SessionId ?? "<requested>"}	handoffId={options.HandoffId ?? "<unknown>"}"),
            RealtimeCommandKind.Stop => WriteActionResult($"Submitted realtime stop request. threadId={options.ThreadId ?? "<unknown>"}	sessionId={options.SessionId ?? "<requested>"}"),
            _ => throw new InvalidOperationException($"不支持的 realtime 命令：{options.CommandKind}"),
        };
    }

    private int WriteRealtimeStartResult(RealtimeCommandOptions options, ControlPlaneRealtimeCommandAcceptedResult result, RealtimeSessionPayload? notification)
    {
        var resolvedThreadId = options.ThreadId;
        var resolvedSessionId = options.SessionId;
        if (notification is not null)
        {
            resolvedThreadId = notification.ThreadId ?? resolvedThreadId;
            resolvedSessionId = notification.SessionId ?? resolvedSessionId;
        }

        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                rpcResult = result,
                notificationReceived = notification is not null,
                threadId = resolvedThreadId,
                sessionId = resolvedSessionId,
            }, jsonOptions));
            return 0;
        }

        if (string.IsNullOrWhiteSpace(resolvedSessionId))
        {
            Console.Error.WriteLine("Realtime start RPC returned, but no started notification was observed. Pass --session-id or inspect runtime events.");
            return 1;
        }

        return WriteActionResult($"Started realtime session. threadId={resolvedThreadId ?? "<unknown>"}	sessionId={resolvedSessionId}");
    }

    private int WriteReviewStartResult(RuntimeSurfaceCommandOptions options, ControlPlaneReviewStartResult result)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
            return 0;
        }

        var turn = result.Turn;
        Console.WriteLine("已启动 review。");
        Console.WriteLine($"reviewThreadId：{(string.IsNullOrWhiteSpace(result.ReviewThreadId) ? "<unknown>" : result.ReviewThreadId)}");
        Console.WriteLine($"turnId：{(string.IsNullOrWhiteSpace(turn?.Id) ? "<unknown>" : turn!.Id)}");
        Console.WriteLine($"状态：{(string.IsNullOrWhiteSpace(turn?.Status) ? "<unknown>" : turn!.Status)}");

        if (!string.IsNullOrWhiteSpace(turn?.DisplayText))
        {
            Console.WriteLine($"请求：{turn.DisplayText}");
        }

        return 0;
    }

    private int WriteCollaborationModeListResult(RuntimeSurfaceCommandOptions options, ControlPlaneCollaborationModeCatalogResult result)
    {
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { data = result.Items }, jsonOptions));
            return 0;
        }

        if (result.Items.Count == 0)
        {
            Console.WriteLine("未发现协作模式预设。");
            return 0;
        }

        foreach (var item in result.Items)
        {
            Console.WriteLine($"{item.Name}\t{item.Mode ?? "<unknown>"}\t{item.Model ?? "<inherit>"}\t{item.ReasoningEffort ?? "<inherit>"}");
        }

        return 0;
    }

    private int WriteJsonFallback(JsonElement result)
    {
        Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
        return 0;
    }

    private void WriteNextCursor(JsonElement result)
    {
        var nextCursor = ReadString(result, "nextCursor");
        if (!string.IsNullOrWhiteSpace(nextCursor))
        {
            Console.WriteLine($"nextCursor\t{nextCursor}");
        }
    }

    private void WriteNextCursor(string? nextCursor)
    {
        if (!string.IsNullOrWhiteSpace(nextCursor))
        {
            Console.WriteLine($"nextCursor\t{nextCursor}");
        }
    }

    private static StructuredValue? ParseParamsJson(string? paramsJson)
    {
        if (string.IsNullOrWhiteSpace(paramsJson))
        {
            return null;
        }

        using var document = JsonDocument.Parse(paramsJson);
        return StructuredValue.FromJsonElement(document.RootElement);
    }

    internal static async Task<FormalRpcDispatchResult> TryInvokeFormalRpcAsync(
        IExecutionRuntime runtime,
        string? method,
        StructuredValue? parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        var normalizedMethod = Normalize(method)?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedMethod))
        {
            return default;
        }

        var controlPlane = TianShuControlPlaneClientFactory.Create(runtime);
        var json = NormalizeRpcParameters(parameters);

        return normalizedMethod switch
        {
            "conversation/thread/read" => new(
                true,
                await controlPlane.Conversations.GetThreadProjectionAsync(
                        new GetThreadProjection(new ThreadId(ReadRequiredRpcString(json, "threadId", normalizedMethod))),
                        cancellationToken)
                    .ConfigureAwait(false)),
            "session/snapshot/read" => new(
                true,
                await controlPlane.Sessions.GetSnapshotAsync(cancellationToken).ConfigureAwait(false)),
            "session/overview/read" => new(
                true,
                await controlPlane.Sessions.GetSessionOverviewAsync(
                        new GetSessionOverview(new SessionId(ReadRequiredRpcString(json, "sessionId", normalizedMethod))),
                        cancellationToken)
                    .ConfigureAwait(false)),
            "session/list" => new(
                true,
                await controlPlane.Sessions.ListSessionsAsync(
                        new ListSessions(
                            CollaborationSpaceId: ReadOptionalRpcString(json, "collaborationSpaceId", "spaceId") is { Length: > 0 } collaborationSpaceId
                                ? new CollaborationSpaceId(collaborationSpaceId)
                                : null,
                            IncludeClosed: ReadBool(json, "includeClosed") ?? false),
                        cancellationToken)
                    .ConfigureAwait(false)),
            "governance/approvalqueue/read" => new(
                true,
                await controlPlane.Governance.GetApprovalQueueProjectionAsync(
                        new ListPendingApprovals(
                            RequestedFromParticipantId: ReadOptionalRpcString(json, "participantId", "requestedFromParticipantId") is { Length: > 0 } participantId
                                ? new ParticipantId(participantId)
                                : null),
                        cancellationToken)
                    .ConfigureAwait(false)),
            "governance/userinputs/list" => new(
                true,
                await controlPlane.Governance.ListUserInputRequestsAsync(
                        new ListUserInputRequests(
                            RequestedFromParticipantId: ReadOptionalRpcString(json, "participantId", "requestedFromParticipantId") is { Length: > 0 } participantId
                                ? new ParticipantId(participantId)
                                : null),
                        cancellationToken)
                    .ConfigureAwait(false)),
            "diagnostics/trace/read" => new(
                true,
                await controlPlane.Diagnostics.GetExecutionTraceAsync(
                        new GetExecutionTrace(new ExecutionTraceId(ReadRequiredRpcString(json, "traceId", normalizedMethod))),
                        cancellationToken)
                    .ConfigureAwait(false)),
            "diagnostics/attempts/list" => new(
                true,
                await controlPlane.Diagnostics.ListAttemptSummariesAsync(
                        new ListAttemptSummaries(new ExecutionId(ReadRequiredRpcString(json, "executionId", normalizedMethod))),
                        cancellationToken)
                    .ConfigureAwait(false)),
            "tianshu/debug/clear-memories" => new(
                true,
                await controlPlane.Diagnostics.ClearDebugMemoriesAsync(cancellationToken).ConfigureAwait(false)),
            "exec_wait" => new(
                true,
                await InvokeCodeModeWaitRpcAsync(runtime, json, cancellationToken).ConfigureAwait(false)),
            "model/list" => new(
                true,
                await controlPlane.Catalog.ListModelsAsync(
                        new ControlPlaneModelCatalogQuery
                        {
                            Limit = ReadInt32(json, "limit") ?? 50,
                            Cursor = Normalize(ReadString(json, "cursor")),
                            IncludeHidden = ReadBool(json, "includeHidden") ?? false,
                        },
                        cancellationToken)
                    .ConfigureAwait(false)),
            "model/catalog/read" => new(
                true,
                await controlPlane.Catalog.GetCapabilityCatalogAsync(
                        new GetCapabilityCatalog(
                            workspacePath: Normalize(ReadOptionalRpcString(json, "cwd", "workspacePath")),
                            includeHiddenModels: ReadBool(json, "includeHidden") ?? false,
                            modelLimit: ReadInt32(json, "limit") ?? 200,
                            includeHiddenTools: ReadBool(json, "includeHiddenTools") ?? false),
                        cancellationToken)
                    .ConfigureAwait(false)),
            "tools/catalog/read" => new(
                true,
                (await controlPlane.Catalog.GetCapabilityCatalogAsync(
                        new GetCapabilityCatalog(
                            workspacePath: Normalize(ReadOptionalRpcString(json, "cwd", "workspacePath")),
                            includeHiddenTools: ReadBool(json, "includeHidden") ?? false),
                        cancellationToken)
                    .ConfigureAwait(false)).Tools),
            "model/binding/resolve" => new(
                true,
                await controlPlane.Catalog.ResolveEngineBindingAsync(
                        new ResolveEngineBinding(
                            WorkspacePath: Normalize(ReadOptionalRpcString(json, "cwd", "workspacePath")),
                            PreferredProviderKey: Normalize(ReadOptionalRpcString(json, "providerKey", "preferredProviderKey")),
                            PreferredModelKey: Normalize(ReadOptionalRpcString(json, "modelKey", "preferredModelKey")),
                            ReasoningEffort: Normalize(ReadOptionalRpcString(json, "reasoningEffort")),
                            ReasoningSummary: Normalize(ReadOptionalRpcString(json, "reasoningSummary")),
                            Verbosity: Normalize(ReadOptionalRpcString(json, "verbosity")),
                            PreferWebsocketTransport: ReadBool(json, "preferWebsocketTransport") ?? false),
                        cancellationToken)
                    .ConfigureAwait(false)),
            "feedback/upload" => new(
                true,
                await controlPlane.Diagnostics.UploadFeedbackAsync(
                        new ControlPlaneFeedbackUploadCommand
                        {
                            Classification = ReadRequiredRpcString(json, "classification", normalizedMethod),
                            IncludeLogs = ReadBool(json, "includeLogs") ?? false,
                            ThreadId = Normalize(ReadString(json, "threadId")),
                            Reason = Normalize(ReadString(json, "reason")),
                            ExtraLogFiles = TryGetArray(json, "extraLogFiles", out var extraLogFiles)
                                ? ReadStringArray(extraLogFiles)
                                : Array.Empty<string>(),
                        },
                        cancellationToken)
                    .ConfigureAwait(false)),
            _ => default,
        };
    }

    private static async Task<ControlPlaneCodeModeResult> InvokeCodeModeWaitRpcAsync(
        IExecutionRuntime runtime,
        JsonElement json,
        CancellationToken cancellationToken)
    {
        var sessionSnapshot = await CliSessionSnapshotUtilities.GetSnapshotAsync(runtime, cancellationToken).ConfigureAwait(false);
        var threadId = ReadOptionalRpcString(json, "threadId") ?? sessionSnapshot.ActiveThreadId?.Value;
        if (string.IsNullOrWhiteSpace(threadId))
        {
            throw new InvalidOperationException("RPC method `exec_wait` 缺少必填参数 `threadId`，且当前会话没有 active thread。");
        }

        return await runtime.AsNorthboundSurface()
            .Execution
            .WaitCodeModeAsync(
                new ControlPlaneCodeModeWaitCommand
                {
                    ThreadId = new ThreadId(threadId),
                    CellId = ReadRequiredRpcString(json, "cellId", "exec_wait"),
                    YieldTimeMs = ReadInt32(json, "yieldTimeMs"),
                    MaxTokens = ReadInt32(json, "maxTokens"),
                    Terminate = ReadBool(json, "terminate") ?? false,
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static JsonElement NormalizeRpcParameters(StructuredValue? parameters)
    {
        if (parameters is null)
        {
            return JsonSerializer.SerializeToElement(new Dictionary<string, object?>());
        }

        var element = JsonSerializer.SerializeToElement(parameters.ToPlainObject());
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("RPC formal dispatch 需要 JSON object 参数。");
        }

        return element;
    }

    private static string ReadRequiredRpcString(JsonElement element, string propertyName, string method)
        => Normalize(ReadString(element, propertyName))
           ?? throw new InvalidOperationException($"RPC method `{method}` 缺少必填参数 `{propertyName}`。");

    private static string? ReadOptionalRpcString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var value = Normalize(ReadString(element, propertyName));
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static object BuildSessionOverviewPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "sessionId", options.SessionId);
        return payload;
    }

    private static object BuildSessionSnapshotPayload()
        => new Dictionary<string, object?>();

    private static object BuildConversationThreadPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "threadId", options.ThreadId);
        return payload;
    }

    private static GetThreadProjection BuildConversationThreadRequest(RuntimeSurfaceCommandOptions options)
        => new(new ThreadId(options.ThreadId ?? string.Empty));

    private static GetSessionOverview BuildSessionOverviewRequest(RuntimeSurfaceCommandOptions options)
        => new(new SessionId(options.SessionId ?? string.Empty));

    private static object BuildSessionListPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "collaborationSpaceId", options.CollaborationSpaceId);
        AddIfTrue(payload, "includeClosed", options.IncludeClosed);
        return payload;
    }

    private static ListSessions BuildSessionListRequest(RuntimeSurfaceCommandOptions options)
        => new(
            CollaborationSpaceId: string.IsNullOrWhiteSpace(options.CollaborationSpaceId)
                ? null
                : new CollaborationSpaceId(options.CollaborationSpaceId),
            IncludeClosed: options.IncludeClosed);

    private static object BuildApprovalQueuePayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "participantId", options.ParticipantId);
        return payload;
    }

    private static ListPendingApprovals BuildApprovalQueueRequest(RuntimeSurfaceCommandOptions options)
        => new(
            RequestedFromParticipantId: string.IsNullOrWhiteSpace(options.ParticipantId)
                ? null
                : new ParticipantId(options.ParticipantId));

    private static object BuildUserInputRequestListPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "participantId", options.ParticipantId);
        return payload;
    }

    private static ListUserInputRequests BuildUserInputRequestListRequest(RuntimeSurfaceCommandOptions options)
        => new(
            RequestedFromParticipantId: string.IsNullOrWhiteSpace(options.ParticipantId)
                ? null
                : new ParticipantId(options.ParticipantId));

    private static object BuildCollaborationOverviewPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "spaceId", options.CollaborationSpaceId);
        return payload;
    }

    private static object BuildCollaborationCreatePayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "spaceId", options.CollaborationSpaceId);
        AddIfNotNull(payload, "key", options.CollaborationSpaceKey);
        AddIfNotNull(payload, "displayName", options.DisplayName);
        AddIfNotNull(payload, "purpose", options.Purpose);
        AddIfNotNull(payload, "defaultWorkspace", options.DefaultWorkspace);
        AddIfNotNull(payload, "defaultExecutionProfile", options.DefaultExecutionProfile);
        AddIfNotNull(payload, "policyKey", options.PolicyKey);
        return payload;
    }

    private static CreateCollaborationSpace BuildCollaborationCreateRequest(RuntimeSurfaceCommandOptions options)
        => new(
            new CollaborationSpaceId(options.CollaborationSpaceId ?? string.Empty),
            options.CollaborationSpaceKey ?? string.Empty,
            options.DisplayName ?? string.Empty,
            new CollaborationSpaceProfile(options.Purpose ?? string.Empty),
            BuildCollaborationDefaultSet(options),
            string.IsNullOrWhiteSpace(options.PolicyKey) ? null : new CollaborationPolicyRef(options.PolicyKey));

    private static object BuildCollaborationConfigurePayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "spaceId", options.CollaborationSpaceId);
        AddIfNotNull(payload, "displayName", options.DisplayName);
        AddIfNotNull(payload, "purpose", options.Purpose);
        AddIfNotNull(payload, "defaultWorkspace", options.DefaultWorkspace);
        AddIfNotNull(payload, "defaultExecutionProfile", options.DefaultExecutionProfile);
        AddIfNotNull(payload, "policyKey", options.PolicyKey);
        return payload;
    }

    private static ConfigureCollaborationSpace BuildCollaborationConfigureRequest(RuntimeSurfaceCommandOptions options)
        => new(
            new CollaborationSpaceId(options.CollaborationSpaceId ?? string.Empty),
            DisplayName: options.DisplayName,
            Profile: string.IsNullOrWhiteSpace(options.Purpose) ? null : new CollaborationSpaceProfile(options.Purpose),
            Defaults: HasCollaborationDefaults(options) ? BuildCollaborationDefaultSet(options) : null,
            PolicyRef: string.IsNullOrWhiteSpace(options.PolicyKey) ? null : new CollaborationPolicyRef(options.PolicyKey));

    private static object BuildCollaborationArchivePayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "spaceId", options.CollaborationSpaceId);
        return payload;
    }

    private static ArchiveCollaborationSpace BuildCollaborationArchiveRequest(RuntimeSurfaceCommandOptions options)
        => new(new CollaborationSpaceId(options.CollaborationSpaceId ?? string.Empty));

    private static GetCollaborationSpaceOverview BuildCollaborationOverviewRequest(RuntimeSurfaceCommandOptions options)
        => new(new CollaborationSpaceId(options.CollaborationSpaceId ?? string.Empty));

    private static object BuildCollaborationSpacePayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "spaceId", options.CollaborationSpaceId);
        return payload;
    }

    private static GetCollaborationSpaceProjection BuildCollaborationSpaceRequest(RuntimeSurfaceCommandOptions options)
        => new(new CollaborationSpaceId(options.CollaborationSpaceId ?? string.Empty));

    private static object BuildCollaborationListPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfTrue(payload, "includeArchived", options.IncludeArchived);
        return payload;
    }

    private static ListCollaborationSpaces BuildCollaborationListRequest(RuntimeSurfaceCommandOptions options)
        => new(options.IncludeArchived);

    private static object BuildParticipantBindSessionPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "sessionId", options.SessionId);
        AddIfNotNull(payload, "participantId", options.ParticipantId);
        return payload;
    }

    private static BindParticipantToSession BuildParticipantBindSessionRequest(RuntimeSurfaceCommandOptions options)
        => new(
            new SessionId(options.SessionId ?? string.Empty),
            new ParticipantId(options.ParticipantId ?? string.Empty));

    private static object BuildParticipantBindWorkflowPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "workflowId", options.WorkflowId);
        AddIfNotNull(payload, "participantId", options.ParticipantId);
        return payload;
    }

    private static BindParticipantToWorkflow BuildParticipantBindWorkflowRequest(RuntimeSurfaceCommandOptions options)
        => new(
            new WorkflowId(options.WorkflowId ?? string.Empty),
            new ParticipantId(options.ParticipantId ?? string.Empty));

    private static object BuildParticipantUpdateRolePayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "participantId", options.ParticipantId);
        AddIfNotNull(payload, "role", options.Role);
        return payload;
    }

    private static UpdateParticipantRole BuildParticipantUpdateRoleRequest(RuntimeSurfaceCommandOptions options)
        => new(
            new ParticipantId(options.ParticipantId ?? string.Empty),
            options.Role ?? string.Empty);

    private static object BuildParticipantReadPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "participantId", options.ParticipantId);
        return payload;
    }

    private static GetParticipantProjection BuildParticipantReadRequest(RuntimeSurfaceCommandOptions options)
        => new(new ParticipantId(options.ParticipantId ?? string.Empty));

    private static object BuildParticipantViewPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "participantId", options.ParticipantId);
        return payload;
    }

    private static GetParticipantViewProjection BuildParticipantViewRequest(RuntimeSurfaceCommandOptions options)
        => new(new ParticipantId(options.ParticipantId ?? string.Empty));

    private static object BuildParticipantListPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "spaceId", options.CollaborationSpaceId);
        return payload;
    }

    private static ListParticipantsInScope BuildParticipantListRequest(RuntimeSurfaceCommandOptions options)
        => new(new CollaborationSpaceId(options.CollaborationSpaceId ?? string.Empty));

    private static bool HasCollaborationDefaults(RuntimeSurfaceCommandOptions options)
        => !string.IsNullOrWhiteSpace(options.DefaultWorkspace)
           || !string.IsNullOrWhiteSpace(options.DefaultExecutionProfile);

    private static CollaborationDefaultSet BuildCollaborationDefaultSet(RuntimeSurfaceCommandOptions options)
        => new(
            options.DefaultWorkspace,
            options.DefaultExecutionProfile);

    private static object BuildArtifactReadPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "artifactId", options.ArtifactId);
        return payload;
    }

    private static GetArtifactDetail BuildArtifactReadRequest(RuntimeSurfaceCommandOptions options)
        => new(new ArtifactId(options.ArtifactId ?? string.Empty));

    private static object BuildArtifactListPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "spaceId", options.CollaborationSpaceId);
        AddIfNotNull(payload, "participantId", options.ParticipantId);
        return payload;
    }

    private static ListArtifacts BuildArtifactListRequest(RuntimeSurfaceCommandOptions options)
        => new(
            CollaborationSpaceId: string.IsNullOrWhiteSpace(options.CollaborationSpaceId)
                ? null
                : new CollaborationSpaceId(options.CollaborationSpaceId),
            ProducedByParticipantId: string.IsNullOrWhiteSpace(options.ParticipantId)
                ? null
                : new ParticipantId(options.ParticipantId));

    private static object BuildWorkflowCreatePayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "workflowId", options.WorkflowId);
        AddIfNotNull(payload, "spaceId", options.CollaborationSpaceId);
        AddIfNotNull(payload, "displayName", options.DisplayName);
        AddIfNotNull(payload, "threadId", options.ThreadId);
        AddIfNotNull(payload, "participantId", options.ParticipantId);
        return payload;
    }

    private static CreateWorkflow BuildWorkflowCreateRequest(RuntimeSurfaceCommandOptions options)
        => new(
            new WorkflowId(options.WorkflowId ?? string.Empty),
            new CollaborationSpaceId(options.CollaborationSpaceId ?? string.Empty),
            options.DisplayName ?? string.Empty,
            BuildOptionalWorkflowOwner(options),
            string.IsNullOrWhiteSpace(options.ThreadId) ? null : new ThreadId(options.ThreadId));

    private static object BuildWorkflowPublishPlanPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "workflowId", options.WorkflowId);
        AddIfNotNull(payload, "title", options.Title);

        var steps = BuildWorkflowPlanStepPayloads(options);
        if (steps.Length > 0)
        {
            payload["steps"] = steps;
        }

        return payload;
    }

    private static PublishPlan BuildWorkflowPublishPlanRequest(RuntimeSurfaceCommandOptions options)
        => new(
            new WorkflowId(options.WorkflowId ?? string.Empty),
            new Plan(
                options.Title ?? string.Empty,
                BuildWorkflowPlanSteps(options)));

    private static object BuildWorkflowCreateTaskPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "taskId", options.TaskId);
        AddIfNotNull(payload, "workflowId", options.WorkflowId);
        AddIfNotNull(payload, "title", options.Title);
        AddIfNotNull(payload, "state", NormalizeWorkflowTaskStateText(options.Status));
        AddIfNotNull(payload, "participantId", options.ParticipantId);
        return payload;
    }

    private static CreateTask BuildWorkflowCreateTaskRequest(RuntimeSurfaceCommandOptions options)
        => new(
            new TianShu.Contracts.Workflows.Task(
                new TaskId(options.TaskId ?? string.Empty),
                new WorkflowId(options.WorkflowId ?? string.Empty),
                options.Title ?? string.Empty,
                ParseWorkflowTaskState(options.Status),
                BuildOptionalWorkflowOwner(options)));

    private static object BuildWorkflowUpdateTaskStatePayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "taskId", options.TaskId);
        AddIfNotNull(payload, "state", NormalizeWorkflowTaskStateText(options.Status));
        AddIfNotNull(payload, "participantId", options.ParticipantId);
        return payload;
    }

    private static UpdateTaskState BuildWorkflowUpdateTaskStateRequest(RuntimeSurfaceCommandOptions options)
        => new(
            new TaskId(options.TaskId ?? string.Empty),
            ParseWorkflowTaskState(options.Status),
            BuildOptionalWorkflowOwner(options));

    private static object BuildWorkflowBoardPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "workflowId", options.WorkflowId);
        return payload;
    }

    private static GetWorkflowBoard BuildWorkflowBoardRequest(RuntimeSurfaceCommandOptions options)
        => new(new WorkflowId(options.WorkflowId ?? string.Empty));

    private static object BuildTaskBoardPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "workflowId", options.WorkflowId);
        return payload;
    }

    private static GetTaskBoard BuildTaskBoardRequest(RuntimeSurfaceCommandOptions options)
        => new(new WorkflowId(options.WorkflowId ?? string.Empty));

    private static object BuildPlanProjectionPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "workflowId", options.WorkflowId);
        return payload;
    }

    private static GetPlanProjection BuildPlanProjectionRequest(RuntimeSurfaceCommandOptions options)
        => new(new WorkflowId(options.WorkflowId ?? string.Empty));

    private static ParticipantRef? BuildOptionalWorkflowOwner(RuntimeSurfaceCommandOptions options)
        => string.IsNullOrWhiteSpace(options.ParticipantId)
            ? null
            : new ParticipantRef(
                new ParticipantId(options.ParticipantId),
                ParticipantKind.Agent,
                options.ParticipantId);

    private static object[] BuildWorkflowPlanStepPayloads(RuntimeSurfaceCommandOptions options)
        => BuildWorkflowPlanSteps(options)
            .Select(
                static step => (object)new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["order"] = step.Order,
                    ["title"] = step.Title,
                    ["description"] = step.Description,
                })
            .ToArray();

    private static IReadOnlyList<PlanStep> BuildWorkflowPlanSteps(RuntimeSurfaceCommandOptions options)
    {
        if (!CliStructuredPayloadReader.TryReadTypedArrayPayload<WorkflowPlanStepInput>(
                options.ItemsJson,
                options.ItemsFilePath,
                "workflow 计划步骤",
                out var rawSteps,
                out var error))
        {
            throw new InvalidOperationException(error);
        }

        if (rawSteps is null || rawSteps.Count == 0)
        {
            return Array.Empty<PlanStep>();
        }

        return rawSteps
            .Select(
                static (step, index) => new PlanStep(
                    step.Order ?? index,
                    step.Title ?? throw new InvalidOperationException("workflow 计划步骤缺少 title。"),
                    step.Description))
            .ToArray();
    }

    private static TaskState ParseWorkflowTaskState(string? value)
        => NormalizeWorkflowTaskStateText(value) switch
        {
            "todo" => TaskState.Todo,
            "in-progress" => TaskState.InProgress,
            "blocked" => TaskState.Blocked,
            "done" => TaskState.Done,
            "cancelled" => TaskState.Cancelled,
            _ => throw new InvalidOperationException($"不支持的 workflow task state：{value ?? "<null>"}"),
        };

    private static string? NormalizeWorkflowTaskStateText(string? value)
        => Normalize(value)?.Trim().ToLowerInvariant() switch
        {
            "todo" => "todo",
            "in-progress" => "in-progress",
            "inprogress" => "in-progress",
            "blocked" => "blocked",
            "done" => "done",
            "cancelled" => "cancelled",
            "canceled" => "cancelled",
            _ => Normalize(value),
        };

    private static object BuildAgentListPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "limit", options.Limit);
        AddIfNotNull(payload, "cursor", options.Cursor);
        AddIfTrue(payload, "includePrimaryThreads", options.IncludePrimaryThreads);
        return payload;
    }

    private static ControlPlaneAgentListQuery BuildAgentListRequest(RuntimeSurfaceCommandOptions options)
        => new()
        {
            Limit = options.Limit,
            Cursor = Normalize(options.Cursor),
            IncludePrimaryThreads = options.IncludePrimaryThreads,
        };

    private static object BuildAgentRosterPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "workflowId", options.WorkflowId);
        return payload;
    }

    private static GetAgentRoster BuildAgentRosterRequest(RuntimeSurfaceCommandOptions options)
        => string.IsNullOrWhiteSpace(options.WorkflowId)
            ? new GetAgentRoster()
            : new GetAgentRoster(new WorkflowId(options.WorkflowId));

    private static object BuildTeamProjectionPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "teamId", options.TeamId);
        return payload;
    }

    private static GetTeamProjection BuildTeamProjectionRequest(RuntimeSurfaceCommandOptions options)
        => new(new TeamId(options.TeamId ?? string.Empty));

    private static object BuildAgentThreadRegisterPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "threadId", options.ThreadId);
        AddIfNotNull(payload, "agentNickname", options.AgentNickname);
        AddIfNotNull(payload, "agentRole", options.AgentRole);
        return payload;
    }

    private static ControlPlaneRegisterAgentThreadCommand BuildRuntimeSurfaceRegisterAgentThreadCommand(RuntimeSurfaceCommandOptions options)
        => new()
        {
            ThreadId = new ThreadId(options.ThreadId ?? string.Empty),
            AgentNickname = Normalize(options.AgentNickname),
            AgentRole = Normalize(options.AgentRole),
        };

    private static object BuildAgentJobCreatePayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "jobId", options.JobId);
        AddIfNotNull(payload, "name", options.Name);
        AddIfNotNull(payload, "instruction", options.Instruction);

        var inputHeaders = ReadStructuredJsonPayload(options.InputHeadersJson, options.InputHeadersFilePath, "agent input headers");
        if (inputHeaders is not null)
        {
            payload["inputHeaders"] = inputHeaders.ToPlainObject();
        }

        AddIfNotNull(payload, "inputCsvPath", options.InputCsvPath);
        AddIfNotNull(payload, "outputCsvPath", options.OutputCsvPath);
        AddIfNotNull(payload, "autoExport", options.AutoExport);

        var outputSchema = ReadStructuredJsonPayload(options.OutputSchemaJson, options.OutputSchemaFilePath, "agent output schema");
        if (outputSchema is not null)
        {
            payload["outputSchema"] = outputSchema.ToPlainObject();
        }

        var items = ReadStructuredArrayPayload(options.ItemsJson, options.ItemsFilePath, "agent items payload");
        if (items is not null)
        {
            payload["items"] = items.Select(static item => item.ToPlainObject()).ToArray();
        }

        return payload;
    }

    private static ControlPlaneCreateJobCommand BuildRuntimeSurfaceCreateJobCommand(RuntimeSurfaceCommandOptions options)
        => new()
        {
            JobId = string.IsNullOrWhiteSpace(options.JobId) ? null : new JobId(options.JobId),
            Name = Normalize(options.Name),
            Instruction = options.Instruction ?? string.Empty,
            InputHeaders = ToControlPlaneStructuredValue(ReadStructuredJsonPayload(options.InputHeadersJson, options.InputHeadersFilePath, "agent input headers")),
            InputCsvPath = Normalize(options.InputCsvPath),
            OutputCsvPath = Normalize(options.OutputCsvPath),
            AutoExport = options.AutoExport,
            OutputSchema = ToControlPlaneStructuredValue(ReadStructuredJsonPayload(options.OutputSchemaJson, options.OutputSchemaFilePath, "agent output schema")),
            Items = ReadStructuredArrayPayload(options.ItemsJson, options.ItemsFilePath, "agent items payload")?.Select(
                    static item => ControlPlaneStructuredValue.FromPlainObject(item.ToPlainObject()))
                .ToArray()
                ?? Array.Empty<ControlPlaneStructuredValue>(),
        };

    private static object BuildAgentJobDispatchPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "jobId", options.JobId);
        if (options.DispatchThreadIds.Count > 0)
        {
            payload["threadIds"] = options.DispatchThreadIds
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return payload;
    }

    private static ControlPlaneDispatchJobCommand BuildRuntimeSurfaceDispatchJobCommand(RuntimeSurfaceCommandOptions options)
        => new()
        {
            JobId = new JobId(options.JobId ?? string.Empty),
            ThreadIds = options.DispatchThreadIds
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(static threadId => new ThreadId(threadId))
                .ToArray(),
        };

    private static object BuildAgentJobItemReportPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "jobId", options.JobId);
        AddIfNotNull(payload, "itemId", options.ItemId);
        AddIfNotNull(payload, "status", options.Status);

        var result = ReadStructuredJsonPayload(options.ResultJson, options.ResultFilePath, "agent result payload");
        if (result is not null)
        {
            payload["result"] = result.ToPlainObject();
        }

        AddIfNotNull(payload, "lastError", options.LastError);
        return payload;
    }

    private static ControlPlaneReportJobItemCommand BuildRuntimeSurfaceReportJobItemCommand(RuntimeSurfaceCommandOptions options)
        => new()
        {
            JobId = new JobId(options.JobId ?? string.Empty),
            ItemId = new JobItemId(options.ItemId ?? string.Empty),
            Status = options.Status ?? string.Empty,
            Result = ToControlPlaneStructuredValue(ReadStructuredJsonPayload(options.ResultJson, options.ResultFilePath, "agent result payload")),
            LastError = Normalize(options.LastError),
        };

    private static object BuildAgentJobReadPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "jobId", options.JobId);
        return payload;
    }

    private static ControlPlaneReadJobQuery BuildRuntimeSurfaceReadJobQuery(RuntimeSurfaceCommandOptions options)
        => new()
        {
            JobId = new JobId(options.JobId ?? string.Empty),
        };

    private static object BuildAccountProfilePayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "accountId", options.AccountId);
        return payload;
    }

    private static GetAccountProfile BuildAccountProfileRequest(RuntimeSurfaceCommandOptions options)
        => new(new AccountId(options.AccountId ?? string.Empty));

    private static object BuildBoundDeviceListPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "accountId", options.AccountId);
        return payload;
    }

    private static ListBoundDevices BuildBoundDeviceListRequest(RuntimeSurfaceCommandOptions options)
        => new(new AccountId(options.AccountId ?? string.Empty));

    private static object BuildMemorySpaceListPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        if (options.MemoryScopeKind is { } scopeKind)
        {
            payload["scopeKind"] = scopeKind.ToString().ToLowerInvariant();
        }

        return payload;
    }

    private static object BuildMemoryProviderListPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = BuildTypedPayloadObject(options, "memory providers payload");
        if (payload is not null)
        {
            return payload;
        }

        var result = new Dictionary<string, object?>();
        if (options.MemoryScopeKind is { } scopeKind)
        {
            result["scopeKind"] = scopeKind.ToString().ToLowerInvariant();
        }

        return result;
    }

    private static ListMemoryProviders BuildMemoryProviderListRequest(RuntimeSurfaceCommandOptions options)
        => ReadTypedPayload<ListMemoryProviders>(options, "memory providers payload") ?? new(options.MemoryScopeKind);

    private static ListMemorySpaces BuildMemorySpaceListRequest(RuntimeSurfaceCommandOptions options)
        => new(options.MemoryScopeKind);

    private static object BuildMemoryOverlayPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "memorySpaceId", options.MemorySpaceId);
        AddIfNotNull(payload, "spaceId", options.CollaborationSpaceId);
        return payload;
    }

    private static ResolveMemoryOverlay BuildMemoryOverlayRequest(RuntimeSurfaceCommandOptions options)
        => new(
            MemorySpaceId: string.IsNullOrWhiteSpace(options.MemorySpaceId)
                ? null
                : new MemorySpaceId(options.MemorySpaceId),
            CollaborationSpaceId: string.IsNullOrWhiteSpace(options.CollaborationSpaceId)
                ? null
                : new CollaborationSpaceId(options.CollaborationSpaceId));

    private static FilterMemory BuildMemoryFilterRequest(RuntimeSurfaceCommandOptions options)
        => ReadTypedPayload<FilterMemory>(options, "memory filter payload") ?? new();

    private static AddMemory BuildMemoryAddRequest(RuntimeSurfaceCommandOptions options)
        => ReadRequiredTypedPayload<AddMemory>(options, "memory add payload");

    private static ExtractMemory BuildMemoryExtractRequest(RuntimeSurfaceCommandOptions options)
        => ReadRequiredTypedPayload<ExtractMemory>(options, "memory extract payload");

    private static ImportMemory BuildMemoryImportRequest(RuntimeSurfaceCommandOptions options)
        => ReadRequiredTypedPayload<ImportMemory>(options, "memory import payload");

    private static ExportMemory BuildMemoryExportRequest(RuntimeSurfaceCommandOptions options)
        => ReadRequiredTypedPayload<ExportMemory>(options, "memory export payload");

    private static BindMemoryProvider BuildMemoryBindProviderRequest(RuntimeSurfaceCommandOptions options)
        => ReadRequiredTypedPayload<BindMemoryProvider>(options, "memory provider binding payload");

    private static RunMemoryConsolidation BuildMemoryConsolidationRequest(RuntimeSurfaceCommandOptions options)
        => ReadTypedPayload<RunMemoryConsolidation>(options, "memory consolidation payload") ?? new();

    private static ForgetMemory BuildMemoryForgetRequest(RuntimeSurfaceCommandOptions options)
        => ReadRequiredTypedPayload<ForgetMemory>(options, "memory forget payload");

    private static DeleteMemory BuildMemoryDeleteRequest(RuntimeSurfaceCommandOptions options)
        => ReadRequiredTypedPayload<DeleteMemory>(options, "memory delete payload");

    private static SupersedeMemory BuildMemorySupersedeRequest(RuntimeSurfaceCommandOptions options)
        => ReadRequiredTypedPayload<SupersedeMemory>(options, "memory supersede payload");

    private static ListMemoryReviews BuildMemoryReviewListRequest(RuntimeSurfaceCommandOptions options)
        => ReadTypedPayload<ListMemoryReviews>(options, "memory review list payload") ?? new();

    private static ApproveMemoryReview BuildMemoryReviewApproveRequest(RuntimeSurfaceCommandOptions options)
        => ReadRequiredTypedPayload<ApproveMemoryReview>(options, "memory review approve payload");

    private static DemoteMemoryReview BuildMemoryReviewDemoteRequest(RuntimeSurfaceCommandOptions options)
        => ReadRequiredTypedPayload<DemoteMemoryReview>(options, "memory review demote payload");

    private static MergeMemoryReview BuildMemoryReviewMergeRequest(RuntimeSurfaceCommandOptions options)
        => ReadRequiredTypedPayload<MergeMemoryReview>(options, "memory review merge payload");

    private static RestoreMemoryReview BuildMemoryReviewRestoreRequest(RuntimeSurfaceCommandOptions options)
        => ReadRequiredTypedPayload<RestoreMemoryReview>(options, "memory review restore payload");

    private static RecordMemoryFeedback BuildMemoryFeedbackRequest(RuntimeSurfaceCommandOptions options)
        => ReadRequiredTypedPayload<RecordMemoryFeedback>(options, "memory feedback payload");

    private static RecordMemoryCitation BuildMemoryCitationRequest(RuntimeSurfaceCommandOptions options)
        => ReadRequiredTypedPayload<RecordMemoryCitation>(options, "memory citation payload");

    private static object BuildExecutionTracePayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "traceId", options.TraceId);
        return payload;
    }

    private static GetExecutionTrace BuildExecutionTraceRequest(RuntimeSurfaceCommandOptions options)
        => new(new ExecutionTraceId(options.TraceId ?? string.Empty));

    private static object BuildAttemptSummaryListPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "executionId", options.ExecutionId);
        return payload;
    }

    private static ListAttemptSummaries BuildAttemptSummaryListRequest(RuntimeSurfaceCommandOptions options)
        => new(new ExecutionId(options.ExecutionId ?? string.Empty));

    private static object BuildModelListPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "limit", options.Limit);
        AddIfNotNull(payload, "cursor", options.Cursor);
        AddIfTrue(payload, "includeHidden", options.IncludeHidden);
        return payload;
    }

    private static ControlPlaneModelCatalogQuery BuildModelListRequest(RuntimeSurfaceCommandOptions options)
    {
        return new ControlPlaneModelCatalogQuery
        {
            Limit = options.Limit ?? 50,
            Cursor = Normalize(options.Cursor),
            IncludeHidden = options.IncludeHidden,
        };
    }

    private static object BuildModelCatalogPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "cwd", options.WorkingDirectory);
        AddIfNotNull(payload, "limit", options.Limit);
        AddIfTrue(payload, "includeHidden", options.IncludeHidden);
        return payload;
    }

    private static GetCapabilityCatalog BuildModelCatalogRequest(RuntimeSurfaceCommandOptions options)
        => new(
            workspacePath: Normalize(options.WorkingDirectory),
            includeHiddenModels: options.IncludeHidden,
            modelLimit: options.Limit ?? 200);

    private static object BuildToolCatalogPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "cwd", options.WorkingDirectory);
        AddIfTrue(payload, "includeHidden", options.IncludeHidden);
        return payload;
    }

    private static object BuildToolConfigExportPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "cwd", options.WorkingDirectory);
        AddIfTrue(payload, "includeHidden", true);
        AddIfNotNull(payload, "out", options.ToolConfigOutputPath);
        return payload;
    }

    private static GetCapabilityCatalog BuildToolCatalogRequest(RuntimeSurfaceCommandOptions options)
        => new(
            workspacePath: Normalize(options.WorkingDirectory),
            includeHiddenTools: options.CommandKind == RuntimeSurfaceCommandKind.ToolConfigExport || options.IncludeHidden);

    private static object BuildModelResolvePayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "cwd", options.WorkingDirectory);
        AddIfNotNull(payload, "providerKey", options.ProviderKey);
        AddIfNotNull(payload, "modelKey", options.ModelKey);
        AddIfNotNull(payload, "reasoningEffort", options.ReasoningEffort);
        AddIfNotNull(payload, "reasoningSummary", options.ReasoningSummary);
        AddIfNotNull(payload, "verbosity", options.Verbosity);
        AddIfTrue(payload, "preferWebsocketTransport", options.PreferWebsocketTransport);
        return payload;
    }

    private static ResolveEngineBinding BuildModelResolveRequest(RuntimeSurfaceCommandOptions options)
        => new(
            WorkspacePath: Normalize(options.WorkingDirectory),
            PreferredProviderKey: Normalize(options.ProviderKey),
            PreferredModelKey: Normalize(options.ModelKey),
            ReasoningEffort: Normalize(options.ReasoningEffort),
            ReasoningSummary: Normalize(options.ReasoningSummary),
            Verbosity: Normalize(options.Verbosity),
            PreferWebsocketTransport: options.PreferWebsocketTransport);

    private static ControlPlaneAppCatalogQuery BuildAppListRequest(RuntimeSurfaceCommandOptions options)
    {
        var threadId = Normalize(options.ThreadId);
        return new ControlPlaneAppCatalogQuery
        {
            Limit = options.Limit,
            Cursor = Normalize(options.Cursor),
            ThreadId = string.IsNullOrWhiteSpace(threadId) ? null : new ThreadId(threadId),
            ForceRefetch = options.ForceRefetch,
        };
    }

    private static ControlPlaneSkillCatalogQuery BuildSkillsListRequest(RuntimeSurfaceCommandOptions options)
        => new()
        {
            WorkingDirectories = new[] { options.WorkingDirectory },
            ForceReload = options.ForceReload,
            ExtraRootsByWorkingDirectory = options.ExtraRoots.Count == 0
                ? Array.Empty<ControlPlaneSkillsExtraRootsForWorkingDirectory>()
                : new[]
                {
                    new ControlPlaneSkillsExtraRootsForWorkingDirectory
                    {
                        WorkingDirectory = options.WorkingDirectory,
                        ExtraUserRoots = options.ExtraRoots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    },
                },
        };

    private static ControlPlaneSkillConfigWriteCommand BuildSkillsConfigWriteRequest(RuntimeSurfaceCommandOptions options)
        => new()
        {
            Path = options.SkillPath!,
            Enabled = options.Enabled!.Value,
            WorkingDirectory = options.WorkingDirectory,
        };

    private static ControlPlaneRemoteSkillCatalogQuery BuildSkillsRemoteListRequest(RuntimeSurfaceCommandOptions options)
        => new()
        {
            HazelnutScope = Normalize(options.HazelnutScope),
            ProductSurface = Normalize(options.ProductSurface),
            Enabled = options.RemoteEnabled,
        };

    private static ControlPlaneRemoteSkillExportCommand BuildSkillsRemoteExportRequest(RuntimeSurfaceCommandOptions options)
        => new()
        {
            HazelnutId = options.HazelnutId!,
        };

    private static ControlPlanePluginCatalogQuery BuildPluginListRequest(RuntimeSurfaceCommandOptions options)
        => new()
        {
            WorkingDirectories = new[] { options.WorkingDirectory },
            ForceRemoteSync = options.ForceRemoteSync,
        };

    private static ControlPlanePluginReadQuery BuildPluginReadRequest(RuntimeSurfaceCommandOptions options)
        => new()
        {
            MarketplacePath = options.MarketplacePath!,
            PluginName = options.PluginName!,
        };

    private static ControlPlanePluginInstallCommand BuildPluginInstallRequest(RuntimeSurfaceCommandOptions options)
        => new()
        {
            MarketplacePath = options.MarketplacePath!,
            PluginName = options.PluginName!,
            WorkingDirectory = options.WorkingDirectory,
        };

    private static ControlPlanePluginUninstallCommand BuildPluginUninstallRequest(RuntimeSurfaceCommandOptions options)
        => new()
        {
            PluginId = options.PluginId!,
            WorkingDirectory = options.WorkingDirectory,
        };

    private static object BuildSkillsListPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>
        {
            ["cwds"] = new[] { options.WorkingDirectory },
        };

        AddIfTrue(payload, "forceReload", options.ForceReload);
        if (options.ExtraRoots.Count > 0)
        {
            payload["perCwdExtraUserRoots"] = new[]
            {
                new
                {
                    cwd = options.WorkingDirectory,
                    extraUserRoots = options.ExtraRoots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                },
            };
        }

        return payload;
    }

    private static object BuildSkillsConfigWritePayload(RuntimeSurfaceCommandOptions options)
        => new Dictionary<string, object?>
        {
            ["path"] = options.SkillPath!,
            ["enabled"] = options.Enabled!.Value,
            ["cwd"] = options.WorkingDirectory,
        };


    private static object BuildSkillsRemoteListPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "hazelnutScope", options.HazelnutScope);
        AddIfNotNull(payload, "productSurface", options.ProductSurface);
        AddIfNotNull(payload, "enabled", options.RemoteEnabled);
        return payload;
    }

    private static object BuildSkillsRemoteExportPayload(RuntimeSurfaceCommandOptions options)
        => new Dictionary<string, object?>
        {
            ["hazelnutId"] = options.HazelnutId!,
        };

    private static object BuildMcpServerOauthLoginPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>
        {
            ["name"] = options.McpServerName!,
        };
        AddIfNotNull(payload, "timeoutSecs", options.TimeoutSecs);
        return payload;
    }

    private static IReadOnlyList<StructuredValue>? ReadStructuredArrayPayload(string? inlineJson, string? filePath, string subject)
    {
        var payload = ReadJsonPayload(inlineJson, filePath, subject, requireObjectOrArray: false);
        if (!payload.HasValue)
        {
            return null;
        }

        if (payload.Value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"{subject} must be a JSON array.");
        }

        return payload.Value.EnumerateArray().Select(StructuredValue.FromJsonElement).ToArray();
    }

    private static StructuredValue? ReadStructuredJsonPayload(string? inlineJson, string? filePath, string subject)
    {
        var payload = ReadJsonPayload(inlineJson, filePath, subject, requireObjectOrArray: false);
        return payload.HasValue ? StructuredValue.FromJsonElement(payload.Value) : null;
    }

    private static object BuildPluginListPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>
        {
            ["cwds"] = new[] { options.WorkingDirectory },
        };
        AddIfTrue(payload, "forceRemoteSync", options.ForceRemoteSync);
        return payload;
    }

    private static object BuildPluginReadPayload(RuntimeSurfaceCommandOptions options)
        => new Dictionary<string, object?>
        {
            ["marketplacePath"] = options.MarketplacePath!,
            ["pluginName"] = options.PluginName!,
        };

    private static object BuildPluginInstallPayload(RuntimeSurfaceCommandOptions options)
        => new Dictionary<string, object?>
        {
            ["marketplacePath"] = options.MarketplacePath!,
            ["pluginName"] = options.PluginName!,
            ["cwd"] = options.WorkingDirectory,
        };

    private static object BuildPluginUninstallPayload(RuntimeSurfaceCommandOptions options)
        => new Dictionary<string, object?>
        {
            ["pluginId"] = options.PluginId!,
            ["cwd"] = options.WorkingDirectory,
        };

    private static object BuildAppListPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "limit", options.Limit);
        AddIfNotNull(payload, "cursor", options.Cursor);
        AddIfNotNull(payload, "threadId", options.ThreadId);
        AddIfTrue(payload, "forceRefetch", options.ForceRefetch);
        return payload;
    }

    private static object BuildReviewStartPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>
        {
            ["threadId"] = options.ThreadId!,
            ["target"] = BuildReviewTargetPayload(options),
        };
        AddIfNotNull(payload, "delivery", options.Delivery);
        return payload;
    }

    private static ControlPlaneReviewStartCommand BuildReviewStartRequest(RuntimeSurfaceCommandOptions options)
        => new()
        {
            ThreadId = options.ThreadId!,
            Target = BuildReviewTargetRequest(options),
            Delivery = Normalize(options.Delivery),
        };

    private static ControlPlaneReviewTarget BuildReviewTargetRequest(RuntimeSurfaceCommandOptions options)
        => options.ReviewTargetType switch
        {
            "uncommittedChanges" => new ControlPlaneReviewUncommittedChangesTarget(),
            "baseBranch" => new ControlPlaneReviewBaseBranchTarget
            {
                Branch = options.ReviewBranch!,
            },
            "commit" => new ControlPlaneReviewCommitTarget
            {
                Sha = options.ReviewSha!,
                Title = Normalize(options.ReviewTitle),
            },
            "custom" => new ControlPlaneReviewCustomTarget
            {
                Instructions = options.ReviewInstructions!,
            },
            _ => throw new InvalidOperationException($"不支持的 review target：{options.ReviewTargetType}"),
        };

    private static object BuildReviewTargetPayload(RuntimeSurfaceCommandOptions options)
        => options.ReviewTargetType switch
        {
            "uncommittedChanges" => new Dictionary<string, object?>
            {
                ["type"] = "uncommittedChanges",
            },
            "baseBranch" => new Dictionary<string, object?>
            {
                ["type"] = "baseBranch",
                ["branch"] = options.ReviewBranch!,
            },
            "commit" => BuildReviewCommitTargetPayload(options),
            "custom" => new Dictionary<string, object?>
            {
                ["type"] = "custom",
                ["instructions"] = options.ReviewInstructions!,
            },
            _ => throw new InvalidOperationException($"不支持的 review target：{options.ReviewTargetType}"),
        };

    private static object BuildReviewCommitTargetPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>
        {
            ["type"] = "commit",
            ["sha"] = options.ReviewSha!,
        };
        AddIfNotNull(payload, "title", options.ReviewTitle);
        return payload;
    }

    private static object BuildThreadLoadedListPayload(ThreadCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "limit", options.Limit);
        AddIfNotNull(payload, "cursor", options.Cursor);
        return payload;
    }

    private static object BuildThreadCompactPayload(ThreadCommandOptions options)
        => new Dictionary<string, object?>
        {
            ["threadId"] = options.ThreadId!,
            ["keepRecentTurns"] = options.KeepRecentTurns,
        };

    private static object BuildThreadIdPayload(ThreadCommandOptions options)
        => new Dictionary<string, object?>
        {
            ["threadId"] = options.ThreadId!,
        };

    private static object BuildThreadReadPayload(ThreadCommandOptions options)
    {
        var payload = new Dictionary<string, object?>
        {
            ["threadId"] = options.ThreadId!,
        };
        AddIfTrue(payload, "includeTurns", options.IncludeTurns);
        return payload;
    }

    private static object BuildThreadMetadataPayload(ThreadCommandOptions options)
    {
        var gitInfo = new Dictionary<string, object?>();
        if (options.ClearGitSha || !string.IsNullOrWhiteSpace(options.GitSha))
        {
            gitInfo["sha"] = options.ClearGitSha ? null : options.GitSha;
        }

        if (options.ClearGitBranch || !string.IsNullOrWhiteSpace(options.GitBranch))
        {
            gitInfo["branch"] = options.ClearGitBranch ? null : options.GitBranch;
        }

        if (options.ClearGitOriginUrl || !string.IsNullOrWhiteSpace(options.GitOriginUrl))
        {
            gitInfo["originUrl"] = options.ClearGitOriginUrl ? null : options.GitOriginUrl;
        }

        return new Dictionary<string, object?>
        {
            ["threadId"] = options.ThreadId!,
            ["gitInfo"] = gitInfo,
        };
    }

    private static object BuildThreadRollbackPayload(ThreadCommandOptions options)
        => new Dictionary<string, object?>
        {
            ["threadId"] = options.ThreadId!,
            ["numTurns"] = options.NumTurns!.Value,
        };

    private static object BuildConversationSummaryPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "threadId", options.ThreadId);
        AddIfNotNull(payload, "rolloutPath", options.RolloutPath);
        return payload;
    }

    private static ControlPlaneExperimentalFeatureQuery BuildExperimentalFeatureListRequest(RuntimeSurfaceCommandOptions options)
        => new()
        {
            Limit = options.Limit,
            Cursor = Normalize(options.Cursor),
        };

    private static object? BuildFeatureListPayload()
        => null;

    private static object BuildFeatureConfigWritePayload(RuntimeSurfaceCommandOptions options)
        => new Dictionary<string, object?>
        {
            ["keyPath"] = BuildFeatureConfigKeyPath(options),
            ["value"] = options.Enabled == true,
            ["mergeStrategy"] = "replace",
            ["cwd"] = options.WorkingDirectory,
        };

    private static ControlPlaneConfigValueWriteCommand BuildFeatureConfigWriteRequest(RuntimeSurfaceCommandOptions options)
    {
        var value = JsonSerializer.SerializeToElement(options.Enabled == true);
        return new ControlPlaneConfigValueWriteCommand
        {
            KeyPath = BuildFeatureConfigKeyPath(options),
            Value = ToStructuredValue(value),
            MergeStrategy = "replace",
            WorkingDirectory = options.WorkingDirectory,
        };
    }

    private static string BuildFeatureConfigKeyPath(RuntimeSurfaceCommandOptions options)
        => $"features.{options.FeatureName ?? string.Empty}";

    private static ControlPlaneMcpServerStatusQuery BuildMcpServerStatusListRequest(RuntimeSurfaceCommandOptions options)
        => new()
        {
            Limit = options.Limit,
            Cursor = Normalize(options.Cursor),
        };

    internal static ControlPlaneMcpServerOauthLoginStartCommand BuildMcpServerOauthLoginRequest(RuntimeSurfaceCommandOptions options)
        => new()
        {
            Name = options.McpServerName ?? string.Empty,
            TimeoutSecs = options.TimeoutSecs,
        };

    private static ControlPlaneConfigBatchWriteCommand BuildMcpAddRequest(McpCommandOptions options)
    {
        var items = new List<ControlPlaneConfigWriteItem>();
        var keyPrefix = $"mcp_servers.{options.Name}";

        if (!string.IsNullOrWhiteSpace(options.Url))
        {
            items.Add(BuildConfigWriteItem($"{keyPrefix}.url", options.Url));
            items.Add(BuildConfigWriteItem($"{keyPrefix}.bearer_token_env_var", options.BearerTokenEnvVar));
            items.Add(BuildConfigWriteItem($"{keyPrefix}.command", value: null));
            items.Add(BuildConfigWriteItem($"{keyPrefix}.args", value: null));
            items.Add(BuildConfigWriteItem($"{keyPrefix}.env", value: null));
            items.Add(BuildConfigWriteItem($"{keyPrefix}.env_vars", value: null));
            items.Add(BuildConfigWriteItem($"{keyPrefix}.cwd", value: null));
            items.Add(BuildConfigWriteItem($"{keyPrefix}.http_headers", value: null));
            items.Add(BuildConfigWriteItem($"{keyPrefix}.env_http_headers", value: null));
        }
        else
        {
            items.Add(BuildConfigWriteItem($"{keyPrefix}.command", options.Command[0]));
            items.Add(BuildConfigWriteItem($"{keyPrefix}.args", options.Command.Skip(1).ToArray()));
            items.Add(BuildConfigWriteItem(
                $"{keyPrefix}.env",
                options.EnvironmentVariables.Count == 0
                    ? null
                    : options.EnvironmentVariables));
            items.Add(BuildConfigWriteItem($"{keyPrefix}.env_vars", value: null));
            items.Add(BuildConfigWriteItem($"{keyPrefix}.cwd", value: null));
            items.Add(BuildConfigWriteItem($"{keyPrefix}.url", value: null));
            items.Add(BuildConfigWriteItem($"{keyPrefix}.bearer_token_env_var", value: null));
            items.Add(BuildConfigWriteItem($"{keyPrefix}.http_headers", value: null));
            items.Add(BuildConfigWriteItem($"{keyPrefix}.env_http_headers", value: null));
        }

        items.Add(BuildConfigWriteItem($"{keyPrefix}.enabled", true));
        return new ControlPlaneConfigBatchWriteCommand
        {
            Items = items,
            WorkingDirectory = null,
            FilePath = options.ConfigFilePath,
        };
    }

    private static ControlPlaneConfigBatchWriteCommand BuildMcpRemoveRequest(McpCommandOptions options)
        => new()
        {
            Items =
            [
                BuildConfigWriteItem($"mcp_servers.{options.Name}", value: null),
            ],
            WorkingDirectory = null,
            FilePath = options.ConfigFilePath,
        };

    private static ControlPlaneConfigWriteItem BuildConfigWriteItem(string keyPath, object? value, string mergeStrategy = "replace")
        => new()
        {
            KeyPath = keyPath,
            Value = ToStructuredValue(value),
            MergeStrategy = mergeStrategy,
        };

    private static ControlPlaneConversationArtifactQuery BuildConversationSummaryRequest(RuntimeSurfaceCommandOptions options)
        => new()
        {
            ThreadId = string.IsNullOrWhiteSpace(Normalize(options.ThreadId)) ? null : new ThreadId(options.ThreadId!),
            RolloutPath = Normalize(options.RolloutPath),
        };

    private static ControlPlaneGitDiffArtifactQuery BuildGitDiffToRemoteRequest(RuntimeSurfaceCommandOptions options)
        => new()
        {
            ThreadId = new ThreadId(options.ThreadId!),
        };

    private static object BuildGitDiffToRemotePayload(RuntimeSurfaceCommandOptions options)
        => new Dictionary<string, object?>
        {
            ["threadId"] = options.ThreadId!,
        };

    private static object BuildConfigReadPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>
        {
            ["cwd"] = options.WorkingDirectory,
        };
        AddIfTrue(payload, "includeLayers", options.IncludeLayers);
        return payload;
    }

    private static ControlPlaneConfigReadQuery BuildConfigReadRequest(RuntimeSurfaceCommandOptions options)
    {
        return new ControlPlaneConfigReadQuery
        {
            WorkingDirectory = options.WorkingDirectory,
            IncludeLayers = options.IncludeLayers,
        };
    }

    private static ControlPlaneConfigRequirementsQuery BuildConfigRequirementsReadRequest(RuntimeSurfaceCommandOptions options)
    {
        return new ControlPlaneConfigRequirementsQuery
        {
            WorkingDirectory = options.WorkingDirectory,
        };
    }

    private static object BuildConfigValueWritePayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>
        {
            ["keyPath"] = options.KeyPath!,
            ["value"] = ReadJsonPayload(options.ConfigValueJson, options.ConfigValueFilePath, "配置值", requireObjectOrArray: false)!.Value,
            ["mergeStrategy"] = options.MergeStrategy,
            ["cwd"] = options.WorkingDirectory,
        };
        AddIfNotNull(payload, "filePath", options.ConfigEditFilePath);
        AddIfNotNull(payload, "expectedVersion", options.ExpectedVersion);
        return payload;
    }

    private static ControlPlaneConfigValueWriteCommand BuildConfigValueWriteRequest(RuntimeSurfaceCommandOptions options)
    {
        var value = ReadJsonPayload(options.ConfigValueJson, options.ConfigValueFilePath, "配置值", requireObjectOrArray: false);
        return new ControlPlaneConfigValueWriteCommand
        {
            KeyPath = options.KeyPath!,
            Value = value.HasValue ? ToStructuredValue(value.Value) : null,
            MergeStrategy = options.MergeStrategy,
            WorkingDirectory = options.WorkingDirectory,
            FilePath = options.ConfigEditFilePath,
            ExpectedVersion = options.ExpectedVersion,
        };
    }

    private static object BuildConfigBatchWritePayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>
        {
            ["items"] = ReadBatchConfigItemsPayload(options),
            ["mergeStrategy"] = options.MergeStrategy,
            ["cwd"] = options.WorkingDirectory,
            ["reloadUserConfig"] = options.ReloadUserConfig,
        };
        AddIfNotNull(payload, "filePath", options.ConfigEditFilePath);
        AddIfNotNull(payload, "expectedVersion", options.ExpectedVersion);
        return payload;
    }

    private static ControlPlaneConfigBatchWriteCommand BuildConfigBatchWriteRequest(RuntimeSurfaceCommandOptions options)
    {
        var defaultMergeStrategy = options.MergeStrategy;
        var items = ReadBatchConfigItemsPayload(options)
            .EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new ControlPlaneConfigWriteItem
            {
                KeyPath = ReadString(item, "keyPath")
                          ?? ReadString(item, "key")
                          ?? ReadString(item, "path")
                          ?? string.Empty,
                Value = item.TryGetProperty("value", out var valueElement) ? ToStructuredValue(valueElement) : null,
                MergeStrategy = ReadString(item, "mergeStrategy")
                                ?? ReadString(item, "merge_strategy")
                                ?? defaultMergeStrategy,
            })
            .Where(static item => !string.IsNullOrWhiteSpace(item.KeyPath))
            .ToArray();

        return new ControlPlaneConfigBatchWriteCommand
        {
            Items = items,
            WorkingDirectory = options.WorkingDirectory,
            FilePath = options.ConfigEditFilePath,
            ExpectedVersion = options.ExpectedVersion,
            ReloadUserConfig = options.ReloadUserConfig,
        };
    }

    private static object BuildFuzzyFileSearchSearchPayload(FuzzyFileSearchCommandOptions options)
    {
        var payload = new Dictionary<string, object?>
        {
            ["query"] = options.Query!,
            ["cwd"] = options.WorkingDirectory,
        };
        AddIfNotNull(payload, "limit", options.Limit);
        if (options.Roots.Count > 0)
        {
            payload["roots"] = options.Roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        return payload;
    }

    private static object BuildFuzzyFileSearchSessionStartPayload(FuzzyFileSearchCommandOptions options)
        => new Dictionary<string, object?>
        {
            ["sessionId"] = options.SessionId!,
            ["roots"] = options.Roots.Count > 0
                ? options.Roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                : new[] { options.WorkingDirectory },
        };

    private static object BuildFuzzyFileSearchSessionUpdatePayload(FuzzyFileSearchCommandOptions options)
        => new Dictionary<string, object?>
        {
            ["sessionId"] = options.SessionId!,
            ["query"] = options.Query!,
        };

    private static object BuildFuzzyFileSearchSessionStopPayload(FuzzyFileSearchCommandOptions options)
        => new Dictionary<string, object?>
        {
            ["sessionId"] = options.SessionId!,
        };

    internal static ControlPlaneFuzzyFileSearchQuery BuildFuzzyFileSearchSearchRequest(FuzzyFileSearchCommandOptions options)
        => new()
        {
            Query = options.Query!,
            WorkingDirectory = options.WorkingDirectory,
            Limit = options.Limit,
            Roots = options.Roots
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        };

    internal static ControlPlaneStartFuzzyFileSearchSessionCommand BuildFuzzyFileSearchSessionStartRequest(FuzzyFileSearchCommandOptions options)
        => new()
        {
            SessionId = options.SessionId!,
            Roots = (options.Roots.Count > 0
                    ? options.Roots
                    : [options.WorkingDirectory])
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()!,
        };

    internal static ControlPlaneUpdateFuzzyFileSearchSessionCommand BuildFuzzyFileSearchSessionUpdateRequest(FuzzyFileSearchCommandOptions options)
        => new()
        {
            SessionId = options.SessionId!,
            Query = options.Query!,
        };

    internal static ControlPlaneStopFuzzyFileSearchSessionCommand BuildFuzzyFileSearchSessionStopRequest(FuzzyFileSearchCommandOptions options)
        => new()
        {
            SessionId = options.SessionId!,
        };

    private static object BuildFeedbackPayload(FeedbackCommandOptions options)
    {
        var payload = new Dictionary<string, object?>
        {
            ["classification"] = options.Classification!,
            ["includeLogs"] = options.IncludeLogs,
        };

        AddIfNotNull(payload, "threadId", options.ThreadId);
        AddIfNotNull(payload, "reason", options.Reason);
        if (options.ExtraLogFiles.Count > 0)
        {
            payload["extraLogFiles"] = options.ExtraLogFiles.ToArray();
        }

        return payload;
    }

    private static ControlPlaneFeedbackUploadCommand BuildFeedbackRequest(FeedbackCommandOptions options)
        => new()
        {
            Classification = options.Classification!,
            IncludeLogs = options.IncludeLogs,
            ThreadId = options.ThreadId,
            Reason = options.Reason,
            ExtraLogFiles = options.ExtraLogFiles.ToArray(),
        };

    private static object BuildWindowsSandboxPayload(WindowsSandboxCommandOptions options)
    {
        var payload = new Dictionary<string, object?>
        {
            ["mode"] = options.Mode!,
        };
        AddIfNotNull(payload, "cwd", options.SandboxCwd);
        return payload;
    }

    internal static ControlPlaneRealtimeStartCommand BuildRealtimeStartRequest(RealtimeCommandOptions options)
        => new()
        {
            ThreadId = new ThreadId(options.ThreadId!),
            SessionId = options.SessionId,
            Prompt = options.Prompt,
        };

    internal static ControlPlaneRealtimeAppendTextCommand BuildRealtimeAppendTextRequest(RealtimeCommandOptions options)
        => new()
        {
            ThreadId = new ThreadId(options.ThreadId!),
            SessionId = options.SessionId,
            Text = options.Text!,
        };

    internal static ControlPlaneRealtimeAppendAudioCommand BuildRealtimeAppendAudioRequest(RealtimeCommandOptions options)
        => new()
        {
            ThreadId = new ThreadId(options.ThreadId!),
            SessionId = options.SessionId,
            Audio = BuildRealtimeAudioInput(options),
        };

    internal static ControlPlaneRealtimeHandoffOutputCommand BuildRealtimeHandoffOutputRequest(RealtimeCommandOptions options)
        => new()
        {
            ThreadId = new ThreadId(options.ThreadId!),
            SessionId = options.SessionId,
            HandoffId = options.HandoffId!,
            Output = options.Output ?? string.Empty,
        };

    internal static ControlPlaneRealtimeStopCommand BuildRealtimeStopRequest(RealtimeCommandOptions options)
        => new()
        {
            ThreadId = new ThreadId(options.ThreadId!),
            SessionId = options.SessionId,
        };

    private static ControlPlaneRealtimeAudioInput BuildRealtimeAudioInput(RealtimeCommandOptions options)
    {
        var audio = ReadJsonObjectPayload(options.AudioJson, options.AudioFilePath, "realtime audio payload");
        if (!audio.HasValue)
        {
            throw new InvalidOperationException("realtime audio payload 不能为空。");
        }

        return new ControlPlaneRealtimeAudioInput
        {
            Data = ReadString(audio.Value, "data") ?? string.Empty,
            SampleRate = TryGetProperty(audio.Value, "sampleRate", out var sampleRate) && sampleRate.TryGetInt32(out var sampleRateValue)
                ? sampleRateValue
                : null,
            NumChannels = TryGetProperty(audio.Value, "numChannels", out var numChannels) && numChannels.TryGetInt32(out var numChannelsValue)
                ? numChannelsValue
                : null,
            SamplesPerChannel = TryGetProperty(audio.Value, "samplesPerChannel", out var samplesPerChannel)
                && samplesPerChannel.TryGetInt32(out var samplesPerChannelValue)
                    ? samplesPerChannelValue
                    : null,
        };
    }

    private static ControlPlaneCommandExecutionTerminalSize? BuildCommandExecTerminalSize(int? rows, int? cols)
    {
        if (!rows.HasValue || !cols.HasValue)
        {
            return null;
        }

        return BuildRequiredCommandExecTerminalSize(rows, cols);
    }

    private static ControlPlaneCommandExecutionTerminalSize BuildRequiredCommandExecTerminalSize(int? rows, int? cols)
    {
        if (!rows.HasValue || !cols.HasValue)
        {
            throw new InvalidOperationException("command exec resize 需要同时提供 rows 与 cols。");
        }

        return new ControlPlaneCommandExecutionTerminalSize
        {
            Rows = checked((ushort)rows.Value),
            Cols = checked((ushort)cols.Value),
        };
    }

    private static IReadOnlyDictionary<string, string?> ReadStringOrNullDictionaryPayload(string? inlineJson, string? filePath, string subject)
    {
        var payload = ReadJsonPayload(inlineJson, filePath, subject, requireObjectOrArray: false);
        if (!payload.HasValue)
        {
            return new Dictionary<string, string?>();
        }

        if (payload.Value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"{subject} 必须是 JSON 对象。");
        }

        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var property in payload.Value.EnumerateObject())
        {
            values[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Null => null,
                _ => throw new InvalidOperationException($"{subject} 的值必须是字符串或 null。key={property.Name}"),
            };
        }

        return values;
    }

    private static StructuredValue? ReadStructuredValuePayload(string? inlineJson, string? filePath, string subject)
    {
        var payload = ReadJsonPayload(inlineJson, filePath, subject, requireObjectOrArray: false);
        return payload.HasValue ? StructuredValue.FromJsonElement(payload.Value) : null;
    }

    private static object BuildListPayload(RuntimeSurfaceCommandOptions options)
    {
        var payload = new Dictionary<string, object?>();
        AddIfNotNull(payload, "limit", options.Limit);
        AddIfNotNull(payload, "cursor", options.Cursor);
        return payload;
    }

    private static JsonElement ReadBatchConfigItemsPayload(RuntimeSurfaceCommandOptions options)
    {
        var payloadText = !string.IsNullOrWhiteSpace(options.BatchItemsFilePath)
            ? File.ReadAllText(options.BatchItemsFilePath!)
            : options.BatchItemsJson;
        if (string.IsNullOrWhiteSpace(payloadText))
        {
            throw new InvalidOperationException("批量配置写入缺少 items。请提供 --items-json 或 --items-file。");
        }

        using var document = JsonDocument.Parse(payloadText);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object && TryGetProperty(root, "items", out var items))
        {
            if (items.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("批量配置 items 必须是数组。");
            }

            return items.Clone();
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("--items-json/--items-file 必须是 items 数组，或包含 items 数组的对象。");
        }

        return root.Clone();
    }

    private static JsonElement? ReadJsonPayload(string? inlineJson, string? filePath, string subject, bool requireObjectOrArray)
    {
        if (string.IsNullOrWhiteSpace(inlineJson) && string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(inlineJson) && !string.IsNullOrWhiteSpace(filePath))
        {
            throw new InvalidOperationException($"{subject} 不能同时提供内联 JSON 和文件路径。");
        }

        var payloadText = !string.IsNullOrWhiteSpace(filePath)
            ? File.ReadAllText(filePath!)
            : inlineJson!;
        using var document = JsonDocument.Parse(payloadText);
        var root = document.RootElement;
        if (requireObjectOrArray && root.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
        {
            throw new InvalidOperationException($"{subject} 必须是 JSON 对象或数组。");
        }

        return root.Clone();
    }

    private static object? BuildTypedPayloadObject(RuntimeSurfaceCommandOptions options, string subject)
    {
        var payload = ReadJsonPayload(options.PayloadJson, options.PayloadFilePath, subject, requireObjectOrArray: true);
        return payload.HasValue ? ConvertJsonElementToPlainObject(payload.Value) : null;
    }

    private static T? ReadTypedPayload<T>(RuntimeSurfaceCommandOptions options, string subject)
        where T : class
    {
        var payload = ReadJsonPayload(options.PayloadJson, options.PayloadFilePath, subject, requireObjectOrArray: true);
        if (!payload.HasValue)
        {
            return null;
        }

        var value = JsonSerializer.Deserialize<T>(payload.Value.GetRawText(), TypedPayloadJsonOptions);
        return value ?? throw new InvalidOperationException($"{subject} 无法反序列化为 {typeof(T).Name}。");
    }

    private static T ReadRequiredTypedPayload<T>(RuntimeSurfaceCommandOptions options, string subject)
        where T : class
        => ReadTypedPayload<T>(options, subject)
           ?? throw new InvalidOperationException($"{subject} 缺少有效 JSON 载荷。");

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

    private static JsonElement? ReadJsonObjectPayload(string? inlineJson, string? filePath, string subject)
    {
        var payload = ReadJsonPayload(inlineJson, filePath, subject, requireObjectOrArray: false);
        if (!payload.HasValue)
        {
            return null;
        }

        if (payload.Value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"{subject} must be a JSON object.");
        }

        return payload.Value;
    }

    private static string[] ReadStringArrayPayload(string? inlineJson, string? filePath, string subject)
    {
        var payload = ReadJsonPayload(inlineJson, filePath, subject, requireObjectOrArray: false);
        if (!payload.HasValue || payload.Value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"{subject} 必须是字符串数组。");
        }

        return payload.Value.EnumerateArray().Select(static item => item.GetString() ?? string.Empty).ToArray();
    }

    private static string ReadCodeModeInput(CodeModeCommandOptions options)
    {
        var inlineInput = Normalize(options.Input);
        if (!string.IsNullOrWhiteSpace(inlineInput))
        {
            return inlineInput;
        }

        if (!string.IsNullOrWhiteSpace(options.InputFilePath))
        {
            return File.ReadAllText(options.InputFilePath);
        }

        throw new InvalidOperationException("缺少 exec 输入。请提供 --input <text> 或 --input-file <path>。");
    }

    private static string? ReadCommandInputBase64(CommandExecCommandOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.InputBase64))
        {
            return options.InputBase64;
        }

        if (!string.IsNullOrWhiteSpace(options.InputText))
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(options.InputText));
        }

        if (!string.IsNullOrWhiteSpace(options.InputFilePath))
        {
            return Convert.ToBase64String(File.ReadAllBytes(options.InputFilePath!));
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

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetArray(JsonElement element, string name, out JsonElement array)
    {
        if (TryGetProperty(element, name, out array) && array.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        array = default;
        return false;
    }

    private static string? ReadString(JsonElement element, string name)
        => TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? ReadBool(JsonElement element, string name)
        => TryGetProperty(element, name, out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            ? value.GetBoolean()
            : null;

    private static int? ReadInt32(JsonElement element, string name)
        => TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)
            ? result
            : null;

    private static string[] ReadStringArray(JsonElement array)
        => array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(static item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : item.ToString()).ToArray()
            : Array.Empty<string>();

    private static string[] ReadFuzzyFileDisplayItems(JsonElement array)
        => array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(static item => item.ValueKind switch
            {
                JsonValueKind.Object => ReadString(item, "path") ?? ReadString(item, "fileName") ?? item.ToString(),
                JsonValueKind.String => item.GetString() ?? string.Empty,
                _ => item.ToString(),
            }).ToArray()
            : Array.Empty<string>();

    private static string[] ReadFuzzyFileDisplayItems(IReadOnlyList<ControlPlaneFuzzyFileSearchFile> files)
        => files.Select(static item => !string.IsNullOrWhiteSpace(item.Path) ? item.Path : item.FileName ?? string.Empty)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToArray();

    private static string[] ReadFuzzyFileDisplayItems(IReadOnlyList<FuzzyFileSearchFilePayload> files)
        => files.Select(static item => !string.IsNullOrWhiteSpace(item.Path) ? item.Path : item.FileName ?? string.Empty)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToArray();

    private static object? ReadJsonValue(JsonElement element)
        => JsonSerializer.Deserialize<object>(element.GetRawText());

    private static object? ConvertJsonElementToPlainObject(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(static property => property.Name, static property => ConvertJsonElementToPlainObject(property.Value)),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElementToPlainObject).ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var intValue) => intValue,
            JsonValueKind.Number when element.TryGetDouble(out var doubleValue) => doubleValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText(),
        };

    private static StructuredValue? ToStructuredValue(object? value)
        => value is null
            ? null
            : value is StructuredValue structuredValue
                ? structuredValue
            : value is JsonElement element
                ? StructuredValue.FromPlainObject(ConvertJsonElementToPlainObject(element))
                : StructuredValue.FromPlainObject(ConvertJsonElementToPlainObject(JsonSerializer.SerializeToElement(value)));

    private static object? ReadJsonTextAsPlainObject(string? jsonText, string? fallbackText)
    {
        if (!string.IsNullOrWhiteSpace(jsonText))
        {
            using var document = JsonDocument.Parse(jsonText);
            return ConvertJsonElementToPlainObject(document.RootElement);
        }

        return fallbackText;
    }

    private sealed class ThreadResumeReplayState
    {
        private readonly IExecutionRuntime runtime;
        private readonly IGovernanceControlPlane governance;
        private readonly ThreadCommandOptions options;
        private readonly ProbePermissionRequestScript? permissionScript;
        private readonly ProbeUserInputScript? userInputScript;
        private readonly CancellationToken cancellationToken;
        private readonly ConcurrentDictionary<string, CliPendingApprovalRequestState> pendingApprovals = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, CliPendingPermissionRequestState> pendingPermissionRequests = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, CliPendingUserInputRequestState> pendingUserInputs = new(StringComparer.Ordinal);
        private readonly ConcurrentQueue<ThreadResumePendingFollowUp> restoredPendingFollowUps = new();
        private readonly ConcurrentQueue<Task> autoResponseTasks = new();
        private readonly ConcurrentQueue<string> errors = new();
        private long lastObservedActivityUtcTicks = DateTime.UtcNow.Ticks;

        public ThreadResumeReplayState(
            IExecutionRuntime runtime,
            IGovernanceControlPlane governance,
            ThreadCommandOptions options,
            ProbePermissionRequestScript? permissionScript,
            ProbeUserInputScript? userInputScript,
            CancellationToken cancellationToken)
        {
            this.runtime = runtime;
            this.governance = governance;
            this.options = options;
            this.permissionScript = permissionScript;
            this.userInputScript = userInputScript;
            this.cancellationToken = cancellationToken;
        }

        public int ReplayedApprovalCount { get; private set; }

        public int ReplayedPermissionCount { get; private set; }

        public int ReplayedUserInputCount { get; private set; }

        public int ReplayedInteractiveRequestCount => ReplayedApprovalCount + ReplayedPermissionCount + ReplayedUserInputCount;

        public bool HasFailures => !errors.IsEmpty;

        public void OnStreamEvent(object? sender, ControlPlaneConversationStreamEventArgs args)
            => HandleStreamEvent(args.StreamEvent);

        public void ConsumeResumedThreadState(ControlPlaneThreadSnapshot resumed)
        {
            pendingApprovals.Clear();
            pendingPermissionRequests.Clear();
            pendingUserInputs.Clear();

            ReplayedApprovalCount = 0;
            ReplayedPermissionCount = 0;
            ReplayedUserInputCount = 0;

            foreach (var request in resumed.PendingInteractiveRequests.OrderBy(static item => item.RequestId))
            {
                switch (Normalize(request.RequestKind)?.ToLowerInvariant())
                {
                    case "approval_requested":
                        ReplayedApprovalCount++;
                        break;
                    case "permission_requested":
                        ReplayedPermissionCount++;
                        break;
                    case "request_user_input":
                        ReplayedUserInputCount++;
                        break;
                }

                var replayEvent = BuildPendingInteractiveReplayEvent(request);
                if (replayEvent is not null)
                {
                    HandleStreamEvent(replayEvent);
                }
            }

            while (restoredPendingFollowUps.TryDequeue(out _))
            {
            }

            var restored = ResolveRestorablePendingFollowUps(resumed.PendingInputState)
                .Select(entry => BuildRestoredPendingFollowUp(entry, resumed.PendingInputState?.SubmitPendingSteersAfterInterrupt ?? false))
                .Where(static item => item is not null)
                .Cast<ThreadResumePendingFollowUp>()
                .ToArray();
            foreach (var item in restored)
            {
                restoredPendingFollowUps.Enqueue(item);
            }

            if (!options.OutputJson && restored.Length > 0)
            {
                Console.WriteLine($"已恢复待编辑 follow-up：{restored.Length}。");
                foreach (var item in restored)
                {
                    Console.WriteLine($"已恢复到待编辑 {item.Mode} follow-up：{item.PreviewText}");
                }
            }
        }

        public async Task<bool> WaitForSettledAsync(TimeSpan timeout)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await AwaitBackgroundTasksAsync(autoResponseTasks).ConfigureAwait(false);
                if (HasFailures)
                {
                    return false;
                }

                if (await IsSettledAsync().ConfigureAwait(false))
                {
                    return true;
                }

                if (GetConversationIdleDuration() >= timeout)
                {
                    return await IsSettledAsync().ConfigureAwait(false) && !HasFailures;
                }

                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }

        public void WriteErrors()
        {
            while (errors.TryDequeue(out var error))
            {
                Console.Error.WriteLine(error);
            }
        }

        private async Task<bool> IsSettledAsync()
        {
            if (!pendingApprovals.IsEmpty
                || !pendingPermissionRequests.IsEmpty
                || !pendingUserInputs.IsEmpty)
            {
                return false;
            }

            var sessionSnapshot = await CliSessionSnapshotUtilities.GetSnapshotAsync(runtime, cancellationToken).ConfigureAwait(false);
            return !sessionSnapshot.HasActiveTurn;
        }

        private void HandleStreamEvent(ControlPlaneConversationStreamEvent streamEvent)
        {
            Touch();
            switch (streamEvent.Kind)
            {
                case ControlPlaneConversationStreamEventKind.ApprovalRequested:
                    HandleApprovalRequested(streamEvent);
                    break;
                case ControlPlaneConversationStreamEventKind.PermissionRequested:
                    HandlePermissionRequested(streamEvent);
                    break;
                case ControlPlaneConversationStreamEventKind.UserInputRequested:
                    HandleUserInputRequested(streamEvent);
                    break;
                case ControlPlaneConversationStreamEventKind.ServerRequestResolved:
                    HandleServerRequestResolved(streamEvent);
                    break;
                case ControlPlaneConversationStreamEventKind.TurnCompleted:
                    HandleTurnCompleted(streamEvent);
                    break;
            }
        }

        private void HandleServerRequestResolved(ControlPlaneConversationStreamEvent streamEvent)
        {
            var callId = streamEvent.CallId?.Value;
            if (!string.IsNullOrWhiteSpace(callId))
            {
                ClearPendingInteractiveRequest(callId);
            }
        }

        private void HandleTurnCompleted(ControlPlaneConversationStreamEvent streamEvent)
        {
            ClearPendingInteractiveRequestsForTurn(streamEvent.ThreadId?.Value, streamEvent.TurnId?.Value);
        }

        private void ClearPendingInteractiveRequest(string callId)
        {
            pendingApprovals.TryRemove(callId, out _);
            pendingPermissionRequests.TryRemove(callId, out _);
            pendingUserInputs.TryRemove(callId, out _);
        }

        private void ClearPendingInteractiveRequestsForTurn(string? threadId, string? turnId)
        {
            var normalizedThreadId = Normalize(threadId);
            if (string.IsNullOrWhiteSpace(normalizedThreadId))
            {
                return;
            }

            var callIds = pendingApprovals.Values
                .Select(static item => (CliPendingInteractiveRequestState)item)
                .Concat(pendingPermissionRequests.Values.Select(static item => (CliPendingInteractiveRequestState)item))
                .Concat(pendingUserInputs.Values.Select(static item => (CliPendingInteractiveRequestState)item))
                .Where(item => MatchesPendingInteractiveTurn(item, normalizedThreadId!, turnId))
                .Select(item => item.CallId)
                .Where(static callId => !string.IsNullOrWhiteSpace(callId))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            foreach (var callId in callIds)
            {
                ClearPendingInteractiveRequest(callId);
            }
        }

        private static bool MatchesPendingInteractiveTurn(CliPendingInteractiveRequestState pendingRequest, string threadId, string? turnId)
        {
            var eventThreadId = Normalize(pendingRequest.ThreadId);
            if (string.IsNullOrWhiteSpace(eventThreadId)
                || !string.Equals(eventThreadId, threadId, StringComparison.Ordinal))
            {
                return false;
            }

            var normalizedTurnId = Normalize(turnId);
            var eventTurnId = Normalize(pendingRequest.TurnId);
            if (string.IsNullOrWhiteSpace(normalizedTurnId))
            {
                return string.IsNullOrWhiteSpace(eventTurnId);
            }

            return string.IsNullOrWhiteSpace(eventTurnId)
                   || string.Equals(eventTurnId, normalizedTurnId, StringComparison.Ordinal);
        }

        private void HandleApprovalRequested(ControlPlaneConversationStreamEvent streamEvent)
        {
            var pendingApproval = CliInteractiveStateConverters.ToPendingApprovalRequestState(streamEvent);
            if (pendingApproval is not null)
            {
                pendingApprovals[pendingApproval.CallId] = pendingApproval;
            }

            if (!options.ApproveAll)
            {
                errors.Enqueue("恢复线程后存在待审批请求，但未启用 --approve-all。");
                return;
            }

            autoResponseTasks.Enqueue(AutoApproveAsync(pendingApproval));
        }

        private void HandlePermissionRequested(ControlPlaneConversationStreamEvent streamEvent)
        {
            var pendingPermission = CliInteractiveStateConverters.ToPendingPermissionRequestState(streamEvent);
            if (pendingPermission is not null)
            {
                pendingPermissionRequests[pendingPermission.CallId] = pendingPermission;
            }

            if (permissionScript is null)
            {
                errors.Enqueue("恢复线程后存在权限申请请求，但未提供 --permissions-json。");
                return;
            }

            autoResponseTasks.Enqueue(AutoProvidePermissionGrantAsync(pendingPermission));
        }

        private void HandleUserInputRequested(ControlPlaneConversationStreamEvent streamEvent)
        {
            var pendingUserInput = CliInteractiveStateConverters.ToPendingUserInputRequestState(streamEvent);
            if (pendingUserInput is not null)
            {
                pendingUserInputs[pendingUserInput.CallId] = pendingUserInput;
            }

            if (userInputScript is null)
            {
                errors.Enqueue("恢复线程后存在用户补录请求，但未提供 --user-input-json。");
                return;
            }

            autoResponseTasks.Enqueue(AutoProvideUserInputAsync(pendingUserInput));
        }

        private async Task AutoApproveAsync(CliPendingApprovalRequestState? pendingApproval)
        {
            if (pendingApproval is null || string.IsNullOrWhiteSpace(pendingApproval.CallId))
            {
                errors.Enqueue("收到待审批恢复事件但缺少 callId。");
                return;
            }

            try
            {
                var response = CliApprovalResponseResolver.BuildResolution(
                    pendingApproval.CallId,
                    pendingApproval,
                    options.ApprovalDecision,
                    "TianShu.Cli thread resume 自动审批",
                    out _);
                var responded = await governance.ResolveApprovalAsync(response, cancellationToken).ConfigureAwait(false);
                if (!responded)
                {
                    errors.Enqueue($"自动批准失败：{pendingApproval.CallId}");
                    return;
                }

                pendingApprovals.TryRemove(pendingApproval.CallId, out _);
                Touch();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Enqueue($"自动批准异常：{ex.Message}");
            }
        }

        private async Task AutoProvidePermissionGrantAsync(CliPendingPermissionRequestState? pendingPermission)
        {
            if (permissionScript is null)
            {
                errors.Enqueue("恢复线程后存在权限申请请求，但未提供 --permissions-json。");
                return;
            }

            if (pendingPermission is null || string.IsNullOrWhiteSpace(pendingPermission.CallId))
            {
                errors.Enqueue("收到待恢复权限申请事件但缺少 callId。");
                return;
            }

            if (!permissionScript.TryResolveResponse(pendingPermission.CallId, out var response))
            {
                errors.Enqueue($"权限申请请求未匹配到响应：{pendingPermission.CallId}");
                return;
            }

            try
            {
                var responded = await governance.ResolvePermissionRequestAsync(response, cancellationToken).ConfigureAwait(false);
                if (!responded)
                {
                    errors.Enqueue($"自动提交权限授权结果失败：{pendingPermission.CallId}");
                    return;
                }

                pendingPermissionRequests.TryRemove(pendingPermission.CallId, out _);
                Touch();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Enqueue($"自动提交权限授权结果异常：{ex.Message}");
            }
        }

        private async Task AutoProvideUserInputAsync(CliPendingUserInputRequestState? pendingUserInput)
        {
            if (userInputScript is null)
            {
                errors.Enqueue("恢复线程后存在用户补录请求，但未提供 --user-input-json。");
                return;
            }

            if (pendingUserInput is null || string.IsNullOrWhiteSpace(pendingUserInput.CallId))
            {
                errors.Enqueue("收到待恢复用户补录事件但缺少 callId。");
                return;
            }

            if (!userInputScript.TryResolveAnswers(pendingUserInput.CallId, out var answers))
            {
                errors.Enqueue($"用户补录请求未匹配到答案：{pendingUserInput.CallId}");
                return;
            }

            try
            {
                var responded = await governance.SubmitUserInputAsync(answers, cancellationToken).ConfigureAwait(false);
                if (!responded)
                {
                    errors.Enqueue($"自动提交用户补录答案失败：{pendingUserInput.CallId}");
                    return;
                }

                pendingUserInputs.TryRemove(pendingUserInput.CallId, out _);
                Touch();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Enqueue($"自动提交用户补录答案异常：{ex.Message}");
            }
        }

        private void Touch()
            => Interlocked.Exchange(ref lastObservedActivityUtcTicks, DateTime.UtcNow.Ticks);

        private TimeSpan GetConversationIdleDuration()
        {
            var lastActivityTicks = Interlocked.Read(ref lastObservedActivityUtcTicks);
            if (lastActivityTicks <= 0)
            {
                return TimeSpan.MaxValue;
            }

            var idleDuration = DateTime.UtcNow - new DateTime(lastActivityTicks, DateTimeKind.Utc);
            return idleDuration < TimeSpan.Zero ? TimeSpan.Zero : idleDuration;
        }
    }

    private static async Task AwaitBackgroundTasksAsync(ConcurrentQueue<Task> tasks)
    {
        if (tasks.IsEmpty)
        {
            return;
        }

        await Task.WhenAll(tasks.ToArray()).ConfigureAwait(false);
    }

    private static ControlPlaneConversationStreamEvent? BuildPendingInteractiveReplayEvent(ControlPlanePendingInteractiveRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CallId))
        {
            return null;
        }

        return request.RequestKind switch
        {
            "approval_requested" => new ControlPlaneConversationStreamEvent
            {
                Kind = ControlPlaneConversationStreamEventKind.ApprovalRequested,
                ThreadId = string.IsNullOrWhiteSpace(request.ThreadId) ? null : new ThreadId(request.ThreadId),
                TurnId = string.IsNullOrWhiteSpace(request.TurnId) ? null : new TurnId(request.TurnId),
                CallId = string.IsNullOrWhiteSpace(request.CallId) ? null : new CallId(request.CallId),
                ToolName = request.ToolName,
                ServerName = request.ServerName,
                Text = request.Text,
                Status = request.Status,
                Phase = request.Phase,
                RequiresApproval = request.RequiresApproval ?? true,
                ApprovalKind = request.ApprovalKind,
                AvailableDecisions = request.AvailableDecisions,
                AvailableDecisionOptions = request.AvailableDecisionOptions,
            },
            "permission_requested" => new ControlPlaneConversationStreamEvent
            {
                Kind = ControlPlaneConversationStreamEventKind.PermissionRequested,
                ThreadId = string.IsNullOrWhiteSpace(request.ThreadId) ? null : new ThreadId(request.ThreadId),
                TurnId = string.IsNullOrWhiteSpace(request.TurnId) ? null : new TurnId(request.TurnId),
                CallId = string.IsNullOrWhiteSpace(request.CallId) ? null : new CallId(request.CallId),
                ToolName = request.ToolName,
                Text = request.Text,
                Status = request.Status,
                Phase = request.Phase,
            },
            "request_user_input" => new ControlPlaneConversationStreamEvent
            {
                Kind = ControlPlaneConversationStreamEventKind.UserInputRequested,
                ThreadId = string.IsNullOrWhiteSpace(request.ThreadId) ? null : new ThreadId(request.ThreadId),
                TurnId = string.IsNullOrWhiteSpace(request.TurnId) ? null : new TurnId(request.TurnId),
                CallId = string.IsNullOrWhiteSpace(request.CallId) ? null : new CallId(request.CallId),
                ToolName = request.ToolName,
                Text = request.Text,
                Status = request.Status,
                Phase = request.Phase,
            },
            _ => null,
        };
    }

    private static IReadOnlyList<ControlPlanePendingInputStateEntry> ResolveRestorablePendingFollowUps(ControlPlanePendingInputState? pendingInputState)
    {
        if (pendingInputState is null)
        {
            return Array.Empty<ControlPlanePendingInputStateEntry>();
        }

        var merged = new List<ControlPlanePendingInputStateEntry>();
        if (pendingInputState.Entries is { Count: > 0 } entries)
        {
            merged.AddRange(entries);
        }

        if (pendingInputState.QueuedUserMessages is { Count: > 0 } queuedUserMessages)
        {
            merged.AddRange(queuedUserMessages);
        }

        if (pendingInputState.PendingSteers is { Count: > 0 } pendingSteers)
        {
            merged.AddRange(pendingSteers);
        }

        if (merged.Count == 0)
        {
            return Array.Empty<ControlPlanePendingInputStateEntry>();
        }

        var deduped = new List<ControlPlanePendingInputStateEntry>(merged.Count);
        var seenCorrelationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in merged)
        {
            var correlationId = Normalize(entry.CorrelationId);
            if (!string.IsNullOrWhiteSpace(correlationId) && !seenCorrelationIds.Add(correlationId!))
            {
                continue;
            }

            deduped.Add(entry);
        }

        return deduped;
    }

    private static ThreadResumePendingFollowUp? BuildRestoredPendingFollowUp(
        ControlPlanePendingInputStateEntry entry,
        bool submitPendingSteersAfterInterrupt)
    {
        IReadOnlyList<ControlPlaneInputItem> inputs = entry.Inputs is { Count: > 0 }
            ? entry.Inputs.ToArray()
            : Array.Empty<ControlPlaneInputItem>();
        if (inputs.Count == 0)
        {
            return null;
        }

        var requestedMode = Normalize(entry.EffectiveMode) ?? Normalize(entry.RequestedMode);
        var mode = requestedMode?.ToLowerInvariant() switch
        {
            "steer" => ControlPlaneFollowUpMode.Steer,
            "interrupt" => ControlPlaneFollowUpMode.Interrupt,
            "queue" => ControlPlaneFollowUpMode.Queue,
            _ => string.Equals(entry.PendingBucket, "PendingSteer", StringComparison.OrdinalIgnoreCase)
                ? ControlPlaneFollowUpMode.Steer
                : ControlPlaneFollowUpMode.Queue,
        };
        if (submitPendingSteersAfterInterrupt
            && mode == ControlPlaneFollowUpMode.Steer
            && string.Equals(entry.PendingBucket, "PendingSteer", StringComparison.OrdinalIgnoreCase))
        {
            mode = ControlPlaneFollowUpMode.Queue;
        }

        var previewText = CliConversationInputUtilities.BuildPreview(inputs)
            ?? "<empty>";
        var correlationId = Normalize(entry.CorrelationId) ?? Guid.NewGuid().ToString("N");
        return new ThreadResumePendingFollowUp(
            inputs,
            mode,
            correlationId,
            previewText,
            Normalize(entry.PendingBucket) ?? "QueuedUserMessage");
    }

    private static async Task<T?> WaitForNotificationOrNullAsync<T>(Task<T> notificationTask, TimeSpan timeout, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            return await notificationTask.WaitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static TPayload? ReadStreamPayload<TPayload>(
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

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static IReadOnlyList<FuzzyFileSearchFilePayload> ReadFuzzyFilePayloads(JsonElement array)
    {
        if (!TryGetArray(array, "files", out var files))
        {
            files = array;
        }

        if (files.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<FuzzyFileSearchFilePayload>();
        }

        var results = new List<FuzzyFileSearchFilePayload>();
        foreach (var item in files.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                results.Add(new FuzzyFileSearchFilePayload(item.GetString(), null));
                continue;
            }

            if (item.ValueKind == JsonValueKind.Object)
            {
                results.Add(new FuzzyFileSearchFilePayload(ReadString(item, "path"), ReadString(item, "fileName")));
            }
        }

        return results;
    }

    private int WriteThreadSummaryResult(ControlPlaneThreadSummary? thread, bool outputJson, string successText, string missingText)
    {
        if (outputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(ToCliThreadInfo(thread), jsonOptions));
            return thread is null ? 1 : 0;
        }

        if (thread is null)
        {
            Console.Error.WriteLine(missingText);
            return 1;
        }

        Console.WriteLine(successText);
        Console.WriteLine(JsonSerializer.Serialize(ToCliThreadInfo(thread), jsonOptions));
        return 0;
    }

    private static string ResolveThreadTitle(string? name, string? preview, string threadId)
    {
        var resolvedName = Normalize(name);
        if (!string.IsNullOrWhiteSpace(resolvedName))
        {
            return resolvedName!;
        }

        var resolvedPreview = Normalize(preview);
        return string.IsNullOrWhiteSpace(resolvedPreview) ? threadId : resolvedPreview!;
    }

    private static bool ConfirmThreadDelete(ThreadCommandOptions options)
        => ConfirmDestructiveOperation(
            options,
            expectedConfirmation: options.ThreadId!,
            prompt: $"将永久删除线程 {options.ThreadId} 及其会话日志。请输入完整 thread id 确认：");

    private static bool ConfirmThreadClear(ThreadCommandOptions options)
        => ConfirmDestructiveOperation(
            options,
            expectedConfirmation: "DELETE ALL THREADS",
            prompt: "将永久删除全部线程及全部会话日志，包括当前会话。请输入 DELETE ALL THREADS 确认：");

    private static bool ConfirmDestructiveOperation(ThreadCommandOptions options, string expectedConfirmation, string prompt)
    {
        var configuredConfirmation = Normalize(options.Confirmation);
        if (!string.IsNullOrWhiteSpace(configuredConfirmation))
        {
            return string.Equals(configuredConfirmation, expectedConfirmation, StringComparison.Ordinal);
        }

        Console.Error.Write(prompt);
        var entered = Console.In.ReadLine();
        return string.Equals(entered, expectedConfirmation, StringComparison.Ordinal);
    }

    private int WriteDestructiveOperationCancelled(bool outputJson, string message)
    {
        if (outputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { success = false, cancelled = true }, jsonOptions));
            return 1;
        }

        Console.Error.WriteLine(message);
        return 1;
    }

    private int WriteBooleanResult(bool success, bool outputJson, string successText, string failureText)
    {
        if (outputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { success }, jsonOptions));
            return success ? 0 : 1;
        }

        if (!success)
        {
            Console.Error.WriteLine(failureText);
            return 1;
        }

        Console.WriteLine(successText);
        return 0;
    }
}
