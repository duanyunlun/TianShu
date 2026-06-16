using System.Text.Json;
using System.Xml.Linq;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Kernel.Tests;

public sealed class KernelContractTests
{
    [Fact]
    public void TurnIntent_RequiresGovernanceEnvelope()
    {
        var subject = new KernelSubjectRef(new SessionId("session-001"), new ThreadId("thread-001"));

        Assert.Throws<ArgumentNullException>(() => new TurnIntent(
            new CoreIntentId("intent-001"),
            subject,
            governance: null!,
            userInputRef: "input-001"));
    }

    [Fact]
    public void KernelBudget_RejectsNegativeValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new KernelBudget(tokenBudget: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new KernelBudget(timeBudgetMs: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new KernelBudget(costBudget: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new KernelBudget(retryBudget: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new KernelBudget(toolCallBudget: -1));
    }

    [Fact]
    public void DefaultPolicies_AreFailClosed()
    {
        var governance = new GovernanceEnvelope("envelope-001");
        var graphPolicies = new GraphPolicySet();
        var modelRoutePolicy = new ModelRoutePolicy();
        var sideEffect = new SideEffectProfile();

        Assert.True(governance.RequiresHumanGate);
        Assert.Equal(SideEffectLevel.Unspecified, governance.MaxSideEffectLevel);
        Assert.Equal(PolicyEnforcementMode.Deny, graphPolicies.EnforcementMode);
        Assert.True(graphPolicies.RequiresHumanGate);
        Assert.True(modelRoutePolicy.FailClosedWhenMissingCandidate);
        Assert.Equal(SideEffectLevel.Unspecified, sideEffect.Level);
        Assert.True(sideEffect.RequiresAudit);
    }

    [Fact]
    public void StageGraphProposal_RoundTripsConcreteContract()
    {
        var proposal = new StageGraphProposal(
            new KernelProposalId("proposal-001"),
            new CoreIntentId("intent-001"),
            CreateGraph(),
            new RiskProfile("low", requiresHumanGate: false),
            new KernelBudgetImpact(new KernelBudget(tokenBudget: 128), "small graph"),
            new RollbackPlan("rollback-001", reversible: true),
            new EvaluationPlan("eval-plan-001", new[] { "accuracy" }));

        var json = JsonSerializer.Serialize(proposal);
        var roundTripped = JsonSerializer.Deserialize<StageGraphProposal>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(new KernelProposalId("proposal-001"), roundTripped.ProposalId);
        Assert.Equal(KernelProposalKind.StageGraph, roundTripped.ProposalKind);
        Assert.Equal(new StageGraphId("graph-001"), roundTripped.Graph.GraphId);
        Assert.Equal("accuracy", Assert.Single(roundTripped.EvaluationPlan.MetricIds));
    }

    [Fact]
    public void KernelOperation_PreservesTypedSourceIdsAndProfiles()
    {
        var operation = new RequestCapabilityCallOperation(
            new KernelOperationId("operation-001"),
            new CoreIntentId("intent-001"),
            new StageId("stage-001"),
            "tool.shell",
            StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["command"] = "echo hi",
            }),
            new PermissionEnvelope(new[] { "workspace.read" }, requiresHumanGate: true),
            new SideEffectProfile(SideEffectLevel.ReadOnly));

        Assert.Equal(KernelOperationKind.RequestCapabilityCall, operation.OperationKind);
        Assert.Equal(new CoreIntentId("intent-001"), operation.SourceIntentId);
        Assert.Equal(new StageId("stage-001"), operation.SourceStageId);
        Assert.Equal("workspace.read", Assert.Single(operation.Permission.Scopes));
        Assert.Equal(SideEffectLevel.ReadOnly, operation.SideEffect.Level);
    }

    [Fact]
    public void StrategyAndEvaluation_DefaultStatesDoNotAutoPromote()
    {
        var strategy = new StrategyRecord(
            new StrategyId("strategy-001"),
            "Default graph",
            new StageGraphId("graph-001"));
        var evaluation = new KernelEvaluationResult(
            "evaluation-001",
            new KernelRunId("run-001"),
            new KernelTraceId("trace-001"),
            KernelReviewDecision.RequiresHumanGate);

        Assert.Equal(StrategyLifecycleState.Draft, strategy.LifecycleState);
        Assert.NotEqual(StrategyLifecycleState.Promoted, strategy.LifecycleState);
        Assert.Equal(KernelReviewDecision.RequiresHumanGate, evaluation.Decision);
    }

    [Fact]
    public void ContextPolicyContracts_ShouldCarryBudgetRulesAndDroppedReasons()
    {
        var policy = new ContextPolicy(
            maxInputTokens: 256,
            priorityRefs: new[] { "input-current" },
            allowedSourceKinds: new[] { ContextSourceKind.CurrentUserInput.ToString(), ContextSourceKind.ToolEvidence.ToString() },
            sourceRules: new[]
            {
                new ContextSourceRule(ContextSourceKind.CurrentUserInput, priority: 0),
                new ContextSourceRule(ContextSourceKind.ToolEvidence, priority: 10, requireEvidenceRef: true),
            },
            policyId: "context.policy.turn");
        var approved = new ApprovedContextPolicy(
            policy,
            new CoreIntentId("intent-001"),
            new StageGraphId("graph-001"),
            new StageId("stage-001"),
            new KernelOperationId("operation-001"),
            validationRefs: new[] { "trace://validation/context-policy" });
        var report = new ContextPolicyApplicationReport(
            policy.PolicyId,
            policy.MaxInputTokens,
            estimatedTotalTokens: 300,
            estimatedIncludedTokens: 120,
            includedSegments: new[]
            {
                new MaterializedContextSegment("input-current", ContextSourceKind.CurrentUserInput, ContextProjectionMode.Full, 40),
            },
            droppedSegments: new[]
            {
                new DroppedContextSegment("history-low", ContextSourceKind.ConversationHistory, ContextDropReason.BudgetExceeded, 180),
            });

        Assert.Equal("context.policy.turn", policy.PolicyId);
        Assert.True(policy.FailClosed);
        Assert.True(policy.RequireEvidenceRefs);
        Assert.Equal(new StageGraphId("graph-001"), approved.SourceGraphId);
        Assert.Equal("trace://validation/context-policy", Assert.Single(approved.ValidationRefs));
        Assert.Equal(ContextDropReason.BudgetExceeded, Assert.Single(report.DroppedSegments).Reason);
        Assert.Equal(ContextProjectionMode.Full, Assert.Single(report.IncludedSegments).ProjectionMode);
    }

    [Fact]
    public void ModelRoutePolicyContracts_ShouldCarryApprovedCandidatesAndReportWithoutSecretValues()
    {
        var candidate = new ModelRouteCandidateBinding(
            "candidate-openai-gpt5",
            "provider.openai",
            "openai",
            "gpt-5",
            candidateIndex: 0,
            protocol: "responses",
            endpointRef: "endpoint://provider/openai",
            secretRef: "secret://env/OPENAI_API_KEY",
            capabilities: new[] { "code" });
        var policy = new ModelRoutePolicy(
            routeCandidateIds: new[] { candidate.CandidateId },
            preferredRouteId: candidate.CandidateId,
            policyId: "model.route.policy.turn",
            routeKind: "coding",
            candidates: new[] { candidate });
        var approved = new ApprovedModelRoutePolicy(
            policy,
            new CoreIntentId("intent-001"),
            new StageGraphId("graph-001"),
            new StageId("stage-001"),
            new KernelOperationId("operation-001"),
            validationRefs: new[] { "trace://validation/model-route" });
        var report = new ModelRoutePolicyApplicationReport(
            policy.PolicyId,
            policy.RouteKind,
            candidate.CandidateId,
            candidate.ProviderModuleId,
            candidate.ProviderKey,
            candidate.Model,
            candidate.CandidateIndex,
            candidate.Protocol,
            candidate.EndpointRef,
            diagnosticsCorrelationId: "model-route-001");

        Assert.Equal("model.route.policy.turn", policy.PolicyId);
        Assert.Equal("coding", policy.RouteKind);
        Assert.True(policy.FailClosedWhenMissingCandidate);
        Assert.Equal(candidate, Assert.Single(policy.Candidates));
        Assert.Equal(new StageGraphId("graph-001"), approved.SourceGraphId);
        Assert.Equal("trace://validation/model-route", Assert.Single(approved.ValidationRefs));
        Assert.Equal("provider.openai", report.ProviderModuleId);
        Assert.Equal("openai", report.ProviderKey);
        Assert.Equal("gpt-5", report.Model);
        Assert.Equal("responses", report.Protocol);
        Assert.DoesNotContain("API_KEY_VALUE", report.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ContractsKernelProject_DoesNotReferenceImplementationProjects()
    {
        var projectPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "Contracts",
            "TianShu.Contracts.Kernel",
            "TianShu.Contracts.Kernel.csproj"));
        var document = XDocument.Load(projectPath);
        var references = document
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();

        Assert.All(references, reference =>
        {
            Assert.Contains("TianShu.Contracts.", reference);
            Assert.DoesNotContain("Core", reference, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Execution.Runtime", reference, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Hosting", reference, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Provider.", reference, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Tools.", reference, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static StageGraph CreateGraph()
    {
        var stageId = new StageId("stage-001");
        var contract = new ContractRef("contract.text", "1");
        var stage = new StageNode(
            stageId,
            "model",
            "answer user",
            contract,
            contract,
            new[] { "kernel.select_model_route" },
            new[] { "tool.model.invoke" },
            new ModelRoutePolicy(new[] { "route.default" }, "route.default"),
            new ContextPolicy(maxInputTokens: 256),
            SideEffectLevel.ReadOnly,
            new KernelBudget(tokenBudget: 256, timeBudgetMs: 1_000),
            new SuccessCriteria(new[] { "assistant_message" }),
            new FailureHandlerRef("fail-closed"));

        return new StageGraph(
            new StageGraphId("graph-001"),
            "1",
            CoreIntentKind.Turn,
            stageId,
            new[] { stage },
            Array.Empty<StageEdge>(),
            new GraphPolicySet(
                PolicyEnforcementMode.AllowListed,
                allowedKernelToolIds: new[] { "kernel.select_model_route" },
                allowedCapabilityToolIds: new[] { "tool.model.invoke" },
                maxSideEffectLevel: SideEffectLevel.ReadOnly,
                requiresHumanGate: false),
            new KernelBudget(tokenBudget: 256, timeBudgetMs: 1_000),
            new CheckpointRules(enabled: true, new[] { stageId }),
            new RecoveryRules(enabled: true, maxRecoveryAttempts: 1, new[] { "retry" }),
            new EvaluationRules(enabled: true, new[] { "accuracy" }),
            new StageGraphMetadata("kernel", "unit-test"));
    }
}
