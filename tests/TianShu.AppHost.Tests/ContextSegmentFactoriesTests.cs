using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

public sealed class ContextSegmentFactoriesTests
{
    [Fact]
    public void CreateHistoricalSummary_ShouldKeepTraceAndMarkSummaryAsNonFact()
    {
        var sourceRefs = new[]
        {
            new ContextSourceRef(ContextSourceKind.ConversationHistory, ThreadId: "thread", TurnId: "turn-1", ItemId: "item-1"),
        };
        var trace = new ContextSummaryTrace(
            "summary-1",
            "manual-test",
            new DateTimeOffset(2026, 5, 17, 10, 0, 0, TimeSpan.Zero),
            sourceRefs,
            0.75d);

        var segment = ContextSegmentFactories.CreateHistoricalSummary("history-summary", "用户确认过 X。", trace);

        Assert.Equal(ContextSegmentKind.HistoricalSummary, segment.Kind);
        Assert.Equal(ContextRetentionPolicy.KeepIfRelevant, segment.RetentionPolicy);
        Assert.Equal(sourceRefs, segment.SourceRefs);
        Assert.Equal("summary-1", segment.Metadata["summaryId"]);
        Assert.False((bool)segment.Metadata["summaryIsFact"]!);
        Assert.Equal(0.75d, segment.Confidence);
    }

    [Fact]
    public void CreateMemoryOverlay_WhenCounterexample_ShouldIncreaseAuthorityAndCarryContextSignature()
    {
        var sourceRefs = new[]
        {
            new ContextSourceRef(ContextSourceKind.Memory, Id: "memory-1"),
        };

        var segment = ContextSegmentFactories.CreateMemoryOverlay(
            "memory-overlay",
            "反例：不要把用户配置覆盖成默认值。",
            sourceRefs,
            "repo:tianshu",
            isCounterexample: true,
            confidence: 0.9d);

        Assert.Equal(ContextSegmentKind.MemoryOverlay, segment.Kind);
        Assert.Equal(ContextSegmentPriority.MediumHigh, segment.Priority);
        Assert.True(segment.AuthorityWeight >= 70);
        Assert.Equal("repo:tianshu", segment.Metadata["contextSignature"]);
        Assert.True((bool)segment.Metadata["isCounterexample"]!);
    }

    [Fact]
    public void CreateToolArtifactSlice_ShouldPreserveCommandExitCodeErrorAndArtifactRef()
    {
        var stdout = string.Concat(Enumerable.Repeat("line output ", 500));
        var stderr = "error CS1001: demo failure";

        var segment = ContextSegmentFactories.CreateToolArtifactSlice(new ContextToolArtifactSliceRequest
        {
            SegmentId = "tool-1",
            ToolName = "shell",
            Command = "dotnet test",
            Cwd = @"D:\Work\TianShu",
            ExitCode = 1,
            Duration = TimeSpan.FromSeconds(3),
            Stdout = stdout,
            Stderr = stderr,
            ArtifactRef = "artifact://tool-1/stdout.log",
            MaxTextChars = 400,
            SourceRefs =
            [
                new ContextSourceRef(ContextSourceKind.ToolOutput, TurnId: "turn-1", ItemId: "tool-1"),
            ],
        });

        Assert.Equal(ContextSegmentKind.ToolResult, segment.Kind);
        Assert.Contains("command: dotnet test", segment.Text, StringComparison.Ordinal);
        Assert.Contains("exitCode: 1", segment.Text, StringComparison.Ordinal);
        Assert.Contains(stderr, segment.Text, StringComparison.Ordinal);
        Assert.Contains("artifact://tool-1/stdout.log", segment.Text, StringComparison.Ordinal);
        Assert.Contains("omitted", segment.Text, StringComparison.Ordinal);
        Assert.Equal(1, segment.Metadata["exitCode"]);
        Assert.True(segment.EstimatedTokens > 0);
    }

    [Fact]
    public void Plan_WhenMemoryOverlayDropped_ShouldReportDroppedReason()
    {
        var planner = new ContextSlicePlanner();
        var request = new ContextSliceRequest
        {
            ThreadId = "thread",
            TurnId = "turn",
            ProviderEffectiveContextLimitTokens = 100_000,
            BudgetProfile = new ContextBudgetProfile { SoftBudgetTokens = 10, SafetyMarginTokens = 0 },
            CandidateSegments =
            [
                new ContextSegment
                {
                    Id = "user",
                    Kind = ContextSegmentKind.CurrentUserInput,
                    Priority = ContextSegmentPriority.Critical,
                    RetentionPolicy = ContextRetentionPolicy.MustKeep,
                    Text = "当前问题",
                    EstimatedTokens = 10,
                },
                ContextSegmentFactories.CreateMemoryOverlay(
                    "memory",
                    "用户偏好：保留配置文件。",
                    [new ContextSourceRef(ContextSourceKind.Memory, Id: "memory")],
                    "repo:tianshu") with
                {
                    EstimatedTokens = 10,
                },
            ],
        };

        var result = planner.Plan(request);

        Assert.Contains(result.DroppedSegments, static item => item.Segment.Id == "memory" && item.Reason == DroppedContextReason.BudgetExceeded);
        Assert.Contains(result.Report.DroppedSegments, static item => item.SegmentId == "memory" && item.DroppedReason == DroppedContextReason.BudgetExceeded);
    }
}
