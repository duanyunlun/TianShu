using System.Text.Json;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Kernel.AdaptiveAcceptance;
using TianShu.Kernel.Abstractions;
using TianShu.Kernel.Interpretation;
using TianShu.Kernel.Validation;

namespace TianShu.Kernel.Tests;

public sealed class AdaptiveKernelCandidateMapperTests
{
    [Fact]
    public async Task MapStageGraph_ShouldProduceRealContractAndDryRunThroughKernel()
    {
        var candidate = CreateGraphCandidate();
        var graph = AdaptiveKernelCandidateMapper.MapStageGraph(candidate);
        var intent = CreateIntent(graph);
        var validator = new KernelValidator();
        var interpreter = new StageGraphInterpreter();

        var validation = await validator.ValidateGraphAsync(graph, new KernelValidationContext(intent, graph: graph));
        var plan = await interpreter.InterpretAsync(
            graph,
            new KernelInterpreterContext(
                intent,
                new KernelRunState(new KernelRunId("run-candidate-mapper"), intent.IntentId, selectedGraphId: graph.GraphId),
                new KernelRunOptions(runId: new KernelRunId("run-candidate-mapper"), requireHumanGate: false)));

        Assert.IsType<StageGraph>(graph);
        Assert.True(validation.IsApproved, string.Join(Environment.NewLine, validation.Issues.Select(static issue => $"{issue.Code}: {issue.Message}")));
        Assert.Equal(graph.GraphId, plan.SourceGraphId);
        Assert.Equal(graph.Stages.Count, plan.Steps.Count);
        Assert.All(plan.Steps, step => Assert.Equal(graph.GraphId, step.SourceGraphId));
    }

    [Fact]
    public async Task ApplyPatch_ShouldReturnStageGraphThatStillPassesValidatorAndInterpreter()
    {
        var baseGraph = AdaptiveKernelCandidateMapper.MapStageGraph(CreateGraphCandidate());
        var patch = new AdaptiveKernelStageGraphPatchCandidate
        {
            ProposalId = "proposal.patch.acceptance",
            TargetGraphId = baseGraph.GraphId.Value,
            Operations =
            [
                new AdaptiveKernelPatchOperationCandidate
                {
                    OperationKind = "replace_stage",
                    TargetStageId = "stage-2",
                    Payload = JsonSerializer.SerializeToElement(new AdaptiveKernelStageCandidate
                    {
                        StageId = "stage-2",
                        Kind = "diagnostics.emit_trace",
                        Objective = "emit patched diagnostic trace",
                        CapabilityToolIds = ["diagnostics.emit_trace"],
                        SideEffectLevel = "None",
                        Budget = new AdaptiveKernelBudgetCandidate { TokenBudget = 256, TimeBudgetMs = 10_000, ToolCallBudget = 0 },
                    }),
                },
            ],
        };

        var proposal = AdaptiveKernelCandidateMapper.MapPatchProposal(patch, new CoreIntentId("intent-candidate-patch"));
        var patchedGraph = AdaptiveKernelCandidateMapper.ApplyPatch(baseGraph, patch);
        var intent = CreateIntent(patchedGraph);
        var validator = new KernelValidator();
        var interpreter = new StageGraphInterpreter();

        var validation = await validator.ValidateGraphAsync(patchedGraph, new KernelValidationContext(intent, graph: patchedGraph));
        var plan = await interpreter.InterpretAsync(
            patchedGraph,
            new KernelInterpreterContext(
                intent,
                new KernelRunState(new KernelRunId("run-candidate-patch"), intent.IntentId, selectedGraphId: patchedGraph.GraphId),
                new KernelRunOptions(runId: new KernelRunId("run-candidate-patch"), requireHumanGate: false)));

        Assert.IsType<StageGraphPatchProposal>(proposal);
        Assert.IsType<StageGraph>(patchedGraph);
        Assert.True(validation.IsApproved, string.Join(Environment.NewLine, validation.Issues.Select(static issue => $"{issue.Code}: {issue.Message}")));
        Assert.Equal("emit patched diagnostic trace", patchedGraph.Stages.Single(stage => stage.StageId.Value == "stage-2").Objective);
        Assert.Equal(patchedGraph.Stages.Count, plan.Steps.Count);
    }

    [Fact]
    public void MapRecoveryCheckpointAndContextPolicy_ShouldReturnFormalContracts()
    {
        var sourceIntentId = new CoreIntentId("intent-candidate-support");

        var recovery = AdaptiveKernelCandidateMapper.MapRecoveryProposal(
            new AdaptiveKernelRecoveryProposalCandidate
            {
                ProposalId = "proposal.recovery.acceptance",
                RecoveryKind = "retry_with_reduced_context",
                ActionRefs = ["action.trim_context"],
                RequiresHumanGate = true,
            },
            sourceIntentId);
        var checkpoint = AdaptiveKernelCandidateMapper.MapCheckpointProposal(
            new AdaptiveKernelCheckpointProposalCandidate
            {
                OperationId = "operation.checkpoint.acceptance",
                SourceStageId = "stage-1",
                CheckpointRef = "checkpoint.acceptance.stage-1",
            },
            sourceIntentId);
        var contextPolicy = AdaptiveKernelCandidateMapper.MapContextPolicy(
            new AdaptiveKernelContextPolicyCandidate
            {
                MaxInputTokens = 1024,
                AllowedSourceKinds = ["CurrentUserInput", "ToolEvidence"],
                PolicyId = "context.policy.acceptance",
            });

        Assert.IsType<RecoveryProposal>(recovery);
        Assert.IsType<CheckpointProposalOperation>(checkpoint);
        Assert.IsType<ContextPolicy>(contextPolicy);
        Assert.Equal("retry_with_reduced_context", recovery.RecoveryPlan.RecoveryKind);
        Assert.Equal("checkpoint.acceptance.stage-1", checkpoint.CheckpointRef);
        Assert.Equal(1024, contextPolicy.MaxInputTokens);
    }

    [Fact]
    public void MapStageGraph_ShouldFailClosedForInvalidCandidate()
    {
        var candidate = CreateGraphCandidate() with
        {
            Stages = [],
        };

        var error = Assert.Throws<InvalidDataException>(() => AdaptiveKernelCandidateMapper.MapStageGraph(candidate));

        Assert.Contains("Stages", error.Message, StringComparison.Ordinal);
    }

    private static AdaptiveKernelStageGraphCandidate CreateGraphCandidate()
        => new()
        {
            GraphId = "graph-candidate-001",
            Version = "1",
            IntentKind = "Turn",
            EntryStageId = "stage-1",
            Stages =
            [
                new AdaptiveKernelStageCandidate
                {
                    StageId = "stage-1",
                    Kind = "model.invoke.initial",
                    Objective = "invoke the initial model step",
                    CapabilityToolIds = ["model.invoke.initial"],
                    SideEffectLevel = "None",
                    Budget = new AdaptiveKernelBudgetCandidate { TokenBudget = 512, TimeBudgetMs = 20_000, ToolCallBudget = 0 },
                },
                new AdaptiveKernelStageCandidate
                {
                    StageId = "stage-2",
                    Kind = "diagnostics.emit_trace",
                    Objective = "emit diagnostic trace",
                    CapabilityToolIds = ["diagnostics.emit_trace"],
                    SideEffectLevel = "None",
                    Budget = new AdaptiveKernelBudgetCandidate { TokenBudget = 256, TimeBudgetMs = 10_000, ToolCallBudget = 0 },
                },
            ],
            Edges =
            [
                new AdaptiveKernelEdgeCandidate
                {
                    EdgeId = "edge-1-2",
                    FromStageId = "stage-1",
                    ToStageId = "stage-2",
                    TransitionKind = "Success",
                },
            ],
            Policies = new AdaptiveKernelPolicyCandidate
            {
                AllowedCapabilityToolIds = ["model.invoke.initial", "diagnostics.emit_trace"],
                MaxSideEffectLevel = "None",
                RequiresHumanGate = false,
            },
            Budgets = new AdaptiveKernelBudgetCandidate { TokenBudget = 768, TimeBudgetMs = 30_000, ToolCallBudget = 0 },
            CheckpointRules = new AdaptiveKernelCheckpointRulesCandidate { Enabled = true, RequiredStageIds = ["stage-1"] },
            RecoveryRules = new AdaptiveKernelRecoveryRulesCandidate { Enabled = true, MaxRecoveryAttempts = 1 },
            EvaluationRules = new AdaptiveKernelEvaluationRulesCandidate { Enabled = true, MetricIds = ["acceptance.candidate.validation"] },
        };

    private static TurnIntent CreateIntent(StageGraph graph)
        => new(
            new CoreIntentId("intent-candidate-mapper"),
            new KernelSubjectRef(new SessionId("session-candidate-mapper"), new ThreadId("thread-candidate-mapper"), turnId: new TurnId("turn-candidate-mapper")),
            new GovernanceEnvelope(
                "governance-candidate-mapper",
                allowedToolIds: graph.Policies.AllowedCapabilityToolIds,
                maxSideEffectLevel: graph.Policies.MaxSideEffectLevel,
                requiresHumanGate: false),
            "candidate-mapper-user-input",
            graph.Budgets);
}
