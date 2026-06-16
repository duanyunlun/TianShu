using System.Runtime.CompilerServices;
using System.Text.Json;
using TianShu.Contracts.Agents;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
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
await using var parentRuntime = CreateRuntime(
    new DeterministicAcceptanceProvider("parent"),
    new Dictionary<string, ISubAgentModule>(StringComparer.Ordinal)
    {
        ["module.sub_agent"] = subAgentModule,
    });
var parentLoop = new AdaptiveRuntimeExecutionLoop(new StableKernelCore(), parentRuntime);

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
    && ReadString(step.Output, "assistantText")?.Contains("父模型已收到子代理结果", StringComparison.Ordinal) == true) == true;

var evidence = new SubAgentAcceptanceEvidence(
    Success: result.Disposition == KernelRuntimeExecutionDisposition.RuntimeCompleted
             && replay.Steps.Any(static step => step.StepKind == RuntimeStepKind.ModuleCapability)
             && subAgentBridgeStep is not null
             && secondModelReceivedToolResult,
    AcceptanceKind: "deterministic-subagent-mechanism",
    Disposition: result.Disposition.ToString(),
    StagePath: replay.StagePath,
    ModuleCapabilityStepObserved: replay.Steps.Any(static step => step.StepKind == RuntimeStepKind.ModuleCapability),
    SubAgentBridgeObserved: subAgentBridgeStep is not null,
    ParentSecondModelReceivedToolResult: secondModelReceivedToolResult,
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
            yield return new ProviderCompletionEvent(new ProviderCompletion(
                "子代理已完成 Codex 配置 schema 与现行实现差异审计，并返回可供父模型使用的摘要。",
                new ProviderUsage(InputTokens: 31, OutputTokens: 19)));
            yield break;
        }

        if (request.Inputs.OfType<ToolResultProviderInputItem>().Any())
        {
            yield return new ProviderCompletionEvent(new ProviderCompletion(
                "父模型已收到子代理结果，并据此完成最终汇总。",
                new ProviderUsage(InputTokens: 43, OutputTokens: 17)));
            yield break;
        }

        if (currentCall == 1)
        {
            yield return new ProviderToolDirectiveEvent(new ProviderToolDirective(
                new CallId("call-subagent-acceptance-001"),
                "spawn_agent",
                StructuredValue.FromPlainObject(new Dictionary<string, object?>
                {
                    ["operation"] = "spawn",
                    ["taskBrief"] = "独立审计 Codex 官方 config schema 与当前实现之间的主要配置域差异，返回父模型可消费摘要。",
                    ["evidenceRefs"] = new[] { "acceptance://subagent/complex-config-audit" },
                    ["requiresHumanGate"] = false,
                })));
            yield break;
        }

        yield return new ProviderCompletionEvent(new ProviderCompletion(
            "父模型未收到子代理结果就结束，这是验收失败路径。",
            new ProviderUsage(InputTokens: 10, OutputTokens: 10)));
    }
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

internal static partial class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}
