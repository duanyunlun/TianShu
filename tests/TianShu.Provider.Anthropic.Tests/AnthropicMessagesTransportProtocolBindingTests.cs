using TianShu.Provider.Abstractions;
using TianShu.Provider.Anthropic;

namespace TianShu.Provider.Anthropic.Tests;

public sealed class AnthropicMessagesTransportProtocolBindingTests
{
    [Fact]
    public void Resolve_ShouldReturnAnthropicTransportBinding_ForAnthropicProtocol()
    {
        var binding = ProviderResponsesTransportProtocolBindings.Resolve("anthropic_messages", "test.providerWireApi");

        var typed = Assert.IsType<AnthropicMessagesTransportProtocolBinding>(binding);
        Assert.Equal("anthropic_messages", typed.WireApi);
    }

    [Theory]
    [InlineData("https://api.anthropic.com", "https://api.anthropic.com/v1/messages")]
    [InlineData("https://api.anthropic.com/v1", "https://api.anthropic.com/v1/messages")]
    [InlineData("https://api.anthropic.com/v1/messages", "https://api.anthropic.com/v1/messages")]
    public void CreateHttpRequestBinding_ShouldUseOfficialMessagesEndpointAndHeaders(
        string baseUrl,
        string expectedEndpoint)
    {
        IProviderResponsesTransportProtocolBinding binding = new AnthropicMessagesTransportProtocolBinding();

        var request = binding.CreateHttpRequestBinding(
            baseUrl,
            new ProviderResponsesTransportHttpRequestContext(
                ApiKey: "test-key",
                StickyTurnState: null,
                TurnMetadataHeader: null,
                Kind: ProviderResponsesTransportHttpRequestKind.StreamRequest,
                TraceParent: "00-11111111111111111111111111111111-2222222222222222-01"));

        Assert.Equal(expectedEndpoint, request.Endpoint);
        Assert.Equal("test-key", request.Headers["x-api-key"]);
        Assert.Equal("2023-06-01", request.Headers["anthropic-version"]);
        Assert.Equal("text/event-stream", request.Headers["Accept"]);
        Assert.Equal("00-11111111111111111111111111111111-2222222222222222-01", request.Headers["traceparent"]);
        Assert.DoesNotContain("Authorization", request.Headers.Keys);
    }
}
