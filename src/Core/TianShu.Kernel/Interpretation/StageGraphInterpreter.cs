using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Provider;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;
using TianShu.Kernel.Abstractions;

namespace TianShu.Kernel.Interpretation;

/// <summary>
/// 默认 StageGraph 解释器，将内置 Stage 映射为明确 RuntimeStep，并为未知 Stage 保留模块能力 fallback。
/// Default StageGraph interpreter that maps built-in stages to explicit RuntimeSteps and keeps a module-capability fallback for unknown stages.
/// </summary>
public sealed class StageGraphInterpreter : IStageGraphInterpreter
{
    public Task<ExecutionPlan> InterpretAsync(StageGraph graph, KernelInterpreterContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(context);

        var orderedStages = OrderStages(graph);
        var steps = orderedStages
            .SelectMany(stage => CreateSteps(graph, stage, context))
            .ToArray();

        var plan = new ExecutionPlan(
            $"plan-{graph.GraphId.Value}-{context.State.RunId.Value}",
            graph.GraphId,
            context.Intent.IntentId,
            steps,
            new ExecutionPlanPolicy(sequential: true, stopOnFailure: true, maxParallelism: 1),
            new TracePolicy(enabled: true, requireDiagnosticsRef: true, requireRuntimeTraceRef: true));

        return Task.FromResult(plan);
    }

    public Task<StageTransitionDecision> DecideNextStageAsync(StageGraph graph, StageResult currentStageResult, KernelInterpreterContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(currentStageResult);

        var signals = ResolveSignals(currentStageResult);
        var nextEdge = graph.Edges.FirstOrDefault(edge =>
            edge.FromStageId == currentStageResult.StageId
            && Matches(edge.TransitionKind, currentStageResult.Status)
            && MatchesSignals(edge, signals));

        var decision = nextEdge is null
            ? new StageTransitionDecision(currentStageResult.StageId, StageTransitionKind.Success, shouldStop: true, validationDecision: KernelValidationDecision.Approved)
            : new StageTransitionDecision(currentStageResult.StageId, nextEdge.TransitionKind, nextEdge.ToStageId, validationDecision: KernelValidationDecision.Approved);

        return Task.FromResult(decision);
    }

    private static IReadOnlyList<RuntimeStep> CreateSteps(StageGraph graph, StageNode stage, KernelInterpreterContext context)
        => stage.Kind switch
        {
            "prepare-context" => [CreatePrepareContextStep(graph, stage, context)],
            "model-reason" => [CreateModelReasonStep(graph, stage, context)],
            "tool-exec" => [CreateToolExecStep(graph, stage, context)],
            "finalize" => CreateFinalizeSteps(graph, stage, context),
            "interrupt-host" => [CreateHostInteractionStep(graph, stage, context, "interrupt.cancel_tail_stream")],
            "resume-host" => [CreateHostInteractionStep(graph, stage, context, "resume.from_checkpoint")],
            "memory-retrieve" => [CreateMemoryCapabilityStep(graph, stage, context, "memory.retrieve", requiresHumanGate: false)],
            "memory-form" => [CreateMemoryCapabilityStep(graph, stage, context, "memory.form", requiresHumanGate: true)],
            "memory-supersede" => [CreateMemoryCapabilityStep(graph, stage, context, "memory.supersede", requiresHumanGate: true)],
            _ => [CreateModuleCapabilityFallbackStep(graph, stage, context)],
        };

    private static ModuleCapabilityStep CreatePrepareContextStep(StageGraph graph, StageNode stage, KernelInterpreterContext context)
        => new(
            $"step-{stage.StageId.Value}",
            context.Intent.IntentId,
            graph.GraphId,
            stage.StageId,
            KernelIds.OperationIdFor(stage.StageId),
            "kernel.default",
            "prepare_context",
            CreateStageEnvelope(stage),
            new PermissionEnvelope(
                stage.AllowedKernelToolIds,
                requiresHumanGate: context.Governance.RequiresHumanGate,
                reason: "Stable Kernel Core approved context preparation."),
            new SideEffectProfile(stage.SideEffectLevel, requiresAudit: true),
            stage.Budget,
            stage.OutputContract,
            new TracePolicy(enabled: true, requireDiagnosticsRef: true, requireRuntimeTraceRef: true));

    private static ModelInvocationStep CreateModelReasonStep(StageGraph graph, StageNode stage, KernelInterpreterContext context)
    {
        var candidate = ResolveModelCandidate(stage.ModelRoutePolicy);
        return new ModelInvocationStep(
            $"step-{stage.StageId.Value}",
            context.Intent.IntentId,
            graph.GraphId,
            stage.StageId,
            KernelIds.OperationIdFor(stage.StageId),
            candidate.ProviderModuleId,
            stage.ModelRoutePolicy,
            new ProviderInvocationRequest(
                new ExecutionId($"execution-{context.State.RunId.Value}-{stage.StageId.Value}"),
                candidate.ProviderKey,
                candidate.Model,
                new ProviderConversationContext(context.Intent.Subject.ThreadId, context.Intent.Subject.TurnId),
                [new TextProviderInputItem(stage.Objective)]),
            new PermissionEnvelope(
                stage.AllowedKernelToolIds,
                requiresHumanGate: context.Governance.RequiresHumanGate,
                reason: "Stable Kernel Core approved model invocation."),
            new SideEffectProfile(stage.SideEffectLevel, requiresAudit: true),
            stage.Budget,
            stage.OutputContract,
            new TracePolicy(enabled: true, requireDiagnosticsRef: true, requireRuntimeTraceRef: true));
    }

    private static ToolInvocationStep CreateToolExecStep(StageGraph graph, StageNode stage, KernelInterpreterContext context)
    {
        var capabilityToolId = stage.AllowedCapabilityToolIds.FirstOrDefault() ?? "tool.unavailable";
        var permission = new PermissionEnvelope(
            [capabilityToolId],
            requiresHumanGate: context.Governance.RequiresHumanGate,
            reason: "Stable Kernel Core approved tool execution boundary.");
        var sideEffect = new SideEffectProfile(stage.SideEffectLevel, requiresAudit: true);
        return new ToolInvocationStep(
            $"step-{stage.StageId.Value}",
            context.Intent.IntentId,
            graph.GraphId,
            stage.StageId,
            KernelIds.OperationIdFor(stage.StageId),
            capabilityToolId,
            new ToolInvocationEnvelope(
                new CallId($"call-{stage.StageId.Value}"),
                capabilityToolId,
                "execute_approved_requests",
                CreateStaticToolDispatchEnvelope(stage, capabilityToolId),
                permission,
                sideEffect),
            permission,
            sideEffect,
            stage.Budget,
            stage.OutputContract,
            new TracePolicy(enabled: true, requireDiagnosticsRef: true, requireRuntimeTraceRef: true));
    }

    private static IReadOnlyList<RuntimeStep> CreateFinalizeSteps(StageGraph graph, StageNode stage, KernelInterpreterContext context)
    {
        var permission = new PermissionEnvelope(
            requiresHumanGate: context.Governance.RequiresHumanGate,
            reason: "Stable Kernel Core approved turn finalization.");
        var sideEffect = new SideEffectProfile(stage.SideEffectLevel, requiresAudit: true);
        return
        [
            new StateCommitStep(
                $"step-{stage.StageId.Value}-state",
                context.Intent.IntentId,
                graph.GraphId,
                stage.StageId,
                KernelIds.OperationIdFor(stage.StageId),
                "state.thread",
                CreateStageEnvelope(stage),
                permission,
                sideEffect,
                stage.Budget,
                stage.OutputContract,
                new TracePolicy(enabled: true, requireDiagnosticsRef: true, requireRuntimeTraceRef: true)),
            new DiagnosticStep(
                $"step-{stage.StageId.Value}-diagnostic",
                context.Intent.IntentId,
                graph.GraphId,
                stage.StageId,
                KernelIds.OperationIdFor(stage.StageId),
                "execution.trace",
                CreateStageEnvelope(stage),
                permission,
                new SideEffectProfile(SideEffectLevel.ReadOnly, requiresAudit: true),
                stage.Budget,
                stage.OutputContract,
                new TracePolicy(enabled: true, requireDiagnosticsRef: true, requireRuntimeTraceRef: true)),
        ];
    }

    private static HostInteractionStep CreateHostInteractionStep(
        StageGraph graph,
        StageNode stage,
        KernelInterpreterContext context,
        string interactionKind)
    {
        var permission = new PermissionEnvelope(
            requiresHumanGate: context.Governance.RequiresHumanGate,
            reason: "Stable Kernel Core approved host interaction boundary.");
        var sideEffect = new SideEffectProfile(stage.SideEffectLevel, requiresAudit: true);
        return new HostInteractionStep(
            $"step-{stage.StageId.Value}",
            context.Intent.IntentId,
            graph.GraphId,
            stage.StageId,
            KernelIds.OperationIdFor(stage.StageId),
            interactionKind,
            CreateHostInteractionEnvelope(stage, context.Intent),
            permission,
            sideEffect,
            stage.Budget,
            stage.OutputContract,
            new TracePolicy(enabled: true, requireDiagnosticsRef: true, requireRuntimeTraceRef: true));
    }

    private static StructuredValue CreateHostInteractionEnvelope(StageNode stage, CoreIntent intent)
    {
        var envelope = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["objective"] = stage.Objective,
            ["inputContract"] = stage.InputContract.ContractId,
            ["outputContract"] = stage.OutputContract.ContractId,
            ["stageKind"] = stage.Kind,
            ["intentKind"] = intent.IntentKind.ToString(),
            ["sessionId"] = intent.Subject.SessionId.Value,
            ["threadId"] = intent.Subject.ThreadId.Value,
            ["turnId"] = intent.Subject.TurnId?.Value,
        };

        if (intent is InterruptIntent interrupt)
        {
            envelope["interruptReason"] = interrupt.InterruptReason;
        }

        if (intent is ResumeIntent resume)
        {
            envelope["resumeToken"] = resume.ResumeToken;
            envelope["checkpointRef"] = resume.CheckpointRef;
        }

        return StructuredValue.FromPlainObject(envelope);
    }

    private static ModuleCapabilityStep CreateMemoryCapabilityStep(
        StageGraph graph,
        StageNode stage,
        KernelInterpreterContext context,
        string capabilityId,
        bool requiresHumanGate)
        => new(
            $"step-{stage.StageId.Value}",
            context.Intent.IntentId,
            graph.GraphId,
            stage.StageId,
            KernelIds.OperationIdFor(stage.StageId),
            "memory.identity",
            capabilityId,
            CreateMemoryStageEnvelope(stage, capabilityId),
            new PermissionEnvelope(
                [capabilityId],
                requiresHumanGate: requiresHumanGate || context.Governance.RequiresHumanGate,
                reason: "Stable Kernel Core approved Memory Module capability."),
            new SideEffectProfile(stage.SideEffectLevel, ["memory"], reversible: capabilityId == "memory.retrieve", requiresAudit: true),
            stage.Budget,
            stage.OutputContract,
            new TracePolicy(enabled: true, requireDiagnosticsRef: true, requireRuntimeTraceRef: true));

    private static StructuredValue CreateMemoryStageEnvelope(StageNode stage, string capabilityId)
    {
        var envelope = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["objective"] = stage.Objective,
            ["inputContract"] = stage.InputContract.ContractId,
            ["outputContract"] = stage.OutputContract.ContractId,
            ["stageKind"] = stage.Kind,
            ["memoryCapabilityId"] = capabilityId,
        };

        if (capabilityId == "memory.retrieve")
        {
            envelope["queryText"] = stage.Objective;
        }

        return StructuredValue.FromPlainObject(envelope);
    }

    private static ModuleCapabilityStep CreateModuleCapabilityFallbackStep(StageGraph graph, StageNode stage, KernelInterpreterContext context)
        => new(
            $"step-{stage.StageId.Value}",
            context.Intent.IntentId,
            graph.GraphId,
            stage.StageId,
            KernelIds.OperationIdFor(stage.StageId),
            "kernel.default",
            stage.Kind,
            CreateStageEnvelope(stage),
            new PermissionEnvelope(
                stage.AllowedCapabilityToolIds,
                requiresHumanGate: context.Governance.RequiresHumanGate,
                reason: "Stable Kernel Core approved stage capability materialization."),
            new SideEffectProfile(stage.SideEffectLevel, requiresAudit: true),
            stage.Budget,
            stage.OutputContract,
            new TracePolicy(enabled: true, requireDiagnosticsRef: true, requireRuntimeTraceRef: true));

    private static IReadOnlyList<StageNode> OrderStages(StageGraph graph)
    {
        var byId = graph.Stages.ToDictionary(static stage => stage.StageId);
        var ordered = new List<StageNode>();
        var visited = new HashSet<StageId>();
        var queue = new Queue<StageId>();
        queue.Enqueue(graph.EntryStageId);

        while (queue.Count > 0)
        {
            var stageId = queue.Dequeue();
            if (!visited.Add(stageId) || !byId.TryGetValue(stageId, out var stage))
            {
                continue;
            }

            ordered.Add(stage);
            foreach (var edge in graph.Edges.Where(edge => edge.FromStageId == stageId))
            {
                queue.Enqueue(edge.ToStageId);
            }
        }

        var terminalStages = graph.Stages
            .Select(static stage => stage.StageId)
            .Where(stageId => graph.Edges.All(edge => edge.FromStageId != stageId))
            .ToHashSet();
        return ordered
            .OrderBy(stage => terminalStages.Contains(stage.StageId) ? 1 : 0)
            .ToArray();
    }

    private static bool Matches(StageTransitionKind transitionKind, StageResultStatus status)
        => (transitionKind, status) switch
        {
            (StageTransitionKind.Success, StageResultStatus.Succeeded) => true,
            (StageTransitionKind.Failure, StageResultStatus.Failed) => true,
            (StageTransitionKind.Abort, StageResultStatus.Blocked) => true,
            (StageTransitionKind.Conditional, _) => true,
            (StageTransitionKind.Recovery, StageResultStatus.Failed) => true,
            _ => false,
        };

    private static bool MatchesSignals(StageEdge edge, IReadOnlySet<string> signals)
    {
        if (signals.Count == 0)
        {
            return edge.Guard.RequiredSignals.Count == 0;
        }

        if (!string.IsNullOrWhiteSpace(edge.Condition.ConditionKind)
            && !signals.Contains(edge.Condition.ConditionKind))
        {
            return false;
        }

        return edge.Guard.RequiredSignals.All(signals.Contains);
    }

    private static IReadOnlySet<string> ResolveSignals(StageResult result)
    {
        var signals = new HashSet<string>(StringComparer.Ordinal);
        AddSignals(result.Output, signals);
        if (result.Metadata.TryGetValue("signals", out var metadataSignals))
        {
            AddSignals(metadataSignals, signals);
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

        if (value.Kind == StructuredValueKind.Object
            && value.TryGetProperty("signals", out var nestedSignals))
        {
            AddSignals(nestedSignals, signals);
        }
    }

    private static ModelRouteCandidateBinding ResolveModelCandidate(ModelRoutePolicy policy)
    {
        var preferred = policy.Candidates.FirstOrDefault(candidate => string.Equals(candidate.CandidateId, policy.PreferredRouteId, StringComparison.Ordinal));
        if (preferred is not null)
        {
            return preferred;
        }

        var firstCandidate = policy.Candidates.FirstOrDefault();
        return firstCandidate ?? new ModelRouteCandidateBinding("route.default", "provider.default", "default", "default");
    }

    private static StructuredValue CreateStageEnvelope(StageNode stage)
        => StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["objective"] = stage.Objective,
            ["inputContract"] = stage.InputContract.ContractId,
            ["outputContract"] = stage.OutputContract.ContractId,
            ["stageKind"] = stage.Kind,
        });

    private static StructuredValue CreateStaticToolDispatchEnvelope(StageNode stage, string capabilityToolId)
        => StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["objective"] = stage.Objective,
            ["inputContract"] = stage.InputContract.ContractId,
            ["outputContract"] = stage.OutputContract.ContractId,
            ["stageKind"] = stage.Kind,
            ["toolDispatchMode"] = "static_stage_skeleton",
            ["selectedCapabilityToolId"] = capabilityToolId,
            ["selectedCapabilityToolIdSource"] = "stage.allow_list.first",
            ["requiresModelToolRequests"] = true,
        });
}
