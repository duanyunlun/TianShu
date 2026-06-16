using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Provider;
using TianShu.Execution.Runtime;

namespace TianShu.Execution.Runtime.Tests;

public sealed class ContextPolicyExecutionTests
{
    [Fact]
    public async Task PrepareProviderInputAsync_ShouldApplyApprovedPolicyBudgetAndDroppedReasons()
    {
        var bridge = new ExecutionRuntimeContextPolicyBridge();
        var step = CreateModelStep();
        var approvedPolicy = CreateApprovedPolicy(step, new ContextPolicy(
            maxInputTokens: 60,
            allowedSourceKinds: new[]
            {
                ContextSourceKind.CurrentUserInput.ToString(),
                ContextSourceKind.LatestUserCorrection.ToString(),
                ContextSourceKind.ToolEvidence.ToString(),
                ContextSourceKind.ArtifactReference.ToString(),
                ContextSourceKind.ConversationHistory.ToString(),
            },
            sourceRules: new[]
            {
                new ContextSourceRule(ContextSourceKind.CurrentUserInput, priority: 0),
                new ContextSourceRule(ContextSourceKind.LatestUserCorrection, priority: 0),
                new ContextSourceRule(ContextSourceKind.ToolEvidence, priority: 10, requireEvidenceRef: true),
                new ContextSourceRule(ContextSourceKind.ArtifactReference, priority: 20, requireEvidenceRef: true),
                new ContextSourceRule(ContextSourceKind.ConversationHistory, priority: 90),
            },
            policyId: "context.policy.runtime"));
        var candidates = new[]
        {
            new ContextSourceCandidate("history-1", ContextSourceKind.ConversationHistory, "old history", 80),
            new ContextSourceCandidate("tool-1", ContextSourceKind.ToolEvidence, "tool evidence", 20, evidenceRef: "tool://call/1"),
            new ContextSourceCandidate("artifact-1", ContextSourceKind.ArtifactReference, "artifact evidence", 20, artifactRef: "artifact://a1"),
            new ContextSourceCandidate("user-1", ContextSourceKind.CurrentUserInput, "current user input", 10),
            new ContextSourceCandidate("correction-1", ContextSourceKind.LatestUserCorrection, "latest correction", 10, isLatestUserCorrection: true),
        };

        var result = await bridge.PrepareProviderInputAsync(step, CreateContext(), approvedPolicy, candidates, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(4, result.Inputs.Count);
        Assert.All(result.Inputs, input => Assert.IsType<TextProviderInputItem>(input));
        Assert.Equal(60, result.Report?.EstimatedIncludedTokens);
        Assert.Contains(result.Report!.IncludedSegments, segment => segment.SourceKind == ContextSourceKind.CurrentUserInput);
        Assert.Contains(result.Report.IncludedSegments, segment => segment.SourceKind == ContextSourceKind.LatestUserCorrection);
        var dropped = Assert.Single(result.Report.DroppedSegments);
        Assert.Equal("history-1", dropped.SegmentId);
        Assert.Equal(ContextDropReason.BudgetExceeded, dropped.Reason);
    }

    [Fact]
    public async Task PrepareProviderInputAsync_ShouldReferenceLowConfidenceHistoryAndDropEvidenceWithoutRefs()
    {
        var bridge = new ExecutionRuntimeContextPolicyBridge();
        var step = CreateModelStep();
        var approvedPolicy = CreateApprovedPolicy(step, new ContextPolicy(
            maxInputTokens: 80,
            allowedSourceKinds: new[] { ContextSourceKind.ConversationHistory.ToString(), ContextSourceKind.ToolEvidence.ToString() },
            sourceRules: new[]
            {
                new ContextSourceRule(ContextSourceKind.ConversationHistory, priority: 20, minConfidence: 0.8m),
                new ContextSourceRule(ContextSourceKind.ToolEvidence, priority: 10, requireEvidenceRef: true),
            },
            policyId: "context.policy.low-confidence"));
        var candidates = new[]
        {
            new ContextSourceCandidate("history-low", ContextSourceKind.ConversationHistory, "uncertain prior memory", 70, confidence: 0.3m, evidenceRef: "memory://record/low"),
            new ContextSourceCandidate("tool-missing-ref", ContextSourceKind.ToolEvidence, "tool evidence without ref", 10),
        };

        var result = await bridge.PrepareProviderInputAsync(step, CreateContext(), approvedPolicy, candidates, CancellationToken.None);

        Assert.True(result.Success);
        var input = Assert.IsType<TextProviderInputItem>(Assert.Single(result.Inputs));
        Assert.Contains("[reference-only:ConversationHistory]", input.Text, StringComparison.Ordinal);
        Assert.Equal(ContextProjectionMode.ReferenceOnly, Assert.Single(result.Report!.IncludedSegments).ProjectionMode);
        var dropped = Assert.Single(result.Report.DroppedSegments);
        Assert.Equal("tool-missing-ref", dropped.SegmentId);
        Assert.Equal(ContextDropReason.MissingEvidenceRef, dropped.Reason);
    }

    [Fact]
    public async Task PrepareProviderInputAsync_ShouldDropExcludedSourcesBeforeProviderInput()
    {
        var bridge = new ExecutionRuntimeContextPolicyBridge();
        var step = CreateModelStep();
        var approvedPolicy = CreateApprovedPolicy(step, new ContextPolicy(
            maxInputTokens: 80,
            allowedSourceKinds: new[] { ContextSourceKind.CurrentUserInput.ToString(), ContextSourceKind.ConversationHistory.ToString() },
            sourceRules: new[]
            {
                new ContextSourceRule(ContextSourceKind.CurrentUserInput, priority: 0),
                new ContextSourceRule(ContextSourceKind.ConversationHistory, priority: 10, projectionMode: ContextProjectionMode.Excluded),
            },
            policyId: "context.policy.excluded"));
        var candidates = new[]
        {
            new ContextSourceCandidate("history-excluded", ContextSourceKind.ConversationHistory, "excluded history must not leak", 10),
            new ContextSourceCandidate("user-1", ContextSourceKind.CurrentUserInput, "current user input", 10),
        };

        var result = await bridge.PrepareProviderInputAsync(step, CreateContext(), approvedPolicy, candidates, CancellationToken.None);

        Assert.True(result.Success);
        var input = Assert.IsType<TextProviderInputItem>(Assert.Single(result.Inputs));
        Assert.Equal("current user input", input.Text);
        var dropped = Assert.Single(result.Report!.DroppedSegments);
        Assert.Equal("history-excluded", dropped.SegmentId);
        Assert.Equal(ContextDropReason.PolicyExcluded, dropped.Reason);
    }

    [Fact]
    public async Task PrepareProviderInputAsync_ShouldRejectPolicySourceMismatch()
    {
        var bridge = new ExecutionRuntimeContextPolicyBridge();
        var step = CreateModelStep();
        var approvedPolicy = new ApprovedContextPolicy(
            new ContextPolicy(maxInputTokens: 32),
            SourceIntentId,
            new StageGraphId("graph-other"),
            SourceStageId,
            SourceKernelOperationId);

        var result = await bridge.PrepareProviderInputAsync(
            step,
            CreateContext(),
            approvedPolicy,
            new[] { new ContextSourceCandidate("user-1", ContextSourceKind.CurrentUserInput, "hello", 1) },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("context_policy_source_mismatch", result.Failure?.Code);
        Assert.Null(result.Report);
    }

    [Fact]
    public async Task PrepareProviderInputAsync_ShouldRejectMissingBudgetWhenPolicyFailsClosed()
    {
        var bridge = new ExecutionRuntimeContextPolicyBridge();
        var step = CreateModelStep();
        var approvedPolicy = CreateApprovedPolicy(step, new ContextPolicy(maxInputTokens: 0, failClosed: true));

        var result = await bridge.PrepareProviderInputAsync(
            step,
            CreateContext(),
            approvedPolicy,
            new[] { new ContextSourceCandidate("user-1", ContextSourceKind.CurrentUserInput, "hello", 1) },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("context_policy_missing_budget", result.Failure?.Code);
    }

    private static ApprovedContextPolicy CreateApprovedPolicy(ModelInvocationStep step, ContextPolicy policy)
        => new(
            policy,
            step.SourceIntentId,
            step.SourceGraphId,
            step.SourceStageId,
            step.SourceKernelOperationId,
            validationRefs: new[] { "trace://context-policy/approved" });

    private static ModelInvocationStep CreateModelStep()
        => new(
            "model-context-step",
            SourceIntentId,
            SourceGraphId,
            SourceStageId,
            SourceKernelOperationId,
            "provider.module",
            new ModelRoutePolicy(new[] { "provider-route" }, "provider-route"),
            new ProviderInvocationRequest(
                new ExecutionId("execution-context-policy"),
                "provider-key",
                "model-name",
                new ProviderConversationContext(),
                new ProviderInputItem[] { new TextProviderInputItem("placeholder") }),
            Permission,
            new SideEffectProfile(SideEffectLevel.ExternalNetwork, affectedResources: new[] { "provider" }, requiresAudit: true),
            Budget,
            OutputContract,
            TracePolicy);

    private static ExecutionRuntimeContext CreateContext()
        => new(
            new ExecutionId("execution-context-policy"),
            new KernelRunId("kernel-run-context-policy"),
            new GovernanceEnvelope(
                "governance-context-policy",
                allowedModuleIds: new[] { "provider.module" },
                maxSideEffectLevel: SideEffectLevel.ExternalNetwork,
                requiresHumanGate: false));

    private static readonly CoreIntentId SourceIntentId = new("intent-context-policy");
    private static readonly StageGraphId SourceGraphId = new("graph-context-policy");
    private static readonly StageId SourceStageId = new("stage-context-policy");
    private static readonly KernelOperationId SourceKernelOperationId = new("operation-context-policy");
    private static readonly PermissionEnvelope Permission = new(
        scopes: new[] { "provider.invoke" },
        grants: new[] { "test" },
        requiresHumanGate: false,
        reason: "context policy test");
    private static readonly KernelBudget Budget = new(tokenBudget: 1_000, timeBudgetMs: 1_000, costBudget: 1, retryBudget: 1, toolCallBudget: 1);
    private static readonly ContractRef OutputContract = new("provider.output", "v1");
    private static readonly TracePolicy TracePolicy = new();
}
