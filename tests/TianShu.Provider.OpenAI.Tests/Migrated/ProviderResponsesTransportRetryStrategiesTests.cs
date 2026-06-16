using TianShu.Provider.Abstractions;
using TianShu.Provider.OpenAI;
using TianShu.Provider.OpenAICompatible;

namespace TianShu.Provider.OpenAI.Tests;

public sealed class ProviderResponsesTransportRetryStrategiesTests
{
    [Fact]
    public void Resolve_ShouldReturnOpenAiResponsesTransportRetryStrategy_ForResponsesWireApi()
    {
        var strategy = ProviderResponsesTransportRetryStrategies.Resolve("responses", "test.providerWireApi");

        var typed = Assert.IsType<OpenAiResponsesTransportRetryStrategy>(strategy);
        Assert.Equal("responses", typed.WireApi);
    }

    [Fact]
    public void Resolve_ShouldReturnChatCompletionsTransportRetryStrategy_ForOpenAiChatCompletionsProtocol()
    {
        var strategy = ProviderResponsesTransportRetryStrategies.Resolve("openai_chat_completions", "test.providerWireApi");

        var typed = Assert.IsType<OpenAiChatCompletionsTransportRetryStrategy>(strategy);
        Assert.Equal("openai_chat_completions", typed.WireApi);
    }

    [Fact]
    public void EvaluateHttpStreamRetry_WhenRetryableProviderFailure_ShouldRetryWithReconnectMessage()
    {
        IProviderResponsesTransportRetryStrategy strategy = new OpenAiResponsesTransportRetryStrategy();

        var decision = strategy.EvaluateHttpStreamRetry(
            new ProviderResponsesTransportFailure(
                ProviderResponsesTransportFailureKind.ProviderStreamFailure,
                IsRetryable: true,
                Message: "stream closed before response.completed"),
            retryIndex: 0,
            maxRetries: 1,
            baseDelay: TimeSpan.Zero,
            maxDelay: TimeSpan.FromSeconds(30),
            cancellationRequested: false);

        Assert.True(decision.ShouldRetry);
        Assert.False(decision.ShouldSwitchToHttpTransport);
        Assert.Equal("Reconnecting... 1/1", decision.RetryMessage);
    }

    [Fact]
    public void EvaluateWebSocketRetry_WhenUpgradeRequired_ShouldSwitchToHttpTransport()
    {
        IProviderResponsesTransportRetryStrategy strategy = new OpenAiResponsesTransportRetryStrategy();

        var decision = strategy.EvaluateWebSocketRetry(
            new ProviderResponsesTransportFailure(
                ProviderResponsesTransportFailureKind.WebSocketUpgradeRequired,
                IsRetryable: false,
                Message: "426 Upgrade Required"),
            retryIndex: 0,
            maxRetries: 2,
            baseDelay: TimeSpan.FromMilliseconds(100),
            maxDelay: TimeSpan.FromSeconds(30),
            cancellationRequested: false);

        Assert.False(decision.ShouldRetry);
        Assert.True(decision.ShouldSwitchToHttpTransport);
    }

    [Fact]
    public void EvaluateWebSocketRetry_WhenRetryBudgetExhausted_ShouldSwitchToHttpTransport()
    {
        IProviderResponsesTransportRetryStrategy strategy = new OpenAiResponsesTransportRetryStrategy();

        var decision = strategy.EvaluateWebSocketRetry(
            new ProviderResponsesTransportFailure(
                ProviderResponsesTransportFailureKind.WebSocketTransportFailure,
                IsRetryable: true,
                Message: "socket closed"),
            retryIndex: 1,
            maxRetries: 1,
            baseDelay: TimeSpan.FromMilliseconds(100),
            maxDelay: TimeSpan.FromSeconds(30),
            cancellationRequested: false);

        Assert.False(decision.ShouldRetry);
        Assert.True(decision.ShouldSwitchToHttpTransport);
    }
}
