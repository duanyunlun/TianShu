using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

public sealed class KernelCodeModeRuntimeHelpersTests
{
    [Fact]
    public void ResolveCodeModeToolReference_WhenQualifiedMcpToolNameProvided_ShouldReturnMcpModulePath()
    {
        var reference = KernelCodeModeRuntimeHelpers.ResolveCodeModeToolReference("mcp__filesystem__read_file");

        Assert.Equal("tools/mcp/filesystem.js", reference.ModulePath);
        Assert.Equal(["mcp", "filesystem"], reference.Namespace);
        Assert.Equal("read_file", reference.ToolKey);
    }

    [Theory]
    [InlineData("mcp__filesystem__read_file", true, "filesystem", "read_file")]
    [InlineData("shell_command", false, null, null)]
    public void TrySplitQualifiedMcpToolName_ShouldReturnExpectedResult(
        string toolName,
        bool expected,
        string? expectedServer,
        string? expectedTool)
    {
        var result = KernelCodeModeRuntimeHelpers.TrySplitQualifiedMcpToolName(toolName, out var serverName, out var toolKey);

        Assert.Equal(expected, result);
        Assert.Equal(expectedServer, serverName);
        Assert.Equal(expectedTool, toolKey);
    }

    [Fact]
    public void BuildCodeModeNestedToolCallItemId_ShouldSanitizeUnsafeCharacters()
    {
        var itemId = KernelCodeModeRuntimeHelpers.BuildCodeModeNestedToolCallItemId(
            "turn_001",
            "request:001",
            "tool/with spaces");

        Assert.Equal("codemode_tool_with_spaces_request_001_turn_001", itemId);
    }

    [Fact]
    public void TryBuildCodeModeFunctionArguments_WhenInputIsNotObject_ShouldReturnError()
    {
        var input = JsonSerializer.SerializeToElement("plain-text");

        var success = KernelCodeModeRuntimeHelpers.TryBuildCodeModeFunctionArguments(
            "dynamic_tool",
            input,
            out _,
            out var error);

        Assert.False(success);
        Assert.Equal("tool `dynamic_tool` expects a JSON object for arguments", error);
    }
}
