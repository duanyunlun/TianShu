using TianShu.Provider.Abstractions;

namespace TianShu.Provider.OpenAI;

/// <summary>
/// OpenAI Responses transport 的重试/回退策略。
/// Retry/fallback strategy for the OpenAI Responses transport.
/// </summary>
public sealed class OpenAiResponsesTransportRetryStrategy : IProviderResponsesTransportRetryStrategy
{
    /// <inheritdoc />
    public string WireApi => "responses";

    /// <inheritdoc />
    public ProviderResponsesTransportRetryDecision EvaluateHttpStreamRetry(
        ProviderResponsesTransportFailure failure,
        int retryIndex,
        int maxRetries,
        TimeSpan baseDelay,
        TimeSpan maxDelay,
        bool cancellationRequested)
    {
        ArgumentNullException.ThrowIfNull(failure);

        if (cancellationRequested || retryIndex >= maxRetries)
        {
            return ProviderResponsesTransportRetryDecision.None;
        }

        return failure.Kind switch
        {
            ProviderResponsesTransportFailureKind.ProviderStreamFailure when failure.IsRetryable => BuildRetryDecision(retryIndex, maxRetries, baseDelay, maxDelay),
            ProviderResponsesTransportFailureKind.HttpRequestFailure => BuildRetryDecision(retryIndex, maxRetries, baseDelay, maxDelay),
            ProviderResponsesTransportFailureKind.IoFailure => BuildRetryDecision(retryIndex, maxRetries, baseDelay, maxDelay),
            ProviderResponsesTransportFailureKind.Timeout => BuildRetryDecision(retryIndex, maxRetries, baseDelay, maxDelay),
            ProviderResponsesTransportFailureKind.OperationCanceled when !cancellationRequested => BuildRetryDecision(retryIndex, maxRetries, baseDelay, maxDelay),
            _ => ProviderResponsesTransportRetryDecision.None,
        };
    }

    /// <inheritdoc />
    public ProviderResponsesTransportRetryDecision EvaluateWebSocketRetry(
        ProviderResponsesTransportFailure failure,
        int retryIndex,
        int maxRetries,
        TimeSpan baseDelay,
        TimeSpan maxDelay,
        bool cancellationRequested)
    {
        ArgumentNullException.ThrowIfNull(failure);

        if (cancellationRequested)
        {
            return ProviderResponsesTransportRetryDecision.None;
        }

        if (failure.Kind == ProviderResponsesTransportFailureKind.WebSocketUpgradeRequired
            || retryIndex >= maxRetries)
        {
            return ProviderResponsesTransportRetryDecision.SwitchToHttpTransport;
        }

        return failure.Kind switch
        {
            ProviderResponsesTransportFailureKind.ProviderStreamFailure when failure.IsRetryable => BuildRetryDecision(retryIndex, maxRetries, baseDelay, maxDelay),
            ProviderResponsesTransportFailureKind.WebSocketTransportFailure => BuildRetryDecision(retryIndex, maxRetries, baseDelay, maxDelay),
            ProviderResponsesTransportFailureKind.IoFailure => BuildRetryDecision(retryIndex, maxRetries, baseDelay, maxDelay),
            ProviderResponsesTransportFailureKind.Timeout => BuildRetryDecision(retryIndex, maxRetries, baseDelay, maxDelay),
            ProviderResponsesTransportFailureKind.OperationCanceled when !cancellationRequested => BuildRetryDecision(retryIndex, maxRetries, baseDelay, maxDelay),
            _ => ProviderResponsesTransportRetryDecision.None,
        };
    }

    private static ProviderResponsesTransportRetryDecision BuildRetryDecision(
        int retryIndex,
        int maxRetries,
        TimeSpan baseDelay,
        TimeSpan maxDelay)
    {
        var retryAttempt = retryIndex + 1;
        var delay = ComputeRetryDelay(retryAttempt, baseDelay, maxDelay);
        return ProviderResponsesTransportRetryDecision.Retry(
            delay,
            $"Reconnecting... {retryAttempt}/{maxRetries}");
    }

    private static TimeSpan ComputeRetryDelay(int retryAttempt, TimeSpan baseDelay, TimeSpan maxDelay)
    {
        if (baseDelay <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var factor = Math.Pow(2, Math.Max(retryAttempt - 1, 0));
        var delay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * factor);
        return delay <= maxDelay ? delay : maxDelay;
    }
}
