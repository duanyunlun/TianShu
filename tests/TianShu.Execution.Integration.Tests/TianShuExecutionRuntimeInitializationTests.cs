using System.Reflection;
using System.Text;
using TianShu.AppHost.Configuration;
using TianShu.Execution.Runtime.Models;
using TianShu.Execution.Runtime;
using TianShu.Execution.Runtime.Events;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Sessions;
using TianShu.Contracts.Tools;
using TianShu.RuntimeComposition;

namespace TianShu.Execution.Integration.Tests;

public sealed class TianShuExecutionRuntimeInitializationTests
{
    [Fact]
    public async Task InitializeAsync_WhenProcessExitsImmediately_ThrowsStartupFailureWithDiagnostics()
    {
        var directory = CreateScriptDirectory();
        var executablePath = WriteBatchExecutable(
            directory,
            "exit-immediately",
            "@echo off\r\necho fatal startup failure 1>&2\r\nexit /b 5\r\n");

        await using var runtime = new TianShuExecutionRuntime();
        var options = new ExecutionRuntimeOptions
        {
            UseDotNetProjectLauncher = false,
            ExecutablePath = executablePath,
            WorkingDirectory = directory,
            StartupTimeout = TimeSpan.FromSeconds(2),
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => InitializeRuntimeAsync(runtime, options));
        Assert.Contains("启动后立即退出", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ExitCode=5", exception.Message, StringComparison.Ordinal);
        Assert.Contains("fatal startup failure", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeAsync_WhenServerDoesNotRespond_ThrowsStartupTimeoutException()
    {
        var directory = CreateScriptDirectory();
        var executablePath = WriteBatchExecutable(
            directory,
            "sleep-forever",
            "@echo off\r\npowershell.exe -NoProfile -Command \"Start-Sleep -Seconds 5\"\r\n");

        await using var runtime = new TianShuExecutionRuntime();
        var options = new ExecutionRuntimeOptions
        {
            UseDotNetProjectLauncher = false,
            ExecutablePath = executablePath,
            WorkingDirectory = directory,
            StartupTimeout = TimeSpan.FromMilliseconds(300),
        };

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => InitializeRuntimeAsync(runtime, options));
        Assert.Contains("初始化超时", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeAsync_WhenResumeLatestFails_FallsBackToStartThread()
    {
        var directory = CreateScriptDirectory();
        var executablePath = WritePowerShellServerExecutable(
            directory,
            "resume-fallback-server",
            """
            $hasProfileArg = $false
            for ($index = 0; $index -lt $args.Length - 1; $index++) {
              if ($args[$index] -eq '-c' -and $args[$index + 1] -eq 'profile=demo-profile') {
                $hasProfileArg = $true
                break
              }
            }
            $reader = [Console]::In
            while (($line = $reader.ReadLine()) -ne $null) {
              if ([string]::IsNullOrWhiteSpace($line)) {
                continue
              }

              $request = $line | ConvertFrom-Json
              $method = $request.method

              if ($method -eq 'initialize') {
                [Console]::Out.WriteLine("{""id"":$($request.id),""result"":{}}")
                continue
              }

              if ($method -eq 'initialized') {
                continue
              }

              if ($method -eq 'thread/list') {
                [Console]::Out.WriteLine("{""id"":$($request.id),""result"":{""data"":[{""id"":""thread-old-1""}]}}")
                continue
              }

              if ($method -eq 'thread/resume') {
                [Console]::Out.WriteLine("{""id"":$($request.id),""error"":{""message"":""resume failed""}}")
                continue
              }

              if ($method -eq 'thread/start') {
                [Console]::Out.WriteLine("{""id"":$($request.id),""result"":{""thread"":{""id"":""thread-new-1""}}}")
                continue
              }
            }
            """);

        await using var runtime = new TianShuExecutionRuntime();
        List<ControlPlaneConversationStreamEvent> events = [];
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        var options = new ExecutionRuntimeOptions
        {
            UseDotNetProjectLauncher = false,
            ExecutablePath = executablePath,
            WorkingDirectory = directory,
            StartupTimeout = TimeSpan.FromSeconds(3),
            ResumeLatestThread = true,
            ResumeLatestMatchCwd = false,
        };

        await InitializeRuntimeAsync(runtime, options);

        Assert.Equal("thread-new-1", runtime.ActiveThreadId);
        Assert.Contains(events, static streamEvent =>
            streamEvent.Kind == ControlPlaneConversationStreamEventKind.Info
            && string.Equals(streamEvent.Message, "恢复最近会话失败，回退到新会话。threadId=thread-old-1", StringComparison.Ordinal));
        Assert.Contains(events, static streamEvent =>
            streamEvent.Kind == ControlPlaneConversationStreamEventKind.Info
            && string.Equals(streamEvent.Message, "已建立线程：thread-new-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InitializeAsync_WhenCreateThreadOnInitializeDisabled_DoesNotStartThread()
    {
        var directory = CreateScriptDirectory();
        var executablePath = WritePowerShellServerExecutable(
            directory,
            "no-auto-thread-server",
            """
            $reader = [Console]::In
            while (($line = $reader.ReadLine()) -ne $null) {
              if ([string]::IsNullOrWhiteSpace($line)) {
                continue
              }

              $request = $line | ConvertFrom-Json
              $method = $request.method

              if ($method -eq 'initialize') {
                [Console]::Out.WriteLine("{""id"":$($request.id),""result"":{}}")
                continue
              }

              if ($method -eq 'initialized') {
                continue
              }

              if ($method -eq 'thread/start') {
                [Console]::Out.WriteLine("{""id"":$($request.id),""error"":{""message"":""thread/start should not be called""}}")
                continue
              }
            }
            """);

        await using var runtime = new TianShuExecutionRuntime();
        List<ControlPlaneConversationStreamEvent> events = [];
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        var options = new ExecutionRuntimeOptions
        {
            UseDotNetProjectLauncher = false,
            ExecutablePath = executablePath,
            WorkingDirectory = directory,
            StartupTimeout = TimeSpan.FromSeconds(3),
            CreateThreadOnInitialize = false,
        };

        await InitializeRuntimeAsync(runtime, options);

        Assert.Null(runtime.ActiveThreadId);
        Assert.Contains(events, static streamEvent =>
            streamEvent.Kind == ControlPlaneConversationStreamEventKind.Info
            && string.Equals(streamEvent.Message, "未自动创建线程，等待首次发送。", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InitializeAsync_WhenResumeLatestFailsAndCreateThreadOnInitializeDisabled_StaysBlank()
    {
        var directory = CreateScriptDirectory();
        var executablePath = WritePowerShellServerExecutable(
            directory,
            "resume-no-auto-thread-server",
            """
            $reader = [Console]::In
            while (($line = $reader.ReadLine()) -ne $null) {
              if ([string]::IsNullOrWhiteSpace($line)) {
                continue
              }

              $request = $line | ConvertFrom-Json
              $method = $request.method

              if ($method -eq 'initialize') {
                [Console]::Out.WriteLine("{""id"":$($request.id),""result"":{}}")
                continue
              }

              if ($method -eq 'initialized') {
                continue
              }

              if ($method -eq 'thread/list') {
                [Console]::Out.WriteLine("{""id"":$($request.id),""result"":{""data"":[{""id"":""thread-old-2""}]}}")
                continue
              }

              if ($method -eq 'thread/resume') {
                [Console]::Out.WriteLine("{""id"":$($request.id),""error"":{""message"":""resume failed""}}")
                continue
              }

              if ($method -eq 'thread/start') {
                [Console]::Out.WriteLine("{""id"":$($request.id),""error"":{""message"":""thread/start should not be called""}}")
                continue
              }
            }
            """);

        await using var runtime = new TianShuExecutionRuntime();
        List<ControlPlaneConversationStreamEvent> events = [];
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        var options = new ExecutionRuntimeOptions
        {
            UseDotNetProjectLauncher = false,
            ExecutablePath = executablePath,
            WorkingDirectory = directory,
            StartupTimeout = TimeSpan.FromSeconds(3),
            ResumeLatestThread = true,
            ResumeLatestMatchCwd = false,
            CreateThreadOnInitialize = false,
        };

        await InitializeRuntimeAsync(runtime, options);

        Assert.Null(runtime.ActiveThreadId);
        Assert.Contains(events, static streamEvent =>
            streamEvent.Kind == ControlPlaneConversationStreamEventKind.Info
            && string.Equals(streamEvent.Message, "恢复最近会话失败，不自动创建新会话。threadId=thread-old-2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InitializeAsync_WhenThreadListReadFails_EmitsInfoAndFallsBackToStartThread()
    {
        var directory = CreateScriptDirectory();
        var executablePath = WritePowerShellServerExecutable(
            directory,
            "thread-list-failure-server",
            """
            $reader = [Console]::In
            while (($line = $reader.ReadLine()) -ne $null) {
              if ([string]::IsNullOrWhiteSpace($line)) {
                continue
              }

              $request = $line | ConvertFrom-Json
              $method = $request.method

              if ($method -eq 'initialize') {
                [Console]::Out.WriteLine("{""id"":$($request.id),""result"":{}}")
                continue
              }

              if ($method -eq 'initialized') {
                continue
              }

              if ($method -eq 'thread/list') {
                [Console]::Out.WriteLine("{""id"":$($request.id),""error"":{""message"":""list failed""}}")
                continue
              }

              if ($method -eq 'thread/start') {
                [Console]::Out.WriteLine("{""id"":$($request.id),""result"":{""thread"":{""id"":""thread-list-fallback-1""}}}")
                continue
              }
            }
            """);

        await using var runtime = new TianShuExecutionRuntime();
        List<ControlPlaneConversationStreamEvent> events = [];
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        var options = new ExecutionRuntimeOptions
        {
            UseDotNetProjectLauncher = false,
            ExecutablePath = executablePath,
            WorkingDirectory = directory,
            StartupTimeout = TimeSpan.FromSeconds(3),
            ResumeLatestThread = true,
            ResumeLatestMatchCwd = false,
        };

        await InitializeRuntimeAsync(runtime, options);

        Assert.Equal("thread-list-fallback-1", runtime.ActiveThreadId);
        Assert.Contains(events, static streamEvent =>
            streamEvent.Kind == ControlPlaneConversationStreamEventKind.Info
            && string.Equals(streamEvent.Message, "读取会话列表失败：app-server 返回错误：list failed", StringComparison.Ordinal));
        Assert.Contains(events, static streamEvent =>
            streamEvent.Kind == ControlPlaneConversationStreamEventKind.Info
            && string.Equals(streamEvent.Message, "已建立线程：thread-list-fallback-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InitializeAsync_WhenIsolatedSessionStorageEnabled_EmitsInfoAndCreatesSqliteHome()
    {
        var directory = CreateScriptDirectory();
        var executablePath = WritePowerShellServerExecutable(
            directory,
            "isolated-storage-server",
            """
            $reader = [Console]::In
            while (($line = $reader.ReadLine()) -ne $null) {
              if ([string]::IsNullOrWhiteSpace($line)) {
                continue
              }

              $request = $line | ConvertFrom-Json
              $method = $request.method

              if ($method -eq 'initialize') {
                [Console]::Out.WriteLine("{""id"":$($request.id),""result"":{}}")
                continue
              }

              if ($method -eq 'initialized') {
                continue
              }

              if ($method -eq 'thread/start') {
                if ([string]::IsNullOrWhiteSpace($env:TIANSHU_SQLITE_HOME) -or -not (Test-Path $env:TIANSHU_SQLITE_HOME)) {
                  [Console]::Out.WriteLine("{""id"":$($request.id),""error"":{""message"":""missing sqlite home""}}")
                }
                else {
                  [Console]::Out.WriteLine("{""id"":$($request.id),""result"":{""thread"":{""id"":""thread-isolated-1""}}}")
                }
                continue
              }
            }
            """);

        var isolatedRoot = Path.Combine(directory, "isolated-root");

        await using var runtime = new TianShuExecutionRuntime();
        List<ControlPlaneConversationStreamEvent> events = [];
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        var options = new ExecutionRuntimeOptions
        {
            UseDotNetProjectLauncher = false,
            ExecutablePath = executablePath,
            WorkingDirectory = directory,
            StartupTimeout = TimeSpan.FromSeconds(3),
            UseIsolatedSessionStorage = true,
            IsolatedSessionStorageRoot = isolatedRoot,
        };

        await InitializeRuntimeAsync(runtime, options);

        Assert.Equal("thread-isolated-1", runtime.ActiveThreadId);
        Assert.Contains(events, streamEvent =>
            streamEvent.Kind == ControlPlaneConversationStreamEventKind.Info
            && string.Equals(streamEvent.Message, $"已启用隔离会话存储：{isolatedRoot}", StringComparison.Ordinal));
        Assert.True(Directory.Exists(Path.Combine(isolatedRoot, "sqlite")));
    }

    [Fact]
    public async Task InitializeAsync_WhenResumeLatestThreadConfigured_DoesNotSendLegacyProviderTransportFieldsOnThreadResume()
    {
        var directory = CreateScriptDirectory();
        var executablePath = WritePowerShellServerExecutable(
            directory,
            "thread-resume-provider-env-server",
            """
            $reader = [Console]::In
            while (($line = $reader.ReadLine()) -ne $null) {
              if ([string]::IsNullOrWhiteSpace($line)) {
                continue
              }

              $request = $line | ConvertFrom-Json
              $method = $request.method

              if ($method -eq 'initialize') {
                [Console]::Out.WriteLine("{""id"":$($request.id),""result"":{}}")
                continue
              }

              if ($method -eq 'initialized') {
                continue
              }

              if ($method -eq 'thread/list') {
                [Console]::Out.WriteLine("{""id"":$($request.id),""result"":{""data"":[{""id"":""thread-old-provider-1""}]}}")
                continue
              }

              if ($method -eq 'thread/resume') {
                if ($request.params.PSObject.Properties.Name -contains 'providerApiKeyEnvironmentVariable' `
                  -or $request.params.PSObject.Properties.Name -contains 'providerBaseUrl' `
                  -or $request.params.PSObject.Properties.Name -contains 'providerWireApi' `
                  -or $request.params.PSObject.Properties.Name -contains 'providerRequestMaxRetries' `
                  -or $request.params.PSObject.Properties.Name -contains 'providerStreamMaxRetries' `
                  -or $request.params.PSObject.Properties.Name -contains 'providerStreamIdleTimeoutMs' `
                  -or $request.params.PSObject.Properties.Name -contains 'providerWebsocketConnectTimeoutMs' `
                  -or $request.params.PSObject.Properties.Name -contains 'providerSupportsWebsockets') {
                  [Console]::Out.WriteLine("{""id"":$($request.id),""error"":{""message"":""unexpected legacy provider transport field""}}")
                }
                else {
                  [Console]::Out.WriteLine("{""id"":$($request.id),""result"":{""thread"":{""id"":""thread-old-provider-1""}}}")
                }
                continue
              }
            }
            """);

        await using var runtime = new TianShuExecutionRuntime();
        var options = new ExecutionRuntimeOptions
        {
            UseDotNetProjectLauncher = false,
            ExecutablePath = executablePath,
            WorkingDirectory = directory,
            StartupTimeout = TimeSpan.FromSeconds(3),
            ResumeLatestThread = true,
            ResumeLatestMatchCwd = false,
            ProviderApiKeyEnvironmentVariable = "ANTHROPIC_API_KEY",
        };

        await InitializeRuntimeAsync(runtime, options);

        Assert.Equal("thread-old-provider-1", runtime.ActiveThreadId);
    }

    [Fact]
    public async Task InitializeAsync_WhenWebSearchModeConfigured_DoesNotSendLegacyWebSearchModeOnThreadStart()
    {
        var directory = CreateScriptDirectory();
        var executablePath = WritePowerShellServerExecutable(
            directory,
            "thread-start-web-search-server",
            """
            $reader = [Console]::In
            while (($line = $reader.ReadLine()) -ne $null) {
              if ([string]::IsNullOrWhiteSpace($line)) {
                continue
              }

              $request = $line | ConvertFrom-Json
              $method = $request.method

              if ($method -eq 'initialize') {
                [Console]::Out.WriteLine("{""id"":$($request.id),""result"":{}}")
                continue
              }

              if ($method -eq 'initialized') {
                continue
              }

              if ($method -eq 'thread/start') {
                if ($request.params.PSObject.Properties.Name -contains 'webSearchMode') {
                  [Console]::Out.WriteLine("{""id"":$($request.id),""error"":{""message"":""unexpected webSearchMode""}}")
                }
                else {
                  [Console]::Out.WriteLine("{""id"":$($request.id),""result"":{""thread"":{""id"":""thread-start-1""}}}")
                }
                continue
              }
            }
            """);

        await using var runtime = new TianShuExecutionRuntime();
        var options = new ExecutionRuntimeOptions
        {
            UseDotNetProjectLauncher = false,
            ExecutablePath = executablePath,
            WorkingDirectory = directory,
            StartupTimeout = TimeSpan.FromSeconds(3),
            WebSearchMode = "disabled",
        };

        await InitializeRuntimeAsync(runtime, options);

        Assert.Equal("thread-start-1", runtime.ActiveThreadId);
    }
    [Fact]
    public async Task InitializeAsync_WhenWebSearchModeConfigured_DoesNotSendLegacyWebSearchModeOnThreadResume()
    {
        var directory = CreateScriptDirectory();
        var executablePath = WritePowerShellServerExecutable(
            directory,
            "thread-resume-web-search-server",
            """
            $reader = [Console]::In
            while (($line = $reader.ReadLine()) -ne $null) {
              if ([string]::IsNullOrWhiteSpace($line)) {
                continue
              }

              $request = $line | ConvertFrom-Json
              $method = $request.method

              if ($method -eq 'initialize') {
                [Console]::Out.WriteLine("{""id"":$($request.id),""result"":{}}")
                continue
              }

              if ($method -eq 'initialized') {
                continue
              }

              if ($method -eq 'thread/resume') {
                if ($request.params.PSObject.Properties.Name -contains 'webSearchMode') {
                  [Console]::Out.WriteLine("{""id"":$($request.id),""error"":{""message"":""unexpected webSearchMode""}}")
                }
                else {
                  [Console]::Out.WriteLine("{""id"":$($request.id),""result"":{""thread"":{""id"":""thread-resume-1""}}}")
                }
                continue
              }
            }
            """);

        await using var runtime = new TianShuExecutionRuntime();
        var options = new ExecutionRuntimeOptions
        {
            UseDotNetProjectLauncher = false,
            ExecutablePath = executablePath,
            WorkingDirectory = directory,
            StartupTimeout = TimeSpan.FromSeconds(3),
            ResumeThreadId = "thread-resume-1",
            WebSearchMode = "disabled",
        };

        await InitializeRuntimeAsync(runtime, options);

        Assert.Equal("thread-resume-1", runtime.ActiveThreadId);
    }

    [Fact]
    public async Task InitializeAsync_WhenProfileNameConfigured_PassesProfileCliArgumentToKernelProcess()
    {
        var directory = CreateScriptDirectory();
        var executablePath = WritePowerShellServerExecutable(
            directory,
            "thread-start-profile-env-server",
            """
            $hasProfileArg = $false
            for ($index = 0; $index -lt $args.Length - 1; $index++) {
              if ($args[$index] -eq '-c' -and $args[$index + 1] -eq 'profile=demo-profile') {
                $hasProfileArg = $true
                break
              }
            }
            $reader = [Console]::In
            while (($line = $reader.ReadLine()) -ne $null) {
              if ([string]::IsNullOrWhiteSpace($line)) {
                continue
              }

              $request = $line | ConvertFrom-Json
              $method = $request.method

              if ($method -eq 'initialize') {
                [Console]::Out.WriteLine("{""id"":$($request.id),""result"":{}}")
                continue
              }

              if ($method -eq 'initialized') {
                continue
              }

              if ($method -eq 'thread/start') {
                if (-not $hasProfileArg) {
                  [Console]::Out.WriteLine("{""id"":$($request.id),""error"":{""message"":""missing profile arg""}}")
                }
                else {
                  [Console]::Out.WriteLine("{""id"":$($request.id),""result"":{""thread"":{""id"":""thread-profile-1""}}}")
                }
                continue
              }
            }
            """);

        await using var runtime = new TianShuExecutionRuntime();
        var options = new ExecutionRuntimeOptions
        {
            UseDotNetProjectLauncher = false,
            ExecutablePath = executablePath,
            WorkingDirectory = directory,
            StartupTimeout = TimeSpan.FromSeconds(3),
            ProfileName = "demo-profile",
        };

        await InitializeRuntimeAsync(runtime, options);

        Assert.Equal("thread-profile-1", runtime.ActiveThreadId);
    }

    [Fact]
    public async Task ResumeThreadAsync_WhenSameTurnAssistantPrecedesUser_PreservesTypedTurnOrderWithoutHydratingLegacyMessages()
    {
        var directory = CreateScriptDirectory();
        var executablePath = WritePowerShellServerExecutable(
            directory,
            "thread-resume-order-server",
            """
            $reader = [Console]::In
            while (($line = $reader.ReadLine()) -ne $null) {
              if ([string]::IsNullOrWhiteSpace($line)) {
                continue
              }

              $request = $line | ConvertFrom-Json
              $method = $request.method

              if ($method -eq 'initialize') {
                [Console]::Out.WriteLine("{""id"":$($request.id),""result"":{}}")
                continue
              }

              if ($method -eq 'initialized') {
                continue
              }

              if ($method -eq 'thread/start') {
                [Console]::Out.WriteLine("{""id"":$($request.id),""result"":{""thread"":{""id"":""thread-bootstrap-1""}}}")
                continue
              }

              if ($method -eq 'thread/resume') {
                [Console]::Out.WriteLine("{""id"":$($request.id),""result"":{""thread"":{""id"":""thread-order-1"",""preview"":""恢复线程"",""turns"":[{""items"":[{""type"":""assistant_message"",""text"":""先出现的回答""},{""type"":""user_message"",""text"":""实际提问""}]}]}}}")
                continue
              }
            }
            """);

        await using var runtime = new TianShuExecutionRuntime();
        var options = new ExecutionRuntimeOptions
        {
            UseDotNetProjectLauncher = false,
            ExecutablePath = executablePath,
            WorkingDirectory = directory,
            StartupTimeout = TimeSpan.FromSeconds(3),
        };

        await InitializeRuntimeAsync(runtime, options);
        var result = await runtime.ResumeThreadAsync("thread-order-1", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("thread-order-1", result!.Thread.ThreadId.Value);
        var turn = Assert.Single(result.Turns);
        Assert.Collection(
            turn.Items,
            item =>
            {
                Assert.Equal("assistant_message", item.Type);
                Assert.Equal("先出现的回答", item.Text);
            },
            item =>
            {
                Assert.Equal("user_message", item.Type);
                var userContent = item.Data;
                Assert.NotNull(userContent);
                var contentItems = userContent!.Properties["content"];
                Assert.NotNull(contentItems);
                var userText = Assert.Single(contentItems.Items);
                Assert.Equal(StructuredValueKind.Object, userText.Kind);
                Assert.Equal("text", userText.Properties["type"].StringValue);
                Assert.Equal("实际提问", userText.Properties["text"].StringValue);
            });
        Assert.Collection(
            result.Messages,
            message =>
            {
                Assert.Equal(ControlPlaneConversationRole.User, message.Role);
                Assert.Equal("实际提问", message.Content);
            },
            message =>
            {
                Assert.Equal(ControlPlaneConversationRole.Assistant, message.Role);
                Assert.Equal("先出现的回答", message.Content);
            });
    }

    [Fact]
    public void BuildArguments_WhenCustomConfigFilePathAndNotDefault_IncludesConfigFileArgument()
    {
        var customPath = Path.Combine(AppContext.BaseDirectory, "custom-config.toml");
        var options = new ExecutionRuntimeOptions
        {
            UseDotNetProjectLauncher = false,
            ConfigFilePath = customPath,
        };

        var args = BuildArgumentsForOptions(options);

        Assert.Contains("--config-file", args);
        Assert.Contains("custom-config.toml", args);
    }

    [Fact]
    public void BuildArguments_WhenDefaultConfigFilePath_SkipsConfigFileArgument()
    {
        var options = new ExecutionRuntimeOptions
        {
            UseDotNetProjectLauncher = false,
            ConfigFilePath = TianShuTomlConfigurationLoader.ResolveDefaultPath(),
        };

        var args = BuildArgumentsForOptions(options);

        Assert.DoesNotContain("--config-file", args);
    }

    [Fact]
    public void BuildArguments_WhenConfigOverridesConfigured_IncludesCliOverrideArguments()
    {
        var options = new ExecutionRuntimeOptions
        {
            UseDotNetProjectLauncher = false,
            ProfileName = "work",
            ConfigOverrides = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["model"] = "gpt-5-mini",
                ["web_search"] = "live",
                ["profile"] = "ignored-by-profile-name",
            },
        };

        var args = BuildArgumentsForOptions(options);

        Assert.Contains("-c model=gpt-5-mini", args, StringComparison.Ordinal);
        Assert.Contains("-c web_search=live", args, StringComparison.Ordinal);
        Assert.Contains("-c profile=work", args, StringComparison.Ordinal);
        Assert.DoesNotContain("ignored-by-profile-name", args, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildArguments_WhenProfileNameComesFromResolvedConfig_SkipsProfileCliOverride()
    {
        var options = new ExecutionRuntimeOptions
        {
            UseDotNetProjectLauncher = false,
            ProfileName = "work",
            ProfileNameResolvedFromConfig = true,
            ConfigOverrides = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["model"] = "gpt-5-mini",
            },
        };

        var args = BuildArgumentsForOptions(options);

        Assert.Contains("-c model=gpt-5-mini", args, StringComparison.Ordinal);
        Assert.DoesNotContain("-c profile=work", args, StringComparison.Ordinal);
    }

    private static string BuildArgumentsForOptions(ExecutionRuntimeOptions options)
    {
        var runtime = new TianShuExecutionRuntime();
        var method = typeof(TianShuExecutionRuntime)
            .GetMethod("BuildArguments", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("BuildArguments method missing.");
        return (string)method.Invoke(runtime, new object[] { options })!;
    }

    private static Task InitializeRuntimeAsync(TianShuExecutionRuntime runtime, ExecutionRuntimeOptions options)
        => runtime.InitializeAsync(
            ToInitializeCommand(options),
            options.DynamicToolCallHandler,
            CancellationToken.None);

    private static ControlPlaneInitializeRuntimeCommand ToInitializeCommand(ExecutionRuntimeOptions options)
        => new()
        {
            UseDotNetProjectLauncher = options.UseDotNetProjectLauncher,
            ExecutablePath = options.ExecutablePath,
            AppHostProjectPath = options.AppHostProjectPath,
            WorkingDirectory = options.WorkingDirectory,
            AdditionalArguments = options.AdditionalArguments,
            ConfigFilePath = options.ConfigFilePath,
            ProfileName = options.ProfileName,
            ProfileNameResolvedFromConfig = options.ProfileNameResolvedFromConfig,
            ConfigOverrides = options.ConfigOverrides,
            Model = options.Model,
            ModelProvider = options.ModelProvider,
            ApprovalPolicy = options.ApprovalPolicy?.ToString(),
            SandboxMode = options.SandboxMode,
            WebSearchMode = options.WebSearchMode,
            ServiceTier = options.ServiceTier.IsSpecified ? options.ServiceTier.ToString() : null,
            ModelReasoningSummary = options.ModelReasoningSummary,
            ModelVerbosity = options.ModelVerbosity,
            CollaborationMode = options.CollaborationMode,
            SessionSource = options.SessionSource,
            ProviderBaseUrl = options.ProviderBaseUrl,
            ProviderApiKeyEnvironmentVariable = options.ProviderApiKeyEnvironmentVariable,
            ProviderWireApi = options.ProviderWireApi,
            ProviderRequestMaxRetries = options.ProviderRequestMaxRetries,
            ProviderStreamMaxRetries = options.ProviderStreamMaxRetries,
            ProviderStreamIdleTimeoutMs = options.ProviderStreamIdleTimeoutMs,
            ProviderWebsocketConnectTimeoutMs = options.ProviderWebsocketConnectTimeoutMs,
            ProviderSupportsWebsockets = options.ProviderSupportsWebsockets,
            ProtocolAdapter = options.ProtocolAdapter,
            ResumeThreadId = options.ResumeThreadId,
            ResumeLatestThread = options.ResumeLatestThread,
            ResumeLatestMatchCwd = options.ResumeLatestMatchCwd,
            ResumeThreadListLimit = options.ResumeThreadListLimit,
            CreateThreadOnInitialize = options.CreateThreadOnInitialize,
            UseIsolatedSessionStorage = options.UseIsolatedSessionStorage,
            IsolatedSessionStorageRoot = options.IsolatedSessionStorageRoot,
            StartupTimeout = options.StartupTimeout,
            RequestTimeout = options.RequestTimeout,
            TurnTimeout = options.TurnTimeout,
            DynamicTools = options.DynamicTools,
            OutputSchema = options.OutputSchema,
        };

    private static string CreateScriptDirectory()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "runtime-init-scripts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string WriteBatchExecutable(string directory, string fileName, string content)
    {
        var path = Path.Combine(directory, $"{fileName}.cmd");
        File.WriteAllText(path, content.Replace("\\r\\n", Environment.NewLine), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static string WritePowerShellServerExecutable(string directory, string fileName, string scriptContent)
    {
        var scriptPath = Path.Combine(directory, $"{fileName}.ps1");
        File.WriteAllText(scriptPath, scriptContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var wrapperPath = Path.Combine(directory, $"{fileName}.cmd");
        var wrapperContent = $"@echo off\r\npowershell.exe -NoProfile -ExecutionPolicy Bypass -File \"%~dp0{fileName}.ps1\" %*\r\n";
        File.WriteAllText(wrapperPath, wrapperContent.Replace("\r\n", Environment.NewLine), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return wrapperPath;
    }
}
