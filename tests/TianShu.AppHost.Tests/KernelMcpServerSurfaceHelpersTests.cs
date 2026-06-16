using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

public sealed class McpServerSurfaceHelpersTests
{
    [Fact]
    public void CreateMcpServerToolCallPayload_WhenThreadIdPresent_ShouldIncludeStructuredContent()
    {
        var payload = McpServerSurfaceHelpers.CreateMcpServerToolCallPayload(
            new McpServerToolCallResult("ok", false, "thread_001"));
        var element = JsonSerializer.SerializeToElement(payload);

        Assert.False(element.GetProperty("isError").GetBoolean());
        Assert.Equal("ok", element.GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("thread_001", element.GetProperty("structuredContent").GetProperty("threadId").GetString());
        Assert.Equal("ok", element.GetProperty("structuredContent").GetProperty("content").GetString());
    }

    [Fact]
    public void CreateMcpServerTianShuToolDefinition_ShouldExposePromptAndOutputSchema()
    {
        var definition = JsonSerializer.SerializeToElement(McpServerSurfaceHelpers.CreateMcpServerTianShuToolDefinition());

        Assert.Equal("tianshu", definition.GetProperty("name").GetString());
        Assert.Equal("string", definition.GetProperty("inputSchema").GetProperty("properties").GetProperty("prompt").GetProperty("type").GetString());
        Assert.Contains(
            "threadId",
            definition.GetProperty("outputSchema").GetProperty("required").EnumerateArray().Select(static item => item.GetString()));
        Assert.Contains(
            "content",
            definition.GetProperty("outputSchema").GetProperty("required").EnumerateArray().Select(static item => item.GetString()));
    }

    [Theory]
    [InlineData("on-request", "on-request")]
    [InlineData("never", "never")]
    public void TryReadMcpApprovalPolicy_ShouldParseKnownValue(string rawValue, string expected)
    {
        var policy = McpServerSurfaceHelpers.TryReadMcpApprovalPolicy(rawValue);

        Assert.NotNull(policy);
        Assert.Equal(expected, policy!.ScalarValue);
    }

    [Fact]
    public void TryReadMcpSandboxOverride_ShouldMapWorkspaceWriteMode()
    {
        var sandbox = McpServerSurfaceHelpers.TryReadMcpSandboxOverride("workspace-write");

        Assert.NotNull(sandbox);
        Assert.Equal("workspaceWrite", sandbox!.Type);
    }

    [Theory]
    [InlineData("{\"threadId\":\"thread_001\"}", "thread_001")]
    [InlineData("{\"thread_id\":\"thread_002\"}", "thread_002")]
    [InlineData("{\"conversationId\":\"thread_003\"}", "thread_003")]
    public void ReadMcpTianShuReplyThreadId_ShouldHonorFallbackKeys(string json, string expected)
    {
        var arguments = JsonDocument.Parse(json).RootElement.Clone();

        var threadId = McpServerSurfaceHelpers.ReadMcpTianShuReplyThreadId(arguments);

        Assert.Equal(expected, threadId);
    }

    [Fact]
    public void NormalizeMcpServerToolContent_WhenPromptContainsInlineTool_ShouldTrimParityPrefix()
    {
        var normalized = McpServerSurfaceHelpers.NormalizeMcpServerToolContent(
            "/tool read {}",
            "工具执行结果\r\ntool: read\r\noutput:\r\nhello");

        Assert.Equal("hello", normalized);
    }
}
