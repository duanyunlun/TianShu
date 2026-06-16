using System.Collections;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace TianShu.Cli.Tests;

[Collection("EnvironmentVariables")]
public sealed class TianShuCliProcessAcceptanceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private static readonly SemaphoreSlim BuildGate = new(1, 1);
    private static bool projectsBuilt;

    [Fact(Skip = "旧 AppHost send 兼容路径已移除；真实 CLI 默认 send 路径由 Kernel→Runtime loop 用例覆盖。")]
    public async Task Send_WithResumeLatest_UsesRealCliProcess_And_WritesArtifacts()
    {
        using var workspace = new CliProcessWorkspace();
        var scenarioPath = workspace.WriteScenario(
            CreateScenario(
                threadListSteps:
                [
                    CreateThreadListStep("thread-send-last-001", "最近线程", workspace.RootPath),
                ],
                threadResumeSteps:
                [
                    CreateThreadResultStep("thread-send-last-001", "最近线程", workspace.RootPath),
                ],
                turnStartSteps:
                [
                    CreateTurnStartStep(
                        threadId: "thread-send-last-001",
                        turnId: "turn-send-process-001",
                        assistantText: "send process ok",
                        responseStatus: "inProgress",
                        emitTurnCompleted: true),
                ]));

        var requestLogPath = workspace.GetPath("fake-kernel-requests.jsonl");
        var artifactsRoot = workspace.GetPath("artifacts");
        var result = await RunCliProcessAsync(
            workspace,
            scenarioPath,
            requestLogPath,
            [
                "send",
                "--apphost-control-plane",
                "--message",
                "请继续验证",
                "--cwd",
                workspace.RootPath,
                "--config",
                workspace.ConfigPath,
                "--profile",
                "work",
                "--apphost-project",
                GetFakeAppHostProjectPath(),
                "--artifacts",
                artifactsRoot,
                "--resume-latest",
                "--json",
            ],
            stdinText: null,
            timeout: TimeSpan.FromSeconds(20));

        Assert.Equal(0, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StdErr), result.StdErr);

        using var outputDocument = JsonDocument.Parse(result.StdOut);
        Assert.Equal("thread-send-last-001", outputDocument.RootElement.GetProperty("threadId").GetString());
        Assert.Equal("turn-send-process-001", outputDocument.RootElement.GetProperty("turnId").GetString());
        Assert.Equal("completed", outputDocument.RootElement.GetProperty("turnStatus").GetString());
        Assert.True(outputDocument.RootElement.GetProperty("reusedExistingThread").GetBoolean());
        Assert.Equal("send process ok", outputDocument.RootElement.GetProperty("resultText").GetString());

        var runDirectory = Assert.Single(Directory.GetDirectories(artifactsRoot));
        using var summaryDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(runDirectory, "summary.json")));
        Assert.True(summaryDocument.RootElement.GetProperty("resumeLatestThread").GetBoolean());
        Assert.Equal("thread-send-last-001", summaryDocument.RootElement.GetProperty("threadId").GetString());

        Assert.Equal(new[] { "initialize", "initialized", "thread/list", "thread/resume", "turn/start" }, ReadRequestMethods(requestLogPath));
        var listRequest = Assert.Single(
            ReadJsonLines(requestLogPath),
            static root => string.Equals(root.GetProperty("method").GetString(), "thread/list", StringComparison.Ordinal));
        Assert.Equal(workspace.RootPath, listRequest.GetProperty("params").GetProperty("cwd").GetString());
    }

    [Fact]
    public async Task FollowUp_WithResumeLatest_UsesRealCliProcess()
    {
        using var workspace = new CliProcessWorkspace();
        var scenarioPath = workspace.WriteScenario(
            CreateScenario(
                threadListSteps:
                [
                    CreateThreadListStep("thread-followup-last-001", "最近 follow-up 线程", workspace.RootPath),
                ],
                threadResumeSteps:
                [
                    CreateThreadResultStep("thread-followup-last-001", "最近 follow-up 线程", workspace.RootPath),
                ],
                turnStartSteps:
                [
                    CreateTurnStartStep(
                        threadId: "thread-followup-last-001",
                        turnId: "turn-followup-process-001",
                        assistantText: "follow-up process ok",
                        responseStatus: "completed",
                        emitTurnCompleted: true),
                ]));

        var requestLogPath = workspace.GetPath("fake-kernel-requests.jsonl");
        var result = await RunCliProcessAsync(
            workspace,
            scenarioPath,
            requestLogPath,
            [
                "follow-up",
                "--message",
                "继续下一步",
                "--mode",
                "queue",
                "--cwd",
                workspace.RootPath,
                "--config",
                workspace.ConfigPath,
                "--profile",
                "work",
                "--apphost-project",
                GetFakeAppHostProjectPath(),
                "--resume-latest",
                "--json",
            ],
            stdinText: null,
            timeout: TimeSpan.FromSeconds(20));

        Assert.Equal(0, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StdErr), result.StdErr);

        using var outputDocument = JsonDocument.Parse(result.StdOut);
        Assert.Equal("thread-followup-last-001", outputDocument.RootElement.GetProperty("threadId").GetString());
        Assert.Equal("turn-followup-process-001", outputDocument.RootElement.GetProperty("turnId").GetString());
        Assert.Equal("completed", outputDocument.RootElement.GetProperty("turnStatus").GetString());
        Assert.Equal("Queue", outputDocument.RootElement.GetProperty("requestedMode").GetString());

        Assert.Equal(
            new[] { "initialize", "initialized", "thread/list", "thread/resume", "tianshu/thread/pending_input/update", "turn/start", "tianshu/thread/pending_input/update" },
            ReadRequestMethods(requestLogPath));
    }

    [Theory]
    [InlineData("thread", "resume", "thread/resume", "thread-process-resume-001")]
    [InlineData("thread", "fork", "thread/fork", "thread-process-fork-001")]
    public async Task ThreadCommands_UseRealCliProcess(string command, string subcommand, string kernelMethod, string expectedThreadId)
    {
        using var workspace = new CliProcessWorkspace();
        var scenarioPath = workspace.WriteScenario(
            CreateScenario(
                threadStartSteps:
                [
                    CreateThreadResultStep("thread-initialize-001", "初始化线程", workspace.RootPath),
                ],
                extraSteps: new Dictionary<string, object[]>
                {
                    [kernelMethod] =
                    [
                        CreateThreadResultStep(expectedThreadId, "线程进程验收", workspace.RootPath),
                    ],
                }));

        var requestLogPath = workspace.GetPath("fake-kernel-requests.jsonl");
        var result = await RunCliProcessAsync(
            workspace,
            scenarioPath,
            requestLogPath,
            [
                command,
                subcommand,
                "--thread-id",
                "thread-source-001",
                "--cwd",
                workspace.RootPath,
                "--config",
                workspace.ConfigPath,
                "--profile",
                "work",
                "--apphost-project",
                GetFakeAppHostProjectPath(),
                "--json",
            ],
            stdinText: null,
            timeout: TimeSpan.FromSeconds(20));

        Assert.Equal(0, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StdErr), result.StdErr);
        using var outputDocument = JsonDocument.Parse(result.StdOut);
        Assert.Equal(expectedThreadId, outputDocument.RootElement.GetProperty("threadId").GetString());
        Assert.Equal(new[] { "initialize", "initialized", "thread/start", kernelMethod }, ReadRequestMethods(requestLogPath));
    }

    [Theory]
    [InlineData("resume", "thread/resume", "thread-startup-last-001", "已恢复线程：thread-startup-last-001")]
    [InlineData("fork", "thread/fork", "thread-startup-forked-001", "已分叉线程：thread-startup-forked-001")]
    public async Task TopLevelStartupThreadCommands_WithLast_UseRealCliProcess(
        string command,
        string kernelMethod,
        string expectedThreadId,
        string expectedStatusText)
    {
        using var workspace = new CliProcessWorkspace();
        var scenarioPath = workspace.WriteScenario(
            CreateScenario(
                threadListSteps:
                [
                    CreateThreadListStep("thread-source-001", "最近启动线程", workspace.RootPath),
                ],
                extraSteps: new Dictionary<string, object[]>
                {
                    [kernelMethod] =
                    [
                        CreateThreadResultStep(expectedThreadId, "最近启动线程", workspace.RootPath),
                    ],
                }));

        var requestLogPath = workspace.GetPath("fake-kernel-requests.jsonl");
        var result = await RunCliProcessAsync(
            workspace,
            scenarioPath,
            requestLogPath,
            [
                command,
                "--last",
                "--cwd",
                workspace.RootPath,
                "--config",
                workspace.ConfigPath,
                "--profile",
                "work",
                "--apphost-project",
                GetFakeAppHostProjectPath(),
            ],
            stdinText: "/exit" + Environment.NewLine,
            timeout: TimeSpan.FromSeconds(20));

        Assert.Equal(0, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StdErr), result.StdErr);
        Assert.Contains(expectedStatusText, result.StdOut, StringComparison.Ordinal);
        Assert.Contains("天枢 TianShu 已启动。", result.StdOut, StringComparison.Ordinal);
        Assert.Equal(new[] { "initialize", "initialized", "thread/list", kernelMethod }, ReadRequestMethods(requestLogPath));
    }

    [Fact]
    public async Task Chat_WithResumeLatest_And_JsonlProtocol_UsesRealCliProcess()
    {
        using var workspace = new CliProcessWorkspace();
        var scenarioPath = workspace.WriteScenario(
            CreateScenario(
                threadListSteps:
                [
                    CreateThreadListStep("thread-chat-last-001", "最近聊天线程", workspace.RootPath),
                ],
                threadResumeSteps:
                [
                    CreateThreadResultStep("thread-chat-last-001", "最近聊天线程", workspace.RootPath),
                    CreateThreadResultStep("thread-chat-last-001", "最近聊天线程", workspace.RootPath),
                ],
                turnStartSteps:
                [
                    CreateTurnStartStep(
                        threadId: "thread-chat-last-001",
                        turnId: "turn-chat-process-001",
                        assistantText: "chat jsonl ok",
                        responseStatus: "inProgress",
                        emitTurnCompleted: true),
                ]));

        var requestLogPath = workspace.GetPath("fake-kernel-requests.jsonl");
        var artifactsRoot = workspace.GetPath("chat-artifacts");
        var result = await RunCliProcessAsync(
            workspace,
            scenarioPath,
            requestLogPath,
            [
                "chat",
                "--message",
                "请只回答ok",
                "--protocol",
                "jsonl",
                "--resume-latest",
                "--cwd",
                workspace.RootPath,
                "--config",
                workspace.ConfigPath,
                "--profile",
                "work",
                "--apphost-project",
                GetFakeAppHostProjectPath(),
                "--artifacts",
                artifactsRoot,
            ],
            stdinText: null,
            timeout: TimeSpan.FromSeconds(20));

        Assert.Equal(0, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StdErr), result.StdErr);

        var outputLines = result.StdOut
            .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static line => JsonDocument.Parse(line))
            .ToArray();
        try
        {
            Assert.Contains(outputLines, static document =>
                string.Equals(document.RootElement.GetProperty("type").GetString(), "stdout", StringComparison.Ordinal)
                && string.Equals(document.RootElement.GetProperty("text").GetString(), "chat jsonl ok", StringComparison.Ordinal));
            Assert.Contains(outputLines, static document =>
                string.Equals(document.RootElement.GetProperty("type").GetString(), "stdout", StringComparison.Ordinal)
                && string.Equals(document.RootElement.GetProperty("text").GetString(), "天枢 TianShu 已启动。", StringComparison.Ordinal));
        }
        finally
        {
            foreach (var document in outputLines)
            {
                document.Dispose();
            }
        }

        Assert.Equal(new[] { "initialize", "initialized", "thread/list", "thread/resume", "thread/resume", "turn/start" }, ReadRequestMethods(requestLogPath));
        var runDirectory = Assert.Single(Directory.GetDirectories(artifactsRoot));
        Assert.Contains(
            ReadJsonLines(Path.Combine(runDirectory, "projection-records.jsonl")),
            static root => string.Equals(root.GetProperty("kind").GetString(), "assistant_block_committed", StringComparison.Ordinal)
                           && string.Equals(root.GetProperty("text").GetString(), "chat jsonl ok", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Chat_ConsoleCancelEvent_InterruptsActiveTurn_And_ExitsCleanly()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var workspace = new CliProcessWorkspace();
        var scenarioPath = workspace.WriteScenario(
            CreateScenario(
                threadStartSteps:
                [
                    CreateThreadResultStep("thread-chat-console-001", "控制台中断线程", workspace.RootPath),
                ],
                turnStartSteps:
                [
                    CreateTurnStartStep(
                        threadId: "thread-chat-console-001",
                        turnId: "turn-chat-console-001",
                        assistantText: "处理中",
                        responseStatus: "inProgress",
                        emitTurnCompleted: false),
                ],
                extraSteps: new Dictionary<string, object[]>
                {
                    ["turn/interrupt"] =
                    [
                        CreateInterruptStep("thread-chat-console-001", "turn-chat-console-001"),
                    ],
                }));

        var requestLogPath = workspace.GetPath("fake-kernel-requests.jsonl");
        await EnsureProjectsBuiltAsync();

        using var process = WindowsConsoleProcess.Start(
            GetCliExecutablePath(),
            [
                "chat",
                "--cwd",
                workspace.RootPath,
                "--config",
                workspace.ConfigPath,
                "--profile",
                "work",
                "--apphost-project",
                GetFakeAppHostProjectPath(),
            ],
            FindRepoRoot(),
            CreateCliProcessEnvironment(workspace, scenarioPath, requestLogPath));

        await process.WriteLineAsync("请先保持运行");
        await WaitUntilAsync(
            () => File.Exists(requestLogPath)
                  && ReadRequestMethods(requestLogPath).Contains("turn/start", StringComparer.Ordinal),
            TimeSpan.FromSeconds(10),
            "等待 chat 发出 turn/start");

        process.SendBreak();
        await WaitUntilAsync(
            () => File.Exists(requestLogPath)
                  && ReadRequestMethods(requestLogPath).Contains("turn/interrupt", StringComparer.Ordinal),
            TimeSpan.FromSeconds(10),
            "等待 chat 发出 turn/interrupt");

        await Task.Delay(500);
        await process.WriteLineAsync("/exit");

        var result = await process.WaitForExitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(0, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StdErr), result.StdErr);
        Assert.Contains("天枢 TianShu 已启动。", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("已请求中断当前回合，等待确认。", result.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("chat 执行失败", result.StdOut, StringComparison.Ordinal);
        Assert.Equal(
            new[] { "initialize", "initialized", "thread/start", "turn/start", "turn/interrupt", "tianshu/thread/pending_input/update" },
            ReadRequestMethods(requestLogPath));
    }

    private static object CreateScenario(
        object[]? threadListSteps = null,
        object[]? threadStartSteps = null,
        object[]? threadResumeSteps = null,
        object[]? turnStartSteps = null,
        Dictionary<string, object[]>? extraSteps = null)
    {
        var methods = new Dictionary<string, object[]>(StringComparer.Ordinal);
        if (threadListSteps is not null)
        {
            methods["thread/list"] = threadListSteps;
        }

        if (threadStartSteps is not null)
        {
            methods["thread/start"] = threadStartSteps;
        }

        if (threadResumeSteps is not null)
        {
            methods["thread/resume"] = threadResumeSteps;
        }

        if (turnStartSteps is not null)
        {
            methods["turn/start"] = turnStartSteps;
        }

        if (extraSteps is not null)
        {
            foreach (var pair in extraSteps)
            {
                methods[pair.Key] = pair.Value;
            }
        }

        return new { methods };
    }

    private static object CreateThreadListStep(string threadId, string threadName, string cwd)
        => new
        {
            result = new
            {
                data = new[]
                {
                    new
                    {
                        id = threadId,
                        name = threadName,
                        cwd,
                    },
                },
            },
        };

    private static object CreateThreadResultStep(string threadId, string threadName, string cwd)
        => new
        {
            result = new
            {
                thread = new
                {
                    id = threadId,
                    preview = threadName,
                    name = threadName,
                    cwd,
                    turns = Array.Empty<object>(),
                },
            },
        };

    private static object CreateTurnStartStep(
        string threadId,
        string turnId,
        string assistantText,
        string responseStatus,
        bool emitTurnCompleted)
        => new
        {
            result = new
            {
                turn = new
                {
                    id = turnId,
                    status = responseStatus,
                },
            },
            notifications = BuildTurnNotifications(threadId, turnId, assistantText, emitTurnCompleted),
        };

    private static object CreateInterruptStep(string threadId, string turnId)
        => new
        {
            result = new
            {
                acknowledged = true,
            },
            notifications = new object[]
            {
                new
                {
                    method = "turn/completed",
                    @params = new
                    {
                        threadId,
                        turn = new
                        {
                            id = turnId,
                            status = "interrupted",
                        },
                    },
                },
            },
        };

    private static object[] BuildTurnNotifications(string threadId, string turnId, string assistantText, bool emitTurnCompleted)
    {
        var notifications = new List<object>
        {
            new
            {
                method = "item/delta",
                @params = new
                {
                    threadId,
                    turnId,
                    item = new
                    {
                        id = $"item-{turnId}",
                        type = "assistant_message",
                        delta = assistantText,
                    },
                },
            },
            new
            {
                method = "rawResponseItem/completed",
                @params = new
                {
                    threadId,
                    turnId,
                    item = new
                    {
                        id = $"item-{turnId}",
                        type = "assistant_message",
                        text = assistantText,
                    },
                },
            },
        };

        if (emitTurnCompleted)
        {
            notifications.Add(new
            {
                method = "turn/completed",
                @params = new
                {
                    threadId,
                    turn = new
                    {
                        id = turnId,
                        status = "completed",
                    },
                },
            });
        }

        return notifications.ToArray();
    }

    private static async Task<CliProcessResult> RunCliProcessAsync(
        CliProcessWorkspace workspace,
        string scenarioPath,
        string requestLogPath,
        IReadOnlyList<string> cliArgs,
        string? stdinText,
        TimeSpan timeout)
    {
        await EnsureProjectsBuiltAsync();

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = FindRepoRoot(),
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(GetCliProjectPath());
        startInfo.ArgumentList.Add("--");
        foreach (var arg in cliArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        startInfo.Environment["OPENAI_API_KEY"] = "test-key";
        startInfo.Environment["TIANSHU_FAKE_KERNEL_SCENARIO_PATH"] = scenarioPath;
        startInfo.Environment["TIANSHU_FAKE_KERNEL_REQUEST_LOG_PATH"] = requestLogPath;
        startInfo.Environment["TIANSHU_HOME"] = workspace.TianShuHomePath;
        startInfo.Environment["TIANSHU_STATE_HOME"] = workspace.KernelHomePath;
        startInfo.Environment["TIANSHU_SESSIONS_HOME"] = workspace.SessionsHomePath;

        using var process = new Process
        {
            StartInfo = startInfo,
        };

        Assert.True(process.Start(), "CLI 真实进程启动失败。");
        if (stdinText is not null)
        {
            await process.StandardInput.WriteAsync(stdinText);
        }

        process.StandardInput.Close();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeoutSource = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
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
                // ignore cleanup failure
            }

            throw new TimeoutException($"CLI 真实进程执行超时：{string.Join(' ', cliArgs)}");
        }

        return new CliProcessResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
    }

    private static Dictionary<string, string> CreateCliProcessEnvironment(
        CliProcessWorkspace workspace,
        string scenarioPath,
        string requestLogPath)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                environment[key] = value;
            }
        }

        environment["OPENAI_API_KEY"] = "test-key";
        environment["TIANSHU_FAKE_KERNEL_SCENARIO_PATH"] = scenarioPath;
        environment["TIANSHU_FAKE_KERNEL_REQUEST_LOG_PATH"] = requestLogPath;
        environment["TIANSHU_HOME"] = workspace.TianShuHomePath;
        environment["TIANSHU_STATE_HOME"] = workspace.KernelHomePath;
        environment["TIANSHU_SESSIONS_HOME"] = workspace.SessionsHomePath;
        return environment;
    }

    private static async Task EnsureProjectsBuiltAsync()
    {
        if (projectsBuilt)
        {
            return;
        }

        await BuildGate.WaitAsync();
        try
        {
            if (projectsBuilt)
            {
                return;
            }

            await BuildProjectAsync(GetFakeAppHostProjectPath());
            await BuildProjectAsync(GetCliProjectPath());
            projectsBuilt = true;
        }
        finally
        {
            BuildGate.Release();
        }
    }

    private static async Task BuildProjectAsync(string projectPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = FindRepoRoot(),
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("-nologo");
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("minimal");
        startInfo.ArgumentList.Add("-m:1");
        startInfo.ArgumentList.Add("/p:UseSharedCompilation=false");
        startInfo.ArgumentList.Add("/nodeReuse:false");

        using var process = new Process
        {
            StartInfo = startInfo,
        };

        Assert.True(process.Start(), $"项目构建启动失败：{projectPath}");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
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
                // ignore cleanup failure
            }

            throw new TimeoutException($"项目构建超时：{projectPath}");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        Assert.True(
            process.ExitCode == 0,
            $"项目构建失败：{projectPath}{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string description)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.True(condition(), $"{description} 超时。");
    }

    private static string[] ReadRequestMethods(string requestLogPath)
        => ReadJsonLines(requestLogPath)
            .Select(static root => root.GetProperty("method").GetString())
            .Where(static method => !string.IsNullOrWhiteSpace(method))
            .Cast<string>()
            .ToArray();

    private static JsonElement[] ReadJsonLines(string path)
        => File.ReadAllLines(path)
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Select(static line =>
            {
                using var document = JsonDocument.Parse(line);
                return document.RootElement.Clone();
            })
            .ToArray();

    private static string GetCliProjectPath()
        => Path.Combine(FindRepoRoot(), "src", "Presentations", "TianShu.Cli", "TianShu.Cli.csproj");

    private static string GetCliExecutablePath()
    {
        var cliDirectory = Path.GetDirectoryName(GetCliProjectPath())
                           ?? throw new DirectoryNotFoundException("未找到 TianShu.Cli 项目目录。");
        var executablePath = Path.Combine(cliDirectory, "bin", "Debug", "net10.0", "TianShu.Cli.exe");
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("未找到 TianShu.Cli.exe。", executablePath);
        }

        return executablePath;
    }

    private static string GetFakeAppHostProjectPath()
        => Path.Combine(FindRepoRoot(), "tests", "TianShu.Cli.Tests", "FakeCliKernel", "FakeCliKernel.csproj");

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TianShu.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("未找到 TianShu.sln。");
    }

    private sealed record CliProcessResult(int ExitCode, string StdOut, string StdErr);

    private sealed class CliProcessWorkspace : IDisposable
    {
        public CliProcessWorkspace()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "tianshu-cli-process", Guid.NewGuid().ToString("N"));
            TianShuHomePath = Path.Combine(RootPath, ".tianshu-home");
            KernelHomePath = Path.Combine(RootPath, ".kernel-home");
            SessionsHomePath = Path.Combine(RootPath, ".tianshu-sessions");
            ConfigPath = Path.Combine(RootPath, ".tianshu", "tianshu.toml");

            Directory.CreateDirectory(RootPath);
            Directory.CreateDirectory(TianShuHomePath);
            Directory.CreateDirectory(KernelHomePath);
            Directory.CreateDirectory(SessionsHomePath);
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);

            File.WriteAllText(
                ConfigPath,
                """
                model = "gpt-5-codex"
                provider = "openai"
                approval_policy = "never"

                [profiles.work]
                model = "gpt-5-codex"
                provider = "openai"
                approval_policy = "never"

                [providers.openai]
                base_url = "https://api.openai.com/v1"
                api_key_env = "OPENAI_API_KEY"
                default_protocol = "responses"
                """,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        public string RootPath { get; }

        public string TianShuHomePath { get; }

        public string KernelHomePath { get; }

        public string SessionsHomePath { get; }

        public string ConfigPath { get; }

        public string GetPath(string relativePath)
        {
            var path = Path.Combine(RootPath, relativePath);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return path;
        }

        public string WriteScenario(object scenario)
        {
            var path = GetPath("fake-kernel-scenario.json");
            File.WriteAllText(
                path,
                JsonSerializer.Serialize(scenario, JsonOptions),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return path;
        }

        public void Dispose()
        {
            if (!Directory.Exists(RootPath))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(RootPath, "*", SearchOption.AllDirectories))
            {
                var attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                }
            }

            Directory.Delete(RootPath, recursive: true);
        }
    }
}
