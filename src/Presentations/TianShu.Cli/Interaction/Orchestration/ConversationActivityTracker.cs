namespace TianShu.Cli.Interaction.Orchestration;

internal sealed class ConversationActivityTracker
{
    private int runningConversationCount;
    private bool sessionHasActiveTurn;
    private long lastObservedActivityUtcTicks = DateTime.UtcNow.Ticks;
    private long workingStartedUtcTicks;

    public bool IsBusy => Volatile.Read(ref runningConversationCount) > 0;

    public TimeSpan? WorkingElapsed
    {
        get
        {
            if (Volatile.Read(ref runningConversationCount) <= 0)
            {
                return null;
            }

            var startedTicks = Volatile.Read(ref workingStartedUtcTicks);
            if (startedTicks <= 0)
            {
                return null;
            }

            return DateTime.UtcNow - new DateTime(startedTicks, DateTimeKind.Utc);
        }
    }

    public void Reset()
    {
        sessionHasActiveTurn = false;
        Interlocked.Exchange(ref runningConversationCount, 0);
        Volatile.Write(ref workingStartedUtcTicks, 0);
        Touch();
    }

    public void ApplySessionActiveTurn(bool hasActiveTurn)
    {
        sessionHasActiveTurn = hasActiveTurn;
    }

    public void MarkTurnObserved()
    {
        sessionHasActiveTurn = true;
    }

    public void MarkTerminalTurn()
    {
        sessionHasActiveTurn = false;
    }

    public bool StartOperation()
    {
        Touch();
        if (Interlocked.Increment(ref runningConversationCount) == 1)
        {
            Volatile.Write(ref workingStartedUtcTicks, DateTime.UtcNow.Ticks);
            return true;
        }

        return false;
    }

    public bool CompleteOperation()
    {
        if (Interlocked.Decrement(ref runningConversationCount) <= 0)
        {
            Interlocked.Exchange(ref runningConversationCount, 0);
            Volatile.Write(ref workingStartedUtcTicks, 0);
            return true;
        }

        return false;
    }

    public bool HasSteerableConversation(string? lastObservedTurnId, string? lastObservedTurnStatus)
        => Volatile.Read(ref runningConversationCount) > 0
           || (sessionHasActiveTurn
               && !string.IsNullOrWhiteSpace(lastObservedTurnId)
               && !IsTerminalTurnStatus(lastObservedTurnStatus));

    public bool HasActiveConversation(string? lastObservedTurnStatus)
        => Volatile.Read(ref runningConversationCount) > 0
           || (sessionHasActiveTurn && !IsTerminalTurnStatus(lastObservedTurnStatus));

    public TimeSpan GetIdleDuration()
    {
        var lastActivityTicks = Interlocked.Read(ref lastObservedActivityUtcTicks);
        if (lastActivityTicks <= 0)
        {
            return TimeSpan.MaxValue;
        }

        var idleDuration = DateTime.UtcNow - new DateTime(lastActivityTicks, DateTimeKind.Utc);
        return idleDuration < TimeSpan.Zero ? TimeSpan.Zero : idleDuration;
    }

    public void Touch()
        => Interlocked.Exchange(ref lastObservedActivityUtcTicks, DateTime.UtcNow.Ticks);

    private static bool IsTerminalTurnStatus(string? status)
        => Normalize(status)?.ToLowerInvariant() switch
        {
            "completed" or "failed" or "errored" or "cancelled" or "canceled" or "interrupted" => true,
            _ => false,
        };

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
