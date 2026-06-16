using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace TianShu.AppHost.Tools;

/// <summary>
/// unified-exec 会话注册表，负责分配与回收本地进程会话。
/// Unified-exec session registry responsible for allocating and cleaning up local process sessions.
/// </summary>
internal sealed class KernelCommittedUnifiedExecProcessManager
{
    private readonly ConcurrentDictionary<int, KernelCommittedUnifiedExecSession> sessions = new();
    private int sequence;

    public int NextSessionId() => Interlocked.Increment(ref sequence);

    public void StoreSession(int sessionId, KernelCommittedUnifiedExecSession session) => sessions[sessionId] = session;

    public bool TryGetSession(int sessionId, out KernelCommittedUnifiedExecSession? session) => sessions.TryGetValue(sessionId, out session);

    public void RemoveSession(int sessionId)
    {
        if (sessions.TryRemove(sessionId, out var session))
        {
            session.Dispose();
        }
    }
}

/// <summary>
/// unified-exec 本地进程会话运行时，承接 stdin/stdout/stderr 与 managed-network outcome 聚合。
/// Unified-exec local process runtime that owns stdin/stdout/stderr flow and managed-network outcome aggregation.
/// </summary>
internal sealed class KernelCommittedUnifiedExecSession : IDisposable
{
    private readonly Process process;
    private readonly IKernelManagedNetworkExecutionLease? managedNetworkLease;
    private readonly StringBuilder outputBuffer = new();
    private readonly object gate = new();
    private readonly Task stdoutDrainTask;
    private readonly Task stderrDrainTask;
    private int consumedChars;

    private KernelCommittedUnifiedExecSession(Process process, IKernelManagedNetworkExecutionLease? managedNetworkLease)
    {
        this.process = process;
        this.managedNetworkLease = managedNetworkLease;
        stdoutDrainTask = DrainAsync(process.StandardOutput);
        stderrDrainTask = DrainAsync(process.StandardError);
    }

    public static KernelCommittedUnifiedExecSession Start(
        IReadOnlyList<string> command,
        string cwd,
        IReadOnlyDictionary<string, string>? environment = null,
        IKernelManagedNetworkExecutionLease? managedNetworkLease = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command[0],
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = cwd,
        };
        foreach (var arg in command.Skip(1))
        {
            startInfo.ArgumentList.Add(arg);
        }

        if (environment is not null)
        {
            startInfo.Environment.Clear();
            foreach (var variable in environment)
            {
                startInfo.Environment[variable.Key] = variable.Value;
            }
        }

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start unified exec process.");
        return new KernelCommittedUnifiedExecSession(process, managedNetworkLease);
    }

    public int ProcessId => process.Id;

    public bool HasExited => process.HasExited;

    public int? ExitCode => process.HasExited ? process.ExitCode : null;

    public async Task WriteStdinAsync(string text, bool close, CancellationToken cancellationToken)
    {
        await process.StandardInput.WriteAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
        await process.StandardInput.FlushAsync().ConfigureAwait(false);
        if (close)
        {
            process.StandardInput.Close();
        }
    }

    public string ReadNewOutput(int? maxOutputTokens, out int? originalTokenCount)
    {
        string text;
        lock (gate)
        {
            text = consumedChars >= outputBuffer.Length
                ? string.Empty
                : outputBuffer.ToString(consumedChars, outputBuffer.Length - consumedChars);
            consumedChars = outputBuffer.Length;
        }

        var outcomeMessage = managedNetworkLease?.ConsumeOutcomeMessage() ?? string.Empty;
        if (string.IsNullOrEmpty(text))
        {
            text = outcomeMessage;
        }
        else if (!string.IsNullOrEmpty(outcomeMessage))
        {
            text = text + "\n" + outcomeMessage;
        }

        return KernelTextTruncator.FormattedTruncateTokens(
            text,
            maxOutputTokens ?? KernelCodeModeManager.DefaultMaxOutputTokens,
            out originalTokenCount);
    }

    public async Task WaitForOutputOrExitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (HasUnreadOutput() || managedNetworkLease?.HasRejectedOutcome == true)
            {
                return;
            }

            if (HasExited && stdoutDrainTask.IsCompleted && stderrDrainTask.IsCompleted)
            {
                return;
            }

            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
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
        }
        finally
        {
            process.Dispose();
            managedNetworkLease?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private bool HasUnreadOutput()
    {
        lock (gate)
        {
            return consumedChars < outputBuffer.Length;
        }
    }

    private async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[1024];
        while (true)
        {
            int read;
            try
            {
                read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            }
            catch
            {
                break;
            }

            if (read <= 0)
            {
                break;
            }

            lock (gate)
            {
                outputBuffer.Append(buffer, 0, read);
            }
        }
    }
}
