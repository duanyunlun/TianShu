using TianShu.Contracts.Agents;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Provider;
using TianShu.Contracts.Tools;
using TianShu.Execution.Runtime;
using TianShu.Kernel.Abstractions;
using TianShu.Kernel.Interpretation;

namespace TianShu.RuntimeComposition;

/// <summary>
/// Kernel 到 Execution Runtime 的受控组合入口。
/// Controlled composition entry point from Kernel to Execution Runtime.
/// </summary>
public interface IKernelRuntimeExecutionLoop
{
    /// <summary>
    /// 运行 Stable Kernel Core，并按选项决定是否执行已批准的 ExecutionPlan。
    /// Runs Stable Kernel Core and optionally executes the approved ExecutionPlan.
    /// </summary>
    Task<KernelRuntimeExecutionResult> RunAsync(
        CoreIntent intent,
        KernelRuntimeExecutionOptions options,
        CancellationToken cancellationToken);

    /// <summary>
    /// 运行受控反应式 StageGraph 循环，逐 Stage 执行并由 StageResult 决定下一跳。
    /// Runs the controlled reactive StageGraph loop, executing one stage at a time and deciding transitions from StageResult.
    /// </summary>
    Task<KernelRuntimeExecutionResult> RunReactiveAsync(
        CoreIntent intent,
        KernelRuntimeExecutionOptions options,
        CancellationToken cancellationToken);
}

/// <summary>
/// Kernel 与 Runtime 组合执行选项。
/// Options for composed Kernel and Runtime execution.
/// </summary>
public sealed record KernelRuntimeExecutionOptions(
    KernelRunOptions KernelOptions,
    ExecutionRuntimeContext RuntimeContext,
    bool ExecuteRuntimePlan);

/// <summary>
/// Kernel 与 Runtime 组合执行结果。
/// Result of composed Kernel and Runtime execution.
/// </summary>
public sealed record KernelRuntimeExecutionResult(
    KernelRunResult KernelResult,
    ExecutionRunResult? RuntimeResult,
    KernelRuntimeExecutionDisposition Disposition,
    KernelTraceId? KernelTraceId,
    string? RuntimeTraceRef,
    string? DiagnosticsRef);

/// <summary>
/// Kernel 与 Runtime 组合执行最终分流状态。
/// Final disposition for composed Kernel and Runtime execution.
/// </summary>
public enum KernelRuntimeExecutionDisposition
{
    Unspecified = 0,
    ApprovalOnly = 1,
    KernelRejected = 2,
    RuntimeCompleted = 3,
    RuntimeBlocked = 4,
    RuntimeFailed = 5,
}

/// <summary>
/// Kernel/Runtime replay 摘要，供验收复盘 graph -> stage -> step -> result -> metrics 关系。
/// Kernel/Runtime replay summary for acceptance replay of graph -> stage -> step -> result -> metrics relationships.
/// </summary>
public sealed record KernelRuntimeReplaySummary(
    string RunId,
    string IntentId,
    string? GraphId,
    string? PlanId,
    string? ExecutionId,
    KernelRuntimeExecutionDisposition Disposition,
    string Completeness,
    IReadOnlyList<string> StagePath,
    IReadOnlyList<KernelRuntimeReplayStepSummary> Steps,
    IReadOnlyList<string> FailureCodes,
    IReadOnlyList<string> MetricsEventIds);

/// <summary>
/// 单个 RuntimeStep 的 replay 摘要。
/// Replay summary for one RuntimeStep.
/// </summary>
public sealed record KernelRuntimeReplayStepSummary(
    string StageId,
    string StepId,
    RuntimeStepKind StepKind,
    RuntimeStepResultStatus Status,
    string? RuntimePlanId,
    string? RuntimeTraceRef,
    string? DiagnosticsRef,
    IReadOnlyList<string> MetricsEventIds);

/// <summary>
/// Runtime blocked 后的恢复状态机判定。
/// Recovery state-machine decision after a runtime blocked result.
/// </summary>
public sealed record KernelRuntimeRecoveryDecision(
    KernelRuntimeRecoveryDisposition Disposition,
    string? FailureCode,
    string? RollbackTargetRef,
    IReadOnlyList<string> CheckpointRefs);

/// <summary>
/// Kernel/Runtime 恢复状态机状态。
/// Kernel/Runtime recovery state-machine disposition.
/// </summary>
public enum KernelRuntimeRecoveryDisposition
{
    Unspecified = 0,
    NotRequired = 1,
    RecoveryBlockedMissingCheckpoint = 2,
    RecoveryCandidatePendingKernelValidation = 3,
}

/// <summary>
/// Kernel/Runtime 恢复状态机，只给出受控恢复门禁，不自动晋升恢复方案。
/// Kernel/Runtime recovery state machine that only produces controlled recovery gates without auto-promoting recovery plans.
/// </summary>
public static class KernelRuntimeRecoveryStateMachine
{
    public static KernelRuntimeRecoveryDecision Evaluate(
        KernelRuntimeExecutionResult result,
        KernelTraceSummary? traceSummary = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.RuntimeResult is null || result.RuntimeResult.Status == RuntimeStepResultStatus.Succeeded)
        {
            return new KernelRuntimeRecoveryDecision(
                KernelRuntimeRecoveryDisposition.NotRequired,
                FailureCode: null,
                RollbackTargetRef: null,
                Array.Empty<string>());
        }

        var checkpointRefs = traceSummary?.CheckpointRefs ?? Array.Empty<string>();
        var failureCode = result.RuntimeResult.StepResults
            .Select(static item => item.Failure?.Code)
            .FirstOrDefault(static code => !string.IsNullOrWhiteSpace(code));

        if (checkpointRefs.Count == 0)
        {
            return new KernelRuntimeRecoveryDecision(
                KernelRuntimeRecoveryDisposition.RecoveryBlockedMissingCheckpoint,
                failureCode,
                RollbackTargetRef: null,
                checkpointRefs);
        }

        return new KernelRuntimeRecoveryDecision(
            KernelRuntimeRecoveryDisposition.RecoveryCandidatePendingKernelValidation,
            failureCode,
            result.KernelResult.ExecutionPlan?.SourceGraphId.Value,
            checkpointRefs);
    }
}

/// <summary>
/// Kernel/Runtime replay 投影器，只读取组合结果和 metrics event，不访问 runtime 内部状态。
/// Kernel/Runtime replay projector that only reads composed results and metrics events without accessing runtime internals.
/// </summary>
public static class KernelRuntimeReplayProjector
{
    public static KernelRuntimeReplaySummary Build(
        KernelRuntimeExecutionResult result,
        IReadOnlyList<RuntimeMetricsEvent>? metricsEvents = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        metricsEvents ??= Array.Empty<RuntimeMetricsEvent>();
        var plan = result.KernelResult.ExecutionPlan;
        if (result.RuntimeResult is not null && plan is null)
        {
            throw new InvalidOperationException("RuntimeResult 存在时必须同时存在 Kernel ExecutionPlan。");
        }

        var stepSummaries = new List<KernelRuntimeReplayStepSummary>();
        var failureCodes = new List<string>();
        if (plan is not null)
        {
            var runtimeResults = result.RuntimeResult?.StepResults ?? Array.Empty<RuntimeStepResult>();
            var reactiveRun = result.RuntimeResult is not null && result.KernelResult.StageResults.Count > 0;
            if (reactiveRun)
            {
                foreach (var runtimeStepResult in runtimeResults)
                {
                    if (runtimeStepResult.Failure is not null)
                    {
                        failureCodes.Add(runtimeStepResult.Failure.Code);
                    }

                    var runtimePlanId = ReadOutputString(runtimeStepResult.Output, "runtimePlanId") ?? result.RuntimeResult!.PlanId;
                    var stepMetricIds = ResolveStepMetricIds(metricsEvents, runtimeStepResult.StepId, runtimePlanId);
                    stepSummaries.Add(new KernelRuntimeReplayStepSummary(
                        ResolveStageId(plan, metricsEvents, runtimeStepResult),
                        runtimeStepResult.StepId,
                        runtimeStepResult.StepKind,
                        runtimeStepResult.Status,
                        runtimePlanId,
                        runtimeStepResult.TraceRef,
                        runtimeStepResult.DiagnosticsRef,
                        stepMetricIds));
                }
            }
            else
            {
                var runtimeResultsByStep = runtimeResults.ToDictionary(static item => item.StepId, StringComparer.Ordinal);
                foreach (var step in plan.Steps)
                {
                    if (result.RuntimeResult is not null && !runtimeResultsByStep.TryGetValue(step.StepId, out var stepResult))
                    {
                        throw new InvalidOperationException($"RuntimeResult 缺少 step 结果：{step.StepId}。");
                    }

                    runtimeResultsByStep.TryGetValue(step.StepId, out var runtimeStepResult);
                    if (runtimeStepResult?.Failure is not null)
                    {
                        failureCodes.Add(runtimeStepResult.Failure.Code);
                    }

                    var runtimePlanId = ReadOutputString(runtimeStepResult?.Output, "runtimePlanId") ?? result.RuntimeResult?.PlanId;
                    var stepMetricIds = ResolveStepMetricIds(metricsEvents, step.StepId, runtimePlanId);
                    stepSummaries.Add(new KernelRuntimeReplayStepSummary(
                        step.SourceStageId.Value,
                        step.StepId,
                        step.StepKind,
                        runtimeStepResult?.Status ?? RuntimeStepResultStatus.Unspecified,
                        runtimePlanId,
                        runtimeStepResult?.TraceRef,
                        runtimeStepResult?.DiagnosticsRef,
                        stepMetricIds));
                }
            }
        }

        return new KernelRuntimeReplaySummary(
            result.KernelResult.RunId.Value,
            result.KernelResult.SourceIntentId.Value,
            plan?.SourceGraphId.Value,
            plan?.PlanId,
            result.RuntimeResult?.ExecutionId.Value,
            result.Disposition,
            ResolveCompleteness(result, stepSummaries),
            ResolveStagePath(result, stepSummaries),
            stepSummaries,
            failureCodes,
            metricsEvents.Select(static item => item.EventId).ToArray());
    }

    private static IReadOnlyList<string> ResolveStagePath(
        KernelRuntimeExecutionResult result,
        IReadOnlyList<KernelRuntimeReplayStepSummary> steps)
    {
        var stageResults = result.KernelResult.StageResults
            .Select(static item => item.StageId.Value)
            .ToArray();
        if (stageResults.Length > 0)
        {
            return stageResults;
        }

        return CollapseConsecutiveStageIds(steps.Select(static item => item.StageId));
    }

    private static IReadOnlyList<string> CollapseConsecutiveStageIds(IEnumerable<string> stageIds)
    {
        var path = new List<string>();
        foreach (var stageId in stageIds)
        {
            if (path.Count == 0 || !string.Equals(path[^1], stageId, StringComparison.Ordinal))
            {
                path.Add(stageId);
            }
        }

        return path;
    }

    private static string ResolveCompleteness(
        KernelRuntimeExecutionResult result,
        IReadOnlyList<KernelRuntimeReplayStepSummary> steps)
    {
        if (result.Disposition == KernelRuntimeExecutionDisposition.RuntimeCompleted
            && steps.Count > 0
            && steps.All(static item => item.Status == RuntimeStepResultStatus.Succeeded && !string.IsNullOrWhiteSpace(item.RuntimeTraceRef)))
        {
            return result.KernelResult.StageResults.Count > 0
                ? "live-pass-reactive-graph"
                : "live-pass-fixed-graph";
        }

        if (result.RuntimeResult is not null)
        {
            return "live-partial";
        }

        return "kernel-only";
    }

    private static IReadOnlyList<string> ResolveStepMetricIds(
        IReadOnlyList<RuntimeMetricsEvent> metricsEvents,
        string stepId,
        string? runtimePlanId)
    {
        var matched = metricsEvents
            .Where(item => string.Equals(item.StepId, stepId, StringComparison.Ordinal)
                && (string.IsNullOrWhiteSpace(runtimePlanId)
                    || string.Equals(item.PlanId, runtimePlanId, StringComparison.Ordinal)))
            .Select(static item => item.EventId)
            .ToArray();
        if (matched.Length > 0)
        {
            return matched;
        }

        return metricsEvents
            .Where(item => string.Equals(item.StepId, stepId, StringComparison.Ordinal))
            .Select(static item => item.EventId)
            .ToArray();
    }

    private static string ResolveStageId(
        ExecutionPlan plan,
        IReadOnlyList<RuntimeMetricsEvent> metricsEvents,
        RuntimeStepResult stepResult)
        => ReadOutputString(stepResult.Output, "sourceStageId")
            ?? metricsEvents.FirstOrDefault(item => string.Equals(item.StepId, stepResult.StepId, StringComparison.Ordinal))?.StageId
            ?? plan.Steps.FirstOrDefault(step => string.Equals(step.StepId, stepResult.StepId, StringComparison.Ordinal))?.SourceStageId.Value
            ?? "stage.unknown";

    private static string? ReadOutputString(StructuredValue? output, string propertyName)
        => output is not null
            && output.TryGetProperty(propertyName, out var property)
            && property is not null
            && property.Kind is StructuredValueKind.String or StructuredValueKind.Number
            ? property.GetString()
            : null;
}

/// <summary>
/// 默认组合循环，只负责编排 Kernel -> Runtime 交接，不拥有 Kernel 或 Runtime 内部语义。
/// Default composition loop that only orchestrates the Kernel -> Runtime handoff without owning Kernel or Runtime semantics.
/// </summary>
public sealed class AdaptiveRuntimeExecutionLoop : IKernelRuntimeExecutionLoop
{
    private const string SpawnAgentToolId = "spawn_agent";
    private const string SubAgentModuleId = "module.sub_agent";
    private const string SubAgentCapabilityId = "sub_agent.spawn";
    private readonly IStableKernelCore kernelCore;
    private readonly IExecutionRuntime executionRuntime;
    private readonly IStageGraphInterpreter stageGraphInterpreter;
    private readonly ISubAgentSpawnLedger subAgentSpawnLedger;
    private readonly SubAgentSpawnQuota subAgentSpawnQuota;

    public AdaptiveRuntimeExecutionLoop(
        IStableKernelCore kernelCore,
        IExecutionRuntime executionRuntime,
        IStageGraphInterpreter? stageGraphInterpreter = null,
        ISubAgentSpawnLedger? subAgentSpawnLedger = null,
        SubAgentSpawnQuota? subAgentSpawnQuota = null)
    {
        this.kernelCore = kernelCore ?? throw new ArgumentNullException(nameof(kernelCore));
        this.executionRuntime = executionRuntime ?? throw new ArgumentNullException(nameof(executionRuntime));
        this.stageGraphInterpreter = stageGraphInterpreter ?? new StageGraphInterpreter();
        this.subAgentSpawnLedger = subAgentSpawnLedger ?? new SubAgentSpawnLedger();
        this.subAgentSpawnQuota = subAgentSpawnQuota ?? new SubAgentSpawnQuota(1, 8, 32, 0);
    }

    public async Task<KernelRuntimeExecutionResult> RunAsync(
        CoreIntent intent,
        KernelRuntimeExecutionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(options);

        var kernelOptions = NormalizeKernelOptions(options.KernelOptions, options.RuntimeContext.KernelRunId);
        var kernelResult = await kernelCore.RunAsync(intent, kernelOptions, cancellationToken).ConfigureAwait(false);
        if (!kernelResult.Validation.IsApproved || kernelResult.ExecutionPlan is null)
        {
            return CreateKernelOnlyResult(kernelResult, KernelRuntimeExecutionDisposition.KernelRejected);
        }

        if (!options.ExecuteRuntimePlan)
        {
            return CreateKernelOnlyResult(kernelResult, KernelRuntimeExecutionDisposition.ApprovalOnly);
        }

        var runtimeContext = AlignRuntimeContext(options.RuntimeContext, kernelResult.RunId);
        var runtimeResult = await executionRuntime.ExecuteAsync(kernelResult.ExecutionPlan, runtimeContext, cancellationToken).ConfigureAwait(false);
        return new KernelRuntimeExecutionResult(
            kernelResult,
            runtimeResult,
            MapDisposition(runtimeResult.Status),
            kernelResult.TraceId,
            runtimeResult.TraceRef,
            runtimeResult.DiagnosticsRef);
    }

    public async Task<KernelRuntimeExecutionResult> RunReactiveAsync(
        CoreIntent intent,
        KernelRuntimeExecutionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(options);

        var kernelOptions = NormalizeKernelOptions(options.KernelOptions, options.RuntimeContext.KernelRunId);
        var kernelResult = await kernelCore.RunAsync(intent, kernelOptions, cancellationToken).ConfigureAwait(false);
        if (!kernelResult.Validation.IsApproved || kernelResult.ExecutionPlan is null)
        {
            return CreateKernelOnlyResult(kernelResult, KernelRuntimeExecutionDisposition.KernelRejected);
        }

        if (!options.ExecuteRuntimePlan)
        {
            return CreateKernelOnlyResult(kernelResult, KernelRuntimeExecutionDisposition.ApprovalOnly);
        }

        if (kernelResult.ApprovedStageGraph is null)
        {
            return CreateRuntimeFailureResult(
                kernelResult,
                options.RuntimeContext.ExecutionId,
                "runtime.reactive.graph_missing",
                "反应式执行需要 KernelRunResult 携带已批准 StageGraph。");
        }

        var runtimeContext = AlignRuntimeContext(options.RuntimeContext, kernelResult.RunId);
        var graph = kernelResult.ApprovedStageGraph;
        var approvedPlan = kernelResult.ExecutionPlan;
        var stageResults = new List<StageResult>();
        var runtimeStepResults = new List<RuntimeStepResult>();
        var currentStageId = graph.EntryStageId;
        var toolReturnCount = 0;
        var iteration = 0;
        IReadOnlyList<ModelToolRequest> pendingToolRequests = Array.Empty<ModelToolRequest>();
        IReadOnlyList<ModelToolResult> pendingToolEvidence = Array.Empty<ModelToolResult>();
        RuntimeStepResultStatus aggregateStatus = RuntimeStepResultStatus.Succeeded;
        string? lastRuntimeTraceRef = null;
        string? lastDiagnosticsRef = null;
        var intentModelInputs = ResolveIntentModelInputs(intent);
        var subAgentLineage = CreateRootSubAgentLineage(kernelResult.RunId);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (iteration++ > Math.Max(1, graph.Budgets.ToolCallBudget + graph.Stages.Count + 4))
            {
                return CreateRuntimeFailureResult(
                    kernelResult,
                    runtimeContext.ExecutionId,
                    "runtime.reactive.iteration_budget_exhausted",
                    "反应式执行超过组合层安全迭代上限。");
            }

            var currentStage = graph.Stages.FirstOrDefault(stage => stage.StageId == currentStageId);
            if (currentStage is null)
            {
                return CreateRuntimeFailureResult(
                    kernelResult,
                    runtimeContext.ExecutionId,
                    "runtime.reactive.stage_missing",
                    $"StageGraph 缺少当前 stage：{currentStageId.Value}。");
            }

            string? toolPlanFailureCode = null;
            string? toolPlanFailureMessage = null;
            IReadOnlyList<RuntimeStepResult> precomputedToolStepResults = Array.Empty<RuntimeStepResult>();
            var stagePlan = currentStage.Kind == "tool-exec"
                ? CreateToolRequestExecutionPlan(
                    approvedPlan,
                    currentStage,
                    pendingToolRequests,
                    intent,
                    runtimeContext.Governance,
                    subAgentLineage,
                    iteration,
                    precomputedToolStepResults: out precomputedToolStepResults,
                    out toolPlanFailureCode,
                    out toolPlanFailureMessage)
                : CreateStageExecutionPlan(
                    approvedPlan,
                    currentStage.StageId,
                    iteration,
                    currentStage.Kind == "model-reason" ? pendingToolEvidence : Array.Empty<ModelToolResult>(),
                    currentStage.Kind == "model-reason" ? intentModelInputs : Array.Empty<ProviderInputItem>());
            if (stagePlan is null)
            {
                return CreateRuntimeFailureResult(
                    kernelResult,
                    runtimeContext.ExecutionId,
                    toolPlanFailureCode ?? "runtime.reactive.stage_steps_missing",
                    toolPlanFailureMessage ?? $"ExecutionPlan 缺少 stage 对应的 RuntimeStep：{currentStage.StageId.Value}。");
            }

            var runtimeResult = await executionRuntime.ExecuteAsync(stagePlan, runtimeContext, cancellationToken).ConfigureAwait(false);
            if (precomputedToolStepResults.Count > 0)
            {
                var mergedStepResults = runtimeResult.StepResults.Concat(precomputedToolStepResults).ToArray();
                runtimeResult = new ExecutionRunResult(
                    runtimeResult.PlanId,
                    runtimeResult.ExecutionId,
                    ResolveMergedRunStatus(mergedStepResults),
                    mergedStepResults,
                    runtimeResult.DiagnosticsRef,
                    runtimeResult.TraceRef);
            }
            runtimeStepResults.AddRange(runtimeResult.StepResults);
            lastRuntimeTraceRef = runtimeResult.TraceRef;
            lastDiagnosticsRef = runtimeResult.DiagnosticsRef;

            var stageResult = ProjectStageResult(currentStage, runtimeResult, graph, toolReturnCount);
            stageResults.Add(stageResult);
            aggregateStatus = MergeStatus(aggregateStatus, runtimeResult.Status);

            if (currentStage.Kind == "model-reason" && StageResultHasSignal(stageResult, "tool.requests.available"))
            {
                var toolStage = graph.Stages.FirstOrDefault(stage => stage.Kind == "tool-exec");
                if (toolStage is null)
                {
                    return CreateRuntimeFailureResult(
                        kernelResult,
                        runtimeContext.ExecutionId,
                        "runtime.reactive.tool_stage_missing",
                        "模型请求工具调用，但 StageGraph 缺少 tool-exec stage。");
                }

                if (!TryReadToolRequests(
                    runtimeResult,
                    toolStage,
                    runtimeContext.Governance,
                    out var toolRequests,
                    out var failureCode,
                    out var failureMessage))
                {
                    return CreateRuntimeFailureResult(
                        kernelResult,
                        runtimeContext.ExecutionId,
                        failureCode,
                        failureMessage);
                }

                pendingToolRequests = toolRequests;
            }
            else if (currentStage.Kind == "model-reason")
            {
                pendingToolRequests = Array.Empty<ModelToolRequest>();
            }
            if (currentStage.Kind == "model-reason")
            {
                pendingToolEvidence = Array.Empty<ModelToolResult>();
            }

            var interpreterContext = new KernelInterpreterContext(
                intent,
                new KernelRunState(kernelResult.RunId, intent.IntentId, KernelRunLifecycleState.Executing, graph.GraphId, currentStage.StageId),
                kernelOptions,
                runtimeContext.Governance);
            var decision = await stageGraphInterpreter.DecideNextStageAsync(graph, stageResult, interpreterContext, cancellationToken).ConfigureAwait(false);

            if (currentStage.Kind == "tool-exec" && stageResult.Status == StageResultStatus.Succeeded)
            {
                if (!TryReadToolResults(
                    runtimeResult,
                    pendingToolRequests,
                    out var toolResults,
                    out var failureCode,
                    out var failureMessage))
                {
                    return CreateRuntimeFailureResult(
                        kernelResult,
                        runtimeContext.ExecutionId,
                        failureCode,
                        failureMessage);
                }

                toolReturnCount++;
                pendingToolEvidence = toolResults;
                pendingToolRequests = Array.Empty<ModelToolRequest>();
            }

            if (decision.ShouldStop || decision.NextStageId is null)
            {
                break;
            }

            currentStageId = decision.NextStageId.Value;
        }

        var aggregateRuntimeResult = new ExecutionRunResult(
            approvedPlan.PlanId,
            runtimeContext.ExecutionId,
            aggregateStatus,
            runtimeStepResults,
            lastDiagnosticsRef,
            lastRuntimeTraceRef);
        var reactiveKernelResult = new KernelRunResult(
            kernelResult.RunId,
            kernelResult.SourceIntentId,
            kernelResult.LifecycleState,
            kernelResult.Validation,
            kernelResult.ExecutionPlan,
            kernelResult.TraceId,
            kernelResult.ApprovedStageGraph,
            stageResults,
            kernelResult.Metadata);

        return new KernelRuntimeExecutionResult(
            reactiveKernelResult,
            aggregateRuntimeResult,
            MapDisposition(aggregateRuntimeResult.Status),
            kernelResult.TraceId,
            aggregateRuntimeResult.TraceRef,
            aggregateRuntimeResult.DiagnosticsRef);
    }

    private static KernelRuntimeExecutionResult CreateKernelOnlyResult(
        KernelRunResult kernelResult,
        KernelRuntimeExecutionDisposition disposition)
        => new(
            kernelResult,
            RuntimeResult: null,
            disposition,
            kernelResult.TraceId,
            RuntimeTraceRef: null,
            DiagnosticsRef: null);

    private static KernelRuntimeExecutionDisposition MapDisposition(RuntimeStepResultStatus status)
        => status switch
        {
            RuntimeStepResultStatus.Succeeded => KernelRuntimeExecutionDisposition.RuntimeCompleted,
            RuntimeStepResultStatus.Blocked => KernelRuntimeExecutionDisposition.RuntimeBlocked,
            RuntimeStepResultStatus.Failed or RuntimeStepResultStatus.Cancelled or RuntimeStepResultStatus.Unspecified => KernelRuntimeExecutionDisposition.RuntimeFailed,
            _ => KernelRuntimeExecutionDisposition.RuntimeFailed,
        };

    private static ExecutionPlan? CreateStageExecutionPlan(
        ExecutionPlan approvedPlan,
        StageId stageId,
        int iteration,
        IReadOnlyList<ModelToolResult>? pendingToolEvidence = null,
        IReadOnlyList<ProviderInputItem>? modelInputs = null)
    {
        var stageSteps = approvedPlan.Steps
            .Where(step => step.SourceStageId == stageId)
            .ToArray();
        if (stageSteps.Length == 0)
        {
            return null;
        }

        if (pendingToolEvidence is { Count: > 0 })
        {
            stageSteps = InjectToolEvidence(stageSteps, pendingToolEvidence);
        }

        if (modelInputs is { Count: > 0 })
        {
            stageSteps = InjectModelInputs(stageSteps, modelInputs);
        }

        return new ExecutionPlan(
            $"{approvedPlan.PlanId}-stage-{iteration:000}-{stageId.Value}",
            approvedPlan.SourceGraphId,
            approvedPlan.SourceIntentId,
            stageSteps,
            approvedPlan.Policy,
            approvedPlan.TracePolicy,
            approvedPlan.Metadata);
    }

    private static IReadOnlyList<ProviderInputItem> ResolveIntentModelInputs(CoreIntent intent)
    {
        var inputs = new List<ProviderInputItem>();
        if (intent.Metadata.Entries.TryGetValue("message", out var message)
            && message.Kind == StructuredValueKind.String
            && !string.IsNullOrWhiteSpace(message.StringValue))
        {
            inputs.Add(new TextProviderInputItem(message.StringValue!));
        }

        if (intent.Metadata.Entries.TryGetValue("steerInputs", out var steerInputs)
            && steerInputs.Kind == StructuredValueKind.Array)
        {
            foreach (var input in steerInputs.Items)
            {
                if (input.Kind == StructuredValueKind.String && !string.IsNullOrWhiteSpace(input.StringValue))
                {
                    inputs.Add(new TextProviderInputItem(input.StringValue!));
                }
            }
        }

        return inputs;
    }

    private static RuntimeStep[] InjectModelInputs(RuntimeStep[] stageSteps, IReadOnlyList<ProviderInputItem> modelInputs)
        => stageSteps
            .Select(step => step is ModelInvocationStep modelStep ? InjectModelInputs(modelStep, modelInputs) : step)
            .ToArray();

    private static ModelInvocationStep InjectModelInputs(ModelInvocationStep step, IReadOnlyList<ProviderInputItem> modelInputs)
    {
        var inputs = step.InputEnvelope.Inputs
            .Concat(modelInputs)
            .ToArray();
        var metadata = new Dictionary<string, StructuredValue>(step.InputEnvelope.Metadata.Entries, StringComparer.Ordinal)
        {
            ["productInput.count"] = StructuredValue.FromPlainObject(modelInputs.Count),
            ["productInput.source"] = StructuredValue.FromString("turn.message_and_steer"),
        };
        var inputEnvelope = new ProviderInvocationRequest(
            step.InputEnvelope.ExecutionId,
            step.InputEnvelope.ProviderKey,
            step.InputEnvelope.Model,
            step.InputEnvelope.Conversation,
            inputs,
            step.InputEnvelope.PreviousTurnState,
            new MetadataBag(metadata),
            step.InputEnvelope.InvocationContext);

        return new ModelInvocationStep(
            step.StepId,
            step.SourceIntentId,
            step.SourceGraphId,
            step.SourceStageId,
            step.SourceKernelOperationId,
            step.ProviderModuleId,
            step.ModelRoute,
            inputEnvelope,
            step.Permission,
            step.SideEffect,
            step.Budget,
            step.ExpectedOutputContract,
            step.TracePolicy,
            step.Metadata);
    }

    private static RuntimeStep[] InjectToolEvidence(RuntimeStep[] stageSteps, IReadOnlyList<ModelToolResult> toolEvidence)
        => stageSteps
            .Select(step => step is ModelInvocationStep modelStep ? InjectToolEvidence(modelStep, toolEvidence) : step)
            .ToArray();

    private static ModelInvocationStep InjectToolEvidence(ModelInvocationStep step, IReadOnlyList<ModelToolResult> toolEvidence)
    {
        var inputs = step.InputEnvelope.Inputs
            .Concat(toolEvidence.SelectMany(static result => new ProviderInputItem[]
            {
                new ToolCallProviderInputItem(new CallId(result.CallId), result.ToolId, result.Arguments),
                new ToolResultProviderInputItem(new CallId(result.CallId), result.ToProviderInputValue()),
            }))
            .ToArray();
        var metadata = new Dictionary<string, StructuredValue>(step.InputEnvelope.Metadata.Entries, StringComparer.Ordinal)
        {
            ["toolEvidence.count"] = StructuredValue.FromPlainObject(toolEvidence.Count),
            ["toolEvidence.source"] = StructuredValue.FromString("tool-exec.toolResults"),
        };
        var inputEnvelope = new ProviderInvocationRequest(
            step.InputEnvelope.ExecutionId,
            step.InputEnvelope.ProviderKey,
            step.InputEnvelope.Model,
            step.InputEnvelope.Conversation,
            inputs,
            step.InputEnvelope.PreviousTurnState,
            new MetadataBag(metadata),
            step.InputEnvelope.InvocationContext);

        return new ModelInvocationStep(
            step.StepId,
            step.SourceIntentId,
            step.SourceGraphId,
            step.SourceStageId,
            step.SourceKernelOperationId,
            step.ProviderModuleId,
            step.ModelRoute,
            inputEnvelope,
            step.Permission,
            step.SideEffect,
            step.Budget,
            step.ExpectedOutputContract,
            step.TracePolicy,
            step.Metadata);
    }

    private ExecutionPlan? CreateToolRequestExecutionPlan(
        ExecutionPlan approvedPlan,
        StageNode stage,
        IReadOnlyList<ModelToolRequest> toolRequests,
        CoreIntent intent,
        GovernanceEnvelope governance,
        SubAgentLineage subAgentLineage,
        int iteration,
        out IReadOnlyList<RuntimeStepResult> precomputedToolStepResults,
        out string? failureCode,
        out string? failureMessage)
    {
        precomputedToolStepResults = Array.Empty<RuntimeStepResult>();
        failureCode = null;
        failureMessage = null;

        var template = approvedPlan.Steps
            .OfType<ToolInvocationStep>()
            .FirstOrDefault(step => step.SourceStageId == stage.StageId);
        if (template is null)
        {
            failureCode = "runtime.reactive.stage_steps_missing";
            failureMessage = $"ExecutionPlan 缺少 stage 对应的 ToolInvocationStep：{stage.StageId.Value}。";
            return null;
        }

        if (toolRequests.Count == 0)
        {
            failureCode = "runtime.reactive.tool_requests_missing";
            failureMessage = "模型声明存在工具请求，但缺少可物化的 toolRequests[] 明细。";
            return null;
        }

        var steps = new List<RuntimeStep>(toolRequests.Count);
        var precomputedResults = new List<RuntimeStepResult>();
        for (var index = 0; index < toolRequests.Count; index++)
        {
            var request = toolRequests[index];
            if (!IsToolAllowed(request.ToolId, stage, governance))
            {
                failureCode = "runtime.reactive.tool_request_not_allowed";
                failureMessage = $"模型请求的工具不在当前 tool-exec allow-list 或治理边界内：{request.ToolId}。";
                return null;
            }

            if (string.Equals(request.ToolId, "spawn_agent", StringComparison.Ordinal))
            {
                var decision = subAgentSpawnLedger.TryAdmitSpawn(subAgentLineage, subAgentSpawnQuota);
                if (!decision.Admitted)
                {
                    precomputedResults.Add(CreateBlockedSubAgentToolResult(template, request, decision, iteration, index));
                    continue;
                }

                steps.Add(CreateSubAgentModuleStep(template, stage, request, intent, governance, subAgentLineage, decision.ChildLineage!, subAgentSpawnQuota, iteration, index));
                continue;
            }

            var permission = new PermissionEnvelope(
                [request.ToolId],
                requiresHumanGate: governance.RequiresHumanGate || template.Permission.RequiresHumanGate,
                reason: "RuntimeComposition materialized model tool request.");
            var sideEffect = new SideEffectProfile(stage.SideEffectLevel, requiresAudit: true);
            steps.Add(new ToolInvocationStep(
                $"{template.StepId}-request-{iteration:000}-{index + 1:000}-{SanitizeStepSegment(request.ToolId)}",
                template.SourceIntentId,
                template.SourceGraphId,
                template.SourceStageId,
                template.SourceKernelOperationId,
                request.ToolId,
                new ToolInvocationEnvelope(
                    new CallId(request.CallId),
                    request.ToolId,
                    request.Operation,
                    request.Input,
                    permission,
                    sideEffect,
                    new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                    {
                        ["toolDispatchMode"] = StructuredValue.FromString("model_tool_request"),
                        ["source"] = StructuredValue.FromString("model-reason.toolRequests"),
                    })),
                permission,
                sideEffect,
                template.Budget,
                template.ExpectedOutputContract,
                template.TracePolicy,
                new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                {
                    ["toolDispatchMode"] = StructuredValue.FromString("model_tool_request"),
                    ["modelToolCallId"] = StructuredValue.FromString(request.CallId),
                })));
        }

        if (steps.Count == 0)
        {
            steps.Add(CreateToolExecDiagnosticStep(template, stage, iteration));
        }

        precomputedToolStepResults = precomputedResults;
        return new ExecutionPlan(
            $"{approvedPlan.PlanId}-stage-{iteration:000}-{stage.StageId.Value}",
            approvedPlan.SourceGraphId,
            approvedPlan.SourceIntentId,
            steps,
            approvedPlan.Policy,
            approvedPlan.TracePolicy,
            approvedPlan.Metadata);
    }

    private static ModuleCapabilityStep CreateSubAgentModuleStep(
        ToolInvocationStep template,
        StageNode stage,
        ModelToolRequest request,
        CoreIntent intent,
        GovernanceEnvelope governance,
        SubAgentLineage parentLineage,
        SubAgentLineage childLineage,
        SubAgentSpawnQuota quota,
        int iteration,
        int index)
    {
        var permission = new PermissionEnvelope(
            [SubAgentModuleId],
            requiresHumanGate: governance.RequiresHumanGate || template.Permission.RequiresHumanGate,
            reason: "RuntimeComposition materialized model sub-agent spawn request.");
        var sideEffect = new SideEffectProfile(SideEffectLevel.HostMutation, ["subagent", "kernel-run"], reversible: false, requiresAudit: true);
        return new ModuleCapabilityStep(
            $"{template.StepId}-request-{iteration:000}-{index + 1:000}-{SanitizeStepSegment(SpawnAgentToolId)}",
            template.SourceIntentId,
            template.SourceGraphId,
            template.SourceStageId,
            template.SourceKernelOperationId,
            SubAgentModuleId,
            SubAgentCapabilityId,
            CreateSubAgentSpawnRequestInput(request, intent, governance, parentLineage, template.Budget),
            permission,
            sideEffect,
            template.Budget,
            new ContractRef("sub_agent.spawn.output", "v1"),
            template.TracePolicy,
            new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["toolDispatchMode"] = StructuredValue.FromString("model_tool_request"),
                ["modelToolCallId"] = StructuredValue.FromString(request.CallId),
                ["subAgent.childLineage"] = CreateLineageValue(childLineage),
                ["subAgent.quota"] = StructuredValue.FromPlainObject(CreateQuotaPlainObject(quota)),
            }));
    }

    private static DiagnosticStep CreateToolExecDiagnosticStep(
        ToolInvocationStep template,
        StageNode stage,
        int iteration)
        => new(
            $"{template.StepId}-request-{iteration:000}-diagnostic",
            template.SourceIntentId,
            template.SourceGraphId,
            template.SourceStageId,
            template.SourceKernelOperationId,
            "subagent.admission.precomputed",
            StructuredValue.FromPlainObject(new Dictionary<string, object?>
            {
                ["stageId"] = stage.StageId.Value,
                ["reason"] = "tool-exec contains only composition-level precomputed tool results",
            }),
            new PermissionEnvelope(["runtime.diagnostic"], requiresHumanGate: false, reason: "Composition diagnostic placeholder."),
            new SideEffectProfile(SideEffectLevel.ReadOnly, ["diagnostics"], reversible: true, requiresAudit: true),
            template.Budget,
            new ContractRef("runtime.diagnostic", "v1"),
            template.TracePolicy);

    private static RuntimeStepResult CreateBlockedSubAgentToolResult(
        ToolInvocationStep template,
        ModelToolRequest request,
        SubAgentSpawnDecision decision,
        int iteration,
        int index)
    {
        var failure = new ExecutionFailure(
            decision.FailureCode ?? "subagent.spawn_denied",
            decision.FailureMessage ?? "Sub-agent spawn was denied by structural admission.");
        return new RuntimeStepResult(
            $"{template.StepId}-request-{iteration:000}-{index + 1:000}-{SanitizeStepSegment(SpawnAgentToolId)}-admission",
            RuntimeStepKind.Diagnostic,
            RuntimeStepResultStatus.Succeeded,
            StructuredValue.FromPlainObject(new Dictionary<string, object?>
            {
                ["runtimeBoundary"] = "runtime.composition.subagent_admission",
                ["signals"] = new[] { "tool.results.materialized", "tool.result.blocked", "subagent.spawn.blocked" },
                ["toolResults"] = new object?[]
                {
                    new Dictionary<string, object?>
                    {
                        ["callId"] = request.CallId,
                        ["toolId"] = SpawnAgentToolId,
                        ["status"] = "blocked",
                        ["output"] = new Dictionary<string, object?>
                        {
                            ["resultText"] = null,
                            ["childRunId"] = null,
                            ["childThreadId"] = null,
                            ["replaySummaryRef"] = null,
                            ["diagnosticsRefs"] = Array.Empty<string>(),
                        },
                        ["failure"] = new Dictionary<string, object?>
                        {
                            ["code"] = failure.Code,
                            ["message"] = failure.Message,
                            ["isRetryable"] = failure.IsRetryable,
                        },
                    },
                },
            }),
            diagnosticsRef: $"diagnostics://runtime-composition/subagent/{request.CallId}/{failure.Code}",
            traceRef: $"trace://runtime-composition/subagent/{request.CallId}/{failure.Code}");
    }

    private static StructuredValue CreateSubAgentSpawnRequestInput(
        ModelToolRequest request,
        CoreIntent intent,
        GovernanceEnvelope governance,
        SubAgentLineage parentLineage,
        KernelBudget budget)
        => StructuredValue.FromPlainObject(new Dictionary<string, object?>
        {
            ["spawnCallId"] = request.CallId,
            ["parentLineage"] = CreateLineagePlainObject(parentLineage),
            ["parentSubject"] = CreateSubjectPlainObject(intent.Subject),
            ["taskBrief"] = ResolveSubAgentTaskBrief(request.Input),
            ["evidenceRefs"] = ResolveSubAgentEvidenceRefs(request.Input),
            ["requestedGovernance"] = CreateGovernancePlainObject(governance),
            ["requestedBudget"] = CreateBudgetPlainObject(budget),
            ["requiresHumanGate"] = ReadOptionalBool(request.Input, "requiresHumanGate") ?? false,
            ["metadata"] = new Dictionary<string, object?>
            {
                ["toolInput"] = request.Input,
            },
        });

    private static StructuredValue CreateLineageValue(SubAgentLineage lineage)
        => StructuredValue.FromPlainObject(CreateLineagePlainObject(lineage));

    private static Dictionary<string, object?> CreateLineagePlainObject(SubAgentLineage lineage)
        => new(StringComparer.Ordinal)
        {
            ["rootRunId"] = lineage.RootRunId,
            ["currentRunId"] = lineage.CurrentRunId,
            ["parentRunId"] = lineage.ParentRunId,
            ["depth"] = lineage.Depth,
            ["siblingIndex"] = lineage.SiblingIndex,
            ["ledgerRef"] = lineage.LedgerRef,
        };

    private static Dictionary<string, object?> CreateQuotaPlainObject(SubAgentSpawnQuota quota)
        => new(StringComparer.Ordinal)
        {
            ["maxSpawnDepth"] = quota.MaxSpawnDepth,
            ["maxFanoutPerAgent"] = quota.MaxFanoutPerAgent,
            ["maxTreeNodes"] = quota.MaxTreeNodes,
            ["maxConcurrentAgents"] = quota.MaxConcurrentAgents,
        };

    private static Dictionary<string, object?> CreateSubjectPlainObject(KernelSubjectRef subject)
        => new(StringComparer.Ordinal)
        {
            ["sessionId"] = subject.SessionId.Value,
            ["threadId"] = subject.ThreadId.Value,
            ["workflowId"] = subject.WorkflowId?.Value,
            ["turnId"] = subject.TurnId?.Value,
        };

    private static Dictionary<string, object?> CreateGovernancePlainObject(GovernanceEnvelope governance)
        => new(StringComparer.Ordinal)
        {
            ["envelopeId"] = governance.EnvelopeId,
            ["policyIds"] = governance.PolicyIds,
            ["allowedToolIds"] = governance.AllowedToolIds,
            ["allowedModuleIds"] = governance.AllowedModuleIds,
            ["maxSideEffectLevel"] = governance.MaxSideEffectLevel.ToString(),
            ["requiresHumanGate"] = governance.RequiresHumanGate,
            ["approvalIds"] = governance.ApprovalIds.Select(static approval => approval.Value).ToArray(),
            ["auditRecordIds"] = governance.AuditRecordIds.Select(static audit => audit.Value).ToArray(),
        };

    private static Dictionary<string, object?> CreateBudgetPlainObject(KernelBudget budget)
        => new(StringComparer.Ordinal)
        {
            ["tokenBudget"] = budget.TokenBudget,
            ["timeBudgetMs"] = budget.TimeBudgetMs,
            ["costBudget"] = budget.CostBudget,
            ["retryBudget"] = budget.RetryBudget,
            ["toolCallBudget"] = budget.ToolCallBudget,
        };

    private static string ResolveSubAgentTaskBrief(StructuredValue input)
    {
        var taskBrief = ReadOptionalString(input, "taskBrief")
                        ?? ReadOptionalString(input, "task")
                        ?? ReadOptionalString(input, "prompt")
                        ?? input.GetString();
        return string.IsNullOrWhiteSpace(taskBrief)
            ? "Run the requested sub-agent task using the provided structured input."
            : taskBrief;
    }

    private static IReadOnlyList<string> ResolveSubAgentEvidenceRefs(StructuredValue input)
    {
        if (!input.TryGetProperty("evidenceRefs", out var evidenceRefs) || evidenceRefs is null || evidenceRefs.Kind != StructuredValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return evidenceRefs.Items
            .Select(static item => item.GetString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .ToArray();
    }

    private static bool TryReadToolRequests(
        ExecutionRunResult runtimeResult,
        StageNode toolStage,
        GovernanceEnvelope governance,
        out IReadOnlyList<ModelToolRequest> requests,
        out string failureCode,
        out string failureMessage)
    {
        var output = runtimeResult.StepResults
            .Select(static step => step.Output)
            .FirstOrDefault(static output => output is not null && output.TryGetProperty("toolRequests", out _));
        if (output is null || !output.TryGetProperty("toolRequests", out var toolRequestsValue))
        {
            requests = Array.Empty<ModelToolRequest>();
            failureCode = "runtime.reactive.tool_requests_missing";
            failureMessage = "模型声明存在工具请求，但输出缺少 toolRequests[]。";
            return false;
        }

        if (toolRequestsValue is null || toolRequestsValue.Kind != StructuredValueKind.Array || toolRequestsValue.Items.Count == 0)
        {
            requests = Array.Empty<ModelToolRequest>();
            failureCode = "runtime.reactive.tool_requests_missing";
            failureMessage = "模型声明存在工具请求，但 toolRequests[] 为空或不是数组。";
            return false;
        }

        var parsedRequests = new List<ModelToolRequest>(toolRequestsValue.Items.Count);
        var seenCallIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < toolRequestsValue.Items.Count; index++)
        {
            var item = toolRequestsValue.Items[index];
            if (item.Kind != StructuredValueKind.Object)
            {
                requests = Array.Empty<ModelToolRequest>();
                failureCode = "runtime.reactive.tool_request_invalid";
                failureMessage = $"toolRequests[{index}] 必须是对象。";
                return false;
            }

            var callId = ReadOptionalString(item, "callId");
            if (string.IsNullOrWhiteSpace(callId))
            {
                callId = $"call-{runtimeResult.ExecutionId.Value}-tool-{index + 1:000}";
            }

            if (!seenCallIds.Add(callId))
            {
                requests = Array.Empty<ModelToolRequest>();
                failureCode = "runtime.reactive.tool_request_duplicate_call_id";
                failureMessage = $"toolRequests[] 存在重复 callId：{callId}。";
                return false;
            }

            var toolId = ReadOptionalString(item, "toolId");
            var operation = ReadOptionalString(item, "operation");
            if (string.IsNullOrWhiteSpace(toolId) || string.IsNullOrWhiteSpace(operation))
            {
                requests = Array.Empty<ModelToolRequest>();
                failureCode = "runtime.reactive.tool_request_invalid";
                failureMessage = $"toolRequests[{index}] 缺少 toolId 或 operation。";
                return false;
            }

            if (!IsToolAllowed(toolId, toolStage, governance))
            {
                requests = Array.Empty<ModelToolRequest>();
                failureCode = "runtime.reactive.tool_request_not_allowed";
                failureMessage = $"模型请求的工具不在当前 tool-exec allow-list 或治理边界内：{toolId}。";
                return false;
            }

            var input = item.TryGetProperty("input", out var inputValue) && inputValue is not null
                ? inputValue
                : StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal));
            parsedRequests.Add(new ModelToolRequest(callId, toolId, operation, input));
        }

        requests = parsedRequests;
        failureCode = string.Empty;
        failureMessage = string.Empty;
        return true;
    }

    private static bool TryReadToolResults(
        ExecutionRunResult runtimeResult,
        IReadOnlyList<ModelToolRequest> pendingRequests,
        out IReadOnlyList<ModelToolResult> results,
        out string failureCode,
        out string failureMessage)
    {
        var pendingByCallId = pendingRequests.ToDictionary(static request => request.CallId, StringComparer.Ordinal);
        var resultItems = runtimeResult.StepResults
            .Select(static step => step.Output)
            .Where(static output => output is not null && output.TryGetProperty("toolResults", out _))
            .SelectMany(static output => output!.GetProperty("toolResults").Items)
            .ToArray();
        if (resultItems.Length == 0)
        {
            results = Array.Empty<ModelToolResult>();
            failureCode = "runtime.reactive.tool_results_missing";
            failureMessage = "工具执行声明结果已物化，但输出缺少 toolResults[]。";
            return false;
        }

        var parsedResults = new List<ModelToolResult>(resultItems.Length);
        var seenCallIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < resultItems.Length; index++)
        {
            var item = resultItems[index];
            if (item.Kind != StructuredValueKind.Object)
            {
                results = Array.Empty<ModelToolResult>();
                failureCode = "runtime.reactive.tool_result_invalid";
                failureMessage = $"toolResults[{index}] 必须是对象。";
                return false;
            }

            var callId = ReadOptionalString(item, "callId");
            var toolId = ReadOptionalString(item, "toolId");
            var status = ReadOptionalString(item, "status");
            if (string.IsNullOrWhiteSpace(callId) || string.IsNullOrWhiteSpace(toolId) || string.IsNullOrWhiteSpace(status))
            {
                results = Array.Empty<ModelToolResult>();
                failureCode = "runtime.reactive.tool_result_invalid";
                failureMessage = $"toolResults[{index}] 缺少 callId、toolId 或 status。";
                return false;
            }

            if (!seenCallIds.Add(callId))
            {
                results = Array.Empty<ModelToolResult>();
                failureCode = "runtime.reactive.tool_result_duplicate_call_id";
                failureMessage = $"toolResults[] 存在重复 callId：{callId}。";
                return false;
            }

            if (!pendingByCallId.TryGetValue(callId, out var pendingRequest))
            {
                results = Array.Empty<ModelToolResult>();
                failureCode = "runtime.reactive.tool_result_unmatched";
                failureMessage = $"工具结果无法匹配上一轮工具请求：{callId}。";
                return false;
            }

            if (!string.Equals(toolId, pendingRequest.ToolId, StringComparison.Ordinal))
            {
                results = Array.Empty<ModelToolResult>();
                failureCode = "runtime.reactive.tool_result_tool_mismatch";
                failureMessage = $"工具结果的 toolId 与请求不一致：{callId}。";
                return false;
            }

            if (!IsAllowedToolResultStatus(status))
            {
                results = Array.Empty<ModelToolResult>();
                failureCode = "runtime.reactive.tool_result_status_invalid";
                failureMessage = $"工具结果状态非法：{status}。";
                return false;
            }

            var output = item.TryGetProperty("output", out var outputValue) && outputValue is not null
                ? outputValue
                : StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal));
            var failure = item.TryGetProperty("failure", out var failureValue) && failureValue is not null
                ? failureValue
                : StructuredValue.Null;
            parsedResults.Add(new ModelToolResult(callId, toolId, pendingRequest.Input, status, output, failure));
        }

        var missingRequest = pendingRequests.FirstOrDefault(request => !seenCallIds.Contains(request.CallId));
        if (missingRequest is not null)
        {
            results = Array.Empty<ModelToolResult>();
            failureCode = "runtime.reactive.tool_result_missing_request";
            failureMessage = $"工具结果缺少上一轮请求对应的 callId：{missingRequest.CallId}。";
            return false;
        }

        results = parsedResults;
        failureCode = string.Empty;
        failureMessage = string.Empty;
        return true;
    }

    private static bool IsAllowedToolResultStatus(string status)
        => string.Equals(status, "succeeded", StringComparison.Ordinal)
            || string.Equals(status, "failed", StringComparison.Ordinal)
            || string.Equals(status, "blocked", StringComparison.Ordinal)
            || string.Equals(status, "cancelled", StringComparison.Ordinal)
            || string.Equals(status, "approval-required", StringComparison.Ordinal)
            || string.Equals(status, "timeout", StringComparison.Ordinal);

    private static bool StageResultHasSignal(StageResult result, string signal)
    {
        var signals = new HashSet<string>(StringComparer.Ordinal);
        AddSignals(result.Output, signals);
        foreach (var metadataValue in result.Metadata.Entries.Values)
        {
            AddSignals(metadataValue, signals);
        }

        return signals.Contains(signal);
    }

    private static string? ReadOptionalString(StructuredValue value, string propertyName)
        => value.TryGetProperty(propertyName, out var propertyValue)
            && propertyValue is not null
            && propertyValue.Kind is StructuredValueKind.String or StructuredValueKind.Number
            ? propertyValue.GetString()
            : null;

    private static bool? ReadOptionalBool(StructuredValue value, string propertyName)
        => value.TryGetProperty(propertyName, out var propertyValue)
           && propertyValue is not null
           && propertyValue.Kind == StructuredValueKind.Boolean
            ? propertyValue.BooleanValue
            : null;

    private static bool IsToolAllowed(string toolId, StageNode stage, GovernanceEnvelope governance)
        => stage.AllowedCapabilityToolIds.Contains(toolId, StringComparer.Ordinal)
            && governance.AllowedToolIds.Contains(toolId, StringComparer.Ordinal)
            && (!string.Equals(toolId, SpawnAgentToolId, StringComparison.Ordinal)
                || governance.AllowedModuleIds.Contains(SubAgentModuleId, StringComparer.Ordinal));

    private static SubAgentLineage CreateRootSubAgentLineage(KernelRunId runId)
        => new(
            runId.Value,
            runId.Value,
            parentRunId: null,
            depth: 0,
            siblingIndex: 0,
            $"ledger-{runId.Value}");

    private static string SanitizeStepSegment(string value)
        => string.Concat(value.Select(static ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-'));

    private static StageResult ProjectStageResult(
        StageNode stage,
        ExecutionRunResult runtimeResult,
        StageGraph graph,
        int toolReturnCount)
    {
        var runtimeStatus = runtimeResult.Status;
        if (runtimeStatus == RuntimeStepResultStatus.Blocked)
        {
            return CreateStageResult(stage, StageResultStatus.Blocked, runtimeStatus, runtimeResult, ["stage.blocked"]);
        }

        if (runtimeStatus is RuntimeStepResultStatus.Failed or RuntimeStepResultStatus.Cancelled or RuntimeStepResultStatus.Unspecified)
        {
            var failureSignals = stage.Kind == "model-reason"
                ? new[] { "budget.exhausted", "reason_budget_exhausted", "model_failed", "model.failed", "provider.retry_exhausted" }
                : [$"{stage.Kind}.failed"];
            return CreateStageResult(stage, StageResultStatus.Failed, runtimeStatus, runtimeResult, failureSignals);
        }

        return stage.Kind switch
        {
            "prepare-context" => CreateStageResult(stage, StageResultStatus.Succeeded, runtimeStatus, runtimeResult, ["context.prepared"]),
            "model-reason" => ProjectModelStageResult(stage, runtimeResult, graph, toolReturnCount),
            "tool-exec" => CreateStageResult(stage, StageResultStatus.Succeeded, runtimeStatus, runtimeResult, ["tool.results.materialized"]),
            "finalize" => CreateStageResult(stage, StageResultStatus.Succeeded, runtimeStatus, runtimeResult, ["turn.finalized"]),
            _ => CreateStageResult(stage, StageResultStatus.Succeeded, runtimeStatus, runtimeResult, [$"{stage.Kind}.succeeded"]),
        };
    }

    private static StageResult ProjectModelStageResult(
        StageNode stage,
        ExecutionRunResult runtimeResult,
        StageGraph graph,
        int toolReturnCount)
    {
        var modelSignals = ResolveRuntimeSignals(runtimeResult);
        if (modelSignals.Contains("tool_requests_available") || modelSignals.Contains("tool.requests.available"))
        {
            if (toolReturnCount >= graph.Budgets.ToolCallBudget)
            {
                return CreateStageResult(stage, StageResultStatus.Failed, runtimeResult.Status, runtimeResult, ["budget.exhausted", "reason_budget_exhausted"]);
            }

            return CreateStageResult(stage, StageResultStatus.Succeeded, runtimeResult.Status, runtimeResult, ["tool.requests.available", "model.tool_requests", "model.responded"]);
        }

        if (modelSignals.Contains("model_failed"))
        {
            return CreateStageResult(stage, StageResultStatus.Failed, runtimeResult.Status, runtimeResult, ["budget.exhausted", "model.failed"]);
        }

        return CreateStageResult(stage, StageResultStatus.Succeeded, runtimeResult.Status, runtimeResult, ["model.final_response", "model.responded"]);
    }

    private static StageResult CreateStageResult(
        StageNode stage,
        StageResultStatus status,
        RuntimeStepResultStatus runtimeStatus,
        ExecutionRunResult runtimeResult,
        IReadOnlyList<string> signals)
        => new(
            stage.StageId,
            status,
            runtimeStatus,
            StructuredValue.FromPlainObject(new Dictionary<string, object?>
            {
                ["signals"] = signals,
            }),
            runtimeResult.DiagnosticsRef,
            runtimeResult.TraceRef,
            new MetadataBag(new Dictionary<string, StructuredValue>
            {
                ["signals"] = StructuredValue.FromPlainObject(signals),
            }));

    private static IReadOnlySet<string> ResolveRuntimeSignals(ExecutionRunResult runtimeResult)
    {
        var signals = new HashSet<string>(StringComparer.Ordinal);
        foreach (var output in runtimeResult.StepResults.Select(step => step.Output).Where(output => output is not null))
        {
            AddSignals(output, signals);
        }

        return signals;
    }

    private static void AddSignals(StructuredValue? value, ISet<string> signals)
    {
        if (value is null)
        {
            return;
        }

        if (value.Kind == StructuredValueKind.String && !string.IsNullOrWhiteSpace(value.StringValue))
        {
            signals.Add(value.StringValue);
            return;
        }

        if (value.Kind == StructuredValueKind.Array)
        {
            foreach (var item in value.Items)
            {
                AddSignals(item, signals);
            }

            return;
        }

        if (value.Kind == StructuredValueKind.Object && value.TryGetProperty("signals", out var nestedSignals))
        {
            AddSignals(nestedSignals, signals);
        }
    }

    private sealed record ModelToolRequest(
        string CallId,
        string ToolId,
        string Operation,
        StructuredValue Input);

    private sealed record ModelToolResult(
        string CallId,
        string ToolId,
        StructuredValue Arguments,
        string Status,
        StructuredValue Output,
        StructuredValue Failure)
    {
        public StructuredValue ToProviderInputValue()
            => StructuredValue.FromPlainObject(new Dictionary<string, object?>
            {
                ["callId"] = CallId,
                ["toolId"] = ToolId,
                ["status"] = Status,
                ["output"] = Output,
                ["failure"] = Failure,
            });
    }

    private static RuntimeStepResultStatus MergeStatus(RuntimeStepResultStatus current, RuntimeStepResultStatus next)
    {
        if (current == RuntimeStepResultStatus.Failed || next == RuntimeStepResultStatus.Failed)
        {
            return RuntimeStepResultStatus.Failed;
        }

        if (current == RuntimeStepResultStatus.Blocked || next == RuntimeStepResultStatus.Blocked)
        {
            return RuntimeStepResultStatus.Blocked;
        }

        if (current == RuntimeStepResultStatus.Cancelled || next == RuntimeStepResultStatus.Cancelled)
        {
            return RuntimeStepResultStatus.Cancelled;
        }

        return next == RuntimeStepResultStatus.Unspecified ? current : next;
    }

    private static RuntimeStepResultStatus ResolveMergedRunStatus(IReadOnlyList<RuntimeStepResult> stepResults)
        => stepResults.Count == 0
            ? RuntimeStepResultStatus.Unspecified
            : stepResults.Select(static step => step.Status).Aggregate(RuntimeStepResultStatus.Succeeded, MergeStatus);

    private static KernelRuntimeExecutionResult CreateRuntimeFailureResult(
        KernelRunResult kernelResult,
        ExecutionId executionId,
        string failureCode,
        string message)
    {
        var runtimeResult = new ExecutionRunResult(
            kernelResult.ExecutionPlan?.PlanId ?? $"plan-{kernelResult.RunId.Value}-runtime-failure",
            executionId,
            RuntimeStepResultStatus.Failed,
            [
                new RuntimeStepResult(
                    $"step-{failureCode}",
                    RuntimeStepKind.Diagnostic,
                    RuntimeStepResultStatus.Failed,
                    failure: new ExecutionFailure(failureCode, message)),
            ]);
        return new KernelRuntimeExecutionResult(
            kernelResult,
            runtimeResult,
            KernelRuntimeExecutionDisposition.RuntimeFailed,
            kernelResult.TraceId,
            runtimeResult.TraceRef,
            runtimeResult.DiagnosticsRef);
    }

    private static KernelRunOptions NormalizeKernelOptions(KernelRunOptions options, KernelRunId runtimeKernelRunId)
        => options.RunId is null || string.IsNullOrWhiteSpace(options.RunId.Value)
            ? new KernelRunOptions(
                runtimeKernelRunId,
                options.PreferredGraphId,
                options.PreferredStrategyId,
                options.BudgetOverride,
                options.EnableAdaptive,
                options.RequireHumanGate,
                options.Metadata)
            : options;

    private static ExecutionRuntimeContext AlignRuntimeContext(ExecutionRuntimeContext context, KernelRunId kernelRunId)
    {
        if (string.Equals(context.KernelRunId.Value, kernelRunId.Value, StringComparison.Ordinal))
        {
            return context;
        }

        return new ExecutionRuntimeContext(
            context.ExecutionId,
            kernelRunId,
            context.Governance,
            context.WorkingDirectory,
            context.Metadata);
    }
}
