namespace TianShu.AppHost.Tools;

/// <summary>
/// fs/watch 句柄与注册表，负责为宿主侧文件监听会话分配稳定 watchId。
/// fs/watch handle and registry responsible for allocating stable watch ids for host-side file watching sessions.
/// </summary>
internal sealed record KernelFsWatchHandle(string WatchId, string Path);

/// <summary>
/// fs/watch 运行时注册表，负责注册、注销和统一回收 watcher 生命周期。
/// fs/watch runtime registry that owns watcher registration, unregistration, and bulk disposal.
/// </summary>
internal sealed class KernelFsWatchManager : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<string, KernelFsWatchRegistration> registrations = new(StringComparer.Ordinal);

    public KernelFsWatchHandle Register(string path, Func<string, IReadOnlyList<string>, Task> onChanged)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(onChanged);

        var canonicalPath = Path.GetFullPath(path);
        var watchId = Guid.CreateVersion7().ToString();
        var registration = new KernelFsWatchRegistration(watchId, canonicalPath, onChanged);

        lock (gate)
        {
            registrations.Add(watchId, registration);
        }

        return new KernelFsWatchHandle(watchId, canonicalPath);
    }

    public async Task UnregisterAsync(string watchId)
    {
        KernelFsWatchRegistration? registration = null;
        lock (gate)
        {
            if (registrations.TryGetValue(watchId, out registration))
            {
                registrations.Remove(watchId);
            }
        }

        if (registration is not null)
        {
            await registration.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        KernelFsWatchRegistration[] snapshot;
        lock (gate)
        {
            snapshot = registrations.Values.ToArray();
            registrations.Clear();
        }

        foreach (var registration in snapshot)
        {
            await registration.DisposeAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>
/// 单个 fs/watch 注册项，封装 FileSystemWatcher 去抖与变更路径聚合逻辑。
/// Single fs/watch registration that encapsulates FileSystemWatcher debounce and changed-path aggregation.
/// </summary>
internal sealed class KernelFsWatchRegistration : IAsyncDisposable
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(200);

    private readonly object gate = new();
    private readonly string watchId;
    private readonly string watchedPath;
    private readonly bool watchingDirectory;
    private readonly string? filterFilePath;
    private readonly FileSystemWatcher watcher;
    private readonly Func<string, IReadOnlyList<string>, Task> onChanged;
    private readonly HashSet<string> pendingPaths = new(PathComparer);
    private readonly CancellationTokenSource shutdown = new();
    private Task? debounceTask;
    private bool disposed;

    public KernelFsWatchRegistration(
        string watchId,
        string watchedPath,
        Func<string, IReadOnlyList<string>, Task> onChanged)
    {
        this.watchId = watchId;
        this.watchedPath = watchedPath;
        this.onChanged = onChanged;

        watchingDirectory = Directory.Exists(watchedPath);
        filterFilePath = watchingDirectory ? null : watchedPath;

        var watchRoot = watchingDirectory
            ? watchedPath
            : Path.GetDirectoryName(watchedPath);
        if (string.IsNullOrWhiteSpace(watchRoot) || !Directory.Exists(watchRoot))
        {
            throw new InvalidOperationException($"fs/watch cannot watch path without an existing parent directory: {watchedPath}");
        }

        watcher = new FileSystemWatcher(watchRoot)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.LastWrite
                | NotifyFilters.CreationTime,
            EnableRaisingEvents = true,
        };

        watcher.Changed += OnChanged;
        watcher.Created += OnChanged;
        watcher.Deleted += OnChanged;
        watcher.Renamed += OnRenamed;
    }

    public async ValueTask DisposeAsync()
    {
        Task? pendingTask;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            pendingTask = debounceTask;
        }

        watcher.EnableRaisingEvents = false;
        watcher.Changed -= OnChanged;
        watcher.Created -= OnChanged;
        watcher.Deleted -= OnChanged;
        watcher.Renamed -= OnRenamed;
        shutdown.Cancel();
        watcher.Dispose();

        if (pendingTask is not null)
        {
            try
            {
                await pendingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        shutdown.Dispose();
    }

    private void OnChanged(object sender, FileSystemEventArgs args)
        => QueuePath(args.FullPath);

    private void OnRenamed(object sender, RenamedEventArgs args)
    {
        QueuePath(args.OldFullPath);
        QueuePath(args.FullPath);
    }

    private void QueuePath(string? path)
    {
        var normalized = NormalizeAbsolutePath(path);
        if (string.IsNullOrWhiteSpace(normalized) || !ShouldTrack(normalized))
        {
            return;
        }

        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            pendingPaths.Add(normalized);
            if (debounceTask is null || debounceTask.IsCompleted)
            {
                debounceTask = Task.Run(FlushLoopAsync);
            }
        }
    }

    private bool ShouldTrack(string path)
    {
        if (!watchingDirectory)
        {
            return PathComparer.Equals(path, filterFilePath);
        }

        return PathComparer.Equals(path, watchedPath)
               || path.StartsWith(watchedPath + Path.DirectorySeparatorChar, PathComparison);
    }

    private async Task FlushLoopAsync()
    {
        try
        {
            while (true)
            {
                await Task.Delay(DebounceInterval, shutdown.Token).ConfigureAwait(false);

                string[] changedPaths;
                lock (gate)
                {
                    if (pendingPaths.Count == 0)
                    {
                        debounceTask = null;
                        return;
                    }

                    changedPaths = pendingPaths
                        .OrderBy(static path => path, PathComparer)
                        .ToArray();
                    pendingPaths.Clear();
                }

                await onChanged(watchId, changedPaths).ConfigureAwait(false);

                lock (gate)
                {
                    if (pendingPaths.Count == 0)
                    {
                        debounceTask = null;
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            lock (gate)
            {
                debounceTask = null;
            }
        }
    }

    private static string? NormalizeAbsolutePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return Path.GetFullPath(path);
    }
}
