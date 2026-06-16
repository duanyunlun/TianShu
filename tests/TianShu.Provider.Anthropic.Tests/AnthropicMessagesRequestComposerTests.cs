using System.Text.Json;
using TianShu.Provider.Abstractions;
using TianShu.Provider.Anthropic;

namespace TianShu.Provider.Anthropic.Tests;

public sealed class AnthropicMessagesRequestComposerTests
{
    [Fact]
    public void Resolve_ShouldReturnAnthropicMessagesRequestComposer_ForAnthropicProtocol()
    {
        var composer = ProviderResponsesRequestComposers.Resolve("anthropic_messages", "test.providerWireApi");

        var typed = Assert.IsType<AnthropicMessagesRequestComposer>(composer);
        Assert.Equal("anthropic_messages", typed.WireApi);
    }

    [Fact]
    public void Compose_WhenTextMessagesProvided_ShouldUseMessagesApiShape()
    {
        IProviderResponsesRequestComposer composer = new AnthropicMessagesRequestComposer();

        var composition = composer.Compose(new ProviderResponsesRequestComposerContext(
            Model: "claude-sonnet-4-5",
            Instructions: "system root",
            Input:
            [
                JsonSerializer.SerializeToElement(new
                {
                    type = "message",
                    role = "developer",
                    content = new[] { new { type = "input_text", text = "developer hint" } },
                }),
                JsonSerializer.SerializeToElement(new
                {
                    type = "message",
                    role = "user",
                    content = new[] { new { type = "input_text", text = "hello" } },
                }),
            ],
            Tools: [],
            Store: false,
            Stream: true,
            ToolChoice: "auto",
            ParallelToolCalls: null,
            ServiceTier: null,
            ReasoningEffort: null,
            ReasoningSummary: null,
            TextVerbosity: null,
            OutputSchema: null));

        Assert.Equal("claude-sonnet-4-5", composition.TransportPayload["model"]);
        Assert.Equal(4096, composition.TransportPayload["max_tokens"]);
        Assert.Equal(true, composition.TransportPayload["stream"]);
        Assert.Equal($"system root{Environment.NewLine}{Environment.NewLine}developer hint", composition.TransportPayload["system"]);
        Assert.Null(composition.InputPropertyName);

        var payloadJson = JsonSerializer.SerializeToElement(composition.CreateHttpPayload());
        var message = Assert.Single(payloadJson.GetProperty("messages").EnumerateArray());
        Assert.Equal("user", message.GetProperty("role").GetString());
        var content = Assert.Single(message.GetProperty("content").EnumerateArray());
        Assert.Equal("text", content.GetProperty("type").GetString());
        Assert.Equal("hello", content.GetProperty("text").GetString());
    }

    [Fact]
    public void Compose_WhenToolReplayProvided_ShouldMapFunctionCallAndOutputToToolBlocks()
    {
        IProviderResponsesRequestComposer composer = new AnthropicMessagesRequestComposer();

        var composition = composer.Compose(new ProviderResponsesRequestComposerContext(
            Model: "claude-sonnet-4-5",
            Instructions: string.Empty,
            Input:
            [
                JsonSerializer.SerializeToElement(new
                {
                    type = "function_call",
                    call_id = "toolu_01",
                    name = "read_file",
                    arguments = "{\"path\":\"README.md\"}",
                }),
                JsonSerializer.SerializeToElement(new
                {
                    type = "function_call_output",
                    call_id = "toolu_01",
                    output = "file body",
                }),
            ],
            Tools: [],
            Store: false,
            Stream: true,
            ToolChoice: null,
            ParallelToolCalls: null,
            ServiceTier: null,
            ReasoningEffort: null,
            ReasoningSummary: null,
            TextVerbosity: null,
            OutputSchema: null));

        var payloadJson = JsonSerializer.SerializeToElement(composition.CreateHttpPayload());
        var messages = payloadJson.GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal(2, messages.Length);

        var toolUse = Assert.Single(messages[0].GetProperty("content").EnumerateArray());
        Assert.Equal("assistant", messages[0].GetProperty("role").GetString());
        Assert.Equal("tool_use", toolUse.GetProperty("type").GetString());
        Assert.Equal("toolu_01", toolUse.GetProperty("id").GetString());
        Assert.Equal("read_file", toolUse.GetProperty("name").GetString());
        Assert.Equal("README.md", toolUse.GetProperty("input").GetProperty("path").GetString());

        var toolResult = Assert.Single(messages[1].GetProperty("content").EnumerateArray());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("tool_result", toolResult.GetProperty("type").GetString());
        Assert.Equal("toolu_01", toolResult.GetProperty("tool_use_id").GetString());
        Assert.Equal("file body", toolResult.GetProperty("content").GetString());
    }

    [Fact]
    public void Compose_WhenFunctionCallHasThinkingBlocks_ShouldReplayThinkingBeforeToolUse()
    {
        IProviderResponsesRequestComposer composer = new AnthropicMessagesRequestComposer();

        var composition = composer.Compose(new ProviderResponsesRequestComposerContext(
            Model: "openai-compatible-default",
            Instructions: string.Empty,
            Input:
            [
                JsonSerializer.SerializeToElement(new
                {
                    type = "function_call",
                    call_id = "toolu_01",
                    name = "shell_command",
                    arguments = "{\"command\":\"pwd\"}",
                    thinking_blocks = new[]
                    {
                        new { type = "thinking", thinking = "Need to inspect cwd.", signature = "sig-1" },
                    },
                }),
            ],
            Tools: [],
            Store: false,
            Stream: true,
            ToolChoice: null,
            ParallelToolCalls: null,
            ServiceTier: null,
            ReasoningEffort: null,
            ReasoningSummary: null,
            TextVerbosity: null,
            OutputSchema: null));

        var payloadJson = JsonSerializer.SerializeToElement(composition.CreateHttpPayload());
        var message = Assert.Single(payloadJson.GetProperty("messages").EnumerateArray());
        var content = message.GetProperty("content").EnumerateArray().ToArray();

        Assert.Equal("assistant", message.GetProperty("role").GetString());
        Assert.Equal("thinking", content[0].GetProperty("type").GetString());
        Assert.Equal("Need to inspect cwd.", content[0].GetProperty("thinking").GetString());
        Assert.Equal("sig-1", content[0].GetProperty("signature").GetString());
        Assert.Equal("Need to inspect cwd.", message.GetProperty("reasoning_content").GetString());
        Assert.Equal("tool_use", content[1].GetProperty("type").GetString());
        Assert.Equal("toolu_01", content[1].GetProperty("id").GetString());
    }

    [Fact]
    public void Compose_WhenFunctionCallHasReasoningContent_ShouldReplayAsThinkingBeforeToolUse()
    {
        IProviderResponsesRequestComposer composer = new AnthropicMessagesRequestComposer();

        var composition = composer.Compose(new ProviderResponsesRequestComposerContext(
            Model: "openai-compatible-default",
            Instructions: string.Empty,
            Input:
            [
                JsonSerializer.SerializeToElement(new
                {
                    type = "function_call",
                    call_id = "call_001",
                    name = "shell",
                    arguments = "{\"command\":[\"powershell.exe\",\"-Command\",\"pwd\"]}",
                    reasoning_content = "需要先确认当前目录。",
                }),
            ],
            Tools: [],
            Store: false,
            Stream: true,
            ToolChoice: null,
            ParallelToolCalls: null,
            ServiceTier: null,
            ReasoningEffort: null,
            ReasoningSummary: null,
            TextVerbosity: null,
            OutputSchema: null));

        var payloadJson = JsonSerializer.SerializeToElement(composition.CreateHttpPayload());
        var message = Assert.Single(payloadJson.GetProperty("messages").EnumerateArray());
        var content = message.GetProperty("content").EnumerateArray().ToArray();

        Assert.Equal("assistant", message.GetProperty("role").GetString());
        Assert.Equal("thinking", content[0].GetProperty("type").GetString());
        Assert.Equal("需要先确认当前目录。", content[0].GetProperty("thinking").GetString());
        Assert.Equal("需要先确认当前目录。", message.GetProperty("reasoning_content").GetString());
        Assert.False(content[0].TryGetProperty("signature", out _));
        Assert.Equal("tool_use", content[1].GetProperty("type").GetString());
        Assert.Equal("call_001", content[1].GetProperty("id").GetString());
    }

    [Fact]
    public void Compose_WhenNonClaudeThinkingToolCallsAndOutputsProvided_ShouldFlattenFollowUpAsText()
    {
        IProviderResponsesRequestComposer composer = new AnthropicMessagesRequestComposer();

        var composition = composer.Compose(new ProviderResponsesRequestComposerContext(
            Model: "openai-compatible-default",
            Instructions: string.Empty,
            Input:
            [
                JsonSerializer.SerializeToElement(new
                {
                    type = "function_call",
                    call_id = "call_001",
                    name = "shell",
                    arguments = "{\"command\":[\"powershell.exe\",\"-Command\",\"Get-Location\"]}",
                    reasoning_content = "需要并行确认环境。",
                }),
                JsonSerializer.SerializeToElement(new
                {
                    type = "function_call",
                    call_id = "call_002",
                    name = "shell",
                    arguments = "{\"command\":[\"powershell.exe\",\"-Command\",\"dotnet --version\"]}",
                    reasoning_content = "需要并行确认环境。",
                }),
                JsonSerializer.SerializeToElement(new
                {
                    type = "function_call_output",
                    call_id = "call_001",
                    output = "C:\\repo",
                }),
                JsonSerializer.SerializeToElement(new
                {
                    type = "function_call_output",
                    call_id = "call_002",
                    output = "10.0.100",
                }),
            ],
            Tools: [],
            Store: false,
            Stream: true,
            ToolChoice: null,
            ParallelToolCalls: null,
            ServiceTier: null,
            ReasoningEffort: null,
            ReasoningSummary: null,
            TextVerbosity: null,
            OutputSchema: null));

        var payloadJson = JsonSerializer.SerializeToElement(composition.CreateHttpPayload());
        var messages = payloadJson.GetProperty("messages").EnumerateArray().ToArray();
        var message = Assert.Single(messages);
        var content = Assert.Single(message.GetProperty("content").EnumerateArray());

        Assert.Equal("user", message.GetProperty("role").GetString());
        Assert.Equal("text", content.GetProperty("type").GetString());
        var text = content.GetProperty("text").GetString();
        Assert.Contains("工具执行结果如下", text, StringComparison.Ordinal);
        Assert.Contains("shell", text, StringComparison.Ordinal);
        Assert.Contains("Get-Location", text, StringComparison.Ordinal);
        Assert.Contains("C:\\repo", text, StringComparison.Ordinal);
        Assert.Contains("dotnet --version", text, StringComparison.Ordinal);
        Assert.Contains("10.0.100", text, StringComparison.Ordinal);
        Assert.DoesNotContain("tool_use", JsonSerializer.Serialize(message), StringComparison.Ordinal);
        Assert.DoesNotContain("tool_result", JsonSerializer.Serialize(message), StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_WhenClaudeFunctionCallHasReasoningContent_ShouldNotAddCompatibilityReasoningField()
    {
        IProviderResponsesRequestComposer composer = new AnthropicMessagesRequestComposer();

        var composition = composer.Compose(new ProviderResponsesRequestComposerContext(
            Model: "claude-sonnet-4-5",
            Instructions: string.Empty,
            Input:
            [
                JsonSerializer.SerializeToElement(new
                {
                    type = "function_call",
                    call_id = "toolu_01",
                    name = "read_file",
                    arguments = "{\"path\":\"README.md\"}",
                    reasoning_content = "compat reasoning",
                }),
            ],
            Tools: [],
            Store: false,
            Stream: true,
            ToolChoice: null,
            ParallelToolCalls: null,
            ServiceTier: null,
            ReasoningEffort: null,
            ReasoningSummary: null,
            TextVerbosity: null,
            OutputSchema: null));

        var payloadJson = JsonSerializer.SerializeToElement(composition.CreateHttpPayload());
        var message = Assert.Single(payloadJson.GetProperty("messages").EnumerateArray());

        Assert.False(message.TryGetProperty("reasoning_content", out _));
    }
}
