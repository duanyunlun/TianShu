using System.Diagnostics;
using System.Text;

namespace TianShu.AppHost.Tools;

/// <summary>
/// 封装宿主侧本地进程执行、输出聚合与超时终止逻辑，避免 shell tool 在各消费点重复实现。
/// Encapsulates host-side local process execution, output aggregation, and timeout termination so shell tools do not duplicate the logic.
/// </summary>
internal static class KernelExecRunner
{
    private const int ExecOutputMaxChars = 1024 * 1024; // 1 MiB cap, aligns with the legacy EXEC_OUTPUT_MAX_BYTES baseline.
    private const string RuntimeTracePathEnvironmentVariable = "TIANSHU_RUNTIME_TRACE_PATH";
    private const string LegacyRuntimeTracePathEnvironmentVariable = "TIANSHU_RUNTIME_TRACE_PATH";
    private static readonly object TraceFileSync = new();

    public static async Task<KernelExecToolCallOutput> ExecuteAsync(
        IReadOnlyList<string> command,
        string cwd,
        int timeoutMs,
        IReadOnlyDictionary<string, string>? environment,
        CancellationToken cancellationToken)
    {
        if (command.Count == 0)
        {
            return new KernelExecToolCallOutput(
                ExitCode: -1,
                Stdout: string.Empty,
                Stderr: "command must not be empty",
                AggregatedOutput: string.Empty,
                Duration: TimeSpan.Zero,
                TimedOut: false);
        }

        var execId = Guid.NewGuid().ToString("N");
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
        var started = Stopwatch.GetTimestamp();
        timeoutMs = Math.Clamp(timeoutMs, 1000, 300000);
        TraceExec(
            "exec/start",
            $"execId={execId}; cwd={startInfo.WorkingDirectory}; timeoutMs={timeoutMs}; command={FormatCommand(command)}; envCount={(environment?.Count ?? 0)}");
        process.Start();
        TraceExec(
            "process/start",
            $"execId={execId}; pid={process.Id}; hasExited={SafeHasExited(process)}");
        TryCloseStandardInput(process, execId);

        var stdoutTask = ReadStreamCappedAsync(process.StandardOutput, ExecOutputMaxChars, cancellationToken);
        var stderrTask = ReadStreamCappedAsync(process.StandardError, ExecOutputMaxChars, cancellationToken);
        TraceExec("stream/read/start", $"execId={execId}; pid={process.Id}; stream=stdout");
        TraceExec("stream/read/start", $"execId={execId}; pid={process.Id}; stream=stderr");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMs);

        var timedOut = false;
        TraceExec("wait/start", $"execId={execId}; pid={process.Id}; timeoutMs={timeoutMs}");
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            TraceExec(
                "wait/completed",
                $"execId={execId}; pid={process.Id}; hasExited={SafeHasExited(process)}; exitCode={SafeExitCode(process)}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TraceExec("wait/cancelled", $"execId={execId}; pid={SafeProcessId(process)}; source=user");
            throw;
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            TraceExec(
                "wait/timeout",
                $"execId={execId}; pid={SafeProcessId(process)}; hasExited={SafeHasExited(process)}; exitCode={SafeExitCode(process)}");
            TryKillProcess(process, execId);
        }

        if (timedOut)
        {
            try
            {
                TraceExec("wait/post-kill/start", $"execId={execId}; pid={SafeProcessId(process)}");
                await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2))
                    .ConfigureAwait(false);
                TraceExec(
                    "wait/post-kill/completed",
                    $"execId={execId}; pid={SafeProcessId(process)}; hasExited={SafeHasExited(process)}; exitCode={SafeExitCode(process)}");
            }
            catch (Exception ex)
            {
                TraceExec(
                    "wait/post-kill/failed",
                    $"execId={execId}; pid={SafeProcessId(process)}; hasExited={SafeHasExited(process)}; exitCode={SafeExitCode(process)}; error={DescribeException(ex)}");
            }
        }

        string stdout;
        string stderr;
        try
        {
            stdout = await stdoutTask.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false);
            TraceExec(
                "stream/read/completed",
                $"execId={execId}; pid={SafeProcessId(process)}; stream=stdout; length={stdout.Length}");
        }
        catch (Exception ex)
        {
            TraceExec(
                "stream/read/failed",
                $"execId={execId}; pid={SafeProcessId(process)}; stream=stdout; error={DescribeException(ex)}");
            stdout = string.Empty;
        }

        try
        {
            stderr = await stderrTask.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false);
            TraceExec(
                "stream/read/completed",
                $"execId={execId}; pid={SafeProcessId(process)}; stream=stderr; length={stderr.Length}");
        }
        catch (Exception ex)
        {
            TraceExec(
                "stream/read/failed",
                $"execId={execId}; pid={SafeProcessId(process)}; stream=stderr; error={DescribeException(ex)}");
            stderr = string.Empty;
        }

        var duration = TimeSpan.FromSeconds((double)(Stopwatch.GetTimestamp() - started) / Stopwatch.Frequency);
        var exitCode = timedOut ? -1 : process.ExitCode;
        var aggregated = AggregateOutput(stdout, stderr, ExecOutputMaxChars);
        TraceExec(
            "exec/return",
            $"execId={execId}; pid={SafeProcessId(process)}; exitCode={exitCode}; timedOut={timedOut}; durationMs={(long)Math.Max(0, duration.TotalMilliseconds)}; stdoutLength={stdout.Length}; stderrLength={stderr.Length}");

        return new KernelExecToolCallOutput(
            ExitCode: exitCode,
            Stdout: stdout,
            Stderr: stderr,
            AggregatedOutput: aggregated,
            Duration: duration,
            TimedOut: timedOut);
    }

    private static void TryKillProcess(Process process, string execId)
    {
        try
        {
            if (!process.HasExited)
            {
                TraceExec("kill/start", $"execId={execId}; pid={SafeProcessId(process)}; hasExited=false");
                process.Kill(entireProcessTree: true);
                TraceExec("kill/completed", $"execId={execId}; pid={SafeProcessId(process)}");
            }
            else
            {
                TraceExec("kill/skipped", $"execId={execId}; pid={SafeProcessId(process)}; hasExited=true");
            }
        }
        catch (Exception ex)
        {
            TraceExec(
                "kill/failed",
                $"execId={execId}; pid={SafeProcessId(process)}; hasExited={SafeHasExited(process)}; error={DescribeException(ex)}");
        }
    }

    private static void TryCloseStandardInput(Process process, string execId)
    {
        try
        {
            // 避免子进程继承 kernel 的 stdio RPC 输入管道，把协议 stdin 当成交互输入而长期挂住。
            // Prevent child processes from inheriting the kernel stdio RPC pipe and hanging on protocol stdin.
            process.StandardInput.Close();
            TraceExec("stream/input/closed", $"execId={execId}; pid={SafeProcessId(process)}");
        }
        catch (Exception ex)
        {
            TraceExec(
                "stream/input/close-failed",
                $"execId={execId}; pid={SafeProcessId(process)}; error={DescribeException(ex)}");
        }
    }

    private static async Task<string> ReadStreamCappedAsync(StreamReader reader, int maxChars, CancellationToken cancellationToken)
    {
        if (maxChars <= 0)
        {
            _ = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            return string.Empty;
        }

        var builder = new StringBuilder(Math.Min(maxChars, 16 * 1024));
        var buffer = new char[4096];
        while (builder.Length < maxChars)
        {
            var remaining = maxChars - builder.Length;
            var readSize = Math.Min(buffer.Length, remaining);
            var read = await reader.ReadAsync(buffer.AsMemory(0, readSize), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            builder.Append(buffer, 0, read);
        }

        return builder.ToString();
    }

    private static string AggregateOutput(string stdout, string stderr, int maxChars)
    {
        if (maxChars <= 0)
        {
            return string.Empty;
        }

        var totalLen = stdout.Length + stderr.Length;
        if (totalLen <= maxChars)
        {
            return string.Concat(stdout, stderr);
        }

        var wantStdout = Math.Min(stdout.Length, maxChars / 3);
        var wantStderr = stderr.Length;
        var stderrTake = Math.Min(wantStderr, Math.Max(maxChars - wantStdout, 0));
        var remaining = Math.Max(maxChars - wantStdout - stderrTake, 0);
        var stdoutTake = wantStdout + Math.Min(remaining, Math.Max(stdout.Length - wantStdout, 0));

        var builder = new StringBuilder(maxChars);
        if (stdoutTake > 0)
        {
            builder.Append(stdout.AsSpan(0, stdoutTake));
        }

        if (stderrTake > 0)
        {
            builder.Append(stderr.AsSpan(0, stderrTake));
        }

        return builder.ToString();
    }

    private static void TraceExec(string channel, string message)
    {
        var path = ResolveExecTracePath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var line = $"[{DateTimeOffset.Now:O}] [kernel-exec/{channel}] pid={Environment.ProcessId} {message}{Environment.NewLine}";
            lock (TraceFileSync)
            {
                File.AppendAllText(path, line, Encoding.UTF8);
            }
        }
        catch
        {
            // ignore diagnostic trace failures
        }
    }

    private static string? ResolveExecTracePath()
    {
        var runtimeTracePath = Environment.GetEnvironmentVariable(RuntimeTracePathEnvironmentVariable)
            ?? Environment.GetEnvironmentVariable(LegacyRuntimeTracePathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(runtimeTracePath))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(runtimeTracePath);
        var extension = Path.GetExtension(runtimeTracePath);
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(runtimeTracePath);
        if (string.IsNullOrWhiteSpace(fileNameWithoutExtension))
        {
            fileNameWithoutExtension = "tianshu-runtime-trace";
        }

        var fileName = string.IsNullOrWhiteSpace(extension)
            ? $"{fileNameWithoutExtension}.kernel-exec.log"
            : $"{fileNameWithoutExtension}.kernel-exec{extension}";
        return string.IsNullOrWhiteSpace(directory) ? fileName : Path.Combine(directory, fileName);
    }

    private static string FormatCommand(IReadOnlyList<string> command)
        => string.Join(' ', command.Select(QuoteCommandPart));

    private static string QuoteCommandPart(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        return value.IndexOfAny([' ', '\t', '\r', '\n', '"']) >= 0
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
    }

    private static string DescribeException(Exception ex)
        => $"{ex.GetType().Name}: {ex.Message}";

    private static int SafeProcessId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch
        {
            return -1;
        }
    }

    private static bool SafeHasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static string SafeExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode.ToString() : "<running>";
        }
        catch (Exception ex)
        {
            return $"<error:{ex.GetType().Name}>";
        }
    }
}
