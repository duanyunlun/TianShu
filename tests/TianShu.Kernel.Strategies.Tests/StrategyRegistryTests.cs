using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Kernel.Abstractions;
using TianShu.Kernel.Strategies;
using TianShu.Kernel.Tracing;

namespace TianShu.Kernel.Strategies.Tests;

public sealed class StrategyRegistryTests
{
    [Fact]
    public async Task StrategyLifecycle_AllowsOrderedPromotionWithEvidence()
    {
        var registry = new StrategyRegistry();
        var strategy = await registry.SaveDraftAsync(CreateStrategy());
        var evidence = CreateEvidence(humanApproved: true);

        strategy = await registry.TransitionAsync(strategy.StrategyId, StrategyLifecycleState.Validated, evidence);
        strategy = await registry.TransitionAsync(strategy.StrategyId, StrategyLifecycleState.Trial, evidence);
        strategy = await registry.TransitionAsync(strategy.StrategyId, StrategyLifecycleState.Promoted, evidence);

        Assert.Equal(StrategyLifecycleState.Promoted, strategy.LifecycleState);
        Assert.Equal(strategy, await registry.GetPromotedAsync(CoreIntentKind.Turn));
    }

    [Fact]
    public async Task StrategyLifecycle_RejectsIllegalTransition()
    {
        var registry = new StrategyRegistry();
        var strategy = await registry.SaveDraftAsync(CreateStrategy());

        var result = await registry.ValidateTransitionAsync(strategy, StrategyLifecycleState.Promoted, CreateEvidence(humanApproved: true));

        Assert.Equal(KernelValidationDecision.Rejected, result.Decision);
        Assert.Contains(result.Issues, issue => issue.Code == "kernel.strategy.illegal_transition");
    }

    [Fact]
    public async Task PromotionGate_RejectsMissingEvidenceAndHumanGate()
    {
        var registry = new StrategyRegistry();
        var strategy = await registry.SaveDraftAsync(CreateStrategy());
        strategy = await registry.TransitionAsync(strategy.StrategyId, StrategyLifecycleState.Validated, CreateEvidence());
        strategy = await registry.TransitionAsync(strategy.StrategyId, StrategyLifecycleState.Trial, CreateEvidence());

        var missingEvidence = await registry.ValidateTransitionAsync(strategy, StrategyLifecycleState.Promoted, Array.Empty<StrategyTransitionEvidence>());
        var missingHumanGate = await registry.ValidateTransitionAsync(strategy, StrategyLifecycleState.Promoted, CreateEvidence(humanApproved: false));

        Assert.Equal(KernelValidationDecision.Rejected, missingEvidence.Decision);
        Assert.Equal(KernelValidationDecision.Rejected, missingHumanGate.Decision);
        Assert.Contains(missingHumanGate.Issues, issue => issue.Code == "kernel.strategy.missing_human_gate");
    }

    [Fact]
    public async Task Rollback_RecordsReasonAndKeepsDeprecatedStrategy()
    {
        var registry = new StrategyRegistry();
        var strategy = await registry.SaveDraftAsync(CreateStrategy());
        strategy = await registry.TransitionAsync(strategy.StrategyId, StrategyLifecycleState.Validated, CreateEvidence());
        strategy = await registry.TransitionAsync(strategy.StrategyId, StrategyLifecycleState.RolledBack, CreateEvidence("rollback.reason"));

        Assert.Equal(StrategyLifecycleState.RolledBack, strategy.LifecycleState);
        var rollback = Assert.Single(registry.RollbackRecords);
        Assert.Equal("rollback.reason", rollback.ReasonRef);
    }

    [Fact]
    public void ReplayCompatibility_RequiresCoreTraceEventsAndNoRejection()
    {
        var checker = new ReplayCompatibilityChecker();
        var trace = new KernelRunTrace(new KernelRunId("run-001"), new[]
        {
            new KernelTraceEvent(KernelTraceEventKind.IntentAccepted, "intent"),
            new KernelTraceEvent(KernelTraceEventKind.GraphValidated, "graph"),
            new KernelTraceEvent(KernelTraceEventKind.ExecutionPlanCreated, "plan"),
        });

        var result = checker.Check(trace);

        Assert.True(result.Compatible);
    }

    [Fact]
    public async Task KernelEvaluator_GeneratesEvaluationFromTrace()
    {
        var evaluator = new KernelEvaluator();
        var runId = new KernelRunId("run-001");
        var intentId = new CoreIntentId("intent-001");
        var traceId = new KernelTraceId("trace-run-001");
        var runResult = new KernelRunResult(
            runId,
            intentId,
            KernelRunLifecycleState.Completed,
            new KernelValidationResult(KernelValidationDecision.Approved),
            traceId: traceId);
        var trace = new KernelRunTrace(runId, new[]
        {
            new KernelTraceEvent(KernelTraceEventKind.IntentAccepted, "intent"),
            new KernelTraceEvent(KernelTraceEventKind.GraphValidated, "graph"),
            new KernelTraceEvent(KernelTraceEventKind.ExecutionPlanCreated, "plan"),
        });

        var result = await evaluator.EvaluateAsync(runResult, trace, new EvaluationPlan("evaluation", requireReplay: true));

        Assert.Equal(KernelReviewDecision.Approved, result.Decision);
        Assert.Equal(1m, result.MetricScores["replay_compatible"]);
    }

    [Fact]
    public async Task TraceEvaluationService_ReadsRunTraceBeforeEvaluation()
    {
        var traceStore = new InMemoryKernelTraceStore();
        var runId = new KernelRunId("run-001");
        await traceStore.AppendAsync(runId, new KernelTraceEvent(KernelTraceEventKind.IntentAccepted, "intent"));
        await traceStore.AppendAsync(runId, new KernelTraceEvent(KernelTraceEventKind.GraphValidated, "graph"));
        await traceStore.AppendAsync(runId, new KernelTraceEvent(KernelTraceEventKind.ExecutionPlanCreated, "plan"));
        var service = new KernelTraceEvaluationService(traceStore);
        var result = await service.EvaluateRunAsync(
            new KernelRunResult(
                runId,
                new CoreIntentId("intent-001"),
                KernelRunLifecycleState.Completed,
                new KernelValidationResult(KernelValidationDecision.Approved),
                traceId: new KernelTraceId("trace-run-001")),
            new EvaluationPlan("evaluation"));

        Assert.Equal(KernelReviewDecision.Approved, result.Decision);
    }

    private static StrategyRecord CreateStrategy()
        => new(
            new StrategyId("strategy-001"),
            "Strategy",
            new StageGraphId("graph-001"));

    private static IReadOnlyList<StrategyTransitionEvidence> CreateEvidence(string evidenceRef = "evaluation.evidence", bool humanApproved = false)
        =>
        [
            new StrategyTransitionEvidence(
                new KernelRunId("run-001"),
                new KernelTraceId("trace-run-001"),
                evidenceRef,
                new[] { "success", "replay_compatible" },
                humanApproved),
        ];
}
