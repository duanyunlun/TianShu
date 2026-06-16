using System.Collections.Concurrent;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Sessions;

namespace TianShu.Cli.Interaction.Orchestration;

/// <summary>
/// Tracks the CLI-visible chat session identity and terminal turn status.
/// 跟踪 CLI 可见的 chat 会话标识、当前线程、回合状态与用户主动中断预期。
/// </summary>
internal sealed class ChatSessionState
{
    private readonly ConcurrentDictionary<string, byte> expectedInterruptedTurns = new(StringComparer.Ordinal);
    private int pendingInterruptWithoutTurnIdCount;

    public string? LastObservedThreadId { get; private set; }

    public string? LastObservedTurnId { get; private set; }

    public string? LastObservedTurnStatus { get; private set; }

    public string? SessionActiveThreadId { get; private set; }

    public string CurrentDisplayModel { get; private set; } = "<config>";

    public string? LastFailureMessage { get; private set; }

    public string? CurrentThreadId => SessionActiveThreadId ?? LastObservedThreadId;

    public ChatSessionSnapshot Snapshot
        => new(
            LastObservedThreadId,
            LastObservedTurnId,
            LastObservedTurnStatus,
            SessionActiveThreadId,
            CurrentDisplayModel,
            LastFailureMessage,
            CurrentThreadId);

    public void Reset()
    {
        ClearCurrentThread();
        CurrentDisplayModel = "<config>";
        LastFailureMessage = null;
    }

    public void ClearCurrentThread()
    {
        expectedInterruptedTurns.Clear();
        LastObservedThreadId = null;
        LastObservedTurnId = null;
        LastObservedTurnStatus = null;
        SessionActiveThreadId = null;
        Interlocked.Exchange(ref pendingInterruptWithoutTurnIdCount, 0);
    }

    public void ApplySessionSnapshot(ControlPlaneSessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        SessionActiveThreadId = snapshot.ActiveThreadId?.Value;
        LastObservedThreadId = SessionActiveThreadId ?? LastObservedThreadId;
    }

    public string? ObserveStreamEvent(ControlPlaneConversationStreamEvent streamEvent)
    {
        ArgumentNullException.ThrowIfNull(streamEvent);
        var observedThreadId = streamEvent.ThreadId?.Value ?? SessionActiveThreadId ?? LastObservedThreadId;
        if (!string.IsNullOrWhiteSpace(observedThreadId))
        {
            SessionActiveThreadId = observedThreadId;
        }

        LastObservedThreadId = observedThreadId;
        LastObservedTurnId = streamEvent.TurnId?.Value ?? LastObservedTurnId;
        return observedThreadId;
    }

    public void ApplyTurnStatus(string? status)
    {
        LastObservedTurnStatus = status ?? LastObservedTurnStatus;
    }

    public void ApplyTurnResult(string? turnId, string? status)
    {
        LastObservedTurnId = turnId ?? LastObservedTurnId;
        LastObservedTurnStatus = status ?? LastObservedTurnStatus;
    }

    public void SetSessionActiveThreadId(string? threadId)
    {
        SessionActiveThreadId = threadId;
        LastObservedThreadId = threadId ?? LastObservedThreadId;
    }

    public void SetCurrentDisplayModel(string model)
    {
        CurrentDisplayModel = string.IsNullOrWhiteSpace(model) ? "<config>" : model;
    }

    public void SetLastFailureMessage(string? message)
    {
        LastFailureMessage = message;
    }

    public void RememberUserRequestedInterrupt(string? turnId)
    {
        var normalizedTurnId = Normalize(turnId);
        if (!string.IsNullOrWhiteSpace(normalizedTurnId))
        {
            expectedInterruptedTurns[normalizedTurnId!] = 0;
            return;
        }

        Interlocked.Increment(ref pendingInterruptWithoutTurnIdCount);
    }

    public bool TryConsumeUserRequestedInterrupt(string? turnId, string? turnStatus)
    {
        if (!string.Equals(Normalize(turnStatus), "interrupted", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalizedTurnId = Normalize(turnId);
        if (!string.IsNullOrWhiteSpace(normalizedTurnId) && expectedInterruptedTurns.TryRemove(normalizedTurnId!, out _))
        {
            return true;
        }

        while (true)
        {
            var pendingCount = Volatile.Read(ref pendingInterruptWithoutTurnIdCount);
            if (pendingCount <= 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref pendingInterruptWithoutTurnIdCount, pendingCount - 1, pendingCount) == pendingCount)
            {
                return true;
            }
        }
    }

    public static bool IsFailedTurnStatus(string? status)
        => string.Equals(Normalize(status), "failed", StringComparison.OrdinalIgnoreCase);

    public static bool IsTerminalTurnStatus(string? status)
        => Normalize(status)?.ToLowerInvariant() switch
        {
            "completed" or "failed" or "errored" or "cancelled" or "canceled" or "interrupted" => true,
            _ => false,
        };

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed record ChatSessionSnapshot(
    string? LastObservedThreadId,
    string? LastObservedTurnId,
    string? LastObservedTurnStatus,
    string? SessionActiveThreadId,
    string CurrentDisplayModel,
    string? LastFailureMessage,
    string? CurrentThreadId);
