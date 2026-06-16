using System.Text.Json;
using TianShu.Provider.Abstractions;
using TianShu.Provider.OpenAI;
using TianShu.Provider.OpenAICompatible;

namespace TianShu.Provider.OpenAI.Tests;

public sealed class ProviderResponsesRequestComposersTests
{
    [Fact]
    public void Resolve_ShouldReturnOpenAiResponsesRequestComposer_ForResponsesWireApi()
    {
        var composer = ProviderResponsesRequestComposers.Resolve("responses", "test.providerWireApi");

        var typed = Assert.IsType<OpenAiResponsesRequestComposer>(composer);
        Assert.Equal("responses", typed.WireApi);
    }

    [Fact]
    public void Resolve_ShouldReturnChatCompletionsRequestComposer_ForOpenAiChatCompletionsProtocol()
    {
        var composer = ProviderResponsesRequestComposers.Resolve("openai_chat_completions", "test.providerWireApi");

        var typed = Assert.IsType<OpenAiChatCompletionsRequestComposer>(composer);
        Assert.Equal("openai_chat_completions", typed.WireApi);
    }

    [Fact]
    public void Compose_WhenStreamingRequest_ShouldBuildProviderSpecificPayloadOutsideKernel()
    {
        IProviderResponsesRequestComposer composer = new OpenAiResponsesRequestComposer();
        var input = new[]
        {
            JsonSerializer.SerializeToElement(new
            {
                type = "message",
                role = "user",
                content = new object[]
                {
                    new
                    {
                        type = "input_text",
                        text = "hello",
                    },
                },
            }),
        };
        var tools = new[]
        {
            JsonSerializer.SerializeToElement(new
            {
                type = "function",
                name = "shell",
            }),
        };
        var outputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                answer = new
                {
                    type = "string",
                },
            },
            required = new[] { "answer" },
            additionalProperties = false,
        });

        var composition = composer.Compose(
            new ProviderResponsesRequestComposerContext(
                Model: "gpt-5",
                Instructions: "system prompt",
                Input: input,
                Tools: tools,
                Store: false,
                Stream: true,
                ToolChoice: "auto",
                ParallelToolCalls: true,
                ServiceTier: "flex",
                ReasoningEffort: "high",
                ReasoningSummary: "auto",
                TextVerbosity: "medium",
                OutputSchema: outputSchema));

        Assert.Equal("gpt-5", composition.TransportPayload["model"]);
        Assert.Equal("system prompt", composition.TransportPayload["instructions"]);
        Assert.Equal("auto", composition.TransportPayload["tool_choice"]);
        Assert.Equal(true, composition.TransportPayload["parallel_tool_calls"]);
        Assert.Equal(false, composition.TransportPayload["store"]);
        Assert.Equal(true, composition.TransportPayload["stream"]);
        Assert.Equal("flex", composition.TransportPayload["service_tier"]);
        Assert.False(composition.TransportPayload.ContainsKey("input"));

        var include = Assert.IsAssignableFrom<IReadOnlyList<string>>(composition.TransportPayload["include"]);
        Assert.Equal(["reasoning.encrypted_content"], include);

        using var reasoningJson = JsonDocument.Parse(JsonSerializer.Serialize(composition.TransportPayload["reasoning"]));
        Assert.Equal("high", reasoningJson.RootElement.GetProperty("effort").GetString());
        Assert.Equal("auto", reasoningJson.RootElement.GetProperty("summary").GetString());

        using var textJson = JsonDocument.Parse(JsonSerializer.Serialize(composition.TransportPayload["text"]));
        Assert.Equal("medium", textJson.RootElement.GetProperty("verbosity").GetString());
        var format = textJson.RootElement.GetProperty("format");
        Assert.Equal("codex_output_schema", format.GetProperty("name").GetString());
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.True(format.GetProperty("strict").GetBoolean());

        var httpPayload = composition.CreateHttpPayload();
        var httpInput = Assert.IsAssignableFrom<IReadOnlyList<JsonElement>>(httpPayload["input"]);
        Assert.Single(httpInput);
        Assert.Equal("message", httpInput[0].GetProperty("type").GetString());
    }

    [Fact]
    public void Compose_WhenOptionalFieldsMissing_ShouldOmitOptionalSectionsAndKeepEmptyInclude()
    {
        IProviderResponsesRequestComposer composer = new OpenAiResponsesRequestComposer();
        var input = new[]
        {
            JsonSerializer.SerializeToElement(new
            {
                type = "message",
                role = "user",
            }),
        };

        var composition = composer.Compose(
            new ProviderResponsesRequestComposerContext(
                Model: "gpt-5",
                Instructions: "system prompt",
                Input: input,
                Tools: Array.Empty<JsonElement>(),
                Store: false,
                Stream: null,
                ToolChoice: null,
                ParallelToolCalls: null,
                ServiceTier: null,
                ReasoningEffort: null,
                ReasoningSummary: null,
                TextVerbosity: null,
                OutputSchema: null));

        Assert.False(composition.TransportPayload.ContainsKey("reasoning"));
        Assert.False(composition.TransportPayload.ContainsKey("text"));
        Assert.False(composition.TransportPayload.ContainsKey("service_tier"));
        Assert.False(composition.TransportPayload.ContainsKey("tool_choice"));
        Assert.False(composition.TransportPayload.ContainsKey("parallel_tool_calls"));
        Assert.False(composition.TransportPayload.ContainsKey("stream"));

        var include = Assert.IsAssignableFrom<IReadOnlyList<string>>(composition.TransportPayload["include"]);
        Assert.Empty(include);
    }

    [Fact]
    public void Compose_WhenTianShuVerbosityUsesNativeNames_ShouldMapToResponsesVerbosity()
    {
        IProviderResponsesRequestComposer composer = new OpenAiResponsesRequestComposer();

        var composition = composer.Compose(
            new ProviderResponsesRequestComposerContext(
                Model: "gpt-5",
                Instructions: string.Empty,
                Input: Array.Empty<JsonElement>(),
                Tools: Array.Empty<JsonElement>(),
                Store: false,
                Stream: false,
                ToolChoice: null,
                ParallelToolCalls: null,
                ServiceTier: null,
                ReasoningEffort: null,
                ReasoningSummary: null,
                TextVerbosity: "normal",
                OutputSchema: null));

        using var textJson = JsonDocument.Parse(JsonSerializer.Serialize(composition.TransportPayload["text"]));
        Assert.Equal("medium", textJson.RootElement.GetProperty("verbosity").GetString());
    }

    [Fact]
    public void Compose_WhenChatCompletionsProtocol_ShouldBuildMessagesPayloadWithoutResponsesInput()
    {
        IProviderResponsesRequestComposer composer = new OpenAiChatCompletionsRequestComposer();
        var input = new[]
        {
            JsonSerializer.SerializeToElement(new
            {
                type = "message",
                role = "developer",
                content = new object[]
                {
                    new
                    {
                        type = "input_text",
                        text = "dev instructions",
                    },
                },
            }),
            JsonSerializer.SerializeToElement(new
            {
                type = "message",
                role = "user",
                content = new object[]
                {
                    new
                    {
                        type = "input_text",
                        text = "hello",
                    },
                },
            }),
        };

        var composition = composer.Compose(
            new ProviderResponsesRequestComposerContext(
                Model: "openai-compatible-default",
                Instructions: "system prompt",
                Input: input,
                Tools: Array.Empty<JsonElement>(),
                Store: false,
                Stream: true,
                ToolChoice: "auto",
                ParallelToolCalls: true,
                ServiceTier: null,
                ReasoningEffort: null,
                ReasoningSummary: null,
                TextVerbosity: null,
                OutputSchema: null));

        var httpPayload = composition.CreateHttpPayload();
        Assert.Equal("openai-compatible-default", httpPayload["model"]);
        Assert.Equal(true, httpPayload["stream"]);
        Assert.False(httpPayload.ContainsKey("input"));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(httpPayload["messages"]));
        var messages = json.RootElement;
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("system prompt", messages[0].GetProperty("content").GetString());
        Assert.Equal("system", messages[1].GetProperty("role").GetString());
        Assert.Equal("dev instructions", messages[1].GetProperty("content").GetString());
        Assert.Equal("user", messages[2].GetProperty("role").GetString());
        Assert.Equal("hello", messages[2].GetProperty("content").GetString());
    }

    [Fact]
    public void Compose_WhenChatCompletionsAssistantHistoryHasReasoningContent_ShouldReplayProviderArtifact()
    {
        IProviderResponsesRequestComposer composer = new OpenAiChatCompletionsRequestComposer();
        var input = new[]
        {
            JsonSerializer.SerializeToElement(new
            {
                type = "message",
                role = "assistant",
                content = new object[]
                {
                    new
                    {
                        type = "output_text",
                        text = "上一轮回答",
                    },
                },
                reasoning_content = "上一轮 provider reasoning artifact",
            }),
            JsonSerializer.SerializeToElement(new
            {
                type = "message",
                role = "user",
                content = new object[]
                {
                    new
                    {
                        type = "input_text",
                        text = "继续",
                    },
                },
            }),
        };

        var composition = composer.Compose(
            new ProviderResponsesRequestComposerContext(
                Model: "openai-compatible-default",
                Instructions: string.Empty,
                Input: input,
                Tools: Array.Empty<JsonElement>(),
                Store: false,
                Stream: true,
                ToolChoice: "auto",
                ParallelToolCalls: true,
                ServiceTier: null,
                ReasoningEffort: null,
                ReasoningSummary: null,
                TextVerbosity: null,
                OutputSchema: null));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(composition.CreateHttpPayload()["messages"]));
        var assistant = json.RootElement[0];
        Assert.Equal("assistant", assistant.GetProperty("role").GetString());
        Assert.Equal("上一轮回答", assistant.GetProperty("content").GetString());
        Assert.Equal("上一轮 provider reasoning artifact", assistant.GetProperty("reasoning_content").GetString());
        Assert.False(json.RootElement[1].TryGetProperty("reasoning_content", out _));
    }

    [Fact]
    public void Compose_WhenChatCompletionsToolLoopHasReasoningContent_ShouldReplayToolCallsAndToolResult()
    {
        IProviderResponsesRequestComposer composer = new OpenAiChatCompletionsRequestComposer();
        var input = new[]
        {
            JsonSerializer.SerializeToElement(new
            {
                type = "function_call",
                call_id = "call_001",
                name = "get_cwd",
                arguments = "{}",
                reasoning_content = "需要先调用工具确认目录。",
            }),
            JsonSerializer.SerializeToElement(new
            {
                type = "function_call_output",
                call_id = "call_001",
                output = "C:\\Users\\Example",
            }),
        };
        var tools = new[]
        {
            JsonSerializer.SerializeToElement(new
            {
                type = "function",
                function = new
                {
                    name = "get_cwd",
                    parameters = new { type = "object", properties = new { } },
                },
            }),
        };

        var composition = composer.Compose(
            new ProviderResponsesRequestComposerContext(
                Model: "openai-compatible-default",
                Instructions: string.Empty,
                Input: input,
                Tools: tools,
                Store: false,
                Stream: true,
                ToolChoice: "auto",
                ParallelToolCalls: true,
                ServiceTier: null,
                ReasoningEffort: null,
                ReasoningSummary: null,
                TextVerbosity: null,
                OutputSchema: null));

        var payload = composition.CreateHttpPayload();
        Assert.True(payload.ContainsKey("tools"));
        Assert.Equal("auto", payload["tool_choice"]);
        Assert.Equal(true, payload["parallel_tool_calls"]);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(payload["messages"]));
        var assistant = json.RootElement[0];
        var tool = json.RootElement[1];
        Assert.Equal("assistant", assistant.GetProperty("role").GetString());
        Assert.Equal("需要先调用工具确认目录。", assistant.GetProperty("reasoning_content").GetString());
        var toolCall = assistant.GetProperty("tool_calls")[0];
        Assert.Equal("call_001", toolCall.GetProperty("id").GetString());
        Assert.Equal("get_cwd", toolCall.GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("{}", toolCall.GetProperty("function").GetProperty("arguments").GetString());
        Assert.Equal("tool", tool.GetProperty("role").GetString());
        Assert.Equal("call_001", tool.GetProperty("tool_call_id").GetString());
        Assert.Equal("C:\\Users\\Example", tool.GetProperty("content").GetString());
    }

    [Fact]
    public void Compose_WhenChatCompletionsToolLoopHasMultipleToolCalls_ShouldGroupCallsBeforeToolResults()
    {
        IProviderResponsesRequestComposer composer = new OpenAiChatCompletionsRequestComposer();
        var input = new[]
        {
            JsonSerializer.SerializeToElement(new
            {
                type = "function_call",
                call_id = "call_001",
                name = "create_file",
                arguments = "{\"path\":\"a.txt\"}",
                reasoning_content = "需要并行创建两个文件。",
            }),
            JsonSerializer.SerializeToElement(new
            {
                type = "function_call",
                call_id = "call_002",
                name = "create_file",
                arguments = "{\"path\":\"b.txt\"}",
            }),
            JsonSerializer.SerializeToElement(new
            {
                type = "function_call_output",
                call_id = "call_001",
                output = "created a.txt",
            }),
            JsonSerializer.SerializeToElement(new
            {
                type = "function_call_output",
                call_id = "call_002",
                output = "created b.txt",
            }),
            JsonSerializer.SerializeToElement(new
            {
                type = "message",
                role = "user",
                content = new object[]
                {
                    new
                    {
                        type = "input_text",
                        text = "继续",
                    },
                },
            }),
        };

        var composition = composer.Compose(
            new ProviderResponsesRequestComposerContext(
                Model: "openai-compatible-default",
                Instructions: string.Empty,
                Input: input,
                Tools: Array.Empty<JsonElement>(),
                Store: false,
                Stream: true,
                ToolChoice: "auto",
                ParallelToolCalls: true,
                ServiceTier: null,
                ReasoningEffort: null,
                ReasoningSummary: null,
                TextVerbosity: null,
                OutputSchema: null));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(composition.CreateHttpPayload()["messages"]));
        var messages = json.RootElement;
        Assert.Equal(4, messages.GetArrayLength());

        var assistant = messages[0];
        Assert.Equal("assistant", assistant.GetProperty("role").GetString());
        Assert.Equal("需要并行创建两个文件。", assistant.GetProperty("reasoning_content").GetString());
        var toolCalls = assistant.GetProperty("tool_calls");
        Assert.Equal(2, toolCalls.GetArrayLength());
        Assert.Equal("call_001", toolCalls[0].GetProperty("id").GetString());
        Assert.Equal("create_file", toolCalls[0].GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("{\"path\":\"a.txt\"}", toolCalls[0].GetProperty("function").GetProperty("arguments").GetString());
        Assert.Equal("call_002", toolCalls[1].GetProperty("id").GetString());
        Assert.Equal("{\"path\":\"b.txt\"}", toolCalls[1].GetProperty("function").GetProperty("arguments").GetString());

        Assert.Equal("tool", messages[1].GetProperty("role").GetString());
        Assert.Equal("call_001", messages[1].GetProperty("tool_call_id").GetString());
        Assert.Equal("created a.txt", messages[1].GetProperty("content").GetString());
        Assert.Equal("tool", messages[2].GetProperty("role").GetString());
        Assert.Equal("call_002", messages[2].GetProperty("tool_call_id").GetString());
        Assert.Equal("created b.txt", messages[2].GetProperty("content").GetString());
        Assert.Equal("user", messages[3].GetProperty("role").GetString());
        Assert.Equal("继续", messages[3].GetProperty("content").GetString());
    }

    [Fact]
    public void Compose_WhenChatCompletionsToolCallHasNoMatchingOutput_ShouldSkipIncompleteToolReplay()
    {
        IProviderResponsesRequestComposer composer = new OpenAiChatCompletionsRequestComposer();
        var input = new[]
        {
            JsonSerializer.SerializeToElement(new
            {
                type = "function_call",
                call_id = "call_missing_output",
                name = "shell",
                arguments = "{\"command\":\"Get-Location\"}",
                reasoning_content = "需要先调用工具确认目录。",
            }),
            JsonSerializer.SerializeToElement(new
            {
                type = "message",
                role = "user",
                content = new object[]
                {
                    new
                    {
                        type = "input_text",
                        text = "继续",
                    },
                },
            }),
        };

        var composition = composer.Compose(
            new ProviderResponsesRequestComposerContext(
                Model: "openai-compatible-default",
                Instructions: string.Empty,
                Input: input,
                Tools: Array.Empty<JsonElement>(),
                Store: false,
                Stream: true,
                ToolChoice: "auto",
                ParallelToolCalls: true,
                ServiceTier: null,
                ReasoningEffort: null,
                ReasoningSummary: null,
                TextVerbosity: null,
                OutputSchema: null));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(composition.CreateHttpPayload()["messages"]));
        var messages = json.RootElement;
        var user = Assert.Single(messages.EnumerateArray());
        Assert.Equal("user", user.GetProperty("role").GetString());
        Assert.Equal("继续", user.GetProperty("content").GetString());
        Assert.False(user.TryGetProperty("tool_calls", out _));
    }

    [Fact]
    public void Compose_WhenChatCompletionsToolLoopIsPartiallyComplete_ShouldReplayOnlyMatchedToolCalls()
    {
        IProviderResponsesRequestComposer composer = new OpenAiChatCompletionsRequestComposer();
        var input = new[]
        {
            JsonSerializer.SerializeToElement(new
            {
                type = "function_call",
                call_id = "call_001",
                name = "create_file",
                arguments = "{\"path\":\"a.txt\"}",
                reasoning_content = "需要并行创建两个文件。",
            }),
            JsonSerializer.SerializeToElement(new
            {
                type = "function_call",
                call_id = "call_002",
                name = "create_file",
                arguments = "{\"path\":\"b.txt\"}",
            }),
            JsonSerializer.SerializeToElement(new
            {
                type = "function_call_output",
                call_id = "call_001",
                output = "created a.txt",
            }),
            JsonSerializer.SerializeToElement(new
            {
                type = "message",
                role = "user",
                content = new object[]
                {
                    new
                    {
                        type = "input_text",
                        text = "继续",
                    },
                },
            }),
        };

        var composition = composer.Compose(
            new ProviderResponsesRequestComposerContext(
                Model: "openai-compatible-default",
                Instructions: string.Empty,
                Input: input,
                Tools: Array.Empty<JsonElement>(),
                Store: false,
                Stream: true,
                ToolChoice: "auto",
                ParallelToolCalls: true,
                ServiceTier: null,
                ReasoningEffort: null,
                ReasoningSummary: null,
                TextVerbosity: null,
                OutputSchema: null));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(composition.CreateHttpPayload()["messages"]));
        var messages = json.RootElement;
        Assert.Equal(3, messages.GetArrayLength());

        var assistant = messages[0];
        Assert.Equal("assistant", assistant.GetProperty("role").GetString());
        var toolCalls = assistant.GetProperty("tool_calls");
        var toolCall = Assert.Single(toolCalls.EnumerateArray());
        Assert.Equal("call_001", toolCall.GetProperty("id").GetString());

        var tool = messages[1];
        Assert.Equal("tool", tool.GetProperty("role").GetString());
        Assert.Equal("call_001", tool.GetProperty("tool_call_id").GetString());
        Assert.Equal("created a.txt", tool.GetProperty("content").GetString());

        var user = messages[2];
        Assert.Equal("user", user.GetProperty("role").GetString());
        Assert.Equal("继续", user.GetProperty("content").GetString());
    }
}
