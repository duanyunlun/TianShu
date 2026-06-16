using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;


[Collection("EnvironmentVariables")]
public sealed class AppHostServerCommandExecParityTests
{
    [Fact]
    public async Task RunAsync_ShouldRejectWindowsSandboxSetupStartWhenCwdRelative()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            const string input = """{"jsonrpc":"2.0","id":1,"method":"windowsSandbox/setupStart","params":{"mode":"elevated","cwd":"relative-root"}}""";
            using var writer = new StringWriter();
            var server = new AppHostServer(
                new StringReader(TianShu.Execution.Integration.Tests.KernelAppServerTestProtocol.WithInitialize(input)),
                writer,
                new KernelThreadStore(storePath));

            await server.RunAsync(CancellationToken.None);

            using var response = FindMessageById(writer.ToString(), 1);
            var error = response.RootElement.GetProperty("error");
            Assert.Equal(-32600, error.GetProperty("code").GetInt32());
            Assert.Contains("Invalid request", error.GetProperty("message").GetString(), StringComparison.Ordinal);
            Assert.Contains("cwd", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRejectCommandExecWhenTimeoutAndDisableTimeoutAreCombined()
    {
        await AssertCommandExecErrorAsync(
            new
            {
                command = CreateShellCommand("Start-Sleep -Seconds 1", "sleep 1"),
                processId = "invalid-timeout-1",
                timeoutMs = 1000,
                disableTimeout = true,
            },
            -32602,
            "command/exec cannot set both timeoutMs and disableTimeout");
    }

    [Fact]
    public async Task RunAsync_ShouldRejectCommandExecWhenOutputCapAndDisableOutputCapAreCombined()
    {
        await AssertCommandExecErrorAsync(
            new
            {
                command = CreateShellCommand("Start-Sleep -Seconds 1", "sleep 1"),
                processId = "invalid-cap-1",
                outputBytesCap = 1024,
                disableOutputCap = true,
            },
            -32602,
            "command/exec cannot set both outputBytesCap and disableOutputCap");
    }

    [Fact]
    public async Task RunAsync_ShouldRejectCommandExecWhenTimeoutMsNegative()
    {
        await AssertCommandExecErrorAsync(
            new
            {
                command = CreateShellCommand("Start-Sleep -Seconds 1", "sleep 1"),
                processId = "negative-timeout-1",
                timeoutMs = -1,
            },
            -32602,
            "command/exec timeoutMs must be non-negative, got -1");
    }

    [Fact]
    public async Task RunAsync_ShouldRejectCommandExecStreamingWithoutProcessId()
    {
        await AssertCommandExecErrorAsync(
            new
            {
                command = CreateShellCommand("Write-Output ready", "printf 'ready\n'"),
                streamStdoutStderr = true,
            },
            -32600,
            "command/exec tty or streaming requires a client-supplied processId");
    }

    [Fact]
    public async Task RunAsync_ShouldRejectCommandExecSizeWithoutTty()
    {
        await AssertCommandExecErrorAsync(
            new
            {
                command = CreateShellCommand("Write-Output ready", "printf 'ready\n'"),
                size = new
                {
                    rows = 24,
                    cols = 80,
                },
            },
            -32602,
            "command/exec size requires tty: true");
    }

    [Fact]
    public async Task RunAsync_ShouldStreamPipeCommandExecOutputAndAcceptWrite()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var inputChannel = CreateInputChannel();
        var reader = new ChannelTextReader(inputChannel.Reader);
        using var writer = new ChannelTextWriter();
        var server = new AppHostServer(reader, writer, new KernelThreadStore(storePath));
        var runTask = server.RunAsync(CancellationToken.None);

        try
        {
            await TianShu.Execution.Integration.Tests.KernelAppServerTestProtocol.InitializeAsync(inputChannel.Writer, writer.Lines, TimeSpan.FromSeconds(5));

            const string processId = "pipe-1";
            var commandRequest = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "command/exec",
                @params = new
                {
                    command = CreateInteractiveEchoCommand(),
                    processId,
                    streamStdin = true,
                    streamStdoutStderr = true,
                    cwd = root.Replace("\\", "/"),
                    approvalPolicy = "never",
                },
            });
            Assert.True(inputChannel.Writer.TryWrite(commandRequest));

            _ = await WaitForJsonRpcMethodAsync(writer.Lines, "command/exec/outputDelta", TimeSpan.FromSeconds(10));

            var writeRequest = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "command/exec/write",
                @params = new
                {
                    processId,
                    deltaBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("hello\n")),
                    closeStdin = true,
                },
            });
            Assert.True(inputChannel.Writer.TryWrite(writeRequest));

            _ = await WaitForJsonRpcResponseIdAsync(writer.Lines, 2, TimeSpan.FromSeconds(10));
            await WaitForCapturedMessageIdAsync(writer, 1, TimeSpan.FromSeconds(15));

            inputChannel.Writer.TryComplete();
            await runTask.WaitAsync(TimeSpan.FromSeconds(15));

            var messages = ParseCapturedMessages(writer.CapturedText.ToString());
            var commandResponse = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("result");
            Assert.Equal(0, commandResponse.GetProperty("exitCode").GetInt32());
            Assert.Equal(string.Empty, commandResponse.GetProperty("stdout").GetString());
            Assert.Equal(string.Empty, commandResponse.GetProperty("stderr").GetString());

            var writeResponse = messages.Single(x => IsResponseId(x.RootElement, 2)).RootElement.GetProperty("result");
            Assert.Equal("{}", writeResponse.GetRawText());

            var stdout = CollectCommandExecOutput(messages, processId, "stdout");
            var stderr = CollectCommandExecOutput(messages, processId, "stderr");
            Assert.Contains("out-start", stdout, StringComparison.Ordinal);
            Assert.Contains("out:hello", stdout, StringComparison.Ordinal);
            Assert.Contains("err-start", stderr, StringComparison.Ordinal);
            Assert.Contains("err:hello", stderr, StringComparison.Ordinal);
        }
        finally
        {
            inputChannel.Writer.TryComplete();
            await runTask.WaitAsync(TimeSpan.FromSeconds(15));
            DeleteDirectory(root);
        }
    }



    [Fact]
    public async Task RunAsync_ShouldTreatTtyCommandExecAsStreamingAndAcceptWrite()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var inputChannel = CreateInputChannel();
        var reader = new ChannelTextReader(inputChannel.Reader);
        using var writer = new ChannelTextWriter();
        var server = new AppHostServer(reader, writer, new KernelThreadStore(storePath));
        var runTask = server.RunAsync(CancellationToken.None);

        try
        {
            await TianShu.Execution.Integration.Tests.KernelAppServerTestProtocol.InitializeAsync(inputChannel.Writer, writer.Lines, TimeSpan.FromSeconds(5));

            const string processId = "tty-1";
            var commandRequest = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "command/exec",
                @params = new
                {
                    command = CreateTtyEchoCommand(root),
                    processId,
                    tty = true,
                    cwd = root.Replace("\\", "/"),
                    approvalPolicy = "never",
                },
            });
            Assert.True(inputChannel.Writer.TryWrite(commandRequest));

            _ = await WaitForCapturedCommandExecOutputContainsAsync(writer, processId, "stdout", "tty\n", TimeSpan.FromSeconds(15));

            var writeRequest = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "command/exec/write",
                @params = new
                {
                    processId,
                    deltaBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("world\r\n")),
                    closeStdin = !OperatingSystem.IsWindows(),
                },
            });
            Assert.True(inputChannel.Writer.TryWrite(writeRequest));

            _ = await WaitForJsonRpcResponseIdAsync(writer.Lines, 2, TimeSpan.FromSeconds(10));
            _ = await WaitForCapturedCommandExecOutputContainsAsync(writer, processId, "stdout", "echo:world\n", TimeSpan.FromSeconds(15));
            await WaitForCapturedMessageIdAsync(writer, 1, TimeSpan.FromSeconds(15));

            inputChannel.Writer.TryComplete();
            await runTask.WaitAsync(TimeSpan.FromSeconds(15));

            var messages = ParseCapturedMessages(writer.CapturedText.ToString());
            try
            {
                var stdout = NormalizeLineEndings(CollectCommandExecOutput(messages, processId, "stdout"));
                var response = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("result");
                Assert.Equal(0, response.GetProperty("exitCode").GetInt32());
                Assert.Equal(string.Empty, response.GetProperty("stdout").GetString());
                Assert.Equal(string.Empty, response.GetProperty("stderr").GetString());
                Assert.Contains("tty\n", stdout, StringComparison.Ordinal);
                Assert.Contains("echo:world\n", stdout, StringComparison.Ordinal);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            inputChannel.Writer.TryComplete();
            await runTask.WaitAsync(TimeSpan.FromSeconds(15));
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldHonorTtyInitialSizeAndResize()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var inputChannel = CreateInputChannel();
        var reader = new ChannelTextReader(inputChannel.Reader);
        using var writer = new ChannelTextWriter();
        var server = new AppHostServer(reader, writer, new KernelThreadStore(storePath));
        var runTask = server.RunAsync(CancellationToken.None);

        try
        {
            await TianShu.Execution.Integration.Tests.KernelAppServerTestProtocol.InitializeAsync(inputChannel.Writer, writer.Lines, TimeSpan.FromSeconds(5));

            const string processId = "tty-size-1";
            var commandRequest = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "command/exec",
                @params = new
                {
                    command = CreateTtySizeProbeCommand(root),
                    processId,
                    tty = true,
                    size = new
                    {
                        rows = 31,
                        cols = 101,
                    },
                    cwd = root.Replace("\\", "/"),
                    approvalPolicy = "never",
                },
            });
            Assert.True(inputChannel.Writer.TryWrite(commandRequest));

            _ = await WaitForCapturedCommandExecOutputContainsAsync(writer, processId, "stdout", "start:31 101\n", TimeSpan.FromSeconds(15));

            var resizeRequest = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "command/exec/resize",
                @params = new
                {
                    processId,
                    size = new
                    {
                        rows = 45,
                        cols = 132,
                    },
                },
            });
            Assert.True(inputChannel.Writer.TryWrite(resizeRequest));
            _ = await WaitForJsonRpcResponseIdAsync(writer.Lines, 2, TimeSpan.FromSeconds(10));

            var writeRequest = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 3,
                method = "command/exec/write",
                @params = new
                {
                    processId,
                    deltaBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("go\r\n")),
                    closeStdin = !OperatingSystem.IsWindows(),
                },
            });
            Assert.True(inputChannel.Writer.TryWrite(writeRequest));
            _ = await WaitForJsonRpcResponseIdAsync(writer.Lines, 3, TimeSpan.FromSeconds(10));

            _ = await WaitForCapturedCommandExecOutputContainsAsync(writer, processId, "stdout", "after:45 132\n", TimeSpan.FromSeconds(15));
            await WaitForCapturedMessageIdAsync(writer, 1, TimeSpan.FromSeconds(15));

            inputChannel.Writer.TryComplete();
            await runTask.WaitAsync(TimeSpan.FromSeconds(15));

            var messages = ParseCapturedMessages(writer.CapturedText.ToString());
            try
            {
                var stdout = NormalizeLineEndings(CollectCommandExecOutput(messages, processId, "stdout"));
                var response = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("result");
                Assert.Equal(0, response.GetProperty("exitCode").GetInt32());
                Assert.Equal(string.Empty, response.GetProperty("stdout").GetString());
                Assert.Equal(string.Empty, response.GetProperty("stderr").GetString());
                Assert.Contains("start:31 101\n", stdout, StringComparison.Ordinal);
                Assert.Contains("after:45 132\n", stdout, StringComparison.Ordinal);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            inputChannel.Writer.TryComplete();
            await runTask.WaitAsync(TimeSpan.FromSeconds(15));
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldTerminateTrackedCommandExecAndReturnNonZero()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var inputChannel = CreateInputChannel();
        var reader = new ChannelTextReader(inputChannel.Reader);
        using var writer = new ChannelTextWriter();
        var server = new AppHostServer(reader, writer, new KernelThreadStore(storePath));
        var runTask = server.RunAsync(CancellationToken.None);

        try
        {
            await TianShu.Execution.Integration.Tests.KernelAppServerTestProtocol.InitializeAsync(inputChannel.Writer, writer.Lines, TimeSpan.FromSeconds(5));

            const string processId = "terminate-1";
            var commandRequest = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "command/exec",
                @params = new
                {
                    command = CreateReadyAndSleepCommand(),
                    processId,
                    streamStdoutStderr = true,
                    cwd = root.Replace("\\", "/"),
                    approvalPolicy = "never",
                },
            });
            Assert.True(inputChannel.Writer.TryWrite(commandRequest));

            _ = await WaitForJsonRpcMethodAsync(writer.Lines, "command/exec/outputDelta", TimeSpan.FromSeconds(10));

            var terminateRequest = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "command/exec/terminate",
                @params = new
                {
                    processId,
                },
            });
            Assert.True(inputChannel.Writer.TryWrite(terminateRequest));

            _ = await WaitForJsonRpcResponseIdAsync(writer.Lines, 2, TimeSpan.FromSeconds(10));
            await WaitForCapturedMessageIdAsync(writer, 1, TimeSpan.FromSeconds(15));

            inputChannel.Writer.TryComplete();
            await runTask.WaitAsync(TimeSpan.FromSeconds(15));

            var messages = ParseCapturedMessages(writer.CapturedText.ToString());
            var terminateResponse = messages.Single(x => IsResponseId(x.RootElement, 2)).RootElement.GetProperty("result");
            Assert.Equal("{}", terminateResponse.GetRawText());

            var commandResponse = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("result");
            Assert.NotEqual(0, commandResponse.GetProperty("exitCode").GetInt32());
            Assert.Equal(string.Empty, commandResponse.GetProperty("stdout").GetString());
            Assert.Equal(string.Empty, commandResponse.GetProperty("stderr").GetString());

            var stdout = CollectCommandExecOutput(messages, processId, "stdout");
            Assert.Contains("ready", stdout, StringComparison.Ordinal);
        }
        finally
        {
            inputChannel.Writer.TryComplete();
            await runTask.WaitAsync(TimeSpan.FromSeconds(15));
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRejectResizeForNonTtyTrackedCommandExec()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var inputChannel = CreateInputChannel();
        var reader = new ChannelTextReader(inputChannel.Reader);
        using var writer = new ChannelTextWriter();
        var server = new AppHostServer(reader, writer, new KernelThreadStore(storePath));
        var runTask = server.RunAsync(CancellationToken.None);

        try
        {
            await TianShu.Execution.Integration.Tests.KernelAppServerTestProtocol.InitializeAsync(inputChannel.Writer, writer.Lines, TimeSpan.FromSeconds(5));

            const string processId = "resize-1";
            var commandRequest = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "command/exec",
                @params = new
                {
                    command = CreateReadyAndSleepCommand(),
                    processId,
                    streamStdoutStderr = true,
                    cwd = root.Replace("\\", "/"),
                    approvalPolicy = "never",
                },
            });
            Assert.True(inputChannel.Writer.TryWrite(commandRequest));

            _ = await WaitForJsonRpcMethodAsync(writer.Lines, "command/exec/outputDelta", TimeSpan.FromSeconds(10));

            var resizeRequest = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "command/exec/resize",
                @params = new
                {
                    processId,
                    size = new
                    {
                        rows = 40,
                        cols = 120,
                    },
                },
            });
            Assert.True(inputChannel.Writer.TryWrite(resizeRequest));

            _ = await WaitForJsonRpcResponseIdAsync(writer.Lines, 2, TimeSpan.FromSeconds(10));

            var terminateRequest = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 3,
                method = "command/exec/terminate",
                @params = new
                {
                    processId,
                },
            });
            Assert.True(inputChannel.Writer.TryWrite(terminateRequest));

            _ = await WaitForJsonRpcResponseIdAsync(writer.Lines, 3, TimeSpan.FromSeconds(10));
            await WaitForCapturedMessageIdAsync(writer, 1, TimeSpan.FromSeconds(15));

            inputChannel.Writer.TryComplete();
            await runTask.WaitAsync(TimeSpan.FromSeconds(15));

            var messages = ParseCapturedMessages(writer.CapturedText.ToString());
            var resizeError = messages.Single(x => IsResponseId(x.RootElement, 2)).RootElement.GetProperty("error");
            Assert.Equal(-32600, resizeError.GetProperty("code").GetInt32());
            Assert.Contains("not tty-backed", resizeError.GetProperty("message").GetString(), StringComparison.Ordinal);

            var terminateResponse = messages.Single(x => IsResponseId(x.RootElement, 3)).RootElement.GetProperty("result");
            Assert.Equal("{}", terminateResponse.GetRawText());
        }
        finally
        {
            inputChannel.Writer.TryComplete();
            await runTask.WaitAsync(TimeSpan.FromSeconds(15));
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldApplyOutputCapToStreamingCommandExecOutput()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var inputChannel = CreateInputChannel();
        var reader = new ChannelTextReader(inputChannel.Reader);
        using var writer = new ChannelTextWriter();
        var server = new AppHostServer(reader, writer, new KernelThreadStore(storePath));
        var runTask = server.RunAsync(CancellationToken.None);

        try
        {
            await TianShu.Execution.Integration.Tests.KernelAppServerTestProtocol.InitializeAsync(inputChannel.Writer, writer.Lines, TimeSpan.FromSeconds(5));

            const string processId = "stream-cap-1";
            var commandRequest = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "command/exec",
                @params = new
                {
                    command = CreateCapProbeCommand(),
                    processId,
                    streamStdoutStderr = true,
                    outputBytesCap = 5,
                    cwd = root.Replace("\\", "/"),
                    approvalPolicy = "never",
                },
            });
            Assert.True(inputChannel.Writer.TryWrite(commandRequest));

            _ = await WaitForJsonRpcMethodAsync(writer.Lines, "command/exec/outputDelta", TimeSpan.FromSeconds(10));

            var terminateRequest = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "command/exec/terminate",
                @params = new
                {
                    processId,
                },
            });
            Assert.True(inputChannel.Writer.TryWrite(terminateRequest));

            _ = await WaitForJsonRpcResponseIdAsync(writer.Lines, 2, TimeSpan.FromSeconds(10));
            await WaitForCapturedMessageIdAsync(writer, 1, TimeSpan.FromSeconds(15));

            inputChannel.Writer.TryComplete();
            await runTask.WaitAsync(TimeSpan.FromSeconds(15));

            var messages = ParseCapturedMessages(writer.CapturedText.ToString());
            var deltas = messages
                .Where(x => IsNotificationMethod(x.RootElement, "command/exec/outputDelta"))
                .Select(x => x.RootElement.GetProperty("params"))
                .Where(x => string.Equals(x.GetProperty("processId").GetString(), processId, StringComparison.Ordinal))
                .ToArray();
            Assert.NotEmpty(deltas);

            var stdoutDelta = deltas.Single(x => string.Equals(x.GetProperty("stream").GetString(), "stdout", StringComparison.Ordinal));
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(stdoutDelta.GetProperty("deltaBase64").GetString()!));
            Assert.Equal("abcde", decoded);
            Assert.True(stdoutDelta.GetProperty("capReached").GetBoolean());

            var commandResponse = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("result");
            Assert.NotEqual(0, commandResponse.GetProperty("exitCode").GetInt32());
            Assert.Equal(string.Empty, commandResponse.GetProperty("stdout").GetString());
            Assert.Equal(string.Empty, commandResponse.GetProperty("stderr").GetString());
        }
        finally
        {
            inputChannel.Writer.TryComplete();
            await runTask.WaitAsync(TimeSpan.FromSeconds(15));
            DeleteDirectory(root);
        }
    }

    private static async Task AssertCommandExecErrorAsync(object commandParams, int expectedCode, string expectedMessage)
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            var paramsJson = JsonSerializer.Serialize(new
            {
                cwd = root.Replace("\\", "/"),
                approvalPolicy = "never",
            }).TrimEnd('}');
            var requestJson = JsonSerializer.Serialize(commandParams).TrimStart('{');
            var mergedJson = paramsJson + "," + requestJson;
            var input = TianShu.Execution.Integration.Tests.KernelAppServerTestProtocol.WithInitialize(
                "{" + "\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"command/exec\",\"params\":" + mergedJson + "}");

            using var writer = new StringWriter();
            var server = new AppHostServer(new StringReader(input), writer, new KernelThreadStore(storePath));
            await server.RunAsync(CancellationToken.None);

            using var response = FindMessageById(writer.ToString(), 1);
            var error = response.RootElement.GetProperty("error");
            Assert.Equal(expectedCode, error.GetProperty("code").GetInt32());
            Assert.Equal(expectedMessage, error.GetProperty("message").GetString());
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static Channel<string> CreateInputChannel()
    {
        return Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });
    }

    private static string[] CreateShellCommand(string windowsScript, string unixScript)
    {
        return OperatingSystem.IsWindows()
            ? ["powershell", "-NoProfile", "-Command", windowsScript]
            : ["/bin/sh", "-lc", unixScript];
    }

    private static string[] CreateInteractiveEchoCommand()
    {
        return OperatingSystem.IsWindows()
            ? ["cmd.exe", "/v:on", "/d", "/c", "(echo out-start & echo err-start 1>&2 & set /p line= & echo out:!line! & echo err:!line! 1>&2)"]
            : ["/bin/sh", "-lc", "printf 'out-start\\n'; printf 'err-start\\n' >&2; IFS= read line; printf 'out:%s\\n' \"$line\"; printf 'err:%s\\n' \"$line\" >&2"];
    }

    private static string[] CreateReadyAndSleepCommand()
    {
        return CreateShellCommand(
            "$ErrorActionPreference='Stop'; [Console]::OutputEncoding=[System.Text.Encoding]::UTF8; Write-Output 'ready'; Start-Sleep -Seconds 30",
            "printf 'ready\\n'; sleep 30");
    }

    private static string[] CreateCapProbeCommand()
    {
        return CreateShellCommand(
            "$ErrorActionPreference='Stop'; [Console]::OutputEncoding=[System.Text.Encoding]::UTF8; Write-Output 'abcdefghij'; Start-Sleep -Seconds 30",
            "printf 'abcdefghij'; sleep 30");
    }


    private static string[] CreateTtyEchoCommand(string root)
    {
        var python = FindPythonExecutable();
        if (!string.IsNullOrWhiteSpace(python))
        {
            var scriptPath = Path.Combine(root, "tty_echo.py");
            File.WriteAllText(
                scriptPath,
                """
                import sys
                print('tty' if sys.stdin.isatty() else 'notty', flush=True)
                line = sys.stdin.readline().rstrip('\r\n')
                print(f'echo:{line}', flush=True)
                """,
                Encoding.UTF8);
            return [python, "-u", scriptPath];
        }

        if (!OperatingSystem.IsWindows())
        {
            return ["/bin/sh", "-lc", "stty -echo; if [ -t 0 ]; then printf 'tty\n'; else printf 'notty\n'; fi; IFS= read line; printf 'echo:%s\n' \"$line\""];
        }

        return ["cmd.exe", "/v:on", "/d", "/c", "(echo tty & set /p line= & echo echo:!line!)"];
    }

    private static string[] CreateTtySizeProbeCommand(string root)
    {
        var python = FindPythonExecutable();
        if (!string.IsNullOrWhiteSpace(python))
        {
            var scriptPath = Path.Combine(root, "tty_size.py");
            File.WriteAllText(
                scriptPath,
                """
                import sys, shutil
                size = shutil.get_terminal_size()
                print(f'start:{size.lines} {size.columns}', flush=True)
                sys.stdin.readline()
                size = shutil.get_terminal_size()
                print(f'after:{size.lines} {size.columns}', flush=True)
                """,
                Encoding.UTF8);
            return [python, "-u", scriptPath];
        }

        if (!OperatingSystem.IsWindows())
        {
            return ["/bin/sh", "-lc", "stty -echo; printf 'start:%s\n' \"$(stty size)\"; IFS= read _line; printf 'after:%s\n' \"$(stty size)\""];
        }

        return [
            "powershell",
            "-NoProfile",
            "-Command",
            "$ErrorActionPreference='Stop'; [Console]::OutputEncoding=[System.Text.Encoding]::UTF8; Write-Output ('start:' + [Console]::WindowHeight + ' ' + [Console]::WindowWidth); [void][Console]::In.ReadLine(); Write-Output ('after:' + [Console]::WindowHeight + ' ' + [Console]::WindowWidth)"];
    }

    private static string? FindPythonExecutable()
    {
        foreach (var candidate in new[] { "python", "python3" })
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = candidate,
                    ArgumentList = { "--version" },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    continue;
                }

                if (process.WaitForExit(2000) && process.ExitCode == 0)
                {
                    return candidate;
                }
            }
            catch
            {
                // ignore lookup failures and continue probing
            }
        }

        return null;
    }

    private static JsonDocument FindMessageById(string capturedText, long id)
    {
        foreach (var line in capturedText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var document = JsonDocument.Parse(line);
            if (IsResponseId(document.RootElement, id))
            {
                return document;
            }

            document.Dispose();
        }

        throw new Xunit.Sdk.XunitException($"未在 capturedText 中找到 id={id} 的 JSON-RPC 消息。");
    }

    private static bool IsResponseId(JsonElement json, long id)
    {
        return json.TryGetProperty("id", out var idElement)
               && idElement.ValueKind == JsonValueKind.Number
               && idElement.TryGetInt64(out var parsed)
               && parsed == id;
    }

    private static bool IsNotificationMethod(JsonElement json, string method)
    {
        return json.TryGetProperty("method", out var methodElement)
               && methodElement.ValueKind == JsonValueKind.String
               && string.Equals(methodElement.GetString(), method, StringComparison.Ordinal);
    }

    private static async Task<string> WaitForJsonRpcMethodAsync(ChannelReader<string> lines, string method, TimeSpan timeout)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        while (await lines.WaitToReadAsync(timeoutCts.Token))
        {
            while (lines.TryRead(out var line))
            {
                using var doc = JsonDocument.Parse(line);
                if (IsNotificationMethod(doc.RootElement, method))
                {
                    return line;
                }
            }
        }

        throw new TimeoutException($"未等到 method={method} 的 JSON-RPC 消息。");
    }

    private static async Task<string> WaitForJsonRpcResponseIdAsync(ChannelReader<string> lines, long id, TimeSpan timeout)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        while (await lines.WaitToReadAsync(timeoutCts.Token))
        {
            while (lines.TryRead(out var line))
            {
                using var doc = JsonDocument.Parse(line);
                if (IsResponseId(doc.RootElement, id))
                {
                    return line;
                }
            }
        }

        throw new TimeoutException($"未等到 id={id} 的 JSON-RPC response。");
    }

    private static async Task WaitForCapturedMessageIdAsync(ChannelTextWriter writer, long id, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var message = FindMessageById(writer.CapturedText.ToString(), id);
                return;
            }
            catch (Xunit.Sdk.XunitException)
            {
                await Task.Delay(50);
            }
        }

        throw new TimeoutException($"Did not observe id={id} in buffered JSON-RPC output. Captured={NormalizeLineEndings(writer.CapturedText.ToString())}");
    }

    private static async Task<string> WaitForCapturedCommandExecOutputContainsAsync(
        ChannelTextWriter writer,
        string processId,
        string stream,
        string expectedText,
        TimeSpan timeout)
    {
        var expected = NormalizeLineEndings(expectedText);
        var deadline = DateTime.UtcNow + timeout;
        string actualOutput = string.Empty;
        while (DateTime.UtcNow < deadline)
        {
            var messages = ParseCapturedMessages(writer.CapturedText.ToString());
            try
            {
                actualOutput = NormalizeLineEndings(CollectCommandExecOutput(messages, processId, stream));
                if (actualOutput.Contains(expected, StringComparison.Ordinal))
                {
                    return actualOutput;
                }
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Did not observe processId={processId} stream={stream} containing text {expectedText}. ActualOutput={actualOutput}; Captured={NormalizeLineEndings(writer.CapturedText.ToString())}");
    }

    private static JsonDocument[] ParseCapturedMessages(string capturedText)
    {
        return capturedText
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line))
            .ToArray();
    }

    private static string CollectCommandExecOutput(IEnumerable<JsonDocument> messages, string processId, string stream)
    {
        var builder = new StringBuilder();
        foreach (var message in messages)
        {
            if (!IsNotificationMethod(message.RootElement, "command/exec/outputDelta"))
            {
                continue;
            }

            var @params = message.RootElement.GetProperty("params");
            if (!string.Equals(@params.GetProperty("processId").GetString(), processId, StringComparison.Ordinal)
                || !string.Equals(@params.GetProperty("stream").GetString(), stream, StringComparison.Ordinal))
            {
                continue;
            }

            builder.Append(Encoding.UTF8.GetString(Convert.FromBase64String(@params.GetProperty("deltaBase64").GetString()!)));
        }

        return builder.ToString();
    }

    private static string NormalizeLineEndings(string value)
    {
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        normalized = StripTerminalControlSequences(normalized);
        return normalized;
    }

    private static string StripTerminalControlSequences(string value)
    {
        var withoutOsc = Regex.Replace(value, @"\u001B\][^\u0007\u001B]*(?:\u0007|\u001B\\)", string.Empty);
        return Regex.Replace(withoutOsc, @"\u001B\[[0-?]*[ -/]*[@-~]", string.Empty);
    }

    private sealed class ChannelTextReader(ChannelReader<string> source) : TextReader
    {
        public override async Task<string?> ReadLineAsync()
        {
            while (await source.WaitToReadAsync().ConfigureAwait(false))
            {
                if (source.TryRead(out var line))
                {
                    return line;
                }
            }

            return null;
        }
    }


    private sealed class ChannelTextWriter : TextWriter
    {
        private readonly Channel<string> lines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });

        private bool disposed;
        private readonly StringBuilder capturedText = new();

        public ChannelReader<string> Lines => lines.Reader;

        public StringBuilder CapturedText => capturedText;

        public override Encoding Encoding => Encoding.UTF8;

        public override Task WriteLineAsync(string? value)
        {
            if (disposed)
            {
                return Task.CompletedTask;
            }

            var line = value ?? string.Empty;
            capturedText.AppendLine(line);
            lines.Writer.TryWrite(line);
            return Task.CompletedTask;
        }

        public override Task FlushAsync() => Task.CompletedTask;

        protected override void Dispose(bool disposing)
        {
            if (!disposing || disposed)
            {
                base.Dispose(disposing);
                return;
            }

            disposed = true;
            lines.Writer.TryComplete();
            base.Dispose(disposing);
        }
    }

    private static string CreateTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "tianshu-kernel-parity", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // ignore cleanup failures
        }
    }
}






