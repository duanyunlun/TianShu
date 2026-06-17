using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Kernel.Abstractions;
using TianShu.Kernel.Interpretation;
using TianShu.Kernel.Tracing;
using TianShu.Kernel.Trials;
using TianShu.Kernel.Validation;

namespace TianShu.Kernel.Tests;

public sealed class AdaptiveCandidateValidationServiceTests
{
    [Fact]
    public async Task ValidateCandidatesAsync_AcceptsValidStageGraphCandidateWithAllCheckGroups()
    {
        var intent = CreateIntent();
        var proposal = CreateProposal(intent, CreateGraph(intent));
        var service = new AdaptiveCandidateValidationService();

        var report = await service.ValidateCandidatesAsync(new AdaptiveCandidateValidationRequest(
            new KernelProposalSet(new[] { proposal }, "test.candidates"),
            new KernelValidationContext(intent, new KernelRunState(new KernelRunId("run-candidate-valid"), intent.IntentId))));

        var record = Assert.Single(report.Records);
        Assert.True(report.HasAcceptedCandidate);
        Assert.Equal(AdaptiveCandidateValidationStatus.Accepted, record.Status);
        Assert.Contains(record.Checks, check => check.CheckKind == AdaptiveCandidateValidationCheckKind.Schema && check.Status == AdaptiveCandidateValidationStatus.Accepted);
        Assert.Contains(record.Checks, check => check.CheckKind == AdaptiveCandidateValidationCheckKind.DeterministicKernel && check.Status == AdaptiveCandidateValidationStatus.Accepted);
        Assert.Contains(record.Checks, check => check.CheckKind == AdaptiveCandidateValidationCheckKind.Governance && check.Status == AdaptiveCandidateValidationStatus.Accepted);
        Assert.Contains(record.Checks, check => check.CheckKind == AdaptiveCandidateValidationCheckKind.Budget && check.Status == AdaptiveCandidateValidationStatus.Accepted);
        Assert.Contains(record.Checks, check => check.CheckKind == AdaptiveCandidateValidationCheckKind.Capability && check.Status == AdaptiveCandidateValidationStatus.Accepted);
    }

    [Fact]
    public async Task ValidateCandidatesAsync_RejectsCandidateWhenCapabilityIsOutsideGovernance()
    {
        var intent = CreateIntent(allowedToolIds: new[] { "kernel.request_capability_call" });
        var proposal = CreateProposal(intent, CreateGraph(intent));
        var service = new AdaptiveCandidateValidationService();

        var report = await service.ValidateCandidatesAsync(new AdaptiveCandidateValidationRequest(
            new KernelProposalSet(new[] { proposal }),
            new KernelValidationContext(intent, new KernelRunState(new KernelRunId("run-candidate-reject"), intent.IntentId))));

        var record = Assert.Single(report.Records);
        Assert.False(report.HasAcceptedCandidate);
        Assert.Equal(AdaptiveCandidateValidationStatus.Rejected, record.Status);
        Assert.Contains(record.Checks, check =>
            check.CheckKind == AdaptiveCandidateValidationCheckKind.Capability
            && check.Status == AdaptiveCandidateValidationStatus.Rejected
            && check.Result.Issues.Any(issue => issue.Code == "kernel.graph.capability_tool_not_in_governance"));
    }

    [Fact]
    public async Task ValidateCandidatesAsync_RejectsCandidateWhenBudgetIsUnbounded()
    {
        var intent = CreateIntent();
        var proposal = CreateProposal(intent, CreateGraph(intent, budget: KernelBudget.Zero));
        var service = new AdaptiveCandidateValidationService();

        var report = await service.ValidateCandidatesAsync(new AdaptiveCandidateValidationRequest(
            new KernelProposalSet(new[] { proposal }),
            new KernelValidationContext(intent, new KernelRunState(new KernelRunId("run-candidate-budget-reject"), intent.IntentId))));

        var record = Assert.Single(report.Records);
        Assert.False(report.HasAcceptedCandidate);
        Assert.Equal(AdaptiveCandidateValidationStatus.Rejected, record.Status);
        Assert.Contains(record.Checks, check =>
            check.CheckKind == AdaptiveCandidateValidationCheckKind.Budget
            && check.Status == AdaptiveCandidateValidationStatus.Rejected
            && check.Result.Issues.Any(issue => issue.Code == "kernel.graph.unbounded_budget"));
    }

    [Fact]
    public async Task ValidateCandidatesAsync_RejectsCandidateWhenGovernanceCeilingIsExceeded()
    {
        var intent = CreateIntent();
        var proposal = CreateProposal(intent, CreateGraph(
            intent,
            stageSideEffectLevel: SideEffectLevel.ExternalNetwork,
            graphSideEffectLevel: SideEffectLevel.ExternalNetwork));
        var service = new AdaptiveCandidateValidationService();

        var report = await service.ValidateCandidatesAsync(new AdaptiveCandidateValidationRequest(
            new KernelProposalSet(new[] { proposal }),
            new KernelValidationContext(intent, new KernelRunState(new KernelRunId("run-candidate-governance-reject"), intent.IntentId))));

        var record = Assert.Single(report.Records);
        Assert.False(report.HasAcceptedCandidate);
        Assert.Equal(AdaptiveCandidateValidationStatus.Rejected, record.Status);
        Assert.Contains(record.Checks, check =>
            check.CheckKind == AdaptiveCandidateValidationCheckKind.Governance
            && check.Status == AdaptiveCandidateValidationStatus.Rejected
            && check.Result.Issues.Any(issue => issue.Code == "kernel.graph.side_effect_exceeds_governance"));
    }

    [Fact]
    public async Task StableKernelCore_ReviewsAdaptiveCandidatesWithoutReplacingDefaultExecutionGraph()
    {
        var intent = CreateDefaultTurnIntent();
        var invalidCandidate = CreateProposal(intent, CreateGraph(intent, budget: KernelBudget.Zero));
        var traceStore = new InMemoryKernelTraceStore();
        var core = new StableKernelCore(
            traceStore: traceStore,
            adaptiveOrchestrator: new FixedAdaptiveOrchestrator(new KernelProposalSet(new[] { invalidCandidate }, "test.invalid.candidate")));

        var result = await core.RunAsync(intent, new KernelRunOptions(runId: new KernelRunId("run-stable-candidate"), requireHumanGate: false));
        var trace = await traceStore.ReadRunTraceAsync(result.RunId);

        Assert.Equal(KernelRunLifecycleState.Executing, result.LifecycleState);
        Assert.True(result.Validation.IsApproved);
        Assert.Equal(new StageGraphId("graph.turn.default"), result.ApprovedStageGraph?.GraphId);
        Assert.NotNull(trace);
        Assert.Contains(trace!.Events, item => item.Kind == KernelTraceEventKind.ProposalCreated);
        Assert.Contains(trace.Events, item =>
            item.Kind == KernelTraceEventKind.ProposalReviewed
            && item.Message.Contains("Rejected", StringComparison.Ordinal)
            && item.Data.GetProperty("status").GetString() == AdaptiveCandidateValidationStatus.Rejected.ToString());
        Assert.True(result.Metadata.TryGetValue("adaptiveCandidateValidationReport", out var reportValue));
        Assert.Equal(0, reportValue.GetProperty("acceptedCount").GetInt32());
        Assert.Equal(1, reportValue.GetProperty("rejectedCount").GetInt32());
    }

    [Fact]
    public async Task RunTrialsAsync_MaterializesAcceptedCandidateWithoutRuntimeExecutionOrPromotion()
    {
        var intent = CreateIntent();
        var candidateGraph = CreateGraph(intent);
        var proposal = CreateProposal(intent, candidateGraph);
        var validationService = new AdaptiveCandidateValidationService();
        var validationReport = await validationService.ValidateCandidatesAsync(new AdaptiveCandidateValidationRequest(
            new KernelProposalSet(new[] { proposal }),
            new KernelValidationContext(intent, new KernelRunState(new KernelRunId("run-trial"), intent.IntentId))));
        var baselinePlan = await new StageGraphInterpreter().InterpretAsync(
            candidateGraph,
            new KernelInterpreterContext(
                intent,
                new KernelRunState(new KernelRunId("run-trial"), intent.IntentId, selectedGraphId: candidateGraph.GraphId),
                new KernelRunOptions(runId: new KernelRunId("run-trial"), requireHumanGate: false)));
        var trialService = new AdaptiveCandidateTrialService();

        var report = await trialService.RunTrialsAsync(new AdaptiveCandidateTrialRequest(
            new KernelProposalSet(new[] { proposal }),
            validationReport,
            new KernelValidationContext(intent, new KernelRunState(new KernelRunId("run-trial"), intent.IntentId)),
            candidateGraph,
            baselinePlan,
            new KernelRunOptions(runId: new KernelRunId("run-trial"), requireHumanGate: false)));

        Assert.False(report.ExecutedRuntime);
        Assert.False(report.PromotedStrategy);
        Assert.Equal(2, report.SucceededCount);
        Assert.Contains(report.Records, record => record.Mode == AdaptiveCandidateTrialMode.ShadowRun && record.Diff is not null);
        Assert.Contains(report.Records, record => record.Mode == AdaptiveCandidateTrialMode.BoundedPlanTrial && record.Validation.IsApproved);
        Assert.All(report.Records, record =>
        {
            Assert.False(record.ExecutedRuntime);
            Assert.False(record.PromotedStrategy);
        });
    }

    [Fact]
    public async Task StableKernelCore_RunsPlanOnlyTrialForAcceptedCandidateWithoutReplacingDefaultGraph()
    {
        var intent = CreateDefaultTurnIntent(
            extraAllowedToolIds: ["kernel.request_capability_call", "module.core_loop"],
            extraAllowedModuleIds: ["module.core_loop"]);
        var validCandidate = CreateProposal(intent, CreateGraph(intent));
        var traceStore = new InMemoryKernelTraceStore();
        var core = new StableKernelCore(
            traceStore: traceStore,
            adaptiveOrchestrator: new FixedAdaptiveOrchestrator(new KernelProposalSet(new[] { validCandidate }, "test.valid.candidate")));

        var result = await core.RunAsync(intent, new KernelRunOptions(runId: new KernelRunId("run-stable-trial"), requireHumanGate: false));
        var trace = await traceStore.ReadRunTraceAsync(result.RunId);

        Assert.Equal(KernelRunLifecycleState.Executing, result.LifecycleState);
        Assert.True(result.Validation.IsApproved);
        Assert.Equal(new StageGraphId("graph.turn.default"), result.ApprovedStageGraph?.GraphId);
        Assert.True(result.Metadata.TryGetValue("adaptiveCandidateValidationReport", out var validationReport));
        Assert.True(result.Metadata.TryGetValue("adaptiveCandidateTrialReport", out var trialReport));
        Assert.Equal(1, validationReport.GetProperty("acceptedCount").GetInt32());
        Assert.False(trialReport.GetProperty("executedRuntime").GetBoolean());
        Assert.False(trialReport.GetProperty("promotedStrategy").GetBoolean());
        Assert.True(trialReport.GetProperty("succeededCount").GetInt32() >= 2);
        Assert.NotNull(trace);
        Assert.Contains(trace!.Events, item =>
            item.Kind == KernelTraceEventKind.ProposalReviewed
            && item.Message.Contains("Adaptive candidate trial", StringComparison.Ordinal)
            && item.Data.GetProperty("executedRuntime").GetBoolean() is false
            && item.Data.GetProperty("promotedStrategy").GetBoolean() is false);
    }

    private static StageGraphProposal CreateProposal(CoreIntent intent, StageGraph graph)
        => new(
            new KernelProposalId($"proposal.{graph.GraphId.Value}"),
            intent.IntentId,
            graph,
            new RiskProfile("test", requiresHumanGate: false),
            new KernelBudgetImpact(graph.Budgets, "candidate test"),
            new RollbackPlan($"rollback.{graph.GraphId.Value}", reversible: true),
            new EvaluationPlan($"evaluation.{graph.GraphId.Value}", new[] { "candidate.validation" }));

    private static StageGraph CreateGraph(
        CoreIntent intent,
        KernelBudget? budget = null,
        SideEffectLevel stageSideEffectLevel = SideEffectLevel.ReadOnly,
        SideEffectLevel graphSideEffectLevel = SideEffectLevel.ReadOnly)
    {
        var stage = new StageNode(
            new StageId("stage-candidate"),
            "core_loop",
            "candidate validation stage",
            new ContractRef("input", "1"),
            new ContractRef("output", "1"),
            new[] { "kernel.request_capability_call" },
            new[] { "module.core_loop" },
            new ModelRoutePolicy(
                routeCandidateIds: new[] { "route.default" },
                preferredRouteId: "route.default",
                candidates: new[] { new ModelRouteCandidateBinding("route.default", "provider.module", "provider", "model") }),
            new ContextPolicy(maxInputTokens: 128),
            stageSideEffectLevel,
            new KernelBudget(tokenBudget: 128, timeBudgetMs: 1_000, toolCallBudget: 1),
            new SuccessCriteria(new[] { "ok" }),
            new FailureHandlerRef("fail", mayRecover: true));

        return new StageGraph(
            new StageGraphId($"graph.candidate.{intent.IntentId.Value}"),
            "1",
            intent.IntentKind,
            stage.StageId,
            new[] { stage },
            Array.Empty<StageEdge>(),
            new GraphPolicySet(
                PolicyEnforcementMode.AllowListed,
                allowedKernelToolIds: new[] { "kernel.request_capability_call" },
                allowedCapabilityToolIds: new[] { "module.core_loop" },
                allowedModuleIds: new[] { "module.core_loop" },
                maxSideEffectLevel: graphSideEffectLevel,
                requiresHumanGate: false),
            budget ?? new KernelBudget(tokenBudget: 512, timeBudgetMs: 1_000, toolCallBudget: 1),
            new CheckpointRules(),
            new RecoveryRules(enabled: true, maxRecoveryAttempts: 1),
            new EvaluationRules(enabled: true, metricIds: new[] { "candidate.validation" }),
            new StageGraphMetadata("test", "adaptive-candidate-validation"));
    }

    private static TurnIntent CreateIntent(IReadOnlyList<string>? allowedToolIds = null)
        => new(
            new CoreIntentId("intent-candidate"),
            new KernelSubjectRef(new SessionId("session-candidate"), new ThreadId("thread-candidate")),
            new GovernanceEnvelope(
                "governance-candidate",
                allowedToolIds: allowedToolIds ?? new[] { "kernel.request_capability_call", "module.core_loop" },
                allowedModuleIds: new[] { "module.core_loop" },
                maxSideEffectLevel: SideEffectLevel.ReadOnly,
                requiresHumanGate: false),
            "input-candidate",
            new KernelBudget(tokenBudget: 1_024, timeBudgetMs: 10_000, retryBudget: 1, toolCallBudget: 1));

    private static TurnIntent CreateDefaultTurnIntent(
        IReadOnlyList<string>? extraAllowedToolIds = null,
        IReadOnlyList<string>? extraAllowedModuleIds = null)
        => new(
            new CoreIntentId("intent-default-turn"),
            new KernelSubjectRef(new SessionId("session-default"), new ThreadId("thread-default")),
            new GovernanceEnvelope(
                "governance-default",
                allowedToolIds: new[]
                {
                    "update_context_policy",
                    "request_capability_call",
                    "module.core_loop",
                    "read_file",
                    "list_dir",
                    "grep",
                    "glob",
                    "apply_patch",
                    "write",
                    "memory_search",
                    "artifacts",
                }.Concat(extraAllowedToolIds ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray(),
                allowedModuleIds: new[] { "kernel.default", "provider.default" }.Concat(extraAllowedModuleIds ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray(),
                maxSideEffectLevel: SideEffectLevel.ExternalNetwork,
                requiresHumanGate: false),
            "input-default-turn",
            new KernelBudget(tokenBudget: 1_024, timeBudgetMs: 10_000, retryBudget: 1, toolCallBudget: 1));

    private sealed class FixedAdaptiveOrchestrator : IAdaptiveOrchestrator
    {
        private readonly KernelProposalSet proposalSet;

        public FixedAdaptiveOrchestrator(KernelProposalSet proposalSet)
        {
            this.proposalSet = proposalSet;
        }

        public Task<KernelProposalSet> ProposeAsync(CoreIntent intent, KernelRunState state, KernelRunOptions options, CancellationToken cancellationToken = default)
            => Task.FromResult(proposalSet);

        public Task<KernelProposalSet> ReviseAsync(KernelValidationResult validationResult, KernelRunState state, KernelRunOptions options, CancellationToken cancellationToken = default)
            => Task.FromResult(new KernelProposalSet());
    }
}
