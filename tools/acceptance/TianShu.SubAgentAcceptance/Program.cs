using System.Runtime.CompilerServices;
using System.Text.Json;
using TianShu.Contracts.Agents;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Modules;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Provider;
using TianShu.Contracts.Tools;
using TianShu.Execution.Runtime;
using TianShu.Kernel;
using TianShu.Kernel.Abstractions;
using TianShu.Provider.Abstractions;
using TianShu.RuntimeComposition;
using TianShu.SubAgent;

var options = SubAgentAcceptanceOptions.Parse(args);
Directory.CreateDirectory(options.WorkingDirectory);

await using var childRuntime = CreateRuntime(new DeterministicAcceptanceProvider("child"));
var childLoop = new AdaptiveRuntimeExecutionLoop(new StableKernelCore(), childRuntime);
var subAgentModule = new SubAgentOrchestrationModule(childLoop);
var recordingSubAgentModule = new RecordingSubAgentModule(subAgentModule);
await using var parentRuntime = CreateRuntime(
    new DeterministicAcceptanceProvider("parent"),
    new Dictionary<string, ISubAgentModule>(StringComparer.Ordinal)
    {
        ["module.sub_agent"] = recordingSubAgentModule,
    });
var parentLoop = new AdaptiveRuntimeExecutionLoop(
    new StableKernelCore(),
    parentRuntime,
    subAgentSpawnQuota: new SubAgentSpawnQuota(
        maxSpawnDepth: 1,
        maxFanoutPerAgent: 4,
        maxTreeNodes: 8,
        maxConcurrentAgents: 2));

var result = await parentLoop.RunReactiveAsync(
        CreateParentIntent(options.WorkingDirectory),
        CreateExecutionOptions(options.WorkingDirectory),
        CancellationToken.None)
    .ConfigureAwait(false);
var replay = KernelRuntimeReplayProjector.Build(result);
var stepOutputs = result.RuntimeResult?.StepResults
    .Select(static step => new SubAgentAcceptanceStepEvidence(
        step.StepId,
        step.StepKind.ToString(),
        step.Status.ToString(),
        step.Output is null ? null : JsonSerializer.Serialize(step.Output, JsonOptions),
        step.Failure?.Code,
        step.TraceRef,
        step.DiagnosticsRef))
    .ToArray()
    ?? [];
var subAgentBridgeStep = result.RuntimeResult?.StepResults.FirstOrDefault(static step =>
    step.StepKind == RuntimeStepKind.ModuleCapability
    && step.Output is not null
    && ReadString(step.Output, "runtimeBoundary") == "execution.runtime.subagent_module_bridge");
var subAgentToolResult = subAgentBridgeStep?.Output is null
    ? null
    : JsonSerializer.Serialize(subAgentBridgeStep.Output, JsonOptions);
var secondModelReceivedToolResult = result.RuntimeResult?.StepResults.Any(static step =>
    step.StepKind == RuntimeStepKind.ModelInvocation
    && step.Output is not null
    && ReadString(step.Output, "assistantText")?.Contains("父模型已收到多 Agent fan-in 结果", StringComparison.Ordinal) == true) == true;
var multiAgentCase = AnalyzeMultiAgentCase(stepOutputs, recordingSubAgentModule.Records);

var evidence = new SubAgentAcceptanceEvidence(
    Success: result.Disposition == KernelRuntimeExecutionDisposition.RuntimeCompleted
             && replay.Steps.Any(static step => step.StepKind == RuntimeStepKind.ModuleCapability)
             && subAgentBridgeStep is not null
             && secondModelReceivedToolResult
             && multiAgentCase.MultiAgentFinalCaseObserved,
    AcceptanceKind: "deterministic-subagent-mechanism",
    Disposition: result.Disposition.ToString(),
    StagePath: replay.StagePath,
    ModuleCapabilityStepObserved: replay.Steps.Any(static step => step.StepKind == RuntimeStepKind.ModuleCapability),
    SubAgentBridgeObserved: subAgentBridgeStep is not null,
    ParentSecondModelReceivedToolResult: secondModelReceivedToolResult,
    MultiAgentFinalCaseObserved: multiAgentCase.MultiAgentFinalCaseObserved,
    ParallelFanoutObserved: multiAgentCase.ParallelFanoutObserved,
    SubtreeGovernanceObserved: multiAgentCase.SubtreeGovernanceObserved,
    BudgetSplitObserved: multiAgentCase.BudgetSplitObserved,
    FanInObserved: multiAgentCase.FanInObserved,
    FailureIsolationObserved: multiAgentCase.FailureIsolationObserved,
    WholeTreeDiagnosticsObserved: multiAgentCase.WholeTreeDiagnosticsObserved,
    PlannedSubTaskCount: multiAgentCase.PlannedSubTaskCount,
    MaxConcurrentAgents: multiAgentCase.MaxConcurrentAgents,
    CompletedChildCount: multiAgentCase.CompletedChildCount,
    FailedChildCount: multiAgentCase.FailedChildCount,
    TreeNodeCount: multiAgentCase.TreeNodeCount,
    TreeEdgeCount: multiAgentCase.TreeEdgeCount,
    SubAgentGovernanceRecords: recordingSubAgentModule.Records,
    SubAgentToolResultJson: subAgentToolResult,
    Replay: replay,
    StepOutputs: stepOutputs);

if (!string.IsNullOrWhiteSpace(options.OutputPath))
{
    var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(options.OutputPath));
    if (!string.IsNullOrWhiteSpace(outputDirectory))
    {
        Directory.CreateDirectory(outputDirectory);
    }

    await File.WriteAllTextAsync(options.OutputPath, JsonSerializer.Serialize(evidence, JsonOptions) + Environment.NewLine)
        .ConfigureAwait(false);
}

Console.WriteLine(JsonSerializer.Serialize(evidence, JsonOptions));
return evidence.Success ? 0 : 1;

static TianShuExecutionRuntime CreateRuntime(
    IProviderModule provider,
    IReadOnlyDictionary<string, ISubAgentModule>? subAgentModules = null)
    => new(
        new ExecutionRuntimeStepBindingRegistry(
            providers: new Dictionary<string, IProviderModule>(StringComparer.Ordinal)
            {
                ["provider.default"] = provider,
            },
            tools: new Dictionary<string, ITianShuTool>(StringComparer.Ordinal),
            subAgentModules: subAgentModules));

static TurnIntent CreateParentIntent(string workingDirectory)
{
    var governance = new GovernanceEnvelope(
        "governance-subagent-acceptance",
        allowedToolIds:
        [
            "kernel.request_capability_call",
            "update_context_policy",
            "request_capability_call",
            "spawn_agent",
            "read_file",
        ],
        allowedModuleIds:
        [
            "kernel.default",
            "provider.default",
            "module.sub_agent",
        ],
        maxSideEffectLevel: SideEffectLevel.HostMutation,
        requiresHumanGate: false);

    return new TurnIntent(
        new CoreIntentId("intent-subagent-acceptance"),
        new KernelSubjectRef(
            new SessionId("session-subagent-acceptance"),
            new ThreadId("thread-subagent-acceptance"),
            new WorkflowId("workflow-subagent-acceptance"),
            new TurnId("turn-subagent-acceptance")),
        governance,
        "acceptance://subagent/autonomous-spawn-mechanism",
        new KernelBudget(tokenBudget: 8000, timeBudgetMs: 60000, retryBudget: 2, toolCallBudget: 2),
        new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["message"] = StructuredValue.FromString(
                "请完成一个需要拆分审计 Codex schema 与当前实现的复杂任务；如果模型认为需要委托独立审计轨道，应通过可用协作能力派生子代理。"),
            ["workingDirectory"] = StructuredValue.FromString(workingDirectory),
        }));
}

static KernelRuntimeExecutionOptions CreateExecutionOptions(string workingDirectory)
{
    var runId = new KernelRunId("run-subagent-acceptance-parent");
    var governance = new GovernanceEnvelope(
        "governance-subagent-acceptance",
        allowedToolIds:
        [
            "kernel.request_capability_call",
            "update_context_policy",
            "request_capability_call",
            "spawn_agent",
            "read_file",
        ],
        allowedModuleIds:
        [
            "kernel.default",
            "provider.default",
            "module.sub_agent",
        ],
        maxSideEffectLevel: SideEffectLevel.HostMutation,
        requiresHumanGate: false);

    return new KernelRuntimeExecutionOptions(
        new KernelRunOptions(runId, enableAdaptive: false, requireHumanGate: false),
        new ExecutionRuntimeContext(
            new ExecutionId("execution-subagent-acceptance-parent"),
            runId,
            governance,
            workingDirectory,
            new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["planId"] = StructuredValue.FromString("plan-subagent-acceptance-parent"),
            })),
        ExecuteRuntimePlan: true);
}

static string? ReadString(StructuredValue? value, string propertyName)
    => value is not null && value.TryGetProperty(propertyName, out var property)
        ? property?.GetString()
        : null;

static MultiAgentCaseEvidence AnalyzeMultiAgentCase(
    IReadOnlyList<SubAgentAcceptanceStepEvidence> stepOutputs,
    IReadOnlyList<SubAgentGovernanceRecord> governanceRecords)
{
    var fanout = FindStepOutput(stepOutputs, "subagent.fanout.diagnostics");
    var fanIn = FindStepOutput(stepOutputs, "subagent.fanin.summary");
    var plannedSubTaskCount = ReadInt(fanout, "plannedSubTaskCount");
    var maxConcurrentAgents = ReadInt(fanout, "maxConcurrentAgents");
    var fanoutJobCount = ReadArrayLength(fanout, "jobs");
    var budgetSplitObserved = fanoutJobCount >= 3 && AllArrayItemsHaveObjectProperty(fanout, "jobs", "allocatedBudget");

    var fanInSummary = ReadObject(fanIn, "subAgentFanInSummary");
    var fanInStatus = ReadJsonString(fanInSummary, "status");
    var fanInResultCount = ReadArrayLength(fanInSummary, "results");
    var completedChildCount = CountResultsWithStatus(fanInSummary, "Completed");
    var failedChildCount = CountResultsWithStatus(fanInSummary, "Failed");
    var failureIsolationObserved = string.Equals(fanInStatus, "CompletedWithFailures", StringComparison.Ordinal)
                                   && completedChildCount >= 2
                                   && failedChildCount >= 1
                                   && ContainsText(fanInSummary, "subagent.acceptance_child_failed");

    var treeDiagnostics = ReadObject(fanIn, "subAgentTreeDiagnostics")
                          ?? ReadObject(fanout, "subAgentTreeDiagnostics");
    var treeNodeCount = ReadArrayLength(treeDiagnostics, "nodes");
    var treeEdgeCount = ReadArrayLength(treeDiagnostics, "edges");
    var wholeTreeDiagnosticsObserved = treeNodeCount >= 4
                                       && treeEdgeCount >= 3
                                       && ContainsText(treeDiagnostics, "sub_agent.tree_diagnostics.v1");

    var subtreeGovernanceObserved = governanceRecords.Count >= 3
                                    && governanceRecords.All(static record => record.ParentAllowedSpawnAgent)
                                    && governanceRecords.All(static record => !record.ChildAllowedSpawnAgent)
                                    && governanceRecords.All(static record => record.ChildSideEffectWithinParent)
                                    && governanceRecords.All(static record => record.ChildBudgetWithinAllocatedBudget);
    var parallelFanoutObserved = plannedSubTaskCount >= 3
                                 && maxConcurrentAgents >= 2
                                 && fanoutJobCount >= 3
                                 && HasOverlappingJobs(fanout);
    var fanInObserved = string.Equals(fanInStatus, "CompletedWithFailures", StringComparison.Ordinal)
                        && fanInResultCount >= 3;

    return new MultiAgentCaseEvidence(
        parallelFanoutObserved
        && subtreeGovernanceObserved
        && budgetSplitObserved
        && fanInObserved
        && failureIsolationObserved
        && wholeTreeDiagnosticsObserved,
        parallelFanoutObserved,
        subtreeGovernanceObserved,
        budgetSplitObserved,
        fanInObserved,
        failureIsolationObserved,
        wholeTreeDiagnosticsObserved,
        plannedSubTaskCount,
        maxConcurrentAgents,
        completedChildCount,
        failedChildCount,
        treeNodeCount,
        treeEdgeCount);
}

static JsonElement? FindStepOutput(IReadOnlyList<SubAgentAcceptanceStepEvidence> stepOutputs, string signal)
{
    foreach (var step in stepOutputs)
    {
        if (string.IsNullOrWhiteSpace(step.OutputJson) || !step.OutputJson.Contains(signal, StringComparison.Ordinal))
        {
            continue;
        }

        using var document = JsonDocument.Parse(step.OutputJson);
        return document.RootElement.Clone();
    }

    return null;
}

static JsonElement? ReadObject(JsonElement? element, string propertyName)
{
    if (element is not { ValueKind: JsonValueKind.Object } value
        || !value.TryGetProperty(propertyName, out var property)
        || property.ValueKind != JsonValueKind.Object)
    {
        return null;
    }

    return property.Clone();
}

static string? ReadJsonString(JsonElement? element, string propertyName)
{
    if (element is not { ValueKind: JsonValueKind.Object } value
        || !value.TryGetProperty(propertyName, out var property))
    {
        return null;
    }

    return property.ValueKind == JsonValueKind.String
        ? property.GetString()
        : property.ToString();
}

static int ReadInt(JsonElement? element, string propertyName)
{
    if (element is not { ValueKind: JsonValueKind.Object } value
        || !value.TryGetProperty(propertyName, out var property))
    {
        return 0;
    }

    return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number)
        ? number
        : int.TryParse(property.ToString(), out var parsed)
            ? parsed
            : 0;
}

static int ReadArrayLength(JsonElement? element, string propertyName)
{
    if (element is not { ValueKind: JsonValueKind.Object } value
        || !value.TryGetProperty(propertyName, out var property)
        || property.ValueKind != JsonValueKind.Array)
    {
        return 0;
    }

    return property.GetArrayLength();
}

static bool AllArrayItemsHaveObjectProperty(JsonElement? element, string arrayPropertyName, string itemPropertyName)
{
    if (element is not { ValueKind: JsonValueKind.Object } value
        || !value.TryGetProperty(arrayPropertyName, out var array)
        || array.ValueKind != JsonValueKind.Array
        || array.GetArrayLength() == 0)
    {
        return false;
    }

    return array.EnumerateArray().All(item =>
        item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty(itemPropertyName, out var property)
        && property.ValueKind == JsonValueKind.Object);
}

static int CountResultsWithStatus(JsonElement? fanInSummary, string status)
{
    if (fanInSummary is not { ValueKind: JsonValueKind.Object } value
        || !value.TryGetProperty("results", out var results)
        || results.ValueKind != JsonValueKind.Array)
    {
        return 0;
    }

    return results.EnumerateArray().Count(item =>
        item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty("status", out var statusValue)
        && string.Equals(statusValue.GetString(), status, StringComparison.Ordinal));
}

static bool ContainsText(JsonElement? element, string text)
    => element is not null
       && element.Value.GetRawText().Contains(text, StringComparison.Ordinal);

static bool HasOverlappingJobs(JsonElement? fanout)
{
    if (fanout is not { ValueKind: JsonValueKind.Object } value
        || !value.TryGetProperty("jobs", out var jobs)
        || jobs.ValueKind != JsonValueKind.Array)
    {
        return false;
    }

    var windows = jobs.EnumerateArray()
        .Select(static item => new
        {
            Started = TryReadDateTimeOffset(item, "startedAt"),
            Completed = TryReadDateTimeOffset(item, "completedAt"),
        })
        .Where(static item => item.Started is not null && item.Completed is not null)
        .ToArray();
    for (var i = 0; i < windows.Length; i++)
    {
        for (var j = i + 1; j < windows.Length; j++)
        {
            if (windows[i].Started < windows[j].Completed && windows[j].Started < windows[i].Completed)
            {
                return true;
            }
        }
    }

    return false;
}

static DateTimeOffset? TryReadDateTimeOffset(JsonElement item, string propertyName)
{
    if (item.ValueKind != JsonValueKind.Object
        || !item.TryGetProperty(propertyName, out var property)
        || property.ValueKind != JsonValueKind.String)
    {
        return null;
    }

    return DateTimeOffset.TryParse(property.GetString(), out var parsed)
        ? parsed
        : null;
}

internal sealed class DeterministicAcceptanceProvider : IProviderModule
{
    private int callCount;
    private readonly string role;

    public DeterministicAcceptanceProvider(string role)
    {
        this.role = role;
    }

    public ProviderDescriptor Descriptor { get; } = new(
        "provider.default",
        "Sub-Agent Acceptance Provider",
        ProviderProtocolKind.Custom,
        new ProviderCapabilityProfile(SupportsStreaming: true, SupportsTools: true),
        [new TianShu.Contracts.Provider.ProviderModelDescriptor("acceptance-deterministic-model", "Acceptance deterministic model")]);

    public async IAsyncEnumerable<ProviderStreamEvent> InvokeAsync(
        ProviderInvocationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        var currentCall = Interlocked.Increment(ref callCount);
        if (string.Equals(role, "child", StringComparison.Ordinal))
        {
            await foreach (var childEvent in InvokeChildAsync(request, cancellationToken).ConfigureAwait(false))
            {
                yield return childEvent;
            }

            yield break;
        }

        if (request.Inputs.OfType<ToolResultProviderInputItem>().Any())
        {
            yield return new ProviderCompletionEvent(new ProviderCompletion(
                "父模型已收到多 Agent fan-in 结果，并据此完成最终汇总。",
                new ProviderUsage(InputTokens: 43, OutputTokens: 17)));
            yield break;
        }

        if (currentCall == 1)
        {
            yield return CreateSpawnDirective(
                "call-subagent-acceptance-001",
                "审计 Codex 官方 config schema 中模型与推理配置域，返回父模型可消费摘要。",
                "acceptance://subagent/config-schema-model");
            yield return CreateSpawnDirective(
                "call-subagent-acceptance-002",
                "审计 Codex 当前实现中的 sandbox 与权限配置读取路径，返回父模型可消费摘要。",
                "acceptance://subagent/current-implementation-sandbox");
            yield return CreateSpawnDirective(
                "call-subagent-acceptance-003",
                "执行确定性失败隔离探针：返回一个受控失败，用于证明父级 fan-in 不被单个失败子项污染。",
                "acceptance://subagent/failure-isolation");
            yield break;
        }

        yield return new ProviderCompletionEvent(new ProviderCompletion(
            "父模型未收到子代理结果就结束，这是验收失败路径。",
            new ProviderUsage(InputTokens: 10, OutputTokens: 10)));
    }

    private async IAsyncEnumerable<ProviderStreamEvent> InvokeChildAsync(
        ProviderInvocationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var prompt = string.Join(
            "\n",
            request.Inputs.OfType<TextProviderInputItem>().Select(static input => input.Text));
        await Task.Delay(150, cancellationToken).ConfigureAwait(false);

        if (prompt.Contains("确定性失败隔离探针", StringComparison.Ordinal))
        {
            yield return new ProviderFailureEvent(new ProviderFailure(
                "subagent.acceptance_child_failed",
                "确定性失败隔离探针触发，子项按预期失败。",
                isRetryable: false));
            yield break;
        }

        yield return new ProviderCompletionEvent(new ProviderCompletion(
            prompt.Contains("sandbox", StringComparison.OrdinalIgnoreCase)
                ? "子代理完成 sandbox 与权限配置读取路径审计，结论：应优先核对实现侧默认值。"
                : "子代理完成 Codex 官方 config schema 模型与推理配置域审计，结论：应优先核对官方 schema。",
            new ProviderUsage(InputTokens: 31, OutputTokens: 19)));
    }

    private static ProviderToolDirectiveEvent CreateSpawnDirective(string callId, string taskBrief, string evidenceRef)
        => new(new ProviderToolDirective(
            new CallId(callId),
            "spawn_agent",
            StructuredValue.FromPlainObject(new Dictionary<string, object?>
            {
                ["operation"] = "spawn",
                ["taskBrief"] = taskBrief,
                ["evidenceRefs"] = new[] { evidenceRef },
                ["requiresHumanGate"] = false,
                ["requestedGovernance"] = new Dictionary<string, object?>
                {
                    ["envelopeId"] = $"governance-request-{callId}",
                    ["allowedToolIds"] = new[] { "read_file", "spawn_agent" },
                    ["allowedModuleIds"] = new[] { "provider.default", "module.sub_agent" },
                    ["maxSideEffectLevel"] = "HostMutation",
                    ["requiresHumanGate"] = false,
                },
            })));
}

internal sealed record SubAgentAcceptanceOptions(string WorkingDirectory, string? OutputPath)
{
    public static SubAgentAcceptanceOptions Parse(IReadOnlyList<string> args)
    {
        var workingDirectory = Directory.GetCurrentDirectory();
        string? outputPath = null;
        for (var i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], "--workdir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                workingDirectory = Path.GetFullPath(args[++i]);
                continue;
            }

            if (string.Equals(args[i], "--output", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                outputPath = Path.GetFullPath(args[++i]);
            }
        }

        return new SubAgentAcceptanceOptions(workingDirectory, outputPath);
    }
}

internal sealed record SubAgentAcceptanceEvidence(
    bool Success,
    string AcceptanceKind,
    string Disposition,
    IReadOnlyList<string> StagePath,
    bool ModuleCapabilityStepObserved,
    bool SubAgentBridgeObserved,
    bool ParentSecondModelReceivedToolResult,
    bool MultiAgentFinalCaseObserved,
    bool ParallelFanoutObserved,
    bool SubtreeGovernanceObserved,
    bool BudgetSplitObserved,
    bool FanInObserved,
    bool FailureIsolationObserved,
    bool WholeTreeDiagnosticsObserved,
    int PlannedSubTaskCount,
    int MaxConcurrentAgents,
    int CompletedChildCount,
    int FailedChildCount,
    int TreeNodeCount,
    int TreeEdgeCount,
    IReadOnlyList<SubAgentGovernanceRecord> SubAgentGovernanceRecords,
    string? SubAgentToolResultJson,
    KernelRuntimeReplaySummary Replay,
    IReadOnlyList<SubAgentAcceptanceStepEvidence> StepOutputs);

internal sealed record SubAgentAcceptanceStepEvidence(
    string StepId,
    string StepKind,
    string Status,
    string? OutputJson,
    string? FailureCode,
    string? TraceRef,
    string? DiagnosticsRef);

internal sealed record MultiAgentCaseEvidence(
    bool MultiAgentFinalCaseObserved,
    bool ParallelFanoutObserved,
    bool SubtreeGovernanceObserved,
    bool BudgetSplitObserved,
    bool FanInObserved,
    bool FailureIsolationObserved,
    bool WholeTreeDiagnosticsObserved,
    int PlannedSubTaskCount,
    int MaxConcurrentAgents,
    int CompletedChildCount,
    int FailedChildCount,
    int TreeNodeCount,
    int TreeEdgeCount);

internal sealed record SubAgentGovernanceRecord(
    string SpawnCallId,
    bool ParentAllowedSpawnAgent,
    bool ChildAllowedSpawnAgent,
    bool ChildSideEffectWithinParent,
    bool ChildBudgetWithinAllocatedBudget,
    string ParentEnvelopeId,
    string RequestedEnvelopeId,
    string ChildEnvelopeId);

internal sealed class RecordingSubAgentModule : ISubAgentModule
{
    private readonly ISubAgentModule inner;
    private readonly List<SubAgentGovernanceRecord> records = [];
    private readonly object syncRoot = new();

    public RecordingSubAgentModule(ISubAgentModule inner)
    {
        this.inner = inner;
    }

    public ModuleDescriptor Descriptor => inner.Descriptor;

    public IReadOnlyList<SubAgentGovernanceRecord> Records
    {
        get
        {
            lock (syncRoot)
            {
                return records.ToArray();
            }
        }
    }

    public ValueTask<ModuleSmokeCheckResult> CheckAsync(CancellationToken cancellationToken)
        => inner.CheckAsync(cancellationToken);

    public async ValueTask<SubAgentRunResult> SpawnAsync(
        SubAgentSpawnRequest request,
        SubAgentLineage childLineage,
        SubAgentSpawnQuota quota,
        SubAgentModuleInvocationContext context,
        CancellationToken cancellationToken)
    {
        var childGovernance = SubAgentGovernanceNarrowing.Narrow(
            context.Governance,
            request.RequestedGovernance,
            childLineage.CurrentRunId,
            request.RequiresHumanGate);
        var record = new SubAgentGovernanceRecord(
            request.SpawnCallId,
            context.Governance.AllowedToolIds.Contains("spawn_agent", StringComparer.Ordinal),
            childGovernance.AllowedToolIds.Contains("spawn_agent", StringComparer.Ordinal),
            childGovernance.MaxSideEffectLevel <= context.Governance.MaxSideEffectLevel,
            request.RequestedBudget.TokenBudget <= ResolveBudgetInt(context.Metadata, "subAgent.allocatedBudget", "tokenBudget")
            && request.RequestedBudget.TimeBudgetMs <= ResolveBudgetLong(context.Metadata, "subAgent.allocatedBudget", "timeBudgetMs")
            && request.RequestedBudget.ToolCallBudget <= ResolveBudgetInt(context.Metadata, "subAgent.allocatedBudget", "toolCallBudget"),
            context.Governance.EnvelopeId,
            request.RequestedGovernance.EnvelopeId,
            childGovernance.EnvelopeId);
        lock (syncRoot)
        {
            records.Add(record);
        }

        return await inner.SpawnAsync(request, childLineage, quota, context, cancellationToken).ConfigureAwait(false);
    }

    private static int ResolveBudgetInt(MetadataBag metadata, string objectKey, string propertyName)
        => metadata.TryGetValue(objectKey, out var value)
           && value.TryGetProperty(propertyName, out var property)
           && property is not null
           && int.TryParse(property.GetString(), out var parsed)
            ? parsed
            : 0;

    private static long ResolveBudgetLong(MetadataBag metadata, string objectKey, string propertyName)
        => metadata.TryGetValue(objectKey, out var value)
           && value.TryGetProperty(propertyName, out var property)
           && property is not null
           && long.TryParse(property.GetString(), out var parsed)
            ? parsed
            : 0;
}

internal static partial class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}
