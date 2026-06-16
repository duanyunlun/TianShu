using System.Text.Json;
using TianShu.AppHost.Tools;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

public sealed class KernelCodeModeProtocolHelpersTests
{
    [Fact]
    public void BuildCodeModeProtocolPayload_WhenRunningWithoutFallbackCellId_ShouldInferCellIdFromHeader()
    {
        var result = new KernelCodeModeOperationResult(
            Success: true,
            Output: "partial output",
            ContentItems:
            [
                new KernelToolOutputContentItem(
                    "input_text",
                    Text: "Script running with cell ID cell_001\npartial output"),
                new KernelToolOutputContentItem("input_text", Text: "partial output"),
            ]);

        var payload = KernelCodeModeProtocolHelpers.BuildCodeModeProtocolPayload(
            threadId: "thread_001",
            turnId: "turn_001",
            result,
            fallbackCellId: null);
        var element = JsonSerializer.SerializeToElement(payload);

        Assert.True(element.GetProperty("success").GetBoolean());
        Assert.Equal("running", element.GetProperty("status").GetString());
        Assert.Equal("thread_001", element.GetProperty("threadId").GetString());
        Assert.Equal("turn_001", element.GetProperty("turnId").GetString());
        Assert.Equal("cell_001", element.GetProperty("cellId").GetString());
        Assert.Equal("partial output", element.GetProperty("output").GetString());
        Assert.Equal(2, element.GetProperty("contentItems").GetArrayLength());
    }

    [Fact]
    public void BuildCodeModeProtocolPayload_WhenFallbackCellIdProvided_ShouldPreserveIt()
    {
        var result = new KernelCodeModeOperationResult(
            Success: true,
            Output: string.Empty,
            ContentItems:
            [
                new KernelToolOutputContentItem("input_text", Text: "Script completed"),
                new KernelToolOutputContentItem("input_text", Text: "done"),
            ]);

        var payload = KernelCodeModeProtocolHelpers.BuildCodeModeProtocolPayload(
            threadId: "thread_002",
            turnId: "turn_002",
            result,
            fallbackCellId: "cell_keep");
        var element = JsonSerializer.SerializeToElement(payload);

        Assert.Equal("completed", element.GetProperty("status").GetString());
        Assert.Equal("cell_keep", element.GetProperty("cellId").GetString());
    }

    [Theory]
    [InlineData("Script completed", true, "completed")]
    [InlineData("Script terminated", true, "terminated")]
    [InlineData("Script failed", true, "failed")]
    [InlineData("other", false, "failed")]
    [InlineData("other", true, "completed")]
    public void InferCodeModeStatus_ShouldReturnExpectedStatus(string header, bool success, string expected)
    {
        var status = KernelCodeModeProtocolHelpers.InferCodeModeStatus(
            [new KernelToolOutputContentItem("input_text", Text: header)],
            success);

        Assert.Equal(expected, status);
    }

    [Fact]
    public void TryExtractCodeModeCellId_WhenHeaderMissingRunningPrefix_ShouldReturnNull()
    {
        var cellId = KernelCodeModeProtocolHelpers.TryExtractCodeModeCellId(
            [new KernelToolOutputContentItem("input_text", Text: "Script completed")]);

        Assert.Null(cellId);
    }
}
