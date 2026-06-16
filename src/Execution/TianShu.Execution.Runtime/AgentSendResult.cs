namespace TianShu.Execution.Runtime;

internal sealed class AgentSendResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public string? RawPayload { get; init; }

    public string? TurnId { get; init; }

    public string? TurnStatus { get; init; }

    public string? CorrelationId { get; init; }

    public FollowUpMode? RequestedMode { get; init; }

    public FollowUpMode? EffectiveMode { get; init; }

    public static AgentSendResult Ok(
        string message,
        string? rawPayload = null,
        string? turnId = null,
        string? turnStatus = null,
        string? correlationId = null,
        FollowUpMode? requestedMode = null,
        FollowUpMode? effectiveMode = null)
        => new()
        {
            Success = true,
            Message = message,
            RawPayload = rawPayload,
            TurnId = turnId,
            TurnStatus = turnStatus,
            CorrelationId = correlationId,
            RequestedMode = requestedMode,
            EffectiveMode = effectiveMode,
        };

    public static AgentSendResult Fail(
        string message,
        string? rawPayload = null,
        string? turnId = null,
        string? turnStatus = null,
        string? correlationId = null,
        FollowUpMode? requestedMode = null,
        FollowUpMode? effectiveMode = null)
        => new()
        {
            Success = false,
            Message = message,
            RawPayload = rawPayload,
            TurnId = turnId,
            TurnStatus = turnStatus,
            CorrelationId = correlationId,
            RequestedMode = requestedMode,
            EffectiveMode = effectiveMode,
        };
}

