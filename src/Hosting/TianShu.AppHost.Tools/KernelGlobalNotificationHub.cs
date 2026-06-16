using System.Collections.Concurrent;
using System.Threading;

namespace TianShu.AppHost.Tools;

internal sealed class KernelGlobalNotificationHub
{
    private readonly ConcurrentDictionary<long, RegistrationState> registrations = new();
    private long registrationSequence;

    public KernelGlobalNotificationRegistration Register(Func<string, bool> tryPublish, Action? disconnect)
    {
        ArgumentNullException.ThrowIfNull(tryPublish);

        var registrationId = Interlocked.Increment(ref registrationSequence);
        var state = new RegistrationState(tryPublish, disconnect);
        registrations[registrationId] = state;
        return new KernelGlobalNotificationRegistration(this, registrationId);
    }

    public void Unregister(long registrationId)
    {
        registrations.TryRemove(registrationId, out _);
    }

    public void MarkInitialized(long registrationId)
    {
        if (registrations.TryGetValue(registrationId, out var state))
        {
            state.MarkInitialized();
        }
    }

    public bool IsInitialized(long registrationId)
        => registrations.TryGetValue(registrationId, out var state) && state.IsInitialized;

    public Task BroadcastAsync(string json, long? excludedRegistrationId, CancellationToken cancellationToken)
    {
        foreach (var entry in registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (excludedRegistrationId.HasValue && entry.Key == excludedRegistrationId.Value)
            {
                continue;
            }

            if (!entry.Value.IsInitialized)
            {
                continue;
            }

            if (entry.Value.TryPublish(json))
            {
                continue;
            }

            if (registrations.TryRemove(entry.Key, out var removed))
            {
                removed.Disconnect();
            }
        }

        return Task.CompletedTask;
    }

    private sealed class RegistrationState(Func<string, bool> tryPublish, Action? disconnect)
    {
        private int initialized;

        public bool IsInitialized => Volatile.Read(ref initialized) != 0;

        public void MarkInitialized()
        {
            Volatile.Write(ref initialized, 1);
        }

        public bool TryPublish(string json)
            => tryPublish(json);

        public void Disconnect()
            => disconnect?.Invoke();
    }
}

internal sealed class KernelGlobalNotificationRegistration : IDisposable
{
    private readonly KernelGlobalNotificationHub hub;
    private int disposed;

    internal KernelGlobalNotificationRegistration(KernelGlobalNotificationHub hub, long id)
    {
        this.hub = hub;
        Id = id;
    }

    public long Id { get; }

    public bool IsInitialized => hub.IsInitialized(Id);

    public void MarkInitialized()
    {
        hub.MarkInitialized(Id);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        hub.Unregister(Id);
    }
}
