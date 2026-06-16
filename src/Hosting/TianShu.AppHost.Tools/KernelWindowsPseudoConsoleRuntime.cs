using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace TianShu.AppHost.Tools;

/// <summary>
/// Windows 平台下基于 pseudo console 的 `command/exec` PTY runtime。
/// Windows pseudo-console based PTY runtime for `command/exec`.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class KernelWindowsPseudoConsoleRuntime : IKernelTrackedCommandExecRuntime
{
    private const int CreateUnicodeEnvironment = 0x00000400;
    private const int ExtendedStartupInfoPresent = 0x00080000;
    private const int StartupfUseStdHandles = 0x00000100;
    private const uint HandleFlagInherit = 0x00000001;
    private const uint ProcThreadAttributePseudoConsole = 0x00020016;
    private const uint PseudoConsoleResizeQuirk = 0x00000002;
    private const uint WaitObject0 = 0x00000000;
    private const uint WaitTimeout = 0x00000102;
    private const uint WaitFailed = 0xFFFFFFFF;
    private const uint StillActive = 259;

    private readonly SafePseudoConsoleHandle pseudoConsole;
    private readonly FileStream stdinWriter;
    private readonly FileStream stdoutReader;
    private readonly SafeWaitHandle processHandle;
    private readonly int processId;
    private bool stdinClosed;

    private KernelWindowsPseudoConsoleRuntime(
        SafePseudoConsoleHandle pseudoConsole,
        FileStream stdinWriter,
        FileStream stdoutReader,
        SafeWaitHandle processHandle,
        int processId)
    {
        this.pseudoConsole = pseudoConsole;
        this.stdinWriter = stdinWriter;
        this.stdoutReader = stdoutReader;
        this.processHandle = processHandle;
        this.processId = processId;
    }

    public int ProcessId => processId;

    public Stream StdoutReader => stdoutReader;

    public Stream StderrReader => Stream.Null;

    public static KernelWindowsPseudoConsoleRuntime Start(
        IReadOnlyList<string> command,
        string cwd,
        IReadOnlyDictionary<string, string> environment,
        KernelCommandExecTerminalSize terminalSize)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException("command/exec tty/pty is not supported on this platform");
        }

        SafePseudoConsoleHandle? pseudoConsole = null;
        FileStream? stdinWriter = null;
        FileStream? stdoutReader = null;
        SafeWaitHandle? processHandle = null;

        try
        {
            using var pseudoConsoleInputRead = CreatePipeEnd(out var appInputWrite);
            using var pseudoConsoleOutputWrite = CreatePipeEnd(out var appOutputRead, reverse: true);

            pseudoConsole = CreatePseudoConsole(terminalSize, pseudoConsoleInputRead, pseudoConsoleOutputWrite);
            stdinWriter = new FileStream(appInputWrite, FileAccess.Write, 4096, isAsync: false);
            stdoutReader = new FileStream(appOutputRead, FileAccess.Read, 4096, isAsync: false);

            using var attributeList = KernelProcThreadAttributeList.Create(1);
            attributeList.SetPseudoConsole(pseudoConsole);

            var startupInfo = new STARTUPINFOEXW();
            startupInfo.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEXW>();
            startupInfo.StartupInfo.dwFlags = StartupfUseStdHandles;
            startupInfo.StartupInfo.hStdInput = new IntPtr(-1);
            startupInfo.StartupInfo.hStdOutput = new IntPtr(-1);
            startupInfo.StartupInfo.hStdError = new IntPtr(-1);
            startupInfo.lpAttributeList = attributeList.Handle;

            var commandLine = new StringBuilder(BuildCommandLine(command, environment, cwd));
            var environmentBlock = BuildEnvironmentBlock(environment);
            var environmentHandle = GCHandle.Alloc(environmentBlock, GCHandleType.Pinned);
            try
            {
                if (!CreateProcessW(
                        null,
                        commandLine,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        false,
                        ExtendedStartupInfoPresent | CreateUnicodeEnvironment,
                        environmentHandle.AddrOfPinnedObject(),
                        cwd,
                        ref startupInfo,
                        out var processInfo))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"CreateProcessW failed for {command[0]}");
                }

                using var threadHandle = new SafeWaitHandle(processInfo.hThread, ownsHandle: true);
                processHandle = new SafeWaitHandle(processInfo.hProcess, ownsHandle: true);
                return new KernelWindowsPseudoConsoleRuntime(
                    pseudoConsole,
                    stdinWriter,
                    stdoutReader,
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
            processHandle?.Dispose();
            pseudoConsole?.Dispose();
            throw;
        }
    }

    public async Task WriteStdinAsync(ReadOnlyMemory<byte> delta, CancellationToken cancellationToken)
    {
        if (stdinClosed)
        {
            throw new InvalidOperationException("stdin is already closed");
        }

        await stdinWriter.WriteAsync(delta, cancellationToken).ConfigureAwait(false);
        await stdinWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public void CloseStdin()
    {
        if (stdinClosed)
        {
            return;
        }

        stdinClosed = true;
        stdinWriter.Dispose();
    }

    public void RequestTerminate()
    {
        try
        {
            if (!GetExitCodeProcess(processHandle.DangerousGetHandle(), out var exitCode))
            {
                return;
            }

            if (exitCode != StillActive)
            {
                return;
            }

            _ = TerminateProcess(processHandle.DangerousGetHandle(), 1);
        }
        catch
        {
            // ignore termination races
        }
    }

    public void Resize(int rows, int cols)
    {
        var result = ResizePseudoConsole(pseudoConsole, CreateCoord(rows, cols));
        if (result != 0)
        {
            throw new InvalidOperationException($"failed to resize PTY: {FormatHResult(result)}");
        }
    }

    public Task<int> WaitForExitAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() =>
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
                    continue;
                }

                if (waitResult == WaitFailed)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "WaitForSingleObject failed for command/exec PTY process");
                }

                throw new InvalidOperationException($"Unexpected WaitForSingleObject result: {waitResult}");
            }

            if (!GetExitCodeProcess(processHandle.DangerousGetHandle(), out var exitCode))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetExitCodeProcess failed for command/exec PTY process");
            }

            return unchecked((int)exitCode);
        }, CancellationToken.None);
    }

    public void Dispose()
    {
        stdoutReader.Dispose();
        stdinWriter.Dispose();
        pseudoConsole.Dispose();
        processHandle.Dispose();
    }

    private static SafePseudoConsoleHandle CreatePseudoConsole(
        KernelCommandExecTerminalSize size,
        SafeFileHandle inputReadHandle,
        SafeFileHandle outputWriteHandle)
    {
        var result = CreatePseudoConsole(
            CreateCoord(size.Rows, size.Cols),
            inputReadHandle.DangerousGetHandle(),
            outputWriteHandle.DangerousGetHandle(),
            PseudoConsoleResizeQuirk,
            out var pseudoConsoleHandle);
        if (result != 0)
        {
            throw new InvalidOperationException($"failed to create pseudo console: {FormatHResult(result)}");
        }

        return new SafePseudoConsoleHandle(pseudoConsoleHandle);
    }

    private static SafeFileHandle CreatePipeEnd(out SafeFileHandle counterpart, bool reverse = false)
    {
        var securityAttributes = new SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            bInheritHandle = true,
        };

        if (!CreatePipe(out var readHandle, out var writeHandle, ref securityAttributes, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe failed for command/exec PTY session");
        }

        var parentHandle = reverse ? readHandle : writeHandle;
        var pseudoConsoleHandle = reverse ? writeHandle : readHandle;
        if (!SetHandleInformation(parentHandle, HandleFlagInherit, 0))
        {
            readHandle.Dispose();
            writeHandle.Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetHandleInformation failed for command/exec PTY session");
        }

        counterpart = parentHandle;
        return pseudoConsoleHandle;
    }

    private static COORD CreateCoord(int rows, int cols)
        => new()
        {
            X = checked((short)cols),
            Y = checked((short)rows),
        };

    private static char[] BuildEnvironmentBlock(IReadOnlyDictionary<string, string> environment)
    {
        var builder = new StringBuilder();
        foreach (var entry in environment)
        {
            builder.Append(entry.Key);
            builder.Append('=');
            builder.Append(entry.Value);
            builder.Append('\0');
        }

        builder.Append('\0');
        builder.Append('\0');
        return builder.ToString().ToCharArray();
    }

    private static string BuildCommandLine(
        IReadOnlyList<string> command,
        IReadOnlyDictionary<string, string> environment,
        string cwd)
    {
        if (command.Count == 0)
        {
            throw new InvalidOperationException("command must not be empty");
        }

        var executable = ResolveExecutable(command[0], environment, cwd);
        var builder = new StringBuilder();
        AppendQuotedArgument(builder, executable);
        foreach (var argument in command.Skip(1))
        {
            builder.Append(' ');
            AppendQuotedArgument(builder, argument);
        }

        return builder.ToString();
    }

    private static string ResolveExecutable(
        string executable,
        IReadOnlyDictionary<string, string> environment,
        string cwd)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new InvalidOperationException("command must not be empty");
        }

        if (Path.IsPathRooted(executable))
        {
            return executable;
        }

        if (executable.Contains(Path.DirectorySeparatorChar) || executable.Contains(Path.AltDirectorySeparatorChar))
        {
            return Path.GetFullPath(Path.Combine(cwd, executable));
        }

        var pathValue = environment.TryGetValue("PATH", out var configuredPath)
            ? configuredPath
            : Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathExtensionsValue = environment.TryGetValue("PATHEXT", out var configuredPathExt)
            ? configuredPathExt
            : Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";
        var hasExtension = Path.HasExtension(executable);
        var pathExtensions = pathExtensionsValue
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static extension => extension.StartsWith('.') ? extension : "." + extension)
            .ToArray();

        foreach (var rawPath in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var basePath = Path.Combine(rawPath, executable);
                if (File.Exists(basePath))
                {
                    return basePath;
                }

                if (hasExtension)
                {
                    continue;
                }

                foreach (var extension in pathExtensions)
                {
                    var candidate = basePath + extension;
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            catch
            {
                // ignore malformed PATH entries and continue searching
            }
        }

        return executable;
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

    private static string FormatHResult(int hResult)
        => new Win32Exception(hResult).Message;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(
        out SafeFileHandle hReadPipe,
        out SafeFileHandle hWritePipe,
        ref SECURITY_ATTRIBUTES lpPipeAttributes,
        int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(
        SafeHandle hObject,
        uint dwMask,
        uint dwFlags);

    [DllImport("kernel32.dll", EntryPoint = "CreatePseudoConsole")]
    private static extern int CreatePseudoConsole(
        COORD size,
        IntPtr hInput,
        IntPtr hOutput,
        uint dwFlags,
        out IntPtr hPC);

    [DllImport("kernel32.dll", EntryPoint = "ResizePseudoConsole")]
    private static extern int ResizePseudoConsole(
        SafePseudoConsoleHandle hPC,
        COORD size);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        int dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        [In] ref STARTUPINFOEXW lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD
    {
        public short X;
        public short Y;
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
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEXW
    {
        public STARTUPINFOW StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    private sealed class KernelProcThreadAttributeList : IDisposable
    {
        private IntPtr handle;

        private KernelProcThreadAttributeList(IntPtr handle)
        {
            this.handle = handle;
        }

        public IntPtr Handle => handle;

        public static KernelProcThreadAttributeList Create(int attributeCount)
        {
            nuint size = 0;
            _ = InitializeProcThreadAttributeList(IntPtr.Zero, attributeCount, 0, ref size);
            var handle = Marshal.AllocHGlobal(checked((int)size));
            if (!InitializeProcThreadAttributeList(handle, attributeCount, 0, ref size))
            {
                var error = Marshal.GetLastWin32Error();
                Marshal.FreeHGlobal(handle);
                throw new Win32Exception(error, "InitializeProcThreadAttributeList failed for command/exec PTY session");
            }

            return new KernelProcThreadAttributeList(handle);
        }

        public void SetPseudoConsole(SafePseudoConsoleHandle pseudoConsole)
        {
            if (!UpdateProcThreadAttribute(
                    handle,
                    0,
                    (IntPtr)ProcThreadAttributePseudoConsole,
                    pseudoConsole.DangerousGetHandle(),
                    (IntPtr)IntPtr.Size,
                    IntPtr.Zero,
                    IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "UpdateProcThreadAttribute failed for command/exec PTY session");
            }
        }

        public void Dispose()
        {
            if (handle == IntPtr.Zero)
            {
                return;
            }

            DeleteProcThreadAttributeList(handle);
            Marshal.FreeHGlobal(handle);
            handle = IntPtr.Zero;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool InitializeProcThreadAttributeList(
            IntPtr lpAttributeList,
            int dwAttributeCount,
            int dwFlags,
            ref nuint lpSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool UpdateProcThreadAttribute(
            IntPtr lpAttributeList,
            uint dwFlags,
            IntPtr attribute,
            IntPtr lpValue,
            IntPtr cbSize,
            IntPtr lpPreviousValue,
            IntPtr lpReturnSize);

        [DllImport("kernel32.dll")]
        private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);
    }

    private sealed class SafePseudoConsoleHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafePseudoConsoleHandle(IntPtr handle)
            : base(ownsHandle: true)
        {
            SetHandle(handle);
        }

        protected override bool ReleaseHandle()
        {
            ClosePseudoConsole(handle);
            return true;
        }

        [DllImport("kernel32.dll", EntryPoint = "ClosePseudoConsole")]
        private static extern void ClosePseudoConsole(IntPtr hPC);
    }
}
