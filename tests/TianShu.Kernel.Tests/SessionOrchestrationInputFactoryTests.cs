using TianShu.Contracts.Orchestration;

namespace TianShu.Kernel.Tests;

public sealed class SessionOrchestrationInputFactoryTests
{
    [Fact]
    public void Create_WhenNonDefaultStageRequestedWithoutPreviousStage_UsesDefaultAsPreviousStage()
    {
        var input = SessionOrchestrationInputFactory.Create(
            "thread-001",
            "correlation-001",
            requestedStageId: BuiltInStageDefinitions.Review);

        Assert.Equal(BuiltInStageDefinitions.Default, input.PreviousStageId);
        Assert.Equal(BuiltInStageDefinitions.Review, input.RequestedStageId);
    }

    [Fact]
    public void Create_WhenDefaultStageRequestedWithoutPreviousStage_DoesNotUseFallback()
    {
        var input = SessionOrchestrationInputFactory.Create(
            "thread-001",
            "correlation-001",
            requestedStageId: BuiltInStageDefinitions.Default);

        Assert.Null(input.PreviousStageId);
        Assert.Equal(BuiltInStageDefinitions.Default, input.RequestedStageId);
    }

    [Fact]
    public void Create_WhenCurrentStageExists_UsesCurrentStageBeforeCheckpoint()
    {
        var input = SessionOrchestrationInputFactory.Create(
            " thread-001 ",
            "correlation-001",
            currentStageId: $" {BuiltInStageDefinitions.Coding} ",
            latestCheckpointStageId: BuiltInStageDefinitions.Planning,
            requestedStageId: $" {BuiltInStageDefinitions.Review} ");

        Assert.Equal("thread-001", input.ThreadId.Value);
        Assert.Equal(BuiltInStageDefinitions.Coding, input.PreviousStageId);
        Assert.Equal(BuiltInStageDefinitions.Review, input.RequestedStageId);
    }

    [Fact]
    public void Create_WhenOnlyCheckpointExists_UsesCheckpointStage()
    {
        var input = SessionOrchestrationInputFactory.Create(
            "thread-001",
            "correlation-001",
            latestCheckpointStageId: $" {BuiltInStageDefinitions.Planning} ",
            requestedStageId: BuiltInStageDefinitions.Review);

        Assert.Equal(BuiltInStageDefinitions.Planning, input.PreviousStageId);
    }
}
