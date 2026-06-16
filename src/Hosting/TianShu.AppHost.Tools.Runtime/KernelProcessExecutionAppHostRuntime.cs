using System.Collections.Concurrent;
using System.Diagnostics;
using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// 进程执行与后台终端跟踪宿主运行时。
/// Host runtime for process execution and background terminal tracking.
/// </summary>
internal sealed class KernelProcessExecutionAppHostRuntime : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Process>> backgroundTerminalsByThread = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<int, KernelManagedNetworkExecutionLease> backgroundManagedNetworkLeasesByPid = new();

    public async Task<int> StartBackgroundCommandAsync(
        IReadOnlyList<string> command,
        string? cwd,
        string threadId,
        string processId,
        IReadOnlyDictionary<string, string>? environment = null,
        KernelManagedNetworkExecutionLease? managedNetworkLease = null,
        Func<Process, Task>? onExited = null,
        string? turnId = null,
        string? itemId = null,
        string? commandText = null,
        Func<string, string, string, string, string, string?, CancellationToken, Task>? emitCommandExecutionStartedNotificationAsync = null)
    {
        if (command.Count == 0)
        {
            throw new InvalidOperationException("command must not be empty");
        }

        var workingDirectory = string.IsNullOrWhiteSpace(cwd) ? Environment.CurrentDirectory : cwd!;
        var startInfo = new ProcessStartInfo
        {
            FileName = command[0],
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };
        for (var i = 1; i < command.Count; i++)
        {
            startInfo.ArgumentList.Add(command[i]);
        }

        if (environment is not null)
        {
            startInfo.Environment.Clear();
            foreach (var variable in environment)
            {
                startInfo.Environment[variable.Key] = variable.Value;
            }
        }

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("failed to start process");
        }

        if (emitCommandExecutionStartedNotificationAsync is not null
            && !string.IsNullOrWhiteSpace(turnId)
            && !string.IsNullOrWhiteSpace(itemId)
            && !string.IsNullOrWhiteSpace(commandText))
        {
            await emitCommandExecutionStartedNotificationAsync(
                threadId,
                turnId!,
                itemId!,
                commandText!,
                workingDirectory,
                processId,
                CancellationToken.None).ConfigureAwait(false);
        }

        TrackBackgroundTerminal(threadId, processId, process, onExited);
        if (managedNetworkLease is { IsActive: true })
        {
            backgroundManagedNetworkLeasesByPid[process.Id] = managedNetworkLease;
        }

        return process.Id;
    }

    public int CleanBackgroundTerminals(string threadId)
    {
        if (!backgroundTerminalsByThread.TryRemove(threadId, out var threadProcesses))
        {
            return 0;
        }

        var cleaned = 0;
        foreach (var pair in threadProcesses.ToArray())
        {
            if (threadProcesses.TryRemove(pair.Key, out var process))
            {
                cleaned++;
                TryDisposeBackgroundManagedNetworkLease(process.Id);
                TryDisposeProcess(process, killIfRunning: true);
            }
        }

        return cleaned;
    }

    public async Task<KernelCommandRunResult> ExecuteCommandAsync(
        IReadOnlyList<string> command,
        string? cwd,
        int? timeoutMs,
        IReadOnlyDictionary<string, string>? environment,
        CancellationToken cancellationToken)
    {
        if (command.Count == 0)
        {
            return new KernelCommandRunResult(-1, string.Empty, "command must not be empty", TimedOut: false);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = command[0],
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = string.IsNullOrWhiteSpace(cwd) ? Environment.CurrentDirectory : cwd,
        };
        for (var i = 1; i < command.Count; i++)
        {
            startInfo.ArgumentList.Add(command[i]);
        }

        if (environment is not null)
        {
            startInfo.Environment.Clear();
            foreach (var variable in environment)
            {
                startInfo.Environment[variable.Key] = variable.Value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        // 避免子进程继承宿主 app-server 的长连接 stdin，导致 git 等命令卡在输入管道上。
        process.StandardInput.Close();
        var outTask = process.StandardOutput.ReadToEndAsync();
        var errTask = process.StandardError.ReadToEndAsync();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeoutMs is int effectiveTimeoutMs)
        {
            timeoutCts.CancelAfter(Math.Clamp(effectiveTimeoutMs, 1000, 300000));
        }

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // ignore
            }

            return new KernelCommandRunResult(-1, await outTask.ConfigureAwait(false), await errTask.ConfigureAwait(false), TimedOut: true);
        }

        return new KernelCommandRunResult(process.ExitCode, await outTask.ConfigureAwait(false), await errTask.ConfigureAwait(false), TimedOut: false);
    }

    public ValueTask DisposeAsync()
    {
        foreach (var threadId in backgroundTerminalsByThread.Keys.ToArray())
        {
            CleanBackgroundTerminals(threadId);
        }

        foreach (var pid in backgroundManagedNetworkLeasesByPid.Keys.ToArray())
        {
            TryDisposeBackgroundManagedNetworkLease(pid);
        }

        return ValueTask.CompletedTask;
    }

    private void TrackBackgroundTerminal(string threadId, string processId, Process process, Func<Process, Task>? onExited = null)
    {
        var threadProcesses = backgroundTerminalsByThread.GetOrAdd(
            threadId,
            static _ => new ConcurrentDictionary<string, Process>(StringComparer.Ordinal));
        if (!threadProcesses.TryAdd(processId, process))
        {
            TryDisposeProcess(process, killIfRunning: true);
            throw new InvalidOperationException($"duplicate process id: {processId}");
        }

        process.Exited += (_, _) =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    if (onExited is not null)
                    {
                        await onExited(process).ConfigureAwait(false);
                    }
                }
                finally
                {
                    UntrackBackgroundTerminal(threadId, processId, killIfRunning: false);
                }
            }, CancellationToken.None);
        };
    }

    private void UntrackBackgroundTerminal(string threadId, string processId, bool killIfRunning)
    {
        if (!backgroundTerminalsByThread.TryGetValue(threadId, out var threadProcesses))
        {
            return;
        }

        if (!threadProcesses.TryRemove(processId, out var process))
        {
            return;
        }

        TryDisposeBackgroundManagedNetworkLease(process.Id);
        TryDisposeProcess(process, killIfRunning);
        if (threadProcesses.IsEmpty)
        {
            backgroundTerminalsByThread.TryRemove(new KeyValuePair<string, ConcurrentDictionary<string, Process>>(threadId, threadProcesses));
        }
    }

    private void TryDisposeBackgroundManagedNetworkLease(int pid)
    {
        if (!backgroundManagedNetworkLeasesByPid.TryRemove(pid, out var lease))
        {
            return;
        }

        lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static void TryDisposeProcess(Process process, bool killIfRunning)
    {
        try
        {
            if (killIfRunning && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // ignore
        }

        try
        {
            process.Dispose();
        }
        catch
        {
            // ignore
        }
    }
}
