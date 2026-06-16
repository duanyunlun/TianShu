using TianShu.Contracts.Conversations;

namespace TianShu.Cli.Interaction.Orchestration;

/// <summary>
/// Tracks recently observed runtime events and coordinates command-side waiters.
/// 跟踪最近观测到的 runtime event，并协调 `/wait-*` 命令侧等待生命周期。
/// </summary>
internal sealed class ConversationEventWaiter
{
    private const int MaxObservedEvents = 256;

    private readonly object syncRoot = new();
    private readonly List<ControlPlaneConversationStreamEvent> observedEvents = [];
    private readonly List<EventWaitRegistration> eventWaiters = [];

    public int ObservedEventCount
    {
        get
        {
            lock (syncRoot)
            {
                return observedEvents.Count;
            }
        }
    }

    public int WaitingRegistrationCount
    {
        get
        {
            lock (syncRoot)
            {
                return eventWaiters.Count;
            }
        }
    }

    public void Reset()
    {
        lock (syncRoot)
        {
            observedEvents.Clear();
            eventWaiters.Clear();
        }
    }

    public async Task<bool> WaitForIdleAsync(
        Func<CancellationToken, Task> refreshSnapshotAsync,
        Func<bool> isIdle,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(refreshSnapshotAsync);
        ArgumentNullException.ThrowIfNull(isIdle);

        var deadlineUtc = DateTime.UtcNow + timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await refreshSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (isIdle())
            {
                return true;
            }

            if (DateTime.UtcNow >= deadlineUtc)
            {
                return isIdle();
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<ControlPlaneConversationStreamEvent?> WaitForEventAsync(
        Func<ControlPlaneConversationStreamEvent, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var waiter = new EventWaitRegistration(predicate);
        lock (syncRoot)
        {
            var matchedObservedEvent = observedEvents.LastOrDefault(predicate);
            if (matchedObservedEvent is not null)
            {
                return matchedObservedEvent;
            }

            eventWaiters.Add(waiter);
        }

        try
        {
            return await waiter.Completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
        finally
        {
            lock (syncRoot)
            {
                eventWaiters.Remove(waiter);
            }
        }
    }

    public void RecordObservedEventAndNotifyWaiters(ControlPlaneConversationStreamEvent streamEvent)
    {
        ArgumentNullException.ThrowIfNull(streamEvent);

        List<EventWaitRegistration>? matchedWaiters = null;
        lock (syncRoot)
        {
            observedEvents.Add(streamEvent);
            if (observedEvents.Count > MaxObservedEvents)
            {
                observedEvents.RemoveRange(0, observedEvents.Count - MaxObservedEvents);
            }

            foreach (var waiter in eventWaiters)
            {
                if (!waiter.Predicate(streamEvent))
                {
                    continue;
                }

                matchedWaiters ??= [];
                matchedWaiters.Add(waiter);
            }

            if (matchedWaiters is not null)
            {
                foreach (var waiter in matchedWaiters)
                {
                    eventWaiters.Remove(waiter);
                }
            }
        }

        if (matchedWaiters is null)
        {
            return;
        }

        foreach (var waiter in matchedWaiters)
        {
            waiter.Completion.TrySetResult(streamEvent);
        }
    }

    private sealed class EventWaitRegistration(Func<ControlPlaneConversationStreamEvent, bool> predicate)
    {
        public Func<ControlPlaneConversationStreamEvent, bool> Predicate { get; } = predicate;

        public TaskCompletionSource<ControlPlaneConversationStreamEvent> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
