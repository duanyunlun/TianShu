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

    [Fact]
    public async Task PrepareProviderInputAsync_ShouldProjectSupersedeDecisionAsReferenceOnlyTraceableSegment()
    {
        var bridge = new ExecutionRuntimeContextPolicyBridge();
        var step = CreateModelStep();
        var approvedPolicy = CreateApprovedPolicy(step, new ContextPolicy(
            maxInputTokens: 80,
            allowedSourceKinds: new[] { ContextSourceKind.MemoryRecord.ToString() },
            sourceRules: new[]
            {
                new ContextSourceRule(ContextSourceKind.MemoryRecord, priority: 10),
            },
            policyId: "context.policy.supersede"));
        var candidates = new[]
        {
            new ContextSourceCandidate(
                "memory-old",
                ContextSourceKind.MemoryRecord,
                "older memory that should only be referenced",
                40,
                evidenceRef: "memory://old",
                metadata: Metadata(
                    ("context.layer", StructuredValue.FromString("L2")),
                    ("context.supersedeDecisionId", StructuredValue.FromString("supersede-001")),
                    ("context.supersedeReplacementSegmentId", StructuredValue.FromString("memory-new")),
                    ("context.supersedeDisposition", StructuredValue.FromString("ReferenceOnlySuperseded")),
                    ("context.supersedeReason", StructuredValue.FromString("newer memory wins")),
                    ("context.auditRef", StructuredValue.FromString("audit://context/supersede")),
                    ("context.traceRef", StructuredValue.FromString("trace://context/memory-old")))),
        };

        var result = await bridge.PrepareProviderInputAsync(step, CreateContext(), approvedPolicy, candidates, CancellationToken.None);

        Assert.True(result.Success);
        var segment = Assert.Single(result.Report!.IncludedSegments);
        Assert.Equal("memory-old", segment.SegmentId);
        Assert.Equal(ContextProjectionMode.ReferenceOnly, segment.ProjectionMode);
        Assert.Equal("L2", segment.SourceLayer);
        Assert.Equal("supersede-001", segment.SupersedeDecisionRef);
        Assert.Equal(ContextSupersedeDisposition.ReferenceOnlySuperseded, segment.SupersedeDisposition);
        Assert.Equal("audit://context/supersede", segment.AuditRef);
        Assert.Equal("trace://context/memory-old", segment.TraceRef);
        var supersede = Assert.Single(result.Report.SupersedeDecisions);
        Assert.Equal("memory-new", supersede.ReplacementSegmentId);
        Assert.Equal("newer memory wins", supersede.Reason);
    }

    [Fact]
    public async Task PrepareProviderInputAsync_ShouldReportCompressionCandidateAndCheckpointRefs()
    {
        var bridge = new ExecutionRuntimeContextPolicyBridge();
        var step = CreateModelStep();
        var approvedPolicy = CreateApprovedPolicy(step, new ContextPolicy(
            maxInputTokens: 120,
            allowedSourceKinds: new[] { ContextSourceKind.ConversationHistory.ToString() },
            sourceRules: new[]
            {
                new ContextSourceRule(ContextSourceKind.ConversationHistory, priority: 10, projectionMode: ContextProjectionMode.Summary, maxTokens: 50),
            },
            policyId: "context.policy.compression"));
        var candidates = new[]
        {
            new ContextSourceCandidate(
                "history-compact",
                ContextSourceKind.ConversationHistory,
                "long prior conversation",
                100,
                evidenceRef: "history://turns/1-20",
                metadata: Metadata(
                    ("context.layer", StructuredValue.FromString("L3")),
                    ("context.compressionCandidateId", StructuredValue.FromString("compress-001")),
                    ("context.compressionTargetTokens", StructuredValue.FromNumber("32")),
                    ("context.compressionReversible", StructuredValue.FromBoolean(true)),
                    ("context.compressionReason", StructuredValue.FromString("conversation history compaction")),
                    ("context.compressionArtifactRef", StructuredValue.FromString("artifact://context/history-summary")),
                    ("context.compressionCheckpointRef", StructuredValue.FromString("checkpoint-001")),
                    ("context.auditRef", StructuredValue.FromString("audit://context/compression")))),
        };

        var result = await bridge.PrepareProviderInputAsync(step, CreateContext(), approvedPolicy, candidates, CancellationToken.None);

        Assert.True(result.Success);
        var segment = Assert.Single(result.Report!.IncludedSegments);
        Assert.Equal("checkpoint-001", segment.CompressionCheckpointRef);
        Assert.Equal("audit://context/compression", segment.AuditRef);
        var compression = Assert.Single(result.Report.CompressionCandidates);
        Assert.Equal("compress-001", compression.CandidateId);
        Assert.Equal(32, compression.TargetEstimatedTokens);
        Assert.True(compression.Reversible);
        var checkpoint = Assert.Single(result.Report.CompressionCheckpoints);
        Assert.Equal("checkpoint-001", checkpoint.CheckpointId);
        Assert.Equal("artifact://context/history-summary", checkpoint.CompressedArtifactRef);
        Assert.Equal("audit://context/compression", checkpoint.AuditRef);
    }

    [Fact]
    public async Task PrepareProviderInputAsync_ShouldReportEstimatedUsageSignalAndPressureTriggers()
    {
        var bridge = new ExecutionRuntimeContextPolicyBridge();
        var step = CreateModelStep();
        var approvedPolicy = CreateApprovedPolicy(step, new ContextPolicy(
            maxInputTokens: 64,
            allowedSourceKinds: new[] { ContextSourceKind.CurrentUserInput.ToString(), ContextSourceKind.ConversationHistory.ToString() },
            sourceRules: new[]
            {
                new ContextSourceRule(ContextSourceKind.CurrentUserInput, priority: 0),
                new ContextSourceRule(ContextSourceKind.ConversationHistory, priority: 10),
            },
            policyId: "context.policy.usage"));
        var context = CreateContext(Metadata(
            ("context.planId", StructuredValue.FromString("plan-usage")),
            ("context.auditRef", StructuredValue.FromString("audit://context/usage")),
            ("context.usage.source", StructuredValue.FromString("runtime.estimated")),
            ("context.usage.estimated", StructuredValue.FromBoolean(true)),
            ("context.usage.inputTokens", StructuredValue.FromNumber("90")),
            ("context.usage.modelContextWindow", StructuredValue.FromNumber("128")),
            ("context.usage.missingReason", StructuredValue.FromString("provider_usage_missing")),
            ("context.triggerRef", StructuredValue.FromString("trigger-budget"))));
        var candidates = new[]
        {
            new ContextSourceCandidate("user-1", ContextSourceKind.CurrentUserInput, "current question", 10),
            new ContextSourceCandidate("history-large", ContextSourceKind.ConversationHistory, "large history", 90),
        };

        var result = await bridge.PrepareProviderInputAsync(step, context, approvedPolicy, candidates, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("plan-usage", result.Report!.PlanId);
        Assert.Equal("audit://context/usage", result.Report.AuditRef);
        Assert.NotNull(result.Report.UsageSignal);
        Assert.True(result.Report.UsageSignal!.Estimated);
        Assert.Equal(90, result.Report.UsageSignal.InputTokens);
        Assert.Equal(128, result.Report.UsageSignal.ModelContextWindow);
        Assert.Equal("provider_usage_missing", result.Report.UsageSignal.MissingReason);
        Assert.Contains("trigger-budget", result.Report.TriggerRefs);
        Assert.Contains(result.Report.TriggerRefs, static triggerRef => triggerRef.EndsWith(":missing-usage", StringComparison.Ordinal));
        Assert.Contains(result.Report.DroppedSegments, static dropped => dropped.Reason == ContextDropReason.BudgetExceeded);
    }

    [Fact]
    public async Task PrepareProviderInputAsync_ShouldRecordTokenThresholdTriggerAndBudgetDegradation()
    {
        var bridge = new ExecutionRuntimeContextPolicyBridge();
        var step = CreateModelStep();
        var approvedPolicy = CreateApprovedPolicy(step, new ContextPolicy(
            maxInputTokens: 50,
            allowedSourceKinds: new[] { ContextSourceKind.CurrentUserInput.ToString(), ContextSourceKind.ConversationHistory.ToString() },
            sourceRules: new[]
            {
                new ContextSourceRule(ContextSourceKind.CurrentUserInput, priority: 0),
                new ContextSourceRule(ContextSourceKind.ConversationHistory, priority: 90),
            },
            policyId: "context.policy.threshold"));
        var candidates = new[]
        {
            new ContextSourceCandidate("history-large", ContextSourceKind.ConversationHistory, "large history", 80),
            new ContextSourceCandidate("user-1", ContextSourceKind.CurrentUserInput, "current question", 10),
        };

        var result = await bridge.PrepareProviderInputAsync(step, CreateContext(), approvedPolicy, candidates, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(90, result.Report!.EstimatedTotalTokens);
        Assert.Equal(10, result.Report.EstimatedIncludedTokens);
        Assert.Contains(result.Report.TriggerRefs, static triggerRef => triggerRef.EndsWith(":budget", StringComparison.Ordinal));
        var dropped = Assert.Single(result.Report.DroppedSegments);
        Assert.Equal("history-large", dropped.SegmentId);
        Assert.Equal(ContextDropReason.BudgetExceeded, dropped.Reason);
        Assert.Equal("L3", dropped.SourceLayer);
        Assert.NotNull(dropped.TriggerRef);
        var degradation = Assert.Single(result.Report.DegradationDecisions);
        Assert.Equal("history-large", degradation.SegmentId);
        Assert.Equal(ContextDropReason.BudgetExceeded, degradation.DropReason);
    }

    [Fact]
    public async Task PrepareProviderInputAsync_ShouldPreserveReversibleCompressionEvidenceChain()
    {
        var bridge = new ExecutionRuntimeContextPolicyBridge();
        var step = CreateModelStep();
        var approvedPolicy = CreateApprovedPolicy(step, new ContextPolicy(
            maxInputTokens: 120,
            allowedSourceKinds: new[] { ContextSourceKind.ConversationHistory.ToString() },
            sourceRules: new[]
            {
                new ContextSourceRule(ContextSourceKind.ConversationHistory, priority: 10, projectionMode: ContextProjectionMode.Summary, maxTokens: 48),
            },
            policyId: "context.policy.reversible-compression"));
        var candidates = new[]
        {
            new ContextSourceCandidate(
                "history-reversible",
                ContextSourceKind.ConversationHistory,
                "long prior conversation",
                96,
                evidenceRef: "history://turns/21-40",
                metadata: Metadata(
                    ("context.compressionCandidateId", StructuredValue.FromString("compress-reversible")),
                    ("context.compressionTargetTokens", StructuredValue.FromNumber("24")),
                    ("context.compressionReversible", StructuredValue.FromBoolean(true)),
                    ("context.compressionReason", StructuredValue.FromString("reversible history compaction")),
                    ("context.compressionArtifactRef", StructuredValue.FromString("artifact://context/history-reversible-summary")),
                    ("context.compressionCheckpointRef", StructuredValue.FromString("checkpoint-reversible")),
                    ("context.auditRef", StructuredValue.FromString("audit://context/reversible-compression")))),
        };

        var result = await bridge.PrepareProviderInputAsync(step, CreateContext(), approvedPolicy, candidates, CancellationToken.None);

        Assert.True(result.Success);
        var compression = Assert.Single(result.Report!.CompressionCandidates);
        Assert.Equal("compress-reversible", compression.CandidateId);
        Assert.Equal("history-reversible", Assert.Single(compression.SourceSegmentIds));
        Assert.Equal("history://turns/21-40", compression.EvidenceRef);
        Assert.Equal("artifact://context/history-reversible-summary", compression.ArtifactRef);
        Assert.True(compression.Reversible);
        var checkpoint = Assert.Single(result.Report.CompressionCheckpoints);
        Assert.Equal("checkpoint-reversible", checkpoint.CheckpointId);
        Assert.Equal("history-reversible", Assert.Single(checkpoint.SourceSegmentRefs));
        Assert.Equal("artifact://context/history-reversible-summary", checkpoint.CompressedArtifactRef);
        Assert.True(checkpoint.Reversible);
        Assert.Equal("audit://context/reversible-compression", checkpoint.AuditRef);
    }

    [Fact]
    public async Task PrepareProviderInputAsync_ShouldPrioritizeLatestCorrectionOverSupersededMemory()
    {
        var bridge = new ExecutionRuntimeContextPolicyBridge();
        var step = CreateModelStep();
        var approvedPolicy = CreateApprovedPolicy(step, new ContextPolicy(
            maxInputTokens: 80,
            allowedSourceKinds: new[] { ContextSourceKind.LatestUserCorrection.ToString(), ContextSourceKind.MemoryRecord.ToString() },
            sourceRules: new[]
            {
                new ContextSourceRule(ContextSourceKind.LatestUserCorrection, priority: 0),
                new ContextSourceRule(ContextSourceKind.MemoryRecord, priority: 20),
            },
            policyId: "context.policy.supersede-priority"));
        var candidates = new[]
        {
            new ContextSourceCandidate(
                "memory-old",
                ContextSourceKind.MemoryRecord,
                "old preference is blue",
                30,
                evidenceRef: "memory://old",
                metadata: Metadata(
                    ("context.supersedeDecisionId", StructuredValue.FromString("supersede-priority")),
                    ("context.supersedeReplacementSegmentId", StructuredValue.FromString("correction-latest")),
                    ("context.supersedeDisposition", StructuredValue.FromString("ReferenceOnlySuperseded")),
                    ("context.supersedeReason", StructuredValue.FromString("latest user correction wins")))),
            new ContextSourceCandidate(
                "correction-latest",
                ContextSourceKind.LatestUserCorrection,
                "latest preference is green",
                20,
                isLatestUserCorrection: true),
        };

        var result = await bridge.PrepareProviderInputAsync(step, CreateContext(), approvedPolicy, candidates, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("latest preference is green", Assert.IsType<TextProviderInputItem>(result.Inputs[0]).Text);
        var correction = Assert.Single(result.Report!.IncludedSegments, static segment => segment.SegmentId == "correction-latest");
        Assert.Equal(ContextProjectionMode.Full, correction.ProjectionMode);
        var oldMemory = Assert.Single(result.Report.IncludedSegments, static segment => segment.SegmentId == "memory-old");
        Assert.Equal(ContextProjectionMode.ReferenceOnly, oldMemory.ProjectionMode);
        Assert.Equal("supersede-priority", oldMemory.SupersedeDecisionRef);
        Assert.Equal(ContextSupersedeDisposition.ReferenceOnlySuperseded, oldMemory.SupersedeDisposition);
    }

    [Fact]
    public async Task PrepareProviderInputAsync_ShouldFailClosedWhenProtectedSegmentExceedsBudget()
    {
        var bridge = new ExecutionRuntimeContextPolicyBridge();
        var step = CreateModelStep();
        var approvedPolicy = CreateApprovedPolicy(step, new ContextPolicy(
            maxInputTokens: 8,
            allowedSourceKinds: new[] { ContextSourceKind.CurrentUserInput.ToString(), ContextSourceKind.ConversationHistory.ToString() },
            sourceRules: new[]
            {
                new ContextSourceRule(ContextSourceKind.CurrentUserInput, priority: 0),
                new ContextSourceRule(ContextSourceKind.ConversationHistory, priority: 10),
            },
            policyId: "context.policy.protected-budget"));
        var candidates = new[]
        {
            new ContextSourceCandidate("user-oversized", ContextSourceKind.CurrentUserInput, "oversized protected user input", 12),
            new ContextSourceCandidate("history-1", ContextSourceKind.ConversationHistory, "history", 4),
        };

        var result = await bridge.PrepareProviderInputAsync(step, CreateContext(), approvedPolicy, candidates, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("context_policy_protected_segment_budget_exceeded", result.Failure?.Code);
        Assert.Empty(result.Inputs);
        Assert.NotNull(result.Report);
        Assert.Empty(result.Report!.DroppedSegments);
        var degradation = Assert.Single(result.Report.DegradationDecisions);
        Assert.Equal("user-oversized", degradation.SegmentId);
        Assert.Equal(ContextDropReason.BudgetExceeded, degradation.DropReason);
    }

    [Fact]
    public async Task PrepareProviderInputAsync_ShouldIgnoreCompressionMetadataForProtectedSegments()
    {
        var bridge = new ExecutionRuntimeContextPolicyBridge();
        var step = CreateModelStep();
        var approvedPolicy = CreateApprovedPolicy(step, new ContextPolicy(
            maxInputTokens: 64,
            allowedSourceKinds: new[] { ContextSourceKind.LatestUserCorrection.ToString() },
            sourceRules: new[]
            {
                new ContextSourceRule(ContextSourceKind.LatestUserCorrection, priority: 0),
            },
            policyId: "context.policy.protected-compression"));
        var candidates = new[]
        {
            new ContextSourceCandidate(
                "correction-protected",
                ContextSourceKind.LatestUserCorrection,
                "do not compress this correction",
                20,
                isLatestUserCorrection: true,
                metadata: Metadata(
                    ("context.compressionCandidateId", StructuredValue.FromString("compress-protected")),
                    ("context.compressionArtifactRef", StructuredValue.FromString("artifact://context/protected-summary")),
                    ("context.compressionCheckpointRef", StructuredValue.FromString("checkpoint-protected")))),
        };

        var result = await bridge.PrepareProviderInputAsync(step, CreateContext(), approvedPolicy, candidates, CancellationToken.None);

        Assert.True(result.Success);
        var segment = Assert.Single(result.Report!.IncludedSegments);
        Assert.Equal("correction-protected", segment.SegmentId);
        Assert.Equal(ContextProjectionMode.Full, segment.ProjectionMode);
        Assert.Null(segment.CompressionCheckpointRef);
        Assert.Empty(result.Report.CompressionCandidates);
        Assert.Empty(result.Report.CompressionCheckpoints);
    }

    [Fact]
    public async Task PrepareProviderInputAsync_ShouldDefaultMissingProviderUsageToEstimatedDiagnostics()
    {
        var bridge = new ExecutionRuntimeContextPolicyBridge();
        var step = CreateModelStep();
        var approvedPolicy = CreateApprovedPolicy(step, new ContextPolicy(
            maxInputTokens: 32,
            allowedSourceKinds: new[] { ContextSourceKind.CurrentUserInput.ToString() },
            sourceRules: new[]
            {
                new ContextSourceRule(ContextSourceKind.CurrentUserInput, priority: 0),
            },
            policyId: "context.policy.default-missing-usage"));
        var candidates = new[]
        {
            new ContextSourceCandidate("user-1", ContextSourceKind.CurrentUserInput, "current question", 10),
        };

        var result = await bridge.PrepareProviderInputAsync(step, CreateContext(), approvedPolicy, candidates, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Report!.UsageSignal);
        Assert.True(result.Report.UsageSignal!.Estimated);
        Assert.Equal("runtime.context.estimated", result.Report.UsageSignal.Source);
        Assert.Equal(10, result.Report.UsageSignal.InputTokens);
        Assert.Equal("provider_usage_missing", result.Report.UsageSignal.MissingReason);
        Assert.Contains(result.Report.TriggerRefs, static triggerRef => triggerRef.EndsWith(":missing-usage", StringComparison.Ordinal));
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

    private static ExecutionRuntimeContext CreateContext(MetadataBag? metadata = null)
        => new(
            new ExecutionId("execution-context-policy"),
            new KernelRunId("kernel-run-context-policy"),
            new GovernanceEnvelope(
                "governance-context-policy",
                allowedModuleIds: new[] { "provider.module" },
                maxSideEffectLevel: SideEffectLevel.ExternalNetwork,
                requiresHumanGate: false),
            metadata: metadata);

    private static MetadataBag Metadata(params (string Key, StructuredValue Value)[] entries)
        => new(entries.ToDictionary(static entry => entry.Key, static entry => entry.Value, StringComparer.Ordinal));

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
