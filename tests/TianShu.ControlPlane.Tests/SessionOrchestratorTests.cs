using TianShu.Contracts.Catalog;
using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Primitives;
using TianShu.Kernel;
using Xunit;

namespace TianShu.ControlPlane.Tests;

public sealed class SessionOrchestratorTests
{
    [Fact]
    public void PlanNext_SelectsEntryStageFromLifecycleOrder()
    {
        var orchestrator = new SessionOrchestrator(BuiltInStageDefinitions.All);

        var decision = orchestrator.PlanNext(CreateInput("turn-1"));

        Assert.Equal(BuiltInStageDefinitions.Default, decision.Stage.Id);
        Assert.Equal("session-entry", decision.Decision.ReasonCode);
        Assert.Equal(StageContextProjectionMode.SelectedSegments.ToString(), decision.Decision.ContextProjectionReason);
    }

    [Fact]
    public void PlanNext_UsesRequestedStageWhenTransitionAllowsIt()
    {
        var orchestrator = new SessionOrchestrator(BuiltInStageDefinitions.All);

        var decision = orchestrator.PlanNext(CreateInput(
            "turn-2",
            previousStageId: BuiltInStageDefinitions.Coding,
            requestedStageId: BuiltInStageDefinitions.Review));

        Assert.Equal(BuiltInStageDefinitions.Review, decision.Stage.Id);
        Assert.Equal("requested-stage", decision.Decision.ReasonCode);
        Assert.Equal(BuiltInStageDefinitions.Coding, decision.Decision.PreviousStageId);
    }

    [Fact]
    public void PlanNext_RejectsDisallowedRequestedTransition()
    {
        var orchestrator = new SessionOrchestrator(BuiltInStageDefinitions.All);

        Assert.Throws<InvalidOperationException>(() => orchestrator.PlanNext(CreateInput(
            "turn-3",
            previousStageId: BuiltInStageDefinitions.Coding,
            requestedStageId: BuiltInStageDefinitions.Planning)));
    }

    [Fact]
    public void PlanNext_UsesCheckpointSuggestionWithinAllowedTransitions()
    {
        var checkpoint = new StageCheckpoint(
            "checkpoint-coding",
            BuiltInStageDefinitions.Coding,
            StageExecutionState.Completed,
            DateTimeOffset.Parse("2026-06-09T00:00:00Z"),
            nextStageSuggestions: [BuiltInStageDefinitions.Review]);
        var orchestrator = new SessionOrchestrator(BuiltInStageDefinitions.All);

        var decision = orchestrator.PlanNext(CreateInput("turn-4", checkpoints: [checkpoint]));

        Assert.Equal(BuiltInStageDefinitions.Review, decision.Stage.Id);
        Assert.Equal("checkpoint-suggestion", decision.Decision.ReasonCode);
    }

    [Fact]
    public void PlanNext_WhenPreviousStageHasNoLifecycleCandidates_FailsClosed()
    {
        var orchestrator = new SessionOrchestrator(
        [
            new StageDefinition("entry", "Entry", 0, allowedNext: ["isolated"]),
            new StageDefinition("isolated", "Isolated", 10, allowedPrevious: ["entry"]),
            new StageDefinition("unrelated", "Unrelated", 20),
        ]);

        var error = Assert.Throws<InvalidOperationException>(() => orchestrator.PlanNext(CreateInput(
            "turn-5",
            previousStageId: "isolated")));

        Assert.Contains("没有可达的下一 Stage", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PlanNext_CarriesObservedPolicyHits()
    {
        var orchestrator = new SessionOrchestrator(BuiltInStageDefinitions.All);

        var decision = orchestrator.PlanNext(CreateInput(
            "turn-6",
            observedState: new SessionObservedState(policyHits: ["runtime-policy-context"])));

        Assert.Equal("runtime-policy-context", Assert.Single(decision.Decision.PolicyHits));
    }

    private static SessionOrchestrationInput CreateInput(
        string correlationId,
        string? previousStageId = null,
        string? requestedStageId = null,
        IReadOnlyList<StageCheckpoint>? checkpoints = null,
        IReadOnlyList<StageContextSegment>? contextLedgerSegments = null,
        int? contextBudgetTokens = null,
        SessionObservedState? observedState = null)
        => new(
            new SessionId("session-1"),
            new ThreadId("thread-1"),
            correlationId,
            previousStageId: previousStageId,
            requestedStageId: requestedStageId,
            checkpoints: checkpoints,
            contextLedgerSegments: contextLedgerSegments,
            contextBudgetTokens: contextBudgetTokens,
            observedState: observedState);
}
