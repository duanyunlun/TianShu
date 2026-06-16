using System.Text.Json;
using TianShu.Provider.Abstractions;
using TianShu.Provider.Google;

namespace TianShu.Provider.Google.Tests;

public sealed class GoogleGenerativeRequestComposerTests
{
    [Fact]
    public void Resolve_ShouldReturnGoogleGenerativeRequestComposer_ForGoogleProtocol()
    {
        var composer = ProviderResponsesRequestComposers.Resolve("google_generative", "test.providerWireApi");

        var typed = Assert.IsType<GoogleGenerativeRequestComposer>(composer);
        Assert.Equal("google_generative", typed.WireApi);
    }

    [Fact]
    public void Compose_WhenTextMessagesProvided_ShouldUseGeminiContentShape()
    {
        IProviderResponsesRequestComposer composer = new GoogleGenerativeRequestComposer();

        var composition = composer.Compose(new ProviderResponsesRequestComposerContext(
            Model: "gemini-2.5-pro",
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
                    role = "assistant",
                    content = new[] { new { type = "output_text", text = "previous answer" } },
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

        Assert.Null(composition.InputPropertyName);
        Assert.False(composition.TransportPayload.ContainsKey("model"));

        var payloadJson = JsonSerializer.SerializeToElement(composition.CreateHttpPayload());
        var systemParts = payloadJson.GetProperty("systemInstruction").GetProperty("parts").EnumerateArray().ToArray();
        Assert.Equal("system root", systemParts[0].GetProperty("text").GetString());
        Assert.Equal("developer hint", systemParts[1].GetProperty("text").GetString());

        var contents = payloadJson.GetProperty("contents").EnumerateArray().ToArray();
        Assert.Equal(2, contents.Length);
        Assert.Equal("model", contents[0].GetProperty("role").GetString());
        Assert.Equal("previous answer", Assert.Single(contents[0].GetProperty("parts").EnumerateArray()).GetProperty("text").GetString());
        Assert.Equal("user", contents[1].GetProperty("role").GetString());
        Assert.Equal("hello", Assert.Single(contents[1].GetProperty("parts").EnumerateArray()).GetProperty("text").GetString());
    }

    [Fact]
    public void Compose_WhenToolReplayProvided_ShouldMapFunctionCallAndOutputToGeminiParts()
    {
        IProviderResponsesRequestComposer composer = new GoogleGenerativeRequestComposer();

        var composition = composer.Compose(new ProviderResponsesRequestComposerContext(
            Model: "gemini-2.5-pro",
            Instructions: string.Empty,
            Input:
            [
                JsonSerializer.SerializeToElement(new
                {
                    type = "function_call",
                    call_id = "call_01",
                    name = "read_file",
                    arguments = "{\"path\":\"README.md\"}",
                }),
                JsonSerializer.SerializeToElement(new
                {
                    type = "function_call_output",
                    call_id = "call_01",
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
        var contents = payloadJson.GetProperty("contents").EnumerateArray().ToArray();
        Assert.Equal(2, contents.Length);

        var functionCall = Assert.Single(contents[0].GetProperty("parts").EnumerateArray()).GetProperty("functionCall");
        Assert.Equal("model", contents[0].GetProperty("role").GetString());
        Assert.Equal("read_file", functionCall.GetProperty("name").GetString());
        Assert.Equal("README.md", functionCall.GetProperty("args").GetProperty("path").GetString());

        var functionResponse = Assert.Single(contents[1].GetProperty("parts").EnumerateArray()).GetProperty("functionResponse");
        Assert.Equal("user", contents[1].GetProperty("role").GetString());
        Assert.Equal("read_file", functionResponse.GetProperty("name").GetString());
        Assert.Equal("file body", functionResponse.GetProperty("response").GetProperty("output").GetString());
    }
}
