using System.Collections.Concurrent;
using System.Threading.Channels;
using TianShu.Configuration;

namespace TianShu.Execution.Runtime;

internal enum KernelFileWatcherEventKind
{
    SkillsChanged,
}

internal sealed record KernelFileWatcherEvent(
    KernelFileWatcherEventKind Kind,
    IReadOnlyList<string> Paths);

internal sealed class KernelWatchRegistration : IDisposable
{
    private readonly KernelFileWatcher fileWatcher;
    private readonly string[] roots;
    private int disposed;

    public KernelWatchRegistration(KernelFileWatcher fileWatcher, string[] roots)
    {
        this.fileWatcher = fileWatcher;
        this.roots = roots;
    }

    public IReadOnlyList<string> Roots => roots;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        fileWatcher.UnregisterSkillsRoots(roots);
    }
}

internal sealed class KernelFileWatcher : IDisposable
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private readonly object gate = new();
    private readonly Dictionary<string, int> skillsRootRefCounts = new(PathComparer);
    private readonly Dictionary<string, FileSystemWatcher> watchers = new(PathComparer);
    private readonly List<Channel<KernelFileWatcherEvent>> subscribers = new();
    private readonly Channel<string[]> rawChanges = Channel.CreateUnbounded<string[]>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    });
    private readonly CancellationTokenSource shutdown = new();
    private readonly Task eventLoopTask;
    private readonly string homePath;
    private readonly string systemConfigRoot;
    private readonly TimeSpan throttleInterval;
    private readonly Func<string, IReadOnlyList<string>>? resolveSkillRoots;
    private bool disposed;

    public KernelFileWatcher(
        string? tianShuHome = null,
        TimeSpan? throttleInterval = null,
        Func<string, IReadOnlyList<string>>? resolveSkillRoots = null,
        string? systemConfigRoot = null)
    {
        homePath = NormalizePath(tianShuHome) ?? TianShuHomePathUtilities.ResolveTianShuHomePath();
        this.systemConfigRoot = NormalizePath(systemConfigRoot) ?? TianShuSkillRootPaths.ResolveDefaultSystemConfigRoot();
        this.throttleInterval = throttleInterval ?? ResolveThrottleInterval();
        this.resolveSkillRoots = resolveSkillRoots;
        if (this.throttleInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(throttleInterval), "Throttle interval must be positive.");
        }

        eventLoopTask = Task.Run(ProcessRawChangesAsync);
    }


    private static TimeSpan ResolveThrottleInterval()
    {
        var configured = Normalize(Environment.GetEnvironmentVariable("TIANSHU_FILE_WATCHER_THROTTLE_MS"))
                         ?? Normalize(Environment.GetEnvironmentVariable("TIANSHU_FILE_WATCHER_THROTTLE_MS"));
        if (int.TryParse(configured, out var milliseconds) && milliseconds > 0)
        {
            return TimeSpan.FromMilliseconds(milliseconds);
        }

        return TimeSpan.FromSeconds(10);
    }
    public ChannelReader<KernelFileWatcherEvent> Subscribe()
    {
        var channel = Channel.CreateUnbounded<KernelFileWatcherEvent>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

        lock (gate)
        {
            ThrowIfDisposed();
            subscribers.Add(channel);
        }

        return channel.Reader;
    }

    public KernelWatchRegistration RegisterThread(KernelThreadSessionState session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return RegisterSkillsRoots(ResolveThreadSkillRoots(session.Cwd));
    }

    public KernelWatchRegistration RegisterSkillsRoots(IEnumerable<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);

        string[] normalizedRoots;
        lock (gate)
        {
            ThrowIfDisposed();
            normalizedRoots = roots
                .Select(NormalizePath)
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Distinct(PathComparer)
                .OrderBy(static path => path, PathComparer)
                .ToArray()!;

            foreach (var root in normalizedRoots)
            {
                RegisterSkillsRootUnsafe(root);
            }
        }

        return new KernelWatchRegistration(this, normalizedRoots);
    }

    public IReadOnlyList<string> GetRegisteredSkillsRoots()
    {
        lock (gate)
        {
            return skillsRootRefCounts.Keys
                .OrderBy(static root => root, PathComparer)
                .ToArray();
        }
    }

    public int GetSkillsRootReferenceCount(string root)
    {
        var normalized = NormalizePath(root);
        if (normalized is null)
        {
            return 0;
        }

        lock (gate)
        {
            return skillsRootRefCounts.TryGetValue(normalized, out var count) ? count : 0;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        shutdown.Cancel();
        rawChanges.Writer.TryComplete();

        try
        {
            eventLoopTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // ignore shutdown wait failures
        }

        List<FileSystemWatcher> watchersToDispose;
        List<Channel<KernelFileWatcherEvent>> subscribersToComplete;
        lock (gate)
        {
            watchersToDispose = watchers.Values.ToList();
            watchers.Clear();
            skillsRootRefCounts.Clear();
            subscribersToComplete = subscribers.ToList();
            subscribers.Clear();
        }

        foreach (var watcher in watchersToDispose)
        {
            watcher.Dispose();
        }

        foreach (var subscriber in subscribersToComplete)
        {
            subscriber.Writer.TryComplete();
        }

        shutdown.Dispose();
    }

    internal void UnregisterSkillsRoots(IReadOnlyList<string> roots)
    {
        List<FileSystemWatcher>? watchersToDispose = null;

        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            foreach (var root in roots)
            {
                if (!skillsRootRefCounts.TryGetValue(root, out var count))
                {
                    continue;
                }

                if (count > 1)
                {
                    skillsRootRefCounts[root] = count - 1;
                    continue;
                }

                skillsRootRefCounts.Remove(root);
                if (!watchers.Remove(root, out var watcher))
                {
                    continue;
                }

                watchersToDispose ??= new List<FileSystemWatcher>();
                watchersToDispose.Add(watcher);
            }
        }

        if (watchersToDispose is null)
        {
            return;
        }

        foreach (var watcher in watchersToDispose)
        {
            watcher.Dispose();
        }
    }

    private IReadOnlyList<string> ResolveThreadSkillRoots(string cwd)
    {
        if (resolveSkillRoots is null)
        {
            return ResolveDefaultSkillRoots(cwd).ToArray();
        }

        try
        {
            return resolveSkillRoots(cwd);
        }
        catch
        {
            return ResolveDefaultSkillRoots(cwd).ToArray();
        }
    }

    private IEnumerable<string> ResolveDefaultSkillRoots(string cwd)
    {
        yield return TianShuSkillPackageConfiguration.ResolveUserSkillRootDirectory(homePath);

        var home = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (!string.IsNullOrWhiteSpace(home))
        {
            yield return Path.Combine(home!, ".agents", "skills");
        }

        yield return TianShuSkillRootPaths.ResolveSystemSkillsCacheRoot(homePath);
        yield return TianShuSkillRootPaths.ResolveAdminSkillsRoot(systemConfigRoot);

        var normalizedCwd = NormalizePath(cwd);
        if (!string.IsNullOrWhiteSpace(normalizedCwd))
        {
            var projectDirectories = TianShuProjectRootResolver.EnumerateDirectoriesBetweenProjectRootAndCwd(normalizedCwd);
            foreach (var directory in projectDirectories)
            {
                yield return Path.Combine(directory, ".tianshu", "skills");
            }

            foreach (var directory in projectDirectories)
            {
                yield return Path.Combine(directory, ".agents", "skills");
            }
        }
    }

    private void RegisterSkillsRootUnsafe(string root)
    {
        if (skillsRootRefCounts.TryGetValue(root, out var count))
        {
            skillsRootRefCounts[root] = count + 1;
            return;
        }

        skillsRootRefCounts[root] = 1;
        TryCreateWatcherUnsafe(root);
    }

    private void TryCreateWatcherUnsafe(string root)
    {
        if (watchers.ContainsKey(root) || !Directory.Exists(root))
        {
            return;
        }

        var watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.LastWrite
                | NotifyFilters.CreationTime,
        };

        watcher.Changed += OnWatcherChanged;
        watcher.Created += OnWatcherChanged;
        watcher.Deleted += OnWatcherChanged;
        watcher.Renamed += OnWatcherRenamed;
        watcher.EnableRaisingEvents = true;

        watchers[root] = watcher;
    }

    private void OnWatcherChanged(object sender, FileSystemEventArgs args)
    {
        QueueRawPaths(args.FullPath);
    }

    private void OnWatcherRenamed(object sender, RenamedEventArgs args)
    {
        QueueRawPaths(args.OldFullPath, args.FullPath);
    }

    private void QueueRawPaths(params string?[] paths)
    {
        string[] normalizedPaths;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            normalizedPaths = paths
                .Select(NormalizePath)
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Distinct(PathComparer)
                .ToArray()!;
        }

        if (normalizedPaths.Length == 0)
        {
            return;
        }

        _ = rawChanges.Writer.TryWrite(normalizedPaths);
    }

    private async Task ProcessRawChangesAsync()
    {
        var pending = new HashSet<string>(PathComparer);
        var nextAllowedAt = DateTimeOffset.MinValue;

        try
        {
            while (true)
            {
                if (pending.Count == 0)
                {
                    if (!await rawChanges.Reader.WaitToReadAsync(shutdown.Token).ConfigureAwait(false))
                    {
                        break;
                    }

                    DrainAvailablePaths(pending);
                    if (pending.Count == 0)
                    {
                        continue;
                    }
                }

                var now = DateTimeOffset.UtcNow;
                if (now >= nextAllowedAt)
                {
                    PublishPending(pending);
                    nextAllowedAt = DateTimeOffset.UtcNow + throttleInterval;
                    continue;
                }

                var waitToReadTask = rawChanges.Reader.WaitToReadAsync(shutdown.Token).AsTask();
                var delayTask = Task.Delay(nextAllowedAt - now, shutdown.Token);
                var completed = await Task.WhenAny(waitToReadTask, delayTask).ConfigureAwait(false);

                if (completed == delayTask)
                {
                    PublishPending(pending);
                    nextAllowedAt = DateTimeOffset.UtcNow + throttleInterval;
                    continue;
                }

                if (!await waitToReadTask.ConfigureAwait(false))
                {
                    break;
                }

                DrainAvailablePaths(pending);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (pending.Count > 0)
            {
                PublishPending(pending);
            }
        }
    }

    private void DrainAvailablePaths(HashSet<string> pending)
    {
        while (rawChanges.Reader.TryRead(out var batch))
        {
            foreach (var path in batch)
            {
                if (IsSkillsPath(path))
                {
                    pending.Add(path);
                }
            }
        }
    }

    private bool IsSkillsPath(string path)
    {
        lock (gate)
        {
            return skillsRootRefCounts.Keys.Any(root => path.StartsWith(root, PathComparison));
        }
    }

    private void PublishPending(HashSet<string> pending)
    {
        if (pending.Count == 0)
        {
            return;
        }

        var payload = pending
            .OrderBy(static path => path, PathComparer)
            .ToArray();
        pending.Clear();

        var evt = new KernelFileWatcherEvent(KernelFileWatcherEventKind.SkillsChanged, payload);
        Channel<KernelFileWatcherEvent>[] snapshot;
        lock (gate)
        {
            snapshot = subscribers.ToArray();
        }

        foreach (var subscriber in snapshot)
        {
            _ = subscriber.Writer.TryWrite(evt);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return Path.GetFullPath(path.Trim());
    }
}
