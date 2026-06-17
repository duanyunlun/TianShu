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

        Assert.Equal(StrategyLifecycleState.Candidate, strategy.LifecycleState);
        Assert.NotEqual(StrategyLifecycleState.Promoted, strategy.LifecycleState);
        Assert.Equal(KernelReviewDecision.RequiresHumanGate, evaluation.Decision);
    }

    [Fact]
    public void StrategyLifecycleAuditRecord_ShouldCarryEvidenceMetricRefsAndApproval()
    {
        var audit = new StrategyLifecycleAuditRecord(
            "audit-001",
            new StrategyId("strategy-001"),
            StrategyLifecycleState.Candidate,
            StrategyLifecycleState.Trial,
            new[] { "evidence://trial/001" },
            new[] { "success", "replay_compatible" },
            humanApproved: true,
            reasonRef: "evidence://trial/001");
        var strategy = new StrategyRecord(
            new StrategyId("strategy-001"),
            "Candidate graph",
            new StageGraphId("graph-001"),
            lifecycleAuditRecords: new[] { audit });

        Assert.Equal(StrategyLifecycleState.Candidate, strategy.LifecycleState);
        Assert.Equal(StrategyLifecycleState.Trial, audit.TargetState);
        Assert.Equal("success", audit.MetricRefs[0]);
        Assert.True(audit.HumanApproved);
        Assert.Equal(audit, Assert.Single(strategy.LifecycleAuditRecords));
    }

    [Fact]
    public void StrategyLifecycleAuditRecord_RequiresEvidenceAndNonUnspecifiedTarget()
    {
        Assert.Throws<ArgumentException>(() => new StrategyLifecycleAuditRecord(
            "audit-001",
            new StrategyId("strategy-001"),
            StrategyLifecycleState.Candidate,
            StrategyLifecycleState.Trial,
            Array.Empty<string>()));

        Assert.Throws<ArgumentOutOfRangeException>(() => new StrategyLifecycleAuditRecord(
            "audit-001",
            new StrategyId("strategy-001"),
            StrategyLifecycleState.Candidate,
            StrategyLifecycleState.Unspecified,
            new[] { "evidence://trial/001" }));
    }

    [Fact]
    public void EvaluationMetricContracts_ShouldCarryEvidenceConfidenceAndDisagreement()
    {
        var estimatedUsage = new KernelEvaluationMetricObservation(
            "token.total.estimated",
            KernelEvaluationMetricKind.Observable,
            KernelEvaluationSignalKind.RuntimeMetrics,
            "metrics://runtime/run-001/token",
            observedValue: 128,
            unit: "tokens",
            confidence: 0.4m,
            estimated: true,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["missingReason"] = "provider_usage_missing",
            });
        var objectiveAnchor = new KernelEvaluationMetricObservation(
            "tests.pass_rate",
            KernelEvaluationMetricKind.ObjectiveAnchor,
            KernelEvaluationSignalKind.ObjectiveAnchor,
            "anchor://tests/run-001",
            score: 1m,
            confidence: 1m);
        var modelJudge = new KernelEvaluationMetricObservation(
            "judge.answer_quality",
            KernelEvaluationMetricKind.ModelJudge,
            KernelEvaluationSignalKind.ModelJudge,
            "judge://review/run-001",
            score: 0.3m,
            confidence: 0.7m);
        var disagreement = new KernelEvaluationDisagreement(
            "disagreement-001",
            KernelEvaluationDisagreementKind.ObjectiveAnchorConflict,
            new[] { objectiveAnchor.MetricId, modelJudge.MetricId },
            "objective anchor passed while model judge score is low",
            "disagreement://run-001",
            severity: 0.8m);
        var evaluation = new KernelEvaluationResult(
            "evaluation-001",
            new KernelRunId("run-001"),
            new KernelTraceId("trace-001"),
            KernelReviewDecision.RequiresHumanGate,
            metricScores: new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["tests.pass_rate"] = 1m,
            },
            evidence: new KernelEvaluationEvidenceSet(
                runtimeMetricRefs: new[] { "metrics://runtime/run-001/token" },
                objectiveAnchorRefs: new[] { "anchor://tests/run-001" },
                modelJudgeRefs: new[] { "judge://review/run-001" }),
            observations: new[] { estimatedUsage, objectiveAnchor, modelJudge },
            disagreements: new[] { disagreement },
            overallConfidence: 0.6m,
            disagreementScore: 0.8m);

        Assert.True(estimatedUsage.Estimated);
        Assert.Equal("provider_usage_missing", estimatedUsage.Metadata["missingReason"]);
        Assert.Equal(KernelEvaluationMetricKind.ObjectiveAnchor, objectiveAnchor.MetricKind);
        Assert.Equal(KernelEvaluationMetricKind.ModelJudge, modelJudge.MetricKind);
        Assert.Equal("anchor://tests/run-001", Assert.Single(evaluation.Evidence.ObjectiveAnchorRefs));
        Assert.True(Assert.Single(evaluation.Disagreements).RequiresHumanGate);
        Assert.Equal(0.8m, evaluation.DisagreementScore);
    }

    [Fact]
    public void EvaluationMetricContracts_RejectInvalidConfidenceAndEmptyDisagreementRefs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new KernelEvaluationMetricObservation(
            "metric-001",
            KernelEvaluationMetricKind.Confidence,
            KernelEvaluationSignalKind.ModelJudge,
            "judge://review/run-001",
            confidence: 1.1m));

        Assert.Throws<ArgumentException>(() => new KernelEvaluationDisagreement(
            "disagreement-001",
            KernelEvaluationDisagreementKind.MissingEvidence,
            Array.Empty<string>(),
            "missing objective anchor",
            "disagreement://run-001"));
    }

    [Fact]
    public void CrossReviewContracts_ShouldCarryHeterogeneousReviewScoresAndUncertainty()
    {
        var baseline = new KernelEvaluationResult(
            "evaluation-001",
            new KernelRunId("run-001"),
            new KernelTraceId("trace-001"),
            KernelReviewDecision.Approved);
        var reviewerB = new KernelCrossReviewReviewerSpec(
            "reviewer-b",
            "route://review/openai",
            "openai",
            "gpt-5.5",
            new[] { "quality" });
        var reviewerC = new KernelCrossReviewReviewerSpec(
            "reviewer-c",
            "route://review/anthropic",
            "anthropic",
            "claude-opus-4.8",
            new[] { "quality" });
        var score = new KernelCrossReviewMetricScore(
            "quality",
            score: 0.75m,
            confidence: 0.8m,
            uncertainty: 0.2m,
            "answer is grounded but incomplete",
            "judge://review/reviewer-b/quality");
        var submission = new KernelCrossReviewSubmission(
            "review-b-001",
            reviewerB.ReviewerId,
            reviewerB.ModelRouteRef,
            new[] { score },
            "mostly acceptable",
            "judge://review/reviewer-b");
        var request = new KernelCrossReviewExperimentRequest(
            "cross-review-001",
            new KernelRunId("run-001"),
            new KernelTraceId("trace-001"),
            "executor-a",
            baseline,
            new[] { reviewerB, reviewerC },
            new[]
            {
                submission,
                new KernelCrossReviewSubmission(
                    "review-c-001",
                    reviewerC.ReviewerId,
                    reviewerC.ModelRouteRef,
                    new[]
                    {
                        new KernelCrossReviewMetricScore("quality", 0.6m, 0.7m, 0.3m, "missing edge case", "judge://review/reviewer-c/quality"),
                    },
                    "needs revision",
                    "judge://review/reviewer-c"),
            });

        Assert.Equal("cross-review-001", request.ExperimentId);
        Assert.Equal("quality", Assert.Single(reviewerB.MetricIds));
        Assert.Equal(0.2m, score.Uncertainty);
        Assert.Equal("judge://review/reviewer-b", submission.EvidenceRef);
        Assert.Equal(2, request.Reviewers.Count);
    }

    [Fact]
    public void CrossReviewContracts_RequireTwoDistinctReviewerModelsAndSubmissions()
    {
        var baseline = new KernelEvaluationResult(
            "evaluation-001",
            new KernelRunId("run-001"),
            new KernelTraceId("trace-001"),
            KernelReviewDecision.Approved);
        var reviewerB = new KernelCrossReviewReviewerSpec("reviewer-b", "route://review/openai-a", "openai", "gpt-5.5");
        var reviewerC = new KernelCrossReviewReviewerSpec("reviewer-c", "route://review/openai-b", "openai", "gpt-5.5");

        Assert.Throws<ArgumentException>(() => new KernelCrossReviewExperimentRequest(
            "cross-review-001",
            new KernelRunId("run-001"),
            new KernelTraceId("trace-001"),
            "executor-a",
            baseline,
            new[] { reviewerB, reviewerC },
            new[]
            {
                new KernelCrossReviewSubmission(
                    "review-b-001",
                    reviewerB.ReviewerId,
                    reviewerB.ModelRouteRef,
                    new[] { new KernelCrossReviewMetricScore("quality", 0.7m, 0.8m, 0.2m, "ok", "judge://review/reviewer-b/quality") },
                    "ok",
                    "judge://review/reviewer-b"),
            }));
    }

    [Fact]
    public void ObjectiveAnchorCalibrationContracts_ShouldCarryBuildTestGoldenAndHumanLabelAnchors()
    {
        var modelJudge = new KernelEvaluationMetricObservation(
            "cross_review.calibration.reviewer-b.correctness",
            KernelEvaluationMetricKind.ModelJudge,
            KernelEvaluationSignalKind.ModelJudge,
            "judge://review/reviewer-b/correctness",
            score: 0.9m,
            confidence: 0.8m,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sourceMetricId"] = "correctness",
            });
        var anchors = new[]
        {
            new KernelObjectiveAnchorObservation(
                "build-001",
                KernelObjectiveAnchorKind.BuildSucceeded,
                "correctness",
                1m,
                1m,
                "anchor://build/release",
                "release build succeeded"),
            new KernelObjectiveAnchorObservation(
                "tests-001",
                KernelObjectiveAnchorKind.TestsPassed,
                "correctness",
                1m,
                1m,
                "anchor://tests/kernel",
                "kernel tests passed"),
            new KernelObjectiveAnchorObservation(
                "golden-001",
                KernelObjectiveAnchorKind.GoldenAnswer,
                "correctness",
                0.75m,
                0.9m,
                "anchor://golden/answer-001",
                "golden answer partially matched"),
            new KernelObjectiveAnchorObservation(
                "human-001",
                KernelObjectiveAnchorKind.HumanLabel,
                "correctness",
                1m,
                1m,
                "anchor://human/label-001",
                "human accepted result"),
        };
        var request = new KernelObjectiveAnchorCalibrationRequest(
            "calibration-001",
            new KernelRunId("run-001"),
            new KernelTraceId("trace-001"),
            new[] { modelJudge },
            anchors);
        var report = new KernelObjectiveAnchorCalibrationReport(
            request.CalibrationId,
            request.RunId,
            request.TraceId,
            new KernelEvaluationEvidenceSet(
                objectiveAnchorRefs: anchors.Select(static item => item.EvidenceRef).ToArray(),
                modelJudgeRefs: new[] { modelJudge.EvidenceRef }),
            new[]
            {
                new KernelEvaluationMetricObservation(
                    "objective_anchor.calibration-001.build-001.correctness",
                    KernelEvaluationMetricKind.ObjectiveAnchor,
                    KernelEvaluationSignalKind.ObjectiveAnchor,
                    anchors[0].EvidenceRef,
                    score: anchors[0].Score,
                    confidence: anchors[0].Confidence),
            },
            new[] { modelJudge },
            Array.Empty<KernelEvaluationDisagreement>(),
            objectiveAnchorScore: 0.9375m,
            modelJudgeScore: 0.9m,
            originalModelJudgeConfidence: 0.8m,
            calibratedModelJudgeConfidence: 0.8m,
            requiresHumanGate: false);

        Assert.Equal(4, request.ObjectiveAnchors.Count);
        Assert.Contains(request.ObjectiveAnchors, item => item.AnchorKind == KernelObjectiveAnchorKind.BuildSucceeded);
        Assert.Contains(request.ObjectiveAnchors, item => item.AnchorKind == KernelObjectiveAnchorKind.TestsPassed);
        Assert.Contains(request.ObjectiveAnchors, item => item.AnchorKind == KernelObjectiveAnchorKind.GoldenAnswer);
        Assert.Contains(request.ObjectiveAnchors, item => item.AnchorKind == KernelObjectiveAnchorKind.HumanLabel);
        Assert.Equal("anchor://human/label-001", request.ObjectiveAnchors[^1].EvidenceRef);
        Assert.Equal(0.9375m, report.ObjectiveAnchorScore);
        Assert.Equal(modelJudge.EvidenceRef, Assert.Single(report.Evidence.ModelJudgeRefs));
    }

    [Fact]
    public void ObjectiveAnchorCalibrationContracts_RequireModelJudgeAndObjectiveAnchors()
    {
        Assert.Throws<ArgumentException>(() => new KernelObjectiveAnchorCalibrationRequest(
            "calibration-001",
            new KernelRunId("run-001"),
            new KernelTraceId("trace-001"),
            Array.Empty<KernelEvaluationMetricObservation>(),
            new[]
            {
                new KernelObjectiveAnchorObservation("build-001", KernelObjectiveAnchorKind.BuildSucceeded, "correctness", 1m, 1m, "anchor://build", "build passed"),
            }));

        Assert.Throws<ArgumentException>(() => new KernelObjectiveAnchorCalibrationRequest(
            "calibration-001",
            new KernelRunId("run-001"),
            new KernelTraceId("trace-001"),
            new[]
            {
                new KernelEvaluationMetricObservation(
                    "success",
                    KernelEvaluationMetricKind.Observable,
                    KernelEvaluationSignalKind.RuntimeTrace,
                    "trace://run-001",
                    score: 1m),
            },
            new[]
            {
                new KernelObjectiveAnchorObservation("build-001", KernelObjectiveAnchorKind.BuildSucceeded, "correctness", 1m, 1m, "anchor://build", "build passed"),
            }));
    }

    [Fact]
    public void StrategyEvaluationAggregationContracts_ShouldCarryComparisonEvidenceAndGateState()
    {
        var strategyId = new StrategyId("strategy-candidate");
        var sample = new KernelStrategyEvaluationSample(
            "sample-001",
            strategyId,
            new KernelRunId("run-001"),
            new KernelTraceId("trace-001"),
            CreateEvaluation(
                "evaluation-001",
                "run-001",
                "trace-001",
                new[]
                {
                    new KernelEvaluationMetricObservation(
                        "success",
                        KernelEvaluationMetricKind.Observable,
                        KernelEvaluationSignalKind.RuntimeTrace,
                        "trace://trace-001",
                        score: 1m,
                        confidence: 1m),
                }),
            "evidence://sample-001");
        var metric = new KernelStrategyMetricAggregate(
            "success",
            sampleCount: 2,
            meanScore: 0.9m,
            minScore: 0.8m,
            maxScore: 1m,
            meanConfidence: 0.95m,
            estimatedCount: 0,
            signalKinds: new[] { KernelEvaluationSignalKind.RuntimeTrace });
        var comparison = new KernelStrategyComparison(
            strategyId,
            sampleCount: 2,
            new[] { metric },
            averageScore: 0.9m,
            averageConfidence: 0.95m,
            disagreementCount: 0,
            objectiveAnchorConflictCount: 0,
            missingEvidenceCount: 0,
            modelJudgeOnly: false,
            KernelStrategyEvaluationGateState.PromotionReady,
            promotionReady: true,
            requiresHumanGate: false);
        var request = new KernelStrategyEvaluationAggregationRequest(
            "aggregation-001",
            new[] { sample },
            minimumSamplesPerStrategy: 2);
        var report = new KernelStrategyEvaluationAggregationReport(
            request.AggregationId,
            new[] { comparison },
            strategyId,
            hasPromotionReadyCandidate: true,
            requiresHumanGate: false);

        Assert.Equal("aggregation-001", request.AggregationId);
        Assert.Equal(strategyId, sample.StrategyId);
        Assert.Equal("evidence://sample-001", sample.EvidenceRef);
        Assert.Equal(KernelStrategyEvaluationGateState.PromotionReady, comparison.GateState);
        Assert.True(report.HasPromotionReadyCandidate);
        Assert.Equal(strategyId, report.BestCandidateStrategyId);
    }

    [Fact]
    public void StrategyEvaluationAggregationContracts_RejectEmptySamplesAndUnspecifiedGate()
    {
        Assert.Throws<ArgumentException>(() => new KernelStrategyEvaluationAggregationRequest(
            "aggregation-001",
            Array.Empty<KernelStrategyEvaluationSample>()));

        Assert.Throws<ArgumentOutOfRangeException>(() => new KernelStrategyComparison(
            new StrategyId("strategy-candidate"),
            sampleCount: 1,
            Array.Empty<KernelStrategyMetricAggregate>(),
            averageScore: 0.5m,
            averageConfidence: 0.5m,
            disagreementCount: 0,
            objectiveAnchorConflictCount: 0,
            missingEvidenceCount: 0,
            modelJudgeOnly: false,
            KernelStrategyEvaluationGateState.Unspecified,
            promotionReady: false,
            requiresHumanGate: false));
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
    public void StructuredContextManagementContracts_ShouldCarryUsageTriggersSupersedeAndCheckpoints()
    {
        var policy = new ContextPolicy(maxInputTokens: 256, policyId: "context.policy.structured");
        var approved = new ApprovedContextPolicy(
            policy,
            new CoreIntentId("intent-structured"),
            new StageGraphId("graph-structured"),
            new StageId("stage-structured"),
            new KernelOperationId("operation-structured"),
            validationRefs: new[] { "trace://validation/context-structured" });
        var usage = new ContextUsageSignal(
            "usage-structured",
            "provider.usage",
            estimated: false,
            inputTokens: 180,
            outputTokens: 30,
            totalTokens: 210,
            modelContextWindow: 512);
        var trigger = new ContextPressureTrigger(
            "trigger-budget",
            ContextPressureTriggerKind.EstimatedInputBudgetExceeded,
            thresholdRatio: 1,
            thresholdTokens: 256,
            observedTokens: 300,
            modelContextWindow: 512);
        var layer = new ContextDegradationLayerRule(
            "L2",
            new[] { ContextSourceKind.MemoryRecord },
            ContextProjectionMode.Full,
            ContextProjectionMode.ReferenceOnly,
            protectedFromDrop: false,
            protectedFromCompression: false,
            priority: 20);
        var supersede = new ContextSupersedeDecision(
            "supersede-001",
            "memory-old",
            "memory-new",
            ContextSupersedeDisposition.ReferenceOnlySuperseded,
            "newer memory supersedes older memory",
            auditRef: "audit://context/supersede");
        var candidate = new ContextCompressionCandidate(
            "compress-001",
            new[] { "history-001" },
            originalEstimatedTokens: 180,
            targetEstimatedTokens: 64,
            reversible: true,
            reason: "history compaction",
            artifactRef: "artifact://context/history-summary");
        var checkpoint = new ContextCompressionCheckpoint(
            "checkpoint-001",
            candidate.CandidateId,
            candidate.SourceSegmentIds,
            "artifact://context/history-summary",
            reversible: true,
            policy.PolicyId,
            "audit://context/checkpoint");
        var plan = new StructuredContextManagementPlan(
            "plan-structured",
            usage,
            approved,
            new[] { trigger },
            new[] { layer },
            new[] { supersede },
            new[] { candidate });
        var audit = new ContextManagementAuditRecord(
            "audit-structured",
            plan.PlanId,
            policy.PolicyId,
            approved.SourceIntentId.Value,
            approved.SourceGraphId.Value,
            approved.SourceStageId.Value,
            approved.SourceKernelOperationId.Value,
            triggerIds: new[] { trigger.TriggerId },
            includedSegmentIds: new[] { "memory-new" },
            droppedSegmentIds: new[] { "memory-old" },
            compressionCheckpointRefs: new[] { checkpoint.CheckpointId },
            diagnosticsRefs: new[] { "diagnostics://context/structured" });

        Assert.False(plan.UsageSignal.Estimated);
        Assert.Equal(ContextPressureTriggerKind.EstimatedInputBudgetExceeded, Assert.Single(plan.Triggers).Kind);
        Assert.Equal("L2", Assert.Single(plan.LayerRules).LayerId);
        Assert.Equal(ContextSupersedeDisposition.ReferenceOnlySuperseded, Assert.Single(plan.SupersedeDecisions).Disposition);
        Assert.True(Assert.Single(plan.CompressionCandidates).Reversible);
        Assert.Equal("checkpoint-001", Assert.Single(audit.CompressionCheckpointRefs));
        Assert.Equal("audit-structured", audit.AuditId);
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

    private static KernelEvaluationResult CreateEvaluation(
        string evaluationId,
        string runId,
        string traceId,
        IReadOnlyList<KernelEvaluationMetricObservation> observations)
        => new(
            evaluationId,
            new KernelRunId(runId),
            new KernelTraceId(traceId),
            KernelReviewDecision.Approved,
            evidence: new KernelEvaluationEvidenceSet(traceRefs: new[] { $"trace://{traceId}" }),
            observations: observations,
            overallConfidence: observations.Count == 0 ? 0m : observations.Average(static item => item.Confidence),
            disagreementScore: 0m);
}
