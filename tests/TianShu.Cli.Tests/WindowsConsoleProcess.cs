using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace TianShu.Cli.Tests;

internal sealed class WindowsConsoleProcess : IDisposable
{
    private const uint HandleFlagInherit = 0x00000001;
    private const uint WaitObject0 = 0x00000000;
    private const uint WaitTimeout = 0x00000102;
    private const uint WaitFailed = 0xFFFFFFFF;
    private const uint StillActive = 259;
    private const uint CtrlBreakEvent = 1;
    private const uint CreateNewProcessGroup = 0x00000200;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint StartupfUseStdHandles = 0x00000100;

    private readonly StreamWriter stdinWriter;
    private readonly StreamReader stdoutReader;
    private readonly StreamReader stderrReader;
    private readonly SafeWaitHandle processHandle;
    private readonly int processId;
    private readonly Task<string> stdoutTask;
    private readonly Task<string> stderrTask;
    private bool stdinClosed;

    private WindowsConsoleProcess(
        StreamWriter stdinWriter,
        StreamReader stdoutReader,
        StreamReader stderrReader,
        SafeWaitHandle processHandle,
        int processId)
    {
        this.stdinWriter = stdinWriter;
        this.stdoutReader = stdoutReader;
        this.stderrReader = stderrReader;
        this.processHandle = processHandle;
        this.processId = processId;
        stdoutTask = stdoutReader.ReadToEndAsync();
        stderrTask = stderrReader.ReadToEndAsync();
    }

    public static WindowsConsoleProcess Start(
        string executablePath,
        IReadOnlyList<string> args,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows console process tests require Windows.");
        }

        SafeFileHandle? parentStdinWriterHandle = null;
        SafeFileHandle? parentStdoutReaderHandle = null;
        SafeFileHandle? parentStderrReaderHandle = null;
        SafeWaitHandle? processHandle = null;
        StreamWriter? stdinWriter = null;
        StreamReader? stdoutReader = null;
        StreamReader? stderrReader = null;

        try
        {
            using var childStdinReaderHandle = CreatePipeEnd(out parentStdinWriterHandle);
            using var childStdoutWriterHandle = CreatePipeEnd(out parentStdoutReaderHandle, reverse: true);
            using var childStderrWriterHandle = CreatePipeEnd(out parentStderrReaderHandle, reverse: true);

            var startupInfo = new STARTUPINFOW
            {
                cb = Marshal.SizeOf<STARTUPINFOW>(),
                dwFlags = StartupfUseStdHandles,
                hStdInput = childStdinReaderHandle.DangerousGetHandle(),
                hStdOutput = childStdoutWriterHandle.DangerousGetHandle(),
                hStdError = childStderrWriterHandle.DangerousGetHandle(),
            };

            var commandLine = new StringBuilder(BuildCommandLine(executablePath, args));
            var environmentBlock = BuildEnvironmentBlock(environment);
            var environmentHandle = GCHandle.Alloc(environmentBlock, GCHandleType.Pinned);
            try
            {
                if (!CreateProcessW(
                        null,
                        commandLine,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        true,
                        CreateNewProcessGroup | CreateUnicodeEnvironment,
                        environmentHandle.AddrOfPinnedObject(),
                        workingDirectory,
                        ref startupInfo,
                        out var processInfo))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"CreateProcessW failed for {executablePath}");
                }

                using var threadHandle = new SafeWaitHandle(processInfo.hThread, ownsHandle: true);
                processHandle = new SafeWaitHandle(processInfo.hProcess, ownsHandle: true);

                stdinWriter = new StreamWriter(new FileStream(parentStdinWriterHandle, FileAccess.Write, 4096, isAsync: false), new UTF8Encoding(false))
                {
                    AutoFlush = true,
                    NewLine = Environment.NewLine,
                };
                stdoutReader = new StreamReader(new FileStream(parentStdoutReaderHandle, FileAccess.Read, 4096, isAsync: false), detectEncodingFromByteOrderMarks: true);
                stderrReader = new StreamReader(new FileStream(parentStderrReaderHandle, FileAccess.Read, 4096, isAsync: false), detectEncodingFromByteOrderMarks: true);

                parentStdinWriterHandle = null;
                parentStdoutReaderHandle = null;
                parentStderrReaderHandle = null;

                return new WindowsConsoleProcess(
                    stdinWriter,
                    stdoutReader,
                    stderrReader,
                    processHandle,
                    unchecked((int)processInfo.dwProcessId));
            }
            finally
            {
                environmentHandle.Free();
            }
        }
        catch
        {
            stdinWriter?.Dispose();
            stdoutReader?.Dispose();
            stderrReader?.Dispose();
            processHandle?.Dispose();
            parentStdinWriterHandle?.Dispose();
            parentStdoutReaderHandle?.Dispose();
            parentStderrReaderHandle?.Dispose();
            throw;
        }
    }

    public async Task WriteLineAsync(string text)
    {
        if (stdinClosed)
        {
            throw new InvalidOperationException("stdin is already closed");
        }

        await stdinWriter.WriteLineAsync(text);
        await stdinWriter.FlushAsync();
    }

    public void SendBreak()
    {
        if (!GenerateConsoleCtrlEvent(CtrlBreakEvent, unchecked((uint)processId)))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GenerateConsoleCtrlEvent failed");
        }
    }

    public async Task<WindowsConsoleProcessResult> WaitForExitAsync(TimeSpan timeout)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        try
        {
            var exitCode = await WaitForExitCodeAsync(timeoutSource.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            return new WindowsConsoleProcessResult(exitCode, stdout, stderr);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (TryGetStillActive())
                {
                    _ = TerminateProcess(processHandle.DangerousGetHandle(), 1);
                }
            }
            catch
            {
            }

            throw new TimeoutException("Windows console process wait timed out.");
        }
    }

    public void Dispose()
    {
        stdinClosed = true;
        stdinWriter.Dispose();
        stdoutReader.Dispose();
        stderrReader.Dispose();
        processHandle.Dispose();
    }

    private async Task<int> WaitForExitCodeAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var waitResult = WaitForSingleObject(processHandle.DangerousGetHandle(), 100);
            if (waitResult == WaitObject0)
            {
                break;
            }

            if (waitResult == WaitTimeout)
            {
                await Task.Delay(20, cancellationToken);
                continue;
            }

            if (waitResult == WaitFailed)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WaitForSingleObject failed for Windows console process");
            }

            throw new InvalidOperationException($"Unexpected WaitForSingleObject result: {waitResult}");
        }

        if (!GetExitCodeProcess(processHandle.DangerousGetHandle(), out var exitCode))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetExitCodeProcess failed for Windows console process");
        }

        return unchecked((int)exitCode);
    }

    private bool TryGetStillActive()
        => GetExitCodeProcess(processHandle.DangerousGetHandle(), out var exitCode) && exitCode == StillActive;

    private static SafeFileHandle CreatePipeEnd(out SafeFileHandle counterpart, bool reverse = false)
    {
        var securityAttributes = new SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            bInheritHandle = true,
        };

        if (!CreatePipe(out var readHandle, out var writeHandle, ref securityAttributes, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe failed for Windows console process");
        }

        var parentHandle = reverse ? readHandle : writeHandle;
        var childHandle = reverse ? writeHandle : readHandle;
        if (!SetHandleInformation(parentHandle, HandleFlagInherit, 0))
        {
            readHandle.Dispose();
            writeHandle.Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetHandleInformation failed for Windows console process");
        }

        counterpart = parentHandle;
        return childHandle;
    }

    private static char[] BuildEnvironmentBlock(IReadOnlyDictionary<string, string> environment)
    {
        var builder = new StringBuilder();
        foreach (var entry in environment.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(entry.Key);
            builder.Append('=');
            builder.Append(entry.Value);
            builder.Append('\0');
        }

        builder.Append('\0');
        return builder.ToString().ToCharArray();
    }

    private static string BuildCommandLine(string executablePath, IReadOnlyList<string> args)
    {
        var builder = new StringBuilder();
        AppendQuotedArgument(builder, executablePath);
        foreach (var arg in args)
        {
            builder.Append(' ');
            AppendQuotedArgument(builder, arg);
        }

        return builder.ToString();
    }

    private static void AppendQuotedArgument(StringBuilder builder, string argument)
    {
        if (!RequiresQuoting(argument))
        {
            builder.Append(argument);
            return;
        }

        builder.Append('"');
        var backslashCount = 0;
        foreach (var ch in argument)
        {
            if (ch == '\\')
            {
                backslashCount++;
                continue;
            }

            if (ch == '"')
            {
                builder.Append('\\', backslashCount * 2 + 1);
                builder.Append('"');
                backslashCount = 0;
                continue;
            }

            if (backslashCount > 0)
            {
                builder.Append('\\', backslashCount);
                backslashCount = 0;
            }

            builder.Append(ch);
        }

        if (backslashCount > 0)
        {
            builder.Append('\\', backslashCount * 2);
        }

        builder.Append('"');
    }

    private static bool RequiresQuoting(string argument)
    {
        if (argument.Length == 0)
        {
            return true;
        }

        foreach (var ch in argument)
        {
            if (char.IsWhiteSpace(ch) || ch == '"')
            {
                return true;
            }
        }

        return false;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(
        out SafeFileHandle hReadPipe,
        out SafeFileHandle hWritePipe,
        ref SECURITY_ATTRIBUTES lpPipeAttributes,
        int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(
        SafeFileHandle hObject,
        uint dwMask,
        uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string lpCurrentDirectory,
        ref STARTUPINFOW lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFOW
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public uint dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }
}

internal sealed record WindowsConsoleProcessResult(int ExitCode, string StdOut, string StdErr);
