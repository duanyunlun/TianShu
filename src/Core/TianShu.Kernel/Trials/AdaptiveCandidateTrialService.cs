using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Kernel.Abstractions;
using TianShu.Kernel.Interpretation;
using TianShu.Kernel.Validation;

namespace TianShu.Kernel.Trials;

/// <summary>
/// 默认 Adaptive 候选试运行服务，只做计划物化、验证和差异记录，不调用 Execution Runtime。
/// Default adaptive candidate trial service that only materializes, validates, and diffs plans without invoking Execution Runtime.
/// </summary>
public sealed class AdaptiveCandidateTrialService : IAdaptiveCandidateTrialService
{
    private readonly IStageGraphInterpreter interpreter;
    private readonly IKernelValidator validator;

    public AdaptiveCandidateTrialService(IStageGraphInterpreter? interpreter = null, IKernelValidator? validator = null)
    {
        this.interpreter = interpreter ?? new StageGraphInterpreter();
        this.validator = validator ?? new KernelValidator();
    }

    public async Task<AdaptiveCandidateTrialReport> RunTrialsAsync(
        AdaptiveCandidateTrialRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validationByProposalId = request.ValidationReport.Records.ToDictionary(static record => record.ProposalId);
        var records = new List<AdaptiveCandidateTrialRecord>();
        foreach (var proposal in request.ProposalSet.Proposals)
        {
            if (!validationByProposalId.TryGetValue(proposal.ProposalId, out var validationRecord)
                || !validationRecord.IsAccepted
                || proposal is not StageGraphProposal stageGraphProposal)
            {
                AddSkippedRecords(request, records, proposal, validationRecord);
                continue;
            }

            await AddTrialRecordsAsync(request, records, stageGraphProposal, cancellationToken).ConfigureAwait(false);
        }

        return new AdaptiveCandidateTrialReport(records, "adaptive.candidate.plan_only_trial");
    }

    private async Task AddTrialRecordsAsync(
        AdaptiveCandidateTrialRequest request,
        List<AdaptiveCandidateTrialRecord> records,
        StageGraphProposal proposal,
        CancellationToken cancellationToken)
    {
        ExecutionPlan? candidatePlan = null;
        KernelValidationResult? executionValidation = null;
        AdaptiveCandidatePlanDiff? diff = null;
        try
        {
            var state = request.Context.State ?? new KernelRunState(new KernelRunId("run-adaptive-trial-preview"), request.Context.Intent.IntentId);
            state = state with { SelectedGraphId = proposal.Graph.GraphId };
            candidatePlan = await interpreter.InterpretAsync(
                proposal.Graph,
                new KernelInterpreterContext(request.Context.Intent, state, request.Options, request.Context.Governance),
                cancellationToken).ConfigureAwait(false);
            diff = CreateDiff(request.BaselineGraph, request.BaselinePlan, proposal.Graph, candidatePlan);
            executionValidation = await ValidateExecutionPlanAsync(proposal.Graph, candidatePlan, request.Context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            var failed = Rejected(
                "kernel.candidate_trial.materialization_failed",
                "候选 StageGraph 无法物化为可审查 ExecutionPlan。",
                proposal.Graph.GraphId.Value);
            AddModeRecords(request, records, proposal, AdaptiveCandidateTrialStatus.Failed, diff, failed, "trial.materialization_failed");
            return;
        }

        foreach (var mode in request.Modes)
        {
            switch (mode)
            {
                case AdaptiveCandidateTrialMode.ShadowRun:
                    records.Add(new AdaptiveCandidateTrialRecord(
                        proposal.ProposalId,
                        proposal.Graph.GraphId,
                        AdaptiveCandidateTrialMode.ShadowRun,
                        AdaptiveCandidateTrialStatus.Succeeded,
                        diff,
                        Approved(),
                        "shadow.plan_diff",
                        executedRuntime: false,
                        promotedStrategy: false));
                    break;
                case AdaptiveCandidateTrialMode.BoundedPlanTrial:
                    records.Add(new AdaptiveCandidateTrialRecord(
                        proposal.ProposalId,
                        proposal.Graph.GraphId,
                        AdaptiveCandidateTrialMode.BoundedPlanTrial,
                        executionValidation!.IsApproved ? AdaptiveCandidateTrialStatus.Succeeded : AdaptiveCandidateTrialStatus.Blocked,
                        diff,
                        executionValidation,
                        executionValidation.IsApproved ? "bounded_plan_trial.approved" : "bounded_plan_trial.blocked",
                        executedRuntime: false,
                        promotedStrategy: false));
                    break;
                default:
                    records.Add(new AdaptiveCandidateTrialRecord(
                        proposal.ProposalId,
                        proposal.Graph.GraphId,
                        mode,
                        AdaptiveCandidateTrialStatus.Skipped,
                        diff,
                        Rejected("kernel.candidate_trial.unspecified_mode", "候选试运行模式未指定。", proposal.Graph.GraphId.Value),
                        "trial.unspecified_mode"));
                    break;
            }
        }
    }

    private static void AddSkippedRecords(
        AdaptiveCandidateTrialRequest request,
        ICollection<AdaptiveCandidateTrialRecord> records,
        KernelProposal proposal,
        AdaptiveCandidateValidationRecord? validationRecord)
    {
        var validation = validationRecord?.Checks.FirstOrDefault(static check => !check.Result.IsApproved)?.Result
            ?? Rejected(
                "kernel.candidate_trial.validation_not_accepted",
                "候选未通过 P30.3 验证，不能进入 trial / shadow run。",
                proposal.ProposalId.Value);
        foreach (var mode in request.Modes)
        {
            records.Add(new AdaptiveCandidateTrialRecord(
                proposal.ProposalId,
                validationRecord?.GraphId,
                mode,
                AdaptiveCandidateTrialStatus.Skipped,
                validation: validation,
                rationaleRef: "trial.skipped.validation_not_accepted"));
        }
    }

    private static void AddModeRecords(
        AdaptiveCandidateTrialRequest request,
        ICollection<AdaptiveCandidateTrialRecord> records,
        StageGraphProposal proposal,
        AdaptiveCandidateTrialStatus status,
        AdaptiveCandidatePlanDiff? diff,
        KernelValidationResult validation,
        string rationaleRef)
    {
        foreach (var mode in request.Modes)
        {
            records.Add(new AdaptiveCandidateTrialRecord(
                proposal.ProposalId,
                proposal.Graph.GraphId,
                mode,
                status,
                diff,
                validation,
                rationaleRef));
        }
    }

    private async Task<KernelValidationResult> ValidateExecutionPlanAsync(
        StageGraph graph,
        ExecutionPlan candidatePlan,
        KernelValidationContext context,
        CancellationToken cancellationToken)
    {
        var graphContext = new KernelValidationContext(context.Intent, context.State, graph, policySet: graph.Policies, metadata: context.Metadata);
        foreach (var step in candidatePlan.Steps)
        {
            var result = await validator.ValidateRuntimeStepAsync(step, graphContext, cancellationToken).ConfigureAwait(false);
            if (!result.IsApproved)
            {
                return result;
            }
        }

        return Approved();
    }

    private static AdaptiveCandidatePlanDiff CreateDiff(
        StageGraph baselineGraph,
        ExecutionPlan baselinePlan,
        StageGraph candidateGraph,
        ExecutionPlan candidatePlan)
    {
        var baselineStepKinds = baselinePlan.Steps.Select(static step => step.StepKind.ToString()).ToArray();
        var candidateStepKinds = candidatePlan.Steps.Select(static step => step.StepKind.ToString()).ToArray();
        var baselineBudget = SumBudget(baselinePlan);
        var candidateBudget = SumBudget(candidatePlan);
        return new AdaptiveCandidatePlanDiff(
            baselineGraph.GraphId,
            candidateGraph.GraphId,
            baselinePlan.PlanId,
            candidatePlan.PlanId,
            candidatePlan.Steps.Count - baselinePlan.Steps.Count,
            candidateBudget.TokenBudget - baselineBudget.TokenBudget,
            candidateBudget.TimeBudgetMs - baselineBudget.TimeBudgetMs,
            candidateBudget.ToolCallBudget - baselineBudget.ToolCallBudget,
            MaxSideEffectLevel(candidatePlan) - MaxSideEffectLevel(baselinePlan),
            baselineStepKinds,
            candidateStepKinds,
            candidateStepKinds.Except(baselineStepKinds, StringComparer.Ordinal).ToArray(),
            baselineStepKinds.Except(candidateStepKinds, StringComparer.Ordinal).ToArray());
    }

    private static KernelBudget SumBudget(ExecutionPlan plan)
        => new(
            tokenBudget: plan.Steps.Sum(static step => step.Budget.TokenBudget),
            timeBudgetMs: plan.Steps.Sum(static step => step.Budget.TimeBudgetMs),
            costBudget: plan.Steps.Sum(static step => step.Budget.CostBudget),
            retryBudget: plan.Steps.Sum(static step => step.Budget.RetryBudget),
            toolCallBudget: plan.Steps.Sum(static step => step.Budget.ToolCallBudget));

    private static int MaxSideEffectLevel(ExecutionPlan plan)
        => plan.Steps.Count == 0 ? 0 : plan.Steps.Max(static step => (int)step.SideEffect.Level);

    private static KernelValidationResult Approved()
        => new(KernelValidationDecision.Approved);

    private static KernelValidationResult Rejected(string code, string message, string? sourceRef)
        => new(
            KernelValidationDecision.Rejected,
            new[] { new KernelValidationIssue(code, message, KernelValidationIssueSeverity.Error, sourceRef) });
}
