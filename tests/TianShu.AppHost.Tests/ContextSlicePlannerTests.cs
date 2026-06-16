using System.Text.Json;
using System.Text.Encodings.Web;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

public sealed class ContextSlicePlannerTests
{
    [Fact]
    public void ContextBudgetProfile_Default_ShouldUseFiftyThousandSoftBudget()
    {
        var profile = ContextBudgetProfile.Default;

        Assert.Equal(50_000, profile.SoftBudgetTokens);
        Assert.Equal(80_000, profile.ExpandedBudgetTokens);
        Assert.Equal(50_000, profile.GetEffectiveSoftBudgetTokens(100_000));
    }

    [Fact]
    public void ContextBudgetProfile_WhenProviderLimitSmaller_ShouldUseProviderAsHardCeilingWithMargin()
    {
        var profile = ContextBudgetProfile.Default;

        var budget = profile.GetEffectiveSoftBudgetTokens(12_000);

        Assert.Equal(7_000, budget);
    }

    [Fact]
    public void ApproximateContextTokenEstimator_ShouldEstimateTextAndStructuredContent()
    {
        var estimator = new ApproximateContextTokenEstimator("openai");
        var segment = Segment(
            "tool",
            ContextSegmentKind.ToolResult,
            ContextSegmentPriority.High,
            ContextRetentionPolicy.KeepIfRelevant,
            tokens: 0) with
        {
            Text = "abcdefghi",
            StructuredContent = new { exitCode = 1, stderr = "boom" },
        };

        var estimate = estimator.Estimate(segment);

        Assert.True(estimate.Tokens >= 2);
        Assert.True(estimate.IsApproximate);
    }

    [Fact]
    public void Plan_ShouldAlwaysKeepCriticalAndMustKeepSegments()
    {
        var planner = new ContextSlicePlanner();
        var request = Request(
            new ContextBudgetProfile { SoftBudgetTokens = 10, SafetyMarginTokens = 0 },
            Segment("system", ContextSegmentKind.CoreInstruction, ContextSegmentPriority.Critical, ContextRetentionPolicy.MustKeep, tokens: 20),
            Segment("user", ContextSegmentKind.CurrentUserInput, ContextSegmentPriority.Critical, ContextRetentionPolicy.MustKeep, tokens: 20),
            Segment("old", ContextSegmentKind.RecentTurn, ContextSegmentPriority.Low, ContextRetentionPolicy.KeepIfRelevant, tokens: 5));

        var result = planner.Plan(request);

        Assert.Equal(["system", "user"], result.IncludedSegments.Select(static item => item.Id));
        Assert.Contains(result.DroppedSegments, static item => item.Segment.Id == "old" && item.Reason == DroppedContextReason.BudgetExceeded);
        Assert.Equal(OverBudgetReasonCode.DefaultBudgetExceeded, result.Report.OverBudgetReasonCode);
    }

    [Fact]
    public void Plan_ShouldPreferHigherPrioritySegmentsBeforeLowerPrioritySegments()
    {
        var planner = new ContextSlicePlanner();
        var request = Request(
            new ContextBudgetProfile { SoftBudgetTokens = 25, SafetyMarginTokens = 0 },
            Segment("user", ContextSegmentKind.CurrentUserInput, ContextSegmentPriority.Critical, ContextRetentionPolicy.MustKeep, tokens: 10),
            Segment("memory", ContextSegmentKind.MemoryOverlay, ContextSegmentPriority.MediumHigh, ContextRetentionPolicy.KeepIfRelevant, tokens: 10),
            Segment("summary", ContextSegmentKind.HistoricalSummary, ContextSegmentPriority.Medium, ContextRetentionPolicy.KeepIfRelevant, tokens: 10),
            Segment("hint", ContextSegmentKind.RecoveryHint, ContextSegmentPriority.Low, ContextRetentionPolicy.KeepIfRelevant, tokens: 10));

        var result = planner.Plan(request);

        Assert.Equal(["user", "memory"], result.IncludedSegments.Select(static item => item.Id));
        Assert.DoesNotContain(result.IncludedSegments, static item => item.Id == "summary");
        Assert.DoesNotContain(result.IncludedSegments, static item => item.Id == "hint");
    }

    [Fact]
    public void Plan_WhenOverflow_ShouldRouteSummarizeAndReferencePolicies()
    {
        var planner = new ContextSlicePlanner();
        var request = Request(
            new ContextBudgetProfile { SoftBudgetTokens = 15, SafetyMarginTokens = 0 },
            Segment("user", ContextSegmentKind.CurrentUserInput, ContextSegmentPriority.Critical, ContextRetentionPolicy.MustKeep, tokens: 10),
            Segment("history", ContextSegmentKind.RecentTurn, ContextSegmentPriority.High, ContextRetentionPolicy.SummarizeIfDropped, tokens: 10),
            Segment("artifact", ContextSegmentKind.ArtifactSnippet, ContextSegmentPriority.MediumHigh, ContextRetentionPolicy.ReferenceOnlyIfDropped, tokens: 10));

        var result = planner.Plan(request);

        Assert.Equal(["user"], result.IncludedSegments.Select(static item => item.Id));
        Assert.Equal(["history"], result.SummarizedSegments.Select(static item => item.Id));
        Assert.Equal(["artifact"], result.ReferenceOnlySegments.Select(static item => item.Id));
        Assert.Contains(result.Report.SummarizedSegments, static item => item.SegmentId == "history");
        Assert.Contains(result.Report.ReferenceOnlySegments, static item => item.SegmentId == "artifact");
    }

    [Fact]
    public void Plan_ShouldDropDuplicateSegmentIdsWithDiagnosticReason()
    {
        var planner = new ContextSlicePlanner();
        var request = Request(
            new ContextBudgetProfile { SoftBudgetTokens = 100, SafetyMarginTokens = 0 },
            Segment("user", ContextSegmentKind.CurrentUserInput, ContextSegmentPriority.Critical, ContextRetentionPolicy.MustKeep, tokens: 10),
            Segment("user", ContextSegmentKind.CurrentUserInput, ContextSegmentPriority.Critical, ContextRetentionPolicy.MustKeep, tokens: 10));

        var result = planner.Plan(request);

        Assert.Single(result.IncludedSegments);
        Assert.Contains(result.DroppedSegments, static item => item.Segment.Id == "user" && item.Reason == DroppedContextReason.Duplicate);
    }

    [Fact]
    public void Plan_WhenCriticalExceedsProviderLimit_ShouldReportProviderLimitReached()
    {
        var planner = new ContextSlicePlanner();
        var request = Request(
            new ContextBudgetProfile { SoftBudgetTokens = 100, SafetyMarginTokens = 0 },
            Segment("system", ContextSegmentKind.CoreInstruction, ContextSegmentPriority.Critical, ContextRetentionPolicy.MustKeep, tokens: 80),
            Segment("user", ContextSegmentKind.CurrentUserInput, ContextSegmentPriority.Critical, ContextRetentionPolicy.MustKeep, tokens: 80)) with
        {
            ProviderEffectiveContextLimitTokens = 100,
        };

        var result = planner.Plan(request);

        Assert.Equal(160, result.Report.EstimatedIncludedTokens);
        Assert.Equal(OverBudgetReasonCode.ProviderLimitReached, result.Report.OverBudgetReasonCode);
    }

    [Fact]
    public void BuildSlicedResponsesConversationInput_WhenOverBudget_ShouldKeepDeveloperAndCurrentUser()
    {
        var sliced = KernelTurnExecutionRuntimeHelpers.BuildSlicedResponsesConversationInput(
            thread: null,
            userText: "当前请求必须保留",
            developerInstructions: "开发者指令必须保留",
            contextualUserMessages:
            [
                string.Concat(Enumerable.Repeat("older history ", 100)),
            ],
            currentInputItems: null,
            budgetProfile: new ContextBudgetProfile { SoftBudgetTokens = 20, SafetyMarginTokens = 0 });

        var serialized = sliced.Input.Select(static item => JsonSerializer.Serialize(
            item,
            new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })).ToArray();

        Assert.Equal(3, sliced.Input.Count);
        Assert.Contains(serialized, static item => item?.Contains("开发者指令必须保留", StringComparison.Ordinal) == true);
        Assert.Contains(serialized, static item => item?.Contains("当前请求必须保留", StringComparison.Ordinal) == true);
        Assert.Contains(serialized, static item => item?.Contains("Summarized overflow segments", StringComparison.Ordinal) == true);
        Assert.Contains(sliced.Report.SummarizedSegments, static item => item.Kind == ContextSegmentKind.RecentTurn);
    }

    private static ContextSliceRequest Request(ContextBudgetProfile profile, params ContextSegment[] segments)
        => new()
        {
            ThreadId = "thread-1",
            TurnId = "turn-1",
            ModelId = "model",
            ProviderId = "openai",
            ProviderEffectiveContextLimitTokens = 100_000,
            BudgetProfile = profile,
            ContextSignature = "repo:test",
            CandidateSegments = segments,
        };

    private static ContextSegment Segment(
        string id,
        ContextSegmentKind kind,
        ContextSegmentPriority priority,
        ContextRetentionPolicy retentionPolicy,
        int tokens = 1)
        => new()
        {
            Id = id,
            Kind = kind,
            Priority = priority,
            RetentionPolicy = retentionPolicy,
            Text = id,
            EstimatedTokens = tokens,
            AuthorityWeight = priority == ContextSegmentPriority.Critical ? 100 : 0,
            RecencyWeight = kind == ContextSegmentKind.CurrentUserInput ? 100 : 0,
            SourceRefs =
            [
                new ContextSourceRef(ContextSourceKind.UserInput, Id: id, ThreadId: "thread-1", TurnId: "turn-1"),
            ],
        };
}
