using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Kernel.Abstractions;
using TianShu.Kernel.Graphs;
using TianShu.Kernel.Interpretation;
using TianShu.Kernel.Tracing;
using TianShu.Kernel.Trials;
using TianShu.Kernel.Validation;

namespace TianShu.Kernel;

/// <summary>
/// Stable Kernel Core 默认实现，负责验证 StageGraph 并生成已批准的 ExecutionPlan。
/// Default Stable Kernel Core implementation that validates StageGraph and produces an approved ExecutionPlan.
/// </summary>
public sealed class StableKernelCore : IStableKernelCore
{
    private readonly IAdaptiveOrchestrator? adaptiveOrchestrator;
    private readonly IKernelValidator validator;
    private readonly IStageGraphInterpreter interpreter;
    private readonly IKernelTraceStore traceStore;
    private readonly IAdaptiveCandidateValidationService candidateValidationService;
    private readonly IAdaptiveCandidateTrialService candidateTrialService;

    public StableKernelCore(
        IKernelValidator? validator = null,
        IStageGraphInterpreter? interpreter = null,
        IKernelTraceStore? traceStore = null,
        IAdaptiveOrchestrator? adaptiveOrchestrator = null,
        IAdaptiveCandidateValidationService? candidateValidationService = null,
        IAdaptiveCandidateTrialService? candidateTrialService = null)
    {
        this.validator = validator ?? new KernelValidator();
        this.interpreter = interpreter ?? new StageGraphInterpreter();
        this.traceStore = traceStore ?? new InMemoryKernelTraceStore();
        this.adaptiveOrchestrator = adaptiveOrchestrator;
        this.candidateValidationService = candidateValidationService ?? new AdaptiveCandidateValidationService(this.validator);
        this.candidateTrialService = candidateTrialService ?? new AdaptiveCandidateTrialService(this.interpreter, this.validator);
    }

    public async Task<KernelRunResult> RunAsync(CoreIntent intent, KernelRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);

        options ??= new KernelRunOptions();
        var runId = options.RunId ?? KernelIds.NewRunId();
        var traceId = KernelIds.TraceIdFor(runId);
        var state = new KernelRunState(runId, intent.IntentId);
        var context = new KernelValidationContext(intent, state);

        var intentValidation = await validator.ValidateIntentAsync(intent, context, cancellationToken).ConfigureAwait(false);
        if (!intentValidation.IsApproved)
        {
            await AppendRejectedAsync(runId, intent.IntentId, intentValidation, cancellationToken: cancellationToken).ConfigureAwait(false);
            return FailedResult(runId, intent.IntentId, traceId, intentValidation);
        }

        state = Transition(state, KernelRunLifecycleState.IntentAccepted);
        await AppendAsync(runId, KernelTraceEventKind.IntentAccepted, "Core intent accepted.", intent.IntentId, cancellationToken: cancellationToken).ConfigureAwait(false);

        var graph = DefaultKernelStageGraphs.CreateForIntent(intent, options);
        state = Transition(state with { SelectedGraphId = graph.GraphId }, KernelRunLifecycleState.GraphSelected);
        KernelProposalSet? adaptiveProposalSet = null;
        AdaptiveCandidateValidationReport? candidateValidationReport = null;
        AdaptiveCandidateTrialReport? candidateTrialReport = null;

        if (options.EnableAdaptive && adaptiveOrchestrator is not null)
        {
            state = Transition(state, KernelRunLifecycleState.ProposalPending);
            adaptiveProposalSet = await adaptiveOrchestrator.ProposeAsync(intent, state, options, cancellationToken).ConfigureAwait(false);
            state = state with { PendingProposals = adaptiveProposalSet.Proposals };
            foreach (var proposal in adaptiveProposalSet.Proposals)
            {
                await AppendAsync(
                    runId,
                    KernelTraceEventKind.ProposalCreated,
                    $"Proposal created: {proposal.ProposalKind}.",
                    intent.IntentId,
                    sourceGraphId: ProposalGraphId(proposal) ?? graph.GraphId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            candidateValidationReport = await candidateValidationService.ValidateCandidatesAsync(
                new AdaptiveCandidateValidationRequest(adaptiveProposalSet, new KernelValidationContext(intent, state)),
                cancellationToken).ConfigureAwait(false);
            foreach (var record in candidateValidationReport.Records)
            {
                await AppendAsync(
                    runId,
                    KernelTraceEventKind.ProposalReviewed,
                    $"Adaptive candidate {record.ProposalId.Value} {record.Status}.",
                    intent.IntentId,
                    sourceGraphId: record.GraphId ?? graph.GraphId,
                    data: CandidateRecordToStructuredValue(record),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            state = Transition(state, KernelRunLifecycleState.ProposalPending);
        }

        context = new KernelValidationContext(intent, state, graph, policySet: graph.Policies);
        var graphValidation = await validator.ValidateGraphAsync(graph, context, cancellationToken).ConfigureAwait(false);
        if (!graphValidation.IsApproved)
        {
            await AppendRejectedAsync(runId, intent.IntentId, graphValidation, graph.GraphId, cancellationToken).ConfigureAwait(false);
            return FailedResult(runId, intent.IntentId, traceId, graphValidation, candidateValidationReport, candidateTrialReport);
        }

        state = Transition(state, KernelRunLifecycleState.GraphValidated);
        await AppendAsync(runId, KernelTraceEventKind.GraphValidated, "StageGraph validated.", intent.IntentId, graph.GraphId, cancellationToken: cancellationToken).ConfigureAwait(false);

        var interpreterContext = new KernelInterpreterContext(intent, state, options);
        var executionPlan = await interpreter.InterpretAsync(graph, interpreterContext, cancellationToken).ConfigureAwait(false);
        var executionValidation = await ReviewExecutionPlanAsync(context, executionPlan, cancellationToken).ConfigureAwait(false);
        if (!executionValidation.IsApproved)
        {
            await AppendRejectedAsync(runId, intent.IntentId, executionValidation, graph.GraphId, cancellationToken).ConfigureAwait(false);
            return FailedResult(runId, intent.IntentId, traceId, executionValidation, candidateValidationReport, candidateTrialReport);
        }

        if (adaptiveProposalSet is not null && candidateValidationReport is not null && candidateValidationReport.HasAcceptedCandidate)
        {
            candidateTrialReport = await candidateTrialService.RunTrialsAsync(
                new AdaptiveCandidateTrialRequest(
                    adaptiveProposalSet,
                    candidateValidationReport,
                    new KernelValidationContext(intent, state, graph, policySet: graph.Policies),
                    graph,
                    executionPlan,
                    options),
                cancellationToken).ConfigureAwait(false);
            foreach (var record in candidateTrialReport.Records)
            {
                await AppendAsync(
                    runId,
                    KernelTraceEventKind.ProposalReviewed,
                    $"Adaptive candidate trial {record.Mode}: {record.Status}.",
                    intent.IntentId,
                    sourceGraphId: record.GraphId ?? graph.GraphId,
                    data: CandidateTrialRecordToStructuredValue(record),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }

        state = Transition(state, KernelRunLifecycleState.Executing);
        await AppendAsync(runId, KernelTraceEventKind.ExecutionPlanCreated, "ExecutionPlan approved for Execution Runtime.", intent.IntentId, graph.GraphId, cancellationToken: cancellationToken).ConfigureAwait(false);
        await AppendAsync(runId, KernelTraceEventKind.CheckpointCreated, $"Checkpoint policy materialized for graph {graph.GraphId.Value}.", intent.IntentId, graph.GraphId, cancellationToken: cancellationToken).ConfigureAwait(false);
        await AppendAsync(runId, KernelTraceEventKind.EvaluationRecorded, $"Evaluation policy registered for graph {graph.GraphId.Value}.", intent.IntentId, graph.GraphId, cancellationToken: cancellationToken).ConfigureAwait(false);

        return new KernelRunResult(
            runId,
            intent.IntentId,
            state.LifecycleState,
            executionValidation,
            executionPlan,
            traceId,
            approvedStageGraph: graph,
            metadata: AdaptiveReportsMetadata(candidateValidationReport, candidateTrialReport));
    }

    public Task<KernelValidationResult> ReviewProposalAsync(KernelValidationContext context, KernelProposal proposal, CancellationToken cancellationToken = default)
        => validator.ValidateProposalAsync(proposal, context, cancellationToken);

    public Task<KernelValidationResult> ReviewOperationAsync(KernelValidationContext context, KernelOperation operation, CancellationToken cancellationToken = default)
        => validator.ValidateOperationAsync(operation, context, cancellationToken);

    public async Task<KernelValidationResult> ReviewExecutionPlanAsync(KernelValidationContext context, ExecutionPlan executionPlan, CancellationToken cancellationToken = default)
    {
        if (executionPlan is null)
        {
            return KernelValidationResults.Rejected("kernel.execution_plan.missing", "ExecutionPlan 不能为空。", "execution_plan");
        }

        foreach (var step in executionPlan.Steps)
        {
            var stepValidation = await validator.ValidateRuntimeStepAsync(step, context, cancellationToken).ConfigureAwait(false);
            if (!stepValidation.IsApproved)
            {
                return stepValidation;
            }
        }

        return KernelValidationResults.Approved();
    }

    private static KernelRunState Transition(KernelRunState state, KernelRunLifecycleState targetState)
    {
        if (!KernelRunStateMachine.CanTransition(state.LifecycleState, targetState))
        {
            throw new InvalidOperationException($"Kernel 状态不能从 {state.LifecycleState} 转换到 {targetState}。");
        }

        return state with { LifecycleState = targetState };
    }

    private static KernelRunResult FailedResult(
        KernelRunId runId,
        CoreIntentId intentId,
        KernelTraceId traceId,
        KernelValidationResult validation,
        AdaptiveCandidateValidationReport? candidateValidationReport = null,
        AdaptiveCandidateTrialReport? candidateTrialReport = null)
        => new(
            runId,
            intentId,
            KernelRunLifecycleState.Failed,
            validation,
            traceId: traceId,
            metadata: AdaptiveReportsMetadata(candidateValidationReport, candidateTrialReport));

    private Task AppendRejectedAsync(KernelRunId runId, CoreIntentId intentId, KernelValidationResult validation, StageGraphId? graphId = null, CancellationToken cancellationToken = default)
    {
        var reason = validation.Issues.Count == 0
            ? "Kernel validation rejected the request."
            : validation.Issues[0].Message;
        return AppendAsync(runId, KernelTraceEventKind.Rejected, reason, intentId, graphId, cancellationToken: cancellationToken);
    }

    private Task AppendAsync(
        KernelRunId runId,
        KernelTraceEventKind kind,
        string message,
        CoreIntentId sourceIntentId,
        StageGraphId? sourceGraphId = null,
        StageId? sourceStageId = null,
        KernelOperationId? sourceOperationId = null,
        StructuredValue? data = null,
        CancellationToken cancellationToken = default)
        => traceStore.AppendAsync(
            runId,
            new KernelTraceEvent(kind, message, sourceIntentId: sourceIntentId, sourceGraphId: sourceGraphId, sourceStageId: sourceStageId, sourceOperationId: sourceOperationId, data: data),
            cancellationToken);

    private static StageGraphId? ProposalGraphId(KernelProposal proposal)
        => proposal is StageGraphProposal stageGraphProposal ? stageGraphProposal.Graph.GraphId : null;

    private static MetadataBag AdaptiveReportsMetadata(AdaptiveCandidateValidationReport? validationReport, AdaptiveCandidateTrialReport? trialReport)
    {
        if (validationReport is null && trialReport is null)
        {
            return MetadataBag.Empty;
        }

        var entries = new Dictionary<string, StructuredValue>(StringComparer.Ordinal);
        if (validationReport is not null)
        {
            entries["adaptiveCandidateValidationReport"] = CandidateReportToStructuredValue(validationReport);
        }

        if (trialReport is not null)
        {
            entries["adaptiveCandidateTrialReport"] = CandidateTrialReportToStructuredValue(trialReport);
        }

        return new MetadataBag(entries);
    }

    private static StructuredValue CandidateReportToStructuredValue(AdaptiveCandidateValidationReport report)
        => StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["rationaleRef"] = report.RationaleRef,
            ["acceptedCount"] = report.AcceptedCount,
            ["rejectedCount"] = report.RejectedCount,
            ["records"] = report.Records.Select(CandidateRecordPlainObject).ToArray(),
        });

    private static StructuredValue CandidateRecordToStructuredValue(AdaptiveCandidateValidationRecord record)
        => StructuredValue.FromPlainObject(CandidateRecordPlainObject(record));

    private static StructuredValue CandidateTrialReportToStructuredValue(AdaptiveCandidateTrialReport report)
        => StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["rationaleRef"] = report.RationaleRef,
            ["succeededCount"] = report.SucceededCount,
            ["blockedCount"] = report.BlockedCount,
            ["executedRuntime"] = report.ExecutedRuntime,
            ["promotedStrategy"] = report.PromotedStrategy,
            ["records"] = report.Records.Select(CandidateTrialRecordPlainObject).ToArray(),
        });

    private static StructuredValue CandidateTrialRecordToStructuredValue(AdaptiveCandidateTrialRecord record)
        => StructuredValue.FromPlainObject(CandidateTrialRecordPlainObject(record));

    private static Dictionary<string, object?> CandidateRecordPlainObject(AdaptiveCandidateValidationRecord record)
        => new(StringComparer.Ordinal)
        {
            ["proposalId"] = record.ProposalId.Value,
            ["proposalKind"] = record.ProposalKind.ToString(),
            ["status"] = record.Status.ToString(),
            ["graphId"] = record.GraphId?.Value,
            ["checks"] = record.Checks.Select(static check => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["checkKind"] = check.CheckKind.ToString(),
                ["status"] = check.Status.ToString(),
                ["decision"] = check.Result.Decision.ToString(),
                ["issueCodes"] = check.Result.Issues.Select(static issue => issue.Code).ToArray(),
                ["sourceRef"] = check.SourceRef,
            }).ToArray(),
        };

    private static Dictionary<string, object?> CandidateTrialRecordPlainObject(AdaptiveCandidateTrialRecord record)
        => new(StringComparer.Ordinal)
        {
            ["proposalId"] = record.ProposalId.Value,
            ["mode"] = record.Mode.ToString(),
            ["status"] = record.Status.ToString(),
            ["graphId"] = record.GraphId?.Value,
            ["rationaleRef"] = record.RationaleRef,
            ["executedRuntime"] = record.ExecutedRuntime,
            ["promotedStrategy"] = record.PromotedStrategy,
            ["decision"] = record.Validation.Decision.ToString(),
            ["issueCodes"] = record.Validation.Issues.Select(static issue => issue.Code).ToArray(),
            ["diff"] = record.Diff is null
                ? null
                : new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["baselineGraphId"] = record.Diff.BaselineGraphId.Value,
                    ["candidateGraphId"] = record.Diff.CandidateGraphId.Value,
                    ["baselinePlanId"] = record.Diff.BaselinePlanId,
                    ["candidatePlanId"] = record.Diff.CandidatePlanId,
                    ["stepCountDelta"] = record.Diff.StepCountDelta,
                    ["tokenBudgetDelta"] = record.Diff.TokenBudgetDelta,
                    ["timeBudgetMsDelta"] = record.Diff.TimeBudgetMsDelta,
                    ["toolCallBudgetDelta"] = record.Diff.ToolCallBudgetDelta,
                    ["maxSideEffectLevelDelta"] = record.Diff.MaxSideEffectLevelDelta,
                    ["baselineStepKinds"] = record.Diff.BaselineStepKinds,
                    ["candidateStepKinds"] = record.Diff.CandidateStepKinds,
                    ["addedStepKinds"] = record.Diff.AddedStepKinds,
                    ["removedStepKinds"] = record.Diff.RemovedStepKinds,
                },
        };
}
