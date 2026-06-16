using System.Text.Json;
using TianShu.AppHost.Tools.Runtime;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;

namespace TianShu.AppHost.Tests;

public sealed class KernelToolInvocationResultMapperTests
{
    [Fact]
    public void FromInvocationResult_WhenFailure_ShouldPreserveContractResultAsStructuredOutput()
    {
        var result = new ToolInvocationResult(
            new CallId("call_failure"),
            "read_file",
            failure: new ToolInvocationFailure("tool_failed", "read failed"));

        var mapped = KernelToolInvocationResultMapper.FromInvocationResult(result);

        Assert.False(mapped.Success);
        Assert.Equal("read failed", mapped.OutputText);
        Assert.NotNull(mapped.StructuredOutput);
        Assert.Equal("read_file", mapped.StructuredOutput.Value.GetProperty("ToolKey").GetString());
        Assert.Equal("tool_failed", mapped.StructuredOutput.Value.GetProperty("Failure").GetProperty("Code").GetString());
    }

    [Fact]
    public void FromInvocationResult_WhenTerminalTextTool_ShouldProjectTerminalText()
    {
        var result = new ToolInvocationResult(
            new CallId("call_shell"),
            "shell",
            streamItems:
            [
                new ToolStreamItem("text", StructuredValue.FromPlainObject("done"), isTerminal: true),
            ]);

        var mapped = KernelToolInvocationResultMapper.FromInvocationResult(result);

        Assert.True(mapped.Success);
        Assert.Equal("done", mapped.OutputText);
        Assert.NotNull(mapped.StructuredOutput);
        Assert.Equal("shell", mapped.StructuredOutput.Value.GetProperty("ToolKey").GetString());
    }

    [Fact]
    public void FromInvocationResult_WhenContentItemsExist_ShouldPreserveOutputContentAndRawItems()
    {
        var rawOutput = JsonSerializer.SerializeToElement(new { type = "text", text = "raw" });
        var result = new ToolInvocationResult(
            new CallId("call_content"),
            "custom_tool",
            outputContentItems:
            [
                new ToolOutputContentItem("input_text", Text: "hello"),
                new ToolOutputContentItem("input_image", ImageUrl: "data:image/png;base64,abc", Detail: "high"),
            ],
            rawOutputContentItems: [rawOutput]);

        var mapped = KernelToolInvocationResultMapper.FromInvocationResult(result);

        Assert.True(mapped.Success);
        Assert.NotNull(mapped.OutputContentItems);
        Assert.Equal(2, mapped.OutputContentItems.Count);
        Assert.Equal("hello", mapped.OutputContentItems[0].Text);
        Assert.Equal("data:image/png;base64,abc", mapped.OutputContentItems[1].ImageUrl);
        var raw = Assert.Single(mapped.RawOutputContentItems!);
        Assert.Equal("raw", raw.GetProperty("text").GetString());
        Assert.Contains("\"ToolKey\":\"custom_tool\"", mapped.OutputText, StringComparison.Ordinal);
    }
}
