using TianShu.Provider.Abstractions;
using TianShu.Provider.Google;

namespace TianShu.Provider.Google.Tests;

public sealed class GoogleGenerativeTransportProtocolBindingTests
{
    [Fact]
    public void Resolve_ShouldReturnGoogleGenerativeTransportBinding_ForGoogleProtocol()
    {
        var binding = ProviderResponsesTransportProtocolBindings.Resolve("google_generative", "test.providerWireApi");

        var typed = Assert.IsType<GoogleGenerativeTransportProtocolBinding>(binding);
        Assert.Equal("google_generative", typed.WireApi);
    }

    [Theory]
    [InlineData("https://generativelanguage.googleapis.com", "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-pro:streamGenerateContent?key=test-key&alt=sse")]
    [InlineData("https://generativelanguage.googleapis.com/v1beta", "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-pro:streamGenerateContent?key=test-key&alt=sse")]
    [InlineData("https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-pro:generateContent", "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-pro:streamGenerateContent?key=test-key&alt=sse")]
    public void CreateHttpRequestBinding_WhenStreamRequest_ShouldUseStreamGenerateContentEndpoint(
        string baseUrl,
        string expectedEndpoint)
    {
        IProviderResponsesTransportProtocolBinding binding = new GoogleGenerativeTransportProtocolBinding();

        var request = binding.CreateHttpRequestBinding(
            baseUrl,
            new ProviderResponsesTransportHttpRequestContext(
                ApiKey: "test-key",
                StickyTurnState: null,
                TurnMetadataHeader: null,
                Kind: ProviderResponsesTransportHttpRequestKind.StreamRequest,
                Model: "gemini-2.5-pro",
                TraceParent: "00-11111111111111111111111111111111-2222222222222222-01"));

        Assert.Equal(expectedEndpoint, request.Endpoint);
        Assert.Equal("text/event-stream", request.Headers["Accept"]);
        Assert.Equal("00-11111111111111111111111111111111-2222222222222222-01", request.Headers["traceparent"]);
        Assert.DoesNotContain("Authorization", request.Headers.Keys);
    }

    [Theory]
    [InlineData("https://generativelanguage.googleapis.com", "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-pro:generateContent?key=test-key")]
    [InlineData("https://generativelanguage.googleapis.com/v1beta", "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-pro:generateContent?key=test-key")]
    public void CreateHttpRequestBinding_WhenJsonRequest_ShouldUseGenerateContentEndpoint(
        string baseUrl,
        string expectedEndpoint)
    {
        IProviderResponsesTransportProtocolBinding binding = new GoogleGenerativeTransportProtocolBinding();

        var request = binding.CreateHttpRequestBinding(
            baseUrl,
            new ProviderResponsesTransportHttpRequestContext(
                ApiKey: "test-key",
                StickyTurnState: null,
                TurnMetadataHeader: null,
                Kind: ProviderResponsesTransportHttpRequestKind.JsonRequest,
                Model: "models/gemini-2.5-pro",
                TraceParent: "00-11111111111111111111111111111111-2222222222222222-01"));

        Assert.Equal(expectedEndpoint, request.Endpoint);
        Assert.Equal("00-11111111111111111111111111111111-2222222222222222-01", request.Headers["traceparent"]);
    }
}
