using System.Text.Json;
using TianShu.Provider.Abstractions;
using TianShu.Provider.OpenAI;
using TianShu.Provider.OpenAICompatible;

namespace TianShu.Provider.OpenAI.Tests;

public sealed class ProviderResponsesTransportProtocolBindingsTests
{
    [Fact]
    public void Resolve_ShouldReturnOpenAiResponsesTransportProtocolBinding_ForResponsesWireApi()
    {
        var binding = ProviderResponsesTransportProtocolBindings.Resolve("responses", "test.providerWireApi");

        var typed = Assert.IsType<OpenAiResponsesTransportProtocolBinding>(binding);
        Assert.Equal("responses", typed.WireApi);
    }

    [Fact]
    public void Resolve_ShouldReturnChatCompletionsTransportBinding_ForOpenAiChatCompletionsProtocol()
    {
        var binding = ProviderResponsesTransportProtocolBindings.Resolve("openai_chat_completions", "test.providerWireApi");

        var typed = Assert.IsType<OpenAiChatCompletionsTransportProtocolBinding>(binding);
        Assert.Equal("openai_chat_completions", typed.WireApi);
    }

    [Theory]
    [InlineData("https://api.openai.com", "https://api.openai.com/v1/responses")]
    [InlineData("https://api.openai.com/v1", "https://api.openai.com/v1/responses")]
    [InlineData("https://api.openai.com/v1/responses", "https://api.openai.com/v1/responses")]
    public void CreateHttpRequestBinding_WhenStreamRequest_ShouldIncludeResponsesEndpointAndTransportHeaders(
        string baseUrl,
        string expectedEndpoint)
    {
        IProviderResponsesTransportProtocolBinding binding = new OpenAiResponsesTransportProtocolBinding();

        var requestBinding = binding.CreateHttpRequestBinding(
            baseUrl,
            new ProviderResponsesTransportHttpRequestContext(
                ApiKey: "test-key",
                StickyTurnState: "sticky-state",
                TurnMetadataHeader: "{\"turn_id\":\"turn-1\"}",
                Kind: ProviderResponsesTransportHttpRequestKind.StreamRequest,
                TraceParent: "00-11111111111111111111111111111111-2222222222222222-01"));

        Assert.Equal(expectedEndpoint, requestBinding.Endpoint);
        Assert.Equal("Bearer test-key", requestBinding.Headers["Authorization"]);
        Assert.Equal("text/event-stream", requestBinding.Headers["Accept"]);
        Assert.Equal("sticky-state", requestBinding.Headers["x-codex-turn-state"]);
        Assert.Equal("{\"turn_id\":\"turn-1\"}", requestBinding.Headers["x-codex-turn-metadata"]);
        Assert.Equal("00-11111111111111111111111111111111-2222222222222222-01", requestBinding.Headers["traceparent"]);
    }

    [Theory]
    [InlineData("https://api.openai.com", "wss://api.openai.com/v1/responses")]
    [InlineData("https://api.openai.com/v1", "wss://api.openai.com/v1/responses")]
    public void CreateWebSocketConnectionBinding_WhenBaseUrlIsHttps_ShouldUseWssAndHandshakeHeaders(
        string baseUrl,
        string expectedEndpoint)
    {
        IProviderResponsesTransportProtocolBinding binding = new OpenAiResponsesTransportProtocolBinding();

        var connectionBinding = binding.CreateWebSocketConnectionBinding(
            baseUrl,
            new ProviderResponsesTransportWebSocketConnectContext(
                ApiKey: "test-key",
                ClientRequestId: "thread-1",
                StickyTurnState: "sticky-state",
                TurnMetadataHeader: "{\"turn_id\":\"turn-1\"}",
                TraceParent: "00-11111111111111111111111111111111-2222222222222222-01"));

        Assert.Equal(expectedEndpoint, connectionBinding.Endpoint.ToString());
        Assert.Equal("Bearer test-key", connectionBinding.Headers["Authorization"]);
        Assert.Equal("responses_websockets=2026-02-06", connectionBinding.Headers["OpenAI-Beta"]);
        Assert.Equal("thread-1", connectionBinding.Headers["x-client-request-id"]);
        Assert.Equal("sticky-state", connectionBinding.Headers["x-codex-turn-state"]);
        Assert.Equal("{\"turn_id\":\"turn-1\"}", connectionBinding.Headers["x-codex-turn-metadata"]);
        Assert.Equal("00-11111111111111111111111111111111-2222222222222222-01", connectionBinding.Headers["traceparent"]);
    }

    [Fact]
    public void CreateWebSocketRequestBinding_WhenMetadataPresent_ShouldInjectClientMetadata()
    {
        IProviderResponsesTransportProtocolBinding binding = new OpenAiResponsesTransportProtocolBinding();
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["model"] = "gpt-5",
            ["stream"] = true,
        };
        var input = new[]
        {
            JsonSerializer.SerializeToElement(new { type = "message", role = "user" }),
        };

        var requestBinding = binding.CreateWebSocketRequestBinding(
            new ProviderResponsesTransportWebSocketRequestContext(
                payload,
                input,
                PreviousResponseId: "resp_123",
                TurnMetadataHeader: "{\"turn_id\":\"turn-1\"}"));

        Assert.Equal("response.create", requestBinding.Payload["type"]);
        Assert.Equal("resp_123", requestBinding.Payload["previous_response_id"]);
        var clientMetadata = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(requestBinding.Payload["client_metadata"]);
        Assert.Equal("{\"turn_id\":\"turn-1\"}", clientMetadata["x-codex-turn-metadata"]);
    }

    [Fact]
    public void ReadStickyTurnState_WhenHeaderPresent_ShouldReturnNormalizedValue()
    {
        IProviderResponsesTransportProtocolBinding binding = new OpenAiResponsesTransportProtocolBinding();
        var headers = new ProviderResponsesTransportResponseHeaders(
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["x-codex-turn-state"] = [" sticky-state "],
            });

        var stickyTurnState = binding.ReadStickyTurnState(headers);

        Assert.Equal("sticky-state", stickyTurnState);
    }

    [Theory]
    [InlineData("https://api.example.com", "https://api.example.com/v1/chat/completions")]
    [InlineData("https://api.example.com/v1", "https://api.example.com/v1/chat/completions")]
    [InlineData("https://api.example.com/v1/chat/completions", "https://api.example.com/v1/chat/completions")]
    public void CreateHttpRequestBinding_WhenChatCompletionsProtocol_ShouldUseChatCompletionsEndpoint(
        string baseUrl,
        string expectedEndpoint)
    {
        IProviderResponsesTransportProtocolBinding binding = new OpenAiChatCompletionsTransportProtocolBinding();

        var requestBinding = binding.CreateHttpRequestBinding(
            baseUrl,
            new ProviderResponsesTransportHttpRequestContext(
                ApiKey: "test-key",
                StickyTurnState: "ignored",
                TurnMetadataHeader: "{\"turn_id\":\"turn-1\"}",
                Kind: ProviderResponsesTransportHttpRequestKind.StreamRequest,
                TraceParent: "00-11111111111111111111111111111111-2222222222222222-01"));

        Assert.Equal(expectedEndpoint, requestBinding.Endpoint);
        Assert.Equal("Bearer test-key", requestBinding.Headers["Authorization"]);
        Assert.Equal("text/event-stream", requestBinding.Headers["Accept"]);
        Assert.Equal("00-11111111111111111111111111111111-2222222222222222-01", requestBinding.Headers["traceparent"]);
        Assert.False(requestBinding.Headers.ContainsKey("x-codex-turn-state"));
        Assert.False(requestBinding.Headers.ContainsKey("x-codex-turn-metadata"));
    }
}
