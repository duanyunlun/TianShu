using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace TianShu.AppHost.Tools;

/// <summary>
/// `command/exec` 本地进程运行时行为契约。
/// Runtime contract for local `command/exec` process execution.
/// </summary>
internal interface IKernelTrackedCommandExecRuntime : IDisposable
{
    int ProcessId { get; }

    Stream StdoutReader { get; }

    Stream StderrReader { get; }

    Task WriteStdinAsync(ReadOnlyMemory<byte> delta, CancellationToken cancellationToken);

    void CloseStdin();

    void RequestTerminate();

    void Resize(int rows, int cols);

    Task<int> WaitForExitAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 基于普通管道的 `command/exec` 本地进程运行时。
/// Pipe-backed local process runtime for `command/exec`.
/// </summary>
internal sealed class KernelPipeCommandExecRuntime : IKernelTrackedCommandExecRuntime
{
    private readonly Process process;
    private readonly bool stdinEnabled;

    private KernelPipeCommandExecRuntime(Process process, bool stdinEnabled)
    {
        this.process = process;
        this.stdinEnabled = stdinEnabled;
    }

    public int ProcessId => process.Id;

    public Stream StdoutReader => process.StandardOutput.BaseStream;

    public Stream StderrReader => process.StandardError.BaseStream;

    public static KernelPipeCommandExecRuntime Start(
        IReadOnlyList<string> command,
        string cwd,
        IReadOnlyDictionary<string, string> environment,
        bool stdinEnabled)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command[0],
            UseShellExecute = false,
            RedirectStandardInput = stdinEnabled,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = cwd,
        };

        foreach (var arg in command.Skip(1))
        {
            startInfo.ArgumentList.Add(arg);
        }

        startInfo.Environment.Clear();
        foreach (var variable in environment)
        {
            startInfo.Environment[variable.Key] = variable.Value;
        }

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("failed to spawn command/exec process");
        return new KernelPipeCommandExecRuntime(process, stdinEnabled);
    }

    public async Task WriteStdinAsync(ReadOnlyMemory<byte> delta, CancellationToken cancellationToken)
    {
        if (!stdinEnabled)
        {
            throw new InvalidOperationException("stdin streaming is not enabled for this command/exec");
        }

        await process.StandardInput.BaseStream.WriteAsync(delta, cancellationToken).ConfigureAwait(false);
        await process.StandardInput.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public void CloseStdin()
    {
        if (stdinEnabled)
        {
            process.StandardInput.Close();
        }
    }

    public void RequestTerminate()
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
            // ignore termination races
        }
    }

    public void Resize(int rows, int cols)
    {
        throw new InvalidOperationException("failed to resize PTY: session is not tty-backed");
    }

    public async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
    {
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    public void Dispose()
    {
        process.Dispose();
    }
}

/// <summary>
/// `command/exec` 跟踪型会话运行时，负责 stdin/stdout/stderr 聚合、输出流回调与超时终止。
/// Tracked `command/exec` session runtime responsible for IO aggregation, streaming callbacks, and timeout termination.
/// </summary>
internal sealed class KernelTrackedCommandExecSession : IDisposable
{
    private const int IoDrainTimeoutMs = 2000;
    private readonly IKernelTrackedCommandExecRuntime runtime;
    private readonly bool streamStdin;
    private readonly bool streamStdoutStderr;
    private readonly int? outputBytesCap;
    private readonly Func<string, string, string, bool, Task> outputCallback;
    private readonly IKernelManagedNetworkExecutionLease? managedNetworkLease;
    private readonly object gate = new();
    private readonly MemoryStream stdoutBuffer = new();
    private readonly MemoryStream stderrBuffer = new();
    private readonly Task stdoutDrainTask;
    private readonly Task stderrDrainTask;
    private readonly CancellationTokenSource ioDrainCancellation = new();
    private bool stdinClosed;
    private int stdoutObservedBytes;
    private int stderrObservedBytes;
    private bool stdoutCapReached;
    private bool stderrCapReached;
    private int processExited;

    private KernelTrackedCommandExecSession(
        string processId,
        IKernelTrackedCommandExecRuntime runtime,
        bool streamStdin,
        bool streamStdoutStderr,
        int? outputBytesCap,
        IKernelManagedNetworkExecutionLease? managedNetworkLease,
        Func<string, string, string, bool, Task> outputCallback)
    {
        ProcessId = processId;
        this.runtime = runtime;
        this.streamStdin = streamStdin;
        this.streamStdoutStderr = streamStdoutStderr;
        this.outputBytesCap = outputBytesCap;
        this.managedNetworkLease = managedNetworkLease;
        this.outputCallback = outputCallback;
        stdoutDrainTask = DrainAsync(runtime.StdoutReader, isStdout: true);
        stderrDrainTask = DrainAsync(runtime.StderrReader, isStdout: false);
    }

    public string ProcessId { get; }

    public int ProcessHandleId => runtime.ProcessId;

    public static KernelTrackedCommandExecSession Start(
        string processId,
        IReadOnlyList<string> command,
        string cwd,
        IReadOnlyDictionary<string, string> environment,
        bool tty,
        KernelCommandExecTerminalSize? terminalSize,
        bool streamStdin,
        bool streamStdoutStderr,
        int? outputBytesCap,
        IKernelManagedNetworkExecutionLease? managedNetworkLease,
        Func<string, string, string, bool, Task> outputCallback)
    {
        if (command.Count == 0)
        {
            throw new InvalidOperationException("command must not be empty");
        }

        var effectiveStreamStdin = tty || streamStdin;
        var effectiveStreamStdoutStderr = tty || streamStdoutStderr;
        IKernelTrackedCommandExecRuntime runtime;
        if (tty)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new InvalidOperationException("command/exec tty/pty is not supported on this platform");
            }

            runtime = KernelWindowsPseudoConsoleRuntime.Start(
                command,
                cwd,
                environment,
                terminalSize ?? KernelCommandExecTerminalSize.Default);
        }
        else
        {
            runtime = KernelPipeCommandExecRuntime.Start(command, cwd, environment, effectiveStreamStdin);
        }

        return new KernelTrackedCommandExecSession(
            processId,
            runtime,
            effectiveStreamStdin,
            effectiveStreamStdoutStderr,
            outputBytesCap,
            managedNetworkLease,
            outputCallback);
    }

    public async Task WriteStdinAsync(byte[] delta, bool closeStdin, CancellationToken cancellationToken)
    {
        if (!streamStdin)
        {
            throw new InvalidOperationException("stdin streaming is not enabled for this command/exec");
        }

        ThrowIfNoLongerRunning();
        if (stdinClosed && delta.Length > 0)
        {
            throw new InvalidOperationException("stdin is already closed");
        }

        if (!stdinClosed && delta.Length > 0)
        {
            await runtime.WriteStdinAsync(delta, cancellationToken).ConfigureAwait(false);
        }

        if (closeStdin && !stdinClosed)
        {
            runtime.CloseStdin();
            stdinClosed = true;
        }
    }

    public void RequestTerminate()
    {
        ThrowIfNoLongerRunning();
        RequestTerminateCore();
    }

    public void Resize(int rows, int cols)
    {
        ThrowIfNoLongerRunning();
        runtime.Resize(rows, cols);
    }

    public async Task<KernelTrackedCommandExecResult> WaitForCompletionAsync(int? timeoutMs, bool disableTimeout, CancellationToken cancellationToken)
    {
        int? effectiveTimeout = disableTimeout ? null : (timeoutMs ?? 30000);
        var timedOut = false;
        var exitCode = -1;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (effectiveTimeout is int timeout)
        {
            timeoutCts.CancelAfter(Math.Clamp(timeout, 1000, 300000));
        }

        try
        {
            exitCode = await runtime.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            MarkProcessExited();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            RequestTerminateCore();
            try
            {
                exitCode = await runtime.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                exitCode = -1;
            }
            finally
            {
                MarkProcessExited();
            }
        }
        catch
        {
            MarkProcessExited();
            throw;
        }

        var drainTasks = Task.WhenAll(stdoutDrainTask, stderrDrainTask);
        try
        {
            var completed = await Task.WhenAny(drainTasks, Task.Delay(IoDrainTimeoutMs, CancellationToken.None)).ConfigureAwait(false);
            if (completed == drainTasks)
            {
                await drainTasks.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ioDrainCancellation.IsCancellationRequested)
        {
            // forced termination stops pending pipe drains
        }

        string stdout;
        string stderr;
        lock (gate)
        {
            stdout = streamStdoutStderr ? string.Empty : ReadBufferedText(stdoutBuffer);
            stderr = streamStdoutStderr ? string.Empty : ReadBufferedText(stderrBuffer);
        }

        var finalExitCode = timedOut ? 124 : exitCode;

        if (ManagedNetworkExecutionRejected())
        {
            var outcomeMessage = managedNetworkLease?.ConsumeOutcomeMessage();
            if (!string.IsNullOrWhiteSpace(outcomeMessage))
            {
                stderr = string.IsNullOrWhiteSpace(stderr)
                    ? outcomeMessage
                    : stderr + Environment.NewLine + outcomeMessage;
                if (finalExitCode == 0)
                {
                    finalExitCode = -1;
                }
            }
        }

        return new KernelTrackedCommandExecResult(finalExitCode, stdout, stderr, timedOut);
    }

    public void Dispose()
    {
        try
        {
            ioDrainCancellation.Cancel();
        }
        catch
        {
            // ignore cancellation races
        }

        try
        {
            RequestTerminateCore();
        }
        catch
        {
            // ignore terminate races
        }

        try
        {
            runtime.Dispose();
        }
        catch
        {
            // ignore dispose races
        }

        managedNetworkLease?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private void ThrowIfNoLongerRunning()
    {
        if (Volatile.Read(ref processExited) != 0)
        {
            throw new InvalidOperationException($"command/exec {JsonSerializer.Serialize(ProcessId)} is no longer running");
        }
    }

    private void MarkProcessExited()
    {
        Interlocked.Exchange(ref processExited, 1);
    }

    private void RequestTerminateCore()
    {
        runtime.RequestTerminate();
        try
        {
            ioDrainCancellation.Cancel();
        }
        catch
        {
            // ignore cancellation races
        }
    }

    private bool ManagedNetworkExecutionRejected() => managedNetworkLease?.HasRejectedOutcome == true;

    private static string ReadBufferedText(MemoryStream buffer)
    {
        return buffer.Length == 0
            ? string.Empty
            : Encoding.UTF8.GetString(buffer.ToArray());
    }

    private async Task DrainAsync(Stream stream, bool isStdout)
    {
        var buffer = new byte[4096];
        while (true)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ioDrainCancellation.Token).ConfigureAwait(false);
            }
            catch
            {
                break;
            }

            if (read <= 0)
            {
                break;
            }

            var chunk = buffer.AsSpan(0, read).ToArray();
            var cappedChunk = CaptureChunk(chunk, isStdout, out var capReached);
            if (streamStdoutStderr && cappedChunk.Length > 0)
            {
                await outputCallback(
                        ProcessId,
                        isStdout ? "stdout" : "stderr",
                        Convert.ToBase64String(cappedChunk),
                        capReached)
                    .ConfigureAwait(false);
            }
        }
    }

    private byte[] CaptureChunk(byte[] chunk, bool isStdout, out bool capReached)
    {
        lock (gate)
        {
            ref var observedBytes = ref (isStdout ? ref stdoutObservedBytes : ref stderrObservedBytes);
            ref var streamCapReached = ref (isStdout ? ref stdoutCapReached : ref stderrCapReached);
            var targetBuffer = isStdout ? stdoutBuffer : stderrBuffer;

            if (streamCapReached)
            {
                capReached = true;
                return Array.Empty<byte>();
            }

            var allowedLength = chunk.Length;
            if (outputBytesCap is int cap)
            {
                allowedLength = Math.Max(0, Math.Min(cap - observedBytes, chunk.Length));
            }

            observedBytes += allowedLength;
            streamCapReached = outputBytesCap is int limit && observedBytes >= limit;
            capReached = streamCapReached;

            if (allowedLength <= 0)
            {
                return Array.Empty<byte>();
            }

            if (!streamStdoutStderr)
            {
                targetBuffer.Write(chunk, 0, allowedLength);
            }

            return allowedLength == chunk.Length ? chunk : chunk[..allowedLength];
        }
    }
}

/// <summary>
/// `command/exec` 跟踪型会话完成结果。
/// Completion result for a tracked `command/exec` session.
/// </summary>
internal sealed record KernelTrackedCommandExecResult(int ExitCode, string StdOut, string StdErr, bool TimedOut);
