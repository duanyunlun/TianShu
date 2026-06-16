using TianShu.Provider.Abstractions;

namespace TianShu.Provider.Google;

/// <summary>
/// Google Generative transport 的重试策略。
/// Retry strategy for Google Generative transport.
/// </summary>
public sealed class GoogleGenerativeTransportRetryStrategy : IProviderResponsesTransportRetryStrategy
{
    /// <inheritdoc />
    public string WireApi => ProviderWireApi.GoogleGenerative;

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
        => ProviderResponsesTransportRetryDecision.SwitchToHttpTransport;

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
