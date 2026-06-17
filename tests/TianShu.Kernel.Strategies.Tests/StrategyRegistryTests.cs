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
        var evidence = CreateEvidence(humanApproved: true);
        var strategy = await registry.SaveCandidateAsync(CreateStrategy(), evidence);

        strategy = await registry.TransitionAsync(strategy.StrategyId, StrategyLifecycleState.Trial, evidence);
        strategy = await registry.TransitionAsync(strategy.StrategyId, StrategyLifecycleState.Promoted, evidence);

        Assert.Equal(StrategyLifecycleState.Promoted, strategy.LifecycleState);
        Assert.Equal(strategy, await registry.GetPromotedAsync(CoreIntentKind.Turn));
        var audit = await registry.ListAuditRecordsAsync(strategy.StrategyId);
        Assert.Equal(
            new[]
            {
                StrategyLifecycleState.Candidate,
                StrategyLifecycleState.Trial,
                StrategyLifecycleState.Promoted,
            },
            audit.Select(static item => item.TargetState).ToArray());
    }

    [Fact]
    public async Task StrategyLifecycle_RejectsIllegalTransition()
    {
        var registry = new StrategyRegistry();
        var strategy = await registry.SaveCandidateAsync(CreateStrategy(), CreateEvidence());

        var result = await registry.ValidateTransitionAsync(strategy, StrategyLifecycleState.Promoted, CreateEvidence(humanApproved: true));

        Assert.Equal(KernelValidationDecision.Rejected, result.Decision);
        Assert.Contains(result.Issues, issue => issue.Code == "kernel.strategy.illegal_transition");
    }

    [Fact]
    public async Task PromotionGate_RejectsMissingEvidenceAndHumanGate()
    {
        var registry = new StrategyRegistry();
        var strategy = await registry.SaveCandidateAsync(CreateStrategy(), CreateEvidence());
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
        var strategy = await registry.SaveCandidateAsync(CreateStrategy(), CreateEvidence());
        strategy = await registry.TransitionAsync(strategy.StrategyId, StrategyLifecycleState.RolledBack, CreateEvidence("rollback.reason"));

        Assert.Equal(StrategyLifecycleState.RolledBack, strategy.LifecycleState);
        var rollback = Assert.Single(registry.RollbackRecords);
        Assert.Equal("rollback.reason", rollback.ReasonRef);
        var audit = await registry.ListAuditRecordsAsync(strategy.StrategyId);
        Assert.Equal(2, audit.Count);
        Assert.Equal(StrategyLifecycleState.RolledBack, audit[^1].TargetState);
        Assert.Equal("rollback.reason", audit[^1].ReasonRef);
    }

    [Fact]
    public async Task Rollback_RemovesPreviouslyPromotedStrategyFromSelection()
    {
        var registry = new StrategyRegistry();
        var evidence = CreateEvidence(humanApproved: true);
        var strategy = await registry.SaveCandidateAsync(CreateStrategy(), evidence);
        strategy = await registry.TransitionAsync(strategy.StrategyId, StrategyLifecycleState.Trial, evidence);
        strategy = await registry.TransitionAsync(strategy.StrategyId, StrategyLifecycleState.Promoted, evidence);

        Assert.Equal(strategy, await registry.GetPromotedAsync(CoreIntentKind.Turn));

        strategy = await registry.TransitionAsync(strategy.StrategyId, StrategyLifecycleState.RolledBack, CreateEvidence("rollback.after.promotion", humanApproved: true));

        Assert.Equal(StrategyLifecycleState.RolledBack, strategy.LifecycleState);
        Assert.Null(await registry.GetPromotedAsync(CoreIntentKind.Turn));
        Assert.DoesNotContain(await registry.ListCandidatesAsync(CoreIntentKind.Turn), item => item.StrategyId == strategy.StrategyId);
        var audit = await registry.ListAuditRecordsAsync(strategy.StrategyId);
        Assert.Equal(
            new[]
            {
                StrategyLifecycleState.Candidate,
                StrategyLifecycleState.Trial,
                StrategyLifecycleState.Promoted,
                StrategyLifecycleState.RolledBack,
            },
            audit.Select(static item => item.TargetState).ToArray());
    }

    [Fact]
    public async Task DeprecatedStrategy_IsAuditedAndRemovedFromCandidateList()
    {
        var registry = new StrategyRegistry();
        var evidence = CreateEvidence(humanApproved: true);
        var strategy = await registry.SaveCandidateAsync(CreateStrategy(), evidence);
        strategy = await registry.TransitionAsync(strategy.StrategyId, StrategyLifecycleState.Trial, evidence);
        strategy = await registry.TransitionAsync(strategy.StrategyId, StrategyLifecycleState.Promoted, evidence);

        strategy = await registry.TransitionAsync(strategy.StrategyId, StrategyLifecycleState.Deprecated, CreateEvidence("deprecated.reason", humanApproved: true));

        Assert.Equal(StrategyLifecycleState.Deprecated, strategy.LifecycleState);
        Assert.Empty(await registry.ListCandidatesAsync(CoreIntentKind.Turn));
        var audit = await registry.ListAuditRecordsAsync(strategy.StrategyId);
        Assert.Equal(StrategyLifecycleState.Deprecated, audit[^1].TargetState);
        Assert.Equal("deprecated.reason", audit[^1].ReasonRef);
    }

    [Fact]
    public async Task SaveCandidate_RejectsMissingEvidenceAndNonCandidateRecord()
    {
        var registry = new StrategyRegistry();

        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.SaveCandidateAsync(CreateStrategy(), Array.Empty<StrategyTransitionEvidence>()));
        await Assert.ThrowsAsync<ArgumentException>(() => registry.SaveCandidateAsync(
            CreateStrategy(StrategyLifecycleState.Trial),
            CreateEvidence()));
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
        Assert.Equal(1m, result.OverallConfidence);
        Assert.Equal(0m, result.DisagreementScore);
        Assert.Equal("trace://trace-run-001", Assert.Single(result.Evidence.TraceRefs));
        Assert.Contains(result.Observations, item =>
            item.MetricId == "success"
            && item.MetricKind == KernelEvaluationMetricKind.Observable
            && item.SignalKind == KernelEvaluationSignalKind.RuntimeTrace
            && item.Score == 1m);
        Assert.Contains(result.Observations, item =>
            item.MetricId == "replay_compatible"
            && item.MetricKind == KernelEvaluationMetricKind.ObjectiveAnchor
            && item.Score == 1m);
        Assert.Empty(result.Disagreements);
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

    [Fact]
    public async Task CrossReviewExperiment_ProjectsReviewerScoresUncertaintyAndDisagreement()
    {
        var service = new KernelCrossReviewExperimentService();
        var runId = new KernelRunId("run-001");
        var traceId = new KernelTraceId("trace-run-001");
        var baseline = new KernelEvaluationResult(
            "evaluation-001",
            runId,
            traceId,
            KernelReviewDecision.Approved);
        var reviewerB = new KernelCrossReviewReviewerSpec(
            "reviewer-b",
            "route://review/openai",
            "openai",
            "gpt-5.5",
            new[] { "answer_quality" });
        var reviewerC = new KernelCrossReviewReviewerSpec(
            "reviewer-c",
            "route://review/anthropic",
            "anthropic",
            "claude-opus-4.8",
            new[] { "answer_quality" });
        var request = new KernelCrossReviewExperimentRequest(
            "cross-review-001",
            runId,
            traceId,
            "executor-a",
            baseline,
            new[] { reviewerB, reviewerC },
            new[]
            {
                new KernelCrossReviewSubmission(
                    "review-b-001",
                    reviewerB.ReviewerId,
                    reviewerB.ModelRouteRef,
                    new[]
                    {
                        new KernelCrossReviewMetricScore("answer_quality", 0.9m, 0.8m, 0.1m, "complete and grounded", "judge://review/reviewer-b/answer_quality"),
                    },
                    "accept",
                    "judge://review/reviewer-b"),
                new KernelCrossReviewSubmission(
                    "review-c-001",
                    reviewerC.ReviewerId,
                    reviewerC.ModelRouteRef,
                    new[]
                    {
                        new KernelCrossReviewMetricScore("answer_quality", 0.4m, 0.7m, 0.5m, "misses required edge case", "judge://review/reviewer-c/answer_quality"),
                    },
                    "needs revision",
                    "judge://review/reviewer-c"),
            },
            disagreementThreshold: 0.25m);

        var report = await service.RunAsync(request);

        Assert.True(report.RequiresHumanGate);
        Assert.Equal(2, report.ReviewerReports.Count);
        Assert.Equal(2, report.Observations.Count);
        Assert.Equal(0.65m, report.AverageScore);
        Assert.Equal(0.3m, report.AverageUncertainty);
        Assert.Equal("trace://trace-run-001", Assert.Single(report.Evidence.TraceRefs));
        Assert.Equal(2, report.Evidence.ModelJudgeRefs.Count);
        var disagreement = Assert.Single(report.Disagreements);
        Assert.Equal(KernelEvaluationDisagreementKind.ModelJudgeDisagreement, disagreement.DisagreementKind);
        Assert.True(disagreement.RequiresHumanGate);
        Assert.Contains(report.Observations, item =>
            item.MetricKind == KernelEvaluationMetricKind.ModelJudge
            && item.SignalKind == KernelEvaluationSignalKind.ModelJudge
            && item.Metadata["reviewerId"] == "reviewer-b"
            && item.Metadata["reason"] == "complete and grounded"
            && item.Metadata["uncertainty"] == "0.1");
    }

    [Fact]
    public async Task CrossReviewExperiment_RejectsSubmissionFromUndeclaredReviewer()
    {
        var service = new KernelCrossReviewExperimentService();
        var runId = new KernelRunId("run-001");
        var traceId = new KernelTraceId("trace-run-001");
        var baseline = new KernelEvaluationResult(
            "evaluation-001",
            runId,
            traceId,
            KernelReviewDecision.Approved);
        var reviewerB = new KernelCrossReviewReviewerSpec("reviewer-b", "route://review/openai", "openai", "gpt-5.5");
        var reviewerC = new KernelCrossReviewReviewerSpec("reviewer-c", "route://review/anthropic", "anthropic", "claude-opus-4.8");
        var request = new KernelCrossReviewExperimentRequest(
            "cross-review-001",
            runId,
            traceId,
            "executor-a",
            baseline,
            new[] { reviewerB, reviewerC },
            new[]
            {
                new KernelCrossReviewSubmission(
                    "review-b-001",
                    reviewerB.ReviewerId,
                    reviewerB.ModelRouteRef,
                    new[] { new KernelCrossReviewMetricScore("quality", 0.9m, 0.8m, 0.1m, "ok", "judge://review/reviewer-b/quality") },
                    "ok",
                    "judge://review/reviewer-b"),
                new KernelCrossReviewSubmission(
                    "review-x-001",
                    "reviewer-x",
                    "route://review/unknown",
                    new[] { new KernelCrossReviewMetricScore("quality", 0.2m, 0.5m, 0.6m, "bad", "judge://review/reviewer-x/quality") },
                    "bad",
                    "judge://review/reviewer-x"),
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(request));
    }

    [Fact]
    public async Task ObjectiveAnchorCalibration_LowersModelJudgeConfidenceWhenAnchorsConflict()
    {
        var service = new KernelObjectiveAnchorCalibrationService();
        var runId = new KernelRunId("run-001");
        var traceId = new KernelTraceId("trace-run-001");
        var modelJudge = new KernelEvaluationMetricObservation(
            "cross_review.reviewer-b.correctness",
            KernelEvaluationMetricKind.ModelJudge,
            KernelEvaluationSignalKind.ModelJudge,
            "judge://review/reviewer-b/correctness",
            score: 0.9m,
            confidence: 0.8m,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sourceMetricId"] = "correctness",
                ["reviewerId"] = "reviewer-b",
            });
        var request = new KernelObjectiveAnchorCalibrationRequest(
            "calibration-001",
            runId,
            traceId,
            new[] { modelJudge },
            new[]
            {
                new KernelObjectiveAnchorObservation(
                    "build-001",
                    KernelObjectiveAnchorKind.BuildSucceeded,
                    "correctness",
                    score: 0m,
                    confidence: 1m,
                    "anchor://build/release",
                    "release build failed"),
                new KernelObjectiveAnchorObservation(
                    "tests-001",
                    KernelObjectiveAnchorKind.TestsPassed,
                    "correctness",
                    score: 0m,
                    confidence: 1m,
                    "anchor://tests/kernel",
                    "kernel tests failed"),
            },
            conflictThreshold: 0.25m);

        var report = await service.CalibrateAsync(request);

        Assert.True(report.RequiresHumanGate);
        Assert.Equal(0m, report.ObjectiveAnchorScore);
        Assert.Equal(0.9m, report.ModelJudgeScore);
        Assert.Equal(0.8m, report.OriginalModelJudgeConfidence);
        Assert.Equal(0.08m, report.CalibratedModelJudgeConfidence);
        var disagreement = Assert.Single(report.Disagreements);
        Assert.Equal(KernelEvaluationDisagreementKind.ObjectiveAnchorConflict, disagreement.DisagreementKind);
        Assert.Equal("trace://trace-run-001", Assert.Single(report.Evidence.TraceRefs));
        Assert.Equal(2, report.Evidence.ObjectiveAnchorRefs.Count);
        var calibrated = Assert.Single(report.CalibratedModelJudgeObservations);
        Assert.Equal(0.08m, calibrated.Confidence);
        Assert.Equal("0.8", calibrated.Metadata["originalConfidence"]);
        Assert.Equal("0.08", calibrated.Metadata["calibratedConfidence"]);
    }

    [Fact]
    public async Task ObjectiveAnchorCalibration_WhenAnchorsAlign_DoesNotCreateDisagreement()
    {
        var service = new KernelObjectiveAnchorCalibrationService();
        var modelJudge = new KernelEvaluationMetricObservation(
            "cross_review.reviewer-b.correctness",
            KernelEvaluationMetricKind.ModelJudge,
            KernelEvaluationSignalKind.ModelJudge,
            "judge://review/reviewer-b/correctness",
            score: 0.9m,
            confidence: 0.75m,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sourceMetricId"] = "correctness",
            });
        var request = new KernelObjectiveAnchorCalibrationRequest(
            "calibration-001",
            new KernelRunId("run-001"),
            new KernelTraceId("trace-run-001"),
            new[] { modelJudge },
            new[]
            {
                new KernelObjectiveAnchorObservation(
                    "human-001",
                    KernelObjectiveAnchorKind.HumanLabel,
                    "correctness",
                    score: 0.85m,
                    confidence: 1m,
                    "anchor://human/label-001",
                    "human accepted with minor caveat"),
            },
            conflictThreshold: 0.25m);

        var report = await service.CalibrateAsync(request);

        Assert.False(report.RequiresHumanGate);
        Assert.Empty(report.Disagreements);
        Assert.Equal(0.75m, report.CalibratedModelJudgeConfidence);
        Assert.Equal(KernelEvaluationMetricKind.ObjectiveAnchor, Assert.Single(report.ObjectiveAnchorObservations).MetricKind);
    }

    [Fact]
    public async Task StrategyEvaluationAggregation_AggregatesMultipleSamplesToPromotionReadyComparison()
    {
        var service = new KernelStrategyEvaluationAggregationService();
        var strategyId = new StrategyId("strategy-candidate");
        var request = new KernelStrategyEvaluationAggregationRequest(
            "aggregation-001",
            new[]
            {
                CreateStrategySample(strategyId, "001", observableScore: 1m, anchorScore: 1m),
                CreateStrategySample(strategyId, "002", observableScore: 0.8m, anchorScore: 0.9m),
            },
            minimumSamplesPerStrategy: 2);

        var report = await service.AggregateAsync(request);

        Assert.True(report.HasPromotionReadyCandidate);
        Assert.Equal(strategyId, report.BestCandidateStrategyId);
        Assert.False(report.RequiresHumanGate);
        var comparison = Assert.Single(report.Comparisons);
        Assert.True(comparison.PromotionReady);
        Assert.Equal(KernelStrategyEvaluationGateState.PromotionReady, comparison.GateState);
        Assert.Equal(2, comparison.SampleCount);
        Assert.Equal(0.925m, comparison.AverageScore);
        Assert.Contains(comparison.MetricAggregates, item =>
            item.MetricId == "correctness"
            && item.SampleCount == 2
            && item.MeanScore == 0.95m
            && item.MinScore == 0.9m
            && item.MaxScore == 1m);
    }

    [Fact]
    public async Task StrategyEvaluationAggregation_DoesNotPromoteWithoutRegistryGate()
    {
        var registry = new StrategyRegistry();
        var service = new KernelStrategyEvaluationAggregationService();
        var strategy = await registry.SaveCandidateAsync(CreateStrategy(), CreateEvidence(humanApproved: true));
        strategy = await registry.TransitionAsync(strategy.StrategyId, StrategyLifecycleState.Trial, CreateEvidence(humanApproved: true));
        var request = new KernelStrategyEvaluationAggregationRequest(
            "aggregation-001",
            new[]
            {
                CreateStrategySample(strategy.StrategyId, "001", observableScore: 1m, anchorScore: 1m),
                CreateStrategySample(strategy.StrategyId, "002", observableScore: 0.9m, anchorScore: 0.9m),
            },
            minimumSamplesPerStrategy: 2);

        var report = await service.AggregateAsync(request);

        Assert.True(report.HasPromotionReadyCandidate);
        Assert.Null(await registry.GetPromotedAsync(CoreIntentKind.Turn));
        Assert.Equal(StrategyLifecycleState.Trial, (await registry.GetAsync(strategy.StrategyId))!.LifecycleState);
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.TransitionAsync(
            strategy.StrategyId,
            StrategyLifecycleState.Promoted,
            CreateEvidence("evidence://aggregation/001", humanApproved: false)));

        var promoted = await registry.TransitionAsync(
            strategy.StrategyId,
            StrategyLifecycleState.Promoted,
            CreateEvidence("evidence://aggregation/001", humanApproved: true));

        Assert.Equal(StrategyLifecycleState.Promoted, promoted.LifecycleState);
    }

    [Fact]
    public async Task StrategyEvaluationAggregation_SingleSampleIsInsufficient()
    {
        var service = new KernelStrategyEvaluationAggregationService();
        var request = new KernelStrategyEvaluationAggregationRequest(
            "aggregation-001",
            new[] { CreateStrategySample(new StrategyId("strategy-candidate"), "001", observableScore: 1m, anchorScore: 1m) },
            minimumSamplesPerStrategy: 2);

        var report = await service.AggregateAsync(request);

        var comparison = Assert.Single(report.Comparisons);
        Assert.False(report.HasPromotionReadyCandidate);
        Assert.False(comparison.PromotionReady);
        Assert.Equal(KernelStrategyEvaluationGateState.InsufficientEvidence, comparison.GateState);
        Assert.Contains("kernel.strategy_aggregation.insufficient_samples", comparison.BlockingReasons);
    }

    [Fact]
    public async Task StrategyEvaluationAggregation_ObjectiveAnchorConflictBlocksPromotionReady()
    {
        var service = new KernelStrategyEvaluationAggregationService();
        var strategyId = new StrategyId("strategy-candidate");
        var conflict = new KernelEvaluationDisagreement(
            "objective-anchor.conflict.001.correctness",
            KernelEvaluationDisagreementKind.ObjectiveAnchorConflict,
            new[] { "judge.correctness", "anchor.correctness" },
            "anchor and judge conflict",
            "diagnostics://conflict/correctness",
            severity: 0.8m,
            requiresHumanGate: true);
        var request = new KernelStrategyEvaluationAggregationRequest(
            "aggregation-001",
            new[]
            {
                CreateStrategySample(strategyId, "001", observableScore: 1m, anchorScore: 0m, disagreements: new[] { conflict }),
                CreateStrategySample(strategyId, "002", observableScore: 0.9m, anchorScore: 0m, disagreements: new[] { conflict }),
            },
            minimumSamplesPerStrategy: 2);

        var report = await service.AggregateAsync(request);

        var comparison = Assert.Single(report.Comparisons);
        Assert.False(report.HasPromotionReadyCandidate);
        Assert.True(report.RequiresHumanGate);
        Assert.Equal(KernelStrategyEvaluationGateState.BlockedByDisagreement, comparison.GateState);
        Assert.Equal(2, comparison.ObjectiveAnchorConflictCount);
        Assert.Contains("kernel.strategy_aggregation.objective_anchor_conflict", comparison.BlockingReasons);
    }

    [Fact]
    public async Task StrategyEvaluationAggregation_ModelJudgeDisagreementRequiresHumanGate()
    {
        var service = new KernelStrategyEvaluationAggregationService();
        var strategyId = new StrategyId("strategy-candidate");
        var disagreement = new KernelEvaluationDisagreement(
            "cross-review.disagreement.answer_quality",
            KernelEvaluationDisagreementKind.ModelJudgeDisagreement,
            new[] { "judge.reviewer-b.answer_quality", "judge.reviewer-c.answer_quality" },
            "model reviewers disagree",
            "diagnostics://cross-review/disagreement/answer_quality",
            severity: 0.6m,
            requiresHumanGate: true);
        var request = new KernelStrategyEvaluationAggregationRequest(
            "aggregation-001",
            new[]
            {
                CreateStrategySample(strategyId, "001", observableScore: 1m, anchorScore: 1m, disagreements: new[] { disagreement }),
                CreateStrategySample(strategyId, "002", observableScore: 0.9m, anchorScore: 0.9m, disagreements: new[] { disagreement }),
            },
            minimumSamplesPerStrategy: 2);

        var report = await service.AggregateAsync(request);

        var comparison = Assert.Single(report.Comparisons);
        Assert.False(report.HasPromotionReadyCandidate);
        Assert.True(report.RequiresHumanGate);
        Assert.False(comparison.PromotionReady);
        Assert.True(comparison.RequiresHumanGate);
        Assert.Equal(KernelStrategyEvaluationGateState.BlockedByDisagreement, comparison.GateState);
        Assert.Contains("kernel.strategy_aggregation.disagreement_requires_human_gate", comparison.BlockingReasons);
    }

    [Fact]
    public async Task StrategyEvaluationAggregation_ModelJudgeOnlySamplesAreNotPromotionReady()
    {
        var service = new KernelStrategyEvaluationAggregationService();
        var strategyId = new StrategyId("strategy-candidate");
        var request = new KernelStrategyEvaluationAggregationRequest(
            "aggregation-001",
            new[]
            {
                CreateModelJudgeOnlySample(strategyId, "001", 0.9m),
                CreateModelJudgeOnlySample(strategyId, "002", 0.85m),
            },
            minimumSamplesPerStrategy: 2);

        var report = await service.AggregateAsync(request);

        var comparison = Assert.Single(report.Comparisons);
        Assert.False(report.HasPromotionReadyCandidate);
        Assert.False(comparison.PromotionReady);
        Assert.True(comparison.RequiresHumanGate);
        Assert.True(comparison.ModelJudgeOnly);
        Assert.Equal(KernelStrategyEvaluationGateState.RequiresHumanGate, comparison.GateState);
        Assert.Contains("kernel.strategy_aggregation.model_judge_only", comparison.BlockingReasons);
    }

    private static StrategyRecord CreateStrategy(StrategyLifecycleState state = StrategyLifecycleState.Candidate)
        => new(
            new StrategyId("strategy-001"),
            "Strategy",
            new StageGraphId("graph-001"),
            state);

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

    private static KernelStrategyEvaluationSample CreateStrategySample(
        StrategyId strategyId,
        string suffix,
        decimal observableScore,
        decimal anchorScore,
        IReadOnlyList<KernelEvaluationDisagreement>? disagreements = null)
    {
        var runId = new KernelRunId($"run-{suffix}");
        var traceId = new KernelTraceId($"trace-{suffix}");
        var observable = new KernelEvaluationMetricObservation(
            $"success.{suffix}",
            KernelEvaluationMetricKind.Observable,
            KernelEvaluationSignalKind.RuntimeTrace,
            $"trace://{traceId.Value}",
            score: observableScore,
            confidence: 1m,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sourceMetricId"] = "success",
            });
        var anchor = new KernelEvaluationMetricObservation(
            $"objective_anchor.{suffix}.correctness",
            KernelEvaluationMetricKind.ObjectiveAnchor,
            KernelEvaluationSignalKind.ObjectiveAnchor,
            $"anchor://tests/{suffix}",
            score: anchorScore,
            confidence: 1m,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sourceMetricId"] = "correctness",
            });

        return new KernelStrategyEvaluationSample(
            $"sample-{suffix}",
            strategyId,
            runId,
            traceId,
            CreateEvaluation($"evaluation-{suffix}", runId, traceId, new[] { observable, anchor }, disagreements ?? Array.Empty<KernelEvaluationDisagreement>()),
            $"evidence://sample/{suffix}");
    }

    private static KernelStrategyEvaluationSample CreateModelJudgeOnlySample(StrategyId strategyId, string suffix, decimal score)
    {
        var runId = new KernelRunId($"run-{suffix}");
        var traceId = new KernelTraceId($"trace-{suffix}");
        var modelJudge = new KernelEvaluationMetricObservation(
            $"judge.{suffix}.quality",
            KernelEvaluationMetricKind.ModelJudge,
            KernelEvaluationSignalKind.ModelJudge,
            $"judge://{suffix}/quality",
            score: score,
            confidence: 0.7m,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sourceMetricId"] = "quality",
            });

        return new KernelStrategyEvaluationSample(
            $"sample-{suffix}",
            strategyId,
            runId,
            traceId,
            CreateEvaluation($"evaluation-{suffix}", runId, traceId, new[] { modelJudge }, Array.Empty<KernelEvaluationDisagreement>()),
            $"evidence://sample/{suffix}");
    }

    private static KernelEvaluationResult CreateEvaluation(
        string evaluationId,
        KernelRunId runId,
        KernelTraceId traceId,
        IReadOnlyList<KernelEvaluationMetricObservation> observations,
        IReadOnlyList<KernelEvaluationDisagreement> disagreements)
        => new(
            evaluationId,
            runId,
            traceId,
            disagreements.Any(static item => item.RequiresHumanGate)
                ? KernelReviewDecision.RequiresHumanGate
                : KernelReviewDecision.Approved,
            evidence: new KernelEvaluationEvidenceSet(traceRefs: new[] { $"trace://{traceId.Value}" }),
            observations: observations,
            disagreements: disagreements,
            overallConfidence: observations.Count == 0 ? 0m : observations.Average(static item => item.Confidence),
            disagreementScore: disagreements.Count == 0 ? 0m : disagreements.Max(static item => item.Severity));
}
