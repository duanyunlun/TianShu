using System.Collections.Concurrent;
using System.Text.Json;
using TianShu.Contracts.Agents;
using TianShu.Contracts.Configuration;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;
using TianShu.Execution.Runtime;
using TianShu.Kernel;
using TianShu.Kernel.Abstractions;
using TianShu.Configuration;

namespace TianShu.RuntimeComposition;

/// <summary>
/// 产品入口到新 Kernel→Runtime turn loop 的受控桥接请求。
/// Controlled bridge request from product entry points to the new Kernel-to-Runtime turn loop.
/// </summary>
public sealed record KernelRuntimeTurnLoopRequest(
    string Message,
    string? WorkingDirectory,
    string? ResumeThreadId,
    int TurnTimeoutSeconds,
    ResolvedTianShuConfig Config,
    bool EnableWorkspaceWrite = false,
    ApprovalId? WorkspaceWriteApprovalId = null,
    IReadOnlyList<string>? SteerInputs = null,
    bool EnableSubAgents = false,
    ApprovalId? SubAgentApprovalId = null,
    IReadOnlyDictionary<string, ISubAgentModule>? SubAgentModules = null,
    bool EnableShell = false,
    bool EnableMcp = false,
    ITianShuMcpResourceToolServices? McpResourceServices = null,
    IReadOnlyList<TianShuMcpToolDescriptor>? McpToolDescriptors = null,
    ITianShuMcpToolServices? McpToolServices = null,
    bool EnableMemory = false,
    ITianShuMemoryToolServices? MemoryToolServices = null,
    IReadOnlyDictionary<string, IMemoryModule>? MemoryModules = null);

/// <summary>
/// 新 Kernel→Runtime turn loop 的产品可消费终态投影。
/// Product-consumable terminal projection from the new Kernel-to-Runtime turn loop.
/// </summary>
public sealed record KernelRuntimeTurnLoopResult(
    ControlPlaneTurnSubmissionResult SendResult,
    KernelRuntimeExecutionResult ExecutionResult,
    KernelRuntimeReplaySummary ReplaySummary,
    KernelRuntimeDiagnosticsProjection DiagnosticsProjection,
    KernelRuntimeProductTerminalProjection TerminalProjection,
    string ThreadId,
    string TurnId,
    string TurnStatus,
    string ResultText);

/// <summary>
/// 新 Kernel→Runtime loop 的产品终态投影。
/// Product terminal projection for the new Kernel-to-Runtime loop.
/// </summary>
public sealed record KernelRuntimeProductTerminalProjection(
    string SessionId,
    string ThreadId,
    string TurnId,
    string TurnStatus,
    string? AssistantText,
    string ResultText,
    KernelRuntimeThreadProjection ThreadProjection,
    KernelRuntimeFeatureAvailabilityProjection TurnLog,
    KernelRuntimeFeatureAvailabilityProjection RolloutRecord,
    KernelRuntimeFeatureAvailabilityProjection ActiveRunCancellation,
    KernelRuntimeFeatureAvailabilityProjection CheckpointResume,
    KernelRuntimeSteerProjection Steer,
    IReadOnlyList<string> RuntimeTraceRefs,
    IReadOnlyList<string> DiagnosticsRefs,
    string ReplayCompleteness,
    IReadOnlyList<string> DowngradeReasons);

/// <summary>
/// 新 loop 的 thread/turn 稳定标识投影。
/// Stable thread/turn identifier projection for the new loop.
/// </summary>
public sealed record KernelRuntimeThreadProjection(
    string SessionId,
    string ThreadId,
    string TurnId,
    string Status,
    bool StableIdsAvailable,
    string? MissingReason);

/// <summary>
/// 尚未迁移或已迁移产品能力的可审计可用性投影。
/// Auditable availability projection for migrated or downgraded product capabilities.
/// </summary>
public sealed record KernelRuntimeFeatureAvailabilityProjection(
    bool Available,
    string? Reason,
    string? Reference);

/// <summary>
/// steer/follow-up 输入合并投影。
/// Projection for steer/follow-up input merge.
/// </summary>
public sealed record KernelRuntimeSteerProjection(
    bool Available,
    string Disposition,
    int AcceptedCount,
    IReadOnlyList<string> Inputs,
    string? Reason);

/// <summary>
/// 新 Kernel→Runtime loop 的 host control 操作结果。
/// Host-control operation result from the new Kernel-to-Runtime loop.
/// </summary>
public sealed record KernelRuntimeHostControlResult(
    string Operation,
    string Status,
    string? FailureCode,
    string? FailureMessage,
    KernelRuntimeExecutionResult? ExecutionResult,
    KernelRuntimeReplaySummary? ReplaySummary,
    KernelRuntimeDiagnosticsProjection? DiagnosticsProjection,
    KernelRuntimeProductTerminalProjection TerminalProjection);

public sealed record KernelRuntimeActiveRunCancellationProjection(
    bool Available,
    string? Reason,
    string? Reference,
    bool CancelRequested,
    string? TargetThreadId,
    string? TargetTurnId,
    string? TargetRunId);

public sealed record KernelRuntimeInterruptRequest(
    string ThreadId,
    string? TurnId,
    string Reason,
    string? WorkingDirectory,
    ResolvedTianShuConfig Config);

public sealed record KernelRuntimeResumeRequest(
    string ThreadId,
    string? TurnId,
    string ResumeToken,
    string CheckpointRef,
    string? WorkingDirectory,
    ResolvedTianShuConfig Config);

public sealed record KernelRuntimeSteerRequest(
    string ThreadId,
    string? TurnId,
    string Message,
    IReadOnlyList<string> SteerInputs,
    string? WorkingDirectory,
    int TurnTimeoutSeconds,
    ResolvedTianShuConfig Config,
    bool EnableWorkspaceWrite = false,
    ApprovalId? WorkspaceWriteApprovalId = null,
    bool EnableShell = false,
    bool EnableMcp = false,
    bool EnableMemory = false);

/// <summary>
/// 新 Kernel→Runtime turn loop 的诊断与指标产品投影。
/// Product projection for diagnostics and metrics emitted by the new Kernel-to-Runtime turn loop.
/// </summary>
public sealed record KernelRuntimeDiagnosticsProjection(
    string? RuntimeTraceRef,
    string? DiagnosticsRef,
    IReadOnlyList<string> RuntimeTraceRefs,
    IReadOnlyList<string> DiagnosticsRefs,
    int MetricsEventCount,
    IReadOnlyList<string> MetricsEventIds,
    int ModelMetricsEventCount,
    int ToolMetricsEventCount,
    KernelRuntimeProviderToolSurfaceProjection ProviderToolSurface,
    KernelRuntimeTokenUsageProjection TokenUsage,
    KernelRuntimeCostProjection Cost,
    IReadOnlyList<string> MissingReasons,
    IReadOnlyList<string> FailureCodes);

/// <summary>
/// Provider 请求工具面审计投影，仅记录工具名集合，不记录请求正文或密钥。
/// Provider request tool-surface audit projection that records tool names only, never request bodies or secrets.
/// </summary>
public sealed record KernelRuntimeProviderToolSurfaceProjection(
    bool Available,
    string? MissingReason,
    IReadOnlyList<string> WireApis,
    int Count,
    IReadOnlyList<string> Names,
    bool HasSpawnAgent);

/// <summary>
/// Kernel runtime loop 的 token usage 汇总投影。
/// Aggregated token-usage projection for the Kernel runtime loop.
/// </summary>
public sealed record KernelRuntimeTokenUsageProjection(
    bool Available,
    string? MissingReason,
    bool Estimated,
    long? InputTokens,
    long? CachedInputTokens,
    long? OutputTokens,
    long? ReasoningOutputTokens,
    long? TotalTokens,
    IReadOnlyList<string> Sources);

/// <summary>
/// Kernel runtime loop 的 cost 汇总投影。
/// Aggregated cost projection for the Kernel runtime loop.
/// </summary>
public sealed record KernelRuntimeCostProjection(
    bool Available,
    string? MissingReason,
    decimal? EstimatedCost,
    string? Currency,
    string? PriceModelVersion);

/// <summary>
/// 显式 opt-in 的 turn loop 桥接器；不声明 provider transport parity。
/// Explicit opt-in turn loop bridge; it does not claim provider transport parity.
/// </summary>
public static class KernelRuntimeTurnLoopBridge
{
    public static async Task<KernelRuntimeTurnLoopResult> RunAsync(
        KernelRuntimeTurnLoopRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var runSuffix = Guid.NewGuid().ToString("N")[..12];
        var intentId = new CoreIntentId($"intent-cli-turn-{runSuffix}");
        var runId = new KernelRunId($"run-cli-turn-{runSuffix}");
        var executionId = new ExecutionId($"execution-cli-turn-{runSuffix}");
        var sessionId = $"session-cli-kernel-{runSuffix}";
        var threadId = string.IsNullOrWhiteSpace(request.ResumeThreadId)
            ? $"thread-cli-kernel-{runSuffix}"
            : request.ResumeThreadId!;
        var turnId = $"turn-cli-kernel-{runSuffix}";
        var tianShuHomePath = ResolveTianShuHomePath(request.Config);
        var steerInputs = MergeSteerInputs(
            KernelRuntimeHostControlFileStore.TryConsumePendingSteers(request.WorkingDirectory, threadId, tianShuHomePath),
            request.SteerInputs);
        var governance = CreateGovernance(
            runSuffix,
            request.EnableWorkspaceWrite,
            request.WorkspaceWriteApprovalId,
            request.EnableSubAgents,
            request.SubAgentApprovalId,
            request.EnableShell,
            request.EnableMcp,
            request.EnableMemory);
        using var activeRun = KernelRuntimeActiveRunRegistry.Register(
            threadId,
            turnId,
            runId.Value,
            request.Message,
            request.WorkingDirectory,
            tianShuHomePath,
            cancellationToken);
        var metricsSink = new RecordingExecutionRuntimeMetricsSink();
        await using var runtime = KernelRuntimeTurnLoopComposition.CreateRuntime(
            request.Config,
            includeWorkspaceWrite: request.EnableWorkspaceWrite,
            includeShell: request.EnableShell,
            includeMcp: request.EnableMcp,
            mcpResourceServices: request.McpResourceServices,
            mcpToolDescriptors: request.McpToolDescriptors,
            mcpToolServices: request.McpToolServices,
            includeMemory: request.EnableMemory,
            memoryToolServices: request.MemoryToolServices,
            metricsSink: metricsSink,
            subAgentModules: request.EnableSubAgents ? request.SubAgentModules : null,
            memoryModules: request.EnableMemory ? request.MemoryModules : null);
        var intent = new TurnIntent(
                intentId,
            new KernelSubjectRef(new SessionId(sessionId), new ThreadId(threadId), turnId: new TurnId(turnId)),
            governance,
            $"cli://send/{runSuffix}",
            new KernelBudget(
                tokenBudget: 4_096,
                timeBudgetMs: Math.Max(1, request.TurnTimeoutSeconds) * 1_000L,
                retryBudget: 5,
                toolCallBudget: 1),
            new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["source"] = StructuredValue.FromString("tianshu.cli.send"),
                ["message"] = StructuredValue.FromString(request.Message),
                ["executionPath"] = StructuredValue.FromString("kernel-runtime-loop"),
                ["model"] = StructuredValue.FromString(request.Config.Model ?? "default"),
                ["modelProvider"] = StructuredValue.FromString(request.Config.ModelProvider ?? "default"),
                ["providerWireApi"] = StructuredValue.FromString(request.Config.ProviderWireApi ?? request.Config.ProtocolAdapter),
                ["steerInputs"] = StructuredValue.FromPlainObject(steerInputs),
            }));

        var loop = new AdaptiveRuntimeExecutionLoop(new StableKernelCore(), runtime);
        KernelRuntimeExecutionResult result;
        try
        {
            result = await loop.RunReactiveAsync(
                    intent,
                    new KernelRuntimeExecutionOptions(
                        new KernelRunOptions(runId, enableAdaptive: false, requireHumanGate: false),
                        new ExecutionRuntimeContext(
                            executionId,
                            runId,
                            governance,
                            request.WorkingDirectory,
                            new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                            {
                                ["source"] = StructuredValue.FromString("tianshu.cli.send"),
                                ["message"] = StructuredValue.FromString(request.Message),
                            })),
                        ExecuteRuntimePlan: true),
                    activeRun.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (activeRun.CancelRequested && !cancellationToken.IsCancellationRequested)
        {
            var kernelResult = await new StableKernelCore()
                .RunAsync(
                    intent,
                    new KernelRunOptions(runId, enableAdaptive: false, requireHumanGate: false),
                    CancellationToken.None)
                .ConfigureAwait(false);
            result = new KernelRuntimeExecutionResult(
                kernelResult,
                RuntimeResult: null,
                KernelRuntimeExecutionDisposition.RuntimeFailed,
                kernelResult.TraceId,
                RuntimeTraceRef: null,
                DiagnosticsRef: null);
        }

        var metricsEvents = metricsSink.Snapshot();
        var replay = KernelRuntimeReplayProjector.Build(result, metricsEvents);
        var diagnosticsProjection = KernelRuntimeDiagnosticsProjector.Build(result, replay, metricsEvents);
        var completed = result.Disposition == KernelRuntimeExecutionDisposition.RuntimeCompleted;
        var interrupted = activeRun.CancelRequested && result.Disposition != KernelRuntimeExecutionDisposition.RuntimeCompleted;
        var turnStatus = completed ? "completed" : interrupted ? "interrupted" : "failed";
        var assistantText = ResolveAssistantText(result);
        var resultText = completed
            ? assistantText ?? $"Kernel runtime loop completed. Stage path: {string.Join(" -> ", replay.StagePath)}."
            : interrupted
                ? $"Kernel runtime loop interrupted. Active run cancellation: {activeRun.CancellationReference}."
            : $"Kernel runtime loop failed. Disposition: {result.Disposition}.";
        var evidence = await KernelRuntimeProductEvidenceWriter.TryPersistAsync(
                request.WorkingDirectory,
                sessionId,
                threadId,
                turnId,
                turnStatus,
                assistantText,
                resultText,
                replay,
                diagnosticsProjection,
                controlOperation: null,
                checkpointRef: null,
                steerInputs,
                tianShuHomePath,
                cancellationToken)
            .ConfigureAwait(false);
        KernelRuntimeHostControlFileStore.TryPersistCheckpoint(
            request.WorkingDirectory,
            sessionId,
            threadId,
            turnId,
            turnStatus,
            replay,
            evidence,
            steerInputs,
            tianShuHomePath);
        var terminalProjection = BuildTerminalProjection(
            sessionId,
            threadId,
            turnId,
            turnStatus,
            assistantText,
            resultText,
            replay,
            diagnosticsProjection,
            steerInputs,
            controlOperation: interrupted ? "interrupt" : null,
            checkpointRef: null,
            evidence,
            activeRun.CreateCancellationProjection());

        return new KernelRuntimeTurnLoopResult(
            new ControlPlaneTurnSubmissionResult
            {
                Accepted = completed,
                Message = resultText,
                TurnId = new TurnId(turnId),
                TurnStatus = turnStatus,
            },
            result,
            replay,
            diagnosticsProjection,
            terminalProjection,
            threadId,
            turnId,
            turnStatus,
            resultText);
    }

    public static Task<KernelRuntimeHostControlResult> RunInterruptAsync(
        KernelRuntimeInterruptRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var runSuffix = Guid.NewGuid().ToString("N")[..12];
        var sessionId = $"session-cli-kernel-{runSuffix}";
        var turnId = string.IsNullOrWhiteSpace(request.TurnId) ? $"turn-cli-kernel-{runSuffix}" : request.TurnId!;
        var intent = new InterruptIntent(
            new CoreIntentId($"intent-cli-interrupt-{runSuffix}"),
            new KernelSubjectRef(new SessionId(sessionId), new ThreadId(request.ThreadId), turnId: new TurnId(turnId)),
            CreateHostControlGovernance(runSuffix, SideEffectLevel.HostMutation),
            string.IsNullOrWhiteSpace(request.Reason) ? "user.cancel" : request.Reason,
            new KernelBudget(timeBudgetMs: 1_000),
            new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["source"] = StructuredValue.FromString("tianshu.cli.interrupt"),
                ["executionPath"] = StructuredValue.FromString("kernel-runtime-loop"),
            }));

        var tianShuHomePath = ResolveTianShuHomePath(request.Config);
        return RunHostControlAsync(
            "interrupt",
            "interrupted",
            sessionId,
            request.ThreadId,
            turnId,
            request.WorkingDirectory,
            request.Config,
            intent,
            checkpointRef: null,
            activeRunCancellation: KernelRuntimeActiveRunRegistry.Cancel(
                request.ThreadId,
                request.TurnId,
                string.IsNullOrWhiteSpace(request.Reason) ? "user.cancel" : request.Reason,
                request.WorkingDirectory,
                tianShuHomePath),
            steerInputs: Array.Empty<string>(),
            cancellationToken);
    }

    public static async Task<KernelRuntimeHostControlResult> RunResumeAsync(
        KernelRuntimeResumeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var runSuffix = Guid.NewGuid().ToString("N")[..12];
        var sessionId = $"session-cli-kernel-{runSuffix}";
        var turnId = string.IsNullOrWhiteSpace(request.TurnId) ? $"turn-cli-kernel-{runSuffix}" : request.TurnId!;
        if (string.IsNullOrWhiteSpace(request.CheckpointRef))
        {
            var terminalProjection = BuildUnavailableControlProjection(
                sessionId,
                request.ThreadId,
                turnId,
                "failed",
                "resume",
                "checkpoint_missing");
            return new KernelRuntimeHostControlResult(
                "resume",
                "failed",
                "kernel_runtime_resume_checkpoint_missing",
                "Resume 需要 checkpointRef，缺失时必须 fail-closed。",
                null,
                null,
                null,
                terminalProjection);
        }

        var tianShuHomePath = ResolveTianShuHomePath(request.Config);
        var checkpoint = KernelRuntimeHostControlFileStore.TryResolveCheckpoint(request.WorkingDirectory, request.CheckpointRef, tianShuHomePath);
        if (checkpoint is null)
        {
            var terminalProjection = BuildUnavailableControlProjection(
                sessionId,
                request.ThreadId,
                turnId,
                "failed",
                "resume",
                "checkpoint_not_found");
            return new KernelRuntimeHostControlResult(
                "resume",
                "failed",
                "kernel_runtime_resume_checkpoint_not_found",
                "Resume checkpointRef 未在 HostControl checkpoint store 中解析到，必须 fail-closed。",
                null,
                null,
                null,
                terminalProjection);
        }

        if (!string.Equals(checkpoint.ThreadId, request.ThreadId, StringComparison.Ordinal))
        {
            var terminalProjection = BuildUnavailableControlProjection(
                sessionId,
                request.ThreadId,
                turnId,
                "failed",
                "resume",
                "checkpoint_thread_mismatch");
            return new KernelRuntimeHostControlResult(
                "resume",
                "failed",
                "kernel_runtime_resume_checkpoint_thread_mismatch",
                "Resume checkpointRef 所属 thread 与请求 thread 不一致，必须 fail-closed。",
                null,
                null,
                null,
                terminalProjection);
        }

        var intent = new ResumeIntent(
            new CoreIntentId($"intent-cli-resume-{runSuffix}"),
            new KernelSubjectRef(new SessionId(sessionId), new ThreadId(request.ThreadId), turnId: new TurnId(turnId)),
            CreateHostControlGovernance(runSuffix, SideEffectLevel.ReadOnly),
            string.IsNullOrWhiteSpace(request.ResumeToken) ? $"resume-token-{runSuffix}" : request.ResumeToken,
            checkpoint.CheckpointRef,
            new KernelBudget(timeBudgetMs: 1_000),
            new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["source"] = StructuredValue.FromString("tianshu.cli.resume"),
                ["executionPath"] = StructuredValue.FromString("kernel-runtime-loop"),
            }));

        return await RunHostControlAsync(
                "resume",
                "completed",
                sessionId,
                request.ThreadId,
                turnId,
                request.WorkingDirectory,
                request.Config,
                intent,
                checkpoint.CheckpointRef,
                activeRunCancellation: null,
                steerInputs: KernelRuntimeHostControlFileStore.TryConsumePendingSteers(request.WorkingDirectory, request.ThreadId, tianShuHomePath),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<KernelRuntimeTurnLoopResult> RunSteerAsync(
        KernelRuntimeSteerRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tianShuHomePath = ResolveTianShuHomePath(request.Config);
        KernelRuntimeHostControlFileStore.TryEnqueuePendingSteers(
            request.WorkingDirectory,
            request.ThreadId,
            request.TurnId,
            NormalizeInputs(request.SteerInputs),
            tianShuHomePath);
        return await RunAsync(
                new KernelRuntimeTurnLoopRequest(
                    request.Message,
                    request.WorkingDirectory,
                    request.ThreadId,
                    request.TurnTimeoutSeconds,
                    request.Config,
                    EnableWorkspaceWrite: request.EnableWorkspaceWrite,
                    WorkspaceWriteApprovalId: request.WorkspaceWriteApprovalId,
                    SteerInputs: request.SteerInputs,
                    EnableShell: request.EnableShell,
                    EnableMcp: request.EnableMcp,
                    EnableMemory: request.EnableMemory),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<KernelRuntimeHostControlResult> RunHostControlAsync(
        string operation,
        string terminalStatus,
        string sessionId,
        string threadId,
        string turnId,
        string? workingDirectory,
        ResolvedTianShuConfig config,
        CoreIntent intent,
        string? checkpointRef,
        KernelRuntimeActiveRunCancellationProjection? activeRunCancellation,
        IReadOnlyList<string> steerInputs,
        CancellationToken cancellationToken)
    {
        var runSuffix = Guid.NewGuid().ToString("N")[..12];
        var runId = new KernelRunId($"run-cli-{operation}-{runSuffix}");
        var executionId = new ExecutionId($"execution-cli-{operation}-{runSuffix}");
        var governance = CreateHostControlGovernance(
            runSuffix,
            string.Equals(operation, "interrupt", StringComparison.Ordinal) ? SideEffectLevel.HostMutation : SideEffectLevel.ReadOnly);
        var metricsSink = new RecordingExecutionRuntimeMetricsSink();
        await using var runtime = KernelRuntimeTurnLoopComposition.CreateRuntime(config, metricsSink: metricsSink);
        var loop = new AdaptiveRuntimeExecutionLoop(new StableKernelCore(), runtime);
        var result = await loop.RunAsync(
                intent,
                new KernelRuntimeExecutionOptions(
                    new KernelRunOptions(runId, enableAdaptive: false, requireHumanGate: false),
                    new ExecutionRuntimeContext(
                        executionId,
                        runId,
                        governance,
                        workingDirectory,
                        new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                        {
                            ["source"] = StructuredValue.FromString($"tianshu.cli.{operation}"),
                        })),
                    ExecuteRuntimePlan: true),
                cancellationToken)
            .ConfigureAwait(false);
        var metricsEvents = metricsSink.Snapshot();
        var replay = KernelRuntimeReplayProjector.Build(result, metricsEvents);
        var diagnosticsProjection = KernelRuntimeDiagnosticsProjector.Build(result, replay, metricsEvents);
        var status = result.Disposition == KernelRuntimeExecutionDisposition.RuntimeCompleted ? terminalStatus : "failed";
        var tianShuHomePath = ResolveTianShuHomePath(config);
        var evidence = await KernelRuntimeProductEvidenceWriter.TryPersistAsync(
                workingDirectory,
                sessionId,
                threadId,
                turnId,
                status,
                assistantText: null,
                resultText: $"Kernel runtime {operation} projected. Status: {status}.",
                replay,
                diagnosticsProjection,
            operation,
            checkpointRef,
            steerInputs,
            tianShuHomePath,
            cancellationToken)
            .ConfigureAwait(false);
        var terminalProjection = BuildTerminalProjection(
            sessionId,
            threadId,
            turnId,
            status,
            assistantText: null,
            resultText: $"Kernel runtime {operation} projected. Status: {status}.",
            replay,
            diagnosticsProjection,
            steerInputs,
            operation,
            checkpointRef,
            evidence,
            activeRunCancellation);

        return new KernelRuntimeHostControlResult(
            operation,
            status,
            result.Disposition == KernelRuntimeExecutionDisposition.RuntimeCompleted ? null : "kernel_runtime_host_control_failed",
            result.Disposition == KernelRuntimeExecutionDisposition.RuntimeCompleted ? null : $"Host control {operation} failed: {result.Disposition}.",
            result,
            replay,
            diagnosticsProjection,
            terminalProjection);
    }

    private static GovernanceEnvelope CreateGovernance(
        string runSuffix,
        bool enableWorkspaceWrite,
        ApprovalId? workspaceWriteApprovalId,
        bool enableSubAgents,
        ApprovalId? subAgentApprovalId,
        bool enableShell,
        bool enableMcp,
        bool enableMemory)
    {
        var allowedToolIds = new List<string>
        {
            "kernel.request_capability_call",
            "update_context_policy",
            "request_capability_call",
            "module.core_loop",
            "read_file",
            "list_dir",
            "grep",
            "glob",
        };
        if (enableWorkspaceWrite)
        {
            allowedToolIds.Add("write");
            allowedToolIds.Add("apply_patch");
        }

        if (enableShell)
        {
            allowedToolIds.Add("shell_command");
        }

        if (enableMcp)
        {
            allowedToolIds.Add("list_mcp_resources");
            allowedToolIds.Add("list_mcp_resource_templates");
            allowedToolIds.Add("read_mcp_resource");
        }

        if (enableMemory)
        {
            allowedToolIds.Add("memory_search");
            allowedToolIds.Add("memory_explain_overlay");
            allowedToolIds.Add("memory_feedback");
        }

        if (enableSubAgents)
        {
            allowedToolIds.Add("spawn_agent");
        }

        var approvalIds = new List<ApprovalId>();
        if (enableWorkspaceWrite && workspaceWriteApprovalId is { } workspaceApprovalId)
        {
            approvalIds.Add(workspaceApprovalId);
        }

        if (enableSubAgents && subAgentApprovalId is { } subAgentApproval)
        {
            approvalIds.Add(subAgentApproval);
        }

        var allowedModuleIds = new List<string>
        {
            "kernel.default",
            "provider.default",
            "module.context",
            "module.memory",
            "module.artifact",
            "module.diagnostics",
        };
        if (enableSubAgents)
        {
            allowedModuleIds.Add("module.sub_agent");
        }

        if (enableMemory)
        {
            allowedModuleIds.Add("memory.identity");
        }

        return new GovernanceEnvelope(
            $"governance-cli-kernel-{runSuffix}",
            allowedToolIds: allowedToolIds,
            allowedModuleIds: allowedModuleIds,
            maxSideEffectLevel: enableSubAgents || enableShell ? SideEffectLevel.HostMutation : SideEffectLevel.ExternalNetwork,
            requiresHumanGate: enableWorkspaceWrite || enableSubAgents || enableShell,
            approvalIds: approvalIds);
    }

    private static string? ResolveTianShuHomePath(ResolvedTianShuConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var configPath = string.IsNullOrWhiteSpace(config.UserConfigPath)
            ? config.ConfigFilePath
            : config.UserConfigPath;
        return string.IsNullOrWhiteSpace(configPath)
            ? null
            : Path.GetDirectoryName(Path.GetFullPath(configPath));
    }

    private static GovernanceEnvelope CreateHostControlGovernance(string runSuffix, SideEffectLevel maxSideEffectLevel)
        => new(
            $"governance-cli-kernel-control-{runSuffix}",
            allowedModuleIds: ["kernel.default"],
            maxSideEffectLevel: maxSideEffectLevel,
            requiresHumanGate: false);

    private static KernelRuntimeProductTerminalProjection BuildTerminalProjection(
        string sessionId,
        string threadId,
        string turnId,
        string turnStatus,
        string? assistantText,
        string resultText,
        KernelRuntimeReplaySummary replay,
        KernelRuntimeDiagnosticsProjection diagnosticsProjection,
        IReadOnlyList<string> steerInputs,
        string? controlOperation,
        string? checkpointRef,
        KernelRuntimeProductEvidenceReferences evidence,
        KernelRuntimeActiveRunCancellationProjection? activeRunCancellationOverride = null)
    {
        var downgradeReasons = new List<string>(evidence.DowngradeReasons);
        var activeRunCancellation = ResolveActiveRunCancellation(controlOperation, activeRunCancellationOverride);
        if (string.Equals(controlOperation, "interrupt", StringComparison.Ordinal)
            && !activeRunCancellation.Available
            && !string.IsNullOrWhiteSpace(activeRunCancellation.Reason))
        {
            downgradeReasons.Add(activeRunCancellation.Reason);
        }

        var checkpointResume = string.Equals(controlOperation, "resume", StringComparison.Ordinal)
            ? new KernelRuntimeFeatureAvailabilityProjection(true, null, checkpointRef)
            : new KernelRuntimeFeatureAvailabilityProjection(false, "not_applicable", null);
        var steer = steerInputs.Count > 0
            ? new KernelRuntimeSteerProjection(true, "applied_to_model_input", steerInputs.Count, steerInputs, null)
            : new KernelRuntimeSteerProjection(false, "not_requested", 0, Array.Empty<string>(), "no_steer_input");

        return new KernelRuntimeProductTerminalProjection(
            sessionId,
            threadId,
            turnId,
            turnStatus,
            assistantText,
            resultText,
            new KernelRuntimeThreadProjection(sessionId, threadId, turnId, turnStatus, StableIdsAvailable: true, MissingReason: null),
            evidence.TurnLog,
            evidence.RolloutRecord,
            new KernelRuntimeFeatureAvailabilityProjection(
                activeRunCancellation.Available,
                activeRunCancellation.Reason,
                activeRunCancellation.Reference),
            checkpointResume,
            steer,
            diagnosticsProjection.RuntimeTraceRefs,
            diagnosticsProjection.DiagnosticsRefs,
            replay.Completeness,
            downgradeReasons);
    }

    private static KernelRuntimeActiveRunCancellationProjection ResolveActiveRunCancellation(
        string? controlOperation,
        KernelRuntimeActiveRunCancellationProjection? activeRunCancellation)
    {
        if (!string.Equals(controlOperation, "interrupt", StringComparison.Ordinal))
        {
            return new KernelRuntimeActiveRunCancellationProjection(
                Available: false,
                Reason: "not_applicable",
                Reference: null,
                CancelRequested: false,
                TargetThreadId: null,
                TargetTurnId: null,
                TargetRunId: null);
        }

        return activeRunCancellation
               ?? new KernelRuntimeActiveRunCancellationProjection(
                   Available: false,
                   Reason: "active_run_not_found",
                   Reference: null,
                   CancelRequested: false,
                   TargetThreadId: null,
                   TargetTurnId: null,
                   TargetRunId: null);
    }

    private static KernelRuntimeProductTerminalProjection BuildUnavailableControlProjection(
        string sessionId,
        string threadId,
        string turnId,
        string turnStatus,
        string operation,
        string reason)
        => new(
            sessionId,
            threadId,
            turnId,
            turnStatus,
            AssistantText: null,
            ResultText: $"Kernel runtime {operation} unavailable: {reason}.",
            new KernelRuntimeThreadProjection(sessionId, threadId, turnId, turnStatus, StableIdsAvailable: true, MissingReason: null),
            new KernelRuntimeFeatureAvailabilityProjection(false, "turn_log_not_migrated_23_6", null),
            new KernelRuntimeFeatureAvailabilityProjection(false, "not_migrated_23_6", null),
            new KernelRuntimeFeatureAvailabilityProjection(false, "not_applicable", null),
            new KernelRuntimeFeatureAvailabilityProjection(false, reason, null),
            new KernelRuntimeSteerProjection(false, "not_requested", 0, Array.Empty<string>(), "no_steer_input"),
            Array.Empty<string>(),
            Array.Empty<string>(),
            "kernel-only",
            [reason, "turn_log_not_migrated_23_6", "rollout_record_not_migrated_23_6"]);

    private static IReadOnlyList<string> NormalizeInputs(IReadOnlyList<string>? inputs)
        => inputs?
            .Where(static input => !string.IsNullOrWhiteSpace(input))
            .Select(static input => input.Trim())
            .ToArray()
            ?? Array.Empty<string>();

    private static IReadOnlyList<string> MergeSteerInputs(IReadOnlyList<string>? pendingInputs, IReadOnlyList<string>? requestInputs)
        => (pendingInputs ?? Array.Empty<string>())
            .Concat(NormalizeInputs(requestInputs))
            .Where(static input => !string.IsNullOrWhiteSpace(input))
            .Select(static input => input.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string? ResolveAssistantText(KernelRuntimeExecutionResult result)
        => result.RuntimeResult?.StepResults
            .Select(static step => step.Output)
            .Where(static output => output is not null && output.TryGetProperty("assistantText", out _))
            .Select(static output => output!.GetProperty("assistantText").GetString())
            .LastOrDefault(static text => !string.IsNullOrWhiteSpace(text));

    private sealed class RecordingExecutionRuntimeMetricsSink : IExecutionRuntimeMetricsSink
    {
        private readonly object gate = new();
        private readonly List<RuntimeMetricsEvent> events = [];

        public ValueTask RecordAsync(RuntimeMetricsEvent metricsEvent, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            ArgumentNullException.ThrowIfNull(metricsEvent);
            lock (gate)
            {
                events.Add(metricsEvent);
            }

            return ValueTask.CompletedTask;
        }

        public IReadOnlyList<RuntimeMetricsEvent> Snapshot()
        {
            lock (gate)
            {
                return events.ToArray();
            }
        }
    }
}

internal sealed record KernelRuntimeProductEvidenceReferences(
    KernelRuntimeFeatureAvailabilityProjection TurnLog,
    KernelRuntimeFeatureAvailabilityProjection RolloutRecord,
    IReadOnlyList<string> DowngradeReasons);

internal sealed class KernelRuntimeActiveRunRegistration : IDisposable
{
    private readonly CancellationTokenSource cancellation;
    private readonly CancellationTokenSource monitorCancellation = new();
    private readonly CancellationTokenRegistration parentRegistration;
    private readonly KernelRuntimePersistentActiveRunRecord? persistentRecord;
    private readonly Task? monitorTask;
    private int cancelRequested;
    private bool disposed;

    public KernelRuntimeActiveRunRegistration(
        string threadId,
        string turnId,
        string runId,
        string message,
        string? workingDirectory,
        string? tianShuHomePath,
        CancellationToken parentCancellationToken)
    {
        ThreadId = threadId;
        TurnId = turnId;
        RunId = runId;
        Message = message;
        StartedAtUtc = DateTimeOffset.UtcNow;
        cancellation = new CancellationTokenSource();
        persistentRecord = KernelRuntimeHostControlFileStore.TryRegister(workingDirectory, threadId, turnId, runId, message, tianShuHomePath);
        if (persistentRecord is not null)
        {
            monitorTask = MonitorPersistentCancellationAsync(persistentRecord.CancelSignalPath, monitorCancellation.Token);
        }

        parentRegistration = parentCancellationToken.Register(static state =>
        {
            var registration = (KernelRuntimeActiveRunRegistration)state!;
            registration.Cancel("parent_cancellation");
        }, this);
    }

    public string ThreadId { get; }

    public string TurnId { get; }

    public string RunId { get; }

    public string Message { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public string? CancellationReason { get; private set; }

    public DateTimeOffset? CancelledAtUtc { get; private set; }

    public string CancellationReference => $"active-run://{ThreadId}/{TurnId}/{RunId}";

    public CancellationToken Token => cancellation.Token;

    public bool CancelRequested => Volatile.Read(ref cancelRequested) == 1;

    public KernelRuntimeActiveRunCancellationProjection Cancel(string reason)
    {
        if (Interlocked.Exchange(ref cancelRequested, 1) == 0)
        {
            CancellationReason = string.IsNullOrWhiteSpace(reason) ? "user.cancel" : reason;
            CancelledAtUtc = DateTimeOffset.UtcNow;
            cancellation.Cancel();
        }

        return CreateCancellationProjection();
    }

    public KernelRuntimeActiveRunCancellationProjection CreateCancellationProjection()
        => CancelRequested
            ? new KernelRuntimeActiveRunCancellationProjection(
                Available: true,
                Reason: null,
                Reference: CancellationReference,
                CancelRequested: true,
                TargetThreadId: ThreadId,
                TargetTurnId: TurnId,
                TargetRunId: RunId)
            : new KernelRuntimeActiveRunCancellationProjection(
                Available: false,
                Reason: "not_requested",
                Reference: null,
                CancelRequested: false,
                TargetThreadId: ThreadId,
                TargetTurnId: TurnId,
                TargetRunId: RunId);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        KernelRuntimeActiveRunRegistry.Unregister(this);
        monitorCancellation.Cancel();
        try
        {
            monitorTask?.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch (AggregateException)
        {
        }

        parentRegistration.Dispose();
        monitorCancellation.Dispose();
        cancellation.Dispose();
        KernelRuntimeHostControlFileStore.TryUnregister(persistentRecord);
    }

    private async Task MonitorPersistentCancellationAsync(string cancelSignalPath, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (File.Exists(cancelSignalPath))
                {
                    Cancel(KernelRuntimeHostControlFileStore.TryReadCancellationReason(cancelSignalPath) ?? "user.cancel");
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }
    }
}

internal static class KernelRuntimeActiveRunRegistry
{
    private static readonly ConcurrentDictionary<string, KernelRuntimeActiveRunRegistration> ActiveRunsByThread = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, KernelRuntimeActiveRunRegistration> ActiveRunsByTurn = new(StringComparer.Ordinal);

    public static KernelRuntimeActiveRunRegistration Register(
        string threadId,
        string turnId,
        string runId,
        string message,
        string? workingDirectory,
        string? tianShuHomePath,
        CancellationToken cancellationToken)
    {
        var registration = new KernelRuntimeActiveRunRegistration(threadId, turnId, runId, message, workingDirectory, tianShuHomePath, cancellationToken);
        ActiveRunsByThread[NormalizeKey(threadId)] = registration;
        ActiveRunsByTurn[NormalizeKey(turnId)] = registration;
        return registration;
    }

    public static KernelRuntimeActiveRunCancellationProjection Cancel(
        string threadId,
        string? turnId,
        string reason,
        string? workingDirectory,
        string? tianShuHomePath = null)
    {
        var registration = Resolve(threadId, turnId);
        if (registration is null)
        {
            if (KernelRuntimeHostControlFileStore.TryCancel(workingDirectory, threadId, turnId, reason, tianShuHomePath) is { } persistentProjection)
            {
                return persistentProjection;
            }

            return new KernelRuntimeActiveRunCancellationProjection(
                Available: false,
                Reason: "active_run_not_found",
                Reference: null,
                CancelRequested: false,
                TargetThreadId: threadId,
                TargetTurnId: turnId,
                TargetRunId: null);
        }

        return registration.Cancel(reason);
    }

    public static void Unregister(KernelRuntimeActiveRunRegistration registration)
    {
        ActiveRunsByThread.TryRemove(new KeyValuePair<string, KernelRuntimeActiveRunRegistration>(
            NormalizeKey(registration.ThreadId),
            registration));
        ActiveRunsByTurn.TryRemove(new KeyValuePair<string, KernelRuntimeActiveRunRegistration>(
            NormalizeKey(registration.TurnId),
            registration));
    }

    private static KernelRuntimeActiveRunRegistration? Resolve(string threadId, string? turnId)
    {
        if (!string.IsNullOrWhiteSpace(turnId)
            && ActiveRunsByTurn.TryGetValue(NormalizeKey(turnId), out var byTurn))
        {
            return byTurn;
        }

        return ActiveRunsByThread.TryGetValue(NormalizeKey(threadId), out var byThread)
            ? byThread
            : null;
    }

    private static string NormalizeKey(string value)
        => value.Trim();
}

internal sealed record KernelRuntimePersistentActiveRunRecord(
    string ThreadId,
    string TurnId,
    string RunId,
    string Message,
    DateTimeOffset StartedAtUtc,
    string RootDirectory,
    string ThreadIndexPath,
    string TurnIndexPath,
    string CancelSignalPath);

internal sealed record KernelRuntimeHostControlCheckpointRecord(
    string CheckpointRef,
    string SessionId,
    string ThreadId,
    string TurnId,
    string TurnStatus,
    string ReplayCompleteness,
    IReadOnlyList<string> StagePath,
    string? TurnLogRef,
    string? RolloutRef,
    IReadOnlyList<string> SteerInputs,
    DateTimeOffset CreatedAtUtc);

internal sealed record KernelRuntimePendingSteerRecord(
    string ThreadId,
    string? TurnId,
    IReadOnlyList<string> Inputs,
    DateTimeOffset UpdatedAtUtc);

internal static class KernelRuntimeHostControlFileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public static KernelRuntimePersistentActiveRunRecord? TryRegister(
        string? workingDirectory,
        string threadId,
        string turnId,
        string runId,
        string message,
        string? tianShuHomePath = null)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return null;
        }

        try
        {
            var root = ResolveRoot(workingDirectory, tianShuHomePath);
            Directory.CreateDirectory(Path.Combine(root, "active-runs", "by-thread"));
            Directory.CreateDirectory(Path.Combine(root, "active-runs", "by-turn"));
            Directory.CreateDirectory(Path.Combine(root, "cancellations"));

            var record = new KernelRuntimePersistentActiveRunRecord(
                threadId,
                turnId,
                runId,
                message,
                DateTimeOffset.UtcNow,
                root,
                Path.Combine(root, "active-runs", "by-thread", SanitizePathSegment(threadId) + ".json"),
                Path.Combine(root, "active-runs", "by-turn", SanitizePathSegment(turnId) + ".json"),
                Path.Combine(root, "cancellations", SanitizePathSegment(threadId) + "." + SanitizePathSegment(turnId) + ".cancel.json"));
            var payload = JsonSerializer.Serialize(record, JsonOptions);
            File.WriteAllText(record.ThreadIndexPath, payload);
            File.WriteAllText(record.TurnIndexPath, payload);
            return record;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    public static KernelRuntimeActiveRunCancellationProjection? TryCancel(
        string? workingDirectory,
        string threadId,
        string? turnId,
        string reason,
        string? tianShuHomePath = null)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return null;
        }

        try
        {
            var record = TryResolve(workingDirectory, threadId, turnId, tianShuHomePath);
            if (record is null)
            {
                return null;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(record.CancelSignalPath)!);
            File.WriteAllText(
                record.CancelSignalPath,
                JsonSerializer.Serialize(
                    new
                    {
                        reason = string.IsNullOrWhiteSpace(reason) ? "user.cancel" : reason,
                        requestedAtUtc = DateTimeOffset.UtcNow,
                        threadId = record.ThreadId,
                        turnId = record.TurnId,
                        runId = record.RunId,
                    },
                    JsonOptions));

            return new KernelRuntimeActiveRunCancellationProjection(
                Available: true,
                Reason: null,
                Reference: $"active-run://{record.ThreadId}/{record.TurnId}/{record.RunId}",
                CancelRequested: true,
                TargetThreadId: record.ThreadId,
                TargetTurnId: record.TurnId,
                TargetRunId: record.RunId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or JsonException)
        {
            return null;
        }
    }

    public static string? TryReadCancellationReason(string cancelSignalPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(cancelSignalPath));
            return document.RootElement.TryGetProperty("reason", out var reasonElement)
                ? reasonElement.GetString()
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    public static KernelRuntimeHostControlCheckpointRecord? TryPersistCheckpoint(
        string? workingDirectory,
        string sessionId,
        string threadId,
        string turnId,
        string turnStatus,
        KernelRuntimeReplaySummary replay,
        KernelRuntimeProductEvidenceReferences evidence,
        IReadOnlyList<string> steerInputs,
        string? tianShuHomePath = null)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return null;
        }

        try
        {
            var root = ResolveRoot(workingDirectory, tianShuHomePath);
            Directory.CreateDirectory(Path.Combine(root, "checkpoints"));
            var record = new KernelRuntimeHostControlCheckpointRecord(
                BuildTerminalCheckpointRef(threadId, turnId),
                sessionId,
                threadId,
                turnId,
                turnStatus,
                replay.Completeness,
                replay.StagePath,
                evidence.TurnLog.Reference,
                evidence.RolloutRecord.Reference,
                steerInputs,
                DateTimeOffset.UtcNow);
            File.WriteAllText(ResolveCheckpointPath(root, record.CheckpointRef), JsonSerializer.Serialize(record, JsonOptions));
            return record;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    public static KernelRuntimeHostControlCheckpointRecord? TryResolveCheckpoint(
        string? workingDirectory,
        string checkpointRef,
        string? tianShuHomePath = null)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || string.IsNullOrWhiteSpace(checkpointRef))
        {
            return null;
        }

        try
        {
            var root = ResolveRoot(workingDirectory, tianShuHomePath);
            var path = ResolveCheckpointPath(root, checkpointRef);
            return File.Exists(path)
                ? JsonSerializer.Deserialize<KernelRuntimeHostControlCheckpointRecord>(File.ReadAllText(path), JsonOptions)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    public static bool TryEnqueuePendingSteers(
        string? workingDirectory,
        string threadId,
        string? turnId,
        IReadOnlyList<string> inputs,
        string? tianShuHomePath = null)
    {
        var normalized = NormalizeInputs(inputs);
        if (string.IsNullOrWhiteSpace(workingDirectory) || normalized.Count == 0)
        {
            return false;
        }

        try
        {
            var root = ResolveRoot(workingDirectory, tianShuHomePath);
            Directory.CreateDirectory(Path.Combine(root, "pending-steers"));
            var path = ResolvePendingSteerPath(root, threadId);
            var existing = TryReadPendingSteers(path);
            var merged = (existing?.Inputs ?? Array.Empty<string>())
                .Concat(normalized)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var record = new KernelRuntimePendingSteerRecord(
                threadId,
                string.IsNullOrWhiteSpace(turnId) ? existing?.TurnId : turnId,
                merged,
                DateTimeOffset.UtcNow);
            File.WriteAllText(path, JsonSerializer.Serialize(record, JsonOptions));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    public static IReadOnlyList<string> TryConsumePendingSteers(
        string? workingDirectory,
        string threadId,
        string? tianShuHomePath = null)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return Array.Empty<string>();
        }

        try
        {
            var root = ResolveRoot(workingDirectory, tianShuHomePath);
            var path = ResolvePendingSteerPath(root, threadId);
            var record = TryReadPendingSteers(path);
            TryDelete(path);
            return record?.Inputs ?? Array.Empty<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return Array.Empty<string>();
        }
    }

    public static string BuildTerminalCheckpointRef(string threadId, string turnId)
        => $"checkpoint://kernel-runtime/{threadId}/{turnId}/terminal";

    public static void TryUnregister(KernelRuntimePersistentActiveRunRecord? record)
    {
        if (record is null)
        {
            return;
        }

        TryDelete(record.ThreadIndexPath);
        TryDelete(record.TurnIndexPath);
        TryDelete(record.CancelSignalPath);
    }

    private static KernelRuntimePersistentActiveRunRecord? TryResolve(
        string workingDirectory,
        string threadId,
        string? turnId,
        string? tianShuHomePath)
    {
        var root = ResolveRoot(workingDirectory, tianShuHomePath);
        if (!string.IsNullOrWhiteSpace(turnId))
        {
            var byTurn = Path.Combine(root, "active-runs", "by-turn", SanitizePathSegment(turnId!) + ".json");
            if (TryReadRecord(byTurn) is { } turnRecord)
            {
                return turnRecord;
            }
        }

        var byThread = Path.Combine(root, "active-runs", "by-thread", SanitizePathSegment(threadId) + ".json");
        return TryReadRecord(byThread);
    }

    private static KernelRuntimePersistentActiveRunRecord? TryReadRecord(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<KernelRuntimePersistentActiveRunRecord>(File.ReadAllText(path), JsonOptions)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static string ResolveRoot(string workingDirectory, string? tianShuHomePath)
        => string.IsNullOrWhiteSpace(tianShuHomePath)
            ? TianShuRuntimeLayoutPaths.ResolveRuntimeWorkspacePath("kernel-runtime", workingDirectory, "host-control")
            : TianShuRuntimeLayoutPaths.ResolveRuntimeWorkspacePathFromHome(tianShuHomePath!, "kernel-runtime", workingDirectory, "host-control");

    private static string ResolveCheckpointPath(string root, string checkpointRef)
        => Path.Combine(root, "checkpoints", SanitizePathSegment(checkpointRef) + ".json");

    private static string ResolvePendingSteerPath(string root, string threadId)
        => Path.Combine(root, "pending-steers", SanitizePathSegment(threadId) + ".json");

    private static KernelRuntimePendingSteerRecord? TryReadPendingSteers(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<KernelRuntimePendingSteerRecord>(File.ReadAllText(path), JsonOptions)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }

    private static IReadOnlyList<string> NormalizeInputs(IReadOnlyList<string>? inputs)
        => inputs?
            .Where(static input => !string.IsNullOrWhiteSpace(input))
            .Select(static input => input.Trim())
            .ToArray()
            ?? Array.Empty<string>();
}

internal static class KernelRuntimeProductEvidenceWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public static async Task<KernelRuntimeProductEvidenceReferences> TryPersistAsync(
        string? workingDirectory,
        string sessionId,
        string threadId,
        string turnId,
        string turnStatus,
        string? assistantText,
        string resultText,
        KernelRuntimeReplaySummary replay,
        KernelRuntimeDiagnosticsProjection diagnosticsProjection,
        string? controlOperation,
        string? checkpointRef,
        IReadOnlyList<string> steerInputs,
        string? tianShuHomePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return NotPersisted("turn_log_not_migrated_23_6", "not_migrated_23_6");
        }

        try
        {
            var evidenceDirectory = Path.Combine(
                string.IsNullOrWhiteSpace(tianShuHomePath)
                    ? TianShuRuntimeLayoutPaths.ResolveRuntimeWorkspacePath("kernel-runtime", workingDirectory, "evidence")
                    : TianShuRuntimeLayoutPaths.ResolveRuntimeWorkspacePathFromHome(tianShuHomePath!, "kernel-runtime", workingDirectory, "evidence"),
                SanitizePathSegment(threadId),
                SanitizePathSegment(turnId));
            Directory.CreateDirectory(evidenceDirectory);

            var turnLogPath = Path.Combine(evidenceDirectory, "turn-log.jsonl");
            var rolloutPath = Path.Combine(evidenceDirectory, "rollout.jsonl");
            var envelope = new
            {
                type = "kernel_runtime_terminal_projection",
                timestampUtc = DateTimeOffset.UtcNow,
                sessionId,
                threadId,
                turnId,
                turnStatus,
                assistantText,
                resultText,
                controlOperation,
                checkpointRef,
                steerInputs,
                replayCompleteness = replay.Completeness,
                stagePath = replay.StagePath,
                runtimeTraceRefs = diagnosticsProjection.RuntimeTraceRefs,
                diagnosticsRefs = diagnosticsProjection.DiagnosticsRefs,
                metricsEventIds = diagnosticsProjection.MetricsEventIds,
                failureCodes = diagnosticsProjection.FailureCodes,
            };
            var line = JsonSerializer.Serialize(envelope, JsonOptions);
            await File.WriteAllTextAsync(turnLogPath, line + Environment.NewLine, cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(rolloutPath, line + Environment.NewLine, cancellationToken).ConfigureAwait(false);

            return new KernelRuntimeProductEvidenceReferences(
                new KernelRuntimeFeatureAvailabilityProjection(true, null, turnLogPath),
                new KernelRuntimeFeatureAvailabilityProjection(true, null, rolloutPath),
                Array.Empty<string>());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return new KernelRuntimeProductEvidenceReferences(
                new KernelRuntimeFeatureAvailabilityProjection(false, "turn_log_persist_failed", null),
                new KernelRuntimeFeatureAvailabilityProjection(false, "rollout_record_persist_failed", null),
                ["turn_log_persist_failed", "rollout_record_persist_failed"]);
        }
    }

    private static KernelRuntimeProductEvidenceReferences NotPersisted(string turnLogReason, string rolloutReason)
        => new(
            new KernelRuntimeFeatureAvailabilityProjection(false, turnLogReason, null),
            new KernelRuntimeFeatureAvailabilityProjection(false, rolloutReason, null),
            ["turn_log_not_migrated_23_6", "rollout_record_not_migrated_23_6"]);

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }
}

public static class KernelRuntimeDiagnosticsProjector
{
    public static KernelRuntimeDiagnosticsProjection Build(
        KernelRuntimeExecutionResult result,
        KernelRuntimeReplaySummary replay,
        IReadOnlyList<RuntimeMetricsEvent> metricsEvents)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(metricsEvents);

        var runtimeTraceRefs = CollectRefs(
            result.RuntimeTraceRef,
            result.RuntimeResult?.TraceRef,
            result.RuntimeResult?.StepResults.Select(static step => step.TraceRef));
        var diagnosticsRefs = CollectRefs(
            result.DiagnosticsRef,
            result.RuntimeResult?.DiagnosticsRef,
            result.RuntimeResult?.StepResults.Select(static step => step.DiagnosticsRef));
        var missingReasons = metricsEvents
            .SelectMany(static item => item.MissingReasons)
            .Concat(metricsEvents.Select(static item => item.TokenUsage.MissingReason))
            .Concat(metricsEvents.Select(static item => item.Cost.MissingReason))
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .Select(static item => item!)
            .ToArray();

        return new KernelRuntimeDiagnosticsProjection(
            result.RuntimeTraceRef,
            result.DiagnosticsRef,
            runtimeTraceRefs,
            diagnosticsRefs,
            metricsEvents.Count,
            metricsEvents.Select(static item => item.EventId).ToArray(),
            metricsEvents.Count(static item => item.ModelCallCount > 0),
            metricsEvents.Count(static item => item.ModelCallCount == 0),
            BuildProviderToolSurface(result),
            BuildTokenUsage(metricsEvents),
            BuildCost(metricsEvents),
            missingReasons,
            replay.FailureCodes);
    }

    private static KernelRuntimeProviderToolSurfaceProjection BuildProviderToolSurface(KernelRuntimeExecutionResult result)
    {
        var surfaces = result.RuntimeResult?.StepResults
            .Where(static step => step.StepKind == RuntimeStepKind.ModelInvocation && step.Output is not null)
            .Select(static step => step.Output!)
            .Select(static output => output.TryGetProperty("providerToolSurface", out var surface) ? surface : null)
            .Where(static surface => surface is not null && surface.Kind == StructuredValueKind.Object)
            .Select(static surface => surface!)
            .ToArray()
            ?? Array.Empty<StructuredValue>();

        var names = surfaces
            .SelectMany(ReadToolSurfaceNames)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var wireApis = surfaces
            .Select(ReadToolSurfaceWireApi)
            .Where(static wireApi => !string.IsNullOrWhiteSpace(wireApi))
            .Distinct(StringComparer.Ordinal)
            .Select(static wireApi => wireApi!)
            .ToArray();
        var available = surfaces.Any(ReadToolSurfaceAvailable);

        return new KernelRuntimeProviderToolSurfaceProjection(
            available,
            available ? null : "provider_tool_surface_missing",
            wireApis,
            names.Length,
            names,
            names.Contains("spawn_agent", StringComparer.Ordinal));
    }

    private static IEnumerable<string> ReadToolSurfaceNames(StructuredValue surface)
    {
        if (!surface.TryGetProperty("names", out var names)
            || names is null
            || names.Kind != StructuredValueKind.Array)
        {
            yield break;
        }

        foreach (var item in names.Items)
        {
            if (item.Kind == StructuredValueKind.String && !string.IsNullOrWhiteSpace(item.StringValue))
            {
                yield return item.StringValue!;
            }
        }
    }

    private static string? ReadToolSurfaceWireApi(StructuredValue surface)
        => surface.TryGetProperty("wireApi", out var wireApi)
           && wireApi is not null
           && wireApi.Kind == StructuredValueKind.String
           && !string.IsNullOrWhiteSpace(wireApi.StringValue)
            ? wireApi.StringValue
            : null;

    private static bool ReadToolSurfaceAvailable(StructuredValue surface)
        => surface.TryGetProperty("available", out var available)
           && available is not null
           && available.Kind == StructuredValueKind.Boolean
           && available.BooleanValue == true;

    private static KernelRuntimeTokenUsageProjection BuildTokenUsage(IReadOnlyList<RuntimeMetricsEvent> metricsEvents)
    {
        var available = metricsEvents
            .Select(static item => item.TokenUsage)
            .Where(static usage => usage.Available)
            .ToArray();
        if (available.Length == 0)
        {
            var missingReason = metricsEvents
                .Select(static item => item.TokenUsage.MissingReason)
                .FirstOrDefault(static reason => !string.IsNullOrWhiteSpace(reason))
                ?? "token_usage_missing";
            return new KernelRuntimeTokenUsageProjection(
                Available: false,
                MissingReason: missingReason,
                Estimated: false,
                InputTokens: null,
                CachedInputTokens: null,
                OutputTokens: null,
                ReasoningOutputTokens: null,
                TotalTokens: null,
                Sources: Array.Empty<string>());
        }

        return new KernelRuntimeTokenUsageProjection(
            Available: true,
            MissingReason: null,
            Estimated: available.Any(static usage => usage.Estimated),
            InputTokens: SumNullable(available.Select(static usage => usage.InputTokens)),
            CachedInputTokens: SumNullable(available.Select(static usage => usage.CachedInputTokens)),
            OutputTokens: SumNullable(available.Select(static usage => usage.OutputTokens)),
            ReasoningOutputTokens: SumNullable(available.Select(static usage => usage.ReasoningOutputTokens)),
            TotalTokens: SumNullable(available.Select(static usage => usage.TotalTokens)),
            Sources: available.Select(static usage => usage.Source).Distinct(StringComparer.Ordinal).ToArray());
    }

    private static KernelRuntimeCostProjection BuildCost(IReadOnlyList<RuntimeMetricsEvent> metricsEvents)
    {
        var available = metricsEvents
            .Select(static item => item.Cost)
            .Where(static cost => cost.Available)
            .ToArray();
        if (available.Length == 0)
        {
            var missingReason = metricsEvents
                .Select(static item => item.Cost.MissingReason)
                .FirstOrDefault(static reason => !string.IsNullOrWhiteSpace(reason))
                ?? "cost_missing";
            return new KernelRuntimeCostProjection(
                Available: false,
                MissingReason: missingReason,
                EstimatedCost: null,
                Currency: null,
                PriceModelVersion: null);
        }

        return new KernelRuntimeCostProjection(
            Available: true,
            MissingReason: null,
            EstimatedCost: available
                .Where(static cost => cost.EstimatedCost.HasValue)
                .Select(static cost => cost.EstimatedCost!.Value)
                .DefaultIfEmpty(0m)
                .Sum(),
            Currency: available.Select(static cost => cost.Currency).FirstOrDefault(static currency => !string.IsNullOrWhiteSpace(currency)),
            PriceModelVersion: available.Select(static cost => cost.PriceModelVersion).FirstOrDefault(static version => !string.IsNullOrWhiteSpace(version)));
    }

    private static IReadOnlyList<string> CollectRefs(
        string? first,
        string? second,
        IEnumerable<string?>? rest)
        => new[] { first, second }
            .Concat(rest ?? Array.Empty<string?>())
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .Select(static item => item!)
            .ToArray();

    private static long? SumNullable(IEnumerable<long?> values)
    {
        long sum = 0;
        var hasValue = false;
        foreach (var value in values)
        {
            if (!value.HasValue)
            {
                continue;
            }

            sum += value.Value;
            hasValue = true;
        }

        return hasValue ? sum : null;
    }
}
