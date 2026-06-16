using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

public sealed class KernelCodeModeRuntimeResultTests
{
    [Fact]
    public void ConvertToolResultToCodeModeResult_ForMcpTool_PreservesWrapper()
    {
        var contentItem = JsonSerializer.SerializeToElement(new
        {
            type = "text",
            text = "hello",
        });
        var structured = JsonSerializer.SerializeToElement(new
        {
            echo = "hello",
        });
        var metadata = JsonSerializer.SerializeToElement(new
        {
            trace = "abc123",
        });
        var result = new KernelToolResult(
            success: false,
            outputText: "hello",
            rawOutputContentItems: [contentItem],
            structuredOutput: structured,
            metadata: metadata);

        var payload = KernelCodeModeRuntimeHelpers.ConvertToolResultToCodeModeResult("mcp__rmcp__echo", null, result, isDynamicTool: true);

        Assert.Equal(JsonValueKind.Object, payload.ValueKind);
        Assert.True(payload.GetProperty("isError").GetBoolean());
        Assert.Equal("hello", payload.GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("hello", payload.GetProperty("structuredContent").GetProperty("echo").GetString());
        Assert.Equal("abc123", payload.GetProperty("_meta").GetProperty("trace").GetString());
    }

    [Fact]
    public void ConvertToolResultToCodeModeResult_ForDynamicTool_PreservesStructuredOutput()
    {
        var structured = JsonSerializer.SerializeToElement(new
        {
            ok = true,
        });
        var outputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                ok = new { type = "boolean" },
            },
        });
        var result = new KernelToolResult(
            success: true,
            outputText: "{\"ok\":true}",
            structuredOutput: structured);

        var payload = KernelCodeModeRuntimeHelpers.ConvertToolResultToCodeModeResult("dynamic_tool", outputSchema, result, isDynamicTool: true);

        Assert.Equal(JsonValueKind.Object, payload.ValueKind);
        Assert.True(payload.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void ConvertToolResultToCodeModeResult_ForDynamicToolWithoutTypedOutput_FallsBackToString()
    {
        var result = new KernelToolResult(
            success: true,
            outputText: "plain text");

        var payload = KernelCodeModeRuntimeHelpers.ConvertToolResultToCodeModeResult("dynamic_tool", null, result, isDynamicTool: true);

        Assert.Equal(JsonValueKind.String, payload.ValueKind);
        Assert.Equal("plain text", payload.GetString());
    }
}
