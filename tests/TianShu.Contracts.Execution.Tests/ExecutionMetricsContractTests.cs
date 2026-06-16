using TianShu.Contracts.Execution;

namespace TianShu.Contracts.Execution.Tests;

public sealed class ExecutionMetricsContractTests
{
    [Fact]
    public void RuntimeMetricsEvent_ShouldPreserveRealProviderUsageAttribution()
    {
        var tokenUsage = new TokenUsageSnapshot(
            Available: true,
            MissingReason: null,
            Estimated: false,
            InputTokens: 11,
            CachedInputTokens: null,
            OutputTokens: 23,
            ReasoningOutputTokens: 7,
            TotalTokens: 41,
            Source: "provider.completion.usage");
        var cost = new RuntimeCostSnapshot(false, "price_model_missing", null, null, null);

        var metrics = new RuntimeMetricsEvent(
            "metrics-runtime-001",
            "run-001",
            "execution-001",
            "plan-001",
            "graph-001",
            "stage-001",
            "step-001",
            "gpt-5",
            attemptIndex: 1,
            reviseRound: null,
            tokenUsage,
            cost,
            modelCallCount: 1,
            TimeSpan.FromMilliseconds(42),
            ["price_model_missing"]);

        Assert.True(metrics.TokenUsage.Available);
        Assert.False(metrics.TokenUsage.Estimated);
        Assert.Equal(41, metrics.TokenUsage.TotalTokens);
        Assert.Equal("provider.completion.usage", metrics.TokenUsage.Source);
        Assert.False(metrics.Cost.Available);
        Assert.Equal("price_model_missing", metrics.Cost.MissingReason);
        Assert.Equal("run-001", metrics.RunId);
        Assert.Equal("graph-001", metrics.GraphId);
        Assert.Equal("step-001", metrics.StepId);
    }

    [Fact]
    public void CandidateGenerationMetricsEvent_ShouldAllowEstimatedTokenForDiagnosticsOnly()
    {
        var tokenUsage = new TokenUsageSnapshot(
            Available: true,
            MissingReason: null,
            Estimated: true,
            InputTokens: 100,
            CachedInputTokens: null,
            OutputTokens: 50,
            ReasoningOutputTokens: null,
            TotalTokens: 150,
            Source: "candidate_generation.estimated_text_length");
        var cost = new RuntimeCostSnapshot(false, "estimated_token_not_allowed_for_cost", null, null, null);

        var metrics = new CandidateGenerationMetricsEvent(
            "metrics-candidate-001",
            "task-001",
            "stage_graph",
            attemptIndex: 2,
            reviseRound: 1,
            "gpt-5",
            tokenUsage,
            cost,
            modelCallCount: 1,
            TimeSpan.FromSeconds(1),
            ["estimated_token_not_allowed_for_cost"]);

        Assert.True(metrics.TokenUsage.Estimated);
        Assert.False(metrics.Cost.Available);
        Assert.Equal("estimated_token_not_allowed_for_cost", metrics.Cost.MissingReason);
        Assert.Equal("task-001", metrics.TaskId);
        Assert.Equal("stage_graph", metrics.CandidateKind);
        Assert.Equal(1, metrics.ReviseRound);
    }
}
