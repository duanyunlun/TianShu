using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading.Channels;

namespace TianShu.Execution.Runtime;

internal enum KernelRealtimeEventParser
{
    V1,
    RealtimeV2,
}

internal enum KernelRealtimeSessionMode
{
    Conversational,
    Transcription,
}

internal sealed record KernelRealtimeTranscriptEntry(
    string Role,
    string Text);

internal sealed class KernelRealtimeSessionState
{
    private readonly List<string> textInputs = new();
    private readonly List<KernelRealtimeTranscriptEntry> activeTranscript = new();
    private readonly object gate = new();
    private readonly object transportGate = new();
    private int audioChunkCount;
    private int startedNotificationWritten;
    private int closedNotificationWritten;
    private string sessionId;
    private ClientWebSocket? transportSocket;
    private CancellationTokenSource? transportCancellation;
    private Task? transportTask;

    public KernelRealtimeSessionState(
        string threadId,
        string sessionId,
        string effectiveInstructions,
        string? model,
        string? realtimeWebSocketBaseUrl,
        KernelRealtimeEventParser eventParser = KernelRealtimeEventParser.V1,
        KernelRealtimeSessionMode sessionMode = KernelRealtimeSessionMode.Conversational)
    {
        ThreadId = threadId;
        this.sessionId = sessionId;
        EffectiveInstructions = effectiveInstructions;
        Model = model;
        RealtimeWebSocketBaseUrl = realtimeWebSocketBaseUrl;
        EventParser = eventParser;
        SessionMode = sessionMode;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public string ThreadId { get; }

    public string SessionId
    {
        get
        {
            lock (gate)
            {
                return sessionId;
            }
        }
    }

    public string EffectiveInstructions { get; }

    public string? Model { get; }

    public string? RealtimeWebSocketBaseUrl { get; }

    public KernelRealtimeEventParser EventParser { get; }

    public KernelRealtimeSessionMode SessionMode { get; }

    public DateTimeOffset StartedAt { get; }

    public int AudioChunkCount => Volatile.Read(ref audioChunkCount);

    public int TextChunkCount
    {
        get
        {
            lock (gate)
            {
                return textInputs.Count;
            }
        }
    }

    public bool UsesRealtimeWebSocket => !string.IsNullOrWhiteSpace(RealtimeWebSocketBaseUrl);

    public ClientWebSocket? TransportSocket
    {
        get
        {
            lock (transportGate)
            {
                return transportSocket;
            }
        }
    }

    public void UpdateSessionId(string updatedSessionId)
    {
        if (string.IsNullOrWhiteSpace(updatedSessionId))
        {
            return;
        }

        lock (gate)
        {
            sessionId = updatedSessionId;
        }
    }

    public string? AddText(string? text)
    {
        var normalized = KernelRuntimeJsonHelpers.Normalize(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        lock (gate)
        {
            textInputs.Add(normalized!);
        }

        return normalized;
    }

    public void AppendTranscriptDelta(string role, string delta)
    {
        if (string.IsNullOrEmpty(delta))
        {
            return;
        }

        lock (gate)
        {
            if (activeTranscript.Count > 0
                && string.Equals(activeTranscript[^1].Role, role, StringComparison.Ordinal))
            {
                activeTranscript[^1] = activeTranscript[^1] with
                {
                    Text = activeTranscript[^1].Text + delta,
                };
                return;
            }

            activeTranscript.Add(new KernelRealtimeTranscriptEntry(role, delta));
        }
    }

    public int AddAudio()
    {
        return Interlocked.Increment(ref audioChunkCount);
    }

    public string BuildTextSummary()
    {
        lock (gate)
        {
            if (textInputs.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(Environment.NewLine, textInputs);
        }
    }

    public IReadOnlyList<KernelRealtimeTranscriptEntry> DrainActiveTranscript()
    {
        lock (gate)
        {
            if (activeTranscript.Count == 0)
            {
                return Array.Empty<KernelRealtimeTranscriptEntry>();
            }

            var snapshot = activeTranscript.ToArray();
            activeTranscript.Clear();
            return snapshot;
        }
    }

    public void AttachTransport(ClientWebSocket socket, CancellationTokenSource cancellation, Task task)
    {
        lock (transportGate)
        {
            transportSocket = socket;
            transportCancellation = cancellation;
            transportTask = task;
        }
    }

    public (ClientWebSocket? Socket, CancellationTokenSource? Cancellation, Task? Task) DetachTransport()
    {
        lock (transportGate)
        {
            var socket = transportSocket;
            var cancellation = transportCancellation;
            var task = transportTask;
            transportSocket = null;
            transportCancellation = null;
            transportTask = null;
            return (socket, cancellation, task);
        }
    }

    public bool TryMarkStartedNotificationWritten()
    {
        return Interlocked.Exchange(ref startedNotificationWritten, 1) == 0;
    }

    public bool TryMarkClosedNotificationWritten()
    {
        return Interlocked.Exchange(ref closedNotificationWritten, 1) == 0;
    }
}

internal sealed class KernelRuntimeThread
{
    private readonly object gate = new();
    private KernelThreadRecord record;
    private KernelThreadSessionState session;
    private readonly Dictionary<string, string> dependencyEnvironment = OperatingSystem.IsWindows()
        ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        : new Dictionary<string, string>(StringComparer.Ordinal);
    private bool isLoaded;
    private string? activeTurnId;
    private KernelRealtimeSessionState? realtimeSession;
    private string? pendingRealtimeEndReason;
    private KernelWatchRegistration? watchRegistration;
    private ulong outOfBandElicitationCount;

    public KernelRuntimeThread(
        KernelThreadRecord record,
        KernelThreadSessionState session,
        bool isLoaded,
        KernelWatchRegistration? watchRegistration = null)
    {
        this.record = record;
        this.session = session;
        this.isLoaded = isLoaded;
        this.watchRegistration = watchRegistration;
    }

    public string Id => record.Id;

    public KernelThreadRecord Record
    {
        get
        {
            lock (gate)
            {
                return record;
            }
        }
    }

    public KernelThreadSessionState Session
    {
        get
        {
            lock (gate)
            {
                return session;
            }
        }
    }

    public bool IsLoaded
    {
        get
        {
            lock (gate)
            {
                return isLoaded;
            }
        }
    }

    public string? ActiveTurnId
    {
        get
        {
            lock (gate)
            {
                return activeTurnId;
            }
        }
    }

    public KernelRealtimeSessionState? RealtimeSession
    {
        get
        {
            lock (gate)
            {
                return realtimeSession;
            }
        }
    }

    public KernelThreadConfigSnapshot ConfigSnapshot
    {
        get
        {
            lock (gate)
            {
                return KernelThreadConfigSnapshotFactory.FromSession(session);
            }
        }
    }

    public IReadOnlyList<string> WatchedSkillsRoots
    {
        get
        {
            lock (gate)
            {
                return watchRegistration?.Roots ?? Array.Empty<string>();
            }
        }
    }

    public void Update(KernelThreadRecord updatedRecord, KernelThreadSessionState? updatedSession = null, bool? loaded = null)
    {
        lock (gate)
        {
            record = updatedRecord;
            if (updatedSession is not null)
            {
                session = updatedSession;
            }

            if (loaded.HasValue)
            {
                isLoaded = loaded.Value;
            }
        }
    }

    public void UpdateSession(KernelThreadSessionState updatedSession)
    {
        lock (gate)
        {
            session = updatedSession;
        }
    }

    public Dictionary<string, string> CopyDependencyEnvironment()
    {
        lock (gate)
        {
            return new Dictionary<string, string>(dependencyEnvironment, dependencyEnvironment.Comparer);
        }
    }

    public void SetDependencyEnvironment(IReadOnlyDictionary<string, string>? values)
    {
        lock (gate)
        {
            dependencyEnvironment.Clear();
            if (values is null)
            {
                return;
            }

            foreach (var pair in values)
            {
                dependencyEnvironment[pair.Key] = pair.Value;
            }
        }
    }

    public void UpsertDependencyEnvironment(IReadOnlyDictionary<string, string> values)
    {
        lock (gate)
        {
            foreach (var pair in values)
            {
                dependencyEnvironment[pair.Key] = pair.Value;
            }
        }
    }

    public void ReplaceWatchRegistration(KernelWatchRegistration? registration)
    {
        KernelWatchRegistration? previous;
        lock (gate)
        {
            previous = watchRegistration;
            watchRegistration = registration;
        }

        previous?.Dispose();
    }

    public void MarkLoaded()
    {
        lock (gate)
        {
            isLoaded = true;
        }
    }

    public void MarkUnloaded()
    {
        KernelWatchRegistration? registrationToDispose;
        lock (gate)
        {
            isLoaded = false;
            registrationToDispose = watchRegistration;
            watchRegistration = null;
        }

        registrationToDispose?.Dispose();
    }

    public void SetActiveTurn(string? turnId)
    {
        lock (gate)
        {
            activeTurnId = turnId;
        }
    }

    public bool ClearActiveTurn(string turnId)
    {
        lock (gate)
        {
            if (!string.Equals(activeTurnId, turnId, StringComparison.Ordinal))
            {
                return false;
            }

            activeTurnId = null;
            return true;
        }
    }

    public void SetRealtimeSession(KernelRealtimeSessionState? sessionState)
    {
        lock (gate)
        {
            if (sessionState is null && realtimeSession is not null)
            {
                pendingRealtimeEndReason ??= "inactive";
            }
            else if (sessionState is not null)
            {
                pendingRealtimeEndReason = null;
            }

            realtimeSession = sessionState;
        }
    }

    public bool ProviderHttpFallbackEnabled
    {
        get
        {
            lock (gate)
            {
                return session.ProviderHttpFallbackEnabled;
            }
        }
    }

    public void MarkProviderHttpFallbackEnabled()
    {
        lock (gate)
        {
            if (session.ProviderHttpFallbackEnabled)
            {
                return;
            }

            session = session with { ProviderHttpFallbackEnabled = true };
        }
    }

    public void ResetProviderHttpFallback()
    {
        lock (gate)
        {
            if (!session.ProviderHttpFallbackEnabled)
            {
                return;
            }

            session = session with { ProviderHttpFallbackEnabled = false };
        }
    }

    public string? ConsumePendingRealtimeEndReason()
    {
        lock (gate)
        {
            var reason = pendingRealtimeEndReason;
            pendingRealtimeEndReason = null;
            return reason;
        }
    }

    public ulong IncrementOutOfBandElicitationCount()
    {
        lock (gate)
        {
            checked
            {
                outOfBandElicitationCount++;
            }

            return outOfBandElicitationCount;
        }
    }

    public bool TryDecrementOutOfBandElicitationCount(out ulong count)
    {
        lock (gate)
        {
            if (outOfBandElicitationCount == 0)
            {
                count = 0;
                return false;
            }

            outOfBandElicitationCount--;
            count = outOfBandElicitationCount;
            return true;
        }
    }
}

internal sealed class KernelThreadManager : IDisposable
{
    private readonly ConcurrentDictionary<string, KernelRuntimeThread> threads = new(StringComparer.Ordinal);
    private readonly object subscriptionGate = new();
    private readonly List<Channel<string>> createdSubscribers = new();
    private readonly KernelFileWatcher fileWatcher;

    public KernelThreadManager(KernelFileWatcher? fileWatcher = null)
    {
        this.fileWatcher = fileWatcher ?? new KernelFileWatcher();
    }

    public KernelRuntimeThread AttachThread(
        KernelThreadRecord record,
        KernelThreadSessionState session,
        bool loaded,
        bool publishCreated)
    {
        var thread = threads.AddOrUpdate(
            record.Id,
            _ => new KernelRuntimeThread(record, session, loaded),
            (_, existing) =>
            {
                existing.Update(record, session, loaded ? true : existing.IsLoaded);
                return existing;
            });

        if (loaded)
        {
            thread.MarkLoaded();
            RefreshWatchRegistration(thread);
        }

        if (publishCreated)
        {
            PublishCreated(record.Id);
        }

        return thread;
    }

    public KernelRuntimeThread GetOrAttachThread(
        KernelThreadRecord record,
        Func<KernelThreadRecord, KernelThreadSessionState> sessionFactory,
        bool loaded)
    {
        var thread = threads.AddOrUpdate(
            record.Id,
            _ => new KernelRuntimeThread(record, sessionFactory(record), loaded),
            (_, existing) =>
            {
                existing.Update(record, loaded: loaded ? true : existing.IsLoaded);
                return existing;
            });

        if (loaded)
        {
            thread.MarkLoaded();
            RefreshWatchRegistration(thread);
        }

        return thread;
    }

    public bool TryGetThread(string threadId, out KernelRuntimeThread? thread)
        => threads.TryGetValue(threadId, out thread);

    public bool IsLoaded(string threadId)
        => threads.TryGetValue(threadId, out var thread) && thread.IsLoaded;

    public void MarkLoaded(string threadId)
    {
        if (threads.TryGetValue(threadId, out var thread))
        {
            thread.MarkLoaded();
            RefreshWatchRegistration(thread);
        }
    }

    public void MarkUnloaded(string threadId)
    {
        if (threads.TryGetValue(threadId, out var thread))
        {
            thread.MarkUnloaded();
        }
    }

    public bool RemoveThread(string threadId)
    {
        if (!threads.TryRemove(threadId, out var thread))
        {
            return false;
        }

        thread.MarkUnloaded();
        return true;
    }

    public IReadOnlyList<string> ClearThreads()
    {
        var threadIds = threads.Keys.OrderBy(static threadId => threadId, StringComparer.Ordinal).ToArray();
        foreach (var threadId in threadIds)
        {
            _ = RemoveThread(threadId);
        }

        return threadIds;
    }

    public IReadOnlyList<string> GetLoadedThreadIds()
    {
        return threads.Values
            .Where(static thread => thread.IsLoaded)
            .Select(static thread => thread.Id)
            .OrderBy(static threadId => threadId, StringComparer.Ordinal)
            .ToArray();
    }

    public ChannelReader<string> SubscribeCreated()
    {
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

        lock (subscriptionGate)
        {
            createdSubscribers.Add(channel);
        }

        return channel.Reader;
    }

    public ChannelReader<KernelFileWatcherEvent> SubscribeFileWatcher()
        => fileWatcher.Subscribe();

    internal KernelFileWatcher FileWatcher => fileWatcher;

    public void Dispose()
    {
        fileWatcher.Dispose();
    }

    private void RefreshWatchRegistration(KernelRuntimeThread thread)
    {
        var registration = fileWatcher.RegisterThread(thread.Session);
        thread.ReplaceWatchRegistration(registration);
    }

    private void PublishCreated(string threadId)
    {
        Channel<string>[] subscribers;
        lock (subscriptionGate)
        {
            subscribers = createdSubscribers.ToArray();
        }

        foreach (var subscriber in subscribers)
        {
            _ = subscriber.Writer.TryWrite(threadId);
        }
    }
}





