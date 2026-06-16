namespace TianShu.Kernel.Tests;

public sealed class CoreLoopEntryIntentResolverTests
{
    [Theory]
    [InlineData("plan")]
    [InlineData(" Plan ")]
    [InlineData("PLAN")]
    public void ResolveRequestedStageId_WhenDefaultTurnInPlanMode_ReturnsPlanningStage(string mode)
    {
        var resolver = new CoreLoopEntryIntentResolver();

        var stageId = resolver.ResolveRequestedStageId(mode, CoreLoopEntryIntent.DefaultTurn);

        Assert.Equal(KernelBuiltInStageIds.Planning, stageId);
    }

    [Fact]
    public void ResolveRequestedStageId_WhenDefaultTurnWithoutPlanMode_ReturnsNull()
    {
        var resolver = new CoreLoopEntryIntentResolver();

        var stageId = resolver.ResolveRequestedStageId("default", CoreLoopEntryIntent.DefaultTurn);

        Assert.Null(stageId);
    }

    [Fact]
    public void ResolveRequestedStageId_WhenReviewTurn_ReturnsReviewStage()
    {
        var resolver = new CoreLoopEntryIntentResolver();

        var stageId = resolver.ResolveRequestedStageId(null, CoreLoopEntryIntent.ReviewTurn);

        Assert.Equal(KernelBuiltInStageIds.Review, stageId);
    }
}
